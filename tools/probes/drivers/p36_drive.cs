// NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
//
// here is SHORT and targeted at the two engine widgets that carried E19:
//   Boot -> MainMenu
//     -> raise the engine widgets: ToastLayer x2 (the exact E19 CJK strings) + LoadingLayer
//     -> let the UI settle (>= 7s; SKILL: a capture right after a mutation returns the previous frame)
//     -> E19 scan (drawn Texts whose text contains non-ASCII  ==  0 expected)
//     -> hand off to the run script, which captures 1920x1080 from the CLI while we hold the frame
//     -> in-session equivalence probe: hook = null  => that very Text keeps the engine's built-in font;
//        hook restored => the same creation path yields a mirrored (non-drawing) Text + bitmap glyphs.
//
// ASCII ONLY (PS 5.1 / Roslyn both read a BOM-less non-ASCII file as ANSI): every CJK string below is
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CloverEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace P36
{
    public static class Drive
    {
        private const string Tag = "P36";

        /// <summary>E19 evidence string 1 -- "role name already exists", AppFlow.cs:1050 (Game.UI.Toast).</summary>
        private const string E19ToastA = "\u89D2\u8272\u540D\u5DF2\u5B58\u5728";

        /// <summary>E19 evidence string 2 -- "backpack full", InventoryPanel.cs:835 (Game.UI.Toast).</summary>
        private const string E19ToastB = "\u80CC\u5305\u5DF2\u6EE1";

        /// <summary>E19 evidence string 3 -- the LoadingLayer default label, UIWidgets.cs (ui.loading).</summary>
        private const string E19Loading = "\u52A0\u8F7D\u4E2D...";

        private static string _shotDir = string.Empty;
        private static string _readyPath = string.Empty;
        private static string _goPath = string.Empty;
        private static string _donePath = string.Empty;

        /// <summary>Entry point: Step("action", "arg"). Never throws (a probe must leave a log line).</summary>
        public static string Step(string action, string arg)
        {
            try
            {
                switch (action)
                {
                    case "ping": return Ping();
                    case "cfg": return Cfg();
                    case "state": return State(arg);
                    case "scan": return Scan(arg);
                    case "mirrordump": return MirrorDump();
                    case "toast": return RaiseEngineWidgets();
                    case "equivalence": return Equivalence();
                    case "paths": return Paths(arg);
                    case "shot": return Shot(_shotDir, "p36_engine_text_native.png");
                    case "click": return Click(arg);
                    case "mdown": return MouseEvent(true);
                    case "mup": return MouseEvent(false);
                    case "keydown": return KeyDown(arg);
                    case "keyup": return KeyUp();
                    default:
                        Log("ERR unknown-action=" + action);
                        return "ERR-unknown-action";
                }
            }
            catch (Exception ex)
            {
                Log("FATAL action=" + action + " arg=" + arg + " ex=" + ex.GetType().Name + ": " + ex.Message);
                return "EX-" + ex.GetType().Name;
            }
        }

        // ---------------------------------------------------------------- log

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

        // ---------------------------------------------------------------- one-shot steps

        private static string Ping()
        {
            Log("PONG frame=" + Time.frameCount + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
            return "PONG";
        }

        internal static string Paths(string arg)
        {
            // arg = "shotDir|readyPath|goPath|donePath"
            var parts = (arg ?? string.Empty).Split('|');
            if (parts.Length > 0) _shotDir = parts[0];
            if (parts.Length > 1) _readyPath = parts[1];
            if (parts.Length > 2) _goPath = parts[2];
            if (parts.Length > 3) _donePath = parts[3];
            Log("PATHS shotDir=" + _shotDir + " ready=" + _readyPath + " go=" + _goPath + " done=" + _donePath);
            return "PATHS-OK";
        }

        internal static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // NEVER touch InputSystem.settings.updateMode (engine skill P-4 item 4).

            var added = 0;
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); added = 1; }
            var mouseAdded = 0;
            if (Mouse.current == null) { InputSystem.AddDevice<Mouse>(); mouseAdded = 1; }

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " focused=" + (Application.isFocused ? 1 : 0)
                       + " keyboard=" + (kb != null ? kb.name : "(null)")
                       + " keyboardAdded=" + added
                       + " mouseAdded=" + mouseAdded
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " hookRegistered=" + (TextHooks.Current != null ? 1 : 0)
                       + " hookType=" + (TextHooks.Current != null ? TextHooks.Current.GetType().FullName : "(null)");
            Log(line);
            return line;
        }

        internal static string State(string note)
        {
            var line = "SAMPLE " + SampleText(note);
            Log(line);
            return line;
        }

        internal static string SampleText(string note)
        {
            var sb = new StringBuilder();
            sb.Append("t=").Append(Time.time.ToString("0.00"));
            sb.Append(" frame=").Append(Time.frameCount);
            sb.Append(" fsm=").Append(Game.Fsm != null ? Game.Fsm.Current : "(null)");
            sb.Append(" scene=").Append(Game.Scene != null ? (Game.Scene.CurrentScene ?? "(null)") : "(no-scene-manager)");
            sb.Append(" sceneCount=").Append(SceneManager.sceneCount);
            sb.Append(" panels=[").Append(Panels()).Append(']');
            sb.Append(" loading=").Append(Game.UI != null && Game.UI.IsLoading ? 1 : 0);
            sb.Append(" hook=").Append(TextHooks.Current != null ? "set" : "null");
            sb.Append(" note=").Append(note ?? string.Empty);
            return sb.ToString();
        }

        internal static string Panels()
        {
            if (Game.UI == null) return "(no-ui)";
            var sb = new StringBuilder();
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.BootPanel>(), "Boot");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(), "MainMenu");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(), "CharSelect");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(), "CharCreate");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(), "LoadingPanel");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.HudPanel>(), "Hud");
            return sb.ToString();
        }

        private static void AddIf(StringBuilder sb, bool on, string name)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }

        // ---------------------------------------------------------------- the E19 scan

        private static bool HasNonAscii(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (var i = 0; i < s.Length; i++)
                if (s[i] > 127) return true;
            return false;
        }

        /// <summary>
        /// The E19 criterion, unchanged from round 3 (`[A54] ... nonAscii=N`): a uGUI Text counts as a
        /// violation only when it is BOTH enabled AND has a font AND its text contains non-ASCII.
        /// `D2TextMirror` sets `enabled = false` + `font = null` => mirrored texts can never count.
        /// </summary>
        internal static string Scan(string note)
        {
            var all = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var drawn = new List<string>();
            var drawnAscii = 0;
            var mirrored = 0;
            var mirroredBad = 0;
            var probeDrawn = 0;
            var probeMirrored = 0;

            foreach (var t in all)
            {
                if (t == null || t.gameObject == null) continue;
                var path = PathOf(t.transform);
                var isProbe = t.gameObject.name.StartsWith("P36Probe", StringComparison.Ordinal);
                var mirror = t.GetComponent("Diablo2.UI.D2TextMirror") != null;

                if (t.enabled && t.font != null)
                {
                    if (HasNonAscii(t.text))
                    {
                        drawn.Add(path + "=" + t.text);
                        if (isProbe) probeDrawn++;
                    }
                    else drawnAscii++;
                }
                else if (mirror)
                {
                    mirrored++;
                    if (t.enabled || t.font != null) mirroredBad++;
                    if (isProbe) probeMirrored++;
                }
            }

            var line = "TEXTSCAN note=" + note
                       + " totalTexts=" + all.Length
                       + " drawnNonAscii=" + drawn.Count
                       + " drawnAscii=" + drawnAscii
                       + " mirrored=" + mirrored
                       + " mirroredStillDrawing=" + mirroredBad
                       + " probeDrawn=" + probeDrawn
                       + " probeMirrored=" + probeMirrored
                       + " nonAsciiList=[" + (drawn.Count == 0 ? "(none)" : string.Join(" | ", drawn.ToArray())) + "]";
            Log(line);
            return "drawnNonAscii=" + drawn.Count;
        }

        /// <summary>
        /// Per-node proof that the pixels of the engine widgets no longer come from uGUI: for every Text
        /// under an engine-widget layer we print enabled/font/mirror and the D2Label child layout
        /// ("Glyphs(N)" == N bitmap quads actually drawn).
        /// </summary>
        internal static string MirrorDump()
        {
            var all = UnityEngine.Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var hits = 0;
            var drawnByUgui = 0;
            foreach (var t in all)
            {
                if (t == null || t.gameObject == null) continue;
                var path = PathOf(t.transform);
                if (!IsEngineWidgetPath(path)) continue;
                hits++;

                var mirror = t.GetComponent("Diablo2.UI.D2TextMirror") != null;
                if (t.enabled && t.font != null) drawnByUgui++;

                var children = new StringBuilder();
                for (var i = 0; i < t.transform.childCount; i++)
                {
                    var c = t.transform.GetChild(i);
                    if (children.Length > 0) children.Append(',');
                    children.Append(c.name);
                    if (c.name == "Glyphs")
                        children.Append('(').Append(c.childCount).Append(')');
                }

                Log("WIDGET path=" + path
                    + " enabled=" + (t.enabled ? 1 : 0)
                    + " font=" + (t.font == null ? "(null)" : t.font.name)
                    + " mirror=" + (mirror ? 1 : 0)
                    + " text=\"" + t.text + "\""
                    + " activeInHierarchy=" + (t.gameObject.activeInHierarchy ? 1 : 0)
                    + " children=[" + children + "]");
            }
            Log("WIDGET-DUMP engineTexts=" + hits + " stillDrawnByUgui=" + drawnByUgui);
            return "widgets=" + hits + " drawnByUgui=" + drawnByUgui;
        }

        private static bool IsEngineWidgetPath(string path)
        {
            return path.IndexOf("/Toasts/", StringComparison.Ordinal) >= 0
                   || path.IndexOf("/Loading/", StringComparison.Ordinal) >= 0
                   || path.IndexOf("/FloatTexts/", StringComparison.Ordinal) >= 0
                   || path.IndexOf("/Confirm", StringComparison.Ordinal) >= 0
                   || path.IndexOf("/Guide", StringComparison.Ordinal) >= 0;
        }

        /// <summary>Raise the two engine widgets that carried E19 (ToastLayer x2 + LoadingLayer).</summary>
        internal static string RaiseEngineWidgets()
        {
            if (Game.UI == null) { Warn("RAISE Game.UI == null"); return "ERR-no-ui"; }

            // long duration on purpose: the run script captures from the CLI seconds later
            Game.UI.Toast(E19ToastA, 999f);
            Game.UI.Toast(E19ToastB, 999f);
            Game.UI.ShowLoading(E19Loading);

            var line = "RAISE toastA=\"" + E19ToastA + "\" toastB=\"" + E19ToastB + "\" loading=\"" + E19Loading
                       + "\" isLoading=" + (Game.UI.IsLoading ? 1 : 0);
            Log(line);
            return "RAISED";
        }

        /// <summary>
        /// The in-session "unregistered == byte-identical" proof (no extra Play session needed):
        /// the SAME creation path (`UIFactory.CreateText`) is exercised twice, once with
        /// `TextHooks.Current = null` and once with the project hook restored.
        ///   - no hook  => the Text keeps UIFactory.DefaultFont() and IS drawn by uGUI (E19 behaviour)
        ///   - hook set => the Text is a data holder (enabled=false / font=null) while a D2Label child
        ///                 draws bitmap glyphs instead
        /// </summary>
        internal static string Equivalence()
        {
            var saved = TextHooks.Current;
            var parent = UiCanvasTransform();
            if (parent == null) { Warn("EQUIV no canvas found"); return "ERR-no-canvas"; }

            const string sample = "\u89D2\u8272\u540D\u5DF2\u5B58\u5728";   // same CJK string as the toast

            // ---- A) no hook: the engine's own behaviour ------------------------------------------
            TextHooks.Current = null;
            var a = UIFactory.CreateText("P36ProbeNoHook", parent, sample, 24, TextAnchor.MiddleCenter, Color.white);
            Log("EQUIV nohook enabled=" + (a.enabled ? 1 : 0)
                + " font=" + (a.font == null ? "(null)" : a.font.name)
                + " mirror=" + (a.GetComponent("Diablo2.UI.D2TextMirror") != null ? 1 : 0)
                + " text=\"" + a.text + "\"");
            var afterA = Scan("equiv-noHook");
            if (a != null) UnityEngine.Object.Destroy(a.gameObject);

            // ---- B) hook restored: the same call, mirrored ---------------------------------------
            TextHooks.Current = saved;
            var b = UIFactory.CreateText("P36ProbeHook", parent, sample, 24, TextAnchor.MiddleCenter, Color.white);
            var bChildren = new StringBuilder();
            for (var i = 0; i < b.transform.childCount; i++)
            {
                var c = b.transform.GetChild(i);
                if (bChildren.Length > 0) bChildren.Append(',');
                bChildren.Append(c.name);
                if (c.name == "Glyphs") bChildren.Append('(').Append(c.childCount).Append(')');
            }
            Log("EQUIV hooked enabled=" + (b.enabled ? 1 : 0)
                + " font=" + (b.font == null ? "(null)" : b.font.name)
                + " mirror=" + (b.GetComponent("Diablo2.UI.D2TextMirror") != null ? 1 : 0)
                + " children=[" + bChildren + "]"
                + " text=\"" + b.text + "\"");
            var afterB = Scan("equiv-hooked");
            if (b != null) UnityEngine.Object.Destroy(b.gameObject);
            TextHooks.Current = saved;

            Log("EQUIV-RESULT hookRestored=" + (TextHooks.Current != null ? 1 : 0)
                + " afterNoHook=" + afterA + " afterHooked=" + afterB);
            return "EQUIV " + afterA + " / " + afterB;
        }

        private static Transform UiCanvasTransform()
        {
            var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            Transform first = null;
            for (var i = 0; i < canvases.Length; i++)
            {
                var c = canvases[i];
                if (c == null) continue;
                if (first == null) first = c.transform;
                if (c.gameObject.name == "[UI]") return c.transform;
            }
            return first;
        }

        // ---------------------------------------------------------------- markers

        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) { Warn("MARKER path empty"); return; }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
                Log("MARKER-WRITTEN " + path);
            }
            catch (Exception ex)
            {
                Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); } catch { return false; }
        }

        // ---------------------------------------------------------------- input / click

        internal static string Click(string arg)
        {
            if (EventSystem.current == null)
            {
                Log("ERR click name=" + arg + " eventSystem=null");
                return "ERR-no-eventsystem";
            }

            var pathSub = string.Empty;
            var want = arg ?? string.Empty;
            var bar = want.IndexOf('|');
            if (bar >= 0) { pathSub = want.Substring(0, bar); want = want.Substring(bar + 1); }

            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            var picks = new List<Button>();
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                var p = PathOf(b.transform);
                if (pathSub.Length > 0 && p.IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                picks.Add(b);
            }

            if (picks.Count == 0)
            {
                Log("ERR click-miss name=" + arg + " buttons=" + all.Length);
                return "ERR-click-miss";
            }

            var pick = picks[0];
            foreach (var c in picks)
                if (PathOf(c.transform).Length > PathOf(pick.transform).Length) pick = c;

            var go = pick.gameObject;
            var ped = new PointerEventData(EventSystem.current);
            ped.button = PointerEventData.InputButton.Left;
            var rt = pick.transform as RectTransform;
            if (rt != null)
                ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            var top = hits.Count > 0 && hits[0].gameObject != null ? hits[0].gameObject.name : "(none)";
            var path = PathOf(go.transform);
            Log("CLICK name=" + want + " path=" + path + " raycastTop=" + top
                + " hitSelf=" + (hits.Count > 0 && hits[0].gameObject == go ? 1 : 0));

            if (!pick.interactable || !go.activeInHierarchy)
            {
                Log("CLICK-SKIP name=" + want + " (not interactable / inactive)");
                return "CLICK-SKIP";
            }

            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-DONE name=" + want + " path=" + path);
            return "CLICKED";
        }

        internal static string KeyDown(string arg)
        {
            var kb = RequireKeyboard();
            if (kb == null) return "ERR-no-keyboard";
            Key key;
            switch ((arg ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = Key.Space; break;
                case "escape": key = Key.Escape; break;
                case "enter": key = Key.Enter; break;
                case "i": key = Key.I; break;
                default:
                    Log("ERR keydown unknown-key=" + arg);
                    return "ERR-unknown-key";
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key + " keyboard=" + kb.name);
            return "KEYDOWN-" + key;
        }

        internal static string KeyUp()
        {
            var kb = RequireKeyboard();
            if (kb == null) return "ERR-no-keyboard";
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
            return "KEYUP";
        }

        internal static string MouseEvent(bool down)
        {
            var mouse = Mouse.current;
            if (mouse == null) { mouse = InputSystem.AddDevice<Mouse>(); Warn("mouse device missing -> added"); }
            if (mouse == null) return "ERR-no-mouse";
            // bottom-left corner on purpose: screen centre lands on UI hotspots and would leave
            // hover states in the screenshots (see p34_drive.cs for the full story).
            var state = new MouseState { position = new Vector2(60f, 60f) };
            if (down) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
            Log("MOUSE down=" + (down ? 1 : 0));
            return down ? "MDOWN" : "MUP";
        }

        private static Keyboard RequireKeyboard()
        {
            var kb = Keyboard.current;
            if (kb != null) return kb;
            kb = InputSystem.AddDevice<Keyboard>();
            Log("WARN keyboard-missing added=" + (kb != null ? kb.name : "(null)"));
            return kb;
        }

        // ---------------------------------------------------------------- helpers

        internal static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            var cur = t;
            while (cur != null)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, cur.name);
                cur = cur.parent;
            }
            return sb.ToString();
        }

        internal static string Shot(string dir, string file)
        {
            var path = (string.IsNullOrEmpty(dir) ? Application.dataPath + "/Screenshots" : dir) + "/" + file;
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                Log("SHOT-SELF file=" + file + " path=" + path + " frame=" + Time.frameCount
                    + " t=" + Time.time.ToString("0.00"));
                return path;
            }
            catch (Exception ex)
            {
                Log("SHOT-SELF-FAIL file=" + file + " ex=" + ex.GetType().Name + ": " + ex.Message);
                return string.Empty;
            }
        }
    }

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;shot dir&gt;|&lt;ready&gt;|&lt;go&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P36TextHookDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                      + " hookRegistered=" + (TextHooks.Current != null ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One short tour (no engine timers -- polling in Update): Boot -> MainMenu -> raise the engine
    /// widgets -> settle -> E19 scan -> write READY and WAIT for the run script's capture ->
    /// in-session equivalence probe -> DONE.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "r1";
        private string _shotDir = string.Empty;
        private string _ready = string.Empty;
        private string _go = string.Empty;
        private string _done = string.Empty;

        private int _step;
        private float _stepAt;
        private string _timedOutAt = string.Empty;
        private bool _doneFlag;
        private int _shots;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _shotDir = parts[2];
            if (parts.Length > 3) _ready = parts[3];
            if (parts.Length > 4) _go = parts[4];
            if (parts.Length > 5) _done = parts[5];
            _step = 0;
            _stepAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount);
        }

        private void Update()
        {
            if (_doneFlag) return;
            try { Step(); }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private bool Elapsed(float seconds) { return Time.unscaledTime - _stepAt >= seconds; }
        private void Next() { _step++; _stepAt = Time.unscaledTime; }
        private void Sample(string state) { Drive.Log("SAMPLE state=" + state + " " + Drive.SampleText(state)); }

        private void Timeout(string step)
        {
            Drive.Log("STEP-TIMEOUT step=" + step + " elapsed=" + (Time.unscaledTime - _stepAt).ToString("0.0")
                      + " " + Drive.SampleText("timeout-" + step));
            if (_timedOutAt.Length == 0) _timedOutAt = step;
            Finish("timeout-" + step);
        }

        private void Finish(string why)
        {
            _doneFlag = true;
            Sample("done");
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step + " shots=" + _shots
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                      + " t=" + Time.time.ToString("0.00"));
            Drive.WriteFile(_done, "TOUR-DONE tag=" + _tag + " ok=" + why + " steps=" + _step
                + " shots=" + _shots + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void Step()
        {
            switch (_step)
            {
                // ---- 00 boot screen: the shipped entry is "any key OR a real left click" ---------
                case 0:
                    if (!BootOpen()) { if (Elapsed(30f)) Timeout("boot"); return; }
                    Sample("boot");
                    Drive.MouseEvent(true);
                    Drive.MouseEvent(false);
                    Next();
                    return;

                case 1:
                    if (!Elapsed(1.2f)) return;
                    if (BootOpen()) { Drive.KeyDown("space"); Drive.KeyUp(); }
                    Next();
                    return;

                // ---- 01 main menu ---------------------------------------------------------------
                case 2:
                    if (!MenuOpen() || SceneName() != "Menu")
                    {
                        if (Elapsed(25f)) Timeout("mainmenu");
                        return;
                    }
                    Sample("mainmenu");
                    Drive.Scan("mainmenu");
                    Next();
                    return;

                // ---- 02 raise the engine widgets that carried E19 (mutation gets its OWN step:
                //         a capture in the same frame would still show the previous one) ----------
                case 3:
                    if (!Elapsed(0.4f)) return;
                    Drive.RaiseEngineWidgets();
                    Next();
                    return;

                // ---- 03 settle, then measure + hand the frame to the run script for the capture --
                case 4:
                    if (!Elapsed(7.0f)) return;      // SKILL: capture >= 6s after the last UI change
                    Sample("settled");
                    Drive.Scan("settled");
                    Drive.MirrorDump();
                    _shots++;
                    Drive.Shot(_shotDir, "p36_engine_text_native.png");   // backup tile (native res)
                    Drive.WriteFile(_ready, "READY tag=" + _tag + " frame=" + Time.frameCount
                        + " t=" + Time.time.ToString("0.00") + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
                    Next();
                    return;

                // ---- 04 wait for the run script's GO (it captured 1920x1080 from the CLI) --------
                case 5:
                    if (Drive.FileExists(_go)) { Drive.Log("GO-SEEN file=" + _go); Next(); return; }
                    if (Elapsed(120f)) { Drive.Log("CHAIN go-file-never-appeared"); Next(); }
                    return;

                // ---- 05 in-session equivalence probe + final scan --------------------------------
                case 6:
                    if (!Elapsed(0.3f)) return;
                    Drive.Log("EQUIV-STEP result=" + Drive.Equivalence());
                    Drive.Scan("after-equiv");
                    Drive.MirrorDump();
                    Finish("done");
                    return;

                default:
                    Finish("end");
                    return;
            }
        }

        private void OnApplicationQuit()
        {
            Finish("appquit");
        }
    }
}
