#!/usr/bin/env python3
"""camverify_lag.py -- the px-class rows of the cam-verify table, from a frozen TSV.

Input : a TSV frozen by tools/probes/drivers/camjitter_drive.cs
        (frame \t dt \t px \t py \t cx \t cy ...), px/py = IPlayerModule.World,
        cx/cy = Camera.main.position.

Definitions (identical to the ones used elsewhere, so nothing is re-interpreted):
  screen px offset of the player vs the camera = (p - c) * ppu,  ppu = screenH / (2*ortho)
      (for an orthographic camera with no rotation: vp = 0.5 + (p-c)/(2*half), and
       (vp-0.5) * screenW = (px-cx) * screenW/(2*ortho*aspect) = (px-cx) * ppu)
  dist_px = |p - c| * ppu                       -- the row report-playverify.md calls
                                                   "player <-> camera screen distance px"
  halfCell_px = 0.5 cell * ppu / cellsPerWorldUnit ... here 1 cell = 2 world units of
      (gx+gy) span; the frozen convention of the previous piece is 1 cell = 144 px
      @1080p/ortho 3.75, so halfCell = 72 px.
  inViewport (playercheck 11.11) = |px-cx| <= halfW_world and |py-cy| <= halfH_world
      with halfW_world = ortho*aspect, halfH_world = ortho  -- i.e. vp in [0,1] on both axes.

usage: python camverify_lag.py <tsv> [--label after] [--screen-h 1080] [--ortho 3.75] [--aspect 1.77778]
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
    ap.add_argument("--screen-h", type=float, default=1080.0)
    ap.add_argument("--ortho", type=float, default=3.75)
    ap.add_argument("--aspect", type=float, default=16.0 / 9.0)
    ap.add_argument("--eps", type=float, default=1e-9)
    a = ap.parse_args()

    rows = read_tsv(a.tsv)
    rows.sort(key=lambda r: r[0])
    ppu = a.screen_h / (2.0 * a.ortho)
    half_cell_px = ppu / 2.0                     # 0.5 cell
    halfW = a.ortho * a.aspect
    halfH = a.ortho

    dist_all, dist_moving, axis_moving = [], [], []
    over_all = over_moving = 0
    outvp_all = outvp_moving = 0
    moved = 0
    for i in range(1, len(rows)):
        _, _, px0, py0, cx0, cy0 = rows[i - 1]
        _, _, px1, py1, cx1, cy1 = rows[i]
        dx, dy = px1 - cx1, py1 - cy1
        d = math.hypot(dx, dy) * ppu
        dist_all.append(d)
        if d > half_cell_px:
            over_all += 1
        if abs(dx) > halfW or abs(dy) > halfH:
            outvp_all += 1
        step = math.hypot(px1 - px0, py1 - py0)
        if step <= a.eps:
            continue
        moved += 1
        dist_moving.append(d)
        axis_moving.append((abs(dx) * ppu, abs(dy) * ppu))
        if d > half_cell_px:
            over_moving += 1
        if abs(dx) > halfW or abs(dy) > halfH:
            outvp_moving += 1

    def p(vals, q):
        return pct(vals, q)

    label = (" " + a.label) if a.label else ""
    out = []
    out.append("== camverify lag%s ==" % label)
    out.append("tsv=%s" % a.tsv)
    out.append("frames=%d moving=%d span=%.3fs  pxPerUnit=%.1f  halfCell(72px ref)=%.1f"
               % (len(rows), moved, sum(r[1] for r in rows), ppu, half_cell_px))
    out.append("follow |r| px (moving frames)     : p50=%.1f  p95=%.1f  max=%.1f"
               % (p(dist_moving, .5), p(dist_moving, .95), max(dist_moving)))
    out.append("follow |r| px (all frames)        : p50=%.1f  p95=%.1f  max=%.1f"
               % (p(dist_all, .5), p(dist_all, .95), max(dist_all)))
    out.append("frames with |r| > %.0fpx (half cell): moving=%d/%d = %.1f%%   all=%d/%d = %.1f%%"
               % (half_cell_px, over_moving, moved, 100.0 * over_moving / max(moved, 1),
                  over_all, len(dist_all), 100.0 * over_all / max(len(dist_all), 1)))
    out.append("playercheck 11.11 out-of-viewport : moving=%d/%d = %.1f%%   all=%d/%d = %.1f%%"
               % (outvp_moving, moved, 100.0 * outvp_moving / max(moved, 1),
                  outvp_all, len(dist_all), 100.0 * outvp_all / max(len(dist_all), 1)))
    if axis_moving:
        xs = [v[0] for v in axis_moving]
        ys = [v[1] for v in axis_moving]
        out.append("axis |dx| px (moving)            : p50=%.1f  p95=%.1f  max=%.1f"
                   % (p(xs, .5), p(xs, .95), max(xs)))
        out.append("axis |dy| px (moving)            : p50=%.1f  p95=%.1f  max=%.1f"
                   % (p(ys, .5), p(ys, .95), max(ys)))
    print("\n".join(out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
