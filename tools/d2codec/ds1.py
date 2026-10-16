# -*- coding: utf-8 -*-
"""Diablo II `.DS1`（地图布局）解码器（纯 Python，无第三方依赖）。

格式依据（**逐条对齐参考实现，不猜**）：
  `_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/IO/D2Formats/DS1.cs`
    · version i32 / width i32+1 / height i32+1                      (DS1.cs:93-95)
    · version ≥ 8 ⇒ act i32（>4 截到 4，用于选 PL2）                  (DS1.cs:98-102)
    · version ≥ 10 ⇒ tagType i32                                     (DS1.cs:105-108)
    · version ≥ 3 ⇒ 依赖 dt1 名表：fileCount i32 + N 个 `\\0` 结尾串    (DS1.cs:110-120, 354-375)
    · version 9..13 ⇒ 跳过 8 字节                                     (DS1.cs:122-123)
    · 层：version ≥ 4 ⇒ wallLayerCount；version ≥ 16 ⇒ 再读 floorLayerCount
          version < 4 ⇒ 固定 1 wall + 1 floor + 1 tag，且层序不同        (DS1.cs:192-251)
      每层 cell = 4 字节 (prop1..prop4)；墙层后紧跟 orientation 层（4 字节/格，只用第 1 字节）
    · cell 语义：
          墙：prop1==0 ⇒ 空；否则 orientation（version<7 过 dirLookup）、
              mainIndex = (prop3 >> 4) + ((prop4 & 3) << 4)、subIndex = prop2
          地：同上但 orientation 恒 0                                  (DS1.cs:269-310)
    · objects：count i32 + 每个 {type i32, id i32, x i32, y i32, (version>5) flags i32}  (DS1.cs:166-190)
    · groups：version ≥ 12 且 tagType ∈ {1,2} 才有；version ≥ 18 先读 1 个 i32；
              每组 {x,y,w,h i32, (version ≥ 13) 1 个 i32}                (DS1.cs:140-164)

**像素坐标约定**（本项目推导，用于 `compose`）：
    格 (x, y) 的**屏幕中心** = ((x - y) * 80, (x + y) * 40)（地砖 160×80 ⇒ 半宽 80、半高 40）。
    一个瓦片图像（w×h）以**中心**落在这个点上；墙/屋顶类瓦片（h > 80）的落脚菱形在图像**底部 80 px**，
    所以它的中心要再抬高 (h - 80) / 2 像素。

用法：
    python ds1.py <file.ds1> [--dump] [--compose <out.png>] [--raw <d2raw 根>]
"""

import os
import struct
import sys

try:
    from . import dt1 as dt1mod
    from . import pl2 as pl2mod
    from . import pngio
except ImportError:
    import dt1 as dt1mod
    import pl2 as pl2mod
    import pngio

# DS1.cs:49-53 的 dirLookup（version < 7 的墙 orientation 映射），逐字照抄。
DIR_LOOKUP = (0x00, 0x01, 0x02, 0x01, 0x02, 0x03, 0x03, 0x05, 0x05, 0x06,
              0x06, 0x07, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E,
              0x0F, 0x10, 0x11, 0x12, 0x14)

FLOOR_TILE_W = 160
FLOOR_TILE_H = 80

DEFAULT_RAW = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    '原版资源', 'd2raw')


class DS1Cell(object):
    __slots__ = ('prop1', 'prop2', 'prop3', 'prop4', 'orientation',
                 'main_index', 'sub_index', 'tile_index')

    def __init__(self):
        self.prop1 = self.prop2 = self.prop3 = self.prop4 = 0
        self.orientation = 0
        self.main_index = 0
        self.sub_index = 0
        self.tile_index = 0

    @property
    def is_empty(self):
        return self.prop1 == 0

    def __repr__(self):
        return 'Cell(m=%d s=%d o=%d idx=%d)' % (self.main_index, self.sub_index,
                                               self.orientation, self.tile_index)


class DS1Object(object):
    __slots__ = ('obj_type', 'obj_id', 'x', 'y', 'flags')

    def __init__(self, obj_type, obj_id, x, y, flags):
        self.obj_type = obj_type
        self.obj_id = obj_id
        self.x = x
        self.y = y
        self.flags = flags

    def __repr__(self):
        return 'Obj(type=%d id=%d @(%d,%d) flags=%d)' % (self.obj_type, self.obj_id,
                                                        self.x, self.y, self.flags)


