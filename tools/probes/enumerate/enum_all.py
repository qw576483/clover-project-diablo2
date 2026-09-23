# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/enum_all.py
#
# **T0 全量覆盖枚举器**（`SKILL.md` T0 / `patterns/full-coverage-audit.md` §2·§5 /
#   `scaffold/coverage-matrix.md` §1·§4 的实例化）。
#
# 它做什么：**只读盘上真实产物**（读目录 / 读 manifest / 读配表 / 读源码常量 /
#   读已有审计表），按 12+3 个维度产出三张表：
#     ① 策划/实体清单.tsv   `维度 实体 载体/路径 出处 状态数 判据类型 归属片`
#     ② 策划/状态矩阵.tsv   `维度 实体 状态/事件 边界值 期望表现(出处) 实测 结论 证据`
#     ③ 策划/差异登记.tsv   `是什么 为什么 出处 何时消除`
#        （③ 的内容 = 从 `策划/验收表.md` 的「允许的差异」区**逐条搬运**，
#          口径与 `tools/verify.ps1` 的 `allow-diff-registry` 检查项**逐字一致**：
#          行匹配 `^\|\s*\*{0,2}E\d+` 且 ≥5 个非空单元格。）
#
# ⛔ 硬要求（照 `coverage-matrix.md` §4）：
#   · **脚本产出，不许手写清单** —— 本文件里没有任何一条"实体名"是手敲的，
#     全部来自 `discover_*()` 的扫盘结果；
#   · **稳定排序** —— 同一份盘 ⇒ 同一份输出，可 `diff`（排序键 = 维度码表序 → 实体名）；
#   · 每行都能回答：该维度**一共多少实体** / 每个实体的**载体路径**与**出处（文件:行 或资源文件）**
#     / 每个实体的**状态数**（展开后行数）。
#   · **实体键唯一（`(维度,实体)` 不许重复）** —— `tools/verify.ps1` 的 `coverage-rows` 第一条
#     子判是「清单行数 == 矩阵去重实体数」，只要有一个重复键该闸门**恒真红**（填什么都红）。
#     实测有 3 对重复（见下 `disambiguate`），修法 = **把来源行号并入实体名**，
#     ⛔ 不是删行、⛔ 不是改闸门。**只给重复键加后缀** ⇒ 其余实体名一字不变（避免无谓改动矩阵）。
#
# ⛔ 本脚本**不写挂钟时刻**进 .tsv：三张表要求"两次运行逐字节相同"，
#    生成时刻写在 `.ai-tmp/test/t0-red-rows.md` 与本脚本的 stdout（见文件末 `main()`）。
#
# 复现口径：
#     cd <项目根>
#     python tools/probes/enumerate/enum_all.py            # 写三张表 + 打印统计
#     python tools/probes/enumerate/enum_all.py --check    # 只打印统计，不写表（供对账）
#
# 维度码表（照 `scaffold/coverage-matrix.md` §1，⛔ 只用这 15 个码）：
#     D1资源 D2几何 D3材质 D4UI D5动画 D6特效 D7音乐 D8音效 D9碰撞
#     D10逻辑 D11输入 D12流程 S1数值 S2性能 S3设置
#
# 每维度的**实体来源**（脚本注释逐维写明，禁"凭记忆列"）：
#     D1资源   ← D2/**/manifest.json + D2/UI/*/ + D2/Items + D2/Fonts + Sound/{SFX,BGM} + StreamingAssets/Table
#     D2几何   ← 三个生成布局 MapGen{Town,Wild,Cave}Layout.cs 的逐格瓦片键（去重）+ 3 个区域
#     D3材质   ← 同 D1 的 26 个瓦片包 + 18 个单位包 + 12 个 UI 素材目录（每个包的 palette 出处）
#     D4UI     ← .ai-tmp/screenshots/w3_uigame_audit.tsv(89 控件) + w3_uiflow_audit.tsv(78 控件)
#     D5动画   ← .ai-tmp/screenshots/w3_anim_audit.tsv(129 行 = 21 单位 × 动作)
#     D6特效   ← Module/View/ViewModule.cs 的公共表现入口 + Module/Skill/ProjectileView.cs 的伤害系
#     D7音乐   ← Module/Audio/SfxRegistry.cs 的 BgmFiles（键即资源名）
#     D8音效   ← Module/Audio/SfxRegistry.cs 的 SfxFiles（键即资源名）
#     D9碰撞   ← Def/Enums.cs 的 TileKind（11 种）× 三个区域（AreaId）
#     D10逻辑  ← client/Assets/Scripts/Module/* 子目录（玩法系统）+ Core/Events.cs 的事件通道
#     D11输入  ← Def/GameKeyAlias.cs 的按键别名（值 = 引擎 GameKey）
#     D12流程  ← Module/Flow/AppFlow.cs 的 RegisterState/AddTransition 调用（站点 7 + 迁移 10）
#     S1数值   ← client/Assets/StreamingAssets/Table/*.tsv 的每个 id（行）+ 每表 2 条边界
#     S2性能   ← 性能判据本身（帧时间/加载耗时/内存/渲染设备），口径见 experience/perf-triage.md
#     S3设置   ← Core/GameConst.cs 的 SettingKey* 常量（持久化设置项）
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import glob
import json

# ⚠️ 宿主控制台默认是 GBK（本机 cp936）⇒ stdout 打印任何非 GBK 字符（如 '⛔' U+26D4）会抛
#   UnicodeEncodeError，`--check` 直接崩（实测：exit 1，对账数字打不出来）。
#   本函数只把 **stdout/stderr 的编码** 换成 UTF-8（errors='replace'），
#   ⛔ 不改任何表内容 / 不改统计口径 ⇒ 三张 .tsv 的字节不受影响。
def _safe_stdio():
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
SV = os.path.join(ROOT, 'client/Assets/Scripts')
RES = os.path.join(ROOT, 'client/Assets/Resources/Clover')
AUD = os.path.join(ROOT, '.ai-tmp/screenshots')

# 维度码表（顺序 = 排序键；⛔ 15 个码，一个不多一个不少）
DIMS = ['D1资源', 'D2几何', 'D3材质', 'D4UI', 'D5动画', 'D6特效', 'D7音乐', 'D8音效',
        'D9碰撞', 'D10逻辑', 'D11输入', 'D12流程', 'S1数值', 'S2性能', 'S3设置']
DIM_ORDER = {d: i for i, d in enumerate(DIMS)}
# 归属片（照 `scaffold/coverage-matrix.md` §6 的切片建议）
DIM_SLICE = {
    'D1资源': 'D1D2D3资产与几何', 'D2几何': 'D1D2D3资产与几何', 'D3材质': 'D1D2D3资产与几何',
    'D4UI': 'D4UI', 'D5动画': 'D5动画', 'D6特效': 'D6特效',
    'D7音乐': 'D7D8音频', 'D8音效': 'D7D8音频',
    'D9碰撞': 'D9碰撞与可行走', 'D10逻辑': 'D10玩法逻辑', 'D11输入': 'D11输入',
    'D12流程': 'D12流程', 'S1数值': 'S1数值', 'S2性能': 'S2性能', 'S3设置': 'S3设置',
}


def rd(p):
    with io.open(p, encoding='utf-8-sig') as f:
        return f.read()


