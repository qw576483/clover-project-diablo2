// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/QuestLogPanel.cs
// 任务日志面板（原版 Q 键）。
//
//   ① **石龛里画原版任务图**（`MENU/a{章}q{序号}.dc6` 72×86，居中于 80×95 石龛）：
//      归属依据 = **原版任务图文件名 ↔ 原版串表键名同构** —— `a{章}q{序号}` 共 **21** 个文件
//      （`a1q1..a4q3`），串表里的任务条目键正是 `qstsa{章}q{序号}…`：Act I 6 + II 6 + III 6
//      + **IV 3** = **21**（Act IV 只有 3 个任务 ⇒ 没有 `a4q4..`，也没有 `qstsa4q4..`）。
//      实证：`a1q2_0` 的图里是**一只红乌鸦**，而 `qstsa1q2` = 「修女埋骨之地」（血鸟 Blood Raven）。
//      ⇒ 本项目只做 Act I 主线 1 ⇒ 用 **`a1q1`**（对应 `qstsa1q1` = 「邪惡洞穴」）。
//      完成态 = 原版 `MENU/questdone.dc6`（21 帧 72×86，与任务图**不透明形状逐像素相同**、配色为灰）。
//   ② **正文改成"原版串表里的任务条目行"**（一条一屏，全部来自 `data/local/LNG` 的串表）：
//      `qstsa1q1` 任务名 / `qstsa1q11` / `qstsa1q12` 目标 / `qstsa1q14`+N（或 `qstsa1q140`）进度 /
//      `qstsa1q15`（可交付）· `qstsComplete`（已完成）· `noactivequest`（未接取）。
//      串 id 逐条写在本文件下方的常量注释里（**画面中文 0 条自写**）。
//
// 版面依据（**唯一依据 = 原版底图 + 原版 DC6 控件真身**）：
//   参考工程 `Diablerie/Assets/Prefabs/` **没有任务面板 prefab**（`NpcInteractions.cs` 只播问候音）
//   ⇒ 分区由 `Panel/quest_back.png`（原版 `MENU/questbackground.dc6`，320×432）**逐行扫金框**实测得出
//   （y = 0 / 28 / 230 / 252 / 383 / 430；黑芯 x 2..317 / y 254..382）。逐条出处写在
//   `UI/UiLayoutGame.cs` §⑥。
//
// 行距口径（**有出处**）：原版文本是运行时文字，但 libd2 有排版函数的逐行移植
//   （`packages/formats/src/font.zig:141-146` 行高 = 最高字形 / `:239-259` 每行 `baseline += line_height`、
//   左对齐 / `:188-216` `D2WINTEXTBOX_WordWrapAndSetText @0x4fcda0` 换行规则）
//   ⇒ 本项目**不给魔数行距**，交给 `UI/D2Text` 按原版字模算。
//
// 数据：只吃 `Diablo2.Def.QuestStateDto`（`OnOpen` 参数 + `Events.QuestChanged`）：
//     questId / name / state / progress / required / rewardClaimed / objective
//   「邪恶洞穴」的进度语义（`Module/Contracts.cs` 字段注释）：
//     progress = 已清怪数、required = 洞内初始怪物总数 ⇒ **剩余怪物数 = required − progress**
//     （原版也是这么做：清光才允许交付；硬条件由 `IQuestModule.CanTurnInDen` 把关）。
//   进度行**只有这一个数字来源**（`Remaining()` = required − progress）⇒ 不可能与别的行自相矛盾
// 事件：只**发** `Events.PanelToggleRequest`（页签点击提示"未实装"路径不发热键）；
//   只**收** `Events.QuestChanged`（刷新）与 `Events.QuestCompleted`（提示）。
//   不在这里发 `QuestAcceptRequest` / `QuestTurnInRequest` —— 那是 NPC 对话面板的职责（见 ①-④）。
// 零 `using Diablo2.Module`（分层自检 ③）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 任务日志面板。层：<see cref="UILayer.Normal"/>。
    /// <para>本屏层 = <see cref="UILayer.Normal"/>：`Popup` 层会让引擎在 Popup 层插**全屏模态
    /// 遮罩**（`clover-client-unity-engine/Runtime/Presentation/UI.cs:155-159` → `:443-461` `ShowMask()`，
    /// `raycastTarget = true`），遮罩把 `Normal`（HUD）整个盖住且吃射线 ⇒ 纯鼠标玩家点不到 HUD 的任何入口。
    /// 本屏出口 = **右下角关闭钮**（见 `BuildCloseButton`）+ Q 键 / HUD「任務記錄」按钮。
    /// 原版没有模态遮罩（靠 Q 键 / 小面板按钮开合）⇒ 按同一口径取 `Normal` 层 —— 先例 = `UI/NpcDialogPanel.cs`。
    /// 引擎的 `CloseMutexPanels()` 只关 `Layer == Popup` 的面板 ⇒ 同族互斥改由 HUD 入口显式补
    /// （`UI/HudPanel.cs` 的 `CloseScreenFamily`，注释里有"为什么必须补"）。</para>
    /// </summary>
    public class QuestLogPanel : UIPanel
    {
        // ═════════════════════════════════════════════════════════════════════
        // 画面文案 —— **全部来自原版串表**（`原版资源/d2text/chi_string.txt`，格式 `id<TAB>文本`）
        // 键名对照：`原版资源/参考工程_Diablierie/Diablerie/Assets/StreamingAssets/data/local/string.txt`
        // 复跑口径：`Get-Content 原版资源\d2text\chi_string.txt -Encoding UTF8 | Select-String '^3738\t'`
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>未接取/没有任务时的整屏文案（原版串 **3723** = 键 `noactivequest`）。</summary>
        public const string TextNoActiveQuest = "沒有進行中的任務。";

        /// <summary>进度行前缀（原版串 **3738** = 键 `qstsa1q14`，原文含全角冒号）⇒ 行文 = 前缀 + 剩余数。</summary>
        public const string TextMonstersRemainingPrefix = "剩下的怪物：";

        /// <summary>只剩 1 只时的整行（原版串 **3739** = 键 `qstsa1q140`）。</summary>
        public const string TextOneMonsterLeft = "還有一個怪物。";

        // ═════════════════════════════════════════════════════════════════════
        // 布局（**全部 = 原版 `questbackground.dc6` 实测分区 → ×1.8**，见 `UiLayoutGame` §⑥）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>面板尺寸（原版 320×432 → ×1.8 = 576×777.6）。</summary>
        public static readonly Vector2 PanelSize = UiLayoutGame.QuestPanelSize;

        /// <summary>面板中心（原版无 prefab ⇒ 按"800×600 画布居中"口径取画布中心）。</summary>
        public static readonly Vector2 PanelPos = UiLayoutGame.QuestPanelPos;

        /// <summary>每章任务格数（原版 Act I 6 个任务 ⇒ 3 列 × 2 行）。</summary>
        public const int SlotCount = UiLayoutGame.QuestSlotCount;

        // ═════════════════════════════════════════════════════════════════════
        // 纯函数（**离线自检宿主 `uicheck` 逐条断言**）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>是否可交付（纯函数：状态机口径 + "进度已满但状态还没翻"的边界）。</summary>
        public static bool CanTurnIn(QuestStateDto quest)
        {
            if (quest == null) return false;
            return quest.state == QuestState.ReadyToTurnIn
                   || (quest.state == QuestState.InProgress && quest.required > 0 && quest.progress >= quest.required);
        }

        /// <summary>剩余怪物数（进度语义：required - progress；数据缺失 ⇒ 0）。</summary>
        public static int Remaining(QuestStateDto quest)
        {
            if (quest == null) return 0;
            var left = quest.required - quest.progress;
            return left > 0 ? left : 0;
        }

        /// <summary>
        /// 任务 → **第几号石龛**（0..5）。
        /// <para>
        /// 口径：原版第一章（Act I）的 6 个任务按 `quests.txt` 顺序占 6 个石龛，
        /// 本项目 `Def.QuestId` 的取值就是这条顺序（`DenOfEvil = 1` = 第一章第 1 个任务）。
        /// ⇒ `slot = questId − 1`。越界（本项目的任务范围边界之外）返回 −1 并限频告警，
        /// **不"就近塞进第 0 格"**（那会把未知任务画成"邪恶洞穴"，属于静默错误）。
        /// </para>
        /// </summary>
        public static int SlotOf(int questId)
        {
            var slot = questId - 1;
            if (questId <= 0 || slot >= SlotCount)
            {
                UiLog.WarnThrottled("quest.slot.range." + questId,
                    $"任务 id={questId} 不在第一章 6 个任务的范围内（本项目当前只做主线任务 1「邪恶洞穴」）"
                    + " ⇒ 该任务不进任务格网格（面板其余格子保持原版的空石龛）");
                return -1;
            }
            return slot;
        }

        /// <summary>
        /// 任务 id → **原版任务图文件名**（`a{章}q{章内序号}`）。
        /// <para>
        /// 出处（**同构 + 计数双重对账**）：
        /// ① 原版 `MENU/` 下正好是 `a1q1..a4q3` **21 个** `.dc6`（`Get-ChildItem` 实测）；
        /// ② 原版串表的任务条目键是 `qstsa{章}q{序号}…`，Act I 6 + II 6 + III 6 + **IV 3** = **21**；
        /// ③ 实证语义：`a1q2` 的图画的是**红乌鸦**，对应 `qstsa1q2` =「修女埋骨之地」= 血鸟那一章任务。
        /// ⇒ 每章 6 个、Act IV 只有 3 个；越界返回 null 并告警（**不猜**）。
        /// </para>
        /// </summary>
        public static string QuestArtFileOf(int questId)
        {
            if (questId < 1)
            {
                UiLog.WarnThrottled("quest.art.range." + questId, $"任务 id={questId} 不是合法任务 ⇒ 不画任务图");
                return null;
            }
            var act = (questId - 1) / 6 + 1;
            var index = (questId - 1) % 6 + 1;
            // 原版任务图只有 a1q1..a4q3：Act IV 只做到 q3（串表 qstsa4q1..q3 同）
            if (act > 4 || (act == 4 && index > 3))
            {
                UiLog.WarnThrottled("quest.art.range." + questId,
                    $"任务 id={questId} ⇒ a{act}q{index} 超出原版任务图范围（原版只有 a1q1..a4q3 共 21 张）"
                    + " ⇒ 不画任务图（保持石龛原样）");
                return null;
            }
            return "a" + act + "q" + index;
        }

        /// <summary>
        /// 某状态下石龛里画哪张**原版任务图**（返回资源路径；`null` = 不画，保持原版空石龛）。
        /// <para>
        ///   未接取 → `null`（原版空石龛）／进行中、可交付 → `a{章}q{序号}` 帧 0／已完成 → `questdone` 帧 0。
        /// </para>
        /// </summary>
        public static string SlotArtPathOf(int questId, QuestState state)
        {
            switch (state)
            {
                case QuestState.NotStarted:
                    return null;                                  // 原版：未接取 = 空石龛
                case QuestState.InProgress:
                case QuestState.ReadyToTurnIn:
                {
                    var file = QuestArtFileOf(questId);
                    return file == null ? null : ResPaths.QuestImage(file, 0);
                }
                case QuestState.Done:
                    return ResPaths.Frame(ResPaths.PanelQuestDone, 0);   // 原版 `questdone.dc6` 帧 0
                default:
                    UiLog.WarnOnce("quest.art.state." + (int)state,
                        $"未知任务状态 {(int)state} ⇒ 不画任务图（保持空石龛）");
                    return null;
            }
        }

        /// <summary>
        /// 石龛用哪一帧：`0` = 银灰常态、`1` = **金框高亮**（可交付）。
        /// <para>实测依据：`questsocket_0/_1` 的**不透明形状逐像素完全相同（alpha 差异 = 0 像素）**，
        /// 只有边框材质不同（银灰 / 金）⇒ 天然是"常态 / 高亮"一对（见 `UI对照.md` §⑥）。</para>
        /// </summary>
        public static int SocketFrameOf(QuestStateDto quest) => CanTurnIn(quest) ? 1 : 0;

        /// <summary>
        /// 正文区整屏文案（**一行一条，全部来自原版串**；行距由 `UI/D2Text` 按原版字模算）。
        /// <para>
        /// 行序 = 原版任务日志"任务条目"的写法：任务名 → 目标行 → 进度行。
        ///  · 未接取 → `noactivequest`（**3723**）
        ///  · 进行中 → 任务名（Module 给，= **3714**）+ 目标（Module 给，= **3735**/**3736**）
        ///             + 进度行（**3738**+剩余数，只剩 1 只时用 **3739**）
        ///  · 可交付 → 任务名 + 目标（Module 给，= **3740**）
        ///  · 已完成 → 任务名 + 目标（Module 给，= **3726**）
        /// </para>
        /// </summary>
        public static string TextOf(QuestStateDto quest)
        {
            if (quest == null || quest.state == QuestState.NotStarted) return TextNoActiveQuest;

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(quest.name)) sb.Append(quest.name);
            if (!string.IsNullOrEmpty(quest.objective))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(quest.objective);
            }
            if (quest.state == QuestState.InProgress && quest.required > 0)
            {
                var left = Remaining(quest);
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(left == 1 ? TextOneMonsterLeft : TextMonstersRemainingPrefix + left);
            }
            return sb.ToString();
        }

        // ═════════════════════════════════════════════════════════════════════
        // 运行时
        // ═════════════════════════════════════════════════════════════════════
        private sealed class Slot
        {
            public Image socket;         // 拱形石龛（原版 `questsocket_*`，80×95）
            public Image art;            // 任务图（原版 `a{章}q{序号}` / `questdone`，72×86）
            public string socketPath;    // 已贴的石龛路径（避免每次 Rebuild 重复发起异步加载）
            public string artPath;       // 已贴的任务图路径
        }

        private bool _built;
        private bool _subscribed;

        /// <summary>已收到的任务（`questId` → DTO）。`Events.QuestChanged` 是**逐个任务**派发的。</summary>
        private readonly Dictionary<int, QuestStateDto> _quests = new Dictionary<int, QuestStateDto>();

        private readonly Slot[] _slots = new Slot[SlotCount];

        private Text _text;

        /// <summary>关闭钮的悬停提示（`OnClose` 收起，对应参考实现的 `OnDisable`）。</summary>
        private ControlTip _closeTip;

        /// <inheritdoc/>
        /// <remarks>层 = `Normal`（**无遮罩 ⇒ HUD 的「任務記錄 Q」入口可点 = 同一入口开合**；
        /// 理由/出处见类头注释与 `UI/NpcDialogPanel.cs`）。</remarks>
        public override UILayer Layer => UILayer.Normal;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Subscribe();

            var quest = UiLog.Require<QuestStateDto>(param, nameof(QuestLogPanel));
            if (quest != null) _quests[quest.questId] = quest;
            Rebuild();

            UiLog.Info($"任务日志已打开（已知任务 {_quests.Count} 个，"
                       + $"版面 = 原版 questbackground 实测分区 ×{UiLayoutGame.K}："
                       + $"章节页签 {UiLayoutGame.QuestActCount} 个 / 任务石龛 {SlotCount} 个 / "
                       + $"任务图 72×86 居中于石龛（原版 a{{章}}q{{序号}}.dc6 / questdone.dc6））");
        }

        /// <inheritdoc/>
        public override void OnClose()
        {
            Unsubscribe();
            _closeTip?.Hide();
            UiLog.Info("任务日志已关闭");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════
        private void Build()
        {
            if (_built) return;
            _built = true;

            // ── 底图：原版 `MENU/questbackground.dc6`（320×432 → ×1.8 = 576×777.6，1:1 不拉伸）──
            //   本图却是**面板矩形**（576×777.6，非满屏）⇒ 与 `ShopPanel.BuySellBg`（true）同口径。
            //   不吃射线的后果：点面板内部空白 → `IsPointerOverGameObject()` 为 false →
            //   `InputReader.UiEatsIntent` 判不成"点 UI" → 反投影成"点地面" ⇒ 角色走动。
            UiArt.Art(transform, "QuestBg", ResPaths.PanelQuestBack, PanelSize, PanelPos, true);

            // ── 标题条：原版中文「任務」（`data/local/ui/chi/quests.dc6` = 74×54，位图实测逐字「任務」）。
            //    页签行与石纹区都已排满 ⇒ 贴**面板顶沿、整条在面板上方**（原版无坐标出处，见 E16）。──
            UiArt.Banner(transform, "QuestBanner", ResPaths.Banner("quests_0"),
                UiLayoutGame.QuestBannerBox, new Vector2(0f, UiLayoutGame.QuestBannerY));

            BuildActTabs();
            BuildSlots();
            BuildTextBox();
            BuildCloseButton();
        }

        /// <summary>
        /// 关闭钮（面板右下角）：命中区 = <see cref="UiLayoutGame.QuestClosePos"/>（= 底图右下角
        /// 雕出的**靠右**那个方槽的内芯，口径与依据写在该常量上），尺寸 = <see cref="UiLayoutGame.CharCloseSize"/>
        /// （与该槽内芯实测 32×31 原版px 同值）。
        /// <para>图形 = 原版方钮的「关闭 / 取消」帧（`PANEL/buysellbtn.DC6` 帧 10 常态 / 11 按下，
        /// 见 `Core/ResPaths.cs` 的 `BuySellButtonFrameClose`）；底板**透明** ⇒ 露出版图自带的凹槽，
        /// 不另贴板（与背包关闭钮同一处置）。</para>
        /// <para>点它走 `Events.PanelToggleRequest`（与按 Q / HUD 小面板的「任務記錄」同一入口）。</para>
        /// </summary>
        private void BuildCloseButton()
        {
            var close = UiArt.Panel(transform, "CloseButton", UiLayoutGame.CharCloseSize,
                PanelPos + UiLayoutGame.QuestClosePos, new Color(1f, 1f, 1f, 0f), true);
            close.preserveAspect = true;

            var button = close.gameObject.AddComponent<Button>();
            button.targetGraphic = close;
            UiArt.ApplyCloseButtonArt(close, button);

            _closeTip = ControlTip.Create(transform, close.rectTransform, WaypointPanel.CloseText);
            var hover = close.gameObject.AddComponent<HoverTarget>();
            if (_closeTip != null)
            {
                hover.OnEnter = _closeTip.Show;
                hover.OnExit = _closeTip.Hide;
            }

            button.onClick.AddListener(() =>
            {
                UiLog.Info("点任务日志关闭按钮 ⇒ 走 `Events.PanelToggleRequest` 关闭（与按 Q 同一条路径）");
                Game.Event.Emit(Events.PanelToggleRequest, nameof(QuestLogPanel));
            });
        }

        /// <summary>
        /// 顶部**章节页签行**（原版 y 0..28 那条窄带；`questtab_*` 4 帧 78×30）。
        /// <para>
        /// 帧序（实测读图 `Panel/questtab_0..7.png`）：位图里逐字是罗马数字
        /// **I / II / III / IV**，**每章两帧**——帧 `2n` = 常态（平），帧 `2n+1` = 选中（金框凸起）。
        /// ⇒ Act I 用帧 1（选中）、Act II/III/IV 用帧 2/4/6（常态）。
        /// </para>
        /// <para>
        /// 本项目只做第一章 ⇒ 点其它章 = 未实装提示（与主菜单 `MULTIPLAYER` 的处置一致），
        /// **不静默**（留 `UiLog.Info`）。
        /// </para>
        /// </summary>
        private void BuildActTabs()
        {
            for (var a = 0; a < UiLayoutGame.QuestActCount; a++)
            {
                var act = a;
                // Act I（index 0）= 当前章节 ⇒ 用"选中"帧 1；其余用各自常态帧 2n。
                var frame = a == 0 ? 1 : a * 2;
                var img = UiArt.Art(transform, "ActTab" + a,
                    ResPaths.PanelQuestTabs + "_" + frame,
                    UiLayoutGame.QuestTabSize,
                    new Vector2(UiLayoutGame.QuestTabX(a), UiLayoutGame.QuestTabY), true);

                var btn = img.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => OnActClicked(act));
            }
        }

        /// <summary>章节页签点击：第一章 = 当前章（无操作）；其余章 = 未实装提示。</summary>
        private void OnActClicked(int act)
        {
            if (act == 0)
            {
                UiLog.Info("点任务日志「Act I」页签 ⇒ 已是当前章节，无操作（原版也不切页）");
                return;
            }

            UiLog.Info($"点任务日志 Act {act + 1} 页签 ⇒ 本项目范围边界只做第一章（Act I）⇒ 提示「未实装」");
            Game.UI.Toast($"第 {act + 1} 章未实装");
        }

        /// <summary>
        /// 石纹区里的 **3×2 任务格**：原版一层 `questsocket_*`（80×95 拱形石龛框）
        /// + 一层**原版任务图**（72×86，`a{章}q{序号}` / `questdone`，居中 ⇒ 偏移 0）。
        /// <para>这里**不画任务名**：石纹区净高 200、2×95 已占满，名字必然压在下一行石龛上（实测读图确认过）。</para>
        /// </summary>
        private void BuildSlots()
        {
            for (var i = 0; i < SlotCount; i++)
            {
                var center = new Vector2(UiLayoutGame.QuestSlotX(i % UiLayoutGame.QuestSlotCols),
                    UiLayoutGame.QuestSlotY(i / UiLayoutGame.QuestSlotCols));

                var socket = UiArt.Art(transform, "Slot" + i,
                    ResPaths.PanelQuestSocket + "_0", UiLayoutGame.QuestSlotSize, center);

                var art = UiArt.Panel(socket.transform, "Art", UiLayoutGame.QuestArtSize,
                    UiLayoutGame.QuestArtPos, Color.white, false);
                art.preserveAspect = true;      // 原版像素画：按比例内缩，绝不拉变形
                art.gameObject.SetActive(false);

                _slots[i] = new Slot
                {
                    socket = socket,
                    art = art,
                    socketPath = ResPaths.PanelQuestSocket + "_0",
                };
            }
        }

        /// <summary>
        /// **正文区**（原版实测黑芯 x 2..317 / y 254..382 = 316×129 原版px → ×1.8）。
        /// <para>
        /// 一个文本框画完整屏文案（**一行一条**）：行距 = 该字号下**原版字模行高**
        /// （`UI/D2Text` 按 `font{N}_chi_map.txt` 的 `advance/行距` 算），左对齐；
        /// `UpperLeft` + 框顶沿 = 文本起点。
        /// </para>
        /// </summary>
        private void BuildTextBox()
        {
            _text = UiArt.Label(transform, "QuestText", string.Empty, UiLayoutGame.QuestTextFont,
                TextAnchor.UpperLeft, UiArt.TextColor,
                new Vector2(UiLayoutGame.QuestTextWidth, UiLayoutGame.QuestTextH),
                UiLayoutGame.QuestTextPos);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 刷新
        // ═════════════════════════════════════════════════════════════════════
        private void Rebuild()
        {
            if (!_built)
            {
                UiLog.Warn("任务日志尚未构建就收到刷新 ⇒ 忽略");
                return;
            }

            RefreshSlots();
            RefreshTextBox();
        }

        /// <summary>把已知任务画进各自的石龛（未收到的格子保持原版的**空石龛**）。</summary>
        private void RefreshSlots()
        {
            for (var i = 0; i < SlotCount; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;

                QuestStateDto quest = null;
                foreach (var kv in _quests)
                {
                    if (SlotOf(kv.Key) == i) { quest = kv.Value; break; }
                }

                if (quest == null)
                {
                    // 未收到的格子 = 原版的**空石龛**（只有拱形框，不画任务图）
                    slot.art.gameObject.SetActive(false);
                    SetSpriteIfChanged(slot, "Socket", ResPaths.PanelQuestSocket + "_0");
                    continue;
                }

                var artPath = SlotArtPathOf(quest.questId, quest.state);
                if (string.IsNullOrEmpty(artPath))
                {
                    slot.art.gameObject.SetActive(false);
                }
                else
                {
                    slot.art.gameObject.SetActive(true);
                    SetSpriteIfChanged(slot, "Art", artPath);
                }

                SetSpriteIfChanged(slot, "Socket",
                    ResPaths.PanelQuestSocket + "_" + SocketFrameOf(quest));
            }
        }

        /// <summary>
        /// 贴图去重：只有路径**变了**才发起一次异步加载。
        /// <para>为什么必须去重：`Rebuild` 会被每次 `Events.QuestChanged` 触发，
        /// 不去重就会每次重新加载同一张图（白跑 + 覆盖刚贴好的图，见 `InventoryPanel` 的同一处置）。</para>
        /// </summary>
        private void SetSpriteIfChanged(Slot slot, string which, string path)
        {
            if (slot == null || string.IsNullOrEmpty(path)) return;

            if (which == "Art")
            {
                if (slot.artPath == path) return;
                slot.artPath = path;
                UiArt.SetSprite(slot.art, path, UiArt.ArtFullBright);
                return;
            }

            if (slot.socketPath == path) return;
            slot.socketPath = path;
            UiArt.SetSprite(slot.socket, path, UiArt.ArtFullBright);
        }

        /// <summary>刷新正文区（整屏文案见 <see cref="TextOf"/>；**一行一条，全部原版串**）。</summary>
        private void RefreshTextBox()
        {
            // 只显示"第一章任务里号最小的那个"（本项目范围内恒为「邪恶洞穴」）；
            // 多任务同时存在时以 id 最小者为主视角，其余仅在石龛里显示，不在这里堆文字。
            QuestStateDto quest = null;
            foreach (var kv in _quests)
                if (quest == null || kv.Key < quest.questId) quest = kv.Value;

            if (quest == null)
            {
                UiLog.WarnOnce("quest.no.data",
                    "任务日志未收到任何 `QuestStateDto` ⇒ 正文显示原版「沒有進行中的任務。」；"
                    + "请 Quest 模块在接取/推进时 emit `Events.QuestChanged`");
            }

            _text.text = TextOf(quest);
            if (quest != null)
            {
                UiLog.Info($"任务日志正文（原版串）：state={quest.state} progress={quest.progress}/{quest.required} "
                           + $"剩余={Remaining(quest)} ⇒ 文本=\"{_text.text.Replace("\n", " | ")}\"");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 事件
        // ═════════════════════════════════════════════════════════════════════
        private void Subscribe()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<QuestStateDto>(Events.QuestChanged, OnQuestChanged);
            Game.Event.On<int>(Events.QuestCompleted, OnQuestCompleted);
            // 交付被拒的提示**留在这里**：本面板不再有「交付任务」按钮，但 `QuestModule.TurnInDen()`
            // 的拒绝事件（`Events.QuestTurnInDenied`）仍需要有人给玩家回馈（本面板是它的唯一订阅者，
            // 见 `Module/Quest/QuestModule.cs:156`）⇒ 不许顺手删掉这个订阅（= "定义了但没人用"）。
            Game.Event.On<int>(Events.QuestTurnInDenied, OnTurnInDenied);
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<QuestStateDto>(Events.QuestChanged, OnQuestChanged);
            Game.Event.Off<int>(Events.QuestCompleted, OnQuestCompleted);
            Game.Event.Off<int>(Events.QuestTurnInDenied, OnTurnInDenied);
        }

        private void OnQuestChanged(QuestStateDto quest)
        {
            if (quest == null)
            {
                UiLog.Warn("收到任务状态事件但参数为 null ⇒ 忽略");
                return;
            }
            _quests[quest.questId] = quest;
            Rebuild();
        }

        private void OnQuestCompleted(int questId)
        {
            UiLog.Info($"任务 {questId} 已完成（奖励发放由 Quest 模块负责；面板随下一次 QuestChanged 刷新）");
            Game.UI.Toast("任务完成！");
        }

        private void OnTurnInDenied(int questId)
        {
            UiLog.Info($"任务 {questId} 交付被拒（未达成条件）⇒ 提示玩家");
            Game.UI.Toast("任务条件未达成");
        }
    }
}
