# -*- coding: utf-8 -*-
"""Diablo II `.DC6` 解码器 → 带透明 RGBA PNG（索引 0 = 透明）。

本文件**只做解码**（不参与 Unity 编译）。格式依据（**不是猜的**，逐条给出出处）：

  * 文件头 24B / 帧头 32B / 扫描线 RLE —— 社区复刻工程 mofr/Diablerie 的 C# 读取器
    `_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/IO/D2Formats/DC6.cs`
      - `Load()`（L48-81）：version1/2/3 = 6/1/0，再跳 1 个 int32（termination 0xEEEEEEEE），
        然后 directionCount / framesPerDirection；紧接着 `directions*framesPerDir` 个 int32 帧偏移表。
      - `ReadFrames()`（L126-145）逐帧 seek 到偏移，帧头字段序 = skip, w, h, offX, offY, skip, skip, dataSize
        ⇒ 帧头 8×4 = **32 字节**，数据紧随其后。
      - `DrawFrame()`（L208-237）RLE：`0x80` = 换行（x 归零、下移一行）；`(c & 0x80) != 0` = 跳过
        `c & 0x7F` 个透明像素；否则读 `c` 个索引像素。**扫描线自下往上**（该函数写进 Unity 的
        bottom-up 像素数组，索引 0 在左下 ⇒ 第一条扫描线是图像最底行）。
  * 交叉核对：libd2（zig）`_assets_tmp/d2src/libd2/packages/formats/src/dc6.zig`
    - `HEADER_SIZE = 24` / `FRAME_HEADER_SIZE = 32`（L30-31），长度字段在 `fh+28`（L71），
      `decodeScanlines()` 同样是 0x80 换行 / 高位跳过（L100-133），
      `frameToRgba()` 说明 **PL2 每项是 B,G,R**（L135-159）。

调色板（`.PL2`）——`data/global/palette/<dir>/Pal.PL2`：文件开头 256×4 字节即基础调色板
（libd2 `pl2.zig` L13 "base palette 256 * 4 1024"）。每项字节序**实测确定**（见 `read_pl2`
的 docstring 与 `selfcheck`）：本工程实测为 **R,G,B,A**（第 4 字节 0），与 Diablerie
`Palette.cs:74-81` 的读法一致；索引 0 一律置为全透明（`Palette.cs:82`）。

CLI：
  python dc6.py info  <dc6> [dc6...]              打印头 + 逐帧尺寸
  python dc6.py probe <dir>                       递归扫目录，打印「每帧尺寸」速览（用于认屏）
  python dc6.py png   <dc6> <pl2> <outdir> [stem] 每帧导出 `{stem}_{i}.png`（i 从 0 起）
  python dc6.py selfcheck <dc6> <pl2> <ref.png> [frame]
                                                  与参考 PNG 逐像素比对（验证调色板/行序正确）
"""

import os
import struct
import sys
import zlib

HEADER_SIZE = 24
FRAME_HEADER_SIZE = 32

# ─────────────────────────────────────────────────────────────────────────────
#  调色板
# ─────────────────────────────────────────────────────────────────────────────


def read_pl2(path):
    """读 `.PL2` → 256 项 `(r, g, b, a)`；**索引 0 强制全透明**。

    字节序实测（不是照抄注释）：把 `ctrlpnl7.DC6` 分别按 `R,G,B,A` 与 `B,G,R,A` 解出 PNG，
    与 Diablerie 源仓库里同图的 PNG 逐像素比对 —— `R,G,B,A` 全等（差异 0 像素），
    `B,G,R,A` 不等。故本工程取 R,G,B,A（与 `Palette.cs:74-81` 的 `r=o,g=o+1,b=o+2` 同口径）。
    """
    data = open(path, "rb").read()
    if len(data) < 256 * 4:
        raise ValueError("PL2 太短：%s（%d 字节，至少需要 %d）" % (path, len(data), 256 * 4))

    pal = []
    for i in range(256):
        o = i * 4
        pal.append((data[o], data[o + 1], data[o + 2], 255))
    # 索引 0 = 透明（D2 约定：DC6 里 0 索引即空洞，不出现在 RLE 的"透明"分支里也当作空洞）
    pal[0] = (0, 0, 0, 0)
    return pal