def rl(p):
    with io.open(p, encoding='utf-8-sig') as f:
        return f.read().splitlines()


def rel(p):
    return os.path.relpath(p, ROOT).replace('\\', '/')


def line_of(text, pos):
    """字节/字符偏移 → 1 基行号（出处写 `文件:行` 用）。"""
    return text.count('\n', 0, pos) + 1


# ═══════════════════════════════════════════════════════════════════════════
# 各维度实体发现（**全部来自扫盘**）
# ═══════════════════════════════════════════════════════════════════════════
def discover_d1():
    """D1 资源：每个素材载体单元（pack 级）+ 每个音频键 + 每张配表 + 配置文件。
    实体来源 = `D2/**/manifest.json`、`D2/UI/*/`、`D2/Items`、`D2/Fonts`、
               `Sound/{SFX,BGM}/*.wav`、`StreamingAssets/Table/*.tsv`。"""
    out = []
    # ① 瓦片/物件包（manifest.json，kind=Tiles/Objects；状态数=3）
    for p in sorted(glob.glob(RES + '/D2/Tiles/*/manifest.json') +
                    glob.glob(RES + '/D2/Objects/*/manifest.json')):
        d = json.load(io.open(p, encoding='utf-8-sig'))
        n = os.path.basename(os.path.dirname(p))
        out.append(dict(ent='pack:%s/%s' % (d.get('kind', '?'), n),
                        carrier=rel(p), origin='%s:1 (kind=%s tileCount=%s)' % (rel(p), d.get('kind'), d.get('tileCount')),
                        states=3))
    # ② 单位包（玩家 5 + 怪物/NPC 13）
    for p in sorted(glob.glob(RES + '/D2/Chars/*/manifest.json') +
                    glob.glob(RES + '/D2/Monsters/*/manifest.json')):
        d = json.load(io.open(p, encoding='utf-8-sig'))
        n = os.path.basename(os.path.dirname(p))
        out.append(dict(ent='unit:%s/%s(%s)' % (d.get('kind', '?'), n, d.get('token', '?')),
                        carrier=rel(p), origin='%s:1 (kind=%s pngCount=%s)' % (rel(p), d.get('kind'), d.get('pngCount')),
                        states=3))
    # ③ UI 素材目录（12 个）
    for p in sorted(glob.glob(RES + '/D2/UI/*')):
        if not os.path.isdir(p):
            continue
        cnt = len(glob.glob(p + '/*.png'))
        out.append(dict(ent='uiarts:%s(%d png)' % (os.path.basename(p), cnt),
                        carrier=rel(p) + '/', origin='%s/*.png (%d 张)' % (rel(p), cnt), states=3))
    # ④ 图标集 / 字体集
    for sub in ('D2/Items', 'D2/Fonts'):
        p = os.path.join(RES, sub)
        files = sorted(glob.glob(p + '/*'))
        out.append(dict(ent='set:%s(%d files)' % (sub.split('/')[-1], len(files)),
                        carrier=rel(p) + '/', origin='%s/* (%d 个文件)' % (rel(p), len(files)), states=3))
    # ⑤ 音频（键即资源名，逐文件一个实体）
    for sub, kind in (('SFX', 'sfx'), ('BGM', 'bgm')):
        for p in sorted(glob.glob(RES + '/Sound/%s/*.wav' % sub)):
            out.append(dict(ent='%s:%s' % (kind, os.path.splitext(os.path.basename(p))[0]),
                            carrier=rel(p), origin=rel(p), states=3))
    # ⑥ 配表
    for p in sorted(glob.glob(ROOT + '/client/Assets/StreamingAssets/Table/*.tsv')):
        out.append(dict(ent='table:%s' % os.path.splitext(os.path.basename(p))[0],
                        carrier=rel(p), origin=rel(p), states=3))
    # ⑦ 运行期配置（存在才登记）
    for p in sorted(glob.glob(ROOT + '/client/Assets/Resources/**/config.json', recursive=True)):
        out.append(dict(ent='config:%s' % os.path.basename(os.path.dirname(p)),
                        carrier=rel(p), origin=rel(p), states=3))
    return out


def resolve_tile(pack, idx):
    """`<pack>/<idx>` → 盘上真实 PNG（先 Tiles 后 Objects）；都不在盘上返回 None。"""
    for sub in ('Tiles', 'Objects'):
        p = os.path.join(RES, 'D2', sub, pack, '%03d.png' % idx)
        if os.path.exists(p):
            return rel(p)
    return None


def _packs_of(s):
    m = re.search(r'static readonly string\[\] Packs\s*=\s*\{(.*?)\};', s, re.S)
    return re.findall(r'"([^"]*)"', m.group(1)) if m else []


def _layout_tiles(s, fname):
    """从生成布局里取出**被引用的瓦片**：{(layer, pack, idx): 首个出处(文件:行)}。
    · Town  ：`GroundRows`（floor 层）/ `ObjectRows`（wall 层），6 字符 = `<packId:3><idx:3>`
    · Wild  ：`Alphabet` 条目 `<kind><class><ground6><object6>`（14 字符）
    · Cave  ：`Alphabet` 条目 `<kind><ground6><object6>`（13 字符）
    ground6 → `Tiles/<pack>`，object6 → `Objects/<pack>`（口径见各文件头注释）。"""
    packs = _packs_of(s)
    found = {}

    def add(layer, pid, idx, pos):
        if 0 <= pid < len(packs):
            found.setdefault((layer, packs[pid], idx), line_of(s, pos))
    if 'Town' in fname:
        for name, layer in (('GroundRows', 'Tiles'), ('ObjectRows', 'Objects')):
            m = re.search(r'static readonly string\[\] %s\s*=\s*\{(.*?)\};' % name, s, re.S)
            for mm in re.finditer(r'"([0-9-]+)"', m.group(1)):
                row = mm.group(1)
                for i in range(0, len(row), 6):
                    seg = row[i:i + 6]
                    if len(seg) == 6 and seg[0] != '-':
                        add(layer, int(seg[0:3]), int(seg[3:6]), m.start() + mm.start())
    else:
        m = re.search(r'Alphabet\s*=\s*\{?(?:.*?)\n\s*new\[\]\s*\{(.*?)\};', s, re.S) or \
            re.search(r'Alphabet\s*=\s*\{(.*?)\};', s, re.S)
        pat = re.compile(r'"([ .#X])[TFWCSOR.]?([0-9-]{6})([0-9-]{6})"')
        for mm in pat.finditer(m.group(1)):
            for g, layer in ((2, 'Tiles'), (3, 'Objects')):
                seg = mm.group(g)
                if seg[0] != '-':
                    add(layer, int(seg[0:3]), int(seg[3:6]), m.start() + mm.start())
    return found


