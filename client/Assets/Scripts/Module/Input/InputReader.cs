// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Input/InputReader.cs
// **全项目唯一读输入的地方**（内部助手，不是门面接口 —— 契约只有 12 个门面，不许新增）。
//
// 职责：把「鼠标左键点击地面 / 按住左键持续走 / 悬停格 / 键盘快捷键」翻译成**格坐标与意图**，
// 交给 `Module/Player` 使用；`Module/Camera` 若开启可选缩放/边缘滚动也走这里读轴。
//
// ⛔ 一律走 `Game.Input`（引擎封装：旧 InputManager / 新 InputSystem 双后端都能用）。
//    直连 `UnityEngine.Input` / `Keyboard.current` 在只有新后端的工程里**静默失效**
//    （`docs/步骤文档.md` §3.4 已记为坑）。
// ⛔ 屏幕 → 地面反投影**必须**用 `Core/Iso.ScreenToWorldOnGround`（`constraints.md` #6：
//    要显式给「到地面的距离」= -camera.z，否则点击位置整体偏移）。
//
// ── 悬停 / 光标（`docs/agents/agent-13-修复轮.md` §A）────────────────────────────
//   `Events.HoverTargetChanged` / `Events.CursorChanged` **全工程原先没有发送方**
//   ⇒ 本类补上：`UpdateHover` 每帧把 `HoverGrid` 交给 `HoverPicker` 解析，
//   目标变化时发 `HoverTargetChanged`（载荷 `Def.HoverTarget`）+ `CursorChanged`（载荷 `Def.CursorKind`）。
//   解析细节（怪物/地面物品/NPC、分层与契约缺口）见 `Module/Input/HoverPicker.cs` 头注释。
//
// ── 点 UI 的鼠标左键不再被读成"点地面"（R1-E 的 S2）──────────────────────────────
//   症状（`策划/自审对比/实机-A.md:37` 记的实机现象）：点商店格/面板按钮时，同一次左键
//   既被 uGUI 吃掉、又被这里当成"点地面"⇒ 角色乱走；落点若在 NPC 的 `TalkRange` 内
//   还会触发 `NpcModule.TryAutoInteract` **自动开对话顶掉商店面板**。
//   修法：`TryGetGroundClick` / `TryGetGroundHoldTarget` 两个取点入口在反投影**之前**
//   先过 `UiEatsIntent(按下/按住, 指针是否在 UI 上)`（纯函数，离线宿主逐行断言）。
//   ⛔ 判定源**不读裸 `UnityEngine.Input`**：走 `UnityEngine.EventSystems` 的指针命中
//      （见本文件下方 `UiPointerProbe` 的说明：为什么走反射、为什么不能直接 using 那个类型）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

// ═════════════════════════════════════════════════════════════════════════════
// ⚠️ 命名空间**刻意**用 `Diablo2.Module`，**不是** `Diablo2.Module.Input`（目录仍是 `Module/Input/`）。
//
// 三条理由（第 1 条是实测报错，见 `Module/Camera/CameraRig.cs` 的同款注释）：
//   ① 命名空间 `Diablo2.Module.X` 里若 X 与常用 Unity 类型同名（`Camera` / `Input`），
//      会让**整个程序集**里裸写该类型名的文件报 CS0118（真报错过：`MapView.cs` 的 `Camera`）；
//   ② `PlayerModule` 要持有本助手，若它在子命名空间（`Diablo2.Module` + 点 + `Input`）里，
//      就得写一条 `using` 指令去引它 —— 那正是分层自检 ②「跨模块耦合」的 grep 模式
//      （正则 `using Diablo2\.Module\.`），会变成假阳性、掩盖真问题；
//   ③ `Diablo2.Module` 是 `Diablo2.Module.Player` 的外层命名空间 ⇒ 引用本类**不需要任何 using**。
// ═════════════════════════════════════════════════════════════════════════════
namespace Diablo2.Module
{
    /// <summary>输入读取助手（唯一读 `Game.Input` 的地方；状态按帧缓存）。</summary>
    internal sealed class InputReader
    {
        /// <summary>日志 tag。</summary>
        private const string Tag = "Input";

