// ─────────────────────────────────────────────────────────────────────────────
//
// 唯一职责：**物品/技能 → 原版图标路径**（把 `Table/**` 的配表 id 翻成 DC6 解出的 PNG 路径）。
// 为什么要这个文件：UI 里三个地方（背包格 / 装备栏 / 腰带 / 技能树 / tooltip）都要按 id 取图标，
// 各写一遍必然出现"有一处拼错路径 ⇒ 静默取不到图"（`constraints.md` #9 那类失败）。
// 路径全部经 `Core/ResPaths.cs`（唯一来源）。
//
// 依据（**不是猜的**）：
//   · 物品：原版物品图标文件名 = `inv<code>.DC6`（小写），而 `Table.Item.tsv` 的 `code` 列就是
//     官方 item code（`hax` / `axe` / `lax` / `hp1` …）⇒ 路径 = `D2/Items/inv{code}`。
//     配表字段出处：`Table/Base/BaseItem.cs:13` `public string Code; // 官方 code`。
//   · 技能：原版职业技能图标文件 `SPELLS/{Am,So,Ne,Pa,Ba}Skillicon.DC6` 实测各 60 帧 48×48
//     （野蛮人 156 帧），与 `Table/Skill.tsv` 的「每职业 30 技能、official_id 连续 6..155」一致
//     ⇒ 帧号 = `(official_id − (6 + 30×(class−1))) × 2`，+1 = 灰化帧（不能点的技能用）。
//     配表字段出处：`Table/Base/BaseSkill.cs:12-16`（Class / Tree / Name / Code / OfficialId）。
//   · 职业码（`ama`/`sor`/`nec`/`pal`/`bar`）出处：`Table/Class.tsv` 的 `code` 列
//     （`Table/Base/BaseClass.cs:13` `public string Code;`）。
//
// 本文件在 UI 层：只允许引用 `Diablo2.Core` / `Table` / 引擎；**不得引用 `Diablo2.Module`**。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>配表 id → 原版图标路径（物品 / 技能）。全部为**纯函数**，离线宿主可直接断言。</summary>
    internal static class D2Icon
    {
        private const string Tag = "Icon";

        /// <summary>每职业技能数（官方 Skill.txt：每职业 30 个技能）。</summary>
        public const int SkillsPerClass = 30;

        /// <summary>第 1 个职业技能的 official_id（亚马逊 = 6，出处见文件头注释）。</summary>
        public const int FirstOfficialId = 6;

        /// <summary>官方 charstats 的职业名（`class_c.code`：Amazon/Sorceress/…；查不到返回 null）。</summary>
        public static string ClassCodeOf(int classId)
        {
            var row = Table.TableLoader.Class(classId);
            if (row != null && !string.IsNullOrEmpty(row.Code)) return row.Code;

            UiLog.WarnOnce("icon.class." + classId,
                $"配表 class_c 里没有 id={classId}（或 code 为空）⇒ 该职业技能图标取不到"
                + "（请确认 Table.TableLoader.LoadAll 已执行）");
            return null;
        }

        /// <summary>
        /// <para>
        /// **3 字母**（`amaSkillicon_0.png` …，共 5 组），而配表 `class_c` 的 **`code` 列是官方 charstats 的
        /// 职业名**（`Amazon`/`Sorceress`/…，见 `Table/Base/BaseClass.cs:13`）；3 字母码在**另一列** `skill_class`
        /// 上（`BaseClass.cs:14` 明文「skills.txt 的 charclass 标识（ama/sor/nec/pal/bar）」）。
        /// 旧代码把 `Code` 拼进文件名 ⇒ 请求 `D2/UI/SkillIcon/AmazonSkillicon_59`，**磁盘上没有这个文件**，
        /// 于是 30 个技能节点全部退回纯色块（Play 实测日志：`[Ui] 原版贴图缺失：D2/UI/SkillIcon/AmazonSkillicon_59`）。
        /// </para>
        /// </summary>
        public static string IconClassCodeOf(int classId)
        {
            var row = Table.TableLoader.Class(classId);
            if (row == null)
            {
                UiLog.WarnOnce("icon.class." + classId,
                    $"配表 class_c 里没有 id={classId} ⇒ 该职业技能图标取不到"
                    + "（请确认 Table.TableLoader.LoadAll 已执行）");
                return null;
            }

            if (!string.IsNullOrEmpty(row.SkillClass)) return row.SkillClass;

            // 非预期分支：skill_class 列为空 ⇒ 退回 Code（官方职业名）并点名：素材文件名一定是 3 字母码，
            // 用 Code 必然取不到图（旧 B3 就是这么坏的），所以这里必须留日志而不是静默。
            UiLog.WarnOnce("icon.class.skillclass." + classId,
                $"配表 class_c 的 skill_class 列为空（id={classId}，Code={row.Code}）⇒ 图标路径只能用 Code 拼，"
                + "而原版图标文件名前缀是 3 字母码（ama/sor/nec/pal/bar）⇒ 该职业技能图标很可能取不到");
            return row.Code;
        }

        /// <summary>
        /// 配表 `code` → 图标文件后缀的别名表（本表逐条来自**原版 D2 数据表**，
        /// **不是猜的**：`原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/`
        /// 的 `Weapons.txt` / `Armor.txt` / `Misc.txt` 的 **`invfile` 列**）。
        /// <para>
        /// 图标名出自 `invfile` 列（例：`bax`=Broad Axe → `invbrx`；`qui`=Quilted Armor → `invqlt`；
        /// `hpo`=Healing Potion → `invrps`）。只按 `inv{code}` 拼路径会让 137 个物品里 **50 个拼错**
        /// （磁盘上没有那个文件）⇒ 商店/背包/腰带该格**贴图静默取不到、只剩白底方块**
        /// （用户投诉「商店没商品图标」）⇒ 本表列出这 50 条别名。
        /// 逐条核对结果：137 个 code 中 **123 个能对上磁盘文件**（其中 50 个要靠本表），
        /// 另有 **14 个原版图标本身不在本机素材里**（刺客/德鲁伊/野蛮人/圣骑士/死灵的专属装备，
        /// 见 `client/资源欠缺清单.md`）。
        /// </para>
        /// <para>`qui → quil` 是**错的**（`invquil` 不存在，`invqlt` 才是 Quilted Armor）。</para>
        /// </summary>
        private static readonly Dictionary<string, string> IconFileAlias = new Dictionary<string, string>
        {
            { "aqv", "qvr" }, { "bax", "brx" }, { "gcb", "gsba" }, { "gcg", "gsga" },
            { "gcr", "gsra" }, { "gcv", "gsva" }, { "gcw", "gswa" }, { "gcy", "gsya" },
            { "gfb", "gsbb" }, { "gfg", "gsgb" }, { "gfr", "gsrb" }, { "gfv", "gsvb" },
            { "gfw", "gswb" }, { "gfy", "gsyb" }, { "glb", "gsbd" }, { "glg", "gsgd" },
            { "glr", "gsrd" }, { "glw", "gswd" }, { "gly", "gsyd" }, { "gpb", "gsbe" },
            { "gpg", "gsge" }, { "gpl", "gps" }, { "gpr", "gsre" }, { "gpv", "gsve" },
            { "gpw", "gswe" }, { "gpy", "gsye" }, { "gsb", "gsbc" }, { "gsg", "gsgc" },
            { "gsr", "gsrc" }, { "gsv", "gsvc" }, { "gsw", "gswc" }, { "gsy", "gsyc" },
            { "gzv", "gsvd" }, { "hpf", "rpl" }, { "hpo", "rps" }, { "ibk", "rbk" },
            { "isc", "rsc" }, { "mpf", "bpl" }, { "mpo", "bps" }, { "opl", "ops" },
            { "qui", "qlt" }, { "rvl", "vpl" }, { "rvs", "vps" }, { "tbk", "bbk" },
            { "tch", "trch" }, { "tkf", "tkn" }, { "tsc", "bsc" }, { "vps", "wps" },
            { "wms", "yps" }, { "yps", "nps" },
        };

        /// <summary>
        /// 图标层的「原版图标不在本机素材里」占位色（**不是白**）：
        /// 白底方块看起来像"图没加载"，这个暗青色一眼能认出是占位（并已逐条登记在资源欠缺清单）。
        /// </summary>
        public static readonly Color MissingIconColor = new Color(0.10f, 0.13f, 0.16f, 0.85f);

        /// <summary>把配表 `code` 翻成图标文件名后缀（命中别名表就用别名）。</summary>
        public static string IconFileCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            return IconFileAlias.TryGetValue(code, out var alias) ? alias : code;
        }

        /// <summary>
        /// 原版图标**是否真的在可加载源里**（**同步**回答，走引擎 `Game.Res.Exists`：
        /// 它只回答"在不在"、不驻留；`ResourceManager` 按路径缓存 ⇒ 同一张图一辈子只探一次）。
        /// <para>用途：把「配表里没有这个 code」与「有 code 但原版图不在本机素材里」分开处理 ——
        /// 后者要给一个**看得出来的占位**而不是让 Image 停在初始的白色（Play 实测：
        /// 商店第 10 格就是这么一个白方块）。</para>
        /// <para>为什么不用 `TryGet`：它只取**已驻留**的（没装进来就返回 null，会把"在"误答成"不在"）；
        /// 也不用 `LoadAsset`：那是异步的，回答不了"现在在不在"。三者语义差别见
        /// `clover-client-unity-engine/Runtime/Core/Contracts.cs` 的 `IResourceManager.Exists` 注释。</para>
        /// <para>路径是**相对 `CloverRes` 根前缀**的写法（`D2/Items/invbrx`），
        /// 根前缀由后端拼 —— 别再写 `ResPaths.Root + "/" + path`（那是 Unity `Resources` 的拼法）。
        /// `Game.Res` 未初始化（`CloverRes.Init` 未调用）⇒ 恒 false（引擎没起来 = 什么都取不到）。</para>
        /// </summary>
        public static bool ItemIconExists(int itemId)
        {
            var path = ItemIconPath(itemId);
            if (string.IsNullOrEmpty(path)) return false;
            return Game.Res != null && Game.Res.Exists(path);
        }

        /// <summary>配表 id → 图标路径（`D2/Items/inv{code}`，code 已过别名表）。</summary>
        public static string ItemIconPath(int itemId)
        {
            if (itemId <= 0)
            {
                // 非预期分支：itemId 是空/无效物品（0 = 空）⇒ 不该来取图标
                UiLog.WarnOnce("icon.item.id0", "请求物品图标时 itemId ≤ 0（空物品）⇒ 无图标（调用方应先判 !occupied）");
                return null;
            }

            var row = Table.TableLoader.Item(itemId);
            if (row == null)
            {
                UiLog.WarnOnce("icon.item.miss." + itemId,
                    $"配表 item_c 里没有 id={itemId} ⇒ 该物品图标取不到（退回品质色块；"
                    + "请确认 Table.TableLoader.LoadAll 已执行）");
                return null;
            }
            if (string.IsNullOrEmpty(row.Code)) return null;

            return ResPaths.ItemIcon(IconFileCode(row.Code));
        }

        /// <summary>
        /// 技能图标路径（`D2/UI/SkillIcon/{cls}Skillicon_{帧}`）；
        /// <paramref name="dull"/> = 灰化帧（原版对"还不能点"的技能用第 2 帧）。
        /// 配表缺失 ⇒ 返回 null（调用方退回纯色节点）。
        /// </summary>
        public static string SkillIconPath(int skillId, bool dull)
        {
            var row = Table.TableLoader.Skill(skillId);
            if (row == null)
            {
                UiLog.WarnOnce("icon.skill.miss." + skillId,
                    $"配表 skill_c 里没有 id={skillId} ⇒ 该技能图标取不到（退回纯色节点）");
                return null;
            }

            var cls = IconClassCodeOf(row.Class);      // ★ 文件名用的是 skill_class（ama/…），不是 Code（Amazon）
            if (cls == null) return null;

            var index = (row.OfficialId - (FirstOfficialId + SkillsPerClass * (row.Class - 1))) * 2;
            if (index < 0)
            {
                // 非预期分支：official_id 与 class 对不上（配表被改过？）⇒ 点名，不静默取到隔壁职业的图
                UiLog.WarnThrottled("icon.skill.range." + skillId,
                    $"技能 {skillId}（class={row.Class} official_id={row.OfficialId}）算出的图标帧号 {index} < 0"
                    + " ⇒ 图标取不到（请核对 Table/Skill.tsv 的 class 与 official_id 两列口径）");
                return null;
            }

            return ResPaths.SkillIcon(cls, index + (dull ? 1 : 0));
        }

        /// <summary>
        /// 技能树**背景页**路径（`D2/UI/Panel/skltree_{cls}_back_{page}`）。
        /// 原版 `SPELLS/skltree_*_back.DC6` 实测 16 帧 = 4 张 320×432（tile 拼装后纵切），
        /// ⇒ <paramref name="page"/> ∈ [0,4)；越界返回 null 并点名。
        /// </summary>
        public static string SkillTreeBackPath(int classId, int page)
        {
            // 实测磁盘：`Resources/Clover/D2/UI/Panel/skltree_{a,b,n,p,s}_back_0..3.png`（本项目按 tile 拼装后纵切）。
            var cls = IconClassCodeOf(classId);
            if (string.IsNullOrEmpty(cls)) return null;
            var letter = cls.Substring(0, 1).ToLowerInvariant();

            if (page < 0 || page > 3)
            {
                UiLog.WarnThrottled("icon.treeback.range",
                    $"技能树背景页 page={page} 越界（原版只有 4 张）⇒ 退回第 0 页");
                page = 0;
            }
            return ResPaths.D2UiPanel + "skltree_" + letter + "_back_" + page;
        }

        /// <summary>`skillId` 所属职业 id（查不到 0），用于面板分组显示。</summary>
        public static int ClassOfSkill(int skillId)
        {
            var row = Table.TableLoader.Skill(skillId);
            return row != null ? row.Class : 0;
        }

        /// <summary>
        /// 把**原版物品图标**贴到某个图标层上（背包格 / 装备栏 / 腰带共用这一处口径）。
        /// <para>
        /// 贴图是**异步**回来的，所以：
        ///  ① 同一个路径不重复发起加载（<paramref name="cache"/> 按槽位记住"最近一次贴的路径"）；
        ///  ② 品质色由 `UiArt.SetArtTint` **先记下**，加载完成时由 `UiArt.SetSprite` 统一套用。
        ///     原版物品图是 8bit 索引色，**改动色就是改原版像素** ⇒ 这里只用品质色**轻微**着色
        ///     （原版的做法是把名字染色、图标不染；本项目把染色收在 0.62 档以免把原图压暗）。
        ///  ③ 图标取不到（配表缺行 / 文件缺失）⇒ 退回**品质色块**并 Warn（`ItemIconPath` 已点名）。
        /// </para>
        /// </summary>
        public static void ApplyItemIcon(Image icon, string[] cache, int index, ItemStack item)
        {
            if (icon == null || cache == null || index < 0 || index >= cache.Length) return;

            if (item == null)
            {
                icon.gameObject.SetActive(false);
                cache[index] = null;
                return;
            }

            icon.gameObject.SetActive(true);
            // 原版物品图是**像素画**：图标框与图的原生比例不一致时按比例内缩，
            //   绝不把原版像素拉长/压扁（`UiArt.SetSprite` 不会改 preserveAspect）。
            icon.preserveAspect = true;

            var path = ItemIconPath(item.itemId);
            if (string.IsNullOrEmpty(path))
            {
                // 非预期分支：itemId ≤ 0 或配表里查不到 code ⇒ 用**暗色**占位。
                //   ⛔ 不用品质色：普通品质的品质色是纯白，画出来是一块白方块
                //   （与「图没加载」的表现一模一样，正是商店格子被报「白方块」的来源）。
                icon.sprite = null;
                icon.color = MissingIconColor;
                cache[index] = null;
                return;
            }

            if (cache[index] == path) return;           // 同一个图标，已经在（或正在）加载

            // 非预期分支：**可加载源里没有这张原版图**（348 张里确实缺这 14 个 code，
            //   见 `client/资源欠缺清单.md`）⇒ 给一个**看得出来的暗色占位**，
            //   而不是让 Image 停在初始白色（Play 实测：商店第 10 格就是一块白方块）。
            //   `Game.Res == null`（引擎没起来）时**不在这里断言"素材缺"**：那是启动顺序问题、
            //      不是素材问题，混成同一句话会把排查方向带偏 ⇒ 交给下面 `UiArt.SetSprite` 的
            //      `res.null` 分支去报（那条分支原地保留）。
            if (Game.Res != null && !Game.Res.Exists(path))
            {
                cache[index] = path;
                icon.sprite = null;
                icon.color = MissingIconColor;
                UiLog.WarnOnce("icon.item.file." + item.itemId,
                    $"原版物品图不在本批素材里：{path}（itemId={item.itemId}「{item.name}」，code 见 D2Icon.IconFileAlias）"
                    + " ⇒ 该格用暗色占位（已登记资源欠缺清单）；换素材后无需改代码");
                return;
            }

            cache[index] = path;
            var tint = QualityTint(item.quality);
            icon.color = tint;                          // 贴图未到时可见的底色
            UiArt.SetArtTint(icon, tint);               // 记下"这幅图想要的色"（加载完成时统一套用）
            UiArt.SetSprite(icon, path);
        }

        /// <summary>
        /// 图标层用的品质色（**比文字用的品质色浅**）：原版像素不能被染死，
        /// 所以把品质色往白里提 62%，只留一点点色偏。
        /// </summary>
        public static Color QualityTint(ItemQuality quality)
        {
            var c = ItemQualityColor.Of(quality);
            return new Color(1f - (1f - c.r) * 0.38f, 1f - (1f - c.g) * 0.38f, 1f - (1f - c.b) * 0.38f, 1f);
        }

        // w3 审计删除：`DistinctClasses(IEnumerable<int>)` —— 全仓（含 13 个离线宿主）
        //   **零调用方**（「定义了但没人用」）。技能树面板现在按 `Def/SkillTreeLayout` 的
    }
}
