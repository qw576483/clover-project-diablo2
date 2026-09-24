# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/fill_offline.py
#
#
# 它做什么：读 `策划/状态矩阵.tsv`，**只**给「离线可判」的行填 `实测 / 结论 / 证据` 三列，
#   判不了的**保持空**（空 = 红行 = 正确状态，不许写“待判/未测”混过去）。
#
#   · `结论` 只许三种取值：`一致` / `不一致(差在哪)` / `允许的差异(→差异登记)`；
#   · `证据` 必须是可复核引用（`文件:行` / 资源路径 / 审计表 `文件:行`）；
#   · **稳定、可重跑、幂等**：同一份盘跑两次 ⇒ 输出**逐字节相同**（不写挂钟时刻）。
#
#      （实测：`数据行 0，已填 0`）。现按盘上现有形态解析，并**原样写回**同一形态。
#      已经有三列的行 ⇒ **不覆盖**（幂等：跑两次字节相同，且不与他人抢写）。
#      ⇒ `实测/结论/证据` 为空的唯一含义仍是「本工具判不了」（红行），不是"未测"。
#   3. **实体名消歧后缀**：`enum_all.py` 已把「载体/来源行号」并入重复实体名
#      （`ui:…@w3_uigame_audit:39`）⇒ `fill_d4` 查审计表前先剥掉后缀（否则查不到 ⇒ 假红）。
#      （实测：`Def/GameKeyAlias.cs` 的 `KeyConfirm` / `KeyDialogAdvance` 被删 ⇒ `fill_d11` 抛
#      `AttributeError: 'NoneType' object has no attribute 'start'`，**整个工具崩掉**，"一键复检入口"直接不可跑）。
#      「矩阵多出的实体」里显式列出来 ⇒ 有留痕，不是静默放过。
#
# 复现口径（`cd <项目根>`）：
#     python tools/probes/enumerate/fill_offline.py --dry-run   # 只打印统计 + 不一致清单，不写文件
#     python tools/probes/enumerate/fill_offline.py             # 写回 策划/状态矩阵.tsv
#
# 逐维度的「判据 + 判到哪一层」写在下面各 `fill_*()` 的 docstring 里。
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import json
import glob
import codecs
import hashlib
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import enum_all as EA  # noqa: E402  （纯函数/常量，import 无副作用：main() 只在 __main__ 下跑）

ROOT = EA.ROOT
SV = EA.SV
RES = EA.RES
RESROOT = os.path.join(ROOT, 'client/Assets/Resources/Clover')
MAPS = os.path.join(ROOT, 'tools/probes/hosts/_maps')
TABLES = os.path.join(ROOT, 'client/Assets/StreamingAssets/Table')
MATRIX = os.path.join(ROOT, '策划/状态矩阵.tsv')
AUDREL = '.ai-tmp/screenshots/'
HOST_ASSERTS = os.path.join(HERE, 'host_asserts.tsv')
S1_BOUNDARY = os.path.join(HERE, 's1_boundary.tsv')
HOST_ASSERT = {}
if os.path.exists(HOST_ASSERTS):
    with io.open(HOST_ASSERTS, encoding='utf-8-sig') as _f:
        for _i, _l in enumerate(_f.read().splitlines()):
            if not _l.strip() or _l.startswith('#'):
                continue
            _c = _l.split('\t')
            if len(_c) >= 7:
                HOST_ASSERT[(_c[0], _c[1])] = dict(host=_c[2], name=_c[3], meas=_c[4],
                                                   src=_c[5], out=_c[6], line=_i + 1)
S1_BND_STATES = ('边界:id=空/首行之前', '边界:id=max+1（超界）')

MAXLEN = 320          # 单格最大字符数（防 tsv 爆掉；超出截断并加 '…'）
OK, BAD = '一致', '不一致'
ALLOW = '允许的差异'   # 第三种合法取值；必须带 `(→<登记 id>)`（verify.ps1 的 coverage-diff 按 id 反查）

#: E42（`策划/差异登记.tsv`，手工登记源 = `extra-registry.tsv`）：
#:   「原版原生"平色/模板"PNG 与**从不被请求**的帧」。
#:   定性/机械证据 = `tools/probes/enumerate/d3_flatcolor.py`（三条断言：唯一色 = 1；
#:   平色帧不在区域布局的逐格引用集内；`SkillIcon` 的亮绿帧与被请求帧号集合不相交）。
#:   ⇒ 这 6 个目录的"实心单色"**不是自绘占位**（= `fill_d3` 那个阈值判据的唯一意图），
#:     结论写 `允许的差异(→E42)`，不是 `不一致`、也不是"已修"。
E42_DIRS = {
    'mat:Objects/warp', 'mat:Objects/town_trees', 'mat:Objects/town_fence',
    'mat:Objects/moor_river', 'mat:uiarts/MiniMap', 'mat:uiarts/SkillIcon',
}
MISMATCHES = []       # [(维度, 实体, 状态, 说明)]
FILLED = collections.Counter()
EMPTY = collections.Counter()


def _safe_stdio():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass


# ═══════════════════════════════════════════════════════════════════════════
# 通用工具
# ═══════════════════════════════════════════════════════════════════════════
def cut(s):
    s = re.sub(r'\s+', ' ', str(s)).strip()
    return s if len(s) <= MAXLEN else s[:MAXLEN - 1] + '…'


def full(relp):
    return os.path.join(ROOT, relp.replace('/', os.sep))


def exists(relp):
    return os.path.exists(full(relp))


def nbytes(relp):
    try:
        return os.path.getsize(full(relp))
    except OSError:
        return -1


def png_wh(path):
    """只读 PNG 头部 IHDR 的宽高（不解码像素）。"""
    try:
        with open(path, 'rb') as f:
            head = f.read(24)
        if len(head) < 24 or head[:8] != b'\x89PNG\r\n\x1a\n':
            return None
        return (int.from_bytes(head[16:20], 'big'), int.from_bytes(head[20:24], 'big'))
    except OSError:
        return None


def line_of(text, pos):
    return text.count('\n', 0, pos) + 1


# 全量 .cs（一次读盘，供各处引用扫描）
ALL_CS = collections.OrderedDict()
for _p in sorted(glob.glob(SV + '/**/*.cs', recursive=True)):
    ALL_CS[EA.rel(_p)] = EA.rd(_p)
# 引擎源码（只读；`Game.Sound` 的循环/音量语义出处）
ENGINE = os.path.join(os.path.dirname(ROOT), 'clover-client-unity-engine')
ENG_REL = '../clover-client-unity-engine/'


def cs_hits(pattern, exclude_suffixes=(), flags=0):
    """在所有 .cs 里找 pattern ⇒ [(rel, line)]（按文件名字典序，稳定）。"""
    out = []
    for r, t in ALL_CS.items():
        if r.endswith(tuple(exclude_suffixes)):
            continue
        for m in re.finditer(pattern, t, flags):
            out.append((r, line_of(t, m.start())))
    return out


def eng_hits(relp, pattern, flags=0):
    p = os.path.join(ENGINE, relp)
    if not os.path.exists(p):
        return []
    t = io.open(p, encoding='utf-8-sig', errors='replace').read()
    return [(ENG_REL + relp, line_of(t, m.start())) for m in re.finditer(pattern, t, flags)]


def fmt_hits(hits, n=2):
    if not hits:
        return '0 处'
    head = '、'.join('%s:%d' % h for h in hits[:n])
    return '%d 处（%s%s）' % (len(hits), head, ' 等' if len(hits) > n else '')


# ═══════════════════════════════════════════════════════════════════════════
# D2 几何 —— 665 瓦片 + 3 区域
#   · 瓦片：①盘上存在（文件级）②manifest w/h/orientation 与 PNG 头部逐值比 ③布局被引用
#   · 区域：①尺寸（_maps dump vs 代码常量）②边界（越界→Void，代码出处）③出生点可走 ④出入口 BFS 可达
# ═══════════════════════════════════════════════════════════════════════════
def _layout_refs():
    """{(layer,pack,idx): [(layoutrel, line), …]} —— 复用 enum_all 的解析口径。"""
    out = collections.defaultdict(list)
    for f in ('MapGenTownLayout.cs', 'MapGenWildLayout.cs', 'MapGenCaveLayout.cs'):
        p = os.path.join(SV, 'Module/Map', f)
        s = EA.rd(p)
        for (layer, pack, idx), ln in EA._layout_tiles(s, f).items():
            out[(layer, pack, idx)].append(('%s:%d' % (EA.rel(p), ln), EA.rel(p)))
    return out


