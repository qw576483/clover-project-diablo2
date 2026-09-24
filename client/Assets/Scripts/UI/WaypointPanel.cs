// ─────────────────────────────────────────────────────────────────────────────
// 站点：无（游戏内面板）。层：**Popup**（与 `InventoryPanel` / `ShopPanel` 同层）。
// 预制体：`Resources/UI/WaypointPanel`（空壳，内容由本文件 `Build()` 用 `UiLayoutFlow` 搭）。
//
// **要修的现象**：全仓没有传送点（无物件 / 无生成器 / 无面板）⇒ 用户点不到、走不到、用不了。
//
// **数据来源（没有一个自创的目的地）**：
//   · 锚点 = `IMapModule.WaypointPoints`（罗格营地 1 个；坐标出处逐条写在 `MapGenTown.Waypoint`）；
//   · 目的地集合 = **已去过区域** − 当前区域（打开参数由 `App/AppWaypoint.cs` 组装，
//     本面板只负责画 + 把点击转成 `Events.WaypointTravelRequest`）。
//   · 字串 = **原版**（`原版资源/d2text/chi_string.txt`，繁体字形，与项目其它 UI 文本同源）：
//       索引 3988 =「傳送點」/ 3990 =「選擇你的目的地」/ 3991 =「尚未啟動其他傳送點」。
//
// **外观口径（复用既有常量，不新增颜色 / 不用自创数值）**：
//   · 底板 = 原版拼装窗框 `ResPaths.PanelBoxFrameSettings`（`SettingsPanel` 同款，
//     那里已确立"框尺寸从本屏内容外接框派生"的口径 ⇒ 本面板沿用同一做法，见 `Layout`）；
//   · 标题 = 原版位图字体 `Font24`、正文/列表 = `Font16`（`D2Text.D2Font`，中英同源）；
//   · 按钮 = 原版中等按钮（`128×35`，`UiLayoutFlow.MediumButtonOrig`）。
//
// 本文件只有一个类、且是 MonoBehaviour（`constraints.md` #1：一个 .cs 一个 MonoBehaviour）。
// 不引用任何业务模块（分层自检 ③）—— 只吃 `Diablo2.Def.WaypointArgs` + 发 `Core/Events` 的常量。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 传送面板（原版「傳送點」屏）：列出**已激活**的目的地，点一条即传送到该区域。
    /// <para>
    /// 面板**不做**"该不该允许"的判定：合法性（该区域确实是已激活的目的地 / 不是当前区域 /
    /// 地图已生成）一律由 `App/AppWaypoint.cs` 判定并点名告警 —— 本层只画与转发。
    /// </para>
    /// </summary>
    public class WaypointPanel : UIPanel
    {
        /// <summary>原版字串：面板标题（`chi_string.txt` 索引 3988）。</summary>
        public const string TitleWaypoint = "傳送點";

        /// <summary>原版字串：列表提示（索引 3990）。</summary>
        public const string HintChoose = "選擇你的目的地";

        /// <summary>原版字串：还没有任何已激活目的地（索引 3991）。</summary>
        public const string EmptyNone = "尚未啟動其他傳送點";

        /// <summary>关闭按钮文案（与 `ShopPanel` 的关闭钮同一口径：项目 UI 文本用中文）。</summary>
        public const string CloseText = "关闭";

        /// <summary>
        /// 目的地按钮（含关闭钮）的**字色** = 既有配色常量 <see cref="UiArt.TitleColor"/>
        /// （0.95/0.87/0.60，与本屏标题同色）。
        /// <para>本屏用的原版中等按钮底图是**深板岩灰**（内区实测 mean sRGB
        /// 0.376，量法 `tools/probes/measure/btn_plate_luma.py`）⇒ 当时的默认字色
        /// `UiLayoutFlow.ButtonText`（照抄 `WideButton.prefab` 的 #191919）只有 **2.79:1**
        /// （< WCAG 2.1 AA 正文门槛 4.5:1），中文密笔画糊成一块黑。本色 = **4.70:1** ✓。</para>
        /// <para>主 agent 裁决 ① 之后，**全局默认字色也已改成同一个可读值**（本屏这两处 `Create`
        /// 仍**显式**传本色：面板自己声明自己的口径，便于本屏单独调整，也让"本屏 = 不可读色"这类
        /// 回归被 uicheck 的面板级断言单独钉住）。离线门禁 = `tools/probes/hosts/uicheck`
        /// （①-b 面板级字色对比度 + 流程屏"按钮 label 对比度 ≥ 4.5:1（逐屏列数）"）。</para>
        /// </summary>
        public static readonly Color DestLabelColor = UiArt.TitleColor;

        /// <summary>
        /// 本面板能列出的**最多目的地数**。
        /// <para>
        /// 出处 = 本工程 Act I 只有 3 个区域（`AreaId`：Town / BloodMoor / DenOfEvil），
        /// 而面板只在罗格营地打开、列表又要去掉当前区域 ⇒ 最多 **2** 条。
        /// 超出的（未来加了区域却忘了排版）**不静默**：`Build` 会 Warn 并只画前 2 条。
        /// </para>
        /// </summary>
        public const int MaxDests = 2;

        // ── 布局（原版 px；全部由既有常量派生，见 `Layout` 的逐条注释）──────────────
        /// <summary>
        /// 本面板的几何（原版 px）。尺寸/坐标不许散落在 `Build` 里 —— 离线宿主
        /// （`tools/probes/hosts/uicheck`）按这些常量断言"不重叠 + 在框内"。
        /// </summary>
        internal static class Layout
        {
            /// <summary>
            /// 行节奏（原版 px）= 原版中等按钮高 35 + 间隙 10 = **45**
            /// —— 与 `UiLayoutFlow.RowStep` 同源（那里是本项目"原版行节奏"的唯一出处）。
            /// </summary>
            internal const float RowStep = 45f;

            /// <summary>内容外沿留白（原版 px）= (窗框宽 288 − 标题框宽 272) / 2 = **8**（派生自 `UiLayoutFlow.Confirm`）。</summary>
            internal const float Pad = 8f;

            /// <summary>按钮尺寸 = 原版中等按钮 128×35（`UiLayoutFlow.MediumButtonOrig`）。</summary>
            internal static Vector2 BtnSize => UiLayoutFlow.MediumButtonOrig;

            /// <summary>占满内容的行尺寸 = 标题框宽 272 × 行节奏 45。</summary>
            internal static Vector2 RowSize => new Vector2(UiLayoutFlow.Confirm.TitleSizeOrig.x, RowStep);

            /// <summary>标题行中心 y = 二次确认框标题行（原版 67.5）+ 一行节奏 = **112.5**。</summary>
            internal static float TitleY => UiLayoutFlow.Confirm.TitlePos.y + RowStep;

            /// <summary>提示行中心 y = 标题 − 一行节奏 = **67.5**。</summary>
            internal static float HintY => TitleY - RowStep;

            /// <summary>第 <paramref name="i"/> 条目的地行中心 y = 提示行 − (i+1) 行节奏。</summary>
            internal static float DestY(int i) => HintY - (i + 1) * RowStep;

            /// <summary>关闭钮行中心 y = 最后一条目的地行 − 一行节奏。</summary>
            internal static float CloseY => DestY(MaxDests - 1) - RowStep;

            /// <summary>内容外接框：上 = 标题行顶边。</summary>
            internal static float ContentTop => TitleY + UiLayoutFlow.Confirm.TitleSizeOrig.y / 2f;

            /// <summary>内容外接框：下 = 关闭钮底边。</summary>
            internal static float ContentBottom => CloseY - BtnSize.y / 2f;

            /// <summary>窗框尺寸（原版 px）= 内容外接框 + 上下各 <see cref="Pad"/>。</summary>
            internal static Vector2 BoxSizeOrig =>
                new Vector2(UiLayoutFlow.Confirm.BoxSizeOrig.x, ContentTop - ContentBottom + 2f * Pad);

            /// <summary>窗框中心（原版 px）= 内容外接框中点。</summary>
            internal static Vector2 BoxPosOrig =>
                new Vector2(0f, (ContentTop + ContentBottom) / 2f);
        }

        private bool _built;
        private UiLayoutFlow.FlowLabel _title;
        private UiLayoutFlow.FlowLabel _hint;
        private UiLayoutFlow.FlowLabel _empty;
        private UiLayoutFlow.FlowButton _close;
        private readonly List<UiLayoutFlow.FlowButton> _rows = new List<UiLayoutFlow.FlowButton>();

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            var args = param as WaypointArgs;
            if (args == null)
            {
                // 非预期：打开参数不对 ⇒ 点名（不静默），并按"空列表"画。
                UiLog.Warn($"{nameof(WaypointPanel)}.OnOpen 收到的参数不是 {nameof(WaypointArgs)}"
                    + $"（{(param == null ? "null" : param.GetType().Name)}）⇒ 按空列表显示");
                args = new WaypointArgs();
            }

            Apply(args);
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            UiLog.Info("传送面板已关闭");
        }

        // ── 纯函数（离线宿主 `uicheck` 逐项断言）────────────────────────────────

        /// <summary>
        /// 由"已去过区域集合"算出面板要列的目的地（**纯函数**，离线可断言）。
        /// <para>
        /// ① 只含 `Def.AreaId` 里登记过的区域（不出现表外的号）；② 去掉**当前区域**（在那儿点自己
        /// 没有意义 —— 原版列表也不列当前那一个）；③ 顺序 = `AreaId` 枚举序（稳定，可断言）。
        /// </para>
        /// </summary>
        /// <param name="visitedAreas">已去过区域的 `(int)AreaId` 集合（可为 null = 一个都没去过）。</param>
        /// <param name="currentArea">当前区域 `(int)AreaId`。</param>
        public static List<WaypointDest> PlanDests(IEnumerable<int> visitedAreas, int currentArea)
        {
            var res = new List<WaypointDest>();
            var seen = new HashSet<int>();
            if (visitedAreas != null)
            {
                foreach (var a in visitedAreas) seen.Add(a);
            }

            foreach (AreaId a in System.Enum.GetValues(typeof(AreaId)))
            {
                var id = (int)a;
                if (!seen.Contains(id)) continue;
                if (id == currentArea) continue;
                res.Add(new WaypointDest { area = id, name = NameOf(id) });
            }
            return res;
        }

        /// <summary>
        /// 区域显示名（= 本工程 `策划/验收表.md` / `tools/ai-skill/registry.md` 里的中文区域名，
        /// 三张图各一个；表外的号返回带号占位串，绝不猜名字）。
        /// </summary>
        public static string NameOf(int area)
        {
            switch ((AreaId)area)
            {
                case AreaId.Town: return "罗格营地";
                case AreaId.BloodMoor: return "血腥荒野";
                case AreaId.DenOfEvil: return "邪恶洞穴";
                default:
                    UiLog.Warn($"传送面板：区域号 {area} 不在 AreaId 登记表里 ⇒ 显示为占位名");
                    return $"区域#{area}";
            }
        }

        // ── 构建 / 刷新 ───────────────────────────────────────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;

            // ① 整屏遮罩（与 `D2ConfirmPanel` 同档 `UiArt.Overlay`；吃掉点底层的点击）
            UiArt.FullPanel(transform, "Shade", UiArt.Overlay, true);

            // ② 屏适配容器（弹窗内容全在原版 |y| ≤ 108 内 ⇒ 系数 = 1，同 `Pause`/`Confirm`）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);

            // ③ 底板两层：框内深色底（`UiArt.PanelBg`）+ 原版拼装窗框（后建 ⇒ 压在底色上，层序同 Pause/Confirm）
            //   而贴图走 `Game.Res.LoadAsset` **异步**回调 ⇒ 面板打开后的头几帧 BoxFrame 画成一整块
            //   **纯白矩形**（实机证据：s2 巡游"开面板即截图"每次都是白块，`.ai-tmp/screenshots/pv_wp_1_panel*.png`、
            //   `uifix3_wp_tour_panel.png`；节点转储 `.ai-tmp/test/uifix3_wp_dump.txt` 证明 sprite/贴图本身
            //   都对 —— 432×348、边框不透明、内部 alpha=0，延迟一拍再截的 `uifix3_wp_panel.png` 就是
            //   视觉上"框还没到"时就是深色底板，而不是刺眼的白块）；贴图到位后 `UiArt.SetSprite`
            //   会把 color 覆写回原版亮度（白），缺图分支保留深色占位并 Warn —— 两条既有分支都不变。
            var boxSize = UiLayoutFlow.Px(Layout.BoxSizeOrig);
            var boxPos = UiLayoutFlow.Px(Layout.BoxPosOrig);
            UiArt.Panel(screen, "Box", boxSize, boxPos, UiArt.PanelBg, true);
            var boxFrame = UiArt.Art(screen, "BoxFrame", ResPaths.PanelBoxFrameSettings, boxSize, boxPos);
            if (boxFrame != null) boxFrame.color = UiArt.PanelBg;   // 在途占位 = 深色（贴图到位后由 SetSprite 覆写回白）

            // ④ 标题（原版字模 Font24）
            _title = UiLayoutFlow.FlowLabel.Create(screen, "Title", TitleWaypoint, D2Text.D2Font.Font24,
                TextAnchor.MiddleCenter, UiArt.TitleColor,
                UiLayoutFlow.Confirm.TitleSizeOrig,
                UiLayoutFlow.Px(new Vector2(0f, Layout.TitleY)));

            // ⑤ 提示行（原版字模 Font16）
            _hint = UiLayoutFlow.FlowLabel.Create(screen, "Hint", HintChoose, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                Layout.RowSize,
                UiLayoutFlow.Px(new Vector2(0f, Layout.HintY)));

            // ⑥ 空列表那一句（原版字串；有目的地时隐藏）
            _empty = UiLayoutFlow.FlowLabel.Create(screen, "Empty", EmptyNone, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor,
                Layout.RowSize,
                UiLayoutFlow.Px(new Vector2(0f, Layout.DestY(0))));

            // ⑦ 目的地按钮（最多 `MaxDests` 条；点一条 ⇒ `Events.WaypointTravelRequest`）
            //
            //   字色**显式**传 `UiArt.TitleColor`（既有常量，0.95/0.87/0.60 —— 与本屏标题同一色）。
            //   为什么不走 `FlowButton` 的默认字色：默认 = `UiLayoutFlow.ButtonText` = 原版
            //   `WideButton.prefab` 的 #191919（0.098 近黑），那是给**浅色**石牌的；而本屏用的原版
            //   中等按钮底图 `Menu/btn_med_normal.png` 是**深板岩灰**（内区实测平均 sRGB 亮度 0.376，
            //   量法 `tools/probes/measure/btn_plate_luma.py`）⇒ 近黑压在它上面只有 **2.79:1**
            //   （WCAG 2.1 AA 正文门槛 4.5:1）⇒ 13px 的中文密笔画糊成一块黑（实机 before 图
            //   `.ai-tmp/screenshots/uifix4_z_before_btn1.png`）。换成 `UiArt.TitleColor` 后 = **4.70:1** ✓。
            //   不改 `UiLayoutFlow.ButtonText` 本身（它被 `uicheck` 的「= #191919」那条断言钉着，
            //   且拉丁按钮（Single/EXIT/OK…）细笔画压在上面仍可读）。
            //   不新造颜色：这一路只用了既有配色常量（门禁 = `uicheck` ①-b 的字色对比度断言）。
            for (var i = 0; i < MaxDests; i++)
            {
                var index = i;
                var btn = UiLayoutFlow.FlowButton.Create(screen, "Dest" + i, string.Empty,
                    Layout.BtnSize, UiLayoutFlow.Px(new Vector2(0f, Layout.DestY(i))),
                    () => Travel(index), D2Text.D2Font.Font16, DestLabelColor);
                _rows.Add(btn);
            }

            // ⑧ 关闭钮（原版中等按钮 + 原版字模标签；同上，显式给可读字色）
            _close = UiLayoutFlow.FlowButton.Create(screen, "Close", CloseText,
                Layout.BtnSize, UiLayoutFlow.Px(new Vector2(0f, Layout.CloseY)), OnCloseClicked,
                D2Text.D2Font.Font16, DestLabelColor);

            UiLog.Info($"{nameof(WaypointPanel)} 已构建：窗框 {Layout.BoxSizeOrig.x:0.#}×{Layout.BoxSizeOrig.y:0.#} 原版px"
                + $"（内容外接框派生）+ 原版中等按钮；行节奏 {Layout.RowStep:0.#}，最多 {MaxDests} 条目的地");
        }

        /// <summary>把打开参数写到界面上（列表为空 ⇒ 显示原版那句「尚未啟動其他傳送點」）。</summary>
        private void Apply(WaypointArgs args)
        {
            var dests = args.dests;
            var count = dests == null ? 0 : dests.Count;

            if (count > MaxDests)
            {
                UiLog.Warn($"传送面板：已激活目的地 {count} 条 > 本屏排版上限 {MaxDests} 条"
                    + " ⇒ 只显示前 " + MaxDests + " 条（加区域时请一并排版本面板）");
            }

            for (var i = 0; i < _rows.Count; i++)
            {
                var has = dests != null && i < dests.Count && i < MaxDests && dests[i] != null;
                var row = _rows[i];
                row.SetText(has ? dests[i].name : string.Empty);
                // 空槽位：整颗按钮隐藏 + 关掉可交互（`FlowButton` 没提供 SetInteractable，
                // 就按它的两个公开字段做 —— 与 `D2ConfirmPanel` 同层口径，不新增 API）。
                if (row.Button != null) row.Button.interactable = has;
                if (row.Image != null) row.Image.gameObject.SetActive(has);
            }

            // 空列表 ⇒ 用原版字串说明"还没有激活任何别的传送点"
            _empty.SetText(count == 0 ? (string.IsNullOrEmpty(args.empty) ? EmptyNone : args.empty)
                                      : string.Empty);

            _title.SetText(string.IsNullOrEmpty(args.title) ? TitleWaypoint : args.title);
            _hint.SetText(string.IsNullOrEmpty(args.hint) ? HintChoose : args.hint);

            _current = args;

            UiLog.Info($"传送面板：目的地 {count} 条"
                + (count > 0 ? $"（{string.Join("、", NamesOf(dests))}）" : "（显示原版「尚未啟動其他傳送點」）"));
        }

        private static string[] NamesOf(List<WaypointDest> dests)
        {
            if (dests == null) return new string[0];
            var names = new string[dests.Count];
            for (var i = 0; i < dests.Count; i++) names[i] = dests[i]?.name ?? "?";
            return names;
        }

        /// <summary>点第 <paramref name="index"/> 条目的地 ⇒ 发 `Events.WaypointTravelRequest`（唯一出口）。</summary>
        private void Travel(int index)
        {
            if (_current == null || index >= _current.dests.Count || _current.dests[index] == null)
            {
                UiLog.Warn($"传送面板：点第 {index} 条，但该槽位没有目的地 ⇒ 忽略（按钮本应置灰）");
                return;
            }

            var dest = _current.dests[index];
            UiLog.Info($"传送面板：选择目的地「{dest.name}」(area={dest.area}) ⇒ 发 `{Events.WaypointTravelRequest}`");
            Game.Event.Emit(Events.WaypointTravelRequest, dest.area);
        }

        /// <summary>关闭钮（面板自己关；合法性判定不在这里）。</summary>
        private void OnCloseClicked()
        {
            UiLog.Info($"传送面板：玩家关闭 ⇒ `Game.UI.Close<{nameof(WaypointPanel)}>()`");
            Game.UI.Close<WaypointPanel>();
        }

        /// <summary>当前这一份打开参数（`Travel` 解码目标区域用）。</summary>
        private WaypointArgs _current;
    }
}
