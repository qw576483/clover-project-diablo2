#!/usr/bin/env python3
"""d2u27_judge.py -- U27 three-layer judge for the TSV frozen by d2u27_jitter.cs.

Input columns (one row per rendered frame):
    scen frame dt lx ly rx ry cx cy grid crossed spr
    lx/ly = IPlayerModule.World          (layer 3: LOGIC)
    rx/ry = player view node transform   (layer 2: RENDER, the only writer is ViewModule)
    cx/cy = Camera.main.transform         (layer 1: CAMERA)
    crossed = 1 on the frame where p.Grid changed (scenario S4 "cell crossing")

Rules (fixed HERE, before any capture, so the numbers cannot be reinterpreted):
  R1 render-vs-logic   : max per-frame |render - logic| over the whole trace must be <= 1e-4 cells.
                         (layer 2 must be a pure pass-through; >0.5 px on screen => someone
                          interpolates a second time.)
  R2 camera-vs-logic   : per-frame |d(relative offset)| <= (player's per-frame world step) * 1.1
                         (worst case = the camera is frozen => the offset changes exactly by the
                          player's step; the low-pass may lag one frame but must never overshoot.)
  R3 clamp band (S6)   : count frames where the player is moving while |camera step| < 5% of the
                         player's step => "the camera froze while the character kept walking".
                         Report it; also report the max single-frame camera step in S6 (a
                         freeze->catch-up gives a step far above the nominal).
  R4 pacing            : dt mean / stdev / p95 / max, plus corr(|step deviation|, dt) per scenario
                         (the existing camjitter口径: high corr => the unevenness IS frame time).
  R5 per-direction     : |world step| / |grid step| bucketed by the sign pair of the grid delta;
                         within-bucket spread must be <= 1.001; the CROSS-bucket range is reported
                         (the 2:1 isometric projection makes it 0.7071..1.4142 => registered, not judged).

usage: python d2u27_judge.py <tsv> [--label X] [--screen-h 1080] [--ortho 3.75]
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
            p = line.split("\t")
            if header is None:
                header = p
                if header[0] != "scen":
                    raise SystemExit("unexpected header: %r" % (header,))
                continue
            if len(p) < 12:
                continue
            try:
                # p[1..8] = frame dt lx ly rx ry cx cy  (p[9]=grid 是字符串，不参与取数)
                num = [float(x) for x in p[1:9]]
            except ValueError:
                continue
            # p[12] = 可选的 `cad` 列（帧节奏 A/B 分组；旧 TSV 没有 ⇒ 一律当 A）
            rows.append((p[0], num, p[9], p[10], p[11], p[12] if len(p) > 12 else "A"))
    if len(rows) < 20:
        raise SystemExit("too few rows (%d) -- the capture did not run" % len(rows))
    return rows


def pct(v, q):
    if not v:
        return float("nan")
    s = sorted(v)
    if len(s) == 1:
        return s[0]
    pos = q * (len(s) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    return s[lo] if lo == hi else s[lo] + (s[hi] - s[lo]) * (pos - lo)


def hyp(a, b):
    return math.hypot(a, b)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv")
    ap.add_argument("--label", default="")
    ap.add_argument("--screen-h", type=float, default=1080.0)
    ap.add_argument("--ortho", type=float, default=3.75)
    a = ap.parse_args()
    rows = read_tsv(a.tsv)
    ppu = a.screen_h / (2.0 * a.ortho)          # screen px per world unit (144 @1080p/3.75)

    scen = [r[0] for r in rows]
    #     num = [frame, dt, lx, ly, rx, ry, cx, cy]  => 0    1   2   3   4   5   6   7
    dt = [r[1][1] for r in rows]
    L = [(r[1][2], r[1][3]) for r in rows]
    R = [(r[1][4], r[1][5]) for r in rows]
    C = [(r[1][6], r[1][7]) for r in rows]

    # ---- R1 -----------------------------------------------------------------
    dmax, dmax_i = 0.0, -1
    for i in range(len(rows)):
        if any(math.isnan(v) for v in R[i]):
            continue
        d = hyp(R[i][0] - L[i][0], R[i][1] - L[i][1])
        if d > dmax:
            dmax, dmax_i = d, i
    nan_render = sum(1 for v in R if any(math.isnan(x) for x in v))

    # ---- R2 / R3 ------------------------------------------------------------
    step_p, jmp, frozen = {}, {}, {}
    for i in range(1, len(rows)):
        s = scen[i]
        lp = hyp(L[i][0] - L[i - 1][0], L[i][1] - L[i - 1][1])
        cp = hyp(C[i][0] - C[i - 1][0], C[i][1] - C[i - 1][1])
        r0 = (L[i - 1][0] - C[i - 1][0], L[i - 1][1] - C[i - 1][1])
        r1 = (L[i][0] - C[i][0], L[i][1] - C[i][1])
        j = hyp(r1[0] - r0[0], r1[1] - r0[1])
        step_p.setdefault(s, []).append(lp)
        jmp.setdefault(s, []).append(j)
        if lp > 1e-6 and cp < 0.05 * lp:
            frozen[s] = frozen.get(s, 0) + 1
    bad_r2 = []
    for s, v in jmp.items():
        lim = max(step_p[s]) * 1.1 + 1e-4
        for i in range(len(v)):
            if v[i] > lim:
                bad_r2.append((s, i, v[i], lim))
                break

    # ---- R4 -----------------------------------------------------------------
    dtv = [d if d > 0 else float("nan") for d in dt]
    dtv = [d for d in dtv if not math.isnan(d)]
    mean_dt = statistics.fmean(dtv) if dtv else float("nan")
    sd_dt = statistics.pstdev(dtv) if len(dtv) > 1 else float("nan")

    def corr_dt(s):
        idx = [i for i in range(1, len(rows)) if scen[i] == s]
        jl, dl = [], []
        for i in idx:
            lp = hyp(L[i][0] - L[i - 1][0], L[i][1] - L[i - 1][1])
            cp = hyp(C[i][0] - C[i - 1][0], C[i][1] - C[i - 1][1])
            jl.append(cp - lp)          # camera-vs-player step deviation
            dl.append(dt[i])
        if len(jl) < 5:
            return float("nan")
        sj, sd = statistics.pstdev(jl), statistics.pstdev(dl)
        if sj < 1e-12 or sd < 1e-12:
            return float("nan")
        mj, md = statistics.fmean(jl), statistics.fmean(dl)
        cov = sum((x - mj) * (y - md) for x, y in zip(jl, dl)) / len(jl)
        return cov / (sj * sd)

    # ---- R5 -----------------------------------------------------------------
    # SELF-TEST FINDING (2026-09-24, offline fixtures): bucketing by the INTEGER `grid`
    # column is a MEASUREMENT-RECIPE bug, not a defect detector. Most frames carry
    # dgrid == 0 (dropped), and every cell crossing carries dgrid == 1 no matter where in
    # the cell it happened, so a perfectly uniform advance still shows spread ~= 1.04 =>
    # a FALSE RED on real data. The invariant that actually matters is "|world step| is
    # constant within ONE direction" => bucket by the WORLD delta's sign pair (the iso
    # projection maps the grid axes onto world diagonals, so this still yields 8 buckets).
    # The world/grid RATIOS (0.7071 / 1.11797 / 1.41421) are pinned OFFLINE in
    # tools/probes/hosts/playercheck §15 f2 and are deliberately NOT re-derived here.
    bydir = {}
    for i in range(1, len(rows)):
        if math.isnan(R[i][0]) or math.isnan(L[i][0]):
            continue
        dwx = L[i][0] - L[i - 1][0]
        dwy = L[i][1] - L[i - 1][1]
        wb = hyp(dwx, dwy)
        if wb < 1e-6:
            continue                     # player did not move => direction undefined
        sx = 1 if dwx > 1e-6 else (-1 if dwx < -1e-6 else 0)
        sy = 1 if dwy > 1e-6 else (-1 if dwy < -1e-6 else 0)
        bydir.setdefault((sx, sy), []).append(wb)
    worst = 1.0
    dirline = []
    for k, v in sorted(bydir.items()):
        if len(v) < 5:
            continue
        sp = max(v) / min(v)
        worst = max(worst, sp)
        dirline.append("world(%d,%d) n=%d step %.5f..%.5f" % (k[0], k[1], len(v), min(v), max(v)))

    # ---- report -------------------------------------------------------------
    L_ = []
    lbl = (" " + a.label) if a.label else ""
    L_.append("== d2u27 judge%s ==" % lbl)
    L_.append("tsv=%s  frames=%d  pxPerUnit=%.1f (screenH=%.0f ortho=%.2f)" % (a.tsv, len(rows), ppu, a.screen_h, a.ortho))
    L_.append("scenarios: " + ", ".join("%s=%d" % (s, scen.count(s)) for s in sorted(set(scen))))
    L_.append("dt: mean=%.6f sd=%.6f min=%.6f p95=%.6f max=%.6f" % (mean_dt, sd_dt, min(dtv), pct(dtv, 0.95), max(dtv)))
    # ⚠️ 实测教训：渲染列**整列 NaN**（驱动探针取不到节点）时，`max|diff|` 会算出 0.0 ⇒ R1"零样本空过"=假绿。
    #    ⇒ 判据必须要求**足够多的有效样本**，否则判 FAIL（"判据无效"），而不是 PASS。
    r1_samples = len(rows) - nan_render
    r1_verdict = "INVALID (nan-render rows=%d, need >=10 valid samples)" % nan_render if r1_samples < 10 else \
                ("PASS" if dmax <= 1e-4 else "FAIL")
    L_.append("R1 render-vs-logic  max|diff|=%.6f cells = %.3f px  (valid samples=%d, nan-render rows=%d)  %s"
              % (dmax, dmax * ppu, r1_samples, nan_render, r1_verdict))
    L_.append("R2 camera-vs-logic  per-scenario: " + ("; ".join(
        "%s maxJump=%.5f lim=%.5f %s" % (s, max(jmp[s]), max(step_p[s]) * 1.1 + 1e-4,
                                          "FAIL" if any(b[0] == s for b in bad_r2) else "PASS")
        for s in sorted(jmp))))
    L_.append("R3 clamp band (clampband) frozen-while-walking frames=%d ; camera max step=%.5f cells=%.1f px"
              % (frozen.get("clampband", 0),
                 max([hyp(C[i][0] - C[i - 1][0], C[i][1] - C[i - 1][1])
                      for i in range(1, len(rows)) if scen[i] == "clampband"] or [float("nan")]),
                 max([hyp(C[i][0] - C[i - 1][0], C[i][1] - C[i - 1][1])
                      for i in range(1, len(rows)) if scen[i] == "clampband"] or [float("nan")]) * ppu))
    L_.append("R3b all scenarios frozen-while-walking: " + ", ".join("%s=%d" % (s, n) for s, n in sorted(frozen.items())))
    L_.append("R4 corr(camera-step deviation, dt) per scenario: " + "; ".join(
        "%s=%.3f" % (s, corr_dt(s)) for s in sorted(set(scen))))
    L_.append("R5 world/grid per grid-direction (within-bucket spread worst=%.6f %s): %s"
              % (worst, "PASS" if worst <= 1.001 else "FAIL", "; ".join(dirline)))
    L_.append("NOTE 'crossed' frames (S4 cell crossings): %d"
              % sum(1 for r in rows if r[3] == "1"))

    # ---- R6 ★ 帧节奏 A/B（team-lead 指定的实验设计；一次 Play 会话内做完） --------------
    #   同一会话里先跑档位 A（vSync=0 + targetFrameRate=60）再跑档位 B（vSync=1 + targetFrameRate=-1），
    #   两遍走**同一段场景**，靠 TSV 的 `cad` 列分组。判据：**哪一组把 dt 的 sd/P99 与抖动指标 J 压得更低**
    #   （⛔ 不许只看"看起来顺"）。J = 相机逐帧格空间步长的局部均值（±5 帧）偏差 sd —— 与
    #   `playercheck §15 g2` 的 J 同一把尺；Play 里做不了"置换成均匀 dt 再重跑"，所以这里是**两组直接比**。
    def judder_sd(vals, win=5):
        dev = []
        for i in range(len(vals)):
            lo, hi = max(0, i - win), min(len(vals), i + win + 1)
            m = sum(vals[lo:hi]) / (hi - lo)
            dev.append(vals[i] - m)
        if len(dev) < 2:
            return float("nan")
        return statistics.pstdev(dev)

    groups = sorted(set(r[5] for r in rows))
    L_.append("R6 cadence A/B groups=%s" % ",".join(groups))
    for g in groups:
        idx = [i for i in range(len(rows)) if rows[i][5] == g]
        gdt = [rows[i][1][1] for i in idx if rows[i][1][1] > 0]
        # 相机逐帧步长（格空间；只在**同一组内**的连续帧之间算）
        csteps = []
        for a, b in zip(idx, idx[1:]):
            if b != a + 1 or any(math.isnan(v) for v in C[b] + C[a]):
                continue
            csteps.append(hyp(C[b][0] - C[a][0], C[b][1] - C[a][1]))
        jd = judder_sd(csteps)
        corm = float("nan")
        if len(csteps) > 5:
            dev = []
            for t in range(len(csteps)):
                lo, hi = max(0, t - 5), min(len(csteps), t + 6)
                dev.append(csteps[t] - sum(csteps[lo:hi]) / (hi - lo))
            dts2 = [rows[idx[t + 1]][1][1] for t in range(len(csteps))]
            if len(dev) == len(dts2) and statistics.pstdev(dev) > 1e-12 and statistics.pstdev(dts2) > 1e-12:
                md, mt = statistics.fmean(dev), statistics.fmean(dts2)
                cov = sum((x - md) * (y - mt) for x, y in zip(dev, dts2)) / len(dev)
                corm = cov / (statistics.pstdev(dev) * statistics.pstdev(dts2))
        L_.append("  %s: frames=%d dt n=%d mean=%.6f sd=%.6f min=%.6f p95=%.6f p99=%.6f max=%.6f"
                  " | J(相机步 sd)=%.6f corr(步偏差,dt)=%.3f"
                  % (g, len(idx), len(gdt), statistics.fmean(gdt), statistics.pstdev(gdt), min(gdt),
                     pct(gdt, 0.95), pct(gdt, 0.99), max(gdt), jd, corm))
    if len(groups) > 1:
        def _stat(g, key):
            idx = [i for i in range(len(rows)) if rows[i][5] == g and rows[i][1][1] > 0]
            v = [rows[i][1][1] for i in idx]
            return key(v)
        L_.append("  VERDICT-AB (lower is better): " + " | ".join(
            "%s sd=%.6f p99=%.6f" % (g, _stat(g, statistics.pstdev), _stat(g, lambda v: pct(v, 0.99)))
            for g in groups))
    print("\n".join(L_))
    return 0


if __name__ == "__main__":
    sys.exit(main())
