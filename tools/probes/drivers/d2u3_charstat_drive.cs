// =============================================================================
// d2u3_charstat_drive.cs -- slice u52play (hand-over of 片 charstat; U3 complaint #1
//   "the character stat panel: the NUMBERS are wrong, the UI display is wrong").
//
// WHY THIS FILE WAS REWRITTEN (measured, not guessed):
//   the previous version waited in phase 0 for `Game.UI.IsOpen<HudPanel>()` for 90 s while
//   the editor had only reached the MAIN MENU (Play does NOT auto-enter the stage) -> the
//   session always ended with "BLOCKED hud never opened".  The boot chain below is copied
//   from the proven `tools/probes/drivers/x_drive.cs` B1/B2/B5 stations
//   (Space on the boot screen -> main menu -> REAL mouse click on "Single" -> character
//   roster -> roster row ENTER -> stage), not reflected/short-circuited.
//
// WHAT IT ANSWERS (ONE Play session, no per-item re-entry):
//   P1  the panel as the running game really lays it out: a screenshot + EVERY visible Text
//       under `CharacterPanel` with its screen rect / font size / line count.
//   P2  `Def.PlayerStatsDto` field by field next to the on-screen value and next to the
//       OFFICIAL level-1 value (charstats.txt hpadd/stamina + the Arreat Summit class pages,
//       the same table `playercheck` section 1 uses) -> table driven TSV.
//   D1..D6  the fixed display items re-checked at runtime (value column no longer straddles
//       the separator bar, text no longer sits on the row's bottom gold line, the three
//       top-right slots stay inside their recesses, the close control is VISIBLE).
//   D7  the "add point" arrow: per-stat Image -> sprite name / alpha / screen rect /
//       interactable. This is the runtime half of the "frame 0 is transparent" question.
//
// ENTRY: Diablo2.Probes.D2U3C.Charstat.Install(spec)   spec = "<shotDir>|<tag>"
//   unity command run_script --file tools/probes/drivers/d2u3_charstat_drive.cs \
//         --entry Diablo2.Probes.D2U3C.Charstat.Install --args '["<dir>","u52run1"]'
//   (ASCII only: a BOM-less non-ASCII .cs is read as ANSI by the CLI/IDE.)
//
// ★ U3 (2026-09-24, 片 charstat, display-complaint pass): the panel's VALUE columns are drawn by
//   `D2Label` (no uGUI `Text` at all), so the uGUI-only dump used to leave every on-screen NUMBER
//   as "(missing)". Now: (a) `Text(...)` falls back to the D2Label that really draws the node,
//   (b) `DumpScreen` adds a pass over `D2Text.D2Label.Live` restricted to this panel root (text +
//   rect + fontSize + lineCount + column verdict), and (c) the flagged rows in `DumpDto` are judged
//   from the ON-SCREEN string against the official value (that is what a display complaint is
//   about), with the dto printed next to it so a logic/display split stays visible.
//
// Evidence written into <shotDir> (absolute paths, `<root>/.ai-tmp/screenshots/`):
//   d2u3_charstat_<tag>.png          screenshot of the OPEN panel
//   d2u3_charstat_<tag>.log          every station line (also Debug.Log "[D2U3C] ...")
//   d2u3_charstat_<tag>.done         DONE marker (the runner polls it)
//   u3_charstat_readings_<tag>.tsv   DTO vs on-screen vs official
//   u3_charstat_screen_<tag>.tsv     every visible Text + the geometry verdicts
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Diablo2.Probes
{
    /// <summary>Shared probe helpers (the subset of `X.Drive` this driver needs).</summary>
    internal static class Aux
    {
        internal const string Tag = "D2U3C";

        private static string _dir = string.Empty;
        private static string _done = string.Empty;
        internal static string Dir { get { return _dir; } }
        internal static string DonePath { get { return _done; } }

        // ---- logging ---------------------------------------------------------------------
        internal static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else UnityEngine.Debug.Log("[" + Tag + "] " + m);
            UnityEngine.Debug.Log("[" + Tag + "] " + m);
        }
        internal static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + m);
            UnityEngine.Debug.LogWarning("[" + Tag + "] " + m);
        }
        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static string Esc(string s)
        {
            if (s == null) return "(null)";
            return s.Replace('"', '\'').Replace("\r", " ").Replace("\n", "\\n").Replace("\t", " ");
        }
        internal static string Paths(string arg)
        {
            // spec = "<dir>|<tag>" (field 2 IS A TAG, not the done path -- measured 2026-09-24:
            // writing the tag into _done made Finish() create a file literally named "u52run2"
            // in the editor's cwd, so the runner never saw the marker and stopped the editor
            // after its full wait budget). An explicit done path may be passed as field 3.
            var p = (arg ?? string.Empty).Split('|');
            if (p.Length > 0) _dir = p[0];
            if (_dir.Length == 0) _dir = ".";
            _done = _dir + "/d2u3_charstat_" + (p.Length > 1 && p[1].Length > 0 ? p[1] : "run1") + ".done";
            if (p.Length > 2 && p[2].Length > 0) _done = p[2];
            Log("PATHS dir=" + _dir + " done=" + _done);
            return "PATHS-OK";
        }
        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
            catch (Exception e) { Warn("WRITEFILE-FAIL " + path + " " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- engine state ----------------------------------------------------------------
        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }
        internal static bool Open<T>() where T : Component, IUIPanel
        {
            try { return Game.UI != null && Game.UI.IsOpen<T>(); } catch { return false; }
        }
        internal static string State()
        {
            return "fsm=" + Fsm() + " scene=" + SceneName()
                + " hud=" + (Open<HudPanel>() ? 1 : 0)
                + " boot=" + (Open<BootPanel>() ? 1 : 0)
                + " menu=" + (Open<MainMenuPanel>() ? 1 : 0)
                + " select=" + (Open<CharSelectPanel>() ? 1 : 0)
                + " loading=" + (Open<LoadingPanel>() ? 1 : 0)
                + " charpanel=" + (Open<CharacterPanel>() ? 1 : 0)
                + " gfx=\"" + SafeDevice() + "\"";
        }
        private static string SafeDevice()
        {
            try { return SystemInfo.graphicsDeviceName; } catch { return "(unknown)"; }
        }
        internal static ISaveModule Save() { return CtxMember("Save") as ISaveModule; }
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
        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(name); if (t != null) return t; }
            return null;
        }
        internal static Component FindPanel<T>() where T : Component
        {
            var a = UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return a != null && a.Length > 0 ? a[0] : null;
        }

        // ---- ui nodes --------------------------------------------------------------------
        internal static GameObject Node(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t.gameObject;
            return null;
        }
        internal static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            var cur = t;
            while (cur != null) { if (sb.Length > 0) sb.Insert(0, '/'); sb.Insert(0, cur.name); cur = cur.parent; }
            return sb.ToString();
        }
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
        internal static string RectStr(Component c)
        {
            if (c == null) return "(none)";
            Vector2 lo, hi, ct;
            if (!ScreenRect(c.transform as RectTransform, out lo, out hi, out ct)) return "(no-rect)";
            return "c=" + ct.x.ToString("0.0") + "," + ct.y.ToString("0.0")
                + " wh=" + (hi.x - lo.x).ToString("0.0") + "x" + (hi.y - lo.y).ToString("0.0");
        }
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
                if (pathSub.Length > 0 && PathOf(b.transform).IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                if (pick == null || PathOf(b.transform).Length > PathOf(pick.transform).Length) pick = b;
            }
            diag = "buttons=" + all.Length + " candidates=" + n;
            return pick;
        }
        internal static string RaycastTop(Vector2 pos)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current);
            ped.position = pos;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            var g = hits[0].gameObject;
            return g == null ? "(null)" : (g.name + "@" + PathOf(g.transform));
        }
        internal static string Click(string arg)
        {
            if (EventSystem.current == null) { Warn("ERR click name=" + arg + " eventSystem=null"); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(arg, out diag);
            if (pick == null) { Warn("ERR click-miss name=" + arg + " " + diag); return "ERR-click-miss"; }
            var go = pick.gameObject;
            var ped = new PointerEventData(EventSystem.current);
            ped.button = PointerEventData.InputButton.Left;
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            if (!pick.interactable) { Log("CLICK-SKIP name=" + arg + " (not interactable)"); return "CLICK-SKIP"; }
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-EXEC name=" + arg + " path=" + PathOf(go.transform) + " " + diag);
            return "CLICKED";
        }

        // ---- real input (InputSystem) -----------------------------------------------------
        internal static UnityEngine.InputSystem.Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }
        internal static UnityEngine.InputSystem.Keyboard KeyboardDev()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }
        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = pos };
            if (leftDown) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(m, st);
        }
        internal static string KeyDown(string arg)
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            UnityEngine.InputSystem.Key key;
            switch ((arg ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = UnityEngine.InputSystem.Key.Space; break;
                case "escape": key = UnityEngine.InputSystem.Key.Escape; break;
                case "enter": key = UnityEngine.InputSystem.Key.Enter; break;
                case "c": key = UnityEngine.InputSystem.Key.C; break;
                default: key = UnityEngine.InputSystem.Key.None; break;
            }
            if (key == UnityEngine.InputSystem.Key.None) { Warn("keydown unknown-key=" + arg); return "ERR-unknown-key"; }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(key));
            Log("KEYDOWN key=" + key);
            return "KEYDOWN-" + key;
        }
        internal static string KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
            Log("KEYUP");
            return "KEYUP";
        }
    }

    /// <summary>The driver: one linear station list polled from Update (same shape as `ShopGrid.Driver`).</summary>
    public class CharstatDriver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;

        private string _tag = "run1";
        private string _saveName = string.Empty;
        private string _enterVia = string.Empty;
        private int _bootSpaced;
        private int _singleMouseTried;
        private int _singleExecTried;
        private int _enterMouseTried;
        private int _enterExecTried;
        private int _enterEventTried;
        private int _panelMouseTried;
        private int _keyDownSent;
        private int _statsEvents;
        private PlayerStatsDto _last;
        private bool _subscribed;
        private int _shotDone;
        private readonly List<string> _lines = new List<string>();

        private const float PollStep = 0.1f;
        private const float BootTimeout = 30f;
        private const float SelectTimeout = 25f;
        private const float StageTimeout = 90f;
        private const float PanelTimeout = 15f;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            Aux.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            if (p.Length > 1 && p[1].Length > 0) _tag = p[1];
            Aux.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                + " gameRunning=" + (Game.IsRunning ? 1 : 0) + " " + Aux.State());
            BuildPlan();
            Aux.KV("PLAN", "stations=" + _plan.Count + " tag=" + _tag);
        }

        private void Update()
        {
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
                Aux.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        // ---- station plumbing ------------------------------------------------------------
        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            var fn = f;
            _plan.Add(() =>
            {
                if (_cur != nm) { _cur = nm; _at = Time.unscaledTime; Aux.Log("PHASE " + nm + " " + Aux.State()); }
                return fn();
            });
        }
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private bool Tout(string what, float s)
        {
            if (!Elapsed(s)) return false;
            Aux.KV("STATION-TIMEOUT", "at=" + what + " " + Aux.State());
            Note("timeout:" + what);
            return true;
        }
        private void Note(string s) { _lines.Add(s); }

        private void Shoot(string file)
        {
            var path = Aux.Dir + "/" + file;
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            ScreenCapture.CaptureScreenshot(path);
            Aux.KV("SHOT-REQUEST", "file=" + file + " frame=" + Time.frameCount);
        }
        private bool ShotLanded(string file)
        {
            var path = Aux.Dir + "/" + file;
            if (!Elapsed(1.5f)) return false;
            if (File.Exists(path) && new FileInfo(path).Length > 1000)
            {
                Aux.KV("SHOT-OK", "file=" + file + " bytes=" + new FileInfo(path).Length);
                _shotDone = 1;
                return true;
            }
            if (Elapsed(15f)) { Aux.Warn("SHOT-TIMEOUT file=" + path); return true; }
            return false;
        }

        /// <summary>Real-mouse click on a Button found by name; returns 1 hit / 0 miss.</summary>
        private int RealClick(string name, string tag)
        {
            string diag;
            var b = Aux.FindButton(name, out diag);
            if (b == null) { Aux.Warn("UI-BUTTON-MISS name=" + name + " " + diag); return 0; }
            Vector2 lo, hi, c;
            Aux.ScreenRect(b.transform as RectTransform, out lo, out hi, out c);
            _clickPos = new Vector2(c.x, Screen.height - c.y);
            _clickName = name;
            _clickPhase = 1;
            _clickFrames = 2;
            Aux.MouseState(_clickPos, false);
            Aux.KV("CLICKTOP", "tag=" + tag + " name=" + name + " screen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                + " interactable=" + (b.interactable ? 1 : 0)
                + " raycastTop=\"" + Aux.RaycastTop(c) + "\" " + diag);
            return 1;
        }
        private Vector2 _clickPos;
        private string _clickName = string.Empty;
        private int _clickPhase;
        private int _clickFrames;

        private void TickClick()
        {
            if (_clickPhase == 0) return;
            _clickFrames--;
            if (_clickFrames > 0) return;
            if (_clickPhase == 1)
            {
                Aux.MouseState(_clickPos, true);
                Aux.KV("UI-DOWN", "node=" + _clickName + " pos=" + _clickPos.x.ToString("0") + "," + _clickPos.y.ToString("0")
                    + " frame=" + Time.frameCount);
                _clickPhase = 2; _clickFrames = 2;
                return;
            }
            Aux.MouseState(_clickPos, false);
            Aux.KV("UI-UP", "node=" + _clickName + " frame=" + Time.frameCount);
            _clickPhase = 0; _clickFrames = 0;
        }

        // ---- plan -------------------------------------------------------------------------
        private void BuildPlan()
        {
            // ------------------------------------------------------------------ CFG
            Add("cfg", () =>
            {
                Application.runInBackground = true;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = 60;
                try
                {
                    var st = UnityEngine.InputSystem.InputSystem.settings;
                    st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                    st.editorInputBehaviorInPlayMode =
                        UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                }
                catch (Exception e) { Aux.Warn("CFG input-system settings EX " + e.GetType().Name); }
                Aux.KV("CFG", "gameRunning=" + (Game.IsRunning ? 1 : 0)
                    + " vSync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate
                    + " screen=" + Screen.width + "x" + Screen.height + " " + Aux.State());
                return true;
            });

            // ------------------------------------------------------------------ BOOT -> MAIN MENU
            // x_drive B1: the boot screen advances on a real Space press.
            Add("boot", () =>
            {
                TickClick();
                if (Aux.Open<MainMenuPanel>() || Aux.Fsm() == "MainMenu") { Aux.KV("BOOT-OK", Aux.State()); return true; }
                if (Aux.Open<BootPanel>() && Elapsed(1.2f) && _bootSpaced == 0)
                {
                    _bootSpaced = 1;
                    Aux.KeyDown("space");
                    Note("boot:space-down");
                    return false;
                }
                if (_bootSpaced == 1 && Elapsed(0.3f)) { _bootSpaced = 2; Aux.KeyUp(); return false; }
                if (Elapsed(12f) && _bootSpaced == 2)
                {
                    _bootSpaced = 3;
                    Aux.Warn("BOOT the Space press did not advance the boot screen -> FSM trigger TriggerBootDone (the same transition the key raises)");
                    if (Game.Fsm != null) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                    return false;
                }
                if (Tout("boot", BootTimeout)) return true;
                return false;
            });

            // ------------------------------------------------------------------ MAIN MENU -> SINGLE
            Add("mainmenu", () =>
            {
                TickClick();
                if (Aux.Open<CharSelectPanel>()) return true;
                // real-mouse click on "Single" (x_drive B2-click-single); the boot screen is gone
                if (_singleMouseTried == 0 && Aux.Open<MainMenuPanel>() && Elapsed(1.0f))
                {
                    _singleMouseTried = 1;
                    RealClick("Single", "u52");
                    return false;
                }
                if (_singleMouseTried == 1 && Elapsed(4f) && _singleExecTried == 0 && !Aux.Open<CharSelectPanel>())
                {
                    _singleExecTried = 1;
                    Aux.Warn("SINGLE the real-mouse click did not open the roster -> ExecuteEvents path (same fallback x_drive uses)");
                    Aux.Click("Single");
                    return false;
                }
                if (Tout("mainmenu", SelectTimeout))
                {
                    Aux.Warn("SELECT never opened -> BLOCKED (nothing to measure)");
                    Finish("blocked-no-select");
                }
                return false;
            });

            // ------------------------------------------------------------------ ROSTER DUMP
            Add("roster", () =>
            {
                var save = Aux.Save();
                var list = save != null ? save.List() : null;
                Aux.KV("ROSTER", "saves=" + (list != null ? list.Count : -1)
                    + " names=[" + (list != null ? string.Join(",", list.ToArray()) : "-") + "]");
                if (list != null && list.Count > 0) _saveName = list[0];
                return true;
            });

            // ------------------------------------------------------------------ ROSTER -> STAGE
            // x_drive B5-enter: the roster row ENTER button (real mouse first, then the
            // ExecuteEvents path, then the event the button itself raises).
            Add("enter-stage", () =>
            {
                TickClick();
                if (Aux.Open<HudPanel>() && Aux.Fsm() == "Stage") { Aux.KV("STAGE-OK", "via=" + _enterVia + " " + Aux.State()); return true; }
                if (!Aux.Open<CharSelectPanel>() && !Aux.Open<LoadingPanel>() && Elapsed(6f)
                    && _enterEventTried == 0 && Aux.Fsm() != "Stage")
                {
                    // roster never closed and no loading screen -> something else is on top
                    Aux.KV("ENTER-WAIT", Aux.State());
                }
                if (_enterMouseTried == 0 && Elapsed(0.5f))
                {
                    _enterMouseTried = 1;
                    var name = RowName();
                    if (RealClick(name + "|Enter", "u52") == 1) _enterVia = "real-mouse:" + name + "|Enter";
                    return false;
                }
                if (_enterMouseTried == 1 && Elapsed(4f) && _enterExecTried == 0 && Aux.Open<CharSelectPanel>())
                {
                    _enterExecTried = 1;
                    _enterVia = "exec-events:" + RowName() + "|Enter";
                    Aux.Warn("ENTER the real-mouse row click did not leave the roster -> ExecuteEvents path (x_drive B5 measured the same)");
                    Aux.Click(RowName() + "|Enter");
                    return false;
                }
                if (_enterExecTried == 1 && Elapsed(6f) && _enterEventTried == 0 && Aux.Open<CharSelectPanel>())
                {
                    _enterEventTried = 1;
                    _enterVia = "event:CharSelectRequest(" + _saveName + ")";
                    Aux.Warn("ENTER the ExecuteEvents click did not leave the roster either -> emitting the event the ENTER button raises"
                        + " (NOT a teleport: AppFlow still runs the whole Boot->Stage transition)");
                    Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
                    return false;
                }
                if (Tout("enter-stage", StageTimeout))
                {
                    Aux.Warn("STAGE never reached -> BLOCKED");
                    Finish("blocked-no-stage");
                }
                return false;
            });

            // ------------------------------------------------------------------ HUD baseline
            Add("hud", () =>
            {
                SubscribeStats();
                Aux.KV("HUD", Aux.State() + " statsEvents=" + _statsEvents);
                return true;
            });

            // ------------------------------------------------------------------ OPEN THE PANEL (real C key)
            Add("open-panel", () =>
            {
                TickClick();
                if (Aux.Open<CharacterPanel>()) { Aux.KV("PANEL-OPEN", "t=" + Time.unscaledTime.ToString("0.0") + " statsEvents=" + _statsEvents); return true; }
                if (_panelMouseTried == 0 && Elapsed(0.6f))
                {
                    _panelMouseTried = 1;
                    // the product path: inject the real 'C' key (the HUD's InputReader maps it)
                    _keyDownSent = 1;
                    Aux.KeyDown("c");
                    Note("panel:key-c-down");
                    return false;
                }
                if (_keyDownSent == 1 && Elapsed(0.25f)) { _keyDownSent = 2; Aux.KeyUp(); return false; }
                if (_keyDownSent == 2 && Elapsed(3f))
                {
                    _keyDownSent = 3;
                    Aux.Warn("PANEL the 'C' key did not open CharacterPanel -> emitting Events.PanelToggleRequest (the same request the key raises)");
                    Game.Event.Emit(Events.PanelToggleRequest, nameof(CharacterPanel));
                    return false;
                }
                if (Tout("open-panel", PanelTimeout))
                {
                    Aux.Warn("PANEL never opened -> BLOCKED");
                    Finish("blocked-no-panel");
                }
                return false;
            });

            // ------------------------------------------------------------------ SETTLE + SHOT
            Add("panel-settle", () => Elapsed(1.2f));
            Add("panel-shot", () => { Shoot("d2u3_charstat_" + _tag + ".png"); return true; });
            Add("panel-shot-land", () => ShotLanded("d2u3_charstat_" + _tag + ".png"));

            // ------------------------------------------------------------------ DUMP
            Add("dump", () =>
            {
                DumpScreen();
                DumpDto();
                return true;
            });

            Add("finish", () => { Finish("ok"); return true; });
        }

        private string RowName()
        {
            var save = Aux.Save();
            var list = save != null ? save.List() : null;
            if (list != null && list.Count > 0 && !string.IsNullOrEmpty(_saveName))
            {
                var idx = list.IndexOf(_saveName);
                if (idx >= 0) return "Row" + idx;
            }
            return "Row0";
        }

        private void SubscribeStats()
        {
            if (_subscribed || Game.Event == null) return;
            _subscribed = true;
            Game.Event.On<PlayerStatsDto>(Events.HudDirty, OnStats);
            Game.Event.On<PlayerStatsDto>(Events.PlayerStatsChanged, OnStats);
            Aux.KV("SUBSCRIBED", Events.HudDirty + " / " + Events.PlayerStatsChanged);
        }
        private void OnStats(PlayerStatsDto dto) { _statsEvents++; if (dto != null) _last = dto; }

        // ---- readings ---------------------------------------------------------------------
        private Component Panel() { return Aux.FindPanel<CharacterPanel>(); }

        /// <summary>Every visible Text under the panel + the geometry verdicts (P1 / D1..D7).</summary>
        private void DumpScreen()
        {
            var panel = Panel();
            var root = panel != null ? panel.transform : null;
            if (root == null) { Aux.Warn("PANEL-MISSING no CharacterPanel component found for the screen dump"); return; }

            // the panel background node carries the assembled original PNG (320x432 art px)
            var bg = Aux.Node(root, "CharstatBg");
            Vector2 lo = Vector2.zero, hi = Vector2.zero, bc = Vector2.zero;
            var haveBg = bg != null && Aux.ScreenRect(bg.transform as RectTransform, out lo, out hi, out bc);
            var scale = haveBg && bg.transform is RectTransform
                ? ((RectTransform)bg.transform).rect.width / 320f : 0f;
            Aux.KV("PANEL-RECT", "bg=" + (haveBg ? ("c=" + bc.x.ToString("0.0") + "," + bc.y.ToString("0.0")
                    + " wh=" + (hi.x - lo.x).ToString("0.0") + "x" + (hi.y - lo.y).ToString("0.0"))
                    : "(none)")
                + " scale=" + scale.ToString("0.0000") + " (canvas px per original art px, derived from the PNG width 320)");

            var sb = new StringBuilder();
            sb.AppendLine("# u3_charstat_screen_" + _tag + ".tsv");
            sb.AppendLine("# every visible Text under CharacterPanel as the running game lays it out");
            sb.AppendLine("# panelBgC=" + bc.x.ToString("0.0") + "," + bc.y.ToString("0.0")
                + " panelW=" + (hi.x - lo.x).ToString("0.0") + " scale=" + scale.ToString("0.0000"));
            sb.AppendLine("node\tts\tscreen_cx\tscreen_cy\tw\th\tfontSize\tlines\ttext\texpectedCx\tverdict");

            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t == null || t.text == null) continue;
                if (!t.gameObject.activeInHierarchy) continue;
                Vector2 a, b, c;
                var ok = Aux.ScreenRect(t.rectTransform, out a, out b, out c);
                var lines = 0;
                try { lines = t.cachedTextGenerator != null ? t.cachedTextGenerator.lineCount : 0; } catch { }
                var w = ok ? (b.x - a.x) : 0f;
                var path = Aux.PathOf(t.transform);
                // autosize-off: the label node name is the leaf
                var leaf = t.gameObject.name;
                var exp = ExpectedCx(leaf, bc, scale);
                var verdict = exp.HasValue && ok
                    ? (Mathf.Abs(c.x - exp.Value) <= Mathf.Max(2f, 1.5f * scale) ? "ok" : "MISMATCH")
                    : "n/a";
                sb.AppendLine(leaf + "\t" + (ok ? "1" : "0")
                    + "\t" + (ok ? c.x.ToString("0.0") : "-")
                    + "\t" + (ok ? c.y.ToString("0.0") : "-")
                    + "\t" + (ok ? w.ToString("0.0") : "-")
                    + "\t" + (ok ? (b.y - a.y).ToString("0.0") : "-")
                    + "\t" + t.fontSize
                    + "\t" + lines
                    + "\t" + Aux.Esc(t.text)
                    + "\t" + (exp.HasValue ? exp.Value.ToString("0.0") : "-")
                    + "\t" + verdict);
                Aux.Log("UITEXT node=" + leaf + " active=1 cx=" + (ok ? c.x.ToString("0.0") : "-")
                    + " cy=" + (ok ? c.y.ToString("0.0") : "-")
                    + " w=" + (ok ? w.ToString("0.0") : "-")
                    + " fontSize=" + t.fontSize + " lines=" + lines
                    + " text=\"" + Aux.Esc(t.text) + "\"");
            }
            // ── every D2Label under the panel: the VALUE columns really live here ──────────────
            // The `GetComponentsInChildren<Text>` loop above lists only the NAME labels (a uGUI Text
            // mirrored onto a D2Label). The VALUE labels are created straight as
            // `D2Label.Create(...)` and have NO uGUI Text at all -- the FIT block below documents the
            // same split. Without this pass the ON-SCREEN NUMBER of every field would be missing,
            // which is precisely the wrong gap for a complaint about the numbers shown.
            // Source of the list: `D2Text.D2Label.Live` (`D2Text.cs:702`, `Live.Add(label)` at `:862`)
            // -- the renderer's own registry; we keep the entries under THIS panel root.
            var shown = 0;
            foreach (var pair in D2LabelsUnder(root))
            {
                var lbl2 = pair.Value;
                var leaf2 = pair.Key.gameObject.name;
                var text2 = D2LabelProp(lbl2, "text") as string;
                Vector2 a2, b2, c2;
                var ok2 = Aux.ScreenRect(pair.Key, out a2, out b2, out c2);
                var lines2 = D2LabelInt(lbl2, "LineCount");
                var fs2 = D2LabelInt(lbl2, "fontSize");
                var exp2 = ExpectedCx(leaf2, bc, scale);
                var v2 = exp2.HasValue && ok2
                    ? (Mathf.Abs(c2.x - exp2.Value) <= Mathf.Max(2f, 1.5f * scale) ? "ok" : "MISMATCH")
                    : "n/a";
                shown++;
                sb.AppendLine(leaf2 + "\t" + (ok2 ? "1" : "0")
                    + "\t" + (ok2 ? c2.x.ToString("0.0") : "-")
                    + "\t" + (ok2 ? c2.y.ToString("0.0") : "-")
                    + "\t" + (ok2 ? (b2.x - a2.x).ToString("0.0") : "-")
                    + "\t" + (ok2 ? (b2.y - a2.y).ToString("0.0") : "-")
                    + "\t" + fs2 + "\t" + lines2
                    + "\t" + Aux.Esc(text2)
                    + "\t" + (exp2.HasValue ? exp2.Value.ToString("0.0") : "-")
                    + "\t" + v2);
                Aux.Log("D2LABEL node=" + leaf2 + " text=\"" + Aux.Esc(text2) + "\" fontSize=" + fs2
                    + " lineCount=" + lines2 + " rect=" + Aux.RectStr(pair.Key)
                    + " expectedCx=" + (exp2.HasValue ? exp2.Value.ToString("0.0") : "-") + " verdict=" + v2);
            }
            Aux.KV("D2LABELS-SHOWN", "count=" + shown + " (the value columns; names come from uGUI Text above)");

            Aux.WriteFile(Aux.Dir + "/u3_charstat_screen_" + _tag + ".tsv", sb.ToString());

            // ---- the "add point" arrow (D7): sprite name / alpha / rect / interactable ----
            // expected centre = row centre (node -115.4 = CharStatRowOrig.x) + CharPlusX (75.4)
            // = node -40 (original art px) -> screen via the panel-derived scale.
            for (var i = 0; i < 4; i++)
            {
                var plus = Aux.Node(root, "Plus" + i);
                var img = plus != null ? plus.GetComponent<Image>() : null;
                var btn = plus != null ? plus.GetComponent<Button>() : null;
                var expCx = bc.x + (-40f) * scale;
                var expCy = bc.y + UiLayoutRowY(i) * scale;
                Vector2 alo = Vector2.zero, ahi = Vector2.zero, ac = Vector2.zero;
                var haveRect = img != null && Aux.ScreenRect(img.transform as RectTransform, out alo, out ahi, out ac);
                var dxOk = haveRect && Mathf.Abs(ac.x - expCx) <= Mathf.Max(2f, 1.5f * scale);
                Aux.Log("ARROW i=" + i
                    + " node=" + (plus != null ? "Plus" + i : "(missing)")
                    + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "none")
                    + " alpha=" + (img != null ? img.color.a.ToString("0.00") : "-")
                    + " color=" + (img != null ? img.color.r.ToString("0.00") + "/" + img.color.g.ToString("0.00") + "/" + img.color.b.ToString("0.00") : "-")
                    + " interactable=" + (btn != null ? (btn.interactable ? 1 : 0) : -1)
                    + " rect=" + Aux.RectStr(img)
                    + " expectedCx=" + expCx.ToString("0.0") + " expectedCy=" + expCy.ToString("0.0")
                    + " verdict=" + (!haveRect ? "n/a"
                        : ((dxOk && Mathf.Abs(ac.y - expCy) <= 3f * scale) ? "at-original-slot" : "OFF-SLOT"))
                    + " spriteVerdict=" + (img != null && img.sprite != null ? "art-attached" : "NO-ART"));
            }

            // ---- close control (D5/close fix): must be VISIBLE and carry the original frame ----
            var close = Aux.Node(root, "CloseButton");
            var cimg = close != null ? close.GetComponent<Image>() : null;
            Aux.Log("CLOSE node=" + (close != null ? "CloseButton" : "(missing)")
                + " sprite=" + (cimg != null && cimg.sprite != null ? cimg.sprite.name : "none")
                + " alpha=" + (cimg != null ? cimg.color.a.ToString("0.00") : "-")
                + " label=\"" + (Aux.Node(root, "CloseLabel") != null
                    ? Aux.Esc(Aux.Node(root, "CloseLabel").GetComponent<Text>() != null
                        ? Aux.Node(root, "CloseLabel").GetComponent<Text>().text : "-") : "-") + "\""
                + " rect=" + Aux.RectStr(cimg)
                + " visibleVerdict=" + (cimg != null && cimg.color.a > 0.9f ? "ok" : "MISMATCH"));

            // ---- the three top-right band slots must sit inside the panel rect (D4) ----
            foreach (var nm in new[] { "CharName", "TopRight", "Band2Mid", "Band2Right" })
            {
                var go = Aux.Node(root, nm);
                Vector2 a, b, c;
                if (go == null || !Aux.ScreenRect(go.transform as RectTransform, out a, out b, out c)) { Aux.Log("SLOT node=" + nm + " (missing)"); continue; }
                var inside = a.x >= lo.x - 1f && b.x <= hi.x + 1f && a.y >= lo.y - 1f && b.y <= hi.y + 1f;
                Aux.Log("SLOT node=" + nm + " cx=" + c.x.ToString("0.0") + " cy=" + c.y.ToString("0.0")
                    + " w=" + (b.x - a.x).ToString("0.0")                     + " insidePanel=" + (inside ? 1 : 0)
                    + " verdict=" + (inside ? "ok" : "MISMATCH"));
            }

            // ---- D10 / U1: the WRAP criterion, measured with the REAL renderer ------------------
            // NOT `Text.cachedTextGenerator.lineCount` (a mirrored Text keeps `font = null`, so its
            // generator is never laid out -> the 11:38:35 run printed `lines=0`, and 0 != 1 line is a
            // FALSE verdict) and NOT `Text.preferredWidth` (also 0 for the same reason -> an
            // always-true check). Production formula (`UI/D2Text.cs:981-983` + `D2Text.cs:593`):
            //     availPx = max(1, round(rect.width / scale));   scale = fontSize / cellH
            //     one line  <=>  D2Text.MeasureNative(font, text, chi) < availPx
            // plus the line count the renderer itself produced (`D2Label.LineCount`).
            foreach (var nm in new[]
            {
                "ResistName0", "ResistName1", "ResistName2", "ResistName3",
                "ResistValue0", "ResistValue1", "ResistValue2", "ResistValue3",
            })
            {
                var go = Aux.Node(root, nm);
                // Two kinds of label live here: the NAME labels are a uGUI `Text` mirrored onto a
                // D2Label (`UiArt.Label`); the VALUE labels are created straight as
                // `D2Label.Create(...)` and have NO uGUI Text at all -- that is why this driver
                // own Text dump never lists StatValue/DerivedValue/ExtraValue/ResistValue.
                // Both are read the same way below: text + line count taken from the D2Label
                // instance that really draws (registered in `D2Text.D2Label.Live`).
                var rt = go != null ? go.transform as RectTransform : null;
                var txt = go != null ? go.GetComponent<Text>() : null;
                var lbl = D2LabelOf(rt);
                var text = txt != null ? txt.text : (D2LabelProp(lbl, "text") as string);
                var fontSize = txt != null ? txt.fontSize : D2LabelInt(lbl, "fontSize");
                Vector2 fa, fb, fc;
                if (rt == null || text == null || !Aux.ScreenRect(rt, out fa, out fb, out fc))
                {
                    Aux.Log("FIT node=" + nm + " node=" + (go != null ? "present" : "missing")
                        + " text=\"" + Aux.Esc(text) + "\" verdict=UNCERTAIN(no-node-or-rect)");
                    continue;
                }

                var latinObj = D2Call("IsLatinOnly", text);
                if (!(latinObj is bool))
                {
                    // `Diablo2.UI.D2Text` is internal and reached by reflection -- if it is gone we must
                    // NOT answer "fits" (that would be the always-true verdict this whole block replaces)
                    Aux.Log("FIT node=" + nm + " text=\"" + Aux.Esc(text) + "\" verdict=UNCERTAIN(D2Text unreachable)");
                    continue;
                }
                var chi = !(bool)latinObj;
                var font = D2Font("Font16");
                var readyObj = D2Call("ChiReady", font);
                if (chi && (readyObj is bool) && !(bool)readyObj)
                {
                    // no fake green: with the atlas missing every advance is 0 => "everything fits"
                    Aux.Warn("FIT node=" + nm + " chi font NOT READY -> needNative would be 0 (fake green); "
                        + "reporting UNCERTAIN instead");
                    Aux.Log("FIT node=" + nm + " kind=chi text=\"" + Aux.Esc(text)
                        + "\" verdict=UNCERTAIN(font-not-ready)");
                    continue;
                }

                var boxCanvas = fb.x - fa.x;
                var scaleObj = D2Call("ScaleFor", (int)UiConst("FontPx16", 28f), chi, font);
                var needObj = D2Call("MeasureNative", font, text, chi);
                if (!(scaleObj is float) || !(needObj is int))
                {
                    Aux.Log("FIT node=" + nm + " text=\"" + Aux.Esc(text) + "\" verdict=UNCERTAIN(D2Text not callable)");
                    continue;
                }
                var fitScale = (float)scaleObj;
                var avail = Mathf.Max(1, Mathf.RoundToInt(boxCanvas / fitScale));
                var need = (int)needObj;
                var lines = lbl != null ? D2LabelInt(lbl, "LineCount") : MirrorLineCount(txt);
                var fits = need < avail;
                var verdict = !fits ? "WRAPPED"
                    : (lines == 1 ? "single-line"
                        : (lines < 0 ? "single-line(renderer-lineCount-not-found)" : "need-fits-but-renderer-lines=" + lines));
                Aux.Log("FIT node=" + nm + " kind=" + (chi ? "chi" : "latin")
                    + " text=\"" + Aux.Esc(text) + "\" fontSize=" + fontSize
                    + " scale=" + fitScale.ToString("0.0000")
                    + " boxCanvas=" + boxCanvas.ToString("0.0") + " needNative=" + need
                    + " availPx=" + avail + " lineCount=" + lines + " verdict=" + verdict);

                // PROBE (team-lead approved 2026-09-24, PRINT-ONLY -- never an abort reason): can the
                // mirrored uGUI Text answer at all? `D2TextMirror.Attach` sets `font = null`
                // (`UI/D2TextMirror.cs:65-67`), so every TextGenerator route should be blind and
                // `preferredWidth` should stay 0 => `preferredWidth > rect.width + 0.5f` would be an
                // ALWAYS-FALSE criterion. Printed next to the real renderer line count, same node.
                if (txt != null)
                {
                    var probeFont = txt.font != null ? txt.font.name : "(null)";
                    var probePw = txt.preferredWidth;
                    var probeGw = txt.cachedTextGeneratorForLayout.GetPreferredWidth(txt.text,
                        txt.GetGenerationSettings(Vector2.zero));
                    var probeW = fb.x - fa.x;
                    var probeFolded = probePw > probeW + 0.5f;
                    Aux.Log("PROBE node=" + nm + " font=" + probeFont
                        + " preferredWidth=" + probePw.ToString("0.0")
                        + " cachedTextGeneratorForLayout.GetPreferredWidth=" + probeGw.ToString("0.0")
                        + " rectWidth=" + probeW.ToString("0.0")
                        + " horizontalOverflow=" + txt.horizontalOverflow
                        + " rendererLineCount=" + lines
                        + " preferredWidthVerdict=" + (probeFolded ? "folded" : "NOT-folded"));
                }
            }
        }

        /// <summary>Expected screen centre x per node (original art geometry x panel-derived scale).</summary>
        private static float? ExpectedCx(string leaf, Vector2 panelC, float scale)
        {
            if (scale <= 0f) return null;
            // panel-local node x (original art px, panel centre = art 160 -> node 0) -> screen
            switch (leaf)
            {
                // panel-local node x, all from `UI/UiLayoutGame.cs` (originals x1.8 at runtime)
                case "StatValue0": case "StatValue1": case "StatValue2": case "StatValue3":
                    return panelC.x + (-64f) * scale;      // CharStatValueX  (art 80..112, centre 96)
                case "ResistValue0": case "ResistValue1": case "ResistValue2": case "ResistValue3":
                    // u52-resist: read the constant instead of copying it (the old hard-coded -80f
                    // went stale the moment the value column was narrowed to make room for the label)
                    return panelC.x + UiConst("CharResistValueX", -70f) * scale;   // art 67.5..112.5
                case "ResistName0": case "ResistName1": case "ResistName2": case "ResistName3":
                    return panelC.x + UiConst("CharResistNameX", -127f) * scale;   // art 0..66
                case "DerivedValue0": case "ExtraValue0": case "ExtraValue1":
                    return panelC.x + (130f) * scale;      // CharDefValueX (art 271..309)
                case "DerivedValue1": case "DerivedValue2": case "DerivedValue3":
                    return panelC.x + (110f) * scale;      // CharCurMaxX (art 231..309)
                case "CharName": return panelC.x + (-63.1f) * scale;   // CharNamePos
                case "TopRight": return panelC.x + (91f) * scale;      // CharTopRightPos
                case "Band2Mid": return panelC.x + (-37.5f) * scale;   // CharBand2MidPos
                case "Band2Right": return panelC.x + (90.5f) * scale;  // CharBand2RightPos
                case "CloseButton": case "CloseLabel": return panelC.x + (-15.4f) * scale; // CharClosePos
                default: return null;
            }
        }
        /// <summary>
        /// Read an internal `Diablo2.UI.UiLayoutGame` constant by name (single source of truth: the probe
        /// must NOT keep its own copy of the geometry -- measured 2026-09-24, the hard-coded -80f for
        /// `CharResistValueX` survived the constant's change and would have reported MISMATCH on a
        /// perfectly correct panel). Falls back to <paramref name="fallback"/> when the field is gone.
        /// </summary>
        internal static float UiConst(string name, float fallback)
        {
            try
            {
                var t = typeof(CharacterPanel).Assembly.GetType("Diablo2.UI.UiLayoutGame");
                if (t == null) return fallback;
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) return fallback;
                var v = f.GetValue(null);
                if (v is float) return (float)v;
                if (v is int) return (int)v;
                return fallback;
            }
            catch { return fallback; }
        }

        /// <summary>
        /// Call `Diablo2.UI.D2Text` by reflection.
        /// WHY: `D2Text` is declared `internal static class D2Text` (`UI/D2Text.cs:44`) and the CLI
        /// compiles this driver into its OWN assembly => internal members are unreachable by name.
        /// Measured 2026-09-24 12:16: writing `D2Text.MeasureNative(...)` directly left the driver
        /// uncompiled, so the whole session produced ZERO [D2U3C] lines while `run_script` still
        /// answered `success: true` (the runner only prints the first 500 chars, so the CS0122 hid).
        /// Returns null on any failure -- callers MUST treat null as UNCERTAIN (never as "fits").
        /// </summary>
        /// <summary>
        /// Find the `Diablo2.UI.D2Label` (top-level, internal, `D2Text.cs:697`) that draws the given RectTransform.
        /// D2Label is a plain class (not a MonoBehaviour) registered in the private static list
        /// `D2Text.D2Label.Live` (`D2Text.cs:702`, `Live.Add(label)` at `:862`), so matching
        /// `rectTransform` against that list is the only way to reach it from a node.
        /// </summary>
        internal static object D2LabelOf(RectTransform rt)
        {
            if (rt == null) return null;
            try
            {
                var asm = typeof(CharacterPanel).Assembly;   // D2Label is a TOP-LEVEL internal class
                var t = asm.GetType("Diablo2.UI.D2Label") ?? asm.GetType("Diablo2.UI.D2Text+D2Label");
                if (t == null) return null;
                var f = t.GetField("Live", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var live = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
                if (live == null) return null;
                var p = t.GetProperty("rectTransform");
                foreach (var l in live)
                {
                    var lrt = p != null ? p.GetValue(l, null) as RectTransform : null;
                    if (lrt == rt) return l;
                }
            }
            catch { }
            return null;
        }

        /// <summary>Read a `D2Label` property by name; null when absent.</summary>
        internal static object D2LabelProp(object lbl, string name)
        {
            if (lbl == null) return null;
            try
            {
                var p = lbl.GetType().GetProperty(name);
                return p != null ? p.GetValue(lbl, null) : null;
            }
            catch { return null; }
        }

        /// <summary>Read an int `D2Label` property; -1 when absent (never a silent 0 or 1).</summary>
        internal static int D2LabelInt(object lbl, string name)
        {
            var v = D2LabelProp(lbl, name);
            return v is int ? (int)v : -1;
        }

        /// <summary>
        /// The `D2Label` instances whose rect lives under <paramref name="root"/>, as
        /// (rectTransform, label) pairs. Registry: `D2Text.D2Label.Live` (private static list,
        /// `D2Text.cs:702`). Returns an EMPTY list on any failure -- callers print a count, so an
        /// empty result is visible instead of silently looking like "the panel has no labels".
        /// </summary>
        private static List<KeyValuePair<RectTransform, object>> D2LabelsUnder(Transform root)
        {
            var outp = new List<KeyValuePair<RectTransform, object>>();
            if (root == null) return outp;
            try
            {
                var asm = typeof(CharacterPanel).Assembly;
                var t = asm.GetType("Diablo2.UI.D2Label") ?? asm.GetType("Diablo2.UI.D2Text+D2Label");
                if (t == null) { Aux.Warn("D2LABELS class Diablo2.UI.D2Label not found -> value columns cannot be read"); return outp; }
                var f = t.GetField("Live", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
                if (list == null) { Aux.Warn("D2LABELS field D2Label.Live not found -> value columns cannot be read"); return outp; }
                var p = t.GetProperty("rectTransform");
                foreach (var l in list)
                {
                    var rt = p != null ? p.GetValue(l, null) as RectTransform : null;
                    if (rt == null) continue;
                    // `Transform`, NOT `var` (CS0266 measured by the compile sentinel 2026-09-24):
                    // `RectTransform.parent` returns `Transform`, so an inferred `RectTransform cur`
                    // cannot take the result back. `cur == root` stays a reference compare and the
                    // pair still carries `rt` (the RectTransform), so nothing else changes.
                    Transform cur = rt;
                    var under = false;
                    while (cur != null) { if (cur == root) { under = true; break; } cur = cur.parent; }
                    if (under) outp.Add(new KeyValuePair<RectTransform, object>(rt, l));
                }
            }
            catch (Exception e) { Aux.Warn("D2LABELS-ENUM-FAIL " + e.GetType().Name + ": " + e.Message); }
            return outp;
        }

        internal static object D2Call(string method, params object[] args)
        {
            try
            {
                var t = typeof(CharacterPanel).Assembly.GetType("Diablo2.UI.D2Text");
                if (t == null) return null;
                var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) return null;
                return m.Invoke(null, args);
            }
            catch (Exception e)
            {
                Aux.Warn("D2CALL-FAIL " + method + " -> " + e.GetType().Name + ": " + e.Message);
                return null;
            }
        }

        /// <summary>`Diablo2.UI.D2Text+D2Font` value by name (nested public enum inside an internal class).</summary>
        internal static object D2Font(string name)
        {
            try
            {
                var t = typeof(CharacterPanel).Assembly.GetType("Diablo2.UI.D2Text+D2Font");
                return t != null ? Enum.Parse(t, name) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// REAL line count of the bitmap label that actually draws this node.
        /// The uGUI `Text` itself is a data holder only (`D2TextMirror.Attach` sets `font = null` and
        /// `enabled = false`, `UI/D2TextMirror.cs:65-67`) =&gt; its TextGenerator is never laid out and
        /// `cachedTextGenerator.lineCount` stays 0 (exactly what the 11:38:35 run printed).
        /// The pixels come from `D2Label`; its `LineCount` is written by `BuildBitmap` from
        /// `D2Text.WrapLines`. -1 = not found (never silently 0/1).
        /// </summary>
        internal static int MirrorLineCount(Text t)
        {
            try
            {
                var m = t.GetComponent("D2TextMirror");
                if (m == null) return -1;
                var f = m.GetType().GetField("_label", BindingFlags.NonPublic | BindingFlags.Instance);
                var lbl = f != null ? f.GetValue(m) : null;
                if (lbl == null) return -1;
                var p = lbl.GetType().GetProperty("LineCount");
                return p != null ? (int)p.GetValue(lbl, null) : -1;
            }
            catch { return -1; }
        }

        /// <summary>Row centre y in original art px (UiLayoutGame.CharStatRowOrig[i].y), order str/dex/vit/eng.</summary>
        private static float UiLayoutRowY(int i)
        {
            switch (i)
            {
                case 0: return 119.1f;
                case 1: return 57.0f;
                case 2: return -28.4f;
                case 3: return -90.5f;
                default: return 0f;
            }
        }

        /// <summary>DTO field by field: on-screen value / official level-1 value / verdict.</summary>
        private void DumpDto()
        {
            var d = _last;
            if (d == null)
            {
                Aux.Warn("DTO-MISSING no HudDirty/PlayerStatsChanged payload captured (statsEvents=" + _statsEvents + ")");
                return;
            }
            Aux.Log("DTO name=" + d.name + " cls=" + d.cls + " level=" + d.level
                + " str=" + d.str + " dex=" + d.dex + " vit=" + d.vit + " eng=" + d.eng
                + " life=" + d.life + "/" + d.maxLife + " mana=" + d.mana + "/" + d.maxMana
                + " stamina=" + d.stamina + "/" + d.maxStamina
                + " def=" + d.defense + " ar=" + d.attackRating + " block=" + d.blockChance
                + " res=" + d.fireResist + "/" + d.coldResist + "/" + d.lightResist + "/" + d.poisonResist
                + " exp=" + d.exp + "/" + d.expNext + " statPts=" + d.statPoints + " skillPts=" + d.skillPoints);

            var panel = Panel();
            var root = panel != null ? panel.transform : null;
            var idx = (int)d.cls - 1;
            var life = new[] { 50, 40, 45, 55, 55 };
            var mana = new[] { 15, 35, 25, 15, 10 };
            var stam = new[] { 84, 74, 79, 89, 92 };
            var lvl1 = d.level == 1 && idx >= 0 && idx < 5;

            // ★ ON SCREEN FIRST (2026-09-24): user complaint #1 is a DISPLAY complaint ("the numbers
            //   are wrong / the UI shows it wrong"), so the criterion for the flagged rows is
            //   "what the panel really prints" (the D2Label text read through `Text(...)`) versus the
            //   official value. The dto column stays printed next to it, so a logic/display split is
            //   visible at a glance instead of being hidden behind one aggregate verdict.
            var onLife = Text(root, "DerivedValue2");
            var onMana = Text(root, "DerivedValue3");
            var onStam = Text(root, "DerivedValue1");
            var onExp = Text(root, "Band2Right");
            var onSkill = Text(root, "Band2Mid");
            var onRes = Text(root, "ResistValue0") + "/" + Text(root, "ResistValue1") + "/"
                + Text(root, "ResistValue2") + "/" + Text(root, "ResistValue3");
            var offLife = lvl1 ? life[idx] + "/" + life[idx] : "-";
            var offMana = lvl1 ? mana[idx] + "/" + mana[idx] : "-";
            var offStam = lvl1 ? stam[idx] + "/" + stam[idx] : "-";

            var sb = new StringBuilder();
            sb.AppendLine("# u3_charstat_readings_" + _tag + ".tsv  tag=" + _tag + " level=" + d.level
                + " statsEvents=" + _statsEvents + " fsm=" + Aux.Fsm());
            sb.AppendLine("field\tdto\tonscreen\tofficial\tsource\tverdict");
            Row(sb, "name", d.name, Text(root, "CharName"), "-", "prefab CharstatPanel/CharName m_Text", "n/a");
            Row(sb, "str", d.str.ToString(), Text(root, "StatValue0"), "20/25/15/25/30", "charstats.txt str (class dependent)", lvl1 ? "n/a" : "n/a");
            Row(sb, "dex", d.dex.ToString(), Text(root, "StatValue1"), "-", "charstats.txt dex", "n/a");
            Row(sb, "vit", d.vit.ToString(), Text(root, "StatValue2"), "-", "charstats.txt vit", "n/a");
            Row(sb, "eng", d.eng.ToString(), Text(root, "StatValue3"), "-", "charstats.txt int", "n/a");
            // criterion = ON SCREEN == official (the panel prints exactly "cur/max", no prefix)
            // ⚠️ 判据形状（2026-09-24 本片实测，别再把判据写成假红）：屏上值是 `cur/max`，而
            //   `off*` 是 `max/max` ⇒ **整串比会把"当前值不满"判成 MISMATCH**，而"当前值不满"
            //   是**合法游戏状态**（耐力会消耗；实测 14:16 活档 `20/84` 就是这一格）。
            //   ⇒ 本三行只判 **max 段**（显示/公式该判的东西）；`cur` 段留在 `onscreen` 列里供人读。
            Row(sb, "life/max", d.life + "/" + d.maxLife, onLife, offLife,
                "charstats hpadd + start vit / Arreat Summit Hit Points (MAX part judged on screen)",
                lvl1 ? Verdict(MaxPart(onLife) == MaxPart(offLife)) : "n/a");
            Row(sb, "mana/max", d.mana + "/" + d.maxMana, onMana, offMana,
                "start energy / Arreat Summit Mana (MAX part judged on screen)",
                lvl1 ? Verdict(MaxPart(onMana) == MaxPart(offMana)) : "n/a");
            Row(sb, "stamina/max", d.stamina + "/" + d.maxStamina, onStam, offStam,
                "charstats.txt stamina column (MAX part judged on screen; cur is gameplay state)",
                lvl1 ? Verdict(MaxPart(onStam) == MaxPart(offStam)) : "n/a");
            Row(sb, "defense", d.defense.ToString(), Text(root, "DerivedValue0"), "-", "dex/4 + armor", "n/a");
            Row(sb, "attackRating", d.attackRating.ToString(), Text(root, "ExtraValue0"), "-", "(dex-7)*5 + ToHitFactor", "n/a");
            Row(sb, "blockChance", d.blockChance + "%", Text(root, "ExtraValue1"), "-", "needs a shield in vanilla", "n/a");
            // criterion = ON SCREEN digits == the dto side (display vs logic split); '%' is stripped
            // because the panel prints "0%" while the dto carries 0.
            Row(sb, "resists", d.fireResist + "/" + d.coldResist + "/" + d.lightResist + "/" + d.poisonResist,
                onRes, "0%/0%/0%/0%", "no gear at level 1",
                Verdict(onRes.Replace("%", "") == (d.fireResist + "/" + d.coldResist + "/" + d.lightResist + "/" + d.poisonResist)));
            Row(sb, "level", d.level.ToString(), Text(root, "TopRight"), "-",
                "this project's own field (U3 put it alone into the top-right recess)", "n/a");
            // U3 split the three top slots, so the on-screen text now carries a label prefix
            // ("经验 ", "技能点 ") => the criterion is stated as "the value part is present", not an
            // exact compare (an exact compare against a naked number would be a false red).
            Row(sb, "exp", d.exp + "/" + d.expNext, onExp, "0/500",
                "experience_c level 1 = 500 (prefix added by U3)", lvl1 ? Verdict(onExp.Contains("0/500")) : "n/a");
            Row(sb, "skillPoints", d.skillPoints.ToString(), onSkill, "0",
                "vanilla level 1 has 0 skill points (prefix added by U3)",
                lvl1 ? Verdict(onSkill.TrimEnd().EndsWith(d.skillPoints.ToString())) : "n/a");
            Row(sb, "statPoints", d.statPoints.ToString(), "(not shown - the arrow brightness)", "0",
                "vanilla level 1 has 0 stat points", lvl1 ? Verdict(d.statPoints == 0) : "n/a");
            Aux.WriteFile(Aux.Dir + "/u3_charstat_readings_" + _tag + ".tsv", sb.ToString());
            Aux.Log("TSV " + Aux.Dir + "/u3_charstat_readings_" + _tag + ".tsv");
        }

        /// <summary>
        /// The text AS DRAWN for a node. Two kinds of label live in this panel:
        ///   * the NAME labels are a uGUI `Text` mirrored onto a D2Label (`UiArt.Label`);
        ///   * the VALUE labels are created straight as `D2Label.Create(...)` and carry NO uGUI
        ///     `Text` at all (see the FIT block in <see cref="DumpScreen"/>) -- for those the uGUI
        ///     route answers "(missing)", which is exactly the wrong answer for a complaint about
        ///     the numbers ON SCREEN.
        /// So: uGUI Text first, then the D2Label that really draws the node.
        /// </summary>
        private static string Text(Transform root, string node)
        {
            var go = Aux.Node(root, node);
            if (go == null) return "(missing-node)";
            var t = go.GetComponent<Text>();
            if (t != null) return t.text;
            var lbl = D2LabelOf(go.transform as RectTransform);
            var s = D2LabelProp(lbl, "text") as string;
            return s != null ? s : "(no-text-component)";
        }
        private static string Verdict(bool ok) { return ok ? "ok" : "MISMATCH"; }

        /// <summary>
        /// The `max` half of an on-screen "cur/max" string (the whole string when there is no '/').
        /// Needed because the panel prints `cur/max` while the official level-1 figure is `max/max`:
        /// comparing the whole string would call a legitimately drained current value a MISMATCH.
        /// </summary>
        private static string MaxPart(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var i = s.LastIndexOf('/');
            return i >= 0 ? s.Substring(i + 1) : s;
        }
        private static void Row(StringBuilder sb, string field, string dto, string on, string official, string source, string verdict)
        {
            sb.AppendLine(field + "\t" + dto + "\t" + on + "\t" + official + "\t" + source + "\t" + verdict);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Aux.Log("DONE reason=" + why + " statsEvents=" + _statsEvents + " shot=" + _shotDone);
            var body = new StringBuilder();
            body.AppendLine("# d2u3_charstat " + _tag + " reason=" + why);
            foreach (var l in _lines) body.AppendLine("note\t" + l);
            Aux.WriteFile(Aux.Dir + "/d2u3_charstat_" + _tag + ".log", body.ToString());
            Aux.WriteFile(Aux.DonePath,
                why + " statsEvents=" + _statsEvents + " shot=" + _shotDone + " fsm=" + Aux.Fsm() + " tag=" + _tag + "\n");
        }
    }

    /// <summary>Public run_script entries.</summary>
    public static class D2U3C
    {
        public static class Charstat
        {
            private const string DefaultDir = "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots";

            /// <summary>Zero-argument entry.</summary>
            public static string Run() { return Install(null); }

            /// <summary>spec = "&lt;shot dir&gt;|&lt;tag&gt;".</summary>
            public static string Install(string spec)
            {
                var p = (spec ?? string.Empty).Split('|');
                var dir = p.Length > 0 && p[0].Length > 0 ? p[0] : DefaultDir;
                var tag = p.Length > 1 && p[1].Length > 0 ? p[1] : "u52run1";
                var done = dir + "/d2u3_charstat_" + tag + ".done";
                var go = new GameObject("D2U3CCharstatDriver");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var drv = go.AddComponent<CharstatDriver>();
                drv.Init(dir + "|" + tag);
                // NOTE (measured 2026-09-24): do NOT create the DONE marker here. The runner
                // polls that exact path, so an install-time marker makes it believe the session
                // is over and it stops the editor mid-run (that is what killed run u52run1 right
                // after the main-menu phase). The marker is written by Finish() only.
                Aux.WriteFile(dir + "/d2u3_charstat_" + tag + ".installed",
                    "installed " + DateTime.Now.ToString("s") + "\n");
                Aux.Log("INSTALLED dir=" + dir + " tag=" + tag + " done=" + done);
                return "D2U3C-CHARSTAT-INSTALLED";
            }

            /// <summary>Warm-up entry: compiles this file without installing anything.</summary>
            public static string Ping() { return "D2U3C-PONG frame=" + Time.frameCount; }
        }
    }
}