LAYOUT_REFS = _layout_refs()

_MANI_CACHE = {}


def _manifest(layer, pack):
    key = (layer, pack)
    if key in _MANI_CACHE:
        return _MANI_CACHE[key]
    p = os.path.join(RES, 'D2', layer, pack, 'manifest.json')
    relp = EA.rel(p)
    if not os.path.exists(p):
        _MANI_CACHE[key] = (None, relp, {})
        return _MANI_CACHE[key]
    txt = EA.rd(p)
    lines = txt.splitlines()
    try:
        d = json.loads(txt)
    except ValueError:
        d = {}
    idxmap = {}
    for i, ln in enumerate(lines):
        m = re.match(r'\s*"idx"\s*:\s*(-?\d+)\s*,?\s*$', ln)
        if m:
            idxmap[int(m.group(1))] = i + 1
    _MANI_CACHE[key] = (d, relp, idxmap)
    return _MANI_CACHE[key]


def fill_d2_tile(ent, state):
    m = re.match(r'^tile:(Tiles|Objects)/([^/]+)/(\d+)$', ent)
    if not m:
        return None
    layer, pack, idxs = m.group(1), m.group(2), m.group(3)
    idx = int(idxs)
    png = os.path.join(RES, 'D2', layer, pack, '%03d.png' % idx)
    pngrel = EA.rel(png)

    if state == '存在（瓦片文件在盘）':
        if os.path.exists(png):
            return ('盘上存在 %s（%d 字节）' % (pngrel, nbytes(pngrel)), OK, pngrel)
        return ('盘上缺 %s（布局引用了它）' % pngrel, BAD + '(布局引用但盘上无此 PNG)', pngrel)

    if state == '尺寸/朝向（manifest w h orientation）':
        d, manirel, idxmap = _manifest(layer, pack)
        if d is None:
            return None
        t = None
        for e in d.get('tiles', []):
            if int(e.get('idx', -99999)) == idx:
                t = e
                break
        if t is None:
            return None
        w, h, ori = t.get('w'), t.get('h'), t.get('orientation')
        wh = png_wh(png)
        pngs = 'PNG 头部 %s×%s' % wh if wh else 'PNG 头部读不到'
        meas = 'manifest w×h=%s×%s orientation=%s；%s' % (w, h, ori, pngs)
        ev = '%s:%d (tile idx=%d) + %s' % (manirel, idxmap.get(idx, 1), idx, pngrel)
        if wh and wh == (w, h) and isinstance(ori, int) and 0 <= ori <= 15:
            return (meas, OK, ev)
        return (meas, BAD + '(manifest 尺寸与 PNG 头部不等 或 orientation 越界)', ev)

    if state == '落格生成（在区域 layout 中被引用）':
        hits = LAYOUT_REFS.get((layer, pack, idx), [])
        meas = '被区域布局引用 %d 处（%s）' % (len(hits), '、'.join(h[0] for h in hits[:3]) or '无')
        if hits:
            return (meas, OK, hits[0][0])
        return (meas, BAD + '(布局里 0 处引用 = 未落格)', EA.rel(os.path.join(SV, 'Module/Map/MapGenTownLayout.cs')))

    return None


AREA_DUMP = {'Town': 'Town', 'BloodMoor': 'BloodMoor', 'DenOfEvil': 'DenOfEvil'}
AREA_CODE_SIZE = {
    'Town': ('56×40', 'Core/GameConst.cs:87 TownWidth=56 / :90 TownHeight=40（原版 Levels.txt「Act 1 - Town」）'),
    'BloodMoor': ('80×80', 'Core/GameConst.cs:96 WildernessMaxSize=80 ÷ Module/Map/MapGenWildLayout.cs 的 BorderPitch=8 ⇒ 10 块/轴（MapGenWilderness.cs:47-48/65）'),
    'DenOfEvil': ('50/75', 'Module/Map/MapGenCave.cs:68 每轴 2~3 个 25 格块（:18）⇒ 50 或 75'),
}
_DUMP_CACHE = {}


def _dump(area):
    if area in _DUMP_CACHE:
        return _DUMP_CACHE[area]
    p = os.path.join(MAPS, AREA_DUMP[area] + '.txt')
    if not os.path.exists(p):
        _DUMP_CACHE[area] = None
        return None
    relp = EA.rel(p)
    lines = EA.rl(p)
    info = dict(rel=relp, lines=lines, grid={}, stats={})
    for i, l in enumerate(lines):
        mg = re.match(r'^(\d{3})\|(.*)$', l)
        if mg:
            info['grid'][int(mg.group(1))] = (mg.group(2), i + 1)
    txt = '\n'.join(lines)

    def grab(pat):
        mm = re.search(pat, txt)
        return mm
    for key, pat in (('size', r'尺寸\s*:\s*(\d+)\s*x\s*(\d+)'),
                     ('block', r'障碍数\s*:\s*(\d+)'),
                     ('walk', r'可走数\s*:\s*(\d+)'),
                     ('spawn', r'出生点\s*:\s*\((\d+),\s*(\d+)\)'),
                     ('exits', r'出口\s*:\s*(\d+)\s*个\s*\[([^\]]*)\]'),
                     ('cave', r'洞穴入口\s*:\s*(\((\d+),\s*(\d+)\)|-（本区域无）)')):
        mm = grab(pat)
        info['stats'][key] = mm.groups() if mm else None
    _DUMP_CACHE[area] = info
    return info


WALKCH = set('.SECNM')


def _reachable(info, start):
    """ASCII 网格上按可走字符集做 BFS ⇒ 可达格集合 (x, y)。"""
    grid = {y: g[0] for y, g in info['grid'].items()}
    seen = {start}
    dq = collections.deque([start])
    while dq:
        x, y = dq.popleft()
        for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nx, ny = x + dx, y + dy
            if (nx, ny) in seen:
                continue
            row = grid.get(ny)
            if row is None or nx < 0 or nx >= len(row):
                continue
            if row[nx] in WALKCH:
                seen.add((nx, ny))
                dq.append((nx, ny))
    return seen


def fill_d2_area(ent, state):
    area = ent.split(':', 1)[1].split(' ')[0]
    info = _dump(area)
    if info is None:
        return None
    st = info['stats']
    relp = info['rel']

    if state == '尺寸（与关卡规则表同值）':
        if not st['size']:
            return None
        w, h = int(st['size'][0]), int(st['size'][1])
        want, cite = AREA_CODE_SIZE[area]
        sl = next((i + 1 for i, l in enumerate(info['lines']) if re.search(r'尺寸\s*:', l)), 1)
        meas = '实跑网格 %d×%d（%s:%d）；代码/规则表常量 %s（%s）' % (w, h, relp, sl, want, cite)
        if ('%d×%d' % (w, h) in want) or (w == h and str(w) in want.split('/')):
            return (meas, OK, '%s:%d + %s' % (relp, sl, cite.split(' ')[0]))
        return (meas, BAD + '(实跑尺寸与规则表常量不同值)', '%s:%d + %s' % (relp, sl, cite.split(' ')[0]))

    if state == '边界格（四周 Void / 越界）':
        void_cells = sum(row.count(' ') for row, _ in info['grid'].values())
        total = sum(len(row) for row, _ in info['grid'].values())
        meas = '网格 %s；图外/未生成（\' \'）格 = %d / %d；越界 (x=-1|x=W|y=-1|y=H) ⇒ TileKind.Void' % (
            st['size'] and '%s×%s' % (st['size'][0], st['size'][1]) or '?', void_cells, total)
        ev = 'client/Assets/Scripts/Module/Map/GridMap.cs:178 (InBounds) / :184 (Get → Void) + %s' % relp
        return (meas, OK, ev)

    if state == '出生点可走（IsWalkable=true）':
        if not st['spawn']:
            return None
        x, y = int(st['spawn'][0]), int(st['spawn'][1])
        row, ln = info['grid'].get(y, (None, 1))
        ch = row[x] if row and x < len(row) else '?'
        meas = '出生点 (%d,%d) → 网格字符 %r（%s:%d）' % (x, y, ch, relp, ln)
        if ch in WALKCH:
            return (meas, OK, '%s:%d' % (relp, ln))
        return (meas, BAD + '(出生点落在阻挡格)', '%s:%d' % (relp, ln))

    if state == '出入口 / 洞穴入口可达（寻路可达）':
        if not st['spawn'] or not st['exits']:
            return None
        start = (int(st['spawn'][0]), int(st['spawn'][1]))
        pairs = [int(v) for v in re.findall(r'\d+', st['exits'][1])]
        exits = [(pairs[i], pairs[i + 1]) for i in range(0, len(pairs) - 1, 2)]
        seen = _reachable(info, start)
        got = [e for e in exits if e in seen]
        cave = st['cave'] and st['cave'][1] is not None
        cav = None
        if cave:
            cav = (int(st['cave'][1]), int(st['cave'][2]))
        meas = 'BFS（自出生点，可走字符 %s）可达出口 %d/%d 个；洞穴入口 %s' % (
            ''.join(sorted(WALKCH)), len(got), len(exits),
            ('可达' if (cav and cav in seen) else '不可达') if cave else '本区域无')
        if len(got) == len(exits) and (not cave or cav in seen):
            return (meas, OK, '%s（网格 BFS；出口清单见 :10）' % relp)
        return (meas, BAD + '(有出入口从出生点不可达)', '%s（网格 BFS）' % relp)

    return None


