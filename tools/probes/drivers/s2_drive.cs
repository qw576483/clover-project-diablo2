// =============================================================================
// u1_drive.cs -- ONE Play session that produces the U-1 runtime verdict
//   (direction-key family does not move / W only swaps the weapon group / a real
//    ground click really walks the character).
//
// WHY THIS FILE EXISTS (promotion of a deleted one-off probe):
//   The 2026-09-22 U-1 recapture (".ai-tmp/screenshots/u1_evidence_recapture.txt")
//   was produced by a one-off probe ".ai-tmp/test/u1_probe.cs" that was deleted
//   afterwards, so the live chain could not be re-run from the repo.  Per
//   clover-engine skill 3.5 a judging asset (probe / driver / measuring script)
//   must live in tools/probes/ and be committed.  This file is that driver; the
//   chain, the log markers and the THREE judged assertions are exactly the ones
//   recorded in the frozen first evidence file (they are NOT re-designed here).
//
// CHAIN (identical to the frozen evidence, one Play session):
//   Boot(Injection Space) -> MainMenu("Single") -> CharSelect("Create")
//   -> CharCreate("Back") -> CharSelect -> Events.CharSelectRequest(<save>)
//   -> Loading -> Stage, then
//     (1) hold the direction-key family W+A+S+D for 1.5s  => 0 displacement
//     (2) press W                                           => swap weapon group, 0 displacement
//     (3) real mouse click on a ground cell                 => the character walks there
//
// ENVIRONMENT SELF-CHECK (skill 4.5): the driver logs the render device name,
// graphics device type, resolution, frame pacing, the module wiring and the Unity
// version, and it ABORTS (writes a done marker that says INVALID) when the render
// device is a software raster (Microsoft Basic Render Driver / WARP) -- on such a
// device every frame-time / pacing verdict is void.  recompile_status and the
// console error count are CLI-level, so u1_run.ps1 logs those two into the same
// frozen evidence header.
//
// NUMERIC CLASS: the judged rows are runtime logs + assertions, so this driver
// captures NO tiles (skill 4.9: numeric rows are not screenshot rows).
//
// Input is injected as REAL InputSystem events (QueueStateEvent) so the whole
// chain runs: InputSystem -> InputSystemUIInputModule(uGUI) / CloverInput ->
// InputReader -> Player/Item.  The ground click additionally verifies the
// screen<->grid round trip before it clicks (a missed projection would make the
// click land on the wrong cell and the assertion would be meaningless).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
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

