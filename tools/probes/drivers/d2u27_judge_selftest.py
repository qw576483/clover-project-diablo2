#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
U27 judge self-test (offline, no editor): the "three-way" rule (README 5.2-34).

  (A) POSITIVE fixture  -> d2u27_judge.py must be GREEN (R1/R2/R5 PASS)
  (B) DEGENERATE fixture -> it must be RED, and red FOR THE RIGHT REASON
      (render != logic, camera frozen while walking, 5% step spike, dt spike)

Column order is the driver's header (d2u27_jitter.cs):
  scen frame dt lx ly rx ry cx cy grid crossed spr cad
World convention (derived from the measured world/grid ratios in
tools/probes/hosts/playercheck §15 f2: (1,0)->1.11797, (0,1)->1.11797,
(1,-1)->1.41421, (-1,-1)->0.7071):
  world = ((gx - gy), (gx + gy) * 0.5)
"""
import os
import subprocess
import sys

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
JUDGE = os.path.join(ROOT, "tools/probes/drivers/d2u27_judge.py")
OUT = os.path.dirname(os.path.abspath(__file__))  # moved into tools/probes/drivers/: keep the fixtures next to the judge

HDR = "scen\tframe\tdt\tlx\tly\trx\try\tcx\tcy\tgrid\tcrossed\tspr\tcad\n"


def world(gx, gy):
    return ((gx - gy), (gx + gy) * 0.5)


def build(path, degenerate):
    rows = []
    gx, gy = 0.0, 0.0
    dt = 1.0 / 60.0
    # walk along grid +x at 3 cells/s => 0.05 cells per frame (GameConst.PlayerWalkSpeed/...)
    step = 3.0 / 60.0
    prev_lw = None
    prev_cam = None
    lx, ly = world(gx, gy)
    for i in range(120):
        d = dt
        if degenerate and i == 40:
            d = 0.05                      # known error 3: dt spike
        # POSITIVE fixture must be UNIFORM (a uniform advance is the thing R5 asserts);
        # any position jitter belongs in the degenerate fixture only.
        s = step
        if degenerate:
            s = step * (1.0 + (0.02 if (i % 2 == 0) else -0.02))   # real-world dt jitter
        if degenerate and i == 30:
            s *= 1.05                     # known error 2: 5% step spike
        gx += s
        lx, ly = world(gx, gy)
        # camera: one frame of lag (low pass), unless we inject "frozen while walking"
        if degenerate and i == 60:
            cx, cy = prev_cam if prev_cam is not None else (lx, ly)   # frozen camera
        else:
            cx, cy = (prev_lw if prev_lw is not None else (lx, ly))
        prev_cam = (cx, cy)
        prev_lw = (lx, ly)
        # render: pure pass-through (must equal logic), unless we inject an offset
        rx, ry = lx, ly
        if degenerate and i == 50:
            rx = lx + 1e-3                # known error 1: layer 2 re-interpolated
        scen = "line"
        cad = "A"
        rows.append("%s\t%d\t%.6f\t%.6f\t%.6f\t%.6f\t%.6f\t%.6f\t%.6f\t(%d,%d)\t%s\t%s\t%s\n" % (
            scen, i, d, lx, ly, rx, ry, cx, cy, int(gx // 1), 0, "1" if i % 12 == 0 else "0",
            "walk", cad))
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(HDR)
        f.writelines(rows)
    return path


def run(path):
    p = subprocess.run([sys.executable, JUDGE, path], capture_output=True, text=True)
    return p.returncode, p.stdout, p.stderr


def main():
    ok_clean = os.path.join(OUT, "u27_judge_selftest_clean.tsv")
    ok_bad = os.path.join(OUT, "u27_judge_selftest_bad.tsv")
    build(ok_clean, False)
    build(ok_bad, True)
    rc1, out1, err1 = run(ok_clean)
    rc2, out2, err2 = run(ok_bad)
    print("=== (A) POSITIVE fixture: exit=%d ===" % rc1)
    print(out1.strip() or err1.strip())
    print()
    print("=== (B) DEGENERATE fixture: exit=%d ===" % rc2)
    print(out2.strip() or err2.strip())
    print()
    # three-way assertions
    a_ok = ("R1 render-vs-logic" in out1 and "PASS" in out1.split("R1 render-vs-logic")[1].split("\n")[0])
    b_r1 = "R1 render-vs-logic" in out2 and "FAIL" in out2.split("R1 render-vs-logic")[1].split("\n")[0]
    print("=== SELFTEST VERDICT ===")
    print("(A) positive -> R1 PASS : %s" % a_ok)
    print("(B) degenerate -> R1 FAIL : %s" % b_r1)
    print("THREE-WAY: %s" % ("PASS" if (a_ok and b_r1) else "FAIL"))


if __name__ == "__main__":
    main()
