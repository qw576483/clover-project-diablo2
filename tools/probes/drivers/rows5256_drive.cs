// =============================================================================
// rows5256_drive.cs -- ONE Play session that judges acceptance-row 56
//   ("presentation: ground item nameplate -- hover shows the item name, holding
//    Alt shows them all").
//
// WHY THIS EXISTS (the criterion is presentation-class, so it must be a picture):
//   The offline host `tools/probes/hosts/uicheck` pins the nameplate GEOMETRY and
//   STYLE (21-1..21-8) but it cannot render: `GroundItemLabelView` is a
//   non-MonoBehaviour `internal sealed class` and the Uicheck host has no Unity
//   native objects.  The row's remaining gap was therefore "no live frame".
//   This driver produces that frame from the REAL chain, not a synthetic payload:
//     1. a real ground item is dropped through `IItemModule.DropToGround`
//        (the same production entry monster loot uses);
//     2. a REAL mouse position is queued on the InputSystem mouse device at the
//        item's projected cell centre -- so `InputReader` -> `HoverPicker.Resolve`
//        -> `PublishGroundItemLabels(altHeld:false)` emits the single hover label;
//     3. `LeftAlt` is then held on the InputSystem keyboard device -- so
//        `PlayerModule.Tick` -> `_input.ShowGroundItems` (KeyShowGroundItems =
//        LeftAlt) -> `PublishGroundItemLabels(altHeld:true)` emits all labels.
//   Two tiles are taken (hover / alt) plus the runtime node metrics (glyph height
//   in CANVAS px + rendered row count) so the "is it really the same magnitude as
//   the rest of the HUD text" question has a number as well as pixels.
//
// The mouse/keyboard injection mirrors `tools/probes/drivers/x_drive.cs`
// (`MouseState` / `KeyboardState` + `InputSystem.QueueStateEvent`); the boot ->
// stage chain mirrors `tools/probes/drivers/fontscale_drive.cs`.
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

namespace RS
{
    internal static class D
    {
        internal const string Tag = "RS";
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
        internal static Diablo2.Module.IViewModule View() { return CtxMember("View") as Diablo2.Module.IViewModule; }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string G(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }
        internal static string V2(Vector2 v) { return v.x.ToString("0.#") + "x" + v.y.ToString("0.#"); }

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
        internal static List<RectTransform> FindWithPrefix(string prefix)
        {
            var hits = new List<RectTransform>();
            var all = UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (t.name != null && t.name.StartsWith(prefix, StringComparison.Ordinal)) hits.Add(t);
            }
            hits.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return hits;
        }
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
        internal static float CanvasScale(Transform t)
        {
            var canvas = t != null ? t.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }
        internal static string Geo(string label, RectTransform rt)
        {
            if (rt == null) return label + "=MISSING";
            Vector4 s;
            ScreenRect(rt, out s);
            return label + "[" + rt.name + "] scr=(" + s.x.ToString("0") + "," + s.y.ToString("0") + ")-("
                   + s.z.ToString("0") + "," + s.w.ToString("0")
                   + ") rect=" + V2(rt.rect.size) + " apos=" + V2(rt.anchoredPosition)
                   + " kids=" + rt.childCount + " act=" + (rt.gameObject.activeInHierarchy ? 1 : 0);
        }

