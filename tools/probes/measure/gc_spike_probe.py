#!/usr/bin/env python3
"""U27 (S1): attribute the dt spikes using the per-frame GC columns (spec field "gc").

Companion of `spike_probe.py` (which characterises the spikes without GC data). Requires a TSV
captured with `camjitter_run.ps1 -Gc`; its rows carry two extra columns after `spr`:
  gcc = GC.CollectionCount(0) delta this frame     (an EXACT counter, not a proxy)
  gcm = GC.GetTotalMemory(false) delta bytes       (a proxy for managed allocation)

Attribution rule, FIXED BEFORE the capture (report-S1 15.1):
  gcc > 0 in the spike window                        => GC
  gcc == 0, and the sprite/node column changes       => node/visual churn (see caveat)
  neither                                            => NOT JUDGED (say so; never force a class)

CAVEAT THAT IS PART OF THE RULE (learned from the first run): a small `gcm` bump (tens of KB) is
ordinary managed allocation - it is NOT evidence of "asset loading". So `gcm` alone must NOT be
reported as "loading"; this script therefore only reports the gcm numbers as context and keeps
the three buckets GC / node-churn / NOT-JUDGED.

usage: python tools/probes/measure/gc_spike_probe.py <cam-jitter-*.tsv>
"""
import math
import sys


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


def read(path):
    rows = []
    with open(path, "r", encoding="utf-8") as f:
        head = None
        for line in f:
            line = line.rstrip("\n")
            if not line:
                continue
            p = line.split("\t")
            if head is None:
                head = p
                if "gcc" not in head:
                    raise SystemExit("no gcc column in header -> capture with -Gc: %r" % head)
                continue
            if len(p) < 15:
                continue
            try:
                rows.append(dict(frame=int(p[0]), dt=float(p[1]), spr=p[12],
                                 gcc=int(float(p[13])), gcm=int(float(p[14]))))
            except ValueError:
                pass
    return rows


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    rows = read(argv[1])
    n = len(rows)
    dts = [r["dt"] for r in rows]
    med = pct(dts, 0.5)
    thr = max(2.5 * med, pct(dts, 0.99))
    spikes = [i for i, r in enumerate(rows) if r["dt"] >= thr]
    print("== %s" % argv[1])
    print("frames=%d  dt p50=%.4f p99=%.4f max=%.4f  spike thr=%.4f  spikes=%d (%.2f%%)  "
          "time-in-spikes=%.1f%%"
          % (n, med, pct(dts, 0.99), max(dts), thr, len(spikes), 100.0 * len(spikes) / n,
             100.0 * sum(dts[i] for i in spikes) / sum(dts) if dts else 0))
    gc_hits = churn_hits = neither = 0
    thirds = [0, 0, 0]
    for i in spikes:
        thirds[min(2, i * 3 // n)] += 1
        r = rows[i]
        win = rows[max(0, i - 2):min(n, i + 3)]
        gcc_sum = sum(x["gcc"] for x in win)
        gcm_max = max((x["gcm"] for x in win), default=0)
        gcm_min = min((x["gcm"] for x in win), default=0)
        sprites = sorted({x["spr"] for x in win})
        if gcc_sum > 0:
            gc_hits += 1
            cls = "GC"
        elif len(sprites) > 1:
            churn_hits += 1
            cls = "node-churn"
        else:
            neither += 1
            cls = "NOT-JUDGED"
        print("   frame=%5d dt=%.4f  gcc(win)=%d  gcm(win)=[%d .. %d] B  sprites=%s  => %s"
              % (r["frame"], r["dt"], gcc_sum, gcm_min, gcm_max, sprites, cls))
    # Is the sprite-change proxy informative at all? The sprite animates every ~2.4 frames, so a
    # +-2 window containing >1 sprite may be TRUE FOR EVERY FRAME -> the proxy then says nothing.
    base = sum(1 for i in range(n)
               if len({rows[j]["spr"] for j in range(max(0, i - 2), min(n, i + 3))}) > 1)
    rate = (100.0 * base / n) if n else 0.0
    print("proxy base rate: %.1f%% of ALL frames have >1 sprite in their +-2 window" % rate)
    if rate > 80.0:
        print("   => the sprite-change proxy is UNINFORMATIVE (it fires on almost every frame) "
              "=> `node-churn` must be reported as NOT-JUDGED too; loading stays UNPROVEN.")
    print("phase(first/middle/last third) = %d/%d/%d" % tuple(thirds))
    print("ATTRIBUTION: GC=%d  node-churn=%d%s  NOT-JUDGED=%d  (of %d spikes)"
          % (gc_hits, churn_hits, " [proxy uninformative]" if rate > 80.0 else "",
             neither, len(spikes)))
    if gc_hits == 0:
        print("   => GC is EXCLUDED for every spike (gcc is an exact counter).")
    if neither:
        print("   => the NOT-JUDGED ones stay NOT-JUDGED: a tens-of-KB gcm bump is ordinary "
              "allocation and is NOT proof of asset loading (rule, report-S1 15.1).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