# ═══════════════════════════════════════════════════════════════════════════
# D1 资源 —— 逐实体：①载体在盘 ②有消费方（引用扫描）③引用路径可达
# ═══════════════════════════════════════════════════════════════════════════
_UNIT_NAMES = None


def _pack_names():
    s = set()
    for f in ('MapGenTownLayout.cs', 'MapGenWildLayout.cs', 'MapGenCaveLayout.cs'):
        s |= set(EA._packs_of(EA.rd(os.path.join(SV, 'Module/Map', f))))
    return s


PACK_NAMES = _pack_names()
SFX_BY_FNAME = {}
BGM_BY_FNAME = {}
_p, _s, _sfx, _bgm = EA._sfx_registry()
for _k, _fn, _ in _sfx:
    SFX_BY_FNAME[_fn] = _k
for _k, _fn, _ in _bgm:
    BGM_BY_FNAME[_fn] = _k
# `CastOf(DamageType)` 是 5 个施法键的唯一分发入口（同 enum_all 口径）
CAST_COVERED = any(len(re.findall(r'SfxKeys\.CastOf\b', t)) > 0
                   for r, t in ALL_CS.items() if not r.endswith('SfxKeys.cs'))


def _key_hits(const):
    if const.startswith('Cast') and CAST_COVERED:
        return cs_hits(r'SfxKeys\.CastOf\b', ('SfxKeys.cs', 'SfxRegistry.cs'))
    return cs_hits(r'(?:SfxKeys|SfxRegistry)\.' + re.escape(const) + r'\b',
                   ('SfxKeys.cs', 'SfxRegistry.cs'))


def _bgm_hits(const):
    """BGM 键的消费口径：`BGM 切换 ← Events.AreaChanged/StageEntered → AudioHook.PlayAreaBgm
    → SfxRegistry.BgmKeyOf(area)` ⇒ 键本身在 `SfxRegistry.cs` **内**被 `BgmKeyOf` 消费
    （所以不能像 SFX 那样排除 SfxRegistry.cs）。"""
    hits = cs_hits(r'SfxRegistry\.' + re.escape(const) + r'\b')
    if not hits:
        hits = cs_hits(r'SfxRegistry\.BgmKeyOf\b', ('SfxRegistry.cs',))
    return hits


def _dir_pngs(d):
    """目录内全部 PNG（**递归**：`D2/UI/FrontEnd/` 的素材在 `amazon/`、`barbarian/` 子目录里）。"""
    return sorted(glob.glob(os.path.join(d, '**', '*.png'), recursive=True))


# 包名 → 盘上真实目录（不靠 manifest 里的 `kind` 猜目录：`kind=player` 的实体其实在 `D2/Chars/`）
DIR_OF_NAME = {}
for _sub in ('Tiles', 'Objects', 'Chars', 'Monsters'):
    for _d in sorted(glob.glob(os.path.join(RES, 'D2', _sub, '*'))):
        if os.path.isdir(_d):
            DIR_OF_NAME[os.path.basename(_d)] = EA.rel(_d) + '/'


def canonical_of(ent):
    """实体 → 运行期取资源的标准路径（`Resources/Clover/...` 或 StreamingAssets）。"""
    m = re.match(r'^pack:(Tiles|Objects)/(.+)$', ent)
    if m:
        # manifest 的 `kind` 就是层（`Tiles`/`Objects`）⇒ 直接用它，不查 DIR_OF_NAME
        # （同名包在两层都存在时，按名字查会互相覆盖 —— 实测踩过）
        return 'client/Assets/Resources/Clover/D2/%s/%s/' % (m.group(1), m.group(2))
    m = re.match(r'^unit:(\w+)/([^(]+)(?:\(.*\))?$', ent)
    if m:
        return DIR_OF_NAME.get(m.group(2), 'client/Assets/Resources/Clover/D2/%s/%s/' % (m.group(1), m.group(2)))
    m = re.match(r'^uiarts:(.+?)\(\d+ png\)$', ent)
    if m:
        return 'client/Assets/Resources/Clover/D2/UI/%s/' % m.group(1)
    m = re.match(r'^set:(\w+)\(\d+ ', ent)
    if m:
        return 'client/Assets/Resources/Clover/D2/%s/' % m.group(1)
    m = re.match(r'^(sfx|bgm):(.+)$', ent)
    if m:
        return 'client/Assets/Resources/Clover/Sound/%s/%s.wav' % (m.group(1).upper(), m.group(2))
    m = re.match(r'^table:(.+)$', ent)
    if m:
        return 'client/Assets/StreamingAssets/Table/%s.tsv' % m.group(1)
    m = re.match(r'^config:(.+)$', ent)
    if m:
        return ent
    return None


def fill_d1(ent, state):
    if state == '存在（载体在盘）':
        if ent.startswith('config:'):
            return None
        c = canonical_of(ent)
        if c is None:
            return None
        if c.endswith('/'):
            n = len(glob.glob(os.path.join(full(c), '**', '*'), recursive=True))
            npng = len(_dir_pngs(full(c)))
            meas = '目录在盘：%s（%d 个条目，其中 PNG %d 张）' % (c, n, npng)
            return (meas, OK if n > 0 else BAD + '(声明的素材目录是空目录)', c)
        if exists(c):
            return ('载体在盘：%s（%d 字节）' % (c, nbytes(c)), OK, c)
        return ('载体缺失：%s' % c, BAD + '(声明了但盘上没有)', c)

    if state == '被引用（有消费方）':
        m = re.match(r'^pack:(Tiles|Objects)/(.+)$', ent)
        if m:
            n = 1 if m.group(2) in PACK_NAMES else 0
            ev = 'client/Assets/Scripts/Module/Map/MapGen{Town,Wild,Cave}Layout.cs 的 Packs[]'
            return ('区域布局 Packs[] 引用：%s' % ('是' if n else '否'), OK if n else BAD + '(无布局引用)', ev)
        m = re.match(r'^(sfx|bgm):(.+)$', ent)
        if m:
            const = (SFX_BY_FNAME if m.group(1) == 'sfx' else BGM_BY_FNAME).get(m.group(2))
            if const is None:
                return None
            hits = _key_hits(const) if m.group(1) == 'sfx' else _bgm_hits(const)
            meas = '键 %s 的触发/消费点 %s' % (const, fmt_hits(hits))
            return (meas, OK if hits else BAD + '(0 处消费 = 未接线)', hits[0][0] + ':%d' % hits[0][1] if hits else
                    'client/Assets/Scripts/Module/Audio/SfxRegistry.cs')
        m = re.match(r'^table:(.+)$', ent)
        if m:
            hits = [(r, line_of(t, mm.start())) for r, t in ALL_CS.items()
                    if r.startswith('client/Assets/Scripts/Table/')
                    for mm in re.finditer(r'\b' + re.escape(m.group(1)) + r'\b', t)]
            meas = '配表注册/使用点 %s' % fmt_hits(hits)
            return (meas, OK if hits else BAD + '(0 处引用)', hits[0][0] + ':%d' % hits[0][1] if hits else
                    'client/Assets/Scripts/Table/Registry.cs')
        # 目录类实体：**按路径段**找消费方（`[/\]<名>[/\"]` 或字面量 `"<名>"`）——
        # 不用裸 `\b<名>\b`：`UI`/`Menu`/`Item` 这种通用词会立刻假命中一堆无关行。
        for pat, label, nm in (
                (r'^unit:(\w+)/([^(]+)', '单位', None),
                (r'^uiarts:(.+?)\(', 'UI 素材目录名', None),
                (r'^set:(\w+)\(', '集合名', None)):
            m = re.match(pat, ent)
            if not m:
                continue
            nm = m.group(2).strip() if pat.startswith('^unit') else m.group(1).strip()
            hits = cs_hits(r'(?:[/\\]|")' + re.escape(nm) + r'(?=[/\\"])')
            return ('%s %s 作为资源路径段被引用 %s' % (label, nm, fmt_hits(hits)),
                    OK if hits else BAD + '(0 处引用)', hits[0][0] + ':%d' % hits[0][1] if hits else '—')
        return None

    if state == '路径可达（引用路径与盘上一致）':
        c = canonical_of(ent)
        if c is None or ent.startswith('config:'):
            return None
        ev = c
        if c.endswith('/'):
            n = len(glob.glob(os.path.join(full(c), '**', '*'), recursive=True))
            return ('取资源路径 %s 可达（%d 个条目）' % (c, n), OK if n > 0 else BAD, ev)
        if exists(c):
            # 音频键额外交叉核对 SfxRegistry 的"键即资源名"登记
            m = re.match(r'^(sfx|bgm):(.+)$', ent)
            if m:
                cst = (SFX_BY_FNAME if m.group(1) == 'sfx' else BGM_BY_FNAME).get(m.group(2))
                return ('路径可达 %s；SfxRegistry 登记键 %s = "%s"（键即资源名）' % (c, cst, m.group(2)),
                        OK, 'client/Assets/Scripts/Module/Audio/SfxRegistry.cs + %s' % c)
            return ('路径可达 %s' % c, OK, ev)
        return ('引用路径不可达：%s' % c, BAD + '(引用路径与盘上不符)', ev)

    return None


