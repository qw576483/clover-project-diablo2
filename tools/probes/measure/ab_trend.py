# -*- coding: utf-8 -*-
"""A/B 帧节奏 —— REPORT-ONLY 读数器（**不自创阈值、不判 PASS/FAIL**）。

用法：
    python tools/probes/measure/ab_trend.py <d2u27-*.tsv> [<第二份 d2u27-*.tsv>]
    （第二份 = 顺序对调后的另一批，用于 R-REPRO 的"是否同一点重现"对照）

列序（实测 13 列，写死前先按表头数过；踩过一次按 12 列取 `cad` 取到 `spr` ⇒ n=0 假阴）：
    0 scen | 1 frame | 2 dt | 3 lx | 4 ly | 5 rx | 6 ry | 7 cx | 8 cy | 9 grid | 10 crossed | 11 spr | 12 cad

═══════════════════════════════════════════════════════════════════════════════
§1 口径声明（★ **采前写死**，⛔ 不许"看完数才挑口径"）
   实测代价：同一批数据，`R5 max/min` 与 `R5b median/占比` 就是因为事后挑口径而**反号**。
   · **P1（主口径）** = **同场景同行为**（`scen=line` 且 `spr=run_*`）的 dt **绝对 sd**（秒），
     且**只在"未触顶帧"上算**。
   · **P2（辅口径）** = **同一帧集**上的 **CV = sd/mean**（相对口径）。
   · ⛔ **两套必须一起报**；只报一套 = 违规。两套反号 ⇒ 结论记 `metric-dependent`。
   · **P0（对照行）** = 同一帧集**不过滤触顶帧**的 sd/CV —— 明示"过滤"本身带来的影响。
   · **未触顶帧（本器唯一实现，机械定义）**：
       该档有帧率上限（`targetFrameRate > 0`）时，dt 贴在**目标周期**上的帧 = 触顶帧：
           |dt − 1/target| ≤ TOUCH_REL × (1/target)  ⇒ 判为触顶，**剔除**
       无上限档（`targetFrameRate = -1`）**构造上没有触顶帧**。
       档位映射（与驱动 `d2u27_jitter.cs` 的 `ApplyCadence` 同源，⛔ 档位定义只此一处）：
           A = `targetFrameRate=60  + vSyncCount=0`  ⇒ 目标周期 1/60 s
           B = `targetFrameRate=-1  + vSyncCount=1`  ⇒ 无上限（本环境 vSync 未真正节流）
       ⚠️ 该过滤**只对 A 生效**（只有 A 有上限）⇒ 它自身也是**不对称**的：会把 A 的样本从
          `n_all` 削到 `n_keep`（B 不变）。⇒ P1/P2 必须与 **n** 一起读，n 过小则该口径退化。
   · **TOUCH 敏感性行** = 另报 ±0.5 / ±1 / ±2 ms 下的 n 与 P1。这是**明示口径敏感**，
     ⛔ **不是"候选口径"**（不许拿它事后挑一套）。
   · **术语裁定 + 分工（2026-09-24 team-lead；⛔ 别把两个量混成一个词）**：**"未触顶帧"只指本口径**
     （剔 `abs(dt − 1/target) ≤ 1% × (1/target)`）。**本文件 = 口径读数器**（P1/P2/P0 + 三个只报数读数）；
     片 `jitter` 的 `tools/probes/measure/d2u27_stutter_repro.py` = **位置/顺序 + 场景标签自洽审计**
     （它的 `[M1a]` 正是抓出驱动换档缺陷的那一段）⇒ ⛔ **两边都留、不合并、不互相替代**。
     重叠量**实测逐项一致**（`u27v9`；本器 R-DIST/R-SPIKE ↔ 它 `[M2]`/`[M1]`）：
       `n=1545/4045 · mean 0.017204/0.005604 · median 0.016724/0.005175 · max 0.155077/0.104158`；
       `R-SPIKE count=2/2` 且 `frame/scen/spr/dt` 四条**逐字段相同**（`2294 idle/idle_s_4 0.155077`、
       `2638 line/idle_n_1 0.103288`、`6210 clickspam/run_se_3 0.104158`、`8104 clampband/idle_w_0 0.100612`）。
       **唯一差异 = B 的 `p95`：本器 `0.007381` / 它 `0.007380`（末位 1e-6）** = 分位取法末位差，不追。
     它剔的是 `dt ≤ 1/target` 的**低半区** ⇒ 那叫 **"长帧子集（`dt > 目标周期`）"**，⛔ **不叫"未触顶帧"**。
     实测（`u27v9`，同场景同行为 A）：本口径 n=269 sd=0.002345 CV=0.140 / 长帧子集 n=151 sd=0.002182 CV=0.121
     （B 两个集合都不变：n=924 sd=0.001221 CV=0.228）⇒ **两个集合下结论同向**（`sd` ⇒ B 更稳 / `CV` ⇒ A 更稳）
     ⇒ `order-confounded` + `metric-dependent` 的**降级结论不变**。
   · **⚠️ 机制描述已更正（team-lead 2026-09-24，据 `jitter` 实测；本片用同一批数据独立复算、逐值相同）**：
     原写"A 的 dt 被 60fps 目标**封顶** ⇒ 尾部分布被截断（削峰）"**不成立**。实测 `u27v9` A（n=1545）：
       `dt ≤ 1/60` 占 **747 (48.3%)**、`abs(dt − 1/60) ≤ 1% × cap` 仅 **153 (9.9%)**、
       `dt < 0.99 × cap`（**比 60fps 还快**）占 **677 (43.8%)** ⇒ **A 不是硬封顶、没有可分离的"被钳住"子集**。
     ⇒ 正确描述 = **两档不在同一运行区间**（A 被钉在 60fps（p50 = 0.016724s）、B 在 193fps（p50 = 0.005175s）
       ⇒ **帧预算差 3.2×**）。⇒ 本器的"未触顶帧"过滤**只是"换抽样区间"、不是"剔除同一现象"**
       ⇒ 跨档比 `sd`/`CV` **比原先认为的更不成立**。**⛔ 结论方向不变，⛔ 也不据此改生产帧节奏。**
   · 旧 `P3（>2×median 卡峰）` 已**撤下**：`2×median` 是**逐档移动**的阈值（A 中位 16.7ms ⇒ 33ms；
     B 中位 5.2ms ⇒ 10ms）⇒ 本身就是一个隐藏口径；改由 **R-SPIKE 的固定桶 0.100s** 承担，
     读数见 §2（原始 tsv 才是证据，撤下的只是一个会动的判读器，⛔ 未删任何采集数据）。

§2 三个**只报数、不判定**的读数（"真正方向 = 单帧卡顿"，价值可能高于档位本身）
   · **R-DIST** ：两档 dt 分布并排（n / 中位 / 均值 / p95 / max）
   · **R-SPIKE**：dt ≥ 0.100 s 的单帧 —— **次数 + 位置（frame/scen/spr/grid）+ 落在哪些(场景,行为)**
   · **R-REPRO**：给了第 2 份 tsv 时，对照两批的 ≥0.100 s 帧**是否落在同一点（同场景同行为）**
   ⛔ 三段**无 PASS/FAIL、⛔ 无新阈值**：`0.100 s` 只是**报数桶**，不是判据线。
   ⚠️ 帧号**跨档不可比**（同段墙钟场景两档采样密度不同：`u27v9` A 采 1545 帧 / B 采 4045 帧）
      ⇒ "同一点"一律按 **(scen, spr/行为)** 判，⛔ 不按 frame 号。

§3 已知数据缺陷（**采集侧的**，⛔ 不是本器的；`jitter` 实测报出、本片已从代码+数据两路复核）
   · `u27v9` 的**第 2 遍开场静止块**被驱动错标：`frame 4063~4090`（27 行，`cad=B`）的 `scen` 列写的是
     **上一遍的收尾场景**（实测 `scen=clampband` 而 `spr=idle_w_*`），且行数只有 **27**（第 1 遍是 **41**）。
     根因 = `d2u27_jitter.cs` 换档块旧版只复位录制/位置/`_step`，**未复位 `_scen` / `_scenFrames`**
     ⇒ 第 2 遍的 `case 5`（`if (_scenFrames++ < 40) return;`）从上一遍的余值接着数、并把行标成上一遍的 `_scen`。
   · **已修**（本片，`:938-939` 加 `_scen = Scen.Idle; _scenFrames = 0;`）⇒ 编译哨兵 `CSC-EXIT=0`、
     静态不变式 + 已知错误样本自证通过。⚠️ 为什么必须在 `u27v10` 开跑**之前**修：`u27v10` 是 `BFirst = true`
     ⇒ 第 2 遍 = **A**，这次错到 A 头上，而 **A 的 155.077ms 卡峰就落在开场块里**
     ⇒ 不修就会污染 R-SPIKE / R-REPRO 的"对调后是否同一点重现"。
   · **判读 `u27v9` 时的口径**：凡**按 `scen` 分组**的读数，**B 的开场块（那 27 行）不可信**（已被驱动标签污染）。
     ✅ 对本器的影响已逐项核过：**P1/P2/P0 取 `scen=line ∧ spr=run_*`，那 27 行是 `clampband` ⇒ 不进主/辅口径，
     读数不受影响**；**R-DIST 无 scen 过滤** ⇒ 不受影响；受影响的只有 **R-SPIKE/R-REPRO 的 `(scen,spr)` 标签**
     ⇒ 读 v9 的 R-SPIKE 时把"B 的 clampband 那两条要区分是不是开场块"（v9 的两条 ≥100ms 都在 frame 6210/8104，
     远晚于 4090 ⇒ **都不是开场块**，本批结论不变）。
═══════════════════════════════════════════════════════════════════════════════
"""
import hashlib
import io
import os
import statistics as st
import sys

