# -*- coding: utf-8 -*-
"""生成 `策划/对照表.md` —— 1:1 硬标准六维（全局 skill `clover-engine` §5）。

用法（任意目录，脚本自己找仓库根）：
    python tools/probes/measure/build_parity_table.py

本文件 = 「六维对照」这件事的唯一机械判据资产（判据资产按全局 skill §1.8 落 tools/probes/ 并提交）：
删了它就没有可复跑的「对照表」生成口径；`tools/verify.ps1` 的 `six-dim-parity` 检查它的产物。

口径（照 §5 逐字）：
  六个维度 = 布局按原版像素 / 素材必须 A 原版 / 字体照原版 / 色调不加滤镜 / 交互反馈 / 节奏。
  每行三列 = `原版值(出处) | 我们的值 | 差值`。
  差值只允许两种取值：`0`，或 `策划/差异登记.tsv` 里真实存在的登记 id（如 E42）。
  ⛔ 不许模糊措辞；⛔ 不许编数值 —— 每个「原版值」的出处都必须**在盘**（file:line），
     本脚本每次运行都会**逐条复核**该 file:line 存在且该行含锚点串，任一不过 ⇒ exit 1。

输出确定性：无时间戳 / 无随机 ⇒ 连续两次运行逐字节相同（自检：SHA256）。
"""

import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ── 仓库根（向上找含 client/ 且含 策划/ 的那一层）──────────────────────────────
_here = os.path.dirname(os.path.abspath(__file__))
REPO = _here
while REPO and not (os.path.isdir(os.path.join(REPO, 'client'))
                    and os.path.isdir(os.path.join(REPO, '策划'))):
    parent = os.path.dirname(REPO)
    if parent == REPO:
        raise SystemExit('find repo root (client/ + 策划/) failed from %s' % _here)
    REPO = parent

PLAN = os.path.join(REPO, '策划')
CMP = os.path.join(PLAN, '自审对比')
SHOTS = os.path.join(REPO, '.ai-tmp', 'screenshots')
DIFF = os.path.join(PLAN, '差异登记.tsv')
SPEC = os.path.join(PLAN, '验收表.md')
OUT = os.path.join(PLAN, '对照表.md')

SELF_PATH = 'tools/probes/measure/build_parity_table.py'
CMP_MD = '策划/自审对比/启动链路对照.md'          # 启动链路对照
TONE_MD = '策划/自审对比/场景对照.md'             # 场景对照
NUM_MD = '策划/自审对比/数值对照.md'              # 数值对照
DIFF_TSV = '策划/差异登记.tsv'
SPEC_MD = '策划/验收表.md'
UIFLOW_TSV = '.ai-tmp/screenshots/w3_uiflow_audit.tsv'
UIGAME_TSV = '.ai-tmp/screenshots/w3_uigame_audit.tsv'
T0_S2 = '.ai-tmp/screenshots/t0-s2recap_evidence.txt'

DIM_LAYOUT = '布局按原版像素'
DIM_ASSET = '素材必须 A 原版'
DIM_FONT = '字体照原版'
DIM_TONE = '色调不加滤镜'
DIM_FEED = '交互反馈'
DIM_RHYTHM = '节奏'
DIMS = [DIM_LAYOUT, DIM_ASSET, DIM_FONT, DIM_TONE, DIM_FEED, DIM_RHYTHM]

FORBIDDEN = ['基本一致', '大致像', '略有差异', '后续可优化']


def rel(p):
    return os.path.relpath(p, REPO).replace('\\', '/')


def c(relpath, lineno, needle):
    """一个 on-disk 出处复核：relpath 的第 lineno 行必须含 needle。"""
    return (relpath, lineno, needle)


