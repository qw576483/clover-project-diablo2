// =============================================================================
// d2p1_evidence.cs -- one Play session for the four "needs a window" items:
//   (1) E60  old save migration  : load client/setting/saves/S2203805.json (version=1)
//                                -> CharacterPanel derived rows must read 84/84, 50/50, 15/15
//                                -> SaveAndExit -> that JSON's "version" key must become 2
//   (2) W11  CharacterPanel Plus0..3 at statPoints == 0 (sprite / color.a / rect / interactable)
//   (3) W12  town waypoint: walk to (31,26) -> panel -> delay >= 2 frames -> screenshot
//   (4) W2/W3 hover dispatch: town npc (plate, no bar) / live monster in BloodMoor (bar, no plate)
//                                -> kill -> 3 samples of payload hasTarget + BarVisible
//
// Why one window (play-log reason): all four are runtime state that the offline hosts
// cannot produce -- (1) needs a real Load of the on-disk old save plus a real save
// round-trip; (2) is the on-screen state of uGUI nodes built by the production panel;
// (3) is a rendered panel (asynchronous UiArt.Art) ; (4) is the dispatch of
// Events.HoverTargetChanged into UI/EnemyBarView. None of them is computable offline.
//
// Honest limitation, stated up front: the hover GRID is injected with the project's own
// non-contract self-test API `InputReader.OverrideHoverGrid` + `InputReader.UpdateHover`
// (both public, reached via reflection because InputReader is internal). Everything after
// the grid -- HoverPicker.Resolve, Game.Event.Emit(HoverTargetChanged), EnemyBarView
// dispatch and its own log lines -- is the production path. No real OS mouse was moved.
//
// Shape (Probe / Api / Tour / Driver, log tags, done marker, absolute-path screenshots)
// reuses the existing drivers under .ai-tmp/drivers/ (saveprogress_probe.cs). ASCII only.
// =============================================================================

