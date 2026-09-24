// ─────────────────────────────────────────────────────────────────────────────
// d2u27_jitter.cs  U27（人物抖动「一顿一顿」三分判据）· Play 侧采集器
//
//   一次 Play 采齐六场景的**逐帧三层位置序列** + 截图（文件名带格号）：
//     S1 静止      —— 站着不动（对照：三层都应当**零**步长）
//     S2 直线走    —— 沿一条轴方向直线走 ≥8 格（等距投影：屏幕速度 = 1.118×格速）
//     S3 斜向走    —— 沿一条对角方向走 ≥5 格（屏幕速度 = 0.7071× / 1.4142×格速）
//     S4 过格点    —— 由 S2/S3 的逐帧**落格帧**自动标出（`p.Grid` 变化的那一帧）
//     S5 连续点地  —— 每 4 帧改一次目标（连续点地/追鼠标）⇒ 频繁换向
//     S6 过夹制边界 —— 从出生点走到城镇南带（`gy ≥ 33`）⇒ 相机的边界夹制**咬合/脱开**
//                      （离线判不了：`CameraRig.ClampToMapBounds` 在 `_cam == null` 时直接
//                       return（`CameraRig.cs:913`），只有真机有 `Camera.main` 才走夹制）
//
//   三层（各自取生产件的唯一出口，不镜像公式）：
//     ② 渲染 = 玩家视图节点的真实 `transform.position`（`IViewModule.GetView(PlayerEntityId)`）
//     ③ 逻辑 = `IPlayerModule.World`
//     ① 相机 = `Camera.main.transform.position`
//   列：scen \t frame \t dt \t lx \t ly \t rx \t ry \t cx \t cy \t grid \t spr
//   ⇒ 判据（离线跑）：① 渲染−逻辑 逐帧必须 = 0（层② 不插值）；② 相机−逻辑 的相对偏移
//     逐帧变化量 ≤ 玩家单帧位移×1.1；③ S6 段相机单帧步长出现"冻结→追赶"阶跃即判红。
//     同一个量法脚本 = `d2u27_judge.py`（与本文件同一目录）。
//
//      未经验证的点已在文件尾 `UNVERIFIED` 注释里逐条列清（回报里也写了）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace U27
{
    /// <summary>与 `CamJit.Probe` 同一套骨架（日志走 `[U27]` 前缀，采集脚本从 Editor.log 里捞）。</summary>
    public static class Probe
    {
        private const string Tag = "U27";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            if (Application.isPlaying) UnityEngine.Debug.Log("[" + Tag + "] " + msg);
            else Console.WriteLine("[" + Tag + "] " + msg);
        }

        internal static void Warn(string msg) { UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg); }
        internal static void KV(string key, string value) { Log(key + "=" + value); }

        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void WriteFile(string path, string content)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
            }
            catch (Exception ex) { Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- 组合根：**必须走反射** ----------------------------------------------
        // 离线编译哨兵实测（`tools/probes/drivers/d2u27_compilecheck`）：
        //    `Diablo2.App.AppContext` 是 **internal** ⇒ 注入的脚本里写 `AppContext.I` 直接
        //    `CS0122: “AppContext”不可访问`（本文件第一版就是这么写错的，被哨兵当场抓住）。
        //    已证的写法 = `camjitter_drive.cs` 那条反射链（`Ctx()` → 字段 → 成员），本文件照抄口径。
        internal static object Ctx()
        {
            try
            {
                var t = Type.GetType("Diablo2.App.AppContext, Diablo2");
                if (t == null)
                {
                    var asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (var i = 0; i < asms.Length && t == null; i++) t = asms[i].GetType("Diablo2.App.AppContext");
                }
                if (t == null) { Warn("CTX-PROBE 找不到 Diablo2.App.AppContext（程序集未加载？）"); return null; }
                var f = t.GetField("I", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null) return f.GetValue(null);
                var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return p != null ? p.GetValue(null) : null;
            }
            catch (Exception ex) { Warn("CTX-PROBE " + ex.GetType().Name + ": " + ex.Message); return null; }
        }

        internal static object CtxMember(string name)
        {
            var ctx = Ctx();
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) : null;
        }

        internal static Diablo2.Module.IPlayerModule Player()
            => CtxMember("Player") as Diablo2.Module.IPlayerModule;

        internal static Diablo2.Module.IMapModule Map()
            => CtxMember("Map") as Diablo2.Module.IMapModule;

        /// <summary>
        /// 玩家**渲染节点**（= `EntityView.Root` 的 GameObject）：`ViewModule` 是 internal、
        /// `_player` 是私有字段 ⇒ 只能走反射（口径同 `camjitter_drive.cs` 的 `PlayerNode()`）。
        /// </summary>
        internal static GameObject ViewNode()
        {
            try
            {
                var view = CtxMember("View");
                //    判据 R1 于是"零样本空过"（假绿）。**非预期分支必须留痕**，且每条只报一次。
                if (view == null) { WarnOnce("view.ctx", "VIEW-PROBE AppContext.View == null ⇒ 渲染位置列 = NaN（判据 R1 将因零样本无效）"); return null; }
                var f = view.GetType().GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance);
                var ev = f != null ? f.GetValue(view) : null;
                if (ev == null) { WarnOnce("view.player", "VIEW-PROBE ViewModule._player 取不到（视图还没建 / 字段改名？）⇒ 渲染位置列 = NaN"); return null; }
                var rf = ev.GetType().GetField("Root", BindingFlags.Public | BindingFlags.Instance)
                      ?? ev.GetType().GetField("Root", BindingFlags.NonPublic | BindingFlags.Instance);
                if (rf == null) { WarnOnce("view.root", "VIEW-PROBE EntityView 没有 Root 字段（改名？）⇒ 渲染位置列 = NaN"); return null; }
                return rf.GetValue(ev) as GameObject;
            }
            catch (Exception ex) { WarnOnce("view.ex", "VIEW-PROBE " + ex.GetType().Name + ": " + ex.Message); return null; }
        }

        private static readonly System.Collections.Generic.HashSet<string> _warnedOnce = new System.Collections.Generic.HashSet<string>();

        /// <summary>只报一次（同一 key 重复调用不再刷屏）—— 非预期分支留痕用。</summary>
        internal static void WarnOnce(string key, string msg)
        {
            if (_warnedOnce.Contains(key)) return;
            _warnedOnce.Add(key);
            Warn(msg + "（本条只报一次）");
        }

        internal static Vector2Int GridOf(Diablo2.Module.IPlayerModule p)
            => p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string DeviceName() { return SystemInfo.graphicsDeviceName; }

        /// <summary>
        /// 运行时切帧节奏（A/B 实验用）：直接写 `Application.targetFrameRate` / `QualitySettings.vSyncCount`
        /// 并**读回校验**（切不动就如实报，不假装成功）。`fps &lt; 0` = 交给平台（vSync 接管）；
        /// `fps == 0` = 用显示器刷新率（读不到则退回 60）。
        /// </summary>
        internal static string SetCadence(string tag, int fps, int vSync)
        {
            var wantFps = fps;
            if (fps == 0)
            {
                var hz = (float)Screen.currentResolution.refreshRateRatio.value;
                wantFps = hz > 1f ? (int)System.Math.Round(hz) : 60;
            }
            Application.targetFrameRate = wantFps;
            QualitySettings.vSyncCount = vSync;
            var readFps = Application.targetFrameRate;
            var readVSync = QualitySettings.vSyncCount;
            var ok = readFps == wantFps && readVSync == vSync;
            var line = "CADENCE tag=" + tag + " wantFps=" + wantFps + " wantVSync=" + vSync
                     + " readFps=" + readFps + " readVSync=" + readVSync + " ok=" + (ok ? 1 : 0)
                     + " refresh=" + Screen.currentResolution.refreshRateRatio.value.ToString("0.##") + "Hz";
            Log(line);
            if (!ok) Warn("CADENCE-NOT-APPLIED " + line);
            return line;
        }

        /// <summary>软件光栅（WARP/Basic Render Driver）⇒ 任何帧节奏/渲染结论都无效（同 CamJit 口径）。</summary>
        internal static bool SoftwareRaster(string device)
        {
            if (string.IsNullOrEmpty(device)) return true;
            var d = device.ToLowerInvariant();
            return d.Contains("basic render") || d.Contains("warp") || d.Contains("software");
        }

        internal static Diablo2.Module.IPlayerModule PlayerOrNull() { return Player(); }

        /// <summary>场景节点是否在场（Boot 面板等；`GameObject.Find` 只找**激活**的节点，够用）。</summary>
        internal static GameObject GameObjectNamed(string name)
        {
            try { return GameObject.Find(name); }
            catch (Exception ex) { Warn("FIND " + name + " " + ex.GetType().Name); return null; }
        }

        /// <summary>存活的第一个存档名（反射读 `AppFlow._roster.ListAll()`，与 CamJit 同一路径）。</summary>
        internal static string FirstSaveName()
        {
            try
            {
                // 同样必须走反射（`AppContext` 是 internal；见本文件 `Ctx()` 的注释）
                var flow = CtxMember("Flow");
                if (flow == null) { Warn("ROSTER-PROBE AppContext.Flow == null"); return null; }
                var rf = flow.GetType().GetField("_roster", BindingFlags.NonPublic | BindingFlags.Instance);
                var roster = rf != null ? rf.GetValue(flow) : null;
                if (roster == null) { Warn("ROSTER-PROBE no AppFlow._roster"); return null; }
                var m = roster.GetType().GetMethod("ListAll", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) { Warn("ROSTER-PROBE no CharRoster.ListAll"); return null; }
                var items = m.Invoke(roster, null) as IEnumerable;
                string pick = null;
                var n = 0;
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        n++;
                        if (it == null) continue;
                        var f = it.GetType().GetField("Name", BindingFlags.Public | BindingFlags.Instance);
                        var name = f != null ? f.GetValue(it) as string : null;
                        if (string.IsNullOrEmpty(name))
                        {
                            var pf = it.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
                            name = pf != null ? pf.GetValue(it, null) as string : null;
                        }
                        if (!string.IsNullOrEmpty(name)) { pick = name; break; }
                    }
                }
                Log("ROSTER-PROBE count=" + n + " pick=" + (pick ?? "(none)"));
                return pick;
            }
            catch (Exception ex) { Warn("ROSTER-PROBE " + ex.GetType().Name + ": " + ex.Message); return null; }
        }

        // ---- 真实输入注入（口径抄 `camjitter_drive.cs` 的**已证**写法）----------------
        // 第一版写的 `kb.spaceKey.QueueStateChange(...)` + `LowLevel.KeyEventState` 两个都不存在
        // （离线哨兵报 CS1061 / CS0234）⇒ 改成 CamJit 那条：`InputSystem.QueueStateEvent(kb, KeyboardState)`。
        internal static void KeysDown(string[] names)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { Warn("KEYSDOWN no-keyboard"); return; }
            var keys = new List<UnityEngine.InputSystem.Key>();
            for (var i = 0; i < names.Length; i++)
                if (names[i] == "space") keys.Add(UnityEngine.InputSystem.Key.Space);
                else Warn("KEYSDOWN unknown-key=" + names[i]);
            if (keys.Count == 0) return;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb,
                new UnityEngine.InputSystem.LowLevel.KeyboardState(keys.ToArray()));
            Log("KEYSDOWN keys=" + string.Join("+", names) + " keyboard=" + kb.name);
        }

        /// <summary>松开全部键（空 `KeyboardState` = 全部释放，同 CamJit 的 `KeyUp()`）。</summary>
        internal static void KeyUpSpace()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb,
                new UnityEngine.InputSystem.LowLevel.KeyboardState());
        }

        /// <summary>把指针放到某个 UI 按钮上并点一下（Raycast 找命中的 Button，再 ExecuteEvents 点击）。</summary>
        internal static string ClickButtonByLabel(string label)
        {
            var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (canvas == null) { Warn("CLICK no Canvas"); return "ERR-no-canvas"; }
            var buttons = canvas.GetComponentsInChildren<Button>(true);
            foreach (var b in buttons)
            {
                if (b == null) continue;
                var txt = b.GetComponentInChildren<Text>(true);
                var has = txt != null ? txt.text : null;
                if (string.IsNullOrEmpty(has)) continue;
                if (has.IndexOf(label, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var rt = b.transform as RectTransform;
                if (rt == null) continue;
                var cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var center = RectTransformUtility.WorldToScreenPoint(cam, rt.position);
                var ped = new PointerEventData(EventSystem.current) { position = center };
                ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
                Log("CLICK-LABEL label=\"" + label + "\" hit=\"" + has + "\" at=" + center);
                return "OK";
            }
            var names = new StringBuilder();
            foreach (var b in buttons) if (b != null) names.Append(b.name).Append(' ');
            Warn("CLICK-LABEL label=\"" + label + "\" NOT-FOUND buttons=[" + names + "]");
            return "ERR-not-found";
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        //   为什么必须加：`ClickButtonByLabel` 走 `ExecuteEvents.pointerClickHandler`（合成事件），
        //   实机实测 u27v4 = `CLICK-LABEL label="Single" NOT-FOUND buttons=[]`（主菜单那一刻
        //   Canvas 下枚举到 **0 个 Button**）⇒ 引导链断在第一步、后续 60s 超时、TSV 未生成。
        //   ⇒ 按主 agent 给的配方**照抄** `tools/probes/drivers/d2u3_charstat_drive.cs:174-268`
        //     （`ScreenRect` / `FindButton` / `MouseDev` / `MouseState`），不自创。
        //   `ClickButtonByLabel` **保留**，降级为**诊断**（找不到时仍打印它枚举到的按钮名）。
        // ══════════════════════════════════════════════════════════════════════════════════════
        internal static bool ScreenRect(RectTransform rt, out Vector2 lo, out Vector2 hi, out Vector2 center)
        {
            lo = Vector2.zero; hi = Vector2.zero; center = Vector2.zero;
            if (rt == null) return false;
            try
            {
                var a = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMin, 0f)));
                var b = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.yMax, 0f)));
                var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                lo = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
                hi = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                center = new Vector2(c.x, c.y);
                return true;
            }
            catch (Exception e) { Warn("SCREEN-RECT-FAIL " + e.GetType().Name + ": " + e.Message); return false; }
        }

        /// <summary>节点全路径（本文件原名 `PathOf` 不存在 ⇒ 用独立名避免与既有成员重名）。</summary>
        internal static string NodePathOf(Transform t)
        {
            var sb = new StringBuilder();
            var cur = t;
            while (cur != null) { if (sb.Length > 0) sb.Insert(0, '/'); sb.Insert(0, cur.name); cur = cur.parent; }
            return sb.ToString();
        }

        /// <summary>按 `name`（可选 `路径子串|name`）找**活动**且**最具体**的 Button。</summary>
        internal static Button FindButton(string arg, out string diag)
        {
            diag = string.Empty;
            var pathSub = string.Empty;
            var want = arg ?? string.Empty;
            var bar = want.IndexOf('|');
            if (bar >= 0) { pathSub = want.Substring(0, bar); want = want.Substring(bar + 1); }
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            Button pick = null;
            var n = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (pathSub.Length > 0 && NodePathOf(b.transform).IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                if (pick == null || NodePathOf(b.transform).Length > NodePathOf(pick.transform).Length) pick = b;
            }
            diag = "buttons=" + all.Length + " candidates=" + n;
            return pick;
        }

        internal static UnityEngine.InputSystem.Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }

        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = pos };
            if (leftDown) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(m, st);
        }

        /// <summary>指针落点处的最上层射线命中（诊断用：证明"按钮真的在指针底下"）。</summary>
        internal static string RaycastTop(Vector2 pos)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current);
            ped.position = pos;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            var g = hits[0].gameObject;
            return g == null ? "(null)" : (g.name + "@" + NodePathOf(g.transform));
        }
    }

    /// <summary>CLI 入口（`unity command run_script --file d2u27_jitter.cs --entry U27.Api.<M>`）。</summary>
    public static class Api
    {
        /// <summary>环境自证：设备/刷新率/帧节奏/时间步。软件光栅时驱动会拒跑。</summary>
        public static string Cfg()
        {
            var r = Screen.currentResolution;
            return "CFG ok device=\"" + Probe.DeviceName() + "\" type=" + SystemInfo.graphicsDeviceType
                 + " res=" + Screen.width + "x" + Screen.height
                 + " refresh=" + r.refreshRateRatio.value.ToString("0.##") + "Hz"
                 + " targetFps=" + Application.targetFrameRate
                 + " vSync=" + QualitySettings.vSyncCount
                 + " deltaTime=" + Time.deltaTime.ToString("0.#####")
                 + " gameRunning=" + (CloverEngine.Game.IsRunning ? 1 : 0)
                 + " unity=" + Application.unityVersion;
        }

        public static string Paths(string spec) { Probe.Paths(spec); return "PATHS-OK"; }
        public static string Ping() { return "PONG gameRunning=" + (CloverEngine.Game.IsRunning ? 1 : 0); }

        /// <summary>
        /// 帧节奏 A/B（team-lead 指定的实验设计，**一次 Play 会话内做完**）：
        ///   A = 现行（`vSyncCount = 0` + `targetFrameRate = 60`，= `Core/FramePacing` 的兜底口径）
        ///   B = 运行时切换（`vSyncCount = 1` + `targetFrameRate = -1`，= 引擎 `Recommend()` 在刷新率可读时的档位）
        ///   C = `targetFrameRate = 显示器刷新率` + `vSyncCount = 0`（有余量再采）
        /// 驱动会在**同一会话**里用同一段场景跑 A、再跑 B（记录列 `cad` 区分）；本组入口是给采集脚本
        /// 做"进 Play 后先确认能切"的探针 + 给人工复现用。
        /// </summary>
        public static string CfgA() { return Probe.SetCadence("A", 60, 0); }

        public static string CfgB() { return Probe.SetCadence("B", -1, 1); }

        public static string CfgC() { return Probe.SetCadence("C", 0, 0); }
    }

    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("U27JitterDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>
    /// 一次 Play：Boot → MainMenu → CharSelect → Stage（与 CamJit 同一套已证配方），
    /// 然后依次跑六个场景并**连续录制**每一帧的三层位置；截图落盘、文件名带格号。
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private const float IdleSettle = 1.2f;
        // u27v7：2600 → 3400；u27v8：3400 → 5200。**为什么**（有读数，不是拍脑袋）：
        //   `u27v6`（缺 clampband 的 A 档）`frames=953`；`u27v7` 的 **A 档六场景收齐 = 1534 帧**
        //   （`SCEN-DONE=档位 A 六场景收齐`，且 A 组 n=1534 与此吻合）⇒ **两档 ≈ 2×1534 ≈ 3070**，
        //   再加建计划/停稳/换档零头 ⇒ 3400 仍会在 **B 档的 clickspam/clampband** 上撞上限
        //   （u27v7 实测：B 只收齐 line+diag）。
        // **u27v8 → 9000 的关键读数**：B 档 dtMean **5.54ms** vs A 档 **17.33ms** ⇒ **同一条行走
        //   （同样距离）在 B 档要花 ~3.1× 的帧数** ⇒ "两档 ≈ 2×A" 的估法是错的（实测 5200 帧时
        //   B 已 3666 帧、clampband 还没走完）。⇒ A(1534) + B(≈1534×3.1≈4750) + 零头 ≈ 6500，
        //   给 9000 留 ~38% 余量。**墙钟不吃亏**：5200 帧那批实测 ~2min（B 每帧更短）⇒ 9000 帧
        //   ≈ 26s(A) + 26s(B) + 零头 ≈ 1.5min，远小于 runner 的 **420s** DONE 等待。
        private const int FrameCap = 9000;

        private string _tag = "u27";
        private string _save = "g66";
        private string _tsv = string.Empty;
        private string _donePath = string.Empty;
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _trigAt;
        private bool _done;
        private bool _bootSpaceSent, _bootSpaceUp;
        private float _bootSpaceAt;
        private string _lastPanel = "(null)";
        private bool _sawLoading;

        // ---- 场景机 ----
        private enum Scen { Idle = 1, Line, Diag, ClickSpam, ClampBand }
        private Scen _scen = Scen.Idle;
        private Vector2Int _start;
        private Vector2Int _lineTarget, _diagTarget, _bandTarget;
        private int _scenFrames;
        private int _clickTick;
        private int _shotTick;
        /// <summary>A/B：跑第几遍（0 = 档位 A，1 = 档位 B）；同一段场景两遍，记录列 `cad` 区分。</summary>
        private int _pass;
        private string _cad = "A";

        // u27v10：A/B 档位映射**集中一处** + 顺序可对调（见 case 4 的注释）。
        //   `BFirst = true` ⇒ 同会话内**先 B 后 A**（解 `u27v9` 的 order-confound；对调后同向才算可行动结论）。
        //   档位定义只此一处：A = `targetFrameRate=60 + vSyncCount=0`；B = `targetFrameRate=-1 + vSyncCount=1`。
        private const bool BFirst = true;

        /// <summary><paramref name="pass"/> 0/1 ⇒ 档位标签（受 <see cref="BFirst"/> 控顺序）。</summary>
        private static string TagOf(int pass) { return (pass == 0) == BFirst ? "B" : "A"; }

        /// <summary>套用档位（内含**读回校验** ⇒ trace 里出 `CADENCE tag=… readFps=… readVSync=… ok=1`）。</summary>
        private static void ApplyCadence(string tag)
        {
            if (tag == "B") Probe.SetCadence("B", -1, 1);   // B = vSyncCount=1 + targetFrameRate=-1（不封顶）
            else Probe.SetCadence("A", 60, 0);              // A = vSyncCount=0 + targetFrameRate=60
        }
        /// <summary>建计划时那份地图的尺寸（每帧比对 ⇒ 录制途中换区域立刻 fail-fast，不记假数据）。</summary>
        private int _planW;
        private int _planH;

        // ---- 录制 ----
        private bool _recording;
        private readonly List<string> _rows = new List<string>();
        private readonly List<string> _shots = new List<string>();
        private int _recFrames;
        private int _recErrors;
        private Vector2Int _lastGrid;
        private float _dtSum;
        private float _dtMax;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            if (parts.Length > 2) _tsv = parts[2];
            if (parts.Length > 3) _donePath = parts[3];
            if (parts.Length > 4) _shotDir = parts[4];
            Probe.Paths(_donePath);
            if (_shotDir.Length > 0 && !Directory.Exists(_shotDir)) Directory.CreateDirectory(_shotDir);
            _step = 0;
            _at = Time.unscaledTime;
            Probe.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" tsv=" + _tsv + " shots=" + _shotDir);
        }

        private void Update()
        {
            if (_done) return;
            try { Step(); }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        /// <summary>采样本帧的三层位置：必须在游戏 tick 链**之后**（相机在 Update 里被写）。</summary>
        private void LateUpdate()
        {
            if (!_recording || _done) return;
            try { Record(); }
            catch (Exception ex)
            {
                if (_recErrors++ == 0) Probe.Log("RECORD-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        // ================================================================ 录制 ==============
        private void Record()
        {
            var p = Probe.Player();
            if (p == null) return;
            var logic = p.World;
            var node = Probe.ViewNode();
            var render = node != null ? node.transform.position : new Vector3(float.NaN, float.NaN, 0f);
            var cam = Camera.main;
            var cc = cam != null ? cam.transform.position : new Vector3(float.NaN, float.NaN, 0f);

            var sp = node != null ? node.GetComponentInChildren<SpriteRenderer>() : null;
            var spr = sp != null && sp.sprite != null ? sp.sprite.name : "(none)";

            var grid = Probe.GridOf(p);
            // S4「过格点」：`p.Grid` 变化的那一帧由该列自动标出（不用额外场景开关）
            var crossed = grid != _lastGrid ? "1" : "0";
            _lastGrid = grid;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(160);
            sb.Append(ScenName()).Append('\t')
              .Append(Time.frameCount.ToString(ci)).Append('\t')
              .Append(Time.deltaTime.ToString("0.######", ci)).Append('\t')
              .Append(logic.x.ToString("0.######", ci)).Append('\t')
              .Append(logic.y.ToString("0.######", ci)).Append('\t')
              .Append(render.x.ToString("0.######", ci)).Append('\t')
              .Append(render.y.ToString("0.######", ci)).Append('\t')
              .Append(cc.x.ToString("0.######", ci)).Append('\t')
              .Append(cc.y.ToString("0.######", ci)).Append('\t')
              .Append(Probe.Grid(grid)).Append('\t')
              .Append(crossed).Append('\t')
              .Append(spr).Append('\t')
              // A/B：`cad` 列**放最后**（不动前面任何列的索引 ⇒ 既有判据脚本零改动就能读旧列，
              //    只多一列可分组）。同一段场景先跑 A 再跑 B，两组都在这一个 TSV 里。
              .Append(_cad);
            _rows.Add(sb.ToString());
            _recFrames++;
            _dtSum += Time.deltaTime;
            if (Time.deltaTime > _dtMax) _dtMax = Time.deltaTime;

            // 截图：每个场景 1 张（文件名带格号 ⇒ 与读数一一对应）
            if (_shotDir.Length > 0 && _scenFrames == ShotAtFrame && _shotTick != (int)_scen)
            {
                _shotTick = (int)_scen;
                var file = _shotDir + "/" + _tag + "_s" + (int)_scen + "_" + ScenName()
                         + "_f" + Time.frameCount + "_" + grid.x + "_" + grid.y + ".png";
                UnityEngine.ScreenCapture.CaptureScreenshot(file);
                _shots.Add("s" + (int)_scen + "\t" + ScenName() + "\t" + Time.frameCount + "\t"
                           + Probe.Grid(grid) + "\t" + file);
                Probe.KV("SHOT", "scen=" + ScenName() + " frame=" + Time.frameCount + " grid=" + Probe.Grid(grid) + " file=" + file);
            }
        }

        private const int ShotAtFrame = 8;

        private string ScenName()
        {
            switch (_scen)
            {
                case Scen.Idle: return "idle";
                case Scen.Line: return "line";
                case Scen.Diag: return "diag";
                case Scen.ClickSpam: return "clickspam";
                case Scen.ClampBand: return "clampband";
                default: return "?";
            }
        }

        private void StopRecording(string why)
        {
            if (!_recording) return;
            _recording = false;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            // `cad` 列放最后（A/B 分组用）；前面列的索引与语义一字不变。
            sb.Append("scen\tframe\tdt\tlx\tly\trx\try\tcx\tcy\tgrid\tcrossed\tspr\tcad\n");
            for (var i = 0; i < _rows.Count; i++) sb.Append(_rows[i]).Append('\n');
            if (_tsv.Length > 0) Probe.WriteFile(_tsv, sb.ToString());
            Probe.KV("REC-STOP", "why=" + why + " frames=" + _recFrames
                + " dtMean=" + (_recFrames > 0 ? _dtSum / _recFrames : 0f).ToString("0.######", ci)
                + " dtMax=" + _dtMax.ToString("0.######", ci)
                + " shots=" + _shots.Count + " recordErrors=" + _recErrors);
            if (_shotDir.Length > 0)
            {
                var man = new StringBuilder();
                man.Append("scen\tname\tframe\tgrid\tfile\n");
                for (var i = 0; i < _shots.Count; i++) man.Append(_shots[i]).Append('\n');
                Probe.WriteFile(_shotDir + "/" + _tag + "-shots.tsv", man.ToString());
            }
        }

        private void Finish(string why)
        {
            if (_done) return;
            StopRecording(why);
            _done = true;
            Probe.WriteFile(_donePath, "DONE why=" + why + " frames=" + _recFrames + " \n".Trim());
            Probe.KV("FINISH", "why=" + why + " frames=" + _recFrames);
        }

        // ================================================================ 流程 ==============
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return CloverEngine.Game.Fsm != null ? CloverEngine.Game.Fsm.Current : "(null)"; }
        private static bool HudOpen() { return CloverEngine.Game.UI != null && CloverEngine.Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }

        private bool Tout(string at, float s)
        {
            if (!Elapsed(s)) return false;
            Probe.KV("STEP-TIMEOUT", "at=" + at + " fsm=" + Fsm() + " panel=" + HudOpen());
            Next();
            return true;
        }

        private void Step()
        {
            switch (_step)
            {
                case 0:
                    {
                        var device = Probe.DeviceName();
                        Probe.Log("ENV device=\"" + device + "\" res=" + Screen.width + "x" + Screen.height
                            + " refresh=" + Screen.currentResolution.refreshRateRatio.value.ToString("0.##")
                            + " targetFps=" + Application.targetFrameRate + " vSync=" + QualitySettings.vSyncCount);
                        if (Probe.SoftwareRaster(device))
                        {
                            Probe.Warn("DEVICE-SOFTWARE-RASTER device=\"" + device + "\" => 帧节奏/渲染类结论全部无效，链路不跑");
                            Finish("software-raster");
                            return;
                        }
                        Probe.Log("PROBE-BEGIN frame=" + Time.frameCount + " fsm=" + Fsm());
                        Next();
                        return;
                    }

                // Boot 面板：真键盘 Space（与 CamJit 同一配方）
                case 1:
                    if (!_bootSpaceSent)
                    {
                        if (Probe.GameObjectNamed("BootPanel") == null && !Elapsed(30f)) return;
                        if (!Elapsed(1.2f)) return;
                        Probe.KeysDown(new[] { "space" });
                        _bootSpaceSent = true;
                        _bootSpaceAt = Time.unscaledTime;
                        _trigAt = _bootSpaceAt;
                        return;
                    }
                    if (!_bootSpaceUp && Time.unscaledTime - _bootSpaceAt >= 0.25f) { Probe.KeyUpSpace(); _bootSpaceUp = true; }
                    if (Time.unscaledTime - _bootSpaceAt > 12f) { Probe.Warn("BOOT 仍然开着，继续往下走"); Next(); }
                    else if (Elapsed(1.5f)) Next();
                    return;

                case 2:
                    if (!Elapsed(1.0f)) return;
                    // u27v5：**真鼠标**点 "Single"。u27v4 用合成事件时实测
                    //   `CLICK-LABEL label="Single" NOT-FOUND buttons=[]`（那一刻 Canvas 下 0 个 Button）
                    //   ⇒ 引导断在这里、后面 60s 超时、TSV 未生成。本步每帧都进 ⇒ 用 TickClick 推进。
                    if (_clickPhase != 0) { TickClick(); return; }
                    if (_mainClickTries == 0)
                    {
                        _mainClickTries = 1;
                        if (RealClick("Single", "mainmenu") == 0)
                        {
                            // 降级：合成事件（**保留** = 诊断，同时证明它为什么不够）+ 打印它枚举到的按钮名
                            Probe.ClickButtonByLabel("Single");
                            Probe.KV("MAIN-CLICK", "result=fallback-synthetic");
                        }
                        else Probe.KV("MAIN-CLICK", "result=real-mouse");
                        return;
                    }
                    Next();
                    return;

                case 3:
                    if (!Elapsed(2.0f)) return;
                    var pick = Probe.FirstSaveName();
                    if (!string.IsNullOrEmpty(pick)) _save = pick;
                    // u27v5-diagnose（主 agent 定的"先诊断、后改目标"）：把 CharSelect 屏上**所有**
                    //   `Selectable`（Button/Toggle/Slider/Dropdown/Scrollbar/InputField 的基类）的
                    //   类型 / 名字 / 完整路径 / interactable / activeInHierarchy 全枚举落 trace，
                    //   外加总数、类型直方图、当前选中项。
                    //   这一步**不改任何点击目标**：先把"roster 的 75 行到底是不是可点控件、是什么类型"
                    //   变成读数，再据此改一次（避免"改对了/改错了"混在同一次会话里）。
                    if (!_selectablesDumped)
                    {
                        _selectablesDumped = true;
                        var sels = UnityEngine.Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None);
                        var byType = new StringBuilder();
                        var shown = 0;
                        for (var i = 0; i < sels.Length; i++)
                        {
                            var s = sels[i];
                            if (s == null || s.gameObject == null) continue;
                            var tn = s.GetType().Name;
                            byType.Append(tn).Append(' ');
                            if (shown < 60)
                            {
                                Probe.KV("CS-SEL", shown + "|" + tn + "|" + s.gameObject.name + "|"
                                    + Probe.NodePathOf(s.transform)
                                    + "|interactable=" + (s.interactable ? 1 : 0)
                                    + "|active=" + (s.gameObject.activeInHierarchy ? 1 : 0)
                                    + "|raycast=" + (s.targetGraphic != null && s.targetGraphic.raycastTarget ? 1 : 0));
                                shown++;
                            }
                        }
                        Probe.KV("CS-SEL-COUNT", "total=" + sels.Length + " shown=" + shown + " fsm=" + Fsm() + " hud=" + HudOpen());
                        Probe.KV("CS-SEL-TYPES", byType.ToString());
                        var es = UnityEngine.EventSystems.EventSystem.current;
                        Probe.KV("CS-SELECTED", "current=" + (es == null || es.currentSelectedGameObject == null
                            ? "(none)" : (es.currentSelectedGameObject.name + "@" + Probe.NodePathOf(es.currentSelectedGameObject.transform))));
                        Probe.KV("CS-SCROLLRECT", "count=" + UnityEngine.Object.FindObjectsByType<UnityEngine.UI.ScrollRect>(FindObjectsSortMode.None).Length);
                    }
                    // u27v5：roster 行**真点击**（按钮名 = 存档名）；点不到才回退到发事件。
                    if (_clickPhase != 0) { TickClick(); return; }
                    if (_rosterClickTries == 0)
                    {
                        _rosterClickTries = 1;
                        // u27v5d 的教训：`fsm=MainMenu` **不足以**判断"在哪个屏"（那次实际只有
                        //    `Single`/`Quit` 两个 Button ⇒ 真的还在主菜单）。⇒ 先打**面板级真相**。
                        Probe.KV("CS-PANEL", "charSelect=" + (CloverEngine.Game.UI != null && CloverEngine.Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>() ? 1 : 0)
                            + " charCreate=" + (CloverEngine.Game.UI != null && CloverEngine.Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>() ? 1 : 0)
                            + " mainMenu=" + (CloverEngine.Game.UI != null && CloverEngine.Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>() ? 1 : 0)
                            + " fsm=" + Fsm() + " hud=" + (HudOpen() ? 1 : 0));
                        // roster 行的命名口径抄 X 巡回（`x_drive.cs:2619` = `"Row"+idx+"|Delete"`；
                        // 进档 = `"<row>|Enter"`，`x_drive.cs:5421`）⇒ 这里**找第一个存在的 `Row<i>`**，
                        // 而不是猜"按钮名 = 存档名"（u27v5 两次都因此 miss）。
                        // u27v5e 读数纠正了我的假设：**行名在"路径"里，不在按钮名上** ——
                        //   实测 23 个 Button 全是 `…/List/Row<i>/{Enter|Delete|RowHotspot}`（按钮名 =
                        //   末段），所以我上一版按 `^Row\d+$` 匹配**按钮名**必然 0 命中（`CS-ROWS []=`）。
                        //   ⇒ 改成**按路径**找：路径含 `Row<digits>` 且按钮名 == `Enter` 的，取它的行名当 key。
                        var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
                        var rows = new StringBuilder();
                        string rowKey = null;
                        for (var i = 0; i < all.Length; i++)
                        {
                            var bt = all[i];
                            if (bt == null || bt.gameObject == null) continue;
                            if (!string.Equals(bt.gameObject.name, "Enter", StringComparison.OrdinalIgnoreCase)) continue;
                            var path = Probe.NodePathOf(bt.transform);
                            var mRow = System.Text.RegularExpressions.Regex.Match(path, @"Row(\d+)");
                            if (!mRow.Success) continue;
                            rows.Append("Row").Append(mRow.Groups[1].Value).Append(' ');
                            if (rowKey == null) rowKey = "Row" + mRow.Groups[1].Value;
                        }
                        Probe.KV("CS-ROWS", "enterButtons=[" + rows + "] picked=" + (rowKey ?? "(none)")
                            + " buttons=" + all.Length);
                        var targets = new List<string>();
                        if (rowKey != null) { targets.Add(rowKey + "|Enter"); targets.Add(rowKey + "|RowHotspot"); }
                        if (!string.IsNullOrEmpty(_save)) { targets.Add(_save + "|Enter"); targets.Add(_save); }
                        foreach (var tg in targets)
                        {
                            if (RealClick(tg, "roster") == 1)
                            {
                                Probe.KV("ROSTER-CLICK", "result=real-mouse target=\"" + tg + "\"");
                                return;
                            }
                        }
                        Probe.KV("ROSTER-CLICK", "result=fallback-event save=\"" + _save + "\" roster=" + (pick ?? "(none)"));
                        _trigAt = Time.unscaledTime;
                        CloverEngine.Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                        return;
                    }
                    Probe.KV("ENTER-STAGE", "save=\"" + _save + "\" roster=" + (pick ?? "(none)"));
                    Next();
                    return;

                case 4:
                    // u27v5（`jitter` 的配方）：60s 还进不了 Stage ⇒ **fail-fast**，别 `Next()` 让
                    //   后面 6 个场景在"没 stage"的状态下空跑 240 帧（u27v4 就是这么变成 0 样本的）。
                    if (!(HudOpen() && Fsm() == "Stage"))
                    {
                        if (Elapsed(60f))
                        {
                            Probe.KV("STEP-TIMEOUT", "at=stage-wait fsm=" + Fsm() + " panel=" + HudOpen());
                            Finish("blocked-no-stage");
                        }
                        return;
                    }
                    if (!Elapsed(IdleSettle)) return;
                    // A/B：每遍开场把档位写死一遍（不依赖采集脚本先调过；幂等 + 读回校验）
                    // u27v10（主 agent 派：解 `order-confounded`）：档位映射**集中一处** + 顺序可对调。
                    //   缘由（`jitter` 的反证）：`u27v9` 里 A 先跑、B 后跑（帧数 1545 vs 4045）⇒ 存在
                    //   **暖机/顺序混淆**（实测 A 的 155ms 卡峰落在前 1/3、组内 sd 单调收敛 0.0091→0.0038；
                    //   B 无趋势）⇒ 不换顺序就**不能**拿它改生产帧节奏。
                    //   档位定义（与 driver 原文一一对应，见 `SetCadence(tag,fps,vSync)` :150）：
                    //     A = `targetFrameRate=60`  + `vSyncCount=0`
                    //     B = `targetFrameRate=-1`  + `vSyncCount=1`（不封顶；本环境 vSync 未真正节流）
                    //   本批 `BFirst = true` ⇒ **同会话内先 B 后 A**；其余（场景集/帧上限/采样窗口/读回校验）一字不动。
                    _cad = TagOf(_pass);
                    ApplyCadence(_cad);
                    ResolvePlan();
                    // u27v5 退化计划闸门：三目标都 == 出生点 ⇒ 一步不动地空跑（u27v4 实测
                    //   `FINISH why=frame-cap frames=0`、TSV 未生成）⇒ 立刻 bad-plan，不许空跑 240 帧。
                    if (_lineTarget == _start && _diagTarget == _start && _bandTarget == _start)
                    {
                        Probe.Warn("PLAN-DEGENERATE 三目标都等于出生点 " + Probe.Grid(_start) + "（地图无可走目标？）⇒ bad-plan");
                        Probe.KV("PLAN-GATE", "result=bad-plan start=" + Probe.Grid(_start));
                        Finish("bad-plan");
                        return;
                    }
                    _recording = true;
                    _lastGrid = new Vector2Int(int.MinValue, int.MinValue);
                    Probe.KV("PLAN", "start=" + Probe.Grid(_start) + " line=" + Probe.Grid(_lineTarget)
                        + " diag=" + Probe.Grid(_diagTarget) + " band=" + Probe.Grid(_bandTarget)
                        + " corridorSkips=" + _corridorSkips);
                    // u27v6 取证：改了"走廊也查 Exit"就必须能证明它**真的排掉了东西**
                    //   （否则又是一条"断言绿、行为缺"）。`_corridorSkips == 0` ⇒ 本图本来就没有
                    //   穿过 Exit 的走廊候选（读数如此，不是没接线）；> 0 ⇒ 逐条见 `PLAN-CORRIDOR-SKIP`。
                    Probe.KV("PLAN-CORRIDOR", "skipped=" + _corridorSkips
                        + " line=" + Probe.Grid(_lineTarget) + " diag=" + Probe.Grid(_diagTarget)
                        + " band=" + Probe.Grid(_bandTarget));
                    Next();
                    return;

                // 场景 1：静止（40 帧）
                case 5:
                    if (_scenFrames++ < 40) return;
                    StartScen(Scen.Line, _lineTarget);
                    Next();
                    return;

                // 场景 2/3/5/6：走到目标 ⇒ 停稳 ⇒ 收下一场景
                case 6:
                    {
                        var p = Probe.Player();
                        if (p == null) { Tout("no-player", 10f); return; }

                        // 实测抓到的两个 bug（第一次 Play 的教训，逐条修）：
                        //   ① 场景切换那两行原本带 `Next()` ⇒ 把 `_step` 从 6 推到 7（= 帧数上限收尾）
                        //      ⇒ 驱动只跑完 2 个场景就 `REC-STOP why=frame-cap`（实测 559 帧）。
                        //   ② 实测驱动器里 `p.Grid` 一度到 (9,52) —— 地图高 40 的城镇不可能有 y=52 ⇒
                        //      **玩家在录制途中穿过了关卡出口，换了区域**，原计划（城镇格）随之失效。
                        var mm = Probe.Map();
                        if (mm != null && (_planW != mm.Width || _planH != mm.Height))
                        {
                            Probe.Warn("AREA-CHANGED plan=" + _planW + "x" + _planH + " now=" + mm.Width + "x" + mm.Height
                                + " grid=" + Probe.Grid(Probe.GridOf(p)) + " ⇒ 本批作废（计划跨了区域）");
                            Finish("area-changed");
                            return;
                        }
                        if (_recFrames >= FrameCap) { Finish("frame-cap"); return; }

                        if (_scen == Scen.ClickSpam)
                        {
                            // S5：每 4 帧改一次目标（连续点地）——目标在起点附近来回
                            // u27v7 修（实测 `u27v6` 就是死在这里）：这行原为
                            //    `{ StartScen(Scen.ClampBand, _bandTarget); **Next()**; return; }`
                            //    ⇒ `Next()` 把 `_step` 从 6 推到 **7**，而 `case 7` 就是 `Finish("frame-cap")`
                            //    ⇒ 批子在 **clampband 只有 1 帧样本**时就结束（`REC-STOP why=frame-cap frames=953`,
                            //    `scenarios: clampband=1`），S6 与 A/B 第二档全丢。**与下面 L856 注释警告的是同一类坑**
                            if (_scenFrames++ > 240) { StartScen(Scen.ClampBand, _bandTarget); return; }
                            if (_clickTick++ % 4 == 0)
                            {
                                var t = (_scenFrames / 24) % 2 == 0 ? _lineTarget : _diagTarget;
                                p.MoveTo(t);
                            }
                            return;
                        }

                        if (p.IsMoving) { _scenFrames = 0; return; }
                        if (_scenFrames++ < 12) return;          // 停稳 12 帧（相机收敛后再换场景）
                        switch (_scen)
                        {
                            // 这里**不许** `Next()`：那会把 `_step` 推到 7（收尾）⇒ 只跑 2 个场景就结束
                            //    （第一次 Play 实测：559 帧 + `why=frame-cap`）。场景机全程留在 case 6。
                            case Scen.Line: StartScen(Scen.Diag, _diagTarget); return;
                            case Scen.Diag: StartScen(Scen.ClickSpam, _lineTarget); return;
                            default:
                                // A/B（team-lead 指定）：**同一会话内**先跑完档位 A 的六场景，再切档位 B
                                //   重跑**同一段场景**（同一条场景计划 = 同一段输入），记录列 `cad` 区分。
                                //   切换是"运行时切"（写 `Application.targetFrameRate` / `QualitySettings.vSyncCount`
                                //   并读回校验）⇒ 两次采样在**同一台机器、同一会话、无并发**条件下完成。
                                if (_pass == 0)
                                {
                                    var doneTag = _cad;
                                    _pass = 1;
                                    _cad = TagOf(_pass);
                                    Probe.KV("SCEN-DONE", "档位 " + doneTag + " 六场景收齐 ⇒ 切档位 " + _cad + " 重跑同一段"
                                        + " order=" + (BFirst ? "B-then-A" : "A-then-B"));
                                    ApplyCadence(_cad);                    // 读回校验在 `SetCadence` 内（`CADENCE` 行）
                                    // u27v8 修（实测 `u27v7` 的 R1 就死在这一帧）：换档要**回出生点**，
                                    //   而"传送"是**驱动自己的状态复位、不是被测行为** ⇒ `TeleportTo` 那一帧
                                    //   逻辑瞬移 24 格、渲染要下一帧才跟上 ⇒ 判官 R1（render vs logic）
                                    //   报 `max|diff|=24.083189` FAIL。实测定位：`R1` 越界帧**恰好 1 帧**，
                                    //   索引 1534 = **A 组帧数（n=1534）** = A→B 边界帧；同帧 `R5 world(1,-1)
                                    //   max step=24.08319` 是同一个瞬移。⇒ 传送**移出采样窗口**（停录 →
                                    //   传送 → case 4 里重新开录），判据语义不变、只是不采"换档那一下"。
                                    _recording = false;
                                    var p2 = Probe.Player();
                                    if (p2 != null) p2.TeleportTo(Probe.Map() != null ? Probe.Map().SpawnPoint
                                        : new Vector2Int(0, 0));
                                    //   换档**必须复位场景机**。旧行为只复位了录制/位置/`_step`，
                                    //   `_scen` / `_scenFrames` 带着上一遍的值进第 2 遍 ⇒ 第 2 遍的**开场静止块**
                                    //   （`case 5` 的 `if (_scenFrames++ < 40) return;`）出两个错：
                                    //     ① 行数不足 —— 实测 A 遍 41 行 / B 遍只有 **27** 行
                                    //        （`u27v9` 第 1546~1573 行 = frame 4063~4090）；
                                    //     ② **场景标签错** —— 那 27 行的 `scen` 列写的是**上一遍的收尾场景**
                                    //        （实测 `scen=clampband` 而 `spr=idle_w_*`）⇒ 凡按 `scen` 分组的读数
                                    //        （`ab_trend` 的 P1 用 `scen=line`、`d2u27_stutter_repro` 的 M3 按 (scen,spr)）
                                    //        都会把开场块算进**错误的组**。
                                    //   为什么现在要紧：`u27v10` 是 `BFirst = true` ⇒ 第 2 遍 = **A**
                                    //      ⇒ 这次错到 A 头上；而 A 的 155.077ms 卡峰**就落在开场块里**
                                    //      ⇒ 会直接污染 R-SPIKE / R-REPRO 的"对调后是否同一点重现"。
                                    //   `Scen.Idle` = 字段初始值（第 1 遍之所以正确就是靠它，`:452`）。
                                    _scen = Scen.Idle;
                                    _scenFrames = 0;
                                    _step = 4;                              // 回到"建计划 + 开始录制"那一步
                                    _at = Time.unscaledTime;
                                    return;
                                }
                                Probe.KV("SCEN-DONE", "档位 " + (BFirst ? "B/A" : "A/B") + " 六场景都收齐，结束"
                                    + " order=" + (BFirst ? "B-then-A" : "A-then-B"));
                                Finish("all-scenarios-done");
                                return;
                        }
                    }

                case 7:
                    Finish("frame-cap");
                    return;

                default:
                    if (_recFrames >= FrameCap) { Next(); return; }
                    Tout("tail", 20f);
                    return;
            }
        }

        private void StartScen(Scen s, Vector2Int target)
        {
            var p = Probe.Player();
            _scen = s;
            _scenFrames = 0;
            _shotTick = 0;
            Probe.KV("SCEN", "-> " + ScenName() + " target=" + Probe.Grid(target)
                + " from=" + Probe.Grid(Probe.GridOf(p)) + " frame=" + Time.frameCount);
            if (p != null && target.x > int.MinValue) p.MoveTo(target);
        }

        // ══════════════════════════════════════════════════════════════════════════════════════
        // u27v5：真鼠标点击（2 帧"按下/抬起"），抄 `d2u3_charstat_drive.cs:402-441`（不自创）。
        //   用法：`RealClick(name, tag)` **arm**（1 = 已找到按钮并已把指针移上去），
        //   之后**每帧调 `TickClick()`** 直到 `_clickPhase == 0`（一轮点击做完）。
        // ══════════════════════════════════════════════════════════════════════════════════════
        private Vector2 _clickPos;
        private string _clickName = string.Empty;
        private int _clickPhase;
        private int _clickFrames;
        private int _mainClickTries;
        private int _rosterClickTries;
        private bool _selectablesDumped;   // u27v5-diagnose：CharSelect 的 Selectable 清单只打一次
        private int _corridorSkips;        // u27v6：因"行走走廊含 Exit"而被弃选的目标候选数（取证用）

        private int RealClick(string name, string tag)
        {
            string diag;
            var b = Probe.FindButton(name, out diag);
            if (b == null)
            {
                Probe.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " => 降级到 ClickButtonByLabel 诊断");
                return 0;
            }
            Vector2 lo, hi, c;
            Probe.ScreenRect(b.transform as RectTransform, out lo, out hi, out c);
            //    命中按钮，但 **MainMenu 没往前走**（`CS-SEL` 只有 `Single`/`Quit`）⇒ 说明**注入坐标不在按钮上**。
            //    X 巡回（已实测能一路进 stage）用的是 `Drive.Inject(c)`，不是 `Screen.height - c.y`
            //    （我抄的是 charstat 的写法，它那一路从没被"点击真的生效"验证过）。
            //    ⇒ 这里改成**自证式选点**：两个候选都先 `RaycastTop` 试一下，**谁命中目标按钮就用谁**，
            //      并把注入点与"注入点上的射线命中"一起打进 trace（下次一眼能看出坐标口径对不对）。
            var cNoFlip = new Vector2(c.x, c.y);
            var cFlip = new Vector2(c.x, Screen.height - c.y);
            var hitNoFlip = Probe.RaycastTop(cNoFlip);
            var hitFlip = Probe.RaycastTop(cFlip);
            var hitNo = hitNoFlip.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
            var hitFl = hitFlip.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0;
            _clickPos = hitNo ? cNoFlip : (hitFl ? cFlip : cNoFlip);
            _clickName = name;
            _clickPhase = 1;
            _clickFrames = 2;
            Probe.MouseState(_clickPos, false);
            Probe.KV("CLICKTOP", "tag=" + tag + " name=" + name + " screen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                + " interactable=" + (b.interactable ? 1 : 0)
                + " raycastTop=\"" + Probe.RaycastTop(c) + "\" " + diag);
            Probe.KV("CLICK-INJECT", "name=" + name + " canvasY=" + c.y.ToString("0")
                + " inject=" + _clickPos.x.ToString("0") + "," + _clickPos.y.ToString("0")
                + " noFlipHit=" + (hitNo ? 1 : 0) + " flipHit=" + (hitFl ? 1 : 0)
                + " raycastAtInject=\"" + Probe.RaycastTop(_clickPos) + "\"");
            if (!hitNo && !hitFl)
                Probe.Warn("CLICK-POS-BAD name=" + name + " 两个候选坐标的射线都没命中目标（noFlip=" + hitNoFlip + " flip=" + hitFlip + "）");
            return 1;
        }

        private void TickClick()
        {
            if (_clickPhase == 0) return;
            _clickFrames--;
            if (_clickFrames > 0) return;
            if (_clickPhase == 1)
            {
                Probe.MouseState(_clickPos, true);
                Probe.KV("UI-DOWN", "node=" + _clickName + " pos=" + _clickPos.x.ToString("0") + "," + _clickPos.y.ToString("0")
                    + " frame=" + Time.frameCount);
                _clickPhase = 2; _clickFrames = 2;
                return;
            }
            Probe.MouseState(_clickPos, false);
            Probe.KV("UI-UP", "node=" + _clickName + " frame=" + Time.frameCount);
            _clickPhase = 0; _clickFrames = 0;
        }

        /// <summary>
        /// 场景目标解析（全部落在**真地图的可走格**上）：
        ///   直线 = 出生点所在行/列上最远的可走格；斜向 = 到最远的对角格；南带 = 全图 `gy` 最大的可走格。
        /// </summary>
        private void ResolvePlan()
        {
            _start = Probe.GridOf(Probe.Player());
            var m = Probe.Map();
            _lineTarget = _start;
            _diagTarget = _start;
            _bandTarget = _start;
            if (m == null) { Probe.Warn("PLAN no map"); return; }
            _planW = m.Width;
            _planH = m.Height;
            _corridorSkips = 0;

            // u27v6（`jitter` 点名要的这一处）：**行走走廊也查 Exit** —— u27v5f 的 `line` 场景就是
            //   在**末尾一步**踩进 `Exit` 格 ⇒ `AREA-CHANGED` 早停、六场景没跑完。
            //   实测定位：最后一条采样 `frame=2610 grid=(18,28)`，紧接着 `AREA-CHANGED … grid=(17,28)`
            //   = **相邻格** ⇒ 确实只差最后一步。只排除"目标自身是 Exit"（下面第 997 行那句）不够。
            //   口径：直/对角候选查**实际行走线**（沿行/列或沿对角逐格）；其它（南带那种）保守地查
            //   "行走廊 ∪ 列走廊"。命中 ⇒ **弃选**（外层 `cheb > best*` 会自动退到更近的合法目标）；
            //   三个目标都退到出生点 ⇒ 已有的 `PLAN-DEGENERATE` 闸门 ⇒ `Finish("bad-plan")`（fail-fast 通道）。
            bool CorridorHasExit(Vector2Int g0, int tdx, int tdy)
            {
                if (tdx == 0 || tdy == 0 || Mathf.Abs(tdx) == Mathf.Abs(tdy))
                {
                    var sx = System.Math.Sign(tdx);
                    var sy = System.Math.Sign(tdy);
                    var cx = _start.x;
                    var cy = _start.y;
                    while (cx != g0.x || cy != g0.y)
                    {
                        cx += sx;
                        cy += sy;
                        if (m.TileAt(new Vector2Int(cx, cy)).ToString() == "Exit") return true;
                    }
                    return false;
                }
                var rx = System.Math.Sign(tdx);
                var ry = System.Math.Sign(tdy);
                for (var cx = _start.x + rx; cx != g0.x; cx += rx)
                    if (m.TileAt(new Vector2Int(cx, _start.y)).ToString() == "Exit") return true;
                for (var cy = _start.y + ry; cy != g0.y; cy += ry)
                    if (m.TileAt(new Vector2Int(g0.x, cy)).ToString() == "Exit") return true;
                return false;
            }

            var bestLine = 0;
            var bestDiag = 0;
            var bestBandY = int.MinValue;
            for (var y = 0; y < m.Height; y++)
            {
                for (var x = 0; x < m.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (!m.Walkable(g)) continue;
                    // 出口格不当目标：踩上去会**换区域**（实测第一次 Play 就这么作废了一批）。
                    //    用 `ToString()` 比名字而不是写 `TileKind.Exit`：枚举的命名空间在本驱动里
                    //    拿不准（宿主里是 `TileKind`，注入脚本里不一定 using 得到）⇒ 不在驱动里编造类型路径。
                    if (m.TileAt(g).ToString() == "Exit") continue;
                    var dx = x - _start.x;
                    var dy = y - _start.y;
                    var cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

                    // u27v6：走廊里含 Exit ⇒ 弃选（承重改动，见上面 `CorridorHasExit` 的注释）。
                    if (CorridorHasExit(g, dx, dy))
                    {
                        if (_corridorSkips < 8)
                            Probe.KV("PLAN-CORRIDOR-SKIP", "cand=" + Probe.Grid(g) + " cheb=" + cheb + " 走廊含 Exit");
                        _corridorSkips++;
                        continue;
                    }

                    if ((dx == 0 || dy == 0) && cheb > bestLine) { bestLine = cheb; _lineTarget = g; }
                    if (dx != 0 && Mathf.Abs(dx) == Mathf.Abs(dy) && cheb > bestDiag) { bestDiag = cheb; _diagTarget = g; }
                    // 南带（相机夹制的解析边界：gy > H − (a+b)/2，见 CameraBounds 口径）
                    if (y > bestBandY) { bestBandY = y; _bandTarget = g; }
                }
            }
            Probe.KV("PLAN-RESOLVE", "lineCheb=" + bestLine + " diagCheb=" + bestDiag + " bandY=" + bestBandY);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 编译期状态：**已过关**（0 error）—— 一键复检：
//     dotnet build tools/probes/drivers/d2u27_compilecheck
//   这个哨兵引的是 Unity **真实**程序集 + `client/Library/ScriptAssemblies/*`，当场抓出过 3 处真错：
//     ① `Diablo2.App.AppContext` 是 internal ⇒ `AppContext.I` CS0122（改成 `Ctx()` 反射链）
//     ② `KeyControl.QueueStateChange` / `LowLevel.KeyEventState` 不存在
//        （改成 `InputSystem.QueueStateEvent(kb, new KeyboardState(keys))`）
//     ③ `UnityEngine.ScreenCapture` 不在 CoreModule（单引 ScreenCaptureModule）
//   ⇒ 上一版列的 4 处 UNVERIFIED 全部**编译期**解决（不再是"带进 Play 才知道"）。
//
// 仍需 Play 才能确认的（**行为**，编译管不了）：
//   1. `Probe.ClickButtonByLabel`（新写的：Canvas → Button 的 Text 文本 → RectTransformUtility →
//      ExecuteEvents.pointerClickHandler）。若菜单按钮文本不是 "Single"，日志会打
//      `CLICK-LABEL ... NOT-FOUND buttons=[...]` ⇒ 照日志里的按钮名改字面量即可（这是设计好的降级）。
//   2. `ScreenCapture.CaptureScreenshot(path)` 是**异步**落盘 ⇒ 采集脚本在 `editor_stop` 前多等 2 秒
//      （`d2u27_run.ps1` 已加 `Start-Sleep -Seconds 2`）。
//   3. `Ctx()` 反射链取到的成员是否为 null（AppContext 未装配 / 字段改名）⇒ 驱动会打
//      `CTX-PROBE` / `VIEW-PROBE` 并让对应列变 NaN，**不崩**（降级已设计好）。
// ─────────────────────────────────────────────────────────────────────────────