        internal static string DeviceLine()
        {
            var dev = "(unknown)"; var typ = "(unknown)";
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { typ = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            return "device=\"" + dev + "\" type=" + typ + " res=" + Screen.width + "x" + Screen.height;
        }
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

        // ------------------------------------------------------------ input injection
        internal static Mouse MouseDev()
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }
        internal static Keyboard KeyboardDev()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }
        internal static void MouseTo(Vector2 pos)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseTo: no mouse device"); return; }
            InputSystem.QueueStateEvent(m, new MouseState { position = pos });
        }
        internal static void KeyState(Key k, bool held)
        {
            var kb = KeyboardDev();
            if (kb == null) { Warn("KeyState: no keyboard device"); return; }
            InputSystem.QueueStateEvent(kb, held ? new KeyboardState(k) : new KeyboardState());
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
            D.Log("CFG frame=" + Time.frameCount + " " + D.DeviceLine());
            return "CFG-OK";
        }

        public static string Paths(string spec) { D.Paths(spec); return "PATHS-OK"; }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("Rows5256EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<RSDriver>();
            drv.Init(spec);
            return "INSTALLED";
        }
    }

    public class RSDriver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;
        private int _stFrame = -1;
        private string _saveName = "";

        // ground item state
        private int _itemId = -1;
        private Vector2Int _itemGrid = new Vector2Int(int.MinValue, int.MinValue);
        private readonly List<Vector2> _candidates = new List<Vector2>();
        private int _candIx;
        private bool _hoverHit;

        // runtime read-backs (subscribed, not guessed)
        private string _hover = "(none)";
        private string _labels = "(none)";
        private int _hoverEvents, _labelEvents;
        private int _lblHover = -1, _lblAlt = -1;

        private static readonly string[] Names =
        {
            "rows5256_itemlabel_hover.png",
            "rows5256_itemlabel_alt.png",
        };
        private const int N = 2;
        private int _shotIndex = -1;
        private int _shotStage;
        private bool _capDone, _capOk;
        private string _capInfo = "";
        private string _capPending = "";
        private int _shots;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            D.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            D.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                  + " isPlaying=" + (Application.isPlaying ? 1 : 0));
            Subscribe();
            BuildPlan();
            D.KV("PLAN", "stations=" + _plan.Count + " tiles=" + N);
        }

        private void Subscribe()
        {
            try
            {
                Game.Event.On<Diablo2.Def.HoverTarget>(Diablo2.Core.Events.HoverTargetChanged, OnHover);
                Game.Event.On<Diablo2.Def.GroundItemLabelsArgs>(Diablo2.Core.Events.GroundItemLabelsChanged, OnLabels);
                D.KV("SUBSCRIBE", "events=HoverTargetChanged,GroundItemLabelsChanged (driver assembly)");
            }
            catch (Exception e) { D.Warn("SUBSCRIBE-FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private void OnHover(Diablo2.Def.HoverTarget t)
        {
            _hoverEvents++;
            _hover = "hasTarget=" + (t.hasTarget ? 1 : 0) + " cursor=" + t.cursor + " id=" + t.id
                     + " cell=(" + t.gridX + "," + t.gridY + ") name=\"" + t.name + "\"";
        }

        private void OnLabels(Diablo2.Def.GroundItemLabelsArgs a)
        {
            _labelEvents++;
            if (a == null) { _labels = "(null)"; return; }
            var n = a.labels != null ? a.labels.Count : -1;
            if (a.altHeld) _lblAlt = n; else _lblHover = n;
            var first = (n > 0) ? ("#" + a.labels[0].id + " \"" + a.labels[0].name + "\" q=" + a.labels[0].quality
                                  + " cell=(" + a.labels[0].gridX + "," + a.labels[0].gridY + ")") : "(empty)";
            _labels = "altHeld=" + (a.altHeld ? 1 : 0) + " labels=" + n + " first=" + first;
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
                // first frame only: align the camera so the projected cell centre is the real landing point
                if (_stFrame < 0) { _stFrame = Time.frameCount; D.SnapCam(); return false; }
                if (Time.frameCount - _stFrame < 240) return false;
                var m = D.Map();
                D.KV("STAGE-READY", "fsm=" + D.Fsm() + " area=" + (m != null ? m.Area.ToString() : "-")
                    + " size=" + (m != null ? m.Width + "x" + m.Height : "-")
                    + " playerGrid=" + (D.Player() != null ? D.G(D.Player().Grid) : "-")
                    + " " + D.DeviceLine() + " " + D.BitmapState());
                return true;
            });

            Add("drop-item", () =>
            {
                var it = D.Item();
                if (it == null) { D.Warn("drop-item: no IItemModule"); return true; }
                var pl = D.Player();
                var baseGrid = pl != null ? pl.Grid : new Vector2Int(0, 0);
                var map = D.Map();

                // The Alt criterion is "hold Alt -> EVERY ground item shows", so more than one
                // item must exist, otherwise the hover tile and the Alt tile are indistinguishable.
                // Distinct walkable cells come from the production query (IMapModule.Walkable).
                var cells = new List<Vector2Int>();
                var offs = new[] { new Vector2Int(2, 0), new Vector2Int(-2, 0), new Vector2Int(0, 2),
                                   new Vector2Int(0, -2), new Vector2Int(1, 0), new Vector2Int(-1, 0) };
                for (var i = 0; i < offs.Length && cells.Count < 3; i++)
                {
                    var c = new Vector2Int(baseGrid.x + offs[i].x, baseGrid.y + offs[i].y);
                    var ok = map == null || map.Walkable(c);
                    D.KV("DROP-CELL", "cand=" + D.G(c) + " walkable=" + (ok ? 1 : 0));
                    if (ok) cells.Add(c);
                }
                if (cells.Count == 0) cells.Add(baseGrid);

                var rng = new CloverEngine.Rng(4242);
                var mi = it.GetType().GetMethod("CreateRandom", BindingFlags.Public | BindingFlags.Instance);
                for (var i = 0; i < cells.Count; i++)
                {
                    try
                    {
                        var stack = mi != null ? (Diablo2.Def.ItemStack)mi.Invoke(it, new object[] { 1, rng }) : null;
                        if (stack == null)
                        {
                            var inv = it.Inventory;
                            if (inv != null)
                                for (var k = 0; k < inv.Count; k++)
                                {
                                    var cc = inv[k];
                                    if (cc != null && cc.item != null) { stack = cc.item; break; }
                                }
                        }
                        if (stack == null) { D.Warn("drop-item: no ItemStack available"); break; }
                        it.DropToGround(stack, cells[i]);
                        D.KV("DROP", "i=" + i + " item=\"" + stack.name + "\" q=" + stack.quality
                            + " grid=" + D.G(cells[i]) + " (IItemModule.DropToGround)");
                    }
                    catch (Exception e) { D.Warn("drop-item EX i=" + i + " " + e.GetType().Name + ": " + e.Message); }
                }

                // read the ids back from the production list (not assumed) and pick the one
                // that actually sits on the hover cell
                var ground = it.GroundItems;
                var view = D.View();
                var dropped = 0;
                if (ground != null)
                {
                    for (var i = 0; i < ground.Count; i++)
                    {
                        var kv = ground[i];
                        var vg = view != null ? view.GetView(kv.Key) : null;
                        D.KV("DROP-ID", "groundItemId=" + kv.Key + " name=\"" + (kv.Value != null ? kv.Value.name : "?")
                            + "\" view=" + (vg != null ? "ok" : "(null)")
                            + " world=" + (vg != null ? D.V2(new Vector2(vg.transform.position.x, vg.transform.position.y)) : "-"));
                        dropped++;
                    }
                    // hover cell = the first dropped cell (cells[0]); prefer the item that lands there
                    _itemGrid = ground.Count > 0 ? cells[0] : baseGrid;
                    for (var i = 0; i < ground.Count; i++)
                    {
                        var vg = view != null ? view.GetView(ground[i].Key) : null;
                        if (vg == null) continue;
                        if (Iso.WorldToGrid(vg.transform.position) == _itemGrid) { _itemId = ground[i].Key; break; }
                    }
                    if (_itemId < 0) _itemId = ground[0].Key;
                }
                D.KV("DROP-TOTAL", "groundItems=" + dropped + " hoverId=" + _itemId + " hoverCell=" + D.G(_itemGrid)
                    + " (the Alt tile must show ALL of them)");

                // candidate inject points: cell centre, then the same point with Y flipped
                var p = CellScreen(_itemGrid);
                _candidates.Clear();
                _candidates.Add(p);
                _candidates.Add(new Vector2(p.x, Screen.height - p.y));
                D.KV("CAND", "n=" + _candidates.Count + " p0=" + D.V2(_candidates[0]) + " p1=" + D.V2(_candidates[1])
                    + " screen=" + Screen.width + "x" + Screen.height);
                _candIx = 0;
                return true;
            });

            Add("hover-probe", () =>
            {
                if (_itemId < 0) return true;
                if (_candIx >= _candidates.Count) return true;
                var pt = _candidates[_candIx];
                D.MouseTo(pt);
                if (_stFrame < 0) { _stFrame = Time.frameCount; return false; }
                if (Time.frameCount - _stFrame < 45) return false;
                var hit = _hoverEvents > 0 && _hover.Contains("id=" + _itemId + " ") && _hover.Contains("cursor=Pickup");
                D.KV("HOVER-PROBE", "cand=" + _candIx + " at=" + D.V2(pt)
                    + " events=" + _hoverEvents + " labels=" + _labelEvents
                    + " hover=\"" + _hover + "\" labelsLast=\"" + _labels + "\" hit=" + (hit ? 1 : 0));
                if (hit) { _hoverHit = true; return true; }
                _candIx++; _stFrame = -1;
                return false;
            });

            Add("hover-settle", () =>
            {
                if (_itemId < 0) return true;
                if (!_hoverHit && _candIx >= _candidates.Count)
                {
                    D.Warn("hover-settle: no candidate produced a Pickup hover on the dropped item -- the tile will show the honest miss");
                    return true;
                }
                if (!Elapsed(1.2f)) return false;
                D.KV("HOVER-SETTLE", "hover=\"" + _hover + "\" labels=\"" + _labels + "\"");
                Dump("HOVER", "GroundItemLabel_");
                Dump("HOVER", "LifeText");
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
                        if (Time.frameCount - _stFrame < 30) return false;
                        _shotStage = 2;
                        return false;
                    case 2:
                        if (!CaptureFull(D.RawDir + "/" + Names[_shotIndex])) return false;
                        _shotStage = 0; _shotIndex++;
                        return _shotIndex >= N;
                }
                return false;
            });

            Add("alt-off", () => { D.KeyState(Key.LeftAlt, false); return true; });

            Add("finish", () =>
            {
                D.KV("SUMMARY", "tiles=" + _shots + "/" + N + " hoverHit=" + (_hoverHit ? 1 : 0)
                    + " gid=" + _itemId + " hoverLabels=" + _lblHover + " altLabels=" + _lblAlt
                    + " fsm=" + D.Fsm() + " " + D.DeviceLine() + " " + D.BitmapState());
                return true;
            });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private void Setup(int i)
        {
            switch (i)
            {
                case 0: // hover tile: keep the real mouse on the item, Alt NOT held
                {
                    D.KeyState(Key.LeftAlt, false);
                    if (_hoverHit) D.MouseTo(_candidates[Mathf.Min(_candIx, _candidates.Count - 1)]);
                    D.KV("T-HOVER", "altHeld=0 hover=\"" + _hover + "\" labels=\"" + _labels + "\"");
                    Dump("T5a", "GroundItemLabel_");
                    break;
                }
                case 1: // alt tile: hold the real Alt key (KeyShowGroundItems = LeftAlt)
                {
                    D.KeyState(Key.LeftAlt, true);
                    if (_hoverHit) D.MouseTo(_candidates[Mathf.Min(_candIx, _candidates.Count - 1)]);
                    D.KV("T-ALT", "altHeld=1 hover=\"" + _hover + "\" labels=\"" + _labels + "\"");
                    Dump("T5b", "GroundItemLabel_");
                    break;
                }
            }
        }

        private static Vector2 CellScreen(Vector2Int g)
        {
            var cam = Camera.main;
            if (cam == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var sp = cam.WorldToScreenPoint(Iso.GridToWorld(g));
            return new Vector2(sp.x, sp.y);
        }

        /// <summary>
        /// Dump every live node whose name starts with `name` plus its rendered glyph
        /// metrics: glyphs = rendered glyph blocks, rows = distinct glyph rows (the
        /// "text got bigger but the box did not" wrap cross-check), glyphH = effective
        /// glyph height in CANVAS px (screen height / Canvas.scaleFactor).
        /// </summary>
        private static void Dump(string tag, string name)
        {
            var hits = D.FindWithPrefix(name);
            if (hits.Count == 0) { D.KV(tag + "-NODE-" + name, "0 nodes (missing?)"); return; }
            for (var i = 0; i < hits.Count; i++)
            {
                var rt = hits[i];
                var scale = D.CanvasScale(rt);
                var glyphs = D.Child(rt, "Glyphs");
                var rows = new List<string>();
                RectTransform first = null;
                if (glyphs != null)
                {
                    for (var k = 0; k < glyphs.childCount; k++)
                    {
                        var g = glyphs.GetChild(k) as RectTransform;
                        if (g == null) continue;
                        if (first == null) first = g;
                        var y = g.anchoredPosition.y.ToString("0.#");
                        if (!rows.Contains(y)) rows.Add(y);
                    }
                }
                var glyphH = 0f;
                if (first != null)
                {
                    Vector4 gr;
                    if (D.ScreenRect(first, out gr) && scale > 0f) glyphH = (gr.w - gr.y) / scale;
                }
                D.Log(tag + "-NODE i=" + i + " node=" + rt.name
                    + " act=" + (rt.gameObject.activeInHierarchy ? 1 : 0)
                    + " glyphs=" + (glyphs != null ? glyphs.childCount : -1)
                    + " rows=" + rows.Count
                    + " glyphH=" + glyphH.ToString("0.#") + "cp"
                    + " boxW=" + rt.rect.width.ToString("0.#") + "cp"
                    + " boxH=" + rt.rect.height.ToString("0.#") + "cp"
                    + " canvasScale=" + scale.ToString("0.###")
                    + " pos=" + D.Geo("", rt));
            }
        }

        // ============================================================ capture
        private bool CaptureFull(string file)
        {
            if (_capDone) { _capDone = false; _shots++; D.KV("TILE", "file=" + _capPending + " ok=" + (_capOk ? 1 : 0) + " " + _capInfo); return true; }
            if (_capPending == file) return false;
            _capPending = file;
            StartCoroutine(CaptureRoutine(file));
            return false;
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
            D.WriteFile(D.DonePath, "ROWS5256-DONE why=" + why + " tiles=" + _shots + "/" + N
                + " hoverHit=" + (_hoverHit ? 1 : 0) + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
