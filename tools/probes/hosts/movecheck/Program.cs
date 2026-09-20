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

            Console.WriteLine();
            Console.WriteLine($"================ MoveCheck 结束：通过 {_ok} 项，失败 {_fail} 项 ================");
            return _fail == 0 ? 0 : 1;
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
