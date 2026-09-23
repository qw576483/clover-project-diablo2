// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/LevelEntryTitle.cs   ★ agent-a3 新增（「经典 load 动画 / 区域名弹出」轮）
//
// **区域名弹出**：玩家**首次进入某区域**时，屏幕上浮出该区域名，淡入 → 停留 → 淡出。
//
// ── 依据（**原版 prefab 逐值 + 原版脚本逐行，不是回忆、不是估的**）──────────────
//  ① 几何：`原版资源/参考工程_Diablerie/Diablerie/Assets/Prefabs/LevelEntryTitle.prefab`
//       root RectTransform：`m_AnchorMin = (0,1)` / `m_AnchorMax = (1,1)` / `m_AnchoredPosition = (0,0)`
//                            / `m_SizeDelta = (0,300)` / `m_Pivot = (0.5,1)`
//       ⇒ **满宽、贴屏幕顶边、高 300 原版px** 的文本框；
//       它下面那个 `Text` 的 `m_FontData.m_Alignment = 4`（MiddleCenter）
//       ⇒ 文字中心 = 顶边往下 300/2 = **150 原版px**。
//       该 prefab 在 `Scenes/Game.unity` 里是 **Canvas（referenceResolution 800×600）的子节点**（:630-728）
//       ⇒ 换算到本项目（原版 800×600 → 画布 1920×1080，**按高度 ×1.8**）：
//          原版 150 从顶边算 → ×1.8 = **270** 画布单位 → 画布 y = +540 − 270 = **+270**；
//          原版高 300 → ×1.8 = **540**（贴顶：rect 上沿 = +540）
//       （= `UiLayoutGame.LevelTitlePos` / `LevelTitleRect()`，本文件只消费它，不自己写数字）。
//  ② 字体：prefab 的 `Text.m_Font = {fileID: 12800000, guid: 75686650217a5234cb237fa8bfb75f0c}`
//       该 guid 在参考工程里 = **`Assets/Resources/Fonts/font30.fontsettings.meta`**
//       ⇒ 原版用的是 **font30**（本项目已有：`ResPaths.Font30` + `UI/D2Text.cs` 的位图字模排版）。
//       （`m_FontSize: 0` 是 Unity 位图字体资产的常态字段，真正尺寸来自 fontsettings 的
//         `m_LineSpacing: 30` 与逐字形矩形 —— 不是"字号 0"。）
//  ③ 颜色：prefab 的 `Text.m_Color = (0.8308824, 0.37267518, 0.37267518, 1)`
//       ⇒ 原版那颗偏红的字色，**照抄**（不提亮不压暗）。
//  ④ 文案与时序：`Assets/Scripts/Diablerie/Engine/Level.cs:20`
//         `LevelEntryTitle.Show("Entering " + info.levelName);`
//       `Assets/Scripts/Diablerie/Engine/UI/LevelEntryTitle.cs:27`
//         `public static void Show(string title, float duration = 3.75f)`
//       ⇒ 文案 = `"Entering " + 区域官方英文名`（`Levels.txt` 的 LevelName）；
//         可见总时长 = **3.75s**。
//
// ── 「首次进入某区域」的口径 ────────────────────────────────────────────────
//   本文件按**每个区域在本局游戏里第一次进入**才弹（`_shown` 去重）。
//   `Diablerie/Level.cs:19-20` 的原写法是「有上一个区域就弹」（等于每次过门都弹）——
//   两者只差"重复进入同一区域弹不弹"；本轮按任务书要求取**首次**，
//   并在 `HudPanel` 侧的每条日志里写明「首次/已弹过」，便于核对（差异已写进回报）。
//
// ── 淡入 / 停留 / 淡出（**只有时长分摊是本项目新增**）────────────────────────
//   原版只有"总时长 3.75s"，`LevelEntryTitle.cs` 里**没有**淡入淡出（到点直接隐藏）。
//   任务书要求「淡入 → 停留 → 淡出」，故把 3.75s **拆开**用（总时长仍是原版的 3.75s）：
//     淡入 0.4s → 停留 2.95s → 淡出 0.4s（0.4 + 2.95 + 0.4 = 3.75 ✓，见 `HoldSeconds`）。
//   透明度用 `CanvasGroup.alpha` 表达（**不逐帧重建字模**：`D2Label.SetText` 会销毁重建字形节点，
//   每帧调用会持续产生 GC —— 引擎自己的 Toast/Loading 通用件也是用 `CanvasGroup` 做淡出）。
//
// ⛔ 本文件在 UI 层：只引用 `CloverEngine` / `Diablo2.Core` / `Diablo2.Def` / UnityEngine(.UI)，
//    **不得**引用 `Diablo2.Module.*`（分层自检 ③）—— 所以区域名在本文件内自持一份（见
//    `OfficialLevelName`），并由离线宿主逐条与 `Table/Tsv/Level.tsv` 的 `level_name` 列核对。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.UI
{
    /// <summary>
    /// 区域名弹出控件（原版 `LevelEntryTitle`）。**非 MonoBehaviour**：由 `HudPanel` 持有并驱动
    /// （`constraints.md` #1：一个 `.cs` 只放一个 MonoBehaviour；本文件刻意一个都不放）。
    /// </summary>
    internal sealed class LevelEntryTitle
    {
        // ═════════════════════════════════════════════════════════════════════
        // 原版常量（每一项都有出处，见文件头）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>可见总时长（秒）—— 原版 `UI/LevelEntryTitle.cs:27` 的 `duration = 3.75f`。</summary>
        public const float TotalSeconds = 3.75f;

        /// <summary>
        /// 淡入时长（秒）。**本项目新增**：原版没有淡入淡出（到点直接隐藏），
        /// 任务书要求「淡入→停留→淡出」⇒ 在**原版总时长 3.75s 之内**分摊，不额外拉长时间。
        /// </summary>
        public const float FadeInSeconds = 0.4f;

        /// <summary>淡出时长（秒）。本项目新增，理由同 <see cref="FadeInSeconds"/>。</summary>
        public const float FadeOutSeconds = 0.4f;

        /// <summary>停留（满不透明）时长 = 3.75 − 0.4 − 0.4 = **2.95s**（见 <see cref="TotalSeconds"/>）。</summary>
        public const float HoldSeconds = TotalSeconds - FadeInSeconds - FadeOutSeconds;

        /// <summary>文案前缀 —— 原版 `Engine/Level.cs:20`：`"Entering " + info.levelName`。</summary>
        public const string TextPrefix = "Entering ";

        /// <summary>
        /// 文字颜色 —— 原版 prefab 的 `Text.m_Color = (0.8308824, 0.37267518, 0.37267518, 1)`（**照抄**）。
        /// </summary>
        public static readonly Color TextColor = new Color(0.8308824f, 0.37267518f, 0.37267518f, 1f);

        /// <summary>字体 —— 原版 prefab 的 `Text.m_Font` 指向 `Resources/Fonts/font30`（见文件头 ②）。</summary>
        public const D2Text.D2Font Font = D2Text.D2Font.Font30;

        // ═════════════════════════════════════════════════════════════════════
        // 纯函数（离线宿主逐条断言）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// `AreaId` → **官方关卡名**（`Levels.txt` 的 `LevelName`，也就是原版 `info.levelName` 的取值）。
        /// <para>出处：本项目 `Table/Tsv/Level.tsv` 的 `level_name` 列 ——
        /// `1=罗格营地 Rogue Encampment / 2=血腥荒野 Blood Moor / 3=邪恶洞穴 Den of Evil`；
        /// 与 `Module/Monster/AreaLevelTable.ExpectedLevelNameOf` 的同一份"契约哨兵"逐字一致
        /// （UI 不许引用 `Module.*` ⇒ 本文件自持一份，由离线宿主断言两边 + TSV 三者一致）。</para>
        /// </summary>
        /// <returns>未登记的区域 ⇒ null（调用方不弹，并留日志）。</returns>
        public static string OfficialLevelName(AreaId area)
        {
            switch (area)
            {
                case AreaId.Town: return "Rogue Encampment";
                case AreaId.BloodMoor: return "Blood Moor";
                case AreaId.DenOfEvil: return "Den of Evil";
                default:
                    UiLog.WarnOnce("leveltitle.area.unknown." + (int)area,
                        $"LevelEntryTitle：未登记的区域 AreaId={(int)area}（期望 Town/BloodMoor/DenOfEvil 之一）"
                        + " ⇒ 本次不弹区域名（请在 OfficialLevelName 补一行）");
                    return null;
            }
        }

        /// <summary>
        /// 某一帧的透明度（**纯函数**，0..1）。
        /// <para>分段：`[0,0.4)` 线性 0→1；`[0.4,3.35)` 恒 1；`[3.35,3.75)` 线性 1→0；之后 0。</para>
        /// </summary>
        /// <param name="elapsedSeconds">自弹出起的秒数（负数/NaN ⇒ 0 = 还没出现）。</param>
        public static float AlphaAt(float elapsedSeconds)
        {
            if (float.IsNaN(elapsedSeconds) || elapsedSeconds <= 0f) return 0f;

            if (elapsedSeconds < FadeInSeconds) return elapsedSeconds / FadeInSeconds;
            if (elapsedSeconds < FadeInSeconds + HoldSeconds) return 1f;
            if (elapsedSeconds < TotalSeconds)
                return 1f - (elapsedSeconds - FadeInSeconds - HoldSeconds) / FadeOutSeconds;
            return 0f;
        }

        /// <summary>区域名文案（`"Entering " + 官方关卡名`）；区域未登记 ⇒ null。</summary>
        public static string TitleTextFor(AreaId area)
        {
            var name = OfficialLevelName(area);
            return name == null ? null : TextPrefix + name;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 运行时
        // ═════════════════════════════════════════════════════════════════════

        private readonly RectTransform _root;
        private readonly CanvasGroup _group;
        private readonly D2Label _label;

        /// <summary>本局已弹过区域名的区域（"首次进入"的判据；随 `HudPanel` 实例生命周期，进新一局自动清空）。</summary>
        private readonly System.Collections.Generic.HashSet<AreaId> _shown
            = new System.Collections.Generic.HashSet<AreaId>();

        private float _elapsed;

        /// <summary>是否正在显示（true 期间 `Tick` 推透明度）。</summary>
        public bool IsShowing { get; private set; }

        /// <summary>当前显示的区域（未显示时为 false）。</summary>
        public bool TryGetShowingArea(out AreaId area)
        {
            area = _showingArea;
            return IsShowing;
        }

        private AreaId _showingArea;

        /// <summary>本控件挂在哪（日志用）。</summary>
        public readonly string Owner;

        private LevelEntryTitle(RectTransform root, CanvasGroup group, D2Label label, string owner)
        {
            _root = root;
            _group = group;
            _label = label;
            Owner = owner;
            SetAlpha(0f);
            SetActive(false);
        }

        /// <summary>
        /// 造一个区域名控件（挂在 <paramref name="parent"/> 下；**建完即在最上层 siblings 末位**，
        /// 所以调用方应在 HUD 构件都建完之后再调它）。
        /// </summary>
        /// <param name="parent">宿主面板的内容根（本项目 = `HudPanel` 的 transform）。</param>
        /// <param name="owner">宿主名（日志里点名，便于定位谁在弹区域名）。</param>
        public static LevelEntryTitle Create(Transform parent, string owner)
        {
            if (parent == null)
            {
                UiLog.Error("LevelEntryTitle.Create 收到 null 父节点 ⇒ 区域名弹不出来");
                return null;
            }

            // 外框 = 画布单位（满宽 1920 × 原版高 300→×1.8 = 540，贴顶 ⇒ 中心 (0,270)）
            var canvasSize = new Vector2(UiArt.RefWidth, UiLayoutGame.LevelTitleH);
            var root = UIFactory.CreateCentered("LevelEntryTitle", parent, canvasSize, UiLayoutGame.LevelTitlePos);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;      // 纯表现，不挡点击
            group.blocksRaycasts = false;

            // 内层 = 位图字体按**原版 px** 排版（等效原版满宽 × 300 高），再整体 ×1.8 ⇒ 字面尺寸与整屏口径一致。
            // 与 `UiLayoutFlow.FlowLabel` 同一套做法（那边也是"内容按原版 px 排版 + 根节点 ×1.8"）。
            //
            // ★ 片 font-scale（**核实结论：这一处不是缺陷，故不给 `fontSize`**）：
            //   全仓扫「`D2Label.Create` 没给字号」时这一处会被机械命中（V6 报告 §4-② 也列了它），
            //   但它的放大来自 `label.Root.localScale = K`（下方 :213）**而不是** `fontSize`：
            //     font30 的 chi 格高 30 原版px × 1.8 ⇒ 画出来就是 **54 画布px = `UiLayoutGame.FontPx30`**，
            //   与"传 fontSize = FontPx30"等效。⛔ 若照 §4-② 再加一个 `(int)UiLayoutGame.FontPx30`
            //   就会**双重放大**（54 → 97.2）—— V6 对 `UiLayoutFlow.cs:1806` 已给出同款警告。
            //   判据：`uicheck` 的 FontScaleCheck 把它作为**已登记例外**（要求同域内出现 `localScale`
            //   的 ×K 放大）；实机 `Probe.LevelTitle` dump 的字块画布高应 ≈ 54。
            var origSize = UiLayoutGame.LevelTitleOrigSize;
            var label = D2Label.Create(root, "Title", string.Empty, Font, TextAnchor.MiddleCenter,
                TextColor, origSize, Vector2.zero);
            if (label == null)
            {
                UiLog.Error("区域名字标签未建出来（D2Label.Create 返回 null）⇒ 弹不出区域名");
                return null;
            }
            label.Root.localScale = new Vector3(UiLayoutGame.K, UiLayoutGame.K, 1f);
            label.Root.anchoredPosition = Vector2.zero;

            var title = new LevelEntryTitle(root, group, label, owner);
            UiLog.Info($"[区域名] 控件已建：几何 = 原版 LevelEntryTitle.prefab（满宽 ×300、贴顶，文字中心 = 顶边下 150 原版px）"
                + $" ×{UiLayoutGame.K} ⇒ 画布 {canvasSize.x:0}×{canvasSize.y:0} @ {UiLayoutGame.LevelTitlePos}；"
                + $"字体 = font{(int)Font}（prefab 的 m_Font guid → Resources/Fonts/font30）；"
                + $"颜色 = {TextColor}（原版 m_Color 照抄）；总时长 {TotalSeconds}s（淡入 {FadeInSeconds} + 停留 {HoldSeconds} + 淡出 {FadeOutSeconds}）");
            return title;
        }

        /// <summary>
        /// **首次进入某区域**时弹出区域名（同一区域本局只弹一次）。
        /// </summary>
        /// <returns>true = 本次真的弹了；false = 已弹过 / 区域未登记（两种情况都留日志）。</returns>
        public bool ShowForFirstEntry(AreaId area)
        {
            if (_root == null)
            {
                UiLog.Warn($"[区域名] {Owner}：控件已销毁（面板已关？）⇒ 忽略本次 {area} 的区域名");
                return false;
            }

            var text = TitleTextFor(area);
            if (text == null) return false;                 // `OfficialLevelName` 已 Warn 过

            if (!_shown.Add(area))
            {
                UiLog.Info($"[区域名] {area}（{OfficialLevelName(area)}）本局已弹过 ⇒ 不重复弹（口径：每个区域**首次**进入才弹）");
                return false;
            }

            _showingArea = area;
            IsShowing = true;
            _elapsed = 0f;
            SetActive(true);
            SetAlpha(0f);                                    // 从全透明开始 ⇒ 才是"淡入"
            _label.SetText(text);
            UiLog.Info($"[区域名] 弹出：\"{text}\"（区域={area}，持有者={Owner}；"
                + $"淡入 {FadeInSeconds}s → 停留 {HoldSeconds}s → 淡出 {FadeOutSeconds}s，合计 {TotalSeconds}s）");
            return true;
        }

        /// <summary>按时间推进淡入/停留/淡出；淡出结束即隐藏。（`dt` = `Game.UI` 传进来的帧间隔）</summary>
        public void Tick(float dt)
        {
            if (!IsShowing || _root == null) return;
            if (dt <= 0f) return;                            // 暂停（timeScale=0）时冻结，与"游戏内时间"一致

            _elapsed += dt;
            var alpha = AlphaAt(_elapsed);

            if (_elapsed < TotalSeconds)
            {
                SetAlpha(alpha);
                return;
            }

            // 走完 3.75s ⇒ 隐藏（原版也是到点隐藏；本项目多出来的只是"末端已淡到 0"）
            IsShowing = false;
            SetAlpha(0f);
            SetActive(false);
            UiLog.Info($"[区域名] 淡出结束并隐藏：\"{TitleTextFor(_showingArea)}\"（可见 {_elapsed:0.00}s，"
                + $"原版时长 {TotalSeconds}s）");
        }

        /// <summary>清掉"已弹过"记录（**进新的一局时**调；进图/过门过程中不许调，否则会重复弹）。</summary>
        public void Reset()
        {
            if (_shown.Count > 0)
                UiLog.Info($"[区域名] 新一局：已弹记录清空（上一局弹过 {_shown.Count} 个区域）");
            _shown.Clear();
            IsShowing = false;
            _elapsed = 0f;
            SetAlpha(0f);
            SetActive(false);
        }

        private void SetAlpha(float a)
        {
            if (_group != null) _group.alpha = Mathf.Clamp01(a);
        }

        private void SetActive(bool on)
        {
            if (_root != null && _root.gameObject.activeSelf != on) _root.gameObject.SetActive(on);
        }
    }
}
