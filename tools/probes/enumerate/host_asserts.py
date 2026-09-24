# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/host_asserts.py
#
# **D10 玩法系统（`sys:*`）的「宿主断言取证器」** —— 把 14 个 `Module/*` 子系统的
# 「默认分支 / 边界值 / 异常分支」三态映射到**既有离线宿主**里那条对应的断言，
# 并把它本次运行的**实测值**抄下来 ⇒ 产出 `tools/probes/enumerate/host_asserts.tsv`。
#
# 为什么用这个形状（三个约束同时满足）：
#   · `策划/状态矩阵.tsv` 的 `实测` 列要求「宿主 + 断言名 + **实测值**」⇒ 值只能来自**真跑**；
#   · 矩阵又要求「幂等 / 可复跑」⇒ 判据**不许**在填表时现跑（`fill_offline.py` 必须秒级）；
#   · `tools/probes/hosts/**` 是别的片的冻结区 ⇒ 不许往宿主里加断言。
#   ⇒ 把「真跑一次」与「填表」拆开：本脚本跑宿主并落盘 tsv（判据资产，提交进仓），
#     `fill_offline.py` 只**读** tsv（秒级、幂等）。
#
# 复现：
#     python tools/probes/enumerate/host_asserts.py --run    # 跑全部宿主（~50s）并写 tsv
#     python tools/probes/enumerate/host_asserts.py --check  # 只校验盘上 tsv 与宿主源码是否对得上
#
# 本脚本**只读** `client/**` 与 `tools/probes/hosts/**`，只写
#    `tools/probes/enumerate/host_asserts.tsv` 与 `.ai-tmp/test/hosts-run/`。
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import glob
import codecs
import hashlib
import subprocess

for _s in (sys.stdout, sys.stderr):
    try:
        _s.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
HOSTS = os.path.join(ROOT, 'tools/probes/hosts')
RUN_DIR = os.path.join(ROOT, '.ai-tmp/test/hosts-run')
OUT = os.path.join(HERE, 'host_asserts.tsv')

