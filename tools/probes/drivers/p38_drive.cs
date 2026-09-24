//                 YELLOW SQUARE").
// NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
//
// is SHORT and targeted at ONE numeric assertion, plus ONE camera capture:
//   Boot -> MainMenu -> SINGLE PLAYER -> roster row 0 ENTER -> Stage (town, Amazon)
//     -> settle (let the first idle group finish loading)
//     -> read baseline A = ViewModule.PlaceholderTicksOf(GameConst.PlayerEntityId)
//     -> walk the player for >= 4 s across SEVERAL of the 8 directions (AppContext.I.Player.MoveTo)
//        while sampling: Dir / SpriteRenderer.sprite.name / Anim frame / placeholder ticks
//     -> write READY mid-walk (the run script captures 1920x1080 from the CLI while we still walk)
//     -> read B  ==>  ASSERT B - A == 0   (pre-fix this is ALWAYS > 0: every first-touch frame key made
//        ApplyFrame swap the renderer to SpriteFrames.Placeholder == white 40x79 tinted by the class
//        placeholder colour, Amazon = new Color(0.95f, 0.80f, 0.25f) == YELLOW)
//     -> also assert sprite.name != "D2CharPlaceholder" on every sample taken while walking.
//
// WHY A MONOBEHAVIOUR AND NO TIMER: the chain needs wall-clock pacing across many frames (walk >= 4 s,
// with a CLI capture in the middle) -- polling Time.unscaledTime in Update is the only shape that can
// hold a frame for the run script (see p36_drive.cs for the same handshake). No engine timer is created.
//
// ASCII ONLY (PS 5.1 / Roslyn both read a BOM-less non-ASCII file as ANSI).
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

namespace P38
{
    public static class Drive
    {
        private const string Tag = "P38";

        /// <summary>Sprite name of `SpriteFrames.Placeholder` (the square that used to flash).</summary>
        public const string PlaceholderSpriteName = "D2CharPlaceholder";

