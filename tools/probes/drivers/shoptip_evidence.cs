// =============================================================================
// d2tour_evidence.cs -- ONE Play session that walks the whole game once and
//   captures, in this order:
//     A) 18 panel screenshots (Boot / MainMenu / CharSelect / CharCreate /
//        Loading / Hud / Inventory / Character / SkillTree / QuestLog / MiniMap /
//        Pause / Settings / Confirm / NpcDialog / Shop / Death / Waypoint)
//     B) 3 in-game area screenshots (Town / BloodMoor / DenOfEvil)
//     C) the remaining "needs a window" readings:
//        W1  same camera + same sprite: un-hovered vs hovered-then-left, two frames
//        W4  CharacterPanel: mirror Text preferredWidth / GetPreferredWidth /
//            D2Label.LineCount (three values together)
//        W9  NPC nameplate black bar width: rendered rect vs NameplateSizeFor(text)
//        N1  the first `[Hover]` hit of the session must be a MONSTER body
//        U32 town waypoint anchor (31,26): sprite name read twice, frames apart
//        N2  monster move / hit frames (viewed on the contact sheet)
//        N4  inventory drag ghost + drop-cell highlight
//
// Shape (Probe / Api / Tour / Driver, done marker, absolute-path screenshots)
// follows tools/probes/drivers/d2p1_evidence.cs (same runner lock protocol).
// ASCII only. No production file is touched; internal members are reached by
// reflection (the driver is compiled into its own assembly by run_script).
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace P2
{
    /// <summary>Log / reflection / json / screenshot helpers.</summary>
    public static class Probe
    {
        internal const string Tag = "TOUR";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, msg); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }

        internal static void Warn(string msg) { Log("WARN " + msg); }
        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("TOUR-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var d = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                    File.WriteAllText(_done, "TOUR-DONE " + DateTime.Now.ToString("o"));
                }
            }
            catch (Exception ex) { Log("DONE-MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static Type FindType(string full)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < asms.Length; i++)
            {
                try
                {
                    var t = asms[i].GetType(full, false);
                    if (t != null) return t;
                }
                catch { }
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

        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }
        internal static IMonsterModule Monsters() { return CtxMember("Monster") as IMonsterModule; }
        internal static ICombatModule Combat() { return CtxMember("Combat") as ICombatModule; }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }
        internal static string Grid2(int x, int y) { return "(" + x + "," + y + ")"; }

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

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

        // ---- live bitmap-label registry (Diablo2.UI.D2Label is internal) -------

        internal static List<object> Labels()
        {
            var res = new List<object>();
            var t = FindType("Diablo2.UI.D2Label");
            if (t == null) { Log("LABELMAP type-missing"); return res; }
            var f = t.GetField("Live", BindingFlags.NonPublic | BindingFlags.Static);
            var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
            if (list == null) { Log("LABELMAP field-missing"); return res; }
            foreach (var o in list) { if (o != null) res.Add(o); }
            return res;
        }

        private static PropertyInfo _propText;
        private static PropertyInfo _propRoot;
        private static PropertyInfo _propLines;

        private static void EnsureLabelProps(Type t)
        {
            if (_propText != null) return;
            _propText = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            _propRoot = t.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance);
            _propLines = t.GetProperty("LineCount", BindingFlags.Public | BindingFlags.Instance);
        }

        internal static string LabelText(object label)
        {
            try { EnsureLabelProps(label.GetType()); return _propText != null ? (string)_propText.GetValue(label) : null; }
            catch { return null; }
        }

        internal static Transform LabelRoot(object label)
        {
            try { EnsureLabelProps(label.GetType()); return _propRoot != null ? _propRoot.GetValue(label) as Transform : null; }
            catch { return null; }
        }

        internal static int LabelLines(object label)
        {
            try
            {
                EnsureLabelProps(label.GetType());
                if (_propLines == null) return -1;
                return Convert.ToInt32(_propLines.GetValue(label));
            }
            catch { return -1; }
        }

        internal static bool Under(Transform t, Transform root)
        {
            var p = t;
            while (p != null) { if (p == root) return true; p = p.parent; }
            return false;
        }

        internal static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>First active in-hierarchy component of <paramref name="typeName"/>'s root.</summary>
        internal static Transform ActiveRoot(string typeName)
        {
            var t = FindType(typeName);
            if (t == null) { Warn("TYPE-MISSING " + typeName); return null; }
            var arr = Resources.FindObjectsOfTypeAll(t);
            if (arr == null) return null;
            for (var i = 0; i < arr.Length; i++)
            {
                var c = arr[i] as Component;
                if (c != null && c.gameObject.activeInHierarchy) return c.transform;
            }
            return null;
        }

        /// <summary>uGUI Text on the label node itself, else on a child/parent within 2 levels.</summary>
        internal static UnityEngine.UI.Text MirrorText(Transform t)
        {
            if (t == null) return null;
            var self = t.GetComponent<UnityEngine.UI.Text>();
            if (self != null) return self;
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i).GetComponent<UnityEngine.UI.Text>();
                if (c != null) return c;
            }
            if (t.parent != null)
            {
                var p = t.parent.GetComponent<UnityEngine.UI.Text>();
                if (p != null) return p;
            }
            return null;
        }

        internal static string F2(float v) { return v.ToString("0.####"); }

        internal static SpriteRenderer NearestRenderer(Vector3 world, float maxDist)
        {
            var all = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            SpriteRenderer best = null;
            var bd = maxDist;
            if (all == null) return null;
            for (var i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null) continue;
                var d = Vector3.Distance(sr.transform.position, world);
                if (d < bd) { bd = d; best = sr; }
            }
            return best;
        }

        internal static string MatName(SpriteRenderer sr)
        {
            if (sr == null) return "(no-renderer)";
            var m = sr.sharedMaterial;
            return m == null ? "(null:unity-default)" : m.name;
        }

        /// <summary>EntityHighlight.ReadBack(SpriteRenderer, out float, out float) via reflection.</summary>
        internal static string Props(SpriteRenderer sr)
        {
            var t = FindType("Diablo2.Module.View.EntityHighlight");
            if (t == null) return "(_Brightness/type-missing _Contrast/type-missing)";
            var m = t.GetMethod("ReadBack", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return "(_Brightness/? _Contrast/?)";
            var args = new object[] { sr, 0f, 0f };
            try
            {
                m.Invoke(null, args);
                return "_Brightness=" + F2(Convert.ToSingle(args[1])) + " _Contrast=" + F2(Convert.ToSingle(args[2]));
            }
            catch (Exception ex) { return "(readback-fail " + ex.GetType().Name + ")"; }
        }

        internal static object FieldOf(object inst, string name)
        {
            if (inst == null) return null;
            var f = inst.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(inst) : null;
        }

        internal static void SetField(object inst, string name, object value)
        {
            if (inst == null) return;
            var f = inst.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(inst, value);
        }
    }

    /// <summary>Callable entries for the runner (run_script --entry).</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000"); }

        public static string Shot(string path) { return Probe.Shot(path); }
    }

    /// <summary>Installer: spec = "&lt;charName&gt;|&lt;done marker&gt;|&lt;shot dir&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("TourEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain: front-end panels -> in-game panels -> areas -> readings.</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _loadingShot;
        private int _shotCount;
        private bool _interactTried;
        private int _qlPhase;

        // forced hover (grid injected through the project's own self-test API)
        private bool _hoverOn;
        private Vector2Int _hoverGrid;
        private int _monsterId = -1;
        private int _monsterX = -1;
        private int _monsterY = -1;
        private Vector2Int _npcGrid = new Vector2Int(int.MinValue, int.MinValue);
        private string _wpFrameA = "(unread)";

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _name = parts[0];
            var done = parts.Length > 1 ? parts[1] : string.Empty;
            _shotDir = parts.Length > 2 ? parts[2] : string.Empty;
            _t0 = Time.unscaledTime;
            Probe.Paths(done);
            Probe.Log("DRIVER-INIT name=\"" + _name + "\" shots=" + _shotDir + " frame=" + Time.frameCount);
        }

        // ---- plumbing ---------------------------------------------------------
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static bool Open<T>() where T : class, CloverEngine.IUIPanel
        { return Game.UI != null && Game.UI.IsOpen<T>(); }

        private string Shots(string leaf)
        {
            var dir = string.IsNullOrEmpty(_shotDir) ? Directory.GetCurrentDirectory() : _shotDir;
            _shotCount++;
            return Path.Combine(dir, "tour_" + leaf + ".png");
        }

        /// <summary>
        /// Request a capture and then ABORT the current step (SnapHold), so whatever
        /// state change the caller does next (close the panel / enter the next
        /// station) only happens after a frame has been rendered in the state being
        /// photographed. ScreenCapture writes the frame that is rendered AFTER the
        /// call, so a capture followed by a synchronous Close/Open in the same
        /// Update photographs the *next* state (measured: five in-game panels came
        /// back showing the closed panel). Re-entry is free: the same (step, leaf)
        /// pair returns immediately, so the transition runs right after the hold.
        /// </summary>
        private void Snap(string leaf)
        {
            var key = _step + "|" + leaf;
            if (_snapKey == key) return;
            _snapKey = key;
            Probe.KV("SHOT-" + leaf, Probe.Shot(Shots(leaf)));
            throw new SnapHold();
        }

        private static int Area()
        {
            var m = Probe.Map();
            return m != null ? (int)m.Area : -1;
        }

        private static Vector2Int PlayerGrid()
        {
            var p = Probe.Player();
            return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);
        }

        /// <summary>First in-bounds walkable grid at chebyshev distance r from g.</summary>
        private static Vector2Int WalkableRing(Vector2Int g, int r)
        {
            var map = Probe.Map();
            if (map == null) return new Vector2Int(-1, -1);
            for (var dx = -r; dx <= r; dx++)
            {
                for (var dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var t = new Vector2Int(g.x + dx, g.y + dy);
                    if (map.InBounds(t) && map.Walkable(t)) return t;
                }
            }
            return new Vector2Int(-1, -1);
        }

        private static int AliveAt(Vector2Int g)
        {
            var mon = Probe.Monsters();
            if (mon == null || mon.All == null) return -1;
            var n = 0;
            for (var i = 0; i < mon.All.Count; i++)
            {
                var m = mon.All[i];
                if (m != null && m.alive && m.gridX == g.x && m.gridY == g.y) n++;
            }
            return n;
        }

        private static int AliveNear(Vector2Int g, int r)
        {
            var mon = Probe.Monsters();
            if (mon == null || mon.All == null) return -1;
            var n = 0;
            for (var i = 0; i < mon.All.Count; i++)
            {
                var m = mon.All[i];
                if (m == null || !m.alive) continue;
                if (Mathf.Max(Mathf.Abs(m.gridX - g.x), Mathf.Abs(m.gridY - g.y)) <= r) n++;
            }
            return n;
        }

        private static object Reader()
        {
            var p = Probe.Player();
            if (p == null) return null;
            try
            {
                var pr = p.GetType().GetProperty("Input", BindingFlags.Public | BindingFlags.Instance);
                return pr != null ? pr.GetValue(p) : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Force the hover grid through InputReader.OverrideHoverGrid + UpdateHover
        /// (both public members of an internal type; same approach as d2p1).
        /// </summary>
        private void ForceHover(Vector2Int g)
        {
            var inp = Reader();
            if (inp == null) { Probe.Warn("FORCE-HOVER no InputReader"); return; }
            try
            {
                var t = inp.GetType();
                var m1 = t.GetMethod("OverrideHoverGrid", BindingFlags.Public | BindingFlags.Instance);
                var m2 = t.GetMethod("UpdateHover", BindingFlags.Public | BindingFlags.Instance);
                if (m1 != null) m1.Invoke(inp, new object[] { g });
                if (m2 != null) m2.Invoke(inp, new object[] { true });
            }
            catch (Exception ex) { Probe.Log("FORCE-HOVER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// The one-shot production `[Hover]` line only ever fires once per session
        /// (InputReader._hoverLogged). Clearing that flag right before a MONSTER
        /// hover makes the production line itself the monster-body evidence (N1).
        /// The payload is still produced by the production path.
        /// </summary>
        private void ArmHoverLog()
        {
            var inp = Reader();
            if (inp == null) { Probe.Warn("ARM-HOVERLOG no InputReader"); return; }
            Probe.SetField(inp, "_hoverLogged", false);
            Probe.KV("ARM-HOVERLOG", "InputReader._hoverLogged reset so the production [Hover] line fires on the monster hover");
        }

        private void LogHoverState(string tag, Vector2Int g)
        {
            var inp = Reader();
            var cur = inp != null ? inp.GetType().GetProperty("CurrentHover", BindingFlags.Public | BindingFlags.Instance) : null;
            var t = cur != null ? cur.GetValue(inp) : null;
            if (t == null) { Probe.KV(tag, "grid=" + Probe.Grid(g) + " currentHover=(null)"); return; }
            var ty = t.GetType();
            var f = new Func<string, object>((n) =>
            {
                var fi = ty.GetField(n, BindingFlags.Public | BindingFlags.Instance);
                return fi != null ? fi.GetValue(t) : null;
            });
            Probe.KV(tag, "grid=" + Probe.Grid(g)
                + " hasTarget=" + f("hasTarget")
                + " cursor=" + f("cursor")
                + " id=" + f("id")
                + " 格=" + Probe.Grid2(Convert.ToInt32(f("gridX")), Convert.ToInt32(f("gridY")))
                + " name=\"" + f("name") + "\"");
        }

        // ---- W4: CharacterPanel three-way text reading ------------------------
        private void ReadCharPanelW4()
        {
            var root = Probe.ActiveRoot("Diablo2.UI.CharacterPanel");
            if (root == null) { Probe.Warn("W4 char panel root missing"); return; }
            var n = 0;
            var labels = Probe.Labels();
            for (var i = 0; i < labels.Count; i++)
            {
                var rt = Probe.LabelRoot(labels[i]);
                if (rt == null || !Probe.Under(rt, root)) continue;
                n++;
                var txt = Probe.MirrorText(rt);
                var pref = txt != null ? txt.preferredWidth : -999f;
                var gen = -999f;
                if (txt != null)
                {
                    try
                    {
                        var settings = txt.GetGenerationSettings(new Vector2(txt.rectTransform.rect.width, 0f));
                        var g = txt.cachedTextGeneratorForLayout;
                        var me = g.GetType().GetMethod("GetPreferredWidth", new[] { typeof(string), settings.GetType() });
                        if (me != null) gen = Convert.ToSingle(me.Invoke(g, new object[] { txt.text, settings }));
                    }
                    catch (Exception ex) { gen = -998f; Probe.Warn("W4 genpref-ex " + ex.GetType().Name); }
                }
                Probe.KV("W4", "node=" + rt.name
                    + " text=\"" + (Probe.LabelText(labels[i]) ?? "") + "\""
                    + " mirrorText=" + (txt == null ? "(no-ugui-text)" : "yes")
                    + " mirrorFont=" + (txt == null ? "-" : (txt.font == null ? "(null)" : txt.font.name))
                    + " preferredWidth=" + Probe.F2(pref)
                    + " genPreferredWidth=" + Probe.F2(gen)
                    + " d2labelLineCount=" + Probe.LabelLines(labels[i]));
            }
            Probe.KV("W4-COUNT", n.ToString());
        }

        // ---- W9: NPC nameplate black bar width -------------------------------
        private void ReadNameplate(string tag)
        {
            var t = Probe.FindType("Diablo2.Module.EntityHighlight");
            var c = Probe.FindType("Diablo2.UI.EnemyBarView");
            if (c == null) { Probe.Warn("W9 EnemyBarView type missing"); return; }
            var inst = c.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            var view = inst != null ? inst.GetValue(null) : null;
            if (view == null) { Probe.Warn("W9 EnemyBarView.Instance null"); return; }

            var nm = c.GetProperty("NameplateText", BindingFlags.Public | BindingFlags.Instance).GetValue(view) as string;
            var vis = c.GetProperty("NameplateVisible", BindingFlags.Public | BindingFlags.Instance).GetValue(view);
            var sz = (Vector2)c.GetMethod("NameplateSizeFor", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { nm ?? string.Empty });

            // The plate root is the engine WorldNameplate root: an Image (black bar)
            // with a child label named "Label". Find it by walking the EnemyBarView canvas.
            var rt = view as MonoBehaviour;
            Transform plate = null;
            if (rt != null)
            {
                var arr = Resources.FindObjectsOfTypeAll<RectTransform>();
                for (var i = 0; i < arr.Length; i++)
                {
                    var p = arr[i];
                    if (p == null || p.name != "NpcNameplate") continue;
                    if (!Probe.Under(p, rt.transform)) continue;
                    if (p.gameObject.activeSelf) { plate = p; break; }
                }
                if (plate == null)
                {
                    for (var i = 0; i < arr.Length; i++)
                    {
                        var p = arr[i];
                        if (p == null || p.name != "NpcNameplate" || !Probe.Under(p, rt.transform)) continue;
                        plate = p; break;
                    }
                }
            }

            if (plate == null)
            {
                Probe.KV("W9", tag + " plateNode=NOT-FOUND visible=" + vis + " text=\"" + nm + "\""
                    + " NameplateSizeFor=(" + Probe.F2(sz.x) + "," + Probe.F2(sz.y) + ")");
                return;
            }

            var plateW = ((RectTransform)plate).rect.width;
            var plateScaled = ((RectTransform)plate).sizeDelta.x;
            var lab = plate.childCount > 0 ? plate.GetChild(0) : null;
            var labW = lab is RectTransform ? ((RectTransform)lab).rect.width : -1f;
            var white = labelWidthWhite(nm ?? string.Empty);
            Probe.KV("W9", tag + " text=\"" + nm + "\" visible=" + vis
                + " plateRectW=" + Probe.F2(plateW)
                + " plateSizeDeltaW=" + Probe.F2(plateScaled)
                + " labelRectW=" + Probe.F2(labW)
                + " NameplateSizeFor.x=" + Probe.F2(sz.x)
                + " NameplateSizeFor.y=" + Probe.F2(sz.y)
                + " labelW+pad12=" + Probe.F2(labW + 12f * UiK())
                + " expect_pad6x2=" + Probe.F2(12f * UiK()));
        }

        private static float _uiK = -1f;
        private static float UiK()
        {
            if (_uiK > 0f) return _uiK;
            var t = Probe.FindType("Diablo2.UI.UiLayoutGame");
            if (t != null)
            {
                var f = t.GetField("K", BindingFlags.Public | BindingFlags.Static | BindingFlags.NonPublic);
                if (f != null) _uiK = Convert.ToSingle(f.GetValue(null));
            }
            if (_uiK <= 0f) _uiK = 1.8f;
            return _uiK;
        }

        /// <summary>Pure text extent through the project's own measuring口径 (NameplateSizeFor minus padding).</summary>
        private float labelWidthWhite(string text)
        {
            var c = Probe.FindType("Diablo2.UI.EnemyBarView");
            if (c == null) return -1f;
            var m = c.GetMethod("NameplateSizeFor", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return -1f;
            var v = (Vector2)m.Invoke(null, new object[] { text });
            return v.x - 12f * UiK();
        }

        // ---- U32: town waypoint anchor sprite ---------------------------------
        /// <summary>
        /// The waypoint anchor body is whatever live SpriteRenderer currently wears a
        /// `waypoint/*` frame sprite -- identifying it by the rendered asset name does
        /// not depend on any MapView internal (field names / pooled-node ownership).
        /// </summary>
        private List<SpriteRenderer> _wpCandidates;

        /// <summary>
        /// Renderers sitting on the waypoint anchor cell (within 0.6 world units of
        /// the anchor's grid centre). The body is identified by *behaviour*, not by
        /// name: it is the only one there whose frame sprite changes between two
        /// samples a few frames apart (tiles do not animate, and the player is 2.0
        /// units away, so it is outside this radius).
        /// </summary>
        private List<SpriteRenderer> WaypointCandidates()
        {
            if (_wpCandidates != null) return _wpCandidates;
            _wpCandidates = new List<SpriteRenderer>();
            var map = Probe.Map();
            if (map == null || map.WaypointPoints == null || map.WaypointPoints.Count == 0) return _wpCandidates;
            var w = Iso.GridToWorld(map.WaypointPoints[0]);
            Probe.KV("U32-ANCHOR-POS", "grid=" + Probe.Grid(map.WaypointPoints[0]) + " world=" + w.ToString("0.00"));
            var all = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            if (all == null) return _wpCandidates;
            for (var i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null || !sr.gameObject.activeInHierarchy) continue;
                if (Vector3.Distance(sr.transform.position, w) > 1.3f) continue;
                _wpCandidates.Add(sr);
            }
            var names = new System.Text.StringBuilder();
            for (var i = 0; i < _wpCandidates.Count && i < 10; i++)
            {
                var s = _wpCandidates[i].sprite;
                names.Append("[" + i + "]=" + _wpCandidates[i].gameObject.name
                    + "/" + (s != null ? s.name : "(null)")
                    + "/order=" + _wpCandidates[i].sortingOrder + " ");
            }
            Probe.KV("U32-CANDIDATES", _wpCandidates.Count + " : " + names);

            // which of them wears one of the 8 waypoint-frame sprites (000..007)?
            for (var i = 0; i < _wpCandidates.Count; i++)
            {
                var s = _wpCandidates[i].sprite;
                if (s == null) continue;
                var nm = s.name;
                if (nm.Length != 3) continue;
                if (nm != "000" && nm != "001" && nm != "002" && nm != "003"
                    && nm != "004" && nm != "005" && nm != "006" && nm != "007") continue;
                Probe.KV("U32-FRAME-SPRITE", "[" + i + "] node=" + _wpCandidates[i].gameObject.name
                    + " sprite=" + nm + " pos=" + _wpCandidates[i].transform.position.ToString("0.00")
                    + " order=" + _wpCandidates[i].sortingOrder);
            }

            // MapView's own animation carrier: dump every SpriteRenderer field so a
            // null reading is attributable (field missing vs field null).
            var mt = Probe.FindType("Diablo2.Module.Map.MapView");
            if (mt != null)
            {
                var arr = Resources.FindObjectsOfTypeAll(mt);
                Probe.KV("U32-MAPVIEW-INSTANCES", (arr == null ? 0 : arr.Length).ToString());
                if (arr != null)
                {
                    for (var i = 0; i < arr.Length; i++)
                    {
                        var fields = arr[i].GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                        var line = new System.Text.StringBuilder();
                        for (var k = 0; k < fields.Length; k++)
                        {
                            var f = fields[k];
                            if (f.FieldType != typeof(SpriteRenderer)) continue;
                            var v = f.GetValue(arr[i]) as SpriteRenderer;
                            line.Append(f.Name + "=" + (v == null ? "null" : (v.sprite != null ? v.sprite.name : "sprite-null")) + " ");
                        }
                        Probe.KV("U32-MAPVIEW-" + i, "spriteRendererFields: " + line);
                    }
                }
            }
            return _wpCandidates;
        }

        private string WaypointSprite()
        {
            // Verdict source = the animation carrier's OWN sprite. The candidate list
            // below cannot separate the waypoint from the player's idle animation, so it
            // stays as diagnostics only.
            var mt = Probe.FindType("Diablo2.Module.Map.MapView");
            var arr = mt != null ? Resources.FindObjectsOfTypeAll(mt) : null;
            if (arr != null)
            {
                for (var i = 0; i < arr.Length; i++)
                {
                    var node = Probe.FieldOf(arr[i], "_waypointNode") as SpriteRenderer;
                    if (node == null) continue;
                    var sp = node.sprite;
                    return "node=" + node.gameObject.name
                        + " sprite=" + (sp != null ? sp.name : "(null)")
                        + " tex=" + (sp != null && sp.texture != null ? sp.texture.name : "?")
                        + " active=" + (node.gameObject.activeInHierarchy ? "1" : "0")
                        + " pos=" + node.transform.position.ToString("0.00");
                }
            }
            var list = WaypointCandidates();
            if (list.Count == 0) return "(no-waypointNode-instance / no-renderer-on-anchor-cell)";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < list.Count && i < 10; i++)
            {
                var s = list[i] != null ? list[i].sprite : null;
                sb.Append("[" + i + "]" + (s != null ? s.name : "(null)") + " ");
            }
            return sb.ToString().TrimEnd();
        }

        // ---- main chain -------------------------------------------------------
        /// <summary>Thrown by Snap() to hold the frame; never an error.</summary>
        private class SnapHold : Exception { }

        private string _snapKey = string.Empty;
        private float _holdUntil;

        private void Update()
        {
            if (_done) return;
            if (Time.unscaledTime < _holdUntil) return;      // a capture is still in flight
            if (Time.unscaledTime - _t0 > 600f) { Probe.Warn("WATCHDOG 600s -> finish"); _done = true; Probe.Done(); return; }
            try { Step(); }
            catch (SnapHold)
            {
                _holdUntil = Time.unscaledTime + 0.5f;
            }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        /// <summary>
        /// The real-mouse Poll overwrites the hover grid every frame, so a forced
        /// hover has to be re-applied each frame to survive long enough for the
        /// production highlight / bar / plate paths to react (same as d2p1).
        /// </summary>
        private void LateUpdate()
        {
            if (_done || !_hoverOn) return;
            ForceHover(_hoverGrid);
        }

        private void Step()
        {
            switch (_step)
            {
                // 0) boot screen: it waits for any key (BootPanel.OnAnyKey -> Events.BootDone)
                case 0:
                    if (Open<Diablo2.UI.BootPanel>())
                    {
                        if (Elapsed(0.5f))
                        {
                            Probe.KV("PANEL-OPEN", "BootPanel fsm=" + Fsm());
                            Snap("panel_boot");
                            Next();
                        }
                        break;
                    }
                    if (Elapsed(2f)) { Probe.KV("BOOT-SKIP", "emit " + Events.BootDone); Probe.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(120f)) { Probe.Warn("boot 120s fsm=" + Fsm()); Next(); }
                    break;

                // 1) main menu
                case 1:
                    if (Open<Diablo2.UI.MainMenuPanel>() || Fsm() == Events.Fsm.StateMainMenu)
                    {
                        if (!Elapsed(1.4f)) break;
                        Probe.KV("PANEL-OPEN", "MainMenuPanel fsm=" + Fsm());
                        Snap("panel_mainmenu");
                        Probe.Emit(Events.Fsm.TriggerNewGame);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { Probe.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(60f)) { Probe.Warn("main menu 60s fsm=" + Fsm()); Next(); }
                    break;

                // 2) char select
                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect)
                    {
                        if (!Elapsed(1.6f)) break;
                        Probe.KV("PANEL-OPEN", "CharSelectPanel fsm=" + Fsm());
                        Snap("panel_charselect");
                        Probe.Emit(Events.Fsm.TriggerNeedCreate);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { Probe.Warn("char select 30s fsm=" + Fsm()); Next(); }
                    break;

                // 3) char create
                case 3:
                    if (Open<Diablo2.UI.CharCreatePanel>() || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(2.0f)) break;
                        Probe.KV("PANEL-OPEN", "CharCreatePanel fsm=" + Fsm());
                        Snap("panel_charcreate");
                        Probe.Emit(Events.Fsm.TriggerCreated);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { Probe.Warn("char create 30s fsm=" + Fsm()); Next(); }
                    break;

                // 4) back on char select -> load the on-disk save (Loading screen is next)
                case 4:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect || Elapsed(25f))
                    {
                        Probe.KV("LOAD-SAVE", "emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        Probe.Emit(Events.CharSelectRequest, _name);
                        Next();
                    }
                    break;

                // 5) loading screen (opportunistic) -> first playable frame
                case 5:
                    if (!_loadingShot && Open<Diablo2.UI.LoadingPanel>())
                    {
                        _loadingShot = true;
                        Snap("panel_loading");
                    }
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(1.6f)) break;
                        Probe.KV("PANEL-OPEN", "HudPanel area=" + Area() + " me=" + Probe.Grid(PlayerGrid()));
                        Snap("panel_hud");
                        Next();
                        break;
                    }
                    if (Elapsed(120f)) { Probe.Warn("stage 120s area=" + Area() + " fsm=" + Fsm()); Next(); }
                    break;

                // 6) InventoryPanel
                case 6:
                    // own frame: two CaptureScreenshot calls in the same frame write one file
                    if (Elapsed(1.0f))
                    {
                        Snap("area_town");
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.InventoryPanel));
                        Next();
                    }
                    break;
                case 7:
                    if (Open<Diablo2.UI.InventoryPanel>())
                    {
                        if (!Elapsed(0.9f)) break;
                        Probe.KV("PANEL-OPEN", "InventoryPanel");
                        ReadCloseButton("Diablo2.UI.InventoryPanel", "inventory");
                        Snap("panel_inventory");
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.InventoryPanel));
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("InventoryPanel 15s not open"); Next(); }
                    break;

                // 8) CharacterPanel + W4
                case 8:
                    Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.CharacterPanel));
                    Next();
                    break;
                case 9:
                    if (Open<Diablo2.UI.CharacterPanel>())
                    {
                        if (!Elapsed(0.9f)) break;
                        Probe.KV("PANEL-OPEN", "CharacterPanel");
                        ReadCloseButton("Diablo2.UI.CharacterPanel", "character");
                        ReadExpBox("Diablo2.UI.CharacterPanel", "char");
                        ReadCharPanelW4();
                        Snap("panel_character");
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.CharacterPanel));
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("CharacterPanel 15s not open"); Next(); }
                    break;

                // 10) SkillTreePanel
                case 10:
                    Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.SkillTreePanel));
                    Next();
                    break;
                case 11:
                    if (Open<Diablo2.UI.SkillTreePanel>())
                    {
                        if (!Elapsed(0.9f)) break;
                        Probe.KV("PANEL-OPEN", "SkillTreePanel");
                        Snap("panel_skilltree");
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.SkillTreePanel));
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("SkillTreePanel 15s not open"); Next(); }
                    break;

                // 12) QuestLogPanel
                case 12:
                    Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.QuestLogPanel));
                    Next();
                    break;
                case 13:
                    if (Open<Diablo2.UI.QuestLogPanel>() || _qlPhase != 0)
                    {
                        // Two states in one step: the empty (NotStarted) body is shot first,
                        // then the quest is taken through the NPC dialog's own option path
                        // (Npc module -> Events.DialogOptionChosen index 1) and the panel is
                        // reopened so the InProgress body is shot too.
                        if (_qlPhase == 1)
                        {
                            Probe.KV("QUEST-STEP", "DialogOptionChosen(1) = quest action, then close dialog");
                            Probe.Emit(Events.DialogOptionChosen, 1);
                            Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
                            _qlPhase = 2;
                            _at = Time.unscaledTime;
                            break;
                        }
                        if (_qlPhase == 2)
                        {
                            Probe.KV("QUEST-STEP", "reopen QuestLogPanel (InProgress)");
                            Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.QuestLogPanel));
                            _qlPhase = 3;
                            _at = Time.unscaledTime;
                            break;
                        }
                        if (_qlPhase == 3)
                        {
                            if (!Elapsed(1.0f)) break;
                            Probe.KV("PANEL-OPEN", "QuestLogPanel state=InProgress");
                            Snap("panel_questlog_progress");
                            _qlPhase = 0;
                            Next();
                            break;
                        }
                        if (!Elapsed(0.9f)) break;
                        Probe.KV("PANEL-OPEN", "QuestLogPanel state=NotStarted");
                        Snap("panel_questlog");
                        {
                            var qn = Probe.CtxMember("Npc");
                            var qm = qn != null ? qn.GetType().GetMethod("Interact", BindingFlags.Public | BindingFlags.Instance) : null;
                            var qr = qm != null ? qm.Invoke(qn, new object[] { 0 }) : null;
                            Probe.KV("QUEST-STEP", "Interact(0) -> production dialog; result="
                                + (qr == null ? "(no-method)" : qr.ToString()));
                        }
                        _qlPhase = 1;
                        _at = Time.unscaledTime;
                        break;
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.QuestLogPanel));
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("QuestLogPanel 15s not open"); Next(); }
                    break;

                // 14) MiniMapPanel
                case 14:
                    Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.MiniMapPanel));
                    Next();
                    break;
                case 15:
                    if (Open<Diablo2.UI.MiniMapPanel>())
                    {
                        if (!Elapsed(1.2f)) break;
                        Probe.KV("PANEL-OPEN", "MiniMapPanel");
                        Snap("panel_minimap");
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.MiniMapPanel));
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("MiniMapPanel 15s not open"); Next(); }
                    break;

                // 16) PausePanel
                case 16:
                    Probe.KV("PAUSE", "emit " + Events.PauseRequest);
                    Probe.Emit(Events.PauseRequest);
                    Next();
                    break;
                case 17:
                    if (Open<Diablo2.UI.PausePanel>())
                    {
                        if (!Elapsed(1.0f)) break;
                        Probe.KV("PANEL-OPEN", "PausePanel fsm=" + Fsm());
                        Snap("panel_pause");
                        Probe.KV("SETTINGS", "open SettingsPanel (same call as the Options button)");
                        Game.UI.Open<Diablo2.UI.SettingsPanel>();
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("PausePanel 15s not open fsm=" + Fsm()); Next(); }
                    break;

                // 18) SettingsPanel
                case 18:
                    if (Open<Diablo2.UI.SettingsPanel>())
                    {
                        if (!Elapsed(2.5f)) break;
                        Probe.KV("PANEL-OPEN", "SettingsPanel");
                        PanelDiag("Diablo2.UI.SettingsPanel");
                        Snap("panel_settings");
                        Game.UI.Close<Diablo2.UI.SettingsPanel>();
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("SettingsPanel 15s not open"); Next(); }
                    break;

                // 19) D2ConfirmPanel
                case 19:
                    Probe.KV("CONFIRM", "D2ConfirmPanel.Show(...)");
                    Diablo2.UI.D2ConfirmPanel.Show("TOUR", "tour panel screenshot", null, null);
                    Next();
                    break;
                case 20:
                    if (Open<Diablo2.UI.D2ConfirmPanel>())
                    {
                        if (!Elapsed(2.5f)) break;
                        Probe.KV("PANEL-OPEN", "D2ConfirmPanel");
                        PanelDiag("Diablo2.UI.D2ConfirmPanel");
                        Snap("panel_confirm");
                        Game.UI.Close<Diablo2.UI.D2ConfirmPanel>();
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("D2ConfirmPanel 15s not open"); Next(); }
                    break;

                // 21) resume to Stage
                case 21:
                    if (Elapsed(0.6f)) { Probe.KV("RESUME", "emit " + Events.ResumeRequest); Probe.Emit(Events.ResumeRequest); Next(); }
                    break;
                case 22:
                    if (Fsm() == Events.Fsm.StateStage && Open<Diablo2.UI.HudPanel>()) { Probe.KV("STAGE-BACK", "fsm=" + Fsm()); Next(); break; }
                    if (Elapsed(20f)) { Probe.Warn("resume 20s fsm=" + Fsm()); Next(); }
                    break;

                // 23) walk next to the town npc (landing inside TalkRange opens the dialog)
                case 23:
                    {
                        var map = Probe.Map();
                        if (map == null || map.NpcPoints == null || map.NpcPoints.Count == 0)
                        { Probe.Warn("town NpcPoints empty -> NpcDialogPanel BLOCKED"); Next(); break; }
                        _npcGrid = map.NpcPoints[0];
                        var t = WalkableRing(_npcGrid, 2);
                        Probe.KV("WALK-NPC", "npc=" + Probe.Grid(_npcGrid) + " walkTo=" + Probe.Grid(t));
                        if (t.x >= 0) Probe.Emit(Events.MoveCommand, t);
                        Next();
                    }
                    break;

                case 24:
                    {
                        var p = PlayerGrid();
                        var d = Mathf.Max(Mathf.Abs(_npcGrid.x - p.x), Mathf.Abs(_npcGrid.y - p.y));
                        if (Open<Diablo2.UI.NpcDialogPanel>())
                        {
                            if (!Elapsed(1.0f)) break;
                            Probe.KV("PANEL-OPEN", "NpcDialogPanel dist=" + d);
                            PanelDiag("Diablo2.UI.NpcDialogPanel");
                            Snap("panel_npcdialog");
                            Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
                            Next();
                            break;
                        }
                        if (!_interactTried && (d <= 3 || Elapsed(20f)))
                        {
                            // production path: the Npc module assembles the DTO and emits
                            // Events.DialogOpen -> HudPanel.OnDialogOpen -> Open<NpcDialogPanel>
                            _interactTried = true;
                            var npcModule = Probe.CtxMember("Npc");
                            var im = npcModule != null ? npcModule.GetType().GetMethod("Interact",
                                BindingFlags.Public | BindingFlags.Instance) : null;
                            var ok = im != null ? im.Invoke(npcModule, new object[] { 0 }) : null;
                            Probe.KV("NPC-INTERACT", "INpcModule.Interact(0) dist=" + d
                                + " module=" + (npcModule == null ? "(null)" : npcModule.GetType().Name)
                                + " result=" + (ok == null ? "(no-method)" : ok.ToString()));
                            _at = Time.unscaledTime;
                            break;
                        }
                        if (_interactTried && Elapsed(6f))
                        {
                            Probe.Warn("NpcDialogPanel still not open after Interact -> BLOCKED for this session");
                            Snap("panel_npcdialog_missing");
                            Next();
                            break;
                        }
                        if (Elapsed(25f)) { Probe.Warn("NpcDialog walk timeout dist=" + d); Next(); }
                    }
                    break;

                // 25) ShopPanel
                case 25:
                    if (Elapsed(0.6f))
                    {
                        var npcMod = Probe.CtxMember("Npc");
                        var gsm = npcMod != null ? npcMod.GetType().GetMethod("GetShop",
                            BindingFlags.Public | BindingFlags.Instance) : null;
                        // Three ways, per the shop hand-off: sweep npcIds (the id used by the
                        // dialog is not guaranteed to be the shopkeeper's).
                        ShopOpenArgs _realShop = null;
                        var tryLog = new System.Text.StringBuilder();
                        for (var sid = 0; sid < 6 && _realShop == null; sid++)
                        {
                            var cand = gsm != null ? gsm.Invoke(npcMod, new object[] { sid }) as ShopOpenArgs : null;
                            tryLog.Append("id" + sid + "=" + (cand == null ? "null"
                                : ((cand.stock != null ? cand.stock.Count : -1) + "items")) + " ");
                            if (cand != null && cand.stock != null && cand.stock.Count > 0) _realShop = cand;
                        }
                        var tryTxt = tryLog.ToString().Trim();
                        var args = new ShopOpenArgs
                        {
                            npcId = 1,
                            npcName = "Charsi",
                            canRepair = true,
                            playerGold = 999,
                            repairAllCost = 12,
                        };
                        args.stock.Add(new ShopEntry { index = 0, itemId = 0, name = "short sword", price = 25, gridW = 1, gridH = 3, count = -1, affordable = true });
                        args.stock.Add(new ShopEntry { index = 1, itemId = 0, name = "buckler", price = 40, gridW = 2, gridH = 2, count = -1, affordable = true });
                        args = _realShop != null ? _realShop : args;
                        var idtxt = new System.Text.StringBuilder();
                        if (args.stock != null)
                        {
                            for (var k = 0; k < args.stock.Count && k < 8; k++)
                            {
                                idtxt.Append(args.stock[k].itemId + (k + 1 < args.stock.Count ? "," : ""));
                            }
                        }
                        Probe.KV("SHOP", "source=" + (_realShop != null ? "INpcModule.GetShop(" + tryTxt + ")" : "(no-shop-data) [" + tryTxt + "]")
                            + " emit " + Events.ShopOpen + " npc=" + args.npcName
                            + " stock=" + (args.stock != null ? args.stock.Count : -1) + " itemIds=[" + idtxt + "]");
                        Probe.Emit(Events.ShopOpen, args);
                        Next();
                    }
                    break;
                case 26:
                    if (Open<Diablo2.UI.ShopPanel>())
                    {
                        if (!Elapsed(1.0f)) break;
                        Probe.KV("PANEL-OPEN", "ShopPanel");
                        Snap("panel_shop");
                        Game.UI.Close<Diablo2.UI.ShopPanel>();
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("ShopPanel 15s not open"); Next(); }
                    break;

                // 27) DeathPanel
                case 27:
                    if (Elapsed(0.6f)) { Probe.KV("DEATH", "emit " + Events.PlayerDied); Probe.Emit(Events.PlayerDied); Next(); }
                    break;
                case 28:
                    if (Open<Diablo2.UI.DeathPanel>())
                    {
                        if (!Elapsed(1.2f)) break;
                        Probe.KV("PANEL-OPEN", "DeathPanel");
                        ReadNodesMatching("Diablo2.UI.DeathPanel", "anner", "death-banner");
                        ReadNodesMatching("Diablo2.UI.DeathPanel", "", "death-all");
                        Snap("panel_death");
                        Probe.KV("REVIVE", "emit " + Events.Revived);
                        Probe.Emit(Events.Revived);
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("DeathPanel 15s not open"); Next(); }
                    break;
                case 29:
                    if (!Open<Diablo2.UI.DeathPanel>() || Elapsed(8f)) { Probe.KV("DEATH-CLOSED", "open=" + Open<Diablo2.UI.DeathPanel>()); Next(); }
                    break;

                // 30) walk onto the waypoint anchor (31,26) -> WaypointPanel opens
                case 30:
                    {
                        var map = Probe.Map();
                        if (map == null || map.WaypointPoints == null || map.WaypointPoints.Count == 0)
                        { Probe.Warn("no waypoint anchor -> WaypointPanel BLOCKED"); Next(); break; }
                        var a = map.WaypointPoints[0];
                        Probe.KV("WALK-WP", "anchor=" + Probe.Grid(a) + " emit " + Events.MoveCommand);
                        Probe.Emit(Events.MoveCommand, a);
                        Next();
                    }
                    break;
                case 31:
                    if (Open<Diablo2.UI.WaypointPanel>())
                    {
                        if (!Elapsed(1.2f)) break;
                        Probe.KV("PANEL-OPEN", "WaypointPanel me=" + Probe.Grid(PlayerGrid()));
                        Snap("panel_waypoint");
                        Next();
                        break;
                    }
                    if (Elapsed(25f)) { Probe.Warn("WaypointPanel 25s not open me=" + Probe.Grid(PlayerGrid())); Next(); }
                    break;
                case 32:
                    if (Elapsed(0.5f)) { Game.UI.Close<Diablo2.UI.WaypointPanel>(); Next(); }
                    break;
                // U32: the anchor node's sprite, twice, frames apart (8-frame anim must move)
                case 33:
                    if (Elapsed(0.6f))
                    {
                        _wpFrameA = WaypointSprite();
                        Probe.KV("U32-ANCHOR-A", "sprite=" + _wpFrameA + " frame=" + Time.frameCount);
                        Next();
                    }
                    break;
                case 34:
                    if (Elapsed(0.45f))
                    {
                        var b = WaypointSprite();
                        Probe.KV("U32-ANCHOR-B", "sprite=" + b + " frame=" + Time.frameCount);
                        Probe.KV("U32-ANCHOR", "A=" + _wpFrameA + " B=" + b + " moved=" + (_wpFrameA == b ? "NO" : "YES"));
                        Snap("town_waypoint");
                        Next();
                    }
                    break;

                // 35) BloodMoor
                case 35:
                    Probe.KV("EXIT-MOOR", "emit " + Events.ExitEntered + "(BloodMoor)");
                    Probe.Emit(Events.ExitEntered, AreaId.BloodMoor);
                    Next();
                    break;
                case 36:
                    if (Area() == (int)AreaId.BloodMoor && Open<Diablo2.UI.HudPanel>() && Elapsed(2.0f))
                    {
                        Probe.KV("AREA", "BloodMoor me=" + Probe.Grid(PlayerGrid()));
                        Snap("area_bloodmoor");
                        Next();
                        break;
                    }
                    if (Elapsed(45f)) { Probe.Warn("BloodMoor 45s area=" + Area()); Next(); }
                    break;

                // 37) N1: the production [Hover] first-hit line must be a MONSTER body
                case 37:
                    {
                        var mon = Probe.Monsters();
                        var p = PlayerGrid();
                        var best = -1; var bd = int.MaxValue; var bx = -1; var by = -1;
                        if (mon != null && mon.All != null)
                        {
                            for (var i = 0; i < mon.All.Count; i++)
                            {
                                var m = mon.All[i];
                                if (m == null || !m.alive) continue;
                                var d = Mathf.Max(Mathf.Abs(m.gridX - p.x), Mathf.Abs(m.gridY - p.y));
                                if (AliveNear(new Vector2Int(m.gridX, m.gridY), 1) != 1) continue;
                                if (d < bd) { bd = d; best = m.id; bx = m.gridX; by = m.gridY; }
                            }
                        }
                        if (best < 0)
                        {
                            Probe.Warn("no isolated live monster -> N1 monster hover BLOCKED");
                            Next(); break;
                        }
                        _monsterId = best; _monsterX = bx; _monsterY = by;
                        _hoverGrid = new Vector2Int(bx, by);
                        Probe.KV("MOB-PICK", "m#" + best + " grid=(" + bx + "," + by + ") dist=" + bd);
                        ArmHoverLog();
                        _hoverOn = true;
                        ForceHover(_hoverGrid);
                        LogHoverState("N1-HOVER", _hoverGrid);
                        Next();
                    }
                    break;
                case 38:
                    if (Elapsed(0.7f))
                    {
                        ForceHover(_hoverGrid);
                        LogHoverState("N1-HOVER-2", _hoverGrid);
                        var w = Iso.GridToWorld(_hoverGrid);
                        var sr = W1Renderer();
                        Probe.KV("W1-MAT-HOVERED", "renderer=" + (sr == null ? "(none)" : sr.name)
                            + " material=" + Probe.MatName(sr) + " " + Probe.Props(sr));
                        var c = Probe.FindType("Diablo2.UI.EnemyBarView");
                        if (c != null)
                        {
                            var inst = c.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                            var view = inst != null ? inst.GetValue(null) : null;
                            if (view != null)
                            {
                                var nm = c.GetProperty("NameplateText", BindingFlags.Public | BindingFlags.Instance).GetValue(view) as string;
                                Probe.KV("W9-PLATE-TEXT", "\"" + nm + "\" visible="
                                    + c.GetProperty("NameplateVisible", BindingFlags.Public | BindingFlags.Instance).GetValue(view)
                                    + " (monster hover -> bar, plate empty = expected)");
                            }
                        }
                        Snap("mob_hover");
                        Next();
                    }
                    break;

                // 39) W1 numeric half: material before / during / after the hover
                case 39:
                    if (Elapsed(0.5f))
                    {
                        var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
                        var sr = W1Renderer();
                        Probe.KV("W1-MAT-A", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr));
                        Next();
                    }
                    break;
                case 40:
                    if (Elapsed(0.5f))
                    {
                        _hoverOn = false;
                        // move the pointer off the monster (same camera, same sprite)
                        ForceHover(PlayerGrid());
                        LogHoverState("W1-HOVER-OFF", PlayerGrid());
                        var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
                        var sr = W1Renderer();
                        Probe.KV("W1-MAT-B", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr));
                        Next();
                    }
                    break;

                // 41) W1 pixel half: freeze time, capture the same frame twice
                case 41:
                    if (Elapsed(0.6f))
                    {
                        Time.timeScale = 0f;
                        Probe.KV("W1-FREEZE", "timeScale=0 frame=" + Time.frameCount);
                        Next();
                    }
                    break;
                case 42:
                    if (Elapsed(1.0f))
                    {
                        _hoverOn = false;
                        ForceHover(PlayerGrid());
                        var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
                        var sr = W1Renderer();
                        Probe.KV("W1-FRAME-A", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr) + " grid=" + Probe.Grid(PlayerGrid()));
                        Snap("w1_before");
                        Next();
                    }
                    break;
                case 43:
                    if (Elapsed(1.0f))
                    {
                        _hoverOn = true;
                        ForceHover(_hoverGrid);
                        var w = Iso.GridToWorld(_hoverGrid);
                        var sr = W1Renderer();
                        Probe.KV("W1-FRAME-HOVER", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr));
                        Next();
                    }
                    break;
                case 44:
                    if (Elapsed(1.0f))
                    {
                        _hoverOn = false;
                        ForceHover(PlayerGrid());
                        var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
                        var sr = W1Renderer();
                        Probe.KV("W1-FRAME-B", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr) + " grid=" + Probe.Grid(PlayerGrid()));
                        Next();
                    }
                    break;
                case 45:
                    if (Elapsed(1.0f))
                    {
                        var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
                        var sr = W1Renderer();
                        Probe.KV("W1-FRAME-B2", "material=" + Probe.MatName(sr) + " " + Probe.Props(sr));
                        Snap("w1_after");
                        Time.timeScale = 1f;
                        Probe.KV("W1-UNFREEZE", "timeScale=1 frame=" + Time.frameCount);
                        Next();
                    }
                    break;

                // 46) N2: monster walk-up (movement frames) + hit frame
                case 46:
                    if (Elapsed(0.5f))
                    {
                        Probe.KV("WALK-MOB", "emit " + Events.MoveCommand + "(" + _monsterX + "," + _monsterY + ")");
                        Probe.Emit(Events.MoveCommand, new Vector2Int(_monsterX, _monsterY));
                        Next();
                    }
                    break;
                case 47:
                    {
                        var mon = Probe.Monsters();
                        var m = mon != null ? mon.Get(_monsterId) : null;
                        if (m == null || !m.alive) { Probe.KV("N2-SKIP", "target gone"); Next(); break; }
                        var p = PlayerGrid();
                        var d = Mathf.Max(Mathf.Abs(m.gridX - p.x), Mathf.Abs(m.gridY - p.y));
                        if (d <= 2 || Elapsed(20f))
                        {
                            Probe.KV("N2-READY", "dist=" + d + " hp=" + m.hp + "/" + m.maxHp);
                            Snap("mob_move");
                            Next();
                            break;
                        }
                    }
                    break;
                case 48:
                    if (Elapsed(0.3f))
                    {
                        var commbatOk = Probe.Combat() != null;
                        Probe.KV("N2-ATTACK", "RequestAttack m#" + _monsterId + " combat=" + commbatOk);
                        if (commbatOk) Probe.Combat().RequestAttack(_monsterId);
                        Next();
                    }
                    break;
                case 49:
                    if (Elapsed(0.25f))
                    {
                        var mon = Probe.Monsters();
                        var m = mon != null ? mon.Get(_monsterId) : null;
                        Probe.KV("N2-HIT", "hp=" + (m != null ? m.hp + "/" + m.maxHp : "?") + " alive=" + (m != null && m.alive ? "1" : "0"));
                        Snap("mob_hit");
                        Next();
                    }
                    break;
                case 50:
                    if (Elapsed(0.22f)) { Snap("mob_hit2"); Next(); }
                    break;

                // 51) DenOfEvil
                case 51:
                    Probe.KV("EXIT-DEN", "emit " + Events.ExitEntered + "(DenOfEvil)");
                    Probe.Emit(Events.ExitEntered, AreaId.DenOfEvil);
                    Next();
                    break;
                case 52:
                    if (Area() == (int)AreaId.DenOfEvil && Open<Diablo2.UI.HudPanel>() && Elapsed(2.0f))
                    {
                        Probe.KV("AREA", "DenOfEvil me=" + Probe.Grid(PlayerGrid()));
                        Snap("area_den");
                        Next();
                        break;
                    }
                    if (Elapsed(45f)) { Probe.Warn("DenOfEvil 45s area=" + Area()); Next(); }
                    break;

                // 53) back to town for N4 (inventory drag ghost + drop-cell highlight)
                case 53:
                    Probe.KV("EXIT-TOWN", "emit " + Events.ExitEntered + "(Town)");
                    Probe.Emit(Events.ExitEntered, AreaId.Town);
                    Next();
                    break;
                case 54:
                    if (Area() == (int)AreaId.Town && Open<Diablo2.UI.HudPanel>() && Elapsed(2.0f))
                    {
                        Probe.KV("TOWN-BACK", "me=" + Probe.Grid(PlayerGrid()));
                        Probe.Emit(Events.PanelToggleRequest, nameof(Diablo2.UI.InventoryPanel));
                        Next();
                        break;
                    }
                    if (Elapsed(45f)) { Probe.Warn("Town 45s area=" + Area()); Next(); }
                    break;
                case 55:
                    if (Open<Diablo2.UI.InventoryPanel>())
                    {
                        if (!Elapsed(0.9f)) break;
                        Probe.KV("N4-TOWN-SHOT", "inventory open in town");
                        Snap("inventory_town");
                        Next();
                        break;
                    }
                    if (Elapsed(15f)) { Probe.Warn("inventory 15s not open (town)"); Next(); }
                    break;

                // 56) N4: drag ghost + drop-cell highlight (production OnBeginDrag/OnDrag)
                case 56:
                    if (Elapsed(0.6f))
                    {
                        var root = Probe.ActiveRoot("Diablo2.UI.InventoryPanel");
                        if (root == null) { Probe.Warn("N4 inventory root missing"); Next(); break; }
                        var panel = root.GetComponent(Probe.FindType("Diablo2.UI.InventoryPanel")) as MonoBehaviour;
                        if (panel == null) { Probe.Warn("N4 panel component missing"); Next(); break; }
                        var occupied = Probe.FieldOf(panel, "_cellIconPath") as string[];
                        var anchor = -1;
                        if (occupied != null)
                        {
                            for (var i = 0; i < occupied.Length; i++) { if (!string.IsNullOrEmpty(occupied[i])) { anchor = i; break; } }
                        }
                        Probe.KV("N4-ANCHOR", "first occupied cell index=" + anchor);
                        var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                        ev.position = ScreenPointOfCell(panel, anchor);
                        Probe.KV("N4-DRAG-PT", "screen=" + ev.position + " realMouse=" + (Game.Input != null ? Game.Input.MousePosition.ToString() : "(no-input)"));
                        panel.GetType().GetMethod("OnBeginDrag").Invoke(panel, new object[] { ev });
                        var g0 = Probe.FieldOf(panel, "_ghost") as UnityEngine.UI.Image;
                        Probe.KV("N4-AFTER-BEGINDRAG", "dragAnchor=" + Probe.FieldOf(panel, "_dragAnchor")
                            + " ghostActive=" + (g0 != null && g0.gameObject.activeSelf ? "1" : "0"));
                        panel.GetType().GetMethod("OnDrag").Invoke(panel, new object[] { ev });
                        Next();
                    }
                    break;
                case 57:
                    if (Elapsed(0.6f))
                    {
                        var root = Probe.ActiveRoot("Diablo2.UI.InventoryPanel");
                        var panel = root != null ? root.GetComponent(Probe.FindType("Diablo2.UI.InventoryPanel")) as MonoBehaviour : null;
                        if (panel != null)
                        {
                            var ghost = Probe.FieldOf(panel, "_ghost") as UnityEngine.UI.Image;
                            var hl = Probe.FieldOf(panel, "_dropHighlight") as UnityEngine.UI.Image;
                            Probe.KV("N4-STATE", "ghostActive=" + (ghost != null && ghost.gameObject.activeSelf ? "1" : "0")
                                + " ghostColor=" + (ghost != null ? ghost.color.ToString() : "-")
                                + " highlightActive=" + (hl != null && hl.gameObject.activeSelf ? "1" : "0")
                                + " highlightAnchorPos=" + (hl != null ? hl.rectTransform.anchoredPosition.ToString() : "-")
                                + " loggedDropCell=" + Probe.FieldOf(panel, "_loggedDropCell"));
                            Snap("inventory_drag");
                        }
                        Next();
                    }
                    break;
                case 58:
                    if (Elapsed(0.5f))
                    {
                        var root = Probe.ActiveRoot("Diablo2.UI.InventoryPanel");
                        var panel = root != null ? root.GetComponent(Probe.FindType("Diablo2.UI.InventoryPanel")) as MonoBehaviour : null;
                        if (panel != null)
                        {
                            var ev = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
                            panel.GetType().GetMethod("OnEndDrag").Invoke(panel, new object[] { ev });
                            Probe.KV("N4-ENDDRAG", "ghost/highlight hidden");
                        }
                        Next();
                    }
                    break;
                case 59:
                    if (Elapsed(0.5f))
                    {
                        if (Open<Diablo2.UI.InventoryPanel>()) Game.UI.Close<Diablo2.UI.InventoryPanel>();
                        Next();
                    }
                    break;

                // 60) nameplate half of W9: hover the town npc so the black bar is on screen
                case 60:
                    if (Elapsed(0.6f))
                    {
                        var map = Probe.Map();
                        if (map == null || map.NpcPoints == null || map.NpcPoints.Count == 0)
                        { Probe.Warn("W9 no NPC -> BLOCKED"); Next(); break; }
                        var g = map.NpcPoints[0];
                        ForceHover(g);
                        LogHoverState("W9-HOVER-NPC", g);
                        Next();
                    }
                    break;
                case 61:
                    if (Elapsed(0.9f))
                    {
                        ForceHover(new Vector2Int(_npcGrid.x, _npcGrid.y));
                        ReadNameplate("npc");
                        Snap("nameplate_npc");
                        Next();
                    }
                    break;
                case 62:
                    if (Elapsed(0.4f)) { Snap("nameplate_npc2"); Next(); }
                    break;

                default:
                    Probe.KV("SHOTS-TOTAL", _shotCount.ToString());
                    _done = true;
                    Probe.Done();
                    break;
            }
        }

        /// <summary>
        /// The hovered entity's renderer, identified POSITIVELY while the highlight is
        /// live: exactly one renderer carries the shared `D2EntityHighlight` material
        /// during a hover. Falling back to "nearest renderer to the monster grid" can
        /// pick a tile (a tile sits at the same world point), so the material path is
        /// the authoritative one and the fallback is reported as such.
        /// </summary>
        private SpriteRenderer W1Renderer()
        {
            var hit = HighlightRenderer();
            if (hit != null) return hit;
            var w = Iso.GridToWorld(new Vector2Int(_monsterX, _monsterY));
            return Probe.NearestRenderer(w, 1.6f);
        }

        private SpriteRenderer _hlRenderer;
        private SpriteRenderer HighlightRenderer()
        {
            if (_hlRenderer != null) return _hlRenderer;
            var all = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
            if (all == null) return null;
            for (var i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null || sr.sharedMaterial == null) continue;
                if (sr.sharedMaterial.name == "D2EntityHighlight") { _hlRenderer = sr; return sr; }
            }
            return null;
        }

        /// <summary>
        /// The character sheet's experience box: located by CONTENT (the live D2Label whose
        /// text starts with the box's own prefix), so no node name is assumed. Reports the
        /// string, its length, the label's own rendered line count and the box width -- the
        /// runtime half of the "10-digit experience still fits on one line" claim.
        /// </summary>
        private void ReadExpBox(string panelType, string tag)
        {
            var root = Probe.ActiveRoot(panelType);
            if (root == null) { Probe.KV("EXPBOX-" + tag, "no-active-root"); return; }
            // Authoritative source = the uGUI Text the panel writes into
            // (CharacterPanel.cs:145 `UiArt.Label(..., "Band2Right", ...)` / the only
            // write site is :375 `经验 {exp}`).
            var node = Probe.FindDeep(root, "Band2Right");
            if (node == null) { Probe.KV("EXPBOX-" + tag, "no-Band2Right-node"); return; }
            var rt = node as RectTransform;
            var ugui = node.GetComponent<UnityEngine.UI.Text>();
            var uguiText = ugui != null ? ugui.text : "(no-ugui-Text)";
            var mirrorText = "(none-under)";
            var mirrorLines = -1;
            var mirrorN = 0;
            var labels = Probe.Labels();
            for (var i = 0; i < labels.Count; i++)
            {
                var lr = Probe.LabelRoot(labels[i]);
                if (lr == null || !Probe.Under(lr, node)) continue;
                mirrorN++;
                mirrorText = Probe.LabelText(labels[i]);
                mirrorLines = Probe.LabelLines(labels[i]);
            }
            Probe.KV("EXPBOX-" + tag,
                "node=Band2Right uguiText=\"" + uguiText + "\""
                + " uguiDigits=" + (uguiText != null && uguiText.Length > 3 ? (uguiText.Length - 3) : 0)
                + " mirrorLabelsUnder=" + mirrorN + " mirrorText=\"" + mirrorText + "\" mirrorLines=" + mirrorLines
                + " boxW=" + (rt != null ? rt.rect.width.ToString("0.#") : "?")
                + " sizeDeltaW=" + (rt != null ? rt.sizeDelta.x.ToString("0.#") : "?")
                + " " + ImageRectInPx(rt));
        }

        /// <summary>RectTransform rect as image pixels (top-left origin) for a sheet crop.</summary>
        private static string ImageRectInPx(RectTransform rt)
        {
            if (rt == null) return string.Empty;
            var c0 = rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMin, 0f));
            var c1 = rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.yMax, 0f));
            var x = Mathf.Min(c0.x, c1.x);
            var y = Mathf.Min(c0.y, c1.y);
            var w = Mathf.Abs(c1.x - c0.x);
            var h = Mathf.Abs(c1.y - c0.y);
            return "img=" + x.ToString("0.#") + "," + (Screen.height - y - h).ToString("0.#")
                + "," + w.ToString("0.#") + "x" + h.ToString("0.#");
        }

        /// <summary>
        /// Close button truth: the sprite that is actually on the node at runtime, its tint,
        /// the button's two-state wiring (pressed/highlighted), the hit rect, and whether any
        /// live D2Label sits inside that rect (the "no permanent text on the button face"
        /// claim). The expectation string is printed next to the reading, not compared here.
        /// </summary>
        private void ReadCloseButton(string panelType, string tag)
        {
            var root = Probe.ActiveRoot(panelType);
            if (root == null) { Probe.KV("CLOSEBTN-" + tag, "no-active-root"); return; }
            var node = Probe.FindDeep(root, "CloseButton");
            if (node == null) { Probe.KV("CLOSEBTN-" + tag, "no-CloseButton-node under " + panelType); return; }
            var img = node.GetComponent<UnityEngine.UI.Image>();
            var btn = node.GetComponent<UnityEngine.UI.Button>();
            var rt = node as RectTransform;
            var sp = img != null ? img.sprite : null;

            var inside = 0;
            var texts = new System.Text.StringBuilder();
            var labels = Probe.Labels();
            for (var i = 0; i < labels.Count; i++)
            {
                var lr = Probe.LabelRoot(labels[i]);
                if (lr == null || !Probe.Under(lr, node)) continue;
                var lrt = lr as RectTransform;
                if (lrt == null || !RectOverlaps(lrt, rt, rt != null ? rt.rect : new Rect())) continue;
                inside++;
                if (texts.Length < 120) texts.Append("«" + Probe.LabelText(labels[i]) + "» ");
            }

            // ControlTip visibility: driven by uGUI pointer-enter (dispatched natively
            // below -- this is NOT a real OS cursor move, so say so in the report).
            var tip = Probe.FindDeep(root, "ControlTip");
            var tipRt = tip as RectTransform;
            var tipBefore = tip != null && tip.gameObject.activeSelf;
            DispatchPointerEnter(node);
            var tipAfter = tip != null && tip.gameObject.activeSelf;
            var tipInfo = "tipNode=" + (tip != null ? "present" : "absent")
                + " activeBefore=" + tipBefore + " activeAfter=" + tipAfter
                + " rect=" + (tipRt != null
                    ? tipRt.rect.width.ToString("0.#") + "x" + tipRt.rect.height.ToString("0.#")
                      + " anch=" + tipRt.anchoredPosition.ToString("0.#")
                    : "?")
                + " btnTopCenter=" + (rt != null ? (rt.anchoredPosition.x).ToString("0.#") + "," + (rt.anchoredPosition.y + rt.rect.height * 0.5f).ToString("0.#") : "?");
            DispatchPointerExit(node);

            Probe.KV("CLOSEBTN-" + tag,
                "node=" + node.name
                + " sprite=" + (sp != null ? sp.name : "(null)")
                + " tex=" + (sp != null && sp.texture != null ? sp.texture.name : "?")
                + " expect=D2/UI/Panel/buysellbtn_10"
                + " color=" + (img != null ? img.color.ToString() : "-")
                + " alpha=" + (img != null ? img.color.a.ToString("0.###") : "-")
                + " raycast=" + (img != null ? img.raycastTarget.ToString() : "-")
                + " transition=" + (btn != null ? btn.transition.ToString() : "(no-button)")
                + " pressed=" + (btn != null && btn.spriteState.pressedSprite != null ? btn.spriteState.pressedSprite.name : "(none)")
                + " highlighted=" + (btn != null && btn.spriteState.highlightedSprite != null ? btn.spriteState.highlightedSprite.name : "(none)")
                + " interactable=" + (btn != null ? btn.interactable.ToString() : "-")
                + " rect=" + (rt != null ? rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#") : "?")
                + " localPos=" + (rt != null ? rt.anchoredPosition.ToString("0.#") : "?")
                + " " + ImageRectInPx(rt)
                + " labelsInside=" + inside + (inside > 0 ? " " + texts.ToString().Trim() : "")
                + " " + tipInfo);
        }

        /// <summary>
        /// uGUI NATIVE pointer dispatch (EventSystem + PointerEventData + ExecuteEvents).
        /// It is NOT a real OS cursor move, so ControlTip visibility proven this way has
        /// to be reported as "native dispatch", not as "hovered with the mouse".
        /// </summary>
        private static void DispatchPointerEnter(Transform t)
        {
            Dispatch(t, true);
        }

        private static void DispatchPointerExit(Transform t)
        {
            Dispatch(t, false);
        }

        private static void Dispatch(Transform t, bool enter)
        {
            try
            {
                var rt = t as RectTransform;
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (rt == null || es == null) { Probe.Warn("pointer-dispatch skipped (rt/es null)"); return; }
                var canvas = rt.GetComponentInParent<Canvas>();
                var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? (canvas.worldCamera != null ? canvas.worldCamera : Camera.main) : null;
                var pd = new UnityEngine.EventSystems.PointerEventData(es);
                pd.position = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
                pd.pointerCurrentRaycast = new UnityEngine.EventSystems.RaycastResult { gameObject = rt.gameObject };
                if (enter)
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(rt.gameObject, pd,
                        UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
                }
                else
                {
                    UnityEngine.EventSystems.ExecuteEvents.Execute(rt.gameObject, pd,
                        UnityEngine.EventSystems.ExecuteEvents.pointerExitHandler);
                }
            }
            catch (Exception ex) { Probe.Warn("pointer-dispatch fail " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>Every active RectTransform under a panel whose name contains `needle`.</summary>
        private void ReadNodesMatching(string panelType, string needle, string tag)
        {
            var root = Probe.ActiveRoot(panelType);
            if (root == null) { Probe.KV("NODE-" + tag, "no-active-root"); return; }
            var all = root.GetComponentsInChildren<RectTransform>(true);
            var n = 0;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < all.Length; i++)
            {
                var rt2 = all[i];
                if (rt2 == null || !rt2.gameObject.activeInHierarchy) continue;
                if (rt2.name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                if (n <= 6)
                {
                    sb.Append("[" + n + "]" + rt2.name
                        + " rect=" + rt2.rect.width.ToString("0.#") + "x" + rt2.rect.height.ToString("0.#")
                        + " sizeDelta=" + rt2.sizeDelta.x.ToString("0.#") + "x" + rt2.sizeDelta.y.ToString("0.#")
                        + " children=" + rt2.childCount + " " + ImageRectInPx(rt2) + " ");
                }
            }
            Probe.KV("NODE-" + tag, needle + " matches=" + n + " " + sb.ToString().Trim());
        }

        private static bool RectOverlaps(RectTransform label, RectTransform btn, Rect btnRect)
        {
            if (label == null || btn == null) return false;
            var lb = label.TransformPoint(new Vector3(label.rect.xMin, label.rect.yMin, 0f));
            var lt = label.TransformPoint(new Vector3(label.rect.xMax, label.rect.yMax, 0f));
            var bx = btn.TransformPoint(new Vector3(btnRect.xMin, btnRect.yMin, 0f));
            var bt = btn.TransformPoint(new Vector3(btnRect.xMax, btnRect.yMax, 0f));
            var lx0 = Mathf.Min(lb.x, lt.x); var lx1 = Mathf.Max(lb.x, lt.x);
            var ly0 = Mathf.Min(lb.y, lt.y); var ly1 = Mathf.Max(lb.y, lt.y);
            var bx0 = Mathf.Min(bx.x, bt.x); var bx1 = Mathf.Max(bx.x, bt.x);
            var by0 = Mathf.Min(bx.y, bt.y); var by1 = Mathf.Max(bx.y, bt.y);
            return lx0 < bx1 && lx1 > bx0 && ly0 < by1 && ly1 > by0;
        }

        /// <summary>Panel geometry / layer diagnostics (why a panel may not be visible).</summary>
        private void PanelDiag(string typeName)
        {
            var root = Probe.ActiveRoot(typeName);
            if (root == null) { Probe.KV("DIAG-" + typeName, "root=(none-active)"); return; }
            var c = root.GetComponent(Probe.FindType(typeName));
            var layer = c != null ? c.GetType().GetProperty("Layer", BindingFlags.Public | BindingFlags.Instance) : null;
            var canvas = root.GetComponentInParent<Canvas>();
            var rt = root as RectTransform;
            Probe.KV("DIAG-" + typeName,
                "active=" + root.gameObject.activeInHierarchy
                + " layer=" + (layer != null ? layer.GetValue(c).ToString() : "(no-layer)")
                + " canvas=" + (canvas != null ? canvas.name + "/sortingOrder=" + canvas.sortingOrder
                    + "/override=" + (canvas.overrideSorting ? "1" : "0") : "(no-canvas)")
                + " rect=" + (rt != null ? rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#") : "?")
                + " children=" + root.childCount
                + " sibling=" + root.GetSiblingIndex());
        }

        /// <summary>Screen point of an inventory grid cell (production layout: CellAt's inverse).</summary>
        private static Vector2 ScreenPointOfCell(MonoBehaviour panel, int cell)
        {
            try
            {
                var rt = panel.transform as RectTransform;
                var node = cell >= 0 ? Probe.FindDeep(rt, "Cell" + cell) : null;
                Vector3 world;
                if (node != null)
                {
                    world = node.position;      // exact: the cell node's own world position
                }
                else
                {
                    var m = panel.GetType().GetMethod("CellCenter",
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                    var c = m != null && cell >= 0 ? (Vector2)m.Invoke(panel, new object[] { cell }) : Vector2.zero;
                    world = rt.TransformPoint(new Vector3(c.x, c.y, 0f));
                }
                var canvas = rt.GetComponentInParent<Canvas>();
                // Screen Space Overlay: a child's world position already IS screen pixels.
                if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                    return new Vector2(world.x, world.y);
                var cam = canvas != null && canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
                if (cam == null) return new Vector2(world.x, world.y);
                return RectTransformUtility.WorldToScreenPoint(cam, world);
            }
            catch (Exception ex)
            {
                Probe.Warn("N4 cellpoint-fail " + ex.GetType().Name);
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }
        }
    }
}

// =============================================================================
// ShopTip: shop-grid hover evidence (driver for `unity command run_script`).
//
//   spec = "<charName>|<done marker>|<shot dir>"
//
//   chain: boot -> main menu -> char select -> load save -> stage -> open shop
//          -> shot with the pointer parked away (no tooltip)
//          -> pin the pointer onto cell 0 (buy page) -> log the tooltip text -> shot
//          -> click tab 1 (sell page) -> log the tooltip text -> shot -> TOUR-DONE
//
//   Why a fake input manager: the shop panel's hover test reads
//   `Game.Input.MousePosition`, so a hover state cannot be produced offline and a
//   real cursor cannot be aimed at a game-screen point from a driver. The engine's
//   public seam `CloverEngine.Game.AttachInput(IInputManager)` lets the driver
//   report a fixed pointer, which makes the hover state deterministic.
// =============================================================================
namespace P2Shop
{
    using System;
    using System.Collections;
    using System.Reflection;
    using CloverEngine;
    using Diablo2.Core;
    using Diablo2.Def;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>Input manager with the real behaviour except the reported pointer.</summary>
    internal sealed class FixedPointerInput : IInputManager
    {
        private readonly IInputManager _real;

        public Vector3 Pointer;

        public FixedPointerInput(IInputManager real) { _real = real; }

        public InputState State { get { return _real.State; } }
        public bool Available { get { return true; } }
        public string BackendName { get { return _real.BackendName; } }
        public bool IsLocked { get { return _real.IsLocked; } }
        public bool HasTouch { get { return false; } }
        public bool PointerOverUi { get { return _real.PointerOverUi; } }
        public void Lock() { _real.Lock(); }
        public void Unlock() { _real.Unlock(); }
        public bool GetKey(GameKey key) { return _real.GetKey(key); }
        public bool GetKeyDown(GameKey key) { return _real.GetKeyDown(key); }
        public bool GetKeyUp(GameKey key) { return _real.GetKeyUp(key); }
        public bool GetMouseButton(int button) { return _real.GetMouseButton(button); }
        public bool GetMouseButtonDown(int button) { return _real.GetMouseButtonDown(button); }
        public bool GetMouseButtonUp(int button) { return _real.GetMouseButtonUp(button); }
        public Vector3 MousePosition { get { return Pointer; } }
        public Vector2 MouseDelta { get { return Vector2.zero; } }
        public float GetAxis(string axis, bool raw) { return _real.GetAxis(axis, raw); }
        public void OnMove(Action<Vector2> h) { _real.OnMove(h); }
        public void OffMove(Action<Vector2> h) { _real.OffMove(h); }
        public void OnSkill(int i, Action h) { _real.OnSkill(i, h); }
        public void OffSkill(int i, Action h) { _real.OffSkill(i, h); }
        public void OnJump(Action h) { _real.OnJump(h); }
        public void OffJump(Action h) { _real.OffJump(h); }
        public void OnDodge(Action h) { _real.OnDodge(h); }
        public void OffDodge(Action h) { _real.OffDodge(h); }
        public void OnInteract(Action h) { _real.OnInteract(h); }
        public void OffInteract(Action h) { _real.OffInteract(h); }
        public void EnsureEventSystem() { _real.EnsureEventSystem(); }
        public void Tick() { _real.Tick(); }
    }

    /// <summary>Callable entry for the runner (run_script --entry).</summary>
    public static class ShopTip
    {
        public static string Install(string spec)
        {
            var go = new GameObject("ShopTipDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<ShopDriver>();
            d.Init(spec ?? string.Empty);
            P2.Probe.Log("SHOPTIP-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain: reach the town, open the shop, hover a grid cell on both pages.</summary>
    public class ShopDriver : MonoBehaviour
    {
        private const string PanelType = "Diablo2.UI.ShopPanel";

        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _swapped;
        private FixedPointerInput _fake;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) P2.Probe.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            P2.Probe.Log("SHOPTIP-INIT char=" + _name + " shots=" + _shotDir);
        }

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        private static bool Open<T>() where T : class, CloverEngine.IUIPanel
        {
            return Game.UI != null && Game.UI.IsOpen<T>();
        }

        private void Snap(string leaf)
        {
            if (string.IsNullOrEmpty(_shotDir)) { P2.Probe.Warn("SNAP-NO-DIR " + leaf); return; }
            P2.Probe.Log("SNAP " + leaf + " " + P2.Probe.Shot(_shotDir + "/" + leaf + ".png"));
        }

        private static object FieldOf(object inst, string name)
        {
            if (inst == null) return null;
            var f = inst.GetType().GetField(name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(inst) : null;
        }

        private static Component Panel()
        {
            var t = P2.Probe.FindType(PanelType);
            if (t == null) return null;
            var all = Resources.FindObjectsOfTypeAll(t);
            for (var i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c != null && c.gameObject.activeInHierarchy) return c;
            }
            return null;
        }

        /// <summary>Log the live tooltip content (name / body incl. the price line).</summary>
        private void ReadTip(string tag)
        {
            var panel = Panel();
            if (panel == null) { P2.Probe.KV(tag, "panel=null"); return; }

            var tip = FieldOf(panel, "_tooltip");
            if (tip == null) { P2.Probe.KV(tag, "tooltip=null"); return; }

            var visProp = tip.GetType().GetProperty("IsVisible", BindingFlags.Public | BindingFlags.Instance);
            var vis = visProp != null ? visProp.GetValue(tip) : "?";
            var title = FieldOf(tip, "_title") as Text;
            var body = FieldOf(tip, "_body") as Text;
            var t = title != null ? title.text : "(no-title)";
            var b = body != null ? body.text : "(no-body)";
            P2.Probe.KV(tag, "visible=" + vis
                + " title=[" + t + "]"
                + " body=[" + (b ?? string.Empty).Replace("\n", " / ") + "]");
        }

        /// <summary>Point the reported pointer at a shop cell (0-based, row major).</summary>
        private void PinPointer(int cellIndex)
        {
            var panel = Panel();
            if (panel == null) { P2.Probe.Warn("PIN no panel"); return; }

            var cells = FieldOf(panel, "_cells") as IList;
            if (cells == null || cells.Count <= cellIndex) { P2.Probe.Warn("PIN no cells"); return; }

            var hit = FieldOf(cells[cellIndex], "Hit") as Image;
            if (hit == null) { P2.Probe.Warn("PIN no hit image"); return; }

            var sp = RectTransformUtility.WorldToScreenPoint(null, hit.rectTransform.position);
            SwapInput();
            _fake.Pointer = new Vector3(sp.x, sp.y, 0f);
            P2.Probe.KV("PIN", "cell=" + cellIndex + " owner=" + FieldOf(panel, "_owner") + " screen="
                + sp.x.ToString("0.#") + "," + sp.y.ToString("0.#"));
        }

        /// <summary>Park the reported pointer in a corner (no cell under it).</summary>
        private void ParkPointer()
        {
            SwapInput();
            _fake.Pointer = new Vector3(2f, 2f, 0f);
            P2.Probe.KV("PARK", "pointer=2,2 (outside the panel)");
        }

        private void SwapInput()
        {
            if (_swapped) return;
            _fake = new FixedPointerInput(Game.Input);
            Game.AttachInput(_fake);
            _swapped = true;
            P2.Probe.KV("INPUT-SWAP", "Game.AttachInput(FixedPointerInput) real backend=" + _fake.BackendName);
        }

        /// <summary>Invoke the shop tab button (0 = buy page, 1 = sell page).</summary>
        private void ClickTab(int index)
        {
            var panel = Panel();
            if (panel == null) { P2.Probe.Warn("TAB no panel"); return; }

            var tabs = FieldOf(panel, "_tabHit") as Array;
            if (tabs == null || tabs.Length <= index) { P2.Probe.Warn("TAB no hit array"); return; }

            var img = tabs.GetValue(index) as Image;
            var btn = img != null ? img.GetComponent<Button>() : null;
            if (btn == null) { P2.Probe.Warn("TAB no button"); return; }

            btn.onClick.Invoke();
            P2.Probe.Log("TAB-CLICK index=" + index);
        }

        private void OpenShop()
        {
            var npcMod = P2.Probe.CtxMember("Npc");
            var gsm = npcMod != null
                ? npcMod.GetType().GetMethod("GetShop", BindingFlags.Public | BindingFlags.Instance)
                : null;

            ShopOpenArgs real = null;
            var tried = string.Empty;
            for (var sid = 0; sid < 6 && real == null; sid++)
            {
                var cand = gsm != null ? gsm.Invoke(npcMod, new object[] { sid }) as ShopOpenArgs : null;
                tried += "id" + sid + "=" + (cand == null ? "null" : ((cand.stock != null ? cand.stock.Count : -1) + "items")) + " ";
                if (cand != null && cand.stock != null && cand.stock.Count > 0) real = cand;
            }

            if (real == null) { P2.Probe.Warn("SHOP-NO-DATA " + tried.Trim()); return; }

            P2.Probe.KV("SHOP", "npc=" + real.npcName
                + " stock=" + real.stock.Count
                + " sellable=" + (real.playerItems != null ? real.playerItems.Count : -1)
                + " gold=" + real.playerGold
                + " canRepair=" + real.canRepair
                + " try=" + tried.Trim());
            P2.Probe.Emit(Events.ShopOpen, real);
        }

        private void Update()
        {
            if (_done) return;
            if (Time.unscaledTime - _t0 > 420f) { P2.Probe.Warn("WATCHDOG 420s -> finish"); _done = true; P2.Probe.Done(); return; }
            try { Step(); }
            catch (Exception ex)
            {
                P2.Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void Step()
        {
            switch (_step)
            {
                // 0) boot screen (waits for a key; the driver advances it explicitly)
                case 0:
                    if (Open<Diablo2.UI.BootPanel>() || Fsm() == Events.Fsm.StateBoot)
                    {
                        if (!Elapsed(2f)) break;
                        P2.Probe.Log("BOOT fsm=" + Fsm() + " emit " + Events.BootDone);
                        P2.Probe.Emit(Events.BootDone);
                        Next();
                        break;
                    }
                    if (Elapsed(2f)) { P2.Probe.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(120f)) { P2.Probe.Warn("boot 120s fsm=" + Fsm()); Next(); }
                    break;

                // 1) main menu -> new game (char select)
                case 1:
                    if (Open<Diablo2.UI.MainMenuPanel>() || Fsm() == Events.Fsm.StateMainMenu)
                    {
                        if (!Elapsed(1.4f)) break;
                        P2.Probe.KV("PANEL-OPEN", "MainMenuPanel fsm=" + Fsm());
                        P2.Probe.Emit(Events.Fsm.TriggerNewGame);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { P2.Probe.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(60f)) { P2.Probe.Warn("main menu 60s fsm=" + Fsm()); Next(); }
                    break;

                // 2) char select -> load the on-disk save
                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect
                        || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(1.6f)) break;
                        P2.Probe.KV("PANEL-OPEN", "CharSelectPanel fsm=" + Fsm()
                            + " emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        P2.Probe.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { P2.Probe.Warn("char select 30s fsm=" + Fsm()); Next(); }
                    break;

                // 3) first playable frame (stage)
                case 3:
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(2f)) break;
                        P2.Probe.KV("PANEL-OPEN", "HudPanel area fsm=" + Fsm());
                        Snap("stage_town");
                        Next();
                        break;
                    }
                    if (Elapsed(120f)) { P2.Probe.Warn("stage 120s fsm=" + Fsm()); Next(); }
                    break;

                // 4) open the shop (real stock from INpcModule.GetShop)
                case 4:
                    if (Elapsed(0.8f)) { OpenShop(); Next(); }
                    break;

                // 5) shop open, pointer parked away -> tooltip must stay hidden
                case 5:
                    if (Open<Diablo2.UI.ShopPanel>())
                    {
                        if (!Elapsed(1.2f)) break;
                        P2.Probe.KV("PANEL-OPEN", "ShopPanel");
                        ParkPointer();
                        Next();
                        break;
                    }
                    if (Elapsed(20f)) { P2.Probe.Warn("ShopPanel 20s not open"); Next(); }
                    break;

                // 6) settle + read (expect no tooltip) + shot
                case 6:
                    if (Elapsed(1.0f)) { ReadTip("SHOPTIP-BUY-PARKED"); Snap("shop_buy_nohover"); Next(); }
                    break;

                // 7) pin the pointer on cell 0 (buy page) -> tooltip must appear
                case 7:
                    if (Elapsed(0.4f)) { PinPointer(0); Next(); }
                    break;

                // 8) settle + read (expect name + price) + shot
                case 8:
                    if (Elapsed(1.0f)) { ReadTip("SHOPTIP-BUY-HOVER"); Snap("shop_buy_hover"); Next(); }
                    break;

                // 9) switch to the sell page (tab 1) and keep the same cell hovered
                case 9:
                    if (Elapsed(0.4f)) { ClickTab(1); Next(); }
                    break;

                case 10:
                    if (Elapsed(1.2f)) { ReadTip("SHOPTIP-SELL-HOVER"); Snap("shop_sell_hover"); Next(); }
                    break;

                // 11) park again on the sell page -> tooltip must hide
                case 11:
                    if (Elapsed(0.4f)) { ParkPointer(); Next(); }
                    break;

                case 12:
                    if (Elapsed(1.0f)) { ReadTip("SHOPTIP-SELL-PARKED"); Snap("shop_sell_nohover"); Next(); }
                    break;

                default:
                    P2.Probe.Log("SHOPTIP-FINISH steps=" + _step);
                    _done = true;
                    P2.Probe.Done();
                    break;
            }
        }
    }
}
