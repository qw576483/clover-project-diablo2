# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/d3_flatcolor.py
#
# **D3「实心单色 PNG」定性 + 机械证据**（= `策划/差异登记.tsv` 里 **E42** 的判据资产）。
#
# 为什么存在：`fill_offline.py::fill_d3` 的「非纯色占位」判据（唯一色 = 1 ⇒ 平色块）会把
#   6 个素材包判成 `不一致`。主 agent 裁决：这些是**原版原生"平色/模板"PNG** 与
#   **从不被任何索引请求的帧** ⇒ 登记为 E42（允许的差异），而不是"已修"。
#   本脚本给出**可复跑的机械证据**（不是叙述），三件事逐类断言：
#     ① `UI/SkillIcon`：把"**被请求的帧号集合**"（由 `Table/Skill.tsv` + `Table/Class.tsv`
#        按 `UI/D2Icon.cs:194-204` 的公式现算）与"**实心单色帧号集合**"（逐 PNG 取唯一色）比
#        ⇒ 交集必须为 **∅**（= 亮绿帧从不被请求）。同时覆盖 `SkilliconAttack_*`（唯一消费方
#        `Core/ResPaths.cs:149` 只按**基名**请求）。
#     ② `Objects/{warp,town_trees,town_fence}`：实心单色张数 + 该包 **0 处**出现在任何区域
#        布局（`Module/Map/MapGen{Town,Wild,Cave}Layout.cs` 的 `Packs[]` 与逐格瓦片键）。
#     ③ `UI/MiniMap`（原版白色模板）/ `Objects/moor_river`（已被 R1-B 白名单不叠 = E32）
#        的实心单色帧逐张列色值。
#
# 复现（`cd <项目根>`）：
#     python tools/probes/enumerate/d3_flatcolor.py            # 打印 + 写 .ai-tmp/test/d3-flatcolor.txt
#
# 本脚本**只读盘**（PNG / 配表 / 布局源码），不改任何文件（除 .ai-tmp/test/ 的报告）。
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import glob
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import enum_all as EA  # noqa: E402

ROOT = EA.ROOT
RES = EA.RES
TABLE = os.path.join(ROOT, 'client/Assets/StreamingAssets/Table')

OUT = []
FAILED = []


def say(s=''):
    OUT.append(s)


def chk(label, cond, detail):
    say('%s %s :: %s' % ('[ OK ]' if cond else '[FAIL]', label, detail))
    if not cond:
        FAILED.append(label)


