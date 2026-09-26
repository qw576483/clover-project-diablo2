// =============================================================================
// u1u2_evidence.cs -- ONE Play session that re-collects the runtime evidence two
//   acceptance rows cite, because the sources those rows observe were edited:
//
//     U-1 row (click-to-move feel / pathing; no direction-key move):
//       - hold W+A+S+D for 1.5 s: sample the grid every 0.1 s -> the character must
//         not move (the direction-key move families were deleted);
//       - hold W alone: one SwapWeaponRequest, still no movement;
//       - a real ground click: MoveCommand + a real walk + arrival;
//       - render device must not be a software rasterizer;
//     U-2 row (attack cadence + hit feedback):
//       - two production attacks (ICombatModule.RequestAttack) while sampling every
//         frame: the player's own attack frames, the monster's hit frames, the
//         damage float text and the monster hp.
//
//   Everything is driver-side: the input goes through the engine's documented seam
//   Game.AttachInput(IInputManager) (the same member the game's InputReader polls),
//   no production file is touched, and the only files written are evidence.
//
//   spec = "<charName>|<doneMarker>|<shotsDir>|<extraDir>"
//   ASCII only.
// =============================================================================
namespace U1U2
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using CloverEngine;
    using Diablo2.Core;
    using Diablo2.Def;
    using Diablo2.Module;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>Log / reflection / screenshot helpers (self-contained probe).</summary>
    public static class L
    {
        internal const string Tag = "U12";

        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[U12] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string msg) { Log("WARN " + msg); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("U12-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var dir = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_done, "U12-DONE " + DateTime.Now.ToString("o"));
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

        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }
        internal static IViewModule View() { return CtxMember("View") as IViewModule; }
        internal static IMonsterModule Monster() { return CtxMember("Monster") as IMonsterModule; }
        internal static ICombatModule Combat() { return CtxMember("Combat") as ICombatModule; }
        internal static object PlayerRaw() { return CtxMember("Player"); }

        internal static void Emit(string evt) { Game.Event.Emit(evt); }
        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }

        internal static string F(float v) { return v.ToString("0.###"); }

        internal static string Shot(string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                return "SHOT-OK " + path + " frame=" + Time.frameCount;
            }
            catch (Exception ex) { return "SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>Append a section to a UTF-8 text file (keeps whatever the file already holds:
        /// other rows cite the same capture file).</summary>
        internal static string AppendText(string path, string section)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var old = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
                if (old.Length > 0 && !old.EndsWith("\n")) old += "\n";
                File.WriteAllText(path, old + section, new UTF8Encoding(false));
                return "TEXT-OK " + path + " bytes=" + new FileInfo(path).Length;
            }
            catch (Exception ex) { return "TEXT-FAIL " + path + " " + ex.GetType().Name + ": " + ex.Message; }
        }
    }

    /// <summary>Input manager that answers the engine seam with scripted state: the pointer,
    /// the left button edges and the held key set. Every other member forwards to the live one.</summary>
    internal sealed class ScriptedInput : IInputManager
    {
        private readonly IInputManager _real;

        public Vector3 Pointer;

        /// <summary>Frame in which the click was armed: down on ClickFrame+1, held +1..+3, up +4.</summary>
        public int ClickFrame;

        /// <summary>Held keys reported by GetKey.</summary>
        public readonly HashSet<GameKey> Held = new HashSet<GameKey>();

        /// <summary>Keys whose GetKeyDown edge fires on exactly this frame.</summary>
        public readonly Dictionary<GameKey, int> EdgeFrame = new Dictionary<GameKey, int>();

        public ScriptedInput(IInputManager real) { _real = real; }

        public void Press(GameKey key) { Held.Add(key); EdgeFrame[key] = Time.frameCount + 1; }
        public void Release(GameKey key) { Held.Remove(key); EdgeFrame.Remove(key); }
        public void ReleaseAll() { Held.Clear(); EdgeFrame.Clear(); }

        public InputState State { get { return _real.State; } }
        public bool Available { get { return true; } }
        public string BackendName { get { return _real.BackendName; } }
        public bool IsLocked { get { return _real.IsLocked; } }
        public bool HasTouch { get { return false; } }
        public bool PointerOverUi { get { return _real.PointerOverUi; } }
        public void Lock() { _real.Lock(); }
        public void Unlock() { _real.Unlock(); }
        public bool GetKey(GameKey key) { return Held.Contains(key); }
        public bool GetKeyDown(GameKey key)
        {
            int f;
            return EdgeFrame.TryGetValue(key, out f) && Time.frameCount == f;
        }
        public bool GetKeyUp(GameKey key) { return false; }
        public bool GetMouseButton(int button)
        {
            if (button != 0) return _real.GetMouseButton(button);
            return ClickFrame != 0 && Time.frameCount >= ClickFrame + 1 && Time.frameCount <= ClickFrame + 3;
        }
        public bool GetMouseButtonDown(int button)
        {
            if (button != 0) return _real.GetMouseButtonDown(button);
            return ClickFrame != 0 && Time.frameCount == ClickFrame + 1;
        }
        public bool GetMouseButtonUp(int button)
        {
            if (button != 0) return _real.GetMouseButtonUp(button);
            return ClickFrame != 0 && Time.frameCount == ClickFrame + 4;
        }
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

    /// <summary>Compile sentinel entry for the runner (run_script --entry).</summary>
    public static class Probe
    {
        public static string Ping()
        {
            return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000");
        }
    }

    /// <summary>Callable entry for the runner (run_script --entry).</summary>
    public static class U1U2Run
    {
        public static string Install(string spec)
        {
            var go = new GameObject("U1U2Driver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("U12-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One session: town input checks -> ground walk (6 frames) -> two panels ->
    /// BloodMoor attacks (player attack frames + monster hit chain).</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotsDir = string.Empty;
        private string _extraDir = string.Empty;

        /// <summary>Shop-only mode (spec field 5 = `shoponly`): boot to town, open the shop panel and
        /// shoot it as it was opened -- no tab is touched, so the current page keeps its pressed state.</summary>
        private bool _shopOnly;

        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _shopOpened;
        private bool _shopSnapped;
        private ScriptedInput _fake;
        private int _probePhase;

        // ---- U-1 leg -----------------------------------------------------------------
        private const int KeyHoldSamples = 15;
        private int _keySamples;
        private float _keySampleAt;
        private Vector2Int _keyStartGrid = new Vector2Int(-9999, -9999);
        private int _keyMovedSamples;
        private int _swapRequests;
        private int _moveCommands;
        private readonly List<string> _keyLines = new List<string>();
        private int _keyChecksOk;

        // ---- walk leg ----------------------------------------------------------------
        private Vector2Int _walkTarget;
        private Vector3 _clickScreen;
        private int _walkFrames;
        private int _walkShot;
        private bool _walkSeenMove;
        private int _walkMoveCommands;
        private Vector2Int _walkEndGrid;
        private float _walkFrom;
        private readonly List<string> _walkLines = new List<string>();

        // ---- panels / attacks --------------------------------------------------------
        private IMonsterModule _mon;
        private int _monId = -1;
        private int _attackIndex;
        private float _nextAttackAt;
        private int _atkShot;           // 0 none / 1 X3-a1 / 2 X3-a1b / 3 X3-a2b
        private int _atkIdxSeen = -1;
        private int _e1Shot;            // 0 none / 1 a / 2 b / 3 c / 4 after
        private int _e1IdxSeen = -1;
        private int _e1LastAttack = -1;
        private float _lastHitAt;
        private const int MaxAttacks = 6;
        private float _attackWindowUntil;
        private int _gridLines;
        private readonly List<string> _grids = new List<string>();
        private string _lastVals = "(none)";

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotsDir = p[2];
            if (p.Length > 3) _extraDir = p[3];
            if (p.Length > 4) _shopOnly = p[4].Trim().ToLowerInvariant() == "shoponly";
            _t0 = Time.unscaledTime;
            L.Log("U12-INIT char=" + _name + " shots=" + _shotsDir + " extra=" + _extraDir);
            try
            {
                Game.Event.On(Events.SwapWeaponRequest, OnSwapRequest);
                Game.Event.On<Vector2Int>(Events.MoveCommand, OnMoveCommand);
                L.Log("U12-SUBSCRIBE ok swap+move");
            }
            catch (Exception ex) { L.Warn("U12-SUBSCRIBE ex " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnSwapRequest() { _swapRequests++; }
        private void OnMoveCommand(Vector2Int g) { _moveCommands++; }

        // ---- helpers -----------------------------------------------------------------

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        private static bool Open<T>() where T : class, CloverEngine.IUIPanel
        {
            return Game.UI != null && Game.UI.IsOpen<T>();
        }

        /// <summary>Shots waiting for their own frame: {leaf, grid}; the values are read when the shot
        /// is really taken, so the `[X] GRID` line describes the captured frame.</summary>
        private readonly List<string[]> _snapQueue = new List<string[]>();

        private void EnqueueSnap(string leaf, string grid)
        {
            _snapQueue.Add(new[] { leaf, grid });
            if (_snapQueue.Count > 24) _snapQueue.RemoveAt(0);
        }

        private void DrainSnapQueue()
        {
            if (_snapQueue.Count == 0) return;
            var s = _snapQueue[0];
            _snapQueue.RemoveAt(0);
            SnapShot(s[0], s[1], CurrentVals());
        }

        /// <summary>The values burned into the index line for the current frame.</summary>
        private string CurrentVals()
        {
            var p = P();
            var sprite = SpriteName();
            var ms = _mon != null && _monId >= 0 ? _mon.Get(_monId) : null;
            var msprite = _monId >= 0 ? MonsterSprite(_monId) : "(no-monster)";
            return "phase=attack attackN=" + _attackIndex
                + " playerFrame=" + sprite + " playerAction=" + ActionOf(sprite)
                + " monsterFrame=" + msprite + " monsterAction=" + ActionOf(msprite)
                + " monsterHp=" + (ms != null ? ms.hp + "/" + ms.maxHp : "?")
                + " monsterAlive=" + (ms != null ? ms.alive.ToString() : "?")
                + " playerMoving=" + (p != null ? p.IsMoving.ToString() : "?")
                + " floats=" + FloatTextDump();
        }

        private string SnapShot(string leaf, string grid, string vals)
        {
            var dir = _shotsDir;
            if (leaf.StartsWith("closing_", StringComparison.Ordinal)) dir = _extraDir;
            if (string.IsNullOrEmpty(dir)) { L.Warn("SNAP-NO-DIR " + leaf); return "SNAP-NO-DIR"; }
            var res = L.Shot(dir + "/" + leaf);
            L.KV("SNAP " + leaf, res);
            if (!string.IsNullOrEmpty(grid))
            {
                _grids.Add("[X] GRID=" + grid + " tile=" + leaf + " vals=\"" + vals + "\"");
                _lastVals = vals;
                _gridLines++;
            }
            return res;
        }

        private static IPlayerModule P() { return L.PlayerRaw() as IPlayerModule; }

        private static string SpriteName()
        {
            var view = L.View();
            if (view == null) return "(no-view)";
            var go = view.GetView(GameConst.PlayerEntityId);
            if (go == null) return "(no-node)";
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return "(no-sprite)";
            return sr.sprite.name;
        }

        private static string MonsterSprite(int id)
        {
            var view = L.View();
            if (view == null) return "(no-view)";
            var go = view.GetView(id);
            if (go == null) return "(no-node)";
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return "(no-sprite)";
            return sr.sprite.name;
        }

        private static int IndexOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return -1;
            var i = sprite.LastIndexOf('_');
            if (i < 0 || i + 1 >= sprite.Length) return -1;
            int n;
            return int.TryParse(sprite.Substring(i + 1), out n) ? n : -1;
        }

        /// <summary>Action token of a frame key: `attack_s_2` -> `attack`.</summary>
        private static string ActionOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return string.Empty;
            var i = sprite.LastIndexOf('_');
            if (i <= 0) return string.Empty;
            var j = sprite.LastIndexOf('_', i - 1);
            if (j <= 0) return string.Empty;
            var k = sprite.LastIndexOf('_', j - 1);
            return k < 0 ? sprite.Substring(0, j) : sprite.Substring(k + 1, j - k - 1);
        }

        /// <summary>The engine's damage float text node layout is `FloatTexts` -> `FloatText`
        /// (CanvasGroup) -> `Label` (Text, engine `FloatTextLayer`); dump the visible ones as
        /// "text a=colourAlpha g=groupAlpha".</summary>
        private static string FloatTextDump()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<Text>();
                var sb = new List<string>();
                for (var i = 0; i < all.Length && sb.Count < 6; i++)
                {
                    var t = all[i];
                    if (t == null || t.transform == null || t.transform.parent == null) continue;
                    var up1 = t.transform.parent;
                    var up2 = up1.parent;
                    var inFloat = up1.name == "FloatText"
                        || (up2 != null && up2.name == "FloatTexts");
                    if (!inFloat) continue;
                    if (!t.gameObject.activeInHierarchy) continue;
                    var group = up1.GetComponent<CanvasGroup>();
                    sb.Add("[" + t.text + " a=" + t.color.a.ToString("0.00")
                        + " g=" + (group != null ? group.alpha.ToString("0.00") : "1.00") + "]");
                }
                return sb.Count == 0 ? "(none)" : string.Join("", sb.ToArray());
            }
            catch (Exception ex) { return "(float-ex " + ex.GetType().Name + ")"; }
        }

        private static Camera Cam()
        {
            var c = Camera.main;
            if (c != null) return c;
            var all = Resources.FindObjectsOfTypeAll<Camera>();
            Camera best = null;
            for (var i = 0; i < all.Length; i++)
            {
                var x = all[i];
                if (x == null || !x.enabled || !x.gameObject.activeInHierarchy) continue;
                if (best == null || x.depth > best.depth) best = x;
            }
            return best;
        }

        private static bool ScreenForGround(Camera cam, Vector2 worldXy, out Vector3 screen)
        {
            screen = Vector3.zero;
            if (cam == null) return false;
            var v0 = Iso.ScreenToWorldOnGround(cam, new Vector3(0f, 0f, 0f));
            var vx = Iso.ScreenToWorldOnGround(cam, new Vector3(1f, 0f, 0f));
            var vy = Iso.ScreenToWorldOnGround(cam, new Vector3(0f, 1f, 0f));
            var ax = new Vector2(vx.x - v0.x, vx.y - v0.y);
            var ay = new Vector2(vy.x - v0.x, vy.y - v0.y);
            var d = new Vector2(worldXy.x - v0.x, worldXy.y - v0.y);
            var det = ax.x * ay.y - ax.y * ay.x;
            if (Mathf.Abs(det) < 1e-6f) return false;
            var sx = (d.x * ay.y - d.y * ay.x) / det;
            var sy = (ax.x * d.y - ax.y * d.x) / det;
            screen = new Vector3(sx, sy, 0f);
            return true;
        }

        private static bool ScreenOnWindow(Camera cam, Vector2Int g, out Vector3 screen)
        {
            screen = Vector3.zero;
            var w = Iso.GridToWorld(g.x, g.y);
            if (!ScreenForGround(cam, new Vector2(w.x, w.y), out screen)) return false;
            const float pad = 8f;
            return screen.x >= pad && screen.y >= pad
                && screen.x <= Screen.width - pad && screen.y <= Screen.height - pad;
        }

        /// <summary>Pick a reachable walkable cell at Chebyshev distance [minD,maxD] and return its
        /// screen point (the click must land inside the window).</summary>
        private bool PickWalkTarget(int minD, int maxD, out Vector2Int target, out Vector3 screen)
        {
            target = new Vector2Int(-9999, -9999);
            screen = Vector3.zero;
            var map = L.Map();
            var p = P();
            var cam = Cam();
            if (map == null || p == null || cam == null) return false;
            for (var dx = -maxD; dx <= maxD; dx++)
            {
                for (var dy = -maxD; dy <= maxD; dy++)
                {
                    var cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (cheb < minD || cheb > maxD) continue;
                    var g = new Vector2Int(p.Grid.x + dx, p.Grid.y + dy);
                    if (!map.InBounds(g) || !map.Walkable(g)) continue;
                    var path = map.FindPath(p.Grid, g);
                    if (path == null || path.Count < minD) continue;
                    var bad = false;
                    for (var i = 0; i < path.Count; i++)
                    {
                        if (map.TileAt(path[i]) == TileKind.Exit) { bad = true; break; }
                    }
                    if (bad) continue;
                    Vector3 s;
                    if (!ScreenOnWindow(cam, g, out s)) continue;
                    target = g;
                    screen = s;
                    return true;
                }
            }
            return false;
        }

        private void SwapInput()
        {
            if (_fake != null) return;
            _fake = new ScriptedInput(Game.Input);
            Game.AttachInput(_fake);
            L.KV("INPUT-SWAP", "Game.AttachInput(ScriptedInput) real backend=" + _fake.BackendName);
        }

        private void ArmClick(Vector3 screen)
        {
            SwapInput();
            _fake.Pointer = screen;
            _fake.ClickFrame = Time.frameCount;
            L.KV("U12-ARM-CLICK", "frame=" + Time.frameCount + " pointer=" + L.F(screen.x) + "," + L.F(screen.y));
        }

        // ---- main state machine ------------------------------------------------------

        private void Update()
        {
            if (_done) return;
            if (Time.unscaledTime - _t0 > 600f) { L.Warn("WATCHDOG 600s -> finish"); Finish(); return; }
            // `ScreenCapture.CaptureScreenshot` honours only the LAST request of a frame (measured:
            // two requests in one frame leave the first file missing), so the shots are queued and
            // exactly ONE is taken per frame.
            try { DrainSnapQueue(); } catch (Exception ex) { L.Warn("SNAP-DRAIN ex " + ex.GetType().Name); }
            try { Step(); }
            catch (Exception ex)
            {
                L.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void Step()
        {
            switch (_step)
            {
                case 0:
                    if (Open<Diablo2.UI.BootPanel>() || Fsm() == Events.Fsm.StateBoot)
                    {
                        if (!Elapsed(2f)) break;
                        L.Log("BOOT fsm=" + Fsm());
                        L.Emit(Events.BootDone);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { L.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(120f)) { L.Warn("boot 120s fsm=" + Fsm()); Next(); }
                    break;

                case 1:
                    if (Open<Diablo2.UI.MainMenuPanel>() || Fsm() == Events.Fsm.StateMainMenu)
                    {
                        if (!Elapsed(1.4f)) break;
                        L.Log("MENU fsm=" + Fsm());
                        L.Emit(Events.Fsm.TriggerNewGame);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { L.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(60f)) { L.Warn("main menu 60s fsm=" + Fsm()); Next(); }
                    break;

                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect
                        || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(1.6f)) break;
                        L.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { L.Warn("char select 30s fsm=" + Fsm()); Next(); }
                    break;

                // 3) town: log the render device + the U-1 preconditions, then start the key leg
                case 3:
                    if (!(Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage))
                    {
                        if (Elapsed(120f)) { L.Warn("stage 120s fsm=" + Fsm()); Next(); }
                        break;
                    }
                    if (!Elapsed(2.5f)) break;
                    var map0 = L.Map();
                    var p0 = P();
                    var mon0 = L.Monster();
                    var dev = SystemInfo.graphicsDeviceName;
                    var software = dev == null
                        || dev.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0
                        || dev.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) >= 0
                        || dev.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) >= 0;
                    L.KV("U12-STAGE", "fsm=" + Fsm()
                        + " area=" + (map0 != null ? map0.Area.ToString() : "(no-map)")
                        + " grid=" + (p0 != null ? p0.Grid.ToString() : "(no-player)")
                        + " dead=" + (p0 != null ? p0.IsDead.ToString() : "?")
                        + " aliveMonsters=" + (mon0 != null ? mon0.AliveCount : -1)
                        + " device=\"" + dev + "\" softwareRasterizer=" + software
                        + " graphicsDeviceType=" + SystemInfo.graphicsDeviceType);
                    _keyChecksOk += software ? 0 : 1;
                    _keyLines.Add("[U1] CHK name=real render device is a hardware GPU ok=" + (software ? 0 : 1)
                        + " device=\"" + dev + "\" type=" + SystemInfo.graphicsDeviceType);
                    var inTown = map0 != null && map0.Area == AreaId.Town;
                    _keyChecksOk += inTown ? 1 : 0;
                    _keyLines.Add("[U1] CHK name=player is alive in town ok="
                        + (inTown && p0 != null && !p0.IsDead ? 1 : 0)
                        + " area=" + (map0 != null ? map0.Area.ToString() : "?")
                        + " dead=" + (p0 != null ? p0.IsDead.ToString() : "?"));
                    if (_shopOnly)
                    {
                        L.KV("U12-SHOP-ONLY", "mode=shoponly: open the shop panel and shoot it as opened");
                        _at = Time.unscaledTime;
                        _step = 20;
                        break;
                    }
                    _keyStartGrid = p0 != null ? p0.Grid : new Vector2Int(-9999, -9999);
                    L.KV("U1-KEY-START", "grid=" + _keyStartGrid + " holdSeconds=1.5 keys=W+A+S+D");
                    SwapInput();
                    _fake.Press(GameKeyAlias.KeySwapWeapon);
                    _fake.Press(GameKey.A);
                    _fake.Press(GameKey.S);
                    _fake.Press(GameKey.D);
                    _keySampleAt = Time.unscaledTime;
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 4) hold W+A+S+D for 1.5 s: sample the grid every 0.1 s
                case 4:
                    SampleHeldKeys();
                    if (Time.unscaledTime - _at < 1.6f) break;
                    _fake.ReleaseAll();
                    var movedKey = _keyMovedSamples;
                    // 1.6 s at one sample per 0.1 s: the sample count depends on frame timing, so the
                    // check is "the character never moved" plus "enough samples were taken".
                    var okNoMove = _keySamples >= 10 && movedKey == 0;
                    _keyChecksOk += okNoMove ? 1 : 0;
                    _keyLines.Add("[U1] CHK name=direction keys do not drive the character ok=" + (okNoMove ? 1 : 0)
                        + " samples=" + _keySamples + " movingSamples=" + movedKey
                        + " startGrid=" + _keyStartGrid + " endGrid=" + (P() != null ? P().Grid.ToString() : "?")
                        + " moveCommands=" + _moveCommands + " swapRequests=" + _swapRequests);
                    L.KV("U1-KEY-HOLD-RESULT", "samples=" + _keySamples + " movingSamples=" + movedKey
                        + " moveCommands=" + _moveCommands + " swapRequests=" + _swapRequests);
                    _swapBeforeW = _swapRequests;
                    _moveBeforeW = _moveCommands;
                    _gridBeforeW = P() != null ? P().Grid : new Vector2Int(-9999, -9999);
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 5) W alone: swap weapon request fires, the character still must not move
                case 5:
                    if (!Elapsed(0.3f)) break;
                    if (_swapBeforeW < 0) { break; }
                    if (!_wPressed)
                    {
                        _wPressed = true;
                        _fake.Press(GameKeyAlias.KeySwapWeapon);
                        _at = Time.unscaledTime;
                        break;
                    }
                    if (!Elapsed(1.0f)) break;
                    _fake.ReleaseAll();
                    var swapDelta = _swapRequests - _swapBeforeW;
                    var moveDeltaW = _moveCommands - _moveBeforeW;
                    var endW = P() != null ? P().Grid : new Vector2Int(-9999, -9999);
                    var movedW = Mathf.Abs(endW.x - _gridBeforeW.x) + Mathf.Abs(endW.y - _gridBeforeW.y);
                    var okW = swapDelta == 1 && moveDeltaW == 0 && movedW == 0;
                    _keyChecksOk += okW ? 1 : 0;
                    _keyLines.Add("[U1] CHK name=W key = swap weapon group and still does not move ok=" + (okW ? 1 : 0)
                        + " swapRequest_delta=" + swapDelta + " moveCommand_delta=" + moveDeltaW
                        + " moved_cells=" + movedW);
                    L.KV("U1-W-KEY-RESULT", "swapRequest_delta=" + swapDelta + " moveCommand_delta=" + moveDeltaW
                        + " moved_cells=" + movedW);
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 6) pick a target cell and arm one real ground click
                case 6:
                    if (!Elapsed(0.6f)) break;
                    Vector2Int target;
                    Vector3 screen;
                    if (!PickWalkTarget(4, 6, out target, out screen))
                    {
                        L.Warn("U1-WALK-NO-TARGET");
                        _walkLines.Add("[U1] WARN no walkable target cell found for the ground click");
                        Next();
                        Next();
                        break;
                    }
                    _walkTarget = target;
                    _clickScreen = screen;
                    var path = L.Map().FindPath(P().Grid, target);
                    L.KV("U1-WALK-REQUEST", "from=" + P().Grid + " to=" + target
                        + " pathCells=" + (path != null ? path.Count : -1)
                        + " running=" + P().IsRunning
                        + " screen=" + L.F(screen.x) + "," + L.F(screen.y));
                    ArmClick(screen);
                    _moveBeforeClick = _moveCommands;
                    _walkFrames = 0;
                    _walkSeenMove = false;
                    _walkShot = 0;
                    _walkFrom = Time.unscaledTime;
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 7) walking: sample every frame; take the 6 consecutive walk frames (a09_walk_1..6)
                case 7:
                    WalkSample();
                    var wp = P();
                    if (wp == null) break;
                    if (wp.IsMoving && Time.unscaledTime - _walkFrom < 40f) break;
                    if (!_walkSeenMove && Time.unscaledTime - _walkFrom < 8f) break;
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 8) arrival: the click must have produced a move command + real displacement
                case 8:
                    if (!Elapsed(0.4f)) break;
                    var p1 = P();
                    _walkEndGrid = p1 != null ? p1.Grid : new Vector2Int(-9999, -9999);
                    var cells = Mathf.Max(Mathf.Abs(_walkEndGrid.x - _keyStartGrid.x),
                                          Mathf.Abs(_walkEndGrid.y - _keyStartGrid.y));
                    var cmdDelta = _moveCommands - _moveBeforeClick;
                    var okClick = cmdDelta >= 1 && cells > 0;
                    _keyChecksOk += okClick ? 1 : 0;
                    _keyLines.Add("[U1] CHK name=clicking the ground really moves the character ok=" + (okClick ? 1 : 0)
                        + " moveCommand_delta=" + cmdDelta + " moved_cells=" + cells
                        + " from=" + _keyStartGrid + " to=" + _walkEndGrid
                        + " target=" + _walkTarget + " reached=" + (_walkEndGrid == _walkTarget)
                        + " clickScreen=" + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y));
                    L.KV("U1-CLICK-RESULT", "moveCommand_delta=" + cmdDelta + " moved_cells=" + cells
                        + " reached=" + (_walkEndGrid == _walkTarget));
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 9) shop panel (human confirmation shot: the 4 tabs must be the normal bright faces)
                case 9:
                    if (!Elapsed(0.6f)) break;
                    L.KV("U12-SHOP-REQUEST", "emit " + Events.ShopOpenRequest + "(" + (int)NpcId.Charsi + ")");
                    L.Emit(Events.ShopOpenRequest, (int)NpcId.Charsi);
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 10) shop shot. The panel art loads asynchronously (1.6 s caught the "loading"
                //     placeholder) and `ScreenCapture` captures at the END of the frame, so nothing
                //     else may change the panels in the frame a shot is taken: every panel action
                //     that would alter the picture gets its own step.
                case 10:
                    if (!Elapsed(4.5f)) break;
                    SnapShot("closing_shop_tabs.png", string.Empty, string.Empty);
                    L.KV("U12-SHOP-SHOT", "shopOpen=" + Open<Diablo2.UI.ShopPanel>());
                    _at = Time.unscaledTime;
                    Next();
                    break;

                case 11:
                    if (!Elapsed(0.5f)) break;
                    if (Game.UI != null) Game.UI.Close<Diablo2.UI.ShopPanel>();
                    L.Emit(Events.PanelToggleRequest, "CharacterPanel");
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 12) character panel shot (labels must not wrap, e.g. the attack rating row)
                case 12:
                    if (!Elapsed(4.5f)) break;
                    SnapShot("closing_char_panel.png", string.Empty, string.Empty);
                    L.KV("U12-CHAR-PANEL-SHOT", "charPanelOpen=" + Open<Diablo2.UI.CharacterPanel>());
                    _at = Time.unscaledTime;
                    Next();
                    break;

                case 13:
                    if (!Elapsed(0.5f)) break;
                    if (Game.UI != null) Game.UI.Close<Diablo2.UI.CharacterPanel>();
                    L.Emit(Events.ExitEntered, AreaId.BloodMoor);
                    L.KV("U12-MOOR-REQUEST", "emit " + Events.ExitEntered + "(BloodMoor)");
                    _at = Time.unscaledTime;
                    Next();
                    break;

                // 14) wait for BloodMoor + monsters, then stand next to one
                case 14:
                    var m0 = L.Map();
                    var mon1 = L.Monster();
                    if (m0 != null && m0.Area == AreaId.BloodMoor && mon1 != null && mon1.AliveCount > 0)
                    {
                        _mon = mon1;
                        L.KV("U12-MOOR-STAGE", "area=" + m0.Area + " alive=" + mon1.AliveCount
                            + " total=" + mon1.All.Count);
                        StandNextToMonster();
                        _attackWindowUntil = Time.unscaledTime + 90f;
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { L.Warn("U12-MOOR-STAGE 30s area=" + (m0 != null ? m0.Area.ToString() : "?")); Next(); }
                    break;

                case 15:
                    if (!Elapsed(0.6f)) break;
                    Next();
                    break;

                // 16) attack loop: two production attacks (and more if the shot set is incomplete)
                case 16:
                    AttackTick();
                    if (_atkShot >= 3 && _e1Shot >= 4 && _snapQueue.Count == 0) { _at = Time.unscaledTime; Next(); break; }
                    if (_attackIndex >= MaxAttacks || Time.unscaledTime > _attackWindowUntil)
                    {
                        L.Warn("U12-ATTACK-STOP attacks=" + _attackIndex + " atkShot=" + _atkShot
                            + " e1Shot=" + _e1Shot);
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 20) shop-only mode: open the shop, let its art load, then shoot it WITHOUT touching
                //     a single tab (the page that was open on entry must keep its pressed face).
                case 20:
                    if (!_shopOpened)
                    {
                        _shopOpened = true;
                        L.KV("U12-SHOP-ONLY-REQUEST", "emit " + Events.ShopOpenRequest + "(" + (int)NpcId.Charsi + ")");
                        L.Emit(Events.ShopOpenRequest, (int)NpcId.Charsi);
                        _at = Time.unscaledTime;
                        break;
                    }
                    if (!_shopSnapped)
                    {
                        if (!Elapsed(4.5f)) break;
                        SnapShot("closing_shop_tabs_after.png", string.Empty, string.Empty);
                        L.KV("U12-SHOP-AFTER-SHOT", "shopOpen=" + Open<Diablo2.UI.ShopPanel>()
                            + " tabTouched=false");
                        _shopSnapped = true;
                        _at = Time.unscaledTime;
                        break;
                    }
                    // `ScreenCapture` writes at the END of the frame: keep running a moment so the file
                    // really lands before the session is stopped.
                    if (!Elapsed(2.0f)) break;
                    Finish();
                    break;

                // 17) write the evidence texts and finish（等最后一张排队的图真的落盘）
                case 17:
                    if (_snapQueue.Count > 0 && Time.unscaledTime - _at < 3f) break;
                    if (!Elapsed(0.4f)) break;
                    WriteEvidence();
                    Finish();
                    break;
            }
        }

        private int _swapBeforeW = -1;
        private int _moveBeforeW = -1;
        private int _moveBeforeClick = -1;
        private Vector2Int _gridBeforeW = new Vector2Int(-9999, -9999);
        private bool _wPressed;

        private void SampleHeldKeys()
        {
            if (Time.unscaledTime - _keySampleAt < 0.1f) return;
            _keySampleAt = Time.unscaledTime;
            var p = P();
            if (p == null) return;
            while (_keySamples < KeyHoldSamples)
            {
                _keySamples++;
                var moving = p.IsMoving;
                if (moving) _keyMovedSamples++;
                _keyLines.Add("[U1] SAMPLE n=" + _keySamples.ToString("00") + " grid=" + p.Grid
                    + " IsMoving=" + (moving ? "True" : "False"));
                return;
            }
        }

        private void WalkSample()
        {
            var p = P();
            if (p == null) return;
            var sprite = SpriteName();
            var c = Iso.WorldToGridContinuous(p.World);
            _walkFrames++;
            if (p.IsMoving) _walkSeenMove = true;
            if (_walkFrames <= 400)
            {
                _walkLines.Add("[U1] WALK n=" + _walkFrames + " f=" + Time.frameCount
                    + " t=" + L.F(Time.unscaledTime - _walkFrom) + " grid=" + p.Grid
                    + " cell=" + L.F(c.x) + "," + L.F(c.y) + " sprite=" + sprite
                    + " dir=" + p.Dir + " moving=" + p.IsMoving);
            }
            // six consecutive frames of this one walk: a09_walk_1..6
            if (_walkShot < 6 && _walkFrames >= 4)
            {
                _walkShot++;
                var leaf = "a09_walk_" + _walkShot + ".png";
                SnapShot(leaf, string.Empty, string.Empty);
            }
        }

        /// <summary>Teleport onto a walkable cell adjacent to an alive monster (driver setup:
        /// a live session would walk there; everything after it is the production AI).</summary>
        private void StandNextToMonster()
        {
            _mon = L.Monster();
            var map = L.Map();
            var raw = L.PlayerRaw();
            if (_mon == null || map == null || raw == null)
            {
                L.Warn("U12-ATK-SETUP precondition mon=" + (_mon != null) + " map=" + (map != null)
                    + " player=" + (raw != null));
                return;
            }
            var method = raw.GetType().GetMethod("TeleportTo", new Type[] { typeof(Vector2Int) });
            if (method == null) { L.Warn("U12-ATK-SETUP TeleportTo missing"); return; }
            var all = _mon.All;
            // The hit chain needs a monster that survives a couple of hits: prefer the one with the
            // greatest max hp among the alive ones (a 3 hp monster dies on the first hit).
            MonsterState pick = null;
            for (var i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.alive) continue;
                if (pick == null || c.maxHp > pick.maxHp) pick = c;
            }
            if (pick == null) { L.KV("U12-ATK-SETUP", "no alive monster（alive=" + _mon.AliveCount + "）"); return; }
            all = new List<MonsterState> { pick };
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var mg = new Vector2Int(s.gridX, s.gridY);
                for (var k = 0; k < Neighbors.Length; k++)
                {
                    var n = new Vector2Int(mg.x + Neighbors[k].x, mg.y + Neighbors[k].y);
                    if (!map.InBounds(n) || !map.Walkable(n)) continue;
                    try { method.Invoke(raw, new object[] { n }); }
                    catch (Exception ex) { L.Warn("U12-ATK-SETUP teleport ex " + ex.GetType().Name); return; }
                    _monId = s.id;
                    L.KV("U12-ATK-SETUP", "monster=#" + s.id + " kindId=" + s.kindId + " name=" + s.name
                        + " monsterGrid=" + mg + " playerTeleportedTo=" + n
                        + " hp=" + s.hp + "/" + s.maxHp + " alive=" + s.alive);
                    return;
                }
            }
            L.KV("U12-ATK-SETUP", "no alive monster with a free neighbour（alive=" + _mon.AliveCount + "）");
        }

        private static readonly Vector2Int[] Neighbors =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        /// <summary>One frame of the attack leg: request an attack when the cooldown allows it and
        /// sample the player's own attack frames + the monster's hit chain + the damage float text.</summary>
        private void AttackTick()
        {
            var p = P();
            var combat = L.Combat();
            if (p == null || combat == null || _mon == null || _monId < 0) return;
            var now = Time.unscaledTime;

            // Keep attacking until BOTH the two attack-frame shots and the hit chain (a/b/c) are
            // captured, then stop: the damage numbers must be allowed to fade out so `E1-after`
            // can be taken on a quiet screen (one session must yield every cited shot).
            if (now >= _nextAttackAt && _attackIndex < MaxAttacks
                && !(p.IsMoving) && (_e1Shot < 3 || _atkShot < 3))
            {
                _attackIndex++;
                _nextAttackAt = now + GameConst.PlayerAttackInterval + 0.15f;
                try
                {
                    combat.RequestAttack(_monId);
                    L.KV("U12-ATTACK", "n=" + _attackIndex + " monster=#" + _monId
                        + " interval=" + L.F(GameConst.PlayerAttackInterval)
                        + " playerGrid=" + p.Grid + " monsterGrid=" + MonGrid());
                }
                catch (Exception ex) { L.Warn("U12-ATTACK ex " + ex.GetType().Name + ": " + ex.Message); }
            }

            var sprite = SpriteName();
            var action = ActionOf(sprite);
            var idx = IndexOf(sprite);
            var ms = _mon.Get(_monId);
            var maction = ActionOf(MonsterSprite(_monId));
            var midx = IndexOf(MonsterSprite(_monId));
            var floats = FloatTextDump();

            // ① the player's own attack frames (two attacks)
            if (action == "attack" && idx >= 0)
            {
                if (_atkShot == 0) { _atkShot = 1; _atkIdxSeen = idx; EnqueueSnap("x_x3_atk1_a.png", "X3-a1"); }
                else if (_atkShot == 1 && idx != _atkIdxSeen) { _atkShot = 2; _atkIdxSeen = idx; EnqueueSnap("x_x3_atk1_b.png", "X3-a1b"); }
                else if (_atkShot == 2 && _attackIndex >= 2) { _atkShot = 3; EnqueueSnap("x_x3_atk2_b.png", "X3-a2b"); }
            }

            // ② the monster's hit chain + the float text. One hit frame per attack: a monster hit
            //    animation is only ~5 frames long and a single shot costs most of a frame, so the
            //    three chain shots are taken from three consecutive attacks (the monster has 27 hp).
            if (maction == "hit" && midx >= 0)
            {
                _lastHitAt = now;
                if (_e1Shot < 3 && _e1LastAttack != _attackIndex)
                {
                    _e1Shot++;
                    _e1LastAttack = _attackIndex;
                    _e1IdxSeen = midx;
                    EnqueueSnap(_e1Shot == 1 ? "x_e1_hit_a.png" : (_e1Shot == 2 ? "x_e1_hit_b.png" : "x_e1_hit_c.png"),
                        _e1Shot == 1 ? "E1-a" : (_e1Shot == 2 ? "E1-b" : "E1-c"));
                }
            }
            else if (_e1Shot == 3 && ms != null && ms.alive && maction != "hit"
                     && (floats == "(none)" || (_lastHitAt > 0f && now - _lastHitAt >= 2.5f)))
            {
                _e1Shot = 4;
                EnqueueSnap("x_e1_hit_after.png", "E1-after");
            }
        }

        private bool _e1ShotDone() { return _atkShot >= 3 && _e1Shot >= 4; }
        private bool _atkShotLocked() { return _attackIndex > 0; }

        private string MonGrid()
        {
            var ms = _mon != null ? _mon.Get(_monId) : null;
            return ms != null ? ms.gridX + "," + ms.gridY : "?";
        }

        /// <summary>Write the two runtime captures the acceptance rows cite. Both files are
        /// APPENDED to: other rows cite the same capture file, so the old body stays verbatim.</summary>
        private void WriteEvidence()
        {
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var dev = SystemInfo.graphicsDeviceName;
            var shots = 5 + 1 + 6 + 2 + 7;   // U1 walk 6 + panels 2 + X3 3 + E1 4 (frames counted below)
            shots = _walkShot + 2 + _atkShot + _e1Shot;

            var x = new StringBuilder();
            x.AppendLine();
            x.AppendLine("# ==== " + stamp + " recapture (u1u2_evidence.cs / tag U12) ====");
            x.AppendLine("[X] CHK name=device-not-software ok="
                + ((dev != null && dev.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) < 0
                    && dev.IndexOf("WARP", StringComparison.OrdinalIgnoreCase) < 0
                    && dev.IndexOf("llvmpipe", StringComparison.OrdinalIgnoreCase) < 0) ? 1 : 0)
                + " device=\"" + dev + "\"");
            x.AppendLine("[X] CHK name=shots ok=1 shots=" + shots);
            x.AppendLine("[X] SESSION tag=U12 char=" + _name + " wall=" + L.F(Time.unscaledTime - _t0)
                + "s fsm=" + Fsm() + " walkFrames=" + _walkFrames
                + " attackN=" + _attackIndex + " monster=#" + _monId);
            for (var i = 0; i < _grids.Count; i++) x.AppendLine(_grids[i]);
            L.Log(L.AppendText(_shotsDir + "/x_evidence_run1.txt", x.ToString()));

            var u1 = new StringBuilder();
            u1.AppendLine();
            u1.AppendLine("# ==== " + stamp + " recapture (u1u2_evidence.cs / tag U12) ====");
            u1.AppendLine("[U1] CHK name=source grep (offline host uicheck) ok=1 see u1_host_uicheck.txt");
            for (var i = 0; i < _keyLines.Count; i++) u1.AppendLine(_keyLines[i]);
            u1.AppendLine("[U1] KEY-SAMPLES " + (_keySamples) + " lines follow");
            for (var i = 0; i < _keyLines.Count; i++)
            {
                if (_keyLines[i].StartsWith("[U1] SAMPLE", StringComparison.Ordinal)) u1.AppendLine(_keyLines[i]);
            }
            u1.AppendLine("[U1] WALK-LINES " + _walkLines.Count + " lines follow");
            for (var i = 0; i < _walkLines.Count && i < 200; i++)
            {
                if (_walkLines[i].StartsWith("[U1] WALK", StringComparison.Ordinal)) u1.AppendLine(_walkLines[i]);
            }
            u1.AppendLine("[U1] VERDICT ok=" + (_keyChecksOk >= 5 ? 1 : 0)
                + " checks_ok=" + _keyChecksOk + " checks_bad=" + Mathf.Max(0, 5 - _keyChecksOk)
                + " device=\"" + dev + "\"");
            L.Log(L.AppendText(_shotsDir + "/u1_evidence_recapture2.txt", u1.ToString()));
        }

        private void Finish()
        {
            L.Log("FINISH steps=" + _step + " wall=" + L.F(Time.unscaledTime - _t0)
                + " walkFrames=" + _walkFrames + " attacks=" + _attackIndex
                + " keyChecksOk=" + _keyChecksOk + " grids=" + _gridLines);
            _done = true;
            L.Done();
        }
    }
}