# ── 六维对照行（dim, 原版值(出处), 我们的值, 差值, checks）─────────────────────
ROWS = [
    # ---- 布局按原版像素 ----
    (DIM_LAYOUT,
     '原版 `MainMenu.prefab` 的 `GameMenu/Buttons/*` 272×35（出处 `' + CMP_MD + ':20`）',
     '`Single`/`Quit` 按钮 489.6×63 = 272×35 ×1.8（按高等比 + 水平居中；同出处）',
     '0',
     [c(CMP_MD, 20, '272×35'), c(CMP_MD, 20, '489.6×63')]),
    (DIM_LAYOUT,
     '原版 `ClassSelectMenu/Canvas/SelectHeroClass` (0,267) 493×30（出处 `' + CMP_MD + ':35`）',
     '`Title` 887.4×54 @(0,480.6)（= ×1.8；同出处）',
     '0',
     [c(CMP_MD, 35, '493×30'), c(CMP_MD, 35, '887.4×54')]),
    (DIM_LAYOUT,
     '原版 `ControlPanel.prefab` Background 948×160（出处 `' + UIGAME_TSV + ':2`）',
     '`HudBgSize` = 1706.40×288.00（= 原版 ×K，两轴非等比 0.00%；同出处）',
     '0',
     [c(UIGAME_TSV, 2, '948×160'), c(UIGAME_TSV, 2, '1706.40')]),
    (DIM_LAYOUT,
     '原版 `FrontEnd/{cls}/{CLS}NU1.DC6` 帧 0 原生 118×198 / 58×183 / 86×183 / 82×179 / 89×166（出处 `' + CMP_MD + ':40`）',
     '`PortraitAmazon` 212.4×356.4 = 118×198 ×1.8（共 5 个半身像；同出处）',
     '0',
     [c(CMP_MD, 40, '118×198'), c(CMP_MD, 40, '212.4×356.4')]),

    # ---- 素材必须 A 原版 ----
    (DIM_ASSET,
     '原版 `data/global/ui/Logo/logo.DC6` 帧 0（DIABLO II 火焰字标）原生 319×177（出处 `' + UIFLOW_TSV + ':3`）',
     'sprite=`logo_0`，实测 IHDR 319×177（= 原版 PNG 原样；同出处）',
     '0',
     [c(UIFLOW_TSV, 3, '319×177'), c(UIFLOW_TSV, 3, 'logo_0')]),
    (DIM_ASSET,
     '原版 `MINIMAP/mapicons.DC6` 帧 0（16×16 ×8，白色模板）（出处 `' + UIGAME_TSV + ':77`）',
     '`MiniMap/mapicon_0.png` 实测 IHDR 16×16 → 28.80×28.80（= ×1.8；同出处）',
     '0',
     [c(UIGAME_TSV, 77, 'mapicons.DC6'), c(UIGAME_TSV, 77, '16×16')]),
    (DIM_ASSET,
     '原版 `data/global/ui/MENU/EndGame.dc6` 页 0 四块 256×256 / 64×256 / 256×224 / 64×224 + `Pal.PL2`（出处 `' + SPEC_MD + ':138`）',
     '工程 `endgame_{0..3}.png` 与独立 DC6 解帧 SHA256 全等 4/4（同出处）',
     '0',
     [c(SPEC_MD, 138, 'EndGame.dc6'), c(SPEC_MD, 138, 'endgame_')]),
    (DIM_ASSET,
     '原版平色/模板帧：`Objects/warp` 81 帧（唯一色 #004430）/ `UI/MiniMap` 8 帧（白模板 #F4F4F4）（出处 `' + DIFF_TSV + ':43` = E42）',
     '这些帧不参与落格、也不被任何帧号请求 ⇒ 对成品画面 0 影响',
     'E42',
     [c(DIFF_TSV, 43, '#004430'), c(DIFF_TSV, 43, 'E42')]),

    # ---- 字体照原版 ----
    (DIM_FONT,
     '原版位图字模 `D2/Fonts/font16_chi.png` + `font16_chi_map.txt`（COLS 117 / CELL 13 13）（出处 `' + CMP_MD + ':49`）',
     '启动屏提示行 = 该字模合成模板：模板匹配 IoU 0.9743（同出处 :57）、墨迹色数 11（= 原版调色板 10）（:58）',
     '0',
     [c(CMP_MD, 49, 'COLS 117'), c(CMP_MD, 57, '0.9743'), c(CMP_MD, 58, '不同 RGB 值个数')]),
    (DIM_FONT,
     '原版 `WideButton.prefab:68` `m_FontData.m_FontSize = 18`（Bold）（出处 `' + CMP_MD + ':26`）',
     '字模 `font16` 格子 16×18 → scale 2.025 ⇒ 32.4×36.45 画布px（= 原版字号 18 的位图档口径；同出处）',
     '0',
     [c(CMP_MD, 26, 'm_FontSize = 18'), c(CMP_MD, 26, '32.4×36.45')]),
    (DIM_FONT,
     '原版 `Diablo_light.TTF @18px`：`SINGLE PLAYER` = 145 原版px（出处 `' + CMP_MD + ':70`）',
     '`font16` 位图字模（18/16 换算后）`SINGLE PLAYER` = 129.4 原版px（同出处）',
     'E21',
     [c(CMP_MD, 70, '129.4'), c(CMP_MD, 70, '145'), c(DIFF_TSV, 10, 'E21')]),

    # ---- 色调不加滤镜 ----
    (DIM_TONE,
     '原版瓦片原色（`ACT1/Pal.PL2`），无滤镜（出处 `' + TONE_MD + ':69`）',
     '原版瓦片 PNG 原样、`SpriteRenderer.color = 白`、无色调乘数（同出处）；实机 `[Ui] [原版贴图] ... 色调=RGBA(1.000, 1.000, 1.000, 1.000)`（出处 `' + T0_S2 + ':1057`）',
     '0',
     [c(TONE_MD, 69, 'Pal.PL2'), c(T0_S2, 1057, 'RGBA(1.000, 1.000, 1.000, 1.000)')]),
    (DIM_TONE,
     '原版精英 = 运行期 PL2 调色板变体（逐词缀调色板本机无出处）（出处 `' + DIFF_TSV + ':41` = E40）',
     '本项目不加金色 tint（真素材到位后精英与普通怪同色）',
     'E40',
     [c(DIFF_TSV, 41, 'E40'), c(DIFF_TSV, 41, '金色')]),
    (DIM_TONE,
     '原版河面 = 同 `river.dt1` 的 floor 水瓦片（wall 层纯色瓦片无运行期 PL2 循环）（出处 `' + DIFF_TSV + ':33` = E32）',
     '不叠 `moor_river/028`（白名单恰 1 键；命中 49 格全在 x=47/54，仍为水=阻挡）',
     'E32',
     [c(DIFF_TSV, 33, 'E32'), c(DIFF_TSV, 33, 'moor_river/028')]),

    # ---- 交互反馈 ----
    (DIM_FEED,
     '原版 `WideButton.prefab:61` `m_Color=(0.098,0.098,0.098,1)` = `#191919`（出处 `' + CMP_MD + ':25`）',
     '字模节点 `img.color = RGBA(0.098,0.098,0.098,1)`（同出处）',
     '0',
     [c(CMP_MD, 25, '0.098,0.098,0.098,1')]),
    (DIM_FEED,
     '原版 `WideButtonBlank.dc6` 帧 0+1 常态 / 帧 2+3 按下（出处 `' + CMP_MD + ':24`）',
     '`btn_wide_normal`（常态）/ `btn_wide_pressed`（按下，走 SpriteSwap）（同出处）',
     '0',
     [c(CMP_MD, 24, 'WideButtonBlank.dc6'), c(CMP_MD, 24, 'btn_wide_pressed')]),
    (DIM_FEED,
     '原版 `CURSOR/Cursor.DC6` 5 态（攻击/交互/拾取/不可走 4 态本批素材没有）（出处 `' + UIGAME_TSV + ':88`）',
     '5 态统一显示普通箭头，切到缺口形态逐态一条 Warn',
     'E40',
     [c(UIGAME_TSV, 88, 'Cursor/Cursor.png'), c(UIGAME_TSV, 88, '5 态'), c(DIFF_TSV, 41, 'E40')]),

    # ---- 节奏 ----
    (DIM_RHYTHM,
     '官方游戏循环 = 25 fps（R1 `gameserver.zig:675`）（出处 `' + NUM_MD + ':78`）',
     '`MonsterTuning.LogicFps = 25`（换算基准）（同出处）',
     '0',
     [c(NUM_MD, 78, 'gameserver.zig:675'), c(NUM_MD, 78, 'LogicFps')]),
    (DIM_RHYTHM,
     '官方 `CORPSE_TTL = 500` 帧 ≈ 20 s @25fps（R1 `gameserver.zig:3604-3606`）（出处 `' + NUM_MD + ':81`）',
     '`CorpseLifetimeSeconds = 20`（改前 25；同出处）',
     '0',
     [c(NUM_MD, 81, 'CORPSE_TTL'), c(NUM_MD, 81, 'gameserver.zig:3604-3606')]),
    (DIM_RHYTHM,
     '官方萨满复活冷却 = `aidel 15` 帧 ÷ 25 fps = 0.60 s（出处 `' + NUM_MD + ':82`）',
     '`ShamanReviveCooldownSeconds = 0.60`（改前 4.0；同出处）',
     '0',
     [c(NUM_MD, 82, 'ShamanReviveCooldownSeconds'), c(NUM_MD, 82, '0.6')]),
    (DIM_RHYTHM,
     '官方：仇恨记忆/出手间隔/攻击动画时长/受击硬直/远程保持距离/逃跑触发 不是 txt 数值（在可执行 AI 脚本或 `.cof` 帧数里）（出处 `' + DIFF_TSV + ':14` = E28）',
     '`MonsterTuning` 的 17 条为本项目新增近似（其中 3 条已按官方改为 0.60 / 0.60 / 20）',
     'E28',
     [c(DIFF_TSV, 14, 'E28'), c(DIFF_TSV, 14, 'MonsterTuning')]),
]


