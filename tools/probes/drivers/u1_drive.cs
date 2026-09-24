// =============================================================================
// u1_drive.cs -- ONE Play session that produces the U-1 runtime verdict
//   (direction-key family does not move / W only swaps the weapon group / a real
//    ground click really walks the character).
//
// WHY THIS FILE EXISTS (promotion of a deleted one-off probe):
//   was produced by a one-off probe ".ai-tmp/test/u1_probe.cs" that was deleted
//   afterwards, so the live chain could not be re-run from the repo.  Per
//   clover-engine skill 3.5 a judging asset (probe / driver / measuring script)
//   must live in tools/probes/ and be committed.  This file is that driver; the
//   chain, the log markers and the THREE judged assertions are exactly the ones
//   recorded in the frozen first evidence file (they are NOT re-designed here).
//
// CHAIN (identical to the frozen evidence, one Play session):
//   Boot(Injection Space) -> MainMenu("Single") -> CharSelect("Create")
//   -> CharCreate("Back") -> CharSelect -> Events.CharSelectRequest(<save>)
//   -> Loading -> Stage, then
//     (1) hold the direction-key family W+A+S+D for 1.5s  => 0 displacement
//     (2) press W                                           => swap weapon group, 0 displacement
//     (3) real mouse click on a ground cell                 => the character walks there
//
// ENVIRONMENT SELF-CHECK (skill 4.5): the driver logs the render device name,
// graphics device type, resolution, frame pacing, the module wiring and the Unity
// version, and it ABORTS (writes a done marker that says INVALID) when the render
// device is a software raster (Microsoft Basic Render Driver / WARP) -- on such a
// device every frame-time / pacing verdict is void.  recompile_status and the
// console error count are CLI-level, so u1_run.ps1 logs those two into the same
// frozen evidence header.
//
// NUMERIC CLASS: the judged rows are runtime logs + assertions, so this driver
// captures NO tiles (skill 4.9: numeric rows are not screenshot rows).
//
// Input is injected as REAL InputSystem events (QueueStateEvent) so the whole
// chain runs: InputSystem -> InputSystemUIInputModule(uGUI) / CloverInput ->
// InputReader -> Player/Item.  The ground click additionally verifies the
// screen<->grid round trip before it clicks (a missed projection would make the
// click land on the wrong cell and the assertion would be meaningless).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace U1
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Probe
    {
        internal const string Tag = "U1";

        private static string _done = string.Empty;

        internal static string DonePath { get { return _done; } }

        private static readonly List<string> Checks = new List<string>();

        // ---- logging -----------------------------------------------------------------------
        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Info(Tag, msg);
            else UnityEngine.Debug.Log("[" + Tag + "] " + msg);
        }

        internal static void Warn(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }

        internal static void KV(string key, string value) { Log(key + "=" + value); }

        internal static void Check(string name, bool ok, string detail)
        {
            Checks.Add(name + "=" + (ok ? 1 : 0));
            Log("CHK name=" + name + " ok=" + (ok ? 1 : 0) + " " + detail);
        }

        internal static int ChecksOk()
        {
            var n = 0;
            foreach (var c in Checks) { if (c.EndsWith("=1")) n++; }
            return n;
        }

        internal static int ChecksBad() { return Checks.Count - ChecksOk(); }

        internal static bool AllChecksOk() { return Checks.Count > 0 && ChecksBad() == 0; }

        // ---- files -------------------------------------------------------------------------
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) { Warn("MARKER path empty"); return; }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
            }
            catch (Exception ex) { Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- reflection --------------------------------------------------------------------
        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name);
                if (t != null) return t;
            }
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
            var ctx = Ctx();
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) : null;
        }

        internal static Diablo2.Module.IPlayerModule Player() { return CtxMember("Player") as Diablo2.Module.IPlayerModule; }
        internal static Diablo2.Module.IMapModule Map() { return CtxMember("Map") as Diablo2.Module.IMapModule; }

        /// <summary>Module wiring snapshot (log only; a null module is not a failure by itself).</summary>
        internal static string ModuleLine()
        {
            var p = CtxMember("Player");
            var m = CtxMember("Map");
            return "Map=" + (m != null ? 1 : 0) + " Player=" + (p != null ? 1 : 0);
        }

        internal static Vector2Int GridOf(Diablo2.Module.IPlayerModule p)
        {
            return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);
        }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string V(Vector2 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")"; }

        internal static int Chebyshev(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        internal static string DeviceName() { return SystemInfo.graphicsDeviceName; }

        /// <summary>Software rasterisation (every pacing / frame-time verdict on it is void).</summary>
        internal static bool SoftwareRaster(string device)
        {
            var d = device ?? string.Empty;
            return d.IndexOf("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("SwiftShader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- editor / play setup (skill 4.5 environment self-check) -------------------------
        internal static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            var kb = Keyboard.current;
            if (kb == null) kb = InputSystem.AddDevice<Keyboard>();
            if (UnityEngine.InputSystem.Mouse.current == null) InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();

            Log("CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate
                + " focused=" + (Application.isFocused ? 1 : 0)
                + " keyboard=" + (kb != null ? kb.name : "(null)")
                + " mouse=" + (UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.name : "(null)")
                + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)"));
            Log("DEVICE device=\"" + DeviceName() + "\" res=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate);
            Log("MODULES=" + ModuleLine());
            return "CFG-OK";
        }

        // ---- keyboard ----------------------------------------------------------------------
        internal static bool TryParseKey(string name, out Key key)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "w": key = Key.W; return true;
                case "a": key = Key.A; return true;
                case "s": key = Key.S; return true;
                case "d": key = Key.D; return true;
                case "space": key = Key.Space; return true;
                case "escape": key = Key.Escape; return true;
                case "enter": key = Key.Enter; return true;
                default: key = Key.None; return false;
            }
        }

        private static Keyboard KeyboardDev()
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }

        /// <summary>Press several keys at once (the direction-key family is held simultaneously).</summary>
        internal static void KeysDown(string[] names)
        {
            var kb = KeyboardDev();
            if (kb == null) { Warn("KEYSDOWN no-keyboard"); return; }
            var keys = new List<Key>();
            foreach (var n in names)
            {
                Key k;
                if (TryParseKey(n, out k)) keys.Add(k);
                else Warn("KEYSDOWN unknown-key=" + n);
            }
            if (keys.Count == 0) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys.ToArray()));
            Log("KEYSDOWN keys=" + string.Join("+", names) + " keyboard=" + kb.name);
        }

        internal static void KeyDown(string name) { KeysDown(new[] { name }); }

        internal static void KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
        }

        // ---- mouse -------------------------------------------------------------------------
        internal static UnityEngine.InputSystem.Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null)
            {
                m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                Warn("mouse device missing -> added");
            }
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

        internal static Vector3 MousePosNow()
        {
            var input = Game.Input;
            return input != null ? input.MousePosition : Vector3.zero;
        }

        // ---- UI ----------------------------------------------------------------------------
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

        internal static Button FindButton(string name, out string diag)
        {
            diag = string.Empty;
            var want = name ?? string.Empty;
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            Button pick = null;
            var n = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                n++;
                if (pick == null) pick = b;
            }
            diag = "buttons=" + all.Length + " candidates=" + n;
            return pick;
        }

        internal static string ButtonLabel(Button b)
        {
            if (b == null) return "-";
            var t = b.GetComponentInChildren<Text>(true);
            return t != null ? t.text : "-";
        }

        internal static string RaycastTop(Vector2 screenPos)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current);
            ped.position = screenPos;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            var g = hits[0].gameObject;
            return g == null ? "(null)" : g.name;
        }

        /// <summary>Legacy click path (ExecuteEvents). Used only as a retry when the real mouse misses.</summary>
        internal static string Click(string name)
        {
            if (EventSystem.current == null) { Warn("CLICK-LEGACY no Eventsystem name=" + name); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(name, out diag);
            if (pick == null) { Warn("CLICK-LEGACY miss name=" + name + " " + diag); return "ERR-miss"; }
            if (!pick.interactable) { Warn("CLICK-LEGACY skip name=" + name + " (not interactable)"); return "ERR-not-interactable"; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            ExecuteEvents.Execute(pick.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-LEGACY name=" + name + " label=\"" + ButtonLabel(pick) + "\" " + diag);
            return "CLICKED";
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Cfg() { return Probe.Cfg(); }
        public static string Paths(string spec) { Probe.Paths(spec); return "PATHS-OK"; }
        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0); }
    }

    /// <summary>Installer. spec = "&lt;tag&gt;|&lt;save name&gt;|&lt;done marker path&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("U1EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One Play session: environment self-check -> the menu chain -> the three U-1 assertions.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private const float HoldSeconds = 1.5f;      // the direction-key family hold (frozen evidence)
        private const float SampleStep = 0.1f;       // one sample per 100ms => 15 samples
        private const float ArriveBudget = 10f;      // the click-to-move assertion budget
        private static readonly string[] DirKeys = new[] { "w", "a", "s", "d" };

        private string _tag = "u1";
        private string _save = "g66";
        private int _step;
        private float _at;
        private bool _done;
        private float _trigAt;
        private bool _invalidDevice;
        private bool _bootSpaceSent;
        private bool _bootSpaceUp;
        private float _bootSpaceAt;

        // ---- direction-key hold sampling -------------------------------------------------
        private float _holdAt;
        private float _nextSample;
        private readonly List<Vector2Int> _kGrids = new List<Vector2Int>();
        private readonly List<bool> _kMoving = new List<bool>();
        private Vector2Int _kStart;
        private int _kMoved;

        // ---- event counters --------------------------------------------------------------
        private int _moveCmds;
        private int _swapReqs;

        // ---- W step ----------------------------------------------------------------------
        private int _wSwapBefore;
        private int _wMoveBefore;
        private Vector2Int _wGridBefore;
        private int _wSwapDelta;
        private int _wMoveDelta;
        private int _wMoved;

        // ---- ground click ----------------------------------------------------------------
        private int _ckPhase;
        private int _ckFrames;
        private Vector2 _ckPos;
        private Vector2Int _ckWant;
        private Vector2Int _ckFrom;
        private int _ckFound;
        private int _ckMoveBefore = -1;
        private bool _ckFlipTried;
        private bool _ckFlipY;
        private int _ckMoveDelta;
        private int _ckMoved;
        private int _ckArrived;
        private int _cMovedFinal = -1;
        private int _cMoveCmdsFinal = -1;

        // ---- UI click sequencer ----------------------------------------------------------
        private Vector2 _pendingUiClick;
        private string _pendingUiClickName = string.Empty;
        private int _uiClickPhase;
        private int _uiClickFrames;

        // ---- flow ------------------------------------------------------------------------
        private string _lastPanel = "(null)";
        private bool _legacyRetried;
        private bool _sawLoading;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            var donePath = parts.Length > 2 ? parts[2] : string.Empty;
            Probe.Paths(donePath);
            _step = 0;
            _at = Time.unscaledTime;
            // same marker line the first (frozen) evidence carries
            Probe.Log("PROBE-INSTALL done=" + donePath + " frame=" + Time.frameCount);
            Probe.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" frame=" + Time.frameCount);
        }

        private void Update()
        {
            if (_done) return;
            try
            {
                TickUiClick();
                Step();
            }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        // ================================================================ plumbing ==========
        private bool Elapsed(float seconds) { return Time.unscaledTime - _at >= seconds; }
        private void Next() { _step++; _at = Time.unscaledTime; _legacyRetried = false; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }

        private string Panel()
        {
            if (BootOpen()) return "Boot";
            if (MenuOpen()) return "MainMenu";
            if (SelectOpen()) return "CharSelect";
            if (CreateOpen()) return "CharCreate";
            if (HudOpen()) return "Hud";
            return "(none)";
        }

        private void Reached(string panel)
        {
            var dt = Time.unscaledTime - _trigAt;
            Probe.KV("FLOW", _lastPanel + "->" + panel + " ok after " + dt.ToString("0.00") + "s fsm=" + Fsm());
            _lastPanel = panel;
            Next();
        }

        /// <summary>Note a timeout and advance (never abort -- the assertions decide).</summary>
        private bool Tout(string at, float seconds)
        {
            if (!Elapsed(seconds)) return false;
            Probe.KV("STEP-TIMEOUT", "at=" + at + " elapsed=" + (Time.unscaledTime - _at).ToString("0.0")
                + " fsm=" + Fsm() + " scene=" + SceneName() + " panel=" + Panel());
            Next();
            return true;
        }

        /// <summary>
        /// Wait for a panel; on the SECOND timeout retry the trigger once through the legacy
        /// ExecuteEvents path (the UI navigation is not the judged item here -- the real-mouse
        /// delivery is judged on the ground click, where a miss is reported, not retried away).
        /// </summary>
        private void WaitPanel(string at, float seconds, string retryButton)
        {
            if (Elapsed(seconds) && retryButton.Length > 0 && !_legacyRetried)
            {
                Probe.Warn("FLOW-RETRY " + at + ": the target panel never opened -> legacy click \"" + retryButton + "\"");
                _legacyRetried = true;
                Probe.Click(retryButton);
                _at = Time.unscaledTime;
                return;
            }
            Tout(at, seconds);
        }

        // ================================================================ UI clicks ========
        /// <summary>Move -> down (2 frames) -> up (2 frames); same shape the sibling drivers use.</summary>
        private void QueueUiClick(Vector2 pos, string nodeName)
        {
            _pendingUiClick = pos;
            _pendingUiClickName = nodeName;
            Probe.MouseState(pos, false);
            _uiClickPhase = 1;
            _uiClickFrames = 2;
        }

        private void TickUiClick()
        {
            if (_uiClickPhase == 0) return;
            _uiClickFrames--;
            if (_uiClickFrames > 0) return;
            if (_uiClickPhase == 1)
            {
                Probe.MouseState(_pendingUiClick, true);
                Probe.KV("UI-DOWN", "node=" + _pendingUiClickName + " pos=" + Probe.V(_pendingUiClick)
                    + " frame=" + Time.frameCount);
                _uiClickPhase = 2;
                _uiClickFrames = 2;
                return;
            }
            Probe.MouseState(_pendingUiClick, false);
            Probe.KV("UI-UP", "node=" + _pendingUiClickName + " pos=" + Probe.V(_pendingUiClick)
                + " frame=" + Time.frameCount);
            _uiClickPhase = 0;
            _uiClickFrames = 0;
        }

        /// <summary>Real-mouse click on a Button found by GameObject name. false => legacy path was used.</summary>
        private bool ClickButton(string name, string tag)
        {
            string diag;
            var b = Probe.FindButton(name, out diag);
            if (b == null)
            {
                Probe.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Probe.Click(name);
                return false;
            }
            Vector2 lo, hi, c;
            var has = Probe.ScreenRect(b.transform as RectTransform, out lo, out hi, out c);
            Probe.KV("CLICKTOP", "tag=" + tag + " name=" + name
                + " label=\"" + Probe.ButtonLabel(b) + "\""
                + " interactable=" + (b.interactable ? 1 : 0)
                + " screen=" + Probe.V(c) + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0")
                + " raycastTop=\"" + Probe.RaycastTop(c) + "\" " + diag);
            if (!has) { Probe.Warn("UI-RECT-MISS name=" + name + " -> legacy path"); Probe.Click(name); return false; }
            QueueUiClick(c, name);
            return true;
        }

        // ================================================================ chain =============
        private void Step()
        {
            switch (_step)
            {
                // ---------------------------------------------------------- 0 environment
                case 0:
                    {
                        var device = Probe.DeviceName();
                        Probe.Log("ENV device=\"" + device + "\" type=" + SystemInfo.graphicsDeviceType
                            + " res=" + Screen.width + "x" + Screen.height
                            + " unity=" + Application.unityVersion
                            + " isPlaying=" + (Application.isPlaying ? 1 : 0)
                            + " batchMode=" + (Application.isBatchMode ? 1 : 0)
                            + " runInBg=" + (Application.runInBackground ? 1 : 0)
                            + " vSync=" + QualitySettings.vSyncCount
                            + " targetFps=" + Application.targetFrameRate);
                        if (Probe.SoftwareRaster(device))
                        {
                            _invalidDevice = true;
                            Probe.Warn("DEVICE-SOFTWARE-RASTER device=\"" + device + "\" => every frame-pacing/"
                                + "rendering verdict on this machine is INVALID; the U-1 chain is not run");
                            Finish("software-raster");
                            return;
                        }
                        Game.Event.On<Vector2Int>(Diablo2.Core.Events.MoveCommand, OnMoveCommand);
                        Game.Event.On(Diablo2.Core.Events.SwapWeaponRequest, OnSwapWeaponRequest);
                        Probe.Log("SUBSCRIBE MoveCommand=1 SwapWeaponRequest=1");
                        Probe.Log("PROBE-BEGIN frame=" + Time.frameCount + " fsm=" + Fsm());
                        _lastPanel = "boot";
                        _trigAt = Time.unscaledTime;
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 1 boot -> main menu
                // NOTE: the boot panel closes on the key press itself, so "!BootOpen()" after the
                // press means SUCCESS (the main menu comes next) -- it must not be treated as
                // "not ready yet" (that would burn the whole timeout and leave the key held).
                case 1:
                    if (!_bootSpaceSent)
                    {
                        if (!BootOpen()) { Tout("boot-wait", 30f); return; }
                        if (!Elapsed(1.2f)) return;
                        Probe.Log("BOOT injection: real Keyboard Space press fsm=" + Fsm());
                        Probe.KeyDown("space");
                        _bootSpaceSent = true;
                        _bootSpaceAt = Time.unscaledTime;
                        _trigAt = _bootSpaceAt;
                        return;
                    }
                    if (!_bootSpaceUp && Time.unscaledTime - _bootSpaceAt >= 0.25f)
                    {
                        Probe.KeyUp();
                        _bootSpaceUp = true;
                    }
                    if (!BootOpen()) { Next(); return; }            // boot closed => main menu is next
                    if (Time.unscaledTime - _bootSpaceAt > 10f)
                    {
                        Probe.Warn("BOOT the panel is still open 10s after the Space press");
                        Next();
                    }
                    return;

                case 2:
                    if (!MenuOpen()) { WaitPanel("mainmenu-wait", 25f, string.Empty); return; }
                    Reached("MainMenu");
                    return;

                // ---------------------------------------------------------- 3 main menu -> select
                case 3:
                    if (!Elapsed(1.0f)) return;
                    ClickButton("Single", "menu");
                    Next();
                    return;

                case 4:
                    if (!SelectOpen()) { WaitPanel("charselect-wait", 25f, "Single"); return; }
                    Reached("CharSelect");
                    return;

                // ---------------------------------------------------------- 5 select -> create
                case 5:
                    if (!Elapsed(0.8f)) return;
                    ClickButton("Create", "select");
                    Next();
                    return;

                case 6:
                    if (!CreateOpen()) { WaitPanel("charcreate-wait", 25f, "Create"); return; }
                    Reached("CharCreate");
                    return;

                // ---------------------------------------------------------- 7 create -> select (Back)
                case 7:
                    if (!Elapsed(0.9f)) return;
                    ClickButton("Back", "create");
                    Next();
                    return;

                case 8:
                    if (!SelectOpen()) { WaitPanel("charselect2-wait", 25f, "Back"); return; }
                    Reached("CharSelect");
                    return;

                // ---------------------------------------------------------- 9 enter stage
                case 9:
                    if (!Elapsed(0.6f)) return;
                    Probe.KV("ENTER-STAGE", "save=\"" + _save + "\"");
                    Probe.KV("ENTER-STAGE-VIA", "Events.CharSelectRequest from panel=" + Panel());
                    _trigAt = Time.unscaledTime;
                    Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                    Next();
                    return;

                case 10:
                    if (!_sawLoading && LoadingOpen()) _sawLoading = true;
                    if (!(HudOpen() && Fsm() == "Stage")) { Tout("stage-wait", 45f); return; }
                    if (_lastPanel != "Stage")
                    {
                        var dt = Time.unscaledTime - _trigAt;
                        var from = _sawLoading ? "Loading" : _lastPanel;
                        Probe.KV("FLOW", from + "->Stage ok after " + dt.ToString("0.00") + "s fsm=" + Fsm());
                        _lastPanel = "Stage";
                        _at = Time.unscaledTime;
                        return;
                    }
                    if (!Elapsed(0.5f)) return;
                    {
                        var p = Probe.Player();
                        var m = Probe.Map();
                        Probe.KV("STAGE-READY", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                            + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                            + " grid=" + Probe.Grid(Probe.GridOf(p))
                            + " alive=" + (p != null && !p.IsDead ? 1 : 0)
                            + " timeScale=" + Time.timeScale.ToString("0.##"));
                    }
                    Next();
                    return;

                // ---------------------------------------------------------- 11 (1) direction keys
                case 11:
                    {
                        var p = Probe.Player();
                        _kStart = Probe.GridOf(p);
                        _kGrids.Clear();
                        _kMoving.Clear();
                        _kMoved = 0;
                        Probe.KV("K START", "grid=" + Probe.Grid(_kStart)
                            + " area=" + (Probe.Map() != null ? Probe.Map().Area.ToString() : "-")
                            + " isMoving=" + (p != null && p.IsMoving ? 1 : 0)
                            + " keys=" + string.Join("+", DirKeys).ToUpperInvariant()
                            + " held " + HoldSeconds.ToString("0.0") + "s frame=" + Time.frameCount);
                        Probe.KeysDown(DirKeys);
                        _holdAt = Time.unscaledTime;
                        _nextSample = _holdAt + SampleStep;
                        Next();
                        return;
                    }

                case 12:
                    {
                        var now = Time.unscaledTime;
                        while (now >= _nextSample && _kGrids.Count < (int)(HoldSeconds / SampleStep))
                        {
                            var p = Probe.Player();
                            _kGrids.Add(Probe.GridOf(p));
                            _kMoving.Add(p != null && p.IsMoving);
                            _nextSample += SampleStep;
                        }
                        if (now - _holdAt < HoldSeconds) return;
                        Probe.KeyUp();
                        var end = Probe.GridOf(Probe.Player());
                        _kMoved = Probe.Chebyshev(_kStart, end);
                        var movingSamples = 0;
                        for (var i = 0; i < _kMoving.Count; i++) { if (_kMoving[i]) movingSamples++; }
                        for (var i = 0; i < _kGrids.Count; i++)
                        {
                            Probe.KV("K n=" + (i + 1).ToString("00"), "grid=" + Probe.Grid(_kGrids[i])
                                + " IsMoving=" + (_kMoving[i] ? "True" : "False"));
                        }
                        var ok = _kMoved == 0 && movingSamples == 0;
                        Probe.Check("direction keys do not drive the character", ok,
                            "K DONE: after holding " + HoldSeconds.ToString("0.0") + "s "
                            + "grid=" + Probe.Grid(_kStart) + "->" + Probe.Grid(end)
                            + " moved_cells=" + _kMoved + " (expect 0)"
                            + " isMoving_samples=" + movingSamples + " of " + _kMoving.Count
                            + " => " + (ok ? "PASS" : "FAIL"));
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 13 (2) W
                case 13:
                    if (!Elapsed(0.5f)) return;
                    _wSwapBefore = _swapReqs;
                    _wMoveBefore = _moveCmds;
                    _wGridBefore = Probe.GridOf(Probe.Player());
                    Probe.KeysDown(new[] { "w" });
                    Next();
                    return;

                case 14:
                    if (!Elapsed(0.25f)) return;
                    Probe.KeyUp();
                    Next();
                    return;

                case 15:
                    if (!Elapsed(0.6f)) return;
                    {
                        var end = Probe.GridOf(Probe.Player());
                        _wSwapDelta = _swapReqs - _wSwapBefore;
                        _wMoveDelta = _moveCmds - _wMoveBefore;
                        _wMoved = Probe.Chebyshev(_wGridBefore, end);
                        var ok = _wSwapDelta >= 1 && _wMoveDelta == 0 && _wMoved == 0;
                        Probe.Check("W key = swap weapon group and still does not move", ok,
                            "W DONE: swapRequest_delta=" + _wSwapDelta + " moveCommand_delta=" + _wMoveDelta
                            + " moved_cells=" + _wMoved + " (expect swap>=1, move=0) => " + (ok ? "PASS" : "FAIL"));
                        Probe.KV("W START", "binding_source=GameKeyAlias.KeySwapWeapon (=W)"
                            + " reader=InputReader.SwapWeaponPressed grid=" + Probe.Grid(_wGridBefore));
                        Next();
                    }
                    return;

                // ---------------------------------------------------------- 16 (3) ground click
                case 16:
                    {
                        var p = Probe.Player();
                        _ckFrom = Probe.GridOf(p);
                        _ckWant = PickTarget(_ckFrom, out _ckFound);
                        Probe.KV("C TARGET", "found=" + _ckFound + " want=" + Probe.Grid(_ckWant)
                            + " from=" + Probe.Grid(_ckFrom)
                            + " walkable=" + (Probe.Map() != null && Probe.Map().Walkable(_ckWant) ? 1 : 0)
                            + " camera=" + (Camera.main != null ? 1 : 0));
                        if (_ckFound == 0 || Camera.main == null || Probe.Map() == null)
                        {
                            Probe.Warn("C-ABORT no walkable target / no camera / no map");
                            Probe.Check("clicking the ground really moves the character", false,
                                "C DONE: want=" + Probe.Grid(_ckWant) + " from=" + Probe.Grid(_ckFrom)
                                + " to=" + Probe.Grid(_ckFrom) + " moved_cells=0 (expect >0)"
                                + " moveCommand_delta=0 arrived=0 => FAIL");
                            Next();
                            return;
                        }
                        _ckPhase = 0;
                        _ckFrames = 0;
                        _ckFlipTried = false;
                        _ckFlipY = false;
                        _ckMoveBefore = -1;
                        Next();
                        return;
                    }

                case 17:
                    {
                        var r = TickGroundClick();
                        if (r == 0) return;
                        if (r < 0)
                        {
                            // the projection never round-tripped => the real click could not be aimed
                            Probe.Check("clicking the ground really moves the character", false,
                                "C DONE: want=" + Probe.Grid(_ckWant) + " from=" + Probe.Grid(_ckFrom)
                                + " to=" + Probe.Grid(Probe.GridOf(Probe.Player()))
                                + " moved_cells=0 (expect >0) moveCommand_delta=0 arrived=0"
                                + " => FAIL (screen<->grid projection mismatch, the click was NOT delivered)");
                        }
                        Next();
                        return;
                    }

                case 18:
                    {
                        var p = Probe.Player();
                        var now = Probe.GridOf(p);
                        var arrived = now == _ckWant;
                        if (!arrived && !Elapsed(ArriveBudget)) return;
                        _ckMoved = Probe.Chebyshev(_ckFrom, now);
                        _ckMoveDelta = _moveCmds - (_ckMoveBefore < 0 ? _moveCmds : _ckMoveBefore);
                        _ckArrived = arrived ? 1 : 0;
                        _cMovedFinal = _ckMoved;
                        _cMoveCmdsFinal = _ckMoveDelta;
                        var ok = arrived && _ckMoved > 0 && _ckMoveDelta >= 1;
                        Probe.Check("clicking the ground really moves the character", ok,
                            "C DONE: want=" + Probe.Grid(_ckWant) + " from=" + Probe.Grid(_ckFrom)
                            + " to=" + Probe.Grid(now) + " moved_cells=" + _ckMoved + " (expect >0)"
                            + " moveCommand_delta=" + _ckMoveDelta + " arrived=" + _ckArrived
                            + " => " + (ok ? "PASS" : "FAIL"));
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 19 device + alive + verdict
                case 19:
                    {
                        var device = Probe.DeviceName();
                        Probe.Check("real render device is a hardware GPU", !Probe.SoftwareRaster(device),
                            "device=\"" + device + "\"");
                        var p = Probe.Player();
                        var m = Probe.Map();
                        var alive = p != null && !p.IsDead;
                        Probe.Check("player is alive in town", alive,
                            "dead=" + (p != null && p.IsDead ? 1 : 0)
                            + " area=" + (m != null ? m.Area.ToString() : "(no-map)"));
                        Probe.KV("SUMMARY", "k_moved=" + _kMoved + " w_swap=" + _swapReqs
                            + " c_moved=" + _cMovedFinal + " c_moveCmds=" + _cMoveCmdsFinal);
                        var okAll = Probe.AllChecksOk();
                        Probe.Log("VERDICT ok=" + (okAll ? 1 : 0)
                            + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad()
                            + " fsm=" + Fsm());
                        Finish("end");
                        return;
                    }

                default:
                    Finish("end");
                    return;
            }
        }

        // ================================================================ ground click ======
        /// <summary>
        /// Pick a walkable target a few cells away (the frozen evidence used 3 cells west:
        /// (9,6) -> (6,6)).  The offsets are tried in order; "found" = 0 means no candidate.
        /// The criterion is "a real ground click makes the character walk", so the exact cell
        /// may differ per session (the spawn comes from the save) -- the assertion does not.
        /// </summary>
        private static Vector2Int PickTarget(Vector2Int from, out int found)
        {
            var offsets = new[]
            {
                new Vector2Int(-3, 0), new Vector2Int(-2, 0), new Vector2Int(-2, -1), new Vector2Int(-2, 1),
                new Vector2Int(-3, -1), new Vector2Int(-3, 1), new Vector2Int(0, -3), new Vector2Int(-1, -3),
                new Vector2Int(1, -3), new Vector2Int(0, 3), new Vector2Int(-1, 3), new Vector2Int(1, 3),
                new Vector2Int(-3, -2), new Vector2Int(-3, 2), new Vector2Int(2, -3), new Vector2Int(2, 3),
                new Vector2Int(-4, 0), new Vector2Int(4, 0), new Vector2Int(0, -4), new Vector2Int(0, 4),
                new Vector2Int(-2, -2), new Vector2Int(-2, 2), new Vector2Int(2, -2), new Vector2Int(2, 2),
            };
            var map = Probe.Map();
            found = 0;
            foreach (var o in offsets)
            {
                var g = from + o;
                found++;
                if (map != null && map.Walkable(g)) return g;
            }
            found = 0;
            return from;
        }

        /// <summary>0 = running, 1 = delivered, -1 = gave up (projection never round-tripped).</summary>
        private int TickGroundClick()
        {
            var cam = Camera.main;
            if (cam == null) { Probe.Warn("C-ABORT Camera.main is null"); return -1; }
            _ckFrames++;
            switch (_ckPhase)
            {
                case 0:
                    {
                        var sp = cam.WorldToScreenPoint(Iso.GridToWorld(_ckWant));
                        _ckPos = new Vector2(sp.x, _ckFlipY ? Screen.height - sp.y : sp.y);
                        Probe.MouseState(_ckPos, false);
                        _ckPhase = 1;
                        _ckFrames = 0;
                        return 0;
                    }
                case 1:
                    if (_ckFrames < 3) return 0;
                    {
                        var got = Iso.ScreenToGrid(cam, Probe.MousePosNow());
                        Probe.KV("C PROJECT", "want=" + Probe.Grid(_ckWant) + " got=" + Probe.Grid(got)
                            + " project_ok=" + (got == _ckWant ? 1 : 0)
                            + " flipY=" + (_ckFlipY ? 1 : 0)
                            + " injected=" + Probe.V(_ckPos) + " frame=" + Time.frameCount);
                        if (got == _ckWant) { _ckPhase = 2; _ckFrames = 0; return 0; }
                        if (!_ckFlipTried)
                        {
                            _ckFlipTried = true;
                            _ckFlipY = !_ckFlipY;
                            Probe.Warn("C CALIB the injected Y did not round-trip -> retrying with flipped Y");
                            _ckPhase = 0;
                            _ckFrames = 0;
                            return 0;
                        }
                        return -1;
                    }
                case 2:
                    if (_ckFrames < 2) return 0;
                    _ckMoveBefore = _moveCmds;
                    Probe.MouseState(_ckPos, true);
                    _ckPhase = 3;
                    _ckFrames = 0;
                    return 0;
                case 3:
                    // the C DOWN line is written one frame AFTER the mousedown, so the values it
                    // reports are the ones the click already produced (isMoving=1) -- same as the
                    // first evidence.  moveCmdsBefore is still the pre-click counter.
                    if (_ckFrames < 2) return 0;
                    Probe.KV("C DOWN", "grid=" + Probe.Grid(Probe.GridOf(Probe.Player()))
                        + " isMoving=" + (Probe.Player() != null && Probe.Player().IsMoving ? 1 : 0)
                        + " moveCmdsBefore=" + _ckMoveBefore
                        + " frame=" + Time.frameCount);
                    Probe.KV("C RAYCAST", "want=" + Probe.Grid(_ckWant)
                        + " top=\"" + Probe.RaycastTop(_ckPos) + "\""
                        + " note=the pointer must not sit over a UI panel (InputReader's UI-hit guard)");
                    Probe.MouseState(_ckPos, false);
                    _ckPhase = 4;
                    _ckFrames = 0;
                    return 0;
                case 4:
                    if (_ckFrames < 2) return 0;
                    return 1;
                default:
                    return 1;
            }
        }

        // ================================================================ events ============
        private void OnMoveCommand(Vector2Int target) { _moveCmds++; }
        private void OnSwapWeaponRequest() { _swapReqs++; }

        // ================================================================ finish ============
        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Probe.KV("FINISH", "why=" + why + " step=" + _step
                + " device=\"" + Probe.DeviceName() + "\""
                + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad());
            Probe.Log("TOUR-DONE why=" + why + " steps=" + _step);
            Probe.WriteFile(Probe.DonePath,
                "TOUR-DONE tag=" + _tag + " why=" + why
                + " verdict=" + (Probe.AllChecksOk() ? 1 : 0)
                + (_invalidDevice ? " INVALID-DEVICE" : string.Empty)
                + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad()
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
    }
}