class DS1(object):
    def __init__(self, path):
        self.path = path
        self.version = 0
        self.act = 0
        self.tag_type = 0
        self.width = 0
        self.height = 0
        self.dt1_files = []
        self.walls = []        # list[list[DS1Cell]]（每层 width*height）
        self.floors = []
        self.shadows = []
        self.objects = []
        self.groups = []
        self.tail_error = None      # 非 None = 文件尾部的 objects/groups 表解析失败（不影响 floor/wall）

    # ── 查询 ────────────────────────────────────────────────────────────────
    def floor_at(self, x, y):
        if not self.floors:
            return None
        return self.floors[0][y * self.width + x]

    def wall_at(self, layer, x, y):
        if layer >= len(self.walls):
            return None
        return self.walls[layer][y * self.width + x]

    def stats(self):
        nf = sum(1 for c in (self.floors[0] if self.floors else []) if not c.is_empty)
        nw = sum(1 for layer in self.walls for c in layer if not c.is_empty)
        return nf, nw, len(self.objects), len(self.groups)


def _read_c_string(data, ptr):
    end = data.index(0, ptr)
    raw = data[ptr:end]
    return raw.decode('latin-1'), end + 1


def load_ds1(path):
    with open(path, 'rb') as fh:
        data = fh.read()

    ds1 = DS1(path)
    ptr = 0
    ds1.version = struct.unpack_from('<i', data, ptr)[0]
    ptr += 4
    ds1.width = struct.unpack_from('<i', data, ptr)[0] + 1
    ptr += 4
    ds1.height = struct.unpack_from('<i', data, ptr)[0] + 1
    ptr += 4

    if ds1.version >= 8:
        act = struct.unpack_from('<i', data, ptr)[0]
        ptr += 4
        ds1.act = min(act, 4)

    if ds1.version >= 10:
        ds1.tag_type = struct.unpack_from('<i', data, ptr)[0]
        ptr += 4

    if ds1.version >= 3:
        file_count = struct.unpack_from('<i', data, ptr)[0]
        ptr += 4
        for _ in range(file_count):
            name, ptr = _read_c_string(data, ptr)
            name = name.lower().replace('.tg1', '.dt1').replace('c:\\d2\\', '').replace('\\d2\\', '')
            ds1.dt1_files.append(name)

    if 9 <= ds1.version <= 13:
        ptr += 8

    ptr = _read_layers(ds1, data, ptr)

    # 对象 / 分组表在**文件尾部**。实测少数原版 ds1（如 OUTDOORS/Trees.ds1）尾部被截断 ⇒
    # `struct.unpack_from` 直接抛 struct.error。这两张表本项目**不使用**（地图只用
    # floor / wall 两层），所以这里**容错并留痕**，不让它把整块预设废掉。
    try:
        ptr = _read_objects(ds1, data, ptr)
        _read_groups(ds1, data, ptr)
    except struct.error as exc:
        ds1.tail_error = str(exc)
        print('  [ds1] WARN %s：对象/分组表解析失败（%s）⇒ 保留已读到的 floor/wall 层'
              % (os.path.basename(path), exc))
    return ds1


def _read_layers(ds1, data, ptr):
    wall_layer_count = 1
    floor_layer_count = 1
    tag_layer_count = 0

    if ds1.version >= 4:
        wall_layer_count = struct.unpack_from('<i', data, ptr)[0]
        ptr += 4
        if ds1.version >= 16:
            floor_layer_count = struct.unpack_from('<i', data, ptr)[0]
            ptr += 4
    else:
        tag_layer_count = 1

    if ds1.tag_type in (1, 2):
        tag_layer_count = 1

    n = ds1.width * ds1.height
    ds1.floors = [[DS1Cell() for _ in range(n)] for _ in range(floor_layer_count)]
    ds1.walls = [[DS1Cell() for _ in range(n)] for _ in range(wall_layer_count)]
    ds1.shadows = [DS1Cell() for _ in range(n)]

    if ds1.version < 4:
        ptr = _read_cells(ds1.walls[0], data, ptr)
        ptr = _read_cells(ds1.floors[0], data, ptr)
        ptr = _read_orientations(ds1.walls[0], data, ptr)
        ptr += 4 * n                                    # tag 层（丢掉）
        ptr = _read_cells(ds1.shadows, data, ptr)
    else:
        for i in range(wall_layer_count):
            ptr = _read_cells(ds1.walls[i], data, ptr)
            ptr = _read_orientations(ds1.walls[i], data, ptr)
        for i in range(floor_layer_count):
            ptr = _read_cells(ds1.floors[i], data, ptr)
        ptr = _read_cells(ds1.shadows, data, ptr)
        if tag_layer_count:
            ptr += 4 * n

    # 影子层：orientation 恒 13（DS1.cs:253-267）
    for cell in ds1.shadows:
        if cell.prop1 == 0:
            continue
        cell.orientation = 13
        cell.main_index = (cell.prop3 >> 4) + ((cell.prop4 & 0x03) << 4)
        cell.sub_index = cell.prop2
        cell.tile_index = dt1mod.tile_index(cell.main_index, cell.sub_index, cell.orientation)

    for layer in ds1.walls:
        for cell in layer:
            if cell.prop1 == 0:
                continue
            if ds1.version < 7:
                cell.orientation = DIR_LOOKUP[cell.orientation]
            cell.main_index = (cell.prop3 >> 4) + ((cell.prop4 & 0x03) << 4)
            cell.sub_index = cell.prop2
            cell.tile_index = dt1mod.tile_index(cell.main_index, cell.sub_index, cell.orientation)

    for layer in ds1.floors:
        for cell in layer:
            if cell.prop1 == 0:
                continue
            cell.main_index = (cell.prop3 >> 4) + ((cell.prop4 & 0x03) << 4)
            cell.sub_index = cell.prop2
            cell.orientation = 0
            cell.tile_index = dt1mod.tile_index(cell.main_index, cell.sub_index, 0)

    return ptr


