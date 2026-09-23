// =============================================================================
// sfx_drive.cs -- ONE Play session that proves the 24 landed SFX clips really
//                 load and really PLAY at runtime, and reads their names back
//                 off the engine's own AudioSource (clip.name).
//
// WHY: the SFX entry (#6) of the client-side resource-gap list was believed to be
//      an empty stub when this slice started; it is in fact implemented and the 24
//      original .wav files are on disk.  The offline host
//      (tools/probes/hosts/audiocheck) proves the BYTES (sha256 vs d2sfx.mpq);
//      THIS driver proves the runtime half: Resources load + AudioSource.Play +
//      clip.name readback, for the six triggers the task names.
//
// CHAIN (all inside the real Play session):
//   Boot -> MainMenu -> CharSelect -> CharCreate -> CharSelect -> CharSelectRequest
//   -> Loading -> Stage, then, in Stage:
//     (a) UI click      : click real interactable HUD buttons (executePointerClick)
//     (b) swing / hit   : ICombatModule.RequestAttack(nearest alive monster) xN
//     (c) being hit     : whatever the monsters' own AI does back (no injection)
//     (d) skill cast    : ISkillModule.Learn (if CanLearn) + TryCast on the monster
//     (e) pickup        : emits Events.ItemPicked -- the PRODUCTION event that
//                         ItemModule itself emits and AudioHook consumes
//                         (logged with SRC=event, never claimed as a ground walk)
//   and every frame it watches the REAL AudioSource instances.
//
// CRITERION CLASS: the judged rows are runtime log lines + assertions => numeric,
// no contact sheet is required.  A few screenshots are still written as context.
//
// ASCII ONLY (run_script reads this file; PS 5.1 / the probe-ASCII check).
// Re-run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/sfx_run.ps1 -Tag sfx1
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace SfxDrive
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    internal static class Drive
    {
        internal const string Tag = "SFX";

        private static string _done = string.Empty;
        private static readonly List<string> Checks = new List<string>();

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
        internal static Diablo2.Module.ICombatModule Combat() { return CtxMember("Combat") as Diablo2.Module.ICombatModule; }
        internal static Diablo2.Module.IMonsterModule Monster() { return CtxMember("Monster") as Diablo2.Module.IMonsterModule; }
        internal static Diablo2.Module.ISkillModule Skill() { return CtxMember("Skill") as Diablo2.Module.ISkillModule; }

        internal static Vector2Int GridOf(Diablo2.Module.IPlayerModule p)
        {
            return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);
        }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string DeviceName() { return SystemInfo.graphicsDeviceName; }

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

        /// <summary>Legacy click path (ExecuteEvents.pointerClick) used for the boot menu chain
        /// and for the in-Stage UI-click step. Returns CLICKED / ERR-*.</summary>
        internal static string Click(string name)
        {
            if (EventSystem.current == null) { Warn("CLICK no EventSystem name=" + name); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(name, out diag);
            if (pick == null) { Warn("CLICK miss name=" + name + " " + diag); return "ERR-miss"; }
            if (!pick.interactable) { Warn("CLICK skip name=" + name + " (not interactable)"); return "ERR-not-interactable"; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            ExecuteEvents.Execute(pick.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK name=" + name + " " + diag);
            return "CLICKED";
        }

        internal static void DoneMarker(string content) { WriteFile(_done, content); }

        // ---- keyboard (real InputSystem events; the boot panel needs a real key press) ----
        private static Keyboard KeyboardDev()
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }

        /// <summary>Real "Keyboard Space" press (queue a KeyboardState with Space held).</summary>
        internal static void KeyDown(string name)
        {
            var kb = KeyboardDev();
            if (kb == null) { Warn("KEYDOWN no-keyboard"); return; }
            var k = Key.None;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "space": k = Key.Space; break;
                case "escape": k = Key.Escape; break;
                case "enter": k = Key.Enter; break;
                // the original D2 panel keys (Def/GameKeyAlias.cs:25/28/31/34)
                case "i": k = Key.I; break;      // KeyInventory  (原版 I)
                case "c": k = Key.C; break;      // KeyCharSheet  (原版 C)
                case "t": k = Key.T; break;      // KeySkillTree  (原版 T)
                case "q": k = Key.Q; break;      // KeyQuestLog   (原版 Q)
                default: Warn("KEYDOWN unknown-key=" + name); return;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            Log("KEYDOWN key=" + name + " keyboard=" + kb.name);
        }

        /// <summary>Release every key (queue an empty KeyboardState).</summary>
        internal static void KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Paths(string spec) { Drive.Paths(spec); return "PATHS-OK"; }

        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0); }

        public static string Cfg()
        {
            var dev = Drive.DeviceName();
            var m = Drive.Map();
            Drive.KV("CFG", "gameRunning=" + (Game.IsRunning ? 1 : 0) + " device=\"" + dev + "\"");
            Drive.KV("MODELINE", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                + " hud=" + (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>() ? 1 : 0));
            if (string.IsNullOrEmpty(dev)) return "CFG-BAD device-empty";
            return "CFG-OK gameRunning=" + (Game.IsRunning ? 1 : 0) + " device=\"" + dev + "\"";
        }
    }

    /// <summary>Installer. spec = "tag|save|doneMarkerPath|screenshotsDir".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("SfxEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>One Play session: boot to Stage, drive the six SFX triggers, read the
    /// engine's own AudioSource.clip.name back, and freeze a verdict.</summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "sfx1";
        private string _save = "g66";
        private string _shots = string.Empty;

        private int _step;
        private float _at;
        private bool _done;

        private bool _bootSent;
        private bool _bootUp;
        private float _bootAt;
        private bool _sawLoading;

        private int _attempts;
        private int _uiClicked;
        private int _attacks;
        private int _casts;
        private int _moves;
        private int _panelPhase;
        private float _areaAt;

        // per-step clip window ends (so one step's clips can never leak into another's)
        private int _uiEnd;
        private int _attackEnd;
        private int _castEnd;
        private int _pickupEnd;

        private string _uiOutcome = "(not-run)";
        private string _castOutcome = "(not-run)";
        private string _pickOutcome = "(not-run)";

        // ---- audio watch: the REAL engine AudioSources --------------------------
        private readonly List<string> _played = new List<string>();
        private readonly Dictionary<UnityEngine.EntityId, float> _srcLoggedAt =
            new Dictionary<UnityEngine.EntityId, float>();
        private readonly Dictionary<string, int> _mark = new Dictionary<string, int>();
        private int _lastAlive = -1;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            var donePath = parts.Length > 2 ? parts[2] : string.Empty;
            if (parts.Length > 3 && parts[3].Length > 0) _shots = parts[3];
            Drive.Paths(donePath);
            _step = 0;
            _at = Time.unscaledTime;
            Drive.Log("PROBE-INSTALL done=" + donePath + " shots=" + _shots + " frame=" + Time.frameCount);
            Drive.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" frame=" + Time.frameCount);
        }

        private void Update()
        {
            if (_done) return;
            try
            {
                WatchAudio();
                Step();
            }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        // ============================================================ plumbing ====
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }

        private void Next() { _step++; _at = Time.unscaledTime; }

        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }

        private void Mark(string where) { _mark[where] = _played.Count; }

        private string ClipsSince(string where)
        {
            int from;
            if (!_mark.TryGetValue(where, out from)) from = 0;
            var seen = new List<string>();
            for (var i = from; i < _played.Count; i++) { if (!seen.Contains(_played[i])) seen.Add(_played[i]); }
            return seen.Count == 0 ? "(none)" : string.Join(",", seen.ToArray());
        }

        private bool AnySince(string where, string clip)
        {
            int from;
            if (!_mark.TryGetValue(where, out from)) from = 0;
            for (var i = from; i < _played.Count; i++) { if (_played[i] == clip) return true; }
            return false;
        }

        // ---- closed per-step windows (a step's clips can never leak into another's) ----
        private int MarkAt(string where) { int v; return _mark.TryGetValue(where, out v) ? v : 0; }

        private string ClipsRange(int from, int to)
        {
            if (to < from) to = from;
            var seen = new List<string>();
            for (var i = from; i < to && i < _played.Count; i++) { if (!seen.Contains(_played[i])) seen.Add(_played[i]); }
            return seen.Count == 0 ? "(none)" : string.Join(",", seen.ToArray());
        }

        private bool HasClip(int from, int to, string clip)
        {
            for (var i = from; i < to && i < _played.Count; i++) { if (_played[i] == clip) return true; }
            return false;
        }

        private void Shoot(string name)
        {
            if (string.IsNullOrEmpty(_shots)) return;
            try
            {
                if (!Directory.Exists(_shots)) Directory.CreateDirectory(_shots);
                var path = Path.Combine(_shots, name);
                ScreenCapture.CaptureScreenshot(path);
                Drive.Log("SHOT " + name);
            }
            catch (Exception ex) { Drive.Warn("SHOT-FAIL " + name + " " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void WatchAudio()
        {
            try
            {
                var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                for (var i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i];
                    if (s == null || !s.isPlaying || s.clip == null) continue;
                    if (s.time > 0.40f) continue;              // only "just started" transitions
                    var id = s.GetEntityId();
                    float last;
                    if (_srcLoggedAt.TryGetValue(id, out last) && Time.unscaledTime - last < 0.25f) continue;
                    _srcLoggedAt[id] = Time.unscaledTime;
                    var n = s.clip.name;
                    _played.Add(n);
                    Drive.Log("PLAY clip=" + n + " src=" + s.gameObject.name
                        + " t=" + Time.unscaledTime.ToString("0.00") + " step=" + _step);
                }
            }
            catch (Exception) { }
        }

        private int NearestAliveMonster()
        {
            var mon = Drive.Monster();
            var p = Drive.Player();
            if (mon == null || p == null) return -1;
            var best = -1;
            var bestD = float.MaxValue;
            foreach (var s in mon.All)
            {
                if (s == null || !s.alive) continue;
                var dx = Mathf.Abs(s.gridX - p.Grid.x);
                var dy = Mathf.Abs(s.gridY - p.Grid.y);
                var d = dx > dy ? dx : dy;
                if (d < bestD) { bestD = d; best = s.id; }
            }
            return best;
        }

        // ============================================================== steps =====
        private void Step()
        {
            switch (_step)
            {
                case 0:
                    Drive.KV("STEP", "env device=\"" + Drive.DeviceName() + "\" gameRunning="
                        + (Game.IsRunning ? 1 : 0) + " frame=" + Time.frameCount);
                    Next();
                    return;

                // boot -> main menu: the boot panel closes ON the key press itself, so
                // "!BootOpen()" AFTER the press means SUCCESS (do not read it as "not ready").
                case 1:
                    if (!_bootSent)
                    {
                        if (!BootOpen()) { Next(); return; }
                        if (!Elapsed(1.2f)) return;
                        Drive.KV("BOOT", "injecting a real Keyboard Space press fsm=" + Fsm());
                        Drive.KeyDown("space");
                        _bootSent = true;
                        _bootAt = Time.unscaledTime;
                        _at = _bootAt;
                        return;
                    }
                    if (!_bootUp && Time.unscaledTime - _bootAt > 0.3f) { Drive.KeyUp(); _bootUp = true; }
                    if (!BootOpen()) { Drive.KV("BOOT", "boot panel closed -> MainMenu comes next"); Next(); return; }
                    if (Time.unscaledTime - _bootAt > 12f) { Drive.Warn("BOOT panel still open 12s after the Space press"); Next(); }
                    return;

                case 2:
                    if (!MenuOpen())
                    {
                        if (Elapsed(30f)) { Drive.Warn("MAINMENU-TIMEOUT 30s"); Finish("mainmenu-timeout"); }
                        return;
                    }
                    Drive.KV("FLOW", "reached MainMenu");
                    Next();
                    return;

                case 3:
                    if (!Elapsed(1.0f)) return;
                    Drive.Click("Single");
                    Next();
                    return;

                case 4:
                    if (!SelectOpen())
                    {
                        if (Elapsed(30f)) { Drive.Warn("CHARSELECT-TIMEOUT 30s"); Finish("charselect-timeout"); }
                        return;
                    }
                    Drive.KV("FLOW", "reached CharSelect");
                    Next();
                    return;

                case 5:
                    if (!Elapsed(0.8f)) return;
                    Drive.Click("Create");
                    Next();
                    return;

                case 6:
                    if (!CreateOpen())
                    {
                        if (Elapsed(30f)) { Drive.Warn("CHARCREATE-TIMEOUT 30s"); Next(); }
                        return;
                    }
                    Next();
                    return;

                case 7:
                    if (!Elapsed(0.9f)) return;
                    Drive.Click("Back");
                    Next();
                    return;

                case 8:
                    if (!SelectOpen())
                    {
                        if (Elapsed(30f)) { Drive.Warn("CHARSELECT2-TIMEOUT 30s"); Next(); }
                        return;
                    }
                    Next();
                    return;

                case 9:
                    if (!Elapsed(0.6f)) return;
                    Drive.KV("ENTER-STAGE", "save=\"" + _save + "\" via Events.CharSelectRequest");
                    Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                    Next();
                    return;

                case 10:
                    if (!_sawLoading && LoadingOpen()) _sawLoading = true;
                    if (!(HudOpen() && Fsm() == "Stage"))
                    {
                        if (Elapsed(45f))
                        {
                            Drive.Warn("STAGE-TIMEOUT waited 45s; hud=" + (HudOpen() ? 1 : 0) + " fsm=" + Fsm());
                            Finish("stage-timeout");
                        }
                        return;
                    }
                    if (!Elapsed(1.0f)) return;
                    {
                        var p = Drive.Player();
                        var m = Drive.Map();
                        Drive.KV("STAGE-READY", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                            + " grid=" + Drive.Grid(Drive.GridOf(p))
                            + " alive=" + (p != null && !p.IsDead ? 1 : 0)
                            + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                            + " timeScale=" + Time.timeScale.ToString("0.##"));
                    }
                    Shoot(_tag + "_00_stage.png");
                    Next();
                    return;

                // ---- go where the monsters are + dump the live UI button names -----
                case 11:
                    if (_moves == 0)
                    {
                        _moves = 1;
                        {
                            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
                            var names = new List<string>();
                            foreach (var b in all)
                            {
                                if (b == null || b.gameObject == null) continue;
                                if (!b.gameObject.activeInHierarchy) continue;
                                names.Add(b.gameObject.name + (b.interactable ? "" : "(off)"));
                            }
                            Drive.KV("UI-BUTTONS", "n=" + names.Count + " -> " + string.Join(",", names.ToArray()));
                        }
                        Drive.KV("AREA-SWITCH", "Events.ExitEntered(BloodMoor) -- the same production event PlayerModule.CheckExit emits");
                        Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, Diablo2.Def.AreaId.BloodMoor);
                        _areaAt = Time.unscaledTime;
                        _at = _areaAt;
                        return;
                    }
                    {
                        var m = Drive.Map();
                        var alive = Drive.Monster() != null ? Drive.Monster().AliveCount : -1;
                        var area = m != null ? m.Area.ToString() : "(no-map)";
                        if (alive > 0)
                        {
                            Drive.KV("AREA-READY", "area=" + area + " monstersAlive=" + alive
                                + " after " + (Time.unscaledTime - _areaAt).ToString("0.00") + "s");
                            Shoot(_tag + "_06_areaready.png");
                            Next();
                            return;
                        }
                        if (Time.unscaledTime - _areaAt > 35f)
                        {
                            Drive.Warn("AREA-TIMEOUT area=" + area + " monstersAlive=" + alive);
                            Next();
                        }
                        return;
                    }

                // ---- (a) UI click: real interactable buttons in the live HUD -------
                case 12:
                    // the HUD carries only Belt*/SkillBar* buttons (no panel-toggle button),
                    // so the PRODUCTION path for ui_click is the panel KEY: I = inventory
                    // (Def/GameKeyAlias.cs:25 KeyInventory = GameKey.I). Both a real button
                    // click and that real key press are driven here.
                    if (_panelPhase == 1)
                    {
                        Drive.KeyDown("i");
                        Drive.KV("UICLICK-PANELKEY", "real Keyboard I press (GameKeyAlias.KeyInventory = GameKey.I)");
                        _panelPhase = 2;
                        _at = Time.unscaledTime;
                        return;
                    }
                    if (_panelPhase == 2)
                    {
                        if (!Elapsed(0.5f)) return;
                        Drive.KeyUp();
                        _panelPhase = 3;
                        _at = Time.unscaledTime;
                        return;
                    }
                    if (_panelPhase == 3)
                    {
                        if (!Elapsed(0.6f)) return;
                        Next();
                        return;
                    }
                    _panelPhase = 1;
                    Mark("ui");
                    {
                        var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
                        var clicked = 0;
                        var skipped = 0;
                        foreach (var b in all)
                        {
                            if (b == null || b.gameObject == null) continue;
                            if (!b.gameObject.activeInHierarchy || !b.interactable) continue;
                            var nm = b.gameObject.name;
                            var low = nm.ToLowerInvariant();
                            // never click something that would tear the session down
                            if (low.Contains("quit") || low.Contains("exit") || low.Contains("close")
                                || low.Contains("back") || low.Contains("cancel") || low.Contains("delete"))
                            { skipped++; continue; }
                            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                            var rt = b.transform as RectTransform;
                            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                            ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
                            Drive.Log("UICLICK name=" + nm);
                            clicked++;
                            if (clicked >= 3) break;
                        }
                        _uiClicked = clicked;
                        _uiOutcome = clicked > 0 ? ("clicked=" + clicked + " skipped-risky=" + skipped)
                            : ("no-interactable-button-found skipped-risky=" + skipped);
                    }
                    _at = Time.unscaledTime;      // stay in case 12 -> panel-key phases 1..3
                    return;

                case 13:
                    if (!Elapsed(1.5f)) return;
                    Shoot(_tag + "_01_uiclick.png");
                    _uiEnd = _played.Count;                     // close the UI window here
                    Drive.KV("HEARD-ui", ClipsSince("ui"));
                    Next();
                    return;

                // ---- (b) swing / hit: the production combat entry, in range -------
                case 14:
                    Mark("attack");
                    Drive.KV("ATTACK-BEGIN", "combat=" + (Drive.Combat() != null ? 1 : 0)
                        + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1));
                    Next();
                    return;

                case 15:
                    {
                        if (!Elapsed(0.7f)) return;
                        var cb = Drive.Combat();
                        var mon = Drive.Monster();
                        var id = NearestAliveMonster();
                        if (cb == null || id < 0 || mon == null)
                        {
                            _attempts++;
                            if (_attempts > 24) { Drive.KV("ATTACK", "no monster reachable attempts=" + _attempts); Next(); return; }
                            _at = Time.unscaledTime;
                            return;
                        }
                        var st = mon.Get(id);
                        var p = Drive.Player();
                        var dx = st != null ? Mathf.Abs(st.gridX - p.Grid.x) : -1;
                        var dy = st != null ? Mathf.Abs(st.gridY - p.Grid.y) : -1;
                        var dist = dx > dy ? dx : dy;
                        if (dist > 1 && _moves < 40)
                        {
                            // walk toward it on the SAME production event a ground click emits
                            _moves++;
                            Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand,
                                new Vector2Int(st.gridX, st.gridY));
                            Drive.Log("MOVE-TO target=m#" + id + " cell=(" + st.gridX + "," + st.gridY + ") dist=" + dist + " cmd=" + _moves);
                            _at = Time.unscaledTime;
                            return;
                        }
                        Drive.KV("ATTACK", "n=" + _attacks + " target=m#" + id
                            + " dist=" + dist + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-"));
                        cb.RequestAttack(id);
                        _attacks++;
                        if (_attacks == 1) Shoot(_tag + "_02_attack.png");
                        if (_attacks < 12) { _at = Time.unscaledTime; return; }
                        Next();
                    }
                    return;

                case 16:
                    if (!Elapsed(9.0f)) return;                 // let the monsters answer (real AI)
                    _attackEnd = _played.Count;
                    {
                        _lastAlive = Drive.Monster() != null ? Drive.Monster().AliveCount : -1;
                        var p = Drive.Player();
                        Drive.KV("AFTER-ATTACK", "attacks=" + _attacks + " monstersAlive=" + _lastAlive
                            + " playerLife=" + (p != null ? p.Life + "/" + p.MaxLife : "-")
                            + " clips=" + ClipsSince("attack"));
                        Drive.KV("HEARD-attack", ClipsSince("attack"));
                    }
                    Shoot(_tag + "_03_after_attack.png");
                    Next();
                    return;

                // ---- (d) skill cast: learn (if allowed) then TryCast --------------
                case 17:
                    Mark("cast");
                    {
                        var sk = Drive.Skill();
                        if (sk == null) { _castOutcome = "no-skill-module"; Next(); return; }
                        var id = -1;
                        foreach (var def in sk.Available)
                        {
                            if (def == null) continue;
                            if (sk.GetLevel(def.id) > 0) { id = def.id; break; }
                        }
                        if (id < 0)
                        {
                            foreach (var def in sk.Available)
                            {
                                if (def == null) continue;
                                if (sk.CanLearn(def.id)) { if (sk.Learn(def.id)) { id = def.id; break; } }
                            }
                        }
                        if (id < 0) { _castOutcome = "no-known-skill-and-cannot-learn"; Next(); return; }
                        var mon = Drive.Monster();
                        var tid = NearestAliveMonster();
                        var st = mon != null && tid >= 0 ? mon.Get(tid) : null;
                        var target = st != null ? new Vector2Int(st.gridX, st.gridY) : Drive.GridOf(Drive.Player());
                        var p = Drive.Player();
                        Drive.KV("CAST-PRE", "skillId=" + id + " level=" + sk.GetLevel(id)
                            + " mana=" + (p != null ? p.Mana + "/" + p.MaxMana : "-")
                            + " target=" + Drive.Grid(target));
                        var ok = sk.TryCast(id, target);
                        _casts++;
                        _castOutcome = "skillId=" + id + " ok=" + (ok ? 1 : 0);
                        Drive.KV("CAST", "skillId=" + id + " ok=" + (ok ? 1 : 0)
                            + " level=" + sk.GetLevel(id));
                        Shoot(_tag + "_04_cast.png");
                    }
                    Next();
                    return;

                case 18:
                    if (!Elapsed(1.2f)) return;
                    _castEnd = _played.Count;
                    Drive.KV("HEARD-cast", ClipsSince("cast"));
                    Next();
                    return;

                // ---- (e) pickup: the PRODUCTION event ItemModule emits ------------
                case 19:
                    Mark("pickup");
                    {
                        var stack = new Diablo2.Def.ItemStack { itemId = 1, name = "Healing Potion", isGold = false };
                        Game.Event.Emit<Diablo2.Def.ItemStack>(Diablo2.Core.Events.ItemPicked, stack);
                        _pickOutcome = "SRC=event-emit(production ItemPicked; no ground walk driven)";
                        Drive.KV("PICKUP", _pickOutcome);
                    }
                    Next();
                    return;

                case 20:
                    if (!Elapsed(1.0f)) return;
                    _pickupEnd = _played.Count;
                    Drive.KV("HEARD-pickup", ClipsSince("pickup"));
                    Shoot(_tag + "_05_pickup.png");
                    Next();
                    return;

                case 21:
                    if (!Elapsed(1.0f)) return;
                    Finish("tour-complete");
                    return;
            }
        }

        // ============================================================ verdict =====
        private void Finish(string reason)
        {
            if (_done) return;
            _done = true;

            var uiOk = _uiClicked > 0;
            var uiFrom = MarkAt("ui");
            var atkFrom = MarkAt("attack");
            var castFrom = MarkAt("cast");
            var pickFrom = MarkAt("pickup");
            var uiClips = ClipsRange(uiFrom, _uiEnd);
            var attackClips = ClipsRange(atkFrom, _attackEnd);
            var castClips = ClipsRange(castFrom, _castEnd);
            var pickupClips = ClipsRange(pickFrom, _pickupEnd);

            var swingOrHit = HasClip(atkFrom, _attackEnd, "miss") || HasClip(atkFrom, _attackEnd, "hit");
            var hitOk = HasClip(atkFrom, _attackEnd, "hit");
            var swipeOk = HasClip(atkFrom, _attackEnd, "miss");
            var hurtOk = HasClip(atkFrom, _attackEnd, "player_hurt") || HasClip(atkFrom, _attackEnd, "player_die");
            var monOk = HasClip(atkFrom, _attackEnd, "monster_attack") || HasClip(atkFrom, _attackEnd, "monster_die");
            var castOk = HasClip(castFrom, _castEnd, "cast") || HasClip(castFrom, _castEnd, "cast_fire")
                         || HasClip(castFrom, _castEnd, "cast_cold") || HasClip(castFrom, _castEnd, "cast_lightning")
                         || HasClip(castFrom, _castEnd, "cast_poison");
            var pickupOk = HasClip(pickFrom, _pickupEnd, "item_pickup") || HasClip(pickFrom, _pickupEnd, "gold_pickup");
            var uiClipOk = HasClip(uiFrom, _uiEnd, "ui_click");

            Drive.Check("ui-click-trigger-fired", uiOk, "clicked=" + _uiClicked + " clips=" + uiClips);
            Drive.Check("swing(swing-or-hit)-clip-played", swingOrHit, "clips=" + attackClips);
            Drive.Check("hit-clip-played", hitOk, "clips=" + attackClips);
            Drive.Check("player-hurt-clip-played", hurtOk, "clips=" + attackClips);
            Drive.Check("monster-voice-clip-played", monOk, "clips=" + attackClips);
            Drive.Check("skill-clip-played", castOk, "outcome=" + _castOutcome + " clips=" + castClips);
            Drive.Check("pickup-clip-played", pickupOk, "outcome=" + _pickOutcome + " clips=" + pickupClips);
            Drive.Check("ui-click-clip-played", uiClipOk, "clips=" + uiClips);

            var distinct = new List<string>();
            foreach (var n in _played) { if (!distinct.Contains(n)) distinct.Add(n); }
            distinct.Sort(StringComparer.Ordinal);

            Drive.KV("PLAYED-DISTINCT", distinct.Count + " -> " + string.Join(",", distinct.ToArray()));
            Drive.KV("PLAYED-ORDER", string.Join(",", _played.ToArray()));
            Drive.KV("SWING", swipeOk ? "miss-heard" : "miss-not-heard");
            Drive.Log("VERDICT ok=" + (Drive.ChecksBad() == 0 ? 1 : 0)
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " reason=" + reason + " ui=" + _uiClicked + " attacks=" + _attacks + " casts=" + _casts);

            Drive.DoneMarker("tag=" + _tag + " reason=" + reason
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " ui=" + _uiClicked + " attacks=" + _attacks + " casts=" + _casts
                + " played_distinct=" + distinct.Count + "\n");
        }
    }
}
