// ─────────────────────────────────────────────────────────────────────────────
//
// 运行：dotnet run --project <项目根>/tools/playercheck/PlayerCheck.csproj -c Release
//
// 覆盖的验收项：
//   ① 离线宿主编译/运行：0 错 0 警告（编译期）+ 断言全过（运行期）
//   ② 点击移动：Town 出生点 → 远端可走格，逐帧 Tick(0.02f) 直到到达（贴起点/终点/路径长度/帧数）
//   ③ 绕障：目标与出生点无视线 ⇒ 路径非直连，且**每一格**都可走、玩家全程 Walkable==true
//   ④ R1-B 新口径：**图上有地形但阻挡** ⇒ 最近可走格回退（≤2 格，原版 D2「点不可走处走向最近
//      合法点」）；图外（越界）/ 图内 `TileKind.Void` / 半径内无可走格 ⇒ 仍拒绝（不移动 + 日志 + 计数）
//   ④b R1-B 桥/水专项：点桥栏杆 ⇒ 走到桥面；点水面 ⇒ 走到岸上；点河中央 ⇒ 仍拒绝
//   ⑤ 受阻：桩地图给出「穿过不可走格」的路径 ⇒ 停在原地（不穿墙）+ Warn + BlockedStops++
//   ⑥ 踩出入口：Town 的出城口 ⇒ 发 Events.ExitEntered(BloodMoor)，且同一格只发一次
//   ⑦ 升级公式：AddExp 到 experience_c 阈值 ⇒ 等级 +1、生命上限按配表增长、日志 `[Player] level 1→2`
//   ⑧ 属性点：AllocateStat(Vitality,5) ⇒ MaxLife 增量 == life_per_vit × 5；非法请求被拒
//   ⑨ 装备生效：EquipChanged 事件 ⇒ 四维/抗性/护甲/AR 变化（不引用 Item 模块，只走事件）
//   ⑩ 死亡/复活：ApplyDamage 归零 ⇒ IsDead + PlayerDied；Revive ⇒ 回出生点满血
//   ⑪ 相机：等距参数（跟随位置/正交尺寸/边界钳制/缩放钳制/震动衰减）纯数学断言 + 无相机时优雅降级
//   ⑫ 输入：ShouldRetarget 真值表 / 按住状态机 / 无相机时点击被拒 / 腰带快捷键发事件
//
// 不覆盖（需要 Unity 原生 API，留给主 agent 进 Play 后验）：
//    · 真实 `Camera.main` 的屏幕→地面反投影（离线宿主里 `Camera.main`/`Screen` 不可用，已断言降级路径）；
//    · 手感（跟随是否"抖"）与像素级画面。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;              // 12 个门面接口 + CameraRig + InputReader（后两者刻意与接口同命名空间，见其文件头）
using Diablo2.Module.Player;       // PlayerModule / PlayerStats / PlayerMotor / PlayerLog
using Diablo2.Module.Save;         // ★ u52block：SaveModule / SaveJson（链路级一跳要用**真**存档模块）
using UnityEngine;
// 故意**不** `using Table;`：`Table` 命名空间里有一个 `Table.Vector3` 结构，
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

        public IReadOnlyList<Vector2Int> WaypointPoints => new List<Vector2Int>();

        public IReadOnlyCollection<Vector2Int> ExploredCells => new List<Vector2Int>();

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < 8 && g.y < 8;
        public bool Walkable(Vector2Int g) => InBounds(g) && _walkable[g.x, g.y];

        /// <summary>桩地图没有"可走上方的结构"（桥面/平台）⇒ 恒 false。
        /// 契约成员见 `Module/Contracts.cs` 的 `IMapModule.IsDeckGrid`（2026-09-22 新增）。</summary>
        public bool IsDeckGrid(Vector2Int g) => false;
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

    /// <summary>
    /// 桩技能门面（impl-I-input，审计 R1）：本宿主**没有编入** `Module/Skill`
    /// （`PlayerCheck.csproj` 的编译清单里没有它，既有断言「未编入的模块保持 null」依赖这一点）
    /// <para>不判 `TryCast` 内部（扣蓝/冷却/投射物）—— 那由既有 `tools/probes/hosts/combatcheck`
    /// 用**真实 `SkillModule`** 覆盖（其 §9「TryCast：扣法力 + 进冷却」）。</para>
    /// </summary>
    internal sealed class StubSkill : ISkillModule
    {
        /// <summary>
        /// **刻意不给无参构造**：`AppContext.AutoWire` 的规则是「程序集里第一个实现该接口的类型」
        /// （`App/AppContext.cs:136-154`）⇒ 若本桩能被无参实例化，它会被自动装配成 `ctx.Skill`，
        /// 从而破坏既有断言「本宿主未编入的模块保持 null」（实测：加了这个桩之后那条断言立刻 FAIL）。
        /// 带一个必填参数 ⇒ `Activator.CreateInstance` 抛 `MissingMethodException` 被 AutoWire 吞掉
        /// （只留一条 Warn「该模块保持未接入」），本宿主只在用例里手工 new。
        /// </summary>
        public StubSkill(int autoWireGuard)
        {
            _ = autoWireGuard;
        }

        /// <summary>右键技能格绑定的技能 id（-1 = 未绑 = 普通攻击）。</summary>
        public int Button1 = -1;

        /// <summary>被调用的施放记录（`#技能id@(x,y)`）。</summary>
        public readonly List<string> Casts = new List<string>();

        public PlayerClass Class => PlayerClass.Amazon;
        public int SelectedSkillId => Button1;
        public IReadOnlyList<SkillDef> Available => new List<SkillDef>();
        public int GetLevel(int skillId) => 1;
        public bool CanLearn(int skillId) => false;
        public bool Learn(int skillId) => false;
        public void SelectSkill(int skillId) => Button1 = skillId;
        public void AssignToButton(int button, int skillId) { if (button == 1) Button1 = skillId; }
        public int GetButtonSkill(int button) => button == 1 ? Button1 : -1;

        public bool TryCast(int skillId, Vector2Int targetGrid)
        {
            Casts.Add($"#{skillId}@({targetGrid.x},{targetGrid.y})");
            return true;
        }

        public float GetCooldownRemain(int skillId) => 0f;
        public SkillTreeArgs BuildTree() => new SkillTreeArgs();
        public void Tick(float dt) { }
        public void ResetForClass(PlayerClass cls, CharacterSave save) { }
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

    /// <summary>
    /// R1-D：`IAppFlow` 的**最小桩** —— 让 <c>CameraRig.RefreshFocus</c> 走「跟主角世界坐标」那条路
    /// （真机由 `AppFlow` 提供同一个判据，见 `CameraRig.FlowReady`）。
    /// <para>为什么需要它：`AppContext.Flow == null` 时相机**不接管机位**（宿主/菜单语义）
    /// ⇒ §15 c 就测不到「Player → Camera 同帧跟随」这条真链路。</para>
    /// </summary>
    internal sealed class StubFlow : Diablo2.Module.Flow.IAppFlow
    {
        public string CurrentState => "Stage";
        public void Enter() { }
        public void GoStage(AreaId area) { }
        public void BackToMain() { }
        public void QuitGame() { }
    }

    public static class Program
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

        private const float Dt = 0.02f;          // 50 FPS（验收要求逐帧 Tick(0.02f)）
        private const int FrameCap = 6000;       // 单次移动的帧数上限（300 秒游戏时间，足够）
        private static int _fail;
        private static int _ok;

        /// <summary>「不适用」项：判据已按设计退役（或输入资产不在仓库里）。
        /// **不计入**通过/失败 —— 它既不是绿灯也不是缺陷，写清楚是为了让"没判的看起来像没判的"。</summary>
        private static int _na;

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
            RunStep("2b. 近距点击矩阵（U33）", () => Step2b_NearClickMatrix(ctx, map, player, bus));
            RunStep("3. 绕障", () => Step3_Detour(ctx, map, player, bus));
            RunStep("4. 不可达 / 最近可走格回退（R1-B）", () => Step4_Unreachable(ctx, map, player, bus));
            RunStep("4b. 桥/水专项（R1-B）", () => Step4b_BridgeWaterFallback(ctx, map, player, bus));
            RunStep("5. 受阻（桩地图）", () => Step5_Blocked(ctx, player, bus));
            RunStep("6. 出入口", () => Step6_ExitPortal(ctx, map, player, bus));
            RunStep("7. 升级", () => Step7_LevelUp(ctx, player));
            RunStep("8. 属性点", () => Step8_AllocateStat(ctx, player));
            RunStep("9. 装备生效", () => Step9_Equip(ctx, player, bus));
            RunStep("9b. ★ R6 格挡（盾基材 block 接入）", () => Step9b_Block(ctx, player, bus));
            RunStep("9c. ★ u52cur 三资源 cur/max（新建即满 / 旧档迁移 / 沿用语义）",
                () => Step9c_Resources(ctx, player, bus));
            RunStep("9d. ★ u52block 链路级一跳：真 SaveModule.Load → 真 PlayerModule.LoadFrom（老档耐力 20/84 的断链点）",
                () => Step9d_SaveToPlayerHop(ctx, player));
            RunStep("10. 死亡/复活", () => Step10_DeathRevive(ctx, map, player, bus));
            RunStep("11. 等距跟随相机", () => Step11_Camera(ctx, map, rig, input));
            RunStep("12. 输入读取", () => Step12_Input(ctx, input, bus));
            RunStep("13. 交互：悬停 / 点怪攻击 / Shift 站立攻击 / 走跑切换",
                () => Step13_Interaction(ctx, map, player, input, bus));
            RunStep("14. 复位", () => Step13_Reset(ctx, player));
            RunStep("15. ★ 移动抖动（R1-D）：逐帧位移上界 / 变 dt 终点一致 / 相机低通 / 帧节奏 / 重铺合并",
                () => Step15_Jitter(ctx, map, player, rig, input));
            RunStep("16. ★ 扣蓝/回蓝（w7）：TrySpendMana 扣减生效 / 不足与非正数不扣 / RestoreMana(-n) 仍钳制",
                () => Step16_ManaSpend(ctx, player, bus));
            RunStep("17. ★ 双武器组（T0 缺口 2）：W 键 → SwapWeaponRequest → 主手互换 / 切回 / 派生只算生效组",
                () => Step17_WeaponGroup(ctx, player, map, input, bus));
            RunStep("18. ★ 死亡扣金币 10%（T0 缺口 1）：12345→11111→10000 / 金币 0 与 7 不扣 / 只报一次",
                () => Step18_DeathGold(ctx, player, map, bus));
            RunStep("19. ★ R1（impl-I）：右键 button 1 → 已有施放入口 TryCast / 未绑回退普攻 / 右键不移动",
                () => Step19_RightClick(ctx, map, player, input, bus));
            RunStep("20. ★ R4（impl-I）：F1~F8 技能槽 → SkillSlotAssignRequest（槽号与左右手映射）",
                () => Step20_SkillSlots(ctx, player, input, bus));
            RunStep("21. ★ R5（impl-I）：Alt 常显 / 悬停单件地面物品名牌 → GroundItemLabelsChanged",
                () => Step21_GroundItemNames(ctx, map, player, input, bus));

            Console.WriteLine();
            Console.WriteLine($"================ 结束：{_ok} 项通过，{_fail} 项失败，{_na} 项不适用 ================");
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
            //    这里改为断言「本宿主**没有编入**的模块仍保持 null」——这才是该用例的本意（降级不崩）。
            //    本轮（T0 判据缺口 2）：`Module/Item` 已加入本宿主的编译清单（见 `PlayerCheck.csproj`），
            //        名单里剩下的 4 个（Combat/Skill/Quest/Audio）仍必须为 null，
            //        而 Save 必须**恰好**是生产实现（不是桩）。
            Check("本宿主未编入的模块保持 null（降级，不崩）",
                ctx.Combat == null && ctx.Skill == null && ctx.Quest == null
                && ctx.Audio == null,
                ctx.Describe() + "（Monster/Npc = 本宿主 §13 的桩 StubMonsters/StubNpcs，非生产实现）");
            Check("本轮新增编入的 ViewModule 是真实现（§15 f 的三分判据打在它上面）",
                ctx.View != null && ctx.View.GetType().Name == "ViewModule",
                ctx.View == null ? "null" : ctx.View.GetType().FullName);
            Check("本轮新增编入的 ItemModule 是真实现（§17 的双武器组断言打在它上面）",
                ctx.Item != null && ctx.Item.GetType().Name == "ItemModule",
                ctx.Item == null ? "null" : ctx.Item.GetType().FullName);
            Check("本片（u52block）新增编入的 SaveModule 是真实现（§9d 的链路级一跳打在它上面）",
                ctx.Save != null && ctx.Save.GetType().Name == "SaveModule",
                ctx.Save == null ? "null" : ctx.Save.GetType().FullName);

            Console.WriteLine("    （下方若出现 [Cfg] 的 WARN：非 Unity 进程读 config.json 的正常降级，" +
                              "Cfg 内部已 try/catch，不是失败）");
            return ctx;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 1. 五职业 1 级派生属性 ↔ class_c（配表一致性）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step1_FiveClasses(AppContext ctx, object mapObj)
        {
            Section("1. 五职业 1 级 MaxLife/MaxMana/MaxStamina ↔ **官方起始值**（charstats.txt + Arreat Summit）");
            var player = (PlayerModule)ctx.Player;

            //   旧断言的"期望值"是**用同一个错公式现算**（`row.Vit * row.LifePerVit`）再和 PlayerModule 比，
            //   两边同源 ⇒ 无论公式多错它都 OK（实测：它给 60/22/20 全绿，而官方 1 级是 50/15/84）。
            //   现在期望值 = **官方原始数据，硬编码**（出处逐条写在下面三行数组旁），
            //   并且**不再由 `class_c` 参与**期望值的计算。
            //
            //   出处①（列语义 + 逐列取值）：`原版资源/d2raw/data/global/excel/charstats.txt:2..6`
            //     · 第 7 列 `stamina` = "Starting amount of Stamina" ⇒ 84 / 74 / 79 / 89 / 92
            //     · 第 8 列 `hpadd`   = "Bonus starting Life value (…gets added with the vit field value
            //                            to determine the overall starting amount of Life)" ⇒ 5 职业同为 30
            //   出处②（1 级实际值，官方职业页 classic.battle.net/diablo2exp/classes/*.shtml
            //          Starting Attributes → Hit Points / Stamina / Mana）：
            var officialLife = new[] { 50, 40, 45, 55, 55 };   // Amazon/Sorceress/Necromancer/Paladin/Barbarian
            var officialMana = new[] { 15, 35, 25, 15, 10 };
            var officialStam = new[] { 84, 74, 79, 89, 92 };

            Console.WriteLine("    id 职业        str dex vit eng  life/vit mana/mag stam/vit  生命(官方) 法力(官方) 耐力(官方)   防御  AR");
            var allOk = true;

            for (var id = 1; id <= 5; id++)
            {
                var row = Table.TableLoader.Class(id);
                player.CreateNew((PlayerClass)id, "Check" + id);

                var expLife = officialLife[id - 1];
                var expMana = officialMana[id - 1];
                var expStam = officialStam[id - 1];

                var ok = player.MaxLife == expLife && player.MaxMana == expMana && player.MaxStamina == expStam;
                allOk &= ok;

                Console.WriteLine($"    {id}  {row.Name,-8}  {row.Str,3} {row.Dex,3} {row.Vit,3} {row.Eng,3}" +
                                  $"  {row.LifePerVit,7:0.##} {row.ManaPerMag,7:0.##} {row.StamPerVit,7:0.##}" +
                                  $"   {player.MaxLife,4}({expLife,3}) {player.MaxMana,4}({expMana,3}) {player.MaxStamina,4}({expStam,3})" +
                                  $"   {player.Defense,4} {player.AttackRating,4}   {(ok ? "" : "❌ 与官方起始值不符")}");
                Check($"{row.Name} 1 级生命/法力/耐力 = 官方起始值（hpadd+起始体力 / 起始精力 / charstats.stamina）",
                    ok, $"期望 {expLife}/{expMana}/{expStam}，实际 {player.MaxLife}/{player.MaxMana}/{player.MaxStamina}");
            }

            Check("5 职业逐一相符（官方起始值）", allOk, "见上表；期望值逐条硬编码自 charstats.txt + Arreat Summit");

            // ── 判据自检（**退化样本**，不许恒真）─────────────────────────────
            //   把**旧公式**（起始四维 × 成长系数）喂进同一条判据：必须 5 个职业全部变红。
            var degGreen = 0;
            for (var id = 1; id <= 5; id++)
            {
                var r = Table.TableLoader.Class(id);
                var oldLife = Mathf.Max(1, Mathf.RoundToInt(r.Vit * r.LifePerVit));
                var oldMana = Mathf.Max(1, Mathf.RoundToInt(r.Eng * r.ManaPerMag));
                var oldStam = Mathf.Max(1, Mathf.RoundToInt(r.Vit * r.StamPerVit));
                if (oldLife == officialLife[id - 1] && oldMana == officialMana[id - 1] && oldStam == officialStam[id - 1])
                    degGreen++;
            }
            Check("退化样本：旧公式（起始四维 × 成长系数）在 5 职业上**全红**（判据真的在判起始截距）",
                degGreen == 0, $"旧公式与官方起始值相同的职业数 = {degGreen}（要求 0）");

            // 与创角屏同口径的抽查（验收 #17：创角预览要跟进图后对得上）
            //   这里把 `UI/CharCreatePanel.LifeOf` 的**表达式逐字抄一遍**（UI 层不进本宿主；
            //      分层自检 ③ 也禁止 UI 引用 Module）—— 真正"屏上预览 == 面板"的比对在 Play 驱动里做。
            var amazon = Table.TableLoader.Class(1);
            player.CreateNew(PlayerClass.Amazon, "Check1");
            var uiLife = Mathf.Max(1, Mathf.RoundToInt((30 + amazon.Vit) + (amazon.Vit - amazon.Vit) * amazon.LifePerVit
                                                       + 0 * amazon.LifePerLvl));
            Check("与 CharCreatePanel.LifeOf 同式（亚马逊 1 级 = 50，与官方一致）",
                player.MaxLife == uiLife && uiLife == 50,
                $"PlayerModule={player.MaxLife} 创角屏式={uiLife} 官方=50");
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
        // 2b. U33「鼠标在人附近移动没效果，必须要远」：近距点击矩阵
        //            按住左键持续更新目标」⇒ 近处（脚下/8 邻域/距离 2）一样要有反应。
        //     走**单击真链路** `PlayerModule.HandlePrimaryClick`（= `HandleMoveIntent` 的 ① 支），
        //     每一步先把角色放回 P，保证矩阵各格互不干扰。
        // ═════════════════════════════════════════════════════════════════════
        private static void Step2b_NearClickMatrix(AppContext ctx, object mapObj, PlayerModule player, RecordingEventBus bus)
        {
            Section("2b. ★ U33 近距点击矩阵：脚下格 / 8 邻域 / Chebyshev 距离 2 圈（逐帧 Tick 0.02f）");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "Check");
            player.TeleportTo(map.SpawnPoint);

            var p = player.Grid;
            Console.WriteLine($"    玩家格 P={p}（Town 出生点 {map.SpawnPoint}）；地图 {map.Width}x{map.Height}");

            // ① 脚下格：原版语义 = 点自己 ⇒ 停下来（不许移动到别处）
            player.TeleportTo(p);
            var foot0 = bus.CountOf(Events.MoveCommand);
            player.HandlePrimaryClick(p);
            WalkUntilArrive(player, map, 400, assertWalkableEachFrame: true);
            Check("点脚下格 P ⇒ 仍下发 MoveCommand、角色原地不动（不是被吞掉，是走了 MoveTo 的「已在目标格」分支）",
                bus.CountOf(Events.MoveCommand) == foot0 + 1 && player.Grid == p && !player.IsMoving,
                $"MoveCommand +{bus.CountOf(Events.MoveCommand) - foot0}，终点 {player.Grid}，IsMoving={player.IsMoving}");
            Check("点脚下格留下了 `[Move] 已在目标格` 日志", CaptureLogger.Has("[Move] 已在目标格"),
                CaptureLogger.Last("[Move] 已在目标格"));

            // ② (P,M) 矩阵：Chebyshev 距离 1 8 格 + 距离 2 圈 16 格
            var walkableHit = 0;
            var walkableTotal = 0;
            var blockedMoves = 0;
            var notReached = new List<string>();
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;                 // 脚下格已单独验过
                    var m = new Vector2Int(p.x + dx, p.y + dy);
                    if (!map.InBounds(m)) continue;                   // 图外：由既有 §4「图外拒绝」覆盖

                    player.TeleportTo(p);
                    var mv0 = bus.CountOf(Events.MoveCommand);
                    player.HandlePrimaryClick(m);
                    WalkUntilArrive(player, map, 400, assertWalkableEachFrame: true);

                    // 放开近距 ≠ 放开阻挡：任何一格走完都不能停在不可走格上
                    if (!map.Walkable(player.Grid)) blockedMoves++;
                    if (!map.Walkable(m)) continue;                    // 不可走落点 ⇒ 由既有的 R1-B 回退口径覆盖

                    walkableTotal++;
                    if (player.Grid == m) walkableHit++;
                    else notReached.Add($"M={m}(d={Iso.GridDistance(p, m)})终点={player.Grid}");
                    if (bus.CountOf(Events.MoveCommand) <= mv0) notReached.Add($"M={m} 未下发MoveCommand");
                }
            }

            Check($"★ 近距可走格全部走到（{walkableHit}/{walkableTotal} 命中；含 8 邻域与距离 2 圈）",
                walkableTotal > 0 && walkableHit == walkableTotal,
                $"命中 {walkableHit}/{walkableTotal}" + (notReached.Count > 0 ? "；未达标：" + string.Join("，", notReached) : string.Empty));
            Check("点了不可走格也没有把阻挡一起放开（走完仍站在可走格上）", blockedMoves == 0,
                $"踩到不可走格的格数 = {blockedMoves}");
            Console.WriteLine($"    ⇒ P={p}；矩阵共 {walkableTotal} 个可走近距格，全部在 ≤400 帧内到达");
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
            Section("4. ★ R1-B：点不可走格 ⇒ 最近可走格回退（≤2 格）；半径内无可走格 / 图外 ⇒ 仍拒绝");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Barbarian, "Blocked");
            player.TeleportTo(map.SpawnPoint);

            var start = player.Grid;
            Check("回退半径 = 2 格（口径与出处见 PlayerModule.MoveFallbackRadius 注释）",
                PlayerModule.MoveFallbackRadius == 2, $"= {PlayerModule.MoveFallbackRadius}");

            // ── ① 点阻挡格（栅栏/石墙/帐篷/水…）⇒ 回退到半径内最近可走格并**真的走过去** ──────
            //   旧口径（本轮已废除）是「点障碍 ⇒ 原地不动」；原版 D2 是「走向最近合法点」。
            var blocked = FindBlockedWithLanding(map, start, PlayerModule.MoveFallbackRadius, out var expect);
            if (!blocked.HasValue)
            {
                Check("地图里存在「半径内有可走格且走得到」的阻挡格", false, "找不到这样的格（换 seed 试）");
            }
            else
            {
                var beforeCount = player.UnreachableCount;
                var beforeLogs = CaptureLogger.Count("[Move] unreachable");
                Console.WriteLine($"    用例：点击阻挡格 {blocked.Value}（地形={map.TileAt(blocked.Value)}），" +
                                  $"半径复算的落点 = {expect}");
                player.MoveTo(blocked.Value);
                Console.WriteLine("    " + CaptureLogger.Last("[R1-B]"));
                for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);

                Check("点阻挡格**不再原地不动**：角色走到了邻近可走格",
                    player.Grid != start && map.Walkable(player.Grid),
                    $"起点 {start} → 现在 {player.Grid}（点击 {blocked.Value}）");
                Check("落点 == 口径复算的「离点击点最近 → 离角色最近 → (dx,dy) 升序」那格",
                    player.Grid == expect, $"实测 {player.Grid} vs 复算 {expect}");
                Check("落点在回退半径内", Iso.GridDistance(player.Grid, blocked.Value) <= PlayerModule.MoveFallbackRadius,
                    $"Chebyshev 距离 = {Iso.GridDistance(player.Grid, blocked.Value)} ≤ {PlayerModule.MoveFallbackRadius}");
                Check("回退成功**不计入** UnreachableCount", player.UnreachableCount == beforeCount,
                    $"{beforeCount} → {player.UnreachableCount}");
                Check("留下了 R1-B 生效口径日志（只报一次）",
                    CaptureLogger.Has("[R1-B]") && CaptureLogger.Count("[R1-B]") == 1,
                    CaptureLogger.Last("[R1-B]"));
                Check("回退成功时**没有**打 [Move] unreachable", CaptureLogger.Count("[Move] unreachable") == beforeLogs,
                    $"{beforeLogs} → {CaptureLogger.Count("[Move] unreachable")}");
            }

            // ── ② 图外（越界）⇒ **仍然直接拒绝**（点关卡外不动；回退只对「图上有地形只是阻挡」生效）──
            var here = player.Grid;
            var cntOut = player.UnreachableCount;
            var logsOut = CaptureLogger.Count("[Move] unreachable");
            player.MoveTo(new Vector2Int(-5, -5));
            for (var f = 0; f < 50; f++) player.Tick(Dt);
            Check("图外目标仍被拒：不动 + UnreachableCount+1 + 留日志",
                player.Grid == here && player.UnreachableCount == cntOut + 1
                && CaptureLogger.Count("[Move] unreachable") == logsOut + 1,
                $"格={player.Grid}（应停在 {here}）UnreachableCount={cntOut}→{player.UnreachableCount}");
            Console.WriteLine("    " + CaptureLogger.Last("[Move] unreachable"));

            // ── ③ 图内 `TileKind.Void`（原版没铺、`MapView` 也不画的西北角 ⇒ 视觉上就是关卡外那片黑）
            //      ⇒ **与越界同处置：直接拒绝**（不许"点黑就走到旁边草地"）─────────────────────
            var voidCell = FindInBoundsVoid(map);
            if (!voidCell.HasValue)
            {
                Check("地图里存在图内 Void 格（城镇西北角 3×10）", false, "找不到 Void 格");
            }
            else
            {
                var cntVoid = player.UnreachableCount;
                player.MoveTo(voidCell.Value);
                for (var f = 0; f < 50; f++) player.Tick(Dt);
                Check("点图内 Void（视觉=关卡外黑）仍被拒：不动 + UnreachableCount+1",
                    player.Grid == here && player.UnreachableCount == cntVoid + 1,
                    $"格={player.Grid}（应停在 {here}）Void 格={voidCell.Value} UnreachableCount={cntVoid}→{player.UnreachableCount}");
            }
        }

        /// <summary>图内（`InBounds` 为真）的 `TileKind.Void` 格 —— 原版没铺瓦片、渲染层也不画的格。</summary>
        private static Vector2Int? FindInBoundsVoid(Diablo2.Module.Map.MapModule map)
        {
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (map.InBounds(g) && map.TileAt(g) == TileKind.Void) return g;
                }
            }
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 4b. R1-B 桥 / 水专项（用户报「为什么不是从桥上走？」）
        //     判据全部取自**逐格原版瓦片键**（`MapModule.TryGetTileKeys`），不是靠坐标猜：
        //       ① 点**桥栏杆**格（floor+wall 都来自 `bridge.dt1`）⇒ 落到**桥面**格
        //          （floor 来自 `bridge.dt1` 且 wall 为空）
        //       ② 点**水面**格（floor 来自 `river.dt1`）⇒ 落到**岸上**（floor 不是 `river.dt1`）
        //       ③ 点**河中央**（半径 2 内一格可走都没有）⇒ **仍拒绝**（证明没有放宽成"点哪都能走"）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step4b_BridgeWaterFallback(AppContext ctx, object mapObj, PlayerModule player,
            RecordingEventBus bus)
        {
            Section("4b. ★ R1-B 桥/水专项：点栏杆 ⇒ 走到桥面；点水面 ⇒ 走到岸上；点河中央 ⇒ 仍拒绝");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "Bridge");
            player.TeleportTo(map.SpawnPoint);

            // ── ① 桥栏杆 ⇒ 桥面 ────────────────────────────────────────────────────
            var rail = FindBridgeRail(map);
            if (!rail.HasValue)
            {
                Check("地图里存在桥栏杆格（floor+wall 都来自 bridge.dt1）", false, "找不到桥栏杆格");
            }
            else
            {
                map.TryGetTileKeys(rail.Value.x, rail.Value.y, out var rg, out var ro);
                Console.WriteLine($"    用例①：桥栏杆格 {rail.Value} floor={rg} wall={ro} 地形={map.TileAt(rail.Value)}");
                player.MoveTo(rail.Value);
                for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);

                map.TryGetTileKeys(player.Grid.x, player.Grid.y, out var lg, out var lo);
                Check("点桥栏杆 ⇒ 落到了**桥面**（floor 来自 bridge.dt1 且 wall 为空）",
                    player.Grid != rail.Value && map.Walkable(player.Grid)
                    && lg.StartsWith("moor_bridge/") && string.IsNullOrEmpty(lo),
                    $"落点 {player.Grid} floor={lg} wall={(string.IsNullOrEmpty(lo) ? "(空=桥面)" : lo)}");
                Check("落点是栏杆的**相邻格**（桥面与栏杆相邻 1 格 —— 这就是「点桥不走桥」的根因）",
                    Iso.GridDistance(player.Grid, rail.Value) == 1,
                    $"Chebyshev 距离 = {Iso.GridDistance(player.Grid, rail.Value)}");
            }

            // ── ② 水面 ⇒ 岸上 ──────────────────────────────────────────────────────
            var water = FindRiverWaterWithWalkableNeighbor(map, PlayerModule.MoveFallbackRadius);
            if (!water.HasValue)
            {
                Check("地图里存在「半径内有可走岸」的水面格", false, "找不到这样的水面格");
            }
            else
            {
                map.TryGetTileKeys(water.Value.x, water.Value.y, out var wg, out var wo);
                Console.WriteLine($"    用例②：水面格 {water.Value} floor={wg} wall={(string.IsNullOrEmpty(wo) ? "(空)" : wo)}" +
                                  $" 地形={map.TileAt(water.Value)}");
                player.MoveTo(water.Value);
                for (var f = 0; f < FrameCap && player.IsMoving; f++) player.Tick(Dt);

                map.TryGetTileKeys(player.Grid.x, player.Grid.y, out var lg2, out _);
                Check("点水面 ⇒ 落到了**岸上**（可走 + floor 不再是 river.dt1）",
                    player.Grid != water.Value && map.Walkable(player.Grid) && !lg2.StartsWith("moor_river/"),
                    $"落点 {player.Grid} floor={lg2} 地形={map.TileAt(player.Grid)}");
                Check("落点在水面格的回退半径内",
                    Iso.GridDistance(player.Grid, water.Value) <= PlayerModule.MoveFallbackRadius,
                    $"Chebyshev 距离 = {Iso.GridDistance(player.Grid, water.Value)}");
            }

            // ── ③ 河中央（半径 2 内无可走格）⇒ 仍拒绝 ───────────────────────────────
            var midRiver = FindRiverWaterWithoutWalkableNeighbor(map, PlayerModule.MoveFallbackRadius);
            if (!midRiver.HasValue)
            {
                Check("地图里存在「半径内一格可走都没有」的水面格", false, "找不到（河太窄？）");
            }
            else
            {
                var here = player.Grid;
                var cnt = player.UnreachableCount;
                map.TryGetTileKeys(midRiver.Value.x, midRiver.Value.y, out var mg, out _);
                Console.WriteLine($"    用例③：河中央 {midRiver.Value} floor={mg} 地形={map.TileAt(midRiver.Value)}");
                player.MoveTo(midRiver.Value);
                for (var f = 0; f < 50; f++) player.Tick(Dt);
                Check($"点河中央（回退半径 {PlayerModule.MoveFallbackRadius} 内无路可上）⇒ **仍拒绝**：不动 + UnreachableCount+1",
                    player.Grid == here && player.UnreachableCount == cnt + 1,
                    $"格={player.Grid}（应停在 {here}）UnreachableCount={cnt}→{player.UnreachableCount}");
                Console.WriteLine("    " + CaptureLogger.Last("[Move] unreachable"));
            }
        }

        /// <summary>
        /// 找一个「**不可走（且图上有地形，即不是 Void）** 且 在 <paramref name="radius"/> 内存在可走格、
        /// 且那格从 <paramref name="from"/> 走得到」的格；并按**口径复算**（离点击点最近 → 离角色最近 →
        /// dx/dy 升序，与 `PlayerModule.MoveFallbackRadius` 的文档一致）给出期望落点 —— 把回退规则钉死。
        /// </summary>
        private static Vector2Int? FindBlockedWithLanding(Diablo2.Module.Map.MapModule map, Vector2Int from,
            int radius, out Vector2Int expect)
        {
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var t = new Vector2Int(x, y);
                    if (map.Walkable(t)) continue;
                    // Void（原版没铺、也不画 ⇒ 视觉=关卡外）不参与回退 ⇒ 本用例也不该拿它当"阻挡格"
                    if (!map.InBounds(t) || map.TileAt(t) == TileKind.Void) continue;
                    var e = ExpectedLanding(map, from, t, radius);
                    if (e.x == int.MinValue) continue;
                    if (map.FindPath(from, e) == null) continue;
                    expect = e;
                    return t;
                }
            }
            expect = new Vector2Int(int.MinValue, int.MinValue);
            return null;
        }

        /// <summary>按文档口径独立复算「最近可走格」（宿主侧复算，用来对账；返回 int.MinValue 格 = 半径内没有可走格）。</summary>
        private static Vector2Int ExpectedLanding(Diablo2.Module.Map.MapModule map, Vector2Int from,
            Vector2Int target, int radius)
        {
            var best = new Vector2Int(int.MinValue, int.MinValue);
            var bc = int.MaxValue;
            var bp = int.MaxValue;
            for (var dx = -radius; dx <= radius; dx++)
            {
                for (var dy = -radius; dy <= radius; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(target.x + dx, target.y + dy);
                    if (!map.Walkable(g)) continue;
                    var dc = dx * dx + dy * dy;
                    if (dc > bc) continue;
                    var fx = g.x - from.x;
                    var fy = g.y - from.y;
                    var dp = fx * fx + fy * fy;
                    if (dc == bc && dp >= bp) continue;
                    best = g;
                    bc = dc;
                    bp = dp;
                }
            }
            return best;
        }

        /// <summary>
        /// 桥栏杆格：floor 与 wall **都**来自 `bridge.dt1`（= 桥的栏杆行；桥面行的 wall 是空的），
        /// 且**所有距离 1 的可走邻格都是桥面**（floor 也是 `bridge.dt1` 且 wall 空）。
        /// <para>为什么要加后半句：桥的**两端**（x=46/55）栏杆的邻格里有营地地面/岸上草地，
        /// 那种用例会让"点到栏杆应该走到桥面"这条断言变成在考"离角色更近"这条 tie-break；
        /// 取桥中段的栏杆，落点就必然落在桥面上（判据才指向"点桥不走桥"这个真问题）。</para>
        /// </summary>
        private static Vector2Int? FindBridgeRail(Diablo2.Module.Map.MapModule map)
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    if (!map.TryGetTileKeys(x, y, out var g, out var o)) continue;
                    if (!g.StartsWith("moor_bridge/") || !o.StartsWith("moor_bridge/")) continue;

                    var walkableNeighbors = 0;
                    var allDeck = true;
                    for (var i = 0; i < 4 && allDeck; i++)
                    {
                        var nx = x + (i == 0 ? 1 : i == 1 ? -1 : 0);
                        var ny = y + (i == 2 ? 1 : i == 3 ? -1 : 0);
                        if (!map.Walkable(new Vector2Int(nx, ny))) continue;
                        walkableNeighbors++;
                        map.TryGetTileKeys(nx, ny, out var ng, out var no);
                        if (!ng.StartsWith("moor_bridge/") || !string.IsNullOrEmpty(no)) allDeck = false;
                    }
                    if (walkableNeighbors > 0 && allDeck) return new Vector2Int(x, y);
                }
            }
            return null;
        }

        /// <summary>水面格（floor 来自 `river.dt1`）且半径内有可走格。</summary>
        private static Vector2Int? FindRiverWaterWithWalkableNeighbor(Diablo2.Module.Map.MapModule map, int radius)
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    if (!map.TryGetTileKeys(x, y, out var g, out _)) continue;
                    if (!g.StartsWith("moor_river/")) continue;
                    var t = new Vector2Int(x, y);
                    var e = ExpectedLanding(map, t, t, radius);
                    if (e.x == int.MinValue) continue;
                    return t;
                }
            }
            return null;
        }

        /// <summary>水面格（floor 来自 `river.dt1`）但半径内**一格可走都没有**（= 河中央）。</summary>
        private static Vector2Int? FindRiverWaterWithoutWalkableNeighbor(Diablo2.Module.Map.MapModule map, int radius)
        {
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    if (!map.TryGetTileKeys(x, y, out var g, out _)) continue;
                    if (!g.StartsWith("moor_river/")) continue;
                    var t = new Vector2Int(x, y);
                    var e = ExpectedLanding(map, t, t, radius);
                    if (e.x != int.MinValue) continue;
                    return t;
                }
            }
            return null;
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
            // U3：期望值同样换成**官方起始口径**（起始精力 + 1 级 × mana_per_lvl），
            Check("法力上限按配表增长（起始精力 + mana_per_lvl）",
                player.MaxMana == Mathf.RoundToInt(row.Eng + 1 * row.ManaPerLvl),
                $"法力上限 {player.MaxMana}（起始精力 {row.Eng} + mana_per_lvl={row.ManaPerLvl}）");
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
        // 9b. u52block（R6）：格挡 —— 盾牌**基材** block 接入 / 合成式 / 上限 / 闸门 / 退化样本
        //     用户报的「人物状态框，数值信息不对」含这一格：装了盾格挡仍是 0%。
        //     出处（逐条；无一条来自"看起来合理"）：
        //       ① **合成式**：Arreat Summit「Basics: Character Information → Blocking」
        //          `Total Blocking = (Blocking * (Dexterity - 15)) / (Character Level * 2)`
        //          `Blocking = A total of the Blocking on all of your items.`
        //          `The block value itself is a combination of a value inherent to that particular
        //           player class, and any other block bonuses from items. This value is capped at 75%.`
        //       ② **盾牌基材 block** = 官方 `Armor.txt` 第 11 列 `block`
        //          （`原版资源/d2lod1.10txt-1.10f/data/global/excel/Armor.txt`；打表 ⇒ `item_c.block`）
        //       ③ **职业固有值** = 官方 `charstats.txt` 第 32 列 `BlockFactor`（打表 ⇒ `class_c.block_factor`）
        //          + `https://d2grail.com/items/bases/armor/shields/normal/buckler`（Buckler 同行一致）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step9b_Block(AppContext ctx, PlayerModule player, RecordingEventBus bus)
        {
            Section("9b. ★ R6 格挡：盾基材 block 接入（逐职业期望值 / 上限边界 / 无盾闸门 / 退化样本）");

            // ── ① 出处交叉核对：打表出来的 `item_c.block` == 官方公布格挡 − 职业固有值(Pal 30) ──
            //   官方公布的普通盾「格挡」（Paladin 档，实取 https://www.diablo-2.net/items/shields）：
            //     Buckler 30 / Small 35 / Large 42 / Kite 38 / Spiked 40 / Bone 50 / Tower 54 / Gothic 46
            //   `charstats.txt:2..6` 第 32 列 BlockFactor = Pal 30 / Ama 25 / Bar 25 / Sor 20 / Nec 20
            //   ⇒ 官方公布值 = 职业固有值 + 盾基材 block ⇒ 盾基材 block = 公布值 − 30（与职业无关）。
            var officialPaladinBlock = new Dictionary<string, int>
            {
                { "buc", 30 }, { "sml", 35 }, { "lrg", 42 }, { "kit", 38 },
                { "spk", 40 }, { "bsh", 50 }, { "tow", 54 }, { "gts", 46 },
            };
            var palFactor = Table.TableLoader.Class(4).BlockFactor;      // 圣骑士 = 30
            var crossOk = 0;
            var crossTotal = 0;
            var missing = new List<string>();
            var crossDetail = new List<string>();
            foreach (var kv in officialPaladinBlock)
            {
                var r = FindItemRowByCode(kv.Key);
                var exp = kv.Value - palFactor;
                if (r == null)
                {
                    // Act I 打表口径（`tools/table-convert/convert.py` 的 `ITEM_MAX_LEVEL = 12`）：
                    //    官方 qlvl > 12 的盾不进 `item_c`（Kite 15 / Bone 19 / Tower 22 / Gothic 30）
                    missing.Add(kv.Key);
                    continue;
                }
                crossTotal++;
                if (r.Block == exp) crossOk++;
                crossDetail.Add($"{kv.Key}:{r.Block}(期望{exp})");
            }
            Check("Act I 表里的盾：`item_c.block` = 官方公布格挡 − `class_c.block_factor`(Pal 30) 逐条相符（4 种）",
                crossTotal == 4 && crossOk == crossTotal,
                $"相符 {crossOk}/{crossTotal}：{string.Join(" ", crossDetail)}");
            Check("官方 qlvl>12 的 4 种盾按既有 Act I 口径不在 `item_c`（Kite15/Bone19/Tower22/Gothic30）",
                string.Join(",", missing) == "kit,bsh,tow,gts",
                "不在表里的：" + string.Join(",", missing));

            // ── ② 合成式（逐职业期望值；期望值**硬编码自官方公式**，不由被测代码现算）──
            //   盾 = Small Shield（官方 block = 5）；等级两档：Lv1 与 Lv10。
            //   Lv1（各职业起始敏/固有值都读 `class_c`）：敏25+BF25 → (10×30)/2 = 150 → 钳 75；…全部顶到 75
            //   Lv10（各职业分道，验的是"**盾 block 与职业 BlockFactor 两项都进去了**"）：
            //     Amazon     (25−15)×(5+25)/(2×10) = 300/20 = 15
            //     Sorceress  (25−15)×(5+20)/(2×10) = 250/20 = 12（整数除法，与官方同）
            //     Necromancer                                            = 12
            //     Paladin    (20−15)×(5+30)/(2×10) = 175/20 = 8
            //     Barbarian  (20−15)×(5+25)/(2×10) = 150/20 = 7
            var smallShield = FindItemRowByCode("sml");
            Check("配表里取到 Small Shield（item_c.block = 5，官方 Armor.txt 同值）",
                smallShield != null && smallShield.Block == 5,
                smallShield == null ? "取不到行" : $"id={smallShield.Id} block={smallShield.Block}");

            var expectLv1 = new[] { 75, 75, 75, 75, 75 };
            var expectLv10 = new[] { 15, 12, 12, 8, 7 };
            var formulaOk = true;
            var formulaDetail = new List<string>();
            for (var id = 1; id <= 5; id++)
            {
                var cr = Table.TableLoader.Class(id);
                var got1 = PlayerStats.ComputeBlockChance(true, cr.Dex, smallShield.Block, cr.BlockFactor, 1);
                var got10 = PlayerStats.ComputeBlockChance(true, cr.Dex, smallShield.Block, cr.BlockFactor, 10);
                formulaOk &= got1 == expectLv1[id - 1] && got10 == expectLv10[id - 1];
                formulaDetail.Add($"{cr.Name} Lv1={got1}(期望{expectLv1[id - 1]}) Lv10={got10}(期望{expectLv10[id - 1]})");
            }
            Check("装盾（Small Shield block=5）后逐职业格挡 = 官方公式值（Lv1 与 Lv10 两档，5 职业）",
                formulaOk, string.Join("；", formulaDetail));
            Console.WriteLine("   " + string.Join("；", formulaDetail));

            // ── ③ 上限钳制的边界用例（官方 capped at 75%）──
            //   Amazon + Small Shield + Lv1：BF25+block5 = 30 ⇒ (dex−15)×30/2 = (dex−15)×15
            //     dex=20 → 75（**刚好到上限**）· dex=21 → 90（**超过上限** ⇒ 钳 75）· dex=19 → 60（未到上限，原值）
            Check("上限边界①：敏20 ⇒ (20−15)×(5+25)/2 = 75 **刚好到上限**",
                PlayerStats.ComputeBlockChance(true, 20, 5, 25, 1) == 75,
                "实际 " + PlayerStats.ComputeBlockChance(true, 20, 5, 25, 1));
            Check("上限边界②：敏21 ⇒ 算得 90 **超过上限** ⇒ 钳到 75",
                PlayerStats.ComputeBlockChance(true, 21, 5, 25, 1) == 75,
                "实际 " + PlayerStats.ComputeBlockChance(true, 21, 5, 25, 1));
            Check("上限边界③：敏19 ⇒ 60 **未到上限** ⇒ 保留原值（钳制不误伤正常值）",
                PlayerStats.ComputeBlockChance(true, 19, 5, 25, 1) == 60,
                "实际 " + PlayerStats.ComputeBlockChance(true, 19, 5, 25, 1));
            Check("下限：敏14（< 15）⇒ 负数钳到 0（不是负数格挡）",
                PlayerStats.ComputeBlockChance(true, 14, 5, 25, 1) == 0,
                "实际 " + PlayerStats.ComputeBlockChance(true, 14, 5, 25, 1));

            // ── ④ 无盾闸门（**既有读数，不许放宽**）──
            //   官方：无盾/无死灵头骨时角色屏不显示格挡率 ⇒ 徒手/只穿甲恒 0%。
            //   1 级徒手亚马逊若把职业固有值单独算进去会得 (25−15)×25/2 = 125% ⇒ 钳 75%，与官方 0% 冲突。
            Check("无盾闸门：`hasShield=false` ⇒ 恒 0%（哪怕敏捷很高）",
                PlayerStats.ComputeBlockChance(false, 9999, 0, 25, 1) == 0
                && PlayerStats.ComputeBlockChance(false, 25, 0, 25, 1) == 0,
                "expected 0/0");
            Check("★ Buckler（官方 block=0，3 个职业的起始盾）也算「有盾」：Ama (25−15)×25/2 = 125→75 / Bar (20−15)×25/2 = 62",
                PlayerStats.ComputeBlockChance(true, 25, 0, 25, 1) == 75
                && PlayerStats.ComputeBlockChance(true, 20, 0, 25, 1) == 62,
                $"Ama={PlayerStats.ComputeBlockChance(true, 25, 0, 25, 1)} Bar={PlayerStats.ComputeBlockChance(true, 20, 0, 25, 1)}");

            // ── ⑤ 退化样本：把**修前形状**喂进同一判据 ⇒ 必须变红 ──
            //   修前：盾基材 block 没接入 ⇒ 装备项只等于词缀 block 之和（本样例无词缀 ⇒ 0）
            var preFixAma = PreFixBlockChance(25, 0, 25, 1);
            var preFixBar = PreFixBlockChance(20, 0, 25, 1);
            Check("退化样本①（装盾后=公式值）：修前形状（装备项恒 0）在同一判据上**变红**（Ama 0≠75 / Bar 0≠62）",
                preFixAma != 75 && preFixBar != 62,
                $"修前 Ama={preFixAma} Bar={preFixBar}（要求分别 ≠ 75 / ≠ 62）");
            Check("退化样本②（上限边界同判据）：修前形状在敏19/20/21 上**全得 0** ⇒ 三个边界读数全红",
                PreFixBlockChance(19, 0, 25, 1) != 60 && PreFixBlockChance(20, 0, 25, 1) != 75
                && PreFixBlockChance(21, 0, 25, 1) != 75,
                $"修前 19→{PreFixBlockChance(19, 0, 25, 1)} / 20→{PreFixBlockChance(20, 0, 25, 1)}"
                + $" / 21→{PreFixBlockChance(21, 0, 25, 1)}（要求 ≠ 60 / ≠ 75 / ≠ 75）");
            // 退化样本③（无盾闸门）：闸门是**承重**的 —— 去掉它（按"有盾"恒真算）1 级徒手亚马逊
            //   会得 (25−15)×25/2 = 125 ⇒ 钳 75%，与官方「无盾 = 0%」冲突 ⇒ 证明这条判据判的是闸门本身。
            Check("退化样本③（无盾闸门）：闸门去掉（`hasShield` 恒真）徒手亚马逊会算成 75% ≠ 官方 0%",
                PlayerStats.ComputeBlockChance(true, 25, 0, 25, 1) == 75,
                "去掉闸门：徒手亚马逊 = " + PlayerStats.ComputeBlockChance(true, 25, 0, 25, 1) + "%（官方 0%）");

            // ── ⑥ 事件链（真入口）：装盾 ⇒ `Events.EquipChanged` ⇒ 面板那一格（`BlockChance`）不再 0 ──
            player.CreateNew(PlayerClass.Barbarian, "Shield");
            var noShield = player.Stats.BlockChance;
            Check("装盾前（徒手）= 0%（既有读数：`Check1 … 格挡0%` / `row17.json` 指着它，⛔ 未放宽）",
                noShield == 0, $"BlockChance={noShield}（HasShield={player.Stats.HasShield}）");

            var shield = new ItemStack
            {
                itemId = smallShield.Id,
                name = "Small Shield(自检)",
                type = ItemType.Armor,
                defMin = smallShield.DefMin,
                defMax = smallShield.DefMax,
            };
            var equipArgs = new InventoryChangedArgs { gold = 0 };
            equipArgs.equip.Add(shield);
            bus.Emit(Events.EquipChanged, equipArgs);

            // 野蛮人 1 级：起始敏 20（`class_c.dex`）+ 盾 block 5 + BF 25
            //   ⇒ (20−15) × (5+25) / (2×1) = 150 / 2 = 75（硬编码自官方公式，不由被测代码现算）
            const int barbExpect = 75;
            Check("装盾后（Barbarian 1 级 + Small Shield）= 官方公式值 75%",
                player.Stats.BlockChance == barbExpect && barbExpect > 0,
                $"期望 {barbExpect}，实际 {player.Stats.BlockChance}（盾格挡加成 BonusBlock={player.Stats.BonusBlock}）");
            Check("装备日志：盾 1 件 / 带盾=True / 格挡上屏同一真值",
                CaptureLogger.Has("盾 1 件") && CaptureLogger.Has("带盾=True")
                && CaptureLogger.Has($"格挡 {barbExpect}%"),
                CaptureLogger.Last("[Equip] 装备生效"));

            // 再加一档**非上限**的读数（Lv1 各职业都顶到 75 ⇒ 单这一条分不出"公式对不对"）：
            //   直接置等级只为把钳制从读数里排除（走的是同一条 `Recompute`，不绕过公式）。
            //   野蛮人 Lv10 + Small Shield：敏 20 → (20−15) × (5+25) / (2×10) = 150/20 = 7
            player.Stats.Level = 10;
            player.Stats.Recompute();
            Check("装盾后（Barbarian Lv10 + Small Shield）= 官方公式值 7%（非上限档，公式能分道）",
                player.Stats.BlockChance == 7, $"期望 7，实际 {player.Stats.BlockChance}");

            bus.Emit(Events.EquipChanged, new InventoryChangedArgs());
            Check("卸下盾 ⇒ 回到 0%（闸门跟着「有没有盾」走，不是「有没有加成」）",
                player.Stats.BlockChance == 0 && !player.Stats.HasShield,
                $"BlockChance={player.Stats.BlockChance} HasShield={player.Stats.HasShield}");
        }

        // ═════════════════════════════════════════════════════════════════════
        //       （实机读数第 99 行）
        //       `[D2U3C] DTO name=S2203805 cls=Amazon level=1 … life=50/50 mana=15/15 stamina=**20/84**`
        //     活档（**只读**）：`client/setting/saves/S2203805.json` =
        //       `{version:1, name:S2203805, cls:1, level:1, str:20, dex:25, vit:20, eng:15,
        //         与现在的官方起始上限（50 / 15 / 84）**不同源**。
        //     出处（起始量 = 满值）：`charstats.txt:2..6` 的 `hpadd`(30)+起始体力(20)=50 / 起始精力(15) /
        //     类别：**数值类** ⇒ 运行时日志 + 断言（不截图、不进 Play）。
        //       `Module/View`（别的片把 View 改坏时整宿主编不过，本轮真实发生过一次），而 `itemcheck`
        // ═════════════════════════════════════════════════════════════════════
        private static void Step9c_Resources(AppContext ctx, PlayerModule player, RecordingEventBus bus)
        {
            Section("9c. ★ u52cur 三资源 cur/max：新建即满 / 旧档迁移 / 现行档沿用（退化样本）");

            // ── ① 新建角色进图 ⇒ cur == max（三资源逐条）──
            player.CreateNew(PlayerClass.Amazon, "CurNew");
            var cL = player.Life == player.MaxLife;
            var cM = player.Mana == player.MaxMana;
            var cS = player.Stamina == player.MaxStamina;
            Check("新建角色：cur == max（生命/法力/耐力逐条；官方起始量即满值起步）",
                cL && cM && cS,
                $"生命 {player.Life}/{player.MaxLife} 法力 {player.Mana}/{player.MaxMana} 耐力 {player.Stamina}/{player.MaxStamina}");
            Check("新建角色 1 级三资源 = 官方起始值（50 / 15 / 84，`charstats.txt`）",
                player.MaxLife == 50 && player.MaxMana == 15 && player.MaxStamina == 84,
                $"{player.MaxLife}/{player.MaxMana}/{player.MaxStamina}");

            // ── ② 新建 → 写档 → 读档 ⇒ 仍是 cur == max（现行版本档不许被"沿用"语义打坏）──
            var rt = new CharacterSave();
            player.WriteTo(rt);
            Check("写档：version 被写成当前 `GameConst.SaveVersion`（迁移判据的前提）",
                rt.version == GameConst.SaveVersion, $"档内 version={rt.version} 当前={GameConst.SaveVersion}");
            player.LoadFrom(rt);
            Check("新建档往返（写→读）后仍 cur == max（三资源）",
                player.Life == player.MaxLife && player.Mana == player.MaxMana && player.Stamina == player.MaxStamina,
                $"生命 {player.Life}/{player.MaxLife} 法力 {player.Mana}/{player.MaxMana} 耐力 {player.Stamina}/{player.MaxStamina}");

            // ── ③ 旧口径档（逐值抄活档 S2203805.json）⇒ 迁移后 cur == max ──
            var legacy = LegacySave();
            player.LoadFrom(legacy);
            Check("旧档（version < 当前，= 活档 S2203805 逐值）迁移后 三资源 cur == max（50/15/84）",
                player.Life == 50 && player.Mana == 15 && player.Stamina == 84
                && player.Life == player.MaxLife && player.Stamina == player.MaxStamina,
                $"生命 {player.Life}/{player.MaxLife} 法力 {player.Mana}/{player.MaxMana} 耐力 {player.Stamina}/{player.MaxStamina}");
            Check("旧档迁移留下了**一条**可定位 Info（不是每条资源一条）",
                CaptureLogger.Has("旧档迁移"), CaptureLogger.Last("旧档迁移"));

            // ── ④ 退化样本：把"旧档也走 `Min(cur,max)`"的**修前形状**喂进同一判据 ⇒ 必须变红 ──
            var preFixStam = PreFixLoadedCur(legacy.stamina, player.MaxStamina);        // = Min(20, 84) = 20
            Check("退化样本（旧档同判据）：修前形状只做 `Min(cur,max)` ⇒ 耐力得 20 ≠ 84（正是实机症状）",
                preFixStam == 20 && preFixStam != 84,
                $"修前形状耐力 = {preFixStam}/84（要求 ≠ 84；实机读数正是 20/84）");

            // ── ⑤ 现行版本档**必须沿用** cur（中局受伤档不许被补满：活档 SAArea1.json life=34）──
            var midGame = LegacySave();
            midGame.version = GameConst.SaveVersion;        // 现行版本
            midGame.life = 34;                              // 实测活档 SAArea1.json 的中局值
            midGame.stamina = 40;
            player.LoadFrom(midGame);
            Check("现行版本档沿用 cur：中局受伤档（life=34 / stamina=40）读档后**原样保留**（⛔ 不许改成补满）",
                player.Life == 34 && player.Stamina == 40,
                $"生命 {player.Life}/{player.MaxLife} 耐力 {player.Stamina}/{player.MaxStamina}");

            // ── ⑥ 降级路径：越界 / 缺失字段不崩、口径不变（既有上界不放宽）──
            var over = LegacySave();
            over.version = GameConst.SaveVersion;
            over.life = 9999;                               // 越上限 ⇒ 钳到 max（既有行为）
            over.stamina = 0;                               // "没存" ⇒ 满（既有行为）
            player.LoadFrom(over);
            Check("现行版本档：越上限的 cur 钳到 max、cur=0 视为「没存」⇒ 满（既有降级口径未放宽）",
                player.Life == player.MaxLife && player.Stamina == player.MaxStamina,
                $"生命 {player.Life}/{player.MaxLife} 耐力 {player.Stamina}/{player.MaxStamina}");

            var broken = LegacySave();
            broken.version = GameConst.SaveVersion;
            broken.name = null;                             // 缺失字段 ⇒ 退回默认名，不崩
            broken.cls = 0;
            var threw = false;
            try { player.LoadFrom(broken); } catch (Exception ex) { threw = true; Console.WriteLine("   " + ex); }
            Check("缺失字段（name=null / cls=0）的档不抛异常（降级路径照旧）", !threw, "LoadFrom 未抛异常");
            player.CreateNew(PlayerClass.Amazon, "CurNew");  // 复位，避免污染后续段
        }

        // ═════════════════════════════════════════════════════════════════════
        // 9d. u52block（链路级一跳）：真 `SaveModule.Load`（读盘）→ 真 `PlayerModule.LoadFrom`
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// <para>**为什么单开一节**：本宿主此前只编 `Module/Player/**`，而 `savecheck` 只编 Save（Player 是
        /// shim）⇒ 「真 `SaveModule.Load` → 真 `PlayerModule.LoadFrom`」这**一跳是断的**。而本轮那个 bug
        /// 恰恰是**单元级绿 / 链路级红**：`Load` 若把 `data.version` 抬到当前，下游
        /// `PlayerModule.cs:448` 的 `save.version &lt; GameConst.SaveVersion` 恒 false ⇒ 迁移分支成死代码；
        /// **跳过 `Load`**）**两边都绿**。§9c 与本节的唯一差别 = 夹具**从磁盘经真 `Load` 拿**。</para>
        /// <para>不复制 `SaveModule.cs` / 不镜像它的逻辑：本节调的就是 `Assets/Scripts` 里**同一份**源文件
        /// （由 `PlayerCheck.csproj` 链入）。槽位目录 = 临时目录，跑完删除；`Game.Config` / `ctx.Save` 复原。</para>
        /// </summary>
        private static void Step9d_SaveToPlayerHop(AppContext ctx, PlayerModule player)
        {
            Section("9d. ★ u52block 链路级一跳：真 SaveModule.Load（读盘）→ 真 PlayerModule.LoadFrom（老档耐力 20/84 断链点）");

            // 槽位目录 = 仓库根下**一次性产物目录**里的 `test/playercheck-u52hop/saves`
            // （一次性产物只许落那里；落点见下面那行 `Path.Combine`）
            var dir = System.IO.Path.Combine(ResolveProjectRoot(), ".ai-tmp", "test", "playercheck-u52hop");
            try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
            var savesDir = System.IO.Path.Combine(dir, "saves");
            System.IO.Directory.CreateDirectory(savesDir);

            var prevConfig = Game.Config;                         // 复原用（不给后续段留副作用）
            var prevSave = ctx.Save;
            Game.Config = new GameConfig { SettingDir = dir };    // 真 `SaveModule.Store` 靠它拼 <SettingDir>/saves
            var save = new SaveModule();
            ctx.Save = save;                                      // `ApplyOtherModules` 经 ctx 取其余模块（离线全 null ⇒ 只多几条 Warn）

            try
            {
                Check("1) 被验证对象就位：真 `SaveModule`（与 `Assets/Scripts` 同一份源文件）装上 `ctx.Save`、`Ready == true`",
                    save.Ready, "Ready=" + save.Ready + " 槽位目录=" + savesDir);

                // ── 夹具 = 活档 S2203805.json 的**逐值**副本，version 故意留成 `SaveVersion-1`（旧口径档）──
                var legacy = LegacySave();
                legacy.name = "HopLegacyHero";
                legacy.version = GameConst.SaveVersion - 1;
                System.IO.File.WriteAllText(System.IO.Path.Combine(savesDir, legacy.name + ".json"), SaveJson.Write(legacy));

                // ① 真 Load 读盘：版本号必须**原样保留**（这一跳的关键）
                var loaded = save.Load(legacy.name);
                Check("2) ★ 真 `SaveModule.Load` 从磁盘读出旧档 ⇒ `data != null`、`LastError == \"\"`、且 " +
                      "`data.version` 仍是档内的 " + (GameConst.SaveVersion - 1) + "（⛔ 不被抬到当前 —— " +
                      "否则下游 `PlayerModule.cs:448` 的判据恒 false、三资源迁移成死代码）",
                    loaded != null && save.LastError == "" && loaded.version == GameConst.SaveVersion - 1,
                    loaded == null ? "data=null LastError=\"" + save.LastError + "\""
                        : ("version=" + loaded.version + " gold=" + loaded.gold + " LastError=\"" + save.LastError + "\""));

                // ② 这个 data **不加工**直喂真 `PlayerModule.LoadFrom`（= `AppFlow` 的真实调用形状）
                var migBefore = CaptureLogger.Count("旧档迁移");       // ★ 段序即输入：只用**本条链**造成的增量
                var threw = false;
                if (loaded != null)
                {
                    try { player.LoadFrom(loaded); }
                    catch (Exception ex) { threw = true; Console.WriteLine("   " + ex); }
                }
                var migDelta = CaptureLogger.Count("旧档迁移") - migBefore;
                Check("3) 该 data **不加工**直喂真 `PlayerModule.LoadFrom` ⇒ 不抛异常 + **本条链真的触发了「旧档迁移」分支**" +
                      "（日志增量 ≥1；若 `Load` 把版本抬到当前，这里**不会**有增量）",
                    loaded != null && !threw && migDelta >= 1, "threw=" + threw + " 迁移日志增量=" + migDelta);

                Check("4) 迁移后三资源 cur == max，且**逐值** 生命 50 / 法力 15 / 耐力 84（实机症状 = 耐力 20/84）",
                    player.Life == 50 && player.Mana == 15 && player.Stamina == 84
                    && player.Life == player.MaxLife && player.Mana == player.MaxMana && player.Stamina == player.MaxStamina,
                    "生命 " + player.Life + "/" + player.MaxLife + " 法力 " + player.Mana + "/" + player.MaxMana
                    + " 耐力 " + player.Stamina + "/" + player.MaxStamina);

                // ④ 判据**能红**：把同一份夹具的版本改成"当前"（= 模拟旧契约「Load 抬版本」之后的形状）
                var asCurrent = LegacySave();
                asCurrent.name = "HopAsCurrentHero";
                asCurrent.version = GameConst.SaveVersion;         // = 旧契约（Load 抬版本）之后的形状
                var migBeforeCurrent = CaptureLogger.Count("旧档迁移");
                player.LoadFrom(asCurrent);
                var migDeltaCurrent = CaptureLogger.Count("旧档迁移") - migBeforeCurrent;
                Check("5) ★ 本条红 = 旧契约（Load 抬版本）复活：同一档但 `version == 当前` ⇒ 迁移**不触发**（日志增量 0）、" +
                      "耐力**停在 20 ≠ 84**（⇒ 证明 2)~4) 测的是「版本有没有被改动」这个过程，不是恒绿）",
                    player.Stamina == 20 && player.Stamina != player.MaxStamina && player.MaxStamina == 84
                    && migDeltaCurrent == 0,
                    "生命 " + player.Life + "/" + player.MaxLife + " 耐力 " + player.Stamina + "/" + player.MaxStamina
                    + " 迁移日志增量=" + migDeltaCurrent);
            }
            finally
            {
                player.CreateNew(PlayerClass.Amazon, "CurNew");    // 复位（与 §9c 末尾同一处置，避免污染后续段）
                ctx.Save = prevSave;
                Game.Config = prevConfig;
                try { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); } catch { }
            }
        }

        /// <summary>
        /// **旧口径活档的逐值副本**（`client/setting/saves/S2203805.json`，**只读**抄写；
        /// </summary>
        private static CharacterSave LegacySave()
        {
            return new CharacterSave
            {
                version = GameConst.SaveVersion - 1,
                name = "S2203805",
                cls = PlayerClass.Amazon,
                level = 1,
                exp = 0,
                str = 20, dex = 25, vit = 20, eng = 15,
                life = 60,          // 旧公式产物（= 起始体力 20 × 3）
                mana = 22,          // 旧公式产物（= 起始精力 15 × 1.5）
                stamina = 20,       // 旧公式产物（= 起始体力 20 × 1）—— 实机卡在这一格
                statPoints = 0, skillPoints = 0, gold = 0,
                gridX = 0, gridY = 0, mapSeed = 77928551,
            };
        }

        /// <summary>**修前形状**：读档时只做 `Min(cur, max)`（不判版本、不迁移）。仅用于退化样本。</summary>
        private static int PreFixLoadedCur(int savedCur, int max)
            => savedCur > 0 ? Mathf.Min(savedCur, max) : max;

        /// <summary>
        /// **修前形状**（旧 `ComputeBlockChance`：装备项 = 只有词缀 block，且"装备项 &lt;= 0 ⇒ 0%"）。
        /// 只用于**退化样本** —— 证明新判据真的在判"盾基材 block 那一环"（喂进去必须变红）。
        /// </summary>
        private static int PreFixBlockChance(int dex, int affixBlock, int classBlockFactor, int level)
        {
            if (affixBlock <= 0 || level <= 0) return 0;
            return Mathf.Clamp((dex - 15) * (affixBlock + classBlockFactor) / (2 * level), 0, 75);
        }

        /// <summary>按官方 `code` 在 `item_c` 里找一行（后缀/异常时返回 null）。</summary>
        private static Table.BaseItemRow FindItemRowByCode(string code)
        {
            var rows = Table.Tables.Default.Item.All();
            for (var i = 0; i < rows.Count; i++)
                if (rows[i] != null && rows[i].Code == code) return rows[i];
            return null;
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

            var a = new Vector3(0f, 0f, 0f);
            var b = new Vector3(10f, 0f, 0f);
            Check("CameraMath.Follow(dt<=0) 保持不动",
                CloverEngine.CameraMath.Follow(a, b, 0f, 0.12f) == a, "dt=0 → k=0 → 原样");
            Check("CameraMath.Follow(tau<=0) 直接吸附",
                CloverEngine.CameraMath.Follow(a, b, 0.02f, 0f) == b, "tau=0 → want");
            var mid = CloverEngine.CameraMath.Follow(a, b, 0.02f, 0.12f);
            Check("CameraMath.Follow(Vector3) 在两端之间（不过冲）", mid.x > 0f && mid.x < 10f, $"x={mid.x:0.000}");
            var midF = CloverEngine.CameraMath.Follow(0f, 10f, 0.02f, 0.12f);
            Check("CameraMath.Follow(Vector3) 与既有 float 版**同公式**（逐轴一致 ⇒ 下沉没有改数值）",
                Math.Abs(midF - mid.x) < 1e-6f, $"float={midF:0.######} vs Vector3.x={mid.x:0.######}");

            // 纯函数：**临界阻尼**跟随（带速度状态）—— 本项目相机跟随改用它（换向不摆滞后矢量）
            var vel = Vector3.zero;
            var sd0 = CloverEngine.CameraMath.SmoothDamp(Vector3.zero, b, ref vel, 0.02f, 0f);
            Check("CameraMath.SmoothDamp(dt<=0) 保持不动且速度清零", sd0 == Vector3.zero && vel == Vector3.zero, $"→ {sd0}");
            vel = Vector3.zero;
            var y = Vector3.zero;
            var overshoot = 0f;
            for (var i = 0; i < 200; i++)
            {
                y = CloverEngine.CameraMath.SmoothDamp(y, b, ref vel, 0.02f, Dt);
                if (y.x > b.x + 1e-4f) overshoot = MathF.Max(overshoot, y.x - b.x);
            }
            Check("CameraMath.SmoothDamp 收敛到目标且**不过冲**（临界阻尼）",
                Math.Abs(y.x - b.x) < 1e-3f && overshoot < 1e-4f,
                $"200 帧后 x={y.x:0.######}（目标 {b.x}），最大过冲 {overshoot:0.######}");
            // 稳态滞后 = speed×smoothTime（临界阻尼解析解 y = v·t − 2v/ω，ω=2/smoothTime）—— c5 的理论口径
            vel = Vector3.zero;
            var yy = Vector3.zero;
            var tt = 0f;
            for (var i = 0; i < 300; i++) { tt += Dt; yy = CloverEngine.CameraMath.SmoothDamp(yy, new Vector3(3f * tt, 0f, 0f), ref vel, 0.02f, Dt); }
            // 稳态滞后：连续解析值 = v×smoothTime；Unity 的**离散**实现实测更小（本机 0.577×）⇒ 断言写成
            // 「> 0 且 ≤ 解析上界」（下界非零 = 确实有阻尼跟随，不是刚性；上界 = 连续解，不与实现细节耦合）。
            var lagMeasured = 3f * tt - yy.x;
            Check("CameraMath.SmoothDamp 稳态滞后 ∈ (0, speed×smoothTime]（连续解析值是上界）",
                lagMeasured > 0f && lagMeasured <= 3f * 0.02f + 1e-4f,
                $"实测滞后 {lagMeasured:0.######} 格 = 连续解析值 {3f * 0.02f:0.######} 格的 " +
                $"{lagMeasured / (3f * 0.02f):0.000} 倍（Unity 离散实现偏小；解析值当上界用）");

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
                    //   `CameraBounds.ClampFocusGrid`（格空间）——旧口径用世界 AABB 的 `ClampFocus`，
                    //   会与生产"两套算法"（AABB 说没夹、生产说夹了 ⇒ 分支选错、断言判的是另一件事）。
                    var clamped = CameraBounds.ClampFocusGrid(new Vector2(focusWorld.x, focusWorld.y), cs.W, cs.H, halfW, halfH);
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

            // ═════════════════════════════════════════════════════════════════
            // 11.10 black-why2：贴边不露「地图外虚空」——**生产** `CameraPosForFocus`
            //       这边断言**生产实现**真的做到了（不然规格永远变不了绿）。
            // ═════════════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("    ── 贴边不露虚空：生产 CameraPosForFocus 的可见格范围 ⊆ 地图 ──");
            const float eps = 1e-3f;
            var covA = 1920f / 1080f;
            var halfWA = CameraRig.DefaultOrthographicSize * covA;
            var halfHA = CameraRig.DefaultOrthographicSize;
            var edgeCases = new[]
            {
                new { W = 80, H = 80, G = new Vector2Int(1, 20),  Tag = "血腥荒野 80×80 西边界(1,20)（实测黑窗现场）" },
                new { W = 80, H = 80, G = new Vector2Int(0, 0),   Tag = "血腥荒野 80×80 北角(0,0)" },
                new { W = 80, H = 80, G = new Vector2Int(79, 79), Tag = "血腥荒野 80×80 南角(79,79)" },
                new { W = 80, H = 80, G = new Vector2Int(0, 79),  Tag = "血腥荒野 80×80 西角(0,79)" },
                new { W = 80, H = 80, G = new Vector2Int(79, 0),  Tag = "血腥荒野 80×80 东角(79,0)" },
                new { W = 80, H = 80, G = new Vector2Int(40, 40), Tag = "血腥荒野 80×80 正中(40,40)" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(0, 0),       Tag = "城镇 西北角(0,0)" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(31, 31),     Tag = "城镇 东南角(31,31)" },
            };
            foreach (var ec in edgeCases)
            {
                var fw = Iso.GridToWorld(ec.G);
                var cam = CameraRig.CameraPosForFocus(fw, ec.W, ec.H,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);

                // 同口径的「修前」对照：只做 AABB 夹制（`ClampFocus` 的旧生产路径）
                CameraRig.MapWorldBounds(ec.W, ec.H, out var oMin, out var oMax);
                var old = CameraRig.ClampFocus(new Vector2(fw.x, fw.y), oMin, oMax, halfWA, halfHA);

                var offNew = OffGridAmount(cam.x, cam.y, halfWA, halfHA, ec.W, ec.H);
                var offOld = OffGridAmount(old.x, old.y, halfWA, halfHA, ec.W, ec.H);
                var inView = Math.Abs(cam.x - fw.x) <= halfWA + eps && Math.Abs(cam.y - fw.y) <= halfHA + eps;

                Check($"[{ec.W}×{ec.H}] {ec.Tag} ⇒ 焦点（玩家）仍在视口内（不许把玩家顶出画面）",
                    inView,
                    $"|机位−焦点|=({Math.Abs(cam.x - fw.x):0.##},{Math.Abs(cam.y - fw.y):0.##}) 上限=({halfWA:0.##},{halfHA:0.##})");
                Check($"[{ec.W}×{ec.H}] {ec.Tag} ⇒ 图外格量不差于修前（{offOld:0.##} → {offNew:0.##}）",
                    offNew <= offOld + eps,
                    $"机位=({cam.x:0.##},{cam.y:0.##}) 修前机位=({old.x:0.##},{old.y:0.##})");

                // 「可见格范围 ⊆ 地图」只在**推得动**的用例上严格成立（角落要推 > 半屏 ⇒ 与"玩家在画面内"不可兼得）
                var feasible = (ec.G.x == 1 && ec.G.y == 20) || (ec.G.x == 40 && ec.G.y == 40);
                if (!feasible) continue;
                Check($"[{ec.W}×{ec.H}] {ec.Tag} ⇒ 可见格范围 **严格** ⊆ 地图（图外格量 = 0）",
                    offNew <= eps, $"offNew={offNew:0.####} 机位=({cam.x:0.##},{cam.y:0.##})");
            }

            // ── 反证（fail-to-pass）：**修前的生产实现**（只 AABB 夹制）在同一点必然露虚空 ──
            {
                const int mw = 80, mh = 80;
                var fw = Iso.GridToWorld(1, 20);
                CameraRig.MapWorldBounds(mw, mh, out var bMin, out var bMax);
                var old = CameraRig.ClampFocus(new Vector2(fw.x, fw.y), bMin, bMax, halfWA, halfHA);
                float loX, hiX, loY, hiY;
                CameraRig.VisibleGridRect(old.x, old.y, halfWA, halfHA, out loX, out hiX, out loY, out hiY);
                Check("反证：只做 AABB 夹制（修前）在同一点**必然**露出地图外（本断言抓得住该缺陷）",
                    loX < -0.01f,
                    $"修前可见格 x∈[{loX:0.##},{hiX:0.##}]（x<0 的那几列在图外=黑）机位=({old.x:0.##},{old.y:0.##})");
            }

            // ═════════════════════════════════════════════════════════════════
            //   前一片只断言「可见格 ⊆ 地图」⇒ 判据全绿、实机玩家却被顶到画面角落
            //   （play-verify 实测 玩家↔相机 屏幕距离 p50=598.6px / max=1065.8px / 75.8% 帧 > 半格）。
            //   本组用例覆盖 **地图四角 + 四边 + 图心**（80×80 血腥荒野、56×40 城镇（实机那张）、
            //   32×32 `GameConst.Town`），逐条把焦点投影到视口归一化坐标，
            //   判据 = 焦点必须落在 [inset, 1−inset]（inset = (1 − FocusSafeMarginRatio)/2）。
            //   退化校验见 report-camerafollow.md：把生产退回「夹焦点」（去掉主角可见预算）
            //      ⇒ 四角用例必须变红，否则本判据是摆设。
            // ═════════════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("    ── 玩家焦点必须在视口内（含安全边距）：四角 + 四边 + 图心 ──");
            var insetF = (1f - CameraBounds.FocusSafeMarginRatio) * 0.5f;
            Console.WriteLine($"  FocusSafeMarginRatio={CameraBounds.FocusSafeMarginRatio:0.##} ⇒ 视口安全边距 inset={insetF:0.###}" +
                              $"（焦点归一化坐标必须落在 [{insetF:0.###}, {1f - insetF:0.###}]）");
            var focusCases = new[]
            {
                new { W = 80, H = 80, G = new Vector2Int(0, 0),    Tag = "80×80 西北角(0,0)" },
                new { W = 80, H = 80, G = new Vector2Int(79, 0),   Tag = "80×80 东北角(79,0)" },
                new { W = 80, H = 80, G = new Vector2Int(0, 79),   Tag = "80×80 西南角(0,79)" },
                new { W = 80, H = 80, G = new Vector2Int(79, 79),  Tag = "80×80 东南角(79,79)" },
                new { W = 80, H = 80, G = new Vector2Int(40, 0),   Tag = "80×80 北边(40,0)" },
                new { W = 80, H = 80, G = new Vector2Int(40, 79),  Tag = "80×80 南边(40,79)" },
                new { W = 80, H = 80, G = new Vector2Int(0, 40),   Tag = "80×80 西边(0,40)" },
                new { W = 80, H = 80, G = new Vector2Int(79, 40),  Tag = "80×80 东边(79,40)" },
                new { W = 80, H = 80, G = new Vector2Int(1, 20),   Tag = "80×80 西边界(1,20)（mapcheck §29 同点）" },
                new { W = 80, H = 80, G = new Vector2Int(40, 40),  Tag = "80×80 图心(40,40)" },
                new { W = 56, H = 40, G = new Vector2Int(0, 0),    Tag = "56×40 西北角(0,0)" },
                new { W = 56, H = 40, G = new Vector2Int(55, 0),   Tag = "56×40 东北角(55,0)" },
                new { W = 56, H = 40, G = new Vector2Int(0, 39),   Tag = "56×40 西南角(0,39)" },
                new { W = 56, H = 40, G = new Vector2Int(55, 39),  Tag = "56×40 东南角(55,39)" },
                new { W = 56, H = 40, G = new Vector2Int(28, 0),   Tag = "56×40 北边(28,0)" },
                new { W = 56, H = 40, G = new Vector2Int(28, 39),  Tag = "56×40 南边(28,39)" },
                new { W = 56, H = 40, G = new Vector2Int(0, 20),   Tag = "56×40 西边(0,20)" },
                new { W = 56, H = 40, G = new Vector2Int(55, 20),  Tag = "56×40 东边(55,20)" },
                new { W = 56, H = 40, G = new Vector2Int(46, 37),  Tag = "56×40 (46,37)（play-verify 实机回归路径点）" },
                new { W = 56, H = 40, G = new Vector2Int(20, 33),  Tag = "56×40 (20,33)（play-verify 实机回归路径点）" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(0, 0),   Tag = "城镇 西北角(0,0)" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(31, 31), Tag = "城镇 东南角(31,31)" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(16, 0),  Tag = "城镇 北边(16,0)" },
                new { W = GameConst.TownWidth, H = GameConst.TownHeight, G = new Vector2Int(0, 16),  Tag = "城镇 西边(0,16)" },
            };
            var worstInset = float.MaxValue;
            var worstTag = "";
            foreach (var fc in focusCases)
            {
                var fw = Iso.GridToWorld(fc.G);
                // 夹**机位**（camera = 想停在玩家身上的理想机位），焦点只当位移上限的参照
                var camF = CameraRig.CameraPosForCamera(fw, fw, fc.W, fc.H,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);
                var vpf = CameraRig.WorldToViewport(fw, camF, CameraRig.DefaultOrthographicSize, covA);

                // 焦点到视口四边的最小余量（<0 = 焦点已经在画面外）
                var marginF = Mathf.Min(Mathf.Min(vpf.x, 1f - vpf.x), Mathf.Min(vpf.y, 1f - vpf.y));
                if (marginF < worstInset) { worstInset = marginF; worstTag = fc.Tag; }

                var okF = vpf.x >= insetF - 1e-3f && vpf.x <= 1f - insetF + 1e-3f
                       && vpf.y >= insetF - 1e-3f && vpf.y <= 1f - insetF + 1e-3f;
                Check($"[{fc.W}×{fc.H}] {fc.Tag} ⇒ 焦点（玩家）在视口安全边距内 vp=({vpf.x:0.####},{vpf.y:0.####})",
                    okF,
                    $"余量={marginF:0.####}（下限 {insetF:0.####}；<0 = 玩家真的跑出画面）" +
                    $" 屏幕偏移=({Math.Abs(camF.x - fw.x) / (CameraRig.DefaultOrthographicSize * covA):0.###}," +
                    $"{Math.Abs(camF.y - fw.y) / CameraRig.DefaultOrthographicSize:0.###})×半屏");
            }
            Check("所有用例里最紧的一条仍有非负余量（焦点从未被顶到画面边框上）",
                worstInset >= insetF - 1e-3f,
                $"最紧 = {worstTag} 余量={worstInset:0.####}（下限 {insetF:0.####}）");

            // ═════════════════════════════════════════════════════════════════
            //   (a) 可见格范围 ⊆ 地图（地图外虚空 = 0）
            //   (b) 玩家（焦点）仍在视口内
            //   11.10 只断言 (a)、11.11 只断言 (b)，各自都能"绿"；实机读数（
            //     · 血腥荒野 80×80：四边内缩 8 格（cv3 实测 outerGx=[8..71] outerGy=[8..71]，ringFromEdge=8）
            //     · 罗格营地 56×40：实测最外侧可走格 (8,20)(8,31)(55,25)(55,27)(27,17)(27,38)
            //       （桥面 `x=W-1` 与南边界行 `gy=38` 是 map-border2 明确保留的例外）
            //   不弱化任何既有断言（只新增）；真实可走区**之外**的最外圈另列，如实打印
            //      "两条不可兼得"的两个数 —— 那属于地图边界环该解决的事，不是相机。
            // ═════════════════════════════════════════════════════════════════
            Console.WriteLine();
            Console.WriteLine("    ── 贴边同时判：可见格 ⊆ 地图 **且** 玩家在视口内（用例形状 = 实机实测的可走区）──");
            var bothTotal = 0;
            var bothOk = 0;
            var worstBoth = float.MaxValue;
            var worstBothTag = "";

            // (1) 野外 80×80：可走区 = 四边内缩 8 格 ⇒ 取四角 + 四边中点（8 个最外侧可走格）
            var ringCells = new[]
            {
                new Vector2Int(8, 8), new Vector2Int(8, 71), new Vector2Int(71, 8), new Vector2Int(71, 70),
                new Vector2Int(8, 39), new Vector2Int(71, 39), new Vector2Int(39, 8), new Vector2Int(39, 71),
            };
            foreach (var c in ringCells)
            {
                var fw = Iso.GridToWorld(c);
                var camR = CameraRig.CameraPosForCamera(fw, fw, 80, 80,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);
                var vpR = CameraRig.WorldToViewport(fw, camR, CameraRig.DefaultOrthographicSize, covA);
                var marginR = Mathf.Min(Mathf.Min(vpR.x, 1f - vpR.x), Mathf.Min(vpR.y, 1f - vpR.y));
                var offTileR = OffTileAmount(camR.x, camR.y, halfWA, halfHA, 80, 80);
                bothTotal++;
                if (offTileR <= eps && marginR >= -eps) bothOk++;
                if (marginR < worstBoth) { worstBoth = marginR; worstBothTag = $"80×80({c.x},{c.y})"; }
                Check($"[80×80] 最外侧可走格({c.x},{c.y}) ⇒ 可见格 ⊆ 地图 且 玩家在视口内",
                    offTileR <= eps && marginR >= -eps,
                    $"两条数：offTile={offTileR:0.####}（0=无虚空） margin={marginR:0.####}（<0=玩家出画）" +
                    $" 机位=({camR.x:0.##},{camR.y:0.##})");
            }

            // (2) 城镇 56×40：实机量到的最外侧可走格（含桥面 x=W-1 与南边界行 gy=38 两处例外）
            var townEdgeCells = new[]
            {
                new Vector2Int(8, 20), new Vector2Int(8, 31), new Vector2Int(55, 25),
                new Vector2Int(55, 27), new Vector2Int(27, 17), new Vector2Int(27, 38),
            };
            foreach (var c in townEdgeCells)
            {
                var fw = Iso.GridToWorld(c);
                var camR = CameraRig.CameraPosForCamera(fw, fw, GameConst.TownWidth, GameConst.TownHeight,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);
                var vpR = CameraRig.WorldToViewport(fw, camR, CameraRig.DefaultOrthographicSize, covA);
                var marginR = Mathf.Min(Mathf.Min(vpR.x, 1f - vpR.x), Mathf.Min(vpR.y, 1f - vpR.y));
                var offTileR = OffTileAmount(camR.x, camR.y, halfWA, halfHA, GameConst.TownWidth, GameConst.TownHeight);
                bothTotal++;
                if (offTileR <= eps && marginR >= -eps) bothOk++;
                if (marginR < worstBoth) { worstBoth = marginR; worstBothTag = $"56×40({c.x},{c.y})"; }
                Check($"[56×40] 最外侧可走格({c.x},{c.y}) ⇒ 可见格 ⊆ 地图 且 玩家在视口内",
                    offTileR <= eps && marginR >= -eps,
                    $"两条数：offTile={offTileR:0.####}（0=无虚空） margin={marginR:0.####}（<0=玩家出画）" +
                    $" 机位=({camR.x:0.##},{camR.y:0.##})");
            }

            Check("贴边可走区：两条判据**同时**成立的用例 = 全部（没有任何一条被悄悄放宽）",
                bothOk == bothTotal && bothTotal > 0,
                $"成立 {bothOk}/{bothTotal}；最紧一条 = {worstBothTag} margin={worstBoth:0.####}");

            // (3) 对照：**不可达**的最外圈。两条在这里数学上不可兼得（前两片都栽在这上面）
            //     ⇒ 如实打印两个数，并断言「玩家永不被顶出画面」这一条**仍然**成立（cap=1 的硬保证）。
            var unreachable = new[]
            {
                new { W = 80, H = 80, X = 0,  Y = 0,  Tag = "80×80 北角(0,0)" },
                new { W = 56, H = 40, X = 0,  Y = 0,  Tag = "56×40 西北角(0,0)" },
                new { W = 56, H = 40, X = 55, Y = 39, Tag = "56×40 东南角(55,39)" },
            };
            foreach (var uc in unreachable)
            {
                var fwU = Iso.GridToWorld(new Vector2Int(uc.X, uc.Y));
                var camU = CameraRig.CameraPosForCamera(fwU, fwU, uc.W, uc.H,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);
                var vpU = CameraRig.WorldToViewport(fwU, camU, CameraRig.DefaultOrthographicSize, covA);
                var marginU = Mathf.Min(Mathf.Min(vpU.x, 1f - vpU.x), Mathf.Min(vpU.y, 1f - vpU.y));
                var offTileU = OffTileAmount(camU.x, camU.y, halfWA, halfHA, uc.W, uc.H);
                Check($"[{uc.W}×{uc.H}] {uc.Tag}（不可达，仅对照）⇒ 玩家仍不被顶出画面（cap=1 的硬保证）",
                    marginU >= -eps,
                    $"已知取舍两条数：offTile={offTileU:0.####}（>0 = 此处确实藏不住虚空，须由**地图边界环**解决）" +
                    $" margin={marginU:0.####} 机位=({camU.x:0.##},{camU.y:0.##})");
            }

            // (4) cam-verify 追补（主 agent 追补口径）：**真实可达性** —— 从出生点洪水填充，
            //     取「离地图边界最近的可达格」。这才是"玩家真的能走到多靠边"，不是人造落点
            //     判据：可达格离边界 ≥ (半屏半跨 a+b) 格 ⇔「零虚空」与「玩家居中」处处同时成立；
            var im = (mapObj as Diablo2.Module.IMapModule) ?? ctx.Map;
            if (im != null && im.IsGenerated)
            {
                int w4 = im.Width, h4 = im.Height;
                var seen = new bool[w4 * h4];
                var q = new System.Collections.Generic.Queue<Vector2Int>();
                var sp4 = im.SpawnPoint;
                if (im.Walkable(sp4)) { seen[sp4.y * w4 + sp4.x] = true; q.Enqueue(sp4); }
                var minD = int.MaxValue; var minCell = sp4; var reached = 0;
                var nb = new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
                while (q.Count > 0)
                {
                    var c = q.Dequeue(); reached++;
                    var d = Math.Min(Math.Min(c.x, c.y), Math.Min(w4 - 1 - c.x, h4 - 1 - c.y));
                    if (d < minD) { minD = d; minCell = c; }
                    foreach (var s in nb)
                    {
                        var nn = new Vector2Int(c.x + s.x, c.y + s.y);
                        if (nn.x < 0 || nn.y < 0 || nn.x >= w4 || nn.y >= h4) continue;
                        if (seen[nn.y * w4 + nn.x] || !im.Walkable(nn)) continue;
                        seen[nn.y * w4 + nn.x] = true; q.Enqueue(nn);
                    }
                }
                // 零虚空要求**机位**离地图边界 ≥ (a+b)/2 格（a=halfW/HalfW、b=halfH/HalfH；
                var need = (halfWA / Iso.HalfW + halfHA / Iso.HalfH) * 0.5f;
                var fw4 = Iso.GridToWorld(minCell);
                var cam4 = CameraRig.CameraPosForCamera(fw4, fw4, w4, h4,
                    CameraRig.DefaultOrthographicSize, covA, -CameraRig.CameraDistance);
                var vp4 = CameraRig.WorldToViewport(fw4, cam4, CameraRig.DefaultOrthographicSize, covA);
                var margin4 = Mathf.Min(Mathf.Min(vp4.x, 1f - vp4.x), Mathf.Min(vp4.y, 1f - vp4.y));
                var off4 = OffTileAmount(cam4.x, cam4.y, halfWA, halfHA, w4, h4);
                var pxPerUnit4 = 1080f / (2f * CameraRig.DefaultOrthographicSize);
                var dist4 = Math.Sqrt(Math.Pow((fw4.x - cam4.x) * pxPerUnit4, 2) + Math.Pow((fw4.y - cam4.y) * pxPerUnit4, 2));
                Console.WriteLine($"  ★ 可达区洪水填充：area={im.Area} {w4}×{h4} 可达格={reached}" +
                                  $" 最靠边可达格=({minCell.x},{minCell.y}) 离边界={minD} 格（零虚空要求 ≥ {need:0.###} 格）");
                Check($"★ 可达的最靠边格({minCell.x},{minCell.y})离边界 {minD} 格：该格上两条底线（零虚空 / 玩家在视口内）仍成立",
                    off4 <= eps && margin4 >= -eps,
                    $"两条数：offTile={off4:0.####}（0=无虚空） margin={margin4:0.####}（<0=出画）" +
                    $" 玩家↔相机屏幕距离={dist4:0.#}px" +
                    (dist4 > 72.0 ? " ⇒ **居中判据不成立 = 剩余缺陷（地图侧）**" : " ⇒ 居中判据成立") +
                    $" 机位=({cam4.x:0.##},{cam4.y:0.##})");
            }

            rig.Reset();
            Check("Reset 后正交尺寸回到默认、跟随与震动清空",
                Math.Abs(rig.OrthographicSize - CameraRig.DefaultOrthographicSize) < 1e-4f && !rig.IsShaking,
                $"size={rig.OrthographicSize}");
        }

        /// <summary>
        /// black-why2：**图外格量** = 可见格包围盒越过地图矩形的**总格数**（0 = 整屏都在图内）。
        /// 比「面积占比」更适合当判据：它是**格**口径，与 `VisibleGridRect` 同源，可直接读出"越了几列/几行"。
        /// </summary>
        private static float OffGridAmount(float camX, float camY, float halfW, float halfH, int mw, int mh)
        {
            float loX, hiX, loY, hiY;
            CameraRig.VisibleGridRect(camX, camY, halfW, halfH, out loX, out hiX, out loY, out hiY);
            var off = 0f;
            if (loX < 0f) off += -loX;
            if (hiX > mw - 1) off += hiX - (mw - 1);
            if (loY < 0f) off += -loY;
            if (hiY > mh - 1) off += hiY - (mh - 1);
            return off;
        }

        /// <summary>
        /// cam-verify：**真实"虚空"格量** = 可见格包围盒越过 `[0,W)×[0,H)` 的总格数（0 = 整屏都有地砖）。
        /// <para>与 `OffGridAmount` 的口径差别（两处都打印，避免"换个口径变绿"）：
        /// 格子 g 覆盖连续区间 `[g, g+1)`（= `Iso` 的 `WorldToGridContinuous` 口径）
        /// ⇒ 地图真正铺了砖的范围是 `[0,W)×[0,H)`；`OffGridAmount` 用 `W-1` 作上界，
        /// 会把**最后一整圈格**算成图外（那圈其实有地砖）。</para>
        /// </summary>
        private static float OffTileAmount(float camX, float camY, float halfW, float halfH, int mw, int mh)
        {
            float loX, hiX, loY, hiY;
            CameraRig.VisibleGridRect(camX, camY, halfW, halfH, out loX, out hiX, out loY, out hiY);
            var off = 0f;
            if (loX < 0f) off += -loX;
            if (hiX > mw) off += hiX - mw;
            if (loY < 0f) off += -loY;
            if (hiY > mh) off += hiY - mh;
            return off;
        }

        /// <summary>
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

            // 12.1 目标变化判定（纯函数）
            var from = new Vector2Int(5, 5);
            Check("ShouldRetarget：首次按住 → true",
                InputReader.ShouldRetarget(from, null, new Vector2Int(6, 5)), "(5,5) 按住到 (6,5)");
            Check("ShouldRetarget：目标没变 → false（唯一的逐帧节流：同一格不重复跑 A*）",
                !InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(6, 5)), "同一格");
            Check("ShouldRetarget：★ U33 差 1 格 → true（旧口径这里返回 false ⇒ 鼠标慢移永远不重算）",
                InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(7, 5)), "差 1 格");
            Check("ShouldRetarget：差 ≥2 格 → true",
                InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(8, 5)), "差 2 格");
            Check("ShouldRetarget：★ U33 目标移到脚下格 → true（由 MoveTo 的「已在目标格 ⇒ 清空路径」落地）",
                InputReader.ShouldRetarget(from, new Vector2Int(9, 9), from), "now == 玩家当前格");

            // 12.1b U33 矩阵式断言（**真因所在的那一型**：鼠标**只挪了 1 格**也要重算）
            //   棋局：上一次下发的目标 G0 = M + (1,0)（滚滚手的鼠标在当前目标旁挪一格）
            //   ⇒ 本次鼠标格 M 取：脚下格 + 8 邻域 + 距离 2 圈（共 25 格）⇒ **全部应为 true**。
            //   退化：旧口径在这里「next== 玩家格」与「差 ≤1 格」两条都返回 false ⇒ 25 格全红。
            var miss = new List<string>();
            var cells = 0;
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    var m = new Vector2Int(from.x + dx, from.y + dy);
                    var prev = new Vector2Int(m.x + 1, m.y);       // 鼠标从上一次目标只挪了 1 格
                    cells++;
                    if (InputReader.ShouldRetarget(from, prev, m)) continue;
                    miss.Add($"M={m} 距P={Iso.GridDistance(from, m)}");
                }
            }
            Check($"★ U33 矩阵：鼠标每帧只挪 1 格时，脚下格+8 邻域+距离 2 圈共 {cells} 格 ⇒ 全部重算",
                miss.Count == 0,
                $"总共 {cells} 格，未重算 {miss.Count} 格：" + (miss.Count > 0 ? string.Join("，", miss) : "无"));
            Check("★ U33 矩阵补一格：本次鼠标 == 上一次目标 ⇒ 不重算（仍在节流，不是每帧跑 A*）",
                !InputReader.ShouldRetarget(from, new Vector2Int(6, 5), new Vector2Int(6, 5)), "M=上一次目标=(6,5)");

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

            // 12.2b R1-E 的 S2：**点 UI 的那次左键不许被读成"点地面"**
            //   两种情形都要覆盖：①「UI 面板开着的区域」（拦掉）②「面板外的地面」（照旧）。
            //   判据 = 纯函数真值表（4 行）+ 给 `PointerOverUi` 注入替身走一遍**真实入口**
            //   （离线宿主没有真 EventSystem，所以走注入；见 `InputReader.PointerOverUi` 的注释）。
            Check("UiEatsIntent 真值表：按下+指针在 UI ⇒ 吃掉；按下+指针不在 UI ⇒ 不吃（面板外照样点地面）",
                InputReader.UiEatsIntent(true, true)
                && !InputReader.UiEatsIntent(true, false)
                && !InputReader.UiEatsIntent(false, true)
                && !InputReader.UiEatsIntent(false, false),
                "true/true→true；true/false→false；false/*→false");

            input.BeginFrame();
            input.MouseDown();
            reader.Poll();
            reader.OverrideHoverGrid(new Vector2Int(3, 3));      // 给一个"表面在 UI 之下的地面格"
            reader.PointerOverUi = () => true;                   // 替身：指针压在 UI 上

            var hitLogs = CaptureLogger.Count("[R1-E] S2");
            Check("指针在 UI 上 ⇒ 单击**不产生**落点（连反投影都不走 ⇒ 下游不会 Emit MoveCommand）",
                !reader.TryGetGroundClick(out _), "TryGetGroundClick=false（UI 命中拦截）");
            Check("指针在 UI 上 ⇒ 按住也不产生落点（否则同帧的 _held 会从那一支漏出一条移动/攻击）",
                !reader.TryGetGroundHoldTarget(out _), "TryGetGroundHoldTarget=false");
            Check("拦截时留下一条可定位的 `[R1-E] S2` 日志（且只报一次）",
                CaptureLogger.Count("[R1-E] S2") == hitLogs + 1, CaptureLogger.Last("[R1-E] S2"));

            reader.PointerOverUi = () => false;                  // 替身：指针在面板外的地面
            Check("指针不在 UI 上 ⇒ 拦截不生效（本宿主无相机 ⇒ 仍被相机那条挡下，与改动前逐字一致）",
                !reader.TryGetGroundClick(out _) && CaptureLogger.Has("TryGetGroundClick：找不到主相机"),
                "面板外可点地面：这条路未被 UI 判定影响");
            Check("第二帧不再重复播 S2 日志（『只报一次』语义）",
                CaptureLogger.Count("[R1-E] S2") == hitLogs + 1, $"次数 {CaptureLogger.Count("[R1-E] S2")}");

            // 复位：只清替身，**不调 Reset()** —— 避免扰动后面几段对"只报一次"计数与相机日志的断言
            reader.PointerOverUi = null;

            input.BeginFrame();          // 松开
            input.MouseUp();
            reader.Poll();
            Check("松开后 PrimaryHeld=false、PrimaryUp=true", !reader.PrimaryHeld && reader.PrimaryUp,
                $"held={reader.PrimaryHeld} up={reader.PrimaryUp}");

            // 12.3 本段原为「方向键备选移动」读取器断言（已随该功能整体删除，见验收表 U-1）。
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

            // ── A2. S3：怪物按**贴图实际矩形**命中（原版口径：精灵覆盖到就算悬停到）──
            //   实机依据：`s3_evidence.tsv` 的 HOVER round=1 —— 鼠标从怪 (15,53) 的脚下沿屏幕上移
            //   0/24/48/72px 仍解出 (15,53) 命中；**96px 起**解出 (14,52)（≠ 脚下格）⇒ 旧口径
            //   hasTarget=False（怪的上半身完全无反馈）。这里注入"贴图矩形覆盖到上半身那一格的世界点"，
            //   断言：① 旧口径（只给格）**仍然不命中**（改动没污染既有语义）；② 新口径命中；
            //   ③ 世界点在矩形外仍然不命中（证明不是"把阈值放大"）；④ 脚下格精确命中仍优先。
            {
                var savedRect = reader.Hover.MonsterSpriteRect;
                // 「怪上半身覆盖到的那一格」= 与怪相邻、且**不是**别的悬停用例的格（避免把 item/npc 命中算进来）
                var bodyCell = monGrid;
                var bodyOffs = new[]
                {
                    new Vector2Int(-1, -1), new Vector2Int(1, 1), new Vector2Int(1, -1),
                    new Vector2Int(-1, 1), new Vector2Int(0, 2), new Vector2Int(2, 0)
                };
                for (var k = 0; k < bodyOffs.Length; k++)
                {
                    var c = monGrid + bodyOffs[k];
                    if (c == itemGrid || c == npcGrid || c == emptyGrid) continue;
                    bodyCell = c; break;
                }
                var worldInBody = (Vector2)Iso.GridToWorld(bodyCell) + new Vector2(0.1f, 0.1f);
                reader.Hover.MonsterSpriteRect = id => id == monsterId
                    ? (Rect?)new Rect(worldInBody.x - 0.5f, worldInBody.y - 0.5f, 1f, 1f)
                    : null;
                Console.WriteLine($"    贴图矩形用例：怪脚下格 {monGrid}、上半身格 {bodyCell}、世界点 {worldInBody}");

                reader.OverrideHoverGrid(bodyCell);
                var tOld = reader.UpdateHover(true);
                Check("★ 旧口径不变：合成格（无配对世界点）落在怪上半身格 ⇒ 仍不命中",
                    !tOld.hasTarget,
                    $"hasTarget={tOld.hasTarget} cursor={tOld.cursor} 格=({tOld.gridX},{tOld.gridY})");

                var tNew = reader.Hover.Resolve(bodyCell, worldInBody);
                Check("★ 新口径：贴图矩形覆盖到鼠标世界点 ⇒ 命中该怪（cursor=Attack / id 正确 / 格=悬停格）",
                    tNew.hasTarget && tNew.cursor == CursorKind.Attack && tNew.id == monsterId
                    && tNew.gridX == bodyCell.x && tNew.gridY == bodyCell.y,
                    $"hasTarget={tNew.hasTarget} cursor={tNew.cursor} id={tNew.id} 格=({tNew.gridX},{tNew.gridY})");

                var tMiss = reader.Hover.Resolve(bodyCell, worldInBody + new Vector2(9f, 9f));
                Check("★ 新口径：世界点在矩形之外 ⇒ 仍不命中（不是把命中范围放大）",
                    !tMiss.hasTarget,
                    $"hasTarget={tMiss.hasTarget} cursor={tMiss.cursor}");

                reader.Hover.MonsterSpriteRect = id => null;          // 拿不到贴图 ⇒ 必须退回旧口径
                var tNoRect = reader.Hover.Resolve(bodyCell, worldInBody);
                Check("★ 无贴图矩形（异步未加载/离线）⇒ 退回旧口径，不命中", !tNoRect.hasTarget,
                    $"hasTarget={tNoRect.hasTarget}");

                reader.Hover.MonsterSpriteRect = savedRect;
            }

            Check("留下了悬停命中日志（验收 #14 取证）", CaptureLogger.Has("[Hover] 首个悬停命中"),
                CaptureLogger.Last("[Hover]"));

            // ── A3. hover-probe 片：`D2.Input.HoverChanged` **往返**（发送方 → 真实订阅回调）──
            //   为什么补它：状态矩阵 L3801/L3802 的判定是「有订阅者（被消费）」，旧证据只引
            //   `Events.cs` / `Contracts.cs`（事件的**声明处本身**）⇒ 只证得出"事件名 + 载荷类存在"，
            //   证不出"真有人收到"。这里订阅一个真实回调，走真实入口 `InputReader.UpdateHover`
            //   派发，断言回调**真的被调用**且载荷逐字段正确；再做**两条退化校验**
            //   （摘掉发送方 / 摘掉订阅方 ⇒ 收不到），证明本断言不是摆设。
            //   总线 = 宿主的 `RecordingEventBus`，`On/Off/Emit` 是**真派发**（不是只记账）。
            {
                var received = 0;
                Diablo2.Def.HoverTarget got = null;
                Action<Diablo2.Def.HoverTarget> onHover = h => { received++; got = h; };

                reader.OverrideHoverGrid(emptyGrid);
                reader.UpdateHover(true);                    // 归零到"空地"态 ⇒ 下一步目标一定变化

                Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
                var rtBus0 = bus.CountOf(Events.HoverTargetChanged);
                reader.OverrideHoverGrid(monGrid);
                var tRt = reader.UpdateHover(true);

                Check("★ 往返：`D2.Input.HoverChanged` 真实订阅回调被调用 1 次（总线 +1）",
                    received == 1 && bus.CountOf(Events.HoverTargetChanged) == rtBus0 + 1,
                    $"回调 {received} 次 / 总线 +{bus.CountOf(Events.HoverTargetChanged) - rtBus0}（{Events.HoverTargetChanged}）");
                Check("★ 往返：回调收到的载荷 == 派发的载荷（cursor=Attack / id=怪物 / 格=悬停格 / 同一实例）",
                    got != null && got.hasTarget && got.cursor == CursorKind.Attack
                    && got.id == monsterId && got.gridX == monGrid.x && got.gridY == monGrid.y
                    && ReferenceEquals(got, tRt),
                    got == null ? "回调收到 null"
                                : $"hasTarget={got.hasTarget} cursor={got.cursor} id={got.id} " +
                                  $"格=({got.gridX},{got.gridY}) 同一实例={ReferenceEquals(got, tRt)}");

                // 退化①：摘掉**发送方**（`canInteract=false` ⇒ `UpdateHover` 冻结、不派发）
                var rtRecv0 = received;
                reader.OverrideHoverGrid(npcGrid);
                reader.UpdateHover(false);
                Check("★ 退化①：发送方冻结（canInteract=false）⇒ 回调收不到（断言不是摆设）",
                    received == rtRecv0, $"回调 {rtRecv0} → {received}");

                // 退化②：摘掉**订阅方**（`Off`）⇒ 同一条派发不再进回调
                Game.Event.Off<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
                var rtRecvOff = received;
                reader.OverrideHoverGrid(itemGrid);
                reader.UpdateHover(true);
                Check("★ 退化②：Off 之后同一条派发不再进回调（订阅/退订闭环）",
                    received == rtRecvOff, $"回调 {rtRecvOff} → {received}");
            }

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

            //   旧断言 `> 1.85 && < 2.15` 的中心点 = 2.0（写死的"走 = 跑的一半"，值本身无出处）、
            //   为什么必须留这个量级的容差：这两段是"逐帧位移"的离散计数，还夹着 A* 逐格转向的
            //   世界/格量纲换算 ⇒ 精确到小数位本来就不成立。实测（同一条 44 格路径、同起点同终点）：
            //   跑 856 帧 / 走 1779 帧 = 2.078，与 2.143 差 3% —— 落在旧断言同样的 ±7.5% 里。
            //   判据仍是"实测比值 == 由常量推出的速度比"；若 R 切换没生效，比值会跑到 1.0 附近，照样红。
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
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// w7 契约新增 <c>IPlayerModule.TrySpendMana(int)</c>（**扣蓝**入口）的回归 + <c>RestoreMana</c> 既有语义不变：
        /// ① 成功扣减并走 <c>Events.HudDirty</c> 属性刷新路径；② 法力不足 ⇒ false 且不扣；
        /// ③ 非正数 ⇒ false 且不扣；④ **<c>RestoreMana(-n)</c> 仍按非正数忽略**（不许为扣蓝把它改成减法）。
        /// </summary>
        private static void Step16_ManaSpend(AppContext ctx, PlayerModule player, RecordingEventBus bus)
        {
            Section("16. ★ 扣蓝/回蓝（w7）：TrySpendMana 扣减生效 + HudDirty；不足/非正数不扣；RestoreMana(-n) 仍忽略");
            player.CreateNew(PlayerClass.Sorceress, "ManaCheck");
            var full = player.MaxMana;
            Check("建角后法力 = 上限（前置）", player.Mana == full && full > 3, $"法力 {player.Mana}/{full}");

            // ① 成功扣蓝（必须走既有属性刷新路径）
            var hudBefore = bus.CountOf(Events.HudDirty);
            var ok = player.TrySpendMana(3);
            Check("TrySpendMana(3) 返回 true 且法力 -3", ok && player.Mana == full - 3,
                $"返回 {ok}，法力 {full} → {player.Mana}");
            Check("扣蓝发了 Events.HudDirty（走既有属性刷新路径）",
                bus.CountOf(Events.HudDirty) > hudBefore,
                $"{Events.HudDirty} {hudBefore} → {bus.CountOf(Events.HudDirty)}");

            // ② 法力不足 ⇒ false 且不扣
            if (full - 4 > 0) player.TrySpendMana(full - 4);        // 扣到只剩 1
            Check("扣到只剩 1 法力（前置）", player.Mana == 1, $"法力 {player.Mana}");
            Check("法力不足（1 < 5）⇒ false 且不扣", !player.TrySpendMana(5) && player.Mana == 1,
                $"法力 {player.Mana}");

            // ③ 非正数 ⇒ false 且不扣
            Check("TrySpendMana(0) = false 且不扣", !player.TrySpendMana(0) && player.Mana == 1,
                $"法力 {player.Mana}");
            Check("TrySpendMana(-5) = false 且不扣", !player.TrySpendMana(-5) && player.Mana == 1,
                $"法力 {player.Mana}");

            // ④ RestoreMana 既有语义不变：非正数仍忽略（没被改成扣蓝）
            player.RestoreMana(10);
            var afterRestore = player.Mana;
            player.RestoreMana(-5);
            Check("RestoreMana(-5) 仍被忽略（法力不变，未退化成扣蓝）",
                afterRestore == 11 && player.Mana == afterRestore, $"法力 {afterRestore} → {player.Mana}");
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
        // 15. 移动抖动（R1-D · 用户投诉「人物移动抖动」）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 把「移动抖动」的候选逐条拆开（全部离线、秒级，不依赖 Unity 原生）：
        /// <list type="bullet">
        /// <item>**a**：逐帧位移上界（每帧位移 ≤ speed×dt、相邻位移不反向）—— 判「吸格心不扣预算」的**周期跳变**；</item>
        /// <item>**b**：变 dt 序列（0.033/0.016/0.050 交替）走**同一条**路径 ⇒ 终点必须逐格一致 —— 隔离「帧率敏感」；</item>
        /// <item>**c**：相机低通（每帧位移单调收敛 / 二阶差分有界 / dt 抖动传导比 ≤ 1.3）—— 判相机是否**放大** dt 抖动；</item>
        /// <item>**e**：帧节奏口径（`Core/FramePacing`）与「地图整图重铺」的合并口径 + 单帧节点数上限。</item>
        /// </list>
        /// 被测实现：`Module/Player/PlayerMotor.Tick`（积分口径）/ `Module/Camera/CameraRig.Tick`（平滑跟随）。
        /// </summary>
        private static void Step15_Jitter(AppContext ctx, object mapObj, PlayerModule player, CameraRig rig,
            ScriptedInput input)
        {
            Section("15. ★ 移动抖动（R1-D）：逐帧位移上界 / 变 dt 终点一致 / 相机低通 / 帧节奏 / 重铺合并");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);

            // 中立输入（前面步骤打开过 `input.Available`）：清本帧边沿 ⇒ 没有按键/按住状态干扰位移
            input.BeginFrame();
            input.Available = true;

            const float dt = 1f / 60f;                       // 真机帧长（R1-D 把帧率钉死在 60）
            player.CreateNew(PlayerClass.Amazon, "Jitter");
            player.SetRunning(true);
            Check("跑（速度 = GameConst.PlayerWalkSpeed）",
                player.Running && Math.Abs(player.MoveSpeed - GameConst.PlayerWalkSpeed) < 1e-4f,
                $"Running={player.Running} MoveSpeed={player.MoveSpeed}");

            player.TeleportTo(map.SpawnPoint);
            var target = FindFarWalkable(map, map.SpawnPoint, 12, requireNoLineOfSight: false);
            Check("找到够远的目标格（≥12 格）", target != map.SpawnPoint,
                $"目标 {target}（出生点 {map.SpawnPoint}）");
            if (target == map.SpawnPoint) return;

            // ── a) 逐帧位移上界（cell 空间；反投影走 `Iso.WorldToGridContinuous`，不另写几何）──
            var limit = player.MoveSpeed * dt;
            var steps = new List<float>();
            var prevCell = CellOf(player.World);
            var prevDelta = Vector2.zero;
            var over = 0;
            var badDots = 0;
            var maxStep = 0f;
            player.MoveTo(target);
            var frames = 0;
            for (; frames < FrameCap && player.IsMoving; frames++)
            {
                player.Tick(dt);
                var cur = CellOf(player.World);
                var d = cur - prevCell;
                var mag = d.magnitude;
                steps.Add(mag);
                if (mag > limit + 1e-4f) over++;
                if (mag > maxStep) maxStep = mag;
                if (prevDelta != Vector2.zero && d != Vector2.zero && Vector2.Dot(prevDelta, d) < -1e-6f) badDots++;
                prevDelta = d;
                prevCell = cur;
            }
            var sum = 0f;
            for (var i = 0; i < steps.Count; i++) sum += steps[i];
            var avg = steps.Count > 0 ? sum / steps.Count : 0f;

            Check("a1 每帧位移 ≤ speed×dt（旧口径「吸格心不扣预算」实测会超到 ~2×）", over == 0,
                $"帧数 {frames} / 路径 {player.Motor.LastSteps} 格；帧位移 最大 {maxStep:0.#####} 平均 {avg:0.#####} / 上限 {limit:0.#####} 格（超出上限的帧 = {over}）");
            Check("a2 相邻两帧位移方向不反向（点积 ≥ 0）", badDots == 0, $"反向对数 = {badDots}");
            Check("a3 位移断言跑的是真路径（已到达目标格）", player.Grid == target, $"终点 {player.Grid}");

            // ── b) 变 dt 序列走同一条路径 ⇒ 终点必须逐格一致（隔离「帧率敏感」）──
            var endFixed = WalkSamePath(player, map, target, null, 0.02f);
            var endJitter = WalkSamePath(player, map, target, new[] { 0.033f, 0.016f, 0.050f }, 0f);
            var cellTarget = new Vector2(target.x + 0.5f, target.y + 0.5f);
            Check("b1 变 dt 与固定 dt 走同一条路径 ⇒ 终点逐格一致（位置与帧率无关）",
                (endFixed - endJitter).magnitude < 1e-5f,
                $"固定 dt 终点 {endFixed} vs 变 dt 终点 {endJitter}（差 {(endFixed - endJitter).magnitude:0.#######} 格）");
            Check("b2 终点 = 目标格心（落格精确、不漂）", (endFixed - cellTarget).magnitude < 1e-4f,
                $"{endFixed} vs 格心 {cellTarget}");

            // ── c) 相机低通：焦点匀速运动下每帧位移单调收敛、二阶差分有界；dt 抖动不被放大 ──
            //      `Flow` 必须非 null 才走「跟主角世界坐标」那条路（真机由 AppFlow 提供，见 CameraRig.FlowReady）
            var flowBefore = ctx.Flow;
            ctx.Flow = new StubFlow();
            try
            {
                var camFixed = new List<float>();
                var playFixed = new List<float>();
                float gapMax, limitFixed, gapLast;
                TraceCamera(player, rig, map, map.SpawnPoint, target, null, 0.02f, camFixed, playFixed,
                    out gapMax, out limitFixed, out gapLast);

                var camMax = 0f;
                for (var i = 0; i < camFixed.Count; i++) if (camFixed[i] > camMax) camMax = camFixed[i];
                var jumpMax = 0f;
                for (var i = 1; i < camFixed.Count; i++)
                {
                    var j = Math.Abs(camFixed[i] - camFixed[i - 1]);
                    if (j > jumpMax) jumpMax = j;
                }
                var diffMax = 0f;
                for (var i = 0; i < camFixed.Count && i < playFixed.Count; i++)
                {
                    var dd = Math.Abs(camFixed[i] - playFixed[i]);
                    if (dd > diffMax) diffMax = dd;
                }

                Check("c1 相机每帧位移 ≤ 焦点标称单帧位移 ×1.1（低通：不放大、不超调）",
                    camMax <= limitFixed * 1.1f + 1e-4f,
                    $"单帧位移 相机最大 {camMax:0.#####} vs 焦点标称上限 {limitFixed:0.#####} 格（{camFixed.Count} 帧）");
                // 「单调收敛」只对**匀速直线**成立：锯齿路径换向时相机的滞后矢量要先转向
                //    （单帧位移小幅回落是**正确的低通行为**，不是抖动）⇒ 必须用一条**真·单方向直线**
                //    （同一行/列的连续可走格）单独判，而不是"有视线的目标"（A* 仍会先斜后直）。
                Vector2Int lineFrom, lineTo;
                var hasLine = FindLongestAxisRun(map, out lineFrom, out lineTo);
                var camLine = new List<float>();
                var playLine = new List<float>();
                if (hasLine)
                {
                    TraceCamera(player, rig, map, lineFrom, lineTo, null, 0.02f, camLine, playLine,
                        out gapMax, out limitFixed, out gapLast);
                }
                var monotone = hasLine;
                var breakAt = -1;
                var breakDelta = 0f;
                for (var i = 1; i < camLine.Count; i++)
                {
                    if (camLine[i] >= camLine[i - 1] - 1e-5f) continue;
                    if (breakAt < 0) { breakAt = i; breakDelta = camLine[i] - camLine[i - 1]; }
                    monotone = false;
                }
                var lineJump = 0f;
                for (var i = 1; i < camLine.Count; i++)
                {
                    var j = Math.Abs(camLine[i] - camLine[i - 1]);
                    if (j > lineJump) lineJump = j;
                }
                // 判定范围**只取匀速段**（焦点单帧位移 == 标称上限的那些帧）：
                //    到达帧焦点会停下（位移 < 标称），相机随之减速 —— 那是正确的末端行为，不是"来回"。
                var steady = 0;
                var steadyOk = true;
                var prevSteady = -1f;
                for (var i = 0; i < camLine.Count; i++)
                {
                    if (i >= playLine.Count || playLine[i] < limitFixed - 1e-4f) continue;
                    if (prevSteady >= 0f && camLine[i] < prevSteady - 1e-5f) steadyOk = false;
                    steady++;
                    prevSteady = camLine[i];
                }
                var probe = steadyOk
                    ? $"匀速段 {steady} 帧全程单调不减"
                    : $"匀速段第 {steady} 帧起有回落（首个回落在全序列第 {breakAt} 帧：{breakDelta:0.#####}）";
                Check("c2 匀速**单方向直线**段：相机每帧位移单调不减（收敛过程无来回）",
                    hasLine && steadyOk && steady > 5,
                    $"直线段 {lineFrom}→{lineTo}（{camLine.Count} 帧）；末帧位移 {(camLine.Count > 0 ? camLine[camLine.Count - 1] : 0f):0.#####} ⇒ 渐近标称 {limitFixed:0.#####} 格；{probe}");
                //   · 旧阈值 0.25 是为**旧口径**（纯指数滞后 τ=0.12，把一切高频都抹平）标定的；
                //     波纹自然更大 —— 这是**有意交换**：用这一点位移波纹换掉用户看到的**横向摆动**
                //     （见下方 c7 / c8 的配对实测）。
                //   · 0.6 仍在"有低通"这一侧：**刚性跟随（smoothTime=0）**换一次 45° 向时
                //     |Δstep| = |Δv|·dt = 2·sin22.5° × 标称 = **0.765 × 标称**，而本口径实测 0.38 × 标称
                //     ⇒ 阈值 0.6 同时满足"实测留 ~1.6 倍余量"与"仍能把无低通判红"。
                Check("c3 二阶差分有界（平滑、无跳变；锯齿与直线段都算；阈值 0.6×标称，来历见源码注释）",
                    jumpMax <= limitFixed * 0.6f + 1e-4f && lineJump <= limitFixed * 0.6f + 1e-4f,
                    $"锯齿段最大 |Δstep| = {jumpMax:0.#####}（= {jumpMax / MathF.Max(1e-6f, limitFixed):0.00}×标称）；" +
                    $"直线段 = {lineJump:0.#####}（上限 {limitFixed * 0.6f:0.#####}；刚性跟随会是 0.765×）");
                Check("c4 相机与焦点同帧位移之差 ≤ 一个标称帧步（不甩开、不提前）",
                    diffMax <= limitFixed + 1e-4f,
                    $"最大差 {diffMax:0.#####} 格 = 标称帧步的 {diffMax / MathF.Max(1e-6f, limitFixed) * 100f:0.0}%");
                Check($"c5 稳态滞后 ≤ speed×τ×1.5（τ=FollowSmoothTime {CameraRig.FollowSmoothTime}s ⇒ 理论 {player.MoveSpeed * CameraRig.FollowSmoothTime:0.###} 格）",
                    gapLast <= player.MoveSpeed * CameraRig.FollowSmoothTime * 1.5f + 1e-3f,
                    $"末帧滞后 {gapLast:0.####} 格（全程最大 {gapMax:0.####}）");

                // dt 抖动传导比：输入 dt 的变异系数 vs 相机每帧位移的变异系数（稳态段取样）
                var camJit = new List<float>();
                var playJit = new List<float>();
                TraceCamera(player, rig, map, map.SpawnPoint, target, new[] { 0.033f, 0.016f, 0.050f }, 0f,
                    camJit, playJit, out gapMax, out limitFixed, out gapLast);
                var skip = 30;                                    // 跳过启动暂态
                var cvPlay = Cv(playJit, skip, 5);
                var cvCam = Cv(camJit, skip, 5);
                var amp = cvPlay > 1e-4f ? cvCam / cvPlay : 1f;
                Check("c6 dt 抖动传导比 ≤ 1.3（相机不放大帧长抖动；1.0 = 原样通过）",
                    amp <= 1.3f && cvPlay > 0.05f,
                    $"输入 dt 抖动 CV={cvPlay:0.###} → 相机单帧位移 CV={cvCam:0.###}，传导比 {amp:0.###}（稳态取样，剔除首 {skip} 帧与末 5 帧）");

                //   旧口径（纯指数滞后 τ=0.12s）：稳态滞后 = 速度×τ = 3.0×0.12 = 0.36 格，而 A* 走 8 向
                //   锯齿、每 1~2 格换一次向 ⇒ 滞后矢量每帧要转 45° ⇒ 相对偏移的**横向**分量来回摆
                //   （实测 before：峰峰值 0.5 格级 ≈ 70+ 屏幕像素 @1080p）—— 这就是用户看到的"抖"。
                //   新口径（临界阻尼 + 小 smoothTime）：滞后随 smoothTime 线性缩小 ⇒ 摆幅同比例缩小。
                //   阈值 = 实测 after 上界 × 2 余量（来历写在 CameraRig.ZigZagLateralSwingMax 的注释里）。
                var zigLat = new List<float>();
                TraceCamera(player, rig, map, map.SpawnPoint, target, null, 0.02f, new List<float>(),
                    new List<float>(), out gapMax, out limitFixed, out gapLast, zigLat);
                var swing = 0f;
                if (zigLat.Count > 0)
                {
                    var lo = zigLat[0];
                    var hi = zigLat[0];
                    for (var i = 1; i < zigLat.Count; i++)
                    {
                        if (zigLat[i] < lo) lo = zigLat[i];
                        if (zigLat[i] > hi) hi = zigLat[i];
                    }
                    swing = hi - lo;
                }
                Check($"c7 8 向锯齿路径：相机与焦点相对偏移的**横向**摆幅 ≤ {CameraRig.ZigZagLateralSwingMax:0.###} 格（换向不再摆）",
                    zigLat.Count > 10 && swing <= CameraRig.ZigZagLateralSwingMax,
                    $"横向偏移峰峰值 {swing:0.#####} 格（{zigLat.Count} 帧；阈值 {CameraRig.ZigZagLateralSwingMax:0.#####} 格" +
                    "= 本口径的解析上界 2×speed×smoothTime×sin45°（+余量），来历见 CameraRig.ZigZagLateralSwingMax 注释）");

                //   （不写回产品代码）；两条模型喂**同一条玩家路径、同一个 dt**，只换相机那一行，
                //   因此差值只可能来自相机口径本身（不是地图/路径/帧长差异）。
                {
                    player.Stop();
                    player.TeleportTo(map.SpawnPoint);
                    rig.Reset();
                    rig.EnableZoom = false;
                    rig.EnableEdgeScroll = false;
                    rig.SetTargetGrid(map.SpawnPoint);
                    rig.SnapToTarget();
                    rig.Tick(0.02f);

                    const float oldTau = 0.12f;                       // 旧口径的时间常数（冻结值）
                    var oldPos = rig.Position;
                    var latNew = new List<float>();
                    var latOld = new List<float>();
                    var prevW = player.World;
                    player.MoveTo(target);
                    var cf = 0;
                    for (; cf < 20000 && player.IsMoving; cf++)
                    {
                        player.Tick(0.02f);
                        rig.Tick(0.02f);                              // 新口径（产品实现）
                        var want = CameraRig.DesiredPosition(player.World, -CameraRig.CameraDistance);
                        var kOld = 1f - MathF.Exp(-0.02f / oldTau);    // 旧口径：纯指数滞后（一步 Lerp）
                        oldPos = Vector3.Lerp(oldPos, want, kOld);

                        var pw = player.World;
                        var dpx = pw.x - prevW.x;
                        var dpy = pw.y - prevW.y;
                        var st = MathF.Sqrt(dpx * dpx + dpy * dpy);
                        if (st > 1e-6f)
                        {
                            var lx = -dpy / st;
                            var ly = dpx / st;
                            latNew.Add((pw.x - rig.Position.x) * lx + (pw.y - rig.Position.y) * ly);
                            latOld.Add((pw.x - oldPos.x) * lx + (pw.y - oldPos.y) * ly);
                        }
                        prevW = pw;
                    }

                    float swNew = 0f, swOld = 0f;
                    if (latNew.Count > 0)
                    {
                        float nlo = latNew[0], nhi = latNew[0], olo = latOld[0], ohi = latOld[0];
                        for (var i = 1; i < latNew.Count; i++)
                        {
                            if (latNew[i] < nlo) nlo = latNew[i];
                            if (latNew[i] > nhi) nhi = latNew[i];
                            if (latOld[i] < olo) olo = latOld[i];
                            if (latOld[i] > ohi) ohi = latOld[i];
                        }
                        swNew = nhi - nlo;
                        swOld = ohi - olo;
                    }
                    Check("c8 同一段锯齿路径上：新口径横向摆幅 < 旧口径（配对实测，只换相机口径）",
                        latNew.Count > 10 && swOld > 0f && swNew < swOld * 0.5f,
                        $"旧口径(纯指数 τ={oldTau}) 峰峰值 {swOld:0.#####} 格 → 新口径(临界阻尼 {CameraRig.FollowSmoothTime}s) " +
                        $"{swNew:0.#####} 格（{cf} 帧，同路径同 dt；降幅 {(swOld > 0f ? (1f - swNew / swOld) * 100f : 0f):0.0}%）");
                }
            }
            finally
            {
                ctx.Flow = flowBefore;                            // 还原（后面的步骤/宿主语义不受影响）
                player.Stop();
            }

            // ── e) 帧节奏口径（Core/FramePacing）+ 地图整图重铺的合并口径与单帧节点上限 ──
            Check("e1 帧节奏口径 = targetFrameRate 60 + vSync 0（恒定，与画质档位无关）",
                FramePacing.TargetFrameRate == 60 && FramePacing.VSyncCount == 0,
                $"targetFrameRate={FramePacing.TargetFrameRate} vSyncCount={FramePacing.VSyncCount}" +
                "（旧口径：QualitySettings.asset 逐档 vSync 0/1 ⇒ 选 LOW/MED 时无帧率上限）");

            // U27（帧节奏下沉）**新增**断言：把"离线宿主拿到的档位"实打实打出来 —— 它必须是
            //   **兜底口径**（刷新率读不到 ⇒ 60/0）。这条断言是给 e1/e3 的**前提**做锚：
            //   离线档位没变 ⇒ e1/e3 一行都不用改（改判据只许因为口径真的变了，不许为了凑绿）。
            int recFps, recVsync;
            float recHz;
            var recReadable = CloverEngine.FramePacingPolicy.Recommend(out recFps, out recVsync, out recHz);
            Check("e0 U27 离线口径：`FramePacingPolicy.Recommend()` 在宿主里 = 兜底 60/0（刷新率读不到 ⇒ 不走 vSync=1）",
                !recReadable && recFps == FramePacing.TargetFrameRate && recVsync == FramePacing.VSyncCount,
                $"readable={recReadable} refreshHz={recHz:0.##} ⇒ targetFrameRate={recFps} vSyncCount={recVsync}"
                + $"（兜底常量 = {FramePacing.TargetFrameRate}/{FramePacing.VSyncCount}）");

            FramePacing.ResetStaticsForNewPlaySession();
            var logsBefore = CaptureLogger.Count("[R1-D]");
            FramePacing.Pin("自检宿主（第一次）");
            var logsFirst = CaptureLogger.Count("[R1-D]");
            FramePacing.Pin("自检宿主（第二次）");
            var logsSecond = CaptureLogger.Count("[R1-D]");
            Check("e2 Pin 在无 Unity 运行时时不抛异常、留痕、且只报一次",
                logsFirst > logsBefore && logsSecond == logsFirst,
                $"第 1 次 +{logsFirst - logsBefore} 条 / 第 2 次 +{logsSecond - logsFirst} 条（离线原生 API 不可用 ⇒ 走 Warn 那条分支）");
            var line = FramePacing.Describe("自检宿主", 0, 1);
            Check("e3 生效口径文本写清了三件事（targetFrameRate / vSyncCount / 动画复位口径 / 移动积分口径）",
                line.IndexOf("targetFrameRate=60", StringComparison.Ordinal) >= 0
                && line.IndexOf("vSyncCount=0", StringComparison.Ordinal) >= 0
                && line.IndexOf("动画复位口径", StringComparison.Ordinal) >= 0
                && line.IndexOf("移动积分口径", StringComparison.Ordinal) >= 0
                && CaptureLogger.Has("[R1-D]"),
                line);

            // 整图重铺的合并口径（纯函数）—— 旧口径 = 每来一张贴图就整图重铺一次
            // 用全名 `Diablo2.Module.Map.MapView`：本宿主的 using 里没有 `Diablo2.Module.Map`
            // （与 `MapModule` 同一处置，避免为一个纯函数断言多引一个命名空间）。
            Check("e4 无待办 ⇒ 不重铺",
                !Diablo2.Module.Map.MapView.ShouldRepaintNow(10f, -1f, 0f, float.NegativeInfinity), "firstRequestAt < 0");
            Check("e5 贴图还在陆续到位（距最近一次 < 静默期）⇒ 先攒着",
                !Diablo2.Module.Map.MapView.ShouldRepaintNow(10f, 9.9f, 9.9f, float.NegativeInfinity),
                $"now-last={10f - 9.9f:0.##} < 静默期 {Diablo2.Module.Map.MapView.RepaintQuietTime:0.##}s");
            Check("e6 静默期满且距上次重铺 ≥ 最小间隔 ⇒ 重铺",
                Diablo2.Module.Map.MapView.ShouldRepaintNow(10f, 9.5f, 9.5f, float.NegativeInfinity),
                $"now-first={0.5f:0.##} 静默={0.5f:0.##} 距上次=∞");
            Check("e7 距上次重铺 < 最小间隔 ⇒ 不重铺（节流）",
                !Diablo2.Module.Map.MapView.ShouldRepaintNow(10f, 9.5f, 9.5f, 9.8f),
                $"now-lastRepaint={0.2f:0.##} < 最小间隔 {Diablo2.Module.Map.MapView.RepaintMinInterval:0.##}s");
            Check("e8 超时兜底：距首次请求 ≥ 最长等待 ⇒ 无条件重铺（贴图一定会换上）",
                Diablo2.Module.Map.MapView.ShouldRepaintNow(10f, 8.5f, 9.99f, 9.95f),
                $"now-first={1.5f:0.##} ≥ 最长等待 {Diablo2.Module.Map.MapView.RepaintMaxDelay:0.##}s");

            // 单帧节点数上限（离线可算；真机实测见下一批"进 Play"）
            const int perCellMax = 3;                                  // ground + object + overlay(迷雾)
            var chunkCells = Diablo2.Module.Map.MapView.ChunkSize * Diablo2.Module.Map.MapView.ChunkSize;
            var townCells = GameConst.TownWidth * GameConst.TownHeight;
            Check($"e9 单块节点上限 = {Diablo2.Module.Map.MapView.ChunkSize}×{Diablo2.Module.Map.MapView.ChunkSize} 格 × ≤{perCellMax} 层 = {chunkCells * perCellMax} 个 GameObject",
                chunkCells * perCellMax == 768,
                $"ChunkSize={Diablo2.Module.Map.MapView.ChunkSize} ⇒ {chunkCells} 格 × {perCellMax} 层 = {chunkCells * perCellMax}");
            Check("e10 Town 56×40 = 2240 格 ≤ 一次铺满阈值 4096 ⇒ **不分块**：一次 ShowArea 就在单帧建满全图",
                townCells <= Diablo2.Module.Map.MapView.BuildAllTileThreshold,
                $"Town 格数 {townCells} ≤ 阈值 {Diablo2.Module.Map.MapView.BuildAllTileThreshold} ⇒ 单帧最多 ≈ {townCells}~{townCells * perCellMax} 个节点" +
                "（旧口径：每来一张贴图 = 再来一次整图重建 ⇒ 贴图流式到位期间每秒十几次 ⇒ 移动顿挫）");
            Check("e11 野外 80×80 = 6400 格 > 4096 ⇒ 分块；单帧节点上限 = 可见缺块数 × 768",
                GameConst.WildernessMaxSize * GameConst.WildernessMaxSize > Diablo2.Module.Map.MapView.BuildAllTileThreshold,
                $"野外最大 {GameConst.WildernessMaxSize}×{GameConst.WildernessMaxSize} = {GameConst.WildernessMaxSize * GameConst.WildernessMaxSize} 格 > {Diablo2.Module.Map.MapView.BuildAllTileThreshold}" +
                $" ⇒ 每块 {chunkCells * perCellMax} 个节点（不在本片改动范围，只登记）");

            // ═════════════════════════════════════════════════════════════════
            // f) U27 三分判据：**相机 / 角色渲染节点 / 角色逻辑** 三层位置分列
            //    （同一段输入驱动，逐帧采样；回答"抖出现在哪一层"）
            //
            //  层定义（每层都取生产件的**唯一出口**，不镜像公式）：
            //    ① 相机 = `CameraRig.Position`（跟随 + 边界夹制算出来的机位）
            //    ② 渲染 = `ViewModule.EntityWorld(PlayerEntityId, w)` —— 它正是
            //             `ViewModule.TickPlayer` 写 `EntityView.Root.transform.position` 的
            //             **唯一口径**（`ViewModule.cs:787` 写、`:1345` 定义）
            //    ③ 逻辑 = `player.World`（契约 `IPlayerModule.World` 原文 = "渲染插值后的实际位置"）
            //
            //  为什么要这条判据（用户报「人物抖动」有两种**互相排斥**的形态，必须分开判）：
            //    · **层间差**：渲染 ≠ 逻辑（表现层又插了一次值 / 用了另一套坐标），
            //    · **层内时间不匀**：三层位置各自都"干净"，但位置是 f(t) 按 dt 积分 ⇒
            //      **帧间隔不匀直接变成每帧推进量不匀** ⇒ 帧节奏问题，改表现层没用。
            //    ⇒ f1/f3 判层间差；f2 判层内推进量（按 8 方向分组，因为等距投影是各向异性的）。
            //
            //  判据自检（"退化样本必须变红"）：f1b/f2b/f3b 三条 = 对**同一批采样**故意注入
            //     退化量，断言检测函数能抓出来 —— 否则"全绿"可能只是检测函数没睡醒。
            // ═════════════════════════════════════════════════════════════════
            {
                var flowBefore3 = ctx.Flow;
                ctx.Flow = new StubFlow();      // `RefreshFocus` 才走"跟主角"那条（同 c 段的口径）
                try
                {
                    player.Stop();
                    player.TeleportTo(map.SpawnPoint);
                    rig.Reset();
                    rig.EnableZoom = false;
                    rig.EnableEdgeScroll = false;
                    rig.SetTargetGrid(map.SpawnPoint);
                    rig.SnapToTarget();
                    rig.Tick(dt);

                    var target3 = FindFarWalkable(map, map.SpawnPoint, 12, requireNoLineOfSight: false);

                    // ── 逐帧三分采样（同一段输入：一次 MoveTo + 每帧 player.Tick/rig.Tick）──
                    var camSeq = new List<Vector3>();
                    var renderSeq = new List<Vector3>();
                    var logicSeq = new List<Vector3>();
                    player.MoveTo(target3);
                    for (var f3 = 0; f3 < FrameCap && player.IsMoving; f3++)
                    {
                        player.Tick(dt);
                        rig.Tick(dt);                       // 与真机同一帧内顺序（AppContext.Tick：View → Camera）
                        logicSeq.Add(player.World);
                        renderSeq.Add(RenderWorldOf(player.World));
                        camSeq.Add(rig.Position);
                    }

                    Check("f0 三分判据采到了一段真路径（帧数 > 60，且三层序列等长）",
                        logicSeq.Count > 60 && logicSeq.Count == renderSeq.Count && logicSeq.Count == camSeq.Count,
                        $"帧数 {logicSeq.Count}（{map.SpawnPoint} → {target3}，dt={dt:0.####}s，" +
                        $"逻辑终点 {CellOf(player.World)}）");

                    // ── f1 层间差①：渲染层 vs 逻辑层（层② 的**唯一**口径就是不插值 ⇒ 必须逐位相等）──
                    var diffRL = LayerDiffMax(renderSeq, logicSeq);
                    var diffRLInjected = LayerDiffMaxWithInjection(renderSeq, logicSeq, 1e-3f);
                    Check("f1 渲染层 ≡ 逻辑层（同一帧逐位相等：层② 不二次插值，`ViewModule.cs:787`）",
                        diffRL <= 1e-6f,
                        $"逐帧 |渲染−逻辑| 最大 = {diffRL:0.#########} 格（{logicSeq.Count} 帧；" +
                        "层② 的 z 是常数排序键（同 id 恒定），xy 恒等 ⇒ 差值只可能来自「别人又插了一次值」）");
                    Check("f1b ★ 判据自检（退化样本必须变红）：给渲染层注入 1e-3 格偏移 ⇒ 必须被抓出",
                        diffRLInjected > 1e-4f && diffRLInjected <= 1e-3f + 1e-6f,
                        $"注入 1e-3 后同一检测函数读到 {diffRLInjected:0.#########} 格（阈值 1e-4）");

                    // ── f2 层内：逻辑层「每帧推进量」按 8 方向分组后**组内恒定** ──
                    //   口径 = **格空间恒定速度**（`PlayerMotor` 每帧按 `speed×dt` 推格空间距离；
                    //   出处 = 原版 `unit.runSpeed = 15` map 单位/秒 ÷ 5 单位/格 = 3 格/秒，
                    //   见 `GameConst.PlayerWalkSpeed` 注释与 `Engine/Iso.SubTileCount = 5`）。
                    //   ⇒ 同一个格空间方向上的世界步长/格步长 比值必须**逐帧同一个数**。
                    //   **跨方向**的比值本来就不等（等距投影 HalfW:HalfH = 2:1 的必然，
                    //      解析值 0.7071(屏幕正下) ~ 1.4142(屏幕正右)）—— 那是**投影口径**，
                    //      本判据**只登记不判**（不为了让数字好看去改积分口径）。
                    int buckets3;
                    string dirDetail;
                    var budget3 = player.MoveSpeed * dt;              // 标称单帧格步长（移动积分口径的输入）
                    var spreadLogic = AdvanceSpread(logicSeq, budget3, true, out buckets3, out dirDetail);
                    var spreadLogicInjected = AdvanceSpreadNoTurnFilter(logicSeq, budget3);
                    Check("f2 逻辑层「世界步长/格步长」按 8 方向分组后**组内恒定**（容差 1e-3 = float 噪声余量）",
                        buckets3 >= 3 && spreadLogic <= 1.001f,
                        $"有样本的方向数 {buckets3}；组内 spread（max/min）最大 = {spreadLogic:0.######}；" + dirDetail);
                    Check("f2b ★ 判据自检（退化样本必须变红）：把「纯帧」过滤关掉 ⇒ 同一批采样必须变红",
                        spreadLogicInjected > 1.001f,
                        $"过滤关掉后同一检测函数读到 spread = {spreadLogicInjected:0.######}（阈值 1.001）；" +
                        "=> 换向帧确实存在、且上面的过滤是承重的（不是把缺陷一起滤掉）");

                    // ── f3 层间差②：**相机层不许"跳"/"冻"** ──
                    //   量的是「相机与逻辑的相对偏移」的逐帧变化量（世界空间，方向无关）。
                    //   上界 = 玩家该帧最大位移 ×1.1（解析：相机最坏 = 完全冻结 ⇒ 变化量 = 玩家位移；
                    //   ×1.1 余量与 c1 同口径）。**绝不用"相机单帧步长 vs 逻辑单帧步长"比大小**：
                    //   相机是低通，换向时它的速度矢量要转，单帧步长**可以**大于玩家该帧步长
                    //   （实测第一版就这么误判了 416 帧，见 report-u27.md）。
                    // 玩家单帧**世界**位移的上限 = 标称格步长 × 「世界/格」比值的最大值
                    // （比值上限由 `Iso.GridToWorld` 对 8 个格方向现算，不写死数字）
                    var perFrameLimit3 = budget3 * MaxWorldPerGridRatio() * 1.1f;
                    int relJumpAt;
                    var relJump = RelativeOffsetJump(camSeq, logicSeq, perFrameLimit3, out relJumpAt);
                    Check("f3 相机层与逻辑层的相对偏移**逐帧变化量** ≤ 玩家单帧最大位移 ×1.1（相机不跳、不冻）",
                        relJumpAt < 0,
                        $"最大单帧相对偏移变化量 = {perFrameLimit3 + (relJumpAt < 0 ? 0f : relJump) + 0f:0.#####} 格（上限 {perFrameLimit3:0.#####}）" +
                        $"；超限帧 = {(relJumpAt < 0 ? "无" : relJumpAt.ToString())}（共 {camSeq.Count} 帧）");

                    // f3b 判据自检（退化样本必须变红）：把某一帧的**相机位置**猛地挪一格 ⇒ 必须抓出
                    {
                        var injectedCam = new List<Vector3>(camSeq);
                        var im = camSeq.Count / 2;
                        injectedCam[im] = camSeq[im] + new Vector3(1f, 0f, 0f);
                        int at2;
                        var jump2 = RelativeOffsetJump(injectedCam, logicSeq, perFrameLimit3, out at2);
                        Check("f3b ★ 判据自检（退化样本必须变红）：给某一帧相机注入 1 格跳变 ⇒ 必须被抓出",
                            at2 == im && jump2 > 0f,
                            $"注入帧 {im}（+1 格）⇒ 检测函数报超限帧 {at2}、超限量 {jump2:0.#####} 格");
                    }

                    // ── f4 逐帧原始读数（三层；给回报直接抄）──
                    var head = Math.Min(10, logicSeq.Count);
                    var sb3 = new System.Text.StringBuilder();
                    sb3.Append("f4 逐帧原始读数（前 ").Append(head).Append(" 帧；单位=世界格）：");
                    for (var i5 = 0; i5 < head; i5++)
                    {
                        sb3.Append("\n        #").Append(i5)
                           .Append(" 逻辑(").Append(logicSeq[i5].x.ToString("0.#####")).Append(",")
                           .Append(logicSeq[i5].y.ToString("0.#####")).Append(")")
                           .Append(" 渲染(").Append(renderSeq[i5].x.ToString("0.#####")).Append(",")
                           .Append(renderSeq[i5].y.ToString("0.#####")).Append(")  相机(")
                           .Append(camSeq[i5].x.ToString("0.#####")).Append(",")
                           .Append(camSeq[i5].y.ToString("0.#####")).Append(")")
                           .Append("  相对偏移(").Append((logicSeq[i5].x - camSeq[i5].x).ToString("0.####")).Append(",")
                           .Append((logicSeq[i5].y - camSeq[i5].y).ToString("0.####")).Append(")");
                    }
                    Console.WriteLine("    [INFO ] " + sb3);

                    // ── f5 相机夹制带（**补的判据缺口**）─────────────────────────────
                    //   既有 c1~c8 的路径是 (32,28)→(8,20)（镇西北），而且**离线时
                    //   `CameraRig.ClampToMapBounds` 因 `_cam == null` 直接 return**
                    //   （`CameraRig.cs:913`：拿不到 aspect ⇒ 不做半屏换算）
                    //   ⇒ **既有 8 条相机断言从未覆盖"边界夹制"**。
                    //   这里换一条可离线判的入口：直接调**生产纯函数** `CameraBounds.ClampCameraGrid`
                    //   逐格算"机位被夹到哪"。
                    //   半屏口径（有出处，不是自创参数）：
                    //     halfH = `CameraRig.DefaultOrthographicSize`（3.75，出处见该常量注释）
                    //     halfW = halfH × aspect，aspect = 1920/1080（出处 = 本机 Play 采集分辨率
                    //   解析边界：机位可行域 = 焦点格 g 满足 g.y ≤ H − (a+b)/2（此处 a=halfW/HalfW=6.667、
                    //   b=halfH/HalfH=7.5 ⇒ (a+b)/2 = 7.0833）⇒ Town 40 行时 g.y ≤ 32.917；
                    //   同式 g.x ≤ W − 7.0833 = 48.917。
                    {
                        const float aspect = 1920f / 1080f;
                        var halfW = CameraRig.DefaultOrthographicSize * aspect;
                        var halfH = CameraRig.DefaultOrthographicSize;
                        var boundY = map.Height - (halfW / Iso.HalfW + halfH / Iso.HalfH) * 0.5f;
                        var boundX = map.Width - (halfW / Iso.HalfW + halfH / Iso.HalfH) * 0.5f;

                        var walkableCount = 0;
                        var clampedCount = 0;
                        var outsideBand = 0;
                        var maxShift = 0f;
                        var maxAt = Vector2Int.zero;
                        for (var y2 = 0; y2 < map.Height; y2++)
                        {
                            for (var x2 = 0; x2 < map.Width; x2++)
                            {
                                var g2 = new Vector2Int(x2, y2);
                                if (!map.Walkable(g2)) continue;
                                walkableCount++;
                                var fw = Iso.GridToWorld(g2);
                                var focus2 = new Vector2(fw.x, fw.y);
                                var cam2 = CameraBounds.ClampCameraGrid(focus2, focus2,
                                    map.Width, map.Height, halfW, halfH);
                                var shift = (cam2 - focus2).magnitude;
                                if (shift <= 1e-4f) continue;
                                clampedCount++;
                                if (g2.y > boundY && g2.x > boundX) outsideBand++;
                                if (shift > maxShift) { maxShift = shift; maxAt = g2; }
                            }
                        }

                        Check("f5 相机夹制只在该咬合的带内发生（Town: g.y > 32.917 或 g.x > 48.917）" +
                              "—— 且 Town 里确实有被夹的可走格（既有断言没覆盖这块）",
                            clampedCount > 0 && outsideBand == 0,
                            $"可走格 {walkableCount}，被夹 {clampedCount} 格（带外被夹 = {outsideBand}；" +
                            $"解析边界 g.y>{boundY:0.###} / g.x>{boundX:0.###}）");
                        Check("f6 ★ 夹制位移读数（= 实机「玩家偏离屏幕中心」那两处的离线同源量）",
                            maxShift > 0f,
                            $"最大夹制位移 {maxShift:0.####} 格 = {maxShift * (1080f / (2f * CameraRig.DefaultOrthographicSize)):0.#} px" +
                            $"@1080p，发生在格 ({maxAt.x},{maxAt.y})" +
                            "（实机同两处读数：`report-camverify.md` §3 `dist p50 459/483px、max 904px`" +
                            " ⇒ 离线纯函数与实机同源、同量级）");
                    }
                }
                finally
                {
                    ctx.Flow = flowBefore3;
                    player.Stop();
                }
            }

            // ═════════════════════════════════════════════════════════════════
            // g) U27 **时间轴判据**（team-lead 追补）：同一段输入，**只换 dt 序列**
            //
            //  为什么必须补：f1 的读数（渲染 ≡ 逻辑 逐帧 0 偏差）已把"渲染层自己抖"排除，
            //  三层位置也各自干净 ⇒ 剩下唯一能造"一顿一顿"的候选是**帧时间**
            //  （位置 = `f(t)` 按 dt 积分 ⇒ dt 不匀直接变每帧推进量不匀）。
            //  判据形态（team-lead 指定，机械可判）：
            //    **把 dt 序列置换成均匀值后重算位移序列** —— 抖动能被均匀 dt 消掉 ⇒ 抖源在**帧节奏**。
            //
            //  dt 序列出处：`tools/probes/drivers/d2u27_real_dt.txt`（真机逐帧 dt 列 + 文件头带统计）。
            //  **该路径当前不在仓库里** —— 它随「闸门收敛（只留 3 条要求）+ 退役验收机器」一并移除，
            //  属**按设计退役**的判据资产 ⇒ g0 登记为「不适用」，不是缺陷、也不去重建该文件。
            //  量法本身留着：哪天再把一份真机 dt 序列按同一路径登记进仓库，把下面那行 `NotApplicable`
            //  换回 `Check(...)` 即可恢复本判据（对照逻辑一字未动）。
            //
            //  量的空间 = **格空间**（`CellOf`）：格空间的单帧步长 = `speed×dt`，**与方向无关**
            //     ⇒ 它是"时间轴"的干净坐标；世界空间的步长还夹着 2:1 投影的方向因子（f2 已登记）。
            // ═════════════════════════════════════════════════════════════════
            {
                var dtReal = LoadRealDtSeq("tools/probes/drivers/d2u27_real_dt.txt");
                NotApplicable("g0 时间轴判据的输入（真机 dt 序列）",
                    "判据资产 tools/probes/drivers/d2u27_real_dt.txt 已按设计退役（闸门收敛后不再保留）⇒ 本项不适用；" +
                    "⛔ 不伪造 dt 文件、也不据此说「无抖动」");

                if (dtReal != null && dtReal.Count >= 60)
                {
                    var flowBefore4 = ctx.Flow;
                    ctx.Flow = new StubFlow();
                    try
                    {
                        var meanDt = Mean(dtReal);
                        var dtUni = new float[dtReal.Count];
                        for (var i = 0; i < dtUni.Length; i++) dtUni[i] = meanDt;
                        var start4 = map.SpawnPoint;
                        var target4 = FindFarWalkable(map, start4, 12, requireNoLineOfSight: false);

                        // 跑 A：真机 dt 序列；跑 B：同一序列**置换成均匀值**
                        var camA = new List<float>(); var playA = new List<float>();
                        var camB = new List<float>(); var playB = new List<float>();
                        float gA1, gA2, gA3, gB1, gB2, gB3;
                        TraceCamera(player, rig, map, start4, target4, dtReal.ToArray(), 0f, camA, playA, out gA1, out gA2, out gA3);
                        var endA = CellOf(player.World);
                        TraceCamera(player, rig, map, start4, target4, dtUni, 0f, camB, playB, out gB1, out gB2, out gB3);
                        var endB = CellOf(player.World);

                        var sd = 0f;
                        for (var i = 0; i < dtReal.Count; i++) { var d = dtReal[i] - meanDt; sd += d * d; }
                        sd = (float)Math.Sqrt(sd / dtReal.Count);

                        var jA = JudderSd(camA, 5);
                        var jB = JudderSd(camB, 5);
                        var corrA = CorrDt(camA, dtReal, 5);

                        Check("g1 dt 序列统计（真机 cvkeep 1185 帧；判据输入自证）",
                            dtReal.Count >= 1000,
                            $"n={dtReal.Count} mean={meanDt:0.######}s sd={sd:0.######}s " +
                            $"min={Min(dtReal):0.######} p95={Pct(dtReal, 0.95f):0.######} " +
                            $"p99={Pct(dtReal, 0.99f):0.######} max={Max(dtReal):0.######}");

                        Check("g2 ★ **dt 置换成均匀值后抖动必须消失**（抖动可被均匀 dt 消掉 ⇒ 抖源 = 帧节奏，不是位置）",
                            jA > 1e-5f && jB <= 0.2f * jA,
                            $"相机逐帧格空间步长的抖动 sd（局部均值 ±5 帧，与 既有真机量法的 judge() 同口径）：" +
                            $"真机 dt ⇒ J_A={jA:0.######} 格；均匀 dt(={meanDt:0.######}s) ⇒ J_B={jB:0.######} 格；" +
                            $"J_B/J_A={(jA > 1e-9f ? jB / jA : float.NaN):0.####}（判据线 0.2；" +
                            "解析：步长 = speed×dt ⇒ 抖动 ∝ dt 的偏离，均匀 dt ⇒ 偏离 = 0 ⇒ J_B 只余换向帧噪声）");

                        Check("g2b ★ 判据自检（退化样本必须变红）：A 与 B 必须**显著不同**，否则本判据没有分辨力",
                            jB < 0.2f * jA || jB > 5f * jA,
                            $"J_A={jA:0.######} vs J_B={jB:0.######}（比值 {(jA > 1e-9f ? jB / jA : float.NaN):0.####}）" +
                            " ⇒ 两条曲线在数值上确实不同（不是同一个数被打印两遍）");

                        Check("g3 corr(相机纵向偏差, dt−均值) ≥ 0.7 ⇒ 不匀就是帧时间造成的（与既有真机量法的 C3 同口径）",
                            corrA >= 0.7f,
                            $"corr={corrA:0.####}（判据线 0.7：解析理想 = 1；既有真机双口径实测 0.729/0.765" +
                            "（有夹制）/ 0.923（无夹制），见 `.ai-tmp/test/report-camverify.md` §3）");

                        Check("g4 换 dt 序列**不改几何**：两种 dt 下终点一致（证明 dt 只影响时序）",
                            (endA - endB).magnitude <= 1e-3f,
                            $"终点 A={endA} B={endB} 差 {(endA - endB).magnitude:0.#######} 格");

                        // 逐帧原始读数（前 12 帧：dt / 玩家格步 / 相机格步）
                        var n5 = Math.Min(12, camA.Count - 1);
                        var sb4 = new System.Text.StringBuilder("g5 逐帧原始读数（前 " + n5 + " 帧；格空间）：");
                        for (var i = 1; i <= n5; i++)
                        {
                            sb4.Append("\n        #").Append(i)
                               // 配对口径：`TraceCamera` 在第 f 帧用 `dtSeq[f % n]` 推进，
                               //    并把**该帧的步长**追加到 `camSteps[f]` ⇒ 打印 `camA[i]` 必须配 `dtReal[i % n]`
                               .Append(" dt=").Append(dtReal[i % dtReal.Count].ToString("0.######"))
                               .Append(" 玩家步=").Append(playA[i].ToString("0.######"))
                               .Append(" 相机步=").Append(camA[i].ToString("0.######"))
                               .Append(" | 均匀dt 下 相机步=").Append(i < camB.Count ? camB[i].ToString("0.######") : "n/a");
                        }
                        Console.WriteLine("    [INFO ] " + sb4);
                    }
                    finally
                    {
                        ctx.Flow = flowBefore4;
                        player.Stop();
                    }
                }
            }

            // ═════════════════════════════════════════════════════════════════
            //
            //  `return`（"拿不到 aspect ⇒ 不做半屏换算"）⇒ **离线宿主永远走不到夹制**，
            //  c1~c8 八条相机断言从未覆盖它。修后该逻辑抽成**纯函数** `CameraRig.StepFollow`
            //  （不碰 `Camera` / 原生 API），本判据**逐帧驱动这一份生产实现** ⇒ 覆盖成立。
            //
            //  被夹的判定：`shown`（本帧写进相机的机位）与 `raw`（自由平滑状态）不相等
            //  ⇔ 输出被夹制改写过（`StepFollow` 的 out 与 in/out 参数天然给出这两个量）。
            //
            //  判据：**全程最大单帧显示机位位移 ≤ 全程最大单帧玩家位移 ×1.05**（h2；f3 同一余量口径），
            //  并拿**旧口径**（状态被改写 + 清零速度）在同一段输入上当退化样本（h3 必须红）。
            //  别把"7.3604 格"当成脱开时的跳变量：那是「玩家偏离屏幕中心」的**静态位移量**
            //  （f5/f6；与实机同格逐字相同）。实测旧口径脱开时的代价只有 **+15.5%（1.3px@1080p）**
            //  —— 咬合点与脱开点重合，账不累积；详见 `CameraRig.StepFollow` 的注释与本段 h4 的澄清。
            // ═════════════════════════════════════════════════════════════════
            {
                const float aspectH = 1920f / 1080f;                  // 出处同 f5：本机 Play 采集分辨率 1920×1080
                const float orthoH = CameraRig.DefaultOrthographicSize;
                var startH = map.SpawnPoint;
                var southH = startH;
                var bestY = int.MinValue;
                var bestD = int.MaxValue;
                for (var y = 0; y < map.Height; y++)
                    for (var x = 0; x < map.Width; x++)
                    {
                        var g = new Vector2Int(x, y);
                        if (!map.Walkable(g) || map.TileAt(g) == TileKind.Exit) continue;
                        var d = Math.Abs(x - startH.x) + Math.Abs(y - startH.y);
                        if (y > bestY || (y == bestY && d < bestD)) { bestY = y; bestD = d; southH = g; }
                    }

                // 每帧同时算两条口径（**同一条路径、同一串 dt**）：
                //   ① 生产口径 = `CameraRig.StepFollow`（本帧唯一实现）
                //   ② 旧口径对照 = 状态被改写成夹制值 + 清零速度状态（**已删掉的实现**，只作对照实验；
                var pShown = new List<float>();      // 新口径：显示机位的逐帧位移（格空间）
                var pFocus = new List<float>();      // 玩家（焦点）的逐帧位移（格空间）
                var oJump = 0f;
                var oSteps = new List<float>();      // 旧口径：显示机位的逐帧位移（格空间）—— 用于"步长序列阶跃"对照
                var releaseAt = new List<int>();     // 脱开帧（从"被夹"变"不被夹"的第一帧）的序号
                var clampedFrames = 0;
                var unclampedAfterClamp = 0;
                var worstJump = -1f;
                var worstJumpAt = -1;
                var raw = Vector3.zero;
                var vel = Vector3.zero;
                var oRaw = Vector3.zero;
                var oVel = Vector3.zero;

                player.Stop();
                player.TeleportTo(startH);
                var w0 = CameraRig.DesiredPosition(player.World, -CameraRig.CameraDistance);
                raw = w0; oRaw = w0;                      // 先吸附（与 `rig.SnapToTarget()` 同义）

                var prevFocus = CellOf(player.World);
                var prevShown = new Vector2(float.NaN, float.NaN);
                var prevOld = new Vector2(float.NaN, float.NaN);
                var sawClamp = false;

                for (var leg = 0; leg < 2; leg++)         // 腿 0：走进南带（夹制咬合）；腿 1：走回出生点（脱开）
                {
                    player.MoveTo(leg == 0 ? southH : startH);
                    for (var f = 0; f < 3000 && player.IsMoving; f++)
                    {
                        player.Tick(Dt);
                        var focus = player.World;
                        var want = CameraRig.DesiredPosition(focus, -CameraRig.CameraDistance);

                        Vector3 shown;
                        CameraRig.StepFollow(ref raw, ref vel, want, focus, CameraRig.FollowSmoothTime, Dt,
                            map.Width, map.Height, orthoH, aspectH, out shown);

                        oRaw = CloverEngine.CameraMath.SmoothDamp(oRaw, want, ref oVel, CameraRig.FollowSmoothTime, Dt);
                        var oShown = CameraRig.ClampForAspect(oRaw, focus, map.Width, map.Height, orthoH, aspectH);
                        if ((oShown - oRaw).sqrMagnitude > 0f) { oVel = Vector3.zero; oRaw = oShown; }

                        var curFocus = CellOf(focus);
                        var fs = (curFocus - prevFocus).magnitude;
                        var cs = CellOf(shown);
                        var os = CellOf(oShown);
                        var isClamped = (shown - raw).sqrMagnitude > 1e-8f;    // 输出被夹制改写过
                        if (isClamped) { clampedFrames++; sawClamp = true; }
                        else if (sawClamp)
                        {
                            if (unclampedAfterClamp == 0) releaseAt.Add(pShown.Count);   // 脱开首帧
                            unclampedAfterClamp++;
                        }

                        if (!float.IsNaN(prevShown.x))
                        {
                            var csStep = (cs - prevShown).magnitude;
                            pShown.Add(csStep);
                            pFocus.Add(fs);
                            var lim = fs * 1.1f + 1e-4f;
                            if (csStep > lim && csStep - lim > worstJump) { worstJump = csStep - lim; worstJumpAt = pShown.Count - 1; }
                        }
                        if (!float.IsNaN(prevOld.x))
                        {
                            var osStep = (os - prevOld).magnitude;
                            oSteps.Add(osStep);
                            if (osStep > oJump) oJump = osStep;
                        }

                        prevFocus = curFocus;
                        prevShown = cs;
                        prevOld = os;
                    }
                }

                Check("h1 这条路径**确实**进出过相机夹制带（被夹帧 > 0 且之后脱开过）——否则本判据没有信息量",
                    clampedFrames > 5 && unclampedAfterClamp > 5,
                    $"{startH}→{southH}→{startH}：被夹帧 {clampedFrames}，脱开后帧 {unclampedAfterClamp}（共采 {pShown.Count} 帧）");

                //   相机是低通，玩家**换向**时相机要先把速度反号，那几帧它**合法地**比玩家单帧位移大
                //   有效口径 = 全程比对**最大值**：新口径下显示机位单帧位移**不超过**玩家单帧位移（状态只滞后、
                //   输出是状态的单调函数 ⇒ 不会领跑）；旧口径会把"攒下的账"在释放后补一帧 ⇒ 超出来。
                var maxShown = Max(pShown);
                var maxFocus = Max(pFocus);
                Check("h2 ★ 夹制段连续：全程**最大**单帧显示机位位移 ≤ 全程最大单帧玩家位移 ×1.05（不领跑、不跳）",
                    maxShown <= maxFocus * 1.05f,
                    $"{startH}→{southH}→{startH}（{pShown.Count} 帧，其中被夹 {clampedFrames} 帧）：" +
                    $"max|Δ显示机位| = {maxShown:0.######} 格 vs max|Δ玩家| = {maxFocus:0.######} 格" +
                    $"（比值 {maxShown / maxFocus:0.####}；1 格 = {144f:0} px@1080p ⇒ 差 {(maxShown - maxFocus) * 144f:0.#} px）");

                Check("h3 ★ 判据自检（退化样本 = **旧口径**：状态被改写 + 清零速度）在同一段输入上必须变红",
                    oJump > maxShown * 1.1f,
                    $"旧口径 max|Δ显示机位| = {oJump:0.######} 格 vs 新口径 {maxShown:0.######} 格" +
                    $"（= +{(oJump / maxShown - 1f) * 100f:0.#}%，{(oJump - maxShown) * 144f:0.#} px@1080p）" +
                    " ⇒ 旧口径「脱开后从静止起步 / 补攒下的账」造成的单帧超出可被判据抓出");

                // ── h4 team-lead 指定的口径：**相机格空间「步长序列」在咬合期/脱开后不存在阶跃** ──
                //   度量 = 步长序列自身的一阶差分 `max|step[i] − step[i−1]|`（"阶跃"二字的直接量化）。
                //   判据线**不引绝对常数**：拿同一次运行里的**旧口径**当尺（新口径要 ≤ 旧口径的一半）。
                //   一处如实说明：旧口径的"脱开首帧速度归零"在本配置下**不表现为步长掉到 0**
                //   （`smoothTime = 0.02s` 远小于 `dt = 1/60s` ⇒ `SmoothDamp` 那一帧由**误差项**主导、
                //   照样输出一大步）——它表现为**步长过冲**（h3 的 +15.5%）+ **步长序列的阶跃**（本条）。
                // **实测反例（本判据改成"把反例钉住"）**：team-lead 指定的"脱开首帧速度**不归零**"
                //    这条**在两口径下都不成立为差异** —— 实测两个口径的脱开首帧步长都 ≈ 玩家步长：
                //      · 新口径 `pShown[r]` / 旧口径 `oSteps[r]` 与玩家该帧步长之比都接近 1；
                //      · 原因：`smoothTime = 0.02s` **远小于** `dt = 1/60s` ⇒ `SmoothDamp` 那一帧由**误差项**
                //        主导，速度状态被清零也照样输出一大步 ⇒ "清零"**不**表现为步长掉到 0。
                //    步长**序列**的阶跃量（`max|Δstep|`）同样不是分辨量：新 0.05965 vs 旧 0.04749 格
                //    —— 阶跃的真正来源是**夹制本身**（被夹轴不动 ⇒ 步长变小；脱开后恢复满步长），
                //    两个口径都有，与该不该清零速度状态无关。
                //    ⇒ 旧口径**唯一**可测的代价 = h3 的**单帧过冲**（+15.5% = 1.3px@1080p）。
                //    本断言把上面这条澄清**钉成判据**（将来谁把脱开首帧做成"掉到 0"就会红）。
                var dNew = MaxAbsStepDelta(pShown);
                var dOld = MaxAbsStepDelta(oSteps);
                var relNew = float.NaN;
                var relOld = float.NaN;
                if (releaseAt.Count > 0)
                {
                    var r = releaseAt[0];
                    if (r < pShown.Count && r < pFocus.Count && pFocus[r] > 1e-6f) relNew = pShown[r] / pFocus[r];
                    if (r < oSteps.Count && r < pFocus.Count && pFocus[r] > 1e-6f) relOld = oSteps[r] / pFocus[r];
                }
                // 实测读数（脱开首帧步长 / 玩家该帧步长）：新 **0.5627**、旧 **0.5932** —— **两口径几乎同一个数**
                //   ⇒ 钉住的结论 = "清零速度状态**不**改变脱开首帧的步长"（差异 0.031，判据线 0.1 = 区分"同/不同"的分辨率）
                //   同时要求两者都 > 0.2（只看数量级：证的是"**不**掉到 0"，不承担精细判定）。
                Check("h4 口径澄清（⭐ 实测反例已钉住）：脱开首帧步长**两口径相同**且**不为 0**（\"清零速度\"不改变它）",
                    relNew == relNew && relOld == relOld
                    && MathF.Abs(relNew - relOld) <= 0.1f && relNew > 0.2f && relOld > 0.2f,
                    $"脱开首帧 #{string.Join(",", releaseAt)}：步长/玩家步长 = 新 {relNew:0.####} vs 旧 {relOld:0.####}" +
                    $"（差 {MathF.Abs(relNew - relOld):0.####}；两口径同一个数 ⇒ 清零速度状态**不**改脱开首帧的步长）" +
                    $"；步长序列 max|Δstep|：新 = {dNew:0.######} vs 旧 = {dOld:0.######} 格（阶跃来自夹制本身，不是清零）" +
                    "；⇒ 旧口径唯一可测代价 = h3 的单帧过冲 +15.5%（见 CameraRig.StepFollow 注释）");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 17. 双武器组（T0 判据缺口 2）—— 读键 → 事件 → 装备侧真的换了主手 → 派生重算
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 本轮新增：原版 <c>W</c> 键切武器组的**端到端**自证。
        /// <para>
        /// 断言分三层，每层都咬在生产代码上：
        /// ① **读键层**：`InputReader.SwapWeaponPressed` 只由 `GameKeyAlias.KeySwapWeapon` 驱动
        ///    （按 W 为真 / 按 R 为假）—— 直接咬"键位唯一来源"这条硬约束；
        /// ② **链路层**：`PlayerModule.Tick` 读键 ⇒ 发 `Events.SwapWeaponRequest`；`ItemModule` 收到后
        ///    真的改了生效武器组（`ActiveWeaponGroup` 翻转）；
        /// ③ **数值层**：装备载荷只带**生效组**那把武器 ⇒ `PlayerModule` 的攻击力只吃它的词缀
        ///    （换组后 AR 从 +20 变 +50，而**不是** +70 —— 这条正是"背着两把武器双倍加成"的反向断言）。
        /// </para>
        /// </summary>
        private static void Step17_WeaponGroup(AppContext ctx, PlayerModule player, object mapObj,
            ScriptedInput input, RecordingEventBus bus)
        {
            Section("17. ★ 双武器组（T0 判据缺口 2）：W 键 → SwapWeaponRequest → 主手互换 / 切回 / 派生只算生效组");

            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250916);
            player.CreateNew(PlayerClass.Amazon, "WgHero");
            player.TeleportTo(map.SpawnPoint);

            var itemMod = ctx.Item as Diablo2.Module.Item.ItemModule;
            Check("ItemModule 是真实现（本步断言打在它上面，不是桩）", itemMod != null,
                ctx.Item == null ? "null" : ctx.Item.GetType().FullName);

            ctx.Item.Reset();
            var factory = new Diablo2.Module.Item.ItemFactory();
            // 表里两件 **str_req=0 / lvl_req=0** 的武器（`item_c`）：1 = 手斧 hax（1×3，伤害 3-6）、
            // 5 = 法杖 wnd（1×2，伤害 2-4）⇒ 1 级亚马逊也装得上，断言不依赖职业数值。
            var wA = factory.Create(1, 1, ItemQuality.Normal, new Rng(9001));
            var wB = factory.Create(5, 1, ItemQuality.Normal, new Rng(9002));
            wA.affixes.Add(new ItemAffix { affixId = 101, kind = AffixKind.Prefix, mod = "att", value = 20 });
            wB.affixes.Add(new ItemAffix { affixId = 102, kind = AffixKind.Prefix, mod = "att", value = 50 });
            Check("两件武器就位（各带一条 att 词缀：Ⅰ组 +20 / Ⅱ组 +50）",
                wA != null && wB != null && wA.strReq == 0 && wB.strReq == 0 && wA.lvlReq == 0 && wB.lvlReq == 0,
                $"{wA?.name}(att +{wA?.affixes[0].value}) vs {wB?.name}(att +{wB?.affixes[0].value})");

            var arBase = player.AttackRating;
            ctx.Item.AddToInventory(wA);
            var a1 = FirstItemAnchor(ctx.Item);
            Check("装 Ⅰ组武器", a1 >= 0 && ctx.Item.EquipFromInventory(a1), "anchor=" + a1);
            Check("只有 1 件武器 ⇒ 生效组 = 0（Ⅰ）", itemMod.ActiveWeaponGroup == 0 && itemMod.WeaponGroupCount == 1,
                $"active={itemMod.ActiveWeaponGroup} groups={itemMod.WeaponGroupCount}");
            Check("AR = 基础 + 20（Ⅰ组词缀生效）", player.AttackRating == arBase + 20,
                $"{arBase} → {player.AttackRating}");

            ctx.Item.AddToInventory(wB);
            var a2 = FirstItemAnchor(ctx.Item);
            Check("装 Ⅱ组武器", a2 >= 0 && ctx.Item.EquipFromInventory(a2), "anchor=" + a2);
            // `IItemModule.Equipment` 的语义已收紧为**生效集**（只含生效组那把武器）⇒
            //   `CombatModule.GetWeaponDamage` 不再把两把武器的 dmg 相加；两套都存得住由 `WriteTo` 保证。
            Check("两把武器都装着（组数 = 2）而生效集只有 1 把（基础伤害也只算这一把）",
                itemMod.WeaponGroupCount == 2 && ctx.Item.Equipment.Count == 1,
                $"组数={itemMod.WeaponGroupCount} 生效集={ctx.Item.Equipment.Count}");
            Check("生效组跟着新武器 = 1（Ⅱ）", itemMod.ActiveWeaponGroup == 1, "active=" + itemMod.ActiveWeaponGroup);
            Check("AR = 基础 + 50 **而不是** +70（非生效组的词缀不进载荷 ⇒ 不会双倍加成）",
                player.AttackRating == arBase + 50, $"{arBase} → {player.AttackRating}（+20 与 +70 都是错的）");

            // ① 读键层：只有 W 为真
            input.BeginFrame();
            input.Press(GameKeyAlias.KeySwapWeapon);
            Check("InputReader.SwapWeaponPressed：按 W（GameKeyAlias.KeySwapWeapon）为真",
                player.Input.SwapWeaponPressed, "SwapWeaponPressed=true");
            input.BeginFrame();
            input.Press(GameKeyAlias.KeyRunToggle);
            Check("按 R（走/跑键）时 SwapWeaponPressed 为假 ⇒ 键位绑定是 W 而不是「任意键」",
                !player.Input.SwapWeaponPressed, "SwapWeaponPressed=false");
            input.BeginFrame();

            // ② 链路层：W → 事件 → Item 侧真的切了
            var swapsBefore = bus.CountOf(Events.SwapWeaponRequest);
            input.BeginFrame();
            input.Press(GameKeyAlias.KeySwapWeapon);
            player.Tick(Dt);
            input.BeginFrame();
            Check("W 键 ⇒ 发 Events.SwapWeaponRequest（消费点从 0 变 1）",
                bus.CountOf(Events.SwapWeaponRequest) == swapsBefore + 1,
                $"次数 {swapsBefore} → {bus.CountOf(Events.SwapWeaponRequest)}");
            Check("切换后生效组 = 0（Ⅰ）", itemMod.ActiveWeaponGroup == 0, "active=" + itemMod.ActiveWeaponGroup);
            Check("AR 回到 基础 + 20（主手/副手互换后派生重算）", player.AttackRating == arBase + 20,
                $"{arBase + 50} → {player.AttackRating}");

            var eqSwap = ctx.Item.Snapshot().equip;
            Check("装备载荷里只剩 Ⅰ组那把（主手互换，非生效组不进载荷）",
                WeaponsIn(eqSwap).Count == 1 && WeaponsIn(eqSwap)[0].itemId == wA.itemId,
                "载荷武器=" + WeaponNamesOf(eqSwap));
            Check("接线口径留了一条 Info（只报一次）", CaptureLogger.Has("[SwapWeapon] 原版 W 键按下"),
                CaptureLogger.Last("[SwapWeapon]"));
            Check("切换本体留了含两个组名的 Info", CaptureLogger.Has("[SwapWeapon] 武器组 Ⅱ → Ⅰ"),
                CaptureLogger.Last("[SwapWeapon] 武器组"));

            // ③ 再按一次 W ⇒ 切回原样
            input.BeginFrame();
            input.Press(GameKeyAlias.KeySwapWeapon);
            player.Tick(Dt);
            input.BeginFrame();
            Check("再按一次 W ⇒ 回到 Ⅱ（可逆）", itemMod.ActiveWeaponGroup == 1, "active=" + itemMod.ActiveWeaponGroup);
            Check("切回后 AR 又是 基础 + 50（与切换前一致）", player.AttackRating == arBase + 50,
                "AR=" + player.AttackRating);
            Check("两条事件（一来一回）", bus.CountOf(Events.SwapWeaponRequest) == swapsBefore + 2,
                "次数=" + bus.CountOf(Events.SwapWeaponRequest));
        }

        /// <summary>（本轮新增）装备载荷里的武器（`ItemStack.type == ItemType.Weapon`）。</summary>
        private static List<ItemStack> WeaponsIn(List<ItemStack> equip)
        {
            var res = new List<ItemStack>();
            if (equip == null) return res;
            for (var i = 0; i < equip.Count; i++)
            {
                if (equip[i] != null && equip[i].type == ItemType.Weapon) res.Add(equip[i]);
            }
            return res;
        }

        /// <summary>（本轮新增）装备载荷里武器的可读清单（断言失败时的详情）。</summary>
        private static string WeaponNamesOf(List<ItemStack> equip)
        {
            var w = WeaponsIn(equip);
            if (w.Count == 0) return "(无武器)";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < w.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(w[i].name).Append('#').Append(w[i].itemId);
            }
            return sb.ToString();
        }

        /// <summary>（本轮新增）背包里第一个"有物品的锚点格"（本步先 Reset 过背包 ⇒ 就是刚放进去那件）。</summary>
        private static int FirstItemAnchor(Diablo2.Module.IItemModule item)
        {
            var inv = item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i].isAnchor && inv[i].item != null) return i;
            }
            return -1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        private static void Step18_DeathGold(AppContext ctx, PlayerModule player, object mapObj, RecordingEventBus bus)
        {
            Section("18. ★ 死亡扣金币 10%（T0 判据缺口 1；本宿主用真实 PlayerModule 独立复验一遍）");

            var town = (Diablo2.Module.Map.MapModule)mapObj;
            town.Generate(AreaId.Town, 20250916);

            player.CreateNew(PlayerClass.Amazon, "GoldHero");
            player.TeleportTo(town.SpawnPoint);
            player.AddGold(12345);
            Check("金币就位 12345", player.Gold == 12345, "gold=" + player.Gold);

            var ruleLines = CaptureLogger.Count("死亡扣金币");
            player.Kill();
            Check("死亡扣 10%：12345 → 11111（损失 1234 = floor(12345×10%)）", player.Gold == 11111,
                "gold=" + player.Gold);
            Check("生效口径行打了一条（tag = T0GAP）", CaptureLogger.Count("死亡扣金币") == ruleLines + 1,
                $"口径行 {ruleLines} → {CaptureLogger.Count("死亡扣金币")}");
            Check("口径行写清了取整口径与「不会为负」的论证",
                CaptureLogger.Has("[T0GAP]") && CaptureLogger.Has("向下取整") && CaptureLogger.Has("不会为负"),
                CaptureLogger.Last("[T0GAP]"));

            player.Revive();
            player.Kill();
            Check("同一角色连续第二次死亡仍扣：11111 → 10000（损失 1111）", player.Gold == 10000,
                "gold=" + player.Gold);
            Check("口径行只报一次（第二次死亡不再打）", CaptureLogger.Count("死亡扣金币") == ruleLines + 1,
                "口径行=" + CaptureLogger.Count("死亡扣金币"));
            Check("扣后不为负", player.Gold >= 0, "gold=" + player.Gold);

            player.Revive();
            Check("先花光金币", player.AddGold(-player.Gold) && player.Gold == 0, "gold=" + player.Gold);
            player.Kill();
            Check("金币 0 ⇒ 死亡不扣、不为负", player.Gold == 0, "gold=" + player.Gold);

            player.Revive();
            player.AddGold(7);
            player.Kill();
            Check("金币 7（< 10）⇒ 损失 floor(7/10)=0 ⇒ 仍为 7", player.Gold == 7, "gold=" + player.Gold);
        }

        /// <summary>世界坐标 → **连续格坐标**（z 分量清零：世界是 z=0 的 XY 平面，相机 z=-10）。</summary>
        private static Vector2 CellOf(Vector3 world) => Iso.WorldToGridContinuous(new Vector3(world.x, world.y, 0f));

        // ═════════════════════════════════════════════════════════════════════
        // U27：三分判据（相机 / 渲染节点 / 逻辑）用的检测函数
        //      没睡醒的检测函数会让"全绿"变成假绿。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// U27 层②的口径：角色**渲染节点**的世界坐标 = 直接调**生产件** `ViewModule.EntityWorld`
        /// （`internal static`，本宿主已把 `Module/View` 编入；它就是 `ViewModule.TickPlayer`
        /// 写 `EntityView.Root.transform.position` 的那一行，见 `ViewModule.cs:787` / `:1345`）。
        /// 不在宿主里镜像这个公式 —— 镜像 = 改了生产也不变红的假闸门。
        /// </summary>
        private static Vector3 RenderWorldOf(Vector3 logicWorld) =>
            Diablo2.Module.View.ViewModule.EntityWorld(GameConst.PlayerEntityId, logicWorld);

        /// <summary>
        /// U27 f3：**「世界步长 / 格步长」比值的最大值**（8 个格方向里取最大）。
        /// 同一个格方向下这个比值是常数（`Iso` 是线性映射）⇒ 它的最大值就是"同样的速度 ×dt
        /// 最多能在世界里走多远"的量纲换算因子。现算（走生产件 `Iso.GridToWorld`），不写死 1.414。
        /// </summary>
        private static float MaxWorldPerGridRatio()
        {
            var dirs = new[]
            {
                new Vector2Int(1, 0), new Vector2Int(0, 1),
                new Vector2Int(1, 1), new Vector2Int(1, -1),
            };
            var origin = Iso.GridToWorld(Vector2Int.zero);
            var best = 0f;
            for (var i = 0; i < dirs.Length; i++)
            {
                var w = Iso.GridToWorld(dirs[i]) - origin;
                var wl = MathF.Sqrt(w.x * w.x + w.y * w.y);
                var gl = MathF.Sqrt(dirs[i].x * dirs[i].x + dirs[i].y * dirs[i].y);
                var r = wl / gl;
                if (r > best) best = r;
            }
            return best;
        }

        //   口径 = 既有真机量法（不引新参数）：J = 逐帧步长相对**局部均值**（±win 帧）的纵向偏差 sd；
        //      corr = corr(纵向偏差, dt)。这两个量在真机 TSV 上就是同一把尺。

        /// <summary>真机逐帧 dt 序列（判据资产，文件头带出处；取不到返回 null，由调用方如实留痕）。</summary>
        private static List<float> LoadRealDtSeq(string path)
        {
            try
            {
                // 路径解析：`dotnet run` 的 CWD 因调用方式而异（本项目根 / 宿主目录 / 别处）
                // ⇒ 从**可执行文件目录**逐级向上找「仓库根」（判据资产按仓库相对路径登记）。
                var resolved = (string)null;
                var candidates = new List<string> { path };
                // 必须写全名 `System.AppContext`：本文件有 `using AppContext = Diablo2.App.AppContext;`
                //    的别名（组合根），裸写会解析到别名 ⇒ CS0117。
                var up = System.AppContext.BaseDirectory;
                // 10 级 = 从 `<宿主>/bin/Debug/net10.0` 一直找到**仓库根**（实测 8 级只到 `tools/`）
                for (var i = 0; i < 10 && up != null; i++)
                {
                    candidates.Add(System.IO.Path.Combine(up, path));
                    up = System.IO.Path.GetDirectoryName(up);
                }
                for (var i = 0; i < candidates.Count; i++)
                    if (System.IO.File.Exists(candidates[i])) { resolved = candidates[i]; break; }
                if (resolved == null)
                {
                    Console.WriteLine("    [WARN ] 真机 dt 序列找不到；试过 " + string.Join(" | ", candidates));
                    return null;
                }

                var list = new List<float>();
                foreach (var line in System.IO.File.ReadAllLines(resolved))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    float v;
                    if (float.TryParse(line, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v) && v > 0f)
                        list.Add(v);
                }
                return list.Count > 0 ? list : null;
            }
            catch (Exception e)
            {
                Console.WriteLine("    [WARN ] 读真机 dt 序列失败（" + e.GetType().Name + ": " + e.Message + "）⇒ 时间轴判据跳过");
                return null;
            }
        }

        private static float Mean(List<float> xs)
        {
            var s = 0f;
            for (var i = 0; i < xs.Count; i++) s += xs[i];
            return xs.Count > 0 ? s / xs.Count : 0f;
        }

        private static float Min(List<float> xs) { var m = float.MaxValue; for (var i = 0; i < xs.Count; i++) if (xs[i] < m) m = xs[i]; return m; }
        private static float Max(List<float> xs) { var m = float.MinValue; for (var i = 0; i < xs.Count; i++) if (xs[i] > m) m = xs[i]; return m; }

        /// <summary>百分位（线性插值；与既有真机量法的 `pct()` 同算法）。</summary>
        private static float Pct(List<float> xs, float q)
        {
            if (xs.Count == 0) return float.NaN;
            var s = new List<float>(xs); s.Sort();
            if (s.Count == 1) return s[0];
            var pos = q * (s.Count - 1);
            var lo = (int)Math.Floor(pos);
            var hi = (int)Math.Ceiling(pos);
            return lo == hi ? s[lo] : s[lo] + (s[hi] - s[lo]) * (pos - lo);
        }

        /// <summary>
        /// U27 g2：逐帧步长序列的**抖动 sd**（局部均值 ±<paramref name="win"/> 帧的纵向偏差 sd）。
        /// 序列是**一维标量**（格空间步长 = speed×dt，与方向无关）⇒ 纵向偏差 = 该帧步长 − 局部均值，
        /// 与既有真机量法的两维版在同一把尺上（直线段上等价）。
        /// </summary>
        private static float JudderSd(List<float> steps, int win)
        {
            var dev = new List<float>();
            for (var i = 0; i < steps.Count; i++)
            {
                var lo = Math.Max(0, i - win);
                var hi = Math.Min(steps.Count, i + win + 1);
                var m = 0f;
                for (var k = lo; k < hi; k++) m += steps[k];
                m /= (hi - lo);
                dev.Add(steps[i] - m);
            }
            if (dev.Count < 2) return 0f;
            var mu = Mean(dev);
            var s = 0f;
            for (var i = 0; i < dev.Count; i++) { var d = dev[i] - mu; s += d * d; }
            return (float)Math.Sqrt(s / dev.Count);
        }

        /// <summary>
        /// U27 g3：`corr(逐帧步长的纵向偏差, dt)` —— 与既有真机量法的 `corr_dt()` 同口径。
        /// 高相关 ⇒ 不匀就是帧时间造成的（位移 = v·dt ⇒ 理想相关 = 1）。
        /// </summary>
        private static float CorrDt(List<float> steps, List<float> dts, int win)
        {
            var jl = new List<float>();
            var dl = new List<float>();
            for (var i = 0; i < steps.Count; i++)
            {
                var lo = Math.Max(0, i - win);
                var hi = Math.Min(steps.Count, i + win + 1);
                var m = 0f;
                for (var k = lo; k < hi; k++) m += steps[k];
                m /= (hi - lo);
                jl.Add(steps[i] - m);
                dl.Add(dts[i % dts.Count]);
            }
            if (jl.Count < 5) return float.NaN;
            var mj = Mean(jl); var md = Mean(dl);
            var sj = 0f; var sd = 0f; var cov = 0f;
            for (var i = 0; i < jl.Count; i++)
            {
                var a = jl[i] - mj; var b = dl[i] - md;
                sj += a * a; sd += b * b; cov += a * b;
            }
            sj = (float)Math.Sqrt(sj / jl.Count); sd = (float)Math.Sqrt(sd / dl.Count);
            if (sj < 1e-12f || sd < 1e-12f) return float.NaN;
            return (cov / jl.Count) / (sj * sd);
        }

        /// <summary>
        /// U27 h4：「步长序列」的一阶差分最大值 `max|step[i] − step[i−1]|` = **阶跃量**（格）。
        /// 用于判"咬合期/脱开后速度不连续"：序列平滑 ⇒ 这个量小。
        /// </summary>
        private static float MaxAbsStepDelta(List<float> steps)
        {
            var worst = 0f;
            for (var i = 1; i < steps.Count; i++)
            {
                var d = MathF.Abs(steps[i] - steps[i - 1]);
                if (d > worst) worst = d;
            }
            return worst;
        }

        /// <summary>U27 f1：两条世界坐标序列的**逐帧最大偏差**（xy 平面；层间差判据）。</summary>
        private static float LayerDiffMax(List<Vector3> a, List<Vector3> b)
        {
            var max = 0f;
            var n = Math.Min(a.Count, b.Count);
            for (var i = 0; i < n; i++)
            {
                var dx = a[i].x - b[i].x;
                var dy = a[i].y - b[i].y;
                var d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > max) max = d;
            }
            return max;
        }

        /// <summary>U27 f1b：同一条判据，先把 <paramref name="a"/> 整体加一个偏移（**退化样本**）。</summary>
        private static float LayerDiffMaxWithInjection(List<Vector3> a, List<Vector3> b, float offset)
        {
            var injected = new List<Vector3>(a.Count);
            for (var i = 0; i < a.Count; i++) injected.Add(a[i] + new Vector3(offset, 0f, 0f));
            return LayerDiffMax(injected, b);
        }

        /// <summary>
        /// U27 f2：**层内**判据 —— 逐帧算「世界步长 / 格步长」的比值，按 8 个格空间方向分组，
        /// 返回**组内** spread（max/min）的最大值（1.0 = 同一方向每帧推进量恒定）。
        /// <para>为什么按方向分组：格空间恒定速度 × 等距 2:1 投影 ⇒ **跨方向**的比值本来就不等
        /// （解析 0.7071 ~ 1.4142），那是投影口径（登记在案，不判、不改）；能判的是"同一方向内"。</para>
        /// <para>分组键 = `Iso.DirectionTo(符号化的格增量)`（生产件；增量取符号后 8 方向各一桶）。</para>
        /// </summary>
        private static float AdvanceSpread(List<Vector3> seq, float budget, bool filterTurns,
            out int buckets, out string detail)
        {
            // 桶键 = **格增量的符号对**（8 个），不用 `Iso.DirectionTo`：
            //   实测（本判据第一版）：`DirectionTo` 判的是**屏幕**朝向 ⇒ 屏幕空间里
            //   `(1,-1)` 与 `(1,0)` 落在同一个 45° 扇区（投影角 0° 与 −26.6°）⇒ 两个不同的
            //   格方向被并成一桶，桶内 spread 假红 1.581（实测已复现）。
            //   而 世界/格 比值只取决于**格方向**（`Iso` 是线性映射）⇒ 必须按格方向分桶。
            var byDir = new Dictionary<int, List<float>>();
            var skippedTurn = 0;
            for (var i = 1; i < seq.Count; i++)
            {
                var dg = CellOf(seq[i]) - CellOf(seq[i - 1]);
                var dgl = dg.magnitude;
                if (dgl < 1e-6f) continue;                       // 没动：方向未定义，不进样本
                // 「纯」帧（只走一个方向）：整帧预算全花在同一段 ⇒ |Δ格| == 标称预算。
                //   跨路点那一帧会把**两段不同方向**的位移相加（|Δ格| < 预算，比值是两段混合）
                //   —— 那是**换向帧**，不是"同一方向推进量不齐"，必须剔出样本。
                //   尺子就是**标称预算本身**（`speed×dt`，移动积分口径的输入），不引新阈值。
                if (filterTurns && dgl < budget * (1f - 1e-3f)) { skippedTurn++; continue; }
                var dw = seq[i] - seq[i - 1];
                var dwl = MathF.Sqrt(dw.x * dw.x + dw.y * dw.y);
                // **零分量必须带容差**：轴方向步长的"零"那一维是浮点残渣（~1e-7），
                //    直接判符号会把它当成 ±1 ⇒ 纯轴向帧被塞进对角桶 ⇒ 桶内 spread 假红 1.581
                //    （实测第一版就是这个假红；`dirsign` 用预算的 1e-3 当零带，与上面的"纯帧"尺同源）。
                var eps = budget * 1e-3f;
                var sx = dg.x > eps ? 1 : (dg.x < -eps ? -1 : 0);
                var sy = dg.y > eps ? 1 : (dg.y < -eps ? -1 : 0);
                if (sx == 0 && sy == 0) { skippedTurn++; continue; }   // 双向都是残渣 ⇒ 这帧方向不可判
                var key = (sx + 1) * 3 + (sy + 1);               // 0..8 唯一的符号对编码
                if (!byDir.ContainsKey(key)) byDir[key] = new List<float>();
                byDir[key].Add(dwl / dgl);
            }
            buckets = 0;
            var worst = 1f;
            var sb = new System.Text.StringBuilder();
            sb.Append($"（剔出换向/收尾帧 {skippedTurn} 帧）");
            foreach (var kv in byDir)
            {
                var v = kv.Value;
                if (v.Count < 5) continue;                       // 样本太少的桶不参与判定（只登记）
                buckets++;
                var lo = v[0];
                var hi = v[0];
                for (var i = 1; i < v.Count; i++)
                {
                    if (v[i] < lo) lo = v[i];
                    if (v[i] > hi) hi = v[i];
                }
                var spread = hi / lo;
                if (spread > worst) worst = spread;
                var sx2 = kv.Key / 3 - 1;
                var sy2 = kv.Key % 3 - 1;
                sb.Append($"  格({sx2},{sy2}) n={v.Count} 世界/格={lo:0.#####}~{hi:0.#####}");
            }
            detail = "分格方向读数（世界步长/格步长；**跨方向**的差异 = 等距 HalfW:HalfH=2:1 的必然，只登记不判）：" + sb;
            return worst;
        }

        /// <summary>
        /// U27 f2b（**判据自检 / 退化样本**）：同一条检测函数，**把"纯帧"过滤关掉**再喂同一批采样。
        /// <para>为什么这才是这条判据的退化样本：世界→格的映射是**固定线性**的 ⇒ 单帧的
        /// 「世界步长/格步长」比值**只由格方向决定**，世界坐标上的任何扰动都会连带改掉格增量
        /// ⇒ 你**造不出**"同方向、不同比值"的样本（实测：注入 5% 步长改动比值恒为 0.0）。
        /// 能造出"同桶不同比值"的只有**换向帧**（一帧横跨两个格方向，比值为两段混合）。</para>
        /// <para>⇒ 自检内容 = 「**过滤关掉后同一批采样必须变红**」：既证明换向帧真实存在，
        /// 也证明上面的过滤是**承重**的（不是把缺陷一起滤掉了）。</para>
        /// </summary>
        private static float AdvanceSpreadNoTurnFilter(List<Vector3> seq, float budget)
        {
            int buckets;
            string detail;
            return AdvanceSpread(seq, budget, false, out buckets, out detail);
        }

        /// <summary>
        /// U27 f3：**相机层不许"跳"/"冻"** —— 逐帧量「相机与逻辑的相对偏移」的变化量
        /// （世界空间；方向无关，故可用一把尺）。
        /// <para>上界有解析出处：**相机最坏情况 = 完全冻结**（机位不动）⇒ 相对偏移的变化量 = 玩家该帧位移；
        /// 低通只允许"跟不上一帧"，不允许"冲出去"（同一余量口径见 `CameraRig` c1 的 ×1.1）。
        /// ⇒ 上界 = 玩家该帧最大位移 × 1.1。</para>
        /// <para>返回最大超限量与其发生的帧号（&lt; 0 = 无超限）。</para>
        /// </summary>
        private static float RelativeOffsetJump(List<Vector3> camSeq, List<Vector3> logicSeq,
            float perFrameLimit, out int at)
        {
            var worst = -1f;
            at = -1;
            var n = Math.Min(camSeq.Count, logicSeq.Count);
            for (var i = 1; i < n; i++)
            {
                var r0 = new Vector2(logicSeq[i - 1].x - camSeq[i - 1].x, logicSeq[i - 1].y - camSeq[i - 1].y);
                var r1 = new Vector2(logicSeq[i].x - camSeq[i].x, logicSeq[i].y - camSeq[i].y);
                var j = (r1 - r0).magnitude;
                if (j <= perFrameLimit + 1e-4f) continue;
                if (j - perFrameLimit > worst) { worst = j - perFrameLimit; at = i; }
            }
            return worst;
        }

        /// <summary>
        /// 地图上**最长的一条「单方向直线」可走段**（同一行或同一列的连续可走格），
        /// 用于「匀速**直线**运动 ⇒ 相机每帧位移单调收敛」这条断言（锯齿路径上该性质**不成立**，
        /// 换向时相机的滞后矢量要先转向 ⇒ 单帧位移会有正确的小幅回落）。
        /// </summary>
        /// <returns>找到（长度 ≥ 6 格）返回 true 并给出起终点；否则 false。</returns>
        private static bool FindLongestAxisRun(Diablo2.Module.Map.MapModule map,
            out Vector2Int start, out Vector2Int end)
        {
            start = Vector2Int.zero;
            end = Vector2Int.zero;
            var bestLen = 0;
            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1) };   // 只扫两个方向（反向段会被另一个起点扫到）
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var a = new Vector2Int(x, y);
                    if (!map.Walkable(a)) continue;
                    for (var d = 0; d < dirs.Length; d++)
                    {
                        var prev = new Vector2Int(a.x - dirs[d].x, a.y - dirs[d].y);
                        if (map.Walkable(prev)) continue;      // 不是段的起点（上一格也可走 ⇒ 会更早被扫到）
                        var n = 0;
                        var g = a;
                        while (map.Walkable(g) && map.TileAt(g) != TileKind.Exit)
                        {
                            n++;
                            g = new Vector2Int(g.x + dirs[d].x, g.y + dirs[d].y);
                        }
                        if (n > bestLen)
                        {
                            bestLen = n;
                            start = a;
                            end = new Vector2Int(a.x + dirs[d].x * (n - 1), a.y + dirs[d].y * (n - 1));
                        }
                    }
                }
            }
            return bestLen >= 6;
        }

        /// <summary>变异系数（标准差 / 均值；剔除首 <paramref name="skip"/> 帧与末 <paramref name="tail"/> 帧）。</summary>
        private static float Cv(List<float> xs, int skip, int tail)
        {
            var lo = skip;
            var hi = xs.Count - tail;
            if (hi - lo < 2) return 0f;
            var mean = 0f;
            for (var i = lo; i < hi; i++) mean += xs[i];
            mean /= hi - lo;
            if (mean <= 1e-6f) return 0f;
            var varSum = 0f;
            for (var i = lo; i < hi; i++) { var d = xs[i] - mean; varSum += d * d; }
            return (float)Math.Sqrt(varSum / (hi - lo)) / mean;
        }

        /// <summary>
        /// 从头（出生点）走同一条路径到 <paramref name="target"/>，返回**终点连续格坐标**。
        /// `dtSeq != null` ⇒ 逐帧在序列里循环取 dt（隔离"帧率敏感"）；否则用固定 <paramref name="fixedDt"/>。
        /// </summary>
        private static Vector2 WalkSamePath(PlayerModule player, Diablo2.Module.Map.MapModule map,
            Vector2Int target, float[] dtSeq, float fixedDt)
        {
            player.Stop();
            player.TeleportTo(map.SpawnPoint);
            player.MoveTo(target);
            for (var f = 0; f < 20000 && player.IsMoving; f++)
            {
                var dtNow = dtSeq != null ? dtSeq[f % dtSeq.Length] : fixedDt;
                player.Tick(dtNow);
            }
            return CellOf(player.World);
        }

        /// <summary>
        /// 让相机**跟随真主角**走同一条路径（`ctx.Flow` 由调用方置为非 null ⇒ `RefreshFocus` 走"跟主角"那条），
        /// 逐帧记录「相机单帧位移 / 焦点单帧位移」（均为 cell 空间），并给出最大滞后与单帧位移上限。
        /// </summary>
        private static void TraceCamera(PlayerModule player, CameraRig rig, Diablo2.Module.Map.MapModule map,
            Vector2Int start, Vector2Int target, float[] dtSeq, float fixedDt, List<float> camSteps,
            List<float> playSteps, out float gapMax, out float limit, out float gapLast,
            List<float> latOffsets = null)
        {
            player.Stop();
            player.TeleportTo(start);
            rig.Reset();
            rig.EnableZoom = false;
            rig.EnableEdgeScroll = false;
            rig.SetTargetGrid(start);
            rig.SnapToTarget();
            rig.Tick(fixedDt > 0f ? fixedDt : 0.02f);      // 吸收一帧，让机位先到位

            player.MoveTo(target);
            var prevCam = CellOf(rig.Position);
            var prevPlay = CellOf(player.World);
            var prevWorld = player.World;                  // 世界坐标（横向偏移量法要用未取整的一份）
            gapMax = 0f;
            gapLast = 0f;
            limit = 0f;

            for (var f = 0; f < 20000 && player.IsMoving; f++)
            {
                var dtNow = dtSeq != null ? dtSeq[f % dtSeq.Length] : fixedDt;
                player.Tick(dtNow);
                rig.Tick(dtNow);
                var c = CellOf(rig.Position);
                var p = CellOf(player.World);

                //   定义 = 离线量法的 lat_i（r·perp(u)，u = 本帧玩家位移方向）
                //   ⇒ 线上 Play 证据与离线断言同一把尺。
                if (latOffsets != null)
                {
                    var pw = player.World;
                    var dpx = pw.x - prevWorld.x;
                    var dpy = pw.y - prevWorld.y;
                    var st = MathF.Sqrt(dpx * dpx + dpy * dpy);
                    if (st > 1e-6f)
                    {
                        var lx = -dpy / st;
                        var ly = dpx / st;
                        latOffsets.Add((pw.x - rig.Position.x) * lx + (pw.y - rig.Position.y) * ly);
                    }
                    prevWorld = pw;
                }
                camSteps.Add((c - prevCam).magnitude);
                playSteps.Add((p - prevPlay).magnitude);
                var gap = (p - c).magnitude;
                if (gap > gapMax) gapMax = gap;
                gapLast = gap;
                var l = player.MoveSpeed * dtNow;
                if (l > limit) limit = l;
                prevCam = c;
                prevPlay = p;
            }
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

        // ═════════════════════════════════════════════════════════════════════
        // 19. R1：鼠标右键（原版「右键 = 使用右键技能」）
        // ═════════════════════════════════════════════════════════════════════
        private static void Step19_RightClick(AppContext ctx, object mapObj, PlayerModule player,
            ScriptedInput input, RecordingEventBus bus)
        {
            Section("19. ★ R1：右键（button 1）= 使用右键技能 ⇒ 走**已有**施放入口 ISkillModule.TryCast");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250923);
            player.CreateNew(PlayerClass.Amazon, "RightClick");
            player.TeleportTo(map.SpawnPoint);
            player.Stop();

            var reader = player.Input;
            reader.Reset();
            input.Available = true;
            input.BeginFrame();

            input.RightMouseDown();
            player.Tick(0.02f);
            Check("Poll 读 button 1 ⇒ SecondaryDown / SecondaryHeld 都为 true（改动前恒 false）",
                reader.SecondaryDown && reader.SecondaryHeld,
                $"down={reader.SecondaryDown} held={reader.SecondaryHeld}");

            input.BeginFrame();
            input.RightMouseUp();
            player.Tick(0.02f);
            Check("右键抬起 ⇒ SecondaryUp 为 true（抬起语义与左键同款）",
                reader.SecondaryUp, $"up={reader.SecondaryUp}");
            input.BeginFrame();

            // ── ② 右键技能格绑了技能 ⇒ 走已有的 TryCast（右键技能真的被施放）──
            var stub = new StubSkill(0) { Button1 = 121 };
            ctx.Skill = stub;

            var cells = WalkableCells(map, 2);
            Check("找到 ≥2 个可走格供右键用例", cells.Count >= 2, $"找到 {cells.Count} 个");
            if (cells.Count < 2)
            {
                ctx.Skill = null;
                return;
            }
            var castGrid = cells[0];

            player.HandleSecondaryClick(castGrid);
            Check("右键绑了技能 ⇒ 调 ISkillModule.TryCast(右键技能 id, 目标格) 恰好 1 次且参数正确",
                stub.Casts.Count == 1 && stub.Casts[0] == $"#121@({castGrid.x},{castGrid.y})",
                stub.Casts.Count > 0 ? string.Join(" / ", stub.Casts) : "(无调用)");
            Check("留下了 [Cast] 右键施放技能 日志（数值类判据的锚点）",
                CaptureLogger.Has("[Cast] 右键施放技能 #121"),
                CaptureLogger.Last("[Cast]"));

            // ── ③ 未绑（-1）+ 指针下有怪 ⇒ 原版默认的普通攻击；右键**不移动** ──
            stub.Button1 = -1;
            stub.Casts.Clear();

            var monGrid = cells[1];
            var monsterId = GameConst.MonsterIdBase + 77;
            var stubMon = new StubMonsters();
            stubMon.Add(new MonsterState
            {
                id = monsterId, name = "堕落者(右键自检)", gridX = monGrid.x, gridY = monGrid.y,
                alive = true, hp = 10, maxHp = 10,
            });
            ctx.Monster = stubMon;
            reader.OverrideHoverGrid(monGrid);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);

            var atk0 = bus.CountOf(Events.AttackRequest);
            var mv0 = bus.CountOf(Events.MoveCommand);
            player.HandleSecondaryClick(monGrid);
            Check("未绑右键技能 ⇒ 按原版默认的普通攻击：发 AttackRequest 1 次且 id 正确",
                stub.Casts.Count == 0 && bus.CountOf(Events.AttackRequest) == atk0 + 1
                && bus.LastArgOf(Events.AttackRequest) == monsterId.ToString(),
                $"TryCast={stub.Casts.Count} Attack +{bus.CountOf(Events.AttackRequest) - atk0} 载荷={bus.LastArgOf(Events.AttackRequest)}");
            Check("右键**不产生移动意图**（原版右键不移动角色）：MoveCommand 不增",
                bus.CountOf(Events.MoveCommand) == mv0,
                $"MoveCommand +{bus.CountOf(Events.MoveCommand) - mv0}");

            // ── ④ 未绑 + 指针下没怪 ⇒ 无动作（非预期分支留 Warn，不空放技能）──
            var emptyGrid = map.SpawnPoint;
            reader.OverrideHoverGrid(emptyGrid);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            var atk1 = bus.CountOf(Events.AttackRequest);
            player.HandleSecondaryClick(emptyGrid);
            Check("未绑 + 指针下无怪 ⇒ 不发 AttackRequest / 不调 TryCast（只留一条 Warn）",
                bus.CountOf(Events.AttackRequest) == atk1 && stub.Casts.Count == 0,
                $"Attack +{bus.CountOf(Events.AttackRequest) - atk1} TryCast={stub.Casts.Count}");

            // ── ⑤ 指针压在 UI 上 ⇒ 右键不算施放意图（纯判定，不碰真实 EventSystem）──
            Check("UiEatsIntent(true,true)=true 且 (true,false)=false（点面板不会顺手放技能）",
                InputReader.UiEatsIntent(true, true) && !InputReader.UiEatsIntent(true, false)
                && !InputReader.UiEatsIntent(false, true),
                "见 InputReader.UiEatsIntent（TryGetSecondaryClick 的反投影之前就过它）");

            ctx.Skill = null;
            ctx.Monster = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 20. R4：F1~F8 技能槽 → SkillSlotAssignRequest
        // ═════════════════════════════════════════════════════════════════════
        private static void Step20_SkillSlots(AppContext ctx, PlayerModule player,
            ScriptedInput input, RecordingEventBus bus)
        {
            Section("20. ★ R4：F1~F8 技能槽（改动前 8 个键 + SkillSlotKey()/SkillSlotCount 全仓 0 消费）");

            // ── ① 键位与左右手映射（唯一来源 = Def/GameKeyAlias）──
            var allMapped = true;
            var leftOk = true;
            var idxOk = true;
            for (var slot = 1; slot <= GameKeyAlias.SkillSlotCount; slot++)
            {
                var key = GameKeyAlias.SkillSlotKey(slot);
                if (key == GameKey.None) allMapped = false;
                if (GameKeyAlias.SkillSlotIsLeftHand(slot) != (slot <= 4)) leftOk = false;
                if (GameKeyAlias.SkillSlotIndex(slot) != (slot - 1) % 4) idxOk = false;
            }
            Check("SkillSlotKey(1..8) 全部有键位（改动前 0 消费）", allMapped, $"count={GameKeyAlias.SkillSlotCount}");
            Check("SkillSlotIsLeftHand：F1~F4 = 左键 / F5~F8 = 右键", leftOk, "1..4=左, 5..8=右");
            Check("SkillSlotIndex：F1/F5=0、F2/F6=1、F3/F7=2、F4/F8=3", idxOk, "(slot-1)%4");
            Check("越界槽号：SkillSlotKey(0/9)=None、SkillSlotIndex(0/9)=-1",
                GameKeyAlias.SkillSlotKey(0) == GameKey.None && GameKeyAlias.SkillSlotKey(9) == GameKey.None
                && GameKeyAlias.SkillSlotIndex(0) == -1 && GameKeyAlias.SkillSlotIndex(9) == -1,
                "0/9 都拒绝");

            // ── ② 按下 F1~F8 ⇒ 每个键恰好发一次 SkillSlotAssignRequest，载荷 = 槽号 ──
            var received = new List<int>();
            bus.On<int>(Events.SkillSlotAssignRequest, s => received.Add(s));

            var reader = player.Input;
            reader.Reset();
            input.Available = true;
            input.BeginFrame();

            for (var slot = 1; slot <= GameKeyAlias.SkillSlotCount; slot++)
            {
                input.BeginFrame();
                input.Press(GameKeyAlias.SkillSlotKey(slot));
                reader.PollHotkeys(true);                 // 与 PlayerModule.Tick 同一调用点
                input.Release(GameKeyAlias.SkillSlotKey(slot));
            }
            input.BeginFrame();

            var ordered = received.Count == GameKeyAlias.SkillSlotCount;
            for (var i = 0; ordered && i < received.Count; i++) ordered = received[i] == i + 1;
            Check("F1~F8 各发一次 SkillSlotAssignRequest，载荷 = 槽号 1..8（顺序）",
                ordered, received.Count > 0 ? string.Join(",", received) : "(没发)");

            // ── ③ 不按时不发（不是逐帧刷）──
            var n0 = received.Count;
            reader.PollHotkeys(true);
            reader.PollHotkeys(true);
            Check("没按 F 键 ⇒ 不发（每帧调用也不刷）", received.Count == n0, $"仍为 {received.Count}");

            // ── ④ 不存活时（canUse=false）不发 ──
            input.BeginFrame();
            input.Press(GameKeyAlias.KeySkillSlot1);
            reader.PollHotkeys(false);
            input.Release(GameKeyAlias.KeySkillSlot1);
            input.BeginFrame();
            Check("死亡/暂停（canUse=false）时 F 键不发（与腰带键同一条闸门）", received.Count == n0,
                $"仍为 {received.Count}");

            Console.WriteLine("    [说明] 本宿主未编入 Module/Skill ⇒ 只判「读键 → 发意图」；" +
                              "槽号→技能 id 的解析与存档镜像见 combatcheck §19。");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 21. R5：Alt 常显 / 悬停单件地面物品名牌
        // ═════════════════════════════════════════════════════════════════════
        private static void Step21_GroundItemNames(AppContext ctx, object mapObj, PlayerModule player,
            ScriptedInput input, RecordingEventBus bus)
        {
            Section("21. ★ R5：地面物品名牌（Alt 常显 / 悬停单件）⇒ Events.GroundItemLabelsChanged");
            var map = (Diablo2.Module.Map.MapModule)mapObj;
            map.Generate(AreaId.Town, 20250923);
            player.CreateNew(PlayerClass.Amazon, "GroundNames");
            player.TeleportTo(map.SpawnPoint);
            player.Stop();

            var reader = player.Input;
            reader.Reset();
            input.Available = true;
            input.BeginFrame();

            var cells = WalkableCells(map, 2);
            Check("找到 ≥2 个可走格供名牌用例", cells.Count >= 2, $"找到 {cells.Count} 个");
            if (cells.Count < 2)
            {
                reader.Hover.GroundItemAt = null;
                return;
            }
            var itemGrid = cells[0];
            var emptyGrid = cells[1];
            const int groundItemId = GameConst.GroundItemIdBase + 9;

            reader.Hover.GroundItemAt = g => g == itemGrid
                ? (HoverHit?)new HoverHit { id = groundItemId, name = "短剑(名牌自检)" }
                : null;
            reader.Hover.ItemQualityOf = id => id == groundItemId ? ItemQuality.Magic : ItemQuality.Normal;

            GroundItemLabelsArgs last = null;
            var fired = 0;
            bus.On<GroundItemLabelsArgs>(Events.GroundItemLabelsChanged,
                a => { last = a; fired++; });

            // ── ① 悬停地面物品（光标 = Pickup）⇒ 单件名牌 ──
            reader.OverrideHoverGrid(itemGrid);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            Check("悬停地面物品 ⇒ 名牌 1 条，id/名字/格 = 悬停目标，altHeld=false",
                last != null && !last.altHeld && last.labels.Count == 1
                && last.labels[0].id == groundItemId && last.labels[0].name == "短剑(名牌自检)"
                && last.labels[0].gridX == itemGrid.x && last.labels[0].gridY == itemGrid.y,
                last == null ? "(没发事件)" : $"altHeld={last.altHeld} 条数={last.labels.Count}" +
                    (last.labels.Count > 0 ? $" 首={last.labels[0].name}@({last.labels[0].gridX},{last.labels[0].gridY})" : ""));
            Check("名牌品质来自 HoverPicker.ItemQualityOf（本用例注入 Magic ⇒ 配色蓝）",
                last != null && last.labels.Count == 1 && last.labels[0].quality == ItemQuality.Magic,
                last != null && last.labels.Count == 1 ? last.labels[0].quality.ToString() : "(无)");

            // ── ② 按住 Alt ⇒ 常显**全部**地面物品（消费 InputReader.ShowGroundItems）──
            reader.Hover.AllLabels = () => new List<GroundItemLabel>
            {
                new GroundItemLabel { id = groundItemId, name = "短剑(名牌自检)", quality = ItemQuality.Magic,
                    gridX = itemGrid.x, gridY = itemGrid.y },
                new GroundItemLabel { id = groundItemId + 1, name = "皮靴(名牌自检)", quality = ItemQuality.Normal,
                    gridX = emptyGrid.x, gridY = emptyGrid.y },
            };
            input.BeginFrame();
            input.Press(GameKeyAlias.KeyShowGroundItems);       // 原版 Alt
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            Check("按住 Alt ⇒ altHeld=true 且名牌 = 全部地面物品（2 条）",
                last != null && last.altHeld && last.labels.Count == 2,
                last == null ? "(没发事件)" : $"altHeld={last.altHeld} 条数={last.labels.Count}");

            // ── ③ 松开 Alt ⇒ 回到「只显示悬停那件」──
            input.BeginFrame();
            input.Release(GameKeyAlias.KeyShowGroundItems);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            Check("松开 Alt ⇒ 回到悬停单件（altHeld=false / 1 条）",
                last != null && !last.altHeld && last.labels.Count == 1,
                last == null ? "(没发事件)" : $"altHeld={last.altHeld} 条数={last.labels.Count}");

            // ── ④ 内容不变 ⇒ 不重发（本方法每帧被调，防刷屏/防每帧重建节点）──
            var firedBefore = fired;
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            Check("名牌内容不变 ⇒ 不重发（每帧调用但事件只在变化时发）", fired == firedBefore,
                $"fired={fired}（变化前 {firedBefore}）");

            // ── ⑤ 悬停离开（悬停目标消失）⇒ 发空名牌，UI 侧据此清空 ──
            reader.OverrideHoverGrid(emptyGrid);
            reader.UpdateHover(true);
            // 与 PlayerModule.Tick ②b 同口径：Alt（原版常显）由**调用方**读 `ShowGroundItems` 传进来
            reader.PublishGroundItemLabels(reader.ShowGroundItems);
            Check("悬停离开地面物品 ⇒ 发空名牌（UI 名牌层清空；非预期分支已留日志）",
                last != null && last.labels.Count == 0 && fired > firedBefore,
                $"条数={last?.labels.Count} fired={fired}");
            Check("留下了 [GroundItemLabel] 日志（节点名/条数的锚点）",
                CaptureLogger.Has("[GroundItemLabel]"), CaptureLogger.Last("[GroundItemLabel]"));

            // 收尾：撤掉注入，别影响后面的用例
            reader.Hover.AllLabels = null;
            reader.Hover.GroundItemAt = null;
            reader.Hover.ItemQualityOf = null;
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

        /// <summary>
        /// 登记一条**不适用**的判据（判据本身按设计退役 / 输入资产不在仓库里）：
        /// 不占通过数、也不占失败数，只如实打一行 —— ⛔ 不用它把红项"变绿"。
        /// </summary>
        private static void NotApplicable(string what, string detail)
        {
            _na++;
            Console.WriteLine($"    [ NA ] {what}   ({detail})");
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
