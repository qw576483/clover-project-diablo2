// ─────────────────────────────────────────────────────────────────────────────
//
// 只做**数值与纯逻辑**断言（不依赖 Unity 原生、不进 Play）：
//
// 本宿主不复制任何被测逻辑：链的是 client/Assets/Scripts/** 的真实源码。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.Module.Monster;
using Diablo2.Module.View;
using Vector2Int = UnityEngine.Vector2Int;

namespace MoveCheck
{
    internal static class Program
    {
        private const float Dt = 1f / 60f;      // Unity 常见帧长（60fps）
        private static int _ok;
        private static int _fail;

        private static int Main()
        {
            Console.WriteLine("================ MoveCheck：片 2b 移动手感 / 视野 / 怪物速度 数值自证 ================");

            Section("1. 玩家速度常量（出处：Player.cs:48-49 的 walkSpeed 7 / runSpeed 15 ÷ Iso.SubTileCount=5）");
            Check("GameConst.PlayerWalkSpeed == 3.0（原版跑速 15 map/s ÷ 5）",
                GameConst.PlayerWalkSpeed == 3.0f, $"= {GameConst.PlayerWalkSpeed}");
            Check("GameConst.PlayerWalkSpeedFactor == 7f/15f（原版 walk ÷ run）",
                Math.Abs(GameConst.PlayerWalkSpeedFactor - 7f / 15f) < 1e-6f,
                $"= {GameConst.PlayerWalkSpeedFactor}（{7f / 15f:0.######}）");
            var walkSpeed = GameConst.PlayerWalkSpeed * GameConst.PlayerWalkSpeedFactor;
            Check("走速 = 跑速 × 7/15 == 1.4 格/秒",
                Math.Abs(walkSpeed - 1.4f) < 1e-4f, $"= {walkSpeed:0.######}");
            Check("PlayerModule.WalkSpeedFactor 与 GameConst 同源（不留第二份字面量）",
                Math.Abs(Diablo2.Module.Player.PlayerModule.WalkSpeedFactor - GameConst.PlayerWalkSpeedFactor) < 1e-9f,
                $"{Diablo2.Module.Player.PlayerModule.WalkSpeedFactor} vs {GameConst.PlayerWalkSpeedFactor}");

            Section("2. 相机视野（出处：CameraController.cs:38-41 的 pixelHeight/ppu/2 + Iso.cs:10 的 ppu=80）");
            Check("CameraRig.DefaultOrthographicSize == 3.75（600px / 80ppu / 2）",
                CameraRig.DefaultOrthographicSize == 3.75f, $"= {CameraRig.DefaultOrthographicSize}");
            Check("可见格高 = 2 × size == 7.5 格（1 格 = 1 世界单位）",
                Math.Abs(2f * CameraRig.DefaultOrthographicSize - 7.5f) < 1e-5f,
                $"= {2f * CameraRig.DefaultOrthographicSize}");
            Check("缩放下限/上限未动（3 / 12）",
                CameraRig.MinOrthographicSize == 3f && CameraRig.MaxOrthographicSize == 12f,
                $"min={CameraRig.MinOrthographicSize} max={CameraRig.MaxOrthographicSize}");
            var rig = new CameraRig();
            Check("CameraRig 实例默认正交尺寸 = 3.75（Reset 前）",
                Math.Abs(rig.OrthographicSize - CameraRig.DefaultOrthographicSize) < 1e-4f,
                $"= {rig.OrthographicSize}");

            Section("3. 怪物速度量纲（出处：Iso.cs:9-12 的 SubTileCount=5 + monstats.txt 第 7/21/65 行的 Velocity）");
            Check("MonsterTuning.SpeedToTilesPerSecond == 0.2（1 map 单位 = 1/5 格）",
                MonsterTuning.SpeedToTilesPerSecond == 0.2f, $"= {MonsterTuning.SpeedToTilesPerSecond}");
            Check("MonsterTuning.MinMoveSpeed == 0.2（守卫值，不许把僵尸抬速）",
                MonsterTuning.MinMoveSpeed == 0.2f, $"= {MonsterTuning.MinMoveSpeed}");
            CheckSpeed("Fallen（monstats.txt:21 Velocity=5）", 5, 1.0f);
            CheckSpeed("Zombie（monstats.txt:7 Velocity=1）", 1, 0.2f);
            CheckSpeed("QuillRat（monstats.txt:65 Velocity=3）", 3, 0.6f);

            Section("4. 帧率口径（出处：AnimData.cs:16-37 的每格一循环规律）");
            Check("FpsForCycle(8, 3.0) == 24（跑：8 帧 × 3 格/s）",
                Math.Abs(SpriteFrames.FpsForCycle(8, 3.0f) - 24f) < 1e-4f,
                $"= {SpriteFrames.FpsForCycle(8, 3.0f)}");
            Check("FpsForCycle(8, 1.4) ≈ 11.2（走：8 帧 × 1.4 格/s）",
                Math.Abs(SpriteFrames.FpsForCycle(8, 1.4f) - 11.2f) < 1e-3f,
                $"= {SpriteFrames.FpsForCycle(8, 1.4f)}");
            Check("FpsForCycle(10, 1.0) == 10（怪物样例：僵尸 8 帧 / 堕落者 10 帧 @1.0 格/s）",
                Math.Abs(SpriteFrames.FpsForCycle(10, 1.0f) - 10f) < 1e-4f,
                $"= {SpriteFrames.FpsForCycle(10, 1.0f)}");
            Check("SpeedScaleForCycle(8, 3.0, FpsOf(Run)=24) == 1（基准帧率与目标帧率一致）",
                Math.Abs(SpriteFrames.SpeedScaleForCycle(8, 3.0f, SpriteFrames.FpsOf(ViewAnim.Run)) - 1f) < 1e-4f,
                $"FpsOf(Run)={SpriteFrames.FpsOf(ViewAnim.Run)} scale={SpriteFrames.SpeedScaleForCycle(8, 3.0f, SpriteFrames.FpsOf(ViewAnim.Run)):0.####}");
            Check("SpeedScaleForCycle(8, 1.4, FpsOf(Walk)=12) ≈ 0.9333",
                Math.Abs(SpriteFrames.SpeedScaleForCycle(8, 1.4f, SpriteFrames.FpsOf(ViewAnim.Walk)) - 11.2f / 12f) < 1e-4f,
                $"FpsOf(Walk)={SpriteFrames.FpsOf(ViewAnim.Walk)} scale={SpriteFrames.SpeedScaleForCycle(8, 1.4f, SpriteFrames.FpsOf(ViewAnim.Walk)):0.#####}");
            Check("非法参数（帧数 0 / 速度 0 / 基准帧率 0）⇒ 倍率恒 1（⛔ 不许返回 0 把动画停住）",
                SpriteFrames.SpeedScaleForCycle(0, 3f, 24f) == 1f
                && SpriteFrames.SpeedScaleForCycle(8, 0f, 24f) == 1f
                && SpriteFrames.SpeedScaleForCycle(8, 3f, 0f) == 1f
                && SpriteFrames.SpeedScaleForCycle(-1, -1f, -1f) == 1f,
                "三个非法分支都返回 1");
            Check("IsMoveAnim：只有 Walk/Run 随速度缩放（静态动作一律不缩放）",
                SpriteFrames.IsMoveAnim(ViewAnim.Walk) && SpriteFrames.IsMoveAnim(ViewAnim.Run)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Idle) && !SpriteFrames.IsMoveAnim(ViewAnim.Attack)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Cast) && !SpriteFrames.IsMoveAnim(ViewAnim.Hit)
                && !SpriteFrames.IsMoveAnim(ViewAnim.Death),
                "Walk/Run = true，其余 5 个 = false");

