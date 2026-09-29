// =============================================================================
// movefeel_evidence.cs -- ONE Play session that collects the per-frame movement /
//   animation readings an offline host cannot produce:
//
//     leg A (RUN) : one real left click on a far walkable cell, then sample EVERY
//                   frame until the character stands on it:
//                     - wall time, tile-space displacement per frame, tiles/second,
//                     - the sprite frame actually drawn (SpriteRenderer.sprite.name),
//                     - direction changes and animation advances,
//                   plus a mid-walk screenshot and an arrival screenshot.
//     leg B (WALK): switch to walk (IPlayerModule.SetRunning(false)) and repeat on a
//                   nearer cell -- the same two readings for the walk speed.
//     leg C (ATK) : stand next to a monster cluster and capture every attack edge
//                   (the A1 animation duration is what sets the cadence).
//     leg D (DEATH): the monsters of leg C keep hitting the player until the real
//                   death chain fires (Player.Kill -> PlayerDied -> the view plays DT),
//                   then sample EVERY frame while the death animation runs:
//                     - the sprite frame actually drawn (action/dir/frame index),
//                   - the wall time of the whole DT sequence and its effective fps,
//                   - the corpse hold (the last DT frame must stay on screen),
//                   - the production revive request (Events.ReviveRequest) and the
//                     idle sprite that follows it.
//                   Why Play: "which frame reaches the screen and how long it stays"
//                   only exists in a running session (real SpriteAnimator + async load).
//     leg D round 2 (diagnosis): the same death a second time on an otherwise hot session,
//                   with the cold-cost probes (DeathPanel first build / DT frame prefetch)
//                   measured separately and every long frame logged with its dt -- this is
//                   how the ~0.13s frame at the first death gets attributed.
//
//   Why Play: displacement vs drawn animation frame is a live reading; the drawn
//   frame is only produced by the real SpriteAnimator + async sprite load.
//
//   Input route: real input-system device state events (same reflection route as
//   d2tour/playver); if the device state does not reach Game.Input.MousePosition the
//   engine's public seam Game.AttachInput(IInputManager) carries the pointer while the
//   button edges stay device events (the reading says which route was used).
//   Production chain: InputReader.Poll -> PlayerModule.HandleMoveIntent
//     -> HandlePrimaryClick -> Events.MoveCommand -> PlayerModule.OnMoveCommand
//     -> A* -> PlayerMotor.Tick -> Events.PlayerGridChanged -> ViewModule.TickPlayer.
//
//   ASCII only. No production file is touched; the driver is compiled into its own
//   assembly by run_script and reaches module members by reflection.
// =============================================================================
namespace P2MF
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using CloverEngine;
    using Diablo2.Core;
    using Diablo2.Def;
    using Diablo2.Module;
    using UnityEngine;

    /// <summary>Log / reflection / screenshot helpers (self-contained probe).</summary>
    public static class L
    {
        internal const string Tag = "TOUR";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[TOUR] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string msg) { Log("WARN " + msg); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("TOUR-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var dir = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
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

        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }
        internal static IViewModule View() { return CtxMember("View") as IViewModule; }
        internal static IMonsterModule Monster() { return CtxMember("Monster") as IMonsterModule; }
        internal static object PlayerRaw() { return CtxMember("Player"); }

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

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
    }

    /// <summary>
    /// Input manager with the real behaviour except the reported pointer and the
    /// left button; every other member is forwarded to the live instance.
    /// </summary>
    internal sealed class ScriptedInput : IInputManager
    {
        private readonly IInputManager _real;

        public Vector3 Pointer;

        /// <summary>
        /// Frame in which the click was armed.  The button edges are derived from
        /// Time.frameCount so the down edge lands on exactly one frame no matter
        /// whether the module tick runs before or after the driver's Update:
        /// down on ClickFrame+1, held on +1..+3, up on +4.
        /// </summary>
        public int ClickFrame;

        public ScriptedInput(IInputManager real) { _real = real; }

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
    public static class MoveFeel
    {
        public static string Install(string spec)
        {
            var go = new GameObject("MoveFeelDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("MF-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>Two legs (run / walk) of one click-to-walk chain, sampled per frame.</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotDir = string.Empty;

        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _swapped;
        private ScriptedInput _fake;
        private bool _useSeam;
        private Vector3 _clickScreen;
        private int _deviceDownFrame = -1;
        private int _probePhase;

        private Vector2Int _grid = new Vector2Int(-9999, -9999);
        private readonly List<Vector2Int> _walk = new List<Vector2Int>();
        private Vector2Int _target;
        private float _legFrom;

        // per-frame leg readings
        private bool _sampling;
        private Vector2 _lastC;
        private bool _hasLastC;
        private int _nFrames;
        private float _sumTiles;
        private float _maxStep;
        private float _maxDt;
        private float _maxStepVsDt;
        private float _sumDt;
        private string _animPrefix = string.Empty;
        private int _animAdvances;
        private int _animIndexLast = -1;
        private string _dirLast = string.Empty;
        private string _spriteLast = string.Empty;
        private string _spriteFirst = string.Empty;
        private int _dirChanges;
        private readonly List<string> _sample = new List<string>();
        private float _speedAtLeg;
        private bool _legSeenMove;
        private string _legTag = string.Empty;
        private bool _midSnapped;

        // ── leg C: two consecutive monster attacks (the attack cadence is the one live reading
        //    the attack-timing change must produce: the A1 animation duration per monster) ──
        private IMonsterModule _mon;
        private readonly List<int> _monIds = new List<int>();
        private readonly Dictionary<int, List<float>> _atkTimes = new Dictionary<int, List<float>>();
        private readonly Dictionary<int, bool> _atkPrev = new Dictionary<int, bool>();
        private readonly Dictionary<int, List<string>> _atkSprites = new Dictionary<int, List<string>>();
        private readonly Dictionary<int, int> _atkKind = new Dictionary<int, int>();
        private readonly List<int> _atkOrder = new List<int>();
        private float _capUntil;
        private int _hpStart = -1;
        private int _hpMin = int.MaxValue;
        private int _setupId = -1;
        private int _setupKind = -1;
        private string _tl = string.Empty;
        private float _tlNext;
        private readonly Dictionary<int, string> _atkGrid = new Dictionary<int, string>();

        // ── leg D: the real death chain (monsters kill the player) ────────────────────
        /// <summary>The corpse must keep its last death frame for this long before the revive is asked for.</summary>
        private const float DeathHoldSeconds = 1.5f;

        /// <summary>Give up waiting for the death chain this long after leg C's attack cap.</summary>
        private const float DeathWaitExtraSeconds = 60f;

        /// <summary>How long to wait for the idle sprite after the revive request.</summary>
        private const float ReviveWaitSeconds = 6f;

        private bool _deathActive;
        private bool _deathHoldOk;
        private bool _reviveRequested;
        private bool _reviveSeen;
        private bool _deathLegDone;
        private float _deathStartAt;
        private float _deathLastChangeAt;
        private float _deathWaitUntil;
        private float _reviveAt;
        private int _deathFrames;
        private int _deathChanges;
        private int _reviveFrames;
        private int _deathLastIdx = -1;
        private int _deathOfficialFrames = -1;
        private int _deathFirstDtIdx = -1;
        private float _deathFirstDtAt;
        private float _deathLastDtAt;

        // ── 长帧归因（诊断）：冷启动成本探针 + 每帧 dt + 第 2 轮死亡（全暖基线） ──
        /// <summary>把长帧算作长帧的阈值（秒）。60fps 的正常帧是 0.0167 ⇒ 0.08 = 约 5 帧。</summary>
        private const float HitchSeconds = 0.08f;

        /// <summary>死亡屏在冷启动探针里留住多久（秒）——要跨过至少一次绘制，字模/DC6 才会真被光栅化。</summary>
        private const float DeathPanelHoldSeconds = 0.6f;

        private int _deathRound = 1;
        private int _roundHitches;
        private float _roundMaxDt;
        private bool _deathSnapDone;
        private bool _coldProbeDone;
        private int _coldProbePhase;
        private float _coldProbeAt;
        private float _coldFrameMax;
        private string _coldCost = "(not run)";
        private readonly HashSet<int> _deathLate = new HashSet<int>();
        private readonly List<string> _deathLateLog = new List<string>();
        private string _deathFirstSprite = string.Empty;
        private string _deathLastSprite = string.Empty;
        private string _reviveSprite = string.Empty;
        private readonly List<int> _deathSeq = new List<int>();
        private readonly List<string> _deathSample = new List<string>();

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            L.Log("MF-INIT char=" + _name + " shots=" + _shotDir);
            try
            {
                Game.Event.On<Vector2Int>(Events.PlayerGridChanged, OnGridChanged);
                L.Log("MF-SUBSCRIBE ok grid");
            }
            catch (Exception ex) { L.Warn("MF-SUBSCRIBE ex " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnGridChanged(Vector2Int g) { _grid = g; _walk.Add(g); }

        // ---- helpers ---------------------------------------------------------

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        private static bool Open<T>() where T : class, CloverEngine.IUIPanel
        {
            return Game.UI != null && Game.UI.IsOpen<T>();
        }

        private void Snap(string leaf)
        {
            if (string.IsNullOrEmpty(_shotDir)) { L.Warn("SNAP-NO-DIR " + leaf); return; }
            L.KV("SNAP " + leaf, L.Shot(_shotDir + "/" + leaf + ".png"));
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

        private void SwapInput()
        {
            if (_swapped) return;
            _fake = new ScriptedInput(Game.Input);
            Game.AttachInput(_fake);
            _swapped = true;
            L.KV("INPUT-SWAP", "Game.AttachInput(ScriptedInput) real backend=" + _fake.BackendName
                + " pointerOverUi=" + _fake.PointerOverUi);
        }

        private void PinWorld(Vector3 screen)
        {
            _clickScreen = screen;
            if (_useSeam) { SwapInput(); _fake.Pointer = screen; }
            IlPush(new Vector2(screen.x, screen.y), 0);
        }

        private void ArmClick()
        {
            if (_useSeam) { SwapInput(); _fake.ClickFrame = Time.frameCount; }
            IlPush(new Vector2(_clickScreen.x, _clickScreen.y), 1);
            _deviceDownFrame = Time.frameCount;
            L.KV("MF-ARM-CLICK", "frame=" + Time.frameCount
                + " pointer=" + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y)
                + " route=" + (_useSeam ? "attach-input-seam+device-buttons" : "input-system-device")
                + " pointerOverUi=" + (Game.Input != null ? Game.Input.PointerOverUi.ToString() : "(no-input)"));
        }

        private void DeviceUpPending()
        {
            if (_deviceDownFrame <= 0 || Time.frameCount <= _deviceDownFrame + 1) return;
            IlPush(new Vector2(_clickScreen.x, _clickScreen.y), 0);
            _deviceDownFrame = -1;
        }

        // ---- input system device (reflection, same route as playver) ---------

        private static bool _ilReady;
        private static object _ilMouse;
        private static MethodInfo _ilQueue;
        private static Type _ilState;
        private static FieldInfo _ilPos, _ilDelta, _ilScroll, _ilButtons;

        private static bool IlInit()
        {
            if (_ilReady) return true;
            try
            {
                var isT = L.FindType("UnityEngine.InputSystem.InputSystem");
                var mouseT = L.FindType("UnityEngine.InputSystem.Mouse");
                var stT = L.FindType("UnityEngine.InputSystem.LowLevel.MouseState");
                if (isT == null || mouseT == null || stT == null)
                {
                    L.KV("MF-IL", "types-missing inputSystem=" + (isT != null) + " mouse=" + (mouseT != null) + " state=" + (stT != null));
                    return false;
                }

                var cur = mouseT.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                var dev = cur != null ? cur.GetValue(null) : null;
                if (dev == null)
                {
                    var add = isT.GetMethod("AddDevice", new Type[] { typeof(string) });
                    if (add != null) dev = add.Invoke(null, new object[] { "Mouse" });
                    L.KV("MF-IL-ADDDEVICE", dev != null ? "created" : "failed");
                }
                if (dev == null) { L.KV("MF-IL", "no-mouse-device"); return false; }

                var ms = isT.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (var i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "QueueStateEvent" && ms[i].IsGenericMethodDefinition) { _ilQueue = ms[i]; break; }
                }
                if (_ilQueue == null) { L.KV("MF-IL", "no-QueueStateEvent"); return false; }

                _ilState = stT;
                _ilPos = stT.GetField("position");
                _ilDelta = stT.GetField("delta");
                _ilScroll = stT.GetField("scroll");
                _ilButtons = stT.GetField("buttons");
                if (_ilPos == null) { L.KV("MF-IL", "no-position-field"); return false; }

                _ilMouse = dev;
                _ilReady = true;
                L.KV("MF-IL", "ready device=" + dev.GetType().Name + " paramCount=" + _ilQueue.GetParameters().Length);
                return true;
            }
            catch (Exception ex) { L.KV("MF-IL", "init-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        private static bool IlPush(Vector2 pos, ushort buttons)
        {
            if (!_ilReady && !IlInit()) return false;
            try
            {
                var st = Activator.CreateInstance(_ilState);
                _ilPos.SetValue(st, pos);
                if (_ilDelta != null) _ilDelta.SetValue(st, Vector2.zero);
                if (_ilScroll != null) _ilScroll.SetValue(st, Vector2.zero);
                if (_ilButtons != null) _ilButtons.SetValue(st, buttons);
                var g = _ilQueue.MakeGenericMethod(_ilState);
                var ps = g.GetParameters();
                var args = ps.Length >= 3 ? new object[] { _ilMouse, st, -1.0 } : new object[] { _ilMouse, st };
                g.Invoke(null, args);
                return true;
            }
            catch (Exception ex) { L.KV("MF-IL", "push-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        // ---- player / view readings -----------------------------------------

        private static IPlayerModule P()
        {
            var raw = L.PlayerRaw();
            return raw as IPlayerModule;
        }

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

        /// <summary>Draw the animation frame index out of a frame key like run_s_3.</summary>
        private static int IndexOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return -1;
            var i = sprite.LastIndexOf('_');
            if (i < 0 || i + 1 >= sprite.Length) return -1;
            int n;
            return int.TryParse(sprite.Substring(i + 1), out n) ? n : -1;
        }

        private static string PrefixOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return string.Empty;
            var i = sprite.LastIndexOf('_');
            if (i <= 0) return sprite;
            var j = sprite.LastIndexOf('_', i - 1);
            if (j <= 0) return sprite;
            return sprite.Substring(0, j);
        }

        private sealed class Candidate
        {
            public Vector2Int Grid;
            public int Score;
        }

        /// <summary>Cells kept between the walk leg and any waypoint anchor (its panel would open and
        /// eat later ground clicks).</summary>
        private const int WaypointClearance = 4;

        private static bool NearAny(IReadOnlyList<Vector2Int> points, Vector2Int g, int radius)
        {
            if (points == null) return false;
            for (var i = 0; i < points.Count; i++)
            {
                var d = Mathf.Max(Mathf.Abs(points[i].x - g.x), Mathf.Abs(points[i].y - g.y));
                if (d <= radius) return true;
            }
            return false;
        }

        /// <summary>
        /// Walkable cells at Chebyshev distance in [minD, maxD] reachable by A* without stepping on
        /// an area exit, ordered by score = path cells x 100 - turns (walk the longest with the fewest
        /// turns); empty when none qualifies.
        /// </summary>
        private List<Candidate> PickTargets(IMapModule map, Vector2Int from, int minD, int maxD,
            bool respectWaypoints)
        {
            var list = new List<Candidate>();
            var w = map.Width;
            var h = map.Height;
            var waypoints = map.WaypointPoints;
            for (var dx = -maxD; dx <= maxD; dx++)
            {
                for (var dy = -maxD; dy <= maxD; dy++)
                {
                    var cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (cheb < minD || cheb > maxD) continue;
                    var g = new Vector2Int(from.x + dx, from.y + dy);
                    if (g.x < 0 || g.y < 0 || g.x >= w || g.y >= h) continue;
                    if (!map.Walkable(g)) continue;
                    if (respectWaypoints && waypoints != null && NearAny(waypoints, g, WaypointClearance)) continue;
                    var path = map.FindPath(from, g);
                    if (path == null || path.Count < minD) continue;
                    var turns = 0;
                    var exits = 0;
                    var nearWaypoint = false;
                    for (var i = 0; i < path.Count; i++)
                    {
                        if (map.TileAt(path[i]) == TileKind.Exit) exits++;
                        // the waypoint panel opens within a few cells of its anchor and would eat the
                        // ground click afterwards; keep both the path and the cell clear of it
                        if (respectWaypoints && waypoints != null
                            && NearAny(waypoints, path[i], WaypointClearance)) nearWaypoint = true;
                    }
                    if (nearWaypoint) continue;
                    for (var i = 1; i + 1 < path.Count; i++)
                    {
                        var a = path[i] - path[i - 1];
                        var b = path[i + 1] - path[i];
                        if (a.x != b.x || a.y != b.y) turns++;
                    }
                    if (exits > 0) continue;                  // an exit cell would swap the area mid-leg
                    list.Add(new Candidate { Grid = g, Score = path.Count * 100 - turns });
                }
            }
            list.Sort((a, b) => b.Score.CompareTo(a.Score));
            return list;
        }

        /// <summary>Read back the cell the armed click resolves to (the pointer -> ground -> cell chain
        /// the production code runs) and compare it with the intended cell.</summary>
        private void LogClickMapping(string tag)
        {
            var cam = Cam();
            if (cam == null) { L.KV("MF-" + tag + "-CLICK-MAP", "no-camera"); return; }
            var w = Iso.ScreenToWorldOnGround(cam, new Vector3(_clickScreen.x, _clickScreen.y, 0f));
            var g = Iso.WorldToGrid(w);
            L.KV("MF-" + tag + "-CLICK-MAP", "pointer=" + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y)
                + " groundWorld=" + L.F(w.x) + "," + L.F(w.y)
                + " cell=" + g.x + "," + g.y
                + " intended=" + _target.x + "," + _target.y
                + " match=" + (g.x == _target.x && g.y == _target.y));
        }

        /// <summary>Screen point for a cell, only when it lands inside the window (a click outside
        /// the window would project back to a cell the game never sees).</summary>
        private static bool ScreenOnWindow(Camera cam, Vector2Int g, out Vector3 screen)
        {
            screen = Vector3.zero;
            var w = Iso.GridToWorld(g.x, g.y);
            if (!ScreenForGround(cam, new Vector2(w.x, w.y), out screen)) return false;
            const float pad = 8f;
            return screen.x >= pad && screen.y >= pad
                && screen.x <= Screen.width - pad && screen.y <= Screen.height - pad;
        }

        private bool StartLeg(int minD, int maxD, string tag)
        {
            var map = L.Map();
            var p = P();
            if (map == null || p == null) { L.Warn("MF-" + tag + "-PRECOND map=" + (map != null) + " player=" + (p != null)); return false; }

            var cam = Cam();
            var relaxed = false;
            var cands = PickTargets(map, p.Grid, minD, maxD, true);
            if (cands.Count == 0)
            {
                relaxed = true;                       // every candidate sits near a waypoint anchor
                cands = PickTargets(map, p.Grid, minD, maxD, false);
            }
            L.KV("MF-" + tag + "-CANDIDATES", "n=" + cands.Count + " waypointFilterRelaxed=" + relaxed
                + " waypointPoints=" + (map.WaypointPoints != null ? map.WaypointPoints.Count : -1));
            var target = new Vector2Int(-9999, -9999);
            var screen = Vector3.zero;
            var refusedOffScreen = 0;
            for (var i = 0; i < cands.Count; i++)
            {
                Vector3 s;
                if (!ScreenOnWindow(cam, cands[i].Grid, out s)) { refusedOffScreen++; continue; }
                target = cands[i].Grid;
                screen = s;
                break;
            }
            if (target.x == -9999)
            {
                L.KV("MF-" + tag + "-TARGET", "none (candidates=" + cands.Count
                    + " refusedOffScreen=" + refusedOffScreen + " cam=" + (cam != null)
                    + " chebRange=[" + minD + "," + maxD + "] window=" + Screen.width + "x" + Screen.height + ")");
                return false;
            }
            _target = target;
            _legTag = tag;
            _legFrom = Time.unscaledTime;
            ResetLeg();
            _speedAtLeg = p.IsRunning
                ? GameConst.PlayerWalkSpeed
                : GameConst.PlayerWalkSpeed * GameConst.PlayerWalkSpeedFactor;

            var path = map.FindPath(p.Grid, target);
            L.KV("MF-" + tag + "-REQUEST", "from=" + p.Grid + " to=" + target
                + " pathCells=" + (path != null ? path.Count : -1)
                + " running=" + p.IsRunning
                + " contractSpeed=" + L.F(_speedAtLeg) + " tiles/s"
                + " screen=" + L.F(screen.x) + "," + L.F(screen.y));

            PinWorld(screen);
            _at = Time.unscaledTime;
            return true;
        }

        private void ResetLeg()
        {
            _sampling = true;
            _hasLastC = false;
            _nFrames = 0;
            _sumTiles = 0f;
            _maxStep = 0f;
            _maxDt = 0f;
            _maxStepVsDt = 0f;
            _sumDt = 0f;
            _animAdvances = 0;
            _animIndexLast = -1;
            _dirLast = string.Empty;
            _spriteLast = string.Empty;
            _spriteFirst = string.Empty;
            _dirChanges = 0;
            _animPrefix = string.Empty;
            _legSeenMove = false;
            _midSnapped = false;
            _sample.Clear();
        }

        /// <summary>One per-frame sample of the leg in progress.</summary>
        private void Sample()
        {
            if (!_sampling) return;
            var p = P();
            if (p == null) { _sampling = false; return; }

            var t = Time.unscaledTime;
            var dt = Time.unscaledDeltaTime;
            var w = p.World;
            var c = Iso.WorldToGridContinuous(w);
            var sprite = SpriteName();
            var dir = p.Dir.ToString();

            var step = 0f;
            var rate = 0f;
            if (_hasLastC && dt > 0f)
            {
                step = (c - _lastC).magnitude;
                rate = step / dt;
                _sumTiles += step;
                if (step > 1e-5f) _legSeenMove = true;
                if (step > _maxStep) _maxStep = step;
                var bound = _speedAtLeg * dt;
                if (bound > 1e-6f && step / bound > _maxStepVsDt) _maxStepVsDt = step / bound;
            }
            if (dt > _maxDt) _maxDt = dt;
            _sumDt += dt;
            _lastC = c;
            _hasLastC = true;
            _nFrames++;

            if (_nFrames == 24 && !_midSnapped)
            {
                _midSnapped = true;
                Snap("mf_" + _legTag + "_mid");     // the character is mid-walk here (the arrival shot can be covered by a panel)
            }

            if (string.IsNullOrEmpty(_spriteFirst)) _spriteFirst = sprite;
            var idx = IndexOf(sprite);
            var prefix = PrefixOf(sprite);
            var sameDir = dir == _dirLast;
            if (!string.IsNullOrEmpty(_dirLast) && !sameDir) _dirChanges++;
            if (sameDir && prefix == _animPrefix && idx >= 0 && _animIndexLast >= 0 && idx != _animIndexLast)
                _animAdvances++;
            if (!string.IsNullOrEmpty(prefix)) _animPrefix = prefix;
            _animIndexLast = idx;
            _dirLast = dir;
            _spriteLast = sprite;

            if (_sample.Count < 400)
            {
                _sample.Add("MF-FRAME n=" + _nFrames + " f=" + Time.frameCount + " t=" + L.F(t)
                    + " grid=" + p.Grid.x + "," + p.Grid.y
                    + " cell=" + L.F(c.x) + "," + L.F(c.y)
                    + " stepTiles=" + L.F(step) + " dt=" + L.F(dt) + " tilesPerSec=" + L.F(rate)
                    + " dir=" + dir + " sprite=" + sprite + " frameIdx=" + idx
                    + " moving=" + p.IsMoving);
            }
        }

        private void CloseLeg(string tag)
        {
            _sampling = false;
            var wall = Time.unscaledTime - _legFrom;
            var moved = _walk.Count > 0 ? "gridChanges=" + _walk.Count : "gridChanges=0";
            var avg = wall > 0f ? _sumTiles / wall : 0f;
            for (var i = 0; i < _sample.Count; i++) L.Log(_sample[i]);
            L.KV("MF-" + tag + "-SUMMARY",
                "frames=" + _nFrames + " wallSeconds=" + L.F(wall)
                + " sampledTiles=" + L.F(_sumTiles) + " avgTilesPerSec=" + L.F(avg)
                + " contractTilesPerSec=" + L.F(_speedAtLeg)
                + " maxFrameStepTiles=" + L.F(_maxStep) + " maxDt=" + L.F(_maxDt)
                + " maxStepOverSpeedDt=" + L.F(_maxStepVsDt)
                + " " + moved
                + " dirChanges=" + _dirChanges
                + " animAdvances=" + _animAdvances
                + " animAdvancesPerSec=" + L.F(wall > 0f ? _animAdvances / wall : 0f)
                + " animPrefix=" + _animPrefix
                + " firstSprite=" + _spriteFirst + " lastSprite=" + _spriteLast
                + " reached=" + (_grid == _target) + " endGrid=" + _grid + " target=" + _target);
        }

        // ---- main state machine ---------------------------------------------

        /// <summary>
        /// 同一帧里再采一次精灵。驱动 `Update` 早于视图 `Tick` 时，动作切换那一帧**贴上的新帧**
        /// （例如 DT 的第 0 帧）只在 `Update` 之后才可见 ⇒ 只靠 `Update` 采样会漏掉它，
        /// 于是 `DeathSample` 的序列看起来"从第 1 帧起"。这里把 `LateUpdate` 观察到的 DT 帧号也记下来。
        /// </summary>
        private void LateUpdate()
        {
            if (!_deathActive || _reviveRequested) return;
            var sprite = SpriteName();
            if (ActionOf(sprite) != "death") return;
            var idx = IndexOf(sprite);
            if (idx < 0 || !_deathLate.Add(idx)) return;
            if (_deathLateLog.Count < 40)
            {
                _deathLateLog.Add("MF-DEATH-LATE round=" + _deathRound + " f=" + Time.frameCount
                    + " t=" + L.F(Time.unscaledTime - _deathStartAt) + " sprite=" + sprite + " idx=" + idx);
            }
        }

        private void Update()
        {
            if (_done) return;
            if (Time.unscaledTime - _t0 > 600f) { L.Warn("WATCHDOG 600s -> finish"); _done = true; L.Done(); return; }
            try { DeviceUpPending(); Step(); }
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
                // 0) boot
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

                // 1) main menu -> new game
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

                // 2) char select -> load the save
                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect
                        || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(1.6f)) break;
                        L.KV("MF-CHARSELECT", "fsm=" + Fsm() + " emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        L.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { L.Warn("char select 30s fsm=" + Fsm()); Next(); }
                    break;

                // 3) first playable frame -> pointer route probe
                case 3:
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(2.5f)) break;
                        var map = L.Map();
                        var p = P();
                        L.KV("MF-STAGE", "fsm=" + Fsm()
                            + " area=" + (map != null ? map.Area.ToString() : "(no-map)")
                            + " grid=" + (p != null ? p.Grid.ToString() : "(no-player)")
                            + " running=" + (p != null ? p.IsRunning.ToString() : "(no-player)")
                            + " mapW=" + (map != null ? map.Width : -1)
                            + " mapH=" + (map != null ? map.Height : -1));
                        if (_probePhase == 0)
                        {
                            IlPush(new Vector2(960f, 540f), 0);
                            _probePhase = 1;
                            _at = Time.unscaledTime;
                            break;
                        }
                        var mp = Game.Input != null ? Game.Input.MousePosition : new Vector3(-1f, -1f, 0f);
                        var hit = Mathf.Abs(mp.x - 960f) < 2f && Mathf.Abs(mp.y - 540f) < 2f;
                        _useSeam = !hit;
                        L.KV("MF-POINTER-ROUTE", "devicePushed=960,540 gameInput=" + L.F(mp.x) + "," + L.F(mp.y)
                            + " match=" + hit + " useSeam=" + _useSeam);
                        _probePhase = 2;
                        Next();
                        break;
                    }
                    if (Elapsed(120f)) { L.Warn("stage 120s fsm=" + Fsm()); Next(); }
                    break;

                // 4) leg A: pick a far cell and click it
                case 4:
                    if (!Elapsed(0.5f)) break;
                    if (!StartLeg(3, 5, "LEGA-RUN")) { Next(); Next(); Next(); break; }
                    Next();
                    break;

                // 5) leg A: arm the click
                case 5:
                    if (!Elapsed(0.4f)) break;
                    ArmClick();
                    LogClickMapping("LEGA-RUN");
                    Next();
                    break;

                // 6) leg A: sample every frame until arrival
                case 6:
                    Sample();
                    if (P() == null) break;
                    if (P().IsMoving && !Elapsed(150f)) break;
                    if (!_legSeenMove && !Elapsed(8f)) break;    // the click may not have been consumed yet
                    Snap("mf_run_arrival");
                    CloseLeg("LEGA-RUN");
                    Next();
                    break;

                // 7) switch to walk
                case 7:
                    if (!Elapsed(1f)) break;
                    SetRunning(false);
                    L.KV("MF-WALK-SWITCH", "IPlayerModule.SetRunning(false) -> IsRunning="
                        + (P() != null ? P().IsRunning.ToString() : "(no-player)"));
                    Next();
                    break;

                // 8) leg B: pick a nearer cell and click it
                case 8:
                    if (!Elapsed(1f)) break;
                    if (!StartLeg(2, 4, "LEGB-WALK")) { Next(); Next(); Next(); break; }
                    Next();
                    break;

                // 9) leg B: arm the click
                case 9:
                    if (!Elapsed(0.4f)) break;
                    ArmClick();
                    LogClickMapping("LEGB-WALK");
                    Next();
                    break;

                // 10) leg B: sample every frame until arrival
                case 10:
                    Sample();
                    if (P() == null) break;
                    if (P().IsMoving && !Elapsed(180f)) break;
                    if (!_legSeenMove && !Elapsed(8f)) break;
                    Snap("mf_walk_arrival");
                    CloseLeg("LEGB-WALK");
                    Next();
                    break;

                // 11) restore run, then switch area on the documented production path
                //     (AppWaypoint does exactly this emit; AppFlow.EnterArea does the work)
                case 11:
                    if (!Elapsed(0.8f)) break;
                    SetRunning(true);
                    L.Emit(Events.ExitEntered, AreaId.BloodMoor);
                    L.KV("MF-MOOR-REQUEST", "emit " + Events.ExitEntered + "(BloodMoor)");
                    Next();
                    break;

                // 12) wait for the new area and its spawned monsters
                case 12:
                    var m0 = L.Map();
                    var mon0 = L.Monster();
                    if (m0 != null && m0.Area == AreaId.BloodMoor && mon0 != null && mon0.AliveCount > 0)
                    {
                        _mon = mon0;
                        L.KV("MF-MOOR-STAGE", "area=" + m0.Area + " alive=" + mon0.AliveCount
                            + " total=" + mon0.All.Count);
                        Next();
                        break;
                    }
                    if (Elapsed(30f))
                    {
                        L.Warn("MF-MOOR-STAGE 30s area=" + (m0 != null ? m0.Area.ToString() : "(no-map)")
                            + " alive=" + (mon0 != null ? mon0.AliveCount : -1));
                        Next();
                    }
                    break;

                // 13) stand next to one monster（先做冷启动成本探针：死亡屏**留住若干帧**，
                //     让它的 DC6 图 / 字模真的被画一次；每帧 dt 都记下来，看首画到底吃多少）
                case 13:
                    if (_coldProbePhase == 0)
                    {
                        if (!Elapsed(0.5f)) break;
                        ColdCostProbe();
                        _coldProbeAt = Time.unscaledTime;
                        _coldFrameMax = 0f;
                        _coldProbePhase = 1;
                        break;
                    }
                    if (_coldProbePhase == 1)
                    {
                        var dtp = Time.unscaledDeltaTime;
                        if (dtp > _coldFrameMax) _coldFrameMax = dtp;
                        L.KV("MF-COLD-FRAME", "dt=" + L.F(dtp) + " heldSeconds="
                            + L.F(Time.unscaledTime - _coldProbeAt) + " deathPanelOpen="
                            + (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.DeathPanel>()));
                        if (Time.unscaledTime - _coldProbeAt < DeathPanelHoldSeconds) break;
                        if (Game.UI != null) Game.UI.Close<Diablo2.UI.DeathPanel>();
                        _coldCost += " panelHeldMaxDt=" + L.F(_coldFrameMax);
                        L.KV("COLD-COST-DRAWN", "panelHeldSeconds=" + L.F(Time.unscaledTime - _coldProbeAt)
                            + " maxDtWhilePanelOpen=" + L.F(_coldFrameMax));
                        _coldProbePhase = 2;
                        MoveNextToMonster();
                        _legFrom = Time.unscaledTime;
                        _capUntil = Time.unscaledTime + 40f;   // long enough for the clustered monsters to kill the player
                        _deathWaitUntil = _capUntil + DeathWaitExtraSeconds;
                        Next();
                        break;
                    }
                    Next();                                    // 兜底（探针阶段不可重入）
                    break;

                // 14) capture every attack edge (the marker `MonsterAi.TryAttack` writes) and run
                //     leg D: the same pack keeps hitting the player until the real death chain fires
                case 14:
                    CapAttacks();
                    DeathSample();
                    if (_deathLegDone)
                    {
                        CloseLegC();
                        CloseLegD();
                        Next();
                        break;
                    }
                    if (Time.unscaledTime < _capUntil && EdgeCount() < 12) break;
                    if (Time.unscaledTime < _deathWaitUntil) break;   // still alive: stay in the pack
                    L.Warn("DEATH-LEG-UNREACHED alive=" + (P() != null && !P().IsDead)
                        + " deathActive=" + _deathActive + " edges=" + EdgeCount()
                        + " secondsInPack=" + L.F(Time.unscaledTime - _legFrom));
                    Snap("mf_moor_attacks");
                    CloseLegC();
                    CloseLegD();
                    Next();
                    break;

                // 15) 第 2 轮（长帧归因）：同一套 leg D 口径再死一次。本轮所有东西都已预热
                //     （第 1 轮死过一次 ⇒ 死亡屏与 DT 帧都热），且只在**死亡那一帧**截一张图
                //     ⇒ 与第 1 轮的 maxDt 相减就能分出"截图自身"占了多少。
                case 15:
                    if (!Elapsed(1.0f)) break;
                    ReArmDeathLeg(2);
                    MoveNextToMonster();
                    _legFrom = Time.unscaledTime;
                    _deathWaitUntil = Time.unscaledTime + 45f;
                    L.KV("DEATH-ROUND-START", "round=2 purpose=warm-baseline"
                        + " coldCost={" + _coldCost + "}");
                    _step = 16;
                    break;

                // 16) 第 2 轮：逐帧采样（与 leg D 共用 DeathSample）
                case 16:
                    DeathSample();
                    if (_deathLegDone) { CloseLegD(); _step = 17; break; }
                    if (Time.unscaledTime < _deathWaitUntil) break;
                    L.Warn("DEATH-ROUND-TIMEOUT round=2 alive=" + (P() != null && !P().IsDead));
                    CloseLegD();
                    _step = 17;
                    break;

                // 17) finish
                case 17:
                    if (!Elapsed(0.8f)) break;
                    L.Log("FINISH steps=" + _step + " wall=" + L.F(Time.unscaledTime - _t0));
                    _done = true;
                    L.Done();
                    break;
            }
        }

        // ---- leg C helpers ---------------------------------------------------

        /// <summary>Production attack interval of a monster kind, read by reflection (the class is
        /// internal to the game assembly; the driver compiles into its own).</summary>
        private static float ExpectedInterval(int kindId)
        {
            var t = L.FindType("Diablo2.Module.Monster.MonsterTuning");
            if (t == null) return -1f;
            var m = t.GetMethod("AttackIntervalSecondsOf", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return -1f;
            try { return (float)m.Invoke(null, new object[] { kindId }); }
            catch { return -1f; }
        }

        private static string SpriteOfMonster(int id)
        {
            var view = L.View();
            if (view == null) return "(no-view)";
            var go = view.GetView(id);
            if (go == null) return "(no-node)";
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return "(no-sprite)";
            return sr.sprite.name;
        }

        /// <summary>One frame of leg C: record the rising edges of every monster's <c>attacking</c> flag
        /// (the production marker written by `MonsterAi.TryAttack`) and the sprite that is drawn.</summary>
        private void CapAttacks()
        {
            if (_mon == null) return;
            var now = Time.unscaledTime;
            var all = _mon.All;
            if (all != null)
            {
                for (var i = 0; i < all.Count; i++)
                {
                    var s = all[i];
                    if (s == null) continue;
                    if (!_atkPrev.ContainsKey(s.id)) _atkPrev[s.id] = false;
                    var was = _atkPrev[s.id];
                    if (s.attacking && !was)
                    {
                        if (!_atkTimes.ContainsKey(s.id))
                        {
                            _atkTimes[s.id] = new List<float>();
                            _atkSprites[s.id] = new List<string>();
                            _atkKind[s.id] = s.kindId;
                            _atkOrder.Add(s.id);
                        }
                        if (_atkTimes[s.id].Count < 40) _atkTimes[s.id].Add(now - _legFrom);
                    }
                    _atkPrev[s.id] = s.attacking;
                    if (_atkSprites.ContainsKey(s.id))
                    {
                        var sp = SpriteOfMonster(s.id);
                        var list = _atkSprites[s.id];
                        if (list.Count == 0 || list[list.Count - 1] != sp) list.Add(sp);
                        _atkGrid[s.id] = s.gridX + "," + s.gridY + " alive=" + s.alive;
                        if (s.id == _setupId && now >= _tlNext)
                        {
                            _tlNext = now + 0.5f;          // a 0.5 s-resolution timeline of the flag
                            _tl += (s.attacking ? "1" : "0");
                        }
                    }
                }
            }
            var p = P();
            if (p != null)
            {
                var hp = PlayerHp();
                if (_hpStart < 0) { _hpStart = hp; _hpMin = hp; }
                if (hp < _hpMin) _hpMin = hp;
            }
        }

        private static int PlayerHp()
        {
            var raw = L.PlayerRaw();
            if (raw == null) return -1;
            var t = raw.GetType();
            var prop = t.GetProperty("Hp") ?? t.GetProperty("HpCurrent")
                       ?? t.GetProperty("Life") ?? t.GetProperty("LifeCurrent");
            if (prop == null) return -1;
            try { return (int)prop.GetValue(raw); } catch { return -1; }
        }

        private void CloseLegC()
        {
            var wall = Time.unscaledTime - _legFrom;
            var attacked = 0;
            for (var i = 0; i < _atkOrder.Count; i++)
            {
                var id = _atkOrder[i];
                var times = _atkTimes[id];
                var kind = _atkKind[id];
                var sprites0 = _atkSprites[id];
                var spriteSeq = string.Join("|", sprites0.GetRange(0, Math.Min(20, sprites0.Count)).ToArray());
                L.KV("MF-ATK-EDGES", "id=" + id + " kindId=" + kind + " edges=" + times.Count
                    + " expectedSeconds=" + L.F(ExpectedInterval(kind))
                    + " firstEdgeSeconds=" + L.F(times[0])
                    + " lastEdgeSeconds=" + L.F(times[times.Count - 1])
                    + " spriteChanges=" + sprites0.Count
                    + " sprites=" + spriteSeq
                    + " lastGrid=" + (_atkGrid.ContainsKey(id) ? _atkGrid[id] : "(none)"));
                if (times.Count < 2) continue;
                attacked++;
                var min = float.MaxValue;
                var max = 0f;
                var sum = 0f;
                var seq = string.Empty;
                for (var k = 1; k < times.Count; k++)
                {
                    var d = times[k] - times[k - 1];
                    if (d < min) min = d;
                    if (d > max) max = d;
                    sum += d;
                    seq += (k > 1 ? "," : "") + d.ToString("0.000");
                }
                var n = times.Count - 1;
                var sprites = _atkSprites[id];
                L.KV("MF-ATK-INTERVAL", "id=" + id + " kindId=" + kind
                    + " attacks=" + times.Count + " intervals=" + n
                    + " expectedSeconds=" + L.F(ExpectedInterval(kind))
                    + " meanSeconds=" + L.F(sum / n) + " minSeconds=" + L.F(min) + " maxSeconds=" + L.F(max)
                    + " sequenceSeconds=" + seq);
                L.KV("MF-ATK-SPRITES", "id=" + id + " distinct=" + sprites.Count
                    + " sequence=" + string.Join("|", sprites.ToArray()));
            }
            L.KV("MF-ATK-SUMMARY", "wallSeconds=" + L.F(wall) + " trackedMonsters=" + _atkOrder.Count
                + " monstersWithTwoAttacks=" + attacked
                + " setupMonster=#" + _setupId + " setupKindId=" + _setupKind
                + " setupExpectedSeconds=" + L.F(ExpectedInterval(_setupKind))
                + " playerHpStart=" + _hpStart + " playerHpMin=" + _hpMin);
            L.KV("MF-ATK-TIMELINE", "monster=#" + _setupId + " everyHalfSecond(1=attacking)=" + _tl);
        }

        // ---- leg D helpers ---------------------------------------------------

        /// <summary>Direction token of a drawn frame key: `death_sw_3` -> `sw`.</summary>
        private static string DirOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return string.Empty;
            var i = sprite.LastIndexOf('_');
            if (i <= 0) return string.Empty;
            var j = sprite.LastIndexOf('_', i - 1);
            if (j <= 0 || j + 1 >= i) return string.Empty;
            return sprite.Substring(j + 1, i - j - 1);
        }

        /// <summary>
        /// Action token of a drawn frame key: `death_s_0` -> `death`, and the equipment variants
        /// (`amazon/equip/jav/death_s_0`) resolve to the same token.
        /// </summary>
        private static string ActionOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return string.Empty;
            var i = sprite.LastIndexOf('_');            // frame index
            if (i <= 0) return string.Empty;
            var j = sprite.LastIndexOf('_', i - 1);     // direction
            if (j <= 0) return string.Empty;
            var k = sprite.LastIndexOf('_', j - 1);     // action
            return k < 0 ? sprite.Substring(0, j) : sprite.Substring(k + 1, j - k - 1);
        }

        /// <summary>Official DT frame count of the player class, from the same source the view plays from.</summary>
        private static int OfficialDeathFrames()
        {
            try
            {
                return Diablo2.Module.View.SpriteFrames.Keys(PlayerClass.Amazon,
                    Diablo2.Module.View.ViewAnim.Death, CloverEngine.Dir8.S).Length;
            }
            catch (Exception ex)
            {
                L.Warn("DEATH-OFFICIAL-FRAMES-EX " + ex.GetType().Name + ": " + ex.Message);
                return -1;
            }
        }

        /// <summary>Official DT playback fps of the player class (same source the view plays from).</summary>
        private static float OfficialDeathFps()
        {
            try
            {
                return Diablo2.Module.View.SpriteFrames.FpsOf("amazon", Diablo2.Module.View.ViewAnim.Death);
            }
            catch (Exception ex)
            {
                L.Warn("DEATH-OFFICIAL-FPS-EX " + ex.GetType().Name + ": " + ex.Message);
                return -1f;
            }
        }

        /// <summary>
        /// One frame of leg D.  Recording starts on the first frame that shows the real death chain
        /// (`IPlayerModule.IsDead` or a drawn DT frame), then:
        ///   ① the drawn sprite (action / dir / frame index) is recorded every frame;
        ///   ② once the DT sequence reached its official last frame **and** the sprite stopped
        ///      changing, the production revive request is emitted (`Events.ReviveRequest`, the
        ///      event the DeathPanel continue button emits);
        ///   ③ the idle sprite that follows the revive is recorded.
        /// </summary>
        private void DeathSample()
        {
            var p = P();
            if (p == null) return;
            var now = Time.unscaledTime;
            var sprite = SpriteName();
            var action = ActionOf(sprite);
            var idx = IndexOf(sprite);

            if (!_deathActive)
            {
                if (!p.IsDead && action != "death") return;
                _deathActive = true;
                _deathStartAt = now;
                _deathLastChangeAt = now;
                _deathFirstSprite = sprite;
                _deathLastSprite = sprite;
                _deathLastIdx = idx;
                if (_deathOfficialFrames < 0) _deathOfficialFrames = OfficialDeathFrames();
                L.KV("DEATH-FIRST-FRAME", "round=" + _deathRound + " sprite=" + sprite
                    + " action=" + action + " idx=" + idx
                    + " dead=" + p.IsDead + " hp=" + PlayerHp() + " playerDir=" + p.Dir);
            }

            _deathFrames++;
            // 截图自身会占用本帧（`ScreenCapture` 在帧末做一次读回 + PNG 编码，实测 0.12~0.14s），
            //   而一帧长到 0.12s 就会让 25fps 的 DT 一次跳过 3 帧 ⇒ 第 1 轮（任务 A 的取证轮）
            //   **动画期间一张都不截**（图由播完之后的 `mf_death_corpse` / `mf_death_revived` 提供）；
            //   第 2 轮反而**只在死亡那一帧**截一张，用来把"截图自身是多少"量出来。
            if (!_deathSnapDone && _deathRound == 2 && _deathFrames == 1)
            {
                _deathSnapDone = true;
                Snap("mf_death_r2_snap");
            }

            var dt = Time.unscaledDeltaTime;
            if (dt >= HitchSeconds)
            {
                _roundHitches++;
                if (dt > _roundMaxDt) _roundMaxDt = dt;
                L.KV("MF-HITCH", "round=" + _deathRound + " n=" + _deathFrames + " f=" + Time.frameCount
                    + " dt=" + L.F(dt) + " t=" + L.F(now - _deathStartAt)
                    + " prevSprite=" + _deathLastSprite + " sprite=" + sprite + " idx=" + idx
                    + " dead=" + p.IsDead
                    + " deathPanelOpen=" + (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.DeathPanel>()));
            }
            if (!string.Equals(sprite, _deathLastSprite, StringComparison.Ordinal))
            {
                _deathChanges++;
                _deathLastChangeAt = now;
                _deathLastSprite = sprite;
                _deathLastIdx = idx;
                if (action == "death" && idx >= 0
                    && (_deathSeq.Count == 0 || _deathSeq[_deathSeq.Count - 1] != idx))
                {
                    if (_deathSeq.Count == 0)
                    {
                        _deathFirstDtAt = now;
                        _deathFirstDtIdx = idx;
                    }
                    _deathSeq.Add(idx);
                    _deathLastDtAt = now;
                }
            }

            if (_deathSample.Count < 600)
            {
                _deathSample.Add("MF-DEATH-FRAME round=" + _deathRound + " n=" + _deathFrames
                    + " f=" + Time.frameCount + " t=" + L.F(now - _deathStartAt) + " dt=" + L.F(dt)
                    + " sprite=" + sprite + " action=" + action
                    + " idx=" + idx + " spriteDir=" + DirOf(sprite) + " playerDir=" + p.Dir
                    + " hp=" + PlayerHp() + " dead=" + p.IsDead);
            }

            if (_reviveRequested)
            {
                _reviveFrames++;
                if (!_reviveSeen && !p.IsDead && action == "idle")
                {
                    _reviveSeen = true;
                    _reviveSprite = sprite;
                    L.KV("DEATH-REVIVED-IDLE", "round=" + _deathRound + " sprite=" + sprite
                        + " idx=" + idx + " hp=" + PlayerHp()
                        + " afterSeconds=" + L.F(now - _reviveAt));
                    if (_deathRound == 1) Snap("mf_death_revived");
                }
                if (_reviveSeen) { _deathLegDone = true; }
                else if (now - _reviveAt >= ReviveWaitSeconds)
                {
                    L.Warn("DEATH-REVIVE-TIMEOUT afterSeconds=" + L.F(now - _reviveAt)
                        + " dead=" + p.IsDead + " sprite=" + sprite + " action=" + action);
                    _deathLegDone = true;
                }
                return;
            }

            // ② the corpse must hold its last frame (DT finished) before the revive is asked for.
            if (p.IsDead && _deathOfficialFrames > 0 && _deathLastIdx >= _deathOfficialFrames - 1
                && now - _deathLastDtAt >= DeathHoldSeconds)
            {
                _deathHoldOk = true;
                L.KV("DEATH-CORPSE-HOLD", "round=" + _deathRound + " sprite=" + _deathLastSprite
                    + " idx=" + _deathLastIdx
                    + " unchangedSeconds=" + L.F(now - _deathLastDtAt)
                    + " deathSeconds=" + L.F(now - _deathStartAt));
                if (_deathRound == 1) Snap("mf_death_corpse");
                _reviveRequested = true;
                _reviveAt = now;
                L.KV("DEATH-REVIVE-REQUEST", "round=" + _deathRound + " emit " + Events.ReviveRequest
                    + " (the event the DeathPanel continue button emits) dead=" + p.IsDead);
                L.Emit(Events.ReviveRequest);
            }
        }

        /// <summary>Leg D summary: the DT frame sequence, its span, the effective fps, the corpse hold
        /// and the revive outcome.  Emits the per-frame readings first.</summary>
        private void CloseLegD()
        {
            for (var i = 0; i < _deathSample.Count; i++) L.Log(_deathSample[i]);
            for (var i = 0; i < _deathLateLog.Count; i++) L.Log(_deathLateLog[i]);
            if (!_deathActive)
            {
                L.KV("DEATH-SUMMARY", "round=" + _deathRound
                    + " reached=false (no death chain observed in this session)");
                return;
            }
            var span = _deathSeq.Count > 0 ? _deathLastDtAt - _deathFirstDtAt : 0f;
            var late = new List<int>(_deathLate);
            late.Sort();
            var covered = new HashSet<int>(_deathSeq);
            covered.UnionWith(_deathLate);
            var allDtFrames = new List<int>(covered);
            allDtFrames.Sort();
            var fps = _deathSeq.Count > 1 && span > 0f ? (_deathSeq.Count - 1) / span : 0f;
            var officialFps = OfficialDeathFps();
            var hold = _reviveRequested ? _reviveAt - _deathLastDtAt : 0f;
            L.KV("DEATH-SUMMARY",
                "round=" + _deathRound
                + " reached=true framesRecorded=" + _deathFrames
                + " activeSeconds=" + L.F(Time.unscaledTime - _deathStartAt)
                + " spriteChanges=" + _deathChanges
                + " dtFrames=" + _deathSeq.Count + " dtSequence=" + string.Join(",", _deathSeq)
                + " dtLateSequence=" + string.Join(",", late)
                + " dtAllFrames=" + allDtFrames.Count + "/" + _deathOfficialFrames
                + " dtFirstIdx=" + _deathFirstDtIdx
                + " dtSpanSeconds=" + L.F(span) + " dtEffectiveFps=" + L.F(fps)
                + " officialDtFrames=" + _deathOfficialFrames + " officialDtFps=" + L.F(officialFps)
                + " officialDtSeconds=" + L.F(officialFps > 0f ? _deathOfficialFrames / officialFps : 0f)
                + " corpseHoldSeconds=" + L.F(hold) + " corpseHoldOk=" + _deathHoldOk
                + " reviveRequested=" + _reviveRequested + " reviveSeenIdle=" + _reviveSeen
                + " reviveSprite=" + _reviveSprite + " reviveFrames=" + _reviveFrames
                + " hitches=" + _roundHitches + " maxDtSeconds=" + L.F(_roundMaxDt)
                + " coldCost={" + _coldCost + "}"
                + " firstSprite=" + _deathFirstSprite + " lastSprite=" + _deathLastSprite);
        }

        /// <summary>
        /// 冷启动成本探针（诊断用；进怪堆之前跑一次，只跑一次）。两个数都取**首次**做的成本，
        /// 因此与"死亡那一帧为什么长"直接可比：
        ///   ① 死亡屏首次构建 —— `Game.UI.Open&lt;DeathPanel&gt;()` 同步跑完 `OnOpen`→`Build`（DC6 图 + 字模）；
        ///      ⚠️ 面板**不在这里关**：`Build` 只造控件，字模/贴图的首次光栅化发生在**绘制那一帧**
        ///      ⇒ 由 `case 13` 把它留住 <see cref="DeathPanelHoldSeconds"/> 秒并逐帧记 dt，才能把首画成本量到。
        ///   ② 死亡动作整组帧键的 `SpriteFrames.Prefetch`（发起 23 次 `Resources.LoadAsync`）的发起成本。
        /// ⚠️ 两个探针都会把对应的成本"用掉"（预热），所以第 1 轮死亡采到的是**扣掉这两项之后**的长帧。
        /// </summary>
        private void ColdCostProbe()
        {
            if (_coldProbeDone) return;
            _coldProbeDone = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var panelMs = -1.0;
            var prefetchMs = -1.0;
            try
            {
                if (Game.UI != null)
                {
                    var t0 = sw.Elapsed.TotalMilliseconds;
                    Game.UI.Open<Diablo2.UI.DeathPanel>();
                    panelMs = sw.Elapsed.TotalMilliseconds - t0;
                }
            }
            catch (Exception ex) { L.Warn("COLD-PROBE panel ex " + ex.GetType().Name + ": " + ex.Message); }
            try
            {
                var p = P();
                var dir = p != null ? p.Dir : CloverEngine.Dir8.S;
                var keys = Diablo2.Module.View.SpriteFrames.Keys(PlayerClass.Amazon,
                    Diablo2.Module.View.ViewAnim.Death, dir);
                var t1 = sw.Elapsed.TotalMilliseconds;
                Diablo2.Module.View.SpriteFrames.Prefetch(keys);
                prefetchMs = sw.Elapsed.TotalMilliseconds - t1;
            }
            catch (Exception ex) { L.Warn("COLD-PROBE prefetch ex " + ex.GetType().Name + ": " + ex.Message); }
            _coldCost = "panelOpenMs=" + L.F((float)panelMs) + " deathPrefetchMs=" + L.F((float)prefetchMs);
            L.KV("COLD-COST", _coldCost);
        }

        /// <summary>为第 <paramref name="round"/> 轮重置 leg D 的全部状态（同一个 DeathSample 口径复用）。</summary>
        private void ReArmDeathLeg(int round)
        {
            _deathRound = round;
            _deathActive = false;
            _deathHoldOk = false;
            _reviveRequested = false;
            _reviveSeen = false;
            _deathLegDone = false;
            _deathFrames = 0;
            _deathChanges = 0;
            _reviveFrames = 0;
            _deathLastIdx = -1;
            _deathFirstDtIdx = -1;
            _deathLastSprite = string.Empty;
            _deathFirstSprite = string.Empty;
            _reviveSprite = string.Empty;
            _roundHitches = 0;
            _roundMaxDt = 0f;
            _deathSnapDone = false;
            _deathLate.Clear();
            _deathLateLog.Clear();
            _deathSeq.Clear();
            _deathSample.Clear();
        }

        /// <summary>Teleport the player onto a walkable cell next to the first alive monster so that the
        /// monster reaches its attack branch on the next ticks. The teleport is driver setup (a live
        /// session would walk there); everything after it is the production AI.</summary>
        private void MoveNextToMonster()
        {
            _mon = L.Monster();
            var map = L.Map();
            var raw = L.PlayerRaw();
            if (_mon == null || map == null || raw == null)
            {
                L.Warn("MF-ATK-SETUP precondition mon=" + (_mon != null) + " map=" + (map != null)
                    + " player=" + (raw != null));
                return;
            }

            var method = raw.GetType().GetMethod("TeleportTo", new Type[] { typeof(Vector2Int) });
            if (method == null) { L.Warn("MF-ATK-SETUP TeleportTo missing"); return; }

            var all = _mon.All;
            //   Prefer a monster of a CLUSTER (>= 2 alive neighbours of the same kind within 2 cells):
            //   the "seconds for the monsters to kill the player" reading needs several attackers,
            //   and the spawner clusters a species together.
            var bestI = -1;
            var bestNear = -1;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var near = 0;
                for (var j = 0; j < all.Count; j++)
                {
                    var o = all[j];
                    if (o == null || !o.alive || o.id == s.id) continue;
                    if (o.kindId != s.kindId) continue;
                    var cheb = Mathf.Max(Mathf.Abs(o.gridX - s.gridX), Mathf.Abs(o.gridY - s.gridY));
                    if (cheb <= 2) near++;
                }
                if (near > bestNear) { bestNear = near; bestI = i; }
            }
            if (bestI >= 0)
            {
                var pick = all[bestI];
                all = new List<MonsterState> { pick };
                L.KV("MF-ATK-CLUSTER", "picked m#" + pick.id + " kindId=" + pick.kindId
                    + " sameKindNeighboursWithin2=" + bestNear);
            }
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var mg = new Vector2Int(s.gridX, s.gridY);
                for (var k = 0; k < 8; k++)
                {
                    var n = new Vector2Int(mg.x + Neighbor8[k].x, mg.y + Neighbor8[k].y);
                    if (!map.InBounds(n) || !map.Walkable(n)) continue;
                    try { method.Invoke(raw, new object[] { n }); }
                    catch (Exception ex) { L.Warn("MF-ATK-SETUP teleport ex " + ex.GetType().Name); return; }
                    _setupId = s.id;
                    _setupKind = s.kindId;
                    _tlNext = 0f;
                    L.KV("MF-ATK-SETUP", "monster=#" + s.id + " kindId=" + s.kindId + " name=" + s.name
                        + " monsterGrid=" + mg + " playerTeleportedTo=" + n
                        + " expectedIntervalSeconds=" + L.F(ExpectedInterval(s.kindId))
                        + " hp=" + s.hp + "/" + s.maxHp);
                    return;
                }
            }
            L.KV("MF-ATK-SETUP", "no alive monster with a free neighbour（alive=" + _mon.AliveCount + "）");
        }

        private static readonly Vector2Int[] Neighbor8 =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        private int EdgeCount()
        {
            var n = 0;
            for (var i = 0; i < _atkOrder.Count; i++)
            {
                var t = _atkTimes[_atkOrder[i]];
                if (t.Count >= 2) n++;
            }
            return n;
        }

        private void SetRunning(bool on)
        {
            var raw = L.PlayerRaw();
            if (raw == null) { L.Warn("SetRunning: Player null"); return; }
            try
            {
                var m = raw.GetType().GetMethod("SetRunning", BindingFlags.Public | BindingFlags.Instance);
                if (m == null) { L.Warn("SetRunning: method missing on " + raw.GetType().Name); return; }
                m.Invoke(raw, new object[] { on });
            }
            catch (Exception ex) { L.Warn("SetRunning ex " + ex.GetType().Name + ": " + ex.Message); }
        }
    }
}
