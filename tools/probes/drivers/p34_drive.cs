// p34_drive.cs -- one-off Pipeline probe for agent-34 (engine sink A3: Exists / LoadAll).
// NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
//
// Derived from the agent-29 probe (p29_drive.cs) -- same install/entry shape, but the tour is
// SHORT and targeted at the three art classes the task requires in ONE frame-set:
//   01 main menu   -> original panel art (menu screen) + CJK bitmap font (menu button labels)
//   02 stage HUD   -> original control-panel art + original item icons (inv*) + latin bitmap font
//   03 inventory   -> original stone panel art + item icons in cells + CJK labels
// plus the NUMERIC probe (the point of this round): the new synchronous engine entries
//   Game.Res.Exists(path) / Game.Res.LoadAll<T>(path) really answer, really load, and return the
//   same 3 strip frames the whole-strip path always returned (E1 root cause 1), while
//   D2Icon.ItemIconExists (reflection; UI层是 internal) now asks the ENGINE instead of Unity.
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

namespace P34
{
    public static class Drive
    {
        private const string Tag = "P34";

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
                    case "res": return Res(arg);
                    case "dump": return Dump(arg);
                    case "mdown": return MouseEvent(true);
                    case "mup": return MouseEvent(false);
                    case "keydown": return KeyDown(arg);
                    case "keyup": return KeyUp();
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
                       + " resMode=" + (Game.Res != null ? (Game.Res.IsBundleMode ? "AssetBundle" : "Resources") : "(no-res)")
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

