// ─────────────────────────────────────────────────────────────────────────────
//
//      hit 播完回 idle（并复现"旧口径只出 1 帧"以证明断言有效）/ death 停末帧
//
// 本宿主不复制任何**被测**逻辑：链的是 `client/Assets/Scripts/**` 的真实源码。
//    只用来证明"新断言能判红"（见该函数的注释）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.View;
// 片「武器外观接线」：本宿主**不在** `Module/*` 里 ⇒ 允许跨模块 using，
//   用来把 `Module/View/EquipVisual.HandOf` 与本来的槽位判定 `Module/Item/Equipment.SlotOf`
using Diablo2.Module.Item;
using Table;
using Dir8 = Diablo2.Def.Dir8;

namespace AnimCheck
{
    internal static class Program
    {
        private const float Dt = 1f / 60f;
        private static int _ok;
        private static int _fail;

        /// <summary>(显示名, 种类, 单位键, 是否玩家职业, 是否 NPC)。</summary>
        private static readonly (string Disp, string Kind, string Unit, bool IsPlayer, bool IsNpc)[] Units =
        {
            ("Amazon（亚马逊）", "player", "amazon", true, false),
            ("Barbarian（野蛮人）", "player", "barbarian", true, false),
            ("Sorceress（法师）", "player", "sorceress", true, false),
            ("Necromancer（死灵法师）", "player", "necromancer", true, false),
            ("Paladin（圣骑士）", "player", "paladin", true, false),
            ("堕落者 Fallen", "monster", "fa", false, false),
            ("堕落萨满 FallenShaman", "monster", "fs", false, false),
            ("尖刺鼠 QuillRat", "monster", "si", false, false),
            ("僵尸 Zombie", "monster", "zm", false, false),
            ("堕落罗格 CorruptRogue", "monster", "cr", false, false),
            ("血鹰 BloodHawk", "monster", "bk", false, false),
            ("巨兽 Brute", "monster", "ye", false, false),
            ("幽灵 Wraith", "monster", "wr", false, false),
            ("阿卡拉 Akara(NPC)", "npc", "ps", false, true),
            ("卡夏 Kashya(NPC)", "npc", "rc", false, true),
            ("恰西 Charsi(NPC)", "npc", "ci", false, true),
            ("基德 Gheed(NPC)", "npc", "gh", false, true),
            ("瓦瑞夫 Warriv(NPC)", "npc", "wa", false, true),
        };

        private static readonly Dir8[] AllDirs =
        {
            Dir8.S, Dir8.SW, Dir8.W, Dir8.NW, Dir8.N, Dir8.NE, Dir8.E, Dir8.SE,
        };

        private static readonly ViewAnim[] AllAnims =
        {
            ViewAnim.Idle, ViewAnim.Walk, ViewAnim.Attack, ViewAnim.Cast,
            ViewAnim.Hit, ViewAnim.Death, ViewAnim.Run,
        };

        private static string _root;

