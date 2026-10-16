# -*- coding: utf-8 -*-
"""从导出好的 pack 里**按实测颜色**挑出「草地 / 泥地 / 石地 / 岩壁」等瓦片，并打印成
可直接粘进 `Module/Map/MapView.cs` 的 C# 数组字面量。

为什么要这一步：`GroundKeyOf` 必须指出**具体哪一张原版瓦片**。靠文件名猜（"000.png 一定是草地"）
没有依据；本脚本读每张 PNG 的真实像素求均值色 + 不透明面积，按色相/明度分类 ⇒ 挑出来的
每一块都能复现（脚本 + 输出留档）。

分类口径（简单、可复现）：
    green  : g - max(r, b) >= 8      → 草
    brown  : r - b >= 24 且 r >= g   → 泥 / 土 / 木
    gray   : max-min <= 22           → 石 / 岩
    另外按不透明面积过滤掉"半透明碎片"（area < 该 pack 最大面积的 35%）。

用法：
    python pick_tiles.py --d2 <Resources/Clover/D2 根> --pack Tiles/town_floor --as grass --top 8
    python pick_tiles.py --d2 <...> --pack Objects/town_fence --as all
"""

import json
import os
import struct
import sys
import zlib

try:
    from . import dt1 as dt1mod
except ImportError:
    import dt1 as dt1mod


def read_png(path):
    """读回自己写出的 PNG（8 位 RGBA、filter 0）→ (w, h, rgba bytes)。"""
    with open(path, 'rb') as fh:
        data = fh.read()
    assert data[:8] == b'\x89PNG\r\n\x1a\n', path
    ptr = 8
    w = h = 0
    idat = bytearray()
    while ptr < len(data):
        (length,) = struct.unpack_from('>I', data, ptr)
        tag = data[ptr + 4: ptr + 8]
        payload = data[ptr + 8: ptr + 8 + length]
        if tag == b'IHDR':
            w, h, depth, color = struct.unpack_from('>IIBB', payload, 0)
            assert depth == 8 and color == 6, '只支持 8 位 RGBA'
        elif tag == b'IDAT':
            idat += payload
        elif tag == b'IEND':
            break
        ptr += 12 + length

    raw = zlib.decompress(bytes(idat))
    stride = w * 4
    out = bytearray(w * h * 4)
    p = 0
    for y in range(h):
        assert raw[p] == 0, '只支持 filter=0'
        p += 1
        out[y * stride:(y + 1) * stride] = raw[p:p + stride]
        p += stride
    return w, h, bytes(out)


def stats(path):
    w, h, rgba = read_png(path)
    sr = sg = sb = 0
    n = 0
    for i in range(w * h):
        a = rgba[i * 4 + 3]
        if a == 0:
            continue
        sr += rgba[i * 4]
        sg += rgba[i * 4 + 1]
        sb += rgba[i * 4 + 2]
        n += 1
    if n == 0:
        return None
    return w, h, n, sr / n, sg / n, sb / n


def classify(r, g, b):
    mx = max(r, g, b)
    mn = min(r, g, b)
    if g - mx + (g - mn) * 0 >= 0 and g - max(r, b) >= 8:
        return 'green'
    if r - b >= 24 and r >= g:
        return 'brown'
    if mx - mn <= 22:
        return 'gray'
    return 'other'


def main(argv):
    d2 = argv[argv.index('--d2') + 1]
    pack = argv[argv.index('--pack') + 1]
    want = argv[argv.index('--as') + 1] if '--as' in argv else 'all'
    top = int(argv[argv.index('--top') + 1]) if '--top' in argv else 10
    orientation = int(argv[argv.index('--orientation') + 1]) if '--orientation' in argv else None
    walk = argv[argv.index('--walk') + 1] if '--walk' in argv else None

    directory = os.path.join(d2, pack.replace('/', os.sep))
    manifest_path = os.path.join(directory, 'manifest.json')
    meta = {}
    if os.path.exists(manifest_path):
        with open(manifest_path, encoding='utf-8') as fh:
            m = json.load(fh)
        meta = dict((t['file'], t) for t in m['tiles'])

    rows = []
    for f in sorted(os.listdir(directory)):
        if not f.endswith('.png'):
            continue
        s = stats(os.path.join(directory, f))
        if s is None:
            rows.append({'file': f, 'area': 0, 'kind': 'empty'})
            continue
        w, h, n, r, g, b = s
        rows.append({'file': f, 'w': w, 'h': h, 'area': n, 'r': int(r), 'g': int(g),
                     'b': int(b), 'kind': classify(r, g, b), 'meta': meta.get(f, {})})

    if orientation is not None:
        rows = [x for x in rows if x.get('meta', {}).get('orientation') == orientation]
    if walk is not None:
        want_walk = (walk == 'true')
        rows = [x for x in rows if bool(x.get('meta', {}).get('walk')) == want_walk]

    max_area = max((x['area'] for x in rows), default=1)
    solid = [x for x in rows if x['area'] >= max_area * 0.35]

    print('%s  %d 张（面积 ≥35%% 最大值的 %d 张）' % (pack, len(rows), len(solid)))
    groups = {}
    for x in solid:
        groups.setdefault(x['kind'], []).append(x)

    for kind in ('green', 'brown', 'gray', 'other'):
        items = groups.get(kind, [])
        items.sort(key=lambda x: -(x['r'] * 0.3 + x['g'] * 0.6 + x['b'] * 0.1))
        print('  [%s] %d 张' % (kind, len(items)))
        for x in items[:top]:
            mm = x['meta']
            print('     %s  %dx%d  rgb=(%3d,%3d,%3d) area=%5d  main=%s sub=%s o=%s'
                  % (x['file'], x['w'], x['h'], x['r'], x['g'], x['b'], x['area'],
                     mm.get('main'), mm.get('sub'), mm.get('orientation')))

        if want in (kind, 'all') and kind != 'other':
            keys = ['"' + pack + '/' + x['file'][:-4] + '"' for x in items[:top]]
            print('     C# → new[] { %s }' % (', '.join(keys)))

    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