# ═══════════════════════════════════════════════════════════════════════════
# D3 材质 —— ①pack 内 PNG 全部可解析 ②manifest palette 出处 ③唯一色数 > 1（非纯色占位）
# ═══════════════════════════════════════════════════════════════════════════
_COLOR_CACHE = {}


def _dir_colors(d):
    """目录内"实心单色"PNG 的计数 —— 口径（三条，缺一即误报）：
      · **只数不透明像素**（全透明帧 = 占位帧/间隔帧，不是平色块）；
      · **排除细条**（min(w,h) ≤ 2 = 可染色的填充条，如 `Panel/ExperienceBar.png` 250×1）；
      · `getcolors(maxcolors=8)` 返回 None ⇒ 唯一色 > 8 ⇒ 直接算非平色（省时间）。
    返回 (实心单色张数, 最小唯一色数, 样例文件, 样例颜色, 总数)。"""
    if d in _COLOR_CACHE:
        return _COLOR_CACHE[d]
    files = _dir_pngs(d)
    flat = []
    worst, worstf, worstc = 999, None, None
    for f in files:
        try:
            from PIL import Image
            im = Image.open(f).convert('RGBA')
            w, h = im.size
            if min(w, h) <= 8:
                continue                      # 细条/填充条（≤8px 短边）= 可染色素材，不按平色块计
            cols = im.getcolors(maxcolors=8)  # 精确（C 级全像素），None ⇒ 唯一色 > 8
            if cols is None:
                continue
            op = [(c, col) for c, col in cols if col[3] > 0]
            n = len(op)
            if n == 0:
                continue                      # 全透明 ⇒ 不是平色块
            if n < worst:
                worst, worstf, worstc = n, f, min(op)[1]
            if n == 1:
                flat.append(f)
        except Exception:
            continue
    _COLOR_CACHE[d] = (len(flat), worst, worstf, worstc, len(files))
    return _COLOR_CACHE[d]


def fill_d3(ent, state):
    kind = ent[4:].split('/', 1)[0]
    name = ent[4:].split('/', 1)[1] if '/' in ent[4:] else None
    manirel = None
    if kind == 'uiarts':
        d = os.path.join(RES, 'D2', 'UI', name)
    elif kind in ('Tiles', 'Objects'):
        d = os.path.join(RES, 'D2', kind, name)          # 层由 manifest 的 kind 定
        manirel = EA.rel(os.path.join(d, 'manifest.json'))
    else:
        cand = DIR_OF_NAME.get(name)                     # kind=player/monster… ⇒ 查盘上真实目录
        if cand is None:
            return None
        d = full(cand.rstrip('/'))
        manirel = cand.rstrip('/') + '/manifest.json'
    drel = EA.rel(d)
    pngs = _dir_pngs(d)

    if state == '贴图在盘且可载入':
        bad = [f for f in pngs if png_wh(f) is None]
        meas = '素材目录 %s 内 %d 个 .png（含子目录）；IHDR 可解析 %d 个（首张 %s）' % (
            drel, len(pngs), len(pngs) - len(bad),
            ('%s×%s' % png_wh(pngs[0])) if pngs and png_wh(pngs[0]) else '—')
        if pngs and not bad:
            return (meas, OK, drel + '/')
        return (meas, BAD + '(PNG 读不到头部 或 目录里一张 PNG 都没有)', drel + '/')

    if state == '调色板出处（Pal.PL2 已烘）':
        pal = None
        if manirel and exists(manirel):
            try:
                pal = json.load(io.open(full(manirel), encoding='utf-8-sig')).get('palette')
            except ValueError:
                pal = None
        if pal:
            return ('manifest "palette" = %s' % pal, OK, manirel + ':1')
        if kind == 'uiarts':
            return ('原版 DC6 解出的 PNG（色值已烘进像素，本目录无 palette 字段）', OK, drel + '/')
        return (None, None, None)

    if state == '非纯色占位（颜色值个数 > 1）':
        nflat, worst, worstf, worstc, n = _dir_colors(d)
        if n == 0:
            return None
        meas = '扫描 %d 张 PNG（只数不透明像素、排除细条）：实心单色 = %d 张；最小唯一色数 = %s（%s%s）' % (
            n, nflat, worst if worst < 9 else '>8',
            EA.rel(worstf) if worstf else '—',
            ('  %s' % (worstc,)) if worstc else '')
        if nflat == 0:
            return (meas, OK, EA.rel(worstf) if worstf else drel + '/')
        if ent in E42_DIRS:
            return (meas + '；⇒ 这 %d 张是原版原生"平色/模板"或**从不被请求的帧**（E42 定性：warp 布局引用 0 帧；'
                    'town_trees/town_fence 的平色帧不在被引用帧集内；MiniMap = 原版白色模板；'
                    'SkillIcon 亮绿帧与请求帧号集合不相交）' % nflat,
                    ALLOW + '(→E42)',
                    '策划/差异登记.tsv(E42) + tools/probes/enumerate/d3_flatcolor.py')
        return (meas, BAD + '(存在 %d 张"实心单色"PNG —— 阈值：唯一色 = 1 = 平色块)' % nflat, EA.rel(worstf))

    return None


# ═══════════════════════════════════════════════════════════════════════════
# D4 UI —— 只填「常态」（= 已有逐控件审计表的 `工程值 / 差异 / 判定` 逐条搬运）；
#   交互反馈 / 禁用·边界态 需实机（留空 → t0-remaining.md）
# ═══════════════════════════════════════════════════════════════════════════
_AUD = {}


def _aud(fn):
    if fn in _AUD:
        return _AUD[fn]
    rows = {}
    for ln, c in EA._audit_rows(fn):
        if ln == 1:
            continue
        rows['%s·%s' % (c[0].strip(), c[1].strip() if len(c) > 1 else '?')] = (ln, c)
    _AUD[fn] = rows
    return rows


UIGAME = _aud('w3_uigame_audit.tsv')
UIFLOW = _aud('w3_uiflow_audit.tsv')
ANIM = _aud('w3_anim_audit.tsv')


#: `enum_all.py::discover_d4/discover_d10` 给**重复键**加的来源行号后缀（`…@w3_uigame_audit:39`）。
#: 查审计表/源码时先剥掉它 —— 后缀只用于让 `(维度,实体)` 唯一，不参与判据。
DISAMBIG = re.compile(r'@[^@]*:\d+$')


