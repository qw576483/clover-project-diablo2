// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Camera/CameraRig.cs
// `ICameraRig` 的**唯一实现**：固定等距角度的跟随相机（原版 D2 语义）。
//
// 为什么不用引擎 `Game.Camera.Follow`（`docs/步骤文档.md` §3.3 已注明）：
//   引擎那个 Follow 是「**锁 Z 的简单跟随**」（3D MMO 用），本项目是 2D 预投影等距画面，
//   机位要固定在世界 XY 平面正前方、并要按地图边界钳制 ⇒ 自己写（属业务）。
//
// ── 等距参数（本项目口径，验收报告要抄）──────────────────────────────────────
//   · 世界是 **XY 平面（z = 0）**：`Core/Iso.GridToWorld` 的返回 z 恒 0；
//   · 相机沿 **+Z 轴**正交俯视，机位 **z = -10**（= `-CameraDistance`）：
//     `Iso.ScreenToWorldOnGround` 用「到地面的距离 = -camera.z」做反投影（`constraints.md` #6），
//     所以这个 10 不是随手写的常数，它同时决定了点击反投影的正确性；
//   · **俯仰/偏航/翻滚 = 0**（`rotation = identity`）、**不旋转**；「等距」的观感来自
//     预投影的 128×64 瓦片（2:1 菱形，`GameConst.IsoTilePxW/H`），不是相机倾斜；
//   · 正交尺寸默认 **3.75**（= 视野高 **7.5 世界单位 = 7.5 格**；★ 片 2b 从 6 改来，
//     论据见 `DefaultOrthographicSize` 的注释 = 原版 `CameraController.CalcDesiredSize()`），
//     可调范围 [3, 12]；
//   · **默认不缩放**（原版如此，与 `ICameraRig` 注释「不旋转、不缩放」一致）：
//     滚轮缩放是**可选能力**，需显式打开 `EnableZoom = true`（原版无此功能）。
//
// ⛔ 不调用 `Game.Camera.Follow/Unfollow`（会锁 Z，语义不符）。
// ⛔ 屏幕/相机原生调用一律 try/catch 兜底并打日志：离线自检宿主（非 Unity 进程）里
//    `Camera.main`/`Screen` 不可用，必须**优雅降级**而不是抛异常（`tools/playercheck` 会断言）。
//
// ── ★ 缺陷修复（agent-22 §B1「等距相机不跟随主角」）────────────────────────────
//   **实测现象**（`_dev/p_a22_diag.cs`）：进图时 `Camera.main.position` = (1.00,-15.00,-10.00)
//   （= 出生格 (15,14) 的机位，正确）；随后 `Player.MoveTo((1,4))` 走到 (1,4)，
//   机位**一动不动**，主角落到屏幕 (600,1620)（屏幕高 1080）⇒ 跑出画面。
//   **根因**：焦点只在「进 Stage / 换区域」时被 `AppFlow` 设过一次（`SetTargetGrid`），
//   本类 `Tick` 从不重取焦点；`Follow(transform)` 存的 `_target` 也**从来没被读过**。
//   **修复**：新增 `RefreshFocus()`，在 `Tick` 里每帧刷新焦点
//     · `_target != null` ⇒ 读它的 `position`（契约 `Follow` 才真的会跟随）；
//     · 否则跟 `IPlayerModule.World`（原版语义：镜头跟着主角走）。
//   两条非预期分支都有日志：读目标失败 / 主角坐标非有限值。
// ─────────────────────────────────────────────────────────────────────────────

// ── ★ 缺陷修复（agent-14 §A「进图后地图只占屏幕下方一角、玩家看不见」）────────────
//   **实测根因**（探针 `client/_dev/p_cam.cs` 的原始数字，不是推断）：
//     · 引擎 `SceneModule.Load` 走 `SceneManager.LoadSceneAsync(name)`（**单场景**，见
//       `clover-client-unity-engine/Runtime/Presentation/Scene.cs:27`）⇒ Boot → Menu → Stage
//       每次换场都会把上一台 `Main Camera` **销毁**；
//     · 本类原先把 `Camera.main` 缓存在 `_cam` 并用 `_camLookupDone` 一锤子锁死 ⇒ 进 Stage 时
//       `_cam` 已是「已销毁对象」（Unity 的 `== null` 重载判为 null）而**永不重解析**；
//     · `ApplyToCamera()` 又在 `_cam == null` 时**静默 return**（无日志）⇒
//       `Rig.Position` 算对了 `(-4.00,-17.50,-10)`，但场景里的相机**整局停在出生机位
//       `(0,0,-10)`**，于是 32×32 等距菱形的顶角刚好落在屏幕中央附近、玩家 `(15,19)`
//       落在屏幕外 `(381.3, -479.2)`（Screen 1096×500）—— 与截图现象完全一致。
//   **修复**：① 缓存相机**每帧校验**（已销毁/被停用 ⇒ 重新解析并**重新锁等距参数 + 立即吸附**）；
//            ② `ApplyToCamera` 无相机时**必须留日志**（非预期分支不许静默）；
//            ③ 新增纯函数 `WorldToViewport` / `WorldToScreen`，给「焦点世界坐标 → 屏幕中心」
//               这条验收断言一个**可离线断言**的入口（`tools/playercheck` §11 用它）。
//   `Module/Input/InputReader.cs` 的 `ResolveCamera` 是同一个坑（点击移动会静默失效），同批修掉。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
// 别名：`AppContext` 与 BCL 的 `System.AppContext` 同名（同时 using System 会 CS0104）。
using AppContext = Diablo2.App.AppContext;
// 别名到 `UnityEngine.Camera`：本文件所在命名空间里不能出现叫 `Camera` 的名字（见下条注释），
// 全文只用 `UnityCamera`，把这个坑钉死。
using UnityCamera = UnityEngine.Camera;

