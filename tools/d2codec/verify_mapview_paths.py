# -*- coding: utf-8 -*-
"""自检：`Module/Map/MapView.cs` 里引用的**每一张瓦片路径**是否真的落在磁盘上。

为什么必须跑：`Resources.Load` 找不到资源时**不报错、只返回 null** —— 瓦片会静默退回纯色占位，
正是用户投诉的那种"看不出坏、但就是不对"。所以把"代码里写的路径"与"磁盘上的 PNG"逐条对上。

口径：MapView.cs 的瓦片表里写的是 `"<pack>/<idx>"`（3 位补零），
     `ResPaths.Tile(key)` → `D2/Tiles/<key>.png`；`ResPaths.ObjectSprite(key)` → `D2/Objects/<key>.png`。
     两张目录任一命中即算存在（地砖在 Tiles/、墙/物件在 Objects/）。

用法：
    python verify_mapview_paths.py [--mapview <MapView.cs>] [--d2 <Resources/Clover/D2 根>]
"""

import os
import re
import sys

DEFAULT_MAPVIEW = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    'client', 'Assets', 'Scripts', 'Module', 'Map', 'MapView.cs')
DEFAULT_D2 = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    'client', 'Assets', 'Resources', 'Clover', 'D2')

# 瓦片键：<pack>/<3 位序号>（pack = 小写字母+下划线）
KEY_RE = re.compile(r'"([a-z][a-z0-9_]*)/(\d{3})"')


def _exists(d2, key):
    return (os.path.exists(os.path.join(d2, 'Tiles', key.replace('/', os.sep) + '.png'))
            or os.path.exists(os.path.join(d2, 'Objects', key.replace('/', os.sep) + '.png')))


def _block(src, name):
    """取 `public static readonly string[] <name> = { ... };` 里的全部字符串字面量。"""
    m = re.search(r'string\[\]\s+' + name + r'\s*=\s*\{(.*?)\};', src, re.S)
    if not m:
        print('! MapGenTownLayout.cs：没找到 %s 表' % name)
        return []
    return re.findall(r'"([^"]*)"', m.group(1))


def _check_town(d2):
    """解 `MapGenTownLayout.cs`：Packs + GroundRows/ObjectRows 的 6 字符编码 → 逐格瓦片键。"""
    path = os.path.join(os.path.dirname(DEFAULT_MAPVIEW), 'MapGenTownLayout.cs')
    if not os.path.exists(path):
        print('（没有 %s，跳过原版布局瓦片检查）' % os.path.basename(path))
        return []

    with open(path, encoding='utf-8') as fh:
        src = fh.read()

    # 必须按**块**取，不能全文件扫 `"xxx",`：kind 表的某一行（整行都是小写字母，例如
    #    栅栏整行 `"fffff...fr"`）也会被 `"([a-z]+)",` 匹配到，导致 packId 整体错位
    packs = _block(src, 'Packs') or []
    #   行全过滤掉了 ⇒ 这张表实际一格都没检查，"PASS" 是假的）。出处：`Levels.txt`
    #   「Act 1 - Town」= 56×40，见生成器 `export_town_layout.py`。
    ground_rows = _block(src, 'GroundRows')
    object_rows = _block(src, 'ObjectRows')
    all_rows = ground_rows + object_rows
    if not packs:
        print('! MapGenTownLayout.cs：没解析到 Packs 表')
        return []
    if not all_rows:
        print('! MapGenTownLayout.cs：没解析到 GroundRows/ObjectRows')
        return []

    gw = len(all_rows[0]) // 6
    rows = [r for r in all_rows if len(r) == gw * 6]
    if len(ground_rows) != len(object_rows) or len(rows) != len(all_rows):
        print('! MapGenTownLayout.cs：ground %d 行 / object %d 行 / 行宽一致 %d 行（首行 %d 列）'
              '⇒ 表结构不一致，本检查会漏格' % (len(ground_rows), len(object_rows), len(rows), gw))

    keys = set()
    for row in rows:
        for i in range(gw):
            cell = row[i * 6:(i + 1) * 6]
            if cell[0] == '-':
                continue
            pid = int(cell[:3])
            if pid >= len(packs):
                print('! packId %d 越界（Packs 只有 %d 项）' % (pid, len(packs)))
                continue
            keys.add('%s/%s' % (packs[pid], cell[3:]))
    print('MapGenTownLayout.cs：Packs=%d 个，%d 行 × %d 列，逐格瓦片键 %d 个（ground+object 去重）'
          % (len(packs), len(rows), gw, len(keys)))
    return sorted(keys)


def main(argv):
    mapview = argv[argv.index('--mapview') + 1] if '--mapview' in argv else DEFAULT_MAPVIEW
    d2 = argv[argv.index('--d2') + 1] if '--d2' in argv else DEFAULT_D2

    with open(mapview, encoding='utf-8') as fh:
        src = fh.read()

    keys = []
    seen = set()
    for m in KEY_RE.finditer(src):
        key = '%s/%s' % (m.group(1), m.group(2))
        if key in seen:
            continue
        seen.add(key)
        keys.append(key)

    print('MapView.cs 里引用的瓦片键 %d 个（%s）' % (len(keys), os.path.basename(mapview)))
    missing = []
    for key in keys:
        if not _exists(d2, key):
            missing.append(key)
            print('  MISS %-24s' % key)

    town_keys = _check_town(d2)
    missing_town = [k for k in town_keys if not _exists(d2, k)]
    for k in missing_town:
        print('  MISS(城镇布局) %s' % k)

    if missing or missing_town:
        print('缺失 %d + %d 个（Resources.Load 会静默返回 null，退回纯色占位）'
              % (len(missing), len(missing_town)))
        return 1

    print('全部命中：MapView 的 %d 张 + 原版城镇布局的 %d 张瓦片都能在磁盘上找到'
          % (len(keys), len(town_keys)))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
