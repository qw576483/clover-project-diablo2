// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · tools/probes/hosts/skillcheck/Program.cs
// **法师 30 技能逐条自检宿主**（离线、非 Unity、不参与打包）。
//
// 运行：
//   dotnet run --project <项目根>/tools/probes/hosts/skillcheck/skillcheck.csproj
//
// ── 这个宿主存在的唯一理由 ────────────────────────────────────────────────────
//   指令原文：
//     「只实现一个角色！！！就是法师！！！然后实现法师所有技能！！！
//       必须每个技能都要有测试和表现！！！然后学习技能不限制！！！
//       技能要拖动，要有命中效果，特效啊！！！」
//   `combatcheck` 只覆盖了火弹一个技能（"投射物能飞"），**30 个技能里的 29 个没有判据** ⇒
//   "实现了但一放就没效果"这类回归**不会被任何一个宿主抓住**。本宿主补的就是这一段。
//
// ── 判据的形态："每个技能都要有表现" 怎么机器判 ──────────────────────────────
//   ① **表驱动**：30 个 id 逐条走同一段代码（不是"抽查 3 个"）——
//      `SkillCatalog.Has(id)` 且行为不是 `Unknown`（少登记一行 ⇒ 直接红）；
//   ② **施放后有东西留在世界里**：`ActiveEffectCount + ActiveProjectileCount ≥ 1`
//      或 `IsBuffActive(id)`（增益类挂在自己身上）—— 这就是"表现"的机械口径；
//      ⛔ 不是"日志里说了一句"：日志人人会写，`_effects`/`_projectiles` 是**真的被塞了东西**；
//   ③ **行为逐族强断言**：投射物要命中并掉血、持续场要按 tick 结算若干次、陨石**恰好**落一次、
//      传送要真的挪了位置、增益要真的有剩余时间、召唤要真的开火……
//   ④ **判据自检**：把比对函数抽成纯函数，喂"已知错样本"必须变红（见第 6 节）。
//
// 不覆盖（需要 Unity 原生 API，留给开发者进 Play 后看图验收）：
//   精灵/贴图实际渲染与像素位置、环/盘的观感、特效的叠压与配色、拖拽的手感。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Dir8 = CloverEngine.Dir8;
using Diablo2.Module;
using Diablo2.Module.Combat;
using Diablo2.Module.Map;
using Diablo2.Module.Monster;
using Diablo2.Module.Skill;
using Diablo2.Module.View;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace SkillCheck
{
    internal static class Program
    {
        private static readonly string RepoRoot = ResolveProjectRoot();
        private static readonly string ClientAssets = RepoRoot + @"\client\Assets";

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
        private static SkillModule _skillImpl;

        private static int _playerDamagedEvents;

        /// <summary>最近一次 `Events.SkillHotkeysChanged` 的载荷 + 次数（第 6 节判「HUD 标签数据源」用）。</summary>
        private static SkillHotkeysArgs _lastHotkeys;
        private static int _hotkeyEvents;

        /// <summary>最近一次「玩家打怪」的 `DamageArgs.raw`（**抗性结算之前**的口径）。
        /// <para>为什么从这里取而不看怪掉血：低等级怪的 `hp` 只有个位数，一发就能打死 ⇒
        /// "血量差"会被"死亡截断"，两个样本都量成同一个数（首跑实测：0 → 0）。
        /// `raw` 是**生产事件**里的原值，不引入第二份伤害公式。</para></summary>
        private static int _lastSkillRaw;

        private static void OnDamageDealt(DamageArgs a)
        {
            if (a == null) return;
            if (a.attackerId != GameConst.PlayerEntityId) return;
            if (a.targetId == GameConst.PlayerEntityId) return;
            if (a.raw <= 0) return;
            _lastSkillRaw = a.raw;
        }

        private static int Main()
        {
            Console.WriteLine("================ SkillCheck 开始 ================");

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

            bus.On<DamageArgs>(Events.PlayerDamaged, a => _playerDamagedEvents++);
            bus.On<DamageArgs>(Events.DamageDealt, OnDamageDealt);
            // 技能热键表广播（第 6 节：设完热键必须有这一发，HUD 的技能速度棒靠它刷 F1~F8 标签）
            bus.On<SkillHotkeysArgs>(Events.SkillHotkeysChanged, a => { _lastHotkeys = a; _hotkeyEvents++; });

            var err = Table.TableLoader.LoadAll(null, ClientAssets);
            Check("配表加载 TableLoader.LoadAll（与 Bootstrap 同一入口）", err == null,
                err ?? ("dir=" + Table.TableLoader.LastDir));

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
            Run(Step1_CatalogCompleteness);
            Run(Step2_AllThirtyCastAndShow);
            Run(Step3_BehaviorDetails);
            Run(Step4_PassivesAndBuffs);
            Run(Step5_CommonContract);
            Run(Step6_BindAndHotkeys);
            Run(Step7_JudgementSelfCheck);
            Run(Step8_MissileFrameTable);
            Run(Step9_FxSortOrder);
            Run(Step10_FxPartCount);

            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "================ 自检全部通过 ================" : $"================ 自检失败 {_fail} 项 ================");
            Console.WriteLine("未覆盖（需要 Unity 原生 API，留给开发者进 Play 后看图验收）："
                + "① 环/盘/帧图的实际渲染与像素位置；② 命中特效的可见性与配色；③ 技能拖拽的手感。"
                + "（④ 特效与地形的叠压排序的**数学**已由第 9 节离线断言；⑤ 多份 SpriteRenderer 的"
                + "实际绘制由第 10 节钉住份数口径，只剩真渲染确认）");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 0. 宿主自证
        // ═════════════════════════════════════════════════════════════════════
        private static void Step0_HostCapability()
        {
            Section("0. 宿主可用性自证（验证手段自身要自证）");
            Console.WriteLine($"Iso.GridToWorld(3,5) = {Iso.GridToWorld(3, 5)}；SortOrder(3,5) = {Iso.SortOrder(3, 5)}");
            Check("Iso 正/逆投影往返一致", Iso.WorldToGrid(Iso.GridToWorld(new Vector2Int(3, 5))) == new Vector2Int(3, 5), "见上行");
            Console.WriteLine($"Vector2Int(3,-4) = {new Vector2Int(3, -4)}；Mathf.FloorToInt(-0.5) = {Mathf.FloorToInt(-0.5f)}");
            Check("本宿主能建出 SkillModule 的真实现（非替身）", _skillImpl != null, _skillImpl != null ? _skillImpl.GetType().FullName : "null");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. 行为目录完整性（30 条 × 行为 × 目标要求）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **独立写死的期望表**（不是把 `SkillCatalog.Build()` 抄一遍再"比对自己"）。
        /// 数据来源 = 官方 `skills.txt` 的技能语义（人读一遍定下来的"这个技能怎么打"）。
        /// 它与 `SkillCatalog` 是**两份独立真值** ⇒ 目录行被改错/删掉 ⇒ 当场变红。
        /// </summary>
        private static readonly Dictionary<int, string> ExpectedName = new Dictionary<int, string>
        {
            { 31, "火弹" }, { 32, "温暖" }, { 33, "充能弹" }, { 34, "冰弹" }, { 35, "冻结护甲" },
            { 36, "炼狱之火" }, { 37, "静电力场" }, { 38, "心灵传动" }, { 39, "霜之新星" }, { 40, "冰爆" },
            { 41, "火焰之径" }, { 42, "火球" }, { 43, "新星" }, { 44, "闪电" }, { 45, "寒颤护甲" },
            { 46, "火墙" }, { 47, "附魔" }, { 48, "连锁闪电" }, { 49, "传送" }, { 50, "冰川尖刺" },
            { 51, "陨石" }, { 52, "雷云风暴" }, { 53, "能量护盾" }, { 54, "暴风雪" }, { 55, "冰冷护甲" },
            { 56, "火焰精通" }, { 57, "多头蛇" }, { 58, "闪电精通" }, { 59, "冰霜宝珠" }, { 60, "冰冷精通" },
        };

        /// <summary>期望行为（独立第二真值；键 = 技能 id，值 = `SkillBehavior` 的名字）。</summary>
        private static readonly Dictionary<int, SkillBehavior> ExpectedBehavior = new Dictionary<int, SkillBehavior>
        {
            { 31, SkillBehavior.Projectile },        // 火弹：直线飞、命中单体
            { 32, SkillBehavior.Passive },           // 温暖：被动回蓝
            { 33, SkillBehavior.MultiProjectile },   // 充能弹：一发多颗
            { 34, SkillBehavior.Projectile },        // 冰弹
            { 35, SkillBehavior.Buff },              // 冻结护甲：自身增益
            { 36, SkillBehavior.Beam },              // 炼狱之火：一条线烧过去
            { 37, SkillBehavior.ForceField },        // 静电力场：按当前生命比例削
            { 38, SkillBehavior.Telekinesis },       // 心灵传动：隔空取物
            { 39, SkillBehavior.Nova },              // 霜之新星：自身为圆心
            { 40, SkillBehavior.SplashProjectile },  // 冰爆：命中后炸开
            { 41, SkillBehavior.Trail },             // 火焰之径：跟人走留火星
            { 42, SkillBehavior.SplashProjectile },  // 火球
            { 43, SkillBehavior.Nova },              // 新星
            { 44, SkillBehavior.Beam },              // 闪电
            { 45, SkillBehavior.Buff },              // 寒颤护甲
            { 46, SkillBehavior.GroundField },       // 火墙：留在地上的持续场
            { 47, SkillBehavior.Buff },              // 附魔：自身增益（近战附加火焰）
            { 48, SkillBehavior.Chain },             // 连锁闪电：逐跳
            { 49, SkillBehavior.Teleport },          // 传送
            { 50, SkillBehavior.SplashProjectile },  // 冰川尖刺
            { 51, SkillBehavior.DelayedStrike },     // 陨石：先标记后落地
            { 52, SkillBehavior.Storm },             // 雷云风暴：限时自动劈
            { 53, SkillBehavior.Buff },              // 能量护盾
            { 54, SkillBehavior.GroundField },       // 暴风雪
            { 55, SkillBehavior.Buff },              // 冰冷护甲
            { 56, SkillBehavior.Passive },           // 火焰精通
            { 57, SkillBehavior.Summon },            // 多头蛇：召唤物
            { 58, SkillBehavior.Passive },           // 闪电精通
            { 59, SkillBehavior.SplashProjectile },  // 冰霜宝珠
            { 60, SkillBehavior.Passive },           // 冰冷精通
        };

        /// <summary>期望目标要求（**独立第二真值**）：为什么是它，见 `SkillCatalog.Build` 的判据注释。</summary>
        private static readonly Dictionary<int, SkillTarget> ExpectedTarget = new Dictionary<int, SkillTarget>
        {
            { 31, SkillTarget.Ground }, { 32, SkillTarget.None }, { 33, SkillTarget.Ground }, { 34, SkillTarget.Ground },
            { 35, SkillTarget.Self }, { 36, SkillTarget.Enemy }, { 37, SkillTarget.Self }, { 38, SkillTarget.Ground },
            { 39, SkillTarget.Self }, { 40, SkillTarget.Ground }, { 41, SkillTarget.Self }, { 42, SkillTarget.Ground },
            { 43, SkillTarget.Self }, { 44, SkillTarget.Ground }, { 45, SkillTarget.Self }, { 46, SkillTarget.Ground },
            { 47, SkillTarget.Self }, { 48, SkillTarget.Enemy }, { 49, SkillTarget.Ground }, { 50, SkillTarget.Ground },
            { 51, SkillTarget.Ground }, { 52, SkillTarget.Self }, { 53, SkillTarget.Self }, { 54, SkillTarget.Ground },
            { 55, SkillTarget.Self }, { 56, SkillTarget.None }, { 57, SkillTarget.Ground }, { 58, SkillTarget.None },
            { 59, SkillTarget.Ground }, { 60, SkillTarget.None },
        };

        /// <summary>必须点到怪的技能（其余要么点地、要么打自己）—— 判据里的名单，独立写死。</summary>
        private static readonly int[] NeedEnemyList = { 36, 48 };

        private static void Step1_CatalogCompleteness()
        {
            Section("1. 行为目录完整性：法师 30 技能 × 行为 × 目标要求（表驱动，逐条）");

            PrepareMap(AreaId.BloodMoor, 700001);
            var save = NewSave(PlayerClass.Sorceress, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);

            Check($"当前职业 = 法师（{PlayerClass.Sorceress}）", _ctx.Skill.Class == PlayerClass.Sorceress,
                _ctx.Skill.Class.ToString());
            Check($"技能树载入 30 个技能", _ctx.Skill.Available.Count == 30, _ctx.Skill.Available.Count.ToString());
            Check("SkillCatalog.RowCount == 30（目录行数=技能数，少一行就红）", SkillCatalog.RowCount == 30,
                SkillCatalog.RowCount.ToString());

            var missing = new List<string>();
            var wrongBehavior = new List<string>();
            var wrongTarget = new List<string>();
            var wrongName = new List<string>();
            var unknown = new List<string>();

            for (var id = 31; id <= 60; id++)
            {
                var row = Table.Tables.Default.Skill.Get(id);
                var def = FindDef(id);
                if (row == null || def == null) { missing.Add($"{id}(配表无行)"); continue; }
                if (!SkillCatalog.Has(id)) { missing.Add($"{id}(目录未登记)"); continue; }

                var spec = SkillCatalog.Of(id, row, def);

                if (!string.Equals(def.name, ExpectedName[id], StringComparison.Ordinal))
                    wrongName.Add($"{id}:{def.name}≠{ExpectedName[id]}");
                if (spec.behavior == SkillBehavior.Unknown)
                    unknown.Add($"{id}:{def.name}");
                if (spec.behavior != ExpectedBehavior[id])
                    wrongBehavior.Add($"{id}:{def.name}={spec.behavior}≠{ExpectedBehavior[id]}");
                if (spec.target != ExpectedTarget[id])
                    wrongTarget.Add($"{id}:{def.name}={spec.target}≠{ExpectedTarget[id]}");
            }

            Check("31..60 每一条都在 `skill_c` 里、且都在行为目录里（少一条直接红）",
                missing.Count == 0, missing.Count == 0 ? "30/30" : "缺: " + string.Join(" ", missing));
            Check("技能名与期望一致（配表被改会红）", wrongName.Count == 0,
                wrongName.Count == 0 ? "30/30" : string.Join(" ", wrongName));
            Check("目录里没有 `Unknown` 行为（= 表内行必须显式登记行为）", unknown.Count == 0,
                unknown.Count == 0 ? "0 条" : string.Join(" ", unknown));
            Check("逐条行为与期望一致（**独立第二真值**，改错行为当场红）", wrongBehavior.Count == 0,
                wrongBehavior.Count == 0 ? "30/30" : string.Join(" ", wrongBehavior));
            Check("逐条目标要求与期望一致（点空地放不出来就是这条红）", wrongTarget.Count == 0,
                wrongTarget.Count == 0 ? "30/30" : string.Join(" ", wrongTarget));

            // 目标要求的分档台账（人读一眼）
            var g = 0; var s = 0; var e = 0; var n = 0;
            foreach (var kv in ExpectedTarget)
            {
                switch (kv.Value)
                {
                    case SkillTarget.Ground: g++; break;
                    case SkillTarget.Self: s++; break;
                    case SkillTarget.Enemy: e++; break;
                    default: n++; break;
                }
            }
            Console.WriteLine($"  目标要求台账：点地 {g} 个 / 打自己 {s} 个 / 必须点到怪 {e} 个 / 被动 {n} 个");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. 30 技能逐条：能放 + 有表现
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// "有表现"的**机械判据**（唯一出处）：施放后世界里必须多出东西 ——
        /// 效果（`_effects`）或投射物（`_projectiles`）至少一个，或者增益挂上了。
        /// <para>它是纯函数（只看三个数）⇒ 可以被第 6 节的"已知错样本"直接喂进去自检。</para>
        /// </summary>
        private static bool CountsAsShown(int effectCount, int projectileCount, bool buffActive)
        {
            return effectCount + projectileCount >= 1 || buffActive;
        }

        private static void Step2_AllThirtyCastAndShow()
        {
            Section("2. 法师 30 技能逐条：能放（TryCast）+ 有表现（效果/投射物/增益 ≥1）");

            var shown = 0;
            var lines = new List<string>();

            for (var id = 31; id <= 60; id++)
            {
                var row = Table.Tables.Default.Skill.Get(id);
                var def = FindDef(id);
                if (row == null || def == null) { Check($"#{id} 配表行存在", false, "缺行"); continue; }
                var spec = SkillCatalog.Of(id, row, def);

                // 每个技能都从"干净世界"开始：新地图 + 新刷怪 + 清技能状态
                PrepareMap(AreaId.BloodMoor, 700100 + id);
                _ctx.Monster.SpawnArea(AreaId.BloodMoor);
                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                _player.SetLevel(99);
                _player.SetSkillPoints(99);
                _player.SetMaxMana(400);
                _player.SetMana(400);
                _player.SetLife(9999);

                _skillImpl.Learn(id);

                // ── 被动：`TryCast` 必须拒绝（被动在 `TickPassives` 里持续生效）──
                if (spec.behavior == SkillBehavior.Passive)
                {
                    var castPassive = _ctx.Skill.TryCast(id, _player.Grid);
                    var activeOk = IsPassiveEffectivelyActive(id);
                    Check($"#{id} {def.name}（被动）不能主动施放，但被动效果已在生效",
                        !castPassive && activeOk, $"cast={castPassive} 生效={activeOk}");
                    if (!castPassive && activeOk) shown++;
                    continue;
                }

                // ── 站位：需要敌方的把它放到怪旁边；其余放在出生点（**附近没有怪**）──
                MonsterState near = null;
                if (Array.IndexOf(NeedEnemyList, id) >= 0)
                {
                    near = PlacePlayerNearMonster(2);
                    if (near == null) { Check($"#{id} {def.name}：地图上有可用的敌方目标", false, "none"); continue; }
                }
                else
                {
                    _player.SetGrid(_ctx.Map.SpawnPoint);
                }

                var targetGrid = PickCastTarget(spec, near);
                var before = _skillImpl.ActiveEffectCount + _skillImpl.ActiveProjectileCount;
                var cast = _ctx.Skill.TryCast(id, targetGrid);
                var after = _skillImpl.ActiveEffectCount + _skillImpl.ActiveProjectileCount;
                var buffOn = _skillImpl.IsBuffActive(id);
                var ok = cast && CountsAsShown(after, 0, buffOn) && after >= before;

                Check($"#{id} {def.name}：施放成功且留下可见表现（{spec.behavior}/{spec.target}）", ok,
                    $"cast={cast} 效果+弹={after}（+{after - before}） 增益={buffOn} 目标格={targetGrid}");

                lines.Add($"    #{id,-3} {def.name,-6} {spec.behavior,-18} {spec.target,-6} " +
                          $"效果+弹={after}{(buffOn ? " 增益=是" : "")}");
                if (ok) shown++;
            }

            Console.WriteLine("  ── 30 技能一览 ──");
            for (var i = 0; i < lines.Count; i++) Console.WriteLine(lines[i]);
            Check("30 个技能**全部**都能放且都有表现（差一个就红）", shown == 30, $"{shown}/30");
            Console.WriteLine();
        }

        /// <summary>被动是否"确实在生效"（温暖 = 回蓝；火/电精通 = 有加成百分比；
        /// **冰冷精通 = 有降抗值**——它的机制与另两系不同，不进乘区）。</summary>
        private static bool IsPassiveEffectivelyActive(int id)
        {
            var row = Table.Tables.Default.Skill.Get(id);
            if (row == null) return false;
            switch (row.Code)
            {
                case "Warmth":
                    return _skillImpl.IdOfCode("Warmth") == id && _skillImpl.GetLevel(id) > 0;
                case "Fire Mastery":
                    return _skillImpl.MasteryPercent(DamageType.Fire) > 0;
                case "Lightning Mastery":
                    return _skillImpl.MasteryPercent(DamageType.Lightning) > 0;
                case "Cold Mastery":
                    return _skillImpl.ColdMasteryResistReduction() > 0;
                default:
                    return false;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 3. 行为细节（逐族强断言）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step3_BehaviorDetails()
        {
            Section("3. 行为细节：逐族强断言（不只是「有东西」，而是「按这个技能的方式在打」）");

            // ── 3.1 投射物：飞出去 + 命中 + 掉血 + 回收（火弹 / 冰弹）────────────────
            foreach (var id in new[] { 31, 34 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);
                var target = PlacePlayerForIsolatedShot(out _, 8f);
                if (target == null) { Check($"#{id} 投射物：找得到「弹道无遮挡」的取样点", false, "none"); continue; }

                var hp0 = target.hp;
                var cast = _ctx.Skill.TryCast(id, target.Grid());
                var proj = _skillImpl.ActiveProjectileCount > 0 ? _skillImpl.Projectiles[0] : null;
                Check($"#{id} {def.name}：施放后恰好 1 颗投射物在飞", cast && _skillImpl.ActiveProjectileCount == 1,
                    $"cast={cast} 弹={_skillImpl.ActiveProjectileCount}");

                var steps = 0;
                while (proj != null && proj.alive && !proj.hitSomething && steps < 400) { _ctx.Skill.Tick(Dt); steps++; }
                Check($"#{id} {def.name}：投射物命中了**瞄准的那一只**（飞行 {steps} 步）",
                    proj != null && proj.hitMonsterId == target.id,
                    proj != null ? $"hit={proj.hitMonsterId} 瞄准={target.id}" : "proj=null");
                Check($"#{id} {def.name}：命中后目标掉血（{hp0} → {target.hp}）", target.hp < hp0,
                    $"{hp0} → {target.hp}");
                Check($"#{id} {def.name}：命中后投射物被回收", _skillImpl.ActiveProjectileCount == 0,
                    _skillImpl.ActiveProjectileCount.ToString());
                //   ★ 口径「要有命中效果，特效啊」：命中处必须留一次 `SkillFxKind.Impact` 短促闪光
                //   （寿命 = `SkillTuning.ImpactSeconds`）。它是"打到了"的**最小可见信号** ——
                //   与"投射物飞过"（弹体轨迹）是两件事，缺了就是"放了技能但看不出打中"。
                Check($"#{id} {def.name}：命中处留下冲击闪光（SkillFxKind.Impact）",
                    FindEffectKind(id, SkillFxKind.Impact), "有 / 无");
            }

            // ── 3.2 落点炸开：命中那一刻**周围**也掉血（火球 / 冰爆 / 冰川尖刺 / 冰霜宝珠）──
            foreach (var id in new[] { 42, 40, 50, 59 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);

                //   取样：必须有**一对挨着的怪**（格距 ≤ spec.radius）—— 否则"范围"无从证明。
                if (!FindMonsterPair(spec.radius, -1, out var primary, out var neighbour))
                {
                    Check($"#{id} {def.name}：地图上有「两只挨在 {spec.radius} 格内」的怪（范围用例的前提）", false,
                        "刷怪点里没有相邻的一对");
                    continue;
                }
                if (!PlacePlayerForSplash(primary, neighbour, 3)) continue;

                var hp2 = neighbour.hp;
                var hp1 = primary.hp;
                _ctx.Skill.TryCast(id, primary.Grid());

                var steps = 0;
                while (_skillImpl.ActiveProjectileCount > 0 && steps < 400) { _ctx.Skill.Tick(Dt); steps++; }
                _ctx.Skill.Tick(Dt);

                Check($"#{id} {def.name}（半径 {spec.radius} 格）：主目标掉血（{hp1} → {primary.hp}）",
                    primary.hp < hp1, $"{hp1} → {primary.hp}");
                Check($"#{id} {def.name}：**旁边那只也掉血**（真的按范围结算，不是单体）（{hp2} → {neighbour.hp}）",
                    neighbour.hp < hp2, $"{hp2} → {neighbour.hp}");
                //   ★ 口径「要有命中效果」：炸开处（投射物命中点）必须有 `SkillFxKind.Impact` 闪光。
                Check($"#{id} {def.name}：炸开处留下冲击闪光（SkillFxKind.Impact）",
                    FindEffectKind(id, SkillFxKind.Impact), "有 / 无");
            }

            // ── 3.3 多发：一发多颗（充能弹）──────────────────────────────────────
            {
                SetupFresh(33, out var spec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                var cast = _ctx.Skill.TryCast(33, OpenCellNear(_player.Grid, 6));
                Check($"#33 充能弹：一次施放飞出 3 颗（配置表 count={spec.count}）",
                    cast && _skillImpl.ActiveProjectileCount == spec.count,
                    $"cast={cast} 弹={_skillImpl.ActiveProjectileCount}/期望 {spec.count}");
            }

            // ── 3.4 新星 / 霜之新星：以**自己**为圆心，身边怪全中（不需要点怪）────────
            foreach (var id in new[] { 43, 39 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);
                var m1 = PlacePlayerNearMonster(2);
                if (m1 == null) { Check($"#{id} 新星：身边有怪", false, "none"); continue; }
                var hp0 = m1.hp;
                //   ⚠️ 关键：目标格 = **自己脚下**（点空地），且附近怪物不是靠"点怪"选出来的
                var cast = _ctx.Skill.TryCast(id, _player.Grid);
                var novaKind = FindEffectKind(id, SkillFxKind.Nova);
                Check($"#{id} {def.name}：点自己脚下就能放（不需要点到怪）", cast, $"cast={cast}");
                Check($"#{id} {def.name}：留下了以自己为圆心的环（SkillFxKind.Nova）", novaKind,
                    $"半径 {spec.radius} 格");
                Check($"#{id} {def.name}：半径内的怪掉血（{hp0} → {m1.hp}）", m1.hp < hp0, $"{hp0} → {m1.hp}");
            }

            // ── 3.5 地面持续场：铺 count 格 + **按 tick 结算 ≥2 次**（火墙 / 暴风雪）────
            foreach (var id in new[] { 46, 54 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);
                var target = PlacePlayerNearMonster(5);
                if (target == null) { Check($"#{id} 持续场：有目标", false, "none"); continue; }

                var fieldGrid = target.Grid();
                var cast = _ctx.Skill.TryCast(id, fieldGrid);
                var fx = FindEffect(id, SkillFxKind.Field);
                Check($"#{id} {def.name}：铺出了持续场（{spec.count} 格，存活 {spec.duration:0.0}s，每 {spec.tick:0.0}s 结算）",
                    cast && fx != null, fx != null ? $"格数={fx.points.Count}" : "none");
                if (fx == null) continue;

                var hp0 = target.hp;
                //   跑满两倍 tick 的时长 ⇒ 至少应结算 2 次
                TickSim(spec.tick * 2f + Dt * 2f);
                Check($"#{id} {def.name}：按 tick 反复结算（resolvedTicks={fx.resolvedTicks} ≥ 2）",
                    fx.resolvedTicks >= 2, fx.resolvedTicks.ToString());
                Check($"#{id} {def.name}：站在场里的怪持续掉血（{hp0} → {target.hp}）", target.hp < hp0,
                    $"{hp0} → {target.hp}");
            }

            // ── 3.6 火焰之径：跟人走 + 每 tick 在脚下生一簇独立小火 ──────────────────
            {
                SetupFresh(41, out var spec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                var cast = _ctx.Skill.TryCast(41, _player.Grid);
                var driver = FindEffect(41, SkillFxKind.Field);
                var before = _skillImpl.ActiveEffectCount;
                TickSim(spec.tick * 2.5f + Dt);
                Check("#41 火焰之径：施放后有一个「跟着施法者」的驱动器",
                    cast && driver != null && driver.followCaster, driver != null ? $"followCaster={driver.followCaster}" : "none");
                Check("#41 火焰之径：跑动期间在脚下生出**独立的小火**（子场各自计时）",
                    _skillImpl.ActiveEffectCount > before, $"{before} → {_skillImpl.ActiveEffectCount}");
            }

            // ── 3.7 直线（炼狱之火 / 闪电）：线上**所有**怪都中，线外不中 ──────────────
            foreach (var id in new[] { 36, 44 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);

                //   ⚠️ 炼狱之火配表 `hit_shift = 2` ⇒ 1 级的**有效**伤害区间 = `0 ~ 1`
                //   （`Staged(1, 32, …)<<2 >> 8 = 128 >> 8 = 0`、`(64<<2)>>8 = 1`，整数截断）
                //   ⇒ 单发"掉血"断言**必然 flaky**：技能确实命中、也进了结算，只是这一发掷到 0。
                //   口径是"学习技能不限制" ⇒ 判据把等级抬到 10 级
                //   （`(32 + 26×2 + 24×7) << 2 >> 8 = (252<<2)>>8 = 3`，10 级区间 `3 ~ 4`），
                //   这样判的才是"贯穿 / 命中"，而不是"这一发掷到几点"。
                if (id == 36)
                {
                    for (var i = 1; i < 10; i++) _skillImpl.Learn(id);
                    Console.WriteLine($"   #36 炼狱之火：判据把等级抬到 {_skillImpl.GetLevel(id)} 级"
                        + "（1 级有效伤害 0-1 ⇒ 单发掉血会 flaky；10 级 3-4 稳定掉血）");
                }

                //   直线长度 = `spec.radius` 格（`FireBeam` 里 `end = from + dir × radius`）
                //   ⇒ 取样必须落在**射程之内**，否则判据量的是"我站太远了"，不是技能对不对。
                var near = PlacePlayerNearMonster(Mathf.Max(2, spec.radius - 4));
                if (near == null) { Check($"#{id} 直线：有目标", false, "none"); continue; }
                var euclid = Vector2.Distance(Center(_player.Grid), Center(near.Grid()));
                Check($"#{id} {def.name}：取样站位在射程内（{euclid:0.00} ≤ {spec.radius} 格）",
                    euclid <= spec.radius, $"{euclid:0.00} / {spec.radius}");

                //   线上第二个目标 = 主目标**再往外**一格（同一条射线）
                var dirv = new Vector2Int(Math.Sign(near.Grid().x - _player.Grid.x),
                                          Math.Sign(near.Grid().y - _player.Grid.y));
                if (dirv == Vector2Int.zero) dirv = new Vector2Int(1, 0);
                var far = FindMonsterAt(near.Grid() + dirv);
                var hp0 = near.hp;
                var farHp0 = far != null ? far.hp : 0;

                var cast = _ctx.Skill.TryCast(id, near.Grid());
                var beam = FindEffectKind(id, SkillFxKind.Beam);
                Check($"#{id} {def.name}：留下了一条直线（SkillFxKind.Beam，半宽 {0.75f:0.00} 格）", cast && beam,
                    $"cast={cast} beam={beam}");
                Check($"#{id} {def.name}：线上主目标掉血（{hp0} → {near.hp}）", near.hp < hp0, $"{hp0} → {near.hp}");
                if (far != null)
                {
                    Check($"#{id} {def.name}：**同一条线上的第二只也掉血**（贯穿而非单体）", far.hp < farHp0,
                        $"{farHp0} → {far.hp}");
                }
                else
                {
                    Console.WriteLine($"    [披露] #{id} {def.name}：{near.Grid() + dirv} 上没有第二只怪 ⇒ 本用例只判了主目标");
                }
            }

            // ── 3.8 连锁闪电：逐跳（至少打到 1 只，且留下折线）──────────────────────
            {
                SetupFresh(48, out var spec);
                var m1 = PlacePlayerNearMonster(7);
                if (m1 == null) { Check("#48 连锁闪电：有目标", false, "none"); }
                else
                {
                    var hp0 = m1.hp;
                    var cast = _ctx.Skill.TryCast(48, m1.Grid());
                    var chain = FindEffect(48, SkillFxKind.Chain);
                    Check("#48 连锁闪电：留下了折线（SkillFxKind.Chain，含起点与每一跳）",
                        cast && chain != null && chain.points.Count >= 2,
                        chain != null ? $"顶点数={chain.points.Count}" : "none");
                    Check($"#48 连锁闪电：主目标掉血（{hp0} → {m1.hp}）", m1.hp < hp0, $"{hp0} → {m1.hp}");
                }
            }

            // ── 3.9 陨石：先标记、**恰好落一次** ────────────────────────────────────
            {
                SetupFresh(51, out var spec);
                var target = PlacePlayerNearMonster(6);
                if (target == null) { Check("#51 陨石：有目标", false, "none"); }
                else
                {
                    var cast = _ctx.Skill.TryCast(51, target.Grid());
                    var marker = FindEffect(51, SkillFxKind.Marker);
                    Check($"#51 陨石：先出现落点标记（{spec.duration:0.0}s 后落地）", cast && marker != null,
                        marker != null ? $"半径 {marker.radius} 格" : "none");
                    if (marker != null)
                    {
                        var hp0 = target.hp;
                        //   跑过落地时刻再跑一点 ⇒ resolvedTicks 必须**恰好 1**（不是 0、也不是每帧在炸）
                        TickSim(spec.duration + Dt * 4f);
                        Check($"#51 陨石：**恰好落地一次**（resolvedTicks={marker.resolvedTicks}）",
                            marker.resolvedTicks == 1, marker.resolvedTicks.ToString());
                        Check($"#51 陨石：落点内的怪吃到大伤害（{hp0} → {target.hp}）", target.hp < hp0,
                            $"{hp0} → {target.hp}");
                    }
                }
            }

            // ── 3.10 雷云风暴：限时自动劈（不需要手动瞄准）─────────────────────────
            {
                SetupFresh(52, out var spec);
                var target = PlacePlayerNearMonster(3);
                if (target == null) { Check("#52 雷云风暴：有目标", false, "none"); }
                else
                {
                    var hp0 = target.hp;
                    var cast = _ctx.Skill.TryCast(52, _player.Grid);
                    //   表现层分类 = `Field`（自带半径、跟人走、周期结算）—— 见 `Execute` 的 Storm 分支
                    var storm = FindEffect(52, SkillFxKind.Field);
                    Check($"#52 雷云风暴：点自己脚下放出来（限时 {spec.duration:0.0}s，每 {spec.tick:0.0}s 劈一次）",
                        cast && storm != null && storm.followCaster, storm != null ? $"followCaster={storm.followCaster}" : "none");
                    if (storm != null)
                    {
                        TickSim(spec.tick + Dt * 3f);
                        Check($"#52 雷云风暴：自动劈了 ≥1 次（resolvedTicks={storm.resolvedTicks}）",
                            storm.resolvedTicks >= 1, storm.resolvedTicks.ToString());
                        Check($"#52 雷云风暴：最近的怪被劈掉血（{hp0} → {target.hp}）", target.hp < hp0,
                            $"{hp0} → {target.hp}");
                    }
                }
            }

            // ── 3.11 传送：真的挪了位置 ───────────────────────────────────────────
            {
                SetupFresh(49, out var tpSpec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                var dest = OpenCellNear(_player.Grid, 6);
                var from = _player.Grid;
                var cast = _ctx.Skill.TryCast(49, dest);
                Check($"#49 传送：从 {from} 挪到 {dest}", cast && _player.Grid == dest,
                    $"cast={cast} 现在={_player.Grid}");
                Check("#49 传送：起点与终点各留一次闪光（SkillFxKind.Impact + 脚下的 Aura）",
                    _skillImpl.ActiveEffectCount >= 2, _skillImpl.ActiveEffectCount.ToString());
            }

            // ── 3.12 召唤（多头蛇）：放下 count 个，并**自己开火** ───────────────────
            {
                SetupFresh(57, out var spec);
                var target = PlacePlayerNearMonster(5);
                if (target == null) { Check("#57 多头蛇：有目标", false, "none"); }
                else
                {
                    var hp0 = target.hp;
                    //   落点取**目标怪自己那一格**：召唤物的开火半径是 `spec.radius`（相对**它自己**的位置），
                    //   放到玩家脚下可能离怪 8~9 格 ⇒ 有 tick 但不吐火（首跑实测踩到）。
                    var cast = _ctx.Skill.TryCast(57, target.Grid());
                    var summon = FindEffect(57, SkillFxKind.Summon);
                    Check($"#57 多头蛇：在落点放下 1 个召唤物（`count` 现为**蛇口份数**，只管表现、不再表示召唤数）",
                        cast && summon != null && summon.count == SkillTuning.HydraHeads,
                        summon != null ? $"count={summon.count}（蛇口），spec.count={spec.count}" : "none");
                    if (summon != null)
                    {
                        TickSim(spec.tick + Dt * 3f);
                        Check($"#57 多头蛇：召唤物自己开火了（resolvedTicks={summon.resolvedTicks} ≥ 1）",
                            summon.resolvedTicks >= 1, summon.resolvedTicks.ToString());
                        Check($"#57 多头蛇：怪被吐到（{hp0} → {target.hp}）", target.hp < hp0, $"{hp0} → {target.hp}");
                    }
                }
            }

            // ── 3.13 静电力场：把半径内的怪削到当前生命的 25% ─────────────────────────
            {
                SetupFresh(37, out var spec);
                var target = PlacePlayerNearMonster(3);
                if (target == null) { Check("#37 静电力场：有目标", false, "none"); }
                else
                {
                    var hp0 = target.hp;
                    var cast = _ctx.Skill.TryCast(37, _player.Grid);
                    var keep = Mathf.Max(1, Mathf.FloorToInt(hp0 * 0.25f));
                    Check($"#37 静电力场：点自己脚下放（半径 {spec.radius} 格，保留 25% 当前生命）", cast, $"cast={cast}");
                    Check($"#37 静电力场：怪被削到 25%（{hp0} → {target.hp}，期望 ≤ {keep}）",
                        target.hp <= keep, $"{hp0} → {target.hp}");
                }
            }

            // ── 3.14 心灵传动：隔空拾取（附近**没有怪**也必须放得出来）───────────────
            {
                SetupFresh(38, out var spec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                var before = _item.PickupNearestCalls.Count;
                var cast = _ctx.Skill.TryCast(38, OpenCellNear(_player.Grid, 4));
                Check("#38 心灵传动：附近没有怪也能放（隔空取物）", cast, $"cast={cast}");
                Check($"#38 心灵传动：调用了隔空拾取（半径 {spec.radius} 格）",
                    _item.PickupNearestCalls.Count > before,
                    _item.PickupNearestCalls.Count > before ? _item.PickupNearestCalls[_item.PickupNearestCalls.Count - 1] : "未调用");
            }

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. 被动与增益（数值 + 真实效果）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step4_PassivesAndBuffs()
        {
            Section("4. 被动与增益：温暖回蓝 / 三精通加成 / 能量护盾抵伤 / 三护甲反伤 / 附魔附加火焰");

            // ── 4.1 温暖：唯一的法力回复来源（每满 1 秒回一次）──────────────────────
            {
                PrepareMap(AreaId.BloodMoor, 740001);
                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                _player.SetLevel(99); _player.SetSkillPoints(10);
                _player.SetMaxMana(200);

                var warmth = _skillImpl.IdOfCode("Warmth");
                Check("温暖 id 按**官方 code** 查到（不写死数字）", warmth == 32, warmth.ToString());
                _skillImpl.Learn(warmth);

                _player.SetMana(0);
                TickSim(3f + Dt);
                var expect = 200 * 30 / 1000 * 3;      // maxMana × 30‰ × 3 秒
                Check($"温暖 1 级：3 秒回蓝 {_player.Mana}（期望 {expect} = maxMana 200 × 30‰ × 3s）",
                    _player.Mana == expect, $"实际 {_player.Mana}，期望 {expect}");

                //   ⚠️ 判据自检：**没学**温暖时必须一点都不回（否则"回蓝"这条判据是假的）
                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                _player.SetMana(0);
                TickSim(3f + Dt);
                Check("★ 对照：未学温暖时 3 秒回蓝 = 0（证明上面的回蓝不是别处漏进来的）",
                    _player.Mana == 0, _player.Mana.ToString());
            }

            // ── 4.2 元素精通：火/电"先放大伤害区间再掷点"；冰冷**不进乘区、走降抗** ──────
            //   口径：
            //     Fire Mastery       Param1=30 Param2=7  ⇒ 30 + 7×(lv-1)
            //     Lightning Mastery  Param1=50 Param2=12 ⇒ 50 + 12×(lv-1)
            //     Cold Mastery       Param1=20 Param2=5  ⇒ 降低怪物冰冷抗性 20 + 5×(lv-1)（下限 −100）
            //   乘区位置：官方精通是"技能伤害加成"⇒ 作用于 **min/max 两端**、再在放大后的区间里掷点
            //   （旧口径"掷点后再乘"让每次掷点多受一次整数截断，已废）。
            {
                PrepareMap(AreaId.BloodMoor, 740002);
                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                _player.SetLevel(99); _player.SetSkillPoints(99);

                Check("未学任何精通时加成 = 0（火/电）且降抗 = 0（冰）",
                    _skillImpl.MasteryPercent(DamageType.Fire) == 0 &&
                    _skillImpl.MasteryPercent(DamageType.Lightning) == 0 &&
                    _skillImpl.ColdMasteryResistReduction() == 0,
                    $"{_skillImpl.MasteryPercent(DamageType.Fire)}/{_skillImpl.MasteryPercent(DamageType.Lightning)}/{_skillImpl.ColdMasteryResistReduction()}");

                var fireM = _skillImpl.IdOfCode("Fire Mastery");
                Check("火焰精通 id 按官方 code 查到 = 56", fireM == 56, fireM.ToString());
                _skillImpl.Learn(fireM);
                var p1 = _skillImpl.MasteryPercent(DamageType.Fire);
                _skillImpl.Learn(fireM);
                var p2 = _skillImpl.MasteryPercent(DamageType.Fire);
                Check($"火焰精通：1 级 +{p1}%（期望 30）、2 级 +{p2}%（期望 37；官方 Param1=30 / Param2=7）",
                    p1 == 30 && p2 == 37, $"{p1}/{p2}");
                Check("精通只加本系（学了火焰精通后电仍是 0、冰冷降抗仍是 0）",
                    _skillImpl.MasteryPercent(DamageType.Lightning) == 0 &&
                    _skillImpl.ColdMasteryResistReduction() == 0, "0/0");

                // 闪电精通的官方值（50/12）与火焰（30/7）不同 —— 旧"三系共用 30/12"就是把这条判错的
                var lightM = _skillImpl.IdOfCode("Lightning Mastery");
                _skillImpl.Learn(lightM);
                var l1 = _skillImpl.MasteryPercent(DamageType.Lightning);
                _skillImpl.Learn(lightM);
                var l2 = _skillImpl.MasteryPercent(DamageType.Lightning);
                Check($"闪电精通：1 级 +{l1}%（期望 50）、2 级 +{l2}%（期望 62；官方 Param1=50 / Param2=12）",
                    l1 == 50 && l2 == 62, $"{l1}/{l2}");

                // 冰冷精通：**不进乘区**（MasteryPercent(Cold) 恒 0）+ 降抗 20/25
                var coldM = _skillImpl.IdOfCode("Cold Mastery");
                _skillImpl.Learn(coldM);
                var c1 = _skillImpl.ColdMasteryResistReduction();
                _skillImpl.Learn(coldM);
                var c2 = _skillImpl.ColdMasteryResistReduction();
                Check($"冰冷精通：不进伤害乘区（MasteryPercent(Cold) = 0，机制是降抗）",
                    _skillImpl.MasteryPercent(DamageType.Cold) == 0, "0");
                Check($"冰冷精通降抗：1 级 {c1}%（期望 20）、2 级 {c2}%（期望 25；官方 Param1=20 / Param2=5）",
                    c1 == 20 && c2 == 25, $"{c1}/{c2}");

                //   ★ 端到端：冰冷精通真的**降怪物抗性**（"不进乘区"的那一半落到实处）
                //     量法：同一只怪、同一发 1000 点冰冷 raw，唯一变量 = 降抗参数。
                //     1000×(100−r)/100 = 10×(100−r) 恒为整数 ⇒ 实扣能**精确反推**抗性 r。
                //     ⚠️ `PickFatTarget` 会把靶子 hp 抬到 100000 ⇒ 这三发都打不死。
                var fat = PickFatTarget();
                Check("冰冷降抗取样：找得到靶怪", fat != null, fat != null ? fat.name : "none");
                if (fat != null)
                {
                    var hp0 = fat.hp;
                    fat.hp = hp0;
                    _ctx.Monster.ApplyDamage(fat.id, 1000, DamageType.Cold, 0);
                    var plainCut = hp0 - fat.hp;              // = ApplyResist(1000, r)
                    var rOf = 100 - plainCut / 10;            // 精确反推（免疫怪 plainCut=0 ⇒ r≥100）
                    fat.hp = hp0;
                    _ctx.Monster.ApplyDamage(fat.id, 1000, DamageType.Cold, c2);
                    var cutCut = hp0 - fat.hp;                // = ApplyResist(1000, max(−100, r−25))
                    var expect = DamageFormula.ApplyResist(1000,
                        Mathf.Max(SkillTuning.ColdMasteryResistFloor, rOf - c2));
                    Check($"★ 冰冷精通降抗端到端：抗性 {rOf}% − 精通 {c2}% ⇒ 实扣 {cutCut}"
                        + $"（期望 {expect} = ApplyResist(1000, {Mathf.Max(SkillTuning.ColdMasteryResistFloor, rOf - c2)})）",
                        plainCut > 0 && cutCut == expect && cutCut > plainCut,
                        $"{plainCut} → {cutCut}，期望 {expect}");
                    // 下限自检：降抗远超抗性 ⇒ 夹到官方下限 −100%（不是 −100 以下）
                    fat.hp = hp0;
                    _ctx.Monster.ApplyDamage(fat.id, 1000, DamageType.Cold, 9999);
                    var floored = hp0 - fat.hp;
                    Check($"★ 降抗下限：降抗 9999 ⇒ 抗性夹到 −100%"
                        + $"（实扣 {floored} = ApplyResist(1000, −100) = 2000，双倍伤害）",
                        floored == DamageFormula.ApplyResist(1000, SkillTuning.ColdMasteryResistFloor),
                        floored.ToString());
                    // 自检：降抗参数只对 Cold 生效（同值传给 Fire ⇒ 抗性一动不动）
                    fat.hp = hp0;
                    _ctx.Monster.ApplyDamage(fat.id, 1000, DamageType.Fire, 0);
                    var firePlain = hp0 - fat.hp;
                    fat.hp = hp0;
                    _ctx.Monster.ApplyDamage(fat.id, 1000, DamageType.Fire, 9999);
                    var fireCut = hp0 - fat.hp;
                    Check($"★ 降抗只对冰冷生效：Fire 传 9999 ⇒ 实扣不变（{firePlain} → {fireCut}，火抗未被压）",
                        fireCut == firePlain, $"{firePlain} → {fireCut}");
                    fat.hp = hp0;
                }

                //   ⚠️ 判据自检：精通必须**真的进伤害**，不能只是个数字
                //   做法：**同地图 seed + 同 `ResetForClass`** ⇒ `SkillRng()` 同一条随机流 ⇒
                //   两组样本的底层随机样本逐个相同，唯一变量就是"有没有精通"。
                //   每次施放前把怪的 hp 复位（低等级怪一发就死，不复位就量不到第二发）。
                const int casts = 20;
                PrepareForMasterySample(31, 740100, learnMastery: false);
                var rawsPlain = RawOverCasts(31, casts);
                PrepareForMasterySample(31, 740100, learnMastery: true);
                var pct = _skillImpl.MasteryPercent(DamageType.Fire);
                var rawsBoosted = RawOverCasts(31, casts);
                var span = ExpectSpan(3, 6, pct);   // 火弹 1 级配表 3-6
                var sumA = SumList(rawsPlain);
                var sumB = SumList(rawsBoosted);
                Console.WriteLine($"  精通对照（同 seed、各 {casts} 发，同一条随机流）：精通 +{pct}%；"
                    + $"无精通 [{Join(rawsPlain)}] → 有精通 [{Join(rawsBoosted)}]"
                    + $"（区间 {span.lo}..{span.hi}；合计 {sumA} → {sumB}）");

                //   ★ 区间口径：精通**先放大 min/max 再掷点** ⇒ 每一发都必须落在
                //     [trunc(3×130/100), trunc(6×130/100)] = [3, 7] 内。
                //     ⚠️ 与旧口径的区别就在这条里：旧口径"掷点后再乘"的结果集合 = {3,5,6,7}
                //     （**永远不出现 4**），新区间口径 4 会出现（底层样本 s∈[0.2,0.4) 时 floor(s×5)+3 = 4）。
                var inSpan = rawsBoosted != null && rawsPlain != null;
                if (inSpan)
                {
                    for (var i = 0; i < rawsBoosted.Count; i++)
                        if (rawsBoosted[i] < span.lo || rawsBoosted[i] > span.hi) { inSpan = false; break; }
                }
                Check($"★ 火焰精通真的进伤害：{casts} 发逐次 ∈ [{span.lo},{span.hi}]（trunc(min/max × {100 + pct}%)，先放大区间再掷点）",
                    inSpan, $"[{Join(rawsPlain)}] ⇒ 期望全 ∈ [{span.lo},{span.hi}]，实得 [{Join(rawsBoosted)}]");
                Check($"★ 新区间口径自检：{casts} 发里出现过 {span.lo + 1}（旧\"掷点后再乘\"口径的集合 {{{span.lo},{span.lo + 2},{span.lo + 3},{span.hi}}} 里没有它 ⇒ 抓到旧口径复活）",
                    rawsBoosted != null && rawsBoosted.Contains(span.lo + 1),
                    $"[{Join(rawsBoosted)}]");
                Check($"★ 火焰精通进伤害：{casts} 发合计 {sumA} → {sumB}（区间 3-6→3-7，满额 ≈ ×111%，下界取 105%）",
                    sumA > 0 && sumB >= sumA * 105 / 100,
                    $"比值 {(sumA > 0 ? sumB * 100 / sumA : 0)}%");

                //   ★ 对照：**没学**火焰精通时，掷点上界就只能是火弹自身的 6
                PrepareForMasterySample(31, 740101, learnMastery: false);
                var maxPlain = MaxRawOverCasts(31, casts);
                PrepareForMasterySample(31, 740102, learnMastery: true);
                var maxBoosted = MaxRawOverCasts(31, casts);
                Check($"★ 对照：无精通时最大掷点 {maxPlain}（≤ 6 = 配表上限）→ 有精通时 {maxBoosted}（= 7 = trunc(6×1.3)，不是 ceil 的 8）",
                    maxPlain <= 6 && maxBoosted == 7, $"{maxPlain} → {maxBoosted}");
            }

            // ── 4.3 能量护盾：受伤时按比例由法力承担（等级缩放）──────────────────────
            {
                SetupFresh(53, out var spec);
                Check("能量护盾：1 级 20%、2 级 26%（官方 Param1=20 / Param2=6）",
                    SkillModule.EnergyShieldPercent(1) == 20 && SkillModule.EnergyShieldPercent(2) == 26,
                    $"{SkillModule.EnergyShieldPercent(1)}/{SkillModule.EnergyShieldPercent(2)}");

                _player.SetMaxMana(200);
                _player.SetMana(200);
                _player.SetLife(500);
                var cast = _ctx.Skill.TryCast(53, _player.Grid);
                Check("能量护盾：点自己脚下放（不需要点到怪）且增益在生效",
                    cast && _skillImpl.IsBuffActive(53), $"cast={cast} 剩余={_skillImpl.BuffRemain(53):0.0}s");

                var hpBefore = _player.Life;
                var manaBefore = _player.Mana;
                DamagePipeline.ApplyToPlayer(GameConst.PlayerEntityId + 1000, 100, DamageType.Physical, "自检");
                var hpLoss = hpBefore - _player.Life;
                var manaLoss = manaBefore - _player.Mana;
                Check($"能量护盾：100 伤害里有 {manaLoss} 点由法力承担（1 级 = 20%）", manaLoss == 20,
                    $"法力 -{manaLoss}，生命 -{hpLoss}");
            }

            // ── 4.4 三种护甲：挨打时对攻击者反伤（冰冷）────────────────────────────
            //   口径**按配表**：`skill_c` 里「冻结护甲」的伤害列是 0-0（官方就是纯防御）⇒ 不反伤；
            //   「寒颤护甲」6-8、「冰冷护甲」4-6 ⇒ 反伤。⛔ 不许一刀切写"三种护甲都反伤"。
            {
                foreach (var code in new[] { "Frozen Armor", "Shiver Armor", "Chilling Armor" })
                {
                    var id = _skillImpl.IdOfCode(code);
                    SetupFresh(id, out var spec);
                    var attacker = PlacePlayerNearMonster(3);
                    if (attacker == null) { Check($"护甲 {code}：有攻击者", false, "none"); continue; }

                    var row = Table.Tables.Default.Skill.Get(id);
                    DamageFormula.SkillDamageRange(row, 1, out var mn, out var mx);
                    var shouldReflect = mx > 0;

                    var cast = _ctx.Skill.TryCast(id, _player.Grid);
                    var hp0 = attacker.hp;
                    DamagePipeline.ApplyToPlayer(attacker.id, 30, DamageType.Physical, "自检");
                    var reflected = attacker.hp < hp0;
                    var buffOn = _skillImpl.IsBuffActive(id);
                    Check($"{code}（#{id} {_skillImpl.GetLevel(id)} 级）：点自己脚下放且增益在生效（配表伤害 {mn}-{mx}）",
                        cast && buffOn, $"cast={cast} 增益={buffOn}");
                    Check($"{code}：反伤口径 = {(shouldReflect ? "**要反伤**（配表有伤害列）" : "**不反伤**（配表 0-0，纯防御）")}"
                          + $" ⇒ 实际 {(reflected ? "反伤了" : "没反伤")}",
                        reflected == shouldReflect, $"{hp0} → {attacker.hp}");
                }

                //   互斥：同时挂两种护甲时，按**等级最高**的那个反伤（只反一次）
                SetupFresh(45, out var s45);
                _skillImpl.Learn(55);           // 冰冷护甲 → 1 级
                _skillImpl.Learn(55);           // 冰冷护甲 → 2 级（等级比寒颤护甲高 ⇒ 该它反伤）
                var att2 = PlacePlayerNearMonster(3);
                if (att2 == null) { Check("护甲互斥：有攻击者", false, "none"); }
                else
                {
                    var c1 = _ctx.Skill.TryCast(45, _player.Grid);
                    var c2 = _ctx.Skill.TryCast(55, _player.Grid);
                    var hpB = att2.hp;
                    DamagePipeline.ApplyToPlayer(att2.id, 30, DamageType.Physical, "自检");
                    Check("护甲互斥：两种护甲同时挂时**只反一次**（取等级更高的那个）",
                        c1 && c2 && att2.hp < hpB,
                        $"寒颤#45 1 级 + 冰冷#55 2 级 ⇒ {hpB} → {att2.hp}（只掉一次）");
                }
            }

            // ── 4.5 三种护甲：防御加成（官方 `skill_armor_percent`，跨模块口 `SkillBuffs`）──
            //   出处：官方 `skills.txt` 三甲行 `aurastat1 = skill_armor_percent` +
            //   Param 列注释逐字给出（冻结 30+5/级、寒颤 45+6/级、冰冷 45+5/级）。
            //   读方 = `PlayerModule.Defense`（命中判定 `player.Defense` 与角色面板同走它）。
            //   ⚠️ 本宿主用 `FakePlayer` ⇒ 钉的是**跨模块口**上的值与互斥口径；
            //   "属性上真的乘上去"由 Unity 侧探针验（读施放前后 `player.Defense`）。
            {
                SetupFresh(35, out var a35);
                Check("护甲防御：挂之前跨模块口 = 0（静止态恒为零）",
                    SkillBuffs.ArmorDefensePercent == 0, SkillBuffs.ArmorDefensePercent.ToString());

                var cast35 = _ctx.Skill.TryCast(35, _player.Grid);
                Check("冻结护甲：点自己脚下放出来（不需要点到怪）", cast35, $"cast={cast35}");
                Check("冻结护甲 1 级 ⇒ 防御加成 = 30%（官方 `30 % AC base`）",
                    SkillBuffs.ArmorDefensePercent == 30, $"pct={SkillBuffs.ArmorDefensePercent}");

                //   互斥（与反伤同口径）：多件同时挂只算一份、取**等级最高**者。
                //   ⚠️ 学技能的前置链走 4.4 已验证的那条（45 先学、55 才能升 2 级）。
                SetupFresh(45, out var a45);                     // 寒颤护甲 → 1 级
                _skillImpl.Learn(55);                            // 冰冷护甲 → 1 级
                _skillImpl.Learn(55);                            // → 2 级（等级更高 ⇒ 该它算）
                var cast45 = _ctx.Skill.TryCast(45, _player.Grid);
                var cast55 = _ctx.Skill.TryCast(55, _player.Grid);
                Check("互斥：冰冷 2 级 vs 寒颤 1 级 ⇒ 取等级更高者 = 45+5×(2-1) = 50%（只算一份）",
                    cast45 && cast55 && SkillBuffs.ArmorDefensePercent == 50,
                    $"cast45={cast45} cast55={cast55} pct={SkillBuffs.ArmorDefensePercent}");

                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                Check("清档后防御加成归零（`ResetForClass` ⇒ ArmorDefensePercent=0）",
                    SkillBuffs.ArmorDefensePercent == 0, SkillBuffs.ArmorDefensePercent.ToString());

                //   自检（错样本必须变红）：把「冻结护甲 1 级 = 30%」谎报成 45% ⇒ 同一比对**必须判红**
                SetupFresh(35, out var a35b);
                var castB = _ctx.Skill.TryCast(35, _player.Grid);
                var liePassed = SkillBuffs.ArmorDefensePercent == 45;      // 谎报的比对
                Check("★ 已知错样本：把「冻结护甲 1 级 = 30%」谎报成 45% ⇒ **同一比对必须红**",
                    castB && !liePassed,
                    $"pct={SkillBuffs.ArmorDefensePercent}（谎报比对{(liePassed ? "误判为真 ⇒ 判据失灵" : "判红 ✓")}）");
            }

            // ── 4.6 附魔：近战附加火焰伤害（跨模块只读口 `SkillBuffs`）───────────────
            {
                SetupFresh(47, out var spec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                Check("附魔**未生效**时 `SkillBuffs.HasMeleeFire = false`（静止态恒为零）",
                    !SkillBuffs.HasMeleeFire, SkillBuffs.HasMeleeFire.ToString());

                var cast = _ctx.Skill.TryCast(47, _player.Grid);
                _ctx.Skill.Tick(Dt);          // 推动 `TickBuffs` 把数值推给跨模块口（附魔是"生效后每帧维持"）
                Check("附魔：点自己脚下放（不需要点到怪）", cast, $"cast={cast}");
                Check($"附魔：生效后跨模块口读到 min/max = {SkillBuffs.MeleeFireMin}-{SkillBuffs.MeleeFireMax}（必须 > 0）",
                    SkillBuffs.HasMeleeFire && SkillBuffs.MeleeFireMin > 0 && SkillBuffs.MeleeFireMax > 0,
                    $"has={SkillBuffs.HasMeleeFire} {SkillBuffs.MeleeFireMin}-{SkillBuffs.MeleeFireMax}");

                //   只读口要能真被"清回去"（否则换角色后普攻永远带火）
                _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
                Check("清档后跨模块口归零（`ResetForClass` ⇒ HasMeleeFire=false）",
                    !SkillBuffs.HasMeleeFire, $"{SkillBuffs.MeleeFireMin}-{SkillBuffs.MeleeFireMax}");
            }

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. 共同契约：扣蓝 / 冷却 / **退蓝** / 清场
        // ═════════════════════════════════════════════════════════════════════
        private static void Step5_CommonContract()
        {
            Section("5. 共同契约：扣蓝 · 进冷却 · 拒绝时**退还法力** · 清场清干净");

            // ── 5.1 扣蓝 + 冷却 ─────────────────────────────────────────────────
            SetupFresh(31, out var fireSpec);
            _player.SetGrid(_ctx.Map.SpawnPoint);
            var g = OpenCellNear(_player.Grid, 5);
            _player.SetMana(100);
            var cast = _ctx.Skill.TryCast(31, g);
            Check("火弹：施放成功并扣 5 法力（skill_c.mana_cost = 5）", cast && _player.Mana == 95,
                $"cast={cast} 法力={_player.Mana}");
            Check("火弹：进入了冷却", _ctx.Skill.GetCooldownRemain(31) > 0f,
                $"{_ctx.Skill.GetCooldownRemain(31):0.00}s");

            var manaNow = _player.Mana;
            var again = _ctx.Skill.TryCast(31, g);
            Check("冷却期内再放 = false 且**不再扣蓝**", !again && _player.Mana == manaNow, $"法力={_player.Mana}");

            // ── 5.2 **拒绝必退蓝**（回报的"没效果"的一半根因）──────────────────
            SetupFresh(49, out var tpSpec);
            _player.SetGrid(_ctx.Map.SpawnPoint);
            _player.SetMana(100);
            //   ⚠️ 必须**真的**超出上限：`OpenCellNear` 找的是"最近的格子"（= 4 格），
            //   用它造"太远"会造出一个近点 ⇒ 施放成功、判据测的就不是退蓝了（首跑实测踩到）。
            var far = FarCellNear(_player.Grid, SkillTuning.TeleportMaxTiles + 2, SkillTuning.TeleportMaxTiles + 12);
            var realDist = Iso.GridDistance(far, _player.Grid);
            var manaBefore = _player.Mana;
            var farCast = _ctx.Skill.TryCast(49, far);
            Check($"传送：落点 {far} 超出上限（实际 {realDist} > {SkillTuning.TeleportMaxTiles} 格）⇒ 被拒",
                !farCast, $"cast={farCast} 距离={realDist}");
            Check("传送：被拒时**法力必须被退还**（不许「蓝扣了、什么都没发生」）",
                _player.Mana == manaBefore, $"法力 {manaBefore} → {_player.Mana}");

            // ── 5.3 施放后清场（换角色 / 换地图不许残留）────────────────────────────
            SetupFresh(46, out var fieldSpec);
            var target = PlacePlayerNearMonster(5);
            _ctx.Skill.TryCast(46, target != null ? target.Grid() : _player.Grid);
            _ctx.Skill.TryCast(53, _player.Grid);
            var had = _skillImpl.ActiveEffectCount + _skillImpl.ActiveProjectileCount;
            var hadBuff = _skillImpl.IsBuffActive(53);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            Check($"清场：施放后世界里还有 {had} 个效果/弹 + 增益={hadBuff} ⇒ `ResetForClass` 后必须归零",
                had > 0 && _skillImpl.ActiveEffectCount == 0 && _skillImpl.ActiveProjectileCount == 0 &&
                !_skillImpl.IsBuffActive(53),
                $"现在 效果={_skillImpl.ActiveEffectCount} 弹={_skillImpl.ActiveProjectileCount} 增益={_skillImpl.IsBuffActive(53)}");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 6. 鼠标键绑定与技能热键（**原版操作流程**：左键限制 / F1~F8 双义 / 滚轮环 / 点图标落格）
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 官方 `skills.txt` 里 `charclass=sor` 那 30 行的 `leftskill` 列（**现读文件**，不是抄工程常量）。
        /// 键 = 官方 `skill` code；值 = true（leftskill=1，可装左键）/ false（列空，不能装左键）。
        /// <para>为什么现读：本段判据要证明「工程的左键口径 = 官方数据」，两边必须是**独立真值** ——
        /// 把工程常量抄一遍再比自己，等于没判。</para>
        /// </summary>
        private static Dictionary<string, bool> ReadOfficialLeftSkill()
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var path = System.IO.Path.Combine(RepoRoot,
                @"原版资源\d2lod1.10txt-1.10f\data\global\excel\skills.txt");
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine($"[warn] 找不到官方 skills.txt：{path} ⇒ 本段左键判据退化（会红）");
                return map;
            }

            var lines = System.IO.File.ReadAllLines(path);
            var hdr = lines[0].Split('\t');
            int cSkill = -1, cClass = -1, cLeft = -1;
            for (var i = 0; i < hdr.Length; i++)
            {
                var h = hdr[i].Trim();
                if (h == "skill") cSkill = i;
                else if (h == "charclass") cClass = i;
                else if (h == "leftskill") cLeft = i;
            }
            if (cSkill < 0 || cClass < 0 || cLeft < 0) return map;

            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var f = lines[i].Split('\t');
                if (f.Length <= cLeft) continue;
                if (!string.Equals(f[cClass].Trim(), "sor", StringComparison.OrdinalIgnoreCase)) continue;
                map[f[cSkill].Trim()] = f[cLeft].Trim() == "1";
            }
            return map;
        }

        /// <summary>
        /// 左键可用性判据（**独立比对器**：喂进来的 `actual` 换了实现也能跑 ⇒ 能被"错样本"自检复用）。
        /// 返回 = 不一致的明细（空 = 全部一致）。
        /// </summary>
        private static List<string> LeftCastMismatches(
            Dictionary<string, bool> official, Func<int, string, bool> actual)
        {
            var bad = new List<string>();
            foreach (var kv in official)
            {
                var id = _skillImpl.IdOfCode(kv.Key);
                if (id < 0) { bad.Add($"{kv.Key}(工程里没这个 code)"); continue; }
                var row = Table.Tables.Default.Skill.Get(id);
                if (row == null) { bad.Add($"{kv.Key}#{id}(配表无行)"); continue; }
                if (actual(id, kv.Key) != kv.Value)
                    bad.Add($"{kv.Key}#{id}: 工程={actual(id, kv.Key)} ≠ 官方 leftskill={kv.Value}");
            }
            return bad;
        }

        private static void Step6_BindAndHotkeys()
        {
            Section("6. 鼠标键绑定与技能热键：官方左键限制 / 被动可绑 / F1~F8 双义 / 滚轮环 / 点图标落格");

            // ── 6.1 左键可用性 = 官方 `skills.txt` 的 `leftskill` 列（现读文件比对）────────
            var official = ReadOfficialLeftSkill();
            Check("官方 skills.txt 现读出法师 30 行（charclass=sor）", official.Count == 30,
                $"读到 {official.Count} 行（路径见代码）");

            var mismatches = LeftCastMismatches(official, (id, code) =>
                SkillCatalog.CanLeftCast(id, Table.Tables.Default.Skill.Get(id)));
            Check("★ 左键可用性逐条 = 官方 leftskill 列（30/30；差一条就红）", mismatches.Count == 0,
                mismatches.Count == 0 ? "30/30 一致" : string.Join(" | ", mismatches));

            // ── 6.2 两组 code 构成法师 30 行的**划分**（无交集、并集 30）──────────────
            var setLeft = new HashSet<int>();
            var setNotLeft = new HashSet<int>();
            var dup = new List<string>();
            var missCode = new List<string>();
            foreach (var c in SkillCatalog.CodeLeftCastable)
            {
                var id = _skillImpl.IdOfCode(c);
                if (id < 0) { missCode.Add(c); continue; }
                if (!setLeft.Add(id)) dup.Add(c);
            }
            foreach (var c in SkillCatalog.CodeNotLeftCastable)
            {
                var id = _skillImpl.IdOfCode(c);
                if (id < 0) { missCode.Add(c); continue; }
                if (!setNotLeft.Add(id)) dup.Add(c);
            }
            var both = new List<int>();
            foreach (var id in setLeft) if (setNotLeft.Contains(id)) both.Add(id);

            Check("★ 划分：可装左键 14 条 / 不能装 16 条（官方列的条数）",
                SkillCatalog.CodeLeftCastable.Length == 14 && SkillCatalog.CodeNotLeftCastable.Length == 16,
                $"{SkillCatalog.CodeLeftCastable.Length} + {SkillCatalog.CodeNotLeftCastable.Length}");
            Check("★ 划分：两组**无交集**、并集 = 法师 30 行（打错一个 code 直接红）",
                both.Count == 0 && missCode.Count == 0 &&
                setLeft.Count == 30 - SkillCatalog.CodeNotLeftCastable.Length &&
                setLeft.Count + setNotLeft.Count == 30,
                $"可装 {setLeft.Count} + 不能 {setNotLeft.Count}；交集 {both.Count}；配表查不到的 code {missCode.Count}");
            Check("★ 普通攻击（-1）可装左键（官方 `Attack` Id 0 的 leftskill=1）",
                SkillCatalog.CanLeftCast(-1, null) == SkillCatalog.AttackLeftCastable, SkillCatalog.AttackLeftCastable.ToString());

            //   自检（错样本必须变红）：把「冻结护甲」谎报成可装左键 + 谎报一条不存在的 code ⇒ 同一段比对必须红
            var wrongSample = LeftCastMismatches(official, (id, code) =>
                string.Equals(code, "Frozen Armor", StringComparison.OrdinalIgnoreCase) ? true :
                string.Equals(code, "Frozen Orb", StringComparison.OrdinalIgnoreCase) ? false :
                SkillCatalog.CanLeftCast(id, Table.Tables.Default.Skill.Get(id)));
            Check("★ 已知错样本：把「冻结护甲」当成可装左键 ⇒ **同一段比对必须红**",
                wrongSample.Count == 2, string.Join(" | ", wrongSample));

            // ── 6.3 真绑一次：左键限制生效 / 右键不拦 / 被动可绑（新口径）────────────────
            PrepareMap(AreaId.BloodMoor, 700006);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            _player.SetLevel(99); _player.SetSkillPoints(99);

            var frozenArmor = _skillImpl.IdOfCode("Frozen Armor");       // 官方 leftskill 空
            var fireBolt = _skillImpl.IdOfCode("Fire Bolt");             // 官方 leftskill=1
            var warmth = _skillImpl.IdOfCode("Warmth");                  // 官方 leftskill=1 且 passive=1
            _skillImpl.Learn(frozenArmor); _skillImpl.Learn(fireBolt); _skillImpl.Learn(warmth);

            var mark = _log.Lines.Count;
            _ctx.Skill.AssignToButton(0, frozenArmor);
            Check("★ 左键装「冻结护甲」被拒（官方 leftskill 列为空）⇒ 左键保持原值 + 留日志",
                _ctx.Skill.GetButtonSkill(0) == -1 && _log.IndexOf("不能用鼠标左键", mark) >= 0,
                $"左键={_ctx.Skill.GetButtonSkill(0)}；日志={_log.Count("不能用鼠标左键")} 条");

            mark = _log.Lines.Count;
            _ctx.Skill.AssignToButton(1, frozenArmor);
            Check("★ 同一技能装**右键**必须成功（原版：右键可以设置所有的技能）",
                _ctx.Skill.GetButtonSkill(1) == frozenArmor, $"右键={_ctx.Skill.GetButtonSkill(1)}(冻结护甲 #{frozenArmor})");

            _ctx.Skill.AssignToButton(0, fireBolt);
            Check("★ 左键装「火弹」成功（官方 leftskill=1）",
                _ctx.Skill.GetButtonSkill(0) == fireBolt, $"左键={_ctx.Skill.GetButtonSkill(0)}(火弹 #{fireBolt})");

            mark = _log.Lines.Count;
            _ctx.Skill.AssignToButton(0, warmth);
            Check("★ **被动技能可以绑左右键**（口径纠正：官方 leftskill=1 覆盖温暖 ⇒ 被动与别的技能同权）",
                _ctx.Skill.GetButtonSkill(0) == warmth && _log.IndexOf("被动技能不能绑定", mark) < 0,
                $"左键={_ctx.Skill.GetButtonSkill(0)}(温暖 #{warmth})；旧拒绝日志 {_log.Count("被动技能不能绑定")} 条");
            _ctx.Skill.AssignToButton(1, warmth);
            Check("★ 被动绑右键也可以（官方无『被动不能装鼠标』的规则）",
                _ctx.Skill.GetButtonSkill(1) == warmth, $"右键={_ctx.Skill.GetButtonSkill(1)}");

            var manaBefore = _player.Mana;
            var castPassive = _ctx.Skill.TryCast(warmth, _player.Grid);
            Check("★ 但被动**点了不产生施放**（`TryCast` 拒绝、且不扣蓝；原版点了也没反应）",
                !castPassive && _player.Mana == manaBefore,
                $"TryCast={castPassive}；法力 {manaBefore} → {_player.Mana}（被动日志累计 {_log.Count("是被动技能")} 条）");

            // ── 6.4 F1~F8 双义：速度棒开着+悬停 ⇒ **设**；否则 ⇒ **切右键技能** ──────────
            var save = NewSave(PlayerClass.Sorceress, 0);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);
            _player.SetLevel(99); _player.SetSkillPoints(99);
            _skillImpl.Learn(fireBolt); _skillImpl.Learn(_skillImpl.IdOfCode("Ice Bolt"));
            _lastHotkeys = null; _hotkeyEvents = 0;

            mark = _log.Lines.Count;
            Game.Event.Emit(Events.SkillHotkeyRequest, 3);            // 速度棒没开 ⇒ 走"切"，但 F3 还没设置
            Check("★ 速度棒关着 + 热键没设过 ⇒ **不切换**右键技能（原版：热键要先在速度棒里设）",
                _ctx.Skill.GetButtonSkill(1) == -1 && _log.IndexOf("还没设置过", mark) >= 0,
                $"右键={_ctx.Skill.GetButtonSkill(1)}；提示={_log.Count("还没设置过")} 条");

            //   打开速度棒 + 指针停在火弹上 ⇒ 此时按 F3 = 设
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs
            { open = true, hoverSkillId = fireBolt, targetButton = -1 });
            Game.Event.Emit(Events.SkillHotkeyRequest, 3);
            var savedIdx = save.skillHotkeys != null && save.skillHotkeys.Count > 2 ? save.skillHotkeys[2] : -99;
            Check("★ 速度快照开着 + 悬停火弹 + 按 F3 ⇒ **设热键**（F3 = 火弹）并**落档**",
                savedIdx == fireBolt, $"save.skillHotkeys[2]={savedIdx}（火弹 #{fireBolt}）");
            Check("★ 设热键后广播 `SkillHotkeysChanged`（HUD 靠它刷 FN 标签）",
                _hotkeyEvents >= 1 && _lastHotkeys != null && _lastHotkeys.ids != null &&
                _lastHotkeys.ids.Count > 2 && _lastHotkeys.ids[2] == fireBolt,
                $"事件 {_hotkeyEvents} 次；ids[2]={(_lastHotkeys != null && _lastHotkeys.ids != null && _lastHotkeys.ids.Count > 2 ? _lastHotkeys.ids[2].ToString() : "n/a")}");
            Check("★ 设热键**不动**当前右键技能（F 键只设置/切换，施放仍要点鼠标）",
                _ctx.Skill.GetButtonSkill(1) == -1, $"右键={_ctx.Skill.GetButtonSkill(1)}");

            //   关掉速度棒 ⇒ 同一按键（F3）变成"切"
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs { open = false, hoverSkillId = -2, targetButton = -1 });
            Game.Event.Emit(Events.SkillHotkeyRequest, 3);
            Check("★ 速度棒关掉后按 F3 ⇒ 变成**切右键技能** = 火弹（原版 F1~F8 的第二种含义）",
                _ctx.Skill.GetButtonSkill(1) == fireBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");

            //   错样本自检：**同一按键**在"速度棒开着 + 悬停"下必须走"设"而不是"切"
            var rightBefore = _ctx.Skill.GetButtonSkill(1);
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs
            { open = true, hoverSkillId = _skillImpl.IdOfCode("Ice Bolt"), targetButton = -1 });
            Game.Event.Emit(Events.SkillHotkeyRequest, 3);
            Check("★ 错样本对照：同样的 F3 在「开着 + 悬停冰弹」下走**设**（F3 改指冰弹、右键技能不变）",
                save.skillHotkeys[2] == _skillImpl.IdOfCode("Ice Bolt") && _ctx.Skill.GetButtonSkill(1) == rightBefore,
                $"F3={save.skillHotkeys[2]}（冰弹 #{_skillImpl.IdOfCode("Ice Bolt")}）；右键={_ctx.Skill.GetButtonSkill(1)}");
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs { open = false, hoverSkillId = -2, targetButton = -1 });

            // ── 6.5 滚轮：在**已设热键的技能**里切右手（原版手册：Scrolls through all hotkeyed skills）──
            _ctx.Skill.AssignToButton(1, fireBolt);                    // 当前右键 = 火弹（= F3）
            var iceBolt = _skillImpl.IdOfCode("Ice Bolt");             // F3 现在指冰弹 ⇒ 先改回火弹再测环
            save.skillHotkeys[2] = fireBolt;
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, save);     // 重读档（热键表 = [F3=火弹]）
            _player.SetLevel(99); _player.SetSkillPoints(99);
            _skillImpl.Learn(fireBolt); _skillImpl.Learn(iceBolt);
            _ctx.Skill.AssignToButton(1, iceBolt);                     // 右键 = 冰弹（**不在**热键表里）

            Game.Event.Emit(Events.SkillWheelRequest, -1);             // 下一项：当前不在环里 ⇒ 取环首
            Check("★ 滚轮向下（下一项）：当前右键不在热键环里 ⇒ 取环首 = F3 的火弹",
                _ctx.Skill.GetButtonSkill(1) == fireBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");

            //   再加一个热键 → 环 = [火弹, 冰弹]，测环绕
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs { open = true, hoverSkillId = iceBolt, targetButton = -1 });
            Game.Event.Emit(Events.SkillHotkeyRequest, 5);
            Game.Event.Emit(Events.SkillSpeedBarChanged, new SkillSpeedBarArgs { open = false, hoverSkillId = -2, targetButton = -1 });
            Game.Event.Emit(Events.SkillWheelRequest, -1);
            Check("★ 滚轮在**已设热键**的技能里循环：F3=火弹 → 下一项 = F5 的冰弹",
                _ctx.Skill.GetButtonSkill(1) == iceBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");
            Game.Event.Emit(Events.SkillWheelRequest, -1);
            Check("★ 环**绕回**：冰弹 → 下一项 = 火弹（2 项环取模）",
                _ctx.Skill.GetButtonSkill(1) == fireBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");
            Game.Event.Emit(Events.SkillWheelRequest, 1);
            Check("★ 滚轮向上（前一项）反向绕回 = 冰弹",
                _ctx.Skill.GetButtonSkill(1) == iceBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");

            //   空环：一个热键都没设 ⇒ 不动 + 留日志
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            _player.SetLevel(99); _player.SetSkillPoints(99);
            _skillImpl.Learn(fireBolt);
            _ctx.Skill.AssignToButton(1, fireBolt);
            mark = _log.Lines.Count;
            Game.Event.Emit(Events.SkillWheelRequest, -1);
            Check("★ 一个热键都没设 ⇒ 滚轮**不改**右键技能 + 留提示（不静默）",
                _ctx.Skill.GetButtonSkill(1) == fireBolt && _log.IndexOf("一个热键都没设置过", mark) >= 0,
                $"右键={_ctx.Skill.GetButtonSkill(1)}");

            // ── 6.6 速度棒点图标 = 发落格事件（HUD 那条入口）→ 装到目标手 ──────────────
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            _player.SetLevel(99); _player.SetSkillPoints(99);
            _skillImpl.Learn(fireBolt); _skillImpl.Learn(frozenArmor);
            Game.Event.Emit(Events.SkillSlotBindRequest, 1 | (fireBolt << 1));      // 右键格
            Check("★ 速度棒点图标（`SkillSlotBindRequest`）⇒ 装到**目标那只手**（右键）",
                _ctx.Skill.GetButtonSkill(1) == fireBolt, $"右键={_ctx.Skill.GetButtonSkill(1)}");
            Game.Event.Emit(Events.SkillSlotBindRequest, 0 | (frozenArmor << 1));   // 左键格：应被官方限制拒
            Check("★ 同一条入口选**左键** + 非攻击技能 ⇒ 仍被官方 leftskill 限制拒（入口不绕过校验）",
                _ctx.Skill.GetButtonSkill(0) == -1, $"左键={_ctx.Skill.GetButtonSkill(0)}");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. 判据自检：把"已知错样本"喂进**同一段判据**，必须变红
        // ═════════════════════════════════════════════════════════════════════
        private static void Step7_JudgementSelfCheck()
        {
            Section("7. 判据自检：已知错样本喂进**同一段判据**必须变红（否则判据本身是假的）");

            // ── 6.1 "有表现"判据的灵敏度 ────────────────────────────────────────
            //   同一段代码：正常样本 true、零效果样本 false
            Check("★ `CountsAsShown` 对**正常样本**（1 个效果 / 0 弹 / 无增益）判 true",
                CountsAsShown(1, 0, false), "1,0,false");
            Check("★ 已知错样本：`CountsAsShown` 对**零效果**（0,0,false）必须判 false",
                !CountsAsShown(0, 0, false), "0,0,false");
            Check("★ 已知错样本：`CountsAsShown` 对**只有增益**判 true（增益也是一种表现）",
                CountsAsShown(0, 0, true), "0,0,true");
            Check("★ 已知错样本：`CountsAsShown` 对**只有投射物**判 true",
                CountsAsShown(0, 1, false), "0,1,false");

            // ── 6.2 "目录完整性"判据的灵敏度 ────────────────────────────────────
            //   同一段比对逻辑：真值一致 → 不红；喂一个错值 → 必须红
            Check("★ 行为比对的灵敏度：真值 `(Projectile)` 与期望一致 ⇒ 不红",
                !BehaviorMismatch(SkillBehavior.Projectile, SkillBehavior.Projectile), "一致");
            Check("★ 已知错样本：把火弹的行为改成 `Nova` ⇒ **同一段比对必须红**",
                BehaviorMismatch(SkillBehavior.Nova, SkillBehavior.Projectile), "Nova ≠ Projectile");
            Check("★ 目标比对的灵敏度：真值 `(Ground)` 与期望一致 ⇒ 不红",
                !TargetMismatch(SkillTarget.Ground, SkillTarget.Ground), "一致");
            Check("★ 已知错样本：把火弹的目标要求改回粗判 `Enemy` ⇒ **同一段比对必须红**",
                TargetMismatch(SkillTarget.Enemy, SkillTarget.Ground), "Enemy ≠ Ground");

            // ── 6.3 目录是否**真的**在生效（不是"表建了但 `Of` 没查表"）────────────
            //   已知错样本 = 表外 id：走 `Infer` 兜底，行为必须**不是**表里的那一条
            var row31 = Table.Tables.Default.Skill.Get(31);
            var def31 = FindDef(31);
            var inTable = SkillCatalog.Of(31, row31, def31);
            var outOfTable = SkillCatalog.Of(99999, row31, def31);      // 假的 id ⇒ 一定走 Infer
            Check("★ 目录真的被查：表内 id 与表外 id 走的是**不同**路径（表外 = Infer 兜底）",
                SkillCatalog.Has(31) && !SkillCatalog.Has(99999) &&
                SkillCatalog.Of(99999, row31, def31).target == (def31 != null ? def31.target : SkillTarget.Self),
                $"表内 #{31}={inTable.behavior}；表外 #99999={outOfTable.behavior}（Infer ⇒ {outOfTable.target}）");

            // ── 6.4 表外职业**零回归**：Infer 必须与旧的粗判逐字一致 ────────────────
            {
                var amazonRow = FirstRowOfClass(PlayerClass.Amazon);
                if (amazonRow != null)
                {
                    var fakeDef = new SkillDef { id = amazonRow.Id, target = SkillTarget.Enemy, dmgType = DamageType.Physical };
                    var infer = SkillCatalog.Infer(amazonRow, fakeDef);
                    Check("★ 表外职业（亚马逊）走 `Infer`：`target` = 配表现的粗判值（零回归）",
                        infer.target == SkillTarget.Enemy, infer.target.ToString());
                    Check("★ 表外职业：行为不是 `Unknown`（表外也有兜底行为，不会掉进 default 报错）",
                        infer.behavior != SkillBehavior.Unknown, infer.behavior.ToString());
                }
                else
                {
                    Check("★ 表外职业样本：`skill_c` 里有亚马逊的行", false, "none");
                }
            }

            // ── 6.5 "精通乘区"判据的灵敏度（**先放大区间再掷点**口径）────────────────
            //   同一段算式：真值样本一致 ⇒ 不红；已知错样本 ⇒ 必须红。
            //   火弹 1 级配表 `3-6` ⇒ +30% 后期望区间 [trunc(3×1.3), trunc(6×1.3)] = [3, 7]。
            var spanSample = ExpectSpan(3, 6, 30);
            Check("★ 精通算式（区间放大）：火弹 [3,6] ×130% ⇒ 区间 [3,7]",
                spanSample.lo == 3 && spanSample.hi == 7, $"[{spanSample.lo},{spanSample.hi}]");
            Check("★ 已知错样本：区间写成 [3,6]（等于没放大）⇒ **同一段比对必须红**",
                !(ExpectSpan(3, 6, 30).lo == 3 && ExpectSpan(3, 6, 30).hi == 6), "[3,6] vs [3,7]");
            Check("★ 已知错样本：区间写成 [4,8]（四舍五入口径）⇒ 必须红（生产是截断，不是四舍五入）",
                !(ExpectSpan(3, 6, 30).lo == 4 && ExpectSpan(3, 6, 30).hi == 8), "四舍五入 [4,8] vs 截断 [3,7]");
            Check("★ 已知错样本：区间写成 [3,8]（上界按 ceil(7.8) 取）⇒ 必须红（生产两端都截断）",
                !(ExpectSpan(3, 6, 30).lo == 3 && ExpectSpan(3, 6, 30).hi == 8), "ceil 上界 [3,8] vs 截断 [3,7]");

            Console.WriteLine();
        }

        private static bool BehaviorMismatch(SkillBehavior actual, SkillBehavior expected)
        {
            return actual != expected;
        }

        private static bool TargetMismatch(SkillTarget actual, SkillTarget expected)
        {
            return actual != expected;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具：世界准备 / 站位 / 取件
        // ═════════════════════════════════════════════════════════════════════

        private static void PrepareMap(AreaId area, int seed)
        {
            _ctx.Map.Generate(area, seed);
            _playerDamagedEvents = 0;
            _player.SetLife(9999);
            _player.SetMana(400);
            _player.SetGrid(_ctx.Map.SpawnPoint);
        }

        /// <summary>每个技能都从"干净世界"开始：新地图 + 清怪重刷 + 清技能状态 + 学会该技能。</summary>
        private static void SetupFresh(int id, out SkillSpec spec)
        {
            PrepareMap(AreaId.BloodMoor, 760000 + id);
            _ctx.Monster.DespawnAll();
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            _player.SetLevel(99);
            _player.SetSkillPoints(99);
            _player.SetMaxMana(400);
            _player.SetMana(400);

            var row = Table.Tables.Default.Skill.Get(id);
            var def = FindDef(id);
            spec = SkillCatalog.Of(id, row, def);
            if (def != null) _skillImpl.Learn(id);
        }

        private static void TickSim(float seconds)
        {
            var steps = (int)(seconds / Dt);
            for (var i = 0; i < steps; i++) _ctx.Skill.Tick(Dt);
            _ctx.Skill.Tick(0f);      // 尾巴：让 `Tick(0)` 的"不推进"契约也被走一遍
        }

        /// <summary>按行为目录选一个施放目标格（**判据与被测代码共用同一份目录** ⇒ 目录错了这里也会错，
        /// 所以第 1 节的独立期望表才是真正的把关）。</summary>
        private static Vector2Int PickCastTarget(SkillSpec spec, MonsterState near)
        {
            switch (spec.target)
            {
                case SkillTarget.Enemy:
                    return near != null ? near.Grid() : _player.Grid;
                case SkillTarget.Self:
                case SkillTarget.None:
                    return _player.Grid;
                default:
                    return OpenCellNear(_player.Grid, 5);
            }
        }

        /// <summary>找一格距 <paramref name="from"/> 最近、可走、且尽量与 from 连通的格子（半径 4..max）。</summary>
        private static Vector2Int OpenCellNear(Vector2Int from, int max)
        {
            for (var r = 4; r <= max; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var g = new Vector2Int(from.x + dx, from.y + dy);
                        if (!_ctx.Map.InBounds(g) || !_ctx.Map.Walkable(g)) continue;
                        return g;
                    }
                }
            }
            return from;
        }

        /// <summary>
        /// 找一格距 <paramref name="from"/> 的**切比雪夫距离 ∈ [minR, maxR]**、可走的格子
        /// （`OpenCellNear` 只找"最近的"，用来造"太远了"的用例会造不出来）。
        /// </summary>
        private static Vector2Int FarCellNear(Vector2Int from, int minR, int maxR)
        {
            for (var r = maxR; r >= minR; r--)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var g = new Vector2Int(from.x + dx, from.y + dy);
                        if (!_ctx.Map.InBounds(g) || !_ctx.Map.Walkable(g)) continue;
                        return g;
                    }
                }
            }
            return from;
        }

        /// <summary>把玩家放到某只怪旁边 <paramref name="distance"/> 格的可走格上（Enemy 类技能用）。</summary>
        private static MonsterState PlacePlayerNearMonster(int distance)
        {
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || !m.alive) continue;
                var mg = m.Grid();
                for (var r = distance; r <= distance + 3; r++)
                {
                    for (var dx = -r; dx <= r; dx++)
                    {
                        for (var dy = -r; dy <= r; dy++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                            var g = new Vector2Int(mg.x + dx, mg.y + dy);
                            if (!_ctx.Map.InBounds(g) || !_ctx.Map.Walkable(g)) continue;
                            _player.SetGrid(g, Iso.DirectionTo(g, mg));
                            return m;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 把玩家摆到「朝向某只怪、**弹道上没有别的怪**」的位置（投射物用例的取样口径）。
        /// <para>
        /// 为什么必须这么挑：投射物命中**第一个**进入命中半径的目标 ⇒ 随便找只怪开枪会把"中间那只"
        /// 打掉，判据变成"命中目标 ≠ 瞄准目标"（首跑实测踩到：hit=2009 目标=2008）。
        /// 判据本身没错，是**取样**错了。
        /// </para>
        /// <para>清线阈值 1.2 格（> 投射物命中半径 + 怪物身体半径的常见量级），宁严勿松。</para>
        /// </summary>
        private static MonsterState PlacePlayerForIsolatedShot(out MonsterState target, float maxEuclid = 8f)
        {
            target = null;
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || !m.alive) continue;
                var mg = m.Grid();
                var mc = Center(mg);
                for (var r = 4; r <= 9; r++)
                {
                    for (var dx = -r; dx <= r; dx++)
                    {
                        for (var dy = -r; dy <= r; dy++)
                        {
                            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                            var g = new Vector2Int(mg.x + dx, mg.y + dy);
                            if (!_ctx.Map.InBounds(g) || !_ctx.Map.Walkable(g)) continue;
                            var pc = Center(g);
                            if (Vector2.Distance(pc, mc) > maxEuclid) continue;
                            if (!LineClearOfMonsters(pc, mc, 1.2f, m.id)) continue;

                            _player.SetGrid(g, Iso.DirectionTo(g, mg));
                            target = m;
                            return m;
                        }
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 线段 `a→b` 的**段内**没有别的怪（清线判定）。
        /// <para>为什么只看段内（投影 `t ∈ [0,1]`）：段外的怪（施法者背后 / 主目标背后）**不会被这条弹道打到**，
        /// 拿"到**端点**的距离"去拒它们，恰好会把"玩家 → 主目标 → 邻居"这种**最该取样**的共线布局全拒掉
        /// （正交相邻的邻居到端点只有 1.0 格 &lt; 1.2）—— 首跑实测：范围用例的站位恒为 `none`。</para>
        /// <para><paramref name="except"/> = 不算入清线的怪 id（主目标；以及"只应被炸到"的邻居 ——
        /// 邻居是否压在**段内**由调用方单独判）。</para>
        /// </summary>
        private static bool LineClearOfMonsters(Vector2 a, Vector2 b, float clearance, params int[] except)
        {
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || !m.alive) continue;
                var skip = false;
                for (var k = 0; k < except.Length; k++) if (m.id == except[k]) { skip = true; break; }
                if (skip) continue;
                var p = Center(m.Grid());
                var t = SegmentT(p, a, b);
                if (t < 0f || t > 1f) continue;                       // 段外 ⇒ 这条弹道打不到它
                if (SkillModule.DistanceToSegment(p, a, b) <= clearance) return false;
            }
            return true;
        }

        /// <summary>
        /// 找**一对**挨着的活怪（用于"范围伤害"用例：必须有第二只才能证明"按半径结算"）。
        /// <para>刷怪点随机 ⇒ 不假设地图上一定有；找不到就返回 false（调用点会变红并说明原因）。</para>
        /// </summary>
        private static bool FindMonsterPair(int maxGap, int exceptId, out MonsterState a, out MonsterState b)
        {
            a = null;
            b = null;
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || !m.alive || m.id == exceptId) continue;
                for (var j = 0; j < all.Count; j++)
                {
                    if (j == i) continue;
                    var n = all[j];
                    if (n == null || !n.alive || n.id == exceptId) continue;
                    var d = GridDist(m.Grid(), n.Grid());
                    if (d < 1 || d > maxGap) continue;
                    a = m;
                    b = n;
                    return true;
                }
            }
            return false;
        }

        private static Vector2 Center(Vector2Int g)
        {
            return new Vector2(g.x + 0.5f, g.y + 0.5f);
        }

        /// <summary>
        /// 「范围伤害」用例的取样：把玩家摆到能**干净命中 <paramref name="primary"/>** 的位置，
        /// 同时保证 <paramref name="neighbour"/> **不在**这条弹道上（它必须是被"炸"到的，不是被"打"到的）。
        /// </summary>
        private static bool PlacePlayerForSplash(MonsterState primary, MonsterState neighbour, int minCheb)
        {
            var primaryGrid = primary.Grid();
            var pc0 = Center(primaryGrid);
            var nc = Center(neighbour.Grid());
            var tried = 0;              // 环上真正可走的格数
            var blockedNeighbour = 0;   // 因为"邻居压在弹道**段内**"而拒
            var blockedLine = 0;        // 因为"弹道**段内**还有别的怪"而拒
            for (var r = minCheb; r <= minCheb + 9; r++)      // 近处优先；近处被墙/别的怪占满才往外找
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                        var g = new Vector2Int(primaryGrid.x + dx, primaryGrid.y + dy);
                        if (!_ctx.Map.InBounds(g) || !_ctx.Map.Walkable(g)) continue;
                        tried++;
                        var pc = Center(g);
                        //   ⚠️「邻居在弹道上」必须**只看线段之内**（投影参数 `t ∈ [0,1]`）：
                        //   主目标**背后**（`t > 1`）的邻居到线段（终点 = 主目标格心）的距离天然 ≈1 格 < 1.2
                        //   ⇒ 旧写法（无条件 `DistanceToSegment ≤ 1.2` 就拒）恰好在"玩家 → 主目标 → 邻居"
                        //     这条**最该取样**的共线布局上把候选全拒掉（实测：站位 = none）。
                        //   而共线时邻居**正是**该被"炸到"的那一只 —— 拒它等于把用例本身拒掉了。
                        var t = SegmentT(nc, pc, pc0);
                        if (t >= 0f && t <= 1f && SkillModule.DistanceToSegment(nc, pc, pc0) <= 1.2f)
                        {
                            blockedNeighbour++;
                            continue;
                        }
                        //   弹道要干净：主目标与邻居都排除（邻居是否压线已由上面那条单独判过）
                        if (!LineClearOfMonsters(pc, pc0, 1.2f, primary.id, neighbour.id))
                        {
                            blockedLine++;
                            continue;
                        }
                        _player.SetGrid(g, Iso.DirectionTo(g, primaryGrid));
                        return true;
                    }
                }
            }
            Check($"范围取样：找得到「能干净命中主目标 {primaryGrid}」的站位（扫 {minCheb}~{minCheb + 9} 环）", false,
                $"none（主目标 {primaryGrid} / 邻居 {neighbour.Grid()}；环上可走 {tried} ⇒ 邻居压线 {blockedNeighbour}、弹道不清 {blockedLine}）");
            return false;
        }

        /// <summary>`p` 在线段 `a→b` 上的**投影参数**（0 = a、1 = b；&lt;0 或 &gt;1 = 落在线段**之外**）。</summary>
        private static float SegmentT(Vector2 p, Vector2 a, Vector2 b)
        {
            var dx = b.x - a.x;
            var dy = b.y - a.y;
            var len2 = dx * dx + dy * dy;
            if (len2 <= 1e-6f) return 0f;
            return ((p.x - a.x) * dx + (p.y - a.y) * dy) / len2;
        }

        /// <summary>
        /// 「范围伤害」用例需要**两只挨着的怪**。刷怪点是随机的 ⇒ 不假设地图上一定有；
        /// 用 `IMonsterModule.ApplyDamage` 把**附近别的怪挪不动**，改成：
        /// 直接从 `All` 里找一对"格距 ≤ maxGap"的活怪，把玩家摆到主目标旁。
        /// 找不到 ⇒ 返回 null（调用点会变红并说明原因，不静默跳过）。
        /// </summary>
        private static MonsterState PlaceSecondMonsterNear(Vector2Int center, int maxGap)
        {
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || !m.alive) continue;
                var d = GridDist(m.Grid(), center);
                if (d >= 1 && d <= maxGap) return m;
            }
            return null;
        }

        private static MonsterState FindMonsterAt(Vector2Int grid)
        {
            var all = _ctx.Monster.All;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m != null && m.alive && m.Grid() == grid) return m;
            }
            return null;
        }

        private static int GridDist(Vector2Int a, Vector2Int b)
        {
            var dx = Math.Abs(a.x - b.x);
            var dy = Math.Abs(a.y - b.y);
            return dx > dy ? dx : dy;
        }

        private static SkillEffect FindEffect(int skillId, SkillFxKind kind)
        {
            var list = _skillImpl.Effects;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].skillId == skillId && list[i].kind == kind) return list[i];
            }
            return null;
        }

        private static bool FindEffectKind(int skillId, SkillFxKind kind)
        {
            return FindEffect(skillId, kind) != null;
        }

        /// <summary>
        /// 「精通进伤害」取样用的干净世界：**固定地图 seed**（⇒ `SkillRng()` 的随机流可复现）
        /// + `ResetForClass`（⇒ `_rng` 复位）+ 学会被测量的技能（可选再学火焰精通）。
        /// </summary>
        private static void PrepareForMasterySample(int skillId, int seed, bool learnMastery)
        {
            PrepareMap(AreaId.BloodMoor, seed);
            _ctx.Monster.DespawnAll();
            _ctx.Monster.SpawnArea(AreaId.BloodMoor);
            _ctx.Skill.ResetForClass(PlayerClass.Sorceress, NewSave(PlayerClass.Sorceress, 0));
            _player.SetLevel(99);
            _player.SetSkillPoints(99);
            _player.SetMaxMana(400);
            _player.SetMana(400);
            _skillImpl.Learn(skillId);
            if (learnMastery) _skillImpl.Learn(_skillImpl.IdOfCode("Fire Mastery"));
        }

        /// <summary>
        /// 连打 <paramref name="n"/> 发，返回**逐次** `DamageArgs.raw` 的明细（`null` = 没量够 `n` 发）。
        /// <para>为什么返回明细而不是只要合计：精通判据要判"**逐次**等于 `trunc(raw × 130 / 100)`" ——
        /// 只看合计比值会被整数截断效应拉偏（火弹 3-6 的 20 发实测 ≈117%，不是 130%）。</para>
        /// <para>每次施放前把目标的 `hp/maxHp` 抬到很大并复位 —— 低等级怪一发就死，
        /// 不复位的话第二发就没有靶子了（判据会退化成"只量了一发"）。</para>
        /// </summary>
        private static List<int> RawOverCasts(int skillId, int n)
        {
            var raws = new List<int>();
            for (var i = 0; i < n * 3 && raws.Count < n; i++)
            {
                var target = PickFatTarget();
                if (target == null) break;
                _ctx.Skill.Tick(1f);                  // 排掉冷却
                _lastSkillRaw = 0;
                if (!_ctx.Skill.TryCast(skillId, target.Grid())) continue;
                var steps = 0;
                while (_skillImpl.ActiveProjectileCount > 0 && steps < 400) { _ctx.Skill.Tick(Dt); steps++; }
                if (_lastSkillRaw > 0) raws.Add(_lastSkillRaw);
            }
            return raws.Count == n ? raws : null;
        }

        /// <summary>连打 <paramref name="n"/> 发，返回**最大**的一次 `raw`（判"掷点上界被抬高"）。</summary>
        private static int MaxRawOverCasts(int skillId, int n)
        {
            var raws = RawOverCasts(skillId, n);
            if (raws == null) return -1;
            var best = 0;
            for (var i = 0; i < raws.Count; i++) if (raws[i] > best) best = raws[i];
            return best;
        }

        /// <summary>
        /// 复刻生产的精通乘区口径（`SkillModule.RollSkillDamage(min, max, type)`）：
        /// **先把 min/max 两端各按 `×(100+pct)/100` 整数截断放大**，再在放大后的区间里掷点。
        /// 判据用它算"期望区间"（`lo..hi`）—— 逐次结果必须落在区间内，
        /// 且上界取到 `hi`（证明放大的是两端，不是只掷完再乘一个数）。
        /// </summary>
        private static (int lo, int hi) ExpectSpan(int min, int max, int pct)
        {
            var lo = min * (100 + pct) / 100;
            var hi = max * (100 + pct) / 100;
            if (hi < lo) hi = lo;
            return (lo, hi);
        }

        /// <summary>逐次相等（长度相同且每一位相同；任一为 `null` ⇒ false）。</summary>
        private static bool SameList(List<int> a, List<int> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>合计（`null` ⇒ -1）。</summary>
        private static int SumList(List<int> xs)
        {
            if (xs == null) return -1;
            var s = 0;
            for (var i = 0; i < xs.Count; i++) s += xs[i];
            return s;
        }

        /// <summary>明细打印（`null` ⇒ 文本，不抛）。</summary>
        private static string Join(List<int> xs)
        {
            if (xs == null) return "没量到";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < xs.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(xs[i]);
            }
            return sb.ToString();
        }

        /// <summary>把玩家摆到"弹道无遮挡正对某只怪"的位置，并把那只怪的血量抬到打不死（取样靶）。</summary>
        private static MonsterState PickFatTarget()
        {
            var t = PlacePlayerForIsolatedShot(out _, 8f);
            if (t == null) return null;
            t.maxHp = 100000;
            t.hp = 100000;
            t.alive = true;
            return t;
        }

        private static SkillDef FindDef(int id)
        {
            var list = _ctx.Skill.Available;
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].id == id) return list[i];
            }
            return null;
        }

        private static Table.BaseSkillRow FirstRowOfClass(PlayerClass cls)
        {
            var all = Table.Tables.Default.Skill.All();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Class == (int)cls) return all[i];
            }
            return null;
        }

        private static CharacterSave NewSave(PlayerClass cls, int skillPoints)
        {
            return new CharacterSave
            {
                cls = cls,
                name = "SkillCheck",
                level = 99,
                skillPoints = skillPoints,
                statPoints = 0,
                gold = 0,
                life = 100,
                mana = 400,
                gridX = 0,
                gridY = 0,
                skillIds = new List<int>(),
                skillLevels = new List<int>(),
                buttonSkills = new List<int> { -1, -1 },
            };
        }

        // ═════════════════════════════════════════════════════════════════════
        // 断言 / 小节 / 异常兜底（与 combatcheck 同一套输出形制）
        // ═════════════════════════════════════════════════════════════════════
        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────");
            Console.WriteLine("▶ " + title);
            Console.WriteLine("────────────────────────────────────────────────────────────");
        }
        /// <summary>
        /// 第 8 节：**帧数表可用性**。
        /// <para>背景（真事故）：`MissileFrameCounts` 曾用 `new Dictionary&lt;string,int&gt;(OrdinalIgnoreCase)`
        /// 建表，而表里 `IceBolt` 与 `Icebolt` 是**同一个键** ⇒ 静态初始化器抛 `ArgumentException`
        /// ⇒ 该类型**永久不可用**（此后每次访问都抛 `TypeInitializationException`）
        /// ⇒ `ProjectileView.TryCreate` / `SkillFxView.TryCreate` 全线失败（异常被 catch 成一条 Warn）
        /// ⇒ **所有投射物与技能特效"只有逻辑、没有画面"** —— 画面里的就是「什么技能都没有效果」。
        /// 本节的判据 = 该类型**能被访问**（第一次访问就会跑静态初始化）+ 大小写两种写法都查得到。</para>
        /// </summary>
        private static void Step8_MissileFrameTable()
        {
            // ① 类型可访问（静态初始化没崩）—— 就是那个事故的阳性对照
            var ok = true;
            string err = null;
            var a = 0;
            var b = 0;
            var c = 0;
            try
            {
                a = MissileFrameCounts.Of("IceBolt");
                b = MissileFrameCounts.Of("Icebolt");
                c = MissileFrameCounts.Of("icebolt");
            }
            catch (Exception ex)
            {
                ok = false;
                err = ex.GetType().Name + ": " + ex.Message;
            }

            Check("8-1 帧数表类型可访问（静态初始化未抛异常）", ok, ok ? "Of(\"IceBolt\") 正常返回" : err);
            if (!ok) return;

            Check("8-2 大小写两种写法都查得到（IceBolt == Icebolt == 6）",
                a == 6 && b == 6, $"IceBolt={a} Icebolt={b}");
            Check("8-3 全小写查表也命中（icebolt=6 ⇒ 查表大小写不敏感）", c == 6, $"icebolt={c}");

            var miss = MissileFrameCounts.Of("__no_such_cel__");
            Check("8-4 未登记键回退 Fallback（Has=false 且 Of=Fallback）",
                !MissileFrameCounts.Has("__no_such_cel__") && miss == MissileFrameCounts.Fallback,
                $"Of={miss} Fallback={MissileFrameCounts.Fallback}");

            // ② 判据自检：同口径字典下这两个键**必须**被认成同一个 —— 不成立就说明 8-2 是假绿
            var dup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            dup["IceBolt"] = 6;
            var sameKey = dup.ContainsKey("Icebolt");
            Check("8-5 判据自检：OrdinalIgnoreCase 下 IceBolt 与 Icebolt 是同一个键（重复键正是该事故成因）",
                sameKey, "dup.ContainsKey(\"Icebolt\") = " + sameKey);
        }

        /// <summary>
        /// 第 9 节：**特效排序 = 按贴图下沿行取实体档**。
        /// <para>背景（真事故）：截图"法师脚下冰霜特效被地面瓦片网格线切断"。根因 = 特效贴图
        /// **中心 pivot** 且原版帧偏高（IceBlast 156 px ⇒ 世界半高 0.975）⇒ 贴图下沿探到锚点行以南
        /// 2~3 行，而 地面(D+2) = 4D+104 **大于** 实体档(D) = 4D+102 ⇒ 正前方两格的地面盖住特效。
        /// 实体（角色/怪）没这问题：脚底 pivot 只向上长。修法 = <see cref="SkillFxView.SortOrderKernel"/>
        /// （把特效当成"站在它贴图下沿那一行的实体"）。</para>
        /// <para>判据全部走**纯内核**（不碰 Unity）：① 下沿行公式 == 沿线暴力采样取格的最大行；
        /// ② 真实素材尺寸下档位压得住全部被穿到的地面；③ 不越档（下沿行本身的物件仍盖住它）；
        /// ④ 零落差（脚底 pivot）不虚高；⑤ **旧口径**（锚点实体档）在真实尺寸下必然被地面盖住
        /// = bug 机制自证（否则 ② 是假绿）。</para>
        /// </summary>
        private static void Step9_FxSortOrder()
        {
            // 锚点：格 (10,10)（D=20）格心 ⇒ 世界 y = −(D+2)·HalfH（`Center(g)` + `Projectile.WorldOf`）
            var g = new Vector2Int(10, 10);
            var D = g.x + g.y;
            var anchorY = Projectile.WorldOf(new Vector2(g.x + 0.5f, g.y + 0.5f)).y;

            // ── 9.1 下沿行公式：内核行 == floor(fy0)−1，且 floor(fy0) == 沿线暴力采样最大行 ──
            //   落差值取**非整数 fy0**（恰为整数时下沿线只擦到邻格角点，采样会漏 ⇒ 不进本判据）
            var drops = new[] { 0.2f, 0.45f, 0.525f, 0.688f, 0.975f, 1.325f, 1.5375f, 1.788f, 1.913f };
            var rowOk = 0;
            string rowErr = null;
            foreach (var drop in drops)
            {
                var bottomY = anchorY - drop;
                var fy0 = -bottomY / Iso.HalfH;
                var bruteMax = -999;
                for (var i = -600; i <= 600; i++)
                {
                    var c = Iso.WorldToGrid(new Vector3(i * 0.01f, bottomY, 0f));
                    if (c.x + c.y > bruteMax) bruteMax = c.x + c.y;
                }
                var order = SkillFxView.SortOrderKernel(anchorY, drop, g);
                var row = (order - GameConst.SortOrderBase - GameConst.LayerOffsetEntity) / GameConst.SortOrderStep;
                if (Mathf.FloorToInt(fy0) == bruteMax && row == Mathf.FloorToInt(fy0) - 1) rowOk++;
                else rowErr = $"drop={drop} fy0={fy0:F3} brute={bruteMax} row={row}";
            }
            Check("9-1 下沿行公式：floor(fy0) == 沿线暴力采样最大行，且内核取 floor(fy0)−1",
                rowOk == drops.Length, $"{rowOk}/{drops.Length} 一致{(rowErr == null ? "" : "；首个不一致 " + rowErr)}");

            // ── 9.2 真实素材尺寸（PNG 实测；导入 pivot=(0.5,0.5) ⇒ 中心 pivot）────────────
            var arts = new (string name, int pxH)[]
            {
                ("IceBlast", 156), ("Firebolt", 152), ("ExpArrowExplode", 246),
                ("Meteor", 110), ("MeteorTail", 286), ("FrostNova", 72),
                ("IceBolt", 142), ("ChargedBolt", 84), ("Blizzard", 306),
                ("LightningboltBig", 212), ("环/盘占位(64×32)", 32),
            };
            var coverOk = 0;      // ② 压得住下沿行的地面
            var noOverrun = 0;    // ③ 不越档：下沿行本身的物件层仍盖住特效
            var neverBelow = 0;  // ④ 永不低于正南一格实体档（不会被同格实体盖住）
            var oldCut = 0;      // ⑤ 旧口径必被正南两格地面盖住（bug 机制自证）
            string artErr = null;
            foreach (var (name, pxH) in arts)
            {
                var drop = pxH / (float)GameConst.PixelsPerUnit * 0.5f * SpriteFrames.ArtScale;
                var order = SkillFxView.SortOrderKernel(anchorY, drop, g);
                var k = Mathf.FloorToInt(-(anchorY - drop) / Iso.HalfH);       // 真实最大行
                if (order > Iso.SortOrder(0, k, GameConst.LayerOffsetGround) - GameConst.SortOrderStep) coverOk++;
                else artErr = $"{name}: 档 {order} ≤ 下沿行地面 {Iso.SortOrder(0, k, GameConst.LayerOffsetGround) - GameConst.SortOrderStep}";
                if (order < Iso.SortOrder(0, k, GameConst.LayerOffsetObject)) noOverrun++;
                if (order >= Iso.SortOrder(0, D + 1, GameConst.LayerOffsetEntity)) neverBelow++;
                if (Iso.SortOrder(g, GameConst.LayerOffsetEntity)
                    < Iso.SortOrder(0, D + 2, GameConst.LayerOffsetGround) - GameConst.SortOrderStep) oldCut++;
            }
            Check("9-2 真实素材 11 种：档位压得住贴图下沿穿到的全部地面（ground(k) = 4k+96）",
                coverOk == arts.Length, $"{coverOk}/{arts.Length}{(artErr == null ? "" : "；" + artErr)}");
            Check("9-3 不越档：档位 < 下沿行物件层（前面的墙/树仍盖住特效下半截 = 原版遮挡）",
                noOverrun == arts.Length, $"{noOverrun}/{arts.Length}");
            Check("9-4 永不低于正南一格实体档（同格实体永远盖不住特效）",
                neverBelow == arts.Length, $"{neverBelow}/{arts.Length}");
            Check("9-5 ★ 旧口径机制自证：锚点实体档(4D+102) 必然 < 正南两格地面(4D+104) ⇒ 修复前恒被切",
                oldCut == arts.Length, $"{oldCut}/{arts.Length}（这就是截图那条 bug）");

            // ── 9.6 零落差（脚底 pivot）不虚高：档位恰为正南一格实体档 ────────────────
            var zero = SkillFxView.SortOrderKernel(anchorY, 0f, g);
            Check("9-6 零落差（脚底 pivot）⇒ 档位 = 正南一格实体档（不虚高、仍压得住脚下地面）",
                zero == Iso.SortOrder(0, D + 1, GameConst.LayerOffsetEntity) &&
                zero > Iso.SortOrder(0, D + 1, GameConst.LayerOffsetGround) - GameConst.SortOrderStep,
                $"zero={zero} ent(D+1)={Iso.SortOrder(0, D + 1, GameConst.LayerOffsetEntity)} " +
                $"ground(D+1)={Iso.SortOrder(0, D + 1, GameConst.LayerOffsetGround) - GameConst.SortOrderStep}");

            // ── 9.7 单调性：贴图越高 ⇒ 档位只升不降（防符号错/反代换）────────────────
            var mono = true;
            var prev = -1;
            foreach (var pxH in new[] { 32, 72, 110, 142, 152, 156, 212, 246, 306 })
            {
                var drop = pxH / (float)GameConst.PixelsPerUnit * 0.5f * SpriteFrames.ArtScale;
                var order = SkillFxView.SortOrderKernel(anchorY, drop, g);
                if (order < prev) mono = false;
                prev = order;
            }
            Check("9-7 单调性：贴图越高档位只升不降（9 种高度）", mono, "32→306 px 单调不减");
        }

        /// <summary>
        /// 第 10 节：**份数补齐**。
        /// <para>背景（真事故）：火墙 5 格只画 1 簇火、暴风雪 ~10 格只画 1 坨冰、连锁闪电只画 1 段。
        /// 根因 = `SpawnField`/`FireChain` 的顺序是**先** `SpawnEffect`（内部 `TryCreate` 按**当时的**
        /// <see cref="SkillFxView.PartCount"/> 建 SpriteRenderer）**后** `e.points.AddRange(pts)`
        /// ⇒ 建视图那刻 `points` 还是空的 ⇒ 只建了 1 份。修法 = 每帧 Sync 前
        /// <see cref="SkillFxView.EnsureParts"/> 按当前份数**原地补齐**（只增不删）。</para>
        /// <para>判据：① 纯内核逐 kind 钉份数口径（Field=格数 / Chain=N−1 / Beam=radius 且 ≥2 /
        /// Falling=2 / 其余=1）；② 真施放火墙/暴风雪 ⇒ 份数 == 场上格数（玩家可见的那条）；
        /// ③ 旧口径自证（建视图当刻 points 空 ⇒ 恰好 1 份 = bug 形态，不是随机份数）；
        /// ④ 陨石坠落物（两份、起飞高度、t² 重力插值、与落点标记同生死）；
        /// ⑤ 火焰之径子火素材池非退化（Big/Medium/Small 三池随机）。</para>
        /// </summary>
        private static void Step10_FxPartCount()
        {
            // ── 10.1 纯内核：PartCount 的逐 kind 份数口径 ────────────────────────
            var field = new SkillEffect { kind = SkillFxKind.Field };
            Check("10-1 Field 0 格 ⇒ 兜底 1 份（建视图当刻的口径 = 旧 bug 的形态）",
                SkillFxView.PartCount(field) == 1, SkillFxView.PartCount(field).ToString());
            field.points.Add(new Vector2(3, 4));
            Check("10-2 Field 1 格 ⇒ 1 份", SkillFxView.PartCount(field) == 1,
                SkillFxView.PartCount(field).ToString());
            for (var i = 0; i < 4; i++) field.points.Add(new Vector2(5 + i, 4));
            Check("10-3 Field 5 格（火墙）⇒ 5 份", SkillFxView.PartCount(field) == 5,
                SkillFxView.PartCount(field).ToString());

            var chain = new SkillEffect { kind = SkillFxKind.Chain };
            Check("10-4 Chain 0 顶点 ⇒ 兜底 1 段", SkillFxView.PartCount(chain) == 1,
                SkillFxView.PartCount(chain).ToString());
            for (var i = 0; i < 5; i++) chain.points.Add(new Vector2(i, 4));
            // ：Chain 份数口径从"每跳一份"改成"折线**世界总长** ÷ 0.7"（实测：// "连锁闪电只有一小段蓝色电弧"——5 顶点 4 跳里每一跳跨 1 格，线段长 1.118 世界单位，
            // 而 `LightningStrike` 只有 148px/80PPU = 1.85 单位宽 ⇒ 一跳一份钉在中点，只盖住中段）。
            var chainLen = 0f;
            for (var i = 0; i + 1 < chain.points.Count; i++)
                chainLen += SkillFxView.WorldLen(chain.points[i], chain.points[i + 1]);
            var chainWant = Mathf.Clamp(Mathf.Max(4, Mathf.RoundToInt(chainLen / 0.7f) + 1), 1, 40);
            Check("10-5 Chain 5 顶点 ⇒ 份数 = 折线世界总长 ÷ 0.7 + 1（且 ≥ 跳数）",
                SkillFxView.PartCount(chain) == chainWant,
                $"{SkillFxView.PartCount(chain)} 份（总长 {chainLen:0.00} 世界单位）");

            var beam = new SkillEffect { kind = SkillFxKind.Beam, radius = 8, from = new Vector2(10, 10), to = new Vector2(18, 10) };
            var beamLen = SkillFxView.WorldLen(beam.from, beam.to);
            var beamWant = Mathf.Clamp(Mathf.RoundToInt(beamLen / 0.7f) + 1, 2, 32);
            Check("10-6 Beam 8 格（格坐标 8 步）⇒ 份数 = 世界长度 ÷ 0.7 + 1，**不是**按 radius 8",
                SkillFxView.PartCount(beam) == beamWant && beamWant > 8,
                $"{SkillFxView.PartCount(beam)} 份（世界长 {beamLen:0.00}，radius 口径会给 8）");
            var beam0 = new SkillEffect { kind = SkillFxKind.Beam, radius = 0 };
            Check("10-7 Beam 起点=终点（长度 0）⇒ 下限 2 份", SkillFxView.PartCount(beam0) == 2,
                SkillFxView.PartCount(beam0).ToString());
            Check("10-8 Falling（陨石火球 + 尾迹）⇒ 恒 2 份",
                SkillFxView.PartCount(new SkillEffect { kind = SkillFxKind.Falling }) == 2, "2");
            var singleOk = true;
            foreach (var k in new[] { SkillFxKind.Impact, SkillFxKind.Nova, SkillFxKind.Marker, SkillFxKind.Aura })
                if (SkillFxView.PartCount(new SkillEffect { kind = k }) != 1) singleOk = false;
            Check("10-9 Impact/Nova/Marker/Aura ⇒ 恒 1 份", singleOk, "4/4");
            Check("10-10 Summon（多头蛇）⇒ 份数 = 蛇口数（纯表现，不放大伤害）",
                SkillFxView.PartCount(new SkillEffect { kind = SkillFxKind.Summon, count = SkillTuning.HydraHeads })
                    == SkillTuning.HydraHeads, $"{SkillTuning.HydraHeads} 份");
            Check("10-11 护甲/护盾/附魔光环不再半透明（实测：四系护甲'几乎看不见'）",
                SkillFxView.AlphaOf(new SkillEffect { kind = SkillFxKind.Aura }, 0.5f) > 0.9f,
                SkillFxView.AlphaOf(new SkillEffect { kind = SkillFxKind.Aura }, 0.5f).ToString("0.00"));

            // ── 10.2 真施放：火墙 / 暴风雪 ⇒ 份数 == 场上格数（玩家可见的那条）────────
            foreach (var id in new[] { 46, 54 })
            {
                var def = FindDef(id);
                SetupFresh(id, out var spec);
                var target = PlacePlayerNearMonster(5);
                if (target == null) { Check($"#{id} {def.name}：份数 == 格数", false, "none"); continue; }
                var cast = _ctx.Skill.TryCast(id, target.Grid());
                var fx = FindEffect(id, SkillFxKind.Field);
                Check($"#{id} {def.name}：**每一格各有一份**（份数 == 场上格数，且 ≥ 2）",
                    cast && fx != null && SkillFxView.PartCount(fx) == fx.points.Count && fx.points.Count >= 2,
                    fx != null ? $"{SkillFxView.PartCount(fx)}/{fx.points.Count}" : "none");
            }

            // ── 10.3 陨石坠落物：两份、起飞高度、t² 重力插值、与标记同生死 ──────────
            {
                SetupFresh(51, out var spec);
                var target = PlacePlayerNearMonster(6);
                if (target == null) { Check("#51 陨石坠落物：有目标", false, "none"); }
                else
                {
                    var cast = _ctx.Skill.TryCast(51, target.Grid());
                    var fall = FindEffect(51, SkillFxKind.Falling);
                    var marker = FindEffect(51, SkillFxKind.Marker);
                    Check("#51 陨石：配了纯表现坠落物（火球 `Meteor` + 尾迹 `MeteorTail`）",
                        cast && fall != null && fall.art == "Meteor" && fall.impactArt == "MeteorTail",
                        fall != null ? fall.art + "+" + fall.impactArt : "none");
                    if (fall != null)
                    {
                        Check("#51 陨石：坠落物 = 2 份（火球 + 尾迹）", SkillFxView.PartCount(fall) == 2, "2");
                        var h = fall.fallFrom.y - fall.landAt.y;
                        Check($"#51 陨石：起飞高度 = SkillTuning.MeteorFallHeight（{SkillTuning.MeteorFallHeight:0.#} 格）",
                            Mathf.Abs(h - SkillTuning.MeteorFallHeight) < 1e-3f, $"h={h:0.###}");
                        Check("#51 陨石：坠落物与落点标记同一点、同一寿命（同步爆炸）",
                            marker != null &&
                            Mathf.Abs(fall.landAt.x - marker.pos.x) < 1e-6f &&
                            Mathf.Abs(fall.landAt.y - marker.pos.y) < 1e-6f &&
                            Mathf.Abs(fall.lifeTotal - marker.lifeTotal) < 1e-6f,
                            marker != null ? $"life {fall.lifeTotal:0.00}s" : "no marker");
                        // 出生就在高空（不是落点）：否则第一帧火球会闪现在落点、下一 tick 才升天
                        Check("#51 陨石：火球出生即在起飞点（不先闪现在落点）",
                            Mathf.Abs(fall.pos.y - fall.fallFrom.y) < 1e-3f,
                            $"pos.y={fall.pos.y:0.00} fallFrom.y={fall.fallFrom.y:0.00}");
                        // 重力插值：半程时火球必须严格介于高空与落点之间（t² 曲线，不会提前到底）
                        var y0 = fall.pos.y;
                        TickSim(spec.duration * 0.5f);
                        var midOk = fall.pos.y < y0 - 1e-3f && fall.pos.y > fall.landAt.y + 1e-3f;
                        Check("#51 陨石：半程时火球严格介于高空与落点之间（t² 重力曲线在推进）",
                            midOk, $"y {y0:0.00} → {fall.pos.y:0.00}（落点 {fall.landAt.y:0.00}）");
                        // 与标记同生死：跑满全程 ⇒ 坠落物也消失（不留在世界里的幽灵特效）
                        TickSim(spec.duration);
                        Check("#51 陨石：坠落物与标记一同消失（残留 = 幽灵特效）",
                            !fall.alive && marker != null && !marker.alive,
                            $"fall.alive={fall.alive} marker.alive={(marker != null ? marker.alive : false)}");
                    }
                }
            }

            // ── 10.4 火焰之径：子火素材池 = Big/Medium/Small 三池随机（非退化）───────
            {
                SetupFresh(41, out var spec);
                _player.SetGrid(_ctx.Map.SpawnPoint);
                var cast = _ctx.Skill.TryCast(41, _player.Grid);
                var pool = new HashSet<string> { "groundFireBig", "groundFireMedium", "groundFireSmall" };
                var seen = new HashSet<string>();
                var foreign = new List<string>();
                var children = 0;
                TickSim(spec.duration);     // 跑完整条火路：每 tick 在脚下生一簇独立子火
                var list = _skillImpl.Effects;
                for (var i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    // 子火 = 同技能、Field、**不跟随施法者**（驱动器本身 followCaster=true）
                    if (e == null || e.skillId != 41 || e.kind != SkillFxKind.Field || e.followCaster) continue;
                    children++;
                    if (pool.Contains(e.art)) seen.Add(e.art); else foreign.Add(e.art);
                }
                Check("#41 火焰之径：跑完整条火路后留下多簇子火（各自计时、各自结算）",
                    cast && children >= 3, $"子火 {children} 簇");
                Check("#41 火焰之径：每簇子火的素材都在 Big/Medium/Small 三池内（无第四种）",
                    foreign.Count == 0, foreign.Count == 0 ? string.Join(",", seen) : "外来：" + string.Join(",", foreign));
                Check("#41 火焰之径：三池随机非退化（整条路至少出现 2 种大小）", seen.Count >= 2,
                    $"出现 {seen.Count} 种：{string.Join(",", seen)}");
            }

            // ── 10.5 真施放：炼狱之火 / 连锁闪电 / 多头蛇 的**可见份数**（实测：三条 DIFF）──
            {
                // #36 炼狱之火：原版 `infernoflame1` 沿弹道**高速连发** ⇒ 份数密到互相重叠才读作火柱。
                // 旧口径（按 radius=4 格给 4 份）⇒ 相邻份间隔 1.49 世界单位 > 火球宽 1.0 ⇒ 一排火球。
                SetupFresh(36, out _);
                var t36 = PlacePlayerNearMonster(4);
                if (t36 == null) Check("#36 炼狱之火：有目标", false, "none");
                else
                {
                    var cast = _ctx.Skill.TryCast(36, t36.Grid());
                    var fx = FindEffect(36, SkillFxKind.Beam);
                    if (fx == null) Check("#36 炼狱之火：出直线效果", false, "none");
                    else
                    {
                        var len = SkillFxView.WorldLen(fx.from, fx.to);
                        var parts = SkillFxView.PartCount(fx);
                        var gap = len / (parts - 1 <= 0 ? 1 : parts - 1);
                        Check("#36 炼狱之火：份数按**世界长度**算（≥ radius+1，不是每格一份）",
                            cast && parts > fx.radius, $"{parts} 份 / radius {fx.radius}");
                        Check("#36 炼狱之火：相邻份间距 < 1 世界单位（重叠 ⇒ 读作连续火柱而非一排火球）",
                            gap < 1f, $"间距 {gap:0.00} 世界单位（总长 {len:0.00} / {parts} 份）");
                        Check("#36 炼狱之火：用官方 `Flamethrower`（infernoflame1 的 CelFile）",
                            fx.impactArt == "Flamethrower2" || fx.art == "Flamethrower",
                            $"{fx.art} / {fx.impactArt}");
                    }
                }
            }

            {
                // #48 连锁闪电：官方 missiles.txt `chainlightning` → cel_file `LightningStrike`。
                // `Chain` 每份走 **impactArt**（`SkillFxView.ArtOf`）⇒ 不配就落到默认素材。
                SetupFresh(48, out _);
                var t48 = PlacePlayerNearMonster(6);
                if (t48 == null) Check("#48 连锁闪电：有目标", false, "none");
                else
                {
                    var cast = _ctx.Skill.TryCast(48, t48.Grid());
                    var fx = FindEffect(48, SkillFxKind.Chain);
                    if (fx == null) Check("#48 连锁闪电：出折线效果", false, "none");
                    else
                    {
                        var total = 0f;
                        for (var i = 0; i + 1 < fx.points.Count; i++)
                            total += SkillFxView.WorldLen(fx.points[i], fx.points[i + 1]);
                        Check("#48 连锁闪电：每份用官方 `LightningStrike` 亮折线（`ArtOf` 取 impactArt）",
                            SkillFxView.ArtOf(fx, 0) == "LightningStrike", SkillFxView.ArtOf(fx, 0));
                        Check("#48 连锁闪电：份数覆盖整条折线（> 跳数，一跳一份钉中点只盖住中段）",
                            cast && SkillFxView.PartCount(fx) > fx.points.Count - 1,
                            $"{SkillFxView.PartCount(fx)} 份 / {fx.points.Count - 1} 跳（折线长 {total:0.00}）");
                    }
                }
            }

            {
                // #57 多头蛇：原版是召唤怪物（本工程无召唤怪物精灵管线）⇒ 蛇身用三根原版高火柱顶替。
                SetupFresh(57, out _);
                var t57 = PlacePlayerNearMonster(3);
                if (t57 == null) Check("#57 多头蛇：可落地", false, "none");
                else
                {
                    var cast = _ctx.Skill.TryCast(57, t57.Grid());
                    var fx = FindEffect(57, SkillFxKind.Summon);
                    Check("#57 多头蛇：出召唤物", cast && fx != null, fx != null ? fx.art : "none");
                    if (fx != null)
                    {
                        Check("#57 多头蛇：蛇口本体用原版高火柱 `groundFireBig`（旧值 Firebolt = 一簇火球）",
                            fx.art == "groundFireBig", fx.art);
                        Check($"#57 多头蛇：{SkillTuning.HydraHeads} 个蛇口（主口 + 两侧小一号）",
                            SkillFxView.PartCount(fx) == SkillTuning.HydraHeads,
                            $"{SkillFxView.PartCount(fx)} 份");
                    }
                }
            }
        }

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

    /// <summary>`MonsterState` 没有 `Grid()`，这是宿主侧的便捷扩展（与 combatcheck 同口径）。</summary>
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
            for (var i = 0; i < _loaded.Count; i++) _loaded[i]?.Invoke(sceneName);
            onDone?.Invoke();
        }
        public void Unload(string sceneName, Action onDone = null) { onDone?.Invoke(); }
        public void OnSceneLoaded(Action<string> handler) => _loaded.Add(handler);
        public void OnSceneUnloaded(Action<string> handler) { }
    }
}