        /// <summary>滚轮轴名（与引擎自带的 `Runtime/Presentation/ThirdPersonCamera.cs:219` 同口径）。</summary>
        public const string ScrollWheelAxis = "Mouse ScrollWheel";

        private bool _down;
        private bool _held;
        private bool _up;
        private float _wheel;
        private Camera _cam;

        /// <summary>原生相机 API 在本进程不可用（离线宿主）⇒ 不再重试（只报一次）。</summary>
        private bool _camProbeBlocked;

        /// <summary>是否成功解析过相机（用来区分「首次拿到」与「旧引用失效后重新解析」）。</summary>
        private bool _camEverResolved;

        private bool _noCamLogged;
        private bool _noInputLogged;

        /// <summary>`[R1-E] S2` 只报一次：指针在 UI 上时的取点拦截口径（见 <see cref="IsPointerOverUi"/>）。</summary>
        private bool _uiBlockedLogged;

        private readonly HoverPicker _picker = new HoverPicker();

        /// <summary>
        /// 「此刻指针是否压在 UI 上」的判定源（**本项目新增的非契约注入点**，做法与
        /// <see cref="HoverPicker.GroundItemAt"/> 完全同一套：默认实现 + 宿主可整体替换）。
        /// <para>默认值 = <see cref="UiPointerProbe.PointerOverUi"/>（引擎/Unity 的
        /// `UnityEngine.EventSystems.EventSystem` 指针命中）；离线自检宿主注入替身来断言两种情形。</para>
        /// <para>⛔ 本字段**不参与 <see cref="Reset"/>**：它可能是宿主/集成方注入的替身（同 `GroundItemAt` 的口径）。</para>
        /// </summary>
        public Func<bool> PointerOverUi { get; set; }

        /// <summary>装上默认的 UI 命中判定源（见 <see cref="PointerOverUi"/>）。</summary>
        public InputReader()
        {
            PointerOverUi = UiPointerProbe.PointerOverUi;
        }
        private HoverTarget _hover = new HoverTarget { hasTarget = false, cursor = CursorKind.Default, id = -1 };
        private bool _hoverLogged;
        private int _lastMonsterHoverId = -1;

        /// <summary>鼠标所在格（相机不可用/输入不可用时保持上一次的值，仅作光标提示用）。</summary>
        public Vector2Int HoverGrid { get; private set; }

        /// <summary>悬停解析器（自证/调试用；可替换 <see cref="HoverPicker.GroundItemAt"/>）。</summary>
        public HoverPicker Hover => _picker;

        /// <summary>最近一次解析出的悬停目标（= `Events.HoverTargetChanged` 的载荷）。</summary>
        public HoverTarget CurrentHover => _hover;

        /// <summary>当前光标形态（= `Events.CursorChanged` 的载荷）。</summary>
        public CursorKind CurrentCursor => _hover != null ? _hover.cursor : CursorKind.Default;

        /// <summary>本帧左键是否按下（`Game.Input.GetMouseButtonDown(0)`）。</summary>
        public bool PrimaryDown => _down;

        /// <summary>本帧左键是否按住。</summary>
        public bool PrimaryHeld => _held;

        /// <summary>本帧左键是否抬起。</summary>
        public bool PrimaryUp => _up;

        /// <summary>本帧滚轮轴值（未 `Poll` 时为 0）。</summary>
        public float ScrollWheel => _wheel;

        /// <summary>输入后端是否可用（不可用时全部读取返回默认值，绝不抛异常）。</summary>
        public bool Available => Game.Input != null && Game.Input.Available;

        /// <summary>本帧是否有可用相机（屏幕 → 地面反投影的前提）。</summary>
        public bool HasCamera => ResolveCamera() != null;

