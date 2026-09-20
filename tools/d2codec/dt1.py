# -*- coding: utf-8 -*-
"""Diablo II `.DT1`（地形瓦片）解码器（纯 Python，无第三方依赖）。

格式依据（**逐条对齐参考实现，不猜**）：
  `_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/IO/D2Formats/DT1.cs`
    · 文件头：`version1(=7) int32` + `version2(=6) int32`，再前移 260 字节到 offset 268，
      然后 `tileCount int32` + `tileHeadersOffset int32(=276)`          （DT1.cs:278-287）
    · 每个 tile 头 **96 字节**：
        direction i32 / roofHeight i16 / soundIndex u8 / animated u8 /
        height i32（**负值**，绝对值 = 图像高）/ width i32 / 4 字节 0 /
        orientation i32 / mainIndex i32 / subIndex i32 / rarity i32 / 4 字节 unknown /
        flags[25] / 7 字节 unused / blockHeaderPointer i32 / blockDatasLength i32 /
        blockCount i32 / 12 字节 0                                    （DT1.cs:123-145）
    · 每个 block 头 **20 字节**：
        x i16 / y i16 / 2 字节 0 / gridX u8 / gridY u8 / format i16 /
        length i32 / 2 字节 0 / fileOffset i32                        （DT1.cs:228-239）
      像素数据位置 = `blockHeaderPointer + fileOffset`，长度 = `length`。
    · 两种块编码：
        format == 1 → **等距子块**：固定 256 字节、无游程，按 `xjump`/`nbpix` 直接落盘
                      （DT1.cs:340-368，`xjump`/`nbpix` 逐字照抄）
        format != 1 → **普通 RLE**：`while length>0: (b1,b2)`，
                      b1/b2 全 0 ⇒ 换行；否则 x += b1，然后写 b2 个像素（DT1.cs:301-338）
    · 图像坐标：`dst = len - y0*size - size + x0` ⇒ **y0 从图像顶部向下计**，
      所以 block 的 (x,y) 直接就是"相对瓦片左上角"的像素偏移。
    · 瓦片索引：`Index(main, sub, orientation) = ((main << 6) + sub) << 5 + orientation`
      —— DS1 用这个值引用 DT1 里的瓦片（DT1.cs:147-150）。

尺寸约定（实测）：一个等距"地砖"= **160×80 px**，由 **5×5 个 32×32 等距子块**拼成
（子块本身是 32 宽 / 15 行、2:1 的菱形，`sum(nbpix)=256`）。墙/物件瓦片更高
（`abs(height) > 80`），图像**底部 80 px 是它的落脚菱形**。

用法：
    python dt1.py <file.dt1> [--dump] [--png <outdir>]
"""

import os
import struct
import sys

try:
    from . import pl2 as pl2mod
except ImportError:                                    # 直接 `python dt1.py` 时无包上下文
    import pl2 as pl2mod

# ── 常量（出处 DT1.cs）───────────────────────────────────────────────────────
DT1_HEADER_SIZE = 276           # 两个版本号之后前移 260 字节 ⇒ tileCount 在 268、头部偏移在 272
DT1_TILE_HEADER_SIZE = 96
DT1_BLOCK_HEADER_SIZE = 20
BLOCK_FORMAT_ISOMETRIC = 1

# 等距子块的落盘表（DT1.cs:340-341 逐字照抄）。
XJUMP = (14, 12, 10, 8, 6, 4, 2, 0, 2, 4, 6, 8, 10, 12, 14)
NBPIX = (4, 8, 12, 16, 20, 24, 28, 32, 28, 24, 20, 16, 12, 8, 4)

ISOMETRIC_BLOCK_PIXELS = 256    # 3d 等距子块固定 256 像素（DT1.cs:348）

# 地砖（orientation 0）的图像尺寸 —— 本项目据此把像素换算成世界单位（见 MapView）。
FLOOR_TILE_PX_W = 160
FLOOR_TILE_PX_H = 80


class DT1Block(object):
    """tile 内的一个像素块。"""

    __slots__ = ('x', 'y', 'grid_x', 'grid_y', 'format', 'length', 'file_offset', 'data_offset')

    def __init__(self, x, y, grid_x, grid_y, fmt, length, file_offset, data_offset):
        self.x = x
        self.y = y
        self.grid_x = grid_x
        self.grid_y = grid_y
        self.format = fmt
        self.length = length
        self.file_offset = file_offset
        self.data_offset = data_offset