def discover_d2():
    """D2 几何：三个区域的**被引用瓦片**（去重，逐片一个实体）+ 3 个区域本体。
    实体来源 = `Module/Map/MapGen{Town,Wild,Cave}Layout.cs`（生成物，禁手改）。"""
    tiles = {}
    for f in ('MapGenTownLayout.cs', 'MapGenWildLayout.cs', 'MapGenCaveLayout.cs'):
        p = os.path.join(SV, 'Module/Map', f)
        s = rd(p)
        for (layer, pack, idx), ln in _layout_tiles(s, f).items():
            k = '%s/%s/%03d' % (layer, pack, idx)
            tiles.setdefault(k, (rel(p), ln, layer, pack, idx))
    out = []
    for k, (src, ln, layer, pack, idx) in sorted(tiles.items()):
        real = resolve_tile(pack, idx)
        out.append(dict(ent='tile:%s' % k, carrier=real if real else src,
                        origin='%s:%d (键 %s/%03d%s)' % (src, ln, pack, idx,
                                                         '  ⇒ 盘上=' + real if real else '  ⇒ ⛔ 盘上无此文件'),
                        states=3))
    # 三个区域本体（尺寸/边界/出生点/出入口）
    # 区域尺寸的出处 = 生成物文件头里那句「关卡 56×40」（行号**扫出来**，不写死）
    for name, pat, fp in (('Town 罗格营地', r'关卡 56×40', 'MapGenTownLayout.cs'),
                          ('BloodMoor 血腥荒野', r'Size 80x80', 'MapGenWildLayout.cs'),
                          ('DenOfEvil 邪恶洞穴', r'每块 25×25 格', 'MapGenCaveLayout.cs')):
        full = 'client/Assets/Scripts/Module/Map/' + fp
        s = rd(os.path.join(ROOT, full))
        m = re.search(pat, s)
        out.append(dict(ent='area:%s' % name, carrier=full,
                        origin='%s:%d（原版规则表出处；命中串 "%s"）' % (full, line_of(s, m.start()) if m else 1, pat),
                        states=4))
    return out


def discover_d3():
    """D3 材质与贴图表现：每个素材包的**调色板槽**（palette 出处 —— 本项目没有 运行期 PL2
    循环，色值靠解包时烘进 PNG ⇒ 判据是"包级 palette 出处 + 是否退化成纯色占位"）。
    实体来源 = 26 个瓦片/物件包 + 18 个单位包 + 12 个 UI 素材目录。"""
    out = []
    for p in sorted(glob.glob(RES + '/D2/Tiles/*/manifest.json') +
                    glob.glob(RES + '/D2/Objects/*/manifest.json') +
                    glob.glob(RES + '/D2/Chars/*/manifest.json') +
                    glob.glob(RES + '/D2/Monsters/*/manifest.json')):
        d = json.load(io.open(p, encoding='utf-8-sig'))
        n = os.path.basename(os.path.dirname(p))
        out.append(dict(ent='mat:%s/%s' % (d.get('kind', '?'), n),
                        carrier=rel(p), origin='%s ("palette": %s)' % (rel(p), d.get('palette', '?')),
                        states=3))
    for p in sorted(glob.glob(RES + '/D2/UI/*')):
        if os.path.isdir(p):
            out.append(dict(ent='mat:uiarts/%s' % os.path.basename(p),
                            carrier=rel(p) + '/', origin='%s/ (原版 DC6 解出的 PNG，色值已烘)' % rel(p),
                            states=3))
    return out


def _audit_rows(fn):
    p = os.path.join(AUD, fn)
    return [(i + 1, l.split('\t')) for i, l in enumerate(rl(p)) if l.strip()]


def discover_d4():
    """D4 UI/HUD：每个面板 × 每个控件。实体来源 = 已有逐控件审计表
    `w3_uigame_audit.tsv`（游戏内 UI）与 `w3_uiflow_audit.tsv`（流程屏 UI）。

    ⚠️ **同名控件在审计表里出现多行时，实体键会撞车**（实测 2 对：`背包·装备槽·inv_weapons（整幅）`
    在 `w3_uigame_audit.tsv:33` 与 `:36` 各一行、`背包·装备槽·inv_ring_amulet（右半）` 在 `:39`/`:41`）
    ⇒ 把**来源行号**并入实体名消歧：`ui:<面板>·<控件>@<审计文件基名>:<行>`。
    ⛔ 只对**重复键**加后缀（非重复键逐字不变），否则矩阵会被无谓改名。"""
    rows = []
    for fn in ('w3_uigame_audit.tsv', 'w3_uiflow_audit.tsv'):
        for ln, c in _audit_rows(fn):
            if ln == 1:
                continue
            panel, ctrl = c[0].strip(), (c[1].strip() if len(c) > 1 else '?')
            rows.append((fn, ln, 'ui:%s·%s' % (panel, ctrl)))
    cnt = {}
    for _fn, _ln, base in rows:
        cnt[base] = cnt.get(base, 0) + 1
    out = []
    for fn, ln, base in rows:
        ent = base if cnt[base] == 1 else '%s@%s:%d' % (base, os.path.splitext(fn)[0], ln)
        out.append(dict(ent=ent, carrier='.ai-tmp/screenshots/' + fn,
                        origin='.ai-tmp/screenshots/%s:%d' % (fn, ln), states=3))
    # 面板本体（开/关/层级）—— 实体来源 = client/Assets/Scripts/UI/*Panel.cs
    for p in sorted(glob.glob(SV + '/UI/*Panel.cs')):
        out.append(dict(ent='panel:%s' % os.path.splitext(os.path.basename(p))[0],
                        carrier='client/Assets/Scripts/UI/' + os.path.basename(p),
                        origin='client/Assets/Scripts/UI/' + os.path.basename(p) + ':1', states=3))
    return out


def discover_d5():
    """D5 动画：每个单位 × 每个动作。实体来源 = `w3_anim_audit.tsv`（逐单位逐动作，
    含原版方向数/每向帧数出处 + 工程实际引用帧文件数）。"""
    out = []
    for ln, c in _audit_rows('w3_anim_audit.tsv'):
        if ln == 1:
            continue
        out.append(dict(ent='anim:%s·%s' % (c[0].strip(), c[1].strip()),
                        carrier='.ai-tmp/screenshots/w3_anim_audit.tsv',
                        origin='.ai-tmp/screenshots/w3_anim_audit.tsv:%d' % ln, states=4))
    return out


def discover_d6():
    """D6 特效：每个表现入口 / 每个伤害系投射物。
    实体来源 = `Module/View/ViewModule.cs` 的公共表现入口（PlayHit/PlayDeath/ShowFloatingText）
    + `Module/Skill/ProjectileView.cs::ColorOf` 的 5 个伤害系分支。"""
    out = []
    vm = os.path.join(SV, 'Module/View/ViewModule.cs')
    s = rd(vm)
    eps = [('PlayHit', '受击闪白'), ('PlayDeath', '死亡表现'), ('ShowFloatingText', '飘字（伤害数字）')]
    for name, cn in eps:
        m = re.search(r'public [\w<>\[\],\s]+ ' + name + r'\(', s)
        ln = line_of(s, m.start()) if m else 1
        out.append(dict(ent='fx:%s(%s)' % (name, cn), carrier='client/Assets/Scripts/Module/View/ViewModule.cs',
                        origin='client/Assets/Scripts/Module/View/ViewModule.cs:%d' % ln, states=3))
    pv = os.path.join(SV, 'Module/Skill/ProjectileView.cs')
    s2 = rd(pv)
    for dt, pat in (('Fire', r'case DamageType\.Fire:'), ('Cold', r'case DamageType\.Cold:'),
                    ('Lightning', r'case DamageType\.Lightning:'), ('Poison', r'case DamageType\.Poison:'),
                    ('Physical(默认)', r'default: return new Color')):
        m = re.search(pat, s2)
        ln = line_of(s2, m.start()) if m else 1
        out.append(dict(ent='fx:projectile/%s' % dt, carrier='client/Assets/Scripts/Module/Skill/ProjectileView.cs',
                        origin='client/Assets/Scripts/Module/Skill/ProjectileView.cs:%d' % ln, states=3))
    return out


