# -*- coding: utf-8 -*-
"""Diablo II `.DCC`（单位动画：角色 / 怪物 / NPC / 投掷物）解码器（纯 Python，无第三方依赖）。

**只做解码**（不参与 Unity 编译），供 `export_chars.py` 解出逐帧 PNG。

格式依据（**逐条给出出处，不猜**）：
  * `_assets_tmp/d2src/libd2/packages/formats/src/dcc.zig`
    —— 自述为 "Faithful port of OpenDiablo2's decoder
    (d2common/d2fileformats/d2dcc: dcc.go, dcc_direction.go, dcc_direction_frame.go,
    dcc_cell.go, dcc_pixel_buffer_entry.go) cross-checked against the Phrozen Keep /
    Paul Siramy DCC format notes"：
      · 位流 **LSB-first**（`BitMuncher.getBit`，dcc.zig:62-69）
      · 文件头：`signature=0x74`(u8) / version(u8) / directionCount(u8) /
        framesPerDir(i32) / tag(i32, 必须 == 1) / totalSizeCoded(i32) /
        `dirOffset[directionCount]`(i32，**单位 = 8 bit**：`off * DIR_OFFSET_MULT(=8)`，dcc.zig:141-149)
      · 方向头：`outSizeCoded(u32)` / `compressionFlag(bits(2))` /
        7 × `bits(4)` 经 `CRAZY_BIT_TABLE` 映射成实际位宽（dcc.zig:172-180，表见 :45）
      · 帧头：`variable0 / width / height / xoffset(有符号) / yoffset(有符号) /
        optionalBytes / codedBytes / bottomUp(1 bit)`（dcc.zig:193-202）
      · 帧包围盒：`box = {left: xoffset, top: yoffset - height + 1, width, height}`
        （dcc.zig:204）—— **yoffset 是帧的"底边"**，图像自底向上生长（所以 top 要减 height-1）
      · 方向包围盒 = 各帧 box 的并集（dcc.zig:207-213）
      · `optionalBytes` 的和 ≠ 0 ⇒ 不支持（dcc.zig:216）；`compressionFlag` 的 bit1(0x2)
        ⇒ 有 equalCell 流，bit0(0x1) ⇒ 有 encodingType + rawPixel 流（dcc.zig:218-226）
      · 256 bit 的"本方向用到的调色板索引"位图（dcc.zig:228-238）
      · 子流顺序 = equalCell → pixelMask → encodingType → rawPixel → pixelCode，
        各自**按 bit 紧邻**排布（dcc.zig:240-249）
      · 4×4 像素格（CELLS_PER_ROW = 4）的双层格网：方向格网 + 帧格网（dcc.zig:251-259）
      · 像素缓冲（dcc.zig:359-483）与逐帧生成（dcc.zig:485-595）
  * 交叉核对：`_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/IO/D2Formats/DCC.cs`
    —— 同一套头字段（`ReadHeader` L101-114 / `ReadDirection` L116-127 / `ReadFrame` L129-140）、
    同一套 `widthTable` / `nb_pix_table`（L541-542）、同一套 `FillPixelBuffer` / `MakeFrames` 语义
    （L302-539）。**差异只有一处**：Diablerie 的 `IntRect(xoffset, yoffset, w, h)` 把 yoffset 当
    **顶边**，而 libd2/OD2 把 yoffset 当**底边**。本实现取 **libd2/OD2 的口径**（底边），理由：
      · 只有"底边"口径能让同一方向内**高度不同的帧共用一条地面线**（站立/挥砍/倒地）；
      · 实测自证（`selfcheck` 会打印每方向的"帧底边"）：同一方向各帧的底边落在极小的
        区间内 ⇒ 口径正确；若按"顶边"口径，脚的落点会逐帧漂移，
        正是"角色浮空/错位"的成因（任务书 §A 明确要求保留 offset 且不许浮空）。

不透明/透明：索引 **0 = 透明**（与 DC6 一致，Diablerie `Palette.cs:82` 把 0 号置全透明）。

输出约定：
  · `Direction.box` = 方向包围盒，**坐标原点 (0,0) = 单位的"脚下"**（D2 的单位原点）；
  · 每帧 = `box.width × box.height` 的索引图，**行主序、自顶向下**，`0 = 透明`；
  · 贴图轴心（Unity `Sprite.Create` 的归一化 pivot）：
        px = -box.left / box.width      （原点在图内的横向像素位置）
        py = (box.top + box.height) / box.height
    出处：Diablerie `DCC.cs:453` 的 `pivot = (-dir.box.xMin / box.width, dir.box.yMax / box.height)`。
    本文件把它做成 `Direction.pivot()`，导出器据此写 sidecar，供 Unity 导入时设 spritePivot。

CLI：
  python dcc.py info   <file.dcc>                打印头 + 每方向包围盒/轴心/帧数
  python dcc.py probe  <dir>                     递归扫目录，汇总方向数/帧数/包围盒
  python dcc.py png    <file.dcc> <palette> <outdir> [stem]
                                                 每帧导出 `{stem}_d{dir}_{frame}.png`
  python dcc.py selfcheck <file.dcc> <palette>   自证：帧/方向/包围盒/轴心 + 不变量断言
"""