class DT1Tile(object):
    """DT1 里的一个瓦片（一个 tile 头 + 若干 block）。"""

    __slots__ = ('direction', 'roof_height', 'sound_index', 'animated', 'height', 'width',
                 'orientation', 'main_index', 'sub_index', 'rarity', 'flags',
                 'block_header_pointer', 'block_datas_length', 'block_count',
                 'blocks', 'array_index', 'image')

    def __init__(self):
        self.blocks = []
        self.array_index = -1
        self.image = None       # 解出的索引色位图（`render_tile` 填；DS1 合成器用）

    # ── 派生量 ──────────────────────────────────────────────────────────────
    @property
    def pixel_width(self):
        return self.width

    @property
    def pixel_height(self):
        """图像高 = `|height|`（DT1 里 height 是负值，表示"向上生长"）。"""
        return abs(self.height)

    @property
    def is_floor(self):
        """orientation == 0 ⇒ 实心地面砖（参照 DT1.cs:213 的 'floor or roof' 判定，0 = floor）。"""
        return self.orientation == 0

    @property
    def is_roof(self):
        """orientation == 15 ⇒ 屋顶（DT1.cs:213 同一行）。"""
        return self.orientation == 15

    @property
    def composite_index(self):
        """DS1 引用本瓦片用的键（DT1.cs:147-150）。"""
        return tile_index(self.main_index, self.sub_index, self.orientation)

    @property
    def is_walkable_flag(self):
        """block 0 的 `Walk` 标志（DT1.cs:20 `Walk = 1`）—— 原版的可走性来源。"""
        return bool(self.flags[0] & 1) if self.flags else False

    def __repr__(self):
        return ('DT1Tile(array=%d m=%d s=%d o=%d %dx%d blocks=%d floor=%s walk=%s)'
                % (self.array_index, self.main_index, self.sub_index, self.orientation,
                   self.width, self.pixel_height, len(self.blocks), self.is_floor,
                   self.is_walkable_flag))


def tile_index(main_index, sub_index, orientation):
    """`((main << 6) + sub) << 5 + orientation`（DT1.cs:147-150 逐字照抄）。"""
    return (((main_index << 6) + sub_index) << 5) + orientation


class DT1(object):
    """一个 `.dt1` 文件。"""

    def __init__(self, path, version, tiles):
        self.path = path
        self.version = version
        self.tiles = tiles

    def by_composite_index(self):
        return dict((t.composite_index, t) for t in self.tiles)

    def floors(self):
        return [t for t in self.tiles if t.is_floor]

    def walls(self):
        return [t for t in self.tiles if not t.is_floor and not t.is_roof]


def load_dt1(path):
    """解析一个 `.dt1`。返回 <see cref="DT1"/>；版本不符抛 ValueError。"""
    with open(path, 'rb') as fh:
        data = fh.read()

    if len(data) < DT1_HEADER_SIZE:
        raise ValueError('%s: 只有 %d 字节，连 276 字节文件头都不够' % (path, len(data)))

    version1, version2 = struct.unpack_from('<ii', data, 0)
    if version1 != 7 or version2 != 6:
        raise ValueError('%s: 版本号 %d.%d ≠ 7.6（DT1.cs:280 只接受 7.6）'
                         % (path, version1, version2))

    tile_count, tile_headers_offset = struct.unpack_from('<ii', data, 268)
    if tile_headers_offset != DT1_HEADER_SIZE:
        # 非预期：头部偏移不是 276 ⇒ 按文件里给的值走，但必须留痕（不静默）
        print('[dt1] WARN %s: tileHeadersOffset=%d ≠ %d，按文件值继续'
              % (path, tile_headers_offset, DT1_HEADER_SIZE))

    expected = tile_headers_offset + tile_count * DT1_TILE_HEADER_SIZE
    if len(data) < expected:
        raise ValueError('%s: %d 个 tile 头需要 %d 字节，文件只有 %d'
                         % (path, tile_count, expected, len(data)))

    tiles = []
    for i in range(tile_count):
        off = tile_headers_offset + i * DT1_TILE_HEADER_SIZE
        tile = _read_tile_header(data, off)
        tile.array_index = i
        _read_blocks(data, tile)
        tiles.append(tile)

    return DT1(path, (version1, version2), tiles)


