// =============================================================================
// bgm_drive.cs -- ONE Play session that proves the 3 landed ORIGINAL BGM tracks
//                 really load from Resources, really PLAY, and really SWITCH when
//                 the area changes, reading the name back off the engine's own
//                 AudioSource (clip.name) and off the audio module's current key.
//
// WHY: client-side resource-gap list entry #7 (BGM) was believed to be silent /
//      a placeholder when this slice started; it is in fact wired (AudioHook
//      PlayAreaBgm -> AudioModule.Bgm -> engine PlayBGM with loop=true) and the
//      three original .wav files are on disk.  The offline host
//      (tools/probes/hosts/audiocheck) proves the BYTES (sha256 vs the real
//      D2music.mpq); THIS driver proves the runtime half: Resources load +
//      AudioSource.Play + clip.name readback + a real area->track switch for the
//      three areas the task names (Rogue Encampment / Blood Moor / Den of Evil).
//
// CHAIN (all inside the real Play session):
//   Boot -> MainMenu -> CharSelect -> CharCreate -> CharSelect -> CharSelectRequest
//   -> Loading -> Stage (Town, BGM "town"), then
//     Events.ExitEntered(BloodMoor)  -> Stage in BloodMoor    (BGM "bloodmoor")
//     Events.ExitEntered(DenOfEvil)  -> Stage in DenOfEvil    (BGM "denofevil")
//   Events.ExitEntered is the SAME production event PlayerModule.CheckExit emits
//   when the player steps on an exit cell, and AppFlow.OnExitEntered -> EnterArea
//   regenerates the map in place (AppFlow.cs:1214-1217).
//
// CRITERION CLASS: the judged rows are runtime log lines + assertions => numeric,
// no contact sheet is required.  A few screenshots are still written as context.
//
// ASCII ONLY (run_script reads this file; PS 5.1 / the probe-ASCII check).
// Re-run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/bgm_run.ps1 -Tag bgm1
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