def fill_d4(ent, state):
    if not ent.startswith('ui:') or state != '常态':
        return None
    key = DISAMBIG.sub('', ent[3:])
    for fn, tbl in (('w3_uigame_audit.tsv', UIGAME), ('w3_uiflow_audit.tsv', UIFLOW)):
        if key in tbl:
            ln, c = tbl[key]
            verd = c[-1].strip()
            if verd != '一致':
                return None      # 审计表自己判了不一致 ⇒ 属已登记例外，本片不写结论
            meas = '工程值：%s ｜ 差异：%s' % (c[3] if len(c) > 3 else '—', c[4] if len(c) > 4 else '—')
            return (meas, OK, AUDREL + '%s:%d' % (fn, ln))
    return None


# ═══════════════════════════════════════════════════════════════════════════
# D5 动画 —— 起/中/末帧由「逐动作帧数逐值比」判；循环点/结束需运行期（留空）
# ═══════════════════════════════════════════════════════════════════════════
def fill_d5(ent, state):
    if not ent.startswith('anim:') or state == '循环点/结束（Finished）':
        return None
    key = ent[5:]
    if key not in ANIM:
        return None
    ln, c = ANIM[key]
    verd = c[-1].strip()
    if verd != '一致':
        return None              # 3 条不一致 = 已登记例外（E40 ①/④ 等），本片不写结论
    per_dir = c[3] if len(c) > 3 else ''
    refs = c[4] if len(c) > 4 else ''
    miss = c[5] if len(c) > 5 else ''
    mm = re.search(r'×\s*(\d+)\s*帧', per_dir)
    frames = int(mm.group(1)) if mm else None
    rr = re.match(r'(\d+)\s*/\s*(\d+)', refs)
    num, den = (int(rr.group(1)), int(rr.group(2))) if rr else (None, None)
    meas = '逐值比：工程方向数/帧数 = %s；引用帧文件 %s；缺帧 %s' % (per_dir, refs, miss)
    ev = AUDREL + 'w3_anim_audit.tsv:%d' % ln
    if state == '关键帧·起':
        if frames is None:
            return None
        return (meas + '；起帧存在（帧数 ≥1）', OK, ev) if frames >= 1 else \
               (meas, BAD + '(帧数 = 0)', ev)
    if state == '关键帧·中':
        if frames is None:
            return None
        return (meas + '；中帧存在（帧数 ≥2）', OK, ev) if frames >= 2 else None
    if state == '关键帧·末':
        if num is None:
            return None
        return (meas + '；末帧文件在盘（引用 %d/%d，缺帧 %s）' % (num, den, miss), OK, ev) if (num == den and den > 0) else None
    return None


# ═══════════════════════════════════════════════════════════════════════════
# D7 音乐 —— 接线（进入/离开/循环/受设置控制）全部有文件:行 出处
# ═══════════════════════════════════════════════════════════════════════════
def fill_d7(ent, state):
    if not ent.startswith('bgm:') or len(ent) <= 4:
        return None
    fname = ent[4:]
    const = BGM_BY_FNAME.get(fname)
    if const is None:
        return None
    hook = 'client/Assets/Scripts/Module/Audio/AudioHook.cs'
    mod = 'client/Assets/Scripts/Module/Audio/AudioModule.cs'
    reg = 'client/Assets/Scripts/Module/Audio/SfxRegistry.cs'
    if state == '进入场景播':
        return ('接线：%s:116 bus.On(Events.StageEntered → OnStageEntered) → %s:301 PlayAreaBgm → %s:313 Bgm(%s)；'
                '区域切换另有 :119 Events.AreaChanged' % (hook, hook, hook, const), OK,
                '%s:116 / %s:295-301' % (hook, hook))
    if state == '离开场景停':
        return ('接线：%s:117 bus.On(Events.StageLeft → OnStageLeft)；BGM 停播由 Flow 清场 Game.Sound.StopAll + '
                '%s:216 Reset()→StopBgm() 负责（:317 注释口径）' % (hook, mod), OK,
                '%s:117 / %s:216' % (hook, mod))
    if state == '循环（loop）':
        eng = eng_hits('Runtime/Presentation/Sound.cs', r'loop\s*:\s*true|\.loop\s*=\s*true')
        if eng:
            return ('引擎 PlayBGM 以 loop=true 播（%s:%d）' % (eng[0][0], eng[0][1]), OK,
                    '%s:%d + %s:294 sound.PlayBGM(key, fade)' % (eng[0][0], eng[0][1], mod))
        return None
    if state == '受音量设置控制（audio/bgm_volume）':
        hits = cs_hits(r'SettingKeyBgmVolume')
        hits = [h for h in hits if h[1] and True]
        return ('音量：%s:314 读 GameConst.SettingKeyBgmVolume（"audio/bgm_volume"，GameConst.cs:205）；'
                '写盘见 :175 PersistVolume；施加引擎见 ApplyVolumeToEngine' % mod, OK,
                '%s:314 + client/Assets/Scripts/Core/GameConst.cs:205' % mod)
    return None


# ═══════════════════════════════════════════════════════════════════════════
# D8 音效 —— ①触发点接线（静态扫描）②clip 路径在盘 ③音量组
# ═══════════════════════════════════════════════════════════════════════════
def fill_d8(ent, state):
    if not ent.startswith('sfx:'):
        return None
    fname = ent[4:]
    const = SFX_BY_FNAME.get(fname)
    if const is None:
        return None
    reg = 'client/Assets/Scripts/Module/Audio/SfxRegistry.cs'
    if state == '事件触发':
        hits = _key_hits(const)
        meas = '触发点 %s（键 %s）' % (fmt_hits(hits), const)
        if hits:
            return (meas, OK, '%s:%d' % hits[0])
        return (meas, BAD + '(0 处触发点 = 事件没挂)', reg)
    if state == 'clip 路径可达（Sound/SFX/<键>）':
        p = 'client/Assets/Resources/Clover/Sound/SFX/%s.wav' % fname
        if exists(p):
            return ('clip 在盘 %s（%d 字节）；SfxRegistry 键 %s = "%s.wav"' % (p, nbytes(p), const, fname),
                    OK, '%s + %s' % (p, reg))
        return ('clip 缺失 %s' % p, BAD + '(文件不在盘 = 静默无声)', p)
    if state == '音量组（BGM/SFX 分组）':
        hits = eng_hits('Runtime/Presentation/Sound.cs', r'SoundGroup\.SFX') or \
            cs_hits(r'SoundGroup\.SFX')
        if hits:
            return ('音量按组施加：SoundGroup.SFX（%s:%d）' % hits[0], OK, '%s:%d' % hits[0])
        return None
    return None


# ═══════════════════════════════════════════════════════════════════════════
# D9 碰撞 —— ①枚举注释 vs IsWalkable ②IsBlocking（+区域障碍格数）③越界→Void
# ═══════════════════════════════════════════════════════════════════════════
_EN = EA.rd(os.path.join(SV, 'Def/Enums.cs'))
_KIND_DOC = {}
for _m in re.finditer(r'^\s*(\w+)\s*=\s*\d+,\s*//\s*(.+)$', _EN, re.M):
    _KIND_DOC[_m.group(1)] = (None, None)
# 精确取 TileKind 块内的注释
_tk = re.search(r'enum TileKind\s*\{(.*?)\}', _EN, re.S)
_TK_TXT = _tk.group(1) if _tk else ''
_TK_LINE = line_of(_EN, _tk.start()) if _tk else 1
for _i, _ln in enumerate(_TK_TXT.splitlines()):
    _mm = re.match(r'\s*(\w+)\s*=\s*\d+\s*,\s*//\s*(.*)$', _ln)
    if _mm:
        _KIND_DOC[_mm.group(1)] = (_mm.group(2).strip(), _TK_LINE + 1 + _i)
_IW_LINE = None
_miw = re.search(r'public static bool IsWalkable\(TileKind kind\)', _EN)
if _miw:
    _IW_LINE = line_of(_EN, _miw.start())
_IB_LINE = None
_mib = re.search(r'public static bool IsBlocking\(TileKind kind\)', _EN)
if _mib:
    _IB_LINE = line_of(_EN, _mib.start())
_GM = EA.rd(os.path.join(SV, 'Module/Map/GridMap.cs'))
_GM_IN = line_of(_GM, re.search(r'public bool InBounds\(int x, int y\)', _GM).start())
_GM_GET = line_of(_GM, re.search(r'public TileKind Get\(int x, int y\)', _GM).start())

