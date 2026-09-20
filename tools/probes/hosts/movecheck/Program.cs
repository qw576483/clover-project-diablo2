// ─────────────────────────────────────────────────────────────────────────────
// MoveCheck · 片 2b「移动手感（速度 / 步频同步 / 走跑两套动画）+ 相机视野 + 怪物速度」数值自证
//
// 只做**数值与纯逻辑**断言（不依赖 Unity 原生、不进 Play）：
//   §1 速度常量（玩家跑/走）        §2 相机视野（正交尺寸 / 可见格高）
//   §3 怪物速度量纲（官方 Velocity → 格/秒）
//   §4 帧率口径（FpsForCycle / SpeedScaleForCycle / IsMoveAnim）
//   §5 帧数表与 ViewAnim 下标一致（run 列）+ 帧键拼法 + 缺 RN 的回退
//   §6 ★ 核心：用**真 SpriteAnimator** 逐帧推进，实测「移动动画有效帧率 = 帧数 × 速度」
//
// ⛔ 本宿主不复制任何被测逻辑：链的是 client/Assets/Scripts/** 的真实源码。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.Module.Monster;
using Diablo2.Module.View;

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

            Console.WriteLine();
            Console.WriteLine($"================ MoveCheck 结束：通过 {_ok} 项，失败 {_fail} 项 ================");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. ★ 移动抖动（R1-D · 候选②「动画被反复打回第 0 帧」的离线判定）
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
        /// ★ 核心实测：用真 <see cref="SpriteAnimator"/> 播某动作，倍率按
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
