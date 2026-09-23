// t0i_drive.cs -- ONE Play session for slice t0-play-repave-recapture.
//
// WHY ONE CHAIN (SKILL 2 / unity-cli "a batch action, not a per-item loop"):
//   (a) the T0FIX-H rebuild split (RebuildLayers -> StartRebuild -> per-frame PumpRebuild ->
//       one-frame SwapToBuilt -> PumpRetire) exists only at runtime: the per-frame pump cost,
//       the swap frame, and the 7 self-witness counters can only be read from a live MapView.
//   (b) the area-change VISUAL (does any frame show a blank / half map, how many frames the old
//       map stays up) is a per-frame runtime observation.
//   (c) defect 2: the CharCreate Confirm button is DISABLED until a class is picked, so its
//       hover/press feedback must be measured AFTER a real class selection.
//   (d) frame pacing / enter-stage load / footprint need a live session + the render device name.
//
// WHAT IT MEASURES / WRITES (all lines tagged [T0I] -> client/Logs/Editor.log)
//   DEVICE            render device + resolution + vSync + targetFrameRate
//   PACING-STEADY     n=180 frame times + FrameTimingManager cpuMainThreadFrameTime p50/p95/max
//   LOAD              EnterStage wall -> Stage-ready wall (seconds)
//   MEM               Profiler.GetTotalAllocatedMemoryLong / MonoUsedSize
//   CONFIRM           defect 2: after selecting class, Confirm selectable/interactable + sprite
//                     state (sprN/sprH/sprP + wantH/wantP) + tiles g{n,h,p}.png (pixel diff offline)
//   PUMP-BEGIN/PUMP   the T0FIX-H rebuild measured DIRECTLY: MapView.enabled=false during the
//                     window, so the driver calls the private PumpRebuild() itself once per frame
//                     under a Stopwatch (exactly one pump == exactly one production frame).
//                     PUMP-SUMMARY carries per-frame us p50/p99/max, nodes/frame peak,
//                     us-per-GO, the swap frame us, and the 7 self-witness counters.
//   AREACHANGE-*      per-frame readings across a real BloodMoor area change (blank / half map
//                     detection + how many frames the old map stays up + ms).
//   REPAVE / REPAVE-SUMMARY  the engine-side rebuild markers (fsm / uiLoading / lpOpen / poolDelta)
//
// Tiles land under <repo>/.ai-tmp/screenshots/ (flat, prefix t0i_) -- outside the Unity project
// => never imported => the session survives.
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace T0I
{
    public static class Drive
    {
        internal const string Tag = "T0I";

        internal static string RawDir = string.Empty;
        internal static string DonePath = string.Empty;
        internal static bool Flip;

        internal static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else UnityEngine.Debug.Log("[" + Tag + "] " + m);
        }
        internal static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + m);
        }
        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static string Esc(string s)
        {
            if (s == null) return "(null)";
            return s.Replace('"', '\'').Replace("\r", " ").Replace("\n", "\\n").Replace("\t", " ");
        }
        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(path, content);
            }
            catch (Exception e) { Log("MARKER-FAIL " + e.GetType().Name + " " + e.Message); }
        }
        internal static string Paths(string arg)
        {
            var p = (arg ?? string.Empty).Split('|');
            if (p.Length > 0) RawDir = p[0];
            if (p.Length > 1) DonePath = p[1];
            Log("PATHS rawDir=" + RawDir + " done=" + DonePath);
            return "PATHS-OK";
        }

        // ---- reflection -----------------------------------------------------------
        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(name); if (t != null) return t; }
            return null;
        }
        internal static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            return p != null ? p.GetValue(null) : null;
        }
        internal static object CtxMember(string name)
        {
            var c = Ctx();
            if (c == null) return null;
            var f = c.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(c) : null;
        }
        internal static Diablo2.Module.IMapModule Map() { return CtxMember("Map") as Diablo2.Module.IMapModule; }
        internal static Diablo2.Module.IPlayerModule Player() { return CtxMember("Player") as Diablo2.Module.IPlayerModule; }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }
        internal static int IntProp(object o, string name)
        {
            if (o == null) return -9999;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -9999;
            var v = p.GetValue(o);
            return v is int ? (int)v : -9999;
        }
        internal static bool BoolProp(object o, string name)
        {
            if (o == null) return false;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return false;
            var v = p.GetValue(o);
            return v is bool && (bool)v;
        }
        internal static int IntField(object o, string n)
        {
            var v = Field(o, n);
            if (v is int) return (int)v;
            return -9999;
        }
        internal static int StaticInt(string typeName, string field)
        {
            var t = FindType(typeName);
            if (t == null) return -1;
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return -1;
            var v = f.GetValue(null);
            return v is int ? (int)v : -1;
        }
        internal static int NodeCount(Transform root)
        {
            if (root == null) return -1;
            return root.GetComponentsInChildren<Transform>(true).Length;
        }
        internal static int ActiveChildCount(Transform root)
        {
            if (root == null) return -1;
            var n = 0;
            for (var i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c != null && c.gameObject.activeInHierarchy) n++;
            }
            return n;
        }
        internal static Transform FindByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var r = FindByName(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)"; var vram = -1;
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            try { vram = SystemInfo.graphicsMemorySize; } catch { }
            return "device=\"" + dev + "\" type=" + typ + " vramMB=" + vram
                + " res=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate
                + " qualityLevel=" + QualitySettings.GetQualityLevel();
        }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        internal static string Panels()
        {
            if (Game.UI == null) return "(no-ui)";
            var sb = new StringBuilder();
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.BootPanel>(), "Boot");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(), "MainMenu");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(), "CharSelect");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(), "CharCreate");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(), "Loading");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.HudPanel>(), "Hud");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.PausePanel>(), "Pause");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(), "Settings");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.DeathPanel>(), "Death");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.D2ConfirmPanel>(), "Confirm");
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }
        private static void AddIf(StringBuilder sb, bool on, string name)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }
        internal static bool UiLoading() { return Game.UI != null && Game.UI.IsLoading; }
        internal static bool LpOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }
        internal static bool LoadingVisible() { return UiLoading() || Fsm() != "Stage"; }

        internal static object MapViewObj()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? (object)arr[0] : null;
        }
        internal static GameObject MapViewGo()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? arr[0].gameObject : null;
        }

        // ---- screen rect (bottom-left origin, Unity convention) --------------------
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
            catch (Exception e)
            {
                Warn("SCREEN-RECT-FAIL " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }
        internal static Vector2 Inject(Vector2 screen)
        {
            return Flip ? new Vector2(screen.x, Screen.height - screen.y) : screen;
        }

        // ---- input ----------------------------------------------------------------
        internal static Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }
        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new MouseState { position = pos };
            if (leftDown) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, st);
        }

        internal static Transform RayTopT(Vector2 screenPos)
        {
            try
            {
                if (EventSystem.current == null) return null;
                var pd = new PointerEventData(EventSystem.current);
                pd.position = screenPos;
                var list = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pd, list);
                if (list.Count == 0) return null;
                return list[0].gameObject.transform;
            }
            catch { return null; }
        }
        internal static string RayTop(Vector2 screenPos)
        {
            var t = RayTopT(screenPos);
            return t != null ? t.gameObject.name : "(none)";
        }
        internal static bool IsSelfOrKin(RectTransform rt, Transform hit)
        {
            if (rt == null || hit == null) return false;
            if (hit == rt.transform) return true;
            if (hit.IsChildOf(rt)) return true;
            if (rt.IsChildOf(hit)) return true;
            return false;
        }
        // MEASURED (t0e, 2026-09-21): uGUI's pointer here consumes the Y-FLIPPED value. Pick per
        // widget by raycast so a different canvas mode still resolves correctly.
        internal static Vector2 PickInjectPoint(RectTransform rt)
        {
            Vector2 lo, hi, c;
            if (!ScreenRect(rt, out lo, out hi, out c)) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var a = c;
            var b = new Vector2(c.x, Screen.height - c.y);
            if (IsSelfOrKin(rt, RayTopT(a))) return a;
            if (IsSelfOrKin(rt, RayTopT(b))) return b;
            return b;
        }

        // ---- widget read -----------------------------------------------------------
        internal static Graphic G(Component c)
        {
            if (c == null) return null;
            var g = c as Graphic;
            if (g == null) g = c.GetComponent<Graphic>();
            return g;
        }
        internal static Selectable S(Component c)
        {
            if (c == null) return null;
            var s = c as Selectable;
            if (s == null) s = c.GetComponent<Selectable>();
            return s;
        }
        internal static string SpriteOf(Component c)
        {
            var g = G(c);
            if (g == null) return "-";
            var img = g as Image;
            if (img == null) return "-";
            if (!img.enabled) return "(disabled)";
            return img.sprite != null ? img.sprite.name : "(null)";
        }
        internal static string WantSprite(Component c, int which)
        {
            var s = S(c);
            if (s == null) return "(no-selectable)";
            var sp = which == 0 ? s.spriteState.highlightedSprite : s.spriteState.pressedSprite;
            return sp != null ? sp.name : "(null)";
        }
        internal static int InteractableOf(Component c)
        {
            var s = S(c);
            if (s == null) return -1;
            return s.interactable ? 1 : 0;
        }
        internal static string NodePath(Transform t)
        {
            if (t == null) return "-";
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            var guard = 0;
            while (p != null && guard++ < 12) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        /// <summary>Invoke a private void no-arg method under a Stopwatch; returns microseconds (-1 on failure).</summary>
        internal static float InvokeUs(object o, string method)
        {
            if (o == null) return -1f;
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return -1f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { m.Invoke(o, null); } catch (Exception e) { Warn("invoke " + method + " EX " + e.GetType().Name + ": " + e.Message); }
            sw.Stop();
            return (float)(sw.Elapsed.TotalMilliseconds * 1000.0);
        }

        internal static string Stat(List<float> v)
        {
            if (v == null || v.Count == 0) return "n=0";
            var a = new List<float>(v); a.Sort();
            var sum = 0f; foreach (var x in a) sum += x;
            return "n=" + a.Count
                + " p50=" + a[(int)(0.50f * (a.Count - 1))].ToString("0.0")
                + " p99=" + a[(int)(0.99f * (a.Count - 1))].ToString("0.0")
                + " max=" + a[a.Count - 1].ToString("0.0")
                + " mean=" + (sum / a.Count).ToString("0.0");
        }
    }

    /// <summary>Public one-shot entries for the run script.</summary>
    public static class Api2
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = UnityEngine.InputSystem.InputSystem.settings;
            st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            if (Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + Drive.Fsm();
            Drive.Log(line);
            Drive.Log("DEVICE " + Drive.DeviceLine());
            return line;
        }

        public static string Mem()
        {
            long total = -1; long mono = -1; int gfx = -1; int sys = -1;
            try { total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); } catch { }
            try { mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(); } catch { }
            try { gfx = SystemInfo.graphicsMemorySize; } catch { }
            try { sys = SystemInfo.systemMemorySize; } catch { }
            var line = "MEM profilerTotalMB=" + (total / 1048576.0).ToString("0.0")
                + " monoUsedMB=" + (mono / 1048576.0).ToString("0.0")
                + " graphicsMemoryMB=" + gfx + " systemMemoryMB=" + sys
                + " fsm=" + Drive.Fsm();
            Drive.Log(line);
            return line;
        }

        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("T0IEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<T0IDriver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class T0IDriver : MonoBehaviour
    {
        // ------------------------------------------------------------------ state
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;
        private int _waitFrom = -1;
        private int _stFrame = -1;

        // pacing
        private readonly List<float> _steady = new List<float>();
        private float _steadyAt = -1f; private bool _steadyLogged;
        private readonly List<float> _cpuMain = new List<float>();

        // load
        private float _loadT0; private string _loadWall0; private string _loadWall1;

        // capture
        private bool _capBusy, _capDone, _capOk;
        private string _capInfo = "";
        private string _capPending = "";
        private int _capX, _capY, _capW, _capH;

        // confirm sweep (defect 2)
        private int _cfStage;
        private RectTransform _cfRt; private Component _cfComp;
        private string _cfN = "", _cfH = "", _cfP = "";
        private string _cfWantH = "", _cfWantP = "";
        private int _cfInterN = -1, _cfInterH = -1;

        // class selection
        private int _classStage;
        private RectTransform _spotRt;

        // repave measurement (manual pump)
        private bool _pumpActive;
        private Behaviour _mvBhv;
        private object _mvObj;
        private int _pumpFrames;
        private float _swapUs = -1f;
        private int _swapNodes = -1;
        private readonly List<float> _pumpUs = new List<float>();
        private readonly List<int> _pumpNodes = new List<int>();
        private int _pkNodesBefore, _pkCellsBefore, _framesLastBefore, _framesPeakBefore, _retirePeakBefore;

        // repave watch (engine side)
        private object _pvRef;
        private int _pvPool = -1, _pvNodes = -1;
        private bool _pvInProg;
        private int _repaveRows;
        private readonly List<string> _repaveRowsTxt = new List<string>();

        // area-change per-frame watch
        private bool _watchOn;
        private readonly List<string> _watch = new List<string>();
        private float _watchT0;
        private int _watchStartFrame;
        private int _midCaptured;
        private int _afterCaptured;
        private int _mmChunksBefore = -1, _mmChunksAfter = -1;

        private static int _mainThread = -1;
        private static readonly List<string> _ring = new List<string>();

        private bool _moorRequested, _loadSwept;
        private string _saveName = "";

        private class Widget
        {
            public RectTransform Rt;
            public Component Comp;
        }

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            Drive.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            Drive.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height);
            BuildPlan();
            Drive.KV("PLAN", "stations=" + _plan.Count);
        }

        private void Awake()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            Application.logMessageReceivedThreaded += OnAnyLog;
        }
        private void OnDestroy() { Application.logMessageReceivedThreaded -= OnAnyLog; }

        private void OnAnyLog(string cond, string stack, LogType type)
        {
            try
            {
                if (Thread.CurrentThread.ManagedThreadId != _mainThread) return;
                if (string.IsNullOrEmpty(cond)) return;
                var s = "f" + Time.frameCount + "|" + type + "|" + cond.Replace("\r", " ").Replace("\n", " ");
                if (s.Length > 240) s = s.Substring(0, 240);
                _ring.Add(s);
                while (_ring.Count > 90) _ring.RemoveAt(0);
            }
            catch { }
        }

        // ------------------------------------------------------------------ plumbing
        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            _plan.Add(() =>
            {
                if (_cur != nm)
                {
                    _cur = nm; _at = Time.unscaledTime; _waitFrom = -1; _stFrame = -1;
                    Drive.Log("PHASE " + nm + " fsm=" + Drive.Fsm() + " panels=" + Drive.Panels());
                }
                return f();
            });
        }
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private static bool FsmEq(string s) { return Drive.Fsm() == s; }
        private bool WaitFrames(int n)
        {
            if (_stFrame < 0) { _stFrame = Time.frameCount; return false; }
            return Time.frameCount - _stFrame >= n;
        }

        private void Update()
        {
            try { CollectFrame(); } catch (Exception e) { Drive.Warn("COLLECT-EX " + e.GetType().Name); }
            try { WatchRepave(); } catch (Exception e) { Drive.Warn("REPAVE-WATCH-EX " + e.GetType().Name); }
            try { PumpStep(); } catch (Exception e) { Drive.Warn("PUMP-EX " + e.GetType().Name); }
            try { WatchAreaChange(); } catch (Exception e) { Drive.Warn("WATCH-EX " + e.GetType().Name); }
            if (_done) return;
            try
            {
                var guard = 0;
                while (!_done && _pc < _plan.Count && guard++ < 4000)
                {
                    if (!_plan[_pc]()) return;
                    _pc++; _cur = "?"; _at = Time.unscaledTime;
                }
                if (_pc >= _plan.Count) Finish("end");
            }
            catch (Exception e)
            {
                Drive.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        private void CollectFrame()
        {
            try
            {
                var ft = new FrameTiming[1];
                FrameTimingManager.CaptureFrameTimings();
                var got = FrameTimingManager.GetLatestTimings(1, ft);
                if (got > 0) _cpuMain.Add((float)ft[0].cpuMainThreadFrameTime);
            }
            catch { }

            if (_steadyAt >= 0f && !_steadyLogged)
            {
                _steady.Add(Time.unscaledDeltaTime);
                if (_steady.Count >= 180)
                {
                    _steadyLogged = true;
                    var a = new List<float>(_steady); a.Sort();
                    Func<float, float> ms = s => s * 1000f;
                    var sum = 0f; foreach (var x in a) sum += x;
                    Drive.KV("PACING-STEADY", "n=" + a.Count
                        + " min=" + ms(a[0]).ToString("0.00")
                        + " p50=" + ms(a[(int)(0.50f * (a.Count - 1))]).ToString("0.00")
                        + " p95=" + ms(a[(int)(0.95f * (a.Count - 1))]).ToString("0.00")
                        + " max=" + ms(a[a.Count - 1]).ToString("0.00")
                        + " mean=" + ms(sum / a.Count).ToString("0.00") + " ms/frame"
                        + " cpuMain n=" + _cpuMain.Count
                        + " p50=" + Pct(_cpuMain, 0.50f).ToString("0.0000")
                        + " p95=" + Pct(_cpuMain, 0.95f).ToString("0.0000")
                        + " max=" + MaxOf(_cpuMain).ToString("0.0000")
                        + " ms; " + Drive.DeviceLine());
                }
            }
        }
        private static float Pct(List<float> v, float p)
        {
            if (v.Count == 0) return -1f;
            var a = new List<float>(v); a.Sort();
            return a[(int)(p * (a.Count - 1))];
        }
        private static float MaxOf(List<float> v)
        {
            var m = -1f;
            for (var i = 0; i < v.Count; i++) if (v[i] > m) m = v[i];
            return m;
        }

        // ================================================================ repave watch
        // T0FIX-H: the repave is now split across frames, so a single-frame poolDelta>=1000 no
        // longer happens. Detect it by the job lifetime (RebuildInProgress rising/falling edge)
        // and report the same fields the t0e watch reported.
        private void WatchRepave()
        {
            var mv = Drive.MapViewObj();
            if (mv == null) { _pvRef = null; _pvPool = -1; _pvNodes = -1; return; }
            var g = Drive.MapViewGo();
            var nodes = g != null ? Drive.NodeCount(g.transform) : -1;
            if (!ReferenceEquals(mv, _pvRef)) { _pvRef = mv; _pvPool = -1; _pvNodes = nodes; _pvInProg = false; return; }
            var c = Drive.IntProp(mv, "PoolCreatedCount");
            var r = Drive.IntProp(mv, "PoolReusedCount");
            var pool = (c >= 0 && r >= 0) ? (c + r) : -1;
            var inProg = Drive.BoolProp(mv, "RebuildInProgress");
            var poolDelta = (_pvPool >= 0 && pool >= 0) ? (pool - _pvPool) : 0;
            var nodeDelta = (_pvNodes >= 0 && nodes >= 0) ? (nodes - _pvNodes) : 0;

            if (inProg && !_pvInProg)
            {
                // rising edge: a rebuild job started this frame
                Drive.KV("REPAVE-BEGIN", "t=" + Time.unscaledTime.ToString("0.00")
                    + " frame=" + Time.frameCount + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " fsm=" + Drive.Fsm() + " uiLoading=" + (Drive.UiLoading() ? 1 : 0)
                    + " lpOpen=" + (Drive.LpOpen() ? 1 : 0) + " nodes=" + nodes);
            }
            if (!inProg && _pvInProg)
            {
                // falling edge: the swap frame just happened
                _repaveRows++;
                var loading = Drive.LoadingVisible();
                var line = "t=" + Time.unscaledTime.ToString("0.00")
                    + " wall=" + DateTime.Now.ToString("HH:mm:ss.fff")
                    + " frame=" + Time.frameCount
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " fsm=" + Drive.Fsm() + " scene=" + Drive.SceneName()
                    + " panels=" + Drive.Panels() + " uiLoading=" + (Drive.UiLoading() ? 1 : 0)
                    + " lpOpen=" + (Drive.LpOpen() ? 1 : 0) + " timeScale=" + Time.timeScale.ToString("0.##")
                    + " poolDelta=" + poolDelta + " nodeDelta=" + nodeDelta + " nodes=" + nodes
                    + " framesLast=" + Drive.IntProp(mv, "RebuildFramesLast")
                    + " framesPeak=" + Drive.IntProp(mv, "RebuildFramesPeak")
                    + " peakNodesPerFrame=" + Drive.IntProp(mv, "RebuildPeakNodesPerFrame")
                    + " peakCellsPerFrame=" + Drive.IntProp(mv, "RebuildPeakCellsPerFrame")
                    + " retirePeakPerFrame=" + Drive.IntProp(mv, "RetirePeakPerFrame")
                    + " pendingRetire=" + Drive.IntProp(mv, "PendingRetireChunks")
                    + " chunksNow=" + Drive.IntProp(mv, "BuiltChunkCount")
                    + " inLoading=" + (loading ? 1 : 0);
                Drive.KV("REPAVE", line);
                _repaveRowsTxt.Add(line);
                var win = Drive.RawDir + "/t0i_repave_window_" + _repaveRows + ".txt";
                Drive.WriteFile(win, "REPAVE " + line + "\n---- last " + _ring.Count + " engine log lines ----\n"
                    + string.Join("\n", _ring.ToArray()) + "\n");
                Drive.KV("LOGWINDOW-FILE", "n=" + _repaveRows + " file=" + win + " lines=" + _ring.Count);
            }
            _pvInProg = inProg;
            if (pool >= 0) _pvPool = pool;
            if (nodes >= 0) _pvNodes = nodes;
        }

        // ================================================================ manual pump
        // One call to the private PumpRebuild() == exactly one production frame's worth of work.
        // Measured under a Stopwatch; MapView.enabled is false during the window so its own
        // Update() cannot double-pump.
        private void BeginPumpMeasure()
        {
            _mvObj = Drive.MapViewObj();
            if (_mvObj == null) { Drive.Warn("PUMP no MapView"); return; }
            _mvBhv = _mvObj as Behaviour;
            _pumpUs.Clear(); _pumpNodes.Clear(); _pumpFrames = 0; _swapUs = -1f; _swapNodes = -1;
            _pkNodesBefore = Drive.IntProp(_mvObj, "RebuildPeakNodesPerFrame");
            _pkCellsBefore = Drive.IntProp(_mvObj, "RebuildPeakCellsPerFrame");
            _framesLastBefore = Drive.IntProp(_mvObj, "RebuildFramesLast");
            _framesPeakBefore = Drive.IntProp(_mvObj, "RebuildFramesPeak");
            _retirePeakBefore = Drive.IntProp(_mvObj, "RetirePeakPerFrame");
            var area = Drive.Map() != null ? Drive.Map().Area : AreaId.Town;
            Drive.KV("PUMP-BEGIN", "area=" + area
                + " builtChunks=" + Drive.IntProp(_mvObj, "BuiltChunkCount")
                + " nodes=" + Drive.NodeCount(Drive.MapViewGo().transform)
                + " maxTileNodesPerFrame=" + Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxTileNodesPerFrame")
                + " maxTileCellsPerFrame=" + Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxTileCellsPerFrame"));
            try { ((Diablo2.Module.Map.MapView)_mvObj).ShowArea(area); }
            catch (Exception e) { Drive.Warn("PUMP ShowArea EX " + e.GetType().Name); }
            if (_mvBhv != null) _mvBhv.enabled = false;
            _pumpActive = true;
        }

        private void PumpStep()
        {
            if (!_pumpActive || _mvObj == null) return;
            var go = Drive.MapViewGo();
            var n0 = go != null ? Drive.NodeCount(go.transform) : -1;
            var us = Drive.InvokeUs(_mvObj, "PumpRebuild");
            var n1 = go != null ? Drive.NodeCount(go.transform) : -1;
            var nodes = (n0 >= 0 && n1 >= n0) ? (n1 - n0) : 0;
            _pumpUs.Add(us); _pumpNodes.Add(nodes);
            _pumpFrames++;
            var inProg = Drive.BoolProp(_mvObj, "RebuildInProgress");
            if (!inProg)
            {
                _swapUs = us; _swapNodes = nodes;
                _pumpActive = false;
                if (_mvBhv != null) _mvBhv.enabled = true;
                FinishPumpMeasure();
            }
            else if (_pumpFrames > 600)
            {
                Drive.Warn("PUMP runaway >600 frames; aborting window");
                _pumpActive = false;
                if (_mvBhv != null) _mvBhv.enabled = true;
                FinishPumpMeasure();
            }
        }

        private void FinishPumpMeasure()
        {
            var mv = _mvObj;
            var budget = 1000000.0 / 60.0;
            // per-frame us-per-GO on the frames that actually built nodes
            var usPerGo = new List<float>();
            var builtFrames = 0;
            for (var i = 0; i < _pumpUs.Count; i++)
            {
                if (_pumpNodes[i] > 0) { builtFrames++; usPerGo.Add(_pumpUs[i] / _pumpNodes[i]); }
            }
            var maxFrameUs = MaxOf(_pumpUs);
            var worstUsPerGo = MaxOf(usPerGo);
            var worstNodes = 0;
            for (var i = 0; i < _pumpUs.Count; i++) if (_pumpUs[i] >= maxFrameUs - 0.0001f && _pumpNodes[i] > worstNodes) worstNodes = _pumpNodes[i];
            var peakNodes = Drive.IntProp(mv, "RebuildPeakNodesPerFrame");
            var peakCells = Drive.IntProp(mv, "RebuildPeakCellsPerFrame");
            var framesLast = Drive.IntProp(mv, "RebuildFramesLast");
            var framesPeak = Drive.IntProp(mv, "RebuildFramesPeak");
            var retirePeak = Drive.IntProp(mv, "RetirePeakPerFrame");
            var maxTile = Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxTileNodesPerFrame");
            Drive.KV("PUMP-SUMMARY", "frames=" + _pumpFrames + " builtFrames=" + builtFrames
                + " maxTileNodesPerFrame=" + maxTile
                + " allFrameUs[" + Drive.Stat(_pumpUs) + "]"
                + " builtFrameUs[" + Drive.Stat(usPerGo) + "](us/GO on node-building frames)"
                + " maxFrameUs=" + maxFrameUs.ToString("0.0")
                + " maxFrameNodes=" + worstNodes
                + " worstUsPerGo=" + (worstUsPerGo >= 0f ? worstUsPerGo.ToString("0.000") : "-")
                + " oneFrameBudgetUs=" + budget.ToString("0.0")
                + " maxFrame_over_budget=" + (maxFrameUs / budget).ToString("0.000")
                + " swapFrameUs=" + _swapUs.ToString("0.0")
                + " swapFrameNodes=" + _swapNodes
                + " peakNodesPerFrame=" + peakNodes + "(<=? " + maxTile + " => " + (peakNodes <= maxTile ? "OK" : "VIOLATION") + ")"
                + " peakCellsPerFrame=" + peakCells
                + " framesLast=" + framesLast + " framesPeak=" + framesPeak
                + " retirePeakPerFrame=" + retirePeak
                + " poolNew=" + Drive.IntProp(mv, "PoolCreatedCount")
                + " poolReused=" + Drive.IntProp(mv, "PoolReusedCount")
                + " poolFree=" + Drive.IntProp(mv, "PoolFreeCount")
                + " builtChunks=" + Drive.IntProp(mv, "BuiltChunkCount")
                + " (selfWitness before: peakNodes=" + _pkNodesBefore + " peakCells=" + _pkCellsBefore
                + " framesLast=" + _framesLastBefore + " framesPeak=" + _framesPeakBefore + " retirePeak=" + _retirePeakBefore + ")");
        }

        // ================================================================ area-change watch
        private void WatchAreaChange()
        {
            if (!_watchOn) return;
            var mv = Drive.MapViewObj();
            var go = Drive.MapViewGo();
            var gRoot = Drive.Field(mv, "_groundRoot") as Transform;
            var oRoot = Drive.Field(mv, "_objectRoot") as Transform;
            var ovRoot = Drive.Field(mv, "_overlayRoot") as Transform;
            var gOn = (gRoot != null && gRoot.gameObject.activeInHierarchy) ? 1 : 0;
            var oOn = (oRoot != null && oRoot.gameObject.activeInHierarchy) ? 1 : 0;
            var ovOn = (ovRoot != null && ovRoot.gameObject.activeInHierarchy) ? 1 : 0;
            var agc = Drive.ActiveChildCount(gRoot);
            var row = "f=" + Time.frameCount
                + " t=" + (Time.realtimeSinceStartup - _watchT0).ToString("0.000")
                + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                + " fsm=" + Drive.Fsm() + " uiLoading=" + (Drive.UiLoading() ? 1 : 0)
                + " lpOpen=" + (Drive.LpOpen() ? 1 : 0)
                + " builtChunks=" + Drive.IntProp(mv, "BuiltChunkCount")
                + " gRootOn=" + gOn + " oRootOn=" + oOn + " ovRootOn=" + ovOn
                + " activeGroundChunks=" + agc
                + " inProg=" + (Drive.BoolProp(mv, "RebuildInProgress") ? 1 : 0)
                + " framesLast=" + Drive.IntProp(mv, "RebuildFramesLast")
                + " pendingRetire=" + Drive.IntProp(mv, "PendingRetireChunks")
                + " nodes=" + (go != null ? Drive.NodeCount(go.transform) : -1);
            _watch.Add(row);
            if (_watch.Count > 400) _watch.RemoveAt(0);
        }

        private void FinishAreaWatch()
        {
            var mv = Drive.MapViewObj();
            // blank = a Stage frame with no active layer root / no active ground chunk
            var blanks = 0; var halves = 0;
            var minActive = int.MaxValue;
            for (var i = 0; i < _watch.Count; i++)
            {
                var r = _watch[i];
                var gOn = Kv(r, "gRootOn=");
                var agc = Kv(r, "activeGroundChunks=");
                var inStage = r.Contains("fsm=Stage");
                if (inStage && (gOn <= 0 || agc <= 0)) blanks++;
                if (inStage && agc > 0 && agc < minActive) minActive = agc;
            }
            if (_mmChunksBefore > 0 && _mmChunksAfter > 0)
            {
                var lo = Math.Min(_mmChunksBefore, _mmChunksAfter);
                for (var i = 0; i < _watch.Count; i++)
                {
                    var agc = Kv(_watch[i], "activeGroundChunks=");
                    if (_watch[i].Contains("fsm=Stage") && agc > 0 && agc < lo) halves++;
                }
            }
            // frames the OLD map stayed up = frames from the area-change request until the swap
            var oldFrames = 0; var oldMs = -1f;
            for (var i = 0; i < _watch.Count; i++)
            {
                var inProg = Kv(_watch[i], "inProg=");
                var t = KvF(_watch[i], "t=");
                if (inProg == 1) { oldFrames++; oldMs = t; }
            }
            Drive.KV("AREACHANGE-SUMMARY", "rows=" + _watch.Count
                + " blankStageFrames=" + blanks + " halfStageFrames=" + halves
                + " minActiveGroundChunksInStage=" + (minActive == int.MaxValue ? -1 : minActive)
                + " oldMapShownFrames=" + oldFrames + " oldMapShownMs=" + (oldMs >= 0f ? (oldMs * 1000f).ToString("0.0") : "-")
                + " chunksBefore=" + _mmChunksBefore + " chunksAfter=" + _mmChunksAfter
                + " framesLastAfterSwap=" + Drive.IntProp(mv, "RebuildFramesLast"));
            for (var i = 0; i < _watch.Count && i < 60; i++) Drive.KV("AREACHANGE-ROW", _watch[i]);
        }
        private static int Kv(string row, string key)
        {
            var i = row.IndexOf(key);
            if (i < 0) return -9999;
            i += key.Length;
            var j = i;
            while (j < row.Length && row[j] != ' ') j++;
            int v;
            return int.TryParse(row.Substring(i, j - i), out v) ? v : -9999;
        }
        private static float KvF(string row, string key)
        {
            var i = row.IndexOf(key);
            if (i < 0) return -1f;
            i += key.Length;
            var j = i;
            while (j < row.Length && row[j] != ' ') j++;
            float v;
            return float.TryParse(row.Substring(i, j - i), out v) ? v : -1f;
        }

        // ================================================================ capture
        private IEnumerator CaptureRoutine(string file)
        {
            _capDone = false; _capOk = false; _capInfo = "";
            yield return new WaitForEndOfFrame();
            Texture2D full = null; Texture2D crop = null;
            try
            {
                var w = _capW; var h = _capH; var x0 = _capX; var y0 = _capY;
                if (w <= 0 || h <= 0) { _capInfo = "bad-rect"; }
                else
                {
                    full = ScreenCapture.CaptureScreenshotAsTexture();
                    if (full == null) { _capInfo = "capture-null"; }
                    else
                    {
                        crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
                        crop.ReadPixels(new Rect(x0, y0, w, h), 0, 0);
                        crop.Apply();
                        var px = crop.GetPixels();
                        float sum = 0f; var mn = 3f; var mx = 0f;
                        for (var i = 0; i < px.Length; i++) { var l = px[i].r + px[i].g + px[i].b; sum += l; if (l < mn) mn = l; if (l > mx) mx = l; }
                        var mean = px.Length > 0 ? sum / (px.Length * 3f) : -1f;
                        var dir = Path.GetDirectoryName(file);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        var bytes = crop.EncodeToPNG();
                        File.WriteAllBytes(file, bytes);
                        _capOk = true;
                        _capInfo = "x0=" + x0 + " y0=" + y0 + " w=" + w + " h=" + h
                            + " bytes=" + bytes.Length + " mean=" + mean.ToString("0.000")
                            + " lumMin=" + mn.ToString("0.000") + " lumMax=" + mx.ToString("0.000");
                    }
                }
            }
            catch (Exception e) { _capInfo = "EX-" + e.GetType().Name + "-" + e.Message; Drive.Warn("CAPTURE-FAIL " + file + " " + _capInfo); }
            finally
            {
                if (full != null) UnityEngine.Object.Destroy(full);
                if (crop != null) UnityEngine.Object.Destroy(crop);
            }
            _capDone = true;
        }

        private IEnumerator CaptureFullRoutine(string file)
        {
            _capDone = false; _capOk = false; _capInfo = "";
            yield return new WaitForEndOfFrame();
            Texture2D full = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) { _capInfo = "capture-null"; }
                else
                {
                    var px = full.GetPixels();
                    float sum = 0f;
                    for (var i = 0; i < px.Length; i++) sum += px[i].r + px[i].g + px[i].b;
                    var mean = px.Length > 0 ? sum / (px.Length * 3f) : -1f;
                    var dir = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var bytes = full.EncodeToPNG();
                    File.WriteAllBytes(file, bytes);
                    _capOk = true;
                    _capInfo = "full " + full.width + "x" + full.height + " bytes=" + bytes.Length
                        + " meanLum=" + mean.ToString("0.000");
                }
            }
            catch (Exception e) { _capInfo = "EX-" + e.GetType().Name + "-" + e.Message; Drive.Warn("CAPTURE-FULL-FAIL " + file + " " + _capInfo); }
            finally { if (full != null) UnityEngine.Object.Destroy(full); }
            _capDone = true;
        }

        private bool CaptureFullNow(string file)
        {
            if (!_capBusy)
            {
                _capPending = file; _capBusy = true;
                StartCoroutine(CaptureFullRoutine(file));
                return false;
            }
            if (!_capDone) return false;
            _capBusy = false;
            Drive.KV("TILE", "file=" + _capPending + " ok=" + (_capOk ? 1 : 0) + " " + _capInfo);
            _capPending = "";
            return true;
        }

        private bool CaptureNow(RectTransform rt, string file)
        {
            if (!_capBusy)
            {
                Vector2 lo, hi, c;
                Drive.ScreenRect(rt, out lo, out hi, out c);
                _capX = Mathf.Max(0, Mathf.FloorToInt(lo.x) - 6);
                _capY = Mathf.Max(0, Mathf.FloorToInt(lo.y) - 6);
                var x1 = Mathf.Min(Screen.width, Mathf.CeilToInt(hi.x) + 6);
                var y1 = Mathf.Min(Screen.height, Mathf.CeilToInt(hi.y) + 6);
                _capW = x1 - _capX; _capH = y1 - _capY;
                _capPending = file;
                _capBusy = true;
                StartCoroutine(CaptureRoutine(file));
                return false;
            }
            if (!_capDone) return false;
            _capBusy = false;
            Drive.KV("TILE", "file=" + _capPending + " ok=" + (_capOk ? 1 : 0) + " " + _capInfo);
            _capPending = "";
            return true;
        }

        // ================================================================ confirm sweep (defect 2)
        private bool ConfirmSweep()
        {
            var panel = Game.UI != null ? Game.UI.Get<Diablo2.UI.CharCreatePanel>() : null;
            if (panel == null) { Drive.Warn("CONFIRM no CharCreatePanel"); return true; }
            if (_cfRt == null)
            {
                var t = Drive.FindByName(panel.transform, "Confirm");
                _cfRt = t as RectTransform;
                if (_cfRt == null) { Drive.Warn("CONFIRM no Confirm node"); return true; }
                var sel = _cfRt.GetComponent<Selectable>();
                var tg = sel != null ? sel.targetGraphic : null;
                if (tg != null && tg.rectTransform != null) _cfRt = tg.rectTransform;
                _cfComp = tg != null ? (Component)tg : (sel != null ? (Component)sel : (Component)_cfRt.GetComponent<Graphic>());
                Drive.KV("CONFIRM-NODE", "path=" + Drive.NodePath(t) + " tf=" + (sel != null ? sel.transition.ToString() : "(no-selectable)")
                    + " targetGraphic=" + (tg != null ? tg.name : "(null)"));
                _cfStage = 0;
                return false;
            }
            switch (_cfStage)
            {
                case 0:
                    Drive.MouseState(new Vector2(2f, 2f), false);
                    _cfStage = 1; _stFrame = Time.frameCount; return false;
                case 1:
                    if (Time.frameCount - _stFrame < 2) return false;
                    _cfN = Drive.SpriteOf(_cfComp); _cfWantH = Drive.WantSprite(_cfComp, 0); _cfWantP = Drive.WantSprite(_cfComp, 1);
                    _cfInterN = Drive.InteractableOf(_cfComp);
                    Drive.KV("CONFIRM-BEFORE", "sprN=" + _cfN + " wantH=" + _cfWantH + " wantP=" + _cfWantP
                        + " interactable=" + _cfInterN + " trans=" + (Drive.S(_cfComp) != null ? Drive.S(_cfComp).transition.ToString() : "-"));
                    if (!CaptureNow(_cfRt, Drive.RawDir + "/t0i_confirm_n.png")) return false;
                    _cfStage = 2; return false;
                case 2:
                    Drive.MouseState(Drive.PickInjectPoint(_cfRt), false);
                    _cfStage = 3; _stFrame = Time.frameCount; return false;
                case 3:
                    if (Time.frameCount - _stFrame < 2) return false;
                    _cfH = Drive.SpriteOf(_cfComp); _cfInterH = Drive.InteractableOf(_cfComp);
                    if (!CaptureNow(_cfRt, Drive.RawDir + "/t0i_confirm_h.png")) return false;
                    _cfStage = 4; return false;
                case 4:
                    Drive.MouseState(Drive.PickInjectPoint(_cfRt), true);
                    _cfStage = 5; _stFrame = Time.frameCount; return false;
                case 5:
                    if (Time.frameCount - _stFrame < 2) return false;
                    _cfP = Drive.SpriteOf(_cfComp);
                    if (!CaptureNow(_cfRt, Drive.RawDir + "/t0i_confirm_p.png")) return false;
                    _cfStage = 6; return false;
                case 6:
                    Vector2 lo, hi, c;
                    Drive.ScreenRect(_cfRt, out lo, out hi, out c);
                    Drive.KV("CONFIRM", "panel=CharCreatePanel widget=Confirm"
                        + " rect=" + lo.x.ToString("0") + "," + lo.y.ToString("0") + "," + hi.x.ToString("0") + "," + hi.y.ToString("0")
                        + " sprN=" + _cfN + " sprH=" + _cfH + " sprP=" + _cfP
                        + " wantH=" + _cfWantH + " wantP=" + _cfWantP
                        + " interactableBefore=" + _cfInterN + " interactableHover=" + _cfInterH
                        + " hoverChanged=" + ((_cfH != _cfN) ? 1 : 0) + " pressChanged=" + ((_cfP != _cfH) ? 1 : 0)
                        + " rayTop=" + Drive.RayTop(Drive.PickInjectPoint(_cfRt))
                        + " tileN=t0i_confirm_n.png tileH=t0i_confirm_h.png tileP=t0i_confirm_p.png");
                    Drive.MouseState(new Vector2(2f, 2f), false);
                    _cfStage = 7; return true;
            }
            return false;
        }

        // ================================================================ the plan
        private void BuildPlan()
        {
            Add("wait60", () => WaitFrames(60));

            Add("boot", () =>
            {
                if (FsmEq("MainMenu")) return true;
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(25f)) { Drive.Warn("boot timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("menu-wait", () =>
            {
                var root = PanelRoot("MainMenuPanel");
                if (root != null) return true;
                if (Elapsed(25f)) { Drive.Warn("menu-wait timeout fsm=" + Drive.Fsm() + " panels=" + Drive.Panels()); return true; }
                return false;
            });
            Add("charselect", () =>
            {
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(20f)) { Drive.Warn("charselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("charcreate", () =>
            {
                if (FsmEq("CharCreate")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
                if (Elapsed(20f)) { Drive.Warn("charcreate timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // defect 2: pick a class with a REAL pointer click, then sweep Confirm
            Add("cc-pick-class", () =>
            {
                var panel = Game.UI != null ? Game.UI.Get<Diablo2.UI.CharCreatePanel>() : null;
                if (panel == null) { Drive.Warn("PICK no CharCreatePanel"); return true; }
                if (_spotRt == null)
                {
                    var t = Drive.FindByName(panel.transform, "SpotAmazon");
                    _spotRt = t as RectTransform;
                    if (_spotRt == null) { Drive.Warn("PICK no SpotAmazon node"); return true; }
                    Drive.KV("PICK-BEGIN", "node=" + Drive.NodePath(_spotRt) + " classIndexBefore=" + Drive.IntField(panel, "_classIndex"));
                }
                if (_classStage == 0)
                {
                    Drive.MouseState(new Vector2(2f, 2f), false); _classStage = 1; _stFrame = Time.frameCount; return false;
                }
                if (_classStage == 1) { if (Time.frameCount - _stFrame < 2) return false; Drive.MouseState(Drive.PickInjectPoint(_spotRt), true); _classStage = 2; _stFrame = Time.frameCount; return false; }
                if (_classStage == 2) { if (Time.frameCount - _stFrame < 2) return false; Drive.MouseState(Drive.PickInjectPoint(_spotRt), false); _classStage = 3; _stFrame = Time.frameCount; return false; }
                if (_classStage == 3)
                {
                    if (Time.frameCount - _stFrame < 3 && Drive.IntField(panel, "_classIndex") < 0) return false;
                    var idx = Drive.IntField(panel, "_classIndex");
                    Drive.KV("PICK-CLASS", "classIndex=" + idx + " (>=0 => a class is selected => Confirm is enabled)");
                    if (idx < 0) { Drive.Warn("PICK failed: classIndex still <0"); }
                    _classStage = 4;
                    return true;
                }
                return true;
            });
            Add("cc-confirm-sweep", () => ConfirmSweep());
            // park the pointer + leave CharCreate
            Add("backselect", () =>
            {
                Drive.MouseState(new Vector2(2f, 2f), false);
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                if (Elapsed(20f)) { Drive.Warn("backselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("loading", () =>
            {
                if (FsmEq("Stage")) return true;
                if (FsmEq("Loading"))
                {
                    if (_loadT0 <= 0f) { _loadT0 = Time.realtimeSinceStartup; _loadWall0 = DateTime.Now.ToString("HH:mm:ss.fff"); }
                    return false;
                }
                if (Elapsed(1.0f)) EnterStage();
                if (Elapsed(60f)) { Drive.Warn("loading timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("stage-settle", () =>
            {
                if (!FsmEq("Stage") || !(Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>()))
                {
                    if (Elapsed(40f)) { Drive.Warn("stage wait timeout fsm=" + Drive.Fsm()); return true; }
                    return false;
                }
                if (!Elapsed(3.0f)) return false;
                _loadWall1 = DateTime.Now.ToString("HH:mm:ss.fff");
                var secs = Time.realtimeSinceStartup - _loadT0;
                Drive.KV("LOAD", "enter=" + _loadWall0 + " ready=" + _loadWall1
                    + " seconds=" + secs.ToString("0.00") + " fsm=" + Drive.Fsm());
                Drive.KV("STAGE-READY", "fsm=" + Drive.Fsm() + " panels=" + Drive.Panels()
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " " + Drive.DeviceLine());
                return true;
            });

            Add("mem", () => { Api2.Mem(); return true; });

            Add("steady", () =>
            {
                if (_steadyAt < 0f) { if (!Elapsed(0.5f)) return false; _steadyAt = 1f; _steady.Clear(); _steadyLogged = false; _cpuMain.Clear(); return false; }
                return _steadyLogged;
            });

            Add("repave-drain", () =>
            {
                var mv = Drive.MapViewObj();
                if (mv == null) { Drive.Warn("DRAIN no MapView"); return true; }
                if (Drive.BoolProp(mv, "RebuildInProgress") || Drive.IntProp(mv, "PendingRetireChunks") > 0)
                {
                    if (Elapsed(15f)) { Drive.Warn("DRAIN timeout inProg=" + Drive.BoolProp(mv, "RebuildInProgress") + " retire=" + Drive.IntProp(mv, "PendingRetireChunks")); return true; }
                    return false;
                }
                if (!Elapsed(0.5f)) return false;
                Drive.KV("DRAIN-OK", "builtChunks=" + Drive.IntProp(mv, "BuiltChunkCount")
                    + " nodes=" + Drive.NodeCount(Drive.MapViewGo().transform));
                return true;
            });

            Add("repave-measure", () =>
            {
                if (!_pumpActive && _pumpUs.Count == 0) { BeginPumpMeasure(); }
                // PumpStep() (in Update) does the work; this station is done once the window closed
                return (!_pumpActive && _pumpUs.Count > 0) || Elapsed(30f);
            });
            Add("repave-settle", () =>
            {
                var mv = Drive.MapViewObj();
                if (Drive.BoolProp(mv, "RebuildInProgress") || Drive.IntProp(mv, "PendingRetireChunks") > 0)
                {
                    if (Elapsed(15f)) return true;
                    return false;
                }
                if (!Elapsed(0.4f)) return false;
                if (_mmChunksBefore < 0) _mmChunksBefore = Drive.IntProp(mv, "BuiltChunkCount");
                return true;
            });

            // real area change + per-frame watch
            Add("to-moor", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor)
                {
                    if (!Elapsed(0.3f)) return false;
                    return true;
                }
                if (!_moorRequested && Elapsed(0.4f))
                {
                    _moorRequested = true;
                    _watch.Clear(); _watchOn = true; _watchT0 = Time.realtimeSinceStartup; _watchStartFrame = Time.frameCount;
                    _midCaptured = 0; _afterCaptured = 0;
                    Drive.KV("MOOR-ENTER", "emit ExitEntered(BloodMoor) watchStartFrame=" + Time.frameCount
                        + " areaBefore=" + (m != null ? m.Area.ToString() : "-"));
                    Game.Event.Emit<AreaId>(Events.ExitEntered, AreaId.BloodMoor);
                }
                if (Elapsed(30f)) { Drive.Warn("to-moor timeout area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")); return true; }
                return false;
            });

            // mid-rebuild screenshot: 3 frames after the request the OLD map must still be up
            Add("moor-mid-shot", () =>
            {
                var mv = Drive.MapViewObj();
                if (_midCaptured == 0 && Time.frameCount - _watchStartFrame >= 3)
                {
                    if (!CaptureFullNow(Drive.RawDir + "/t0i_areachange_mid.png")) return false;
                    _midCaptured = 1;
                    Drive.KV("MID-SHOT", "frame=" + Time.frameCount + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                        + " inProg=" + (Drive.BoolProp(mv, "RebuildInProgress") ? 1 : 0)
                        + " builtChunks=" + Drive.IntProp(mv, "BuiltChunkCount"));
                    return true;
                }
                return true;
            });

            Add("moor-settle", () =>
            {
                var mv = Drive.MapViewObj();
                if (Drive.BoolProp(mv, "RebuildInProgress") || Drive.IntProp(mv, "PendingRetireChunks") > 0)
                {
                    if (Elapsed(20f)) return true;
                    return false;
                }
                if (!Elapsed(1.0f)) return false;
                if (_afterCaptured == 0)
                {
                    if (!CaptureFullNow(Drive.RawDir + "/t0i_areachange_after.png")) return false;
                    _afterCaptured = 1;
                    _mmChunksAfter = Drive.IntProp(mv, "BuiltChunkCount");
                    Drive.KV("AFTER-SHOT", "frame=" + Time.frameCount + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                        + " builtChunks=" + _mmChunksAfter);
                }
                _watchOn = false;
                FinishAreaWatch();
                Api2.Mem();
                return true;
            });

            Add("finish", () =>
            {
                Drive.KV("REPAVE-SUMMARY", "rows=" + _repaveRows
                    + " (threshold was poolDelta>=1000 in t0e; T0FIX-H splits it => watch the inProg lifetime)");
                for (var i = 0; i < _repaveRowsTxt.Count && i < 12; i++) Drive.KV("REPAVE-ROW", _repaveRowsTxt[i]);
                return true;
            });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private Transform PanelRoot(string name)
        {
            if (Game.UI == null) return null;
            Component c = null;
            if (name == "MainMenuPanel") c = Game.UI.Get<Diablo2.UI.MainMenuPanel>();
            else if (name == "CharSelectPanel") c = Game.UI.Get<Diablo2.UI.CharSelectPanel>();
            else if (name == "CharCreatePanel") c = Game.UI.Get<Diablo2.UI.CharCreatePanel>();
            return c != null ? c.transform : null;
        }

        private void EnterStage()
        {
            var save = Drive.CtxMember("Save") as Diablo2.Module.ISaveModule;
            if (save != null)
            {
                var list = save.List();
                if (list != null && list.Count > 0) _saveName = list[0];
            }
            if (string.IsNullOrEmpty(_saveName)) { Drive.Warn("EnterStage: no save found; using X162012"); _saveName = "X162012"; }
            Drive.KV("ENTER-STAGE", "save=\"" + _saveName + "\"");
            Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Drive.KV("FINISH", "why=" + why + " pc=" + _pc + " fsm=" + Drive.Fsm()
                + " repaveRows=" + _repaveRows + " " + Drive.DeviceLine());
            Drive.WriteFile(Drive.DonePath, "T0I-DONE why=" + why + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