using System;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace P1
{
    /// <summary>Log / reflection / json / screenshot helpers.</summary>
    public static class Probe
    {
        internal const string Tag = "P1";
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
            Log("P1-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var d = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                    File.WriteAllText(_done, "P1-DONE " + DateTime.Now.ToString("o"));
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

        /// <summary>client/setting/saves/&lt;name&gt;.json (same resolution as SaveModule.SettingDir).</summary>
        internal static string SavePath(string name)
        {
            var proj = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(Path.Combine(Path.Combine(proj, "setting"), "saves"), name + ".json");
        }

        internal static string ReadAll(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) { return "(read-fail " + ex.GetType().Name + ")"; }
        }

        /// <summary>Raw integer value of "key": in a flat json blob; "(missing)" when absent.</summary>
        internal static string JsonNum(string json, string key)
        {
            if (json == null) return "(no-file)";
            var i = json.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return "(missing)";
            i += key.Length + 3;
            var j = i;
            while (j < json.Length && (char.IsDigit(json[j]) || json[j] == '-' || json[j] == '+')) j++;
            return j > i ? json.Substring(i, j - i) : "(empty)";
        }

        internal static string Head(string json, int n)
        {
            if (json == null) return "(no-file)";
            return json.Length <= n ? json : json.Substring(0, n) + "...";
        }

        // ---- transform helpers -------------------------------------------------

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

        /// <summary>
        /// node -> rendered text for every live bitmap label (Diablo2.UI.D2Label is internal
        /// and is NOT a component: it keeps a private static List&lt;D2Label&gt; of live labels
        /// and exposes public Root / text members, so reflection is the only way in).
        /// </summary>
        internal static System.Collections.Generic.Dictionary<Transform, string> LabelTexts()
        {
            var res = new System.Collections.Generic.Dictionary<Transform, string>();
            var t = FindType("Diablo2.UI.D2Label");
            if (t == null) { Log("LABELMAP type-missing"); return res; }
            var f = t.GetField("Live", BindingFlags.NonPublic | BindingFlags.Static);
            var list = f != null ? f.GetValue(null) as System.Collections.IEnumerable : null;
            if (list == null) { Log("LABELMAP field-missing"); return res; }
            var prop = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
            var rootP = t.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || rootP == null) { Log("LABELMAP member-missing"); return res; }
            foreach (var o in list)
            {
                if (o == null) continue;
                var rt = rootP.GetValue(o) as Transform;
                if (rt == null) continue;
                var v = prop.GetValue(o) as string;
                res[rt] = v ?? string.Empty;
            }
            return res;
        }

        internal static string TextOf(Transform root, string name)
        {
            var t = FindDeep(root, name);
            if (t == null) return "(node-missing)";
            var map = LabelTexts();
            string v;
            if (map.TryGetValue(t, out v)) return v;
            var txt = t.GetComponent<UnityEngine.UI.Text>();
            return txt != null ? txt.text : "(no-text)";
        }

        /// <summary>Print every live label that lives under <paramref name="root"/>.</summary>
        internal static int DumpLabels(Transform root, string tag)
        {
            var map = LabelTexts();
            var n = 0;
            foreach (var kv in map)
            {
                var p = kv.Key;
                var inside = false;
                while (p != null) { if (p == root) { inside = true; break; } p = p.parent; }
                if (!inside) continue;
                KV(tag + "-LBL", "node=" + kv.Key.name + " text=\"" + kv.Value + "\"");
                n++;
            }
            KV(tag + "-LBLCOUNT", n.ToString());
            return n;
        }

        internal static string ImgOf(Transform root, string name)
        {
            var t = FindDeep(root, name);
            if (t == null) return "(node-missing)";
            var img = t.GetComponent<UnityEngine.UI.Image>();
            if (img == null) return "(no-image)";
            var sprite = img.sprite == null ? "(null)" : img.sprite.name;
            var rt = img.rectTransform;
            var btn = t.GetComponent<UnityEngine.UI.Button>();
            var act = btn == null ? "(no-button)" : (btn.interactable ? "1" : "0");
            return "sprite=" + sprite
                 + " color=(" + img.color.r.ToString("0.###") + "," + img.color.g.ToString("0.###")
                 + "," + img.color.b.ToString("0.###") + "," + img.color.a.ToString("0.###") + ")"
                 + " rect=" + rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#")
                 + " active=" + (t.gameObject.activeInHierarchy ? "1" : "0")
                 + " interactable=" + act;
        }
    }

    /// <summary>Callable entries for the runner (run_script --entry).</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000"); }

        public static string JsonSummary(string name)
        {
            var json = Probe.ReadAll(Probe.SavePath(name));
            return "version=" + Probe.JsonNum(json, "version")
                 + " stamina=" + Probe.JsonNum(json, "stamina")
                 + " life=" + Probe.JsonNum(json, "life")
                 + " mana=" + Probe.JsonNum(json, "mana")
                 + " statPoints=" + Probe.JsonNum(json, "statPoints")
                 + " head=" + Probe.Head(json, 120);
        }

        public static string Shot(string path) { return Probe.Shot(path); }
    }

    /// <summary>Installer: spec = "&lt;charName&gt;|&lt;done marker&gt;|&lt;shot dir&gt;|&lt;phase&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P1EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain, four evidence groups. Never per-item Play entries.</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private bool _done;
        private int _tryCount;
        private int _sampleNo;

        // forced hover (grid injected through the project's own self-test API)
        private bool _hoverOn;
        private Vector2Int _hoverGrid;
        private string _hoverKey = string.Empty;
        private MonoState _lastPayload;
        private int _monsterId = -1;
        private int _monsterGridX = -1;
        private int _monsterGridY = -1;

        private struct MonoState { public bool hasTarget; public int cursor; public int id; public string name; }

        /// <summary>The grid the killed monster occupied when it died (sampling anchor).</summary>
        private Vector2Int _deathGrid = new Vector2Int(int.MinValue, int.MinValue);

        /// <summary>The town npc grid hovered in the W2 first half.</summary>
        private Vector2Int _npcGrid = new Vector2Int(int.MinValue, int.MinValue);

        private static Diablo2.UI.EnemyBarView _bar;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _name = parts[0];
            var done = parts.Length > 1 ? parts[1] : string.Empty;
            _shotDir = parts.Length > 2 ? parts[2] : string.Empty;
            Probe.Paths(done);
            Probe.Log("DRIVER-INIT name=\"" + _name + "\" shots=" + _shotDir + " frame=" + Time.frameCount);
            try
            {
                Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverPayload);
                Probe.Log("SUBSCRIBED " + Events.HoverTargetChanged);
            }
            catch (Exception ex) { Probe.Log("SUBSCRIBE-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnHoverPayload(Diablo2.Def.HoverTarget t)
        {
            _lastPayload = new MonoState
            {
                hasTarget = t != null && t.hasTarget,
                cursor = t != null ? (int)t.cursor : -1,
                id = t != null ? t.id : -1,
                name = t != null ? (t.name ?? string.Empty) : string.Empty,
            };
        }

        // ---- plumbing ---------------------------------------------------------
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool WpOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.WaypointPanel>(); }
        private static bool CharOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharacterPanel>(); }

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

        private string Shots(string leaf)
        {
            var dir = string.IsNullOrEmpty(_shotDir) ? Directory.GetCurrentDirectory() : _shotDir;
            return Path.Combine(dir, "d2p1_" + leaf + ".png");
        }

        private static Diablo2.UI.EnemyBarView Bar()
        {
            if (_bar != null) return _bar;
            var arr = Resources.FindObjectsOfTypeAll<Diablo2.UI.EnemyBarView>();
            if (arr != null && arr.Length > 0) _bar = arr[0];
            return _bar;
        }

        private static string BarState()
        {
            var b = Bar();
            if (b == null) return "barVisible=(no-view) plateVisible=(no-view) plateText=(no-view)";
            return "barVisible=" + (b.BarVisible ? "1" : "0")
                 + " plateVisible=" + (b.NameplateVisible ? "1" : "0")
                 + " plateText=\"" + (b.NameplateText ?? string.Empty) + "\""
                 + " title=\"" + (b.TitleText ?? string.Empty) + "\"";
        }

        /// <summary>How many LIVE monsters share that grid (tells probe artefacts from real defects).</summary>
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

        /// <summary>First in-bounds walkable grid at chebyshev distance r from <paramref name="g"/>.</summary>
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

        /// <summary>Live monsters within chebyshev radius r of that grid (1 = only itself).</summary>
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

        /// <summary>
        /// Force the hover grid through InputReader.OverrideHoverGrid + UpdateHover.
        /// Both are public members of an internal type, so reflection keeps this file
        /// compiling without touching the module assembly's internals.
        /// </summary>
        private void ForceHover(Vector2Int g)
        {
            var p = Probe.Player();
            if (p == null) return;
            try
            {
                var pr = p.GetType().GetProperty("Input", BindingFlags.Public | BindingFlags.Instance);
                var inp = pr != null ? pr.GetValue(p) : null;
                if (inp == null) { Probe.Warn("FORCE-HOVER no InputReader"); return; }
                var t = inp.GetType();
                var m1 = t.GetMethod("OverrideHoverGrid", BindingFlags.Public | BindingFlags.Instance);
                var m2 = t.GetMethod("UpdateHover", BindingFlags.Public | BindingFlags.Instance);
                if (m1 != null) m1.Invoke(inp, new object[] { g });
                if (m2 != null) m2.Invoke(inp, new object[] { true });
            }
            catch (Exception ex) { Probe.Log("FORCE-HOVER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// Force the grid THEN read the state synchronously, so the labelled line is the state
        /// produced by the forced hover (and not the state left by the real-mouse Poll of this
        /// frame, which runs before LateUpdate).
        /// </summary>
        private void LogHoverForced(string tag)
        {
            ForceHover(_hoverGrid);
            Probe.KV("HOVER-" + tag, "grid=" + Probe.Grid(_hoverGrid) + " " + BarState()
                + " payload.hasTarget=" + (_lastPayload.hasTarget ? "1" : "0")
                + " payload.cursor=" + _lastPayload.cursor);
        }

        private void LogHover(string tag)
        {
            var key = BarState();
            if (key == _hoverKey) return;
            _hoverKey = key;
            Probe.KV("HOVER-" + tag, "grid=" + Probe.Grid(_hoverGrid) + " " + key
                + " payload.hasTarget=" + (_lastPayload.hasTarget ? "1" : "0")
                + " payload.cursor=" + _lastPayload.cursor);
        }

        // ---- character panel read --------------------------------------------
        private static Transform CharPanelRoot()
        {
            var arr = Resources.FindObjectsOfTypeAll<Diablo2.UI.CharacterPanel>();
            if (arr == null) return null;
            for (var i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].gameObject.activeInHierarchy) return arr[i].transform;
            return null;
        }

        private void ReadCharPanel(string why)
        {
            var root = CharPanelRoot();
            if (root == null) { Probe.Warn("CHAR-PANEL-ROOT missing (" + why + ")"); return; }
            for (var i = 0; i < 4; i++)
            {
                Probe.KV("PANELROW-" + i, "name=\"" + Probe.TextOf(root, "DerivedName" + i)
                    + "\" value=\"" + Probe.TextOf(root, "DerivedValue" + i) + "\"");
            }
            for (var i = 0; i < 4; i++)
                Probe.KV("PLUS-" + i, Probe.ImgOf(root, "Plus" + i));
            Probe.KV("PANEL-CLOSE", "text=\"" + Probe.TextOf(root, "CloseText") + "\" "
                + Probe.ImgOf(root, "CloseButton"));
            Probe.DumpLabels(root, "CHAR");
        }

        // ---- the chain --------------------------------------------------------
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

        private void LateUpdate()
        {
            if (!_hoverOn || _done) return;
            ForceHover(_hoverGrid);
            LogHover("state");
        }

        private void Step()
        {
            switch (_step)
            {
                // 0) boot screen: it waits for any key (BootPanel.OnAnyKey -> Events.BootDone)
                case 0:
                    if (Fsm() == Events.Fsm.StateMainMenu || MenuOpen())
                    {
                        Probe.KV("MENU-READY", "fsm=" + Fsm());
                        Probe.Emit(Events.Fsm.TriggerContinue);
                        Next(); break;
                    }
                    if (Elapsed(2f)) { Probe.KV("BOOT-SKIP", "emit " + Events.BootDone); Probe.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(120f)) { Probe.Warn("boot 120s still " + Fsm()); Next(); }
                    break;

                // 1) char select -> load the on-disk OLD save
                case 1:
                    if (SelectOpen() || Fsm() == Events.Fsm.StateCharSelect || Elapsed(30f))
                    {
                        Probe.KV("SELECT", "selectOpen=" + (SelectOpen() ? 1 : 0) + " fsm=" + Fsm());
                        Probe.KV("JSON-BEFORE-LOAD", Api.JsonSummary(_name));
                        Probe.KV("LOAD-OLD", "emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        Probe.Emit(Events.CharSelectRequest, _name);
                        Next();
                    }
                    break;

                // 2) first playable frame
                case 2:
                    if (HudOpen() && Fsm() == Events.Fsm.StateStage)
                    {
                        Probe.KV("AFTER-LOAD", "area=" + Area() + " me=" + Probe.Grid(PlayerGrid()));
                        Next(); break;
                    }
                    if (Elapsed(90f)) { Probe.Warn("load 90s not ready area=" + Area()); Next(); }
                    break;

                // 3) open the character panel (same entry as key C)
                case 3:
                    Probe.KV("PANEL-OPEN-REQ", "emit " + Events.PanelToggleRequest + "(CharacterPanel)");
                    Probe.Emit(Events.PanelToggleRequest, "CharacterPanel");
                    Next();
                    break;

                case 4:
                    if (CharOpen()) { Probe.KV("CHAR-PANEL-OPEN", "frame=" + Time.frameCount); Next(); break; }
                    if (Elapsed(15f)) { Probe.Warn("CharacterPanel 15s not open"); Next(); }
                    break;

                // 5) read nodes + screenshot
                case 5:
                    if (Elapsed(0.6f))
                    {
                        ReadCharPanel("E60+W11");
                        Probe.KV("SHOT-CHAR-PANEL", Probe.Shot(Shots("charpanel")));
                        Next();
                    }
                    break;

                case 6:
                    if (Elapsed(0.5f))
                    {
                        Probe.Emit(Events.PanelToggleRequest, "CharacterPanel");
                        Next();
                    }
                    break;

                // 7) W2 first half: walk next to the town npc (Akara = NpcPoints[0]) so the
                //    in-world nameplate actually lands inside the viewport, then hover it.
                case 7:
                    if (Elapsed(0.5f))
                    {
                        var map = Probe.Map();
                        if (map == null || map.NpcPoints == null || map.NpcPoints.Count == 0)
                        { Probe.Warn("town NpcPoints empty -> W2 npc half BLOCKED"); Next(); break; }
                        _npcGrid = map.NpcPoints[0];
                        var t = WalkableRing(_npcGrid, 2);
                        Probe.KV("HOVER-NPC-WALK", "npc=" + Probe.Grid(_npcGrid) + " walkTo=" + Probe.Grid(t));
                        if (t.x >= 0) Probe.Emit(Events.MoveCommand, t);
                        Next();
                    }
                    break;

                case 8:
                    {
                        var p = PlayerGrid();
                        var d = Mathf.Max(Mathf.Abs(_npcGrid.x - p.x), Mathf.Abs(_npcGrid.y - p.y));
                        // a dialog panel can pop up when the landing grid is inside TalkRange
                        if (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>())
                        {
                            Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
                            Probe.KV("NPC-DIALOG-CLOSED", "landing inside TalkRange opened it; closed again");
                            _at = Time.unscaledTime;
                            break;
                        }
                        if (d > 3 && !Elapsed(25f)) break;
                        if (d > 3) Probe.Warn("walk to npc timed out dist=" + d);
                        Probe.KV("HOVER-NPC-DIST", "dist=" + d);
                        _hoverGrid = _npcGrid;
                        _hoverOn = true;
                        LogHoverForced("W2-NPC");
                        Probe.KV("SHOT-HOVER-NPC", Probe.Shot(Shots("hover_akara")));
                        Next();
                    }
                    break;

                // 9) hover off -> BloodMoor
                case 9:
                    if (Elapsed(0.5f))
                    {
                        _hoverOn = false;
                        Probe.KV("EXIT-MOOR", "emit " + Events.ExitEntered + "(BloodMoor=1)");
                        Probe.Emit(Events.ExitEntered, AreaId.BloodMoor);
                        Next();
                    }
                    break;

                case 10:
                    if (Area() == (int)AreaId.BloodMoor && HudOpen() && Elapsed(1.5f))
                    { Probe.KV("MOOR-ARRIVED", "me=" + Probe.Grid(PlayerGrid())); Next(); break; }
                    if (Elapsed(45f)) { Probe.Warn("BloodMoor 45s not ready area=" + Area()); Next(); }
                    break;

                // 11) W2 second half: hover a live monster
                case 11:
                    {
                        var mon = Probe.Monsters();
                        var p = PlayerGrid();
                        // Prefer an ISOLATED live monster: the Fallen spawn in packs, so a corpse
                        // grid is often re-occupied by another live monster, which would make the
                        // post-kill samples measure "the pack" instead of "the killed one".
                        var best = -1; var bd = int.MaxValue; var bx = -1; var by = -1; var bad = -1;
                        if (mon != null && mon.All != null)
                        {
                            for (var i = 0; i < mon.All.Count; i++)
                            {
                                var m = mon.All[i];
                                if (m == null || !m.alive) continue;
                                var d = Mathf.Max(Mathf.Abs(m.gridX - p.x), Mathf.Abs(m.gridY - p.y));
                                var crowd = AliveNear(new Vector2Int(m.gridX, m.gridY), 1);
                                if (crowd != 1) { if (d < bad || bad < 0) bad = d; continue; }
                                if (d < bd) { bd = d; best = m.id; bx = m.gridX; by = m.gridY; }
                            }
                        }
                        if (best < 0)
                        {
                            Probe.Warn("no isolated live monster in BloodMoor (nearest crowded dist=" + bad
                                + ") -> W2/W3 monster half BLOCKED for this session");
                            Next(); break;
                        }
                        Probe.KV("MOB-PICK", "isolatedWithin1=1 nearestIsolatedDist=" + bd + " nearestCrowdedDist=" + bad);
                        _monsterId = best; _monsterGridX = bx; _monsterGridY = by;
                        _hoverGrid = new Vector2Int(bx, by);
                        _hoverOn = true;
                        Probe.KV("HOVER-MOB-REQ", "m#" + best + " grid=(" + bx + "," + by + ") dist=" + bd);
                        Next();
                    }
                    break;

                case 12:
                    if (Elapsed(0.8f))
                    {
                        LogHoverForced("W2-MOB");
                        Probe.KV("SHOT-HOVER-MOB", Probe.Shot(Shots("hover_monster")));
                        Next();
                    }
                    break;

                // 13) walk next to the monster so a melee attack is legal
                case 13:
                    if (Elapsed(0.5f))
                    {
                        var mon = Probe.Monsters();
                        var m = mon != null ? mon.Get(_monsterId) : null;
                        if (m != null && m.alive)
                        {
                            _hoverGrid = new Vector2Int(m.gridX, m.gridY);
                            Probe.KV("WALK-TO-MOB", "emit " + Events.MoveCommand + "(" + m.gridX + "," + m.gridY + ")");
                            Probe.Emit(Events.MoveCommand, new Vector2Int(m.gridX, m.gridY));
                        }
                        Next();
                    }
                    break;

                case 14:
                    {
                        var mon = Probe.Monsters();
                        var m = mon != null ? mon.Get(_monsterId) : null;
                        if (m == null || !m.alive) { Probe.KV("MELEE-SKIP", "target already gone"); Next(); break; }
                        var p = PlayerGrid();
                        var d = Mathf.Max(Mathf.Abs(m.gridX - p.x), Mathf.Abs(m.gridY - p.y));
                        if (d <= 2) { Probe.KV("MELEE-READY", "dist=" + d); Next(); break; }
                        if (Elapsed(25f)) { Probe.Warn("walk to monster 25s, dist=" + d); Next(); }
                    }
                    break;

                // 15) kill it through the production attack path (left click on monster)
                case 15:
                    {
                        var mon = Probe.Monsters();
                        var comb = Probe.Combat();
                        if (mon == null || comb == null) { Probe.Warn("Monster/Combat module missing -> kill BLOCKED"); Next(); break; }
                        if (!mon.IsAlive(_monsterId))
                        {
                            var corpse = mon.Get(_monsterId);
                            if (corpse != null) _deathGrid = new Vector2Int(corpse.gridX, corpse.gridY);
                            Probe.KV("KILLED", "m#" + _monsterId + " tries=" + _tryCount
                                + " aliveCount=" + mon.AliveCount + " deathGrid=" + Probe.Grid(_deathGrid));
                            _hoverGrid = _deathGrid;
                            Next(); break;
                        }
                        var m = mon.Get(_monsterId);
                        if (m != null) _hoverGrid = new Vector2Int(m.gridX, m.gridY);
                        if (Elapsed(0.6f))
                        {
                            comb.RequestAttack(_monsterId);
                            _tryCount++;
                            _at = Time.unscaledTime;
                            var mm = mon.Get(_monsterId);
                            Probe.KV("ATTACK", "try=" + _tryCount + " hp=" + (mm != null ? mm.hp + "/" + mm.maxHp : "?"));
                        }
                        if (_tryCount >= 45) { Probe.Warn("kill timeout after " + _tryCount + " attacks"); Next(); }
                    }
                    break;

                // 16..18) three post-kill samples
                case 16: case 17: case 18:
                    if (Elapsed(0.7f))
                    {
                        _sampleNo = _step - 15;
                        ForceHover(_hoverGrid);
                        var mon = Probe.Monsters();
                        var alive = mon != null ? (mon.IsAlive(_monsterId) ? "1" : "0") : "?";
                        Probe.KV("W3-SAMPLE-" + _sampleNo,
                            "grid=" + Probe.Grid(_hoverGrid) + " killedAlive=" + alive
                            + " liveAtGrid=" + AliveAt(_hoverGrid)
                            + " payload.hasTarget=" + (_lastPayload.hasTarget ? "1" : "0")
                            + " payload.cursor=" + _lastPayload.cursor
                            + " payload.id=" + _lastPayload.id
                            + " payload.name=\"" + _lastPayload.name + "\""
                            + " " + BarState());
                        Next();
                    }
                    break;

                // 19) leave the monster hover
                case 19:
                    if (Elapsed(0.5f)) { _hoverOn = false; Probe.KV("HOVER-OFF", "frame=" + Time.frameCount); Next(); }
                    break;

                // 20) visit DenOfEvil so the town panel has 2 destinations
                case 20:
                    Probe.KV("EXIT-DEN", "emit " + Events.ExitEntered + "(DenOfEvil)");
                    Probe.Emit(Events.ExitEntered, AreaId.DenOfEvil);
                    Next();
                    break;

                case 21:
                    if (Elapsed(1.5f) && Area() == (int)AreaId.DenOfEvil) { Probe.KV("DEN-ARRIVED", "me=" + Probe.Grid(PlayerGrid())); Next(); break; }
                    if (Elapsed(45f)) { Probe.Warn("DenOfEvil 45s not ready area=" + Area()); Next(); }
                    break;

                case 22:
                    Probe.KV("EXIT-BACK-TOWN", "emit " + Events.ExitEntered + "(Town)");
                    Probe.Emit(Events.ExitEntered, AreaId.Town);
                    Next();
                    break;

                case 23:
                    if (Elapsed(1.5f) && Area() == (int)AreaId.Town) { Probe.KV("TOWN-BACK", "me=" + Probe.Grid(PlayerGrid())); Next(); break; }
                    if (Elapsed(45f)) { Probe.Warn("Town 45s not ready area=" + Area()); Next(); }
                    break;

                // 24) W12: walk to the waypoint cell (31,26) -> the panel opens on arrival
                case 24:
                    {
                        var map = Probe.Map();
                        if (map == null || map.WaypointPoints == null || map.WaypointPoints.Count == 0)
                        { Probe.Warn("no waypoint anchor -> W12 BLOCKED"); Next(); break; }
                        var a = map.WaypointPoints[0];
                        Probe.KV("WALK-TO-WP", "grid=" + Probe.Grid(a) + " emit " + Events.MoveCommand);
                        Probe.Emit(Events.MoveCommand, a);
                        Next();
                    }
                    break;

                case 25:
                    if (WpOpen())
                    {
                        _wpFrame = Time.frameCount;
                        Probe.KV("WP-PANEL-OPEN", "frame=" + _wpFrame + " me=" + Probe.Grid(PlayerGrid()));
                        Next(); break;
                    }
                    if (Elapsed(25f)) { Probe.Warn("waypoint panel 25s not open me=" + Probe.Grid(PlayerGrid())); Next(); }
                    break;

                // 26) delay >= 2 frames (UiArt.Art is asynchronous) then read nodes + screenshot
                case 26:
                    if (Time.frameCount >= _wpFrame + 2 && Elapsed(1.2f))
                    {
                        _at = Time.unscaledTime;
                        var root = WpRoot();
                        if (root == null) Probe.Warn("WaypointPanel root missing");
                        else
                        {
                            for (var i = 0; i < 2; i++)
                                Probe.KV("WP-DEST-" + i, "text=\"" + Probe.TextOf(root, "Dest" + i) + "\" " + Probe.ImgOf(root, "Dest" + i));
                            Probe.KV("WP-CLOSE", "text=\"" + Probe.TextOf(root, "Close") + "\" " + Probe.ImgOf(root, "Close"));
                            Probe.KV("WP-TITLE", "\"" + Probe.TextOf(root, "Title") + "\" hint=\"" + Probe.TextOf(root, "Hint") + "\"");
                            Probe.DumpLabels(root, "WP");
                        }
                        Probe.KV("SHOT-WP-DELAYED", Probe.Shot(Shots("wp_panel_delayed")));
                        Next();
                    }
                    break;

                case 27:
                    if (Elapsed(0.6f))
                    {
                        if (Game.UI != null) Game.UI.Close<Diablo2.UI.WaypointPanel>();
                        Next();
                    }
                    break;

                // 28) E60 third criterion: save once more and read the on-disk version back
                case 28:
                    if (Elapsed(0.8f))
                    {
                        Probe.KV("SAVE-AND-EXIT", "emit " + Events.SaveAndExitRequest);
                        Probe.Emit(Events.SaveAndExitRequest);
                        Next();
                    }
                    break;

                case 29:
                    if (Fsm() == Events.Fsm.StateMainMenu || Elapsed(30f))
                    {
                        Probe.KV("AFTER-SAVE-EXIT", "fsm=" + Fsm());
                        Probe.KV("JSON-AFTER-SAVE", Api.JsonSummary(_name));
                        Next();
                    }
                    break;

                default:
                    _done = true;
                    Probe.Done();
                    break;
            }
        }

        private int _wpFrame;

        private static Transform WpRoot()
        {
            var arr = Resources.FindObjectsOfTypeAll<Diablo2.UI.WaypointPanel>();
            if (arr == null) return null;
            for (var i = 0; i < arr.Length; i++)
                if (arr[i] != null && arr[i].gameObject.activeInHierarchy) return arr[i].transform;
            return null;
        }
    }
}