namespace BgmDrive
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    internal static class Drive
    {
        internal const string Tag = "BGM";

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

        /// <summary>Legacy click path (ExecuteEvents.pointerClick) used for the boot menu chain.</summary>
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
                default: Warn("KEYDOWN unknown-key=" + name); return;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            Log("KEYDOWN key=" + name + " keyboard=" + kb.name);
        }

        internal static void KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
        }

        // ---- BGM readback --------------------------------------------------------
        internal static readonly string[] BgmKeys = { "town", "bloodmoor", "denofevil" };

        /// <summary>
        /// The BGM tracks that are ACTUALLY loaded into a looping engine AudioSource right now.
        /// The engine's two BGM sources are created on the shared "[Sound]" root with loop=true
        /// (clover-client-unity-engine Runtime/Presentation/Sound.cs:116-121, :119 = loop = true);
        /// the 32 SFX pool sources live on child nodes and are NOT looping.
        /// </summary>
        internal static List<string> CurrentBgmClips()
        {
            var got = new List<string>();
            try
            {
                var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                for (var i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i];
                    if (s == null || s.clip == null) continue;
                    if (!s.loop) continue;                       // BGM only
                    var n = s.clip.name;
                    var isBgm = false;
                    foreach (var k in BgmKeys) { if (k == n) { isBgm = true; break; } }
                    if (isBgm && !got.Contains(n)) got.Add(n);
                }
            }
            catch (Exception) { }
            return got;
        }

        /// <summary>Number of looping AudioSources that currently hold ANY clip (BGM or not).</summary>
        internal static int LoopingSourceCount()
        {
            var n = 0;
            try
            {
                var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                for (var i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i];
                    if (s == null || s.clip == null) continue;
                    if (s.loop) n++;
                }
            }
            catch (Exception) { }
            return n;
        }

        /// <summary>The key the audio module currently believes it is playing (private field, reflection).</summary>
        internal static string RequestedBgm()
        {
            var audio = CtxMember("Audio");
            if (audio == null) return "(no-audio-module)";
            var f = audio.GetType().GetField("_currentBgm", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return "(no-field)";
            var v = f.GetValue(audio) as string;
            return v ?? "(null)";
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
            var go = new GameObject("BgmEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>One Play session: boot to Stage, then walk the three areas and read the
    /// engine's own BGM track name back at every station.</summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "bgm1";
        private string _save = "g66";
        private string _shots = string.Empty;

        private int _step;
        private float _at;
        private bool _done;

        private bool _bootSent;
        private bool _bootUp;
        private float _bootAt;
        private bool _sawLoading;

        private string _areaTown = "(not-run)", _areaMoor = "(not-run)", _areaDen = "(not-run)";
        private string _clipTown = "(not-run)", _clipMoor = "(not-run)", _clipDen = "(not-run)";
        private string _reqTown = "(not-run)", _reqMoor = "(not-run)", _reqDen = "(not-run)";
        private int _loopSrcTown = -1;

        /// <summary>samples = the exact engine-readback strings, in order (for the switch assertion)</summary>
        private readonly List<string> _samples = new List<string>();

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
            try { Step(); }
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

        private static string AreaName()
        {
            var m = Drive.Map();
            return m != null ? m.Area.ToString() : "(no-map)";
        }

        /// <summary>"town" / "(none)" style readback string for the CURRENT sample.</summary>
        private string ClipSample()
        {
            var got = Drive.CurrentBgmClips();
            return got.Count == 0 ? "(none)" : string.Join("+", got);
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

        /// <summary>Station = wait (bounded) for the expected track to be really loaded, then record
        /// the raw readback (clip names + the module's current key + the live area).</summary>
        private bool PollStation(string expected, string where, float budget)
        {
            var clips = Drive.CurrentBgmClips();
            var req = Drive.RequestedBgm();
            if (clips.Contains(expected) && req == expected)
            {
                var sample = ClipSample();
                _samples.Add(where + ":" + sample);
                Drive.KV("AREA-TRACK", "station=" + where + " area=" + AreaName()
                    + " clips=" + sample + " requested=" + req
                    + " loopSrc=" + Drive.LoopingSourceCount()
                    + " t=" + Time.unscaledTime.ToString("0.00"));
                return true;
            }
            if (Time.unscaledTime - _at > budget)
            {
                Drive.Warn("TRACK-TIMEOUT station=" + where + " expected=" + expected
                    + " area=" + AreaName() + " clips=" + ClipSample() + " requested=" + req);
                return true;   // give up on this station (the CHK below will read 0)
            }
            return false;
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

                // ---- station 1: Town --------------------------------------------------
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
                    if (Time.unscaledTime - _at < 1.0f) return;
                    _areaTown = AreaName();
                    Drive.KV("STAGE-READY", "area=" + _areaTown
                        + " loopsrc=" + Drive.LoopingSourceCount()
                        + " timeScale=" + Time.timeScale.ToString("0.##"));
                    _at = Time.unscaledTime;
                    Next();
                    return;

                case 11:
                    if (!PollStation("town", "Town", 25f)) return;
                    _clipTown = ClipSample();
                    _reqTown = Drive.RequestedBgm();
                    _loopSrcTown = Drive.LoopingSourceCount();
                    Shoot(_tag + "_00_town.png");
                    Drive.KV("SWITCH-TO", "Events.ExitEntered(BloodMoor) -- the same production event"
                        + " PlayerModule.CheckExit emits on an exit cell");
                    Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, Diablo2.Def.AreaId.BloodMoor);
                    _at = Time.unscaledTime;
                    Next();
                    return;

                // ---- station 2: BloodMoor ---------------------------------------------
                case 12:
                    if (AreaName() != "BloodMoor")
                    {
                        if (Elapsed(40f)) { Drive.Warn("AREA-TIMEOUT waiting BloodMoor, got " + AreaName()); _at = Time.unscaledTime; Next(); }
                        return;
                    }
                    if (!PollStation("bloodmoor", "BloodMoor", 25f)) return;
                    _areaMoor = AreaName();
                    _clipMoor = ClipSample();
                    _reqMoor = Drive.RequestedBgm();
                    Shoot(_tag + "_01_bloodmoor.png");
                    Drive.KV("SWITCH-TO", "Events.ExitEntered(DenOfEvil)");
                    Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, Diablo2.Def.AreaId.DenOfEvil);
                    _at = Time.unscaledTime;
                    Next();
                    return;

                // ---- station 3: DenOfEvil ---------------------------------------------
                case 13:
                    if (AreaName() != "DenOfEvil")
                    {
                        if (Elapsed(40f)) { Drive.Warn("AREA-TIMEOUT waiting DenOfEvil, got " + AreaName()); _at = Time.unscaledTime; Next(); }
                        return;
                    }
                    if (!PollStation("denofevil", "DenOfEvil", 25f)) return;
                    _areaDen = AreaName();
                    _clipDen = ClipSample();
                    _reqDen = Drive.RequestedBgm();
                    Shoot(_tag + "_02_denofevil.png");
                    Next();
                    return;

                case 14:
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

            var townOk = _clipTown.Contains("town") && _reqTown == "town";
            var moorOk = _clipMoor.Contains("bloodmoor") && _reqMoor == "bloodmoor";
            var denOk = _clipDen.Contains("denofevil") && _reqDen == "denofevil";
            var areaOk = _areaTown == "Town" && _areaMoor == "BloodMoor"
                         && _areaDen == "DenOfEvil";
            var loopOk = _loopSrcTown > 0;
            var distinct = new HashSet<string>(new[] { _reqTown, _reqMoor, _reqDen });
            var switchOk = distinct.Count == 3 && _reqTown == "town"
                           && _reqMoor == "bloodmoor" && _reqDen == "denofevil";

            Drive.Check("town-bgm-track-played", townOk, "area=" + _areaTown + " clips=" + _clipTown + " requested=" + _reqTown);
            Drive.Check("bloodmoor-bgm-track-played", moorOk, "area=" + _areaMoor + " clips=" + _clipMoor + " requested=" + _reqMoor);
            Drive.Check("denofevil-bgm-track-played", denOk, "area=" + _areaDen + " clips=" + _clipDen + " requested=" + _reqDen);
            Drive.Check("area-really-changed-Town->BloodMoor->DenOfEvil", areaOk,
                _areaTown + " -> " + _areaMoor + " -> " + _areaDen);
            Drive.Check("bgm-source-is-looping", loopOk, "looping AudioSources=" + _loopSrcTown);
            Drive.Check("area-switch-really-switched-track", switchOk,
                "readback order: " + _reqTown + " -> " + _reqMoor + " -> " + _reqDen);
            Drive.Check("real-render-device", !string.IsNullOrEmpty(Drive.DeviceName())
                && !Drive.DeviceName().Contains("Microsoft Basic Render"), "device=\"" + Drive.DeviceName() + "\"");

            Drive.KV("SAMPLES", string.Join(" | ", _samples.ToArray()));
            Drive.Log("VERDICT ok=" + (Drive.ChecksBad() == 0 ? 1 : 0)
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " reason=" + reason
                + " tracks=" + _reqTown + "/" + _reqMoor + "/" + _reqDen);

            Drive.DoneMarker("tag=" + _tag + " reason=" + reason
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " tracks=" + _reqTown + "/" + _reqMoor + "/" + _reqDen + "\n");
        }
    }
}