        /// <summary>Player entity id (`Diablo2.Core.GameConst.PlayerEntityId` == 1).</summary>
        private const int PlayerEntityId = 1;

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
                    case "player": return PlayerLine("on-demand");
                    case "ticks": return "ticks=" + PlayerTicks();
                    case "animdump": return AnimDump();
                    case "paths": return Paths(arg);
                    case "click": return Click(arg);
                    case "keydown": return KeyDown(arg);
                    case "keyup": return KeyUp();
                    case "mdown": return MouseEvent(true);
                    case "mup": return MouseEvent(false);
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
                       + " ticksOfPlayer=" + PlayerTicks()
                       + " probeMarker=PlaceholderTicksOf(" + (PlayerTicks() != -2 ? "present" : "MISSING") + ")";
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
            sb.Append(" ");
            sb.Append(PlayerLine("pl"));
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
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.HudPanel>(), "Hud");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(), "Inventory");
            return sb.ToString();
        }

        private static void AddIf(StringBuilder sb, bool on, string name)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }

        // ---------------------------------------------------------------- the numeric probe

        /// <summary>
        /// ViewModule.PlaceholderTicksOf(entityId) -- the static counter added by this round (it is
        /// `internal static`, i.e. NOT on IViewModule, so it is reached by reflection).
        /// Returns -1 (type missing) / -2 (method missing) / -3 (invoke failed) so a broken probe can
        /// never look like a passing assertion.
        /// </summary>
        internal static int PlayerTicks()
        {
            return TicksOf(PlayerEntityId);
        }

        internal static int TicksOf(int entityId)
        {
            try
            {
                var t = FindType("Diablo2.Module.View.ViewModule");
                if (t == null) return -1;
                var m = t.GetMethod("PlaceholderTicksOf", BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) return -2;
                return (int)m.Invoke(null, new object[] { entityId });
            }
            catch (Exception ex)
            {
                Log("TICKS-FAIL id=" + entityId + " ex=" + ex.GetType().Name + ": " + ex.Message);
                return -3;
            }
        }

        internal static Diablo2.Module.IPlayerModule PlayerMod()
        {
            return CtxMember("Player") as Diablo2.Module.IPlayerModule;
        }

        internal static Diablo2.Module.IMapModule MapMod()
        {
            return CtxMember("Map") as Diablo2.Module.IMapModule;
        }

        private static object CtxMember(string name)
        {
            var ctx = Ctx();
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) : null;
        }

        private static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            return p != null ? p.GetValue(null) : null;
        }

        /// <summary>Current sprite name on the player's SpriteRenderer (the visible pixels).</summary>
        internal static string PlayerSpriteName()
        {
            try
            {
                var vm = CtxMember("View") as Diablo2.Module.IViewModule;
                if (vm == null) return "(no-viewmod)";
                var go = vm.GetView(PlayerEntityId);
                if (go == null) return "(no-view)";
                var sr = go.GetComponent<SpriteRenderer>();
                if (sr == null) return "(no-sr)";
                var sp = sr.sprite;
                if (sp == null) return "(null)";
                return sp.name;
            }
            catch (Exception ex)
            {
                return "(sprite-ex:" + ex.GetType().Name + ")";
            }
        }

        /// <summary>Player animator state ("Playing#frame/count") reached by reflection (field proof).</summary>
        internal static string PlayerAnimText()
        {
            try
            {
                var vm = CtxMember("View");
                if (vm == null) return "(no-viewmod)";
                var pf = vm.GetType().GetField("_player", BindingFlags.NonPublic | BindingFlags.Instance);
                var view = pf != null ? pf.GetValue(vm) : null;
                if (view == null) return "(no-player-view)";
                return RowText(view, false);
            }
            catch (Exception ex)
            {
                return "(anim-ex:" + ex.GetType().Name + ")";
            }
        }

        /// <summary>
        /// One line of everything that decides "is a placeholder on screen right now" + the frame key,
        /// so a failing assertion can name the entity and the key instead of guessing.
        /// </summary>
        internal static string PlayerLine(string tag)
        {
            var p = PlayerMod();
            if (p == null) return tag + " player=(null) dirs=none";
            var g = p.Grid;
            return tag + " grid=(" + g.x + "," + g.y + ") dir=" + p.Dir + " moving=" + (p.IsMoving ? 1 : 0)
                   + " dead=" + (p.IsDead ? 1 : 0)
                   + " anim=" + PlayerAnimText()
                   + " sprite=" + PlayerSpriteName()
                   + " ticks=" + PlayerTicks()
                   + " t=" + Time.time.ToString("0.00");
        }

        /// <summary>Per-entity dump of the view records (entities + npcs): key / placeholder / sprite.</summary>
        internal static string AnimDump()
        {
            var vm = CtxMember("View");
            if (vm == null) { Log("ANIMDUMP AppContext.View == null"); return "ERR-no-view"; }

            var n = 0;
            n += DumpDict(vm, "_entities");
            n += DumpDict(vm, "_npcs");
            Log("ANIMDUMP rows=" + n);
            return "rows=" + n;
        }

        private static int DumpDict(object vm, string fieldName)
        {
            var f = vm.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            var dict = f != null ? f.GetValue(vm) as IEnumerable : null;
            if (dict == null) { Log("ANIMDUMP field " + fieldName + " missing"); return 0; }
            var n = 0;
            foreach (var kv in dict)
            {
                var kt = kv.GetType();
                var key = kt.GetProperty("Key").GetValue(kv);
                var view = kt.GetProperty("Value").GetValue(kv);
                Log("ANIMROW " + fieldName + " " + (view == null ? "value=null" : RowText(view, true)));
                n++;
            }
            return n;
        }

        private static string RowText(object view, bool withId)
        {
            var ty = view.GetType();
            var id = FieldOf(ty, view, "EntityId");
            var playing = FieldOf(ty, view, "Playing");
            var ph = FieldOf(ty, view, "UsingPlaceholder");
            var nfr = FieldOf(ty, view, "NeedsFrameRefresh");
            var dir = FieldOf(ty, view, "Dir");

            var anim = FieldOf(ty, view, "Anim");
            object key = null, frame = null, frames = null;
            if (anim != null)
            {
                key = PropOf(anim.GetType(), anim, "CurrentKey");
                frame = PropOf(anim.GetType(), anim, "FrameIndex");
                frames = PropOf(anim.GetType(), anim, "FrameCount");
            }

            var spriteName = "(no-sr)";
            var sr = FieldOf(ty, view, "Renderer");
            if (sr != null)
            {
                var sp = PropOf(sr.GetType(), sr, "sprite") as UnityEngine.Object;
                spriteName = sp == null ? "(null)" : sp.name;
            }

            var ticks = withId ? TicksOf(System.Convert.ToInt32(id)) : TicksOf(PlayerEntityId);
            return "id=" + id + " dir=" + dir + " playing=" + playing
                   + " key=" + (key ?? "(null)") + " frame=" + frame + "/" + frames
                   + " placeholder=" + ph + " nfr=" + nfr + " sprite=" + spriteName
                   + " ticks=" + ticks;
        }

        private static object FieldOf(Type t, object o, string name)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }

        private static object PropOf(Type t, object o, string name)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p != null ? p.GetValue(o) : null;
        }

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

            var all = UnityEngine.Object.FindObjectsByType<Button>();
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
            Log("CLICK name=" + want + " path=" + path + " candidates=" + picks.Count
                + " raycastTop=" + top + " hitSelf=" + (hits.Count > 0 && hits[0].gameObject == go ? 1 : 0));

            if (!pick.interactable || !go.activeInHierarchy)
            {
                Log("CLICK-SKIP name=" + want + " path=" + path + " (not interactable / inactive)");
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
    }

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;shot dir&gt;|&lt;ready&gt;|&lt;go&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P38MoveFlashDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                      + " ticksMarker=" + Drive.PlayerTicks());
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One short tour: Boot -> MainMenu -> SINGLE PLAYER -> roster row 0 ENTER -> Stage -> settle ->
    /// baseline A -> walk (>= 4 s, several of the 8 directions) -> READY (run script captures) -> B ->
    /// ASSERT B - A == 0. No engine timers (engine skill P-3): polling in Update.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        /// <summary>Walk targets: the 8 grid steps of the original 8-direction animation.</summary>
        private static readonly int[,] Dirs8 = new int[,]
        {
            { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 },
        };

        /// <summary>Seconds to let the first animation group load before the baseline is read.</summary>
        private const float SettleSeconds = 5.0f;

        /// <summary>Seconds of continuous walking required by the task (>= 4 s; 8 s gives margin).</summary>
        private const float WalkSeconds = 8.0f;

        /// <summary>Re-issue MoveTo this often (the A* path of the previous target may already be done).</summary>
        private const float MoveEvery = 0.45f;

        /// <summary>Sample Dir / sprite / key / ticks this often while walking.</summary>
        private const float SampleEvery = 0.25f;

        private string _tag = "r1";
        private string _shotDir = string.Empty;
        private string _ready = string.Empty;
        private string _go = string.Empty;
        private string _done = string.Empty;

        private int _step;
        private float _stepAt;
        private string _timedOutAt = string.Empty;
        private bool _doneFlag;

        // ---- the numeric assertion state -------------------------------------------------------
        private int _baselineA = int.MinValue;
        private int _finalB = int.MinValue;
        private bool _walking;
        private float _walkStart;
        private float _lastMoveAt;
        private float _lastSampleAt;
        private int _dirIndex;
        private int _moveCount;
        private int _samples;
        private int _placeholderNameSamples;
        private int _resolvedSamples;
        private int _walkMoveFails;
        private readonly List<string> _dirSeen = new List<string>();
        private readonly List<string> _spriteSeen = new List<string>();
        private int _preBaselineOk;
        private string _preBaselineSprite = "(none)";

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
            _lastSampleAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount
                      + " ready=" + _ready + " go=" + _go + " done=" + _done);
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
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
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
            Drive.AnimDump();
            Sample("done");
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step
                      + " baselineA=" + _baselineA + " finalB=" + _finalB
                      + " delta=" + Delta(why)
                      + " samples=" + _samples + " moveCount=" + _moveCount
                      + " placeholderNameSamples=" + _placeholderNameSamples
                      + " resolvedSamples=" + _resolvedSamples
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                      + " t=" + Time.time.ToString("0.00"));
            Drive.WriteFile(_done, "TOUR-DONE tag=" + _tag + " ok=" + why
                + " baselineA=" + _baselineA + " finalB=" + _finalB + " delta=" + Delta(why)
                + " samples=" + _samples + " placeholderNameSamples=" + _placeholderNameSamples
                + " resolvedSamples=" + _resolvedSamples + " moveCount=" + _moveCount
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private string Delta(string why)
        {
            if (_baselineA == int.MinValue || _finalB == int.MinValue) return "(na:" + why + ")";
            return (_finalB - _baselineA).ToString();
        }

        // ---------------------------------------------------------------- walking

        /// <summary>
        /// Next walk target: the farthest walkable grid along the next of the 8 directions, so the player
        /// really crosses several of them (each direction is its own 8-frame group == 8 new frame keys).
        /// </summary>
        private Vector2Int NextTarget()
        {
            var map = Drive.MapMod();
            var p = Drive.PlayerMod();
            if (p == null) return Vector2Int.zero;
            var from = p.Grid;
            if (map == null || !map.IsGenerated) return from;

            var start = _dirIndex % 8;
            _dirIndex++;
            for (var i = 0; i < 8; i++)
            {
                var d = (start + i) % 8;
                var dx = Dirs8[d, 0];
                var dy = Dirs8[d, 1];
                for (var r = 14; r >= 4; r--)
                {
                    var t = new Vector2Int(from.x + dx * r, from.y + dy * r);
                    if (map.InBounds(t) && map.Walkable(t)) return t;
                }
            }
            Drive.Log("WALK-NOTARGET from=(" + from.x + "," + from.y + ") map=" + map.Width + "x" + map.Height);
            return from;
        }

        private void BeginWalk()
        {
            _walking = true;
            _walkStart = Time.unscaledTime;
            _lastMoveAt = -999f;
            _lastSampleAt = Time.unscaledTime;
            _dirIndex = 0;
            Drive.Log("WALK-BEGIN " + Drive.PlayerLine("walk-begin")
                      + " planSeconds=" + WalkSeconds.ToString("0.0"));
        }

        private void TickWalk()
        {
            if (!_walking) return;
            var now = Time.unscaledTime;

            if (now - _lastMoveAt >= MoveEvery)
            {
                _lastMoveAt = now;
                var p = Drive.PlayerMod();
                if (p != null)
                {
                    var before = p.Grid;
                    var t = NextTarget();
                    p.MoveTo(t);
                    _moveCount++;
                    if (t == before) _walkMoveFails++;
                    Drive.Log("WALK-MOVE #" + _moveCount + " from=(" + before.x + "," + before.y + ")"
                              + " to=(" + t.x + "," + t.y + ") " + Drive.PlayerLine("move"));
                }
            }

            if (now - _lastSampleAt >= SampleEvery)
            {
                _lastSampleAt = now;
                _samples++;
                var p = Drive.PlayerMod();
                if (p != null)
                {
                    var d = p.Dir.ToString();
                    if (!_dirSeen.Contains(d)) _dirSeen.Add(d);
                }
                var sprite = Drive.PlayerSpriteName();
                if (!_spriteSeen.Contains(sprite)) _spriteSeen.Add(sprite);
                if (sprite == Drive.PlaceholderSpriteName) _placeholderNameSamples++;
                else if (sprite != "(no-view)" && sprite != "(null)" && sprite != "(no-sr)") _resolvedSamples++;

                Drive.Log("WALK-SAMPLE #" + _samples + " dirsSeen=" + string.Join(",", _dirSeen.ToArray())
                          + " " + Drive.PlayerLine("sample"));
            }
        }

        private bool WalkElapsed(float seconds)
        {
            return _walking && Time.unscaledTime - _walkStart >= seconds;
        }

        // ---------------------------------------------------------------- the tour

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

                // ---- 01 main menu: the roster lives behind SINGLE PLAYER ------------------------
                case 2:
                    if (!MenuOpen() || SceneName() != "Menu")
                    {
                        if (Elapsed(25f)) Timeout("mainmenu");
                        return;
                    }
                    Sample("mainmenu");
                    Next();
                    return;

                case 3:
                    if (!Elapsed(0.4f)) return;
                    Drive.Click("Single");
                    Next();
                    return;

                // ---- 02 SINGLE PLAYER -> roster row 0 -> ENTER (a saved Amazon "HeroP27A") --------
                case 4:
                    if (SelectOpen())
                    {
                        Sample("charselect");
                        Drive.AnimDump();
                        Drive.Click("Row0|Enter");
                        Next();
                        return;
                    }
                    if (CreateOpen())
                    {
                        // no roster: this round must not create a character (the defect is about the
                        // already-shipped walk animation of an existing hero)
                        Drive.Log("CHAIN no-roster -> CharCreate; aborting (a saved hero is required)");
                        Finish("no-roster");
                        return;
                    }
                    if (Elapsed(12f)) Timeout("charselect");
                    return;

                // ---- 03 Stage: wait for the HUD, then settle ------------------------------------
                case 5:
                    if (!HudOpen() || Fsm() != "Stage")
                    {
                        if (Elapsed(45f)) Timeout("stage");
                        return;
                    }
                    Sample("stage-arrived");
                    Drive.PlayerLine("stage-arrived");
                    Next();
                    return;

                // ---- 04 settle: let the first (idle) group finish loading, then take baseline A --
                case 6:
                    if (!Elapsed(SettleSeconds)) return;
                    Sample("settled");
                    _preBaselineSprite = Drive.PlayerSpriteName();
                    _preBaselineOk = (_preBaselineSprite == Drive.PlaceholderSpriteName) ? 0 : 1;
                    _baselineA = Drive.PlayerTicks();
                    Drive.Log("BASELINE A=" + _baselineA
                              + " preconditionSpriteIsReal=" + _preBaselineOk
                              + " preconditionSprite=" + _preBaselineSprite
                              + " " + Drive.PlayerLine("baseline"));
                    BeginWalk();
                    Next();
                    return;

                // ---- 05 walk to the mid point, then hand the frame to the run script ------------
                case 7:
                    TickWalk();
                    if (!WalkElapsed(2.5f)) return;
                    Drive.Log("MIDWALK " + Drive.PlayerLine("midwalk"));
                    Drive.WriteFile(_ready, "READY tag=" + _tag + " frame=" + Time.frameCount
                        + " t=" + Time.time.ToString("0.00")
                        + " ticks=" + Drive.PlayerTicks()
                        + " sprite=" + Drive.PlayerSpriteName()
                        + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
                    Next();
                    return;

                // ---- 06 keep walking while the CLI captures, then finish the walk --------------
                case 8:
                    TickWalk();
                    if (Drive.FileExists(_go)) { Drive.Log("GO-SEEN file=" + _go); Next(); return; }
                    if (Elapsed(90f))
                    {
                        Drive.Log("CHAIN go-file-never-appeared -> finishing the walk without it");
                        Next();
                    }
                    return;

                case 9:
                    TickWalk();
                    if (!WalkElapsed(WalkSeconds)) return;
                    Drive.Log("WALK-END " + Drive.PlayerLine("walk-end"));
                    Next();
                    return;

                // ---- 07 read B and assert -------------------------------------------------------
                case 10:
                    _finalB = Drive.PlayerTicks();
                    var delta = _finalB - _baselineA;
                    var dirs = string.Join(",", _dirSeen.ToArray());
                    var sprites = string.Join("|", _spriteSeen.ToArray());
                    var pass = delta == 0 && _placeholderNameSamples == 0 && _resolvedSamples > 0
                               && _preBaselineOk == 1;
                    Drive.Log("VERDICT delta=" + delta
                              + " A=" + _baselineA + " B=" + _finalB
                              + " placeholderNameSamples=" + _placeholderNameSamples
                              + " resolvedSamples=" + _resolvedSamples
                              + " dirsSeen=" + dirs + " spriteNames=" + sprites
                              + " moveCount=" + _moveCount + " walkMoveFails=" + _walkMoveFails
                              + " result=" + (pass ? "PASS" : "FAIL"));
                    if (!pass)
                    {
                        Drive.Warn("VERDICT FAIL -> the root-cause reading is wrong or the fix is incomplete;"
                                   + " see the ANIMROW lines above for the entity/frame key still on the placeholder");
                        Drive.AnimDump();
                    }
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
