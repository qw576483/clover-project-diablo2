// t0e_drive.cs -- ONE Play session (slice t0-e43-hover-acceptance).
//
// WHY ONE CHAIN (SKILL 2 / unity-cli: "a batch action, not a per-item loop"):
//   three things exist only inside one live session:
//     (a) the D4UI interaction rows: for every interactive control in every panel we must
//         inject a REAL pointer hover and a REAL pointer press and read what the widget
//         actually did (sprite swap / canvas tint / panel-level sprite change).
//         Offline "wiring" evidence (original frames + SpriteSwap) is not a presentation
//     (b) the S2 MapView full-repave (RebuildLayers) trigger times: WHEN the non-incremental
//         whole-map pave fires during a real enter-stage / texture-arrival / area-change
//         chain, and whether a loading bracket is up at that instant (E43 judgement).
//     (c) the death gold penalty (acceptance rows 33 / 47): goldBefore -> goldAfterDeath
//         after a REAL death (IPlayerModule.Kill through the production chain).
//
// WHAT IT MEASURES / WRITES (all lines are [T0E] tagged -> client/Logs/Editor.log)
//   DEVICE              render device + resolution
//   CALIB*              the injected pointer's Y convention, proved on a known button
//   PANEL-SWEEP-BEGIN   panel=<name> widgets=<n>
//   HOVER               panel=.. idx=.. cell=gN widget=<path> kind=.. rect=..
//                       sprN/H/P=.. wantH=.. wantP=.. trans=.. ray=..
//                       tintN/H/P=.. ctintN/H/P=.. rayTopBL=.. rayTopTL=..
//                       highOk= pressOk= hoverChanged= pressChanged= panelSprChanged=
//   HOVER-SUMMARY       panel=.. widgets=.. hoverChanged=.. pressChanged=.. noHover=.. noPress=..
//   PANEL-SWEEP-END     panel=<name> rows=<n>
//   REPAVE              t=.. wall=.. frame=.. area=.. fsm=.. scene=.. panels=.. uiLoading=..
//                       lpOpen=.. timeScale=.. poolDelta=.. nodeDelta=.. nodes=..
//                       chunksBefore=.. chunksAfter=.. inLoading=0/1
//   REPAVE-SUMMARY      rows=.. inLoading=.. outsideLoading=..   (+ REPAVE-OUTSIDE per row)
//   LOGWINDOW-FILE      the engine log lines (with frame numbers) around each repave
//   GOLD                goldBefore=.. seeded=.. goldAtDeath=.. goldAfterDeath=.. lost=..
//                       expected=.. floorOk=0/1 nonNegOk=0/1 dead=0/1
//   DEATH-SCREEN / TILE
//
// Tiles land in <repo>/.ai-tmp/screenshots/t0e_hover/ (outside the Unity project => never
// imported => the session survives).
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

