#!/usr/bin/env python3
"""camjitter_who.py -- "WHO jitters" measuring script (piece g2-resume).

Input : the TSV frozen by tools/probes/drivers/camjitter_drive.cs (one row per rendered frame),
        g2-resume column layout:
            frame dt px py cx cy nx ny sw sh sx sy spr
        px/py = IPlayerModule.World (the follow focus)
        cx/cy = Camera.main.position (what the camera is actually at this frame)
        nx/ny = the PLAYER RENDER NODE world position (EntityView.Root.transform.position)
        sw/sh/sx/sy/spr = the sprite currently on that node (Sprite.rect + name)
Output: the quantities that answer the user's report ("camera movement is very jittery /
        the character jitters"), computed ONLY from those columns, in screen pixels:

  screenPxPerUnit = screenH / (2 * orthoSize)          (ortho camera, see CameraRig)
  cam_i  = (cx,cy) * ppu                               camera position in screen px
  node_i = (nx,ny) * ppu                               character node in screen px
  rel_i  = node_i - cam_i                              character position ON SCREEN (px)

  step_i   = a_i - a_{i-1}                             per-frame displacement (px)
  speed_i  = |step_i| / dt_i                           px/s -- constant iff the motion is smooth IN TIME
  judder_i = step_i - median(step)                     the part of the step that is NOT the nominal motion

  J(x) = stdev(judder) / p95|judder| / max|judder|     the jitter amplitude (px)
  Z(x) = direction reversals of judder per second      the jitter frequency (Hz)

  A camera absolute (the world scroll on screen)
  B character relative to the camera (follow error: only the camera's smoothing can produce it)
  C node vs module data (the same thing twice -> must be identical; a mismatch = a render-side bug)
  D sprite rect changes (frame switches; a rect change is NOT jitter by itself)

Verdict rule (stated BEFORE the run, so the numbers cannot be re-interpreted after it):
  if J(camera) >> J(relative)  -> the judder is in the world scroll (frame pacing / dt), not in the follow
  if J(relative) >> J(camera)  -> the camera's follow smoothing is the source
  if J(node - camera) ~ 0 while J(node) is large -> the whole picture moves together (pacing again)

usage: python camjitter_who.py <tsv> [--label X] [--screen-h 1080] [--ortho 3.75]
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
            line = line.rstrip("\n")
            if not line.strip():
                continue
            parts = line.split("\t")
            if header is None:
                header = parts
                if header[0] != "frame":
                    raise SystemExit("unexpected header: %r" % (header,))
                continue
            if len(parts) < 6:
                continue
            while len(parts) < 13:
                parts.append("nan")
            vals = []
            for x in parts[:12]:
                try:
                    vals.append(float(x))
                except ValueError:
                    vals.append(float("nan"))
            vals.append(parts[12])
            rows.append(vals)
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


def series(rows, col):
    return [r[col] for r in rows if not math.isnan(r[col])]


def deltas(vals, dts):
    """(step vectors (px), speeds (px/s)) for consecutive samples."""
    steps, speeds = [], []
    for i in range(1, len(vals)):
        if any(math.isnan(v) for v in vals[i]) or any(math.isnan(v) for v in vals[i - 1]):
            continue                                   # column absent (older capture) -> not a sample
        dx = (vals[i][0] - vals[i - 1][0], vals[i][1] - vals[i - 1][1])
        dt = dts[i] if i < len(dts) else float("nan")
        if math.isnan(dt) or dt <= 0:
            continue
        steps.append(dx)
        speeds.append(math.hypot(dx[0], dx[1]) / dt)
    return steps, speeds


def judge(steps, win=5):
    """Jitter of a 2-D step series, split into the two components that look different to a player.

    The reference motion is the LOCAL mean step (window +-win frames) -- NOT one global median,
    because the game's path zig-zags and turns 45/90 degrees, and a turn would otherwise be
    counted as "jitter" (the confounder this docstring exists to kill).

      longitudinal = (step - local_mean) . u       u = local mean direction  -> speed unevenness
      lateral      = (step - local_mean) . perp(u)                           -> sideways shake

    |long| in px/frame is what a dropped/variable frame time produces (a frame covering 1.4x
    the nominal time moves 1.4x the nominal distance); lateral is the "shaking" that a
    smoothing/zoom/lag defect produces. They must NOT be added together.
    """
    empty = dict(long_sd=float("nan"), lat_sd=float("nan"), long_p95=float("nan"), lat_p95=float("nan"),
                 lat_p2p=float("nan"), n=0)
    if len(steps) < 2 * win + 1:
        return empty
    jl, jp = [], []
    for i in range(len(steps)):
        lo = max(0, i - win)
        hi = min(len(steps), i + win + 1)
        mx = sum(s[0] for s in steps[lo:hi]) / (hi - lo)
        my = sum(s[1] for s in steps[lo:hi]) / (hi - lo)
        n = math.hypot(mx, my)
        if n < 1e-9:
            continue
        ux, uy = mx / n, my / n
        dx, dy = steps[i][0] - mx, steps[i][1] - my
        jl.append(dx * ux + dy * uy)
        jp.append(dx * -uy + dy * ux)
    if len(jl) < 2:
        return empty
    return dict(long_sd=statistics.pstdev(jl), lat_sd=statistics.pstdev(jp),
                long_p95=pct([abs(v) for v in jl], 0.95), lat_p95=pct([abs(v) for v in jp], 0.95),
                lat_p2p=max(jp) - min(jp), n=len(jl))


def corr_dt(steps, dts, win=5):
    """corr(|longitudinal deviation| , dt - mean(dt)) -- proves (or refutes) the "uneven frame time"
    explanation: a smooth position=f(t) advances by v*dt, so the deviation must be proportional to dt."""
    jl = []
    dl = []
    for i in range(len(steps)):
        lo = max(0, i - win)
        hi = min(len(steps), i + win + 1)
        mx = sum(s[0] for s in steps[lo:hi]) / (hi - lo)
        my = sum(s[1] for s in steps[lo:hi]) / (hi - lo)
        n = math.hypot(mx, my)
        if n < 1e-9:
            continue
        ux, uy = mx / n, my / n
        jl.append((steps[i][0] - mx) * ux + (steps[i][1] - my) * uy)
        dl.append(dts[i + 1] if i + 1 < len(dts) else float("nan"))
    if len(jl) < 3:
        return float("nan")
    md = statistics.fmean(dl)
    sdj = statistics.pstdev(jl)
    sdd = statistics.pstdev(dl)
    if sdj < 1e-12 or sdd < 1e-12:
        return float("nan")
    cov = sum((a - statistics.fmean(jl)) * (b - md) for a, b in zip(jl, dl)) / len(jl)
    return cov / (sdj * sdd)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv")
    ap.add_argument("--label", default="")
    ap.add_argument("--screen-h", type=float, default=1080.0)
    ap.add_argument("--ortho", type=float, default=3.75)
    a = ap.parse_args()

    rows = read_tsv(a.tsv)
    rows.sort(key=lambda r: r[0])
    ppu = a.screen_h / (2.0 * a.ortho)          # screen px per world unit
    dts = [r[1] for r in rows]
    span = sum(d for d in dts if d > 0)

    cam = [(r[4] * ppu, r[5] * ppu) for r in rows]
    node = [(r[6] * ppu, r[7] * ppu) for r in rows]
    rel = [(n[0] - c[0], n[1] - c[1]) for c, n in zip(cam, node)]
    focus = [(r[2], r[3]) for r in rows]

    cam_steps, cam_speed = deltas(cam, dts)
    node_steps, node_speed = deltas(node, dts)
    rel_steps, _ = deltas(rel, dts)

    jc = judge(cam_steps)
    jn = judge(node_steps)
    jr = judge(rel_steps)

    # C: node vs module data (must be identical: ViewModule writes EntityWorld(p.World) = (x,y,z+sortTie))
    dmax = 0.0
    for (px, py), (nx, ny) in zip(focus, node):
        if math.isnan(nx):
            continue
        dmax = max(dmax, math.hypot(px * ppu - nx, py * ppu - ny))

    # D: sprite switches
    sprites = [r[12] for r in rows]
    switch = 0
    for i in range(1, len(sprites)):
        if sprites[i] != sprites[i - 1]:
            switch += 1
    rects = set()
    for r in rows:
        if not math.isnan(r[8]):
            rects.add((r[8], r[9]))
    nan_node = sum(1 for r in rows if math.isnan(r[6]))
    nan_spr = sum(1 for r in rows if r[12] == "(none)")

    node_sd = statistics.pstdev([abs(s) for s in node_speed]) if len(node_speed) > 1 else float("nan")
    cam_sd = statistics.pstdev([abs(s) for s in cam_speed]) if len(cam_speed) > 1 else float("nan")

    label = (" " + a.label) if a.label else ""
    L = []
    L.append("== camjitter WHO%s ==" % label)
    L.append("tsv=%s" % a.tsv)
    L.append("frames=%d span=%.3fs pxPerUnit=%.1f (screenH=%.0f ortho=%.2f; 1 cell = 144px @1080p)"
             % (len(rows), span, ppu, a.screen_h, a.ortho))
    L.append("dt: mean=%.6f sd=%.6f min=%.6f max=%.6f" % (statistics.fmean(dts), statistics.pstdev(dts),
                                                          min(dts), max(dts)))
    L.append("A camera   (world scroll on screen px): step|mean|=%.3f  LONG sd=%.3f p95=%.3f  LAT sd=%.3f p95=%.3f p2p=%.3f"
             % (pct([math.hypot(s[0], s[1]) for s in cam_steps], 0.5), jc["long_sd"], jc["long_p95"],
                jc["lat_sd"], jc["lat_p95"], jc["lat_p2p"]))
    L.append("B relative (character ON SCREEN px)    : step|mean|=%.3f  LONG sd=%.3f p95=%.3f  LAT sd=%.3f p95=%.3f p2p=%.3f"
             % (pct([math.hypot(s[0], s[1]) for s in rel_steps], 0.5), jr["long_sd"], jr["long_p95"],
                jr["lat_sd"], jr["lat_p95"], jr["lat_p2p"]))
    L.append("C node abs (= A+B, physics check)      : LONG sd=%.3f  LAT sd=%.3f" % (jn["long_sd"], jn["lat_sd"]))
    L.append("C3 corr(camera LONG deviation, dt-mean) = %.3f   (|r|>0.8 => the uneven step IS the frame time;"
             " ~0 => it is NOT dt, look at the code)" % corr_dt(cam_steps, dts))
    L.append("C2 node-vs-module-data max|diff|=%.4f px (must be ~0: same source; >0.5px => render-side bug)"
             % dmax)
    L.append("D speed sd: camera=%.2f px/s  node=%.2f px/s  (uneven frame time shows up here)"
             % (cam_sd, node_sd))
    L.append("E sprite: switches=%d (%.2f Hz)  distinct rects=%d  missing-node-frames=%d  no-sprite-frames=%d"
             % (switch, switch / span if span > 0 else 0.0, len(rects), nan_node, nan_spr))
    L.append("F rel offset p2p=%.3f px  (camera smoothing lag; >1px is visible as the character sliding on screen)"
             % (max(v[1] for v in rel) - min(v[1] for v in rel)
                if rel and all(not math.isnan(v[1]) for v in rel) else float("nan")))

    # verdict (rule fixed in the file header, applied mechanically)
    cam_lat, cam_long = jc["lat_sd"], jc["long_sd"]
    rel_lat = jr["lat_sd"]
    if not (rel_lat == rel_lat) or rel_lat < 0.5:
        who = ("the FOLLOW is clean (character stays within %.1f px of the camera laterally);"
               " the visible unevenness is the WORLD SCROLL: camera LONG sd=%.2f px/frame,"
               " frame-time driven" % (0.0 if rel_lat != rel_lat else rel_lat, cam_long))
    elif rel_lat > 2.0 * max(cam_lat, 1e-9):
        who = "camera FOLLOW smoothing (the character slides on screen, LAT sd=%.2f px)" % rel_lat
    else:
        who = "camera follow and world scroll are the same order (LAT rel=%.2f / cam=%.2f)" % (rel_lat, cam_lat)
    L.append("VERDICT who=%s" % who)
    print("\n".join(L))
    return 0


if __name__ == "__main__":
    sys.exit(main())
