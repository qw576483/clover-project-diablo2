// t0b_drive.cs -- one Play session for the t0-play-d11-s2-d5 slice.
//
// WHY ONE CHAIN (SKILL 2 / unity-cli: "a batch action, not a per-item loop"):
//   the S2 perf rows (frame pacing / enter-stage load / footprint / render device + the
//   post-T0FIX MapView single-frame spike) AND the D11 input rows (23 aliases x 3 contexts:
//   MainMenu / DialogOpen / ShopOpen) all exist only inside ONE live session; splitting them
//   would re-run the same boot/load chain twice.
//
// WHAT IT MEASURES
//   S2:
//     [T0B] DEVICE            render device + resolution + vSync + targetFrameRate
//     [T0B] PACING-STEADY     n=180 frame times (Time.unscaledDeltaTime) + FrameTimingManager
//                             cpuMainThreadFrameTime p50/p95/max
//     [T0B] LOAD              EnterStage -> Stage-ready wall-clock delta
//     [T0B] MEM               Profiler.GetTotalAllocatedMemoryLong / MonoUsedSize
//     [T0B] SPIKE-SHOWAREA    Stopwatch around MapView.ShowArea (the RebuildLayers pave), N reps,
//                             min/p50/p99/max us + us/GO + the 1-frame per-GO budget
//     [T0B] SPIKE-FRAME       per-frame window stats: max single-frame ms + max chunks built in
//                             ONE frame + total chunks built (the REAL app-driven first build)
//     [T0B] SPIKE-INCR        the incremental path: camera walks 1 cell/frame for 150 frames;
//                             max chunks built in one frame must be <= MapView.MaxChunksPerFrame
//   D11 (one line per key x context):
//     [T0B] D11 ctx=<menu|dialog|shop> entity=<matrix entity cell> key=<InputSystem Key>
//              eff=<0|1> swallowed=<0|1> move=<0|1> p0=.. p1=.. f0=.. f1=.. g0=.. g1=..
//              dm=<MoveCommand delta> r0=.. r1=.. db=<belt delta> ds=<swap delta>
//     eff = ANY observable state change in the key window (panel set / fsm / player grid /
//     HUD run-toggle / MoveCommand / UseBeltRequest / SwapWeaponRequest).
//     move = a MoveCommand was emitted OR the player grid changed.
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace T0B
{
    public static class Drive
    {
        internal const string Tag = "T0B";

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;

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

        // ---- reflection ---------------------------------------------------------
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
        internal static Diablo2.Module.IViewModule View() { return CtxMember("View") as Diablo2.Module.IViewModule; }
        internal static Diablo2.Module.ICameraRig Rig() { return CtxMember("Camera") as Diablo2.Module.ICameraRig; }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }
        internal static bool BoolField(object o, string n) { var v = Field(o, n); return v is bool && (bool)v; }
        internal static int IntField(object o, string n)
        {
            var v = Field(o, n);
            if (v is int) return (int)v;
            if (v is bool) return ((bool)v) ? 1 : 0;
            return -1;
        }
        internal static bool SetField(object o, string n, object v)
        {
            if (o == null) return false;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(o, v); return true;
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

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)"; var vram = -1;
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            try { vram = SystemInfo.graphicsMemorySize; } catch { }
            return "device=\"" + dev + "\" type=" + typ + " vramMB=" + vram
                + " res=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate
                + " qualityLevel=" + QualitySettings.GetQualityLevel()
                + " fullscreen=" + (Screen.fullScreen ? 1 : 0);
        }

        /// <summary>Compact open-panel set (the D11 observable + D12-style station line).</summary>
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
            AddIf(sb, Game.UI.IsOpen<Diablo2.UI.CharacterPanel>(), "Character");
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

        internal static string Grid()
        {
            var p = Player();
            if (p == null) return "(-)";
            var g = p.Grid;
            return "(" + g.x + "," + g.y + ")";
        }

        internal static string Station(string tag)
        {
            var m = Map();
            return "STATION " + tag + " fsm=" + Fsm() + " scene=" + SceneName()
                + " area=" + (m != null ? m.Area.ToString() : "-")
                + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                + " panels=" + Panels()
                + " grid=" + Grid()
                + " timeScale=" + Time.timeScale.ToString("0.##")
                + " timers=" + TimerCount()
                + " t=" + Time.time.ToString("0.00");
        }

        internal static object MapViewObj()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? (object)arr[0] : null;
        }
        internal static GameObject MapViewGo()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? arr[0].gameObject : null;
        }
        internal static int BuiltChunks(object mv)
        {
            if (mv == null) return -1;
            var p = mv.GetType().GetProperty("BuiltChunkCount", BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -1;
            var v = p.GetValue(mv);
            return v is int ? (int)v : -1;
        }
        internal static int IntProp(object o, string name)
        {
            if (o == null) return -1;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -1;
            var v = p.GetValue(o);
            return v is int ? (int)v : -1;
        }
        internal static int StaticInt(string typeName, string field)
        {
            var t = FindType(typeName);
            if (t == null) return -1;
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return -1;
            var v = f.GetValue(null);
            return v is int ? (int)v : -1;
        }
        internal static string InvokePrivate(object o, string method)
        {
            if (o == null) return "(no-obj)";
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return "(no-method)";
            try { m.Invoke(o, null); return "ok"; } catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        /// <summary>Invoke a method under a Stopwatch; returns elapsed microseconds (-1 on failure).</summary>
        internal static float InvokeUs(object o, string method, object[] args)
        {
            if (o == null) return -1f;
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return -1f;
            var sw = Stopwatch.StartNew();
            try { m.Invoke(o, args); } catch (Exception e) { Warn("invoke " + method + " EX " + e.GetType().Name); }
            sw.Stop();
            return (float)(sw.Elapsed.TotalMilliseconds * 1000.0);
        }
    }

    /// <summary>Public one-shot entries for the run script.</summary>
    public static class Api2
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = UnityEngine.InputSystem.InputSystem.settings;
            st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            if (Keyboard.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) UnityEngine.InputSystem.InputSystem.AddDevice<Mouse>();

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + Drive.Fsm()
                       + " modules=" + ModulesLine();
            Drive.Log(line);
            Drive.Log("DEVICE " + Drive.DeviceLine());
            return line;
        }

        internal static string ModulesLine()
        {
            return "Map=" + (Drive.Map() != null ? 1 : 0) + " Player=" + (Drive.Player() != null ? 1 : 0)
                + " Monster=" + (Drive.Monster() != null ? 1 : 0)
                + " View=" + (Drive.View() != null ? 1 : 0) + " Camera=" + (Drive.Rig() != null ? 1 : 0);
        }

        public static string Mem()
        {
            long total = -1; long mono = -1; int gfx = -1; int sys = -1;
            try { total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong(); } catch { }
            try { mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(); } catch { }
            try { gfx = SystemInfo.graphicsMemorySize; } catch { }
            try { sys = SystemInfo.systemMemorySize; } catch { }
            var line = "MEM profilerTotalMB=" + (total / 1048576.0).ToString("0.0")
                + " monoUsedMB=" + (mono / 1048576.0).ToString("0.0")
                + " graphicsMemoryMB=" + gfx + " systemMemoryMB=" + sys
                + " fsm=" + Drive.Fsm();
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
            var go = new GameObject("T0BEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<T0BDriver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class T0BDriver : MonoBehaviour
    {
        private enum Ctx { Menu, Dialog, Shop }

        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;

        // ---- pacing / frame windows ------------------------------------------------
        private readonly List<float> _head = new List<float>();
        private bool _headLogged;
        private readonly List<float> _steady = new List<float>();
        private float _steadyAt = -1f; private bool _steadyLogged;
        private readonly List<float> _cpuMain = new List<float>();

        private readonly List<float> _winMs = new List<float>();
        private readonly List<int> _winBuilt = new List<int>();
        private int _lastBuilt;
        private int _maxBuiltOneFrame;
        private readonly List<float> _allMs = new List<float>();
        private readonly List<float> _allCpu = new List<float>();

        // ---- S2 --------------------------------------------------------------------
        private float _loadT0; private string _loadWall0; private float _loadT1; private string _loadWall1;
        private readonly Dictionary<string, float> _paveUs = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _paveNodes = new Dictionary<string, int>();

        // ---- D11 -------------------------------------------------------------------
        private bool _phaseActive;
        private Ctx _ctx;
        private int _keyIdx; private int _keyStage; private float _keyAt;
        private int _mw0, _bw0, _sw0;
        private string _p0 = "", _p1 = "", _f0 = "", _f1 = "", _g0 = "", _g1 = "";
        private int _r0, _r1;
        private int _ctxDone;

        // observable counters
        private int _move, _belt, _swap;
        internal static int MoveCount, BeltCount, SwapCount;
        internal static string S2Pacing = "", S2Load = "", S2Mem = "";

        private readonly string[] KeyAlias = {
            "KeyBelt1","KeyBelt2","KeyBelt3","KeyBelt4",
            "KeyCharSheet","KeyClosePanel","KeyInventory","KeyMinimap",
            "KeyPause","KeyQuestLog","KeyRunToggle","KeyShowGroundItems",
            "KeySkillSlot1","KeySkillSlot2","KeySkillSlot3","KeySkillSlot4",
            "KeySkillSlot5","KeySkillSlot6","KeySkillSlot7","KeySkillSlot8",
            "KeySkillTree","KeyStandStill","KeySwapWeapon"
        };
        private readonly string[] KeyLabel = {
            "Num1","Num2","Num3","Num4","C","Escape","I","Tab",
            "Escape","Q","R","LeftAlt",
            "F1","F2","F3","F4","F5","F6","F7","F8",
            "T","LeftShift","W"
        };
        private readonly Key[] KeyCodes = {
            Key.Digit1,Key.Digit2,Key.Digit3,Key.Digit4,
            Key.C,Key.Escape,Key.I,Key.Tab,
            Key.Escape,Key.Q,Key.R,Key.LeftAlt,
            Key.F1,Key.F2,Key.F3,Key.F4,Key.F5,Key.F6,Key.F7,Key.F8,
            Key.T,Key.LeftShift,Key.W
        };

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
            Game.Event.On<Vector2Int>(Events.MoveCommand, OnMove);
            Game.Event.On<int>(Events.UseBeltRequest, OnBelt);
            Game.Event.On(Events.SwapWeaponRequest, OnSwap);
        }
        private void OnMove(Vector2Int g) { _move++; MoveCount = _move; }
        private void OnBelt(int i) { _belt++; BeltCount = _belt; }
        private void OnSwap() { _swap++; SwapCount = _swap; }

        // ---- per-frame collection --------------------------------------------------
        private void Update()
        {
            CollectFrame();
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
                Drive.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        private void CollectFrame()
        {
            var ms = Time.unscaledDeltaTime * 1000f;
            _allMs.Add(ms);
            try
            {
                var ft = new FrameTiming[1];
                FrameTimingManager.CaptureFrameTimings();
                var got = FrameTimingManager.GetLatestTimings(1, ft);
                if (got > 0) { _cpuMain.Add((float)ft[0].cpuMainThreadFrameTime); _allCpu.Add((float)ft[0].cpuMainThreadFrameTime); }
            }
            catch { }

            var mv = Drive.MapViewObj();
            var b = Drive.BuiltChunks(mv);
            var db = (b >= 0 && _lastBuilt >= 0) ? (b - _lastBuilt) : 0;
            _lastBuilt = b;
            var built = db > 0 ? db : 0;
            if (built > _maxBuiltOneFrame) _maxBuiltOneFrame = built;
            _winMs.Add(ms); _winBuilt.Add(built);

            if (!_headLogged)
            {
                _head.Add(Time.unscaledDeltaTime);
                if (_head.Count >= 60)
                {
                    _headLogged = true;
                    Drive.KV("PACING-HEAD60", Stats(_head) + " " + Drive.DeviceLine());
                }
            }
            if (_steadyAt >= 0f && !_steadyLogged)
            {
                _steady.Add(Time.unscaledDeltaTime);
                if (_steady.Count >= 180)
                {
                    _steadyLogged = true;
                    var s = Stats(_steady);
                    S2Pacing = s;
                    Drive.KV("PACING-STEADY", s + " where=stage-fixedcam "
                        + "cpuMain n=" + _cpuMain.Count + " p50=" + Pct(_cpuMain, 0.50f) + " p95=" + Pct(_cpuMain, 0.95f)
                        + " max=" + MaxOf(_cpuMain) + " ms; " + Drive.DeviceLine());
                }
            }
        }

        private static string Stats(List<float> v)
        {
            var a = new List<float>(v); a.Sort();
            var sum = 0f; foreach (var x in a) sum += x;
            Func<float, float> ms = s => s * 1000f;
            return "n=" + a.Count
                + " min=" + ms(a[0]).ToString("0.00")
                + " p50=" + ms(a[(int)(0.50f * (a.Count - 1))]).ToString("0.00")
                + " p95=" + ms(a[(int)(0.95f * (a.Count - 1))]).ToString("0.00")
                + " max=" + ms(a[a.Count - 1]).ToString("0.00")
                + " mean=" + ms(sum / a.Count).ToString("0.00")
                + " (ms/frame)";
        }
        private static float Pct(List<float> v, float p)
        {
            if (v.Count == 0) return -1f;
            var a = new List<float>(v); a.Sort();
            return a[(int)(p * (a.Count - 1))];
        }
        private static float MaxOf(List<float> v)
        {
            var m = -1f;
            for (var i = 0; i < v.Count; i++) if (v[i] > m) m = v[i];
            return m;
        }

        // ---- window stats ----------------------------------------------------------
        private void BeginWindow() { _winMs.Clear(); _winBuilt.Clear(); _maxBuiltOneFrame = 0; }

        private void EndWindow(string tag)
        {
            var ms = new List<float>(_winMs);
            ms.Sort();
            var sumBuilt = 0;
            for (var i = 0; i < _winBuilt.Count; i++) sumBuilt += _winBuilt[i];
            var pmax = ms.Count > 0 ? ms[ms.Count - 1] : -1f;
            var p50 = ms.Count > 0 ? ms[(int)(0.50f * (ms.Count - 1))] : -1f;
            var p99 = ms.Count > 0 ? ms[(int)(0.99f * (ms.Count - 1))] : -1f;
            Drive.KV("SPIKE-FRAME", "tag=" + tag + " frames=" + ms.Count
                + " p50_ms=" + p50.ToString("0.00") + " p99_ms=" + p99.ToString("0.00") + " max_ms=" + pmax.ToString("0.00")
                + " maxBuiltOneFrame=" + _maxBuiltOneFrame + " totalBuilt=" + sumBuilt
                + " maxChunksPerFrameConst=" + Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxChunksPerFrame")
                + " builtChunksNow=" + Drive.BuiltChunks(Drive.MapViewObj()));
        }

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
        private static bool FsmEq(string s) { return Drive.Fsm() == s; }
        private bool WaitFrames(int n) { if (_stFrame < 0) _stFrame = Time.frameCount; return Time.frameCount - _stFrame >= n; }
        private int _stFrame = -1;

        // ---- S2: the MapView ShowArea pave, N reps ---------------------------------
        private void PaveReps(string tag, string area, int reps)
        {
            var mv = Drive.MapViewObj();
            if (mv == null) { Drive.Warn("pave: no MapView tag=" + tag); return; }
            var us = new List<float>();
            for (var i = 0; i < reps; i++)
            {
                var sw = Stopwatch.StartNew();
                try { ((Diablo2.Module.Map.MapView)mv).ShowArea(Drive.Map() != null ? Drive.Map().Area : AreaId.Town); }
                catch (Exception e) { Drive.Warn("pave EX " + e.GetType().Name); }
                sw.Stop();
                us.Add((float)(sw.Elapsed.TotalMilliseconds * 1000.0));
            }
            var go = Drive.MapViewGo();
            var nodes = go != null ? Drive.NodeCount(go.transform) : -1;
            var sorted = new List<float>(us); sorted.Sort();
            var min = sorted[0];
            var p50 = sorted[(int)(0.50f * (sorted.Count - 1))];
            var p99 = sorted[(int)(0.99f * (sorted.Count - 1))];
            var max = sorted[sorted.Count - 1];
            var budget = 1000000.0 / 60.0;
            _paveUs[tag] = max; _paveNodes[tag] = nodes;
            Drive.KV("SPIKE-SHOWAREA", "tag=" + tag + " area=" + area + " reps=" + reps + " nodes=" + nodes
                + " min_us=" + min.ToString("0.0") + " p50_us=" + p50.ToString("0.0")
                + " p99_us=" + p99.ToString("0.0") + " max_us=" + max.ToString("0.0")
                + " max_ms=" + (max / 1000f).ToString("0.000")
                + " max_us_per_GO=" + (nodes > 0 ? (max / nodes).ToString("0.000") : "-")
                + " oneFrameBudget_us=" + budget.ToString("0.0")
                + " threshold_us_per_GO=" + (nodes > 0 ? (budget / nodes).ToString("0.000") : "-")
                + " chunked=" + Drive.BoolField(mv, "_chunked"));
        }

        // ---- D11: key injection ----------------------------------------------------
        private static Keyboard KB()
        {
            var kb = Keyboard.current;
            if (kb == null) kb = UnityEngine.InputSystem.InputSystem.AddDevice<Keyboard>();
            return kb;
        }
        private void KeyDown(int i)
        {
            var kb = KB(); if (kb == null) { Drive.Warn("D11 no-keyboard"); return; }
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new KeyboardState(KeyCodes[i]));
        }
        private void KeyUp()
        {
            var kb = KB(); if (kb == null) return;
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new KeyboardState());
        }

        private static int HudRunning()
        {
            if (Game.UI == null) return -1;
            var hud = Game.UI.Get<Diablo2.UI.HudPanel>();
            if (hud == null) return -1;
            var v = Drive.Field(hud, "_running");
            return v is bool ? (((bool)v) ? 1 : 0) : -1;
        }
        private static void CloseExtra()
        {
            if (Game.UI == null) return;
            Game.UI.Close<Diablo2.UI.InventoryPanel>();
            Game.UI.Close<Diablo2.UI.CharacterPanel>();
            Game.UI.Close<Diablo2.UI.SkillTreePanel>();
            Game.UI.Close<Diablo2.UI.QuestLogPanel>();
            Game.UI.Close<Diablo2.UI.MiniMapPanel>();
            Game.UI.Close<Diablo2.UI.SettingsPanel>();
        }
        private void EnsureCtxPanel()
        {
            if (Game.UI == null) return;
            if (_ctx == Ctx.Dialog) { if (!Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>()) Game.UI.Open<Diablo2.UI.NpcDialogPanel>(); }
            else if (_ctx == Ctx.Shop) { if (!Game.UI.IsOpen<Diablo2.UI.ShopPanel>()) Game.UI.Open<Diablo2.UI.ShopPanel>(); }
        }
        private bool CtxReady()
        {
            if (Game.UI == null) return false;
            if (_ctx == Ctx.Dialog) return Drive.Fsm() == "Stage" && Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>();
            if (_ctx == Ctx.Shop) return Drive.Fsm() == "Stage" && Game.UI.IsOpen<Diablo2.UI.ShopPanel>();
            return Drive.Fsm() == "MainMenu" && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>();
        }
        private void CloseCtxPanels()
        {
            if (Game.UI == null) return;
            Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
            Game.UI.Close<Diablo2.UI.ShopPanel>();
        }
        private string CtxCode() { return _ctx == Ctx.Menu ? "menu" : (_ctx == Ctx.Dialog ? "dialog" : "shop"); }

        private bool RunPhase(Ctx ctx)
        {
            if (!_phaseActive)
            {
                _phaseActive = true; _ctx = ctx; _keyIdx = 0; _keyStage = 0; _phaseAt = Time.unscaledTime;
                if (ctx != Ctx.Menu) { CloseExtra(); CloseCtxPanels(); EnsureCtxPanel(); }
                Drive.Log("D11-PHASE-BEGIN ctx=" + CtxCode() + " fsm=" + Drive.Fsm() + " panels=" + Drive.Panels());
                return false;
            }
            if (_keyIdx >= KeyAlias.Length)
            {
                _phaseActive = false;
                CloseExtra();
                Drive.Log("D11-PHASE-END ctx=" + CtxCode() + " keys=" + KeyAlias.Length + " panels=" + Drive.Panels());
                return true;
            }
            var now = Time.unscaledTime;
            if (_keyStage == 0)
            {
                CloseExtra(); EnsureCtxPanel();
                if (!CtxReady())
                {
                    if (now - _phaseAt > 30f)
                    {
                        Drive.Warn("D11 ctx " + CtxCode() + " never became ready; aborting phase (no rows recorded for this ctx)");
                        _phaseActive = false; return true;
                    }
                    return false;
                }
                _p0 = Drive.Panels(); _f0 = Drive.Fsm(); _g0 = Drive.Grid();
                _r0 = HudRunning(); _mw0 = _move; _bw0 = _belt; _sw0 = _swap;
                KeyDown(_keyIdx);
                _keyAt = now; _keyStage = 1;
                return false;
            }
            if (_keyStage == 1 && now - _keyAt > 0.25f) { KeyUp(); _keyAt = now; _keyStage = 2; return false; }
            if (_keyStage == 2 && now - _keyAt > 0.25f)
            {
                _p1 = Drive.Panels(); _f1 = Drive.Fsm(); _g1 = Drive.Grid();
                _r1 = HudRunning();
                var dm = _move - _mw0; var db = _belt - _bw0; var ds = _swap - _sw0;
                var moved = dm > 0 || _g0 != _g1;
                var eff = (_p0 != _p1) || (_f0 != _f1) || (_g0 != _g1)
                          || (_r0 != _r1) || dm > 0 || db > 0 || ds > 0;
                Drive.KV("D11", "ctx=" + CtxCode() + " entity=key:" + KeyAlias[_keyIdx] + "(" + KeyLabel[_keyIdx] + ")"
                    + " key=" + KeyCodes[_keyIdx]
                    + " eff=" + (eff ? 1 : 0) + " swallowed=" + (eff ? 0 : 1) + " move=" + (moved ? 1 : 0)
                    + " p0=" + _p0 + " p1=" + _p1 + " f0=" + _f0 + " f1=" + _f1
                    + " g0=" + _g0 + " g1=" + _g1 + " dm=" + dm
                    + " r0=" + _r0 + " r1=" + _r1 + " db=" + db + " ds=" + ds);
                _keyAt = now; _keyStage = 3;
                return false;
            }
            if (_keyStage == 3)
            {
                // restore: back to Stage + ctx panel open, then advance
                if (Drive.Fsm() == "Pause") { Game.Event.Emit(Events.ResumeRequest); _keyAt = now; return false; }
                CloseExtra(); EnsureCtxPanel();
                if (CtxReady() || now - _keyAt > 3f) { _keyIdx++; _keyStage = 0; }
                return false;
            }
            return false;
        }

        // ============================ the plan =====================================
        private void BuildPlan()
        {
            Add("head60", () => _headLogged);

            Add("boot", () =>
            {
                if (FsmEq("MainMenu")) { Drive.Log(Drive.Station("D12-MainMenu") + " via=TriggerBootDone"); return true; }
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(25f)) { Drive.Warn("boot timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            // ---- D11 context 1: MainMenu ----
            Add("d11-menu", () => RunPhase(Ctx.Menu));

            Add("charselect", () =>
            {
                if (FsmEq("CharSelect")) { Drive.Log(Drive.Station("D12-CharSelect")); return true; }
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(20f)) { Drive.Warn("charselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("charcreate", () =>
            {
                if (FsmEq("CharCreate")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
                if (Elapsed(20f)) { Drive.Warn("charcreate timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("backselect", () =>
            {
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                if (Elapsed(20f)) { Drive.Warn("backselect timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("loading", () =>
            {
                if (FsmEq("Stage")) return true;
                if (FsmEq("Loading"))
                {
                    if (_loadT0 <= 0f) { _loadT0 = Time.realtimeSinceStartup; _loadWall0 = DateTime.Now.ToString("HH:mm:ss.fff"); }
                    return false;
                }
                if (Elapsed(1.0f)) EnterStage();
                if (Elapsed(60f)) { Drive.Warn("loading timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            Add("stage-enter", () =>
            {
                if (!FsmEq("Stage") || !(Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>()))
                {
                    if (Elapsed(30f)) { Drive.Warn("stage wait timeout fsm=" + Drive.Fsm()); return true; }
                    return false;
                }
                if (!Elapsed(2.0f)) return false;
                _loadT1 = Time.realtimeSinceStartup; _loadWall1 = DateTime.Now.ToString("HH:mm:ss.fff");
                var secs = _loadT1 - _loadT0;
                S2Load = "EnterStage " + _loadWall0 + " -> Stage ready " + _loadWall1
                    + " = " + secs.ToString("0.00") + "s";
                Drive.KV("LOAD", "enter=" + _loadWall0 + " ready=" + _loadWall1
                    + " seconds=" + secs.ToString("0.00") + " watchdog=" + WatchdogSeconds() + " fsm=" + Drive.Fsm());
                Drive.Log(Drive.Station("D12-Stage") + " TRANS=TriggerStageReady->StateStage");
                Api2.Mem();
                S2Mem = "profilerTotalMB=" + (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0).ToString("0.0")
                    + " monoUsedMB=" + (UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0).ToString("0.0")
                    + " graphicsMemoryMB=" + SystemInfo.graphicsMemorySize + " systemMemoryMB=" + SystemInfo.systemMemorySize;
                return true;
            });

            // ---- S2: ShowArea pave reps (town, non-chunked) ----
            Add("pave-town", () => { PaveReps("town-rebuild", "Town", 6); return true; });
            Add("pave-town-cost", () =>
            {
                if (!WaitFrames(2)) return false;
                var mv = Drive.MapViewObj();
                Drive.KV("SPIKE-SHOWAREA-COST", "tag=town-rebuild nodes=" + Drive.NodeCount(Drive.MapViewGo().transform)
                    + " poolNew=" + Drive.IntProp(mv, "PoolCreatedCount") + " poolReused=" + Drive.IntProp(mv, "PoolReusedCount")
                    + " poolFree=" + Drive.IntProp(mv, "PoolFreeCount")
                    + " chunked=" + Drive.BoolField(mv, "_chunked"));
                return true;
            });

            // ---- S2: steady-state pacing ----
            Add("steady", () =>
            {
                if (_steadyAt < 0f) { if (!Elapsed(0.5f)) return false; _steadyAt = 1f; _steady.Clear(); _steadyLogged = false; _cpuMain.Clear(); return false; }
                return _steadyLogged;
            });

            // ---- D11 sanity: the injection must actually reach the game ----
            // (press and release must span FRAMES, so this is a 3-stage mini state machine --
            //  the plan runs whole stations inside one frame, so a same-frame press+release is
            //  never seen by the engine's per-frame GetKeyDown.)
            Add("inject-sanity", () =>
            {
                if (_sanityStage == 0) { KeyDown(6); _keyAt = Time.unscaledTime; _sanityStage = 1; return false; }
                if (_sanityStage == 1)
                {
                    if (Time.unscaledTime - _keyAt <= 0.30f) return false;
                    KeyUp(); _keyAt = Time.unscaledTime; _sanityStage = 2;
                    return false;
                }
                if (Time.unscaledTime - _keyAt <= 0.30f) return false;
                var open = Game.UI != null && Game.UI.IsOpen<Diablo2.UI.InventoryPanel>();
                Drive.KV("INJECT-SANITY", "key=I inventoryOpenAfterPress=" + (open ? 1 : 0)
                    + " panels=" + Drive.Panels() + " (1 = the InputSystem injection reaches the game)");
                CloseExtra();
                _sanityStage = 0;
                return true;
            });

            // ---- D11 context 2: DialogOpen (Stage) ----
            Add("d11-dialog", () => RunPhase(Ctx.Dialog));

            // ---- D11 context 3: ShopOpen (Stage) ----
            Add("d11-shop", () => RunPhase(Ctx.Shop));

            // ---- moor: real app-driven first build + the incremental path ----
            Add("to-moor", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor) return true;
                if (_cur == "to-moor" && _at >= 0f)
                {
                    if (!_moorRequested) { _moorRequested = true; BeginWindow(); Drive.Log("MOOR-ENTER emit ExitEntered(BloodMoor)"); Game.Event.Emit<AreaId>(Events.ExitEntered, AreaId.BloodMoor); }
                }
                if (Elapsed(30f)) { Drive.Warn("to-moor timeout area=" + (m != null ? m.Area.ToString() : "-")); return true; }
                return false;
            });
            Add("moor-settle", () =>
            {
                if (!WaitFrames(45) && !Elapsed(3f)) return false;
                EndWindow("moor-first-build(app-driven)");
                Drive.Log(Drive.Station("D12-Stage-moor")
                    + " monsters=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                    + " chunked=" + Drive.BoolField(Drive.MapViewObj(), "_chunked"));
                Api2.Mem();
                return true;
            });
            Add("pave-moor", () => { PaveReps("moor-rebuild", "BloodMoor", 6); return true; });

            // ---- S2: the incremental path (T0FIX-A), measured DIRECTLY with a Stopwatch ----
            Add("incr-setup", () =>
            {
                if (!WaitFrames(3)) return false;
                var mv = Drive.MapViewObj();
                if (mv == null) { Drive.Warn("incr: no MapView"); return true; }
                _incrBase = Drive.Player() != null ? Drive.Player().Grid : new Vector2Int(0, 0);
                _incrN = 0; _incrUs.Clear(); _incrUsGo.Clear(); _incrRefreshUs.Clear(); _incrMaxDelta = 0; _incrCalls = 0;
                BeginWindow();
                Drive.KV("SPIKE-INCR-BEGIN", "area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " chunked=" + Drive.BoolField(mv, "_chunked")
                    + " base=" + _incrBase.x + "," + _incrBase.y
                    + " builtBefore=" + Drive.BuiltChunks(mv)
                    + " maxChunksPerFrameConst=" + Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxChunksPerFrame"));
                return true;
            });
            Add("incr-walk", () =>
            {
                var mv = Drive.MapViewObj();
                if (mv == null) return true;
                var rig = Drive.Rig();
                var viewCam = Drive.Field(mv, "ViewCamera") as Camera;
                if (viewCam == null) viewCam = Camera.main;
                var g = new Vector2Int(_incrBase.x + _incrN, _incrBase.y);
                if (rig != null) { rig.SetTargetGrid(g); rig.SnapToTarget(); }
                if (viewCam != null)
                {
                    var w0 = Iso.GridToWorld(new Vector2Int(0, 0));
                    var w1 = Iso.GridToWorld(new Vector2Int(1, 0));
                    var per = w1 - w0;
                    viewCam.transform.position = new Vector3(w0.x + per.x * g.x, w0.y + per.y * g.y, viewCam.transform.position.z);
                }
                if (_incrN == 0) Drive.SetField(mv, "_hasChunkRange", false);
                // (a) the registration step (RefreshVisibleChunks) -- must be tiny now
                _incrRefreshUs.Add(Drive.InvokeUs(mv, "RefreshVisibleChunks", null));
                // (b) the per-frame build budget: one PumpChunkBuild(1) call == one frame's worth of work
                for (var k = 0; k < 2; k++)
                {
                    var go = Drive.MapViewGo();
                    var n0 = go != null ? Drive.NodeCount(go.transform) : -1;
                    var b0 = Drive.BuiltChunks(mv);
                    var us = Drive.InvokeUs(mv, "PumpChunkBuild", new object[] { 1 });
                    var b1 = Drive.BuiltChunks(mv);
                    var n1 = go != null ? Drive.NodeCount(go.transform) : -1;
                    var dch = b1 - b0;
                    if (dch > _incrMaxDelta) _incrMaxDelta = dch;
                    if (us >= 0f) { _incrCalls++; _incrUs.Add(us); if (n0 > 0 && n1 > n0) _incrUsGo.Add(us / (n1 - n0)); }
                    if (dch <= 0) break;
                }
                _incrN++;
                if (_incrN < 150) return false;
                EndWindow("moor-incremental(1cell/frame)");
                var mv2 = Drive.MapViewObj();
                Drive.KV("SPIKE-INCR", "tag=moor-incremental steps=" + _incrN
                    + " refreshCalls=" + _incrRefreshUs.Count + " refresh_max_us=" + MaxOf(_incrRefreshUs).ToString("0.0")
                    + " buildCalls=" + _incrCalls
                    + " build_p50_us=" + Pct(_incrUs, 0.50f).ToString("0.0")
                    + " build_p99_us=" + Pct(_incrUs, 0.99f).ToString("0.0")
                    + " build_max_us=" + MaxOf(_incrUs).ToString("0.0")
                    + " build_max_us_per_GO=" + (MaxOf(_incrUsGo) >= 0f ? MaxOf(_incrUsGo).ToString("0.000") : "-")
                    + " maxDeltaChunksInOneCall=" + _incrMaxDelta
                    + " maxChunksPerFrameConst=" + Drive.StaticInt("Diablo2.Module.Map.MapView", "MaxChunksPerFrame")
                    + " builtAfter=" + Drive.BuiltChunks(mv2)
                    + " poolNew=" + Drive.IntProp(mv2, "PoolCreatedCount")
                    + " poolReused=" + Drive.IntProp(mv2, "PoolReusedCount"));
                return true;
            });

            // ---- wrap up ----
            Add("pause", () =>
            {
                if (FsmEq("Pause")) return true;
                if (Elapsed(0.6f)) Game.Event.Emit(Events.PauseRequest);
                if (Elapsed(15f)) { Drive.Warn("pause timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("resume", () =>
            {
                if (FsmEq("Stage")) return true;
                if (Elapsed(0.6f)) Game.Event.Emit(Events.ResumeRequest);
                if (Elapsed(15f)) { Drive.Warn("resume timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("tomain", () =>
            {
                if (FsmEq("MainMenu")) return true;
                if (Elapsed(0.6f)) Game.Event.Emit(Events.ToMainMenuRequest);
                if (Elapsed(20f)) { Drive.Warn("tomain timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });
            Add("finish", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private bool _moorRequested;
        private Vector2Int _incrBase;
        private int _incrN;
        private float _phaseAt;
        private int _sanityStage;
        private int _incrMaxDelta;
        private int _incrCalls;
        private readonly List<float> _incrUs = new List<float>();
        private readonly List<float> _incrUsGo = new List<float>();
        private readonly List<float> _incrRefreshUs = new List<float>();

        /// <summary>Find FlowConst.StageLoadTimeoutSeconds without pinning its namespace.</summary>
        private static float WatchdogSeconds()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] ts;
                try { ts = a.GetTypes(); } catch { continue; }
                foreach (var t in ts)
                {
                    if (t.Name != "FlowConst") continue;
                    var f = t.GetField("StageLoadTimeoutSeconds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (f == null) continue;
                    var v = f.GetValue(null);
                    if (v is float) return (float)v;
                    if (v is int) return (int)v;
                }
            }
            return -1f;
        }

        private void EnterStage()
        {
            var save = Drive.CtxMember("Save") as Diablo2.Module.ISaveModule;
            if (save != null)
            {
                var list = save.List();
                if (list != null && list.Count > 0) _saveName = list[0];
            }
            if (string.IsNullOrEmpty(_saveName)) { Drive.Warn("EnterStage: no save found; using X162012"); _saveName = "X162012"; }
            Drive.KV("ENTER-STAGE", "save=\"" + _saveName + "\"");
            Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Drive.KV("FINISH", "why=" + why + " pc=" + _pc
                + " moveCmds=" + _move + " belts=" + _belt + " swaps=" + _swap
                + " maxBuiltOneFrameGlobal=" + _maxBuiltOneFrame
                + " fsm=" + Drive.Fsm() + " " + Drive.DeviceLine());
            Drive.WriteFile(Drive.DonePath, "T0B-DONE why=" + why + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
