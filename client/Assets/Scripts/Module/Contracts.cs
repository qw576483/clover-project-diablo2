// ═════════════════════════════════════════════════════════════════════════════
// Diablo2 · Module/Contracts.cs  ★★★ 全模块契约（**冻结**）★★★
//
// 本文件 = 后续 agent 的**唯一接口依据**：只放 `public interface` 与可序列化 DTO，
// **不含任何实现**（实现分别落在 `Module/{Flow,Map,Player,Combat,Monster,Skill,Item,Quest,Npc,
// Camera,View,Audio,Save}/`，按 `tools/ai-skill/registry.md` 的模块表）。
//
// ── 命名空间划分（**故意分成两个**，改动前先读这段）──────────────────────────
//   `Diablo2.Module`  模块门面接口（模块之间 + App 装配用）
//   `Diablo2.Def`     DTO（纯数据）—— 放在 Def 而不是 Module，有三条硬理由：
//     ① 分层自检 ③ 要求 `UI/**` **不许** `using Diablo2.Module`，而面板必须收发 DTO
//        （`Game.UI.Open<T>(param)` 的参数就是 DTO）⇒ DTO 若在 `Diablo2.Module` 里，
//        面板只有两个选择：违规，或写 `object` 强转（都不行）；
//     ② `Diablo2.Def` 的定位就是「枚举与**纯数据**定义」，DTO 正是纯数据；
//     ③ 为保住「Def 无 Unity 依赖」，**所有 DTO 字段只用 int/float/string/enum/List<T>**，
//        不出现 `Vector2Int`/`Vector3`（格坐标用 `gridX/gridY`，世界坐标用 `worldX/Y/Z`）。
//   ⇒ 模块实现：`using Diablo2.Def;` + `using Diablo2.Core;`（`Diablo2.Module` 由所在命名空间自动可见）
//   ⇒ UI 面板：只需 `using Diablo2.Def;` + `using Diablo2.Core;`
//   ⇒ App 装配（AppContext）：`using Diablo2.Module;`
//
// ── 冻结声明 ────────────────────────────────────────────────────────────────
// ⛔ 接口签名与 DTO 字段名**即契约**：后续 agent 照它实现，**写好后不许改**。
//    发现契约有问题 → **停下来回报主 agent**，不许自己改。
// ⛔ 契约 §3.5 点名的事件参数类型（`DamageArgs` / `ItemStack` / `QuestStateDto` / `MonsterState`）
//    不许改字段名与语义（事件名与参数类型见 `Core/Events.cs`）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
// ★ agent-33 引擎下沉 A2：引擎侧新增了**同名**枚举 `CloverEngine.Dir8`（`Runtime/Core/Dir8.cs`），
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（语义与序号和改动前**完全一致**）。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
// DTO（命名空间 = Diablo2.Def，纯数据、无 Unity 类型）
// ═════════════════════════════════════════════════════════════════════════════
namespace Diablo2.Def
{
    /// <summary>物品魔法词缀（配表 `affix_c` 的一条实例化结果）。</summary>
    [Serializable]
    public class ItemAffix
    {
        /// <summary>`affix_c` 主键。</summary>
        public int affixId;

        /// <summary>前缀 / 后缀。</summary>
        public AffixKind kind;

        /// <summary>显示名（如「+5 力量的」）。</summary>
        public string name;

        /// <summary>加成对象（配表 `affix_c.mod` 列，如 `str` / `maxhp` / `fireres`）。</summary>
        public string mod;

        /// <summary>数值下限。</summary>
        public int min;

        /// <summary>数值上限。</summary>
        public int max;

        /// <summary>本次生成实际取到的数值（min~max 之间）。</summary>
        public int value;
    }

    /// <summary>
    /// 一叠物品（物品实例）。**契约 §3.5**：`Events.ItemPicked` / `ItemDropped` / `ItemUsed` 的参数。
    /// 叠放：`count &gt; 1` 表示同 id 叠加（药水/卷轴/金币）。
    /// </summary>
    [Serializable]
    public class ItemStack
    {
        /// <summary>`item_c` 主键（0 = 空/无效）。</summary>
        public int itemId;

        /// <summary>显示名（含词缀前缀/后缀后的完整名）。</summary>
        public string name;

        /// <summary>物品大类。</summary>
        public ItemType type;

        /// <summary>品质（决定名字颜色与 tooltip 配色）。</summary>
        public ItemQuality quality;

        /// <summary>叠加数量（≥1）。</summary>
        public int count = 1;

        /// <summary>占格宽（配表 `item_c.grid_w`）。</summary>
        public int gridW = 1;

        /// <summary>占格高（配表 `item_c.grid_h`）。</summary>
        public int gridH = 1;

        /// <summary>等级需求。</summary>
        public int lvlReq;

        /// <summary>力量需求。</summary>
        public int strReq;

        /// <summary>基础最小伤害（武器）。</summary>
        public int dmgMin;

        /// <summary>基础最大伤害（武器）。</summary>
        public int dmgMax;

        /// <summary>基础最小防御（防具）。</summary>
        public int defMin;

        /// <summary>基础最大防御（防具）。</summary>
        public int defMax;

        /// <summary>商店价（买卖折算比例由 Npc 模块定）。</summary>
        public int price;

        /// <summary>词缀列表（普通物品为空列表）。</summary>
        public List<ItemAffix> affixes = new List<ItemAffix>();

        /// <summary>耐久（0 = 不适用/不损坏）。</summary>
        public int durability;

        /// <summary>耐久上限。</summary>
        public int maxDurability;

        /// <summary>是否为金币袋（拾取时直接加金币、不进背包）。</summary>
        public bool isGold;

        /// <summary>是否为任务物品（不可丢弃/不可出售）。</summary>
        public bool isQuestItem;

        /// <summary>该物品是否是装备（可穿戴）。</summary>
        public bool IsEquipment => type == ItemType.Weapon || type == ItemType.Armor;
    }

    /// <summary>
    /// 背包的一格。多格物品占用多格：**锚点格**（`IsAnchor`）持有 <see cref="item"/>，
    /// 其余被占用的格 <c>occupied = true</c> 且 <c>item = null</c>。
    /// </summary>
    [Serializable]
    public class InventorySlot
    {
        /// <summary>线性索引（行优先）：`index = y * GameConst.InventoryCols + x`，范围 0..39。</summary>
        public int index;

        /// <summary>列（0..9）。</summary>
        public int x;

        /// <summary>行（0..3）。</summary>
        public int y;

        /// <summary>本格是否被物品占用。</summary>
        public bool occupied;

        /// <summary>本格是否为物品锚点（左上角格）。</summary>
        public bool isAnchor;

        /// <summary>物品（仅锚点格非 null；其余占用格为 null）。</summary>
        public ItemStack item;

        /// <summary>锚点格的线性索引（非占用格为 -1）。</summary>
        public int anchorIndex = -1;
    }

    /// <summary>
    /// 一次伤害结算结果。**契约 §3.5**：`Events.DamageDealt` / `PlayerDamaged` 的参数；
    /// 飘字位置取 <see cref="worldX"/> / <see cref="worldY"/> / <see cref="worldZ"/>。
    /// </summary>
    [Serializable]
    public class DamageArgs
    {
        /// <summary>攻击者实体 id（玩家 = `GameConst.PlayerEntityId`；-1 = 环境伤害）。</summary>
        public int attackerId;