        /// <summary>
        /// 每帧采样一次（`PlayerModule.Tick` 开头调用）。**只缓存，不做决策**。
        /// 输入后端不可用（`CloverInput.Init` 未调用 / 后端探测失败）时只报一次日志。
        /// </summary>
        public void Poll()
        {
            _down = _held = _up = false;
            _wheel = 0f;

            var input = Game.Input;
            if (input == null || !input.Available)
            {
                if (!_noInputLogged)
                {
                    _noInputLogged = true;
                    Log.Warn(Tag, "Game.Input 不可用（CloverInput.Init 未调用或输入后端探测失败）⇒ " +
                                   "点击移动/悬停/腰带快捷键全部失效（只报一次）");
                }
                return;
            }

            _down = input.GetMouseButtonDown(0);
            _held = input.GetMouseButton(0);
            _up = input.GetMouseButtonUp(0);
            _wheel = input.GetAxis(ScrollWheelAxis);

            var cam = ResolveCamera();
            if (cam != null)
            {
                HoverGrid = Iso.ScreenToGrid(cam, input.MousePosition);
            }
        }

        /// <summary>
        /// 本帧左键按下 → 该点的**地面格**（点击移动入口）。
        /// 相机不可用 / 输入不可用 → 返回 false 并（首次）留下可定位日志，不做任何"猜一个格"的兜底。
        /// <para>★ 指针压在 UI 上（点面板/面板按钮）⇒ 本次按下**不算**地面意图（见 <see cref="UiEatsIntent"/>）。</para>
        /// </summary>
        public bool TryGetGroundClick(out Vector2Int grid)
        {
            grid = HoverGrid;
            if (!_down) return false;
            if (UiEatsIntent(_down, IsPointerOverUi())) return false;
            return TryProjectMouse(out grid);
        }

        /// <summary>
        /// 按住左键时的当前目标格（配合 <see cref="ShouldRetarget"/> 实现「按住持续走」）。
        /// 未按住 → false。
        /// <para>★ 同 <see cref="TryGetGroundClick"/>：指针在 UI 上时按住也不产生地面目标
        /// （否则"按下 UI"的那一帧 `_held` 同时为真 ⇒ 会从这一支漏出一条移动/攻击）。</para>
        /// </summary>
        public bool TryGetGroundHoldTarget(out Vector2Int grid)
        {
            grid = HoverGrid;
            if (!_held) return false;
            if (UiEatsIntent(_held, IsPointerOverUi())) return false;
            return TryProjectMouse(out grid);
        }

        /// <summary>
        /// 「这次按下/按住是否该被 **UI 命中**吃掉」——**纯判定**（离线宿主逐行断言，不碰真实 EventSystem）：
        /// 只有"确实按下/按住" **且** "指针在 UI 上"才算被吃掉。
        /// <para>两种情形的判据（`tools/probes/hosts/playercheck` §12.7 逐行核对）：</para>
        /// <list type="bullet">
        /// <item><c>(true, true)</c> = 「UI 面板开着的区域」⇒ true ⇒ 不产生落点、不发 `MoveCommand`、
        /// 不触发 `NpcModule` 的自动对话（面板按钮照常被 uGUI 收到）。</item>
        /// <item><c>(true, false)</c> = 「面板外的地面」⇒ false ⇒ 照旧反投影成格（原版点击移动不受影响）。</item>
        /// </list>
        /// </summary>
        /// <param name="pressed">本帧左键是否按下（<see cref="PrimaryDown"/>）或按住（<see cref="PrimaryHeld"/>）。</param>
        /// <param name="pointerOverUi">指针是否压在 UI 上（<see cref="PointerOverUi"/> 的当前值）。</param>
        public static bool UiEatsIntent(bool pressed, bool pointerOverUi) => pressed && pointerOverUi;

        /// <summary>
        /// 目标格是否需要重算路径（**纯函数**，宿主可离线断言）。
        /// 规则（`docs/agents/agent-06-*` §4.4）：目标格变化**超过 1 格**才重算，避免每帧跑 A*。
        /// </summary>
        /// <param name="from">玩家当前格。</param>
        /// <param name="previous">上一次已经下发的目标格（`null` = 还没有）。</param>
        /// <param name="now">本次鼠标所指格。</param>
        public static bool ShouldRetarget(Vector2Int from, Vector2Int? previous, Vector2Int now)
        {
            if (now == from) return false;                       // 点到脚下：交给 MoveTo 的"已在原地"分支
            if (!previous.HasValue) return true;                 // 第一次按下：必须下发
            if (previous.Value == now) return false;             // 没变：不重算
            return Iso.GridDistance(previous.Value, now) > 1;    // 变了但只差 1 格：忽略（防抖动 + 省 A*）
        }