def load_lines(relpath):
    p = os.path.join(REPO, relpath.replace('/', os.sep))
    if not os.path.isfile(p):
        return None
    with open(p, 'r', encoding='utf-8', errors='replace') as f:
        return f.read().split('\n')


def registry_base_ids():
    """策划/差异登记.tsv 各行的 leading `E<digits>` 标记。"""
    lines = load_lines(DIFF_TSV)
    ids = set()
    if lines:
        for line in lines:
            m = re.match(r'^\s*\*{0,2}(E[0-9]+)', line)
            if m:
                ids.add(m.group(1))
    return ids


def main():
    problems = []
    cache = {}

    # ① 复核每个 on-disk 出处
    for dim, orig, ours, diff, checks in ROWS:
        for relpath, lineno, needle in checks:
            if relpath not in cache:
                cache[relpath] = load_lines(relpath)
            lines = cache[relpath]
            if lines is None:
                problems.append('source missing: %s (dim %s)' % (relpath, dim))
                continue
            if lineno < 1 or lineno > len(lines):
                problems.append('source line out of range: %s:%d' % (relpath, lineno))
                continue
            if needle not in lines[lineno - 1]:
                problems.append('source drifted: %s:%d lacks "%s"' % (relpath, lineno, needle))

    # ② 复核 差值 列
    reg = registry_base_ids()
    if not reg:
        problems.append('registry ids not found in %s' % DIFF_TSV)
    for dim, orig, ours, diff, checks in ROWS:
        if diff == '0':
            continue
        m = re.match(r'^(E[0-9]+)', diff)
        if not m:
            problems.append('diff not 0 / not a registry id: "%s" (dim %s)' % (diff, dim))
        elif m.group(1) not in reg:
            problems.append('diff id "%s" not in registry (dim %s)' % (diff, dim))

    # ③ 六维每维 >= 1 行
    for d in DIMS:
        if sum(1 for r in ROWS if r[0] == d) < 1:
            problems.append('dimension with no row: %s' % d)

    # ④ 每行三列非空
    for dim, orig, ours, diff, checks in ROWS:
        if not orig.strip() or not ours.strip() or not diff.strip():
            problems.append('row with an empty column (dim %s)' % dim)

    # ⑤ 不出模糊措辞
    blob = '\n'.join('%s %s %s %s' % (d, o, u, x) for d, o, u, x, _ in ROWS)
    for w in FORBIDDEN:
        if w in blob:
            problems.append('forbidden fuzzy wording present: %s' % w)

    if problems:
        for p in problems:
            sys.stdout.write('[X] %s\n' % p)
        sys.stdout.write('build_parity_table: FAILED (%d problem(s)) -- table NOT written\n' % len(problems))
        return 1

    # ── 输出（确定性）────────────────────────────────────────────────────────
    out = []
    out.append('# 对照表 —— 1:1 硬标准六维（skill §5）')
    out.append('')
    out.append('> 由 `' + SELF_PATH + '` 生成（确定性、无时间戳 ⇒ 两次运行逐字节相同）。')
    out.append('> 每行三列：**原版值(出处) | 我们的值 | 差值**。')
    out.append('> 差值只允许两种取值：`0`，或 `策划/差异登记.tsv` 里真实存在的登记 id（如 `E42`）。')
    out.append('> ⚠ 模糊措辞一律不许；每个「原版值」的出处都必须在盘，本脚本每次运行逐条复核（任一漂移 ⇒ 不产出）。')
    out.append('> 六维来源 = 全局 skill `clover-engine` §5「1:1 硬标准六维」。')
    out.append('')
    out.append('| 维度 | 原版值(出处) | 我们的值 | 差值 |')
    out.append('|---|---|---|---|')
    for dim, orig, ours, diff, checks in ROWS:
        out.append('| %s | %s | %s | %s |' % (dim, orig, ours, diff))
    out.append('')

    out.append('## 统计')
    out.append('')
    out.append('| 维度 | 行数 |')
    out.append('|---|---|')
    for d in DIMS:
        # backtick the dim name so this stats row does NOT look like a "dui zhao" data row
        # to the six-dim-parity gate (which keys on a row whose first cell is the dim name).
        out.append('| `%s` | %d |' % (d, sum(1 for r in ROWS if r[0] == d)))
    out.append('')
    n_zero = sum(1 for r in ROWS if r[3] == '0')
    n_e = sum(1 for r in ROWS if r[3] != '0')
    out.append('- 总行数 = **%d**；差值 = `0` 的 **%d** 行，差值 = 登记 id 的 **%d** 行。' % (len(ROWS), n_zero, n_e))
    eids = sorted({r[3] for r in ROWS if r[3] != '0'})
    out.append('- 出现的登记 id：' + ' / '.join('`%s`' % e for e in eids) + '。')
    out.append('')

    text = '\n'.join(out) + '\n'
    for w in FORBIDDEN:
        if w in text:
            sys.stdout.write('[X] forbidden fuzzy wording leaked into the output: %s\n' % w)
            return 1

    with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)

    sys.stdout.write('[OK] wrote %s : %d row(s) over %d dim(s), diff=0 %d / registry-id %d\n'
                     % (rel(OUT), len(ROWS), len(DIMS), n_zero, n_e))
    sys.stdout.write('[OK] all %d on-disk source check(s) passed; registry ids = %d\n'
                     % (sum(len(r[4]) for r in ROWS), len(reg)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