            Section("5. 帧数表与 ViewAnim 下标一致（run 列 = 片 2a 导出产物 / --emit-cs 口径）");
            Check("ViewAnim.Run 的下标 == 6（末尾追加，不插中间）", (int)ViewAnim.Run == 6, $"= {(int)ViewAnim.Run}");
            Check("SpriteFrames.FrameCounts 长度 > Run 下标（兜底数组跟着加了一格）",
                SpriteFrames.FrameCounts.Length > (int)ViewAnim.Run,
                $"length={SpriteFrames.FrameCounts.Length}（Run 下标 {(int)ViewAnim.Run}）");
            Check("兜底帧数表 Run 那格 == 8（亚马逊 RN 真实帧数）",
                SpriteFrames.FrameCounts[(int)ViewAnim.Run] == 8, $"= {SpriteFrames.FrameCounts[(int)ViewAnim.Run]}");
            CheckRunFrames("amazon", 8);
            CheckRunFrames("sorceress", 8);
            CheckRunFrames("necromancer", 8);
            CheckRunFrames("paladin", 8);
            CheckRunFrames("barbarian", 8);
            CheckRunFrames("zm", 8);
            CheckRunFrames("cr", 10);
            CheckRunFrames("fa", 0);
            CheckRunFrames("si", 0);
            CheckRunFrames("ps", 0);
            var runKeys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Run, Dir8.S);
            Check("亚马逊跑的帧键 = 8 个，且前缀是 `run_s_`（原版 RN + 南向）",
                runKeys.Length == 8 && runKeys[0] == ResPaths.CharDir(PlayerClass.Amazon) + "run_s_0"
                && runKeys[7] == ResPaths.CharDir(PlayerClass.Amazon) + "run_s_7",
                $"{runKeys.Length} 个，[0]={runKeys[0]}，[7]={runKeys[7]}");
            var zmRun = SpriteFrames.ResolveAnim("zm", ViewAnim.Run);
            var faRun = SpriteFrames.ResolveAnim("fa", ViewAnim.Run);
            Check("有 RN 的单位请求 Run 不回落（zm ⇒ Run）", zmRun == ViewAnim.Run, "= " + zmRun);
            Check("没有 RN 的单位请求 Run ⇒ 回落 Walk（fa；⛔ 不是占位色块）",
                faRun == ViewAnim.Walk, "= " + faRun);

            Section("6. ★ 有效帧率实测：移动动画帧率 == 帧数 × 速度（真 SpriteAnimator 逐帧推进）");
            MeasureCycle("玩家跑（Run，亚马逊 8 帧 @3.0 格/s ⇒ 24fps）",
                SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Run, Dir8.S), ViewAnim.Run,
                GameConst.PlayerWalkSpeed, 24f);
            MeasureCycle("玩家走（Walk，亚马逊 8 帧 @1.4 格/s ⇒ 11.2fps）",
                SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.S), ViewAnim.Walk,
                walkSpeed, 11.2f);
            MeasureCycle("怪物样例（Walk，堕落者 fa 的 10 帧 @1.0 格/s ⇒ 10fps）",
                SpriteFrames.Keys("fa", ViewAnim.Walk, Dir8.S), ViewAnim.Walk, 1.0f, 10f);
            MeasureCycle("怪物样例（Walk，僵尸 zm 的 12 帧 @0.2 格/s ⇒ 2.4fps，最慢的怪）",
                SpriteFrames.Keys("zm", ViewAnim.Walk, Dir8.S), ViewAnim.Walk, 0.2f, 2.4f);
            Check("静帧动作不缩放：把 SpeedScale 设回 1（ViewModule.SyncMoveScale 的静态分支）后有效帧率 = 基准帧率",
                NonMoveAnimKeepsBaseFps(), "Idle 动作 SpeedScale 恒 1");

            Section7_AnimReset();

            Section8_ExitAndNpc();

            Section9_SortTieBreak();

            Section11_SortFrameSequence();

            Section12_ProjectileSortKey();

            Section10_RetargetSlowDrag();

            Console.WriteLine();
            Console.WriteLine($"================ MoveCheck 结束：通过 {_ok} 项，失败 {_fail} 项 ================");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 10. U33「鼠标在人附近移动没效果，必须要远」：按住左键 + 鼠标**慢慢拖**
        //     这里按真调用方（`Module/Player/PlayerModule.HandleMoveIntent` ② 支）的循环推进：
        //     `ShouldRetarget` 返回 true ⇒ 记下新目标，否则保持旧目标。
        // ═════════════════════════════════════════════════════════════════════
        private static void Section10_RetargetSlowDrag()
        {
            Section("10. ★ U33 按住左键慢拖：鼠标每帧挪 1 格 ⇒ 每一步都要重定标");
            var p = new Vector2Int(20, 20);
            Vector2Int? previous = new Vector2Int(24, 24);      // 按下那一次下发的远端目标

            // 鼠标从 (24,24) 一格一格往回拖到角色脚下（玩家格 P），再往外拖到 P+(0,3)
            var path = new List<Vector2Int>
            {
                new Vector2Int(23, 24), new Vector2Int(22, 24), new Vector2Int(21, 23), new Vector2Int(21, 22),
                new Vector2Int(21, 21), new Vector2Int(20, 21), new Vector2Int(20, 20),   // ← 拖到脚下格
                new Vector2Int(20, 21), new Vector2Int(20, 22), new Vector2Int(20, 23),
            };

            var fired = 0;
            var dead = new List<string>();
            for (var i = 0; i < path.Count; i++)
            {
                var m = path[i];
                if (InputReader.ShouldRetarget(p, previous, m))
                {
                    fired++;
                    previous = m;
                }
                else
                {
                    dead.Add($"第{i + 1}步 M={m}");
                }
            }

            Check($"慢拖 {path.Count} 步（含拖过脚下格）⇒ 每一步都重定标（{fired}/{path.Count}）",
                fired == path.Count && dead.Count == 0,
                $"未重定标 {dead.Count} 步：" + (dead.Count > 0 ? string.Join("，", dead) : "无"));
            Check("拖到脚下格的那一步也算重定标（由 MoveTo 的「已在目标格 ⇒ 清空路径」停下）",
                previous.HasValue && InputReader.ShouldRetarget(p, new Vector2Int(20, 21), new Vector2Int(20, 20)),
                "previous=(20,21) now=脚下格(20,20)");
            Check("同一格不重复重定标（始终保留逐帧节流，不每帧跑 A*）",
                !InputReader.ShouldRetarget(p, new Vector2Int(20, 23), new Vector2Int(20, 23)),
                "previous == now == (20,23)");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// <list type="number">
        /// <item><b>S-08 出口判据同源</b>：`PlayerModule.CheckExit` 用 `IMapModule.Area` 推"目标区域"，
        ///   `AppFlow.EnterArea` 也改判 `map.Area` ⇒ 逐区域逐出口走上去，断言
        ///   「过门请求恰好 1 次 **且** 目标区域 ≠ 当前区域」（自环出口 = 玩家走到出口不换图）。</item>
        /// <item><b>S-19 NPC 站位</b>：站位只来自 `IMapModule.NpcPoints`。城镇里逐格等于地图点位；
        ///   非城镇区域**不装配**（旧实现落 (0,0) ⇒ 洞里靠近原点误开阿卡拉对话）。</item>
        /// </list>
        /// 不复制被测逻辑：只读真模块的公开接口。
        /// </summary>
        private static void Section8_ExitAndNpc()
        {
            Section("8. ★ 片 T：出口判据同源（S-08）+ NPC 站位不落 (0,0)（S-19）");

            var ctx = Diablo2.App.AppContext.Create();
            var map = new Diablo2.Module.Map.MapModule();
            ctx.Map = map;

            // ── S-08：走到出口格 ⇒ 真的发一次过门请求，且目标 ≠ 当前区域 ──
            var player = new Diablo2.Module.Player.PlayerModule();
            player.BindMap(map);
            player.CreateNew(PlayerClass.Amazon, "ExitJudge");
            var fired = new List<AreaId>();
            Action<AreaId> onExit = a => fired.Add(a);
            CloverEngine.Game.Event.On<AreaId>(Diablo2.Core.Events.ExitEntered, onExit);

            var totalExits = 0;
            var selfLoop = 0;
            var noFire = 0;
            var multiFire = 0;
            var unreachable = 0;
            foreach (var area in new[] { AreaId.Town, AreaId.BloodMoor, AreaId.DenOfEvil })
            {
                map.Generate(area, 20260923);
                for (var i = 0; i < map.Exits.Count; i++)
                {
                    var g = map.Exits[i];
                    if (map.TileAt(g) != TileKind.Exit) continue;
                    var from = WalkableNeighbor(map, g);
                    if (from.x < 0) { unreachable++; continue; }   // 出口四面都不可走 ⇒ 用例无效，如实计数
                    totalExits++;

                    fired.Clear();
                    player.TeleportTo(from);
                    player.MoveTo(g);
                    for (var f = 0; f < 200 && player.Grid != g; f++) player.Tick(Dt);

                    if (fired.Count == 0) noFire++;
                    else if (fired.Count > 1) multiFire++;
                    if (fired.Count == 1 && fired[0] == map.Area) selfLoop++;
                }
            }
            CloverEngine.Game.Event.Off<AreaId>(Diablo2.Core.Events.ExitEntered, onExit);

            Check("★S-08 各区域都能枚举到出口（用例有效性：至少 3 个可走上去的出口）",
                totalExits >= 3, $"可走上出口={totalExits}（不可达={unreachable}）");
            Check("★S-08 走到出口格 ⇒ **真的**发一次过门请求（0 次 = 出口静默失效）",
                noFire == 0 && multiFire == 0, $"未触发={noFire} 重复触发={multiFire}");
            Check("★S-08 出口目标区域 **恒 ≠** 当前区域（两道闸门同源 ⇒ 不存在自环出口）",
                selfLoop == 0, $"自环出口={selfLoop}（>0 = 走到出口不换图）");

            // ── S-19：NPC 站位只来自地图点位；非城镇区域不装配 ──
            ctx.Npc = new Diablo2.Module.Npc.NpcModule();

            map.Generate(AreaId.Town, 20260923);
            var defs = ctx.Npc.All;
            var placed = 0;
            var points = map.NpcPoints;
            for (var i = 0; i < defs.Count && i < points.Count; i++)
                if (defs[i].gridX == points[i].x && defs[i].gridY == points[i].y) placed++;
            Check("★S-19 城镇：5 个 NPC 站位**逐格等于** `IMapModule.NpcPoints`（⛔ 无硬编码坐标）",
                defs.Count == 5 && placed == 5 && points.Count >= 5,
                $"defs={defs.Count} placed={placed} points={points.Count}");

            var akara = ctx.Npc.FindNearest(points.Count > 0 ? points[0] : new UnityEngine.Vector2Int(41, 19));
            Check("★S-19 城镇：站在阿卡拉的点位上能找到她（站位真的可交互）",
                akara != null && akara.id == 0, akara == null ? "null" : ("id=" + akara.id + " @" + akara.gridX + "," + akara.gridY));

            var beforeCount = defs.Count;
            map.Generate(AreaId.BloodMoor, 20260923);
            var moor = ctx.Npc.All;
            Check("★S-19 非城镇（BloodMoor）：**一个都不装配**（旧实现 = 5 个落 (0,0) 的幽灵）",
                moor.Count == 0, $"defs={beforeCount}→{moor.Count}");
            Check("★S-19 非城镇：原点附近找不到 NPC（洞里不再误开阿卡拉对话）",
                ctx.Npc.FindNearest(new UnityEngine.Vector2Int(0, 0)) == null, "FindNearest((0,0)) = null");
            Check("★S-19 非城镇：`Interact(0)` 直接拒绝",
                !ctx.Npc.Interact(0), "Interact(0) = false");
        }

        /// <summary>出口格四周找一个可走邻格（找不到返回 (-1,-1)）。</summary>
        private static UnityEngine.Vector2Int WalkableNeighbor(Diablo2.Module.Map.MapModule map,
            UnityEngine.Vector2Int g)
        {
            var dirs = new[]
            {
                new UnityEngine.Vector2Int(1, 0), new UnityEngine.Vector2Int(-1, 0),
                new UnityEngine.Vector2Int(0, 1), new UnityEngine.Vector2Int(0, -1),
            };
            for (var i = 0; i < dirs.Length; i++)
            {
                var n = new UnityEngine.Vector2Int(g.x + dirs[i].x, g.y + dirs[i].y);
                if (map.Walkable(n) && map.TileAt(n) != TileKind.Exit) return n;
            }
            return new UnityEngine.Vector2Int(-1, -1);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. 移动抖动（R1-D · 候选②「动画被反复打回第 0 帧」的离线判定）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 候选② = "8 向 A* 锯齿路径频繁换向 ⇒ 走路动画反复从第 0 帧重开（腿打颤）"。
        /// 本段用**真源码**逐条判定（不复制任何被测逻辑）：
        /// <list type="bullet">
        /// <item>d1：<see cref="SpriteAnimator.Play"/> 的早退判据 ⇒ **同动作 + 同帧键重复调用不重置进度**；</item>
        /// <item>d2：换帧键（换朝向）**必须**从第 0 帧起（原版 8 方向各一套 `.cof`）；</item>
        /// <item>d3：真 <c>PlayerModule</c>（真 A* 路径）逐帧按 <c>ViewModule.cs:710</c> 的判据
        /// （`dirChanged || want != Playing`）驱动真 <see cref="SpriteAnimator"/>，统计
        /// 「调用次数 / 复位次数 / 同组调用次数」。</item>
        /// </list>
        /// </summary>
        private static void Section7_AnimReset()
        {
            Section("7. ★ 动画复位口径（R1-D / 候选②）：同组同状态不重置进度、换组必须从第 0 帧起");

            var keysS = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Run, Dir8.S);
            var fpsRun = SpriteFrames.FpsOf(ViewAnim.Run);

            var a = new SpriteAnimator();
            a.Play(ViewAnim.Run, keysS, fpsRun, true);
            for (var i = 0; i < 6; i++) a.Tick(Dt);
            var advanced = a.FrameIndex;
            a.Play(ViewAnim.Run, keysS, fpsRun, true);            // 同组重复调用（调用侧每帧都判、命中就调）
            Check("d1 同动作 + 同帧键重复 Play ⇒ 帧号**不**回第 0 帧（进度保留）",
                advanced > 0 && a.FrameIndex == advanced,
                $"推进到 frame={advanced}/{a.FrameCount}，重复 Play 后 frame={a.FrameIndex}（早退判据生效）");

            var keysE = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Run, Dir8.E);
            a.Play(ViewAnim.Run, keysE, fpsRun, true);            // 换朝向 = 帧键整组换掉
            Check("d2 同动作但换帧键（换朝向）⇒ **必须**从第 0 帧起（原版 8 方向各一套 .cof）",
                a.FrameIndex == 0 && a.Anim == ViewAnim.Run && a.FrameCount == keysE.Length,
                $"frame={a.FrameIndex}/{a.FrameCount} fps={fpsRun:0.#} loop={a.Loop}");

            // ── d3：真 PlayerModule（真 A* 路径）+ 真 SpriteAnimator 逐帧驱动 ──
            var err = Table.TableLoader.LoadAll(null, ResolveProjectRoot() + @"\client\Assets");
            Check("配表已加载（class_c 是 PlayerModule.CreateNew 的来源）", err == null,
                err ?? ("dir=" + Table.TableLoader.LastDir));

            var map = new Diablo2.Module.Map.MapModule();
            map.Generate(AreaId.Town, 20260920);
            var player = new Diablo2.Module.Player.PlayerModule();
            player.BindMap(map);
            player.CreateNew(PlayerClass.Amazon, "AnimReset");
            player.SetRunning(true);
            player.TeleportTo(map.SpawnPoint);
            var target = FarWalkable(map, map.SpawnPoint, 14);
            player.MoveTo(target);

            var anim = new SpriteAnimator();
            var playing = ViewAnim.Idle;
            var dir = Dir8.S;
            string[] curKeys = null;
            var frames = 0;
            var calls = 0;            // PlayAnim 调用次数（`ViewModule.cs:710` 判据成立的次数）
            var resets = 0;           // 其中**真的**复位到第 0 帧的次数
            var sameGroupCalls = 0;   // 「同动作 + 同帧键」的调用（候选② 说的缺陷 ⇒ 必须 0）
            var dirChanges = 0;
            var actionChanges = 0;

            for (var f = 0; f < 4000 && player.IsMoving; f++)
            {
                player.Tick(Dt);
                frames++;

                var want = player.IsMoving ? (player.Running ? ViewAnim.Run : ViewAnim.Walk) : ViewAnim.Idle;
                var dirChanged = player.Dir != dir;
                if (dirChanged) { dirChanges++; dir = player.Dir; }
                var actionChanged = want != playing;
                if (actionChanged) { actionChanges++; playing = want; }

                if (!dirChanged && !actionChanged) { anim.Tick(Dt); continue; }   // 调用侧不 Play，只推进

                calls++;
                var next = SpriteFrames.Keys(player.Class, want, dir);
                // 「Play 会不会复位」= 早退判据取反（同动作 + 同帧键 + 有帧 + 未播完 ⇒ 原样返回、不复位）
                var willReset = !(want == anim.Anim && SameKeys(curKeys, next) && next.Length > 0 && !anim.Finished);
                if (willReset) resets++; else sameGroupCalls++;

                anim.Play(want, next, SpriteFrames.FpsOf(want), SpriteFrames.LoopOf(want));
                curKeys = next;
                anim.Tick(Dt);
            }

            Check("d3a 走完一条真 A* 路径，且是锯齿路径（朝向变化 ≥ 2 次 ⇒ 用例有效）",
                dirChanges >= 2 && player.Grid == target,
                $"路径 {player.Motor.LastSteps} 格 / {frames} 帧 / 朝向变化 {dirChanges} 次 / 终点 {player.Grid}（目标 {target}）");
            Check("d3b PlayAnim 调用次数 **远小于** 帧数（否证「每帧被打回第 0 帧」）",
                calls > 0 && calls < frames / 4,
                $"调用 {calls} 次 / {frames} 帧 = {100.0 * calls / Math.Max(1, frames):0.##}% 的帧才换动画");
            Check("d3c **不存在**「同动作 + 同帧键」的调用（⇒ 候选②「同组同状态被重置」不成立）",
                sameGroupCalls == 0, $"同组调用 {sameGroupCalls} 次（若 > 0 = 真缺陷）");
            Check("d3d 每次调用都伴随真实状态变化（动作或朝向变）⇒ 复位都是必要的",
                resets == calls && dirChanges + actionChanges >= calls,
                $"调用 {calls} = 真复位 {resets}；朝向变化 {dirChanges} + 动作变化 {actionChanges}");
        }

        /// <summary>两组帧键是否**逐元素相等**（与 `SpriteAnimator.SameKeys` 同口径；键是值比较）。</summary>
        private static bool SameKeys(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            return true;
        }

        /// <summary>找离 <paramref name="from"/> 最远且**非出入口**的可走格（锯齿路径用例的目标）。</summary>
        private static UnityEngine.Vector2Int FarWalkable(Diablo2.Module.Map.MapModule map,
            UnityEngine.Vector2Int from, int minDist)
        {
            var best = from;
            var bestD = -1;
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new UnityEngine.Vector2Int(x, y);
                    if (!map.Walkable(g) || map.TileAt(g) == TileKind.Exit) continue;
                    if (g == from) continue;
                    var d = Iso.GridDistance(from, g);
                    if (d < minDist || d <= bestD) continue;
                    best = g;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>从宿主可执行目录向上找「含 client/Assets 的那一层」= 仓库根（与其它宿主同一套写法）。</summary>
        private static string ResolveProjectRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return System.IO.Directory.GetCurrentDirectory();
        }

        /// <summary>官方 `Velocity` → 格/秒（与 `MonsterModule` 的换算同一份常量）。</summary>
        private static void CheckSpeed(string who, int velocity, float expect)
        {
            var v = velocity * MonsterTuning.SpeedToTilesPerSecond;
            var speed = v < MonsterTuning.MinMoveSpeed ? MonsterTuning.MinMoveSpeed : v;
            Check($"{who} ⇒ {expect} 格/秒", Math.Abs(speed - expect) < 1e-4f,
                $"Velocity {velocity} × {MonsterTuning.SpeedToTilesPerSecond} = {speed:0.###} 格/秒");
        }

        private static void CheckRunFrames(string unitKey, int expect)
        {
            var n = SpriteFrameCountsRun(unitKey);
            Check($"帧数表 run 列：{unitKey} = {expect}", n == expect, $"= {n}");
        }

        /// <summary>
        /// 读 `SpriteFrameCounts.Of(unit, ViewAnim.Run)`（`SpriteFrameCounts` 是 internal，
        /// 本宿主与业务源码编进同一程序集 ⇒ 直接可见）。
        /// </summary>
        private static int SpriteFrameCountsRun(string unitKey) => SpriteFrameCounts.Of(unitKey, ViewAnim.Run);

        /// <summary>
        /// 核心实测：用真 <see cref="SpriteAnimator"/> 播某动作，倍率按
        /// `SpeedScaleForCycle(帧数, 速度, 基准帧率)` 设置，逐帧 `Tick(Dt)` 跑 <paramref name="seconds"/> 秒，
        /// 数出**帧号变化的次数** ⇒ 实测帧率，与 `帧数 × 速度` 比。
        /// </summary>
        private static void MeasureCycle(string label, string[] keys, ViewAnim anim,
            float tilesPerSecond, float expectFps)
        {
            const float seconds = 5f;
            var frames = keys.Length;
            var baseFps = SpriteFrames.FpsOf(anim);
            var scale = SpriteFrames.SpeedScaleForCycle(frames, tilesPerSecond, baseFps);

            var anim2 = new SpriteAnimator();
            anim2.Play(anim, keys, baseFps, true);
            anim2.SpeedScale = scale;

            // ① 公式层：有效帧率 = 基准帧率 × 倍率，必须等于 帧数 × 速度
            var effective = baseFps * scale;
            Check($"{label} 有效帧率 = 基准 × 倍率 == 帧数 × 速度",
                Math.Abs(effective - expectFps) < 0.01f && Math.Abs(effective - SpriteFrames.FpsForCycle(frames, tilesPerSecond)) < 0.01f,
                $"基准 {baseFps:0.##}fps × {scale:0.#####} = {effective:0.####}fps（期望 帧数 {frames} × 速度 {tilesPerSecond} = {expectFps}）");

            // ② 实跑层：逐帧推进 5 秒，数帧号变化次数
            var steps = (int)Math.Round(seconds / Dt);
            var advances = 0;
            var last = anim2.FrameIndex;
            for (var i = 0; i < steps; i++)
            {
                anim2.Tick(Dt);
                if (anim2.FrameIndex != last)
                {
                    advances++;
                    last = anim2.FrameIndex;
                }
            }

            var measured = advances / seconds;
            var expectAdvance = expectFps * seconds;
            Check($"{label} 5 秒内帧号前进 {expectAdvance:0} 次（±1 帧量化）",
                Math.Abs(advances - expectAdvance) <= 1f,
                $"实测 {advances} 次 ⇒ 帧率 {measured:0.###}fps（期望 {expectFps}fps；共 Tick {steps} 次 @ dt={Dt:0.#####}）");
        }

        /// <summary>静态动作（Idle）不该被缩放：倍率恒 1 时 5 秒前进次数 = 基准帧率 × 5。</summary>
        private static bool NonMoveAnimKeepsBaseFps()
        {
            var keys = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Idle, Dir8.S);
            var baseFps = SpriteFrames.FpsOf(ViewAnim.Idle);
            var a = new SpriteAnimator();
            a.Play(ViewAnim.Idle, keys, baseFps, true);
            if (!MathfApprox(a.SpeedScale, 1f)) return false;      // 新建实体的默认倍率

            var steps = 300;                                       // 5 秒
            var advances = 0;
            var last = a.FrameIndex;
            for (var i = 0; i < steps; i++)
            {
                a.Tick(Dt);
                if (a.FrameIndex != last) { advances++; last = a.FrameIndex; }
            }
            var expect = baseFps * 5f;
            return Math.Abs(advances - expect) <= 1f;
        }

        // ═════════════════════════════════════════════════════════════════════
        //    判据（数值类，秒级，不进 Play）：
        //      ① 纯函数：100 次重复调用结果恒定；
        //      ② 同格 4 个实体的次级键两两不等（比较键唯一 ⇒ 次序确定，不依赖渲染器提交顺序）；
        //      ③ 100 次比较的**次序完全一致**，且次序 = 玩家 > NPC > 怪物 > 地面物品。
        //    只加断言，不改既有判据。
        // ═════════════════════════════════════════════════════════════════════
        private static void Section9_SortTieBreak()
        {
            Section("9. ★ 片 M3：实体同格排序的确定性 tie-break（重复调用恒定 + 玩家压 NPC）");

            var grid = new Vector2Int(20, 25);
            var sameOrder = Iso.EntitySortOrder(grid, false);   // 同一格 ⇒ sortingOrder 必然相等
            var ids = new[] { GameConst.PlayerEntityId, -1, 1001, GameConst.GroundItemIdBase + 1 };
            var names = new[] { "玩家", "NPC(阿卡拉)", "怪物#1001", "地面物品#100001" };

            // ① 纯函数：同一 id 反复调用得到同一个值（100 次）
            var pure = true;
            for (var it = 0; it < 100; it++)
            {
                for (var k = 0; k < ids.Length; k++)
                {
                    if (ViewModule.SortTieZ(ids[k]) != ViewModule.SortTieZ(ids[k])) pure = false;
                }
            }
            Check("SortTieZ 是纯函数（100 次重复调用结果恒定）", pure,
                $"z(玩家) = {ViewModule.SortTieZ(ids[0]):0.0000}");

            // ② 同格 4 个实体的次级键两两不等
            var zs = new float[ids.Length];
            for (var k = 0; k < ids.Length; k++) zs[k] = ViewModule.SortTieZ(ids[k]);
            var distinct = true;
            for (var a = 0; a < zs.Length; a++)
            {
                for (var b = a + 1; b < zs.Length; b++)
                {
                    if (Math.Abs(zs[a] - zs[b]) < 1e-6f) distinct = false;
                }
            }
            var zlist = "";
            for (var k = 0; k < zs.Length; k++) zlist += (k > 0 ? " " : "") + names[k] + "=" + zs[k].ToString("0.0000");
            Check("同格 4 个实体的 z 次级键两两不等（比较键唯一 ⇒ 次序确定）", distinct, zlist);

            // ③ 100 次比较的次序恒定，且 = 玩家 > NPC > 怪物 > 地面物品
            //    等 sortingOrder 时：z 小者离（正交）相机近 ⇒ 画在前面。
            var expect = new[] { 0, 1, 2, 3 };
            var stable = true;
            var lastOrder = new int[ids.Length];
            for (var it = 0; it < 100; it++)
            {
                var order = new int[ids.Length];
                for (var k = 0; k < ids.Length; k++) order[k] = k;
                for (var a = 0; a < order.Length; a++)
                {
                    for (var b = a + 1; b < order.Length; b++)
                    {
                        if (zs[order[b]] < zs[order[a]])
                        {
                            var t = order[a]; order[a] = order[b]; order[b] = t;
                        }
                    }
                }
                for (var k = 0; k < order.Length; k++)
                {
                    lastOrder[k] = order[k];
                    if (order[k] != expect[k]) stable = false;
                }
            }
            var actual = "";
            for (var k = 0; k < lastOrder.Length; k++)
                actual += (k > 0 ? " > " : "") + names[lastOrder[k]];
            Check("同格比较 100 次次序恒定，且 = 玩家 > NPC > 怪物 > 地面物品", stable,
                $"sortingOrder={sameOrder}；次序 = {actual}");
            Check("玩家在 NPC 之前（用户症状：同格时不许闪）", zs[0] < zs[1],
                $"玩家 z={zs[0]:0.0000} < NPC z={zs[1]:0.0000}");

            //    纯函数 ⇒ 直接断言；出处见 `Module/Map/MapSeam.cs` 文件头。
            var townW = GameConst.TownWidth;
            Check("接缝判据：城镇东边界列上的桥面格 = 接缝（真）",
                Diablo2.Module.Map.MapSeam.IsTownEastSeam(AreaId.Town, townW,
                    new Vector2Int(townW - 1, 25), true),
                $"Area=Town x={townW - 1} (y=25) deck=true");
            Check("接缝判据：东边界列但**不是**桥面 ⇒ 不是接缝（假）",
                !Diablo2.Module.Map.MapSeam.IsTownEastSeam(AreaId.Town, townW,
                    new Vector2Int(townW - 1, 25), false),
                "deck=false ⇒ 河对岸孤立窄条不算接缝（登记成出口会让连通性自检必然失败）");
            Check("接缝判据：桥面但**不在**东边界列 ⇒ 不是接缝（假）",
                !Diablo2.Module.Map.MapSeam.IsTownEastSeam(AreaId.Town, townW,
                    new Vector2Int(townW - 2, 25), true),
                $"x={townW - 2} ⇒ 桥中部不是接缝");
            Check("接缝判据：野外区域 ⇒ 不是接缝（假）",
                !Diablo2.Module.Map.MapSeam.IsTownEastSeam(AreaId.BloodMoor, townW,
                    new Vector2Int(townW - 1, 25), true),
                "Area=BloodMoor ⇒ 只有城镇那条共享边列是接缝");

            //    只加断言：M3 已定稿的接缝连通性判定 / 桥面可走掩码 / 实体排序口径一律原样不动。
            var entryN = Diablo2.Module.Map.MapGenWilderness.EntryMarginCells;
            //    `MapModule` 是 internal ⇒ 与 mapcheck 同口径：本宿主把同一批源码编进本程序集后直接 new。
            var bm = new Diablo2.Module.Map.MapModule();
            bm.Generate(AreaId.BloodMoor, 20250916);
            var sp = bm.SpawnPoint;
            var margin = Math.Min(Math.Min(sp.x, bm.Width - 1 - sp.x),
                                  Math.Min(sp.y, bm.Height - 1 - sp.y));
            Check("过接缝进荒野：落点距四边界 ≥ N 格（N = 实测可见半跨 7 + 1 备用）",
                margin >= entryN,
                $"落点=({sp.x},{sp.y}) 地图={bm.Width}x{bm.Height} 最小边距={margin} N={entryN}");
            Check("过接缝进荒野：落点所在格可走", bm.Walkable(sp), $"落点=({sp.x},{sp.y})");
            Check("过接缝进荒野：落点仍在回城口那条土路上（原版「西门进、站土路」语义）",
                bm.Exits.Count > 0 && sp.y == bm.Exits[0].y && bm.FindPath(sp, bm.Exits[0]) != null,
                $"回城口={bm.Exits[0]} 落点={sp}");
        }

        // ═════════════════════════════════════════════════════════════════════
        //     台账 = `策划/自审对比/bug清单.md:83` / `:93`，两条都卡在「V5 单帧判不了，缺多帧序列」
        //     ⇒ 本节把「多帧序列」补上（仍是离线数值断言，秒级，不进 Play）。
        //
        //     **判过程不判结果**（一条能靠"改个数字"变绿的检查项等于没判）：
        //       · Unity 的绘制次序 = `(sortingLayer, sortingOrder, 视图轴距离)` 三级键。本项目相机
        //         正交 + rotation=identity + 机位 z 恒 = `-CameraRig.CameraDistance` ⇒ 第三级
        //         **就是实体节点的 z**（口径出处 = Unity 文档 `TransparencySortMode`：
        //         "orthographic cameras sort based on distance along the view direction"）。
        //       · ⇒ 「次序是否由键唯一确定」这一**过程** = **主键相等时第三键必须不相等**。
        //         旧代码（z 恒 0）在同 `gx+gy` 位形下两级键**全等** ⇒ 引擎没有任何决胜依据
        //         ⇒ 用户看到「一会人物一会 npc」。
        //     本节三段：
        //       ⓐ 先证明**旧口径必然不可判别**（红样本 = 闸门极性自检：探针确实能变红）；
        //       ⓑ 再证明现口径在**三种位形 × 60 帧**上零不可判别、次序恒定、且不违反主键优先；
        //       ⓒ 换另一条候选裁决口径（Perspective：到相机位置的距离）复核，证明结论**不依赖**
        //         Unity 到底把 `TransparencySortMode.Default` 解析成哪一种。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>每种位形的采样帧数（≥30；dt = 1/60 ⇒ 60 帧 = 1 秒 ≈ 3 格路程）。</summary>
        private const int Frames11 = 60;

        /// <summary>相机滞后扫描（世界单位）—— 正交口径下相机 xy 不参与排序，这里用它复核 Perspective 口径。</summary>
        private static readonly float[] LagSweep = { -3f, -2f, -1f, 0f, 1f, 2f, 3f };

        private static void Section11_SortFrameSequence()
        {
            Section($"11. ★ U26/U36：排序键的逐帧序列（三种位形 × {Frames11} 帧；含旧口径红样本）");

            // ── 真实输入：城镇地图（真 seed） + 真 NPC 站位 + 真 A* + 真 PlayerMotor ────────────
            var town = new Diablo2.Module.Map.MapModule();
            town.Generate(AreaId.Town, 0);
            var npcGrid = town.NpcPoints[(int)NpcId.Akara];

            // NPC 实体 id 走**生产口径**（`ViewModule.NpcEntityId` 是 private ⇒ 反射取，不另写一份）
            var npcIdMethod = typeof(ViewModule).GetMethod("NpcEntityId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var npcEntityId = npcIdMethod != null
                ? (int)npcIdMethod.Invoke(null, new object[] { NpcId.Akara })
                : 0;
            Check("NPC 实体 id == 生产口径 `ViewModule.NpcEntityId`（负号区段，与玩家 1 / 怪物 1000+ / 物品 100000+ 不冲突）",
                npcEntityId == -1 - (int)NpcId.Akara,
                $"NpcId.Akara={(int)NpcId.Akara} ⇒ entityId={npcEntityId}；出生点={town.SpawnPoint} " +
                $"阿卡拉格=({npcGrid.x},{npcGrid.y}) walkable={town.Walkable(npcGrid)}");

            // ── ⓒ-前置：第三键的口径（相机距离裁决）──────────────────────────────────────
            var camZ = -CameraRig.CameraDistance;
            var camNear = CameraRig.CameraPosForFocus(Iso.GridToWorld(0, 0), town.Width, town.Height,
                CameraRig.DefaultOrthographicSize, 16f / 9f, camZ);
            var camFar = CameraRig.CameraPosForFocus(Iso.GridToWorld(town.Width - 1, town.Height - 1),
                town.Width, town.Height, CameraRig.DefaultOrthographicSize, 16f / 9f, camZ);
            Check("相机机位 z 与焦点无关 ⇒ 正交排序距离只由实体 z 决定（与跟焦/插值抖动无关）",
                Math.Abs(camNear.z - camZ) < 1e-6f && Math.Abs(camFar.z - camZ) < 1e-6f,
                $"焦点(0,0)⇒z={camNear.z:0.###}；焦点对角落⇒z={camFar.z:0.###}；期望 {camZ:0.###}");

            // ── 真走位（真 A* 路径 + 真 PlayerMotor 逐帧推进）────────────────────────────
            var path = FindWalkPath(town, town.SpawnPoint, npcGrid);
            Check("拿到一条真实 A* 路径（出生点 → 阿卡拉站位）",
                path != null && path.Count >= 2,
                path == null ? "FindPath 返回 null（后续断言无法成立）" : $"{path.Count - 1} 步" +
                    $"（起 ({path[0].x},{path[0].y}) 终 ({path[path.Count - 1].x},{path[path.Count - 1].y})）");
            if (path == null || path.Count < 2)
            {
                Console.WriteLine("      ⇒ 没有合法路径：本节其余断言**不成立**（⛔ 不是跳过项）");
                return;
            }

            var motor = new Diablo2.Module.Player.PlayerMotor();
            motor.Teleport(town.SpawnPoint, town);
            motor.SetPath(path, path[path.Count - 1]);

            var gridSeq = new List<Vector2Int>();
            var worldSeq = new List<UnityEngine.Vector3>();
            for (var f = 0; f < Frames11; f++)
            {
                motor.Tick(Dt, town);
                gridSeq.Add(motor.Grid);
                worldSeq.Add(motor.World);
            }

            var gridJumps = 0;
            var orderChanges = 0;
            for (var f = 1; f < Frames11; f++)
            {
                if (gridSeq[f] != gridSeq[f - 1]) gridJumps++;
                if (ViewModule.EntitySortOrder(gridSeq[f]) != ViewModule.EntitySortOrder(gridSeq[f - 1]))
                    orderChanges++;
            }
            // 「重算时机」的完备性：同一个格**任何时刻**都必须得到同一个排序键（帧号/时间/世界坐标都不许参与）
            var orderMismatch = 0;
            for (var f = 0; f < Frames11; f++)
            {
                for (var g = f + 1; g < Frames11; g++)
                {
                    if (gridSeq[g] != gridSeq[f]) continue;
                    if (ViewModule.EntitySortOrder(gridSeq[g]) != ViewModule.EntitySortOrder(gridSeq[f]))
                        orderMismatch++;
                }
            }
            Check("走位序列不是空跑（帧数 ≥ 30，且真的跨了格）",
                gridJumps > 0 && (worldSeq[Frames11 - 1] - worldSeq[0]).magnitude > 1f,
                $"跨格 {gridJumps} 次；世界位移 " +
                $"{(worldSeq[Frames11 - 1] - worldSeq[0]).magnitude:0.###} 世界单位；格序列 " +
                $"({gridSeq[0].x},{gridSeq[0].y})→({gridSeq[Frames11 - 1].x},{gridSeq[Frames11 - 1].y})");
            Check("排序键是格的**纯函数**（同格恒同值）⇒ 「只在格变化时重算 sortingOrder」不会漏",
                orderMismatch == 0,
                $"同格异值 {orderMismatch} 例（60 帧里跨格 {gridJumps} 次；若键还是帧号/世界坐标的函数就会漏 ⇒ 表现为永久错档）");
            Check("★ 主键相等的区间**可以持续整段走位**（不是单帧偶发）",
                gridJumps > 0 && orderChanges == 0,
                $"格序列沿**等 gx+gy 对角线**推进（跨 {gridJumps} 格），主键变化 {orderChanges} 次 " +
                $"(恒 = {ViewModule.EntitySortOrder(gridSeq[0])}) ⇒ 整段走位每帧都要靠第三键决胜；" +
                "旧口径 ⇒ 整段都在交替，正是用户说的「闪**一会**」而不是「闪一帧」");

            // ── 三种位形（都是「主键恒相等」的关系；位形由格偏移定义，不是编出来的坐标）──────
            var shapes = new[] { new Vector2Int(0, 0), new Vector2Int(1, -1), new Vector2Int(2, -2) };
            var shapeNames = new[] { "1 严格同格", "2 相邻格同 y", "3 同 y 不同格" };
            var shapeGaps = new[] { "世界 Δ=(0,0)", "世界 Δ=(2,0)", "世界 Δ=(4,0)" };

            for (var s = 0; s < shapes.Length; s++)
            {
                var off = shapes[s];
                var samePrimary = 0;
                var undecidableNew = 0;
                var undecidableOld = 0;
                var flips = 0;
                var frontPlayer = 0;
                var perspDisagree = 0;
                var perspChecks = 0;
                var prevFront = 0;
                var raw = new System.Text.StringBuilder();
                var sampleFirst = "";
                var sampleLast = "";

                for (var f = 0; f < Frames11; f++)
                {
                    var pGrid = gridSeq[f];
                    var pWorld = worldSeq[f];
                    var nGrid = new Vector2Int(pGrid.x + off.x, pGrid.y + off.y);
                    var nWorld = Iso.GridToWorld(nGrid);      // NPC 静态（建视图时就是这个格心）

                    var pOrder = ViewModule.EntitySortOrder(pGrid);
                    var nOrder = ViewModule.EntitySortOrder(nGrid);
                    // 两条候选口径必须用**同一个实际 transform 位置**（含 z 次级键）——
                    //    第三键取错基准（拿裸世界坐标而不是 `EntityWorld` 的结果）会得出假结论。
                    var pPos = ViewModule.EntityWorld(GameConst.PlayerEntityId, pWorld);
                    var nPos = ViewModule.EntityWorld(npcEntityId, nWorld);
                    var pz = pPos.z;
                    var nz = nPos.z;

                    if (pOrder == nOrder) samePrimary++;
                    var front = FrontOf(pOrder, pz, nOrder, nz);
                    if (front == 0) undecidableNew++;
                    if (front > 0) frontPlayer++;
                    if (pOrder == nOrder && prevFront != 0 && front != prevFront) flips++;
                    if (front != 0) prevFront = front;

                    if (FrontOf(pOrder, pWorld.z, nOrder, nWorld.z) == 0) undecidableOld++;

                    // ⓒ 另一条候选口径：Perspective（到相机**位置**的距离），含相机滞后扫描
                    for (var li = 0; li < LagSweep.Length; li++)
                    {
                        var lag = LagSweep[li];
                        var camX = new UnityEngine.Vector3(pPos.x + lag, pPos.y, camZ);
                        var camY = new UnityEngine.Vector3(pPos.x, pPos.y + lag, camZ);
                        var fx = FrontOf(pOrder, (pPos - camX).magnitude, nOrder, (nPos - camX).magnitude);
                        var fy = FrontOf(pOrder, (pPos - camY).magnitude, nOrder, (nPos - camY).magnitude);
                        perspChecks += 2;
                        if (fx != front || fy != front) perspDisagree++;
                    }

                    raw.Append(front > 0 ? "+" : (front < 0 ? "-" : "?"));
                    var line = $"[{pOrder},{nOrder}]({pz:0.0000},{nz:0.0000})";
                    if (f == 0) sampleFirst = line;
                    if (f == Frames11 - 1) sampleLast = line;
                }

                Console.WriteLine($"  ── 位形 {shapeNames[s]}：偏移格 ({off.x},{off.y})，{shapeGaps[s]}");
                Console.WriteLine($"     逐帧「谁在前」原始读数（+ = 玩家在前，- = NPC 在前，? = 三级键全等=未定义）：");
                Console.WriteLine($"     {raw}");
                Console.WriteLine($"     首帧键 (pOrder,nOrder)(pz,nz) = {sampleFirst}；末帧 = {sampleLast}");

                // ⓑ 现口径：零不可判别 + 次序恒定 + 判定 = 玩家（z 小者离正交相机近 ⇒ 画在前）
                Check($"位形 {shapeNames[s]}：主键恒相等（{samePrimary}/{Frames11} 帧）⇒ 逐帧走的都是「第三键决胜」这条路",
                    samePrimary == Frames11,
                    $"主键相等 {samePrimary} / {Frames11} 帧（gx+gy 相同 ⇒ 必然相等）");
                Check($"位形 {shapeNames[s]}：**零**不可判别帧（三级键全等 = 次序未定义 = 闪的充要前置）",
                    undecidableNew == 0,
                    $"现口径不可判别 {undecidableNew} 帧；旧口径（z 无次级键）不可判别 {undecidableOld} 帧");
                Check($"位形 {shapeNames[s]}：【红样本·闸门极性】旧口径必然不可判别（本节判据确实能变红）",
                    undecidableOld == samePrimary && undecidableOld > 0,
                    $"旧口径不可判别 {undecidableOld} / {samePrimary} 帧 ⇒ 无决胜键，次序交给引擎内部提交顺序");
                Check($"位形 {shapeNames[s]}：相邻帧绘制次序翻转 {flips} 次（要求 0）+ 判定恒为玩家在前",
                    flips == 0 && frontPlayer == Frames11,
                    $"翻转 {flips} 次；玩家在前 {frontPlayer}/{Frames11} 帧");
                Check($"位形 {shapeNames[s]}：换 Perspective 口径（含相机滞后 ±3 扫描 {perspChecks} 例）次序结论一致",
                    perspDisagree == 0,
                    $"不一致 {perspDisagree} / {perspChecks} 例（⇒ 结论不依赖 Unity 把 Default 解析成哪种模式）");
            }

            //    `GameConst.LayerOffsetDeckEntity` 的常量注释写着「4D+106 与 实体(D+1) 同值，但正南恒是栏杆
            //    ⇒ 实际不会并列」——本项把这个"假设"变成**可判**：真并列时，唯一决胜键就是第三键。
            var deckGrid = new Vector2Int(46, 25);                          // 城镇桥面样例格（mapcheck §22 同格）
            var southItemGrid = new Vector2Int(46, 26);                     // gx+gy = D+1 的一格
            var deckOrder = Iso.EntitySortOrder(deckGrid, true);
            var southOrder = Iso.EntitySortOrder(southItemGrid, false);
            var deckZ = ViewModule.SortTieZ(GameConst.PlayerEntityId);
            var itemZ = ViewModule.SortTieZ(GameConst.GroundItemIdBase + 1);
            Check("桥面档 4D+106 与「gx+gy=D+1 的普通实体档」数值并列 ⇒ 一旦同屏，第三键是唯一决胜键（旧口径必闪）",
                deckOrder == southOrder && deckZ < itemZ,
                $"deck({deckGrid.x},{deckGrid.y})档={deckOrder} == 普通({southItemGrid.x},{southItemGrid.y})档={southOrder}；" +
                $"z(玩家)={deckZ:0.0000} < z(地面物品#{GameConst.GroundItemIdBase + 1})={itemZ:0.0000}");

            // ── ⑤ 「唯一入口」不变式（**源码级**，防回归）：`Module/View/**` 里所有对
            //    `Root.transform.position` 的**赋值**都必须经过 `EntityWorld`（z 次级键的唯一产地）。
            //    为什么是源码级：这是一条**不变式**，不是数值 —— 数值断言看不见"新加一条绕过它的路径"。
            //    修前它必然红（那处直接写 `v.LastWorld`）。
            var viewDir = System.IO.Path.Combine(ResolveProjectRoot(),
                "client", "Assets", "Scripts", "Module", "View");
            var posAssigns = 0;
            var bypass = new List<string>();
            if (!System.IO.Directory.Exists(viewDir))
            {
                Check("「唯一入口」扫描：Module/View/** 可定位", false, "目录不存在：" + viewDir);
            }
            else
            {
                foreach (var file in System.IO.Directory.GetFiles(viewDir, "*.cs",
                             System.IO.SearchOption.AllDirectories))
                {
                    var src = System.IO.File.ReadAllLines(file);
                    for (var i = 0; i < src.Length; i++)
                    {
                        var t = src[i];
                        if (t.TrimStart().StartsWith("//")) continue;          // 注释行不算
                        var at = t.IndexOf(".transform.position", StringComparison.Ordinal);
                        if (at < 0) continue;
                        var rest = t.Substring(at + ".transform.position".Length);
                        if (rest.StartsWith("==")) continue;                   // 比较不算
                        if (!rest.TrimStart().StartsWith("=")) continue;       // 读取（`.z` / `;` / `)`）不算
                        posAssigns++;
                        if (t.IndexOf("EntityWorld(", StringComparison.Ordinal) < 0)
                            bypass.Add(System.IO.Path.GetFileName(file) + ":" + (i + 1) + " " + t.Trim());
                    }
                }
                var msg = bypass.Count == 0 ? "" : "违例：" + string.Join(" ｜ ", bypass.ToArray());
                Check("Module/View/** 里 Root.transform.position 的赋值 100% 经过 EntityWorld（唯一入口不变式）",
                    posAssigns > 0 && bypass.Count == 0,
                    $"扫描到 {posAssigns} 处赋值，绕过 {bypass.Count} 处" + (msg.Length > 0 ? "；" + msg : ""));
            }

            //    **仍是 0**（那是逻辑层返回值语义，不许改）；投射物表现层的第三键由
            var projZ = Diablo2.Module.Skill.Projectile.WorldOf(new UnityEngine.Vector2(3.5f, 7.5f)).z;
            Check("`Projectile.WorldOf` 的 z 恒 0（**按裁决保留**：逻辑层语义；表现层第三键在 §12）",
                projZ == 0f,
                "出处 Module/Skill/Projectile.cs:141-144（`Core/` 契约冻结 + 裁决「⛔ 不改返回值语义」）；" +
                "`ProjectileView.WorldPosOf` 补 z = `SortZFor(p.id)` ∈ (0,0.9]，实体档 `SortTieZ` ≥ 1.0001");
        }

        // ═════════════════════════════════════════════════════════════════════
        //     事实链（逐条源码可读，不是推断）：
        //       · `Module/Skill/Projectile.cs:141-144` 的 `WorldOf` **z 恒 0**（逻辑层返回值语义，
        //         裁决「不改」）；
        //       · `ProjectileView` 的 `sortingOrder = ViewModule.EntitySortOrder(p.Grid)` ⇒ 与实体层
        //         **同一个主键函数**（= 同族）；同 `gx+gy` 的两个投射物主键相等、第三键也相等
        //         **纯函数**第三键 z = `ProjectileView.SortZFor(p.id)` ∈ (0, 0.9]。
        //       不等 = 次序可判别；相等 = 次序未定义。
        //     值带 (0, 0.9] **刻意压在实体档 `ViewModule.SortTieZ`（恒 ≥ 1.0001）之下** ⇒
        //       「投射物之间」这条无键带。要改"投射物 vs 实体"的遮挡关系 = **表现类** ⇒
        //       台账「待 Unity 窗口」总表 **W7**（看同格 / 相邻格两态各一图）。
        // ═════════════════════════════════════════════════════════════════════
        private static void Section12_ProjectileSortKey()
        {
            Section("12. ★ U26/U36 同族：投射物第三键（三形 × {vs 实体 / vs 投射物} + 纯函数 + 改前必红 + 值带）");

            var town = new Diablo2.Module.Map.MapModule();
            town.Generate(AreaId.Town, 0);
            var npcGrid = town.NpcPoints[(int)NpcId.Akara];
            var npcIdMethod = typeof(ViewModule).GetMethod("NpcEntityId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var npcEntityId = npcIdMethod != null
                ? (int)npcIdMethod.Invoke(null, new object[] { NpcId.Akara }) : 0;
            var path = FindWalkPath(town, town.SpawnPoint, npcGrid);

            // **现口径**（生产）：投射物表现层第三键 = `ProjectileView.SortZFor`
            Func<int, float> projZ = Diablo2.Module.Skill.ProjectileView.SortZFor;
            Func<int, float> projZOld = _ =>
                Diablo2.Module.Skill.Projectile.WorldOf(new UnityEngine.Vector2(3.5f, 7.5f)).z;
            // 实体第三键 = 生产**唯一入口** `ViewModule.EntityWorld` 的 z
            Func<int, Vector2Int, float> entZAt = (id, g) => ViewModule.EntityWorld(id, Iso.GridToWorld(g)).z;

            if (path == null || path.Count < 2)
            {
                Check("投射物第三键：真 A* 路径可定位（本节前置）", false,
                    "FindPath 返回 null ⇒ 本节其余断言**不成立**（⛔ 不是跳过项）");
            }
            else
            {
                var shapes = new[] { new Vector2Int(0, 0), new Vector2Int(1, -1), new Vector2Int(2, -2) };
                var shapeNames = new[] { "1 严格同格", "2 相邻格同 y", "3 同 y 不同格" };
                var entityIds = new[] { npcEntityId, 1007, GameConst.PlayerEntityId, GameConst.GroundItemIdBase + 1 };
                var entityNames = new[] { "NPC(阿卡拉)", "怪物#1007", "玩家", "地面物品#100001" };
                var projIds = new[] { 1, 2, 3, 7, 12, 99, 1000, 8999 };   // 含跨档/取模边界值

                // ── ⓐ 纯函数：100 次重复调用得到**同一序列**（防"值依赖调用次数/外部状态"）──
                var seqOk = true;
                var first = new float[projIds.Length];
                for (var k = 0; k < projIds.Length; k++) first[k] = projZ(projIds[k]);
                for (var it = 0; it < 100; it++)
                {
                    for (var k = 0; k < projIds.Length; k++)
                    {
                        // 交叉喂别的 id（打乱调用顺序）后再回读同一 id
                        var _ = projZ(projIds[(k + 3) % projIds.Length]);
                        if (projZ(projIds[k]) != first[k]) seqOk = false;
                    }
                }
                Check("`ProjectileView.SortZFor` 是纯函数（100 轮乱序重复调用 ⇒ 同一 id 恒同值）",
                    seqOk,
                    $"id={string.Join(",", projIds)} ⇒ z={string.Join(",", Array.ConvertAll(first, v => v.ToString("0.0000")))}");

                // ── ⓑ 值带：所有投射物键 ∈ (0, 1)，且**严格小于**实体档下界（= 遮挡关系不变）──
                var bandMin = float.MaxValue;
                var bandMax = float.MinValue;
                for (var id = 0; id < 10000; id++)
                {
                    var z = projZ(id);
                    if (z < bandMin) bandMin = z;
                    if (z > bandMax) bandMax = z;
                }
                var entMin = entZAt(GameConst.PlayerEntityId, path[0]);          // 实体档最小（玩家 rank 4 ⇒ 1.0001）
                Check("值带 (0,1) 且 < 实体档下界 ⇒ 「投射物 vs 实体」先后关系与改前**逐值一致**（改遮挡关系留给 W7）",
                    bandMin > 0f && bandMax < entMin,
                    $"投射物 z 扫描 id∈[0,9999] ⇒ [{bandMin:0.0000}, {bandMax:0.0000}]；实体档最小 z={entMin:0.0000}");

                // ── ⓒ 三形 × 「投射物 vs 实体」（要求断言）────────────────────────────────
                for (var s = 0; s < shapes.Length; s++)
                {
                    var off = shapes[s];
                    var samePrimary = 0;
                    var pairs = 0;
                    var collisions = new List<string>();
                    foreach (var cell in path)
                    {
                        var eGrid = new Vector2Int(cell.x + off.x, cell.y + off.y);
                        if (ViewModule.EntitySortOrder(cell) != ViewModule.EntitySortOrder(eGrid)) continue;
                        samePrimary++;
                        var pz = projZ(1);
                        for (var k = 0; k < entityIds.Length; k++)
                        {
                            pairs++;
                            var ez = entZAt(entityIds[k], eGrid);
                            if (pz == ez)
                                collisions.Add($"格({cell.x},{cell.y}) {entityNames[k]}：order={ViewModule.EntitySortOrder(cell)} 且 z 都 = {pz:0.0000}");
                        }
                    }
                    Check($"位形 {shapeNames[s]}：主键相等的格上「投射物 vs 实体」第三键**两两不等**（{pairs} 对）",
                        samePrimary > 0 && collisions.Count == 0,
                        $"主键相等 {samePrimary}/{path.Count} 格 × {entityIds.Length} 类实体 = {pairs} 对；碰撞 {collisions.Count} 处" +
                        (collisions.Count > 0 ? "：" + string.Join(" ｜ ", collisions.ToArray()) : "") +
                        $"（投射物 z={projZ(1):0.0000}；实体 z：NPC={entZAt(npcEntityId, npcGrid):0.0000} " +
                        $"玩家={entZAt(GameConst.PlayerEntityId, path[0]):0.0000}）");
                }

                // ── ⓓ 三形 × 「投射物 vs 投射物」（本裁决点名的要求断言）────────────────────
                for (var s = 0; s < shapes.Length; s++)
                {
                    var off = shapes[s];
                    var samePrimary = 0;
                    var cols = new List<string>();
                    foreach (var cell in path)
                    {
                        var eGrid = new Vector2Int(cell.x + off.x, cell.y + off.y);
                        if (ViewModule.EntitySortOrder(cell) != ViewModule.EntitySortOrder(eGrid)) continue;
                        samePrimary++;
                        var nodes = new List<(string Name, int Order, float Z)>();
                        for (var k = 0; k < projIds.Length; k++)
                            nodes.Add(($"投射物#{projIds[k]}", ViewModule.EntitySortOrder(cell), projZ(projIds[k])));
                        var c = SortCollisions(nodes);
                        if (c.Count > 0) cols.Add($"格({cell.x},{cell.y})：" + string.Join(" ｜ ", c.ToArray()));
                    }
                    Check($"位形 {shapeNames[s]}：同 gx+gy 的 {projIds.Length} 个投射物第三键**两两不等**（{samePrimary} 格）",
                        samePrimary > 0 && cols.Count == 0,
                        $"主键相等 {samePrimary}/{path.Count} 格；碰撞 {cols.Count} 格" +
                        (cols.Count > 0 ? "：" + string.Join(" ｜ ", cols.ToArray()) : "") +
                        $"（id={string.Join(",", projIds)} ⇒ z 全不相等；改前这里恒为 {projIds.Length} 选 2 全撞）");
                }

                // ── ⓔ 判据极性·**已知好**样本（两个实体的真键同格）⇒ 必须判「可判别」──────────
                var goodSample = new List<(string Name, int Order, float Z)>
                {
                    ("玩家", ViewModule.EntitySortOrder(path[0]), entZAt(GameConst.PlayerEntityId, path[0])),
                    ("NPC", ViewModule.EntitySortOrder(path[0]), entZAt(npcEntityId, path[0])),
                };
                var goodColl = SortCollisions(goodSample);
                Check("判据极性·**已知好**样本：两个**有**第三键的节点同格（玩家 + NPC）⇒ 判据判「可判别」（0 碰撞）",
                    goodColl.Count == 0,
                    $"碰撞 {goodColl.Count} 处；z(玩家)={entZAt(GameConst.PlayerEntityId, path[0]):0.0000} " +
                    $"vs z(NPC)={entZAt(npcEntityId, path[0]):0.0000}");

                //     ⓓ 会当场变红；本条证明**同一判据**对旧口径判红。
                var oldSample = new List<(string Name, int Order, float Z)>
                {
                    ("投射物#1(旧口径)", ViewModule.EntitySortOrder(path[0]), projZOld(1)),
                    ("投射物#2(旧口径)", ViewModule.EntitySortOrder(path[0]), projZOld(2)),
                };
                var oldColl = SortCollisions(oldSample);
                Check("【改前必红】旧口径（z 取自 `Projectile.WorldOf` = 恒 0）同 gx+gy 两投射物 ⇒ 判据**必须**报碰撞",
                    oldColl.Count == 1,
                    $"碰撞 {oldColl.Count} 处" + (oldColl.Count > 0 ? "：" + oldColl[0] : "") +
                    $"（z 都 = {projZOld(1):0.0000} ⇒ 主键相等时唯一决胜键也相等 = 次序未定义；"
                    + $"现口径同位置 z = {projZ(1):0.0000}/{projZ(2):0.0000} ⇒ 已可判别）");

                // ── ⓖ 现口径·真输入：同 gx+gy 的两个投射物 ⇒ **0 碰撞**（ⓕ 的翻转态）──────────
                var realTwo = new List<(string Name, int Order, float Z)>
                {
                    ("投射物#1", ViewModule.EntitySortOrder(path[0]), projZ(1)),
                    ("投射物#2", ViewModule.EntitySortOrder(path[0]), projZ(2)),
                };
                Check("现口径·真输入：同 gx+gy 的两个投射物第三键**不等**（ⓕ 的翻转态 = 修法在盘上）",
                    SortCollisions(realTwo).Count == 0,
                    $"格({path[0].x},{path[0].y})：order={ViewModule.EntitySortOrder(path[0])}、z = {projZ(1):0.0000} vs {projZ(2):0.0000}" +
                    $"（出处 `ProjectileView.WorldPosOf` ← `SortZFor(p.id)`）");

                //   真输入 = 真 A* 逐帧走位；两个投射物分别停在「玩家当前格」与「当前格 + 三形偏移」
                //   = **主键恒相等**的那三种关系；位置走**生产唯一产地** `ProjectileView.WorldPosOf`。
                //   **极性一句话（写死在这里，防再读反）**：本组要证的是「**每帧都可判别**」
                //     —— 通过条件 = `ProjPasses` = {主键全相等 ∧ 不可判别 0 ∧ #1 在前 全帧 ∧ 翻转 0}；
                //     **不是**「键元组必须逐帧恒定」：键随格变化属正常（`EntitySortOrder` 随格而变、
                //     两枚投射物**同步**变 ⇒ 次序不变）；反而"恒定地不可判别"才是**旧口径**的病征。
                //   **退化样本** `ⓗ-样本`：把第三键换回旧口径（`WorldOf`，恒 0）喂**同一个** `ProjFrames`
                Func<Diablo2.Module.Skill.Projectile, float> zProd = p => Diablo2.Module.Skill.ProjectileView.WorldPosOf(p).z;
                Func<Diablo2.Module.Skill.Projectile, float> zOldOf = p => Diablo2.Module.Skill.Projectile.WorldOf(p.pos).z;
                for (var s = 0; s < shapes.Length; s++)
                {
                    var off = shapes[s];
                    var st = ProjFrames(town, path, off, zProd, zOldOf);
                    Console.WriteLine($"  ── ⓗ 位形 {shapeNames[s]}：偏移格 ({off.x},{off.y})，真走位 {Frames11} 帧");
                    Console.WriteLine($"     逐帧「谁在前」（+ = 投射物#1 在前，- = #2 在前，? = 三级键全等=未定义）：");
                    Console.WriteLine($"     {st.Seq}");
                    Check($"ⓗ 位形 {shapeNames[s]}：主键全相等 {st.Same}/{Frames11} ∧ 不可判别 {st.UndecNew} ∧ #1 在前 {st.FrontA}/{Frames11} ∧ 翻转 {st.Flips} ⇒ `ProjPasses`={ProjPasses(st)}",
                        ProjPasses(st),
                        $"主键相等 {st.Same}；翻转 {st.Flips}；#1 在前 {st.FrontA}/{Frames11}；不可判别 {st.UndecNew}；" +
                        $"键元组取值 {st.KeySets.Count} 种（{st.KeyChanged}/{Frames11} 帧变值）⇒ {string.Join(" ｜ ", st.KeySets.ToArray())}" +
                        $"（⚠️ 键随格变属正常、⛔ 判据不要求键恒定）");
                    Check($"ⓗ 位形 {shapeNames[s]}：【红样本·闸门极性】旧口径同帧**每帧都不可判别**（原状 = 交给引擎提交顺序）",
                        st.UndecOld == st.Same && st.UndecOld > 0,
                        $"旧口径不可判别 {st.UndecOld}/{st.Same} 帧（两枚 z 都 = 0）⇒ 「新口径每帧可判别 + 旧口径每帧未定义」两条合起来即可离线闭合 W7");
                }

                // 退化样本：**与主路径同一段实现**（`ProjFrames`）+ **同一个通过条件**（`ProjPasses`），
                //  只把第三键来源换成旧口径 ⇒ `ProjPasses` 必须 False（否则就是"只能绿"的假判据）。
                var stBad = ProjFrames(town, path, shapes[2], zOldOf, zOldOf);
                Check("ⓗ-样本（**同实现·能红**）：第三键换回旧口径（`WorldOf` 恒 0）喂同一 `ProjFrames`/`ProjPasses` ⇒ 通过条件必须**不成立**",
                    !ProjPasses(stBad) && stBad.UndecNew > 0 && stBad.FrontA == 0,
                    $"旧口径：主键相等 {stBad.Same}、不可判别 {stBad.UndecNew}/{Frames11}、#1 在前 {stBad.FrontA}、翻转 {stBad.Flips}" +
                    $" ⇒ `ProjPasses`={ProjPasses(stBad)}（必须 False）；逐帧序列 = {stBad.Seq}");
            }
        }

        /// <summary>
        /// 主键相等的一组节点里，第三键**两两不等**（= 次序可判别）的检查：返回「主键相等且第三键也相等」的节点对。
        /// </summary>
        private static List<string> SortCollisions(List<(string Name, int Order, float Z)> nodes)
        {
            var bad = new List<string>();
            for (var a = 0; a < nodes.Count; a++)
            {
                for (var b = a + 1; b < nodes.Count; b++)
                {
                    if (nodes[a].Order != nodes[b].Order) continue;        // 主键不同 ⇒ 不需要第三键
                    if (nodes[a].Z == nodes[b].Z)
                        bad.Add($"{nodes[a].Name} vs {nodes[b].Name}：order={nodes[a].Order} 且 z 都 = {nodes[a].Z:0.0000}");
                }
            }
            return bad;
        }

        /// <summary>ⓗ 的逐帧统计（**主路径与退化样本调同一实现**；第三键来源可注入）。</summary>
        private sealed class ProjFrameStat
        {
            public int Same;          // 主键（sortingOrder）相等 的帧数
            public int Flips;         // 相邻帧「谁在前」翻转 次数
            public int UndecNew;      // 现口径下"三级键全等 = 未定义" 的帧数
            public int UndecOld;      // 对照口径（旧）下未定义 的帧数
            public int FrontA;        // #1 在前 的帧数
            public int KeyChanged;    // 键元组相对首帧变值 的帧数（**信息项**，⛔ 不是通过条件）
            public List<string> KeySets = new List<string>();
            public string Seq = "";
        }

        /// <summary>
        /// ⓗ 的**通过条件**（主路径与退化样本**共用同一个**）：
        /// 主键全相等 ∧ 不可判别 0 ∧ #1 在前 全帧 ∧ 翻转 0。
        /// 不含"键元组恒定" —— 键随格变化属正常；判据是"**每帧都可判别**"，不是"键不变"。
        /// </summary>
        private static bool ProjPasses(ProjFrameStat st)
            => st.Same == Frames11 && st.UndecNew == 0 && st.FrontA == Frames11 && st.Flips == 0;

        /// <summary>
        /// ⓗ 的逐帧实现：真 A* 逐帧走位（真 `PlayerMotor` + 真 `MapModule`），两枚投射物停在
        /// 「当前格」与「当前格 + <paramref name="off"/>」（⇒ 主键恒相等）；`zNew` = 被判的第三键来源，
        /// `zOld` = 对照口径（只喂"旧口径每帧未定义"这条极性锚）。
        /// </summary>
        private static ProjFrameStat ProjFrames(Diablo2.Module.Map.MapModule town, List<Vector2Int> path,
            Vector2Int off,
            Func<Diablo2.Module.Skill.Projectile, float> zNew,
            Func<Diablo2.Module.Skill.Projectile, float> zOld)
        {
            var st = new ProjFrameStat();
            var motor = new Diablo2.Module.Player.PlayerMotor();
            motor.Teleport(town.SpawnPoint, town);
            motor.SetPath(path, path[path.Count - 1]);
            var prev = 0;
            var keyFirst = "";
            var sb = new System.Text.StringBuilder();
            for (var f = 0; f < Frames11; f++)
            {
                motor.Tick(Dt, town);
                var aGrid = motor.Grid;
                var bGrid = new Vector2Int(aGrid.x + off.x, aGrid.y + off.y);
                var oa = ViewModule.EntitySortOrder(aGrid);
                var ob = ViewModule.EntitySortOrder(bGrid);
                // 投射物在**格中心**（`Projectile.Grid` = floor(pos) ⇒ pos = 格 + 0.5）
                var pa = new Diablo2.Module.Skill.Projectile
                { id = 1, pos = new UnityEngine.Vector2(aGrid.x + 0.5f, aGrid.y + 0.5f) };
                var pb = new Diablo2.Module.Skill.Projectile
                { id = 2, pos = new UnityEngine.Vector2(bGrid.x + 0.5f, bGrid.y + 0.5f) };
                var za = zNew(pa);
                var zb = zNew(pb);
                if (oa == ob) st.Same++;
                var front = FrontOf(oa, za, ob, zb);
                if (front == 0) st.UndecNew++;
                if (front > 0) st.FrontA++;
                if (oa == ob && prev != 0 && front != prev) st.Flips++;
                if (front != 0) prev = front;
                if (FrontOf(oa, zOld(pa), ob, zOld(pb)) == 0) st.UndecOld++;
                var key = $"{oa},{ob}|{za:0.0000},{zb:0.0000}";   // 信息项：键随格变化是正常的
                if (f == 0) keyFirst = key;
                else if (key != keyFirst) st.KeyChanged++;
                if (!st.KeySets.Contains(key)) st.KeySets.Add(key);
                sb.Append(front > 0 ? "+" : (front < 0 ? "-" : "?"));
            }
            st.Seq = sb.ToString();
            return st;
        }

        /// <summary>三级键下的「谁在前」：主键（sortingOrder）大者在前；相等时第三键（z / 距离）**小者**在前；全等 = 0（未定义）。</summary>
        private static int FrontOf(int pOrder, float pThird, int nOrder, float nThird)
        {
            if (pOrder != nOrder) return pOrder > nOrder ? +1 : -1;
            if (pThird < nThird) return +1;
            if (nThird < pThird) return -1;
            return 0;
        }

        /// <summary>真 A* 路径；终点不可达时退到起点 8 邻域里最近的一个可走格（保证有 ≥2 点的合法路径）。</summary>
        private static List<Vector2Int> FindWalkPath(IMapModule map, Vector2Int from, Vector2Int to)
        {
            var path = map.FindPath(from, to);
            if (path != null && path.Count >= 2) return path;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var n = new Vector2Int(from.x + dx, from.y + dy);
                    if (!map.Walkable(n)) continue;
                    path = map.FindPath(from, n);
                    if (path != null && path.Count >= 2) return path;
                }
            }
            return null;
        }

        private static bool MathfApprox(float a, float b) => Math.Abs(a - b) < 1e-4f;

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
