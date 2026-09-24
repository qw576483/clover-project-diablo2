// ─────────────────────────────────────────────────────────────────────────────
// 自检宿主专用：**替身模块 + 调用记录**（不是业务代码，不参与 Unity 打包）
//
// 为什么需要替身模块：
//   `Module/{Monster,Combat,Skill,View}` 依赖 `AppContext` 注入的**接口**
//   （`IPlayerModule` / `IItemModule` / `IAudioModule` / `IViewModule`）。
//   **功能可用的最小替身**：能真的扣血/加经验/加技能点、能记录"有没有被调用"，
//   从而让「伤害链 / 掉落触发 / 音效钩子 / 飘字」这些**跨模块交接点**在离线状态下可断言。
//
// `Trace` 是**同一次命中**的因果证据：`RecordingView` / `RecordingAudio` / 宿主的事件订阅
//   都往同一条有序日志里追加，断言"三件套出现在同一段调用里"就靠它。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Dir8 = Diablo2.Def.Dir8;
using Diablo2.Module;
using UnityEngine;

namespace CombatCheck
{
    /// <summary>有序因果日志（同一段调用里发生的事都追加到这里）。</summary>
    internal static class Trace
    {
        public static readonly List<string> Lines = new List<string>();

        public static void Add(string line)
        {
            Lines.Add(line);
        }

        public static void Marker(string name)
        {
            Lines.Add("== " + name + " ==");
        }

        /// <summary>从最后一条 <paramref name="marker"/> 之后的所有记录。</summary>
        public static List<string> Since(string marker)
        {
            var idx = Lines.LastIndexOf("== " + marker + " ==");
            if (idx < 0) return new List<string>();
            return Lines.GetRange(idx + 1, Lines.Count - idx - 1);
        }

        public static void Clear() { Lines.Clear(); }

        /// <summary>数一数某只怪出手了几次（`DamageDealt` 的 attackerId 就是它）。</summary>
        public static int AttacksBy(int monsterId)
        {
            var needle = "event.DamageDealt:from=" + monsterId + ",";
            var n = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }
    }

    /// <summary>把日志同时打到控制台并**留存**（断言靠留存的行）。</summary>
    internal sealed class RecordingLogger : CloverEngine.ILogger
    {
        public readonly List<string> Lines = new List<string>();

        public void Info(string tag, string msg) { Store("INFO ", tag, msg); }
        public void Warn(string tag, string msg) { Store("WARN ", tag, msg); }
        public void Error(string tag, string msg, Exception ex = null) { Store("ERROR", tag, msg + (ex != null ? " | " + ex.GetType().Name : "")); }
        public void Debug(string tag, string msg) { }
        public void Fatal(string tag, string msg, Exception ex = null) { Store("FATAL", tag, msg); }

        private void Store(string level, string tag, string msg)
        {
            var line = $"[{level}] [{tag}] {msg}";
            Lines.Add(line);
            Console.WriteLine(line);
        }

        /// <summary>是否出现过包含某子串的日志行。</summary>
        public bool Has(string needle)
        {
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        /// <summary>包含某子串的日志行数。</summary>
        public int Count(string needle)
        {
            var n = 0;
            for (var i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) n++;
            }
            return n;
        }

        /// <summary>包含某子串的**第一行**下标（-1 = 没出现）——用于断言日志的先后顺序。</summary>
        public int IndexOf(string needle)
        {
            return IndexOf(needle, 0);
        }

