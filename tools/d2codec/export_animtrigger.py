# -*- coding: utf-8 -*-
"""Export official AnimData.d2 A1 trigger frames -> AnimTrigger.generated.cs

Record layout (byte-verified against Diablerie Engine/IO/D2Formats/AnimData.cs):
    +0  char[8]   COF name, e.g. b"ZMA1HTH\\x00"  (unit 2 + mode 2 + weapon class)
    +8  uint32    framesPerDirection
    +12 uint32    animation speed (256 == 1x)
    +16 byte[144] trigger flags: a non-zero byte at flat index k means
                  "frame k of this COF fires the action event";
                  value 1 = melee contact, value 2 = missile release.
Verified on the real D2data.mpq: every A1 record of the 13 units this project
plays (ZM/FA/FS/SI/CR/BK/YE/WR + 5 classes) carries exactly one non-zero byte,
position < framesPerDirection, and BOW/XBW records are exactly the value-2 ones.

Usage:
    python export_animtrigger.py                  # read data\\global\\animdata.d2 out of D2data.mpq
    python export_animtrigger.py --in <file>      # reuse an already extracted dump
"""

import os
import sys
import struct
import collections

REPO = r'C:\Work\Server\f-v2\clover-project-diablo2'
OUT = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'View', 'AnimTrigger.generated.cs')
MPQ = os.path.join(REPO, u'原版资源', '_mpq_incoming', 'D2data.mpq')
ARCHIVE_PATH = r'data\global\animdata.d2'

# modes whose trigger frames this project consumes (A1 = basic attack contact frame)
MODES = ('A1',)


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
    """name -> Counter((frames, trigger, event)). Byte-scan, same shape as export_animdata.py."""
    recs = {}
    end = len(raw) - 160
    off = 0
    while off <= end:
        block = raw[off:off + 8]
        if block[-1] == 0 and block[0:1].isalpha() and all(33 <= c < 127 for c in block[:7]):
            name = block.split(b'\x00')[0].decode('ascii')
            if len(name) >= 4 and name[2:4] in MODES:
                frames = struct.unpack_from('<I', raw, off + 8)[0]
                flags = raw[off + 16:off + 160]
                nz = [i for i, b in enumerate(flags) if b != 0]
                if nz and 0 < frames <= 64:
                    trig = nz[0]
                    evt = flags[trig]
                    if trig < frames and evt in (1, 2):
                        recs.setdefault(name, collections.Counter())[(frames, trig, evt)] += 1
        off += 1
    return recs


def main():
    inpath = sys.argv[sys.argv.index('--in') + 1] if '--in' in sys.argv else None
    raw = read_bytes(inpath)
    if not raw:
        print('animdata.d2 not readable (from %s)' % (inpath or MPQ))
        return 1
    recs = parse_records(raw)
    print('animdata bytes = %d, A1 records with trigger = %d' % (len(raw), len(recs)))

    # per COF name: most frequent (frames, trig, evt); ties -> smaller tuple (deterministic)
    per_cof = {}
    for name, counter in recs.items():
        per_cof[name] = sorted(counter.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]

    # collapse to (unit, mode, frames): prefer event 1 (melee contact), then more votes, then smaller trig
    best = {}
    for name, (frames, trig, evt) in sorted(per_cof.items()):
        key = (name[:2], name[2:4], frames)
        cand = (evt != 1, -recs[name][(frames, trig, evt)], trig)
        if key not in best or cand < best[key][0]:
            best[key] = (cand, trig, evt)

    rows = []
    for (unit, mode, frames) in sorted(best):
        _, trig, evt = best[(unit, mode, frames)]
        rows.append((unit, mode, frames, trig, evt))

    body = '''// -----------------------------------------------------------------------------
// Diablo2 · Module/View/AnimTrigger.generated.cs
// 生成物：官方 `AnimData.d2`（`D2data.mpq` 的 `data\\global\\animdata.d2`）A1 动作的
// **接触帧**（trigger frame）；生成器 = `tools/d2codec/export_animtrigger.py`
// （⇔ 手改会被下次导出覆盖）。
//
// 记录布局（逐字节核对，见生成器文件头）：
//     +0 char[8] COF 名；+8 uint32 framesPerDirection；+12 uint32 speed；
//     +16 byte[144] 触发标记 —— 平直下标 k 处的非零字节 = "第 k 帧触发动作事件"，
//     值 1 = 近战接触、值 2 = 投射物出手。
// 键 = 单位 2 字 + 动作 2 字 + 帧数（同一单位同一动作的逐武器类变体帧数不同 ⇒
// 用"实际播放的帧数"定位到同一条 COF 记录）；值 = 触发帧 × 10 + 事件值。
// -----------------------------------------------------------------------------
using System.Collections.Generic;

namespace Diablo2.Module.View
{
    /// <summary>官方 `AnimData.d2` 的 A1 接触帧表（生成物，见文件头）。</summary>
    public static class AnimTrigger
    {
        private static readonly Dictionary<string, int> ByKey = new Dictionary<string, int>
        {
__ROWS__
        };

        /// <summary>
        /// 某单位某动作、按<paramref name="frames"/>帧播放时的**官方接触帧**（帧下标，0 起）；
        /// 官方表里没有这一条 ⇒ -1。
        /// </summary>
        public static int FrameOf(string unit, string mode, int frames)
        {
            if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(mode)) return -1;
            int v;
            return ByKey.TryGetValue(unit + ":" + mode + ":" + frames, out v) ? v / 10 : -1;
        }

        /// <summary>
        /// 帧数对不上时的折算口径：同单位同动作里挑"事件为近战接触优先、帧数最接近
        /// <paramref name="framesHint"/> 的那条记录，返回 触发帧 ÷ 帧数（0~1）；没有 ⇒ -1。
        /// </summary>
        public static float FractionOf(string unit, string mode, int framesHint)
        {
            if (string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(mode)) return -1f;
            var head = unit + ":" + mode + ":";
            var bestRank = int.MaxValue;
            var bestDelta = int.MaxValue;
            var bestFrames = int.MaxValue;
            var bestTrig = int.MaxValue;
            var found = false;
            foreach (var kv in ByKey)
            {
                var k = kv.Key;
                if (!k.StartsWith(head, System.StringComparison.Ordinal)) continue;
                int frames;
                if (!int.TryParse(k.Substring(head.Length), out frames) || frames <= 0) continue;
                var trig = kv.Value / 10;
                var evt = kv.Value % 10;
                var rank = evt == 1 ? 0 : 1;
                var delta = frames > framesHint ? frames - framesHint : framesHint - frames;
                if (rank > bestRank) continue;
                if (rank == bestRank && delta > bestDelta) continue;
                if (rank == bestRank && delta == bestDelta && frames > bestFrames) continue;
                if (rank == bestRank && delta == bestDelta && frames == bestFrames && trig >= bestTrig) continue;
                bestRank = rank; bestDelta = delta; bestFrames = frames; bestTrig = trig;
                found = true;
            }
            return found ? (float)bestTrig / bestFrames : -1f;
        }
    }
}
'''
    lines = []
    for unit, mode, frames, trig, evt in rows:
        lines.append('            { "%s:%s:%d", %d },' % (unit, mode, frames, trig * 10 + evt))
    with open(OUT, 'wb') as fh:
        fh.write(body.replace('__ROWS__', '\n'.join(lines)).encode('utf-8'))
    print('wrote %s (%d rows)' % (OUT, len(rows)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