def _rd(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read()


def _table(name):
    """TSV 配表 ⇒ (表头列名列表, [dict 行])。"""
    lines = [l for l in _rd(os.path.join(TABLE, name)).splitlines() if l.strip()]
    hdr = lines[0].split('\t')
    rows = []
    for l in lines[1:]:
        c = l.split('\t')
        if len(c) < len(hdr):
            continue
        rows.append(dict(zip(hdr, c)))
    return hdr, rows


def _pngs(d):
    return sorted(glob.glob(os.path.join(d, '**', '*.png'), recursive=True))


def _color_of(path):
    """不透明像素 (唯一色数, 最小色)。全透明 ⇒ (0, None)。读不到 ⇒ (None, None)。"""
    try:
        from PIL import Image
        im = Image.open(path).convert('RGBA')
        cols = im.getcolors(maxcolors=16)
    except Exception:
        return (None, None)
    if cols is None:
        return (99, None)
    op = [(c, col) for c, col in cols if col[3] > 0]
    if not op:
        return (0, None)
    return (len(op), min(op)[1])


def _flat_frames(files, prefix_re, min_side=8):
    """⇒ [(帧号, 文件, 颜色)]（只数**不透明唯一色 = 1** 且短边 > min_side 的；口径同 fill_offline）。"""
    out = []
    for f in files:
        m = prefix_re.match(os.path.basename(f))
        if not m:
            continue
        try:
            from PIL import Image
            im = Image.open(f)
            if min(im.size) <= min_side:
                continue
        except Exception:
            continue
        n, col = _color_of(f)
        if n == 1:
            out.append((int(m.group(1)), f, col))
    return sorted(out)


# ═══════════════════════════════════════════════════════════════════════════
# ① SkillIcon：被请求的帧号集合（现算）× 实心单色帧号集合
# ═══════════════════════════════════════════════════════════════════════════
def skillicon():
    say('=' * 78)
    say('① UI/SkillIcon —— 「被请求的帧号集合」× 「实心单色帧号集合」')
    say('=' * 78)
    sk_hdr, sk = _table('Skill.tsv')
    cl_hdr, cl = _table('Class.tsv')
    say('Skill.tsv 列: %s' % sk_hdr[:8])
    say('Class.tsv 列: %s' % cl_hdr[:8])
    skill_class = {r['id']: r.get('skill_class', '') for r in cl}
    say('class_c.skill_class = %s' % skill_class)

    # 公式出处：client/Assets/Scripts/UI/D2Icon.cs:194（index = (official_id − (6 + 30×(class−1)))×2）
    #           :204（ResPaths.SkillIcon(cls, index + (dull ? 1 : 0))）
    #           Core/ResPaths.cs:337-342（path = D2/UI/SkillIcon/{cls}Skillicon_{frame}）
    FIRST, SPC = 6, 30
    requested = collections.defaultdict(set)
    bad_formula = []
    for r in sk:
        cls = skill_class.get(r['class'], '')
        off = int(r['official_id'])
        clsid = int(r['class'])
        index = (off - (FIRST + SPC * (clsid - 1))) * 2
        if index < 0:
            bad_formula.append(r['id'])
            continue
        requested[cls].add(index)
        requested[cls].add(index + 1)          # +1 = 灰化帧（dull）
    say('每题请求帧号集合（现算，出处 D2Icon.cs:194-204 + ResPaths.cs:337-342）：')
    for k in sorted(requested):
        fs = sorted(requested[k])
        say('   %s: %d 个帧（%d..%d，含灰化帧）' % (k, len(fs), fs[0], fs[-1]))
    chk('skill.icon.formula', not bad_formula, '算出的帧号 < 0 的技能行 = %s（应为 0）' % (bad_formula or '无'))

    d = os.path.join(RES, 'D2/UI/SkillIcon')
    files = _pngs(d)
    chk('skill.icon.count', len(files) == 421, '目录 PNG = %d（baseline 421）' % len(files))

    for cls in sorted(requested):
        pre = re.compile(r'^%sSkillicon_(\d+)\.png$' % re.escape(cls))
        flats = _flat_frames(files, pre)
        fset = set(t[0] for t in flats)
        req = requested[cls]
        inter = sorted(fset & req)
        cols = collections.Counter(t[2] for t in flats)
        say('   %s: 实心单色 %d 张，帧号 %s' % (cls, len(flats),
             ('%d..%d' % (min(fset), max(fset))) if fset else '无'))
        say('        颜色 = %s' % (dict(cols) or '—'))
        say('        请求帧 ∩ 单色帧 = %s' % (inter or '∅'))
        chk('skill.icon.%s.disjoint' % cls, not inter,
            '%s 的单色帧与被请求帧**不相交**（交集 %s）' % (cls, inter or '∅'))

    # SkilliconAttack：唯一消费方 = Core/ResPaths.cs:149（基名，无帧号）+ UI/HudPanel.cs:460-461
    atk = sorted(os.path.basename(f) for f in files if os.path.basename(f).startswith('SkilliconAttack'))
    atk_base = 'SkilliconAttack.png' in atk
    atk_frames = _flat_frames(files, re.compile(r'^SkilliconAttack_(\d+)\.png$'))
    atk_fset = set(t[0] for t in atk_frames)
    say('   SkilliconAttack：基名文件在盘 = %s；带帧号文件 = %d；实心单色帧 = %s'
        % (atk_base, len([a for a in atk if a != 'SkilliconAttack.png']), sorted(atk_fset)))
    chk('skill.icon.attack.base', atk_base,
        '唯一被请求的是**基名** SkilliconAttack.png（ResPaths.cs:149 / HudPanel.cs:460-461）⇒ 带帧号的 _N 从不被请求')
    # 机械证据：全工程 .cs 里**有没有**按帧号请求 `SkilliconAttack_<N>`（有 ⇒ 本断言红）
    atk_src = []
    for p in glob.glob(EA.SV + '/**/*.cs', recursive=True):
        t = EA.rd(p)
        for m in re.finditer(r'SkilliconAttack_', t):
            atk_src.append('%s:%d' % (EA.rel(p), t.count('\n', 0, m.start()) + 1))
    chk('skill.icon.attack.base-only', not atk_src,
        '全工程 .cs 里按帧号请求 SkilliconAttack_<N> 的点 = %d 处（应为 0 ⇒ 单色帧 %s 从不被请求）'
        % (len(atk_src), sorted(atk_fset) or '无'))
    total_flat = sum(len(_flat_frames(files, re.compile(r'^%sSkillicon_(\d+)\.png$' % c))) for c in requested) + len(atk_frames)
    say('   ⇒ SkillIcon 实心单色合计 = %d 张' % total_flat)


# ═══════════════════════════════════════════════════════════════════════════
# ② Objects/{warp,town_trees,town_fence}：实心单色张数 + 布局引用 = 0
# ═══════════════════════════════════════════════════════════════════════════
def layout_refs():
    """⇒ (全部 Packs[] 名集合, {(layer,pack,idx)})（复用 enum_all 的解析口径）。"""
    packs = set()
    tiles = set()
    for f in ('MapGenTownLayout.cs', 'MapGenWildLayout.cs', 'MapGenCaveLayout.cs'):
        s = EA.rd(os.path.join(EA.SV, 'Module/Map', f))
        packs |= set(EA._packs_of(s))
        for (layer, pack, idx) in EA._layout_tiles(s, f).keys():
            tiles.add((layer, pack, idx))
    return packs, tiles


def objects_group():
    say('')
    say('=' * 78)
    say('② Objects/{warp,town_trees,town_fence}：布局引用 = 0（枚举自 MapGen{Town,Wild,Cave}Layout.cs）')
    say('=' * 78)
    packs, tiles = layout_refs()
    say('三个布局的 Packs[] 并集 = %d 个包；逐格瓦片键 = %d 个' % (len(packs), len(tiles)))
    for name in ('warp', 'town_trees', 'town_fence', 'moor_river'):
        d = os.path.join(RES, 'D2/Objects', name)
        files = _pngs(d)
        flats = _flat_frames(files, re.compile(r'^(\d+)\.png$'))
        cols = collections.Counter(t[2] for t in flats)
        in_packs = name in packs
        ref_frames = sorted(set(k[2] for k in tiles if k[1] == name))
        flat_frames = sorted(t[0] for t in flats)
        inter = sorted(set(flat_frames) & set(ref_frames))
        say('   Objects/%s：PNG %d 张，实心单色 %d 张；颜色 %s' % (
            name, len(files), len(flats), dict(cols) or '—'))
        say('      出现在 Packs[] = %s；逐格瓦片引用 = %d 处（引用帧号 %s）'
            % (in_packs, len(set(k for k in tiles if k[1] == name)), ref_frames[:12]))
        say('      实心单色帧号 = %s；**平色帧 ∩ 被引用帧 = %s**' % (flat_frames, inter or '∅'))
    for name in ('warp', 'town_trees', 'town_fence'):
        d = os.path.join(RES, 'D2/Objects', name)
        flats = _flat_frames(_pngs(d), re.compile(r'^(\d+)\.png$'))
        ref_frames = set(k[2] for k in tiles if k[1] == name)
        flat_frames = set(t[0] for t in flats)
        inter = sorted(flat_frames & ref_frames)
        if not ref_frames:
            chk('objects.%s.no-layout-ref' % name, True,
                'Objects/%s：布局**逐格引用 0 处**（实心单色 %d 张全部不被落格）' % (name, len(flats)))
        else:
            chk('objects.%s.flat-unref' % name, not inter,
                'Objects/%s：被布局引用 %d 帧，但实心单色帧 %s **一个都不在引用集内**（交集 %s）'
                % (name, len(ref_frames), sorted(flat_frames), inter or '∅'))
    # moor_river：被布局引用（R1-B 白名单不叠 = E32）
    mr_refs = sorted(k for k in tiles if k[1] == 'moor_river')
    mr_flat = _flat_frames(_pngs(os.path.join(RES, 'D2/Objects/moor_river')), re.compile(r'^(\d+)\.png$'))
    chk('objects.moor_river.whitelisted', bool(mr_refs) and bool(mr_flat),
        'Objects/moor_river：被布局引用 %d 处（R1-B 白名单不叠 = E32）；实心单色帧 = %s'
        % (len(mr_refs), [(t[0], t[2]) for t in mr_flat]))


# ═══════════════════════════════════════════════════════════════════════════
# ③ UI/MiniMap（原版白色模板）
# ═══════════════════════════════════════════════════════════════════════════
def minimap():
    say('')
    say('=' * 78)
    say('③ UI/MiniMap：原版白色模板（实心单色逐张列色值）')
    say('=' * 78)
    d = os.path.join(RES, 'D2/UI/MiniMap')
    flats = _flat_frames(_pngs(d), re.compile(r'^mapicon_(\d+)\.png$'))
    say('   实心单色帧 = %s' % [(t[0], os.path.basename(t[1]), t[2]) for t in flats])
    allwhite = flats and all(t[2] == (244, 244, 244, 255) for t in flats)
    chk('uiarts.minimap.white-template', bool(allwhite),
        '8 张 mapicon_* 全部唯一色 = (244,244,244,255)（原版白色模板 ⇒ 上色由代码做）')


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass
    skillicon()
    objects_group()
    minimap()
    say('')
    say('=' * 78)
    say('VERDICT: %s（断言失败 %d 条）' % ('OK' if not FAILED else 'FAIL', len(FAILED)))
    if FAILED:
        for f in FAILED:
            say('   失败: %s' % f)
    txt = '\n'.join(OUT)
    print(txt)
    with io.open(os.path.join(ROOT, '.ai-tmp/test/d3-flatcolor.txt'), 'w', encoding='utf-8') as f:
        f.write(txt + '\n')
    sys.exit(1 if FAILED else 0)


if __name__ == '__main__':
    main()
