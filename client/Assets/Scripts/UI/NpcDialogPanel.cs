// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/NpcDialogPanel.cs
// NPC 对话面板（原版 `MENU/dialogbackground.DC6` 石框 + 原版串表的台词与菜单项）。
//
// ★★ 本片（2026「对话面板 = 原版石框分区 + 原版串」轮）**删掉了三处没有出处的自造件**：
//   ① 名字行之外还画了一行「（点下方『交易 / 修理』打开商店）」提示 —— 原版串表里没有这句，
//      石框里也没有它的槽位；② 两排动作按钮（「接受任务」/「交付任务」/「结束对话」/「交易 / 修理」）
//      —— 这些**中文原版串表里一个都没有**（原版是"点了 NPC 就算接下任务"，没有这两个按钮；
//      `原版资源/d2text/chi_string.txt` 里 `NPCMenu*` 全族只有「交易/修理 / 交易 / 離開 / 閒話 /
//      再說一點 / 雇用 / 重要消息…」这类菜单项）；③ 旧版还有一颗跑到屏幕最左边缘的孤儿按钮。
//   ⇒ 现在：**台词 + 菜单项都由 `Module/Npc` 给，且都是原版串**；面板只负责摆位置。
//
// ★ 底图分区（**逐像素扫描实测**，口径与逐行数据见 `策划/自审对比/UI对照.md` §③）：
//   `Panel/dialog_back.png`（原版 `MENU/dialogbackground.DC6`，210×158）：
//     · 外沿金线 x 0/1 与 208/209、y 0/1 与 156/157 ⇒ 可见内容区 = 原版 x 2..207 / y 2..155；
//     · **金框长槽**：竖金线 x 29..30 与 196..197、横金线 y 67 与 91 ⇒ 净内 x 31..195 / y 68..90
//       （= 全场唯一一条"正文位"形状的槽）；
//     · **下带** y 93..155，里面有一对**雕出来的方槽 34×34**：左 x 34..67、右 x 139..172、y 115..148。
//   ⚠️ **那两个 34×34 方槽原版放什么控件/什么文案，本批材料里查不到出处**（穷尽记录见
//      `UI对照.md` §⑥ BLOCKED：libd2 0 命中、参考工程 0 命中、素材里没有随附配置，
//      `buysellbtn`(32×32)/`questlast`(30×30)/`okcancelbtn`(96×32) 都对不上 34×34）
//      ⇒ 本项目**不往那两个方槽里塞控件**（保持底图原样），菜单项排在**下带**里。
//
// ★ 文本排版口径（**有出处**）：libd2 `packages/formats/src/font.zig`
//   `:141-146` 行高 = 该字体最高字形、`:239-259` 每行 `baseline += line_height` 且**左对齐**、
//   `:188-216` `D2WINTEXTBOX_WordWrapAndSetText @0x4fcda0` = 换行规则；
//   本工程 `UI/D2Text` 的 `pitch = 字号（画布px）`（`D2Text.cs:D2Label.BuildBitmap` 的
//   `cellH * scale`，而 `scale = 字号 / cellH`）⇒ 行距 = 字号。
//
// ★ 数据：只吃 `Diablo2.Def.NpcDialogArgs`（`OnOpen` 参数 + `Events.DialogOpen`）：
//     npcId / npcName / text（**随任务阶段变化，由 Npc 模块给**）/ options /
//     hasShop / canAcceptQuest / canTurnInQuest / questId
//   ⇒ 本面板**不做任何任务阶段判断**，全部按 DTO 的布尔位渲染（数据由模块算）。
// ★ 请求（全部走 `Core/Events.cs`）：options[i] 点击 → `Events.DialogOptionChosen`（int 下标）
//   —— 下标语义由 `Module/Npc/NpcModule.ChooseOption` 反解（0 = 关闭、1 = 任务动作、其余 = 商店），
//   **本面板不直接发 `QuestAcceptRequest` / `QuestTurnInRequest` / `ShopOpenRequest`**
//   （那些由模块在 `ChooseOption` 里发 ⇒ 只有一条路径，不会出现"两条路径打同一个动作"）。
// ⛔ 零 `using Diablo2.Module`（分层自检 ③）。
//
// ═══════════════════════════════════════════════════════════════════════════
// ★★ R1-E「人物对话时 UI 逻辑乱七八糟」本面板改的 5 处（S1 / S3 / S4 / S6 / S7）
//    每处都在实现处写了依据；日志里各有一条 `[R1-E] S<n>` 的**只报一次** Info 供实机对账。
//
//  S1 **层 Popup → Normal：商店与对话条共存**（原版行为：点「交易」商店打开、对话条仍在）
//     依据 = 引擎互斥/遮罩的语义（`clover-client-unity-engine/Runtime/Presentation/UI.cs`）：
//       · `:155-159` `Open<T>` 里 `panel.Layer == UILayer.Popup` ⇒ 调 `CloseMutexPanels()`
//         + `ShowMask()`；而 `:431-441` `CloseMutexPanels` 把**所有** `Layer == Popup` 的面板
//         `Close` 掉，`Close` 走 `Object.Destroy(root)`（`:199-229`，`OnClose` 只回调、**不发**
//         `Events.DialogClose`）⇒ 对话条被"点交易"**销毁**，而模块侧 `_currentNpcId` 不知道 ⇒ 串档。
//       · `:443-461` `ShowMask` 的遮罩挂在 **Popup 层**、`SetAsFirstSibling`、`raycastTarget = true`
//         ⇒ 遮罩之下的一切（含 Normal 层）**既被压暗、也吃不到点击**。
//     两条合起来只有一个可行解：**共存时必须是"对话条让位"**——
//       商店留在 `Popup`（它要在遮罩**之上**才点得动），对话条降到 `Normal`（它在遮罩**之下**：
//       商店开着时被压暗/不可点，正是"模态商店"该有的样子；关掉商店（`HideMask`）后立刻恢复可点）。
//       反过来（商店降级到 Normal）会让商店自己被遮罩挡住 ⇒ 整屏点不动（已排除）。
//     副作用（登记在回报里）：商店开着时对话条被 0.5 黑遮罩压暗 —— 遮罩是引擎内建、无开关；
//       且它是"关商店后对话条仍在、可继续说话/交付"的**前提**，不是缺陷。
//
//  S3 **`OnClose` 补发 `Events.DialogClose`：面板被引擎销毁时模块侧状态必须归零**
//     问题链：引擎 `Close` 只调 `OnClose`（见上），而 `NpcModule._currentNpcId` 只由
//     `Events.DialogClose` 清（`Module/Npc/NpcModule.cs:746-749`）⇒ 面板被"互斥 / `CloseAll` /
//     换站"销毁后 `_currentNpcId` 残留 ⇒ 之后任何 `Events.QuestChanged` 都会**凭空再弹一次对话**
//     （`NpcModule.OnQuestChanged` 的唯一门槛就是它），且 `ResolveNpcId` 会拿陈旧 NPC 兜底。
//     修法：`OnClose` 里**先** `Unsubscribe()` **再** `Emit(Events.DialogClose)`（顺序重要：
//     先退订 ⇒ 不会收到自己发的那条 ⇒ 无回环；模块侧清理是幂等的）。
//     ⇒ 不变式：**`_currentNpcId` 的非 None 区间 == 本面板实例的存活区间**。
//
//  S4 **去掉重复订阅：一次 `Events.DialogOpen` 只 `Rebuild` 一次**
//     原先 `HudPanel`（`:823` + `:1056-1064`）与本面板（`:336` + `:348-357`）**都**订阅了
//     `Events.DialogOpen` ⇒ 同一次刷新走两遍（`Open<T>` 已开 ⇒ 再 `OnOpen` 一次 + 本面板再
//     `Rebuild` 一次）。面板**开不了自己**（它只有被 `Open<T>` 实例化之后才有实例）⇒
//     权威路径唯一 = `HudPanel.OnDialogOpen` → `Game.UI.Open<NpcDialogPanel>(args)` → `OnOpen(param)`。
//     ⇒ 删掉本面板对 `Events.DialogOpen` 的订阅与处理函数；保留 `Events.DialogClose` 订阅
//     （模块侧要关面板时的回程，唯一的）。
//
//  S6 **菜单项挪出那两个 34×34 雕花方槽**（几何重叠 = S6）
//     实测：改前选项行宽 = `ContentW`(347.4 画布px = 193 原版px)，行心换算回原版坐标
//     y ≈ 100.9 / 116.7 / 132.6 ⇒ 第 2、3 行落在雕槽 `y 115..148` 里（第 3 行整行在内）。
//     下带（原版 y 93..155 = 62px 高）里，被雕槽占掉 `y 115..148` 后剩下的缝只有 22px（上）+ 7px（下）
//     —— 放不下 3 行（每行 14.17 原版px、步进 15.83）。**唯一放得下的位置** = 两个雕槽**之间**的
//     中央列 `x 67..139`（宽 72px，全高 62px 可用）⇒ 选项行取该列内缩 3px = **66 原版px 宽**
//     （118.8 画布px），中心 = 原版 x 103（≈ 底图中线 105）。
//     硬约束全部保持：① 落在石框可见区 `x[-185.4,183.6] × y[-314.7,-30.3]` 内；
//     ② 三行两两不重叠；③ 不越石框底沿；④（新增）与两个雕槽**二维矩形不相交**。
//     ⚠️ 行宽变窄 ⇒ 文案必须放得下：最长的一条是原版串 3334「交易/修理」= 84.6 画布px
//     （实量：`font16_chi_map.txt` 的 advance 求和 × 20/13，见 `uicheck` 的 ④-3 断言）≤ 118.8 ✓。
//
//  S7 **`OnOpen` 漏参数 ⇒ 明确 Warn + 不复用上一次数据**
//     改前 `if (dialog != null) _dialog = dialog;` ⇒ 漏传 param 时会**静默复用**上一次的
//     台词/选项（残留路径）。现在：param 为空 ⇒ `_dialog = null` + Warn（点名原因是"不复用"）。
// ═══════════════════════════════════════════════════════════════════════════
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>NPC 对话面板。层：<see cref="UILayer.Popup"/>。</summary>
    public class NpcDialogPanel : UIPanel
    {
        /// <summary>
        /// 对话条**命中区**尺寸（透明板，只吃点击、不画像素；比底图略宽，保证点到石框边上也不穿透到 HUD）。
        /// </summary>
        public static readonly Vector2 FrameSize = new Vector2(615f, 187.5f);

        /// <summary>
        /// 原版对话框底图尺寸（`MENU/dialogbackground.DC6` 210×158 → ×1.8 = **378×284.4**）。
        /// 用**原版像素 1:1**摆放，不拉伸（该图无同质中段，拉伸会失真）。
        /// </summary>
        public static readonly Vector2 DialogArtSize = new Vector2(210f * 1.8f, 158f * 1.8f);

        /// <summary>对话条中心 y（屏幕下方，原版对话条也在下方；×1.8 口径）。</summary>
        public const float FrameY = -172.5f;

        /// <summary>石框可见区**顶沿**画布 y（= 中心 + 半高）。</summary>
        public static readonly float FrameTop = FrameY + DialogArtSize.y * 0.5f;

        /// <summary>石框可见区**底沿**画布 y。</summary>
        public static readonly float FrameBottom = FrameY - DialogArtSize.y * 0.5f;

        // ═════════════════════════════════════════════════════════════════════
        // 底图实测分区（原版px，**相对底图左上角**；逐像素扫描口径见文件头）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>金框长槽（唯一的"正文位"形状）：金线外沿矩形。</summary>
        public const float SlotOuterX0 = 29f;
        public const float SlotOuterX1 = 197f;
        public const float SlotOuterY0 = 67f;
        public const float SlotOuterY1 = 91f;

        /// <summary>下带（菜单/按钮区）：金线 y 91 之下到外框内沿 y 155。</summary>
        public const float LowerBandY0 = 93f;
        public const float LowerBandY1 = 155f;

        /// <summary>下带里那对**雕出来的方槽**（34×34）：左 x 34..67 / 右 x 139..172，y 115..148。
        /// ⚠️ 原版拿它放什么**无出处** ⇒ 本项目不占用（保持底图原样），见文件头。</summary>
        public const float SlotCellSize = 34f;
        public const float SlotCellLeftX0 = 34f;
        public const float SlotCellLeftX1 = 67f;
        public const float SlotCellRightX0 = 139f;
        public const float SlotCellRightX1 = 172f;
        public const float SlotCellY0 = 115f;
        public const float SlotCellY1 = 148f;

        /// <summary>底图横向中心对应的**原版 x**（210/2 = 105）：画布 x =（原版 x − 105）× K。</summary>
        public const float ArtCenterX = 105f;

        /// <summary>原版 x → 画布 x。</summary>
        public static float Cx(float origX) => (origX - ArtCenterX) * UiLayoutGame.K;

        /// <summary>原版 y（从底图顶沿往下量）→ 画布 y（uGUI 向上为正）。</summary>
        public static float Cy(float origY) => FrameTop - origY * UiLayoutGame.K;

        // ═════════════════════════════════════════════════════════════════════
        // 文本区：原版上带 + 中带（合起来 = 石框内"金框长槽"及其上方的大理石面）
        //   为什么合成一块：长槽净高只有 23 原版px（≈2 行），而原版串最长的一条
        //   （`A1Q1InitAkara`，键名见 Module/Npc/NpcDialog.cs）有 5 段硬换行、折行后 8~9 行；
        //   原版长台词是靠**分页**逐页显示的（串表里有 `NPCPreviousPage` 3340 / `NPCNextPage` 3339），
        //   而"哪个控件触发翻页"在本批材料里同样没有出处 ⇒ 本项目**不分页、一屏画全**，
        //   于是把文本框从长槽向上扩到上带（两带同色：实测逐行亮度都是 ~24..31，中间只隔一条金线 67）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>文本区顶沿（原版 y 3：不贴外框金线 y=1）。</summary>
        public const float TextTopOrigY = 3f;

        /// <summary>文本区底沿（原版 y 90 = 长槽净内下沿）。</summary>
        public const float TextBottomOrigY = 90f;

        /// <summary>文本区总高（画布px）=(90−3)×1.8 = 156.6。</summary>
        public static readonly float TextH = (TextBottomOrigY - TextTopOrigY) * UiLayoutGame.K;

        /// <summary>内容左右留白（原版px）：石框内沿 x 2..207 各内缩 6。</summary>
        public const float TextPadX = 6f;

        /// <summary>内容行宽（画布px）=(205 − 2×6)×1.8 = 347.4。</summary>
        public static readonly float ContentW = (205f - 2f * TextPadX) * UiLayoutGame.K;

        /// <summary>内容中心 x（画布px，= 原版 x 102.5）。</summary>
        public static readonly float ContentCx = Cx((2f + TextPadX + 205f - TextPadX) * 0.5f);

        /// <summary>名字行高（画布px）。</summary>
        public const float NameH = 20f;

        /// <summary>名字行字号（画布px）。</summary>
        public const int NameFont = 16;

        /// <summary>台词字号（画布px）⇒ 行距 = 14 画布px（`D2Text` 口径），最长一屏 8~9 行 = 112~126 ≤ 132.6。</summary>
        public const int BodyFont = 12;

        /// <summary>名字行中心 y。</summary>
        public static readonly float NameY = Cy(TextTopOrigY) - NameH * 0.5f;

        /// <summary>台词框高（画布px）= 文本区高 − 名字行高 = 132.6。</summary>
        public static readonly float BodyH = TextH - NameH;

        /// <summary>台词框中心 y。</summary>
        public static readonly float BodyY = Cy(TextTopOrigY) - NameH - BodyH * 0.5f;

        // ═════════════════════════════════════════════════════════════════════
        // 菜单项（`NpcDialogArgs.options`，最多 3 项）—— 排在下带里、**两个雕花方槽之间的中央列**（S6），
        //   逐行居中。顺序由模块给：0 = 关闭（原版串 `NPCMenuLeave`「離開」）→ 任务动作 → 商店入口。
        //   ⛔ 行宽/列心**不要**改回 `ContentW` / `ContentCx`：那样第 2、3 行会压进雕槽
        //      （几何断言在 `tools/probes/hosts/uicheck` 的 ⑬ S6；依据见文件头 S6）。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 菜单项**宽度**（原版px）= 两个雕花方槽之间的净宽 72 再各留 3 的呼吸位 ⇒ **66**
        /// （×1.8 = 118.8 画布px）。见文件头 **S6**：行宽必须落在这条中央列里，否则会与雕槽几何重叠
        /// （旧值 = 内容行宽 193 原版px ⇒ 第 2/3 行压进雕槽）。
        /// </summary>
        public const float OptionW = 66f;

        /// <summary>菜单项尺寸（宽 = 中央列 66 原版px；高 25.5 画布px）。</summary>
        public static readonly Vector2 OptionSize = new Vector2(OptionW * UiLayoutGame.K, 25.5f);

        /// <summary>
        /// 菜单项列的**中心 x**（画布px）= 两雕槽之间的中点（原版 x (67+139)/2 = 103）。
        /// 底图横向中线是 105（<see cref="ArtCenterX"/>）⇒ 本列天然几乎居中（差 3.6 画布px）。
        /// </summary>
        public static readonly float OptionX = Cx((SlotCellLeftX1 + SlotCellRightX0) * 0.5f);

        /// <summary>菜单项行距（画布px）：高 25.5 + 3 的呼吸位。</summary>
        public const float OptionStep = 28.5f;

        /// <summary>本面板最多显示几个菜单项（再多会越过石框底沿）。</summary>
        public const int MaxOptions = 3;

        /// <summary>第 <paramref name="i"/> 个菜单项的中心 y（第 0 个紧贴下带顶沿之内）。</summary>
        public static float OptionY(int i)
        {
            var k = i < 0 ? 0 : (i >= MaxOptions ? MaxOptions - 1 : i);
            return Cy(LowerBandY0) - 14.25f - k * OptionStep;
        }

        private bool _built;
        private bool _subscribed;
        private NpcDialogArgs _dialog;

        // ── R1-E：每条修复点的「只报一次」标志（本类没有逐帧高频路径，就地一次性标志足够）──
        /// <summary>`[R1-E] S1` 层语义（商店/对话共存）已说明过。</summary>
        private static bool _loggedS1;

        /// <summary>`[R1-E] S3` 关闭即归零（补发 `DialogClose`）已说明过。</summary>
        private static bool _loggedS3;

        /// <summary>`[R1-E] S4` 唯一打开路径（一次 DialogOpen 只 Rebuild 一次）已说明过。</summary>
        private static bool _loggedS4;

        /// <summary>`[R1-E] S6` 菜单项列口径（挪出雕槽）已说明过。</summary>
        private static bool _loggedS6;

        /// <summary>`[R1-E] S7` 漏参数不复用旧数据已说明过。</summary>
        private static bool _loggedS7;

        private Text _speaker;
        private Text _body;
        private readonly List<Image> _optionButtons = new List<Image>();
        private readonly List<Text> _optionLabels = new List<Text>();

        /// <summary>
        /// **Normal**（不是 `Popup`）—— R1-E 的 **S1**：`Popup` 层是**互斥层**，引擎 `UIManager.Open`
        /// 在打开任何 `Popup` 面板时会把同层其它面板全部 `Close`（= `Object.Destroy`，且不发
        /// `Events.DialogClose`）⇒ 点「交易」会把对话条销毁。依据与取舍见文件头 **S1**（含为什么
        /// 商店必须留在 `Popup`、为什么遮罩之下的只能是对话条）。
        /// </summary>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            if (!_loggedS1)
            {
                _loggedS1 = true;
                UiLog.Info("[R1-E] S1 生效：对话条在 Normal 层、商店在 Popup 层 ⇒ 引擎的 Popup 互斥"
                    + "（`UIManager.CloseMutexPanels`）不会再销毁对话条；商店开着时对话条被 Popup 遮罩"
                    + "压暗且不可点（模态），关掉商店后立刻恢复可点、可继续说话/交付");
            }
            if (!_loggedS4)
            {
                _loggedS4 = true;
                UiLog.Info("[R1-E] S4 生效：一次 `Events.DialogOpen` 只刷新一次 —— 本面板**不再订阅**"
                    + "该事件，唯一权威路径 = `HudPanel.OnDialogOpen` → `Game.UI.Open<NpcDialogPanel>(args)`"
                    + " → `OnOpen(param)`（已开则重新 `OnOpen` + 置顶）");
            }

            // S7 的「生效口径」**无条件**报一次（只报一次），不放进入 null 的那一支 ——
            // 否则正常玩一局永远不会出现这条，取证批次就看不到这个修复点的实机证据。
            if (!_loggedS7)
            {
                _loggedS7 = true;
                // ⚠️ 这条日志里**不**写改前那行源码：写成字面量会被 uicheck 的 S7 断言（剥注释后仍看字面量）扫到
                //    ⇒ 假 FAIL。改前写法见文件头 S7 那段注释（注释会被断言剥掉，不参与判定）。
                UiLog.Info("[R1-E] S7 生效：`OnOpen` 拿不到 `NpcDialogArgs` 时置**空态**"
                    + "（`_dialog = null`，**不复用**上一次的台词/选项；同时打一条 WarnOnce 点名）"
                    + "—— 旧版漏参数会静默复用旧数据（改前写法见 NpcDialogPanel 文件头 S7）");
            }

            var dialog = UiLog.Require<NpcDialogArgs>(param, nameof(NpcDialogPanel));
            if (dialog == null)
            {
                // S7：漏参数 ⇒ **明确空态**，绝不复用上一次的 _dialog（否则残留旧台词/旧选项）
                UiLog.WarnOnce("dialog.open.param.null",
                    $"对话面板 `OnOpen(param)` 没拿到 `NpcDialogArgs` ⇒ 置空态（不复用上一次数据："
                    + $"上一次 NPC={_dialog?.npcName ?? "无"}）；请检查打开方是否漏传 DTO（constraints.md #7）");
                _dialog = null;
            }
            else
            {
                _dialog = dialog;
            }
            Rebuild(_dialog);

            UiLog.Info($"对话面板已打开（NPC={(dialog != null ? dialog.npcName : "无数据")}，"
                       + $"选项={_dialog?.options?.Count ?? 0}，可知接任务={_dialog?.canAcceptQuest ?? false}，"
                       + $"可交任务={_dialog?.canTurnInQuest ?? false}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            // 顺序有讲究（见文件头 **S3**）：先退订 ⇒ 下面那条 Emit 不会回到自己身上（无回环）。
            Unsubscribe();

            if (!_loggedS3)
            {
                _loggedS3 = true;
                UiLog.Info("[R1-E] S3 生效：对话面板**关闭即归零** —— `OnClose` 补发 `Events.DialogClose`，"
                    + "`NpcModule._currentNpcId` 随之清空（面板被引擎互斥/CloseAll/换站销毁时也不例外）"
                    + "⇒ 之后 `Events.QuestChanged` 不会再凭空弹出对话；模块侧清理幂等，无回环");
            }

            // ★ S3：引擎 `UIManager.Close` **只**回调 `OnClose`、不补发任何事件（`UI.cs:199-229`）
            //   ⇒ 模块侧的状态（`_currentNpcId`）只能由这里补一条 `DialogClose` 才清得掉。
            //   面板此刻已从 `_panels` 摘除（`Close` 先摘表再回调）⇒ 不会再被 `Game.UI.Close` 二次触发。
            Game.Event?.Emit(Events.DialogClose);
            UiLog.Info("对话面板已关闭（已补发 `Events.DialogClose`，模块侧状态归零）");
        }

        private void Build()
        {
            if (_built) return;
            _built = true;

            // ★ 底图 = **原版对话框**（`MENU/dialogbackground.DC6` = 210×158，按原版像素 1:1 ×1.8 = 378×284.4，
            //   **不拉伸/不九宫格** —— 实测该图没有同质中段（逐列/逐行比对最长同质段 = 1px），拉伸会把石纹拉花）。
            //   命中区单独一块透明板（比底图宽），保证点到空白处也能吃掉点击、不穿透到 HUD。
            UiArt.Art(transform, "DialogArt", ResPaths.PanelDialogBack, DialogArtSize,
                new Vector2(0f, FrameY));
            UiArt.Panel(transform, "DialogHit", FrameSize, new Vector2(0f, FrameY),
                new Color(1f, 1f, 1f, 0f), true);

            // 标题条 = 原版**中文**位图（`data/local/ui/chi/npcspeech.dc6` 95×34，位图实测逐字「NPC 語音」）；
            // 外框 = 原版尺寸 ×1.8 = 171×61.2，整条落在石框**上方**（底沿贴 FrameTop）。
            // ⚠️ 原版**没有这条的坐标出处**（石框里没有标题槽）⇒ 登记 `策划/验收表.md` 的 E17（BLOCKED 部分）。
            UiArt.Banner(transform, "SpeechBanner", ResPaths.Banner("npcspeech_0"),
                new Vector2(95f * 1.8f, 34f * 1.8f), new Vector2(0f, FrameTop + 34f * 1.8f * 0.5f));

            _speaker = UiArt.Label(transform, "Speaker", string.Empty, NameFont, TextAnchor.MiddleCenter,
                UiArt.TitleColor, new Vector2(ContentW, NameH), new Vector2(ContentCx, NameY));

            _body = UiArt.Label(transform, "Body", string.Empty, BodyFont, TextAnchor.UpperLeft,
                UiArt.TextColor, new Vector2(ContentW, BodyH), new Vector2(ContentCx, BodyY));

            if (!_loggedS6)
            {
                _loggedS6 = true;
                UiLog.Info("[R1-E] S6 生效：菜单项列挪出两个 34×34 雕花方槽 —— 行宽 "
                    + OptionW.ToString("0.#") + " 原版px（= 两雕槽之间净宽 72 内缩 3）、列中心 原版 x "
                    + ((SlotCellLeftX1 + SlotCellRightX0) * 0.5f).ToString("0.#")
                    + "；行心（换回原版 y）= " + OptionOrigY(0).ToString("0.0") + " / "
                    + OptionOrigY(1).ToString("0.0") + " / " + OptionOrigY(2).ToString("0.0")
                    + "，与两雕槽（x 34..67 与 139..172、y 115..148）二维矩形不相交");
            }
        }

        /// <summary>第 <paramref name="i"/> 个菜单项行心换算回**原版 y**（从底图顶沿往下量；对账/断言用）。</summary>
        public static float OptionOrigY(int i) => (FrameTop - OptionY(i)) / UiLayoutGame.K;

        // ═════════════════════════════════════════════════════════════════════
        // 刷新
        // ═════════════════════════════════════════════════════════════════════
        private void Rebuild(NpcDialogArgs dialog)
        {
            if (!_built)
            {
                UiLog.Warn("对话面板尚未构建就收到刷新 ⇒ 忽略");
                return;
            }

            if (dialog == null)
            {
                UiLog.WarnOnce("dialog.no.data",
                    "对话面板未收到 `NpcDialogArgs` ⇒ 只显示空对话；请 Npc 模块在交互时 emit `Events.DialogOpen`");
                _speaker.text = string.Empty;
                _body.text = string.Empty;
                HideAllOptions();
                return;
            }

            _speaker.text = dialog.npcName ?? string.Empty;
            _body.text = dialog.text ?? string.Empty;

            var options = dialog.options;
            if (options == null || options.Count == 0)
            {
                UiLog.WarnOnce("dialog.no.options." + dialog.npcId,
                    $"「{dialog.npcName}」的对话没有给任何选项 ⇒ 面板无出口（只剩 ESC/再点 NPC）；"
                    + "请 Npc 模块保证 options[0] = 关闭项");
                HideAllOptions();
                return;
            }

            for (var i = 0; i < options.Count; i++)
            {
                var button = EnsureOption(i);
                var label = options[i] ?? string.Empty;
                _optionLabels[i].text = label;
                var index = i;
                // 重新绑定点击（先清掉旧监听，避免多次 Rebuild 叠加重放）
                var uiButton = button.GetComponent<Button>();
                if (uiButton != null)
                {
                    uiButton.onClick.RemoveAllListeners();
                    uiButton.onClick.AddListener(() => OnOption(index, label));
                }
            }

            for (var i = options.Count; i < _optionButtons.Count; i++)
                _optionButtons[i].gameObject.SetActive(false);

            UiLog.Info($"对话面板已刷新：NPC={dialog.npcName} 台词 {(_body.text ?? string.Empty).Length} 字 "
                       + $"选项 {options.Count} 项=[{string.Join(" / ", options)}]（全部为原版串表文案）");
        }

        private Image EnsureOption(int index)
        {
            while (_optionButtons.Count <= index)
            {
                var i = _optionButtons.Count;
                if (i >= MaxOptions)
                {
                    UiLog.WarnOnce("dialog.option.overflow",
                        $"对话选项超过 {MaxOptions} 个 ⇒ 第 {MaxOptions + 1} 个起会越过石框底沿"
                        + "（本项目最多 3 项：关闭 / 任务动作 / 商店入口）");
                }
                // ★ S6：列中心是 `OptionX`（两雕槽之间的中央列），**不是** `ContentCx`（内容行宽会压进雕槽）
                var button = UiArt.Button(transform, "Option" + i, string.Empty, OptionSize,
                    new Vector2(OptionX, OptionY(i)), null);
                _optionButtons.Add(button);
                _optionLabels.Add(UiArt.ButtonLabel(button));
            }

            _optionButtons[index].gameObject.SetActive(true);
            return _optionButtons[index];
        }

        private void HideAllOptions()
        {
            for (var i = 0; i < _optionButtons.Count; i++)
                _optionButtons[i].gameObject.SetActive(false);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 动作
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 选菜单项：**只发下标**（`Events.DialogOptionChosen`），动作由 `NpcModule.ChooseOption` 反解 ——
        /// 0 = 关闭、1 = 任务动作（接/交）、其余 = 商店入口。
        /// </summary>
        private void OnOption(int index, string label)
        {
            UiLog.Info($"选择对话选项 [{index}]「{label}」⇒ `{Events.DialogOptionChosen}`（下标即 DTO 的 options 下标）");
            Game.Event.Emit(Events.DialogOptionChosen, index);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 订阅（**只剩 `DialogClose` 一条**）。R1-E 的 **S4**：`Events.DialogOpen` 的订阅已删除 ——
        /// 打开/刷新对话的唯一权威路径 = `HudPanel.OnDialogOpen` → `Game.UI.Open<NpcDialogPanel>(args)`
        /// → `OnOpen(param)`。本面板**开不了自己**（没有实例就没有订阅者）⇒ 原先那条订阅纯属重复
        /// （同一次 `DialogOpen` 会 `Rebuild` 两遍）。依据见文件头 **S4**。
        /// </summary>
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On(Events.DialogClose, OnDialogCloseEvent);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off(Events.DialogClose, OnDialogCloseEvent);
        }

        /// <summary>
        /// 模块侧关闭对话 ⇒ 面板跟着关。
        /// 本方法**不再** `Emit(DialogClose)`（那是 `OnClose` 的职责，见文件头 S3）：
        /// 这里是被动的回程，模块已经清过状态 ⇒ 再发一条就是多余事件。
        /// </summary>
        private void OnDialogCloseEvent()
        {
            if (!Game.UI.IsOpen<NpcDialogPanel>()) return;
            UiLog.Info("收到 `Events.DialogClose` ⇒ 关闭对话面板");
            Game.UI.Close<NpcDialogPanel>();
        }
    }
}