COLS = 13
TOUCH_REL = 0.01                 # 触顶带 = 目标周期 ±1%（★ 采前写死，⛔ 不看数再改）
TARGET_FPS = {'A': 60, 'B': -1}  # 档位映射（= driver `ApplyCadence`；B=-1 ⇒ 无上限 ⇒ 无触顶帧）
SPIKE_S = 0.100                  # R-SPIKE 报数桶（⛔ 不是判据阈值）
SCEN_KEY = 'line'
SPR_KEY = 'run_'


def pct(xs, q):
    xs = sorted(xs)
    return float('nan') if not xs else xs[min(len(xs) - 1, int(len(xs) * q))]


def stat(label, xs):
    if not xs:
        print('%-40s n=0' % label)
        return
    m = st.fmean(xs)
    sd = st.pstdev(xs)
    print('%-40s n=%4d mean=%.6f sd=%.6f CV=%.3f p95=%.6f p99=%.6f max=%.6f'
          % (label, len(xs), m, sd, (sd / m if m else float('nan')), pct(xs, 0.95), pct(xs, 0.99), max(xs)))


def target_period(cad):
    """该档的目标帧周期（秒）；无上限档 ⇒ None（构造上没有触顶帧）。"""
    fps = TARGET_FPS.get(cad)
    return (1.0 / fps) if (fps and fps > 0) else None


