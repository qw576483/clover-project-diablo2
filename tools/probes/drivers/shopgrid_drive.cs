// =============================================================================
//
//   run_script --file tools/probes/drivers/shopgrid_drive.cs --entry ShopGrid.Tour.Install
//              --args '["<raw shot dir>|<done marker>"]'
//
// WHAT IT MEASURES (user symptom: "商店商品占的格子不对" = the shop items sit in the wrong cells)
//   SHOPGRID-DUMP   page=buy  per item: index / name / gridW x gridH / anchor cell (col,row) /
//                   price / the ICON node's rect (canvas units, must be w*h cells) / sprite name
//                   plus the whole 10x10 OWNER map (row0..row9), the occupied-cell count and the
//                   cell pitch (ShopPanel.CellSize).
//   SHOPGRID-CLICK  page=buy  click a cell that is COVERED BY a multi-cell item but is NOT that
//                   item's anchor cell; then read the gold delta. The old code mapped
//                   cell -> stock[cell] (an equality that only holds for 1x1 items), so this is
//                   the regression the fix must pass: the bought item is the one that OWNS the
//                   cell, not the one whose index equals the cell number.
//   SHOPGRID-DUMP   page=sell same dump after the real Tab1 click (the sell page path).
//   Tiles: <raw>/shop_cells_buy.png and <raw>/shop_cells_sell.png (screen composite).
//
// WHY IT MUST BE A PLAY SESSION: the owner map / icon rects / the click->purchase chain only
// exist at runtime (the panel is built by HudPanel on Events.ShopOpen; the purchase goes
// Panel -> Events.ShopBuyRequest -> NpcModule.Buy -> IItemModule gold settlement).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI). No engine timers:
// the plan is polled from Update (engine skill P-3).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ShopGrid
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    internal static class Drive
    {
        internal const string Tag = "SG";

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;
        internal static string RawDir { get { return _rawDir; } }
        internal static string DonePath { get { return _done; } }

        // ---- logging -------------------------------------------------------------
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
        internal static string ShotFile(string name) { return _rawDir + "/" + name; }

        // ---- reflection ----------------------------------------------------------
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
        internal static INpcModule Npc() { return CtxMember("Npc") as INpcModule; }
        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }
        internal static IItemModule Item() { return CtxMember("Item") as IItemModule; }
        internal static ISaveModule Save() { return CtxMember("Save") as ISaveModule; }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }
        internal static int[] IntArrayField(object o, string n) { return Field(o, n) as int[]; }

        // ---- ui nodes ------------------------------------------------------------
        internal static GameObject Node(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.name == name) return t.gameObject;
            }
            return null;
        }
        internal static string RectOf(GameObject go)
        {
            if (go == null) return "(none)";
            var rt = go.transform as RectTransform;
            if (rt == null) return "(no-rect)";
            return "size=" + rt.sizeDelta.x.ToString("0.0") + "x" + rt.sizeDelta.y.ToString("0.0")
                + " xy=" + rt.anchoredPosition.x.ToString("0.0") + "," + rt.anchoredPosition.y.ToString("0.0")
                + " wh=" + rt.rect.width.ToString("0.0") + "x" + rt.rect.height.ToString("0.0");
        }
        internal static Image ImgOf(GameObject go)
        {
            return go != null ? go.GetComponent<Image>() : null;
        }
        internal static string SpriteOf(GameObject go)
        {
            var img = ImgOf(go);
            return img != null && img.sprite != null ? img.sprite.name : "none";
        }
        internal static bool InvokeButton(GameObject go)
        {
            if (go == null) return false;
            var b = go.GetComponent<Button>();
            if (b == null) { Warn("InvokeButton: node " + go.name + " has no Button"); return false; }
            b.onClick.Invoke();
            return true;
        }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        internal static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }
        internal static string State()
        {
            var p = Player();
            var g = p != null ? p.Grid : Vector2Int.zero;
            return "fsm=" + Fsm() + " scene=" + SceneName()
                + " hud=" + (Game.UI != null && Game.UI.IsOpen<HudPanel>() ? 1 : 0)
                + " dialog=" + (Game.UI != null && Game.UI.IsOpen<NpcDialogPanel>() ? 1 : 0)
                + " shop=" + (Game.UI != null && Game.UI.IsOpen<ShopPanel>() ? 1 : 0)
                + " gold=" + (p != null ? p.Gold : -1)
                + " grid=(" + g.x + "," + g.y + ")";
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        /// <summary>Keep the loop ticking while the editor is unfocused (same as the other drivers).</summary>
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            try
            {
                var st = UnityEngine.InputSystem.InputSystem.settings;
                st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
                st.editorInputBehaviorInPlayMode =
                    UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            }
            catch (Exception e) { Drive.Warn("CFG input-system settings EX " + e.GetType().Name); }

            var line = "SHOPGRID-CFG gameRunning=" + (Game.IsRunning ? 1 : 0)
                + " vSync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate
                + " screen=" + Screen.width + "x" + Screen.height
                + " " + Drive.State();
            Drive.Log(line);
            return line;
        }

        public static string Paths(string spec) { return Drive.Paths(spec); }
        public static string StateNow() { return Drive.State(); }
    }

    /// <summary>Installer. spec = "&lt;raw dir&gt;|&lt;done marker&gt;".</summary>
    public static class Tour
    {
        /// <summary>
        /// Fixed fallback paths when no `--args` spec is supplied (the CLI wants a JSON array for
        /// `--args`; passing none keeps the run script simple). Override with
        /// `--args '["&lt;raw dir&gt;|&lt;done&gt;"]'` if the repo moves.
        /// </summary>
        private const string DefaultRawDir = "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots";
        private const string DefaultDone = "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/shopgrid_done.txt";

        /// <summary>Zero-argument entry (the CLI requires an argument for `Install(string)`).</summary>
        public static string Run() { return Install(null); }

        public static string Install(string spec)
        {
            var go = new GameObject("ShopGridDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            if (string.IsNullOrEmpty(spec)) spec = DefaultRawDir + "|" + DefaultDone;
            drv.Init(spec);
            Drive.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One short tour: boot -> empty stage with the first save -> open Charsi's shop through the
    /// real NPC chain -> dump the occupancy -> click a COVERED (non-anchor) cell and check the
    /// gold delta -> sell page (Tab1) -> dump -> finish. Values only, no self-praise.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;

        private string _saveName = string.Empty;
        private string _shopPath = string.Empty;   // "ShopOpenRequest" | "dialog:ChooseOption(n)" | "fallback:Game.UI.Open"
        private int _itemsBuy;
        private int _anchorCell = -1;
        private int _coveredCell = -1;
        private int _ownerAtCovered = -1;
        private string _expectedName = string.Empty;
        private int _expectedPrice = -1;
        private int _stockAtCoveredIndex = -1;
        private string _stockAtCoveredName = string.Empty;
        private int _goldBefore;
        private int _goldAfter;
        private readonly List<string> _lines = new List<string>();

        private const float BootTimeout = 30f;
        private const float StageTimeout = 90f;
        private const float ShopTimeout = 12f;
        private const float ShotTimeout = 15f;

        private float _shotAt = -1f;
        private string _shotFile = string.Empty;

        /// <summary>When the current shop-open attempt was fired (each attempt gets its own 4 s).</summary>
        private float _shopTryAt = -1f;

        // The 10x10 board. `UiLayoutGame` is `internal` to the project assembly (not visible to the
        // in-memory run_script assembly), so the two constants are repeated here and cross-checked
        // against the public `ShopPanel.CellCount` at run time. The pinned offline host
        // (tools/probes/hosts/uicheck/ShopGridCheck.cs) proves 10x10 against the original PNG pixels.
        private const int Cols = 10;
        private const int Rows = 10;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            Drive.Paths((p.Length > 0 ? p[0] : "") + "|" + (p.Length > 1 ? p[1] : ""));
            Drive.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height);
            BuildPlan();
            Drive.KV("PLAN", "stations=" + _plan.Count);
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
                if (_pc >= _plan.Count) Finish("end");
            }
            catch (Exception e)
            {
                Drive.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        // ---- station plumbing ----------------------------------------------------
        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            var fn = f;
            _plan.Add(() =>
            {
                if (_cur != nm)
                {
                    _cur = nm;
                    _at = Time.unscaledTime;
                    Drive.Log("PHASE " + nm + " " + Drive.State());
                }
                return fn();
            });
        }
        private bool Elapsed(float seconds) { return Time.unscaledTime - _at >= seconds; }
        private bool Tout(string what, float seconds)
        {
            if (!Elapsed(seconds)) return false;
            Drive.KV("STATION-TIMEOUT", "at=" + what + " " + Drive.State());
            Note("timeout:" + what);
            return true;
        }
        private void Note(string s) { _lines.Add(s); }

        private void Shoot(string name)
        {
            _shotFile = Drive.ShotFile(name);
            _shotAt = Time.unscaledTime;
            try { if (File.Exists(_shotFile)) File.Delete(_shotFile); } catch { }
            ScreenCapture.CaptureScreenshot(_shotFile);
            Drive.KV("SHOT-REQUEST", "file=" + name + " frame=" + Time.frameCount);
        }
        private bool ShotLanded()
        {
            if (_shotFile.Length == 0) return true;
            if (File.Exists(_shotFile) && new FileInfo(_shotFile).Length > 1000)
            {
                Drive.KV("SHOT-OK", "file=" + Path.GetFileName(_shotFile)
                    + " bytes=" + new FileInfo(_shotFile).Length);
                _shotFile = string.Empty;
                return true;
            }
            if (Time.unscaledTime - _shotAt > ShotTimeout)
            {
                Drive.Warn("SHOT-TIMEOUT file=" + _shotFile);
                _shotFile = string.Empty;
                return true;
            }
            return false;
        }

        // ---- plan ----------------------------------------------------------------
        private void BuildPlan()
        {
            // ---------------------------------------------------------------- boot
            Add("boot", () =>
            {
                if (Drive.Fsm() == "MainMenu") return true;
                if (Elapsed(1.5f) && Game.Fsm != null) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Tout("boot", BootTimeout)) return true;
                return false;
            });

            // ---------------------------------------------------------------- stage
            Add("enter-stage", () =>
            {
                if (Drive.Fsm() == "Stage" && Game.UI != null && Game.UI.IsOpen<HudPanel>()) return true;
                if (Elapsed(0.5f) && _saveName.Length == 0)
                {
                    var save = Drive.Save();
                    var list = save != null ? save.List() : null;
                    if (list != null && list.Count > 0) _saveName = list[0];
                    else { _saveName = "X162012"; Drive.Warn("no save in the roster; trying \"" + _saveName + "\""); }
                    Drive.KV("ENTER-STAGE", "save=\"" + _saveName + "\" via=Events.CharSelectRequest");
                    Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
                }
                if (Tout("enter-stage", StageTimeout)) return true;
                return false;
            });

            Add("stage-settle", () =>
            {
                if (!Elapsed(1.5f)) return false;
                Drive.KV("STAGE", Drive.State());
                return true;
            });

            // ---------------------------------------------------------------- open shop
            Add("open-shop", () =>
            {
                if (Game.UI != null && Game.UI.IsOpen<ShopPanel>()) return true;

                if (_shopPath.Length == 0)
                {
                    // real UI entry (see INpcModule.GetShop doc: Events.ShopOpenRequest is the UI door;
                    // App/AppEventRouting resolves the npc from the args and emits Events.ShopOpen)
                    _shopPath = "ShopOpenRequest";
                    _shopTryAt = Time.unscaledTime;
                    Drive.KV("SHOP-OPEN", "emit Events.ShopOpenRequest npcId=" + (int)NpcId.Charsi);
                    Game.Event.Emit<int>(Events.ShopOpenRequest, (int)NpcId.Charsi);
                }
                else if (Time.unscaledTime - _shopTryAt > 4f && _shopPath == "ShopOpenRequest")
                {
                    // fallback: the real dialog chain (Interact -> the shop option)
                    _shopPath = "dialog";
                    _shopTryAt = Time.unscaledTime;
                    var npc = Drive.Npc();
                    if (npc == null) { Drive.Warn("SHOP-OPEN npc module null"); _shopPath = "none"; return true; }
                    var ok = npc.Interact((int)NpcId.Charsi);
                    Drive.KV("SHOP-OPEN-DIALOG", "Interact(Charsi) ok=" + (ok ? 1 : 0) + " " + Drive.State());
                    if (ok)
                    {
                        var args = npc.GetDialog((int)NpcId.Charsi);
                        var opts = args != null ? args.options : null;
                        var idx = -1;
                        if (opts != null)
                        {
                            for (var i = 0; i < opts.Count; i++)
                            {
                                // "\u4ea4\u6613" = the original string-table word for the trade option
                                if (opts[i] != null && opts[i].IndexOf("\u4ea4\u6613", StringComparison.Ordinal) >= 0) { idx = i; break; }
                            }
                            if (idx < 0 && args.hasShop) idx = opts.Count - 1;
                        }
                        Drive.KV("SHOP-OPEN-DIALOG", "options=" + (opts != null ? opts.Count : -1) + " shopIndex=" + idx
                            + " hasShop=" + (args != null && args.hasShop ? 1 : 0));
                        if (idx > 0)
                        {
                            _shopPath = "dialog:ChooseOption(" + idx + ")";
                            npc.ChooseOption((int)NpcId.Charsi, idx);
                        }
                    }
                }
                else if (Time.unscaledTime - _shopTryAt > 4f && _shopPath.StartsWith("dialog", StringComparison.Ordinal))
                {
                    _shopTryAt = Time.unscaledTime;
                    // last resort: open the panel with the module's own snapshot (still product data)
                    var npc = Drive.Npc();
                    var snapshot = npc != null ? npc.GetShop((int)NpcId.Charsi) : null;
                    if (snapshot != null)
                    {
                        _shopPath = "fallback:Game.UI.Open(GetShop)";
                        Drive.Warn("SHOP-OPEN fallback: the dialog chain did not open the panel -> Open<ShopPanel>(GetShop(Charsi))");
                        Game.UI.Open<ShopPanel>(snapshot);
                    }
                    else
                    {
                        Drive.Warn("SHOP-OPEN failed: GetShop(Charsi) is null");
                        _shopPath = "none";
                        return true;
                    }
                }

                if (Tout("open-shop", ShopTimeout)) return true;
                return false;
            });

            Add("shop-settle", () =>
            {
                if (!Elapsed(1.0f)) return false;
                Drive.KV("SHOP-OPENED", "via=" + _shopPath + " " + Drive.State());
                return true;
            });

            // ------------------------------------------------ bottom-bar square buttons (u53-shopart)
            // The two action buttons must carry the ORIGINAL buysellbtn art (repair = frame 2,
            // close = frame 10), not the flat placeholder colour UiArt.ButtonBg. Reading
            // Image.sprite.name at run time is the only way to tell the two apart: the source
            // applies the sprite asynchronously (UiArt.SetSprite -> Game.Res.LoadAsset).
            Add("dump-buttons", () => { DumpShopArt(); return true; });

            // ---------------------------------------------------------------- dump + shot (buy page)
            Add("dump-buy", () => { Dump("buy"); Shoot("shop_cells_buy.png"); return true; });
            Add("shot-buy", () => ShotLanded());

            // ---------------------------------------------------------------- covered-cell click
            Add("pick-covered", () =>
            {
                var shop = FindShop();
                if (shop == null) { Drive.Warn("CLICK-CHECK no ShopPanel"); return true; }
                var sh = Drive.Field(shop, "_shop") as ShopOpenArgs;
                var owner = Drive.IntArrayField(shop, "_owner");
                if (sh == null || sh.stock == null || owner == null) { Drive.Warn("CLICK-CHECK missing data"); return true; }

                for (var i = 0; i < sh.stock.Count; i++)
                {
                    var e = sh.stock[i];
                    if (e == null) continue;
                    var w = e.gridW > 0 ? e.gridW : 1;
                    if (w < 2) continue;                       // need a cell that is covered but not the anchor
                    var anchor = -1;
                    for (var c = 0; c < owner.Length; c++) if (owner[c] == i) { anchor = c; break; }
                    if (anchor < 0) continue;

                    _anchorCell = anchor;
                    _coveredCell = anchor + 1;                 // same row, the next column -> inside the block
                    _ownerAtCovered = owner[_coveredCell];
                    _expectedName = e.name;
                    _expectedPrice = e.price;
                    if (_coveredCell < sh.stock.Count) { _stockAtCoveredIndex = sh.stock[_coveredCell].index; _stockAtCoveredName = sh.stock[_coveredCell].name; }
                    Drive.KV("CLICK-PICK", "item=" + i + " name=\"" + Drive.Esc(e.name) + "\" size=" + w + "x"
                        + (e.gridH > 0 ? e.gridH : 1) + " anchorCell=" + anchor + " anchorColRow=(" + (anchor % 10) + "," + (anchor / 10) + ")"
                        + " coveredCell=" + _coveredCell + " owner(covered)=" + _ownerAtCovered
                        + " stock[coveredCell]=\"" + Drive.Esc(_stockAtCoveredName) + "\""
                        + " note=\"old code bought stock[coveredCell]; the fix must buy the OWNER\"");
                    return true;
                }
                Drive.Warn("CLICK-CHECK found no multi-cell item in the shop stock -> nothing to click");
                return true;
            });

            Add("click-covered", () =>
            {
                if (_coveredCell < 0) return true;
                var shop = FindShop();
                var root = shop != null ? shop.transform : null;
                var node = Drive.Node(root, "Cell" + _coveredCell);
                var p = Drive.Player();
                _goldBefore = p != null ? p.Gold : -1;
                var ok = Drive.InvokeButton(node);
                Drive.KV("CLICK-COVERED", "cell=" + _coveredCell + " owner=" + _ownerAtCovered
                    + " expected=\"" + Drive.Esc(_expectedName) + "\" price=" + _expectedPrice
                    + " goldBefore=" + _goldBefore + " invoked=" + (ok ? 1 : 0)
                    + " nodeRect=" + Drive.RectOf(node));
                return true;
            });

            Add("click-read", () =>
            {
                if (!Elapsed(1.2f)) return false;
                var p = Drive.Player();
                _goldAfter = p != null ? p.Gold : -1;
                var delta = _goldBefore - _goldAfter;
                var ok = delta == _expectedPrice && delta > 0;
                Drive.Log("SHOPGRID-CLICK page=buy cell=" + _coveredCell + " owner=" + _ownerAtCovered
                    + " expected=\"" + Drive.Esc(_expectedName) + "\" expectedPrice=" + _expectedPrice
                    + " goldBefore=" + _goldBefore + " goldAfter=" + _goldAfter + " delta=" + delta
                    + " stockAtCell=\"" + Drive.Esc(_stockAtCoveredName) + "\""
                    + " verdict=" + (ok ? "OWNER-BOUGHT" : (delta == 0 ? "NO-PURCHASE" : "WRONG-ITEM")));
                return true;
            });

            // ---------------------------------------------------------------- sell page
            Add("tab1", () =>
            {
                var shop = FindShop();
                var root = shop != null ? shop.transform : null;
                var ok = Drive.InvokeButton(Drive.Node(root, "Tab1"));
                Drive.KV("TAB1-CLICK", "invoked=" + (ok ? 1 : 0));
                return true;
            });
            Add("dump-sell", () => { if (!Elapsed(0.6f)) return false; Dump("sell"); Shoot("shop_cells_sell.png"); return true; });
            Add("shot-sell", () => ShotLanded());

            Add("finish", () =>
            {
                var p = Drive.Player();
                Drive.Log("SHOPGRID-SUMMARY page=buy items=" + _itemsBuy
                    + " shopPath=" + _shopPath
                    + " coveredCell=" + _coveredCell + " owner=" + _ownerAtCovered
                    + " delta=" + (_goldBefore - _goldAfter)
                    + " gold=" + (p != null ? p.Gold : -1)
                    + " notes=" + string.Join(",", _lines.ToArray()));
                return true;
            });
        }

        private ShopPanel FindShop()
        {
            if (Game.UI == null) return null;
            return Game.UI.IsOpen<ShopPanel>() ? Game.UI.Get<ShopPanel>() : null;
        }

        /// <summary>
        /// The numeric evidence: per item the anchor cell + grid size + the ICON node rect, and the
        /// whole 10x10 owner map. All numbers, no verdict words.
        /// </summary>
        private void Dump(string page)
        {
            var shop = FindShop();
            if (shop == null) { Drive.Log("SHOPGRID-DUMP page=" + page + " open=0"); return; }
            var root = shop.transform;
            var owner = Drive.IntArrayField(shop, "_owner");
            var sh = Drive.Field(shop, "_shop") as ShopOpenArgs;
            if (owner == null) { Drive.Warn("SHOPGRID-DUMP page=" + page + " _owner is null"); return; }

            // NOTE: the two lists are different types -> no conditional expression here.
            var stockList = sh != null ? sh.stock : null;
            var playerList = sh != null ? sh.playerItems : null;
            var n = page == "buy"
                ? (stockList != null ? stockList.Count : 0)
                : (playerList != null ? playerList.Count : 0);

            var occupied = 0;
            for (var c = 0; c < owner.Length; c++) if (owner[c] >= 0) occupied++;

            var sb = new StringBuilder();
            for (var i = 0; i < n; i++)
            {
                int w, h; string name; int price; string extra = "";
                if (page == "buy")
                {
                    var e = sh.stock[i];
                    w = e != null && e.gridW > 0 ? e.gridW : 1;
                    h = e != null && e.gridH > 0 ? e.gridH : 1;
                    name = e != null ? e.name : "(null)";
                    price = e != null ? e.price : -1;
                }
                else
                {
                    var slot = sh.playerItems[i];
                    var it = slot != null ? slot.item : null;
                    w = it != null && it.gridW > 0 ? it.gridW : 1;
                    h = it != null && it.gridH > 0 ? it.gridH : 1;
                    name = it != null ? it.name : "(null)";
                    price = it != null ? it.price : -1;
                    extra = " isAnchor=" + (slot != null && slot.isAnchor ? 1 : 0);
                }

                var anchor = -1;
                for (var c = 0; c < owner.Length; c++) if (owner[c] == i) { anchor = c; break; }

                // the ICON node lives on the anchor cell (that is where the block is laid out)
                var iconNode = anchor >= 0 ? Drive.Node(Drive.Node(root, "Cell" + anchor) != null
                    ? Drive.Node(root, "Cell" + anchor).transform : null, "Icon") : null;
                var cells = 0;
                for (var c = 0; c < owner.Length; c++) if (owner[c] == i) cells++;

                sb.Append('|').Append(i).Append(":\"").Append(Drive.Esc(name)).Append("\" ").Append(w).Append('x').Append(h)
                  .Append(" anchor=").Append(anchor >= 0 ? ("(" + (anchor % 10) + "," + (anchor / 10) + ")") : "(none)")
                  .Append(" cells=").Append(cells)
                  .Append(" price=").Append(price).Append(extra)
                  .Append(" sprite=").Append(Drive.SpriteOf(iconNode))
                  .Append(" icon=").Append(Drive.RectOf(iconNode));
            }

            var boardOk = Cols * Rows == ShopPanel.CellCount;
            if (!boardOk) Drive.Warn("SHOPGRID-DUMP board mismatch cols=" + Cols + " rows=" + Rows
                + " CellCount=" + ShopPanel.CellCount);

            Drive.Log("SHOPGRID-DUMP page=" + page + " items=" + n + " ownerCells=" + occupied + "/" + owner.Length
                + " cellSize=" + ShopPanel.CellSize.ToString("0.0")
                + " cols=" + Cols + " rows=" + Rows + " boardOk=" + (boardOk ? 1 : 0) + sb);

            for (var r = 0; r < Rows; r++)
            {
                var row = new StringBuilder();
                for (var c = 0; c < Cols; c++)
                {
                    if (c > 0) row.Append(' ');
                    row.Append(owner[r * Cols + c]);
                }
                Drive.Log("SHOPGRID-OWNER page=" + page + " row" + r + "=\"" + row + "\"");
            }

            if (page == "buy")
            {
                _itemsBuy = n;
                Drive.Log("SHOPGRID-GEOM page=" + page
                    + " shopCell=" + ShopPanel.CellSize.ToString("0.0")
                    + " panel=" + ShopPanel.PanelSize.x.ToString("0.0") + "x" + ShopPanel.PanelSize.y.ToString("0.0"));
            }
        }

        /// <summary>
        /// node present / sprite name / Image colour / transition / pressed sprite / rect.
        /// The placeholder shape (no sprite + flat UiArt.ButtonBg colour) shows up here as
        /// sprite=none, which is exactly what the offline check (uicheck ShopArtCheck) forbids.
        /// </summary>
        private void DumpShopArt()
        {
            var shop = FindShop();
            if (shop == null) { Drive.Log("SHOPART-BTN open=0"); return; }
            var root = shop.transform;
            DumpButton(root, "RepairAll", 2);
            DumpButton(root, "Close", 3);
        }

        private static string Fmt(Rect r)
            => "(x" + r.xMin.ToString("0.#") + " y" + r.yMin.ToString("0.#") + " "
               + r.width.ToString("0.#") + "x" + r.height.ToString("0.#") + ")";

        /// <summary>Screen-space rect of a RectTransform (ScreenSpaceOverlay canvas => world == screen).</summary>
        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 4; i++)
            {
                var sp = RectTransformUtility.WorldToScreenPoint(null, c[i]);
                min = Vector2.Min(min, sp);
                max = Vector2.Max(max, sp);
            }
            return new Rect(min, max - min);
        }

        /// <summary>
        /// pure glyph -- repair = hammer+anvil, close = circled slash -- so a label on top of it hides
        /// the very art that makes the button self-explanatory). Reads, live: the button rect, the
        /// Label child's rect in BUTTON-LOCAL space (directly comparable to ShopPanel.ButtonRect /
        /// ButtonLabelRect), the screen-space rects, and whether they overlap (must be 0).
        /// </summary>
        private void DumpButton(Transform root, string name, int slot)
        {
            var go = Drive.Node(root, name);
            var img = Drive.ImgOf(go);
            var btn = go != null ? go.GetComponent<Button>() : null;
            var pressed = btn != null && btn.spriteState.pressedSprite != null
                ? btn.spriteState.pressedSprite.name : "none";
            var color = img != null
                ? (img.color.r.ToString("0.###") + "," + img.color.g.ToString("0.###") + ","
                   + img.color.b.ToString("0.###") + "," + img.color.a.ToString("0.###"))
                : "(no-img)";

            var label = go != null ? go.transform.Find("Label") as RectTransform : null;
            var lr = label != null ? label.rect : new Rect();
            var lLocal = label != null
                ? new Rect(label.anchoredPosition - lr.size * 0.5f, lr.size) : new Rect();
            var expect = ShopPanel.ButtonLabelRect(slot);
            var buttonLocal = ShopPanel.ButtonRect(slot);
            var overlapLocal = label != null && lLocal.Overlaps(buttonLocal);
            var labelText = label != null ? label.GetComponent<Text>() : null;

            // R2-a: the button must sit CENTRED in the carved slot's inner recess
            // (the old constant put it on the slot's TOP EDGE -> 18 original px too high).
            if (img != null)
            {
                var c = img.rectTransform.anchoredPosition;      // panel-local (parent = panel root)
                var want = ShopPanel.SlotCenter(slot);
                var inner = ShopPanel.SlotInnerRect(slot);
                var sd = img.rectTransform.sizeDelta;
                var br = new Rect(c.x - sd.x * 0.5f, c.y - sd.y * 0.5f, sd.x, sd.y);
                var inside = br.xMin >= inner.xMin - 0.01f && br.xMax <= inner.xMax + 0.01f
                          && br.yMin >= inner.yMin - 0.01f && br.yMax <= inner.yMax + 0.01f;
                Drive.Log("SHOPART-SLOT slot=" + slot
                    + " actualCenter=(" + c.x.ToString("0.0") + "," + c.y.ToString("0.0") + ")"
                    + " expectCenter=(" + want.x.ToString("0.0") + "," + want.y.ToString("0.0") + ")"
                    + " size=" + sd.x.ToString("0.0") + "x" + sd.y.ToString("0.0")
                    + " distCanvasPx=" + (c - want).magnitude.ToString("0.###")
                    + " distOrigPx=" + ((c - want).magnitude / 1.8f).ToString("0.###")
                    + " inner=" + Fmt(inner)
                    + " buttonInPanel=" + Fmt(br)
                    + " insideInner=" + (inside ? 1 : 0));
            }

            var overlapScreen = false;
            var screen = "labelScreen=(none) buttonScreen=(none)";
            if (label != null && img != null)
            {
                var ls = ScreenRect(label);
                var bs = ScreenRect(img.rectTransform);
                overlapScreen = ls.Overlaps(bs);
                screen = "labelScreen=" + Fmt(ls) + " buttonScreen=" + Fmt(bs);
            }

            Drive.Log("SHOPART-BTN name=" + name + " slot=" + slot
                + " node=" + (go != null ? 1 : 0)
                + " sprite=" + Drive.SpriteOf(go)
                + " color=" + color
                + " transition=" + (btn != null ? btn.transition.ToString() : "(no-btn)")
                + " pressed=" + pressed
                + " rect=" + Drive.RectOf(go)
                + " labelChild=" + (label != null ? 1 : 0)
                + " labelRectLocal=" + Fmt(lLocal)
                + " expectedLabelLocal=" + Fmt(expect)
                + " labelFontSize=" + (labelText != null ? labelText.fontSize.ToString() : "(-)")
                + " overlapLocal=" + (overlapLocal ? 1 : 0)
                + " " + screen
                + " overlapScreen=" + (overlapScreen ? 1 : 0));
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Drive.Log("SHOPGRID-FINISH why=" + why + " " + Drive.State());
            Drive.WriteFile(Drive.DonePath, "DONE why=" + why + " items=" + _itemsBuy
                + " shopPath=" + _shopPath + " coveredCell=" + _coveredCell
                + " delta=" + (_goldBefore - _goldAfter) + " t=" + DateTime.Now.ToString("HH:mm:ss"));
        }
    }
}
