#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
d2u27_stutter_repro.py -- U27「单帧卡顿复现性」三项读数
=====================================================
REPORT-ONLY：本脚本**只报数，不判定** --
  * 不自造阈值：唯一的时间阈值 100 ms 由 team-lead 指定（`--stall-ms` 可覆盖）；
  * 不输出 PASS / FAIL；"触顶"取自**驱动自己的配置**（`targetFrameRate`），不是新阈值；
  * 唯一"判定"性质的东西是**场景标签审计**（`scen` 列与 `spr` 列自相矛盾），
    它判的是**数据自洽**，不是性能好坏，且只报数、不打分。

输入 = `d2u27_jitter.cs` 写出的 TSV（表头出处 `d2u27_jitter.cs:605`）：
    scen \\t frame \\t dt \\t lx \\t ly \\t rx \\t ry \\t cx \\t cy \\t grid \\t crossed \\t spr \\t cad

档位定义（出处 = 驱动自己的常量，`d2u27_jitter.cs:397` / `:399`）：
    A = `targetFrameRate=60` + `vSyncCount=0`  => 有封顶：capPeriod = 1/60 s
    B = `targetFrameRate=-1` + `vSyncCount=1`  => 无封顶（本环境 vSync 未真正节流）

三项读数：
  [M1] 单帧卡顿（dt >= 100 ms）：次数 + 位置（帧号 / passIdx / 场景 / 行为 spr / 场景内位置）
       + `--ref <tsv>` 时给出"与前一批是否落在同一点"的**逐条配对读数**；
  [M1a] 场景标签审计：`scen` 列说的是 A 场景、`spr` 列却说是另一个行为 => 位置归因不可信，先报出来。
  [M2] 两档 dt 分布并排：mean / sd / min / p50 / p90 / p95 / p99 / max + `dt<=capPeriod` 帧数
       + 贴顶比例（判封顶是"硬"还是"只钉住均值"）+ 组内前半/后半趋势。
  [M3] 同（场景, 行为）的 sd（绝对）与 CV（辅助），**只在长帧子集上算**
       （剔除 = **低半区帧** `dt <= 1/targetFrameRate + 半格`；B 无封顶 => 低半区为空）。
  [M3b] 两口径对照：同一帧集上，`ab_trend.py` 口径与"长帧子集"口径各剩多少 / sd / CV（见下）。

⚠️ **术语裁定（team-lead 2026-09-24）—— 本脚本必须照此措辞**：
  * "**未触顶帧**"一词**只属 `ab_trend.py` 的口径**（剔 `abs(dt - 1/target) <= 1% * (1/target)`，
    量 = "**削峰后还剩多少离散度**"）；⛔ 本脚本不许用它。
  * 本脚本剔掉的是 **低半区（`dt <= 目标周期 + 半格`）**，剩下的是
    **「长帧子集（`dt > 目标周期`）」** = "卡顿长帧的分布"。两者是**两个不同的量**。
  * **两种极性下结论同向**（故台账降级不受影响）：实测（`u27v9`，同场景同行为 A）
    甲 `ab_trend` n=269 sd=0.002345 CV=0.140 ／ 乙 长帧子集 n=151 sd=0.002182 CV=0.121
    （B 两集合同值：n=924 sd=0.001221 CV=0.228）⇒ **`sd` ⇒ B 更稳、`CV` ⇒ A 更稳**
    ⇒ `order-confounded` + `metric-dependent` 的降级结论**不变**。

与 `tools/probes/measure/ab_trend.py` 的分工（**同一批 teammate 资产，实测数值已互相对齐**）：
  * `ab_trend.py` = **读数器**：P1/P2/P0 口径（同场景同行为 `line/run_*`、±1% 带宽）、R-DIST、
    R-SPIKE、"同点"按 **(scen, spr) 集合**判（⛔ 不按 frame 号）；
  * 本脚本 = **位置/顺序 + 自洽审计**：`passIdx` / 归一化位置 / 场景段内位置（回答"A max 在前 1/3"
    这类问题 —— 集合口径表达不了这个）、`--ref` 逐条位置配对、`[M1a]` 场景标签审计、`[M3b]` 两口径对照。
  * 重叠量（R-DIST 的 median/mean/p95/max、R-SPIKE 的 count/frame）**必须一致**；
    实测 u27v9 两套完全一致（p95 末位 1e-6 差异来自分位插值实现，已登记）。

