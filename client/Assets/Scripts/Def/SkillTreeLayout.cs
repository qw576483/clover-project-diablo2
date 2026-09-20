// -----------------------------------------------------------------------------
// Diablo2 · Def/SkillTreeLayout.cs
// [!] **生成物，禁止手改** —— 由 `tools/d2codec/export_skilltree_layout.py` 生成。
//
// 口径（复跑：`python tools/d2codec/export_skilltree_layout.py --write`）：
//   · 节点框位置：从原版 `SPELLS/skltree_{a,b,n,p,s}_back.DC6` tile 拼装出的 320x432 页
//     （`Resources/Clover/D2/UI/Panel/skltree_{cls}_back_{0..3}.png`）**逐像素解析**得到；
//     坐标 = **原版 px**，原点 = 该页左上角（x 右 / y 下）。
//   · 页 <-> 系：底图第 k 页 <-> 原版 `SkillPage = k`（k=1,2,3），
//     依据 ① 底图节点框 (列,行) 集合 == `skilldesc.txt` 该 Page 的 (SkillColumn,SkillRow)
//            （15/15 页一致）
//     依据 ② 树区右边框的缺口逐页落在对应页签槽（15/15 页一致）
//     => **两条独立特征互证**；任一不成立时生成器 abort（不猜）。
//   · `skilldesc.txt` 行号 = `official_id + 1`（实测）。
//   · 页签槽：页 0（共用右列，5 职业逐像素相同）解析；**自上而下 = 系 3 / 系 2 / 系 1**。
//   · 节点框 = 原版画出的「L 形管线」的外接矩形（实测量处完整可见的格 = 45x50）。
//
// 为什么这份表在 `Def/` 而不在 `Module/Skill/`：
//   `tools/ai-skill/conventions.md` 硬性规定「**UI 不许 using Diablo2.Module.**」；
//   面板（`UI/SkillTreePanel`）要读这份表 => 它只能是**无逻辑的纯数据**（`Def/` 的定位）。
// -----------------------------------------------------------------------------

namespace Diablo2.Def
{
    /// <summary>原版 px 矩形（原点 = 320x432 底图页左上角）。</summary>
    public struct SkillArtRect
    {
        public float x;
        public float y;
        public float w;
        public float h;

        public SkillArtRect(float x, float y, float w, float h)
        {
            this.x = x;
            this.y = y;
            this.w = w;
            this.h = h;
        }
    }

    /// <summary>一个技能槽的**原版像素**位置（`skillId` = 我们 `skill_c` 的主键）。</summary>
    public struct SkillTreeCell
    {
        /// <summary>`skill_c` 主键（= `SkillDef.id`）。</summary>
        public int skillId;

        /// <summary>职业 1..5（= `PlayerClass`）。</summary>
        public int cls;

        /// <summary>系 1..3（= 原版 `SkillPage`；面板页号也用 1..3）。</summary>
        public int tree;

        /// <summary>原版 `SkillRow`（1..6）。</summary>
        public int row;

        /// <summary>原版 `SkillColumn`（1..3）。</summary>
        public int col;

        /// <summary>原版底图画出的节点框（外接矩形）。</summary>
        public SkillArtRect box;
    }

    /// <summary>技能树面板版面表（**生成物**，见文件头）。</summary>
    public static class SkillTreeLayout
    {
        /// <summary>底图页尺寸（原版 px）。</summary>
        public const int PageW = 320;

        public const int PageH = 432;

        /// <summary>系个数（= 底图第 1/2/3 页）。</summary>
        public const int TreeCount = 3;

        /// <summary>节点框尺寸（原版 px；实测两处完整可见的格 = 45x50）。</summary>
        public const float CellW = 45f;

        public const float CellH = 50f;

        /// <summary>说明区**木框**的外沿 bbox（原版 px；页 0 顶部那个木框）。</summary>
        public static readonly SkillArtRect WoodFrame =
            new SkillArtRect(233f, 2f, 86f, 105f);

        /// <summary>木框金饰条宽度（原版 px）⇒ 可见区 = <see cref="WoodFrame"/> 内缩这么多。</summary>
        public const float WoodTrim = 4f;

        /// <summary>说明文字可见区 = 页 0 顶部木框里的**黑窗内区**（原版 px）。</summary>
        public static readonly SkillArtRect DescWindow =
            new SkillArtRect(255f, 61f, 43f, 23f);

