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
        // 菜单项（`NpcDialogArgs.options`，最多 3 项）—— 排在下带里，逐行居中。
        //   顺序由模块给：0 = 关闭（原版串 `NPCMenuLeave`「離開」）→ 任务动作 → 商店入口。
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>菜单项尺寸（宽 = 内容行宽；高 25.5）。</summary>
        public static readonly Vector2 OptionSize = new Vector2(ContentW, 25.5f);

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

        private Text _speaker;
        private Text _body;
        private readonly List<Image> _optionButtons = new List<Image>();
        private readonly List<Text> _optionLabels = new List<Text>();

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.Popup;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var dialog = UiLog.Require<NpcDialogArgs>(param, nameof(NpcDialogPanel));
            if (dialog != null) _dialog = dialog;
            Rebuild(_dialog);

            UiLog.Info($"对话面板已打开（NPC={(dialog != null ? dialog.npcName : "无数据")}，"
                       + $"选项={_dialog?.options?.Count ?? 0}，可知接任务={_dialog?.canAcceptQuest ?? false}，"
                       + $"可交任务={_dialog?.canTurnInQuest ?? false}）");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            UiLog.Info("对话面板已关闭");
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
        }

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
                var button = UiArt.Button(transform, "Option" + i, string.Empty, OptionSize,
                    new Vector2(ContentCx, OptionY(i)), null);
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
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.On(Events.DialogClose, OnDialogCloseEvent);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
            Game.Event.Off(Events.DialogClose, OnDialogCloseEvent);
        }

        private void OnDialogOpen(NpcDialogArgs args)
        {
            if (args == null)
            {
                UiLog.Warn("收到对话事件但参数为 null ⇒ 忽略");
                return;
            }
            _dialog = args;
            Rebuild(_dialog);
        }

        /// <summary>模块侧关闭对话 ⇒ 面板跟着关（不会再发 DialogClose，避免回环）。</summary>
        private void OnDialogCloseEvent()
        {
            if (!Game.UI.IsOpen<NpcDialogPanel>()) return;
            UiLog.Info("收到 `Events.DialogClose` ⇒ 关闭对话面板");
            Game.UI.Close<NpcDialogPanel>();
        }
    }
}
