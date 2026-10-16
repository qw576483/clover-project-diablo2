# -*- coding: utf-8 -*-
"""Diablo II `.PL2` 调色板解码器（纯 Python，无第三方依赖）。

格式（实测确认，见下方"实测证据"）：
    offset 0x000 : 256 条 × 4 字节 = 1024 字节，每条 = (R, G, B, 0)
                  第 4 字节**恒为 0**（实测 ACT1 的 1024 字节里非零个数 = 0）
    offset 0x400 : 其余部分与调色板无关（D2 在这些文件尾部还塞了别的东西，
                  实测 443175 字节；**只取前 1024 字节**即可，与参考实现一致）

实测证据（`tools/d2codec/pl2.py --dump`）：
    ACT1/Pal.PL2 前 24 条 = [(0,0,0), (36,0,0), (28,24,8), (44,36,16), (60,52,24),
                            (92,0,0), (72,64,32), (84,72,40), (144,0,0), (140,72,16),
                            (188,0,0), (208,132,32), (244,196,108), ...]
    —— 棕色/暗红渐变 + 灰阶：正是 Act I（泥地/草地/岩石）的用色。
    若把三元组按 BGR 读，会得到一片发蓝的色板，与 Act I 实景不符 ⇒ **顺序是 R,G,B**。

**Act I 用哪个 pl2**：`data/global/palette/ACT1/Pal.PL2`
    —— 出处：格式参考实现 `_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/
    IO/D2Formats/Palette.cs:12`（`{ PaletteType.Act1, @"data\\global\\palette\\ACT1\\Pal.PL2" }`），
    而 `PaletteType.Act1 = 0` 且 DS1 的 `act` 字段直接转 `PaletteType`（`Palette.GetPalette(act)`，
    DS1.cs:112）⇒ Act I 的所有 dt1/ds1 一律用 ACT1/Pal.PL2。

用法：
    python pl2.py <Pal.PL2> [--dump [N]]
"""

import sys

# D2 调色板条目数（固定 256）。
PALETTE_SIZE = 256

# 每条 4 字节（第 4 字节恒 0，丢弃）。
PALETTE_STRIDE = 4


def load_pl2(path):
    """读 `.PL2` → `[(r, g, b, a), ...]`，长度恒为 256；`a` 一律 255。

    ⚠️ 索引 0 保持 (0,0,0,255)，**不改成透明** —— DT1 瓦片的"透明"由 RLE 的跳过来表达
    （见 `dt1.py` 的 mask），不是"索引 0 = 透明"。
    """
    with open(path, 'rb') as fh:
        data = fh.read()

    need = PALETTE_SIZE * PALETTE_STRIDE
    if len(data) < need:
        raise ValueError('%s: 文件只有 %d 字节，读不满 256×4 字节的调色板' % (path, len(data)))

    palette = []
    for i in range(PALETTE_SIZE):
        off = i * PALETTE_STRIDE
        palette.append((data[off], data[off + 1], data[off + 2], 255))
    return palette


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2

    palette = load_pl2(argv[1])
    print('%s → %d 条（每条 4 字节，第 4 字节恒 0）' % (argv[1], len(palette)))
    print('前 16 条 : %s' % (palette[:16],))
    print('中段 16 条: %s' % (palette[120:136],))

    if len(argv) > 2 and argv[2] == '--dump':
        n = int(argv[3]) if len(argv) > 3 else 256
        for i in range(min(n, len(palette))):
            print('%3d  %s' % (i, palette[i]))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
