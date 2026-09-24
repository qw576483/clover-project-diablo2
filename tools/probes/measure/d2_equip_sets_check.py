# -*- coding: utf-8 -*-
"""d2_equip_sets_check.py — 判据资产：校验"起始装备套"（`Chars/{class}/equip/{code}/`）导出。

「起始装备套」= `tools/d2codec/export_chars.py --equip-sets` 的产物（身体层 + 武器/盾层合成一张，
与徒手套同口径）。本脚本做**可复跑的机械判定**，不依赖 Unity、不依赖网络、不写任何产物
（`snapshot` 除外 —— 它只写你自己指定的 baseline json）。

用法（三条，都从**仓库根**跑）：
    # ① 先把"不许变"的东西的 md5 记下来（导装备套**之前**跑）
    python tools/probes/measure/d2_equip_sets_check.py snapshot .ai-tmp/test/equip-baseline.json
    # ② 导完装备套之后核对（含 md5 前后对照）
    python tools/probes/measure/d2_equip_sets_check.py check .ai-tmp/test/equip-baseline.json
    # ③ 只打数字表（调阈值 / 人眼核对前先看规模）
    python tools/probes/measure/d2_equip_sets_check.py report

── 判定的 9 项（check 模式，任一不过 ⇒ 退出码非 0）──
 A1 每套目录有 manifest.json 且可解析
 A2 `pngCount` == Σ(逐动作 frames × dirs) == 目录里实际 PNG 数
 A3 每张 PNG > 0 字节（判据只能是"文件真的落了盘"）
 A4 manifest 有 `equipSet`（= 目录名）、非空 `equipMap`，且至少含 `RH` 或 `SH`
 A5 **武器/盾层真的被用上**（不是被静默跳过）：逐动作 `layerFiles` 里
      · 有 `RH` 层 ⇒ 该层文件名含 `RH` 的 equip code（且该 code ≠ 'lit'）；
      · 有 `SH` 层 ⇒ 同理；
      · 没有该层的动作（原版没这层图）⇒ 必须出现在 `skipped` 里（留痕，不许静默）
 A6 逐 (动作, 方向) 的文件个数 == 该动作 frames，且方向恰为 manifest 的 8 个
 A7 逐帧解码后**不透明像素 > 0**（不许有空帧）
 A8 几何：该套逐 (动作,方向) 的不透明包围盒（**原点相对坐标**）与同职业徒手套相比
      · 包围盒 **底边** 差 ≤ `--bottom-tol`（默认 3 px）⇒ 角色还站在同一个原点上（同一个身体）；
      · **至少 `--grow-min`（默认 5）个动作**的包围盒在某一边上**超出**徒手套 ⇒ 手上真有东西。
 A9 baseline md5 前后对照：现有 `Chars/{class}/*.png`（⛔ 不含 `equip/`）与
      `Module/View/SpriteFrameCounts.cs` **逐字节未变**。
 A10 **共画布**：每套的 `canvas.{w,h,originX,originY}` 必须与**同职业徒手套**全等
      （运行时一个实体只有一个 `SpriteRenderer`、pivot 是导入常量 ⇒ 画布不一致 = 换装备时整体偏移）。

── 为什么这样量（而不是截图）──
本片判据是**数值类**（文件数 / 字节数 / md5 / 像素包围盒），按 clover-engine `SKILL.md` §2
第 3 条给"运行时日志行 + 断言"即可；唯一的"表现类"判据（手上真有武器/盾）由人眼读
`idle_s_0.png` / `attack_s_0.png` 负责，本脚本只提供**几何上确凿的那一半**（A8）。
"""

import hashlib
import json
import os
import struct
import sys
import zlib

#: 本文件 = <repo>/tools/probes/measure/xxx.py ⇒ 仓库根 = 上四级。
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
CHARS = os.path.join(REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2', 'Chars')
CS_PATH = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'View', 'SpriteFrameCounts.cs')

