# -*- coding: utf-8 -*-
"""Export official AnimData.d2 animation speeds -> AnimRate.generated.cs

AnimData.d2 record layout (byte-verified, see parse_records below):
    +0  char[8]   COF name, e.g. b"AMNUHTH\\x00"  (unit 2 chars + mode 2 chars + weapon class)
    +8  uint32    frames per direction
    +12 uint32    animation speed (256 == 1x)
Playback fps = 25 * speed / 256  (same scale as the official Objects.txt
FrameDelta conversion already used by ResPaths.WaypointFrameFps).

Usage:
    python export_animdata.py                  # read data\\global\\animdata.d2 out of D2data.mpq
    python export_animdata.py --in <file>      # reuse an already extracted dump
"""

import os
import sys
import struct
import collections

REPO = r'C:\Work\Server\f-v2\clover-project-diablo2'
OUT = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'View', 'AnimRate.generated.cs')
MPQ = os.path.join(REPO, u'\u539f\u7248\u8d44\u6e90', '_mpq_incoming', 'D2data.mpq')
ARCHIVE_PATH = r'data\global\animdata.d2'

# modes this project plays (ViewAnim -> official AnimData mode code)
MODES = ['NU', 'WL', 'RN', 'A1', 'SC', 'GH', 'DT']
NORMAL = 256


def read_bytes(inpath):
    if inpath:
        with open(inpath, 'rb') as fh:
            return fh.read()
    sys.path.insert(0, os.path.join(REPO, 'tools', 'd2codec'))
    import storm
    storm.set_work_dir(os.path.join(REPO, '.ai-tmp', 'test', 'storm-tmp'))
    handle = storm.open_archive(MPQ)
    raw = storm.read_file(handle, ARCHIVE_PATH)
    storm.close_archive(handle)
    return raw


def parse_records(raw):
    """(unit2, mode) -> Counter(speed).  Name fields are 7 printable chars + NUL."""
    recs = {}
    end = len(raw) - 16
    off = 0
    while off <= end:
        block = raw[off:off + 8]
        if block[-1] == 0 and block[0:1].isalpha() and all(33 <= c < 127 for c in block[:7]):
            name = block.split(b'\x00')[0].decode('ascii')
            mode = name[2:4]
            if len(name) >= 4 and mode in MODES:
                speed = struct.unpack_from('<I', raw, off + 12)[0]
                recs.setdefault((name[:2], mode), collections.Counter())[speed] += 1
        off += 1
    return recs


def pick(speeds):
    """Most frequent speed; ties go to the smaller value (deterministic)."""
    return sorted(speeds.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]


def render(recs):
    rows = []
    for (unit, mode) in sorted(recs):
        rows.append('            { "%s%s", %d },' % (unit, mode, pick(recs[(unit, mode)])))
    return '\n'.join(rows)


def main():
    inpath = sys.argv[sys.argv.index('--in') + 1] if '--in' in sys.argv else None
    raw = read_bytes(inpath)
    if not raw:
        print('animdata.d2 not readable (from %s)' % (inpath or MPQ))
        return 1
    recs = parse_records(raw)
    print('animdata bytes = %d, unit/mode pairs = %d' % (len(raw), len(recs)))

    body = '''// -----------------------------------------------------------------------------
// Diablo2 \xb7 Module/View/AnimRate.generated.cs
// \u751f\u6210\u7269\uff1a\u5b98\u65b9 `AnimData.d2`\uff08`D2data.mpq` \u7684 `data\\global\\animdata.d2`\uff09\u7684
// **\u9010\u5355\u4f4d \xd7 \u9010\u52a8\u4f5c** animation speed\uff1b\u751f\u6210\u5668 = `tools/d2codec/export_animdata.py`
// \uff08\u21d4 \u624b\u6539\u4f1a\u88ab\u4e0b\u6b21\u5bfc\u51fa\u8986\u76d6\uff09\u3002
//
// \u8bb0\u5f55\u5e03\u5c40\uff08\u9010\u5b57\u8282\u6838\u5bf9\uff09\uff1a
//     +0  char[8]  COF \u540d\uff08\u5355\u4f4d 2 \u5b57 + \u52a8\u4f5c 2 \u5b57 + \u6b66\u5668\u7c7b\uff09\uff0c\u4f8b `AMNUHTH\\0`\uff1b
//     +8  uint32   framesPerDirection\uff1b+12 uint32  animation speed\uff08256 = 1\xd7\uff09\u3002
// \u64ad\u653e\u5e27\u7387 = `25 \xd7 speed / 256`\uff08\u4e0e `ResPaths.WaypointFrameFps`
// = `25 \xd7 Objects.txt.FrameDelta / 256` \u540c\u4e00\u628a\u5c3a\uff09\u3002
//
// \u540c\u4e00\u5355\u4f4d\u7684\u9010\u6b66\u5668\u7c7b\u53d8\u4f53 speed \u4e0d\u540c\u65f6\u53d6\u4f17\u6570\uff0c\u5e73\u5c40\u53d6\u8f83\u5c0f\u8005
// \uff08\u786e\u5b9a\u6027\u53e3\u5f84\uff0c\u540c\u4e00\u4efd\u8868\u6c38\u8fdc\u5bfc\u51fa\u540c\u4e00\u7ed3\u679c\uff09\u3002
// -----------------------------------------------------------------------------

using System.Collections.Generic;

namespace Diablo2.Module.View
{
    /// <summary>\u5b98\u65b9 `AnimData.d2` \u7684\u9010\u5355\u4f4d\u9010\u52a8\u4f5c animation speed\uff08\u751f\u6210\u7269\uff0c\u89c1\u6587\u4ef6\u5934\uff09\u3002</summary>
    public static class AnimRate
    {
        /// <summary>1\xd7 \u901f\u5ea6\uff08\u5373 fps = \u57fa\u51c6\u5e27\u7387\uff09\u3002\u7f3a\u8bb0\u5f55\u65f6\u6309\u5b83\u7b97\u3002</summary>
        public const int NormalSpeed = %d;

        private static readonly Dictionary<string, int> ByUnitMode = new Dictionary<string, int>
        {
%s
        };

        /// <summary>
        /// \u67d0\u5355\u4f4d\u67d0\u52a8\u4f5c\u7684\u5b98\u65b9 speed\uff08\u952e = 2 \u5b57\u5355\u4f4d\u4ee3\u53f7 + 2 \u5b57\u52a8\u4f5c\u4ee3\u53f7\uff0c\u4f8b `AM` + `NU`\uff09\u3002
        /// <para>\u672a\u767b\u8bb0\uff08\u539f\u7248\u6ca1\u6709\u8be5\u5355\u4f4d\u8be5\u52a8\u4f5c\uff09\u21d2 \u8fd4\u56de <see cref="NormalSpeed"/>\u3002</para>
        /// </summary>
        public static int For(string unitCode, string mode)
        {
            if (string.IsNullOrEmpty(unitCode) || string.IsNullOrEmpty(mode)) return NormalSpeed;
            int speed;
            return ByUnitMode.TryGetValue(unitCode + mode, out speed) ? speed : NormalSpeed;
        }
    }
}
''' % (NORMAL, render(recs))

    with open(OUT, 'wb') as fh:
        fh.write(body.encode('utf-8'))
    print('wrote %s (%d entries)' % (OUT, len(recs)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
