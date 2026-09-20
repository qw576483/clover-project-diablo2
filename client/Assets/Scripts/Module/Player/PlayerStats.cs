// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Player/PlayerStats.cs
// 主角**四维 + 派生属性**的唯一计算处（生命/法力/耐力/防御/命中 AR/格挡/四抗）。
//
// ⛔ 三条硬规则
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

        /// <summary>抗性下限（原版下限 -100%）。</summary>
        public const int MinResist = -100;

        /// <summary>生命/法力/耐力/防御的下限（PropertyType 之外的最小值，防负数）。</summary>
        private const int MinVital = 1;

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

        /// <summary>盾牌格挡加成（词缀 `block`，官方 `toblock`）。</summary>
        public int BonusBlock;

        /// <summary>火焰抗性（词缀 `res-fire` / `res-all`）。</summary>
        public int BonusResFire;

        /// <summary>冰冷抗性。</summary>
        public int BonusResCold;

        /// <summary>闪电抗性。</summary>
        public int BonusResLight;

        /// <summary>毒素抗性。</summary>
        public int BonusResPoison;

        /// <summary>
        /// 魔法抗性（★ 片 16 / 【消除 E27】）：官方 `itemstatcost` 的 `res-magic`。
        /// <para>
        /// ⚠️ 本项目 Act I 的 `affix_c` 里**没有** `res-magic` 词缀（低等级词缀集只有 火/冰/电/毒 与
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
        /// 重算全部派生属性。**公式与出处**（改动前先读 `docs/配表说明.md` §4）：
        /// <list type="bullet">
        /// <item>生命 = 体力 × `life_per_vit` + (等级-1) × `life_per_lvl`
        ///       —— 官方 `charstats.txt`（`class_c` 列注释：LifePerVitality / LifePerLevel，已 ÷4）；
        ///       与 `UI/CharCreatePanel.LifeOf` 同一式子（验收 #17）。</item>
        /// <item>法力 / 耐力：同构（`mana_per_mag`+`mana_per_lvl` / `stam_per_vit`+`stam_per_lvl`）。</item>
        /// <item>防御 = 护甲值 + 敏捷/4，再乘 `item_armor%` 加成
        ///       —— 出处：`_assets_tmp/d2src/libd2/packages/game/src/combat.zig:43-50`（`GetDefense`，
        ///       官方 `Units.cpp:2304` 的移植）。</item>
        /// <item>命中 AR = (敏捷-7)×5 + 职业命中修正 + 装备 AR，再乘 `item_tohit%`
        ///       —— 出处：`libd2 …/combat.zig:52-66`（`GetAttackRate` 的 partial port，玩家 base=(dex-7)*5）；
        ///       职业命中修正 = `class_c.to_hit_factor`（官方 `charstats.ToHitFactor`）。</item>
        /// <item>格挡 = (敏捷-15) × (toblock + BlockFactor) / (2×等级)，上限 75
        ///       —— 出处：`libd2 …/combat.zig:10`（`GetBlockRate`，官方 `Units.cpp`）。
        ///       ⚠️ `class_c` **未导入**官方 `BlockFactor` 列 ⇒ 该项按 0（见回报「未决」）。</item>
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

            MaxLife = Mathf.Max(MinVital,
                Mathf.RoundToInt(Vit * lifePerVit + (Level - 1) * lifePerLvl) + BonusMaxLife);
            MaxMana = Mathf.Max(MinVital,
                Mathf.RoundToInt(Eng * manaPerMag + (Level - 1) * manaPerLvl) + BonusMaxMana);
            MaxStamina = Mathf.Max(MinVital,
                Mathf.RoundToInt(Vit * stamPerVit + (Level - 1) * stamPerLvl) + BonusMaxStamina);

            var defense = ArmorClass + BonusDefenseFlat + dex / 4;
            Defense = Mathf.Max(0, defense + ApplyPercent(defense, DefensePercent));

            var ar = (dex - 7) * 5 + _toHitFactor + BonusAttackRating;
            AttackRating = Mathf.Max(0, ar + ApplyPercent(ar, AttackRatingPercent));

            BlockChance = ComputeBlockChance(dex, BonusBlock, Level);
        }

        /// <summary>
        /// 格挡率（%，[0,75]）。`BlockFactor`（官方 charstats 列）本项目配表未导入 ⇒ 传 0。
        /// 公式：`(dex-15) * (toblock+BlockFactor) / (2*level)`，整数除法（与官方同）。
        /// </summary>
        public static int ComputeBlockChance(int dex, int toBlock, int level)
        {
            if (toBlock <= 0 || level <= 0) return 0;
            var chance = (dex - 15) * toBlock / (2 * level);
            return Mathf.Clamp(chance, 0, MaxResist);
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
                // ⇒ 取区间中点（回报「未决」已登记该近似）。
                if (item.defMax > 0)
                {
                    armorPieces++;
                    ArmorClass += (item.defMin + item.defMax) / 2;
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
                $"力+{BonusStr} 敏+{BonusDex} 体+{BonusVit} 精+{BonusEng} " +
                $"生命+{BonusMaxLife} 法力+{BonusMaxMana} 耐力+{BonusMaxStamina} " +
                $"防御+{BonusDefenseFlat}(+{DefensePercent}%) AR+{BonusAttackRating} 格挡+{BonusBlock} " +
                $"抗性(火/冰/电/毒)+{BonusResFire}/{BonusResCold}/{BonusResLight}/{BonusResPoison} " +
                $"⇒ 生命上限 {MaxLife} 防御 {Defense} AR {AttackRating} 格挡 {BlockChance}%");
            _equipSummaryLogged = true;
        }

        /// <summary>是否已收到过装备生效（自检/调试用；说明事件链路走通）。</summary>
        public bool EquipmentApplied => _equipSummaryLogged;

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
                // ★ 片 16（E27）：官方 `res-magic`（魔法抗性）。⚠️ 本条**不顺手改** `res-all` 的语义 ——
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
