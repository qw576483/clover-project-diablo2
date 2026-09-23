// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterSfx.cs
// **逐类怪物音效键的解析表**（★ 片 monster-audio）。
//
// 为什么要有这一层：
//   `Module/Combat/SfxKeys.cs` 只登记**键名常量**（`monster_hit_fa` 之类），它不知道怪物是哪一类；
//   而契约 `Def.MonsterState` 里**没有**「原版 `MonStats.Code`」这一列（只有 `kindId`/`name`/`ai`，
//   ⛔ 契约冻结不许加字段）⇒ 需要一个「`kindId` ⇒ 原版 Code ⇒ 音效键」的解析点。
//   原版 Code 的唯一在库来源 = `Module/View/SpriteFrames.SpriteCodeOf(kindId)`
//   （它读 `monster_c` 的 `sprite` 列，值就是 `MonStats.Code` 小写：`fa`/`fs`/`si`/`zm`/`ye`/`cr`/`bk`/`wr`）。
//
// ⚠ 代码 → 类别的**唯一出处** = `原版资源/d2lod1.10txt-1.10f/data/global/excel/MonStats.txt`
//   的 `Code` 列 × `MonSound` 列（⛔ **不是**凭显示名猜的 —— 实测反例：
//   `bk` 是 **foulcrow（血鹰）**、`ye` 才是 **brute（野兽）**，按名字猜会整套错位）。
//   音效条目名出处 = `MonSounds.txt` 的 `HitSound` / `Attack1` / `DeathSound` / `Footstep` /
//   `FootstepLayer` 列；目标文件出处 = `Sounds.txt` 的 `FileName` 列。
//
// 未登记 / 取不到 Code ⇒ 返回 **null**（调用方回落到通用键，并**打一次** Warn 留痕），
// ⛔ 不许返回"听起来差不多"的别的键（那等于用别的音效凑，违反 1:1 口径）。
//
// 跨模块引用走**限定名**（不写 `using Diablo2.Module.*`）：`Combat.SfxKeys.*` / `View.SpriteFrames.*`
// （与 `Module/Audio/SfxRegistry.cs` 引用 `Combat.SfxKeys` 的写法一致）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;

namespace Diablo2.Module.Monster
{
    /// <summary>怪物类别（原版 `MonStats.Code` 小写）→ 逐类音效键。</summary>
    internal static class MonsterSfx
    {
        /// <summary>下标：0=受击 1=攻击 2=死亡 3=脚步（`null` = 原版该类没有这一项）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, string[]> ByCode =
            new System.Collections.Generic.Dictionary<string, string[]>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                // fa = Fallen（沉沦魔；`MonSounds.Id=fallen`）
                { "fa", new[] { Combat.SfxKeys.MonsterHitFa, Combat.SfxKeys.MonsterAtkFa,
                                Combat.SfxKeys.MonsterDieFa, Combat.SfxKeys.MonsterStepFa } },
                // fs = FallenShaman（`fallenshaman`）
                { "fs", new[] { Combat.SfxKeys.MonsterHitFs, Combat.SfxKeys.MonsterAtkFs,
                                Combat.SfxKeys.MonsterDieFs, Combat.SfxKeys.MonsterStepFs } },
                // si = QuillRat / SpikeFiend（`quillrat`）—— 原版 `MonSounds` 无 Footstep ⇒ 第 4 项 null
                { "si", new[] { Combat.SfxKeys.MonsterHitSi, Combat.SfxKeys.MonsterAtkSi,
                                Combat.SfxKeys.MonsterDieSi, null } },
                // zm = Zombie（`zombie`）
                { "zm", new[] { Combat.SfxKeys.MonsterHitZm, Combat.SfxKeys.MonsterAtkZm,
                                Combat.SfxKeys.MonsterDieZm, Combat.SfxKeys.MonsterStepZm } },
                // ye = Brute / Yeti（`brute`）
                { "ye", new[] { Combat.SfxKeys.MonsterHitYe, Combat.SfxKeys.MonsterAtkYe,
                                Combat.SfxKeys.MonsterDieYe, Combat.SfxKeys.MonsterStepYe } },
                // cr = CorruptRogue（`corruptrogue`）
                { "cr", new[] { Combat.SfxKeys.MonsterHitCr, Combat.SfxKeys.MonsterAtkCr,
                                Combat.SfxKeys.MonsterDieCr, Combat.SfxKeys.MonsterStepCr } },
                // bk = BloodHawk / FoulCrow（`foulcrow`）—— 飞行怪的脚步档用 `FootstepLayer=hawk_wing_1`
                { "bk", new[] { Combat.SfxKeys.MonsterHitBk, Combat.SfxKeys.MonsterAtkBk,
                                Combat.SfxKeys.MonsterDieBk, Combat.SfxKeys.MonsterStepBk } },
                // wr = Wraith（`wraith`）—— 原版无 Footstep ⇒ 第 4 项 null
                { "wr", new[] { Combat.SfxKeys.MonsterHitWr, Combat.SfxKeys.MonsterAtkWr,
                                Combat.SfxKeys.MonsterDieWr, null } },
            };

