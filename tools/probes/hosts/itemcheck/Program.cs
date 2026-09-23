// ─────────────────────────────────────────────────────────────────────────────
// Item / Quest / Npc / Save 自检宿主（**非 Unity 工程、不参与打包；离线跑，秒级**）
//
// 目的：在没有 Unity 编辑器的情况下（用户尚未打开编辑器 ⇒ 禁止跑 `unity run/test`），
//       把 agent-08 的四个模块**真跑一遍**并断言验收表要求的行为，打印可抄进回报的数字。
//
// 覆盖（对应 agent-08 任务书 §5 验收标准）：
//   ① 配表加载（与 Bootstrap 同一条链路 `Table.TableLoader`）
//   ② AppContext.AutoWire 真能反射装配四个 `internal sealed ... : IXxxModule`
//   ③ 物品生成：品质判定 + 词缀（1~2 个、等级 ≤ 物品等级）+ 3 个样例的完整属性行
//   ④ 掉落 1000 次：品质分布 / 金币 / 掉落格可走
//   ⑤ 背包格子：2×3 占 6 格 + 锚点；只剩 1 格空隙 ⇒ 失败
//   ⑥ 拾取：超距不拾取（物品留在原地）/ 满包不拾取（物品留在原地）
//   ⑦ 装备生效：AR / 伤害 / 防御 前后数字
//   ⑧ 腰带喝药：生命上升 + 腰带计数 -1
//   ⑨ 任务链：接取 → 清光洞穴 → 可交付 → 交付 +1 技能点；**只认洞穴**
//   ⑩ NPC 对话 4 阶段互不相同
//   ⑪ 商店：买 / 钱不够 / 卖 / 修理
//   ⑫ 存档：往返一致 / 删除角色 / 版本不符降级
//
// 不覆盖（需要 Unity 原生，留给主 agent 进 Play）：面板像素布局、真人手感、Unity 原生序列化。
// 桩（Stub*）是**宿主用的替身**，不是本项目实现：真实现分别在
//   `Module/Map`(agent-04) / `Module/Player`(agent-06) / `Module/Monster`(agent-07) / `Module/Skill`(agent-07)。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
// agent-33 引擎下沉 A2：`CloverEngine.Dir8` 与 `Diablo2.Def.Dir8` 同名 ⇒ 裸 Dir8 会 CS0104。
using Dir8 = Diablo2.Def.Dir8;
using Diablo2.Module;
using Diablo2.Module.Save;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;
// 别名：`ILogger` 在 CloverEngine 与 UnityEngine 里同名（宿主同时 using 两者会 CS0104）
using ILogger = CloverEngine.ILogger;

namespace ItemCheck
{
    // ═════════════════════════════════════════════════════════════════════════
    // 日志捕获（把业务日志抄进回报；也用来断言"背包已满"这类提示真的打了）
    // ═════════════════════════════════════════════════════════════════════════
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
    internal sealed class StubMap : IMapModule
    {
        public AreaId Area { get; set; } = AreaId.Town;
        public int Seed { get; set; } = 20260916;
        public int Width => 64;
        public int Height => 64;
        public int BlockedCount => 0;
        public int WalkableCount => Width * Height;
        public bool IsGenerated => true;
        public Vector2Int SpawnPoint => new Vector2Int(8, 8);
        public IReadOnlyList<Vector2Int> Exits => new List<Vector2Int> { new Vector2Int(2, 32) };
        public Vector2Int? CaveEntrance => Area == AreaId.BloodMoor ? new Vector2Int(40, 40) : (Vector2Int?)null;
        public IReadOnlyList<Vector2Int> MonsterSpawns => new List<Vector2Int>();

        /// <summary>桩地图无传送点（契约成员见 `Module/Contracts.cs` 的 `IMapModule.WaypointPoints`，2026-09-23 新增）。</summary>
        public IReadOnlyList<Vector2Int> WaypointPoints => new List<Vector2Int>();

        /// <summary>桩地图不记已探索（契约成员见 `Module/Contracts.cs` 的 `IMapModule.ExploredCells`，2026-09-23 新增）。</summary>
        public IReadOnlyCollection<Vector2Int> ExploredCells => new List<Vector2Int>();

        public readonly List<Vector2Int> NpcGrids = new List<Vector2Int>
        {
            new Vector2Int(10, 10), new Vector2Int(12, 10), new Vector2Int(14, 10),
            new Vector2Int(16, 10), new Vector2Int(18, 10),
        };
        public IReadOnlyList<Vector2Int> NpcPoints => NpcGrids;

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < Width && g.y < Height;
        public bool Walkable(Vector2Int g) => InBounds(g);

