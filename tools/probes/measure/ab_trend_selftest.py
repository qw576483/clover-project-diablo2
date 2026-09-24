# -*- coding: utf-8 -*-
"""`ab_trend.py` 的离线自证（秒级，不占 Play）—— 自证的是**读数器**，⛔ 不是给数据判 PASS/FAIL。

为什么要有它：`ab_trend.py` 的 R-REPRO 承担"对调顺序后 ≥100ms 卡帧是否落在同一点"这条读数，
若它把"不同点"读成"同点"（或反之），"卡顿是否可复现"的结论就是假的。
⇒ 给两种夹具：**已知正确样本（必须报出交集）** + **已知不同样本（必须不报假交集）**。
⛔ 本自检不给被测数据设任何阈值/极性；只断言"读数器的集合运算与口径谓词符合声明"。

跑法：  python tools/probes/measure/ab_trend_selftest.py     （exit 0 = 自证通过）
"""
import importlib.util
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def load():
    spec = importlib.util.spec_from_file_location('ab_trend', os.path.join(HERE, 'ab_trend.py'))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def row(scen, frame, dt, spr, cad):
    return [scen, frame, '%.6f' % dt, '0', '0', '0', '0', '0', '0', '(1,1)', '0', spr, cad]


def main():
    ab = load()
    fails = []
    checks = 0

    def ok(cond, what):
        nonlocal checks
        checks += 1
        if not cond:
            fails.append(what)

    # ── 口径谓词：A 有上限 ⇒ 目标周期 = 1/60；B 无上限 ⇒ 构造上无触顶帧 ──
    ok(abs(ab.target_period('A') - 1.0 / 60) < 1e-12, 'target_period(A) must be 1/60')
    ok(ab.target_period('B') is None, 'target_period(B) must be None (no cap)')
    ok(ab.is_touch(1.0 / 60, 'A', ab.TOUCH_REL / 60) is True, 'dt at A cap must count as touch')
    ok(ab.is_touch(1.0 / 60, 'B', 1.0) is False, 'B must have no touch frames')
    ok(ab.is_touch(0.030000, 'A', ab.TOUCH_REL / 60) is False, '30ms is not at A cap')

    # ── R-SPIKE 桶：0.100s 是**报数桶** ⇒ 0.099999 不入桶、0.100000 入桶 ──
    rs = [row('line', '1', 0.099999, 'run_n_0', 'A'), row('line', '2', 0.100000, 'run_n_0', 'A')]
    ok(ab.spike_spots(rs) == {'line/run_n_0@A'}, 'spike bucket boundary (>=0.100)')

    # ── 已知正确样本：两批完全相同 ⇒ 交集 = 全集，only# 两侧皆空 ──
    good = [row('clampband', '10', 0.150000, 'idle_w_0', 'A'),
            row('clickspam', '11', 0.104000, 'run_se_3', 'B')]
    ga, gb = ab.spike_spots(good), ab.spike_spots(list(good))
    ok(ga == gb and ga == {'clampband/idle_w_0@A', 'clickspam/run_se_3@B'}, 'known-good: identical batches')
    ok((ga - gb) == set() and (gb - ga) == set(), 'known-good: no one-sided spots')

    # ── 已知不同样本：批2 多点 + 批1 少点 ⇒ 交集必须恰好是共有那一点 ──
    b1 = [row('clampband', '10', 0.150000, 'idle_w_0', 'A')]
    b2 = [row('clampband', '99', 0.120000, 'idle_w_0', 'A'),
          row('diag', '100', 0.110000, 'run_s_0', 'B')]
    s1, s2 = ab.spike_spots(b1), ab.spike_spots(b2)
    ok((s1 & s2) == {'clampband/idle_w_0@A'}, 'known-diff: intersection must be the shared spot')
    ok((s2 - s1) == {'diag/run_s_0@B'}, 'known-diff: only#2 must be the extra spot')
    ok((s1 - s2) == set(), 'known-diff: only#1 must be empty')

    # ── 跨档：帧号完全不同但同 (scen,spr) ⇒ 键含 cad ⇒ 两档是两次独立观测（不误并）──
    ok(ab.spike_spots([row('idle', '1', 0.150000, 'idle_s_4', 'A')])
       != ab.spike_spots([row('idle', '999999', 0.150000, 'idle_s_4', 'B')]),
       'same scen/spr across cadences must NOT be merged into one spot')

    print('SELFTEST ab_trend checks=%d fail=%d' % (checks, len(fails)))
    for f in fails:
        print('  [FAIL] ' + f)
    return 1 if fails else 0


if __name__ == '__main__':
    sys.exit(main())