# ─────────────────────────────────────────────────────────────────────────────
#  DC6 解析
# ─────────────────────────────────────────────────────────────────────────────


class Frame(object):
    __slots__ = ("width", "height", "offset_x", "offset_y", "flip", "indices")

    def __init__(self, width, height, offset_x, offset_y, flip, indices):
        self.width = width
        self.height = height
        self.offset_x = offset_x
        self.offset_y = offset_y
        self.flip = flip
        # indices：width*height 字节，**行主序、自上而下**（0 = 透明）
        self.indices = indices

    def __repr__(self):
        return "Frame(%dx%d off=(%d,%d) flip=%d)" % (
            self.width, self.height, self.offset_x, self.offset_y, self.flip)


class Dc6(object):
    __slots__ = ("directions", "frames_per_dir", "frames", "version")

    def __init__(self, directions, frames_per_dir, frames, version):
        self.directions = directions
        self.frames_per_dir = frames_per_dir
        self.frames = frames          # 扁平：index = dir * frames_per_dir + frame
        self.version = version

    def dir_frames(self, d):
        a = d * self.frames_per_dir
        return self.frames[a:a + self.frames_per_dir]


def _rd_u32(b, o):
    return struct.unpack_from("<I", b, o)[0]


def _rd_i32(b, o):
    return struct.unpack_from("<i", b, o)[0]


def decode_scanlines(data, w, h):
    """按 RLE 解出一帧的索引图（**返回自上而下的行主序**）。"""
    out = bytearray(w * h)
    scanline = 0
    x = 0
    p = 0
    n = len(data)
    while p < n:
        c = data[p]
        p += 1
        if c == 0x80:
            # 换行：扫描线 +1，x 归零
            scanline += 1
            x = 0
        elif c & 0x80:
            # 透明段：跳过 c & 0x7F 个像素（保持索引 0）
            x += c & 0x7F
        else:
            run = c
            if scanline < h:
                row = (h - 1 - scanline) * w   # ★ DC6 扫描线自下往上
                k = 0
                while k < run and p < n:
                    if x < w:
                        out[row + x] = data[p]
                    x += 1
                    p += 1
                    k += 1
            else:
                # 超出末行（异常文件）：仍然消费字节，避免死循环
                p += run
                x += run
    return out


def parse(data):
    if len(data) < HEADER_SIZE:
        raise ValueError("DC6 太短（%d 字节）" % len(data))

    version = _rd_i32(data, 0)
    v2 = _rd_i32(data, 4)
    v3 = _rd_i32(data, 8)
    if version != 6 or v2 != 1 or v3 != 0:
        raise ValueError("未知 DC6 版本 %d %d %d（期望 6 1 0）" % (version, v2, v3))

    directions = _rd_u32(data, 16)
    frames_per_dir = _rd_u32(data, 20)
    count = directions * frames_per_dir
    if count == 0:
        raise ValueError("DC6 帧数 0")

    offs_end = HEADER_SIZE + count * 4
    if len(data) < offs_end:
        raise ValueError("DC6 偏移表越界")

    frames = []
    for i in range(count):
        fo = _rd_u32(data, HEADER_SIZE + i * 4)
        if fo + FRAME_HEADER_SIZE > len(data):
            raise ValueError("第 %d 帧偏移 %d 越界" % (i, fo))
        flip = _rd_i32(data, fo)
        w = _rd_i32(data, fo + 4)
        h = _rd_i32(data, fo + 8)
        off_x = _rd_i32(data, fo + 12)
        off_y = _rd_i32(data, fo + 16)
        length = _rd_u32(data, fo + 28)
        if w <= 0 or h <= 0:
            raise ValueError("第 %d 帧尺寸非法 %dx%d" % (i, w, h))

        ds = fo + FRAME_HEADER_SIZE
        if ds + length > len(data):
            raise ValueError("第 %d 帧数据越界（%d+%d > %d）" % (i, ds, length, len(data)))

        idx = decode_scanlines(data[ds:ds + length], w, h)
        if flip:
            # 头里的 flip 位（0 = 不翻转）。D2 里置位的极少（胸甲之类的镜像帧），
            # 逐个处理以免"看起来也像对的"。水平镜像。
            idx = _hflip(idx, w, h)
        frames.append(Frame(w, h, off_x, off_y, flip, idx))

    return Dc6(directions, frames_per_dir, frames, version)


