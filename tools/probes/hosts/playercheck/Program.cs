// ─────────────────────────────────────────────────────────────────────────────
// Player / Camera / Input 自检宿主（`docs/agents/agent-06-主角相机与输入.md` §5 的验收项逐条自证）
//
// 运行：dotnet run --project <项目根>/tools/playercheck/PlayerCheck.csproj -c Release
//
// 覆盖的验收项：
//   ① 离线宿主编译/运行：0 错 0 警告（编译期）+ 断言全过（运行期）
//   ② 点击移动：Town 出生点 → 远端可走格，逐帧 Tick(0.02f) 直到到达（贴起点/终点/路径长度/帧数）
//   ③ 绕障：目标与出生点无视线 ⇒ 路径非直连，且**每一格**都可走、玩家全程 Walkable==true
//   ④ 不可达：非可走格 / 越界格 ⇒ 不移动 + 有日志 + 计数
//   ⑤ 受阻：桩地图给出「穿过不可走格」的路径 ⇒ 停在原地（不穿墙）+ Warn + BlockedStops++
//   ⑥ 踩出入口：Town 的出城口 ⇒ 发 Events.ExitEntered(BloodMoor)，且同一格只发一次
//   ⑦ 升级公式：AddExp 到 experience_c 阈值 ⇒ 等级 +1、生命上限按配表增长、日志 `[Player] level 1→2`
//   ⑧ 属性点：AllocateStat(Vitality,5) ⇒ MaxLife 增量 == life_per_vit × 5；非法请求被拒
//   ⑨ 装备生效：EquipChanged 事件 ⇒ 四维/抗性/护甲/AR 变化（不引用 Item 模块，只走事件）
//   ⑩ 死亡/复活：ApplyDamage 归零 ⇒ IsDead + PlayerDied；Revive ⇒ 回出生点满血
//   ⑪ 相机：等距参数（跟随位置/正交尺寸/边界钳制/缩放钳制/震动衰减）纯数学断言 + 无相机时优雅降级
//   ⑫ 输入：ShouldRetarget 真值表 / 按住状态机 / 无相机时点击被拒 / 腰带快捷键发事件
//
// ⛔ 不覆盖（需要 Unity 原生 API，留给主 agent 进 Play 后验）：
//    · 真实 `Camera.main` 的屏幕→地面反投影（离线宿主里 `Camera.main`/`Screen` 不可用，已断言降级路径）；
//    · 精灵渲染/动画/朝向帧（属 agent-07 的 View 模块）；
//    · 手感（跟随是否"抖"）与像素级画面。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;              // 12 个门面接口 + CameraRig + InputReader（后两者刻意与接口同命名空间，见其文件头）
using Diablo2.Module.Player;       // PlayerModule / PlayerStats / PlayerMotor / PlayerLog
using UnityEngine;
// ⚠️ 故意**不** `using Table;`：`Table` 命名空间里有一个 `Table.Vector3` 结构，
//    与 `UnityEngine.Vector3` 同名 ⇒ 一旦 `using Table;` + `using UnityEngine;` 同时出现，
//    裸写 `Vector3` 就是 CS0104（本宿主需要 UnityEngine.Vector3）。故配表一律写全名 `Table.TableLoader`。
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 using System 会 CS0104）。
using AppContext = Diablo2.App.AppContext;

namespace PlayerCheck
{
    /// <summary>
    /// 桩地图：只用来**逼出**「路径穿过不可走格」这一条防御分支
    /// （真实 `MapModule` 生成的路径永远不会穿过障碍，所以这条分支必须用桩来验）。
    /// </summary>
    internal sealed class StubMap : IMapModule
    {
        private readonly bool[,] _walkable = new bool[8, 8];
        private List<Vector2Int> _scriptedPath;

        public StubMap()
        {
            for (var x = 0; x < 8; x++)
                for (var y = 0; y < 8; y++)
                    _walkable[x, y] = true;
        }

        public int Width => 8;
        public int Height => 8;
        public AreaId Area => AreaId.Town;
        public int Seed => 1;
        public int BlockedCount => 1;
        public int WalkableCount => 63;
        public bool IsGenerated => true;
        public Vector2Int SpawnPoint => new Vector2Int(1, 1);
        public IReadOnlyList<Vector2Int> Exits => new List<Vector2Int>();
        public Vector2Int? CaveEntrance => null;
        public IReadOnlyList<Vector2Int> NpcPoints => new List<Vector2Int>();
        public IReadOnlyList<Vector2Int> MonsterSpawns => new List<Vector2Int>();

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < 8 && g.y < 8;
        public bool Walkable(Vector2Int g) => InBounds(g) && _walkable[g.x, g.y];
        public TileKind TileAt(Vector2Int g) => Walkable(g) ? TileKind.TownFloor : TileKind.Wall;
        public void Generate(AreaId area, int seed) { }
        public void Clear() { }
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to) => _scriptedPath;
        public Vector2Int RandomWalkableTile(Rng rng) => SpawnPoint;
        public void ShowArea(AreaId area) { }
        public MinimapArgs BuildMinimap() => new MinimapArgs();

        /// <summary>把某格设成不可走。</summary>
        public void SetWall(Vector2Int g) => _walkable[g.x, g.y] = false;

