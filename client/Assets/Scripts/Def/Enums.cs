// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/Enums.cs
// 纯枚举与纯数据（**无逻辑、无 Unity 依赖**）。
//
//    取值落盘（存档 / 配表主键）后不许改。
//
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Def
{
    /// <summary>
    /// 5 个职业。取值 = 配表 `class_c` 的主键（职业 id 1-5）。
    /// </summary>
    public enum PlayerClass
    {
        Amazon = 1,       // 亚马逊
        Sorceress = 2,    // 法师
        Necromancer = 3,  // 死灵法师
        Paladin = 4,      // 圣骑士
        Barbarian = 5,    // 野蛮人
    }

    /// <summary>
    /// 伤害/抗性类型。**前 5 系与官方一致**；数组下标即本枚举值（抗性表按元素数组存）。
    /// <para>
    /// `EType=mag`（「魔法」伤害）走**独立的 `ResMa` 抗性**（`MonStats.txt` 的 `ResMa` 列；
    /// 本项目配表 `monster_c.res_magic` **早就导出了**，只是一直没人消费）。
    /// </para>
    /// <para>
    /// 已落盘的 tsv、离线断言都依赖它。
    /// </para>
    /// </summary>
    public enum DamageType
    {
        Physical = 0,   // 物理
        Fire = 1,       // 火焰
        Cold = 2,       // 冰冷
        Lightning = 3,  // 闪电
        Poison = 4,     // 毒素
        Magic = 5,      // 魔法（官方 EType=mag ⇒ 走 ResMa）
    }

    /// <summary>物品品质（原版：普通 / 魔法 / 稀有 / 套装 / 暗金）。</summary>
    public enum ItemQuality
    {
        Normal = 0,   // 白
        Magic = 1,    // 蓝（词缀）
        Rare = 2,     // 金
        Set = 3,      // 绿
        Unique = 4,   // 暗金
    }

    /// <summary>
    /// 装备栏槽位。<see cref="Ring"/> **允许装备两件**（原版双戒指）：
    /// 装备列表按本枚举分组，`Ring` 组内最多 2 件。
    /// </summary>
    public enum ItemSlot
    {
        None = 0,
        Helm = 1,      // 头盔
        Armor = 2,     // 盔甲
        Weapon = 3,    // 武器
        Shield = 4,    // 盾
        Gloves = 5,    // 手套
        Boots = 6,     // 靴子
        Belt = 7,      // 腰带
        Amulet = 8,    // 项链
        Ring = 9,      // 戒指（两枚）
    }

    /// <summary>怪物 AI 类型（配表 `monster_c.ai` 列直接写本枚举名）。</summary>
    public enum MonsterAI
    {
        Melee = 0,   // 近战追击
        Range = 1,   // 远程射击
        Shaman = 2,  // 萨满（复活同伴 / 施法）
        Coward = 3,  // 逃跑型（尖刺鼠 / 堕落者）
    }

    /// <summary>任务状态机状态（配表无关，运行时状态）。</summary>
    public enum QuestState
    {
        NotStarted = 0,      // 未接取
        InProgress = 1,      // 进行中
        ReadyToTurnIn = 2,   // 目标已完成，可交付
        Done = 3,            // 已完成
    }

    /// <summary>
    /// 格子地形类型。**可走性唯一判定在 <see cref="TileKindInfo.IsWalkable"/>**，
    /// 不许在业务里再写一份 `<c>kind == ...</c>` 判等表。
    /// </summary>
    public enum TileKind
    {
        Void = 0,        // 图外/未生成（**不可走**）
        Grass = 1,       // 草地（可走）
        Dirt = 2,        // 泥土（可走）
        Road = 3,        // 道路（可走）
        Rock = 4,        // 岩石（不可走）
        Tree = 5,        // 树（不可走）
        Fence = 6,       // 栅栏（不可走）
        Wall = 7,        // 石墙（不可走）
        CaveFloor = 8,   // 洞穴地面（可走）
        CaveWall = 9,    // 洞穴岩壁（不可走）
        Exit = 10,       // 场景出入口（**可走**，并带特殊语义）
        TownFloor = 11,  // 城镇地面（可走）

        /// <summary>
        /// 水（河 / 水塘 / 水域地面）—— **不可走**（原版不可涉水）。
        /// <para>
        /// （`MapGenTown.KindOf('r')` / `MapGenWilderness.KindOf('X')`），于是「水」在
        /// `TileKind` 层**没有语义**。本值把**城镇布局的 `'r'`**（= 原版 `Levels.txt` /
        /// `LvlTypes.txt` 的水域地形）单独摘出来。
        /// </para>
        /// <para>
        /// 出处：① `MapGenTownLayout.cs:28` 的地图键 —— `'r'` = **水（阻挡）**（生成物，
        /// 源 `data/global/tiles/ACT1/TOWN/*.ds1`）；② 水格的 floor 键全是 `moor_river/*`
        /// （`Tiles/moor_river` = `ACT1/OUTDOORS/river.dt1` 解出的**水瓦片**，
        /// 见 `MapView.PaletteCycledFlatWallTiles` 的取证）；③ 原版水**不可涉水**
        /// ⇒ 可走性必须保持 `false`（`TileKindInfo.IsWalkable`）。
        /// </para>
        /// <para>
        /// </para>
        /// </summary>
        Water = 12,
    }

    /// <summary>
    /// 区域（地图）。取值 = 配表 `level_c` 的主键。
    /// </summary>
    public enum AreaId
    {
        Town = 0,        // 罗格营地（固定布局）
        BloodMoor = 1,   // 血腥荒野（随机生成）
        DenOfEvil = 2,   // 邪恶洞穴（随机生成，任务地牢）
    }

    /// <summary>四维属性。分配入口：`IPlayerModule.AllocateStat(StatKind, int)`（`Module/Contracts.cs`）。</summary>
    public enum StatKind
    {
        Strength = 0,    // 力量
        Dexterity = 1,   // 敏捷
        Vitality = 2,    // 体力
        Energy = 3,      // 精力
    }

    /// <summary>
    /// NPC。取值 = 配表无关的固定 id（Act I 起始 5 个 NPC）。
    /// </summary>
    public enum NpcId
    {
        None = -1,
        Akara = 0,     // 阿卡拉（任务发布者，药剂）
        Kashya = 1,    // 卡夏（罗格弓箭手首领）
        Charsi = 2,    // 恰西（铁匠：武器/防具/修理）
        Gheed = 3,     // 基德（商人：杂货）
        Warriv = 4,    // 瓦瑞夫（商队首领）
    }

    /// <summary>任务 id。取值 = 任务日志的键。</summary>
    public enum QuestId
    {
        None = 0,
        DenOfEvil = 1,   // 邪恶洞穴（主线任务 1）
    }

    /// <summary>物品大类（配表 `item_c.type` 列）。</summary>
    public enum ItemType
    {
        Weapon = 0,   // 武器
        Armor = 1,    // 防具
        Misc = 2,     // 杂项（药水 / 卷轴 / 金币 / 任务物品）
    }

    /// <summary>魔法词缀种类（配表 `affix_c.kind` 列）。</summary>
    public enum AffixKind
    {
        Prefix = 0,   // 前缀
        Suffix = 1,   // 后缀
    }

    /// <summary>技能目标类型（决定施放时怎么用目标格）。</summary>
    public enum SkillTarget
    {
        None = 0,     // 无目标（自身增益）
        Self = 1,     // 自己
        Enemy = 2,    // 需要敌方目标
        Ground = 3,   // 指向地面（投射物 / 范围）
    }

    /// <summary>
    /// 交互光标形态（原版 `Cursor.png` 多帧）。取值顺序 = 光标帧顺序，
    /// </summary>
    public enum CursorKind
    {
        Default = 0,    // 普通箭头
        Attack = 1,     // 可攻击（怪物）
        Interact = 2,   // 可交互（NPC / 门 / 出口）
        Pickup = 3,     // 可拾取（地面物品）
        NoWalk = 4,     // 不可到达
    }

    /// <summary>
    /// 8 方向朝向。**编号沿用暗黑2 的顺时针序（0 = 南）**，
    /// 逻辑含义见 <c>Core/Iso.DirectionTo</c> 的方向 ↔ 格增量映射表。
    /// </summary>
    public enum Dir8
    {
        S = 0,    // 南（格增量 +gy）
        SW = 1,   // 西南
        W = 2,    // 西
        NW = 3,   // 西北
        N = 4,    // 北（格增量 -gy）
        NE = 5,   // 东北
        E = 6,    // 东
        SE = 7,   // 东南
    }

    /// <summary>
    /// <see cref="TileKind"/> 的**唯一可走性判定**（纯数据映射，无 Unity 依赖）。
    /// `IMapModule.Walkable` 必须复用本表，禁止在业务里再写一份判等表。
    /// </summary>
    public static class TileKindInfo
    {
        /// <summary>该地形是否可走（角色/怪物可站立）。</summary>
        public static bool IsWalkable(TileKind kind)
        {
            switch (kind)
            {
                case TileKind.Grass:
                case TileKind.Dirt:
                case TileKind.Road:
                case TileKind.CaveFloor:
                case TileKind.TownFloor:
                case TileKind.Exit:
                    return true;

                case TileKind.Void:
                case TileKind.Rock:
                case TileKind.Tree:
                case TileKind.Fence:
                case TileKind.Wall:
                case TileKind.CaveWall:
                //   default 只是"忘了登记"的兜底，不代表"水本来就该这么判"。
                case TileKind.Water:
                    return false;

                default:
                    // 新增 TileKind 却忘了登记可走性：按不可走处理，并让调用方打日志（Map 模块）
                    return false;
            }
        }

        /// <summary>该地形是否阻挡视线/寻路（含不可走的全部情况）。</summary>
        public static bool IsBlocking(TileKind kind) => !IsWalkable(kind);

        /// <summary>是否为地面层地形（渲染时画在 GroundLayer）。</summary>
        public static bool IsGroundLayer(TileKind kind)
        {
            return kind == TileKind.Grass || kind == TileKind.Dirt || kind == TileKind.Road
                || kind == TileKind.CaveFloor || kind == TileKind.TownFloor || kind == TileKind.Exit
                || kind == TileKind.Water;
        }
    }
}