        /// <summary>从第 <paramref name="from"/> 行起找（用于只看"本次操作"产生的那几行）。</summary>
        public int IndexOf(string needle, int from)
        {
            for (var i = from < 0 ? 0 : from; i < Lines.Count; i++)
            {
                if (Lines[i].IndexOf(needle, StringComparison.Ordinal) >= 0) return i;
            }
            return -1;
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 引擎门面的最小实现（够 Bootstrap/Flow 编译与不崩；本宿主不跑 Flow）
    // ═════════════════════════════════════════════════════════════════════════

    internal sealed class RecUI : CloverEngine.IUIManager
    {
        public readonly List<string> Opened = new List<string>();
        public readonly List<string> Floats = new List<string>();
        public int ToastCount;

        public void Open<T>(object param = null) where T : class, CloverEngine.IUIPanel { Opened.Add(typeof(T).Name); }
        public void Close<T>() where T : class, CloverEngine.IUIPanel { }
        public void Close(string panelName) { }
        public void CloseAll() { Opened.Clear(); }
        public T Get<T>() where T : class, CloverEngine.IUIPanel => null;
        public bool IsOpen<T>() where T : class, CloverEngine.IUIPanel => false;
        public void Toast(string text, float duration = 2f) { ToastCount++; }
        public void FloatText(Vector3 worldPos, string text, Color? color = null, float duration = 1.2f)
        {
            Floats.Add(text);
        }
        public void ShowLoading(string text = null) { }
        public void HideLoading() { }
        public bool IsLoading => false;
        public void Confirm(string title, string message, Action onConfirm, Action onCancel = null,
            string confirmText = null, string cancelText = null) { }
        public void Tick(float dt) { }
    }

    internal sealed class SimpleFsm : CloverEngine.IFsm
    {
        private readonly Dictionary<string, string> _trans = new Dictionary<string, string>();
        public string Current { get; private set; }
        public void RegisterState(string state, Action onEnter = null, Action<float> onTick = null, Action onExit = null) { }
        public void Transition(string toState) { Current = toState; }
        public void AddTransition(string trigger, string toState) => _trans[trigger] = toState;
        public void Trigger(string trigger) { if (_trans.TryGetValue(trigger, out var to)) Current = to; }
        public void Force(string state) => Current = state;
        public void Tick(float dt) { }
        public void OnChange(Action<string, string> handler) { }
        public void OffChange(Action<string, string> handler) { }
    }

    internal sealed class MemSetting : CloverEngine.ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public T Get<T>(string key, T defaultValue = default) => _d.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { }
        public void Load() { }
        public void Delete(string key) => _d.Remove(key);
        public void DeleteAll() => _d.Clear();
    }

    internal sealed class FakeInput : CloverEngine.IInputManager
    {
        public bool Available => true;
        public bool IsLocked => false;
        public bool GetKey(CloverEngine.GameKey key) => false;
        public bool GetKeyDown(CloverEngine.GameKey key) => false;
        public bool GetKeyUp(CloverEngine.GameKey key) => false;
        public bool GetMouseButton(int button) => false;
        public bool GetMouseButtonDown(int button) => false;
        public Vector3 MousePosition => Vector3.zero;
    }

    internal sealed class FakeSound : CloverEngine.ISoundManager
    {
        public void PlayBGM(string clipName, float fadeTime = 0.5f) { }
        public void PlaySFX(string clipName) { }
        public void StopAll() { }
        public void SetVolume(CloverEngine.SoundGroup group, float volume) { }
        public float GetVolume(CloverEngine.SoundGroup group) => 1f;
        public void SetMute(CloverEngine.SoundGroup group, bool mute) { }
    }

    internal sealed class FakeEntities : CloverEngine.IEntityManager
    {
        private readonly List<CloverEngine.EntityInfo> _alive = new List<CloverEngine.EntityInfo>();
        public void ClearAll() { _alive.Clear(); }
        public IEnumerable<CloverEngine.EntityInfo> GetAll() => _alive;
    }

    internal sealed class FakePool : CloverEngine.IObjectPool
    {
        public void ClearAll() { }
    }

    internal sealed class FakeTimer : CloverEngine.ITimer
    {
        private long _id;
        public long After(float delay, Action callback) => ++_id;
        public long AfterUnscaled(float delay, Action callback) => ++_id;
        public long Every(float interval, Action callback) => ++_id;
        public long EveryUnscaled(float interval, Action callback) => ++_id;
        public void Stop(long id) { }
        public void StopNamed(string name) { }
        public void StopScope(string scope) { }
        public void StopAll() { }
        public void Tick(float dt) { }
    }