        /// <summary>受击者实体 id（怪物 id，或 `GameConst.PlayerEntityId`）。</summary>
        public int targetId;

        /// <summary>结算前伤害（未减抗/未减防）。</summary>
        public int raw;

        /// <summary>结算后实际扣除的血量（0 = 未命中/被完全免疫）。</summary>
        public int amount;

        /// <summary>伤害类型。</summary>
        public DamageType type;

        /// <summary>受击者是否为玩家。</summary>
        public bool targetIsPlayer;

        /// <summary>是否命中（false = 未命中/被闪避）。</summary>
        public bool hit;

        /// <summary>是否致命一击（暴击）。</summary>
        public bool critical;

        /// <summary>是否为持续伤害（毒素等）。</summary>
        public bool overTime;

        /// <summary>受击者剩余血量（结算后）。</summary>
        public int targetHpAfter;

        /// <summary>是否因此击杀。</summary>
        public bool killed;

        /// <summary>飘字/命中特效的世界坐标 X（2D 平面，z 恒 0）。</summary>
        public float worldX;

        /// <summary>飘字/命中特效的世界坐标 Y。</summary>
        public float worldY;

        /// <summary>飘字/命中特效的世界坐标 Z。</summary>
        public float worldZ;
    }

    /// <summary>
    /// 一只怪物的运行时状态。**契约 §3.5**：`IMonsterModule.All` 的元素类型。
    /// 由 `MonsterModule` 原地更新（引用类型，视图侧不要缓存副本）。
    /// </summary>
    [Serializable]
    public class MonsterState
    {
        /// <summary>实体 id（≥ `GameConst.MonsterIdBase`）。</summary>
        public int id;

        /// <summary>`monster_c` 主键（怪物种类）。</summary>
        public int kindId;

        /// <summary>显示名（含精英前缀，如「堕落者」/「尖刺鼠」）。</summary>
        public string name;

        /// <summary>怪物等级（区域等级 × 种类修正后）。</summary>
        public int level;

        /// <summary>当前生命。</summary>
        public int hp;

        /// <summary>生命上限。</summary>
        public int maxHp;

        /// <summary>所在格 X。</summary>
        public int gridX;

        /// <summary>所在格 Y。</summary>
        public int gridY;

        /// <summary>世界坐标 X（视图用）。</summary>
        public float worldX;

        /// <summary>世界坐标 Y。</summary>
        public float worldY;

        /// <summary>世界坐标 Z。</summary>
        public float worldZ;

        /// <summary>AI 类型。</summary>
        public MonsterAI ai;

        /// <summary>是否存活（false 后仍可保留尸体信息）。</summary>
        public bool alive = true;

        /// <summary>是否精英怪（带 `monumod_c` 词缀）。</summary>
        public bool isChampion;

        /// <summary>`monumod_c` 主键（0 = 无精英词缀）。</summary>
        public int modId;

        /// <summary>精英词缀名（用于头顶显示）。</summary>
        public string modName;

        /// <summary>最小伤害。</summary>
        public int damageMin;

        /// <summary>最大伤害。</summary>
        public int damageMax;

        /// <summary>防御（被命中判定用）。</summary>
        public int defense;

        /// <summary>命中（AR）。</summary>
        public int attackRating;

        /// <summary>击杀经验。</summary>
        public int exp;

        /// <summary>当前朝向。</summary>
        public Dir8 dir = Dir8.S;

        /// <summary>尸体是否可被利用（萨满复活 / 死灵技能）。</summary>
        public bool corpseUsable;

        /// <summary>是否已被标记为「正在攻击」（视图播攻击动画用）。</summary>
        public bool attacking;

        /// <summary>是否处于受击硬直（视图播 Hit 动画用）。</summary>
        public bool hitStun;
    }

    /// <summary>
    /// 一个任务的运行时状态。**契约 §3.5**：`Events.QuestChanged` 的参数。
    /// </summary>
    [Serializable]
    public class QuestStateDto
    {
        /// <summary>任务 id（见 <see cref="QuestId"/>）。</summary>
        public int questId;

        /// <summary>任务名（中文）。</summary>
        public string name;

        /// <summary>当前状态。</summary>
        public QuestState state;

        /// <summary>目标进度（邪恶洞穴 = 已清怪数）。</summary>
        public int progress;

        /// <summary>目标进度上限（邪恶洞穴 = 洞内初始怪物总数）。</summary>
        public int required;

        /// <summary>奖励是否已领（防重复发放）。</summary>
        public bool rewardClaimed;

        /// <summary>目标描述（任务日志显示用，随状态变化）。</summary>
        public string objective;
    }

    /// <summary>NPC 静态定义（城镇布局 + 商店能力）。</summary>
    [Serializable]
    public class NpcDef
    {
        /// <summary>NPC id（见 <see cref="NpcId"/>）。</summary>
        public int id;

        /// <summary>显示名（中文）。</summary>
        public string name;

        /// <summary>所在格 X（罗格营地固定布局）。</summary>
        public int gridX;

        /// <summary>所在格 Y。</summary>
        public int gridY;

        /// <summary>所在区域（见 <see cref="AreaId"/>）。</summary>
        public int areaId;

        /// <summary>是否为任务发布者（对话里可接/交任务）。</summary>
        public bool isQuestGiver;

        /// <summary>是否有商店（可买卖）。</summary>
        public bool hasShop;

        /// <summary>是否提供修理。</summary>
        public bool canRepair;

        /// <summary>是否为铁匠（武器/防具）。</summary>
        public bool isBlacksmith;
    }

    /// <summary>技能静态定义（技能树面板 + 施放参数）。</summary>
    [Serializable]
    public class SkillDef
    {
        /// <summary>`skill_c` 主键。</summary>
        public int id;

        /// <summary>所属职业。</summary>
        public PlayerClass cls;

        /// <summary>技能树分支序号（0/1/2 = 该职业的三系）。</summary>
        public int tree;

        /// <summary>技能树面板列（0 起）。</summary>
        public int slotCol;

        /// <summary>技能树面板行（0 起）。</summary>
        public int slotRow;

        /// <summary>技能名（中文）。</summary>
        public string name;

        /// <summary>说明文本。</summary>
        public string desc;

        /// <summary>需求等级。</summary>
        public int reqLevel;

        /// <summary>前置技能 id（0 = 无）。</summary>
        public int reqSkill;

        /// <summary>前置技能需求等级（reqSkill != 0 时生效）。</summary>
        public int reqSkillLevel;

        /// <summary>技能等级上限（含加成）。</summary>
        public int maxLevel = 20;

        /// <summary>基础法力消耗（随等级线性增长的增量由 Skill 模块按公式算）。</summary>
        public int manaCost;

        /// <summary>冷却（毫秒）。</summary>
        public int delayMs;

        /// <summary>最小伤害。</summary>
        public int dmgMin;

        /// <summary>最大伤害。</summary>
        public int dmgMax;

        /// <summary>伤害类型。</summary>
        public DamageType dmgType;

