// =============================================================================
// d2u26_sortdrive.cs -- ONE Play session that freezes the U26/U36 draw-order trace.
//
//   U26 "character clipping" / U36 "when the character and an NPC overlap the game
//   flickers between showing the character and showing the NPC".
//
// WHY THIS FILE EXISTS
//   The bug ledger (the project's self-review bug list, U26 row 83 / U36 row 93) marks both rows
//   "partial" with the gap "V5 single frames cannot judge this; a MULTI-FRAME SEQUENCE
//   is needed".  A live Play session is the only place where the engine's real draw
//   order and the real pixels exist, so the sequence is captured here in ONE run.
//
// WHAT IS JUDGED (and by which script)
//   Nothing in this file decides "flicker / no flicker".  The driver only writes:
//     (a) one TSV row per frame with the two candidate keys of the overlapping pair --
//         sortingOrder (primary) and the z secondary key that ViewModule.EntityWorld
//         writes into the transform (the Unity transparent sort distance on an
//         orthographic camera == distance along the view direction == z);
//     (b) a cropped PNG per frame around the player/NPC overlap point (so the sheet
//         shows the actual pixels, and frame numbers are burned onto each grid);
//     (c) `sig` = FNV-1a over the crop's raw pixels, i.e. the frame fingerprint.
//   The offline judge (d2u26_sheet.py) then computes, per geometry relation:
//     * frames whose key tuple is fully equal  => the order is UNDEFINED (the necessary
//       precondition for the flicker), and
//     * signature alternations (sig[i]==sig[i-2] != sig[i-1]) => the pixels really
//       alternate that way.
//   Both numbers are recomputed from the frozen TSV, never narrated here.
//
// CHAIN (one session)
//   Env self-check -> Boot(Space) -> MainMenu("Single") -> CharSelect(save)
//   -> Stage + Hud.  The driver then requires AreaId.Town (the NPC views only exist
//   there), resolves Akara's own cell from IMapModule.NpcPoints, and parks the
//   character at three distances ALONG THE SAME ANTI-DIAGONAL as the NPC
//   (offset k*(1,-1), k = 2, 1, 0).  All three relations share gx+gy with the NPC, so
//   the primary key (sortingOrder) is EQUAL for every recorded frame -- that is
//   exactly the domain the fix has to decide with the secondary key.
//     k=2 -> "same world y, 2 cells apart"   k=1 -> "adjacent, same y"
//     k=0 -> "strictly the same cell"  (the player stands ON the NPC)
//   Each hold is >= 35 frames (Config.HoldFrames) because the ledger demands a
//   multi-frame sequence, not a single frame.
//
// ENVIRONMENT SELF-CHECK (skill 4.5): the render device / resolution / cadence are
// logged, and the run ABORTS (done marker says INVALID) on a software raster
// (Microsoft Basic Render Driver / WARP) -- frame/pixel verdicts there are void.
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;      // AreaId
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace D2U26
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Probe
    {
        internal const string Tag = "D2U26";

        private static string _done = string.Empty;
        private static readonly HashSet<string> Warned = new HashSet<string>();

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

        /// <summary>Warn once per key (unexpected branches must leave a trace, but must not flood).</summary>
        internal static void WarnOnce(string key, string msg)
        {
            if (Warned.Contains(key)) return;
            Warned.Add(key);
            Warn(msg);
        }

        internal static void KV(string key, string value) { Log(key + "=" + value); }

        internal static void Check(string name, bool ok, string detail)
        {
            Log("CHK name=" + name + " ok=" + (ok ? 1 : 0) + " " + detail);
        }

        // ---- files -------------------------------------------------------------------------
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

        // ---- reflection --------------------------------------------------------------------
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

        /// <summary>
        /// `ViewModule.DumpSortKeys()` (internal, added by the u26 slice): one line per entity
        /// node with kind|id|spriteCode|grid|sortingOrder|z.  This is the production view of the
        /// three sort keys, so the driver does not have to walk the scene graph itself.
        /// A missing method means the built assembly predates the u26 change -- logged, not fatal
        /// (the TSV columns then come from the player/NPC nodes read directly instead).
        /// </summary>
        private static MethodInfo _dumpSortKeys;
        private static bool _dumpProbeDone;

        internal static string SortKeys()
        {
            try
            {
                var view = CtxMember("View");
                if (view == null) { WarnOnce("sk.noview", "SORTKEYS: AppContext.View == null"); return "(no-view)"; }
                if (!_dumpProbeDone)
                {
                    _dumpProbeDone = true;
                    _dumpSortKeys = view.GetType().GetMethod("DumpSortKeys",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                    Log("SORTKEYS-PROBE method=" + (_dumpSortKeys != null ? "DumpSortKeys" : "(missing: stale build?)"));
                }
                if (_dumpSortKeys == null) return "(no-method)";
                var s = _dumpSortKeys.Invoke(view, null) as string;
                return s == null ? "(null)" : s.Replace("\r", " ").Replace("\n", "|");
            }
            catch (Exception e)
            {
                WarnOnce("sk.fail", "SORTKEYS " + e.GetType().Name + ": " + e.Message);
                return "(error)";
            }
        }

        /// <summary>ViewModule NPCS: (int)NpcId -> node. Used to read the NPC node's real keys.</summary>
        internal static object ViewMember(string name)
        {
            var view = CtxMember("View");
            if (view == null) return null;
            var f = view.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(view) : null;
        }

        internal static GameObject NodeOf(object view, string field)
        {
            if (view == null) return null;
            // NOTE: `ViewModule._player` / `_npcs` are PRIVATE fields -- a Public-only lookup returns
            // null and the whole run silently degrades to pOrder = int.MinValue / pz = NaN (measured
            // in the 11:56 run).  `EntityView.Root` is public, hence both flags on both lookups.
            var f = view.GetType().GetField(field,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var ev = f != null ? f.GetValue(view) : null;
            if (ev == null) return null;
            var rf = ev.GetType().GetField("Root", BindingFlags.Public | BindingFlags.Instance);
            return rf != null ? rf.GetValue(ev) as GameObject : null;
        }

        internal static GameObject PlayerNode() { return NodeOf(PlayerView(), "_player"); }

        internal static object PlayerView() { return CtxMember("View"); }

        /// <summary>The NPC node of a given NpcId from the private ViewModule._npcs dictionary.</summary>
        internal static GameObject NpcNode(int npcId)
        {
            try
            {
                var npcs = ViewMember("_npcs") as IEnumerable;
                if (npcs == null) return null;
                foreach (var kv in npcs)
                {
                    if (kv == null) continue;
                    var kt = kv.GetType();
                    var kf = kt.GetProperty("Key");
                    var vf = kt.GetProperty("Value");
                    if (kf == null || vf == null) continue;
                    if (!(kf.GetValue(kv) is int)) continue;
                    if ((int)kf.GetValue(kv) != npcId) continue;
                    var ev = vf.GetValue(kv);
                    if (ev == null) return null;
                    var rf = ev.GetType().GetField("Root", BindingFlags.Public | BindingFlags.Instance);
                    return rf != null ? rf.GetValue(ev) as GameObject : null;
                }
            }
            catch (Exception e) { WarnOnce("npcnode", "NPONODE " + e.GetType().Name + ": " + e.Message); }
            return null;
        }

        internal static SpriteRenderer Sr(GameObject go) { return go != null ? go.GetComponent<SpriteRenderer>() : null; }

        internal static string FirstSaveName()
        {
            try
            {
                var flow = CtxMember("Flow");
                if (flow == null) { Warn("ROSTER-PROBE AppContext.Flow == null"); return null; }
                var rf = flow.GetType().GetField("_roster", BindingFlags.NonPublic | BindingFlags.Instance);
                var roster = rf != null ? rf.GetValue(flow) : null;
                if (roster == null) { Warn("ROSTER-PROBE no AppFlow._roster"); return null; }
                var m = roster.GetType().GetMethod("ListAll", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) { Warn("ROSTER-PROBE no CharRoster.ListAll"); return null; }
                var items = m.Invoke(roster, null) as IEnumerable;
                var n = 0;
                string pick = null;
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        n++;
                        if (it == null) continue;
                        var nf = it.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
                        var name = nf != null ? nf.GetValue(it) as string : null;
                        if (pick == null && !string.IsNullOrEmpty(name)) pick = name;
                    }
                }
                Log("ROSTER-PROBE count=" + n + " pick=" + (pick ?? "(none)"));
                return pick;
            }
            catch (Exception e) { Warn("ROSTER-PROBE " + e.GetType().Name + ": " + e.Message); return null; }
        }

        internal static string DeviceName() { return SystemInfo.graphicsDeviceName; }

        internal static bool SoftwareRaster(string device)
        {
            var d = device ?? string.Empty;
            return d.IndexOf("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("SwiftShader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- setup / input -----------------------------------------------------------------
        internal static string Cfg()
        {
            Application.runInBackground = true;
            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var kb = Keyboard.current;
            if (kb == null) kb = InputSystem.AddDevice<Keyboard>();
            if (UnityEngine.InputSystem.Mouse.current == null) InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            Log("CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate
                + " focused=" + (Application.isFocused ? 1 : 0)
                + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)"));
            Log("DEVICE device=\"" + DeviceName() + "\" res=" + Screen.width + "x" + Screen.height
                + " unity=" + Application.unityVersion);
            return "CFG-OK";
        }

        internal static void KeyDown(string name)
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState(Key.Space));
            Log("KEYDOWN space");
        }

        internal static void KeyUp()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP");
        }

        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            if (m == null) return;
            var s = new MouseState { position = pos };
            if (leftDown) s = s.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, s);
        }

        internal static bool ScreenRect(RectTransform rt, out Vector2 center)
        {
            center = Vector2.zero;
            if (rt == null) return false;
            try
            {
                var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                center = new Vector2(c.x, c.y);
                return true;
            }
            catch (Exception e) { WarnOnce("rectfail", "SCREEN-RECT-FAIL " + e.GetType().Name + ": " + e.Message); return false; }
        }

        internal static Button FindButton(string name)
        {
            var want = name ?? string.Empty;
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                return b;
            }
            return null;
        }

        internal static void Click(string name)
        {
            if (EventSystem.current == null) { Warn("CLICK-LEGACY no Eventsystem name=" + name); return; }
            var b = FindButton(name);
            if (b == null) { Warn("CLICK-LEGACY miss name=" + name); return; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = b.transform as RectTransform;
            Vector2 c;
            if (rt != null && ScreenRect(rt, out c)) ped.position = c;
            ExecuteEvents.Execute(b.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-LEGACY name=" + name);
        }

        // ---- capture -----------------------------------------------------------------------
        /// <summary>
        /// Crop `w x h` screen pixels around `center` and write a PNG.
        /// The camera is re-rendered into a temporary RenderTexture (a screen capture of the
        /// game view would be asynchronous and could land on the wrong frame -- both frames of a
        /// flip matter here).
        /// </summary>
        internal static bool CaptureCrop(string path, Camera cam, Vector2 center, int w, int h, out Rect rect, out string sig)
        {
            rect = new Rect(0, 0, 0, 0);
            sig = "(none)";
            if (cam == null) { WarnOnce("crop.nocam", "CROP: no camera"); return false; }
            var sw = Screen.width;
            var sh = Screen.height;
            if (sw < w + 2 || sh < h + 2) { WarnOnce("crop.small", "CROP: screen " + sw + "x" + sh + " too small for " + w + "x" + h); return false; }
            var x0 = Mathf.Clamp(Mathf.RoundToInt(center.x) - w / 2, 0, sw - w);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(center.y) - h / 2, 0, sh - h);
            rect = new Rect(x0, y0, w, h);
            // Buffers are CACHED across frames: allocating a 1920x1080 RenderTexture + a fresh
            // Texture2D every frame cost so much that the 11:56 run only reached ~3 fps (247 frames
            // in ~80 s).  A frozen window is cheaper and denser this way.
            try
            {
                if (_rtCache == null || _rtCache.width != sw || _rtCache.height != sh)
                {
                    if (_rtCache != null) { _rtCache.Release(); UnityEngine.Object.Destroy(_rtCache); }
                    _rtCache = new RenderTexture(sw, sh, 24);
                    _rtCache.name = "D2U26CaptureRT";
                }
                if (_texCache == null || _texCache.width != w || _texCache.height != h)
                {
                    if (_texCache != null) UnityEngine.Object.Destroy(_texCache);
                    _texCache = new Texture2D(w, h, TextureFormat.RGB24, false);
                    _texCache.name = "D2U26CaptureTex";
                }
                var prev = cam.targetTexture;
                cam.targetTexture = _rtCache;
                cam.Render();
                cam.targetTexture = prev;
                var old = RenderTexture.active;
                RenderTexture.active = _rtCache;
                _texCache.ReadPixels(rect, 0, 0);
                _texCache.Apply();
                RenderTexture.active = old;
                sig = Fnv(_texCache.GetRawTextureData());
                var png = _texCache.EncodeToPNG();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, png);
                return true;
            }
            catch (Exception e)
            {
                WarnOnce("crop.fail", "CROP-FAIL " + e.GetType().Name + ": " + e.Message);
                return false;
            }
            finally { RenderTexture.active = null; }
        }

        private static RenderTexture _rtCache;
        private static Texture2D _texCache;

        /// <summary>FNV-1a over the raw pixels: the frame fingerprint the offline judge compares.</summary>
        internal static string Fnv(byte[] data)
        {
            if (data == null) return "(nodata)";
            var h = 2166136261u;
            for (var i = 0; i < data.Length; i++) { h ^= data[i]; h *= 16777619u; }
            return h.ToString("x8");
        }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }
        internal static string F(float v) { return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture); }
        /// <summary>Vector2 -> "x,y" (NOT named Screen: that would shadow UnityEngine.Screen inside this class).</summary>
        internal static string Pt(Vector2 v) { return F(v.x) + "," + F(v.y); }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Cfg() { return Probe.Cfg(); }
        public static string Paths(string spec) { Probe.Paths(spec); return "PATHS-OK"; }
        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0) + " device=" + Probe.DeviceName(); }
        public static string SortKeys() { return Probe.SortKeys(); }
    }

    /// <summary>Installer. spec = "&lt;tag&gt;|&lt;save&gt;|&lt;tsv&gt;|&lt;done&gt;|&lt;cropdir&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("D2U26SortDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>One Play session: reach Stage (Town), park the character at three anti-diagonal
    /// distances from Akara and write one TSV row + one crop PNG per frame.</summary>
    public class Driver : MonoBehaviour
    {
        internal const int HoldFrames = 36;         // >= 30 per geometry relation (ledger requirement)
        internal const int CropW = 320;             // crop window around the overlap point
        internal const int CropH = 240;
        private const float IdleSettle = 1.2f;
        private const int FrameCap = 900;

        // geometry relations (ordered from "2 cells away" to "same cell")
        private static readonly int[] Holds = new int[] { 2, 1, 0 };

        private string _tag = "u26";
        private string _save = "g66";
        private string _tsv = string.Empty;
        private string _cropDir = string.Empty;
        private string _donePath = string.Empty;
        private int _step;
        private float _at;
        private bool _done;
        private float _trigAt;
        private bool _bootSpaceSent;
        private bool _bootSpaceUp;
        private float _bootSpaceAt;
        private string _lastPanel = "(null)";
        private bool _sawLoading;
        private int _totalFrames;

        // ---- geometry ----------------------------------------------------------------------
        private Vector2Int _akara;                  // the NPC's own cell (IMapModule.NpcPoints[0])
        private int _holdIdx = -1;                  // index into Holds
        private Vector2Int _holdTarget;             // the cell this hold must be standing on
        private bool _arrived;                      // arrived at _holdTarget and stopped (gates the freeze)
        private int _freezeLogs;                    // FREEZE-REASSERT log budget (a few lines are enough)
        private bool _colsChecked;                   // one-shot TSV column-count guard (see Record)

        // ---- recording ---------------------------------------------------------------------
        private bool _recording;
        private readonly List<string> _rows = new List<string>();
        private int _recFrames;
        private int _holdFrames;
        private string _lastSig = string.Empty;
        private int _sigFlips;
        private readonly List<string> _sigTail = new List<string>();

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            if (parts.Length > 2) _tsv = parts[2];
            if (parts.Length > 3) _donePath = parts[3];
            if (parts.Length > 4) _cropDir = parts[4];
            Probe.Paths(_donePath);
            _step = 0;
            _at = Time.unscaledTime;
            Probe.Log("PROBE-INSTALL done=" + _donePath + " frame=" + Time.frameCount);
            Probe.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" tsv=" + _tsv + " crops=" + _cropDir);
        }

        private void Update()
        {
            if (_done) return;
            try { Step(); }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        /// <summary>Record AFTER the game tick chain of this frame (ViewModule writes positions in Update).</summary>
        private void LateUpdate()
        {
            if (!_recording || _done) return;
            try
            {
                // FREEZE RE-ASSERT (this is the fix for the 11:56 run, where EVERY recorded row had
                // frozen=0): the game's own flow re-pins Time.timeScale every frame, so setting it
                // once in Update is undone before the frame is drawn.  Re-assert it here, immediately
                // BEFORE recording, so the recorded frame really is a frozen frame (this frame's and
                // the previous frame's dt are both 0 => neither movement nor animation advanced).
                if (_arrived && Time.timeScale != 0f)
                {
                    Time.timeScale = 0f;
                    if (_freezeLogs < 3)
                    {
                        _freezeLogs++;
                        Probe.KV("FREEZE-REASSERT", "frame=" + Time.frameCount + " idx=" + _holdIdx
                            + " dt=" + Time.deltaTime.ToString("0.######") + " (the game had restored it)");
                    }
                }
                Record();
            }
            catch (Exception ex) { Probe.WarnOnce("rec.fail", "RECORD-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        // ================================================================ plumbing ==========
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }

        private void Reached(string panel)
        {
            Probe.KV("FLOW", _lastPanel + "->" + panel + " ok after "
                + (Time.unscaledTime - _trigAt).ToString("0.00") + "s fsm=" + Fsm());
            _lastPanel = panel;
            Next();
        }

        private bool Tout(string at, float seconds)
        {
            if (!Elapsed(seconds)) return false;
            Probe.KV("STEP-TIMEOUT", "at=" + at + " elapsed=" + (Time.unscaledTime - _at).ToString("0.0") + " fsm=" + Fsm());
            Next();
            return true;
        }

        private bool ClickButton(string name)
        {
            var b = Probe.FindButton(name);
            if (b == null)
            {
                Probe.Warn("UI-BUTTON-MISS name=" + name + " -> legacy ExecuteEvents path");
                Probe.Click(name);
                return false;
            }
            Vector2 c;
            var has = Probe.ScreenRect(b.transform as RectTransform, out c);
            Probe.KV("CLICKTOP", "name=" + name + " screen=" + Probe.Pt(c) + " has=" + (has ? 1 : 0));
            if (!has) { Probe.Click(name); return false; }
            return true;
        }

        // ================================================================ recording ==========
        private void StartRecording()
        {
            _rows.Clear();
            _recFrames = 0;
            _recording = true;
            Probe.KV("REC-START", "frame=" + Time.frameCount + " tsv=" + _tsv);
        }

        private void Record()
        {
            var p = Probe.Player();
            var cam = UnityEngine.Camera.main;
            if (p == null || cam == null) return;

            var pNode = Probe.PlayerNode();
            var nNode = Probe.NpcNode(0);                        // NpcId.Akara == 0
            var pSr = Probe.Sr(pNode);
            var nSr = Probe.Sr(nNode);

            var pg = p.Grid;
            var pw = p.World;
            var nw = nNode != null ? nNode.transform.position : new Vector3(float.NaN, float.NaN, float.NaN);
            var pz = pNode != null ? pNode.transform.position.z : float.NaN;
            var nz = nNode != null ? nNode.transform.position.z : float.NaN;
            var pOrder = pSr != null ? pSr.sortingOrder : int.MinValue;
            var nOrder = nSr != null ? nSr.sortingOrder : int.MinValue;

            // geometry relation between the player and the NPC (grid space)
            // The hold geometry walks along the ANTI-diagonal of the NPC: offset (k, -k) => the NPC is
            // at player + (k, -k), i.e. dx == -dy.  (The 11:56 run used dx == dy and therefore filed
            // every anti-diagonal frame under "other".)
            var dx = _akara.x - pg.x;
            var dy = _akara.y - pg.y;
            // The player is parked at akara + (k, -k), so relative to the PLAYER the NPC sits at
            // (-k, +k): dx == -dy with dx NEGATIVE.  (The 12:22 run required dx > 0 and therefore
            // filed both anti-diagonal HOLDS under "other", leaving antiDiag1/2 with transit frames only.)
            var rel = (dx == 0 && dy == 0) ? "sameCell"
                    : ((dx == -dy && dx != 0) ? ("antiDiag" + Math.Abs(dx)) : "other");

            // screen overlap point (midpoint of the two sprites) -- drives the crop window
            var ps = cam.WorldToScreenPoint(pw);
            var ns = cam.WorldToScreenPoint(nw);
            var mid = new Vector2((ps.x + ns.x) * 0.5f, (ps.y + ns.y) * 0.5f);

            Rect rect;
            string sig;
            var crop = Path.Combine(_cropDir, "u26_" + _tag + "_f" + _recFrames.ToString("0000") + ".png");
            var ok = Probe.CaptureCrop(crop, cam, mid, CropW, CropH, out rect, out sig);
            if (!ok) crop = "(none)";

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder(220);
            sb.Append(Time.frameCount.ToString(ci)).Append('\t')
              .Append(Time.deltaTime.ToString("0.######", ci)).Append('\t')
              .Append(_holdIdx.ToString(ci)).Append('\t')
              .Append(rel).Append('\t')
              .Append(pg.x).Append('\t').Append(pg.y).Append('\t')
              .Append(_akara.x).Append('\t').Append(_akara.y).Append('\t')
              .Append(pOrder).Append('\t').Append(nOrder).Append('\t')
              .Append(Probe.F(pz)).Append('\t').Append(Probe.F(nz)).Append('\t')
              .Append(Probe.F(pw.x)).Append('\t').Append(Probe.F(pw.y)).Append('\t')
              .Append(Probe.F(nw.x)).Append('\t').Append(Probe.F(nw.y)).Append('\t')
              // ps/ns are screen POINTS: they must expand into the four columns `psx psy nsx nsy`
              // declared in the header.  Writing "x,y" as ONE field shifted every later column left
              // by two, which is why the 12:22 run read frozen=0 for every row (measured).
              .Append(Probe.F(ps.x)).Append('\t').Append(Probe.F(ps.y)).Append('\t')
              .Append(Probe.F(ns.x)).Append('\t').Append(Probe.F(ns.y)).Append('\t')
              .Append(rect.x.ToString(ci)).Append(',').Append(rect.y.ToString(ci)).Append(',')
              .Append(rect.width.ToString(ci)).Append('x').Append(rect.height.ToString(ci)).Append('\t')
              .Append(sig).Append('\t')
              .Append(pOrder == nOrder ? "PRIMARY-EQUAL" : "primary-differs").Append('\t')
              .Append(crop).Append('\t')
              // frozen = Time.timeScale == 0: the scene is completely static (no movement, no
              // animation advance), so ANY pixel difference between two frozen frames can only
              // come from the draw order itself.  That is what makes the pixel row decidable.
              .Append(Time.timeScale == 0f ? "1" : "0").Append('\t')
              .Append("moving=" + (p.IsMoving ? 1 : 0) + " ts=" + Time.timeScale.ToString("0.##", ci));
            var row = sb.ToString();
            if (!_colsChecked)
            {
                _colsChecked = true;
                var got = 1;
                for (var i = 0; i < row.Length; i++) { if (row[i] == '\t') got++; }
                var want = HeaderCols();
                if (got != want)
                    Probe.Warn("ROW-COLUMNS MISMATCH row=" + got + " header=" + want
                        + " -> the offline judge would read the wrong columns; fix the append chain");
                else
                    Probe.KV("ROW-COLUMNS", "row=" + got + " == header=" + want + " OK");
            }
            _rows.Add(row);

            // signature alternation (the classic flicker shape A-B-A) -- recomputed offline too
            if (_lastSig.Length > 0 && sig != _lastSig)
            {
                if (_sigTail.Count >= 2 && _sigTail[_sigTail.Count - 2] == sig) _sigFlips++;
            }
            _sigTail.Add(sig);
            if (_sigTail.Count > 4) _sigTail.RemoveAt(0);
            _lastSig = sig;

            Probe.Log("SORTKEYS f=" + Time.frameCount + " rel=" + rel + " pOrder=" + pOrder + " nOrder=" + nOrder
                + " pz=" + Probe.F(pz) + " nz=" + Probe.F(nz) + " sig=" + sig + " keys=" + Probe.SortKeys());

            _recFrames++;
            _totalFrames++;
            if (_recFrames >= FrameCap) StopRecording("frame-cap");
        }

        /// <summary>The one and only TSV header.  `Record` asserts that every data row has the same
        /// field count -- a mismatch silently shifts columns and makes the offline judge read the
        /// wrong ones (measured twice: the 11:56 and 12:22 runs both reported frozen=0 because of it).</summary>
        private static string HeaderRow()
        {
            return "frame\tdt\thold\trelation\tpgx\tpgy\tagx\tagy\tpOrder\tnOrder\tpz\tnz\t"
                 + "pwx\tpwy\tnwx\tnwy\tpsx\tpsy\tnsx\tnsy\trect\tsig\tprimary\tcropptr\tfrozen\tnote";
        }

        private static int HeaderCols()
        {
            var h = HeaderRow();
            var n = 1;
            for (var i = 0; i < h.Length; i++) { if (h[i] == '\t') n++; }
            return n;
        }

        private void StopRecording(string why)
        {
            if (!_recording) return;
            _recording = false;
            var sb = new StringBuilder();
            sb.Append(HeaderRow()).Append('\n');
            for (var i = 0; i < _rows.Count; i++) { sb.Append(_rows[i]).Append('\n'); }
            try
            {
                var dir = Path.GetDirectoryName(_tsv);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_tsv, sb.ToString());
                Probe.KV("TSV-WRITTEN", "path=" + _tsv + " rows=" + _rows.Count
                    + " bytes=" + new FileInfo(_tsv).Length);
            }
            catch (Exception ex) { Probe.Warn("TSV-FAIL " + ex.GetType().Name + ": " + ex.Message); }
            Probe.KV("REC-STOP", "why=" + why + " frames=" + _recFrames
                + " sigFlipsInRun=" + _sigFlips + " fsm=" + Fsm());
        }

        // ================================================================ chain =============
        private void Step()
        {
            switch (_step)
            {
                // ---------------------------------------------------------- 0 environment
                case 0:
                    {
                        var device = Probe.DeviceName();
                        Probe.Log("ENV device=\"" + device + "\" type=" + SystemInfo.graphicsDeviceType
                            + " res=" + Screen.width + "x" + Screen.height
                            + " unity=" + Application.unityVersion
                            + " isPlaying=" + (Application.isPlaying ? 1 : 0));
                        if (Probe.SoftwareRaster(device))
                        {
                            Probe.Warn("DEVICE-SOFTWARE-RASTER device=\"" + device
                                + "\" => every rendering/pixel verdict on this machine is INVALID; chain not run");
                            Finish("software-raster");
                            return;
                        }
                        Probe.Log("PROBE-BEGIN frame=" + Time.frameCount + " fsm=" + Fsm());
                        _lastPanel = "boot";
                        _trigAt = Time.unscaledTime;
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 1 boot -> main menu
                case 1:
                    if (!_bootSpaceSent)
                    {
                        if (!BootOpen()) { Tout("boot-wait", 30f); return; }
                        if (!Elapsed(1.2f)) return;
                        Probe.Log("BOOT injection: real Keyboard Space press");
                        Probe.KeyDown("space");
                        _bootSpaceSent = true;
                        _bootSpaceAt = Time.unscaledTime;
                        _trigAt = _bootSpaceAt;
                        return;
                    }
                    if (!_bootSpaceUp && Time.unscaledTime - _bootSpaceAt >= 0.25f) { Probe.KeyUp(); _bootSpaceUp = true; }
                    if (!BootOpen()) { Next(); return; }
                    if (Time.unscaledTime - _bootSpaceAt > 10f)
                    {
                        Probe.Warn("BOOT panel still open 10s after the Space press");
                        Next();
                    }
                    return;

                case 2:
                    if (!MenuOpen()) { Tout("mainmenu-wait", 25f); return; }
                    Reached("MainMenu");
                    return;

                case 3:
                    if (!Elapsed(1.0f)) return;
                    ClickButton("Single");
                    Next();
                    return;

                case 4:
                    if (!SelectOpen()) { Tout("charselect-wait", 25f); return; }
                    Reached("CharSelect");
                    return;

                case 5:
                    if (!Elapsed(0.6f)) return;
                    var pick = Probe.FirstSaveName();
                    if (!string.IsNullOrEmpty(pick)) _save = pick;
                    Probe.KV("ENTER-STAGE", "save=\"" + _save + "\" roster=" + (pick ?? "(none)"));
                    _trigAt = Time.unscaledTime;
                    Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                    Next();
                    return;

                case 6:
                    if (!_sawLoading && LoadingOpen()) _sawLoading = true;
                    if (!(HudOpen() && Fsm() == "Stage")) { Tout("stage-wait", 45f); return; }
                    if (_lastPanel != "Stage")
                    {
                        Probe.KV("FLOW", (_sawLoading ? "Loading" : _lastPanel) + "->Stage ok after "
                            + (Time.unscaledTime - _trigAt).ToString("0.00") + "s fsm=" + Fsm());
                        _lastPanel = "Stage";
                        _at = Time.unscaledTime;
                        return;
                    }
                    if (!Elapsed(IdleSettle)) return;
                    Next();
                    return;

                // ---------------------------------------------------------- 7 resolve Akara
                case 7:
                    {
                        var map = Probe.Map();
                        var p = Probe.Player();
                        if (map == null || p == null) { Tout("no-modules", 10f); return; }
                        Probe.KV("STAGE-READY", "area=" + map.Area + " map=" + map.Width + "x" + map.Height
                            + " grid=" + Probe.Grid(p.Grid) + " playerNode=" + (Probe.PlayerNode() != null ? 1 : 0)
                            + " npcNode=" + (Probe.NpcNode(0) != null ? 1 : 0));
                        Probe.Check("area-is-town", map.Area == AreaId.Town,
                            "actual=" + map.Area + " (the NPC views only exist in the Town)");
                        var pts = map.NpcPoints;
                        if (pts == null || pts.Count == 0)
                        {
                            Probe.Warn("NO-NPC-POINTS: IMapModule.NpcPoints is empty -> cannot run the chain");
                            Finish("no-npc-points");
                            return;
                        }
                        _akara = pts[0];
                        Probe.KV("AKARA", "grid=" + Probe.Grid(_akara) + " walkable=" + (map.Walkable(_akara) ? 1 : 0)
                            + " npcNodes=" + (Probe.NpcNode(0) != null ? 1 : 0));
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 8 first hold target
                case 8:
                    if (!NextHold(-1)) return;               // resolves and issues MoveTo for Holds[0]
                    StartRecording();
                    Next();
                    return;

                // ---------------------------------------------------------- 9 drive + record
                case 9:
                    {
                        var p = Probe.Player();
                        if (p == null) { StopRecording("player-lost"); Finish("player-lost"); return; }
                        if (!_recording) { Finish("recorded"); return; }
                        if (Elapsed(240f)) { StopRecording("timeout"); Finish("timeout"); return; }

                        // ARRIVAL GATE (deadlock guard): the freeze below stops the motor (dt = 0),
                        // so freezing BEFORE the character has reached this hold's target would stall
                        // the chain for ever.  Only count a hold frame once the character really sits
                        // ON the requested cell (grid match + path cleared by PlayerMotor.Arrive).
                        var arrived = (p.Grid == _holdTarget) && !p.IsMoving;
                        _arrived = arrived;
                        if (arrived) _holdFrames++;
                        else _holdFrames = 0;

                        // FROZEN WINDOW: once the character has stopped, pin Time.timeScale = 0 for
                        // the rest of this hold.  The scene then cannot change on its own (no
                        // movement, no animation advance) => any pixel difference inside this
                        // window can only come from the draw order.  Movement needs dt > 0, so the
                        // scale is restored right before the next MoveTo (see NextHold).
                        if (arrived && _holdFrames == 1)
                        {
                            Time.timeScale = 0f;      // re-asserted every LateUpdate (see there)
                            Probe.KV("HOLD-FROZEN", "idx=" + _holdIdx + " k=" + Holds[_holdIdx]
                                + " frame=" + Time.frameCount + " grid=" + Probe.Grid(p.Grid)
                                + " ts=" + Time.timeScale.ToString("0.##")
                                + " (scene frozen for the next " + HoldFrames + " frames)");
                        }
                        if (arrived && _holdFrames >= HoldFrames && _recFrames > 0)
                        {
                            Probe.KV("HOLD-DONE", "idx=" + _holdIdx + " k=" + Holds[_holdIdx]
                                + " frames=" + _recFrames + " grid=" + Probe.Grid(p.Grid));
                            if (!NextHold(_holdIdx)) return;  // all holds done -> stop
                        }
                        return;
                    }

                default:
                    Finish("end");
                    return;
            }
        }

        /// <summary>
        /// Issue the MoveTo for Holds[idx+1] and return true when a new hold was started,
        /// false when every hold is done (the caller then finishes the run).
        /// The target is resolved ON THE ANTI-DIAGONAL through Akara (offset k*(1,-1)) so that
        /// gx+gy stays equal to the NPC's for the whole geometry family; a blocked cell falls
        /// back to the closest smaller k (never off the diagonal -- that would change the
        /// primary key and void the relation).
        /// </summary>
        private bool NextHold(int idx)
        {
            var map = Probe.Map();
            var p = Probe.Player();
            if (map == null || p == null) { Tout("no-modules", 10f); return false; }

            var next = idx + 1;
            if (next >= Holds.Length)
            {
                StopRecording("all-holds-done");
                Probe.KV("CHAIN-DONE", "holds=" + Holds.Length + " frames=" + _recFrames);
                Finish("all-holds-done");
                return false;
            }

            if (Time.timeScale != 1f)
            {
                Time.timeScale = 1f;                 // movement needs dt > 0; the frozen window ends here
                Probe.KV("HOLD-UNFROZEN", "idx=" + idx + " frame=" + Time.frameCount);
            }

            var k = Holds[next];
            var target = _akara;
            var usedK = 0;
            for (var kk = k; kk >= 0; kk--)
            {
                var c = new Vector2Int(_akara.x + kk, _akara.y - kk);
                if (map.InBounds(c) && map.Walkable(c)) { target = c; usedK = kk; break; }
            }
            _holdIdx = next;
            _holdFrames = 0;
            _arrived = false;             // re-armed: the freeze must not start before the next arrival
            _holdTarget = target;         // the freeze is gated on standing exactly here (see case 9)
            Probe.KV("HOLD " + next, "k=" + k + " usedK=" + usedK + " target=" + Probe.Grid(target)
                + " from=" + Probe.Grid(p.Grid) + " frame=" + Time.frameCount);
            if (usedK != k)
            {
                Probe.Warn("HOLD-RELATION-DEGRADED k=" + k + " -> usedK=" + usedK
                    + " (the anti-diagonal cell was not walkable; the relation is the closest one available)");
            }
            p.MoveTo(target);
            return true;
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            if (_recording) StopRecording("finish-" + why);
            Probe.KV("FINISH", "why=" + why + " step=" + _step + " recordedFrames=" + _recFrames
                + " device=" + Probe.DeviceName() + " screen=" + Screen.width + "x" + Screen.height);
            Probe.WriteFile(_donePath, "TOUR-DONE tag=" + _tag + " why=" + why + " frames=" + _recFrames
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
    }
}
