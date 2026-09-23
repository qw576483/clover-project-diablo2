// =============================================================================
// rebuildentries_probe.cs -- ONE Play session that measures the TWO entries the
// travel-black slice left open:
//   (1) LOAD ENTRY   : menu -> Events.CharSelectRequest -> GoStage -> Loading -> Stage
//                      i.e. AppFlow.OnCharSelectRequest -> GoStage(ToArea(save.areaId))
//   (2) DEATH REVIVE : far from the area spawn -> Events.ReviveRequest
//                      i.e. CombatModule.RevivePlayer -> PlayerModule.Revive
//                      (= Teleport(map.SpawnPoint), SAME area, NO ShowArea / NO StartRebuild)
//
// WHAT IS JUDGED (same reading as the travel-black slice, so the two can be
// compared side by side):
//   "at the FIRST PLAYABLE FRAME of each entry, the built ground chunks must be a
//    superset of the chunks the screen shows."
//   Readings: rebuildInProgress / builtGround / pending / MISSING (built set vs the
//   visible chunk range) / blackfrac (from the composited PNG, .ai-tmp screenshot).
//
// HOW (plumbing reused verbatim from tools/probes/drivers/s2_drive.cs -- real
// InputSystem events for the menu chain, the same reflection readings of
// `MapView._view`): this file is SELF-CONTAINED because `unity command run_script`
// compiles one file per call (internal members of another probe are not visible).
//
// TOUCHES NO PRODUCT CODE (reflection + Game.Event.Emit only). ASCII ONLY
// (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace RE
{
    /// <summary>Reflection / log / input / chunk-reading helpers (shape = S2.Probe).</summary>
    public static class Probe
    {
        internal const string Tag = "RE";
        private static string _done = string.Empty;
        internal static string DonePath { get { return _done; } }

        internal static void Log(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, msg); else UnityEngine.Debug.Log("[" + Tag + "] " + msg);
        }
        internal static void Warn(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, msg); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }
        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }
        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) { Warn("MARKER path empty"); return; }
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(path, content);
            }
            catch (Exception ex) { Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- reflection -------------------------------------------------------------------
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
        internal static object Cam() { return CtxMember("Camera"); }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }
        internal static string V(Vector2 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")"; }
        internal static Vector2Int PlayerGrid() { var p = Player(); return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue); }

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

        /// <summary>Invokes a (possibly non-public) parameterless method on the live object.</summary>
        internal static string Call0(object o, string name)
        {
            if (o == null) return "no-object";
            var t = o.GetType();
            while (t != null)
            {
                var m = t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (m != null)
                {
                    try { m.Invoke(o, null); return "ok"; }
                    catch (Exception ex)
                    {
                        var inner = ex.InnerException != null ? ex.InnerException : ex;
                        return "throw " + inner.GetType().Name + ": " + inner.Message;
                    }
                }
                t = t.BaseType;
            }
            return "no-method";
        }

        /// <summary>Tries the same name on the interface first, then on the runtime type.</summary>
        internal static string Call0Any(object o, params string[] names)
        {
            if (o == null) return "no-object";
            var sb = new StringBuilder();
            foreach (var n in names)
            {
                var r = Call0(o, n);
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(n).Append('=').Append(r);
                if (r == "ok") break;
            }
            return sb.ToString();
        }

        // ---- environment / input ------------------------------------------------------------
        internal static bool SoftwareRaster(string d)
        {
            var s = d ?? string.Empty;
            return s.IndexOf("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("SwiftShader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var kb = Keyboard.current;
            if (kb == null) kb = InputSystem.AddDevice<Keyboard>();
            if (UnityEngine.InputSystem.Mouse.current == null) InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
            Log("DEVICE device=\"" + SystemInfo.graphicsDeviceName + "\" res=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate);
            Log("CFG gameRunning=" + (Game.IsRunning ? 1 : 0) + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)"));
            return "CFG-OK";
        }

        internal static bool TryParseKey(string name, out Key key)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = Key.Space; return true;
                case "i": key = Key.I; return true;
                case "escape": key = Key.Escape; return true;
                default: key = Key.None; return false;
            }
        }
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
            Key k;
            if (!TryParseKey(name, out k)) { Warn("KEYDOWN unknown-key=" + name); return; }
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            Log("KEYDOWN key=" + name);
        }
        internal static void KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
        }
        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            var s = new MouseState { position = pos };
            if (leftDown) s = s.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, s);
        }

        // ---- UI -----------------------------------------------------------------------------
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

        internal static Button FindButton(string name, out string diag)
        {
            diag = string.Empty;
            var want = name ?? string.Empty;
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            Button pick = null; var n = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                n++; if (pick == null) pick = b;
            }
            diag = "buttons=" + all.Length + " candidates=" + n;
            return pick;
        }

        internal static string RaycastTop(Vector2 p)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current) { position = p };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            return hits[0].gameObject == null ? "(null)" : hits[0].gameObject.name;
        }

        internal static string ClickLegacy(string name)
        {
            if (EventSystem.current == null) { Warn("CLICK-LEGACY no-eventsystem name=" + name); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(name, out diag);
            if (pick == null) { Warn("CLICK-LEGACY miss name=" + name + " " + diag); return "ERR-miss"; }
            if (!pick.interactable) { Warn("CLICK-LEGACY not-interactable name=" + name); return "ERR-not-interactable"; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            ExecuteEvents.Execute(pick.gameObject, ped, ExecuteEvents.pointerClickHandler);
            KV("CLICK-LEGACY", "name=" + name + " " + diag);
            return "CLICKED";
        }

        // ---- chunk readings (identical formulas to s2_drive.Probe.ChunkState/ChunkMiss) -----
        private static object ViewOfMap()
        {
            var map = Map();
            if (map == null) return null;
            var t = map.GetType();
            while (t != null)
            {
                var f = t.GetField("_view", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(map);
                t = t.BaseType;
            }
            return null;
        }
        private static object ViewField(object view, string name)
        {
            if (view == null) return null;
            var f = view.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(view) : null;
        }
        private static List<Vector2Int> BuiltChunks(object view)
        {
            var res = new List<Vector2Int>();
            var d = ViewField(view, "_groundChunks") as System.Collections.IDictionary;
            if (d == null) return res;
            foreach (var k in d.Keys) { if (k is Vector2Int v) res.Add(v); }
            return res;
        }
        private static bool ExpectedChunkRange(out Vector2Int e0, out Vector2Int e1, out string diag)
        {
            e0 = Vector2Int.zero; e1 = Vector2Int.zero; diag = "(no-map)";
            var map = Map();
            var cam = Camera.main;
            if (map == null) return false;
            var cx = (map.Width + 15) / 16;
            var cy = (map.Height + 15) / 16;
            if (cam == null) { diag = "(no-camera)"; return false; }
            var minX = int.MaxValue; var minY = int.MaxValue; var maxX = int.MinValue; var maxY = int.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? 0f : Screen.width;
                var sy = (i & 2) == 0 ? 0f : Screen.height;
                var g = Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
                maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
            }
            e0 = new Vector2Int(Mathf.Clamp(minX / 16 - 1, 0, cx - 1), Mathf.Clamp(minY / 16 - 1, 0, cy - 1));
            e1 = new Vector2Int(Mathf.Clamp(maxX / 16 + 1, 0, cx - 1), Mathf.Clamp(maxY / 16 + 1, 0, cy - 1));
            diag = "chunks=" + cx + "x" + cy + " gridCorners=[" + minX + "," + minY + ".." + maxX + "," + maxY + "]";
            return true;
        }

        /// <summary>One line: built/pending/job + MISSING (built set vs the on-screen visible chunk range).</summary>
        internal static string Chunk(string where)
        {
            var map = Map(); var view = ViewOfMap();
            if (view == null) return "where=" + where + " _view=(null)";
            var vt = view.GetType();
            var built = BuiltChunks(view);
            var pt = vt.GetProperty("PendingChunkCount");
            var bt = vt.GetProperty("BuiltChunkCount");
            var reb = vt.GetProperty("RebuildInProgress");
            var ret = vt.GetProperty("PendingRetireChunks");
            var sb = new StringBuilder();
            sb.Append("where=").Append(where)
              .Append(" area=").Append(map != null ? ((int)map.Area).ToString() : "-")
              .Append(" map=").Append(map != null ? map.Width + "x" + map.Height : "-")
              .Append(" chunked=").Append(As01(ViewField(view, "_chunked")))
              .Append(" builtGround=").Append(built.Count)
              .Append(" BuiltChunkCount=").Append(bt != null ? bt.GetValue(view) : "-")
              .Append(" PendingChunks=").Append(pt != null ? pt.GetValue(view) : "-")
              .Append(" PendingRetire=").Append(ret != null ? ret.GetValue(view) : "-")
              .Append(" rebuildInProgress=").Append(reb != null && (bool)reb.GetValue(view) ? 1 : 0)
              .Append(" player=").Append(Grid(PlayerGrid()))
              .Append(" spawn=").Append(map != null ? Grid(map.SpawnPoint) : "-");
            Vector2Int e0, e1; string diag;
            if (!ExpectedChunkRange(out e0, out e1, out diag)) return sb.Append(" ").Append(diag).ToString();
            var n = 0; var list = new StringBuilder();
            for (var cx2 = e0.x; cx2 <= e1.x; cx2++)
            {
                for (var cy2 = e0.y; cy2 <= e1.y; cy2++)
                {
                    var c = new Vector2Int(cx2, cy2);
                    var found = false;
                    for (var i = 0; i < built.Count; i++) { if (built[i] == c) { found = true; break; } }
                    if (found) continue;
                    n++;
                    if (list.Length < 200) { if (list.Length > 0) list.Append(' '); list.Append("(" + cx2 + "," + cy2 + ")"); }
                }
            }
            var total = (e1.x - e0.x + 1) * (e1.y - e0.y + 1);
            return sb.Append(" visible=").Append(e0).Append("..").Append(e1)
                     .Append(" builtInRange=").Append(total - n).Append(" MISSING=").Append(n)
                     .Append(" [").Append(list).Append("] ").Append(diag).ToString();
        }
        private static string As01(object b) { return (b is bool && (bool)b) ? "1" : "0"; }

        // ---- shots / camera / teleport -------------------------------------------------------
        internal static string Shot(string path)
        {
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                return "SHOT-OK " + path + " frame=" + Time.frameCount;
            }
            catch (Exception ex) { return "SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>Teleports the player and snaps the camera (same reflection as BWy.Api.Edge).</summary>
        internal static string To(int gx, int gy)
        {
            var gridT = FindType("UnityEngine.Vector2Int");
            var grid = gridT != null ? Activator.CreateInstance(gridT, gx, gy) : null;
            var player = CtxMember("Player");
            var cam = Cam();
            var sb = new StringBuilder();
            sb.Append("TO grid=(").Append(gx).Append(',').Append(gy).Append(") ");
            if (player != null && grid != null)
            {
                var m = player.GetType().GetMethod("TeleportTo", BindingFlags.Public | BindingFlags.Instance, null, new[] { gridT }, null);
                if (m != null) { m.Invoke(player, new[] { grid }); sb.Append("player=ok "); } else sb.Append("player=NO-TeleportTo ");
            }
            else sb.Append("player=null ");
            if (cam != null && grid != null)
            {
                var ms = cam.GetType().GetMethod("SetTargetGrid", BindingFlags.Public | BindingFlags.Instance, null, new[] { gridT }, null);
                if (ms != null) { ms.Invoke(cam, new[] { grid }); sb.Append("cam.target=ok "); } else sb.Append("cam=NO-SetTargetGrid ");
                var mp = cam.GetType().GetMethod("SnapToTarget", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (mp != null) { mp.Invoke(cam, null); sb.Append("cam.snap=ok"); } else sb.Append("cam=NO-SnapToTarget");
            }
            else sb.Append("cam=null");
            return sb.ToString();
        }

        /// <summary>The walkable cell farthest (Chebyshev) from the area spawn -- the "died far away" spot.</summary>
        internal static Vector2Int FarWalkableCell(out int walkableSeen)
        {
            walkableSeen = 0;
            var map = Map();
            if (map == null) return new Vector2Int(-1, -1);
            var spawn = map.SpawnPoint;
            var best = new Vector2Int(-1, -1); var bestD = -1;
            for (var x = 0; x < map.Width; x++)
            {
                for (var y = 0; y < map.Height; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    walkableSeen++;
                    var d = Mathf.Max(Mathf.Abs(g.x - spawn.x), Mathf.Abs(g.y - spawn.y));
                    if (d > bestD) { bestD = d; best = g; }
                }
            }
            return best;
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000"); }
        public static string Read(string where) { return Probe.Chunk(where); }
        public static string Shot(string path) { return Probe.Shot(path); }
        public static string To(string arg)
        {
            var p = (arg ?? string.Empty).Split(',');
            int gx, gy;
            if (p.Length < 2 || !int.TryParse(p[0].Trim(), out gx) || !int.TryParse(p[1].Trim(), out gy)) return "TO-BAD-ARG";
            return "TO " + Probe.To(gx, gy);
        }
    }

    /// <summary>Installer: spec = "&lt;tag&gt;|&lt;save name&gt;|&lt;done marker&gt;|&lt;screenshot dir&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            Probe.Cfg();
            var go = new GameObject("REEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>The one-session chain: menu -> load entry (sampled at the first playable frame) -> death revive.</summary>
    public class Driver : MonoBehaviour
    {
        private string _tag = "re1";
        private string _save = "RE" ;
        private bool _classPicked;
        private int _step;
        private float _at;
        private bool _done;
        private float _trigAt;
        private bool _invalid;
        private bool _bootSent, _bootUp;
        private float _bootAt;
        private int _clickPhase, _clickFrames;
        private Vector2 _clickPos;
        private string _clickName = string.Empty;
        private bool _loadingSeen;
        private float _entryAt;
        private int _entrySamples;
        private bool _reviveSent;
        private float _reviveAt;
        private int _reviveSamples;
        private int _seriesFrames;
        private string _seriesPhase = string.Empty;
        private Vector2Int _spawn;
        private Vector2Int _far;
        private float _deathAt;
        private int _area0 = -1;
        private float _t0;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            var done = parts.Length > 2 ? parts[2] : string.Empty;
            var shots = parts.Length > 3 ? parts[3] : string.Empty;
            Probe.Paths(done);
            _shotDir = shots;
            Probe.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" shots=" + shots + " frame=" + Time.frameCount);
        }

        private string _shotDir = string.Empty;

        private void Update()
        {
            if (_done) return;
            try { TickClick(); Step(); Series(); }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        // ---- per-frame series (the primary evidence: catches transient frames) --------------
        private void Series()
        {
            if (_seriesFrames <= 0) return;
            _seriesFrames--;
            Probe.KV("RE-SERIES", "phase=" + _seriesPhase + " t=" + Time.unscaledTime.ToString("0.000")
                + " frame=" + Time.frameCount
                + " loading=" + (LoadingOpen() ? 1 : 0)
                + " fsm=" + Fsm()
                + " " + Probe.Chunk("series"));
        }

        private void ArmSeries(string phase, int frames)
        {
            _seriesPhase = phase;
            _seriesFrames = frames;
        }

        // ---- plumbing -----------------------------------------------------------------------
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }

        private void TickClick()
        {
            if (_clickPhase == 0) return;
            _clickFrames--;
            if (_clickFrames > 0) return;
            if (_clickPhase == 1)
            {
                Probe.MouseState(_clickPos, true);
                Probe.KV("UI-DOWN", "node=" + _clickName + " pos=" + Probe.V(_clickPos) + " frame=" + Time.frameCount);
                _clickPhase = 2; _clickFrames = 2; return;
            }
            Probe.MouseState(_clickPos, false);
            Probe.KV("UI-UP", "node=" + _clickName + " pos=" + Probe.V(_clickPos) + " frame=" + Time.frameCount);
            _clickPhase = 0; _clickFrames = 0;
        }

        private bool ClickButton(string name, string tag)
        {
            string diag;
            var b = Probe.FindButton(name, out diag);
            if (b == null)
            {
                Probe.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Probe.ClickLegacy(name);
                return false;
            }
            Vector2 lo, hi, c;
            var has = Probe.ScreenRect(b.transform as RectTransform, out lo, out hi, out c);
            Probe.KV("CLICKTOP", "tag=" + tag + " name=" + name + " interactable=" + (b.interactable ? 1 : 0)
                + " screen=" + Probe.V(c) + " label=\"" + (b.GetComponentInChildren<Text>(true) != null ? b.GetComponentInChildren<Text>(true).text : "-") + "\""
                + " raycastTop=\"" + Probe.RaycastTop(c) + "\" " + diag);
            if (!has) { Probe.Warn("UI-RECT-MISS name=" + name + " -> legacy path"); Probe.ClickLegacy(name); return false; }
            _clickPos = c; _clickName = name; _clickPhase = 1; _clickFrames = 2;
            return true;
        }

        private string Shots(string leaf) { return _shotDir + "/rebuildentries_" + leaf + ".png"; }

        // ---- chain ---------------------------------------------------------------------------
        private void Step()
        {
            switch (_step)
            {
                case 0:
                    {
                        var device = SystemInfo.graphicsDeviceName;
                        Probe.Log("ENV device=\"" + device + "\" type=" + SystemInfo.graphicsDeviceType
                            + " res=" + Screen.width + "x" + Screen.height + " unity=" + Application.unityVersion
                            + " runInBg=" + (Application.runInBackground ? 1 : 0));
                        if (Probe.SoftwareRaster(device))
                        {
                            _invalid = true;
                            Probe.Warn("DEVICE-SOFTWARE-RASTER device=\"" + device + "\" => the chain is not run");
                            Finish("software-raster");
                            return;
                        }
                        Probe.Log("PROBE-BEGIN frame=" + Time.frameCount + " fsm=" + Fsm());
                        _trigAt = Time.unscaledTime;
                        Next();
                        return;
                    }

                // ---- boot -> main menu -> (create a character if the roster is empty) -------------
                case 1:
                    if (!_bootSent)
                    {
                        if (!BootOpen()) { Tout("boot-wait", 40f); return; }
                        if (!Elapsed(1.2f)) return;
                        Probe.Log("BOOT injection: real Keyboard Space press");
                        Probe.KeyDown("space");
                        _bootSent = true; _bootAt = Time.unscaledTime; _trigAt = _bootAt;
                        return;
                    }
                    if (!_bootUp && Time.unscaledTime - _bootAt >= 0.25f) { Probe.KeyUp(); _bootUp = true; }
                    if (!BootOpen()) { Probe.KV("FLOW", "boot->MainMenu ok"); Next(); return; }
                    if (Time.unscaledTime - _bootAt > 15f) { Probe.Warn("BOOT still open 15s after Space"); Next(); }
                    return;

                case 2:
                    if (!MenuOpen()) { Tout("mainmenu-wait", 30f); return; }
                    Probe.KV("FLOW", "MainMenu reached; opening the character list (Single)");
                    Next();
                    return;

                case 3:
                    if (!Elapsed(0.8f)) return;
                    ClickButton("Single", "menu");
                    Next();
                    return;

                case 4:
                    if (!SelectOpen()) { Tout("charselect-wait", 30f); return; }
                    Probe.KV("FLOW", "CharSelect reached");
                    Next();
                    return;

                // create a fresh character through the panel's own handlers (same as s2d) -----------
                case 5:
                    if (!Elapsed(0.6f)) return;
                    ClickButton("Create", "select");
                    Next();
                    return;

                case 6:
                    if (!CreateOpen()) { Tout("charcreate-wait", 30f); return; }
                    {
                        Diablo2.UI.CharCreatePanel panel = null;
                        foreach (var p in UnityEngine.Object.FindObjectsByType<Diablo2.UI.CharCreatePanel>(FindObjectsSortMode.None))
                            if (p != null && p.gameObject.activeInHierarchy) { panel = p; break; }
                        if (panel == null) { Probe.Warn("CREATE-PANEL-MISS -> Back"); ClickButton("Back", "create"); Next(); return; }
                        _save = _save.Length > 0 && _save.StartsWith("RE") ? _save : "RE" + DateTime.Now.ToString("HHmmss");
                        var nf = panel.GetType().GetField("_nameBuffer", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (nf != null) nf.SetValue(panel, _save);
                        var m = panel.GetType().GetMethod("OnSpotClick", BindingFlags.NonPublic | BindingFlags.Instance);
                        Probe.KV("CREATE", "name=" + _save + " nameBufferSet=" + (nf != null ? 1 : 0) + " handler=" + (m != null ? 1 : 0));
                        if (m != null) m.Invoke(panel, new object[] { 0 });
                        _classPicked = true;
                        Next();
                        return;
                    }

                case 7:
                    if (!Elapsed(0.9f)) return;
                    if (!_classPicked) { Next(); return; }
                    ClickButton("Confirm", "create");
                    Next();
                    return;

                case 8:
                    if (!SelectOpen()) { Tout("charselect2-wait", 30f); return; }
                    Probe.KV("FLOW", "CharSelect reached again (character created)");
                    Next();
                    return;

                // =============== ENTRY 1: LOAD ENTRY (读到档就进图) ===============================
                case 9:
                    if (!Elapsed(0.6f)) return;
                    Probe.KV("ENTRY-LOAD", "emit " + Events.CharSelectRequest + "(\"" + _save + "\")"
                        + " => AppFlow.OnCharSelectRequest -> TryLoad -> GoStage(ToArea(save.areaId))");
                    _trigAt = Time.unscaledTime;
                    _entryAt = Time.unscaledTime;
                    _loadingSeen = false;
                    Game.Event.Emit<string>(Events.CharSelectRequest, _save);
                    Next();
                    return;

                // wait for Stage, but sample the FIRST PLAYABLE FRAME (the frame the loading
                // screen closes) -- per-frame series catches the transient frames.
                case 10:
                    if (LoadingOpen()) _loadingSeen = true;
                    if (!_loadingSeen) { Tout("loading-wait", 45f); return; }
                    if (LoadingOpen()) return;                       // still behind the loading screen
                    {
                        var dt = Time.unscaledTime - _entryAt;
                        Probe.KV("ENTRY-LOAD-FIRSTFRAME", "loading closed after " + dt.ToString("0.00")
                            + "s fsm=" + Fsm() + " hud=" + (HudOpen() ? 1 : 0) + " " + Probe.Chunk("load-firstframe"));
                        Probe.Shot(Shots("load_t0"));
                        ArmSeries("load", 120);                      // 120 frames of per-frame readings
                        _entrySamples = 0;
                        Next();
                        return;
                    }

                case 11:
                    {
                        var dt = Time.unscaledTime - _entryAt;
                        if (_entrySamples == 0 && dt >= 0.5f)
                        {
                            _entrySamples = 1;
                            Probe.KV("ENTRY-LOAD-T0.5", Probe.Chunk("load+0.5s"));
                            Probe.Shot(Shots("load_t0p5"));
                            return;
                        }
                        if (_entrySamples == 1 && dt >= 1.5f)
                        {
                            _entrySamples = 2;
                            Probe.KV("ENTRY-LOAD-T1.5", Probe.Chunk("load+1.5s"));
                            Probe.Shot(Shots("load_t1p5"));
                            Next();
                            return;
                        }
                        if (dt >= 4f) { Probe.Warn("load-entry sampling timed out"); Next(); }
                        return;
                    }

                // walk the player FAR away first (so the chunks around the spawn get reclaimed
                // exactly like after a long walk), then die there and revive.
                case 12:
                    if (!Elapsed(0.8f)) return;
                    var map = Probe.Map();
                    _spawn = map != null ? map.SpawnPoint : Vector2Int.zero;
                    _far = Probe.FarWalkableCell(out var seen);
                    Probe.KV("REVIVE-FAR-PICK", "spawn=" + Probe.Grid(_spawn) + " far=" + Probe.Grid(_far)
                        + " walkableSeen=" + seen + " area=" + (map != null ? ((int)map.Area).ToString() : "-"));
                    if (_far.x < 0) { Probe.Warn("REVIVE-ABORT no walkable cell"); Next(); return; }
                    Probe.Log("REVIVE-STEP " + Probe.To(_far.x, _far.y));
                    Next();
                    return;

                case 13:
                    if (!Elapsed(2.0f)) return;                      // let the refresh + release settle
                    Probe.KV("REVIVE-AT-FAR", "after the jump: " + Probe.Chunk("at-far"));
                    Probe.Shot(Shots("revive_at_far"));
                    Next();
                    return;

                case 14:
                    if (!Elapsed(0.4f)) return;
                    {
                        var p = Probe.Player();
                        var killed = Probe.Call0Any(p, "Kill", "Die");
                        Probe.KV("REVIVE-KILL", killed + " isDead=" + (p != null && p.IsDead ? 1 : 0)
                            + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                        if (p == null || !p.IsDead) { Probe.Warn("REVIVE-ABORT the player could not be killed"); Next(); return; }
                        _reviveAt = Time.unscaledTime;
                        Probe.KV("REVIVE-REQUEST", "emit " + Events.ReviveRequest
                            + " => CombatModule.RevivePlayer -> PlayerModule.Revive (Teleport(map.SpawnPoint), same area, no ShowArea)");
                        ArmSeries("revive", 120);
                        Game.Event.Emit(Events.ReviveRequest);
                        Probe.KV("REVIVE-T0", "same frame: " + Probe.Chunk("revive-t0"));
                        Probe.Shot(Shots("revive_t0"));
                        _reviveSamples = 0;
                        Next();
                        return;
                    }

                case 15:
                    {
                        var dt = Time.unscaledTime - _reviveAt;
                        if (_reviveSamples == 0 && dt >= 0.5f)
                        {
                            _reviveSamples = 1;
                            Probe.KV("REVIVE-T0.5", Probe.Chunk("revive+0.5s"));
                            Probe.Shot(Shots("revive_t0p5"));
                            return;
                        }
                        if (_reviveSamples == 1 && dt >= 1.5f)
                        {
                            _reviveSamples = 2;
                            Probe.KV("REVIVE-T1.5", Probe.Chunk("revive+1.5s"));
                            Probe.Shot(Shots("revive_t1p5"));
                            Next();
                            return;
                        }
                        if (dt >= 4f) { Probe.Warn("revive sampling timed out"); Next(); }
                        return;
                    }

                // =============== the CHUNKED area (Blood Moor 80x80 > 4096) ======================
                // Why: the town is NOT chunked (56x40 = 2240 <= BuildAllTileThreshold) so no chunk
                // is EVER released there -- the two readings above cannot falsify the hazard.
                // The chunked area is the only one where ReleaseFarChunks can reclaim the spawn.
                case 16:
                    {
                        var mv = Probe.Map();
                        var ex = (mv != null && mv.Exits != null && mv.Exits.Count > 0)
                            ? mv.Exits[0] : new Vector2Int(-1, -1);
                        Probe.KV("WALK-OUT", "area=" + (mv != null ? ((int)mv.Area).ToString() : "-")
                            + " exit=" + Probe.Grid(ex) + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                        if (ex.x < 0) { Probe.Warn("WALK-OUT-ABORT no exit cell"); Next(); return; }
                        _area0 = mv != null ? (int)mv.Area : -1;
                        _t0 = Time.unscaledTime;
                        Game.Event.Emit<Vector2Int>(Events.MoveCommand, ex);
                        Next();
                        return;
                    }

                case 17:
                    {
                        var mv17 = Probe.Map();
                        var now = mv17 != null ? (int)mv17.Area : -1;
                        if (now == _area0)
                        {
                            if (Time.unscaledTime - _t0 >= 90f) { Probe.Warn("WALK-OUT timeout area=" + now); Next(); }
                            return;
                        }
                        Probe.KV("AREA-2", "now=" + now + " (chunked area entry) " + Probe.Chunk("chunked-entry"));
                        Probe.Shot(Shots("chunked_entry"));
                        ArmSeries("chunked-entry", 60);
                        Next();
                        return;
                    }

                // die FAR away in the chunked area (so the spawn chunks really get reclaimed)
                // NOTE (round 2 lesson): the area switch has a SECOND beat (`Events.MapAreaReady` ->
                // `AppFlow.CompleteArrival`) that TELEPORTS THE PLAYER TO THE SPAWN.  A jump issued
                // before it landed was overwritten 0.1 s later (player was back at spawn at BP-AT-FAR),
                // which silently turned the revive reading into a no-op.  So: wait until the arrival is
                // over (player == spawn AND no rebuild in progress) and then verify the jump took.
                case 18:
                    if (!Elapsed(1.0f)) return;
                    var m2 = Probe.Map();
                    _spawn = m2 != null ? m2.SpawnPoint : Vector2Int.zero;
                    if (Probe.PlayerGrid() != _spawn)
                    {
                        if (Time.unscaledTime - _t0 > 20f) { Probe.Warn("BP-ABORT arrival never settled player=" + Probe.Grid(Probe.PlayerGrid()) + " spawn=" + Probe.Grid(_spawn)); Next(); }
                        return;
                    }
                    _far = Probe.FarWalkableCell(out var seen2);
                    Probe.KV("BP-FAR-PICK", "spawn=" + Probe.Grid(_spawn) + " far=" + Probe.Grid(_far)
                        + " walkableSeen=" + seen2 + " area=" + (m2 != null ? ((int)m2.Area).ToString() : "-")
                        + " (arrival settled: player==spawn)");
                    if (_far.x < 0) { Probe.Warn("BP-ABORT no walkable cell"); Next(); return; }
                    Probe.Log("BP-STEP " + Probe.To(_far.x, _far.y));
                    Next();
                    return;

                case 19:
                    if (!Elapsed(2.5f)) return;
                    if (Probe.PlayerGrid() != _far)
                    {
                        Probe.Warn("BP-JUMP-OVERWRITTEN player=" + Probe.Grid(Probe.PlayerGrid())
                            + " wanted=" + Probe.Grid(_far) + " -> retry once");
                        Probe.Log("BP-STEP2 " + Probe.To(_far.x, _far.y));
                        _at = Time.unscaledTime;
                        return;
                    }
                    Probe.KV("BP-AT-FAR", "after the jump in the CHUNKED area: " + Probe.Chunk("bp-at-far"));
                    Probe.Shot(Shots("bp_at_far"));
                    Next();
                    return;

                case 20:
                    if (!Elapsed(0.4f)) return;
                    {
                        var p = Probe.Player();
                        var killed = Probe.Call0Any(p, "Kill", "Die");
                        Probe.KV("BP-KILL", killed + " isDead=" + (p != null && p.IsDead ? 1 : 0)
                            + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                        if (p == null || !p.IsDead) { Probe.Warn("BP-ABORT the player could not be killed"); Next(); return; }
                        _reviveAt = Time.unscaledTime;
                        Probe.KV("BP-REVIVE-REQUEST", "emit " + Events.ReviveRequest + " (chunked area)");
                        ArmSeries("revive-chunked", 150);
                        Game.Event.Emit(Events.ReviveRequest);
                        Probe.KV("BP-REVIVE-T0", "same frame: " + Probe.Chunk("bp-revive-t0"));
                        Probe.Shot(Shots("bp_revive_t0"));
                        _reviveSamples = 0;
                        Next();
                        return;
                    }

                case 21:
                    {
                        var dt = Time.unscaledTime - _reviveAt;
                        if (_reviveSamples == 0 && dt >= 0.5f)
                        {
                            _reviveSamples = 1;
                            Probe.KV("BP-REVIVE-T0.5", Probe.Chunk("bp-revive+0.5s"));
                            Probe.Shot(Shots("bp_revive_t0p5"));
                            return;
                        }
                        if (_reviveSamples == 1 && dt >= 1.5f)
                        {
                            _reviveSamples = 2;
                            Probe.KV("BP-REVIVE-T1.5", Probe.Chunk("bp-revive+1.5s"));
                            Probe.Shot(Shots("bp_revive_t1p5"));
                            Next();
                            return;
                        }
                        if (dt >= 4f) { Probe.Warn("bp revive sampling timed out"); Next(); }
                        return;
                    }

                // =============== LOAD ENTRY #2: the REAL save -> load path in the CHUNKED area =====
                // Save-and-exit (the player's own menu item) writes areaId=BloodMoor + gridX/Y=the
                // cell we stood on, then we re-enter through CharSelectRequest -- the save now carries
                // an area id AND a landing grid that need not be the area spawn.
                case 22:
                    Probe.KV("LOAD2-SAVE-EXIT", "emit " + Events.SaveAndExitRequest
                        + " (save keeps areaId=" + (Probe.Map() != null ? ((int)Probe.Map().Area).ToString() : "-")
                        + " grid=" + Probe.Grid(Probe.PlayerGrid()) + ")");
                    _t0 = Time.unscaledTime;
                    Game.Event.Emit(Events.SaveAndExitRequest);
                    Next();
                    return;

                case 23:
                    if (!MenuOpen())
                    {
                        if (Time.unscaledTime - _t0 >= 30f) { Probe.Warn("LOAD2 menu timeout fsm=" + Fsm()); Next(); }
                        return;
                    }
                    Probe.KV("LOAD2-MENU", "back at the main menu (save written), re-entering through "
                        + Events.CharSelectRequest);
                    Next();
                    return;

                case 24:
                    if (!Elapsed(0.6f)) return;
                    Probe.KV("ENTRY-LOAD2", "emit " + Events.CharSelectRequest + "(\"" + _save + "\")"
                        + " => GoStage(ToArea(save.areaId)) with a SAVED area + SAVED grid");
                    _entryAt = Time.unscaledTime;
                    _loadingSeen = false;
                    Game.Event.Emit<string>(Events.CharSelectRequest, _save);
                    Next();
                    return;

                case 25:
                    if (LoadingOpen()) _loadingSeen = true;
                    if (!_loadingSeen) { Tout("loading2-wait", 45f); return; }
                    if (LoadingOpen()) return;
                    Probe.KV("ENTRY-LOAD2-FIRSTFRAME", "loading closed after "
                        + (Time.unscaledTime - _entryAt).ToString("0.00") + "s fsm=" + Fsm()
                        + " " + Probe.Chunk("load2-firstframe"));
                    Probe.Shot(Shots("load2_t0"));
                    ArmSeries("load2", 150);
                    _entrySamples = 0;
                    Next();
                    return;

                case 26:
                    {
                        var dt = Time.unscaledTime - _entryAt;
                        if (_entrySamples == 0 && dt >= 0.5f)
                        {
                            _entrySamples = 1;
                            Probe.KV("ENTRY-LOAD2-T0.5", Probe.Chunk("load2+0.5s"));
                            Probe.Shot(Shots("load2_t0p5"));
                            return;
                        }
                        if (_entrySamples == 1 && dt >= 1.5f)
                        {
                            _entrySamples = 2;
                            Probe.KV("ENTRY-LOAD2-T1.5", Probe.Chunk("load2+1.5s"));
                            Probe.Shot(Shots("load2_t1p5"));
                            Next();
                            return;
                        }
                        if (dt >= 5f) { Probe.Warn("load2 sampling timed out"); Next(); }
                        return;
                    }

                case 27:
                    Probe.KV("SUMMARY", "final=" + Probe.Chunk("final") + " | isDead="
                        + (Probe.Player() != null && Probe.Player().IsDead ? 1 : 0));
                    Probe.Log("VERDICT done invalidDevice=" + (_invalid ? 1 : 0));
                    Finish("end");
                    return;

                default:
                    Finish("end");
                    return;
            }
        }

        private void Tout(string at, float seconds)
        {
            if (!Elapsed(seconds)) return;
            Probe.KV("STEP-TIMEOUT", "at=" + at + " fsm=" + Fsm() + " panel="
                + (BootOpen() ? "Boot" : MenuOpen() ? "MainMenu" : SelectOpen() ? "CharSelect" : CreateOpen() ? "CharCreate" : HudOpen() ? "Hud" : "(none)"));
            Next();
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Probe.Log("RE-DONE why=" + why + " frame=" + Time.frameCount);
            Probe.WriteFile(Probe.DonePath, "DONE " + why + " tag=" + _tag + " save=" + _save
                + " fsm=" + Fsm() + " t=" + Time.unscaledTime.ToString("0.00"));
        }
    }
}
