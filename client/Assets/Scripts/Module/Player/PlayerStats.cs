// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Player/PlayerStats.cs
// 主角**四维 + 派生属性**的唯一计算处（生命/法力/耐力/防御/命中 AR/格挡/四抗）。
//
// 三条硬规则
//   ① **数值全部来自配表**（`Table.Tables.Default.Class/…`），本文件不出现任何职业硬编码数字；
//   ② **公式必须能指出出处**（逐条写在 <see cref="Recompute"/> 的注释里）；
//   ③ 生命/法力/耐力公式必须与 `UI/CharCreatePanel.LifeOf/ManaOf/StaminaOf` **完全一致**
//      （验收表第 17 行：创角预览与进图后数字要对得上）。
//
// 装备加成从 `Events.EquipChanged`（载荷 `Def.InventoryChangedArgs`，含 `equip` 列表）聚合，
// 走事件而不直接引用 `Module/Item` ⇒ 不产生跨模块类型依赖（`tools/ai-skill/conventions.md`）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Player
{
    /// <summary>主角属性计算器（纯数据 + 纯公式，不碰 Unity 场景对象）。</summary>
    internal sealed class PlayerStats
    {
        /// <summary>抗性上限（原版普通难度上限 75%）。</summary>
        public const int MaxResist = 75;

        /// <summary>
        /// 格挡上限（原版 75%）。
        /// <para>出处：Arreat Summit「Basics: Character Information → Blocking」
        /// "The block value itself is a combination of a value inherent to that particular player class,
        /// and any other block bonuses from items. <b>This value is capped at 75%.</b>"</para>
        /// <para>**已登记的歧义（B3）**：上句"This value"指代不明 —— 可读作"钳 `Blocking`（职业项+装备项之和）"
        /// 或"钳**最终百分比**"。本工程数据域内 `Blocking ≤ class 30 + 盾 24 + 词缀 20 = 74 < 75`
        /// ⇒ **两种读法等价**，故本实现钳的是**最终百分比**（`Mathf.Clamp(chance, 0, MaxBlock)`），
        /// 位置不动。**若将来接入圣骑士 Holy Shield**（`原版资源/…/excel/skills.txt:119`，
        /// `aurastat1=toblock`、`Param5=10`/`Param6=40` ⇒ `Blocking` 可越 75）**必须回来重判**这条歧义。</para>
        /// </summary>
        public const int MaxBlock = 75;

        /// <summary>抗性下限（原版下限 -100%）。</summary>
        public const int MinResist = -100;

        /// <summary>生命/法力/耐力/防御的下限（PropertyType 之外的最小值，防负数）。</summary>
        private const int MinVital = 1;

        // ═════════════════════════════════════════════════════════════════════
        //
        //   旧 `Recompute` 把「每点体力/精力给多少」直接乘到**起始**的体力/精力上
        //   （`MaxLife = vit × life_per_vit`），于是 1 级角色拿到的是 `起点×成长系数`，
        //   而官方 1 级值 = **`hpadd` + 起始体力**（不是乘积）。
        //
        // 出处（两份互证，均可复算）：
        //   ① `原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/charstats.txt:2..6`
        //      （= `convert.py` 的默认输入；与 `原版资源/d2raw/data/global/excel/charstats.txt`
        //      · 第 7 列 `stamina` = 该职业**起始耐力**（84 / 74 / 79 / 89 / 92）
        //      · 第 8 列 `hpadd`   = 起始生命加成（**5 职业同为 30**）
        //      · 第 32 列 `BlockFactor` = 职业格挡系数（25 / 20 / 20 / 30 / 25）
        //        官方字段说明（D2R Data Guide, charstats 页原文）：
        //          hpadd = "Bonus starting Life value (This value gets added with the **vit** field
        //                   value to determine the overall starting amount of Life)"
        //          stamina = "Starting amount of Stamina"
        //   ② 官方职业页（Arreat Summit, `classic.battle.net/diablo2exp/classes/*.shtml`）
        //      Starting Attributes / Hit Points / Stamina / Mana：
        //        亚马逊 50 / 84 / 15（力 20 敏 25 体 20 精 15）
        //        女法师 40 / 74 / 35（体 10 精 35）
        //        野蛮人 55 / 92 / 10（体 25 精 10）
        //      ⇒ 生命 = hpadd(30) + 起始体力（20/10/25 ⇒ 50/40/55 ✓）；
        //         耐力 = `stamina` 列（84/74/92 ✓）；
        //         法力 = 起始精力（15/35/10 ✓，即"起始精力 × 1"）；
        //         且每级/每点成长与该页 "Each Character Level / Attribute Point Effect"
        //         完全一致（本工程的 `class_c` 成长列已按此导入 ✓）。
        //
        //    / `block_factor` **三列**（生成器 = `tools/table-convert/convert.py` 的 build_class()，
        //    映射 `hp_add ← charstats.hpadd`、`base_stamina ← charstats.stamina`、
        //    `block_factor ← charstats.BlockFactor`；值逐条由脚本从官方原始表抽取，无手抄）
        //    ⇒ 本文件**不再有**这些数字的常量，起始截距与职业格挡系数一律读 `Row`。
        //    收口判据：全仓 0 命中那两个旧常量名（防常量复活）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 生命上限（官方口径）：`hp_add + 起始体力 + life_per_vit × (体力 − 起始体力) + life_per_lvl × (等级−1)`。
        /// <para><paramref name="hpAdd"/> = `class_c.hp_add`（官方 `charstats.hpadd`，读表传入）。</para>
        /// <para>纯静态 ⇒ 创角预览（`UI/CharCreatePanel`）与运行时（本类）**同一式子**，
        /// 只是分层自检 ③ 禁止 UI 引用 Module ⇒ 那边用 `ClassEntry.hpAdd`（同样来自配表）走同一式子，
        /// 由 `playercheck` §1 断言两处相等。</para>
        /// </summary>
        public static int MaxLifeOf(int hpAdd, int startVit, float lifePerVit, float lifePerLvl, int vit, int level)
            => Mathf.Max(MinVital, Mathf.RoundToInt(
                (hpAdd + startVit) + (vit - startVit) * lifePerVit + (level - 1) * lifePerLvl));

        /// <summary>
        /// 法力上限（官方口径）：`起始精力 + mana_per_mag × (精力 − 起始精力) + mana_per_lvl × (等级−1)`
        /// —— 官方 1 级法力 = 起始精力（亚马逊 15 = 精 15、女法师 35 = 精 35、野蛮人 10 = 精 10）。
        /// </summary>
        public static int MaxManaOf(int startEng, float manaPerMag, float manaPerLvl, int eng, int level)
            => Mathf.Max(MinVital, Mathf.RoundToInt(
                startEng + (eng - startEng) * manaPerMag + (level - 1) * manaPerLvl));

        /// <summary>
        /// 耐力上限（官方口径）：`base_stamina + stam_per_vit × (体力 − 起始体力) + stam_per_lvl × (等级−1)`。
        /// <para><paramref name="baseStamina"/> = `class_c.base_stamina`（官方 `charstats.stamina`，读表传入）。</para>
        /// </summary>
        public static int MaxStaminaOf(int baseStamina, int startVit, float stamPerVit, float stamPerLvl,
            int vit, int level)
            => Mathf.Max(MinVital, Mathf.RoundToInt(
                baseStamina + (vit - startVit) * stamPerVit + (level - 1) * stamPerLvl));

        // ── 身份与四维基础（不含装备）────────────────────────────────────────
        /// <summary>职业。</summary>
        public PlayerClass Cls;

        /// <summary>等级（1 起）。</summary>
        public int Level = 1;

        /// <summary>力量（配表 `class_c.str` + 玩家分配）。</summary>
        public int BaseStr;

        /// <summary>敏捷。</summary>
        public int BaseDex;

        /// <summary>体力。</summary>
        public int BaseVit;

        /// <summary>精力。</summary>
        public int BaseEng;

        // ── 装备/词缀加成（`Events.EquipChanged` 聚合）───────────────────────
        /// <summary>装备力量加成（词缀 `str`）。</summary>
        public int BonusStr;

        /// <summary>装备敏捷加成（词缀 `dex`）。</summary>
        public int BonusDex;

        /// <summary>装备体力加成。</summary>
        public int BonusVit;

        /// <summary>装备精力加成（词缀 `enr`）。</summary>
        public int BonusEng;

        /// <summary>装备生命上限加成（词缀 `hp`）。</summary>
        public int BonusMaxLife;

        /// <summary>装备法力上限加成（词缀 `mana`）。</summary>
        public int BonusMaxMana;

        /// <summary>装备耐力上限加成（词缀 `stam`）。</summary>
        public int BonusMaxStamina;

        /// <summary>装备护甲值（各件 `(def_min+def_max)/2` 之和）。</summary>
        public int ArmorClass;

        /// <summary>装备固定防御加成（词缀 `ac`）。</summary>
        public int BonusDefenseFlat;

        /// <summary>防御百分比加成（词缀 `ac%`）。</summary>
        public int DefensePercent;

        /// <summary>装备命中加成（词缀 `att`）。</summary>
        public int BonusAttackRating;

        /// <summary>命中百分比加成（官方 `item_tohit_percent`；本项目词缀表暂无该码，留通道）。</summary>
        public int AttackRatingPercent;

        /// <summary>
        /// 装备提供的格挡值（官方 Blocking 的"装备项"）＝ **盾牌基材 `item_c.block`** ＋ 词缀 `block`
        /// （官方 `toblock`）之和。
        /// <para>u52block（R6）修的就是前一环：`class_c.block_factor`（职业项）早已读表，但盾牌**基材**
        /// 的 `block` 从未接入 ⇒ 装盾后格挡恒 0%。</para>
        /// </summary>
        public int BonusBlock;

        /// <summary>
        /// 是否装备了**盾 / 死灵头骨**（官方 `armor.txt` 的 `type` ∈ {`shie`,`ashd`,`head`}）。
        /// <para>原版闸门：**没有盾就没有格挡率** —— Arreat Summit「Blocking」原文
        /// "…you will see a percentage to block if you are carrying a <b>Shield</b> or Necromancer
        /// Shrunken Heads. <b>If you do not have a shield or Shrunken Heads you will not see this
        /// listed.</b>" ⇒ 徒手 / 只穿甲（无盾）恒 0%。</para>
        /// <para> 为什么闸门**不能**写成旧版的"装备格挡值 &lt;= 0"：`Buckler`（本项目 5 职业里
        /// 亚马逊/圣骑士/野蛮人的**起始盾**，`策划/数值文档/start_item_c.txt` 第 2/15/20 行）在官方
        /// 数据里 `block = 0`（`原版资源/d2lod1.10txt-1.10f/data/global/excel/Armor.txt:24` 第 11 列），
        /// 但它照样格挡 —— 官方公布的 Buckler 格挡 = 圣骑士 30% / 亚马逊·野蛮人 25% / 法师·死灵 20%
        /// （恰等于 `charstats.BlockFactor` + 0，逐职业对上）。用"装备格挡值"当闸门会把**起始盾**
        /// 判成 0%。</para>
        /// </summary>
        public bool HasShield;

        /// <summary>火焰抗性（词缀 `res-fire` / `res-all`）。</summary>
        public int BonusResFire;

        /// <summary>冰冷抗性。</summary>
        public int BonusResCold;

        /// <summary>闪电抗性。</summary>
        public int BonusResLight;

        /// <summary>毒素抗性。</summary>
        public int BonusResPoison;

        /// <summary>
        /// <para>
        /// 本项目 Act I 的 `affix_c` 里**没有** `res-magic` 词缀（低等级词缀集只有 火/冰/电/毒 与
        /// `res-all`）⇒ 该字段当前**无来源、恒为 0**。留着是为了：① `ResistOf(DamageType.Magic)`
        /// 有确定返回（不再走 `default` 的 Warn 分支）；② 将来配表补 `res-magic` 时只改这一处。
        /// </para>
        /// </summary>
        public int BonusResMagic;

        // ── 派生结果（<see cref="Recompute"/> 写）────────────────────────────
        /// <summary>生命上限。</summary>
        public int MaxLife;

        /// <summary>法力上限。</summary>
        public int MaxMana;

        /// <summary>耐力上限。</summary>
        public int MaxStamina;

        /// <summary>防御（被命中判定用）。</summary>
        public int Defense;

        /// <summary>命中（AR）。</summary>
        public int AttackRating;

        /// <summary>格挡率（%，上限 75）。</summary>
        public int BlockChance;

        /// <summary>当前职业的 `class_c` 行（null = 配表未加载/职业缺失，已打 Error）。</summary>
        public Table.BaseClassRow Row { get; private set; }

        /// <summary>职业命中修正（`class_c.to_hit_factor` = 官方 `charstats.ToHitFactor`）。</summary>
        private int _toHitFactor;

        /// <summary>「词缀 mod 未映射」只报一次（防刷屏；用标志位而非 Log.WarnOnce，见 PlayerLog 头注释）。</summary>
        private readonly HashSet<string> _unmappedLogged = new HashSet<string>();

        /// <summary>已按装备重算过一次（首帧/首次装备变化时打一条汇总日志）。</summary>
        private bool _equipSummaryLogged;

        /// <summary>四维合成值（含装备）。</summary>
        public int Str => BaseStr + BonusStr;

        /// <summary>敏捷（含装备）。</summary>
        public int Dex => BaseDex + BonusDex;

        /// <summary>体力（含装备）。</summary>
        public int Vit => BaseVit + BonusVit;

        /// <summary>精力（含装备）。</summary>
        public int Eng => BaseEng + BonusEng;

        /// <summary>
        /// 绑定职业：读 `class_c` 的命中修正列。
        /// 配表缺失时**不抛异常**，留下可定位的 Error 并返回 false（数值按 0 算，游戏仍可跑）。
        /// </summary>
        public bool Bind(PlayerClass cls)
        {
            Cls = cls;
            Row = Table.TableLoader.Class((int)cls);
            if (Row == null)
            {
                _toHitFactor = 0;
                PlayerLog.Error(
                    $"配表 class_c 取不到职业 {(int)cls}（{cls}）⇒ 四维/派生属性按 0 计算。" +
                    "检查 Bootstrap 里的 Table.TableLoader.LoadAll 是否成功（见 Table 的 Error 日志）");
                return false;
            }

            _toHitFactor = Row.ToHitFactor;
            return true;
        }

        /// <summary>
        /// <list type="bullet">
        /// <item>**起始值必须按官方起始口径取，不是"起始四维 × 成长系数"**：
        ///       生命 = `class_c.hp_add` + 起始体力 + `life_per_vit` × (体力 − 起始体力) + `life_per_lvl` × (等级−1)；
        ///       法力 = 起始精力 + `mana_per_mag` × (精力 − 起始精力) + `mana_per_lvl` × (等级−1)；
        ///       耐力 = `class_c.base_stamina` + `stam_per_vit` × (体力 − 起始体力) + `stam_per_lvl` × (等级−1)。
        ///       与 `UI/CharCreatePanel.LifeOf/ManaOf/StaminaOf` 同一式子（验收 #17，两处由 playercheck §1 把守）。</item>
        /// <item>防御 = 护甲值 + 敏捷/4，再乘 `item_armor%` 加成
        ///       —— 出处：`_assets_tmp/d2src/libd2/packages/game/src/combat.zig:43-50`（`GetDefense`，
        ///       官方 `Units.cpp:2304` 的移植）。</item>
        /// <item>命中 AR = (敏捷-7)×5 + 职业命中修正 + 装备 AR，再乘 `item_tohit%`
        ///       —— 出处：`libd2 …/combat.zig:52-66`（`GetAttackRate` 的 partial port，玩家 base=(dex-7)*5）；
        ///       职业命中修正 = `class_c.to_hit_factor`（官方 `charstats.ToHitFactor`）。</item>
        /// <item>格挡 = (敏捷-15) × (装备格挡 + `class_c.block_factor`) / (2×等级)，上限 75；
        ///       **无盾恒 0%**。
        ///       —— 出处：Arreat Summit「Blocking」（`classic.battle.net/diablo2exp/basics/characters.shtml`）：
        ///       `Total Blocking = (Blocking * (Dexterity - 15)) / (Character Level * 2)`，
        ///       `Blocking` = 职业固有值 + 所有装备上的格挡值，`capped at 75%`；
        ///       职业项 = `class_c.block_factor`（官方 `charstats.BlockFactor`，列 32；由打表产物给出）、
        ///       **装备项 = 盾牌基材 `item_c.block` + 词缀 `block`**。
        ///       详见 <see cref="ComputeBlockChance"/>。</item>
        /// <item>抗性：装备词缀求和，夹在 [-100, 75]（原版普通难度上限 75）。</item>
        /// </list>
        /// 取整一律 `Mathf.RoundToInt`（与创角预览同口径）。
        /// </summary>
        public void Recompute()
        {
            var row = Row;
            var dex = Dex;

            var lifePerVit = row != null ? row.LifePerVit : 0f;
            var lifePerLvl = row != null ? row.LifePerLvl : 0f;
            var manaPerMag = row != null ? row.ManaPerMag : 0f;
            var manaPerLvl = row != null ? row.ManaPerLvl : 0f;
            var stamPerVit = row != null ? row.StamPerVit : 0f;
            var stamPerLvl = row != null ? row.StamPerLvl : 0f;

            //   官方：生命 = hpadd + 起始体力（亚马逊 30+20 = **50**）、法力 = 起始精力（**15**）、
            //   耐力 = `charstats.stamina`（**84**）。旧式 `vit × life_per_vit` 给出 60/22/20
            //   （实机 `playercheck` 与属性面板都显示这三个错值）。公式与出处逐条见
            //   —— 它们本来就与官方一致，错的只有**截距**。
            var startVit = row != null ? row.Vit : 0;
            var startEng = row != null ? row.Eng : 0;
            // 起始截距与职业格挡系数一律**读表**（`class_c.hp_add` / `base_stamina`
            //   / `block_factor`），代码里不再有第二份真值。
            var hpAdd = row != null ? row.HpAdd : 0;
            var baseStamina = row != null ? row.BaseStamina : 0;
            var classBlockFactor = row != null ? row.BlockFactor : 0;

            MaxLife = MaxLifeOf(hpAdd, startVit, lifePerVit, lifePerLvl, Vit, Level) + BonusMaxLife;
            MaxMana = MaxManaOf(startEng, manaPerMag, manaPerLvl, Eng, Level) + BonusMaxMana;
            MaxStamina = MaxStaminaOf(baseStamina, startVit, stamPerVit, stamPerLvl, Vit, Level) + BonusMaxStamina;

            var defense = ArmorClass + BonusDefenseFlat + dex / 4;
            Defense = Mathf.Max(0, defense + ApplyPercent(defense, DefensePercent));

            var ar = (dex - 7) * 5 + _toHitFactor + BonusAttackRating;
            AttackRating = Mathf.Max(0, ar + ApplyPercent(ar, AttackRatingPercent));

            BlockChance = ComputeBlockChance(HasShield, dex, BonusBlock, classBlockFactor, Level);
        }

        /// <summary>
        /// 格挡率（%，[0,75]）。**有盾才有格挡**（<paramref name="hasShield"/>）。
        /// <para>**公式出处**（Arreat Summit「Basics: Character Information → Blocking」，
        /// <c>Total Blocking = (Blocking * (Dexterity - 15)) / (Character Level * 2)</c>，
        /// 其中 <c>Blocking</c> = "a value inherent to that particular player class"（职业固有值）
        /// + "any other block bonuses from items"（装备项之和），且该值 <c>capped at 75%</c>。</para>
        /// <para>本工程落位：<paramref name="classBlockFactor"/> = 职业项（`class_c.block_factor`
        /// ← 官方 `charstats.txt` 第 32 列 `BlockFactor`）；
        /// <paramref name="toBlock"/> = 装备项（**盾牌基材 `item_c.block`** + 词缀 `block`→官方 stat
        /// `toblock`，见 `Properties.txt:18` / `ItemStatCost.txt:22`）。
        /// 整数除法与钳制口径与官方一致（钳的是**最终百分比**；本工程数据域内 Blocking ≤ 75
        /// ⇒ "钳 Blocking"与"钳结果"两种读法等价）。</para>
        /// <para>没有盾 ⇒ 恒 0%（官方：无盾/无死灵头骨时角色屏**不显示**格挡率这一行）：
        /// 职业 <c>BlockFactor</c> **不单独**产生格挡率（否则 1 级徒手亚马逊会算出
        /// 闸门判的是"**有没有盾**"，不是"装备格挡值是否 &gt; 0" —— 官方 `Buckler` 的 `block = 0`
        /// 但照样格挡（详见 <see cref="HasShield"/> 的注释）。</para>
        /// </summary>
        public static int ComputeBlockChance(bool hasShield, int dex, int toBlock, int classBlockFactor, int level)
        {
            if (!hasShield || level <= 0) return 0;
            var chance = (dex - 15) * (toBlock + classBlockFactor) / (2 * level);
            return Mathf.Clamp(chance, 0, MaxBlock);
        }

        /// <summary>取某系抗性（%，夹在 [-100, 75]）。</summary>
        public int ResistOf(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return Mathf.Clamp(BonusResFire, MinResist, MaxResist);
                case DamageType.Cold: return Mathf.Clamp(BonusResCold, MinResist, MaxResist);
                case DamageType.Lightning: return Mathf.Clamp(BonusResLight, MinResist, MaxResist);
                case DamageType.Poison: return Mathf.Clamp(BonusResPoison, MinResist, MaxResist);
                case DamageType.Magic: return Mathf.Clamp(BonusResMagic, MinResist, MaxResist);
                case DamageType.Physical:
                    // 原版玩家**没有**物理抗性百分比；物理减伤由装备的固定 DR（`red-dmg`）实现，
                    // 那属于 Combat 的结算口径（见 libd2 combat.zig:6-11），故这里恒为 0。
                    return 0;
                default:
                    PlayerLog.Warn($"ResistOf 收到未登记的伤害类型 {(int)type}，按 0 处理");
                    return 0;
            }
        }

        /// <summary>清空装备加成（复位 / 装备变化前调用；不动四维基础与等级）。</summary>
        public void ClearBonuses()
        {
            BonusStr = BonusDex = BonusVit = BonusEng = 0;
            BonusMaxLife = BonusMaxMana = BonusMaxStamina = 0;
            ArmorClass = BonusDefenseFlat = DefensePercent = 0;
            BonusAttackRating = AttackRatingPercent = 0;
            BonusBlock = 0;
            HasShield = false;
            BonusResFire = BonusResCold = BonusResLight = BonusResPoison = BonusResMagic = 0;
        }

        /// <summary>
        /// 按装备列表重算加成（`Events.EquipChanged` 的载荷）。
        /// 词缀数值取 `ItemAffix.value`；为 0（Item 未填实际 roll 值）时退回 `(min+max)/2` 并 Warn。
        /// 不认识的 `mod` 码**不静默**：每个码只报一次 Info（它们由 Combat/Item 消费）。
        /// </summary>
        public void ApplyEquipment(IReadOnlyList<ItemStack> equip)
        {
            ClearBonuses();

            if (equip == null)
            {
                PlayerLog.Warn("ApplyEquipment 收到 null 装备列表 ⇒ 按「无装备」处理");
                Recompute();
                return;
            }

            var armorPieces = 0;
            var shieldPieces = 0;
            var valueFallbackLogged = false;

            for (var i = 0; i < equip.Count; i++)
            {
                var item = equip[i];
                if (item == null)
                {
                    PlayerLog.Warn($"ApplyEquipment：装备列表第 {i} 项为 null（数据异常）⇒ 跳过");
                    continue;
                }

                // 护甲值：官方在**生成物品时**就把防御 roll 成定值，`Def.ItemStack` 只存区间
                // ⇒ 取区间中点（近似口径）。
                if (item.defMax > 0)
                {
                    armorPieces++;
                    ArmorClass += (item.defMin + item.defMax) / 2;
                }

                //   出处：`原版资源/d2lod1.10txt-1.10f/data/global/excel/Armor.txt` 第 11 列 `block`
                //        （表头 :1；盾牌行 :24..204，逐行核过全目录只有 `shie`/`ashd`/`head` 三类非 0）
                //        → 打表链 `tools/table-convert/convert.py` 的 `s.col("block", …)`（Armor.txt `block`）
                //        → `client/Assets/Scripts/Table/Base/BaseItem.cs:33 public int Block`
                //        → `策划/数值文档/item_c.txt` 第 22 列。
                //   为什么回表取而不是读 `ItemStack`：`ItemStack`（`Module/Contracts.cs`，契约文件）里
                var baseRow = Table.TableLoader.Item(item.itemId);
                if (baseRow == null)
                {
                    PlayerLog.Warn($"ApplyEquipment：装备「{item.name}」(itemId={item.itemId}) 在 `item_c` 里取不到行"
                                   + " ⇒ 该件的**基材格挡**按 0（词缀仍照常计）");
                }
                else if (IsShieldType(baseRow.Type))
                {
                    shieldPieces++;
                    HasShield = true;
                    BonusBlock += baseRow.Block;
                }
                else if (baseRow.Block != 0)
                {
                    PlayerLog.Warn($"ApplyEquipment：非盾装备「{item.name}」(type={baseRow.Type}) 的 `item_c.block`="
                                   + $"{baseRow.Block} ≠ 0 ⇒ **不计入**格挡"
                                   + "（官方只有 `shie`/`ashd`/`head` 三类有 block）");
                }

                if (item.affixes == null) continue;
                for (var k = 0; k < item.affixes.Count; k++)
                {
                    var affix = item.affixes[k];
                    if (affix == null)
                    {
                        PlayerLog.Warn($"ApplyEquipment：{item.name} 的第 {k} 条词缀为 null ⇒ 跳过");
                        continue;
                    }

                    var value = affix.value;
                    if (value == 0 && (affix.min != 0 || affix.max != 0))
                    {
                        value = Mathf.RoundToInt((affix.min + affix.max) * 0.5f);
                        if (!valueFallbackLogged)
                        {
                            valueFallbackLogged = true;
                            PlayerLog.Warn(
                                $"词缀 {affix.affixId}({affix.mod}) 的 value=0（Item 未填实际数值？）" +
                                $"⇒ 本件装备按区间中点 {value} 计算");
                        }
                    }

                    ApplyAffix(affix.mod, value);
                }
            }

            Recompute();
            PlayerLog.Info(
                $"[Equip] 装备生效：{equip.Count} 件（含防御 {armorPieces} 件 → 护甲 {ArmorClass}）；" +
                $"盾 {shieldPieces} 件（带盾={HasShield} ⇒ 格挡闸门{(HasShield ? "开" : "关，恒 0%")}）；" +
                $"力+{BonusStr} 敏+{BonusDex} 体+{BonusVit} 精+{BonusEng} " +
                $"生命+{BonusMaxLife} 法力+{BonusMaxMana} 耐力+{BonusMaxStamina} " +
                $"防御+{BonusDefenseFlat}(+{DefensePercent}%) AR+{BonusAttackRating} 格挡+{BonusBlock} " +
                $"抗性(火/冰/电/毒)+{BonusResFire}/{BonusResCold}/{BonusResLight}/{BonusResPoison} " +
                $"⇒ 生命上限 {MaxLife} 防御 {Defense} AR {AttackRating} 格挡 {BlockChance}%");
            _equipSummaryLogged = true;
        }

        /// <summary>是否已收到过装备生效（自检/调试用；说明事件链路走通）。</summary>
        public bool EquipmentApplied => _equipSummaryLogged;

        /// <summary>
        /// 官方**盾类**判定（`armor.txt` 的 `type` 列）：`shie` 盾 / `ashd` 圣骑士盾 / `head` 死灵头骨。
        /// <para>出处：`原版资源/d2lod1.10txt-1.10f/data/global/excel/Armor.txt` —— 全表 203 行里
        /// `block`（第 11 列）非 0 的 53 行**全部**落在这三类（逐行核过）。</para>
        /// </summary>
        private static bool IsShieldType(string type)
            => type == "shie" || type == "ashd" || type == "head";

        /// <summary>把一条词缀折进加成。码表 = `策划/数值文档/affix_c.txt` 的 `mod` 列（官方 ModCodes）。</summary>
        private void ApplyAffix(string mod, int value)
        {
            if (string.IsNullOrEmpty(mod))
            {
                if (_unmappedLogged.Add("(空)"))
                    PlayerLog.Warn("ApplyEquipment：词缀 mod 为空（数据异常）⇒ 该条忽略");
                return;
            }

            switch (mod)
            {
                // 四维
                case "str": BonusStr += value; return;
                case "dex": BonusDex += value; return;
                case "enr": BonusEng += value; return;          // 官方精力码 = enr
                // 上限
                case "hp": BonusMaxLife += value; return;
                case "mana": BonusMaxMana += value; return;
                case "stam": BonusMaxStamina += value; return;
                // 防御 / 命中 / 格挡
                case "ac": BonusDefenseFlat += value; return;
                case "ac%": DefensePercent += value; return;
                case "att": BonusAttackRating += value; return;
                case "block": BonusBlock += value; return;      // 官方 toblock（盾牌）
                // 抗性
                case "res-fire": BonusResFire += value; return;
                case "res-cold": BonusResCold += value; return;
                case "res-ltng": BonusResLight += value; return;
                case "res-pois": BonusResPoison += value; return;
                //   原版 `res-all` 是否含魔法抗性，本项目**没有出处** ⇒ 保持旧的 4 元素口径不动。
                case "res-magic": BonusResMagic += value; return;
                case "res-all":
                    BonusResFire += value; BonusResCold += value;
                    BonusResLight += value; BonusResPoison += value;
                    return;
                default:
                    // 伤害/吸血/金币/光照/攻速/施法… 都不属于**玩家派生属性**（由 Combat / Item / Audio 消费）
                    if (_unmappedLogged.Add(mod))
                        PlayerLog.Info($"词缀 mod={mod} 不参与玩家派生属性（由 Combat/Item 消费），本模块忽略（每个码只报一次）");
                    return;
            }
        }

        /// <summary>`D2ApplyPercent(v,p) = v*p/100`（C 整数除法，向零截断；与官方一致）。</summary>
        private static int ApplyPercent(int value, int percent)
        {
            if (percent == 0) return 0;
            return value * percent / 100;
        }
    }
}
