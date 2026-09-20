# -*- coding: utf-8 -*-
"""Diablo II 字符串表 `.tbl` 解码器 → UTF-8 文本（中文版 = **CP950 / Big5**）。

格式依据（**不是猜的**）：libd2（zig）`_assets_tmp/d2src/libd2/packages/formats/src/strtbl.zig`
的 `Table.parse` / `Table.get`，逐字段对照（行号取自该文件）：

    0x00 u16 crc
    0x02 u16 element count        （id 取值 0..count-1）
    0x04 u32 hash table size
    0x08 u8  version
    0x09 u32 data start offset
    0x0d u32 hash max tries
    0x11 u32 file size            （== 文件真实长度，是"解析对不对"的自证字段）
    0x15 u16 index[count]         （id → 条目号）
         entry[hash size]         （每条 17 字节）

  条目（`entry_len = 17`）布局：`u8 used, u16 id, u32 hash, u32 key offset,
  u32 string offset, u16 length`；key 与 string 都是 NUL 结尾，`length` 含结尾。
  查表走 `index[id]`（不是 hash）——UI 只拿得到数字 id。
  `patch_bias = 0xd8f0`（id ≥ 10000 先查 patch 表，**u16 回绕**）、
  `expansion_bias = 0xb1e0`（id ≥ 20000 先查 expansion 表）。

编码：**逐串自适应**（`--enc auto`，默认）。任务书假设中文版是 **CP950/Big5**，但**本机这份
中文 `string.tbl` 实测是 UTF-8**（原始字节 e6 89 8b e6 96 a7 = 「手斧」；cp950/big5/big5hkscs
全部在该字节处报 illegal multibyte）。⇒ 本脚本按「utf-8 → cp950 → cp1252」依次试，取第一个
**不报错**的编码；英文表 `eng/string.tbl` 实测是 CP1252（id 1976 = `Hand Axe`）。
（想强制指定就传 `--enc cp950` / `--enc cp1252`。）

CLI：
  python tbl.py info  <tbl>                       打印头（含 file size 自证）
  python tbl.py get   <tbl> <id> [id...] [--enc cp950]
  python tbl.py dump  <tbl> <out.txt> [--enc cp950] [--min 0] [--max 0]
  python tbl.py set   <base.tbl> <out.txt> [--patch p.tbl] [--exp e.tbl] [--enc cp950]
                                                  三表合一（口径同 strtbl.zig 的 Set.get）
"""

import os
import struct
import sys

HEADER_LEN = 0x15
ENTRY_LEN = 17
PATCH_BIAS = 0xD8F0
EXPANSION_BIAS = 0xB1E0
PATCH_MIN = 10000
EXPANSION_MIN = 20000


class Tbl(object):
    def __init__(self, data, encoding):
        self.bytes = data
        self.encoding = encoding
        if len(data) < HEADER_LEN:
            raise ValueError("tbl 太短（%d 字节）" % len(data))
        self.crc, self.count = struct.unpack_from("<HH", data, 0)
        self.hash_size = struct.unpack_from("<I", data, 4)[0]
        self.version = data[8]
        self.data_start = struct.unpack_from("<I", data, 9)[0]
        self.hash_max_tries = struct.unpack_from("<I", data, 13)[0]
        self.file_size = struct.unpack_from("<I", data, 17)[0]
        if self.file_size != len(data):
            raise ValueError("头里的 file_size=%d 与真实长度 %d 不符 ⇒ 不是这张表"
                             % (self.file_size, len(data)))
        need = HEADER_LEN + self.count * 2 + self.hash_size * ENTRY_LEN
        if len(data) < need:
            raise ValueError("表体越界：需要 %d，实际 %d" % (need, len(data)))

    @property
    def entries_base(self):
        return HEADER_LEN + self.count * 2

    def raw(self, idx):
        """id → 原始字节串（不含结尾 NUL）；miss 返回 None。"""
        if idx < 0 or idx >= self.count:
            return None
        slot = struct.unpack_from("<H", self.bytes, HEADER_LEN + idx * 2)[0]
        if slot >= self.hash_size:
            return None
        at = self.entries_base + slot * ENTRY_LEN
        if self.bytes[at] != 1:
            return None
        soff = struct.unpack_from("<I", self.bytes, at + 11)[0]
        if soff >= len(self.bytes):
            return None
        end = self.bytes.find(b"\x00", soff)
        if end < 0:
            return None
        return self.bytes[soff:end]

    def get(self, idx):
        raw = self.raw(idx)
        if raw is None:
            return None
        return decode(raw, self.encoding)

    def items(self):
        for i in range(self.count):
            t = self.get(i)
            if t is not None:
                yield i, t


