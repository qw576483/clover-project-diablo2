// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/EnemyBarView.cs   u44（悬停选择表现 · 契约 C3 + C4 + C5）
//
// 本文件 = **鼠标悬停到"人/怪"时的选择表现**，1:1 对齐参考实现 `MouseSelection.cs` 的**显示那一半**
// （另一半"命中判定"本工程已有 = `Module/Input/HoverPicker`，本文件不重造）：
//
//   参考实现 `MouseSelection.cs:53-80` 的分派（逐条照搬，不扩）：
//     · `unit.monStat.interact`            ⇒ `ShowLabel(entity)`     = **NPC 名字牌**（C5）
//     · else `unit.monStat.killable`       ⇒ `ShowEnemyBar(unit)`    = **屏幕顶部血条**（C3）
//     · else / 不是 Unit                    ⇒ `ShowNothing()`
//     · 本工程的可交互口径 = `Def.CursorKind`：`Interact` ⇒ NPC 名字牌；`Attack` ⇒ 顶部血条。
//       （发送方 `Module/Input/HoverPicker.Resolve`：NPC ⇒ `CursorKind.Interact` /
//         怪物 ⇒ `CursorKind.Attack` / 地面物品 ⇒ `Pickup`——地面物品名另有 `UI/GroundItemLabelView`）
//
// ── C3 · 屏幕顶部「怪名字 + 血条」——逐值出处 = 原版 `EnemyBar.prefab` + `EnemyBar.cs` ──────────
//   几何（本文件只消费 `UiLayoutGame.EnemyBar*`，不在这里写 150/20/22 这类原版数）：
//     根    anchor(0.5,1) pos(0,−22) size **150×20** pivot(0.5,1)   → `EnemyBar.prefab:387-391`
//     子    Title anchor(0.5,0)-(0.5,1) pos(0,2) sizeDelta(200,−4) → `EnemyBar.prefab:313-317`
//     孙    Slider 铺满；`m_MinValue 0`（`m_MaxValue 100` 是作者值，运行期被覆盖）→ `:198-201`
//   颜色（**纯 uGUI 绘制，原版没有位图**：两处 `m_Sprite = {fileID: 0}`）：
//     底色 `RGBA(0, 0, 0, 0.478)`                          → `EnemyBar.prefab:246`
//     填充 `RGBA(0.75, 0.022058845, 0.022058845, 0.2509804)` → `EnemyBar.prefab:219`
//   文案/字体（Title 的 Text 组件 `:112-144`）：
//     `m_Color (1,1,1,1)` · `m_Alignment 7`（= LowerCenter） · `m_RichText 0`
//     `m_Font` guid `1f4ed3b918a4a9c4eb213d3a50427325` = **font16**
//   运行期（`EnemyBar.cs:26-35`）：
//     `title.text = unit.title` · `slider.maxValue = unit.maxHealth` · `slider.value = unit.health`
//     `slider.gameObject.SetActive(unit != null)` ⇒ **不悬停就不存在**（本文件同样整根 active=false）
//
// ── C4 · 头顶那份已删 ────────────────────────────────────────────────────────
//   改动前：`UI/EntityTooltip.cs`（U4 新增）把「名字 + 血量」画在**怪物头顶**（世界坐标投影），
//   自陈"原版无载体"。原版对可击杀怪**只有顶部条**（`MouseSelection.cs:62-65` ⇒ `ShowEnemyBar`）
//   **不许两份并存**。判据里的反向断言（`uicheck` 的 HoverSelectCheck）会钉住这件事。
//   另一处"头顶血量"是引擎件 `CloverEngine.WorldHpBar`（`ViewModule` 建、**无名字**、
//     `EntityView.Bar` 字段与 `ViewModule` 里 4 处调用点全部移除 ⇒ **本文件现在承担全部"怪血量显示"**。
//
// ── C5 · NPC 名字牌（世界内、该实体上方）——出处 = 参考实现的三段拼起来 ─────────
//   ① 触发与分派：`MouseSelection.cs:58-61`（`monStat.interact` ⇒ `ShowLabel`）
//   ② 位置：`MouseSelection.cs:120-125`：`entity.transform.position + (Vector3)entity.titleOffset
//      / Iso.pixelsPerUnit`；`Unit.cs:496-505` 里 NPC 分支 = `(0, monStat.ext.pixHeight)`
//      ⇒ **偏移 = pixHeight / 80**（`Iso.cs:9` `pixelsPerUnit = 80`；本项目同尺度 =
//        `SpriteFrames.ArtPixelsPerUnit = 80`，见 `Module/View/SpriteFrames.cs:95-98`）。
//      `MonStats2.txt` 的 `pixHeight` 列 ⇒ 5 个城镇 NPC **全部 = 80**（按 `MonStats.txt` 的
//      `Code ∈ {PS,RC,CI,GH,WA}` 取 `Id` = `akara/kashya/charsi/gheed/warriv1` 后逐行查出）
//      ⇒ 名字牌上移 **80/80 = 1.0 世界单位**（= 该精灵自己的像素高度 = 头顶）。
//      不是"抬 1.5 格"那个自陈值 —— 那个值随 `UI/EntityTooltip.cs` 一起删了。
//   ④ 载体细节（`ScreenLabel.cs:16-47`）：`pivot (0.5,0)`（**锚点在牌子的底边中点**、向上长）+
//      文字 `MiddleCenter` + `font16` + **半透明黑底** `RGBA(0,0,0,0.95)`，
//      黑底尺寸 = 文本尺寸 + padding(左6,右6,上0,下4)（`VerticalLayoutGroup.padding`）。
//      ⇒ 本文件的牌子 = 一个 Image（黑底）+ 一个 D2Label，尺寸按文本实测撑（见 `NameplateSizeFor`）。
//
//   「顶部条（背景 + 进度填充 + 标题）」「世界内名牌（跟随 + 投影 + 显隐 + 底板随字伸缩）」
//   两套机制移入引擎件 `CloverEngine.ScreenTargetBar` / `CloverEngine.WorldNameplate`
//   （`clover-client-unity-engine/Runtime/Presentation/WorldOverlayWidgets.cs`）。
//   本文件只剩：① 原版取值（几何 / 配色 / 字体 / 常量）；② 分派（`CursorKind` → 哪一件）；
//   ③ 渲染注入（把 `D2Label` 包成引擎认得的 `IOverlayLabelView`）；④ 事件订阅与日志。
//   公开 API / 调用点零改动（`BarVisible` / `TitleText` / `BarValue` / `BarMaxValue` /
//   `NameplateText` 全部保留原语义）；画布装配仍在本文件（一块画布上放两件：
//   引擎件的"一件一画布"不适用，故画布 / `CanvasScaler` 口径留在项目侧）。
//   屏幕点 ⇄ 画布局部点换算由引擎件统一走 `ScreenPointUtil`。
//
// ── 结构（一个 `.cs` 一个 MonoBehaviour；装配方式沿用同项目先例 `UI/CursorView`）────
//   自安装常驻画布（`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` + `DontDestroyOnLoad`）
//   ⇒ 不必先跑一次编辑器菜单生成预制体。画布口径与 `UiLayoutGame.CursorCanvasRef` 一致（1920×1080）。
//
// 分层：本文件只 `using CloverEngine` / `Diablo2.Core` / `Diablo2.Def` / UnityEngine(.UI)
//   —— **不许** `using Diablo2.Module`（分层自检 ③）；血量走 `Events.MonsterSpawned/Changed`
// 不碰光标（原版光标只由"手里的物品"驱动，与悬停无关，见 `MouseSelection.cs` 全文 0 处光标调用）、
//   不加音效（原版悬停无声，`MouseSelection.cs` 全文无 `AudioManager`）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 悬停选择表现：屏幕顶部怪名字+血条（原版 `EnemyBar`）+ NPC 世界内名字牌（原版 `ShowLabel`）。
    /// 见文件头（契约 C3/C4/C5 逐条出处）。
    /// </summary>
    public class EnemyBarView : MonoBehaviour
    {
        // ═════════════════════════════════════════════════════════════════════
        // 原版常量（出处见文件头；全部照抄，不许调数值）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>常驻画布的 `sortingOrder`（与 `UI/CursorView` / 旧 `EntityTooltip` 同档）。</summary>
        private const int CanvasSortingOrder = 1;

        /// <summary>顶部条底色 —— 原版 `EnemyBar.prefab:246` 的 `m_Color`（逐值）。</summary>
        public static readonly Color BarBackgroundColor = new Color(0f, 0f, 0f, 0.478f);

        /// <summary>顶部条填充色 —— 原版 `EnemyBar.prefab:219` 的 `m_Color`（逐值）。</summary>
        public static readonly Color BarFillColor = new Color(0.75f, 0.022058845f, 0.022058845f, 0.2509804f);

        /// <summary>Title 文字色 —— 原版 `EnemyBar.prefab:124` 的 `m_Color = (1,1,1,1)`。</summary>
        public static readonly Color TitleColor = Color.white;

        /// <summary>Title 对齐 —— 原版 `m_Alignment = 7`（uGUI 序列化值 7 = `LowerCenter`）。</summary>
        public const TextAnchor TitleAnchor = TextAnchor.LowerCenter;

        /// <summary>Title 字体 —— 原版 `m_Font` guid `1f4ed3b918a4a9c4eb213d3a50427325` = **font16**。</summary>
        /// <remarks>`internal`（不是 `public`）：`D2Text.D2Font` 本身是 internal ⇒ 公开字段会 CS0052。
        /// 离线宿主把 client 源码**编进自己程序集** ⇒ internal 在宿主里照样可断言。</remarks>
        internal const D2Text.D2Font TitleFont = D2Text.D2Font.Font16;

        /// <summary>名字牌字体 —— 参考实现 `ScreenLabel.cs:41` 的 `Fonts.GetFont16()`。</summary>
        internal const D2Text.D2Font NameplateFont = D2Text.D2Font.Font16;

        /// <summary>名字牌黑底色 —— `ScreenLabel.cs:45` 的 `new Color(0, 0, 0, 0.95f)`。</summary>
        public static readonly Color NameplateBackColor = new Color(0f, 0f, 0f, 0.95f);

        /// <summary>名字牌黑底 padding（原版 px）：左 6 / 右 6 / 上 0 / 下 4 —— `ScreenLabel.cs:29`。</summary>
        public const float NameplatePadLeft = 6f;
        public const float NameplatePadRight = 6f;
        public const float NameplatePadTop = 0f;
        public const float NameplatePadBottom = 4f;

        /// <summary>
        /// （`MonStats.txt` 的 `Code ∈ {PS,RC,CI,GH,WA}` → `Id = akara/kashya/charsi/gheed/warriv1`）。
        /// </summary>
        public const float NpcSpritePixHeight = 80f;

        /// <summary>
        /// 原版精灵的像素密度（px / 世界单位）：`Iso.cs:9` 的 `pixelsPerUnit = 80`。
        /// <para>UI 层不许引用 `Module/View/SpriteFrames.ArtPixelsPerUnit`（分层）⇒ 本文件自持一份；
        /// 两者必须相等由离线宿主机械核对（`HoverSelectCheck` 会从 `SpriteFrames.cs` 源码里读回真值比对）。</para>
        /// </summary>
        public const float ArtPixelsPerUnit = 80f;

        /// <summary>
        /// NPC 名字牌相对**格中心（= 脚底）**的上移量（世界单位）= `pixHeight / pixelsPerUnit`
        /// = 80 / 80 = **1.0**（出处见文件头 C5 的 ①~③）。
        /// </summary>
        public const float NpcTitleLiftWorld = NpcSpritePixHeight / ArtPixelsPerUnit;

        // ═════════════════════════════════════════════════════════════════════
        // 纯函数（离线宿主逐条断言；运行时也只用它们，不内联同样的算式）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>指针下是"可攻击的怪"（原版 `monStat.killable` 那一支）⇒ 出**顶部条**。</summary>
        public static bool IsEnemyBarTarget(Diablo2.Def.HoverTarget t)
            => t != null && t.hasTarget && t.cursor == CursorKind.Attack && t.id >= 0;

        /// <summary>指针下是"可交互单位"（原版 `monStat.interact` 那一支）⇒ 出**名字牌**。</summary>
        public static bool IsNameplateTarget(Diablo2.Def.HoverTarget t)
            => t != null && t.hasTarget && t.cursor == CursorKind.Interact;

        /// <summary>`slider.value / slider.maxValue`（原版 `EnemyBar.cs:32-33` 喂的就是 hp / maxHp）。</summary>
        public static float FillAmount(int hp, int maxHp)
            => maxHp <= 0 ? 0f : Mathf.Clamp01((float)hp / maxHp);

        /// <summary>Title 文案（原版 `EnemyBar.cs:31` = `unit.title`；本工程取怪物状态的名字，空串兜底）。</summary>
        public static string TitleTextFor(MonsterState st, Diablo2.Def.HoverTarget t)
        {
            var name = st != null && !string.IsNullOrEmpty(st.name) ? st.name : (t != null ? t.name : null);
            return string.IsNullOrEmpty(name) ? "怪物 #" + (t != null ? t.id : -1) : name;
        }

        /// <summary>Title 的**局部**尺寸（相对顶部条中心；出处见文件头 C3 几何那段）。</summary>
        public static Vector2 TitleLocalSize
            => new Vector2(UiLayoutGame.EnemyBarTitleWidth,
                UiLayoutGame.EnemyBarSize.y - UiLayoutGame.EnemyBarTitleShrinkY);

        /// <summary>Title 中心相对顶部条中心的偏移（原版 `m_AnchoredPosition (0,2)` ×1.8）。</summary>
        public static Vector2 TitleLocalPos => new Vector2(0f, UiLayoutGame.EnemyBarTitleOffsetY);

        /// <summary>顶部条矩形（画布坐标；转发 `UiLayoutGame` 的纯函数，保证只有一处真源）。</summary>
        public static Rect BarRect() => UiLayoutGame.EnemyBarRect();

        /// <summary>Title 矩形（画布坐标；转发 `UiLayoutGame`）。</summary>
        public static Rect TitleRect() => UiLayoutGame.EnemyBarTitleRect();

        /// <summary>Title 矩形是否落在顶部条矩形里（几何自洽判据；虚线框超出 = 排版跑出槽）。</summary>
        public static bool TitleInsideBar() => RectContains(BarRect(), TitleRect());

        /// <summary>
        /// NPC 名字牌的黑底尺寸（**画布 px**）= 文本实测尺寸 ×1.8 后加 padding（**原版 px** ×1.8）。
        /// <para>口径来自 `ScreenLabel.cs:25-35`：`ContentSizeFitter` + `VerticalLayoutGroup.padding(6,6,0,4)`
        /// ⇒ 黑底 = 文本外接矩形 + 左右各 6、下 4（上 0）。文本尺寸用本项目位图字模量（`D2Text.MeasureNative`
        /// / `CellHeight`，与 `D2Label` 排版同一套；中文走 chi 字模）。</para>
        /// <para>本方法现在同时是喂给引擎件 `WorldNameplate` 的 `SizeMeasure` 委托 ⇒ 引擎不认识字模，
        /// 底板尺寸仍由项目侧排版口径决定（没被下沉偷走）。</para>
        /// </summary>
        public static Vector2 NameplateSizeFor(string text)
        {
            var s = text ?? string.Empty;
            var chi = !D2Text.IsLatinOnly(s);
            var scale = D2Text.ScaleFor((int)UiLayoutGame.FontPx16, chi, NameplateFont);
            var native = D2Text.MeasureNative(NameplateFont, s, chi) * scale;
            var cellH = (chi ? D2Text.ChiCellH(NameplateFont) : D2Text.CellHeight(NameplateFont)) * scale;
            var w = native + (NameplatePadLeft + NameplatePadRight) * UiLayoutGame.K;
            var h = cellH + (NameplatePadTop + NameplatePadBottom) * UiLayoutGame.K;
            return new Vector2(w, h);
        }

        /// <summary>NPC 名字牌的世界落点 = 格中心（脚底）+ (0, lift)。</summary>
        public static Vector3 NameplateWorld(int gridX, int gridY)
        {
            var w = Iso.GridToWorld(gridX, gridY);
            w.y += NpcTitleLiftWorld;
            return w;
        }

        private static bool RectContains(Rect outer, Rect inner)
            => inner.xMin >= outer.xMin - 0.01f && inner.xMax <= outer.xMax + 0.01f
            && inner.yMin >= outer.yMin - 0.01f && inner.yMax <= outer.yMax + 0.01f;

        // ═════════════════════════════════════════════════════════════════════
        // 装配（自安装常驻画布）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>当前实例（实机探针 / 自证读取用）。</summary>
        public static EnemyBarView Instance { get; private set; }

        /// <summary>新一局 Play 复位静态闸门（工程可能开了「不重载域」）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForNewPlaySession() => Instance = null;

        /// <summary>建常驻画布（与 `UI/CursorView.Install` 同一处置）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Instance != null) return;

            var go = new GameObject("[EnemyBarView]");
            Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<EnemyBarView>();
        }

        // ── 状态 ─────────────────────────────────────────────────────────────
        private RectTransform _canvasRt;

        /// <summary>引擎件：屏幕顶部目标条（背景 + 进度填充 + 标题，机制全在它里面）。</summary>
        private ScreenTargetBar _bar;

        /// <summary>引擎件：世界内名牌（投影 + 跟随 + 底板随字伸缩）。</summary>
        private WorldNameplate _plate;

        private bool _built;
        private bool _subscribed;
        private bool _stageActive;
        private int _loggedHoverId = int.MinValue;
        private string _loggedName;

        /// <summary>`Def.MonsterState` 引用表（**不拷贝**：模块原地更新同一个对象）。</summary>
        private readonly Dictionary<int, MonsterState> _states = new Dictionary<int, MonsterState>();

        /// <summary>顶部条是否可见（自证/断言用；转发引擎件）。</summary>
        public bool BarVisible => _bar != null && _bar.IsVisible;

        /// <summary>名字牌是否可见（转发引擎件）。</summary>
        public bool NameplateVisible => _plate != null && _plate.IsVisible;

        /// <summary>当前顶部条的标题文本（未显示时为空串）。</summary>
        public string TitleText => _bar != null ? _bar.Title : string.Empty;

        /// <summary>当前 slider.value（未构建 ⇒ -1）。</summary>
        public float BarValue => _bar != null ? _bar.Value : -1f;

        /// <summary>当前 slider.maxValue（未构建 ⇒ -1）。</summary>
        public float BarMaxValue => _bar != null ? _bar.MaxValue : -1f;

        /// <summary>当前名字牌文本（未显示时为空串）。</summary>
        public string NameplateText => _plate != null ? _plate.Text : string.Empty;

        private void Awake() => Build();

        private void OnDestroy()
        {
            Unsubscribe();
            if (_bar != null) _bar.Dispose();
            if (_plate != null) _plate.Dispose();
            _bar = null;
            _plate = null;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // 引擎门面（`Game.Event`）在 `Game.Launch` 之后才有 ⇒ 懒挂接（同 `UI/CursorView`）。
            if (!_subscribed) TrySubscribe();
        }

        // ── 构建 ─────────────────────────────────────────────────────────────
        private void Build()
        {
            if (_built) return;
            _built = true;

            // 画布留在项目侧：**一块画布上放两件**（引擎件是"一件一画布"的装配，这里不适用），
            // 且缩放口径要与引擎常驻画布一致（参考分辨率 / match 见 `UiLayoutGame` 的常量注释）。
            var canvasNode = UIFactory.CreateNode("EnemyBarCanvas", transform);
            _canvasRt = canvasNode;
            var canvas = canvasNode.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var scaler = canvasNode.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UiLayoutGame.CursorCanvasRef;
            scaler.matchWidthOrHeight = UiLayoutGame.CursorCanvasMatch;

            BuildTopBar(canvasNode);
            BuildNameplate(canvasNode);

            if (_bar == null || _plate == null)
            {
                UiLog.Error("[EnemyBarView] 顶部条 / 名字牌没建出来 ⇒ 本局悬停不显示选择信息");
                return;
            }

            HideAll();
        }

        /// <summary>顶部「怪名字 + 血条」：几何/颜色/字体逐值来自原版 `EnemyBar.prefab`（见文件头）。</summary>
        private void BuildTopBar(RectTransform parent)
        {
            var spec = new ScreenTargetBarSpec
            {
                NodeName = "EnemyBar",
                Size = UiLayoutGame.EnemyBarSize,
                Pos = UiLayoutGame.EnemyBarPos,
                Background = BarBackgroundColor,        // 原版 prefab:246
                Fill = BarFillColor,                    // 原版 prefab:219
                TitleSize = TitleLocalSize,             // 原版 pos(0,2)/size(200,−4) ×1.8
                TitlePos = TitleLocalPos,
                TitleNodeName = "Title",
            };

            _bar = ScreenTargetBar.Create(_canvasRt, parent, spec, CreateTitleLabel);
            if (_bar == null) return;

            UiLog.Info($"[EnemyBarView] 顶部条已建：矩形 = {BarRect()}（原版 EnemyBar.prefab 150×20 上沿距顶 22 原版px ×1.8）；"
                + $"Title = {TitleRect()}（原版 pos(0,2)/size(200,−4) ×1.8）；"
                + $"底色 {BarBackgroundColor}（prefab:246）/ 填充 {BarFillColor}（prefab:219）/ 字体 font{(int)TitleFont}（prefab `m_Font` guid → font16）"
                + $"；机制 = 引擎 ScreenTargetBar");
        }

        /// <summary>NPC 世界内名字牌（黑底 + 位图字；`pivot (0.5,0)` ⇒ 锚点在底边中点、向上长）。</summary>
        private void BuildNameplate(RectTransform parent)
        {
            var spec = new WorldNameplateSpec
            {
                NodeName = "NpcNameplate",
                BackColor = NameplateBackColor,          // 原版 `ScreenLabel.cs:45`
                LabelNodeName = "Label",
                Pivot = new Vector2(0.5f, 0f),           // 原版 `ScreenLabel.cs:24`
            };

            // 底板尺寸 = 项目侧排版口径（`NameplateSizeFor`）：引擎只负责"跟着字变"，不认识字模。
            _plate = WorldNameplate.Create(_canvasRt, parent, spec, CreatePlateLabel, NameplateSizeFor);
            if (_plate == null) return;

            UiLog.Info($"[EnemyBarView] NPC 名字牌已建：偏移 = pixHeight/pixelsPerUnit = {NpcSpritePixHeight}/{ArtPixelsPerUnit}"
                + $" = {NpcTitleLiftWorld} 世界单位（MonStats2.pixHeight 实测 80 × 5 个 NPC；Iso.pixelsPerUnit = 80）；"
                + $"字体 font{(int)NameplateFont} / 黑底 {NameplateBackColor}（ScreenLabel.cs:41/45）；机制 = 引擎 WorldNameplate");
        }

        /// <summary>
        /// 引擎的标题工厂（**项目侧渲染注入点**）：原版 font16 / LowerCenter / 白色。
        /// </summary>
        private static IOverlayLabelView CreateTitleLabel(RectTransform parent, string nodeName, Vector2 size, Vector2 pos)
        {
            var title = D2Label.Create(parent, nodeName, string.Empty, TitleFont, TitleAnchor, TitleColor,
                size, pos, (int)UiLayoutGame.FontPx16);
            if (title == null)
            {
                UiLog.Error("[EnemyBarView] 顶部条标题未建出来（D2Label.Create 返回 null）⇒ 顶部条不显示");
                return null;
            }
            return new LabelView(title);
        }

        /// <summary>
        /// 引擎的名牌文字工厂（**项目侧渲染注入点**）：原版 font16 / MiddleCenter。
        /// <para>尺寸由引擎件按 `NameplateSizeFor`（`SizeMeasure` 委托）写；pivot / 位置由引擎件写。</para>
        /// </summary>
        private static IOverlayLabelView CreatePlateLabel(RectTransform parent, string nodeName)
        {
            var plate = D2Label.Create(parent, nodeName, string.Empty, NameplateFont, TextAnchor.MiddleCenter,
                Color.white, Vector2.zero, Vector2.zero, (int)UiLayoutGame.FontPx16);
            if (plate == null)
            {
                UiLog.Error("[EnemyBarView] NPC 名字牌文字未建出来（D2Label.Create 返回 null）⇒ 名字牌不显示");
                return null;
            }
            return new LabelView(plate);
        }

        // ── 订阅 ─────────────────────────────────────────────────────────────
        private void TrySubscribe()
        {
            if (Game.Event == null) return;
            _subscribed = true;

            Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
            Game.Event.On<MonsterState>(Events.MonsterSpawned, OnMonsterState);
            Game.Event.On<MonsterState>(Events.MonsterChanged, OnMonsterState);
            Game.Event.On(Events.StageEntered, OnStageEntered);
            Game.Event.On(Events.StageLeft, OnStageLeft);

            UiLog.Info($"[EnemyBarView] 已就位并订阅：{Events.HoverTargetChanged}（悬停目标）/ "
                + $"{Events.MonsterSpawned}·{Events.MonsterChanged}（血量来源）/StageEntered·StageLeft");
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;

            Game.Event.Off<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
            Game.Event.Off<MonsterState>(Events.MonsterSpawned, OnMonsterState);
            Game.Event.Off<MonsterState>(Events.MonsterChanged, OnMonsterState);
            Game.Event.Off(Events.StageEntered, OnStageEntered);
            Game.Event.Off(Events.StageLeft, OnStageLeft);
        }

        private void OnStageEntered()
        {
            _stageActive = true;
            _states.Clear();                              // 换场：上一张图的怪物状态作废
            UiLog.Info("[EnemyBarView] 已接管（游戏内才显示；进图清空怪物状态表）");
        }

        private void OnStageLeft()
        {
            _stageActive = false;
            HideAll();
            _states.Clear();
            UiLog.Info("[EnemyBarView] 离开游戏内 ⇒ 顶部条与名字牌都不再显示");
        }

        /// <summary>`MonsterSpawned` / `MonsterChanged` 的收方：只记引用（同一对象被原地更新）。</summary>
        private void OnMonsterState(MonsterState state)
        {
            if (state == null || state.id < 0) return;
            _states[state.id] = state;
        }

        // ── 悬停分派（原版 `MouseSelection.cs:53-80`）──────────────────────────
        /// <summary>
        /// `Events.HoverTargetChanged` 的收方：可击杀怪 ⇒ 顶部条；NPC ⇒ 名字牌；其余 ⇒ 都不显示。
        /// <para>参数类型写**全限定名** `Diablo2.Def.HoverTarget`：本命名空间 `Diablo2.UI` 里
        /// 另有一个同名 MonoBehaviour（`UI/HoverTarget.cs`）⇒ 裸写会解析到它（实测 CS1061）。</para>
        /// </summary>
        private void OnHoverChanged(Diablo2.Def.HoverTarget t)
        {
            if (!_stageActive)
            {
                _loggedHoverId = int.MinValue;
                HideAll();
                return;
            }

            if (IsEnemyBarTarget(t))
            {
                ShowEnemyBar(t);
                return;
            }

            if (IsNameplateTarget(t))
            {
                ShowNameplate(t);
                return;
            }

            _loggedHoverId = int.MinValue;
            HideAll();
        }

        /// <summary>可击杀怪 ⇒ 屏幕顶部：`title.text` + `slider.maxValue/value`（原版 `EnemyBar.cs:26-35`）。</summary>
        private void ShowEnemyBar(Diablo2.Def.HoverTarget t)
        {
            if (!EnsureShown()) return;

            _plate.Hide();

            _states.TryGetValue(t.id, out var st);
            var title = TitleTextFor(st, t);
            var hp = st != null ? st.hp : 0;
            var maxHp = st != null ? st.maxHp : 0;

            // 原版 `slider.maxValue = unit.maxHealth` / `slider.value = unit.health`；
            // 精英怪沿用原版金名口径（与 `ItemTooltip` 稀有黄同族）。
            _bar.Show(title, hp, maxHp, st != null && st.isChampion ? new Color(1f, 0.82f, 0.35f, 1f) : TitleColor);

            if (_loggedHoverId != t.id)
            {
                _loggedHoverId = t.id;
                UiLog.Info($"[EnemyBarView] 顶部条显示：m#{t.id}「{title}」"
                    + $" 回读 title.text=\"{TitleText}\" slider.value={BarValue}/{BarMaxValue}"
                    + $" FillAmount={FillAmount(hp, maxHp):0.###} 血量={hp}/{maxHp}"
                    + (st != null ? $" 精英={st.isChampion}" : " 血量=未知（MonsterChanged 未到）")
                    + $" 矩形={BarRect()}");
            }
        }

        /// <summary>NPC / 非单位实体 ⇒ 世界内上方名字牌（原版 `ShowLabel`）。</summary>
        private void ShowNameplate(Diablo2.Def.HoverTarget t)
        {
            if (!EnsureShown()) return;

            _bar.Hide();

            var text = string.IsNullOrEmpty(t.name) ? "?" : t.name;

            // 黑底按文本实测撑（原版 ContentSizeFitter + padding(6,6,0,4)）——量法由引擎件回调 `NameplateSizeFor`。
            // 投影失败（相机缺失 / 点在相机背面）⇒ 引擎件返回 false 并自己藏起来（不画在错位置）。
            if (!_plate.Show(NameplateWorld(t.gridX, t.gridY), text, Color.white))
            {
                _bar.Hide();
                return;
            }

            if (_loggedHoverId != t.id || !string.Equals(_loggedName, text, System.StringComparison.Ordinal))
            {
                _loggedHoverId = t.id;
                _loggedName = text;
                UiLog.Info($"[EnemyBarView] NPC 名字牌显示：「{text}」id={t.id} 格=({t.gridX},{t.gridY})"
                    + $" 世界落点={NameplateWorld(t.gridX, t.gridY)}（= 格中心 + {NpcTitleLiftWorld} 世界单位）"
                    + $" 尺寸={NameplateSizeFor(text)}");
            }
        }

        /// <summary>两个显示都关掉（原版 `ShowNothing`）。</summary>
        private void HideAll()
        {
            if (_bar != null) _bar.Hide();
            if (_plate != null) _plate.Hide();
        }

        /// <summary>构件是否就绪（没就绪 ⇒ 打一条 Warn 并返回 false，绝不半画）。</summary>
        private bool EnsureShown()
        {
            if (_bar != null && _plate != null) return true;
            UiLog.WarnOnce("enemybar.notbuilt", "[EnemyBarView] 构件未就绪（`Build` 失败过）⇒ 本次悬停不显示");
            return false;
        }

        /// <summary>
        /// `D2Label` → 引擎 <see cref="IOverlayLabelView"/> 的适配器（理由同 `UI/GroundItemLabelView.LabelView`：
        /// 引擎件**不许引用项目类**，所以适配只能落在项目侧）。
        /// </summary>
        private sealed class LabelView : IOverlayLabelView
        {
            private readonly D2Label _label;

            public LabelView(D2Label label) { _label = label; }

            public bool IsAlive => _label != null && _label.gameObject != null;

            public RectTransform Rect => _label != null ? _label.rectTransform : null;

            public void SetText(string text) { if (_label != null) _label.SetText(text); }

            public void SetColor(Color color) { if (_label != null) _label.SetColor(color); }

            public void SetActive(bool active) { if (_label != null) _label.SetActive(active); }
        }
    }
}