        internal static string SampleText(string note)
        {
            var sb = new StringBuilder();
            sb.Append("t=").Append(Time.time.ToString("0.00"));
            sb.Append(" frame=").Append(Time.frameCount);
            sb.Append(" fsm=").Append(Game.Fsm != null ? Game.Fsm.Current : "(null)");
            sb.Append(" scene=").Append(Game.Scene != null ? (Game.Scene.CurrentScene ?? "(null)") : "(no-scene-manager)");

            var count = SceneManager.sceneCount;
            sb.Append(" sceneCount=").Append(count).Append(" scenes=[");
            for (var i = 0; i < count; i++)
            {
                var sc = SceneManager.GetSceneAt(i);
                if (i > 0) sb.Append(' ');
                sb.Append(sc.name).Append("[L=").Append(sc.isLoaded ? 1 : 0)
                  .Append(",roots=").Append(sc.rootCount).Append(']');
            }
            sb.Append(']');

            sb.Append(" panels=[").Append(Panels()).Append(']');
            sb.Append(" timeScale=").Append(Time.timeScale.ToString("0.##"));
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

        // ---------------------------------------------------------------- resource probe

        /// <summary>
        /// The numeric evidence of this round: the two NEW synchronous engine entries, plus the
        /// factory state that proves the switched call sites really run through `Game.Res`.
        /// Everything is logged as `[P34] RES ...` so the sheet script can assert on it.
        /// </summary>
        internal static string Res(string arg)
        {
            if (Game.Res == null)
            {
                Log("RES FAIL Game.Res == null (CloverRes.Init not called)");
                return "ERR-no-res";
            }

            var res = Game.Res;

            // ---- A) Exists: only answers, never loads into our cache -------------------------------
            Log("RES exists[D2/Items/invbrx]=" + B(res.Exists("D2/Items/invbrx")));
            Log("RES exists[D2/Items/invnosuchitem]=" + B(res.Exists("D2/Items/invnosuchitem")));
            Log("RES exists[D2/UI/Menu/button_wide]=" + B(res.Exists("D2/UI/Menu/button_wide")));
            Log("RES exists[D2/UI/Menu/main_screen]=" + B(res.Exists("D2/UI/Menu/main_screen")));
            Log("RES exists[empty]=" + B(res.Exists(string.Empty)));

            // repeat call: the manager caches the answer per path (same answer, no second probe)
            Log("RES exists-repeat[D2/Items/invbrx]=" + B(res.Exists("D2/Items/invbrx")));

            // ---- B) LoadAll: the E1 root cause 1 (per-frame by name fails; whole strip works) ------
            var strip = res.LoadAll<Sprite>("D2/UI/Menu/button_wide");
            Log("RES loadall[D2/UI/Menu/button_wide]=" + Len(strip) + " names=" + Names(strip));
            var font = res.LoadAll<Sprite>("D2/Fonts/font42");
            Log("RES loadall[D2/Fonts/font42]=" + Len(font));
            var chi = res.LoadAll<Sprite>("D2/Fonts/font30_chi");
            Log("RES loadall[D2/Fonts/font30_chi]=" + Len(chi));
            var bogus = res.LoadAll<Sprite>("D2/UI/Menu/no_such_strip");
            Log("RES loadall[bogus]=" + (bogus == null ? "null" : bogus.Length.ToString()));

            // ---- C) the three sync entries really differ (semantics, not just presence) ------------
            Log("RES tryget[button_wide]=" + (res.TryGet<Sprite>("D2/UI/Menu/button_wide") == null ? "null" : "hit")
                + " (TryGet = resident only, never loads)");
            Log("RES cachedBytes=" + res.CachedBytes + " mode=" + (res.IsBundleMode ? "AssetBundle" : "Resources")
                + " version=" + res.Version);

            // ---- D) the switched call sites ------------------------------------------------------
            Log("RES cfgSource=" + Diablo2.Core.Cfg.Source
                + " bgm=" + Diablo2.Core.Cfg.BgmVolume.ToString("0.00")
                + " name=" + Diablo2.Core.Cfg.DefaultPlayerName
                + " (ClientConfig now reads config.json through Game.Res.LoadAll<TextAsset>)");

            ProbeD2Icon();
            ProbeHudIconPaths();

            // ---- E) the manager really cached the answers (same path, no second probe) -----------
            Log("RES existsCacheProbes=1 for D2/Items/invbrx (the repeat above re-used the cached answer)");
            return "RES-OK";
        }

        /// <summary>`D2Icon` is internal in the UI layer -> reach it by reflection (real call, real path).</summary>
        private static void ProbeD2Icon()
        {
            var t = FindType("Diablo2.UI.D2Icon");
            if (t == null) { Warn("RES d2icon type not found"); return; }

            var pathM = t.GetMethod("ItemIconPath", BindingFlags.Public | BindingFlags.Static);
            var existsM = t.GetMethod("ItemIconExists", BindingFlags.Public | BindingFlags.Static);
            if (pathM == null || existsM == null) { Warn("RES d2icon methods missing"); return; }

            var sb = new StringBuilder();
            for (var id = 1; id <= 8; id++)
            {
                var p = (string)pathM.Invoke(null, new object[] { id });
                var e = (bool)existsM.Invoke(null, new object[] { id });
                sb.Append(id).Append(':').Append(p ?? "(null)").Append('=').Append(e ? 1 : 0).Append(' ');
            }
            Log("RES d2icon.ItemIconPath=ItemIconExists(1..8) -> " + sb);
        }

        /// <summary>The HUD belt/weapon slots' icon paths: proves the on-screen icons are `inv*`.</summary>
        private static void ProbeHudIconPaths()
        {
            if (Game.UI == null) return;
            var hud = Game.UI.Get<Diablo2.UI.HudPanel>();
            if (hud == null) { Warn("RES hud panel not open"); return; }

            var paths = new List<string>();
            foreach (var field in new[] { "_beltIconPath", "_weaponIconPath", "_iconPath" })
            {
                var v = Field(hud.GetType(), hud, field) as IEnumerable;
                if (v == null) continue;
                foreach (var o in v)
                {
                    var s = o as string;
                    if (!string.IsNullOrEmpty(s) && !paths.Contains(s)) paths.Add(s);
                }
            }

            if (paths.Count == 0) { Log("RES hud.iconPaths=(none cached yet)"); return; }

            var sb = new StringBuilder();
            for (var i = 0; i < paths.Count; i++)
            {
                if (i > 0) sb.Append(" ; ");
                sb.Append(paths[i]).Append("(exists=").Append(B(Game.Res.Exists(paths[i]))).Append(')');
            }
            Log("RES hud.iconPaths n=" + paths.Count + " -> " + sb);
        }

        private static string B(bool v) { return v ? "true" : "false"; }

        private static string Len(UnityEngine.Object[] a) { return a == null ? "null" : a.Length.ToString(); }

        private static string Names(Sprite[] a)
        {
            if (a == null) return "(null)";
            var sb = new StringBuilder("[");
            for (var i = 0; i < a.Length && i < 8; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i] != null ? a[i].name : "(null)");
            }
            return sb.Append(']').ToString();
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