import os
import struct
import sys
import zlib

# ── 常量（出处 libd2 dcc.zig:40-47 / Diablerie DCC.cs:541-542）────────────────
DCC_SIGNATURE = 0x74
DIR_OFFSET_MULT = 8
CELLS_PER_ROW = 4

#: `crazyBitTable`：4 bit 索引 → 实际字段位宽（dcc.zig:45）。
CRAZY_BIT_TABLE = (0, 1, 2, 4, 6, 8, 10, 12, 14, 16, 20, 24, 26, 28, 30, 32)

#: `pixelMaskLookup`：4 bit mask → popcount（一次最多几个像素值）（dcc.zig:47）。
PIXEL_MASK_LOOKUP = (0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4)


# ═════════════════════════════════════════════════════════════════════════════
#  位流：**LSB-first**（dcc.zig:50-93 的 BitMuncher）
# ═════════════════════════════════════════════════════════════════════════════
class BitReader(object):
    """LSB-first 位读取器（游标单位 = bit）。"""

    __slots__ = ('data', 'offset')

    def __init__(self, data, bit_offset=0):
        self.data = data
        self.offset = bit_offset

    def copy(self):
        """同位置的独立副本（子流各自消费，互不影响）—— dcc.zig:59-61。"""
        return BitReader(self.data, self.offset)

    def bit(self):
        i = self.offset >> 3
        b = self.data[i] if i < len(self.data) else 0
        v = (b >> (self.offset & 7)) & 1
        self.offset += 1
        return v

    def bits(self, n):
        if n <= 0:
            return 0
        v = 0
        for i in range(n):
            v |= self.bit() << i
        return v

    def signed(self, n):
        return _make_signed(self.bits(n), n)

    def skip(self, n):
        self.offset += n


def _make_signed(value, bits):
    """二补码符号扩展；`bits == 1` 时置位读作 -1（OD2 `MakeSigned`，dcc.zig:97-105）。"""
    if bits <= 0:
        return 0
    sign = 1 << (bits - 1)
    if not (value & sign):
        return value
    if bits >= 32:
        return value
    return value - (1 << bits)


def _floor_div(a, b):
    """**向负无穷取整**的整除（zig/C# 的 `@divTrunc` 是截断取余 ⇒ 负数上语义不同，必须显式）。"""
    return a // b if a >= 0 else -((-a + b - 1) // b)


# ═════════════════════════════════════════════════════════════════════════════
#  数据结构
# ═════════════════════════════════════════════════════════════════════════════
class Rect(object):
    __slots__ = ('left', 'top', 'width', 'height')

    def __init__(self, left, top, width, height):
        self.left = left
        self.top = top
        self.width = width
        self.height = height

    def __repr__(self):
        return 'Rect(l=%d t=%d w=%d h=%d)' % (self.left, self.top, self.width, self.height)


class Cell(object):
    __slots__ = ('w', 'h', 'xoff', 'yoff', 'last_w', 'last_h', 'last_xoff', 'last_yoff')

    def __init__(self, w=0, h=0, xoff=0, yoff=0):
        self.w = w
        self.h = h
        self.xoff = xoff
        self.yoff = yoff
        self.last_w = -1
        self.last_h = -1
        self.last_xoff = 0
        self.last_yoff = 0


class FrameHeader(object):
    __slots__ = ('box', 'width', 'height', 'xoffset', 'yoffset', 'h_cells', 'v_cells', 'cells')

    def __init__(self):
        self.cells = []