        /// <summary>目标类型（决定施放时怎么用目标格）。</summary>
        public SkillTarget target;
    }

    /// <summary>
    /// 单个角色的存档数据（**多角色**：`Game.Setting` 里键 = `char/{name}`）。
    /// 字段即存档格式；`version` 不匹配时由 Save 模块决定迁移或拒绝（并打日志）。
    /// </summary>
    [Serializable]
    public class CharacterSave
    {
        /// <summary>存档格式版本（写 `GameConst.SaveVersion`）。</summary>
        public int version;

        /// <summary>角色名（**唯一键**；空名不合法）。</summary>
        public string name;

        /// <summary>职业。</summary>
        public PlayerClass cls;

        /// <summary>等级。</summary>
        public int level = 1;

        /// <summary>当前经验。</summary>
        public long exp;

        /// <summary>力量。</summary>
        public int str;

        /// <summary>敏捷。</summary>
        public int dex;

        /// <summary>体力。</summary>
        public int vit;

        /// <summary>精力。</summary>
        public int eng;

        /// <summary>当前生命（恢复用）。</summary>
        public int life;

        /// <summary>当前法力。</summary>
        public int mana;

        /// <summary>当前耐力。</summary>
        public int stamina;

        /// <summary>未分配属性点。</summary>
        public int statPoints;

        /// <summary>未分配技能点。</summary>
        public int skillPoints;

        /// <summary>金币。</summary>
        public int gold;

        /// <summary>所在区域（见 <see cref="AreaId"/>）。</summary>
        public int areaId;

        /// <summary>所在格 X。</summary>
        public int gridX;

        /// <summary>所在格 Y。</summary>
        public int gridY;

        /// <summary>本局地图 seed（**必须存**：读档后同 seed 生成同一张图）。</summary>
        public int mapSeed;

        /// <summary>已学技能 id（与 <see cref="skillLevels"/> 一一对应）。</summary>
        public List<int> skillIds = new List<int>();

        /// <summary>已学技能等级（与 <see cref="skillIds"/> 一一对应）。</summary>
        public List<int> skillLevels = new List<int>();

        /// <summary>左右键绑定的技能 id（`[-1] = 普通攻击`）。下标 0 = 左键，1 = 右键。</summary>
        public List<int> buttonSkills = new List<int> { -1, -1 };

        /// <summary>背包格（长度 = `GameConst.InventoryCellCount`）。</summary>
        public List<InventorySlot> inventory = new List<InventorySlot>();

        /// <summary>已装备物品。</summary>
        public List<ItemStack> equip = new List<ItemStack>();

        /// <summary>腰带（长度 = `GameConst.BeltSlots`，空位为 null）。</summary>
        public List<ItemStack> belt = new List<ItemStack>();

        /// <summary>任务状态。</summary>
        public List<QuestStateDto> quests = new List<QuestStateDto>();

        /// <summary>存档时间（`DateTime.UtcNow.Ticks`，0 = 未知）。</summary>
        public long savedAtTicks;

        /// <summary>累计游戏时长（秒）。</summary>
        public float playedSeconds;
    }

    /// <summary>
    /// 玩家属性快照（HUD 与人物属性面板共用）。
    /// **契约 §3.5**：`Events.HudDirty` 的参数类型。
    /// </summary>
    [Serializable]
    public class PlayerStatsDto
    {
        /// <summary>角色名。</summary>
        public string name;

        /// <summary>职业。</summary>
        public PlayerClass cls;

        /// <summary>等级。</summary>
        public int level;

        /// <summary>当前经验。</summary>
        public long exp;

        /// <summary>升到下一级所需经验（已满级时 = 0）。</summary>
        public long expNext;

        /// <summary>力量。</summary>
        public int str;

        /// <summary>敏捷。</summary>
        public int dex;

        /// <summary>体力。</summary>
        public int vit;

        /// <summary>精力。</summary>
        public int eng;

        /// <summary>当前生命。</summary>
        public int life;

        /// <summary>生命上限。</summary>
        public int maxLife;

        /// <summary>当前法力。</summary>
        public int mana;

        /// <summary>法力上限。</summary>
        public int maxMana;

        /// <summary>当前耐力。</summary>
        public int stamina;

        /// <summary>耐力上限。</summary>
        public int maxStamina;

        /// <summary>防御。</summary>
        public int defense;

        /// <summary>命中（AR）。</summary>
        public int attackRating;

        /// <summary>格挡率（%）。</summary>
        public int blockChance;

        /// <summary>火焰抗性（%）。</summary>
        public int fireResist;

        /// <summary>冰冷抗性（%）。</summary>
        public int coldResist;

        /// <summary>闪电抗性（%）。</summary>
        public int lightResist;

        /// <summary>毒素抗性（%）。</summary>
        public int poisonResist;

        /// <summary>未分配属性点。</summary>
        public int statPoints;

        /// <summary>未分配技能点。</summary>
        public int skillPoints;

        /// <summary>金币。</summary>
        public int gold;

        /// <summary>当前右键技能 id（-1 = 普通攻击）。</summary>
        public int selectedSkillId = -1;

        /// <summary>腰带各格物品数量（长度 = `GameConst.BeltSlots`）。</summary>
        public List<int> beltCounts = new List<int>();
    }

    /// <summary>NPC 对话内容。**契约 §3.5**：`Events.DialogOpen` 的参数（面板用 `OnOpen(param)` 取）。</summary>
    [Serializable]
    public class NpcDialogArgs
    {
        /// <summary>NPC id。</summary>
        public int npcId;

        /// <summary>NPC 名。</summary>
        public string npcName;

        /// <summary>对话正文（**随任务阶段变化**）。</summary>
        public string text;

        /// <summary>可选项文本（下标即 `Events.DialogOptionChosen` 的参数；空 = 只能关闭）。</summary>
        public List<string> options = new List<string>();

        /// <summary>是否有商店入口。</summary>
        public bool hasShop;

        /// <summary>本篇对话是否可接任务。</summary>
        public bool canAcceptQuest;

        /// <summary>本篇对话是否可交任务。</summary>
        public bool canTurnInQuest;

        /// <summary>可接/可交的任务 id。</summary>
        public int questId;
    }

    /// <summary>商店里的一条商品。</summary>
    [Serializable]
    public class ShopEntry
    {
        /// <summary>本店内的商品下标（买卖请求用它定位）。</summary>
        public int index;

        /// <summary>物品的 `item_c` 主键。</summary>
        public int itemId;

        /// <summary>显示名。</summary>
        public string name;

        /// <summary>品质。</summary>
        public ItemQuality quality;

        /// <summary>售价（买入价）。</summary>
        public int price;

        /// <summary>剩余数量（-1 = 无限）。</summary>
        public int count = -1;

        /// <summary>玩家是否买得起。</summary>
        public bool affordable = true;
    }

    /// <summary>商店快照。**契约 §3.5**：`Events.ShopOpen` / `ShopChanged` 的参数。</summary>
    [Serializable]
    public class ShopOpenArgs
    {
        /// <summary>NPC id。</summary>
        public int npcId;

        /// <summary>商店名 / NPC 名。</summary>
        public string npcName;

        /// <summary>是否提供修理。</summary>
        public bool canRepair;