def _sfx_registry():
    p = os.path.join(SV, 'Module/Audio/SfxRegistry.cs')
    s = rd(p)
    sfx = re.search(r'private static readonly Dictionary<string, string> SfxFiles\s*=\s*new Dictionary<string, string>\(StringComparer\.Ordinal\)\s*\{(.*?)\};', s, re.S)
    bgm = re.search(r'private static readonly Dictionary<string, string> BgmFiles\s*=\s*new Dictionary<string, string>\(StringComparer\.Ordinal\)\s*\{(.*?)\};', s, re.S)
    def parse(m):
        res = []
        for line in m.group(1).splitlines():
            mm = re.match(r'\s*\{\s*([A-Za-z0-9_]+)\s*,\s*([^}]+)\}\s*,', line.rstrip())
            if mm:
                val = re.sub(r'\s*\+\s*\w+Extension\s*$', '', mm.group(2)).strip().strip('"')
                res.append((mm.group(1), val, 0))
        return res
    return p, s, parse(sfx), parse(bgm)


def discover_d7():
    """D7 音乐（BGM）：每个 BGM 键。实体来源 = `SfxRegistry.cs` 的 BgmFiles（键即资源名）。"""
    p, s, sfx, bgm = _sfx_registry()
    out = []
    for key, fname, _ in sorted(bgm, key=lambda t: t[1]):
        m = re.search(r'\{ ' + key + r',\s', s)
        out.append(dict(ent='bgm:%s' % fname, carrier=rel(p),
                        origin='client/Assets/Scripts/Module/Audio/SfxRegistry.cs:%d (键 %s → Sound/BGM/%s)'
                               % (line_of(s, m.start()) if m else 1, key, fname), states=4))
    return out


def discover_d8():
    """D8 音效（SFX）：每个音效键 = 一个事件。实体来源 = `SfxRegistry.cs` 的 SfxFiles。"""
    p, s, sfx, bgm = _sfx_registry()
    out = []
    for key, fname, _ in sorted(sfx, key=lambda t: t[1]):
        m = re.search(r'\{ ' + key + r',\s', s)
        out.append(dict(ent='sfx:%s' % fname, carrier=rel(p),
                        origin='client/Assets/Scripts/Module/Audio/SfxRegistry.cs:%d (键 %s → Sound/SFX/%s)'
                               % (line_of(s, m.start()) if m else 1, key, fname), states=3))
    return out


def discover_d9():
    """D9 物理与碰撞：每个区域 × 每个 TileKind（可走性口径唯一来源 `TileKindInfo.IsWalkable`）。
    实体来源 = `Def/Enums.cs` 的 TileKind 枚举 + `AreaId` 枚举。"""
    s = rd(os.path.join(SV, 'Def/Enums.cs'))
    m = re.search(r'enum TileKind\s*\{(.*?)\}', s, re.S)
    kinds = re.findall(r'(\w+)\s*=\s*\d+', m.group(1))
    kln = line_of(s, m.start())
    m2 = re.search(r'enum AreaId\s*\{(.*?)\}', s, re.S)
    areas = re.findall(r'(\w+)\s*=\s*\d+', m2.group(1))
    aln = line_of(s, m2.start())
    out = []
    for a in areas:
        for k in kinds:
            out.append(dict(ent='coll:%s×%s' % (a, k), carrier='client/Assets/Scripts/Def/Enums.cs',
                            origin='client/Assets/Scripts/Def/Enums.cs:%d (TileKind.%s) + :%d (AreaId.%s)' % (kln, k, aln, a),
                            states=3))
    return out


def discover_d10():
    """D10 玩法逻辑：每个玩法系统（Module 子目录）+ 每个事件通道（跨系统接线点）。
    实体来源 = `client/Assets/Scripts/Module/*/`（目录）+ `Core/Events.cs`（`public const string`）。

    ⚠️ **两个常量取同一个字符串值时实体键会撞车**（实测 `Events.StatePause` 与
    `Events.TriggerPause` 都 = `"Pause"` ⇒ `evt:Pause` ×2）⇒ 把**来源行号**并入实体名消歧：
    `evt:<值>@Events.cs:<行>`。⛔ 只对重复值加后缀。"""
    out = []
    for p in sorted(glob.glob(SV + '/Module/*')):
        if os.path.isdir(p):
            n = os.path.basename(p)
            files = sorted(glob.glob(p + '/*.cs'))
            out.append(dict(ent='sys:%s(%d cs)' % (n, len(files)),
                            carrier='client/Assets/Scripts/Module/%s/' % n,
                            origin='client/Assets/Scripts/Module/%s/*.cs (%d 个)' % (n, len(files)), states=3))
    evp = os.path.join(SV, 'Core/Events.cs')
    s = rd(evp)
    found = [(m.group(1), m.group(2), line_of(s, m.start()))
             for m in re.finditer(r'public const string (\w+)\s*=\s*"([^"]+)"', s)]
    vcnt = {}
    for _nm, val, _ln in found:
        vcnt[val] = vcnt.get(val, 0) + 1
    for nm, val, ln in found:
        ent = 'evt:%s' % val if vcnt[val] == 1 else 'evt:%s@Events.cs:%d' % (val, ln)
        out.append(dict(ent=ent, carrier='client/Assets/Scripts/Core/Events.cs',
                        origin='client/Assets/Scripts/Core/Events.cs:%d (Events.%s)' % (ln, nm),
                        states=2))
    return out


def discover_d11():
    """D11 交互与输入：每个按键别名（值 = 引擎 GameKey）。
    实体来源 = `Def/GameKeyAlias.cs` 的 `public const GameKey <名> = <值>;`。"""
    p = os.path.join(SV, 'Def/GameKeyAlias.cs')
    s = rd(p)
    out = []
    for m in re.finditer(r'public const GameKey (\w+)\s*=\s*(GameKey\.\w+);', s):
        out.append(dict(ent='key:%s(%s)' % (m.group(1), m.group(2).split('.')[1]),
                        carrier='client/Assets/Scripts/Def/GameKeyAlias.cs',
                        origin='client/Assets/Scripts/Def/GameKeyAlias.cs:%d (%s = %s)'
                               % (line_of(s, m.start()), m.group(1), m.group(2)), states=4))
    return out


