// =============================================================================
// p_entity_evidence.cs -- ONE Play session collecting the entity / animation
// evidence an offline host cannot produce:
//
//   leg A  (TOWN) : the five town NPCs -- which cell each stands on, whether that
//                   cell is walkable (not inside a wall or a fire), which way each
//                   one faces, whether the idle animation cursor advances, and how
//                   many frames it advances in a fixed window.
//   leg B  (FACING): eight real ground clicks, one per grid direction; the frame
//                   actually drawn (action + direction token) is sampled while the
//                   character moves, and compared with the direction it is walking.
//   leg C  (SHEET): contact sheets rendered from the PRODUCTION frame keys and the
//                   PRODUCTION loader (SpriteFrames.Keys / Resolve):
//                     idle/walk/attack/cast/hit/death/run = 5 classes x 8 directions;
//                     equip = the same five classes bare-handed vs the starting
//                     weapon set (Chars/<class>/equip/<code>);
//                     missile = the CelFiles in play x frames.
//   leg D  (MOOR) : the spawned monsters -- kind, sprite code, facing, the frame
//                   actually drawn, alive/dead.
//   leg E  (FEED) : production hover (real pointer over a monster) -> the screen-top
//                   enemy bar; a real attack click -> the swing frame and the
//                   monster's hit reaction; then the death chain (the corpse node
//                   must still exist afterwards).
//
//   Why Play: which frame reaches the screen, and the missile / hit / corpse
//   visuals, exist only with the real SpriteAnimator + real async sprite load.
//
//   Input route: engine seam Game.AttachInput(IInputManager) carries the pointer and
//   the button edges; the input-system device state is pushed too (same route as
//   movefeel), and the probe logs which one the game really read.
//
//   ASCII only. No production file is touched; the driver is compiled into its own
//   assembly by run_script and reaches internal members by reflection.
// =============================================================================
namespace PENT
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
    // Dir8 alias pinned to the engine enum (CloverEngine.Dir8).
    using Dir8 = CloverEngine.Dir8;

    /// <summary>Log / shot / reflection helpers.</summary>
    public static class L
    {
        internal const string Tag = "PENT";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[PENT] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string m) { Log("WARN " + m); }
        internal static void Paths(string p) { _done = p ?? string.Empty; Log("PATHS done=" + _done); }

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
                try { var t = asms[i].GetType(full, false); if (t != null) return t; }
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

        internal static void Emit<T>(string e, T a) { Game.Event.Emit<T>(e, a); }
        internal static void Emit(string e) { Game.Event.Emit(e); }

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

        /// <summary>Read an internal instance method (the view module Dump* helpers).</summary>
        internal static string CallInternal(object target, string method, object[] args)
        {
            if (target == null) return "(" + method + " no-target)";
            try
            {
                var m = target.GetType().GetMethod(method,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return "(" + method + " missing)";
                var r = m.Invoke(target, args);
                return r == null ? "(null)" : r.ToString();
            }
            catch (Exception ex) { return "(" + method + " ex " + ex.GetType().Name + ": " + ex.Message + ")"; }
        }

        /// <summary>Official per-direction frame count of a missile CelFile (internal table).</summary>
        internal static int MissileFrames(string celFile)
        {
            var t = FindType("Diablo2.Module.Skill.MissileFrameCounts");
            if (t == null) return -1;
            var m = t.GetMethod("Of", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return -1;
            try { return (int)m.Invoke(null, new object[] { celFile }); }
            catch { return -1; }
        }
    }

    /// <summary>Input manager carrying the pointer; button edges from ClickFrame.</summary>
    internal sealed class ScriptedInput : IInputManager
    {
        private readonly IInputManager _real;

        public Vector3 Pointer;

        /// <summary>Down edge on ClickFrame+1, held +1..+3, up on +4 (one frame each).</summary>
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

    /// <summary>Compile sentinel entry (run_script --entry).</summary>
    public static class Probe
    {
        public static string Ping()
        {
            return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000");
        }
    }

    /// <summary>Installer (run_script --entry).</summary>
    public static class PEntity
    {
        public static string Install(string spec)
        {
            var go = new GameObject("PEntityDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("PE-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>Contact-sheet cell: a renderer plus the production frame key it shows.</summary>
    internal sealed class Cell
    {
        public SpriteRenderer R;
        public string Key;
    }

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
        private bool _useSeam = true;
        private int _deviceDownFrame = -1;
        private Vector3 _clickScreen;

        // sheet state
        private Transform _sheetRoot;
        private readonly List<Cell> _cells = new List<Cell>();
        private readonly List<string> _sheetQueue = new List<string>();
        private string _sheetTag = string.Empty;
        private int _sheetWait;
        private int _sheetHold;
        private float _sheetScale = 1f;
        private Vector2 _sheetSpacing = Vector2.one;
        private int _sheetCols = 1;

        // facing leg
        private int _dwIndex;
        private int _dwPhase;      // 0 = choose+click, 1 = sampling
        private readonly List<string> _dwAim = new List<string>();
        private readonly List<string> _dwGot = new List<string>();
        private readonly List<string> _dwSkip = new List<string>();
        private string _dwFirstDir = string.Empty;
        private bool _dwMoving;
        private int _dwSamples;
        private int _dwIdxChanges;
        private int _dwGridChanges;
        private int _dwLastIdx = -1;
        private Vector2Int _dwLastGrid = new Vector2Int(-9999, -9999);
        private float _dwLegStart;
        private readonly Dictionary<string, int> _dwVotes = new Dictionary<string, int>();

        /// <summary>Direction token drawn on the largest number of moving frames of the last leg.</summary>
        private string DominantDir()
        {
            var best = string.Empty;
            var bestN = 0;
            foreach (var kv in _dwVotes)
            {
                if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
            }
            return best;
        }

        // monster legs
        private readonly List<string> _monSprites = new List<string>();
        private int _targetId = -1;
        private int _targetKind = -1;
        private int _clicks;
        private bool _sawHit;
        private bool _sawDeath;
        private bool _sawMonAttack;
        private bool _sawPlayerHit;
        private bool _missileSeen;
        private int _t2Id = -1;
        private int _t2Clicks;
        private bool _t2HitShot;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            L.Log("PE-INIT char=" + _name + " shots=" + _shotDir);
            try { Game.Event.On<HoverTarget>(Events.HoverTargetChanged, OnHoverChanged); }
            catch (Exception ex) { L.Warn("PE-INIT hover-subscribe " + ex.GetType().Name); }
        }

        /// <summary>Production hover payload (the same event the enemy bar consumes).</summary>
        private int _hoverEvents;

        private void OnHoverChanged(HoverTarget t)
        {
            _hoverEvents++;
            if (t == null) { L.KV("PE-HOVER-EVENT", "null"); return; }
            L.KV("PE-HOVER-EVENT", "hasTarget=" + t.hasTarget + " id=" + t.id + " cursor=" + t.cursor
                + " name=" + t.name + " (Events.HoverTargetChanged)");
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
            L.KV("PE-INPUT-SWAP", "AttachInput(ScriptedInput) real=" + _fake.BackendName);
        }

        private void PinPointer(Vector3 screen)
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
        }

        private void DeviceUpPending()
        {
            if (_deviceDownFrame <= 0 || Time.frameCount <= _deviceDownFrame + 1) return;
            IlPush(new Vector2(_clickScreen.x, _clickScreen.y), 0);
            _deviceDownFrame = -1;
        }

        // ---- input system device (reflection) -------------------------------

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
                if (isT == null || mouseT == null || stT == null) { L.KV("PE-IL", "types-missing"); return false; }
                var cur = mouseT.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                var dev = cur != null ? cur.GetValue(null) : null;
                if (dev == null)
                {
                    var add = isT.GetMethod("AddDevice", new Type[] { typeof(string) });
                    if (add != null) dev = add.Invoke(null, new object[] { "Mouse" });
                }
                if (dev == null) { L.KV("PE-IL", "no-mouse-device"); return false; }
                var ms = isT.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (var i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "QueueStateEvent" && ms[i].IsGenericMethodDefinition) { _ilQueue = ms[i]; break; }
                }
                if (_ilQueue == null) { L.KV("PE-IL", "no-QueueStateEvent"); return false; }
                _ilState = stT;
                _ilPos = stT.GetField("position");
                _ilDelta = stT.GetField("delta");
                _ilScroll = stT.GetField("scroll");
                _ilButtons = stT.GetField("buttons");
                if (_ilPos == null) { L.KV("PE-IL", "no-position-field"); return false; }
                _ilMouse = dev;
                _ilReady = true;
                L.KV("PE-IL", "ready device=" + dev.GetType().Name);
                return true;
            }
            catch (Exception ex) { L.KV("PE-IL", "init-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
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
            catch (Exception ex) { L.KV("PE-IL", "push-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        // ---- readings --------------------------------------------------------

        private static IPlayerModule P() { return L.PlayerRaw() as IPlayerModule; }

        private static string SpriteOf(int entityId)
        {
            var view = L.View();
            if (view == null) return "(no-view)";
            var go = view.GetView(entityId);
            if (go == null) return "(no-node)";
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return "(no-sprite)";
            return sr.sprite.name;
        }

        private static string PlayerSprite() { return SpriteOf(GameConst.PlayerEntityId); }

        private static int IndexOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return -1;
            var i = sprite.LastIndexOf('_');
            if (i < 0 || i + 1 >= sprite.Length) return -1;
            int n;
            return int.TryParse(sprite.Substring(i + 1), out n) ? n : -1;
        }

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

        private static string DirOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return string.Empty;
            var i = sprite.LastIndexOf('_');
            if (i <= 0) return string.Empty;
            var j = sprite.LastIndexOf('_', i - 1);
            if (j <= 0 || j + 1 >= i) return string.Empty;
            return sprite.Substring(j + 1, i - j - 1);
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

        // ---- main loop -------------------------------------------------------

        private void Update()
        {
            if (_done) return;
            if (Time.unscaledTime - _t0 > 600f) { L.Warn("WATCHDOG 600s -> finish"); _done = true; L.Done(); return; }
            try { DeviceUpPending(); Step(); }
            catch (Exception ex) { L.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message); Next(); }
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
                        if (!Elapsed(1.2f)) break;
                        L.Log("MENU fsm=" + Fsm());
                        L.Emit(Events.Fsm.TriggerNewGame);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { L.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
                    if (Elapsed(60f)) { L.Warn("menu 60s fsm=" + Fsm()); Next(); }
                    break;

                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect
                        || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(1.2f)) break;
                        L.KV("PE-CHARSELECT", "fsm=" + Fsm() + " emit " + Events.CharSelectRequest + "(" + _name + ")");
                        L.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { L.Warn("charselect 30s fsm=" + Fsm()); Next(); }
                    break;

                case 3:
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(4f)) break;
                        TownLeg();
                        Next();
                        break;
                    }
                    if (Elapsed(150f)) { L.Warn("stage 150s fsm=" + Fsm()); Next(); }
                    break;

                // pointer route probe (which of the two routes the game really reads)
                case 4:
                    if (!Elapsed(0.5f)) break;
                    ProbePointerRoute();
                    Next();
                    break;

                case 5:
                    FacingLeg();
                    break;

                case 6:
                    BuildSheets();
                    Next();
                    break;

                case 7:
                    DriveSheets();
                    break;

                case 8:
                    if (!Elapsed(0.8f)) break;
                    L.Emit(Events.ExitEntered, AreaId.BloodMoor);
                    L.KV("PE-MOOR-REQUEST", "emit " + Events.ExitEntered + "(BloodMoor)");
                    Next();
                    break;

                case 9:
                    {
                        var m = L.Map();
                        var mon = L.Monster();
                        if (m != null && m.Area == AreaId.BloodMoor && mon != null && mon.AliveCount > 0)
                        {
                            if (!Elapsed(3f)) break;
                            MonsterLeg();
                            Snap("p_entity_monsters");
                            Next();
                            break;
                        }
                        if (Elapsed(40f))
                        {
                            L.Warn("MOOR 40s area=" + (m != null ? m.Area.ToString() : "(no-map)")
                                + " alive=" + (mon != null ? mon.AliveCount : -1));
                            Next();
                        }
                        break;
                    }

                case 10:
                    if (!Elapsed(0.5f)) break;
                    StandNextToTarget();
                    Next();
                    break;

                // passive wait: the monster must reach its own attack branch (and shoot)
                case 11:
                    WaitMonAttack();
                    break;

                case 12:
                    HoverLeg();
                    break;

                case 13:
                    AttackLoop();
                    break;

                case 14:
                    DeathLeg();
                    break;

                // second target: the highest-hp kind survives several hits, so the
                // monster hit reaction (action token + knockback) can be photographed
                case 15:
                    Target2Leg();
                    break;

                case 16:
                    if (!Elapsed(0.8f)) break;
                    L.Log("FINISH steps=" + _step + " wall=" + L.F(Time.unscaledTime - _t0));
                    _done = true;
                    L.Done();
                    break;
            }
        }

        private void ProbePointerRoute()
        {
            IlPush(new Vector2(960f, 540f), 0);
            var mp = Game.Input != null ? Game.Input.MousePosition : new Vector3(-1f, -1f, 0f);
            var hit = Mathf.Abs(mp.x - 960f) < 2f && Mathf.Abs(mp.y - 540f) < 2f;
            _useSeam = true;   // the seam always carries the pointer; the device state is pushed too
            L.KV("PE-POINTER-ROUTE", "devicePushed=960,540 gameInput=" + L.F(mp.x) + "," + L.F(mp.y)
                + " deviceReachedGame=" + hit + " useSeam=" + _useSeam);
        }

        // ---- leg A: town NPCs ------------------------------------------------

        private static readonly NpcId[] NpcIds =
        {
            NpcId.Akara, NpcId.Kashya, NpcId.Charsi, NpcId.Gheed, NpcId.Warriv,
        };

        private void TownLeg()
        {
            var map = L.Map();
            var p = P();
            L.KV("PE-STAGE", "fsm=" + Fsm()
                + " area=" + (map != null ? map.Area.ToString() : "(no-map)")
                + " grid=" + (p != null ? p.Grid.ToString() : "(no-player)")
                + " dir=" + (p != null ? p.Dir.ToString() : "(no-player)")
                + " playerClass=" + (p != null ? p.Class.ToString() : "-")
                + " running=" + (p != null ? p.IsRunning.ToString() : "-")
                + " map=" + (map != null ? map.Width + "x" + map.Height : "-"));

            var view = L.View();
            if (view != null)
            {
                for (var i = 0; i < 2; i++) L.Log("PE-NPCDUMP " + L.CallInternal(view, "DumpNpcDebug", null));
                L.Log("PE-STATS " + L.CallInternal(view, "DumpStats", null));
            }

            var points = map != null ? map.NpcPoints : null;
            for (var i = 0; i < NpcIds.Length; i++)
            {
                var id = NpcIds[i];
                var code = Diablo2.Module.View.SpriteFrames.NpcSpriteCode(id);
                var g = points != null && i < points.Count ? points[i] : new Vector2Int(-9999, -9999);
                var walkable = map != null && g.x > -9999 && map.Walkable(g);
                var want = p != null && g.x > -9999 ? Iso.DirectionTo(g, p.Grid) : Dir8.S;
                var eid = -1 - (int)id;
                var go = view != null ? view.GetView(eid) : null;
                L.KV("PE-NPC", "id=" + id + " code=" + code + " cell=" + g.x + "," + g.y
                    + " cellWalkable=" + walkable + " facesPlayer=" + want
                    + " node=" + (go != null) + " drawn=" + SpriteOf(eid));
            }
            NpcAdvanceSample();
            Snap("p_entity_npc_town");
        }

        private void NpcAdvanceSample()
        {
            var a = new string[NpcIds.Length];
            for (var i = 0; i < NpcIds.Length; i++) a[i] = SpriteOf(-1 - (int)NpcIds[i]);
            _npcFirst = string.Join(" ", a);
        }

        private string _npcFirst = string.Empty;
        private string _npcSecond = string.Empty;

        // ---- leg B: facing while walking in eight grid directions ------------

        /// <summary>Grid deltas of the eight screen directions (see Iso.DirectionTo).</summary>
        private static readonly Vector2Int[] DirDelta =
        {
            new Vector2Int(1, 1),    // S
            new Vector2Int(0, 1),    // SW
            new Vector2Int(-1, 1),   // W
            new Vector2Int(-1, 0),   // NW
            new Vector2Int(-1, -1),  // N
            new Vector2Int(0, -1),   // NE
            new Vector2Int(1, -1),   // E
            new Vector2Int(1, 0),    // SE
        };

        private void FacingLeg()
        {
            if (_dwPhase == 1)
            {
                var p = P();
                var sprite = PlayerSprite();
                var idx = IndexOf(sprite);
                _dwSamples++;
                if (idx >= 0 && idx != _dwLastIdx) { _dwIdxChanges++; _dwLastIdx = idx; }
                if (p != null)
                {
                    if (p.Grid != _dwLastGrid) { _dwGridChanges++; _dwLastGrid = p.Grid; }
                    if (p.IsMoving)
                    {
                        _dwMoving = true;
                        var d = DirOf(sprite);
                        if (d.Length > 0 && _dwFirstDir.Length == 0) _dwFirstDir = ActionOf(sprite) + "/" + d;
                        if (d.Length > 0)
                        {
                            int n;
                            _dwVotes.TryGetValue(d, out n);
                            _dwVotes[d] = n + 1;
                        }
                    }
                }
                if ((_dwMoving && (p == null || !p.IsMoving)) || Elapsed(3.5f))
                {
                    var expected = ((Dir8)_dwIndex).ToString().ToLowerInvariant();
                    var got = DirOf(PlayerSprite());
                    var drawn = ActionOf(PlayerSprite()) + "/" + got;
                    _dwAim.Add(expected);
                    var fps = _dwSamples > 0 ? _dwIdxChanges / Mathf.Max(0.001f, Time.unscaledTime - _dwLegStart) : 0f;
                    var dom = DominantDir();
                    _dwGot.Add(dom);
                    var votes = new List<string>();
                    foreach (var kv in _dwVotes) votes.Add(kv.Key + ":" + kv.Value);
                    L.KV("PE-FACING", "walkDir=" + expected + " gridDelta=" + DirDelta[_dwIndex]
                        + " drawnDominant=" + dom + " drawnFirst=" + _dwFirstDir + " drawnLast=" + drawn
                        + " matchDominant=" + (string.Equals(dom, expected, StringComparison.OrdinalIgnoreCase))
                        + " votes=" + string.Join(" ", votes.ToArray())
                        + " frames=" + _dwSamples + " spriteChanges=" + _dwIdxChanges
                        + " gridChanges=" + _dwGridChanges
                        + " legSeconds=" + L.F(Time.unscaledTime - _dwLegStart)
                        + " spriteChangesPerSec=" + L.F(fps)
                        + " framesPerTile=" + L.F(_dwGridChanges > 0 ? (float)_dwIdxChanges / _dwGridChanges : 0f));
                    _dwIndex++;
                    _dwPhase = 0;
                    _at = Time.unscaledTime;
                    return;
                }
                return;
            }

            if (!Elapsed(0.7f) && _dwIndex > 0) return;
            if (_dwIndex >= 8)
            {
                L.KV("PE-FACING-SUMMARY", "aimed=" + string.Join(",", _dwAim.ToArray())
                    + " drawn=" + string.Join(",", _dwGot.ToArray())
                    + " skipped=" + _dwSkip.Count + " " + string.Join(";", _dwSkip.ToArray()));
                Next();
                return;
            }

            var map = L.Map();
            var pl = P();
            if (map == null || pl == null) { L.Warn("FACING no map/player"); Next(); return; }

            var d0 = DirDelta[_dwIndex];
            var cam = Cam();
            var target = new Vector2Int(-9999, -9999);
            var screen = Vector3.zero;
            var offScreen = 0;
            for (var r = 5; r >= 2 && target.x == -9999; r--)
            {
                for (var j = -1; j <= 1 && target.x == -9999; j++)
                {
                    for (var i2 = -1; i2 <= 1; i2++)
                    {
                        var g = new Vector2Int(pl.Grid.x + d0.x * r + i2, pl.Grid.y + d0.y * r + j);
                        if (!map.InBounds(g) || !map.Walkable(g)) continue;
                        if (Iso.DirectionTo(pl.Grid, g) != (Dir8)_dwIndex) continue;
                        var path = map.FindPath(pl.Grid, g);
                        if (path == null || path.Count < 2) continue;
                        // a click outside the window would project back to a cell the game never sees
                        var w0 = Iso.GridToWorld(g.x, g.y);
                        Vector3 s0;
                        if (!ScreenForGround(cam, new Vector2(w0.x, w0.y), out s0)) { offScreen++; continue; }
                        const float pad = 8f;
                        if (s0.x < pad || s0.y < pad
                            || s0.x > Screen.width - pad || s0.y > Screen.height - pad) { offScreen++; continue; }
                        target = g;
                        screen = s0;
                        break;
                    }
                }
            }
            if (target.x == -9999)
            {
                _dwSkip.Add(((Dir8)_dwIndex).ToString() + "(" + offScreen + " off-screen/off-window)");
                L.KV("PE-FACING-SKIP", "walkDir=" + (Dir8)_dwIndex + " reason=no-reachable-on-window-cell"
                    + " refusedOffWindow=" + offScreen + " window=" + Screen.width + "x" + Screen.height);
                _dwIndex++;
                return;
            }
            PinPointer(screen);
            ArmClick();
            _dwFirstDir = string.Empty;
            _dwMoving = false;
            _dwPhase = 1;
            _dwSamples = 0;
            _dwIdxChanges = 0;
            _dwGridChanges = 0;
            _dwLastIdx = -1;
            _dwLastGrid = pl.Grid;
            _dwVotes.Clear();
            _dwLegStart = Time.unscaledTime;
            _at = Time.unscaledTime;
            L.KV("PE-FACING-CLICK", "walkDir=" + (Dir8)_dwIndex + " from=" + pl.Grid + " to=" + target
                + " screen=" + L.F(screen.x) + "," + L.F(screen.y));
        }

        // ---- leg C: contact sheets ------------------------------------------

        private static readonly PlayerClass[] AllClasses =
        {
            PlayerClass.Amazon, PlayerClass.Sorceress, PlayerClass.Necromancer,
            PlayerClass.Paladin, PlayerClass.Barbarian,
        };

        private static readonly Diablo2.Module.View.ViewAnim[] AllActions =
        {
            Diablo2.Module.View.ViewAnim.Idle, Diablo2.Module.View.ViewAnim.Walk,
            Diablo2.Module.View.ViewAnim.Attack, Diablo2.Module.View.ViewAnim.Cast,
            Diablo2.Module.View.ViewAnim.Hit, Diablo2.Module.View.ViewAnim.Death,
            Diablo2.Module.View.ViewAnim.Run,
        };

        private static readonly Dir8[] AllDirs =
        {
            Dir8.S, Dir8.SW, Dir8.W, Dir8.NW, Dir8.N, Dir8.NE, Dir8.E, Dir8.SE,
        };

        /// <summary>Starting weapons whose equip frames are on disk (Chars/&lt;class&gt;/equip/&lt;code&gt;).</summary>
        private static readonly string[][] EquipPairs =
        {
            new[] { "Amazon", "jav" },
            new[] { "Barbarian", "hax" },
            new[] { "Necromancer", "wnd" },
            new[] { "Paladin", "ssd" },
            new[] { "Sorceress", "sst" },
        };

        private static readonly string[] MissileSet = { "SafeArrow", "Arrow", "SpikeFiendMissle", "Firebolt", "IceArrow" };

        /// <summary>Second NPC sample, taken after the facing leg (~30 s later).</summary>
        private void NpcAdvanceCheck()
        {
            var b = new string[NpcIds.Length];
            for (var i = 0; i < NpcIds.Length; i++) b[i] = SpriteOf(-1 - (int)NpcIds[i]);
            _npcSecond = string.Join(" ", b);
            var changed = 0;
            var a = _npcFirst.Split(' ');
            var c = _npcSecond.Split(' ');
            for (var i = 0; i < Math.Min(a.Length, c.Length); i++) if (a[i] != c[i]) changed++;
            L.KV("PE-NPC-FRAMES", "sampleA=" + _npcFirst + " | sampleB=" + _npcSecond
                + " changedSlots=" + changed + "/" + NpcIds.Length
                + " (TickNpcs advances the idle cursor; a frozen cursor means the NPC is a still image)");
        }

        private void BuildSheets()
        {
            NpcAdvanceCheck();
            var q = new[]
            {
                "idle", "walk", "attack", "cast", "hit", "death", "run", "equip", "missile",
                "mon_idle", "mon_walk", "mon_attack", "mon_hit", "mon_death",
            };
            for (var i = 0; i < q.Length; i++) _sheetQueue.Add(q[i]);
            L.KV("PE-SHEET-QUEUE", string.Join(",", _sheetQueue.ToArray()));
        }

        private void DriveSheets()
        {
            if (string.IsNullOrEmpty(_sheetTag))
            {
                if (_sheetHold > 0)
                {
                    _sheetHold--;
                    if (_sheetHold == 0) DestroySheets();   // after the capture of that frame landed
                    return;
                }
                StartNextSheet();
                return;
            }
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (c.R == null) continue;
                var s = Diablo2.Module.View.SpriteFrames.Resolve(c.Key);
                if (s != null) { c.R.sprite = s; c.R.color = Color.white; }
            }
            if (_sheetWait > 0) { _sheetWait--; return; }

            var missing = new List<string>();
            for (var i = 0; i < _cells.Count; i++)
            {
                if (_cells[i].R != null && _cells[i].R.sprite == null) missing.Add(_cells[i].Key);
            }
            Snap("p_entity_sheet_" + _sheetTag);
            L.KV("PE-SHEET-DONE", "tag=" + _sheetTag + " cells=" + _cells.Count
                + " unresolved=" + missing.Count
                + (missing.Count > 0 ? " first=" + string.Join(",", missing.GetRange(0, Math.Min(4, missing.Count)).ToArray()) : ""));
            _sheetTag = string.Empty;
            _sheetHold = 6;
        }

        private void StartNextSheet()
        {
            if (_sheetQueue.Count == 0) { _sheetTag = string.Empty; Next(); return; }
            var tag = _sheetQueue[0];
            _sheetQueue.RemoveAt(0);
            var p = P();
            var world = p != null ? p.World : Vector3.zero;
            var cam = Cam();
            var ortho = cam != null && cam.orthographic ? cam.orthographicSize : 8.5f;
            var aspect = cam != null ? cam.aspect : 1.7777f;
            if (tag == "missile") BuildMissileSheet(world, ortho, aspect);
            else if (tag == "equip") BuildEquipSheet(world, ortho, aspect);
            else if (tag.StartsWith("mon_", StringComparison.Ordinal)) BuildMonsterSheet(tag, world, ortho, aspect);
            else BuildActionSheet(tag, world, ortho, aspect);
            if (_sheetRoot == null) { _sheetTag = string.Empty; }
            else { _sheetTag = tag; _sheetWait = 70; }
        }

        private void SheetBegin(string tag, int rows, int cols, Vector3 world, float ortho, float aspect)
        {
            DestroySheets();
            var go = new GameObject("PESheet");
            _sheetRoot = go.transform;
            var availW = 2f * ortho * aspect * 0.96f;
            var availH = 2f * ortho * 0.92f;
            var sx = availW / Mathf.Max(1, cols);
            var sy = availH / Mathf.Max(1, rows);
            var baseH = 79f / 64f * 0.8f;      // native unit height at 80 px/unit, 64 ppu import
            var baseW = 40f / 64f * 0.8f;
            var k = Mathf.Min(sx * 0.80f / baseW, sy * 0.78f / baseH);
            _sheetRoot.position = new Vector3(world.x, world.y + 0.8f, -5f);
            _sheetScale = k;
            _sheetSpacing = new Vector2(sx, sy);
            _sheetCols = cols;
            L.KV("PE-SHEET-GEO", "tag=" + tag + " ortho=" + L.F(ortho) + " aspect=" + L.F(aspect)
                + " cols=" + cols + " rows=" + rows + " spacing=" + L.F(sx) + "x" + L.F(sy)
                + " magnify=" + L.F(k)
                + " (sheet magnified for direction reading; the in-game shots are 1:1)");
        }

        private void AddCell(string key, int row, int col)
        {
            if (_sheetRoot == null) return;
            var go = new GameObject("Cell_" + row + "_" + col);
            go.transform.SetParent(_sheetRoot, false);
            var x = (col - (_sheetCols - 1) * 0.5f) * _sheetSpacing.x;
            go.transform.localPosition = new Vector3(x, -(row * _sheetSpacing.y), 0f);
            go.transform.localScale = Vector3.one * (_sheetScale * Diablo2.Module.View.SpriteFrames.ArtScale);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = 32000;
            var s = Diablo2.Module.View.SpriteFrames.Resolve(key);
            sr.sprite = s;
            sr.color = s != null ? Color.white : new Color(1f, 0f, 1f, 0.30f);
            _cells.Add(new Cell { R = sr, Key = key });
        }

        private void BuildActionSheet(string tag, Vector3 world, float ortho, float aspect)
        {
            var animIdx = -1;
            for (var i = 0; i < AllActions.Length; i++)
            {
                if (AllActions[i].ToString().ToLowerInvariant() == tag) { animIdx = i; break; }
            }
            if (animIdx < 0) { L.Warn("SHEET-UNKNOWN-ACTION " + tag); return; }
            var anim = AllActions[animIdx];
            SheetBegin(tag, AllClasses.Length, AllDirs.Length, world, ortho, aspect);
            for (var r = 0; r < AllClasses.Length; r++)
            {
                for (var c = 0; c < AllDirs.Length; c++)
                {
                    var keys = Diablo2.Module.View.SpriteFrames.Keys(AllClasses[r], anim, AllDirs[c]);
                    if (keys == null || keys.Length == 0) continue;
                    AddCell(keys[0], r, c);
                }
            }
            L.KV("PE-SHEET-ROWS", "tag=" + tag
                + " rows=amazon,sorceress,necromancer,paladin,barbarian cols=s,sw,w,nw,n,ne,e,se"
                + " frame=frames[0] of that action (production SpriteFrames.Keys + Resolve)");
        }

        private void BuildEquipSheet(Vector3 world, float ortho, float aspect)
        {
            SheetBegin("equip", EquipPairs.Length * 2, AllDirs.Length, world, ortho, aspect);
            for (var r = 0; r < EquipPairs.Length; r++)
            {
                var cls = (PlayerClass)Enum.Parse(typeof(PlayerClass), EquipPairs[r][0], true);
                var code = EquipPairs[r][1];
                for (var c = 0; c < AllDirs.Length; c++)
                {
                    var bare = Diablo2.Module.View.SpriteFrames.Keys(cls, AllActions[1], AllDirs[c]);
                    if (bare != null && bare.Length > 0) AddCell(bare[0], r * 2, c);
                    var eq = Diablo2.Module.View.SpriteFrames.Keys(cls, code, AllActions[1], AllDirs[c]);
                    if (eq != null && eq.Length > 0) AddCell(eq[0], r * 2 + 1, c);
                }
            }
            L.KV("PE-SHEET-ROWS", "tag=equip rowPairs=amazon:jav,barbarian:hax,necromancer:wnd,paladin:ssd,sorceress:sst"
                + " oddRow=bare evenRow=equipped action=walk cols=s,sw,w,nw,n,ne,e,se");
        }

        /// <summary>The eight Act I monster kinds: sprite code (MonStats.Code) + display name.</summary>
        private static readonly string[][] MonsterKinds =
        {
            new[] { "fa", "Fallen" },
            new[] { "fs", "FallenShaman" },
            new[] { "si", "QuillRat" },
            new[] { "zm", "Zombie" },
            new[] { "cr", "DarkHunter" },
            new[] { "bk", "BloodHawk" },
            new[] { "ye", "GargantuanBeast" },
            new[] { "wr", "Ghost" },
        };

        private void BuildMonsterSheet(string tag, Vector3 world, float ortho, float aspect)
        {
            var actionName = tag.Substring(4);
            var animIdx = -1;
            for (var i = 0; i < AllActions.Length; i++)
            {
                if (AllActions[i].ToString().ToLowerInvariant() == actionName) { animIdx = i; break; }
            }
            if (animIdx < 0) { L.Warn("MONSTER-SHEET-UNKNOWN-ACTION " + tag); return; }
            var anim = AllActions[animIdx];
            SheetBegin(tag, MonsterKinds.Length, AllDirs.Length, world, ortho, aspect);
            for (var r = 0; r < MonsterKinds.Length; r++)
            {
                for (var c = 0; c < AllDirs.Length; c++)
                {
                    var keys = Diablo2.Module.View.SpriteFrames.Keys(MonsterKinds[r][0], anim, AllDirs[c]);
                    if (keys == null || keys.Length == 0) continue;
                    AddCell(keys[0], r, c);
                }
            }
            L.KV("PE-SHEET-ROWS", "tag=" + tag
                + " rows=fa(Fallen),fs(FallenShaman),si(QuillRat),zm(Zombie),cr(DarkHunter),bk(BloodHawk),"
                + "ye(GargantuanBeast),wr(Ghost) cols=s,sw,w,nw,n,ne,e,se frame=frames[0] (production keys)");
        }

        private void BuildMissileSheet(Vector3 world, float ortho, float aspect)
        {
            var counts = new int[MissileSet.Length];
            var maxF = 1;
            for (var i = 0; i < MissileSet.Length; i++)
            {
                var n = L.MissileFrames(MissileSet[i]);
                if (n < 1) n = 1;
                counts[i] = n;
                if (n > maxF) maxF = n;
            }
            SheetBegin("missile", MissileSet.Length, maxF, world, ortho, aspect);
            for (var r = 0; r < MissileSet.Length; r++)
            {
                for (var f = 0; f < counts[r]; f++)
                {
                    AddCell(ResPaths.MissileFrame(MissileSet[r], Dir8.E, f), r, f);
                }
            }
            var s = new string[counts.Length];
            for (var i = 0; i < counts.Length; i++) s[i] = counts[i].ToString();
            L.KV("PE-SHEET-ROWS", "tag=missile rows=" + string.Join(",", MissileSet)
                + " dir=e frames=" + string.Join(",", s)
                + " (counts from MissileFrameCounts; keys from ResPaths.MissileFrame)");
        }

        private void DestroySheets()
        {
            if (_sheetRoot != null) { UnityEngine.Object.Destroy(_sheetRoot.gameObject); _sheetRoot = null; }
            _cells.Clear();
            _sheetTag = string.Empty;
        }

        // ---- leg D: the moor and its monsters --------------------------------

        private void MonsterLeg()
        {
            var mon = L.Monster();
            var map = L.Map();
            var p = P();
            var all = mon != null ? mon.All : null;
            L.KV("PE-MOOR", "area=" + (map != null ? map.Area.ToString() : "-")
                + " alive=" + (mon != null ? mon.AliveCount : -1)
                + " total=" + (all != null ? all.Count : -1)
                + " playerGrid=" + (p != null ? p.Grid.ToString() : "-")
                + " map=" + (map != null ? map.Width + "x" + map.Height : "-"));
            if (all == null) return;
            for (var i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null) continue;
                var code = Diablo2.Module.View.SpriteFrames.SpriteCodeOf(st.kindId);
                var drawn = SpriteOf(st.id);
                L.KV("PE-MON", "id=" + st.id + " kind=" + st.kindId + " name=" + st.name + " sprite=" + code
                    + " cell=" + st.gridX + "," + st.gridY + " dir=" + st.dir + " alive=" + st.alive
                    + " hp=" + st.hp + "/" + st.maxHp + " drawn=" + drawn + " frameIdx=" + IndexOf(drawn));
                if (!_monSprites.Contains(code)) _monSprites.Add(code);
            }
            L.KV("PE-MON-CODES", "distinctSpriteCodes=" + _monSprites.Count
                + " list=" + string.Join(",", _monSprites.ToArray()));
        }

        private static readonly Vector2Int[] Neighbor8 =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
            new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        private void StandNextToTarget()
        {
            var mon = L.Monster();
            var map = L.Map();
            var raw = L.PlayerRaw();
            if (mon == null || map == null || raw == null) { L.Warn("STAND precondition"); return; }
            var method = raw.GetType().GetMethod("TeleportTo", new Type[] { typeof(Vector2Int) });
            if (method == null) { L.Warn("STAND TeleportTo missing"); return; }
            var all = mon.All;
            var best = -1;
            var bestNear = -1;
            var bestRange = -1;
            for (var i = 0; i < all.Count; i++)
            {
                var st = all[i];
                if (st == null || !st.alive) continue;
                var near = 0;
                for (var j = 0; j < all.Count; j++)
                {
                    var o = all[j];
                    if (o == null || !o.alive || o.id == st.id) continue;
                    if (o.kindId != st.kindId) continue;
                    if (Mathf.Max(Mathf.Abs(o.gridX - st.gridX), Mathf.Abs(o.gridY - st.gridY)) <= 2) near++;
                }
                // prefer a ranged kind (QuillRat / FallenShaman have MissA1/MissA2) so a
                // missile can be seen in flight, then the biggest cluster
                var ranged = st.ai == MonsterAI.Range || st.ai == MonsterAI.Shaman;
                var score = (ranged ? 1000 : 0) + near;
                if (score > bestRange) { bestRange = score; best = i; bestNear = near; }
            }
            if (best < 0) { L.Warn("STAND no alive monster"); return; }
            var pick = all[best];
            _targetId = pick.id;
            _targetKind = pick.kindId;
            var mg = new Vector2Int(pick.gridX, pick.gridY);
            var scoreTxt = bestRange;
            for (var k = 0; k < 8; k++)
            {
                var n = new Vector2Int(mg.x + Neighbor8[k].x, mg.y + Neighbor8[k].y);
                if (!map.InBounds(n) || !map.Walkable(n)) continue;
                try { method.Invoke(raw, new object[] { n }); }
                catch (Exception ex) { L.Warn("STAND teleport ex " + ex.GetType().Name); return; }
                L.KV("PE-TARGET", "m#" + pick.id + " kind=" + pick.kindId + " name=" + pick.name
                    + " ai=" + pick.ai
                    + " code=" + Diablo2.Module.View.SpriteFrames.SpriteCodeOf(pick.kindId)
                    + " monsterCell=" + mg + " playerTeleportedTo=" + n
                    + " sameKindNeighbours=" + bestNear + " score=" + scoreTxt);
                return;
            }
            L.Warn("STAND no free neighbour cell");
        }

        /// <summary>
        /// Stand still next to the ranged target and wait for the production AI to shoot:
        /// the attack frame, any missile in flight and the player's own hit reaction are
        /// only visible on the frames they reach the screen.
        /// </summary>
        private void WaitMonAttack()
        {
            var st = FindTarget(L.Monster());
            var drawn = SpriteOf(_targetId);
            var action = ActionOf(drawn);
            if (!_sawMonAttack && (action == "attack" || action == "cast" || (st != null && st.attacking)))
            {
                _sawMonAttack = true;
                _missileSeen = MissilePresent();
                L.KV("PE-MON-ATTACK", "m#" + _targetId + " kind=" + _targetKind + " drawn=" + drawn
                    + " action=" + action + " dir=" + DirOf(drawn) + " attacking=" + (st != null && st.attacking)
                    + " missileNode=" + _missileSeen
                    + " playerSprite=" + PlayerSprite() + " playerHp=" + PlayerHp());
                Snap("p_entity_mon_attack");
            }
            if (!_sawPlayerHit && ActionOf(PlayerSprite()) == "hit")
            {
                _sawPlayerHit = true;
                L.KV("PE-PLAYER-HIT", "playerSprite=" + PlayerSprite() + " playerHp=" + PlayerHp()
                    + " targetDrawn=" + drawn + " (damage float text is drawn by the engine in the same frame)");
                Snap("p_entity_player_hit");
            }
            if (Elapsed(16f))
            {
                L.KV("PE-MON-ATTACK-TIMEOUT", "m#" + _targetId + " attacking=" + (st != null && st.attacking)
                    + " drawn=" + drawn + " playerHp=" + PlayerHp() + " waitedSeconds=16");
                Snap("p_entity_mon_attack");
                Next();
            }
        }

        /// <summary>Any live projectile view in the scene (the Skill module self-draws them).</summary>
        private static bool MissilePresent()
        {
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (var i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.StartsWith("Projectile_", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>
        /// Second target = the alive monster with the highest hp (a zombie survives several
        /// hits) so the hit reaction can be read from the screen.
        /// </summary>
        private void Target2Leg()
        {
            var mon = L.Monster();
            var map = L.Map();
            var raw = L.PlayerRaw();
            var st = _t2Id >= 0 ? FindAlive(mon, _t2Id) : null;

            if (_t2HitShot && !_t2FloatShot)
            {
                if (Elapsed(0.35f))
                {
                    _t2FloatShot = true;
                    L.KV("PE-FLOATTEXT", "snapped 0.35s after the hit frame; playerSprite=" + PlayerSprite()
                        + " targetDrawn=" + SpriteOf(_t2Id)
                        + " (IViewModule.ShowFloatingText, GameConst.FloatTextDuration = 1.2s)");
                    Snap("p_entity_floattext");
                    _at = Time.unscaledTime;
                }
                return;
            }

            if (_t2HitShot && st != null && !st.alive)
            {
                if (Elapsed(2.5f))
                {
                    L.KV("PE-T2-END", "m#" + _t2Id + " died after " + _t2Clicks + " hits; corpse node="
                        + (L.View() != null && L.View().GetView(_t2Id) != null));
                    Next();
                }
                return;
            }

            if (st != null)
            {
                var drawn = SpriteOf(_t2Id);
                if (ActionOf(drawn) == "hit")
                {
                    _t2HitShot = true;
                    var node = L.View() != null ? L.View().GetView(_t2Id) : null;
                    var g = new Vector2Int(st.gridX, st.gridY);
                    var w = Iso.GridToWorld(g.x, g.y);
                    var off = node != null ? (node.transform.position - w) : Vector3.zero;
                    L.KV("PE-HIT-REACTION", "m#" + _t2Id + " kind=" + _targetKind + " drawn=" + drawn
                        + " action=hit frameIdx=" + IndexOf(drawn) + " dir=" + DirOf(drawn)
                        + " hp=" + st.hp + "/" + st.maxHp + " clicks=" + _t2Clicks
                        + " nodeOffsetFromCell=" + L.F(off.magnitude)
                        + " (ViewModule.HitKnockback = 0.06 world units + the Hit animation)"
                        + " playerSprite=" + PlayerSprite());
                    Snap("p_entity_hit");
                    _at = Time.unscaledTime;
                    return;
                }
                if (Elapsed(1.0f) && _t2Clicks < 6)
                {
                    _t2Clicks++;
                    var cam = Cam();
                    var w = Iso.GridToWorld(st.gridX, st.gridY);
                    Vector3 screen;
                    if (!ScreenForGround(cam, new Vector2(w.x, w.y), out screen)) { Next(); return; }
                    PinPointer(screen);
                    ArmClick();
                    L.KV("PE-T2-CLICK", "n=" + _t2Clicks + " on=m#" + _t2Id + " hp=" + st.hp + "/" + st.maxHp
                        + " playerSprite=" + PlayerSprite());
                    _at = Time.unscaledTime;
                }
                else if (Elapsed(8f))
                {
                    L.KV("PE-T2-TIMEOUT", "m#" + _t2Id + " drew " + drawn + " for 8s with no hit frame");
                    Next();
                }
                return;
            }

            if (mon == null || map == null || raw == null) { L.Warn("T2 precondition"); Next(); return; }
            var all = mon.All;
            var bestHp = -1;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive || s.id == _targetId) continue;
                if (s.hp > bestHp) { bestHp = s.hp; _t2Id = s.id; _targetKind = s.kindId; }
            }
            if (_t2Id < 0 || st == null && FindAlive(mon, _t2Id) == null) { L.KV("PE-T2", "no second target"); Next(); return; }
            var pick = FindAlive(mon, _t2Id);
            if (pick == null) { Next(); return; }
            var method = raw.GetType().GetMethod("TeleportTo", new Type[] { typeof(Vector2Int) });
            if (method == null) { Next(); return; }
            var mg = new Vector2Int(pick.gridX, pick.gridY);
            for (var k = 0; k < 8; k++)
            {
                var n = new Vector2Int(mg.x + Neighbor8[k].x, mg.y + Neighbor8[k].y);
                if (!map.InBounds(n) || !map.Walkable(n)) continue;
                try { method.Invoke(raw, new object[] { n }); }
                catch (Exception ex) { L.Warn("T2 teleport ex " + ex.GetType().Name); Next(); return; }
                L.KV("PE-T2", "m#" + _t2Id + " kind=" + _targetKind + " name=" + pick.name
                    + " code=" + Diablo2.Module.View.SpriteFrames.SpriteCodeOf(pick.kindId)
                    + " hp=" + pick.hp + "/" + pick.maxHp + " cell=" + mg + " playerAt=" + n);
                return;
            }
            L.KV("PE-T2", "no free neighbour cell");
            Next();
        }

        private static MonsterState FindAlive(IMonsterModule mon, int id)
        {
            if (mon == null || id < 0) return null;
            var all = mon.All;
            for (var i = 0; i < all.Count; i++) if (all[i] != null && all[i].id == id) return all[i];
            return null;
        }

        private void HoverLeg()
        {
            if (Elapsed(1.2f))
            {
                var sprite = SpriteOf(_targetId);
                L.KV("PE-HOVER-RESULT", "target=m#" + _targetId + " drawn=" + sprite
                    + " action=" + ActionOf(sprite) + " dir=" + DirOf(sprite)
                    + " pointer=" + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y)
                    + " hoverEvents=" + _hoverEvents + " hudOpen="
                    + (Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>()));
                Snap("p_entity_feedback_hover");
                Next();
                return;
            }
            var mon = L.Monster();
            var st = FindTarget(mon);
            if (st == null) { Next(); return; }
            var cam = Cam();
            var w = Iso.GridToWorld(st.gridX, st.gridY);
            Vector3 screen;
            if (!ScreenForGround(cam, new Vector2(w.x, w.y), out screen)) { Next(); return; }
            PinPointer(screen);
            if (!_hoverLogged)
            {
                _hoverLogged = true;
                L.KV("PE-HOVER-POINTER", "m#" + st.id + " cell=" + st.gridX + "," + st.gridY
                    + " screen=" + L.F(screen.x) + "," + L.F(screen.y) + " useSeam=" + _useSeam);
            }
        }

        private bool _hoverLogged;

        private void AttackLoop()
        {
            var mon = L.Monster();
            var st = FindTarget(mon);
            if (st == null || !st.alive)
            {
                L.KV("PE-TARGET-STATE", "gone-or-dead clicks=" + _clicks
                    + " state=" + (st == null ? "gone" : "dead"));
                Next();
                return;
            }
            var sprite = SpriteOf(_targetId);
            var action = ActionOf(sprite);
            if (action == "hit" && !_sawHit)
            {
                _sawHit = true;
                L.KV("PE-HIT-REACTION", "m#" + _targetId + " drawn=" + sprite + " frameIdx=" + IndexOf(sprite)
                    + " dir=" + DirOf(sprite) + " hp=" + st.hp + "/" + st.maxHp
                    + " playerSprite=" + PlayerSprite() + " playerAction=" + ActionOf(PlayerSprite()));
                Snap("p_entity_hit");
            }
            if ((st.attacking || action == "attack" || action == "cast") && !_sawMonAttack)
            {
                _sawMonAttack = true;
                L.KV("PE-MON-ATTACK", "m#" + _targetId + " kind=" + _targetKind + " drawn=" + sprite
                    + " action=" + action + " dir=" + DirOf(sprite)
                    + " playerSprite=" + PlayerSprite() + " playerHp=" + PlayerHp());
                Snap("p_entity_mon_attack");
            }
            if (Elapsed(1.0f))
            {
                if (_clicks >= 14) { Next(); return; }
                _clicks++;
                var cam = Cam();
                var w = Iso.GridToWorld(st.gridX, st.gridY);
                Vector3 screen;
                if (!ScreenForGround(cam, new Vector2(w.x, w.y), out screen)) { Next(); return; }
                PinPointer(screen);
                ArmClick();
                L.KV("PE-CLICK", "n=" + _clicks + " on=m#" + st.id + " hp=" + st.hp + "/" + st.maxHp
                    + " screen=" + L.F(screen.x) + "," + L.F(screen.y)
                    + " playerSprite=" + PlayerSprite() + " playerAction=" + ActionOf(PlayerSprite())
                    + " playerHp=" + PlayerHp());
                _at = Time.unscaledTime;
            }
        }

        private void DeathLeg()
        {
            var mon = L.Monster();
            var st = FindTarget(mon);
            if (_sawDeath)
            {
                if (Elapsed(3.5f))
                {
                    var view = L.View();
                    var go = view != null ? view.GetView(_targetId) : null;
                    var drawn = SpriteOf(_targetId);
                    L.KV("PE-CORPSE", "m#" + _targetId + " nodeStillThere=" + (go != null)
                        + " drawn=" + drawn + " action=" + ActionOf(drawn) + " frameIdx=" + IndexOf(drawn)
                        + " alive=" + (st != null && st.alive) + " afterDeathSeconds=3.5");
                    Snap("p_entity_corpse");
                    Next();
                }
                return;
            }
            if (st != null && !st.alive)
            {
                _sawDeath = true;
                var drawn = SpriteOf(_targetId);
                L.KV("PE-DEATH", "m#" + _targetId + " kind=" + _targetKind + " drawn=" + drawn
                    + " action=" + ActionOf(drawn) + " frameIdx=" + IndexOf(drawn)
                    + " node=" + (L.View() != null && L.View().GetView(_targetId) != null)
                    + " clicks=" + _clicks);
                Snap("p_entity_death");
                _at = Time.unscaledTime;
                return;
            }
            if (Elapsed(5f))
            {
                L.KV("PE-DEATH-UNREACHED", "m#" + _targetId + " hp=" + (st != null ? st.hp : -1) + "/"
                    + (st != null ? st.maxHp : -1) + " clicks=" + _clicks
                    + " (level-1 damage did not finish it inside this window)");
                Next();
            }
        }

        private MonsterState FindTarget(IMonsterModule mon)
        {
            if (mon == null || _targetId < 0) return null;
            var all = mon.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].id == _targetId) return all[i];
            }
            return null;
        }
    }
}