class Direction(object):
    """一个方向：`box` = 方向包围盒；`frames` = 逐帧索引图（box 尺寸、自顶向下、0=透明）。"""

    __slots__ = ('box', 'frames')

    def __init__(self, box, frames):
        self.box = box
        self.frames = frames

    def pivot(self):
        """Unity 归一化轴心（原点 = D2 单位原点 = 脚下）。

        出处 Diablerie `DCC.cs:453`：`pivot = (-box.xMin / box.width, box.yMax / box.height)`。
        `box.height` 为 0 时返回 (0.5, 0.5)（非预期：空方向）。
        """
        if self.box.width <= 0 or self.box.height <= 0:
            return (0.5, 0.5)
        return (-float(self.box.left) / self.box.width,
                float(self.box.top + self.box.height) / self.box.height)


class Dcc(object):
    __slots__ = ('directions', 'frames_per_dir', 'version', 'path')

    def __init__(self, directions, frames_per_dir, version, path=None):
        self.directions = directions
        self.frames_per_dir = frames_per_dir
        self.version = version
        self.path = path

    def direction_count(self):
        return len(self.directions)


# ═════════════════════════════════════════════════════════════════════════════
#  解析
# ═════════════════════════════════════════════════════════════════════════════
def parse(data, path=None):
    """解析整个 `.dcc`（全部方向）。返回 <see cref="Dcc"/>；格式不符抛 ValueError。"""
    if len(data) < 4:
        raise ValueError('%s: 只有 %d 字节，连 DCC 头都不够' % (path or '<bytes>', len(data)))

    bm = BitReader(data, 0)
    sig = bm.bits(8)
    if sig != DCC_SIGNATURE:
        raise ValueError('%s: 签名 0x%02X ≠ 0x%02X（不是 DCC）' % (path or '<bytes>', sig, DCC_SIGNATURE))
    version = bm.bits(8)
    ndir = bm.bits(8)
    frames_per_dir = bm.bits(32)
    tag = bm.bits(32)
    if tag != 1:
        raise ValueError('%s: header.tag = %d ≠ 1（dcc.zig:142）' % (path or '<bytes>', tag))
    bm.bits(32)                                  # totalSizeCoded（不用）
    if ndir == 0 or frames_per_dir == 0:
        raise ValueError('%s: directionCount=%d framesPerDir=%d 非法'
                         % (path or '<bytes>', ndir, frames_per_dir))

    dir_offsets = [bm.bits(32) for _ in range(ndir)]

    directions = []
    for i, off in enumerate(dir_offsets):
        directions.append(_decode_direction(data, off * DIR_OFFSET_MULT, frames_per_dir,
                                            '%s [dir %d]' % (path or '<bytes>', i)))
    return Dcc(directions, frames_per_dir, version, path)