def discover_d12():
    """D12 流程与场景流转：每个站点 + 每个迁移。
    实体来源 = `Module/Flow/AppFlow.cs` 的 `fsm.RegisterState(<状态>, …)` / `fsm.AddTransition(<触发>, <状态>)`。"""
    p = os.path.join(SV, 'Module/Flow/AppFlow.cs')
    s = rd(p)
    out = []
    seen = set()
    for m in re.finditer(r'fsm\.RegisterState\(([\w.]+)', s):
        st = m.group(1).rsplit('.', 1)[-1]
        if st in seen:
            continue
        seen.add(st)
        out.append(dict(ent='station:%s' % st, carrier='client/Assets/Scripts/Module/Flow/AppFlow.cs',
                        origin='client/Assets/Scripts/Module/Flow/AppFlow.cs:%d (RegisterState %s)'
                               % (line_of(s, m.start()), m.group(1)), states=3))
    for m in re.finditer(r'fsm\.AddTransition\(([\w.]+),\s*([\w.]+)\)', s):
        out.append(dict(ent='trans:%s→%s' % (m.group(1).rsplit('.', 1)[-1], m.group(2).rsplit('.', 1)[-1]),
                        carrier='client/Assets/Scripts/Module/Flow/AppFlow.cs',
                        origin='client/Assets/Scripts/Module/Flow/AppFlow.cs:%d (AddTransition %s → %s)'
                               % (line_of(s, m.start()), m.group(1), m.group(2)), states=2))
    return out


def discover_s1():
    """S1 数值与配表：每张表 × 每个 id。实体来源 = `StreamingAssets/Table/*.tsv` 的数据行。"""
    out = []
    for p in sorted(glob.glob(ROOT + '/client/Assets/StreamingAssets/Table/*.tsv')):
        name = os.path.splitext(os.path.basename(p))[0]
        lines = rl(p)
        n = len([l for l in lines[1:] if l.strip()])
        out.append(dict(ent='tbl:%s(%d id)' % (name, n), carrier=rel(p),
                        origin='%s:1 (表头) + :2..%d (%d 行数据)' % (rel(p), n + 1, n), states=n + 2))
    return out


def discover_s2():
    """S2 性能：每个性能判据。实体来源 = 口径本身（`experience/perf-triage.md` /
    `SKILL.md` §2 第 7 条：渲染设备必须先证伪）。"""
    src = 'C:/Users/xuanyuan/.codebuddy/skills/ai-skill/experience/perf-triage.md'
    return [dict(ent='perf:%s' % n, carrier='（无盘上载体：运行期量测）', origin=src, states=3)
            for n in ('帧时间(ms/frame)', '进图加载耗时(s)', '内存占用(MB)', '渲染设备名(SystemInfo.graphicsDeviceName)')]


def discover_s3():
    """S3 兼容与设置：每个持久化设置项。实体来源 = `Core/GameConst.cs` 的 `SettingKey*` 常量
    + `App/Bootstrap.cs` 的 `video/quality`。"""
    out = []
    p = os.path.join(SV, 'Core/GameConst.cs')
    s = rd(p)
    for m in re.finditer(r'public const string (SettingKey\w+)\s*=\s*"([^"]+)";', s):
        out.append(dict(ent='set:%s' % m.group(2), carrier='client/Assets/Scripts/Core/GameConst.cs',
                        origin='client/Assets/Scripts/Core/GameConst.cs:%d (%s)'
                               % (line_of(s, m.start()), m.group(1)), states=3))
    p2 = os.path.join(SV, 'App/Bootstrap.cs')
    s2 = rd(p2)
    m = re.search(r'"video/quality"', s2)
    out.append(dict(ent='set:video/quality', carrier='client/Assets/Scripts/App/Bootstrap.cs',
                    origin='client/Assets/Scripts/App/Bootstrap.cs:%d' % line_of(s2, m.start()), states=3))
    return out


# ═══════════════════════════════════════════════════════════════════════════
# 展开：每维度的「状态/事件」枚举值（照 `full-coverage-audit.md` §2：状态取枚举值 + 边界值）
# ═══════════════════════════════════════════════════════════════════════════
STATEDEF = {
    # 维度: [(状态/事件, 边界值)]
    'D1资源': [('存在（载体在盘）', '阈值: 0 字节/缺文件 = 未产出'),
              ('被引用（有消费方）', '阈值: 引用 0 处 = 未接线'),
              ('路径可达（引用路径与盘上一致）', '阈值: 1 处指错 = 静默失效')],
    'D2几何': [('存在（瓦片文件在盘）', '阈值: 0 个文件 = 缺瓦片'),
              ('尺寸/朝向（manifest w h orientation）', '边界: orientation 0 / 上限 15'),
              ('落格生成（在区域 layout 中被引用）', '阈值: 被引用 0 格 = 未接线')],
    'D3材质': [('贴图在盘且可载入', '阈值: 0 张 = 纯色占位'),
              ('调色板出处（Pal.PL2 已烘）', '边界: 无 palette 字段 = 退化成纯色'),
              ('非纯色占位（颜色值个数 > 1）', '阈值: 唯一色 = 1 = 平色块（E32 那类）')],
    'D4UI': [('常态', '边界: 面板未打开 = 不渲染'),
             ('交互反馈（悬停/按下）', '边界: 禁用态 = 无反馈'),
             ('禁用/边界态（超长文本·滚动到边界）', '边界: 文本 0 字 / 超框宽度')],
    'D5动画': [('关键帧·起', '边界: 帧数 = 1（单帧）'),
              ('关键帧·中', '边界: 帧数 = 2（无中帧）'),
              ('关键帧·末', '边界: 末帧是否被渲染'),
              ('循环点/结束（Finished）', '阈值: 循环点 = 帧数-1 / 触发回调')],
    'D6特效': [('表现物存在（sprite 非 null）', '阈值: null = 纯色占位'),
              ('时长', '边界: 0s / 上限（FloatTextDuration=1.2s）'),
              ('触发断言（事件 → 表现）', '阈值: 触发 0 次 = 未接线')],
    'D7音乐': [('进入场景播', '边界: 同曲重复进区 = 不重启'),
              ('离开场景停', '边界: 暂停态是否停'),
              ('循环（loop）', '边界: loop=true 到达尾部'),
              ('受音量设置控制（audio/bgm_volume）', '边界: 音量 0 / 1')],
    'D8音效': [('事件触发', '阈值: 触发 0 次 = 事件没挂'),
              ('clip 路径可达（Sound/SFX/<键>）', '阈值: 缺文件 = 静默无声'),
              ('音量组（BGM/SFX 分组）', '边界: 音量 0 / 1')],
    'D9碰撞': [('可走（IsWalkable=true）', '阈值: 该 kind 在三个区域的出现格数 = 0'),
              ('阻挡（IsWalkable=false）', '阈值: 阻挡格可被穿过 = 缺陷'),
              ('边界（图外 Void / 越界格）', '边界: x=-1 / x=Width / y=-1 / y=Height')],
    'D10逻辑': None,  # 见 expand 里按实体类型分派
    'D11输入': [('游戏内上下文（Stage）', '边界: 长按 / 连按'),
               ('菜单上下文（MainMenu/CharSelect）', '边界: 菜单内该键应被屏蔽'),
               ('对话上下文（DialogOpen）', '边界: 对话中移动指令应被吞'),
               ('商店/面板上下文', '边界: 面板内点地面不应移动')],
    'D12流程': None,   # 见 expand
    'S1数值': None,    # 见 expand
    'S2性能': [('帧时间（ms/frame）', '边界: 渲染设备非真 GPU ⇒ 数字无效'),
              ('进图加载耗时（s）', '边界: 20s 看门狗（FlowConst.StageLoadTimeoutSeconds）'),
              ('内存占用（MB）', '边界: 0 / 上限')],
    'S3设置': [('默认值（首次启动）', '边界: 缺键 / 越界值'),
              ('运行期修改生效', '边界: 音量 0 / 画质越界 5'),
              ('重启后仍生效（持久化）', '边界: 冷启动读回 0 / 越界')],
}
D10_SYS_STATES = [('默认分支', '边界: 默认参数路径'),
                  ('边界值（阈值±1 / 0 / 上限 / 超界）', '边界: 阈值上下各一'),
                  ('异常分支（必须留痕）', '阈值: 无日志 = 静默失败')]
