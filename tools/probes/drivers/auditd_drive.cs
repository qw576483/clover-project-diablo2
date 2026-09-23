// =============================================================================
// auditd_drive.cs -- audit slice D (animation / VFX / BGM / SFX) in ONE Play
//                    session, judged by RUNTIME READINGS (never by grep).
//
// WHY: the four dimensions D5 anim / D6 vfx / D7 bgm / D8 sfx share one typical
//      failure shape -- "the asset is on disk but nothing is wired to it, so it
//      never plays" (a SILENT failure).  A resource scan or a sha256 check cannot
//      see it.  This driver reads the LIVE engine objects:
//        D5  SpriteAnimator of the real EntityView instances (Anim / FrameIndex /
//            FrameCount / CurrentKey / Finished) + the SpriteRenderer.sprite.name
//            actually on screen, per entity, sampled over time => proves the frame
//            cursor ADVANCES and that an action switch really swaps the frame group.
//        D6  the number of live effect carriers: UIText float-damage nodes,
//            Projectile_* nodes, HitFlashTimer>0 states, corpse fade.
//        D7  every playing AudioSource (clip.name + loop + volume) per scene label,
//            and the volume response to the production IAudioModule.SetVolume.
//        D8  every playing AudioSource clip name per driven EVENT WINDOW (closed
//            windows: a step's clips can never leak into another step's verdict).
//
// The boot chain + the input injection + the audio watch are the SAME machinery
// already proven by tools/probes/drivers/sfx_drive.cs (kept byte-identical in
// shape); only the judgement block is new.  sfx_drive.cs stays untouched.
//
// Judged rows are runtime log lines + assertions => numeric class, no contact
// sheet required.  Context screenshots are still written (auditD_NN_*.png).
//
// ASCII ONLY (the probe-ASCII gate; run_script reads this file as text).
// Re-run: powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/drivers/auditd_run.ps1 -Tag d1
// =============================================================================
using System;
using System.Collections;
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

