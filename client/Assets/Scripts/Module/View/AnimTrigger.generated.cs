// -----------------------------------------------------------------------------
// Diablo2 · Module/View/AnimTrigger.generated.cs
// 生成物：官方 `AnimData.d2`（`D2data.mpq` 的 `data\global\animdata.d2`）A1 动作的
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
            { "AE:A1:17", 72 },
            { "AM:A1:13", 81 },
            { "AM:A1:14", 62 },
            { "AM:A1:15", 91 },
            { "AM:A1:16", 101 },
            { "AM:A1:18", 111 },
            { "AM:A1:20", 121 },
            { "AN:A1:16", 91 },
            { "BA:A1:12", 61 },
            { "BA:A1:15", 72 },
            { "BA:A1:16", 71 },
            { "BA:A1:18", 81 },
            { "BA:A1:19", 91 },
            { "BA:A1:20", 102 },
            { "BB:A1:12", 61 },
            { "BH:A1:8", 31 },
            { "BK:A1:11", 61 },
            { "BT:A1:10", 51 },
            { "BU:A1:12", 71 },
            { "CR:A1:13", 52 },
            { "CR:A1:16", 101 },
            { "CR:A1:18", 111 },
            { "CS:A1:16", 91 },
            { "DI:A1:16", 91 },
            { "DM:A1:16", 111 },
            { "DU:A1:10", 61 },
            { "EC:A1:19", 91 },
            { "FA:A1:10", 71 },
            { "FC:A1:15", 92 },
            { "FD:A1:15", 81 },
            { "FE:A1:12", 81 },
            { "FK:A1:12", 81 },
            { "FR:A1:14", 51 },
            { "FS:A1:17", 81 },
            { "FW:A1:25", 92 },
            { "G1:A1:16", 91 },
            { "G2:A1:16", 101 },
            { "G3:A1:17", 101 },
            { "G4:A1:16", 101 },
            { "GM:A1:20", 121 },
            { "GT:A1:5", 2 },
            { "GU:A1:16", 111 },
            { "GY:A1:16", 91 },
            { "GZ:A1:16", 101 },
            { "HP:A1:10", 51 },
            { "HX:A1:12", 82 },
            { "HY:A1:12", 82 },
            { "HZ:A1:12", 82 },
            { "IW:A1:15", 61 },
            { "K9:A1:2", 12 },
            { "M4:A1:17", 31 },
            { "M6:A1:37", 191 },
            { "MM:A1:18", 91 },
            { "MO:A1:12", 71 },
            { "MP:A1:18", 61 },
            { "NE:A1:15", 81 },
            { "NE:A1:18", 92 },
            { "NE:A1:19", 91 },
            { "NE:A1:20", 111 },
            { "NE:A1:23", 111 },
            { "NE:A1:24", 101 },
            { "PA:A1:14", 71 },
            { "PA:A1:15", 71 },
            { "PA:A1:16", 82 },
            { "PA:A1:17", 81 },
            { "PA:A1:18", 81 },
            { "PA:A1:20", 81 },
            { "PN:A1:15", 81 },
            { "PW:A1:22", 161 },
            { "RD:A1:16", 91 },
            { "RG:A1:15", 62 },
            { "S7:A1:16", 131 },
            { "SB:A1:18", 121 },
            { "SC:A1:16", 81 },
            { "SD:A1:14", 61 },
            { "SI:A1:16", 111 },
            { "SK:A1:16", 101 },
            { "SL:A1:20", 71 },
            { "SM:A1:18", 121 },
            { "SO:A1:16", 91 },
            { "SO:A1:17", 92 },
            { "SO:A1:18", 111 },
            { "SO:A1:19", 111 },
            { "SO:A1:20", 121 },
            { "SO:A1:23", 131 },
            { "SO:A1:24", 141 },
            { "SP:A1:11", 71 },
            { "SR:A1:15", 71 },
            { "ST:A1:12", 71 },
            { "SW:A1:20", 61 },
            { "TE:A1:15", 12 },
            { "TH:A1:10", 61 },
            { "TN:A1:20", 131 },
            { "UM:A1:16", 101 },
            { "VA:A1:14", 91 },
            { "VC:A1:10", 61 },
            { "VD:A1:12", 51 },
            { "VM:A1:17", 81 },
            { "WR:A1:18", 121 },
            { "WW:A1:10", 51 },
            { "YE:A1:12", 51 },
            { "ZM:A1:16", 91 },
            { "ZP:A1:14", 71 },
            { "ZZ:A1:15", 101 },
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