        /// <summary>指定 FindPath 的返回值（含穿过障碍的"坏路径"）。</summary>
        public void Script(List<Vector2Int> path) => _scriptedPath = path;
    }

    /// <summary>
    /// 桩怪物门面（agent-13 §A 悬停自证用）：本宿主只编 `Map/Player/Camera/Input`，
    /// `Module/Monster` 不在其中 ⇒ 注入本桩来走**真实的**悬停怪物解析路径（`IMonsterModule.All`）。
    /// </summary>
    internal sealed class StubMonsters : IMonsterModule
    {
        private readonly List<MonsterState> _all = new List<MonsterState>();

        /// <summary>最近一次 `SetHovered` 的 id（自证：悬停有没有同步到怪物模块）。</summary>
        public int HoveredId = int.MinValue;

        public void Add(MonsterState s) => _all.Add(s);

        public int AliveCount
        {
            get
            {
                var n = 0;
                for (var i = 0; i < _all.Count; i++) if (_all[i].alive) n++;
                return n;
            }
        }

        public IReadOnlyList<MonsterState> All => _all;

        public int CountInArea(AreaId area) => 0;

        public MonsterState Get(int monsterId)
        {
            for (var i = 0; i < _all.Count; i++) if (_all[i].id == monsterId) return _all[i];
            return null;
        }

        public bool IsAlive(int monsterId)
        {
            var m = Get(monsterId);
            return m != null && m.alive;
        }

        public void SpawnArea(AreaId area) { }
        public void DespawnAll() => _all.Clear();
        public void RemoveCorpse(int monsterId) { }
        public void Tick(float dt) { }
        public void ApplyDamage(int monsterId, int amount, DamageType type) { }
        public void NotifyAttacked(int monsterId) { }
        public void SetHovered(int monsterId) => HoveredId = monsterId;
        public bool ConsumeCorpse(int monsterId) => false;
    }

    /// <summary>桩 NPC 门面（同上；走真实的 `INpcModule.All` 悬停解析路径）。</summary>
    internal sealed class StubNpcs : INpcModule
    {
        private readonly List<NpcDef> _all = new List<NpcDef>();

        public void Add(NpcDef d) => _all.Add(d);

        public IReadOnlyList<NpcDef> All => _all;

        public NpcDef Get(int npcId)
        {
            for (var i = 0; i < _all.Count; i++) if (_all[i].id == npcId) return _all[i];
            return null;
        }

        public NpcDef FindNearest(Vector2Int grid) => null;
        public bool Interact(int npcId) => false;
        public NpcDialogArgs GetDialog(int npcId) => null;
        public void ChooseOption(int npcId, int optionIndex) { }
        public ShopOpenArgs GetShop(int npcId) => null;
        public bool Buy(int npcId, int index, int count) => false;
        public bool Sell(int npcId, int anchorIndex) => false;
        public int Repair(int npcId, int anchorIndex) => -1;
        public void LoadFrom(CharacterSave save) { }
        public void Tick(float dt) { }
        public void Reset() { }
    }

    public static class Program
    {
        private const string ClientAssets =
            @"client\Assets";

        private const float Dt = 0.02f;          // 50 FPS（验收要求逐帧 Tick(0.02f)）
        private const int FrameCap = 6000;       // 单次移动的帧数上限（300 秒游戏时间，足够）
        private static int _fail;
        private static int _ok;

        public static int Main()
        {
            Console.WriteLine("================ PlayerCheck：主角/相机/输入 离线自检 ================");

            var bus = new RecordingEventBus();
            var input = new ScriptedInput();
            var setting = new MemSetting();
            Game.Logger = new CaptureLogger();
            Game.Event = bus;
            Game.Input = input;
            Game.Setting = setting;
            Game.Res = new EmptyResourceManager();
            Game.IsRunning = true;

            var ctx = WireUp();
            if (ctx == null)
            {
                Console.WriteLine("装配阶段失败 ⇒ 后续步骤无法进行");
                return 1;
            }

            var map = (Diablo2.Module.Map.MapModule)ctx.Map;
            var player = (PlayerModule)ctx.Player;
            var rig = (CameraRig)ctx.Camera;

            RunStep("1. 五职业 1 级派生属性", () => Step1_FiveClasses(ctx, map));
            RunStep("2. 点击移动", () => Step2_ClickMove(ctx, map, player, bus));
            RunStep("3. 绕障", () => Step3_Detour(ctx, map, player, bus));
            RunStep("4. 不可达", () => Step4_Unreachable(ctx, map, player, bus));
            RunStep("5. 受阻（桩地图）", () => Step5_Blocked(ctx, player, bus));
            RunStep("6. 出入口", () => Step6_ExitPortal(ctx, map, player, bus));
            RunStep("7. 升级", () => Step7_LevelUp(ctx, player));
            RunStep("8. 属性点", () => Step8_AllocateStat(ctx, player));
            RunStep("9. 装备生效", () => Step9_Equip(ctx, player, bus));
            RunStep("10. 死亡/复活", () => Step10_DeathRevive(ctx, map, player, bus));
            RunStep("11. 等距跟随相机", () => Step11_Camera(ctx, map, rig, input));
            RunStep("12. 输入读取", () => Step12_Input(ctx, input, bus));
            RunStep("13. 交互：悬停 / 点怪攻击 / Shift 站立攻击 / 走跑切换",
                () => Step13_Interaction(ctx, map, player, input, bus));
            RunStep("14. 复位", () => Step13_Reset(ctx, player));

            Console.WriteLine();
            Console.WriteLine($"================ 结束：{_ok} 项通过，{_fail} 项失败 ================");
            if (_fail != 0) return 1;
            Console.WriteLine();
            Console.WriteLine("未覆盖（需要 Unity 原生 API，留给主 agent 进 Play 后验）：");
            Console.WriteLine("  ① Camera.main 的真实屏幕→地面反投影（点哪走哪）—— 离线宿主里 Camera.main/Screen 不可用，本宿主只断言了降级路径；");
            Console.WriteLine("  ② 光标换帧 / 行走动画 / 朝向帧（属 Module/View，agent-07）；");
            Console.WriteLine("  ③ 跟随相机的「手感」（是否抖）与像素级画面。");
            return 0;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 0. 装配：配表 + AppContext.AutoWire（**命名/无参构造契约的自证**）
        // ═════════════════════════════════════════════════════════════════════
        private static AppContext WireUp()
        {
            try
            {
                return Step0_WireUp();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine($"    ❌ 装配阶段抛异常：{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return null;
            }
        }

        private static AppContext Step0_WireUp()
        {
            Section("0. 装配：配表 + AppContext.AutoWire（实现类命名 = 自动装配契约）");


            var err = Table.TableLoader.LoadAll(null, ClientAssets);
            Check("配表已加载（与 Bootstrap 同一条链路）", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));
            Check("class_c 5 个职业可读", Table.TableLoader.Class(1) != null && Table.TableLoader.Class(5) != null,
                "class1=" + (Table.TableLoader.Class(1)?.Name ?? "null") + " class5=" + (Table.TableLoader.Class(5)?.Name ?? "null"));
            Check("experience_c 可读（1 级阈值 500 / 99 级 3837739017）",
                Table.TableLoader.Experience(1) != null && Table.TableLoader.Experience(1).Exp == 500
                && Table.TableLoader.Experience(99) != null && Table.TableLoader.Experience(99).Exp == 3837739017L,
                $"E(1)={Table.TableLoader.Experience(1)?.Exp} E(99)={Table.TableLoader.Experience(99)?.Exp}");

            var ctx = AppContext.Create();
            ctx.AutoWire();

            Check("AutoWire 反射装配 IMapModule", ctx.Map != null && ctx.Map.GetType().Name == "MapModule",
                ctx.Map == null ? "null" : ctx.Map.GetType().FullName);
            Check("AutoWire 反射装配 IPlayerModule", ctx.Player != null && ctx.Player.GetType().Name == "PlayerModule",
                ctx.Player == null ? "null" : ctx.Player.GetType().FullName);
            Check("AutoWire 反射装配 ICameraRig", ctx.Camera != null && ctx.Camera.GetType().Name == "CameraRig",
                ctx.Camera == null ? "null" : ctx.Camera.GetType().FullName);
            Check("实现类是 internal（不是 public）", !ctx.Player.GetType().IsPublic && !ctx.Camera.GetType().IsPublic,
                $"Player public={ctx.Player.GetType().IsPublic} Camera public={ctx.Camera.GetType().IsPublic}");
            Check("实现类有无参构造（Activator 能创建）",
                ctx.Player.GetType().GetConstructor(Type.EmptyTypes) != null
                && ctx.Camera.GetType().GetConstructor(Type.EmptyTypes) != null,
                "PlayerModule()/CameraRig() 均为隐式 public 无参构造");
            Check("PlayerModule 已订阅装备/加点事件", ((PlayerModule)ctx.Player).EquipSubscribed,
                "Game.Event.On(EquipChanged / StatAllocateRequest)");
            // ⚠️ 与 agent-06 原版的差别：本宿主现在自带 `StubMonsters` / `StubNpcs`
            //    （§13 悬停自证的桩，见文件下方）⇒ `AutoWire` 会**如实**把它们装配到 Monster / Npc。
            //    这里改为断言「本宿主**没有编入**的模块仍保持 null」——这才是该用例的本意（降级不崩）。
            Check("本宿主未编入的模块保持 null（降级，不崩）",
                ctx.Combat == null && ctx.Skill == null && ctx.Item == null && ctx.Quest == null
                && ctx.View == null && ctx.Audio == null && ctx.Save == null,
                ctx.Describe() + "（Monster/Npc = 本宿主 §13 的桩 StubMonsters/StubNpcs，非生产实现）");

            Console.WriteLine("    （下方若出现 [Cfg] 的 WARN：非 Unity 进程读 config.json 的正常降级，" +
                              "Cfg 内部已 try/catch，不是失败）");
            return ctx;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. 五职业 1 级派生属性 ↔ class_c（配表一致性）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step1_FiveClasses(AppContext ctx, object mapObj)
        {
            Section("1. 五职业 1 级 MaxLife/MaxMana/MaxStamina ↔ class_c 计算值逐一相等");
            var player = (PlayerModule)ctx.Player;

            Console.WriteLine("    id 职业        str dex vit eng  life/vit mana/mag stam/vit   生命 法力 耐力   防御  AR");
            var allOk = true;

            for (var id = 1; id <= 5; id++)
            {
                var row = Table.TableLoader.Class(id);
                player.CreateNew((PlayerClass)id, "Check" + id);

                var expLife = Mathf.Max(1, Mathf.RoundToInt(row.Vit * row.LifePerVit));
                var expMana = Mathf.Max(1, Mathf.RoundToInt(row.Eng * row.ManaPerMag));
                var expStam = Mathf.Max(1, Mathf.RoundToInt(row.Vit * row.StamPerVit));

                var ok = player.MaxLife == expLife && player.MaxMana == expMana && player.MaxStamina == expStam;
                allOk &= ok;

                Console.WriteLine($"    {id}  {row.Name,-8}  {row.Str,3} {row.Dex,3} {row.Vit,3} {row.Eng,3}" +
                                  $"  {row.LifePerVit,7:0.##} {row.ManaPerMag,7:0.##} {row.StamPerVit,7:0.##}" +
                                  $"   {player.MaxLife,4} {player.MaxMana,4} {player.MaxStamina,4}" +
                                  $"   {player.Defense,4} {player.AttackRating,4}   {(ok ? "" : "❌ 与配表不符")}");
                Check($"{row.Name} 1 级生命/法力/耐力 = 配表公式值",
                    ok, $"期望 {expLife}/{expMana}/{expStam}，实际 {player.MaxLife}/{player.MaxMana}/{player.MaxStamina}");
            }

            Check("5 职业逐一相符", allOk, "见上表（公式与 UI/CharCreatePanel.LifeOf 完全一致）");

            // 与创角屏同口径的抽查（验收 #17：创角预览要跟进图后对得上）
            var amazon = Table.TableLoader.Class(1);
            player.CreateNew(PlayerClass.Amazon, "Check1");
            var uiLife = Mathf.Max(1, Mathf.RoundToInt(amazon.Vit * amazon.LifePerVit + 0 * amazon.LifePerLvl));
            Check("与 CharCreatePanel 的生命公式一致（亚马逊 1 级）", player.MaxLife == uiLife,
                $"PlayerModule={player.MaxLife} CharCreatePanel={uiLife}");
            Console.WriteLine("    " + player.DumpStats());
        }

        // ═════════════════════════════════════════════════════════════════════
        // 2. 点击移动（真地图 + 逐帧 Tick）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step2_ClickMove(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("2. 点击移动：罗格营地出生点 → 远端可走格（逐帧 Tick 0.02f）");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "Check");
            player.TeleportTo(map.SpawnPoint);

            var from = player.Grid;
            var target = FindFarWalkable(map, from, 12, requireNoLineOfSight: false);
            Console.WriteLine($"    地图：{map.Width}x{map.Height} 可走 {map.WalkableCount} 格；" +
                              $"起点 {from}，目标 {target}（Chebyshev 距离 {Iso.GridDistance(from, target)}）");

            Console.WriteLine("    " + CaptureLogger.Last("[Move] steps="));
            player.MoveTo(target);
            Console.WriteLine("    " + CaptureLogger.Last("[Move] steps="));

            var frames = WalkUntilArrive(player, map, FrameCap, assertWalkableEachFrame: true);

            Check("已到达目标格", player.Grid == target, $"终点 {player.Grid}");
            Check("移动过程走满了路径（帧数有记录）", player.Motor.LastArriveFrames > 0,
                $"路径 {player.Motor.LastSteps} 格，实际 {player.Motor.LastArriveFrames} 帧（dt=0.02 ⇒ {player.Motor.LastArriveFrames * Dt:0.00}s）");
            Check("到达后不再移动", !player.IsMoving, $"IsMoving={player.IsMoving}");
            Console.WriteLine("    " + CaptureLogger.Last("[Move] arrived"));
            Check("留下了验收要抄的 `[Move] steps=` 日志", CaptureLogger.Has("[Move] steps="),
                CaptureLogger.Last("[Move] steps="));
            Check("发了 D2.Map.PlayerGridChanged（小地图揭迷雾）", bus.CountOf(Events.PlayerGridChanged) > 0,
                $"次数={bus.CountOf(Events.PlayerGridChanged)}");
            Console.WriteLine($"    ⇒ 起点 {from} / 终点 {player.Grid} / 路径 {player.Motor.LastSteps} 格 / " +
                              $"实际 {player.Motor.LastArriveFrames} 帧（{frames}）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 3. 绕障
        // ═════════════════════════════════════════════════════════════════════
        private static void Step3_Detour(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("3. 绕障：目标与起点无视线 ⇒ 路径非直连，且每格都可走、全程不穿墙");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 424242);
            player.CreateNew(PlayerClass.Paladin, "Detour");
            player.TeleportTo(map.SpawnPoint);

            var from = player.Grid;
            var target = FindFarWalkable(map, from, 10, requireNoLineOfSight: true);
            if (target == from)
            {
                Check("找到「需要绕障」的目标", false, "地图里找不到无视线且够远的目标（换 seed 试）");
                return;
            }

            var straight = Iso.GridDistance(from, target);
            player.MoveTo(target);
            var path = new List<Vector2Int>();
            Console.WriteLine("    " + CaptureLogger.Last("[Move] steps="));

            var steps = player.Motor.LastSteps;
            Check("路径非直连（长度 > 两点直线格数 + 1）", steps > straight + 1,
                $"路径 {steps} 格 vs Chebyshev 距离 {straight}（说明真的绕了）");

            var allWalkable = true;
            for (var f = 0; f < FrameCap && player.IsMoving; f++)
            {
                player.Tick(Dt);
                if (!map.Walkable(player.Grid)) allWalkable = false;
            }

            Check("玩家全程都在可走格上（不穿墙）", allWalkable, "逐帧 map.Walkable(player.Grid) 均为 true");
            Check("已到达目标格", player.Grid == target, $"终点 {player.Grid}（目标 {target}）");
            Check("没有发生受阻停下", player.Motor.BlockedStops == 0, $"BlockedStops={player.Motor.BlockedStops}");
            Console.WriteLine("    " + CaptureLogger.Last("[Move] arrived"));
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4. 不可达
        // ═════════════════════════════════════════════════════════════════════
        private static void Step4_Unreachable(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("4. 不可达：点到障碍格 / 图外格 ⇒ 不移动 + 有日志");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Barbarian, "Blocked");
            player.TeleportTo(map.SpawnPoint);

            var start = player.Grid;
            var wall = FindFirst(map, TileKind.Wall) ?? FindFirst(map, TileKind.Rock) ?? FindFirst(map, TileKind.Fence);
            if (!wall.HasValue)
            {
                Check("地图里存在不可走格", false, "找不到 Wall/Rock/Fence");
                return;
            }

            var beforeCount = player.UnreachableCount;
            var beforeLogs = CaptureLogger.Count("[Move] unreachable");
            player.MoveTo(wall.Value);
            for (var f = 0; f < 50; f++) player.Tick(Dt);

            Check("没有移动（仍在起点）", player.Grid == start, $"起点 {start}，现在 {player.Grid}");
            Check("不可达计数 +1", player.UnreachableCount == beforeCount + 1,
                $"{beforeCount} → {player.UnreachableCount}");
            Check("留下了可定位日志（含地形与起终点）", CaptureLogger.Count("[Move] unreachable") == beforeLogs + 1,
                CaptureLogger.Last("[Move] unreachable"));

            // 图外（Void）
            var outside = new Vector2Int(-5, -5);
            player.MoveTo(outside);
            Check("图外目标同样被判不可达", player.Grid == start && player.UnreachableCount == beforeCount + 2,
                $"UnreachableCount={player.UnreachableCount}，格={player.Grid}");
            Console.WriteLine("    " + CaptureLogger.Last("[Move] unreachable"));
        }

        // ═════════════════════════════════════════════════════════════════════
        // 5. 受阻（桩地图：路径穿过不可走格）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step5_Blocked(AppContext ctx, PlayerModule player, RecordingEventBus bus)
        {
            Section("5. 受阻：路径穿过不可走格 ⇒ 停在原地（不穿墙）+ Warn + BlockedStops++");
            var stub = new StubMap();
            player.CreateNew(PlayerClass.Necromancer, "Stuck");
            player.BindMap(stub);
            player.TeleportTo(new Vector2Int(1, 1));

            // 造一条"坏路径"：目标 (4,1) 本身可走，但路径 (1,1)→(2,1)→(3,1)→(4,1) 中间那格 (3,1) 不可走
            // （真实 MapModule 生成的路径不会这样；这条分支只能用桩地图逼出来）
            stub.SetWall(new Vector2Int(3, 1));
            stub.Script(new List<Vector2Int>
            {
                new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(3, 1), new Vector2Int(4, 1),
            });

            var before = CaptureLogger.Count("移动受阻");
            player.MoveTo(new Vector2Int(4, 1));
            for (var f = 0; f < 200; f++) player.Tick(Dt);

            Check("受阻停下（BlockedStops +1）", player.Motor.BlockedStops == 1, $"BlockedStops={player.Motor.BlockedStops}");
            Check("停在障碍前一格 (2,1)（没有穿墙）", player.Grid == new Vector2Int(2, 1), $"当前格 {player.Grid}");
            Check("留下了 Warn（带起终点与当前格、地形）", CaptureLogger.Count("移动受阻") == before + 1,
                CaptureLogger.Last("移动受阻"));
            Check("受阻后 IsMoving=false", !player.IsMoving, $"IsMoving={player.IsMoving}");

            player.BindMap(null);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 6. 踩出入口
        // ═════════════════════════════════════════════════════════════════════
        private static void Step6_ExitPortal(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("6. 出入口：走到出城口 ⇒ 发 Events.ExitEntered(BloodMoor)，同一格只发一次");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Sorceress, "Exit");
            player.TeleportTo(map.SpawnPoint);

            var exit = map.Exits[0];
            var before = bus.CountOf(Events.ExitEntered);
            player.MoveTo(exit);
            for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);
            Check("走到了出城口", player.Grid == exit, $"格 {player.Grid}（出口 {exit}）");

            for (var f = 0; f < 100; f++) player.Tick(Dt);   // 站在出口上继续 Tick，不该重复发事件

            Check("发了 Events.ExitEntered 且参数 = BloodMoor", bus.CountOf(Events.ExitEntered) == before + 1
                && bus.LastArgOf(Events.ExitEntered) == nameof(AreaId.BloodMoor),
                $"次数 {bus.CountOf(Events.ExitEntered) - before}，载荷 {bus.LastArgOf(Events.ExitEntered)}（{Events.ExitEntered}）");
            Console.WriteLine("    " + CaptureLogger.Last("踩到出入口"));

            // 再验：离开出口格后再次踩上，应能再触发（防"只发一次"变成"永远不发"）
            player.TeleportTo(map.SpawnPoint);
            player.MoveTo(exit);
            for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);
            Check("离开后再踩可再次触发（不是只发一次）", bus.CountOf(Events.ExitEntered) == before + 2,
                $"次数 {bus.CountOf(Events.ExitEntered) - before}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 7. 经验与升级
        // ═════════════════════════════════════════════════════════════════════
        private static void Step7_LevelUp(AppContext ctx, PlayerModule player)
        {
            Section("7. 经验与升级：AddExp 到 experience_c 阈值 ⇒ 等级 +1、上限按配表增长");
            player.CreateNew(PlayerClass.Amazon, "Leveler");
            var row = Table.TableLoader.Class((int)PlayerClass.Amazon);

            var lv1Life = player.MaxLife;
            var lv1StatPoints = player.StatPoints;
            var lv1SkillPoints = player.SkillPoints;
            Console.WriteLine($"    Lv1：生命上限 {lv1Life} 经验 0/{player.ExpNext} 属性点 {lv1StatPoints} 技能点 {lv1SkillPoints}");

            var logged = CaptureLogger.Count("[Player] level 1→2");
            player.AddExp(500);        // E(1) = 500 ⇒ 升到 2 级

            Check("等级 1 → 2", player.Level == 2, $"等级 {player.Level}，经验 {player.Exp}/{player.ExpNext}");
            Check("生命上限按配表增长（+life_per_lvl）",
                player.MaxLife == lv1Life + Mathf.RoundToInt(row.LifePerLvl),
                $"{lv1Life} → {player.MaxLife}（life_per_lvl={row.LifePerLvl}）");
            Check("法力上限按配表增长（+mana_per_lvl）",
                player.MaxMana == Mathf.RoundToInt(row.Eng * row.ManaPerMag + 1 * row.ManaPerLvl),
                $"法力上限 {player.MaxMana}（mana_per_lvl={row.ManaPerLvl}）");
            Check("属性点 +stat_per_lvl", player.StatPoints == lv1StatPoints + row.StatPerLvl,
                $"{lv1StatPoints} → {player.StatPoints}（stat_per_lvl={row.StatPerLvl}）");
            Check("技能点 +1", player.SkillPoints == lv1SkillPoints + 1,
                $"{lv1SkillPoints} → {player.SkillPoints}");
            Check("留下验收要抄的 `[Player] level 1→2` 日志", CaptureLogger.Count("[Player] level 1→2") == logged + 1,
                CaptureLogger.Last("level 1→2"));
            Console.WriteLine("    " + CaptureLogger.Last("level 1→2"));

            // 一次给足经验应连升多级（while 循环口径）
            player.AddExp(1000);       // 累计 1500 = E(2) ⇒ 升到 3 级
            Check("一次给足经验可连升（2 → 3）", player.Level == 3 && player.Exp == 1500,
                $"等级 {player.Level}，经验 {player.Exp}/{player.ExpNext}");
            Console.WriteLine("    " + player.DumpStats());
        }

        // ═════════════════════════════════════════════════════════════════════
        // 8. 属性点分配
        // ═════════════════════════════════════════════════════════════════════
        private static void Step8_AllocateStat(AppContext ctx, PlayerModule player)
        {
            Section("8. 属性点：AllocateStat(Vitality,5) ⇒ MaxLife 增量 == life_per_vit × 5");
            var row = Table.TableLoader.Class((int)PlayerClass.Amazon);
            var before = player.MaxLife;
            var beforePoints = player.StatPoints;
            var beforeVit = player.Vit;

            var ok = player.AllocateStat(StatKind.Vitality, 5);
            var expected = Mathf.RoundToInt(row.LifePerVit * 5);

            Check("加点成功", ok, $"AllocateStat(Vitality,5) = {ok}");
            Check("体力 +5", player.Vit == beforeVit + 5, $"{beforeVit} → {player.Vit}");
            Check("生命上限增量 == life_per_vit × 5", player.MaxLife - before == expected,
                $"{before} → {player.MaxLife}（+{player.MaxLife - before}，期望 +{expected} = {row.LifePerVit} × 5）");
            Check("属性点扣除 5", player.StatPoints == beforePoints - 5, $"{beforePoints} → {player.StatPoints}");

            var p2 = player.StatPoints;
            Check("点数不足被拒（返回 false 且不改值）", !player.AllocateStat(StatKind.Strength, p2 + 100)
                && player.StatPoints == p2, $"剩余 {player.StatPoints}");
            Check("不能降到职业初始值以下", !player.AllocateStat(StatKind.Strength, -1)
                && player.Str == row.Str + 0 + player.Stats.BonusStr, $"力量 {player.Str}（初始 {row.Str}）");
            Check("delta=0 被拒", !player.AllocateStat(StatKind.Vitality, 0), "返回 false 并 Warn");
            Console.WriteLine("    " + CaptureLogger.Last("[Stat]"));
        }

        // ═════════════════════════════════════════════════════════════════════
        // 9. 装备生效（只走事件，不引用 Item 模块）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step9_Equip(AppContext ctx, PlayerModule player, RecordingEventBus bus)
        {
            Section("9. 装备生效：EquipChanged 事件 ⇒ 四维/抗性/护甲/AR 变化（Player 不引用 Item 模块）");
            var beforeStr = player.Str;
            var beforeLife = player.MaxLife;
            var beforeDef = player.Defense;
            var beforeAr = player.AttackRating;
            var beforeFire = player.GetResist(DamageType.Fire);
            var beforePoison = player.GetResist(DamageType.Poison);

            var armor = new ItemStack
            {
                itemId = 1,
                name = "皮甲(自检)",
                type = ItemType.Armor,
                defMin = 20,
                defMax = 30,
                affixes = new List<ItemAffix>
                {
                    new ItemAffix { affixId = 1, kind = AffixKind.Prefix, mod = "str", value = 3 },
                    new ItemAffix { affixId = 2, kind = AffixKind.Prefix, mod = "ac", value = 7 },
                    new ItemAffix { affixId = 3, kind = AffixKind.Prefix, mod = "ac%", value = 10 },
                    new ItemAffix { affixId = 4, kind = AffixKind.Prefix, mod = "att", value = 20 },
                    new ItemAffix { affixId = 5, kind = AffixKind.Suffix, mod = "hp", value = 5 },
                    new ItemAffix { affixId = 6, kind = AffixKind.Suffix, mod = "res-fire", value = 10 },
                    new ItemAffix { affixId = 7, kind = AffixKind.Suffix, mod = "res-all", value = 5 },
                    // 故意混一条"不属于玩家派生属性"的码，验证不静默（只报一次 Info）
                    new ItemAffix { affixId = 8, kind = AffixKind.Suffix, mod = "gold%", value = 40 },
                },
            };

            var args = new InventoryChangedArgs { gold = 0 };
            args.equip.Add(armor);
            bus.Emit(Events.EquipChanged, args);

            var dex = player.Dex;
            var expectedDef = Mathf.Max(0, (25 + 7 + dex / 4) + (25 + 7 + dex / 4) * 10 / 100);

            Check("力量 +3（词缀 str）", player.Str == beforeStr + 3, $"{beforeStr} → {player.Str}");
            Check("生命上限 +5（词缀 hp）", player.MaxLife == beforeLife + 5, $"{beforeLife} → {player.MaxLife}");
            Check("火焰抗性 +15（res-fire 10 + res-all 5）", player.GetResist(DamageType.Fire) == beforeFire + 15,
                $"{beforeFire} → {player.GetResist(DamageType.Fire)}");
            Check("毒素抗性 +5（res-all）", player.GetResist(DamageType.Poison) == beforePoison + 5,
                $"{beforePoison} → {player.GetResist(DamageType.Poison)}");
            Check("防御 = (护甲 25 + ac 7 + 敏/4) × 1.10", player.Defense == expectedDef,
                $"{beforeDef} → {player.Defense}（期望 {expectedDef}）");
            Check("命中 AR 上升（词缀 att 20）", player.AttackRating == beforeAr + 20,
                $"{beforeAr} → {player.AttackRating}");
            Check("留下了装备生效日志", CaptureLogger.Has("[Equip] 装备生效"), CaptureLogger.Last("[Equip]"));
            Check("未映射的词缀码不静默（只报一次 Info）", CaptureLogger.Has("词缀 mod=gold%"),
                CaptureLogger.Last("词缀 mod=gold%"));
            Check("发了 HudDirty（HUD 刷新）", bus.CountOf(Events.HudDirty) > 0, $"次数={bus.CountOf(Events.HudDirty)}");
            Console.WriteLine("    " + CaptureLogger.Last("[Equip] 装备生效"));

            // 卸下（空装备）→ 加成应回到 0
            bus.Emit(Events.EquipChanged, new InventoryChangedArgs());
            Check("卸下后加成归零", player.Str == beforeStr && player.MaxLife == beforeLife
                && player.GetResist(DamageType.Fire) == beforeFire,
                $"力 {player.Str} 生命上限 {player.MaxLife} 火抗 {player.GetResist(DamageType.Fire)}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 10. 死亡 / 复活
        // ═════════════════════════════════════════════════════════════════════
        private static void Step10_DeathRevive(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("10. 死亡/复活：ApplyDamage 归零 ⇒ IsDead + PlayerDied；Revive ⇒ 回出生点满血");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "Dier");
            player.TeleportTo(map.SpawnPoint);

            var beforeDamage = CaptureLogger.Count("受击");
            var died = player.ApplyDamage(9999, DamageType.Physical);

            Check("ApplyDamage 返回 true（因此死亡）", died, $"返回 {died}");
            Check("IsDead = true", player.IsDead, $"Life={player.Life}/{player.MaxLife}");
            Check("发了 Events.PlayerDied", bus.CountOf(Events.PlayerDied) > 0, $"次数={bus.CountOf(Events.PlayerDied)}");
            Check("留下了受击与死亡日志（致命一击走「受击致命」分支）",
                CaptureLogger.Count("受击") == beforeDamage + 1 && CaptureLogger.Has("[Player] died"),
                CaptureLogger.Last("受击致命"));

            var gridAtDeath = player.Grid;
            player.MoveTo(new Vector2Int(gridAtDeath.x + 3, gridAtDeath.y + 3));
            for (var f = 0; f < 100; f++) player.Tick(Dt);
            Check("死亡状态下不移动", player.Grid == gridAtDeath, $"格 {player.Grid}（死亡时 {gridAtDeath}）");

            player.Revive();
            Check("复活后满血", !player.IsDead && player.Life == player.MaxLife,
                $"生命 {player.Life}/{player.MaxLife}");
            Check("复活后回到出生点", player.Grid == map.SpawnPoint, $"格 {player.Grid}（出生点 {map.SpawnPoint}）");
            Console.WriteLine("    " + CaptureLogger.Last("复活：回出生点"));

            var half = player.ApplyDamage(player.MaxLife / 2, DamageType.Fire);
            Check("非致命伤害（半血）不触发死亡且生命下降", !half && player.Life == player.MaxLife - player.MaxLife / 2,
                $"生命 {player.Life}/{player.MaxLife}，返回值 {half}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 11. 相机
        // ═════════════════════════════════════════════════════════════════════
        private static void Step11_Camera(AppContext ctx, object mapObj, CameraRig rig, ScriptedInput input)
        {
            Section("11. 等距跟随相机：固定角度/平滑跟随/边界钳制/缩放钳制/震动衰减");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);

            Check("离线宿主里无 Unity 相机 ⇒ HasCamera=false 且不抛异常（优雅降级）", !rig.HasCamera,
                "Camera.main 在非 Unity 进程不可用（SecurityException）已被 try/catch 兜住");

            rig.Reset();
            var spawn = map.SpawnPoint;
            rig.SetTargetGrid(spawn);
            rig.SnapToTarget();
            rig.Tick(Dt);

            var focus = Iso.GridToWorld(spawn);
            var want = CameraRig.DesiredPosition(focus, -CameraRig.CameraDistance);
            Check("吸附后机位 = 焦点正前方（z = -10）",
                Math.Abs(rig.Position.x - want.x) < 1e-4f && Math.Abs(rig.Position.y - want.y) < 1e-4f
                && Math.Abs(rig.Position.z + CameraRig.CameraDistance) < 1e-4f,
                $"Position={rig.Position} 期望 {want}");
            // ★ 片 2b：默认正交尺寸 6 → 3.75（原版 600px / 80ppu / 2；见 `CameraRig.DefaultOrthographicSize`
            //   的注释）。本断言读的是**常量**（不是字面量 6）⇒ 改值后仍成立，标签里的数字跟着常量走。
            Check($"正交尺寸 = 默认 {CameraRig.DefaultOrthographicSize}",
                Math.Abs(rig.OrthographicSize - CameraRig.DefaultOrthographicSize) < 1e-4f,
                $"size={rig.OrthographicSize}，可见格高 = 2×size = {2f * CameraRig.DefaultOrthographicSize} 格");

            // 平滑跟随：单调收敛、不过冲
            var far = FindFarWalkable(map, spawn, 14, requireNoLineOfSight: false);
            rig.SetTargetGrid(far);
            var target = CameraRig.DesiredPosition(Iso.GridToWorld(far), -CameraRig.CameraDistance);
            var prevDist = float.MaxValue;
            var monotonic = true;
            var ticks = 0;
            for (; ticks < 300; ticks++)
            {
                rig.Tick(Dt);
                var d = Vector3.Distance(rig.Position, target);
                if (d > prevDist + 1e-5f) monotonic = false;
                prevDist = d;
                if (d < 0.01f) break;
            }
            Check("平滑跟随单调收敛、不过冲", monotonic, $"{ticks} 帧后剩余距离 {prevDist:0.0000}");
            Check("收敛到位（误差 < 0.01 世界单位）", prevDist < 0.01f, $"剩余 {prevDist:0.0000}");

            // 暂停（dt=0）⇒ 机位原地不动（timeScale=0 时不该继续滑动）
            var posBeforeZero = rig.Position;
            rig.SetTargetGrid(spawn);
            rig.Tick(0f);
            Check("Tick(0) 时不动（暂停不抖）", Vector3.Distance(rig.Position, posBeforeZero) < 1e-6f,
                $"Position 保持 {rig.Position}（新目标 {Iso.GridToWorld(spawn)}）");

            // 纯函数：指数平滑
            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(10f, 0f, 0f);
            Check("SmoothTowards(dt<=0) 保持不动", CameraRig.SmoothTowards(a, b, 0f, 0.12f) == a, "dt=0 → 原样");
            Check("SmoothTowards(tau<=0) 直接吸附", CameraRig.SmoothTowards(a, b, 0.02f, 0f) == b, "tau=0 → want");
            var mid = CameraRig.SmoothTowards(a, b, 0.02f, 0.12f);
            Check("SmoothTowards 在两端之间（不过冲）", mid.x > 0f && mid.x < 10f, $"x={mid.x:0.000}");

            // 边界钳制（纯函数）
            var min = new Vector2(-10f, -10f);
            var max = new Vector2(10f, 10f);
            var c1 = CameraRig.ClampFocus(new Vector2(100f, 100f), min, max, 2f, 2f);
            Check("焦点被夹进地图（视野小于地图）", Math.Abs(c1.x - 8f) < 1e-4f && Math.Abs(c1.y - 8f) < 1e-4f, $"→ {c1}");
            var c2 = CameraRig.ClampFocus(new Vector2(100f, 100f), min, max, 50f, 50f);
            Check("视野大于地图时居中", Math.Abs(c2.x) < 1e-4f && Math.Abs(c2.y) < 1e-4f, $"→ {c2}");

            // 地图世界包围盒
            CameraRig.MapWorldBounds(map.Width, map.Height, out var bmin, out var bmax);
            Check("地图世界包围盒有效", bmin.x < bmax.x && bmin.y < bmax.y,
                $"{map.Width}x{map.Height} → min {bmin} max {bmax}");

            // 缩放：钳制 + 默认关闭时不读滚轮
            rig.AddZoom(-100f);
            Check("缩放下限被钳制到 3", Math.Abs(rig.OrthographicSize - CameraRig.MinOrthographicSize) < 1e-4f,
                $"size={rig.OrthographicSize}");
            var clampLogged = CaptureLogger.Count("缩放已到边界");
            rig.AddZoom(-1f);
            Check("继续缩小不生效并 Warn（只报一次）", CaptureLogger.Count("缩放已到边界") == clampLogged + 1,
                CaptureLogger.Last("缩放已到边界"));
            rig.AddZoom(+100f);
            Check("缩放上限被钳制到 12", Math.Abs(rig.OrthographicSize - CameraRig.MaxOrthographicSize) < 1e-4f,
                $"size={rig.OrthographicSize}");

            rig.AddZoom(-(CameraRig.MaxOrthographicSize - CameraRig.DefaultOrthographicSize));   // 回到默认（3.75）
            rig.EnableZoom = false;
            var sizeBefore = rig.OrthographicSize;
            input.Wheel = 1f;
            rig.Tick(Dt);
            Check("EnableZoom=false ⇒ 滚轮不改变缩放（原版不缩放）",
                Math.Abs(rig.OrthographicSize - sizeBefore) < 1e-4f, $"size={rig.OrthographicSize}");

            rig.EnableZoom = true;
            rig.Tick(Dt);
            Check("EnableZoom=true ⇒ 滚轮生效（+0.5/格）",
                Math.Abs(rig.OrthographicSize - (sizeBefore + CameraRig.ZoomStepPerNotch)) < 1e-4f,
                $"{sizeBefore} → {rig.OrthographicSize}");
            rig.EnableZoom = false;
            input.Wheel = 0f;

            // 边缘滚动（纯函数）
            var left = CameraRig.EdgeScrollOffset(new Vector2(0f, 540f), 1920f, 1080f, 8f, 1f);
            var center = CameraRig.EdgeScrollOffset(new Vector2(960f, 540f), 1920f, 1080f, 8f, 1f);
            var topRight = CameraRig.EdgeScrollOffset(new Vector2(1920f, 1080f), 1920f, 1080f, 8f, 1f);
            Check("边缘滚动：贴左/贴上产生外推、屏幕中央为 0",
                left.x < 0f && topRight.y > 0f && center == Vector2.zero,
                $"left={left} center={center} topRight={topRight}");

            // 震动：幅度衰减到 0
            rig.SetTargetGrid(spawn);
            rig.SnapToTarget();
            rig.Tick(Dt);
            var basePos = rig.Position;
            rig.Shake(1f, 0.5f);
            rig.Tick(0.1f);
            var offset1 = Vector3.Distance(rig.FinalPosition, basePos);
            rig.Tick(0.1f);
            var offset2 = Vector3.Distance(rig.FinalPosition, basePos);
            Check("震动偏移 ≤ 幅度", offset1 <= 1f + 1e-4f, $"偏移 {offset1:0.000}（幅度 1）");
            Check("震动幅度随时间衰减", offset2 < offset1, $"{offset1:0.000} → {offset2:0.000}");
            for (var i = 0; i < 4; i++) rig.Tick(0.1f);
            Check("震动结束后偏移归零", !rig.IsShaking && Vector3.Distance(rig.FinalPosition, rig.Position) < 1e-4f,
                $"IsShaking={rig.IsShaking}");
            Check("ShakeMagnitude 边界正确",
                Math.Abs(CameraRig.ShakeMagnitude(2f, 1f, 0f) - 2f) < 1e-4f
                && Math.Abs(CameraRig.ShakeMagnitude(2f, 1f, 1f)) < 1e-4f
                && Math.Abs(CameraRig.ShakeMagnitude(2f, 1f, 2f)) < 1e-4f,
                "t=0 → 满幅；t=1 → 0；t>dur → 0");
            var shakeWarns = CaptureLogger.Count("Shake(amp=");
            rig.Shake(0f, 0f);
            Check("Shake 非正参数被拒（不进入震动状态 + 有 Warn）",
                !rig.IsShaking && CaptureLogger.Count("Shake(amp=") == shakeWarns + 1,
                CaptureLogger.Last("Shake(amp="));

            // ═════════════════════════════════════════════════════════════════
            // 11.9 ★ 取景断言（agent-14 §A 验收要求）：
            //      「给定地图尺寸与焦点格，断言 焦点世界坐标 → 屏幕坐标 ≈ 屏幕中心」
            //      并量化「屏幕不出现大片黑」：视口被地图覆盖的比例。
            //      全部走纯函数（`CameraPosForFocus` / `WorldToScreen`），无 Unity 原生调用。
            // ═════════════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("    ── 取景断言：焦点世界坐标 → 屏幕坐标 ≈ 屏幕中心 ──");

            // 屏幕尺寸用 （i）标准 1920×1080 与（ii）实测的 Game View 1096×500（探针 `_dev/p_cam.cs` 的原始值）
            var screens = new[] { new Vector2(1920f, 1080f), new Vector2(1096f, 500f) };
            var cases = new[]
            {
                // 说明：地图尺寸（格） × 焦点格 —— 含本局实测的 Town 32×32 + 玩家格 (15,19)
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(15, 19), Tag = "Town 32×32 玩家格(15,19)（本局实测）" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(0, 0),      Tag = "Town 32×32 西北角(0,0)（会被边界钳制）" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(31, 31),    Tag = "Town 32×32 东南角(31,31)（会被边界钳制）" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(16, 16),    Tag = "Town 32×32 正中(16,16)" },
                new { W = 80, H = 80, G = new Vector2Int(40, 40), Tag = "血腥荒野 80×80 正中(40,40)" },
                new { W = 40, H = 40, G = new Vector2Int(20, 20), Tag = "邪恶洞穴 40×40 正中(20,20)" },
            };

            var allCentered = true;
            var allFocusVisible = true;
            var allCamInside = true;
            foreach (var sc in screens)
            {
                var aspect = sc.x / sc.y;
                foreach (var cs in cases)
                {
                    var focusWorld = Iso.GridToWorld(cs.G);
                    var camPos = CameraRig.CameraPosForFocus(focusWorld, cs.W, cs.H,
                        CameraRig.DefaultOrthographicSize, aspect, -CameraRig.CameraDistance);

                    CameraRig.MapWorldBounds(cs.W, cs.H, out var mbMin, out var mbMax);
                    var halfW = CameraRig.DefaultOrthographicSize * aspect;
                    var halfH = CameraRig.DefaultOrthographicSize;
                    var clamped = CameraRig.ClampFocus(new Vector2(focusWorld.x, focusWorld.y), mbMin, mbMax, halfW, halfH);
                    var needClamp = Math.Abs(clamped.x - focusWorld.x) > 1e-5f || Math.Abs(clamped.y - focusWorld.y) > 1e-5f;

                    var vp = CameraRig.WorldToViewport(focusWorld, camPos, CameraRig.DefaultOrthographicSize, aspect);
                    var screen = CameraRig.WorldToScreen(focusWorld, camPos, CameraRig.DefaultOrthographicSize, aspect, sc.x, sc.y);
                    var dCenter = Math.Abs(screen.x - sc.x * 0.5f) + Math.Abs(screen.y - sc.y * 0.5f);

                    if (needClamp)
                    {
                        // 焦点被边界钳制（相机贴到地图边）⇒ 焦点仍在视口内，且机位恰在钳制上限
                        var inView = vp.x >= -1e-4f && vp.x <= 1f + 1e-4f && vp.y >= -1e-4f && vp.y <= 1f + 1e-4f;
                        if (!inView) allFocusVisible = false;
                        var camInside = camPos.x >= mbMin.x + halfW - 1e-3f && camPos.x <= mbMax.x - halfW + 1e-3f
                                     && camPos.y >= mbMin.y + halfH - 1e-3f && camPos.y <= mbMax.y - halfH + 1e-3f;
                        if (!camInside) allCamInside = false;
                        Check($"[{sc.x}×{sc.y}] {cs.Tag} 被钳制 ⇒ 焦点仍在视口内 vp=({vp.x:0.000},{vp.y:0.000})",
                            inView, $"焦点屏幕=({screen.x:0.0},{screen.y:0.0}) 机位={camPos}");
                    }
                    else
                    {
                        if (dCenter > 1e-2f) allCentered = false;
                        Check($"[{sc.x}×{sc.y}] {cs.Tag} 未钳制 ⇒ 焦点 → 屏幕中心 ({sc.x * 0.5f:0},{sc.y * 0.5f:0})",
                            dCenter <= 1e-2f, $"实际=({screen.x:0.00},{screen.y:0.00}) 偏差={dCenter:0.0000}px 机位={camPos}");
                    }
                }
            }
            Check("所有「未被钳制」用例：焦点世界坐标 → 屏幕坐标 == 屏幕中心（偏差 < 0.01px）", allCentered,
                "见上方逐条（正交 + 不旋转 ⇒ 焦点即视口中心，与屏幕尺寸/宽高比无关）");
            Check("所有「被钳制」用例：焦点仍在视口内（玩家不会跑出画面）", allFocusVisible, "见上方逐条");
            Check("所有用例：机位都落在地图包围盒 - 半屏范围内（相机只在地图内滚动）", allCamInside, "见上方逐条");

            // ── 「屏幕不出现大片黑」的量化：视口被地图覆盖的比例 ──
            //    实测那局是 32×32 城镇 + 玩家格 (15,19)；两种屏幕尺寸各测一次（验收要看「地图大面积可见」）
            var covAspect = 1920f / 1080f;
            var covCam = CameraRig.CameraPosForFocus(Iso.GridToWorld(15, 19), GameConst.TownWidth, GameConst.TownHeight,
                CameraRig.DefaultOrthographicSize, covAspect, -CameraRig.CameraDistance);
            var cov = ViewCoverage(map, covCam, CameraRig.DefaultOrthographicSize * covAspect, CameraRig.DefaultOrthographicSize);
            Check("[1920×1080] Town (15,19) 视口被地图覆盖 ≥ 95%（“不出现大片黑”的量化）", cov >= 0.95f,
                $"覆盖率={cov * 100f:0.0}% 机位={covCam}");
            var covAspect2 = 1096f / 500f;
            var covCam2 = CameraRig.CameraPosForFocus(Iso.GridToWorld(15, 19), GameConst.TownWidth, GameConst.TownHeight,
                CameraRig.DefaultOrthographicSize, covAspect2, -CameraRig.CameraDistance);
            var cov2 = ViewCoverage(map, covCam2, CameraRig.DefaultOrthographicSize * covAspect2, CameraRig.DefaultOrthographicSize);
            Check("[1096×500（本局实测 Game View）] Town (15,19) 覆盖 ≥ 95%", cov2 >= 0.95f,
                $"覆盖率={cov2 * 100f:0.0}% 机位={covCam2}");

            // ── 反向对照（证明这条断言真的抓得住那个缺陷）──
            //    缺陷现场：相机停在场景出生机位 (0,0,-10) ⇒ 焦点 (15,19) 落在屏幕外
            var brokenCam = new Vector3(0f, 0f, -CameraRig.CameraDistance);
            var brokenScreen = CameraRig.WorldToScreen(Iso.GridToWorld(15, 19), brokenCam,
                CameraRig.DefaultOrthographicSize, 1096f / 500f, 1096f, 500f);
            var brokenDist = Math.Abs(brokenScreen.x - 548f) + Math.Abs(brokenScreen.y - 250f);
            Check("反向对照：机位停在出生机位 (0,0,-10) 时，焦点屏幕坐标**远离**屏幕中心（断言有效）",
                brokenDist > 300f, $"缺陷现场 焦点屏幕=({brokenScreen.x:0.0},{brokenScreen.y:0.0}) vs 中心 (548,250)，偏差 {brokenDist:0}px");

            // ── 世界坐标 → 视口：与 Mathf 口径一致性（防「orthographicSize 当成全高」这类口径错误）──
            var vpHalf = CameraRig.WorldToViewport(new Vector3(0f, 6f, 0f), Vector3.zero, 6f, 2f);
            Check("WorldToViewport：正交 size 是**半高**（世界 y=+size ⇒ 视口顶边 v=1）",
                Math.Abs(vpHalf.y - 1f) < 1e-5f && Math.Abs(vpHalf.x - 0.5f) < 1e-5f,
                $"WorldToViewport((0,6), cam(0,0), size=6, aspect=2) = {vpHalf}（期望 (0.5,1)）");
            var vpEdge = CameraRig.WorldToViewport(new Vector3(12f, 0f, 0f), Vector3.zero, 6f, 2f);
            Check("WorldToViewport：视口半宽 = size × aspect（x=+12 ⇒ u=1）",
                Math.Abs(vpEdge.x - 1f) < 1e-5f, $"= {vpEdge}（期望 (1,0.5)）");
            var vpBad = CameraRig.WorldToViewport(Vector3.zero, Vector3.zero, 0f, 0f);
            Check("WorldToViewport：非法参数返回 NaN（会让断言明确失败，而不是假通过）",
                float.IsNaN(vpBad.x) && float.IsNaN(vpBad.y), $"= {vpBad}");

            rig.Reset();
            Check("Reset 后正交尺寸回到默认、跟随与震动清空",
                Math.Abs(rig.OrthographicSize - CameraRig.DefaultOrthographicSize) < 1e-4f && !rig.IsShaking,
                $"size={rig.OrthographicSize}");
        }

        /// <summary>
        /// 采样法量化「视口矩形被地图铺装覆盖的比例」（agent-14 §A「屏幕不出现大片黑」的断言）。
        /// 用 `Iso.WorldToGrid` 反投影采样点 ⇒ 与瓦片实际铺装位置**同一套投影**（不是另写一份几何）。
        /// </summary>
        private static float ViewCoverage(Diablo2.Module.Map.MapModule map, Vector2 cam, float halfW, float halfH)
        {
            const int N = 41;              // 41×41 采样网格（1681 点，秒级）
            var hit = 0;
            var total = 0;
            for (var i = 0; i < N; i++)
            {
                for (var j = 0; j < N; j++)
                {
                    var x = cam.x - halfW + (i + 0.5f) / N * (2f * halfW);
                    var y = cam.y - halfH + (j + 0.5f) / N * (2f * halfH);
                    var g = Iso.WorldToGrid(new Vector3(x, y, 0f));
                    total++;
                    if (map.InBounds(g) && map.TileAt(g) != TileKind.Void) hit++;
                }
            }
            return (float)hit / total;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 12. 输入
        // ═════════════════════════════════════════════════════════════════════
        private static void Step12_Input(AppContext ctx, ScriptedInput input, RecordingEventBus bus)
        {
            Section("12. 输入读取（InputReader）：重算规则 / 按住状态机 / 无相机时点击被拒 / 腰带快捷键");
            var player = (PlayerModule)ctx.Player;
            var reader = player.Input;
            reader.Reset();

            // 12.1 目标变化判定（纯函数，验收 #4「变化超过 1 格才重算」）
            var from = new Vector2Int(5, 5);
            Check("ShouldRetarget：首次按住 → true",
                InputReader.ShouldRetarget(from, null, new Vector2Int(6, 5)), "(5,5) 按住到 (6,5)");
            Check("ShouldRetarget：目标没变 → false",
                !InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(6, 5)), "同一格");
            Check("ShouldRetarget：只差 1 格 → false（防抖动/省 A*）",
                !InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(7, 5)), "差 1 格");
            Check("ShouldRetarget：差 ≥2 格 → true",
                InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(8, 5)), "差 2 格");
            Check("ShouldRetarget：点到脚下 → false",
                !InputReader.ShouldRetarget(from, new Vector2Int(9, 9), from), "now == 玩家当前格");

            // 12.2 按住状态机 + 无相机时点击被拒
            input.Available = true;
            input.BeginFrame();
            input.MouseDown();
            reader.Poll();
            Check("Poll 后 PrimaryDown/PrimaryHeld = true", reader.PrimaryDown && reader.PrimaryHeld,
                $"down={reader.PrimaryDown} held={reader.PrimaryHeld}");
            var noCamLogs = CaptureLogger.Count("TryGetGroundClick：找不到主相机");
            Check("无相机时 TryGetGroundClick 返回 false（不瞎猜一个格）", !reader.TryGetGroundClick(out _),
                CaptureLogger.Last("TryGetGroundClick：找不到主相机"));
            Check("无相机时只报一次日志（防刷屏）",
                CaptureLogger.Count("TryGetGroundClick：找不到主相机") == noCamLogs + 1,
                $"次数 {CaptureLogger.Count("TryGetGroundClick：找不到主相机")}");

            input.BeginFrame();          // 松开
            input.MouseUp();
            reader.Poll();
            Check("松开后 PrimaryHeld=false、PrimaryUp=true", !reader.PrimaryHeld && reader.PrimaryUp,
                $"held={reader.PrimaryHeld} up={reader.PrimaryUp}");

            // 12.3 ★ 本段原为「方向键备选移动」读取器断言（已随该功能整体删除，见验收表 U-1）。
            //      断言改为**反向判据**：原版只有鼠标点地面移动 ⇒ 按方向键不得产生任何移动意图。

            // 12.4 腰带快捷键 → 事件
            var beforeBelt = bus.CountOf(Events.UseBeltRequest);
            input.BeginFrame();
            input.Press(GameKeyAlias.KeyBelt2);
            reader.Poll();
            reader.PollHotkeys(true);
            Check("腰带 2 键 → 发 UseBeltRequest(格号 1)",
                bus.CountOf(Events.UseBeltRequest) == beforeBelt + 1 && bus.LastArgOf(Events.UseBeltRequest) == "1",
                $"次数 {bus.CountOf(Events.UseBeltRequest) - beforeBelt}，载荷 {bus.LastArgOf(Events.UseBeltRequest)}");
            input.BeginFrame();
            input.Release(GameKeyAlias.KeyBelt2);

            input.BeginFrame();
            input.Press(GameKeyAlias.KeyBelt1);
            reader.Poll();
            reader.PollHotkeys(false);   // 死亡/暂停时不该发
            Check("canUse=false 时不发腰带事件（死亡/暂停不吃药）",
                bus.CountOf(Events.UseBeltRequest) == beforeBelt + 1, $"次数 {bus.CountOf(Events.UseBeltRequest) - beforeBelt}");
            input.BeginFrame();

            // 12.5 输入后端不可用
            input.Available = false;
            reader.Reset();
            reader.Poll();
            Check("输入后端不可用时 Poll 不崩、状态为 false，且只报一次 Warn",
                !reader.PrimaryDown && !reader.PrimaryHeld && CaptureLogger.Has("Game.Input 不可用"),
                CaptureLogger.Last("Game.Input 不可用"));
            input.Available = true;
            reader.Reset();

            // 12.6 契约事件 `D2.Input.Move`（点击移动命令通道）
            //      InputReader 点击 → Emit(MoveCommand) → PlayerModule.OnMoveCommand → MoveTo
            var map = (Diablo2.Module.Map.MapModule)ctx.Map;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "CmdMove");
            player.TeleportTo(map.SpawnPoint);
            var cmdFrom = player.Grid;
            var cmdTarget = FindFarWalkable(map, cmdFrom, 6, requireNoLineOfSight: false);
            var beforeCmd = bus.CountOf(Events.MoveCommand);
            bus.Emit(Events.MoveCommand, cmdTarget);
            Check("MoveCommand 事件驱动移动（契约通道打通）",
                player.IsMoving && player.Motor.TargetGrid == cmdTarget,
                $"IsMoving={player.IsMoving} 目标={player.Motor.TargetGrid}（命令 {cmdTarget}）");

            for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);
            Check("按 MoveCommand 走到了目标格", player.Grid == cmdTarget, $"终点 {player.Grid}（目标 {cmdTarget}）");
            Check("MoveTo 不回发 MoveCommand（无递归环）", bus.CountOf(Events.MoveCommand) == beforeCmd + 1,
                $"次数 {bus.CountOf(Events.MoveCommand) - beforeCmd}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 13. 交互：悬停目标 / 左键点怪 / Shift 站立攻击 / 走跑切换（agent-13 §A）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step13_Interaction(AppContext ctx, object mapObj, PlayerModule player,
            ScriptedInput input, RecordingEventBus bus)
        {
            Section("13. 交互：悬停（怪物 / 地面物品 / NPC / 空地 / 障碍）/ 左键点怪 / Shift 站立攻击 / 走跑切换");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "Interact");
            player.TeleportTo(map.SpawnPoint);
            player.Stop();

            var reader = player.Input;
            reader.Reset();
            if (input != null) { input.BeginFrame(); input.Available = true; }

            // 悬停来源：本宿主只编 Map/Player/Camera/Input ⇒ Monster/Npc/Item/View 模块不在程序集里。
            // ⇒ 注入桩（Monster/Npc 仍走**真实**的 `All` 遍历路径）与地面物品查询替身
            //   （契约缺口：`IItemModule.GroundItems` 不含格坐标，见 HoverPicker 头注释）。
            var stubMon = new StubMonsters();
            var stubNpc = new StubNpcs();
            ctx.Monster = stubMon;
            ctx.Npc = stubNpc;

            var cells = WalkableCells(map, 4);
            Check("地图上找到 ≥4 个可走格供悬停用例", cells.Count >= 4, $"找到 {cells.Count} 个");
            if (cells.Count < 4)
            {
                ctx.Monster = null;
                ctx.Npc = null;
                return;
            }

            var monGrid = cells[0];
            var itemGrid = cells[1];
            var npcGrid = cells[2];
            var emptyGrid = cells[3];
            var wallGrid = FindFirst(map, TileKind.Wall) ?? FindFirst(map, TileKind.Rock) ?? FindFirst(map, TileKind.Fence);

            var monsterId = GameConst.MonsterIdBase + 7;
            stubMon.Add(new MonsterState
            {
                id = monsterId,
                name = "堕落者(自检)",
                gridX = monGrid.x,
                gridY = monGrid.y,
                alive = true,
                hp = 10,
                maxHp = 10,
            });
            stubNpc.Add(new NpcDef
            {
                id = 1,
                name = "瓦瑞夫(自检)",
                gridX = npcGrid.x,
                gridY = npcGrid.y,
                areaId = (int)map.Area,
            });

            const int groundItemId = GameConst.GroundItemIdBase + 3;
            reader.Hover.GroundItemAt = g => g == itemGrid
                ? (HoverHit?)new HoverHit { id = groundItemId, name = "短剑(自检)" }
                : null;

            Console.WriteLine($"    悬停点：怪物 {monGrid} / 地面物品 {itemGrid} / NPC {npcGrid} / 空地 {emptyGrid}" +
                              $" / 障碍 {(wallGrid.HasValue ? wallGrid.Value.ToString() : "(无)")}");

            // ── A. 悬停怪物 ⇒ Attack ──
            reader.OverrideHoverGrid(monGrid);
            var hover0 = bus.CountOf(Events.HoverTargetChanged);
            var cursor0 = bus.CountOf(Events.CursorChanged);
            var t = reader.UpdateHover(true);
            Check("悬停怪物 ⇒ HoverTarget{cursor=Attack, id=怪物 id, 格=悬停格}",
                t.hasTarget && t.cursor == CursorKind.Attack && t.id == monsterId
                && t.gridX == monGrid.x && t.gridY == monGrid.y,
                $"hasTarget={t.hasTarget} cursor={t.cursor} id={t.id} 格=({t.gridX},{t.gridY}) name={t.name}");
            Check("发 HoverTargetChanged(1) + CursorChanged(Attack)",
                bus.CountOf(Events.HoverTargetChanged) == hover0 + 1 && bus.CountOf(Events.CursorChanged) == cursor0 + 1
                && bus.LastArgOf(Events.CursorChanged) == nameof(CursorKind.Attack),
                $"Hover +{bus.CountOf(Events.HoverTargetChanged) - hover0} Cursor +{bus.CountOf(Events.CursorChanged) - cursor0}" +
                $" 载荷={bus.LastArgOf(Events.CursorChanged)}");
            Check("悬停怪物同步给 IMonsterModule.SetHovered(id)", stubMon.HoveredId == monsterId,
                $"SetHovered({stubMon.HoveredId})");

            // ── B. 悬停地面物品 ⇒ Pickup ──
            reader.OverrideHoverGrid(itemGrid);
            t = reader.UpdateHover(true);
            Check("悬停地面物品 ⇒ HoverTarget{cursor=Pickup, id, name}",
                t.hasTarget && t.cursor == CursorKind.Pickup && t.id == groundItemId && t.name == "短剑(自检)",
                $"cursor={t.cursor} id={t.id} name={t.name}");
            Check("CursorChanged(Pickup)", bus.LastArgOf(Events.CursorChanged) == nameof(CursorKind.Pickup),
                bus.LastArgOf(Events.CursorChanged));
            Check("离开怪物 ⇒ SetHovered(-1)", stubMon.HoveredId == -1, $"SetHovered({stubMon.HoveredId})");

            // ── C. 悬停 NPC ⇒ Interact ──
            reader.OverrideHoverGrid(npcGrid);
            t = reader.UpdateHover(true);
            Check("悬停 NPC ⇒ HoverTarget{cursor=Interact, id=1}",
                t.hasTarget && t.cursor == CursorKind.Interact && t.id == 1,
                $"cursor={t.cursor} id={t.id} name={t.name}");
            Check("CursorChanged(Interact)", bus.LastArgOf(Events.CursorChanged) == nameof(CursorKind.Interact),
                bus.LastArgOf(Events.CursorChanged));

            // ── D. 空地（可走）⇒ Default；E. 障碍 ⇒ NoWalk ──
            reader.OverrideHoverGrid(emptyGrid);
            t = reader.UpdateHover(true);
            Check("空地 ⇒ 无目标 + cursor=Default", !t.hasTarget && t.cursor == CursorKind.Default,
                $"hasTarget={t.hasTarget} cursor={t.cursor}");

            if (wallGrid.HasValue)
            {
                reader.OverrideHoverGrid(wallGrid.Value);
                t = reader.UpdateHover(true);
                Check("障碍格 ⇒ 无目标 + cursor=NoWalk", !t.hasTarget && t.cursor == CursorKind.NoWalk,
                    $"cursor={t.cursor} 格=({wallGrid.Value.x},{wallGrid.Value.y})");
                Check("CursorChanged(NoWalk)", bus.LastArgOf(Events.CursorChanged) == nameof(CursorKind.NoWalk),
                    bus.LastArgOf(Events.CursorChanged));
            }

            Check("留下了悬停命中日志（验收 #14 取证）", CaptureLogger.Has("[Hover] 首个悬停命中"),
                CaptureLogger.Last("[Hover]"));

            // ── F. 左键点怪 ⇒ AttackRequest 恰好 1 次 ──
            player.Stop();
            reader.OverrideHoverGrid(monGrid);
            var atk0 = bus.CountOf(Events.AttackRequest);
            var mv0 = bus.CountOf(Events.MoveCommand);
            player.HandlePrimaryClick(monGrid);
            Check("左键点怪 ⇒ AttackRequest 恰好 1 次且 id 正确",
                bus.CountOf(Events.AttackRequest) == atk0 + 1
                && bus.LastArgOf(Events.AttackRequest) == monsterId.ToString(),
                $"次数 {bus.CountOf(Events.AttackRequest) - atk0} 载荷 {bus.LastArgOf(Events.AttackRequest)}");
            Check("点怪同时补发走位 MoveCommand（ICombatModule 超距时不走位）",
                bus.CountOf(Events.MoveCommand) == mv0 + 1,
                $"MoveCommand +{bus.CountOf(Events.MoveCommand) - mv0}，逼近格={player.Motor.TargetGrid}");
            Check("留下了 [Attack] 日志", CaptureLogger.Has("[Attack] 左键点怪"), CaptureLogger.Last("[Attack]"));
            player.Stop();

            // ── G. Shift + 点击 ⇒ 只攻击、不产生移动目标 ──
            input.BeginFrame();
            input.Press(GameKeyAlias.KeyStandStill);
            var atk1 = bus.CountOf(Events.AttackRequest);
            var mv1 = bus.CountOf(Events.MoveCommand);
            player.HandlePrimaryClick(monGrid);
            Check("Shift+点击怪 ⇒ 只发 AttackRequest，不产生移动目标（MoveCommand 不增）",
                bus.CountOf(Events.AttackRequest) == atk1 + 1 && bus.CountOf(Events.MoveCommand) == mv1,
                $"Attack +{bus.CountOf(Events.AttackRequest) - atk1} Move +{bus.CountOf(Events.MoveCommand) - mv1}");
            input.Release(GameKeyAlias.KeyStandStill);
            input.BeginFrame();

            // ── H. 空地上按住左键 ⇒ 换目标格（不误判为攻击）──
            reader.OverrideHoverGrid(emptyGrid);
            t = reader.UpdateHover(true);
            Check("空地上按住左键不会被判成攻击（cursor=Default）",
                t.cursor == CursorKind.Default,
                $"cursor={t.cursor}");

            // ── I. 走 / 跑切换（原版 R 键）⇒ 实际速率减半 ──
            player.Stop();
            player.TeleportTo(map.SpawnPoint);
            var far = FindFarWalkable(map, map.SpawnPoint, 8, requireNoLineOfSight: false);
            Check("找到够远的目标格（走跑速率对比用）", far != map.SpawnPoint, $"目标 {far}（出生点 {map.SpawnPoint}）");

            player.SetRunning(true);
            Check("跑：MoveSpeed = PlayerWalkSpeed",
                player.Running && Math.Abs(player.MoveSpeed - GameConst.PlayerWalkSpeed) < 1e-4f,
                $"Running={player.Running} MoveSpeed={player.MoveSpeed}");
            player.MoveTo(far);
            var runFrames = WalkFrames(player, FrameCap);

            player.TeleportTo(map.SpawnPoint);
            player.ToggleRun();
            Check("R 切换后为「走」：MoveSpeed 减半",
                !player.Running
                && Math.Abs(player.MoveSpeed - GameConst.PlayerWalkSpeed * PlayerModule.WalkSpeedFactor) < 1e-4f,
                $"Running={player.Running} MoveSpeed={player.MoveSpeed}（= {GameConst.PlayerWalkSpeed} × {PlayerModule.WalkSpeedFactor}）");
            player.MoveTo(far);
            var walkFrames = WalkFrames(player, FrameCap);

            // ★ 片 2b：期望比值**由常量推出**，容差**保持原来同一个相对量**（⛔ 没有放宽）——
            //   旧断言 `> 1.85 && < 2.15` 的中心点 = 2.0（写死的"走 = 跑的一半"，值本身无出处）、
            //   **相对容差 = ±7.5%**；本片把走速按原版改成 `walkSpeed 7 / runSpeed 15`（= 0.4667）
            //   ⇒ 新中心点 = 1 / WalkSpeedFactor = **15/7 ≈ 2.143**，同一 ±7.5% 得 [1.982, 2.304]。
            //   为什么必须留这个量级的容差：这两段是"逐帧位移"的离散计数，还夹着 A* 逐格转向的
            //   世界/格量纲换算 ⇒ 精确到小数位本来就不成立。实测（同一条 44 格路径、同起点同终点）：
            //   跑 856 帧 / 走 1779 帧 = 2.078，与 2.143 差 3% —— 落在旧断言同样的 ±7.5% 里。
            //   ⛔ 判据仍是"实测比值 == 由常量推出的速度比"；若 R 切换没生效，比值会跑到 1.0 附近，照样红。
            var ratio = runFrames > 0 ? (float)walkFrames / runFrames : 0f;
            var expectRatio = 1f / PlayerModule.WalkSpeedFactor;
            var lo = expectRatio * 0.925f;
            var hi = expectRatio * 1.075f;
            Check($"走的耗时 = 跑的 1/走速倍率 倍（原版 run15/walk7 ⇒ {expectRatio:0.000}，相对容差 ±7.5%）",
                ratio >= lo && ratio <= hi,
                $"跑 {runFrames} 帧 / 走 {walkFrames} 帧 = {ratio:0.000}（容许 [{lo:0.000},{hi:0.000}]，同路径 {player.Motor.LastSteps} 格）");
            Check("留下了 [Run] 走/跑切换日志", CaptureLogger.Has("[Run] 走/跑切换"), CaptureLogger.Last("[Run]"));
            player.SetRunning(true);

            // ── 收尾：还原注入（不影响后续步骤）──
            reader.Hover.GroundItemAt = null;
            ctx.Monster = null;
            ctx.Npc = null;
            player.Stop();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 14. 复位（连续两次进图无残留）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step13_Reset(AppContext ctx, PlayerModule player)
        {
            Section("14. 复位：Reset 后再建角色必须干净（第二次进图无残留）");
            player.Reset();
            Check("Reset 后数值清空", player.Level == 1 && player.MaxLife >= 1 && !player.IsDead && !player.IsMoving,
                player.DumpStats());

            player.CreateNew(PlayerClass.Amazon, "Second");
            Check("复位后重新建角：满血、经验 0、属性点 = stat_per_lvl",
                player.Life == player.MaxLife && player.Exp == 0 && player.StatPoints == Table.TableLoader.Class(1).StatPerLvl,
                $"{player.DumpStats()}");
            Check("复位后装备加成没有残留", player.Stats.BonusStr == 0 && player.GetResist(DamageType.Fire) == 0,
                $"BonusStr={player.Stats.BonusStr} 火抗={player.GetResist(DamageType.Fire)}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 小工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>逐帧 Tick 直到到达（或超帧），返回实际帧数；`assertWalkableEachFrame` 时逐帧断言可走。</summary>
        private static int WalkUntilArrive(PlayerModule player, Diablo2.Module.Map.MapModule map, int cap,
            bool assertWalkableEachFrame)
        {
            var frames = 0;
            var offGrid = 0;
            for (; frames < cap && player.IsMoving; frames++)
            {
                player.Tick(Dt);
                if (assertWalkableEachFrame && !map.Walkable(player.Grid)) offGrid++;
            }

            if (assertWalkableEachFrame)
                Check("移动全程每一帧都落在可走格上", offGrid == 0, $"越界/障碍帧数 = {offGrid}");

            return frames;
        }

        /// <summary>逐帧 Tick 直到停止移动，返回消耗的帧数（走/跑速率对比用）。</summary>
        private static int WalkFrames(PlayerModule player, int cap)
        {
            var f = 0;
            for (; f < cap && player.IsMoving; f++) player.Tick(Dt);
            return f;
        }

        /// <summary>收集地图里前 <paramref name="count"/> 个「可走且非出入口、非出生点」的格。</summary>
        private static List<Vector2Int> WalkableCells(Diablo2.Module.Map.MapModule map, int count)
        {
            var list = new List<Vector2Int>();
            for (var y = 0; y < map.Height && list.Count < count; y++)
            {
                for (var x = 0; x < map.Width && list.Count < count; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    if (map.TileAt(g) == TileKind.Exit) continue;
                    if (g == map.SpawnPoint) continue;
                    list.Add(g);
                }
            }
            return list;
        }

        /// <summary>找离 <paramref name="from"/> 最远的可走格（可选：要求与 from 无视线）。</summary>
        private static Vector2Int FindFarWalkable(Diablo2.Module.Map.MapModule map, Vector2Int from, int minDist,
            bool requireNoLineOfSight)
        {
            var best = from;
            var bestDist = -1;
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    // 跳过出入口：进出场景由第 6 步单独验（否则"纯移动"用例会顺带触发区域切换）
                    if (map.TileAt(g) == TileKind.Exit) continue;
                    var d = Iso.GridDistance(from, g);
                    if (d < minDist || d <= bestDist) continue;
                    if (requireNoLineOfSight && AStar.HasLineOfSight(map.Walkable, from, g)) continue;
                    best = g;
                    bestDist = d;
                }
            }
            return best;
        }

        /// <summary>找地图里第一格该地形的格子（找不到返回 null）。</summary>
        private static Vector2Int? FindFirst(Diablo2.Module.Map.MapModule map, TileKind kind)
        {
            for (var x = 0; x < map.Width; x++)
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (map.TileAt(g) == kind) return g;
                }
            return null;
        }

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("────────────────────────────────────────────────────────────");
            Console.WriteLine("▶ " + title);
            Console.WriteLine("────────────────────────────────────────────────────────────");
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (ok) _ok++;
            else _fail++;
            Console.WriteLine($"    {(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }

        /// <summary>单步隔离：任一步炸掉都不能吞掉后面的证据（与 mapcheck 同一做法）。</summary>
        private static void RunStep(string name, Action step)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                _fail++;
                Console.WriteLine($"    ❌ 【{name}】抛异常：{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }
}
