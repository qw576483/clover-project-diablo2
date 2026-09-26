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
//   ③ Image 还让"哪个形态该显示什么"变成可断言的数据（路径常量 + 面板源码），
//      硬件光标那条路径在离线宿主里查不到任何东西。
//
// ── 形态 → 原版件（**逐态各一件**，不是一个尺寸套所有态）────────────────────
//   原版 `data/global/ui/CURSOR/` 共 9 只：`buysell`(10 帧)/`protate`/`ppress`/`orotate`/`ohand`(8 帧)/
//   `grasp`(8 帧)/`Gaunt`(1 帧 34×30)/`focus16`(20×20)/`Pentspin`。其中能对上本工程
//   `Def.CursorKind` 语义的只有两只：
//     · `Default` ← `ohand.dc6` 帧 0（32×26，工程内 `D2/UI/Cursor/Cursor.png` 就是这一帧，
//       可见像素逐像素全等）；
//     · `Attack`  ← `Gaunt.dc6`（34×30 单帧）。
//   `Interact` / `Pickup` / `NoWalk` **原版没有对应件**（`buysell` 的 10 帧是金色钩/槌/钥匙一类
//   图元，逐帧语义无可核出处；`grasp` 的"抓握"语义与本工程的 `Pickup` 触发点不重合）⇒ 这 3 态
//   仍显示 `Default` 的图，并在**首次**切到时各打一条 Warn。缺口登记在 `client/资源欠缺清单.md` #24。
//   ⛔ 缺口态**不自画**（skill §0「A 没有就不加」）。
//
// ── 只在**游戏内**接管（不是"到处接管"）──────────────────────────────────────
//   菜单里没有可画的替代品，若把系统光标全局藏掉而贴图又没到位，用户会**连指针都看不见**
//   （比现状更糟）。故：`Events.StageEntered` 起接管、`Events.StageLeft` 放开；并且
//   **只有当前形态的贴图真的到位**才隐藏系统光标（取不到图 ⇒ 保留系统光标 + Warn，绝不出现
//   "无光标"状态）。
//
//   「独立画布 + 跟随鼠标 + 显隐 + 系统光标接管」这一套机制由引擎件
//   `CloverEngine.SoftwareCursorLayer` 提供（`clover-client-unity-engine/Runtime/Presentation/
//   WorldOverlayWidgets.cs`）；本文件只剩：① 装配（自安装常驻对象 → 调引擎件建画布）；
//   ② D2 取值（`UiLayoutGame.Cursor*` 全部常量、`ResPaths.Cursor` / `ResPaths.CursorAttack`）；
//   ③ 形态分派、逐态尺寸与缺口 Warn；④ 事件订阅。屏幕点换算由引擎件统一走 `ScreenPointUtil`。
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
    /// <c>StageEntered</c>/<c>StageLeft</c>（何时接管），把该形态的**原版件**贴在鼠标位置；
    /// 原版没有对应件的形态显示默认态那张，并逐态报一次缺口。
    /// </summary>
    public class CursorView : MonoBehaviour
    {
        /// <summary>本工程独立画布的 `sortingOrder`：引擎常驻 UI 画布是 **0**（`Runtime/Presentation/UI.cs:50`）
        /// ⇒ 取 1，光标画在**所有面板之上**（含 Top/System 层）。</summary>
        private const int CanvasSortingOrder = 1;

        private static CursorView _instance;

        /// <summary>已经 Warn 过缺口的形态位掩码（每种形态只报一次，不刷屏）。</summary>
        private static int _gapLogged;

        /// <summary>已经 Warn 过"这张原版件取不到"的形态位掩码。</summary>
        private static int _spriteMissingLogged;

        /// <summary>新一局 Play 复位静态闸门（工程开了「不重载域」时 static 会跨局保留，见 `App/Bootstrap.cs` P-3）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForNewPlaySession()
        {
            _instance = null;
            _gapLogged = 0;
            _spriteMissingLogged = 0;
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

        /// <summary>光标贴图节点（引擎建的；本类只负责往里塞 sprite + 该形态的尺寸）。</summary>
        private Image _image;

        /// <summary>各形态自己的贴图（下标 = `Def.CursorKind`）；没取到就是 null。</summary>
        private readonly Sprite[] _sprite = new Sprite[UiLayoutGame.CursorKindCount];

        /// <summary>已经向 `Game.Res` 要过图的形态位掩码（每个形态只要一次）。</summary>
        private int _spriteRequested;

        private bool _built;
        private bool _subscribed;
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
            EnsureSprite(_kind);
            if (!_stageActive || !HasSprite(_kind)) return;

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
            // 尺寸先给默认态那一份，切到别的形态时由 `ApplyArt()` 按该形态自己的件改。
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

        // ── 贴图（按形态各取一件）─────────────────────────────────────────────

        /// <summary>该形态在工程里用哪张图（原版件路径见 `Core/ResPaths.cs`）。</summary>
        private static string PathFor(CursorKind kind)
            => kind == CursorKind.Attack ? ResPaths.CursorAttack : ResPaths.Cursor;

        /// <summary>该形态**有没有原版件**（没有的形态显示默认态那张；见文件头的"形态 → 原版件"）。</summary>
        private static bool HasOriginalArt(CursorKind kind)
            => kind == CursorKind.Default || kind == CursorKind.Attack;

        /// <summary>该形态当前能画哪张图（自己没有就退回默认态那张；都没有 ⇒ null）。</summary>
        private Sprite SpriteOf(CursorKind kind)
        {
            var sp = _sprite[(int)kind];
            if (sp == null && kind != CursorKind.Default) sp = _sprite[(int)CursorKind.Default];
            return sp;
        }

        /// <summary>该形态能不能画（= 贴图已到位）；不能画就不接管系统光标。</summary>
        private bool HasSprite(CursorKind kind) => SpriteOf(kind) != null;

        /// <summary>
        /// 按需向 `Game.Res` 要图（异步；到位后若正是当前该画的那张就立刻换上）。
        /// <para>原版没有对应件的形态要的就是**默认态那张** ⇒ 按 `Default` 取（不是把默认图灌进它自己的槽：
        /// 那样日志会写成"形态 NoWalk 的图就位：…/Cursor"，读起来像 NoWalk 有自己的原版件）。</para>
        /// </summary>
        private void EnsureSprite(CursorKind kind)
        {
            if (Game.Res == null) return;
            var slot = HasOriginalArt(kind) ? kind : CursorKind.Default;
            var bit = 1 << (int)slot;
            if ((_spriteRequested & bit) != 0) return;
            _spriteRequested |= bit;

            var path = PathFor(slot);
            Game.Res.LoadAsset<Sprite>(path, sp =>
            {
                if (this == null) return;                    // 对象已销毁
                if (sp == null)
                {
                    if ((_spriteMissingLogged & bit) == 0)
                    {
                        _spriteMissingLogged |= bit;
                        UiLog.Warn($"原版光标贴图取不到：{path}（`Resources/Clover/` 下应存在）" +
                                   "⇒ 本形态退回" + (slot == CursorKind.Default ? "系统光标" : "默认态那张图") +
                                   "（绝不隐藏系统光标），登记在 client/资源欠缺清单.md");
                    }
                    return;
                }

                _sprite[(int)slot] = sp;
                UiLog.Info($"[原版光标] {slot} 态的图就位：{path} = 素材 {sp.rect.width:0}×{sp.rect.height:0} 原生px" +
                           $" → 画布 {UiLayoutGame.CursorSizeOf(slot).x:0.#}×{UiLayoutGame.CursorSizeOf(slot).y:0.#}" +
                           $"（×{UiLayoutGame.K}），hot spot = 贴图左上角（pivot {UiLayoutGame.CursorHotspotPivot}）" +
                           (slot == kind ? string.Empty : $"（{kind} 原版没有对应件 ⇒ 用它）"));
                if (slot == _kind || !HasOriginalArt(_kind)) ApplyArt();
            });
        }

        /// <summary>把"当前形态该显示的那张图 + 它自己的尺寸"写到光标节点上。</summary>
        private void ApplyArt()
        {
            if (_image == null) return;
            var sp = SpriteOf(_kind);
            if (sp == null)
            {
                ApplyVisibility();
                return;
            }

            _image.sprite = sp;
            _image.color = Color.white;                      // 原版亮度（白），不加滤镜
            _image.rectTransform.sizeDelta = UiLayoutGame.CursorSizeOf(_kind);
            UiArt.EnsureUnlit(_image);
            ApplyVisibility();
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
            EnsureSprite(_kind);
            ApplyArt();
            UiLog.Info($"游戏内光标已接管（原版件：Default={ResPaths.Cursor} / Attack={ResPaths.CursorAttack}；" +
                       "Interact/Pickup/NoWalk 原版没有对应件 ⇒ 显示默认态那张）");
        }

        private void OnStageLeft()
        {
            _stageActive = false;
            ApplyVisibility();
            UiLog.Info("已离开游戏内 ⇒ 放开光标（系统光标恢复）");
        }

        /// <summary>形态变化：换图 + 换尺寸；**原版没有对应件**的形态逐态 Warn 一次。</summary>
        private void OnCursorChanged(CursorKind kind)
        {
            if (kind == _kind) return;
            _kind = kind;
            EnsureSprite(kind);
            ApplyArt();

            if (kind == CursorKind.Default)
            {
                UiLog.Info($"光标形态 = 普通（原版件 {ResPaths.Cursor} 帧 0）");
                return;
            }

            if (HasOriginalArt(kind))
            {
                UiLog.Info($"光标形态 = {kind}（{Label(kind)}，原版件 {PathFor(kind)}，" +
                           $"{UiLayoutGame.CursorArtPxOf(kind).x:0}×{UiLayoutGame.CursorArtPxOf(kind).y:0} 原生px）");
                return;
            }

            var bit = 1 << (int)kind;
            if ((_gapLogged & bit) != 0) return;
            _gapLogged |= bit;
            UiLog.Warn($"光标形态切到「{kind}」（{Label(kind)}），但原版那套光标里**没有**与该语义对应的件" +
                       "⇒ 仍显示默认态那张图：按 skill §0「A 没有就不加」**不自画**；" +
                       "缺口登记在 client/资源欠缺清单.md #24");
        }

        /// <summary>形态的中文名（日志用；越界不静默）。</summary>
        private static string Label(CursorKind kind)
        {
            switch (kind)
            {
                case CursorKind.Default: return "普通";
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
        /// 可见性 = 「在游戏内」且「当前形态有图可画」；两者都成立才显示自绘光标**并**隐藏系统光标
        /// （成对由引擎件 `SoftwareCursorLayer.SetVisible` 保证 —— 绝不出现"两个光标"或"一个都没有"）。
        /// </summary>
        private void ApplyVisibility()
        {
            if (_cursor != null) _cursor.SetVisible(_stageActive && HasSprite(_kind));
        }
    }
}
