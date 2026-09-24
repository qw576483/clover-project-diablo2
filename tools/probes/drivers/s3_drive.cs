// =============================================================================
// s3_drive.cs -- ONE Play session that produces the S3 verdict for four
//     U26/U36  character vs NPC overlap flicker   -> same-cell multi-frame sort sequence
//     U33      mouse near the character has no effect -> hover hit record near/far
//     U40      red goblin: no hit animation        -> monster GetHit frame-number sequence
//     U40b     monster movement "a blob"           -> monster per-frame grid/world trace
//   The per-monster HIT SOUND asset question is answered OFFLINE (d2sfx.mpq really
//   contains monster\fallen\gethit1..7.wav -- see the S3 report), so this Play
//   session only had to cover what does not exist outside a live Play.
//
// WHY THIS FILE EXISTS (skill 3.5): the judged quantities are runtime-only (render
//   nodes, the sprite frame actually on screen, the mouse->grid projection), so this
//   is a judging asset and lives in tools/probes/.
//
// CHAIN: Boot(Space) -> MainMenu("Single") -> CharSelect -> Events.CharSelectRequest(save)
//        -> Loading -> Stage, then four measurement phases; every phase appends rows to
//        <out>/s3_evidence.tsv (written by the runtime, not by the author's narration).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

namespace S3D
{
    /// <summary>Reflection / IO helpers.</summary>
    public static class H
    {
        public const string Tag = "S3";
        public const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic
                                     | BindingFlags.Instance | BindingFlags.Static;

        public static string OutDir = string.Empty;
        public static string DonePath = string.Empty;
        private static readonly StringBuilder Buf = new StringBuilder();

