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
//   ⛔ 判定源**不读裸 `UnityEngine.Input`**：转调**引擎探针** `Game.Input.PointerOverUi`
//      （内部走 uGUI `UnityEngine.EventSystems` 的指针命中；离线宿主 / 无 EventSystem ⇒ 恒 false；
//      见本文件下方 `UiPointerProbe` 的说明：为什么判定源归引擎、为什么本文件不出现 uGUI 类型名）。
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

        /// <summary>滚轮轴名（与引擎自带的 `Runtime/Presentation/CloverThirdPersonCamera.cs:221` 同口径
        /// —— 该处处说明「滚轮轴（"Mouse ScrollWheel"）两个后端都已支持」，用点在 `:238` 的
        /// `input.GetAxis("Mouse ScrollWheel")`；轴名换算见 `Runtime/Presentation/Input.cs:544/563`。
        /// ★ 2026-09-23 更正：原文引的 `ThirdPersonCamera.cs:219` **全盘不存在**（该文件已更名/拆分为
        /// `CloverThirdPersonCamera.cs`）⇒ 由 audit-C-logic-num §13 的引用可达性复核抓出，本行按现盘更正）。</summary>
        public const string ScrollWheelAxis = "Mouse ScrollWheel";

        /// <summary>左键的鼠标键号（原版「左键 = 左手技能」，`Game.Input.GetMouseButton*(0)`）。</summary>
        public const int PrimaryMouseButton = 0;

        /// <summary>
        /// 右键的鼠标键号（原版「右键 = 右手技能」；★ impl-I-input 新增读取点，见 <see cref="SecondaryDown"/>）。
        /// <para>取值出处 = Unity/引擎 `Game.Input.GetMouseButton(int)` 的键号约定（0 = 左 / 1 = 右 / 2 = 中），
        /// 与既有 <see cref="PrimaryMouseButton"/> 的 `0` 同源；这是唯一的右键读取点。</para>
        /// </summary>
        public const int SecondaryMouseButton = 1;

        private bool _down;
        private bool _held;
        private bool _up;
        private bool _secDown;
        private bool _secHeld;
        private bool _secUp;
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
        /// <para>默认值 = <see cref="UiPointerProbe.PointerOverUi"/>（**转调引擎探针**
        /// `Game.Input.PointerOverUi` —— uGUI `EventSystem` 的指针命中，实现见
        /// `Runtime/Presentation/Input.cs`）；离线自检宿主注入替身来断言两种情形。</para>
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

        /// <summary>上一次广播出去的地面物品名牌（去重：内容没变就**不重发**，不是逐帧刷）。</summary>
        private readonly System.Collections.Generic.List<GroundItemLabel> _labelsPrev
            = new System.Collections.Generic.List<GroundItemLabel>();

        /// <summary>上一次广播时的 Alt 态（参与去重）。</summary>
        private bool _labelsAltPrev;

        /// <summary>`[GroundItemLabel]` 的非预期分支只报一次（Alt 查询抛异常 / 名牌层未接线）。</summary>
        private bool _labelsProbeWarned;

        /// <summary>鼠标所在格（相机不可用/输入不可用时保持上一次的值，仅作光标提示用）。</summary>
        public Vector2Int HoverGrid { get; private set; }

        /// <summary>
        /// 鼠标反投影到地面的**世界点**（与 <see cref="HoverGrid"/> **同一帧、同一次投影**得到）。
        /// ★ S3：悬停怪物要按「贴图实际矩形」命中（原版口径），而矩形判定要用世界点，
        /// 只有格是不够的（同一个格覆盖不到怪物上半身）。见 `HoverPicker.Resolve(Vector2Int, Vector2)`。
        /// </summary>
        private Vector3 _hoverWorld;

        /// <summary>
        /// <see cref="_hoverWorld"/> 是否与当前 <see cref="HoverGrid"/> 配对有效。
        /// <para>⛔ 只有 `Poll` 里**同一次**反投影算出来的两个值才算配对：任何"只给格、不给点"的
        /// 调用（<see cref="OverrideHoverGrid"/>、离线宿主）都会把它置 false ⇒ 那里**一字不变**地
        /// 走旧口径（怪物只认脚下格），不会用上一次的残留世界点误命中。</para>
        /// </summary>
        private bool _hoverWorldValid;

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

        /// <summary>
        /// 本帧**右键**是否按下（原版 D2：右键 = 使用**右手技能**）。
        /// <para>★ impl-I-input：改动前 `Poll()` 只读 button 0，全仓 0 处读 button 1
        /// ⇒ HUD 上已经画出来的 `RightSkill` 技能格**没有任何入口**（审计 R1）。
        /// 消费方 = `Module/Player/PlayerModule`（右键意图 → 已有的技能施放入口 `ISkillModule.TryCast`）。</para>
        /// </summary>
        public bool SecondaryDown => _secDown;

        /// <summary>本帧右键是否按住（原版按住右键持续使用右手技能）。</summary>
        public bool SecondaryHeld => _secHeld;

        /// <summary>本帧右键是否抬起。</summary>
        public bool SecondaryUp => _secUp;

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

            _down = input.GetMouseButtonDown(PrimaryMouseButton);
            _held = input.GetMouseButton(PrimaryMouseButton);
            _up = input.GetMouseButtonUp(PrimaryMouseButton);
            // ★ impl-I-input（审计 R1）：右键 = 右手技能，唯一读取点就是这三行（全仓别处不许再读 button 1）。
            _secDown = input.GetMouseButtonDown(SecondaryMouseButton);
            _secHeld = input.GetMouseButton(SecondaryMouseButton);
            _secUp = input.GetMouseButtonUp(SecondaryMouseButton);
            _wheel = input.GetAxis(ScrollWheelAxis);

            var cam = ResolveCamera();
            if (cam != null)
            {
                // ★ S3：格与世界点必须来自**同一次**反投影（`_hoverWorldValid` = 两者配对）。
                HoverGrid = Iso.ScreenToGrid(cam, input.MousePosition);
                _hoverWorld = Iso.ScreenToWorldOnGround(cam, input.MousePosition);
                _hoverWorldValid = true;
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
        /// 本帧**右键按下** → 该点的**地面格**（右键技能施放入口；★ impl-I-input 新增）。
        /// <para>与 <see cref="TryGetGroundClick"/> 同一套护栏：相机/输入不可用 ⇒ false + 首次日志；
        /// 指针压在 UI 上（点面板/按钮）⇒ 本次右键**不算**施放意图（<see cref="UiEatsIntent"/>）。
        /// **不产生任何移动意图**（原版右键不移动角色）。</para>
        /// </summary>
        public bool TryGetSecondaryClick(out Vector2Int grid)
        {
            grid = HoverGrid;
            if (!_secDown) return false;
            if (UiEatsIntent(_secDown, IsPointerOverUi())) return false;
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
        /// <para>规则出处 = `策划/策划案/暗黑破坏神2参考规格.md` §3.3 第 12 行「鼠标点击移动 … **按住左键持续更新目标**」
        /// （同口径：`策划/自审对比/README.md` 第 5 行「左键点地面 → 8 向逐格寻路」）⇒
        /// **目标格一变就跟着光标重算**，原版没有「必须差够 N 格」这一说。</para>
        /// <para>★ 2026-09-23 修 U33「鼠标在人附近移动没效果，必须要远」：删掉旧实现里两条**本项目自创**的抑制规则
        /// （它们的出处是派活文档 `docs/agents/agent-06-主角相机与输入.md` §4 第 4 条
        /// 「目标格变化超过 1 格才重算路径（避免每帧 A*）」—— 那是工程优化，**不是原版语义**）：
        /// <list type="bullet">
        /// <item>「目标只差 1 格 ⇒ 忽略」：鼠标**慢慢移动**时每帧只跨 1 格 ⇒ 永远触发不了重算，
        /// 只有把光标甩到 ≥2 格外才有反应 ⇒ 用户报的症状逐字对得上；</item>
        /// <item>「点到脚下格 ⇒ 忽略」：与**同一条链路**的单击分支不一致 ——
        /// `PlayerModule.HandlePrimaryClick` 对脚下格照样 `Emit(Events.MoveCommand)`，
        /// `MoveTo` 自己有「已在目标格 ⇒ 清空路径（原地不动）」分支 ⇒ 按住时把光标挪到脚下 = 该停下来。</item>
        /// </list>
        /// 唯一保留的节流 = **同一格不重算**（目标没跑 ⇒ 不重复跑 A*，与"避免每帧 A*"等价且不损失手感）。</para>
        /// </summary>
        /// <param name="from">玩家当前格（★ 2026-09-23 起**不参与**是否重算的判定，仅为保持调用点签名不变）。</param>
        /// <param name="previous">上一次已经下发的目标格（`null` = 还没有）。</param>
        /// <param name="now">本次鼠标所指格。</param>
        public static bool ShouldRetarget(Vector2Int from, Vector2Int? previous, Vector2Int now)
        {
            if (!previous.HasValue) return true;            // 第一次按下：必须下发
            if (previous.Value == now) return false;        // 目标没变：不重算（唯一的逐帧节流）
            return true;                                    // 变了就跟着光标重算（脚下格 / 相邻格同样算）
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
            // ★ S3：拿到了与 HoverGrid 同帧配对的世界点 ⇒ 走新口径（怪物按贴图实际矩形命中，
            //   原版语义）；否则（含 `OverrideHoverGrid` 的离线/自证路径）**一字不变**走旧口径。
            Publish(_hoverWorldValid ? _picker.Resolve(HoverGrid, _hoverWorld) : _picker.Resolve(HoverGrid));
            return _hover;
        }

        /// <summary>
        /// 发布**地面物品名牌**（事件 <see cref="Events.GroundItemLabelsChanged"/>；★ impl-I-input，审计 R5）。
        /// <para>两种触发（与原版一致）：① 按住 Alt（`altHeld = true`）⇒ **全部**地面物品；
        /// ② 没按 Alt 时，只显示**当前悬停的那一件**（`Events.HoverTargetChanged` 解析出的 `Pickup` 目标）。</para>
        /// <para>★ 为什么 Alt 态由**调用方**（`PlayerModule.Tick`）传进来，而不是本类自己读
        /// <see cref="ShowGroundItems"/>：审计 B 的静态对账口径是「属性消费点在 `InputReader` **之外**」
        /// （`audit-B-static-out.txt` ④：`InputReader` 内部引用不算消费方）⇒ 消费者放到 `PlayerModule`
        /// 那一条 `_input.ShowGroundItems` 上，脚本才判得出「已有消费方」。</para>
        /// <para>去重：Alt 态 + 名牌内容（id/格/名字）不变 ⇒ 不发事件、不打日志（本方法每帧被调）。</para>
        /// </summary>
        /// <param name="altHeld">原版 `Alt` 是否按住（调用方传 <see cref="ShowGroundItems"/>）。</param>
        public void PublishGroundItemLabels(bool altHeld)
        {
            var alt = altHeld;
            var args = new GroundItemLabelsArgs { altHeld = alt };

            if (alt)
            {
                var probe = _picker.AllLabels;
                if (probe == null)
                {
                    if (!_labelsProbeWarned)
                    {
                        _labelsProbeWarned = true;
                        Log.Warn(Tag, "Alt 常显地面物品名：HoverPicker.AllLabels 未接线 ⇒ 本次只显示悬停的那一件（只报一次）");
                    }
                }
                else
                {
                    try
                    {
                        var all = probe();
                        if (all != null) args.labels = all;
                    }
                    catch (Exception e)
                    {
                        if (!_labelsProbeWarned)
                        {
                            _labelsProbeWarned = true;
                            Log.Warn(Tag, $"枚举地面物品名牌抛异常（{e.GetType().Name}: {e.Message}）⇒ 本次按" +
                                          "「没有地面物品」处理（只报一次）");
                        }
                    }
                }
            }
            else if (_hover != null && _hover.hasTarget && _hover.cursor == CursorKind.Pickup)
            {
                var quality = ItemQuality.Normal;
                var qProbe = _picker.ItemQualityOf;
                if (qProbe != null)
                {
                    try { quality = qProbe(_hover.id); }
                    catch (Exception e)
                    {
                        if (!_labelsProbeWarned)
                        {
                            _labelsProbeWarned = true;
                            Log.Warn(Tag, $"查地面物品品质抛异常（{e.GetType().Name}: {e.Message}）⇒ 名牌按普通（白）配色（只报一次）");
                        }
                    }
                }

                args.labels.Add(new GroundItemLabel
                {
                    id = _hover.id,
                    name = _hover.name,
                    quality = quality,
                    gridX = _hover.gridX,
                    gridY = _hover.gridY,
                });
            }

            if (!LabelsChanged(args)) return;

            _labelsPrev.Clear();
            for (var i = 0; i < args.labels.Count; i++) _labelsPrev.Add(args.labels[i]);
            _labelsAltPrev = args.altHeld;

            Emit(Events.GroundItemLabelsChanged, args);

            if (args.labels.Count == 0)
            {
                Log.Info(Tag, "[GroundItemLabel] 名牌清空（Alt 已松开，且指针下没有地面物品）");
                return;
            }

            var first = args.labels[0];
            Log.Info(Tag, $"[GroundItemLabel] 名牌 {args.labels.Count} 条（altHeld={args.altHeld}）："
                + $"首个 #{first.id}「{first.name}」品质={first.quality} 格=({first.gridX},{first.gridY})"
                + (args.labels.Count > 1 ? $" …共 {args.labels.Count} 件（常显）" : string.Empty));
        }

        /// <summary>名牌内容是否与上一次广播的不同（Alt 态 + 条数 + 每条的 id/格/名字）。</summary>
        private bool LabelsChanged(GroundItemLabelsArgs args)
        {
            if (args == null) return false;
            if (args.altHeld != _labelsAltPrev) return true;
            if (args.labels == null) return _labelsPrev.Count > 0;
            if (args.labels.Count != _labelsPrev.Count) return true;

            for (var i = 0; i < args.labels.Count; i++)
            {
                var a = args.labels[i];
                var b = _labelsPrev[i];
                if (a == null || b == null) return true;
                if (a.id != b.id || a.gridX != b.gridX || a.gridY != b.gridY) return true;
                if (!string.Equals(a.name, b.name, StringComparison.Ordinal)) return true;
            }

            return false;
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
        public void OverrideHoverGrid(Vector2Int grid)
        {
            HoverGrid = grid;
            // ★ S3：合成的格**没有**配对的世界点 ⇒ 明确作废旧点，保证这里仍是"只认脚下格"的旧口径。
            _hoverWorldValid = false;
        }

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

        /// <summary>
        /// 本帧是否按下「切换武器组」（原版 `W`）。
        /// <para>键位**唯一来源** = <see cref="GameKeyAlias.KeySwapWeapon"/>；消费方 =
        /// `Module/Player/PlayerModule.Tick`（⇒ 发 `Events.SwapWeaponRequest`）。⛔ 本文件与消费方
        /// 都不出现 `GameKey.W` 字面量（改键位只改 `Def/GameKeyAlias.cs` 一处）。</para>
        /// </summary>
        public bool SwapWeaponPressed => GetKeyDown(GameKeyAlias.KeySwapWeapon);

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
        /// 腰带快捷键（原版数字键 1~4）→ 发 `Events.UseBeltRequest`（参数 = 0..3 格号）；
        /// **技能槽键**（原版 `F1`~`F8`）→ 发 `Events.SkillSlotAssignRequest`（参数 = 槽号 1..8）。
        /// `canUse` = 角色存活且未暂停；false 时不读、不发。
        /// <para>★ impl-I-input（审计 R4）：`GameKeyAlias.KeySkillSlot1..8` / `SkillSlotKey(int)` /
        /// `SkillSlotCount` 在改动前**全仓 0 消费**（登记了没消费者）。这里只做"读键 → 发意图"，
        /// 槽号 → 具体技能 id 的解析在 `Module/Skill/SkillModule`（业务不塞进输入层）。</para>
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

            // ── 技能槽键（F1~F8）→ 绑到左右键技能格（原版语义）──────────────
            for (var slot = 1; slot <= GameKeyAlias.SkillSlotCount; slot++)
            {
                var key = GameKeyAlias.SkillSlotKey(slot);
                if (key == GameKey.None) continue;
                if (!Game.Input.GetKeyDown(key)) continue;

                var hand = GameKeyAlias.SkillSlotIsLeftHand(slot) ? "左键" : "右键";
                Log.Info(Tag, $"技能槽键 {slot}（{key}）按下 ⇒ 发 {Events.SkillSlotAssignRequest}"
                    + $"（绑{hand}技能格；已学技能表下标 {GameKeyAlias.SkillSlotIndex(slot)}）");
                Emit(Events.SkillSlotAssignRequest, slot);
            }
        }

        /// <summary>复位（回主菜单 / 换角色：清缓存与相机引用，下次 `Poll` 重新探测）。</summary>
        public void Reset()
        {
            _down = _held = _up = false;
            _secDown = _secHeld = _secUp = false;
            _wheel = 0f;
            _labelsPrev.Clear();
            _labelsAltPrev = false;
            HoverGrid = Vector2Int.zero;
            _hoverWorld = Vector3.zero;
            _hoverWorldValid = false;      // ★ S3：重置后世界点与格同样作废（下一次 Poll 重新配对）
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
                    + "判定源 = 引擎探针 Game.Input.PointerOverUi（内部走 uGUI 指针命中，非裸 UnityEngine.Input）");
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
