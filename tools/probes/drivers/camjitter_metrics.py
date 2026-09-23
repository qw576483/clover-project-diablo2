#!/usr/bin/env python3
"""camjitter_metrics.py -- the MEASURING SCRIPT for the camera-follow jitter rows.

Input  : a TSV frozen by tools/probes/drivers/camjitter_drive.cs (one row per rendered frame):
             frame \t dt \t px \t py \t cx \t cy
         px/py = IPlayerModule.World (the follow focus), cx/cy = Camera.main.position.
Output : the three quantities the task names, computed only from those columns.

Definitions (stated here so the numbers cannot be re-interpreted after the fact):
  r_i   = (px-cx, py-cy)                       relative offset, player minus camera
  dp_i  = p_i - p_{i-1}                        player step of frame i
  u_i   = dp_i / |dp_i|                        the frame's travel direction
  perp  = (-u.y, u.x)                          the frame's LATERAL direction
  dr_i  = r_i - r_{i-1}                        first difference of the relative offset
  A_i   = dr_i . perp(u_i)                     the LATERAL component of that first difference
  B     = max_i |dr_i - dr_{i-1}|              the largest single-frame jump OF that first difference
  C     = mean / stdev / max of dt
  lat_i = r_i . perp(u_i)                      lateral offset; swing = max(lat) - min(lat)
  lag_i = |r_i|                                steady-state follow lag

Only frames where the player actually moved (|dp| > eps) enter A / B / lat / lag: on an idle frame
the direction u is undefined, and the turn-around frames of a leg boundary are transients.

usage: python camjitter_metrics.py <tsv> [--label before]
"""
import argparse
import math
import statistics
import sys


def read_tsv(path):
    rows = []
    with open(path, "r", encoding="utf-8") as f:
        header = None
        for line in f:
            line = line.strip()
            if not line:
                continue
            parts = line.split("\t")
            if header is None:
                header = parts
                if header[0] != "frame":
                    raise SystemExit("unexpected header: %r" % (header,))
                continue
            if len(parts) < 6:
                continue
            try:
                rows.append(tuple(float(x) for x in parts[:6]))
            except ValueError:
                continue
    if len(rows) < 10:
        raise SystemExit("too few rows (%d) -- the capture did not run" % len(rows))
    return rows


def pct(vals, q):
    if not vals:
        return float("nan")
    s = sorted(vals)
    if len(s) == 1:
        return s[0]
    pos = q * (len(s) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    if lo == hi:
        return s[lo]
    return s[lo] + (s[hi] - s[lo]) * (pos - lo)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv")
    ap.add_argument("--label", default="")
    ap.add_argument("--eps", type=float, default=1e-9)
    ap.add_argument("--quiet", action="store_true")
    a = ap.parse_args()

    rows = read_tsv(a.tsv)
    rows.sort(key=lambda r: r[0])

    dts = [r[1] for r in rows if r[1] > 0.0]
    dts_all = [r[1] for r in rows]

    # per-frame geometry (index 1 = previous row)
    a_vals = []          # A: lateral component of the relative-offset first difference
    b_vals = []          # B: |dr_i - dr_{i-1}|
    lat_vals = []        # lateral offset (for the swing)
    lag_vals = []        # |r|
    dr_prev = None
    moved = 0
    for i in range(1, len(rows)):
        _, _, px0, py0, cx0, cy0 = rows[i - 1]
        _, _, px1, py1, cx1, cy1 = rows[i]
        dpx, dpy = px1 - px0, py1 - py0
        step = math.hypot(dpx, dpy)
        rx, ry = px1 - cx1, py1 - cy1
        if step <= a.eps:
            dr_prev = None            # the direction is undefined: this frame is not in the sample
            continue
        moved += 1
        ux, uy = dpx / step, dpy / step
        px_, py_ = -uy, ux            # lateral direction
        rgx, rgy = px0 - cx0, py0 - cy0
        dr = (rx - rgx, ry - rgy)
        a_vals.append(abs(dr[0] * px_ + dr[1] * py_))
        if dr_prev is not None:
            b_vals.append(math.hypot(dr[0] - dr_prev[0], dr[1] - dr_prev[1]))
        dr_prev = dr
        lat_vals.append(rx * px_ + ry * py_)
        lag_vals.append(math.hypot(rx, ry))

    label = (" " + a.label) if a.label else ""
    out = []
    out.append("== camjitter metrics%s ==" % label)
    out.append("tsv=%s" % a.tsv)
    out.append("frames=%d  movingFrames=%d  span=%.3fs" % (len(rows), moved, sum(dts_all)))
    out.append("C dt: mean=%.6f  stdev=%.6f  max=%.6f  p95=%.6f  (n=%d)"
               % (statistics.fmean(dts), statistics.pstdev(dts), max(dts), pct(dts, 0.95), len(dts)))
    out.append("A lateral |d r|: p50=%.6f  p95=%.6f  max=%.6f  (cells/frame)"
               % (pct(a_vals, 0.50), pct(a_vals, 0.95), max(a_vals)))
    out.append("B max single-frame jump of d r: max=%.6f  p95=%.6f  (cells)"
               % (max(b_vals), pct(b_vals, 0.95)))
    out.append("lat offset swing (max-min, moving frames)=%.6f cells  p95|lat|=%.6f"
               % (max(lat_vals) - min(lat_vals), pct([abs(v) for v in lat_vals], 0.95)))
    out.append("follow lag |r|: p50=%.6f  p95=%.6f  max=%.6f cells"
               % (pct(lag_vals, 0.50), pct(lag_vals, 0.95), max(lag_vals)))
    text = "\n".join(out)
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