        public static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else Debug.Log("[" + Tag + "] " + m);
        }
        public static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else Debug.LogWarning("[" + Tag + "] " + m);
        }
        public static void KV(string k, string v) { Log(k + "=" + v); }

        public static void Row(string kind, string body)
        {
            Buf.Append(kind).Append('\t').Append(body).Append('\n');
        }
        public static void RowFlush()
        {
            if (Buf.Length == 0 || string.IsNullOrEmpty(OutDir)) return;
            try
            {
                Directory.CreateDirectory(OutDir);
                File.AppendAllText(Path.Combine(OutDir, "s3_evidence.tsv"), Buf.ToString());
                Buf.Length = 0;
            }
            catch (Exception e) { Warn("ROW-FLUSH-FAIL " + e.GetType().Name + ": " + e.Message); }
        }
        public static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(path, content);
            }
            catch (Exception e) { Warn("MARKER-FAIL " + e.GetType().Name + ": " + e.Message); }
        }
        public static void Paths(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0) OutDir = p[0];
            if (p.Length > 1) DonePath = p[1];
            Log("PATHS outDir=" + OutDir + " done=" + DonePath);
        }

        // ---- reflection --------------------------------------------------------------------
        public static Type T(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(name); if (t != null) return t; }
            return null;
        }
        public static object Ctx()
        {
            var t = T("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BF);
            if (p != null) return p.GetValue(null);
            var f = t.GetField("I", BF);
            return f != null ? f.GetValue(null) : null;
        }
        public static object CtxMember(string n)
        {
            var c = Ctx();
            if (c == null) return null;
            var f = c.GetType().GetField(n, BF);
            if (f != null) return f.GetValue(c);
            var p = c.GetType().GetProperty(n, BF);
            return p != null ? p.GetValue(c) : null;
        }
        public static object Field(object o, string n)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(n, BF);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(n, BF);
            return p != null ? p.GetValue(o) : null;
        }
        public static object Invoke(object o, string name, params object[] args)
        {
            if (o == null) return null;
            var types = new Type[args.Length];
            for (var i = 0; i < args.Length; i++) types[i] = args[i] != null ? args[i].GetType() : typeof(object);
            var m = o.GetType().GetMethod(name, BF, null, types, null);
            if (m == null)
            {
                foreach (var mm in o.GetType().GetMethods(BF))
                {
                    if (mm.Name != name) continue;
                    var ps = mm.GetParameters();
                    if (ps.Length != args.Length) continue;
                    var ok = true;
                    for (var i = 0; i < ps.Length; i++)
                    {
                        if (args[i] == null) continue;
                        if (!ps[i].ParameterType.IsInstanceOfType(args[i])) { ok = false; break; }
                    }
                    if (ok) { m = mm; break; }
                }
            }
            return m != null ? m.Invoke(o, args) : null;
        }
        public static string S(object o) { return o == null ? "(null)" : o.ToString(); }
        public static string Fmt(float f) { return f.ToString("0.####", CultureInfo.InvariantCulture); }

        // ---- module shorthand --------------------------------------------------------------
        public static object Player() { return CtxMember("Player"); }
        public static object Map() { return CtxMember("Map"); }
        public static object Monster() { return CtxMember("Monster"); }
        public static object Npc() { return CtxMember("Npc"); }

        public static Vector2Int GridOf(object player)
        {
            var g = Field(player, "Grid");
            return g is Vector2Int ? (Vector2Int)g : Vector2Int.zero;
        }
        public static List<object> AsList(object enumerable)
        {
            var outList = new List<object>();
            var e = enumerable as IEnumerable;
            if (e == null) return outList;
            foreach (var x in e) outList.Add(x);
            return outList;
        }

        // ---- scene nodes -------------------------------------------------------------------
        public static SpriteRenderer NodeOfEntity(int entityId)
        {
            var prefix = "E_" + entityId + "_";
            var all = UnityEngine.Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            for (var i = 0; i < all.Length; i++)
            {
                var sr = all[i];
                if (sr == null || sr.gameObject == null) continue;
                if (sr.gameObject.name.StartsWith(prefix, StringComparison.Ordinal)) return sr;
            }
            return null;
        }

        // ---- devices / input ---------------------------------------------------------------
        public static Mouse MouseDev()
        {
            var m = Mouse.current;
            if (m == null) { m = InputSystem.AddDevice<Mouse>(); Warn("mouse device missing -> added"); }
            return m;
        }
        public static Keyboard KbDev()
        {
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); Warn("keyboard device missing -> added"); }
            return kb;
        }
        public static void KeyDown(Key k)
        {
            var kb = KbDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState(k));
            Log("KEYDOWN key=" + k);
        }
        public static void KeyUp()
        {
            var kb = KbDev();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
        }
        public static void MouseMove(Vector2 pos)
        {
            var m = MouseDev();
            if (m == null) return;
            InputSystem.QueueStateEvent(m, new MouseState { position = pos });
        }
        public static void ClickButton(string goName)
        {
            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            Button pick = null;
            for (var i = 0; i < all.Length; i++)
            {
                var b = all[i];
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, goName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                pick = b; break;
            }
            if (pick == null) { Warn("CLICK miss name=" + goName); return; }
            if (EventSystem.current == null) { Warn("CLICK no-eventsystem"); return; }
            var ped = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            ExecuteEvents.Execute(pick.gameObject, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK name=" + goName);
        }
        public static string FirstSaveName()
        {
            try
            {
                var flow = CtxMember("Flow");
                if (flow == null) { Warn("ROSTER-PROBE AppContext.Flow == null"); return null; }
                var rf = flow.GetType().GetField("_roster", BF);
                var roster = rf != null ? rf.GetValue(flow) : null;
                if (roster == null) { Warn("ROSTER-PROBE no AppFlow._roster"); return null; }
                var m = roster.GetType().GetMethod("ListAll", BF);
                if (m == null) { Warn("ROSTER-PROBE no CharRoster.ListAll"); return null; }
                var items = m.Invoke(roster, null) as IEnumerable;
                string pick = null; var n = 0;
                if (items != null)
                {
                    foreach (var it in items)
                    {
                        n++;
                        if (it == null) continue;
                        var nf = it.GetType().GetField("name", BindingFlags.Public | BindingFlags.Instance);
                        var nm = nf != null ? nf.GetValue(it) as string : null;
                        if (pick == null && !string.IsNullOrEmpty(nm)) pick = nm;
                    }
                }
                Log("ROSTER-PROBE count=" + n + " pick=" + (pick ?? "(none)"));
                return pick;
            }
            catch (Exception e) { Warn("ROSTER-PROBE " + e.GetType().Name + ": " + e.Message); return null; }
        }
        public static void SnapCam()
        {
            try
            {
                var rig = CtxMember("Camera");
                if (rig == null) { Warn("SnapCam: no ICameraRig"); return; }
                Invoke(rig, "SnapToTarget");
            }
            catch (Exception e) { Warn("SnapCam " + e.GetType().Name); }
        }
        public static bool SoftwareRaster(string dev)
        {
            if (string.IsNullOrEmpty(dev)) return true;
            return dev.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0
                || dev.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                || dev.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount + " gameRunning=" + (Game.IsRunning ? 1 : 0); }
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var kb = Keyboard.current; if (kb == null) InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " screen=" + Screen.width + "x" + Screen.height
                       + " device=\"" + SystemInfo.graphicsDeviceName + "\""
                       + " type=" + SystemInfo.graphicsDeviceType
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)");
            H.Log(line);
            return line;
        }
        public static string Paths(string spec) { H.Paths(spec); return "PATHS-OK"; }
    }

    /// <summary>Installer. spec = "&lt;out dir&gt;|&lt;done marker&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("S3EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            H.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    public class Driver : MonoBehaviour
    {
        private const int SortFrames = 24;
        private const int HitFrames = 200;
        private const int MoveFrames = 120;

        private string _outDir = string.Empty;
        private string _done = string.Empty;
        private int _step;
        private float _at;
        private bool _doneFlag;
        private bool _bootSent, _bootUp;
        private float _bootAt;
        private string _save = string.Empty;
        private float _trigAt;
        private string _lastPanel = "(null)";
        private bool _sawLoading;

        private int _frameIn;
        private Vector2Int _npcGrid;
        private int _npcId = int.MinValue;
        private readonly List<string> _sortRows = new List<string>();
        private readonly List<string> _hitRows = new List<string>();
        private readonly List<string> _moveRows = new List<string>();
        private int _monsterId = -1;
        private Vector2Int _monsterGrid;
        private int _sortShotTaken, _hoverShotTaken;
        private string _pendingShot = string.Empty;
        private bool _travelIssued;
        private float _travelAt, _travelRetry;

        /// <summary>`SpriteFrames.Placeholder` 的 sprite 名（实机 MHIT 行实测值）—— 等它被真图替换再开窗。</summary>
        private const string PlaceholderSpriteName = "D2CharPlaceholder";

        /// <summary>等真贴图的硬上限（秒）；超时也开窗，但打 Warn 说明"矩形可能是占位图的"。</summary>
        private const float SpriteWaitSeconds = 25f;

        private float _spriteWaitAt;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            _outDir = parts.Length > 0 ? parts[0] : string.Empty;
            _done = parts.Length > 1 ? parts[1] : string.Empty;
            H.Paths(_outDir + "|" + _done);
            _step = 0; _at = Time.unscaledTime; _trigAt = _at;
            H.Log("DRIVER-INIT frame=" + Time.frameCount + " out=" + _outDir);
        }

        private void Update()
        {
            if (_doneFlag) return;
            try { Step(); }
            catch (Exception ex) { H.Warn("STEP-FATAL step=" + _step + " " + ex.GetType().Name + ": " + ex.Message); Next(); }
        }

        private void LateUpdate()
        {
            try
            {
                if (_step == 8) RecordSort();
                else if (_step == 16) RecordHit();
                else if (_step == 17) RecordMove();
                if (_pendingShot.Length > 0) { ScreenCapture.CaptureScreenshot(_pendingShot); _pendingShot = string.Empty; }
                H.RowFlush();
            }
            catch (Exception ex) { H.Warn("LATE-FATAL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnApplicationQuit() { Finish("appquit"); }

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; _frameIn = 0; }
        private void Goto(int step) { _step = step; _at = Time.unscaledTime; _frameIn = 0; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool LoadingOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }
        private string Panel()
        {
            if (BootOpen()) return "Boot";
            if (MenuOpen()) return "MainMenu";
            if (SelectOpen()) return "CharSelect";
            if (HudOpen()) return "Hud";
            return "(none)";
        }
        private bool Tout(string at, float s)
        {
            if (!Elapsed(s)) return false;
            H.KV("STEP-TIMEOUT", "at=" + at + " elapsed=" + (Time.unscaledTime - _at).ToString("0.0")
                + " fsm=" + Fsm() + " panel=" + Panel());
            Finish("timeout-" + at);
            return true;
        }
        private void Reached(string p)
        {
            H.KV("FLOW", _lastPanel + "->" + p + " ok after " + (Time.unscaledTime - _trigAt).ToString("0.00") + "s");
            _lastPanel = p; Next();
        }
        private void Shot(string name)
        {
            _pendingShot = Path.Combine(_outDir, name);
        }

        // ================================================================= chain ============
        private void Step()
        {
            switch (_step)
            {
                case 0:
                    {
                        var dev = SystemInfo.graphicsDeviceName;
                        H.KV("ENV", "device=\"" + dev + "\" type=" + SystemInfo.graphicsDeviceType
                            + " res=" + Screen.width + "x" + Screen.height + " unity=" + Application.unityVersion);
                        if (H.SoftwareRaster(dev)) { H.Warn("DEVICE-SOFTWARE-RASTER => verdict INVALID"); Finish("software-raster"); return; }
                        Next(); return;
                    }
                case 1:
                    if (!_bootSent)
                    {
                        if (!BootOpen()) { Tout("boot-wait", 30f); return; }
                        if (!Elapsed(1.2f)) return;
                        H.KeyDown(Key.Space); _bootSent = true; _bootAt = Time.unscaledTime; _trigAt = _bootAt; return;
                    }
                    if (!_bootUp && Time.unscaledTime - _bootAt >= 0.25f) { H.KeyUp(); _bootUp = true; }
                    if (!BootOpen()) { Next(); return; }
                    return;
                case 2:
                    if (!MenuOpen()) { Tout("mainmenu-wait", 25f); return; }
                    Reached("MainMenu"); return;
                case 3:
                    if (!Elapsed(1.0f)) return;
                    H.ClickButton("Single"); Next(); return;
                case 4:
                    if (!SelectOpen()) { Tout("charselect-wait", 25f); return; }
                    Reached("CharSelect"); return;
                case 5:
                    if (!Elapsed(0.6f)) return;
                    var pick = H.FirstSaveName();
                    if (!string.IsNullOrEmpty(pick)) _save = pick;
                    H.KV("ENTER-STAGE", "save=\"" + _save + "\"");
                    _trigAt = Time.unscaledTime;
                    Game.Event.Emit<string>(Diablo2.Core.Events.CharSelectRequest, _save);
                    Next(); return;
                case 6:
                    if (!_sawLoading && LoadingOpen()) _sawLoading = true;
                    if (!(HudOpen() && Fsm() == "Stage")) { Tout("stage-wait", 45f); return; }
                    if (_lastPanel != "Stage") { H.KV("FLOW", "->Stage"); _lastPanel = "Stage"; _at = Time.unscaledTime; return; }
                    if (!Elapsed(1.5f)) return;
                    H.KV("WORLD", "area=" + H.S(H.Field(H.Map(), "Area"))
                        + " map=" + H.S(H.Field(H.Map(), "Width")) + "x" + H.S(H.Field(H.Map(), "Height"))
                        + " playerGrid=" + H.GridOf(H.Player())
                        + " monsters=" + H.AsList(H.Field(H.Monster(), "All")).Count
                        + " npcs=" + H.AsList(H.Field(H.Npc(), "All")).Count);
                    Next(); return;

                // ---- 7/8 same-cell sort ----------------------------------------------------
                case 7:
                    {
                        var npcs = H.AsList(H.Field(H.Npc(), "All"));
                        if (npcs.Count == 0) { H.Warn("SORT: no npc in area => phase skipped"); Next(); return; }
                        var npc = npcs[0];
                        _npcId = (int)H.Field(npc, "id");
                        _npcGrid = new Vector2Int((int)H.Field(npc, "gridX"), (int)H.Field(npc, "gridY"));
                        H.KV("SORT-SETUP", "npcId=" + _npcId + " npcGrid=" + _npcGrid
                            + " playerGridBefore=" + H.GridOf(H.Player()));
                        H.Invoke(H.Player(), "TeleportTo", _npcGrid);
                        H.SnapCam();
                        _sortRows.Clear(); _frameIn = 0;
                        Next(); return;
                    }
                case 8:
                    if (_frameIn >= SortFrames) { SummarizeSort(); Next(); return; }
                    return;

                // ---- 9/11 hover probes (round 0 = town, near the character/NPC) -------------
                case 9:
                    if (!Elapsed(0.2f)) return;
                    BuildHoverProbe(); Goto(11); return;
                case 11:
                    {
                        if (_hoverIdx >= _hoverProbes.Count) { Goto(_hoverRound == 0 ? 12 : 15); return; }
                        if (!_probeIssued)
                        {
                            if (_hoverWait > 0) { _hoverWait--; return; }
                            var pt = PointFor(_hoverProbes[_hoverIdx]);   // resolved against the CURRENT camera
                            _hoverProbes[_hoverIdx].screen = pt;          // record what was actually injected
                            H.MouseMove(pt);
                            _probeIssued = true;
                            _hoverWait = 3;
                            return;
                        }
                        if (_hoverWait > 0) { _hoverWait--; return; }
                        ReadHoverProbe(_hoverIdx);
                        _probeIssued = false;
                        _hoverIdx++;
                        _hoverWait = 0;
                        return;
                    }

                // ---- 12 travel to an area that actually has monsters ------------------------
                case 12:
                    {
                        var areaNow = H.S(H.Field(H.Map(), "Area"));
                        if (areaNow != "Town")
                        {
                            H.KV("TRAVEL", "arrived area=" + areaNow + " grid=" + H.GridOf(H.Player())
                                + " in " + (Time.unscaledTime - _travelAt).ToString("0.0") + "s");
                            Goto(13); return;
                        }
                        // the deadline only applies AFTER the walk was actually issued: `_travelAt` is 0
                        // until then, and `Time.unscaledTime` is already > 40s by the time this step runs in
                        // a slow boot (measured 17:01: the old form timed out on the very first frame and the
                        // whole monster half of the chain was skipped).
                        if (_travelIssued && Time.unscaledTime - _travelAt > 40f)
                        {
                            H.Warn("TRAVEL: still in Town after 40s => monster phases are skipped");
                            Goto(15); return;
                        }
                        if (!_travelIssued || Time.unscaledTime - _travelRetry > 4f)
                        {
                            _travelRetry = Time.unscaledTime;
                            var exits = H.AsList(H.Field(H.Map(), "Exits"));
                            if (exits.Count == 0) { H.Warn("TRAVEL: no exits"); Goto(15); return; }
                            var pg = H.GridOf(H.Player());
                            var best = (Vector2Int)exits[0]; var bestD = int.MaxValue;
                            for (var i = 0; i < exits.Count; i++)
                            {
                                var g = (Vector2Int)exits[i];
                                var d = Mathf.Abs(g.x - pg.x) + Mathf.Abs(g.y - pg.y);
                                if (d < bestD) { bestD = d; best = g; }
                            }
                            if (!_travelIssued)
                            {
                                _travelIssued = true; _travelAt = Time.unscaledTime;
                                H.KV("TRAVEL-BEGIN", "from=" + pg + " exit=" + best + " exits=" + exits.Count
                                    + " area=" + areaNow);
                            }
                            H.Invoke(H.Player(), "MoveTo", best);
                        }
                        return;
                    }

                // ---- 13 build the monster-body hover probes (round 1) ----------------------
                case 13:
                    {
                        H.KV("HIT-PHASE-AREA", "area=" + H.S(H.Field(H.Map(), "Area"))
                            + " monsters=" + H.AsList(H.Field(H.Monster(), "All")).Count);

                        // U33 re-verify gate: the fix measures the monster's SpriteRenderer.bounds, and
                        // ViewModule.CreateEntityNode parks SpriteFrames.Placeholder until the async
                        // load finishes (ViewModule.cs:1300-1308) -> opening the window while the
                        // placeholder is up would measure the WRONG rect. Wait for a real sprite.
                        // The placeholder's observed sprite name is D2CharPlaceholder (see MHIT rows).
                        var monProbe = NearestAliveMonster();
                        if (monProbe == null) { H.Warn("MONHOVER: no alive monster => skipped"); Goto(15); return; }
                        var mid = (int)H.Field(monProbe, "id");
                        var msr = H.NodeOfEntity(mid);
                        var spName = (msr != null && msr.sprite != null) ? msr.sprite.name : "(none)";
                        if (msr == null || msr.sprite == null || spName == PlaceholderSpriteName)
                        {
                            if (_spriteWaitAt <= 0f) _spriteWaitAt = Time.unscaledTime;
                            var waited = Time.unscaledTime - _spriteWaitAt;
                            if (waited < SpriteWaitSeconds) return;   // keep waiting; window stays shut
                            H.Warn("MONHOVER-SPRITE-TIMEOUT sprite=\"" + spName + "\" after "
                                + waited.ToString("0.0") + "s => opening anyway (rect may be the placeholder)");
                        }
                        else if (_spriteWaitAt > 0f)
                        {
                            H.KV("MONHOVER-SPRITE-READY", "sprite=" + spName + " waited="
                                + (Time.unscaledTime - _spriteWaitAt).ToString("0.00") + "s");
                        }

                        BuildMonsterHoverProbe();
                        if (_hoverProbes.Count == 0) { Goto(15); return; }
                        _hoverRound = 1;
                        Goto(11);
                        return;
                    }

                // ---- 15 monster melee setup ------------------------------------------------
                case 15:
                    {
                        var mon = NearestAliveMonster();
                        if (mon == null) { H.Warn("HIT: no alive monster => phases skipped"); Goto(18); return; }
                        _monsterId = (int)H.Field(mon, "id");
                        _monsterGrid = new Vector2Int((int)H.Field(mon, "gridX"), (int)H.Field(mon, "gridY"));
                        var side = _monsterGrid;
                        var dirs = new[]
                        {
                            new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1),
                            new Vector2Int(0, 1), new Vector2Int(-1, -1), new Vector2Int(1, 1)
                        };
                        for (var d = 0; d < dirs.Length; d++)
                        {
                            var cand = _monsterGrid + dirs[d];
                            var wk = H.Map() != null ? H.Invoke(H.Map(), "Walkable", cand) : null;
                            if (wk is bool && (bool)wk) { side = cand; break; }
                        }
                        H.Invoke(H.Player(), "TeleportTo", side);
                        H.SnapCam();
                        var pgAfter = H.GridOf(H.Player());
                        H.KV("MON-SETUP", "monsterId=" + _monsterId + " name=\"" + H.S(H.Field(mon, "name")) + "\""
                            + " monsterGrid=" + _monsterGrid + " playerStandWanted=" + side
                            + " playerGridAfterTeleport=" + pgAfter
                            + " chebyshev=" + Mathf.Max(Mathf.Abs(pgAfter.x - _monsterGrid.x), Mathf.Abs(pgAfter.y - _monsterGrid.y))
                            + " hp=" + H.S(H.Field(mon, "hp")) + "/" + H.S(H.Field(mon, "maxHp")));
                        _hitRows.Clear(); _frameIn = 0;
                        Shot("s3_hit_before.png");
                        Goto(16); return;
                    }
                case 16:
                    if (_frameIn >= HitFrames) { SummarizeHit(); Goto(17); return; }
                    return;
                case 17:
                    if (_frameIn >= MoveFrames) { SummarizeMove(); Goto(18); return; }
                    return;

                default:
                    Finish("end");
                    return;
            }
        }

        // ================================================================= sort ==============
        private void RecordSort()
        {
            _frameIn++;
            var pg = H.GridOf(H.Player());
            var co = Camera.main;
            var psr = H.NodeOfEntity(1);
            var nsr = H.NodeOfEntity(-1 - _npcId);
            if (psr == null || nsr == null)
            {
                _sortRows.Add("missing psr=" + (psr != null ? 1 : 0) + " nsr=" + (nsr != null ? 1 : 0));
                return;
            }
            var pp = co != null ? (Vector2)co.WorldToScreenPoint(psr.transform.position) : Vector2.zero;
            var np = co != null ? (Vector2)co.WorldToScreenPoint(nsr.transform.position) : Vector2.zero;
            _sortRows.Add("frame=" + Time.frameCount
                + " playerGrid=" + pg
                + " playerSO=" + psr.sortingOrder
                + " playerZ=" + H.Fmt(psr.transform.position.z)
                + " playerScreen=" + pp.x.ToString("0") + "," + pp.y.ToString("0")
                + " npcSO=" + nsr.sortingOrder
                + " npcZ=" + H.Fmt(nsr.transform.position.z)
                + " npcScreen=" + np.x.ToString("0") + "," + np.y.ToString("0")
                + " sameCell=" + (pg == _npcGrid ? 1 : 0));
            if (_sortShotTaken == 0 && _frameIn == 6) { _sortShotTaken = 1; Shot("s3_samecell.png"); }
        }

        private void SummarizeSort()
        {
            var sameSo = 0; var pOnTop = 0; var nOnTop = 0; var ties = 0; var bad = 0;
            foreach (var r in _sortRows)
            {
                if (r.StartsWith("missing")) { bad++; continue; }
                var pso = ParseInt(r, "playerSO=");
                var nso = ParseInt(r, "npcSO=");
                var pz = ParseFloat(r, "playerZ=");
                var nz = ParseFloat(r, "npcZ=");
                if (pso == nso) sameSo++;
                if (pso > nso || (pso == nso && pz < nz)) pOnTop++;
                else if (nso > pso || (nso == pso && nz < pz)) nOnTop++;
                else ties++;
            }
            var total = _sortRows.Count - bad;
            var orderConst = (total > 0) && (pOnTop == total || nOnTop == total);
            foreach (var r in _sortRows) H.Row("SORT", r);
            H.KV("SORT-VERDICT", "frames=" + _sortRows.Count + " usable=" + total
                + " sameSortingOrderFrames=" + sameSo
                + " playerOnTop=" + pOnTop + " npcOnTop=" + nOnTop + " exactTie=" + ties
                + " ORDER_CONSTANT=" + (orderConst ? 1 : 0));
            H.Row("SORTV", "frames=" + _sortRows.Count + " usable=" + total + " sameSO=" + sameSo
                + " pTop=" + pOnTop + " nTop=" + nOnTop + " tie=" + ties + " orderConstant=" + (orderConst ? 1 : 0));

            var t = H.T("Diablo2.Module.View.ViewModule");
            if (t == null) { H.Warn("SORT-TIEBREAK type Diablo2.Module.View.ViewModule not found"); return; }
            var mr = t.GetMethod("SortTieRank", H.BF);
            var mz = t.GetMethod("SortTieZ", H.BF);
            if (mr == null || mz == null) { H.Warn("SORT-TIEBREAK method missing"); return; }
            var ids = new[] { 1, -1, -2, -3, 1000, 1001, 1002, 1007, 100000, 100003 };
            var baseRank = new int[ids.Length]; var baseZ = new float[ids.Length];
            for (var k = 0; k < ids.Length; k++)
            {
                baseRank[k] = (int)mr.Invoke(null, new object[] { ids[k] });
                baseZ[k] = (float)mz.Invoke(null, new object[] { ids[k] });
            }
            var rankPure = true; var zPure = true;
            for (var it = 0; it < 100; it++)
                for (var k = 0; k < ids.Length; k++)
                {
                    if ((int)mr.Invoke(null, new object[] { ids[k] }) != baseRank[k]) rankPure = false;
                    if (Math.Abs((float)mz.Invoke(null, new object[] { ids[k] }) - baseZ[k]) > 1e-9) zPure = false;
                }
            H.KV("SORT-TIEBREAK", "ids=" + ids.Length + " iter=100 rankPure=" + (rankPure ? 1 : 0)
                + " zPure=" + (zPure ? 1 : 0)
                + " playerZ=" + H.Fmt(baseZ[0]) + " npcZ=" + H.Fmt(baseZ[1])
                + " monsterZ=" + H.Fmt(baseZ[4]) + " itemZ=" + H.Fmt(baseZ[8]));
            H.Row("SORTV", "tiebreak rankPure=" + (rankPure ? 1 : 0) + " zPure=" + (zPure ? 1 : 0)
                + " playerZ=" + H.Fmt(baseZ[0]) + " npcZ=" + H.Fmt(baseZ[1]));
        }

        // ================================================================= hover =============
        /// <summary>
        /// One hover probe. The SCREEN point is NOT stored as a frozen value any more (measured 17:05:
        /// the camera is still catching up right after a teleport, so a point computed at build time can be
        /// ~2300 px off) -- instead each probe carries an ANCHOR that is resolved against the CURRENT camera
        /// at the moment the mouse is injected:
        ///   "abs"      = fixed screen point (screen)
        ///   "cellOff"  = screen point of `cell` + offsetPx        (round 0)
        ///   "rectFrac" = fraction (fx,fy) inside the monster's CURRENT sprite screen rect (round 1)
        ///   "aboveTop" = (centre x, rect top + off)               (round 1)
        ///   "rightOf"  = (rect right + off, centre y)              (round 1)
        /// </summary>
        private class HoverProbe
        {
            public string name;
            public string note;
            public string anchor = "abs";
            public Vector2 screen;
            public Vector2Int cell;
            public Vector2 offsetPx;
            public float fx, fy, off;
        }
        private readonly List<HoverProbe> _hoverProbes = new List<HoverProbe>();
        private int _hoverIdx; private int _hoverWait; private int _hoverRound;
        private bool _probeIssued;

        /// <summary>Resolve the probe's screen point against the CURRENT camera (never a stale projection).</summary>
        private Vector2 PointFor(HoverProbe p)
        {
            var co = Camera.main;
            if (co == null) return p.screen;

            if (p.anchor == "cellOff")
                return (Vector2)co.WorldToScreenPoint(Iso.GridToWorld(p.cell)) + p.offsetPx;

            if (p.anchor == "rectFrac" || p.anchor == "aboveTop" || p.anchor == "rightOf")
            {
                var sr = H.NodeOfEntity(_monsterId);
                if (sr == null || sr.sprite == null) return p.screen;
                var b = sr.bounds;
                var z = sr.transform.position.z;
                var minS = (Vector2)co.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, z));
                var maxS = (Vector2)co.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, z));
                var x0 = Mathf.Min(minS.x, maxS.x); var x1 = Mathf.Max(minS.x, maxS.x);
                var y0 = Mathf.Min(minS.y, maxS.y); var y1 = Mathf.Max(minS.y, maxS.y);
                if (p.anchor == "rectFrac") return new Vector2(Mathf.Lerp(x0, x1, p.fx), Mathf.Lerp(y0, y1, p.fy));
                if (p.anchor == "aboveTop") return new Vector2((x0 + x1) * 0.5f, y1 + p.off);
                return new Vector2(x1 + p.off, (y0 + y1) * 0.5f);
            }

            return p.screen;
        }

        private void BuildHoverProbe()
        {
            _hoverProbes.Clear();
            _hoverIdx = 0; _hoverWait = 0;
            var co = Camera.main;
            var pg = H.GridOf(H.Player());
            if (co == null) { H.Warn("HOVER: no Camera.main"); return; }
            Func<Vector2Int, Vector2, HoverProbe> atCell = (c, off) => new HoverProbe
            { anchor = "cellOff", cell = c, offsetPx = off, note = "cell " + c };
            _hoverProbes.Add(atCell(pg, Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "player_foot";
            _hoverProbes.Add(atCell(pg, new Vector2(0, 50))); _hoverProbes[_hoverProbes.Count - 1].name = "player_body_plus50";
            _hoverProbes[_hoverProbes.Count - 1].note = "character sprite body";
            _hoverProbes.Add(atCell(pg, new Vector2(0, 100))); _hoverProbes[_hoverProbes.Count - 1].name = "player_body_plus100";
            _hoverProbes[_hoverProbes.Count - 1].note = "character sprite head";
            _hoverProbes.Add(atCell(pg + new Vector2Int(1, 0), Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "grid_plus1_east";
            _hoverProbes[_hoverProbes.Count - 1].note = "1 cell east";
            _hoverProbes.Add(atCell(pg + new Vector2Int(2, 0), Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "grid_plus2_east";
            _hoverProbes[_hoverProbes.Count - 1].note = "2 cells east";
            _hoverProbes.Add(atCell(pg + new Vector2Int(-1, 0), Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "grid_minus1_west";
            _hoverProbes[_hoverProbes.Count - 1].note = "1 cell west";
            _hoverProbes.Add(atCell(pg + new Vector2Int(0, 1), Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "grid_plus1_north";
            _hoverProbes[_hoverProbes.Count - 1].note = "1 cell north";
            _hoverProbes.Add(atCell(pg + new Vector2Int(7, 7), Vector2.zero)); _hoverProbes[_hoverProbes.Count - 1].name = "grid_far_7";
            _hoverProbes[_hoverProbes.Count - 1].note = "far away";
            _hoverProbes.Add(new HoverProbe { anchor = "abs", screen = new Vector2(2, 2), name = "screen_offscreen_2_2", note = "off the playable area" });
            H.KV("HOVER-SETUP", "playerGrid=" + pg + " probes=" + _hoverProbes.Count
                + " screen=" + Screen.width + "x" + Screen.height);
            H.Row("HOVERH", "round=0 playerGrid=" + pg + " screen=" + Screen.width + "x" + Screen.height);
            _probeIssued = false;
            _hoverWait = 4;   // let the camera/InputSystem settle before the first injection
        }

        /// <summary>
        /// Round 1 (2nd revision): probe points derived from the monster's OWN sprite rectangle
        /// (`SpriteRenderer.bounds` -> screen), NOT fixed pixel offsets above the cell centre.
        /// Why the change (measured 17:00): with fixed offsets it is impossible to tell
        /// "the fix did not work" from "that point is above the sprite top and is simply not the
        /// monster's body" -- the sprite is only +-0.84..0.93 world units tall (134..148 px / 64 PPU * 0.8)
        /// while 96 px of mouse travel may be more than that in world units.
        /// The player is teleported next to the monster first so the sprite is INSIDE the 1920x1080 frame
        /// (in the 17:00 run the monster's foot screen y was -36 = off-screen).
        /// </summary>
        private void BuildMonsterHoverProbe()
        {
            _hoverProbes.Clear();
            _hoverIdx = 0; _hoverWait = 0;
            var co = Camera.main;
            var mon = NearestAliveMonster();
            if (co == null || mon == null) { H.Warn("MONHOVER: no camera/monster"); return; }
            _monsterId = (int)H.Field(mon, "id");
            _monsterGrid = new Vector2Int((int)H.Field(mon, "gridX"), (int)H.Field(mon, "gridY"));

            // stand next to it so the sprite is on screen (same walkable-neighbour rule as the melee setup)
            var side = _monsterGrid;
            var dirs = new[]
            {
                new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1),
                new Vector2Int(0, 1), new Vector2Int(-1, -1), new Vector2Int(1, 1)
            };
            for (var d = 0; d < dirs.Length; d++)
            {
                var cand = _monsterGrid + dirs[d];
                var wk = H.Map() != null ? H.Invoke(H.Map(), "Walkable", cand) : null;
                if (wk is bool && (bool)wk) { side = cand; break; }
            }
            H.Invoke(H.Player(), "TeleportTo", side);
            H.SnapCam();

            var sr = H.NodeOfEntity(_monsterId);
            if (sr == null || sr.sprite == null)
            {
                H.Warn("MONHOVER: no sprite renderer for monster " + _monsterId + " => round 1 skipped");
                return;
            }

            var b = sr.bounds;
            var minS = (Vector2)co.WorldToScreenPoint(new Vector3(b.min.x, b.min.y, sr.transform.position.z));
            var maxS = (Vector2)co.WorldToScreenPoint(new Vector3(b.max.x, b.max.y, sr.transform.position.z));
            var x0 = Mathf.Min(minS.x, maxS.x); var x1 = Mathf.Max(minS.x, maxS.x);
            var y0 = Mathf.Min(minS.y, maxS.y); var y1 = Mathf.Max(minS.y, maxS.y);
            var cx = (x0 + x1) * 0.5f; var cy = (y0 + y1) * 0.5f; var hh = y1 - y0;

            H.KV("MONHOVER-SETUP", "monsterId=" + _monsterId + " name=\"" + H.S(H.Field(mon, "name")) + "\""
                + " monsterGrid=" + _monsterGrid + " playerStand=" + side
                + " sprite=" + sr.sprite.name
                + " bounds=(" + H.Fmt(b.min.x) + "," + H.Fmt(b.min.y) + ")-(" + H.Fmt(b.max.x) + "," + H.Fmt(b.max.y) + ")"
                + " screenRect=(" + x0.ToString("0") + "," + y0.ToString("0") + ")-(" + x1.ToString("0") + "," + y1.ToString("0") + ")"
                + " onScreen=" + (x1 >= 0 && y1 >= 0 && x0 <= Screen.width && y0 <= Screen.height ? 1 : 0));
            H.Row("HOVERH", "round=1 monsterId=" + _monsterId + " monsterGrid=" + _monsterGrid
                + " bounds=(" + H.Fmt(b.min.x) + "," + H.Fmt(b.min.y) + ")-(" + H.Fmt(b.max.x) + "," + H.Fmt(b.max.y) + ")"
                + " screenRect=(" + x0.ToString("0") + "," + y0.ToString("0") + ")-(" + x1.ToString("0") + "," + y1.ToString("0") + ")");

            // (a)/(b)/(c) points ON the body -> must hit; (d)/(e)/(f) points off it -> must NOT hit.
            // the anchors are resolved at INJECTION time against the current camera (see PointFor):
            //    a screen point frozen here was measured ~2300 px off because the camera was still
            //    catching up after the teleport (17:05 run, inRect=0 for every probe).
            _hoverProbes.Add(new HoverProbe { anchor = "rectFrac", fx = 0.5f, fy = 0.5f, name = "body_center", note = "inside sprite" });
            _hoverProbes.Add(new HoverProbe { anchor = "rectFrac", fx = 0.5f, fy = 0.85f, name = "body_85pct", note = "inside sprite (upper body)" });
            _hoverProbes.Add(new HoverProbe { anchor = "rectFrac", fx = 0.5f, fy = 0.20f, name = "body_20pct", note = "inside sprite (lower body)" });
            _hoverProbes.Add(new HoverProbe { anchor = "aboveTop", off = 8f, name = "above_top_8", note = "8px above the sprite top" });
            _hoverProbes.Add(new HoverProbe { anchor = "rightOf", off = 40f, name = "right_40", note = "40px right of the sprite" });
            _hoverProbes.Add(new HoverProbe { anchor = "abs", screen = new Vector2(2f, 2f), name = "offscreen_2_2", note = "off the playable area" });
            _probeIssued = false;
            _hoverWait = 6;   // the camera is still catching up right after the teleport+SnapCam
        }

        /// <summary>Read the state the LIVE Poll produced for the probe whose mouse was injected earlier.</summary>
        private void ReadHoverProbe(int i)
        {
            var pr = _hoverProbes[i];
            var ir = H.Field(H.Player(), "Input");
            if (ir == null) { H.Warn("HOVER: no InputReader"); return; }
            H.Invoke(ir, "UpdateHover", true);
            var hg = H.Field(ir, "HoverGrid");
            var cur = H.Field(ir, "CurrentHover");
            var pg = H.GridOf(H.Player());
            // round 1 diagnostics: is the mouse world point inside the monster's own sprite rect?
            var diag = string.Empty;
            if (_hoverRound == 1)
            {
                var co2 = Camera.main;
                var msr = H.NodeOfEntity(_monsterId);
                if (co2 != null && msr != null && msr.sprite != null)
                {
                    var bb = msr.bounds;
                    var w = Iso.ScreenToWorldOnGround(co2, new Vector3(pr.screen.x, pr.screen.y, 0f));
                    var inRect = w.x >= bb.min.x && w.x <= bb.max.x && w.y >= bb.min.y && w.y <= bb.max.y;
                    diag = " worldFromMouse=" + H.Fmt(w.x) + "," + H.Fmt(w.y)
                         + " nodePos=" + H.Fmt(msr.transform.position.x) + "," + H.Fmt(msr.transform.position.y)
                         + " bounds=(" + H.Fmt(bb.min.x) + "," + H.Fmt(bb.min.y) + ")-(" + H.Fmt(bb.max.x) + "," + H.Fmt(bb.max.y) + ")"
                         + " inRect=" + (inRect ? 1 : 0)
                         + " sprite=" + msr.sprite.name;
                }
                else diag = " diag=UNAVAILABLE cam=" + (co2 != null ? 1 : 0) + " sr=" + (msr != null ? 1 : 0);
            }

            var row = "round=" + _hoverRound + " probe=" + pr.name + " note=\"" + pr.note + "\""
                + " mouse=" + pr.screen.x.ToString("0") + "," + pr.screen.y.ToString("0")
                + " hoverGrid=" + H.S(hg)
                + " hasTarget=" + H.S(H.Field(cur, "hasTarget"))
                + " cursor=" + H.S(H.Field(cur, "cursor"))
                + " id=" + H.S(H.Field(cur, "id"))
                + " name=\"" + H.S(H.Field(cur, "name")) + "\""
                + " playerGrid=" + pg
                + " monsterNowGrid=" + MonsterGridNow()
                + diag;
            H.Row("HOVER", row);
            if (i == 0 && _hoverShotTaken == 0) { _hoverShotTaken = 1; Shot("s3_hover_foot.png"); }
            if (i == 2 && _hoverShotTaken == 1) { _hoverShotTaken = 2; Shot("s3_hover_body.png"); }
        }

        /// <summary>Where the tracked monster is RIGHT NOW (the hover probes must not use a stale cell).</summary>
        private string MonsterGridNow()
        {
            if (_monsterId < 0) return "-";
            var all = H.AsList(H.Field(H.Monster(), "All"));
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                if ((int)H.Field(all[i], "id") != _monsterId) continue;
                return H.S(H.Field(all[i], "gridX")) + "," + H.S(H.Field(all[i], "gridY"));
            }
            return "gone";
        }

        // ================================================================= monster hit ========
        private object NearestAliveMonster()
        {
            var all = H.AsList(H.Field(H.Monster(), "All"));
            var pg = H.GridOf(H.Player());
            object best = null; var bestD = int.MaxValue;
            for (var i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null) continue;
                var alive = H.Field(m, "alive");
                if (!(alive is bool) || !(bool)alive) continue;
                var g = new Vector2Int((int)H.Field(m, "gridX"), (int)H.Field(m, "gridY"));
                var d = Mathf.Max(Mathf.Abs(g.x - pg.x), Mathf.Abs(g.y - pg.y));
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }

        private void RecordHit()
        {
            _frameIn++;
            var all = H.AsList(H.Field(H.Monster(), "All"));
            object mon = null;
            for (var i = 0; i < all.Count; i++)
                if (all[i] != null && (int)H.Field(all[i], "id") == _monsterId) { mon = all[i]; break; }
            if (mon == null) { _hitRows.Add("frame=" + Time.frameCount + " MONSTER-GONE"); return; }
            var sr = H.NodeOfEntity(_monsterId);
            var sprite = (sr != null && sr.sprite != null) ? sr.sprite.name : "(none)";
            var row = "frame=" + Time.frameCount
                + " t=" + H.Fmt(Time.time)
                + " sprite=" + sprite
                + " hp=" + H.S(H.Field(mon, "hp"))
                + " alive=" + H.S(H.Field(mon, "alive"))
                + " grid=" + H.S(H.Field(mon, "gridX")) + "," + H.S(H.Field(mon, "gridY"))
                + " playerGrid=" + H.GridOf(H.Player())
                + " world=" + H.Fmt((float)H.Field(mon, "worldX")) + "," + H.Fmt((float)H.Field(mon, "worldY"));
            _hitRows.Add(row);
            H.Row("MHIT", row);
            if (_frameIn % 20 == 1) H.Invoke(H.Player(), "HandlePrimaryClick", _monsterGrid);
        }

        private void SummarizeHit()
        {
            var modes = new List<string>();
            var hitFrames = new List<int>();
            foreach (var r in _hitRows)
            {
                if (r.StartsWith("frame=") && r.Contains("MONSTER-GONE")) { modes.Add("gone"); continue; }
                var sn = ParseStr(r, "sprite=");
                var idx = FrameIndexOf(sn);
                var md = ModeOf(sn);
                modes.Add(md);
                if (idx >= 0) modes.Add(md + "#" + idx);
                if (md == "gethit" && idx >= 0) hitFrames.Add(idx);
            }
            var distinct = new HashSet<int>(hitFrames);
            var seq = new StringBuilder();
            for (var i = 0; i < hitFrames.Count && i < 40; i++) { if (i > 0) seq.Append(','); seq.Append(hitFrames[i]); }
            var modeSet = new HashSet<string>();
            foreach (var r in _hitRows)
            {
                if (r.StartsWith("frame=") && r.Contains("MONSTER-GONE")) { modeSet.Add("gone"); continue; }
                modeSet.Add(ModeOf(ParseStr(r, "sprite=")));
            }
            var ms = new List<string>(modeSet);
            H.KV("MHIT-VERDICT", "rows=" + _hitRows.Count + " getHitSamples=" + hitFrames.Count
                + " distinctGetHitFrames=" + distinct.Count + " seq=[" + seq + "]"
                + " modes=[" + string.Join(",", ms.ToArray()) + "]");
            H.Row("MHITV", "rows=" + _hitRows.Count + " getHitSamples=" + hitFrames.Count
                + " distinctGetHit=" + distinct.Count + " seq=" + seq + " modes=" + string.Join("|", ms.ToArray()));
        }

        private static int FrameIndexOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return -1;
            var i = sprite.LastIndexOf('_');
            if (i < 0 || i + 1 >= sprite.Length) return -1;
            int v;
            return int.TryParse(sprite.Substring(i + 1), out v) ? v : -1;
        }
        private static string ModeOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return "?";
            var s = sprite;
            var slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            var parts = s.Split('_');
            return parts.Length > 0 ? parts[0] : "?";
        }

        // ================================================================= monster move =======
        private void RecordMove()
        {
            _frameIn++;
            var all = H.AsList(H.Field(H.Monster(), "All"));
            object mon = null;
            for (var i = 0; i < all.Count; i++)
                if (all[i] != null && (int)H.Field(all[i], "id") == _monsterId) { mon = all[i]; break; }
            if (mon == null) { _moveRows.Add("frame=" + Time.frameCount + " MONSTER-GONE"); return; }
            var row = "frame=" + Time.frameCount
                + " dt=" + H.Fmt(Time.deltaTime)
                + " grid=" + H.S(H.Field(mon, "gridX")) + "," + H.S(H.Field(mon, "gridY"))
                + " world=" + H.Fmt((float)H.Field(mon, "worldX")) + "," + H.Fmt((float)H.Field(mon, "worldY"))
                + " alive=" + H.S(H.Field(mon, "alive"));
            _moveRows.Add(row);
            H.Row("MOVE", row);
        }

        private void SummarizeMove()
        {
            var gx = new List<int>(); var gy = new List<int>();
            var wx = new List<float>(); var wy = new List<float>();
            foreach (var r in _moveRows)
            {
                if (r.Contains("MONSTER-GONE")) continue;
                var g = ParseStr(r, "grid=");
                var w = ParseStr(r, "world=");
                var gp = g.Split(','); var wp = w.Split(',');
                int a, b; float c, d;
                if (gp.Length == 2 && int.TryParse(gp[0], out a) && int.TryParse(gp[1], out b)
                    && wp.Length == 2 && float.TryParse(wp[0], NumberStyles.Float, CultureInfo.InvariantCulture, out c)
                    && float.TryParse(wp[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                { gx.Add(a); gy.Add(b); wx.Add(c); wy.Add(d); }
            }
            var sameGridFrames = 0; var movedGridFrames = 0; var jumps = 0;
            var maxStep = 0f; var sumStep = 0f; var stepN = 0;
            for (var i = 1; i < wx.Count; i++)
            {
                var ddx = wx[i] - wx[i - 1]; var ddy = wy[i] - wy[i - 1];
                var d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                maxStep = Mathf.Max(maxStep, d); sumStep += d; stepN++;
                var gd = Mathf.Max(Mathf.Abs(gx[i] - gx[i - 1]), Mathf.Abs(gy[i] - gy[i - 1]));
                if (gd == 0) sameGridFrames++; else movedGridFrames++;
                if (gd > 1) jumps++;
            }
            var minX = float.MaxValue; var maxX = float.MinValue; var minY = float.MaxValue; var maxY = float.MinValue;
            for (var i = 0; i < wx.Count; i++)
            {
                if (wx[i] < minX) minX = wx[i]; if (wx[i] > maxX) maxX = wx[i];
                if (wy[i] < minY) minY = wy[i]; if (wy[i] > maxY) maxY = wy[i];
            }
            var spanX = wx.Count > 0 ? maxX - minX : 0f;
            var spanY = wx.Count > 0 ? maxY - minY : 0f;
            H.KV("MOVE-VERDICT", "frames=" + wx.Count + " sameGridFrames=" + sameGridFrames
                + " gridChangeFrames=" + movedGridFrames + " multiCellJumps=" + jumps
                + " maxPerFrameStep=" + H.Fmt(maxStep) + " meanPerFrameStep=" + H.Fmt(stepN > 0 ? sumStep / stepN : 0f)
                + " span=" + H.Fmt(spanX) + "x" + H.Fmt(spanY));
            H.Row("MOVEV", "frames=" + wx.Count + " sameGrid=" + sameGridFrames + " gridChanged=" + movedGridFrames
                + " jumps=" + jumps + " maxStep=" + H.Fmt(maxStep) + " span=" + H.Fmt(spanX) + "x" + H.Fmt(spanY));
        }

        // ================================================================= parsing ============
        private static string ParseStr(string row, string key)
        {
            var i = row.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            i += key.Length;
            var j = i;
            while (j < row.Length && row[j] != ' ' && row[j] != '\t') j++;
            return row.Substring(i, j - i);
        }
        private static int ParseInt(string row, string key)
        {
            var s = ParseStr(row, key); int v;
            return int.TryParse(s, out v) ? v : int.MinValue;
        }
        private static float ParseFloat(string row, string key)
        {
            var s = ParseStr(row, key); float v;
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : float.NaN;
        }

        private void Finish(string why)
        {
            if (_doneFlag) return;
            _doneFlag = true;
            H.RowFlush();
            H.KV("FINISH", "why=" + why + " step=" + _step + " device=\"" + SystemInfo.graphicsDeviceName + "\"");
            H.Log("TOUR-DONE why=" + why + " step=" + _step);
            H.WriteFile(_done, "S3-DONE why=" + why + " step=" + _step
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }
    }
}