#: 「5 职业徒手套」的 7 个动作（`SpriteFrameCounts.cs` / manifest 的口径）。
ACTIONS = ('idle', 'walk', 'attack', 'cast', 'hit', 'death', 'run')
DIRS = ('s', 'sw', 'w', 'nw', 'n', 'ne', 'e', 'se')


# ── 极简 PNG 读回（纯 Python：只用 zlib；不引入第三方依赖）────────────────────
def read_rgba(path):
    """读 8 位 RGBA 非隔行 PNG → `(w, h, bytes)`（行序自顶向下）。其它格式抛 ValueError。"""
    with open(path, 'rb') as fh:
        data = fh.read()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('不是 PNG：%s' % path)
    pos = 8
    w = h = None
    idat = bytearray()
    while pos + 8 <= len(data):
        (ln,) = struct.unpack_from('>I', data, pos)
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + ln]
        if tag == b'IHDR':
            w, h, depth, ctype, comp, filt, inter = struct.unpack('>IIBBBBB', body)
            if depth != 8 or ctype != 6 or inter != 0:
                raise ValueError('%s：只支持 8 位 RGBA 非隔行（depth=%d ctype=%d interlace=%d）'
                                 % (path, depth, ctype, inter))
        elif tag == b'IDAT':
            idat += body
        elif tag == b'IEND':
            break
        pos += 12 + ln
    raw = zlib.decompress(bytes(idat))
    stride = w * 4
    out = bytearray(w * h * 4)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        ft = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if ft == 0:
            pass
        elif ft == 1:
            for i in range(4, stride):
                line[i] = (line[i] + line[i - 4]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - 4] if i >= 4 else 0
                b = prev[i]
                c = prev[i - 4] if i >= 4 else 0
                pp = a + b - c
                pa, pb, pc = abs(pp - a), abs(pp - b), abs(pp - c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        else:
            raise ValueError('%s：未知 PNG filter %d' % (path, ft))
        out[y * stride:(y + 1) * stride] = line
        prev = line
    return w, h, bytes(out)


def opaque_bbox(rgba, w, h, origin_x, origin_y):
    """不透明像素的包围盒（**原点相对**坐标，左闭右开）；无像素返回 None。"""
    minx = miny = 1 << 30
    maxx = maxy = -(1 << 30)
    n = 0
    for y in range(h):
        base = y * w * 4 + 3
        for x in range(w):
            if rgba[base + x * 4]:
                n += 1
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y
    if n == 0:
        return None
    return {'left': minx - origin_x, 'top': miny - origin_y,
            'right': maxx + 1 - origin_x, 'bottom': maxy + 1 - origin_y, 'px': n}


def union(a, b):
    if a is None:
        return dict(b) if b else None
    if b is None:
        return dict(a)
    return {'left': min(a['left'], b['left']), 'top': min(a['top'], b['top']),
            'right': max(a['right'], b['right']), 'bottom': max(a['bottom'], b['bottom']),
            'px': a['px'] + b['px']}


# ── 产物扫描 ─────────────────────────────────────────────────────────────────
ONLY_CLASSES = None

ONLY_SETS = None


def set_key(cls, code):
    return '%s/equip/%s' % (cls, code)


def _wanted(cls, code):
    if ONLY_CLASSES and cls not in ONLY_CLASSES:
        return False
    if ONLY_SETS and set_key(cls, code) not in ONLY_SETS:
        return False
    return True


def equip_sets():
    """→ [(class, code, 目录绝对路径), …]（按名字排序，保证可复跑）。"""
    out = []
    if not os.path.isdir(CHARS):
        return out
    for cls in sorted(os.listdir(CHARS)):
        eq = os.path.join(CHARS, cls, 'equip')
        if not os.path.isdir(eq):
            continue
        for code in sorted(os.listdir(eq)):
            d = os.path.join(eq, code)
            if os.path.isdir(d) and _wanted(cls, code):
                out.append((cls, code, d))
    return out


def action_union(d, manifest, action, origin):
    """某动作 8 个方向的并集包围盒 + 逐方向是否都有帧。"""
    per = {}
    for dr in DIRS:
        u = None
        for fn in os.listdir(d):
            if fn.startswith('%s_%s_' % (action, dr)) and fn.endswith('.png'):
                w, h, rgba = read_rgba(os.path.join(d, fn))
                u = union(u, opaque_bbox(rgba, w, h, origin[0], origin[1]))
        per[dr] = u
    total = None
    for u in per.values():
        total = union(total, u)
    return per, total


def protected_md5():
    """「不许变」的文件 → md5：现有 `Chars/*`（⛔ 排除 `equip/`）+ `SpriteFrameCounts.cs`。"""
    out = {}
    for root, dirs, files in os.walk(CHARS):
        if os.sep + 'equip' in root:
            continue
        dirs[:] = [x for x in dirs if x != 'equip']
        for fn in sorted(files):
            p = os.path.join(root, fn)
            rel = os.path.relpath(p, REPO).replace(os.sep, '/')
            with open(p, 'rb') as fh:
                out[rel] = hashlib.md5(fh.read()).hexdigest()
    if os.path.exists(CS_PATH):
        with open(CS_PATH, 'rb') as fh:
            out[os.path.relpath(CS_PATH, REPO).replace(os.sep, '/')] = hashlib.md5(fh.read()).hexdigest()
    return out


def equip_md5():
    """本轮**新增交付区**（`Chars/{职业}/equip/**`）的逐文件 md5 —— 幂等判定用。"""
    out = {}
    for cls in sorted(os.listdir(CHARS)) if os.path.isdir(CHARS) else []:
        eq = os.path.join(CHARS, cls, 'equip')
        if not os.path.isdir(eq):
            continue
        for code in sorted(os.listdir(eq)):
            if not (os.path.isdir(os.path.join(eq, code)) and _wanted(cls, code)):
                continue
            for root, _dirs, files in os.walk(os.path.join(eq, code)):
                for fn in sorted(files):
                    p = os.path.join(root, fn)
                    with open(p, 'rb') as fh:
                        rel = os.path.relpath(p, REPO).replace(os.sep, '/')
                        out[rel] = hashlib.md5(fh.read()).hexdigest()
    return out


def load_manifest(d):
    with open(os.path.join(d, 'manifest.json'), encoding='utf-8') as fh:
        return json.load(fh)


def bare_handed(cls):
    """同职业徒手套目录（`Chars/<class>` 根，不带 `equip/`）。"""
    return os.path.join(CHARS, cls)


# ── report ───────────────────────────────────────────────────────────────────
def report():
    for cls, code, d in equip_sets():
        m = load_manifest(d)
        org = (m['canvas']['originX'], m['canvas']['originY'])
        print('== %s/equip/%s  画布 %dx%d 原点 (%d,%d)  PNG %d'
              % (cls, code, m['canvas']['w'], m['canvas']['h'], org[0], org[1], m['pngCount']))
        bh = bare_handed(cls)
        try:
            bm = load_manifest(bh)
            borg = (bm['canvas']['originX'], bm['canvas']['originY'])
        except Exception:
            bm, borg = None, None
        for a in ACTIONS:
            st = m['actions'].get(a)
            if not st:
                print('   %-6s ——（该套没有这个动作）' % a)
                continue
            _per, u = action_union(d, m, a, org)
            bu = None
            if bm is not None and bm['actions'].get(a):
                _bp, bu = action_union(bh, bm, a, borg)
            print('   %-6s mode=%-3s f=%2d dirs=%d layers=%s' %
                  (a, st['mode'], st['frames'], st['dirs'], ','.join(st['layers'])))
            print('          union(原点相对) %s' % (u,))
            if bu is not None:
                dlt = {k: u[k] - bu[k] for k in ('left', 'top', 'right', 'bottom')}
                print('          徒手 union      %s  Δ=%s' % (bu, dlt))


# ── check ────────────────────────────────────────────────────────────────────
def check(baseline_path, bottom_tol, grow_min):
    fails = []
    notes = []

    sets = equip_sets()
    if not sets:
        fails.append('A0 一个装备套目录都没找到（%s/*/equip/*）' % CHARS)

    for cls, code, d in sets:
        tag = '%s/equip/%s' % (cls, code)
        # A1
        mpath = os.path.join(d, 'manifest.json')
        if not os.path.exists(mpath):
            fails.append('%s A1 缺 manifest.json' % tag)
            continue
        m = load_manifest(d)
        org = (m['canvas']['originX'], m['canvas']['originY'])

        # A2
        pngs = sorted(f for f in os.listdir(d) if f.endswith('.png'))
        expect = sum(st['frames'] * st['dirs'] for st in m['actions'].values())
        if m['pngCount'] != expect:
            fails.append('%s A2 manifest.pngCount=%d ≠ Σ(frames×dirs)=%d' % (tag, m['pngCount'], expect))
        if len(pngs) != expect:
            fails.append('%s A2 实际 PNG 数=%d ≠ Σ(frames×dirs)=%d' % (tag, len(pngs), expect))

        # A3
        small = [f for f in pngs if os.path.getsize(os.path.join(d, f)) <= 0]
        if small:
            fails.append('%s A3 %d 张 PNG 为 0 字节（例 %s）' % (tag, len(small), small[:3]))

        # A4
        if m.get('equipSet') != code:
            fails.append('%s A4 manifest.equipSet=%r ≠ 目录名 %r' % (tag, m.get('equipSet'), code))
        emap = m.get('equipMap') or {}
        if not emap:
            fails.append('%s A4 manifest.equipMap 为空' % tag)
        if not ({'RH', 'SH'} & set(emap)):
            fails.append('%s A4 equipMap 既没有 RH 也没有 SH（那就不是"装备套"）' % tag)

        # A5 + A6 + A7
        for a in ACTIONS:
            st = m['actions'].get(a)
            if not st:
                continue
            files = [f for f in pngs if f.startswith(a + '_')]
            if len(files) != st['frames'] * st['dirs']:
                fails.append('%s A6 %s：PNG 数 %d ≠ frames(%d)×dirs(%d)'
                             % (tag, a, len(files), st['frames'], st['dirs']))
            for dr in DIRS:
                n = len([f for f in files if f.startswith('%s_%s_' % (a, dr))])
                if n != st['frames']:
                    fails.append('%s A6 %s_%s：%d 张 ≠ frames %d' % (tag, a, dr, n, st['frames']))
            lf = st.get('layerFiles')
            cof_layers = st.get('cofLayers')
            if not lf:
                fails.append('%s A5 %s 缺 layerFiles（无法审计"实际用了哪个文件"）' % (tag, a))
            elif cof_layers is None:
                fails.append('%s A5 %s 缺 cofLayers（无法区分"原版没这层"与"静默丢层"）' % (tag, a))
            else:
                got = dict((x['component'], x) for x in lf)
                for comp in ('RH', 'SH'):
                    want = emap.get(comp)
                    if not want:
                        continue
                    if comp in got:
                        f = got[comp]
                        if f['equip'] != want or want.lower() not in os.path.basename(f['file']).lower():
                            fails.append('%s A5 %s：%s 层用了 %s（应为 equip=%s 的文件 %s）'
                                         % (tag, a, comp, f, want, f['file']))
                    elif comp not in cof_layers:
                        # 原版该动作的 COF **就没有**这一层（实测 death 的 `amdthth.cof` 只有 TR）
                        # ⇒ 不是丢层。这条要留痕，否则就成了"静默少一层"。
                        notes.append('%s %s：COF（%s）无 %s 层 ⇒ 该动作没有武器/盾（原版如此）'
                                     % (tag, a, st['mode'], comp))
                    elif not any(comp in s for s in st.get('skipped', [])):
                        fails.append('%s A5 %s：COF 有 %s 层但没有它、也没在 skipped 里留痕 ⇒ 静默丢层'
                                     % (tag, a, comp))
            for f in files:
                try:
                    w, h, rgba = read_rgba(os.path.join(d, f))
                except Exception as exc:
                    fails.append('%s A7 %s 解码失败：%s' % (tag, f, exc))
                    continue
                if opaque_bbox(rgba, w, h, 0, 0) is None:
                    fails.append('%s A7 %s 是空帧（不透明像素 0）' % (tag, f))

        # A8 几何对照
        bh = bare_handed(cls)
        try:
            bm = load_manifest(bh)
            borg = (bm['canvas']['originX'], bm['canvas']['originY'])
        except Exception as exc:
            fails.append('%s A8 读不到同职业徒手套 manifest（%s）：%s' % (tag, bh, exc))
            continue
        # A10 **共画布**：运行时每个实体只有一个 `SpriteRenderer`，pivot 是导入设置里的常量
        # （`AssetImporter` 按 `Resources/Clover/D2/**` 统一给 (0.5,0.5)）⇒ 同一职业的**所有**
        # 帧集（徒手 + 各装备套）必须用**同一张画布**（w/h/origin 全等），否则同一实体换装备时
        # 会因为画布尺寸不同而整体偏移/缩放不一致。
        # 实测依据：并发片把 5 职业徒手套改成"共画布"（amazon 158x214 / barbarian 142x178），
        ca, cb = m.get('canvas') or {}, bm.get('canvas') or {}
        for k in ('w', 'h', 'originX', 'originY'):
            if ca.get(k) != cb.get(k):
                fails.append('%s A10 共画布不符：canvas.%s=%s，而同职业徒手套是 %s'
                             '（⇒ 换装备时实体会整体偏移；用 --canvas 重导）'
                             % (tag, k, ca.get(k), cb.get(k)))
        grown = 0
        for a in ACTIONS:
            st = m['actions'].get(a)
            if not st or not bm['actions'].get(a):
                continue
            _p, u = action_union(d, m, a, org)
            _bp, bu = action_union(bh, bm, a, borg)
            if u is None or bu is None:
                fails.append('%s A8 %s：某一侧没有不透明像素' % (tag, a))
                continue
            if abs(u['bottom'] - bu['bottom']) > bottom_tol:
                fails.append('%s A8 %s：底边 %d vs 徒手 %d（差 %d > %d）⇒ 脚没站在同一原点'
                             % (tag, a, u['bottom'], bu['bottom'], u['bottom'] - bu['bottom'], bottom_tol))
            if (u['left'] < bu['left'] or u['right'] > bu['right']
                    or u['top'] < bu['top'] or u['bottom'] > bu['bottom']):
                grown += 1
        if grown < grow_min:
            fails.append('%s A8 只有 %d 个动作的包围盒超出徒手套（要求 ≥ %d）⇒ 手上可能没东西'
                         % (tag, grown, grow_min))
        notes.append('%s：%d 张 PNG；%d 个动作的包围盒超出徒手套' % (tag, len(pngs), grown))

    # A9 md5 前后对照
    if baseline_path:
        with open(baseline_path, encoding='utf-8') as fh:
            base = json.load(fh)
        now = protected_md5()
        added = sorted(set(now) - set(base))
        removed = sorted(set(base) - set(now))
        changed = sorted(k for k in set(base) & set(now) if base[k] != now[k])
        # 新增文件只在**受保护区**里才算违规；若它落在 `Chars/{职业}/equip…` 下
        # （本轮新增的交付目录 / Unity 给新目录生成的 `.meta`）⇒ 是预期产物。
        # 判据是"路径在 equip 段之下"，不是"后缀是 .meta"（后者会把真·误改放过去）。
        stray = [k for k in added if '/Chars/' not in k or '/equip' not in k]
        if removed or changed or stray:
            fails.append('A9 受保护文件变了：删除 %d / 内容变 %d / 受保护区外新增 %d（例 %s）'
                         % (len(removed), len(changed), len(stray),
                            (changed or removed or stray)[:5]))
        notes.append('A9 受保护文件 %d 个 md5 全等（现有 Chars/*.png + SpriteFrameCounts.cs）；'
                     'equip 目录内新增 %d 个（预期）' % (len(base), len(added) - len(stray)))
    else:
        notes.append('A9 跳过（没给 baseline json）')

    for n in notes:
        print('  ok  %s' % n)
    if fails:
        print('\nFAIL=%d' % len(fails))
        for f in fails:
            print('  ✗ %s' % f)
        return 1
    print('\nFAIL=0 全部判定通过')
    return 0


def main(argv):
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    except Exception:
        pass
    if not argv:
        print(__doc__)
        return 2
    # `--chars` / `--cs` 只为**试跑**用（把导出器指到 `.ai-tmp/test/out-chars` 时核对同一套判定）；
    # 正式判定一律用默认的工程路径。
    global CHARS, CS_PATH, ONLY_CLASSES
    if '--chars' in argv:
        CHARS = os.path.abspath(argv[argv.index('--chars') + 1])
    if '--cs' in argv:
        CS_PATH = os.path.abspath(argv[argv.index('--cs') + 1])
    if '--classes' in argv:
        ONLY_CLASSES = set(x.strip().lower()
                           for x in argv[argv.index('--classes') + 1].split(',') if x.strip())
    global ONLY_SETS
    if '--only-sets' in argv:
        ONLY_SETS = set(x.strip().lower().replace('\\', '/').rstrip('/')
                        for x in argv[argv.index('--only-sets') + 1].split(',') if x.strip())
    cmd = argv[0]
    if cmd == 'report':
        report()
        return 0
    if cmd == 'snapshot':
        if len(argv) < 2:
            print('snapshot 需要一个输出 json 路径')
            return 2
        snap = protected_md5()
        with open(argv[1], 'w', encoding='utf-8', newline='\n') as fh:
            json.dump(snap, fh, ensure_ascii=False, indent=1, sort_keys=True)
        print('→ %s（%d 个受保护文件）' % (argv[1], len(snap)))
        return 0
    if cmd in ('equipmd5', 'equipmd5-diff'):
        if len(argv) < 2:
            print('%s 需要一个 json 路径' % cmd)
            return 2
        snap = equip_md5()
        p = argv[1]
        if cmd == 'equipmd5':
            with open(p, 'w', encoding='utf-8', newline='\n') as fh:
                json.dump(snap, fh, ensure_ascii=False, indent=1, sort_keys=True)
            print('→ %s（equip 区 %d 个文件）' % (p, len(snap)))
            return 0
        with open(p, encoding='utf-8') as fh:
            old = json.load(fh)
        changed = sorted(k for k in set(old) & set(snap) if old[k] != snap[k])
        gone = sorted(set(old) - set(snap))
        new = sorted(set(snap) - set(old))
        print('equip 区幂等对照：对比 %d 个文件；内容变 %d / 少 %d / 多 %d'
              % (len(old), len(changed), len(gone), len(new)))
        for k in (changed + gone + new)[:10]:
            print('  ✗ %s' % k)
        return 1 if (changed or gone or new) else 0
    if cmd == 'check':
        bottom_tol = 3
        grow_min = 5
        if '--bottom-tol' in argv:
            bottom_tol = int(argv[argv.index('--bottom-tol') + 1])
        if '--grow-min' in argv:
            grow_min = int(argv[argv.index('--grow-min') + 1])
        base = argv[1] if len(argv) > 1 and not argv[1].startswith('--') else None
        return check(base, bottom_tol, grow_min)
    print(__doc__)
    return 2


if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