        // ═════════════════════════════════════════════════════════════════════
        // 原版时序（★ 片 monster-audio 第四轮）：逐条取自 `MonSounds.txt` 的对应列，
        //   ⛔ **没有一个数是凭空写的**。
        //
        //   列 → 含义（原版 `MonSounds.txt` 表头逐字）：
        //     `HitDelay` = 受击音**延迟多少帧**才响（fallenshaman/zombie/brute/corruptrogue/
        //                  foulcrow/wraith = 2，quillrat = 5，fallen = 2）；
        //     `DeaDelay` = 死亡音延迟帧数（除 quillrat=4 外全为 1）；
        //     `FsCnt`    = **一个走路循环有几次脚步** ⇒ 每走 `1/FsCnt` 格一步（有脚步的 6 类全为 2
        //                  ⇒ **半格一步**；quillrat / wraith 该列为空 = 原版没有移动音）；
        //     `FsOff`/`FsPrb` = 偏移与触发概率（6 类都是 0 / 100 ⇒ 不掷随机、不偏移）。
        //
        //   帧 → 秒的换算口径 = **÷ `MonsterTuning.LogicFps`（25）**：
        //     出处 `Module/Monster/MonsterTuning.cs` 里「官方出处是动画帧数…**÷ 25**」那条注释，
        //     以及 `OfficialAiDelayFrames / LogicFps` 的既有写法（同一个 25）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>下标：0=`HitDelay`（帧） 1=`FsCnt`（一个走路循环几步；0 = 原版无移动音）。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float[]> Timing =
            new System.Collections.Generic.Dictionary<string, float[]>(
                System.StringComparer.OrdinalIgnoreCase)
            {
                { "fa", new[] { 2f, 2f } },   // fallen:      HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "fs", new[] { 2f, 2f } },   // fallenshaman:HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "si", new[] { 5f, 0f } },   // quillrat:    HitDelay=5（FsCnt 空 = 无移动音）
                { "zm", new[] { 2f, 2f } },   // zombie:      HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "ye", new[] { 2f, 2f } },   // brute:       HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "cr", new[] { 2f, 2f } },   // corruptrogue:HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "bk", new[] { 2f, 2f } },   // foulcrow:    HitDelay=2 FsCnt=2 FsOff=0 FsPrb=100
                { "wr", new[] { 2f, 0f } },   // wraith:      HitDelay=2（FsCnt 空 = 无移动音）
            };

        /// <summary>
        /// 该怪物**自身受击音**相对撞击音的延迟（秒）= `MonSounds.HitDelay` 帧 ÷ `LogicFps`。
        /// 未登记 / 原版该列为 0 ⇒ 返回 0（同帧响）。
        /// </summary>
        public static float HitDelaySeconds(MonsterState s)
        {
            var row = TimingRow(s);
            return row == null ? 0f : row[0] / MonsterTuning.LogicFps;
        }

        /// <summary>
        /// 该怪物**每走多少格出一次脚步** = `1 / MonSounds.FsCnt`（6 类都是 2 ⇒ 0.5 格一步）。
        /// 原版无移动音（`FsCnt` 为空）⇒ 返回 0（调用方不出声，⛔ 不许拿别的类凑）。
        /// </summary>
        public static float StepPeriodTiles(MonsterState s)
        {
            var row = TimingRow(s);
            if (row == null || row[1] <= 0f) return 0f;
            return 1f / row[1];
        }

        private static float[] TimingRow(MonsterState s)
        {
            if (s == null) return null;
            var code = View.SpriteFrames.SpriteCodeOf(s.kindId);
            if (string.IsNullOrEmpty(code)) return null;
            float[] row;
            return ByCode.ContainsKey(code) && Timing.TryGetValue(code, out row) ? row : null;
        }

        /// <summary>该怪物的**受击**音键（原版 `MonSounds.HitSound`）；未登记返回 null。</summary>
        public static string HitOf(MonsterState s) { return Pick(s, 0, "hit"); }

        /// <summary>该怪物的**出手**音键（原版 `MonSounds.Attack1`）；未登记返回 null。</summary>
        public static string AttackOf(MonsterState s) { return Pick(s, 1, "attack"); }

        /// <summary>该怪物的**死亡**音键（原版 `MonSounds.DeathSound`）；未登记返回 null。</summary>
        public static string DieOf(MonsterState s) { return Pick(s, 2, "die"); }

        /// <summary>
        /// 该怪物的**脚步**音键（原版 `MonSounds.Footstep`；飞行怪取 `FootstepLayer`）；
        /// 原版该类没有移动音 ⇒ 返回 null（⛔ 不许拿别的怪的脚步音凑）。
        /// </summary>
        public static string StepOf(MonsterState s) { return Pick(s, 3, "step"); }

        private static string Pick(MonsterState s, int slot, string what)
        {
            if (s == null) return null;

            var code = View.SpriteFrames.SpriteCodeOf(s.kindId);
            if (string.IsNullOrEmpty(code))
            {
                MonsterLog.WarnThrottled("sfx.nocode",
                    $"MonsterSfx.{what}Of：m#{s.id} {s.name} 的 kindId={s.kindId} 取不到原版 Code" +
                    "（`monster_c` 无此种类或 sprite 列为空）⇒ 本次回落到通用怪物音效键");
                return null;
            }

            string[] row;
            if (!ByCode.TryGetValue(code, out row) || row == null || row.Length <= slot)
            {
                MonsterLog.WarnThrottled("sfx.code." + code,
                    $"MonsterSfx.{what}Of：原版 Code=\"{code}\"（m#{s.id} {s.name}）未登记逐类音效" +
                    " ⇒ 本次回落到通用怪物音效键（要补：先解 `MonSounds.txt` 该 Id 的对应列）");
                return null;
            }

            return row[slot];       // 可能为 null（原版该类没有这一项）⇒ 调用方跳过，不回落
        }
    }
}