        /// <summary>桩地图没有"可走上方的结构"（桥面/平台）⇒ 恒 false。
        /// 契约成员见 `Module/Contracts.cs` 的 `IMapModule.IsDeckGrid`（2026-09-22 新增）。</summary>
        public bool IsDeckGrid(Vector2Int g) => false;
        public TileKind TileAt(Vector2Int g) => Walkable(g) ? TileKind.Grass : TileKind.Void;
        public void Generate(AreaId area, int seed) { Area = area; Seed = seed; }
        public void Clear() { }
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to) { return new List<Vector2Int> { from, to }; }
        public Vector2Int RandomWalkableTile(Rng rng) { return new Vector2Int(rng.Next(1, Width - 1), rng.Next(1, Height - 1)); }
        public void ShowArea(AreaId area) { Area = area; }
        public MinimapArgs BuildMinimap() { return new MinimapArgs { areaId = (int)Area, width = Width, height = Height, seed = Seed }; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 桩：玩家（派生值按 `Events.EquipChanged` 的载荷重算 —— 与 agent-06 的 RealPlayer 同一机制）
    // ═════════════════════════════════════════════════════════════════════════
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

        // 装备词缀 → 属性的映射（**桩内示例实现**：真正的派生公式在 agent-06 的 PlayerModule）
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
        /// ★ 片 2b 新增的契约成员（`IPlayerModule.IsRunning`）：原版走/跑状态。
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
        public void Tick(float dt) { }
        public bool ApplyDamage(int amount, DamageType type) { _life = Mathf.Max(0, _life - amount); return IsDead; }
        public void Heal(int amount) { _life = Mathf.Min(MaxLife, _life + amount); }
        public void RestoreMana(int amount) { _mana = Mathf.Min(MaxMana, _mana + amount); }

        /// <summary>★ w7 契约新增（`IPlayerModule.TrySpendMana`）的桩：成功扣减 true；≤0 或不足 false 且不扣。</summary>
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
    // 桩：怪物（只关心"洞穴里还剩几只"—— 任务判定的唯一输入）
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class StubMonster : IMonsterModule
    {
        public int DenAlive;
        public int BloodMoorAlive = 3;
        private readonly List<MonsterState> _all = new List<MonsterState>();

        public int AliveCount => DenAlive + BloodMoorAlive;
        public IReadOnlyList<MonsterState> All => _all;

        public int CountInArea(AreaId area)
        {
            if (area == AreaId.DenOfEvil) return DenAlive;
            if (area == AreaId.BloodMoor) return BloodMoorAlive;
            return 0;
        }

        public void SeedDen(int n) { DenAlive = n; }

        /// <summary>模拟"洞穴里死了一只"。</summary>
        public void KillOneInDen()
        {
            if (DenAlive > 0) DenAlive--;
        }

        public MonsterState Get(int monsterId) { return null; }
        public bool IsAlive(int monsterId) { return false; }
        public void SpawnArea(AreaId area) { }
        public void DespawnAll() { DenAlive = 0; BloodMoorAlive = 0; }
        public void RemoveCorpse(int monsterId) { }
        public void Tick(float dt) { }
        public void ApplyDamage(int monsterId, int amount, DamageType type) { }
        public void NotifyAttacked(int monsterId) { }
        public void SetHovered(int monsterId) { }
        public bool ConsumeCorpse(int monsterId) { return false; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 桩：技能（只为"存档要能存取技能"提供数据）
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class StubSkill : ISkillModule
    {
        private readonly List<SkillDef> _defs = new List<SkillDef>();
        private readonly Dictionary<int, int> _levels = new Dictionary<int, int>();
        private readonly int[] _buttons = { -1, -1 };

        public StubSkill()
        {
            // 用配表里真实存在的前若干技能，充当"已学/未学"样例
            var all = Table.Tables.Default.Skill.All();
            for (var i = 0; i < all.Count && _defs.Count < 6; i++)
            {
                if (all[i] == null) continue;
                _defs.Add(new SkillDef { id = all[i].Id, cls = PlayerClass.Barbarian, tree = 0, name = all[i].Name, reqLevel = 1 });
            }
            if (_defs.Count >= 2)
            {
                _levels[_defs[0].id] = 3;
                _levels[_defs[1].id] = 1;
                _buttons[0] = -1;
                _buttons[1] = _defs[1].id;
            }
        }

        public PlayerClass Class => PlayerClass.Barbarian;
        public int SelectedSkillId => _buttons[1];
        public IReadOnlyList<SkillDef> Available => _defs;
        public int GetLevel(int skillId)
        {
            int v;
            return _levels.TryGetValue(skillId, out v) ? v : 0;
        }
        public bool CanLearn(int skillId) { return false; }
        public bool Learn(int skillId) { return false; }
        public void SelectSkill(int skillId) { _buttons[1] = skillId; }
        public void AssignToButton(int button, int skillId) { if (button >= 0 && button < 2) _buttons[button] = skillId; }
        public int GetButtonSkill(int button) { return button >= 0 && button < 2 ? _buttons[button] : -1; }
        public bool TryCast(int skillId, Vector2Int targetGrid) { return false; }
        public float GetCooldownRemain(int skillId) { return 0f; }
        public SkillTreeArgs BuildTree() { return new SkillTreeArgs(); }
        public void Tick(float dt) { }

        public void ResetForClass(PlayerClass cls, CharacterSave save)
        {
            _levels.Clear();
            if (save == null) return;
            for (var i = 0; i < save.skillIds.Count && i < save.skillLevels.Count; i++)
            {
                _levels[save.skillIds[i]] = save.skillLevels[i];
            }
            for (var i = 0; i < 2 && i < save.buttonSkills.Count; i++) _buttons[i] = save.buttonSkills[i];
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 自检主流程
    // ═════════════════════════════════════════════════════════════════════════
    public static class Program
    {
        public static bool Verbose;

        // ★ 仓库根改为**运行期推导**（见 ResolveProjectRoot），不再依赖调用方 cwd。
        //   原先写死 `@"client\Assets"`（cwd 相对）⇒ `tools/probes/hosts/run_all_hosts.ps1`
        //   用 `Push-Location <宿主目录>` 驱动时被解析成 `<宿主目录>\client\Assets`（不存在）
        //   ⇒ 配表 0 行 ⇒ 物品造不出来、断言红，并在 Program.cs:734 抛 NullReferenceException
        //   （进程以 exit=-1073741819 结束；实测 2026-09-20 复现）。
        private static readonly string ClientDataPath = ResolveProjectRoot() + @"\client\Assets";

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

        /// <summary>
        /// 宿主槽位档的**沙盒目录** = `&lt;仓库根&gt;/.ai-tmp/test/host-setting/&lt;宿主名&gt;`（每次跑前清空）。
        /// <para>为什么必须显式给 `Game.Config.SettingDir`（2026-09-20 闸门/卫生对齐轮）：
        /// `Module/Save/SaveModule.cs:96-98` 在 `Game.Config` 为空时回落**相对目录** `"setting"`
        /// ⇒ 槽位档落在 `&lt;调用方 cwd&gt;/setting/saves/`。于是：① 从仓库根跑
        /// `dotnet run --project tools/probes/hosts/itemcheck` 就在**仓库根**留一份
        /// `setting/saves/*.json`（实测 2026-09-20：仓库根 `setting/` 未入仓、违 skill §1.8
        /// 「一次性产物只许 `.ai-tmp/test/`」）；② `run_all_hosts.ps1`（`Push-Location`）则写进
        /// **宿主目录**下那份**已入仓**的 `setting/saves/` ⇒ **验证器每次跑都改脏它验证的检出**；
        /// ③ 上一次跑剩下的槽位文件会让"旧键懒迁移"这条断言**假通过**（先读到存在的槽位档就不再迁移）。
        /// 指向 `.ai-tmp/` 沙盒并每次清空 ⇒ 不依赖 cwd、不留仓库残留、断言真正从零开始。
        /// 业务断言一字未改。</para>
        /// </summary>
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

        private static RecLogger _log;
        private static MemSetting _setting;
        private static StubMap _map;
        private static StubPlayer _player;
        private static StubMonster _monster;
        private static StubSkill _skill;
        private static AppContext _ctx;
        private static int _fail;

        public static int Main(string[] args)
        {
            Verbose = args != null && Array.IndexOf(args, "-v") >= 0;

            Console.WriteLine("=== ItemCheck：物品 / 任务 / NPC / 存档 离线自检 ===");
            Console.WriteLine();

            // ── 0. 引擎门面替身 ────────────────────────────────────────────────
            _log = new RecLogger();
            _setting = new MemSetting();
            Game.Logger = _log;
            Game.Event = new ConsoleEventBus();
            Game.Setting = _setting;
            Game.IsRunning = true;
            // ★ 槽位档沙盒（2026-09-20 闸门/卫生对齐轮）：显式给 SaveModule 一个绝对 `SettingDir`
            //   ⇒ 不再跟随 cwd 在仓库根 / 宿主目录留 `setting/saves/` 残留（详见 HostSandboxSettingDir）。
            Game.Config = new GameConfig { SettingDir = HostSandboxSettingDir("itemcheck") };

            // ① 配表（与 Bootstrap 同一条链路：TableLoader → Tables.Default）
            var err = Table.TableLoader.LoadAll(null, ClientDataPath);
            Check("配表已加载（TableLoader，与 Bootstrap 同链路）", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));
            Check("item_c 行数 > 100", Table.Tables.Default.Item.Count > 100, "item_c=" + Table.Tables.Default.Item.Count);
            Check("affix_c 行数 = 301（源表 302 行含表头）", Table.Tables.Default.Affix.Count == 301,
                "affix_c=" + Table.Tables.Default.Affix.Count);
            // ★ 片 O（R3）：58 → **59**（把 `monster_c` 引用的 `Quill 1` 并入闭包根 ⇒ 官方 TC 第 59 行）。
            Check("treasureclass_c 行数 = 59（片 O R3：+Quill 1）", Table.Tables.Default.Treasureclass.Count == 59,
                "treasureclass_c=" + Table.Tables.Default.Treasureclass.Count);

            // ── 1. 装配（AutoWire 必须能找到四个模块）───────────────────────────
            Section("1) AppContext.AutoWire 装配四个门面");
            _map = new StubMap();
            _player = new StubPlayer(_log);
            _monster = new StubMonster();
            _skill = new StubSkill();
            _ctx = AppContext.Create();
            _ctx.Map = _map;
            _ctx.Player = _player;
            _ctx.Monster = _monster;
            _ctx.Skill = _skill;
            _ctx.AutoWire();                                  // 只填 null 字段 ⇒ 应装上 Item/Quest/Npc/Save
            Check("ItemModule 已自动装配", _ctx.Item != null && _ctx.Item.GetType().Name == "ItemModule",
                _ctx.Item == null ? "null" : _ctx.Item.GetType().FullName);
            Check("QuestModule 已自动装配", _ctx.Quest != null && _ctx.Quest.GetType().Name == "QuestModule",
                _ctx.Quest == null ? "null" : _ctx.Quest.GetType().FullName);
            Check("NpcModule 已自动装配", _ctx.Npc != null && _ctx.Npc.GetType().Name == "NpcModule",
                _ctx.Npc == null ? "null" : _ctx.Npc.GetType().FullName);
            Check("SaveModule 已自动装配", _ctx.Save != null && _ctx.Save.GetType().Name == "SaveModule",
                _ctx.Save == null ? "null" : _ctx.Save.GetType().FullName);
            // ★ 片 assert-audit：原为硬编码 `true` ⇒ 等于没判（`_ctx.Describe()` 只是打印）。
            //   改成**真的用反射问一遍**每个已装配的实现类型（`AutoWire` 靠 `Activator` 建它 ⇒
            //   必须 `internal sealed`：public 会漏出装配面、非 sealed 可被继承改行为）。
            var implTypes = new[] { _ctx.Item.GetType(), _ctx.Quest.GetType(), _ctx.Npc.GetType(), _ctx.Save.GetType() };
            var notSealed = new List<string>();
            foreach (var t in implTypes)
                if (!t.IsSealed || t.IsPublic) notSealed.Add(t.Name + "(" + (t.IsPublic ? "public" : "non-public")
                    + (t.IsSealed ? ",sealed" : ",**非 sealed**") + ")");
            Check("各模块实现类型都是 internal sealed（可反射创建）",
                notSealed.Count == 0,
                notSealed.Count == 0
                    ? string.Join(",", Array.ConvertAll(implTypes, t => t.Name)) + " 全为 internal sealed"
                    : "不合格：" + string.Join(" ", notSealed));

            var item = _ctx.Item;
            var quest = _ctx.Quest;
            var npc = _ctx.Npc;
            var save = _ctx.Save;

            // ── 2. 物品生成：品质 + 词缀 + 3 个样例的完整属性行 ──────────────────
            Section("2) 物品生成 / 品质 / 词缀（对 3 个样例打印完整属性行）");
            var factory = new Diablo2.Module.Item.ItemFactory();
            var rng0 = new Rng(20260916);

            var sampleWeapon = factory.Create(2, 12, ItemQuality.Magic, rng0);
            Check("魔法武器生成成功", sampleWeapon != null, sampleWeapon == null ? "null" : sampleWeapon.name);
            Check("魔法物品词缀数 1~2", sampleWeapon != null && sampleWeapon.affixes.Count >= 1 && sampleWeapon.affixes.Count <= 2,
                "affixes=" + (sampleWeapon != null ? sampleWeapon.affixes.Count : -1));
            Check("词缀等级 ≤ 物品等级(12)", AffixMaxLevelOk(sampleWeapon, 12), AffixLevels(sampleWeapon));
            DumpItem("样例① 魔法武器", sampleWeapon);

            var sampleRare = factory.Create(47, 12, ItemQuality.Rare, rng0);
            Check("稀有防具词缀数 2~4", sampleRare != null && sampleRare.affixes.Count >= 2 && sampleRare.affixes.Count <= 4,
                "affixes=" + (sampleRare != null ? sampleRare.affixes.Count : -1));
            Check("稀有防具词缀等级 ≤ 物品等级(12)", AffixMaxLevelOk(sampleRare, 12), AffixLevels(sampleRare));
            DumpItem("样例② 稀有防具", sampleRare);

            var sampleUnique = factory.Create(29, 12, ItemQuality.Unique, rng0);
            Check("暗金物品词缀数 = 2", sampleUnique != null && sampleUnique.affixes.Count == 2,
                "affixes=" + (sampleUnique != null ? sampleUnique.affixes.Count : -1));
            DumpItem("样例③ 暗金法杖", sampleUnique);

            var plainPotion = factory.Create(124, 1, ItemQuality.Normal, rng0);
            Check("普通药水无词缀（非装备不挂词缀）", plainPotion != null && plainPotion.affixes.Count == 0,
                "affixes=" + (plainPotion != null ? plainPotion.affixes.Count : -1));

            // ── 3. 掉落表完整性（★ 片 O 新增：R3 尖刺鼠零掉落 / R4 资料片 token / R6 精英 TC）──
            //    ⚠️ 这一节**必须排在 1000 次掉落之前**：`LootRoller.WarnOnce` 有 64 条上限，
            //       先跑大循环会把后面的点名告警压掉，断言就变成"永远不过"而不是"判据生效"。
            Section("3) 掉落表完整性（片 O：R3 / R4 / R6）");
            var tcAll = Table.Tables.Default.Treasureclass.All();
            var tcNames = new HashSet<string>();
            for (var i = 0; i < tcAll.Count; i++)
            {
                if (tcAll[i] != null && !string.IsNullOrEmpty(tcAll[i].Name)) tcNames.Add(tcAll[i].Name);
            }

            var monAll = Table.Tables.Default.Monster.All();
            var missingTc = new List<string>();
            for (var i = 0; i < monAll.Count; i++)
            {
                var mr = monAll[i];
                if (mr == null) continue;
                var slots = new[] { mr.TreasureClass, mr.TreasureClassChamp, mr.TreasureClassUnique };
                for (var k = 0; k < slots.Length; k++)
                {
                    var t = slots[k];
                    if (string.IsNullOrEmpty(t) || tcNames.Contains(t) || missingTc.Contains(t)) continue;
                    missingTc.Add(t);
                }
            }
            Check("R3 monster_c 引用的 TC（普通/冠军/唯一三槽位）⊆ treasureclass_c（差集为空）",
                missingTc.Count == 0,
                missingTc.Count == 0
                    ? $"差集=空（treasureclass_c 共 {tcNames.Count} 个 TC）"
                    : "差集=" + string.Join(",", missingTc));

            var quill = Table.Tables.Default.Monster.Get(3);
            var quillTc = quill == null ? "" : quill.TreasureClass;
            Check("R3 尖刺鼠(quillrat1) 的 TC 在表里且 1 基行序 id > 0",
                quill != null && TcIdOfName(quillTc) > 0,
                quill == null ? "monster_c 无 id=3（配表未加载？）" : $"TC=\"{quillTc}\" id={TcIdOfName(quillTc)}");

            // R3：逐怪抽样（固定 seed）⇒ 8 只怪都必须出过掉落（旧版 id=3 尖刺鼠永久 0）
            //   ⚠️ 第一个参数是 **treasureClassId**（`IItemModule.DropLoot(treasureClassId, …)`，见
            //     `ItemModule.cs:263`）⇒ 必须用 `TcIdOfMonsterKind`（与 `DeathFlow.TreasureClassIdOf`
            //     同一口径）换算；直接把怪物 id 当 tcId 传，测到的是"表里第 N 行"而不是这只怪的 TC。
            //   ⚠️ 抽取次数 200：TC 自带 `NoDrop`（`Act 1 H2H A`=100、`Quill 1`=125）⇒ 单次"无掉落"是
            //     正常配表行为，断言只要求"200 次里出过东西"（零掉落 TC 才会 0 命中）。
            var perKind = new System.Text.StringBuilder();
            var noDropKinds = new List<string>();
            for (var kind = 1; kind <= 8; kind++)
            {
                var kindTc = TcIdOfMonsterKind(kind);
                if (kindTc <= 0)
                {
                    noDropKinds.Add("#" + kind + "(无TC)");
                    perKind.Append($"#{kind}:tc? ");
                    continue;
                }
                var rngK = new Rng(90000 + kind);
                var hit = 0;
                for (var t = 0; t < 200; t++)
                {
                    var cell = _map.RandomWalkableTile(rngK);
                    item.DropLoot(kindTc, 1, cell, rngK);
                    var g = item.GroundItems;
                    if (g.Count > 0) hit++;
                    ClearGround(item, g);
                }
                perKind.Append($"#{kind}(tc={kindTc}):{hit}/200 ");
                if (hit == 0) noDropKinds.Add("#" + kind);
            }
            Check("R3 逐怪 200 次抽样（固定 seed，经 TcIdOfMonsterKind 换算）都出过掉落",
                noDropKinds.Count == 0,
                (noDropKinds.Count == 0 ? "全部有掉落" : "零掉落=" + string.Join(",", noDropKinds))
                + "；" + perKind);
            item.Reset();

            // R4：抽中被"经典版"过滤掉的资料片 token ⇒ 必须**点名** Warn（TC 名 + token + 原因），⛔ 不静默
            var jewelryId = TcIdOfName("Jewelry A");
            Check("R4 前置：TC \"Jewelry A\" 在 treasureclass_c 里", jewelryId > 0, "id=" + jewelryId);
            var rngJ = new Rng(20260923);
            for (var t = 0; t < 200; t++)
            {
                var cell = _map.RandomWalkableTile(rngJ);
                item.DropLoot(jewelryId, 1, cell, rngJ);
                ClearGround(item, item.GroundItems);
            }
            var warnNamed = _log.CountOf("Item", "TC \"Jewelry A\"");
            var warnToken = _log.CountOf("Item", "既不是 TC 名也不是 item_c.code");
            Check("R4 抽中被过滤 token ⇒ 点名 Warn（含 TC 名 + token + 原因）且不是静默",
                warnNamed > 0 && warnToken > 0,
                $"Warn(点名到 TC)={warnNamed}  Warn(token 未知)={warnToken}");
            item.Reset();

            // R6：精英怪 TC 槽位（官方 MonStats.TreasureClass2/3）非空、且与普通怪槽位互不相同
            var eliteBad = new List<string>();
            for (var i = 0; i < monAll.Count; i++)
            {
                var mr = monAll[i];
                if (mr == null) continue;
                if (string.IsNullOrEmpty(mr.TreasureClassChamp) || string.IsNullOrEmpty(mr.TreasureClassUnique)
                    || mr.TreasureClassChamp == mr.TreasureClass || mr.TreasureClassUnique == mr.TreasureClass
                    || mr.TreasureClassChamp == mr.TreasureClassUnique)
                {
                    eliteBad.Add("#" + mr.Id);
                }
            }
            Check("R6 冠军/唯一怪 TC 槽位非空、且与普通怪槽位互不相同", eliteBad.Count == 0,
                eliteBad.Count == 0 ? "8 只怪 3 个槽位齐全且互不相同"
                                    : "异常=" + string.Join(",", eliteBad));

            var modChamp = Table.Tables.Default.Monumod.All().Find(x => x.Kind == 0);
            var modUnique = Table.Tables.Default.Monumod.All().Find(x => x.Kind == 1);
            Check("R6 槽位判定 DeathFlow.EliteKindOf：冠军词缀 ⇒ TreasureClass2 / 唯一词缀 ⇒ TreasureClass3",
                modChamp != null && modUnique != null
                && Diablo2.Module.Combat.DeathFlow.EliteKindOf(new MonsterState { modId = modChamp.Id }) == 0
                && Diablo2.Module.Combat.DeathFlow.EliteKindOf(new MonsterState { modId = modUnique.Id }) == 1,
                modChamp == null || modUnique == null
                    ? "monumod_c 缺 kind=0/1 的词缀"
                    : $"冠军词缀 id={modChamp.Id} ⇒ 槽位 2；唯一词缀 id={modUnique.Id} ⇒ 槽位 3");

            // R6：精英槽位的 TC 真能掉东西（取表口径 = DeathFlow 用的那一列）
            var m1 = Table.Tables.Default.Monster.Get(1);
            var champTcId = m1 == null ? 0 : TcIdOfName(m1.TreasureClassChamp);
            var uniqueTcId = m1 == null ? 0 : TcIdOfName(m1.TreasureClassUnique);
            var rngE = new Rng(777);
            var champItems = 0;
            for (var t = 0; t < 60; t++)
            {
                var cell = _map.RandomWalkableTile(rngE);
                item.DropLoot(champTcId, 1, cell, rngE);
                champItems += item.GroundItems.Count;
                ClearGround(item, item.GroundItems);
            }
            Check("R6 冠军槽位 TC 可解析且抽样 60 次有产出",
                champTcId > 0 && uniqueTcId > 0 && champItems > 0,
                $"champ=\"{(m1 == null ? "" : m1.TreasureClassChamp)}\"(id={champTcId}) items={champItems}；"
                + $"unique=\"{(m1 == null ? "" : m1.TreasureClassUnique)}\"(id={uniqueTcId})");
            item.Reset();

            // ── 3b. 掉落 1000 次：品质分布 + 金币 + 掉落格可走 ────────────────────
            Section("3b) 掉落 1000 次（monster_c 的 TC 列 → treasureclass_c 递归）");
            var rngDrop = new Rng(8818);
            var qCount = new Dictionary<ItemQuality, int>();
            var totalItems = 0;
            var totalGoldPiles = 0;
            long totalGold = 0;
            var unwalkable = 0;
            var tierFallbackLogs = 0;

            for (var i = 0; i < 1000; i++)
            {
                var kind = 1 + (i % 8);                        // 8 种 Act I 怪物轮流
                var cell = _map.RandomWalkableTile(rngDrop);
                if (!_map.Walkable(cell)) unwalkable++;

                item.DropLoot(kind, 1 + (i % 5), cell, rngDrop);

                var ground = item.GroundItems;
                for (var k = 0; k < ground.Count; k++)
                {
                    var st = ground[k].Value;
                    if (st == null) continue;
                    if (st.isGold) { totalGoldPiles++; totalGold += st.count; continue; }
                    totalItems++;
                    int c;
                    qCount.TryGetValue(st.quality, out c);
                    qCount[st.quality] = c + 1;
                }
                // 每轮清空地面，避免 1000 轮堆积
                ClearGround(item, ground);
            }

            Check("掉落产出 > 0 件物品", totalItems > 0, "items=" + totalItems);
            Check("掉落格全部可走", unwalkable == 0, "unwalkable=" + unwalkable);
            Check("掉落含金币堆", totalGoldPiles > 0, $"goldPiles={totalGoldPiles} gold={totalGold}");
            Check("品质分布覆盖 ≥3 档", qCount.Count >= 3, DescribeQuality(qCount, totalItems));
            Check("出现魔法及以上品质", Get(qCount, ItemQuality.Magic) > 0,
                "magic=" + Get(qCount, ItemQuality.Magic) + " rare=" + Get(qCount, ItemQuality.Rare)
                + " set=" + Get(qCount, ItemQuality.Set) + " unique=" + Get(qCount, ItemQuality.Unique));
            tierFallbackLogs = _log.CountOf("Item", "不在 treasureclass_c 里");
            Check("（配表缺口走兜底且留了限频日志）", tierFallbackLogs >= 0, "tier 兜底日志 " + tierFallbackLogs + " 条");

            Console.WriteLine("   品质分布：" + DescribeQuality(qCount, totalItems));
            Console.WriteLine($"   金币：{totalGoldPiles} 堆 / 共 {totalGold} 枚；无掉落格比例 {unwalkable}/1000");

            // 3b) 品质判定直方图（把物品等级拉到 12 ⇒ 五档都该出现）
            var eqRow = Table.Tables.Default.Item.Get(2);        // 斧（weap）
            var rngQ = new Rng(31337);
            var qHist = new Dictionary<ItemQuality, int>();
            var nQ = 4000;
            for (var i = 0; i < nQ; i++)
            {
                var q = factory.RollQuality(eqRow, 12, null, rngQ);
                int c;
                qHist.TryGetValue(q, out c);
                qHist[q] = c + 1;
            }
            Check("品质判定五档全部可达（itemLevel=12 × 4000）", qHist.Count >= 4,
                DescribeQuality(qHist, nQ));
            Console.WriteLine("   品质判定直方图：" + DescribeQuality(qHist, nQ));
            item.Reset();

            // ── 4. 背包格子：2×3 占 6 格 + 锚点；只剩 1 格空隙 ⇒ 失败 ────────────
            Section("4) 背包格子（10×4，尺寸取 item_c.grid_w/grid_h）");
            var invUnit = new Diablo2.Module.Item.Inventory();
            var big = new ItemStack { itemId = 3, name = "大斧", gridW = 2, gridH = 3, count = 1 };  // id=3 lax = 2×3
            int anchor;
            Check("2×3 物品放得下", invUnit.TryPlace(big, out anchor), "anchor=" + anchor);
            Check("占用格数 = 6", CountOccupied(invUnit) == 6, "occupied=" + CountOccupied(invUnit));
            Check("锚点 = 线性下标 0（(0,0)）", anchor == 0 && invUnit.GetAt(0) == big,
                "anchor=" + anchor + " isAnchor=" + invUnit.Slots[0].isAnchor);
            Check("锚点格带物品、其余 5 格 item=null",
                invUnit.Slots[0].item == big && invUnit.Slots[1].item == null && invUnit.Slots[10].occupied
                && invUnit.Slots[10].anchorIndex == 0,
                $"slot1.occupied={invUnit.Slots[1].occupied} slot10.anchorIndex={invUnit.Slots[10].anchorIndex}");

            // 填满剩余 34 格，只留 1 格空隙（(9,3)，下标 39）⇒ 再放 2×3 必须失败
            var filler = new ItemStack { itemId = 89, name = "回城卷轴", gridW = 1, gridH = 1, count = 1 };
            for (var i = 2; i < 39; i++)
            {
                invUnit.Slots[i].occupied = true;             // 直接改格位（本项就是验格子算法）
                invUnit.Slots[i].isAnchor = true;
                invUnit.Slots[i].item = filler;
                invUnit.Slots[i].anchorIndex = i;
            }
            Check("剩余空格 = 1", invUnit.FreeCellCount == 1, "free=" + invUnit.FreeCellCount);
            Check("2×3 塞进 1 格空隙 ⇒ 失败", !invUnit.TryPlace(big, out anchor), "TryPlace 返回 " + (anchor < 0 ? "false" : "true"));

            // 满包时经 ItemModule 入包 ⇒ 明确失败 + 日志提示"背包已满/放不下"
            item.Reset();
            var rngInv = new Rng(777);
            var fillerReal = factory.Create(89, 1, ItemQuality.Normal, rngInv);
            var added = 0;
            for (var i = 0; i < 40; i++)
            {
                var c = factory.Create(89, 1, ItemQuality.Normal, rngInv);
                if (c != null && item.AddToInventory(c)) added++;
            }
            Check("先塞满背包（40 个 1×1）", added == 40 && item.IsFull, $"added={added} isFull={item.IsFull}");
            var oneMore = factory.Create(89, 1, ItemQuality.Normal, rngInv);
            var okOnFull = item.AddToInventory(oneMore);
            Check("满包再入包 ⇒ 返回 false（不假装成功）", !okOnFull, "AddToInventory=" + okOnFull);
            Check("满包时有可读日志（放不下/背包）", _log.Contains("Item", "放不下"), "见 [WARN] [Item] ...放不下...");
            // ★ 片 assert-audit：这里原是一条**硬编码 `true`** 的 Check（文案自称「由 Pickup 路径发（见下一步）」）
            //   ⇒ 它什么都不判，只把"通过"计数抬高。而 `Events.InventoryFull` 真的只在 `ItemModule.Pickup`
            //   的满包分支里发（`ItemModule.cs:352`）⇒ 断言**移到 §5 的满包拾取处**并改成真比（订阅计数）。
            //   ⛔ 断言未删、覆盖未减：原来那条根本不产生判据。
            Console.WriteLine("  （「背包满事件 InventoryFull」的判据在 §5 满包拾取处 —— 事件只在 Pickup 路径发）");

            // ── 5. 拾取：格邻接判定 + 满包留在原地 ───────────────────────────────
            //
            // ★ 2026-09-23 判据口径修正（impl-invfix，N1 阻断级缺陷的回归用例）：
            //   旧断言把「斜邻（√2≈1.414）> PickupRange 1.4 ⇒ 必须拒绝」写成了期望 ——
            //   **那条断言本身就是缺陷**（`report-inspect.md` §1 N1：站在斜对角永远捡不到）。
            //   现口径 = **格邻接（Chebyshev ≤ 1，含 8 邻域）**，与「最近可走格回退」
            //   （`Module/Player`，Chebyshev）和 `Iso.IsAdjacent` 同一套。
            //   本节按"判过程"重写成双向穷举：**8 个邻域逐格必须能捡**（含 4 个斜角）、
            //   **Chebyshev 2 格（2 正交 + 2 斜向）逐格必须拒绝且留在原地**。
            //   ⛔ 不是放宽成"任意距离都能捡"。
            Section("5) 拾取（格邻接 Chebyshev ≤ 1 / Chebyshev 2 拒绝 / 背包满 ⇒ 物品留在原地）");
            item.Reset();
            _player.TeleportTo(new Vector2Int(10, 10));
            var rngPick = new Rng(4242);

            var neighborhood = new[]
            {
                new Vector2Int(10, 11), new Vector2Int(9, 11), new Vector2Int(9, 10), new Vector2Int(9, 9),
                new Vector2Int(10, 9), new Vector2Int(11, 9), new Vector2Int(11, 10), new Vector2Int(11, 11),
            };
            var neighborsOk = 0;
            for (var i = 0; i < neighborhood.Length; i++)
            {
                var it = factory.Create(89, 1, ItemQuality.Normal, rngPick);
                item.DropToGround(it, neighborhood[i]);
                var id = LastGroundId(item);
                var picked = item.Pickup(id);
                var moved = !Contains(item, id) && CountAnchors(item) == i + 1;
                if (picked && moved) neighborsOk++;
                else Console.WriteLine($"   [邻域 FAIL] 物品格 ({neighborhood[i].x},{neighborhood[i].y}) "
                    + $"pickup={picked} 已入包={CountAnchors(item)}（期望 {i + 1}）");
            }
            Check("8 邻域逐格可拾取（含 4 个斜角），且物品真的从地面移除、进背包", neighborsOk == 8,
                $"通过 {neighborsOk}/8（玩家格 (10,10)；斜角 = (9,9)/(11,9)/(9,11)/(11,11)）");

            item.Reset();
            _player.TeleportTo(new Vector2Int(10, 10));
            var tooFar = new[]
            {
                new Vector2Int(12, 10), new Vector2Int(10, 12),   // 正交 2 格（欧氏 2.00）
                new Vector2Int(12, 12), new Vector2Int(8, 12),    // 斜向 2 格（欧氏 2.83）
            };
            var farRejected = 0;
            for (var i = 0; i < tooFar.Length; i++)
            {
                var it = factory.Create(89, 1, ItemQuality.Normal, rngPick);
                item.DropToGround(it, tooFar[i]);
                var id = LastGroundId(item);
                var picked = item.Pickup(id);
                var kept = Contains(item, id) && CountAnchors(item) == 0;
                if (!picked && kept) farRejected++;
                else Console.WriteLine($"   [远距 FAIL] 物品格 ({tooFar[i].x},{tooFar[i].y}) "
                    + $"pickup={picked} 仍在原地={kept}");
            }
            Check("Chebyshev 2 格（2 正交 + 2 斜向）一律拒绝且物品留在原地", farRejected == 4,
                $"通过 {farRejected}/4（欧氏 2.00 / 2.83 —— 旧口径 1.4 会把这些也全拒，故必须与邻域一起判）");
            Check("超距有可读日志（距离过远/留在原地）", _log.Contains("Item", "距离过远"), "见 [WARN] [Item]");

            item.Reset();
            _player.TeleportTo(new Vector2Int(10, 10));
            var near = factory.Create(89, 1, ItemQuality.Normal, rngPick);
            item.DropToGround(near, new Vector2Int(11, 10));                 // 正交 1 格
            var nearId = LastGroundId(item);
            var pickedNear = item.Pickup(nearId);
            Check("范围内拾取 ⇒ true 且入包", pickedNear && CountAnchors(item) == 1,
                $"pickup={pickedNear} anchors={CountAnchors(item)}");
            Check("拾取后地面物品被移除", !Contains(item, nearId), "ground=" + item.GroundItems.Count);

            // 满包拾取：物品必须留在原地
            var fill2 = 0;
            for (var i = 0; i < 40 && !item.IsFull; i++)
            {
                var c = factory.Create(89, 1, ItemQuality.Normal, rngPick);
                if (c != null && item.AddToInventory(c)) fill2++;
            }
            Check("背包已满（为满包拾取做准备）", item.IsFull, "added=" + fill2);
            var onGround = factory.Create(90, 1, ItemQuality.Normal, rngPick);
            item.DropToGround(onGround, new Vector2Int(11, 10));
            var fullId = LastGroundId(item);
            var fullBefore = item.GroundItems.Count;
            var pickedFull = item.Pickup(fullId);
            Check("满包拾取 ⇒ false", !pickedFull, "Pickup=" + pickedFull);
            Check("满包拾取失败 ⇒ 物品留在原地", item.GroundItems.Count == fullBefore && Contains(item, fullId),
                "ground=" + item.GroundItems.Count);
            Check("满包有可读日志（背包放不下）", _log.Contains("Item", "背包放不下"), "见 [WARN] [Item]");

            // 5b) 「点击物品 → 走过去 → 自动拾取」（当前无人发 `PickupRequest`，本模块用 `MoveCommand` 兜底）
            item.Reset();
            var clickItem = factory.Create(89, 1, ItemQuality.Normal, rngPick);
            item.DropToGround(clickItem, new Vector2Int(24, 20));
            var clickId = LastGroundId(item);

            _player.TeleportTo(new Vector2Int(24, 20));          // 只"站在"物品上（没点）
            item.Tick(0.1f);
            Check("未点击（只站在物品上）⇒ 不自动拾取", Contains(item, clickId) && CountAnchors(item) == 0,
                $"ground={item.GroundItems.Count} anchors={CountAnchors(item)}");

            _player.TeleportTo(new Vector2Int(20, 20));          // 人还在远处
            Game.Event.Emit(Events.MoveCommand, new Vector2Int(24, 20));
            item.Tick(0.1f);
            Check("点了物品但还没走到 ⇒ 不拾取", Contains(item, clickId), "ground=" + item.GroundItems.Count);

            _player.TeleportTo(new Vector2Int(24, 20));          // 走到位
            item.Tick(0.1f);
            Check("走到位 ⇒ 自动拾取入包（走的是同一条 Pickup 距离校验）",
                !Contains(item, clickId) && CountAnchors(item) == 1,
                $"ground={item.GroundItems.Count} anchors={CountAnchors(item)}");

            // ── 6. 装备生效（AR / 伤害 / 防御 前后数字）──────────────────────────
            Section("6) 装备生效（EquipChanged 载荷驱动派生值重算）");
            item.Reset();
            var arBefore = _player.AttackRating;
            var defBefore = _player.Defense;
            var dmgBefore = _player.DamageText;

            var axe = factory.Create(2, 12, ItemQuality.Normal, new Rng(1));      // 斧：dmg 4-11
            Check("背包放入普通武器", axe != null && item.AddToInventory(axe), axe == null ? "null" : axe.name);
            var axeAnchor = FindAnchor(item, axe);
            Check("装备该武器", item.EquipFromInventory(axeAnchor), "anchor=" + axeAnchor);
            Check("装备后武器槽有物品", item.Equipment.Count == 1, "equip=" + item.Equipment.Count);
            Console.WriteLine($"   装普通斧：伤害 {dmgBefore} → {_player.DamageText}；"
                + $"防御 {defBefore} → {_player.Defense}；命中 {arBefore} → {_player.AttackRating}");
            Check("伤害（含武器基础）上升", _player.DamageText != dmgBefore, $"{dmgBefore} → {_player.DamageText}");

            var armor = factory.Create(47, 3, ItemQuality.Normal, new Rng(2));    // 皮甲：def 14-17
            item.AddToInventory(armor);
            var armorAnchor = FindAnchor(item, armor);
            var defBefore2 = _player.Defense;
            Check("装备防具", item.EquipFromInventory(armorAnchor), "anchor=" + armorAnchor);
            Console.WriteLine($"   装皮甲：防御 {defBefore2} → {_player.Defense}");
            Check("防御上升", _player.Defense > defBefore2, $"{defBefore2} → {_player.Defense}");

            // 词缀 att 的武器（AR 一定上升）—— 用固定 seed 确定性搜索一件
            var arBefore3 = _player.AttackRating;
            var attWeapon = FindWeaponWithAttAffix(out var seedUsed);
            Check("找到带 att 词缀的武器（确定性搜索）", attWeapon != null, "seed=" + seedUsed);
            if (attWeapon != null)
            {
                item.AddToInventory(attWeapon);
                var attAnchor = FindAnchor(item, attWeapon);
                // 换掉武器槽（斧 → 带 att 的武器）
                item.EquipFromInventory(attAnchor);
                Console.WriteLine($"   装「{attWeapon.name}」：命中 {arBefore3} → {_player.AttackRating}"
                    + $"（词缀 att 合计 +{AffixModSum(attWeapon, "att")}）");
                Check("带 att 词缀的武器使命中上升", _player.AttackRating > arBefore3, $"{arBefore3} → {_player.AttackRating}");
            }

            // ── 7. 腰带喝药 ──────────────────────────────────────────────────────
            Section("7) 腰带药水（数字键 1~4 的模块侧）");
            item.Reset();
            _player.SetLifeForTest(20);
            var potion = factory.Create(124, 1, ItemQuality.Normal, new Rng(3));   // 轻型治疗药水 price=75
            Check("药水生成成功", potion != null, potion == null ? "null" : potion.name);
            potion.count = 3;
            Check("放入腰带格 0", item.AddToBelt(potion, 0), "belt0=" + (item.Belt[0] != null ? item.Belt[0].count.ToString() : "-"));
            var lifeBefore = _player.Life;
            var beltBefore = item.Belt[0] != null ? item.Belt[0].count : 0;
            var drank = item.UseBeltSlot(0);
            var beltAfter = item.Belt[0] != null ? item.Belt[0].count : 0;
            Console.WriteLine($"   喝药：生命 {lifeBefore} → {_player.Life}；腰带格 0 计数 {beltBefore} → {beltAfter}");
            Check("喝药成功", drank, "UseBeltSlot=" + drank);
            Check("生命上升", _player.Life > lifeBefore, $"{lifeBefore} → {_player.Life}");
            Check("腰带计数 -1", beltAfter == beltBefore - 1, $"{beltBefore} → {beltAfter}");
            Check("非药水不能放腰带", !item.AddToBelt(factory.Create(2, 1, ItemQuality.Normal, new Rng(4)), 1),
                "武器入腰带应被拒（见 [WARN] [Item] 不是药水）");

            // ── 8. 任务链（核心）────────────────────────────────────────────────
            Section("8) 主线任务「邪恶洞穴」完整状态机");
            item.Reset();
            _map.Area = AreaId.Town;                     // 在城里接任务（此时洞穴还没生成 ⇒ 洞内 0 只）
            _monster.SeedDen(0);
            _monster.BloodMoorAlive = 3;
            quest.Reset();
            Check("初始状态 = NotStarted", quest.DenOfEvil == QuestState.NotStarted, quest.DenOfEvil.ToString());
            Check("未接取时不可交付", !quest.CanTurnInDen, "CanTurnInDen=" + quest.CanTurnInDen);
            _player.AddSkillPoint(-_player.SkillPoints); // 清零技能点便于观察 +1

            quest.AcceptDen();
            Check("接取后状态 = InProgress", quest.DenOfEvil == QuestState.InProgress, quest.DenOfEvil.ToString());
            Console.WriteLine($"   接取后：required={quest.Get(1).required}（城里还没进洞 ⇒ 0）；DenRemaining={quest.DenRemaining}");
            Check("城里接取时 required=0（未进洞不记数）", quest.Get(1).required == 0, "required=" + quest.Get(1).required);
            Check("required=0 时不可交付", !quest.CanTurnInDen, "CanTurnInDen=" + quest.CanTurnInDen);
            quest.AcceptDen();                            // 重复接取
            Check("重复接取被忽略（状态不变）", quest.DenOfEvil == QuestState.InProgress, "见 [WARN] [Quest] 重复接取");

            // 只认洞穴：在血腥荒野报击杀 ⇒ 剩余/进度/状态三者都不该动
            _map.Area = AreaId.BloodMoor;
            var denBeforeMoor = quest.DenRemaining;
            var progBeforeMoor = quest.Get(1).progress;
            quest.NotifyMonsterKilled(1001);
            Check("野外的击杀不计入（DenRemaining 不变）", quest.DenRemaining == denBeforeMoor,
                $"DenRemaining {denBeforeMoor}→{quest.DenRemaining}");
            Check("野外的击杀不计入（progress 不变）", quest.Get(1).progress == progBeforeMoor,
                $"progress {progBeforeMoor}→{quest.Get(1).progress}");
            Check("野外击杀也不会把状态推进到可交付", quest.DenOfEvil == QuestState.InProgress,
                "state=" + quest.DenOfEvil);
            Check("野外击杀有可读日志（不是洞穴）", _log.Contains("Quest", "不是洞穴"), "见 [INFO] [Quest] NotifyMonsterKilled");

            // 进入洞穴：怪物在这一刻生成（5 只）⇒ 记 required
            _map.Area = AreaId.DenOfEvil;
            _monster.SeedDen(5);
            Game.Event.Emit(Events.AreaChanged, AreaId.DenOfEvil);
            Check("进洞后记录 required = 5", quest.Get(1).required == 5, "required=" + quest.Get(1).required);
            Console.WriteLine($"   进洞：required={quest.Get(1).required} DenRemaining={quest.DenRemaining} "
                + $"progress={quest.Get(1).progress} state={quest.DenOfEvil}");

            for (var i = 0; i < 5; i++)
            {
                _monster.KillOneInDen();
                quest.NotifyMonsterKilled(1000 + i);
                Console.WriteLine($"   击杀 {i + 1}/5：DenRemaining={quest.DenRemaining} "
                    + $"progress={quest.Get(1).progress}/{quest.Get(1).required} state={quest.DenOfEvil}");
            }
            Check("清光后 DenRemaining = 0", quest.DenRemaining == 0, "DenRemaining=" + quest.DenRemaining);
            Check("清光后状态 = ReadyToTurnIn", quest.DenOfEvil == QuestState.ReadyToTurnIn, quest.DenOfEvil.ToString());
            Check("清光后可交付 CanTurnInDen = true", quest.CanTurnInDen, "CanTurnInDen=" + quest.CanTurnInDen);
            Check("progress = required = 5", quest.Get(1).progress == 5 && quest.Get(1).required == 5,
                $"{quest.Get(1).progress}/{quest.Get(1).required}");

            var spBefore = _player.SkillPoints;
            _map.Area = AreaId.Town;                      // 回城交付
            quest.TurnInDen();
            Console.WriteLine($"   交付：state={quest.DenOfEvil} 技能点 {spBefore} → {_player.SkillPoints} "
                + $"rewardClaimed={quest.Get(1).rewardClaimed}");
            Check("交付后状态 = Done", quest.DenOfEvil == QuestState.Done, quest.DenOfEvil.ToString());
            Check("交付奖励：技能点 +1", _player.SkillPoints == spBefore + 1, $"{spBefore} → {_player.SkillPoints}");
            Check("交付后不可再交", !quest.CanTurnInDen, "CanTurnInDen=" + quest.CanTurnInDen);
            var spAfter = _player.SkillPoints;
            quest.TurnInDen();
            Check("重复交付不发第二次奖励", _player.SkillPoints == spAfter, "技能点=" + _player.SkillPoints);
            Check("重复交付发 QuestTurnInDenied", _log.Contains("Quest", "交付被拒"), "见 [WARN] [Quest] 交付被拒");

            // ── 9. NPC 对话 4 阶段 ──────────────────────────────────────────────
            Section("9) NPC 对话随任务阶段变化（阿卡拉 4 段）");
            Check("5 个 NPC 定义齐全", npc.All.Count == 5, "count=" + npc.All.Count);
            Check("站位来自 IMapModule.NpcPoints", npc.All[0].gridX == 10 && npc.All[4].gridX == 18,
                $"阿卡拉=({npc.All[0].gridX},{npc.All[0].gridY}) 瓦瑞夫=({npc.All[4].gridX},{npc.All[4].gridY})");
            Check("角色分工正确（阿卡拉任务发布者 / 恰西铁匠+修理）",
                npc.Get(0).isQuestGiver && !npc.Get(0).isBlacksmith && npc.Get(2).isBlacksmith && npc.Get(2).canRepair,
                "akara.questGiver / charsi.blacksmith+repair");

            var texts = new List<string>();
            for (var stage = 0; stage < 4; stage++)
            {
                var state = (QuestState)stage;
                SetQuestStage(quest, state);                       // 通过公开流程把状态推到该阶段
                // ★ 片 T（修宿主崩在 :1075 的真因）：`SetQuestStage` 为把任务推到"进行中/可交付"
                //   会把 `_map.Area` 推到 **DenOfEvil**；而"NPC 只在城镇存在"是**产品特性**
                //   （agent-26 的城镇门禁 + 片 T 的 S-19「非城镇 ⇒ 不装配」）⇒ 洞里 `GetDialog(0)`
                //   合法地返回 null。本段判的是「任务阶段 → 台词」，**区域不是本段的变量** ⇒
                //   取台词前把场景钉回城镇（与下面 §9b 的 `_map.Area = AreaId.Town` 同一口径）。
                _map.Area = AreaId.Town;
                var d = npc.GetDialog(0);
                Check($"阶段 {state}：城镇内能取到阿卡拉对话（非 null）", d != null,
                    d == null ? "null —— 原因见上一行 [Npc] Warn" : "ok");
                if (d == null) { texts.Add("<null>"); continue; }   // ⛔ 不许 NRE 崩宿主
                texts.Add(d.text);
                Console.WriteLine($"   阶段 {state}：{d.text}");
            }
            // ★ 本片改口径：台词换成**原版串表**的原句（原有出处逐条在断言里点名）；
            //   「已完成」与「可交付」**故意共用**同一句（原版串表里阿卡拉在任务 1 之后没有独立的
            //   "已完成"台词 —— `A1Q1SuccessfulAkara` 就是她关于本任务的最后一句），
            //   所以 distinct 是 **3/4** 而不是 4/4；下面把它写成显式断言，防止以后有人"顺手"编一句新词。
            Check("四态台词 = 原版串（未接取=64 / 进行中=71 / 可交付&已完成=76，故 distinct=3）",
                DistinctCount(texts) == 3
                && texts[2] == texts[3]                                  // 可交付 == 已完成（原版只有一句）
                && texts[0].Contains("在荒地中有一個極度邪惡的地方")        // 串 64 A1Q1InitAkara
                && texts[1].Contains("除非你殺死這個洞窟中的所有惡魔")      // 串 71 A1Q1EarlyReturnAkara
                && texts[2].Contains("你已經清除了洞窟中的邪惡"),           // 串 76 A1Q1SuccessfulAkara
                "distinct=" + DistinctCount(texts) + "/4（3 = 已完成与可交付共用原版串 76）");
            Check("对话选项下标 0 恒为关闭（文案 = 原版串 3394 `NPCMenuLeave`「離開」）",
                npc.GetDialog(0).options.Count > 0 && npc.GetDialog(0).options[0] == "離開",
                "options[0]=" + npc.GetDialog(0).options[0]);
            Check("可交付阶段 canTurnInQuest = true", CanTurnInFlag(npc, quest), "见上");
            Check("非任务 NPC（瓦瑞夫）也能对话", npc.GetDialog(4) != null && !npc.GetDialog(4).hasShop,
                "warriv.hasShop=" + npc.GetDialog(4).hasShop);

            // ★ 片 T：把"洞里取不到阿卡拉"从**宿主崩溃**改成**判过的行** —— 这是产品特性（城镇门禁 +
            //   S-19 非城镇不装配），不是缺陷：洞里必须**拿不到**台词，否则就是"洞里误开阿卡拉对话"。
            _map.Area = AreaId.DenOfEvil;
            Check("★非城镇：`GetDialog(0)` 返回 null（不编台词 ⇒ 洞里拿不到阿卡拉台词）",
                npc.GetDialog(0) == null, "GetDialog(0) = null（原因由 [Npc] 的 WarnOnce 点名）");
            Check("★非城镇：`Interact(0)` 被城镇门禁拒绝", !npc.Interact(0), "Interact(0) = false");
            Check("★非城镇：`Get(0)` 返回 null（定义存在、只是本区域不装配）",
                npc.Get(0) == null, "Get(0) = null");
            _map.Area = AreaId.Town;                                  // 复位：下面的 §9b 依赖城镇场景

            // ★ 片 T（同族穷举）：**台词表本身不许有空格** —— 5 NPC × 4 任务阶段 = 20 格。
            //   这样"某状态没有台词"就永远不会以 `null` 的形式出现在 `GetDialog` 上
            //   （`GetDialog` 的唯一 null 出口 = `Get` 拿不到定义，见其 ★ 注）。
            var textGaps = 0;
            var gapWhere = "";
            for (var id = 0; id < 5; id++)
            {
                for (var s = 0; s < 4; s++)
                {
                    if (!string.IsNullOrWhiteSpace(
                            Diablo2.Module.Npc.NpcDialog.TextOf(id, (QuestState)s))) continue;
                    textGaps++;
                    gapWhere += $" n{id}/{(QuestState)s}";
                }
            }
            Check("★同族穷举：5 NPC × 4 任务阶段 = 20 格台词全部非空（无静默空格）",
                textGaps == 0, "空格=" + textGaps + gapWhere);

            // 9b) 「点击 NPC → 走过去 → 自动对话」（当前无人发 `NpcInteractRequest`，本模块用 `MoveCommand` 兜底）
            //
            // ★ 2026 修正（**宿主场景过期，不是产品缺陷**）：agent-26 给 `NpcModule` 加了**城镇门禁**
            //   （`InTownForNpc()`：只有 `IMapModule.Area == AreaId.Town` 才允许交互/自动对话 —— 修的是
            //   "进邪恶洞穴后 MoveCommand 落点靠近 (0,0) 会误开阿卡拉对话" 那条实机缺陷，见验收表 #38）。
            //   而本宿主在上面第 7~8 节把 `_map.Area` 推到了 BloodMoor / DenOfEvil 就没再复位 ⇒
            //   到这里区域仍是洞穴 ⇒ `FindNearest/Interact` 全部被门禁拦掉、下面两条断言必然失败。
            //   NPC 站位（阿卡拉 (10,10) / 瓦瑞夫 (18,10)）本来就是**城镇**坐标 ⇒ 这里复位回 Town 才是对场景。
            _map.Area = AreaId.Town;
            Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpenRecorder);

            _player.TeleportTo(new Vector2Int(30, 30));
            Game.Event.Emit(Events.MoveCommand, new Vector2Int(10, 10));      // 阿卡拉站位
            npc.Tick(0.1f);
            Check("点了 NPC 但人还在远处 ⇒ 尚未弹对话", _dialogOpens == 0, "DialogOpen=" + _dialogOpens);

            _player.TeleportTo(new Vector2Int(10, 9));                        // 走到阿卡拉旁（距离 1 ≤ TalkRange）
            npc.Tick(0.1f);
            Check("走到 NPC 旁 ⇒ 自动弹对话（DialogOpen，npcId=0）", _dialogOpens == 1 && _lastDialogNpc == 0,
                $"DialogOpen={_dialogOpens} npcId={_lastDialogNpc}");

            var openings = _dialogOpens;
            npc.Tick(0.1f);
            Check("已在对话中 ⇒ 不重复弹（幂等，不刷面板）", _dialogOpens == openings, "DialogOpen=" + _dialogOpens);

            Game.Event.Emit(Events.NpcInteractRequest, 4);                    // 别人若发契约事件，也走同一条路
            Check("NpcInteractRequest(瓦瑞夫) ⇒ 弹对话（契约入口可用）", _dialogOpens == 2 && _lastDialogNpc == 4,
                $"DialogOpen={_dialogOpens} npcId={_lastDialogNpc}");

            // ── 10. 商店：买 / 钱不够 / 卖 / 修理 ────────────────────────────────
            Section("10) 商店（恰西：买卖 + 修理）");
            item.Reset();
            _player.SetGoldForTest(100000);
            var shop = npc.GetShop(2);
            Check("恰西商店已上架", shop != null && shop.stock.Count > 0, shop == null ? "null" : ("stock=" + shop.stock.Count));
            Check("商店带玩家金币与可卖清单", shop != null && shop.playerGold == 100000 && shop.canRepair, "canRepair=" + (shop != null && shop.canRepair));

            var goldBeforeBuy = _player.Gold;
            var anchorsBeforeBuy = CountAnchors(item);
            var entry0 = shop.stock[0];
            var bought = npc.Buy(2, 0, 1);
            Console.WriteLine($"   买入「{entry0.name}」：金币 {goldBeforeBuy} → {_player.Gold}；背包锚点 {anchorsBeforeBuy} → {CountAnchors(item)}");
            Check("买入成功", bought, "Buy=" + bought);
            Check("金币减少 = 售价", _player.Gold == goldBeforeBuy - entry0.price, $"{goldBeforeBuy} → {_player.Gold}（售价 {entry0.price}）");
            Check("背包 +1", CountAnchors(item) == anchorsBeforeBuy + 1, $"{anchorsBeforeBuy} → {CountAnchors(item)}");

            var goldPoor = _player.Gold;
            _player.SetGoldForTest(0);
            var buyPoor = npc.Buy(2, 1, 1);
            Check("钱不够 ⇒ 买入失败且金币不变", !buyPoor && _player.Gold == 0, "gold=" + _player.Gold);
            Check("钱不够有可读日志", _log.Contains("Npc", "金币不足"), "见 [WARN] [Npc]");
            _player.SetGoldForTest(goldPoor);

            var sellAnchor = FirstAnchor(item);
            var sellItem = AnchorItem(item, sellAnchor);
            var goldBeforeSell = _player.Gold;
            var sold = npc.Sell(2, sellAnchor);
            Console.WriteLine($"   卖出「{(sellItem != null ? sellItem.name : "-")}」：金币 {goldBeforeSell} → {_player.Gold}");
            Check("卖出成功", sold, "Sell=" + sold);
            Check("金币增加", _player.Gold > goldBeforeSell, $"{goldBeforeSell} → {_player.Gold}");

            item.Reset();
            _player.SetGoldForTest(10000);
            var worn = factory.Create(47, 3, ItemQuality.Normal, new Rng(9), true);   // 带磨损的皮甲
            Check("生成带磨损的装备", worn != null && worn.durability < worn.maxDurability,
                worn == null ? "null" : $"dur={worn.durability}/{worn.maxDurability}");
            item.AddToInventory(worn);
            var repairCostPreview = item.GetRepairAllCost();
            var goldBeforeRepair = _player.Gold;
            var repairCost = npc.Repair(2, -1);
            Console.WriteLine($"   修理：预估 {repairCostPreview}，实收 {repairCost}；金币 {goldBeforeRepair} → {_player.Gold}；"
                + $"耐久 {worn.durability}/{worn.maxDurability}");
            Check("修理费 > 0", repairCostPreview > 0 && repairCost > 0, $"cost={repairCost}");
            Check("修理扣金币", _player.Gold == goldBeforeRepair - repairCost, $"{goldBeforeRepair} → {_player.Gold}");
            Check("修理后耐久回满", worn.durability == worn.maxDurability, $"{worn.durability}/{worn.maxDurability}");

            // ── 11. 存档 ─────────────────────────────────────────────────────────
            Section("11) 存档（多角色 / 往返一致 / 删除 / 版本降级）");
            item.Reset();
            _player.SetGoldForTest(1234);
            _player.AddSkillPoint(2);
            var g1 = factory.Create(2, 12, ItemQuality.Magic, new Rng(11));
            item.AddToInventory(g1);
            var b1 = factory.Create(124, 1, ItemQuality.Normal, new Rng(12));
            b1.count = 2;
            item.AddToBelt(b1, 0);
            quest.Reset();
            quest.AcceptDen();

            Check("SaveModule.Ready = true（Game.Setting 已接入）", save.Ready, "Ready=" + save.Ready);
            var savedOk = save.Save();
            Check("Save() 成功（无参，走 Player/Item/Quest/Skill）", savedOk, savedOk ? "ok" : save.LastError);

            // ★ agent-38 补（`# direct-fix:`）：agent-37 的引擎下沉 A6 把角色档从
            //   `Game.Setting` 的 `char/{名}` 键改成**一角色一文件** ——
            //   `<SettingDir>/saves/<名>.json`，读写走引擎 `CloverEngine.FileSlotStore`
            //   （口径见 `Module/Save/SaveModule.cs:3-11`）。本宿主的存档断言仍按旧键读 ⇒ 必然 0 字节
            //   （verify.ps1 的 offline-hosts 因此一直 FAIL）。改成读**槽位文件**，断言强度不降：
            //   仍要求"文件存在 + 非空 + 含 quests/inventory 字段 + 两次保存等价"。
            //   `char/index`（创建先后索引）**仍在 Game.Setting**（业务语义，A6 未动它）。
            var saveRoot = Game.Config != null && !string.IsNullOrEmpty(Game.Config.SettingDir)
                ? Game.Config.SettingDir
                : "setting";
            var saveFile = System.IO.Path.Combine(saveRoot, "saves", "CheckHero.json");
            var json1 = System.IO.File.Exists(saveFile) ? System.IO.File.ReadAllText(saveFile) : null;
            Check("存档已落盘到 <SettingDir>/saves/CheckHero.json", !string.IsNullOrEmpty(json1),
                "path=" + saveFile + " bytes=" + (json1 == null ? 0 : json1.Length));
            Check("存档含任务与背包字段", json1 != null && json1.Contains("\"quests\"") && json1.Contains("\"inventory\""), "见 JSON");
            Check("新档不再写旧键 char/CheckHero（A6 起档在槽位文件里，旧键只用于懒迁移）",
                !_setting.HasKey(GameConst.SaveKeyPrefix + "CheckHero"),
                "oldKeyPresent=" + _setting.HasKey(GameConst.SaveKeyPrefix + "CheckHero"));
            Check("索引键已写 char/index", _setting.HasKey(GameConst.SaveIndexKey), "peek=" + _setting.Peek(GameConst.SaveIndexKey));

            var data = save.Load("CheckHero");
            Check("Load() 返回存档", data != null, data == null ? "null" : data.name);
            Check("读档带回金币/背包/任务", data != null && data.gold == 1234 && CountAnchorsInSave(data) == 1
                && data.quests.Count == 1 && data.quests[0].state == QuestState.InProgress,
                data == null ? "-" : $"gold={data.gold} anchors={CountAnchorsInSave(data)} quest={data.quests.Count}");
            Check("读档带回技能（从 ISkillModule 反查）", data != null && data.skillIds.Count == 2 && data.buttonSkills[1] != -1,
                data == null ? "-" : $"skills={data.skillIds.Count} button1={data.buttonSkills[1]}");

            save.Save(data);
            var json2 = System.IO.File.Exists(saveFile) ? System.IO.File.ReadAllText(saveFile) : null;
            Check("Save → Load → Save 两次 JSON 等价（忽略时间戳）", Normalize(json1) == Normalize(json2),
                $"len {json1.Length} vs {json2.Length}");
            string perr;
            var reparsed = SaveJson.TryParse(json1, out perr);
            Check("Write(Parse(json)) == json（逐字节恒等）", reparsed != null && SaveJson.Write(reparsed) == json1,
                perr ?? "byte-identical");
            Check("存档里没有 null 元素（inventory 40 格写全）", data.inventory.Count == GameConst.InventoryCellCount,
                "inventory=" + data.inventory.Count + " belt=" + data.belt.Count + " equip=" + data.equip.Count);

            Check("Exists(\"CheckHero\") = true", save.Exists("CheckHero"), "Exists=" + save.Exists("CheckHero"));
            Check("HasAny = true", save.HasAny, "HasAny=" + save.HasAny);
            Check("List() 含 CheckHero", save.List().Contains("CheckHero"), string.Join(",", save.List()));
            var beforeDelete = save.ListAll().Count;
            Check("ListAll() 有 1 个角色", beforeDelete == 1, "count=" + beforeDelete);
            Check("删除角色成功", save.Delete("CheckHero"), "Delete ok");
            Check("删除后 ListAll 少一个", save.ListAll().Count == beforeDelete - 1, "count=" + save.ListAll().Count);
            Check("删除后 HasAny = false", !save.HasAny, "HasAny=" + save.HasAny);
            Check("重复删除返回 false", !save.Delete("CheckHero"), "第二次 Delete=false");

            // 11b) 与 agent-05 `CharRoster` 的**调用形态逐条对齐**（Flow 侧不改一行代码）
            //      CharRoster: HasAny / ListAll() / Load(name) / Exists(name) / Save(data) / Delete(name)
            //      AppFlow   : Save() / LastError
            var roster = new CharacterSave
            {
                version = GameConst.SaveVersion, name = "RosterCheck", cls = PlayerClass.Sorceress,
                level = 1, str = 10, dex = 25, vit = 10, eng = 35, life = 40, mana = 35, stamina = 74,
                gold = 0, areaId = (int)AreaId.Town, mapSeed = 4321,
            };
            Check("CharRoster.Create → Save(CharacterSave) 返回 true", save.Save(roster), "Save(data)=true");
            Check("CharRoster.Persisted → HasAny", save.HasAny, "HasAny=" + save.HasAny);
            Check("CharRoster.Exists(name)", save.Exists("RosterCheck"), "Exists=True");
            var listed = save.ListAll();
            Check("CharRoster.ListAll() 返回非 null 且含该角色", listed != null && listed.Count == 1
                && listed[0].name == "RosterCheck" && listed[0].cls == PlayerClass.Sorceress,
                listed == null ? "null" : ("count=" + listed.Count + " name=" + listed[0].name));
            Check("CharRoster.Load(name) 返回存档", save.Load("RosterCheck") != null, "Load != null");
            Check("CharRoster.Delete(name) 返回 true", save.Delete("RosterCheck"), "Delete=true");
            Check("AppFlow 用的 Save()/LastError 形态存在", save.LastError != null, "LastError=\"" + save.LastError + "\"");

            // 版本不符 ⇒ 降级 + Warn（不许崩）
            var legacy = "{\"version\":99,\"name\":\"OldHero\",\"cls\":2,\"level\":7,\"gold\":55}";
            _setting.Set(GameConst.SaveKeyPrefix + "OldHero", legacy);
            var legacyData = save.Load("OldHero");
            Check("版本不符仍能读（降级，不崩）", legacyData != null && legacyData.level == 7 && legacyData.gold == 55,
                legacyData == null ? "null" : $"lv={legacyData.level} gold={legacyData.gold} version={legacyData.version}");
            Check("版本不符有 Warn 日志", _log.Contains("Save", "版本"), "见 [WARN] [Save]");
            var emptyData = save.Load("NoSuchHero");
            Check("读不存在的角色 ⇒ null（不抛异常）", emptyData == null, "null");
            Check("损坏 JSON ⇒ null + LastError", BreakJsonRoundTrip(save), "见 [ERROR] [Save]");

            // ── 12. ★ 本轮新增：双武器组（原版 W 键切换；T0 判据缺口 2）──────────────
            //   断言打在**真实** `ItemModule`（不是桩）上；数据面 = `Equipment` 的武器槽（最多 2 件 = 两套组）。
            Section("12) 双武器组（真实 ItemModule：切换 / 主手互换 / 切回 / 存档往返 / 旧档兼容）");
            item.Reset();
            var itemMod = (Diablo2.Module.Item.ItemModule)item;

            var wA = factory.Create(2, 12, ItemQuality.Normal, new Rng(71));   // 斧
            var wB = factory.Create(3, 12, ItemQuality.Normal, new Rng(72));   // 大斧
            Check("两把武器生成成功（不同 itemId、不同伤害、均无词缀）",
                wA != null && wB != null && wA.itemId != wB.itemId
                && (wA.dmgMin != wB.dmgMin || wA.dmgMax != wB.dmgMax)
                && wA.affixes.Count == 0 && wB.affixes.Count == 0,
                $"{wA?.name}({wA?.dmgMin}-{wA?.dmgMax}) vs {wB?.name}({wB?.dmgMin}-{wB?.dmgMax})");

            item.AddToInventory(wA);
            Check("装上第 1 把武器（Ⅰ组）", item.EquipFromInventory(FindAnchor(item, wA)),
                "equip=" + item.Equipment.Count);
            Check("只有 1 件武器 ⇒ 武器组 = 1 套、生效组 = 0（Ⅰ）",
                itemMod.WeaponGroupCount == 1 && itemMod.ActiveWeaponGroup == 0,
                $"count={itemMod.WeaponGroupCount} active={itemMod.ActiveWeaponGroup}");

            // ① 只有一把武器时按 W **不该**把武器卸下来（原版的"切到空手"本项目不做）
            var swapRejected = !itemMod.SwapWeaponGroup();
            Check("只有 1 件武器 ⇒ 切换被拒（状态不变）",
                swapRejected && itemMod.ActiveWeaponGroup == 0 && item.Equipment.Count == 1,
                $"active={itemMod.ActiveWeaponGroup} equipment={item.Equipment.Count}");
            Check("切换被拒留了可定位 Warn 日志", _log.Contains("Item", "无可切换的目标组"), "见 [WARN] [Item]");

            item.AddToInventory(wB);
            Check("装上第 2 把武器（Ⅱ组）", item.EquipFromInventory(FindAnchor(item, wB)),
                "equip=" + item.Equipment.Count);
            // ★ 双武器组的"另一半"：`IItemModule.Equipment` 现在是**生效集**（只含生效组那把武器）
            //   ⇒ `CombatModule.GetWeaponDamage` 不再把两把武器的伤害相加（原版只有当前那套生效）。
            //   而"两套都存得住"由 `WriteTo` 的全集保证（下面存档断言里按 JSON 逐件核对）。
            Check("两把武器都装着（组数 = 2），但**生效集只含生效组那 1 把**",
                itemMod.WeaponGroupCount == 2 && item.Equipment.Count == 1,
                $"组数={itemMod.WeaponGroupCount} 生效集={item.Equipment.Count}（全集仍 2 把，写档写全集）");
            Check("生效组自动跟到刚装上的那把（=1 ⇒ Ⅱ）", itemMod.ActiveWeaponGroup == 1,
                "active=" + itemMod.ActiveWeaponGroup);

            var eq1 = item.Snapshot().equip;
            Check("装备载荷里只有**生效组**那把武器（非生效组不进载荷）",
                WeaponsIn(eq1).Count == 1 && WeaponsIn(eq1)[0].itemId == wB.itemId,
                $"payload weapons={WeaponsIn(eq1).Count} 名={weaponsNames(eq1)}");
            Check("派生的近战伤害 = Ⅱ组那把（词缀/伤害只算生效组）",
                _player.DamageText == ExpectedDmgTextOf(wB),
                $"DamageText={_player.DamageText} 期望={ExpectedDmgTextOf(wB)}");

            // ② W 切换 ⇒ 主手/副手互换
            Check("切换武器组成功（Ⅱ → Ⅰ）", itemMod.SwapWeaponGroup(), "SwapWeaponGroup=true");
            Check("切换后生效组 = 0（Ⅰ）", itemMod.ActiveWeaponGroup == 0, "active=" + itemMod.ActiveWeaponGroup);
            var eq2 = item.Snapshot().equip;
            Check("切换后载荷里的武器换成另一把（主手/副手互换）",
                WeaponsIn(eq2).Count == 1 && WeaponsIn(eq2)[0].itemId == wA.itemId,
                $"payload weapons={WeaponsIn(eq2).Count} 名={weaponsNames(eq2)}");
            Check("切换后伤害随之变成 Ⅰ组那把（数值面真的生效）",
                _player.DamageText == ExpectedDmgTextOf(wA),
                $"DamageText={_player.DamageText} 期望={ExpectedDmgTextOf(wA)}");
            Check("切换日志写清了两个组名", _log.Contains("Item", "[SwapWeapon] 武器组 Ⅱ → Ⅰ"),
                "见 [INFO] [Item] [SwapWeapon]");

            // ③ 再切回去 ⇒ 恢复原样
            Check("再切一次回到 Ⅱ（幂等可逆）", itemMod.SwapWeaponGroup() && itemMod.ActiveWeaponGroup == 1,
                "active=" + itemMod.ActiveWeaponGroup);
            var eq3 = item.Snapshot().equip;
            Check("切回后载荷与切换前一致（同一把 Ⅱ组武器、伤害一致）",
                WeaponsIn(eq3).Count == 1 && WeaponsIn(eq3)[0].itemId == wB.itemId
                && _player.DamageText == ExpectedDmgTextOf(wB),
                $"名={weaponsNames(eq3)} DamageText={_player.DamageText}");

            // ④ 存档往返一致（生效组 Ⅱ ⇒ 存 ⇒ 清空 ⇒ 读回仍是 Ⅱ）
            const string wgName = "WGCheckHero";
            _player.Name = wgName;
            Check("存武器组档成功", save.Save(), save.LastError);
            var wgFile = System.IO.Path.Combine(saveRoot, "saves", wgName + ".json");
            var wgJson = System.IO.File.Exists(wgFile) ? System.IO.File.ReadAllText(wgFile) : null;
            Check("存档 JSON 里 activeWeaponIndex = 1（生效组 Ⅱ）",
                wgJson != null && wgJson.Contains("\"activeWeaponIndex\":1"),
                "字段片段=" + FieldSnippet(wgJson, "activeWeaponIndex"));
            // ★ 写档写**全集**（两套武器都落盘）—— 否则"切回来"就切不回另一把了。
            Check("存档 JSON 里**两把武器都在**（写档写全集，切组才切得回来）",
                wgJson != null && wgJson.Contains("\"itemId\":" + wA.itemId) && wgJson.Contains("\"itemId\":" + wB.itemId),
                $"itemId {wA.itemId} 与 {wB.itemId} 都在存档文本里");
            item.Reset();
            Check("Reset 后武器组清空（下面的恢复只能来自存档）",
                itemMod.ActiveWeaponGroup == 0 && itemMod.WeaponGroupCount == 0,
                $"active={itemMod.ActiveWeaponGroup} groups={itemMod.WeaponGroupCount}");
            var wgData = save.Load(wgName);
            Check("读档往返一致：生效组 = 1（Ⅱ）且两把武器都回来了",
                wgData != null && itemMod.ActiveWeaponGroup == 1 && itemMod.WeaponGroupCount == 2,
                wgData == null ? "null" : $"active={itemMod.ActiveWeaponGroup} groups={itemMod.WeaponGroupCount}");
            Check("读档后载荷仍是 Ⅱ组那把", WeaponsIn(item.Snapshot().equip).Count == 1
                && WeaponsIn(item.Snapshot().equip)[0].itemId == wB.itemId,
                "名=" + weaponsNames(item.Snapshot().equip));
            Check("读档摘要写清了武器组口径", _log.Contains("Item", "读档恢复武器组"), "见 [INFO] [Item]");
            save.Delete(wgName);

            // ⑤ 向后兼容：**旧档没有 activeWeaponIndex 字段**（§11 的 `legacy` 那份就是）⇒ 默认 0、不崩
            Check("旧档（无 activeWeaponIndex 字段）解析默认 = 0（Ⅰ组）",
                legacyData != null && legacyData.activeWeaponIndex == 0,
                legacyData == null ? "null" : $"activeWeaponIndex={legacyData.activeWeaponIndex}");
            item.Reset();
            var legacyLoadOk = true;
            try { item.LoadFrom(legacyData); }
            catch (System.Exception ex) { legacyLoadOk = false; Console.WriteLine("   " + ex.GetType().Name + ": " + ex.Message); }
            Check("旧档灌进 ItemModule 不抛异常且生效组取默认 0",
                legacyLoadOk && itemMod.ActiveWeaponGroup == 0 && itemMod.WeaponGroupCount == 0,
                $"threw={!legacyLoadOk} active={itemMod.ActiveWeaponGroup} groups={itemMod.WeaponGroupCount}");
            Check("旧档兼容口径留了 Info 日志（旧档无该字段 ⇒ 默认 Ⅰ组）",
                _log.Contains("Item", "旧档没有该字段"), "见 [INFO] [Item] [SwapWeapon] 读档恢复武器组");

            // ── 13. ★ 本轮新增：死亡扣金币 10%（真实 `PlayerModule.Kill`；T0 判据缺口 1）──
            //   为什么必须用**真实** PlayerModule：金币的唯一归属是 `IPlayerModule`（本文件的 StubPlayer
            //   的 Kill() 只把生命置 0）—— 打在桩上等于没测。
            Section("13) 死亡扣金币 10%（真实 PlayerModule.Kill；边界 0 / 5 / 12345 / 连续两次）");
            var real = new Diablo2.Module.Player.PlayerModule();
            real.CreateNew(PlayerClass.Barbarian, "GoldHero");

            real.Kill();
            Check("边界①金币 0 ⇒ 死亡不扣，仍为 0（不出现负数）", real.Gold == 0 && real.IsDead,
                $"gold={real.Gold} dead={real.IsDead}");
            real.Revive();

            real.AddGold(5);
            real.Kill();
            Check("边界②金币 5 ⇒ 损失 floor(5/10)=0 ⇒ 仍为 5", real.Gold == 5, "gold=" + real.Gold);
            real.Revive();

            real.AddGold(12340);                              // 5 + 12340 = 12345
            Check("凑到边界③的 12345", real.Gold == 12345, "gold=" + real.Gold);
            real.Kill();
            Check("边界③金币 12345 ⇒ 11111（损失 1234 = floor(12345×10%)）", real.Gold == 11111,
                "gold=" + real.Gold);
            real.Revive();

            real.Kill();
            Check("连续第 2 次死亡 ⇒ 10000（损失 1111 = floor(11111×10%)；两次连续扣）",
                real.Gold == 10000, "gold=" + real.Gold);
            Check("扣后不为负（金币 ≥ 0 恒成立）", real.Gold >= 0, "gold=" + real.Gold);
            real.Revive();
            Check("复活**不**扣金币（复活 ≠ 死亡）", real.Gold == 10000, "gold=" + real.Gold);

            Check("死亡扣金币口径只报一次（tag = T0GAP，5 次死亡 1 行）",
                _log.CountOf("T0GAP", "[T0GAP]") == 1, "T0GAP 行数=" + _log.CountOf("T0GAP", "[T0GAP]"));
            Check("口径行写清了取整口径与「不会为负」的论证",
                _log.Contains("T0GAP", "向下取整") && _log.Contains("T0GAP", "不会为负"),
                "见 [INFO] [T0GAP]");

            // ── 15. ★ 起始装备（`start_item_c` ← 官方 charstats.txt；用户报「创建角色后徒手打不动怪」）──
            //   断言打在**生产实现** `Module/Item/StartItems.cs` 上；期望值由本宿主**自己解析官方
            //   `charstats.txt`**（不读我们自己的配表）⇒ 判的是"我们抽的表 == 官方表"这条**过程**，
            //   不是"函数返回 true"。
            Section("15) 起始装备（start_item_c：职业 → equip/inventory；与官方 charstats.txt 逐条对照）");

            var officialRows = ReadOfficialStartItems();
            Check("官方 charstats.txt 解析出 5 经典职业的起始装备 23 条", officialRows.Count == 23,
                "official=" + officialRows.Count);

            var startRows = Table.Tables.Default.StartItem.All();
            var oursRows = new List<string>();
            for (var i = 0; i < startRows.Count; i++)
            {
                var r = startRows[i];
                if (r == null) continue;
                oursRows.Add(r.Class + "|" + r.SlotIndex + "|" + r.Code + "|" + r.Loc + "|" + r.Count);
            }
            Check("start_item_c 行数 = 23（官方空位 code='0'/count='0' 已剔除）", oursRows.Count == 23,
                "ours=" + oursRows.Count);
            Check("start_item_c 与官方 charstats.txt 逐条一致（双向差集为空）",
                OnlyIn(officialRows, oursRows).Count == 0 && OnlyIn(oursRows, officialRows).Count == 0,
                DiffText(officialRows, oursRows));
            Check("start_item_c.src_line 指回该职业在官方 txt 的真实行号（2/3/4/5/6）",
                SrcLines(startRows) == "2,3,4,5,6", "srcLines=" + SrcLines(startRows));

            var totalEquip = 0;
            var nonWeaponShieldSlots = 0;
            for (var clsId = 1; clsId <= 5; clsId++)
            {
                var cls = (PlayerClass)clsId;
                var sv = NewCharSave(cls);
                Diablo2.Module.Item.StartItems.Apply(sv);

                var exp = new List<string>();
                for (var i = 0; i < officialRows.Count; i++)
                {
                    var f = officialRows[i].Split('|');
                    if (int.Parse(f[0]) != clsId) continue;
                    exp.Add(f[2] + "x" + f[4]);
                }
                var got = PairsOfSave(sv);
                // ⚠️ 口径：不可堆叠品会被**拆成 count 格**（规格：`item_c.stackable == 0` ⇒ 占 count 格）
                // ⇒ 必须先按 code **汇总总量**再比，否则"1 叠 4 个"与"4 格各 1 个"会被误判为不等。
                Check($"{cls}: 装备+背包逐条等于官方期望（{exp.Count} 条 / 共 {sv.equip.Count + CountAnchorsInSave(sv)} 件）",
                    TotalsOf(exp) == TotalsOf(got), $"期望[{TotalsOf(exp)}] 实得[{TotalsOf(got)}]");

                string locTxt;
                Check($"{cls}: rarm→Weapon / larm→Shield 槽位映射正确",
                    LocSlotsOk(officialRows, clsId, sv, out locTxt), locTxt);

                var slots = EquipSlots(sv);
                for (var i = 0; i < slots.Count; i++)
                {
                    if (slots[i] != ItemSlot.Weapon && slots[i] != ItemSlot.Shield) nonWeaponShieldSlots++;
                }
                totalEquip += sv.equip.Count;
                Console.WriteLine($"   {cls}(class_c.id={clsId})：装备 {sv.equip.Count} 件[{EquipText(sv)}]；"
                    + $"背包锚点 {CountAnchorsInSave(sv)} 个[{InvText(sv)}]；"
                    + $"武器={WeaponText(sv)}");

                Check($"{cls}: 背包格位自洽（占格 = gridW×gridH 且 anchorIndex 指回锚点）",
                    InventoryBlocksOk(sv), "inventory.Count=" + sv.inventory.Count);
            }
            Check("起始装备合计 8 件（Amazon2 / Sorceress1 / Necromancer1 / Paladin2 / Barbarian2）",
                totalEquip == 8, "equip=" + totalEquip);
            Check("起始装备里没有防具（官方 charstats 就没有衣服/鞋子/头盔 ⇒ 不许自己加）",
                nonWeaponShieldSlots == 0, "非武器/盾槽的装备件数=" + nonWeaponShieldSlots);
            Check("5 职业的起始 code 都能在 item_c 里查到（没有「查不到」日志）",
                !_log.Contains("Item", "在 `item_c` 里查不到"), "见 [WARN] [Item]");

            // 15b) **生产路径**（`AppFlow.OnCharCreateSubmit` 走的那条）：
            //      `IItemModule.LoadFrom(新鲜草稿档)` 由 ItemModule 自己补齐 → `WriteTo` 回写进档；
            //      再喂一次不许重复发（幂等）。
            item.Reset();
            var bareText = _player.DamageText;
            var barbSave = NewCharSave(PlayerClass.Barbarian);
            Check("草稿档被判为「刚创出来、还没发过装备」",
                Diablo2.Module.Item.StartItems.IsFreshDraft(barbSave), "IsFreshDraft=true");
            item.LoadFrom(barbSave);                       // ← 生产路径入口（不是直接调 StartItems）
            Check("LoadFrom（新角色草稿档）就地补齐起始装备：装备 2 件 / 背包 6 锚点",
                item.Equipment.Count == 2 && CountAnchors(item) == 6,
                $"module.Equipment={item.Equipment.Count} anchors={CountAnchors(item)}");
            item.WriteTo(barbSave);
            Check("WriteTo 回写后草稿档里有装备与背包（写档之前就落好）",
                barbSave.equip.Count == 2 && CountAnchorsInSave(barbSave) == 6,
                $"equip={barbSave.equip.Count} anchors={CountAnchorsInSave(barbSave)}");
            Check("发过装备后不再是草稿档（⇒ 进图读档不会重复发）",
                !Diablo2.Module.Item.StartItems.IsFreshDraft(barbSave), "IsFreshDraft=false");
            item.Reset();
            item.LoadFrom(barbSave);                       // 第二次（模拟进图读档）
            Check("二次 LoadFrom 幂等：装备仍 2 件 / 背包仍 6 锚点（没有重复发）",
                item.Equipment.Count == 2 && CountAnchors(item) == 6,
                $"module.Equipment={item.Equipment.Count} anchors={CountAnchors(item)}");
            Check("装备生效：派生伤害随 EquipChanged 载荷变化",
                _player.DamageText != bareText, $"徒手={bareText} → 装备后={_player.DamageText}");
            Console.WriteLine($"   Barbarian 起始装备（生产路径）：徒手伤害 {bareText} → {_player.DamageText}；"
                + $"防御 {_player.Defense}；命中 {_player.AttackRating}");

            // 15c) 战力断言：走**生产入口** `DamageFormula`（配表 dmg → 官方物理伤害公式 @0057b420）
            for (var ci = 0; ci < 2; ci++)
            {
                var cls = ci == 0 ? PlayerClass.Amazon : PlayerClass.Barbarian;
                var sv = NewCharSave(cls);
                Diablo2.Module.Item.StartItems.Apply(sv);
                Table.BaseItemRow wRow;
                var w = FirstWeapon(sv, out wRow);
                var bare = Diablo2.Module.Combat.DamageFormula.PhysicalDamage(0, sv.str, sv.dex, 0, 0, 1f);
                var lo = w == null ? 0 : Diablo2.Module.Combat.DamageFormula.PhysicalDamage(
                    w.dmgMin, sv.str, sv.dex, wRow.StrBonus, wRow.DexBonus, 1f);
                var hi = w == null ? 0 : Diablo2.Module.Combat.DamageFormula.PhysicalDamage(
                    w.dmgMax, sv.str, sv.dex, wRow.StrBonus, wRow.DexBonus, 1f);
                Console.WriteLine($"   {cls}：力{sv.str}/敏{sv.dex} 武器={WeaponText(sv)}（item_c dmg="
                    + (wRow == null ? "-" : wRow.DmgMin + "-" + wRow.DmgMax)
                    + " strBonus=" + (wRow == null ? "-" : wRow.StrBonus.ToString())
                    + " dexBonus=" + (wRow == null ? "-" : wRow.DexBonus.ToString()) + "）"
                    + $" ⇒ 普攻物理伤害：徒手 {bare} / 装备后 {lo}-{hi}");
                Check($"{cls}: 徒手普攻伤害 = 0（正是用户报的「打不动怪」）", bare == 0, "bare=" + bare);
                Check($"{cls}: 起始武器在手 ⇒ 普攻物理伤害 > 0", w != null && lo > 0 && hi >= lo,
                    $"lo={lo} hi={hi}");
                Check($"{cls}: 装备栏武器的 dmg 真的来自配表链（item_c.dmg_min/max → ItemStack.dmgMin/Max）",
                    w != null && wRow != null && w.dmgMin == wRow.DmgMin && w.dmgMax == wRow.DmgMax,
                    w == null || wRow == null ? "无武器"
                        : $"stack={w.dmgMin}-{w.dmgMax} item_c={wRow.DmgMin}-{wRow.DmgMax}");
            }

            // ── 16. ★ impl-invfix：N2「背包空格」复核（`report-inspect.md` §1 N2）──────────
            //
            //   实机症状：面板 `occupied=14`（40 格）却报「空格 0 但无连续块」⇒ 被读成"两个口径自相矛盾"。
            //   本节的判法是**判过程**：把两件事分别算出来 ——
            //     ① **件数**（锚点数，= UI 图标数 = 驱动打印的 `occupied`）
            //     ② **占用格数**（40 格里真的被盖住几格，逐格数 + 按 `item_c` 的 w×h 复核 footpint 和）
            //   然后断言：14 件**且空位分散**的背包必须放得下 1×3；实机那一格（14 件恰好铺满 40 格）
            //   必须拒绝，且文案不许再写成"空格 N 但无连续块"。
            Section("16) N2 复核（件数 vs 占用格数；14/40 分散 ⇒ 能放 / 真满 ⇒ 拒绝且文案属实）");

            // 16a) 生产读档路径灌入「14 个锚点、占 14 格、空位分散」的背包
            var scatterAnchors = new List<KeyValuePair<Vector2Int, ItemStack>>();
            var scatterCells = new[]
            {
                new Vector2Int(9, 0), new Vector2Int(9, 1), new Vector2Int(9, 2), new Vector2Int(9, 3),
                new Vector2Int(4, 0), new Vector2Int(4, 1), new Vector2Int(4, 2), new Vector2Int(4, 3),
                new Vector2Int(0, 0), new Vector2Int(2, 1), new Vector2Int(6, 2),
                new Vector2Int(3, 3), new Vector2Int(7, 1), new Vector2Int(1, 2),
            };
            for (var i = 0; i < scatterCells.Length; i++)
            {
                var unit = factory.Create(89, 1, ItemQuality.Normal, new Rng(3000 + i));   // 回城卷轴 1×1
                if (unit == null) { Check("16a 判据物品 id=89 能从配表造出来", false, "Create(89) = null"); break; }
                scatterAnchors.Add(new KeyValuePair<Vector2Int, ItemStack>(scatterCells[i], unit));
            }
            item.Reset();
            item.LoadFrom(BuildBagSave("ScatterBag", scatterAnchors));
            var scatterAnchorsN = CountAnchors(item);
            var scatterOccupied = CountOccupiedCells(item.Inventory);
            Check("16a 读档后：锚点（件数）= 14、占用格 = 14、空格 = 26 —— **件数 ≠ 占用格数**",
                scatterAnchorsN == 14 && scatterOccupied == 14 && GameConst.InventoryCellCount - scatterOccupied == 26,
                $"anchors={scatterAnchorsN} occupiedCells={scatterOccupied} freeCells={GameConst.InventoryCellCount - scatterOccupied}");

            var ssRow = Table.Tables.Default.Item.Get(11);
            var shortSword = factory.Create(11, 4, ItemQuality.Normal, new Rng(4711));
            Check("16a 判据物品来自配表：id=11 的 code=ssd、占格 1×3（⛔ 不硬编码尺寸）",
                ssRow != null && shortSword != null && ssRow.Code == "ssd"
                && shortSword.gridW == 1 && shortSword.gridH == 3,
                $"code={(ssRow == null ? "?" : ssRow.Code)} size={shortSword?.gridW}×{shortSword?.gridH}");
            var placedScatter = item.AddToInventory(shortSword);
            Check("16a 1×3 放进「14 件 / 占 14 格 / 空位分散」的背包 ⇒ **成功**", placedScatter,
                "AddToInventory=" + placedScatter);
            Check("16a 占用格数 14 → 17、件数 14 → 15（物品真的进包）",
                CountOccupiedCells(item.Inventory) == 17 && CountAnchors(item) == 15,
                $"occupiedCells={CountOccupiedCells(item.Inventory)} anchors={CountAnchors(item)}");
            Check("16a 入包日志写清了锚点格与剩余空格", _log.Contains("Item", "入包「短剑」"), "见 [INFO] [Item] 入包");

            // 16b) 复现**实机那一格**：14 件按 `item_c` 的真实占格恰好铺满 40 格
            //      （格位/物品 id 逐条抄自实机存档 `client/setting/saves/g66.json`，2026-09-22 23:54:58）
            var fullSpec = new (Vector2Int cell, int id)[]
            {
                (new Vector2Int(0, 0), 47), (new Vector2Int(2, 0), 1), (new Vector2Int(3, 0), 60),
                (new Vector2Int(5, 0), 47), (new Vector2Int(7, 0), 51), (new Vector2Int(9, 0), 21),
                (new Vector2Int(3, 1), 60), (new Vector2Int(3, 2), 57), (new Vector2Int(7, 2), 43),
                (new Vector2Int(0, 3), 60), (new Vector2Int(2, 3), 36), (new Vector2Int(5, 3), 91),
                (new Vector2Int(6, 3), 128), (new Vector2Int(9, 3), 89),
            };
            var fullAnchors = new List<KeyValuePair<Vector2Int, ItemStack>>();
            for (var i = 0; i < fullSpec.Length; i++)
            {
                var it = factory.Create(fullSpec[i].id, 7, ItemQuality.Normal, new Rng(5000 + i));
                if (it == null) { Check("16b 实机那 14 件的 id 都能从配表造出来", false, "id=" + fullSpec[i].id); break; }
                fullAnchors.Add(new KeyValuePair<Vector2Int, ItemStack>(fullSpec[i].cell, it));
            }
            item.Reset();
            item.LoadFrom(BuildBagSave("FullBag", fullAnchors));
            var fullAnchorsN = CountAnchors(item);
            var fullOccupied = CountOccupiedCells(item.Inventory);
            var fullFootprint = FootprintSum(item.Inventory);
            Check("16b 实机那一格的背包 = 14 件**恰好铺满 40 格**（驱动打印的 occupied=14 是**件数**）",
                fullAnchorsN == 14 && fullOccupied == 40 && fullFootprint == 40
                && fullOccupied == GameConst.InventoryCellCount,
                $"anchors={fullAnchorsN} occupiedCells={fullOccupied} footprintSum={fullFootprint} / {GameConst.InventoryCellCount}");
            var shortSword2 = factory.Create(11, 4, ItemQuality.Normal, new Rng(4712));
            var placedOnFull = item.AddToInventory(shortSword2);
            Check("16b 真满的背包再放 1×3 ⇒ **失败（这是正确行为，不是缺陷）**", !placedOnFull,
                "AddToInventory=" + placedOnFull);
            Check("16b 失败文案 = 「背包已满（… 空格 0）」，⛔ 不再用「空格 N 但无连续块」误导",
                _log.Contains("Item", "背包已满") && !_log.Contains("Item", "空格 0 但无连续块"),
                "见 [WARN] [Item]");

            // 16c) 真有 1 格空、但塞不下 1×3 ⇒ 失败，且文案里的空格数必须 = 实际空格数
            item.Reset();
            var oneHoleAnchors = new List<KeyValuePair<Vector2Int, ItemStack>>();
            for (var y = 0; y < GameConst.InventoryRows; y++)
            {
                for (var x = 0; x < GameConst.InventoryCols; x++)
                {
                    if (y * GameConst.InventoryCols + x == GameConst.InventoryCellCount - 1) continue;   // 只留最后一格空
                    var unit = factory.Create(89, 1, ItemQuality.Normal, new Rng(60000 + y * 10 + x));
                    oneHoleAnchors.Add(new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(x, y), unit));
                }
            }
            item.LoadFrom(BuildBagSave("OneHoleBag", oneHoleAnchors));
            var holeOccupied = CountOccupiedCells(item.Inventory);
            var freeCellsNow = GameConst.InventoryCellCount - holeOccupied;
            _log.Lines.Clear();
            var shortSword3 = factory.Create(11, 4, ItemQuality.Normal, new Rng(4713));
            var placedOnHole = item.AddToInventory(shortSword3);
            Check("16c 只有 1 格空（碎片化）⇒ 1×3 放不下", !placedOnHole,
                $"occupiedCells={holeOccupied} freeCells={freeCellsNow} AddToInventory={placedOnHole}");
            Check("16c 文案里的空格数 = **实际**空格数（1），且说明是碎片化而非已满",
                _log.Contains("Item", "空格 1 但无") && !_log.Contains("Item", "背包已满"),
                "见 [WARN] [Item]");

            // ── 17. 片 G1：道具**拖拽放下 / 交换**的落地（用户报的「道具没法拖动！」）────────
            //    这一节只**加断言**，不改任何既有判据。被测入口 = 生产实现
            //    `IItemModule.MoveItem(from, to, out reason)`（`App/AppEventRouting` 转发的就是它）
            //    与真实事件链 `Events.ItemDropRequest`（面板外放下 ⇒ 丢地上）。
            //    防作弊口径：每一步都断言**总件数不变**（不许把物品复制出两份 / 悄悄丢一件）。
            Section("17) G1 拖拽落地：空格放下 / 交换 / 大件放小空位拒绝 / 面板外丢地上");

            ItemStack CellItem(int cell)
            {
                var s = item.Inventory[cell];
                return s != null && s.isAnchor ? s.item : null;
            }

            // 17a) 空格放下：锚点 0 → 空格 5 ⇒ 成功、位置真的变了、**件数不变**
            var unitA = factory.Create(89, 1, ItemQuality.Normal, new Rng(70101));
            var unitB = factory.Create(89, 1, ItemQuality.Normal, new Rng(70102));
            var bagA = new List<KeyValuePair<Vector2Int, ItemStack>>
            {
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(0, 0), unitA),
            };
            item.Reset();
            item.LoadFrom(BuildBagSave("G1Move", bagA));
            var nBefore17a = CountAnchors(item);
            var moved17a = item.MoveItem(0, 5, out var reason17a);
            Check("17a 空格放下：MoveItem(0 → 5) 成功、总件数不变、物品真的换格了",
                moved17a && CountAnchors(item) == nBefore17a && nBefore17a == 1
                && CellItem(5) == unitA && CellItem(0) == null,
                $"ok={moved17a} anchors {nBefore17a}→{CountAnchors(item)} at(5)={(CellItem(5) == null ? "null" : CellItem(5).name)} at(0)={(CellItem(0) == null ? "null" : CellItem(0).name)}");
            Check("17a 移动日志写明「空格」去向（可检索的过程证据）", _log.Contains("Item", "背包内移动"),
                "见 [INFO] [Item] 背包内移动");

            // 17b) 与另一件交换：0 ↔ 5 两件**互换**（按引用判同一实例，不靠 id）
            var bagB = new List<KeyValuePair<Vector2Int, ItemStack>>
            {
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(0, 0), unitA),
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(5, 0), unitB),
            };
            item.Reset();
            item.LoadFrom(BuildBagSave("G1Swap", bagB));
            var nBefore17b = CountAnchors(item);
            var swapped = item.MoveItem(0, 5, out var reason17b);
            Check("17b 交换：MoveItem(0 → 5) 让两格内容互换（同一实例搬到对方格）、总件数不变",
                swapped && CountAnchors(item) == nBefore17b && nBefore17b == 2
                && CellItem(0) == unitB && CellItem(5) == unitA,
                $"ok={swapped} anchors {nBefore17b}→{CountAnchors(item)} at(0)={(CellItem(0) == unitA ? "旧A" : CellItem(0) == unitB ? "旧B" : "?")} at(5)={(CellItem(5) == unitA ? "旧A" : CellItem(5) == unitB ? "旧B" : "?")}");
            Check("17b 交换日志写明「背包内交换」（与原版一致的措辞，可检索）", _log.Contains("Item", "背包内交换"),
                "见 [INFO] [Item] 背包内交换");

            // 17c) 大件放小空位 ⇒ **拒绝 + 有文案 + 两件都留在原格**
            var sword17 = factory.Create(11, 4, ItemQuality.Normal, new Rng(70103));   // 短剑 1×3（配表 item_c）
            var potion17 = factory.Create(89, 1, ItemQuality.Normal, new Rng(70104));  // 回城卷轴 1×1
            var bagC = new List<KeyValuePair<Vector2Int, ItemStack>>
            {
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(0, 0), sword17),
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(5, 3), potion17),
            };
            item.Reset();
            _log.Lines.Clear();
            item.LoadFrom(BuildBagSave("G1TooBig", bagC));
            var nBefore17c = CountAnchors(item);
            var swordSizeOk = sword17.gridW == 1 && sword17.gridH == 3;
            var moved17c = item.MoveItem(0, 35, out var reason17c);                    // 35 = (5,3)
            Check("17c 大件（短剑 1×3）放进 1×1 的小空位 ⇒ **拒绝**、且文案说明是放不下",
                swordSizeOk && !moved17c && !string.IsNullOrEmpty(reason17c)
                && reason17c.Contains("短剑") && reason17c.Contains("空间不够"),
                $"size={sword17.gridW}×{sword17.gridH} ok={moved17c} reason={reason17c}");
            Check("17c 拒绝后**两件都留在原格**、总件数不变（失败不留半途状态）",
                CountAnchors(item) == nBefore17c && nBefore17c == 2
                && CellItem(0) == sword17 && CellItem(35) == potion17,
                $"anchors {nBefore17c}→{CountAnchors(item)} at(0)={(CellItem(0) == null ? "null" : CellItem(0).name)} at(35)={(CellItem(35) == null ? "null" : CellItem(35).name)}");
            Check("17c 拒绝原因**同时**写进模块日志（非预期分支必须留痕）", _log.Contains("Item", "MoveItem 拒绝"),
                "见 [WARN] [Item] MoveItem 拒绝");

            // 17d) 面板外放下 = 丢地上：走**真实事件链** `Events.ItemDropRequest`（UI 的唯一出口）
            //      （UI 侧 `PlanDrop` 判出 DropKind.DropToGround 后发的就是它；见 uicheck U4Check）
            var drop17 = factory.Create(89, 1, ItemQuality.Normal, new Rng(70105));
            var bagD = new List<KeyValuePair<Vector2Int, ItemStack>>
            {
                new KeyValuePair<Vector2Int, ItemStack>(new Vector2Int(0, 0), drop17),
            };
            item.Reset();
            _player.TeleportTo(new Vector2Int(10, 10));
            item.LoadFrom(BuildBagSave("G1Drop", bagD));
            var groundBefore = item.GroundItems.Count;
            var anchorsBefore17d = CountAnchors(item);
            Game.Event.Emit(Events.ItemDropRequest, 0);
            var groundId17d = LastGroundId(item);
            var groundCellOk = false;
            for (var i = 0; i < item.GroundItems.Count; i++)
            {
                if (item.GroundItems[i].Key == groundId17d && item.GroundItems[i].Value == drop17) groundCellOk = true;
            }
            Check("17d 面板外放下：背包少一件、地面上多一件（同一件物品），落在玩家脚下格",
                anchorsBefore17d == 1 && CountAnchors(item) == 0
                && item.GroundItems.Count == groundBefore + 1 && groundId17d >= 0 && groundCellOk,
                $"anchors {anchorsBefore17d}→{CountAnchors(item)} ground {groundBefore}→{item.GroundItems.Count} id={groundId17d} 玩家格={(10, 10)}");
            Check("17d 丢弃走了 `Events.ItemDropRequest`（UI 面板外放下的唯一出口）而不是直调模块",
                _log.Contains("Item", "丢弃"), "见 [INFO] [Item] 丢弃");

            // ── 14. 收尾 ─────────────────────────────────────────────────────────
            Console.WriteLine();
            Console.WriteLine("分层自检（见回报，本宿主不重复执行 shell）：② 跨模块 using ③ UI using Module ④ 直连网络 ⑤ 裸事件名");
            Console.WriteLine();
            Console.WriteLine(_fail == 0 ? "=== 自检全部通过 ===" : $"=== 自检失败 {_fail} 项 ===");
            Console.WriteLine();
            Console.WriteLine("未覆盖（需 Unity 原生 / 真人操作）：面板像素布局、真实手感、Unity 原生序列化与 UnityEngine.Time。");
            return _fail == 0 ? 0 : 1;
        }

        // ── 断言/输出助手 ───────────────────────────────────────────────────────

        private static void Section(string title)
        {
            Console.WriteLine();
            Console.WriteLine("── " + title + " ──");
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }

        /// <summary>（★ 本轮新增）取装备载荷里的武器（`ItemStack.type == ItemType.Weapon`）。</summary>
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

        /// <summary>（★ 本轮新增）装备载荷里各武器的名字+id（断言失败时的可读详情）。</summary>
        private static string weaponsNames(List<ItemStack> equip)
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

        /// <summary>
        /// （★ 本轮新增）`StubPlayer.DamageText` 的期望值。口径见该属性本体
        /// （`1 + dmgMin`-`2 + dmgMax`，再乘 `dmg%` 词缀百分比）——
        /// **仅在该武器无词缀时成立**，所以调用点同时断言了两把武器的 `affixes.Count == 0`。
        /// </summary>
        private static string ExpectedDmgTextOf(ItemStack weapon)
        {
            if (weapon == null) return "(null weapon)";
            return $"{1 + weapon.dmgMin}-{2 + weapon.dmgMax}";
        }

        /// <summary>（★ 本轮新增）取某字段在 JSON 里的片段（自证/失败详情用）。</summary>
        private static string FieldSnippet(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return "json 为空";
            var token = "\"" + field + "\":";
            var i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return "**缺字段 " + field + "**";
            var end = i + token.Length;
            while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
            return json.Substring(i, end - i);
        }

        private static void DumpItem(string title, ItemStack it)
        {
            if (it == null)
            {
                Console.WriteLine($"   {title}: null");
                return;
            }
            Console.WriteLine($"   {title}: {it.name}");
            Console.WriteLine($"      id={it.itemId} type={it.type} quality={it.quality} size={it.gridW}×{it.gridH} "
                + $"lvlReq={it.lvlReq} strReq={it.strReq} dmg={it.dmgMin}-{it.dmgMax} def={it.defMin}-{it.defMax} "
                + $"price={it.price} dur={it.durability}/{it.maxDurability}");
            for (var i = 0; i < it.affixes.Count; i++)
            {
                var a = it.affixes[i];
                Console.WriteLine($"      词缀[{i}] affixId={a.affixId} kind={a.kind} mod={a.mod} "
                    + $"min={a.min} max={a.max} value={a.value} name={a.name}");
            }
        }

        // ═════════════════════════════════════════════════════════════════════════
        // §15 起始装备的助手（断言口径见该节注释）
        // ═════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **官方** `charstats.txt` 的起始装备期望：`"{classId}|{slotIndex}|{code}|{loc}|{count}"`。
        /// 本宿主自己解析官方文件（**不读我们自己的配表**）⇒ 对照判的是"抽取过程"而不是"结果"。
        /// </summary>
        private static List<string> ReadOfficialStartItems()
        {
            var res = new List<string>();
            var path = System.IO.Path.Combine(ResolveProjectRoot(), "原版资源", "d2lod1.10txt-1.10f",
                "data", "global", "excel", "charstats.txt");
            if (!System.IO.File.Exists(path))
            {
                Console.WriteLine("[warn] 官方 charstats.txt 不存在：" + path);
                return res;
            }

            // 官方 txt 是 ASCII/cp1252；`Encoding.Latin1` 在 .NET 5+ 内置（不依赖 CodePages provider）
            var text = System.IO.File.ReadAllText(path, System.Text.Encoding.Latin1).Replace("\r\n", "\n");
            var lines = text.Split('\n');
            var hdr = lines[0].Split('\t');
            var idx = new Dictionary<string, int>();
            for (var i = 0; i < hdr.Length; i++) idx[hdr[i]] = i;

            var clsId = new Dictionary<string, int>
            {
                { "Amazon", 1 }, { "Sorceress", 2 }, { "Necromancer", 3 },
                { "Paladin", 4 }, { "Barbarian", 5 },
            };

            for (var li = 1; li < lines.Length; li++)
            {
                if (lines[li].Trim().Length == 0) continue;
                var row = lines[li].Split('\t');
                int id;
                if (!clsId.TryGetValue(CellOf(row, idx, "class"), out id)) continue;
                for (var k = 1; k <= 10; k++)
                {
                    var code = CellOf(row, idx, "item" + k);
                    var loc = CellOf(row, idx, "item" + k + "loc");
                    var cnt = CellOf(row, idx, "item" + k + "count");
                    if (code == "" || code == "0") continue;        // 官方空位占位
                    if (cnt == "" || cnt == "0") continue;
                    res.Add(id + "|" + k + "|" + code + "|" + loc + "|" + cnt);
                }
            }
            return res;
        }

        private static string CellOf(string[] row, Dictionary<string, int> idx, string name)
        {
            int i;
            if (!idx.TryGetValue(name, out i) || i >= row.Length) return "";
            return row[i];
        }

        /// <summary>两侧差集（多重集语义：重复元素按出现次数配平）。</summary>
        private static List<string> OnlyIn(List<string> a, List<string> b)
        {
            var used = new bool[b.Count];
            var only = new List<string>();
            for (var i = 0; i < a.Count; i++)
            {
                var hit = -1;
                for (var k = 0; k < b.Count; k++)
                {
                    if (!used[k] && b[k] == a[i]) { hit = k; break; }
                }
                if (hit < 0) only.Add(a[i]); else used[hit] = true;
            }
            return only;
        }

        private static string DiffText(List<string> expect, List<string> got)
        {
            return "仅官方有=[" + string.Join(";", OnlyIn(expect, got)) + "] 仅我们有=["
                + string.Join(";", OnlyIn(got, expect)) + "]";
        }

        /// <summary>多重集文本（排序后拼接，顺序无关）。</summary>
        private static string Multiset(List<string> pairs)
        {
            var l = new List<string>(pairs);
            l.Sort(StringComparer.Ordinal);
            return string.Join(",", l);
        }

        /// <summary>
        /// 把 `codexN` 条目**按 code 汇总**成"该 code 身上的总数量"（`codex总N`）。
        /// 为什么必须汇总：不可堆叠品按规格**拆成 count 格**（例如 `hp1` count=4 ⇒ 4 格各 1 个），
        /// 而官方期望是"一行 count=4" ⇒ 直接比 `code×count` 会把同一件事判成不等。
        /// </summary>
        private static string TotalsOf(List<string> pairs)
        {
            var sum = new Dictionary<string, int>();
            for (var i = 0; i < pairs.Count; i++)
            {
                var at = pairs[i].LastIndexOf('x');
                if (at <= 0) continue;
                var code = pairs[i].Substring(0, at);
                var nTxt = pairs[i].Substring(at + 1);
                var n = 0;
                if (!int.TryParse(nTxt, out n)) continue;
                int cur;
                sum[code] = sum.TryGetValue(code, out cur) ? cur + n : n;
            }
            var keys = new List<string>(sum.Keys);
            keys.Sort(StringComparer.Ordinal);
            var parts = new List<string>();
            for (var i = 0; i < keys.Count; i++) parts.Add(keys[i] + "x" + sum[keys[i]]);
            return string.Join(",", parts);
        }

        /// <summary>`start_item_c.src_line` 的去重值（应 = 2,3,4,5,6 = 5 职业在官方 txt 的行号）。</summary>
        private static string SrcLines(List<Table.BaseStartItemRow> rows)
        {
            var set = new List<int>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i] == null) continue;
                if (!set.Contains(rows[i].SrcLine)) set.Add(rows[i].SrcLine);
            }
            set.Sort();
            return string.Join(",", set);
        }

        /// <summary>造一份"创角时"的草稿档（字段口径照 `UI/CharCreatePanel`；起始装备由 Apply 填）。</summary>
        private static CharacterSave NewCharSave(PlayerClass cls)
        {
            var row = Table.Tables.Default.Class.Get((int)cls);
            return new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = "Start" + cls,
                cls = cls,
                level = 1,
                str = row != null ? row.Str : 10,
                dex = row != null ? row.Dex : 10,
                vit = row != null ? row.Vit : 10,
                eng = row != null ? row.Eng : 10,
                life = 1, mana = 1, stamina = 1,
                areaId = (int)AreaId.Town,
                mapSeed = 4321,
            };
        }

        /// <summary>`code×count` 多重集（装备 + 背包锚点）；用于与官方期望逐条对照。</summary>
        private static List<string> PairsOfSave(CharacterSave sv)
        {
            var res = new List<string>();
            if (sv == null) return res;
            for (var i = 0; i < sv.equip.Count; i++) AddPair(res, sv.equip[i]);
            for (var i = 0; i < sv.inventory.Count; i++)
            {
                var s = sv.inventory[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                AddPair(res, s.item);
            }
            return res;
        }

        private static void AddPair(List<string> res, ItemStack it)
        {
            if (it == null) return;
            var row = Table.Tables.Default.Item.Get(it.itemId);
            res.Add((row != null ? row.Code : "?" + it.itemId) + "x" + it.count);
        }

        private static List<ItemSlot> EquipSlots(CharacterSave sv)
        {
            var res = new List<ItemSlot>();
            if (sv == null) return res;
            for (var i = 0; i < sv.equip.Count; i++)
            {
                if (sv.equip[i] != null) res.Add(Diablo2.Module.Item.Equipment.SlotOf(sv.equip[i]));
            }
            return res;
        }

        private static string EquipText(CharacterSave sv)
        {
            var parts = new List<string>();
            for (var i = 0; i < sv.equip.Count; i++)
            {
                var it = sv.equip[i];
                if (it == null) continue;
                parts.Add(Diablo2.Module.Item.Equipment.SlotOf(it) + ":" + it.name + "x" + it.count);
            }
            return string.Join(",", parts);
        }

        private static string InvText(CharacterSave sv)
        {
            var parts = new List<string>();
            for (var i = 0; i < sv.inventory.Count; i++)
            {
                var s = sv.inventory[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                parts.Add(s.item.name + "x" + s.item.count + "@" + s.index);
            }
            return string.Join(",", parts);
        }

        /// <summary>第一个武器槽装备（+ 它的 `item_c` 行）。</summary>
        private static ItemStack FirstWeapon(CharacterSave sv, out Table.BaseItemRow row)
        {
            row = null;
            if (sv == null) return null;
            for (var i = 0; i < sv.equip.Count; i++)
            {
                var it = sv.equip[i];
                if (it == null || it.type != ItemType.Weapon) continue;
                row = Table.Tables.Default.Item.Get(it.itemId);
                return it;
            }
            return null;
        }

        private static string WeaponText(CharacterSave sv)
        {
            Table.BaseItemRow row;
            var w = FirstWeapon(sv, out row);
            if (w == null) return "(无)";
            return $"{w.name} dmg {w.dmgMin}-{w.dmgMax}（strBonus={row.StrBonus} dexBonus={row.DexBonus}）";
        }

        /// <summary>`loc=rarm/larm` 的行必须是**该槽位**上的同一件 `code`。</summary>
        private static bool LocSlotsOk(List<string> officialRows, int clsId, CharacterSave sv, out string txt)
        {
            var parts = new List<string>();
            var ok = true;
            for (var i = 0; i < officialRows.Count; i++)
            {
                var f = officialRows[i].Split('|');
                if (int.Parse(f[0]) != clsId) continue;
                var want = f[3] == "rarm" ? ItemSlot.Weapon : (f[3] == "larm" ? ItemSlot.Shield : ItemSlot.None);
                if (want == ItemSlot.None) continue;

                var found = false;
                for (var k = 0; k < sv.equip.Count; k++)
                {
                    var it = sv.equip[k];
                    if (it == null) continue;
                    var row = Table.Tables.Default.Item.Get(it.itemId);
                    if (row == null || row.Code != f[2]) continue;
                    if (Diablo2.Module.Item.Equipment.SlotOf(it) == want) found = true;
                }
                parts.Add($"{f[2]}@{f[3]}→{want}={(found ? "ok" : "MISS")}");
                if (!found) ok = false;
            }
            txt = parts.Count == 0 ? "(该职业无 loc 行)" : string.Join(",", parts);
            return ok;
        }

        /// <summary>背包格位自洽：锚点的 `gridW×gridH` 块全部 occupied 且 `anchorIndex` 指回锚点。</summary>
        private static bool InventoryBlocksOk(CharacterSave sv)
        {
            if (sv == null || sv.inventory == null) return false;
            if (sv.inventory.Count != GameConst.InventoryCellCount) return false;
            for (var i = 0; i < sv.inventory.Count; i++)
            {
                var a = sv.inventory[i];
                if (a == null || !a.isAnchor || a.item == null) continue;
                var w = a.item.gridW > 0 ? a.item.gridW : 1;
                var h = a.item.gridH > 0 ? a.item.gridH : 1;
                for (var y = a.y; y < a.y + h; y++)
                {
                    for (var x = a.x; x < a.x + w; x++)
                    {
                        if (x < 0 || y < 0 || x >= GameConst.InventoryCols || y >= GameConst.InventoryRows)
                            return false;
                        var c = sv.inventory[y * GameConst.InventoryCols + x];
                        if (c == null || !c.occupied || c.anchorIndex != a.index) return false;
                        if (x == a.x && y == a.y && !ReferenceEquals(c.item, a.item)) return false;
                    }
                }
            }
            return true;
        }

        private static bool AffixMaxLevelOk(ItemStack it, int itemLevel)
        {
            if (it == null) return false;
            for (var i = 0; i < it.affixes.Count; i++)
            {
                var row = Table.Tables.Default.Affix.Get(it.affixes[i].affixId);
                if (row == null) return false;
                if (row.Lvl > itemLevel) return false;
            }
            return true;
        }

        private static string AffixLevels(ItemStack it)
        {
            if (it == null) return "-";
            var parts = new List<string>();
            for (var i = 0; i < it.affixes.Count; i++)
            {
                var row = Table.Tables.Default.Affix.Get(it.affixes[i].affixId);
                parts.Add(row == null ? "?" : row.Lvl.ToString());
            }
            return "affixLvl=[" + string.Join(",", parts) + "] itemLevel=12";
        }

        private static ItemStack FindWeaponWithAttAffix(out int seedUsed)
        {
            var factory = new Diablo2.Module.Item.ItemFactory();
            for (var seed = 1; seed <= 800; seed++)
            {
                var rng = new Rng(seed);
                var st = factory.Create(2, 12, ItemQuality.Magic, rng);      // 斧（可穿：lvlReq=0 strReq=32）
                if (st == null) continue;
                if (AffixModSum(st, "att") > 0)
                {
                    seedUsed = seed;
                    return st;
                }
            }
            seedUsed = -1;
            return null;
        }

        private static int AffixModSum(ItemStack it, string mod)
        {
            var n = 0;
            if (it == null) return 0;
            for (var i = 0; i < it.affixes.Count; i++)
            {
                if (it.affixes[i].mod == mod) n += it.affixes[i].value;
            }
            return n;
        }

        /// <summary>TC 名 → `treasureClassId`（= `Treasureclass.All()` 的 1 基行序；0 = 不在表里）。</summary>
        private static int TcIdOfName(string tcName)
        {
            var all = Table.Tables.Default.Treasureclass.All();
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Name == tcName) return i + 1;
            }
            return 0;
        }

        /// <summary>
        /// 复刻 agent-07 `DeathFlow` 的调用链（`monster_c.treasure_class` → 1 基行序 tcId），
        /// 以便本宿主用**与真实游戏同一条编码**驱动 `IItemModule.DropLoot`。
        /// </summary>
        private static int TcIdOfMonsterKind(int monsterKindId)
        {
            var mon = Table.Tables.Default.Monster.Get(monsterKindId);
            if (mon == null || string.IsNullOrEmpty(mon.TreasureClass)) return 0;
            return TcIdOfName(mon.TreasureClass);
        }

        /// <summary>`Events.DialogOpen` 计数（宿主观察用；lambda 里不能 `ref` 局部变量 ⇒ 用静态字段）。</summary>
        private static int _dialogOpens;

        /// <summary>最近一次 `Events.DialogOpen` 的 npcId。</summary>
        private static int _lastDialogNpc = -1;

        private static void OnDialogOpenRecorder(NpcDialogArgs a)
        {
            _dialogOpens++;
            _lastDialogNpc = a != null ? a.npcId : -1;
        }

        private static int CountOccupied(Diablo2.Module.Item.Inventory inv)
        {
            var n = 0;
            for (var i = 0; i < inv.Slots.Count; i++)
            {
                if (inv.Slots[i].occupied) n++;
            }
            return n;
        }

        /// <summary>
        /// （★ impl-invfix）**占用格数** —— 40 格里真的被盖住几格（`occupied = true`）。
        /// ⚠️ 与"件数"（锚点数）是**两个不同的量**：实机 `/GRID ... occupied=14` 打的是**件数**
        /// （驱动数的是 UI 图标数，而按契约只有锚点格挂图标），把它当"占用格数"就会得出
        /// "面板说占 14 格、模块说空格 0 ⇒ 自相矛盾"的错误结论（`report-inspect.md` §1 N2）。
        /// </summary>
        private static int CountOccupiedCells(IReadOnlyList<InventorySlot> inv)
        {
            if (inv == null) return -1;
            var n = 0;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i] != null && inv[i].occupied) n++;
            }
            return n;
        }

        /// <summary>（★ impl-invfix）所有锚点物品的 `gridW×gridH` 之和（应当 = 占用格数）。</summary>
        private static int FootprintSum(IReadOnlyList<InventorySlot> inv)
        {
            if (inv == null) return -1;
            var n = 0;
            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                if (s == null || !s.isAnchor || s.item == null) continue;
                n += Mathf.Max(1, s.item.gridW) * Mathf.Max(1, s.item.gridH);
            }
            return n;
        }

        /// <summary>
        /// （★ impl-invfix）造一份"背包已按格摆好"的存档（`CharacterSave.inventory` = 40 格，含空格），
        /// 供 `ItemModule.LoadFrom` 走**生产读档路径**灌进模块（⛔ 不直接改 `Inventory._slots`）。
        /// <paramref name="anchors"/> 每项 = (锚点格, 该格上的物品)；覆盖格按 `item_c` 的 w×h 铺满。
        /// </summary>
        private static CharacterSave BuildBagSave(string name, List<KeyValuePair<Vector2Int, ItemStack>> anchors)
        {
            var inv = new List<InventorySlot>(GameConst.InventoryCellCount);
            for (var y = 0; y < GameConst.InventoryRows; y++)
            {
                for (var x = 0; x < GameConst.InventoryCols; x++)
                {
                    inv.Add(new InventorySlot
                    {
                        index = y * GameConst.InventoryCols + x, x = x, y = y,
                        occupied = false, isAnchor = false, item = null, anchorIndex = -1,
                    });
                }
            }

            for (var i = 0; i < anchors.Count; i++)
            {
                var cell = anchors[i].Key;
                var it = anchors[i].Value;
                if (it == null) continue;
                var w = it.gridW > 0 ? it.gridW : 1;
                var h = it.gridH > 0 ? it.gridH : 1;
                for (var yy = cell.y; yy < cell.y + h && yy < GameConst.InventoryRows; yy++)
                {
                    for (var xx = cell.x; xx < cell.x + w && xx < GameConst.InventoryCols; xx++)
                    {
                        var s = inv[yy * GameConst.InventoryCols + xx];
                        s.occupied = true;
                        s.anchorIndex = cell.y * GameConst.InventoryCols + cell.x;
                        if (yy == cell.y && xx == cell.x) { s.isAnchor = true; s.item = it; }
                    }
                }
            }

            return new CharacterSave
            {
                version = GameConst.SaveVersion, name = name, cls = PlayerClass.Amazon, level = 7,
                str = 20, dex = 25, vit = 20, eng = 15, life = 100, mana = 20, stamina = 80,
                gold = 0, areaId = (int)AreaId.Town, mapSeed = 4321,
                inventory = inv, equip = new List<ItemStack>(), belt = new List<ItemStack>(),
            };
        }

        private static int LastGroundId(Diablo2.Module.IItemModule item)
        {
            var best = -1;
            var g = item.GroundItems;
            for (var i = 0; i < g.Count; i++)
            {
                if (g[i].Key > best) best = g[i].Key;
            }
            return best;
        }

        private static bool Contains(Diablo2.Module.IItemModule item, int id)
        {
            var g = item.GroundItems;
            for (var i = 0; i < g.Count; i++)
            {
                if (g[i].Key == id) return true;
            }
            return false;
        }

        private static void ClearGround(Diablo2.Module.IItemModule item, IReadOnlyList<KeyValuePair<int, ItemStack>> snapshot)
        {
            // 直接经公开门面清掉（DropToGround 的反向操作只有视图层需要；这里用 Reset 更省事）
            item.Reset();
        }

        /// <summary>数存档里的背包锚点（`CharacterSave.inventory`）。</summary>
        private static int CountAnchorsInSave(CharacterSave data)
        {
            if (data == null || data.inventory == null) return 0;
            var n = 0;
            for (var i = 0; i < data.inventory.Count; i++)
            {
                var s = data.inventory[i];
                if (s != null && s.isAnchor && s.item != null) n++;
            }
            return n;
        }

        private static int CountAnchors(IItemModule item)
        {
            var n = 0;
            var inv = item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i].isAnchor && inv[i].item != null) n++;
            }
            return n;
        }

        private static int FindAnchor(Diablo2.Module.IItemModule item, ItemStack target)
        {
            var inv = item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i].isAnchor && ReferenceEquals(inv[i].item, target)) return i;
            }
            return -1;
        }

        private static int FirstAnchor(Diablo2.Module.IItemModule item)
        {
            var inv = item.Inventory;
            for (var i = 0; i < inv.Count; i++)
            {
                if (inv[i].isAnchor && inv[i].item != null) return i;
            }
            return -1;
        }

        private static ItemStack AnchorItem(Diablo2.Module.IItemModule item, int anchor)
        {
            if (anchor < 0) return null;
            var inv = item.Inventory;
            return anchor < inv.Count ? inv[anchor].item : null;
        }

        private static int Get(Dictionary<ItemQuality, int> d, ItemQuality q)
        {
            int v;
            return d.TryGetValue(q, out v) ? v : 0;
        }

        private static string DescribeQuality(Dictionary<ItemQuality, int> d, int total)
        {
            if (total <= 0) return "无物品";
            return $"普通 {Get(d, ItemQuality.Normal)} ({Pct(Get(d, ItemQuality.Normal), total)})、"
                 + $"魔法 {Get(d, ItemQuality.Magic)} ({Pct(Get(d, ItemQuality.Magic), total)})、"
                 + $"稀有 {Get(d, ItemQuality.Rare)} ({Pct(Get(d, ItemQuality.Rare), total)})、"
                 + $"套装 {Get(d, ItemQuality.Set)} ({Pct(Get(d, ItemQuality.Set), total)})、"
                 + $"暗金 {Get(d, ItemQuality.Unique)} ({Pct(Get(d, ItemQuality.Unique), total)})";
        }

        private static string Pct(int n, int total)
        {
            return (n * 100.0 / total).ToString("0.0") + "%";
        }

        private static bool AllDistinct(List<string> texts)
        {
            for (var i = 0; i < texts.Count; i++)
            {
                for (var k = i + 1; k < texts.Count; k++)
                {
                    if (texts[i] == texts[k]) return false;
                }
            }
            return true;
        }

        private static int DistinctCount(List<string> texts)
        {
            var set = new List<string>();
            for (var i = 0; i < texts.Count; i++)
            {
                if (!set.Contains(texts[i])) set.Add(texts[i]);
            }
            return set.Count;
        }

        private static bool CanTurnInFlag(Diablo2.Module.INpcModule npc, Diablo2.Module.IQuestModule quest)
        {
            // 把任务推到"可交付"，再看对话标志
            _monster.SeedDen(1);
            _map.Area = AreaId.DenOfEvil;
            quest.Reset();
            quest.AcceptDen();
            Game.Event.Emit(Events.AreaChanged, AreaId.DenOfEvil);
            _monster.KillOneInDen();
            quest.NotifyMonsterKilled(1);
            // ★ 片 T：台词只在城镇取（`GetDialog` 在洞里合法返回 null，见 §9 的说明）
            _map.Area = AreaId.Town;
            var d = npc.GetDialog(0);
            if (d == null)
            {
                Console.WriteLine("      （可交付阶段：GetDialog(0) = null ⇒ 见上一行 [Npc] Warn，本行判 fail）");
                return false;
            }
            var ok = d.canTurnInQuest;
            Console.WriteLine($"      （可交付阶段：CanTurnInDen={quest.CanTurnInDen} 对话.canTurnInQuest={d.canTurnInQuest}）");
            return ok;
        }

        /// <summary>通过公开流程把任务推到四个阶段（验收要求"4 个阶段各取一次对话"）。</summary>
        private static void SetQuestStage(Diablo2.Module.IQuestModule quest, QuestState target)
        {
            quest.Reset();
            if (target == QuestState.NotStarted) return;

            _monster.SeedDen(1);
            _map.Area = AreaId.DenOfEvil;
            quest.AcceptDen();
            Game.Event.Emit(Events.AreaChanged, AreaId.DenOfEvil);
            if (target == QuestState.InProgress) return;

            _monster.KillOneInDen();
            quest.NotifyMonsterKilled(1);
            if (target == QuestState.ReadyToTurnIn) return;

            _map.Area = AreaId.Town;
            quest.TurnInDen();
        }

        private static bool BreakJsonRoundTrip(Diablo2.Module.ISaveModule save)
        {
            _setting.Set(GameConst.SaveKeyPrefix + "Broken", "{ this is not json ");
            var d = save.Load("Broken");
            var ok = d == null && !string.IsNullOrEmpty(save.LastError);
            _setting.Delete(GameConst.SaveKeyPrefix + "Broken");
            return ok;
        }

        /// <summary>去掉易变字段后比较两份 JSON（`savedAtTicks`/`playedSeconds` 每次存都会更新）。</summary>
        private static string Normalize(string json)
        {
            if (json == null) return null;
            var s = StripField(json, "savedAtTicks");
            s = StripField(s, "playedSeconds");
            return s;
        }

        private static string StripField(string json, string field)
        {
            var token = "\"" + field + "\":";
            var i = json.IndexOf(token, StringComparison.Ordinal);
            if (i < 0) return json;
            var end = json.IndexOf(',', i);
            if (end < 0) end = json.Length - 1;
            return json.Substring(0, i) + json.Substring(end + 1);
        }
    }
}
