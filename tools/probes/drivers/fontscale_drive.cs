// =============================================================================
// fontscale_drive.cs -- ONE Play session that judges the "text too small" fix.
//
// DEFECT FAMILY (V6 report 4-(2)): `D2Label.Create` call sites that omitted the
// 9th argument `fontSize`. Its default 0 means "draw at ORIGINAL bitmap pixels
// 1:1", and the canvas is 1920x1080 while the original baseline is 800x600, so
// those labels rendered at ~1/1.8 = 55% of the intended size. The fix passes
// `(int)UiLayoutGame.FontPx16` (= 28) instead of relying on the default.
//
// WHAT THIS DRIVER PROVES (number first, tile second -- same discipline as
// v6_drive.cs, so a missing tile can never be mistaken for a missing feature):
//   * effective glyph height in CANVAS px = (screen height of the rendered
//     glyph child) / Canvas.scaleFactor. Expected: 28 for the Font16 labels
//     (FontPx16 = 28.8 -> (int)28) and 54 for the level title (FontPx30 = 54).
//     Before the fix the same number was ~16 (chi cell height, unscaled).
//   * rendered LINE COUNT (distinct glyph rows). This is the cross-check the
//     task asked for: "text got bigger but the box did not" shows up here as
//     an unexpected wrap (2 rows where 1 is expected).
//
// CHAIN (one Play session): boot -> menu -> char select -> stage from save ->
//   T0 HUD (orb Life/Mana numbers) -> T1 inventory (cell counts + gold) ->
//   T2 shop (title / hint / cell count / gold) -> T3 character panel (stat /
//   derived / extra / resist values) -> T4 level title -> T5 ground nameplate.
//
// PUBLIC ENTRIES: FS.Api.Cfg / FS.Api.Paths / FS.Api.Ping / FS.Tour.Install
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
using UnityEngine.UI;

namespace FS
{
    internal static class D
    {
        internal const string Tag = "FS";
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
                   + " kids=" + rt.childCount + " act=" + (rt.gameObject.activeInHierarchy ? 1 : 0)
                   + " scale=" + rt.localScale.x.ToString("0.##");
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
            var go = new GameObject("FontScaleEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<FSDriver>();
            drv.Init(spec);
            return "INSTALLED";
        }
    }

    public class FSDriver : MonoBehaviour
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
        private bool _capDone, _capOk;
        private string _capInfo = "";
        private string _capPending = "";
        private int _shots;

        private static readonly string[] Names =
        {
            "fontscale_01_hud.png",          // T0 orb Life/Mana numbers
            "fontscale_02_inventory.png",    // T1 cell counts + gold
            "fontscale_03_shop.png",         // T2 title / hint / count / gold
            "fontscale_04_character.png",    // T3 stat / derived / extra / resist values
            "fontscale_05_leveltitle.png",   // T4 area name
            "fontscale_06_nameplate.png",    // T5 ground item nameplate
        };
        private const int N = 6;

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