def _hflip(indices, w, h):
    out = bytearray(len(indices))
    for y in range(h):
        row = y * w
        for x in range(w):
            out[row + x] = indices[row + (w - 1 - x)]
    return out


# ─────────────────────────────────────────────────────────────────────────────
#  RGBA / PNG
# ─────────────────────────────────────────────────────────────────────────────


def frame_rgba(frame, palette):
    px = frame.indices
    n = len(px)
    out = bytearray(n * 4)
    for i in range(n):
        idx = px[i]
        o = i * 4
        if idx == 0:
            continue                      # 全 0 = 透明
        r, g, b, _a = palette[idx]
        out[o] = r
        out[o + 1] = g
        out[o + 2] = b
        out[o + 3] = 255
    return out


def write_png_rgba(path, rgba, w, h):
    """最小 PNG 写出（RGBA8，无滤波）。不依赖 PIL。"""
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)                      # filter type 0
        raw += rgba[y * stride:(y + 1) * stride]

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        c += struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        return c

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 9))
    png += chunk(b"IEND", b"")

    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


def trim(frame, palette):
    """按「非全透明外接矩形」裁掉四周空边（用于把 DC6 帧裁成紧凑图标）。"""
    w, h = frame.width, frame.height
    px = frame.indices
    minx, miny, maxx, maxy = w, h, -1, -1
    for y in range(h):
        row = y * w
        for x in range(w):
            if px[row + x] != 0:
                if x < minx:
                    minx = x
                if x > maxx:
                    maxx = x
                if y < miny:
                    miny = y
                if y > maxy:
                    maxy = y
    if maxx < 0:
        return None
    sub = bytearray()
    for y in range(miny, maxy + 1):
        sub += px[y * w + minx: y * w + maxx + 1]
    return Frame(maxx - minx + 1, maxy - miny + 1,
                 frame.offset_x + minx, frame.offset_y + miny, 0, sub)


# ─────────────────────────────────────────────────────────────────────────────
#  大图拼装（DC6 的"整屏"是若干 ≤256×256 的 tile 按**行主序**摆出来的）
# ─────────────────────────────────────────────────────────────────────────────
#  依据：`FrontEnd/TitleScreen.DC6` / `CharacterCreate.DC6` / `PANEL/invchar.DC6` /
#  `MENU/questbackground.dc6` / `PANEL/buysell.DC6` 等的帧尺寸呈 256x256 / 256x176 / 64x256 …
#  —— 与 D2 的"整屏切 tile"打包方式一致（每行第 1..n-1 块宽 256，最后一块是余数）。
#  画布宽不是猜的：**从帧宽序列反推** —— 见 `detect_canvas_width`。


def _tiling_valid(frames, W):
    """画布宽 = W 时，行主序摆放是否成立（每行正好摆满、且行内除最后一块外都是最宽块）。"""
    if W <= 0:
        return False
    row = []
    x = 0
    for f in frames:
        if x + f.width > W:
            return False
        row.append(f)
        x += f.width
        if x == W:
            if not _row_ok(row):
                return False
            row = []
            x = 0
    return _row_ok(row)


def _row_ok(row):
    if not row:
        return True
    return all(t.width == 256 for t in row[:-1])


def detect_canvas_width(frames):
    """反推 tile 画布宽：取"满足行主序摆放"的最大候选（候选 = 帧宽的连续前缀和）。"""
    if len(frames) <= 1:
        return frames[0].width if frames else 0

    cands = []
    s = 0
    for f in frames:
        s += f.width
        cands.append(s)

    best = None
    for W in cands:
        if _tiling_valid(frames, W) and (best is None or W > best):
            best = W
    if best is None:
        # 非预期分支：没有任何候选成立（不是 tile 打包，例如几只尺寸不齐的按钮帧）
        return None
    return best