    /// <summary>永远取不到资源（⇒ 业务走"纯色占位"分支；本宿主无素材）。</summary>
    internal sealed class MissRes : CloverEngine.IResourceManager
    {
        public int LoadCount;
        public void LoadAsset<T>(string path, Action<T> cb) where T : UnityEngine.Object
        {
            LoadCount++;
            cb?.Invoke(null);
        }
        public T TryGet<T>(string path) where T : UnityEngine.Object => null;

        public bool Exists(string path) => false;
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object => Array.Empty<T>();
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 业务替身：IPlayerModule
    // ═════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// 玩家替身：**真的**扣血/加经验/扣蓝/加减技能点（这样"击杀链""扣蓝""升级"都能被断言）。
    /// 抗性/防御等派生值用固定公式（不是被测对象，只要稳定可复现）。
    /// </summary>
    internal sealed class FakePlayer : IPlayerModule
    {
        private readonly int[] _resist = new int[5];
        private int _level = 1;
        private long _exp;

        public PlayerClass Class { get; private set; } = PlayerClass.Amazon;
        public string Name { get; private set; } = "Check";
        public int Level => _level;
        public int Str { get; private set; } = 20;
        public int Dex { get; private set; } = 20;
        public int Vit { get; private set; } = 20;
        public int Eng { get; private set; } = 20;
        public int Life { get; private set; } = 50;
        public int MaxLife { get; private set; } = 50;
        public int Mana { get; private set; } = 50;
        public int MaxMana { get; private set; } = 50;
        public int Stamina { get; private set; } = 50;
        public int MaxStamina { get; private set; } = 50;
        public long Exp => _exp;
        public long ExpNext { get; private set; } = 500;
        public int StatPoints { get; private set; } = 5;
        public int SkillPoints { get; private set; }
        public int Gold { get; private set; } = 100;
        public Vector2Int Grid { get; private set; }
        public Vector3 World => Iso.GridToWorld(Grid);
        public Dir8 Dir { get; private set; } = Dir8.S;
        public bool IsMoving { get; private set; }

        /// <summary>
        /// 本宿主不测表现层 ⇒ 固定 `true`（= 原版默认跑，与 `PlayerModule._running` 的默认值一致）。
        /// </summary>
        public bool IsRunning => true;

        public bool IsDead { get; private set; }
        public int Defense => 10 + _level * 2;
        public int AttackRating => 20 + _level * 5;

        /// <summary>本宿主用：累计受到的伤害（断言"怪物真的打到了"）。</summary>
        public int TotalDamageTaken;

        public int GetResist(DamageType type) => _resist[(int)type];

        public void SetResist(int physical, int fire, int cold, int light, int poison)
        {
            _resist[0] = physical; _resist[1] = fire; _resist[2] = cold; _resist[3] = light; _resist[4] = poison;
        }

        /// <summary>本宿主用：直接摆位置（AI 测试要把玩家放到怪物旁边）。</summary>
        public void SetGrid(Vector2Int g, Dir8 dir = Dir8.S)
        {
            Grid = g;
            Dir = dir;
        }

        public void SetMana(int mana) { Mana = mana; }
        public void SetLife(int life) { Life = life; }
        public void SetLevel(int level) { _level = level; RefreshExpNext(); }
        public void SetSkillPoints(int n) { SkillPoints = n; }

        public PlayerStatsDto Snapshot()
        {
            return new PlayerStatsDto
            {
                name = Name,
                cls = Class,
                level = _level,
                exp = _exp,
                expNext = ExpNext,
                str = Str,
                dex = Dex,
                vit = Vit,
                eng = Eng,
                life = Life,
                maxLife = MaxLife,
                mana = Mana,
                maxMana = MaxMana,
                stamina = Stamina,
                maxStamina = MaxStamina,
                defense = Defense,
                attackRating = AttackRating,
                fireResist = _resist[1],
                coldResist = _resist[2],
                lightResist = _resist[3],
                poisonResist = _resist[4],
                statPoints = StatPoints,
                skillPoints = SkillPoints,
                gold = Gold,
            };
        }