def _read_tile_header(data, off):
    t = DT1Tile()
    (t.direction, t.roof_height, t.sound_index, t.animated,
     t.height, t.width) = struct.unpack_from('<ihBBii', data, off)
    # off+16 .. off+19 = 4 字节 0
    (t.orientation, t.main_index, t.sub_index, t.rarity) = struct.unpack_from('<iiii', data, off + 20)
    # off+36 .. off+39 = 4 字节 unknown
    t.flags = tuple(data[off + 40: off + 40 + 25])
    # off+65 .. off+71 = 7 字节 unused
    (t.block_header_pointer, t.block_datas_length, t.block_count) = struct.unpack_from('<iii', data, off + 72)
    # off+84 .. off+95 = 12 字节 0
    return t


def _read_blocks(data, tile):
    for b in range(tile.block_count):
        off = tile.block_header_pointer + b * DT1_BLOCK_HEADER_SIZE
        # 字段顺序与 DT1.cs:230-238 一致：x/y 之后是 2 字节 0、gridX、gridY、format
        x, y = struct.unpack_from('<hh', data, off)
        grid_x, grid_y, fmt = struct.unpack_from('<BBh', data, off + 6)
        length = struct.unpack_from('<i', data, off + 10)[0]
        # ⚠️ off+14..15 是 2 字节 0（DT1.cs:237 `reader.ReadBytes(2)`），`fileOffset` 在 off+16
        file_offset = struct.unpack_from('<i', data, off + 16)[0]
        data_offset = tile.block_header_pointer + file_offset
        tile.blocks.append(DT1Block(x, y, grid_x, grid_y, fmt, length, file_offset, data_offset))


class TileImage(object):
    """一个瓦片解码后的**索引色位图**。

    `indices[y*w + x]` = 调色板索引；`mask[y*w + x] != 0` = 该像素有内容。
    `y` 从**顶部**向下计（与 DT1 的 block 坐标一致）。
    """

    __slots__ = ('w', 'h', 'indices', 'mask', 'opaque_bbox')

    def __init__(self, w, h):
        self.w = w
        self.h = h
        self.indices = bytearray(w * h)
        self.mask = bytearray(w * h)
        self.opaque_bbox = None      # (x0, y0, x1, y1) 含端点；全空则 None

    def set_pixel(self, x, y, value):
        if x < 0 or y < 0 or x >= self.w or y >= self.h:
            return False          # 越界丢弃（DT1 里确有越界的 block，原版也是裁掉）
        i = y * self.w + x
        self.indices[i] = value
        self.mask[i] = 1
        return True

    def add_skip(self, x, y):
        """占位（不写像素），用于推进 RLE 的当前行位置。"""
        return (x, y)

    def finish(self):
        x0 = y0 = 1 << 30
        x1 = y1 = -1
        for y in range(self.h):
            row = y * self.w
            for x in range(self.w):
                if self.mask[row + x]:
                    if x < x0:
                        x0 = x
                    if x > x1:
                        x1 = x
                    if y < y0:
                        y0 = y
                    if y > y1:
                        y1 = y
        self.opaque_bbox = None if x1 < 0 else (x0, y0, x1, y1)
        return self

    def to_rgba(self, palette):
        """→ `bytes`，每个像素 4 字节 RGBA（未写的像素 a=0）。"""
        out = bytearray(self.w * self.h * 4)
        for i in range(self.w * self.h):
            if not self.mask[i]:
                continue                      # a = 0（透明），RGB 保持 0
            r, g, b, a = palette[self.indices[i]]
            o = i * 4
            out[o] = r
            out[o + 1] = g
            out[o + 2] = b
            out[o + 3] = a
        return bytes(out)


def render_tile(tile, data=None, path=None):
    """把一个 tile 解成索引色位图（<see cref="TileImage"/>）。

    `data` 为文件字节；给了 `path` 就自己读（便于单测）。
    """
    if data is None:
        if path is None:
            raise ValueError('render_tile: data / path 至少给一个')
        with open(path, 'rb') as fh:
            data = fh.read()

    img = TileImage(tile.pixel_width, tile.pixel_height)
    for block in tile.blocks:
        payload = data[block.data_offset: block.data_offset + block.length]
        if len(payload) < block.length:
            # 非预期：block 数据被截断 ⇒ 解码会错位，留痕
            print('[dt1] WARN tile(array=%d) block 数据被截断：要 %d 只有 %d'
                  % (tile.array_index, block.length, len(payload)))
        row = block_row(tile, block)
        if block.format == BLOCK_FORMAT_ISOMETRIC:
            _draw_isometric(img, block.x, row, payload)
        else:
            _draw_normal(img, block.x, row, payload)
    return img.finish()


