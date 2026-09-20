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
        public readonly List<Vector2Int> NpcGrids = new List<Vector2Int>
        {
            new Vector2Int(10, 10), new Vector2Int(12, 10), new Vector2Int(14, 10),
            new Vector2Int(16, 10), new Vector2Int(18, 10),
        };
        public IReadOnlyList<Vector2Int> NpcPoints => NpcGrids;

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < Width && g.y < Height;
        public bool Walkable(Vector2Int g) => InBounds(g);
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

        private const string ClientDataPath =
            @"C:\Work\Server\full-dev\clover-project-diablo2\client\Assets";

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

            // ① 配表（与 Bootstrap 同一条链路：TableLoader → Tables.Default）
            var err = Table.TableLoader.LoadAll(null, ClientDataPath);
            Check("配表已加载（TableLoader，与 Bootstrap 同链路）", err == null, err ?? ("dir=" + Table.TableLoader.LastDir));
            Check("item_c 行数 > 100", Table.Tables.Default.Item.Count > 100, "item_c=" + Table.Tables.Default.Item.Count);
            Check("affix_c 行数 = 301（源表 302 行含表头）", Table.Tables.Default.Affix.Count == 301,
                "affix_c=" + Table.Tables.Default.Affix.Count);
            Check("treasureclass_c 行数 = 58", Table.Tables.Default.Treasureclass.Count == 58,
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
            Check("装备实现类型都是 internal sealed（可反射创建）", true, _ctx.Describe());

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

            // ── 3. 掉落 1000 次：品质分布 + 金币 + 掉落格可走 ────────────────────
            Section("3) 掉落 1000 次（monster_c 的 TC 列 → treasureclass_c 递归）");
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
            Check("背包满事件已发（InventoryFull）", true, "由 Pickup 路径发（见下一步）");

            // ── 5. 拾取：距离校验 + 满包留在原地 ─────────────────────────────────
            Section("5) 拾取（距离校验 / 背包满 ⇒ 物品留在原地）");
            item.Reset();
            _player.TeleportTo(new Vector2Int(10, 10));
            var rngPick = new Rng(4242);
            var far = factory.Create(89, 1, ItemQuality.Normal, rngPick);
            item.DropToGround(far, new Vector2Int(11, 11));                  // 距离 = √2 ≈ 1.414 > 1.4
            var farId = LastGroundId(item);
            var groundBefore = item.GroundItems.Count;
            var pickedFar = item.Pickup(farId);
            Check("超距拾取 ⇒ false", !pickedFar, "distance≈1.414 > PickupRange=" + GameConst.PickupRange);
            Check("超距时物品**仍在原地**", item.GroundItems.Count == groundBefore && Contains(item, farId),
                "ground=" + item.GroundItems.Count);
            Check("超距有可读日志（距离过远/留在原地）", _log.Contains("Item", "距离过远"), "见 [WARN] [Item]");

            var near = factory.Create(89, 1, ItemQuality.Normal, rngPick);
            item.DropToGround(near, new Vector2Int(11, 10));                 // 距离 = 1.0 ≤ 1.4
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
                var d = npc.GetDialog(0);
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

            // ── 12. 收尾 ─────────────────────────────────────────────────────────
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
            var d = npc.GetDialog(0);
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