        // ── 悬停 / 光标（agent-13 §A）──────────────────────────────────────────

        /// <summary>
        /// 每帧：把 <see cref="HoverGrid"/> 解析成悬停目标，**变化时**发 `Events.HoverTargetChanged`
        /// + `Events.CursorChanged`（并通知 `IMonsterModule.SetHovered`）。
        /// `canInteract == false`（死亡/未进图）⇒ 冻结，不产生新事件（防死亡后光标乱跳）。
        /// </summary>
        public HoverTarget UpdateHover(bool canInteract)
        {
            if (!canInteract) return _hover;
            Publish(_picker.Resolve(HoverGrid));
            return _hover;
        }

        /// <summary>
        /// 解析**指定格**的悬停目标（**不改变 <see cref="CurrentHover"/>、不发事件**）。
        /// 点击派发（左键点怪）时用它拿到"这一格上是哪个目标"。
        /// </summary>
        public HoverTarget HoverAt(Vector2Int grid) => _picker.Resolve(grid);

        /// <summary>
        /// 自证/集成用（非契约）：强制指定悬停格。
        /// 离线自检宿主里 `Camera.main`/`Screen` 不可用（`SecurityException`）⇒ `Poll` 无法反投影，
        /// 由宿主直接给出格坐标来驱动悬停解析。
        /// </summary>
        public void OverrideHoverGrid(Vector2Int grid) => HoverGrid = grid;

        /// <summary>发布一次悬停目标（目标真的变了才发事件 + 打日志）。</summary>
        private void Publish(HoverTarget t)
        {
            if (t == null) return;
            if (SameAs(_hover, t)) return;

            _hover = t;
            Emit(Events.HoverTargetChanged, t);
            Emit(Events.CursorChanged, t.cursor);

            if (t.hasTarget && !_hoverLogged)
            {
                _hoverLogged = true;      // 至少记一条"悬停真的命中过目标"的证据（验收 #14）
                Log.Info(Tag, $"[Hover] 首个悬停命中：格=({t.gridX},{t.gridY}) cursor={t.cursor} id={t.id}「{t.name}」");
            }

            NotifyMonsterHovered(t);
        }

        /// <summary>把"当前悬停的怪物"同步给 `IMonsterModule.SetHovered`（只在变化时调用；无怪物则 -1）。</summary>
        private void NotifyMonsterHovered(HoverTarget t)
        {
            var id = t != null && t.cursor == CursorKind.Attack ? t.id : -1;
            if (id == _lastMonsterHoverId) return;
            _lastMonsterHoverId = id;

            var ctx = Diablo2.App.AppContext.I;
            var monster = ctx != null ? ctx.Monster : null;
            if (monster == null) return;                 // 怪物模块未接入：静默跳过（玩家/NPC/物品悬停不受影响）
            monster.SetHovered(id);
        }

        /// <summary>两个悬停目标是否等价（决定要不要发事件）。</summary>
        private static bool SameAs(HoverTarget a, HoverTarget b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            return a.hasTarget == b.hasTarget
                   && a.cursor == b.cursor
                   && a.id == b.id
                   && a.gridX == b.gridX
                   && a.gridY == b.gridY;
        }

        // ⛔ 这里**没有**「方向键备选移动」读取器：原版 D2 只有鼠标点地面移动，
        //   曾经那一族（读取器 + `Cfg` 开关 + `Game.Setting` 设置项）已按全局 skill §0
        //   「A 没有 ⇒ 不加」整体删除（删除清单与 grep 判据见验收表 U-1 与 bug 表 B35）。

        /// <summary>站立不动（原版 `Shift`）：按住时不因点击而移动（战斗模块判「原地攻击」用）。</summary>
        public bool StandStill => GetKey(GameKeyAlias.KeyStandStill);

        /// <summary>显示地面物品名（原版 `Alt`）。</summary>
        public bool ShowGroundItems => GetKey(GameKeyAlias.KeyShowGroundItems);

        /// <summary>本帧是否按下「走/跑切换」（原版 `R`）。</summary>
        public bool RunTogglePressed => GetKeyDown(GameKeyAlias.KeyRunToggle);

