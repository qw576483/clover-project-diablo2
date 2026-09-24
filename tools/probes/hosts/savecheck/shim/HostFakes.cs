// ─────────────────────────────────────────────────────────────────────────────
//
// **无参 `Save()`** —— 它把"当前游戏状态"逐字段收集成一个**新造**的 `CharacterSave`
// （`SaveModule.cs:167`）。要离线跑通这条链，就必须让 `AppContext.Map` / `.Player` 非 null，
// 于是一定要有 `IMapModule` / `IPlayerModule` 的**桩**。
//
// 桩的两条纪律（否则判据会变成"自证"）：
//   ① **忠实于真实现**：`StubPlayer.WriteTo` **不许**写 `save.areaId` —— 真 `PlayerModule.WriteTo`
//      （`Module/Player/PlayerModule.cs:448-477`）在 `:473` 明文写着「mapSeed / areaId / 背包 / 任务：
//      分别由 Flow、Map、Item、Quest 负责，**这里不碰**」。
//      里有一行 `save.areaId = (int)AreaId.Town;` —— 那是桩自己的发明、与真实现**相反**，
//   ② **只用被验证对象真正读到的成员**：桩地图/桩玩家只提供 `Save()` 收集链用得到的值
//      （`Name` / `Class` / `Grid` / `WriteTo` + 地图的 `Area` / `Seed` / `IsGenerated`），
//      其余成员给"无副作用的最小值"，不模拟任何玩法。
//
// 桩的形状抄自 `tools/probes/hosts/itemcheck/Program.cs`（同项目、同契约版本），
// 不引入任何新契约成员、不改被测文件。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace SaveCheck
{
    // ═════════════════════════════════════════════════════════════════════════
    // 桩：地图（只用区域 / seed / 是否已生成；可走性规则与真地图一致：越界不可走）
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class StubMap : IMapModule
    {
        public AreaId Area { get; set; } = AreaId.Town;
        public int Seed { get; set; } = 20260924;
        public int Width => 80;
        public int Height => 80;
        public int BlockedCount => 0;
        public int WalkableCount => Width * Height;
        public bool IsGenerated { get; set; } = true;
        public Vector2Int SpawnPoint => new Vector2Int(9, 60);
        public IReadOnlyList<Vector2Int> Exits => new List<Vector2Int> { new Vector2Int(2, 32) };
        public Vector2Int? CaveEntrance => Area == AreaId.BloodMoor ? new Vector2Int(40, 40) : (Vector2Int?)null;
        public IReadOnlyList<Vector2Int> MonsterSpawns => new List<Vector2Int>();

        /// <summary>桩地图无传送点（契约成员见 `Module/Contracts.cs` 的 `IMapModule.WaypointPoints`）。</summary>
        public IReadOnlyList<Vector2Int> WaypointPoints => new List<Vector2Int>();

        /// <summary>
        /// 桩地图的已探索格（契约成员见 `Module/Contracts.cs` 的 `IMapModule.ExploredCells`）。
        /// <para>★ 片 save-progress：改成**可注入**（`ExploredForTest`）—— 本片要判"存盘时把当前区域的
        /// 已探索格收进档案"，没有可注入的权威集合就没法让断言**可能变红**（默认为空 = 真实运行初值）。</para>
        /// </summary>
        public IReadOnlyCollection<Vector2Int> ExploredCells => ExploredForTest;

        /// <summary>测试用：权威已探索集合（模拟渲染层 `MapView._explored` 的投影）。</summary>
        public readonly List<Vector2Int> ExploredForTest = new List<Vector2Int>();

        public IReadOnlyList<Vector2Int> NpcPoints => new List<Vector2Int>();

        public bool InBounds(Vector2Int g) => g.x >= 0 && g.y >= 0 && g.x < Width && g.y < Height;
        public bool Walkable(Vector2Int g) => InBounds(g);
        public bool IsDeckGrid(Vector2Int g) => false;
        public TileKind TileAt(Vector2Int g) => Walkable(g) ? TileKind.Grass : TileKind.Void;
        public void Generate(AreaId area, int seed) { Area = area; Seed = seed; }
        public void Clear() { }
        public List<Vector2Int> FindPath(Vector2Int from, Vector2Int to) { return new List<Vector2Int> { from, to }; }
        public Vector2Int RandomWalkableTile(Rng rng) => new Vector2Int(rng.Next(1, Width - 1), rng.Next(1, Height - 1));
        public void ShowArea(AreaId area) { Area = area; }
        public MinimapArgs BuildMinimap() { return new MinimapArgs { areaId = (int)Area, width = Width, height = Height, seed = Seed }; }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 桩：玩家（`WriteTo` **逐字段照真实现**写；不写 areaId —— 见文件头纪律 ①）
    // ═════════════════════════════════════════════════════════════════════════
    internal sealed class StubPlayer : IPlayerModule
    {
        private int _baseStr = 20, _baseDex = 25, _baseVit = 20, _baseEng = 15, _baseDef = 5;
        private int _life = 60, _mana = 22, _stamina = 20;
        private int _maxLife = 60, _maxMana = 22, _maxStamina = 20;

        public PlayerClass Class => PlayerClass.Amazon;
        public string Name { get; set; } = "AreaHero";
        public int Level { get; set; } = 1;
        public int Str => _baseStr;
        public int Dex => _baseDex;
        public int Vit => _baseVit;
        public int Eng => _baseEng;
        public int Life => _life;
        public int MaxLife => _maxLife;
        public int Mana => _mana;
        public int MaxMana => _maxMana;
        public int Stamina => _stamina;
        public int MaxStamina => _maxStamina;
        public long Exp { get; private set; }
        public long ExpNext { get; private set; } = 1000;
        public int StatPoints { get; private set; }
        public int SkillPoints { get; private set; }
        public int Gold { get; private set; }
        public Vector2Int Grid { get; private set; } = new Vector2Int(10, 10);
        public Vector3 World => new Vector3(Grid.x, Grid.y, 0);
        /// <summary>必须写全名：`CloverEngine.Dir8` 与 `Diablo2.Def.Dir8` 同时可见 ⇒ 裸 `Dir8` 是 CS0104。</summary>
        public Diablo2.Def.Dir8 Dir => Diablo2.Def.Dir8.S;
        public bool IsMoving => false;
        public bool IsRunning => true;
        public bool IsDead => _life <= 0;
        public int Defense => _baseDef;
        public int AttackRating => 20 + Dex * 4;

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

        /// <summary>测试用：直接给金币 / 生命（避免用全量 API 拼状态）。</summary>
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

        /// <summary>
        /// 与真 `<see cref="Diablo2.Module.PlayerModule.WriteTo"/>`（`Module/Player/PlayerModule.cs:448-477`）
        /// **逐字段对齐**：真实现只写属性/等级/经验/金币/**位置**，并在 `:473` 明文声明
        /// 「mapSeed / **areaId** / 背包 / 任务：分别由 Flow、Map、Item、Quest 负责，**这里不碰**」。
        /// </summary>
        public void WriteTo(CharacterSave save)
        {
            if (save == null) return;

            save.version = GameConst.SaveVersion;
            save.name = Name;
            save.cls = Class;
            save.level = Level;
            save.exp = Exp;
            save.str = _baseStr; save.dex = _baseDex; save.vit = _baseVit; save.eng = _baseEng;
            save.life = Life; save.mana = Mana; save.stamina = Stamina;
            save.statPoints = StatPoints; save.skillPoints = SkillPoints; save.gold = Gold;
            save.gridX = Grid.x;
            save.gridY = Grid.y;
            // 这里**不得**出现 `save.areaId = …`（真实现不写；写了本宿主的断言就永远是绿的）
        }

        public void Reset() { }
    }
}