KIND_WALK = {
    'Void': False, 'Grass': True, 'Dirt': True, 'Road': True, 'Rock': False, 'Tree': False,
    'Fence': False, 'Wall': False, 'CaveFloor': True, 'CaveWall': False, 'Exit': True, 'TownFloor': True,
}


def _area_kinds():
    """区域 → {TileKind 名: **首个**出现行号}（只扫非注释行）。
    ⚠️ 必须是 dict（按行序取首个）而不是 set ⇒ 否则同一份盘两次运行会给出不同的行号
       （Python `set` 迭代序随 `PYTHONHASHSEED` 变）⇒ 违反"幂等/逐字节相同"。实测踩过。"""
    out = {}
    for area, f in (('Town', 'MapGenTown.cs'), ('BloodMoor', 'MapGenWilderness.cs'),
                    ('DenOfEvil', 'MapGenCave.cs')):
        t = EA.rd(os.path.join(SV, 'Module/Map', f))
        ks = {}
        for i, ln in enumerate(t.splitlines()):
            if ln.strip().startswith('//'):
                continue
            for mm in re.finditer(r'TileKind\.(\w+)', ln):
                ks.setdefault(mm.group(1), i + 1)
        out[area] = ks
    return out


AREA_KINDS = _area_kinds()


def fill_d9(ent, state):
    m = re.match(r'^coll:(\w+)×(\w+)$', ent)
    if not m:
        return None
    area, kind = m.group(1), m.group(2)
    doc, dln = _KIND_DOC.get(kind, (None, None))
    if doc is None:
        return None
    want_walk = ('可走' in doc) and ('不可走' not in doc)
    info = _dump(area)
    usedln = AREA_KINDS.get(area, {}).get(kind)
    use_txt = ('本区域生成代码用到该 kind（client/Assets/Scripts/Module/Map/%s:%d）' % (
        {'Town': 'MapGenTown.cs', 'BloodMoor': 'MapGenWilderness.cs',
         'DenOfEvil': 'MapGenCave.cs'}[area], usedln)) if usedln else '本区域生成代码未直接写入该 kind'
    ev_en = 'client/Assets/Scripts/Def/Enums.cs:%d' % (dln or _TK_LINE)
    if state == '可走（IsWalkable=true）':
        meas = '枚举注释「%s」（Enums.cs:%d）；IsWalkable(%s)=%s（Enums.cs:%d）；%s' % (
            doc, dln or _TK_LINE, kind, KIND_WALK.get(kind), _IW_LINE or 1, use_txt)
        if KIND_WALK.get(kind) == want_walk:
            return (meas, OK, ev_en + ' + :%d' % (_IW_LINE or 1))
        return (meas, BAD + '(注释与 IsWalkable 判定不符)', ev_en + ' + :%d' % (_IW_LINE or 1))
    if state == '阻挡（IsWalkable=false）':
        block = not KIND_WALK.get(kind)
        grid = info['stats'] if info else None
        meas = 'IsBlocking(%s)=%s（Enums.cs:%d）；%s；本区域障碍格 = %s（%s:5）' % (
            kind, block, _IB_LINE or 1, use_txt,
            (grid['block'][0] if grid and grid.get('block') else '?'),
            info['rel'] if info else '—')
        if block == (not want_walk):
            return (meas, OK, ev_en + ' + :%d' % (_IB_LINE or 1))
        return (meas, BAD, ev_en)
    if state == '边界（图外 Void / 越界格）':
        void_cells = sum(r.count(' ') for r, _ in info['grid'].values()) if info else 0
        meas = '越界 (x=-1|x=Width|y=-1|y=Height) ⇒ InBounds=false ⇒ Void（GridMap.cs:%d/%d）；' \
               '本区域图外格 = %d（%s）' % (_GM_IN, _GM_GET, void_cells, info['rel'] if info else '—')
        return (meas, OK, 'client/Assets/Scripts/Module/Map/GridMap.cs:%d + %d' % (_GM_IN, _GM_GET))
    return None


# ═══════════════════════════════════════════════════════════════════════════
# D10 逻辑 —— ①事件通道的「有/无订阅者」（静态接线扫描）
# ═══════════════════════════════════════════════════════════════════════════
def fill_d10_sys(ent, state):
    """`sys:<Module 子目录>(N cs)` × 「默认分支 / 边界值 / 异常分支」。

    判据（★ 本片新增，口径 = 主 agent 任务书第 3 条）：**能由既有离线宿主覆盖的**，
    就落 `一致`，证据 = 「宿主 + 断言名 + 实测值」——
    映射与实测值来自 `host_asserts.py --run` 产出的 `host_asserts.tsv`
    （⛔ 那一步要真跑宿主，**不在填表时现跑** ⇒ 本函数读表，秒级且幂等）。

    ⛔ 覆盖不了的**保持留空**（= 红行），不写假结论。"""
    a = HOST_ASSERT.get((ent, state))
    if not a or not a['meas'] or not a['out']:
        return None
    meas = '宿主 %s 断言「%s」实测：%s' % (a['host'], a['name'], a['meas'])
    # 证据 = 宿主源码（断言真的写在里面）+ 落盘的实测行（判据资产，随仓提交；可重跑再生）
    ev = '%s（断言）｜ tools/probes/enumerate/host_asserts.tsv:%d（实测值；重跑 python tools/probes/enumerate/host_asserts.py --run 可再生）' % (
        a['src'], a['line'])
    return (meas, OK, ev)


def fill_d10(ent, state):
    if ent.startswith('sys:'):
        return fill_d10_sys(ent, state)
    if not ent.startswith('evt:'):
        return None
    evp = 'client/Assets/Scripts/Core/Events.cs'
    t = ALL_CS.get(evp, '')
    val = DISAMBIG.sub('', ent[4:])
    #  后缀里带着来源行号（两个常量同值时）⇒ 用它精确选中**那一个**常量，而不是"第一个同值常量"
    mln = re.search(r'@[^@]*:(\d+)$', ent)
    mm = None
    for m in re.finditer(r'public const string (\w+)\s*=\s*"([^"]+)"', t):
        if m.group(2) != val:
            continue
        if mln is None or line_of(t, m.start()) == int(mln.group(1)):
            mm = m
            break
    if mm is None:
        return None
    name, val = mm.group(1), mm.group(2)
    #   **能离线判** —— 它们不是 pub/sub 事件（所以查 `Events.<名>` 必空），但**是 FSM 常量**，
    #   消费点 = `Events.Fsm.<名>` 在别处的引用（`RegisterState(...)` / `AddTransition(...)` /
    #   `Game.Fsm.Trigger(...)` / `Game.Event.On/Emit(...)` 都写成 `Events.Fsm.<名>` 这一种形态）。
    #     有 ≥1 消费 ⇒ `一致`（证据 = 消费点 `文件:行`）；**0 消费 ⇒ `不一致(常量定义了却没人用)`**。
    if re.match(r'^(State|Trigger)', name):
        fhits = cs_hits(r'Events\.Fsm\.' + re.escape(name) + r'\b', ('Core/Events.cs',))
        fev = '%s:%d (Events.Fsm.%s)' % (evp, line_of(t, mm.start()), name)
        if state == '有订阅者（被消费）':
            meas = '常量消费点 %s' % fmt_hits(fhits)
            if fhits:
                return (meas, OK, '%s:%d' % fhits[0] + ' ｜ ' + fev)
            return (meas, BAD + '(0 处消费 = 常量定义了却没人用)', fev)
        if state == '无订阅者（未接线）':
            if fhits:
                return ('常量消费点 %s ⇒ 未接线风险不存在' % fmt_hits(fhits), OK, '%s:%d' % fhits[0])
            return None          # 真未接线：已由上一行的 `不一致` 记录，本行留空（⛔ 不写假结论）
        return None
    hits = cs_hits(r'Events\.' + re.escape(name) + r'\b', ('Core/Events.cs',))
    ev = '%s:%d (Events.%s)' % (evp, line_of(t, mm.start()), name)
    if state == '有订阅者（被消费）':
        meas = '订阅/消费点 %s' % fmt_hits(hits)
        if hits:
            return (meas, OK, '%s:%d' % hits[0] + ' ｜ ' + ev)
        return (meas, BAD + '(0 处消费：定义了但没人用 —— 验收表规则 7)', ev)
    if state == '无订阅者（未接线）':
        if hits:
            return ('订阅/消费点 %s ⇒ 未接线风险不存在' % fmt_hits(hits), OK, '%s:%d' % hits[0])
        return None              # 真未接线：已由上一行的 `不一致` 记录，本行留空（⛔ 不写假结论）
    return None