        /// <summary>`Game.Input.GetKey` 的薄封装（`Game.Input` 为 null 时返回 false）。</summary>
        public bool GetKey(GameKey key)
        {
            var input = Game.Input;
            return input != null && input.GetKey(key);
        }

        /// <summary>`Game.Input.GetKeyDown` 的薄封装（`Game.Input` 为 null 时返回 false）。</summary>
        public bool GetKeyDown(GameKey key)
        {
            var input = Game.Input;
            return input != null && input.GetKeyDown(key);
        }

        /// <summary>
        /// 腰带快捷键（原版数字键 1~4）→ 发 `Events.UseBeltRequest`（参数 = 0..3 格号）。
        /// `canUse` = 角色存活且未暂停；false 时不读、不发。
        /// </summary>
        public void PollHotkeys(bool canUse)
        {
            if (!canUse || !Available) return;

            for (var i = 0; i < GameConst.BeltSlots; i++)
            {
                var key = GameKeyAlias.BeltKey(i);
                if (key == GameKey.None) continue;
                if (!Game.Input.GetKeyDown(key)) continue;

                Log.Info(Tag, $"腰带 {i + 1} 键（{key}）按下 ⇒ 发 {Events.UseBeltRequest}（格号 {i}）");
                Emit(Events.UseBeltRequest, i);
            }
        }

        /// <summary>复位（回主菜单 / 换角色：清缓存与相机引用，下次 `Poll` 重新探测）。</summary>
        public void Reset()
        {
            _down = _held = _up = false;
            _wheel = 0f;
            HoverGrid = Vector2Int.zero;
            _noCamLogged = false;
            _noInputLogged = false;
            _cam = null;
            _camProbeBlocked = false;
            _camEverResolved = false;
            _hover = new HoverTarget { hasTarget = false, cursor = CursorKind.Default, id = -1 };
            _hoverLogged = false;
            _lastMonsterHoverId = -1;
            _uiBlockedLogged = false;
            // ⚠️ 不动 `_picker.GroundItemAt`：那可能是宿主/集成方注入的替身，复位不该把它清掉。
            //    同理**不动** `PointerOverUi`（同一类注入点，见其属性注释）。
        }

        // ── 内部 ─────────────────────────────────────────────────────────────

        /// <summary>
        /// 指针是否压在 UI 上（取 <see cref="PointerOverUi"/> 的当前值；判定源抛异常 ⇒ 按"不在 UI 上"降级）。
        /// <para>★ `[R1-E] S2` 的**只报一次**留痕就在本方法里：第一次真命中时打一条 Info，
        /// 把"生效口径"写清楚（点了面板就不会再产生移动指令）；不是高频回调，故用就地一次性标志。</para>
        /// </summary>
        private bool IsPointerOverUi()
        {
            var probe = PointerOverUi;
            if (probe == null) return false;               // 未接线（离线宿主未注入替身）⇒ 判不出 ⇒ 不拦

            bool over;
            try
            {
                over = probe();
            }
            catch (Exception e)
            {
                if (!_uiBlockedLogged)
                {
                    _uiBlockedLogged = true;
                    Log.Warn(Tag, $"UI 命中判定抛异常（{e.GetType().Name}: {e.Message}）⇒ 本局按"
                        + "「指针不在 UI 上」处理（点击照旧走地面；只报一次）");
                }
                return false;
            }

            if (over && !_uiBlockedLogged)
            {
                _uiBlockedLogged = true;
                Log.Info(Tag, "[R1-E] S2 生效：指针压在 UI 上 ⇒ 左键不再当作「点地面 / 按住走」的"
                    + "地面意图（不发 MoveCommand、不触发 Npc 自动对话）；指针离开 UI 后照旧。"
                    + "判定源 = UnityEngine.EventSystems 的指针命中（非裸 UnityEngine.Input）");
            }
            return over;
        }