def _read_cells(cells, data, ptr):
    for i in range(len(cells)):
        off = ptr + i * 4
        c = cells[i]
        c.prop1, c.prop2, c.prop3, c.prop4 = data[off], data[off + 1], data[off + 2], data[off + 3]
    return ptr + 4 * len(cells)


def _read_orientations(cells, data, ptr):
    for i in range(len(cells)):
        cells[i].orientation = data[ptr + i * 4]
    return ptr + 4 * len(cells)


def _read_objects(ds1, data, ptr):
    if ds1.version < 2:
        return ptr
    count = struct.unpack_from('<i', data, ptr)[0]
    ptr += 4
    for _ in range(count):
        obj_type, obj_id, x, y = struct.unpack_from('<iiii', data, ptr)
        ptr += 16
        flags = 0
        if ds1.version > 5:
            flags = struct.unpack_from('<i', data, ptr)[0]
            ptr += 4
        ds1.objects.append(DS1Object(obj_type, obj_id, x, y, flags))
    return ptr


def _read_groups(ds1, data, ptr):
    if ds1.version < 12 or ds1.tag_type not in (1, 2):
        return ptr
    if ds1.version >= 18:
        ptr += 4
    count = struct.unpack_from('<i', data, ptr)[0]
    ptr += 4
    for _ in range(count):
        x, y, w, h = struct.unpack_from('<iiii', data, ptr)
        ptr += 16
        if ds1.version >= 13:
            ptr += 4
        ds1.groups.append((x, y, w, h))
    return ptr


# ══════════════════════════════════════════════════════════════════════════════
#  把 DS1 合成一张 PNG（**开发/验收用**：用来肉眼确认"解出来的是原版地图"）
# ══════════════════════════════════════════════════════════════════════════════

