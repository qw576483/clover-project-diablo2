// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/UiLayoutGame.cs（agent-09 · 1:1 轮；2026 主 agent 裁决：改 ×1.8 居中）
// **游戏内面板（HUD / 背包 / 属性 / 技能 / 商店 / 怪物血条 / 关卡标题）的布局常量总表**。
//
// ★ 唯一依据（`docs/agents/agent-15-UI一対一复刻.md` §0）：
//   `原版资源/参考工程_Diablerie/Diablerie/Assets/Prefabs/{ControlPanel,InventoryPanel,CharstatPanel,
//   SkillPanel,SkillSlot,AvailableSkillsPanel,EnemyBar,LevelEntryTitle}.prefab`
//   里的 **RectTransform 精确值**（m_AnchorMin/Max、m_AnchoredPosition、m_SizeDelta、m_Pivot）。
//   这些值由脚本从 prefab YAML 里逐节点解析出来（不靠肉眼估），下面每个常量都标注
//   「原版值 → ×1.8（居中）」与来源节点名。
//
// ★★ 换算口径（**2026 主 agent 裁决，取代旧的 ×2.4**）：
//   原版暗黑2 是 **4:3（800×600）**，本工程画布是 **16:9（1920×1080）**。
//   ⛔ 旧口径用**宽度比** 1920/800 = **2.4** 等比缩放 —— 600×2.4 = **1440 > 1080**
//      ⇒ 原版 HUD 控制面板（原版底边就比画布底边低 21.3）被放大到出屏 51px（用户报「hud 都是不对的」）。
//   ✅ 正确口径（原版在宽屏下的实际做法）：**缩放系数 = 1080/600 = 1.8（按高度等比）**，
//      水平居中，左右多出的空间交给相机（视野更宽，不是把 UI 横向拉伸）。
//      · 水平：原版 `x ∈ [0,800]` → 屏幕 `960 + (x − 400) × 1.8`。本表所有 x 都写成
//        「相对屏幕中线的原版偏移 × K」，乘出来天然居中（±400 → ±720，画布 ±960 之内）。
//      · 垂直（底边锚定元素）：原版 y（相对画布底边）× 1.8 − 540。
//
// ★ HUD 贴底（消除旧 E5）：原版 `ControlPanel.prefab` 的 `Background` 是
//   `anchor(0.5,0)` + `pos.y = −21.3` + `pivot(0.5,0)` ⇒ 它的**底边比原版画布底边还低 21.3px**
//   （原版 800×600 下这 21.3px 是被裁掉的）。原版在宽屏下的控制面板底边与屏幕底边齐平 ⇒
//   本项目给整组 HUD 加一个**贴底抬升** `HudBaseLift = 21.3`（原版 px，×1.8 = 38.34 画布单位），
//   使控制面板底边正好落在画布底边（y = −540），**不再出屏**。见 <see cref="BottomY"/>。
//   （HUD 的所有元素都锚在画布底边 ⇒ 同一抬升，保留它们相互之间的原版相对几何。）
//
//   两种锚定方式分别换算（uGUI 语义，逐个算过、不是"看着差不多"）：
//     · **底边锚定**（HUD：prefab 里 anchorMin.y == 0）：走 <see cref="BottomY"/>。
//       例：ControlPanel 背景 pos.y=−21.3 + 抬高 21.3 ⇒ 底边贴底（画布 y = −540），
//       中心 y = 80×1.8 − 540 = **−396**。
//     · **居中锚定**（背包/属性/技能/商店：prefab 里 anchorMin == anchorMax == (0.5,0.5)）：
//       子节点的 `m_AnchoredPosition` 相对**父面板矩形中心** ⇒ 我们的坐标 = 原版值 × 1.8，
//       面板自身中心 = 原版面板中心 × 1.8（`InventoryPanel` 面板矩形占原版 x 0..320 ⇒
//       中心 (160,0) ⇒ 我们 (288,0)：**面板贴屏幕中线右侧**，与暗黑2 原版一致；
//       `CharstatPanel` 反之贴左侧）。
//
// ★ 原版贴图本身是 1:1 原版像素（`ControlPanel.png` 948×160、`inventory.png` 320×432、
//   `charstat.png` 320×432、`healthbar.png` 80×80…），所以 sizeDelta ×1.8 就是
//   「把原版画面**按高**放大到 1080 高、水平居中」，与 D2 支持 800×600 之外的宽屏分辨率时的做法一致。
//
// ⛔ 本文件只放**纯数据 + 纯函数**（无 GameObject、无 Unity 生命周期）⇒ `uicheck` 宿主
//    可以逐条离线断言「我们常量 == 原版值 × 1.8」（含 HUD 贴底）。
// ⛔ 本文件不属于 §A（流程面板）——那个 agent 只许动 `UI/UiLayoutFlow.cs`；
//    本文件是 §B（游戏内面板）的布局表，两边互不碰。
// ─────────────────────────────────────────────────────────────────────────────
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 游戏内面板的布局常量总表（原版 800×600 prefab 精确值 → 本工程 1920×1080 的 **×1.8 居中**）。
    /// </summary>
    internal static class UiLayoutGame
    {
        // ═════════════════════════════════════════════════════════════════════
        // 换算口径
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>原版 800×600 → 本工程 1920×1080 的缩放系数（**按高度等比**：1080/600 = 1.8）。</summary>
        public const float K = 1.8f;

        /// <summary>
        /// HUD 的**贴底抬升**（原版 px）：原版 `ControlPanel` 底边比画布底边低 21.3px（原版被裁），
        /// 本项目按「控制面板底边与屏幕底边齐平」把整组 HUD 抬这么多 ⇒ 底边不再出屏（旧 E5）。
        /// </summary>
        public const float HudBaseLift = 21.3f;

        /// <summary>
        /// ★ agent-27：HUD「技能格带**下方**那条空白大理石」的中心 y（原版 px）。
        /// <para>
        /// **为什么需要它**（实测，不是推断）：原版 `ControlPanel.prefab` 里
        /// `ImageExpBarLeft`(GO 1692291003419990，跑/走按钮的父容器) 与
        /// `ImageExpBarRight`(GO 1161153139115278，小面板开关的父容器) 的 **`m_IsActive: 0`**，
        /// 且参考工程 `Scenes/Game.unity` 对这两个对象**没有 active 覆盖** ⇒
        /// **原版 HUD 上根本看不到跑/走按钮与小面板开关**。
        /// 而按 prefab 原值摆（art y 21 / 26）时它们会**分别压在第 1、第 5 个技能格上**
        /// （`ControlPanel.png` 实测：技能格带 art y 86..115）—— 这正是用户报的
        /// 「下方技能栏图标摆放位置不对」。
        /// </para>
        /// <para>
        /// 本项目需要这两个鼠标入口（键盘 I/C/T/Q/Tab/Esc 只覆盖各面板，小面板 7 键要走这个开关），
        /// 故只把 **y** 挪到格带**下方的空白条**（实测该条里没有任何原版图元：经验条 art y 27..31、
        /// 技能格下沿 art y 115 ⇒ 按钮占 art y 2..26，两者都不碰），**x 仍取 prefab 原值**。
        /// 登记在 `策划/验收表.md` 的 E7。
        /// </para>
        /// </summary>
        public const float HudSubBarArtY = 14f;

        /// <summary>
        /// 上面那条空白条的本工程 y（**按底图像素**换算：底图底边 = 画布底边，与技能格/腰带格同一口径）。
        /// </summary>
        public const float HudSubBarY = HudSubBarArtY * K - UiArt.RefHeight * 0.5f;

        /// <summary>原版像素 → 本工程像素（位置与尺寸同系数；x 用它是**居中**的，因为原版 x 是相对屏幕中线的偏移）。</summary>
        public static float S(float origPx) => origPx * K;

        /// <summary>原版 (x,y) → 本工程 (x,y)。</summary>
        public static Vector2 S(float origX, float origY) => new Vector2(origX * K, origY * K);

        /// <summary>原版尺寸 → 本工程尺寸（等价于 <see cref="S(float,float)"/>，语义更清楚）。</summary>
        public static Vector2 Size(float origW, float origH) => new Vector2(origW * K, origH * K);

        /// <summary>
        /// **底边锚定**元素的原版 y → 我们的中心 y：
        /// 原版 y 是相对画布底边的距离 ⇒ 中心 y = (原版 y + <see cref="HudBaseLift"/>) × 1.8 − RefHeight/2。
        /// <para>`HudBaseLift` 让控制面板底边贴到画布底边（原版 <see cref="HudBgPos"/> 的底边正是画布底边）。</para>
        /// </summary>
        public static float BottomY(float origY) => (origY + HudBaseLift) * K - UiArt.RefHeight * 0.5f;

        /// <summary>
        /// HUD「子图元在其父图元内」的换算：父图元中心 = <paramref name="parentCenterOrig"/>（原版坐标），
        /// 子图元原版偏移 <paramref name="offsetOrig"/> ⇒ 我们的绝对中心。
        /// </summary>
        public static Vector2 BottomIn(Vector2 parentCenterOrig, Vector2 offsetOrig)
            => new Vector2((parentCenterOrig.x + offsetOrig.x) * K, BottomY(parentCenterOrig.y + offsetOrig.y));

        // ═════════════════════════════════════════════════════════════════════
        // ① HUD —— 原版 `ControlPanel.prefab`（锚点 (0.5,0)，即贴画布底边）
        //
        // ★ 唯一依据：`原版资源/参考工程_Diablerie/Diablerie/Assets/Prefabs/ControlPanel.prefab`
        //   （脚本逐节点解析 m_AnchoredPosition / m_SizeDelta / m_Pivot / m_AnchorMin/Max + m_IsActive，
        //    **非肉眼估**）。
        //   原版节点清单（含兄弟顺序 = 绘制顺序，后者盖前者）：
        //     1 Background          948×160     pivot(0.5,0) pos(0,-21.3)
        //     2 LeftSkill           33.495×35.12  pos(-229.9, 35.2)   子: Label 1
        //     3 RightSkill          33.495×35.12  pos(-192.2, 35.5)   子: Label 1
        //     4 Lifebulb            108×108     pos(-316.01, 66.96)  子: HealthBar/HealthBulbOverlay/HealthLabel
        //     5 Manabulb            108×108     pos(299.56, 66.96)   子: ManaAnimation/ManaBulbOverlay/ManaLabel
        //     6 ImageExpBarLeft     128×104     pos(-90, 3)   子: Button 16×20 pos(18,18) anchor(0,0.5) [act=0]
        //     7 ImageExpBarRight    128×104     pos(38, 3)    子: Button 15×24 pos(-37,23)             [act=0]
        //     8 ImageBeltRight      128×104     pos(166, 3)  （腰带框，底图已画格线）                 [act=0]
        //     9 ImageMinipanel      152×26      pos(0, 60)   + 7 子按钮 20×20，x=-63..63 步进 21，y=0 [act=0]
        //    10 ImageMinipanelArrowDown ×2  15×24 @(0,0)/≈0（**只当箭头 sprite 载体**，本身不显示）[act=0]
        //    11 ExperienceBar       486.94×4.06 pos(-8.9, 7.77) 子: Filler/TooltipArea
        //    12 ExpBarOverlay       948×160     pos(0, 59.1)  ← **最后一个兄弟 = 画在最上层**
        //
        // ★ 腰带 4 格（原版**没有独立节点**，格线画在 `ControlPanel.png` 底图里）：
        //   底图实测 4 个暗格 art x = 586..612 / 617..643 / 648..674 / 679..705 ⇒ 中心 599/630/661/692、
        //   **pitch 31**、**格内凹槽 27×25**、中心 art y ≈ 101；art x − 474(底图半宽) = 屏幕 x 125/156/187/218，
        //   art y 101 ⇒ 距底边 37.7。**在画布中线右侧**，正好被原版 `ImageBeltRight`(128×104 @ 166,3) 框住。
        //   ★ 2026 修正：格矩形取**格内凹槽 27×25**（不是 pitch/可见格高 31/29，见 `BeltCellSize`）。
        //
        // ★ 6 格技能栏（原版 `SkillPanel.prefab` 根 = 224.97×35.12 @ pos(-44.58, 35.76)，
        //   6 个子槽位由 LayoutGroup 摆 ⇒ prefab 里各子节点 pos/size 都是 0）：
        //   底图实测 6 个暗格 art x 中心 = 336/374/412/450/487.5/526（pitch 37.495 ≈ 224.97/6）。
        //   我们用「SkillPanel 根矩形 + 均分 6 份」，与原版根节点一致。
        //
        // ★ `ImageExpBarLeft/Right` 在原版 prefab 里是 `m_IsActive=0` 的容器，
        //   但它们的**子 Button 位置是对的** —— 两个按钮的 art y 分别 = 21 / 26，都在
        //   **格带（art y 47..73）下方的大理石条**上，不会压住任何技能格。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>控制面板底图尺寸（原版 948×160 → ×1.8 = 1706.4×288，**水平居中含在 1920 内**）。</summary>
        public static readonly Vector2 HudBgSize = Size(948f, 160f);

        /// <summary>
        /// 控制面板底图中心 y（原版 pivot(0.5,0) + pos(0,-21.3) + 贴底抬升 21.3 ⇒ 底边贴画布底边）。
        /// 中心 = `BottomY(-21.3+80)` = (58.7+21.3)×1.8 − 540 = 144 − 540 = **−396**
        /// （底边 = −396 − 144 = **−540 = 画布底边**，**不再出屏**）。
        /// </summary>
        public static readonly Vector2 HudBgPos = new Vector2(0f, BottomY(-21.3f + 80f));

        /// <summary>球直径（原版 Lifebulb/Manabulb 的 m_SizeDelta = 108×108 → ×1.8 = 194.4）。</summary>
        public const float OrbSize = 108f * K;

        /// <summary>生命球中心（原版 Lifebulb pos(-316.01, 66.96) → ×1.8 居中）。</summary>
        public static readonly Vector2 LifeOrbPos = new Vector2(-316.01f * K, BottomY(66.96f));

        /// <summary>法力球中心（原版 Manabulb pos(299.56, 66.96) → ×1.8 居中）。</summary>
        public static readonly Vector2 ManaOrbPos = new Vector2(299.56f * K, BottomY(66.96f));

        /// <summary>球内数字标签尺寸（原版 HealthLabel/ManaLabel：铺满球、高 8px、y 偏 4）。</summary>
        public static readonly Vector2 OrbLabelSize = Size(108f, 8f);

        /// <summary>球内数字标签相对球心的 y 偏移（原版 pos.y = 4 → ×1.8）。</summary>
        public const float OrbLabelOffsetY = 4f * K;

        /// <summary>技能格尺寸（原版 LeftSkill/RightSkill 的 m_SizeDelta = 33.495×35.12 → ×1.8）。</summary>
        public static readonly Vector2 SkillSlotSize = Size(33.495f, 35.12f);

        /// <summary>左键技能格中心（原版 LeftSkill pos(-229.9, 35.2) → ×1.8 居中）。</summary>
        public static readonly Vector2 LeftSkillPos = new Vector2(-229.9f * K, BottomY(35.2f));

        /// <summary>右键技能格中心（原版 RightSkill pos(-192.2, 35.5) → ×1.8 居中）。</summary>
        public static readonly Vector2 RightSkillPos = new Vector2(-192.2f * K, BottomY(35.5f));

        /// <summary>
        /// 技能格上热键标签的 **sizeDelta**（原版 `SkillSlot.prefab` 的 `HotkeyLabel`：
        /// anchorMin(0,0)/anchorMax(1,1) + sizeDelta(-2,3) ⇒ 比格子窄 2、高 3）。
        /// </summary>
        public static readonly Vector2 SkillLabelSizeDelta = S(-2f, 3f);

        /// <summary>技能格上热键标签的 **anchoredPosition**（原版 pos(2,3)、pivot(0,1) 左上对齐）。</summary>
        public static readonly Vector2 SkillLabelPos = S(2f, 3f);

        /// <summary>热键标签锚点（原版 Label 1 = anchorMin(0,0)/anchorMax(1,1) 铺满格子）。</summary>
        public static readonly Vector2 SkillLabelAnchorMin = Vector2.zero;
        public static readonly Vector2 SkillLabelAnchorMax = Vector2.one;
        public static readonly Vector2 SkillLabelPivot = new Vector2(0f, 1f);

        /// <summary>经验条**填充块**尺寸（原版 `Filler` sizeDelta(0,−1.44) ⇒ 高 4.06−1.44=2.62 → ×1.8）。</summary>
        public static readonly Vector2 ExpFillerSize = Size(486.94f, 4.06f - 1.44f);

        /// <summary>技能栏槽位数（原版 `SkillPanel.prefab` 根下有 6 个子槽位）。</summary>
        public const int SkillBarSlots = 6;

        /// <summary>技能栏根矩形尺寸（原版 `SkillPanel.prefab` 根 224.97×35.12 → ×1.8）。</summary>
        public static readonly Vector2 SkillBarSize = Size(224.97f, 35.12f);

        /// <summary>技能栏根矩形中心（原版 `SkillPanel.prefab` pos(-44.58, 35.76) → ×1.8 居中）。</summary>
        public static readonly Vector2 SkillBarCenter = new Vector2(-44.58f * K, BottomY(35.76f));

        /// <summary>技能栏槽位步进（原版 224.97 ÷ 6 = 37.495 → ×1.8）。</summary>
        public const float SkillBarStep = 224.97f / SkillBarSlots * K;

        /// <summary>技能栏第 <paramref name="i"/> 个槽位的中心 x（0 = 最左；`i` 越界自动夹取）。</summary>
        public static float SkillBarSlotX(int i)
        {
            var k = i < 0 ? 0 : (i >= SkillBarSlots ? SkillBarSlots - 1 : i);
            return SkillBarCenter.x + (k - (SkillBarSlots - 1) * 0.5f) * SkillBarStep;
        }

        /// <summary>小面板底图尺寸（原版 ImageMinipanel 152×26 → ×1.8）。</summary>
        public static readonly Vector2 MiniPanelSize = Size(152f, 26f);

        /// <summary>小面板底图中心 y（原版 pos(0,60)，走贴底抬升 ⇒ `BottomY(60)`）。</summary>
        public const float MiniPanelY = (60f + HudBaseLift) * K - UiArt.RefHeight * 0.5f;

        /// <summary>小面板 7 个按钮的 x（原版 ImageMinipanel 子节点实测：−63/−42/−21/0/21/42/63 → ×1.8）。</summary>
        public static readonly float[] MiniButtonX =
        {
            -63f * K, -42f * K, -21f * K, 0f, 21f * K, 42f * K, 63f * K,
        };

        /// <summary>小面板按钮尺寸（原版 20×20 → ×1.8 = 36）。</summary>
        public const float MiniButtonSize = 20f * K;

        /// <summary>
        /// 腰带格**边长**（**原版 px = 27** → ×1.8 = 48.6）。
        /// <para>
        /// ★ 2026 修正（**实测驱动**）：取的是 `ControlPanel.png` 底图上**画出来的格内凹槽宽**，
        /// 不是 pitch 31。实测（`scan` 工具逐行/逐列取亮线）：
        /// 竖分隔饰条在 art x **613..616 / 644..647 / 675..678 / 706** ⇒ 格内列 617..643、648..674、
        /// 679..705（**宽 27**）；上/下沿在 art y **86..88 / 114..116** ⇒ 格内行 89..113（**高 25**）。
        /// 旧的 `31×29`（= 相邻格 pitch / 可见格高）比原版格子**大 15%**，物品图标会压到格线、
        /// 甚至盖到相邻格上（用户报的「道具栏图标摆放位置不对」的成因之一；见 `Verify/A` 的叠加图）。
        /// </para>
        /// <para>4 格之间的**间隔**由 <see cref="BeltCellX"/> 的实测中心（pitch 31）表达 ⇒ 与底图凹槽逐格重合。</para>
        /// </summary>
        public const float BeltCellSize = 27f * K;

        /// <summary>腰带格高度（**原版底图格内凹槽高 25px** → ×1.8 = 45；旧值 29 是"可见格高"，过高）。</summary>
        public const float BeltCellH = 25f * K;

        /// <summary>
        /// 腰带 4 格中心的 x（原版底图实测 art x 中心 599.5/630.5/661.5/692.5，减 474 ⇒
        /// 屏幕 x 125.5/156.5/187.5/218.5；腰带在原版**画布中线右侧**，紧邻法力球）→ ×1.8 居中。
        /// </summary>
        public static readonly float[] BeltCellX =
        {
            125.5f * K, 156.5f * K, 187.5f * K, 218.5f * K,
        };

        /// <summary>腰带格中心 y（原版底图腰带格 art y 中心 101 ⇒ 屏幕 y 37.7；走贴底抬升 ⇒ `BottomY(37.7)`）。</summary>
        public const float BeltCellY = (37.7f + HudBaseLift) * K - UiArt.RefHeight * 0.5f;

        /// <summary>跑/走按钮尺寸（原版 `ImageExpBarLeft/Button` = 16×20 → ×1.8）。</summary>
        public static readonly Vector2 RunButtonSize = Size(16f, 20f);

        /// <summary>
        /// 跑/走按钮中心。原版父容器中心 art x=−90+474=**384**、y=3（贴底边）；子 Button 的 anchor 是
        /// `(0, 0.5)`（贴父**左沿**的竖直中点）+ pos(18,18) ⇒ art 中心 x = 320+18 = **338**、y = 21。
        /// <para>
        /// ⛔ **y 不用 prefab 的 21**：本体（`ImageExpBarLeft`）原版 `m_IsActive=0` ⇒ 原版看不到它，
        /// 而 y=21 会把按钮压在**第 1 个技能格**上（用户报的「图标摆放位置不对」）。
        /// 本项目移到格带下方的空白条，见 <see cref="HudSubBarArtY"/>；**x 仍照 prefab**。
        /// </para>
        /// </summary>
        public static readonly Vector2 RunButtonPos = new Vector2((338f - 474f) * K, HudSubBarY);

        /// <summary>经验条轨道尺寸（原版 ExperienceBar 486.94×4.06 → ×1.8）。</summary>
        public static readonly Vector2 ExpBarSize = Size(486.94f, 4.06f);

        /// <summary>经验条轨道中心（原版 pos(-8.9, 7.77) → ×1.8 居中）。</summary>
        public static readonly Vector2 ExpBarPos = new Vector2(-8.9f * K, BottomY(7.77f));

        /// <summary>经验条覆盖层中心（原版 ExpBarOverlay 948×160 @ pos(0,59.1) → ×1.8 居中）。</summary>
        public static readonly Vector2 ExpOverlayPos = new Vector2(0f, BottomY(59.1f));

        /// <summary>展开/收起小面板的箭头按钮尺寸（原版 `ImageExpBarRight/Button` = 15×24 → ×1.8）。</summary>
        public static readonly Vector2 MiniPanelArrowSize = Size(15f, 24f);

        /// <summary>
        /// 展开/收起小面板的箭头按钮中心。原版 `ImageExpBarRight` 中心 art x=38+474=**512**、y=3；
        /// 子 Button anchor `(0.5,0.5)` + pos(−37,23) ⇒ art 中心 x = **475**、y = 26。
        /// <para>
        /// ⛔ **y 不用 prefab 的 26**：本体（`ImageExpBarRight`）原版 `m_IsActive=0` ⇒ 原版看不到它，
        /// 而 y=26 会把箭头压在**第 5 个技能格**上（用户报的「图标摆放位置不对」）。
        /// 本项目移到格带下方的空白条，见 <see cref="HudSubBarArtY"/>；**x 仍照 prefab**。
        /// </para>
        /// </summary>
        public static readonly Vector2 MiniPanelArrowPos = new Vector2((475f - 474f) * K, HudSubBarY);

        // 小面板开关箭头的贴图路径不在这里 —— 资源路径的唯一来源是 `Core/ResPaths.cs`
        // （本文件只放布局数值，见 `HudPanel.MiniPanelArrowFrames`）。

        /// <summary>
        /// HUD 图元（矩形 + 名字 + 是否"底图容器"）——纯函数，供 `uicheck` 断言「两两不重叠」。
        /// <para>
        /// `Container = true` 的两项（整幅控制面板底图、小面板底图）**不参与两两判定**：
        /// 它们的子图元本来就画在它们上面（原版 `ControlPanel.prefab` 就是这么挂的），
        /// 拿它们去判"重叠"必然假阳性。
        /// </para>
        /// </summary>
        public static (string name, Rect rect, bool container)[] HudRects()
        {
            var list = new System.Collections.Generic.List<(string, Rect, bool)>();
            void Add(string n, Vector2 c, Vector2 s, bool container = false)
                => list.Add((n, new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y), container));

            Add("Background", HudBgPos, HudBgSize, true);
            Add("LifeOrb", LifeOrbPos, new Vector2(OrbSize, OrbSize));
            Add("ManaOrb", ManaOrbPos, new Vector2(OrbSize, OrbSize));
            Add("LeftSkill", LeftSkillPos, SkillSlotSize);
            Add("RightSkill", RightSkillPos, SkillSlotSize);
            for (var i = 0; i < SkillBarSlots; i++)
                Add("SkillBar" + i, new Vector2(SkillBarSlotX(i), SkillBarCenter.y), SkillSlotSize);
            Add("MiniPanel", new Vector2(0f, MiniPanelY), MiniPanelSize, true);
            for (var i = 0; i < MiniButtonX.Length; i++)
                Add("MiniBtn" + i, new Vector2(MiniButtonX[i], MiniPanelY), new Vector2(MiniButtonSize, MiniButtonSize));
            for (var i = 0; i < BeltCellX.Length; i++)
                Add("Belt" + i, new Vector2(BeltCellX[i], BeltCellY), new Vector2(BeltCellSize, BeltCellH));
            Add("RunButton", RunButtonPos, RunButtonSize);
            Add("MiniArrow", MiniPanelArrowPos, MiniPanelArrowSize);
            Add("ExpBar", ExpBarPos, ExpBarSize);
            return list.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ①b 商店（原版 buysell_back 320x432 —— 居中锚定 ⇒ 面板中心 = 画布中心）
        public static readonly Vector2 ShopSize = Size(320f, 432f);
        public static readonly Vector2 ShopPos = Vector2.zero;
        public const int ShopCols = 10;
        public const int ShopRows = 10;
        public const float ShopCell = 29f * K;
        public static readonly Vector2 ShopGridOrigin = new Vector2((14f - 160f) * K, (216f - 62f) * K);

        public static Vector2 ShopCellCenter(int col, int row)
            => new Vector2(ShopGridOrigin.x + (col + 0.5f) * ShopCell, ShopGridOrigin.y - (row + 0.5f) * ShopCell);

        public static readonly Vector2 ShopInfoBarPos = new Vector2((108.5f - 160f) * K, (216f - 367f) * K);
        public static readonly Vector2 ShopInfoBarSize = Size(183f, 20f);

        public static readonly float[] ShopBottomSlotX = { -28f * K, 24f * K, 76f * K, 128f * K };
        public const float ShopBottomSlotY = (216f - 381f) * K;

        /// <summary>
        /// 底部 4 个雕槽里**按钮的边长** = 底图雕槽内高 28 ×1.8。
        /// ★ agent-23：旧实现底部两个按钮用 `ShopActionY / ShopActionX0 / ShopActionX1`
        /// （77×17 的 `tradebtn` 摆在 原版 y=414 的**空白大理石**上）—— 底图在那里**没有雕槽**
        /// （实测：底图底部只有左下 188×16 名牌 + 右下 4 个 34×27 方槽）⇒ 那三个常量已删除，
        /// 底部按钮改放雕槽里、用方钮艺术。见 `UI/ShopPanel.BuildBottomBar` 的依据说明。
        /// </summary>
        public const float ShopBottomSlotSize = 28f * K;

        public static readonly float[] ShopTabX = { -120f * K, -40f * K, 40f * K, 120f * K };
        public const float ShopTabY = (216f - 14f) * K;
        public static readonly Vector2 ShopTabSize = Size(79f, 31f);   // —— 原版 `buysell_back.png`（320×432 → ×1.8 = 576×777.6）

        // ── ★ R1-E 的 S5：商店「标题行 / 提示行」落位 ───────────────────────────
        // 为什么在这个空带：底图 `buysell_back.png` 从上到下是 页签带（原版 y 0..28）→ **空白大理石**
        // → 10×10 格区（顶沿原版 y 62）→ 底部名牌/雕槽。两带之间那段（原版 y ≈ 29..62 = 58.5 画布px）
        // 底图上**没有任何图元**（逐行扫过：无金线、无雕槽）⇒ 这是底图里唯一能放文字的空带。
        // 两行各 25 画布px（共 50）+ 行距 5 = 55 ≤ 58.5，两行都落在空带内、与页签带 / 格区都不相交
        // （`uicheck` 的 ④-2 断言逐条核对）。文案口径见 `UI/ShopPanel.ApplyTitle`。
        /// <summary>页签带的**底沿**（画布 y）= 页签行心 − 半高。</summary>
        public static readonly float ShopTabBottomY = ShopTabY - ShopTabSize.y * 0.5f;

        /// <summary>10×10 格区的**顶沿**（画布 y）= <see cref="ShopGridOrigin"/> 的 y。</summary>
        public static readonly float ShopGridTopY = ShopGridOrigin.y;

        /// <summary>标题行 / 提示行的行高（画布px）= 25。</summary>
        public const float ShopInfoLineH = 25f;

        /// <summary>标题行 / 提示行的宽度（画布px）= 原版 288 ×1.8 = 518.4（面板宽 576，左右各余 28.8）。</summary>
        public static readonly Vector2 ShopInfoLineSize = new Vector2(288f * K, ShopInfoLineH);

        /// <summary>商店**标题行**（NPC 名）中心（画布）= 页签带与格区之间空带的上行。</summary>
        public static readonly Vector2 ShopTitlePos =
            new Vector2(0f, (ShopTabBottomY + ShopGridTopY) * 0.5f + 15f);

        /// <summary>商店**提示行**（买入 / 卖出）中心（画布）= 同一空带的下行。</summary>
        public static readonly Vector2 ShopHintPos =
            new Vector2(0f, (ShopTabBottomY + ShopGridTopY) * 0.5f - 15f);
        //
        // 原版节点实测：
        //   Panel(320×432, pivot(0.0,0.5), pos(0,0)) ⇒ 面板矩形占原版 x 0..320（**贴屏幕中线右侧**）
        //   Grid(287.8×114.9, pos(-140.6,-153.2), pivot(0,0)) ⇒ 10×4 格区
        //   head(54×54 @ (2.9,184.2))        neck(24.15×24.3 @ (60.425,171))
        //   tors(54×83 @ (3.4,99.1))         rarm(54.93×108.7 @ (-112.21,112.9))
        //   larm(54×109.6 @ (119.2,112.4))   glov(54.9×54.2 @ (-112.2,10.287))
        //   belt(52×24 @ (3.4,26.5))         rrin(23.7×24.125 @ (-53.4,25.538))
        //   lrin(24.1×25 @ (60.4,25.975))    feet(53.9×55.3 @ (119.15,10.825))
        //   GoldButton(20×17 @ (-65.5,-184.1))  CloseButton(32×31 @ (-125.8,-184.1))
        //   GoldText(87.1×15.2 @ (-7,-183.2))
        // ⚠️ 格子步进取**底图实测**（`inventory.png` 格线 x=17,46,…,309 ⇒ 格宽 29.2；
        //    y=252,281,310,339,369 ⇒ 格高 29.25）—— 格子和底图画出来的框必须对齐，
        //    这是"看得见"的判据；prefab 的 Grid 框(287.8×114.9) 是外框，与格线差 1.5%。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>背包面板尺寸（原版 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 InvPanelSize = Size(320f, 432f);

        /// <summary>背包面板中心（原版矩形 x 0..320、y −216..216 ⇒ 中心 (160,0) → ×1.8 = (288,0)）。</summary>
        public static readonly Vector2 InvPanelPos = new Vector2(160f * K, 0f);

        /// <summary>背包格宽（底图实测 29.2 → ×1.8）。</summary>
        public const float InvCellW = 29.2f * K;

        /// <summary>背包格高（底图实测 29.25 → ×1.8）。</summary>
        public const float InvCellH = 29.25f * K;

        /// <summary>背包格区**左上角**（面板中心坐标；底图实测 (17,252) ⇒ (17−160, 216−252) = (−143,−36) → ×1.8）。</summary>
        public static readonly Vector2 InvGridOrigin = S(-143f, -36f);

        /// <summary>
        /// 10 个装备槽（**原版 prefab 的节点矩形逐一 ×1.8**；`pos` 相对面板矩形中心，与 prefab 同口径）。
        /// `half` 语义同 <see cref="InventoryPanel.EquipSlotDef.half"/>：0 = 整幅、1 = 左半、2 = 右半
        /// （`inv_helm_glove` / `inv_ring_amulet` 是双槽拼图，贴图本身不可切分 ⇒ 用 `RectMask2D` 取半）。
        /// <para>
        /// ★ 1:1 轮修正（**看原版图得到的，不是推的**）：原版 `inv_helm_glove.png`(128×64)
        /// **左半 = 手套、右半 = 头盔**；`inv_ring_amulet.png`(64×32) **左半 = 项链、右半 = 戒指**。
        /// </para>
        /// </summary>
        public static readonly (string node, string file, int half, Vector2 center, Vector2 size)[]
            InvEquipOrig =
        {
            ("rarm", "inv_weapons",     0, new Vector2(-112.21f, 112.9f),    new Vector2(54.93f, 108.7f)),
            ("head", "inv_helm_glove",  2, new Vector2(2.899994f, 184.2f),   new Vector2(54f, 54f)),
            ("neck", "inv_ring_amulet", 1, new Vector2(60.42502f, 171.0f),   new Vector2(24.15f, 24.3f)),
            ("larm", "inv_weapons",     0, new Vector2(119.20001f, 112.40004f), new Vector2(54f, 109.6f)),
            ("tors", "inv_armor",       0, new Vector2(3.399994f, 99.1f),    new Vector2(54f, 83f)),
            ("glov", "inv_helm_glove",  1, new Vector2(-112.2f, 10.287f),    new Vector2(54.9f, 54.2f)),
            ("rrin", "inv_ring_amulet", 2, new Vector2(-53.4f, 25.538f),     new Vector2(23.7f, 24.125f)),
            ("belt", "inv_belt",        0, new Vector2(3.399994f, 26.5f),    new Vector2(52f, 24f)),
            ("lrin", "inv_ring_amulet", 2, new Vector2(60.4f, 25.975004f),   new Vector2(24.1f, 25f)),
            ("feet", "inv_boots",       0, new Vector2(119.150024f, 10.825001f), new Vector2(53.9f, 55.3f)),
        };

        /// <summary>背包底部「金币按钮」矩形（原版 GoldButton 20×17 @ (-65.5,-184.1) → ×1.8）。</summary>
        public static readonly Vector2 InvGoldButtonPos = S(-65.5f, -184.1f);
        public static readonly Vector2 InvGoldButtonSize = Size(20f, 17f);

        /// <summary>背包底部「关闭按钮」矩形（原版 CloseButton 32×31 @ (-125.8,-184.1) → ×1.8）。</summary>
        public static readonly Vector2 InvCloseButtonPos = S(-125.8f, -184.1f);
        public static readonly Vector2 InvCloseButtonSize = Size(32f, 31f);

        /// <summary>背包底部「金币数字」矩形（原版 GoldText 87.1×15.2 @ (-7,-183.2) → ×1.8）。</summary>
        public static readonly Vector2 InvGoldTextPos = S(-7f, -183.2f);
        public static readonly Vector2 InvGoldTextSize = Size(87.1f, 15.2f);

        // ═════════════════════════════════════════════════════════════════════
        // ③ 人物属性 —— 原版 `CharstatPanel.prefab`
        //
        // 原版节点实测（全部相对 Panel 矩形中心；Panel 320×432 pivot(1.0,0.5) @ (0,0)
        // ⇒ 面板矩形占原版 x −320..0，即**贴屏幕中线左侧**，与暗黑2 原版一致）：
        //   CharName(-63.1, 193.2) 172.1×27.8
        //   StrengthLabel(-115.4, 119.1)  DexLabel(-115.4, 57.0)
        //   VitalityLabel(-115.4,-28.4)   EnergyLabel(-115.4,-90.5)   ← 每行 74.3×27.9
        //   DefenseLabel(56.7, 9.5) 109.1×28.3
        //   StaminaLabel(38.5,-28.6) LifeLabel(38.5,-52.8) ManaLabel(38.5,-90.8) ← 每行 74.3×27.9
        //   CloseButton(-15.4,-188.3) 32×31
        // ⚠️ 原版 prefab **没有**「等级/经验/命中/格挡/四系抗性」这 8 行的节点（原版把这些画在
        //    别的屏/别的节点上）—— 本项目 DTO 有这些字段，故按**原版右上空框与右下空框**补齐，
        //    尺寸沿用同一套行度量，逐行标注「本项目新增」（见属性表）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>属性面板尺寸（原版 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 CharPanelSize = Size(320f, 432f);

        /// <summary>属性面板中心（原版矩形 x −320..0、y −216..216 ⇒ 中心 (−160,0) → ×1.8 = (−288,0)）。</summary>
        public static readonly Vector2 CharPanelPos = new Vector2(-160f * K, 0f);

        /// <summary>角色名标签（原版 CharName → ×1.8）。</summary>
        public static readonly Vector2 CharNamePos = S(-63.1f, 193.2f);
        public static readonly Vector2 CharNameSize = Size(172.1f, 27.8f);

        /// <summary>四维行中心（原版 Strength/Dex/Vitality/Energy 的 Label 矩形；顺序 力量/敏捷/体力/精力）。</summary>
        public static readonly Vector2[] CharStatRowOrig =
        {
            new Vector2(-115.4f, 119.1f), new Vector2(-115.4f, 57.0f),
            new Vector2(-115.4f, -28.4f), new Vector2(-115.4f, -90.5f),
        };

        /// <summary>四维行的标签矩形尺寸（原版 74.3×27.9 → ×1.8）。</summary>
        public static readonly Vector2 CharStatRowSize = Size(74.3f, 27.9f);

        /// <summary>加点箭头中心**相对该行中心**的 x 偏移（= 原版三角槽实测 offset 75.4 art px → ×1.8）。</summary>
        public const float CharPlusX = 75.4f * K;

        /// <summary>加点箭头尺寸（原版 `menubutton__0__0.png` = 15×24 → ×1.8）。</summary>
        public static readonly Vector2 CharPlusSize = Size(15f, 24f);

        /// <summary>右侧派生行（原版 Defense/Stamina/Life/Mana 的 Label 矩形，顺序一致）。</summary>
        public static readonly Vector2[] CharDerivedRowOrig =
        {
            new Vector2(56.7f, 9.5f), new Vector2(38.5f, -28.6f),
            new Vector2(38.5f, -52.8f), new Vector2(38.5f, -90.8f),
        };

        /// <summary>右侧派生行矩形尺寸（原版 Defense 109.1×28.3 / 其余 74.3×27.9 → ×1.8）。</summary>
        public static readonly Vector2 CharDefenseSize = Size(109.1f, 28.3f);
        public static readonly Vector2 CharDerivedSize = Size(74.3f, 27.9f);

        /// <summary>关闭按钮（原版 CloseButton → ×1.8）。</summary>
        public static readonly Vector2 CharClosePos = S(-15.4f, -188.3f);
        public static readonly Vector2 CharCloseSize = Size(32f, 31f);

        /// <summary>
        /// 右上空框（原版 `charstat.png` 顶部右侧那个与 CharName 同高的空框，
        /// 底图实测 art x 165..315、y 8..40 ⇒ 面板中心坐标 (80,191)、尺寸 150×26 → ×1.8）
        /// → 本项目放「等级 / 经验」。
        /// </summary>
        public static readonly Vector2 CharTopRightPos = S(80f, 191f);
        public static readonly Vector2 CharTopRightSize = Size(150f, 26f);

        /// <summary>
        /// 右下两个细长空框（底图实测 art x 180..310、y 403..415 / 418..430
        /// ⇒ 面板中心坐标 (85,−193) / (85,−208)、尺寸 130×12 → ×1.8）→ 本项目放「命中 / 格挡」。
        /// </summary>
        public static readonly Vector2[] CharBottomRightOrig =
        {
            new Vector2(85f, -193f), new Vector2(85f, -208f),
        };

        public static readonly Vector2 CharBottomRightSize = Size(130f, 12f);

        /// <summary>
        /// 左下空白区（原版大理石底纹，无凹槽；art y 322/346/370/394）→ 本项目放四系抗性。
        /// <para>
        /// 行高取 **20**（不是四维行的 27.9）：能量行的下沿在 −104.45、面板下沿在 −216，
        /// 中间只有 111.5px，4 行 27.9 高放不下（会越出面板）⇒ 用 20 高 + 24 行距。
        /// </para>
        /// </summary>
        public static readonly Vector2[] CharResistRowOrig =
        {
            new Vector2(-115.4f, -128f), new Vector2(-115.4f, -152f),
            new Vector2(-115.4f, -176f), new Vector2(-115.4f, -200f),
        };

        /// <summary>四系抗性行尺寸（原版行宽 74.3，高取 20 → ×1.8）。</summary>
        public static readonly Vector2 CharResistRowSize = Size(74.3f, 20f);

        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // ④ 技能树 —— 原版 `SPELLS/skltree_{a,b,n,p,s}_back.DC6`
        //    （tile 拼装 = 320×432 一"页"×4 页；页 0 = 共用右列，页 1/2/3 = 系 1/2/3）
        //
        // ★★ 本片（「技能树面板 1:1 重做」）改口径：**版面全部来自原版像素**，
        //    不再由"原版图标尺寸"反推；上一版的 `SkillNodeSize` / `SkillTreeColumnX` /
        //    `SkillNodeX` / `SkillNodeY` / `SkillNodeStepY` / `SkillPanelW` / `SkillPanelH` /
        //    `SkillLinkW` / `SkillNameH` / `SkillHeaderH` / `SkillDescH` / `SkillRows` /
        //    `SkillCols` / `SkillTreeBand` **全部删除**（那些都是自绘口径，原版底图里都有）。
        //
        // 出处（三份载体，逐项对应）：
        //   · 底图页：`D2/UI/Panel/skltree_{cls}_back_{0..3}.png`（320×432，原版像素未改）
        //   · 版面表：`Def/SkillTreeLayout.cs`（**生成物**，来自
        //     `tools/d2codec/export_skilltree_layout.py` 对上面那批 PNG 的逐像素解析）
        //   · 页↔系：底图第 k 页 ↔ 原版 `skilldesc.txt` 的 `SkillPage = k`
        //     （两条独立像素特征互证，15/15 页一致 —— 见生成器文件头）
        //
        // 面板 = 320×432 原版 px ×K(=1.8) = **576 × 777.6**，居中（与任务日志面板同口径）。
        // 技能图标 = 原版底图**框的内径**（框线实测 2px ⇒ 四周内缩 `SkillCellLine`）；
        //   原版位图 48×48 由 `preserveAspect` 等比落进去（不拉变形、不放大）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>技能树底图一页的尺寸（原版 px → 画布）= 320×432 ×1.8 = 576×777.6。</summary>
        public static readonly Vector2 SkillPanelSize =
            Size(SkillTreeLayout.PageW, SkillTreeLayout.PageH);

        /// <summary>技能树面板位置（画布居中）。</summary>
        public static readonly Vector2 SkillPanelPos = Vector2.zero;

        /// <summary>原版底图的框线宽（实测 2px）——「框的内径」= 框外接矩形四周内缩这么多。</summary>
        public const float SkillCellLine = 2f;

        /// <summary>
        /// 技能图标层尺寸（原版 px → 画布）= 节点框**内径**
        /// （45−2×2 = 41 × 50−2×2 = 46 原版 px ⇒ 73.8 × 82.8 画布 px）。
        /// </summary>
        public static readonly Vector2 SkillIconCell = Size(
            SkillTreeLayout.CellW - 2f * SkillCellLine,
            SkillTreeLayout.CellH - 2f * SkillCellLine);

        /// <summary>原版 px 点 → 面板局部坐标（原点 = 页左上角 ⇒ 面板中心；y 轴翻转）。</summary>
        public static Vector2 SkillArtToPanel(float artX, float artY)
            => new Vector2((artX - SkillTreeLayout.PageW * 0.5f) * K,
                           (SkillTreeLayout.PageH * 0.5f - artY) * K);

        /// <summary>原版 px 尺寸 → 画布尺寸。</summary>
        public static Vector2 SkillArtSize(float artW, float artH) => S(artW, artH);

        /// <summary>技能树「共用右列（顶部木框说明区 + 3 个系页签）」的页码。</summary>
        public const int SkillTabPage = 0;

        /// <summary>
        /// 说明区**可见区**（原版 px）= 页 0 顶部木框外沿内缩金饰条宽（实测 4px）
        /// ⇒ 木框可见区 x 237..314 / y 6..102（78×97 原版 px = 140.4×174.6 画布 px）。
        /// <para>依据：`skills` 页签/正文都在原版这一块里画；本项目把「剩余技能点」与
        /// 所点技能的「名 / 等级 / 说明」都放这里 —— 面板上再无自绘边框（本片禁写）。</para>
        /// </summary>
        public static readonly SkillArtRect SkillInfoBox = new SkillArtRect(
            SkillTreeLayout.WoodFrame.x + SkillTreeLayout.WoodTrim,
            SkillTreeLayout.WoodFrame.y + SkillTreeLayout.WoodTrim,
            SkillTreeLayout.WoodFrame.w - 2f * SkillTreeLayout.WoodTrim,
            SkillTreeLayout.WoodFrame.h - 2f * SkillTreeLayout.WoodTrim);

        /// <summary>
        /// 说明区里「剩余技能点」那一行的高度（画布 px）。
        /// <para>取 12 原版 px ×1.8 = 21.6（≈ 原版黑窗高 23 的一半）—— 是**面板排版量**，
        /// 不是原版度量（原版这一行画什么、画在哪，底图里没有证据，见回报的差异登记）。</para>
        /// </summary>
        public const float SkillPointsLineH = 12f * K;

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 怪物血条 / 关卡标题 —— 原版 `EnemyBar.prefab` / `LevelEntryTitle.prefab`
        //    （**屏幕空间 UI**：顶部锚定）
        //
        //   EnemyBar:        根 150×20, anchor(0.5,1), pos(0,-22), pivot(0.5,1)
        //                    子 Title: anchor(0.5,0)-(0.5,1), pos(0,2), size(200,-4)
        //   LevelEntryTitle: 根 fullWidth×300, anchor(0,1)-(1,1), pos(0,0), pivot(0.5,1)
        // ⚠️ 本工程画「怪头顶血条」的是 `Module/View`（世界空间），**不在 UI/ 层** ⇒ 这里给出
        //    原版矩形（纯函数，`uicheck` 逐条断言），由主 agent 派 View 侧 agent 消费。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>怪物血条尺寸（原版 150×20 → ×1.8 = 270×36）。</summary>
        public static readonly Vector2 EnemyBarSize = Size(150f, 20f);

        /// <summary>怪物血条中心（原版顶部居中、上沿距顶 22px → ×1.8）。</summary>
        public static readonly Vector2 EnemyBarPos =
            new Vector2(0f, UiArt.RefHeight * 0.5f - 22f * K - EnemyBarSize.y * 0.5f);

        /// <summary>怪物血条内标题的 y 偏移（原版 pos.y = 2）与高度收缩（原版 sizeDelta.y = -4），均 ×1.8。</summary>
        public const float EnemyBarTitleOffsetY = 2f * K;
        public const float EnemyBarTitleShrinkY = 4f * K;

        /// <summary>关卡进入标题：满宽 × 原版 300px 高、贴顶（原版 LevelEntryTitle）→ ×1.8 = 540 高。</summary>
        public const float LevelTitleH = LevelTitleOrigH * K;

        /// <summary>
        /// 关卡标题的**原版高**（= 原版 `LevelEntryTitle.prefab` 的 `m_SizeDelta.y = 300`）。
        /// <para>`UI/LevelEntryTitle.cs` 用位图字体（font30）排版，而位图字模是
        /// **按原版 px** 切的 ⇒ 标签内层要用**原版尺寸**建、再整体 ×K。所以这里把原版值单独拿出来。</para>
        /// </summary>
        public const float LevelTitleOrigH = 300f;

        /// <summary>
        /// 关卡标题的**原版尺寸**（满宽 × 300 原版px）——位图字体排版用（内层标签的 sizeDelta）。
        /// <para>满宽 = `UiArt.RefWidth / K` = 1920 / 1.8 = **1066.67**（= 16:9 画布在 800×600 基准下的等效宽；
        /// 原版是「铺满父层」的 anchor(0,1)-(1,1) ⇒ 我们铺满 1920 画布，等效原版宽就是 1066.67，不另写魔数）。</para>
        /// </summary>
        public static readonly Vector2 LevelTitleOrigSize = new Vector2(UiArt.RefWidth / K, LevelTitleOrigH);

        /// <summary>
        /// 关卡标题的**画布中心**：原版贴顶 + 高 300 且文字 MiddleCenter
        /// ⇒ 文字中心 = 顶边往下 150 原版px ⇒ ×1.8 = 270 ⇒ 画布 y = 540 − 270 = **+270**。
        /// </summary>
        public static readonly Vector2 LevelTitlePos = new Vector2(0f, UiArt.RefHeight * 0.5f - LevelTitleH * 0.5f);

        /// <summary>怪物血条矩形（纯函数；左上角 + 尺寸）。</summary>
        public static Rect EnemyBarRect()
            => new Rect(EnemyBarPos.x - EnemyBarSize.x * 0.5f, EnemyBarPos.y - EnemyBarSize.y * 0.5f,
                EnemyBarSize.x, EnemyBarSize.y);

        /// <summary>关卡标题矩形（纯函数）。</summary>
        public static Rect LevelTitleRect()
            => new Rect(-UiArt.RefWidth * 0.5f, UiArt.RefHeight * 0.5f - LevelTitleH,
                UiArt.RefWidth, LevelTitleH);

        // ═════════════════════════════════════════════════════════════════════
        // ⑥ 任务日志（原版 Q）—— **唯一依据 = 原版底图 + 原版 DC6 控件真身 + 原版串表**
        //
        // ⚠️ 为什么这里没有 prefab 依据：参考工程 `Diablerie/Assets/Prefabs/` 只有
        //    ControlPanel / InventoryPanel / CharstatPanel / SkillPanel / SkillSlot /
        //    AvailableSkillsPanel / EnemyBar / LevelEntryTitle / GameManager / CommandPrompt /
        //    Camera / Menu*，**没有任务面板 prefab**（也没有买卖屏 prefab）。
        //    ⇒ 版面只能从 `MENU/questbackground.dc6`（→ `Panel/quest_back.png`，320×432）**实测分区**得出。
        //
        // ★ 分区怎么来的（**不是估的**，2026「任务框」轮用 `.ai-tmp/test/quest/scan_bg.py`
        //    重跑了一遍，口径与读图结果都留在 `策划/自审对比/UI对照.md`）：逐行扫 `quest_back.png`
        //    的"金像素"（判定：a>40 且 r>90 且 r−b>25），满宽金线出现在
        //      y = 0 / 28 / 230 / 252 / 383 / 430；左/右金边在 x = 0 / 318。
        //    ⇒ 五个横带：
        //      · y   0.. 28 → **章节页签行**（黑底）。依据：`questtab_*.png` 实测 78×30，
        //                     4 个页签 × 78 = **312 ≈ 320**（同构证据：`waygatetabs.dc6` 也是 78×30 ×8、
        //                     商店 `buyselltabs` 79×31 ×4，三者底图顶部都是 y 0..28 的同一条黑带）
        //                     且 `questtab_0/2/4/6` 的位图里逐字是罗马数字 **I / II / III / IV**。
        //      · y  30..230 → **任务格区**（石纹区，实测满宽金线 y=28/230 ⇒ 净高 200）。
        //                     `questsocket_*.png` 实测 80×95：
        //                     2 行 × 95 = 190，在 200 里上下各余 5 ⇒ 行心 82.5 / 177.5；
        //                     3 列 × 80 = 240 居中于 320 ⇒ 列心 80 / 160 / 240（暗黑2 每章 6 个任务）。
        //      · y 230..252 → 缠结纹分隔条（原版装饰，**无控件**，本项目不放东西）。
        //      · y 252..383 → **任务正文区**（纯黑 + 金框；实测黑芯 x 2..317 / y 254..382
        //                     ⇒ 316×129 原版px）。
        //      · y 383..430 → 底部大理石条（右下 2 个方槽，实测近黑内芯 x 229..257 / 281..309、
        //                     y 391..418 ⇒ ≈29×28；与 `MENU/questlast.dc6` 的 30×30 ×2 帧同量级）。
        //                     ⚠️ 这两格原版到底放什么（哪个控件/什么行为）在素材与参考工程里**查不到出处**
        //                     ⇒ 本项目**保持底图原样**（不发明按钮），登记在 `UI对照.md` 的 BLOCKED。
        //
        // ★ 6 个任务格画什么（**本片定案，出处 = 原版任务图文件名 ↔ 原版串表键名同构**）：
        //   · 原版 `MENU/a{章}q{章内序号}.dc6` 共 **21 个文件**（`a1q1..a4q3`），
        //     而原版串表里的任务条目键正是 **`qstsa{章}q{序号}…`**：Act I 6 + Act II 6 + Act III 6
        //     + **Act IV 3** = **21** ⇒ 文件数、编号规则**逐一对上**（Act IV 只有 3 个任务
        //     ⇒ `a4q4..` 不存在，串表也只有 `qstsa4q1..q3`）。
        //   · 实证：`a1q2_0` 的图里是**一只红乌鸦**，而 `qstsa1q2` = 「修女埋骨之地」
        //     （= 血鸟 Blood Raven 那个任务）⇒ 归属不是猜的。
        //   · 本项目只做 Act I 主线 1 ⇒ **`a1q1`**（对应 `qstsa1q1` = 「邪惡洞穴」）。
        //   · 每帧 **72×86**；石龛 80×95 ⇒ (80−72)/2 = 4、(95−86)/2 = **4.5** ⇒ 任务图在石龛里**居中**
        //     ⇒ <see cref="QuestArtPos"/> = (0,0)（相对石龛中心）。
        //   · 完成态 = 原版 `MENU/questdone.dc6`（**21 帧 72×86**，与任务图**不透明形状逐像素相同**
        //     —— 实测 alpha 掩码差异 = **0 像素**，只是配色为灰）。
        //   · ⛔ `MENU/questicons.dc6`（50×52 ×3）**本片起不再使用**：实测它的最佳匹配位置就是
        //     `a1q1` 里 (9,28) 的那块"徽记板"区域（差 25.3%）⇒ 它是任务图内部那一块的裁剪，
        //     而任务图**本来就把每个任务自己的图形画在里面**（`a1q2` = 红乌鸦）⇒ 再叠一层
        //     `questicon` 只会把任务自己的图形盖掉。登记见 `UI对照.md` §⑥。
        //   · 石龛两帧 `questsocket_0/_1`：实测**不透明形状逐像素完全相同（alpha 差异 = 0 像素）**、
        //     只有边框材质不同（银灰 / 金）⇒ 「常态 / 高亮」一对（<see cref="QuestSlotSize"/>）。
        //
        // ★ 标题条 `Banner/quests_0.png`（74×54，位图逐字是「任務」）放哪：页签行（0..30）已被
        //   4 个页签占满、石纹区（30..230）要留给 3×2 格网（190/200）、正文区（252..383）是正文
        //   ⇒ 标题条**只能落在面板之外**。本表按"贴面板顶沿、居中、整条在面板上方"摆。
        //   ⚠️ 原版**没有这条的坐标出处** ⇒ 登记 `策划/验收表.md` 的 **E16（BLOCKED 部分）**。
        //
        // ★ 正文区（黑芯）的**行距口径**（出处可查）：原版文本是**运行时文字**，但 libd2 里
        //   有原版排版函数的逐行移植 —— `packages/formats/src/font.zig`：
        //     · `:148-153` `D2WINFONT_DrawWideString` 把行 `y` 当**基线**（字形在基线上方）；
        //     · `:239-259` `drawWrapped(... line_height)` 每行 `baseline += line_height`，**左对齐**；
        //     · `:141-146` `lineHeight()` = 该字体最高字形 ⇒ 行距 = **原版字模行高**（不是任意值）；
        //     · `:188-216` `D2WINTEXTBOX_WordWrapAndSetText @0x4fcda0` = 换行规则（超宽即断、在最后
        //       一个空格断、断点空格不进下一行）。
        //   ⇒ 本表不再给"5 个魔数行位置"，正文交给**一个文本框**，行距由 `UI/D2Text` 按原版字模算。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>任务日志面板尺寸（原版 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 QuestPanelSize = Size(320f, 432f);

        /// <summary>任务日志面板中心（原版无 prefab ⇒ 按"800×600 画布居中"口径取画布中心）。</summary>
        public static readonly Vector2 QuestPanelPos = Vector2.zero;

        /// <summary>章节页签个数（原版 `quests` 只有 Act I~IV 四个页签：`questtab_*.png` 8 帧 = 4 × 常态/选中）。</summary>
        public const int QuestActCount = 4;

        /// <summary>章节页签尺寸（原版 78×30 → ×1.8）。</summary>
        public static readonly Vector2 QuestTabSize = Size(78f, 30f);

        /// <summary>
        /// 第 <paramref name="act"/>（0 起）个章节页签的中心 x。
        /// 原版：4 个页签 4..316（各 78 宽，居中于 320）⇒ 中心 43 / 121 / 199 / 277。
        /// </summary>
        public static float QuestTabX(int act)
        {
            var a = act < 0 ? 0 : (act >= QuestActCount ? QuestActCount - 1 : act);
            return ((43f + 78f * a) - 160f) * K;
        }

        /// <summary>章节页签行中心 y（原版 y 0..30 ⇒ 中心 15 → ×1.8）。</summary>
        public static readonly float QuestTabY = (216f - 15f) * K;

        /// <summary>任务格列数（原版每章 6 个任务 ⇒ 3 列 × 2 行）。</summary>
        public const int QuestSlotCols = 3;

        /// <summary>任务格行数。</summary>
        public const int QuestSlotRows = 2;

        /// <summary>任务格总数（= 3 × 2 = 6，与原版每章任务数一致）。</summary>
        public const int QuestSlotCount = QuestSlotCols * QuestSlotRows;

        /// <summary>任务格（拱形石龛）尺寸（原版 `questsocket_*.png` 80×95 → ×1.8）。</summary>
        public static readonly Vector2 QuestSlotSize = Size(80f, 95f);

        /// <summary>
        /// 任务格里的**原版任务图**尺寸（原版 `MENU/a{章}q{序号}.dc6` / `MENU/questdone.dc6` 帧 = **72×86** → ×1.8）。
        /// <para>出处：`dc6.py info 原版资源/d2dc6/data/global/ui/MENU/a1q1.dc6` = `dir=1 fpd=27 frames=27 72x86 …`；
        /// `questdone.dc6` = `fpd=21 frames=21 72x86`。</para>
        /// </summary>
        public static readonly Vector2 QuestArtSize = Size(72f, 86f);

        /// <summary>
        /// 任务图**相对石龛中心**的偏移：石龛 80×95、任务图 72×86 ⇒
        /// (80−72)/2 = 4、(95−86)/2 = **4.5** ⇒ **居中**（偏移 0）。
        /// </summary>
        public static readonly Vector2 QuestArtPos = Vector2.zero;

        /// <summary>
        /// 第 <paramref name="col"/>（0..2）列石龛中心 x。
        /// 原版：3 列 80 宽、居中于 320 ⇒ 中心 80 / 160 / 240（横向留 40 边距，与原版一致）。
        /// </summary>
        public static float QuestSlotX(int col)
        {
            var c = col < 0 ? 0 : (col >= QuestSlotCols ? QuestSlotCols - 1 : col);
            return ((80f + 80f * c) - 160f) * K;
        }

        /// <summary>
        /// 第 <paramref name="row"/>（0..1）行石龛中心 y。
        /// 原版：石纹区实测 y 30..230（净高 200）、2 行 95 高、上下各余 5px ⇒ 行心 82.5 / 177.5
        /// （旧值 81.5/176.5 是按"y28..230 + 上下各留 6"算的，差 1px，本轮按实测分区改准）。
        /// </summary>
        public static float QuestSlotY(int row)
        {
            var r = row < 0 ? 0 : (row >= QuestSlotRows ? QuestSlotRows - 1 : row);
            return (216f - (82.5f + 95f * r)) * K;
        }

        /// <summary>
        /// 正文（黑底金框区）矩形：**原版实测黑芯** x 2..317 / y 254..382 ⇒ **316×129 原版px** → ×1.8。
        /// <para>⚠️ 上一版是"黑区四周 inset 10 ⇒ 296×111"，那个 10 没有出处；本轮按实测黑芯取值。</para>
        /// </summary>
        public static readonly Vector2 QuestTextBoxSize = Size(316f, 129f);

        /// <summary>
        /// 正文区中心（原版黑芯中心 (159.5, 318) ⇒ 相对面板中心 (−0.5, −102) → ×1.8）。
        /// </summary>
        public static readonly Vector2 QuestTextBoxPos = S(-0.5f, -102f);

        /// <summary>正文区**顶沿**的画布 y（各行位置都由它往下排，行距口径统一在画布单位）。</summary>
        public static readonly float QuestTextTopY = QuestTextBoxPos.y + QuestTextBoxSize.y * 0.5f;

        /// <summary>
        /// 正文左侧/右侧留白（原版px）：黑芯左右各留 8，正文行宽 = 316 − 16 = **300 原版px** = 540 画布px。
        /// </summary>
        public const float QuestTextPadX = 8f;

        /// <summary>正文行宽（画布单位）=（316 − 2×8）×1.8 = 540。</summary>
        public static readonly float QuestTextWidth = (316f - 2f * QuestTextPadX) * K;

        /// <summary>正文上下留白（原版px）：黑芯上下各留 2（不与金框贴死）。</summary>
        public const float QuestTextPadY = 2f;

        /// <summary>
        /// 正文字框高（画布单位）=（黑芯高 129 − 2×2）×1.8 = **225**。
        /// <para>行距不在这里给：文案**一次多行画**，行距 = 该字号下**原版字模的行高**
        /// （出处见本节 §⑥ 末段 libd2 `font.zig:141-146 / :239-259`），由 `UI/D2Text` 自己算。</para>
        /// </summary>
        public static readonly float QuestTextH = (129f - 2f * QuestTextPadY) * K;

        /// <summary>正则文本框中心（同黑芯中心；`UpperLeft` 对齐 ⇒ 文本从框顶沿往下排）。</summary>
        public static readonly Vector2 QuestTextPos = QuestTextBoxPos;

        /// <summary>
        /// 正文字号（画布px）。口径：黑芯高 225 画布px，本项目最长一屏
        /// （`qstsa1q1` + `qstsa1q11` + `qstsa1q12` + 进度行 = 4 行）在 20 号字下占 ~84px ⇒ 余量充足；
        /// **统一一个字号**（旧版名 22 / 正文 20 / 提示 16 三个混用，名与正文的行距对不齐）。
        /// </summary>
        public const int QuestTextFont = 20;

        /// <summary>
        /// 标题条中心 y：贴**面板顶沿**、整条在面板上方。
        /// 原版 `Banner/quests_0.png` 74×54（高 54）⇒ 中心落在面板顶沿上方 27 原版px。
        /// </summary>
        public static readonly float QuestBannerY = (216f + 27f) * K;

        /// <summary>标题条外框（原版 74×54 → ×1.8；`preserveAspect` 保证不被拉变形）。</summary>
        public static readonly Vector2 QuestBannerBox = Size(74f, 54f);

        /// <summary>
        /// 任务日志面板的**图元矩形表**（纯函数，供 `uicheck` 断言「面板内两两不重叠 / 都在面板内」）。
        /// 只列"有原版尺寸依据"的控件；标题条不在表内（它在面板**外**，见 <see cref="QuestBannerY"/>）。
        /// </summary>
        public static (string name, Rect rect)[] QuestRects()
        {
            var list = new System.Collections.Generic.List<(string, Rect)>();
            void Add(string n, Vector2 c, Vector2 s)
                => list.Add((n, new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y)));

            for (var a = 0; a < QuestActCount; a++)
                Add("Tab" + a, new Vector2(QuestTabX(a), QuestTabY), QuestTabSize);
            for (var i = 0; i < QuestSlotCount; i++)
                Add("Slot" + i, new Vector2(QuestSlotX(i % QuestSlotCols), QuestSlotY(i / QuestSlotCols)),
                    QuestSlotSize);
            Add("TextBox", QuestTextBoxPos, QuestTextBoxSize);
            return list.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ 片 5（「小地图 + 死亡屏 1:1」轮）新增：死亡屏（EndGame）布局
        //   **只增不改** —— 上面一个字都没动。
        //
        // 基准（逐条给出处，**没有一个是拍的**）：
        //   ① 底图 = 原版 `data/global/ui/MENU/EndGame.dc6` 的 tile 打包，**实测**
        //      `dc6.py info`：`frames=8  256x256 64x256 256x224 64x224 ×2`
        //      ⇒ 每 4 帧一页：左列 256+右列 64 = **320 宽**；上行 256+下行 224 = **480 高**
        //      ⇒ **2 页 × 320×480**（片 1 的"320×480×2 还是 320×960"悬案定案）。
        //      视觉复核：两页各是一张**独立的 320×480 整幅画**（页 0 = 暗厅 + 孤身人影；
        //      页 1 = 金甲天使（泰瑞尔）），不是一张 320×960 的上下半 ⇒ 拼装口径确认。
        //   ② 标题条 = 原版 `data/LOCAL/UI/chi/youdiedsoftcore.dc6` **帧 0 = 256×54**
        //      （实测；帧 1 = 40×54 是空白/尾块）⇒ 外框 256×54。
        //   ③ 按钮 = 原版 `data/global/ui/MENU/endgameok.dc6` **96×32 ×2 帧**（常态/按下，实测）。
        //   ④ 换算 = 本文件全局口径 `K = 1.8`（原版 800×600 → 1920×1080，按高等比）+ 居中。
        //
        // ⛔ **本项目新增排布（登记 E22）**：原版**该屏的控件坐标没有出处** ——
        //    参考工程 `Diablerie/Assets/Prefabs/` 里**没有 EndGame / 死亡屏 prefab**
        //    （只有 ControlPanel/InventoryPanel/CharstatPanel/SkillPanel/SkillSlot/
        //      AvailableSkillsPanel/EnemyBar/LevelEntryTitle/MainMenu/ClassSelectMenu/…），
        //    原版素材本身也**没有画好的控件槽**（底图是整幅画，`EndGame.png` 里没有框）。
    //    ⇒ 本项目的规则（写死在这里，可离线断言）：
        //      **内容栈 = 「标题条 / 提示行 / 按钮」三行自上而下，各行高 = 该控件原版像素高，
        //        行距固定 15 原版px，整栈垂直居中于面板（= 画布中心 (0,0)）**。
        //      x 一律水平居中。
        // ⛔ **未渲染项（BLOCKED，片 5）**：原版该屏在标题条下面是**引擎渲染的金币数字**
        //    （原版串表 `data/d2text/{eng,chi}_string.txt` id 5094 = `Death takes its toll of
        //     %d Gold` / 「死亡取走了%d金幣」；图形标题条正文 = 「你損失金錢數量為」）。
        //    本项目 `Events.PlayerDied` **无参**（契约 §3.5 冻结），`Events.GoldChanged` /
        //    `PlayerStatsDto` 只能给"当前金币"，**拿不到"死亡前金币"** ⇒ 金额**不可得** ⇒
        //    **本片不渲染该行**（⛔ 不许自己拍一个数字当原版）。见回报 BLOCKED。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>原版死亡屏底图宽度（原版px，= 左列 256 + 右列 64；实测）。</summary>
        public const float DeathBackW = 320f;

        /// <summary>原版死亡屏底图高度（原版px，= 上行 256 + 下行 224；实测）。</summary>
        public const float DeathBackH = 480f;

        /// <summary>死亡屏面板尺寸（画布）：320×480 ×1.8 = **576×864**，居中于画布。</summary>
        public static readonly Vector2 DeathPanelSize = Size(DeathBackW, DeathBackH);

        /// <summary>死亡屏底图**4 块**的（原版 宽, 高, 左上 x, 左上 y）—— 逐值 = `dc6.py info` 实测帧尺寸。</summary>
        public static readonly (float w, float h, float x, float y)[] DeathBackTiles =
        {
            (256f, 256f, 0f, 0f),      // 帧 0：左列 · 上行
            (64f, 256f, 256f, 0f),     // 帧 1：右列 · 上行
            (256f, 224f, 0f, 256f),    // 帧 2：左列 · 下行
            (64f, 224f, 256f, 256f),   // 帧 3：右列 · 下行
        };

        /// <summary>死亡屏标题条（原版 `chi/youdiedsoftcore.dc6` 帧 0）原版尺寸 256×54。</summary>
        public static readonly Vector2 DeathBannerSize = Size(256f, 54f);

        /// <summary>死亡屏按钮（原版 `MENU/endgameok.dc6`）原版尺寸 96×32 ⇒ 画布 172.8×57.6。</summary>
        public static readonly Vector2 DeathButtonSize = Size(96f, 32f);

        /// <summary>死亡屏三行各自的**原版像素高**（本项目新增排布，见 E22）。</summary>
        public const float DeathBannerH = 54f;
        public const float DeathHintH = 25f;
        public const float DeathButtonH = 32f;

        /// <summary>死亡屏行距（原版px；本项目新增排布，见 E22）。</summary>
        public const float DeathRowGap = 15f;

        /// <summary>死亡屏提示行外框宽（原版px = 面板宽 − 左右各 24；本项目新增排布，见 E22）。</summary>
        public const float DeathHintW = DeathBackW - 48f;

        /// <summary>面板顶沿的画布 y（面板 480 高、居中于画布 ⇒ 顶沿 = +240 原版px）。</summary>
        public static readonly float DeathPanelTopY = DeathBackH * 0.5f * K;

        /// <summary>
        /// 把「面板内自顶量 y（原版px）」换成**画布 y**（画布 y 向上为正，面板内 y 向下为正）。
        /// </summary>
        public static float DeathCanvasY(float origTopY) => (DeathBackH * 0.5f - origTopY) * K;

        /// <summary>
        /// 死亡屏 4 块底图在**画布上的中心坐标**（相对画布中心）：由实测块表算出，**没有魔数**。
        /// 公式：块中心（原版）= (x + w/2, y + h/2)；减面板中心 (320/2, 480/2)；y 轴取负换到画布。
        /// </summary>
        public static Vector2 DeathTilePos(int i)
        {
            var t = DeathBackTiles[i];
            var cx = t.x + t.w * 0.5f - DeathBackW * 0.5f;
            var cy = t.y + t.h * 0.5f - DeathBackH * 0.5f;
            return new Vector2(cx * K, -cy * K);
        }

        /// <summary>死亡屏 4 块底图各自的画布尺寸（= 实测原版帧尺寸 × K）。</summary>
        public static Vector2 DeathTileSize(int i)
        {
            var t = DeathBackTiles[i];
            return new Vector2(t.w * K, t.h * K);
        }

        /// <summary>
        /// 死亡屏内容栈的行中心 y（画布）：**整栈垂直居中于面板**（本项目排布规则，见 E22）。
        /// 顺序：0 = 标题条 / 1 = 提示行 / 2 = 按钮。纯函数 ⇒ `uicheck` 逐条断言。
        /// </summary>
        public static float DeathRowCenterY(int row)
        {
            var heights = new[] { DeathBannerH, DeathHintH, DeathButtonH };
            var total = 0f;
            for (var i = 0; i < heights.Length; i++) total += heights[i];
            total += DeathRowGap * (heights.Length - 1);

            var top = (DeathBackH - total) * 0.5f;      // 栈顶（面板内自顶量，原版px）
            for (var i = 0; i < row; i++) top += heights[i] + DeathRowGap;
            return DeathCanvasY(top + heights[row] * 0.5f);
        }

        /// <summary>
        /// 死亡屏的**图元矩形表**（纯函数，供 `uicheck` 断言「两两不重叠 / 都在面板内」）。
        /// 4 块底图 + 标题条 + 提示行 + 按钮，全部列出。
        /// </summary>
        public static (string name, Rect rect)[] DeathRects()
        {
            var list = new System.Collections.Generic.List<(string, Rect)>();
            void Add(string n, Vector2 c, Vector2 s)
                => list.Add((n, new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y)));

            for (var i = 0; i < DeathBackTiles.Length; i++)
                Add("BackTile" + i, DeathTilePos(i), DeathTileSize(i));
            Add("Banner", new Vector2(0f, DeathRowCenterY(0)), DeathBannerSize);
            Add("Hint", new Vector2(0f, DeathRowCenterY(1)), Size(DeathHintW, DeathHintH));
            Add("Button", new Vector2(0f, DeathRowCenterY(2)), DeathButtonSize);
            return list.ToArray();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ 片 5 新增：自动地图（小地图）面板
        //
        // ⛔ **原版没有"窗口框"这种素材**（实测 `原版资源/d2dc6/data/global/ui/**` 与
        //    `data/LOCAL/UI/{chi,eng}/**` 的全部 DC6）：自动地图那一族的图形只有
        //      · 标题/开关条：`automap.dc6`（chi 256×36 + 122×36 = "AUTOMAP OPTIONS"）、
        //        `AutoMapCenter.dc6` 120×34「清除時置中」、`AutoMapParty.dc6` 96×34「顯示隊伍」、
        //        `automapfade.dc6` 48×34「淡化」、`AutoMapOptions.dc6` 222×54「自動地圖選項」、
        //        `AutoMapPartyNames.dc6`（键名串表 `CfgAutoMapNames` = "Names on Automap"）
        //      · 地图瓦片表：`ui/AUTOMAP/MaxiMap.dc6`（**1260 帧 16×32**，**全 act 共用**）、
    //        `Act2Map.dc6`（40 帧 160×100）、`Act4Map.dc6`（4 帧 136×90）
        //    ⇒ 原版自动地图是**满屏叠加层**（把已探索的格子按 `AutoMap.txt` 的 Cel 号从
        //      MaxiMap 上取 16×32 小图 blit 到等距位置），**没有一个"右上角定尺框"**。
        //    本项目这个是**右上角定尺框 + 自绘格子贴图** ⇒ 登记 E23（含为什么：逐格 Cel 数据
        //    在本项目拿不到，见回报 BLOCKED）。
        //    ⚠️ 因此本节的坐标**没有原版出处**，它们沿用本项目既有表现（agent-09 定的框），
        //      只把"自加的标题行 / 图例行"删掉（原版自动地图**没有**标题与图例文字 —— 片 2 的
        //      `自审对比/README.md` ② 第 12 行已登记过这条不一致，片 5 收口）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>自动地图框最大边长（画布px；本项目表现，见 E23）。</summary>
        public const float MiniMapBoxMax = 360f;

        /// <summary>自动地图框距画布右上角的留白（画布px；本项目表现，见 E23）。</summary>
        public const float MiniMapMargin = 20f;

        /// <summary>自动地图框**右上角**（pivot = (1,1)）的画布坐标 —— 贴画布右上角留 <see cref="MiniMapMargin"/>。</summary>
        public static readonly Vector2 MiniMapBoxPos = new Vector2(
            UiArt.RefWidth * 0.5f - MiniMapMargin, UiArt.RefHeight * 0.5f - MiniMapMargin);

        /// <summary>
        /// 自动地图标记图标的显示边长（画布px）：原版 `mapicons.DC6` 每帧都是 **16×16**（实测），
        /// 原版把图标**按固定像素尺寸 blit**（不随地图缩放）⇒ 本项目 = 16 × K = **28.8**。
        /// </summary>
        public const float MiniMapIconPx = 16f * K;
    }
}