        /// <summary>鼠标屏幕坐标 → 地面格（相机不可用时返回 false + 首次日志）。</summary>
        private bool TryProjectMouse(out Vector2Int grid)
        {
            grid = Vector2Int.zero;
            var input = Game.Input;
            if (input == null || !input.Available)
            {
                if (!_noInputLogged) Log.Warn(Tag, "TryGetGroundClick：Game.Input 不可用，本次点击被忽略");
                return false;
            }

            var cam = ResolveCamera();
            if (cam == null)
            {
                if (!_noCamLogged)
                {
                    _noCamLogged = true;
                    Log.Warn(Tag, "TryGetGroundClick：找不到主相机（Camera.main 为 null 或取用抛异常）⇒ " +
                                  "本次点击无法反投影为格（只报一次）；请确认 Stage 场景有 MainCamera 标签的相机，" +
                                  "或由集成方调 CameraRig.BindCamera");
                }
                return false;
            }

            grid = Iso.ScreenToGrid(cam, input.MousePosition);
            return true;
        }

        /// <summary>
        /// 取当前可用的主相机（**每帧校验缓存**；与 `CameraRig.ResolveCamera` 同一处置，见其文件头
        /// 「缺陷修复」）：引擎换场是**单场景加载**（`Runtime/Presentation/Scene.cs:27`）⇒
        /// Boot → Menu → Stage 每次都会销毁上一台主相机；缓存住那个「已销毁引用」会让
        /// **点击移动的屏幕反投影静默失效**（点哪都不走，且没有任何日志）。
        /// </summary>
        private Camera ResolveCamera()
        {
            // ① 缓存仍有效（Unity 的 `==` 重载会把「已销毁对象」判成 null）⇒ 直接用
            if (_cam != null && IsUsable(_cam)) return _cam;

            // ② 本进程的原生相机 API 不可用（离线宿主）：不再重试
            if (_camProbeBlocked) return null;

            // ③ 缓存失效 / 还没有 ⇒ 重新解析
            Camera found;
            try
            {
                found = Camera.main;
            }
            catch (System.Exception e)
            {
                _camProbeBlocked = true;
                Log.Warn(Tag, $"取 Camera.main 抛异常（{e.GetType().Name}: {e.Message}）⇒ 本进程按无相机处理" +
                              "（不再重试；离线自检宿主走这一条）");
                return null;
            }

            if (found == null || !IsUsable(found)) return null;

            Log.Info(Tag, $"已找到主相机：{found.name} orthographic={found.orthographic} " +
                          $"pos={found.transform.position.ToString("F2")}（反投影按 z=0 地面算）"
                          + (_camEverResolved ? "｜原相机引用已失效（场景已切换？）⇒ 已重新解析" : ""));

            _cam = found;
            _camEverResolved = true;
            _noCamLogged = false;
            return _cam;
        }

        /// <summary>
        /// 相机是否可用（`== null` 覆盖「已销毁对象」，`isActiveAndEnabled` 覆盖「被禁用」）。
        /// 原生 API 不可用时（离线宿主）判为不可用并只报一次。
        /// </summary>
        private bool IsUsable(Camera c)
        {
            if (c == null) return false;
            try
            {
                return c.isActiveAndEnabled;
            }
            catch (System.Exception e)
            {
                if (!_camProbeBlocked)
                {
                    _camProbeBlocked = true;
                    Log.Warn(Tag, $"探测相机可用性抛异常（{e.GetType().Name}: {e.Message}）⇒ 本进程按无相机处理");
                }
                return false;
            }
        }

        /// <summary>事件发送（`Game.Event` 为 null 时只打日志，不崩）。</summary>
        private static void Emit<T>(string eventName, T arg)
        {
            if (Game.Event == null)
            {
                Log.Warn(Tag, $"Game.Event 为 null（Game.Launch 未调用？）⇒ 事件 {eventName} 未能发出");
                return;
            }
            Game.Event.Emit(eventName, arg);
        }
    }

