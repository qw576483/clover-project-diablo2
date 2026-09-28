# -*- coding: utf-8 -*-
"""把水面瓦片的**调色板循环帧**解成 PNG 落进 Unity 工程（`Module/Map` 的水面动画用）。

背景（原版口径）
----------------
原版水面的动效来自 **ACT1 调色板的循环色段**：同一张瓦片不动，引擎把调色板里一段
连续索引的颜色**整体轮转一步**，瓦片上的明暗纹理就沿色阶走一格 ⇒ 看着像水波在动。
本项目没有运行期调色板循环 ⇒ 预先按循环偏移**重算 RGBA** 出 N 帧 PNG，运行期用
逐帧动画器（`Module/View/SpriteAnimator`）切图，等价于原版的循环。

循环色段的取值（**本项目新增，见 `策划/对照表.md` 的登记行**）
------------------------------------------------------------
    CYCLE = [233..237]   —— 以 233 起的一段**连续**索引。
    233 = 平色水墙瓦片 `moor_river/028` 唯一用到的索引（`ACT1/Pal.PL2` 索引 233 =
          RGBA(0,32,68)），234/235/236/237 紧邻其后，五个都是该调色板尾部的深色
          （蓝 / 深蓝 / 近黑 / 深灰），且 235/236/237 是 `river.dt1` 几乎每张瓦片都在用的
          暗部色阶。
    ⛔ 原版的**确切**循环区间与步进**未公开**（取证结论：`Pal.PL2` 里没有
      (start,end,rate) 段表 —— 该文件是"调色板 + 变换表"容器，1727 个 256 字节变换
      对应光照渐变 / 混合 / 色相偏移，与循环无关；参考工程 Diablerie 未实现循环；
      全部 ACT1 的 `.dt1` 的 `animated` 标志 = 0 ⇒ 也没有帧序列表）。本脚本只做
      "**把该调色板这段连续色阶整体轮转**"这一件事，不自造任何颜色（见自证 ③）。

产出
----
    client/Assets/Resources/Clover/D2/Tiles/<pack>/f<帧>/<idx>.png     orientation==0 的地砖帧
    client/Assets/Resources/Clover/D2/Objects/<pack>/f<帧>/<idx>.png   orientation!=0 的物件帧
    `<idx>` = dt1 内 tile 头的数组下标（与 `export_tiles.py` 同口径）。
    第 0 帧 = 不轮转 ⇒ **与 `export_tiles.py` 出的那张静态图逐字节相同**（自证 ⑤）。

用法：
    python tools/d2codec/export_water_frames.py [--raw <d2raw 根>] [--out <Resources/Clover/D2 根>]
"""

import os
import sys

try:
    from . import dt1 as dt1mod
    from . import pl2 as pl2mod
    from . import pngio
except ImportError:                                    # 直接 `python export_water_frames.py` 时无包上下文
    import dt1 as dt1mod
    import pl2 as pl2mod
    import pngio

_REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_RAW = os.path.join(_REPO, '原版资源', 'd2raw')
DEFAULT_OUT = os.path.join(_REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2')

#: 循环色段（连续索引；起止都含）。依据与出处见文件头。
CYCLE_FIRST = 233
CYCLE_LAST = 237
CYCLE = list(range(CYCLE_FIRST, CYCLE_LAST + 1))

#: 帧数 = 色段长度（每帧轮转一步，走满一圈回到原色）。
FRAMES = len(CYCLE)

#: 要出帧的 dt1（原版路径 → pack 名）。水面瓦片族 = `moor_river`。
PACKS = [
    ('data/global/tiles/ACT1/OUTDOORS/river.dt1', 'moor_river'),
]

PALETTE_REL = 'data/global/palette/ACT1/Pal.PL2'

#: 平色水墙瓦片：它整张只有一个索引，且必须是循环色段的第一个。
FLAT_WATER_TILE = ('moor_river', 28)


def rotated_palette(base, step):
    """把 `CYCLE` 这一段整体轮转 `step` 步（其余索引不动）。返回新的 256 项表。"""
    out = list(base)
    for pos, idx in enumerate(CYCLE):
        out[idx] = base[CYCLE[(pos + step) % FRAMES]]
    return out


def self_check(base, frames):
    """机械自证（任一条不过 ⇒ 非零退出，绝不"看起来像"就放行）。"""
    errs = []

    # ① 色段确实是连续索引，且第一项就是平色水墙瓦片的那个索引
    if CYCLE != list(range(CYCLE_FIRST, CYCLE_LAST + 1)):
        errs.append('循环色段不是连续索引：%s' % CYCLE)
    if CYCLE[0] != 233:
        errs.append('循环色段起点 %d ≠ 233（平色水墙瓦片的索引）' % CYCLE[0])

    # ② 帧数 = 色段长度，且第 0 帧就是原调色板（不轮转）
    if len(frames) != FRAMES:
        errs.append('帧数 %d ≠ %d' % (len(frames), FRAMES))
    if frames[0] != base:
        errs.append('第 0 帧 ≠ 源调色板（轮转 0 步必须是恒等）')

    # ③ 逐帧逐值与源调色板对账：**只许是同一批颜色的重排**，颜色一个都不许新增/丢失
    base_sorted = sorted(base)
    for step, pal in enumerate(frames):
        if sorted(pal) != base_sorted:
            errs.append('第 %d 帧的颜色集合 ≠ 源调色板（出现自造颜色或丢色）' % step)
        changed = [i for i in range(256) if pal[i] != base[i]]
        want = [] if step == 0 else CYCLE        # 第 0 帧 = 不轮转 = 恒等
        if changed != want:
            errs.append('第 %d 帧被改的索引 %s ≠ %s' % (step, changed, want))
    # 走满一圈必须回到原色
    if rotated_palette(base, FRAMES) != base:
        errs.append('轮转 %d 步没有回到源调色板（色段长度与步进对不上）' % FRAMES)

    return errs


def export_pack(raw_root, out_root, rel_dt1, pack, base, frames):
    src = os.path.join(raw_root, rel_dt1.replace('/', os.sep))
    if not os.path.exists(src):
        return ['源文件不存在：%s' % src]

    dt1 = dt1mod.load_dt1(src)
    with open(src, 'rb') as fh:
        data = fh.read()

    errs = []
    written = 0
    animated_tiles = 0
    for tile in dt1.tiles:
        if tile.width <= 0 or tile.pixel_height <= 0:
            continue                                        # 与 `export_tiles.py` 同口径：0×0 占位瓦片不写

        # ① 平色水墙瓦片整张只有一个索引，且就是色段起点 —— 这是"233 起一段"的依据本身
        if (pack, tile.array_index) == FLAT_WATER_TILE:
            img0 = dt1mod.render_tile(tile, data=data)
            used = sorted(set(img0.indices[i] for i in range(img0.w * img0.h) if img0.mask[i]))
            if used != [CYCLE[0]]:
                errs.append('%s/%03d 的索引集合 %s ≠ [%d]（"色段起点 = 平色水墙索引"这条依据不成立）'
                            % (pack, tile.array_index, used, CYCLE[0]))

        images = []
        for step in range(FRAMES):
            img = dt1mod.render_tile(tile, data=data)
            images.append(img.to_rgba(frames[step]))

        # ④ 至少两帧不同 —— 否则这张瓦片根本不吃循环，出帧是白出（必须点名，不许静默）
        differs = any(images[step] != images[0] for step in range(1, FRAMES))
        if not differs:
            errs.append('瓦片 %s/%03d 在循环色段下逐像素不变 ⇒ 出帧无意义' % (pack, tile.array_index))
            continue
        animated_tiles += 1

        folder = 'Tiles' if tile.is_floor else 'Objects'
        for step in range(FRAMES):
            out = os.path.join(out_root, folder, pack, 'f%d' % step, '%03d.png' % tile.array_index)
            os.makedirs(os.path.dirname(out), exist_ok=True)
            pngio.write_rgba(out, tile.width, tile.pixel_height, images[step])
            if os.path.getsize(out) <= 0:
                errs.append('产物 0 字节：%s' % out)
            written += 1

        # ⑤ 第 0 帧 = 不轮转 ⇒ 必须与 `export_tiles.py` 出的静态图**逐字节相同**
        static_png = os.path.join(out_root, folder, pack, '%03d.png' % tile.array_index)
        frame0_png = os.path.join(out_root, folder, pack, 'f0', '%03d.png' % tile.array_index)
        if os.path.exists(static_png):
            with open(static_png, 'rb') as a, open(frame0_png, 'rb') as b:
                if a.read() != b.read():
                    errs.append('%s/%03d 第 0 帧与既有静态 PNG 不同字节（生成口径跑偏）'
                                % (pack, tile.array_index))

    print('  %-14s %-16s 出帧瓦片 %3d / 共写 PNG %4d（%d 帧 × %d 张）'
          % (pack, os.path.basename(rel_dt1), animated_tiles, written, FRAMES, animated_tiles))
    return errs


def main(argv):
    raw_root = DEFAULT_RAW
    out_root = DEFAULT_OUT
    if '--raw' in argv:
        raw_root = argv[argv.index('--raw') + 1]
    if '--out' in argv:
        out_root = argv[argv.index('--out') + 1]

    base = pl2mod.load_pl2(os.path.join(raw_root, PALETTE_REL.replace('/', os.sep)))
    frames = [rotated_palette(base, step) for step in range(FRAMES)]

    print('循环色段 [%d..%d] = %s' % (CYCLE_FIRST, CYCLE_LAST, CYCLE))
    for i in CYCLE:
        print('   %3d %s' % (i, base[i][:3]))
    print('帧数 %d / 输出根 %s' % (FRAMES, out_root))

    errs = self_check(base, frames)
    for rel_dt1, pack_name in PACKS:
        errs += export_pack(raw_root, out_root, rel_dt1, pack_name, base, frames)

    if errs:
        print('自证不过（%d 条）：' % len(errs))
        for e in errs:
            print('  [NG] %s' % e)
        return 1
    print('自证通过：色段 %s · %d 帧 · 逐帧逐值与源调色板对账一致（无自造颜色）· 第 0 帧 = 既有静态图'
          % (CYCLE, FRAMES))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
