// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · tools/probes/hosts/npccheck/Program.cs
// **NPC 模块行为专测宿主**（非 Unity 工程、不参与打包；离线跑，秒级）
//
// 为什么需要它：
//   「宿主 × 模块」矩阵里 `Diablo2.Module.Npc`（NpcModule 891 行 + NpcShop 236 行 +
//   NpcDialog 107 行 = 1234 行）**没有任何行为专测** —— itemcheck 链入了 Npc 源码，
//   但只断言「NpcModule 已装配」与「对话文本非空」两条；uicheck 只读源码做锚点检查。
//   而 NPC 是玩家进图后接触的第一批交互（点击 NPC → 走过去说话 / 对话选项 / 商店买卖 /
//   修理），全部是确定性逻辑 ⇒ 本宿主把这条链的行为面补上。
//
// 口径（与 itemcheck 一致）：**真 NpcModule + 真 ItemModule**（只桩地图/玩家；引擎替身见 shim/）。
//   · 只走**契约入口**（INpcModule / IItemModule）+ **事件**（Game.Event）驱动，
//     ⛔ 不镜像内部公式、不直接调 private；唯一例外 `NpcDialog.TextOf` 是 internal 纯函数，
//     用来对"画面上真的会显示的文本"下断言 —— 它就是要被断言的产物本身。
//   · 每条易永真的判据配「错样本喂进同一判据必须变红」的自检。
//
// 覆盖清单（§ 号与输出一致）：
//   §1 装配：城镇门禁 / 5 站位逐一对上 NpcPoints / 旗标（商店·修理·铁匠）
//   §2 非城镇：不装配（无 (0,0) 幽灵 NPC）、FindNearest/Interact/GetDialog 全拒绝
//   §3 站位缺失：只装存在的，⛔ 不用 (0,0) 兜底；警告点名
//   §4 对话文本与选项：原版 5 句互异 / 下标 0 恒「離開」/ 铁匠「交易/修理」vs 商人「交易」
//   §5 Interact / ChooseOption：DialogOpen / DialogClose / ShopOpen / 越界与无商店忽略
//   §6 商店：无商店 null / 快照 / 同 seed 货物确定性 / 限量 vs 无限
//   §7 买入：成功 / 限量售完 / 钱不够 / **满包整笔取消 + 回滚**
//   §8 卖出：成功 + 价 = SellPriceOf / 任务物品拒绝 / 空锚点拒绝
//   §9 修理：只有恰西（canRepair）/ 修好 + 扣费 / 全修
//   §10 点击→走位→说话：范围外先走位、到位自动开口、改目标即作废、范围内当场开口
//   §11 范围自动关：走出 TalkRange 自动关对话（开着商店连商店一起关，且先于 DialogClose）
//   §12 交易事件路由：ShopBuyRequest 正常转发 / npcId=None 拒绝（⛔ 不兜底阿卡拉）
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Dir8 = CloverEngine.Dir8;
using Diablo2.Module;
using Diablo2.Module.Npc;      // ★ NpcModule / NpcShop / NpcDialog（internal —— 宿主直接编入源码，同程序集可见）
using Diablo2.Module.Save;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;
// 别名：`ILogger` 在 CloverEngine 与 UnityEngine 里同名（宿主同时 using 两者会 CS0104）
using ILogger = CloverEngine.ILogger;

namespace NpcCheck
{
    internal sealed class RecLogger : ILogger
    {
        public readonly List<string> Lines = new List<string>();

        public void Info(string tag, string msg) { Add("INFO ", tag, msg); }
        public void Warn(string tag, string msg) { Add("WARN ", tag, msg); }
        public void Error(string tag, string msg, Exception ex = null) { Add("ERROR", tag, msg); }
        public void Debug(string tag, string msg) { }
        public void Fatal(string tag, string msg, Exception ex = null) { Add("FATAL", tag, msg); }

        private void Add(string level, string tag, string msg)
        {
            var line = $"[{level}] [{tag}] {msg}";
            Lines.Add(line);
            if (level != "INFO " || Program.Verbose) Console.WriteLine("   " + line);
        }

