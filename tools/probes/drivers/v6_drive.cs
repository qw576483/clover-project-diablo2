// =============================================================================
// v6_drive.cs -- ONE Play session that re-judges the 6 V5 defects (1 dialog text
//   outside the frame + residual, 2 inventory item tooltip missing, 3 entity
//   tooltip tiny/duplicated, 4 shop cannot be closed, 5 automap draws nothing,
//   6 drop-target highlight unseen).
//
// WHY IT EXISTS: V5 judged these six from live pixels, but three of its probes
//   could not reach the thing they judged (a NullReferenceException in
//   D.TooltipState killed station 4 BEFORE the hover, and the item tooltip is a
//   plain class, not a UnityEngine.Object, so FindObjectsByType on it throws).
//   This driver judges each defect with a NUMBER first (reflection into the live
//   panel) and a tile second, so a missing tile can never be mistaken for a
//   missing feature and vice versa.
//
// CHAIN (one Play session): boot -> menu -> char select -> stage from save ->
//   dialog (tile 1) -> dialog closed / residual check (tile 2) -> shop close
//   (tile 3) -> inventory item tooltip (tile 4) -> drag highlight (tile 5) ->
//   automap (tile 6) -> monster hover (tile 7).
//
// PUBLIC ENTRIES: V6.Api.Cfg / V6.Api.Paths / V6.Api.Ping / V6.Tour.Install
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

namespace V6
{
    internal static class D
    {
        internal const string Tag = "V6";
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
        internal static string V2(Vector2 v) { return v.x.ToString("0.#") + "x" + v.y.ToString("0.#"); }

        /// <summary>Snap the camera rig to the player.</summary>
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