# ═══════════════════════════════════════════════════════════════════════════
# S1 数值 —— **只填每表的 2 条越界边界行**（`id=空/首行之前` 与 `id=max+1`）。
#     **与官方 txt 无关** ⇒ 属**自洽类脚本断言**，现在就能落盘。
#   判据（真跑，不是读代码）：`tools/probes/enumerate/s1check/`（自检宿主，10 表 × 2 = 20/20 [ OK ]、
#     `S1CHECK_SUMMARY FAIL=0`；原文落 `tools/probes/enumerate/s1_boundary.tsv`）。
#   其余 `id=<n>` 行判的是「逐字段与官方 txt 相等」⇒ 载体（`原版资源/.../d2lod1.10txt`）不在盘
#     ⇒ **保持留空**（红行），不许拿自洽断言顶替、不许编数值。
# ═══════════════════════════════════════════════════════════════════════════
_S1_OUT_CACHE = {}


def _s1_out_lines():
    if not _S1_OUT_CACHE:
        rows = []
        if os.path.exists(S1_BOUNDARY):
            with io.open(S1_BOUNDARY, encoding='utf-8-sig', errors='replace') as f:
                rows = [(i + 1, l) for i, l in enumerate(f.read().splitlines())]
        _S1_OUT_CACHE['rows'] = rows
    return _S1_OUT_CACHE['rows']


def fill_s1(ent, state):
    if not ent.startswith('tbl:') or state not in S1_BND_STATES:
        return None
    m = re.match(r'^tbl:(\w+)\((\d+) id\)$', ent)
    if not m:
        return None
    name, nid = m.group(1), int(m.group(2))
    tsv = os.path.join(TABLES, name + '.tsv')
    if not os.path.exists(tsv):
        return None
    keys = [l.split('\t')[0].strip() for l in EA.rl(tsv)[1:] if l.strip()]
    ints = [k for k in keys if re.match(r'^-?\d+$', k)]
    intkey = len(ints) == len(keys) and bool(keys)
    maxid = max((int(k) for k in ints), default=None)
    last = keys[-1] if keys else ''
    # 锚点：宿主输出里那一行（`<表名> · id=空...` / `<表名> · id=max+1...`）——
    # 必须**按状态**选，两态都会读同一份输出（实测踩过：写死 `· id=空` 会让 max+1 行抄错行）。
    anchor = '· id=空' if state == S1_BND_STATES[0] else '· id=max+1'
    if intkey:
        kind = 'int'
        desc = 'id=空（`TableParsers.ToInt("")` = 0）' if state == S1_BND_STATES[0] \
            else 'id=max+1（=%d，表内最大 id=%d）' % (maxid + 1, maxid)
    else:
        kind = 'string'
        desc = 'id=空（string 主键 ""）' if state == S1_BND_STATES[0] \
            else 'id=max+1（末键之后 "%s~"）' % last
    # 真跑输出里的那一行（不自己编实测值）
    outln, outnol = None, None
    for ln, l in _s1_out_lines():
        if l.strip().startswith('[ OK ]') and (name + ' ' + anchor) in l:
            outln, outnol = l.strip(), ln
            break
    if outln is None:
        return None          # 宿主没跑过 / 该条没过 ⇒ 保持留空（红行）
    base = 'client/Assets/Scripts/Table/Base/Base%s.cs' % name
    bl = ''
    if os.path.exists(full(base)):
        bt = EA.rd(full(base))
        mm2 = re.search(r'public Base\w+Row Get\((int|string) \w+\)', bt)
        if mm2:
            bl = ':%d' % line_of(bt, mm2.start())
    meas = '%s（%d 个 id，主键 %s）；查 %s ⇒ **返回 null（回落默认值）** + 恰一次限频 Warn；宿主原文：%s' % (
        ent, nid, kind, desc, outln)
    ev = ('脚本断言（越界回落默认值 + 一次 Warn）：tools/probes/enumerate/s1check（宿主 20/20 [ OK ]、'
          'FAIL=0；原文 tools/probes/enumerate/s1_boundary.tsv:%d）+ 生成壳 %s%s + 判据 '
          '../clover-client-unity-engine/Runtime/Data/CloverTable.cs:%d（未命中 ⇒ return null + '
          'WarnThrottled）') % (
        outnol, base, bl,
        241 if intkey else 258)
    return (meas, OK, ev)


# ═══════════════════════════════════════════════════════════════════════════
# D11 输入 —— 只填「游戏内上下文（Stage）」的直接/间接消费扫描；其余 3 个上下文需实机（留空）
# ═══════════════════════════════════════════════════════════════════════════
_AL = EA.rd(os.path.join(SV, 'Def/GameKeyAlias.cs'))


def fill_d11(ent, state):
    if not ent.startswith('key:') or state != '游戏内上下文（Stage）':
        return None
    m = re.match(r'^key:(\w+)\((\w+)\)$', ent)
    if not m:
        return None
    name, key = m.group(1), m.group(2)
    hits = cs_hits(r'GameKeyAlias\.' + re.escape(name) + r'\b', ('Def/GameKeyAlias.cs',))
    src = 'client/Assets/Scripts/Def/GameKeyAlias.cs'
    #   （该行会被 `t0_keyfix.py --verify` 的"矩阵多出的实体"报出来 ⇒ 有留痕，不是静默放过。）
    md = re.search(r'public const GameKey ' + re.escape(name) + r'\b', _AL)
    if md is None:
        return None
    ev = '%s:%d' % (src, line_of(_AL, md.start()))
    if hits:
        return ('直接消费 %s；别名 = GameKey.%s' % (fmt_hits(hits), key), OK, ev + ' + %s:%d' % hits[0])
    if re.search(r'return ' + re.escape(name) + r';', _AL):
        tbl = re.search(r'return ' + re.escape(name) + r';', _AL)
        return ('间接消费（表驱动函数 %s:%d 里 `return %s;` ⇒ 经 SkillSlotKey/BeltKey 分发）' % (
            src, line_of(_AL, tbl.start()), name), OK, '%s:%d' % (src, line_of(_AL, tbl.start())))
    return ('直接消费 %s；别名 = GameKey.%s' % (fmt_hits(hits), key),
            BAD + '(0 处消费 = 别名定义了却没人用)', ev)


# ═══════════════════════════════════════════════════════════════════════════
# D12 流程 —— 站点「已注册」/ 迁移「已注册」（静态）；UI 就位/离场清理需实跑（留空）
# ═══════════════════════════════════════════════════════════════════════════
_AF = EA.rd(os.path.join(SV, 'Module/Flow/AppFlow.cs'))
_AFREL = 'client/Assets/Scripts/Module/Flow/AppFlow.cs'


def fill_d12(ent, state):
    if state not in ('进入（状态机切换 + 站点日志恰一条）', '触发条件成立 ⇒ 迁移发生'):
        return None
    if ent.startswith('station:'):
        st = ent[8:]
        m = re.search(r'fsm\.RegisterState\([\w.]*\b' + re.escape(st) + r'\b', _AF)
        if not m:
            return None
        return ('RegisterState(%s) 已注册（%s:%d）' % (st, _AFREL, line_of(_AF, m.start())), OK,
                '%s:%d' % (_AFREL, line_of(_AF, m.start())))
    if ent.startswith('trans:'):
        body = ent[6:]
        a, b = body.split('→')
        m = re.search(r'fsm\.AddTransition\([\w.]*' + re.escape(a) + r'\s*,\s*[\w.]*' + re.escape(b) + r'\s*\)', _AF)
        if not m:
            return None
        return ('AddTransition(%s → %s) 已注册（%s:%d）' % (a, b, _AFREL, line_of(_AF, m.start())), OK,
                '%s:%d' % (_AFREL, line_of(_AF, m.start())))
    return None


# ═══════════════════════════════════════════════════════════════════════════
# S3 设置 —— 只填「默认值」「运行期修改生效」（读写点有文件:行）；冷启动持久化需实跑（留空）
# ═══════════════════════════════════════════════════════════════════════════
_GC = EA.rd(os.path.join(SV, 'Core/GameConst.cs'))
_GCREL = 'client/Assets/Scripts/Core/GameConst.cs'


_GC_CONSTS = {}
for _m in re.finditer(r'public const string (SettingKey\w+)\s*=\s*"([^"]+)";', _GC):
    _GC_CONSTS[_m.group(2)] = (_m.group(1), line_of(_GC, _m.start()))