def is_touch(dt, cad, tol_s):
    p = target_period(cad)
    return p is not None and abs(dt - p) <= tol_s


def fingerprint(path):
    blob = io.open(path, 'rb').read()
    return hashlib.sha256(blob).hexdigest()[:16], len(blob), os.path.getmtime(path)


def load(path):
    rows = [l.rstrip('\n').split('\t') for l in io.open(path, encoding='utf-8')]
    if not rows or len(rows[0]) < COLS:
        print('BAD-TSV: %s header has %d columns (expected %d)' % (path, len(rows[0]) if rows else 0, COLS))
        return None
    return rows[1:]


def report_file(path, rows):
    sha16, nbytes, mtime = fingerprint(path)
    scen = [r[0] for r in rows]
    spr = [r[11] for r in rows]
    cad = [r[12] for r in rows]
    dt = [float(r[2]) for r in rows]
    n = len(rows)

    print('== ab_trend ==  tsv=%s' % path)
    # ⚠️ 必须打**解析后的绝对路径**：读文件用的是进程 CWD，而 `cd` 只改 shell 的 location
    #    （实测本机 PS location = <项目根> 而进程 CWD = 工作区根 ⇒ 同名文件会被**静默读错**：
    #     同一条 `.ai-tmp/test/u27-heartbeat.txt` cmdlet 读 37674 B / .NET 读 118 B）。
    #    这里把"实际读了哪个文件"印出来，配合下面的 self-fingerprint ⇒ 读错必现形，⛔ 别删这行。
    print('   resolved=%s' % os.path.abspath(path))
    print('   self-fingerprint sha256_16=%s bytes=%d lines=ReadAllLines().Count=%d mtime=%.0f'
          % (sha16, nbytes, n + 1, mtime))

    # ── 组内三分位（暖机/顺序效应的形状）────────────────────────────────────────
    for tag in ('A', 'B'):
        idx = [i for i in range(n) if cad[i] == tag]
        if not idx:
            print('  cad=%s: 无样本' % tag)
            continue
        k = len(idx)
        stat('  %s ALL' % tag, [dt[i] for i in idx])
        for j in range(3):
            seg = idx[j * k // 3:(j + 1) * k // 3]
            stat('  %s seg%d/3(by row order)' % (tag, j + 1), [dt[i] for i in seg])
        stat('  %s walk-only(spr=%s*)' % (tag, SPR_KEY), [dt[i] for i in idx if spr[i].startswith(SPR_KEY)])
        stat('  %s scen=%s' % (tag, SCEN_KEY), [dt[i] for i in idx if scen[i] == SCEN_KEY])
        stat('  %s scen=%s & walk-only' % (tag, SCEN_KEY),
             [dt[i] for i in idx if scen[i] == SCEN_KEY and spr[i].startswith(SPR_KEY)])

    # ── P1/P2（主/辅口径，采前写死）+ P0 对照 + TOUCH 敏感性 ────────────────────
    print('  --- P1/P2 口径（采前写死）: P1=sd(同场景同行为, 未触顶) · P2=CV(同集) · P0=同集不过滤 ---')
    for tag in ('A', 'B'):
        pr = target_period(tag)
        tol = (TOUCH_REL * pr) if pr else 0.0
        same = [i for i in range(n) if cad[i] == tag
                and scen[i] == SCEN_KEY and spr[i].startswith(SPR_KEY)]
        if not same:
            print('  cad=%s: 无同场景同行为样本' % tag)
            continue
        keep = [dt[i] for i in same if not is_touch(dt[i], tag, tol)]
        allx = [dt[i] for i in same]
        p1 = st.pstdev(keep) if keep else float('nan')
        p2 = (p1 / st.fmean(keep)) if keep else float('nan')
        print('  %s target_period=%s touch_band=±%.6fs' % (tag, ('%.6fs' % pr) if pr else 'none(B: no cap)', tol))
        print('     P1(主) sd=%.6f n=%d(未触顶) | P2(辅) CV=%.3f' % (p1, len(keep), p2))
        print('     P0(对照·不过滤) sd=%.6f CV=%.3f n=%d(全量)'
              % (st.pstdev(allx), st.pstdev(allx) / st.fmean(allx), len(allx)))
        sens = []
        for ms in (0.0005, 0.001, 0.002):
            s2 = [dt[i] for i in same if not is_touch(dt[i], tag, ms)]
            sens.append('±%.1fms:n=%d sd=%s' % (ms * 1000, len(s2), ('%.6f' % st.pstdev(s2)) if s2 else 'n/a'))
        print('     TOUCH sensitivity(口径敏感,⛔非候选口径): ' + ' | '.join(sens))

    # ── R-DIST：两档 dt 分布并排（只报数）──────────────────────────────────────
    print('  --- R-DIST 两档 dt 分布并排（只报数）---')
    for tag in ('A', 'B'):
        xs = sorted(dt[i] for i in range(n) if cad[i] == tag)
        if not xs:
            print('     cad=%s: 无样本' % tag)
            continue
        print('     %s n=%d median=%.6f mean=%.6f p95=%.6f max=%.6f'
              % (tag, len(xs), st.median(xs), st.fmean(xs), pct(xs, 0.95), xs[-1]))

    # ── R-SPIKE：dt >= SPIKE_S 的单帧（只报数；SPIKE_S = 报数桶，⛔ 不是判据）────
    print('  --- R-SPIKE dt>=%.3fs 单帧（只报数；%.3fs=报数桶, ⛔ 不是判据）---' % (SPIKE_S, SPIKE_S))
    for tag in ('A', 'B'):
        hits = [r for r in rows if r[12] == tag and float(r[2]) >= SPIKE_S]
        spots = sorted(set('%s/%s' % (r[0], r[11]) for r in hits))
        print('     %s count=%d spots(scen/spr)=%s' % (tag, len(hits), spots if spots else 'none'))
        for r in hits:
            print('        frame=%s dt=%.6f scen=%s spr=%s grid=%s'
                  % (r[1], float(r[2]), r[0], r[11], r[9]))

    # ── 暖机/顺序（趋势比；≈1 = 无趋势）────────────────────────────────────────
    print('  --- warmup/order: sd(last1/3)/sd(first1/3) (≈1 = no trend) ---')
    for tag in ('A', 'B'):
        idx = [i for i in range(n) if cad[i] == tag]
        k = len(idx)
        a = [dt[i] for i in idx[:k // 3]]
        c = [dt[i] for i in idx[2 * k // 3:]]
        if a and c and st.pstdev(a) > 0:
            print('     %s sd(后1/3)/sd(前1/3)=%.3f  mean %.6f -> %.6f  (CV %.3f -> %.3f)'
                  % (tag, st.pstdev(c) / st.pstdev(a), st.fmean(a), st.fmean(c),
                     st.pstdev(a) / st.fmean(a), st.pstdev(c) / st.fmean(c)))
    return sha16


def spike_spots(rows):
    """R-SPIKE/R-REPRO 的纯函数核：dt >= SPIKE_S 的帧 → {'scen/spr@cad'}。
    ⛔ 无阈值判定、无 PASS/FAIL；`cad` 入键是因为同一点在两档里是两次独立观测。"""
    return set('%s/%s@%s' % (r[0], r[11], r[12]) for r in rows if float(r[2]) >= SPIKE_S)


def report_repro(loaded):
    """R-REPRO：两批的 >=SPIKE_S 帧是否落在同一点（scen/spr + 档位；⛔ 不按 frame 号）。"""
    print('== R-REPRO 两批对照：dt>=%.3fs 帧是否落在同一点（scen/spr@cad；⛔ 不按 frame 号）==' % SPIKE_S)
    per = [(path, spike_spots(rows)) for path, rows in loaded]
    for path, spots in per:
        print('   %s -> %s' % (os.path.basename(path), sorted(spots) if spots else 'none'))
    a = per[0][1]
    b = per[1][1]
    print('   same-point(intersection)=%s  only#1=%s  only#2=%s'
          % (sorted(a & b) or 'none', sorted(a - b) or 'none', sorted(b - a) or 'none'))
    sa = set(x.split('/')[0] for x in a)
    sb = set(x.split('/')[0] for x in b)
    print('   scene-level: intersection=%s only#1=%s only#2=%s'
          % (sorted(sa & sb) or 'none', sorted(sa - sb) or 'none', sorted(sb - sa) or 'none'))
    print('   ⛔ 只报数：本段不判 PASS/FAIL，也不判定"是否可行动"。')


def main(argv):
    paths = [p for p in argv if p and not p.startswith('-')]
    bad = [p for p in paths if not os.path.isfile(p)]
    if not paths or bad:
        # ⚠️ 把 cwd 印出来：相对路径按**进程 CWD** 解析（`cd` 不影响它）⇒ 从工作区根跑会静默
        #    去找 `<工作区根>\.ai-tmp\...` 的同名文件。这里选择 **fail-loud（exit 2）** 而非读错，
        #    并把 cwd 摆给调用方 ⇒ 换绝对路径即可。
        print('USAGE: python tools/probes/measure/ab_trend.py <d2u27-*.tsv> [<second.tsv>]'
              '  (missing: %s)  cwd=%s' % (bad, os.getcwd()))
        return 2
    loaded = []
    for path in paths:
        rows = load(path)
        if rows is None:
            return 2
        loaded.append((path, rows))
    for path, rows in loaded:
        report_file(path, rows)
    if len(loaded) >= 2:
        report_repro(loaded)
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