class TblSet(object):
    """base + patch + expansion 的查询口径（照 `strtbl.zig::Set.get`）。"""

    def __init__(self, base, patch=None, expansion=None):
        self.base, self.patch, self.expansion = base, patch, expansion

    def get(self, idx):
        if idx >= EXPANSION_MIN and self.expansion is not None:
            s = self.expansion.get((idx + EXPANSION_BIAS) & 0xFFFF)
            if s is not None:
                return s
        if idx >= PATCH_MIN and self.patch is not None:
            s = self.patch.get((idx + PATCH_BIAS) & 0xFFFF)
            if s is not None:
                return s
        if self.base is not None:
            return self.base.get(idx)
        return None


def load(path, encoding):
    return Tbl(open(path, "rb").read(), encoding)


def decode(raw, enc="auto"):
    """按 `enc` 解码；`auto` = 依次试 utf-8 → cp950 → cp1252，取第一个成功的。"""
    if enc != "auto":
        return raw.decode(enc, errors="replace")
    for e in ("utf-8", "cp950", "cp1252"):
        try:
            return raw.decode(e)
        except UnicodeDecodeError:
            continue
    return raw.decode("cp1252", errors="replace")


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    encoding = "auto"
    if "--enc" in sys.argv:
        encoding = sys.argv[sys.argv.index("--enc") + 1]

    cmd = sys.argv[1]

    if cmd == "info":
        t = load(sys.argv[2], encoding)
        print("%s  count=%d hash_size=%d version=%d file_size=%d(自证:与真实长度一致)"
              % (os.path.basename(sys.argv[2]), t.count, t.hash_size, t.version, t.file_size))
        return 0

    if cmd == "get":
        t = load(sys.argv[2], encoding)
        for a in sys.argv[3:]:
            if a.startswith("--"):
                break
            i = int(a)
            print("%d\t%s" % (i, t.get(i) if t.get(i) is not None else "(miss)"))
        return 0

    if cmd == "dump":
        t = load(sys.argv[2], encoding)
        out = sys.argv[3]
        lo = int(sys.argv[sys.argv.index("--min") + 1]) if "--min" in sys.argv else 0
        hi = int(sys.argv[sys.argv.index("--max") + 1]) if "--max" in sys.argv else 1 << 30
        n = 0
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            for i, s in t.items():
                if i < lo or i > hi:
                    continue
                f.write("%d\t%s\n" % (i, s.replace("\n", "\\n")))
                n += 1
        print("DUMP %s → %s（%d 条，编码 %s）" % (os.path.basename(sys.argv[2]), out, n, encoding))
        return 0

    if cmd == "set":
        base = load(sys.argv[2], encoding)
        out = sys.argv[3]
        p = load(sys.argv[sys.argv.index("--patch") + 1], encoding) if "--patch" in sys.argv else None
        e = load(sys.argv[sys.argv.index("--exp") + 1], encoding) if "--exp" in sys.argv else None
        s = TblSet(base, p, e)
        n = 0
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            for i in range(base.count):
                t = s.get(i)
                if t is None:
                    continue
                f.write("%d\t%s\n" % (i, t.replace("\n", "\\n")))
                n += 1
        print("SET  %s(+patch+exp) → %s（%d 条）" % (os.path.basename(sys.argv[2]), out, n))
        return 0

    print("未知子命令：%s" % cmd)
    return 1


if __name__ == "__main__":
    sys.exit(main())