// ═════════════════════════════════════════════════════════════════════════════
// ⚠️ 命名空间**刻意**用 `Diablo2.Module`，**不是** `Diablo2.Module.Camera`（目录仍是 `Module/Camera/`）。
//
// 实测（本机 `dotnet build tools/playercheck` 的真实报错，不是猜的）：
//   只要**存在**命名空间 `Diablo2.Module.Camera`，整个程序集里所有裸写 `Camera` 的地方都会
//   解析到这个**命名空间**而不是 `UnityEngine.Camera` ⇒
//     `Module/Map/MapView.cs(47,16): error CS0118: “Camera”是 命名空间，但此处被当做 类型 来使用`
//   （`Camera` 是 Unity 最常用的类型之一，`Diablo2.Module` 的成员优先于 `using UnityEngine;`。）
//   ⇒ 用一个「和目录同名」的命名空间会**打断别人已交付的文件**，代价远大于收益。
// 故：`CameraRig` 与 `ICameraRig` 同处 `Diablo2.Module`（同文件族里读起来也最自然），
//     目录仍按 `tools/ai-skill/registry.md` 的约定是 `Module/Camera/`。
//     `Module/Input/InputReader.cs` 同理（`Diablo2.Module.Input` 会与 `UnityEngine.Input` 同名）。
//     ⇒ 已回报主 agent 记入 registry/conventions。
// ═════════════════════════════════════════════════════════════════════════════
namespace Diablo2.Module
{
    /// <summary>等距跟随相机（固定角度、平滑跟随、可选缩放/边缘滚动）。</summary>
    internal sealed class CameraRig : ICameraRig
    {
        /// <summary>日志 tag。</summary>
        private const string Tag = "Camera";

        /// <summary>相机到地面（z=0）的距离 ⇒ 机位 z = -10。见文件头说明。</summary>
        public const float CameraDistance = 10f;

        /// <summary>
        /// 默认正交尺寸（**半高**，世界单位）⇒ 视野高 = 2 × 3.75 = **7.5 世界单位 = 7.5 格**。
        /// <para>★ **片 2b 修正：原值 6（视野高 12 格）是原版的 1.6 倍** —— 用户说"视野太高、能看到的东西太多"。</para>
        /// <para>出处（逐条可查）：
        /// ① 原版按**像素高**反推正交尺寸：`原版资源/参考工程_Diablerie/.../Engine/CameraController.cs:38-41`
        /// 的 `CalcDesiredSize() =&gt; camera.pixelHeight / Iso.pixelsPerUnit / 2`；
        /// ② `Engine/Iso.cs:10` 的 `pixelsPerUnit = 80`；
        /// ③ 原版 800×600 分辨率下 `pixelHeight = 600` ⇒ **600 / 80 / 2 = 3.75**（世界单位）。
        /// 本项目 1 格 = 1 世界单位高（`GameConst.TileSize`）⇒ 可见 **7.5 个格高**。</para>
        /// <para>为什么取 600（而不是 800/1080）：原版 D2 的**参考分辨率就是 640×480 / 800×600**
        /// （`Diablerie` 的 `CameraController` 按 `camera.pixelHeight` 算，即"视野高度固定，
        /// 宽高比变宽只会看到更宽"）；600 是其中最高的那一档，取它得到**最宽松**的视野上限，
        /// 再大就明确超出原版范围（旧值 6 = 12 格高 = 原版 960px 高，原版没有这种分辨率）。</para>
        /// </summary>
        public const float DefaultOrthographicSize = 3.75f;

        /// <summary>正交尺寸下限（再小就只剩 3 格视野）。</summary>
        public const float MinOrthographicSize = 3f;

        /// <summary>正交尺寸上限。</summary>
        public const float MaxOrthographicSize = 12f;

        /// <summary>每格滚轮改变的正交尺寸。</summary>
        public const float ZoomStepPerNotch = 0.5f;

        /// <summary>
        /// 跟随阻尼时间常数（秒）—— **临界阻尼**（`CloverEngine.CameraMath.SmoothDamp`，带速度状态）。
        ///
        /// <para>★ 本片（用户：「镜头移动的很抖，不平滑」）从 **0.12 改到 0.02**，换掉原「纯指数滞后」。
        /// 为什么是 0.02，两条都要写清（⛔ 不许写"看起来合理"）：</para>
        /// <list type="number">
        /// <item><b>为什么能消除"换向摆幅"（主因）</b>：临界阻尼的稳态滞后有解析上界
        /// <c>≤ 目标速度 × smoothTime</c>（连续二阶方程 <c>y''+2ωy'+ω²y=ω²x</c>、<c>ω=2/smoothTime</c>
        /// 的稳态解 <c>y = v·t − v·smoothTime</c>；Unity 的离散实现实测更小，为解析值的 0.577 倍
        /// —— 两处都断言在 `tools/probes/hosts/playercheck`）。
        /// 而 A* 走 8 向锯齿（每 1~2 格换向）⇒ 滞后矢量随行进方向每帧转 45° ⇒ 相机相对玩家的**横向**
        /// 偏移峰峰值 ≈ <c>2 × 速度 × smoothTime × sin45°</c>：
        /// 旧值 0.12 ⇒ 2×3.0×0.12×0.707 = **0.51 格**（1 格 = 144 屏幕像素 @1080p/orthoSize 3.75 ⇒ **73 px**，肉眼必见摆动）；
        /// 新值 0.02 ⇒ 解析上界 2×3.0×0.02×0.707 = **0.085 格**（12 px），而本机**离线配对实测**
        /// （playercheck c8，同一段锯齿路径只换相机口径）旧 0.51 格 → 新 **0.0148 格**（≈2 px）⇒ 降到 3%。</item>
        /// <item><b>为什么不是 0（完全刚性）</b>：0.02 s = 1.2 帧 @60fps，仍能吸收**单帧 dt 尖峰**
        /// （帧节奏虽被 `Core/FramePacing` 钉在 60/无 vSync，但仍会有偶发长帧；刚性跟随会把长帧直接
        /// 变成一次画面跳跃）。0.02 的代价是 3.0×0.02 = 0.06 格 ≈ 8.6 px 的稳态偏移（滞回方向朝行进方向），
        /// 这比 0.12 的 0.36 格 ≈ 52 px 小一个量级。</item>
        /// </list>
        /// <para>⚠️ **滞后量的原版出处缺失**：原版参考工程（`原版资源/参考工程_Diablerie`）本机只有
        /// `d2lod1.10txt` 数据表，**没有** `Engine/CameraController.cs`（该文件路径在本仓不存在，已全盘查过）
        /// ⇒ 无法证明"原版相机有无跟随滞后"。故本值按用户明说的「消除抖动」目标取，**并已在回报里登记该缺口**。</para>
        /// </summary>
        public const float FollowSmoothTime = 0.02f;

