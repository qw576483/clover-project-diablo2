// ─────────────────────────────────────────────────────────────────────────────
//
// 运行：
//   dotnet run --project <项目根>/tools/probes/hosts/combatcheck/CombatCheck.csproj -c Release
//
// 这只是**类型层 + 逻辑层**的验证；画面（精灵/贴图/飘字/血条像素位置）必须在用户打开
//    Unity 编辑器后进 Play 由主 agent 看图验收。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Dir8 = Diablo2.Def.Dir8;
using Diablo2.Module;
using Diablo2.Module.Combat;
using Diablo2.Module.Map;
using Diablo2.Module.Monster;
using Diablo2.Module.Skill;
using Diablo2.Module.View;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace CombatCheck
{
    internal static class Program
    {
        // 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        //   用 `Push-Location <宿主目录>` 驱动时被解析成 `<宿主目录>\client\Assets`（不存在）
        private static readonly string ClientAssets = ResolveProjectRoot() + @"\client\Assets";

        /// <summary>
        /// 从宿主自己的可执行目录向上找「含 client/Assets 的那一层」= 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/（与 corecheck / fullcheck / savecheck / uicheck 同一套写法）。
        /// </summary>
        private static string ResolveProjectRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根，回退相对路径 clover-project-diablo2");
            return @"clover-project-diablo2";
        }

        private const float Dt = 0.05f;

        private static int _fail;
        private static RecordingLogger _log;
        private static AppContext _ctx;
        private static FakePlayer _player;
        private static RecordingItem _item;
        private static RecordingAudio _audio;
        private static RecordingView _view;

        /// <summary>具体实现引用（自证要用到契约之外的自证入口，如 `Projectiles` / `DumpStats`）。</summary>
        private static SkillModule _skillImpl;

        private static int _monsterAttackEvents;
        private static int _playerDamagedEvents;
        private static int _monsterKilledEvents;

        private static int Main()
        {
            Console.WriteLine("================ CombatCheck 开始 ================");

            // ── 引擎门面替身 ──
            _log = new RecordingLogger();
            Game.Logger = _log;

            var bus = new ConsoleEventBus();
            Game.Event = bus;
            Game.Fsm = new SimpleFsm();
            Game.Timer = new FakeTimer();
            Game.Setting = new MemSetting();
            Game.UI = new RecUI();
            Game.Scene = new FakeScene();
            Game.Entity = new FakeEntities();
            Game.Pool = new FakePool();
            Game.Res = new MissRes();
            Game.Sound = new FakeSound();
            Game.Input = new FakeInput();
            Game.IsRunning = true;

            bus.On<DamageArgs>(Events.DamageDealt, OnDamageDealt);
            bus.On<DamageArgs>(Events.PlayerDamaged, a => _playerDamagedEvents++);
            bus.On<int>(Events.MonsterKilled, id =>
            {
                _monsterKilledEvents++;
                Trace.Add("event.MonsterKilled:" + id);
            });

            // ── 配表（与 Bootstrap 同一条链路）──
            var err = Table.TableLoader.LoadAll(null, ClientAssets);
            Check("配表加载 TableLoader.LoadAll（与 Bootstrap 同一入口）", err == null,
                err ?? ("dir=" + Table.TableLoader.LastDir));

            // ── 模块装配（与 AutoWire 的结果等价；这里显式 new 以便替换替身）──
            _ctx = AppContext.Create();
            _ctx.Map = new MapModule();
            _player = new FakePlayer();
            _ctx.Player = _player;
            _ctx.Combat = new CombatModule();
            _ctx.Monster = new MonsterModule();
            _skillImpl = new SkillModule();
            _ctx.Skill = _skillImpl;
            _view = new RecordingView();
            _ctx.View = _view;
            _item = new RecordingItem();
            _ctx.Item = _item;
            _audio = new RecordingAudio();
            _ctx.Audio = _audio;

            Check("AppContext 装配完成（Map/Player/Combat/Monster/Skill/View/Item/Audio 全部非 null）",
                _ctx.Map != null && _ctx.Player != null && _ctx.Combat != null && _ctx.Monster != null &&
                _ctx.Skill != null && _ctx.View != null && _ctx.Item != null && _ctx.Audio != null, "(见装配代码)");

            Run(Step0_HostCapability);
            Run(Step1_OfficialFormula);
            Run(Step2_HitRateStatistics);
            Run(Step3_HitFeedbackTriple);
            Run(Step4_FourAiBehaviours);
            Run(Step5_EliteAffix);
            Run(Step6_DeathChain);
            Run(Step7_DropGridsWalkable);
            Run(Step8_SkillTree);
            Run(Step9_CastManaAndCooldown);
            Run(Step16_SkillSlotBinding);      // ★ 审计 R4（impl-I）：F1~F8 → 左右键技能格 + 存档往返
            Run(Step10_ProjectileFlight);
            Run(Step11_SpriteAnimator);
            Run(Step14_ViewStaleRefSafety);
            Run(Step15_ProjectileTerrainAndDeckSort);   // ★ 审计 B 红行 R2/R3（投射物）
            Run(Step17_WeaponDamageSkills);              // ★ 片 N：审计 R1/R2（武器伤害类技能）
            Run(Step18_AttackShape);                     // ★ C3：攻击判定形状（扇形/矩形/线段，不是圆）
            Run(Step19_SameCellMeleeHit);                // ★ melee-samecell：同格攻击必须结算（真实链路）
            Run(Step20_MeleeLineBlocked);                // ★ lineclear-fix：线段被地形阻断时怪不许出手（隔墙挥空）
            Run(Step12_UnwiredDegradation);
            Run(Step13_AutoWireContract);   // 放最后：它会新建一个 AppContext（真实游戏走的就是 AutoWire）

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "================ 自检全部通过 ================" : $"================ 自检失败 {_fail} 项 ================");
            Console.WriteLine("未覆盖（需要 Unity 原生 API，留给主 agent 进 Play 后看图验收）："
                + "① 精灵/贴图实际渲染与像素位置；② 飘字可见性与配色；③ 头顶血条的世界位置与遮挡；"
                + "④ 8 方向走路动画的观感；⑤ 相机下的排序叠压。");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 0. 宿主自证
        // ═════════════════════════════════════════════════════════════════════
        private static void Step0_HostCapability()
        {
            Section("0. 宿主可用性自证（验证手段自身要自证）");
            Console.WriteLine($"Vector2Int(3,-4) = {new Vector2Int(3, -4)}；Mathf.FloorToInt(-0.5) = {Mathf.FloorToInt(-0.5f)}");
            Console.WriteLine($"Iso.GridToWorld(3,5) = {Iso.GridToWorld(3, 5)}；SortOrder(3,5) = {Iso.SortOrder(3, 5)}");
            Check("Iso 正/逆投影往返一致", Iso.WorldToGrid(Iso.GridToWorld(new Vector2Int(3, 5))) == new Vector2Int(3, 5), "见上行");

            try
            {
                var t = Time.realtimeSinceStartup;
                Console.WriteLine($"Time.realtimeSinceStartup 可用（{t}）⇒ Core/Log 的 WarnThrottled 在本宿主也能用");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Time.realtimeSinceStartup 在非 Unity 进程不可用（{ex.GetType().Name}）" +
                                  "⇒ `Core/Log.WarnThrottled/WarnOnce` 会抛异常，故本模块组的降频日志" +
                                  "（`CombatLog/MonsterLog/SkillLog/ViewLog`）**自己实现无时钟降频**（见各文件头）。");
            }
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. 官方公式（固定输入 → 固定输出）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step1_OfficialFormula()
        {
            Section("1. 官方伤害 / 命中 / 抗性公式：固定输入 → 固定输出");

            // ① 物理 = 武器 + 武器 × (力量×StrBonus/100 + 敏捷×DexBonus/100 + 技能ED%) / 100
            //    （官方 DAMAGE_CalculatePhysicalDamage @0057b420；参考实现 combat.zig:149-180）。
            //    **整数截断**（D2ApplyPercent），不是四舍五入。
            //    近战 100/0、**弓弩 0/100**（见下面新增的弓用例）。
            var p1 = DamageFormula.PhysicalDamage(10, 100, 0, 100, 0, 1f);
            var p2 = DamageFormula.PhysicalDamage(10, 100, 0, 100, 0, 1.5f);
            var p3 = DamageFormula.PhysicalDamage(9, 0, 0, 100, 0, 1f);
            var p4 = DamageFormula.PhysicalDamage(13, 33, 0, 100, 0, 1f);  // 截断：13 + 13*33/100 = 13+4 = 17
            Console.WriteLine($"物理伤害：武器10 力量100 倍率1.0 → {p1}（期望 20：dmg% = 100×100/100 = 100）");
            Console.WriteLine($"物理伤害：武器10 力量100 倍率1.5 → {p2}（期望 25：dmg% = 100 + 50，截断）");
            Console.WriteLine($"物理伤害：武器 9 力量  0 倍率1.0 → {p3}（期望  9：dmg% = 0）");
            Console.WriteLine($"物理伤害：武器13 力量 33 倍率1.0 → {p4}（期望 17：dmg% = 33，13*33/100 = 4 截断）");
            Check("物理伤害 = 武器 + 武器 × (力×StrBonus/100 + 敏×DexBonus/100 + 技能ED%)/100（官方整数截断口径）",
                p1 == 20 && p2 == 25 && p3 == 9 && p4 == 17, $"{p1}/{p2}/{p3}/{p4}");

            var pBow = DamageFormula.PhysicalDamage(10, 0, 100, 0, 100, 1f);      // 弓：力0 敏100
            var pBowStr = DamageFormula.PhysicalDamage(10, 100, 0, 0, 100, 1f);   // 弓：力100 敏0 ⇒ 力量不该生效
            Console.WriteLine($"物理伤害（弓 0/100）：武器10 力0  敏100 → {pBow}（期望 20：dmg% = 100×100/100）");
            Console.WriteLine($"物理伤害（弓 0/100）：武器10 力100 敏0  → {pBowStr}（期望 10：**力量不参与**）");
            Check("弓/弩按 DexBonus 结算（StrBonus=0 ⇒ 力量不参与；片 14 消除 E25）",
                pBow == 20 && pBowStr == 10, $"{pBow}/{pBowStr}");

            // ② 命中率 = 200 × ALvl/(ALvl+DLvl) × AR/(AR+DR)，夹 5%~95%
            var hSame = DamageFormula.HitChance(10, 10, 100, 100);
            var hLowLvl = DamageFormula.HitChance(1, 10, 100, 100);
            var hHighLvl = DamageFormula.HitChance(20, 1, 100, 100);
            var hTooLow = DamageFormula.HitChance(1, 99, 1, 100000);
            var hTooHigh = DamageFormula.HitChance(99, 1, 100000, 1);
            Console.WriteLine($"命中率：ALvl=DLvl=10 AR=DR=100 → {hSame * 100f:0.00}%（期望 50.00%）");
            Console.WriteLine($"命中率：ALvl=1  DLvl=10 AR=DR=100 → {hLowLvl * 100f:0.00}%（期望 9.09%）");
            Console.WriteLine($"命中率：ALvl=20 DLvl=1  AR=DR=100 → {hHighLvl * 100f:0.00}%（期望 95.00%，被上限夹住）");
            Console.WriteLine($"命中率：极差（AR=1 vs DR=100000）→ {hTooLow * 100f:0.00}%（被下限 5% 夹住）");
            Console.WriteLine($"命中率：极优（AR=100000 vs DR=1）→ {hTooHigh * 100f:0.00}%（被上限 95% 夹住）");
            Check("命中率：同 AR/DR 同等级 = 50%",
                Math.Abs(hSame - 0.5f) < 1e-5f, (hSame * 100f).ToString("0.00"));
            Check("命中率：等级差会改变命中率（ALvl<DLvl 更低，ALvl>DLvl 更高）",
                hLowLvl < hSame && hHighLvl > hSame, $"{hLowLvl * 100f:0.0} < {hSame * 100f:0.0} < {hHighLvl * 100f:0.0}");
            Check("命中率：被夹在 5%~95%",
                Math.Abs(hTooLow - GameConst.MinHitChance) < 1e-5f && Math.Abs(hTooHigh - GameConst.MaxHitChance) < 1e-5f,
                $"{hTooLow * 100f:0.00}/{hTooHigh * 100f:0.00}");

            // ③ 抗性 = trunc(伤害 × (100 - 抗性)/100)；**抗性 ≥ 100 = 免疫（0）**；**怪物抗性不夹 75%**
            //    （官方 DAMAGE_ApplyElementalDamageWithResist @0057bf80；参考实现 spell.zig:252-264；
            //     怪物无上限见 combat.zig:299-313）
            var r0 = DamageFormula.ApplyResist(100, 0);
            var r50 = DamageFormula.ApplyResist(100, 50);
            var r75 = DamageFormula.ApplyResist(100, 75);
            var rImmune = DamageFormula.ApplyResist(100, 100);      // 官方：>=100 ⇒ 免疫
            var rOver = DamageFormula.ApplyResist(100, 200);        // 同上
            var rNeg = DamageFormula.ApplyResist(100, -50);
            var rTrunc = DamageFormula.ApplyResist(7, 30);          // 官方用例：7 - 7*30/100 = 4（截断）
            Console.WriteLine($"抗性：raw=100 → 0% → {r0}；50% → {r50}；75% → {r75}；100%（免疫）→ {rImmune}；200%（免疫）→ {rOver}；-50%（加伤）→ {rNeg}；raw=7 抗 30% → {rTrunc}");
            Check("抗性减免公式（免疫阈值 100、负抗性加伤、整数截断；**不夹 75%**）",
                r0 == 100 && r50 == 50 && r75 == 25 && rImmune == 0 && rOver == 0 && rNeg == 150 && rTrunc == 4,
                $"{r0}/{r50}/{r75}/{rImmune}/{rOver}/{rNeg}/{rTrunc}");

            // ④ 技能伤害/法力取配表（skill_c）
            var fb = Table.Tables.Default.Skill.Get(31);      // 法师「火弹」
            Check("skill_c 取到法师「火弹」(id=31)", fb != null, fb != null ? fb.Name : "null");
            if (fb != null)
            {
                DamageFormula.SkillDamageRange(fb, 1, out var d1Min, out var d1Max);
                DamageFormula.SkillDamageRange(fb, 3, out var d3Min, out var d3Max);
                var m1 = DamageFormula.SkillManaCost(fb, 1);
                var m3 = DamageFormula.SkillManaCost(fb, 3);
                Console.WriteLine($"火弹：1 级伤害 {d1Min}-{d1Max}（表 dmg_min/dmg_max={fb.DmgMin}/{fb.DmgMax}），" +
                                  $"3 级伤害 {d3Min}-{d3Max}；1 级法力 {m1}（表 mana={fb.ManaCost}/每级 {fb.ManaPerLvl}），3 级法力 {m3}");
                //   火弹的官方列（skills.txt）：EMin=6 / EMax=12 / EMinLev1=3 / EMaxLev1=3 / HitShift=7
                //   官方口径 = (base + 各段增量) × 2^HitShift / 256，整数截断：
                //     1 级：6  ×128/256 = 3  .. 12×128/256 = 6
                //     3 级：(6+3×2)=12 ⇒ 6  .. (12+3×2)=18 ⇒ 9
                //   ⇒ 3×8 = 旧线性式会给的是 3-6 / 9-18（高出一倍），这个断言能把它抓出来。
                Check("技能伤害 = 官方分段式（引擎 SKILLS_GetDamage；参考实现 spell.zig:88-116 `staged`）：1 级 3-6、3 级 6-9",
                    d1Min == 3 && d1Max == 6 && d3Min == 6 && d3Max == 9,
                    $"{d1Min}-{d1Max} / {d3Min}-{d3Max}");
                Check("技能法力消耗 = mana + lvlmana×(slvl-1)（官方）",
                    m1 == fb.ManaCost && m3 == fb.ManaCost + fb.ManaPerLvl * 2, $"{m1}/{m3}");
            }

            // ⑤ 致命一击：官方 SkillCalc.txt `dm12` + skills.txt「Critical Strike」行 Param1=5 / Param2=80
            //    （参考实现 calc.zig:33-36 的整数口径 + skills_amazon.zig:123-131 的实测用例：1/5/20 级 = 16/42/68）
            Console.WriteLine($"致命一击成功率：1 级={DamageFormula.CriticalStrikeChance(1) * 100f:0}% " +
                              $"5 级={DamageFormula.CriticalStrikeChance(5) * 100f:0}% " +
                              $"10 级={DamageFormula.CriticalStrikeChance(10) * 100f:0}% " +
                              $"20 级={DamageFormula.CriticalStrikeChance(20) * 100f:0}% " +
                              $"30 级={DamageFormula.CriticalStrikeChance(30) * 100f:0}%（渐进上限 = Param2 = 80%，未学=0%）");
            Check("致命一击 = 官方 dm12(Param1=5, Param2=80)，1/5/20 级实测 16/42/68（与参考实现用例逐值一致）",
                DamageFormula.CriticalStrikeChance(0) == 0f
                && Math.Abs(DamageFormula.CriticalStrikeChance(1) - 0.16f) < 1e-6f
                && Math.Abs(DamageFormula.CriticalStrikeChance(5) - 0.42f) < 1e-6f
                && Math.Abs(DamageFormula.CriticalStrikeChance(20) - 0.68f) < 1e-6f
                && DamageFormula.CriticalStrikeChance(30) > DamageFormula.CriticalStrikeChance(20),
                $"{DamageFormula.CriticalStrikeChance(1):0.00}/{DamageFormula.CriticalStrikeChance(5):0.00}/" +
                $"{DamageFormula.CriticalStrikeChance(20):0.00}/{DamageFormula.CriticalStrikeChance(30):0.00}");

            // ⑥ 命中率的**整数口径**（官方 DAMAGE_RollAttackHit @0057d9b0；参考实现 combat.zig:83-108）
            var hLevel = DamageFormula.HitChance(1, 10, 100, 100);          // 100/11 截断 ⇒ 9%
            var hNegDr = DamageFormula.HitChance(10, 10, 0, -50);           // 负数交叉 ⇒ DR=0 / AR=50 ⇒ 100%
            Console.WriteLine($"命中率整数口径：ALvl=1 DLvl=10 AR=DR=100 → {hLevel * 100f:0}%（官方 9%，非浮点 9.09%）");
            Console.WriteLine($"命中率负数交叉：AR=0 DR=-50 → {hNegDr * 100f:0}%（官方把 -50 折给 AR ⇒ AR=50/DR=0 ⇒ 中间值 100%，再被 95% 上限夹住）");
            Check("命中率：官方整数截断（ALvl=1/DLvl=10/AR=DR=100 ⇒ 恰好 9%）",
                Math.Abs(hLevel - 0.09f) < 1e-6f, (hLevel * 100f).ToString("0.00"));
            Check("命中率：负数交叉（AR=0 DR=-50 ⇒ 中间值 100% 后夹到 95%，**不是** 0%）",
                Math.Abs(hNegDr - 0.95f) < 1e-6f, (hNegDr * 100f).ToString("0.00"));
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. 命中率统计（固定 seed 打 1000 次）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step2_HitRateStatistics()
        {
            Section("2. 命中率统计：固定 seed 打 1000 次（可复现）");

            StatCase("中档 50%（ALvl=DLvl=10 AR=DR=100）", DamageFormula.HitChance(10, 10, 100, 100), 0.44f, 0.56f);
            StatCase("上限 95%（AR 远大于 DR、等级碾压）", DamageFormula.HitChance(99, 1, 100000, 1), 0.91f, 0.99f);
            StatCase("下限 5%（AR 远小于 DR、等级被碾压）", DamageFormula.HitChance(1, 99, 1, 100000), 0.015f, 0.09f);
            Console.WriteLine();
        }

        private static void StatCase(string label, float chance, float lo, float hi)
        {
            var rng = new Rng(20250916);
            var hits = 0;
            const int n = 1000;
            for (var i = 0; i < n; i++)
            {
                if (rng.Chance(chance)) hits++;
            }

            var rate = (float)hits / n;
            Console.WriteLine($"  {label}：期望 {chance * 100f:0.00}% ⇒ 实测 {rate * 100f:0.00}%（{hits}/{n}，seed=20250916）");
            Check($"命中率落在期望区间 [{lo * 100f:0}%, {hi * 100f:0}%]（{label}）", rate >= lo && rate <= hi, $"{rate:P2}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 3. 命中反馈三件套
        // ═════════════════════════════════════════════════════════════════════
        private static void Step3_HitFeedbackTriple()
        {
            Section("3. 命中反馈三件套（飘字 + 音效钩子 + 头顶血条下降）必须在**同一次命中**里都发生");

            PrepareMap(AreaId.BloodMoor, 20250916);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            var target = PickMonster(m => m.ai == MonsterAI.Melee);
            Check("血腥荒野刷出了近战怪（僵尸）", target != null, target != null ? $"m#{target.id} {target.name}" : "null");
            if (target == null) return;

            PlacePlayerAdjacent(target.Grid());
            var hpBefore = target.hp;

            List<string> hitWindow = null;
            for (var attempt = 0; attempt < 40 && hitWindow == null; attempt++)
            {
                Trace.Marker("hit");
                var floatsBefore = _view.Floats.Count;
                _ctx.Combat.RequestAttack(target.id);
                var seq = Trace.Since("hit");

                if (_view.Floats.Count > floatsBefore)
                {
                    hitWindow = seq;      // 这一次命中成功
                }
                else
                {
                    _ctx.Combat.Tick(GameConst.PlayerAttackInterval + 0.01f);   // 清冷却再试
                    if (!target.alive) break;
                }
            }

            Check("出现过一次命中（飘字已产生）", hitWindow != null, hitWindow == null ? "(40 次尝试内未命中)" : "命中");
            if (hitWindow == null) return;

            Console.WriteLine("  本次命中的调用序列（同一次 RequestAttack 内）：");
            for (var i = 0; i < hitWindow.Count; i++) Console.WriteLine("    " + hitWindow[i]);

            var hasHp = ContainsStartsWith(hitWindow, "monsterHp:");
            var hasFloat = ContainsStartsWith(hitWindow, "float:");
            var hasSfx = ContainsStartsWith(hitWindow, "sfxAt:");
            var hasPlayHit = ContainsStartsWith(hitWindow, "view.PlayHit:");

            Check("③ 头顶血条下降（ViewModule.UpdateMonster 被调用，同一调用栈）", hasHp, hasHp ? "有 monsterHp: 记录" : "缺失");
            Check("① 飘字（ViewModule.ShowFloatingText 被调用）", hasFloat, hasFloat ? "有 float: 记录" : "缺失");
            Check("② 音效钩子（IAudioModule.SfxAt 被调用）", hasSfx, hasSfx ? "有 sfxAt: 记录" : "缺失");
            Check("受击表现（PlayHit 闪白 + Hit 动画）", hasPlayHit, hasPlayHit ? "有 view.PlayHit: 记录" : "缺失");
            Check("三者出现在**同一次**攻击调用里（不是三次各自触发）",
                hasHp && hasFloat && hasSfx, string.Join(" | ", hitWindow));
            Check("目标血量确实下降", target.hp < hpBefore, $"{hpBefore} → {target.hp}");
            Check("飘字颜色是打包的 ARGB（非 0）", _view.LastArgb != 0, "0x" + _view.LastArgb.ToString("X8"));
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. 四种 AI
        // ═════════════════════════════════════════════════════════════════════
        private static void Step4_FourAiBehaviours()
        {
            Section("4. 四种 AI 各演一次");

            // ── 4.1 血腥荒野：Melee(僵尸) / Range(尖刺鼠) / Coward(堕落者) ──
            PrepareMap(AreaId.BloodMoor, 424242);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            AiMelee();
            AiRangeKeepsDistance();
            AiRangeBacksOff();
            AiCowardFlees();
            MonsterVoiceTiming();
            MonsterStepCadence();
            MonsterDeathDelay();

            // ── 4.2 邪恶洞穴：Shaman(堕落萨满) 复活同伴 ──
            // 本轮改：洞穴布点是随机的，**"萨满 + 复活半径内的同伴"这一对不是每张图都有**
            //   ⇒ 按固定 seed 取图的旧写法会偶发"这张图没有可测场景"而误报失败。
            //   改成：在若干 seed 里找**第一张能测的图**测到底；一张都找不到才判失败（如实报）。
            var seeds = new[] { 424343, 20250916, 555001, 12345, 987654321, 424242 };
            var tested = false;
            for (var i = 0; i < seeds.Length && !tested; i++)
            {
                PrepareMap(AreaId.DenOfEvil, seeds[i]);
                _ctx.Monster.DespawnAll();
                _ctx.Monster.SpawnArea(AreaId.DenOfEvil);
                tested = AiShamanRevives(seeds[i]);
            }
            if (!tested)
            {
                Check("Shaman：候选 seed 里存在「萨满 + 复活半径内同伴」的洞穴图", false,
                    $"试过 {seeds.Length} 张图都没有 ⇒ 测不了（不是模块缺陷，但本轮也没测到）");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Melee（僵尸）行为：会靠近 → 进近战范围 → 出手。
        /// <para>
        /// 会因为**地形**（怪在墙后 / 要绕远）量到"几乎没靠近"，那是地形不是 AI。
        /// 本步要测的是 **AI 行为**，所以遍历若干只近战怪，取第一只"真的靠近并出手"的
        /// 只放宽"用哪一只来演"，**判据本身没放宽**（靠近 > 1.0 格 + 出手 > 0 次，逐字未改）。
        /// </para>
        /// </summary>
        private static void AiMelee()
        {
            const int maxTries = 6;
            var tried = 0;
            //   （实测：`[ OK ] Melee：候选里的近战怪会靠近并出手 (试了 6 只…没有一只同时满足…)`），
            //   且上面「会靠近 / 出手」两条真判据因此**永不执行**（死代码）⇒ 这是"没判的看起来像判了"。
            //   改成真比：走到兜底 = 没有任何一只满足 ⇒ 必须变红（不为变绿而放宽判据）。
            var satisfied = 0;

            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive || m.ai != MonsterAI.Melee) continue;
                if (tried >= maxTries) break;
                tried++;

                PlacePlayerAtDistance(m.Grid(), 5, 7.5f);   // 5 格起步、欧氏 ≤7.5 ⇒ 必在发现半径(8)内
                var before = DistanceToPlayer(m.Grid());

                //   窗口 = ⌈闭合到出手距离所需秒数⌉ + 事件余量（= 2 × 出手间隔 1.10s = 2.20s，至少 2 次机会）
                //   ① 闭合需求 = (起步欧氏距离 − 出手门槛) / 该怪的**格每秒**速度
                //      · 出手门槛 = `GameConst.MeleeRange` = 1.60 格
                //        出处 `Core/GameConst.cs:129`；生产侧同一把尺子 `MonsterAi.cs:193`
                //        （`if (dist <= GameConst.MeleeRange) return TryAttack(...)`）
                //      · 格每秒速度 = `monster_c.speed` × `MonsterTuning.SpeedToTilesPerSecond`
                //        出处 **生产实现** `MonsterModule.SpeedOf`（`Module/Monster/MonsterModule.cs:531-536`）
                //        × 0.2（`MonsterTuning.cs:120`，1 格 = 5 map 单位的换算）；下限钳 `MinMoveSpeed`
                //      日志里那只 `speed=1` 是**配表原值**（官方 `MonStats.Velocity`，单位 = map 单位/秒），
                //         **不是格/秒** —— 僵尸 `Velocity`=1 ⇒ **0.20 格/秒**（`MonsterTuning.cs:117` 逐行注明）。
                //   ② 僵尸实测代入：闭合需求 = (5.00 − 1.60) / 0.20 = **17.00s**；旧窗口 `6f` 只能走
                //      0.20 × 6 = **1.20 格**（且 `Grid()` 是**格取整** ⇒ 日志显示成 Δ1.00 格，实测 5.00→4.00 吻合）
                //   本改动只把**窗口**对上"到出手距离所需帧数"，判据行（`after < before - 1.0f` /
                //      `attacks > 0`）**一字未改**，也**没有**放宽任何阈值。
                var tileSpeed = TilesPerSecondOf(m);
                var needSeconds = Mathf.Max(0f, (before - GameConst.MeleeRange) / tileSpeed);
                var window = needSeconds + MonsterTuning.AttackIntervalSeconds * 2f;

                TickSim(window);

                var after = DistanceToPlayer(m.Grid());
                var attacks = Trace.AttacksBy(m.id);
                Console.WriteLine($"  Melee 候选 {tried}（m#{m.id} {m.name}，配表 speed={SpeedOf(m)}" +
                                  $"（官方 MonStats.Velocity，map 单位/秒）= {tileSpeed:0.00} 格/秒）：" +
                                  $"与玩家距离 {before:0.00} → {after:0.00} 格，该怪出手 {attacks} 次" +
                                  $"（窗口 {window:0.00}s = 闭合 {needSeconds:0.00}s + 出手余量 " +
                                  $"{MonsterTuning.AttackIntervalSeconds * 2f:0.00}s）");

                if (after < before - 1.0f && attacks > 0)
                {
                    //   （值就是上面日志里的 before/after/attacks）⇒ 判据可失败，且与文案逐字对应。
                    Check("Melee：会靠近（距离显著减小）", after < before - 1.0f,
                        $"{before:0.00} → {after:0.00}（m#{m.id}；判据 = 减小 > 1.0 格）");
                    Check("Melee：进入近战范围后出手攻击", attacks > 0, $"出手 {attacks} 次（m#{m.id}）");
                    satisfied++;
                    return;
                }
            }

            Check("Melee：候选里的近战怪会靠近并出手（★ 判据 = 至少一只同时满足「靠近 >1.0 格 + 出手 >0 次」）",
                satisfied > 0,
                tried > 0
                    ? $"试了 {tried} 只近战怪，**没有一只**同时满足「靠近 >1.0 格 + 出手 >0 次」 ⇒ 见逐只明细（落点/地形 或 追击链）"
                    : "血腥荒野里没有活着的近战怪");
        }

        private static void AiRangeKeepsDistance()
        {
            var m = PickMonster(x => x.ai == MonsterAI.Range);
            if (m == null)
            {
                Check("Range：刷到了远程怪", false, "none");
                return;
            }

            // 摆位口径必须用**真实出手上限** `MonsterTuning.RangedAttackMaxRange`（= **5.0**，C3 新增：
            //   用户「屏幕外都能打我」⇒ 出手距离由 `GameConst.RangedRange`(8) 收到 5.0，见该常量注释）。
            //   旧写法按"射程(8)"把玩家摆到欧氏 7 格 ⇒ 怪**合理地不出手**（实测日志
            //   `RequestMonsterAttack: 距离 7.07 > 射程 5.00 ⇒ 本次攻击取消`；5 格环上也实测 5.83 > 5.00）。
            //   现在摆在「保持距离 4.0 之外、出手上限 5.0 之内」那一段；断言（在射程内必须出手）一字未改。
            PlacePlayerAtDistance(m.Grid(), 4, MonsterTuning.RangedAttackMaxRange - 0.2f);
            var before = DistanceToPlayer(m.Grid());

            //   所需秒数 = **最坏情况下把距离拉回 `RangedKeepDistance` 的时间**：怪被摆到欧氏 ≤4.8 的环 4 上
            //   （见上一条注释的出处），`MonsterAi.Ranged` 在格距 < `MonsterTuning.RangedKeepDistance`(4.0)
            //   时先后撤、**后撤期间不射击** ⇒ 窗口必须覆盖"从 0 格撤到 4.0 格"这一段：
            //   4.0 格 ÷ 该怪的格每秒速度（出处见 `TilesPerSecondOf`） + 出手余量。
            //
            //   `Trace` 是**全局累计**且全程从不 `Clear()` ⇒ 累计量会把"本窗口内实际出手 **0** 次"
            //   报成"有出手"（= "跳过却记 OK" 的同类隐患）。与 `AiCowardFlees` 那条（:555-560）同款。
            //   实测（退化校验 B 的读数）：本场景这只尖刺鼠
            //   在窗口**前**的累计量恰为 **0** ⇒ 两种口径当前**同值**（不存在"本来红、被修绿"）。
            //   累计口径就会立刻误判 ⇒ 本条是**隐患消除**；判据行与阈值一字未改，不是放宽。
            var atkBefore = Trace.AttacksBy(m.id);
            TickSim(EventWindowSeconds(MonsterTuning.RangedKeepDistance / TilesPerSecondOf(m)));

            var after = DistanceToPlayer(m.Grid());
            var attacks = Trace.AttacksBy(m.id) - atkBefore;
            Console.WriteLine($"  Range（m#{m.id} {m.name}）：距离 {before:0.00} → {after:0.00} 格，" +
                              $"该怪出手 {attacks} 次（**窗口内差值**；玩家原地不动）");
            Check("Range：不会贴脸（保持距离 > 3.4 格）", after > MonsterKeepDistance() - 0.6f, $"{after:0.00}");
            Check("Range：在射程内出手射击", attacks > 0, $"出手 {attacks} 次");
        }

        private static void AiRangeBacksOff()
        {
            var m = PickMonster(x => x.ai == MonsterAI.Range && x.alive);
            if (m == null)
            {
                Check("Range 后撤：有可用远程怪", false, "none");
                return;
            }

            PlacePlayerAtDistance(m.Grid(), 2, 4f);     // 贴到 2 格（欧氏 ≤4）⇒ 必触发后撤
            var before = DistanceToPlayer(m.Grid());
            //   （判据 = `after > before + 0.5f` ⇒ 0.5 格；速度算法同 `TilesPerSecondOf` 的出处注释）。
            TickSim(EventWindowSeconds(0.5f / TilesPerSecondOf(m)));
            var after = DistanceToPlayer(m.Grid());

            Console.WriteLine($"  Range 后撤（m#{m.id} {m.name}）：玩家贴到 2 格 ⇒ 距离 {before:0.00} → {after:0.00} 格");
            Check("Range：贴脸时会后撤拉开距离", after > before + 0.5f, $"{before:0.00} → {after:0.00}");
        }

        private static void AiCowardFlees()
        {
            var m = PickMonster(x => x.ai == MonsterAI.Coward);
            if (m == null)
            {
                Check("Coward：刷到了逃跑型怪", false, "none");
                return;
            }

            // 打到 35% 血以下。
            //   「堕落者」的 maxHp 从 3 变 **2** ⇒ 走生产入口 `ApplyDamage` **无法**构造"低血但不死"
            //   （打 1 点只剩 50%、打 2 点就死）。⇒ 这里**把血量夹具调大**（**只动测试夹具**，
            //   不改生产代码）：本断言测的是**逃跑 AI 逻辑**，与具体血量数值无关。
            m.maxHp = 100;
            m.hp = 30;
            Check("Coward：已被打到低血（≤35%）", m.hp <= m.maxHp * 0.35f, $"{m.hp}/{m.maxHp}");

            PlacePlayerAtDistance(m.Grid(), 3, 5f);
            var before = DistanceToPlayer(m.Grid());
            var hpBefore = m.hp;

            //   `Trace` 是**全局累计**且全程从不 `Clear()`；本段之前 `AiMelee` / `AiRangeKeepsDistance`
            //   / `AiRangeBacksOff` 已在同一张图上跑过若干秒，而这只 Coward **满血时走的正是近战分支**
            //   （`MonsterAi.Coward` 的非逃跑路径 = `if (dist <= MeleeRange) TryAttack`）⇒ 它在那几段里
            //   打出的伤害事件**全部**被 `AttacksBy` 算进本段。
            //   这不是放宽：改成**差值口径**后，逃跑窗口内只要真出手一次，照样判红。
            var atkBefore = Trace.AttacksBy(m.id);

            //   ÷ 该怪的格每秒速度（算法出处见 `TilesPerSecondOf`）；余量见 `EventWindowSeconds`。
            //   代入堕落者（`fallen` Velocity=5 ⇒ 1.0 格/秒）= 0.5s + 2.2s = 2.7s
            //   ⇒ 仍落在 `MonsterTuning.CowardFleeSeconds`(3.0s) 的一次逃跑期内（"逃跑窗口内出手"语义不变）。
            TickSim(EventWindowSeconds(0.5f / TilesPerSecondOf(m)));

            var after = DistanceToPlayer(m.Grid());
            var attacks = Trace.AttacksBy(m.id) - atkBefore;
            Console.WriteLine($"  Coward（m#{m.id} {m.name}）：hp {hpBefore}/{m.maxHp}（{(float)hpBefore / m.maxHp:P0}）" +
                              $"⇒ 距离玩家 {before:0.00} → {after:0.00} 格，逃跑期间出手 {attacks} 次");
            Check("Coward：低血会逃跑（距离变大）", after > before + 0.5f, $"{before:0.00} → {after:0.00}");
            Check("Coward：逃跑时不再攻击（逃跑优先于出手）", attacks == 0, $"{attacks} 次");
            Check("Coward：日志里有 [Monster] flee（验收要贴的行）", _log.Has("[Monster] flee"), "见上方日志");
        }

        /// <summary>
        /// 必须**从 `MonSounds.txt` 取值**，不许凭空写常量。这里把生产侧的解析结果
        /// 与表里的原值逐类对账（值写死在本函数里，但每条都标了 `MonSounds.txt` 的 Id + 列名）。
        /// </summary>
        private static void MonsterVoiceTiming()
        {
            Section("4.1b 逐类怪物音效时序（MonSounds.txt：FsCnt 脚步间隔 / HitDelay 受击延迟，帧÷LogicFps）");

            // 原版 `MonSounds.txt` 的行（Id → HitDelay 帧、FsCnt；空列写 0 = 原版无该项）
            var hitDelay = new System.Collections.Generic.Dictionary<string, float>
            {
                { "fallen", 2f }, { "fallenshaman", 2f }, { "quillrat", 5f }, { "zombie", 2f },
                { "brute", 2f }, { "corruptrogue", 2f }, { "foulcrow", 2f }, { "wraith", 2f },
            };
            // 原版 Code → MonSounds 的 Id（出处 `MonStats.txt` 的 Code × MonSound 列）
            var idOf = new System.Collections.Generic.Dictionary<string, string>
            {
                { "fa", "fallen" }, { "fs", "fallenshaman" }, { "si", "quillrat" }, { "zm", "zombie" },
                { "ye", "brute" }, { "cr", "corruptrogue" }, { "bk", "foulcrow" }, { "wr", "wraith" },
            };
            var fsCnt = new System.Collections.Generic.Dictionary<string, float>
            {
                { "fa", 2f }, { "fs", 2f }, { "zm", 2f }, { "ye", 2f }, { "cr", 2f }, { "bk", 2f },
                // si(尖刺鼠) / wr(幽灵)：`MonSounds.txt` 的 `FsCnt` 列为空 ⇒ 原版没有移动音
            };

            // 原版 `MonSounds.txt` 的 `DeaDelay`（死亡音延迟帧）：除 quillrat=4 外全类 = 1
            var deaDelay = new System.Collections.Generic.Dictionary<string, float>
            {
                { "fallen", 1f }, { "fallenshaman", 1f }, { "quillrat", 4f }, { "zombie", 1f },
                { "brute", 1f }, { "corruptrogue", 1f }, { "foulcrow", 1f }, { "wraith", 1f },
            };

            var seen = new System.Collections.Generic.HashSet<string>();
            var badDelay = new System.Collections.Generic.List<string>();
            var badStep = new System.Collections.Generic.List<string>();
            var badDeath = new System.Collections.Generic.List<string>();

            foreach (var m in _ctx.Monster.All)
            {
                if (m == null) continue;
                var code = Diablo2.Module.View.SpriteFrames.SpriteCodeOf(m.kindId);
                if (string.IsNullOrEmpty(code) || !idOf.ContainsKey(code) || !seen.Add(code)) continue;

                var id = idOf[code];
                var wantDelay = hitDelay[id] / Diablo2.Module.Monster.MonsterTuning.LogicFps;
                var gotDelay = Diablo2.Module.Monster.MonsterSfx.HitDelaySeconds(m);
                if (System.Math.Abs(gotDelay - wantDelay) > 1e-5f)
                    badDelay.Add($"{code}({id}) 期望 {wantDelay:0.###}s 实得 {gotDelay:0.###}s");

                float cnt;
                var wantStep = fsCnt.TryGetValue(code, out cnt) ? 1f / cnt : 0f;
                var gotStep = Diablo2.Module.Monster.MonsterSfx.StepPeriodTiles(m);
                if (System.Math.Abs(gotStep - wantStep) > 1e-5f)
                    badStep.Add($"{code}({id}) 期望 {wantStep:0.###} 格/步 实得 {gotStep:0.###}");

                var wantDeath = deaDelay[id] / Diablo2.Module.Monster.MonsterTuning.LogicFps;
                var gotDeath = Diablo2.Module.Monster.MonsterSfx.DeathDelaySeconds(m);
                if (System.Math.Abs(gotDeath - wantDeath) > 1e-5f)
                    badDeath.Add($"{code}({id}) 期望 {wantDeath:0.###}s 实得 {gotDeath:0.###}s");
            }

            Check("逐类受击延迟 = `MonSounds.HitDelay` 帧 ÷ LogicFps(25)（quillrat=5 ⇒ 0.2s，其余 2 ⇒ 0.08s）",
                badDelay.Count == 0 && seen.Count > 0,
                badDelay.Count == 0 ? $"已对账 {seen.Count} 类" : string.Join("; ", badDelay));
            Check("脚步间隔 = 1 / `MonSounds.FsCnt`（有脚步的 6 类 = 2 ⇒ **0.5 格一步**；尖刺鼠/幽灵 = 0 = 不响）",
                badStep.Count == 0 && seen.Count > 0,
                badStep.Count == 0 ? $"已对账 {seen.Count} 类（半格一步，不再是跨格一次）" : string.Join("; ", badStep));
            Check("死亡音延迟 = `MonSounds.DeaDelay` 帧 ÷ LogicFps(25)（quillrat=4 ⇒ 0.16s，其余 1 ⇒ 0.04s）",
                badDeath.Count == 0 && seen.Count > 0,
                badDeath.Count == 0 ? $"已对账 {seen.Count} 类" : string.Join("; ", badDeath));

            Console.WriteLine();
        }

        /// <summary>
        /// 期望值 `wantDelay` **从表经 `MonsterSfx.DeathDelaySeconds` 算**（不硬编码）。
        /// </summary>
        private static void MonsterDeathDelay()
        {
            Section("4.1d 死亡音延迟（行为）：MonSounds.DeaDelay 帧 ÷ LogicFps ⇒ 同帧不响、到期才响");

            MonsterState target = null;
            foreach (var m in _ctx.Monster.All)
            {
                if (m != null && m.alive) { target = m; break; }
            }
            if (target == null)
            {
                Check("刷到一只可击杀的怪", false, "none");
                return;
            }

            // 期望延迟：**从表算**（每类的 `DeaDelay` 帧 ÷ LogicFps），不是写死的数
            var wantDelay = Diablo2.Module.Monster.MonsterSfx.DeathDelaySeconds(target);
            var wantFrames = wantDelay * Diablo2.Module.Monster.MonsterTuning.LogicFps;

            var dieBefore = CountCalls("sfxAt:monster_die_");
            _ctx.Monster.ApplyDamage(target.id, 999999, Diablo2.Def.DamageType.Physical);
            var atDeath = CountCalls("sfxAt:monster_die_") - dieBefore;
            Check("死亡**同帧**不播死亡音（延迟 > 0 ⇒ 排期到 `DeaDelay` 之后起播）",
                atDeath == 0, $"同帧 {atDeath} 次");

            var waited = 0f;
            var fired = 0;
            var limit = (int)(wantDelay / Dt) + 8;
            for (var i = 0; i <= limit && fired == 0; i++)
            {
                _ctx.Monster.Tick(Dt);
                waited += Dt;
                fired = CountCalls("sfxAt:monster_die_") - dieBefore;
            }

            Check($"死亡音在 {waited:0.###}s 后起播（= `DeaDelay` {wantFrames:0} 帧 ÷ LogicFps ⇒ 期望 {wantDelay:0.###}s，±1 tick）",
                fired == 1 && System.Math.Abs(waited - wantDelay) <= Dt + 1e-5f,
                $"waited={waited:0.###}s 期望={wantDelay:0.###}s Dt={Dt:0.###} fired={fired}");
            Console.WriteLine();
        }

        /// <summary>
        /// <para>
        /// 判的到底是什么：`FsCnt=2` ⇒ 每 **0.5 格**一步。若哪天被改回"跨格一次"，
        /// 脚步次数会掉到 ≈ 跨格次数（比值 ≈ 1）⇒ 本条立刻判红。
        /// 期望值**不是硬编码的 2**：比值门槛 1.5 来自「0.5 格/步 vs 1 格/步」这两档之间的空隙，
        ///   具体每类的 `FsCnt` 仍由 `MonSsounds.txt` 经 `MonsterSfx.StepPeriodTiles` 给。
        /// </para>
        /// </summary>
        private static void MonsterStepCadence()
        {
            Section("4.1c 脚步节奏（行为）：FsCnt=2 ⇒ 半格一步，脚步次数显著多于跨格次数");

            MonsterState anchor = null;
            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive) continue;
                if (Diablo2.Module.Monster.MonsterSfx.StepPeriodTiles(m) > 0f) { anchor = m; break; }
            }
            if (anchor == null)
            {
                Check("刷到了「有脚步音」的怪（FsCnt 非空）", false, "本图没有 ⇒ 测不了（不是模块缺陷）");
                return;
            }

            PlacePlayerAtDistance(anchor.Grid(), 3, 5f);      // 进仇恨 ⇒ 怪会真的走起来

            var stepsBefore = CountStepCalls();
            var last = new System.Collections.Generic.Dictionary<int, Vector2Int>();
            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive) continue;
                if (Diablo2.Module.Monster.MonsterSfx.StepPeriodTiles(m) > 0f) last[m.id] = m.Grid();
            }

            var gridMoves = 0;
            var ticks = (int)(6f / Dt);
            for (var i = 0; i < ticks; i++)
            {
                _ctx.Monster.Tick(Dt);
                foreach (var m in _ctx.Monster.All)
                {
                    if (m == null || !m.alive) continue;
                    Vector2Int prev;
                    if (!last.TryGetValue(m.id, out prev)) continue;
                    var g = m.Grid();
                    if (g != prev) { gridMoves++; last[m.id] = g; }
                }
            }

            var steps = CountStepCalls() - stepsBefore;
            var ratio = gridMoves > 0 ? (float)steps / gridMoves : 0f;
            Check($"脚步 {steps} 次 / 跨格 {gridMoves} 次 ⇒ 比值 {ratio:0.##}"
                  + "（FsCnt=2 = 半格一步 ⇒ 期望 ≈2；若退回「跨格一次」只会有 ≈1 ⇒ 判红）",
                steps > 0 && gridMoves > 0 && ratio >= 1.5f,
                $"steps={steps} gridMoves={gridMoves} ratio={ratio:0.##}");
            Console.WriteLine();
        }

        private static int CountStepCalls()
        {
            return CountCalls("sfxAt:monster_step_");
        }

        /// <summary>数 `RecordingAudio` 里某个前缀被请求了几次（**总数**，调用方自己取差值口径）。</summary>
        private static int CountCalls(string prefix)
        {
            var n = 0;
            foreach (var c in _audio.Calls)
            {
                if (c != null && c.StartsWith(prefix, System.StringComparison.Ordinal)) n++;
            }
            return n;
        }

        /// <summary>
        /// 4.2 萨满复活：**配对必须按系统自己的距离口径找**。
        /// <para>
        /// 选配对，注释声称"世界单位 ≤ `ShamanReviveRange`"。**那是错的** —— 生产口径的出处：
        /// `Module/Monster/MonsterModule.cs::FindRevivableCorpse` 比的是
        /// `Vector2.Distance(shaman.Pos, corpse.Pos)`，而 `MonsterRuntime.Pos` 是
        /// **连续格坐标（格中心制）**（`Module/Monster/MonsterRuntime.cs` 文件头第 11~14 行：
        /// "格 (gx,gy) 覆盖 [gx,gx+1]×[gy,gy+1]，中心 = (gx+0.5, gy+0.5)"，
        /// 即 `Pos == (gx+0.5, gy+0.5)` 时才等于 `Iso.GridToWorld(gx,gy)` 的**输入**）
        /// ⇒ 该距离的单位是 **格**，与 `MonsterTuning.ShamanReviveRange`（注释写明"最大距离（**格**）"）同量纲。
        /// </para>
        /// <para>
        /// 等距世界单位与格欧氏**不是同一个度量**（`world.x=(gx-gy)·1.0`、`world.y=-(gx+gy+1)·0.5`）：
        /// 实测同一对（萨满 (17,59) ↔ 同伴 (10,54)）**格欧氏 = 8.60 > 7**（生产正确返回 -1，
        /// 因为这对真的不在复活半径内），而**等距世界距离只有 6.32 ≤ 7** ⇒ 旧判据把一对"够不着"的
        /// 组合当成"可测场景"，杀掉同伴后萨满当然复活不了 ⇒ 3 项断言连锁变红（既存 FAIL 的真因）。
        /// `Chebyshev` 格距（= 7）同样不能用：对角时 7 格 Chebyshev = 9.90 格欧氏。
        /// </para>
        /// <para>⇒ 判定一律走 <see cref="ReviveScanDistance"/>（与生产同一把尺子）。断言本身一字未改。</para>
        /// </summary>
        /// <returns>true = 本图有可测场景且已完整测过；false = 本图没有可测场景（换 seed）。</returns>
        private static bool AiShamanRevives(int seed)
        {
            MonsterState shaman = null, nearest = null;
            var nearestD = float.MaxValue;
            foreach (var s in _ctx.Monster.All)
            {
                if (s == null || !s.alive || s.ai != MonsterAI.Shaman) continue;
                foreach (var m in _ctx.Monster.All)
                {
                    if (m == null || !m.alive || m.id == s.id) continue;
                    var d = ReviveScanDistance(s, m);
                    if (d >= nearestD) continue;
                    nearestD = d;
                    shaman = s;
                    nearest = m;
                }
            }
            if (shaman == null)
            {
                Check($"Shaman(seed={seed})：洞穴里刷到了堕落萨满", false, "none");
                return true;                       // 有萨满才算"测过"；没萨满属模块问题，直接判失败
            }
            if (nearestD > MonsterTuning.ShamanReviveRange)
            {
                Console.WriteLine($"  seed={seed}：最近的一对（萨满, 同伴）**格欧氏距离** = {nearestD:0.00} > " +
                                  $"ShamanReviveRange = {MonsterTuning.ShamanReviveRange}（格）" +
                                  $" ⇒ 本图无场景，换图");
                return false;
            }

            // 本行原为硬编码 `true`（只当"场景构造成功"的打印）⇒ 改成真的比一遍：判据更严，不是放宽。
            Check($"Shaman(seed={seed})：复活半径（{MonsterTuning.ShamanReviveRange:0.#} 格）内有同伴可复活",
                nearestD <= MonsterTuning.ShamanReviveRange, $"最近一对 = {nearestD:0.00} 格（格欧氏）");

            // 杀这一对里那个"同伴"（它在复活半径内 ⇒ 萨满必然能吃尸体复活它）
            var companion = nearest;
            Console.WriteLine($"  Shaman m#{shaman.id} 与同伴 m#{companion.id} 的**格欧氏距离** = " +
                              $"{ReviveScanDistance(shaman, companion):0.00} " +
                              $"（ShamanReviveRange = {MonsterTuning.ShamanReviveRange}（格）；" +
                              $"Chebyshev 格距 = {Iso.GridDistance(shaman.Grid(), companion.Grid())}；" +
                              $"等距世界距离 = {WorldDistance(shaman, companion):0.00}（⛔ 非判定口径，仅披露））");

            _ctx.Monster.ApplyDamage(companion.id, 9999, DamageType.Physical);
            Check("Shaman：同伴已死且**保留可复活尸体**", !companion.alive && companion.corpseUsable,
                $"m#{companion.id} alive={companion.alive} corpseUsable={companion.corpseUsable}");

            PlacePlayerAtDistance(shaman.Grid(), 5, 7f);
            //   `MonsterTuning.ShamanReviveCooldownSeconds`（0.6s，= 官方 aidel 15 帧 ÷ 25fps，见其注释）
            //   + 出手余量 ⇒ 覆盖"首个思考帧就复活"与"冷却后才复活"两种情形。
            TickSim(EventWindowSeconds(MonsterTuning.ShamanReviveCooldownSeconds));

            Console.WriteLine($"  Shaman（m#{shaman.id} {shaman.name}）复活 m#{companion.id} {companion.name}：" +
                              $"alive={companion.alive} hp={companion.hp}/{companion.maxHp} corpseUsable={companion.corpseUsable}");
            Check("Shaman：日志里有 [Monster] revive（验收要贴的行）", _log.Has("[Monster] revive"), "见上方日志");
            Check("Shaman：同伴真的被复活（alive=true 且血量 > 0）", companion.alive && companion.hp > 0,
                $"alive={companion.alive} hp={companion.hp}");
            Check("Shaman：复活消耗了尸体（corpseUsable=false）", !companion.corpseUsable, "见上行");
            return true;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. 精英怪（monumod_c 倍率）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step5_EliteAffix()
        {
            Section("5. 精英怪：刷出 champion/unique，属性按 monumod_c 倍率放大");

            PrepareMap(AreaId.BloodMoor, 555001);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            MonsterState elite = null;
            var eliteCount = 0;
            foreach (var m in _ctx.Monster.All)
            {
                if (!m.isChampion) continue;
                eliteCount++;
                if (elite == null) elite = m;
            }

            Check("刷出了精英怪（champion/unique）", eliteCount > 0, $"精英 {eliteCount} 只 / 总 {_ctx.Monster.All.Count} 只");
            if (elite == null) return;

            var row = Table.Tables.Default.Monster.Get(elite.kindId);
            var mod = Table.Tables.Default.Monumod.Get(elite.modId);
            Check("精英怪带词缀 id 且能在 monumod_c 里查到", mod != null, mod != null ? $"modId={mod.Id} {mod.Name}" : "null");
            if (row == null || mod == null) return;

            MonsterState normal = null;
            foreach (var m in _ctx.Monster.All)
            {
                if (m.kindId == elite.kindId && !m.isChampion && m.alive) { normal = m; break; }
            }

            Console.WriteLine($"  普通 {row.Name}：hp={row.Hp} dmg={row.DmgMin}-{row.DmgMax} ac={row.Ac} ar={row.Ar} exp={row.Exp}");
            Console.WriteLine($"  精英「{elite.name}」词缀={mod.Name}(kind={mod.Kind})：hp={elite.maxHp} " +
                              $"dmg={elite.damageMin}-{elite.damageMax} ac={elite.defense} ar={elite.attackRating} exp={elite.exp}");
            Console.WriteLine($"  词缀倍率（monumod_c）：hp_mul={mod.HpMul} dmg_mul={mod.DmgMul} ac_mul={mod.AcMul} tohit_add={mod.TohitAdd}%");
            if (normal != null)
            {
                Console.WriteLine($"  同种普通怪 m#{normal.id}：hp={normal.maxHp} dmg={normal.damageMin}-{normal.damageMax} " +
                                  $"ac={normal.defense} ar={normal.attackRating}");
            }

            var expectHp = Mathf.Max(1, Mathf.RoundToInt(row.Hp * mod.HpMul));
            var expectDmgMax = Mathf.Max(Mathf.Max(1, Mathf.RoundToInt(row.DmgMin * mod.DmgMul)),
                Mathf.RoundToInt(row.DmgMax * mod.DmgMul));
            Check("精英血量 = round(monster_c.hp × monumod_c.hp_mul)", elite.maxHp == expectHp, $"{elite.maxHp} vs {expectHp}");
            Check("精英伤害 = round(monster_c.dmg × monumod_c.dmg_mul)", elite.damageMax == expectDmgMax,
                $"{elite.damageMax} vs {expectDmgMax}");
            Check("精英名带词缀前缀（HUD 可显示）", elite.name.StartsWith(mod.Name, StringComparison.Ordinal),
                elite.name);
            Check("精英比同种普通怪强（hp/dmg 都更大）", normal == null || (elite.maxHp > normal.maxHp && elite.damageMax >= normal.damageMax),
                normal == null ? "(没有同种普通怪可比)" : $"{normal.maxHp}/{normal.damageMax} → {elite.maxHp}/{elite.damageMax}");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 6. 死亡链
        // ═════════════════════════════════════════════════════════════════════
        private static void Step6_DeathChain()
        {
            Section("6. 死亡链：怪物死亡 → 经验给玩家 → 掉落被触发");

            PrepareMap(AreaId.BloodMoor, 666001);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            var victim = PickMonster(m => m.alive);
            if (victim == null)
            {
                Check("死亡链：有可杀的怪", false, "none");
                return;
            }

            _item.DropLootCalls.Clear();
            _view.Deaths.Clear();
            var expBefore = _player.Exp;
            var killedBefore = _monsterKilledEvents;
            var exp = victim.exp;
            var grid = victim.Grid();

            var killedLines = _log.Lines.Count;
            _ctx.Monster.ApplyDamage(victim.id, 9999, DamageType.Physical);

            Console.WriteLine($"  击杀 m#{victim.id} {victim.name}（exp={exp}）");
            Console.WriteLine($"  经验：{expBefore} → {_player.Exp}（+{_player.Exp - expBefore}）");
            Console.WriteLine($"  掉落调用：{( _item.DropLootCalls.Count > 0 ? string.Join(" | ", _item.DropLootCalls) : "(无)")}");

            Check("MonsterKilled 事件已发", _monsterKilledEvents == killedBefore + 1, $"{killedBefore} → {_monsterKilledEvents}");
            Check("怪物已死且保留尸体（corpseUsable 供萨满复活）", !victim.alive && victim.corpseUsable,
                $"alive={victim.alive} corpseUsable={victim.corpseUsable}");
            Check("经验已给玩家", _player.Exp == expBefore + exp, $"+{_player.Exp - expBefore}（期望 +{exp}）");
            Check("掉落被触发（IItemModule.DropLoot 被调用 1 次）", _item.DropLootCalls.Count == 1,
                _item.DropLootCalls.Count.ToString());
            Check("死亡表现已播（ViewModule.PlayDeath）", _view.Deaths.Contains(victim.id), string.Join(",", _view.Deaths));

            // 只看**本次击杀**产生的那几行（日志里前面步骤已经死过怪，不能从 0 开始找）
            var from = killedLines;
            var iDeath = _log.IndexOf("死亡（等级", from);
            var iExp = _log.IndexOf("[击杀链] m#" + victim.id, from);
            var iDrop = _log.IndexOf("[击杀链] 掉落触发", from);
            Console.WriteLine($"  本次击杀的日志序列（从第 {from} 行起）：死亡={iDeath} 经验={iExp} 掉落={iDrop}");
            for (var i = from; i < Mathf.Min(from + 8, _log.Lines.Count); i++)
            {
                Console.WriteLine("    " + _log.Lines[i]);
            }
            Check("日志顺序 = 死亡 → 经验 → 掉落", iDeath >= 0 && iExp > iDeath && iDrop > iExp,
                $"{iDeath} < {iExp} < {iDrop}");

            Section("6b. IItemModule 为 null 时的降级（必须有明确告警，不静默）");
            _ctx.Item = null;
            var warnBefore = _log.Count("⇒ 本次掉落丢失");
            for (var i = 0; i < 2; i++)
            {
                var v = PickMonster(m => m.alive);
                if (v == null) break;
                _ctx.Monster.ApplyDamage(v.id, 9999, DamageType.Physical);
            }
            var warnAfter = _log.Count("⇒ 本次掉落丢失");
            Console.WriteLine($"  Item 缺失时的告警行数：{warnBefore} → {warnAfter}（杀 2 只怪）");
            Check("Item 缺失时会告警（不静默丢掉落）", warnAfter >= 1, warnAfter.ToString());
            Check("同一告警只出一条（WarnOnce 语义，不刷屏）", warnAfter == 1, warnAfter.ToString());
            _ctx.Item = _item;
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. 掉落格可走
        // ═════════════════════════════════════════════════════════════════════
        private static void Step7_DropGridsWalkable()
        {
            Section("7. 掉落落点合理性：所有掉落格 Walkable == true");

            PrepareMap(AreaId.DenOfEvil, 777003);
            _ctx.Monster.SpawnArea(AreaId.DenOfEvil);

            _item.DropGrids.Clear();
            var killed = 0;
            while (killed < 5)
            {
                var v = PickMonster(m => m.alive);
                if (v == null) break;
                _ctx.Monster.ApplyDamage(v.id, 9999, DamageType.Physical);
                killed++;
            }

            var allWalkable = true;
            for (var i = 0; i < _item.DropGrids.Count; i++)
            {
                if (!_ctx.Map.Walkable(_item.DropGrids[i]))
                {
                    allWalkable = false;
                    Console.WriteLine($"  ❌ 掉落格不可走：{_item.DropGrids[i]}");
                }
            }
            Console.WriteLine($"  杀 {killed} 只怪 → {_item.DropGrids.Count} 个掉落格：" +
                              string.Join(" ", _item.DropGrids.ConvertAll(g => $"({g.x},{g.y})")));
            Check("所有掉落格都可走", _item.DropGrids.Count > 0 && allWalkable, $"{_item.DropGrids.Count} 个");

            // 刷怪点 / 随机可走格抽样
            var rng = new Rng(2024);
            var ok = true;
            for (var i = 0; i < 200; i++)
            {
                if (!_ctx.Map.Walkable(_ctx.Map.RandomWalkableTile(rng))) { ok = false; break; }
            }
            Check("RandomWalkableTile 200 次抽样全部落在可走格上", ok, "200/200");

            var spawnsWalkable = true;
            for (var i = 0; i < _ctx.Map.MonsterSpawns.Count; i++)
            {
                if (!_ctx.Map.Walkable(_ctx.Map.MonsterSpawns[i])) spawnsWalkable = false;
            }
            Check($"洞穴刷新点全部可走（{_ctx.Map.MonsterSpawns.Count} 个）", spawnsWalkable, "见上");

            var monstersWalkable = true;
            foreach (var m in _ctx.Monster.All)
            {
                if (!_ctx.Map.Walkable(m.Grid())) monstersWalkable = false;
            }
            Check("所有怪物都站在可走格上", monstersWalkable, $"{_ctx.Monster.All.Count} 只");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 8. 技能树
        // ═════════════════════════════════════════════════════════════════════
        private static void Step8_SkillTree()
        {
            Section("8. 技能树：5 职业 × 3 系 / CanLearn / Learn / 绑键");

            var classes = new[]
            {
                PlayerClass.Amazon, PlayerClass.Sorceress, PlayerClass.Necromancer,
                PlayerClass.Paladin, PlayerClass.Barbarian,
            };

            foreach (var cls in classes)
            {
                var save = NewSave(cls, 0);
                _ctx.Skill.ResetForClass(cls, save);
                var tree = _ctx.Skill.BuildTree();

                var t0 = CountTree(tree, 0);
                var t1 = CountTree(tree, 1);
                var t2 = CountTree(tree, 2);
                Console.WriteLine($"  {cls}（{(int)cls}）：技能 {tree.skills.Count} 个，系分布 {t0}/{t1}/{t2}，" +
                                  $"技能点 {tree.skillPoints}，可学 {CountLearnable(tree)} 个");

                Check($"{cls}：技能表非空", tree.skills.Count > 0, tree.skills.Count.ToString());
                Check($"{cls}：3 个系都有技能（1/2/3 系 = {t0}/{t1}/{t2}）", t0 > 0 && t1 > 0 && t2 > 0, $"{t0}/{t1}/{t2}");
                Check($"{cls}：SkillTreeArgs 三个列表长度对齐（skills/learnedLevels/learnable）",
                    tree.learnedLevels.Count == tree.skills.Count && tree.learnable.Count == tree.skills.Count,
                    $"{tree.skills.Count}/{tree.learnedLevels.Count}/{tree.learnable.Count}");

                var rowsOk = true;
                for (var i = 0; i < tree.skills.Count; i++)
                {
                    var s = tree.skills[i];
                    if (s.slotRow < 0 || s.slotRow > 5 || s.slotCol < 0 || s.tree < 0 || s.tree > 2) rowsOk = false;
                }
                Check($"{cls}：面板行列在合法范围（行 0-5 / 列 ≥0）", rowsOk, "见代码断言");
            }

            // CanLearn / Learn 的正负路径（用亚马逊）
            Section("8b. CanLearn / Learn 的正负路径（亚马逊）");
            var save2 = NewSave(PlayerClass.Amazon, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Amazon, save2);
            _player.SetLevel(1);
            _player.SetSkillPoints(0);

            var first = FirstLearnable(PlayerClass.Amazon);
            Check("找到「1 级可学、无前置」的第一个技能", first != null, first != null ? $"{first.name}#{first.id}" : "null");
            if (first == null) return;

            Check("技能点 = 0 时 CanLearn = false（点数不足）", !_ctx.Skill.CanLearn(first.id), "见日志的拒绝原因");

            _player.SetSkillPoints(1);
            Check("技能点 = 1 时 CanLearn = true", _ctx.Skill.CanLearn(first.id), "true");
            var ok = _ctx.Skill.Learn(first.id);
            Console.WriteLine($"  Learn({first.name}#{first.id}) → {ok}；等级 {_ctx.Skill.GetLevel(first.id)}；剩余技能点 {_player.SkillPoints}");
            Check("Learn 成功且扣 1 点技能点", ok && _ctx.Skill.GetLevel(first.id) == 1 && _player.SkillPoints == 0,
                $"level={_ctx.Skill.GetLevel(first.id)} points={_player.SkillPoints}");
            Check("已学等级反映到 BuildTree.learnedLevels", LearnedLevelOf(_ctx.Skill.BuildTree(), first.id) == 1, "1");
            Check("技能点用尽后再学同一技能 = false", !_ctx.Skill.Learn(first.id), "false");

            // 前置技能
            var dep = FirstWithPrereq(PlayerClass.Amazon, first.id);
            if (dep != null)
            {
                _player.SetLevel(99);
                _player.SetSkillPoints(10);
                var prereqLevel = _ctx.Skill.GetLevel(dep.reqSkill);
                Check($"前置未学/不足时 CanLearn = false（{dep.name} 需要前置 #{dep.reqSkill}）×{dep.reqSkillLevel} 级）",
                    prereqLevel < dep.reqSkillLevel && !_ctx.Skill.CanLearn(dep.id),
                    $"前置等级 {prereqLevel}");
                while (_ctx.Skill.GetLevel(dep.reqSkill) < dep.reqSkillLevel) _ctx.Skill.Learn(dep.reqSkill);
                Check($"补满前置后可学（{dep.name}）", _ctx.Skill.CanLearn(dep.id), "true");
            }

            // 绑键 + 存档镜像
            _ctx.Skill.AssignToButton(1, first.id);
            Check("AssignToButton(1, skill) 生效（GetButtonSkill 一致）", _ctx.Skill.GetButtonSkill(1) == first.id,
                _ctx.Skill.GetButtonSkill(1).ToString());
            Check("绑键镜像到存档 buttonSkills[1]",
                save2.buttonSkills != null && save2.buttonSkills.Count >= 2 && save2.buttonSkills[1] == first.id,
                save2.buttonSkills != null ? string.Join(",", save2.buttonSkills) : "null");
            Check("已学技能镜像到存档 skillIds/skillLevels",
                save2.skillIds.Count == save2.skillLevels.Count && save2.skillIds.Count > 0,
                $"{save2.skillIds.Count} 条");
            //   改成**真的绑一次被动技能**：`AssignToButton` 内部 `ValidateSelectable` 判 `passive` 拒
            //   ⇒ 原绑定必须**一字不变**，且拒绝日志必须出现（两处任缺 ⇒ 变红）。
            Table.BaseSkillRow passiveRow = null;
            foreach (var r in Table.Tables.Default.Skill.All())
            {
                if (r == null || r.Class != (int)PlayerClass.Amazon || r.Passive == 0) continue;
                passiveRow = r;
                break;
            }
            var bindBefore = _ctx.Skill.GetButtonSkill(1);
            var passiveRejected = false;
            if (passiveRow != null)
            {
                _ctx.Skill.AssignToButton(1, passiveRow.Id);
                passiveRejected = _ctx.Skill.GetButtonSkill(1) == bindBefore
                                  && _log.Count("被动技能不能绑定到左右键") >= 1;
            }
            Check("被动技能不能绑到左右键（防御：AssignToButton 保持原绑定 + 拒绝日志）",
                passiveRow != null && passiveRejected,
                passiveRow == null
                    ? "skill_c 里没有 Amazon 的被动技能行 ⇒ 判不了（配表核对）"
                    : $"被动 #{passiveRow.Id} {passiveRow.Name}：绑定保持 {bindBefore}；"
                      + $"拒绝日志 {_log.Count("被动技能不能绑定到左右键")} 条");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 9. 施放：扣法力 + 冷却
        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // 16. R4（impl-I）：F1~F8 技能槽 → 左右键技能格绑定（真 SkillModule）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step16_SkillSlotBinding()
        {
            Section("16. ★ R4：F1~F8 技能槽 → 左右键技能格（真 SkillModule；改动前 8 键 0 消费）+ 存档往返");

            var save = NewSave(PlayerClass.Amazon, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Amazon, save);
            _player.SetLevel(99);
            _player.SetSkillPoints(99);

            // 多趟学（前置链要逐级满足）
            for (var pass = 0; pass < 4; pass++)
                foreach (var def in _ctx.Skill.Available) _ctx.Skill.Learn(def.id);

            // 期望表 = 生产口径的独立复述：已学（等级>0）且**非被动**，顺序 = `Available`（技能树顺序）
            var expected = new List<SkillDef>();
            foreach (var def in _ctx.Skill.Available)
            {
                if (_ctx.Skill.GetLevel(def.id) <= 0) continue;
                var row = Table.Tables.Default.Skill.Get(def.id);
                if (row != null && row.Passive != 0) continue;
                expected.Add(def);
            }
            Check("亚马逊已学且可主动施放的技能 ≥4 个（供 F1~F4 / F5~F8 绑定）", expected.Count >= 4,
                $"{expected.Count} 个（已学共 {CountLearned()} 个，顺序 = 技能树 tree→reqLevel→id）");
            if (expected.Count < 4) return;

            SkillButtonsArgs lastButtons = null;
            Game.Event.On<SkillButtonsArgs>(Events.SkillButtonsChanged, a => lastButtons = a);

            // ── ① F1~F4 ⇒ 左键 = 已学可施放第 1~4 个（逐个发、逐个核）──
            var leftOk = true;
            var leftDetail = string.Empty;
            for (var slot = 1; slot <= 4; slot++)
            {
                Game.Event.Emit(Events.SkillSlotAssignRequest, slot);
                var want = expected[GameKeyAlias.SkillSlotIndex(slot)];
                var got = _ctx.Skill.GetButtonSkill(0);
                if (got != want.id)
                {
                    leftOk = false;
                    leftDetail += $" F{slot}:{got}≠{want.id}";
                }
            }
            Check("F1~F4 ⇒ 左键依次绑到「已学可施放」第 1~4 个（下标 = (slot-1)%4）",
                leftOk, leftOk ? $"最终左键={DescribeSkill(_ctx.Skill.GetButtonSkill(0))}" : leftDetail);

            // ── ② F5~F8 ⇒ 右键 = 同表第 1~4 个 ──
            var rightOk = true;
            var rightDetail = string.Empty;
            for (var slot = 5; slot <= 8; slot++)
            {
                Game.Event.Emit(Events.SkillSlotAssignRequest, slot);
                var want = expected[GameKeyAlias.SkillSlotIndex(slot)];
                var got = _ctx.Skill.GetButtonSkill(1);
                if (got != want.id)
                {
                    rightOk = false;
                    rightDetail += $" F{slot}:{got}≠{want.id}";
                }
            }
            Check("F5~F8 ⇒ 右键依次绑到同表第 1~4 个（下标 = (slot-1)%4）",
                rightOk, rightOk ? $"最终右键={DescribeSkill(_ctx.Skill.GetButtonSkill(1))}" : rightDetail);

            // ── ③ 存档镜像 + 读档往返（重启后仍在）──
            Check("绑定镜像到存档 buttonSkills[0]/[1]（格式不变：既有字段）",
                save.buttonSkills != null && save.buttonSkills.Count >= 2
                && save.buttonSkills[0] == _ctx.Skill.GetButtonSkill(0)
                && save.buttonSkills[1] == _ctx.Skill.GetButtonSkill(1),
                save.buttonSkills != null ? string.Join(",", save.buttonSkills) : "null");

            var reload = NewSave(PlayerClass.Amazon, 0);
            reload.skillIds = new List<int>(save.skillIds);
            reload.skillLevels = new List<int>(save.skillLevels);
            reload.buttonSkills = new List<int>(save.buttonSkills);
            _ctx.Skill.ResetForClass(PlayerClass.Amazon, reload);
            Check("读档重建（ResetForClass）后左右键绑定与存档一致 ⇒ 重启后仍在",
                _ctx.Skill.GetButtonSkill(0) == save.buttonSkills[0]
                && _ctx.Skill.GetButtonSkill(1) == save.buttonSkills[1],
                $"左={DescribeSkill(_ctx.Skill.GetButtonSkill(0))} 右={DescribeSkill(_ctx.Skill.GetButtonSkill(1))}");

            // ── ④ HUD 数据源：SkillButtonsChanged 真的发了，且载荷 = 左右两格 + 显示名 ──
            Check("发 Events.SkillButtonsChanged（载荷 Def.SkillButtonsArgs：左右 id + 显示名）",
                lastButtons != null
                && lastButtons.leftId == _ctx.Skill.GetButtonSkill(0)
                && lastButtons.rightId == _ctx.Skill.GetButtonSkill(1)
                && !string.IsNullOrEmpty(lastButtons.leftName) && !string.IsNullOrEmpty(lastButtons.rightName),
                lastButtons == null ? "(没发事件)"
                    : $"左={lastButtons.leftName}({lastButtons.leftId}) 右={lastButtons.rightName}({lastButtons.rightId})");

            // ── ⑤ 非预期分支：还没有已学技能时按 F ⇒ 不改绑定（并留 Warn）──
            _ctx.Skill.ResetForClass(PlayerClass.Amazon, NewSave(PlayerClass.Amazon, 0));
            Check("换到「0 已学」的角色后绑定回 -1（ResetForClass 清空）",
                _ctx.Skill.GetButtonSkill(0) == -1 && _ctx.Skill.GetButtonSkill(1) == -1,
                $"左={_ctx.Skill.GetButtonSkill(0)} 右={_ctx.Skill.GetButtonSkill(1)}");
            Game.Event.Emit(Events.SkillSlotAssignRequest, 1);
            Check("无已学可施放技能时按 F1 ⇒ 不改绑定（保持 -1；槽位下限等非预期分支已留 Warn）",
                _ctx.Skill.GetButtonSkill(0) == -1 && _ctx.Skill.GetButtonSkill(1) == -1,
                $"左={_ctx.Skill.GetButtonSkill(0)} 右={_ctx.Skill.GetButtonSkill(1)}");
            Check("槽号越界（0 / 9）⇒ 不改绑定（SkillSlotIndex 返回 -1）",
                EmitSlotAndCheck(0) && EmitSlotAndCheck(9), "0/9 都被拒");
            Console.WriteLine();
        }

        private static bool EmitSlotAndCheck(int slot)
        {
            Game.Event.Emit(Events.SkillSlotAssignRequest, slot);
            return _ctx.Skill.GetButtonSkill(0) == -1 && _ctx.Skill.GetButtonSkill(1) == -1;
        }

        private static int CountLearned()
        {
            var n = 0;
            foreach (var def in _ctx.Skill.Available) if (_ctx.Skill.GetLevel(def.id) > 0) n++;
            return n;
        }

        private static string DescribeSkill(int id)
        {
            if (id < 0) return "普通攻击(-1)";
            foreach (var def in _ctx.Skill.Available) if (def.id == id) return $"{def.name}#{id}";
            return "#" + id;
        }

        private static void Step9_CastManaAndCooldown()
        {
            Section("9. TryCast：扣法力 + 进冷却（贴数字）");

            PrepareMap(AreaId.BloodMoor, 888001);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            var save = NewSave(PlayerClass.Sorceress, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);
            _player.SetLevel(99);
            _player.SetSkillPoints(10);

            var fb = FindSkill(PlayerClass.Sorceress, "火弹");
            Check("找到法师「火弹」", fb != null, fb != null ? $"{fb.name}#{fb.id} target={fb.target} 伤害 {fb.dmgMin}-{fb.dmgMax}" : "null");
            if (fb == null) return;

            _ctx.Skill.Learn(fb.id);
            Check("学会火弹（等级 1）", _ctx.Skill.GetLevel(fb.id) == 1, _ctx.Skill.GetLevel(fb.id).ToString());

            _player.SetMana(50);
            var grid = _player.Grid + new Vector2Int(4, 0);
            var cast = _ctx.Skill.TryCast(fb.id, grid);
            var cd = _ctx.Skill.GetCooldownRemain(fb.id);
            Console.WriteLine($"  TryCast(火弹) → {cast}；法力 50 → {_player.Mana}；冷却剩余 {cd:0.00}s；在飞投射物 {_skillImpl.ActiveProjectileCount} 个");
            Check("TryCast 成功", cast, cast.ToString());
            Check("扣了法力 5（skill_c.mana_cost=5，1 级无增量）", _player.Mana == 45, _player.Mana.ToString());
            Check("进入了冷却（>0）", cd > 0f, $"{cd:0.00}s");
            Check("火弹是投射物技能 ⇒ 真的生成了投射物", _skillImpl.ActiveProjectileCount == 1,
                _skillImpl.ActiveProjectileCount.ToString());

            var manaNow = _player.Mana;
            Check("冷却期内再施放 = false，且不再扣蓝", !_ctx.Skill.TryCast(fb.id, grid) && _player.Mana == manaNow,
                $"mana={_player.Mana}");

            _ctx.Skill.Tick(2f);
            Check("冷却走完后 GetCooldownRemain == 0", _ctx.Skill.GetCooldownRemain(fb.id) == 0f,
                _ctx.Skill.GetCooldownRemain(fb.id).ToString("0.00"));

            _player.SetMana(1);
            Check("法力不足时 = false 且法力不变", !_ctx.Skill.TryCast(fb.id, grid) && _player.Mana == 1, $"mana={_player.Mana}");

            // w7：扣蓝契约入口本体（`IPlayerModule.TrySpendMana`）—— 成功扣减 / 不足 / 非正数
            _player.SetMana(20);
            var spent = _player.TrySpendMana(5);
            Check("TrySpendMana(5) = true 且 -5（扣蓝入口生效）", spent && _player.Mana == 15,
                $"返回 {spent}，mana={_player.Mana}");
            Check("TrySpendMana(99) 不足 = false 且不扣", !_player.TrySpendMana(99) && _player.Mana == 15,
                $"mana={_player.Mana}");
            Check("TrySpendMana(0)/(-1) = false 且不扣（非正数不扣）",
                !_player.TrySpendMana(0) && !_player.TrySpendMana(-1) && _player.Mana == 15,
                $"mana={_player.Mana}");

            // 未学技能 / 被动的拒绝路径
            var notLearned = FirstLearnable(PlayerClass.Sorceress);
            if (notLearned != null && notLearned.id != fb.id)
            {
                Check("未学技能 TryCast = false（不扣蓝）", !_ctx.Skill.TryCast(notLearned.id, grid), "false");
            }
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 10. 投射物飞行 + 命中
        // ═════════════════════════════════════════════════════════════════════
        private static void Step10_ProjectileFlight()
        {
            Section("10. 投射物：火弹从 A 飞到 B 命中（轨迹采样 + 命中日志）");

            PrepareMap(AreaId.BloodMoor, 999001);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            // 挑一只"直线上没有别的怪挡路"的目标 ⇒ 才能断言"从 A 飞到 B 命中 B"
            var target = PickIsolatedTarget(out var playerGrid);
            if (target == null)
            {
                Check("投射物：有可打的目标（且弹道上没有别的怪）", false, "none");
                return;
            }

            _player.SetGrid(playerGrid, Iso.DirectionTo(playerGrid, target.Grid()));

            var save = NewSave(PlayerClass.Sorceress, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);
            _player.SetLevel(99);
            _player.SetSkillPoints(10);
            var fb = FindSkill(PlayerClass.Sorceress, "火弹");
            if (fb == null) { Check("投射物：找到火弹", false, "null"); return; }
            _ctx.Skill.Learn(fb.id);
            _player.SetMana(50);

            var hpBefore = target.hp;
            var casts = _ctx.Skill.TryCast(fb.id, target.Grid());
            Check("施放成功并生成投射物", casts && _skillImpl.Projectiles.Count == 1,
                $"{casts} / {_skillImpl.Projectiles.Count}");

            var proj = _skillImpl.Projectiles.Count > 0 ? _skillImpl.Projectiles[0] : null;
            if (proj == null) return;

            Console.WriteLine($"  起点 {proj.pos}（连续格）→ 方向 {proj.dir}，速度 {proj.speed:0.00} 格/s，" +
                              $"射程 {proj.rangeLeft:0.00} 格，命中半径 {proj.hitRadius:0.00} 格");
            Console.WriteLine($"  目标 m#{target.id} {target.name} 在格 ({target.gridX},{target.gridY})，距离 " +
                              $"{Vector2.Distance(new Vector2(_player.Grid.x + 0.5f, _player.Grid.y + 0.5f), new Vector2(target.gridX + 0.5f, target.gridY + 0.5f)):0.00} 格");

            var steps = 0;
            while (proj.alive && !proj.hitSomething && steps < 400)
            {
                _ctx.Skill.Tick(Dt);
                steps++;
            }

            Console.WriteLine($"  飞行 {steps} 步（{steps * Dt:0.00}s）：{proj.TrailText(10)}");
            Console.WriteLine($"  命中结果：hitSomething={proj.hitSomething} hitMonsterId={proj.hitMonsterId} " +
                              $"飞行 {proj.traveled:0.00} 格；目标血量 {hpBefore} → {target.hp}");

            Check("投射物命中了目标（hitMonsterId 与瞄准的目标一致）", proj.hitMonsterId == target.id,
                $"hit={proj.hitMonsterId} target={target.id}");
            Check("命中后目标掉血", target.hp < hpBefore, $"{hpBefore} → {target.hp}");
            Check("命中后投射物被回收（不在飞列表里）", _skillImpl.ActiveProjectileCount == 0,
                _skillImpl.ActiveProjectileCount.ToString());
            Check("日志里有投射物的发出与命中记录",
                _log.Has("[Skill] 投射物") && _log.Has("命中 m#" + target.id), "见上方日志");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 15. 审计 B 红行 R2/R3：投射物 × 地形碰撞 / 投射物 × 桥栏杆排序
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        ///   R3 = `Projectile.Step` / `TickProjectiles` 全无地形判定 ⇒ 隔墙/隔水/隔树射杀；
        ///   R2 = 投射物表现用**裸实体档**（`ProjectileView.cs:50,77`）⇒ 桥面射出的投射物被
        ///        正南一格桥栏杆盖住（"桥下走"同族，上一轮只修了实体一条路径）。
        /// 判**过程**不判结果：逐类裁决表 + 真实地图上的 P→W→T 三连格 + 一帧跨 3 格 + deck 逐格不等式。
        /// </summary>
        private static void Step15_ProjectileTerrainAndDeckSort()
        {
            Section("15. ★ 审计 B R2/R3：投射物撞地形消散（不穿墙）+ 投射物排序走 deck 口径");

            // ── 15.1 逐类裁决表（口径唯一出处 = `SkillModule.BlocksProjectile`）───────
            // 期望值**独立写**（不是把 `IsWalkable` 抄一遍）：新增 TileKind 却忘了裁决 ⇒ 当场变红
            var expect = new Dictionary<TileKind, bool>
            {
                { TileKind.Void,      true  },   // 图外 / 未生成
                { TileKind.Grass,     false },   // 地面层
                { TileKind.Dirt,      false },   // 地面层（桥面 = Dirt + deck 登记）
                { TileKind.Road,      false },   // 地面层
                { TileKind.Rock,      true  },   // 石头矮墙 / 桥栏杆 / 水 / 崖壁 / 碎石（占整格；R12：水=Rock）
                { TileKind.Tree,      true  },   // 树干
                { TileKind.Fence,     true  },   // 栅栏（本项目占满整格，不是"半格矮物"）
                { TileKind.Wall,      true  },   // 帐篷 / 摊位 / 货车
                { TileKind.CaveFloor, false },   // 洞穴地面
                { TileKind.CaveWall,  true  },   // 洞穴岩壁
                { TileKind.Exit,      false },   // 出入口（可走）
                { TileKind.TownFloor, false },   // 城镇地面
                //   水 = 占满整格 + 不可走；项目对 TileKind 只有一个"可走性"轴（没有"仅挡行走不挡弹道"
                //   的数据位）；拆值前水就是 Rock ⇒ 判"挡"= 零行为回归。
                //   仍待参考物：原版 ds1 的 BlockWalk / BlockMissile 是两个位；若水只置 BlockWalk，
                //   投射物应飞过水面 ⇒ 那时改 SkillModule 一行 + 本表一行。
                { TileKind.Water,     true  },   // 水（本片裁决：挡；待参考物复核）
            };
            var wrong = 0;
            var uncovered = new List<string>();
            foreach (TileKind k in Enum.GetValues(typeof(TileKind)))
            {
                if (!expect.ContainsKey(k)) { uncovered.Add(k.ToString()); continue; }
                if (SkillModule.BlocksProjectile(k) != expect[k]) wrong++;
            }
            Console.WriteLine("  [逐类裁决] " + string.Join(" / ",
                new List<string>(new[] { "Void", "Grass", "Dirt", "Road", "Rock", "Tree", "Fence", "Wall",
                    "CaveFloor", "CaveWall", "Exit", "TownFloor", "Water" })));
            Check("每个 TileKind 都有明确裁决（新增地形必须回来登记本表）",
                uncovered.Count == 0, uncovered.Count == 0 ? $"共 {expect.Count} 类" : "未登记: " + FmtStr(uncovered));
            Check("逐类裁决与期望一致（⛔ 不是一刀切地照抄可走性）", wrong == 0, $"不一致 {wrong} 类");

            // 与「可走性」的关系（语义不同、当前同集）：作为"新地形漏裁决"的机械闸门
            var walkMismatch = 0;
            foreach (TileKind k in Enum.GetValues(typeof(TileKind)))
                if (SkillModule.BlocksProjectile(k) != TileKindInfo.IsBlocking(k)) walkMismatch++;
            Check("裁决表与 TileKindInfo.IsBlocking 逐类同集（新地形漏裁决会被抓住）",
                walkMismatch == 0, $"不一致 {walkMismatch} 类");

            // ── 15.2 隔墙不命中（先跑"无墙对照"，防"把投射物全废了"）───────────────
            PrepareMap(AreaId.BloodMoor, 1502001);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            MonsterState victim = null;
            foreach (var m in _ctx.Monster.All)
            {
                if (m != null && m.alive) { victim = m; break; }
            }
            Check("有可用于测试的活怪", victim != null, victim != null ? $"m#{victim.id} {victim.name}" : "none");
            if (victim == null) return;

            var save = NewSave(PlayerClass.Sorceress, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);
            _player.SetLevel(99);
            _player.SetSkillPoints(10);
            _player.SetMana(50);
            var fb = FindSkill(PlayerClass.Sorceress, "火弹");
            if (fb == null) { Check("找到法师「火弹」", false, "null"); return; }
            _ctx.Skill.Learn(fb.id);

            // 对照：弹道全程可穿 + 没有别的怪挡路 ⇒ **必须命中并掉血**
            // 构造 = 找一条 **5 格连续可走**的走廊（起点可走 ⇒ 全程可穿由构造保证，不靠运气）
            if (FindClearCorridor(victim, out var ctrlPlayer, out var ctrlTarget))
            {
                victim.gridX = ctrlTarget.x;
                victim.gridY = ctrlTarget.y;
                _player.SetGrid(ctrlPlayer, Iso.DirectionTo(ctrlPlayer, ctrlTarget));
                var hp0 = victim.hp;
                var cast0 = _ctx.Skill.TryCast(fb.id, ctrlTarget);
                var proj0 = _skillImpl.Projectiles.Count > 0 ? _skillImpl.Projectiles[0] : null;
                var n0 = 0;
                while (proj0 != null && proj0.alive && !proj0.hitSomething && !proj0.hitTerrain && n0 < 400)
                {
                    _ctx.Skill.Tick(Dt); n0++;
                }
                Console.WriteLine($"  [对照·无墙] {n0} 步：cast={cast0} hitMonsterId=" +
                                  $"{(proj0 != null ? proj0.hitMonsterId.ToString() : "null")} hitTerrain=" +
                                  $"{(proj0 != null ? proj0.hitTerrain.ToString() : "null")}；怪血 {hp0} → {victim.hp}");
                Check("对照：无墙时命中该怪（投射物没被这次改动废掉）",
                    proj0 != null && proj0.hitMonsterId == victim.id,
                    proj0 != null ? proj0.hitMonsterId.ToString() : "null");
                Check("对照：目标真的掉血", victim.hp < hp0, $"{hp0} → {victim.hp}");
                Check("对照：没有误判成地形命中", proj0 != null && !proj0.hitTerrain,
                    proj0 != null ? proj0.hitTerrain.ToString() : "null");
            }
            else
            {
                Check("对照：地图上找到弹道全程可穿的一对格", false, "none");
            }

            // ── 15.2b 隔墙：P(可走) → W(阻挡) → T(可走) ─────────────────────────
            // 对照组那一发可能把目标打死了 ⇒ 重新挑一只活怪（否则下面的"没掉血"是 0→0 的空断言）
            if (!victim.alive)
            {
                foreach (var m in _ctx.Monster.All)
                {
                    if (m != null && m.alive && m.hp > 0) { victim = m; break; }
                }
            }
            Check("隔墙用例的目标是**存活且血量 > 0** 的怪（断言不是 0→0 空跑）",
                victim.alive && victim.hp > 0, $"m#{victim.id} alive={victim.alive} hp={victim.hp}");

            if (!FindWallAlley(out var pGrid, out var wGrid, out var tGrid))
            {
                Check("地图上找到 P(可走)→W(阻挡)→T(可走) 三连格", false, "none");
                return;
            }
            Console.WriteLine($"  [隔墙用例] P={pGrid} → W={wGrid}({_ctx.Map.TileAt(wGrid)}) → T={tGrid}");

            victim.gridX = tGrid.x; victim.gridY = tGrid.y;          // 把怪挪到墙后
            _player.SetGrid(pGrid, Iso.DirectionTo(pGrid, tGrid));
            _ctx.Skill.Tick(1f);                                     // 清掉上一发的冷却
            _player.SetMana(50);

            var hpBefore = victim.hp;
            var cast = _ctx.Skill.TryCast(fb.id, tGrid);
            var proj = _skillImpl.Projectiles.Count > 0 ? _skillImpl.Projectiles[0] : null;
            Check("隔墙用例：施放成功并生成投射物", cast && proj != null,
                $"{cast} / {_skillImpl.Projectiles.Count}");
            if (proj == null) return;

            var steps = 0;
            while (proj.alive && !proj.hitSomething && !proj.hitTerrain && steps < 400)
            {
                _ctx.Skill.Tick(Dt); steps++;
            }
            Console.WriteLine($"  [隔墙] {steps} 步：hitTerrain={proj.hitTerrain} terrainCell={proj.terrainCell}" +
                              $"({proj.terrainKind}) hitMonsterId={proj.hitMonsterId}；飞了 {proj.traveled:0.00} 格；" +
                              $"怪血 {hpBefore} → {victim.hp}");
            Check("隔墙：怪物**没有掉血**", victim.hp == hpBefore, $"{hpBefore} → {victim.hp}");
            Check("隔墙：投射物在**墙格**命中地形消散", proj.hitTerrain && proj.terrainCell == wGrid,
                $"hitTerrain={proj.hitTerrain} cell={proj.terrainCell} 期望 {wGrid}");
            Check("隔墙：没有命中任何怪", proj.hitMonsterId == -1, proj.hitMonsterId.ToString());
            Check("隔墙：日志里有『命中地形消散』记录", _log.Has("命中地形消散"), "见上方日志");

            // ── 15.3 高速投射物：一帧跨 3 格也不穿墙 ─────────────────────────────
            victim.gridX = tGrid.x; victim.gridY = tGrid.y;
            _player.SetGrid(pGrid, Iso.DirectionTo(pGrid, tGrid));
            _ctx.Skill.Tick(1f);
            _player.SetMana(50);

            var hp3 = victim.hp;
            var cast3 = _ctx.Skill.TryCast(fb.id, tGrid);
            var proj3 = _skillImpl.Projectiles.Count > 0 ? _skillImpl.Projectiles[0] : null;
            Check("高速用例：施放成功并生成投射物", cast3 && proj3 != null,
                $"{cast3} / {_skillImpl.Projectiles.Count}");
            if (proj3 != null)
            {
                var dtOne = 3f / (proj3.speed > 0.01f ? proj3.speed : 1f);
                _ctx.Skill.Tick(dtOne);                              // **一次** Tick = 一帧走 3 格
                Console.WriteLine($"  [高速] 一帧 dt={dtOne:0.000}s ⇒ 位移 {proj3.speed * dtOne:0.00} 格（≥3）；" +
                                  $"hitTerrain={proj3.hitTerrain} terrainCell={proj3.terrainCell} " +
                                  $"hitMonsterId={proj3.hitMonsterId}；怪血 {hp3} → {victim.hp}");
                Check("高速：一帧位移 ≥ 3 格（用例确实跨多格）", proj3.speed * dtOne >= 3f,
                    $"{proj3.speed * dtOne:0.00} 格");
                Check("高速：仍在墙格消散（没穿墙）", proj3.hitTerrain && proj3.terrainCell == wGrid,
                    $"hitTerrain={proj3.hitTerrain} cell={proj3.terrainCell}");
                Check("高速：墙后的怪没有掉血", victim.hp == hp3, $"{hp3} → {victim.hp}");
            }

            // ── 15.4 【R2】桥面投射物排序 = 实体同一口径（ViewModule.EntitySortOrder）──
            PrepareMap(AreaId.Town, 0);                              // 城镇 = 固定布局（含 deck 桥面）
            var deck = new List<Vector2Int>();
            for (var y = 0; y < _ctx.Map.Height; y++)
            {
                for (var x = 0; x < _ctx.Map.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (_ctx.Map.IsDeckGrid(g)) deck.Add(g);
                }
            }
            Check("城镇存在 deck（桥面）格 ⇒ 下面逐格断言不是空跑", deck.Count > 0, $"{deck.Count} 格");

            var okLow = 0;
            var okHigh = 0;
            var plainCovered = 0;
            foreach (var g in deck)
            {
                var order = ViewModule.EntitySortOrder(g);            // ← 投射物现在调的就是这个
                if (order > Iso.SortOrder(new Vector2Int(g.x, g.y + 1), GameConst.LayerOffsetObject)) okLow++;
                if (order < Iso.SortOrder(new Vector2Int(g.x, g.y + 2), GameConst.LayerOffsetObject)) okHigh++;
                if (Iso.SortOrder(g, GameConst.LayerOffsetEntity) <=
                    Iso.SortOrder(new Vector2Int(g.x, g.y + 1), GameConst.LayerOffsetObject)) plainCovered++;
            }
            var sample = new Vector2Int(46, 25);
            Console.WriteLine($"  deck 格 = {deck.Count}；例（格(46,25)）：投射物路径排序 = " +
                              $"{ViewModule.EntitySortOrder(sample)}（普通实体档 = " +
                              $"{Iso.SortOrder(sample, GameConst.LayerOffsetEntity)}，正南一格物件层 = " +
                              $"{Iso.SortOrder(new Vector2Int(sample.x, sample.y + 1), GameConst.LayerOffsetObject)}，" +
                              $"正南两格物件层 = {Iso.SortOrder(new Vector2Int(sample.x, sample.y + 2), GameConst.LayerOffsetObject)}）");
            Check("【R2】投射物排序逐格 > 正南一格物件层（桥上射出的投射物不被栏杆盖住）",
                okLow == deck.Count, $"{okLow}/{deck.Count}");
            Check("【R2】投射物排序逐格 < 正南两格物件层（不越档）", okHigh == deck.Count, $"{okHigh}/{deck.Count}");
            Check("【R2】反证根因：普通实体档确实逐格被正南栏杆盖住（= 改前的值）",
                plainCovered == deck.Count, $"{plainCovered}/{deck.Count}");
            Console.WriteLine();
        }

        /// <summary>找 P(可走) → W(阻挡) → T(可走) 的三连格（东向优先；不写死坐标，从当前地图搜）。</summary>
        private static bool FindWallAlley(out Vector2Int p, out Vector2Int w, out Vector2Int t)
        {
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) };
            for (var y = 1; y < _ctx.Map.Height - 1; y++)
            {
                for (var x = 1; x < _ctx.Map.Width - 1; x++)
                {
                    foreach (var d in dirs)
                    {
                        var a = new Vector2Int(x, y);
                        var b = new Vector2Int(x + d.x, y + d.y);
                        var c = new Vector2Int(x + 2 * d.x, y + 2 * d.y);
                        if (!_ctx.Map.Walkable(a)) continue;
                        if (_ctx.Map.Walkable(b)) continue;          // 中格必须阻挡
                        if (!_ctx.Map.Walkable(c)) continue;
                        p = a; w = b; t = c;
                        return true;
                    }
                }
            }
            p = w = t = _ctx.Map.SpawnPoint;
            return false;
        }

        /// <summary>
        /// 找一条**5 格连续可走**的水平走廊（用作"无墙对照"）：玩家格 = 起点、怪格 = 终点。
        /// <para>为什么这样构造：起点可走 ⇒ 弹道全程可穿是**构造保证**的（不靠环上碰运气，
        /// 实测按环找在尖刺鼠附近会一个都找不到 ⇒ 断言恒红）。另需弹道附近没有别的活怪。</para>
        /// </summary>
        private static bool FindClearCorridor(MonsterState target, out Vector2Int playerGrid,
            out Vector2Int targetGrid)
        {
            for (var y = 0; y < _ctx.Map.Height; y++)
            {
                var run = 0;
                for (var x = 0; x < _ctx.Map.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    run = _ctx.Map.Walkable(g) ? run + 1 : 0;
                    if (run < 5) continue;

                    var start = new Vector2Int(x - 4, y);
                    if (!TerrainClear(start, g)) continue;
                    if (!MonsterLineClear(start, g, target.id)) continue;
                    playerGrid = start;
                    targetGrid = g;
                    return true;
                }
            }
            playerGrid = targetGrid = _ctx.Map.SpawnPoint;
            return false;
        }

        /// <summary>该格到某点的直线上有没有别的活怪（阈值 1.6 格 = 弹道命中半径量级）。</summary>
        private static bool MonsterLineClear(Vector2Int from, Vector2Int toGrid, int excludeMonsterId)
        {
            var a = new Vector2(from.x + 0.5f, from.y + 0.5f);
            var b = new Vector2(toGrid.x + 0.5f, toGrid.y + 0.5f);
            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive || m.id == excludeMonsterId) continue;
                var p = new Vector2(m.gridX + 0.5f, m.gridY + 0.5f);
                if (PointSegmentDistance(p, a, b) <= 1.6f) return false;
            }
            return true;
        }

        /// <summary>
        /// 该段直线是否**全程可被投射物穿过**（判据口径 = `SkillModule.PassableForProjectile`，
        /// 与运行时同一份实现；宿主里不另写一份地形判定）。
        /// </summary>
        private static bool TerrainClear(Vector2Int from, Vector2Int to)
        {
            var a = new Vector2(from.x + 0.5f, from.y + 0.5f);
            var b = new Vector2(to.x + 0.5f, to.y + 0.5f);
            var d = b - a;
            var dist = d.magnitude;
            if (dist <= 0f) return true;

            var steps = Mathf.CeilToInt(dist / 0.25f);
            var last = new Vector2Int(int.MinValue, int.MinValue);
            for (var i = 0; i <= steps; i++)
            {
                var pt = a + d * ((float)i / steps);
                var g = new Vector2Int(Mathf.FloorToInt(pt.x), Mathf.FloorToInt(pt.y));
                if (g == last) continue;
                last = g;
                if (!SkillModule.PassableForProjectile(_ctx.Map.TileAt(g))) return false;
            }
            return true;
        }

        private static string FmtStr(List<string> items)
        {
            return items.Count == 0 ? "-" : string.Join(",", items);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 11. 逐帧动画器（本项目新增）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step11_SpriteAnimator()
        {
            Section("11. 逐帧 Sprite 动画器（**本项目新增**，引擎 Game.Anim 是 Animator 驱动不覆盖逐帧切图）");

            var keys = new[] { "walk_s_0", "walk_s_1", "walk_s_2", "walk_s_3" };
            var anim = new SpriteAnimator();
            anim.Play(ViewAnim.Walk, keys, 8f, true);

            Check("初始帧 = 0，键名正确", anim.FrameIndex == 0 && anim.CurrentKey == "walk_s_0",
                $"{anim.FrameIndex}/{anim.CurrentKey}");

            // 8fps、帧间隔 0.125s
            anim.Tick(0.125f);
            var f1 = anim.FrameIndex;
            anim.Tick(0.125f);
            var f2 = anim.FrameIndex;
            anim.Tick(0.125f);
            var f3 = anim.FrameIndex;
            anim.Tick(0.125f);
            var f4 = anim.FrameIndex;
            Console.WriteLine($"  8fps 循环：0.125s 一步 ⇒ 帧序 0 → {f1} → {f2} → {f3} → {f4}（应回到 0）");
            Check("按帧率推进并且循环回第 0 帧", f1 == 1 && f2 == 2 && f3 == 3 && f4 == 0, $"{f1}/{f2}/{f3}/{f4}");

            // 同一动作重复 Play 不应重置进度
            anim.Play(ViewAnim.Walk, keys, 8f, true);
            Check("同一动作重复 Play 不会把进度打回第 0 帧（逐帧动画最典型缺陷）", anim.FrameIndex == f4,
                anim.FrameIndex.ToString());

            // 非循环：停在最后一帧
            var deathKeys = new[] { "death_s_0", "death_s_1", "death_s_2" };
            anim.Play(ViewAnim.Death, deathKeys, 8f, false);
            for (var i = 0; i < 10; i++) anim.Tick(0.125f);
            Console.WriteLine($"  非循环死亡动画：10 步后 frame={anim.FrameIndex} finished={anim.Finished}（应停在末帧）");
            Check("非循环动画停在最后一帧且 Finished=true",
                anim.FrameIndex == 2 && anim.Finished, $"{anim.FrameIndex}/{anim.Finished}");

            // 换方向 = 换整套帧键
            //
            //   本轮接上原版 `.dcc` 后帧数是**逐单位真实值**（`SpriteFrameCounts.cs` 生成物）：
            //   亚马逊 walk=8（正好也是 8）、堕落者 attack=10（**不是** 8）。
            //   ⇒ 断言改为"对着真实帧数表断言"，这样它验的是**帧键与帧数表一致**（真正的契约），
            //     而不是一个会随素材变化而失效的魔数。
            var ne = SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.NE);
            var amazonWalk = SpriteFrameCounts.Of("amazon", ViewAnim.Walk);
            Check("换方向会换帧键（8 方向）",
                ne.Length == amazonWalk && ne[0] == "D2/Chars/amazon/walk_ne_0", ne[0]);
            Console.WriteLine($"  亚马逊 walk 真实帧数（原版 .cof）= {amazonWalk}，帧键数 = {ne.Length}");

            var monsterKeys = SpriteFrames.Keys("fa", ViewAnim.Attack, Dir8.S);
            var fallenAttack = SpriteFrameCounts.Of("fa", ViewAnim.Attack);
            Check("怪物帧键走 D2/Monsters/{sprite}/（sprite 来自 monster_c.sprite）",
                monsterKeys.Length == fallenAttack && monsterKeys[3] == "D2/Monsters/fa/attack_s_3",
                monsterKeys[3]);
            Console.WriteLine($"  堕落者 attack 真实帧数（原版 .cof）= {fallenAttack}，帧键数 = {monsterKeys.Length}");

            // 缺动作的回退（NPC 只有 NU/WL；多数怪物没有 SC）——回退后**仍然指到存在的帧键**
            var npcCast = SpriteFrames.Keys(SpriteFrames.NpcSpriteCode(Diablo2.Def.NpcId.Akara),
                ViewAnim.Cast, Dir8.S);
            Check("NPC 缺 cast 时回退到原版 idle（帧键前缀变成 idle，不指空图）",
                npcCast.Length > 0 && npcCast[0].StartsWith("D2/Monsters/ps/idle_s_"), npcCast[0]);
            Console.WriteLine($"  阿卡拉（原版代码 ps）cast 回退后首帧键 = {npcCast[0]}");
            Console.WriteLine($"  帧数表兜底值（亚马逊真实值）：idle={SpriteFrames.FrameCounts[0]} walk={SpriteFrames.FrameCounts[1]} " +
                              $"attack={SpriteFrames.FrameCounts[2]} cast={SpriteFrames.FrameCounts[3]} hit={SpriteFrames.FrameCounts[4]} death={SpriteFrames.FrameCounts[5]}");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 实体根随 Stage 场景卸载（`_root` 的 Unity 引用判为 null）之后，`AppContext.Tick` 仍每帧调
        /// <para>离线进程建不了 `GameObject`（`new GameObject()` 会抛异常），而 Unity 里「已销毁对象」
        /// 的 `== null` 恰好就是 true ⇒ 这里直接把 `Root` 置 null 来构造**同一条代码路径**。</para>
        /// </summary>
        private static void Step14_ViewStaleRefSafety()
        {
            Section("14. 视图空引用防护（agent-16）：实体根随场景卸载后 Tick 不抛异常 + 失效只报一次");

            var m = new ViewModule();

            // 3 条「已失效」的视图记录（玩家 / 怪物 / 地面物品），父根 `_root` 也已被销毁
            var playerStale = new EntityView { EntityId = GameConst.PlayerEntityId, IsPlayer = true, Root = null, Renderer = null };
            var monsterStale = new EntityView { EntityId = 9001, Root = null, Renderer = null };
            var itemStale = new EntityView { EntityId = 7001, IsGroundItem = true, Root = null, Renderer = null };

            SetPriv(m, "_player", playerStale);
            var entities = (Dictionary<int, EntityView>)GetPriv(m, "_entities");
            entities[GameConst.PlayerEntityId] = playerStale;
            entities[9001] = monsterStale;
            var items = (Dictionary<int, EntityView>)GetPriv(m, "_groundItems");
            items[7001] = itemStale;

            Check("构造完成：`_root == null`（= 场景已卸载）但仍持有 2 个实体视图 + 1 个地面物品视图",
                GetPriv(m, "_root") == null && entities.Count == 2 && items.Count == 1,
                $"root={(GetPriv(m, "_root") == null ? "null" : "有")} entities={entities.Count} items={items.Count}");

            // `GetView` 不许把「假 null」交出去（HoverPicker 会去读 `.transform`）
            var viewThrew = false;
            GameObject got = null;
            try { got = m.GetView(9001); }
            catch (Exception e) { viewThrew = true; Console.WriteLine($"  ❌ GetView 抛异常：{e.GetType().Name}: {e.Message}"); }
            Check("GetView(失效实体) 返回真正的 null 且不抛异常（不许把碰不得的引用交出去）",
                !viewThrew && got == null, viewThrew ? "见上方异常" : "null");

            var threw = false;
            try
            {
                for (var i = 0; i < 5; i++) m.Tick(Dt);      // 模拟「每帧都被 AppContext.Tick 调到」
            }
            catch (Exception e)
            {
                threw = true;
                Console.WriteLine($"  ❌ Tick 抛异常：{e.GetType().Name}: {e.Message}");
            }

            Check("实体根失效后**连续 5 帧** Tick 不抛异常（原缺陷 = 每帧 MissingReferenceException）",
                !threw, threw ? "见上方异常" : "5 帧无异常");
            Check("失效在 Tick 的**总闸门**里被处理：引用已全部丢弃",
                GetPriv(m, "_player") == null && entities.Count == 0 && items.Count == 0,
                $"player={(GetPriv(m, "_player") == null ? "null" : "还在")} entities={entities.Count} items={items.Count}");

            const string StaleNeedle = "[View] 实体根节点已随场景卸载";
            var staleLines = _log.Count(StaleNeedle);
            Check("留下了**可检索**日志（tag=View + 「已随场景卸载」，可与「正常卸载」区分）", staleLines >= 1,
                staleLines >= 1 ? "见上方 [WARN ] [View] 行" : "一条都没有（非预期分支不许静默）");
            Check("★ 只报一次：连跑 5 帧只产生 1 条该日志（不许每帧刷屏）", staleLines == 1, $"实际 {staleLines} 条");
            var staleIdx = _log.IndexOf(StaleNeedle);
            Console.WriteLine("  引用失效日志样例行：" + (staleIdx >= 0 ? _log.Lines[staleIdx] : "(无)"));

            // `Clear()` 幂等：已经干净时静默返回（Flow 的 ResetModules 与 StageLeft 会各调一次）
            var clearsBefore = _log.Count("Clear：清场完成");
            var clearThrew = false;
            try { m.Clear(); m.Clear(); }
            catch (Exception e) { clearThrew = true; Console.WriteLine($"  ❌ Clear 抛异常：{e.GetType().Name}: {e.Message}"); }
            Check("`Clear()` 幂等（连调两次）且已干净时静默（不重复打「清场完成」）",
                !clearThrew && _log.Count("Clear：清场完成") == clearsBefore,
                $"新增 {_log.Count("Clear：清场完成") - clearsBefore} 条");

            // 离场兜底：本模块自己监听 `StageLeft`（幂等；留一条复位日志）
            var leftThrew = false;
            try { Game.Event.Emit(Events.StageLeft); }
            catch (Exception e) { leftThrew = true; Console.WriteLine($"  ❌ Emit(StageLeft) 抛异常：{e.GetType().Name}: {e.Message}"); }
            Check("本模块监听 StageLeft 后 Emit 不抛异常（离场兜底置 null 就位）", !leftThrew,
                leftThrew ? "见上方异常" : "无异常");
            Console.WriteLine();
        }

        /// <summary>读私有字段（自检用；正常业务不许这么干）。</summary>
        private static object GetPriv(object o, string field)
        {
            var f = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(o.GetType().FullName, field);
            return f.GetValue(o);
        }

        /// <summary>写私有字段（自检用）。</summary>
        private static void SetPriv(object o, string field, object value)
        {
            var f = o.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f == null) throw new MissingFieldException(o.GetType().FullName, field);
            f.SetValue(o, value);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 12. 未接线时的降级（AppContext 字段为 null）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step12_UnwiredDegradation()
        {
            Section("12. 模块未接入时的降级（null 容忍 + 明确日志，不许崩）");

            var map = _ctx.Map;
            var monster = _ctx.Monster;
            var item = _ctx.Item;
            var view = _ctx.View;
            var audio = _ctx.Audio;

            _ctx.Map = null;
            _ctx.Monster = null;
            _ctx.Item = null;
            _ctx.View = null;
            _ctx.Audio = null;

            var threw = false;
            try
            {
                _ctx.Monster?.Tick(Dt);
                _ctx.Combat.RequestAttack(1000);
                _ctx.Combat.RequestMonsterAttack(1000);
                _ctx.Combat.RevivePlayer();
                _ctx.Skill.TryCast(31, Vector2Int.zero);
            }
            catch (Exception e)
            {
                threw = true;
                Console.WriteLine($"  ❌ 抛异常：{e.GetType().Name}: {e.Message}");
            }

            Check("门面全为 null 时调用不抛异常", !threw, threw ? "见上方异常" : "无异常");
            Check("留下了可定位的降级日志（Monster/View/Item 未接入）",
                _log.Has("未接入") || _log.Has("未就绪"), "见上方日志");

            _ctx.Map = map;
            _ctx.Monster = monster;
            _ctx.Item = item;
            _ctx.View = view;
            _ctx.Audio = audio;
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 13. AutoWire 契约（真实游戏里 4 个模块**就是**这么被装上的）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step13_AutoWireContract()
        {
            Section("13. AutoWire 契约：`internal sealed class XxxModule : IXxxModule` + 无参构造（真实装配路径）");

            // 新的 AppContext（会 Warn「重复创建」——正常）；AutoWire 用反射按接口找实现并实例化
            var ctx2 = AppContext.Create();
            ctx2.AutoWire();

            Check("IMapModule   ← 找得到实现（MapModule）", ctx2.Map != null, Name(ctx2.Map));
            Check("ICombatModule ← 找得到实现（CombatModule）", ctx2.Combat != null, Name(ctx2.Combat));
            Check("IMonsterModule ← 找得到实现（MonsterModule）", ctx2.Monster != null, Name(ctx2.Monster));
            Check("ISkillModule ← 找得到实现（SkillModule）", ctx2.Skill != null, Name(ctx2.Skill));
            Check("IViewModule  ← 找得到实现（ViewModule）", ctx2.View != null, Name(ctx2.View));
            // 注意：AutoWire 是在**本宿主程序集**里找实现 ⇒ 它会把本文件的替身
            // （RecordingItem / FakePlayer / RecordingAudio）也装配进去 —— 这恰好证明
            // "按接口找唯一实现 + 反射实例化" 这条机制是通的。真实游戏里这三个位置分别是
            Check("按接口找实现：宿主里的 Item/Player/Audio 替身也被 AutoWire 装了（机制自证）",
                ctx2.Item != null && ctx2.Player != null && ctx2.Audio != null,
                $"Item={Name(ctx2.Item)} Player={Name(ctx2.Player)} Audio={Name(ctx2.Audio)}");
            Check("本程序集里**没有**实现的接口保持 null 且有 Warn（降级不崩）",
                ctx2.Quest == null && ctx2.Npc == null && ctx2.Camera == null && ctx2.Save == null,
                ctx2.Describe());
            Check("IAppFlow 刻意不在 AutoWire 里（契约要求，由 Bootstrap 显式 new）", ctx2.Flow == null,
                ctx2.Flow == null ? "null（正确）" : "被装配了（**违反契约**）");
            Console.WriteLine("  装配摘要：" + ctx2.Describe());
            Console.WriteLine();
        }

        private static string Name(object o)
        {
            return o == null ? "null" : o.GetType().FullName;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        private static void PrepareMap(AreaId area, int seed)
        {
            _ctx.Map.Generate(area, seed);
            _monsterAttackEvents = 0;
            _playerDamagedEvents = 0;
            _monsterKilledEvents = 0;
            _player.SetMana(50);
            _player.SetLife(999);         // 血量拉满，避免 AI 测试中途被打死导致脱战
            _player.SetGrid(_ctx.Map.SpawnPoint);
        }

        /// <summary>
        /// 「等一个行为事件」的通用**窗口算式**：`所需秒数` + 事件余量（= 2 × `MonsterTuning.AttackIntervalSeconds`
        /// = 2.20s，即至少给 2 次出手机会）。
        /// <para>★ 片 melee-ai-why：本宿主原有多处"固定 N 帧"窗口是**拍的**（无算式出处），
        /// 其中 `AiMelee` 的 `6f` 用了**足以证伪**的短窗口（慢怪 17s 才到出手距离 ⇒ 判据必然量不到出手）。
        /// 统一改成"所需秒数（各调用点标明出处）+ 余量"，判据本身不动、阈值不放宽。</para>
        /// </summary>
        private static float EventWindowSeconds(float requiredSeconds)
            => requiredSeconds + MonsterTuning.AttackIntervalSeconds * 2f;

        private static void TickSim(float seconds)
        {
            var steps = (int)(seconds / Dt);
            for (var i = 0; i < steps; i++)
            {
                _ctx.Monster.Tick(Dt);
                _ctx.Combat.Tick(Dt);
                _ctx.Skill.Tick(Dt);
            }
        }

        private static void OnDamageDealt(DamageArgs a)
        {
            if (a == null) return;
            if (a.attackerId != GameConst.PlayerEntityId) _monsterAttackEvents++;
            Trace.Add($"event.DamageDealt:from={a.attackerId},to={a.targetId},amount={a.amount},hit={a.hit}");
        }

        private static MonsterState PickMonster(Func<MonsterState, bool> pred)
        {
            foreach (var m in _ctx.Monster.All)
            {
                if (m != null && pred(m)) return m;
            }
            return null;
        }

        /// <summary>
        /// 按**复活判定用的距离口径**取最近的活怪 —— 与 `MonsterModule.FindRevivableCorpse` 同一把尺子。
        /// 那是错的（生产比的是 `MonsterRuntime.Pos`，单位 = **格**；见 `AiShamanRevives` 的出处说明）。
        /// </summary>
        private static MonsterState NearestMonsterByReviveMetric(MonsterState from, float maxDistance, int exceptId)
        {
            MonsterState best = null;
            var bestD = maxDistance;
            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive || m.id == exceptId) continue;
                var d = ReviveScanDistance(from, m);
                if (d > bestD) continue;
                bestD = d;
                best = m;
            }
            return best;
        }

        /// <summary>
        /// **复活判定的唯一距离口径** = `MonsterModule.FindRevivableCorpse` 里那句
        /// `Vector2.Distance(shaman.Pos, corpse.Pos)`（`MonsterRuntime.Pos` = 连续格坐标，格中心制）
        /// ⇒ 等价于两格中心的**格欧氏距离**（单位 = 格，与 `MonsterTuning.ShamanReviveRange` 同量纲）。
        /// 不要用 `WorldDistance`（`Iso.GridToWorld` 的等距世界单位）或 `Iso.GridDistance`（Chebyshev）替代。
        /// </summary>
        private static float ReviveScanDistance(MonsterState a, MonsterState b)
            => Iso.GridDistanceEuclidean(a.Grid(), b.Grid());

        /// <summary>
        /// 两怪之间的**等距世界单位**距离（`MonsterState` 只有格坐标 ⇒ 用格中心 `Iso.GridToWorld` 换算）。
        /// 这**不是**复活判定的口径（易与格欧氏混淆：同一对实测 6.32 世界 vs 8.60 格），
        /// 仅用于人读参考 / 与相机可见范围打交道的地方。
        /// </summary>
        private static float WorldDistance(MonsterState a, MonsterState b)
            => Vector2.Distance(Iso.GridToWorld(a.Grid()), Iso.GridToWorld(b.Grid()));

        private static MonsterState NearestMonsterTo(Vector2Int grid, int maxDistance, int exceptId)
        {
            MonsterState best = null;
            var bestD = maxDistance + 1;
            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive || m.id == exceptId) continue;
                var d = Iso.GridDistance(m.Grid(), grid);
                if (d > maxDistance || d >= bestD) continue;
                bestD = d;
                best = m;
            }
            return best;
        }

        /// <summary>把玩家放到怪物旁（8 邻里的可走格）。</summary>
        private static void PlacePlayerAdjacent(Vector2Int monsterGrid)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(monsterGrid.x + dx, monsterGrid.y + dy);
                    if (!_ctx.Map.Walkable(g)) continue;
                    _player.SetGrid(g, Iso.DirectionTo(g, monsterGrid));
                    return;
                }
            }
            _player.SetGrid(monsterGrid);
        }

        /// <summary>
        /// 把玩家放到离怪物指定距离的可走格上（在环上找一个满足欧氏距离上限的点）。
        /// <para>
        /// `maxEuclidean` 必须给：怪物的仇恨判定用的是**欧氏距离**（`MonsterAggroRange`/`RangedRange`），
        /// 而环上的点是按 Chebyshev 距离挑的 ⇒ 不设上限时可能落到 8 之外的"看得见却不算仇恨"的位置
        /// （实测踩到过：Range 怪在 9.22 格外没进入仇恨，测试拿不到出手证据）。
        /// </para>
        /// </summary>
        private static void PlacePlayerAtDistance(Vector2Int monsterGrid, int distance,
            float maxEuclidean = float.MaxValue)
        {
            // C3 之后「线段不得被不可走地形阻断」对**怪物出手**同样生效
            //   （`CombatModule.RequestMonsterAttack` → `MeleeShape.LineClear`，与玩家侧同一把尺子）
            //   （Range 用例实测出手 0 次）。现在**优先挑视线通畅**的格（同一把 `MeleeShape.LineClear`），
            //   环上找不到才退回旧口径并留一行披露 —— 断言本身（在射程内必须出手）一字未改。
            if (TryPlacePlayerAtDistance(monsterGrid, distance, maxEuclidean, true)) return;

            Console.WriteLine("   [披露] 环上没有**视线通畅**的可走格（C3 线段口径）⇒ 退回旧口径（允许被墙挡住）；"
                + "若因此不出手，那是正确行为，见该怪日志 `monatk.blocked`");
            if (TryPlacePlayerAtDistance(monsterGrid, distance, maxEuclidean, false)) return;

            MonsterLogFallback(monsterGrid, distance, maxEuclidean);
            _player.SetGrid(_ctx.Map.SpawnPoint);
        }

        /// <summary>在环上挑一格摆玩家；<paramref name="requireLineOfSight"/> = 是否要求与怪的线段通畅。成功返回 true。</summary>
        private static bool TryPlacePlayerAtDistance(Vector2Int monsterGrid, int distance,
            float maxEuclidean, bool requireLineOfSight)
        {
            var center = new Vector2(monsterGrid.x + 0.5f, monsterGrid.y + 0.5f);

            for (var r = distance; r <= distance + 3; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var g = new Vector2Int(monsterGrid.x + dx, monsterGrid.y + dy);
                        if (!_ctx.Map.Walkable(g)) continue;

                        var euclid = Vector2.Distance(center, new Vector2(g.x + 0.5f, g.y + 0.5f));
                        if (euclid > maxEuclidean) continue;
                        if (requireLineOfSight && !MeleeShape.LineClear(_ctx.Map.Walkable, monsterGrid, g)) continue;

                        _player.SetGrid(g, Iso.DirectionTo(g, monsterGrid));
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>摆位失败时说清楚（否则测试报"怪没出手"会让人查错方向）。</summary>
        private static void MonsterLogFallback(Vector2Int monsterGrid, int distance, float maxEuclidean)
        {
            Console.WriteLine($"    ⚠️ PlacePlayerAtDistance: 在 {monsterGrid} 周围 {distance}~{distance + 3} 环上" +
                              $"找不到满足欧氏上限 {maxEuclidean:0.0} 的可走格 ⇒ 玩家退回出生点（本用例的距离不再可控）");
        }

        /// <summary>
        /// 挑一只"与玩家连线上没有别的怪挡路"的目标（保证投射物测试断言的是**瞄准的那只**），
        /// 并给出一个 8~10 格外的可走玩家格。
        /// </summary>
        private static MonsterState PickIsolatedTarget(out Vector2Int playerGrid)
        {
            foreach (var cand in _ctx.Monster.All)
            {
                if (cand == null || !cand.alive) continue;

                for (var r = 8; r <= 10; r++)
                {
                    if (!TrySpotOnRing(cand.Grid(), r, out var g)) continue;
                    if (!LineIsClear(g, cand)) continue;
                    if (!TerrainClear(g, cand.Grid())) continue;   // ★ 15.2 起地形会挡投射物

                    playerGrid = g;
                    return cand;
                }
            }
            playerGrid = _ctx.Map.SpawnPoint;
            return null;
        }

        private static bool TrySpotOnRing(Vector2Int center, int r, out Vector2Int grid)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var g = new Vector2Int(center.x + dx, center.y + dy);
                    if (!_ctx.Map.Walkable(g)) continue;
                    grid = g;
                    return true;
                }
            }
            grid = center;
            return false;
        }

        /// <summary>该格到目标的直线上有没有别的活怪（阈值 1.6 格 = 弹道命中半径量级）。</summary>
        private static bool LineIsClear(Vector2Int from, MonsterState target)
        {
            var a = new Vector2(from.x + 0.5f, from.y + 0.5f);
            var b = new Vector2(target.gridX + 0.5f, target.gridY + 0.5f);

            foreach (var m in _ctx.Monster.All)
            {
                if (m == null || !m.alive || m.id == target.id) continue;
                var p = new Vector2(m.gridX + 0.5f, m.gridY + 0.5f);
                if (PointSegmentDistance(p, a, b) <= 1.6f) return false;
            }
            return true;
        }

        private static float PointSegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);

            var t = Vector2.Dot(p - a, ab) / len2;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;
            return Vector2.Distance(p, a + ab * t);
        }

        private static float DistanceToPlayer(Vector2Int monsterGrid)
        {
            return Vector2.Distance(new Vector2(_player.Grid.x + 0.5f, _player.Grid.y + 0.5f),
                new Vector2(monsterGrid.x + 0.5f, monsterGrid.y + 0.5f));
        }

        /// <summary>
        /// 配表**原值** `monster_c.speed`（= 官方 `MonStats.Velocity`，单位 **map 单位/秒**，不是格/秒）。
        /// 打印时请标清量纲；要拿"格/秒"请用 <see cref="TilesPerSecondOf"/>。
        /// </summary>
        private static float SpeedOf(MonsterState s)
        {
            var row = Table.Tables.Default.Monster.Get(s.kindId);
            return row != null ? row.Speed : 0f;
        }

        /// <summary>
        /// 怪物的**实际移动速度（格/秒）** —— 与**生产实现** `MonsterModule.SpeedOf`
        /// （`Module/Monster/MonsterModule.cs:531-536`）逐字同式：
        /// `monster_c.speed × MonsterTuning.SpeedToTilesPerSecond`（0.2，1 格 = 5 map 单位，
        /// 出处 `MonsterTuning.cs:120`），再被 `MonsterTuning.MinMoveSpeed`（0.2）钳下限。
        /// <para>为什么必须按这个量纲算窗口：僵尸 `Velocity`=1 ⇒ **0.20 格/秒**
        /// （`MonsterTuning.cs:117` 逐行注明），不是 1 格/秒。</para>
        /// </summary>
        private static float TilesPerSecondOf(MonsterState s)
        {
            var v = SpeedOf(s) * MonsterTuning.SpeedToTilesPerSecond;
            return v < MonsterTuning.MinMoveSpeed ? MonsterTuning.MinMoveSpeed : v;
        }

        private static float MonsterKeepDistance()
        {
            return 4.0f;   // MonsterTuning.RangedKeepDistance
        }

        private static bool ContainsStartsWith(List<string> list, string prefix)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].StartsWith(prefix, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static CharacterSave NewSave(PlayerClass cls, int skillPoints)
        {
            return new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = "Check" + cls,
                cls = cls,
                level = 1,
                skillPoints = skillPoints,
                mapSeed = 12345,
                buttonSkills = new List<int> { -1, -1 },
            };
        }

        private static int CountTree(SkillTreeArgs args, int tree)
        {
            var n = 0;
            for (var i = 0; i < args.skills.Count; i++)
            {
                if (args.skills[i].tree == tree) n++;
            }
            return n;
        }

        private static int CountLearnable(SkillTreeArgs args)
        {
            var n = 0;
            for (var i = 0; i < args.learnable.Count; i++) if (args.learnable[i]) n++;
            return n;
        }

        private static int LearnedLevelOf(SkillTreeArgs args, int skillId)
        {
            for (var i = 0; i < args.skills.Count; i++)
            {
                if (args.skills[i].id == skillId) return args.learnedLevels[i];
            }
            return -1;
        }

        private static SkillDef FirstLearnable(PlayerClass cls)
        {
            foreach (var s in _ctx.Skill.Available)
            {
                if (s.reqLevel <= 1 && s.reqSkill == 0) return s;
            }
            return null;
        }

        private static SkillDef FirstWithPrereq(PlayerClass cls, int excludePrereqId)
        {
            foreach (var s in _ctx.Skill.Available)
            {
                if (s.reqSkill != 0 && s.reqSkill != excludePrereqId) return s;
            }
            return null;
        }

        private static SkillDef FindSkill(PlayerClass cls, string name)
        {
            foreach (var s in _ctx.Skill.Available)
            {
                if (s.name == name) return s;
            }
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 审计 R1（21 个武器伤害类技能"零效果"，含用户报的「重击#121」）+ R2（30 行次要投射物槽未导出）
        /// 的**回归断言**。判**过程**、不判"调用了一次函数"：
        /// <list type="number">
        /// <item><description>配表：`skill_c` 里 `src_dam>0` = 33 行；其中"自身无伤害值(dmg_max=0) ∧ 无主投射物"
        /// = 21 行，**逐个列出** `calc1` 原文 / `*calc1 desc` / 解析出的倍率（不抽样）。</description></item>
        /// <item><description>生产入口：21 行逐个过 `DamageFormula.PhysicalDamageEd`（与技能结算**同一入口**），
        /// 用 `item_c` 里**真实的两把武器**（最小/最大 `dmg_max`）做对照 ⇒ 伤害必须 >0 且随武器单调变化、
        /// 随技能等级变化。</description></item>
        /// <item><description>端到端：野蛮人真学「重击」→ `TryCast` ⇒ 从**运行时自己打的日志**取 `⇒ raw=`，
        /// 比较「徒手 vs 装备武器」两组 + 怪物真的掉血。</description></item>
        /// </list>
        /// </summary>
        private static void Step17_WeaponDamageSkills()
        {
            Section("17. ★ 片 N（审计 R1/R2）：武器伤害类技能（官方 SrcDam≠0）—— 配表 + 生产入口 + 徒手/武器对照");

            // ── 17.1 配表（21 行逐个，不抽样）────────────────────────────────
            var all = Table.Tables.Default.Skill.All();
            var srcRows = new List<Table.BaseSkillRow>();
            var armed = new List<Table.BaseSkillRow>();
            var slotRows = 0;
            for (var i = 0; i < all.Count; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (!string.IsNullOrEmpty(r.MissileA) || !string.IsNullOrEmpty(r.MissileB)
                    || !string.IsNullOrEmpty(r.MissileC)) slotRows++;
                if (r.SrcDam <= 0) continue;
                srcRows.Add(r);
                if (r.DmgMax <= 0 && string.IsNullOrEmpty(r.Missile)) armed.Add(r);
                Console.WriteLine($"  id={r.Id,3} {r.Name,-6} src_dam={r.SrcDam,3}/128 calc1=\"{r.DmgPctCalc}\" " +
                                  $"desc=\"{r.DmgPctDesc}\" base={r.DmgPctBase} per_lvl={r.DmgPctPerLvl} " +
                                  $"parsed={r.DmgPctParsed} 主槽=\"{r.Missile}\" " +
                                  $"次槽=({r.MissileA}|{r.MissileB}|{r.MissileC})");
            }
            Check("skill_c 里官方 SrcDam≠0 的技能 = 33 行（官方 skills.txt 5 职业口径）",
                srcRows.Count == 33, srcRows.Count.ToString());
            Check("其中「自身无伤害值(dmg_max=0) ∧ 无主投射物」= 21 行（= 审计 R1 同族，含「重击#121」）",
                armed.Count == 21, armed.Count.ToString());
            var noCalc = 0;
            var hasBash = false;
            for (var i = 0; i < armed.Count; i++)
            {
                var r = armed[i];
                if (r.DmgPctParsed == 0 && string.IsNullOrEmpty(r.DmgPctCalc)) noCalc++;
                if (string.Equals(r.Code, "Bash", StringComparison.Ordinal)) hasBash = true;
            }
            Check("21 行都带官方 calc1 原文（parsed=0 的行也不静默：原文在表里 + 运行时 WarnOnce）",
                noCalc == 0, $"缺原文 {noCalc} 行");
            Check("「重击」= 官方 Bash，被识别为武器伤害类", hasBash, "skill_c.code=Bash");
            Check("★ R2：次要投射物槽 missile_a/b/c 已导出 = 31 行（旧表 0 行）",
                slotRows == 31, slotRows.ToString());

            // ── 17.2 生产入口公式（21 行 × 真实武器）──────────────────────────
            var items = Table.Tables.Default.Item.All();
            Table.BaseItemRow wSmall = null, wBig = null;
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || it.Source != "weap") continue;
                if (it.DmgMin <= 0 || it.DmgMax <= 0) continue;      // 排除单手列为空的双手/投掷武器行
                if (wSmall == null || it.DmgMax < wSmall.DmgMax) wSmall = it;
                if (wBig == null || it.DmgMax > wBig.DmgMax) wBig = it;
            }
            Check("item_c 里取到两把真实武器（最小/最大 dmg_max）",
                wSmall != null && wBig != null && wBig.DmgMax > wSmall.DmgMax,
                wSmall == null || wBig == null
                    ? "null"
                    : $"{wSmall.Name}({wSmall.DmgMin}-{wSmall.DmgMax}) vs {wBig.Name}({wBig.DmgMin}-{wBig.DmgMax})");
            if (wSmall == null || wBig == null || wBig.DmgMax <= wSmall.DmgMax) return;

            const int fakeStr = 20;     // FakePlayer 固定 Str/Dex = 20（见 shim/HostFakes.cs）
            const int fakeDex = 20;
            var notPositive = 0;
            var noScale = 0;
            var noLevel = 0;
            for (var i = 0; i < armed.Count; i++)
            {
                var r = armed[i];
                var ed1 = r.DmgPctParsed != 0 ? r.DmgPctBase : 0;
                var ed3 = r.DmgPctParsed != 0 ? r.DmgPctBase + r.DmgPctPerLvl * 2 : 0;

                // 三个对照点（**同一次掷值下比才可比**）：
                //   lo    = 大武器**最小掷值** ⇒ 必须 >0（连最小值都出伤害）
                //   hiS   = 小武器最大掷值 / hiB = 大武器最大掷值 ⇒ hiB > hiS（伤害随武器变）
                //   hiB3  = 大武器最大掷值 @3 级 ⇒ > hiB（倍率随技能等级变）
                var lo = DamageFormula.PhysicalDamageEd(wBig.DmgMin, fakeStr, fakeDex,
                    wBig.StrBonus, wBig.DexBonus, ed1);
                var hiS = DamageFormula.PhysicalDamageEd(wSmall.DmgMax, fakeStr, fakeDex,
                    wSmall.StrBonus, wSmall.DexBonus, ed1);
                var hiB = DamageFormula.PhysicalDamageEd(wBig.DmgMax, fakeStr, fakeDex,
                    wBig.StrBonus, wBig.DexBonus, ed1);
                var hiB3 = DamageFormula.PhysicalDamageEd(wBig.DmgMax, fakeStr, fakeDex,
                    wBig.StrBonus, wBig.DexBonus, ed3);

                if (lo <= 0) notPositive++;
                if (!(hiB > hiS)) noScale++;
                if (r.DmgPctParsed != 0 && r.DmgPctPerLvl != 0 && !(hiB3 > hiB)) noLevel++;

                Console.WriteLine($"  {r.Name,-6}#{r.Id,-3} 倍率 {ed1}%（3 级 {ed3}%）⇒ " +
                                  $"{wBig.Name} 最小掷={lo}；最大掷 {wSmall.Name}={hiS} vs {wBig.Name}={hiB} " +
                                  $"，{wBig.Name}@3级={hiB3}");
            }
            Check("21 行的武器伤害都 >0（连武器最小掷值都出伤害 ⇒ 武器真的进了计算）",
                notPositive == 0, $"非正 {notPositive} 行");
            Check("21 行的伤害随武器变化（同一掷值：大武器 > 小武器；逐行单调，不是「调用过」）",
                noScale == 0, $"未随武器变化 {noScale} 行");
            Check("每级伤害倍率增量真的生效（增量≠0 的行：3 级 > 1 级）", noLevel == 0, $"未随等级变化 {noLevel} 行");

            // ── 17.3 端到端：野蛮人真学「重击」→ 徒手 vs 装备武器 ──────────────
            var bashId = 0;
            for (var i = 0; i < armed.Count; i++)
                if (string.Equals(armed[i].Code, "Bash", StringComparison.Ordinal)) bashId = armed[i].Id;
            Check("找到「重击」id（官方 Bash）", bashId > 0, bashId.ToString());
            if (bashId <= 0) return;

            var save = NewSave(PlayerClass.Barbarian, 5);
            _ctx.Skill.ResetForClass(PlayerClass.Barbarian, save);
            _player.SetLevel(1);
            _player.SetSkillPoints(5);
            Check($"野蛮人学会「重击#{bashId}」", _ctx.Skill.Learn(bashId), $"等级 {_ctx.Skill.GetLevel(bashId)}");

            // 端到端要"必定"看出差别 ⇒ 选一把 `dmg_min ≥ 5` 的武器：徒手上限 = 2×(1+70%) ≈ 3 < 5×(1+70%) ≈ 8
            // ⇒ 与随机掷值无关，装备组一定大于徒手组（不靠运气）。
            Table.BaseItemRow wE2E = null;
            for (var i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || it.Source != "weap") continue;
                if (it.DmgMin < 5) continue;
                if (wE2E == null || it.DmgMax > wE2E.DmgMax) wE2E = it;
            }
            Check("端到端用武器：item_c 里存在 dmg_min ≥ 5 的武器（保证「装备 > 徒手」与掷值无关）",
                wE2E != null, wE2E == null ? "null" : $"{wE2E.Name}({wE2E.DmgMin}-{wE2E.DmgMax})");
            if (wE2E == null) return;

            PrepareMap(AreaId.BloodMoor, 771717);
            _player.SetMana(500);

            int hpUnarmed, hpArmed;
            bool castUnarmed, castArmed;
            var rawUnarmed = CastBashAndReadRaw(bashId, false, wE2E, out hpUnarmed, out castUnarmed);
            var rawArmed = CastBashAndReadRaw(bashId, true, wE2E, out hpArmed, out castArmed);

            Console.WriteLine($"  徒手 raw={rawUnarmed}（掉血 {hpUnarmed}）/ " +
                              $"装备 {wE2E.Name}({wE2E.DmgMin}-{wE2E.DmgMax}) raw={rawArmed}（掉血 {hpArmed}）");
            Check("徒手施放「重击」成功且怪物真的掉血", castUnarmed && hpUnarmed > 0,
                $"cast={castUnarmed} drop={hpUnarmed}");
            Check("装备武器施放「重击」成功且怪物真的掉血", castArmed && hpArmed > 0,
                $"cast={castArmed} drop={hpArmed}");
            Check("★ 同一技能：装备武器的结算伤害 > 徒手（武器真的参与 ⇒ 不再是「零效果」）",
                rawArmed > rawUnarmed && rawUnarmed > 0, $"{rawUnarmed} → {rawArmed}");
            Check("结算日志带官方 SrcDam 与官方 calc1 倍率（可追溯到配表列，不是硬编码）",
                _log.Has("×SrcDam 128/128") && _log.Has("技能倍率=50%"), "见上方 [Skill] 武器伤害结算 行");
            Console.WriteLine();
        }

        /// <summary>
        /// （L3 锚点：由被测程序在结算时写出，不是断言方自己算的）。
        /// 返回 -1 表示没读到 ⇒ 断言必红，不会假绿。
        /// </summary>
        private static int CastBashAndReadRaw(int bashId, bool armed, Table.BaseItemRow weapon,
            out int hpDrop, out bool castOk)
        {
            hpDrop = 0;
            castOk = false;

            _ctx.Monster.DespawnAll();
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            _item.EquipmentOverride.Clear();
            if (armed && weapon != null)
            {
                _item.EquipmentOverride.Add(new ItemStack
                {
                    itemId = weapon.Id,
                    name = weapon.Name,
                    type = ItemType.Weapon,
                    dmgMin = weapon.DmgMin,
                    dmgMax = weapon.DmgMax,
                    count = 1,
                });
            }

            var target = PickMonster(m => m.alive && m.ai == MonsterAI.Melee);
            if (target == null) target = PickMonster(m => m.alive);
            if (target == null) return -1;

            PlacePlayerAdjacent(target.Grid());
            _player.SetMana(500);
            _ctx.Skill.Tick(10f);                       // 清冷却（上一组的冷却不该污染这一组）

            var hp0 = target.hp;
            var since = _log.Lines.Count;
            castOk = _ctx.Skill.TryCast(bashId, target.Grid());
            hpDrop = hp0 - target.hp;
            return LastWeaponRaw(since);
        }

        private static int LastWeaponRaw(int since)
        {
            for (var i = _log.Lines.Count - 1; i >= 0 && i >= since; i--)
            {
                var k = _log.Lines[i].IndexOf("⇒ raw=", StringComparison.Ordinal);
                if (k < 0) continue;
                var s = _log.Lines[i].Substring(k + 6);
                var e = s.IndexOf(' ');
                if (e > 0) s = s.Substring(0, e);
                int v;
                if (int.TryParse(s, out v)) return v;
            }
            return -1;
        }

        /// <summary>
        /// 18. C3：**攻击判定形状**（正面扇形 + 矩形走廊 + 线段通畅）——
        /// 起因 = 用户本轮原话「你是圆形判断的打击范围」「为什么打击范围这么奇怪」「屏幕外都能打我？？？？？」。
        /// <para>纯函数逐例驱动（不埋 MonoBehaviour、不依赖 AppContext 与真实地图）。</para>
        /// <para>只**新增**断言，不动 1~17 节的任何判据。</para>
        /// </summary>
        private static void Step18_AttackShape()
        {
            Section("18. ★ C3：攻击判定形状 = 正面扇形 + 矩形走廊 + 线段通畅（⛔ 不是圆）");

            const float reach = 1.6f;   // 契约常量（下表断言它 == GameConst.MeleeRange）
            Check("判据用到的 reach 与契约常量一致", Math.Abs(reach - GameConst.MeleeRange) < 0.001f,
                $"reach={reach:0.00} GameConst.MeleeRange={GameConst.MeleeRange:0.00}");

            // 朝向取 N。格增量走**引擎权威表** `Iso.DirectionDelta(Dir8.N)`（宿主与 MeleeShape 都不另写映射表）
            var nv = Iso.DirectionDelta(Dir8.N);
            float fx, fy;
            var got = MeleeShape.ToUnit(nv.x, nv.y, out fx, out fy);
            Check("朝向 N（Iso.DirectionDelta）可解成单位向量", got && Math.Abs(fx * fx + fy * fy - 1f) < 1e-4f,
                $"N=({fx:0.00},{fy:0.00}) len²={fx * fx + fy * fy:0.0000} 表值=({nv.x},{nv.y})");
            float zx, zy;
            Check("零向量（朝向不可解）⇒ 返回 false",
                !MeleeShape.ToUnit(0, 0, out zx, out zy), "(0,0) ⇒ false");

            // ① 正前方 1.5 格（沿朝向、垂距 0）⇒ 命中
            Check("正前方 1.5 格 ⇒ 在判定形状内",
                MeleeShape.InFrontCone(fx, fy, 0f, -1.5f, MeleeShape.FrontConeCos)
                && MeleeShape.InMeleeRect(fx, fy, 0f, -1.5f, reach, MeleeShape.MeleeHalfWidth),
                "偏移 (0,-1.5)：锥内 ∧ 走廊内");

            // ② 正侧方 1.5 格（90°）⇒ **不**命中 —— 这一条就是"扇形/矩形 vs 圆"的分水岭
            var sideCone = MeleeShape.InFrontCone(fx, fy, 1.5f, 0f, MeleeShape.FrontConeCos);
            var sideRect = MeleeShape.InMeleeRect(fx, fy, 1.5f, 0f, reach, MeleeShape.MeleeHalfWidth);
            Check("正侧方 1.5 格 ⇒ **不**命中（旧圆口径会命中）", !sideCone && !sideRect,
                $"锥内={sideCone} 走廊内={sideRect}；距离 1.5 ≤ {reach:0.00} ⇒ 纯半径判定必命中");

            // ③ 正后方 1.5 格 ⇒ 不命中
            Check("正后方 1.5 格 ⇒ 不命中",
                !MeleeShape.InFrontCone(fx, fy, 0f, 1.5f, MeleeShape.FrontConeCos)
                && !MeleeShape.InMeleeRect(fx, fy, 0f, 1.5f, reach, MeleeShape.MeleeHalfWidth),
                "偏移 (0,+1.5) = 背后");

            // ④ 距离 > 攻击范围 ⇒ 不命中
            Check("正前方 2.5 格（> 攻击范围）⇒ 不命中",
                !MeleeShape.InMeleeRect(fx, fy, 0f, -2.5f, reach, MeleeShape.MeleeHalfWidth),
                $"沿轴 2.5 > reach {reach:0.00}");

            // ⑤ 8 向量化误差（22.5°）下的斜向贴身不许被丢掉（否则"打不到贴身的怪"）
            //   偏差偏移必须**以引擎权威朝向向量 (fx,fy) 为轴**旋转，不许假定"N 的格增量 = (0,-1)"：
            //   `Iso.DirectionDelta(Dir8.N)` = (-1,-1)（`IsoLayout.DirectionDelta` 的表；屏幕正上）——
            //   旧写法按 (0,-1) 口径摆偏移 ⇒ 沿轴/垂距两项都算错（实测把"实际垂距 1.30 > 半宽 1.20"
            //   错报成"沿轴 1.30 ≤ 1.60、垂距 0.54 ≤ 1.20"）。这里改为现算轴与投影。
            var c = (float)Math.Cos(22.5 * Math.PI / 180.0);
            var s = (float)Math.Sin(22.5 * Math.PI / 180.0);
            const float diag = 1.41f;
            // 把**单位朝向向量**绕原点旋转 22.5° 再乘距离 ⇒ "与朝向差 22.5°、距 1.41 格"的偏移
            var offX = diag * (fx * c - fy * s);
            var offY = diag * (fx * s + fy * c);
            var along = offX * fx + offY * fy;                 // 沿轴投影（现算，不用假定轴）
            var perp = Math.Abs(offX * (-fy) + offY * fx);     // 垂距（现算）
            Check("斜向贴身（偏差 22.5°、距离 1.41）⇒ 仍命中（8 向不丢）",
                MeleeShape.InFrontCone(fx, fy, offX, offY, MeleeShape.FrontConeCos)
                && MeleeShape.InMeleeRect(fx, fy, offX, offY, reach, MeleeShape.MeleeHalfWidth),
                $"朝向向量 ({fx:0.00},{fy:0.00})（引擎表 N=({nv.x},{nv.y})）偏移 ({offX:0.00},{offY:0.00}) "
                + $"沿轴 {along:0.00} ≤ {reach:0.00}、垂距 {perp:0.00} ≤ 半宽 {MeleeShape.MeleeHalfWidth:0.00}、"
                + $"余弦 {(offX * fx + offY * fy) / diag:0.000} ≥ 锥阈值 {MeleeShape.FrontConeCos:0.000}");

            // ⑥ 线段通畅（不许隔墙/隔水打）
            Func<Vector2Int, bool> wall = g => !(g.x == 1 && g.y == 0);
            Check("隔一格墙 ⇒ 线段不通",
                !MeleeShape.LineClear(wall, new Vector2Int(0, 0), new Vector2Int(2, 0)),
                "from(0,0) → to(2,0)，(1,0) 不可走");
            Check("相邻 1 格（无中间格）⇒ 线段通（两端点不作阻断判据）",
                MeleeShape.LineClear(wall, new Vector2Int(0, 0), new Vector2Int(1, 0)),
                "from(0,0) → to(1,0)");
            Check("空地直线 4 格 ⇒ 线段通",
                MeleeShape.LineClear(g => true, new Vector2Int(0, 0), new Vector2Int(4, 2)),
                "无阻断格");
            Check("walkable 委托为 null（地图未接入）⇒ 放行，不把'没地图'变成'打不到'",
                MeleeShape.LineClear(null, new Vector2Int(0, 0), new Vector2Int(4, 2)),
                "null ⇒ true");

            // ⑦ 远程出手距离上限必须**严格小于画面半宽**（= 用户「屏幕外都能打我」的判据）
            const float tileWorldW = GameConst.IsoTilePxW / (float)GameConst.PixelsPerUnit;   // 128/64 = 2.0
            var visibleHalfWidthTiles = 6f * (16f / 9f) / tileWorldW;                          // ortho 6 × 画幅 ÷ 格宽
            Check("远程出手上限 < 可见半宽（出手时怪物一定在画面内）",
                MonsterTuning.RangedAttackMaxRange < visibleHalfWidthTiles,
                $"RangedAttackMaxRange={MonsterTuning.RangedAttackMaxRange:0.00} < 可见半宽 {visibleHalfWidthTiles:0.00} 格" +
                $"（ortho 6 ×16/9 ÷ 一格世界宽 {tileWorldW:0.0}）");
            Check("远程出手上限 > 近战范围（远程仍比近战远）",
                MonsterTuning.RangedAttackMaxRange > GameConst.MeleeRange,
                $"{MonsterTuning.RangedAttackMaxRange:0.00} > {GameConst.MeleeRange:0.00}");

            //   **同格（偏移 (0,0)）⇒ 必命中**。
            //   起因 = 实测 40 次真实左键**全部**被"判定形状拒绝（锥半角 60°）偏移=(0,0) 距离 0.00"
            //   原版口径 = 近战触及是**距离 / 外接框**比较，不是角度比较：
            //     · `原版资源/d2lod1.10txt-1.10f/data/global/excel/Weapons.txt` 第 20 列 `rangeadder`
            //       （Short Sword / Hand Axe = 空(=0)，War Staff = 1）⇒ 触及 = 1 + rangeadder **格**；
            //     · 同目录 `MonStats2.txt` 第 8 列 `MeleeRng`（skeleton1 = 0）⇒ 同样是**格数**。
            //   ⇒ `0 ≤ reach` 恒真（同格时两者外接框必然重叠）⇒ 同格必命中；
            //   而角度锥是**本项目新增**的量化近似，不该在这个恒真的点上把攻击拒掉。
            Check("同格（偏移 (0,0)）⇒ 在判定形状内（★ melee-samecell）",
                MeleeShape.InFrontCone(fx, fy, 0f, 0f, MeleeShape.FrontConeCos)
                && MeleeShape.InMeleeRect(fx, fy, 0f, 0f, reach, MeleeShape.MeleeHalfWidth)
                && MeleeShape.LineClear(g => true, new Vector2Int(0, 0), new Vector2Int(0, 0)),
                "偏移 (0,0)：锥内（零偏移特例）∧ 走廊内（沿轴 0 ∈ [0,1.60]、垂距 0 ≤ 1.20）"
                + " ∧ 线段通（无中间格）");

            // ⑨ ⑧ 是"只补退化点"的负向对照：正侧方 90° 仍必须被拒 ——
            //   若这一条翻了，说明 ⑧ 的改法把扇形放宽成了圆形（C3 定稿口径不许推翻）。
            Check("同格特例**没有**把扇形放宽：正侧方 1.5 格仍不命中（= ② 的修复后复核）",
                !MeleeShape.InFrontCone(fx, fy, 1.5f, 0f, MeleeShape.FrontConeCos)
                && !MeleeShape.InMeleeRect(fx, fy, 1.5f, 0f, reach, MeleeShape.MeleeHalfWidth),
                $"锥内={MeleeShape.InFrontCone(fx, fy, 1.5f, 0f, MeleeShape.FrontConeCos)}"
                + $" 走廊内={MeleeShape.InMeleeRect(fx, fy, 1.5f, 0f, reach, MeleeShape.MeleeHalfWidth)}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 19. melee-samecell：**真实链路**同格攻击（不是纯函数）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// "被**判定形状**拒绝"。
        /// <para>
        /// 为什么不能只靠第 18 节的纯函数断言：形状闸门是**接线**（`CombatModule.ShapeGate` 调
        /// `MeleeShape`），纯函数绿 ≠ 生产路径绿。本节把"同格 ⇒ 结算"钉在真实调用链上。
        /// </para>
        /// </summary>
        private static void Step19_SameCellMeleeHit()
        {
            Section("19. ★ melee-samecell：玩家与怪**同格**时 `RequestAttack` 必须结算（真实链路）");

            PrepareMap(AreaId.BloodMoor, 20250916);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            var target = PickMonster(m => m.ai == MonsterAI.Melee);
            Check("刷出了近战怪（同格用例的靶子）", target != null,
                target != null ? $"m#{target.id} {target.name}" : "null");
            if (target == null) return;

            // 玩家**站到怪那一格**，朝向刻意取一个与"怪在我的哪一侧"无关的固定方向（`Dir8.S`）：
            // 零偏移下"夹角"无定义，命中**不许**依赖朝向 —— 这正是本用例要钉的地方。
            _player.SetGrid(target.Grid(), Dir8.S);
            Check("玩家与靶子**同格**（偏移 (0,0)）",
                _player.Grid == target.Grid(),
                $"player=({_player.Grid.x},{_player.Grid.y}) monster=({target.Grid().x},{target.Grid().y})"
                + $" 偏移=({target.Grid().x - _player.Grid.x},{target.Grid().y - _player.Grid.y})");

            var shapeRejects = 0;
            var hit = false;
            for (var attempt = 0; attempt < 40 && !hit; attempt++)
            {
                var hp0 = target.hp;
                var mark = _log.Lines.Count;
                _ctx.Combat.RequestAttack(target.id);          // ← 生产入口（与左键点怪同一条路）
                for (var i = mark; i < _log.Lines.Count; i++)
                {
                    if (_log.Lines[i].Contains("判定形状")) shapeRejects++;
                }
                if (target.hp < hp0 || !target.alive) hit = true;
                _ctx.Combat.Tick(GameConst.PlayerAttackInterval + 0.01f);   // 清冷却再试
            }

            Check("同格攻击**从不**被判定形状拒绝", shapeRejects == 0,
                $"被拒次数 {shapeRejects}（⚠️ `WarnThrottled` 会把重复行折叠 ⇒ 这个数是**节流后的下限**；"
                + "旧口径下非 0 即代表\"一次都没挥出去\"）");
            Check("同格攻击造成了伤害（40 次内至少命中一次）", hit,
                $"m#{target.id} hp={target.hp} alive={target.alive}");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 20. lineclear-fix：**线段被地形阻断 ⇒ 怪不许出手**（"隔墙反复挥空"的回归）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 地形阻断"时**只打日志就 return**，而发起方 `MonsterAi.TryAttack` 在发起前已经把
        /// **出手动画 / 出手音效 / 出手计时器**都写好了 ⇒ 怪每 `AttackIntervalSeconds` 挥一次空，
        /// 且永不知道自己被拒（死循环）。
        /// <para>
        /// 本用例把这条钉在**真实链路**上（不是纯函数）：
        /// ① 在真图上找一组「格距 ≤ `GameConst.MeleeRange`（够得着）但 `LineClear` 判不通（看不见）」的
        ///    （怪格, 玩家格）；
        /// ② 逐 tick 采样"该 tick 决策时线段是否被挡"，只把**被挡 tick** 里发生的
        ///    **出手（音效 = `MonsterModule.RequestMonsterAttack` 的第一句）** 与
        ///    **结算层拒绝（`monatk.blocked` 日志）** 记进计数；
        /// ③ 断言两者都 == 0（修前：每 1.10s 一次 ⇒ 必然 > 0）。
        /// </para>
        /// <para>
        /// 为什么不数 `Trace.AttacksBy`：被拒的出手**不会**产生 `DamageDealt` 事件（结算层在
        /// 可观测的"挥手"只有两处：出手音效（在结算之前播）与结算层拒绝日志。
        /// </para>
        /// <para>
        /// 判据/阈值一字未放宽：本用例只**新增**断言，不改任何既有用例。
        /// </para>
        /// </summary>
        private static void Step20_MeleeLineBlocked()
        {
            Section("20. ★ lineclear-fix：线段被地形阻断时怪**不许出手**（隔墙挥空）");

            var n8 = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
                new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
            };

            // ── 20.0 几何前提：先把"哪一档出手才可能被地形拒"量清楚 ────────────────
            //   `MeleeShape.LineClear` 是 Bresenham（**除两端点外**每一格都要可走）：
            //   · 步长 ≤ 1 格（8 邻）⇒ 循环第一步就落在终点 ⇒ **没有中间格** ⇒ 恒通
            //     （用"全不可走"的地形委托也打不通）。近战触及 `GameConst.MeleeRange` = 1.60 ⇒ 只可能落在
            //     8 邻 ⇒ **近战出手永远不可能被地形拒**（所以用户看到的"隔墙挥空"不可能出在近战身上）。
            //   · 步长 ≥ 2 格（远程上限 `RangedAttackMaxRange` = 5.0 ⇒ 2~5 格）⇒ 有中间格 ⇒ 可被拒。
            //   本条只**量事实**，不改任何判据。
            var probeWall = new Func<Vector2Int, bool>(g => false);
            var adjacentAlwaysClear = true;
            for (var i = 0; i < n8.Length; i++)
            {
                var bn = new Vector2Int(10 + n8[i].x, 10 + n8[i].y);
                if (!MeleeShape.LineClear(probeWall, new Vector2Int(10, 10), bn)) adjacentAlwaysClear = false;
            }
            Check("20.0 几何前提：8 邻（步长 ≤ 1 格）上 `LineClear` **无中间格** ⇒ 恒通（近战触及只可能落在这里）",
                adjacentAlwaysClear, "8 个偏移逐个用 `g => false` 的地形委托试过，结果全部 true");
            Check("20.0 几何前提：2 格正交（步长 ≥ 2）= 有中间格 ⇒ 可被地形拒（远程 2~5 格这一档）",
                !MeleeShape.LineClear(probeWall, new Vector2Int(10, 10), new Vector2Int(12, 10)),
                "from(10,10) → to(12,10)，(11,10) 不可走 ⇒ false");

            PrepareMap(AreaId.BloodMoor, 20250924);
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);

            MonsterState target = null;
            var spot = Vector2Int.zero;

            // 找「**射程内**（格距 ∈ [`RangedKeepDistance`, `RangedAttackDistanceCap`]）但**线段被挡**」的一组
            // （怪, 玩家格）。下界取 `RangedKeepDistance` 是为了让怪落在"出手那一支"而不是"后撤那一支"
            // （`MonsterAi.Ranged` 在**连续**距离 < `RangedKeepDistance` 时先 `StepAway`、后撤期间不射击）。
            var lo = MonsterTuning.RangedKeepDistance + 0.05f;
            var hi = Mathf.Min(GameConst.RangedRange, MonsterTuning.RangedAttackMaxRange);
            var areas = new[] { AreaId.BloodMoor, AreaId.DenOfEvil };
            for (var ai = 0; ai < areas.Length && target == null; ai++)
            {
                if (ai > 0)
                {
                    PrepareMap(areas[ai], 20250924);
                    _ctx.Monster.SpawnArea(areas[ai]);
                }

                foreach (var cand in _ctx.Monster.All)
                {
                    if (cand == null || !cand.alive) continue;
                    if (cand.ai != MonsterAI.Range && cand.ai != MonsterAI.Shaman) continue;
                    var a = cand.Grid();
                    for (var dx = -6; dx <= 6 && target == null; dx++)
                    {
                        for (var dy = -6; dy <= 6; dy++)
                        {
                            var b = new Vector2Int(a.x + dx, a.y + dy);
                            if (!_ctx.Map.InBounds(b) || !_ctx.Map.Walkable(b)) continue;
                            var d = Iso.GridDistanceEuclidean(a, b);
                            if (d < lo || d > hi) continue;                       // 必须落在"出手那一支"
                            if (CombatModule.AttackLineClear(a, b)) continue;     // 必须"看不见"
                            target = cand;
                            spot = b;
                            break;
                        }
                    }
                    if (target != null) break;
                }
            }

            Check("找到「远程怪 + 射程内但线段被地形阻断的玩家格」这一组用例", target != null,
                target != null
                    ? $"m#{target.id} {target.name}（{target.ai}）格 {target.Grid()} ← 玩家格 {spot}"
                      + $"（格距 {Iso.GridDistanceEuclidean(target.Grid(), spot):0.00} ∈ [{lo:0.00},{hi:0.00}]，"
                      + $"LineClear={CombatModule.AttackLineClear(target.Grid(), spot)}）"
                    : "BloodMoor / DenOfEvil 两个区域都没找到「射程内 + 线段被挡」的组合（⛔ 不是跳过，是本用例无法构造）");
            if (target == null) return;

            _player.SetGrid(spot, Iso.DirectionTo(spot, target.Grid()));

            // 出手的**可观测标记** = 出手音效（`MonsterModule.RequestMonsterAttack` 在把球交给结算层
            // **之前**就播了它；被拒的出手也会播 ⇒ 它才是"挥了几次手"的计数器）。
            var attackKey = MonsterSfx.AttackOf(target) ?? SfxKeys.MonsterAttack;
            const string rejectNeedle = "线段被不可走地形阻断";      // `CombatModule.RequestMonsterAttack` 的拒绝文案
            const string aggroNeedle = "aggro m#";                   // `MonsterAi.UpdateEngagement` 进入仇恨

            // 窗口 = 4 × 出手间隔（= 4.40s ⇒ 修前至少有 4 次出手机会）
            var steps = (int)(MonsterTuning.AttackIntervalSeconds * 4f / Dt);
            var blockedSteps = 0;
            var swingsWhileBlocked = 0;
            var rejectsWhileBlocked = 0;
            var swingsTotal = 0;
            var prevSfx = SfxCount(attackKey);
            var prevRej = _log.Count(rejectNeedle);

            for (var s = 0; s < steps; s++)
            {
                // 决策帧的"线段是否被挡"：AI 在本 tick 开头用**当时的格**判定 ⇒ 先采样再 Tick
                var blocked = !CombatModule.AttackLineClear(target.Grid(), _player.Grid);
                _ctx.Monster.Tick(Dt);
                _ctx.Combat.Tick(Dt);

                var sfxNow = SfxCount(attackKey);
                var rejNow = _log.Count(rejectNeedle);
                var dSwing = sfxNow - prevSfx;
                var dRej = rejNow - prevRej;
                prevSfx = sfxNow;
                prevRej = rejNow;
                swingsTotal += dSwing;
                if (!blocked) continue;
                blockedSteps++;
                swingsWhileBlocked += dSwing;
                rejectsWhileBlocked += dRej;
            }

            Console.WriteLine($"  m#{target.id} {target.name}：窗口 {steps * Dt:0.00}s（4 × 出手间隔 "
                + $"{MonsterTuning.AttackIntervalSeconds:0.00}s），其中**线段被挡**的决策帧 {blockedSteps}/{steps}；"
                + $"被挡帧内出手 {swingsWhileBlocked} 次、结算层拒绝 {rejectsWhileBlocked} 次"
                + $"（音效键 {attackKey}；全窗口出手 {swingsTotal} 次）");

            Check("用例真的成立：窗口里有 **线段被阻断** 的决策帧（否则本用例是空转）",
                blockedSteps > 0, $"{blockedSteps}/{steps} 帧（阈值 = > 0）");
            Check("★ 线段被阻断时 **出手 0 次**（怪物不再隔墙挥空）",
                swingsWhileBlocked == 0, $"出手 {swingsWhileBlocked} 次（被挡帧 {blockedSteps}；修前每 "
                + $"{MonsterTuning.AttackIntervalSeconds:0.00}s 一次 ⇒ 必然 > 0）");
            Check("★ 结算层 **一次都没有** 因线段阻断拒绝（发起方已在出手前自检）",
                rejectsWhileBlocked == 0, $"被拒 {rejectsWhileBlocked} 次（日志含「{rejectNeedle}」；"
                + "`WarnThrottled` 只印第 1 次、第 100 次、第 200 次… ⇒ 这个数是**节流后的下限**，0 才是「一次都没有」）");
            Check("用例真的进入了仇恨（不是「怪没发现玩家」导致的空转）",
                _log.Has(aggroNeedle + target.id), $"日志含 \"{aggroNeedle}{target.id}\"");

            // ── 通畅对照（防"把正常攻击也一起掐掉"）：**同距离档**、只是把墙换成通路 ──
            var a2 = target.Grid();
            var open = Vector2Int.zero;
            var foundOpen = false;
            for (var dx = -6; dx <= 6 && !foundOpen; dx++)
            {
                for (var dy = -6; dy <= 6; dy++)
                {
                    var b = new Vector2Int(a2.x + dx, a2.y + dy);
                    if (!_ctx.Map.InBounds(b) || !_ctx.Map.Walkable(b)) continue;
                    var d = Iso.GridDistanceEuclidean(a2, b);
                    if (d < lo || d > hi) continue;
                    if (!CombatModule.AttackLineClear(a2, b)) continue;
                    open = b;
                    foundOpen = true;
                    break;
                }
            }
            Check("对照用例：找到「射程内 **且** 线段通畅」的玩家格", foundOpen,
                foundOpen ? $"怪格 {a2} ← 玩家格 {open}（格距 {Iso.GridDistanceEuclidean(a2, open):0.00} ∈ [{lo:0.00},{hi:0.00}]）"
                          : $"怪格 {a2} 周围 [{lo:0.00},{hi:0.00}] 档里没有线段通畅的可走格");
            if (!foundOpen) return;

            _player.SetGrid(open, Iso.DirectionTo(open, a2));
            var sfx0 = SfxCount(attackKey);
            TickSim(MonsterTuning.AttackIntervalSeconds * 2f);
            var swingsOpen = SfxCount(attackKey) - sfx0;
            Console.WriteLine($"  对照（同距离、无墙）：玩家格 {open}，怪在窗口 "
                + $"{MonsterTuning.AttackIntervalSeconds * 2f:0.00}s 内出手 {swingsOpen} 次");
            Check("★ 对照：线段通畅时同一只怪**会**正常出手（防把正常攻击一起掐掉）",
                swingsOpen > 0, $"出手 {swingsOpen} 次（阈值 = > 0）");

            // ── 20.3 近战对照：新增的线段自检**不许**把近战正常出手掐掉 ──────────────
            //   （20.0 已量出近战触达距离上 `LineClear` 恒通 ⇒ 新自检对近战恒为 true）
            var melee = PickMonster(x => x.ai == MonsterAI.Melee && x.alive);
            Check("20.3 近战对照：有活着的近战怪", melee != null, melee != null ? $"m#{melee.id} {melee.name}" : "none");
            if (melee == null) return;

            var mg = melee.Grid();
            var adj = Vector2Int.zero;
            var foundAdj = false;
            for (var i = 0; i < n8.Length && !foundAdj; i++)
            {
                var b = new Vector2Int(mg.x + n8[i].x, mg.y + n8[i].y);
                if (!_ctx.Map.InBounds(b) || !_ctx.Map.Walkable(b)) continue;
                adj = b;
                foundAdj = true;
            }
            Check("20.3 近战对照：怪旁边有可走格给玩家站", foundAdj, foundAdj ? $"怪格 {mg} ← 玩家格 {adj}" : $"怪格 {mg}");
            if (!foundAdj) return;

            _player.SetGrid(adj, Iso.DirectionTo(adj, mg));
            var meleeKey = MonsterSfx.AttackOf(melee) ?? SfxKeys.MonsterAttack;
            var meleeSfx0 = SfxCount(meleeKey);
            TickSim(MonsterTuning.AttackIntervalSeconds * 2f);
            var meleeSwings = SfxCount(meleeKey) - meleeSfx0;
            Console.WriteLine($"  近战对照（贴身、无墙）：玩家格 {adj}，怪在窗口 "
                + $"{MonsterTuning.AttackIntervalSeconds * 2f:0.00}s 内出手 {meleeSwings} 次");
            Check("★ 20.3 对照：近战贴身（线段必通）**照常出手** —— 新自检没有把近战掐掉",
                meleeSwings > 0, $"出手 {meleeSwings} 次（阈值 = > 0）");
            Console.WriteLine();
        }

        private static int SfxCount(string key)
        {
            var needle = "sfxAt:" + key;
            var n = 0;
            for (var i = 0; i < _audio.Calls.Count; i++)
            {
                if (string.Equals(_audio.Calls[i], needle, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────");
            Console.WriteLine("▶ " + title);
            Console.WriteLine("────────────────────────────────────────────────────────────");
        }

        /// <summary>断言（参数顺序 = 断言内容 / 是否通过 / 证据详情）。</summary>
        private static void Check(string what, bool ok, string detail)
        {
            Console.WriteLine($"    {(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
            if (!ok) _fail++;
        }

        private static void Run(Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine($"❌ {step.Method.Name} 抛异常：{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    /// <summary>`MonsterState` 没有 `Grid()`，这是宿主侧的便捷扩展（避免到处写 new Vector2Int）。</summary>
    internal static class MonsterStateExt
    {
        public static Vector2Int Grid(this MonsterState s)
        {
            return new Vector2Int(s.gridX, s.gridY);
        }
    }

    /// <summary>宿主用的场景管理器替身（本宿主不跑 Flow）。</summary>
    internal sealed class FakeScene : CloverEngine.ISceneManager
    {
        private readonly List<Action<string>> _loaded = new List<Action<string>>();

        public string CurrentScene { get; private set; }
        public void Load(string sceneName, Action<float> progress = null, Action onDone = null)
        {
            progress?.Invoke(1f);
            CurrentScene = sceneName;
            for (var i = 0; i < _loaded.Count; i++) _loaded[i]?.Invoke(sceneName);   // 与引擎同序：handler 先于 onDone
            onDone?.Invoke();
        }
        public void Unload(string sceneName, Action onDone = null) { onDone?.Invoke(); }
        public void OnSceneLoaded(Action<string> handler) => _loaded.Add(handler);
        public void OnSceneUnloaded(Action<string> handler) { }
    }
}