namespace T0E
{
    public static class Drive
    {
        internal const string Tag = "T0E";

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
        internal static Diablo2.Module.IMonsterModule Monster() { return CtxMember("Monster") as Diablo2.Module.IMonsterModule; }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }
        internal static int IntProp(object o, string name)
        {
            if (o == null) return -1;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -1;
            var v = p.GetValue(o);
            return v is int ? (int)v : -1;
        }
        internal static int NodeCount(Transform root)
        {
            if (root == null) return -1;
            return root.GetComponentsInChildren<Transform>(true).Length;
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
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(), "Inventory");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharacterPanel>(), "Character");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.SkillTreePanel>(), "SkillTree");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>(), "MiniMap");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.QuestLogPanel>(), "QuestLog");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>(), "Dialog");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.ShopPanel>(), "Shop");
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
        internal static Vector2 RectInjectPoint(RectTransform rt)
        {
            Vector2 lo, hi, c;
            if (!ScreenRect(rt, out lo, out hi, out c)) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return Inject(c);
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
        internal static string TintOf(Component c)
        {
            var img = G(c) as Image;
            if (img == null) return "-";
            var k = img.color;
            return k.r.ToString("0.000") + "/" + k.g.ToString("0.000") + "/" + k.b.ToString("0.000") + "/" + k.a.ToString("0.000");
        }
        /// <summary>uGUI ColorTint writes the CANVAS RENDERER colour, not Image.color.</summary>
        internal static string CanvasTintOf(Component c)
        {
            var g = G(c);
            if (g == null) return "-";
            var k = g.canvasRenderer.GetColor();
            return k.r.ToString("0.000") + "/" + k.g.ToString("0.000") + "/" + k.b.ToString("0.000") + "/" + k.a.ToString("0.000");
        }
        internal static string TransitionOf(Component c)
        {
            var s = S(c);
            return s != null ? s.transition.ToString() : "(no-selectable)";
        }
        internal static string WantSprite(Component c, int which)
        {
            var s = S(c);
            if (s == null) return "(no-selectable)";
            var sp = which == 0 ? s.spriteState.highlightedSprite : s.spriteState.pressedSprite;
            return sp != null ? sp.name : "(null)";
        }
        internal static string ColorWantSprite(Component c, int which)
        {
            var s = S(c);
            if (s == null) return "(no-selectable)";
            var cb = s.colors;
            var col = which == 0 ? cb.highlightedColor : cb.pressedColor;
            return col.r.ToString("0.000") + "/" + col.g.ToString("0.000") + "/" + col.b.ToString("0.000") + "/" + col.a.ToString("0.000");
        }
        internal static int RayTargetOf(Component c)
        {
            var g = G(c);
            return (g != null && g.raycastTarget) ? 1 : 0;
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
        /// <summary>
        /// (Screen.height - y) -- the flipped candidate raycasts onto the widget itself for
        /// 190/191 controls, the unflipped one for only 27/191.  Rather than hard-code it, pick
        /// per widget by raycast so a different project/canvas mode still resolves correctly.
        /// </summary>
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
        /// <summary>Fingerprint of every enabled Image sprite name under a root.</summary>
        internal static string SpriteKey(Transform root)
        {
            if (root == null) return "-";
            var list = new List<string>();
            var imgs = root.GetComponentsInChildren<Image>(true);
            for (var i = 0; i < imgs.Length; i++)
            {
                var img = imgs[i];
                if (img == null || !img.enabled) continue;
                list.Add(img.sprite != null ? img.sprite.name : "(null)");
            }
            list.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (var i = 0; i < list.Count; i++) { sb.Append(list[i]); sb.Append(';'); }
            return "n=" + list.Count + "|" + sb.ToString();
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
            var es = EventSystem.current;
            Drive.KV("EVENTSYSTEM", "current=" + (es != null ? es.gameObject.name : "(null)")
                + " module=" + (es != null && es.currentInputModule != null ? es.currentInputModule.GetType().Name : "(null)")
                + " inputSystemUI=" + (es != null && es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() != null ? 1 : 0));
            return line;
        }

        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("T0EEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<T0EDriver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class T0EDriver : MonoBehaviour
    {
        // ------------------------------------------------------------------ state
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;
        private int _waitFrom = -1;

        // calibration
        private bool _calibInit, _calibFlipTried;
        private bool _calibFlip;
        private Selectable _calibTarget;
        private string _calibBase = "", _calibHigh = "";

        // sweep
        private readonly List<Widget> _widgets = new List<Widget>();
        private int _wi, _stage, _stageFrame;
        private bool _sweepStarted;
        private int _globalCell;
        private string _panelName = "";
        private string _panelKeyN = "", _panelKeyH = "", _panelKeyP = "";
        private string _sprN = "", _sprH = "", _sprP = "";
        private string _tinN = "", _tinH = "", _tinP = "";
        private string _ctN = "", _ctH = "", _ctP = "";
        private string _wantH = "", _wantP = "";
        private string _rayTopBL = "", _rayTopTL = "";
        private bool _highOk, _pressOk;
        private int _sweepRows, _sweepHover, _sweepPress, _sweepNoHover, _sweepNoPress;
        private string _capPending = "";
        private bool _capBusy, _capDone, _capOk;
        private string _capInfo = "";
        private int _capX, _capY, _capW, _capH;

        // repave watch
        private object _pvRef;
        private int _pvPool = -1, _pvNodes = -1, _pvShown = -1;
        private int _repaveRows, _repaveLoading, _chunkFrames;
        private readonly List<string> _repaveOutside = new List<string>();
        private string _lastArea = "";

        // log ring (decisive: shows whether a repave is inside a loading bracket)
        private static int _mainThread = -1;
        private static readonly List<string> _ring = new List<string>();

        // misc
        private bool _moorRequested, _died, _goldSeeded, _loadSwept, _openLogged;
        private int _openFrom = -1;
        private int _goldBefore = -1, _goldAtDeath = -1, _goldAfterDeath = -1;

        private class Widget
        {
            public RectTransform Rt;
            public Component Comp;
            public string Kind;
            public string Path;
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
                if (s.Length > 260) s = s.Substring(0, 260);
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
                    _cur = nm; _at = Time.unscaledTime; _waitFrom = -1; _openLogged = false;
                    Drive.Log("PHASE " + nm + " fsm=" + Drive.Fsm() + " panels=" + Drive.Panels());
                }
                return f();
            });
        }
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private static bool FsmEq(string s) { return Drive.Fsm() == s; }
        private bool WaitFrames(int n)
        {
            if (_waitFrom < 0) { _waitFrom = Time.frameCount; return false; }
            return Time.frameCount - _waitFrom >= n;
        }

        private void Update()
        {
            try { WatchRepave(); } catch (Exception e) { Drive.Warn("REPAVE-WATCH-EX " + e.GetType().Name); }
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

        // ================================================================ repave watch
        // A whole-map repave (RebuildLayers = return every chunk to the pool + rebuild every
        // chunk this frame) shows up as one frame whose pool activity jumps by ~nodeCount
        // (town 2824 / moor 2698).  One incremental chunk build (MaxChunksPerFrame = 1) moves
        // ~293.  The cold build shows up as a node-count jump (0 -> nodeCount).
        private void WatchRepave()
        {
            var mv = Drive.MapViewObj();
            if (mv == null) { _pvRef = null; _pvPool = -1; _pvNodes = -1; _pvShown = -1; return; }
            var g = Drive.MapViewGo();
            var nodes = g != null ? Drive.NodeCount(g.transform) : -1;
            if (!ReferenceEquals(mv, _pvRef)) { _pvRef = mv; _pvPool = -1; _pvNodes = 0; _pvShown = -1; return; }   // _pvNodes = 0 => the cold build shows up as nodeDelta
            var c = Drive.IntProp(mv, "PoolCreatedCount");
            var r = Drive.IntProp(mv, "PoolReusedCount");
            var shown = Drive.IntProp(mv, "BuiltChunkCount");
            var pool = (c >= 0 && r >= 0) ? (c + r) : -1;
            var poolDelta = (_pvPool >= 0 && pool >= 0) ? (pool - _pvPool) : 0;
            var nodeDelta = (_pvNodes >= 0 && nodes >= 0) ? (nodes - _pvNodes) : 0;
            var hit = poolDelta >= 1000 || nodeDelta >= 1000;
            if (!hit && poolDelta >= 300) _chunkFrames++;
            if (hit)
            {
                var loading = Drive.LoadingVisible();
                _repaveRows++;
                if (loading) _repaveLoading++;
                var area = Drive.Map() != null ? Drive.Map().Area.ToString() : "-";
                var line = "t=" + Time.unscaledTime.ToString("0.00")
                    + " wall=" + DateTime.Now.ToString("HH:mm:ss.fff")
                    + " frame=" + Time.frameCount
                    + " area=" + area + " fsm=" + Drive.Fsm() + " scene=" + Drive.SceneName()
                    + " panels=" + Drive.Panels() + " uiLoading=" + (Drive.UiLoading() ? 1 : 0)
                    + " lpOpen=" + (Drive.LpOpen() ? 1 : 0) + " timeScale=" + Time.timeScale.ToString("0.##")
                    + " poolDelta=" + poolDelta + " nodeDelta=" + nodeDelta + " nodes=" + nodes
                    + " chunksBefore=" + _pvShown + " chunksAfter=" + shown
                    + " inLoading=" + (loading ? 1 : 0);
                Drive.KV("REPAVE", line);
                if (!loading) _repaveOutside.Add(line);
                // DECISIVE: dump the engine log lines (with frame numbers) seen just before
                // this repave -- shows whether ShowLoading/HideLoading bracket it.
                var win = Drive.RawDir + "/repave_window_" + _repaveRows + ".txt";
                Drive.WriteFile(win, "REPAVE " + line + "\n---- last " + _ring.Count + " engine log lines ----\n"
                    + string.Join("\n", _ring.ToArray()) + "\n");
                Drive.KV("LOGWINDOW-FILE", "n=" + _repaveRows + " file=" + win + " lines=" + _ring.Count);
            }
            if (pool >= 0) _pvPool = pool;
            if (nodes >= 0) _pvNodes = nodes;
            _pvShown = shown;

            var m = Drive.Map();
            if (m != null)
            {
                var a = m.Area.ToString();
                if (a != _lastArea)
                {
                    _lastArea = a;
                    Drive.KV("AREA", "area=" + a + " fsm=" + Drive.Fsm() + " panels=" + Drive.Panels()
                        + " uiLoading=" + (Drive.UiLoading() ? 1 : 0) + " t=" + Time.unscaledTime.ToString("0.00"));
                }
            }
        }

        // ================================================================ sweep
        private void SweepBegin(string panelName, Transform root)
        {
            _panelName = panelName;
            _widgets.Clear();
            _wi = 0; _stage = 0;
            _sweepRows = _sweepHover = _sweepPress = _sweepNoHover = _sweepNoPress = 0;
            if (root == null) { Drive.Warn("SWEEP no root panel=" + panelName); return; }
            var seen = new List<Component>();
            var sels = root.GetComponentsInChildren<Selectable>(true);
            for (var i = 0; i < sels.Length; i++)
            {
                var s = sels[i];
                if (s == null || !s.isActiveAndEnabled) continue;
                var tg = s.targetGraphic;
                var rt = tg != null ? tg.rectTransform : (s.transform as RectTransform);
                if (rt == null) continue;
                if (seen.Contains((Component)rt)) continue;
                seen.Add((Component)rt);
                _widgets.Add(new Widget { Rt = rt, Comp = tg != null ? (Component)tg : (Component)s, Kind = "Selectable", Path = Drive.NodePath(rt) });
            }
            var hv = root.GetComponentsInChildren<Diablo2.UI.HoverTarget>(true);
            for (var i = 0; i < hv.Length; i++)
            {
                var h = hv[i];
                if (h == null || !h.isActiveAndEnabled) continue;
                var rt = h.transform as RectTransform;
                if (rt == null) continue;
                if (seen.Contains((Component)rt)) continue;
                seen.Add((Component)rt);
                _widgets.Add(new Widget { Rt = rt, Comp = h, Kind = "HoverTarget", Path = Drive.NodePath(rt) });
            }
            Drive.KV("PANEL-SWEEP-BEGIN", "panel=" + panelName + " widgets=" + _widgets.Count);
        }

        private bool SweepGeneric(string panelName)
        {
            if (!_sweepStarted || _panelName != panelName)
            {
                SweepBegin(panelName, PanelRoot(panelName));
                _sweepStarted = true;
            }
            return SweepStep();
        }

        private bool SweepStep()
        {
            if (_widgets.Count == 0 || _wi >= _widgets.Count)
            {
                SweepEnd();
                _sweepStarted = false;
                return true;
            }
            var w = _widgets[_wi];
            switch (_stage)
            {
                case 0:
                    Drive.MouseState(new Vector2(2f, 2f), false);
                    _stage = 1; _stageFrame = Time.frameCount; return false;
                case 1:
                    if (Time.frameCount - _stageFrame < 2) return false;
                    _panelKeyN = Drive.SpriteKey(PanelRoot());
                    _sprN = Drive.SpriteOf(w.Comp);
                    _tinN = Drive.TintOf(w.Comp);
                    _ctN = Drive.CanvasTintOf(w.Comp);
                    if (!CaptureNow(w.Rt, Tile(_globalCell, "n"))) return false;
                    _stage = 2; return false;
                case 2:
                    Drive.MouseState(Drive.PickInjectPoint(w.Rt), false);
                    _stage = 3; _stageFrame = Time.frameCount; return false;
                case 3:
                    if (Time.frameCount - _stageFrame < 2) return false;
                    _panelKeyH = Drive.SpriteKey(PanelRoot());
                    _sprH = Drive.SpriteOf(w.Comp);
                    _tinH = Drive.TintOf(w.Comp);
                    _ctH = Drive.CanvasTintOf(w.Comp);
                    _wantH = Drive.WantSprite(w.Comp, 0);
                    _wantP = Drive.WantSprite(w.Comp, 1);
                    _highOk = _wantH != "(null)" && _wantH != "(no-selectable)" && _sprH == _wantH;
                    {
                        Vector2 lo, hi, cc;
                        Drive.ScreenRect(w.Rt, out lo, out hi, out cc);
                        var p = Drive.PickInjectPoint(w.Rt);
                        _rayTopBL = Drive.RayTop(p);
                        _rayTopTL = Drive.RayTop(new Vector2(p.x, Screen.height - p.y));
                    }
                    if (!CaptureNow(w.Rt, Tile(_globalCell, "h"))) return false;
                    _stage = 4; return false;
                case 4:
                    Drive.MouseState(Drive.PickInjectPoint(w.Rt), true);
                    _stage = 5; _stageFrame = Time.frameCount; return false;
                case 5:
                    if (Time.frameCount - _stageFrame < 2) return false;
                    _panelKeyP = Drive.SpriteKey(PanelRoot());
                    _sprP = Drive.SpriteOf(w.Comp);
                    _tinP = Drive.TintOf(w.Comp);
                    _ctP = Drive.CanvasTintOf(w.Comp);
                    _pressOk = _wantP != "(null)" && _wantP != "(no-selectable)" && _sprP == _wantP;
                    if (!CaptureNow(w.Rt, Tile(_globalCell, "p"))) return false;
                    _stage = 6; return false;
                case 6:
                    // release FAR AWAY so uGUI's click (fires on pointer-up over the same object)
                    // never runs -- we want the pressed LOOK, not the action.
                    Drive.MouseState(new Vector2(2f, 2f), true);
                    _stage = 7; _stageFrame = Time.frameCount; return false;
                case 7:
                    if (Time.frameCount - _stageFrame < 1) return false;
                    Drive.MouseState(new Vector2(2f, 2f), false);
                    _stage = 8; _stageFrame = Time.frameCount; return false;
                case 8:
                    if (Time.frameCount - _stageFrame < 1) return false;
                    {
                        var hoverChanged = (_sprH != _sprN) || (_panelKeyH != _panelKeyN) || (_tinH != _tinN) || (_ctH != _ctN);
                        var pressChanged = (_sprP != _sprH) || (_panelKeyP != _panelKeyH) || (_tinP != _tinH) || (_ctP != _ctH);
                        Vector2 lo, hi, c;
                        Drive.ScreenRect(w.Rt, out lo, out hi, out c);
                        Drive.KV("HOVER", "panel=" + _panelName + " idx=" + (_wi + 1) + "/" + _widgets.Count
                            + " cell=g" + _globalCell + " widget=" + Drive.Esc(w.Path) + " kind=" + w.Kind
                            + " rect=" + lo.x.ToString("0") + "," + lo.y.ToString("0") + "," + hi.x.ToString("0") + "," + hi.y.ToString("0")
                            + " sprN=" + _sprN + " sprH=" + _sprH + " sprP=" + _sprP
                            + " wantH=" + _wantH + " wantP=" + _wantP
                            + " trans=" + Drive.TransitionOf(w.Comp) + " ray=" + Drive.RayTargetOf(w.Comp)
                            + " tintN=" + _tinN + " tintH=" + _tinH + " tintP=" + _tinP
                            + " ctintN=" + _ctN + " ctintH=" + _ctH + " ctintP=" + _ctP
                            + " colWantH=" + Drive.ColorWantSprite(w.Comp, 0) + " colWantP=" + Drive.ColorWantSprite(w.Comp, 1)
                            + " rayTopBL=" + _rayTopBL + " rayTopTL=" + _rayTopTL
                            + " highOk=" + (_highOk ? 1 : 0) + " pressOk=" + (_pressOk ? 1 : 0)
                            + " hoverChanged=" + (hoverChanged ? 1 : 0) + " pressChanged=" + (pressChanged ? 1 : 0)
                            + " panelSprChanged=" + ((_panelKeyH != _panelKeyN || _panelKeyP != _panelKeyH) ? 1 : 0));
                        _sweepRows++;
                        if (hoverChanged) _sweepHover++; else _sweepNoHover++;
                        if (pressChanged || _pressOk) _sweepPress++; else _sweepNoPress++;
                        _globalCell++; _wi++; _stage = 0;
                    }
                    return false;
            }
            return false;
        }

        private void SweepEnd()
        {
            Drive.KV("HOVER-SUMMARY", "panel=" + _panelName + " widgets=" + _widgets.Count
                + " rows=" + _sweepRows + " hoverChanged=" + _sweepHover + " pressChanged=" + _sweepPress
                + " noHover=" + _sweepNoHover + " noPress=" + _sweepNoPress);
            Drive.KV("PANEL-SWEEP-END", "panel=" + _panelName + " rows=" + _sweepRows);
        }

        private string Tile(int cell, string state)
        {
            return Drive.RawDir + "/g" + cell + "_" + state + ".png";
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

        // ================================================================ panel roots
        private Transform PanelRoot(string name)
        {
            if (Game.UI == null) return null;
            Component c = null;
            if (name == "MainMenuPanel") c = Game.UI.Get<Diablo2.UI.MainMenuPanel>();
            else if (name == "CharSelectPanel") c = Game.UI.Get<Diablo2.UI.CharSelectPanel>();
            else if (name == "CharCreatePanel") c = Game.UI.Get<Diablo2.UI.CharCreatePanel>();
            else if (name == "HudPanel") c = Game.UI.Get<Diablo2.UI.HudPanel>();
            else if (name == "PausePanel") c = Game.UI.Get<Diablo2.UI.PausePanel>();
            else if (name == "SettingsPanel") c = Game.UI.Get<Diablo2.UI.SettingsPanel>();
            else if (name == "DeathPanel") c = Game.UI.Get<Diablo2.UI.DeathPanel>();
            else if (name == "InventoryPanel") c = Game.UI.Get<Diablo2.UI.InventoryPanel>();
            else if (name == "CharacterPanel") c = Game.UI.Get<Diablo2.UI.CharacterPanel>();
            else if (name == "SkillTreePanel") c = Game.UI.Get<Diablo2.UI.SkillTreePanel>();
            else if (name == "MiniMapPanel") c = Game.UI.Get<Diablo2.UI.MiniMapPanel>();
            else if (name == "QuestLogPanel") c = Game.UI.Get<Diablo2.UI.QuestLogPanel>();
            else if (name == "NpcDialogPanel") c = Game.UI.Get<Diablo2.UI.NpcDialogPanel>();
            else if (name == "ShopPanel") c = Game.UI.Get<Diablo2.UI.ShopPanel>();
            else if (name == "D2ConfirmPanel") c = Game.UI.Get<Diablo2.UI.D2ConfirmPanel>();
            else if (name == "BootPanel") c = Game.UI.Get<Diablo2.UI.BootPanel>();
            else if (name == "LoadingPanel") c = Game.UI.Get<Diablo2.UI.LoadingPanel>();
            return c != null ? c.transform : null;
        }
        private Transform PanelRoot() { return PanelRoot(_panelName); }
        private bool IsOpen(string name) { return PanelRoot(name) != null; }

        private void LogOpen(string name)
        {
            if (_openLogged) return;
            _openLogged = true;
            Drive.KV("PANEL-OPEN", "panel=" + name + " panels=" + Drive.Panels() + " fsm=" + Drive.Fsm());
        }

        private bool OpenPanel(string name)
        {
            if (IsOpen(name)) { LogOpen(name); return true; }
            if (Game.UI == null) { Drive.Warn("OPEN no UI " + name); return true; }
            if (_openFrom < 0) { _openFrom = Time.frameCount; }
            if (Time.frameCount - _openFrom == 2 || (Time.frameCount - _openFrom) % 30 == 0)
            {
                if (name == "InventoryPanel") Game.UI.Open<Diablo2.UI.InventoryPanel>();
                else if (name == "CharacterPanel") Game.UI.Open<Diablo2.UI.CharacterPanel>();
                else if (name == "SkillTreePanel") Game.UI.Open<Diablo2.UI.SkillTreePanel>();
                else if (name == "QuestLogPanel") Game.UI.Open<Diablo2.UI.QuestLogPanel>();
                else if (name == "MiniMapPanel") Game.UI.Open<Diablo2.UI.MiniMapPanel>();
                else if (name == "NpcDialogPanel") Game.UI.Open<Diablo2.UI.NpcDialogPanel>();
                else if (name == "ShopPanel") Game.UI.Open<Diablo2.UI.ShopPanel>();
                else if (name == "SettingsPanel") Game.UI.Open<Diablo2.UI.SettingsPanel>();
            }
            if (Time.frameCount - _openFrom > 300) { Drive.Warn("OPEN timeout " + name + " panels=" + Drive.Panels()); return true; }
            return false;
        }
        private void ClosePanel(string name)
        {
            if (Game.UI == null) return;
            if (name == "InventoryPanel") Game.UI.Close<Diablo2.UI.InventoryPanel>();
            else if (name == "CharacterPanel") Game.UI.Close<Diablo2.UI.CharacterPanel>();
            else if (name == "SkillTreePanel") Game.UI.Close<Diablo2.UI.SkillTreePanel>();
            else if (name == "QuestLogPanel") Game.UI.Close<Diablo2.UI.QuestLogPanel>();
            else if (name == "MiniMapPanel") Game.UI.Close<Diablo2.UI.MiniMapPanel>();
            else if (name == "NpcDialogPanel") Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
            else if (name == "ShopPanel") Game.UI.Close<Diablo2.UI.ShopPanel>();
            else if (name == "SettingsPanel") Game.UI.Close<Diablo2.UI.SettingsPanel>();
            else if (name == "D2ConfirmPanel") Game.UI.Close<Diablo2.UI.D2ConfirmPanel>();
        }
        private bool OpenThenSweep(string name)
        {
            if (!OpenPanel(name)) return false;
            _openFrom = -1;
            return SweepGeneric(name);
        }
        private bool ClosePanelStation(string name)
        {
            ClosePanel(name);
            Drive.KV("PANEL-CLOSE", "panel=" + name + " panels=" + Drive.Panels());
            return true;
        }

        // ================================================================ the plan
        private void BuildPlan()
        {
            Add("wait60", () => WaitFrames(60));
            Add("boot-sweep", () => SweepGeneric("BootPanel"));

            Add("boot", () =>
            {
                if (FsmEq("MainMenu")) return true;
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(25f)) { Drive.Warn("boot timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // the Menu is a SCENE + panel; FSM reaches MainMenu before the panel is up
            Add("menu-wait", () =>
            {
                if (IsOpen("MainMenuPanel")) return true;
                if (Elapsed(25f)) { Drive.Warn("menu-wait timeout fsm=" + Drive.Fsm() + " panels=" + Drive.Panels()); return true; }
                return false;
            });

            Add("calib", () =>
            {
                if (!_calibInit)
                {
                    var root = PanelRoot("MainMenuPanel");
                    if (root == null) { Drive.Warn("CALIB no MainMenu root"); _calibInit = true; return true; }
                    var sels = root.GetComponentsInChildren<Selectable>(true);
                    if (sels.Length == 0) { Drive.Warn("CALIB no Selectable"); _calibInit = true; return true; }
                    _calibTarget = sels[0];
                    _calibBase = Drive.SpriteOf(_calibTarget);
                    _calibHigh = Drive.WantSprite(_calibTarget, 0);
                    Drive.KV("CALIB-BEGIN", "target=" + Drive.Esc(Drive.NodePath(_calibTarget.transform))
                        + " sprN=" + _calibBase + " wantH=" + _calibHigh);
                    _calibInit = true; return false;
                }
                if (_stage == 0)
                {
                    Drive.Flip = _calibFlip;
                    Drive.MouseState(Drive.PickInjectPoint(_calibTarget.transform as RectTransform), false);
                    _stage = 1; _stageFrame = Time.frameCount; return false;
                }
                if (_stage == 1) { if (Time.frameCount - _stageFrame < 3) return false; _stage = 2; return false; }
                if (_stage == 2)
                {
                    var now = Drive.SpriteOf(_calibTarget);
                    Drive.KV("CALIB-TRY", "flip=" + (_calibFlip ? 1 : 0) + " spr=" + now + " wantH=" + _calibHigh
                        + " ok=" + (now == _calibHigh ? 1 : 0));
                    if (now == _calibHigh) { Drive.KV("CALIB-OK", "flip=" + (_calibFlip ? 1 : 0)); Drive.Flip = _calibFlip; return true; }
                    if (!_calibFlipTried) { _calibFlipTried = true; _calibFlip = !_calibFlip; _stage = 0; return false; }
                    Drive.Warn("CALIB-FAIL: neither flip made the hover swap the sprite");
                    return true;
                }
                return false;
            });
            Add("calib-park", () => { Drive.MouseState(new Vector2(2f, 2f), false); return true; });

            Add("menu-sweep", () => SweepGeneric("MainMenuPanel"));

            Add("charselect", () =>
            {
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(20f)) { Drive.Warn("charselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("cs-sweep", () => SweepGeneric("CharSelectPanel"));

            Add("charcreate", () =>
            {
                if (FsmEq("CharCreate")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
                if (Elapsed(20f)) { Drive.Warn("charcreate timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("cc-sweep", () => SweepGeneric("CharCreatePanel"));

            Add("backselect", () =>
            {
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                if (Elapsed(20f)) { Drive.Warn("backselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("loading", () =>
            {
                if (FsmEq("Stage")) return true;
                if (!_loadSwept && IsOpen("LoadingPanel"))
                {
                    if (SweepGeneric("LoadingPanel")) _loadSwept = true;
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
                    if (Elapsed(30f)) { Drive.Warn("stage wait timeout fsm=" + Drive.Fsm()); return true; }
                    return false;
                }
                if (!Elapsed(3.0f)) return false;
                Drive.KV("STAGE-READY", "fsm=" + Drive.Fsm() + " panels=" + Drive.Panels()
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " gold=" + (Drive.Player() != null ? Drive.Player().Gold : -1)
                    + " repaveSoFar=" + _repaveRows + " inLoading=" + _repaveLoading
                    + " " + Drive.DeviceLine());
                return true;
            });

            Add("hud-sweep", () => SweepGeneric("HudPanel"));

            // the 7 mini-panel buttons + the toggle are inactive by ORIGINAL-D2 default
            // (ImageMinipanel m_IsActive=0).  Expand on purpose so they can be measured,
            // then collapse again -- the swept state is recorded in the log.
            Add("hud-mini", () =>
            {
                var hud = Game.UI != null ? Game.UI.Get<Diablo2.UI.HudPanel>() : null;
                if (hud == null) { Drive.Warn("hud-mini no HudPanel"); return true; }
                if (!_miniOpen)
                {
                    _miniOpen = true;
                    hud.SetMiniPanelOpen(true);
                    Drive.KV("HUD-MINI", "SetMiniPanelOpen(true) -- original default is collapsed; expanded for measurement");
                    return false;
                }
                if (WaitFrames(3)) return true;
                return false;
            });
            Add("hud-mini-sweep", () => SweepGeneric("HudPanel"));
            Add("hud-mini-close", () =>
            {
                var hud = Game.UI != null ? Game.UI.Get<Diablo2.UI.HudPanel>() : null;
                if (hud != null) hud.SetMiniPanelOpen(false);
                return true;
            });

            Add("inv", () => OpenThenSweep("InventoryPanel"));
            Add("inv-close", () => ClosePanelStation("InventoryPanel"));
            Add("chr", () => OpenThenSweep("CharacterPanel"));
            Add("chr-close", () => ClosePanelStation("CharacterPanel"));
            Add("skl", () => OpenThenSweep("SkillTreePanel"));
            Add("skl-close", () => ClosePanelStation("SkillTreePanel"));
            Add("qst", () => OpenThenSweep("QuestLogPanel"));
            Add("qst-close", () => ClosePanelStation("QuestLogPanel"));
            Add("mm", () => OpenThenSweep("MiniMapPanel"));
            Add("mm-close", () => ClosePanelStation("MiniMapPanel"));
            Add("dlg", () => OpenThenSweep("NpcDialogPanel"));
            Add("dlg-close", () => ClosePanelStation("NpcDialogPanel"));
            Add("shop", () => OpenThenSweep("ShopPanel"));
            Add("shop-close", () => ClosePanelStation("ShopPanel"));

            Add("confirm-open", () =>
            {
                if (Game.UI == null) return true;
                if (Game.UI.IsOpen<Diablo2.UI.D2ConfirmPanel>()) return true;
                if (Elapsed(0.3f)) Diablo2.UI.D2ConfirmPanel.Show("t0e", "t0e", () => { }, () => { });
                if (Elapsed(8f)) { Drive.Warn("confirm timeout panels=" + Drive.Panels()); return true; }
                return Game.UI.IsOpen<Diablo2.UI.D2ConfirmPanel>();
            });
            Add("confirm-sweep", () => SweepGeneric("D2ConfirmPanel"));
            Add("confirm-close", () => ClosePanelStation("D2ConfirmPanel"));

            Add("pause", () =>
            {
                if (FsmEq("Pause")) return true;
                if (Elapsed(0.6f)) Game.Event.Emit(Events.PauseRequest);
                if (Elapsed(15f)) { Drive.Warn("pause timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("pause-sweep", () => SweepGeneric("PausePanel"));

            Add("settings", () => OpenPanel("SettingsPanel"));
            Add("settings-sweep", () => SweepGeneric("SettingsPanel"));
            Add("settings-close", () => ClosePanelStation("SettingsPanel"));

            Add("resume", () =>
            {
                Game.UI.Close<Diablo2.UI.PausePanel>();
                if (FsmEq("Stage")) return true;
                if (Elapsed(0.4f)) Game.Event.Emit(Events.ResumeRequest);
                if (Elapsed(15f)) { Drive.Warn("resume timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("to-moor", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor) return true;
                if (!_moorRequested && Elapsed(0.5f)) { _moorRequested = true; Drive.KV("MOOR-ENTER", "emit ExitEntered(BloodMoor)"); Game.Event.Emit<AreaId>(Events.ExitEntered, AreaId.BloodMoor); }
                if (Elapsed(30f)) { Drive.Warn("to-moor timeout area=" + (m != null ? m.Area.ToString() : "-")); return true; }
                return false;
            });
            Add("moor-settle", () =>
            {
                if (!WaitFrames(120) && !Elapsed(4f)) return false;
                Drive.KV("MOOR-READY", "area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-") + " panels=" + Drive.Panels());
                return true;
            });

            Add("gold-setup", () =>
            {
                var p = Drive.Player();
                if (p == null) { Drive.Warn("GOLD no player"); return true; }
                _goldBefore = p.Gold;
                if (_goldBefore < 100)
                {
                    _goldSeeded = true;
                    var add = 1234 - _goldBefore;
                    Drive.KV("GOLD-SEED", "gold=" + _goldBefore + " AddGold(+" + add + ") (seeded so the 10% penalty is observable)");
                    p.AddGold(add);
                    _goldBefore = p.Gold;
                }
                Drive.KV("GOLD-BEFORE", "gold=" + _goldBefore);
                return true;
            });
            Add("die", () =>
            {
                var p = Drive.Player();
                if (p == null) return true;
                if (!_died) { _died = true; _goldAtDeath = p.Gold; Drive.KV("DEATH-TRIGGER", "goldAtDeath=" + _goldAtDeath + " hp=" + p.Life); p.Kill(); return false; }
                if (Elapsed(3f)) return true;
                return false;
            });
            Add("death-read", () =>
            {
                var p = Drive.Player();
                if (p != null) _goldAfterDeath = p.Gold;
                var open = Game.UI != null && Game.UI.IsOpen<Diablo2.UI.DeathPanel>();
                var expected = _goldAtDeath / 10;
                Drive.KV("GOLD", "goldBefore=" + _goldBefore + " seeded=" + (_goldSeeded ? 1 : 0)
                    + " goldAtDeath=" + _goldAtDeath + " goldAfterDeath=" + _goldAfterDeath
                    + " lost=" + (_goldAtDeath - _goldAfterDeath) + " expected=" + expected
                    + " floorOk=" + ((_goldAtDeath - _goldAfterDeath) == expected ? 1 : 0)
                    + " nonNegOk=" + (_goldAfterDeath >= 0 ? 1 : 0)
                    + " dead=" + (p != null && p.IsDead ? 1 : 0));
                Drive.KV("DEATH-SCREEN", "open=" + (open ? 1 : 0) + " panels=" + Drive.Panels()
                    + " fsm=" + Drive.Fsm() + " gold=" + _goldAfterDeath);
                return true;
            });
            Add("death-sweep", () => SweepGeneric("DeathPanel"));
            Add("death-close", () =>
            {
                ClosePanel("DeathPanel");
                var p = Drive.Player();
                if (p != null && p.IsDead) p.Revive();
                return true;
            });

            Add("finish", () =>
            {
                Drive.KV("REPAVE-SUMMARY", "rows=" + _repaveRows + " inLoading=" + _repaveLoading
                    + " outsideLoading=" + _repaveOutside.Count
                    + " incrementalChunkFrames=" + _chunkFrames
                    + " (threshold: repave = poolDelta>=1000 OR nodeDelta>=1000)");
                for (var i = 0; i < _repaveOutside.Count && i < 20; i++) Drive.KV("REPAVE-OUTSIDE", _repaveOutside[i]);
                return true;
            });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private bool _miniOpen;

        private void EnterStage()
        {
            var saveName = "";
            var save = Drive.CtxMember("Save") as Diablo2.Module.ISaveModule;
            if (save != null)
            {
                var list = save.List();
                if (list != null && list.Count > 0) saveName = list[0];
            }
            if (string.IsNullOrEmpty(saveName)) { Drive.Warn("EnterStage: no save found; using X162012"); saveName = "X162012"; }
            Drive.KV("ENTER-STAGE", "save=\"" + saveName + "\"");
            Game.Event.Emit<string>(Events.CharSelectRequest, saveName);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Drive.KV("FINISH", "why=" + why + " pc=" + _pc + " fsm=" + Drive.Fsm()
                + " repaveRows=" + _repaveRows + " inLoading=" + _repaveLoading
                + " outside=" + _repaveOutside.Count + " goldBefore=" + _goldBefore
                + " goldAfterDeath=" + _goldAfterDeath + " " + Drive.DeviceLine());
            Drive.WriteFile(Drive.DonePath, "T0E-DONE why=" + why + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