D10_EVT_STATES = [('有订阅者（被消费）', '阈值: 0 个订阅者 = 未接线'),
                  ('无订阅者（未接线）', '阈值: 订阅者 ≥1 ⇒ 本行转「一致」')]


def expand(dim, ent, states):
    """把一个实体展开成 状态数 行（返回 [(状态/事件, 边界值, 期望表现(出处))]）。"""
    origin = ent['origin']
    if dim == 'D2几何' and ent['ent'].startswith('area:'):
        base = [('尺寸（与关卡规则表同值）', '边界: 与 GameConst 不一致 ⇒ 必须报错拒绝生成（E12 口径）'),
                ('边界格（四周 Void / 越界）', '边界: x=-1 / x=W / y=-1 / y=H'),
                ('出生点可走（IsWalkable=true）', '边界: 出生点落在阻挡格'),
                ('出入口 / 洞穴入口可达（寻路可达）', '阈值: 可达路径数 = 0')]
        return [(s, b, '原版规则表 `Levels.txt`/`LvlPrest.txt` 为尺寸与布局的出处（出处：%s）' % origin) for s, b in base]
    if dim == 'D10逻辑':
        base = D10_SYS_STATES if ent['ent'].startswith('sys:') else D10_EVT_STATES
        return [(s, b, '跨系统接线必须闭环（`验收表.md` 规则 7「定义了但没人用」）（出处：%s）' % origin) for s, b in base]
    if dim == 'D12流程':
        base = ([('进入（状态机切换 + 站点日志恰一条）', '边界: 重复 Force'),
                 ('该站点的 UI 就位（面板开/关）', '边界: 面板残留'),
                 ('离开（计时器/面板清理、timeScale 复位）', '边界: Pause→Stage 后 timeScale=0 残留')]
                if ent['ent'].startswith('station:') else
                [('触发条件成立 ⇒ 迁移发生', '边界: 未注册触发 = Fsm 抛错'),
                 ('目标站点结果（进/离场副作用）', '边界: 未清理上一站点的定时器')])
        return [(s, b, '站点/迁移语义以 `Core/Events.cs` 的 Fsm 常量为唯一来源（出处：%s）' % origin) for s, b in base]
    if dim == 'S1数值':
        step = state_list_s1(ent)
        return step
    return [(s, b, '判据出处：%s' % origin) for s, b in STATEDEF[dim]]


def state_list_s1(ent):
    """S1：该表的每个 id 一行 + 2 条边界。"""
    ptr, _ = ent['origin'].split(' (')[0], None
    path = os.path.join(ROOT, ent['carrier'])
    lines = [l for l in rl(path) if l.strip()]
    rows = []
    for i, l in enumerate(lines[1:]):
        rid = l.split('\t')[0].strip()
        rows.append(('id=%s' % rid, '—',
                     '逐字段与官方 txt 相等（出处：%s:%d）' % (ent['carrier'], i + 2)))
    rows.append(('边界:id=空/首行之前', '边界: 越界 → 必须回落默认并留痕',
                 '越界必须回落默认值并打一次 Warn（⛔ 不许静默）（出处：%s:1 表头口径）' % ent['carrier']))
    rows.append(('边界:id=max+1（超界）', '边界: 越界 → 必须回落默认并留痕',
                 '越界必须回落默认值并打一次 Warn（⛔ 不许静默）（出处：%s:1 表头口径）' % ent['carrier']))
    return rows


# ═══════════════════════════════════════════════════════════════════════════
# 差异登记：从 验收表.md「允许的差异」区逐条搬运（口径 = verify.ps1 的 allow-diff-registry）
#   + ★「手工登记」补充源 `extra-registry.tsv`（与 策划/差异登记.tsv **同格式**的 4 列数据）
#
#   ★ 为什么要有补充源（实测）：主 agent 裁决的 E42（D3 实心单色 PNG 定性）**不在**
#     `策划/验收表.md` 的「允许的差异」区（该表本轮冻结、不许改）⇒ 若本函数只读验收表，
#     则（a）`策划/差异登记.tsv` 里手工追加的 E42 会在下一次重跑枚举时被**抹掉**、
#     （b）`t0_keyfix.py --verify` 的「差异登记：盘上数据行 vs enum_all 现算」必然不等。
#     把 E42 的**输入**放进本目录的 `extra-registry.tsv`（判据资产，随工具一起提交）⇒
#     枚举器仍然"脚本产出、可重跑、两次逐字节相同"，且盘上 策划/差异登记.tsv == 现算结果。
# ═══════════════════════════════════════════════════════════════════════════
EXTRA_REGISTRY = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'extra-registry.tsv')


def collect_extra_registry():
    """⇒ [(是什么, 为什么, 出处, 何时消除)]（`extra-registry.tsv` 的 4 列数据行；无文件 ⇒ [])。"""
    if not os.path.exists(EXTRA_REGISTRY):
        return []
    out = []
    for l in rl(EXTRA_REGISTRY):
        if not l.strip() or l.startswith('#'):
            continue
        c = [x.strip() for x in l.split('\t')]
        if c[0] in ('是什么', 'what'):
            continue                     # 表头
        if len(c) < 4 or not all(c[:4]):
            raise SystemExit('[fatal] extra-registry.tsv 有非四要素行: %r' % (l[:80],))
        out.append(tuple(c[:4]))
    return out


def collect_diff_registry():
    p = os.path.join(ROOT, '策划/验收表.md')
    lines = rl(p)
    out = []
    for i, l in enumerate(lines):
        if not re.match(r'^\|\s*\*{0,2}E\d+', l):
            continue
        cells = [c.strip() for c in l.rstrip().rstrip('|').split('|')]
        cells = [c for c in cells if c]
        if len(cells) < 5:
            out.append(('【格式异常行】' + l[:80], '（原表该行单元格不足 5 个）', '策划/验收表.md:%d' % (i + 1), '补齐四要素后重跑'))
            continue
        num, what, why, org, when = cells[0], cells[1], cells[2], cells[3], cells[4]
        out.append(('%s · %s' % (num, what), why, '%s ｜ 登记行：策划/验收表.md:%d' % (org, i + 1), when))
    out += collect_extra_registry()
    return out


# ═══════════════════════════════════════════════════════════════════════════
# 写表
# ═══════════════════════════════════════════════════════════════════════════
NULL3 = ('', '', '')  # 实测 / 结论 / 证据 —— 本片**故意留空**（红行；`full-coverage-audit.md` §8：先出表后修）


def header(lines):
    return '\r\n'.join(lines) + '\r\n'