def block_row(tile, block):
    """block 的 y → **图像行号（自顶向下）**。

    实测两种取值：
      · **地砖**（orientation == 0）：`y ≥ 0`，就是自顶向下的行号 —— 菱形内容落在图像
        **顶部 80 行**（实测 TOWN/floor.dt1 的 000.png：不透明像素集中在 row 0..79）。
        佐证：参考实现把地砖画在 `topLeft = (-1, +0.5)`（WorldRenderer.cs:166）⇒ 图像**顶边**
        贴在格子中心上方 0.5 ⇒ 菱形（图像顶部 80 行）正好铺满一格。
      · **墙/物件**（orientation ≠ 0）：`y < 0`，是"自图像**底边**向上量"的偏移 ⇒
        `row = tile.pixel_height + y`。佐证同处：墙类画在 `topLeft = (-1, h - 0.5)`
        （WorldRenderer.cs:178）⇒ 图像**底边**贴在格子中心下方 0.5（= 格子菱形的前角）⇒
        图像底部 80 px 就是它的落脚菱形。
    """
    if block.y >= 0:
        return block.y
    return tile.pixel_height + block.y


def _draw_normal(img, x0, y0, payload):
    """普通 RLE（DT1.cs:301-338 逐行对照）。"""
    x = 0
    y = 0
    ptr = 0
    length = len(payload)
    while length > 0:
        if len(payload) < ptr + 2:
            break
        b1 = payload[ptr]
        b2 = payload[ptr + 1]
        ptr += 2
        length -= 2
        if b1 != 0 or b2 != 0:
            x += b1
            length -= b2
            while b2:
                if ptr >= len(payload):
                    break
                img.set_pixel(x0 + x, y0 + y, payload[ptr])
                ptr += 1
                x += 1
                b2 -= 1
        else:
            x = 0
            y += 1


def _draw_isometric(img, x0, y0, payload):
    """等距子块：固定 256 字节，按 XJUMP/NBPIX 落盘（DT1.cs:340-368）。"""
    if len(payload) != ISOMETRIC_BLOCK_PIXELS:
        # 非预期：长度不是 256 ⇒ 参考实现直接 return（DT1.cs:350-351）。同样只跳过并留痕。
        print('[dt1] WARN 等距块长度 %d ≠ 256，按参考实现丢弃该块' % len(payload))
        return

    ptr = 0
    for y in range(len(XJUMP)):
        x = XJUMP[y]
        n = NBPIX[y]
        while n:
            img.set_pixel(x0 + x, y0 + y, payload[ptr])
            ptr += 1
            x += 1
            n -= 1


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    path = argv[1]
    dt1 = load_dt1(path)
    tiles = dt1.tiles
    floors = dt1.floors()
    print('%s  version=%s tiles=%d (floor=%d, wall/obj=%d)'
          % (os.path.basename(path), dt1.version, len(tiles), len(floors), len(tiles) - len(floors)))

    sizes = {}
    for t in tiles:
        key = (t.width, t.pixel_height)
        sizes[key] = sizes.get(key, 0) + 1
    print('尺寸分布（宽×高: 个数）: %s' % (
        ', '.join('%dx%d:%d' % (k[0], k[1], v) for k, v in sorted(sizes.items(), key=lambda kv: -kv[1])[:12]),))

    if '--dump' in argv:
        for t in tiles[:40]:
            print('  ', t)
        if len(tiles) > 40:
            print('   ... 其余 %d 个' % (len(tiles) - 40))

    if '--png' in argv:
        outdir = argv[argv.index('--png') + 1]
        palette = pl2mod.load_pl2(os.path.join(os.path.dirname(path), 'Pal.PL2')) \
            if os.path.exists(os.path.join(os.path.dirname(path), 'Pal.PL2')) else None
        if palette is None:
            print('（同目录没有 Pal.PL2，跳过 PNG 导出示例）')
            return 0
        with open(path, 'rb') as fh:
            data = fh.read()
        os.makedirs(outdir, exist_ok=True)
        for t in tiles:
            img = render_tile(t, data=data)
            print('   tile %d → %dx%d bbox=%s' % (t.array_index, img.w, img.h, img.opaque_bbox))

    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