        /// <summary>
        /// 「8 向锯齿路径上，相机与玩家的相对偏移**横向**摆幅」的验收上界（格）。
        /// <para>量法（与 `tools/probes/drivers/camjitter_metrics.py` 逐字一致）：取每帧玩家位移方向 `u` 的
        /// 正交方向 `perp`，摆幅 = `max(r·perp) - min(r·perp)`，`r` = 玩家世界坐标 − 相机世界坐标。</para>
        /// <para>**来历（两个数都要写清）**：
        /// ① **解析上界**（对任意路径成立，故用它可以判别的路径）：摆幅 ≈ `2 × 速度 × smoothTime × sin45°`
        /// = 2 × 3.0 × 0.02 × 0.7071 = **0.085 格** ⇒ 阈值取 0.09（留 ~6% 余量）；
        /// ② **本机实测**（`tools/probes/hosts/playercheck` c7，Town 地图固定种子、同一段 8 向锯齿路径）：
        /// **0.0148 格**（只有解析上界的 17%）。
        /// 旧口径（τ=0.12 纯指数滞后）同式 = 0.51 格，且 c8 的**配对实测**给出旧口径的真实值
        /// ⇒ 本阈值能把旧口径判红（0.51 ≫ 0.09）。</para>
        /// <para>⚠️ 本行的 Play 侧采集被**他人在飞的编译错误**阻塞（详见 `.ai-tmp/test/report-camera.md`），
        /// 上列实测值来自**离线同源量法**（同一把尺：c7 的横向分量定义与 `camjitter_metrics.py` 逐字一致）。</para>
        /// </summary>
        public const float ZigZagLateralSwingMax = 0.09f;

        /// <summary>镜头震动的角频率（rad/s，纯三角函数偏移，不用随机数 ⇒ 可复现）。</summary>
        public const float ShakeAngularSpeed = 42f;

        /// <summary>边缘滚动的触发边距（像素）。</summary>
        public const float EdgeScrollMarginPx = 8f;

        /// <summary>边缘滚动最大位移（世界单位，防止把镜头拖到天涯海角）。</summary>
        public const float EdgeScrollMaxPan = 4f;

        /// <summary>边缘滚动速度（世界单位/秒，满偏时）。</summary>
        public const float EdgeScrollSpeed = 8f;

        /// <summary>滚轮轴名（与引擎 `Runtime/Presentation/ThirdPersonCamera.cs:219` 同口径）。</summary>
        public const string ScrollWheelAxis = "Mouse ScrollWheel";

        /// <summary>可选：滚轮缩放（**默认关闭** —— 原版 D2 不缩放）。</summary>
        public bool EnableZoom;

        /// <summary>可选：鼠标贴屏幕边缘滚动视野（**默认关闭** —— 原版只有跟随，没有边缘滚动）。</summary>
        public bool EnableEdgeScroll;

        private Transform _target;
        private bool _hasFocus;
        private Vector3 _focus;                // 关注点世界坐标（z=0 平面）
        private Vector3 _pos;                  // 跟随位置（已含边界钳制）
        private Vector3 _camVelocity;          // 临界阻尼跟随的**速度状态**（跨帧保留；见 FollowSmoothTime 注释）
        private Vector2 _panOffset;            // 边缘滚动累积位移
        private bool _snapPending;
        private float _ortho = DefaultOrthographicSize;

        private float _shakeAmp;
        private float _shakeDur;
        private float _shakeT;

        private UnityCamera _cam;

        /// <summary>原生相机 API 在本进程不可用（离线宿主）⇒ 不再重试（只报一次）。</summary>
        private bool _camProbeBlocked;

        /// <summary>是否成功解析过相机（用来区分「首次拿到」与「旧引用失效后重新解析」）。</summary>
        private bool _camEverResolved;

        private bool _camWarned;
        private bool _isoLockLogged;
        private bool _applyFailedLogged;

        /// <summary>「没有可用相机 ⇒ 本帧机位没写进场景」是否已报过（只报一次）。</summary>
        private bool _applySkippedLogged;
        private bool _zoomClampLogged;
        private bool _noMapLogged;
        private bool _ctxMissingLogged;

        /// <summary>「已开始跟随主角」只报一次。</summary>
        private bool _followPlayerLogged;

        /// <summary>「主角坐标非有限值」只报一次。</summary>
        private bool _focusInvalidLogged;

        /// <summary>
        /// 构造即订阅 `Events.StageEntered`（★ 片 2b：进 Stage 时打一条**取景口径**日志）。
        /// <para>为什么要这条日志：正交尺寸是"视野能看到多少格"的唯一决定量，改了它以后
        /// 必须能一眼从日志里核对"实际生效值 + 可见格高"，否则只能靠截图目测
        /// （`Game.Event` 为 null 时只降级、不抛异常，与 `Module/View/ViewModule` 同一处置）。</para>
        /// </summary>
        public CameraRig()
        {
            if (Game.Event == null)
            {
                Log.Warn(Tag, "CameraRig 构造时 Game.Event 为 null（Game.Launch 未调用）⇒ " +
                              "进 Stage 的取景日志不会打；正交尺寸仍按 DefaultOrthographicSize 生效");
                return;
            }

            Game.Event.On(Events.StageEntered, OnStageEntered);
        }

