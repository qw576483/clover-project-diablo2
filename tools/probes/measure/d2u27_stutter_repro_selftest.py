#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d2u27_stutter_repro.py 的三向自检（离线、秒级、不需要编辑器）
==========================================================
口径（skill §8.3「闸门自身也要被闸」）：一项新读数必须能同时通过
  (A) 已知**正确**样本 -> 检不出东西（不会凭空白报）；
  (B) 已知**错误**样本 -> 检出，且**因为对的原因**检出（位置/数值逐条对上）；
  (C) 该读数**确实在判**，不是恒真/恒假的空转（值改一点，读数就变）。
本自检对三项读数逐个做 (A)(B)(C)，另外单独闸住 [M1a] 的场景标签审计
（它必须能 True 也能 False，否则 [M1]/[M3] 的场景归因没有可信度）。

fixture 全部生成到 `<项目根>/.ai-tmp/test/d2u27-stutter-selftest/`（⛔ 不落仓库树）。
用法: python tools/probes/measure/d2u27_stutter_repro_selftest.py
"""
import os
import re
import subprocess
import sys

ROOT = r"c:/Work/Server/f-v2/clover-project-diablo2"
SCRIPT = os.path.join(ROOT, "tools/probes/measure/d2u27_stutter_repro.py")
FIXDIR = os.path.join(ROOT, ".ai-tmp/test/d2u27-stutter-selftest")

IDLE_N = 41                 # 驱动声明每遍开场 40 帧静止（d2u27_jitter.cs:845-849）=> 实采 41 行
STALL = 0.155077            # 注入的单帧卡顿值（= u27v9 A 档实测 max，便于对照）
# `dt <= 目标周期 + 半格` 叫**低半区**，本脚本判定的子集叫**长帧子集**；
# "未触顶帧"一词只属 ab_trend.py 的 ±1% 口径。
CAP_A = 1.0 / 60.0


def dt_for(uncapped, i):
    """构造 dt：A 档 4/5 的帧不快于目标周期（= 落在低半区），B 档无目标周期。"""
    if uncapped:
        return 0.005 + 0.0005 * (i % 4)
    return (1.0 / 60.0, 1.0 / 60.0, 1.0 / 60.0, 0.0145, 0.0190)[i % 5]


def build(path, stall_at=(), mislabel_pass2=False, n_line=300):
    """写出一个 TSV fixture，并返回**构造事实**（断言只用这些事实，不读脚本内部）。

    ⚠️ `facts["capped"]` 按**构造时的名义 dt** 统计（与落盘精度无关）——
    这正是自检 C2 要闸的东西：落盘把 1/60 写成 "0.016667"（> 1/60），
    任何"裸 `dt <= 1/fps`"的实现都会把它错判成"未触顶"（本片自检实测抓到过）。
    """
    rows = []
    facts = {"capped": {}, "untouched": {}, "len": {}, "stall": {}, "head": {}}
    frame = 1000
    for cad, uncapped in (("A", False), ("B", True)):
        scen = "clampband" if (cad == "B" and mislabel_pass2) else "idle"
        facts["head"][cad] = scen
        body = [(scen, "idle_s_%d" % (i % 5)) for i in range(IDLE_N)]
        body += [("line", "run_w_%d" % (i % 8)) for i in range(n_line)]
        facts["len"][cad] = len(body)
        truth = None
        for i, (sc, spr) in enumerate(body):
            dt = dt_for(uncapped, i)
            if (cad, i) in stall_at:
                dt = STALL
                truth = (i, sc, spr)
            if not uncapped and dt <= CAP_A:
                facts["capped"][cad] = facts["capped"].get(cad, 0) + 1
            else:
                facts["untouched"][cad] = facts["untouched"].get(cad, 0) + 1
            rows.append("%s\t%d\t%.6f\t0.000000\t0.000000\t0.000000\t0.000000\t0.000000\t0.000000"
                        "\t(0,0)\t0\t%s\t%s\n" % (sc, frame, dt, spr, cad))
            frame += 1
        if truth:
            facts["stall"][cad] = truth
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write("scen\tframe\tdt\tlx\tly\trx\try\tcx\tcy\tgrid\tcrossed\tspr\tcad\n")
        f.writelines(rows)
    return facts


def run(path, ref=""):
    env = dict(os.environ)
    env["PYTHONIOENCODING"] = "utf-8"          # 读数含中文 => 固定管道编码，别让 cp936 搅局
    cmd = [sys.executable, SCRIPT, path]
    if ref:
        cmd += ["--ref", ref]
    p = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8",
                       errors="replace", env=env)
    return p.returncode, (p.stdout or "") + (p.stderr or "")


def section(out, tag):
    """取输出中 `  [tag]` 开头的那一段（到下一个 `  [` 或结尾）。"""
    lines = out.split("\n")
    res, on = [], False
    for ln in lines:
        if ln.lstrip().startswith("[") and "]" in ln:
            on = ln.lstrip().startswith("[" + tag + "]")
            if on:
                res.append(ln)
        elif on:
            res.append(ln)
    return res


def m1_of(out):
    """返回 {cad: [(passIdx, dt), ...]}。"""
    res = {}
    for ln in out.split("\n"):
        m = re.match(r"\s+frame=(\d+)\s+cad=(\S+)\s+passIdx=(\d+)/(\d+)\s+.*\sdt=([\d.]+)\s*$", ln)
        if m:
            res.setdefault(m.group(2), []).append((int(m.group(3)), float(m.group(5))))
    return res


def m1_count(out, cad):
    for ln in out.split("\n"):
        m = re.match(r"\s+\[M1\] cad=(\S+)\s+frames=(\d+)\s+.*\bn=(\d+)", ln)
        if m and m.group(1) == cad:
            return int(m.group(3))
    return None


def m2_rows(out):
    """{cad: {"max": x, "capped": n, "untouched": n}}"""
    res = {}
    for ln in section(out, "M2"):
        m = re.match(r"\s+([AB])\s+(\d+)\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+"
                     r"\s+[\d.]+\s+([\d.]+)\s+\|\s+(\d+)\s+(\d+)\s*$", ln)
        if m:
            res[m.group(1)] = {"max": float(m.group(3)), "capped": int(m.group(4)),
                               "untouched": int(m.group(5))}
    return res


def m3_pool(out):
    """{cad: {"excluded": n, "untouched": n}}"""
    res = {}
    for ln in out.split("\n"):
        m = re.search(r"pool cad=(\S+)\s*:.*untouched n=(\d+)\s+\(excluded=(\d+)\)", ln)
        if m:
            res[m.group(1)] = {"untouched": int(m.group(2)), "excluded": int(m.group(3))}
    return res


def m1a_head(out):
    """{cad: True/False} —— 每遍开头块是否与驱动声明一致。"""
    res = {}
    for ln in out.split("\n"):
        m = re.match(r"\s+cad=(\S+)\s+开头块 scen=(\S+)\s+n=(\d+)\s+spr前缀=(\S+)\s+"
                     r"frame=(\d+)\.\.(\d+)\s+labelMatchesDeclared=(\S+)", ln)
        if m:
            res[m.group(1)] = m.group(7)
    return res


def main():
    # 同主脚本：本机控制台 cp936 印不出 (U+26D4) ⇒ 会把「打印子进程读数」这一步整个崩掉
    # （实测一次；读数没问题，坏的是输出编码）。退化成 '?' 比整段丢失好。
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass
    if not os.path.isdir(FIXDIR):
        os.makedirs(FIXDIR)
    clean = os.path.join(FIXDIR, "clean.tsv")
    bad = os.path.join(FIXDIR, "bad.tsv")
    ref = os.path.join(FIXDIR, "ref.tsv")

    fc = build(clean, stall_at=(), mislabel_pass2=False)
    fb = build(bad, stall_at={("A", 3), ("B", IDLE_N + 7)}, mislabel_pass2=True)
    fr = build(ref, stall_at={("A", 100), ("B", IDLE_N + 150)}, mislabel_pass2=True, n_line=200)

    rc1, out1 = run(clean)
    rc2, out2 = run(bad, ref=ref)
    print("=== (A) CLEAN fixture: exit=%d ===" % rc1)
    print("\n".join(section(out1, "M1") + section(out1, "M2")))
    print("=== (B) BAD fixture (vs REF): exit=%d ===" % rc2)
    print("\n".join(section(out2, "M1")))
    print("=== fixtures ===")
    for p in (clean, bad, ref):
        print("  %s" % p)

    checks = []

    def chk(name, cond, detail):
        checks.append((name, bool(cond), detail))

    #  为什么必须有：工具"被写入的中间态"被加载时会抛**非终止**异常 ⇒ 输出里**没有裁决行**、
    #  而退出码**仍是 0** ⇒ "没看到 FAIL 就当通过"那一刻判绿、实际什么都没判（select 亲测）。
    #  本自检本来就**结构性 fail-closed**（子进程崩 ⇒ m1_count 返回 None ⇒ A1 立刻红），
    chk("E1 clean 子进程 exit==0", rc1 == 0, "rc=%s" % rc1)
    chk("E2 bad   子进程 exit==0", rc2 == 0, "rc=%s" % rc2)
    chk("E3 两份输出都含裁决行 [M1] cad=A（无裁决行 = 工具故障 => 红）",
        ("[M1] cad=A" in out1) and ("[M1] cad=B" in out1) and ("[M1] cad=A" in out2)
        and ("THREE-WAY" not in out1 and "THREE-WAY" not in out2),
        "clean hasM1=%s hasM1B=%s | bad hasM1=%s" % ("[M1] cad=A" in out1, "[M1] cad=B" in out1,
                                                      "[M1] cad=A" in out2))

    # ---- (A) 已知正确样本 => M1 必须无卡顿（不自造假阳性） ----
    chk("A1 clean.M1.A==0", m1_count(out1, "A") == 0, "n=%s" % m1_count(out1, "A"))
    chk("A2 clean.M1.B==0", m1_count(out1, "B") == 0, "n=%s" % m1_count(out1, "B"))
    chk("A3 clean.M1a 两遍开头块 labelMatchesDeclared=True",
        m1a_head(out1) == {"A": "True", "B": "True"}, str(m1a_head(out1)))

    # ---- (B) 已知错误样本 => 检出，且位置/数值逐条对上 ----
    hits = m1_of(out2)
    ok_pos = True
    detail_pos = []
    for cad, (idx, sc, spr) in fb["stall"].items():
        got = hits.get(cad, [])
        d = [g for g in got if abs(g[1] - STALL) < 1e-9]
        hit = (len(got) == 1 and len(d) == 1 and d[0][0] == idx)
        ok_pos = ok_pos and hit
        detail_pos.append("%s want passIdx=%d got=%s" % (cad, idx, got))
    chk("B1 bad.M1 恰好命中注入帧且 passIdx 相同", ok_pos, " ; ".join(detail_pos))
    chk("B2 bad.M1 计数==注入数", m1_count(out2, "A") == 1 and m1_count(out2, "B") == 1,
        "A=%s B=%s" % (m1_count(out2, "A"), m1_count(out2, "B")))
    r2 = m2_rows(out2)
    chk("B3 bad.M2 两档 max 都等于注入值",
        r2.get("A", {}).get("max") == STALL and r2.get("B", {}).get("max") == STALL, str(r2))

    # ---- (C) 读数确实在判（值/构造变一点，读数就变） ----
    chk("C1 clean 的 max != 注入值（M2 不是恒等回显）",
        r2.get("A", {}).get("max") == STALL and m2_rows(out1).get("A", {}).get("max", 0) < STALL,
        "clean max=%s" % m2_rows(out1).get("A", {}).get("max"))
    p3 = m3_pool(out2)
    chk("C2 bad.M3 A 的 excluded == 构造的触顶帧数",
        p3.get("A", {}).get("excluded") == fb["capped"].get("A"),
        "got=%s want=%s" % (p3.get("A", {}).get("excluded"), fb["capped"].get("A")))
    chk("C3 bad.M3 B 无封顶 => excluded==0 且 untouched==全部",
        p3.get("B", {}).get("excluded") == 0
        and p3.get("B", {}).get("untouched") == fb["untouched"].get("B"),
        "got=%s want untouched=%s" % (p3.get("B", {}), fb["untouched"].get("B")))
    chk("C4 bad.M1a 第 2 遍开头块被标成 clampband => labelMatchesDeclared=False",
        m1a_head(out2).get("B") == "False" and m1a_head(out2).get("A") == "True",
        str(m1a_head(out2)))

    # ---- [M1-REF] 跨批配对：位移过的参考批必须给出非零 dFrac ----
    # 只数 `cur frame=` 明细行；`=> pairs=…` 汇总行不算（第一次写自检时把它也数进去了）。
    det, got_same, cur_cad = [], {}, None
    for ln in section(out2, "M1-REF"):
        m = re.match(r"\s+cad=(\S+)\s*:\s*cur n=", ln)
        if m:
            cur_cad = m.group(1)
            continue
        if "cur frame=" in ln and cur_cad:
            det.append(ln)
            m = re.search(r"sameScen=(\S+)", ln)
            if m:
                got_same[cur_cad] = m.group(1)
    dfr = [abs(float(m.group(1))) for m in
           (re.search(r"dFrac=([+-][\d.]+)", ln) for ln in det) if m]
    exp_same = {}
    for cad, (_i, sc, _s) in fb["stall"].items():
        ridx, rsc, _rs = fr["stall"][cad]
        exp_same[cad] = "Y" if sc == rsc else "N"
    chk("D1 M1-REF 逐条配对数 == 注入数",
        len(det) == len(fb["stall"]), "detail pairs=%d" % len(det))
    chk("D2 M1-REF 参考批被位移过 => min|dFrac| > 0 且 sameScen 与构造事实一致",
        len(dfr) == len(det) and min(dfr) > 0.01 and got_same == exp_same,
        "min|dFrac|=%s got=%s want=%s" % (min(dfr) if dfr else None, got_same, exp_same))
    chk("D3 REF fixture 构造事实自洽（位移确实存在）",
        fr["len"]["A"] != fb["len"]["A"], "refA=%d badA=%d" % (fr["len"]["A"], fb["len"]["A"]))

    bad_n = [c for c in checks if not c[1]]
    print("=== SELFTEST CHECKS ===")
    for name, ok, detail in checks:
        print("  %-58s %s   %s" % (name, "PASS" if ok else "FAIL", detail))
    print("THREE-WAY (detector: silent-on-clean / fires-on-bad / not-a-noop): %s (OK=%d FAIL=%d)"
          % ("PASS" if not bad_n else "FAIL", len(checks) - len(bad_n), len(bad_n)))
    return 0 if not bad_n else 1


if __name__ == "__main__":
    sys.exit(main())