        private static int Main()
        {
            Console.WriteLine("================ AnimCheck：片 W5 动画 1:1 自证（离线）================");
            _root = ResolveProjectRoot();
            Console.WriteLine("仓库根 = " + _root);

            Section1GeneratedTable();
            Section2FramesOnDisk();
            Section3KeyAndCount();
            Section4LoopPolicy();
            Section5SelectorTruthTable();
            Section6AnimatorRun();
            Section7EquipVisual();
            Section8MonsterHit();

            Console.WriteLine();
            Console.WriteLine($"================ AnimCheck 结束：通过 {_ok} 项，失败 {_fail} 项 ================");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section1GeneratedTable()
        {
            Section("1. 生成物 SpriteFrameCounts × 各单位 manifest.json（== 原版 .cof 的 framesPerDirection）");

            var actions = SpriteFrameCounts.ActionNames;
            Check("动作表 = idle/walk/attack/cast/hit/death/run（7 个，次序 = ViewAnim 下标）",
                actions.Length == 7
                && actions[0] == "idle" && actions[1] == "walk" && actions[2] == "attack"
                && actions[3] == "cast" && actions[4] == "hit" && actions[5] == "death"
                && actions[6] == "run",
                string.Join("/", actions));

            foreach (var a in AllAnims)
            {
                Check($"ViewAnim.{a} 的下标 == 动作表里的位置（否则整张帧数表错位）",
                    (int)a == Array.IndexOf(actions, a.ToString().ToLowerInvariant()),
                    $"{(int)a} vs {Array.IndexOf(actions, a.ToString().ToLowerInvariant())}");
            }

            var mismatched = 0;
            var checkedCells = 0;
            foreach (var u in Units)
            {
                if (!SpriteFrameCounts.ByUnit.TryGetValue(u.Unit, out var row))
                {
                    Check($"生成物里有单位「{u.Unit}」", false, "ByUnit 里找不到");
                    continue;
                }
                Check($"{u.Unit}：生成物 7 列", row.Length == 7, $"length={row.Length}");

                // manifest 是**同一份数据**的另一处出处 ⇒ 必须逐格相同
                var man = ReadManifest(u);
                if (man == null)
                {
                    Check($"{u.Unit}：manifest.json 可读", false, "读不到");
                    continue;
                }
                for (var i = 0; i < actions.Length; i++)
                {
                    int manFrames, manDirs;
                    var has = man.TryGetValue(actions[i], out var mf) && mf.HasValue;
                    manFrames = has ? mf.Value.Frames : 0;
                    manDirs = has ? mf.Value.Dirs : 0;
                    checkedCells++;
                    if (has && (manFrames != row[i] || manDirs != 8))
                    {
                        mismatched++;
                        Console.WriteLine($"      ✗ {u.Unit}/{actions[i]}：manifest frames={manFrames} dirs={manDirs}"
                                          + $" vs 生成物 {row[i]} / dirs=8");
                    }
                }
            }
            Check($"生成物 × manifest 逐格一致（{checkedCells} 格，不一致 {mismatched}）",
                mismatched == 0, $"不一致 {mismatched} 格");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section2FramesOnDisk()
        {
            Section("2. 逐单位 × 逐动作：8 方向 × N 帧的 PNG 都在、帧号连续、方向间不重复");

            var totalFiles = 0;
            var missing = new List<string>();
            var dupRows = new List<string>();
            var dirCoverageBad = new List<string>();

            foreach (var u in Units)
            {
                var row = SpriteFrameCounts.ByUnit[u.Unit];
                var subPath = SubPath(u);      // `D2/Chars/amazon/` 或 `D2/Monsters/ps/`

                for (var ai = 0; ai < AllAnims.Length; ai++)
                {
                    var act = AllAnims[ai];
                    var n = row[ai];
                    if (n <= 0) continue;

                    var action = act.ToString().ToLowerInvariant();
                    var perDir = new Dictionary<string, string>();
                    var perDirCount = new Dictionary<string, int>();

                    foreach (var d in AllDirs)
                    {
                        var dl = d.ToString().ToLowerInvariant();
                        var hashes = new List<string>();
                        for (var i = 0; i < n; i++)
                        {
                            var rel = subPath + action + "_" + dl + "_" + i + ".png";
                            var path = Path.Combine(_root, "client", "Assets", "Resources", "Clover",
                                rel.Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(path))
                            {
                                missing.Add(u.Unit + "/" + action + "_" + dl + "_" + i);
                                continue;
                            }
                            totalFiles++;
                            hashes.Add(Md5(path));
                        }
                        perDir[dl] = string.Join(",", hashes);
                        perDirCount[dl] = hashes.Count;

                        // 帧号连续：磁盘上该 (动作, 方向) 的帧号必须正好是 0..N-1（无洞、无多余）
                        var idx = ScanIndices(u, action, dl);
                        var contiguous = idx.Length == n;
                        for (var k = 0; contiguous && k < n; k++) contiguous = idx[k] == k;
                        if (!contiguous)
                        {
                            dirCoverageBad.Add($"{u.Unit}/{action}_{dl}：磁盘帧号 {idx.Length} 个（首 {First(idx)} 末 {Last(idx)}），期望 0..{n - 1}");
                        }
                    }

                    // 方向间不得"复用同一帧充数"（原版 8 方向 = 8 套独立 .cof）
                    for (var i = 0; i < AllDirs.Length; i++)
                    {
                        for (var j = i + 1; j < AllDirs.Length; j++)
                        {
                            var a1 = AllDirs[i].ToString().ToLowerInvariant();
                            var a2 = AllDirs[j].ToString().ToLowerInvariant();
                            // 只在**两边都取满 n 张**时比较（缺帧另有断言）
                            if (perDirCount[a1] == n && perDirCount[a2] == n && perDir[a1] == perDir[a2])
                            {
                                dupRows.Add($"{u.Unit}/{action}：{a1} 与 {a2} 逐字节相同（复用充数）");
                            }
                        }
                    }
                }
            }

            Check($"8 方向 × N 帧 的引用帧文件全部存在（共 {totalFiles} 张）",
                missing.Count == 0,
                missing.Count == 0 ? $"0 缺（{totalFiles} 张）" : "缺 " + missing.Count + "：" + string.Join(",", missing.GetRange(0, Math.Min(5, missing.Count))));
            Check("每个 (动作, 方向) 的磁盘帧号都是连续的 0..N-1（无洞 / 无多余帧）",
                dirCoverageBad.Count == 0,
                dirCoverageBad.Count == 0 ? "全部连续" : string.Join(" | ", dirCoverageBad.GetRange(0, Math.Min(3, dirCoverageBad.Count))));
            Check("任意两条方向都不是「逐字节相同」（⛔ 不许全方向复用同一帧充数）",
                dupRows.Count == 0,
                dupRows.Count == 0 ? "0 组重复" : string.Join(" | ", dupRows.GetRange(0, Math.Min(3, dupRows.Count))));
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section3KeyAndCount()
        {
            Section("3. SpriteFrames.Keys / FrameCountOf / ResolveAnim 与生成物 · 磁盘一致");

            var badCount = new List<string>();
            var badKey = new List<string>();
            var badFallback = new List<string>();

            foreach (var u in Units)
            {
                var row = SpriteFrameCounts.ByUnit[u.Unit];
                for (var ai = 0; ai < AllAnims.Length; ai++)
                {
                    var act = AllAnims[ai];
                    var orig = row[ai];
                    var real = SpriteFrames.ResolveAnim(u.Unit, act);

                    // 原版有该动作 ⇒ 必须原样命中（不回退）
                    if (orig > 0 && real != act) badFallback.Add($"{u.Unit}/{act} ⇒ {real}（原版有 {orig} 帧）");
                    // 原版没有 ⇒ 回退到**该单位自己有的**动作（不是占位）
                    if (orig == 0 && real != act && row[(int)real] <= 0) badFallback.Add($"{u.Unit}/{act} ⇒ {real}（回退目标也没帧）");

                    var n = SpriteFrames.FrameCountOf(u.Unit, real);
                    if (orig > 0 && n != orig) badCount.Add($"{u.Unit}/{act}：FrameCountOf={n} vs 生成物 {orig}");

                    foreach (var d in AllDirs)
                    {
                        var keys = u.IsPlayer
                            ? SpriteFrames.Keys(PlayerClassOf(u.Unit), act, d)
                            : SpriteFrames.Keys(u.Unit, act, d);
                        if (keys.Length != n) { badKey.Add($"{u.Unit}/{act}/{d}：{keys.Length} 个键 vs 帧数 {n}"); continue; }

                        // 前缀必须 = `ResPaths.CharDir/MonsterDir`（帧键与磁盘目录的唯一口径）
                        var prefix = u.IsPlayer ? ResPaths.CharDir(PlayerClassOf(u.Unit)) : ResPaths.MonsterDir(u.Unit);
                        // `SpriteFrames.Build` 用**回退后的动作名**拼键（回退发生时键指向回退动作的帧）
                        var actionName = real.ToString().ToLowerInvariant();
                        var dl = d.ToString().ToLowerInvariant();
                        for (var i = 0; i < keys.Length; i++)
                        {
                            var want = prefix + actionName + "_" + dl + "_" + i;
                            if (keys[i] != want) badKey.Add($"{u.Unit}/{act}/{d}[{i}]={keys[i]} 期望 {want}");
                        }
                    }
                }
            }

            Check("FrameCountOf == 生成物帧数（已登记单位一律走真实值）", badCount.Count == 0,
                badCount.Count == 0 ? "全部一致" : string.Join(" | ", badCount.GetRange(0, Math.Min(3, badCount.Count))));
            Check("ResolveAnim：原版有的动作不复位回退；原版没有的回退到该单位自己的动作",
                badFallback.Count == 0,
                badFallback.Count == 0 ? "全部正确" : string.Join(" | ", badFallback.GetRange(0, Math.Min(3, badFallback.Count))));
            Check("Keys() 的拼法与磁盘命名 {动作}_{方向}_{帧号} 完全一致", badKey.Count == 0,
                badKey.Count == 0 ? "全部一致" : string.Join(" | ", badKey.GetRange(0, Math.Min(3, badKey.Count))));
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section4LoopPolicy()
        {
            Section("4. LoopOf：只有 Idle / Walk / Run 循环（attack 播完回 idle / death 停末帧 的前提）");

            Check("LoopOf(Idle/Walk/Run) == true（周期动画）",
                SpriteFrames.LoopOf(ViewAnim.Idle) && SpriteFrames.LoopOf(ViewAnim.Walk)
                && SpriteFrames.LoopOf(ViewAnim.Run), "3 个都为 true");
            Check("LoopOf(Attack/Cast/Hit/Death) == false（单次播放）",
                !SpriteFrames.LoopOf(ViewAnim.Attack) && !SpriteFrames.LoopOf(ViewAnim.Cast)
                && !SpriteFrames.LoopOf(ViewAnim.Hit) && !SpriteFrames.LoopOf(ViewAnim.Death),
                "4 个都为 false");
            Check("IsMoveAnim 未受影响：只有 Walk/Run 随速度缩放",
                SpriteFrames.IsMoveAnim(ViewAnim.Walk) && SpriteFrames.IsMoveAnim(ViewAnim.Run)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Idle)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Attack)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Cast)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Hit)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Death), "Walk/Run = true，其余 false");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section5SelectorTruthTable()
        {
            Section("5. ViewAnimState.SelectPlayer/SelectMonster 的优先级");

            Check("玩家：死亡压一切（即使同时在受击/施法/移动）",
                ViewAnimState.SelectPlayer(true, true, true, true, true, true) == ViewAnim.Death, "= Death");
            Check("玩家：受击压施法/挥击/移动（原版 GH 不可打断）",
                ViewAnimState.SelectPlayer(false, true, true, true, true, true) == ViewAnim.Hit, "= Hit");
            Check("玩家：施法压挥击/移动",
                ViewAnimState.SelectPlayer(false, false, true, true, true, true) == ViewAnim.Cast, "= Cast");
            Check("玩家：挥击压移动",
                ViewAnimState.SelectPlayer(false, false, false, true, true, true) == ViewAnim.Attack, "= Attack");
            Check("玩家：移动档按契约 IsRunning 选跑/走（Run/Walk = 原版 RN/WL 两套动画）",
                ViewAnimState.SelectPlayer(false, false, false, false, true, true) == ViewAnim.Run
                && ViewAnimState.SelectPlayer(false, false, false, false, true, false) == ViewAnim.Walk,
                "跑 ⇒ Run / 走 ⇒ Walk");
            Check("玩家：全部未激活 ⇒ Idle",
                ViewAnimState.SelectPlayer(false, false, false, false, false, false) == ViewAnim.Idle, "= Idle");
            Check("玩家：受击**+ 移动**仍是 Hit（不被移动档吃掉）",
                ViewAnimState.SelectPlayer(false, true, false, false, true, true) == ViewAnim.Hit, "= Hit");

            Check("怪物：死亡压一切",
                ViewAnimState.SelectMonster(false, true, true, true) == ViewAnim.Death, "= Death");
            Check("怪物：受击压出手/移动",
                ViewAnimState.SelectMonster(true, true, true, true) == ViewAnim.Hit, "= Hit");
            Check("怪物：出手压移动",
                ViewAnimState.SelectMonster(true, false, true, true) == ViewAnim.Attack, "= Attack");
            Check("怪物：移动 ⇒ Walk；站着 ⇒ Idle",
                ViewAnimState.SelectMonster(true, false, false, true) == ViewAnim.Walk
                && ViewAnimState.SelectMonster(true, false, false, false) == ViewAnim.Idle,
                "Walk / Idle");
            Check("怪物链里**没有** Run / Cast（原版怪物只有 NU/WL/A1/GH/DT；RN/SC 触发条件无出处 ⇒ 登记）",
                ViewAnimState.SelectMonster(true, false, false, true) != ViewAnim.Run
                && ViewAnimState.SelectMonster(true, true, true, true) != ViewAnim.Cast,
                "Walk/Idle/Hit/Attack/Death 五档");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section6AnimatorRun()
        {
            Section("6. ★ 真 SpriteAnimator 逐帧推进：walk 循环 / attack 播完回 idle / hit 播完回 idle / death 停末帧");

            var walkKeys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.S);
            var atkKeys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Attack, Dir8.S);
            var hitKeys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Hit, Dir8.S);
            var dthKeys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Death, Dir8.S);

            // ── 6a walk 循环：60s 内帧号绕回 0 至少一次，且永不 Finished ──
            var w = new SpriteAnimator();
            w.Play(ViewAnim.Walk, walkKeys, SpriteFrames.FpsOf(ViewAnim.Walk), SpriteFrames.LoopOf(ViewAnim.Walk));
            var wraps = 0;
            var last = w.FrameIndex;
            var finishedSeen = false;
            for (var i = 0; i < 3600; i++)
            {
                w.Tick(Dt);
                if (w.FrameIndex < last) wraps++;
                last = w.FrameIndex;
                if (w.Finished) finishedSeen = true;
            }
            Check($"6a walk 循环：60s 内帧号绕回 {wraps} 次（每循环 8 帧 @ {SpriteFrames.FpsOf(ViewAnim.Walk):0.#}fps）且从不 Finished",
                wraps > 0 && !finishedSeen, $"绕回 {wraps} 次 / Finished={finishedSeen}");

            // ── 6b attack 单次播放：播到**末帧**停下（Finished），随后 want 回落 Idle ──
            var a = new SpriteAnimator();
            a.Play(ViewAnim.Attack, atkKeys, SpriteFrames.FpsOf(ViewAnim.Attack), SpriteFrames.LoopOf(ViewAnim.Attack));
            var ticks = 0;
            while (!a.Finished && ticks < 600) { a.Tick(Dt); ticks++; }
            var atkLast = atkKeys.Length - 1;
            Check($"6b attack 播完停在末帧（frame={a.FrameIndex}，期望 {atkLast}；{ticks} 帧内）",
                a.Finished && a.FrameIndex == atkLast, $"finished={a.Finished} frame={a.FrameIndex}/{a.FrameCount}");
            Check("6b attack 播完 ⇒ want 回落 Idle（attack 播完回 idle）",
                ViewAnimState.SelectPlayer(false, false, false, false, false, false) == ViewAnim.Idle
                && a.FrameIndex == atkLast, "Playing 判定用 Anim.Finished ⇒ Idle");
            var a2 = new SpriteAnimator();
            a2.Play(ViewAnim.Attack, atkKeys, SpriteFrames.FpsOf(ViewAnim.Attack), SpriteFrames.LoopOf(ViewAnim.Attack));
            for (var i = 0; i < 600; i++) a2.Tick(Dt);
            Check("6b attack 停住后**不再动**（非循环 ⇒ 不自己重播）",
                a2.FrameIndex == atkLast, $"frame={a2.FrameIndex}（600 帧后仍是末帧）");

            // ── 6c death 停末帧 ──
            var d = new SpriteAnimator();
            d.Play(ViewAnim.Death, dthKeys, SpriteFrames.FpsOf(ViewAnim.Death), SpriteFrames.LoopOf(ViewAnim.Death));
            for (var i = 0; i < 600; i++) d.Tick(Dt);
            Check($"6c death 停末帧（frame={d.FrameIndex}，期望 {dthKeys.Length - 1}）",
                d.Finished && d.FrameIndex == dthKeys.Length - 1, $"frame={d.FrameIndex}/{d.FrameCount}");

            var hitFramesNew = SimulatePlayerHit(hitKeys, useLegacyChain: false);
            var hitFramesOld = SimulatePlayerHit(hitKeys, useLegacyChain: true);
            Check($"6d 玩家受击：新口径显示过的受击帧数 = {hitFramesNew}（= 该动作全部 {hitKeys.Length} 帧，播完才回 idle）",
                hitFramesNew == hitKeys.Length, $"新口径 {hitFramesNew} 帧 / 动作共 {hitKeys.Length} 帧");
            Check($"6d 旧口径反例：无「受击档」的 want 链出 {hitFramesOld} 帧（= 0 ⇒ 断言能判红，非空断言）",
                hitFramesOld == 0,
                $"旧口径 {hitFramesOld} 帧 —— 同一帧内 `PlayHit` 先贴 Hit[0]（PlayHit 里那次 ApplyFrame），"
                + "紧接着 `TickPlayer` 把 `Playing` 改成 Idle、`TickOne` 消费 `NeedsFrameRefresh` 贴回 Idle 帧"
                + "（都在渲染之前）⇒ 受击动作**一帧都没被渲染**");
            Check("6d 受击动画播完 ⇒ want 回落 Idle（受击播完回 idle）",
                ViewAnimState.SelectPlayer(false, false, false, false, false, false) == ViewAnim.Idle
                && SpriteFrames.LoopOf(ViewAnim.Hit) == false, "Hit 非循环 ⇒ Finished 可达");
            Check("6d IsHitHolding：只有「正在播 & 帧数>1 & 未播完」才保持"
                  + "（帧数=1 时 `Tick` 直接返回 ⇒ Finished 永假 ⇒ 必须排除，否则会永远卡在受击姿态）",
                ViewAnimState.IsHitHolding(ViewAnim.Hit, 6, false)
                && !ViewAnimState.IsHitHolding(ViewAnim.Hit, 6, true)
                && !ViewAnimState.IsHitHolding(ViewAnim.Hit, 1, false)
                && !ViewAnimState.IsHitHolding(ViewAnim.Idle, 8, false),
                "Hit/6/未播完=True，其余三组=False");

            // ── 6e 怪物：受击硬直结束 ⇒ 回落 Idle（不判帧数：E28 的硬直时长 = 登记项）──
            var m = new SpriteAnimator();
            var mKeys = SpriteFrames.Keys("fa", ViewAnim.Hit, Dir8.S);
            m.Play(ViewAnim.Hit, mKeys, SpriteFrames.FpsOf(ViewAnim.Hit), SpriteFrames.LoopOf(ViewAnim.Hit));
            var stun = 0.18f;
            var elapsed = 0f;
            while (elapsed < stun) { m.Tick(Dt); elapsed += Dt; }
            var wantAfterStun = ViewAnimState.SelectMonster(true, false, false, false);
            Check($"6e 怪物受击硬直（{stun:0.00}s）结束 ⇒ want 回 Idle（受击只出 {m.FrameIndex + 1}/{mKeys.Length} 帧 = 登记项 E28）",
                wantAfterStun == ViewAnim.Idle, $"硬直后 want={wantAfterStun}，受击已出 {m.FrameIndex + 1} 帧");

            // ── 6f 方向换组必须从第 0 帧起（原版 8 方向各一套 .cof）──
            var r = new SpriteAnimator();
            r.Play(ViewAnim.Walk, SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.S),
                SpriteFrames.FpsOf(ViewAnim.Walk), true);
            for (var i = 0; i < 20; i++) r.Tick(Dt);
            var before = r.FrameIndex;
            r.Play(ViewAnim.Walk, SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.E),
                SpriteFrames.FpsOf(ViewAnim.Walk), true);
            Check("6f 同动作换帧键（换朝向）⇒ 从第 0 帧起；同组重复 Play ⇒ 不重置进度",
                before > 0 && r.FrameIndex == 0, $"换向前 {before} ⇒ 换向后 {r.FrameIndex}");
        }

        /// <summary>
        /// 玩家受击的**完整状态链**逐帧驱动：<br/>
        /// ① 受击事件（`ViewModule.PlayHit` 的口径）= `Play(Hit, keys, FpsOf(Hit), LoopOf(Hit))` + `Replay()`；<br/>
        /// ② 每帧按 `ViewAnimState.SelectPlayer(isDead=false, hitAnimPlaying = Playing==Hit &amp;&amp; !Anim.Finished, …)`
        /// 算 want；want 变了才 `Play`（与 `TickPlayer` 的调用口径一致）。<br/>
        /// 返回**显示过的不同受击帧数**。<br/>
        /// <para>`useLegacyChain = true` = **旧口径的反例**（`TickPlayer` 的 want 链里没有受击档，
        /// 于是 `PlayHit` 设的 Hit 同帧被覆盖成 Idle）—— 工程里已经不存在这段逻辑，
        /// 保留它只为证明"上面那条断言能判红"（否则断言可能永远为真 = 空断言）。</para>
        /// </summary>
        private static int SimulatePlayerHit(string[] hitKeys, bool useLegacyChain)
        {
            var anim = new SpriteAnimator();
            var playing = ViewAnim.Idle;
            var seen = new HashSet<int>();

            // 起始：站着 Idle
            anim.Play(ViewAnim.Idle, SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Idle, Dir8.S),
                SpriteFrames.FpsOf(ViewAnim.Idle), true);
            playing = ViewAnim.Idle;

            // ① 受击事件
            anim.Play(ViewAnim.Hit, hitKeys, SpriteFrames.FpsOf(ViewAnim.Hit), SpriteFrames.LoopOf(ViewAnim.Hit));
            anim.Replay();
            playing = ViewAnim.Hit;

            // ② 逐帧（受击动画最长也不到 2 秒）
            for (var f = 0; f < 240; f++)
            {
                // 与 `ViewModule.TickPlayer` 同一个纯函数（不复制判据）
                var hitPlaying = ViewAnimState.IsHitHolding(playing, anim.FrameCount, anim.Finished);
                // 旧链（反例）：`p.IsDead ? Death : CastTimer ? Cast : AttackTimer ? Attack : IsMoving ? … : Idle`
                // —— 本场景里死亡/施法/挥击/移动全为假 ⇒ 旧链恒返回 **Idle**（受击档不存在）
                var want = useLegacyChain
                    ? ViewAnim.Idle
                    : ViewAnimState.SelectPlayer(false, hitPlaying, false, false, false, false);

                if (want != playing)
                {
                    anim.Play(want, SpriteFrames.Keys(PlayerClass.Amazon, want, Dir8.S),
                        SpriteFrames.FpsOf(want), SpriteFrames.LoopOf(want));
                    playing = want;
                }

                if (playing == ViewAnim.Hit) seen.Add(anim.FrameIndex);
                anim.Tick(Dt);
                if (playing == ViewAnim.Hit) seen.Add(anim.FrameIndex);
            }
            return seen.Count;
        }

        // ═════════════════════════════════════════════════════════════════════
        //
        // 判据分两条，缺一不可：
        //   · 新口径（`hitStun || IsHitHolding`）⇒ 该单位受击动作的**每一帧**都上过屏（0..N-1 全在）；
        //   · 旧口径（只按 `MonsterTuning.HitStunSeconds` = 0.18s 保持）⇒ **帧数不全**（E28）。
        //     第二条是**反例断言**：它证明第一条真的在判东西（否则第一条可能恒为真 = 空断言）。
        // ═════════════════════════════════════════════════════════════════════
        private static void Section8MonsterHit()
        {
            Section("8. ★ 片 monster-audio：怪物受击帧号序列（8 类怪物，新口径全帧 / 旧口径不全 = E28）");

            // 8 类怪物 = `MonsterSpawner` 的 AI 映射表（`MonStats.Code`；帧数见生成物 `SpriteFrameCounts`）
            var units = new[] { "fa", "fs", "si", "zm", "cr", "bk", "ye", "wr" };
            var names = new[] { "Fallen", "FallenShaman", "QuillRat", "Zombie", "CorruptRogue", "Brute", "Wraith", "BloodHawk" };

            for (var i = 0; i < units.Length; i++)
            {
                var keys = SpriteFrames.Keys(units[i], ViewAnim.Hit, Dir8.S);
                if (keys == null || keys.Length == 0)
                {
                    Check($"8 {units[i]}（{names[i]}）：受击帧键为空 ⇒ 缺素材（判红）", false, "keys.Length = 0");
                    continue;
                }

                var nowSeq = SimulateMonsterHit(units[i], keys, holdUntilFinished: true);
                var oldSeq = SimulateMonsterHit(units[i], keys, holdUntilFinished: false);

                var full = nowSeq.Count == keys.Length && nowSeq[0] == 0
                           && nowSeq[nowSeq.Count - 1] == keys.Length - 1;
                Check($"8 {units[i]}（{names[i]}）新口径：受击帧序列 {string.Join(",", nowSeq)}"
                      + $" = 全部 {keys.Length} 帧（@ {SpriteFrames.FpsOf(ViewAnim.Hit):0.#}fps = {keys.Length / SpriteFrames.FpsOf(ViewAnim.Hit):0.###}s）",
                    full, $"序列 {string.Join(",", nowSeq)} / 动作共 {keys.Length} 帧");
                Check($"8 {units[i]}（{names[i]}）旧口径反例：只出 {oldSeq.Count}/{keys.Length} 帧"
                      + "（hitStun 0.18s < 动画时长 ⇒ E28「受击只出 3/7 帧」，判红说明新口径非空断言）",
                    oldSeq.Count < keys.Length, $"旧序列 {string.Join(",", oldSeq)}");
            }
        }

        /// <summary>
        /// 怪物受击的**完整状态链**逐帧驱动（与 `ViewModule.UpdateMonster` / `PlayHit` 同一口径）：
        /// ① 受击 ⇒ `Play(Hit, keys, FpsOf(Hit), LoopOf(Hit))` + `Replay()`；
        /// ② 每帧 `want = ViewAnimState.SelectMonster(alive, hitStun || hitHolding, attacking=false, moved=false)`，
        ///    want 变了才 `Play`（与 `UpdateMonster` 的调用口径一致）。
        /// <para>返回**显示过的受击帧号序列**（按首次出现顺序，去重）。</para>
        /// </summary>
        private static List<int> SimulateMonsterHit(string unit, string[] hitKeys, bool holdUntilFinished)
        {
            var anim = new SpriteAnimator();
            var playing = ViewAnim.Idle;
            var seen = new List<int>();
            var uniq = new HashSet<int>();

            anim.Play(ViewAnim.Idle, SpriteFrames.Keys(unit, ViewAnim.Idle, Dir8.S),
                SpriteFrames.FpsOf(ViewAnim.Idle), true);
            playing = ViewAnim.Idle;

            // ① 受击（`ViewModule.PlayHit`）
            anim.Play(ViewAnim.Hit, hitKeys, SpriteFrames.FpsOf(ViewAnim.Hit), SpriteFrames.LoopOf(ViewAnim.Hit));
            anim.Replay();
            playing = ViewAnim.Hit;

            // ② 逐帧（受击动画最长 wr 8 帧 @12fps = 0.67s；跑 600 帧 = 10s 足够）
            const float stun = 0.18f;     // = MonsterTuning.HitStunSeconds
            var elapsed = 0f;
            for (var f = 0; f < 600; f++)
            {
                var hitStun = elapsed < stun;
                var hold = holdUntilFinished
                    && ViewAnimState.IsHitHolding(playing, anim.FrameCount, anim.Finished);
                var want = ViewAnimState.SelectMonster(true, hitStun || hold, false, false);

                if (want != playing)
                {
                    anim.Play(want, SpriteFrames.Keys(unit, want, Dir8.S),
                        SpriteFrames.FpsOf(want), SpriteFrames.LoopOf(want));
                    playing = want;
                }

                if (playing == ViewAnim.Hit && uniq.Add(anim.FrameIndex)) seen.Add(anim.FrameIndex);
                anim.Tick(Dt);
                if (playing == ViewAnim.Hit && uniq.Add(anim.FrameIndex)) seen.Add(anim.FrameIndex);
                elapsed += Dt;
            }
            return seen;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Section7EquipVisual()
        {
            Section("7. 装备外观套：key 规则 · 回退链 · 帧键在盘 · 帧数与生成物一致");

            // ── 7a `KeyOf` 的三条规则 + 规整化 ────────────────────────────────
            var keyCases = new (string W, string S, string Want)[]
            {
                ("jav", "buc", "jav_buc"),   // 有武器有盾 ⇒ {武器}_{盾}
                ("jav", null, "jav"),        // 只有武器 ⇒ {武器}
                (null, "buc", "buc"),        // 只有盾 ⇒ {盾}
                (null, null, null),          // 都没有 ⇒ 徒手（null）
                ("JAV ", " Buc", "jav_buc"), // 配表大小写/空白 ⇒ 规整化成小写
                ("", "  ", null),            // 空串必须与 null 归一（否则会拼出 `/equip//` 这种目录）
            };
            var badKey = new List<string>();
            foreach (var c in keyCases)
            {
                var got = EquipVisual.KeyOf(c.W, c.S);
                if (got != c.Want) badKey.Add($"KeyOf({Show(c.W)},{Show(c.S)})={Show(got)} 期望 {Show(c.Want)}");
            }
            Check($"EquipVisual.KeyOf：三条规则 + 大小写/空白规整（{keyCases.Length} 例）",
                badKey.Count == 0, badKey.Count == 0 ? "全部一致" : string.Join(" | ", badKey));

            // ── 7b 回退链的顺序（去掉盾 → 去掉武器 → 徒手）──────────────────────
            var chainCases = new (string W, string S, string[] Want)[]
            {
                ("jav", "buc", new[] { "jav_buc", "jav", "buc", "徒手" }),
                ("jav", null, new[] { "jav", "徒手" }),
                (null, "buc", new[] { "buc", "徒手" }),
                (null, null, new[] { "徒手" }),
            };
            var badChain = new List<string>();
            foreach (var c in chainCases)
            {
                var got = EquipVisual.Candidates(c.W, c.S);
                var gotS = string.Join(",", Array.ConvertAll(got, Show));
                var wantS = string.Join(",", c.Want);
                if (gotS != wantS) badChain.Add($"Candidates({Show(c.W)},{Show(c.S)})={gotS} 期望 {wantS}");
            }
            Check($"EquipVisual.Candidates：回退链顺序 = 去掉盾 → 去掉武器 → 徒手（{chainCases.Length} 例）",
                badChain.Count == 0, badChain.Count == 0 ? "顺序正确" : string.Join(" | ", badChain));

            // ── 7c `Select` 在**注入的** exists 上的实际选择（= 运行时真走的那条路）──
            var selCases = new (string W, string S, string[] Have, string WantKey, bool WantFell)[]
            {
                ("jav", "buc", new[] { "jav_buc", "jav", "buc" }, "jav_buc", false),
                // 同框套 `jav_buc` 未导（**本机现状**）⇒ 回退到只有武器的 `jav`
                ("jav", "buc", new[] { "jav", "buc" }, "jav", true),
                ("jav", "buc", new[] { "buc" }, "buc", true),
                ("jav", "buc", new string[0], null, true),
                ("jav", null, new[] { "jav" }, "jav", false),
                ("jav", null, new string[0], null, true),
                (null, "buc", new[] { "buc" }, "buc", false),
                // 完全没有装备 ⇒ 徒手，**不算回退**（无装备是正常形态，不是"外观没导"）
                (null, null, new string[0], null, false),
            };
            var badSel = new List<string>();
            foreach (var c in selCases)
            {
                var have = new HashSet<string>(c.Have);
                string wanted;
                bool fell;
                var got = EquipVisual.Select(c.W, c.S, k => have.Contains(k), out wanted, out fell);
                if (Show(got) != Show(c.WantKey) || fell != c.WantFell)
                {
                    badSel.Add($"Select({Show(c.W)},{Show(c.S)} have={string.Join("/", c.Have)})" +
                               $" ⇒ {Show(got)}(fellBack={fell}) 期望 {Show(c.WantKey)}(fellBack={c.WantFell})");
                }
            }
            Check($"EquipVisual.Select：按 exists 逐级回退（{selCases.Length} 例，含本机「同框套未导」的真实现状）",
                badSel.Count == 0, badSel.Count == 0 ? "全部正确" : string.Join(" | ", badSel));

            //   为什么要有这条：修前生产把 `EquipFrameCounts.Has` 直接当 exists 传进 Select，而它的形参是
            //   **unitKey**（`ByUnit` 的键），Select 传进来的是**裸 key** ⇒ 任何 key 都查不到
            //   ⇒ **装备外观套永远回退徒手**（实机：装上武器后仍走 `Chars/{class}/`，还误报"该装备外观未导出"）。
            //   本断言在离线即可判红：生成物里**每一套**都必须能被适配器命中。
            var adapterBad = new List<string>();
            var listed = 0;
            foreach (var kv in EquipFrameCounts.ByUnit)
            {
                var parts2 = kv.Key.Split('/');
                if (parts2.Length != 3) { adapterBad.Add(kv.Key + "：unitKey 形状不是 {class}/equip/{key}"); continue; }
                var clsKey = PlayerClassOf(parts2[0]);
                var bareKey = parts2[2];
                listed++;
                if (!EquipVisual.ExistsAdapter(clsKey)(bareKey))
                {
                    adapterBad.Add($"{kv.Key}：ExistsAdapter({clsKey})({bareKey})=false（生产会错误回退徒手）");
                }
                if (EquipFrameCounts.Has(bareKey))
                {
                    adapterBad.Add($"{bareKey}：EquipFrameCounts.Has(裸 key) 竟为 true ⇒ 本断言的「错位」前提失效，请复核");
                }
            }
            Check($"★ 生产 exists 适配器：生成物里 {listed} 套逐套被 EquipVisual.ExistsAdapter 命中（裸 key → unitKey）",
                listed > 0 && adapterBad.Count == 0,
                adapterBad.Count == 0 ? $"全部命中（{listed} 套）" : string.Join(" | ", adapterBad));

            // 真实装备组合（本机两处都在盘）：barbarian 只有盾 ⇒ 必须选到 buc 套，不是徒手
            string shWanted;
            bool shFell;
            var onlyShield = EquipVisual.Select(null, "buc", EquipVisual.ExistsAdapter(PlayerClass.Barbarian),
                out shWanted, out shFell);
            Check("barbarian 只有盾（buc）⇒ 选到 buc 套（修前会选到 null = 徒手）",
                onlyShield == "buc" && !shFell, $"key={Show(onlyShield)} fellBack={shFell} wanted={Show(shWanted)}");

            //   原写「理想 key hax_buc（本机未登记）⇒ 逐级回退到 hax」—— 那是 `hax_buc` **尚未导出/未登记**
            //   时的事实。主 agent 随后补齐帧数生成物（`gen_equip_frame_counts.py` 现登记 10 套，含
            //   `barbarian/equip/hax_buc` 688 张）⇒ 事实变成「**直接命中 hax_buc、不回退**」。
            //   判据跟着**事实**走；下面**同时保留**「未导出 ⇒ 逐级回退」的语义断言（改用确实未导出的组合测），
            //   所以本改动既没删覆盖、也没放宽 —— 只是把期望值改成了新的真实值。
            string wsWanted2;
            bool wsFell2;
            var weaponShield = EquipVisual.Select("hax", "buc", EquipVisual.ExistsAdapter(PlayerClass.Barbarian),
                out wsWanted2, out wsFell2);
            Check("barbarian 武器+盾（hax+buc）⇒ 命中同框套 hax_buc（已导出+已登记 ⇒ 不回退）",
                weaponShield == "hax_buc" && !wsFell2, $"key={Show(weaponShield)} fellBack={wsFell2} wanted={Show(wsWanted2)}");

            // 回退语义仍要判住：用**确实未导出**的组合（sorceress 的 sst+buc —— 本机无 `sst_buc` 目录）
            // ⇒ 应逐级回退到**武器套 sst**（不是徒手）。这条与原断言等价，只是换了载体。
            string fbWanted2;
            bool fbFell2;
            var fallbackCase = EquipVisual.Select("sst", "buc", EquipVisual.ExistsAdapter(PlayerClass.Sorceress),
                out fbWanted2, out fbFell2);
            Check("sorceress 武器+盾（sst+buc）⇒ 同框套未导出 ⇒ 回退到 sst 套（不是徒手）",
                fallbackCase == "sst" && fbFell2, $"key={Show(fallbackCase)} fellBack={fbFell2} wanted={Show(fbWanted2)}");

            // ── 7d 回退日志文案必须能定位（职业 + 两个 code + 理想 key + 回退 key + "未导出"）──
            var text = EquipVisual.FallbackWarnText(PlayerClass.Amazon, "jav", "buc", "jav_buc", "jav");
            var need = new[] { "Amazon", "jav", "buc", "jav_buc", EquipVisual.MissingReason };
            var missingWord = Array.FindAll(need, w => text.IndexOf(w, StringComparison.Ordinal) < 0);
            Check("回退日志文案含【职业 + 武器 code + 盾 code + 理想 key + 「" + EquipVisual.MissingReason + "」】",
                missingWord.Length == 0,
                missingWord.Length == 0 ? "字段齐全" : "缺：" + string.Join(",", missingWord));

            // ── 7e 徒手 key（null）与"不带 key 的重载"**逐键相同**（没装备 = 行为同改动前）──
            var bareBad = new List<string>();
            foreach (var cls in new[] { PlayerClass.Amazon, PlayerClass.Barbarian, PlayerClass.Sorceress })
            {
                foreach (var act in AllAnims)
                {
                    foreach (var d in AllDirs)
                    {
                        var a = SpriteFrames.Keys(cls, act, d);
                        var b = SpriteFrames.Keys(cls, (string)null, act, d);
                        if (a.Length != b.Length) { bareBad.Add($"{cls}/{act}/{d}：帧数 {a.Length} vs {b.Length}"); continue; }
                        for (var i = 0; i < a.Length; i++)
                        {
                            if (a[i] != b[i]) { bareBad.Add($"{cls}/{act}/{d}[{i}]：{a[i]} vs {b[i]}"); break; }
                        }
                    }
                }
            }
            Check("徒手（key = null）走的重载与改动前**逐键相同**（⛔ 不许悄悄改掉没装备时的表现）",
                bareBad.Count == 0, bareBad.Count == 0 ? "逐键相同" : string.Join(" | ", bareBad.GetRange(0, Math.Min(3, bareBad.Count))));

            // ── 7f 逐套 × 逐动作 × 8 方向 × N 帧：帧键在盘 + 帧数三方一致 ────────
            var setBad = new List<string>();
            var setMissing = new List<string>();
            var totalEquipFiles = 0;
            var sets = 0;
            foreach (var kv in EquipFrameCounts.ByUnit)
            {
                sets++;
                var unitKey = kv.Key;
                var parts = unitKey.Split('/');
                if (parts.Length != 3 || parts[1] != "equip")
                {
                    setBad.Add($"{unitKey}：unitKey 形状不是 {{class}}/equip/{{key}}");
                    continue;
                }
                var cls = PlayerClassOf(parts[0]);
                var equipKey = parts[2];
                var prefix = ResPaths.CharEquipDir(cls, equipKey);
                var subPath = "D2/Chars/" + parts[0] + "/equip/" + equipKey + "/";
                if (prefix != subPath) setBad.Add($"{unitKey}：CharEquipDir={prefix} 磁盘={subPath}");

                // 生成物 × manifest 逐格一致（同一份数据的另一处出处）
                var man = ReadEquipManifest(parts[0], equipKey);
                if (man == null) setBad.Add($"{unitKey}：manifest.json 读不到");
                else
                {
                    for (var ai = 0; ai < AllAnims.Length; ai++)
                    {
                        var actName = AllAnims[ai].ToString().ToLowerInvariant();
                        if (man.TryGetValue(actName, out var mf) && mf != kv.Value[ai])
                        {
                            setBad.Add($"{unitKey}/{actName}：生成物 {kv.Value[ai]} vs manifest {mf}");
                        }
                    }
                }

                for (var ai = 0; ai < AllAnims.Length; ai++)
                {
                    var act = AllAnims[ai];
                    var n = kv.Value[ai];
                    if (n <= 0) { setBad.Add($"{unitKey}/{act}：帧数 {n} ≤ 0"); continue; }
                    foreach (var d in AllDirs)
                    {
                        var keys = SpriteFrames.Keys(cls, equipKey, act, d);
                        if (keys.Length != n) { setBad.Add($"{unitKey}/{act}/{d}：键数 {keys.Length} vs 帧数 {n}"); continue; }
                        var dl = d.ToString().ToLowerInvariant();
                        var actName = act.ToString().ToLowerInvariant();
                        for (var i = 0; i < keys.Length; i++)
                        {
                            var want = prefix + actName + "_" + dl + "_" + i;
                            if (keys[i] != want) { setBad.Add($"{unitKey}/{act}/{d}[{i}]={keys[i]} 期望 {want}"); break; }
                            var path = Path.Combine(_root, "client", "Assets", "Resources", "Clover",
                                (subPath + actName + "_" + dl + "_" + i + ".png").Replace('/', Path.DirectorySeparatorChar));
                            if (!File.Exists(path) || new FileInfo(path).Length <= 0)
                            {
                                setMissing.Add($"{unitKey}/{actName}_{dl}_{i}");
                                continue;
                            }
                            totalEquipFiles++;
                        }
                    }
                    // FrameCountOf 必须按**该套**的真实帧数给（不是徒手那套）
                    if (SpriteFrames.FrameCountOf(unitKey, act) != n)
                    {
                        setBad.Add($"{unitKey}/{act}：FrameCountOf={SpriteFrames.FrameCountOf(unitKey, act)} vs EquipFrameCounts {n}");
                    }
                    if (SpriteFrames.ResolveAnim(unitKey, act) != act)
                    {
                        setBad.Add($"{unitKey}/{act}：ResolveAnim 回退了（该套 7 个动作都有帧，不该回退）");
                    }
                }
            }
            Check($"装备外观套已登记 {sets} 套（键 = {{class}}/equip/{{key}}，与磁盘目录同形）",
                setBad.Count == 0, setBad.Count == 0 ? $"{sets} 套形状/帧数/manifest 三方一致" : string.Join(" | ", setBad.GetRange(0, Math.Min(4, setBad.Count))));
            Check($"装备外观套的帧键逐个在盘且 > 0 字节（共 {totalEquipFiles} 张）",
                setMissing.Count == 0,
                setMissing.Count == 0 ? $"0 缺（{totalEquipFiles} 张）" : "缺 " + setMissing.Count + "：" + string.Join(",", setMissing.GetRange(0, Math.Min(5, setMissing.Count))));

            // ── 7g `EquipVisual.HandOf` × `Equipment.SlotOf` 在整张 `item_c` 上逐行对账 ──
            Check("HandOf × Equipment.SlotOf 逐行对账（整张 item_c）", CheckHandOfAgainstSlotOf(), "见控制台明细");
        }

        /// <summary>
        /// 7g：读 `StreamingAssets/Table/Item.tsv`（打表产物），逐行比较
        /// `EquipVisual.HandOf(source,type,subtype)` 与 `Equipment.SlotOf(row)`：
        /// 两处必须**同义**（武器⟺武器、盾⟺盾、其余 ⇒ HandOf 恒 None）。
        /// </summary>
        private static bool CheckHandOfAgainstSlotOf()
        {
            var path = Path.Combine(_root, "client", "Assets", "StreamingAssets", "Table", "Item.tsv");
            if (!File.Exists(path))
            {
                Console.WriteLine("      !! 找不到 " + path + " ⇒ 本项无法判定");
                return false;
            }

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) { Console.WriteLine("      !! Item.tsv 少于 2 行"); return false; }

            var head = lines[0].Split('\t');
            var col = new Dictionary<string, int>();
            for (var i = 0; i < head.Length; i++) col[head[i].Trim().TrimStart('\ufeff')] = i;
            foreach (var need in new[] { "id", "name", "code", "source", "type", "subtype" })
            {
                if (!col.ContainsKey(need)) { Console.WriteLine("      !! Item.tsv 缺列 " + need); return false; }
            }

            var rows = 0;
            var bad = new List<string>();
            for (var li = 1; li < lines.Length; li++)
            {
                if (string.IsNullOrWhiteSpace(lines[li])) continue;
                var f = lines[li].Split('\t');
                if (f.Length <= col["subtype"]) continue;
                var row = new BaseItemRow
                {
                    Id = int.TryParse(f[col["id"]], out var id) ? id : 0,
                    Name = f[col["name"]],
                    Code = f[col["code"]],
                    Source = f[col["source"]],
                    Type = f[col["type"]],
                    Subtype = f[col["subtype"]],
                };
                rows++;

                var hand = EquipVisual.HandOf(row.Source, row.Type, row.Subtype);
                var slot = Equipment.SlotOf(row);
                var ok = (slot == ItemSlot.Weapon && hand == ItemSlot.Weapon)
                         || (slot == ItemSlot.Shield && hand == ItemSlot.Shield)
                         || (slot != ItemSlot.Weapon && slot != ItemSlot.Shield && hand == ItemSlot.None);
                if (!ok)
                {
                    bad.Add($"{row.Code}(source={row.Source},type={row.Type},subtype={row.Subtype})：" +
                            $"SlotOf={slot} vs HandOf={hand}");
                }
            }

            Console.WriteLine($"      对账 {rows} 行；不一致 {bad.Count} 行" +
                              (bad.Count > 0 ? "：" + string.Join(" | ", bad.GetRange(0, Math.Min(5, bad.Count))) : ""));
            return rows > 0 && bad.Count == 0;
        }

        /// <summary>读装备外观套的 `manifest.json` → 动作名（小写）→ frames。</summary>
        private static Dictionary<string, int> ReadEquipManifest(string cls, string key)
        {
            var path = Path.Combine(_root, "client", "Assets", "Resources", "Clover", "D2",
                "Chars", cls, "equip", key, "manifest.json");
            if (!File.Exists(path)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var outMap = new Dictionary<string, int>();
                if (!doc.RootElement.TryGetProperty("actions", out var acts)) return outMap;
                foreach (var p in acts.EnumerateObject())
                {
                    outMap[p.Name] = p.Value.TryGetProperty("frames", out var fr) ? fr.GetInt32() : 0;
                }
                return outMap;
            }
            catch (Exception e)
            {
                Console.WriteLine("      manifest 解析失败 " + path + "：" + e.Message);
                return null;
            }
        }

        private static string Show(string key) => string.IsNullOrEmpty(key) ? "徒手" : key;

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>读某单位的 `manifest.json`：动作 → (frames, dirs)。</summary>
        private static Dictionary<string, (int Frames, int Dirs)?> ReadManifest(
            (string Disp, string Kind, string Unit, bool IsPlayer, bool IsNpc) u)
        {
            var sub = u.IsPlayer ? "Chars" : "Monsters";
            var path = Path.Combine(_root, "client", "Assets", "Resources", "Clover", "D2", sub, u.Unit, "manifest.json");
            if (!File.Exists(path)) return null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var outMap = new Dictionary<string, (int, int)?>();
                if (!doc.RootElement.TryGetProperty("actions", out var acts)) return outMap;
                foreach (var p in acts.EnumerateObject())
                {
                    var frames = p.Value.TryGetProperty("frames", out var fr) ? fr.GetInt32() : 0;
                    var dirs = p.Value.TryGetProperty("dirs", out var dr) ? dr.GetInt32() : 0;
                    outMap[p.Name] = (frames, dirs);
                }
                return outMap;
            }
            catch (Exception e)
            {
                Console.WriteLine("      manifest 解析失败 " + path + "：" + e.Message);
                return null;
            }
        }

        /// <summary>该 (单位, 动作, 方向) 在磁盘上的帧号（升序）。</summary>
        private static int[] ScanIndices((string Disp, string Kind, string Unit, bool IsPlayer, bool IsNpc) u,
            string action, string dirLower)
        {
            var sub = u.IsPlayer ? "Chars" : "Monsters";
            var dirPath = Path.Combine(_root, "client", "Assets", "Resources", "Clover", "D2", sub, u.Unit);
            if (!Directory.Exists(dirPath)) return Array.Empty<int>();
            var prefix = action + "_" + dirLower + "_";
            var idx = new List<int>();
            foreach (var f in Directory.GetFiles(dirPath, prefix + "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (int.TryParse(name.Substring(prefix.Length), out var i)) idx.Add(i);
            }
            idx.Sort();
            return idx.ToArray();
        }

        /// <summary>该单位的资源子路径（与 `ResPaths.CharDir` / `ResPaths.MonsterDir` 同值）。</summary>
        private static string SubPath((string Disp, string Kind, string Unit, bool IsPlayer, bool IsNpc) u)
        {
            return u.IsPlayer ? "D2/Chars/" + u.Unit + "/" : "D2/Monsters/" + u.Unit + "/";
        }

        private static PlayerClass PlayerClassOf(string unitKey)
        {
            switch (unitKey)
            {
                case "amazon": return PlayerClass.Amazon;
                case "barbarian": return PlayerClass.Barbarian;
                case "sorceress": return PlayerClass.Sorceress;
                case "necromancer": return PlayerClass.Necromancer;
                case "paladin": return PlayerClass.Paladin;
                default:
                    Console.WriteLine("      !! 未知职业键 " + unitKey);
                    return PlayerClass.Amazon;
            }
        }

        private static string Md5(string path)
        {
            using var md5 = MD5.Create();
            using var s = File.OpenRead(path);
            return BitConverter.ToString(md5.ComputeHash(s));
        }

        private static int Last(int[] a) => a.Length == 0 ? -1 : a[a.Length - 1];

        private static int First(int[] a) => a.Length == 0 ? -1 : a[0];

        /// <summary>从宿主可执行目录向上找「含 client/Assets 的那一层」= 仓库根（与其它宿主同一写法）。</summary>
        private static string ResolveProjectRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "client", "Assets"))) return dir.FullName;
                dir = dir.Parent;
            }
            return Directory.GetCurrentDirectory();
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("── " + title);
        }

        private static void Check(string label, bool pass, string detail)
        {
            if (pass) _ok++; else _fail++;
            Console.WriteLine($"[{(pass ? " OK " : "FAIL")}] {label}   ({detail})");
        }
    }
}