        /// <summary>玩家当前金币。</summary>
        public int playerGold;

        /// <summary>可购买清单。</summary>
        public List<ShopEntry> stock = new List<ShopEntry>();

        /// <summary>玩家可出售的背包物品（锚点格索引 + 物品）。</summary>
        public List<InventorySlot> playerItems = new List<InventorySlot>();

        /// <summary>修理全部的费用（canRepair 时有效）。</summary>
        public int repairAllCost;
    }

    /// <summary>一次买卖/修理请求的参数（UI → Npc 模块）。</summary>
    [Serializable]
    public class ShopTradeArgs
    {
        /// <summary>NPC id。</summary>
        public int npcId;

        /// <summary>商品/物品下标（`ShopEntry.index` 或背包锚点格索引；修理全部时 &lt; 0）。</summary>
        public int index;

        /// <summary>数量（≥1）。</summary>
        public int count = 1;
    }

    /// <summary>分配属性点请求的参数（UI → Player 模块）。</summary>
    [Serializable]
    public class StatAllocArgs
    {
        /// <summary>哪一维。</summary>
        public StatKind kind;

        /// <summary>增量（可负，用于「点多了撤回」）。</summary>
        public int delta;
    }

    /// <summary>
    /// 背包/装备/腰带/金币的完整快照。**契约 §3.5**：`Events.InventoryChanged` / `EquipChanged` 的参数。
    /// 面板收到后在 <c>OnOpen/事件回调</c> 里整体重绘（UI 不持有 <c>IItemModule</c>）。
    /// </summary>
    [Serializable]
    public class InventoryChangedArgs
    {
        /// <summary>当前金币。</summary>
        public int gold;

        /// <summary>背包格（长度 = `GameConst.InventoryCellCount`）。</summary>
        public List<InventorySlot> inventory = new List<InventorySlot>();

        /// <summary>已装备物品。</summary>
        public List<ItemStack> equip = new List<ItemStack>();

        /// <summary>腰带（长度 = `GameConst.BeltSlots`，空位为 null）。</summary>
        public List<ItemStack> belt = new List<ItemStack>();
    }

    /// <summary>技能树快照（UI → 技能面板）。</summary>
    [Serializable]
    public class SkillTreeArgs
    {
        /// <summary>职业。</summary>
        public PlayerClass cls;

        /// <summary>未分配技能点。</summary>
        public int skillPoints;

        /// <summary>本职业全部技能定义。</summary>
        public List<SkillDef> skills = new List<SkillDef>();

        /// <summary>与 <see cref="skills"/> 对齐的已学等级（0 = 未学）。</summary>
        public List<int> learnedLevels = new List<int>();

        /// <summary>与 <see cref="skills"/> 对齐的「当前是否可学」。</summary>
        public List<bool> learnable = new List<bool>();
    }

    /// <summary>
    /// 小地图快照。**契约**：`Events.MapGenerated` 的参数。
    /// `tiles` 用行优先一维数组（`index = y * width + x`），取值见本类的 `Tile*` 常量。
    /// 已探索区域由面板**自己**按玩家走过的格累积（`Events.PlayerGridChanged`）。
    /// </summary>
    [Serializable]
    public class MinimapArgs
    {
        /// <summary>不可见/图外。</summary>
        public const byte TileVoid = 0;

        /// <summary>可走地面。</summary>
        public const byte TileWalkable = 1;

        /// <summary>障碍（岩石/树/墙）。</summary>
        public const byte TileBlocking = 2;

        /// <summary>出入口 / 洞穴入口。</summary>
        public const byte TileExit = 3;

        /// <summary>建筑/NPC 等可交互点。</summary>
        public const byte TileInteractable = 4;

        /// <summary>区域（见 <see cref="AreaId"/>）。</summary>
        public int areaId;

        /// <summary>宽（格）。</summary>
        public int width;

        /// <summary>高（格）。</summary>
        public int height;

        /// <summary>地图 seed（便于复现现场）。</summary>
        public int seed;

        /// <summary>地形码（行优先，长度 = width*height）。</summary>
        public List<byte> tiles = new List<byte>();

        /// <summary>玩家所在格 X。</summary>
        public int playerX;

        /// <summary>玩家所在格 Y。</summary>
        public int playerY;

        /// <summary>标记点 X（出口 / 洞穴入口 / NPC）。</summary>
        public List<int> markerX = new List<int>();

        /// <summary>标记点 Y（与 <see cref="markerX"/> 对齐）。</summary>
        public List<int> markerY = new List<int>();

        /// <summary>标记点类型（与 <see cref="markerX"/> 对齐；取 `MinimapArgs.Tile*` 常量）。</summary>
        public List<byte> markerKind = new List<byte>();

