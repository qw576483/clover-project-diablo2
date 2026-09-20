// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/SpriteFrames.cs
// 逐帧动画的**帧键 + 贴图解析**层（素材唯一的出入口）。
//
// 帧键命名：`{动作}_{方向}_{帧号}`，例：`walk_s_0`、`attack_ne_3`、`death_s_7`
//   动作：idle / walk / attack / cast / hit / death / run（★ run = 原版 RN，片 2b 接入，
//         见 `ViewAnim.Run` 的注释；只有部分单位有）
//   方向：s / sw / w / nw / n / ne / e / se（与 `Def.Dir8` 同名小写，**序号沿用暗黑2**）
// 目录：
//   角色 → `Core.ResPaths.CharDir(PlayerClass)`  = `D2/Chars/{class}/`（class = amazon…）
//   怪物 → `Core.ResPaths.MonsterDir(code)`       = `D2/Monsters/{code}/`
//         （code = `monster_c.sprite` 列，官方 `MonStats.Code` 的 DCC 前缀，如 `fa`/`zm`/`si`；
//           NPC 用 `NpcSpriteCode`：阿卡拉 ps / 卡夏 rc / 恰西 ci / 基德 gh / 瓦瑞夫 wa）
//
// ★★ 帧数：**真实值**来自原版 `.cof` 的 `framesPerDirection` —— 生成物
//    `Module/View/SpriteFrameCounts.cs`（导出器 `tools/d2codec/export_chars.py --emit-cs`）。
//    ⛔ 原版**每个单位的每个动作帧数都不同**（例：亚马逊 idle=8、attack=13、cast=20、death=23；
//       堕落者 idle=20、attack=10），所以不能再用一份全局常量（那会让帧键指向不存在的图 = 静默缺图）。
//    `FrameCounts` 保留为**未登记单位的兜底**（值 = 亚马逊的真实帧数，默认职业）。
//
// ★★ 像素尺度（"不许浮空/大小不对"的关键）：原版 D2 是 **80 像素 = 1 世界单位**
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
// ★ agent-33 引擎下沉 A2：引擎侧新增了**同名**枚举 `CloverEngine.Dir8`（`Runtime/Core/Dir8.cs`），
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
        /// <para>⚠️ 保留这个公开数组是**契约**：离线宿主 `tools/combatcheck/Program.cs` 会读它。</para>
        /// </summary>
        public static readonly int[] FrameCounts =
        {
            8,   // Idle   （原版 NU，亚马逊）
            8,   // Walk   （原版 WL，亚马逊）
            13,  // Attack （原版 A1，亚马逊）
            20,  // Cast   （原版 SC，亚马逊）
            6,   // Hit    （原版 GH，亚马逊）
            23,  // Death  （原版 DT，亚马逊）
            8,   // Run    （原版 RN，亚马逊；★ 片 2b 新增，**必须与 ViewAnim.Run = 6 同下标**）
        };

        /// <summary>动作名（帧键前缀，下标 = <see cref="ViewAnim"/>）—— 与导出器同一张表。</summary>
        private static readonly string[] ActionKeys = { "idle", "walk", "attack", "cast", "hit", "death", "run" };

        /// <summary>
        /// 缺动作时的**回退链**（原版有的动作才可能没有：NPC 只有 NU/WL；
        /// 多数怪物没有 SC；`cast` 回退到攻击、`death` 回退到 idle 都是"用原版动画顶上"，
        /// **绝不用占位色块**）。下标 = <see cref="ViewAnim"/>。
        /// <para>★ 片 2b 的 `Run` 一条：**原版只有一部分单位有 RN**
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
            new[] { ViewAnim.Run, ViewAnim.Walk, ViewAnim.Idle },      // Run（★ 片 2b）
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
        /// <para>◆ 为什么必须有它（真实的静默失败，本轮实测踩到）：贴图是**异步**加载的，
        /// 若第一次 `Resolve` 发生在 Unity **还没导完资源**的时刻（实测：8 千多张 PNG 刚落盘、
        /// 编辑器正在导入），回调拿到 null；旧实现此时把键永久留在 `Pending` 里 ⇒
        /// **这一帧的图永远不会再取**，怪物/NPC 就一直停在纯色占位块上（而且只报一条日志）。
        /// 修法：失败时**移出 Pending 并记下退避时刻**，`Resolve` 下次被调到（帧号变化或
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
        /// 动作的**基准**播放帧率（原版节奏按 `AnimData.d2`；本项目沿用既有值）。
        /// <para>⚠️ 它是**基准**、不是最终有效帧率：移动类动作（<see cref="ViewAnim.Walk"/> /
        /// <see cref="ViewAnim.Run"/>）由 <see cref="SpeedScaleForCycle"/> 按**实际速度**再缩放
        /// —— 有效帧率 = 帧数 × 格/秒，见 <see cref="FpsForCycle"/>。</para>
        /// </summary>
        public static float FpsOf(ViewAnim anim)
        {
            switch (anim)
            {
                case ViewAnim.Idle: return 6f;
                case ViewAnim.Walk: return 12f;
                case ViewAnim.Attack: return 12f;
                case ViewAnim.Cast: return 10f;
                case ViewAnim.Hit: return 12f;
                case ViewAnim.Death: return 8f;
                // ★ 片 2b：Run 的基准 = 兜底帧数（亚马逊 RN 8 帧）× 原版跑速 3.0 格/秒 = 24fps
                //   （口径 = `FpsForCycle`；写成表达式是为了不出现第二份字面量）。
                case ViewAnim.Run: return FrameCounts[(int)ViewAnim.Run] * GameConst.PlayerWalkSpeed;
                default:
                    ViewLog.WarnThrottled("anim.fps.unknown", $"FpsOf: 未登记的动作 {(int)anim} ⇒ 用默认帧率");
                    return SpriteAnimator.DefaultFps;
            }
        }

        /// <summary>动作是否循环（只有死亡不循环；受击短暂动作靠 `PlayHit` 后的状态自动切回）。</summary>
        public static bool LoopOf(ViewAnim anim)
        {
            return anim != ViewAnim.Death;
        }

        /// <summary>
        /// **每格一个动画循环** ⇒ 该动作的有效帧率 = **帧数 × 速度（格/秒）**。
        /// <para>出处（原版规律，不是估的）：角色/怪物每个移动动画循环正好走完**一格**
        /// —— `原版资源/参考工程_Diablerie/.../Engine/IO/D2Formats/AnimData.cs:16-37` 的
        /// `referenceFrameCount`（`AMWL = 6` / `AMRN = 4`）配合原版走 1.4 格/s、跑 3.0 格/s，
        /// 得 WL ≈ 6 × 1.4 = 8.4fps、RN = 4 × 3.0 = 12fps（同一"每格一循环"口径）。</para>
        /// <para>本项目用**自己导出的真实帧数**（亚马逊 WL/RN 都是 8 帧）
        /// ⇒ 走 8 × 1.4 = 11.2fps、跑 8 × 3.0 = 24fps。</para>
        /// </summary>
        /// <param name="frameCount">该单位该动作的**真实**帧数（`FrameCountOf` / `Anim.FrameCount`）。</param>
        /// <param name="tilesPerSecond">实际移动速度（格/秒）。</param>
        public static float FpsForCycle(int frameCount, float tilesPerSecond)
        {
            return frameCount * tilesPerSecond;
        }

        /// <summary>
        /// 把「每格一循环的目标帧率」换算成 <see cref="SpriteAnimator.SpeedScale"/>：
        /// `SpeedScale = FpsForCycle(帧数, 速度) / baseFps`（`baseFps` = `Play` 时传的 `FpsOf(动作)`）。
        /// <para>为什么用 `SpeedScale` 而不是改 `Play` 的 fps 参数：`SpriteAnimator.Play` 的**早退判据
        /// 不比较 fps**（见 `SpriteAnimator.Play` 的注释）⇒ "动作没变、只有速度变了"时新 fps 会被吞掉；
        /// 而 `SpeedScale` 每帧可改，且**不动帧号与累计时间**（不会把动画打回第 0 帧）。</para>
        /// <para>参数非法（帧数 ≤ 0 / 速度 ≤ 0 / 基准帧率 ≤ 0）⇒ 恒返回 **1**（⛔ 不返回 0：
        /// 0 倍速会让动画停住，比"帧率不准"更糟）。</para>
        /// </summary>
        public static float SpeedScaleForCycle(int frameCount, float tilesPerSecond, float baseFps)
        {
            if (frameCount <= 0 || tilesPerSecond <= 0f || baseFps <= 0.01f) return 1f;
            return FpsForCycle(frameCount, tilesPerSecond) / baseFps;
        }

        /// <summary>
        /// 是否是**移动类**动作（只有它随速度缩放手感）。
        /// <para>⛔ Idle / Attack / Cast / Hit / Death 一律不许跟着缩放：原版的出招与受击节奏
        /// 与移动速度无关（`AnimData` 里每个动作一套独立帧率）。</para>
        /// </summary>
        public static bool IsMoveAnim(ViewAnim anim)
        {
            return anim == ViewAnim.Walk || anim == ViewAnim.Run;
        }

        /// <summary>角色的某个动作/方向的帧键数组（单位键 = 职业名小写，与 `CharDir` 同名）。</summary>
        public static string[] Keys(PlayerClass cls, ViewAnim anim, Dir8 dir)
        {
            var unitKey = cls.ToString().ToLowerInvariant();
            return Build(unitKey, ResPaths.CharDir(cls), anim, dir);
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

        /// <summary>某单位的某动作**真实**帧数（0 = 该单位没有这个动作的原版动画）。</summary>
        public static int FrameCountOf(string unitKey, ViewAnim anim)
        {
            var n = SpriteFrameCounts.Of(unitKey, anim);
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
            if (SpriteFrameCounts.Of(unitKey, anim) > 0) return anim;

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