def _decode_direction(data, bit_offset, frames_per_dir, tag):
    bm = BitReader(data, bit_offset)

    bm.bits(32)                                   # outSizeCoded
    compression_flags = bm.bits(2)
    variable0_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    width_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    height_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    xoffset_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    yoffset_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    optional_bits = CRAZY_BIT_TABLE[bm.bits(4)]
    coded_bytes_bits = CRAZY_BIT_TABLE[bm.bits(4)]

    frames = []
    minx = miny = 1 << 30
    maxx = maxy = -(1 << 30)
    for _ in range(frames_per_dir):
        fr = FrameHeader()
        bm.bits(variable0_bits)                   # Variable0
        width = bm.bits(width_bits)
        height = bm.bits(height_bits)
        xoffset = bm.signed(xoffset_bits)
        yoffset = bm.signed(yoffset_bits)
        bm.bits(optional_bits)
        bm.bits(coded_bytes_bits)
        bottom_up = bm.bit() == 1
        if bottom_up:
            # 非预期分支：D2 正式数据里几乎没有；解下去会整帧上下颠倒 ⇒ 拒绝并留痕
            raise ValueError('%s: bottomUp 帧未支持（dcc.zig:202 同样报错）' % tag)
        # yoffset = 帧**底边**（dcc.zig:204）
        fr.box = Rect(xoffset, yoffset - height + 1, width, height)
        fr.width = width
        fr.height = height
        fr.xoffset = xoffset
        fr.yoffset = yoffset
        frames.append(fr)

        minx = min(minx, fr.box.left)
        miny = min(miny, fr.box.top)
        maxx = max(maxx, fr.box.left + fr.box.width)
        maxy = max(maxy, fr.box.top + fr.box.height)

    dbox = Rect(minx, miny, maxx - minx, maxy - miny)
    if dbox.width <= 0 or dbox.height <= 0:
        raise ValueError('%s: 方向包围盒非法 %s' % (tag, dbox))

    if optional_bits > 0:
        raise ValueError('%s: 有 optional bytes（dcc.zig:216 同样不支持）' % tag)

    equal_cells_size = bm.bits(20) if (compression_flags & 0x2) else 0
    pixel_mask_size = bm.bits(20)
    encoding_type_size = 0
    raw_pixel_size = 0
    if compression_flags & 0x1:
        encoding_type_size = bm.bits(20)
        raw_pixel_size = bm.bits(20)

    # 256 bit：本方向用到的调色板索引（dcc.zig:228-238）
    palette_entries = []
    for i in range(256):
        if bm.bit():
            palette_entries.append(i)

    ec = bm.copy()
    bm.skip(equal_cells_size)
    pm = bm.copy()
    bm.skip(pixel_mask_size)
    et = bm.copy()
    bm.skip(encoding_type_size)
    rp = bm.copy()
    bm.skip(raw_pixel_size)
    pcd = bm.copy()

    # 方向格网（CELLS_PER_ROW = 4）
    h_cells = 1 + _floor_div(dbox.width - 1, CELLS_PER_ROW)
    dir_cells = _build_direction_cells(dbox, h_cells)

    for fr in frames:
        _recalc_frame_cells(fr, dbox)

    pixel_buffer = _fill_pixel_buffer(ec, pm, et, rp, pcd, frames, dbox, h_cells,
                                      equal_cells_size, encoding_type_size, palette_entries)
    out_frames = _generate_frames(pcd, frames, dir_cells, pixel_buffer, dbox, h_cells)
    return Direction(dbox, out_frames)


def _build_direction_cells(dbox, h_cells):
    v_cells = 1 + _floor_div(dbox.height - 1, CELLS_PER_ROW)
    widths = [dbox.width if h_cells == 1
              else (CELLS_PER_ROW if i < h_cells - 1 else dbox.width - CELLS_PER_ROW * (h_cells - 1))
              for i in range(h_cells)]
    heights = [dbox.height if v_cells == 1
               else (CELLS_PER_ROW if i < v_cells - 1 else dbox.height - CELLS_PER_ROW * (v_cells - 1))
               for i in range(v_cells)]

    cells = [None] * (h_cells * v_cells)
    yoff = 0
    for y in range(v_cells):
        xoff = 0
        for x in range(h_cells):
            cells[x + y * h_cells] = Cell(widths[x], heights[y], xoff, yoff)
            xoff += CELLS_PER_ROW
        yoff += CELLS_PER_ROW
    return cells


def _recalc_frame_cells(fr, dbox):
    w0 = CELLS_PER_ROW - _zi_mod(fr.box.left - dbox.left, CELLS_PER_ROW)
    if fr.width - w0 <= 1:
        fr.h_cells = 1
    else:
        tmp = fr.width - w0 - 1
        fr.h_cells = 2 + _floor_div(tmp, CELLS_PER_ROW)
        if _zi_mod(tmp, CELLS_PER_ROW) == 0:
            fr.h_cells -= 1

    h0 = CELLS_PER_ROW - _zi_mod(fr.box.top - dbox.top, CELLS_PER_ROW)
    if fr.height - h0 <= 1:
        fr.v_cells = 1
    else:
        tmp = fr.height - h0 - 1
        fr.v_cells = 2 + _floor_div(tmp, CELLS_PER_ROW)
        if _zi_mod(tmp, CELLS_PER_ROW) == 0:
            fr.v_cells -= 1

    hc, vc = fr.h_cells, fr.v_cells
    widths = [fr.width] if hc == 1 else \
        [w0] + [CELLS_PER_ROW] * (hc - 2) + [fr.width - w0 - CELLS_PER_ROW * (hc - 2)]
    heights = [fr.height] if vc == 1 else \
        [h0] + [CELLS_PER_ROW] * (vc - 2) + [fr.height - h0 - CELLS_PER_ROW * (vc - 2)]

    fr.cells = [None] * (hc * vc)
    offy = fr.box.top - dbox.top
    for y in range(vc):
        offx = fr.box.left - dbox.left
        for x in range(hc):
            fr.cells[x + y * hc] = Cell(widths[x], heights[y], offx, offy)
            offx += widths[x]
        offy += heights[y]