            Add("hud-dump", () =>
            {
                Dump("T0", "LifeText");
                Dump("T0", "ManaText");
                Dump("T0", "HotkeyLabel");
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
                D.KV("SUMMARY", "tiles=" + _shots + "/" + N + " fsm=" + D.Fsm() + " " + D.DeviceLine()
                    + " " + D.BitmapState());
                return true;
            });
            Add("done", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private static int WaitFor(int i)
        {
            switch (i)
            {
                case 0: return 30;   // HUD: chi font + full rebuild + orb number refresh
                case 4: return 30;   // level title: land inside the fade-in window
                default: return 14;
            }
        }

        // ============================================================ per-tile setup
        private void Setup(int i)
        {
            switch (i)
            {
                case 0: // HUD wide shot: orb numbers + hotkeys + belt counts
                {
                    D.Log("T0-HUD " + D.BitmapState());
                    Dump("T0", "Count");        // belt cell counts are the only "Count" nodes alive here
                    break;
                }

                case 1: // inventory: cell counts + gold
                {
                    EnsureInventoryItem();
                    StackMore();     // a stack of 1 draws no number => push more so a Count node is non-empty
                    Game.Event.Emit<string>(Events.PanelToggleRequest, "InventoryPanel");
                    D.KV("T1-INV", "open=" + (Game.UI.IsOpen<Diablo2.UI.InventoryPanel>() ? 1 : 0));
                    Dump("T1", "Count");
                    Dump("T1", "Gold");
                    break;
                }

                case 2: // shop: title / hint / cell count / gold
                {
                    Game.UI.Close<Diablo2.UI.InventoryPanel>();
                    var sa = new Diablo2.Def.ShopOpenArgs();
                    sa.npcId = 0;
                    sa.npcName = "\u963f\u5361\u62c9";
                    sa.canRepair = true;
                    var it = D.Item();
                    sa.playerGold = it != null ? it.Gold : 0;
                    Game.Event.Emit<Diablo2.Def.ShopOpenArgs>(Events.ShopOpen, sa);
                    D.KV("T2-SHOP", "open=" + (Game.UI.IsOpen<Diablo2.UI.ShopPanel>() ? 1 : 0));
                    Dump("T2", "ShopTitle");
                    Dump("T2", "ShopHint");
                    Dump("T2", "Count");
                    Dump("T2", "Gold");
                    break;
                }

                case 3: // character panel: stat / derived / extra / resist values
                {
                    Game.UI.Close<Diablo2.UI.ShopPanel>();
                    Game.Event.Emit<string>(Events.PanelToggleRequest, "CharacterPanel");
                    D.KV("T3-CHAR", "open=" + (Game.UI.IsOpen<Diablo2.UI.CharacterPanel>() ? 1 : 0));
                    Dump("T3", "StatValue");
                    Dump("T3", "DerivedValue");
                    Dump("T3", "ExtraValue");
                    Dump("T3", "ResistValue");
                    break;
                }

                case 4: // level title (area name): emitted through the production event
                {
                    Game.UI.Close<Diablo2.UI.CharacterPanel>();
                    var vals = Enum.GetValues(typeof(Diablo2.Def.AreaId));
                    var pick = vals.Length > 1 ? (Diablo2.Def.AreaId)vals.GetValue(1) : (Diablo2.Def.AreaId)vals.GetValue(0);
                    D.KV("T4-AREA", "emit area=" + pick + " (idx 1 of " + vals.Length
                        + ", current=" + (D.Map() != null ? D.Map().Area.ToString() : "-") + ")");
                    Game.Event.Emit<Diablo2.Def.AreaId>(Events.AreaChanged, pick);
                    Dump("T4", "Title");
                    break;
                }

                case 5: // ground item nameplate (synthetic payload on the production event)
                {
                    var pl = D.Player();
                    var g = pl != null ? pl.Grid : new Vector2Int(0, 0);
                    var args = new Diablo2.Def.GroundItemLabelsArgs();
                    args.altHeld = true;
                    args.labels.Add(new Diablo2.Def.GroundItemLabel
                    {
                        id = GameConst.GroundItemIdBase + 1,
                        name = "\u5fae\u578b\u6cbb\u7597\u836f\u6c34",
                        quality = Diablo2.Def.ItemQuality.Normal,
                        gridX = g.x,
                        gridY = g.y,
                    });
                    Game.Event.Emit<Diablo2.Def.GroundItemLabelsArgs>(Events.GroundItemLabelsChanged, args);
                    D.KV("T5-NAMEPLATE", "emitted labels=" + args.labels.Count + " at=" + D.G(g));
                    Dump("T5", "GroundItemLabel_");
                    break;
                }
            }
        }

        // ============================================================ measurement
        /// <summary>
        /// Dump every live node whose name equals `name` (or starts with it when it
        /// ends with '_') plus its rendered glyph metrics:
        ///   glyphs = rendered glyph blocks, rows = distinct glyph rows (this is the
        ///   "text got bigger but the box did not" wrap cross-check),
        ///   glyphH = effective glyph height in CANVAS px
        ///   (screen height / Canvas.scaleFactor), boxW/boxH = label rect in canvas px.
        /// </summary>
        private static void Dump(string tag, string name)
        {
            var hits = name.EndsWith("_", StringComparison.Ordinal)
                ? D.FindWithPrefix(name)
                : D.FindAllNamed(name);
            // Fallback: the character panel labels are named StatValue0..3 / ResistValue0..3
            // (an index suffix), so an exact-name lookup finds nothing.
            if (hits.Count == 0 && !name.EndsWith("_", StringComparison.Ordinal))
                hits = D.FindWithPrefix(name);
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

        private void EnsureInventoryItem()
        {
            var it = D.Item();
            if (it == null) { D.Warn("EnsureInventoryItem: no IItemModule"); return; }
            for (var i = 0; i < it.Inventory.Count; i++)
            {
                var s = it.Inventory[i];
                if (s != null && s.item != null) { D.KV("INV-ITEM", "existing items present"); return; }
            }
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

        /// <summary>
        /// A stack of 1 draws no number (the original shows a count only for stacks), so its
        /// "Count" node stays empty and cannot be measured. Push 2 more stacks so at least one
        /// cell carries a number; the log says what actually happened.
        /// </summary>
        private void StackMore()
        {
            var it = D.Item();
            if (it == null) { D.Warn("StackMore: no IItemModule"); return; }
            for (var n = 0; n < 2; n++)
            {
                try
                {
                    var rngType = D.FindType("Diablo2.Def.Rng");
                    object rng = rngType != null ? Activator.CreateInstance(rngType, new object[] { 20000 + n }) : null;
                    var mi = it.GetType().GetMethod("CreateRandom", BindingFlags.Public | BindingFlags.Instance);
                    var stack = mi != null ? mi.Invoke(it, new object[] { 1, rng }) : null;
                    var addOk = stack != null && it.AddToInventory((Diablo2.Def.ItemStack)stack);
                    var used = 0;
                    for (var i = 0; i < it.Inventory.Count; i++)
                    {
                        var s = it.Inventory[i];
                        if (s != null && s.item != null) used++;
                    }
                    D.KV("STACK-ADD-" + n, "added=" + (addOk ? 1 : 0) + " occupiedCells=" + used);
                }
                catch (Exception e) { D.Warn("STACK-ADD-FAIL " + n + " " + e.GetType().Name + ": " + e.Message); }
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
            D.WriteFile(D.DonePath, "FONTSCALE-DONE why=" + why + " tiles=" + _shots + "/" + N
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