        public void CreateNew(PlayerClass cls, string name)
        {
            Class = cls;
            Name = name;
            _level = 1;
            _exp = 0;
            RefreshExpNext();
            Life = MaxLife;
            Mana = MaxMana;
            IsDead = false;
        }

        public void LoadFrom(CharacterSave save)
        {
            if (save == null) return;
            Class = save.cls;
            Name = save.name;
            _level = save.level < 1 ? 1 : save.level;
            _exp = save.exp;
            SkillPoints = save.skillPoints;
            StatPoints = save.statPoints;
            Gold = save.gold;
            Life = save.life > 0 ? save.life : MaxLife;
            Mana = save.mana > 0 ? save.mana : MaxMana;
            Grid = new Vector2Int(save.gridX, save.gridY);
            RefreshExpNext();
        }

        public void WriteTo(CharacterSave save)
        {
            if (save == null) return;
            save.cls = Class;
            save.name = Name;
            save.level = _level;
            save.exp = _exp;
            save.skillPoints = SkillPoints;
            save.statPoints = StatPoints;
            save.gold = Gold;
            save.life = Life;
            save.mana = Mana;
            save.gridX = Grid.x;
            save.gridY = Grid.y;
        }

        public void Reset()
        {
            _level = 1;
            _exp = 0;
            IsDead = false;
            IsMoving = false;
            RefreshExpNext();
        }

        public void MoveTo(Vector2Int target) { Grid = target; }
        public void Stop() { IsMoving = false; }
        public void TeleportTo(Vector2Int grid) { Grid = grid; }
        public void Tick(float dt) { }

        public bool ApplyDamage(int amount, DamageType type)
        {
            if (IsDead) return false;

            var res = DamageFormulaClamp(_resist[(int)type]);
            var final = amount * (100 - res) / 100;
            if (final < 0) final = 0;
            TotalDamageTaken += final;
            Life -= final;
            if (Life > 0) return false;

            Life = 0;
            Kill();
            return true;
        }

        private static int DamageFormulaClamp(int r) => r < -100 ? -100 : (r > 75 ? 75 : r);

        public void Heal(int amount) { Life = Math.Min(MaxLife, Life + amount); }
        public void RestoreMana(int amount) { Mana = Math.Max(0, Math.Min(MaxMana, Mana + amount)); }

        /// <summary>
        /// w7 契约新增（`IPlayerModule.TrySpendMana`）的替身实现：与真实 `PlayerModule` **同语义** ——
        /// 成功扣减返回 true；`amount ≤ 0` 或法力不足返回 false 且**不扣**。
        /// </summary>
        public bool TrySpendMana(int amount)
        {
            if (amount <= 0 || Mana < amount) return false;
            Mana -= amount;
            return true;
        }

        public void RestoreStamina(int amount) { Stamina = Math.Max(0, Math.Min(MaxStamina, Stamina + amount)); }

        public void AddExp(int amount)
        {
            if (amount <= 0) return;
            _exp += amount;

            var guard = 0;
            while (ExpNext > 0 && _exp >= ExpNext && guard++ < 200)
            {
                _exp -= ExpNext;
                _level++;
                StatPoints += 5;
                SkillPoints += 1;
                MaxLife += 5;
                MaxMana += 3;
                Life = MaxLife;
                Mana = MaxMana;
                RefreshExpNext();
            }
        }

        /// <summary>
        /// 升到下一级所需经验。
        /// <para>
        /// `experience_c` 的口径（读 `Table/Base/BaseExperience.cs` + 实测数据）：
        /// 第 <c>level</c> 行的 <c>exp</c> = 该行所对应等级的**累计**经验（level=1 → 500，
        /// 即"1 级升 2 级需 500"）。故升到下一级所需 = `exp[level] - exp[level-1]`（exp[0]=0）。
        /// </para>
        /// </summary>
        private void RefreshExpNext()
        {
            var cur = ExpOfLevel(_level);
            var prev = ExpOfLevel(_level - 1);
            ExpNext = cur > prev ? cur - prev : 0;
        }