        /// <summary>
        /// 3 个**系页签**的点击区（原版 px，索引 = **系 - 1**）。
        /// <para>实测：页签槽自上而下 = 系 3 / 系 2 / 系 1（依据 = 树区右边框缺口 +
        /// 底图上被点亮的那个页签，两条独立特征互证，见文件头）=> 这里按「系」排。</para>
        /// </summary>
        public static readonly SkillArtRect[] TabSlots =
        {
            // 系 1 <= 页签槽 #3（自上而下）
            new SkillArtRect(235f, 328f, 82f, 100f),
            // 系 2 <= 页签槽 #2（自上而下）
            new SkillArtRect(235f, 220f, 82f, 100f),
            // 系 3 <= 页签槽 #1（自上而下）
            new SkillArtRect(235f, 112f, 82f, 100f),
        };

        /// <summary>全部技能槽（150 条 = 5 职业 x 3 系 x 每系技能数）。</summary>
        public static readonly SkillTreeCell[] Cells =
        {
            new SkillTreeCell { skillId = 1, cls = 1, tree = 1, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 2, cls = 1, tree = 1, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 3, cls = 1, tree = 2, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 4, cls = 1, tree = 2, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 5, cls = 1, tree = 3, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 6, cls = 1, tree = 1, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 7, cls = 1, tree = 1, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 8, cls = 1, tree = 2, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 9, cls = 1, tree = 3, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 10, cls = 1, tree = 3, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 11, cls = 1, tree = 1, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 12, cls = 1, tree = 2, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 13, cls = 1, tree = 2, row = 3, col = 2,
                                box = new SkillArtRect(82f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 14, cls = 1, tree = 3, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 15, cls = 1, tree = 3, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 16, cls = 1, tree = 1, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 17, cls = 1, tree = 1, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 18, cls = 1, tree = 2, row = 4, col = 3,
                                box = new SkillArtRect(150f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 19, cls = 1, tree = 3, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 20, cls = 1, tree = 3, row = 4, col = 3,
                                box = new SkillArtRect(150f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 21, cls = 1, tree = 1, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 22, cls = 1, tree = 1, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 23, cls = 1, tree = 2, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 24, cls = 1, tree = 2, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 25, cls = 1, tree = 3, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 26, cls = 1, tree = 1, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 27, cls = 1, tree = 2, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 28, cls = 1, tree = 2, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 29, cls = 1, tree = 3, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 30, cls = 1, tree = 3, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 31, cls = 2, tree = 1, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 32, cls = 2, tree = 1, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 33, cls = 2, tree = 2, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 34, cls = 2, tree = 3, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 35, cls = 2, tree = 3, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 36, cls = 2, tree = 1, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 37, cls = 2, tree = 2, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 38, cls = 2, tree = 2, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 39, cls = 2, tree = 3, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 40, cls = 2, tree = 3, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 41, cls = 2, tree = 1, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 42, cls = 2, tree = 1, row = 3, col = 2,
                                box = new SkillArtRect(82f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 43, cls = 2, tree = 2, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 44, cls = 2, tree = 2, row = 3, col = 2,
                                box = new SkillArtRect(82f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 45, cls = 2, tree = 3, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 46, cls = 2, tree = 1, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 47, cls = 2, tree = 1, row = 4, col = 3,
                                box = new SkillArtRect(150f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 48, cls = 2, tree = 2, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 49, cls = 2, tree = 2, row = 4, col = 3,
                                box = new SkillArtRect(150f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 50, cls = 2, tree = 3, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 51, cls = 2, tree = 1, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 52, cls = 2, tree = 2, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 53, cls = 2, tree = 2, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 54, cls = 2, tree = 3, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 55, cls = 2, tree = 3, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 56, cls = 2, tree = 1, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 57, cls = 2, tree = 1, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 58, cls = 2, tree = 2, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 59, cls = 2, tree = 3, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 60, cls = 2, tree = 3, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 61, cls = 3, tree = 1, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 62, cls = 3, tree = 2, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 63, cls = 3, tree = 2, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 64, cls = 3, tree = 3, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 65, cls = 3, tree = 3, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 66, cls = 3, tree = 1, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 67, cls = 3, tree = 1, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 68, cls = 3, tree = 2, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 69, cls = 3, tree = 2, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 70, cls = 3, tree = 3, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 71, cls = 3, tree = 1, row = 3, col = 2,
                                box = new SkillArtRect(82f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 72, cls = 3, tree = 1, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 73, cls = 3, tree = 2, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 74, cls = 3, tree = 3, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 75, cls = 3, tree = 3, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 76, cls = 3, tree = 1, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 77, cls = 3, tree = 1, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 78, cls = 3, tree = 2, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 79, cls = 3, tree = 2, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 80, cls = 3, tree = 3, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 81, cls = 3, tree = 1, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 82, cls = 3, tree = 1, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 83, cls = 3, tree = 2, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 84, cls = 3, tree = 3, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 85, cls = 3, tree = 3, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 86, cls = 3, tree = 1, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 87, cls = 3, tree = 2, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 88, cls = 3, tree = 2, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 89, cls = 3, tree = 3, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 90, cls = 3, tree = 3, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 91, cls = 4, tree = 1, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 92, cls = 4, tree = 1, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 93, cls = 4, tree = 2, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 94, cls = 4, tree = 3, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 95, cls = 4, tree = 3, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 96, cls = 4, tree = 1, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 97, cls = 4, tree = 2, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 98, cls = 4, tree = 2, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 99, cls = 4, tree = 3, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 100, cls = 4, tree = 3, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 101, cls = 4, tree = 1, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 102, cls = 4, tree = 1, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 103, cls = 4, tree = 2, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 104, cls = 4, tree = 3, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 105, cls = 4, tree = 3, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 106, cls = 4, tree = 1, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 107, cls = 4, tree = 1, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 108, cls = 4, tree = 2, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 109, cls = 4, tree = 2, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 110, cls = 4, tree = 3, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 111, cls = 4, tree = 1, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 112, cls = 4, tree = 1, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 113, cls = 4, tree = 2, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 114, cls = 4, tree = 2, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 115, cls = 4, tree = 3, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 116, cls = 4, tree = 1, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 117, cls = 4, tree = 2, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 118, cls = 4, tree = 2, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 119, cls = 4, tree = 3, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 120, cls = 4, tree = 3, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 121, cls = 5, tree = 1, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 122, cls = 5, tree = 2, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 123, cls = 5, tree = 2, row = 1, col = 2,
                                box = new SkillArtRect(82f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 124, cls = 5, tree = 2, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 125, cls = 5, tree = 3, row = 1, col = 1,
                                box = new SkillArtRect(13f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 126, cls = 5, tree = 3, row = 1, col = 3,
                                box = new SkillArtRect(150f, 15f, 45f, 50f) },
            new SkillTreeCell { skillId = 127, cls = 5, tree = 1, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 128, cls = 5, tree = 1, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 129, cls = 5, tree = 2, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 130, cls = 5, tree = 2, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 131, cls = 5, tree = 2, row = 2, col = 3,
                                box = new SkillArtRect(150f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 132, cls = 5, tree = 3, row = 2, col = 1,
                                box = new SkillArtRect(13f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 133, cls = 5, tree = 3, row = 2, col = 2,
                                box = new SkillArtRect(82f, 82f, 45f, 50f) },
            new SkillTreeCell { skillId = 134, cls = 5, tree = 1, row = 3, col = 2,
                                box = new SkillArtRect(82f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 135, cls = 5, tree = 1, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 136, cls = 5, tree = 2, row = 3, col = 1,
                                box = new SkillArtRect(13f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 137, cls = 5, tree = 3, row = 3, col = 3,
                                box = new SkillArtRect(150f, 152f, 45f, 50f) },
            new SkillTreeCell { skillId = 138, cls = 5, tree = 1, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 139, cls = 5, tree = 1, row = 4, col = 2,
                                box = new SkillArtRect(82f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 140, cls = 5, tree = 2, row = 4, col = 3,
                                box = new SkillArtRect(150f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 141, cls = 5, tree = 3, row = 4, col = 1,
                                box = new SkillArtRect(13f, 220f, 45f, 50f) },
            new SkillTreeCell { skillId = 142, cls = 5, tree = 1, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 143, cls = 5, tree = 2, row = 5, col = 1,
                                box = new SkillArtRect(13f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 144, cls = 5, tree = 3, row = 5, col = 2,
                                box = new SkillArtRect(82f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 145, cls = 5, tree = 3, row = 5, col = 3,
                                box = new SkillArtRect(150f, 287f, 45f, 50f) },
            new SkillTreeCell { skillId = 146, cls = 5, tree = 1, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 147, cls = 5, tree = 1, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 148, cls = 5, tree = 2, row = 6, col = 3,
                                box = new SkillArtRect(150f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 149, cls = 5, tree = 3, row = 6, col = 1,
                                box = new SkillArtRect(13f, 356f, 45f, 50f) },
            new SkillTreeCell { skillId = 150, cls = 5, tree = 3, row = 6, col = 2,
                                box = new SkillArtRect(82f, 356f, 45f, 50f) },
        };

        /// <summary>按 `skillId` 取槽位；表里没有（配表被改过）=> 返回 false。</summary>
        public static bool TryGet(int skillId, out SkillTreeCell cell)
        {
            for (var i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].skillId == skillId)
                {
                    cell = Cells[i];
                    return true;
                }
            }
            cell = default(SkillTreeCell);
            return false;
        }

        /// <summary>某职业某个系（1..3）的槽位数。</summary>
        public static int CountOf(int cls, int tree)
        {
            var n = 0;
            for (var i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].cls == cls && Cells[i].tree == tree) n++;
            }
            return n;
        }
    }
}