        /// <summary>取某格的地形码（越界返回 <see cref="TileVoid"/>）。</summary>
        public byte TileAt(int x, int y)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return TileVoid;
            var i = y * width + x;
            return i < tiles.Count ? tiles[i] : TileVoid;
        }
    }

    /// <summary>鼠标悬停目标快照（`Events.HoverTargetChanged` 的参数）。</summary>
    [Serializable]
    public class HoverTarget
    {
        /// <summary>是否悬停在可交互对象上。</summary>
        public bool hasTarget;

        /// <summary>应该显示的光标形态。</summary>
        public CursorKind cursor = CursorKind.Default;

        /// <summary>目标实体/物品 id（无目标时 -1）。</summary>
        public int id = -1;

        /// <summary>目标名（用于 tooltip / 地面物品名；无目标时空串）。</summary>
        public string name;

        /// <summary>目标格 X。</summary>
        public int gridX;

        /// <summary>目标格 Y。</summary>
        public int gridY;
    }

    /// <summary>音量参数（`Events.VolumeChanged` 的参数）。</summary>
    [Serializable]
    public class AudioVolumeArgs
    {
        /// <summary>BGM 音量 0~1。</summary>
        public float bgm = 0.7f;

        /// <summary>音效音量 0~1。</summary>
        public float sfx = 0.8f;

        /// <summary>BGM 是否静音。</summary>
        public bool bgmMute;

        /// <summary>音效是否静音。</summary>
        public bool sfxMute;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// 模块门面接口（命名空间 = Diablo2.Module）
//
// 实现规则（`tools/ai-skill/conventions.md`）：
//   · 门面 = `I{名}` 接口 + `{名}Module` 实现，**实现 internal**；
//   · `Module/A` 不许 `using Module/B` 的**具体类型** —— 依赖别的模块一律经
//     `AppContext` 注入的**接口**（本文件）或 `Core/Events.cs` 的事件；
//   · 一个 `.cs` 一个类。
// ═════════════════════════════════════════════════════════════════════════════
namespace Diablo2.Module
{
    /// <summary>
    /// 格子地图门面（`Module/Map/MapModule.cs`）。
    /// 三处区域：罗格营地（固定）/ 血腥荒野（随机）/ 邪恶洞穴（随机）。
    /// </summary>
    public interface IMapModule
    {
        /// <summary>当前地图宽（格）。未生成时 = 0。</summary>
        int Width { get; }

        /// <summary>当前地图高（格）。未生成时 = 0。</summary>
        int Height { get; }

        /// <summary>当前区域。</summary>
        AreaId Area { get; }

        /// <summary>当前地图 seed（日志与存档要打它）。</summary>
        int Seed { get; }

        /// <summary>障碍格总数（自证/日志用）。</summary>
        int BlockedCount { get; }

        /// <summary>可走格总数（自证/日志用）。</summary>
        int WalkableCount { get; }

        /// <summary>地图是否已生成。</summary>
        bool IsGenerated { get; }

        /// <summary>出生点（玩家进图位置）。未生成时为 (0,0)。</summary>
        Vector2Int SpawnPoint { get; }

        /// <summary>出入口格（`TileKind.Exit`），城镇的出城口 / 野外的洞穴入口。</summary>
        IReadOnlyList<Vector2Int> Exits { get; }

        /// <summary>洞穴入口格（仅血腥荒野有效；其它区域为 null）。</summary>
        Vector2Int? CaveEntrance { get; }

        /// <summary>城镇 NPC 站位（下标 = `Def.NpcId` 的整数值；非城镇区域为空列表）。</summary>
        IReadOnlyList<Vector2Int> NpcPoints { get; }

        /// <summary>怪物刷新点（洞穴生成时产出；野外为空列表）。</summary>
        IReadOnlyList<Vector2Int> MonsterSpawns { get; }

        /// <summary>是否在图内（不判可走）。</summary>
        bool InBounds(Vector2Int g);

        /// <summary>该格是否可走（图外 = false；**唯一判定走 `Def.TileKindInfo.IsWalkable`**）。</summary>
        bool Walkable(Vector2Int g);

        /// <summary>该格地形（图外返回 <see cref="TileKind.Void"/>）。</summary>
        TileKind TileAt(Vector2Int g);

        /// <summary>
        /// 生成地图。**同 seed ⇒ 同地图**（可复现）。
        /// 生成后必须做连通性自检（出生点可达 Exit / 洞穴入口 / 所有房间），
        /// 不可达则重生成（最多 `GameConst.MapGenMaxRetry` 次）并打日志。
        /// </summary>
        void Generate(AreaId area, int seed);

        /// <summary>清空地图（退出 Stage 时调用）。</summary>
        void Clear();

        /// <summary>
        /// 求路径（逐格，含起终点）。**不可达返回 null**（已打日志）。
        /// 内部复用 `CloverEngine.AStar.Find`，不许另写一份寻路。
        /// </summary>
        List<Vector2Int> FindPath(Vector2Int from, Vector2Int to);

        /// <summary>在世界坐标里取一个随机可走格（掉落/怪物出生用）。无可用格时返回出生点并打日志。</summary>
        Vector2Int RandomWalkableTile(Rng rng);

        /// <summary>把地图渲染出来（一次性铺三层，不要每帧重建）。</summary>
        void ShowArea(AreaId area);

        /// <summary>把格子数据打成快照交给小地图（`Events.MapGenerated` 的参数）。</summary>
        MinimapArgs BuildMinimap();
    }

    /// <summary>
    /// 主角门面（`Module/Player/PlayerModule.cs`）。
    /// 位置由**本模块**本地解算（原版点击移动语义），**不许被任何外部权威位置逐帧覆盖**。
    /// </summary>
    public interface IPlayerModule
    {
        /// <summary>职业。</summary>
        PlayerClass Class { get; }

        /// <summary>角色名。</summary>
        string Name { get; }

        /// <summary>等级。</summary>
        int Level { get; }

        /// <summary>力量（含装备加成）。</summary>
        int Str { get; }

        /// <summary>敏捷（含装备加成）。</summary>
        int Dex { get; }

        /// <summary>体力（含装备加成）。</summary>
        int Vit { get; }

        /// <summary>精力（含装备加成）。</summary>
        int Eng { get; }

        /// <summary>当前生命。</summary>
        int Life { get; }

        /// <summary>生命上限。</summary>
        int MaxLife { get; }

        /// <summary>当前法力。</summary>
        int Mana { get; }

        /// <summary>法力上限。</summary>
        int MaxMana { get; }

        /// <summary>当前耐力。</summary>
        int Stamina { get; }

        /// <summary>耐力上限。</summary>
        int MaxStamina { get; }

        /// <summary>当前经验。</summary>
        long Exp { get; }

        /// <summary>升到下一级所需经验（已满级时 = 0）。</summary>
        long ExpNext { get; }

        /// <summary>未分配属性点。</summary>
        int StatPoints { get; }

        /// <summary>未分配技能点。</summary>
        int SkillPoints { get; }

        /// <summary>金币。</summary>
        int Gold { get; }

        /// <summary>当前格。</summary>
        Vector2Int Grid { get; }

        /// <summary>当前世界坐标（渲染插值后的实际位置，精灵视图跟它）。</summary>
        Vector3 World { get; }

        /// <summary>当前朝向。</summary>
        Dir8 Dir { get; }

        /// <summary>是否正在沿路径移动。</summary>
        bool IsMoving { get; }

        /// <summary>
        /// 是否处于**跑**状态（false = 走）。
        /// <para>★ **片 2b 新增（主 agent 授权的契约扩展）**：原版玩家有**走/跑两套移动动画**
        /// （`.cof` 的 `WL` / `RN`，见 `Engine/IO/D2Formats/AnimData.cs` 的动作代号），
        /// 表现层要按这个状态选 `ViewAnim.Run` / `ViewAnim.Walk`（`Module/View/ViewModule.cs`），
        /// 否则只能一直播走路动画 ⇒ "飘着走"。</para>
        /// <para>实现方（`Module/Player/PlayerModule.cs`）用已有的 `_running` 字段（原版 R 键切换）。</para>
        /// </summary>
        bool IsRunning { get; }

        /// <summary>是否已死亡。</summary>
        bool IsDead { get; }

        /// <summary>防御（含装备）。</summary>
        int Defense { get; }

        /// <summary>命中（AR，含装备）。</summary>
        int AttackRating { get; }

        /// <summary>某系抗性（%，可为负，上限 75）。</summary>
        int GetResist(DamageType type);

        /// <summary>属性快照（HUD / 人物属性面板用；**契约 §3.5 的 `HudDirty` 参数**）。</summary>
        PlayerStatsDto Snapshot();

        /// <summary>建新角色（创角时由 Flow 调用；初始化四维/生命/法力/等级 1）。</summary>
        void CreateNew(PlayerClass cls, string name);

        /// <summary>按存档恢复（读档时由 Flow 调用）。</summary>
        void LoadFrom(CharacterSave save);

        /// <summary>把当前状态写回存档对象（存档时由 Save 模块调用）。</summary>
        void WriteTo(CharacterSave save);

        /// <summary>复位（回主菜单时调用，清空角色）。</summary>
        void Reset();

        /// <summary>移动到目标格（内部 A* 寻路；不可达时打日志并停在原地）。</summary>
        void MoveTo(Vector2Int target);

        /// <summary>停止移动（清空路径）。</summary>
        void Stop();

        /// <summary>直接落到某格（传送/进图/读档用，不做寻路）。</summary>
        void TeleportTo(Vector2Int grid);

        /// <summary>每帧推进（由 AppContext 转发）。</summary>
        void Tick(float dt);

        /// <summary>
        /// 受到伤害（走抗性/防御结算）。返回**是否因此死亡**。
        /// 怪物攻击玩家的结算入口（由 `ICombatModule` 调用）。
        /// </summary>
        bool ApplyDamage(int amount, DamageType type);

        /// <summary>回复生命（药水/技能）。</summary>
        void Heal(int amount);

        /// <summary>回复法力。</summary>
        void RestoreMana(int amount);

        /// <summary>回复耐力。</summary>
        void RestoreStamina(int amount);

        /// <summary>增加经验（到达阈值自动升级）。</summary>
        void AddExp(int amount);

        /// <summary>增减金币（负数扣除；余额不足返回 false 并不改值）。</summary>
        bool AddGold(int amount);

        /// <summary>增减技能点。</summary>
        void AddSkillPoint(int delta);

        /// <summary>分配属性点（校验点数与合法性；非法则打日志并返回 false）。</summary>
        bool AllocateStat(StatKind kind, int delta);

        /// <summary>复活（死亡流程用；回城并恢复一部分生命）。</summary>
        void Revive();

        /// <summary>死亡处理（清零生命、发事件、记日志）。一般由 <see cref="ApplyDamage"/> 内部调用。</summary>
        void Kill();
    }

    /// <summary>
    /// 怪物门面（`Module/Monster/MonsterModule.cs`）：生成 / AI / 精英词缀 / 受击。
    /// </summary>
    public interface IMonsterModule
    {
        /// <summary>全场存活怪物数。</summary>
        int AliveCount { get; }

        /// <summary>全部怪物（含尸体，`alive=false` 仍保留直到清理）。</summary>
        IReadOnlyList<MonsterState> All { get; }

        /// <summary>某区域内的存活怪物数（**任务判定用它**：`CountInArea(AreaId.DenOfEvil) == 0` ⇒ 可交付）。</summary>
        int CountInArea(AreaId area);

        /// <summary>按 id 取怪物状态；不存在返回 null 并限频告警。</summary>
        MonsterState Get(int monsterId);

        /// <summary>该怪物是否存活。</summary>
        bool IsAlive(int monsterId);

        /// <summary>按区域刷怪（读 `level_c` 的密度与 `monster_c` 属性；用 `IMapModule.MonsterSpawns` 定位）。</summary>
        void SpawnArea(AreaId area);

        /// <summary>清掉全部怪物（退出 Stage 时调用）。</summary>
        void DespawnAll();

        /// <summary>移除一具尸体（尸体保留时间到期 / 视图回收）。</summary>
        void RemoveCorpse(int monsterId);

        /// <summary>每帧推进 AI（由 AppContext 转发）。</summary>
        void Tick(float dt);

        /// <summary>扣血（含抗性结算；死亡会发 `Events.MonsterKilled` 并留尸体）。</summary>
        void ApplyDamage(int monsterId, int amount, DamageType type);

        /// <summary>被玩家攻击（切换到仇恨状态）。</summary>
        void NotifyAttacked(int monsterId);

        /// <summary>UI 悬停到某怪物（不做 AI 决策，仅更新「当前悬停」）。</summary>
        void SetHovered(int monsterId);

        /// <summary>使用尸体（萨满复活 / 死灵技能）。成功返回 true。</summary>
        bool ConsumeCorpse(int monsterId);
    }

    /// <summary>
    /// 战斗结算门面（`Module/Combat/CombatModule.cs`）：命中判定 / 伤害公式 / 抗性 / 击杀 / 复活。
    /// 怪物 AI 与玩家/怪物模块之间**只经本门面**结算伤害，禁止各自算一遍。
    /// </summary>
    public interface ICombatModule
    {
        /// <summary>当前攻击目标 id（-1 = 无）。</summary>
        int CurrentTargetId { get; }

        /// <summary>距离下次可攻击的剩余秒数（HUD 可显示）。</summary>
        float AttackCooldownRemain { get; }

        /// <summary>请求攻击指定怪物（左键点怪 / 自动攻击）。</summary>
        void RequestAttack(int monsterId);

        /// <summary>请求怪物攻击玩家（怪物 AI 判定「该出手了」时调用，由本模块做命中与伤害结算）。</summary>
        void RequestMonsterAttack(int monsterId);

        /// <summary>设置当前目标（不改移动意图）。</summary>
        void SetTarget(int monsterId);

        /// <summary>清除当前目标。</summary>
        void ClearTarget();

        /// <summary>玩家死亡后的复活流程（回城、恢复生命、发事件）。</summary>
        void RevivePlayer();

        /// <summary>每帧推进（攻击节奏/持续伤害）。</summary>
        void Tick(float dt);

        /// <summary>复位（回主菜单时调用）。</summary>
        void Reset();
    }

    /// <summary>
    /// 技能门面（`Module/Skill/SkillModule.cs`）：技能树 / 学习 / 左右键绑订 / 施放 / 冷却 / 投射物。
    /// </summary>
    public interface ISkillModule
    {
        /// <summary>当前职业。</summary>
        PlayerClass Class { get; }

        /// <summary>当前选中的右键技能 id（-1 = 普通攻击）。</summary>
        int SelectedSkillId { get; }

        /// <summary>本职业全部技能定义（技能面板用）。</summary>
        IReadOnlyList<SkillDef> Available { get; }

        /// <summary>已学等级（0 = 未学）。</summary>
        int GetLevel(int skillId);

        /// <summary>是否满足学习条件（等级/前置/技能点）。</summary>
        bool CanLearn(int skillId);

        /// <summary>
        /// 学习一级（不满足条件返回 false 并打日志）。
        /// UI 入口：`Events.SkillLearnRequest`（int skillId）—— 本模块**不订阅任何事件**，
        /// 统一由 `App/AppEventRouting.cs` 转发（**agent-12 接线**）。
        /// </summary>
        bool Learn(int skillId);

        /// <summary>选择右键技能（-1 = 普通攻击）。</summary>
        void SelectSkill(int skillId);

        /// <summary>把技能绑到某个键（0 = 左键，1 = 右键）。</summary>
        void AssignToButton(int button, int skillId);

        /// <summary>取某个键绑定的技能 id（-1 = 普通攻击）。</summary>
        int GetButtonSkill(int button);

        /// <summary>施放（校验法力/冷却/目标；成功则扣蓝并触发效果）。</summary>
        bool TryCast(int skillId, Vector2Int targetGrid);

        /// <summary>剩余冷却秒数（0 = 可用）。</summary>
        float GetCooldownRemain(int skillId);

        /// <summary>技能树快照（技能面板用）。</summary>
        SkillTreeArgs BuildTree();

        /// <summary>每帧推进（冷却/投射物飞行）。</summary>
        void Tick(float dt);

        /// <summary>切换职业（创角/读档时调用，重建技能树状态）。</summary>
        void ResetForClass(PlayerClass cls, CharacterSave save);
    }

    /// <summary>
    /// 物品门面（`Module/Item/ItemModule.cs`）：掉落 / 拾取 / 背包 / 装备 / 腰带 / 金币 / 词缀生成。
    /// 物品尺寸来自配表 `item_c.grid_w/grid_h`（不硬编码）。
    /// </summary>
    public interface IItemModule
    {
        /// <summary>当前金币。</summary>
        int Gold { get; }

        /// <summary>背包格快照（长度 = `GameConst.InventoryCellCount`）。</summary>
        IReadOnlyList<InventorySlot> Inventory { get; }

        /// <summary>已装备物品。</summary>
        IReadOnlyList<ItemStack> Equipment { get; }

        /// <summary>腰带（长度 = `GameConst.BeltSlots`，空位为 null）。</summary>
        IReadOnlyList<ItemStack> Belt { get; }

        /// <summary>地面物品（锚点格索引 → 物品）。视图用它建地面图标。</summary>
        IReadOnlyList<KeyValuePair<int, ItemStack>> GroundItems { get; }

        /// <summary>背包是否已满（放不下任何 1×1 物品）。</summary>
        bool IsFull { get; }

        /// <summary>背包/装备/腰带/金币快照（**契约 §3.5 的 `InventoryChanged` 参数**）。</summary>
        InventoryChangedArgs Snapshot();

        /// <summary>按等级随机生成一件物品（走配表 TC + 品质判定 + 词缀）。</summary>
        ItemStack CreateRandom(int level, Rng rng);

        /// <summary>按掉落表生成一批掉落（怪物死亡时由 Combat 调用）。</summary>
        void DropLoot(int treasureClassId, int monsterLevel, Vector2Int grid, Rng rng);

        /// <summary>把一件物品直接放到地面。</summary>
        void DropToGround(ItemStack item, Vector2Int grid);

        /// <summary>拾取（距离校验；背包满返回 false 且**物品留在原地**并打日志）。</summary>
        bool Pickup(int groundItemId);

        /// <summary>拾取离某格最近的物品（-1 表示不限 id）。</summary>
        bool PickupNearest(Vector2Int grid, float maxRange);

        /// <summary>放入背包（自动找位；放不下返回 false）。</summary>
        bool AddToInventory(ItemStack item);

        /// <summary>从背包移除某锚点格上的物品。</summary>
        bool RemoveFromInventory(int anchorIndex);

        /// <summary>装备背包里某锚点格的物品（换下的回背包；失败返回 false）。</summary>
        bool EquipFromInventory(int anchorIndex);

        /// <summary>
        /// 卸下某槽位（戒指/武器组按 <paramref name="slotIndex"/> 区分同槽多件）。
        /// UI 入口：`Events.UnequipRequest`（参数 = `((int)slot &amp; 0xFF) | (slotIndex &lt;&lt; 8)`）
        /// —— 由 `App/AppEventRouting.cs` 转发到本方法（**agent-12 接线**；UI 不许 `using Diablo2.Module`）。
        /// </summary>
        bool Unequip(ItemSlot slot, int slotIndex);

        /// <summary>使用某锚点格的物品（药水/卷轴）。</summary>
        bool UseItem(int anchorIndex);

        /// <summary>腰带格喝药（0..3）。</summary>
        bool UseBeltSlot(int index);

        /// <summary>把物品放进腰带（指定格或自动找位；index &lt; 0 = 自动）。</summary>
        bool AddToBelt(ItemStack item, int index);

        /// <summary>增减金币（余额不足返回 false）。</summary>
        bool AddGold(int amount);

        /// <summary>修理（锚点格索引；&lt; 0 = 全部）。返回修理费，-1 = 失败。</summary>
        int Repair(int anchorIndex);

        /// <summary>总修理费（商店面板显示用）。</summary>
        int GetRepairAllCost();

        /// <summary>按存档恢复背包/装备/腰带/金币。</summary>
        void LoadFrom(CharacterSave save);

        /// <summary>写回存档对象。</summary>
        void WriteTo(CharacterSave save);

        /// <summary>每帧推进（尸体/地面物品超时回收）。</summary>
        void Tick(float dt);

        /// <summary>复位（回主菜单时调用）。</summary>
        void Reset();
    }

    /// <summary>
    /// 任务门面（`Module/Quest/QuestModule.cs`）：邪恶洞穴任务链状态机。
    /// 「清光洞内全部怪物才可交付」是**硬条件**。
    /// </summary>
    public interface IQuestModule
    {
        /// <summary>邪恶洞穴任务状态。</summary>
        QuestState DenOfEvil { get; }

        /// <summary>洞内剩余怪物数（= `IMonsterModule.CountInArea(AreaId.DenOfEvil)`）。</summary>
        int DenRemaining { get; }

        /// <summary>是否满足交付条件（进行中 且 剩余 = 0）。</summary>
        bool CanTurnInDen { get; }

        /// <summary>全部任务状态（任务日志面板用）。</summary>
        IReadOnlyList<QuestStateDto> Quests { get; }

        /// <summary>取某个任务状态。</summary>
        QuestStateDto Get(int questId);

        /// <summary>接取邪恶洞穴任务（重复接取打日志并忽略）。</summary>
        void AcceptDen();

        /// <summary>交付邪恶洞穴任务（不满足条件则发 `Events.QuestTurnInDenied` 并打日志）。</summary>
        void TurnInDen();

        /// <summary>怪物被击杀时调用（重新统计洞内剩余并刷新状态）。</summary>
        void NotifyMonsterKilled(int monsterId);

        /// <summary>按存档恢复任务状态。</summary>
        void LoadFrom(CharacterSave save);

        /// <summary>写回存档对象。</summary>
        void WriteTo(CharacterSave save);

        /// <summary>复位（回主菜单时调用）。</summary>
        void Reset();
    }

    /// <summary>
    /// NPC 门面（`Module/Npc/NpcModule.cs`）：5 个 NPC 的定义 / 对话文本（随任务阶段变化）/ 商店 / 修理。
    /// </summary>
    public interface INpcModule
    {
        /// <summary>全部 NPC 定义（罗格营地 5 个）。</summary>
        IReadOnlyList<NpcDef> All { get; }

        /// <summary>按 id 取定义；不存在返回 null 并限频告警。</summary>
        NpcDef Get(int npcId);

        /// <summary>取离某格最近、且在 `GameConst.TalkRange` 内的 NPC；没有返回 null。</summary>
        NpcDef FindNearest(Vector2Int grid);

        /// <summary>交互（无商店时发 `Events.DialogOpen`；有商店时先对话）。成功返回 true。</summary>
        bool Interact(int npcId);

        /// <summary>取当前对话内容（**文本随任务阶段变化**）。参数用于面板 `OnOpen(param)`。</summary>
        NpcDialogArgs GetDialog(int npcId);

        /// <summary>推进对话（选项下标；0 = 关闭）。</summary>
        void ChooseOption(int npcId, int optionIndex);

        /// <summary>
        /// 取商店快照（`Events.ShopOpen` 的参数）。没有商店返回 null 并打日志。
        /// UI 入口：`Events.ShopOpenRequest`（int npcId）—— 由 `App/AppEventRouting.cs`
        /// 调本方法取快照后发 `Events.ShopOpen`（**agent-12 接线**）。
        /// </summary>
        ShopOpenArgs GetShop(int npcId);

        /// <summary>买入（金币结算 + 背包空间校验）。</summary>
        bool Buy(int npcId, int index, int count);

        /// <summary>卖出背包某锚点格的物品。</summary>
        bool Sell(int npcId, int anchorIndex);

        /// <summary>修理（锚点格索引 &lt; 0 = 全部）。</summary>
        int Repair(int npcId, int anchorIndex);

        /// <summary>按存档恢复（任务阶段影响对话）。</summary>
        void LoadFrom(CharacterSave save);

        /// <summary>每帧推进（NPC 待机动画由 View 负责；本方法供朝向/时序用）。</summary>
        void Tick(float dt);

        /// <summary>复位（回主菜单时调用）。</summary>
        void Reset();
    }

    /// <summary>
    /// 等距跟随相机（`Module/Camera/CameraRig.cs`）。
    /// 原版相机：**固定等距角度、不旋转、不缩放**，平滑跟随主角（只有局部滚动）。
    /// </summary>
    public interface ICameraRig
    {
        /// <summary>跟随目标（持续跟随该 Transform 的世界坐标）。</summary>
        void Follow(Transform target);

        /// <summary>取消跟随。</summary>
        void Unfollow();

        /// <summary>直接吸附到目标位置（进图/传送后不要平滑，避免看它「飞过去」）。</summary>
        void SnapToTarget();

        /// <summary>设置跟随目标格（不持有 Transform 时的便捷入口）。</summary>
        void SetTargetGrid(Vector2Int grid);

        /// <summary>镜头震动（命中/受击）。</summary>
        void Shake(float amplitude, float duration);

        /// <summary>每帧推进（由 AppContext 转发）。</summary>
        void Tick(float dt);

        /// <summary>复位（回主菜单时调用）。</summary>
        void Reset();
    }

    /// <summary>
    /// 精灵视图门面（`Module/View/ViewModule.cs`）：创建/更新实体视图、朝向动画、头顶血条、飘字。
    /// UI 飘字可直接用引擎 `Game.UI.FloatText`；本门面负责**世界空间**（头顶血条等）。
    /// </summary>
    public interface IViewModule
    {
        /// <summary>创建/更新玩家视图（切职业时重建）。</summary>
        void CreatePlayer(PlayerClass cls);

        /// <summary>创建怪物视图。</summary>
        void CreateMonster(MonsterState state);

        /// <summary>同步怪物视图（位置/朝向/血量/动画）。</summary>
        void UpdateMonster(MonsterState state);

        /// <summary>移除怪物视图。</summary>
        void RemoveMonster(int monsterId);

        /// <summary>清除全部怪物视图。</summary>
        void ClearMonsters();

        /// <summary>创建地面物品视图。</summary>
        void CreateGroundItem(int groundItemId, ItemStack item, Vector2Int grid);

        /// <summary>移除地面物品视图。</summary>
        void RemoveGroundItem(int groundItemId);

        /// <summary>播放受击表现（闪白 + Hit 动画）。</summary>
        void PlayHit(int entityId);

        /// <summary>播放死亡表现（Death 动画后隐藏，保留尸体）。</summary>
        void PlayDeath(int entityId);

        /// <summary>显示世界空间飘字（伤害数字等）。颜色用 ARGB 打包（避免 DTO 依赖 Unity 类型）。</summary>
        void ShowFloatingText(float worldX, float worldY, float worldZ, string text, uint argb);

        /// <summary>取某实体的视图 GameObject（拿不到返回 null 并限频告警）。</summary>
        GameObject GetView(int entityId);

        /// <summary>每帧推进（朝向/动画状态）。</summary>
        void Tick(float dt);

        /// <summary>清场（退出 Stage 时调用）。</summary>
        void Clear();
    }

    /// <summary>
    /// 音效门面（`Module/Audio/AudioModule.cs`）：音效触发点的统一出口。
    /// **音效键就是资源名**（见 `ResPaths.Sfx` / `ResPaths.Bgm`，路径由引擎约定）。
    /// </summary>
    public interface IAudioModule
    {
        /// <summary>当前 BGM 音量 0~1。</summary>
        float BgmVolume { get; }

        /// <summary>当前音效音量 0~1。</summary>
        float SfxVolume { get; }

        /// <summary>播放 2D 音效（UI / 拾取 / 升级）。</summary>
        void Sfx(string key);

        /// <summary>在指定世界坐标播放音效（挥砍 / 命中 / 怪物叫声）。</summary>
        void SfxAt(string key, float worldX, float worldY, float worldZ);

        /// <summary>切换 BGM（空串 = 停止）。</summary>
        void Bgm(string key);

        /// <summary>停止 BGM。</summary>
        void StopBgm();

        /// <summary>设置音量（0~1；会写入 `Game.Setting` 并在重进后保持）。</summary>
        void SetVolume(float bgm, float sfx);

        /// <summary>静音开关。</summary>
        void SetMute(bool bgmMute, bool sfxMute);

        /// <summary>取音量快照（设置面板用）。</summary>
        AudioVolumeArgs GetVolume();

        /// <summary>每帧推进（脚步节流等）。</summary>
        void Tick(float dt);

        /// <summary>复位（回主菜单时调用：停 BGM、清空节流状态）。</summary>
        void Reset();
    }

    /// <summary>
    /// 存档门面（`Module/Save/SaveModule.cs`）：多角色存档，**一角色一文件**（档在引擎 `FileSlotStore` 槽位里）。
    /// 档位：`&lt;SettingDir&gt;/saves/&lt;角色名&gt;.json`；`char/index`（`GameConst.SaveIndexKey`）只留**创建先后**（选角屏顺序）；
    /// `char/{角色名}`（`GameConst.SaveKeyPrefix`）只留旧档懒迁移。
    /// ⚠️ 槽位里存的仍是**字符串 JSON**（由 `SaveJson` 编解码）
    ///    —— **不要**往槽位里塞对象本身（反序列化 `object` 会丢类型，见上面契约注释）。
    /// </summary>
    public interface ISaveModule
    {
        /// <summary>最近一次失败原因（成功时为空串）。</summary>
        string LastError { get; }

        /// <summary>存档目录/键是否就绪。</summary>
        bool Ready { get; }

        /// <summary>把当前游戏状态存成角色存档（名字取 `IPlayerModule.Name`）。</summary>
        bool Save();

        /// <summary>存一份指定数据（创角时先把角色写进去）。</summary>
        bool Save(CharacterSave data);

        /// <summary>读某个角色的存档；不存在返回 null 并打日志。</summary>
        CharacterSave Load(string name);

        /// <summary>读存档（不存在的返回 false，不抛异常）。</summary>
        bool TryLoad(string name, out CharacterSave data);

        /// <summary>删除某个角色存档。</summary>
        bool Delete(string name);

        /// <summary>全部存档角色名（主菜单「继续」与选角屏用）。</summary>
        List<string> List();

        /// <summary>全部存档（带等级/职业，选角屏卡片用）。</summary>
        List<CharacterSave> ListAll();

        /// <summary>是否存在该角色的存档。</summary>
        bool Exists(string name);

        /// <summary>是否存在任意存档（主菜单「继续」按钮可用性）。</summary>
        bool HasAny { get; }

        /// <summary>把存档数据装配回各模块（Player / Skill / Item / Quest / Npc）。</summary>
        void ApplyToModules(CharacterSave data);
    }
}