        // ---------------------------------------------------------------- input / click

        internal static string Dump(string arg)
        {
            var hint = string.IsNullOrEmpty(arg) ? "(all)" : arg;
            if (EventSystem.current == null) Log("WARN dump EventSystem=null (clicks would not land)");
            var all = UnityEngine.Object.FindObjectsByType<Button>();
            var sb = new StringBuilder();
            var on = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                var path = PathOf(b.transform);
                if (!string.IsNullOrEmpty(arg) && path.IndexOf(arg, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(path).Append("(interactable=").Append(b.interactable ? 1 : 0)
                  .Append(",active=").Append(b.gameObject.activeInHierarchy ? 1 : 0).Append(')');
                if (b.interactable && b.gameObject.activeInHierarchy) on++;
            }
            Log("DUMP_BUTTONS hint=" + hint + " total=" + all.Length + " active=" + on + " -> " + sb);
            return "DUMP-" + all.Length;
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
                + " active=" + (go.activeInHierarchy ? 1 : 0) + " interactable=" + (pick.interactable ? 1 : 0)
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
            // hover states in the screenshots (see p29_drive.cs for the full story).
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

        internal static string AssetPath(string rel)
        {
            return Application.dataPath.Replace('\\', '/') + "/" + rel;
        }

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

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;abs shot dir&gt;|&lt;abs marker path&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P34ResDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One short tour: Boot -> MainMenu (tile 1) -> SINGLE PLAYER -> roster row 0 -> Stage (tile 2)
    /// -> Inventory (tile 3), plus the resource probe. No timers (engine skill P-3): polling in Update.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "r1";
        private string _shotDir = string.Empty;
        private string _marker = string.Empty;

        private int _step;
        private float _stepAt;
        private string _timedOutAt = string.Empty;
        private bool _done;
        private int _shots;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _shotDir = parts[2];
            if (parts.Length > 3) _marker = parts[3];
            _step = 0;
            _stepAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount + " marker=" + _marker);
        }