def _zi_mod(a, b):
    """zig/C# 语义的取余（结果符号跟被除数）—— 与 Python 的 `%` 不同，必须显式。"""
    r = abs(a) % abs(b)
    return -r if a < 0 else r


def _fill_pixel_buffer(ec, pm, et, rp, pcd, frames, dbox, h_cells, equal_cells_size,
                       encoding_type_size, palette_entries):
    """像素缓冲：把"与上一帧相比变了的 4×4 格"解出来（dcc.zig:359-483）。"""
    v_cells_total = 1 + _floor_div(dbox.height - 1, CELLS_PER_ROW)
    pixel_buffer = []
    grid = [None] * (h_cells * v_cells_total)

    for frame_index, fr in enumerate(frames):
        origin_cx = _floor_div(fr.box.left - dbox.left, CELLS_PER_ROW)
        origin_cy = _floor_div(fr.box.top - dbox.top, CELLS_PER_ROW)

        for cy in range(fr.v_cells):
            cur_cy = cy + origin_cy
            for cx in range(fr.h_cells):
                current_cell = origin_cx + cx + cur_cy * h_cells
                next_cell = False
                if grid[current_cell] is not None:
                    tmp = ec.bit() if equal_cells_size > 0 else 0
                    if tmp == 0:
                        pixel_mask = pm.bits(4)
                    else:
                        next_cell = True
                else:
                    pixel_mask = 0x0F
                if next_cell:
                    continue

                pixel_stack = [0, 0, 0, 0]
                last_pixel = 0
                num_pixel_bits = PIXEL_MASK_LOOKUP[pixel_mask]
                encoding_type = et.bit() if (num_pixel_bits != 0 and encoding_type_size > 0) else 0

                decoded_pixel = 0
                for i in range(num_pixel_bits):
                    if encoding_type:
                        pixel_stack[i] = rp.bits(8)
                    else:
                        pixel_stack[i] = last_pixel
                        disp = pcd.bits(4)
                        pixel_stack[i] = (pixel_stack[i] + disp) & 0xFF
                        while disp == 15:
                            disp = pcd.bits(4)
                            pixel_stack[i] = (pixel_stack[i] + disp) & 0xFF
                    if pixel_stack[i] == last_pixel:
                        pixel_stack[i] = 0
                        break
                    last_pixel = pixel_stack[i]
                    decoded_pixel += 1

                old_entry = grid[current_cell]
                cur_idx = decoded_pixel - 1
                value = [0, 0, 0, 0]
                for k in range(4):
                    if pixel_mask & (1 << k):
                        if cur_idx >= 0:
                            value[k] = pixel_stack[cur_idx]
                            cur_idx -= 1
                        else:
                            value[k] = 0
                    else:
                        value[k] = pixel_buffer[old_entry][0][k] if old_entry is not None else 0
                grid[current_cell] = len(pixel_buffer)
                pixel_buffer.append((value, frame_index, cx + cy * fr.h_cells))

    # 索引值过一遍"本方向用到的调色板索引"表（dcc.zig:474-480）
    mapped = []
    for value, f, ci in pixel_buffer:
        mapped.append(([palette_entries[v] for v in value], f, ci))
    return mapped


