#!/usr/bin/env python3
"""U27 (S1): characterise the dt SPIKES in a cam-jitter trace -- offline, no Play.

Context (measured, report-S1 11.1): `dt.max` is 75~88 ms while `dt.mean` is ~18 ms, and
`get_performance_stats.frameTiming` says `cpuMainThreadFrameTimeMs=2.83` << `cpuFrameTimeMs=17.73`
=> the main thread is NOT saturated, so the spikes are presentation/waiting (or a stall), not
compute. This script narrows it further from the frozen TSV alone:

  * dt quantiles + how much of the total time the spikes own;
  * WHERE the spikes are in time (a start-cluster => asset/area loading; spread over the whole
    run => recurring stall like GC);
  * spike TRAIN structure: gaps between consecutive spikes (single narrow mode => periodic,
    GC-like; broad => event-driven/irregular);
  * catch-up: dt[i]+dt[i+1] vs 2*median (a real stall costs time; a present/vsync hiccup often
    shows the next frame being short).

LIMITATION (stated so the numbers cannot be over-read): periodicity is SUGGESTIVE, not proof.
Attribution to GC vs loading needs the per-frame `GC.CollectionCount` / `GC.GetTotalMemory`
columns, which the driver can now emit (spec field `gc`, see camjitter_run.ps1 -Gc) -- one more
Play capture is required for that; this script never claims the cause by itself.

usage: python tools/probes/measure/spike_probe.py <cam-jitter-*.tsv> [more.tsv ...]
"""
import math
import sys


def read_dt(path):
    dts = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("frame"):
                continue
            parts = line.split("\t")
            if len(parts) < 2:
                continue
            try:
                dts.append(float(parts[1]))
            except ValueError:
                pass
    return dts


def pct(vals, q):
    if not vals:
        return float("nan")
    s = sorted(vals)
    if len(s) == 1:
        return s[0]
    pos = q * (len(s) - 1)
    lo, hi = int(math.floor(pos)), int(math.ceil(pos))
    if lo == hi:
        return s[lo]
    return s[lo] + (s[hi] - s[lo]) * (pos - lo)


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    for path in argv[1:]:
        dts = read_dt(path)
        if len(dts) < 20:
            print("%s: too few rows (%d)" % (path, len(dts)))
            continue
        n = len(dts)
        med = pct(dts, 0.5)
        p90, p99 = pct(dts, 0.90), pct(dts, 0.99)
        mx = max(dts)
        thr = max(2.5 * med, p99)
        spikes = [i for i, v in enumerate(dts) if v >= thr]
        span = sum(dts)
        spent = sum(dts[i] for i in spikes)
        print("== %s" % path)
        print("   frames=%d  span=%.3fs  dt p50=%.4f p90=%.4f p99=%.4f max=%.4f  (spike thr=%.4f)"
              % (n, span, med, p90, p99, mx, thr))
        print("   spikes=%d (%.2f%% of frames)  %.1f%% of total time in spikes"
              % (len(spikes), 100.0 * len(spikes) / n, 100.0 * spent / span if span else 0))
        if not spikes:
            continue
        # where in the run?
        thirds = [0, 0, 0]
        for i in spikes:
            thirds[min(2, i * 3 // n)] += 1
        print("   position: first/middle/last third = %d/%d/%d" % tuple(thirds))
        # catch-up
        short_next = sum(1 for i in spikes if i + 1 < n and dts[i + 1] < dts[i] / 2.5
                         and dts[i] + dts[i + 1] < 2 * med * 1.5)
        print("   catch-up: %d/%d spikes are followed by a much shorter frame (no real cost => "
              "present/vsync hiccup rather than a stall)" % (short_next, len(spikes)))
        # spike train gaps
        gaps = [spikes[i] - spikes[i - 1] for i in range(1, len(spikes))]
        if gaps:
            import collections
            h = collections.Counter(gaps)
            top = h.most_common(5)
            print("   gaps(frames) between spikes: n=%d  min=%d p50=%.0f max=%d  modes=%s"
                  % (len(gaps), min(gaps), pct(gaps, 0.5), max(gaps), top))
            narrow = len([g for g in gaps if g <= max(2, 0.2 * pct(gaps, 0.5))]) if gaps else 0
            print("   interpretation aid: a single narrow mode ~= periodic (GC-like); a broad "
                  "spread ~= irregular (loading/IO/other).  narrow<=20%% of p50: %d/%d"
                  % (narrow, len(gaps)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
