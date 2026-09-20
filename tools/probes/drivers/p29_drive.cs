// p29_drive.cs -- one-off Pipeline probe for agent-29 (verify the Timer id-0 tombstone fix +
// the char-create turn-over animation + every UI button, in ONE fresh Play session).
// NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
//
// Derived from the agent-28 probe (.ai-tmp/test/p28_drive.cs) -- same shape, extended:
//   * P29.Drive.Step(action, arg)   one-shot: cfg / ping / state / click / dump / text /
//                                   keydown / keyup / mdown / mup / shotdir
//   * P29.Tour.Install(spec)        installs P29.Driver (a MonoBehaviour, DontDestroyOnLoad like the
//                                   project's own App/Bootstrap.cs) that walks Boot -> MainMenu ->
//                                   SINGLE PLAYER -> CharSelect -> CharCreate (turn-over animation +
//                                   BACK/OK + name) -> Loading -> Stage (HUD button clicks) ->
//                                   ESC Pause -> OPTIONS (volume / fullscreen / quality / CLOSE) ->
//                                   MAIN MENU -> EXIT, screenshotting each state from inside the game
//                                   and writing a completion marker only IT can write.
//
// WHY A MONOBEHAVIOUR AND NO TIMER (the point of this round):
//   the defect under test only exists in a session whose FIRST CloverEngine.Timer is the engine's
//   scene-progress poller. client/_dev/p_runbg.cs registers a 1 s heartbeat timer which eats id 0
//   and therefore HIDES the defect -- so this probe creates NO timer at all (the tour is driven by
//   Time.unscaledTime polling in Update). runInBackground / vSyncCount are set here, in our own probe.
//
// ASCII ONLY (PS 5.1 / Roslyn both read a BOM-less non-ASCII file as ANSI): the two CJK node names
// of the settings panel are written as \uXXXX escapes.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace P29
{
    public static class Drive
    {
        private const string Tag = "P29";

        /// <summary>Settings panel toggle rows: node name = row label + "Toggle" (CJK label, escaped).</summary>
        public const string NodeFullscreenToggle = "\u5168\u5C4F\u663E\u793AToggle"; // full-screen row
        public const string NodeQualityToggle = "\u753B\u8D28Toggle";                 // quality row

        private static string _shotDir = string.Empty;

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
                    case "click": return Click(arg);
                    case "dump": return Dump(arg);
                    case "text": return Typed(arg);
                    case "keydown": return KeyDown(arg);
                    case "keyup": return KeyUp();
                    case "mdown": return MouseEvent(true);
                    case "mup": return MouseEvent(false);
                    case "shotdir": _shotDir = arg ?? string.Empty; Log("SHOTDIR=" + _shotDir); return _shotDir;
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

        /// <summary>Play-mode + Input System setup. Creates no timer on purpose (see file header).</summary>
        private static string Cfg()
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
            if (kb == null)
            {
                kb = InputSystem.AddDevice<Keyboard>();
                added = 1;
            }

            var mouseAdded = 0;
            if (Mouse.current == null)
            {
                InputSystem.AddDevice<Mouse>();
                mouseAdded = 1;
            }

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " bgBehavior=" + st.backgroundBehavior
                       + " editorInput=" + st.editorInputBehaviorInPlayMode
                       + " focused=" + (Application.isFocused ? 1 : 0)
                       + " keyboard=" + (kb != null ? kb.name : "(null)")
                       + " keyboardAdded=" + added
                       + " mouseAdded=" + mouseAdded
                       + " inputBackend=" + (Game.Input != null ? (Game.Input.Available ? "available" : "UNAVAILABLE") : "(no-input)")
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0);
            Log(line);
            return line;
        }

        internal static string State(string note)
        {
            var line = "SAMPLE " + SampleText(note);
            Log(line);
            return line;
        }

        /// <summary>One state sample, WITHOUT the leading "SAMPLE" token (callers add it).</summary>
        internal static string SampleText(string note)
        {
            var sb = new StringBuilder();
            sb.Append("t=").Append(Time.time.ToString("0.00"));
            sb.Append(" frame=").Append(Time.frameCount);
            sb.Append(" fsm=").Append(Game.Fsm != null ? Game.Fsm.Current : "(null)");
            sb.Append(" scene=").Append(Game.Scene != null
                ? (Game.Scene.CurrentScene ?? "(null)")
                : "(no-scene-manager)");

            var count = SceneManager.sceneCount;
            sb.Append(" sceneCount=").Append(count).Append(" scenes=[");
            var zombies = 0;
            for (var i = 0; i < count; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (i > 0) sb.Append(' ');
                sb.Append(sc.name).Append("[L=").Append(sc.isLoaded ? 1 : 0)
                  .Append(",roots=").Append(sc.rootCount).Append(']');
                if (!sc.isLoaded) zombies++;
            }
            sb.Append(']').Append(" zombies=").Append(zombies);

            sb.Append(" panels=[").Append(Panels()).Append(']');
            sb.Append(" timeScale=").Append(Time.timeScale.ToString("0.##"));
            sb.Append(" dt=").Append(Time.deltaTime.ToString("0.0000"));
            sb.Append(" dtUnscaled=").Append(Time.unscaledDeltaTime.ToString("0.0000"));
            sb.Append(" inBg=").Append(Application.runInBackground ? 1 : 0);
            sb.Append(" vSync=").Append(QualitySettings.vSyncCount);
            sb.Append(" targetFps=").Append(Application.targetFrameRate);
            sb.Append(" focused=").Append(Application.isFocused ? 1 : 0);
            sb.Append(' ').Append(TimerText());
            sb.Append(' ').Append(SceneOpText());
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
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(), "Loading");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.PausePanel>(), "Pause");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.HudPanel>(), "Hud");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(), "Settings");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(), "Inventory");
            return sb.ToString();
        }

        private static void AddIf(StringBuilder sb, bool on, string name)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }

        /// <summary>`CloverEngine.Timer` private state: the id counter and the live entries.</summary>
        internal static string TimerText()
        {
            var timer = Game.Timer;
            if (timer == null) return "timer=(null)";
            var ty = timer.GetType();
            var nextId = Field(ty, timer, "_nextId");
            var ids = new List<string>();
            var entries = Field(ty, timer, "_entries") as IEnumerable;
            if (entries != null)
            {
                foreach (var e in entries) ids.Add(Convert.ToString(Field(e.GetType(), e, "Id")));
            }

            var tombs = new List<string>();
            var toRemove = Field(ty, timer, "_toRemove") as IEnumerable;
            if (toRemove != null)
            {
                foreach (var x in toRemove) tombs.Add(Convert.ToString(x));
            }

            return "nextId=" + nextId + " entries=" + ids.Count + ":[" + string.Join(",", ids.ToArray())
                   + "] tomb=[" + string.Join(",", tombs.ToArray()) + "]";
        }

        /// <summary>`CloverEngine.SceneModule` private state: the in-flight progress op + its poller id.</summary>
        internal static string SceneOpText()
        {
            var scene = Game.Scene;
            if (scene == null) return "progTimerId=(no-scene-manager)";
            var ty = scene.GetType();
            var id = Field(ty, scene, "_progressTimerId");
            var opScene = Field(ty, scene, "_progressScene");
            var op = Field(ty, scene, "_progressOp") as AsyncOperation;

            var sb = new StringBuilder();
            sb.Append("progTimerId=").Append(id);
            sb.Append(" opScene=").Append(opScene == null ? string.Empty : Convert.ToString(opScene));
            sb.Append(" op=");
            if (op == null)
            {
                sb.Append("null");
            }
            else
            {
                sb.Append("progress=").Append(op.progress.ToString("0.###"))
                  .Append(" isDone=").Append(op.isDone ? 1 : 0)
                  .Append(" allowAct=").Append(op.allowSceneActivation ? 1 : 0);
            }
            return sb.ToString();
        }

        /// <summary>Every Button with its path + interactable / active flag (script evidence for the sweep).</summary>
        internal static string Dump(string arg)
        {
            var hint = string.IsNullOrEmpty(arg) ? "(all)" : arg;
            if (EventSystem.current == null) Log("WARN dump EventSystem=null (clicks would not land)");

            var all = UnityEngine.Object.FindObjectsByType<Button>();
            var sb = new StringBuilder();
            var on = 0;
            var off = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                var path = PathOf(b.transform);
                if (!string.IsNullOrEmpty(arg) && path.IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(path).Append("(interactable=").Append(b.interactable ? 1 : 0)
                  .Append(",active=").Append(b.gameObject.activeInHierarchy ? 1 : 0).Append(')');
                if (b.interactable && b.gameObject.activeInHierarchy) on++;
                else off++;
            }

            Log("DUMP_BUTTONS hint=" + hint + " total=" + all.Length + " listed=" + (on + off)
                + " active=" + on + " inactive=" + off + " -> " + sb);
            return "DUMP-" + (on + off);
        }

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
            if (bar >= 0)
            {
                pathSub = want.Substring(0, bar);
                want = want.Substring(bar + 1);
            }

            var all = UnityEngine.Object.FindObjectsByType<Button>();
            var picks = new List<Button>();
            var seen = new StringBuilder();
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                var p = PathOf(b.transform);
                if (pathSub.Length > 0 && p.IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                picks.Add(b);
                if (seen.Length > 0) seen.Append(" ; ");
                seen.Append(p).Append("(interactable=").Append(b.interactable ? 1 : 0).Append(')');
            }

            if (picks.Count == 0)
            {
                Log("ERR click-miss name=" + arg + " buttons=" + all.Length);
                return "ERR-click-miss";
            }

            // deepest path wins (a Confirm dialog sits above the panel that opened it)
            var pick = picks[0];
            foreach (var c in picks)
            {
                if (PathOf(c.transform).Length > PathOf(pick.transform).Length) pick = c;
            }

            var go = pick.gameObject;
            var ped = new PointerEventData(EventSystem.current);
            ped.button = PointerEventData.InputButton.Left;
            var rt = pick.transform as RectTransform;
            if (rt != null)
            {
                ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            }

            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            var top = hits.Count > 0 && hits[0].gameObject != null ? hits[0].gameObject.name : "(none)";

            var path = PathOf(go.transform);
            Log("CLICK name=" + want + " path=" + path + " candidates=" + picks.Count
                + " active=" + (go.activeInHierarchy ? 1 : 0)
                + " interactable=" + (pick.interactable ? 1 : 0)
                + " raycastTop=" + top + " hitSelf=" + (hits.Count > 0 && hits[0].gameObject == go ? 1 : 0)
                + " seen=[" + seen + "]");

            if (!pick.interactable || !go.activeInHierarchy)
            {
                Log("CLICK-SKIP name=" + want + " path=" + path + " (not interactable / inactive)");
                return "CLICK-SKIP";
            }

            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-DONE name=" + want + " path=" + path);
            return "CLICKED";
        }

        /// <summary>Queue characters through the real Input System text channel (Keyboard.onTextInput).</summary>
        internal static string Typed(string arg)
        {
            var kb = RequireKeyboard();
            if (kb == null) return "ERR-no-keyboard";
            if (string.IsNullOrEmpty(arg)) return "ERR-empty-text";

            foreach (var c in arg) InputSystem.QueueTextEvent(kb, c);
            Log("TEXT queued=\"" + arg + "\" len=" + arg.Length + " keyboard=" + kb.name);
            return "TEXT";
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
                case "backspace": key = Key.Backspace; break;
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

        /// <summary>
        /// Queue a real left-mouse state event (the Boot screen's entry is "any key OR mouse click",
        /// and the engine reads the mouse through Game.Input.GetKeyDown(GameKey.MouseLeft)).
        /// </summary>
        internal static string MouseEvent(bool down)
        {
            var mouse = Mouse.current;
            if (mouse == null)
            {
                mouse = InputSystem.AddDevice<Mouse>();
                Warn("mouse device missing -> added " + (mouse != null ? mouse.name : "(null)"));
            }
            if (mouse == null) return "ERR-no-mouse";

            // MouseState has no WithPosition helper (verified in
            // com.unity.inputsystem@7a4e1a2a8194/InputSystem/Runtime/Devices/Mouse.cs:16-124);
            // it exposes position / buttons fields and WithButton only.
            // Position is parked in the bottom-left corner on purpose: screen centre (960,540) lands
            // exactly on the Barbarian hotspot of the char-create screen and would leave that portrait
            // in its HOVER state (nu2) in every "idle" screenshot.
            var state = new MouseState { position = new Vector2(60f, 60f) };
            if (down) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
            Log("MOUSE down=" + (down ? 1 : 0) + " pos=960,540 mouse=" + mouse.name
                + " engineSaysPressed=" + (Game.Input != null ? (Game.Input.GetKey(GameKey.MouseLeft) ? 1 : 0) : -1)
                + " engineSaysDown=" + (Game.Input != null ? (Game.Input.GetKeyDown(GameKey.MouseLeft) ? 1 : 0) : -1));
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

        internal static object Field(Type ty, object obj, string name)
        {
            var f = ty.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(obj) : null;
        }

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

        /// <summary>Absolute path of an Assets-relative file (for ScreenCapture from inside the game).</summary>
        internal static string AssetPath(string rel)
        {
            return Application.dataPath.Replace('\\', '/') + "/" + rel;
        }

        /// <summary>Frame-accurate in-game capture (a CLI round trip is far too slow for a 2.16 s
        /// turn-over or the ~1.3 s read-bar screen). Returns the absolute path.</summary>
        internal static string Shot(string dir, string file)
        {
            var path = (string.IsNullOrEmpty(dir) ? AssetPath("Screenshots") : dir) + "/" + file;
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

    /// <summary>
    /// Installer. spec = "tour|&lt;tag&gt;|&lt;abs shot dir&gt;|&lt;name suffix&gt;|&lt;abs marker path&gt;".
    /// </summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P29TourDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// Walks the whole shipped flow in ONE session, screenshotting every state from inside the game,
    /// clicking every button, and finishing with EXIT (which stops Play mode -- hence it is last).
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private string _spec = string.Empty;
        private string _tag = "r1";
        private string _shotDir = string.Empty;
        private string _nameSuffix = "P29A";
        private string _marker = string.Empty;

        private int _step;
        private float _stepAt;
        private string _timedOutAt = string.Empty;
        private bool _done;
        private bool _straightToCreate;
        private bool _amazonBwShot;
        private float _hbAt;
        private int _lastInflightId = int.MinValue;
        private int _shots;

        public void Init(string spec)
        {
            _spec = spec ?? string.Empty;
            var parts = _spec.Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _shotDir = parts[2];
            if (parts.Length > 3) _nameSuffix = parts[3];
            if (parts.Length > 4) _marker = parts[4];
            _step = 0;
            _stepAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + _spec + " tag=" + _tag + " frame=" + Time.frameCount
                      + " nameSuffix=" + _nameSuffix + " marker=" + _marker);
        }

        private void Update()
        {
            _hbAt += Time.unscaledDeltaTime;
            if (_hbAt >= 2f)
            {
                _hbAt = 0f;
                Drive.Log("HB frame=" + Time.frameCount + " step=" + _step
                          + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)")
                          + " scene=" + (Game.Scene != null ? (Game.Scene.CurrentScene ?? "(null)") : "(null)")
                          + " shots=" + _shots);
            }

            if (_done) return;

            ObserveInflight();

            try
            {
                Step();
            }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        // ---------------------------------------------------------------- helpers

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool PauseOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.PausePanel>(); }
        private static bool SettingsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(); }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private string OpField(string field)
        {
            if (Game.Scene == null) return "?";
            return Convert.ToString(Drive.Field(Game.Scene.GetType(), Game.Scene, field));
        }

        private bool Elapsed(float seconds) { return Time.unscaledTime - _stepAt >= seconds; }

        private void Next() { _step++; _stepAt = Time.unscaledTime; }

        private void Sample(string state) { Drive.Log("SAMPLE state=" + state + " " + Drive.SampleText(state)); }

        /// <summary>Frame-accurate in-game screenshot of one evidence grid.</summary>
        private void Shot(string state, string file)
        {
            _shots++;
            Drive.Shot(_shotDir, file);
            Drive.Log("SHOT-GRID state=" + state + " tile=" + file + " index=" + _shots);
        }

        private void Timeout(string step)
        {
            Drive.Log("STEP-TIMEOUT step=" + step + " elapsed=" + (Time.unscaledTime - _stepAt).ToString("0.0")
                      + " " + Drive.SampleText("timeout-" + step));
            if (_timedOutAt.Length == 0) _timedOutAt = step;
            Finish("timeout-" + step);
        }

        private void Finish(string why)
        {
            _done = true;
            Sample("done");
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step + " shots=" + _shots
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                      + " t=" + Time.time.ToString("0.00"));
            WriteMarker(why);
        }

        /// <summary>The ONLY completion signal the sampler accepts: written by the probe itself.</summary>
        private void WriteMarker(string why)
        {
            if (string.IsNullOrEmpty(_marker))
            {
                Drive.Warn("MARKER path empty -> sampler cannot detect completion");
                return;
            }
            try
            {
                var dir = Path.GetDirectoryName(_marker);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_marker,
                    "TOUR-DONE tag=" + _tag + " ok=" + why + " steps=" + _step + " shots=" + _shots
                    + " nameSuffix=" + _nameSuffix + " t=" + Time.time.ToString("0.00")
                    + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
                Drive.Log("MARKER-WRITTEN " + _marker);
            }
            catch (Exception ex)
            {
                Drive.Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Frame-accurate observation of the scene module's progress poller id.</summary>
        private void ObserveInflight()
        {
            var raw = OpField("_progressTimerId");
            int id;
            if (!int.TryParse(raw, out id)) return;
            if (id == _lastInflightId) return;
            _lastInflightId = id;
            Drive.Log("INFLIGHT progTimerId=" + raw + " " + Drive.TimerText() + " " + Drive.SceneOpText()
                      + " frame=" + Time.frameCount + " t=" + Time.time.ToString("0.00"));
        }

        // ---------------------------------------------------------------- the tour

        private void Step()
        {
            switch (_step)
            {
                // ---- 01 boot screen + the "any key OR click" entry --------------------------------
                case 0:
                    if (!BootOpen())
                    {
                        if (Elapsed(30f)) Timeout("boot");
                        return;
                    }
                    Sample("boot");
                    Shot("boot", "p29_" + _tag + "_01_boot.png");
                    Drive.MouseEvent(true);             // click first: the entry must accept a click
                    Drive.MouseEvent(false);
                    Next();
                    return;

                case 1:
                    if (!Elapsed(1.2f)) return;
                    if (!BootOpen())
                    {
                        Drive.Log("BOOT-ENTRY way=mouseClick ok=1 (panel closed after a real left click)");
                    }
                    else
                    {
                        Drive.Log("BOOT-ENTRY way=mouseClick ok=0 (still on BootPanel) -> fall back to Space");
                        Drive.KeyDown("space");
                        Drive.KeyUp();
                    }
                    Next();
                    return;

                // ---- 02 main menu (this is where the id-0 tombstone used to hang the chain) --------
                case 2:
                    if (!MenuOpen() || SceneName() != "Menu")
                    {
                        if (Elapsed(20f)) Timeout("mainmenu");
                        return;
                    }
                    Sample("mainmenu");
                    Shot("mainmenu", "p29_" + _tag + "_02_mainmenu.png");
                    Next();
                    return;

                // ---- 03/04 main-menu items MULTIPLAYER / CINEMATICS (both must answer, not crash) --
                case 3:
                    Drive.Click("Multi");
                    Next();
                    return;

                case 4:
                    if (!Elapsed(0.5f)) return;
                    Sample("menu_multi_toast");
                    Shot("menu_multi_toast", "p29_" + _tag + "_03_menu_multi_toast.png");
                    Drive.Click("Cinematics");
                    Next();
                    return;

                case 5:
                    if (!Elapsed(0.5f)) return;
                    Sample("menu_cinematics_toast");
                    Shot("menu_cinematics_toast", "p29_" + _tag + "_04_menu_cinematics_toast.png");
                    Drive.Click("Single");
                    Next();
                    return;

                // ---- 05 SINGLE PLAYER -> CharSelect (with a roster) or straight to CharCreate -----
                case 6:
                    if (SelectOpen())
                    {
                        Sample("charselect_first");
                        Shot("charselect_first", "p29_" + _tag + "_05_charselect_first.png");
                        Drive.Dump("CharSelectPanel");
                        Next();
                        return;
                    }
                    if (CreateOpen())
                    {
                        Drive.Log("CHAIN no-roster: SINGLE PLAYER went straight to CharCreate "
                                  + "(that is the shipped MainMenu -> CharSelect -> CharCreate branch)");
                        _straightToCreate = true;
                        _step = 9;
                        _stepAt = Time.unscaledTime;
                        return;
                    }
                    if (Elapsed(12f)) Timeout("charselect-or-create");
                    return;

                case 7:
                    Drive.Click("CharSelectPanel|Create");
                    Next();
                    return;

                case 8:
                    if (!CreateOpen())
                    {
                        if (Elapsed(10f)) Timeout("charcreate");
                        return;
                    }
                    Next();
                    return;

                // ---- 06 char-create entry state (nothing picked, OK greyed) ----------------------
                case 9:
                    Sample(_straightToCreate ? "charcreate_direct" : "charcreate_idle");
                    Drive.Dump("CharCreatePanel");
                    Shot("charcreate_idle", "p29_" + _tag + "_06_charcreate_idle.png");
                    Next();
                    return;

                // ---- 07 BACK button of the char-create screen ------------------------------------
                case 10:
                    Drive.Click("CharCreatePanel|Back");
                    Next();
                    return;

                case 11:
                    if (!SelectOpen())
                    {
                        if (Elapsed(10f)) Timeout("back-to-charselect");
                        return;
                    }
                    Sample("charcreate_back");
                    Shot("charcreate_back", "p29_" + _tag + "_07_charcreate_back.png");
                    Next();
                    return;

                case 12:
                    Drive.Click("CharSelectPanel|Create");
                    Next();
                    return;

                case 13:
                    if (!CreateOpen())
                    {
                        if (Elapsed(10f)) Timeout("charcreate-again");
                        return;
                    }
                    Sample("charcreate_again");
                    Drive.Dump("CharCreatePanel");
                    Next();
                    return;

                // ---- 08 amazon turn-over: fw 54 frames @25fps (2.16 s), then NU3 ------------------
                case 14:
                    Drive.Click("SpotAmazon");
                    Next();
                    return;

                case 15:
                    if (!Elapsed(0.55f)) return;
                    Shot("amazon_fw_mid", "p29_" + _tag + "_08_amazon_fw_mid.png");
                    Next();
                    return;

                case 16:
                    if (!Elapsed(2.5f)) return;
                    Sample("amazon_front");
                    Shot("amazon_front", "p29_" + _tag + "_09_amazon_front.png");
                    Next();
                    return;

                // ---- 10 barbarian turn-over: fw 64 frames (2.56 s) --------------------------------
                case 17:
                    Drive.Click("SpotBarbarian");
                    Next();
                    return;

                case 18:
                    if (!Elapsed(0.85f)) return;
                    Shot("barbarian_fw_mid", "p29_" + _tag + "_10_barbarian_fw_mid.png");
                    Next();
                    return;

                case 19:
                    if (!Elapsed(3.0f)) return;
                    Sample("barbarian_front");
                    Shot("barbarian_front", "p29_" + _tag + "_11_barbarian_front.png");
                    Next();
                    return;

                // ---- 12 re-click the SAME slot: bw 19 frames (0.76 s) ----------------------------
                case 20:
                    Drive.Click("SpotBarbarian");
                    Next();
                    return;

                case 21:
                    if (!Elapsed(0.35f)) return;
                    Shot("barbarian_bw_mid", "p29_" + _tag + "_12_barbarian_bw_mid.png");
                    Next();
                    return;

                case 22:
                    if (!Elapsed(1.2f)) return;
                    Sample("idle_after_bw");
                    Shot("idle_after_bw", "p29_" + _tag + "_13_idle_after_bw.png");
                    Next();
                    return;

                // ---- 14 the 4th transition code: amazon fw again (reselect) then bw 30 (1.20 s) ----
                case 23:
                    Drive.Click("SpotAmazon");
                    Next();
                    return;

                case 24:
                    if (!Elapsed(2.4f)) return;
                    Drive.Log("TRANSITION amazon/fw replayed (54 frames @25fps = 2.16 s) on reselect");
                    Next();
                    return;

                case 25:
                    Drive.Click("SpotAmazon");          // same slot again -> BackTransition bw 30
                    Next();
                    return;

                case 26:
                    if (!_amazonBwShot && Elapsed(0.5f))
                    {
                        _amazonBwShot = true;
                        Shot("amazon_bw_mid", "p29_" + _tag + "_13b_amazon_bw_mid.png");
                        _stepAt = Time.unscaledTime;      // reopen the window for the landing log
                        return;
                    }
                    if (!Elapsed(1.0f)) return;
                    Drive.Log("TRANSITION amazon/bw played then landed on idle (30 frames @25fps = 1.20 s)");
                    Next();
                    return;

                case 27:
                    Drive.Click("SpotAmazon");          // select amazon again for the OK path
                    Next();
                    return;

                case 28:
                    if (!Elapsed(2.5f)) return;
                    Sample("amazon_selected");
                    Next();
                    return;

                // ---- 15 name + OK ----------------------------------------------------------------
                case 29:
                    Drive.Typed(_nameSuffix);
                    Next();
                    return;

                case 30:
                    if (!Elapsed(0.6f)) return;
                    Sample("named");
                    Drive.Dump("CharCreatePanel");
                    Shot("named", "p29_" + _tag + "_14_named.png");
                    Next();
                    return;

                case 31:
                    Drive.Click("CharCreatePanel|Confirm");
                    Next();
                    return;

                case 32:
                    if (!SelectOpen())
                    {
                        if (Elapsed(12f)) Timeout("charselect-after-ok");
                        return;
                    }
                    Sample("charselect_after");
                    Drive.Dump("CharSelectPanel");
                    Shot("charselect_after", "p29_" + _tag + "_15_charselect_after.png");
                    Next();
                    return;

                // ---- 16 ENTER on the first roster row -> Loading -> Stage -------------------------
                case 33:
                    // NOTE the "pathSub|name" form: the arg is NOT a node path. agent-28's probe
                    // passed the bare path here and the click silently missed (proved live in run r1:
                    // "[P29] ERR click-miss name=List/Row0/Enter" -> the chain then timed out on Loading).
                    Drive.Click("Row0|Enter");
                    Next();
                    return;

                case 34:
                    if (!LoadingOpen())
                    {
                        if (Elapsed(12f)) Timeout("loading");
                        return;
                    }
                    Sample("loading");
                    Shot("loading", "p29_" + _tag + "_16_loading.png");
                    Next();
                    return;

                case 35:
                    if (!HudOpen() || Fsm() != "Stage")
                    {
                        if (Elapsed(30f)) Timeout("stage");
                        return;
                    }
                    Sample("stage");
                    Drive.Dump("HudPanel");
                    Shot("stage", "p29_" + _tag + "_17_stage.png");
                    Next();
                    return;

                // ---- 17 HUD buttons (real clicks + their own response logs) -----------------------
                case 36:
                    Drive.Click("SkillBar0");
                    Next();
                    return;

                case 37:
                    if (!Elapsed(0.5f)) return;
                    Drive.Click("Belt0");
                    Next();
                    return;

                case 38:
                    if (!Elapsed(0.5f)) return;
                    Sample("hud_after_clicks");
                    Shot("hud_after_clicks", "p29_" + _tag + "_18_hud_after_clicks.png");
                    Next();
                    return;

                // ---- 18 ESC pause menu + OPTIONS --------------------------------------------------
                case 39:
                    Drive.KeyDown("escape");
                    Drive.KeyUp();
                    Next();
                    return;

                case 40:
                    if (!PauseOpen())
                    {
                        if (Elapsed(10f)) Timeout("pause");
                        return;
                    }
                    Sample("pause");
                    Drive.Dump("PausePanel");
                    Shot("pause", "p29_" + _tag + "_19_pause.png");
                    Next();
                    return;

                case 41:
                    Drive.Click("PausePanel|Options");
                    Next();
                    return;

                case 42:
                    if (!SettingsOpen())
                    {
                        if (Elapsed(10f)) Timeout("settings");
                        return;
                    }
                    Sample("settings");
                    Drive.Dump("SettingsPanel");
                    Shot("settings", "p29_" + _tag + "_20_settings.png");
                    Next();
                    return;

                // ---- 19 settings controls: volume -, full-screen, quality, CLOSE ------------------
                case 43:
                    Drive.Click("Minus0");
                    Next();
                    return;

                case 44:
                    if (!Elapsed(0.5f)) return;
                    Drive.Click(Drive.NodeFullscreenToggle);
                    Next();
                    return;

                case 45:
                    if (!Elapsed(0.5f)) return;
                    Drive.Click(Drive.NodeQualityToggle);
                    Next();
                    return;

                case 46:
                    if (!Elapsed(0.6f)) return;
                    Sample("settings_after_clicks");
                    Shot("settings_after_clicks", "p29_" + _tag + "_21_settings_quality_after.png");
                    Next();
                    return;

                case 47:
                    Drive.Click("SettingsPanel|Close");
                    Next();
                    return;

                case 48:
                    if (!Elapsed(0.8f)) return;
                    Sample(PauseOpen() ? "pause_after_settings_close" : "pause_after_settings_close_PAUSE-GONE");
                    Next();
                    return;

                // ---- 20 MAIN MENU (confirm dialog) -> back to the menu scene ----------------------
                case 49:
                    Drive.Click("PausePanel|ToMain");
                    Next();
                    return;

                case 50:
                    if (!Elapsed(0.7f)) return;
                    Drive.Click("Confirm/SafeArea/Panel|Confirm");
                    Next();
                    return;

                case 51:
                    if (!MenuOpen() || SceneName() != "Menu")
                    {
                        if (Elapsed(25f)) Timeout("mainmenu-after");
                        return;
                    }
                    Sample("mainmenu_after");
                    Shot("mainmenu_after", "p29_" + _tag + "_22_mainmenu_after.png");
                    Next();
                    return;

                // ---- 21 EXIT (last: it stops Play mode through EditorApplication.isPlaying=false). --
                //      The marker is written synchronously in the same frame, right after the click,
                //      so the sampler still gets its completion signal.
                case 52:
                    Drive.Click("Quit");
                    Drive.Log("EXIT-CLICKED main menu EXIT -> Events.QuitRequest -> Flow.QuitGame()");
                    Finish("exit");
                    return;

                default:
                    Finish("end");
                    return;
            }
        }
    }
}
