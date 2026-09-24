// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/CursorView.cs
//
// `Events.CursorChanged` 的**消费方**（发送方 = `Module/Input/InputReader.UpdateHover`，
//   每次悬停目标变化时发 `Def.CursorKind`）。除本类之外，全工程没有别处设 `UnityEngine.Cursor`、
//   也没有跟随鼠标的 Image ⇒ 少了它画面上只有**系统默认箭头**，原版的攻击/交互/拾取/不可走
//   四种形态看不见。
//
// ── 承载方式：**Top 画布上跟随鼠标的 Image + `Cursor.visible = false`**（二选一里选了它）──
//   ① `UnityEngine.Cursor.SetCursor(texture, hotspot, mode)` 要求贴图 **Read/Write Enabled**，
//      而本工程的光标素材实测 `isReadable: 0`
//      （`Assets/Resources/Clover/D2/UI/Cursor/Cursor.png.meta`），且批次导入设置由
//      `Assets/Editor/AssetImporter.cs` 的 `D2/UI/**` 前缀规则统一决定（Point/Sprite/无压缩）；
//      为一个光标把整族素材改成可读会平白多一份 CPU 内存 ⇒ **不走硬件光标**。
//   ② 硬件光标尺寸是**屏幕像素**、不吃 uGUI 的 CanvasScaler ⇒ 它不会跟着整屏 UI 一起 ×1.8
//      （项目全部 UI 都按「原版像素 ×1.8」放，光标会是唯一一个尺寸口径不同的元素）。
//      用 Image 则天然吃到同一个参考分辨率 / 缩放 ⇒ 与其余 UI 一致。
//   ③ Image 还让"哪个形态该显示什么"变成可断言的数据（帧号常量 + 面板源码），
//      硬件光标那条路径在离线宿主里查不到任何东西。
//
// ── 只在**游戏内**接管（不是"到处接管"）──────────────────────────────────────
//   现有素材只有**游戏内用的那一帧箭头**；菜单里没有可画的替代品，
//   若把系统光标全局藏掉而贴图又没到位，用户会**连指针都看不见**（比现状更糟）。
//   故：`Events.StageEntered` 起接管、`Events.StageLeft` 放开；并且**只有贴图真的到位**才隐藏
//   系统光标（取不到图 ⇒ 保留系统光标 + Warn，绝不出现"无光标"状态）。
//
//   原版的攻击 / 交互 / 拾取 / 不可走 4 态图**不在本机**（现有素材只有普通箭头 32×26）
//   ⇒ 5 态**统一显示这一帧箭头**，切到缺口形态时**逐态打一条 Warn** 点名缺口，
//   并登记在 `client/资源欠缺清单.md`。拿到 4 态图后：改 `ResPaths.Cursor` 的取帧口径
//   （若那时是多帧条带）+ 本文件 `SpriteFor` 一处即可，触发链/承载方式都不用动。
//
//   「独立画布 + 跟随鼠标 + 显隐 + 系统光标接管」这一套机制由引擎件
//   `CloverEngine.SoftwareCursorLayer` 提供（`clover-client-unity-engine/Runtime/Presentation/
//   WorldOverlayWidgets.cs`）；本文件只剩：① 装配（自安装常驻对象 → 调引擎件建画布）；
//   ② D2 取值（`UiLayoutGame.Cursor*` 全部常量、`ResPaths.Cursor`）；③ 形态分派与缺口 Warn；
//   ④ 事件订阅。屏幕点换算由引擎件统一走 `ScreenPointUtil`。
//
// ── 装配方式（为什么是自安装而不是挂在某个面板上）────────────────────────────────
//   引擎面板必须走 `Resources/UI/{类名}` 预制体（`Runtime/Presentation/UI.cs:131-140`），
//   而本工程的面板预制体由 `Diablo2 → 一键生成工程` 生成（`Assets/Editor/ProjectBuilder.cs`）
//   而光标是**全局单件**（不属于任何面板、要盖在所有面板之上），所以直接用
//   `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 建一个 `DontDestroyOnLoad` 的
//   独立画布（`sortingOrder` 高于引擎常驻画布的 0 ⇒ 不会被任何面板盖住），
//   完全不碰 `Module/**` / `App/**` / 任何预制体。
//   本类**不引用任何业务模块**（分层自检 ③：`UI/**` 里 0 个 `using Diablo2.Module`）。
// 一个 `.cs` 一个 MonoBehaviour；日志一律 `UiLog.*`（不裸 `UnityEngine.Debug` 那族）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 游戏内鼠标光标的**唯一消费方**：订阅 <c>Events.CursorChanged</c>（形态）+
    /// <c>StageEntered</c>/<c>StageLeft</c>（何时接管），把原版箭头贴图贴在鼠标位置。
    /// </summary>
    public class CursorView : MonoBehaviour
    {
        /// <summary>本工程独立画布的 `sortingOrder`：引擎常驻 UI 画布是 **0**（`Runtime/Presentation/UI.cs:50`）
        /// ⇒ 取 1，光标画在**所有面板之上**（含 Top/System 层）。</summary>
        private const int CanvasSortingOrder = 1;

        private static CursorView _instance;

        /// <summary>已经 Warn 过缺口的形态位掩码（每种形态只报一次，不刷屏）。</summary>
        private static int _gapLogged;

        private static bool _spriteMissingLogged;

        /// <summary>新一局 Play 复位静态闸门（工程开了「不重载域」时 static 会跨局保留，见 `App/Bootstrap.cs` P-3）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForNewPlaySession()
        {
            _instance = null;
            _gapLogged = 0;
            _spriteMissingLogged = false;
        }

        /// <summary>
        /// 建常驻光标画布（`AfterSceneLoad` ⇒ 每次进 Play 都会跑；上一局的对象已随 Play 结束销毁，
        /// `_instance != null` 走的是 Unity 的"已销毁 == null"语义，故不会重复建）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;

            var go = new GameObject("[CursorView]");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<CursorView>();
        }

        // ── 状态 ─────────────────────────────────────────────────────────────
        /// <summary>引擎件：独立画布 + 跟随鼠标 + 显隐（含系统光标接管）。</summary>
        private SoftwareCursorLayer _cursor;

        /// <summary>光标贴图节点（引擎建的；本类只负责往里塞 sprite）。</summary>
        private Image _image;

        private bool _built;
        private bool _subscribed;
        private bool _spriteRequested;
        private bool _spriteReady;
        private bool _stageActive;
        private bool _inputUnavailableLogged;
        private CursorKind _kind = CursorKind.Default;

        private void Awake()
        {
            Build();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            // 引擎件 Dispose 内部会**把系统光标恢复可见**（绝不把玩家的指针留在"藏"的状态）。
            if (_cursor != null) _cursor.Dispose();
            _cursor = null;
            _image = null;
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // 引擎门面（`Game.Event`/`Game.Res`）在 `Game.Launch` 之后才有 ⇒ 懒挂接 + 懒取图。
            if (!_subscribed) TrySubscribe();
            if (!_spriteRequested) TryLoadSprite();
            if (!_stageActive || !_spriteReady) return;

            if (_cursor == null) return;
            if (Game.Input == null || !Game.Input.Available)
            {
                if (!_inputUnavailableLogged)
                {
                    _inputUnavailableLogged = true;
                    UiLog.Warn("光标无法跟随鼠标：`Game.Input` 不可用（输入后端未初始化）⇒ 本帧不移动光标");
                }
                return;
            }

            // 屏幕点 → 画布局部坐标：引擎件内部统一走 `ScreenPointUtil`（相机按**画布模式**取；
            // 本画布是 ScreenSpaceOverlay ⇒ 解析为 null，与原写死的 null 同一实参 ⇒ 数值恒等）。
            var screen = Game.Input.MousePosition;
            _cursor.Follow(new Vector2(screen.x, screen.y));
        }

        // ── 构建（交给引擎件：独立画布 + 一张 Image）─────────────────────────
        private void Build()
        {
            if (_built) return;
            _built = true;

            // D2 取值全部在这里给引擎：画布排序层 / 参考分辨率 / 匹配权重 / 贴图尺寸 / hot spot。
            var spec = new SoftwareCursorSpec
            {
                CanvasNodeName = "CursorCanvas",
                SortingOrder = CanvasSortingOrder,
                ReferenceResolution = UiLayoutGame.CursorCanvasRef,
                MatchWidthOrHeight = UiLayoutGame.CursorCanvasMatch,
                ImageNodeName = "Cursor",
                ImageSize = UiLayoutGame.CursorSize,
                ImagePivot = UiLayoutGame.CursorHotspotPivot,
            };

            _cursor = SoftwareCursorLayer.Create(transform, spec, CreateCursorImage);
            if (_cursor == null)
            {
                UiLog.Error("光标节点没建出来（引擎 `SoftwareCursorLayer.Create` 返回 null）⇒ 本轮只能沿用系统光标");
                return;
            }

            _image = _cursor.Image;
        }

        /// <summary>
        /// 引擎的光标图工厂（**项目侧渲染注入点**）：造那张 `Image`。
        /// <para>pivot / 居中锚点 / `raycastTarget = false` / 初始 `enabled = false` 由引擎件统一写；
        /// 这里只管"用项目的 UI 工厂造一块纯色底板"。</para>
        /// </summary>
        private static Image CreateCursorImage(RectTransform canvasRoot, string nodeName, Vector2 size, Vector2 pivot)
        {
            return UiArt.Panel(canvasRoot, nodeName, size, Vector2.zero, Color.white, false);
        }

        // ── 贴图 ─────────────────────────────────────────────────────────────

        private void TryLoadSprite()
        {
            if (Game.Res == null) return;
            _spriteRequested = true;

            Game.Res.LoadAsset<Sprite>(ResPaths.Cursor, sp =>
            {
                if (this == null || _image == null) return;      // 对象已销毁
                if (sp == null)
                {
                    if (!_spriteMissingLogged)
                    {
                        _spriteMissingLogged = true;
                        UiLog.Warn($"原版光标贴图取不到：{ResPaths.Cursor}（`Resources/Clover/` 下应存在）" +
                                   "⇒ 保留系统光标（绝不隐藏），登记在 client/资源欠缺清单.md");
                    }
                    return;
                }

                _image.sprite = sp;
                _image.color = Color.white;      // 原版亮度（白），不加滤镜
                UiArt.EnsureUnlit(_image);
                _spriteReady = true;
                ApplyVisibility();
                UiLog.Info($"[原版光标] {ResPaths.Cursor} 就位：素材 {sp.rect.width:0}×{sp.rect.height:0} 原生px" +
                           $" → 画布 {UiLayoutGame.CursorSize.x:0.#}×{UiLayoutGame.CursorSize.y:0.#}（×{UiLayoutGame.K}），" +
                           $"hot spot = 箭头尖（原版贴图左上角）");
            });
        }

        // ── 订阅（`Game.Event` 只有 `Game.Launch` 之后才非 null）──────────────

        private void TrySubscribe()
        {
            if (Game.Event == null) return;
            _subscribed = true;

            Game.Event.On<CursorKind>(Events.CursorChanged, OnCursorChanged);
            Game.Event.On(Events.StageEntered, OnStageEntered);
            Game.Event.On(Events.StageLeft, OnStageLeft);
            UiLog.Info($"光标承载已就位并订阅：{Events.CursorChanged}（形态）/" +
                       $"{Events.StageEntered}·{Events.StageLeft}（游戏内才接管，菜单保留系统光标）");
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;
            Game.Event.Off<CursorKind>(Events.CursorChanged, OnCursorChanged);
            Game.Event.Off(Events.StageEntered, OnStageEntered);
            Game.Event.Off(Events.StageLeft, OnStageLeft);
        }

        // ── 事件 ─────────────────────────────────────────────────────────────

        private void OnStageEntered()
        {
            _stageActive = true;
            ApplyVisibility();
            UiLog.Info("游戏内光标已接管（原版单帧箭头；攻击/交互/拾取/不可走 4 态缺图已登记）");
        }

        private void OnStageLeft()
        {
            _stageActive = false;
            ApplyVisibility();
            UiLog.Info("已离开游戏内 ⇒ 放开光标（系统光标恢复）");
        }

        /// <summary>形态变化：只有"当前该显示的图"变了才做事；缺图的形态**逐态 Warn 一次**。</summary>
        private void OnCursorChanged(CursorKind kind)
        {
            if (kind == _kind) return;
            _kind = kind;

            if (kind == CursorKind.Default)
            {
                UiLog.Info("光标形态 = 普通箭头（原版帧）");
                return;
            }

            var bit = 1 << (int)kind;
            if ((_gapLogged & bit) != 0) return;
            _gapLogged |= bit;
            UiLog.Warn($"光标形态切到「{kind}」（{Label(kind)}），但原版该形态的图**不在本批素材里**" +
                       "（只有普通箭头 32×26）⇒ 仍显示箭头：按 skill §0「A 没有就不加」**不自画**；" +
                       "缺口登记在 client/资源欠缺清单.md，拿到图后只改一处取帧口径");
        }

        /// <summary>形态的中文名（日志用；越界不静默）。</summary>
        private static string Label(CursorKind kind)
        {
            switch (kind)
            {
                case CursorKind.Default: return "普通箭头";
                case CursorKind.Attack: return "可攻击";
                case CursorKind.Interact: return "可交互";
                case CursorKind.Pickup: return "可拾取";
                case CursorKind.NoWalk: return "不可到达";
                default:
                    UiLog.WarnOnce("cursor.kind.unknown", $"光标形态 {(int)kind} 未登记（合法 0..{UiLayoutGame.CursorKindCount - 1}）");
                    return "未知" + (int)kind;
            }
        }

        /// <summary>
        /// 可见性 = 「在游戏内」且「贴图到位」；两者都成立才显示自绘光标**并**隐藏系统光标
        /// （成对由引擎件 `SoftwareCursorLayer.SetVisible` 保证 —— 绝不出现"两个光标"或"一个都没有"）。
        /// </summary>
        private void ApplyVisibility()
        {
            if (_cursor != null) _cursor.SetVisible(_stageActive && _spriteReady);
        }
    }
}