def write_tsv(path, hdrs, rows):
    with io.open(path, 'w', encoding='utf-8-sig', newline='') as f:
        f.write(header(hdrs))
        for r in rows:
            f.write('\t'.join(str(x) for x in r) + '\r\n')


def build():
    """按 15 维度扫盘 ⇒ 三张表的 `(表头, 数据行)`。

    ⛔ **纯函数**：不写盘、不读挂钟 ⇒ 同一份盘调两次必须返回**逐字节相同**的六元组
    （`t0_keyfix.py --verify` 与 `--out-dir` 都靠这条做幂等证明）。
    被 `main()` 与外部判据脚本共用 ⇒ 口径只有一份，不会漂移。"""
    entries = []
    for dim, fn in (('D1资源', discover_d1), ('D2几何', discover_d2), ('D3材质', discover_d3),
                    ('D4UI', discover_d4), ('D5动画', discover_d5), ('D6特效', discover_d6),
                    ('D7音乐', discover_d7), ('D8音效', discover_d8), ('D9碰撞', discover_d9),
                    ('D10逻辑', discover_d10), ('D11输入', discover_d11), ('D12流程', discover_d12),
                    ('S1数值', discover_s1), ('S2性能', discover_s2), ('S3设置', discover_s3)):
        for e in fn():
            e['dim'] = dim
            entries.append(e)
    # 稳定排序：维度码表序 → 实体名（⛔ 不依赖 glob 顺序）
    entries.sort(key=lambda e: (DIM_ORDER[e['dim']], e['ent']))

    # 判据类型默认值（照 `coverage-matrix.md` §1 的四个取值）
    def crit(e):
        if e['dim'] in ('D5动画', 'S1数值'):
            return '参考物比对'
        if e['dim'] in ('D3材质', 'D4UI', 'D6特效'):
            if e['dim'] == 'D6特效' and 'projectile/' in e['ent']:
                return '阻塞(登记)'      # 原版投射物 .dcc 素材缺席（E40-④）
            return '并排图'
        return '脚本断言'

    # ---- ① 实体清单 ----
    gen = 'tools/probes/enumerate/enum_all.py'
    h1 = [
        '# 实体清单（T0 全量覆盖）—— 脚本产出，⛔ 不许手写',
        '# 口径：每行 = 一个盘上真实实体；`状态数` = 该实体在 策划/状态矩阵.tsv 里展开的行数',
        '# 生成脚本：' + gen,
        '# 生成时间：见 .ai-tmp/test/t0-red-rows.md（三张 .tsv 按「稳定排序、两次运行逐字节相同」硬要求不写挂钟时刻）',
        '# 维度码表：D1资源 D2几何 D3材质 D4UI D5动画 D6特效 D7音乐 D8音效 D9碰撞 D10逻辑 D11输入 D12流程 S1数值 S2性能 S3设置',
        '# 判据类型：脚本断言 / 参考物比对 / 并排图 / 阻塞(登记)',
        '维度\t实体\t载体/路径\t出处\t状态数\t判据类型\t归属片',
    ]
    rows1 = [(e['dim'], e['ent'], e['carrier'], e['origin'], e['states'], crit(e), DIM_SLICE[e['dim']]) for e in entries]

    # ---- ② 状态矩阵 ----
    h2 = [
        '# 状态矩阵（T0 全量覆盖）—— 行数必须 == 实体清单的 Σ状态数',
        '# 口径：状态取**枚举值 + 边界值**（阈值上下各一 / 0 / 上限 / 超界）；⛔ 不取"任意帧"、⛔ 不做笛卡尔全组合',
        '# 「实测 / 结论 / 证据」三列本片**故意留空** = 红行（先出表后修，判据由后续按维度的片填）',
        '# 生成脚本：' + gen,
        '# 生成时间：见 .ai-tmp/test/t0-red-rows.md',
        '# 维度码表：D1资源 D2几何 D3材质 D4UI D5动画 D6特效 D7音乐 D8音效 D9碰撞 D10逻辑 D11输入 D12流程 S1数值 S2性能 S3设置',
        '维度\t实体\t状态/事件\t边界值\t期望表现(出处)\t实测\t结论\t证据',
    ]
    rows2 = []
    for e in entries:
        exp = expand(e['dim'], e, e['states'])
        assert len(exp) == e['states'], '%s 状态数不符: %d != %d' % (e['ent'], len(exp), e['states'])
        for item in exp:
            try:
                s, b, want = item
            except ValueError:
                raise ValueError('expand(%s, %s) 返回了非 3 元组: %r' % (e['dim'], e['ent'], item))
            rows2.append((e['dim'], e['ent'], s, b, want) + NULL3)

    # ---- ③ 差异登记 ----
    reg = collect_diff_registry()
    h3 = [
        '# 差异登记（只允许「允许的差异」；四要素缺一 = 未登记 = 不许交付）',
        '# 口径：逐条搬运自 策划/验收表.md 的「允许的差异」区（行匹配 ^\\|\\s*\\*{0,2}E\\d+，= tools/verify.ps1 的 allow-diff-registry 检查口径）',
        '#       + 本目录 extra-registry.tsv 的「手工登记」补充源（主 agent 裁决、验收表冻结不许改的那些，如 E42）',
        '# 生成脚本：' + gen,
        '# 生成时间：见 .ai-tmp/test/t0-red-rows.md',
        '# 维度码表：D1资源 D2几何 D3材质 D4UI D5动画 D6特效 D7音乐 D8音效 D9碰撞 D10逻辑 D11输入 D12流程 S1数值 S2性能 S3设置',
        '是什么\t为什么\t出处\t何时消除',
    ]
    rows3 = [tuple(r) for r in reg]

    return dict(entries=entries, manifest=(h1, rows1), matrix=(h2, rows2), registry=(h3, rows3))