        public bool Contains(string tag, string sub)
        {
            for (var i = Lines.Count - 1; i >= 0; i--)
            {
                if (Lines[i].IndexOf("[" + tag + "]", StringComparison.Ordinal) < 0) continue;
                if (Lines[i].IndexOf(sub, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        public int CountOf(string tag, string sub)
        {
            var n = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf("[" + tag + "]", StringComparison.Ordinal) < 0) continue;
                if (Lines[i].IndexOf(sub, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 设置容器（`Game.Setting` 替身）+ 可窥视原始字符串（存档自证要读它）
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class MemSetting : ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public int SaveCount;

        public T Get<T>(string key, T defaultValue = default)
        {
            object v;
            return _d.TryGetValue(key, out v) && v is T ? (T)v : defaultValue;
        }

        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { SaveCount++; }
        public void Load() { }
        public void Delete(string key) { _d.Remove(key); }
        public void DeleteAll() { _d.Clear(); }

        /// <summary>取原始存值（存档往返自证用）。</summary>
        public string Peek(string key)
        {
            object v;
            return _d.TryGetValue(key, out v) ? v as string : null;
        }

        public bool HasKey(string key) { return _d.ContainsKey(key); }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 桩：地图（只用站位/区域/seed；可走性规则与真地图一致：越界不可走）
    // ═════════════════════════════════════════════════════════════════════════
    // 桩：地图（只用站位/区域/seed；可走性规则与真地图一致：越界不可走）
    //   ★ 本宿主把 5 个 NPC 点位摆**远隔**（两两间距 ≥10 > TalkRange 2.4）——
    //     "点击 → 走过去 → 到位才说话"这条链才有可测的位移；itemcheck 版点位间距=2
    //     落在 TalkRange 内，测不出走位分支。
    //   ★ `NpcGrids` 可改（§3 站位缺失要换成 3 个点位）；
    //     注意：**只改点位内容不触发重建** —— `NpcModule.EnsureBuilt` 的缓存键是
    //     (generated, seed, area) ⇒ 换点位后必须同时改 `Seed`（地图重建的代理事件）。
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class StubMap : IMapModule
    {
        public AreaId Area { get; set; } = AreaId.Town;
        public int Seed { get; set; } = 20261013;
        public int Width => 64;
        public int Height => 64;
        public int BlockedCount => 0;
        public int WalkableCount => Width * Height;
        public bool IsGenerated => true;
        public Vector2Int SpawnPoint => new Vector2Int(8, 8);
        public IReadOnlyList<Vector2Int> Exits => new List<Vector2Int> { new Vector2Int(2, 32) };
        public IReadOnlyList<Vector2Int> MonsterSpawns => new List<Vector2Int>();

        public IReadOnlyList<Vector2Int> WaypointPoints => new List<Vector2Int>();

        public IReadOnlyCollection<Vector2Int> ExploredCells => new List<Vector2Int>();

        public List<Vector2Int> NpcGrids = new List<Vector2Int>
        {
            new Vector2Int(10, 10), new Vector2Int(20, 20), new Vector2Int(30, 30),
            new Vector2Int(40, 40), new Vector2Int(50, 50),
        };
        public IReadOnlyList<Vector2Int> NpcPoints => NpcGrids;

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < Width && g.y < Height;
        public bool Walkable(Vector2Int g) => InBounds(g);

        /// <summary>桩地图没有"可走上方的结构"（桥面/平台）⇒ 恒 false。
        /// 契约成员见 `Module/Contracts.cs` 的 `IMapModule.IsDeckGrid`。</summary>
        public bool IsDeckGrid(Vector2Int g) => false;
        public TileKind TileAt(Vector2Int g) => Walkable(g) ? TileKind.Grass : TileKind.Void;
        public void Generate(AreaId area, int seed) { Area = area; Seed = seed; }
        public void Clear() { }
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to) { return new List<Vector2Int> { from, to }; }
        public Vector2Int RandomWalkableTile(Rng rng) { return new Vector2Int(rng.Next(1, Width - 1), rng.Next(1, Height - 1)); }
        public void ShowArea(AreaId area) { Area = area; }
        public MinimapArgs BuildMinimap() { return new MinimapArgs { areaId = (int)Area, width = Width, height = Height, seed = Seed }; }
    }
    internal sealed class StubPlayer : IPlayerModule
    {
        public readonly RecLogger Log;
        private readonly List<ItemStack> _equip = new List<ItemStack>();

        private int _baseStr = 60, _baseDex = 30, _baseVit = 30, _baseEng = 15, _baseDef = 5;
        private int _life = 20, _mana = 20, _stamina = 80;
        private int _maxLife = 200, _maxMana = 60, _maxStamina = 100;

        public StubPlayer(RecLogger log)
        {
            Log = log;
            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnEquipChanged);
        }

        private void OnEquipChanged(InventoryChangedArgs a)
        {
            _equip.Clear();
            if (a != null && a.equip != null) _equip.AddRange(a.equip);
        }

        private int ModSum(string mod)
        {
            var sum = 0;
            for (var i = 0; i < _equip.Count; i++)
            {
                var it = _equip[i];
                if (it == null || it.affixes == null) continue;
                for (var k = 0; k < it.affixes.Count; k++)
                {
                    var af = it.affixes[k];
                    if (af == null || af.mod != mod) continue;
                    sum += af.value;
                }
            }
            return sum;
        }

        public int EquippedDefense
        {
            get
            {
                var sum = 0;
                for (var i = 0; i < _equip.Count; i++)
                {
                    var it = _equip[i];
                    if (it == null) continue;
                    sum += (it.defMin + it.defMax) / 2;
                }
                return sum;
            }
        }

        /// <summary>估算近战伤害（接口没有伤害字段 ⇒ 由宿主从装备载荷算出来展示"伤害变化"）。</summary>
        public string DamageText
        {
            get
            {
                var lo = 1;
                var hi = 2;
                for (var i = 0; i < _equip.Count; i++)
                {
                    var it = _equip[i];
                    if (it == null || it.type != ItemType.Weapon) continue;
                    lo += it.dmgMin;
                    hi += it.dmgMax;
                }
                var pct = ModSum("dmg%");
                lo = lo * (100 + pct) / 100;
                hi = hi * (100 + pct) / 100;
                return $"{lo}-{hi}";
            }
        }

        public PlayerClass Class => PlayerClass.Barbarian;
        public string Name { get; set; } = "CheckHero";
        public int Level { get; set; } = 5;
        public int Str => _baseStr + ModSum("str");
        public int Dex => _baseDex + ModSum("dex");
        public int Vit => _baseVit;
        public int Eng => _baseEng + ModSum("enr");
        public int Life => _life;
        public int MaxLife => _maxLife + ModSum("hp");
        public int Mana => _mana;
        public int MaxMana => _maxMana + ModSum("mana");
        public int Stamina => _stamina;
        public int MaxStamina => _maxStamina;
        public long Exp { get; private set; }
        public long ExpNext { get; private set; } = 1000;
        public int StatPoints { get; private set; } = 15;
        public int SkillPoints { get; private set; }
        public int Gold { get; private set; }
        public Vector2Int Grid { get; private set; } = new Vector2Int(10, 10);
        public Vector3 World => new Vector3(Grid.x, Grid.y, 0);
        public Dir8 Dir => Dir8.S;
        public bool IsMoving => false;

        /// <summary>
        /// 本宿主不测表现层 ⇒ 固定 `true`（= 原版默认跑，与 `PlayerModule._running` 的默认值一致）。
        /// </summary>
        public bool IsRunning => true;

        public bool IsDead => _life <= 0;
        public int Defense => _baseDef + EquippedDefense + ModSum("ac");
        public int AttackRating => 20 + Dex * 4 + ModSum("att") + ModSum("att-demon") + ModSum("att-undead");

        public int GetResist(DamageType type) => 0;

        public PlayerStatsDto Snapshot()
        {
            return new PlayerStatsDto
            {
                name = Name, cls = Class, level = Level, exp = Exp, expNext = ExpNext,
                str = Str, dex = Dex, vit = Vit, eng = Eng,
                life = Life, maxLife = MaxLife, mana = Mana, maxMana = MaxMana,
                stamina = Stamina, maxStamina = MaxStamina,
                defense = Defense, attackRating = AttackRating,
                statPoints = StatPoints, skillPoints = SkillPoints, gold = Gold,
            };
        }

        public void CreateNew(PlayerClass cls, string name) { Name = name; Level = 1; }
        public void MoveTo(Vector2Int target) { Grid = target; }
        public void Stop() { }
        public void TeleportTo(Vector2Int grid) { Grid = grid; }
        public void FaceTo(Vector2Int grid) { }   // 本宿主不测朝向
        public void Tick(float dt) { }
        public bool ApplyDamage(int amount, DamageType type) { _life = Mathf.Max(0, _life - amount); return IsDead; }
        public void Heal(int amount) { _life = Mathf.Min(MaxLife, _life + amount); }
        public void RestoreMana(int amount) { _mana = Mathf.Min(MaxMana, _mana + amount); }

        /// <summary>w7 契约新增（`IPlayerModule.TrySpendMana`）的桩：成功扣减 true；≤0 或不足 false 且不扣。</summary>
        public bool TrySpendMana(int amount)
        {
            if (amount <= 0 || _mana < amount) return false;
            _mana -= amount;
            return true;
        }

        public void RestoreStamina(int amount) { _stamina = Mathf.Min(MaxStamina, _stamina + amount); }
        public void AddExp(int amount) { Exp += amount; }
        public void AddSkillPoint(int delta) { SkillPoints += delta; }
        public bool AllocateStat(StatKind kind, int delta) { return false; }
        public void Revive() { _life = MaxLife; }
        public void Kill() { _life = 0; }

        public bool AddGold(int amount)
        {
            if (amount < 0 && Gold + amount < 0) return false;
            Gold += amount;
            return true;
        }

        /// <summary>测试用：直接给金币（避免用全量 API 拼状态）。</summary>
        public void SetGoldForTest(int v) { Gold = v; }
        public void SetLifeForTest(int v) { _life = v; }

        public void LoadFrom(CharacterSave save)
        {
            Name = save.name;
            Level = save.level;
            _baseStr = save.str; _baseDex = save.dex; _baseVit = save.vit; _baseEng = save.eng;
            _life = save.life; _mana = save.mana; _stamina = save.stamina;
            StatPoints = save.statPoints; SkillPoints = save.skillPoints; Gold = save.gold;
            Grid = new Vector2Int(save.gridX, save.gridY);
            Exp = save.exp;
        }

        public void WriteTo(CharacterSave save)
        {
            save.name = Name;
            save.cls = Class;
            save.level = Level;
            save.exp = Exp;
            save.str = _baseStr; save.dex = _baseDex; save.vit = _baseVit; save.eng = _baseEng;
            save.life = Life; save.mana = Mana; save.stamina = Stamina;
            save.statPoints = StatPoints; save.skillPoints = SkillPoints; save.gold = Gold;
            save.areaId = (int)AreaId.Town;
            save.gridX = Grid.x; save.gridY = Grid.y;
        }

        public void Reset() { _equip.Clear(); }
    }
    // ═════════════════════════════════════════════════════════════════════════
    // 自检主流程
    // ═════════════════════════════════════════════════════════════════════════
    public static class Program
    {
        public static bool Verbose;

        // 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        private static readonly string ClientDataPath = ResolveProjectRoot() + @"\client\Assets";

        /// <summary>
        /// 从宿主自己的可执行目录向上找「含 client/Assets 的那一层」= 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/（与 itemcheck / savecheck 同一套写法）。
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

        /// <summary>本宿主的槽位档沙盒（每次清空；Save 模块经 Game.Setting 落盘时不许污染检出）。</summary>
        private static string HostSandboxSettingDir(string host)
        {
            var p = System.IO.Path.Combine(ResolveProjectRoot(), ".ai-tmp", "test", "host-setting", host);
            try
            {
                if (System.IO.Directory.Exists(p)) System.IO.Directory.Delete(p, true);
                System.IO.Directory.CreateDirectory(p);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[warn] 沙盒目录不可用（{p}）：{ex.GetType().Name}: {ex.Message}");
            }
            return p;
        }

        private const float Dt = 0.02f;          // 50 FPS（与验收口径一致）

        /// <summary>原版 5 个 NPC 的名字（`NpcModule.Names` 的**实现值**；就是画面上显示的那份）。</summary>
        private static readonly string[] NpcNames = { "阿卡拉", "卡夏", "恰西", "基得", "瓦瑞夫" };

        private static RecLogger _log;
        private static MemSetting _setting;
        private static StubMap _map;
        private static StubPlayer _player;
        private static AppContext _ctx;
        private static int _ok;
        private static int _fail;

        // ── 事件录制（断言"模块确实发了什么"）──────────────────────────────
        private static readonly List<string> _ev = new List<string>();
        private static NpcDialogArgs _lastDialogOpen;
        private static ShopOpenArgs _lastShopOpen;

        private static void Rec(string name, string payload) { _ev.Add(name + "|" + payload); }
        private static void ClearEv() { _ev.Clear(); }

        private static int CountEv(string name)
        {
            var n = 0;
            for (var i = 0; i < _ev.Count; i++)
                if (_ev[i].StartsWith(name + "|", StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>第一次出现的下标（-1 = 没出现过）。用于断言两个事件的**先后顺序**。</summary>
        private static int IndexEv(string name)
        {
            for (var i = 0; i < _ev.Count; i++)
                if (_ev[i].StartsWith(name + "|", StringComparison.Ordinal)) return i;
            return -1;
        }

        private static string LastEvPayload(string name)
        {
            for (var i = _ev.Count - 1; i >= 0; i--)
                if (_ev[i].StartsWith(name + "|", StringComparison.Ordinal))
                    return _ev[i].Substring(name.Length + 1);
            return null;
        }

        // ── 背包辅助 ───────────────────────────────────────────────────────
        private static int CountAnchors()
        {
            var n = 0;
            var inv = _ctx.Item.Inventory;
            for (var i = 0; i < inv.Count; i++)
                if (inv[i] != null && inv[i].isAnchor && inv[i].item != null) n++;
            return n;
        }

        /// <summary>背包锚点签名（"格号:itemId x 数量;"串联）——买卖前后比对"一个格子都没动"用。</summary>
        private static string InvSig()
        {
            var sb = new StringBuilder();
            var inv = _ctx.Item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                sb.Append(i).Append(':').Append(s.item.itemId).Append('x').Append(s.item.count).Append(';');
            }
            return sb.ToString();
        }

        private static int FirstAnchorOf(Func<ItemStack, bool> pred)
        {
            var inv = _ctx.Item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                if (pred(s.item)) return i;
            }
            return -1;
        }

        private static ItemStack AnchorItemAt(int index)
        {
            var inv = _ctx.Item.Inventory;
            if (index < 0 || index >= inv.Count) return null;
            var s = inv[index];
            return s != null && s.isAnchor ? s.item : null;
        }

        /// <summary>找阿卡拉/杂货店货架里第一个「1×1 且无限量」的商品下标（-1 = 没有）。</summary>
        private static int FirstUnlimited1x1(ShopOpenArgs shop)
        {
            if (shop == null || shop.stock == null) return -1;
            for (var i = 0; i < shop.stock.Count; i++)
            {
                var e = shop.stock[i];
                if (e.count == -1 && e.gridW == 1 && e.gridH == 1) return i;
            }
            return -1;
        }

        // ── 断言 / 段落 ────────────────────────────────────────────────────
        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) _ok++; else _fail++;
            Console.WriteLine((ok ? "[ OK ] " : "[FAIL] ") + name + (string.IsNullOrEmpty(detail) ? "" : "   (" + detail + ")"));
        }

        private static void Section(string t)
        {
            Console.WriteLine();
            Console.WriteLine("── " + t + " ──");
        }

        // ═════════════════════════════════════════════════════════════════════
        public static int Main(string[] args)
        {
            Verbose = args != null && Array.IndexOf(args, "-v") >= 0;

            Console.WriteLine("=== NpcCheck：NPC 模块（对话 / 商店 / 修理 / 走位意图）离线自检 ===");

            // ── 0. 引擎替身 + 配表 ──────────────────────────────────────────
            _log = new RecLogger();
            _setting = new MemSetting();
            Game.Logger = _log;
            Game.Event = new ConsoleEventBus();
            Game.Setting = _setting;
            Game.IsRunning = true;
            Game.Config = new GameConfig { SettingDir = HostSandboxSettingDir("npccheck") };

            // 事件录制订阅（挂在总线上的"独立监听者"，与模块自己的订阅并行、互不干扰）
            Game.Event.On<NpcDialogArgs>(Events.DialogOpen, a =>
            {
                _lastDialogOpen = a;
                Rec("DialogOpen", a == null ? "null" : a.npcId.ToString());
            });
            Game.Event.On(Events.DialogClose, () => Rec("DialogClose", ""));
            Game.Event.On<ShopOpenArgs>(Events.ShopOpen, a =>
            {
                _lastShopOpen = a;
                Rec("ShopOpen", a == null ? "null" : a.npcId.ToString());
            });
            Game.Event.On<ShopOpenArgs>(Events.ShopChanged, a => Rec("ShopChanged", ""));
            Game.Event.On(Events.ShopClose, () => Rec("ShopClose", ""));
            Game.Event.On<Vector2Int>(Events.MoveCommand, g => Rec("MoveCommand", g.x + "," + g.y));
            Game.Event.On(Events.InventoryFull, () => Rec("InventoryFull", ""));

            var err = Table.TableLoader.LoadAll(null, ClientDataPath);
            Check("§0 配表已加载（TableLoader，与 Bootstrap 同链路）", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));
            Check("§0b item_c 行数 > 100（商店造货的前提）", Table.Tables.Default.Item.Count > 100,
                "item_c=" + Table.Tables.Default.Item.Count);

            // ── 1. 装配：真 Item + 真 Npc ────────────────────────────────────
            Section("§1 装配：城镇 5 站位（取自 IMapModule.NpcPoints，⛔ 无硬编码）");
            _map = new StubMap();
            _player = new StubPlayer(_log);
            _ctx = AppContext.Create();
            _ctx.Map = _map;
            _ctx.Player = _player;
            _ctx.AutoWire();                       // 只填 null 字段 ⇒ 装上 Item / Npc / Save
            Check("§1-0a ItemModule 已自动装配（真件）",
                _ctx.Item != null && _ctx.Item.GetType().Name == "ItemModule",
                _ctx.Item == null ? "null" : _ctx.Item.GetType().FullName);
            Check("§1-0b NpcModule 已自动装配（真件）",
                _ctx.Npc != null && _ctx.Npc.GetType().Name == "NpcModule",
                _ctx.Npc == null ? "null" : _ctx.Npc.GetType().FullName);

            _ctx.Npc.Reset();
            var all = _ctx.Npc.All;
            Check("§1-1 城镇 + 5 点位 ⇒ 装配 5 个 NPC", all.Count == 5, "count=" + all.Count);

            var coordsOk = all.Count == 5;
            var namesOk = all.Count == 5;
            var flagsOk = all.Count == 5;
            // 旗标判据（单点定义、复用；后面自检要把它喂错样本）
            Func<NpcDef, bool> flagOk = d => d != null
                && d.hasShop == (d.id == (int)NpcId.Akara || d.id == (int)NpcId.Charsi || d.id == (int)NpcId.Gheed)
                && d.canRepair == (d.id == (int)NpcId.Charsi)
                && d.isBlacksmith == (d.id == (int)NpcId.Charsi);
            for (var i = 0; i < all.Count; i++)
            {
                if (i < _map.NpcGrids.Count &&
                    (all[i].gridX != _map.NpcGrids[i].x || all[i].gridY != _map.NpcGrids[i].y)) coordsOk = false;
                if (i < NpcNames.Length && all[i].name != NpcNames[i]) namesOk = false;
                if (!flagOk(all[i])) flagsOk = false;
            }
            Check("§1-2 站位 = NpcPoints 逐一对上（顺序搬运，不硬编码）", coordsOk,
                string.Join(" / ", all.Select(d => d.id + "@(" + d.gridX + "," + d.gridY + ")")));
            Check("§1-3 名字 = 原版 5 NPC（阿卡拉/卡夏/恰西/基得/瓦瑞夫）", namesOk,
                string.Join("、", all.Select(d => d.name)));
            Check("§1-4 旗标：商店 = 阿卡拉/恰西/基得；修理 + 铁匠 = 只有恰西", flagsOk,
                string.Join(" / ", all.Select(d => d.name + ":shop=" + d.hasShop + ",repair=" + d.canRepair)));
            // 自检：把「卡夏 canRepair=true」错样本喂进**同一判据** ⇒ 必须判红（防判据永真）
            var badFlagSample = new NpcDef { id = (int)NpcId.Kashya, name = "卡夏", hasShop = false, canRepair = true, isBlacksmith = false };
            Check("§1-5 自检：同一判据喂「卡夏 canRepair=true」错样本 ⇒ 必须判红",
                !flagOk(badFlagSample),
                "错样本 ⇒ 判据 = " + (flagOk(badFlagSample) ? "通过（❌ 判据永真）" : "红 ✓"));

            // ── 2. 非城镇门禁 ────────────────────────────────────────────────
            Section("§2 非城镇门禁：不装配、不交互（防「洞里也能跟阿卡拉说话」）");
            _map.Area = AreaId.BloodMoor;
            _map.Seed = 20261014;                  // seed 变化 ⇒ EnsureBuilt 缓存失效重建
            _ctx.Npc.Reset();
            var allWild = _ctx.Npc.All;
            Check("§2-1 非城镇 ⇒ 装配 0 个（无 (0,0) 幽灵 NPC）", allWild.Count == 0, "count=" + allWild.Count);
            var nearWild = _ctx.Npc.FindNearest(new Vector2Int(0, 0));
            Check("§2-2 非城镇 FindNearest((0,0)) ⇒ null（原点不再命中阿卡拉）", nearWild == null,
                nearWild == null ? "null ✓" : "命中了 " + nearWild.name);
            var interWild = _ctx.Npc.Interact(0);
            Check("§2-3 非城镇 Interact(0) ⇒ false（拒绝交互）", !interWild, "ret=" + interWild);
            var dlgWild = _ctx.Npc.GetDialog(0);
            Check("§2-4 非城镇 GetDialog(0) ⇒ null", dlgWild == null, dlgWild == null ? "null ✓" : dlgWild.text);
            Check("§2-5 日志点名「不是罗格营地」（FindNearest 的 Info + Interact 的 Warn）",
                _log.Contains("Npc", "不是罗格营地"), "命中 " + _log.CountOf("Npc", "不是罗格营地") + " 行");
            // 自检：把「非城镇应当装配 0」的判据喂「装配了 5 个」错样本 ⇒ 必须判红
            Func<int, bool> emptyInWild = c => c == 0;
            Check("§2-6 自检：同一判据喂「非城镇却装了 5 个」错样本 ⇒ 必须判红",
                !emptyInWild(5), "错样本 ⇒ 判据 = " + (emptyInWild(5) ? "通过（❌ 判据永真）" : "红 ✓"));

            // ── 3. 站位缺失：只装存在的 ──────────────────────────────────────
            Section("§3 站位缺失：只装存在的（⛔ 不用 (0,0) 兜底）");
            _map.Area = AreaId.Town;
            _map.NpcGrids.Clear();
            _map.NpcGrids.Add(new Vector2Int(10, 10));
            _map.NpcGrids.Add(new Vector2Int(20, 20));
            _map.NpcGrids.Add(new Vector2Int(30, 30));
            _map.Seed = 20261015;                  // 换点位必须同时换 seed 才触发重建（缓存键见 StubMap 注释）
            _ctx.Npc.Reset();
            var all3 = _ctx.Npc.All;
            var noGhost = true;
            for (var i = 0; i < all3.Count; i++) if (all3[i].gridX == 0 && all3[i].gridY == 0) noGhost = false;
            Check("§3-1 3 个点位 ⇒ 装配 3 个（其余跳过、不补 (0,0)）", all3.Count == 3 && noGhost,
                "count=" + all3.Count + " noGhost=" + noGhost);
            Check("§3-2 日志「NPC 站位缺失」+ 点名（基得、瓦瑞夫）",
                _log.Contains("Npc", "站位缺失") && _log.Contains("Npc", "基得") && _log.Contains("Npc", "瓦瑞夫"), "");
            // 恢复完整城镇（后续段用）
            _map.NpcGrids.Clear();
            _map.NpcGrids.Add(new Vector2Int(10, 10));
            _map.NpcGrids.Add(new Vector2Int(20, 20));
            _map.NpcGrids.Add(new Vector2Int(30, 30));
            _map.NpcGrids.Add(new Vector2Int(40, 40));
            _map.NpcGrids.Add(new Vector2Int(50, 50));
            _map.Seed = 20261013;
            _ctx.Npc.Reset();
            Check("§3-3 恢复 5 点位 ⇒ 重新装配 5 个", _ctx.Npc.All.Count == 5, "count=" + _ctx.Npc.All.Count);

            // ── 4. 对话文本与选项 ────────────────────────────────────────────
            Section("§4 对话文本与选项（原版串）");
            var texts = new string[5];
            for (var i = 0; i < 5; i++) texts[i] = NpcDialog.TextOf(i);
            Check("§4-1 5 个 NPC 对话文本全非空", texts.All(s => !string.IsNullOrWhiteSpace(s)),
                "长度=" + string.Join(",", texts.Select(s => s.Length)));
            Check("§4-2 5 句互不相同（不是复制粘贴同一句）", texts.Distinct().Count() == 5,
                "distinct=" + texts.Distinct().Count());
            Check("§4-3 未知 id ⇒ 空串（不编文案）", NpcDialog.TextOf(99) == "" && NpcDialog.TextOf(-1) == "",
                "len=" + NpcDialog.TextOf(99).Length);

            var dlgA = _ctx.Npc.GetDialog((int)NpcId.Akara);
            var dlgC = _ctx.Npc.GetDialog((int)NpcId.Charsi);
            var dlgK = _ctx.Npc.GetDialog((int)NpcId.Kashya);
            var dlgG = _ctx.Npc.GetDialog((int)NpcId.Gheed);
            Check("§4-4 下标 0 恒为「離開」（原版串 3394）",
                dlgA.options.Count > 0 && dlgA.options[0] == "離開" &&
                dlgC.options[0] == "離開" && dlgK.options[0] == "離開" && dlgG.options[0] == "離開", "");
            Check("§4-5 铁匠恰西的商店入口 = 「交易/修理」（串 3334）",
                dlgC.options.Count == 2 && dlgC.options[1] == "交易/修理",
                "options=" + string.Join("|", dlgC.options));
            Check("§4-6 阿卡拉 = 「交易」（串 3396）；非商人卡夏只有 1 项（离开）",
                dlgA.options.Count == 2 && dlgA.options[1] == "交易" && dlgK.options.Count == 1,
                "阿卡拉=" + string.Join("|", dlgA.options) + " 卡夏=" + string.Join("|", dlgK.options));

            // ── 5. Interact / ChooseOption 事件 ──────────────────────────────
            Section("§5 Interact / ChooseOption 事件");
            ClearEv();
            var okInteract = _ctx.Npc.Interact((int)NpcId.Akara);
            Check("§5-1 Interact(阿卡拉) ⇒ true + DialogOpen ×1（载荷 npcId=0）",
                okInteract && CountEv("DialogOpen") == 1 && LastEvPayload("DialogOpen") == "0",
                "ret=" + okInteract + " 事件=" + CountEv("DialogOpen"));
            Check("§5-1b DialogOpen 载荷文本 = 阿卡拉原版串、名字对上",
                _lastDialogOpen != null && _lastDialogOpen.text == NpcDialog.Akara && _lastDialogOpen.npcName == "阿卡拉",
                _lastDialogOpen == null ? "载荷 null" : ("text.长度=" + _lastDialogOpen.text.Length));

            ClearEv();
            _ctx.Npc.ChooseOption((int)NpcId.Akara, 0);
            Check("§5-2 ChooseOption(0 = 离开) ⇒ DialogClose ×1", CountEv("DialogClose") == 1, "事件=" + CountEv("DialogClose"));

            ClearEv();
            _ctx.Npc.ChooseOption((int)NpcId.Charsi, 1);            // 恰西的商店入口
            Check("§5-3 ChooseOption(恰西, 1) ⇒ ShopOpen ×1（载荷 npcId=2 + 货架非空）",
                CountEv("ShopOpen") == 1 && _lastShopOpen != null && _lastShopOpen.npcId == 2 && _lastShopOpen.stock.Count > 0,
                _lastShopOpen == null ? "载荷 null" : ("stock=" + _lastShopOpen.stock.Count));
            Game.Event.Emit(Events.ShopClose);                       // 模拟面板关闭（把模块的 _shopOpen 归零）

            ClearEv();
            _ctx.Npc.ChooseOption((int)NpcId.Akara, 99);            // 越界
            Check("§5-4 ChooseOption(0, 99) 越界 ⇒ 无 ShopOpen + 日志「越界」",
                CountEv("ShopOpen") == 0 && _log.Contains("Npc", "越界"), "");

            ClearEv();
            _ctx.Npc.ChooseOption((int)NpcId.Kashya, 1);            // 卡夏 options 只有 1 项（離開）⇒ idx=1 越界
            // 注：源码里「无对应动作」分支（hasShop=false 且 index 合法）对 5 个真 NPC **不可达**
            //     —— 非商人的 options 只有 1 项，合法下标 0 又先被"关闭"分支吃掉了；那是"防面板伪造"的
            //     防御性分支，宿主无法通过契约入口构造 ⇒ 不测（测不到的东西不许假装测了）。
            Check("§5-5 ChooseOption(卡夏, 1)（无商店 ⇒ 只有 1 项，越界）⇒ 无 ShopOpen + 日志「越界」",
                CountEv("ShopOpen") == 0 && _log.Contains("Npc", "越界"), "");

            // ── 6. 商店快照与确定性 ──────────────────────────────────────────
            Section("§6 商店快照与确定性");
            _ctx.Item.Reset();
            _player.SetGoldForTest(100000);
            _ctx.Npc.Reset();
            var shopK = _ctx.Npc.GetShop((int)NpcId.Kashya);
            Check("§6-1 卡夏（无商店）⇒ null", shopK == null, shopK == null ? "null ✓" : "非 null");

            var shopA = _ctx.Npc.GetShop((int)NpcId.Akara);
            Check("§6-2 阿卡拉 ⇒ 快照非空 + playerGold = Player.Gold",
                shopA != null && shopA.stock.Count > 0 && shopA.playerGold == _player.Gold && shopA.npcName == "阿卡拉",
                shopA == null ? "null" : ("stock=" + shopA.stock.Count + " gold=" + shopA.playerGold));

            var g1 = _ctx.Npc.GetShop((int)NpcId.Gheed);
            var ids1 = string.Join(",", g1.stock.Select(e => e.itemId));
            var g2 = _ctx.Npc.GetShop((int)NpcId.Gheed);
            var ids2 = string.Join(",", g2.stock.Select(e => e.itemId));
            Check("§6-3 同 seed 两次取货 ⇒ itemId 序列完全一致（重开面板不换货）",
                ids1.Length > 0 && ids1 == ids2, "n=" + g1.stock.Count + " ids=" + (ids1.Length > 60 ? ids1.Substring(0, 60) + "…" : ids1));

            var shopC6 = _ctx.Npc.GetShop((int)NpcId.Charsi);
            var allLimited = shopC6 != null && shopC6.stock.Count > 0 && shopC6.stock.All(e => e.count == 1);
            var allUnlimited = shopA.stock.All(e => e.count == -1);
            Check("§6-4 铁匠货 = 限量（每件 1 个）；杂货 = 无限（-1）", allLimited && allUnlimited,
                "恰西=" + (shopC6 == null ? "null" : shopC6.stock.Count + " 件全限量=" + allLimited) + "；阿卡拉全无限=" + allUnlimited);

            // ── 7. 买入 ─────────────────────────────────────────────────────
            Section("§7 买入：成功 / 限量售完 / 钱不够 / 满包整笔取消 + 回滚");
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(100000);
            var s7 = _ctx.Npc.GetShop((int)NpcId.Akara);
            var idxU = FirstUnlimited1x1(s7);
            Check("§7-0 前提：阿卡拉有 1×1 的无限量商品", idxU >= 0, "idx=" + idxU);
            var priceU = idxU >= 0 ? s7.stock[idxU].price : 0;
            var gold0 = _player.Gold;
            var anchors0 = CountAnchors();
            var okBuy1 = _ctx.Npc.Buy((int)NpcId.Akara, idxU, 1);
            Check("§7-1 买 1 件 ⇒ true + 扣钱 = 价 + 背包 +1",
                okBuy1 && _player.Gold == gold0 - priceU && CountAnchors() == anchors0 + 1,
                "ok=" + okBuy1 + " 钱 " + gold0 + "→" + _player.Gold + "（价 " + priceU + "）背包 " + anchors0 + "→" + CountAnchors());

            // 7-2 铁匠限量：第 1 件成功、第 2 件「已售完」
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(100000);
            _ctx.Npc.GetShop((int)NpcId.Charsi);
            var okC1 = _ctx.Npc.Buy((int)NpcId.Charsi, 0, 1);
            var goldAfterC1 = _player.Gold;
            var okC2 = _ctx.Npc.Buy((int)NpcId.Charsi, 0, 1);
            Check("§7-2 限量装备：第 1 件成功、第 2 件拒绝（已售完）+ 钱不变",
                okC1 && !okC2 && _player.Gold == goldAfterC1 && _log.Contains("Npc", "已售完"),
                "1st=" + okC1 + " 2nd=" + okC2 + " 钱=" + _player.Gold);

            // 7-3 钱不够（前置检查：不扣钱、不进货）
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            var s7c = _ctx.Npc.GetShop((int)NpcId.Akara);
            var idxU3 = FirstUnlimited1x1(s7c);
            var priceU3 = idxU3 >= 0 ? s7c.stock[idxU3].price : 0;
            _player.SetGoldForTest(1);                              // 只有 1 块（商品价必然 > 1）
            var anchorsPoor = CountAnchors();
            var okPoor = _ctx.Npc.Buy((int)NpcId.Akara, idxU3, 1);
            Check("§7-3 钱不够 ⇒ false + 金币不变 + 背包不变",
                !okPoor && _player.Gold == 1 && CountAnchors() == anchorsPoor && priceU3 > 1,
                "ok=" + okPoor + " 钱=" + _player.Gold + "（价 " + priceU3 + "）");

            // 7-4 满包：剩 1 格 + 买 2 件 ⇒ 第 1 件进、第 2 件失败 ⇒ **整笔取消 + 回滚**
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(100000);
            var s7d = _ctx.Npc.GetShop((int)NpcId.Akara);
            var idxU4 = FirstUnlimited1x1(s7d);
            var filled = 0;
            for (var i = 0; i < 39; i++)
            {
                if (_ctx.Item.AddToInventory(new ItemStack { itemId = 1, name = "填塞物", gridW = 1, gridH = 1, count = 1 })) filled++;
            }
            Check("§7-4 前提：灌 39/40 格（剩 1 格空）", filled == 39 && CountAnchors() == 39,
                "filled=" + filled + " anchors=" + CountAnchors());
            // 前提2：目标商品必须存在 —— ⛔ 少了这条，若 idxU4 取到 -1（商品清单变了），
            //   Buy(-1) 会走「没有第 -1 号商品」⇒ false + 钱不变 + 签名不变 ⇒ 7-4a/b **双假绿**。
            Check("§7-4 前提2：目标商品存在（防 idx=-1 时 7-4a/b 假绿）", idxU4 >= 0, "idx=" + idxU4);
            var sigBeforeFull = InvSig();
            var goldBeforeFull = _player.Gold;
            ClearEv();
            var okFull = _ctx.Npc.Buy((int)NpcId.Akara, idxU4, 2);
            Check("§7-4a 满包买 2 件 ⇒ false + 整笔取消（金币不变 + 日志「背包放不下」）",
                !okFull && _player.Gold == goldBeforeFull && _log.Contains("Npc", "背包放不下"),
                "ok=" + okFull + " 钱=" + _player.Gold + " 日志「背包放不下」=" + _log.Contains("Npc", "背包放不下"));
            Check("§7-4b 已放的第 1 件**回滚**（背包签名 = 买入前）",
                InvSig() == sigBeforeFull, "sig 一致=" + (InvSig() == sigBeforeFull) + " anchors=" + CountAnchors());
            Check("§7-4c 发了 InventoryFull（玩家可见提示的来源）", CountEv("InventoryFull") == 1, "事件=" + CountEv("InventoryFull"));

            // 7-5 自检：把 §7-3 的「金币不变」判据喂「§7-1 买成功（钱被扣）」错样本 ⇒ 必须判红
            Func<int, int, bool> goldUnchanged = (before, after) => after == before;
            var paidSample = gold0 - priceU;                        // §7-1 买成功后的余额（= 已扣钱）
            Check("§7-5 自检：同一判据喂「买成功后（钱被扣）」错样本 ⇒ 必须判红",
                !goldUnchanged(gold0, paidSample),
                "错样本 delta=" + (paidSample - gold0) + " ⇒ 判据 = " + (goldUnchanged(gold0, paidSample) ? "通过（❌ 判据永真）" : "红 ✓"));

            // ── 8. 卖出 ─────────────────────────────────────────────────────
            Section("§8 卖出：成功 / 任务物品拒绝 / 空锚点拒绝");
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(0);
            _ctx.Item.AddToInventory(new ItemStack
            {
                itemId = 7, name = "测试短剑", type = ItemType.Weapon,
                price = 1000, gridW = 1, gridH = 1, count = 1,
            });
            var swordAnchor = FirstAnchorOf(it => it.name == "测试短剑");
            var okSell = _ctx.Npc.Sell((int)NpcId.Akara, swordAnchor);
            Check("§8-1 卖出普通物品 ⇒ true + 得 SellPriceOf(1000) = 250 + 物品从背包消失",
                okSell && _player.Gold == 250 && _player.Gold == GameConst.SellPriceOf(1000) && FirstAnchorOf(it => it.name == "测试短剑") < 0,
                "ok=" + okSell + " 得钱=" + _player.Gold + " 还在背包=" + (FirstAnchorOf(it => it.name == "测试短剑") >= 0));

            _ctx.Item.Reset();
            _player.SetGoldForTest(0);
            _ctx.Item.AddToInventory(new ItemStack
            {
                itemId = 8, name = "任务之书", type = ItemType.Misc,
                price = 500, gridW = 1, gridH = 1, count = 1, isQuestItem = true,
            });
            var questAnchor = FirstAnchorOf(it => it.isQuestItem);
            var okSellQ = _ctx.Npc.Sell((int)NpcId.Akara, questAnchor);
            Check("§8-2 任务物品 ⇒ false + 物品还在 + 日志「任务物品」",
                !okSellQ && FirstAnchorOf(it => it.isQuestItem) >= 0 && _log.Contains("Npc", "任务物品"),
                "ok=" + okSellQ + " 还在=" + (FirstAnchorOf(it => it.isQuestItem) >= 0));

            var okSellEmpty = _ctx.Npc.Sell((int)NpcId.Akara, 39);
            Check("§8-3 空锚点格 ⇒ false + 日志「没有物品」",
                !okSellEmpty && _log.Contains("Npc", "没有物品"), "ok=" + okSellEmpty);

            // ── 9. 修理 ─────────────────────────────────────────────────────
            Section("§9 修理：只有恰西（canRepair）");
            var rKashya = _ctx.Npc.Repair((int)NpcId.Kashya, -1);
            Check("§9-1 卡夏（不提供修理）⇒ -1 + 日志「只有恰西能修」",
                rKashya == -1 && _log.Contains("Npc", "只有恰西能修"), "ret=" + rKashya);

            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(100000);
            _ctx.Item.AddToInventory(new ItemStack
            {
                itemId = 9, name = "破损皮甲", type = ItemType.Armor,
                price = 4000, gridW = 1, gridH = 1, count = 1,
                durability = 10, maxDurability = 50,
            });
            var brokenAnchor = FirstAnchorOf(it => it.name == "破损皮甲");
            var goldBeforeRepair = _player.Gold;
            var cost = _ctx.Npc.Repair((int)NpcId.Charsi, brokenAnchor);
            var fixedArmor = AnchorItemAt(FirstAnchorOf(it => it.name == "破损皮甲"));
            Check("§9-2 恰西修破损甲 ⇒ cost > 0 + 耐久回满 50 + 金币 -= cost",
                cost > 0 && fixedArmor != null && fixedArmor.durability == 50 && _player.Gold == goldBeforeRepair - cost,
                "cost=" + cost + " 耐久=" + (fixedArmor == null ? "?" : fixedArmor.durability + "/" + fixedArmor.maxDurability) +
                " 钱 " + goldBeforeRepair + "→" + _player.Gold);

            var costAll = _ctx.Npc.Repair((int)NpcId.Charsi, -1);
            Check("§9-3 恰西全修（index < 0）⇒ >= 0（刚修完 ⇒ 0，属合法「无需修理」）", costAll >= 0, "ret=" + costAll);

            // ── 10. 点击 → 走位 → 说话 ──────────────────────────────────────
            Section("§10 点击→走位→说话（含意图作废）");
            _ctx.Npc.Reset();
            _ctx.Item.Reset();
            _player.TeleportTo(new Vector2Int(5, 5));
            ClearEv();
            Game.Event.Emit(Events.NpcInteractRequest, (int)NpcId.Akara);
            var mv = LastEvPayload("MoveCommand");
            Check("§10-1 远处（(5,5) 距 (10,10) ≈7.07 > 2.4）点阿卡拉 ⇒ 发 MoveCommand(10,10)、不立即开口",
                CountEv("MoveCommand") == 1 && mv == "10,10" && CountEv("DialogOpen") == 0,
                "mv=" + mv + " DialogOpen=" + CountEv("DialogOpen"));

            _player.TeleportTo(new Vector2Int(10, 10));
            _ctx.Npc.Tick(Dt);
            Check("§10-2 走到站位格 ⇒ Tick 自动 DialogOpen(0)", 
                CountEv("DialogOpen") == 1 && LastEvPayload("DialogOpen") == "0",
                "DialogOpen=" + CountEv("DialogOpen"));

            // 10-3 作废：把移动目标改成别的格 ⇒ 走到位也不开口
            _ctx.Npc.ChooseOption((int)NpcId.Akara, 0);             // 先关掉 10-2 开的对话
            ClearEv();
            _player.TeleportTo(new Vector2Int(5, 5));
            Game.Event.Emit(Events.NpcInteractRequest, (int)NpcId.Akara);   // pending + MoveCommand(10,10)
            Game.Event.Emit(Events.MoveCommand, new Vector2Int(5, 5));      // 移动目标 → 别的格 ⇒ 意图作废
            _player.TeleportTo(new Vector2Int(10, 10));
            _ctx.Npc.Tick(Dt);
            Check("§10-3 意图作废（移动目标改成别处）⇒ 走到位也不开口", CountEv("DialogOpen") == 0,
                "DialogOpen=" + CountEv("DialogOpen"));

            ClearEv();
            _player.TeleportTo(new Vector2Int(9, 10));              // 距阿卡拉 1 格 < TalkRange
            Game.Event.Emit(Events.NpcInteractRequest, (int)NpcId.Akara);
            Check("§10-4 范围内点阿卡拉 ⇒ 当场 DialogOpen + 无 MoveCommand",
                CountEv("DialogOpen") == 1 && CountEv("MoveCommand") == 0,
                "DialogOpen=" + CountEv("DialogOpen") + " MoveCommand=" + CountEv("MoveCommand"));

            // ── 11. 走出范围自动关（连商店一起）─────────────────────────────
            Section("§11 走出对话范围 ⇒ 自动关（开着商店连商店一起关）");
            _player.TeleportTo(new Vector2Int(30, 30));             // 10-4 的对话还开着，走到远处
            ClearEv();
            _ctx.Npc.Tick(Dt);
            Check("§11-1 走出 TalkRange ⇒ Tick 自动 DialogClose ×1", CountEv("DialogClose") == 1, "事件=" + CountEv("DialogClose"));

            _player.TeleportTo(new Vector2Int(9, 10));
            _ctx.Npc.Interact((int)NpcId.Akara);                    // 重开对话
            Game.Event.Emit(Events.DialogOptionChosen, 1);          // 选「交易」⇒ 开商店
            _player.TeleportTo(new Vector2Int(30, 30));
            ClearEv();
            _ctx.Npc.Tick(Dt);
            var iShopClose = IndexEv("ShopClose");
            var iDialogClose = IndexEv("DialogClose");
            Check("§11-2 商店开着走出范围 ⇒ ShopClose 先发、DialogClose 后发",
                CountEv("ShopClose") == 1 && CountEv("DialogClose") == 1 && iShopClose >= 0 && iDialogClose > iShopClose,
                "次序 [" + string.Join(",", _ev) + "]");

            _player.TeleportTo(new Vector2Int(9, 10));
            _ctx.Npc.Interact((int)NpcId.Akara);
            ClearEv();
            _ctx.Npc.Tick(Dt);
            Check("§11-3 对话中仍在范围内 ⇒ Tick 不关（负样本）", CountEv("DialogClose") == 0, "事件=" + CountEv("DialogClose"));
            _ctx.Npc.ChooseOption((int)NpcId.Akara, 0);             // 收尾：关掉对话

            // ── 12. 交易事件路由 ────────────────────────────────────────────
            Section("§12 交易事件路由（事件 → 模块）");
            _ctx.Item.Reset();
            _ctx.Npc.Reset();
            _player.SetGoldForTest(100000);
            var s12 = _ctx.Npc.GetShop((int)NpcId.Akara);
            var idx12 = FirstUnlimited1x1(s12);
            var g12 = _player.Gold;
            Game.Event.Emit(Events.ShopBuyRequest, new ShopTradeArgs { npcId = (int)NpcId.Akara, index = idx12, count = 1 });
            Check("§12-1 ShopBuyRequest(阿卡拉, idx, 1) ⇒ 走 Buy（金币 -= 价）",
                idx12 >= 0 && _player.Gold == g12 - s12.stock[idx12].price,
                "钱 " + g12 + "→" + _player.Gold + "（价 " + (idx12 >= 0 ? s12.stock[idx12].price : -1) + "）");

            var g12b = _player.Gold;
            Game.Event.Emit(Events.ShopBuyRequest, new ShopTradeArgs { npcId = (int)NpcId.None, index = idx12, count = 1 });
            Check("§12-2 没带 npcId 且无进行中对话 ⇒ 拒绝 + 金币不变 + 日志（⛔ 不兜底阿卡拉）",
                _player.Gold == g12b && _log.Contains("Npc", "不再用 0/阿卡拉兜底"),
                "钱=" + _player.Gold + "（未变=" + (_player.Gold == g12b) + "）");
            // 附：`Get` 的 Warn 原因必须**准**
            Check("§12-3 Get(id 不在 5 个里) 的 Warn 原因是「没有这个 id」（⛔ 不再笼统「_defs 为空」）",
                _log.Contains("Npc", "但其中没有 id=-1"),
                "命中=" + _log.CountOf("Npc", "但其中没有 id"));

            // ── 收尾 ────────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine($"NPCHECK_SUMMARY ok={_ok} fail={_fail}");
            return _fail == 0 ? 0 : 1;
        }
    }
}