namespace AuditD
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    internal static class Drive
    {
        internal const string Tag = "AUDITD";

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

        internal static string Esc(string s)
        {
            if (s == null) return "(null)";
            return s.Replace('"', '\'').Replace("\r", " ").Replace("\n", "\\n").Replace("\t", " ");
        }

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

        internal static void DoneMarker(string content) { WriteFile(_done, content); }

        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(name); } catch { t = null; }
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
        internal static Diablo2.Module.IItemModule Item() { return CtxMember("Item") as Diablo2.Module.IItemModule; }
        internal static Diablo2.Module.IAudioModule Audio() { return CtxMember("Audio") as Diablo2.Module.IAudioModule; }

        // ---- reflection into the (internal) view layer -----------------------------
        internal static object Field(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) { try { return f.GetValue(o); } catch { return null; } }
                t = t.BaseType;
            }
            return null;
        }

        internal static object Prop(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) { try { return p.GetValue(o, null); } catch { return null; } }
                t = t.BaseType;
            }
            return null;
        }

        internal static object ViewModule()
        {
            var v = CtxMember("View");
            return v != null && v.GetType().Name == "ViewModule" ? v : null;
        }

        internal static System.Collections.IDictionary Dict(object o)
        {
            return o as System.Collections.IDictionary;
        }

        /// <summary>One compact runtime line for an EntityView (animation + what is really painted).</summary>
        internal static string AnimLine(object view)
        {
            if (view == null) return "(no-view)";
            var anim = Field(view, "Anim");
            var a = Prop(anim, "Anim");
            var fi = Prop(anim, "FrameIndex");
            var fc = Prop(anim, "FrameCount");
            var ok = Prop(anim, "CurrentKey");
            var fin = Prop(anim, "Finished");
            var rend = Field(view, "Renderer") as SpriteRenderer;
            var spr = (rend != null && rend.sprite != null) ? rend.sprite.name : "(null)";
            var col = rend != null ? rend.color.ToString() : "-";
            return "Anim=" + (a != null ? a.ToString() : "?")
                 + "#" + (fi != null ? fi.ToString() : "?") + "/" + (fc != null ? fc.ToString() : "?")
                 + " fin=" + (fin != null ? fin.ToString() : "?")
                 + " key=" + (ok != null ? ok.ToString() : "(null)")
                 + " spr=" + spr + " col=" + col
                 + " phantom=" + (Field(view, "UsingPlaceholder") != null ? Field(view, "UsingPlaceholder").ToString() : "?")
                 + " flash=" + Fmt(Field(view, "HitFlashTimer"))
                 + " dead=" + (Field(view, "Dead") != null ? Field(view, "Dead").ToString() : "?")
                 + " equipKey=" + (Field(view, "EquipKey") != null ? Field(view, "EquipKey").ToString() : "(nil)");
        }

        internal static string Fmt(object o)
        {
            if (o == null) return "(null)";
            if (o is float) return ((float)o).ToString("0.00");
            return o.ToString();
        }

        internal static bool IsAnim(object view, string animName)
        {
            var anim = Field(view, "Anim");
            var a = Prop(anim, "Anim");
            return a != null && a.ToString() == animName;
        }

        internal static string SpriteName(object view)
        {
            var rend = Field(view, "Renderer") as SpriteRenderer;
            return (rend != null && rend.sprite != null) ? rend.sprite.name : "(null)";
        }

        internal static string KeyPrefix(object view)
        {
            var k = Prop(Field(view, "Anim"), "CurrentKey") as string;
            if (string.IsNullOrEmpty(k)) return "(null)";
            var i = k.LastIndexOf('_');
            return i > 0 ? k.Substring(0, i) : k;
        }

        internal static int IntField(object view, string name)
        {
            var v = Field(view, name);
            return v == null ? 0 : Convert.ToInt32(v);
        }

        // ---- live effect carriers -------------------------------------------------
        private static readonly List<string> EffectNames = new List<string>();

        /// <summary>
        /// Live effect carriers.  MUST use FindObjectsByType (not a walk of the ACTIVE scene):
        /// the engine builds its Canvas under a DontDestroyOnLoad root ("[UI]") which belongs to
        /// the DontDestroyOnLoad scene, so an active-scene walk can never see the damage float
        /// text layer (measured: the active-scene walk reported floatTextNodes=0 while a fight
        /// was landing hits).  Node names are the engine's own: FloatTextLayer builds
        /// "FloatTexts"/"FloatText"/"Label" (Runtime/Presentation/UIWidgets.cs:356,429,434).
        /// </summary>
        internal static string EffectScan()
        {
            var trs = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
            var projectiles = 0;
            var floats = 0;
            var named = new List<string>();
            for (var i = 0; i < trs.Length; i++)
            {
                var t = trs[i];
                if (t == null) continue;
                var n = t.gameObject.name;
                if (n.StartsWith("Projectile_", StringComparison.Ordinal)) projectiles++;
                else if (n == "FloatText")
                {
                    floats++;
                    if (!named.Contains(n)) named.Add(n);
                }
            }
            return "transforms=" + trs.Length + " projectileNodes=" + projectiles
                 + " floatTextNodes=" + floats + " floatNames=[" + string.Join(",", named.ToArray()) + "]";
        }

        /// <summary>Entities currently under the hit-flash window (EntityView.HitFlashTimer &gt; 0).</summary>
        internal static int FlashCount()
        {
            var vm = ViewModule();
            if (vm == null) return -1;
            var n = 0;
            var pl = Field(vm, "_player");
            if (pl != null && Convert.ToSingle(Field(pl, "HitFlashTimer")) > 0f) n++;
            var ents = Dict(Field(vm, "_entities"));
            if (ents != null)
            {
                foreach (DictionaryEntry e in ents)
                {
                    if (e.Value == null) continue;
                    if (Convert.ToSingle(Field(e.Value, "HitFlashTimer")) > 0f) n++;
                }
            }
            return n;
        }

        // ---- live audio -----------------------------------------------------------
        internal static string AudioScan(out int playingSources)
        {
            var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
            var parts = new List<string>();
            playingSources = 0;
            for (var i = 0; i < srcs.Length; i++)
            {
                var s = srcs[i];
                if (s == null || !s.isPlaying || s.clip == null) continue;
                playingSources++;
                parts.Add(s.clip.name + "@" + s.gameObject.name + "(loop=" + (s.loop ? 1 : 0)
                    + ",vol=" + s.volume.ToString("0.00") + ",t=" + s.time.ToString("0.00") + ")");
            }
            return "[" + string.Join(",", parts.ToArray()) + "]";
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

        // ---- keyboard (real InputSystem events) ------------------------------------
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
                case "i": k = Key.I; break;      // KeyInventory  (original D2 key)
                case "c": k = Key.C; break;      // KeyCharSheet
                case "t": k = Key.T; break;      // KeySkillTree
                case "q": k = Key.Q; break;      // KeyQuestLog
                case "r": k = Key.R; break;      // KeyRunToggle (Def/GameKeyAlias.cs:73)
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

        internal static Color FloatTextColor(UnityEngine.Object o)
        {
            var c = o as Component;
            if (c == null) return Color.clear;
            var txt = c.GetComponent<Text>();
            return txt != null ? txt.color : Color.clear;
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Paths(string spec) { Drive.Paths(spec); return "PATHS-OK"; }

        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0); }

        public static string Cfg()
        {
            var dev = SystemInfo.graphicsDeviceName;
            Drive.KV("CFG", "gameRunning=" + (Game.IsRunning ? 1 : 0) + " device=\"" + dev + "\"");
            if (string.IsNullOrEmpty(dev)) return "CFG-BAD device-empty";
            return "CFG-OK gameRunning=" + (Game.IsRunning ? 1 : 0) + " device=\"" + dev + "\"";
        }

        /// <summary>Ad-hoc single readout (numeric class).</summary>
        public static string Snap()
        {
            int n;
            var audio = Drive.AudioScan(out n);
            var vm = Drive.ViewModule();
            var pl = vm != null ? Drive.Field(vm, "_player") : null;
            Drive.KV("SNAP", "fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)")
                + " effects{" + Drive.EffectScan() + "} audio" + audio
                + " player{" + Drive.AnimLine(pl) + "}");
            return "SNAP-OK playing=" + n;
        }
    }

    /// <summary>Installer. spec = "tag|save|doneMarkerPath|screenshotsDir".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("AuditDEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>One Play session: boot to Stage, drive the four dimensions, freeze verdicts.</summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "d1";
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
        private int _moves;
        private int _attacks;
        private int _casts;
        private int _phase;
        private float _areaAt;
        private bool _hudQueried;

        // ---- closed event windows --------------------------------------------------
        private readonly List<string> _played = new List<string>();
        private readonly Dictionary<UnityEngine.EntityId, float> _srcLoggedAt =
            new Dictionary<UnityEngine.EntityId, float>();
        private readonly List<string> _sceneLog = new List<string>();
        private readonly Dictionary<string, int> _mark = new Dictionary<string, int>();
        private readonly List<string> _frameAdvances = new List<string>();
        private readonly HashSet<string> _animsSeen = new HashSet<string>();
        private readonly List<string> _probeLines = new List<string>();

        private int _uiEnd, _atkEnd, _castEnd, _pickEnd, _readEnd, _hitEnd, _deathEnd, _equipEnd;
        private string _lastSceneLabel = "(init)";

        private readonly Dictionary<int, int> _lastFrame = new Dictionary<int, int>();
        private readonly Dictionary<int, int> _advances = new Dictionary<int, int>();

        private string _runSeen = "(no-run-toggle)";
        private string _equipBefore = "(not-run)";
        private string _equipAfterDrop = "(not-run)";
        private string _equipAfterRe = "(not-run)";
        private string _volLine = "(not-run)";
        private string _npcLine = "(not-run)";
        private string _npcLineLate = "(not-run)";
        private int _monsterSeen;
        private string _deathLine = "(not-run)";
        private float _installedAt;
        private float _switchAt = -1f;
        private int _switchFrom = -1;
        private string _crossfadeLine = "(not-run)";

        /// <summary>One line for all 5 town NPC views (anim + frame-advance count since install).</summary>
        private string NpcAdvLine()
        {
            var vm2 = Drive.ViewModule();
            var npcs2 = Drive.Dict(vm2 != null ? Drive.Field(vm2, "_npcs") : null);
            var parts = new List<string>();
            if (npcs2 != null)
            {
                foreach (DictionaryEntry e in npcs2)
                {
                    var k = e.Key is int ? (int)e.Key : -999;
                    parts.Add("npc" + k + " adv=" + Advances(-(100000 + k)) + " {" + Drive.AnimLine(e.Value) + "}");
                }
            }
            return "npcs=" + (npcs2 != null ? npcs2.Count : -1) + " -> " + string.Join(" ", parts.ToArray());
        }

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
            if (_installedAt <= 0f) _installedAt = Time.unscaledTime;
            if (Time.unscaledTime - _installedAt > 420f)
            {
                Drive.Warn("WATCHDOG 420s reached -> finishing with whatever was collected");
                Finish("watchdog-420s");
                return;
            }
            try
            {
                WatchAudio();
                WatchScene();
                WatchAnim();
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
        private static bool PauseOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.PausePanel>(); }
        private static bool InvOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(); }

        private void Mark(string where) { _mark[where] = _played.Count; }
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

        private void Note(string s) { if (_probeLines.Count < 400) _probeLines.Add(s); Drive.KV("NOTE", s); }

        private void Shoot(string name)
        {
            if (string.IsNullOrEmpty(_shots)) return;
            try
            {
                if (!Directory.Exists(_shots)) Directory.CreateDirectory(_shots);
                ScreenCapture.CaptureScreenshot(Path.Combine(_shots, name));
                Drive.Log("SHOT " + name);
            }
            catch (Exception ex) { Drive.Warn("SHOT-FAIL " + name + " " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- scene label + BGM transitions ---------------------------------------
        private string SceneLabel()
        {
            if (Game.UI != null)
            {
                if (LoadingOpen()) return "loading";
                if (BootOpen()) return "boot";
                if (MenuOpen()) return "mainmenu";
                if (CreateOpen()) return "charcreate";
                if (SelectOpen()) return "charselect";
                if (PauseOpen()) return "pause";
                if (InvOpen()) return "inventory";
            }
            var m = Drive.Map();
            if (m != null)
            {
                var area = m.Area.ToString();
                if (HudOpen()) return "stage:" + area;
                return "stage?:" + area;
            }
            return "fsm=" + Fsm();
        }

        private void WatchScene()
        {
            var label = SceneLabel();
            if (label == _lastSceneLabel) return;
            var prev = _lastSceneLabel;
            _lastSceneLabel = label;
            int n;
            var audio = Drive.AudioScan(out n);
            var line = "SCENE " + prev + " -> " + label + " playing=" + n + " " + audio
                       + " t=" + Time.unscaledTime.ToString("0.00");
            _sceneLog.Add(line);
            Drive.Log(line);
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

        // ---- D5: frame-advance watcher --------------------------------------------
        private void WatchAnim()
        {
            var vm = Drive.ViewModule();
            if (vm == null) return;
            Sniff(vm, Drive.Field(vm, "_player"), 1);
            var ents = Drive.Dict(Drive.Field(vm, "_entities"));
            if (ents != null)
            {
                foreach (DictionaryEntry e in ents)
                {
                    var id = e.Key is int ? (int)e.Key : -999;
                    Sniff(vm, e.Value, id);
                }
            }
            var npcs = Drive.Dict(Drive.Field(vm, "_npcs"));
            if (npcs != null)
            {
                foreach (DictionaryEntry e in npcs)
                {
                    var id = e.Key is int ? -(100000 + (int)e.Key) : -999;
                    Sniff(vm, e.Value, id);
                }
            }
        }

        private void Sniff(object vm, object view, int id)
        {
            if (view == null) return;
            var anim = Drive.Field(view, "Anim");
            if (anim == null) return;
            var a = Drive.Prop(anim, "Anim");
            if (a != null) _animsSeen.Add(a.ToString());
            var fi = Drive.Prop(anim, "FrameIndex");
            if (fi == null) return;
            var cur = Convert.ToInt32(fi);
            int last;
            if (_lastFrame.TryGetValue(id, out last))
            {
                if (cur != last)
                {
                    int acc;
                    _advances.TryGetValue(id, out acc);
                    _advances[id] = acc + 1;
                }
            }
            _lastFrame[id] = cur;
        }

        private int Advances(int id) { int v; return _advances.TryGetValue(id, out v) ? v : 0; }
        private static int PlayerId() { return Diablo2.Core.GameConst.PlayerEntityId; }

        private string PlayerLine(string where)
        {
            var vm = Drive.ViewModule();
            var p = vm != null ? Drive.Field(vm, "_player") : null;
            return where + " player{" + Drive.AnimLine(p) + "}";
        }

        private string MonsterLines()
        {
            var vm = Drive.ViewModule();
            var ents = Drive.Dict(vm != null ? Drive.Field(vm, "_entities") : null);
            var parts = new List<string>();
            var n = 0;
            if (ents != null)
            {
                foreach (DictionaryEntry e in ents)
                {
                    var id = e.Key is int ? (int)e.Key : -999;
                    if (id < 1000) continue;
                    n++;
                    if (parts.Count < 6) parts.Add("m" + id + "{" + Drive.AnimLine(e.Value) + "}");
                }
            }
            _monsterSeen = n;
            return "monsters=" + n + " -> " + string.Join(" ", parts.ToArray());
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
                    Drive.KV("STEP", "env device=\"" + SystemInfo.graphicsDeviceName + "\" gameRunning="
                        + (Game.IsRunning ? 1 : 0) + " frame=" + Time.frameCount
                        + " resolution=" + Screen.width + "x" + Screen.height);
                    Drive.KV("SUBSCRIBE", "view-layer reflection into Diablo2.Module.View.ViewModule{_player,_entities,_npcs}"
                        + " + EntityView.Anim(SpriteAnimator) + SpriteRenderer.sprite.name; audio = live AudioSource scan");
                    Next();
                    return;

                // ---- boot -> main menu -------------------------------------------------
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
                        var m = Drive.Map();
                        var p = Drive.Player();
                        Drive.KV("STAGE-READY", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                            + " grid=" + (p != null ? p.Grid.ToString() : "-")
                            + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1));
                    }
                    Shoot(_tag + "_00_town.png");
                    Next();
                    return;

                // ---- D5-a: town NPC views + player idle --------------------------------
                case 11:
                    if (!Elapsed(1.5f)) return;
                    {
                        _npcLine = NpcAdvLine();
                        Drive.KV("D5-NPC", _npcLine);
                        Drive.KV("D5-IDLE", PlayerLine("town-idle"));
                        Drive.KV("D6-TOWN", Drive.EffectScan());
                        Drive.KV("D7-TOWN-BGM", "sceneLabel=" + _lastSceneLabel);
                    }
                    Shoot(_tag + "_01_npc.png");
                    Next();
                    return;

                // ---- D5-b: walk (real MoveCommand) -------------------------------------
                case 12:
                    Mark("walk");
                    {
                        var p = Drive.Player();
                        var g = p != null ? p.Grid : new Vector2Int(0, 0);
                        Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand, new Vector2Int(g.x + 4, g.y + 4));
                        Drive.KV("D5-WALK-CMD", "MoveCommand(" + (g.x + 4) + "," + (g.y + 4) + ") from (" + g.x + "," + g.y + ")");
                    }
                    Next();
                    return;

                case 13:
                    if (!Elapsed(2.0f)) return;
                    Drive.KV("D5-WALK", PlayerLine("walk") + " advances=" + Advances(PlayerId())
                        + " clips=" + ClipsRange(MarkAt("walk"), _played.Count));
                    _readEnd = _played.Count;
                    Shoot(_tag + "_02_walk.png");
                    Next();
                    return;

                // ---- D5-c: run toggle (real R key) -------------------------------------
                case 14:
                    Drive.KV("D5-RUN-KEY", "real Keyboard R press (GameKeyAlias.KeyRunToggle=GameKey.R)");
                    Drive.KeyDown("r");
                    Next();
                    return;

                case 15:
                    if (!Elapsed(0.35f)) return;
                    Drive.KeyUp();
                    Next();
                    return;

                case 16:
                    if (!Elapsed(0.4f)) return;
                    {
                        var p = Drive.Player();
                        var g = p != null ? p.Grid : new Vector2Int(0, 0);
                        Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand, new Vector2Int(g.x - 4, g.y - 4));
                        Drive.KV("D5-RUN-CMD", "MoveCommand(" + (g.x - 4) + "," + (g.y - 4)
                            + ") isRunning=" + (p != null ? p.IsRunning.ToString() : "?"));
                    }
                    Next();
                    return;

                case 17:
                    if (!Elapsed(2.0f)) return;
                    {
                        var p = Drive.Player();
                        _runSeen = (p != null ? "contractIsRunning=" + p.IsRunning : "no-player");
                        Drive.KV("D5-RUN", PlayerLine("run") + " " + _runSeen);
                    }
                    Shoot(_tag + "_03_run.png");
                    Next();
                    return;

                // ---- D5-d: equip visual group swap (production item API) ---------------
                case 18:
                    {
                        var vm = Drive.ViewModule();
                        var p = vm != null ? Drive.Field(vm, "_player") : null;
                        _equipBefore = "equipKey=" + (Drive.Field(p, "EquipKey") != null ? Drive.Field(p, "EquipKey").ToString() : "(nil)")
                            + " keyPrefix=" + Drive.KeyPrefix(p) + " spr=" + Drive.SpriteName(p);
                        Drive.KV("D5-EQUIP-BEFORE", _equipBefore);
                        var it = Drive.Item();
                        var slotType = Drive.FindType("Diablo2.Def.ItemSlot");
                        var dropped = "no-item-module";
                        if (it != null && slotType != null)
                        {
                            try
                            {
                                var weapon = Enum.Parse(slotType, "Weapon");
                                var m = it.GetType().GetMethod("Unequip");
                                var ok = m != null && (bool)m.Invoke(it, new object[] { weapon, 0 });
                                dropped = "Unequip(Weapon,0)=" + ok;
                            }
                            catch (Exception ex) { dropped = "Unequip-threw:" + ex.GetType().Name; }
                        }
                        Drive.KV("D5-EQUIP-DROP", dropped);
                    }
                    Next();
                    return;

                case 19:
                    if (!Elapsed(0.8f)) return;
                    {
                        var vm = Drive.ViewModule();
                        var p = vm != null ? Drive.Field(vm, "_player") : null;
                        _equipAfterDrop = "equipKey=" + (Drive.Field(p, "EquipKey") != null ? Drive.Field(p, "EquipKey").ToString() : "(nil)")
                            + " keyPrefix=" + Drive.KeyPrefix(p) + " spr=" + Drive.SpriteName(p);
                        Drive.KV("D5-EQUIP-AFTER-DROP", _equipAfterDrop);
                    }
                    Shoot(_tag + "_04_equip_drop.png");
                    Next();
                    return;

                // 1) equip whatever the save already carries; 2) if the visible EquipKey still did not
                //    change, produce items through the PRODUCTION loot API (CreateRandom +
                //    AddToInventory + EquipFromInventory) and equip them until the frame group swaps.
                case 20:
                    {
                        var it = Drive.Item();
                        var res = "no-item-module";
                        var loot = "(not-needed)";
                        if (it != null)
                        {
                            var vm0 = Drive.ViewModule();
                            var p0 = vm0 != null ? Drive.Field(vm0, "_player") : null;
                            var key0 = p0 != null && Drive.Field(p0, "EquipKey") != null ? Drive.Field(p0, "EquipKey").ToString() : "(nil)";
                            try
                            {
                                var done = false;
                                var inv = it.Inventory;
                                if (inv != null)
                                {
                                    for (var i = 0; i < inv.Count && !done; i++)
                                    {
                                        var sl = inv[i];
                                        if (sl == null || !sl.occupied || !sl.isAnchor) continue;
                                        var ai = sl.anchorIndex >= 0 ? sl.anchorIndex : sl.index;
                                        if (it.EquipFromInventory(ai)) { res = "EquipFromInventory(anchor=" + ai + ")=true"; done = true; }
                                    }
                                }
                                if (!done) res = "no-anchor-in-inventory";

                                var vm1 = Drive.ViewModule();
                                var p1 = vm1 != null ? Drive.Field(vm1, "_player") : null;
                                var key1 = p1 != null && Drive.Field(p1, "EquipKey") != null ? Drive.Field(p1, "EquipKey").ToString() : "(nil)";

                                if (key1 == key0)
                                {
                                    var rngType = Drive.FindType("Diablo2.Def.Rng");
                                    object rng = null;
                                    if (rngType != null)
                                    {
                                        var ctor = rngType.GetConstructor(new Type[] { typeof(int) });
                                        if (ctor != null) rng = ctor.Invoke(new object[] { 20260923 });
                                    }
                                    var mCr = it.GetType().GetMethod("CreateRandom");
                                    var mAdd = it.GetType().GetMethod("AddToInventory");
                                    var mEq = it.GetType().GetMethod("EquipFromInventory");
                                    var tried = 0;
                                    var equipped = 0;
                                    if (rng != null && mCr != null && mAdd != null && mEq != null)
                                    {
                                        for (var k = 0; k < 16; k++)
                                        {
                                            var item = mCr.Invoke(it, new object[] { 2 + k, rng });
                                            if (item == null) continue;
                                            tried++;
                                            var added = (bool)mAdd.Invoke(it, new object[] { item });
                                            if (!added) continue;
                                            var snap = it.Snapshot();
                                            var ai = -1;
                                            if (snap != null && snap.inventory != null)
                                            {
                                                for (var i = 0; i < snap.inventory.Count; i++)
                                                {
                                                    var s = snap.inventory[i];
                                                    if (s == null || !s.occupied || !s.isAnchor || s.item == null) continue;
                                                    if (!ReferenceEquals(s.item, item)) continue;
                                                    ai = s.anchorIndex >= 0 ? s.anchorIndex : s.index;
                                                    break;
                                                }
                                            }
                                            if (ai < 0) continue;
                                            if ((bool)mEq.Invoke(it, new object[] { ai })) equipped++;
                                        }
                                        loot = "lootPath tried=" + tried + " equipCalls=" + equipped;
                                    }
                                    else { loot = "lootPath unavailable (rng=" + (rng != null) + " methods=" + (mCr != null) + "/" + (mAdd != null) + "/" + (mEq != null) + ")"; }
                                }
                            }
                            catch (Exception ex) { res = "Equip-threw:" + ex.GetType().Name + ":" + ex.Message; }
                        }
                        Drive.KV("D5-EQUIP-RE", res);
                        Drive.KV("D5-EQUIP-LOOT", loot);
                    }
                    Next();
                    return;

                case 21:
                    if (!Elapsed(0.8f)) return;
                    {
                        var vm = Drive.ViewModule();
                        var p = vm != null ? Drive.Field(vm, "_player") : null;
                        _equipAfterRe = "equipKey=" + (Drive.Field(p, "EquipKey") != null ? Drive.Field(p, "EquipKey").ToString() : "(nil)")
                            + " keyPrefix=" + Drive.KeyPrefix(p) + " spr=" + Drive.SpriteName(p)
                            + " frameCount=" + Drive.Prop(Drive.Field(p, "Anim"), "FrameCount");
                        Drive.KV("D5-EQUIP-AFTER-RE", _equipAfterRe);
                    }
                    Shoot(_tag + "_05_equip_re.png");
                    Drive.KV("D6-TOWN-AFTER", Drive.EffectScan());
                    _npcLineLate = NpcAdvLine();
                    Drive.KV("D5-NPC-LATE", _npcLineLate);
                    Next();
                    return;

                // ---- D7: volume control via the production audio facade -----------------
                case 22:
                    {
                        var au = Drive.Audio();
                        if (au == null) { Drive.Warn("D7-VOLUME no-audio-module"); _volLine = "no-audio-module"; Next(); return; }
                        Drive.KV("D7-VOLUME-BEFORE", "BgmVolume=" + au.BgmVolume.ToString("0.00")
                            + " SfxVolume=" + au.SfxVolume.ToString("0.00") + " " + AudioPlaying());
                        au.SetVolume(0.2f, 0.2f);
                    }
                    Next();
                    return;

                case 23:
                    if (!Elapsed(0.6f)) return;
                    {
                        var au = Drive.Audio();
                        _volLine = "afterSetVolume(0.2,0.2) BgmVolume=" + (au != null ? au.BgmVolume.ToString("0.00") : "?")
                            + " " + AudioPlaying();
                        Drive.KV("D7-VOLUME-AFTER", _volLine);
                    }
                    Next();
                    return;

                case 24:
                    {
                        var au = Drive.Audio();
                        if (au != null) au.SetVolume(1f, 1f);
                        Drive.KV("D7-VOLUME-RESTORE", "SetVolume(1,1) " + AudioPlaying());
                    }
                    Next();
                    return;

                // ---- D5-e: leave town -> BloodMoor, monsters -----------------------------
                case 25:
                    {
                        var au = Drive.Audio();
                        Drive.KV("AREA-SWITCH", "Events.ExitEntered(BloodMoor) -- the same production event PlayerModule.CheckExit emits");
                        Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, Diablo2.Def.AreaId.BloodMoor);
                        _areaAt = Time.unscaledTime;
                    }
                    Next();
                    return;

                case 26:
                    {
                        var m = Drive.Map();
                        var alive = Drive.Monster() != null ? Drive.Monster().AliveCount : -1;
                        if (alive > 0)
                        {
                            Drive.KV("AREA-READY", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                                + " monstersAlive=" + alive + " after "
                                + (Time.unscaledTime - _areaAt).ToString("0.00") + "s");
                            Next();
                            return;
                        }
                        if (Time.unscaledTime - _areaAt > 35f)
                        {
                            Drive.Warn("AREA-TIMEOUT area=" + (m != null ? m.Area.ToString() : "(no-map)") + " monstersAlive=" + alive);
                            Next();
                        }
                        return;
                    }

                // ---- D5-f: monster idle/walk anim ---------------------------------------
                case 27:
                    Mark("monwatch");
                    {
                        var p = Drive.Player();
                        var g = p != null ? p.Grid : new Vector2Int(0, 0);
                        Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand, new Vector2Int(g.x + 2, g.y + 2));
                        Drive.KV("D5-MON-IDLE", MonsterLines());
                    }
                    Next();
                    return;

                case 28:
                    if (!Elapsed(1.6f)) return;
                    Drive.KV("D5-MON", MonsterLines() + " clips=" + ClipsRange(MarkAt("monwatch"), _played.Count));
                    _crossfadeLine = AudioPlaying();
                    Drive.KV("D7-MOOR", "sceneLabel=" + _lastSceneLabel + " " + _crossfadeLine);
                    Shoot(_tag + "_06_moor.png");
                    Next();
                    return;

                // ---- D8/D5: combat (attack loop) ---------------------------------------
                case 29:
                    Mark("attack");
                    Drive.KV("ATTACK-BEGIN", "combat=" + (Drive.Combat() != null ? 1 : 0)
                        + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1));
                    Next();
                    return;

                case 30:
                    {
                        if (!Elapsed(0.6f)) return;
                        var cb = Drive.Combat();
                        var mon = Drive.Monster();
                        var id = NearestAliveMonster();
                        if (cb == null || id < 0 || mon == null)
                        {
                            _attempts++;
                            if (_attempts > 30) { Drive.KV("ATTACK", "no monster reachable attempts=" + _attempts); Next(); return; }
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
                            _moves++;
                            Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand, new Vector2Int(st.gridX, st.gridY));
                            Drive.Log("MOVE-TO target=m#" + id + " cell=(" + st.gridX + "," + st.gridY + ") dist=" + dist + " cmd=" + _moves);
                            _at = Time.unscaledTime;
                            return;
                        }
                        cb.RequestAttack(id);
                        _attacks++;
                        if (_attacks == 1) { Drive.KV("ATTACK-FIRST", PlayerLine("attack") + " target=m#" + id + " dist=" + dist); Shoot(_tag + "_07_attack.png"); }
                        if (_attacks < 14) { _at = Time.unscaledTime; return; }
                        Next();
                    }
                    return;

                case 31:
                    if (!Elapsed(8.0f)) return;
                    _atkEnd = _played.Count;
                    {
                        var p = Drive.Player();
                        Drive.KV("D5-ATTACK", PlayerLine("after-attack") + " advances=" + Advances(PlayerId())
                            + " attacks=" + _attacks);
                        Drive.KV("D5-MON-ATTACK", MonsterLines());
                        Drive.KV("D6-COMBAT", Drive.EffectScan() + " flashEntities=" + Drive.FlashCount());
                        Drive.KV("D8-ATTACK", "clips=" + ClipsRange(MarkAt("attack"), _atkEnd));
                    }
                    Shoot(_tag + "_08_combat.png");
                    Next();
                    return;

                // ---- D8: hurt (whatever the real monster AI does back) ------------------
                case 32:
                    Mark("hurt");
                    Next();
                    return;

                case 33:
                    if (!Elapsed(3.0f)) return;
                    _hitEnd = _played.Count;
                    {
                        Drive.KV("D5-HIT", PlayerLine("hurt-window"));
                        Drive.KV("D8-HURT", "clips=" + ClipsRange(MarkAt("hurt"), _hitEnd));
                    }
                    Next();
                    return;

                // ---- D8/D5: skill cast ---------------------------------------------------
                case 34:
                    Mark("cast");
                    {
                        var sk = Drive.Skill();
                        var id = -1;
                        if (sk == null) { Drive.Warn("D5-CAST no-skill-module"); Next(); return; }
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
                        if (id < 0) { Drive.Warn("D5-CAST no-known-skill-and-cannot-learn"); Next(); return; }
                        var mon = Drive.Monster();
                        var tid = NearestAliveMonster();
                        var st = mon != null && tid >= 0 ? mon.Get(tid) : null;
                        var target = st != null ? new Vector2Int(st.gridX, st.gridY) : Drive.Player().Grid;
                        var ok = sk.TryCast(id, target);
                        _casts++;
                        Drive.KV("D5-CAST", "skillId=" + id + " ok=" + (ok ? 1 : 0) + " target=" + target.ToString()
                            + " mana=" + Drive.Player().Mana + "/" + Drive.Player().MaxMana);
                        Shoot(_tag + "_09_cast.png");
                    }
                    Next();
                    return;

                case 35:
                    if (!Elapsed(0.35f)) return;
                    Drive.KV("D5-CAST-MID", PlayerLine("cast-mid") + " " + Drive.EffectScan() + " flashEntities=" + Drive.FlashCount());
                    Next();
                    return;

                case 36:
                    if (!Elapsed(1.5f)) return;
                    _castEnd = _played.Count;
                    Drive.KV("D5-CAST-END", PlayerLine("cast-end") + " " + Drive.EffectScan());
                    Drive.KV("D8-CAST", "clips=" + ClipsRange(MarkAt("cast"), _castEnd));
                    Next();
                    return;

                // ---- D8: pickup (production ItemPicked event) ----------------------------
                case 37:
                    Mark("pickup");
                    {
                        var stack = new Diablo2.Def.ItemStack { itemId = 1, name = "Healing Potion", isGold = false };
                        Game.Event.Emit<Diablo2.Def.ItemStack>(Diablo2.Core.Events.ItemPicked, stack);
                        Drive.KV("PICKUP", "SRC=event-emit (the production events ItemModule itself emits; no ground walk driven)");
                    }
                    Next();
                    return;

                case 38:
                    if (!Elapsed(0.9f)) return;
                    _pickEnd = _played.Count;
                    Drive.KV("D8-PICKUP", "clips=" + ClipsRange(MarkAt("pickup"), _pickEnd));
                    Next();
                    return;

                // ---- D5-g: death animation (production IViewModule.PlayDeath) ------------
                case 39:
                    {
                        var vm = Drive.ViewModule();
                        var m = vm != null ? vm.GetType().GetMethod("PlayDeath") : null;
                        if (m == null) { Drive.Warn("D5-DEATH PlayDeath not found on ViewModule"); _deathLine = "no-method"; Next(); return; }
                        m.Invoke(vm, new object[] { PlayerId() });
                        Drive.KV("D5-DEATH-CALL", "IViewModule.PlayDeath(playerId=" + PlayerId()
                            + ") -- the same production view entry DamagePipeline calls; NOT a fake clip play");
                    }
                    Next();
                    return;

                case 40:
                    if (!Elapsed(0.5f)) return;
                    Drive.KV("D5-DEATH-MID", PlayerLine("death-mid"));
                    Next();
                    return;

                case 41:
                    if (!Elapsed(3.0f)) return;
                    {
                        var vm = Drive.ViewModule();
                        var p = vm != null ? Drive.Field(vm, "_player") : null;
                        var anim = Drive.Field(p, "Anim");
                        _deathLine = PlayerLine("death-final")
                            + " finalFrame=" + Drive.Prop(anim, "FrameIndex") + "/" + Drive.Prop(anim, "FrameCount")
                            + " finished=" + Drive.Prop(anim, "Finished")
                            + " corpseFaded=" + Drive.Field(p, "CorpseFaded")
                            + " bar=" + (Drive.Field(p, "Bar") != null ? "exists" : "null");
                        Drive.KV("D5-DEATH", _deathLine);
                        Drive.KV("D6-DEATH", Drive.EffectScan());
                    }
                    Shoot(_tag + "_10_death.png");
                    Next();
                    return;

                // ---- D7: pause panel BGM + shop-ish labels -------------------------------
                case 42:
                    Drive.KV("D7-PAUSE-KEY", "real Keyboard Escape press (production pause hotkey)");
                    Drive.KeyDown("escape");
                    Next();
                    return;

                case 43:
                    if (!Elapsed(0.4f)) return;
                    Drive.KeyUp();
                    Next();
                    return;

                case 44:
                    if (!Elapsed(0.8f)) return;
                    Drive.KV("D7-PAUSE", "sceneLabel=" + _lastSceneLabel + " pauseOpen=" + (PauseOpen() ? 1 : 0)
                        + " " + AudioPlaying());
                    Shoot(_tag + "_11_pause.png");
                    Next();
                    return;

                case 45:
                    {
                        var it = Drive.Item();
                        var line = "no-item-module";
                        if (it != null)
                        {
                            try
                            {
                                var inv = it.Inventory;
                                var used = false;
                                if (inv != null)
                                {
                                    for (var i = 0; i < inv.Count && !used; i++)
                                    {
                                        var sl = inv[i];
                                        if (sl == null || !sl.occupied || !sl.isAnchor) continue;
                                        if (it.UseItem(sl.anchorIndex)) { line = "UseItem(anchor=" + sl.anchorIndex + ")=true"; used = true; }
                                    }
                                }
                                if (!used) line = "no-usable-anchor";
                            }
                            catch (Exception ex) { line = "UseItem-threw:" + ex.GetType().Name; }
                        }
                        Drive.KV("D8-ITEMUSE", line);
                    }
                    Next();
                    return;

                case 46:
                    if (!Elapsed(1.2f)) return;
                    Drive.KV("D8-ITEMUSE-CLIPS", "clips=" + ClipsRange(MarkAt("pickup"), _played.Count));
                    Next();
                    return;

                // ---- D7: den BGM ---------------------------------------------------------
                case 47:
                    {
                        Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, Diablo2.Def.AreaId.DenOfEvil);
                        _areaAt = Time.unscaledTime;
                        Drive.KV("AREA-SWITCH-DEN", "Events.ExitEntered(DenOfEvil)");
                    }
                    Next();
                    return;

                case 48:
                    if (Time.unscaledTime - _areaAt < 2.5f) return;
                    {
                        var m = Drive.Map();
                        Drive.KV("D7-DEN", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                            + " sceneLabel=" + _lastSceneLabel + " " + AudioPlaying());
                    }
                    Shoot(_tag + "_12_den.png");
                    Next();
                    return;

                case 49:
                    if (!Elapsed(1.0f)) return;
                    Finish("tour-complete");
                    return;
            }
        }

        private string AudioPlaying()
        {
            int n;
            var a = Drive.AudioScan(out n);
            return "playing=" + n + " " + a;
        }

        // ============================================================ verdict =====
        private void Finish(string reason)
        {
            if (_done) return;
            _done = true;

            // D5 rows
            var vm = Drive.ViewModule();
            var p = vm != null ? Drive.Field(vm, "_player") : null;
            var walkClips = ClipsRange(MarkAt("walk"), _readEnd);
            var atkFrom = MarkAt("attack");
            var castFrom = MarkAt("cast");
            var pickFrom = MarkAt("pickup");
            var hurtFrom = MarkAt("hurt");

            var animSet = new List<string>(_animsSeen);
            animSet.Sort(StringComparer.Ordinal);

            Drive.KV("D5-ANIM-SET-SEEN", "viewAnim values observed at runtime: [" + string.Join(",", animSet.ToArray()) + "]");
            Drive.KV("D5-FRAME-ADVANCES", "player=" + Advances(PlayerId()) + " (monsters/npcs advance counts are in the per-entity lines)");
            Drive.KV("D5-NPC-FINAL", _npcLine);
            Drive.KV("D5-RUN-FINAL", _runSeen);
            Drive.KV("D5-EQUIP-BEFORE", _equipBefore);
            Drive.KV("D5-EQUIP-AFTER-DROP", _equipAfterDrop);
            Drive.KV("D5-EQUIP-AFTER-RE", _equipAfterRe);
            Drive.KV("D5-DEATH-FINAL", _deathLine);
            Drive.KV("D5-MON-FINAL", MonsterLines());
            Drive.KV("D5-NPC-FINAL", _npcLineLate);

            // D7 rows
            Drive.KV("D7-CROSSFADE", _crossfadeLine);
            Drive.KV("D7-SCENE-LOG", "transitions=" + _sceneLog.Count);
            foreach (var l in _sceneLog) Drive.Log(l);
            Drive.KV("D7-VOLUME", _volLine);

            // D8 rows
            Drive.KV("D8-ATTACK-CLIPS", ClipsRange(atkFrom, _atkEnd));
            Drive.KV("D8-HURT-CLIPS", ClipsRange(hurtFrom, _hitEnd));
            Drive.KV("D8-CAST-CLIPS", ClipsRange(castFrom, _castEnd));
            Drive.KV("D8-PICKUP-CLIPS", ClipsRange(pickFrom, _pickEnd));

            var distinct = new List<string>();
            foreach (var n in _played) { if (!distinct.Contains(n)) distinct.Add(n); }
            distinct.Sort(StringComparer.Ordinal);
            Drive.KV("D8-PLAYED-DISTINCT", distinct.Count + " -> " + string.Join(",", distinct.ToArray()));

            // ---- assertions --------------------------------------------------------
            var walkOk = _animsSeen.Contains("Walk");
            var idleOk = _animsSeen.Contains("Idle");
            var attackOk = _animsSeen.Contains("Attack");
            var castOk = _animsSeen.Contains("Cast");
            var deathOk = _animsSeen.Contains("Death");
            var hitOk = _animsSeen.Contains("Hit");
            var playerAdv = Advances(PlayerId());
            var npcAdv = 0;
            var npcs = Drive.Dict(vm != null ? Drive.Field(vm, "_npcs") : null);
            if (npcs != null) foreach (DictionaryEntry e in npcs) { npcAdv += Advances(-(100000 + (e.Key is int ? (int)e.Key : -1))); }
            var monAdv = 0;
            var ents = Drive.Dict(vm != null ? Drive.Field(vm, "_entities") : null);
            if (ents != null) foreach (DictionaryEntry e in ents) { var id = e.Key is int ? (int)e.Key : -1; if (id >= 1000) monAdv += Advances(id); }

            var equipKeyChanged = !string.Equals(_equipBefore, _equipAfterDrop, StringComparison.Ordinal)
                                  && !string.Equals(_equipAfterDrop, _equipAfterRe, StringComparison.Ordinal);

            var atkClips = ClipsRange(atkFrom, _atkEnd);
            var hurtClips = ClipsRange(hurtFrom, _hitEnd);
            var castClips = ClipsRange(castFrom, _castEnd);
            var pickClips = ClipsRange(pickFrom, _pickEnd);

            Drive.Check("D5-player-frame-cursor-advances", playerAdv > 3, "player advances=" + playerAdv);
            Drive.Check("D5-idle-anim-observed", idleOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-walk-anim-observed", walkOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-attack-anim-observed", attackOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-cast-anim-observed", castOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-hit-anim-observed", hitOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-death-anim-observed", deathOk, "anims=" + string.Join("/", animSet.ToArray()));
            Drive.Check("D5-npc-view-frame-advances", npcAdv > 0, "npc advances=" + npcAdv + " " + _npcLine);
            Drive.Check("D5-monster-view-frame-advances", monAdv > 0, "monster advances=" + monAdv + " seen=" + _monsterSeen);
            Drive.Check("D5-equip-group-swaps-both-ways", equipKeyChanged,
                "before=" + _equipBefore + " | drop=" + _equipAfterDrop + " | re=" + _equipAfterRe);
            Drive.Check("D8-attack-window-has-swing-or-hit",
                atkClips.Contains("miss") || atkClips.Contains("hit") || atkClips.Contains("monster_attack")
                || atkClips.Contains("monster_die") || atkClips.Contains("player_hurt"), "clips=" + atkClips);
            Drive.Check("D8-hurt-window-has-player-or-monster-voice",
                hurtClips.Contains("player_hurt") || hurtClips.Contains("player_die")
                || hurtClips.Contains("monster_attack") || hurtClips.Contains("hit"), "clips=" + hurtClips);
            Drive.Check("D8-cast-window-has-cast-clip",
                castClips.Contains("cast") || castClips.Contains("cast_fire") || castClips.Contains("cast_cold")
                || castClips.Contains("cast_lightning") || castClips.Contains("cast_poison"), "clips=" + castClips);
            Drive.Check("D8-pickup-window-has-pickup-clip",
                pickClips.Contains("item_pickup") || pickClips.Contains("gold_pickup"), "clips=" + pickClips);

            Drive.Log("VERDICT ok=" + (Drive.ChecksBad() == 0 ? 1 : 0)
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " reason=" + reason + " sceneTransitions=" + _sceneLog.Count + " attacks=" + _attacks + " casts=" + _casts);

            Drive.DoneMarker("tag=" + _tag + " reason=" + reason
                + " checks_ok=" + Drive.ChecksOk() + " checks_bad=" + Drive.ChecksBad()
                + " attacks=" + _attacks + " casts=" + _casts
                + " distinct_clips=" + distinct.Count + "\n");
        }
    }
}