def main():
    _safe_stdio()
    tbl = build()
    (h1, rows1), (h2, rows2), (h3, rows3) = tbl['manifest'], tbl['matrix'], tbl['registry']
    entries = tbl['entries']

    # 输出目录：默认项目根（`--out-dir` 只给判据脚本做「两次运行逐字节相同」的幂等证明用，
    # ⛔ 不改变任何口径/内容；写盘路径 = <out-dir>/策划/*.tsv）。
    out_root = ROOT
    if '--out-dir' in sys.argv:
        out_root = os.path.abspath(sys.argv[sys.argv.index('--out-dir') + 1])

    if '--check' not in sys.argv:
        write_tsv(os.path.join(out_root, '策划/实体清单.tsv'), h1, rows1)
        write_tsv(os.path.join(out_root, '策划/状态矩阵.tsv'), h2, rows2)
        write_tsv(os.path.join(out_root, '策划/差异登记.tsv'), h3, rows3)

    # ---- 统计打印 ----
    print('== 逐维度：实体数 / Σ状态数 / 实体来源 ==')
    srcs = {'D1资源': 'D2/**/manifest.json + D2/UI/*/ + D2/Items + D2/Fonts + Sound/{SFX,BGM} + StreamingAssets/Table',
            'D2几何': 'Module/Map/MapGen{Town,Wild,Cave}Layout.cs（逐格瓦片键去重）+ 3 区域',
            'D3材质': '26 瓦片/物件包 + 18 单位包 + 12 UI 素材目录 的 palette 出处',
            'D4UI': 'w3_uigame_audit.tsv(89) + w3_uiflow_audit.tsv(78) + UI/*Panel.cs',
            'D5动画': 'w3_anim_audit.tsv(129)',
            'D6特效': 'Module/View/ViewModule.cs 入口 + Module/Skill/ProjectileView.cs 伤害系',
            'D7音乐': 'Module/Audio/SfxRegistry.cs::BgmFiles',
            'D8音效': 'Module/Audio/SfxRegistry.cs::SfxFiles',
            'D9碰撞': 'Def/Enums.cs::TileKind × AreaId',
            'D10逻辑': 'Module/* 子目录 + Core/Events.cs',
            'D11输入': 'Def/GameKeyAlias.cs',
            'D12流程': 'Module/Flow/AppFlow.cs 的 RegisterState/AddTransition',
            'S1数值': 'StreamingAssets/Table/*.tsv 的每个 id',
            'S2性能': '性能判据本身（perf-triage.md）',
            'S3设置': 'Core/GameConst.cs::SettingKey* + App/Bootstrap.cs::video/quality'}
    tot_e = tot_s = 0
    missing = []
    for d in DIMS:
        es = [e for e in entries if e['dim'] == d]
        if not es:
            missing.append(d)
        n = len(es)
        s = sum(e['states'] for e in es)
        tot_e += n
        tot_s += s
        print('%s\t%d\t%d\t%s' % (d, n, s, srcs[d]))
    print('TOTAL\t%d\t%d' % (tot_e, tot_s))
    print('维度缺行：%s' % (','.join(missing) if missing else '无（15/15 全有行）'))
    print('实体清单行数=%d  状态矩阵行数=%d  Σ状态数=%d  相等=%s' % (len(rows1), len(rows2), tot_s, len(rows2) == tot_s))
    print('差异登记行数=%d（口径：验收表「允许的差异」区 ^\\|\\s*\\*{0,2}E\\d+ 行 → %d + extra-registry.tsv → %d）'
          % (len(rows3), len(rows3) - len(collect_extra_registry()), len(collect_extra_registry())))
    # 验收表判定行数（INFO，对账用）
    spec = rl(os.path.join(ROOT, '策划/验收表.md'))
    body = [l for l in spec if re.match(r'^\|\s*\d+\s*\|', l)]
    print('INFO 策划/验收表.md 判定行数 = %d' % len(body))

    # ---- 红行预警分析（喂 .ai-tmp/test/t0-red-rows.md；全部来自扫盘）----
    print()
    print('== 红行预警（"用户从未报过"的高概率类别）==')
    missing = [e for e in entries if '⇒ ⛔ 盘上无此文件' in e['origin']]
    print('① D2 被引用但盘上无文件的瓦片 = %d' % len(missing))
    for e in missing[:20]:
        print('     缺: %s' % e['ent'])
    allcs = {}
    for p in glob.glob(SV + '/**/*.cs', recursive=True):
        allcs[rel(p)] = rd(p)
    evp = 'client/Assets/Scripts/Core/Events.cs'
    ev = rd(os.path.join(ROOT, evp))
    orph = []
    for m in re.finditer(r'public const string (\w+)\s*=\s*"([^"]+)";', ev):
        nm, val = m.group(1), m.group(2)
        if re.match(r'^(State|Trigger)', nm):
            continue  # FSM 常量是流程站点/触发器，D12 已枚举
        hits = 0
        for r, txt in allcs.items():
            if r.endswith('Events.cs'):
                continue
            hits += len(re.findall(r'Events\.' + nm + r'\b', txt))
        if hits == 0:
            orph.append(val)
    print('② D10 定义了但 0 处消费的事件 = %d' % len(orph))
    for x in orph:
        print('     未接线: %s' % x)
    skeys = rd(os.path.join(SV, 'Module/Combat/SfxKeys.cs'))
    sk = re.findall(r'public const string (\w+)\s*=', skeys)
    # `CastOf(DamageType)` 是 5 个施法键的**唯一分发入口**（`SkillModule.cs:325`）⇒ 它被引用
    # 即等价于 Cast/CastFire/CastCold/CastLightning/CastPoison 全部接线（不是"未接线"）。
    cast_covered = any(len(re.findall(r'SfxKeys\.CastOf\b', t)) > 0
                       for r, t in allcs.items() if not r.endswith('SfxKeys.cs'))
    orph2 = []
    for nm in sk:
        if nm.startswith('Cast') and cast_covered:
            continue
        hits = 0
        for r, txt in allcs.items():
            if r.endswith(('SfxKeys.cs', 'SfxRegistry.cs')):
                continue
            hits += len(re.findall(r'(?:SfxKeys|SfxRegistry)\.' + nm + r'\b', txt))
        if hits == 0:
            orph2.append(nm)
    print('③ D8 登记了但 0 处触发点引用的战斗音效键 = %d %s（CastOf 分发链已计入）' % (len(orph2), orph2))
    # ④ D11：按键别名定义了但 0 处消费
    al = rd(os.path.join(SV, 'Def/GameKeyAlias.cs'))
    names = re.findall(r'public const GameKey (\w+)\s*=', al)
    direct, indirect, orph3 = [], [], []
    for nm in names:
        hits = 0
        for r, txt in allcs.items():
            if r.endswith('GameKeyAlias.cs'):
                continue
            hits += len(re.findall(r'GameKeyAlias\.' + nm + r'\b', txt))
        if hits > 0:
            direct.append(nm)
        elif re.search(r'return ' + nm + r';', al):
            indirect.append(nm)      # 经表驱动函数（BeltKey/SkillSlotKey）间接接线
        else:
            orph3.append(nm)
    print('④ D11 按键别名：直接消费 %d / 间接（表驱动函数）%d / ⛔ 零引用 %d'
          % (len(direct), len(indirect), len(orph3)))
    print('     ⛔ 零引用清单: %s' % orph3)
    # ⑤ D5：动画审计表里"缺帧"非 0 的行
    anim = [c for ln, c in _audit_rows('w3_anim_audit.tsv') if ln > 1]
    missrows = [c for c in anim if len(c) > 5 and c[5].strip() not in ('0', '')]
    print('⑤ D5 动画审计里"缺帧"非 0 的单位×动作 = %d（总 %d 行）' % (len(missrows), len(anim)))
    for c in missrows[:10]:
        print('     缺帧: %s·%s → %s' % (c[0], c[1], c[5]))
    # ⑥ D9：TileKind 里**没有**在 IsWalkable 的 switch 显式登记的值（会落到 default ⇒ 静默不可走）
    en = rd(os.path.join(SV, 'Def/Enums.cs'))
    m = re.search(r'enum TileKind\s*\{(.*?)\}', en, re.S)
    kinds = re.findall(r'(\w+)\s*=\s*\d+', m.group(1))
    m2 = re.search(r'public static bool IsWalkable\(TileKind kind\)\s*\{(.*?)\n        \}', en, re.S)
    listed = set(re.findall(r'case TileKind\.(\w+):', m2.group(1)))
    print('⑥ D9 未在 IsWalkable 显式登记的 TileKind = %s'
          % ([k for k in kinds if k not in listed] or '无（12/12 全登记）'))


if __name__ == '__main__':
    main()