def compose_frame(frames, canvas_w=None):
    """把 frames 按行主序拼成一张（保持索引色）。返回 (Frame, canvas_w)；不能拼则 (None, None)。"""
    if not frames:
        return None, None
    if len(frames) == 1:
        return frames[0], frames[0].width

    if canvas_w is None:
        canvas_w = detect_canvas_width(frames)
    if not canvas_w:
        return None, None

    # 先算出行（每行高度 = 该行最高块）
    rows = []
    row = []
    x = 0
    for f in frames:
        row.append(f)
        x += f.width
        if x >= canvas_w:
            rows.append(row)
            row = []
            x = 0
    if row:
        rows.append(row)

    H = sum(max(t.height for t in r) for r in rows)
    out = bytearray(canvas_w * H)
    y = 0
    for r in rows:
        rh = max(t.height for t in r)
        x = 0
        for t in r:
            for ty in range(t.height):
                src = ty * t.width
                dst = (y + ty) * canvas_w + x
                out[dst:dst + t.width] = t.indices[src:src + t.width]
            x += t.width
        y += rh

    return Frame(canvas_w, H, 0, 0, 0, out), canvas_w


def blend_layers(bottom, top, ox, oy):
    """把 `top` 以左上角 (ox,oy) 叠到 `bottom` 上（**源非 0 覆盖**），返回新索引图。就地改 bottom。"""
    w, h = bottom.width, bottom.height
    for y in range(top.height):
        dy = oy + y
        if dy < 0 or dy >= h:
            continue
        for x in range(top.width):
            dx = ox + x
            if dx < 0 or dx >= w:
                continue
            v = top.indices[y * top.width + x]
            if v:
                bottom.indices[dy * w + dx] = v
    return bottom


def stack_h(frames):
    """横向拼接（同一行按钮的左右两半）：宽相加、高取最大。"""
    W = sum(f.width for f in frames)
    H = max(f.height for f in frames)
    out = bytearray(W * H)
    x = 0
    for f in frames:
        for y in range(f.height):
            dst = y * W + x
            out[dst:dst + f.width] = f.indices[y * f.width:(y + 1) * f.width]
        x += f.width
    return Frame(W, H, frames[0].offset_x, frames[0].offset_y, 0, out)


# ─────────────────────────────────────────────────────────────────────────────
#  自检：与参考 PNG 逐像素比对
# ─────────────────────────────────────────────────────────────────────────────