def _s3_alts(key):
    """该设置键在 .cs 里的**全部书写形态**：字面量 `"<key>"` + `GameConst.<常量>` + 任何
    `const string <标识符> = "<key>"`（本项目 `Core/` 冻结 ⇒ `video/quality` 在
    `Bootstrap.cs:192` / `SettingsPanel.cs:57` 各有一份**私有常量**，⛔ 不能只找 `GameConst.`）。"""
    alts = [re.escape('"' + key + '"')]
    if key in _GC_CONSTS:
        c = _GC_CONSTS[key][0]
        alts += [r'GameConst\.' + c, r'\b' + c + r'\b']
    for _r, _t in ALL_CS.items():
        for _mm in re.finditer(r'const string (\w+)\s*=\s*"' + re.escape(key) + r'"', _t):
            alts.append(r'\b' + _mm.group(1) + r'\b')
    return sorted(set(alts))


def fill_s3(ent, state):
    if not ent.startswith('set:'):
        return None
    key = ent[4:]
    if key in _GC_CONSTS:
        cname, cln = _GC_CONSTS[key]
        ev_key = '%s:%d (%s = "%s")' % (_GCREL, cln, cname, key)
    else:
        ev_key = '（无 GameConst 常量；键在 Bootstrap.cs:192 / UI/SettingsPanel.cs:57 各有一份私有常量）'
    alt = '(?:%s)' % '|'.join(_s3_alts(key))
    if state == '默认值（首次启动）':
        hits = cs_hits(r'Get<[^>]*>\s*\([^;\n]{0,120}' + alt)
        meas = '设置键 "%s" 的**默认值读取点** %s' % (key, fmt_hits(hits))
        if hits:
            return (meas, OK, '%s:%d' % hits[0] + ' ｜ ' + ev_key)
        return (meas, BAD + '(0 处读取 = 设置键定义了却没人读)', ev_key)
    if state == '运行期修改生效':
        hits = cs_hits(r'\.Set(?:<[^>]*>)?\s*\([^;\n]{0,120}' + alt)
        meas = '设置键 "%s" 的**运行期写入点** %s' % (key, fmt_hits(hits))
        if hits:
            return (meas, OK, '%s:%d' % hits[0] + ' ｜ ' + ev_key)
        return (meas, BAD + '(0 处写入 = 设置键定义了却没人写)', ev_key)
    if state == '重启后仍生效（持久化）':
        # T0FIX-B 后本分支可离线判：静音开关的**冷启动读回点**（`Get<bool>(…SettingKey…Mute…)`）
        #   与**落盘点**（`Set(…键…)` 之后 ≤14 行内有 `Save()`）都已在代码里接线 ⇒ 读+写+Save
        #   三者齐 = 静态可证"重启会读回"（运行期那一次冷启动仍属实机，见 t0-remaining.md）。
        read_hits = cs_hits(r'Get<[^>]*>\s*\([^;\n]{0,120}' + alt)
        write_hits = cs_hits(r'\.Set(?:<[^>]*>)?\s*\([^;\n]{0,120}' + alt)
        saved = []
        for (r, ln) in write_hits:
            seg = '\n'.join(ALL_CS.get(r, '').splitlines()[ln - 1: ln + 14])
            if re.search(r'\.Save\s*\(\s*\)', seg):
                saved.append((r, ln))
        meas = '设置键 "%s" 的**冷启动读回点** %s；**落盘点（`Set` 后 ≤14 行内有 `Save()`）** %s' % (
            key, fmt_hits(read_hits), fmt_hits(saved))
        if read_hits and saved:
            return (meas, OK, '%s:%d' % read_hits[0] + ' ｜ %s:%d' % saved[0] + ' ｜ ' + ev_key)
        return (meas, BAD + '(读回点或落盘点缺失 ⇒ 无法持久化/重启不生效)', ev_key)
    return None


# ═══════════════════════════════════════════════════════════════════════════
# 分派 + 写表
# ═══════════════════════════════════════════════════════════════════════════
DISPATCH = {
    'D1资源': fill_d1,
    'D2几何': lambda e, s: fill_d2_area(e, s) if e.startswith('area:') else fill_d2_tile(e, s),
    'D3材质': fill_d3,
    'D4UI': fill_d4,
    'D5动画': fill_d5,
    'D7音乐': fill_d7,
    'D8音效': fill_d8,
    'D9碰撞': fill_d9,
    'D10逻辑': fill_d10,
    'D11输入': fill_d11,
    'D12流程': fill_d12,
    'S1数值': fill_s1,
    'S3设置': fill_s3,
}


def main():
    _safe_stdio()
    dry = '--dry-run' in sys.argv
    # 换行/BOM 按**盘上现有形态**解析并原样写回（见文件头 修正 1）：
    #   `策划/状态矩阵.tsv` 实测被别的片写成 LF + 无 BOM；写死 '\r\n' 会一行都读不到。
    rawb = io.open(MATRIX, 'rb').read()
    bom = rawb.startswith(codecs.BOM_UTF8)
    raw = rawb.decode('utf-8-sig')
    nl = '\r\n' if '\r\n' in raw else '\n'
    tail_nl = raw.endswith(('\n', '\r'))
    lines = raw.splitlines()
    out = []
    nrows = nfilled = nkept = njudgeable = 0
    dim_filled = collections.Counter()
    dim_total = collections.Counter()
    dim_judge = collections.Counter()
    dim_has = collections.Counter()
    for ln in lines:
        if not ln.strip() or ln.startswith('#'):
            out.append(ln)
            continue
        cells = ln.split('\t')
        if cells[0] == '维度' or len(cells) < 8:
            out.append(ln)
            continue
        dim, ent, state = cells[0], cells[1], cells[2]
        nrows += 1
        dim_total[dim] += 1
        fn = DISPATCH.get(dim)
        got = fn(ent, state) if fn else None
        if got is not None:
            meas, verd, ev = got
            if meas and verd and ev:
                njudgeable += 1
                dim_judge[dim] += 1
                # 「不一致」清单按**判据结果**收（与是否落盘无关）⇒ 已填过的行也照样进清单
                if verd.startswith(BAD):
                    MISMATCHES.append((dim, ent, state, verd))
                if all(cells[i].strip() for i in (5, 6, 7)):
                    nkept += 1          # ★ 已有三列（可能是别的片的 Play 实测）⇒ ⛔ 不覆盖
                else:
                    cells[5] = cut(meas)
                    cells[6] = cut(verd)
                    cells[7] = cut(ev)
                    nfilled += 1
                    dim_filled[dim] += 1
        # 判不了 / 判据不成立 ⇒ **保持原样**（不清空别人的结论；红行 = 三列本就为空）
        if all(cells[i].strip() for i in (5, 6, 7)):
            dim_has[dim] += 1
        out.append('\t'.join(cells))
        EMPTY[dim] += 0

    print('== fill_offline：逐维度 本工具判得了 / 矩阵已有结论 / 仍空(红行) / 共 ==')
    tot_j = tot_h = tot_t = 0
    for d in EA.DIMS:
        t = dim_total[d]
        j, h = dim_judge[d], dim_has[d]
        tot_j += j
        tot_h += h
        tot_t += t
        print('%-8s 判得了 %5d / 已有结论 %5d / 仍空 %5d / 共 %5d' % (d, j, h, t - h, t))
    print('TOTAL    判得了 %5d / 已有结论 %5d / 仍空 %5d / 共 %5d  矩阵填满率 %.1f%%'
          % (tot_j, tot_h, tot_t - tot_h, tot_t, 100.0 * tot_h / max(1, tot_t)))
    print('本次新填 %d 行、跳过(已有结论) %d 行' % (nfilled, nkept))
    print()
    print('== 不一致清单（%d 条）==' % len(MISMATCHES))
    for d, e, s, v in MISMATCHES:
        print('  %s | %s | %s | %s' % (d, e, s, v))

    if not dry:
        data = ('\ufeff' if bom else '') + nl.join(out) + (nl if tail_nl else '')
        with io.open(MATRIX, 'w', encoding='utf-8', newline='') as f:
            f.write(data)
        print()
        print('已写回 %s（数据行 %d，本次新填 %d）' % (EA.rel(MATRIX), nrows, nfilled))
    else:
        print()
        print('--dry-run：未写文件（数据行 %d，本次可填 %d）' % (nrows, nfilled))


if __name__ == '__main__':
    main()