用法（⛔ 一律绝对路径 —— 队纪律② 2026-09-24）：
    python tools/probes/measure/d2u27_stutter_repro.py <tsv> [--ref <tsv>] [--stall-ms 100]
    # 例：python tools/probes/measure/d2u27_stutter_repro.py \
    #        c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/d2u27-u27v9.tsv \
    #        --ref c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/d2u27-u27v8.tsv \
    #        --out c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/u27-stutter-repro-v9.txt
⛔ `--out` 传相对路径会**直接 FAIL(exit 2)**：本机进程 CWD = 工作区根 `c:\Work\Server\f-v2`，
  而 PS location 常是项目根 ⇒ 相对路径会静默落进**另一个 `.ai-tmp` 树**（那棵树真实存在、不报错）。
  头部逐次打印 `resolved tsv=… cwd=…`，所以"读错树"也会现形（不是靠自觉）。
"""
import argparse
import hashlib
import math
import os
import statistics
import sys
import time

# 驱动表头（d2u27_jitter.cs:605）的列索引。`cad` 是 u27v8 起新增的最后一列；
# 旧 TSV 没有它 => 一律当 "A"（与 d2u27_judge.py:58 同口径）。
C_SCEN, C_FRAME, C_DT, C_SPR, C_CAD = 0, 1, 2, 11, 12
MIN_COLS = 12

# dt 在 TSV 里以 `Time.deltaTime.ToString("0.######")` 落盘（d2u27_jitter.cs:552）
# => 打印精度 1e-6，半格 = 5e-7。判"低半区"必须带上它：
#    真值 1/60 会被写成 "0.016667"，比 1/60 大 3.3e-7 —— 不加半格容差就会把
#    "最靠近目标周期的那批帧"整批漏掉、反而算进"长帧子集"（本片自检 C2 抓到的真错）。
LSB = 5e-7


def is_low_half(dt, cap):
    """**低半区帧** = 该帧不快于目标周期（`fps >= targetFrameRate` ⇔ `dt <= 1/fps`），带半格落盘容差。
    `cap is None`（档位 B: targetFrameRate=-1）=> 低半区为空。

    ⚠️ **术语裁定（team-lead 2026-09-24）**：本判定**不叫"触顶帧"**，本脚本判定的子集
    （`not is_low_half(...)`）**叫「长帧子集（`dt > 目标周期`）」**、**不叫"未触顶帧"**。
    "未触顶帧"一词只属 `tools/probes/measure/ab_trend.py` 的口径
    （剔 `abs(dt - 1/target) <= 1% * (1/target)`，量 = "削峰后还剩多少离散度"）。
    两者是**两个不同的量**，实测同向（见 [M3b] 与 [M3] 的口径提醒）。"""
    return cap is not None and dt <= cap + LSB

# 档位 -> targetFrameRate 的映射（仅默认值；出处 d2u27_jitter.cs:397/:399）。
# ⛔ 不在这里"猜"：TSV 不含帧节奏列，所以必须由外部给（默认值即驱动源码的常量）。
FPS_DEFAULT = {"A": 60, "B": -1}


class Trace(object):
    __slots__ = ("path", "rows", "header", "lines", "bytes", "sha16", "mtime")

    def __init__(self, path):
        with open(path, "rb") as f:
            raw = f.read()
        self.path = path
        self.bytes = len(raw)
        self.sha16 = hashlib.sha256(raw).hexdigest()[:16]
        self.mtime = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(os.path.getmtime(path)))
        text = raw.decode("utf-8-sig")            # 容忍 BOM
        lines = text.split("\n")
        if lines and lines[-1] == "":
            lines.pop()                            # = .NET ReadAllLines 的"末行换行不算一行"
        self.lines = len(lines)
        self.rows = []
        self.header = None
        for ln in lines:
            ln = ln.rstrip("\r")
            if not ln.strip():
                continue
            p = ln.split("\t")
            if self.header is None:
                self.header = p
                if p[C_SCEN] != "scen":
                    raise SystemExit("unexpected header: %r" % (p,))
                continue
            if len(p) < MIN_COLS:
                continue
            try:
                frame = int(p[C_FRAME])
                dt = float(p[C_DT])
            except ValueError:
                continue
            self.rows.append((p[C_SCEN], frame, dt,
                              p[C_SPR] if len(p) > C_SPR else "(none)",
                              p[C_CAD] if len(p) > C_CAD else "A"))


def pct(sorted_vals, q):
    """线性插值分位（与 d2u27_judge.py:64 同一实现，保证跨脚本读数可比）。"""
    s = sorted_vals
    if not s:
        return float("nan")
    if len(s) == 1:
        return s[0]
    pos = q * (len(s) - 1)
    lo = int(math.floor(pos))
    hi = int(math.ceil(pos))
    return s[lo] if lo == hi else s[lo] + (s[hi] - s[lo]) * (pos - lo)


def sd_of(v):
    """总体标准差 pstdev —— 与 d2u27_judge.py R4/R6 同口径。"""
    return statistics.pstdev(v) if len(v) > 1 else float("nan")


def cv_of(v):
    m = statistics.fmean(v) if v else float("nan")
    s = sd_of(v)
    if not v or m == 0 or math.isnan(s):
        return float("nan")
    return s / m


def f6(x):
    return "n/a" if x is None or (isinstance(x, float) and math.isnan(x)) else "%.6f" % x


def f4(x):
    return "n/a" if x is None or (isinstance(x, float) and math.isnan(x)) else "%.4f" % x


def groups_of(rows):
    """cad 分组；返回 [(cad, [row,...]), ...]，按该组**第一帧的帧号**排序（= 实际跑动顺序）。"""
    g = {}
    for r in rows:
        g.setdefault(r[4], []).append(r)
    return sorted(g.items(), key=lambda kv: kv[1][0][1])


def scen_runs(grp):
    """把一组按文件顺序切成 (scen, startIdx, length) 连续段。"""
    runs = []
    for i, r in enumerate(grp):
        if runs and runs[-1][0] == r[0]:
            runs[-1][2] += 1
        else:
            runs.append([r[0], i, 1])
    return runs


def spr_behaviour(spr):
    """spr 是玩家 SpriteRenderer 的 sprite 名（d2u27_jitter.cs:541）=> 前缀即动画行为。"""
    return spr.split("_")[0] if spr else "(none)"


def print_m1(grp, cad, thr, out):
    st = [(i, r) for i, r in enumerate(grp) if r[2] >= thr]
    out.append("  [M1] cad=%s  frames=%d  dt>=%.3fs : n=%d" % (cad, len(grp), thr, len(st)))
    if not st:
        return
    runs = scen_runs(grp)
    run_of = {}
    for s, start, ln in runs:
        for i in range(start, start + ln):
            run_of[i] = (s, start, ln)
    for i, r in st:
        s, start, ln = run_of[i]
        out.append("       frame=%-6d cad=%s passIdx=%d/%d frac=%.4f scen=%-10s spr=%-12s"
                   " scenRun=%d/%d scenFrac=%.3f dt=%.6f"
                   % (r[1], cad, i, len(grp), i / float(len(grp)), r[0], r[3],
                      i - start, ln, (i - start) / float(ln - 1) if ln > 1 else 0.0, r[2]))


def leading_block(grp):
    """一组（= 一遍）的**开头连续段**：驱动声明它必须是 idle 场景。
    出处: `case 5: if (_scenFrames++ < 40) return;` => 每遍开场 40 帧静止
    （tools/probes/drivers/d2u27_jitter.cs:845-849）。
    """
    if not grp:
        return None
    scen = grp[0][0]
    n = 0
    for r in grp:
        if r[0] != scen:
            break
        n += 1
    return scen, n, grp[0][1], grp[n - 1][1], spr_behaviour(grp[0][3])


def conflict_runs(grp):
    """`scen` 列与 `spr` 列自相矛盾的行段。

    依据：`spr` = 玩家视图当前 sprite 名（`d2u27_jitter.cs:541`），前缀 `idle` = 站定、
    `run` = 行走；`scen` = 驱动场景机自己的标签（`ScenName()`，`d2u27_jitter.cs:585-595`）。
    ⛔ 注意：运动场景**收尾的停稳块**（`if (p.IsMoving) ...` 后才换场景）本来就是 idle，
    所以这张表里绝大多数条目是合法的；它的用处是**给 [M1] 的位置读数标注可信边界**。
    """
    prev = None
    start = 0
    hits = []
    for i in range(len(grp) + 1):
        cur = None
        if i < len(grp):
            r = grp[i]
            beh = spr_behaviour(r[3])
            bad = (beh == "idle" and r[0] != "idle") or (beh == "run" and r[0] == "idle")
            cur = (r[0], beh) if bad else None
        if cur != prev:
            if prev is not None:
                h = grp[start:i]
                hits.append((start, i - start, prev[0], prev[1], h[0][3], h[-1][3], h[0][1], h[-1][1]))
            prev = cur
            start = i
    return hits


def print_m1a(groups, out):
    out.append("  [M1a] 场景标签审计（判**数据自洽**，不判性能；[M1]/[M3] 的场景归因靠它才可信）")
    out.append("       驱动声明: 每一遍的开场场景 = idle、40 帧"
               "（case 5），出处 tools/probes/drivers/d2u27_jitter.cs:845-849")
    for cad, grp in groups:
        lb = leading_block(grp)
        scen, n, f0, f1, beh = lb
        match = (scen == "idle" and beh == "idle")
        out.append("       cad=%s 开头块 scen=%-10s n=%-4d spr前缀=%-5s frame=%d..%d labelMatchesDeclared=%s"
                   % (cad, scen, n, beh, f0, f1, "True" if match else "False"))
    out.append("       scen 与 spr 矛盾的行段（含合法的停稳块）:")
    for cad, grp in groups:
        hits = conflict_runs(grp)
        out.append("       cad=%s n=%d" % (cad, len(hits)))
        for (st, ln, scen, beh, spr0, spr1, fr0, fr1) in hits:
            out.append("         passIdx=%d..%d n=%-4d scen=%-10s spr前缀=%-5s spr=%s..%s frame=%d..%d"
                       % (st, st + ln - 1, ln, scen, beh, spr0, spr1, fr0, fr1))


def print_m2(groups, cap_of, out):
    out.append("  [M2] 两档 dt 分布并排（sd = pstdev，与 judge R4/R6 同口径）")
    out.append("       %-4s %6s %10s %10s %10s %10s %10s %10s %10s %10s | %10s %10s"
               % ("cad", "n", "mean", "sd", "min", "p50", "p90", "p95", "p99", "max",
                  "lowHalf", "longSub"))
    for cad, grp in groups:
        d = [r[2] for r in grp if r[2] > 0]
        s = sorted(d)
        cap = cap_of.get(cad)
        ncap = sum(1 for x in d if is_low_half(x, cap))
        out.append("       %-4s %6d %10.6f %10.6f %10.6f %10.6f %10.6f %10.6f %10.6f %10.6f | %10d %10d"
                   % (cad, len(d), statistics.fmean(d), sd_of(d), min(d), pct(s, 0.50),
                      pct(s, 0.90), pct(s, 0.95), pct(s, 0.99), max(d), ncap, len(d) - ncap))
    out.append("  [M2] 并排（median | p95 | max）：")
    for cad, grp in groups:
        d = sorted(r[2] for r in grp if r[2] > 0)
        out.append("       %-4s %10.6f | %10.6f | %10.6f" % (cad, pct(d, 0.50), pct(d, 0.95), max(d)))
    # 组内趋势（前半/后半 sd）：驱动把 A/B 顺序对调的依据就是这个"暖机/收敛"迹象
    # （出处 tools/probes/drivers/d2u27_jitter.cs:810-817），所以分布读数必须带上它，
    # 否则"A 更稳"可能是"谁先跑谁更抖"。⛔ 只报两个 sd，不设阈值。
    out.append("  [M2] 组内趋势（前半 sd | 后半 sd；顺序对调的依据见 d2u27_jitter.cs:810-817）")
    for cad, grp in groups:
        d = [r[2] for r in grp if r[2] > 0]
        h = len(d) // 2
        out.append("       %-4s firstHalf(sd=%s) | secondHalf(sd=%s)"
                   % (cad, f6(sd_of(d[:h])), f6(sd_of(d[h:]))))
    # 贴顶比例：用来判"封顶是不是**硬**的"。若 A 真的被 60fps 钳住，`|dt-cap|<=1%*cap` 应当是
    # 主峰（接近 100%）；实测 u27v9 A 只有 9.9%，而 43.8% 的帧**比 cap 还快**
    # ⇒ 说"A 被削峰/尾部分布被截断"是不成立的，能成立的说法是"**均值**被钉在 60fps"。
    # ⛔ ±1% 这个带宽引自 teammate 的**采前写死**口径（ab_trend.py:50），不是本脚本自选。
    out.append("  [M2] 贴顶比例（判封顶是'硬'还是'只钉住均值'）")
    out.append("       ±1% 带宽引自 tools/probes/measure/ab_trend.py:50 (TOUCH_REL=0.01，采前写死)；⛔ 本脚本不自选带宽")
    for cad, grp in groups:
        d = [r[2] for r in grp if r[2] > 0]
        cap = cap_of.get(cad)
        if cap is None or not d:
            out.append("       %-4s 无封顶（targetFrameRate<0）=> 贴顶比例不适用 n=%d" % (cad, len(d)))
            continue
        le = sum(1 for x in d if is_low_half(x, cap))
        band = sum(1 for x in d if abs(x - cap) <= 0.01 * cap)
        fast = sum(1 for x in d if x < cap * 0.99)
        out.append("       %-4s n=%-5d dt<=capPeriod: %d(%.1f%%) | |dt-cap|<=1%%cap: %d(%.1f%%)"
                   " | dt<capPeriod*0.99: %d(%.1f%%)"
                   % (cad, len(d), le, 100.0 * le / len(d), band, 100.0 * band / len(d),
                      fast, 100.0 * fast / len(d)))


def m3_table(groups, cap_of):
    """(scen, spr) -> {cad: (未触顶样本, 全样本)}；只有未触顶样本参与 sd/CV。"""
    tab = {}
    for cad, grp in groups:
        cap = cap_of.get(cad)
        for r in grp:
            if r[2] <= 0:
                continue
            e = tab.setdefault((r[0], r[3]), {})
            allv, un = e.setdefault(cad, ([], []))
            allv.append(r[2])
            if not is_low_half(r[2], cap):
                un.append(r[2])
    return tab


def print_m3b(groups, cap_of, scen_key, spr_prefix, out):
    """两口径对照（team-lead 2026-09-24 裁定的那件事）：**同一帧集**上，"两种极性"各剩多少、sd/CV 各多少。
      甲 = `ab_trend.py` 口径 —— 剔 `abs(dt - 1/target) <= 1%*(1/target)`（= 削峰后还剩多少离散度）；
      乙 = 本脚本口径 —— 剔低半区 `dt <= 1/target + 半格`（剩下的 = **长帧子集**）。
    ⛔ 两个都不是判据、都不自设阈值（甲的带宽引自 `ab_trend.py:50`）；这里只是把
    "同一句'只在未触顶帧上算'可以落成两个不同的量"这一步**显式化**，避免结论反号被读成矛盾。"""
    out.append("  [M3b] 两口径对照（帧集 = scen==%s and spr.startswith(%s)；两档并排）"
               % (scen_key, spr_prefix))
    out.append("       甲 ab_trend.py 口径: 剔 |dt - 1/target| <= 1% * (1/target)  [带宽出处 ab_trend.py:50]")
    out.append("       乙 本脚本口径:      剔 dt <= 1/target + %g(半格)；剩下的 = 长帧子集(dt > 目标周期)" % LSB)
    out.append("       %-4s %8s | %-32s | %-32s"
               % ("cad", "n(all)", "甲 band-excluded", "乙 low-half excluded(长帧子集)"))
    for cad, grp in groups:
        cap = cap_of.get(cad)
        d = [r[2] for r in grp if r[2] > 0 and r[0] == scen_key and r[3].startswith(spr_prefix)]
        if not d:
            out.append("       %-4s %8d | %-32s | %-32s" % (cad, 0, "-", "-"))
            continue
        if cap is None:
            a = b = d
        else:
            a = [x for x in d if abs(x - cap) > 0.01 * cap]
            b = [x for x in d if not is_low_half(x, cap)]

        def cell(v):
            return "n=%-5d sd=%s CV=%s" % (len(v), f6(sd_of(v)), f4(cv_of(v)))

        out.append("       %-4s %8d | %-32s | %-32s" % (cad, len(d), cell(a), cell(b)))


def print_m3(groups, cap_of, top, out):
    out.append("  [M3] 同（场景, 行为）的 sd / CV —— **只在长帧子集（dt > 目标周期）上算**")
    out.append("       剔除 = 低半区帧 dt <= 1/targetFrameRate + %g（半格：dt 按 0.###### 落盘，"
               "d2u27_jitter.cs:552）" % LSB)
    out.append("       A: 60 => capPeriod=0.0166667s（低半区非空）；B: -1 => 无目标周期，低半区为空")
    out.append("       出处: tools/probes/drivers/d2u27_jitter.cs:397 (A) / :399 (B)")
    out.append("       ⚠️ 术语（team-lead 2026-09-24）：本行子集叫 长帧子集，⛔ 不叫\"未触顶帧\""
               "（那个词只属 ab_trend.py 的 ±1% 口径）")
    for cad, grp in groups:
        cap = cap_of.get(cad)
        d = [r[2] for r in grp if r[2] > 0]
        un = [x for x in d if not is_low_half(x, cap)]
        out.append("       pool cad=%s : all n=%-5d sd=%s CV=%s | untouched n=%-5d (excluded=%d) sd=%s CV=%s"
                   % (cad, len(d), f6(sd_of(d)), f4(cv_of(d)), len(un), len(d) - len(un),
                      f6(sd_of(un)), f4(cv_of(un))))
    cads = [c for c, _ in groups]
    # ⚠️ 口径（必须与读数一起看，否则会误读）：A 的"未触顶"子集 = dt > capPeriod 的**慢帧**
    #    （被节流器钳住的快帧全被剔掉）⇒ 它与 B 的"全部帧"**不是同一个抽样分布**；
    #    这正是本批不可跨档比 sd/CV 的原因（order-confounded + metric-dependent）。
    out.append("       口径提醒: A 的长帧子集只含 dt>capPeriod 的帧（A 另有 48.3% 的帧 <= capPeriod，"
               "其中只有 9.9% 落在 ±1%cap 带内）")
    out.append("         => A **不是硬封顶**、没有可分离的'被钳住'子集；这个过滤只是**换抽样区间**，"
               "不是剔除同一种现象（与 ab_trend.py 的 ±1% 带口径不等价，两套都要报 —— 见 [M3b]）")
    tab = m3_table(groups, cap_of)
    ordered = sorted(tab.items(), key=lambda kv: -sum(len(v[0]) + len(v[1]) for v in kv[1].values()))
    out.append("       （场景, 行为）并排（按样本量降序，最多显示 %d 组；显示条数不是阈值）:" % top)
    hdr = "%-10s %-14s |" % ("scen", "spr")
    for cad in cads:
        hdr += " %-34s |" % (cad + " (untouched)")
    out.append("       " + hdr)
    shown = 0
    hidden = 0
    for (scen, spr), per in ordered:
        if shown >= top:
            hidden += 1
            continue
        shown += 1
        line = "       %-10s %-14s |" % (scen, spr)
        for cad in cads:
            v = per.get(cad)
            if v is None:
                line += " %-34s |" % "-"
            else:
                allv, un = v
                line += " %-34s |" % ("n=%-5d(excl=%d) sd=%s CV=%s"
                                      % (len(un), len(allv) - len(un), f6(sd_of(un)), f4(cv_of(un))))
        out.append(line)
    if hidden:
        out.append("       ... %d 组未显示（仅显示上限，非判据）" % hidden)


def print_ref(cur, ref, thr, out):
    out.append("  [M1-REF] 与前一批逐条配对（键 = cad 档位；按 本遍归一化位置 就近配对）")
    out.append("       ref=%s sha16=%s rows=%d" % (ref.path, ref.sha16, len(ref.rows)))
    cg = dict(groups_of(cur.rows))
    rg = dict(groups_of(ref.rows))
    for cad in sorted(set(cg) | set(rg)):
        c = cg.get(cad, [])
        r = rg.get(cad, [])
        cs = [(i, x) for i, x in enumerate(c) if x[2] >= thr]
        rs = [(i, x) for i, x in enumerate(r) if x[2] >= thr]
        out.append("       cad=%s : cur n=%d ref n=%d" % (cad, len(cs), len(rs)))
        if not cs or not rs:
            continue
        pair = 0
        same_scen = 0
        df = []
        for i, x in cs:
            fx = i / float(len(c)) if c else 0.0
            j, y = min(rs, key=lambda t: abs((t[0] / float(len(r)) if r else 0.0) - fx))
            fy = j / float(len(r)) if r else 0.0
            same = "Y" if x[0] == y[0] else "N"
            same_spr = "Y" if x[3] == y[3] else "N"
            out.append("         cur frame=%-6d passIdx=%d/%d frac=%.4f scen=%-10s spr=%-12s"
                       " -> ref frame=%-6d passIdx=%d/%d frac=%.4f scen=%-10s spr=%-12s"
                       " dFrac=%+.4f sameScen=%s sameSpr=%s"
                       % (x[1], i, len(c), fx, x[0], x[3], y[1], j, len(r), fy, y[0], y[3],
                          fx - fy, same, same_spr))
            pair += 1
            same_scen += 1 if x[0] == y[0] else 0
            df.append(abs(fx - fy))
        out.append("         => pairs=%d sameScen=%d/%d |dFrac| min=%.4f max=%.4f"
                   % (pair, same_scen, pair, min(df), max(df)))
    out.append("       注: 两批的跑动顺序不同（v10 `BFirst=true`）时，cad 标签仍是可比的语义键，"
               "但位置会比较跨顺序，读数需与 [M1] 的 passIdx 一起看。")


def main():
    # 实测：本机控制台默认 cp936，⛔(U+26D4) 等符号**不在 GB2312 里** ⇒ 直接 UnicodeEncodeError 崩在
    # 打印上（读数本身没问题，是输出编码的问题）。`errors="replace"` 让这类字符退化成 '?'，
    # 读数不再因为一个符号整段丢失；要保真字符请 `set PYTHONIOENCODING=utf-8`。
    try:
        sys.stdout.reconfigure(errors="replace")
    except Exception:
        pass
    ap = argparse.ArgumentParser()
    ap.add_argument("tsv")
    ap.add_argument("--ref", default="")
    ap.add_argument("--stall-ms", type=float, default=100.0,
                    help="单帧卡顿阈值（ms）；默认 100 由 team-lead 指定，可覆盖")
    ap.add_argument("--a-fps", type=int, default=FPS_DEFAULT["A"],
                    help="档位 A 的 targetFrameRate（默认 60，出处 d2u27_jitter.cs:397）")
    ap.add_argument("--b-fps", type=int, default=FPS_DEFAULT["B"],
                    help="档位 B 的 targetFrameRate（默认 -1 = 无封顶，出处 d2u27_jitter.cs:399）")
    ap.add_argument("--top", type=int, default=25, help="[M3] 表格最多显示多少组（仅显示上限，非判据）")
    ap.add_argument("--out", default="",
                    help="同时把读数写成 UTF-8(无 BOM) 文件；⛔ 别用 PowerShell '>' 重定向"
                         "（PS 5.1 会按 ANSI 解码管道再转 UTF-16 ⇒ 中文全乱、读数作废）")
    ap.add_argument("--pair-scen", default="line", help="[M3b] 两口径对照用的场景（默认 line，与 ab_trend P1 同集）")
    ap.add_argument("--pair-spr", default="run_", help="[M3b] 两口径对照用的 spr 前缀（默认 run_ = 行走帧）")
    a = ap.parse_args()

    # 队纪律②（2026-09-24）：⛔ 写盘一律绝对路径。本机实测进程 CWD = 工作区根
    # `c:\Work\Server\f-v2`，而 PS location 常是项目根 ⇒ 相对 `--out` 会静默落进
    # **另一个 `.ai-tmp` 树**（真存在、不报错）= 假绿形态。故非绝对即 fail-loud。
    if a.out and not os.path.isabs(a.out):
        sys.stderr.write("FAIL --out must be an ABSOLUTE path (got %r, cwd=%s)\n"
                         % (a.out, os.getcwd()))
        return 2
    if not os.path.isabs(a.tsv):
        sys.stderr.write("WARN tsv=%r is RELATIVE; resolved=%s cwd=%s"
                         " -- pass an absolute path (队纪律②)\n"
                         % (a.tsv, os.path.abspath(a.tsv), os.getcwd()))
    thr = a.stall_ms / 1000.0
    cap_of = {}
    for g, fps in (("A", a.a_fps), ("B", a.b_fps)):
        cap_of[g] = (1.0 / fps) if fps and fps > 0 else None
    for cad, grp in groups_of(Trace(a.tsv).rows):        # 任何其它 cad 标签都按"无封顶"处理
        cap_of.setdefault(cad, None)

    cur = Trace(a.tsv)
    groups = groups_of(cur.rows)
    out = []
    out.append("== d2u27 stutter-repro (REPORT-ONLY: numbers only, no PASS/FAIL, no invented threshold) ==")
    out.append("tsv=%s  sha256_16=%s  bytes=%d  lines(ReadAllLines)=%d  mtime=%s"
               % (cur.path, cur.sha16, cur.bytes, cur.lines, cur.mtime))
    # "实际读的是哪个文件" + 进程 CWD：相对路径读错树时这两行会当场对不上（队纪律①③）
    out.append("resolved tsv=%s  cwd=%s"
               % (os.path.abspath(a.tsv), os.getcwd()))
    if a.ref:
        out.append("resolved ref=%s" % os.path.abspath(a.ref))
    if a.out:
        out.append("resolved out=%s" % os.path.abspath(a.out))
    out.append("rows=%d  groups=%s  pass order=%s"
               % (len(cur.rows),
                  ",".join("%s=%d" % (c, len(g)) for c, g in groups),
                  " -> ".join("%s(frame %d)" % (c, g[0][1]) for c, g in groups)))
    for cad, grp in groups:
        cap = cap_of.get(cad)
        out.append("cap cad=%s targetFrameRate=%s capPeriod=%s"
                   % (cad, (a.a_fps if cad == "A" else a.b_fps if cad == "B" else -1),
                      f6(cap) if cap else "none"))
    out.append("")
    for cad, grp in groups:
        print_m1(grp, cad, thr, out)
    print_m1a(groups, out)
    out.append("")
    print_m2(groups, cap_of, out)
    out.append("")
    print_m3(groups, cap_of, a.top, out)
    out.append("")
    print_m3b(groups, cap_of, a.pair_scen, a.pair_spr, out)
    if a.ref:
        out.append("")
        print_ref(cur, Trace(a.ref), thr, out)
    text = "\n".join(out)
    if a.out:
        # UTF-8 无 BOM + '\n'（与本项目其它产物同口径：report-u27.md §6 的读法表）
        with open(a.out, "w", encoding="utf-8", newline="") as f:
            f.write(text + "\n")
    print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())