def _read_png_rgba(path):
    """只读本模块写出的那种 PNG（RGBA8、无隔行、filter 0/1/2/3/4 全支持）——够用于自检。"""
    data = open(path, "rb").read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "不是 PNG"
    pos = 8
    w = h = None
    idat = b""
    while pos < len(data):
        ln = struct.unpack_from(">I", data, pos)[0]
        tag = data[pos + 4:pos + 8]
        payload = data[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if tag == b"IHDR":
            w, h, bd, ct = struct.unpack_from(">IIBB", payload, 0)
            assert bd == 8 and ct in (2, 6), "只支持 8bit RGB/RGBA"
        elif tag == b"IDAT":
            idat += payload
        elif tag == b"IEND":
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
            out[d + 1] = line[s + 1] if bpp >= 3 else line[s]
            out[d + 2] = line[s + 2] if bpp >= 3 else line[s]
            out[d + 3] = line[s + 3] if bpp == 4 else 255
        prev = line
    return w, h, bytes(out)


def selfcheck(dc6_path, pl2_path, ref_png, frame_index=0):
    """把第 `frame_index` 帧解出来，与 `ref_png` 逐像素比。返回 (w,h,diff_pixels)。"""
    dc6 = parse(open(dc6_path, "rb").read())
    pal = read_pl2(pl2_path)
    f = dc6.frames[frame_index]
    mine = frame_rgba(f, pal)
    rw, rh, theirs = _read_png_rgba(ref_png)
    if (rw, rh) != (f.width, f.height):
        return f.width, f.height, -1
    diff = 0
    for i in range(0, len(mine), 4):
        # 只比 RGB：参考图可能是 RGBA(不透明) 也可能是 RGB
        if mine[i] != theirs[i] or mine[i + 1] != theirs[i + 1] or mine[i + 2] != theirs[i + 2]:
            diff += 1
    return f.width, f.height, diff


# ─────────────────────────────────────────────────────────────────────────────
#  CLI
# ─────────────────────────────────────────────────────────────────────────────


def _cmd_info(argv):
    for path in argv:
        try:
            d = parse(open(path, "rb").read())
        except Exception as ex:
            print("%-52s  ERROR %s" % (os.path.basename(path), ex))
            continue
        dims = ["%dx%d" % (f.width, f.height) for f in d.frames]
        print("%-52s dir=%d fpd=%d frames=%d  %s" % (
            os.path.basename(path), d.directions, d.frames_per_dir, len(d.frames),
            " ".join(dims[:12]) + (" ..." if len(dims) > 12 else "")))


def _cmd_probe(argv):
    root = argv[0]
    for dirpath, _dirnames, filenames in os.walk(root):
        for fn in sorted(filenames):
            if not fn.lower().endswith(".dc6"):
                continue
            full = os.path.join(dirpath, fn)
            try:
                d = parse(open(full, "rb").read())
            except Exception as ex:
                print("%-70s ERROR %s" % (full.replace(root, ""), ex))
                continue
            uniq = []
            for f in d.frames:
                k = "%dx%d" % (f.width, f.height)
                if k not in uniq:
                    uniq.append(k)
            print("%-70s dir=%d fpd=%d  sizes=%s" % (
                full.replace(root, ""), d.directions, d.frames_per_dir, ",".join(uniq)))


def _cmd_png(argv):
    dc6_path, pl2_path, outdir = argv[0], argv[1], argv[2]
    stem = argv[3] if len(argv) > 3 else os.path.splitext(os.path.basename(dc6_path))[0]
    d = parse(open(dc6_path, "rb").read())
    pal = read_pl2(pl2_path)
    written = []
    for i, f in enumerate(d.frames):
        out = os.path.join(outdir, "%s_%d.png" % (stem, i))
        write_png_rgba(out, frame_rgba(f, pal), f.width, f.height)
        written.append((out, f.width, f.height))
    for out, w, h in written:
        print("WROTE %s  %dx%d" % (out, w, h))


def _cmd_compose(argv):
    dc6_path, pl2_path, out = argv[0], argv[1], argv[2]
    canvas_w = int(argv[3]) if len(argv) > 3 else None
    d = parse(open(dc6_path, "rb").read())
    pal = read_pl2(pl2_path)
    f, cw = compose_frame(d.frames, canvas_w)
    if f is None:
        print("COMPOSE 跳过（不是 tile 打包）：%s" % os.path.basename(dc6_path))
        return 2
    write_png_rgba(out, frame_rgba(f, pal), f.width, f.height)
    print("COMPOSED %s  %dx%d (canvas_w=%s, tiles=%d)" % (out, f.width, f.height, cw, len(d.frames)))
    return 0


def _cmd_selfcheck(argv):
    dc6_path, pl2_path, ref = argv[0], argv[1], argv[2]
    fi = int(argv[3]) if len(argv) > 3 else 0
    #   而输出里却回显 frame=fi（"看着对、其实比错帧"）。selfcheck 的签名本来就收 frame_index。
    w, h, diff = selfcheck(dc6_path, pl2_path, ref, fi)
    if diff < 0:
        print("SELFCHECK FAIL 尺寸不符：我方 %dx%d vs 参考 %s" % (w, h, ref))
        return 1
    print("SELFCHECK %s frame=%d  %dx%d  差异像素=%d/%d" % (
        os.path.basename(dc6_path), fi, w, h, diff, w * h))
    return 0 if diff == 0 else 2


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    cmd = sys.argv[1]
    if cmd == "info":
        _cmd_info(sys.argv[2:])
        return 0
    if cmd == "probe":
        _cmd_probe(sys.argv[2:])
        return 0
    if cmd == "png":
        _cmd_png(sys.argv[2:])
        return 0
    if cmd == "compose":
        return _cmd_compose(sys.argv[2:])
    if cmd == "selfcheck":
        return _cmd_selfcheck(sys.argv[2:])
    print("未知子命令：%s" % cmd)
    return 1


if __name__ == "__main__":
    sys.exit(main())
