// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Map/MapGenDeco.cs
// **本文件是生成物，禁止手改**（生成器：`tools/d2codec/export_deco.py`）。
//
// 原版 DS1 `objects` 层里 **kind=2（物件预设单位）** 的物件类表。
// `id` = 该幕 objpreset 表的下标（**不是 `Objects.txt` 的 Id**）；经典 1.10 的包里
// 没有 `ObjPreset.txt`，本表用 D2R 的官方数据表（`Index`/`Act`/`ObjectClass`）：
//   https://raw.githubusercontent.com/pinkufairy/D2R-Excel/main/base/objpreset.txt
// 替换依据（三条，详见生成器文件头 ①）：Index 52 = Waypoint 落在五芒星台；
//   D2R objects.txt 的 Class→Id/Token 与经典逐行相等；Index 6..11 = Stone 1..6 同序。
//
// 每个物件类的**帧数与帧率都读原版 `Objects.txt`**（不是本项目的数字）：
//   · 模式 = 该物件第一个「FrameCnt>1 且 CycleAnim=1」的模式（自带的那段循环动画）；
//     没有任何循环模式 ⇒ 静态，取模式 0 第 0 帧。
//   · 帧率 = `25 × FrameDelta / 256`（换算出处同 `ResPaths.WaypointFrameFps`）。
//   逐条的 Id/Token/模式/帧数/帧率/依据 = `Resources/Clover/D2/Objects/deco-manifest.json`。
//
// 帧贴图：`Resources/Clover/D2/Objects/<Dir>/<帧号三位>.png`
//   （键由 `ResPaths.DecoFrame(Dir, i)` 拼，⛔ 不许在别处手拼路径）。
// ─────────────────────────────────────────────────────────────────────────────
using UnityEngine;

namespace Diablo2.Module.Map
{
    /// <summary>原版 kind=2 物件的类表（生成物，见文件头）。</summary>
    internal static class MapGenDeco
    {
        /// <summary>一个物件类（= 一个 ds1 id 在原版 objpreset 里的那一类）。</summary>
        public struct Kind
        {
            /// <summary>`Resources/Clover/D2/Objects/` 下的目录名（= 原版 Token 的小写）。</summary>
            public readonly string Dir;

            /// <summary>播放帧数（= 原版 `Objects.txt` 该模式列的 `FrameCnt`）。</summary>
            public readonly int Frames;

            /// <summary>帧率（= `25 × FrameDelta / 256`；`Frames <= 1` 时无意义）。</summary>
            public readonly float Fps;

            /// <summary>是否循环（= 原版 `CycleAnim`）。</summary>
            public readonly bool Loop;

            /// <summary>原版 `Objects.txt` 的 `Id`。</summary>
            public readonly int SrcId;

            /// <summary>原版 `Objects.txt` 的 `Token`。</summary>
            public readonly string SrcToken;

            /// <summary>采用的模式下标（0=NU / 1=OP / 2=ON）。</summary>
            public readonly int SrcModeIndex;

            /// <summary>ds1 `objects` 层的 kind=2 id（= objpreset 的 `Index`）。</summary>
            public readonly int Ds1Id;

            /// <summary>objpreset 的 `ObjectClass`。</summary>
            public readonly string ClassName;

            /// <summary>构造（生成物内部用；字段顺序 = 上面声明的顺序）。</summary>
            public Kind(string dir, int frames, float fps, bool loop, int srcId, string srcToken,
                int srcModeIndex, int ds1Id, string className)
            {
                Dir = dir;
                Frames = frames;
                Fps = fps;
                Loop = loop;
                SrcId = srcId;
                SrcToken = srcToken;
                SrcModeIndex = srcModeIndex;
                Ds1Id = ds1Id;
                ClassName = className;
            }
        }

        /// <summary>下标 = ds1 `objects` 层 kind=2 单位要画的物件类（`-1` = 不画）。</summary>
        public static readonly Kind[] Kinds =
        {
            new Kind("to", 20, 19.53125f, true, 37, "TO", 2, 1, "TikiTorch1"),
            new Kind("rb", 10, 12.50000f, true, 39, "RB", 0, 2, "RogueBonfire"),
            new Kind("n1", 10, 12.50000f, true, 35, "N1", 0, 3, "Standard1"),
            new Kind("n2", 10, 12.50000f, true, 36, "N2", 0, 4, "Standard2"),
            new Kind("b6", 1, 0f, false, 267, "b6", 0, 102, "Bank"),
        };

        /// <summary>`ds1 id` → <see cref="Kinds"/> 下标（`-1` = 这类物件原版不画 / 未登记）。</summary>
        public static int IndexOf(int ds1Id)
        {
            for (var i = 0; i < Kinds.Length; i++)
            {
                if (Kinds[i].Ds1Id == ds1Id) return i;
            }
            return -1;
        }
    }
}