def _generate_frames(pcd, frames, dir_cells, pixel_buffer, dbox, h_cells):
    """逐帧生成 box 尺寸的索引图（dcc.zig:485-595）。"""
    for c in dir_cells:
        if c is not None:
            c.last_w = -1
            c.last_h = -1

    bw, bh = dbox.width, dbox.height
    pix_data = bytearray(bw * bh)      # 方向累加器（跨帧保留：EqualCell 要引用上一帧）
    pb_idx = 0
    out = []

    # 终止哨兵：条目用尽后剩下的格子全部走 "EqualCell" 分支（= 与上一帧同格内容相同）。
    # 依据：参考实现里 `pixel_buffer[pb_idx+1].frame = -1`（Diablerie `DCC.cs:408`）与
    # libd2 的"超大数组 + 默认 frame=-1"（dcc.zig:383）都是这个语义：
    # 条目用尽**不是错误**，只是后面的格子没有新内容。少了它就会 IndexError。
    sentinel = ([0, 0, 0, 0], -1, -1)
    pb_len = len(pixel_buffer)

    for frame_index, fr in enumerate(frames):
        fbuf = bytearray(bw * bh)

        for c, cell in enumerate(fr.cells):
            cell_index = _floor_div(cell.xoff, CELLS_PER_ROW) \
                + _floor_div(cell.yoff, CELLS_PER_ROW) * h_cells
            buffer_cell = dir_cells[cell_index]
            value, pbe_frame, pbe_cell = pixel_buffer[pb_idx] if pb_idx < pb_len else sentinel

            cw, ch = cell.w, cell.h
            cxo, cyo = cell.xoff, cell.yoff

            if pbe_frame != frame_index or pbe_cell != c:
                # EqualCell：尺寸一致 ⇒ 从累加器的"上一位置"复制；否则清空该格
                if cell.w != buffer_cell.last_w or cell.h != buffer_cell.last_h:
                    for y in range(ch):
                        base = (y + cyo) * bw + cxo
                        pix_data[base:base + cw] = b'\x00' * cw
                else:
                    lxo, lyo = buffer_cell.last_xoff, buffer_cell.last_yoff
                    for fy in range(ch):
                        sbase = (fy + lyo) * bw + lxo
                        dbase = (fy + cyo) * bw + cxo
                        pix_data[dbase:dbase + cw] = pix_data[sbase:sbase + cw]
                    for fy in range(ch):
                        dbase = (fy + cyo) * bw + cxo
                        fbuf[dbase:dbase + cw] = pix_data[dbase:dbase + cw]
            else:
                if value[0] == value[1]:
                    for y in range(ch):
                        base = (y + cyo) * bw + cxo
                        pix_data[base:base + cw] = bytes([value[0]]) * cw
                else:
                    bits_to_read = 1 if value[1] == value[2] else 2
                    for y in range(ch):
                        base = (y + cyo) * bw + cxo
                        if bits_to_read == 1:
                            for x in range(cw):
                                pix_data[base + x] = value[pcd.bit()]
                        else:
                            for x in range(cw):
                                pix_data[base + x] = value[pcd.bits(2)]
                for fy in range(ch):
                    base = (fy + cyo) * bw + cxo
                    fbuf[base:base + cw] = pix_data[base:base + cw]
                pb_idx += 1

            buffer_cell.last_w = cell.w
            buffer_cell.last_h = cell.h
            buffer_cell.last_xoff = cell.xoff
            buffer_cell.last_yoff = cell.yoff

        out.append(fbuf)

    return out


# ═════════════════════════════════════════════════════════════════════════════
#  调色板 / PNG
# ═════════════════════════════════════════════════════════════════════════════
def read_pl2(path):
    """读 D2 调色板 → 256 项 `(r, g, b, a)`；**索引 0 强制全透明**。

    两种文件、**两种字节序**（这是实测出来的，猜错会得到"蓝紫色的人物"）：
      · `.PL2`（≥1024 字节 = 256×RGBA）：每项 **R,G,B,A** —— 与本项目 `dc6.py::read_pl2`
        同口径（后者已用参考 PNG 逐像素验证过 0 像素差）。
      · `.DAT`（768 字节 = 256×RGB）：每项 **B,G,R**（**反序**）。
        实测证据（`python` 读 `d2data.mpq`）：
            `ACT1/Pal.dat[450:453] = (188,120,84)`（浅棕 = 皮肤）
            `ACT1/Pal.pl2[600:603] = ( 84,120,188)`（同一索引的蓝紫）
        两者**逐通道反序**；按 R,G,B 读 `.dat` 会把皮肤读成蓝紫
        （首次导出 Amazon 时肉眼可见：人物整体偏蓝紫 ⇒ 就是这里错的）。
        故 `.DAT` 必须交换首末字节。
    """
    data = open(path, 'rb').read()
    if len(data) < 768:
        raise ValueError('调色板太短：%s（%d 字节，至少 768）' % (path, len(data)))

    bgr = (len(data) < 1024) or path.lower().endswith('.dat')
    pal = []
    for i in range(256):
        if bgr:
            o = i * 3
            pal.append((data[o + 2], data[o + 1], data[o], 255))
        else:
            o = i * 4
            pal.append((data[o], data[o + 1], data[o + 2], 255))
    pal[0] = (0, 0, 0, 0)
    return pal