        private static long ExpOfLevel(int level)
        {
            if (level <= 0) return 0;
            var row = Table.Tables.Default.Experience.Get(level);
            return row != null ? row.Exp : 0;
        }

        public bool AddGold(int amount)
        {
            if (amount < 0 && Gold + amount < 0) return false;
            Gold += amount;
            return true;
        }

        public void AddSkillPoint(int delta)
        {
            SkillPoints += delta;
            if (SkillPoints < 0) SkillPoints = 0;
        }

        public bool AllocateStat(StatKind kind, int delta)
        {
            if (delta > StatPoints) return false;
            StatPoints -= delta;
            switch (kind)
            {
                case StatKind.Strength: Str += delta; break;
                case StatKind.Dexterity: Dex += delta; break;
                case StatKind.Vitality: Vit += delta; break;
                case StatKind.Energy: Eng += delta; break;
            }
            return true;
        }

        public void Revive()
        {
            IsDead = false;
            Life = Math.Max(1, MaxLife / 2);
            Mana = MaxMana / 2;
        }

        public void Kill()
        {
            if (IsDead) return;
            IsDead = true;
            Life = 0;
            if (Game.Event != null) Game.Event.Emit(Events.PlayerDied);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // 业务替身：IItemModule / IAudioModule / IViewModule（记录调用）
    // ═════════════════════════════════════════════════════════════════════════

    internal sealed class RecordingItem : IItemModule
    {
        public readonly List<string> DropLootCalls = new List<string>();
        public readonly List<Vector2Int> DropGrids = new List<Vector2Int>();

        /// <summary>
        /// （`CombatModule.GetWeaponDamage` 读的就是它）。默认空 = 徒手。
        /// </summary>
        public readonly List<ItemStack> EquipmentOverride = new List<ItemStack>();

        public int Gold => 100;
        public IReadOnlyList<InventorySlot> Inventory => new List<InventorySlot>();
        public IReadOnlyList<ItemStack> Equipment => EquipmentOverride;
        public IReadOnlyList<ItemStack> Belt => new List<ItemStack>();
        public IReadOnlyList<KeyValuePair<int, ItemStack>> GroundItems => new List<KeyValuePair<int, ItemStack>>();
        public bool IsFull => false;

        public InventoryChangedArgs Snapshot() => new InventoryChangedArgs();
        public ItemStack CreateRandom(int level, Rng rng) => null;

        public void DropLoot(int treasureClassId, int monsterLevel, Vector2Int grid, Rng rng)
        {
            DropLootCalls.Add($"tc={treasureClassId} level={monsterLevel} grid=({grid.x},{grid.y}) rng={rng?.Seed}");
            DropGrids.Add(grid);
            Trace.Add($"item.DropLoot:tc={treasureClassId},level={monsterLevel},grid=({grid.x},{grid.y})");
        }

        public void DropToGround(ItemStack item, Vector2Int grid) { }
        public bool Pickup(int groundItemId) => false;
        public bool PickupNearest(Vector2Int grid, float maxRange) => false;
        public bool AddToInventory(ItemStack item) => false;
        /// <summary>换格（契约成员 `IItemModule.MoveItem`，2026-09-23 新增）。
        /// 本宿主只判战斗链路 ⇒ 替身不动格子、给可定位拒绝原因（不返回"成功"以免掩盖调用方假设）。</summary>
        public bool MoveItem(int fromAnchor, int toAnchor, out string failReason)
        {
            failReason = $"RecordingItem 替身不支持换格（from={fromAnchor} to={toAnchor}）";
            return false;
        }
        public bool RemoveFromInventory(int anchorIndex) => false;
        public bool EquipFromInventory(int anchorIndex) => false;
        public bool Unequip(ItemSlot slot, int slotIndex) => false;
        public bool UseItem(int anchorIndex) => false;
        public bool UseBeltSlot(int index) => false;
        public bool AddToBelt(ItemStack item, int index) => false;
        public bool AddGold(int amount) => true;
        public int Repair(int anchorIndex) => 0;
        public int GetRepairAllCost() => 0;
        public void LoadFrom(CharacterSave save) { }
        public void WriteTo(CharacterSave save) { }
        public void Tick(float dt) { }
        public void Reset() { }
    }

    /// <summary>音效替身：记录每一次 `Sfx`/`SfxAt`（命中音效钩子的证据）。</summary>
    internal sealed class RecordingAudio : IAudioModule
    {
        public readonly List<string> Calls = new List<string>();

        public float BgmVolume => 0.7f;
        public float SfxVolume => 0.8f;

        public void Sfx(string key)
        {
            Calls.Add("sfx:" + key);
            Trace.Add("sfx:" + key);
        }

        public void SfxAt(string key, float worldX, float worldY, float worldZ)
        {
            Calls.Add("sfxAt:" + key);
            Trace.Add($"sfxAt:{key}");
        }

        public void Bgm(string key) { }
        public void StopBgm() { }
        public void SetVolume(float bgm, float sfx) { }
        public void SetMute(bool bgmMute, bool sfxMute) { }
        public AudioVolumeArgs GetVolume() => new AudioVolumeArgs();
        public void Tick(float dt) { }
        public void Reset() { }

        public bool Has(string keyPart)
        {
            for (var i = 0; i < Calls.Count; i++)
            {
                if (Calls[i].IndexOf(keyPart, StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }
    }

    /// <summary>视图替身：记录飘字/受击/死亡/血条刷新（三件套里的 ① 与 ③ 靠它取证）。</summary>
    internal sealed class RecordingView : IViewModule
    {
        public readonly List<string> Floats = new List<string>();
        public readonly List<int> Hits = new List<int>();
        public readonly List<int> Deaths = new List<int>();
        public readonly List<string> HpUpdates = new List<string>();
        public readonly List<string> MonstersCreated = new List<string>();
        public readonly List<int> MonstersRemoved = new List<int>();
        public readonly List<string> GroundItems = new List<string>();
        public int ClearCount;
        public int ClearMonstersCount;
        public uint LastArgb;

        public void CreatePlayer(PlayerClass cls) { Trace.Add("view.CreatePlayer:" + cls); }

        public void CreateMonster(MonsterState state)
        {
            MonstersCreated.Add(state.id.ToString());
            Trace.Add("view.CreateMonster:" + state.id);
        }

        public void UpdateMonster(MonsterState state)
        {
            HpUpdates.Add($"{state.id}:{state.hp}/{state.maxHp}");
            Trace.Add($"monsterHp:{state.id}={state.hp}/{state.maxHp}");
        }

        public void RemoveMonster(int monsterId)
        {
            MonstersRemoved.Add(monsterId);
            Trace.Add("view.RemoveMonster:" + monsterId);
        }

        public void ClearMonsters() { ClearMonstersCount++; }

        public void CreateGroundItem(int groundItemId, ItemStack item, Vector2Int grid)
        {
            GroundItems.Add($"{groundItemId}:{(item != null ? item.name : "?")}@({grid.x},{grid.y})");
        }

        public void RemoveGroundItem(int groundItemId) { }

        public void PlayHit(int entityId)
        {
            Hits.Add(entityId);
            Trace.Add("view.PlayHit:" + entityId);
        }

        public void PlayDeath(int entityId)
        {
            Deaths.Add(entityId);
            Trace.Add("view.PlayDeath:" + entityId);
        }

        public void ShowFloatingText(float worldX, float worldY, float worldZ, string text, uint argb)
        {
            Floats.Add(text);
            LastArgb = argb;
            Trace.Add("float:" + text);
        }

        public GameObject GetView(int entityId) => null;
        public void Tick(float dt) { }

        public void Clear()
        {
            ClearCount++;
            Trace.Add("view.Clear");
        }

        /// <summary>最后一次血条更新的血量（三件套的 ③ 取证）。</summary>
        public string LastHpUpdate => HpUpdates.Count > 0 ? HpUpdates[HpUpdates.Count - 1] : "(none)";
    }
}
