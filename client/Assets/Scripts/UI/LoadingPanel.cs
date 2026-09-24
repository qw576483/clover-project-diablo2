// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/LoadingPanel.cs
// 站点：Loading（读条进图）。层：**System**（要盖住一切）。预制体：`Resources/UI/LoadingPanel`。
//
// agent-a3 重写（「经典 load 动画」轮）：**1:1 复刻原版进图画面**
//
//   用户原话：「进入游戏场景，**那个经典的 load 动画也没做**」。
//
//   原版进图画面到底是什么（**逐条给出处，不是回忆**）——
//   依据 = 社区复刻工程 mofr/Diablerie 的
//   `Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs`：
//     :10    SpritesheetPath = @"data\global\ui\Loading\loadingscreen"
//     :43    `Background` = 铺满整屏的 **RawImage，color = Color.black** ⇒ **黑底**
//     :51-52 贴图由 `Spritesheet.Load(SpritesheetPath, PaletteType.Loading)` 取
//            ⇒ 调色板 = `data/global/palette/loading/Pal.PL2`
//            （`Engine/IO/D2Formats/PaletteType.cs:13` + `Palette.cs:20`）
//     :58-61 `Image` 节点 anchorMin/Max=(0.5,0.5)、pivot=(0.5,0.5)、pos=(0.5,0.5) ⇒ **屏幕正中**
//     :66-68 帧号 = `(int)((_sprites.Length - 1) * completeness)`，再 `SetNativeSize()`
//            ⇒ **进度 = 帧号**（门/传送门开得越大 = 越接近读完），按**原始像素**显示
//     :27-35 全文**没有**进度条 / 百分比数字 / 提示文字节点。
//
//     ① 铺满画布的**纯黑**底（原版 `Background`）；
//     ② 屏幕正中一张 **原版 256×256 读条图**（10 帧，逐帧独立 PNG，
//        `ResPaths.Frame(ResPaths.MenuLoadingScreen, i)`），尺寸 = 256 原版px × 1.8 = **460.8**
//        （按高度等比：原版 256/600 = 42.7% 的画布高，本项目 460.8/1080 = 42.7% 一致 ⇒ 视觉占比 1:1）。
//
//   被**删除**的（上一版自造的、原版没有的元素 ⇒ 1:1 硬标准下不能留）：
//     · 一条 400×10 的进度条（锚点宽度填充）·「73%」百分比文字 ·「正在生成地图…」提示文字。
//     为什么删：`LoadingScreen.cs` 的 34-70 行里只有「黑底 + 一张图」；多画一项 = 与原版不一致。
//     进度反馈**没有丢**：它由**帧号**表达（原版就是这么表达的），驱动源仍是**真实进度**。
//
//   驱动方式（「经典 load 动画」轮改）：**进度不再只来自 `Game.Scene.Load`**。
//     ⇒ 本工程 Stage 场景极小，引擎 ~40ms 就把进度推到 0.9 ⇒ 实机只闪过「第 1/10 → 第 10/10 帧」
//     （Play 日志 20:36:40.229 → :40.231，2ms），用户要的「经典的 load 动画」等于看不见。
//     现在由 `Module/Flow/LoadingSteps.cs` 的**分档表**驱动：
//       · 10 档各绑一个**真实里程碑**（场景就位 / 地图生成 / 主角装配 / 刷怪 / 相机 / 世界就绪 + 首帧）；
//       · `AppFlow` 每 tick 做一档真实工作，并只在"真实档位已到"时把门推进一档（门**永不超前**真实）；
//       · 关屏时机 = 世界就绪（`StageEntered`/HUD 已开 + 首帧已渲染）**且**门已开满第 10 帧。
//     ⇒ 本面板仍是"哑"的：`SetProgress(completeness, reason)` 进来就把对应帧贴上去，
//       **不含任何定时器 / 假定曲线**（节奏下限在 `LoadingSteps.FrameCadenceSeconds`，属 Flow 层）。
//
//   一处**本项目换算**（已尽量小、且写在代码里可复核）：引擎 `Runtime/Presentation/Scene.cs:34-43`
//     在 `allowSceneActivation = false` 期间回调，`op.progress` 的值域是 **[0, 0.9]**（到 0.9 才放行）
//     ⇒ 那一段**只**喂门的前 5 档（[0, 0.9] → [0, 0.5]），换算在
//     `LoadingSteps.SceneLoadFrameIndex`（与 `WorldBuilder.cs:33` 的 `Show(0.5f)` 同位置）。
//
// 不引用任何业务模块（分层自检 ③）。资源路径一律来自 `Core/ResPaths.cs`。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>读条面板（原版进图画面：黑底 + 居中 10 帧读条图；帧号 = 真实里程碑的档位）。</summary>
    public class LoadingPanel : UIPanel
    {
        /// <summary>未收到任何进度回调时显示的帧号 = 0（原版 `Show(completeness = 0)` 的默认值）。</summary>
        public const int FrameAtZero = 0;

        private const string Tag = "Ui";

        /// <summary>原版读条图 10 帧（逐个 `ResPaths.Frame` 取；缺帧时该槽为 null）。</summary>
        private readonly Sprite[] _frames = new Sprite[ResPaths.FrameCountLoadingScreen];

        /// <summary>是否整组取图失败（⇒ 只剩黑底 + 一条 Error，不再反复请求）。</summary>
        private bool _framesMissing;

        /// <summary>10 帧的独立 PNG 路径（一次性拼好，避免每帧回调里再拼字符串）。</summary>
        private readonly string[] _framePaths = new string[ResPaths.FrameCountLoadingScreen];

        private Image _art;
        private int _shownFrame = -1;
        private float _lastLoggedProgress = -1f;
        private bool _built;

        /// <inheritdoc/>
        public override UILayer Layer => UILayer.System;

        // ═════════════════════════════════════════════════════════════════════
        // 纯函数（离线自测断言的就是这两个）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 进度 → 帧号（**原版公式**）：`(int)((帧数-1) × completeness)`。
        /// <para>出处：Diablerie `Game/UI/LoadingScreen.cs:66`。
        /// 也是"档号 → 帧号"的唯一换算（`LoadingSteps.CompletenessOf(i)` 保证 `FrameIndex` 回得来 i）。</para>
        /// </summary>
        /// <param name="completeness">原版语义的完整度 [0,1]（值来源见文件头「驱动方式」）。</param>
        /// <param name="frameCount">帧数（本工程 = <see cref="ResPaths.FrameCountLoadingScreen"/>）。</param>
        public static int FrameIndex(float completeness, int frameCount)
        {
            if (frameCount <= 0) return 0;
            var c = Mathf.Clamp01(completeness);
            var idx = (int)((frameCount - 1) * c);
            return idx < 0 ? 0 : (idx >= frameCount ? frameCount - 1 : idx);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 生命周期
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            // 参数校验（constraints.md #7：漏传要 Warn，不静默）。原版进图画面**没有文字节点**，
            //   所以 `FlowConst.LoadingTipStage` 只进日志（当作"当前在做什么"的可检索线索），不上屏。
            var tip = param as string;
            if (param != null && tip == null)
                Log.Warn(Tag, $"LoadingPanel.OnOpen 参数类型非 string（{param.GetType().Name}）⇒ 忽略该参数");

            _lastLoggedProgress = -1f;
            ApplyFrame(FrameAtZero, "第 1 档：读条屏打开（Game.Scene.Load 已发起）");
            UiLayoutFlow.LogTable(nameof(LoadingPanel));
            Log.Info(Tag,
                "读条面板已打开：原版进图画面（黑底 + 居中 10 帧读条图）；帧号 = **分档推进**的真实里程碑"
                + "（Module/Flow/LoadingSteps.cs：场景就位/地图生成/主角装配/刷怪/相机/世界就绪），"
                + "关屏时机 = 世界就绪且门开满第 10 帧"
                + (string.IsNullOrEmpty(tip) ? string.Empty : $"；流程给的提示串=\"{tip}\"（原版无文字节点 ⇒ 只记日志不上屏）"));
        }

        /// <summary>
        /// 刷新读条画面：completeness ∈ [0,1] → 帧号。**唯一调用方 = `AppFlow`**（Loading 站点的呈现步）。
        /// <para>本方法不做任何映射/定时/插值：进来的 `completeness` 直接按**原版公式**取帧
        /// （`(int)((帧数-1) × completeness)`，`LoadingScreen.cs:66`）。节奏与"档位该不该到"
        /// 都在 `Module/Flow` 侧决定（见文件头「驱动方式」）。</para>
        /// </summary>
        /// <param name="completeness">门开到多大（原版语义 [0,1]）。</param>
        /// <param name="reason">为什么推到这一档（写进帧日志，供验收按日志时间戳逐帧核对）。</param>
        public void SetProgress(float completeness, string reason)
        {
            if (!_built)
            {
                // 非预期分支：进度先于 OnOpen 到达（理论上不会）⇒ 记一条，别静默丢掉
                Log.Warn(Tag, $"LoadingPanel 尚未构建就收到进度 {completeness:0.###}（SetProgress 早于 OnOpen？）⇒ 丢弃本次");
                return;
            }

            if (float.IsNaN(completeness))
            {
                // 非预期分支：进度值非法 —— 不能静默当成 0（那会让画面停在第 0 帧且无从查起）
                Log.Warn(Tag, "SetProgress 收到 NaN（期望 [0,1]）⇒ 本次按 0 处理（画面停在当前帧）");
            }

            var c = float.IsNaN(completeness) ? 0f : Mathf.Clamp01(completeness);
            var frame = FrameIndex(c, _frames.Length);
            ApplyFrame(frame, reason);

            // 每跨过 25% 打一条（验收要「能对上的日志数值」；逐帧那一条由 ApplyFrame 打，带原因）。
            if (_lastLoggedProgress < 0f || c - _lastLoggedProgress >= 0.25f || c >= 1f)
            {
                if (c >= 1f && _lastLoggedProgress >= 1f) return;
                _lastLoggedProgress = c;
                Log.Info(Tag, $"读条进度 {c * 100f:0}% ⇒ 第 {frame + 1}/{_frames.Length} 帧（{reason}）");
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // 构件
        // ═════════════════════════════════════════════════════════════════════

        private void Build()
        {
            if (_built) return;
            _built = true;

            // ① 原版 `Background`：铺满整屏的纯黑（`LoadingScreen.cs:41-49`，color = Color.black）。
            //    必须**先建**（同层第一个子节点 ⇒ 层级最低），否则会盖住读条图。
            var bg = UiArt.FullPanel(transform, "Backdrop", Color.black, false);
            if (bg == null) Log.Error(Tag, "读条屏黑底未建出来（UiArt.FullPanel 返回 null）⇒ 进图画面会露出上一帧");

            // ② 原版 `Image`：屏幕正中、按原始像素显示（`LoadingScreen.cs:54-61` + `:68`）。
            //    位置/尺寸来自 `UiLayoutFlow.Loading`（原版 256×256 → ×1.8 = 460.8，中心 = 画布正中）。
            _art = UiArt.Panel(transform, "LoadingScreen", UiLayoutFlow.Loading.ArtSize,
                UiLayoutFlow.Loading.ArtPos, Color.white, false);
            if (_art == null)
            {
                Log.Error(Tag, "原版读条图节点未建出来（UiArt.Panel 返回 null）⇒ 进图画面只剩黑底");
                return;
            }

            RequestFrames();
        }

        /// <summary>取原版 10 帧。本批是**一帧一个 PNG**（不是一张条带切 N 帧）⇒ 逐帧按名加载即可。</summary>
        private void RequestFrames()
        {
            for (var i = 0; i < _framePaths.Length; i++)
                _framePaths[i] = ResPaths.Frame(ResPaths.MenuLoadingScreen, i);

            if (Game.Res == null)
            {
                _framesMissing = true;
                Log.Error(Tag, "Game.Res 未初始化（CloverRes.Init 未调用）⇒ 原版读条图取不到，进图画面只剩黑底");
                return;
            }

            // `LoadAsset` 命中缓存时回调可能**同步**触发 ⇒ 先把累计计数清零，再逐帧发起。
            _framesLeft = _framePaths.Length;
            for (var i = 0; i < _framePaths.Length; i++)
            {
                var index = i;
                Game.Res.LoadAsset<Sprite>(_framePaths[index], sp => OnFrameLoaded(index, sp));
            }
        }

        /// <summary>还在等结果的帧数（全部有结果后才判定"整组缺失"）。</summary>
        private int _framesLeft;

        private void OnFrameLoaded(int index, Sprite sp)
        {
            if (sp != null) _frames[index] = sp;

            if (--_framesLeft > 0) return;      // 还有帧没回来

            if (HasMissingFrame())
            {
                _framesMissing = true;
                Log.Error(Tag, $"原版读条图整组缺失：{ResPaths.D2UiMenu}"
                    + $"{ResPaths.MenuLoadingScreen}_0..{_frames.Length - 1}（逐帧按名取不到）"
                    + " ⇒ 进图画面只剩黑底（素材未导出？跑 "
                    + "`python tools/d2codec/export_d2ui.py 原版dc6 client --only loading`）");
                return;
            }

            Log.Info(Tag, $"[原版读条图] {CountFrames()}/{_frames.Length} 帧就位，"
                + $"尺寸 {UiLayoutFlow.Loading.ArtSize.x:0.#}×{UiLayoutFlow.Loading.ArtSize.y:0.#}"
                + $"（原版 256×256 ×{UiLayoutFlow.Scale}），中心 = 画布正中");

            // 图刚到：把当前应显示的帧补上（打开时 `_frames` 还全是 null ⇒ 上面那次 ApplyFrame 没贴上）
            ApplyFrame(_wantedFrame, "原版读条图到位");
        }

        private bool HasMissingFrame()
        {
            for (var i = 0; i < _frames.Length; i++)
                if (_frames[i] == null) return true;
            return false;
        }

        private int CountFrames()
        {
            var n = 0;
            for (var i = 0; i < _frames.Length; i++)
                if (_frames[i] != null) n++;
            return n;
        }

        /// <summary>把第 <paramref name="frame"/> 帧贴到读条图上（同一帧不重复贴，避免无谓的 SetSprite）。</summary>
        private void ApplyFrame(int frame, string why)
        {
            if (_art == null) return;

            var idx = frame < 0 ? 0 : (frame >= _frames.Length ? _frames.Length - 1 : frame);
            _wantedFrame = idx;                     // 记住"该显示哪一帧"（帧回调晚到时按它补图）
            if (idx == _shownFrame) return;         // 已经是这一帧 ⇒ 不重复贴

            var sp = _frames[idx];
            if (sp == null)
            {
                // 帧还没回来 / 整组缺失：保持黑底 + 已打的 Error（不静默、也不贴错图）。
                // 此时**不**更新 `_shownFrame`：等帧回调到达后 ApplyFrame 会把这一帧真正贴上。
                if (!_framesMissing && !_hasWarnedFrameMissing)
                {
                    _hasWarnedFrameMissing = true;
                    Log.Warn(Tag, $"第 {idx + 1} 帧还没取到（{_framePaths[idx]}）⇒ 本次画面停在上一帧"
                        + "（帧回调到达后会自动补上；若最终整组缺失会另行 Error）");
                }
                return;
            }

            _shownFrame = idx;
            _art.sprite = sp;
            _art.color = Color.white;       // 原版亮度：不做任何提亮/压暗（UiArt.ArtFullBright 同义）
            UiArt.EnsureUnlit(_art);
            Log.Info(Tag, $"[原版读条图] 第 {idx + 1}/{_frames.Length} 帧 → LoadingScreen（{why}）");
        }

        /// <summary>"该显示哪一帧"（`ApplyFrame` 每次都会更新；帧回调晚到时按它补图）。</summary>
        private int _wantedFrame;

        /// <summary>"帧还没到"只报一次（进度回调可能很密，防刷屏）。</summary>
        private bool _hasWarnedFrameMissing;
    }
}