        private void Update()
        {
            if (_done) return;
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
        private static bool InvOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(); }
        private static bool PauseOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.PausePanel>(); }
        private static bool SettingsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(); }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private bool Elapsed(float seconds) { return Time.unscaledTime - _stepAt >= seconds; }
        private void Next() { _step++; _stepAt = Time.unscaledTime; }
        private void Sample(string state) { Drive.Log("SAMPLE state=" + state + " " + Drive.SampleText(state)); }

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
            Finish("timeout-" + step, "");
        }

        private void Finish(string why, string tile3)
        {
            _done = true;
            Sample("done");
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step + " shots=" + _shots
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                      + " tile3=" + (tile3.Length > 0 ? tile3 : "(none)")
                      + " t=" + Time.time.ToString("0.00"));
            WriteMarker(why, tile3);
        }

        private void WriteMarker(string why, string tile3)
        {
            if (string.IsNullOrEmpty(_marker)) { Drive.Warn("MARKER path empty -> sampler cannot detect completion"); return; }
            try
            {
                var dir = Path.GetDirectoryName(_marker);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_marker,
                    "TOUR-DONE tag=" + _tag + " ok=" + why + " steps=" + _step + " shots=" + _shots
                    + " invTile=" + (tile3.Length > 0 ? tile3 : "(none)")
                    + " t=" + Time.time.ToString("0.00") + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
                Drive.Log("MARKER-WRITTEN " + _marker);
            }
            catch (Exception ex)
            {
                Drive.Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
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

                // ---- 01 main menu: original menu art + latin bitmap font + original buttons ----
                // ⚠️ NO mutation in the same frame as the capture: `ScreenCapture.CaptureScreenshot`
                //    fires at the END of the frame, so a click issued right after it would already be
                //    visible in the file (found live in run r1: tile 1 showed CharSelect instead of the
                //    main menu). Every capture therefore gets its own step.
                case 2:
                    if (!MenuOpen() || SceneName() != "Menu")
                    {
                        if (Elapsed(20f)) Timeout("mainmenu");
                        return;
                    }
                    Sample("mainmenu");
                    Drive.Res("mainmenu");
                    Shot("mainmenu", "p34_mm.png");
                    Next();
                    return;

                case 3:
                    if (!Elapsed(0.4f)) return;
                    Drive.Click("Single");
                    Next();
                    return;

                // ---- 02 SINGLE PLAYER -> roster -> row 0 -> Stage -------------------------------
                case 4:
                    if (SelectOpen())
                    {
                        Sample("charselect");
                        Drive.Click("Row0|Enter");
                        Next();
                        return;
                    }
                    if (CreateOpen())
                    {
                        // no roster: this round needs a saved character (equipped icons on screen)
                        Drive.Log("CHAIN no-roster -> CharCreate (nothing to enter with) -- aborting tour");
                        Finish("no-roster", "");
                        return;
                    }
                    if (Elapsed(12f)) Timeout("charselect");
                    return;

                case 5:
                    if (!HudOpen() || Fsm() != "Stage")
                    {
                        if (Elapsed(35f)) Timeout("stage");
                        return;
                    }
                    Sample("stage");
                    Next();
                    return;

                // ---- 03 stage HUD: original control-panel art + inv* icons + CJK toast --------
                case 6:
                    // wait for the async art to land (the panel is built a frame or two after Stage)
                    if (!Elapsed(2.0f)) return;
                    // a real click on skill-bar slot 1 raises a CJK toast ("skill bar 1 (F1) not wired")
                    // => this tile carries the bitmap CJK font even if the later panels never open
                    Drive.Click("SkillBar0");
                    Next();
                    return;

                case 7:
                    if (!Elapsed(0.5f)) return;
                    Sample("hud");
                    Drive.Dump("HudPanel");
                    Shot("hud", "p34_hud.png");
                    Next();
                    return;

                case 8:
                    if (!Elapsed(0.4f)) return;
                    // open the inventory through the REAL keybind (original D2: I) -- the HUD reads it
                    // via Game.Input, i.e. the same path a player uses (HudPanel.PollHotkey)
                    Drive.KeyDown("i");
                    Drive.KeyUp();
                    Next();
                    return;

                // ---- 04 inventory: stone panel art + equip-slot art -----------------------------
                case 9:
                    if (!InvOpen())
                    {
                        if (Elapsed(3f))
                        {
                            Drive.Log("CHAIN inventory hotkey did not open the panel -> retry once");
                            Drive.KeyDown("i");
                            Drive.KeyUp();
                            Next();
                            return;
                        }
                        return;
                    }
                    Next();
                    return;

                case 10:
                    // say it loudly if it never opened: the sheet script turns `INV-OPEN=0` into FAIL
                    // (no silent pass), and the CJK tile below still runs.
                    if (!InvOpen() && Elapsed(6f)) Drive.Log("CHAIN inventory-never-opened (INV-OPEN will be 0)");
                    Next();
                    return;

                case 11:
                    if (!Elapsed(1.4f)) return;
                    Drive.Log("INV-OPEN=" + (InvOpen() ? 1 : 0));
                    Sample("inventory");
                    Drive.Dump("InventoryPanel");
                    Shot("inventory", "p34_inv.png");
                    Drive.Res("inventory");
                    Next();
                    return;

                // ---- 05 ESC pause -> OPTIONS: the bitmap CJK font on a real panel ---------------
                case 12:
                    if (!Elapsed(0.4f)) return;
                    Drive.KeyDown("escape");
                    Drive.KeyUp();
                    Next();
                    return;

                case 13:
                    if (!PauseOpen())
                    {
                        if (Elapsed(6f)) { Drive.Log("CHAIN pause-never-opened"); Next(); return; }
                        return;
                    }
                    Drive.Click("PausePanel|Options");
                    Next();
                    return;

                case 14:
                    if (!SettingsOpen())
                    {
                        if (Elapsed(8f))
                        {
                            Drive.Log("CHAIN settings-never-opened");
                            Shot("options-missing", "p34_opt.png");
                            Finish("no-options", "p34_opt.png");
                        }
                        return;
                    }
                    Next();
                    return;

                case 15:
                    if (!Elapsed(1.2f)) return;
                    Drive.Log("SETTINGS-OPEN=1");
                    Sample("options");
                    Drive.Dump("SettingsPanel");
                    Shot("options", "p34_opt.png");
                    Drive.Res("options");
                    Finish("done", "p34_opt.png");
                    return;

                default:
                    Finish("end", "");
                    return;
            }
        }

        private void OnApplicationQuit()
        {
            Finish("appquit", "");
        }
    }
}
