// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/SpriteFrames.cs
// 逐帧动画的**帧键 + 贴图解析**层（素材唯一的出入口）。
//
// 帧键命名：`{动作}_{方向}_{帧号}`，例：`walk_s_0`、`attack_ne_3`、`death_s_7`
//   方向：s / sw / w / nw / n / ne / e / se（与 `Def.Dir8` 同名小写，**序号沿用暗黑2**）
// 目录：
//   角色 → `Core.ResPaths.CharDir(PlayerClass)`  = `D2/Chars/{class}/`（class = amazon…）
//   怪物 → `Core.ResPaths.MonsterDir(code)`       = `D2/Monsters/{code}/`
//         （code = `monster_c.sprite` 列，官方 `MonStats.Code` 的 DCC 前缀，如 `fa`/`zm`/`si`；
//           NPC 用 `NpcSpriteCode`：阿卡拉 ps / 卡夏 rc / 恰西 ci / 基德 gh / 瓦瑞夫 wa）
//
// 帧数：**真实值**来自原版 `.cof` 的 `framesPerDirection` —— 生成物
//    `Module/View/SpriteFrameCounts.cs`（导出器 `tools/d2codec/export_chars.py --emit-cs`）。
//    原版**每个单位的每个动作帧数都不同**（例：亚马逊 idle=8、attack=13、cast=20、death=23；
//       堕落者 idle=20、attack=10），所以不能再用一份全局常量（那会让帧键指向不存在的图 = 静默缺图）。
//    `FrameCounts` 保留为**未登记单位的兜底**（值 = 亚马逊的真实帧数，默认职业）。
//
// 像素尺度（"不许浮空/大小不对"的关键）：原版 D2 是 **80 像素 = 1 世界单位**
//    （Diablerie `Iso.cs:9` `pixelsPerUnit = 80`；本项目地形也用同一尺度，见
//     `Module/Map/MapView.cs` 的 `D2TilePixelsPerUnit = 80f` 与节点缩放 64/80）。
//    而本项目契约 `GameConst.PixelsPerUnit = 64` 是**导入** PPU ⇒ 实体节点必须再乘
//    `ArtScale = 64/80 = 0.8`，否则角色比地形大 25%（一样"看着不对"）。见 `ArtScale`。
//
// 素材缺失策略（`tools/ai-skill/conventions.md` §素材规则）：
//   **纯色占位 sprite**（按类型上色，pivot = 脚底中心 ⇒ 与 `conventions.md` §坐标的
//   "角色锚点 = 脚底中心"一致），并登记 `client/资源欠缺清单.md`。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（语义与序号和改动前**完全一致**）。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>帧键生成 + 贴图解析（缓存 / 异步加载 / 纯色占位）。</summary>
    public static class SpriteFrames
    {
        /// <summary>
        /// **未登记单位的兜底帧数**（下标 = <see cref="ViewAnim"/>）。
        /// <para>值 = 亚马逊（默认职业）的**真实** `.cof` 帧数（`SpriteFrameCounts` 里同一行）：
        /// idle=8 / walk=8 / attack=13 / cast=20 / hit=6 / death=23 / **run=8**。
        /// 已登记的单位一律走 <see cref="SpriteFrameCounts.Of"/>（逐单位真实值）。</para>
        /// <para>保留这个公开数组是**契约**：离线宿主 `tools/probes/hosts/combatcheck/Program.cs` 会读它。</para>
        /// </summary>
        public static readonly int[] FrameCounts =
        {
            8,   // Idle   （原版 NU，亚马逊）
            8,   // Walk   （原版 WL，亚马逊）
            13,  // Attack （原版 A1，亚马逊）
            20,  // Cast   （原版 SC，亚马逊）
            6,   // Hit    （原版 GH，亚马逊）
            23,  // Death  （原版 DT，亚马逊）
            8,   // Run    （原版 RN，亚马逊；**必须与 ViewAnim.Run = 6 同下标**）
        };

        /// <summary>动作名（帧键前缀，下标 = <see cref="ViewAnim"/>）—— 与导出器同一张表。</summary>
        private static readonly string[] ActionKeys = { "idle", "walk", "attack", "cast", "hit", "death", "run" };

        /// <summary>
        /// 缺动作时的**回退链**（原版有的动作才可能没有：NPC 只有 NU/WL；
        /// 多数怪物没有 SC；`cast` 回退到攻击、`death` 回退到 idle 都是"用原版动画顶上"，
        /// **绝不用占位色块**）。下标 = <see cref="ViewAnim"/>。
        /// <para>`Run` 一条：**原版只有一部分单位有 RN**
        /// （5 个职业 + `zm`/`cr`，见 `ViewAnim.Run` 注释）⇒ 请求 Run 而该单位没有时
        /// 回退到 **Walk**（同一单位的原版移动动画；退回 Idle 会让"跑"变成站着不动，更糟）。</para>
        /// </summary>
        private static readonly ViewAnim[][] FallbackChain =
        {
            new[] { ViewAnim.Idle },                                   // Idle
            new[] { ViewAnim.Walk, ViewAnim.Idle },                    // Walk
            new[] { ViewAnim.Attack, ViewAnim.Idle },                  // Attack
            new[] { ViewAnim.Cast, ViewAnim.Attack, ViewAnim.Idle },   // Cast
            new[] { ViewAnim.Hit, ViewAnim.Idle },                     // Hit
            new[] { ViewAnim.Death, ViewAnim.Hit, ViewAnim.Idle },     // Death
            new[] { ViewAnim.Run, ViewAnim.Walk, ViewAnim.Idle },      // Run（RN）
        };

        /// <summary>
        /// 原版 D2 的像素尺度：**80 像素 = 1 世界单位**（出处 Diablerie `Iso.cs:9`
        /// `pixelsPerUnit = 80`；与 `Module/Map/MapView.cs` 的 `D2TilePixelsPerUnit` 同值）。
        /// </summary>
        public const float ArtPixelsPerUnit = 80f;

        /// <summary>
        /// 实体节点的**像素尺度换算系数** = `GameConst.PixelsPerUnit / ArtPixelsPerUnit` = 0.8。
        /// <para>本项目的贴图按 PPU=64 导入（契约），而原版单位是 80 px/单位 ⇒ 节点乘 0.8 后
        /// 像素尺度与原版地形一致（`MapView` 对地形瓦片做的是同一件事）。</para>
        /// </summary>
        public const float ArtScale = GameConst.PixelsPerUnit / ArtPixelsPerUnit;

        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> Pending = new HashSet<string>();
        private static readonly HashSet<string> FallbackLogged = new HashSet<string>();

        /// <summary>
        /// 「加载失败后的退避复活」时刻表（键 → 允许再次尝试的 `Time.time`）。
        /// <para>◆ 为什么必须有它：贴图是**异步**加载的，
        /// 若第一次 `Resolve` 发生在 Unity **还没导完资源**的时刻（实测：8 千多张 PNG 刚落盘、
        /// **这一帧的图永远不会再取**，怪物/NPC 就一直停在纯色占位块上（而且只报一条日志）。
        /// 贴图到位后的全量重铺）就自动重试 ⇒ 导入完成后自愈。</para>
        /// </summary>
        private static readonly Dictionary<string, float> RetryAfter = new Dictionary<string, float>();

        /// <summary>加载失败后的重试间隔（秒）。</summary>
        private const float RetrySeconds = 2f;
        private static bool _repaintRequested;
        private static Sprite _placeholder;

        /// <summary>是否已经有新的贴图异步到位（调用方据此重铺一次，别每帧重铺）。</summary>
        public static bool ConsumeRepaintRequest()
        {
            if (!_repaintRequested) return false;
            _repaintRequested = false;
            return true;
        }

        /// <summary>
        /// 官方 `charstats.txt` 的**规范帧数**（键 = 单位代号 + 动作代号，逐单位 × 逐动作）。
        /// <para>出处：上游参考工程 `Diablerie/Engine/IO/D2Formats/AnimData.cs:17-32` 的
        /// `referenceFrameCount` 表（原注 `values from charstats.txt`：
        /// `AMWL = 6` / `AMRN = 4` / `SOWL = 8` / `SORN = 5` / `NEWL = 9` / `NERN = 5` /
        /// `PAWL = 8` / `PARN = 5` / `BAWL = 7` / `BARN = 4` / `DZWL = 9` / `DZRN = 5` /
        /// `AIWL = 6` / `AIRN = 4`）；三个职业代号 `DZ` / `AI` 是原版扩展包的德鲁伊 / 刺客，
        /// 本项目没有这两个职业，保留为完整搬运。</para>
        /// </summary>
        private static readonly Dictionary<string, int> RefFrameCountByUnitMode = new Dictionary<string, int>
        {
            { "AMWL", 6 }, { "AMRN", 4 },
            { "SOWL", 8 }, { "SORN", 5 },
            { "NEWL", 9 }, { "NERN", 5 },
            { "PAWL", 8 }, { "PARN", 5 },
            { "BAWL", 7 }, { "BARN", 4 },
            { "DZWL", 9 }, { "DZRN", 5 },
            { "AIWL", 6 }, { "AIRN", 4 },
        };

        /// <summary>
        /// 某单位某动作的**规范帧数**（官方 `charstats.txt`，见 <see cref="RefFrameCountByUnitMode"/>）。
        /// <para>官方表里没有这个单位 / 这个动作 ⇒ 返回**实际帧数** ⇒ 修正系数 = 1，
        /// 与上游 `AnimData.GetCorrectedFrameDuration` 的
        /// `referenceFrameCount.GetValueOrDefault(token + mode, framesPerDir)` 同一口径。</para>
        /// </summary>
        public static int RefFrameCountOf(string unitKey, ViewAnim anim)
        {
            var frames = FrameCountOf(unitKey, anim);
            if (string.IsNullOrEmpty(unitKey) || unitKey.Length < 2) return frames;
            int refFrames;
            var key = unitKey.Substring(0, 2).ToUpperInvariant() + ModeCodeOf(anim);
            return RefFrameCountByUnitMode.TryGetValue(key, out refFrames) ? refFrames : frames;
        }

        /// <summary>
        /// 某单位某动作的**原版播放帧率** = `AnimBaseFps × speed / 256 × 规范帧数 / 实际帧数`。
        /// <para>口径唯一来源 = 上游参考工程 `Diablerie/Engine/IO/D2Formats/AnimData.cs`
        /// （`https://cdn.jsdelivr.net/gh/mofr/Diablerie@master/Assets/Scripts/Diablerie/Engine/IO/D2Formats/AnimData.cs`）
        /// 的 `GetCorrectedFrameDuration`（:34-38）取其倒数：
        /// `frameDuration = 256.0f / 25.0f / speed`（:92）再 `× framesPerDir / refFrameCount`。
        /// **式子里没有移动速度** —— 走 / 跑各自一套独立帧率，与角色跑多快无关。</para>
        /// <para>官方表里**没有**这个单位 / 这个动作 ⇒ 规范帧数 = 实际帧数
        /// ⇒ 等同 <see cref="FpsOf(string, ViewAnim)"/>（25 × speed / 256）。</para>
        /// </summary>
        public static float CorrectedFpsOf(string unitKey, ViewAnim anim)
        {
            var frames = FrameCountOf(unitKey, anim);
            if (frames <= 0) return FpsOf(unitKey, anim);
            return FpsOf(unitKey, anim) * RefFrameCountOf(unitKey, anim) / frames;
        }

        /// <summary>
        /// 某单位某动作的**官方 `AnimData.d2` 基准帧率** = `25 × animationSpeed / 256`
        /// （**未含**规范帧数修正 ⇒ 播放侧一律走 <see cref="CorrectedFpsOf"/>）。
        /// <para>出处：官方 `AnimData.d2` 的 `animationSpeed`（逐单位 × 逐动作 × 逐武器类，
        /// 生成物 <see cref="AnimRate"/>，生成器 `tools/d2codec/export_animdata.py`）；
        /// 换算口径同 <see cref="ResPaths.WaypointFrameFps"/>（官方 `Objects.txt` 的 `FrameDelta`
        /// 用的同一套 1/256 单位：`25 × 200 / 256 = 19.53125`）。</para>
        /// <para>官方表里**没有**这个单位 / 这个动作 ⇒ <see cref="AnimRate.For"/> 返回 1×
        /// （<see cref="AnimRate.NormalSpeed"/>）⇒ 25 fps = <see cref="GameConst.AnimBaseFps"/>。</para>
        /// </summary>
        public static float FpsOf(string unitKey, ViewAnim anim)
            => GameConst.AnimBaseFps * SpeedOf(unitKey, anim) / AnimRate.NormalSpeed;

        /// <summary>
        /// `ViewAnim` → 官方 `AnimData.d2` 的**动作代号**（COF 名的第 3~4 个字符）。
        /// <para>`NU` 待机 / `WL` 走 / `RN` 跑 / `A1` 攻击 / `SC` 施法 / `GH` 受击 / `DT` 死亡。</para>
        /// </summary>
        public static string ModeCodeOf(ViewAnim anim)
        {
            switch (anim)
            {
                case ViewAnim.Idle: return "NU";
                case ViewAnim.Walk: return "WL";
                case ViewAnim.Run: return "RN";
                case ViewAnim.Attack: return "A1";
                case ViewAnim.Cast: return "SC";
                case ViewAnim.Hit: return "GH";
                case ViewAnim.Death: return "DT";
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 该单位该动作的**官方** animation speed（`AnimRate` 由 `AnimData.d2` 生成）。
        /// <para>单位代号 = `unitKey` 前两字符的大写（职业 `amazon`→`AM`、怪物 `zm`→`ZM`、
        /// 装备套键 `amazon/equip/hax`→`AM`）⇒ 与官方 COF 名的命名同形。</para>
        /// <para>官方表里没有该单位该动作 ⇒ 返回 <see cref="AnimRate.NormalSpeed"/>（= 1×）。</para>
        /// </summary>
        public static int SpeedOf(string unitKey, ViewAnim anim)
        {
            if (string.IsNullOrEmpty(unitKey) || unitKey.Length < 2) return AnimRate.NormalSpeed;
            return AnimRate.For(unitKey.Substring(0, 2).ToUpperInvariant(), ModeCodeOf(anim));
        }

        /// <summary>
        /// 该单位该动作的**官方时长（秒）** = 帧数 ÷ 播放帧率
        /// （`LookupCount` 的真实逐单位帧数 ÷ <see cref="CorrectedFpsOf"/>）。
        /// <para>这是"一个动作占多长时间"的**唯一口径** —— 施法/受击/出手这些"动作占满的时间"
        /// 一律由它给（原版一个动作模式的生命周期就是它的动画时长）。</para>
        /// <para>官方表里**没有**这个单位这个动作（帧数 0）⇒ 返回 0（调用方自己决定兜底，
        /// ⛔ 不许在这里编一个"差不多"的时长）。</para>
        /// </summary>
        public static float AnimSecondsOf(string unitKey, ViewAnim anim)
        {
            var frames = LookupCount(unitKey, anim);
            if (frames <= 0) return 0f;
            var fps = CorrectedFpsOf(unitKey, anim);
            return fps > 0f ? frames / fps : 0f;
        }

        /// <summary>
        /// 该单位该动作的**接触帧时刻（秒）** = 官方 `AnimData.d2` 的 trigger frame ÷ 播放帧率。
        /// <para>出处：`AnimTrigger`（官方 `AnimData.d2` 记录 +16 起的触发标记：平直下标 k 的非零字节
        /// = "第 k 帧触发动作事件"，值 1 = 近战接触、值 2 = 投射物出手；生成器
        /// `tools/d2codec/export_animtrigger.py`）。键的第三维 = **实际播放的帧数**
        /// （同一单位逐武器类变体的 COF 帧数不同 ⇒ 用真实帧数定位到同一条记录）。</para>
        /// <para>帧数对不上（装备外观套与官方变体不完全同名）⇒ 用同单位同动作的最近记录按
        /// <see cref="AnimTrigger.FractionOf"/> 折算；表里整个没有 ⇒ 返回 0（调用方退化为
        /// "出手即结算"，并在这里留一次 Warn）。</para>
        /// </summary>
        public static float AttackContactSecondsOf(string unitKey, ViewAnim anim)
        {
            var frames = LookupCount(unitKey, anim);
            if (frames <= 0) return 0f;
            var unit = string.IsNullOrEmpty(unitKey) || unitKey.Length < 2
                ? string.Empty : unitKey.Substring(0, 2).ToUpperInvariant();
            var mode = ModeCodeOf(anim);

            var trig = AnimTrigger.FrameOf(unit, mode, frames);
            if (trig >= 0)
            {
                var fps = CorrectedFpsOf(unitKey, anim);
                if (fps > 0f) return trig / fps;
            }

            var frac = AnimTrigger.FractionOf(unit, mode, frames);
            if (frac > 0f)
            {
                var seconds = AnimSecondsOf(unitKey, anim);
                if (seconds > 0f) return frac * seconds;
            }

            ViewLog.WarnOnce("contact.notrigger." + unit + mode,
                $"AttackContactSecondsOf: AnimData 里没有 {unit} 的 {mode} 触发帧 ⇒ 该单位出手即结算");
            return 0f;
        }

        /// <summary>
        /// 动作是否**循环播放**：只有 `Idle` / `Walk` / `Run` 循环
        /// （三档都是周期动画：`NU` = 站着呼吸、`WL` / `RN` = 连续迈步）。
        /// <para> 修正（审计 `w3_anim_audit.tsv` 的「walk 循环 / attack 播完回 idle / death 停末帧」
        /// 三项判据）：`Attack`(A1) / `Cast`(SC) / `Hit`(GH) / `Death`(DT) 一律**单次播放**
        /// ① **保持时长 &gt; 动画时长**时动作会**自己重播**（一次出手看见"挥了第二刀"；
        ///    例：怪物 A1 11 帧 @12fps = 0.92s，而保持时长若小于它就播不完
        ///    —— 出手/受击的保持时长一律取 `AnimSecondsOf` 的同一个值）；
        /// ② `Hit` 若循环 ⇒ <see cref="SpriteAnimator.Finished"/> **永不为真**
        ///    ⇒ 没法用"受击动画播完"当保持结束条件（玩家受击动作因此只能出 1 帧，见 `ViewModule.TickPlayer`）。</para>
        /// <para>原版语义：一次出手 / 一次施法 / 一次受击 / 一次死亡各播一套动画，播完回到静止。</para>
        /// </summary>
        public static bool LoopOf(ViewAnim anim)
        {
            return anim == ViewAnim.Idle || anim == ViewAnim.Walk || anim == ViewAnim.Run;
        }

        /// <summary>角色的某个动作/方向的帧键数组（单位键 = 职业名小写，与 `CharDir` 同名）。</summary>
        public static string[] Keys(PlayerClass cls, ViewAnim anim, Dir8 dir)
        {
            var unitKey = cls.ToString().ToLowerInvariant();
            return Build(unitKey, ResPaths.CharDir(cls), anim, dir);
        }

        /// <summary>
        /// 角色**装备外观套**的某个动作/方向的帧键数组。
        /// <para>与上面那个重载**同形**，只多一个"用哪一套"的维度：目录 =
        /// <see cref="ResPaths.CharEquipDir"/>，单位键 = `"{class}/equip/{key}"`
        /// （见 <see cref="EquipVisual.UnitKeyOf"/>，也是生成物 <see cref="EquipFrameCounts.ByUnit"/> 的键）。
        /// 帧数来源 = `EquipFrameCounts`（由各套 `manifest.json` 生成）——
        /// **不能**沿用徒手套的帧数：同一职业装上武器后逐动作帧数会变（例 amazon attack 13 → 15）。</para>
        /// <para><paramref name="equipKey"/> 为 null / 空 ⇒ 等价于徒手（走上面那个重载），
        /// 这是 `EquipVisual.Candidates` 链尾的正常形态，不是异常。</para>
        /// </summary>
        public static string[] Keys(PlayerClass cls, string equipKey, ViewAnim anim, Dir8 dir)
        {
            if (string.IsNullOrEmpty(equipKey)) return Keys(cls, anim, dir);

            var unitKey = EquipVisual.UnitKeyOf(cls, equipKey);
            return Build(unitKey, ResPaths.CharEquipDir(cls, equipKey), anim, dir);
        }

        /// <summary>怪物的某个动作/方向的帧键数组（<paramref name="spriteCode"/> = `monster_c.sprite`）。</summary>
        public static string[] Keys(string spriteCode, ViewAnim anim, Dir8 dir)
        {
            if (string.IsNullOrEmpty(spriteCode))
            {
                ViewLog.WarnThrottled("frames.nocode",
                    "SpriteFrames.Keys: 怪物的 sprite 代码为空（monster_c.sprite 列）⇒ 只能用占位图");
                return Build("unknown", ResPaths.MonsterDir("unknown"), anim, dir);
            }

            var code = spriteCode.ToLowerInvariant();
            return Build(code, ResPaths.MonsterDir(code), anim, dir);
        }

        /// <summary>
        /// NPC 的精灵代码（= 原版 `MonStats.Code`，**不是**名字前两字母 —— 实测 MPQ 里
        /// 没有 `ak/ka/ch` 这三个目录；`Code` 列才是权威：Akara→PS / Kashya→RC / Charsi→CI /
        /// Gheed→GH / Warriv→WA）。出处：`d2data.mpq` 的 `data/global/excel/monstats.txt`。
        /// </summary>
        public static string NpcSpriteCode(NpcId id)
        {
            switch (id)
            {
                case NpcId.Akara: return "ps";
                case NpcId.Kashya: return "rc";
                case NpcId.Charsi: return "ci";
                case NpcId.Gheed: return "gh";
                case NpcId.Warriv: return "wa";
                default:
                    ViewLog.WarnThrottled("npc.code.unknown", $"NpcSpriteCode: 未登记 NPC {(int)id} ⇒ 用 unknown");
                    return "unknown";
            }
        }

        /// <summary>
        /// 某单位 / 某装备外观套的某动作**真实**帧数（0 = 该套没有这个动作的原版动画）。
        /// <para>两张生成物都查：**装备外观套**（`EquipFrameCounts`，键含 `/equip/`）与本来的
        /// **徒手/怪物**表（`SpriteFrameCounts`）。两张表的键空间不相交（前者含 `/equip/`），
        /// 所以顺序不影响结果 —— 先查装备表只是为了少一次字典查找。</para>
        /// </summary>
        private static int LookupCount(string unitKey, ViewAnim anim)
        {
            if (string.IsNullOrEmpty(unitKey)) return 0;
            var n = EquipFrameCounts.Of(unitKey, anim);
            if (n > 0) return n;
            return SpriteFrameCounts.Of(unitKey, anim);
        }

        /// <summary>某单位的某动作**真实**帧数（0 = 该单位没有这个动作的原版动画）。</summary>
        public static int FrameCountOf(string unitKey, ViewAnim anim)
        {
            var n = LookupCount(unitKey, anim);
            if (n > 0) return n;

            var i = (int)anim;
            if (i >= 0 && i < FrameCounts.Length) return FrameCounts[i];
            return 1;
        }

        /// <summary>
        /// 该单位**实际能播**的动作：本身没有就沿回退链找（见 <see cref="FallbackChain"/>）。
        /// 每条 (单位, 动作) 只报一次 Warning（避免刷屏）。
        /// </summary>
        public static ViewAnim ResolveAnim(string unitKey, ViewAnim anim)
        {
            if (LookupCount(unitKey, anim) > 0) return anim;

            var chain = (int)anim >= 0 && (int)anim < FallbackChain.Length ? FallbackChain[(int)anim] : null;
            if (chain != null)
            {
                for (var i = 0; i < chain.Length; i++)
                {
                    if (SpriteFrameCounts.Of(unitKey, chain[i]) <= 0) continue;
                    var key = unitKey + "/" + ActionKeys[(int)anim];
                    if (FallbackLogged.Add(key))
                    {
                        ViewLog.WarnOnce("frames.fallback." + key,
                            $"原版没有 {unitKey} 的 {ActionKeys[(int)anim]} 动画（其 .cof 无该模式）" +
                            $" ⇒ 回退到 {ActionKeys[(int)chain[i]]}（同一单位的原版动画，不是占位色块）");
                    }
                    return chain[i];
                }
            }

            // 连 idle 都没有（未登记单位）⇒ 交给 FrameCounts 兜底，帧键用请求的动作
            var fb = unitKey + "/" + ActionKeys[(int)anim];
            if (FallbackLogged.Add(fb))
            {
                ViewLog.WarnThrottled("frames.nofallback",
                    $"SpriteFrameCounts 里没有单位「{unitKey}」⇒ 用兜底帧数（亚马逊值）拼帧键");
            }
            return anim;
        }

        private static string[] Build(string unitKey, string dirPath, ViewAnim anim, Dir8 dir)
        {
            var real = ResolveAnim(unitKey, anim);
            var n = FrameCountOf(unitKey, real);
            if (n < 1) n = 1;

            var action = ActionKey(real);
            var d = dir.ToString().ToLowerInvariant();

            var keys = new string[n];
            for (var i = 0; i < n; i++) keys[i] = dirPath + action + "_" + d + "_" + i;
            return keys;
        }

        private static string ActionKey(ViewAnim anim)
        {
            var i = (int)anim;
            if (i >= 0 && i < ActionKeys.Length) return ActionKeys[i];
            ViewLog.WarnThrottled("frames.action.unknown", $"ActionKey: 未登记的动作 {i} ⇒ 用 idle");
            return "idle";
        }

        /// <summary>
        /// 取某帧贴图：驻留缓存 → 已驻留资源 → 异步加载。**取不到返回 null**（调用方用 `Placeholder`）。
        /// </summary>
        public static Sprite Resolve(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (Cache.TryGetValue(key, out var cached)) return cached;

            if (Game.Res == null)
            {
                ViewLog.WarnOnce("frames.nores",
                    "SpriteFrames.Resolve: Game.Res 为 null（CloverRes.Init 未调用？）⇒ 全部用纯色占位");
                return null;
            }

            var resident = Game.Res.TryGet<Sprite>(key);
            if (resident != null)
            {
                Cache[key] = resident;
                return resident;
            }

            // 正在加载 或 刚失败还在退避期 ⇒ 本帧先返回 null（调用方用占位）
            if (Pending.Contains(key)) return null;
            float retryAt;
            if (RetryAfter.TryGetValue(key, out retryAt) && Time.time < retryAt) return null;

            Pending.Add(key);
            Game.Res.LoadAsset<Sprite>(key, sprite =>
            {
                Pending.Remove(key);
                if (sprite == null)
                {
                    // 缺图（或"资源还没导完"）⇒ 记退避时刻，等下一轮 `Resolve` 自动重试（自愈）。
                    // 日志只报一次（每次刷新都报会刷屏）；清单已登记。
                    RetryAfter[key] = Time.time + RetrySeconds;
                    ViewLog.WarnOnce("frames.missing",
                        $"精灵帧缺失：{key}（用纯色占位，每 {RetrySeconds:0.#}s 自动重试一次；" +
                        "文件名约定 {动作}_{方向}_{帧号}.png，由 tools/d2codec/export_chars.py 从原版 .dcc 导出）");
                    _repaintRequested = true;      // 触发一次全量重铺 ⇒ 下一轮就会重试
                    return;
                }
                RetryAfter.Remove(key);
                Cache[key] = sprite;
                _repaintRequested = true;
            });
            return null;
        }

        /// <summary>
        /// 批量预热一组帧键：切动作 / 换朝向时把**整组帧**一次性发起异步加载。
        /// <para>为什么要它：<see cref="Resolve"/> 只取"当前这一帧"，逐帧首次访问时每一帧都要各等一次
        /// 异步回调 ⇒ 首圈动画会逐帧闪一次占位图（见 `ViewModule.ApplyFrame` 的注释）。
        /// 预取把整组帧的加载并发发起，动画尽快变完整（原版是把整个单位的 .dcc 一次读进内存，行为等价）。</para>
        /// <para>不阻塞、不改渲染状态：已缓存 / 在途 / 退避期内的键走 <see cref="Resolve"/> 的同一套判据，自然跳过。</para>
        /// </summary>
        /// <param name="keys">帧键数组（`Keys(...)` 的返回值；null 安全）。</param>
        public static void Prefetch(string[] keys)
        {
            if (keys == null) return;
            for (var i = 0; i < keys.Length; i++) Resolve(keys[i]);
        }

        /// <summary>
        /// 纯色占位 sprite（懒创建）：**40×79 白图**，**pivot = 脚底中心**（`conventions.md` §坐标），
        /// PPU = `GameConst.PixelsPerUnit`。
        /// <para>尺寸取 40×79 = 原版单位在 80 px/单位 下的典型尺寸（79 px ≈ 亚马逊/阿卡拉的高度），
        /// 于是占位块与真素材**一样大**（旧值是 48 px，明显偏矮）。</para>
        /// </summary>
        public static Sprite Placeholder
        {
            get
            {
                if (_placeholder != null) return _placeholder;

                const int w = 40;
                const int h = 79;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "D2CharPlaceholder",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };

                var px = new Color32[w * h];
                var white = new Color32(255, 255, 255, 255);
                for (var i = 0; i < px.Length; i++) px[i] = white;
                tex.SetPixels32(px);
                tex.Apply(false, false);

                _placeholder = Sprite.Create(tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0f),
                    GameConst.PixelsPerUnit);
                _placeholder.name = "D2CharPlaceholder";
                return _placeholder;
            }
        }

        /// <summary>职业占位色（5 职业一眼分得开）。</summary>
        public static Color PlaceholderColorOfPlayer(PlayerClass cls)
        {
            switch (cls)
            {
                case PlayerClass.Amazon: return new Color(0.95f, 0.80f, 0.25f);
                case PlayerClass.Sorceress: return new Color(0.35f, 0.55f, 0.95f);
                case PlayerClass.Necromancer: return new Color(0.45f, 0.75f, 0.45f);
                case PlayerClass.Paladin: return new Color(0.95f, 0.95f, 0.95f);
                case PlayerClass.Barbarian: return new Color(0.80f, 0.35f, 0.25f);
                default:
                    ViewLog.WarnThrottled("color.cls.unknown", $"PlaceholderColorOfPlayer: 未登记职业 {(int)cls}");
                    return Color.magenta;
            }
        }

        /// <summary>怪物占位色（按官方 AI 类型区分；精英怪统一镀金边色，一眼可辨）。</summary>
        public static Color PlaceholderColorOfMonster(MonsterState s)
        {
            if (s == null) return Color.gray;
            if (s.isChampion) return new Color(1.00f, 0.85f, 0.30f);   // 精英：金色

            switch (s.ai)
            {
                case MonsterAI.Melee: return new Color(0.70f, 0.30f, 0.25f);
                case MonsterAI.Range: return new Color(0.55f, 0.70f, 0.30f);
                case MonsterAI.Shaman: return new Color(0.60f, 0.35f, 0.80f);
                case MonsterAI.Coward: return new Color(0.80f, 0.62f, 0.40f);
                default:
                    ViewLog.WarnThrottled("color.ai.unknown", $"PlaceholderColorOfMonster: 未登记 AI {(int)s.ai}");
                    return Color.gray;
            }
        }

        /// <summary>物品品质色（原版配色：白/蓝/金/绿/暗金）——地面物品与 tooltip 共用同一套。</summary>
        public static Color QualityColor(ItemQuality q)
        {
            switch (q)
            {
                case ItemQuality.Magic: return new Color(0.35f, 0.55f, 1.00f);
                case ItemQuality.Rare: return new Color(1.00f, 0.85f, 0.20f);
                case ItemQuality.Set: return new Color(0.30f, 0.90f, 0.35f);
                case ItemQuality.Unique: return new Color(0.75f, 0.55f, 0.30f);
                default: return Color.white;
            }
        }

        /// <summary>拿某只怪的动画帧目录（`monster_c.sprite` → 小写目录名）。</summary>
        public static string SpriteCodeOf(int kindId)
        {
            var row = Table.Tables.Default.Monster.Get(kindId);
            if (row == null || string.IsNullOrEmpty(row.Sprite))
            {
                ViewLog.WarnThrottled("frames.norow",
                    $"SpriteCodeOf({kindId})：monster_c 里找不到该种类或 sprite 列为空 ⇒ 占位目录");
                return "unknown";
            }
            return row.Sprite.ToLowerInvariant();
        }

        /// <summary>清空缓存（退出 Stage 时；贴图资源由引擎持有，这里只清引用）。</summary>
        public static void Clear()
        {
            Cache.Clear();
            Pending.Clear();
            RetryAfter.Clear();
            FallbackLogged.Clear();
            _repaintRequested = false;
        }
    }
}