        /// <summary>
        /// `Events.StageEntered`：打一条取景日志 —— **实际** `orthographicSize` 与
        /// 「可见格高 = 2 × size」（本项目 1 格 = 1 世界单位高，见 `GameConst.TileSize`）。
        /// <para>实际值优先**从相机读回**（可能被场景/别处改过），读不到（离线宿主无原生相机）才用内部字段。</para>
        /// </summary>
        private void OnStageEntered()
        {
            var size = _ortho;
            var src = "内部字段（本进程无可用相机）";
            var cam = ResolveCamera();
            if (cam != null)
            {
                try
                {
                    size = cam.orthographicSize;
                    src = "Camera.orthographicSize 读回值";
                }
                catch (Exception e)
                {
                    Log.Warn(Tag, $"进 Stage 读回相机 orthographicSize 失败（{e.GetType().Name}: {e.Message}）" +
                                  "⇒ 用内部字段值");
                }
            }

            Log.Info(Tag, $"进入 Stage：正交尺寸 orthographicSize={size:0.###}（{src}），" +
                          $"可见格高 = 2×size = {2f * size:0.###} 格（宽 = 2×size×aspect）；" +
                          $"默认值 {DefaultOrthographicSize}（原版 600px/80ppu/2，见 CameraRig.DefaultOrthographicSize 注释）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ICameraRig
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Follow(Transform target)
        {
            if (target == null)
            {
                Log.Warn(Tag, "Follow(null) 被调用 ⇒ 忽略（保持当前机位）");
                return;
            }

            _target = target;
            _hasFocus = true;
            _focus = target.position;
            Log.Info(Tag, $"开始跟随 {target.name}（位置 {_focus.x:0.00},{_focus.y:0.00}）");
        }

        /// <inheritdoc />
        public void Unfollow()
        {
            if (_target == null)
            {
                Log.Warn(Tag, "Unfollow：当前没有跟随目标 ⇒ 忽略");
                return;
            }

            Log.Info(Tag, "取消跟随（机位停在当前位置，不飞走）");
            _target = null;
        }

        /// <inheritdoc />
        public void SnapToTarget()
        {
            if (!_hasFocus)
            {
                Log.Warn(Tag, "SnapToTarget：还没有焦点（先 Follow 或 SetTargetGrid）⇒ 忽略");
                return;
            }

            _snapPending = true;
            Log.Info(Tag, $"机位吸附请求：焦点 ({_focus.x:0.00},{_focus.y:0.00})（进图/传送后不要平滑）");
        }

        /// <inheritdoc />
        public void SetTargetGrid(Vector2Int grid)
        {
            _target = null;                                   // 用格坐标做焦点（不持有 Transform）
            _focus = Iso.GridToWorld(grid);                   // 格中心 → 世界（z=0）
            _hasFocus = true;
            TargetGrid = grid;
            Log.Info(Tag, $"跟随目标格已设为 ({grid.x},{grid.y}) → 世界 ({_focus.x:0.00},{_focus.y:0.00})");
        }

        /// <inheritdoc />
        public void Shake(float amplitude, float duration)
        {
            if (amplitude <= 0f || duration <= 0f)
            {
                Log.Warn(Tag, $"Shake(amp={amplitude}, dur={duration}) 参数非正 ⇒ 忽略");
                return;
            }

            _shakeAmp = amplitude;
            _shakeDur = duration;
            _shakeT = 0f;
            Log.Info(Tag, $"镜头震动：幅度 {amplitude:0.##}、时长 {duration:0.##}s（三角函数偏移，无随机数）");
        }

        /// <inheritdoc />
        public void Tick(float dt)
        {
            ResolveCamera();
            RefreshFocus();                 // ★ agent-22 §B1：每帧刷新焦点（原先焦点只在进图时设一次）

            if (_hasFocus)
            {
                var want = DesiredPosition(_focus, -CameraDistance) + new Vector3(_panOffset.x, _panOffset.y, 0f);
                if (_snapPending)
                {
                    _pos = want;
                    _camVelocity = Vector3.zero;     // 吸附 ⇒ 速度状态一起清（否则下一帧平滑会带上旧速度冲一下）
                    _snapPending = false;
                }
                else if (dt > 0f)
                {
                    // ★ 临界阻尼跟随（带速度状态）：换向时速度连续 ⇒ 不再"滞后矢量转 45°"。
                    //   纯指数滞后（本行原先的 SmoothTowards）已删；实现只在引擎
                    //   `CloverEngine.CameraMath.SmoothDamp`（⛔ 项目侧不留第二份）。
                    _pos = CameraMath.SmoothDamp(_pos, want, ref _camVelocity, FollowSmoothTime, dt);
                }

                ClampToMapBounds();
            }

            if (dt > 0f)
            {
                ReadZoomInput();
                ReadEdgeScrollInput(dt);
                if (_shakeT < _shakeDur) _shakeT += dt;
            }

            ApplyToCamera();
        }

        /// <inheritdoc />
        public void Reset()
        {
            _target = null;
            _hasFocus = false;
            _focus = Vector3.zero;
            _pos = Vector3.zero;
            _camVelocity = Vector3.zero;          // 速度状态一起复位（否则下一局第一次平滑会带着上一局的速度）
            _panOffset = Vector2.zero;
            _snapPending = false;
            _shakeAmp = _shakeDur = _shakeT = 0f;
            _ortho = DefaultOrthographicSize;
            _cam = null;
            _camProbeBlocked = false;
            _camEverResolved = false;
            _camWarned = false;
            _isoLockLogged = false;
            _applyFailedLogged = false;
            _applySkippedLogged = false;
            _zoomClampLogged = false;
            _noMapLogged = false;
            _ctxMissingLogged = false;
            _followPlayerLogged = false;
            _focusInvalidLogged = false;
            TargetGrid = Vector2Int.zero;
            Log.Info(Tag, $"相机复位（正交尺寸回到默认 {DefaultOrthographicSize}，跟随与震动已清）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 非契约入口（集成 / 自检用；`ICameraRig` 上没有，故不破坏契约）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>当前跟随位置（世界坐标，含边界钳制；不含震动偏移）。</summary>
        public Vector3 Position => _pos;

        /// <summary>本帧真正写到相机上的位置（含震动偏移）。</summary>
        public Vector3 FinalPosition => _pos + ShakeOffset();

        /// <summary>当前正交尺寸。</summary>
        public float OrthographicSize => _ortho;

        /// <summary>当前跟随目标格（配合 <see cref="SetTargetGrid"/>；未设过为 (0,0)）。</summary>
        public Vector2Int TargetGrid { get; private set; }

        /// <summary>是否已拿到可用相机（Play 下由 `Camera.main` 解析，或 <see cref="BindCamera"/> 注入）。</summary>
        public bool HasCamera => ResolveCamera() != null;

        /// <summary>是否正在震动。</summary>
        public bool IsShaking => _shakeT < _shakeDur;

        /// <summary>
        /// **本项目新增的非契约方法**：显式绑定相机（与 `MapModule.AttachRoot` 同一处置）。
        /// `Stage` 场景里的主相机应能被 `Camera.main` 找到；找不到时可由集成方调本方法补上。
        /// </summary>
        public void BindCamera(UnityCamera cam)
        {
            if (cam == null)
            {
                Log.Warn(Tag, "BindCamera(null) 被调用 ⇒ 忽略");
                return;
            }

            _cam = cam;
            _camEverResolved = true;
            _camWarned = false;
            _applySkippedLogged = false;
            _isoLockLogged = false;
            LockIsoParams();
            ApplyToCamera();
        }

        /// <summary>滚轮缩放入口（可选能力）。越界时只报一次 Warn。</summary>
        public void AddZoom(float delta)
        {
            var before = _ortho;
            var after = Zoomed(before, delta, MinOrthographicSize, MaxOrthographicSize);
            if (Mathf.Abs(after - before) < 1e-4f)
            {
                if (!_zoomClampLogged)
                {
                    _zoomClampLogged = true;
                    Log.Warn(Tag, $"缩放已到边界 [{MinOrthographicSize}, {MaxOrthographicSize}]（当前 {before:0.##}），" +
                                  $"本次 {delta:0.##} 被忽略（只报一次）");
                }
                return;
            }

            _ortho = after;
            Log.Info(Tag, $"缩放 {before:0.##} → {after:0.##}（只改正交取景范围；世界坐标与 `Iso` 映射不变，" +
                          $"故点击反投影仍成立）");
            if (_cam != null) _cam.orthographicSize = _ortho;
        }

        /// <summary>纯函数：正交尺寸的钳制结果。</summary>
        public static float Zoomed(float current, float delta, float min, float max)
        {
            return Mathf.Clamp(current + delta, min, max);
        }

        /// <summary>纯函数：期望机位（焦点正前方 `z = cameraZ`，不旋转）。</summary>
        public static Vector3 DesiredPosition(Vector3 focus, float cameraZ)
        {
            return new Vector3(focus.x, focus.y, cameraZ);
        }

        // ⛔ 本类原有 `public static Vector3 SmoothTowards(...)`（纯指数滞后，`k=1-exp(-dt/tau)`+Lerp）
        //   已**整条删除** —— 它下沉为引擎 `CloverEngine.CameraMath.Follow(Vector3,Vector3,float,float)`
        //   （逐字同公式，见该件注释）。跟随实际走的是临界阻尼
        //   `CloverEngine.CameraMath.SmoothDamp(...)`。项目侧**不再持有任何平滑实现**。

        /// <summary>
        /// 纯函数：把焦点夹进「地图包围盒 - 半屏」范围内（视野比地图大时居中）。
        /// 原版相机只在地图范围内滚动。
        /// <para>⚠️ **世界 AABB 口径（旧）**：`MapWorldBounds` 取的是等距菱形的轴对齐包围盒，
        /// 菱形与 AABB 之间的四个三角区**不是地图** ⇒ 夹这个盒子会放行"看得见地图外虚空"的机位
        /// （实测 80×80 落点 (-19,-11) 处屏幕 31.3% 是虚空；根因与修法见 `CameraBounds.cs` 文件头）。
        /// 生产**已改走格空间** `CameraBounds.ClampFocusGrid`；本函数只作为
        /// 「夹制的纯函数形状」保留（`tools/playercheck` §11 用它做边界钳制的对照组断言）。</para>
        /// </summary>
        public static Vector2 ClampFocus(Vector2 focus, Vector2 min, Vector2 max, float halfW, float halfH)
        {
            float x;
            if (max.x - min.x <= halfW * 2f) x = (min.x + max.x) * 0.5f;
            else x = Mathf.Clamp(focus.x, min.x + halfW, max.x - halfW);

            float y;
            if (max.y - min.y <= halfH * 2f) y = (min.y + max.y) * 0.5f;
            else y = Mathf.Clamp(focus.y, min.y + halfH, max.y - halfH);

            return new Vector2(x, y);
        }

        /// <summary>
        /// 纯函数：机位（世界）→ **可见格**包围盒（生产 `Iso.WorldToGridContinuous` 现算）。
        /// <para>用途：`tools/playercheck` §11.10「贴边不露虚空」用它**直接量**生产机位的越界格数
        /// （⛔ 不在宿主里再镜像一份几何 —— 镜像 = 改了生产也不变红的假闸门）。</para>
        /// <para>`Iso` 是线性变换 ⇒ 矩形映射后的极值必在四个角上 ⇒ 只看四角即可（不必逐像素采样）。</para>
        /// </summary>
        public static void VisibleGridRect(float camX, float camY, float halfW, float halfH,
            out float loX, out float hiX, out float loY, out float hiY)
        {
            loX = float.MaxValue; hiX = float.MinValue;
            loY = float.MaxValue; hiY = float.MinValue;
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var g = Iso.WorldToGridContinuous(new Vector3(
                        camX + (i == 0 ? -halfW : halfW),
                        camY + (j == 0 ? -halfH : halfH), 0f));
                    if (g.x < loX) loX = g.x;
                    if (g.x > hiX) hiX = g.x;
                    if (g.y < loY) loY = g.y;
                    if (g.y > hiY) hiY = g.y;
                }
            }
        }

        /// <summary>
        /// 纯函数：**一次算完**「焦点 → 机位」——地图包围盒 + 边界钳制 + 焦点正前方（z = cameraZ）。
        /// 这是 `ClampToMapBounds` 的生产实现路径（本类内部也直接调它）⇒ 离线下可以整条断言，
        /// 不必依赖 `Camera.aspect` 等原生调用（`tools/playercheck` §11「取景断言」用它）。
        /// </summary>
        /// <param name="focus">焦点世界坐标（z 分量被保留到返回值里）。</param>
        /// <param name="mapWidth">地图宽（格）。</param>
        /// <param name="mapHeight">地图高（格）。</param>
        /// <param name="orthoSize">正交尺寸（半高，世界单位）。</param>
        /// <param name="aspect">相机宽高比（`Camera.aspect`）。</param>
        /// <param name="cameraZ">机位 z（= `-CameraDistance`）。</param>
        public static Vector3 CameraPosForFocus(Vector3 focus, int mapWidth, int mapHeight,
            float orthoSize, float aspect, float cameraZ)
        {
            // ★ 格空间夹制（⛔ 不是世界 AABB）：把「可见格矩形」夹进 [0..W-1]×[0..H-1]。
            //   实现只在 `CameraBounds.ClampFocusGrid`（同一份 = 离线宿主 `mapcheck` §29 断言的那份）。
            // ⛔ 只走**一份**夹制实现（本片与 camera-clamp 片撞车过一次，两份叠加 = 夹两次 + 口径可能打架）。
            //   「主角可见优先」那条硬约束已在 `CameraBounds.ClampFocusGrid` 内部处理（见该文件头「一条硬约束」段）。
            var f = CameraBounds.ClampFocusGrid(new Vector2(focus.x, focus.y), mapWidth, mapHeight,
                orthoSize * aspect, orthoSize);
            var p = DesiredPosition(new Vector3(f.x, f.y, focus.z), cameraZ);
            p.z = cameraZ;                        // 焦点 z 不参与（世界是 z=0 的 XY 平面）
            return p;
        }

        /// <summary>
        /// 纯函数：地图世界包围盒（四角投影取 min/max；`Iso` 是线性变换，角点即极值点）。
        /// <para>⚠️ 这是**菱形的外接矩形**，对角线方向那四个三角区**不是地图** ⇒
        /// 拿它做夹制会放行"看得见虚空"的机位。生产夹制已改走 `CameraBounds.ClampFocusGrid`；
        /// 本函数保留给离线宿主的对照断言（AABB 口径 vs 格口径的差异本身就是缺陷的证据）。</para>
        /// </summary>
        public static void MapWorldBounds(int width, int height, out Vector2 min, out Vector2 max)
        {
            var a = Iso.GridToWorld(0, 0);
            var b = Iso.GridToWorld(width - 1, 0);
            var c = Iso.GridToWorld(0, height - 1);
            var d = Iso.GridToWorld(width - 1, height - 1);
            min = new Vector2(Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)),
                              Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)));
            max = new Vector2(Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x)),
                              Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y)));
        }

        /// <summary>「WorldToViewport 参数非法」是否已报过（只报一次，避免纯函数被高频调用时刷屏）。</summary>
        private static bool _viewportArgsWarned;

        /// <summary>
        /// 纯函数：世界坐标 → **视口归一化坐标**（0..1，左下原点）—— 正交、不旋转、沿 +Z 俯视的相机。
        /// 与 `Camera.WorldToViewportPoint` 同语义（本项目相机不旋转、世界是 z=0 的 XY 平面 ⇒ 只剩 xy 平移缩放）。
        /// <para>存在的理由：`Camera.WorldToScreenPoint` 是**原生调用**，离线自检宿主（`tools/*check`）里
        /// 用不了 ⇒ 「焦点世界坐标 → 屏幕中心」这条验收断言就永远没法离线自证。本函数把这段换算抽成
        /// 纯数学，`tools/playercheck` 可以直接断言，不必依赖 Unity 运行时。</para>
        /// <para>参数非法（`orthoSize &lt;= 0` / `aspect &lt;= 0`）⇒ 返回 `NaN` 并只报一次 Warn
        /// （返回 NaN 会让断言**明确失败**，比悄悄返回 (0.5,0.5) 假通过安全）。</para>
        /// </summary>
        /// <param name="world">世界坐标（z 分量被忽略）。</param>
        /// <param name="camPos">相机世界位置（正交相机 = 视口中心的世界坐标）。</param>
        /// <param name="orthoSize">正交尺寸（**半高**，世界单位）—— 与 `Camera.orthographicSize` 同口径。</param>
        /// <param name="aspect">相机宽高比（= `Camera.aspect` = `Screen.width / Screen.height`）。</param>
        public static Vector2 WorldToViewport(Vector3 world, Vector3 camPos, float orthoSize, float aspect)
        {
            if (orthoSize <= 0f || aspect <= 0f)
            {
                if (!_viewportArgsWarned)
                {
                    _viewportArgsWarned = true;
                    Log.Warn(Tag, $"WorldToViewport 参数非法（orthoSize={orthoSize}, aspect={aspect}）" +
                                  "⇒ 返回 NaN（只报一次）");
                }
                return new Vector2(float.NaN, float.NaN);
            }

            var halfW = orthoSize * aspect;              // 视口半宽（世界单位）
            return new Vector2(0.5f + (world.x - camPos.x) / (2f * halfW),
                               0.5f + (world.y - camPos.y) / (2f * orthoSize));
        }

        /// <summary>
        /// 纯函数：世界坐标 → **屏幕像素坐标**（左下原点）—— 与 `Camera.WorldToScreenPoint` 同语义。
        /// 由 <see cref="WorldToViewport"/> 乘以屏幕尺寸得到（验收断言「焦点 → 屏幕中心」用它）。
        /// </summary>
        public static Vector2 WorldToScreen(Vector3 world, Vector3 camPos, float orthoSize, float aspect,
            float screenW, float screenH)
        {
            var v = WorldToViewport(world, camPos, orthoSize, aspect);
            return new Vector2(v.x * screenW, v.y * screenH);
        }

        /// <summary>
        /// 纯函数：边缘滚动偏移（鼠标进入 `marginPx` 边距内时产生指向外部的位移，满偏 = `maxShift`）。
        /// 屏幕坐标系与 `Game.Input.MousePosition` 一致（左下角原点）。
        /// </summary>
        public static Vector2 EdgeScrollOffset(Vector2 pointer, float screenW, float screenH,
            float marginPx, float maxShift)
        {
            if (screenW <= 0f || screenH <= 0f || marginPx <= 0f || maxShift <= 0f) return Vector2.zero;

            var x = 0f;
            if (pointer.x <= marginPx) x = -(1f - Mathf.Clamp01(pointer.x / marginPx));
            else if (pointer.x >= screenW - marginPx) x = 1f - Mathf.Clamp01((screenW - pointer.x) / marginPx);

            var y = 0f;
            if (pointer.y <= marginPx) y = -(1f - Mathf.Clamp01(pointer.y / marginPx));
            else if (pointer.y >= screenH - marginPx) y = 1f - Mathf.Clamp01((screenH - pointer.y) / marginPx);

            return new Vector2(x, y) * maxShift;
        }

        /// <summary>纯函数：震动偏移的幅度（线性衰减到 0；`t &gt;= dur` 恒 0）。</summary>
        public static float ShakeMagnitude(float amplitude, float duration, float t)
        {
            if (amplitude <= 0f || duration <= 0f || t >= duration) return 0f;
            if (t <= 0f) return amplitude;
            return amplitude * (1f - t / duration);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// ★ agent-22 §B1 修复：**每帧刷新跟随焦点**。
        /// <para>
        /// **实测根因**（探针 `client/_dev/p_a22_diag.cs` 的原始数字，不是推断）：
        /// `AppFlow` 只在**进 Stage / 换区域**时调一次 `SetTargetGrid(出生格)` + `SnapToTarget()`，
        /// 而本类把那次给的 `_focus` 一直用到底 —— `Tick` 只做「`_focus` → 机位」的平滑，
        /// **从来不重取焦点**（`_target` 也只存不用）。于是：
        /// 玩家从出生格 (15,14) 走到 (1,4)，`Camera.main.position` 始终停在
        /// `(1.00, -15.00, -10.00)`，主角落到屏幕 `(600, 1620)`（屏幕高 1080 ⇒ 已在画面外）。
        /// </para>
        /// <para>两条来源，按优先级：① 契约 `Follow(transform)` 设过的目标（每帧读它的 position）；
        /// ② 没有显式目标时跟**主角当前世界坐标**（原版语义，`IPlayerModule.World`）。</para>
        /// </summary>
        private void RefreshFocus()
        {
            // ① 显式跟随某个 Transform（契约 Follow）
            if (_target != null)
            {
                try
                {
                    _focus = _target.position;
                    _hasFocus = true;
                    TargetGrid = Iso.WorldToGrid(_focus);
                    return;
                }
                catch (Exception e)
                {
                    Log.Warn(Tag, $"读跟随目标位置失败（{e.GetType().Name}: {e.Message}）⇒ 放弃该目标，改跟主角");
                    _target = null;
                }
            }

            // ② 跟主角：**只有站点 = Stage** 才跟。
            //    · 进过 Stage 才有人设过焦点（`_hasFocus`），菜单里焦点恒为假；
            //    · 再加站点判定是因为「有人在 Stage 之外显式设过目标格」（离线自检宿主 `tools/playercheck`
            //      直接 `rig.SetTargetGrid(far)`）时，不该被主角坐标劫持 —— 那里主角是静止的出生点，
            //      一劫持就永远收敛不到 `far`（实测残留 12.65）。站点判定让两种场景各归各位。
            if (!_hasFocus || !FlowReady()) return;

            var player = PlayerOrNull();
            if (player == null) return;

            var w = player.World;
            if (float.IsNaN(w.x) || float.IsNaN(w.y) || float.IsInfinity(w.x) || float.IsInfinity(w.y))
            {
                // 非预期分支：坐标非有限值 ⇒ 本帧不刷新（写进相机会让整屏消失且不报错）
                if (!_focusInvalidLogged)
                {
                    _focusInvalidLogged = true;
                    Log.Warn(Tag, $"主角世界坐标非有限值 {w} ⇒ 本帧不刷新焦点（只报一次）");
                }
                return;
            }

            _focus = new Vector3(w.x, w.y, 0f);       // 世界是 z = 0 的 XY 平面
            TargetGrid = player.Grid;
            if (!_followPlayerLogged)
            {
                _followPlayerLogged = true;
                Log.Info(Tag, $"开始跟随主角：世界 ({_focus.x:0.00},{_focus.y:0.00})，" +
                              $"格 ({TargetGrid.x},{TargetGrid.y})（之后每帧跟随其当前坐标）");
            }
        }

        /// <summary>
        /// 游戏流程编排（`IAppFlow`）是否就绪 —— 用它当「这一局真的在跑游戏流程（进过 Stage）」的判据。
        /// <para>
        /// 为什么不判 `Game.Fsm.Current == "Stage"`：离线自检宿主（`tools/playercheck`）编的是
        /// **引擎门面替身**（`shim/EngineShim.cs`，只列本项目用到的成员），里面**没有 `Game.Fsm`**
        /// ⇒ 一引用就编不过（实测 `CS0117: “Game”未包含“Fsm”的定义`）。
        /// </para>
        /// <para>
        /// 而 `AppContext.Flow` 在真机由 `Bootstrap.cs:123` 装配（`ctx.Flow = new AppFlow()`），
        /// 在宿主里**恒为 null**（`IAppFlow` 刻意不参与 `AutoWire`，见 `AppContext.cs:13`）⇒
        /// 正好是个干净判据：**宿主/菜单里不接管机位（谁 `SetTargetGrid` 谁说了算），真机进 Stage 才跟主角**。
        /// </para>
        /// </summary>
        private bool FlowReady()
        {
            var ctx = AppContext.I;
            return ctx != null && ctx.Flow != null;
        }

        /// <summary>取主角门面（组合根注入；未接入返回 null）。</summary>
        private IPlayerModule PlayerOrNull()
        {
            var ctx = AppContext.I;
            if (ctx == null) return null;
            return ctx.Player;
        }

        /// <summary>本帧的震动偏移（三角函数方向 × 线性衰减幅度 ⇒ 可复现、不引随机数）。</summary>
        private Vector3 ShakeOffset()
        {
            var m = ShakeMagnitude(_shakeAmp, _shakeDur, _shakeT);
            if (m <= 0f) return Vector3.zero;
            var phase = _shakeT * ShakeAngularSpeed;
            return new Vector3(Mathf.Sin(phase) * m, Mathf.Cos(phase * 1.7f) * m, 0f);
        }

        /// <summary>
        /// 取当前可用的主相机（**每帧校验缓存**；见文件头「缺陷修复」）。
        /// <para>为什么不能一锤子缓存：引擎换场用 `SceneManager.LoadSceneAsync(name)`（单场景，
        /// `Runtime/Presentation/Scene.cs:27`）⇒ Boot → Menu → Stage 每次都会把上一台主相机销毁。
        /// 缓存住那个「已销毁引用」会让本类**整局静默失效**（位置算得再对也写不进场景）。</para>
        /// <para>降级策略：原生相机 API 不可用（离线自检宿主）⇒ 只报一次并**永久**按无相机处理；
        /// 只是「暂时没有主相机」（场景刚切完）⇒ 下次调用会再试。</para>
        /// </summary>
        private UnityCamera ResolveCamera()
        {
            // ① 缓存仍有效（Unity 的 `==` 重载会把「已销毁对象」判成 null）⇒ 直接用
            if (_cam != null && IsUsable(_cam)) return _cam;

            // ② 本进程的原生相机 API 不可用（离线宿主）：不再重试
            if (_camProbeBlocked) return null;

            // ③ 缓存失效 / 还没有 ⇒ 重新解析
            UnityCamera found;
            try
            {
                found = UnityCamera.main;
            }
            catch (Exception e)
            {
                _camProbeBlocked = true;
                Log.Warn(Tag, $"取 Camera.main 抛异常（{e.GetType().Name}: {e.Message}）⇒ 本进程按无相机处理" +
                              "（不再重试；离线自检宿主走这一条）");
                return null;
            }

            if (found != null && IsUsable(found))
            {
                if (_camEverResolved)
                {
                    // 非预期分支（换场把旧主相机销毁了）：必须留下可定位日志，否则「相机整局没跟」无从查起
                    Log.Info(Tag, $"原相机引用已失效（场景已切换？）⇒ 重新解析到主相机「{found.name}」" +
                                  "（重新锁等距参数，并立即吸附到当前焦点）");
                }

                _cam = found;
                _camEverResolved = true;
                _camWarned = false;
                _applySkippedLogged = false;
                _isoLockLogged = false;          // 新相机要重新锁一次（正交 / size / 不旋转）
                LockIsoParams();
                if (_hasFocus) _snapPending = true;   // 相机换了 ⇒ 立刻到位，别从旧机位平滑飞过来
                return _cam;
            }

            if (!_camWarned)
            {
                _camWarned = true;
                Log.Warn(Tag, "找不到主相机（Camera.main 为 null）⇒ 机位只在内部状态里推进，" +
                              "不会写进场景（只报一次）。请确认 Stage 场景有带 MainCamera 标签且启用的相机，" +
                              "或由集成方调 CameraRig.BindCamera");
            }
            return null;
        }

        /// <summary>
        /// 相机是否可用（可渲染 + 在场）：`== null` 已覆盖「已销毁对象」（Unity 的运算符重载），
        /// `isActiveAndEnabled` 覆盖「被禁用 / 所属 GameObject 未激活」（这种相机不渲染，不能当跟随目标）。
        /// 原生 API 不可用时（离线宿主）判为不可用并**只报一次**。
        /// </summary>
        private bool IsUsable(UnityCamera c)
        {
            if (c == null) return false;
            try
            {
                return c.isActiveAndEnabled;
            }
            catch (Exception e)
            {
                if (!_camProbeBlocked)
                {
                    _camProbeBlocked = true;
                    Log.Warn(Tag, $"探测相机可用性抛异常（{e.GetType().Name}: {e.Message}）⇒ 本进程按无相机处理");
                }
                return false;
            }
        }

        /// <summary>把等距参数锁到相机上（正交 / 尺寸 / 不旋转）。只做一次。</summary>
        private void LockIsoParams()
        {
            if (_cam == null || _isoLockLogged) return;
            _isoLockLogged = true;
            try
            {
                _cam.orthographic = true;
                _cam.orthographicSize = _ortho;
                _cam.transform.rotation = Quaternion.identity;   // 俯仰/偏航/翻滚 = 0（不旋转）
                Log.Info(Tag,
                    $"已锁定等距相机参数：正交 orthographic=true、size={_ortho}、rotation=identity、" +
                    $"机位 z=-{CameraDistance}（到地面距离，`constraints.md` #6）；" +
                    $"瓦片 {GameConst.IsoTilePxW}x{GameConst.IsoTilePxH} @ PPU {GameConst.PixelsPerUnit}");
            }
            catch (Exception e)
            {
                Log.Warn(Tag, $"设置相机参数失败（{e.GetType().Name}: {e.Message}）⇒ 保持原样，仅做位置跟随");
            }
        }

        /// <summary>把 `FinalPosition`（+正交尺寸）写到相机上。</summary>
        private void ApplyToCamera()
        {
            if (_cam == null)
            {
                // ★ 这里**不许静默 return**：曾经就是这条静默路径让「相机整局停在出生机位」查不出来
                //   （位置算对了、场景里没动、日志里什么都没有）。非预期分支必须有可定位日志。
                if (!_applySkippedLogged)
                {
                    _applySkippedLogged = true;
                    Log.Warn(Tag, $"没有可用相机 ⇒ 本帧机位 ({_pos.x:0.00},{_pos.y:0.00}) 未写进场景，" +
                                  "画面会停在该场景相机的出生机位（只报一次）。请确认 Stage 场景里有 " +
                                  "tag=MainCamera 且启用的相机，或由集成方调 CameraRig.BindCamera");
                }
                return;
            }
            try
            {
                _cam.transform.position = FinalPosition;
                if (!Mathf.Approximately(_cam.orthographicSize, _ortho)) _cam.orthographicSize = _ortho;
            }
            catch (Exception e)
            {
                if (!_applyFailedLogged)
                {
                    _applyFailedLogged = true;
                    Log.Warn(Tag, $"写机位到相机失败（{e.GetType().Name}: {e.Message}）⇒ 本帧不跟随（只报一次）");
                }
            }
        }

        /// <summary>按地图包围盒钳制跟随位置（原版相机只在地图内滚动）。</summary>
        private void ClampToMapBounds()
        {
            var map = MapOrNull();
            if (map == null || !map.IsGenerated)
            {
                if (!_noMapLogged)
                {
                    _noMapLogged = true;
                    Log.Info(Tag, "地图未生成（或 IMapModule 未接入）⇒ 机位不做边界钳制（只报一次）");
                }
                return;
            }

            if (_cam == null) return;                 // 拿不到 aspect ⇒ 不做半屏换算

            // 走纯函数（与 `tools/playercheck` §11 的取景断言**同一条实现**，避免测试和线上两套算法）
            var clamped = CameraPosForFocus(_pos, map.Width, map.Height, _ortho, _cam.aspect, _pos.z);
            // 被地图边界真的夹住 ⇒ 清速度状态（反积分卷绕）：否则临界阻尼会继续朝"地图外的目标"积分，
            // 等目标回到图内时相机已经攒下一段速度、会冲一下。未被夹住时 clamped 与 _pos 相等（零开销）。
            if (Vector3.SqrMagnitude(clamped - _pos) > 0f) _camVelocity = Vector3.zero;
            _pos = clamped;
        }

        /// <summary>可选滚轮缩放（`EnableZoom` 打开时才读轴；仍走 `Game.Input`）。</summary>
        private void ReadZoomInput()
        {
            if (!EnableZoom) return;
            var input = Game.Input;
            if (input == null || !input.Available) return;

            var wheel = input.GetAxis(ScrollWheelAxis);
            if (Mathf.Abs(wheel) <= 0.0001f) return;
            AddZoom(wheel * ZoomStepPerNotch);
        }

        /// <summary>
        /// 可选边缘滚动（`EnableEdgeScroll` 打开时才读；偏移累加并夹在 `EdgeScrollMaxPan` 内）。
        /// 读的是 `Game.Input.MousePosition` + `Screen` 尺寸（两者都 try/catch 兜底）。
        /// </summary>
        private void ReadEdgeScrollInput(float dt)
        {
            if (!EnableEdgeScroll) return;
            var input = Game.Input;
            if (input == null || !input.Available) return;

            float w;
            float h;
            try
            {
                w = Screen.width;
                h = Screen.height;
            }
            catch (Exception e)
            {
                EnableEdgeScroll = false;
                Log.Warn(Tag, $"读取 Screen 尺寸失败（{e.GetType().Name}）⇒ 关闭边缘滚动（本特性可选，不影响跟随）");
                return;
            }

            var dir = EdgeScrollOffset(input.MousePosition, w, h, EdgeScrollMarginPx, 1f);
            if (dir == Vector2.zero) return;

            _panOffset += dir * (EdgeScrollSpeed * dt);
            _panOffset = new Vector2(
                Mathf.Clamp(_panOffset.x, -EdgeScrollMaxPan, EdgeScrollMaxPan),
                Mathf.Clamp(_panOffset.y, -EdgeScrollMaxPan, EdgeScrollMaxPan));
        }

        /// <summary>取地图（经组合根 `AppContext` 注入的接口；未接入时返回 null 并只报一次）。</summary>
        private IMapModule MapOrNull()
        {
            var ctx = AppContext.I;
            if (ctx == null)
            {
                if (!_ctxMissingLogged)
                {
                    _ctxMissingLogged = true;
                    Log.Warn(Tag, "AppContext.I 为 null（Bootstrap 未装配？）⇒ 相机拿不到地图，边界钳制降级（只报一次）");
                }
                return null;
            }
            return ctx.Map;
        }
    }
}
