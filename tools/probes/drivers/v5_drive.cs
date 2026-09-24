// =============================================================================
// v5_drive.cs -- ONE Play session that captures the 16 presentation tiles the V5
//   verification slice needs for the four implementation slices U3 / U4 / M3 / C3.
//
// WHY IT EXISTS: U3/U4/M3/C3 all landed code + offline assertions but the Editor
//   could not compile at the time (Module/View + Module/Combat were mid-edit), so
//   NOT ONE live tile was captured.  The judged class here is PRESENTATION, which
//   only exists as composited pixels inside a live Play session.
//
// CHAIN (one Play session): boot -> main menu -> char select -> enter stage from an
//   existing save -> HUD town shot -> mini-panel hover -> NPC dialog -> shop ->
//   inventory tooltip -> drag -> Tab automap -> player-proximity hover -> bridge ->
//   walk across the bridge -> stand on an NPC cell -> east boundary -> walk to the
//   east seam (area switch) -> monster hover -> attack.  Every step is guarded so
//   one failing step cannot kill the rest of the tiles.
//
// PUBLIC ENTRIES: V5.Api.Cfg / V5.Api.Paths / V5.Api.Ping / V5.Tour.Install
// The runner enters Play, calls Byline.Api.Cfg (the shared bootstrap), then
// V5.Tour.Install with spec = "<rawDir>|<done>".
//
// ASCII ONLY (Roslyn / PS 5.1 read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace V5
{
    internal static class D
    {
        internal const string Tag = "V5";
        internal static string RawDir = string.Empty;
        internal static string DonePath = string.Empty;

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
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "/").Replace("\n", "\\n").Replace("\r", "");
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
        internal static void Paths(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0) RawDir = p[0];
            if (p.Length > 1) DonePath = p[1];
            Log("PATHS rawDir=" + RawDir + " done=" + DonePath);
        }

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
        internal static Diablo2.Module.IItemModule Item() { return CtxMember("Item") as Diablo2.Module.IItemModule; }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string G(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        /// <summary>Snap the camera rig to the player (after a probe teleport the rig lerps).</summary>
        internal static void SnapCam()
        {
            try
            {
                var rig = CtxMember("Camera") as Diablo2.Module.ICameraRig;
                if (rig == null) { Warn("SnapCam: ICameraRig not available"); return; }
                rig.SnapToTarget();
            }
            catch (Exception e) { Warn("SnapCam failed: " + e.GetType().Name); }
        }

        /// <summary>State of a tooltip-style panel: instantiated? active? which texts does it show?</summary>
        internal static string TooltipState(string typeName)
        {
            var t = FindType(typeName);
            if (t == null) return typeName + "=NO-TYPE";
            var arr = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
            if (arr.Length == 0) return typeName + "=NOT-INSTANTIATED";
            var c = arr[0] as Component;
            if (c == null) return typeName + "=NOT-COMPONENT";
            var go = c.gameObject;
            var sb = new StringBuilder();
            sb.Append(typeName).Append("=inst(").Append(arr.Length).Append(") active=").Append(go.activeInHierarchy ? 1 : 0);
            var txts = go.GetComponentsInChildren<Text>(true);
            sb.Append(" texts=").Append(txts.Length);
            for (var i = 0; i < txts.Length && i < 5; i++)
                sb.Append(" [").Append(i).Append("]=\"").Append(Esc(txts[i].text)).Append("\"");
            // u44 closeout: the hover consumer is now `Diablo2.UI.EnemyBarView`, whose bar title and
            // NPC nameplate are BITMAP font (no UnityEngine.UI.Text children) => `texts=` alone would
            // read as "nothing on screen" (fake-green).  Read its real public state when present.
            var bar = t.GetProperty("BarVisible", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (bar != null)
            {
                sb.Append(" barVisible=").Append(bar.GetValue(arr[0], null));
                var plate = t.GetProperty("NameplateVisible", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (plate != null) sb.Append(" plateVisible=").Append(plate.GetValue(arr[0], null));
                var title = t.GetProperty("TitleText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (title != null) sb.Append(" title=\"").Append(Esc(title.GetValue(arr[0], null) as string)).Append("\"");
                var ptext = t.GetProperty("NameplateText", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (ptext != null) sb.Append(" plateText=\"").Append(Esc(ptext.GetValue(arr[0], null) as string)).Append("\"");
            }
            return sb.ToString();
        }

        /// <summary>Root transform of the first live component of the given project type.</summary>
        internal static Transform FindPanel(string typeName)
        {
            var t = FindType(typeName);
            if (t == null) { Warn("FindPanel: type not found " + typeName); return null; }
            var arr = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
            if (arr.Length == 0) return null;
            var c = arr[0] as Component;
            return c != null ? c.transform : null;
        }

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)";
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            return "device=\"" + dev + "\" type=" + typ + " res=" + Screen.width + "x" + Screen.height;
        }

        // ---- UI nodes ---------------------------------------------------------------------
        internal static RectTransform FindNode(string name)
        {
            var all = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase) && t.gameObject.activeInHierarchy)
                    return t;
            }
            return null;
        }
        internal static RectTransform FindNodeUnder(string rootName, string startsWith)
        {
            var root = FindNode(rootName);
            if (root == null) return null;
            return WalkFind(root, startsWith);
        }
        private static RectTransform WalkFind(Transform t, string startsWith)
        {
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase) && c.gameObject.activeInHierarchy)
                {
                    var rt = c as RectTransform;
                    if (rt != null) return rt;
                }
                var deeper = WalkFind(c, startsWith);
                if (deeper != null) return deeper;
            }
            return null;
        }
        /// <summary>Screen-space centre of an overlay-canvas RectTransform.</summary>
        internal static bool ScreenCenter(RectTransform rt, out Vector2 p)
        {
            p = Vector2.zero;
            if (rt == null) return false;
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
            p = RectTransformUtility.WorldToScreenPoint(cam, rt.TransformPoint(rt.rect.center));
            return true;
        }

        // ---- input ------------------------------------------------------------------------
        internal static Mouse MouseDev()
        {
            var m = Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }
        internal static Keyboard KeyboardDev()
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }
        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new MouseState { position = pos };
            if (leftDown) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, st);
        }
        internal static string KeyPress(string arg)
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            Key key;
            if (string.Equals(arg, "tab", StringComparison.OrdinalIgnoreCase)) key = Key.Tab;
            else if (string.Equals(arg, "i", StringComparison.OrdinalIgnoreCase)) key = Key.I;
            else if (string.Equals(arg, "c", StringComparison.OrdinalIgnoreCase)) key = Key.C;
            else { Warn("KeyPress unknown key=" + arg); return "ERR-unknown-key"; }
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key);
            return "KEY-" + key;
        }
        internal static string KeyRelease()
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            return "KEYUP";
        }
    }

    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " isPlaying=" + (Application.isPlaying ? 1 : 0)
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + D.Fsm() + " canvases=" + CountCanvases();
            D.Log(line);
            D.Log("DEVICE " + D.DeviceLine());
            return line;
        }
        private static int CountCanvases()
        {
            try { return UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Length; }
            catch { return -1; }
        }
        public static string Paths(string spec) { D.Paths(spec); return "PATHS-OK"; }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("V5EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<V5Driver>();
            drv.Init(spec ?? string.Empty);
            D.Log("TOUR-INSTALL spec=" + spec + " isPlaying=" + (Application.isPlaying ? 1 : 0)
                  + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class V5Driver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;
        private int _stFrame = -1;
        private string _saveName = "";

        private int _shotIndex = -1;
        private int _shotStage;
        private bool _busy, _capDone, _capOk;
        private string _capInfo = "";
        private string _capPending = "";
        private int _shots;

        // scratch
        private Vector2 _mouse = new Vector2(-1f, -1f);
        private bool _mouseDown;
        private Vector2Int _walkFrom = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _walkTo = new Vector2Int(int.MinValue, int.MinValue);
        private string _areaBefore = "?";
        private Vector2 _dragFrom = new Vector2(-1f, -1f);
        private Vector2 _dragTo = new Vector2(-1f, -1f);
        private int _monsterId = -1;
        private Vector2Int _monsterGrid = new Vector2Int(int.MinValue, int.MinValue);

        private static readonly string[] Names =
        {
            "v5_01_hud_town.png",        // U3-1  bottom HUD panorama (8 mini-panel entry buttons)
            "v5_02_hud_hover.png",       // U3-2  hover a mini-panel entry button
            "v5_03_npc_dialog.png",      // U3-3  NPC dialog (Akara) -- 756x568.8 stone frame / font
            "v5_04_shop.png",            // U3-4  shop panel (title bar text)
            "v5_05_item_tooltip.png",    // U4-5  inventory hover -> tooltip
            "v5_06_drag.png",            // U4-8  drag in progress (ghost + target cell highlight)
            "v5_07_automap_tab.png",     // U4-9  Tab automap (background must NOT be dimmed)
            "v5_08_player_proximity.png",// U4-7  mouse 1 cell from the player
            "v5_09_bridge.png",          // M3-10 standing on the camp bridge (deck)
            "v5_10_bridge_cross.png",    // M3-10 walk across the bridge (grid before/after in log)
            "v5_11_npc_overlap.png",     // M3-11 player on the same cell as an NPC
            "v5_12_boundary.png",        // M3-12 map boundary (slanted edge / wall gap)
            "v5_13_east_exit.png",       // M3-13 walk east across the seam -> area switch
            "v5_14_monster_hover.png",   // U4-6  hover a monster (name / hp)
            "v5_15_attack.png",          // C3-14 attack a monster (hit feedback)
        };
        private const int N = 15;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            D.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            D.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                  + " isPlaying=" + (Application.isPlaying ? 1 : 0));
            BuildPlan();
            D.KV("PLAN", "stations=" + _plan.Count + " tiles=" + N);
        }

        private void Add(string name, Func<bool> f) { var nm = name; _plan.Add(() => { if (_cur != nm) { _cur = nm; _at = Time.unscaledTime; _stFrame = -1; } return f(); }); }
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private static bool FsmEq(string s) { return D.Fsm() == s; }
        private bool WaitFrames(int n)
        {
            if (_stFrame < 0) { _stFrame = Time.frameCount; return false; }
            return Time.frameCount - _stFrame >= n;
        }

        private void Update()
        {
            if (_done) return;
            try
            {
                var guard = 0;
                while (!_done && _pc < _plan.Count && guard++ < 4000)
                {
                    if (!_plan[_pc]()) return;
                    _pc++; _cur = "?"; _at = Time.unscaledTime;
                }
                if (_pc >= _plan.Count) Finish("plan-end");
            }
            catch (Exception e)
            {
                D.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        private void LateUpdate()
        {
            if (!_capDone || _capPending.Length == 0) return;
        }

        private void BuildPlan()
        {
            Add("wait60", () => WaitFrames(60));

            Add("boot", () =>
            {
                if (FsmEq("MainMenu")) return true;
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(30f)) { D.Warn("boot timeout fsm=" + D.Fsm()); return true; }
                return false;
            });
            Add("menu-wait", () =>
            {
                if (FsmEq("MainMenu")) return true;
                if (Elapsed(30f)) { D.Warn("menu-wait timeout fsm=" + D.Fsm()); return true; }
                return false;
            });
            Add("charselect", () =>
            {
                if (FsmEq("CharSelect")) return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(25f)) { D.Warn("charselect timeout fsm=" + D.Fsm()); return true; }
                return false;
            });

            Add("enter-stage", () =>
            {
                if (FsmEq("Stage")) return true;
                var save = D.CtxMember("Save") as Diablo2.Module.ISaveModule;
                if (string.IsNullOrEmpty(_saveName))
                {
                    var list = save != null ? save.List() : null;
                    _saveName = (list != null && list.Count > 0) ? list[0] : null;
                    D.KV("ENTER-STAGE", "save=\"" + (_saveName ?? "(none)") + "\" fsm=" + D.Fsm());
                }
                if (string.IsNullOrEmpty(_saveName))
                {
                    D.Warn("enter-stage: no save -- char create fallback");
                    if (FsmEq("CharSelect") && Elapsed(0.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerNeedCreate);
                    if (FsmEq("CharCreate") && Elapsed(1.0f)) Game.Fsm.Trigger(Events.Fsm.TriggerCreated);
                    if (Elapsed(25f)) { D.Warn("enter-stage fallback timeout fsm=" + D.Fsm()); return true; }
                    return false;
                }
                if (Elapsed(0.8f)) Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
                if (Elapsed(70f)) { D.Warn("enter-stage timeout fsm=" + D.Fsm()); return true; }
                return false;
            });

            Add("stage-settle", () =>
            {
                var hud = Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>();
                if (!FsmEq("Stage") || !hud)
                {
                    if (Elapsed(50f)) { D.Warn("stage-settle timeout fsm=" + D.Fsm() + " hud=" + hud); return true; }
                    return false;
                }
                if (!Elapsed(4f)) return false;
                var m = D.Map();
                D.KV("STAGE-READY", "fsm=" + D.Fsm() + " area=" + (m != null ? m.Area.ToString() : "-")
                    + " size=" + (m != null ? m.Width + "x" + m.Height : "-")
                    + " playerGrid=" + (D.Player() != null ? D.G(D.Player().Grid) : "-")
                    + " " + D.DeviceLine());
                return true;
            });

            Add("shots", () =>
            {
                if (_shotIndex >= N) return true;
                if (_shotIndex < 0) { _shotIndex = 0; _shotStage = 0; return false; }
                switch (_shotStage)
                {
                    case 0:
                        try { Setup(_shotIndex); }
                        catch (Exception e) { D.Warn("SETUP-FAIL idx=" + _shotIndex + " ex=" + e.GetType().Name + ": " + e.Message); }
                        _shotStage = 1; _stFrame = Time.frameCount;
                        return false;
                    case 1:
                        if (_shotIndex == 5 && _mouseDown)
                        {
                            // keep feeding pointer moves so uGUI's BeginDrag really fires
                            var t = Mathf.Clamp01((Time.frameCount - _stFrame) / 12f);
                            var p = Vector2.Lerp(_dragFrom, _dragTo, t);
                            D.MouseState(p, true); _mouse = p;
                        }
                        if (Time.frameCount - _stFrame < WaitFor(_shotIndex)) return false;
                        _shotStage = 2;
                        return false;
                    case 2:
                        if (!CaptureFull(D.RawDir + "/" + Names[_shotIndex])) return false;
                        _shotStage = 0; _shotIndex++;
                        return _shotIndex >= N;
                }
                return false;
            });

            Add("finish", () =>
            {
                D.KV("SUMMARY", "tiles=" + _shots + "/" + N + " fsm=" + D.Fsm() + " " + D.DeviceLine());
                return true;
            });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private static int WaitFor(int i)
        {
            switch (i)
            {
                case 9: return 40;   // teleport + camera snap
                case 10: return 110; // walk across the bridge
                case 11: return 40;
                case 12: return 40;
                case 13: return 140; // walk east until the area switches
                case 14: return 40;
                case 15: return 30;
                default: return 14;
            }
        }

        // ============================================================ per-tile setup
        private void Setup(int i)
        {
            switch (i)
            {
                case 0: // bottom HUD panorama (town, default state)
                    D.KV("T0-HUD", "miniPanelPresent=" + (D.FindNode("MiniPanel") != null ? 1 : 0)
                        + " miniBtn0=" + (D.FindNode("MiniBtn0") != null ? 1 : 0)
                        + " miniBtn7=" + (D.FindNode("MiniBtn7") != null ? 1 : 0));
                    DumpMiniButtons();
                    break;

                case 1: // hover a mini-panel entry button
                {
                    var rt = D.FindNode("MiniBtn3");
                    Vector2 p;
                    if (D.ScreenCenter(rt, out p)) { D.MouseState(p, false); _mouse = p; D.KV("T1-HOVER", "btn=MiniBtn3 screen=" + p.x.ToString("0") + "," + p.y.ToString("0")); }
                    else D.Warn("T1: MiniBtn3 not found");
                    break;
                }

                case 2: // NPC dialog (real production path: Events.DialogOpen -> HudPanel.OnDialogOpen)
                {
                    var a = new NpcDialogArgs();
                    a.npcId = 0;
                    a.npcName = "\u963f\u5361\u62c9"; // Akara
                    a.text = "\u5728\u90aa\u6076\u4e4b\u5730\u4e2d\u6709\u4e00\u4e2a\u4fee\u9053\u9662\u7684\u5730\u65b9\uff0c\u5341\u5206\u7684\u53ef\u6015\u2026\u2026\u5982\u679c\u4f60\u771f\u7684\u8981\u53bb\u63a2\u9669\uff0c\u8bf7\u5c0f\u5fc3\u3002";
                    a.options.Add("\u96e2\u958b");
                    a.options.Add("\u91cd\u8981\u6d88\u606f");
                    a.options.Add("\u4ea4\u6613");
                    a.hasShop = true;
                    Game.Event.Emit<NpcDialogArgs>(Events.DialogOpen, a);
                    D.KV("T2-DIALOG", "emitted npcName=" + a.npcName + " options=" + a.options.Count);
                    break;
                }

                case 3: // shop panel
                {
                    var a = new ShopOpenArgs();
                    a.npcId = 0;
                    a.npcName = "\u963f\u5361\u62c9";
                    a.canRepair = true;
                    var it = D.Item();
                    a.playerGold = it != null ? it.Gold : 0;
                    Game.Event.Emit<ShopOpenArgs>(Events.ShopOpen, a);
                    D.KV("T3-SHOP", "emitted npcName=" + a.npcName + " gold=" + a.playerGold
                        + " titleNode=" + (D.FindNode("ShopPanel") != null ? 1 : 0));
                    break;
                }

                case 4: // inventory + tooltip
                {
                    Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
                    Game.UI.Close<Diablo2.UI.ShopPanel>();
                    EnsureInventoryItem();
                    Game.Event.Emit<string>(Events.PanelToggleRequest, "InventoryPanel");
                    D.KV("T4-INV", "open=" + (Game.UI.IsOpen<Diablo2.UI.InventoryPanel>() ? 1 : 0));
                    var ip = D.FindPanel("Diablo2.UI.InventoryPanel");
                    if (ip != null)
                    {
                        var irt = ip as RectTransform;
                        Vector2 ic, icc;
                        D.ScreenCenter(irt, out ic);
                        var c0 = FindNth(ip, "Cell", 0);
                        D.ScreenCenter(c0, out icc);
                        D.KV("T4-PANEL", "root=" + ip.name + " centre=" + ic.x.ToString("0") + "," + ic.y.ToString("0")
                            + " cell0=" + (c0 != null ? c0.name : "-") + " cell0Centre=" + icc.x.ToString("0") + "," + icc.y.ToString("0")
                            + " " + D.TooltipState("Diablo2.UI.ItemTooltip"));
                    }
                    HoverInventoryCell(0);
                    break;
                }

                case 5: // drag in progress (press cell0, then interpolate to cell6 while held)
                {
                    var root = D.FindPanel("Diablo2.UI.InventoryPanel");
                    Vector2 a = _mouse, b = _mouse;
                    if (root != null)
                    {
                        var c0 = FindNth(root, "Cell", 0);
                        var c6 = FindNth(root, "Cell", 6);
                        Vector2 p0, p6;
                        if (c0 != null && D.ScreenCenter(c0, out p0)) a = p0;
                        if (c6 != null && D.ScreenCenter(c6, out p6)) b = p6;
                    }
                    _dragFrom = a; _dragTo = b;
                    D.MouseState(a, false);
                    D.MouseState(a, true); _mouseDown = true; _mouse = a;
                    D.KV("T5-DRAG", "from=" + a.x.ToString("0") + "," + a.y.ToString("0")
                        + " to=" + b.x.ToString("0") + "," + b.y.ToString("0"));
                    break;
                }

                case 6: // automap: release drag, PRESS Tab (released next station so the game sees a real frame of "pressed")
                {
                    D.MouseState(_mouse, false); _mouseDown = false;
                    Game.UI.Close<Diablo2.UI.InventoryPanel>();
                    D.KeyPress("tab");   // held across frames; released in station 7
                    D.KV("T6-AUTOMAP", "tabPressed minimapOpen=" + (Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>() ? 1 : 0));
                    break;
                }

                case 7: // release Tab, hover a cell 1 step from the player
                {
                    D.KV("T7-AUTOMAP-AFTER", "minimapOpen=" + (Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>() ? 1 : 0));
                    D.KeyRelease();
                    var pl = D.Player();
                    if (pl != null)
                    {
                        var cell = pl.Grid + new Vector2Int(1, 0);
                        var wp = Iso.GridToWorld(cell);
                        var sp = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(wp) : Vector2.zero;
                        D.MouseState(sp, false); _mouse = sp;
                        D.KV("T7-PROXIMITY", "playerGrid=" + D.G(pl.Grid) + " hoverCell=" + D.G(cell)
                            + " screen=" + sp.x.ToString("0") + "," + sp.y.ToString("0"));
                    }
                    break;
                }

                case 8: // stand on the camp bridge (deck cell)
                {
                    var pl = D.Player(); var m = D.Map();
                    if (pl != null && m != null)
                    {
                        var g = new Vector2Int(51, 25);
                        pl.TeleportTo(g); D.SnapCam();
                        D.KV("T8-BRIDGE", "to=" + D.G(g) + " isDeck=" + (m.IsDeckGrid(g) ? 1 : 0)
                            + " walkable=" + (m.Walkable(g) ? 1 : 0) + " playerGrid=" + D.G(pl.Grid));
                    }
                    break;
                }

                case 9: // walk across the bridge to the east end (grid before/after recorded)
                {
                    var pl = D.Player();
                    if (pl != null)
                    {
                        var g = new Vector2Int(46, 25);
                        pl.TeleportTo(g); D.SnapCam();
                        _walkFrom = pl.Grid;
                        _walkTo = new Vector2Int(55, 25);
                        Game.Event.Emit<Vector2Int>(Events.MoveCommand, _walkTo);
                        D.KV("T9-CROSS-START", "from=" + D.G(_walkFrom) + " to=" + D.G(_walkTo));
                    }
                    break;
                }

                case 10: // record where the crossing walk ended; then stand on an NPC cell
                {
                    var pl = D.Player(); var m = D.Map();
                    if (pl != null) D.KV("T9-CROSS-END", "grid=" + D.G(pl.Grid) + " moving=" + (pl.IsMoving ? 1 : 0));
                    if (pl != null && m != null && m.NpcPoints != null && m.NpcPoints.Count > 0)
                    {
                        var npc = m.NpcPoints[0];
                        pl.TeleportTo(npc); D.SnapCam();
                        D.KV("T10-NPC-OVERLAP", "npcCells=" + m.NpcPoints.Count + " to=" + D.G(npc)
                            + " playerGrid=" + D.G(pl.Grid));
                    }
                    else D.Warn("T10: NpcPoints empty");
                    break;
                }

                case 11: // east map boundary
                {
                    var pl = D.Player(); var m = D.Map();
                    if (pl != null && m != null)
                    {
                        var g = new Vector2Int(m.Width - 1, 20);
                        pl.TeleportTo(g); D.SnapCam();
                        D.KV("T11-BOUNDARY", "to=" + D.G(g) + " size=" + m.Width + "x" + m.Height
                            + " walkable=" + (m.Walkable(g) ? 1 : 0) + " tile=" + m.TileAt(g));
                    }
                    break;
                }

                case 12: // walk east across the seam (expect the area to switch)
                {
                    var pl = D.Player(); var m = D.Map();
                    if (pl != null && m != null)
                    {
                        _areaBefore = m.Area.ToString();
                        var g = new Vector2Int(m.Width - 2, 25);
                        pl.TeleportTo(g); D.SnapCam();
                        Game.Event.Emit<Vector2Int>(Events.MoveCommand, new Vector2Int(m.Width - 1, 25));
                        D.KV("T12-EXIT-START", "area=" + _areaBefore + " from=" + D.G(pl.Grid));
                    }
                    break;
                }

                case 13: // record the area after the walk; find a monster and hover it
                {
                    var m = D.Map();
                    if (m != null) D.KV("T12-EXIT-END", "area=" + m.Area + " areaBefore=" + _areaBefore
                        + " changed=" + (m.Area.ToString() != _areaBefore ? 1 : 0));
                    FindAndApproachMonster();
                    break;
                }

                case 14: // attack the monster (left click at its screen position)
                {
                    D.KV("T14-TIP", D.TooltipState("Diablo2.UI.EnemyBarView"));   // u44: EntityTooltip was deleted (contract C4)
                    if (_monsterId >= 0)
                    {
                        var wp = Iso.GridToWorld(_monsterGrid);
                        var sp = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(wp) : Vector2.zero;
                        D.MouseState(sp, false);
                        D.MouseState(sp, true);
                        D.KV("T14-ATTACK", "monster=" + _monsterId + " cell=" + D.G(_monsterGrid)
                            + " screen=" + sp.x.ToString("0") + "," + sp.y.ToString("0"));
                    }
                    else D.Warn("T14: no monster target");
                    break;
                }
            }
        }

        private void DumpMiniButtons()
        {
            var sb = new StringBuilder();
            for (var k = 0; k < 10; k++)
            {
                var rt = D.FindNode("MiniBtn" + k);
                if (rt == null) continue;
                Vector2 p;
                D.ScreenCenter(rt, out p);
                sb.Append("|").Append(k).Append(" size=").Append(rt.sizeDelta.x.ToString("0.#"))
                  .Append("x").Append(rt.sizeDelta.y.ToString("0.#"))
                  .Append(" screen=").Append(p.x.ToString("0")).Append(",").Append(p.y.ToString("0"));
            }
            D.KV("T0-MINIBTNS", sb.ToString());
        }

        private void EnsureInventoryItem()
        {
            var it = D.Item();
            if (it == null) { D.Warn("EnsureInventoryItem: no IItemModule"); return; }
            var full = false;
            for (var i = 0; i < it.Inventory.Count; i++)
            {
                var s = it.Inventory[i];
                if (s != null && s.item != null) { full = true; break; }
            }
            if (full) { D.KV("INV-ITEM", "existing items present"); return; }
            try
            {
                var rngType = D.FindType("Diablo2.Def.Rng");
                object rng = rngType != null ? Activator.CreateInstance(rngType, new object[] { 12345 }) : null;
                var mi = it.GetType().GetMethod("CreateRandom", BindingFlags.Public | BindingFlags.Instance);
                var stack = mi != null ? mi.Invoke(it, new object[] { 1, rng }) : null;
                var addOk = stack != null && it.AddToInventory((Diablo2.Def.ItemStack)stack);
                D.KV("INV-ITEM", "created=" + (stack != null ? 1 : 0) + " added=" + (addOk ? 1 : 0));
            }
            catch (Exception e) { D.Warn("INV-ITEM-FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private void HoverInventoryCell(int cellIndex)
        {
            var root = D.FindPanel("Diablo2.UI.InventoryPanel");
            if (root == null) { D.Warn("HoverInventoryCell: InventoryPanel not found"); return; }
            var cell = FindNth(root, "Cell", cellIndex);
            Vector2 p;
            if (cell != null && D.ScreenCenter(cell, out p))
            {
                D.MouseState(p, _mouseDown); _mouse = p;
                D.KV("T4-HOVER-CELL", "node=" + cell.name + " screen=" + p.x.ToString("0") + "," + p.y.ToString("0"));
            }
            else D.Warn("HoverInventoryCell: cell node not found (root=" + root.name + ")");
        }

        private static RectTransform FindNth(Transform root, string startsWith, int index)
        {
            var hits = new List<RectTransform>();
            Collect(root, startsWith, hits);
            hits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return index >= 0 && index < hits.Count ? hits[index] : (hits.Count > 0 ? hits[0] : null);
        }
        private static void Collect(Transform t, string startsWith, List<RectTransform> into)
        {
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase) && c.gameObject.activeInHierarchy)
                {
                    var rt = c as RectTransform;
                    if (rt != null) into.Add(rt);
                }
                Collect(c, startsWith, into);
            }
        }

        private void FindAndApproachMonster()
        {
            var pl = D.Player();
            if (pl == null) { D.Warn("FindMonster: no player"); return; }
            SpriteRenderer best = null;
            var bestD = float.MaxValue;
            var arr = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            for (var i = 0; i < arr.Length; i++)
            {
                var sr = arr[i];
                if (sr == null) continue;
                var nm = sr.gameObject.name;
                if (!nm.StartsWith("E_")) continue;
                if (nm.StartsWith("E_1_")) continue; // E_1_* = the player
                var d = Vector3.Distance(sr.transform.position, Iso.GridToWorld(pl.Grid));
                if (d < bestD) { bestD = d; best = sr; }
            }
            if (best == null) { D.Warn("FindMonster: no monster renderer (E_*) found -- area=" + (D.Map() != null ? D.Map().Area.ToString() : "-")); return; }
            _monsterGrid = Iso.WorldToGrid(best.transform.position);
            _monsterId = -1;
            var idPart = best.gameObject.name.Split('_');
            if (idPart.Length > 1) int.TryParse(idPart[1], out _monsterId);
            D.KV("T13-MONSTER", "node=" + best.gameObject.name + " cell=" + D.G(_monsterGrid)
                + " dist=" + bestD.ToString("0.0") + " playerGrid=" + D.G(pl.Grid));
            // step next to the monster so it is on screen, then hover it (try all 8 neighbours + ring 2)
            var m = D.Map();
            var placed = false;
            var offs = new[] {
                new Vector2Int(-1,0), new Vector2Int(1,0), new Vector2Int(0,-1), new Vector2Int(0,1),
                new Vector2Int(-1,-1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(1,1) };
            for (var k = 0; k < offs.Length && !placed; k++)
            {
                var adj = _monsterGrid + offs[k];
                if (m != null && m.Walkable(adj)) { pl.TeleportTo(adj); D.SnapCam(); placed = true; }
            }
            D.KV("T13-APPROACH", "placed=" + (placed ? 1 : 0) + " playerGrid=" + D.G(pl.Grid));
            var wp = Iso.GridToWorld(_monsterGrid);
            var sp = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(wp) : Vector2.zero;
            D.MouseState(sp, false); _mouse = sp;
            D.KV("T13-HOVER", "screen=" + sp.x.ToString("0") + "," + sp.y.ToString("0")
                + " " + D.TooltipState("Diablo2.UI.EnemyBarView"));   // u44: EntityTooltip was deleted (contract C4)
        }

        // ============================================================ capture
        private bool CaptureFull(string file)
        {
            if (!_busy)
            {
                _capPending = file; _busy = true; _capDone = false; _capOk = false;
                StartCoroutine(CaptureRoutine(file));
                return false;
            }
            if (!_capDone) return false;
            _busy = false;
            _shots++;
            D.KV("TILE", "file=" + _capPending + " ok=" + (_capOk ? 1 : 0) + " " + _capInfo);
            _capPending = "";
            return true;
        }

        private IEnumerator CaptureRoutine(string file)
        {
            yield return new WaitForEndOfFrame();
            Texture2D full = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) { _capInfo = "capture-null"; }
                else
                {
                    var px = full.GetPixels();
                    float sum = 0f;
                    for (var i = 0; i < px.Length; i++) sum += px[i].r + px[i].g + px[i].b;
                    var mean = px.Length > 0 ? sum / (px.Length * 3f) : -1f;
                    var dir = Path.GetDirectoryName(file);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    var bytes = full.EncodeToPNG();
                    File.WriteAllBytes(file, bytes);
                    _capOk = true;
                    _capInfo = "full " + full.width + "x" + full.height + " bytes=" + bytes.Length
                        + " meanLum=" + mean.ToString("0.000");
                }
            }
            catch (Exception e)
            {
                _capInfo = "EX-" + e.GetType().Name + "-" + e.Message;
                D.Warn("CAPTURE-FAIL " + file + " " + _capInfo);
            }
            finally { if (full != null) UnityEngine.Object.Destroy(full); }
            _capDone = true;
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            D.KV("FINISH", "why=" + why + " pc=" + _pc + " fsm=" + D.Fsm() + " tiles=" + _shots + "/" + N);
            D.WriteFile(D.DonePath, "V5-DONE why=" + why + " tiles=" + _shots + "/" + N
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
