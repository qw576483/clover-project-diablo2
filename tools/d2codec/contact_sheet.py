# -*- coding: utf-8 -*-
"""开发辅助：把一个 pack 的瓦片 PNG 拼成"联络表"（带序号），供**人眼/多模态**挑选瓦片。

这个脚本**不参与交付**（不改游戏资源），只是让"挑哪块瓦片当地砖/帐篷"有据可查
（而不是靠猜文件名）。依赖 Pillow（仅本脚本；解码与导出链路零依赖）。

用法：
    python contact_sheet.py <pack 目录> <输出 png> [--cols 16] [--scale 2] [--start 0] [--count 64]
"""

import os
import sys

from PIL import Image, ImageDraw


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2

    directory = argv[1]
    out_path = argv[2]
    cols = int(argv[argv.index('--cols') + 1]) if '--cols' in argv else 16
    scale = int(argv[argv.index('--scale') + 1]) if '--scale' in argv else 2
    start = int(argv[argv.index('--start') + 1]) if '--start' in argv else 0
    count = int(argv[argv.index('--count') + 1]) if '--count' in argv else 10 ** 6

    files = sorted(f for f in os.listdir(directory) if f.lower().endswith('.png'))
    files = files[start:start + count]
    if not files:
        print('目录里没有 png：%s' % directory)
        return 1

    imgs = [(f, Image.open(os.path.join(directory, f)).convert('RGBA')) for f in files]
    cell_w = max(i.width for _, i in imgs)
    cell_h = max(i.height for _, i in imgs) + 14

    rows = (len(imgs) + cols - 1) // cols
    sheet = Image.new('RGBA', (cols * cell_w * scale, rows * cell_h * scale), (40, 40, 48, 255))
    draw = ImageDraw.Draw(sheet)

    for n, (name, im) in enumerate(imgs):
        cx = (n % cols) * cell_w * scale
        cy = (n // cols) * cell_h * scale
        im = im.resize((im.width * scale, im.height * scale), Image.NEAREST)
        sheet.paste(im, (cx, cy + 14 * scale), im)
        draw.text((cx + 2, cy + 1), name, fill=(255, 240, 120, 255))

    sheet.save(out_path)
    print('%d 张 → %s（%dx%d，cols=%d scale=%d）'
          % (len(imgs), out_path, sheet.width, sheet.height, cols, scale))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