#: `(sys 全名, 状态, 宿主目录, 断言名里的唯一子串)`。
#: 口径（三态各一条）——「默认分支」= 该系统的典型/缺省输入路径；
#: 「边界值」= 阈值 / 0 / 上限 / 越界那一档；「异常分支」= 非预期分支且**必须留痕**。
SYS_ASSERT = [
    # ── Audio：缺区域信息时按 Town 起 BGM（默认分支）/ 脚步节流的第一档（边界）/ 空载荷走 Warn（异常）
    ('sys:Audio(6 cs)', '默认分支', 'audiocheck', '进图未收到过 AreaChanged'),
    ('sys:Audio(6 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'audiocheck', '刚起步不到 1 步的时间间隔内不出脚步'),
    ('sys:Audio(6 cs)', '异常分支（必须留痕）', 'audiocheck', '空载荷 / 空键不抛异常'),
    # ── Camera：等距跟随（默认）/ 缩放钳到上限（边界）/ 越过下限 ⇒ Warn 一次（异常）
    ('sys:Camera(1 cs)', '默认分支', 'playercheck', '吸附后机位 = 焦点正前方'),
    ('sys:Camera(1 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'playercheck', '缩放上限被钳制到 12'),
    ('sys:Camera(1 cs)', '异常分支（必须留痕）', 'playercheck', '继续缩小不生效并 Warn'),
    # ── Combat：物理伤害公式（默认）/ 命中率夹在 5%~95%（边界）/ 冷却期内再施放被拒（异常）
    ('sys:Combat(6 cs)', '默认分支', 'combatcheck', '物理伤害 = 武器'),
    ('sys:Combat(6 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'combatcheck', '命中率：被夹在 5%~95%'),
    ('sys:Combat(6 cs)', '异常分支（必须留痕）', 'combatcheck', '冷却期内再施放 = false'),
    # ── Flow：站点日志恰一条（默认）/ 已在 Stage 时同区进图被忽略（边界）/ 兜底清扫留 Warn（异常）
    ('sys:Flow(7 cs)', '默认分支', 'flowcheck', '→ Boot 恰好 1 条'),
    ('sys:Flow(7 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'flowcheck', '已在 Stage 时「同区进图」被忽略'),
    ('sys:Flow(7 cs)', '异常分支（必须留痕）', 'flowcheck', '兜底清扫：非本站点面板被关掉'),
    # ── Input：按住状态机（默认）/ 只差 1 格不重寻路（边界）/ 后端不可用 ⇒ 一次 Warn（异常）
    ('sys:Input(2 cs)', '默认分支', 'playercheck', 'Poll 后 PrimaryDown/PrimaryHeld = true'),
    ('sys:Input(2 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'playercheck', 'ShouldRetarget：只差 1 格'),
    ('sys:Input(2 cs)', '异常分支（必须留痕）', 'playercheck', '输入后端不可用时 Poll 不崩'),
    # ── Item：2×3 放得下（默认）/ 满包再入包被拒（边界）/ 满包有可读日志（异常）
    ('sys:Item(8 cs)', '默认分支', 'itemcheck', '2×3 物品放得下'),
    ('sys:Item(8 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'itemcheck', '满包再入包 ⇒ 返回 false'),
    ('sys:Item(8 cs)', '异常分支（必须留痕）', 'itemcheck', '满包时有可读日志'),
    # ── Map：同 seed 幂等（默认）/ 洞穴尺寸落在 [Min,Max]（边界）/ 重试全败走保底布局（异常）
    # mapcheck 的断言名是**插值串**（`Check(same, $"{areas[i]} 同 seed 两次生成完全一致")`，
    #    `Program.cs:136`）⇒ 锚点用插值串的固定片段（它在宿主源码里逐字可见）。
    ('sys:Map(13 cs)', '默认分支', 'mapcheck', '同 seed 两次生成完全一致'),
    ('sys:Map(13 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'mapcheck', '是原版块边长 25 的整数倍且在'),
    ('sys:Map(13 cs)', '异常分支（必须留痕）', 'mapcheck', '重试全部失败后仍产出可玩地图'),
    # ── Monster：Coward 低血逃跑（默认/边界用「打到低血」那一条作前置，异常 = 逃跑优先于出手）
    ('sys:Monster(7 cs)', '默认分支', 'combatcheck', 'Coward：低血会逃跑'),
    ('sys:Monster(7 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'combatcheck', 'Coward：已被打到低血'),
    ('sys:Monster(7 cs)', '异常分支（必须留痕）', 'combatcheck', 'Coward：逃跑时不再攻击'),
    # ── Npc：5 个 NPC 齐全（默认）/ 选项下标 0 恒关闭（边界）/ 远距离点 NPC 不弹（异常）
    ('sys:Npc(3 cs)', '默认分支', 'itemcheck', '5 个 NPC 定义齐全'),
    ('sys:Npc(3 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'itemcheck', '对话选项下标 0 恒为关闭'),
    ('sys:Npc(3 cs)', '异常分支（必须留痕）', 'itemcheck', '点了 NPC 但人还在远处'),
    # ── Player：职业配表可读（默认）/ 属性点不足被拒（边界）/ 图外目标被拒且留日志（异常）
    ('sys:Player(4 cs)', '默认分支', 'playercheck', 'class_c 5 个职业可读'),
    ('sys:Player(4 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'playercheck', '点数不足被拒（返回 false 且不改值）'),
    ('sys:Player(4 cs)', '异常分支（必须留痕）', 'playercheck', '图外目标仍被拒'),
    # ── Quest：初始 NotStarted（默认）/ required=0 不可交付（边界）/ 野外击杀留日志（异常）
    ('sys:Quest(2 cs)', '默认分支', 'itemcheck', '初始状态 = NotStarted'),
    ('sys:Quest(2 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'itemcheck', 'required=0 时不可交付'),
    ('sys:Quest(2 cs)', '异常分支（必须留痕）', 'itemcheck', '野外击杀有可读日志（不是洞穴）'),
    ('sys:Save(2 cs)', '默认分支', 'savecheck', 'Write(Parse(json)) == json'),
    ('sys:Save(2 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'itemcheck', '版本不符仍能读（降级，不崩）'),
    ('sys:Save(2 cs)', '异常分支（必须留痕）', 'savecheck', '坏内容有告警'),
    # ── Skill：1 级可学且无前置（默认）/ 技能点 0 ⇒ 不可学（边界）/ 被动不能绑键（异常）
    ('sys:Skill(5 cs)', '默认分支', 'combatcheck', '找到「1 级可学、无前置」的第一个技能'),
    ('sys:Skill(5 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'combatcheck', '技能点 = 0 时 CanLearn = false'),
    ('sys:Skill(5 cs)', '异常分支（必须留痕）', 'combatcheck', '被动技能不能绑到左右键'),
    # ── View：逐帧动画器起始帧（默认）/ 非循环停末帧（边界）/ 实体根失效只报一次（异常）
    ('sys:View(8 cs)', '默认分支', 'combatcheck', '初始帧 = 0，键名正确'),
    ('sys:View(8 cs)', '边界值（阈值±1 / 0 / 上限 / 超界）', 'combatcheck', '非循环动画停在最后一帧且 Finished=true'),
    ('sys:View(8 cs)', '异常分支（必须留痕）', 'combatcheck', '只报一次：连跑 5 帧只产生 1 条该日志'),
]

OK_MARK = ('[ OK ]', '✅')


def rd(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read()


def run_hosts():
    os.makedirs(RUN_DIR, exist_ok=True)
    hosts = sorted(set(h for _s, _st, h, _a in SYS_ASSERT))
    for h in hosts:
        d = os.path.join(HOSTS, h)
        out = os.path.join(RUN_DIR, h + '.txt')
        print('  [run] %-12s → %s' % (h, os.path.relpath(out, ROOT)))
        with io.open(out, 'w', encoding='utf-8', newline='') as f:
            p = subprocess.run(['dotnet', 'run', '-v', 'q', '--nologo'], cwd=d,
                               stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
            f.write(p.stdout.decode('utf-8', 'replace'))
        if p.returncode != 0:
            print('  ⛔ %s exit=%d（该宿主的行将由 fill_offline 保持留空）' % (h, p.returncode))


def host_src_lines(host, needle):
    """在宿主源码里定位断言名 ⇒ [(相对路径, 行号)]（机械证据：断言**真的**在宿主源码里）。"""
    out = []
    for p in sorted(glob.glob(os.path.join(HOSTS, host, '**', '*.cs'), recursive=True)):
        t = rd(p)
        for i, l in enumerate(t.splitlines()):
            if needle in l:
                out.append((os.path.relpath(p, ROOT).replace('\\', '/'), i + 1))
    return out


def scan_output(host, needle):
    """在宿主输出里定位断言行 ⇒ (状态标记, 断言全名, 实测值原文, 行号) 或 None。"""
    p = os.path.join(RUN_DIR, host + '.txt')
    if not os.path.exists(p):
        return None
    for i, l in enumerate(rd(p).splitlines()):
        if needle not in l:
            continue
        mark = 'OK' if any(m in l for m in OK_MARK) else ('FAIL' if '[FAIL]' in l else None)
        if mark is None:
            continue
        s = l.strip()
        rest = s[s.index(needle) + len(needle):].strip()
        name = rest
        meas = ''
        m = re.search(r'\((.+)\)\s*$', rest)
        if m:
            name = rest[:m.start()].strip()
            meas = m.group(1).strip()
        full = (needle + name).strip() if name else needle
        if not meas:
            meas = s
    # 取**第一个**带标记的行（同一子串可能出现在多行，取首行）
        return (mark, full, meas, i + 1)
    return None


def build():
    rows = []
    for sysent, state, host, needle in SYS_ASSERT:
        src = host_src_lines(host, needle)
        hit = scan_output(host, needle)
        rows.append(dict(sys=sysent, state=state, host=host, needle=needle,
                         src=src, hit=hit))
    return rows


def main():
    if '--run' in sys.argv:
        print('== 跑宿主 ==')
        run_hosts()
    rows = build()
    bad = []
    print('== 逐条（%d 条）==' % len(rows))
    for r in rows:
        s = r['src'][0] if r['src'] else None
        h = r['hit']
        tag = 'OK'
        if not s or not h or h[0] != 'OK':
            tag = 'MISS'
            bad.append(r)
        print('%-16s %-22s %-12s %-4s src=%-46s run=%s' % (
            r['sys'], r['state'], r['host'], tag,
            ('%s:%d' % s) if s else '—',
            ('%s:%d %s' % (r['host'] + '.txt', h[3], h[1][:36])) if h else '—'))
    if '--check' in sys.argv:
        print()
        print('VERDICT: %s（缺 %d 条）' % ('PASS' if not bad else 'FAIL', len(bad)))
        return 0 if not bad else 1

    # 防呆（实测踩过的形状）：`.ai-tmp/test/hosts-run/` 是**一次性**目录、交付前会被清掉
    #   ⇒ 此时本脚本若照旧写盘，会把 42 行**实测值全写成空**（= 把判据资产悄悄弄坏）。
    #   故：有任一条没命中就**不写**，只打印「先跑 --run」。
    if bad and '--force' not in sys.argv:
        print()
        print('⛔ 有 %d 条没命中（多半是 `.ai-tmp/test/hosts-run/` 已被清理）⇒ **不写盘**，'
              '盘上 %s 保持原样。要先跑：python tools/probes/enumerate/host_asserts.py --run'
              % (len(bad), os.path.relpath(OUT, ROOT)))
        return 1

    lines = ['# D10 玩法系统（sys:*）三态 ← 既有离线宿主的断言与本次实测值',
             '# 生成：python tools/probes/enumerate/host_asserts.py --run（只读宿主，不改任何 client/**）',
             '# 列：sys\t状态\t宿主\t断言名\t实测值\t宿主源码(文件:行)\t宿主输出(文件:行)']
    for r in rows:
        s = r['src'][0] if r['src'] else ('', 0)
        h = r['hit']
        lines.append('\t'.join([
            r['sys'], r['state'], r['host'],
            h[1] if h else r['needle'],
            (h[2] if h else ''),
            ('%s:%d' % s) if s else '',
            ('%s:%d' % (r['host'] + '.txt', h[3])) if h else '',
        ]))
    data = '\ufeff' + '\r\n'.join(lines) + '\r\n'
    with io.open(OUT, 'w', encoding='utf-8', newline='') as f:
        f.write(data)
    print()
    print('已写 %s（%d 行数据；缺 %d 条）' % (os.path.relpath(OUT, ROOT), len(rows), len(bad)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
