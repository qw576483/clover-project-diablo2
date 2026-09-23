// t0_drive.cs -- T0 batch Play driver: S2 performance / D6 fx / D12 flow / S3 settings.
//
// One Play session drives Boot -> MainMenu -> CharSelect -> CharCreate -> CharSelect ->
// Loading -> Stage -> Pause -> Resume -> MainMenu, logging per-station side effects (panels /
// timeScale / live timer count) and measuring:
//   * head-60-frame + steady-state frame pacing (Time.unscaledDeltaTime) with the render device,
//   * the MapView RebuildLayers pave (ShowArea) and the incremental RefreshVisibleChunks pave,
//     each wrapped in a Stopwatch, plus the per-GameObject build cost (us/GO),
//   * the D6 fx entries (PlayHit / PlayDeath / ShowFloatingText): sprite presence, duration and
//     the event -> presentation wiring (driven through the real DamagePipeline via Combat).
// A separate cold start reads the persisted setting keys back (S3).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace T0
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Drive
    {
        internal const string Tag = "T0";
        internal const int ShotW = 1920;
        internal const int ShotH = 1080;

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;
        private static string _pendFile = string.Empty;
        private static string _pendKind = string.Empty;
        private static int _pendIdx;

        internal static string DonePath { get { return _done; } }
        internal static string RawDir { get { return _rawDir; } }

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
            if (p.Length > 0) _rawDir = p[0];
            if (p.Length > 1) _done = p[1];
            Log("PATHS rawDir=" + _rawDir + " done=" + _done);
            return "PATHS-OK";
        }

        // ---- capture -------------------------------------------------------------
        internal static void BeginShot(int i, string name, string kind)
        {
            _pendIdx = i; _pendFile = _rawDir + "/" + name; _pendKind = kind;
            try { if (File.Exists(_pendFile)) File.Delete(_pendFile); } catch { }
            Log("SHOT-REQUEST n=" + i + " name=" + name + " kind=" + kind);
        }

        internal static void FlushShot()
        {
            if (_pendFile.Length == 0) return;
            var file = _pendFile; var kind = _pendKind; var idx = _pendIdx;
            _pendFile = string.Empty;
            try
            {
                var d = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);

                if (kind == "screen") { UnityEngine.ScreenCapture.CaptureScreenshot(file); Log("SHOT-ISSUED n=" + idx + " kind=screen file=" + file); return; }

                var cam = Camera.main;
                if (cam == null) { Warn("SHOT-FAIL n=" + idx + " Camera.main null"); return; }
                RenderTexture rt = null; Texture2D tex = null;
                try
                {
                    rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
                    var prev = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = prev;
                    RenderTexture.active = rt;
                    tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0f, 0f, ShotW, ShotH), 0, 0); tex.Apply();
                    RenderTexture.active = null;
                    var b = tex.EncodeToPNG();
                    File.WriteAllBytes(file, b);
                    Log("SHOT-ISSUED n=" + idx + " kind=camera file=" + file + " bytes=" + b.Length);
                }
                finally
                {
                    RenderTexture.active = null;
                    if (rt != null) RenderTexture.ReleaseTemporary(rt);
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                }
            }
            catch (Exception e) { Warn("SHOT-FAIL n=" + idx + " " + e.GetType().Name + " " + e.Message); }
        }

        // ---- reflection ----------------------------------------------------------
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
        internal static Diablo2.Module.ICombatModule Combat() { return CtxMember("Combat") as Diablo2.Module.ICombatModule; }
        internal static Diablo2.Module.IViewModule View() { return CtxMember("View") as Diablo2.Module.IViewModule; }
        internal static Diablo2.Module.ICameraRig Rig() { return CtxMember("Camera") as Diablo2.Module.ICameraRig; }
        internal static Diablo2.Module.IAudioModule Audio() { return CtxMember("Audio") as Diablo2.Module.IAudioModule; }
        internal static Diablo2.Module.ISaveModule Save() { return CtxMember("Save") as Diablo2.Module.ISaveModule; }
        internal static Diablo2.Module.IQuestModule Quest() { return CtxMember("Quest") as Diablo2.Module.IQuestModule; }
        internal static Diablo2.Module.ISkillModule Skill() { return CtxMember("Skill") as Diablo2.Module.ISkillModule; }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }

        internal static bool SetField(object o, string n, object v)
        {
            if (o == null) return false;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(o, v); return true;
        }

        internal static float FloatField(object o, string n) { var v = Field(o, n); return v is float ? (float)v : -999f; }
        internal static bool BoolField(object o, string n) { var v = Field(o, n); return v is bool && (bool)v; }

        internal static object EntityView(int id)
        {
            var vm = CtxMember("View");
            if (vm == null) return null;
            var f = vm.GetType().GetField("_entities", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return null;
            var dict = f.GetValue(vm) as IDictionary;
            if (dict == null || !dict.Contains(id)) return null;
            return dict[id];
        }

        internal static int TimerCount()
        {
            var t = Game.Timer;
            if (t == null) return -1;
            var f = t.GetType().GetField("_entries", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return -1;
            var l = f.GetValue(t) as ICollection;
            return l != null ? l.Count : -1;
        }

        internal static int NodeCount(Transform root)
        {
            if (root == null) return -1;
            return root.GetComponentsInChildren<Transform>(true).Length;
        }

        /// <summary>Read the persisted settings.json (proves which keys were actually written).</summary>
        internal static string ReadSettingsFile()
        {
            try
            {
                var p = Path.Combine(Application.dataPath, "..", "setting", "settings.json");
                return File.Exists(p) ? File.ReadAllText(p) : "(missing:" + p + ")";
            }
            catch (Exception e) { return "(err:" + e.GetType().Name + ")"; }
        }

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)"; var vram = -1;
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            try { vram = SystemInfo.graphicsMemorySize; } catch { }
            return "DEVICE graphicsDeviceName=\"" + dev + "\" graphicsDeviceType=" + typ + " vramMB=" + vram
                + " resolution=" + Screen.width + "x" + Screen.height
                + " vSyncCount=" + QualitySettings.vSyncCount
                + " targetFrameRate=" + Application.targetFrameRate
                + " qualityLevel=" + QualitySettings.GetQualityLevel()
                + " screenFullScreen=" + (Screen.fullScreen ? 1 : 0)
                + " clock=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>Read every persisted setting the S3 batch cares about (no mutation).</summary>
        internal static string SettingsLine(string where)
        {
            var s = Game.Setting;
            if (s == null) return "SETTINGS where=" + where + " (no Game.Setting)";
            var bgmV = s.Get<float>("audio/bgm_volume", -1f);
            var sfxV = s.Get<float>("audio/sfx_volume", -1f);
            var quality = s.Get<int>("video/quality", -999);
            var full = s.Get<bool>("video/fullscreen", false);
            var bgmM = s.Get<bool>("audio/bgm_mute", false);
            var sfxM = s.Get<bool>("audio/sfx_mute", false);
            return "SETTINGS where=" + where
                + " audio/bgm_volume=" + bgmV.ToString("0.###")
                + " audio/sfx_volume=" + sfxV.ToString("0.###")
                + " video/quality=" + quality
                + " video/fullscreen=" + (full ? 1 : 0)
                + " audio/bgm_mute=" + (bgmM ? 1 : 0)
                + " audio/sfx_mute=" + (sfxM ? 1 : 0)
                + " engineQuality=" + QualitySettings.GetQualityLevel()
                + " screenFullScreen=" + (Screen.fullScreen ? 1 : 0);
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
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.PausePanel>(), "Pause");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(), "Settings");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.DeathPanel>(), "Death");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(), "Inventory");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.SkillTreePanel>(), "SkillTree");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>(), "MiniMap");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.QuestLogPanel>(), "QuestLog");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>(), "Dialog");
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.ShopPanel>(), "Shop");
            return sb.Length > 0 ? sb.ToString() : "(none)";
        }

        private static void AddIf(StringBuilder sb, bool on, string name)
        {
            if (!on) return;
            if (sb.Length > 0) sb.Append(',');
            sb.Append(name);
        }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        /// <summary>Per-station side-effect line (D12 evidence).</summary>
        internal static string Station(string tag)
        {
            var m = Map();
            return "STATION " + tag + " fsm=" + Fsm() + " scene=" + SceneName()
                + " area=" + (m != null ? m.Area.ToString() : "-")
                + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                + " panels=" + Panels()
                + " timeScale=" + Time.timeScale.ToString("0.##")
                + " timers=" + TimerCount()
                + " t=" + Time.time.ToString("0.00");
        }
    }

    /// <summary>One-shot entries for the run script.</summary>
    public static class Api2
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        /// <summary>Read-only startup snapshot (cold start S3 evidence) -- no env mutation.</summary>
        public static string StartupRead(string note)
        {
            Drive.Log(Drive.DeviceLine());
            Drive.Log(Drive.SettingsLine("coldstart" + (string.IsNullOrEmpty(note) ? "" : ":" + note)));
            Drive.Log("SETTINGS-FILE where=coldstart raw=" + Drive.Esc(Drive.ReadSettingsFile()));
            return "STARTUP-READ-OK";
        }

        /// <summary>Editor/play setup so injected input really reaches the game.</summary>
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = UnityEngine.InputSystem.InputSystem.settings;
            st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
            if (UnityEngine.InputSystem.Mouse.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + Drive.Fsm()
                       + " modules=" + ModulesLine();
            Drive.Log(line);
            Drive.Log(Drive.DeviceLine());
            Drive.Log(Drive.SettingsLine("cfg"));
            return line;
        }

        internal static string ModulesLine()
        {
            return "Map=" + (Drive.Map() != null ? 1 : 0) + " Player=" + (Drive.Player() != null ? 1 : 0)
                + " Monster=" + (Drive.Monster() != null ? 1 : 0) + " Combat=" + (Drive.Combat() != null ? 1 : 0)
                + " View=" + (Drive.View() != null ? 1 : 0) + " Camera=" + (Drive.Rig() != null ? 1 : 0)
                + " Audio=" + (Drive.Audio() != null ? 1 : 0) + " Save=" + (Drive.Save() != null ? 1 : 0);
        }

        /// <summary>Memory footprint at Stage (S2 内存占用 rows).</summary>
        public static string Mem()
        {
            long total = -1; long mono = -1; int gfx = -1; int sys = -1; string dev = "(unknown)";
            try { total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); } catch { }
            try { mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(); } catch { }
            try { gfx = SystemInfo.graphicsMemorySize; } catch { }
            try { sys = SystemInfo.systemMemorySize; } catch { }
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            var line = "MEM profilerTotalMB=" + (total / 1048576.0).ToString("0.0")
                + " monoUsedMB=" + (mono / 1048576.0).ToString("0.0")
                + " graphicsMemoryMB=" + gfx + " systemMemoryMB=" + sys
                + " fsm=" + Drive.Fsm() + " device=\"" + dev + "\"";
            Drive.Log(line);
            return line;
        }

        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("T0EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<T0Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>The T0 station plan (one Play session).</summary>
    public class T0Driver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;

        private readonly List<float> _head = new List<float>();
        private bool _headLogged;
        private readonly List<float> _steady = new List<float>();
        private float _steadyAt = -1f; private bool _steadyLogged;
        private int _dmg; private int _kills;
        private int _shotSeq;

        private float _lastPaveUs; private string _lastPaveTag = "";
        private readonly Dictionary<string, float> _paveUs = new Dictionary<string, float>();
        private float _chunkUs; private int _chunkNodes; private List<string> _chunkLog = new List<string>();
        private bool _moorRequested; private int _stFrame = -1;

        private int _monId = -1;
        private int _attackSeen;
        private bool _deathSeen;

        private string _saveName = "";

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            Drive.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            Drive.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height);
            Subscribe();
            BuildPlan();
            Drive.KV("PLAN", "stations=" + _plan.Count);
        }

        private void Subscribe()
        {
            if (Game.Event == null) { Drive.Warn("SUBSCRIBE Game.Event null"); return; }
            Game.Event.On<Diablo2.Def.DamageArgs>(Diablo2.Core.Events.DamageDealt, OnDamage);
            Game.Event.On<int>(Diablo2.Core.Events.MonsterKilled, OnKilled);
        }

        private void OnDamage(Diablo2.Def.DamageArgs a) { _dmg++; }
        private void OnKilled(int id) { _kills++; }

        private void Update()
        {
            CollectHead();
            CollectSteady();
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
                Drive.Log("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        private void LateUpdate() { try { Drive.FlushShot(); } catch { } }

        // ---- pacing ---------------------------------------------------------------
        private void CollectHead()
        {
            if (_headLogged) return;
            _head.Add(Time.unscaledDeltaTime);
            if (_head.Count < 60) return;
            _headLogged = true;
            Drive.KV("PACING-HEAD60", Stats(_head) + " device=\"" + SafeDev() + "\" resolution=" + Screen.width + "x" + Screen.height);
        }

        private void CollectSteady()
        {
            if (_steadyAt < 0f || _steadyLogged) return;
            _steady.Add(Time.unscaledDeltaTime);
            if (_steady.Count < 180) return;
            _steadyLogged = true;
            var cam = Camera.main;
            Drive.KV("PACING-STEADY", Stats(_steady)
                + " where=stage-fixedcam frameStart=" + Time.frameCount
                + " camPos=" + (cam != null ? cam.transform.position.x.ToString("0.00") + "," + cam.transform.position.y.ToString("0.00") + "," + cam.transform.position.z.ToString("0.00") : "-")
                + " ortho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " device=\"" + SafeDev() + "\" resolution=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate);
        }

        private static string Stats(List<float> v)
        {
            var a = new List<float>(v); a.Sort();
            var sum = 0f; foreach (var x in a) sum += x;
            Func<float, float> ms = s => s * 1000f;
            return "n=" + a.Count
                + " min=" + ms(a[0]).ToString("0.00")
                + " p05=" + ms(a[(int)(0.05f * (a.Count - 1))]).ToString("0.00")
                + " p50=" + ms(a[(int)(0.50f * (a.Count - 1))]).ToString("0.00")
                + " p95=" + ms(a[(int)(0.95f * (a.Count - 1))]).ToString("0.00")
                + " max=" + ms(a[a.Count - 1]).ToString("0.00")
                + " mean=" + ms(sum / a.Count).ToString("0.00")
                + " (ms/frame)";
        }

        private static string SafeDev() { try { return SystemInfo.graphicsDeviceName; } catch { return "(unknown)"; } }

        // ---- plan plumbing ---------------------------------------------------------
        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            _plan.Add(() =>
            {
                if (_cur != nm) { _cur = nm; _at = Time.unscaledTime; _stFrame = -1; Drive.Log("PHASE " + nm + " " + Drive.Station("enter")); }
                return f();
            });
        }

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }

        private string _pendTile = ""; private float _shotAt;
        private void Shot(string tile, string kind) { Drive.BeginShot(_shotSeq++, tile, kind); _pendTile = tile; _shotAt = Time.unscaledTime; }

        /// <summary>True once the previously requested shot file exists (shots flush one per frame).</summary>
        private bool ShotDone()
        {
            if (_pendTile.Length == 0) return true;
            try { if (File.Exists(Drive.RawDir + "/" + _pendTile)) { _pendTile = ""; return true; } } catch { }
            if (Time.unscaledTime - _shotAt > 5f) { _pendTile = ""; return true; }
            return false;
        }

        private static bool FsmEq(string s) { return Drive.Fsm() == s; }

        private static GameObject MapViewGo()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? arr[0].gameObject : null;
        }

        private static object MapViewObj()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? (object)arr[0] : null;
        }

        private static int BuiltChunks(object mv)
        {
            if (mv == null) return -1;
            var p = mv.GetType().GetProperty("BuiltChunkCount", BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -1;
            var v = p.GetValue(mv);
            return v is int ? (int)v : -1;
        }

        private static string InvokePrivate(object o, string method)
        {
            if (o == null) return "(no-obj)";
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return "(no-method)";
            try { m.Invoke(o, null); return "ok"; } catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        // ============================ the plan =====================================
        private void BuildPlan()
        {
            // ---- 1. head-60 pacing + device --------------------------------------
            Add("head60", () => _headLogged);

            // ---- 2. Boot -> MainMenu ---------------------------------------------
            Add("boot", () =>
            {
                if (FsmEq("MainMenu")) { Drive.Log(Drive.Station("D12-MainMenu") + " via=TriggerBootDone"); return true; }
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(25f)) { Drive.Warn("boot timeout, fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- 3. MainMenu -> CharSelect ---------------------------------------
            Add("charselect", () =>
            {
                if (FsmEq("CharSelect")) { Drive.Log(Drive.Station("D12-CharSelect")); return true; }
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(20f)) { Drive.Warn("charselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- 4. CharSelect -> CharCreate -------------------------------------
            Add("charcreate", () =>
            {
                if (FsmEq("CharCreate")) { Drive.Log(Drive.Station("D12-CharCreate")); return true; }
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
                if (Elapsed(20f)) { Drive.Warn("charcreate timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- 5. CharCreate -> CharSelect -------------------------------------
            Add("backselect", () =>
            {
                if (FsmEq("CharSelect")) { Drive.Log(Drive.Station("D12-CharSelect-again") + " via=TriggerCreated"); return true; }
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                if (Elapsed(20f)) { Drive.Warn("backselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- 6. CharSelect -> Loading -> Stage -------------------------------
            Add("loading", () =>
            {
                if (FsmEq("Stage")) { return true; }
                if (FsmEq("Loading")) { if (!_loadingLogged) { _loadingLogged = true; Drive.Log(Drive.Station("D12-Loading")); } return false; }
                if (Elapsed(1.0f)) EnterStage();
                if (Elapsed(60f)) { Drive.Warn("loading timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("stage", () =>
            {
                if (!FsmEq("Stage") || !(Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>()))
                {
                    if (Elapsed(20f)) { Drive.Warn("stage wait timeout fsm=" + Drive.Fsm()); return true; }
                    return false;
                }
                if (!Elapsed(2.0f)) return false;
                Drive.Log(Drive.Station("D12-Stage") + " TRANS=TriggerStageReady->StateStage");
                var rig = Drive.Rig(); if (rig != null) { rig.SnapToTarget(); }
                Shot("t0_stage_town.png", "screen");
                return true;
            });

            // ---- 7. steady-state frame pacing ------------------------------------
            Add("steady", () =>
            {
                if (_steadyAt < 0f) { if (!Elapsed(0.5f)) return false; _steadyAt = 1f; _steady.Clear(); _steadyLogged = false; return false; }
                return _steadyLogged;
            });

            // ---- 8/9. MapView ShowArea (RebuildLayers) spike: Town cold + warm --
            Add("map-showarea", () => { Pave("town-cold"); return true; });
            Add("map-count", () =>
            {
                if (!WaitFrames(2)) return false;
                CountPave("town-cold");
                Pave("town-warm");
                return true;
            });
            Add("map-count2", () =>
            {
                if (!WaitFrames(2)) return false;
                CountPave("town-warm");
                return true;
            });

            // ---- 10. switch to BloodMoor (monsters + bigger map / chunked path) --
            Add("to-moor", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor) return true;
                if (!_moorRequested) { _moorRequested = true; Drive.Log("MOOR-ENTER emit Events.ExitEntered(BloodMoor)"); Game.Event.Emit<Diablo2.Def.AreaId>(Diablo2.Core.Events.ExitEntered, AreaId.BloodMoor); }
                if (Elapsed(30f)) { Drive.Warn("to-moor timeout area=" + (m != null ? m.Area.ToString() : "-")); return true; }
                return false;
            });
            Add("moor-settle", () =>
            {
                if (!ShotDone()) return false;
                if (!WaitFrames(30) && !Elapsed(3f)) return false;
                var m = Drive.Map();
                Drive.Log(Drive.Station("D12-Stage-moor")
                    + " monsters=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                    + " chunked=" + Drive.BoolField(MapViewObj(), "_chunked"));
                Shot("t0_stage_moor.png", "screen");
                return true;
            });
            Add("map-showarea-moor", () => { Pave("moor-cold"); return true; });
            Add("moor-count", () =>
            {
                if (!WaitFrames(2)) return false;
                CountPave("moor-cold");
                return true;
            });

            // ---- 11. RefreshVisibleChunks (incremental chunk path, 3 camera steps) ----
            Add("chunk-spike", () =>
            {
                var mv = MapViewObj();
                if (mv == null) { Drive.Warn("chunk-spike: no MapView"); _chunkUs = -1f; return true; }
                var m = Drive.Map();
                var pl = Drive.Player();
                var rig = Drive.Rig();
                var baseG = pl != null ? pl.Grid : new Vector2Int(0, 0);
                var steps = new[] { 0, 8, 20 };
                _chunkLog = new List<string>();
                var viewCam = Drive.Field(mv, "ViewCamera") as Camera;
                if (viewCam == null) viewCam = Camera.main;
                var w0 = Iso.GridToWorld(new Vector2Int(0, 0));
                var w8 = Iso.GridToWorld(new Vector2Int(8, 8));
                var perCell = (w8 - w0) / 8f;
                for (var i = 0; i < steps.Length; i++)
                {
                    var g = new Vector2Int(baseG.x + steps[i], baseG.y + (i == 2 ? steps[i] : 0));
                    if (rig != null) { rig.SetTargetGrid(g); rig.SnapToTarget(); }
                    if (viewCam != null)
                        viewCam.transform.position = new Vector3(w0.x + perCell.x * g.x, w0.y + perCell.y * g.y, viewCam.transform.position.z);
                    if (i == 0) Drive.SetField(mv, "_hasChunkRange", false);
                    var before = BuiltChunks(mv);
                    var bw = _chunkTotalNodes();
                    var sw = Stopwatch.StartNew();
                    var r = InvokePrivate(mv, "RefreshVisibleChunks");
                    sw.Stop();
                    var us = (float)(sw.Elapsed.TotalMilliseconds * 1000.0);
                    var after = BuiltChunks(mv);
                    _chunkLog.Add("step" + i + " camGrid=" + g.x + "," + g.y + " invoke=" + r
                        + " wall_us=" + us.ToString("0.0") + " chunksBefore=" + before + " chunksAfter=" + after
                        + " deltaChunks=" + (after - before) + " nodesBefore=" + bw);
                    if (i == 0) { _chunkUs = us; }
                }
                Drive.KV("SPIKE-CHUNK", "what=RefreshVisibleChunks(incremental) area=" + (m != null ? m.Area.ToString() : "-")
                    + " chunked=" + Drive.BoolField(mv, "_chunked") + " chunkSize=" + StaticInt("Diablo2.Module.Map.MapView", "ChunkSize")
                    + " viewCam=" + (viewCam != null ? viewCam.name : "(null)")
                    + " | " + string.Join(" | ", _chunkLog.ToArray()));
                return true;
            });

            Add("chunk-count", () =>
            {
                if (!WaitFrames(2)) return false;
                var go = MapViewGo();
                _chunkNodes = go != null ? Drive.NodeCount(go.transform) : -1;
                Drive.KV("SPIKE-CHUNK-COST", "nodesUnderMapRoot=" + _chunkNodes
                    + " firstStepUs=" + _chunkUs.ToString("0.0")
                    + " us_per_GO_full_root=" + (_chunkNodes > 0 ? (_chunkUs / _chunkNodes).ToString("0.000") : "-"));
                return true;
            });

            // ---- 11. D6 setup: pick a live monster, stand next to it -------------
            Add("d6-setup", () =>
            {
                if (!ShotDone() || !WaitFrames(1)) return false;
                var mon = Drive.Monster();
                if (mon == null) { Drive.Warn("d6-setup: no IMonsterModule"); return true; }
                _monId = -1;
                var all = mon.All;
                for (var i = 0; i < all.Count; i++) { var s = all[i]; if (s != null && s.alive) { _monId = s.id; break; } }
                if (_monId < 0) { Drive.Warn("d6-setup: no alive monster in area " + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")); return true; }
                var st = mon.Get(_monId);
                var pl = Drive.Player(); var rig = Drive.Rig();
                if (pl != null && st != null) { pl.TeleportTo(new Vector2Int(st.gridX, st.gridY - 1)); if (rig != null) { rig.SetTargetGrid(pl.Grid); rig.SnapToTarget(); } }
                var v = Drive.EntityView(_monId);
                var go = Drive.View() != null ? Drive.View().GetView(_monId) : null;
                var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                Drive.KV("D6-SETUP", "m#" + _monId + " kind=" + (st != null ? st.kindId : -1)
                    + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " viewGO=" + (go != null ? go.name : "(null)")
                    + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "(null)")
                    + " spriteNotNull=" + (sr != null && sr.sprite != null ? 1 : 0)
                    + " entityViewFound=" + (v != null ? 1 : 0));
                Shot("t0_d6_monster.png", "camera");
                return true;
            });

            Add("d6-hit-api", () =>
            {
                if (!ShotDone() || !WaitFrames(1)) return false;
                if (_monId < 0) return true;
                var view = Drive.View();
                if (view == null) { Drive.Warn("d6-hit-api: no IViewModule"); return true; }
                view.PlayHit(_monId);
                var v = Drive.EntityView(_monId);
                var flash = Drive.FloatField(v, "HitFlashTimer");
                var sr = null as SpriteRenderer;
                var go = view.GetView(_monId); if (go != null) sr = go.GetComponent<SpriteRenderer>();
                Drive.KV("D6-PLAYHIT", "api=View.PlayHit(" + _monId + ") hitFlashTimer=" + flash.ToString("0.###")
                    + " flashSecConst=0.12 sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "(null)")
                    + " spriteNotNull=" + (sr != null && sr.sprite != null ? 1 : 0)
                    + " duration_ok=" + (flash > 0f && flash <= GameConst.FloatTextDuration ? 1 : 0));
                Shot("t0_d6_hit.png", "camera");
                return true;
            });

            Add("d6-attack", () =>
            {
                if (_monId < 0) return true;
                var cb = Drive.Combat();
                if (cb == null) { Drive.Warn("d6-attack: no ICombatModule"); return true; }
                if (_attackSeen == 0) { cb.RequestAttack(_monId); _attackSeen = 1; _at = Time.unscaledTime - 2f; }
                if (_dmg == 0 && !Elapsed(3f)) return false;
                var v = Drive.EntityView(_monId);
                var flash = Drive.FloatField(v, "HitFlashTimer");
                var st = Drive.Monster() != null ? Drive.Monster().Get(_monId) : null;
                Drive.KV("D6-TRIGGER-PIPE", "what=Combat.RequestAttack->DamagePipeline->View.PlayHit+ShowFloatingText"
                    + " damageEvents=" + _dmg + " monsterHp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " viewHitFlashTimer=" + flash.ToString("0.###")
                    + " wired=" + (_dmg > 0 ? 1 : 0));
                return true;
            });

            Add("d6-float", () =>
            {
                if (!ShotDone() || !WaitFrames(1)) return false;
                var view = Drive.View();
                if (view == null) return true;
                var pl = Drive.Player();
                var w = pl != null ? pl.World : Vector3.zero;
                view.ShowFloatingText(w.x, w.y + 1.2f, w.z, "-12", 0xFFFF3C3Cu);
                Drive.KV("D6-FLOATTEXT", "api=View.ShowFloatingText(-12) at=" + w.x.ToString("0.00") + "," + w.y.ToString("0.00")
                    + " durationConst=" + GameConst.FloatTextDuration.ToString("0.###")
                    + " uiNotNull=" + (Game.UI != null ? 1 : 0));
                Shot("t0_d6_float.png", "screen");
                return true;
            });

            Add("d6-death", () =>
            {
                if (!ShotDone() || !WaitFrames(1)) return false;
                if (_monId < 0) return true;
                var mon = Drive.Monster();
                if (mon == null) return true;
                mon.ApplyDamage(_monId, 99999, DamageType.Physical);
                var v = Drive.EntityView(_monId);
                _deathSeen = Drive.BoolField(v, "Dead");
                var go = Drive.View() != null ? Drive.View().GetView(_monId) : null;
                var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                Drive.KV("D6-PLAYDEATH", "api=Monster.ApplyDamage(99999)->View.PlayDeath(" + _monId + ")"
                    + " viewDead=" + (_deathSeen ? 1 : 0) + " kills=" + _kills
                    + " sprite=" + (sr != null && sr.sprite != null ? sr.sprite.name : "(null)")
                    + " spriteNotNull=" + (sr != null && sr.sprite != null ? 1 : 0));
                Shot("t0_d6_death.png", "camera");
                return true;
            });

            // ---- 12. Stage -> Pause ---------------------------------------------
            Add("pause", () =>
            {
                if (FsmEq("Pause")) { Drive.Log(Drive.Station("D12-Pause") + " TRANS=TriggerPause->StatePause"); return true; }
                if (Elapsed(0.6f)) Game.Event.Emit(Diablo2.Core.Events.PauseRequest);
                if (Elapsed(15f)) { Drive.Warn("pause timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("pause-shot", () =>
            {
                if (!ShotDone()) return false;
                if (!Elapsed(0.4f)) return false;
                Shot("t0_d12_pause.png", "screen");
                return true;
            });

            // ---- 13. open Options (child of Pause) ------------------------------
            Add("options", () =>
            {
                if (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SettingsPanel>()) { Drive.Log(Drive.Station("D12-Options")); return true; }
                if (Elapsed(0.5f) && Game.UI != null) Game.UI.Open<Diablo2.UI.SettingsPanel>();
                if (Elapsed(8f)) return true;
                return false;
            });
            Add("options-shot", () =>
            {
                if (!ShotDone()) return false;
                if (!Elapsed(0.4f)) return false;
                Shot("t0_d12_options.png", "screen");
                return true;
            });
            Add("options-close", () => { if (Game.UI != null) Game.UI.Close<Diablo2.UI.SettingsPanel>(); return true; });

            // ---- 14. Pause -> Stage (timeScale reset) ---------------------------
            Add("resume", () =>
            {
                if (FsmEq("Stage")) { Drive.Log(Drive.Station("D12-Stage-resume") + " TRANS=TriggerResume->StateStage"); return true; }
                if (Elapsed(0.6f)) Game.Event.Emit(Diablo2.Core.Events.ResumeRequest);
                if (Elapsed(15f)) { Drive.Warn("resume timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- 15. Stage -> MainMenu (leave cleanup) --------------------------
            Add("tomain", () =>
            {
                if (FsmEq("MainMenu")) { Drive.Log(Drive.Station("D12-MainMenu-back") + " TRANS=TriggerToMain->StateMainMenu"); return true; }
                if (Elapsed(0.6f)) Game.Event.Emit(Diablo2.Core.Events.ToMainMenuRequest);
                if (Elapsed(20f)) { Drive.Warn("tomain timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("tomain-settle", () =>
            {
                if (!Elapsed(1.2f)) return false;
                var pl = Drive.Player();
                Drive.KV("D12-LEAVE-CLEANUP", "fsm=" + Drive.Fsm() + " playerNull=" + (pl == null ? 1 : 0)
                    + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                    + " timers=" + Drive.TimerCount() + " panels=" + Drive.Panels()
                    + " timeScale=" + Time.timeScale.ToString("0.##"));
                return true;
            });

            // ---- 16. S3 write via the real panel path ---------------------------
            Add("s3-open", () =>
            {
                if (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SettingsPanel>()) return true;
                if (Elapsed(0.4f) && Game.UI != null) Game.UI.Open<Diablo2.UI.SettingsPanel>();
                if (Elapsed(6f)) return true;
                return false;
            });
            Add("s3-write", () =>
            {
                Drive.Log(Drive.SettingsLine("before-panel-write"));
                var panel = Game.UI != null ? Game.UI.Get<Diablo2.UI.SettingsPanel>() : null;
                var r1 = InvokeWith(panel, "OnBgmDelta", new object[] { 0.1f });
                var r2 = InvokeWith(panel, "OnBgmDelta", new object[] { 0.1f });
                var r3 = InvokeWith(panel, "OnSfxDelta", new object[] { -0.1f });
                var r4 = InvokeWith(panel, "OnCycleQuality", new object[0]);
                Drive.KV("S3-PANEL-WRITE", "panel=" + (panel != null ? "ok" : "null")
                    + " OnBgmDelta=" + r1 + "," + r2 + " OnSfxDelta=" + r3 + " OnCycleQuality=" + r4);
                Drive.Log(Drive.SettingsLine("after-panel-write"));
                return true;
            });
            Add("s3-mute", () =>
            {
                // The mute keys have no read/write point: drive the real SetMute path, then show the
                // persisted settings.json carries NO audio/bgm_mute / audio/sfx_mute key at all.
                var au = Drive.Audio();
                if (au != null) au.SetMute(true, true);
                Drive.KV("S3-MUTE", "api=IAudioModule.SetMute(true,true) persistedKeyPresent=" + PersistedHas("audio/bgm_mute")
                    + "," + PersistedHas("audio/sfx_mute"));
                Drive.Log(Drive.SettingsLine("after-setmute"));
                Drive.Log("SETTINGS-FILE where=after-setmute raw=" + Drive.Esc(ReadSettingsFile()));
                return true;
            });
            Add("s3-fullscreen", () =>
            {
                var panel = Game.UI != null ? Game.UI.Get<Diablo2.UI.SettingsPanel>() : null;
                var r1 = InvokeWith(panel, "OnToggleFullscreen", new object[0]);
                var r2 = InvokeWith(panel, "OnToggleFullscreen", new object[0]);
                Drive.KV("S3-FULLSCREEN", "invoke=" + r1 + "," + r2 + " note=twice so the stored value ends non-default(true)");
                Drive.Log(Drive.SettingsLine("after-fullscreen"));
                if (Game.UI != null) Game.UI.Close<Diablo2.UI.SettingsPanel>();
                return true;
            });

            // ---- 17. finish ------------------------------------------------------
            Add("finish", () =>
            {
                Finish("plan-end");
                return true;
            });
            Add("hold", () => true);
        }

        private bool _loadingLogged;

        private void EnterStage()
        {
            var save = Drive.Save();
            if (save != null)
            {
                var list = save.List();
                if (list != null && list.Count > 0) _saveName = list[0];
            }
            if (string.IsNullOrEmpty(_saveName)) { Drive.Warn("EnterStage: no save found; using X162012"); _saveName = "X162012"; }
            Drive.KV("ENTER-STAGE", "save=\"" + _saveName + "\" note=Events.CharSelectRequest -> AppFlow.OnCharSelectRequest -> GoStage -> TriggerEnterStage->StateLoading");
            Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _saveName);
        }

        /// <summary>Wrap MapView.ShowArea (the RebuildLayers pave) in a Stopwatch.</summary>
        private void Pave(string tag)
        {
            var mv = MapViewObj();
            if (mv == null) { Drive.Warn("pave: no MapView tag=" + tag); _lastPaveUs = -1f; return; }
            var m = Drive.Map();
            var area = m != null ? m.Area : AreaId.Town;
            var before = BuiltChunks(mv);
            var sw = Stopwatch.StartNew();
            ((Diablo2.Module.Map.MapView)mv).ShowArea(area);
            sw.Stop();
            _lastPaveUs = (float)(sw.Elapsed.TotalMilliseconds * 1000.0);
            _lastPaveTag = tag;
            _paveUs[tag] = _lastPaveUs;
            Drive.KV("SPIKE-SHOWAREA", "tag=" + tag + " area=" + area
                + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                + " chunked=" + Drive.BoolField(mv, "_chunked")
                + " chunksBefore=" + before + " chunksAfter=" + BuiltChunks(mv)
                + " wall_us=" + _lastPaveUs.ToString("0.0") + " wall_ms=" + (_lastPaveUs / 1000f).ToString("0.000"));
        }

        private void CountPave(string tag)
        {
            var go = MapViewGo();
            var nodes = go != null ? Drive.NodeCount(go.transform) : -1;
            float us; _paveUs.TryGetValue(tag, out us);
            var usGo = nodes > 0 ? us / nodes : -1f;
            Drive.KV("SPIKE-SHOWAREA-COST", "tag=" + tag + " nodes=" + nodes
                + " wall_us=" + us.ToString("0.0")
                + " budget_us_1frame=" + (1000000.0 / 60.0).ToString("0.0")
                + " us_per_GO=" + usGo.ToString("0.000")
                + " threshold_us_per_GO_for_1frame=" + (nodes > 0 ? ((1000000.0 / 60.0) / nodes).ToString("0.000") : "-"));
        }

        private int _chunkTotalNodes() { var go = MapViewGo(); return go != null ? Drive.NodeCount(go.transform) : -1; }

        private bool WaitFrames(int n) { if (_stFrame < 0) _stFrame = Time.frameCount; return Time.frameCount - _stFrame >= n; }

        private static string InvokeWith(object o, string method, object[] args)
        {
            if (o == null) return "(no-obj)";
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return "(no-method)";
            try { var r = m.Invoke(o, args); return r != null ? r.ToString() : "ok"; }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        /// <summary>Read the persisted settings.json (proves which keys were actually written).</summary>
        private static string ReadSettingsFile()
        {
            try
            {
                var p = Path.Combine(Application.dataPath, "..", "setting", "settings.json");
                return File.Exists(p) ? File.ReadAllText(p) : "(missing:" + p + ")";
            }
            catch (Exception e) { return "(err:" + e.GetType().Name + ")"; }
        }

        /// <summary>Is the key literally present in the persisted settings.json?</summary>
        private static int PersistedHas(string key)
        {
            var t = ReadSettingsFile();
            return t.IndexOf("\"" + key + "\"", StringComparison.Ordinal) >= 0 ? 1 : 0;
        }

        private static int StaticInt(string typeName, string field)
        {
            var t = Drive.FindType(typeName);
            if (t == null) return -1;
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return -1;
            var v = f.GetValue(null);
            return v is int ? (int)v : -1;
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Drive.KV("FINISH", "why=" + why + " pc=" + _pc + " shots=" + _shotSeq
                + " dmgEvents=" + _dmg + " kills=" + _kills
                + " fsm=" + Drive.Fsm() + " device=\"" + SafeDev() + "\"");
            Drive.Log(Drive.SettingsLine("session1-end"));
            Drive.Log("SETTINGS-FILE where=session1-end raw=" + Drive.Esc(ReadSettingsFile()));
            Drive.WriteFile(Drive.DonePath, "T0-DONE why=" + why + " shots=" + _shotSeq
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