    /// <summary>
    /// 「指针是否压在 UI 上」的**默认判定源**（`InputReader.PointerOverUi` 的默认值）——
    /// 走 Unity/uGUI 的 `UnityEngine.EventSystems.EventSystem.IsPointerOverGameObject()`。
    ///
    /// <para>★ 为什么用反射，而不是直接 `using UnityEngine.EventSystems;`：</para>
    /// <para>`EventSystem` 定义在 **uGUI 包程序集 `UnityEngine.UI`** 里（不是 UnityEngine 内置模块），
    /// 而本目录（`Module/Input/*.cs`）被离线自检宿主 `tools/probes/hosts/playercheck` **编入**，
    /// 那个宿主只引用 `UnityEngine.CoreModule` / `JSONSerializeModule` —— 一旦这里出现
    /// `EventSystem` 这个**类型名**，那个宿主立刻 CS0246 编译失败（而项目规定不许改它的 `.csproj`）。
    /// 反射把这条依赖变成**运行期可选**：解析不到（离线宿主）⇒ 恒 <c>false</c>（按"指针不在 UI 上"
    /// 降级，与改动前逐字一致），Play 下解析得到 ⇒ 真判定。</para>
    ///
    /// <para>★ 为什么不用裸 `UnityEngine.Input`：本项目 `Active Input Handling` 可能是"只新输入"，
    /// 裸 `Input.*` 会抛异常静默失效（`docs/步骤文档.md` §3.4 已记为坑）；`EventSystem` 由引擎在
    /// `CloverInput.Init()` 时保证存在（`Runtime/Presentation/Input.cs:941 EnsureEventSystem`），
    /// 且它自己就是两套输入后端都认的**指针命中**权威（uGUI 的 `PointerInputModule` 口径）。</para>
    ///
    /// <para>★ 代价与边界：`EventSystem.current` / `IsPointerOverGameObject()` 每帧最多解析一次
    /// （`PropertyInfo` / `MethodInfo` 缓存一次，之后只有一次属性读 + 一次方法调用），且**只在
    /// 左键按下/按住的帧**才会被调用（不是逐帧高频路径）。</para>
    /// </summary>
    internal static class UiPointerProbe
    {
        /// <summary>uGUI 的 EventSystem 类型（程序集限定名；uGUI 包恒为 `UnityEngine.UI`）。</summary>
        private const string EventSystemTypeName = "UnityEngine.EventSystems.EventSystem, UnityEngine.UI";

        private static bool _resolved;
        private static System.Reflection.PropertyInfo _currentProp;   // EventSystem.current（静态属性）
        private static System.Reflection.MethodInfo _isOver;          // IsPointerOverGameObject()（无参重载）
        private static bool _failedLogged;

        /// <summary>指针是否压在 UI 上；判不出（无 uGUI / 无 EventSystem / 解析失败）⇒ false。</summary>
        public static bool PointerOverUi()
        {
            Resolve();
            if (_currentProp == null || _isOver == null) return false;

            try
            {
                var es = _currentProp.GetValue(null);
                if (es == null) return false;               // 场景里还没有 EventSystem（引擎 Init 之前）
                return _isOver.Invoke(es, null) is bool over && over;
            }
            catch (Exception e)
            {
                if (!_failedLogged)
                {
                    _failedLogged = true;
                    Log.Warn("Input", $"调用 EventSystem.IsPointerOverGameObject() 抛异常"
                        + $"（{e.GetType().Name}: {e.Message}）⇒ 本局按「指针不在 UI 上」处理（只报一次）");
                }
                return false;
            }
        }

        /// <summary>解析一次 EventSystem 的反射入口（失败即永久记为"无 uGUI"，不再重试）。</summary>
        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;

            var type = Type.GetType(EventSystemTypeName, false);
            if (type == null)
            {
                // 非预期但**可解释**：离线宿主没引用 uGUI / 该工程不含 uGUI 包。
                // 不打日志（离线宿主每次跑都会看到"正常降级"当告警，反而掩盖真问题）；
                // Play 下若真走到这里，`Input.Infrastructure.EnsureEventSystem` 那条 Info 已经证明
                // "EventSystem 存在"，与本条矛盾 ⇒ 由那一条定位。
                return;
            }

            _currentProp = type.GetProperty("current",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            _isOver = type.GetMethod("IsPointerOverGameObject", Type.EmptyTypes);

            if (_currentProp == null || _isOver == null)
            {
                Log.Warn("Input", $"UI 命中判定：`{type.FullName}` 上找不到 `current` / "
                    + "`IsPointerOverGameObject()`（uGUI 版本差异？）⇒ 本局按「指针不在 UI 上」处理");
            }
        }
    }
}
