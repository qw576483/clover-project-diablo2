# -*- coding: utf-8 -*-
"""决定性事实：原版 DS1 的 objects 层里到底有没有**容器**单位（b1/cx/cu/cy）。

容器在原版 `objects.txt` 的 Id / Token / ds1 预设 id（本项目 `MapGenDeco.Kinds` 已登记）：
    Barrel  Id7   Token B1  ds1 id 95
    Chest5  cy(240)  ds1 id 64
    Chest6  cx(241)  ds1 id 65
    Chest7  cu(242)  ds1 id 66
（`MapGenDeco.Kinds` 的 Ds1Id 列 = ds1 objects 层的预设 id；见 `Resources/Clover/D2/Objects/deco-manifest.json`。）

本脚本扫 `原版资源/d2raw/data/global/tiles/ACT1/**` 全部 `.ds1`，列出 objects 层出现过的
(obj_type, obj_id) 频次，并点名容器 id 出现在哪些文件里。
"""
import os
import sys
from collections import Counter, defaultdict

REPO = r'C:\Work\Server\f-v2\clover-project-diablo2'
sys.path.insert(0, os.path.join(REPO, 'tools', 'd2codec'))
import ds1 as ds1mod  # noqa: E402

ROOT = os.path.join(REPO, '原版资源', 'd2raw', 'data', 'global', 'tiles', 'ACT1')
CONTAINER_IDS = {95: 'Barrel(b1)', 64: 'Chest5(cy)', 65: 'Chest6(cx)', 66: 'Chest7(cu)'}


def main():
    files = []
    for dirpath, _dirnames, filenames in os.walk(ROOT):
        for fn in filenames:
            if fn.lower().endswith('.ds1'):
                files.append(os.path.join(dirpath, fn))
    files.sort()
    print('ds1 文件数 = %d（根 = %s）' % (len(files), ROOT))

    by_type = Counter()
    by_id = Counter()
    container_hits = defaultdict(list)
    errs = 0
    for path in files:
        try:
            d = ds1mod.load_ds1(path)
        except Exception as e:  # noqa: BLE001
            errs += 1
            print('  [ERR] %s : %s' % (os.path.basename(path), e))
            continue
        objs = getattr(d, 'objects', None) or []
        for o in objs:
            by_type[o.obj_type] += 1
            by_id[(o.obj_type, o.obj_id)] += 1
            if o.obj_id in CONTAINER_IDS:
                rel = os.path.relpath(path, ROOT)
                container_hits[(o.obj_id, rel)].append((o.obj_type, o.x, o.y))

    print('objects 层单位总数 = %d（解析失败文件 %d）' % (sum(by_type.values()), errs))
    print('按 type 分布：' + ', '.join('type%d=%d' % (t, n) for t, n in sorted(by_type.items())))
    print('读到的 (type,id) 频次前 40：')
    for (t, i), n in by_id.most_common(40):
        tag = CONTAINER_IDS.get(i, '')
        print('   type=%d id=%-4d × %-5d %s' % (t, i, n, tag))

    print('')
    if not container_hits:
        print('★ 结论：ACT1 全部 ds1 的 objects 层里**一个容器单位都没有**' +
              '（没用到 id 95/64/65/66 中的任何一个）')
    else:
        print('★ 结论：容器单位**存在**，命中如下：')
        for (oid, rel), spots in sorted(container_hits.items()):
            print('   id=%d %s ← %s（%d 处，前 3 处：%s）'
                  % (oid, CONTAINER_IDS[oid], rel, len(spots), spots[:3]))


if __name__ == '__main__':
    main()