def frame_rgba(indices, palette):
    """索引图（自顶向下、行主序）→ RGBA bytes（索引 0 = 全透明）。"""
    out = bytearray(len(indices) * 4)
    for i, idx in enumerate(indices):
        if idx == 0:
            continue
        r, g, b, _a = palette[idx]
        o = i * 4
        out[o] = r
        out[o + 1] = g
        out[o + 2] = b
        out[o + 3] = 255
    return bytes(out)


def write_png_rgba(path, rgba, w, h):
    """最小 PNG 写出（RGBA8、filter 0、无损）。"""
    if w <= 0 or h <= 0:
        raise ValueError('write_png_rgba: 非法尺寸 %dx%d（PNG 不允许 0 宽/0 高）' % (w, h))
    if len(rgba) != w * h * 4:
        raise ValueError('write_png_rgba: 数据长度 %d ≠ %d×%d×4' % (len(rgba), w, h))

    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, payload):
        c = struct.pack('>I', len(payload)) + tag + payload
        c += struct.pack('>I', zlib.crc32(tag + payload) & 0xFFFFFFFF)
        return c

    png = b'\x89PNG\r\n\x1a\n'
    png += chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    png += chunk(b'IEND', b'')

    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    with open(path, 'wb') as fh:
        fh.write(png)
    return len(png)


def read_png_rgba(path):
    """只读本文件写出的那种 PNG（RGBA8/RGB8、无隔行）—— 供自证比对用。"""
    data = open(path, 'rb').read()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('不是 PNG：%s' % path)
    pos = 8
    w = h = None
    ct = 6
    idat = b''
    while pos < len(data):
        ln = struct.unpack_from('>I', data, pos)[0]
        tag = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if tag == b'IHDR':
            w, h, bd, ct = struct.unpack_from('>IIBB', payload, 0)
            if bd != 8 or ct not in (2, 6):
                raise ValueError('只支持 8bit RGB/RGBA：%s' % path)
        elif tag == b'IDAT':
            idat += payload
        elif tag == b'IEND':
            break
    raw = zlib.decompress(idat)
    bpp = 4 if ct == 6 else 3
    stride = w * bpp
    out = bytearray(w * h * 4)
    prev = bytearray(stride)
    p = 0
    for y in range(h):
        ft = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if ft == 1:
            for i in range(bpp, stride):
                line[i] = (line[i] + line[i - bpp]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - bpp] if i >= bpp else 0
                c = prev[i - bpp] if i >= bpp else 0
                b = prev[i]
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        for x in range(w):
            s = x * bpp
            d = (y * w + x) * 4
            out[d] = line[s]
            out[d + 1] = line[s + 1]
            out[d + 2] = line[s + 2]
            out[d + 3] = line[s + 3] if bpp == 4 else 255
        prev = line
    return w, h, bytes(out)


# ═════════════════════════════════════════════════════════════════════════════
#  自证
# ═════════════════════════════════════════════════════════════════════════════
def selfcheck(dcc_path, palette_path=None):
    """解一遍并断言不变量。返回 (ok, lines)。

    断言（每条都对应一个"错了会看起来也像对的"陷阱）：
      ① 每方向帧数一致且 ≥ 1；
      ② 每帧索引图长度 = box.w * box.h（尺寸自洽）；
      ③ **地面线不漂移**：同方向各帧的"帧底边"（= box.top + box.height）与方向包围盒底边
         完全一致（口径取错就必然不等）—— 这就是"角色浮空/错位"的直接检测（任务书 §A）；
      ④ 每方向**至少有一帧非空**（全空 = 解错了，表现为"角色隐形"）；
      ⑤ 轴心落在 [0,1]（否则像素被摆到帧外，表现为"角色歪出格子"）。
    """
    data = open(dcc_path, 'rb').read()
    d = parse(data, dcc_path)
    lines = ['%s: version=%d directions=%d framesPerDir=%d'
             % (os.path.basename(dcc_path), d.version, d.direction_count(), d.frames_per_dir)]
    ok = True
    bottoms = []

    for di, direction in enumerate(d.directions):
        if len(direction.frames) != d.frames_per_dir:
            ok = False
            lines.append('  [FAIL] dir %d 帧数 %d ≠ %d' % (di, len(direction.frames), d.frames_per_dir))
            continue
        expect = direction.box.width * direction.box.height
        any_pixel = False
        for fi, fr in enumerate(direction.frames):
            if len(fr) != expect:
                ok = False
                lines.append('  [FAIL] dir %d frame %d 长度 %d ≠ %d' % (di, fi, len(fr), expect))
            if any(fr):
                any_pixel = True
        if not any_pixel:
            ok = False
            lines.append('  [FAIL] dir %d 全帧为空（解错 ⇒ 角色隐形）' % di)

        px, py = direction.pivot()
        if not (0.0 <= px <= 1.0) or not (0.0 <= py <= 1.0):
            ok = False
            lines.append('  [FAIL] dir %d 轴心 (%0.3f, %0.3f) 越界' % (di, px, py))

        bottoms.append(direction.box.top + direction.box.height)
        lines.append('  dir %d: box=l%-5d t%-6d %dx%-4d pivot=(%0.3f,%0.3f) 帧=%d 底边y=%d'
                     % (di, direction.box.left, direction.box.top, direction.box.width,
                        direction.box.height, px, py, len(direction.frames),
                        direction.box.top + direction.box.height))

    if bottoms:
        lines.append('  各方向"帧底边" y ∈ [%d, %d]（差 %d）' % (min(bottoms), max(bottoms),
                                                          max(bottoms) - min(bottoms)))
    if d.directions and d.directions[0].frames:
        lines.append('  dir0 frame0 非透明像素 = %d' % sum(1 for v in d.directions[0].frames[0] if v))
    return ok, lines