        // ---- reflection helpers -------------------------------------------------
        internal static object LiveComponent(string typeName)
        {
            var t = FindType(typeName);
            if (t == null) return null;
            var arr = UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None);
            return arr.Length > 0 ? arr[0] : null;
        }
        internal static object Field(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f.GetValue(o);
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
                if (p != null) return p.GetValue(o);
                t = t.BaseType;
            }
            return null;
        }
        internal static object Static(Type t, string name)
        {
            if (t == null) return null;
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f != null ? f.GetValue(null) : null;
        }
        internal static string F(object o, string fmt = "0.###")
        {
            if (o == null) return "null";
            if (o is float f) return f.ToString(fmt);
            if (o is double d) return d.ToString(fmt);
            return o.ToString();
        }

        // ---- UI nodes -----------------------------------------------------------
        internal static RectTransform Child(Transform parent, string name)
        {
            if (parent == null) return null;
            for (var i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (string.Equals(c.name, name, StringComparison.Ordinal)) return c as RectTransform;
            }
            return null;
        }
        internal static RectTransform FindNode(string name)
        {
            var all = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) return t;
            }
            return null;
        }
        internal static int CountNodesNamed(string name) { return FindAllNamed(name).Count; }
        internal static List<RectTransform> FindAllNamed(string name)
        {
            var hits = new List<RectTransform>();
            var all = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (string.Equals(t.name, name, StringComparison.OrdinalIgnoreCase)) hits.Add(t);
            }
            return hits;
        }

        /// <summary>Screen-space rect (x0,y0)-(x1,y1) of a node; NaN when unusable.</summary>
        internal static bool ScreenRect(RectTransform rt, out Vector4 r)
        {
            r = new Vector4(float.NaN, 0, 0, 0);
            if (rt == null) return false;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var canvas = rt.GetComponentInParent<Canvas>();
            Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, c[i]);
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }
            r = new Vector4(minX, minY, maxX, maxY);
            return true;
        }

        /// <summary>One-line geometry dump of a node (screen rect + layout fields).</summary>
        internal static string Geo(string label, RectTransform rt)
        {
            if (rt == null) return label + "=MISSING";
            Vector4 s;
            ScreenRect(rt, out s);
            return label + "[" + rt.name + "] scr=(" + s.x.ToString("0") + "," + s.y.ToString("0") + ")-("
                   + s.z.ToString("0") + "," + s.w.ToString("0")
                   + ") aMin=" + V2(rt.anchorMin) + " aMax=" + V2(rt.anchorMax) + " piv=" + V2(rt.pivot)
                   + " sd=" + V2(rt.sizeDelta) + " rect=" + V2(rt.rect.size) + " apos=" + V2(rt.anchoredPosition)
                   + " kids=" + rt.childCount + " act=" + (rt.gameObject.activeInHierarchy ? 1 : 0)
                   + " scale=" + rt.localScale.x.ToString("0.##");
        }

        // ---- mouse / keys -------------------------------------------------------
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
        internal static void KeyPress(Key key)
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key);
        }
        internal static void KeyRelease()
        {
            var kb = KeyboardDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP");
        }

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)";
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            return "device=\"" + dev + "\" type=" + typ + " res=" + Screen.width + "x" + Screen.height;
        }

        /// <summary>Read D2Label's private static downgrade switch (proves whether the bitmap font is live).</summary>
        internal static string BitmapState()
        {
            try
            {
                var t = FindType("Diablo2.UI.D2Label");
                if (t == null) return "D2Label=NO-TYPE";
                var f = t.GetField("_bitmapUnavailable", BindingFlags.NonPublic | BindingFlags.Static);
                return "bitmapUnavailable=" + (f != null && (bool)f.GetValue(null) ? 1 : 0);
            }
            catch (Exception e) { return "bitmapState-ex=" + e.GetType().Name; }
        }

        /// <summary>ShopPanel instance count + IsOpen (the V5 defect 4 judge).</summary>
        internal static string ShopState(string when)
        {
            var t = FindType("Diablo2.UI.ShopPanel");
            var live = t != null ? UnityEngine.Object.FindObjectsByType(t, FindObjectsSortMode.None).Length : -1;
            var open = Game.UI != null && Game.UI.IsOpen<Diablo2.UI.ShopPanel>();
            return when + " shopOpen=" + (open ? 1 : 0) + " liveInstances=" + live;
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
            var go = new GameObject("V6EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<V6Driver>();
            drv.Init(spec ?? string.Empty);
            D.Log("TOUR-INSTALL spec=" + spec + " isPlaying=" + (Application.isPlaying ? 1 : 0)
                  + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class V6Driver : MonoBehaviour
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

        private Vector2 _mouse = new Vector2(-1f, -1f);
        private bool _mouseDown;
        private Vector2 _dragFrom, _dragTo;
        private Vector2Int _monsterGrid = new Vector2Int(int.MinValue, int.MinValue);
        /// <summary>Monster sprite world position (NOT `Iso.GridToWorld`: per-area MapRoot offsets differ).</summary>
        private Vector3 _monsterPos;

        private static readonly string[] Names =
        {
            "v6_01_dialog.png",          // defect 1: dialog geometry (title/body inside the stone frame)
            "v6_02_dialog_closed.png",   // defect 1: residual after close
            "v6_03_shop_after_close.png",// defect 4: shop really closes
            "v6_04_item_tooltip.png",    // defect 2: inventory item tooltip shows
            "v6_05_drag.png",            // defect 6: drag ghost + drop highlight
            "v6_06_automap.png",         // defect 5: automap draws primitives
            "v6_07_bloodmoor.png",       // walk east across the seam (monsters live here, town has none)
            "v6_08_monster_hover.png",   // defect 3: entity tooltip name/hp readable
        };
        private const int N = 8;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            D.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            D.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                  + " isPlaying=" + (Application.isPlaying ? 1 : 0));
            BuildPlan();
            D.KV("PLAN", "stations=" + _plan.Count + " tiles=" + N);
        }

        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            _plan.Add(() => { if (_cur != nm) { _cur = nm; _at = Time.unscaledTime; _stFrame = -1; } return f(); });
        }
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
                    + " " + D.DeviceLine() + " " + D.BitmapState());
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
                if (_shotIndex == 4 && _mouseDown)
                {
                    var t = Mathf.Clamp01((Time.frameCount - _stFrame) / 12f);
                    var p = Vector2.Lerp(_dragFrom, _dragTo, t);
                    D.MouseState(p, true); _mouse = p;
                }
                // defect 3: keep the pointer on the monster every frame (the camera keeps
                // lerping after a teleport, so a point computed once in Setup goes stale).
                if (_shotIndex == 7)
                {
                    D.SnapCam();     // the rig lerps after a teleport -> pin it so the point is stable
                    var sp = Camera.main != null
                        ? (Vector2)Camera.main.WorldToScreenPoint(_monsterPos)
                        : Vector2.zero;
                    D.MouseState(sp, false); _mouse = sp;
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
            Add("entity-dump", () => { D.Log("T9-ENTITY " + EntityState()); return true; });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private static int WaitFor(int i)
        {
            switch (i)
            {
                case 0: return 30;   // dialog: chi font + full rebuild
                case 1: return 12;   // dialog closed (Destroy lands end of frame)
                case 3: return 30;   // inventory opens, then hover the cell
                case 4: return 26;   // keep dragging, then read the highlight
                case 5: return 20;   // automap: full redraw
                case 6: return 150;  // walk east across the seam -> BloodMoor (area switch takes ~110 frames)
                case 7: return 30;   // monster hover
                default: return 14;
            }
        }

        // ============================================================ per-tile setup
        private void Setup(int i)
        {
            switch (i)
            {
                case 0: // defect 1: emit the real production event (HudPanel.OnDialogOpen -> Open<NpcDialogPanel>)
                {
                    var a = new NpcDialogArgs();
                    a.npcId = 0;
                    a.npcName = "\u963f\u5361\u62c9";
                    a.text = "\u5728\u90aa\u6076\u4e4b\u5730\u4e2d\u6709\u4e00\u4e2a\u4fee\u9053\u9662\u7684\u5730\u65b9\uff0c"
                        + "\u5341\u5206\u7684\u53ef\u6015\u2026\u2026\u5982\u679c\u4f60\u771f\u7684\u8981\u53bb\u63a2\u9669\uff0c"
                        + "\u8bf7\u5c0f\u5fc3\u3002";
                    a.options.Add("\u96e2\u958b");
                    a.options.Add("\u91cd\u8981\u6d88\u606f");
                    a.options.Add("\u4ea4\u6613");
                    a.hasShop = true;
                    Game.Event.Emit<NpcDialogArgs>(Events.DialogOpen, a);
                    D.KV("T0-DIALOG", "emitted npcName=" + a.npcName + " options=" + a.options.Count);
                    break;
                }

                case 1: // defect 1: full geometry dump + containment judge, then close
                {
                    DiagDialog("OPEN");
                    Game.UI.Close<Diablo2.UI.NpcDialogPanel>();
                    D.KV("T1-CLOSE", "isOpen=" + (Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>() ? 1 : 0));
                    break;
                }

                case 2: // defect 1: residual check after a full frame + defect 4: shop open then close
                {
                    var names = new[] { "DialogArt", "SpeechBanner", "Speaker", "Body", "Glyphs" };
                    var sb = new StringBuilder();
                    for (var k = 0; k < names.Length; k++)
                        sb.Append("|").Append(names[k]).Append("=").Append(D.CountNodesNamed(names[k]));
                    D.KV("T2-RESIDUAL", sb.ToString() + " (want 0 for each)");
                    foreach (var n in names)
                    {
                        var hit = D.FindAllNamed(n);
                        for (var k = 0; k < hit.Count; k++)
                            if (hit[k] != null) D.Log("T2-RESIDUAL-NODE " + D.Geo(n + "#" + k, hit[k]));
                    }

                    var sa = new ShopOpenArgs();
                    sa.npcId = 0;
                    sa.npcName = "\u963f\u5361\u62c9";
                    sa.canRepair = true;
                    var it = D.Item();
                    sa.playerGold = it != null ? it.Gold : 0;
                    Game.Event.Emit<ShopOpenArgs>(Events.ShopOpen, sa);
                    D.KV("T2-SHOP-OPEN", D.ShopState("afterEmit"));
                    Game.UI.Close<Diablo2.UI.ShopPanel>();
                    D.KV("T2-SHOP-CLOSED", D.ShopState("sameFrameClose"));
                    break;
                }

                case 3: // defect 4 (shop still closed) + defect 2 (open inventory + dump + hover cell0)
                {
                    D.KV("T3-SHOP-AFTER", D.ShopState("frameAfterClose"));
                    EnsureInventoryItem();
                    Game.Event.Emit<string>(Events.PanelToggleRequest, "InventoryPanel");
                    D.KV("T3-INV", "open=" + (Game.UI.IsOpen<Diablo2.UI.InventoryPanel>() ? 1 : 0)
                        + " " + D.ShopState("whileInvOpen"));

                    var ip = D.LiveComponent("Diablo2.UI.InventoryPanel") as Component;
                    if (ip == null) { D.Warn("T4: InventoryPanel component not found"); break; }
                    D.Log("T4-PANEL " + D.Geo("InventoryPanel", ip.transform as RectTransform));
                    var c0 = FindNth(ip.transform, "Cell", 0);
                    D.Log("T4-CELL0 " + D.Geo("Cell0", c0));
                    if (c0 != null && D.ScreenRect(c0, out var r0))
                    {
                        var p = new Vector2((r0.x + r0.z) * 0.5f, (r0.y + r0.w) * 0.5f);
                        D.MouseState(p, false); _mouse = p;
                        D.KV("T4-HOVER", "screen=" + p.x.ToString("0") + "," + p.y.ToString("0"));
                    }
                    else D.Warn("T4: cell0 not found");

                    var c6 = FindNth(ip.transform, "Cell", 6);
                    _dragFrom = _mouse;
                    if (c6 != null && D.ScreenRect(c6, out var r6))
                        _dragTo = new Vector2((r6.x + r6.z) * 0.5f, (r6.y + r6.w) * 0.5f);
                    else _dragTo = _mouse;
                    break;
                }

                case 4: // defect 2 tooltip numeric judge; then start the drag (defect 6)
                {
                    D.Log("T5-TOOLTIP " + TooltipState());
                    D.MouseState(_dragFrom, false);
                    D.MouseState(_dragFrom, true); _mouseDown = true; _mouse = _dragFrom;
                    D.KV("T5-DRAG", "from=" + _dragFrom.x.ToString("0") + "," + _dragFrom.y.ToString("0")
                        + " to=" + _dragTo.x.ToString("0") + "," + _dragTo.y.ToString("0"));
                    break;
                }

                case 5: // defect 6: drop-highlight judge; then close the inventory and open the automap (defect 5)
                {
                    var ip = D.LiveComponent("Diablo2.UI.InventoryPanel") as Component;
                    if (ip != null)
                    {
                        var hl = D.Child(ip.transform, "DropHighlight") as RectTransform;
                        var im = hl != null ? hl.GetComponent<Image>() : null;
                        D.Log("T6-HIGHLIGHT " + D.Geo("DropHighlight", hl)
                            + " colorA=" + (im != null ? im.color.a.ToString("0.###") : "-")
                            + " ghostActive=" + (D.Child(ip.transform, "DragGhost") != null
                                ? (D.Child(ip.transform, "DragGhost").gameObject.activeInHierarchy ? 1 : 0) : -1));
                    }
                    D.MouseState(_mouse, false); _mouseDown = false;
                    Game.UI.Close<Diablo2.UI.InventoryPanel>();
                    D.KeyPress(Key.Tab);
                    D.KV("T6-AUTOMAP", "tabPressed open="
                        + (Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>() ? 1 : 0));
                    break;
                }

                case 6: // defect 5: automap numeric judge (+ sweep proof), then walk east into BloodMoor
                {
                    D.KeyRelease();
                    MiniMapDiag();
                    MiniMapSweepProof();
                    Game.UI.Close<Diablo2.UI.MiniMapPanel>();

                    // Monsters only exist outside town: walk east across the seam (Town -> BloodMoor).
                    var m = D.Map();
                    var pl = D.Player();
                    if (m != null && pl != null && m.Area.ToString() == "Town")
                    {
                        var from = new Vector2Int(m.Width - 2, 25);
                        pl.TeleportTo(from); D.SnapCam();
                        Game.Event.Emit<Vector2Int>(Events.MoveCommand, new Vector2Int(m.Width - 1, 25));
                        D.KV("T7-SEAM", "from=" + D.G(from) + " to=" + D.G(new Vector2Int(m.Width - 1, 25))
                            + " area=" + m.Area);
                    }
                    else D.Warn("T7-SEAM: not in Town (area=" + (m != null ? m.Area.ToString() : "-") + ")");
                    break;
                }

                case 7: // defect 3: hover a real monster outside town
                {
                    var m = D.Map();
                    D.KV("T8-AREA", "area=" + (m != null ? m.Area.ToString() : "-")
                        + " playerGrid=" + (D.Player() != null ? D.G(D.Player().Grid) : "-"));
                    FindAndApproachMonster();
                    break;
                }
            }
        }

        // ============================================================ diagnostics
        private static void DiagDialog(string when)
        {
            var t = D.FindType("Diablo2.UI.NpcDialogPanel");
            if (t == null) { D.Warn("dialog type not found"); return; }
            D.Log("DIALOG-STATICS " + when
                + " K=" + D.F(D.Static(t, "DialogArtScale"))
                + " artSize=" + D.V2((Vector2)(D.Static(t, "DialogArtSize") ?? Vector2.zero))
                + " frameTop=" + D.F(D.Static(t, "FrameTop"))
                + " frameY=" + D.F(D.Static(t, "FrameY"))
                + " contentW=" + D.F(D.Static(t, "ContentW"))
                + " textH=" + D.F(D.Static(t, "TextH"))
                + " nameH=" + D.F(D.Static(t, "NameH"))
                + " nameY=" + D.F(D.Static(t, "NameY"))
                + " bodyH=" + D.F(D.Static(t, "BodyH"))
                + " bodyY=" + D.F(D.Static(t, "BodyY"))
                + " nameFont=" + D.Static(t, "NameFont") + " bodyFont=" + D.Static(t, "BodyFont"));

            var panel = D.LiveComponent("Diablo2.UI.NpcDialogPanel") as Component;
            if (panel == null) { D.Warn("dialog panel component missing"); return; }
            var root = panel.transform as RectTransform;
            D.Log("DIALOG-NODES " + D.Geo("root", root));
            var art = D.Child(panel.transform, "DialogArt");
            var banner = D.Child(panel.transform, "SpeechBanner");
            var spk = D.Child(panel.transform, "Speaker");
            var body = D.Child(panel.transform, "Body");
            D.Log("DIALOG-NODES " + D.Geo("art", art));
            D.Log("DIALOG-NODES " + D.Geo("banner", banner));
            D.Log("DIALOG-NODES " + D.Geo("speaker", spk));
            D.Log("DIALOG-NODES " + D.Geo("body", body));
            if (body != null) D.Log("DIALOG-NODES " + D.Geo("body.Glyphs", D.Child(body, "Glyphs")));
            if (spk != null) D.Log("DIALOG-NODES " + D.Geo("speaker.Glyphs", D.Child(spk, "Glyphs")));

            // containment judge: title/body rect inside the stone frame rect
            // NOTE: assign each out separately (an `out` inside a short-circuited `&&` is only
            // conditionally assigned -> CS0170 "use of possibly unassigned field").
            Vector4 fa, sa, ba;
            D.ScreenRect(art, out fa);
            D.ScreenRect(spk, out sa);
            D.ScreenRect(body, out ba);
            if (art != null && spk != null && body != null)
            {
                var insideSpk = sa.x >= fa.x - 1f && sa.z <= fa.z + 1f && sa.y >= fa.y - 1f && sa.w <= fa.w + 1f;
                var insideBody = ba.x >= fa.x - 1f && ba.z <= fa.z + 1f && ba.y >= fa.y - 1f && ba.w <= fa.w + 1f;
                D.KV("DIALOG-CONTAIN", "frame=(" + fa.x.ToString("0") + "," + fa.y.ToString("0") + ")-("
                    + fa.z.ToString("0") + "," + fa.w.ToString("0") + ") speaker=(" + sa.x.ToString("0") + ","
                    + sa.y.ToString("0") + ")-(" + sa.z.ToString("0") + "," + sa.w.ToString("0") + ") body=("
                    + ba.x.ToString("0") + "," + ba.y.ToString("0") + ")-(" + ba.z.ToString("0") + ","
                    + ba.w.ToString("0") + ") speakerInside=" + (insideSpk ? 1 : 0) + " bodyInside=" + (insideBody ? 1 : 0));
            }
            else D.Warn("DIALOG-CONTAIN: a node is missing (art/spk/body) => containment not judged");
        }

        /// <summary>ItemTooltip is NOT a UnityEngine.Object -> reach it through InventoryPanel._tooltip.</summary>
        private static string TooltipState()
        {
            var ip = D.LiveComponent("Diablo2.UI.InventoryPanel");
            if (ip == null) return "InventoryPanel=MISSING";
            var tip = D.Field(ip, "_tooltip");
            if (tip == null) return "ItemTooltip=NULL";
            var sb = new StringBuilder("ItemTooltip");
            sb.Append(" visible=").Append(D.Field(tip, "_visible"));
            var root = D.Field(tip, "_root") as RectTransform;
            sb.Append(" rootActive=").Append(root != null ? (root.gameObject.activeInHierarchy ? 1 : 0) : -1);
            if (root != null)
            {
                Vector4 r;
                D.ScreenRect(root, out r);
                sb.Append(" scr=(").Append(r.x.ToString("0")).Append(",").Append(r.y.ToString("0")).Append(")-(")
                  .Append(r.z.ToString("0")).Append(",").Append(r.w.ToString("0")).Append(")");
                sb.Append(" sd=").Append(D.V2(root.sizeDelta)).Append(" apos=").Append(D.V2(root.anchoredPosition));
                sb.Append(" texts=").Append(root.GetComponentsInChildren<Text>(true).Length);
                var t = D.Child(root, "Title");
                var b = D.Child(root, "Body");
                sb.Append(" title=\"").Append(D.Esc(t != null ? TextOf(t) : "-")).Append("\"");
                sb.Append(" body=\"").Append(D.Esc(b != null ? TextOf(b) : "-")).Append("\"");
            }
            return sb.ToString();
        }

        private static string TextOf(RectTransform rt)
        {
            var txt = rt.GetComponent<Text>();
            return txt != null ? txt.text : "-";
        }

        private static void MiniMapDiag()
        {
            var mm = D.LiveComponent("Diablo2.UI.MiniMapPanel");
            if (mm == null) { D.Warn("MiniMapPanel not found"); return; }
            var map = D.Field(mm, "_map") as MinimapArgs;
            var explored = D.Field(mm, "_explored") as bool[];
            var pixels = D.Field(mm, "_pixels") as Color32[];
            var markers = D.Field(mm, "_markers") as System.Collections.IList;
            var overlay = D.Field(mm, "_overlay") as RectTransform;
            var raw = D.Field(mm, "_raw") as RawImage;

            var exploredCount = 0;
            if (explored != null) for (var i = 0; i < explored.Length; i++) if (explored[i]) exploredCount++;
            var opaque = 0;
            if (pixels != null) for (var i = 0; i < pixels.Length; i++) if (pixels[i].a > 0) opaque++;

            D.KV("T7-AUTOMAP", "map=" + (map != null ? map.width + "x" + map.height : "null")
                + " explored=" + exploredCount + " texPixels=" + (pixels != null ? pixels.Length : -1)
                + " opaquePixels=" + opaque + " markers=" + (markers != null ? markers.Count : -1)
                + " rawTex=" + (raw != null && raw.texture != null ? 1 : 0)
                + " overlay=" + (overlay != null ? D.V2(overlay.sizeDelta) + "@" + D.V2(overlay.anchoredPosition) : "-")
                + " " + D.Geo("MiniMapRoot", (mm as Component) != null
                    ? (mm as Component).transform as RectTransform : null));
        }

        /// <summary>
        /// Sweep the player along walkable cells (emitting the module's own `Events.PlayerGridChanged`)
        /// and re-measure the automap texture: proves the renderer scales with exploration, i.e. that
        /// "the map looks empty" is the ±1-neighbourhood reveal rule (registered as E23 (4)) and not a
        /// dead draw path.
        /// </summary>
        private static void MiniMapSweepProof()
        {
            var mm = D.LiveComponent("Diablo2.UI.MiniMapPanel");
            if (mm == null) { D.Warn("sweep: MiniMapPanel missing"); return; }
            var map = D.Field(mm, "_map") as MinimapArgs;
            var pl = D.Player();
            if (map == null || pl == null) { D.Warn("sweep: no map/player"); return; }

            var dirs = new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(-1, 0), new Vector2Int(-1, 0) };
            var cur = pl.Grid;
            var steps = 0;
            for (var k = 0; k < dirs.Length; k++)
            {
                for (var t = 0; t < 10; t++)
                {
                    var next = cur + dirs[k];
                    if (next.x < 0 || next.y < 0 || next.x >= map.width || next.y >= map.height) break;
                    var m = D.Map();
                    if (m != null && !m.Walkable(next)) break;
                    cur = next;
                    pl.TeleportTo(cur);
                    Game.Event.Emit<Vector2Int>(Events.PlayerGridChanged, cur);
                    steps++;
                }
            }
            D.SnapCam();

            var pixels = D.Field(mm, "_pixels") as Color32[];
            var explored = D.Field(mm, "_explored") as bool[];
            var opaque = 0;
            var exploredCount = 0;
            if (pixels != null) for (var i = 0; i < pixels.Length; i++) if (pixels[i].a > 0) opaque++;
            if (explored != null) for (var i = 0; i < explored.Length; i++) if (explored[i]) exploredCount++;
            D.KV("T7-AUTOMAP-SWEEP", "steps=" + steps + " explored=" + exploredCount
                + " opaquePixels=" + opaque + " playerGrid=" + D.G(cur));
        }

        private static string EntityState()
        {
            // u44 closeout (select 9-6): `Diablo2.UI.EntityTooltip` (head-top name+hp) was DELETED
            // (contract C4 -- the original only has the screen-top bar).  Reading it by that string
            // now returns NOT-INSTANTIATED forever, i.e. a silent fake-green.  Point it at the new
            // consumer and read its real public state (the bar is BITMAP font => `texts=` stays 0).
            var et = D.LiveComponent("Diablo2.UI.EnemyBarView");
            if (et == null) return "EnemyBarView=NOT-INSTANTIATED";
            var c = et as Component;
            var sb = new StringBuilder("EnemyBarView inst active=");
            sb.Append(c.gameObject.activeInHierarchy ? 1 : 0);
            sb.Append(" barVisible=").Append(D.Prop(et, "BarVisible"));
            sb.Append(" plateVisible=").Append(D.Prop(et, "NameplateVisible"));
            sb.Append(" title=\"").Append(D.Esc(D.Prop(et, "TitleText") as string)).Append("\"");
            sb.Append(" plateText=\"").Append(D.Esc(D.Prop(et, "NameplateText") as string)).Append("\"");
            sb.Append(" value=").Append(D.Prop(et, "BarValue"));
            sb.Append(" max=").Append(D.Prop(et, "BarMaxValue"));
            sb.Append(" texts=").Append(c.gameObject.GetComponentsInChildren<Text>(true).Length);
            sb.Append(" canvases=").Append(c.gameObject.GetComponentsInChildren<Canvas>(true).Length);
            for (var k = 0; k < 2; k++)
            {
                var nm = k == 0 ? "_title" : "_plate";
                var lbl = D.Field(et, nm);
                var root = lbl != null ? D.Prop(lbl, "Root") as RectTransform : null;
                sb.Append(" |").Append(nm).Append(" root=");
                if (root == null) { sb.Append("null"); continue; }
                Vector4 r;
                D.ScreenRect(root, out r);
                sb.Append("scr=(").Append(r.x.ToString("0")).Append(",").Append(r.y.ToString("0")).Append(")-(")
                  .Append(r.z.ToString("0")).Append(",").Append(r.w.ToString("0")).Append(")");
                sb.Append(" sd=").Append(D.V2(root.sizeDelta)).Append(" kids=").Append(root.childCount);
                sb.Append(" active=").Append(root.gameObject.activeInHierarchy ? 1 : 0);
                sb.Append(" fontSize=").Append(D.Field(lbl, "_fontSize"));
                sb.Append(" lines=").Append(D.Field(lbl, "_lineCount"));
                sb.Append(" text=\"").Append(D.Esc(D.Field(lbl, "_text") as string)).Append("\"");
            }
            return sb.ToString();
        }

        // ============================================================ helpers
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
                if (c.name.StartsWith(startsWith, StringComparison.OrdinalIgnoreCase))
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
                if (nm.StartsWith("E_1_")) continue;                  // E_1_* = the player
                if (nm.IndexOf("Npc", StringComparison.OrdinalIgnoreCase) >= 0) continue;  // NPCs are not monsters
                var parts = nm.Split('_');
                if (parts.Length < 2) continue;
                int mid;
                if (!int.TryParse(parts[1], out mid) || mid < 100) continue;   // monster ids are >= 1000
                var d = Vector3.Distance(sr.transform.position, Iso.GridToWorld(pl.Grid));
                if (d < bestD) { bestD = d; best = sr; }
            }
            if (best == null)
            {
                D.Warn("FindMonster: no monster renderer (E_*) found -- area="
                    + (D.Map() != null ? D.Map().Area.ToString() : "-"));
                return;
            }
            var mgrid = Iso.WorldToGrid(best.transform.position);
            _monsterGrid = mgrid;                 // re-fed every frame by the shot loop (camera lerps)
            _monsterPos = best.transform.position;
            var m = D.Map();
            // Stand on the monster's own cell first: then the monster is exactly under the player,
            // i.e. at the screen centre the rig is already looking at (the rig lags/clamps otherwise,
            // measured: approach-by-neighbour left the monster at screen y = -468 / -1332).
            // Stand on a NEIGHBOUR cell (not on the monster): the pointer pick resolves the topmost
            // entity of a cell, so standing on the monster's own cell made the pick resolve to the
            // player (measured 13:35: screen point on screen, yet no hover target -> tooltip hidden).
            var offs = new[] {
                new Vector2Int(1,0), new Vector2Int(-1,0), new Vector2Int(0,1), new Vector2Int(0,-1) };
            for (var k = 0; k < offs.Length; k++)
            {
                var adj = mgrid + offs[k];
                if (m != null && m.Walkable(adj)) { pl.TeleportTo(adj); D.SnapCam(); break; }
            }
            var wp = Iso.GridToWorld(mgrid);
            var sp = Camera.main != null ? (Vector2)Camera.main.WorldToScreenPoint(wp) : Vector2.zero;
            D.MouseState(sp, false); _mouse = sp;
            D.KV("T8-MONSTER", "node=" + best.gameObject.name + " cell=" + D.G(mgrid)
                + " screen=" + sp.x.ToString("0") + "," + sp.y.ToString("0") + " playerGrid=" + D.G(pl.Grid));
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
            D.WriteFile(D.DonePath, "V6-DONE why=" + why + " tiles=" + _shots + "/" + N
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