namespace S2
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Probe
    {
        internal const string Tag = "S2";

        private static string _done = string.Empty;

        internal static string DonePath { get { return _done; } }

        private static readonly List<string> Checks = new List<string>();

        // ---- logging -----------------------------------------------------------------------
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

        internal static bool AllChecksOk() { return Checks.Count > 0 && ChecksBad() == 0; }

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

        internal static Diablo2.Module.IItemModule Item()
        {
            return CtxMember("Item") as Diablo2.Module.IItemModule;
        }

        /// <summary>One-line inventory snapshot: anchors / occupied cells / ground / gold + every anchor.</summary>
        internal static string InvLine(string where)
        {
            var it = Item();
            var inv = it != null ? it.Inventory : null;
            var anchors = 0;
            var occupied = 0;
            var sb = new StringBuilder();
            if (inv != null)
            {
                for (var i = 0; i < inv.Count; i++)
                {
                    var s = inv[i];
                    if (s == null) continue;
                    if (s.occupied) occupied++;
                    if (s.item != null)
                    {
                        anchors++;
                        if (sb.Length > 0) sb.Append(", ");
                        sb.Append(s.index).Append(':').Append(s.item.name)
                          .Append('(').Append(s.item.gridW).Append('x').Append(s.item.gridH).Append(')');
                    }
                }
            }
            return "where=" + where + " anchors=" + anchors + " occupied=" + occupied
                   + " ground=" + (it != null ? it.GroundItems.Count : -1)
                   + " gold=" + (it != null ? it.Gold : -1)
                   + " items=[" + sb + "]";
        }

        /// <summary>Anchor index of the n-th occupied anchor (-1 = none).</summary>
        internal static int NthAnchor(int n)
        {
            var it = Item();
            var inv = it != null ? it.Inventory : null;
            if (inv == null) return -1;
            var k = 0;
            for (var i = 0; i < inv.Count; i++)
            {
                var s = inv[i];
                if (s == null || s.item == null) continue;
                if (k == n) return s.index;
                k++;
            }
            return -1;
        }

        /// <summary>First entirely free cell index whose 1x1 block is free (-1 = none).</summary>
        internal static int FirstFreeCell(int avoid)
        {
            var it = Item();
            var inv = it != null ? it.Inventory : null;
            if (inv == null) return -1;
            for (var i = 0; i < inv.Count; i++)
            {
                if (i == avoid) continue;
                var s = inv[i];
                if (s == null || s.occupied) continue;
                return i;
            }
            return -1;
        }

        internal static int AreaOfMap()
        {
            var m = Map();
            return m != null ? (int)m.Area : -1;
        }

        internal static int AnchorCount()
        {
            var it = Item();
            var inv = it != null ? it.Inventory : null;
            if (inv == null) return -1;
            var n = 0;
            for (var i = 0; i < inv.Count; i++) { var s = inv[i]; if (s != null && s.item != null) n++; }
            return n;
        }

        internal static int GroundCount()
        {
            var it = Item();
            return it != null ? it.GroundItems.Count : -1;
        }

        /// <summary>What sits at a cell index: the item name / "(occupied)" / "(empty)" / "-".</summary>
        internal static string AtAnchor(int idx)
        {
            var it = Item();
            var inv = it != null ? it.Inventory : null;
            if (inv == null || idx < 0 || idx >= inv.Count) return "-";
            var s = inv[idx];
            if (s == null) return "-";
            if (s.item != null) return s.item.name;
            return s.occupied ? "(occupied)" : "(empty)";
        }

        internal static string WaypointLine()
        {
            var m = Map();
            if (m == null) return "(no-map)";
            var pts = m.WaypointPoints;
            var sb = new StringBuilder();
            if (pts != null)
            {
                for (var i = 0; i < pts.Count; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(Grid(pts[i]));
                }
            }
            return "area=" + (int)m.Area + " points=" + (pts != null ? pts.Count : -1) + " [" + sb + "]";
        }

        internal static string ExitLine()
        {
            var m = Map();
            if (m == null) return "(no-map)";
            var sb = new StringBuilder();
            var ex = m.Exits;
            if (ex != null)
            {
                for (var i = 0; i < ex.Count; i++)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(Grid(ex[i]));
                }
            }
            return "area=" + (int)m.Area + " exits=" + (ex != null ? ex.Count : -1) + " [" + sb + "]"
                   + " cave=" + (m.CaveEntrance.HasValue ? Grid(m.CaveEntrance.Value) : "(none)");
        }

        /// <summary>First exit cell that is not the cave entrance (for BloodMoor: that one goes back to Town).</summary>
        internal static Vector2Int BackExit()
        {
            var m = Map();
            if (m == null) return new Vector2Int(-1, -1);
            var ex = m.Exits;
            if (ex == null) return new Vector2Int(-1, -1);
            for (var i = 0; i < ex.Count; i++)
            {
                if (m.CaveEntrance.HasValue && m.CaveEntrance.Value == ex[i]) continue;
                return ex[i];
            }
            return new Vector2Int(-1, -1);
        }

        internal static Vector2Int FirstExit()
        {
            var m = Map();
            if (m == null) return new Vector2Int(-1, -1);
            var ex = m.Exits;
            if (ex == null || ex.Count == 0) return new Vector2Int(-1, -1);
            return ex[0];
        }

        internal static Vector2Int FirstWaypoint()
        {
            var m = Map();
            if (m == null) return new Vector2Int(-1, -1);
            var pts = m.WaypointPoints;
            if (pts == null || pts.Count == 0) return new Vector2Int(-1, -1);
            return pts[0];
        }

        internal static Vector2Int PlayerGrid()
        {
            return GridOf(Player());
        }

        internal static string PanelRectLine()
        {
            var p = FindOpenInventory();
            Vector2 lo, hi;
            if (!PanelRect(p, out lo, out hi)) return "(no-rect)";
            return "inventoryPanelRect screen=" + V(lo) + ".." + V(hi) + " screenSize=" + Screen.width + "x" + Screen.height;
        }

        // ---- the waypoint panel's live open-args (reflection; the panel keeps them in `_current`) ----
        private static Diablo2.UI.WaypointPanel FindWaypointPanel()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<Diablo2.UI.WaypointPanel>(FindObjectsSortMode.None))
            {
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        private static System.Collections.IList DestList()
        {
            var panel = FindWaypointPanel();
            if (panel == null) return null;
            var f = panel.GetType().GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance);
            var args = f != null ? f.GetValue(panel) : null;
            if (args == null) return null;
            var df = args.GetType().GetField("dests", BindingFlags.Public | BindingFlags.Instance);
            return df != null ? df.GetValue(args) as System.Collections.IList : null;
        }

        internal static int DestCount()
        {
            var l = DestList();
            return l != null ? l.Count : -1;
        }

        internal static string DestLabels()
        {
            var l = DestList();
            if (l == null) return "(no-list)";
            var sb = new StringBuilder();
            for (var i = 0; i < l.Count; i++)
            {
                var d = l[i];
                if (d == null) continue;
                var nf = d.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(nf != null ? (string)nf.GetValue(d) : "?");
            }
            return "[" + sb + "]";
        }

        internal static int DestArea0()
        {
            var l = DestList();
            if (l == null || l.Count == 0) return -1;
            var af = l[0].GetType().GetField("area", BindingFlags.Public | BindingFlags.Instance);
            return af != null ? (int)af.GetValue(l[0]) : -1;
        }

        /// <summary>`App/AppWaypoint`'s private static "already visited" set (the list's source of truth).</summary>
        private static System.Collections.IEnumerable VisitedSet()
        {
            var t = FindType("Diablo2.App.AppWaypoint");
            if (t == null) return null;
            var f = t.GetField("Visited", BindingFlags.NonPublic | BindingFlags.Static);
            return f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
        }

        internal static string VisitedLine()
        {
            var set = VisitedSet();
            if (set == null) return "(no-set)";
            var sb = new StringBuilder();
            foreach (var v in set)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append((int)v);
            }
            return "[" + sb + "]";
        }

        internal static int VisitedListCount()
        {
            var set = VisitedSet();
            if (set == null) return -1;
            var n = 0;
            foreach (var v in set) n++;
            return n;
        }

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

        // ---- panels ------------------------------------------------------------------------
        internal static bool PanelOpen(string typeName)
        {
            var t = FindType(typeName);
            if (t == null || Game.UI == null) return false;
            var m = Game.UI.GetType().GetMethod("IsOpen", new Type[0]);
            if (m == null) return false;
            var gm = m.MakeGenericMethod(t);
            return (bool)gm.Invoke(Game.UI, null);
        }

        internal static Diablo2.UI.InventoryPanel FindOpenInventory()
        {
            foreach (var p in UnityEngine.Object.FindObjectsByType<Diablo2.UI.InventoryPanel>(FindObjectsSortMode.None))
            {
                if (p != null && p.gameObject.activeInHierarchy) return p;
            }
            return null;
        }

        /// <summary>Screen centre of inventory cell <paramref name="cell"/> (reflection into `_cells`).</summary>
        internal static bool CellScreen(Diablo2.UI.InventoryPanel panel, int cell, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (panel == null) return false;
            var f = panel.GetType().GetField("_cells", BindingFlags.NonPublic | BindingFlags.Instance);
            var arr = f != null ? f.GetValue(panel) as Array : null;
            if (arr == null || cell < 0 || cell >= arr.Length) return false;
            var el = arr.GetValue(cell);
            if (el == null) return false;
            // Graphic.rectTransform is a PROPERTY (not a field) -- GetField would silently return null
            var rt = el.GetType().GetProperty("rectTransform", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var rtf = rt != null ? rt.GetValue(el) as RectTransform : null;
            if (rtf == null)
            {
                var g = el as Graphic;
                rtf = g != null ? g.rectTransform : null;
            }
            if (rtf == null) return false;
            pos = RectTransformUtility.WorldToScreenPoint(null, rtf.TransformPoint(rtf.rect.center));
            return true;
        }

        /// <summary>
        /// True when the screen point is inside the panel rect.  Same predicate as the panel's own
        /// `InsidePanel(Vector2)` => `RectangleContainsScreenPoint((RectTransform)transform, ...)`.
        /// </summary>
        internal static bool InsidePanel(Diablo2.UI.InventoryPanel panel, Vector2 screen)
        {
            if (panel == null) return false;
            var rt = panel.transform as RectTransform;
            if (rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(rt, screen, null);
        }

        /// <summary>Panel rect in screen space (for aiming an "outside the panel" drop).</summary>
        internal static bool PanelRect(Diablo2.UI.InventoryPanel panel, out Vector2 lo, out Vector2 hi)
        {
            lo = Vector2.zero; hi = Vector2.zero;
            var rt = panel != null ? panel.transform as RectTransform : null;
            if (rt == null) return false;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var a = RectTransformUtility.WorldToScreenPoint(null, c[0]);
            var b = RectTransformUtility.WorldToScreenPoint(null, c[2]);
            lo = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
            hi = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
            return true;
        }

        // ---- S2: black-background (U41/U45) chunked-rendering readings -----------------------
        // Why: Blood Moor (80x80 = 6400 > BuildAllTileThreshold 4096) is the ONLY area that goes
        // through the chunked path; Town (2240) does not.  These readings separate
        //   (a) "expected chunk never built"  (b) "built but not visible"  (c) "released by mistake".
        // Everything is read through reflection -- no product code is touched.
        private static object ViewOfMap()
        {
            var map = Map();
            if (map == null) return null;
            var f = map.GetType().GetField("_view", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(map) : null;
        }

        private static object ViewField(object view, string name)
        {
            if (view == null) return null;
            var f = view.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(view) : null;
        }

        /// <summary>Chunk keys actually built (ground layer dictionary = the layer every non-Void cell has).</summary>
        private static List<Vector2Int> BuiltChunks(object view)
        {
            var res = new List<Vector2Int>();
            var d = ViewField(view, "_groundChunks") as System.Collections.IDictionary;
            if (d == null) return res;
            foreach (var k in d.Keys) { if (k is Vector2Int v) res.Add(v); }
            return res;
        }

        /// <summary>
        /// Expected visible chunk range -- the SAME formula as `MapView.ComputeVisibleChunkRange`
        /// (screen 4 corners -> Iso.ScreenToGrid -> min/max -> +-1 chunk), so "expected vs built"
        /// is an apples-to-apples comparison.
        /// </summary>
        private static bool ExpectedChunkRange(out Vector2Int emin, out Vector2Int emax, out string diag)
        {
            emin = Vector2Int.zero; emax = Vector2Int.zero; diag = "(no-map)";
            var map = Map();
            var cam = Camera.main;
            if (map == null) return false;
            var cx = (map.Width + 15) / 16;
            var cy = (map.Height + 15) / 16;
            if (cam == null) { diag = "(no-camera)"; return false; }

            var minX = int.MaxValue; var minY = int.MaxValue;
            var maxX = int.MinValue; var maxY = int.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? 0f : Screen.width;
                var sy = (i & 2) == 0 ? 0f : Screen.height;
                var g = Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
                maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
            }
            emin = new Vector2Int(Mathf.Clamp(minX / 16 - 1, 0, cx - 1), Mathf.Clamp(minY / 16 - 1, 0, cy - 1));
            emax = new Vector2Int(Mathf.Clamp(maxX / 16 + 1, 0, cx - 1), Mathf.Clamp(maxY / 16 + 1, 0, cy - 1));
            diag = "chunks=" + cx + "x" + cy + " gridCorners=[" + minX + "," + minY + ".." + maxX + "," + maxY + "]";
            return true;
        }

        internal static string ChunkState(string where)
        {
            var map = Map();
            var view = ViewOfMap();
            if (view == null) return "where=" + where + " _view=(null)";
            var vt = view.GetType();
            var chunked = ViewField(view, "_chunked");
            var hasRange = ViewField(view, "_hasChunkRange");
            var cmin = ViewField(view, "_chunkMin");
            var cmax = ViewField(view, "_chunkMax");
            var built = BuiltChunks(view);
            var pt = vt.GetProperty("PendingChunkCount");
            var rt = vt.GetProperty("PendingRetireChunks");
            var bt = vt.GetProperty("BuiltChunkCount");
            var reb = vt.GetProperty("RebuildInProgress");
            var og = ViewField(view, "_objectChunks") as System.Collections.IDictionary;
            var ov = ViewField(view, "_overlayChunks") as System.Collections.IDictionary;
            return "where=" + where
                + " area=" + AreaOfMap() + " map=" + map.Width + "x" + map.Height
                + " chunked=" + (chunked is bool && (bool)chunked ? 1 : 0)
                + " hasChunkRange=" + (hasRange is bool && (bool)hasRange ? 1 : 0)
                + " chunkMin=" + cmin + " chunkMax=" + cmax
                + " builtGround=" + built.Count
                + " builtObject=" + (og != null ? og.Count : -1)
                + " builtOverlay=" + (ov != null ? ov.Count : -1)
                + " BuiltChunkCount=" + (bt != null ? bt.GetValue(view) : "-")
                + " PendingChunks=" + (pt != null ? pt.GetValue(view) : "-")
                + " PendingRetire=" + (rt != null ? rt.GetValue(view) : "-")
                + " rebuildInProgress=" + (reb != null ? (reb.GetValue(view) != null && (bool)reb.GetValue(view) ? 1 : 0) : "-");
        }

        /// <summary>Chunks inside the expected visible range that were NEVER built (= the black cells).</summary>
        internal static string ChunkMiss(string where)
        {
            var view = ViewOfMap();
            if (view == null) return "where=" + where + " _view=(null)";
            Vector2Int e0, e1; string diag;
            if (!ExpectedChunkRange(out e0, out e1, out diag)) return "where=" + where + " " + diag;
            var built = BuiltChunks(view);
            var sb = new StringBuilder();
            var n = 0;
            for (var cx = e0.x; cx <= e1.x; cx++)
            {
                for (var cy = e0.y; cy <= e1.y; cy++)
                {
                    var c = new Vector2Int(cx, cy);
                    var found = false;
                    for (var i = 0; i < built.Count; i++) { if (built[i] == c) { found = true; break; } }
                    if (found) continue;
                    n++;
                    if (sb.Length < 400) { if (sb.Length > 0) sb.Append(' '); sb.Append("(" + cx + "," + cy + ")"); }
                }
            }
            var total = (e1.x - e0.x + 1) * (e1.y - e0.y + 1);
            return "where=" + where + " expected=" + e0 + ".." + e1 + " chunks=" + total
                + " builtInRange=" + (total - n) + " MISSING=" + n + " [" + sb + "]"
                + " player=" + Grid(PlayerGrid()) + " " + diag;
        }

        // ---- composited screenshots (same recipe as byline_drive) ---------------------------
        private static string _rawDir = string.Empty;
        private static string _pending = string.Empty;
        private static int _pendingN;

        internal static void RawDir(string dir) { _rawDir = dir ?? string.Empty; }

        internal static void BeginShot(int n, string name)
        {
            _pendingN = n;
            _pending = _rawDir + "/" + name;
            try { if (File.Exists(_pending)) File.Delete(_pending); } catch { }
            Log("SHOT-REQUEST n=" + n + " name=" + name + " path=" + _pending);
        }

        internal static void FlushShot()
        {
            if (_pending.Length == 0) return;
            var file = _pending;
            var n = _pendingN;
            _pending = string.Empty;
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(file);
                Log("SHOT-ISSUED n=" + n + " file=" + file + " frame=" + Time.frameCount);
            }
            catch (Exception ex) { Warn("SHOT-FAIL n=" + n + " " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static bool ShotReady(string name, out long bytes)
        {
            var f = _rawDir + "/" + name;
            bytes = -1;
            try { if (File.Exists(f)) bytes = new FileInfo(f).Length; } catch { }
            return bytes > 0;
        }

        /// <summary>Module wiring snapshot (log only; a null module is not a failure by itself).</summary>
        internal static string ModuleLine()
        {
            var p = CtxMember("Player");
            var m = CtxMember("Map");
            return "Map=" + (m != null ? 1 : 0) + " Player=" + (p != null ? 1 : 0);
        }

        internal static Vector2Int GridOf(Diablo2.Module.IPlayerModule p)
        {
            return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);
        }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string V(Vector2 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")"; }

        internal static int Chebyshev(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        internal static string DeviceName() { return SystemInfo.graphicsDeviceName; }

        /// <summary>Software rasterisation (every pacing / frame-time verdict on it is void).</summary>
        internal static bool SoftwareRaster(string device)
        {
            var d = device ?? string.Empty;
            return d.IndexOf("Microsoft Basic Render Driver", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0
                || d.IndexOf("SwiftShader", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- editor / play setup (skill 4.5 environment self-check) -------------------------
        internal static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

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
                + " keyboard=" + (kb != null ? kb.name : "(null)")
                + " mouse=" + (UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.name : "(null)")
                + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)"));
            Log("DEVICE device=\"" + DeviceName() + "\" res=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFps=" + Application.targetFrameRate);
            Log("MODULES=" + ModuleLine());
            return "CFG-OK";
        }

        // ---- keyboard ----------------------------------------------------------------------
        internal static bool TryParseKey(string name, out Key key)
        {
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "w": key = Key.W; return true;
                case "a": key = Key.A; return true;
                case "s": key = Key.S; return true;
                case "d": key = Key.D; return true;
                case "i": key = Key.I; return true;
                case "space": key = Key.Space; return true;
                case "escape": key = Key.Escape; return true;
                case "enter": key = Key.Enter; return true;
                default: key = Key.None; return false;
            }
        }

        private static Keyboard KeyboardDev()
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }

        /// <summary>Press several keys at once (the direction-key family is held simultaneously).</summary>
        internal static void KeysDown(string[] names)
        {
            var kb = KeyboardDev();
            if (kb == null) { Warn("KEYSDOWN no-keyboard"); return; }
            var keys = new List<Key>();
            foreach (var n in names)
            {
                Key k;
                if (TryParseKey(n, out k)) keys.Add(k);
                else Warn("KEYSDOWN unknown-key=" + n);
            }
            if (keys.Count == 0) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState(keys.ToArray()));
            Log("KEYSDOWN keys=" + string.Join("+", names) + " keyboard=" + kb.name);
        }

        internal static void KeyDown(string name) { KeysDown(new[] { name }); }

        internal static void KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
        }

        // ---- mouse -------------------------------------------------------------------------
        internal static UnityEngine.InputSystem.Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null)
            {
                m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
                Warn("mouse device missing -> added");
            }
            return m;
        }

        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new MouseState { position = pos };
            if (leftDown) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, st);
        }

        internal static Vector3 MousePosNow()
        {
            var input = Game.Input;
            return input != null ? input.MousePosition : Vector3.zero;
        }

        // ---- UI ----------------------------------------------------------------------------
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
            catch (Exception e)
            {
                Warn("SCREEN-RECT-FAIL " + e.GetType().Name + ": " + e.Message);
                return false;
            }
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

        internal static string ButtonLabel(Button b)
        {
            if (b == null) return "-";
            var t = b.GetComponentInChildren<Text>(true);
            return t != null ? t.text : "-";
        }

        internal static string RaycastTop(Vector2 screenPos)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current);
            ped.position = screenPos;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            var g = hits[0].gameObject;
            return g == null ? "(null)" : g.name;
        }

        /// <summary>Legacy click path (ExecuteEvents). Used only as a retry when the real mouse misses.</summary>
        internal static string Click(string name)
        {
            if (EventSystem.current == null) { Warn("CLICK-LEGACY no Eventsystem name=" + name); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(name, out diag);
            if (pick == null) { Warn("CLICK-LEGACY miss name=" + name + " " + diag); return "ERR-miss"; }
            if (!pick.interactable) { Warn("CLICK-LEGACY skip name=" + name + " (not interactable)"); return "ERR-not-interactable"; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            ExecuteEvents.Execute(pick.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-LEGACY name=" + name + " label=\"" + ButtonLabel(pick) + "\" " + diag);
            return "CLICKED";
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Cfg() { return Probe.Cfg(); }
        public static string Paths(string spec) { Probe.Paths(spec); return "PATHS-OK"; }
        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0); }
    }

    /// <summary>Installer. spec = "&lt;tag&gt;|&lt;save name&gt;|&lt;done marker path&gt;|&lt;raw screenshot dir&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("S2EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One Play session: environment self-check -> the menu chain -> the three U-1 assertions.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private const float HoldSeconds = 1.5f;      // the direction-key family hold (frozen evidence)
        private const float SampleStep = 0.1f;       // one sample per 100ms => 15 samples
        private const float ArriveBudget = 10f;      // the click-to-move assertion budget
        private static readonly string[] DirKeys = new[] { "w", "a", "s", "d" };

        private string _tag = "u1";
        private string _save = "g66";
        private bool _classPicked;
        private int _step;
        private float _at;
        private bool _done;
        private float _trigAt;
        private bool _invalidDevice;
        private bool _bootSpaceSent;
        private bool _bootSpaceUp;
        private float _bootSpaceAt;

        // ---- direction-key hold sampling -------------------------------------------------
        private float _holdAt;
        private float _nextSample;
        private readonly List<Vector2Int> _kGrids = new List<Vector2Int>();
        private readonly List<bool> _kMoving = new List<bool>();
        private Vector2Int _kStart;
        private int _kMoved;

        // ---- event counters --------------------------------------------------------------
        private int _moveCmds;
        private int _swapReqs;

        // ---- W step ----------------------------------------------------------------------
        private int _wSwapBefore;
        private int _wMoveBefore;
        private Vector2Int _wGridBefore;
        private int _wSwapDelta;
        private int _wMoveDelta;
        private int _wMoved;

        // ---- ground click ----------------------------------------------------------------
        private int _ckPhase;
        private int _ckFrames;
        private Vector2 _ckPos;
        private Vector2Int _ckWant;
        private Vector2Int _ckFrom;
        private int _ckFound;
        private int _ckMoveBefore = -1;
        private bool _ckFlipTried;
        private bool _ckFlipY;
        private int _ckMoveDelta;
        private int _ckMoved;
        private int _ckArrived;
        private int _cMovedFinal = -1;
        private int _cMoveCmdsFinal = -1;

        // ---- UI click sequencer ----------------------------------------------------------
        private Vector2 _pendingUiClick;
        private string _pendingUiClickName = string.Empty;
        private int _uiClickPhase;
        private int _uiClickFrames;

        // ---- flow ------------------------------------------------------------------------
        private string _lastPanel = "(null)";
        private bool _legacyRetried;
        private bool _sawLoading;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            if (parts.Length > 1 && parts[1].Length > 0) _save = parts[1];
            var donePath = parts.Length > 2 ? parts[2] : string.Empty;
            var rawDir = parts.Length > 3 ? parts[3] : string.Empty;
            Probe.Paths(donePath);
            Probe.RawDir(rawDir);
            _step = 0;
            _at = Time.unscaledTime;
            Probe.Log("PROBE-INSTALL done=" + donePath + " rawDir=" + rawDir + " frame=" + Time.frameCount);
            Probe.Log("DRIVER-INIT tag=" + _tag + " save=\"" + _save + "\" frame=" + Time.frameCount);
        }

        private void Update()
        {
            if (_done) return;
            try
            {
                TickUiClick();
                Step();
            }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void LateUpdate()
        {
            try { Probe.FlushShot(); }
            catch (Exception ex) { Probe.Warn("FLUSH-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- S2: pending drag / wait helpers ------------------------------------------------
        private sealed class Gesture
        {
            public string tag;
            public int from;
            public int to;
            public int kind;   // 0 = to an empty cell, 1 = onto another item (swap), 2 = outside the panel (ground)
        }

        private readonly List<Gesture> _plan = new List<Gesture>();
        private Gesture _cur;
        private readonly string[] _shots = new string[8];
        private int _gi;
        private int _ph;
        private int _beforeAnchors = -1;
        private int _beforeGround = -1;
        private string _beforeAtFrom = "-";
        private string _beforeAtTo = "-";
        private int _dragFrom = -1;
        private int _dragTo = -1;
        private Vector2 _dragPos;
        private int _dragPhase;
        private int _dragFrames;
        private string _dragTag = string.Empty;
        private Vector2 _dropPos;
        private bool _dragOutside;
        private int _invBaseAnchors = -1;
        private int _invBaseGround = -1;
        private int _area0 = -1;
        private Vector2Int _wpGrid = new Vector2Int(-1, -1);
        private int _travelDest = -1;
        private float _t0;
        private float _chunkAt;

        private bool WaitShot(string name, float budget)
        {
            long bytes;
            if (Probe.ShotReady(name, out bytes))
            {
                Probe.KV("SHOT-OK", "name=" + name + " bytes=" + bytes);
                return true;
            }
            if (Elapsed(budget))
            {
                Probe.Warn("SHOT-TIMEOUT name=" + name + " waited=" + budget.ToString("0.0") + "s");
                return true;
            }
            return false;
        }

        /// <summary>0 = still running, 1 = the gesture finished (assert afterwards), -1 = aborted.</summary>
        private int TickDrag()
        {
            _dragFrames++;
            switch (_dragPhase)
            {
                case 0:   // pointer onto the source cell (no button)
                    Probe.MouseState(_dragPos, false);
                    _dragPhase = 1;
                    _dragFrames = 0;
                    return 0;
                case 1:   // press
                    if (_dragFrames < 3) return 0;
                    Probe.MouseState(_dragPos, true);
                    _dragPhase = 2;
                    _dragFrames = 0;
                    return 0;
                case 2:
                    // Crossing uGUI's drag threshold is what fires OnBeginDrag, and the panel grabs
                    // `CellAt(pointer at that moment)` -- so the FIRST move must still be inside the
                    // SOURCE cell (12 px < 29.2 px cell step).  Run s2d used a 50% lerp here and the
                    // panel therefore grabbed cell 3 (log: "开始拖拽背包物品（锚点格 3..") instead of 0,
                    // and for the outside-the-panel gesture it crossed the threshold at (468,494) --
                    // already outside every cell ("从非背包格开始拖拽") => the gesture never started.
                    if (_dragFrames < 3) return 0;
                    Probe.MouseState(_dragPos + new Vector2(12f, 0f), true);
                    _dragPhase = 3;
                    _dragFrames = 0;
                    return 0;
                case 3:
                    if (_dragFrames < 3) return 0;
                    Probe.MouseState(_dropPos, true);
                    _dragPhase = 4;
                    _dragFrames = 0;
                    return 0;
                case 4:
                    if (_dragFrames < 3) return 0;
                    Probe.KV("DRAG-MOVE", "tag=" + _dragTag + " from=" + _dragFrom + " to=" + _dragTo
                        + " fromPos=" + Probe.V(_dragPos) + " dropPos=" + Probe.V(_dropPos)
                        + " outside=" + (_dragOutside ? 1 : 0) + " frame=" + Time.frameCount);
                    Probe.MouseState(_dropPos, false);   // release
                    _dragPhase = 5;
                    _dragFrames = 0;
                    return 0;
                case 5:
                    if (_dragFrames < 3) return 0;
                    return 1;
                default:
                    return 1;
            }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        // ================================================================ plumbing ==========
        private bool Elapsed(float seconds) { return Time.unscaledTime - _at >= seconds; }
        private void Next() { _step++; _at = Time.unscaledTime; _legacyRetried = false; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }

        private string Panel()
        {
            if (BootOpen()) return "Boot";
            if (MenuOpen()) return "MainMenu";
            if (SelectOpen()) return "CharSelect";
            if (CreateOpen()) return "CharCreate";
            if (HudOpen()) return "Hud";
            return "(none)";
        }

        private void Reached(string panel)
        {
            var dt = Time.unscaledTime - _trigAt;
            Probe.KV("FLOW", _lastPanel + "->" + panel + " ok after " + dt.ToString("0.00") + "s fsm=" + Fsm());
            _lastPanel = panel;
            Next();
        }

        /// <summary>Note a timeout and advance (never abort -- the assertions decide).</summary>
        private bool Tout(string at, float seconds)
        {
            if (!Elapsed(seconds)) return false;
            Probe.KV("STEP-TIMEOUT", "at=" + at + " elapsed=" + (Time.unscaledTime - _at).ToString("0.0")
                + " fsm=" + Fsm() + " scene=" + SceneName() + " panel=" + Panel());
            Next();
            return true;
        }

        /// <summary>
        /// Wait for a panel; on the SECOND timeout retry the trigger once through the legacy
        /// ExecuteEvents path (the UI navigation is not the judged item here -- the real-mouse
        /// delivery is judged on the ground click, where a miss is reported, not retried away).
        /// </summary>
        private void WaitPanel(string at, float seconds, string retryButton)
        {
            if (Elapsed(seconds) && retryButton.Length > 0 && !_legacyRetried)
            {
                Probe.Warn("FLOW-RETRY " + at + ": the target panel never opened -> legacy click \"" + retryButton + "\"");
                _legacyRetried = true;
                Probe.Click(retryButton);
                _at = Time.unscaledTime;
                return;
            }
            Tout(at, seconds);
        }

        // ================================================================ UI clicks ========
        /// <summary>Move -> down (2 frames) -> up (2 frames); same shape the sibling drivers use.</summary>
        private void QueueUiClick(Vector2 pos, string nodeName)
        {
            _pendingUiClick = pos;
            _pendingUiClickName = nodeName;
            Probe.MouseState(pos, false);
            _uiClickPhase = 1;
            _uiClickFrames = 2;
        }

        private void TickUiClick()
        {
            if (_uiClickPhase == 0) return;
            _uiClickFrames--;
            if (_uiClickFrames > 0) return;
            if (_uiClickPhase == 1)
            {
                Probe.MouseState(_pendingUiClick, true);
                Probe.KV("UI-DOWN", "node=" + _pendingUiClickName + " pos=" + Probe.V(_pendingUiClick)
                    + " frame=" + Time.frameCount);
                _uiClickPhase = 2;
                _uiClickFrames = 2;
                return;
            }
            Probe.MouseState(_pendingUiClick, false);
            Probe.KV("UI-UP", "node=" + _pendingUiClickName + " pos=" + Probe.V(_pendingUiClick)
                + " frame=" + Time.frameCount);
            _uiClickPhase = 0;
            _uiClickFrames = 0;
        }

        /// <summary>Real-mouse click on a Button found by GameObject name. false => legacy path was used.</summary>
        private bool ClickButton(string name, string tag)
        {
            string diag;
            var b = Probe.FindButton(name, out diag);
            if (b == null)
            {
                Probe.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Probe.Click(name);
                return false;
            }
            Vector2 lo, hi, c;
            var has = Probe.ScreenRect(b.transform as RectTransform, out lo, out hi, out c);
            Probe.KV("CLICKTOP", "tag=" + tag + " name=" + name
                + " label=\"" + Probe.ButtonLabel(b) + "\""
                + " interactable=" + (b.interactable ? 1 : 0)
                + " screen=" + Probe.V(c) + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0")
                + " raycastTop=\"" + Probe.RaycastTop(c) + "\" " + diag);
            if (!has) { Probe.Warn("UI-RECT-MISS name=" + name + " -> legacy path"); Probe.Click(name); return false; }
            QueueUiClick(c, name);
            return true;
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
                            + " isPlaying=" + (Application.isPlaying ? 1 : 0)
                            + " batchMode=" + (Application.isBatchMode ? 1 : 0)
                            + " runInBg=" + (Application.runInBackground ? 1 : 0)
                            + " vSync=" + QualitySettings.vSyncCount
                            + " targetFps=" + Application.targetFrameRate);
                        if (Probe.SoftwareRaster(device))
                        {
                            _invalidDevice = true;
                            Probe.Warn("DEVICE-SOFTWARE-RASTER device=\"" + device + "\" => every frame-pacing/"
                                + "rendering verdict on this machine is INVALID; the U-1 chain is not run");
                            Finish("software-raster");
                            return;
                        }
                        Game.Event.On<Vector2Int>(Diablo2.Core.Events.MoveCommand, OnMoveCommand);
                        Game.Event.On(Diablo2.Core.Events.SwapWeaponRequest, OnSwapWeaponRequest);
                        Probe.Log("SUBSCRIBE MoveCommand=1 SwapWeaponRequest=1");
                        Probe.Log("PROBE-BEGIN frame=" + Time.frameCount + " fsm=" + Fsm());
                        _lastPanel = "boot";
                        _trigAt = Time.unscaledTime;
                        Next();
                        return;
                    }

                // ---------------------------------------------------------- 1 boot -> main menu
                // NOTE: the boot panel closes on the key press itself, so "!BootOpen()" after the
                // press means SUCCESS (the main menu comes next) -- it must not be treated as
                // "not ready yet" (that would burn the whole timeout and leave the key held).
                case 1:
                    if (!_bootSpaceSent)
                    {
                        if (!BootOpen()) { Tout("boot-wait", 30f); return; }
                        if (!Elapsed(1.2f)) return;
                        Probe.Log("BOOT injection: real Keyboard Space press fsm=" + Fsm());
                        Probe.KeyDown("space");
                        _bootSpaceSent = true;
                        _bootSpaceAt = Time.unscaledTime;
                        _trigAt = _bootSpaceAt;
                        return;
                    }
                    if (!_bootSpaceUp && Time.unscaledTime - _bootSpaceAt >= 0.25f)
                    {
                        Probe.KeyUp();
                        _bootSpaceUp = true;
                    }
                    if (!BootOpen()) { Next(); return; }            // boot closed => main menu is next
                    if (Time.unscaledTime - _bootSpaceAt > 10f)
                    {
                        Probe.Warn("BOOT the panel is still open 10s after the Space press");
                        Next();
                    }
                    return;

                case 2:
                    if (!MenuOpen()) { WaitPanel("mainmenu-wait", 25f, string.Empty); return; }
                    Reached("MainMenu");
                    return;

                // ---------------------------------------------------------- 3 main menu -> select
                case 3:
                    if (!Elapsed(1.0f)) return;
                    ClickButton("Single", "menu");
                    Next();
                    return;

                case 4:
                    if (!SelectOpen()) { WaitPanel("charselect-wait", 25f, "Single"); return; }
                    Reached("CharSelect");
                    return;

                // ---------------------------------------------------------- 5 select -> create
                case 5:
                    if (!Elapsed(0.8f)) return;
                    ClickButton("Create", "select");
                    Next();
                    return;

                case 6:
                    if (!CreateOpen()) { WaitPanel("charcreate-wait", 25f, "Create"); return; }
                    Reached("CharCreate");
                    return;

                // ---------------------------------------------------------- 7 create a character (s2d)
                // WHY (2026-09-23, run s2d): the roster was EMPTY -- `Event.CharSelectRequest("g66")`
                // was answered with "[Save] 没有角色「g66」的存档" + "[Flow] 选角失败" (Editor.log
                // 17:03:11.745/17:03:11.747) and the flow stayed on CharSelect for the whole 45s.
                // So the driver must CREATE one first: pick a class through the panel's own handler
                // (the class hotspots are Images, not Buttons => a by-name mouse click cannot reach
                // them) and then press the REAL "Confirm" button, which emits the fully built
                // `Def.CharacterSave` exactly like the player would.
                case 7:
                    if (!Elapsed(0.9f)) return;
                    if (_classPicked)
                    {
                        ClickButton("Confirm", "create");
                        Next();
                        return;
                    }
                    {
                        Diablo2.UI.CharCreatePanel panel = null;
                        foreach (var p in UnityEngine.Object.FindObjectsByType<Diablo2.UI.CharCreatePanel>(FindObjectsSortMode.None))
                        {
                            if (p != null && p.gameObject.activeInHierarchy) { panel = p; break; }
                        }
                        if (panel == null)
                        {
                            Probe.Warn("CREATE-PANEL-MISS -> legacy Back click (no character will be created)");
                            ClickButton("Back", "create");
                            Next();
                            return;
                        }
                        // a fresh name per run: an already-taken name makes the create fail ("角色名已存在")
                        _save = "S2" + DateTime.Now.ToString("HHmmss");
                        var nf = panel.GetType().GetField("_nameBuffer",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (nf != null) nf.SetValue(panel, _save);
                        var m = panel.GetType().GetMethod("OnSpotClick",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        Probe.KV("CREATE-CLASS", "name=" + _save
                            + " handler=" + (m != null ? "OnSpotClick(0)" : "(missing)")
                            + " nameBufferSet=" + (nf != null ? 1 : 0));
                        if (m != null) m.Invoke(panel, new object[] { 0 });
                        _classPicked = true;
                        _at = Time.unscaledTime;
                        return;
                    }

                case 8:
                    if (!SelectOpen())
                    {
                        // creation failed (e.g. duplicate name) still lands back on CharSelect; give it 25s
                        WaitPanel("charselect2-wait", 25f, "Back");
                        return;
                    }
                    Reached("CharSelect");
                    return;

                // ---------------------------------------------------------- 9 enter stage
                case 9:
                    if (!Elapsed(0.6f)) return;
                    Probe.KV("ENTER-STAGE", "save=\"" + _save + "\"");
                    Probe.KV("ENTER-STAGE-VIA", "Events.CharSelectRequest from panel=" + Panel());
                    _trigAt = Time.unscaledTime;
                    Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                    Next();
                    return;

                case 10:
                    if (!_sawLoading && LoadingOpen()) _sawLoading = true;
                    if (!(HudOpen() && Fsm() == "Stage")) { Tout("stage-wait", 45f); return; }
                    if (_lastPanel != "Stage")
                    {
                        var dt = Time.unscaledTime - _trigAt;
                        var from = _sawLoading ? "Loading" : _lastPanel;
                        Probe.KV("FLOW", from + "->Stage ok after " + dt.ToString("0.00") + "s fsm=" + Fsm());
                        _lastPanel = "Stage";
                        _at = Time.unscaledTime;
                        return;
                    }
                    if (!Elapsed(0.5f)) return;
                    {
                        var p = Probe.Player();
                        var m = Probe.Map();
                        Probe.KV("STAGE-READY", "area=" + (m != null ? m.Area.ToString() : "(no-map)")
                            + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                            + " grid=" + Probe.Grid(Probe.GridOf(p))
                            + " alive=" + (p != null && !p.IsDead ? 1 : 0)
                            + " timeScale=" + Time.timeScale.ToString("0.##"));
                    }
                    Next();
                    return;

                // ============================================================ S2: inventory drop ===
                // 11 baseline + open the inventory the way the player does (a real "i" key press)
                case 11:
                    {
                        Probe.KV("INV-A0", Probe.InvLine("stage-baseline"));
                        Probe.KV("WP0", Probe.WaypointLine());
                        Probe.KV("EXITS-TOWN0", Probe.ExitLine());
                        _invBaseAnchors = Probe.AnchorCount();
                        _invBaseGround = Probe.GroundCount();
                        _area0 = Probe.AreaOfMap();
                        _shots[0] = "s2_01_inventory_open.png";
                        _shots[1] = "s2_02_drag_to_empty.png";
                        _shots[2] = "s2_03_drag_swap.png";
                        _shots[3] = "s2_04_drag_to_ground.png";
                        _shots[4] = "s2_05_waypoint_panel.png";
                        _shots[5] = "s2_06_waypoint_travel.png";
                        // U41/U45: the wilderness (the ONLY chunked area) -- the user's big black background
                        _shots[6] = "s2_07_bloodmoor_on_entry.png";
                        _shots[7] = "s2_08_bloodmoor_after_travel.png";
                        Probe.KeyDown("i");
                        Probe.KeyUp();
                        Probe.Log("KEYDOWN key=I (the player's own inventory toggle; same path as pressing I)");
                        Next();
                        return;
                    }

                // 12 wait for the panel, then capture it (the drag source must be on screen)
                case 12:
                    if (!Elapsed(1.0f)) return;
                    if (!Probe.PanelOpen("Diablo2.UI.InventoryPanel")) { Tout("inv-wait", 15f); return; }
                    Probe.KV("INV-OPEN", "panel=InventoryPanel " + Probe.InvLine("after-open"));
                    Probe.KV("PANEL-RECT", Probe.PanelRectLine());
                    Probe.BeginShot(1, _shots[0]);
                    Next();
                    return;

                case 13:
                    if (!WaitShot(_shots[0], 15f)) return;
                    Next();
                    return;

                // 14 plan the three gestures from the LIVE snapshot (no hard-coded cell indices)
                case 14:
                    {
                        var panel = Probe.FindOpenInventory();
                        _plan.Clear();
                        if (panel == null)
                        {
                            Probe.Check("the inventory panel is open (I opened it with a real I key press)", false,
                                "FindObjectsByType<InventoryPanel>() found none");
                        }
                        else
                        {
                            var a0 = Probe.NthAnchor(0);
                            var a1 = Probe.NthAnchor(1);
                            var free = Probe.FirstFreeCell(a0);
                            Probe.KV("GESTURE-PLAN", "anchor0=" + a0 + "(" + Probe.AtAnchor(a0) + ") anchor1=" + a1
                                + "(" + Probe.AtAnchor(a1) + ") firstFree=" + free + " " + Probe.InvLine("plan"));
                            if (a0 >= 0 && free >= 0) _plan.Add(new Gesture { tag = "to-empty", from = a0, to = free, kind = 0 });
                            if (a0 >= 0 && a1 >= 0) _plan.Add(new Gesture { tag = "swap", from = a0, to = a1, kind = 1 });
                            if (a0 >= 0) _plan.Add(new Gesture { tag = "to-ground", from = a0, to = -1, kind = 2 });
                        }
                        _gi = 0;
                        _ph = 0;
                        Next();
                        return;
                    }

                // 15 the gesture list: prepare -> real mouse press/move/release -> settle -> assert -> shot
                case 15:
                    {
                        if (_gi >= 3) { Next(); return; }
                        // Re-plan EVERY gesture from the LIVE snapshot (run s2d/s2f planned once up
                        // front: after gesture #1 moved the item out of cell 0, gesture #2 then grabbed
                        // the now-EMPTY cell 0 and the panel refused both it and the ground drop).
                        Gesture g;
                        if (_ph == 0)
                        {
                            var a0 = Probe.NthAnchor(0);
                            var a1 = Probe.NthAnchor(1);
                            var free = Probe.FirstFreeCell(a0);
                            if (_gi == 0) g = new Gesture { tag = "to-empty", from = a0, to = free, kind = 0 };
                            else if (_gi == 1) g = new Gesture { tag = "swap", from = a0, to = a1, kind = 1 };
                            else g = new Gesture { tag = "to-ground", from = a0, to = -1, kind = 2 };
                            _cur = g;
                            Probe.KV("GESTURE-PLAN-" + g.tag, "liveAnchor0=" + a0 + "(" + Probe.AtAnchor(a0)
                                + ") liveAnchor1=" + a1 + "(" + Probe.AtAnchor(a1) + ") firstFree=" + free
                                + " " + Probe.InvLine("plan-" + g.tag));
                        }
                        else g = _cur;
                        if (_ph == 0)
                        {
                            var panel = Probe.FindOpenInventory();
                            if (panel == null) { Probe.Check("drag " + g.tag, false, "the panel is closed"); _gi++; _ph = 0; return; }
                            Vector2 a, b;
                            if (!Probe.CellScreen(panel, g.from, out a))
                            {
                                Probe.Check("drag " + g.tag, false, "source cell " + g.from + " has no screen rect");
                                _gi++; _ph = 0; return;
                            }
                            if (g.kind == 2)
                            {
                                Vector2 lo, hi;
                                if (!Probe.PanelRect(panel, out lo, out hi))
                                {
                                    Probe.Check("drag " + g.tag, false, "the panel has no screen rect");
                                    _gi++; _ph = 0; return;
                                }
                                b = new Vector2(lo.x - 80f, (lo.y + hi.y) * 0.5f);
                            }
                            else if (!Probe.CellScreen(panel, g.to, out b))
                            {
                                Probe.Check("drag " + g.tag, false, "target cell " + g.to + " has no screen rect");
                                _gi++; _ph = 0; return;
                            }
                            _dragPos = a;
                            _dropPos = b;
                            _dragFrom = g.from;
                            _dragTo = g.to;
                            _dragOutside = g.kind == 2;
                            _dragTag = g.tag;
                            _dragPhase = 0;
                            _dragFrames = 0;
                            _beforeAnchors = Probe.AnchorCount();
                            _beforeGround = Probe.GroundCount();
                            _beforeAtFrom = Probe.AtAnchor(g.from);
                            _beforeAtTo = Probe.AtAnchor(g.to);
                            Probe.KV("GESTURE", "tag=" + g.tag + " kind=" + g.kind
                                + " from=" + g.from + "(" + _beforeAtFrom + ") to=" + g.to + "(" + _beforeAtTo + ")"
                                + " outside=" + (_dragOutside ? 1 : 0)
                                + " pressAt=" + Probe.V(a) + " releaseAt=" + Probe.V(b)
                                + " raycastTopAtSource=\"" + Probe.RaycastTop(a) + "\""
                                + " anchors=" + _beforeAnchors + " ground=" + _beforeGround);
                            _ph = 1;
                            return;
                        }
                        if (_ph == 1)
                        {
                            if (TickDrag() == 0) return;
                            _ph = 2;
                            _at = Time.unscaledTime;
                            return;
                        }
                        if (_ph == 2)
                        {
                            if (!Elapsed(1.0f)) return;
                            var anchorsNow = Probe.AnchorCount();
                            var groundNow = Probe.GroundCount();
                            var atFromNow = Probe.AtAnchor(g.from);
                            var atToNow = Probe.AtAnchor(g.to);
                            bool ok;
                            string what;
                            string detail;
                            if (g.kind == 2)
                            {
                                what = "drag to-ground: released OUTSIDE the panel => one item leaves the pack, the ground gains one";
                                ok = anchorsNow == _beforeAnchors - 1 && groundNow == _beforeGround + 1;
                                detail = "anchors " + _beforeAnchors + "->" + anchorsNow + " (expect -1); ground "
                                    + _beforeGround + "->" + groundNow + " (expect +1); no duplication";
                            }
                            else if (g.kind == 1)
                            {
                                what = "drag swap: dropped ONTO another item => the two cells exchange contents, count unchanged";
                                ok = anchorsNow == _beforeAnchors
                                     && atToNow == _beforeAtFrom && atFromNow == _beforeAtTo
                                     && atFromNow != "-" && atToNow != "-";
                                detail = "anchors " + _beforeAnchors + "->" + anchorsNow + " (expect same); at(" + g.from + ") "
                                    + _beforeAtFrom + "->" + atFromNow + " (expect " + _beforeAtTo + "); at(" + g.to + ") "
                                    + _beforeAtTo + "->" + atToNow + " (expect " + _beforeAtFrom + ")";
                            }
                            else
                            {
                                what = "drag to-empty: dropped on a FREE cell => the item lands there, count unchanged";
                                // AtAnchor returns "(empty)" for a free cell ("-" only when out of range)
                                var fromEmpty = atFromNow == "(empty)" || atFromNow == "-";
                                ok = anchorsNow == _beforeAnchors && atToNow == _beforeAtFrom && fromEmpty;
                                detail = "anchors " + _beforeAnchors + "->" + anchorsNow + " (expect same); at(" + g.to + ") "
                                    + _beforeAtTo + "->" + atToNow + " (expect " + _beforeAtFrom + "); at(" + g.from + ") "
                                    + _beforeAtFrom + "->" + atFromNow + " (expect empty)";
                            }
                            Probe.Check(what, ok, detail + " | " + Probe.InvLine("after-" + g.tag));
                            Probe.KV("INV-" + g.tag, Probe.InvLine("after-" + g.tag));
                            Probe.BeginShot(2 + _gi, _shots[1 + _gi]);
                            _ph = 3;
                            return;
                        }
                        if (_ph == 3)
                        {
                            if (!WaitShot(_shots[1 + _gi], 15f)) return;
                            _gi++;
                            _ph = 0;
                            return;
                        }
                        return;
                    }

                // 16 close the inventory again (real "i")
                case 16:
                    if (!Elapsed(0.6f)) return;
                    Probe.KeyDown("i");
                    Probe.KeyUp();
                    Next();
                    return;

                case 17:
                    if (!Elapsed(1.0f)) return;
                    Next();
                    return;

                // ============================================================ S2: waypoint ==========
                // 18 visit the neighbouring area first: the panel only lists areas the player HAS VISITED
                case 18:
                    {
                        var ex = Probe.FirstExit();
                        Probe.KV("EXITS-TOWN", Probe.ExitLine());
                        Probe.KV("WALK-OUT", "area=" + Probe.AreaOfMap() + " target=" + Probe.Grid(ex)
                            + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                        if (ex.x < 0) { Probe.Warn("S2-ABORT the town has no exit cell"); Next(); return; }
                        _area0 = Probe.AreaOfMap();
                        _t0 = Time.unscaledTime;
                        Probe.Emit(Diablo2.Core.Events.MoveCommand, ex);
                        Next();
                        return;
                    }

                case 19:
                    if (Probe.AreaOfMap() == _area0)
                    {
                        if (Time.unscaledTime - _t0 >= 90f)
                        {
                            Probe.Warn("S2 leave-town timeout: still area=" + Probe.AreaOfMap()
                                + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                            Next();
                            return;
                        }
                        return;
                    }
                    Probe.KV("AREA-1", "now=" + Probe.AreaOfMap() + " visited=" + Probe.VisitedLine());
                    _chunkAt = Time.unscaledTime;
                    // U41/U45 black-background readings (report section 13.3): right after entering the 80x80
                    // wilderness -- the only area that goes through the chunked path.
                    Probe.KV("CHUNK-ENTER", Probe.ChunkState("enter-area-" + Probe.AreaOfMap()));
                    Probe.KV("CHUNK-ENTER-MISS", Probe.ChunkMiss("enter-area-" + Probe.AreaOfMap()));
                    Probe.BeginShot(7, _shots[6]);
                    _area0 = Probe.AreaOfMap();
                    _t0 = Time.unscaledTime;
                    Next();
                    return;

                // 20 walk back into town (a BloodMoor exit that is not the cave entrance leads back to Town)
                case 20:
                    {
                        var back = Probe.BackExit();
                        Probe.KV("WALK-BACK", "area=" + Probe.AreaOfMap() + " target=" + Probe.Grid(back)
                            + " " + Probe.ExitLine());
                        if (back.x < 0) { Probe.Warn("S2-ABORT no back exit in this area"); Next(); return; }
                        _t0 = Time.unscaledTime;
                        Probe.Emit(Diablo2.Core.Events.MoveCommand, back);
                        Next();
                        return;
                    }

                case 21:
                    if (Probe.AreaOfMap() == _area0)
                    {
                        if (Time.unscaledTime - _t0 >= 90f)
                        {
                            Probe.Warn("S2 back-to-town timeout: still area=" + Probe.AreaOfMap()
                                + " grid=" + Probe.Grid(Probe.PlayerGrid()));
                            Next();
                            return;
                        }
                        return;
                    }
                    Probe.KV("AREA-2", "now=" + Probe.AreaOfMap() + " visited=" + Probe.VisitedLine());
                    Next();
                    return;

                // 22 walk onto the waypoint anchor: the SAME MoveCommand the InputReader emits for a ground click
                case 22:
                    {
                        var wp = Probe.FirstWaypoint();
                        Probe.KV("WALK-WP", "area=" + Probe.AreaOfMap() + " waypoint=" + Probe.Grid(wp)
                            + " grid=" + Probe.Grid(Probe.PlayerGrid()) + " " + Probe.WaypointLine());
                        if (wp.x < 0) { Probe.Warn("S2-ABORT the town has no waypoint anchor"); Next(); return; }
                        // second sample of the SAME wilderness entry, >= 2 s after the first one:
                        // does the chunk rebuild finish (transient) or stay unfinished (permanent)?
                        if (_chunkAt > 0f && Time.unscaledTime - _chunkAt >= 2.0f)
                        {
                            Probe.KV("CHUNK-ENTER-LATE", Probe.ChunkState("enter+"
                                + (Time.unscaledTime - _chunkAt).ToString("0.0") + "s"));
                            Probe.KV("CHUNK-ENTER-LATE-MISS", Probe.ChunkMiss("enter-late"));
                        }
                        _wpGrid = wp;
                        _t0 = Time.unscaledTime;
                        Probe.Emit(Diablo2.Core.Events.MoveCommand, wp);
                        Next();
                        return;
                    }

                case 23:
                    {
                        var open = Probe.PanelOpen("Diablo2.UI.WaypointPanel");
                        if (!open)
                        {
                            if (Time.unscaledTime - _t0 >= 90f)
                            {
                                Probe.KV("WP-PANEL", "open=0 grid=" + Probe.Grid(Probe.PlayerGrid())
                                    + " waypoint=" + Probe.Grid(_wpGrid) + " " + Probe.WaypointLine());
                                Probe.Check("walking onto the town waypoint opens the waypoint panel", false,
                                    "timeout: player=" + Probe.Grid(Probe.PlayerGrid()) + " anchor=" + Probe.Grid(_wpGrid)
                                    + " dist=" + Probe.Chebyshev(Probe.PlayerGrid(), _wpGrid));
                                Next();
                            }
                            return;
                        }
                        Probe.KV("WP-PANEL", "open=1 grid=" + Probe.Grid(Probe.PlayerGrid())
                            + " waypoint=" + Probe.Grid(_wpGrid)
                            + " dist=" + Probe.Chebyshev(Probe.PlayerGrid(), _wpGrid)
                            + " visited=" + Probe.VisitedLine()
                            + " dests=" + Probe.DestCount() + Probe.DestLabels()
                            + " " + Probe.WaypointLine());
                        Probe.Check("walking onto the town waypoint opens the waypoint panel (list == visited minus current)",
                            Probe.DestCount() >= 0 && Probe.DestCount() == Probe.VisitedListCount() - 1,
                            "dests=" + Probe.DestCount() + Probe.DestLabels() + " visited=" + Probe.VisitedLine()
                            + " player=" + Probe.Grid(Probe.PlayerGrid()) + " anchor=" + Probe.Grid(_wpGrid));
                        Probe.BeginShot(5, _shots[4]);
                        Next();
                        return;
                    }

                case 24:
                    if (!WaitShot(_shots[4], 20f)) return;
                    Next();
                    return;

                // 25 click the first destination row with a REAL mouse click => the area must switch
                case 25:
                    if (!Elapsed(0.8f)) return;
                    {
                        var count = Probe.DestCount();
                        Probe.KV("WP-DESTS", "rows=" + count + " labels=" + Probe.DestLabels()
                            + " visited=" + Probe.VisitedLine());
                        _travelDest = Probe.DestArea0();
                        _area0 = Probe.AreaOfMap();
                        if (count <= 0 || _travelDest < 0)
                        {
                            Probe.Check("clicking a destination travels to that area", false,
                                "the destination list is empty (visited=" + Probe.VisitedLine() + ") => nothing to click");
                            Next();
                            return;
                        }
                        _t0 = Time.unscaledTime;
                        ClickButton("Dest0", "waypoint");
                        Next();
                        return;
                    }

                case 26:
                    if (Probe.AreaOfMap() == _area0 && Time.unscaledTime - _t0 < 40f) return;
                    {
                        var now = Probe.AreaOfMap();
                        var ok = now == _travelDest;
                        Probe.Check("clicking a destination travels to that area", ok,
                            "picked area=" + _travelDest + " area " + _area0 + "->" + now + " (expect " + _travelDest + ")"
                            + " visited=" + Probe.VisitedLine());
                        Probe.KV("AREA-3", "now=" + now + " grid=" + Probe.Grid(Probe.PlayerGrid())
                            + " " + Probe.InvLine("final"));
                        Probe.BeginShot(6, _shots[5]);
                        Next();
                        return;
                    }

                case 27:
                    if (!WaitShot(_shots[5], 20f)) return;
                    // U41/U45 second entry into the wilderness (after the waypoint travel):
                    // does the chunk state heal itself?
                    Probe.KV("CHUNK-TRAVEL", Probe.ChunkState("after-waypoint-travel"));
                    Probe.KV("CHUNK-TRAVEL-MISS", Probe.ChunkMiss("after-waypoint-travel"));
                    Probe.BeginShot(8, _shots[7]);
                    Next();
                    return;

                // ---------------------------------------------------------- verdict
                case 28:
                    {
                        var device = Probe.DeviceName();
                        Probe.Check("real render device is a hardware GPU", !Probe.SoftwareRaster(device),
                            "device=\"" + device + "\"");
                        Probe.KV("SUMMARY", "gestures=" + _plan.Count + " dests=" + Probe.DestCount()
                            + " area=" + Probe.AreaOfMap() + " " + Probe.InvLine("final"));
                        var okAll = Probe.AllChecksOk();
                        Probe.Log("VERDICT ok=" + (okAll ? 1 : 0)
                            + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad()
                            + " fsm=" + Fsm());
                        Finish("end");
                        return;
                    }

                default:
                    Finish("end");
                    return;
            }
        }

        // ================================================================ ground click ======
        /// <summary>
        /// Pick a walkable target a few cells away (the frozen evidence used 3 cells west:
        /// (9,6) -> (6,6)).  The offsets are tried in order; "found" = 0 means no candidate.
        /// The criterion is "a real ground click makes the character walk", so the exact cell
        /// may differ per session (the spawn comes from the save) -- the assertion does not.
        /// </summary>
        private static Vector2Int PickTarget(Vector2Int from, out int found)
        {
            var offsets = new[]
            {
                new Vector2Int(-3, 0), new Vector2Int(-2, 0), new Vector2Int(-2, -1), new Vector2Int(-2, 1),
                new Vector2Int(-3, -1), new Vector2Int(-3, 1), new Vector2Int(0, -3), new Vector2Int(-1, -3),
                new Vector2Int(1, -3), new Vector2Int(0, 3), new Vector2Int(-1, 3), new Vector2Int(1, 3),
                new Vector2Int(-3, -2), new Vector2Int(-3, 2), new Vector2Int(2, -3), new Vector2Int(2, 3),
                new Vector2Int(-4, 0), new Vector2Int(4, 0), new Vector2Int(0, -4), new Vector2Int(0, 4),
                new Vector2Int(-2, -2), new Vector2Int(-2, 2), new Vector2Int(2, -2), new Vector2Int(2, 2),
            };
            var map = Probe.Map();
            found = 0;
            foreach (var o in offsets)
            {
                var g = from + o;
                found++;
                if (map != null && map.Walkable(g)) return g;
            }
            found = 0;
            return from;
        }

        /// <summary>0 = running, 1 = delivered, -1 = gave up (projection never round-tripped).</summary>
        private int TickGroundClick()
        {
            var cam = Camera.main;
            if (cam == null) { Probe.Warn("C-ABORT Camera.main is null"); return -1; }
            _ckFrames++;
            switch (_ckPhase)
            {
                case 0:
                    {
                        var sp = cam.WorldToScreenPoint(Iso.GridToWorld(_ckWant));
                        _ckPos = new Vector2(sp.x, _ckFlipY ? Screen.height - sp.y : sp.y);
                        Probe.MouseState(_ckPos, false);
                        _ckPhase = 1;
                        _ckFrames = 0;
                        return 0;
                    }
                case 1:
                    if (_ckFrames < 3) return 0;
                    {
                        var got = Iso.ScreenToGrid(cam, Probe.MousePosNow());
                        Probe.KV("C PROJECT", "want=" + Probe.Grid(_ckWant) + " got=" + Probe.Grid(got)
                            + " project_ok=" + (got == _ckWant ? 1 : 0)
                            + " flipY=" + (_ckFlipY ? 1 : 0)
                            + " injected=" + Probe.V(_ckPos) + " frame=" + Time.frameCount);
                        if (got == _ckWant) { _ckPhase = 2; _ckFrames = 0; return 0; }
                        if (!_ckFlipTried)
                        {
                            _ckFlipTried = true;
                            _ckFlipY = !_ckFlipY;
                            Probe.Warn("C CALIB the injected Y did not round-trip -> retrying with flipped Y");
                            _ckPhase = 0;
                            _ckFrames = 0;
                            return 0;
                        }
                        return -1;
                    }
                case 2:
                    if (_ckFrames < 2) return 0;
                    _ckMoveBefore = _moveCmds;
                    Probe.MouseState(_ckPos, true);
                    _ckPhase = 3;
                    _ckFrames = 0;
                    return 0;
                case 3:
                    // the C DOWN line is written one frame AFTER the mousedown, so the values it
                    // reports are the ones the click already produced (isMoving=1) -- same as the
                    // first evidence.  moveCmdsBefore is still the pre-click counter.
                    if (_ckFrames < 2) return 0;
                    Probe.KV("C DOWN", "grid=" + Probe.Grid(Probe.GridOf(Probe.Player()))
                        + " isMoving=" + (Probe.Player() != null && Probe.Player().IsMoving ? 1 : 0)
                        + " moveCmdsBefore=" + _ckMoveBefore
                        + " frame=" + Time.frameCount);
                    Probe.KV("C RAYCAST", "want=" + Probe.Grid(_ckWant)
                        + " top=\"" + Probe.RaycastTop(_ckPos) + "\""
                        + " note=the pointer must not sit over a UI panel (InputReader's UI-hit guard)");
                    Probe.MouseState(_ckPos, false);
                    _ckPhase = 4;
                    _ckFrames = 0;
                    return 0;
                case 4:
                    if (_ckFrames < 2) return 0;
                    return 1;
                default:
                    return 1;
            }
        }

        // ================================================================ events ============
        private void OnMoveCommand(Vector2Int target) { _moveCmds++; }
        private void OnSwapWeaponRequest() { _swapReqs++; }

        // ================================================================ finish ============
        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Probe.KV("FINISH", "why=" + why + " step=" + _step
                + " device=\"" + Probe.DeviceName() + "\""
                + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad());
            Probe.Log("TOUR-DONE why=" + why + " steps=" + _step);
            Probe.WriteFile(Probe.DonePath,
                "TOUR-DONE tag=" + _tag + " why=" + why
                + " verdict=" + (Probe.AllChecksOk() ? 1 : 0)
                + (_invalidDevice ? " INVALID-DEVICE" : string.Empty)
                + " checks_ok=" + Probe.ChecksOk() + " checks_bad=" + Probe.ChecksBad()
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
    }
}