def compose(ds1, raw_root, palette, layer_groups=None, margin=0):
    """合成整张 DS1 → (width, height, rgba)。

    `layer_groups`：要画的层，默认 [('floor', 0), ('wall', 0), ('wall', 1)]。
    """
    if layer_groups is None:
        layer_groups = [('floor', 0), ('wall', 0), ('wall', 1)]

    samplers = _load_samplers(ds1, raw_root)

    # 画布尺寸：等距包围盒
    tile_w, tile_h = FLOOR_TILE_W, FLOOR_TILE_H
    canvas_w = (ds1.width + ds1.height) * (tile_w // 2) + margin * 2
    canvas_h = (ds1.width + ds1.height) * (tile_h // 2) + 512 + margin * 2

    buf = bytearray(canvas_w * canvas_h * 4)
    drawn = 0
    for kind, layer in layer_groups:
        cells = ds1.floors[layer] if kind == 'floor' else (
            ds1.walls[layer] if layer < len(ds1.walls) else [])
        if not cells:
            # 非预期但实际存在：默认层组含 ('wall', 1)，而不少预设（CAVES/*、OUTDOORS/*）
            # 只有 1 个墙层 ⇒ 直接跳过（否则 `cells[y*width+x]` 抛 IndexError）。
            print('  [ds1] 跳过不存在的层：%s[%d]（本 DS1 只有 %d 个%s层）'
                  % (kind, layer, len(ds1.walls) if kind == 'wall' else len(ds1.floors), kind))
            continue
        for y in range(ds1.height):
            for x in range(ds1.width):
                cell = cells[y * ds1.width + x]
                if cell.is_empty:
                    continue
                tile = samplers.get(cell.tile_index)
                if tile is None:
                    continue
                cx = margin + (x - y + ds1.height) * (tile_w // 2)
                cy = margin + (x + y) * (tile_h // 2)
                cy += (tile.pixel_height - tile_h) // 2      # 墙/屋顶：落脚菱形在图片底部 ⇒ 中心上移
                _blit_indexed(buf, canvas_w, canvas_h, cx, cy, tile.image, palette)
                drawn += 1

    return canvas_w, canvas_h, bytes(buf), drawn


def _load_samplers(ds1, raw_root):
    """把 DS1 依赖的 dt1 全部读进来，建 `compositeIndex → tile`。

    同一个 compositeIndex 会有多个 **rarity 变体**，其中还夹着 `0×0` 的占位瓦片
    （实测 `TOWN/trees.dt1` 有 7 个）—— 若只取"第一个"，会抽到空瓦片而**什么都不画**
    （这正是第一版合成图看起来空荡荡的原因）。这里按"有内容优先"选：
    `不透明像素多` > `面积大` > 先出现者。
    """
    out = {}
    for rel in ds1.dt1_files:
        rel = rel.replace('\\', '/')
        path = os.path.join(raw_root, rel.replace('/', os.sep))
        if not os.path.exists(path):
            print('  [ds1] WARN 依赖 dt1 不存在：%s' % path)
            continue
        try:
            d = dt1mod.load_dt1(path)
        except ValueError as exc:
            print('  [ds1] WARN 跳过（%s）' % exc)
            continue
        with open(path, 'rb') as fh:
            data = fh.read()
        for tile in d.tiles:
            if tile.width <= 0 or tile.pixel_height <= 0:
                continue                                  # 0×0 占位瓦片：永远不画
            img = dt1mod.render_tile(tile, data=data)
            if img.opaque_bbox is None:
                continue                                  # 全透明：画了也白画
            tile.image = img
            key = tile.composite_index
            prev = out.get(key)
            if prev is None or _Fill(img) > _Fill(prev.image):
                out[key] = tile
    return out


def _Fill(img):
    return sum(img.mask)


def _blit_indexed(buf, cw, ch, cx, cy, img, palette):
    """把索引色图（中心在 cx,cy）混进 RGBA 画布（透明像素跳过）。"""
    x0 = cx - img.w // 2
    y0 = cy - img.h // 2
    for y in range(img.h):
        ty = y0 + y
        if ty < 0 or ty >= ch:
            continue
        row = y * img.w
        for x in range(img.w):
            if not img.mask[row + x]:
                continue
            tx = x0 + x
            if tx < 0 or tx >= cw:
                continue
            r, g, b, a = palette[img.indices[row + x]]
            o = (ty * cw + tx) * 4
            buf[o] = r
            buf[o + 1] = g
            buf[o + 2] = b
            buf[o + 3] = a


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    path = argv[1]
    ds1 = load_ds1(path)
    nf, nw, nobj, ngrp = ds1.stats()
    print('%s' % os.path.basename(path))
    print('  version=%d act=%d tagType=%d size=%dx%d' % (ds1.version, ds1.act, ds1.tag_type,
                                                         ds1.width, ds1.height))
    print('  层：floor=%d wall=%d shadow=1' % (len(ds1.floors), len(ds1.walls)))
    print('  floor cell=%d  wall cell=%d  objects=%d  groups=%d' % (nf, nw, nobj, ngrp))
    print('  依赖 dt1 (%d): %s' % (len(ds1.dt1_files), ', '.join(ds1.dt1_files)))

    if '--dump' in argv:
        print('  --floor--')
        _dump_layer(ds1.floors[0], ds1.width, ds1.height)
        for i, layer in enumerate(ds1.walls):
            print('  --wall layer %d--' % i)
            _dump_layer(layer, ds1.width, ds1.height)
        print('  --objects--')
        for o in ds1.objects:
            print('   ', o)
        print('  --groups--')
        for g in ds1.groups:
            print('    x=%d y=%d w=%d h=%d' % g)

    if '--compose' in argv:
        out = argv[argv.index('--compose') + 1]
        raw_root = argv[argv.index('--raw') + 1] if '--raw' in argv else DEFAULT_RAW
        palette = pl2mod.load_pl2(os.path.join(
            raw_root, 'data', 'global', 'palette', 'ACT1', 'Pal.PL2'))
        w, h, rgba, drawn = compose(ds1, raw_root, palette)
        pngio.write_rgba(out, w, h, rgba)
        print('  合成 → %s（%dx%d，落了 %d 个瓦片）' % (out, w, h, drawn))

    return 0


def _dump_layer(cells, w, h):
    """用字符画打印一层的"是否有瓦片"（`#` 有 / `.` 空）。"""
    for y in range(h):
        line = []
        for x in range(w):
            line.append('#' if not cells[y * w + x].is_empty else '.')
        print('    ' + ''.join(line))


if __name__ == '__main__':
    sys.exit(main(sys.argv))
