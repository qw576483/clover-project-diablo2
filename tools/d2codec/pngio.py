# -*- coding: utf-8 -*-
"""极简 PNG 写出（纯 Python：`zlib` + `struct`，无第三方依赖）。

只做一件事：把 RGBA 字节流写成 8 位真彩 + alpha 的 PNG。
本项目所有解出的瓦片/物件贴图都经这里落盘，保证：
  · 无损（`filter=0`、`compresslevel=9`）；
  · **不重采样、不调色**（像素逐字节来自 DT1 + PL2）。
"""

import struct
import zlib

_PNG_SIG = b'\x89PNG\r\n\x1a\n'


def _chunk(tag, payload):
    return (struct.pack('>I', len(payload)) + tag + payload
            + struct.pack('>I', zlib.crc32(tag + payload) & 0xFFFFFFFF))


def write_rgba(path, width, height, rgba):
    """把 `rgba`（长度 = w*h*4，行序**自顶向下**）写成 PNG。

    PNG 的行序也是自顶向下 ⇒ **不做翻转**。
    """
    if width <= 0 or height <= 0:
        # 非预期：0×0 的 PNG 是非法文件（Unity 会报 "File could not be read"，
        # PIL 也认不出来）⇒ 必须在这里拦住，别写出坏文件。
        raise ValueError('write_rgba: 非法尺寸 %dx%d（PNG 不允许 0 宽/0 高）' % (width, height))

    if len(rgba) != width * height * 4:
        raise ValueError('write_rgba: 数据长度 %d ≠ %d×%d×4=%d'
                         % (len(rgba), width, height, width * height * 4))

    raw = bytearray()
    stride = width * 4
    for y in range(height):
        raw.append(0)                                   # filter type 0 (None)
        raw += rgba[y * stride:(y + 1) * stride]

    out = bytearray(_PNG_SIG)
    out += _chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0))
    out += _chunk(b'IDAT', zlib.compress(bytes(raw), 9))
    out += _chunk(b'IEND', b'')

    with open(path, 'wb') as fh:
        fh.write(bytes(out))
    return len(out)