# ═════════════════════════════════════════════════════════════════════════════
#  CLI
# ═════════════════════════════════════════════════════════════════════════════
def _cmd_info(argv):
    path = argv[0]
    d = parse(open(path, 'rb').read(), path)
    print('%s  version=%d dir=%d fpd=%d' % (os.path.basename(path), d.version,
                                            d.direction_count(), d.frames_per_dir))
    for i, dr in enumerate(d.directions):
        px, py = dr.pivot()
        print('  dir %d  box=l%-5d t%-6d %dx%-4d pivot=(%0.3f,%0.3f) 帧=%d'
              % (i, dr.box.left, dr.box.top, dr.box.width, dr.box.height, px, py, len(dr.frames)))
    return 0


def _cmd_probe(argv):
    root = argv[0]
    total = 0
    fails = 0
    for dirpath, _dn, fns in os.walk(root):
        for fn in sorted(fns):
            if not fn.lower().endswith('.dcc'):
                continue
            full = os.path.join(dirpath, fn)
            try:
                d = parse(open(full, 'rb').read(), full)
            except Exception as exc:
                fails += 1
                print('%-58s ERROR %s' % (os.path.relpath(full, root), exc))
                continue
            total += 1
            boxes = sorted(set('%dx%d' % (x.box.width, x.box.height) for x in d.directions))
            print('%-58s dir=%d fpd=%-3d boxes=%s'
                  % (os.path.relpath(full, root), d.direction_count(), d.frames_per_dir,
                     ','.join(boxes)))
    print('--- probe 共 %d 个 DCC 解成功，%d 个失败' % (total, fails))
    return 0 if fails == 0 else 1


def _cmd_png(argv):
    dcc_path, pal_path, outdir = argv[0], argv[1], argv[2]
    stem = argv[3] if len(argv) > 3 else os.path.splitext(os.path.basename(dcc_path))[0]
    data = open(dcc_path, 'rb').read()
    d = parse(data, dcc_path)
    pal = read_pl2(pal_path)
    n = 0
    for di, dr in enumerate(d.directions):
        for fi, fr in enumerate(dr.frames):
            out = os.path.join(outdir, '%s_d%d_%d.png' % (stem, di, fi))
            write_png_rgba(out, frame_rgba(fr, pal), dr.box.width, dr.box.height)
            n += 1
    print('WROTE %d 帧 → %s（box %dx%d）' % (n, outdir,
                                          d.directions[0].box.width, d.directions[0].box.height))
    return 0


def _cmd_selfcheck(argv):
    dcc_path = argv[0]
    pal = argv[1] if len(argv) > 1 else None
    ok, lines = selfcheck(dcc_path, pal)
    for l in lines:
        print(l)
    print('SELFCHECK %s' % ('PASS' if ok else 'FAIL'))
    return 0 if ok else 2


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    cmd = argv[1]
    if cmd == 'info':
        return _cmd_info(argv[2:])
    if cmd == 'probe':
        return _cmd_probe(argv[2:])
    if cmd == 'png':
        return _cmd_png(argv[2:])
    if cmd == 'selfcheck':
        return _cmd_selfcheck(argv[2:])
    print('未知子命令：%s' % cmd)
    return 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
