// =============================================================================
// playver_evidence.cs -- ONE Play session that collects the three live readings
//   an offline host cannot produce:
//
//   (1) bridge deck (Rogue Encampment, x 46..55 / y 25..28):
//       stand on the north walkable row (y=26), on the south row (y=27), and
//       walk along the deck (row change y26->y27, then west/east on y27).
//       Readings = screenshots + the production PlayerGridChanged walk log.
//   (2) NPC click (Akara, npcId 0, cell 10,10):
//       a) a left click on empty ground next to her must open NO dialog;
//       b) a left click pinned on her sprite rectangle (from
//          IViewModule.GetView(-1-npcId).GetComponent<SpriteRenderer>().bounds,
//          the same rect HoverPicker uses) must log the
//          "walk to her cell first, then talk" line and then the dialog line.
//   (3) the two panel close buttons (SkillTreePanel / QuestLogPanel):
//       node exists, art frame name, on-screen rect, the EventSystem raycast
//       chain at the graphic centre, the hover tip node (ControlTip) turning
//       active, and one click at that centre closing the panel.
//
//   Why a driver in Play mode: every reading above is a live-render / live-input
//   reading.  The world path is driven through the engine's public input seam
//   Game.AttachInput(IInputManager) (the shop-tooltip driver uses the same
//   seam): the driver implements the public interface, forwards every other
//   member to the real instance, and only overrides the reported pointer and
//   the left button.  The production chain therefore runs unchanged:
//     InputReader.Poll -> PlayerModule.HandleMoveIntent -> HandlePrimaryClick
//       -> Events.NpcInteractRequest / Events.MoveCommand.
//   The uGUI path is driven through the input system device (mouse position /
//   left button queued as device state), so the real EventSystem dispatches
//   pointerEnter / pointerClick; if that route is unavailable the driver falls
//   back to ExecuteEvents (the very call the input module makes) and says so in
//   the reading.
//
//   ASCII where possible; the few non-ASCII bytes are the two project strings
//   the driver has to compare against (the close tip text).
// =============================================================================
namespace P2PV
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using CloverEngine;
    using Diablo2.Core;
    using Diablo2.Def;
    using Diablo2.Module;
    using UnityEngine;
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

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
        internal static object NpcModule() { return CtxMember("Npc"); }
        internal static object ViewModule() { return CtxMember("View"); }

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
    /// left button.  Every other member is forwarded to the live instance.
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
    public static class PlayVer
    {
        public static string Install(string spec)
        {
            var go = new GameObject("PlayVerDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("PV-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain over the three live readings.</summary>
    public class Driver : MonoBehaviour
    {
        private const string BridgeRowNorth = "bridge_y26";
        private const string BridgeRowSouth = "bridge_y27";

        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _swapped;
        private ScriptedInput _fake;

        // live readings
        private Vector2Int _grid = new Vector2Int(-9999, -9999);
        private readonly List<Vector2Int> _walk = new List<Vector2Int>();
        private HoverTarget _hover = new HoverTarget { hasTarget = false, cursor = CursorKind.Default, id = -1 };
        private int _dialogCount;
        private string _dialogName = "(none)";
        private bool _useSeam;
        private Vector3 _clickScreen;
        private int _deviceDownFrame = -1;
        private int _probePhase;
        private int _npcReqCount;
        private int _npcReqLast = -1;
        private Vector2Int _pbGrid;
        private int _pbWalk;
        private int _clickTries;

        // step locals
        private Vector2Int _target;
        private float _waitFrom;
        private int _cand;
        private int _npcPhase;
        private readonly List<Vector3> _cands = new List<Vector3>();
        private string _route = string.Empty;
        private Rect _npcRect;
        private int _npcGridX = -1;
        private int _npcGridY = -1;
        private int _skillRays;
        private int _questRays;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            L.Log("PV-INIT char=" + _name + " shots=" + _shotDir);
            Subscribe();
        }

        private void Subscribe()
        {
            try
            {
                if (Game.Event == null) { L.Warn("PV-SUBSCRIBE Game.Event null"); return; }
                Game.Event.On<Vector2Int>(Events.PlayerGridChanged, OnGridChanged);
                Game.Event.On<HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
                Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen);
                Game.Event.On<int>(Events.NpcInteractRequest, OnNpcInteractRequest);
                L.Log("PV-SUBSCRIBE ok grid+hover+dialog+npcRequest");
            }
            catch (Exception ex) { L.Warn("PV-SUBSCRIBE ex " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void OnNpcInteractRequest(int npcId) { _npcReqCount++; _npcReqLast = npcId; }

        private void OnGridChanged(Vector2Int g) { _grid = g; _walk.Add(g); }
        private void OnHoverChanged(HoverTarget t) { if (t != null) _hover = t; }
        private void OnDialogOpen(NpcDialogArgs a)
        {
            _dialogCount++;
            _dialogName = a != null ? a.npcName : "(null-args)";
        }

        // ---- small helpers ---------------------------------------------------

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

        /// <summary>
        /// Screen point whose ground projection (Iso.ScreenToWorldOnGround) equals
        /// <paramref name="worldXy"/>: the mapping is affine for an orthographic
        /// camera, so it is inverted from three probes.
        /// </summary>
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

        /// <summary>
        /// Pin the pointer at a screen point.  The input system device is the
        /// first choice (a real device state event); the engine's public
        /// Game.AttachInput(IInputManager) seam is only used when the probe step
        /// showed the device position does not reach Game.Input.MousePosition.
        /// </summary>
        private void PinWorld(Vector3 screen)
        {
            _clickScreen = screen;
            if (_useSeam)
            {
                SwapInput();
                _fake.Pointer = screen;
            }
            IlPush(new Vector2(screen.x, screen.y), 0);
        }

        /// <summary>
        /// One real left click: the input system device carries the button edges
        /// (down now, up two frames later -- a uGUI click needs the two edges on
        /// different frames).  When the pointer had to fall back to the seam, the
        /// same edges are reported there as well.
        /// </summary>
        private void ArmClick()
        {
            if (_useSeam) { SwapInput(); _fake.ClickFrame = Time.frameCount; }
            IlPush(new Vector2(_clickScreen.x, _clickScreen.y), 1);
            _deviceDownFrame = Time.frameCount;
            L.KV("PV-ARM-CLICK", "frame=" + Time.frameCount
                + " pointer=" + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y)
                + " route=" + (_useSeam ? "attach-input-seam+device-buttons" : "input-system-device")
                + " pointerOverUi=" + (Game.Input != null ? Game.Input.PointerOverUi.ToString() : "(no-input)"));
        }

        /// <summary>Release the device button edge (a real uGUI click needs a later frame).</summary>
        private void DeviceUpPending()
        {
            if (_deviceDownFrame <= 0 || Time.frameCount <= _deviceDownFrame + 1) return;
            IlPush(new Vector2(_clickScreen.x, _clickScreen.y), 0);
            L.KV("PV-DEVICE-UP", "frame=" + Time.frameCount + " at="
                + L.F(_clickScreen.x) + "," + L.F(_clickScreen.y));
            _deviceDownFrame = -1;
        }

        // ---- input system device (uGUI route) --------------------------------

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
                    L.KV("PV-IL", "types-missing inputSystem=" + (isT != null) + " mouse=" + (mouseT != null) + " state=" + (stT != null));
                    return false;
                }

                var cur = mouseT.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                var dev = cur != null ? cur.GetValue(null) : null;
                if (dev == null)
                {
                    var add = isT.GetMethod("AddDevice", new Type[] { typeof(string) });
                    if (add != null) dev = add.Invoke(null, new object[] { "Mouse" });
                    L.KV("PV-IL-ADDDEVICE", dev != null ? "created" : "failed");
                }
                if (dev == null) { L.KV("PV-IL", "no-mouse-device"); return false; }

                var ms = isT.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (var i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name == "QueueStateEvent" && ms[i].IsGenericMethodDefinition) { _ilQueue = ms[i]; break; }
                }
                if (_ilQueue == null) { L.KV("PV-IL", "no-QueueStateEvent"); return false; }

                _ilState = stT;
                _ilPos = stT.GetField("position");
                _ilDelta = stT.GetField("delta");
                _ilScroll = stT.GetField("scroll");
                _ilButtons = stT.GetField("buttons");
                if (_ilPos == null) { L.KV("PV-IL", "no-position-field"); return false; }

                _ilMouse = dev;
                _ilReady = true;
                L.KV("PV-IL", "ready device=" + dev.GetType().Name + " current=" + dev.ToString()
                    + " paramCount=" + _ilQueue.GetParameters().Length);
                return true;
            }
            catch (Exception ex) { L.KV("PV-IL", "init-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
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
            catch (Exception ex) { L.KV("PV-IL", "push-ex " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        // ---- panel helpers ---------------------------------------------------

        private static Component FindPanel(string typeName)
        {
            var t = L.FindType(typeName);
            if (t == null) return null;
            var all = Resources.FindObjectsOfTypeAll(t);
            for (var i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c != null && c.gameObject.activeInHierarchy) return c;
            }
            return null;
        }

        private static Vector3 ScreenOfUi(RectTransform rt, Canvas canvas)
        {
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        private int RaycastChain(Component panel, string closeName, Vector2 screen, out string chain)
        {
            chain = string.Empty;
            var es = EventSystem.current;
            if (es == null) { chain = "(no EventSystem)"; return -1; }
            var ped = new PointerEventData(es);
            ped.position = screen;
            var list = new List<RaycastResult>();
            es.RaycastAll(ped, list);
            var idx = -1;
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < list.Count && i < 5; i++)
            {
                var go = list[i].gameObject;
                sb.Append(i).Append(":").Append(go.name).Append("<").Append(PathOf(go.transform, panel.transform)).Append("> ");
                if (go.name == closeName && idx < 0) idx = i;
            }
            chain = "n=" + list.Count + " first5=" + sb.ToString();
            return idx;
        }

        private static string PathOf(Transform t, Transform stop)
        {
            var s = t.name;
            var p = t.parent;
            var n = 0;
            while (p != null && p != stop && n < 4) { s = p.name + "/" + s; p = p.parent; n++; }
            return s;
        }

        private void ReadTip(Transform panelRoot, string tag)
        {
            var tip = panelRoot != null ? panelRoot.Find("ControlTip") : null;
            if (tip == null) { L.KV(tag, "tipNode=MISSING"); return; }
            var directChild = tip.parent == panelRoot;
            L.KV(tag, "activeSelf=" + tip.gameObject.activeSelf
                + " siblingIndex=" + tip.GetSiblingIndex()
                + " directChildOfPanelRoot=" + directChild
                + " anchored=" + L.F(((RectTransform)tip).anchoredPosition.x) + ","
                + L.F(((RectTransform)tip).anchoredPosition.y)
                + " label=[" + LabelUnder(tip) + "]");
        }

        /// <summary>
        /// The tip label is drawn by the project's own bitmap label class
        /// (Diablo2.UI.D2Label), not by a uGUI Text, so its live registry is read
        /// through reflection (same shim the contact-sheet probe uses).
        /// </summary>
        private static string LabelUnder(Transform root)
        {
            try
            {
                var t = L.FindType("Diablo2.UI.D2Label");
                if (t == null) return "(no-D2Label)";
                var f = t.GetField("Live", BindingFlags.NonPublic | BindingFlags.Static);
                var list = f != null ? f.GetValue(null) as IEnumerable : null;
                if (list == null) return "(no-Live)";
                var propText = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance);
                var propRoot = t.GetProperty("Root", BindingFlags.Public | BindingFlags.Instance);
                if (propText == null || propRoot == null) return "(no-props)";
                var sb = new System.Text.StringBuilder();
                foreach (var o in list)
                {
                    if (o == null) continue;
                    var r = propRoot.GetValue(o) as Transform;
                    if (r == null || !r.IsChildOf(root)) continue;
                    var txt = propText.GetValue(o) as string;
                    sb.Append('[').Append(txt).Append("]@").Append(PathOf(r, root)).Append(' ');
                }
                return sb.Length > 0 ? sb.ToString() : "(no-label-under-tip)";
            }
            catch (Exception ex) { return "(label-ex " + ex.GetType().Name + ")"; }
        }

        // ---- bridge ----------------------------------------------------------

        private void DumpBridgeRows()
        {
            var map = L.Map();
            if (map == null) { L.Warn("BRIDGE-DUMP no map"); return; }
            for (var y = 24; y <= 29; y++)
            {
                var sb = new System.Text.StringBuilder();
                for (var x = 45; x <= 56; x++)
                {
                    var g = new Vector2Int(x, y);
                    var w = map.Walkable(g);
                    var d = map.IsDeckGrid(g);
                    var k = map.TileAt(g);
                    sb.Append(x).Append('=').Append(k).Append(w ? "W" : "-").Append(d ? "D" : ".");
                    sb.Append(' ');
                }
                L.KV("BRIDGE-ROW y" + y, sb.ToString());
            }
        }

        private void WalkTo(int x, int y, float timeout)
        {
            _target = new Vector2Int(x, y);
            _waitFrom = Time.unscaledTime;
            L.KV("PV-WALK-REQ", "target=(" + x + "," + y + ") from=" + _grid + " timeout=" + L.F(timeout));
            L.Emit(Events.MoveCommand, _target);
        }

        // ---- Main state machine ---------------------------------------------

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
                        L.KV("PV-CHARSELECT", "fsm=" + Fsm() + " emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        L.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    if (Elapsed(30f)) { L.Warn("char select 30s fsm=" + Fsm()); Next(); }
                    break;

                // 3) first playable frame -> bridge rows dump
                case 3:
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(2.5f)) break;
                        var map = L.Map();
                        L.KV("PV-STAGE", "area=" + (map != null ? map.Area.ToString() : "(no-map)")
                            + " grid=" + _grid + " mapW=" + (map != null ? map.Width : -1)
                            + " mapH=" + (map != null ? map.Height : -1));
                        DumpBridgeRows();

                        // Pointer route probe: does an input system device state event
                        // reach Game.Input.MousePosition?  If yes the whole clip runs
                        // on real device input; if not the seam is used for the pointer
                        // while the button edges stay real device events.
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
                        L.KV("PV-POINTER-ROUTE", "devicePushed=960,540 gameInput=" + L.F(mp.x) + "," + L.F(mp.y)
                            + " match=" + hit + " pointerOverUi="
                            + (Game.Input != null ? Game.Input.PointerOverUi.ToString() : "(no-input)")
                            + " useSeam=" + _useSeam);
                        _probePhase = 2;
                        Next();
                        break;
                    }
                    if (Elapsed(120f)) { L.Warn("stage 120s fsm=" + Fsm()); Next(); }
                    break;

                // 4) walk to the bridge north row
                case 4:
                    if (Elapsed(0.5f)) { WalkTo(50, 26, 75f); Next(); }
                    break;

                // 5) north row reached -> screenshot
                case 5:
                    if (_grid == _target || Elapsed(75f))
                    {
                        L.KV("PV-BRIDGE-Y26", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target) + " walkSteps=" + _walk.Count);
                        Snap(BridgeRowNorth);
                        Next();
                    }
                    break;

                // 6) step one row south (row change on the deck)
                case 6:
                    if (Elapsed(0.8f)) { WalkTo(50, 27, 60f); Next(); }
                    break;

                // 7) south row reached -> screenshot
                case 7:
                    if (_grid == _target || Elapsed(60f))
                    {
                        L.KV("PV-BRIDGE-Y27", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target));
                        Snap(BridgeRowSouth);
                        Next();
                    }
                    break;

                // 8) walk east along the south row (lateral movement on the deck)
                case 8:
                    if (Elapsed(0.8f)) { WalkTo(54, 27, 60f); Next(); }
                    break;

                // 9) east end reached -> screenshot (lateral displacement)
                case 9:
                    if (_grid == _target || Elapsed(60f))
                    {
                        L.KV("PV-BRIDGE-EAST27", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target));
                        Snap("bridge_y27_east");
                        Next();
                    }
                    break;

                // 10) walk back west along the south row
                case 10:
                    if (Elapsed(0.8f)) { WalkTo(47, 27, 60f); Next(); }
                    break;

                // 11) west end reached -> dump the whole deck walk
                case 11:
                    if (_grid == _target || Elapsed(60f))
                    {
                        L.KV("PV-BRIDGE-WEST27", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target));
                        var sb = new System.Text.StringBuilder();
                        var start = Mathf.Max(0, _walk.Count - 24);
                        for (var i = start; i < _walk.Count; i++) sb.Append(_walk[i]).Append(' ');
                        L.KV("PV-WALK-LAST" + (start > 0 ? "24" : ""), sb.ToString());
                        Next();
                        break;
                    }
                    break;

                // 12) walk back to a staging cell far enough from Akara
                case 12:
                    {
                        if (!Elapsed(0.8f)) break;
                        var map = L.Map();
                        string how;
                        var st = StageCell(map, out how);
                        L.KV("PV-STAGE-PICK", "cell=" + st + " how=" + how);
                        WalkTo(st.x, st.y, 75f);
                        Next();
                        break;
                    }

                // 13) staging reached -> report
                case 13:
                    if (_grid == _target || Elapsed(75f))
                    {
                        L.KV("PV-STAGE-GRID", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target));
                        Next();
                    }
                    break;

                // 14) pass-by: click empty ground next to Akara (no dialog allowed)
                case 14:
                    {
                        if (!Elapsed(0.8f)) break;
                        var map = L.Map();
                        var cam = Cam();
                        if (map == null || cam == null) { L.Warn("PASSBY no map/cam"); Next(); break; }
                        L.KV("PV-AKARA-GRID", AkaraGrid().ToString());

                        string pickHow;
                        var cell = PassByCell(map, out pickHow);
                        if (cell.x < 0) { L.Warn("PASSBY no walkable cell near Akara"); Next(); break; }
                        var a = AkaraGrid();
                        L.KV("PV-PASSBY-CELL", "cell=" + cell + " how=" + pickHow
                            + " chebyshev=" + Mathf.Max(Mathf.Abs(cell.x - a.x), Mathf.Abs(cell.y - a.y))
                            + " walkable=" + map.Walkable(cell) + " tile=" + map.TileAt(cell));

                        var world = Iso.GridToWorld(cell);
                        var rect = NpcRect(0);
                        var inside = rect.HasValue && rect.Value.Contains(new Vector2(world.x, world.y));
                        Vector3 screen;
                        var okS = ScreenForGround(cam, new Vector2(world.x, world.y), out screen);
                        L.KV("PV-PASSBY-POINT", "cell=" + cell + " world=" + L.F(world.x) + "," + L.F(world.y)
                            + " inNpcRect=" + inside + " screenOk=" + okS
                            + " screen=" + L.F(screen.x) + "," + L.F(screen.y));
                        if (!okS) { Next(); break; }

                        PinWorld(screen);
                        _dialogCount = 0;
                        _npcReqCount = 0;
                        _npcReqLast = -1;
                        _pbGrid = _grid;
                        _pbWalk = _walk.Count;
                        _clickTries = 0;
                        L.KV("PV-PASSBY-HOVER", "hover.hasTarget=" + _hover.hasTarget
                            + " cursor=" + _hover.cursor + " id=" + _hover.id + " name=" + _hover.name
                            + " hoverGrid=" + _hover.gridX + "," + _hover.gridY);
                        ArmClick();
                        Next();
                        break;
                    }

                // 15) pass-by result: no dialog, and the click was consumed as a move
                case 15:
                    if (Elapsed(2.0f))
                    {
                        var delivered = _grid != _pbGrid || _walk.Count > _pbWalk;
                        L.KV("PV-PASSBY-RESULT", "dialogs=" + _dialogCount
                            + " dialogName=" + _dialogName
                            + " npcInteractRequests=" + _npcReqCount + " lastNpcId=" + _npcReqLast
                            + " grid=" + _grid + " gridBefore=" + _pbGrid
                            + " walkStepsBefore=" + _pbWalk + " walkSteps=" + _walk.Count
                            + " clickDeliveredAsGroundMove=" + delivered
                            + " verdict=" + (_dialogCount == 0 && _npcReqCount == 0 && delivered
                                ? "PASS(click-delivered-as-move,no-dialog,no-npc-request)"
                                : "FAIL(dialog/npc-request-opened-or-click-not-delivered)"));
                        Next();
                    }
                    break;

                // 16) walk back out of her talk range for the sprite-rect click
                case 16:
                    {
                        if (!Elapsed(0.8f)) break;
                        string how;
                        var st = StageCell(L.Map(), out how);
                        L.KV("PV-STAGE-PICK-2", "cell=" + st + " how=" + how);
                        WalkTo(st.x, st.y, 45f);
                        Next();
                        break;
                    }

                case 17:
                    if (_grid == _target || Elapsed(45f))
                    {
                        L.KV("PV-NPC-STAGE", "grid=" + _grid + " target=" + _target
                            + " reached=" + (_grid == _target));
                        Next();
                    }
                    break;

                // 18) build candidate screen points inside Akara's sprite rect
                case 18:
                    {
                        if (!Elapsed(0.5f)) break;
                        var cam = Cam();
                        var r = NpcRect(0);
                        if (cam == null || !r.HasValue) { L.Warn("NPC no cam/rect"); Next(); break; }
                        _npcRect = r.Value;
                        L.KV("PV-NPC-RECT", "x=" + L.F(_npcRect.x) + " y=" + L.F(_npcRect.y)
                            + " w=" + L.F(_npcRect.width) + " h=" + L.F(_npcRect.height));

                        // npcId 0 = Akara: her cell, for the "rect hit vs grid hit" split
                        var a = AkaraGrid();
                        _npcGridX = a.x;
                        _npcGridY = a.y;
                        L.KV("PV-NPC-DEF", "id=0 grid=(" + _npcGridX + "," + _npcGridY + ")");

                        // upper-body first: the rect's upper half, then the centre, then lower half
                        var c = _npcRect.center;
                        _cands.Clear();
                        _cands.Add(new Vector2(c.x, c.y + 0.30f * _npcRect.height));
                        _cands.Add(new Vector2(c.x, c.y + 0.15f * _npcRect.height));
                        _cands.Add(c);
                        _cands.Add(new Vector2(c.x, c.y - 0.30f * _npcRect.height));
                        _cands.Add(new Vector2(c.x - 0.25f * _npcRect.width, c.y + 0.20f * _npcRect.height));
                        _cands.Add(new Vector2(c.x + 0.25f * _npcRect.width, c.y + 0.20f * _npcRect.height));
                        _cand = 0;
                        Next();
                        break;
                    }

                // 19) pin a candidate, then (next pass) check that production hover
                //     resolves to her through the sprite rectangle
                case 19:
                    {
                        if (!Elapsed(0.9f)) break;
                        var cam = Cam();
                        if (cam == null) { L.Warn("NPC no camera"); Next(); break; }

                        if (_npcPhase == 0)
                        {
                            if (_cand >= _cands.Count)
                            {
                                L.Warn("NPC no candidate resolved to the NPC (tried " + _cands.Count + ")");
                                Next();
                                break;
                            }
                            var w = _cands[_cand];
                            Vector3 screen;
                            var okS = ScreenForGround(cam, w, out screen);
                            var g = okS ? Iso.ScreenToGrid(cam, screen) : new Vector2Int(-9999, -9999);
                            L.KV("PV-NPC-TRY", "i=" + _cand + " world=" + L.F(w.x) + "," + L.F(w.y)
                                + " inRect=" + _npcRect.Contains(w) + " screenOk=" + okS
                                + " screen=" + L.F(screen.x) + "," + L.F(screen.y) + " grid=" + g);
                            if (!okS) { _cand++; _at = Time.unscaledTime; break; }
                            PinWorld(screen);
                            _npcPhase = 1;
                            _at = Time.unscaledTime;
                            break;
                        }

                        if (_hover.cursor == CursorKind.Interact && _hover.id == 0)
                        {
                            L.KV("PV-NPC-HOVER", "PASS cursor=" + _hover.cursor + " id=" + _hover.id
                                + " name=" + _hover.name + " hoverGrid=" + _hover.gridX + "," + _hover.gridY
                                + " npcGrid=(" + _npcGridX + "," + _npcGridY + ")"
                                + " gridIsNpcCell=" + (_hover.gridX == _npcGridX && _hover.gridY == _npcGridY));
                            ArmClick();
                            Next();
                            break;
                        }
                        L.KV("PV-NPC-HOVER-TRY", "i=" + _cand + " notInteract cursor=" + _hover.cursor
                            + " hasTarget=" + _hover.hasTarget + " id=" + _hover.id + " name=" + _hover.name);
                        _cand++;
                        _npcPhase = 0;
                        _at = Time.unscaledTime;
                        break;
                    }

                // 20) click sent -> wait for the production log lines / dialog
                case 20:
                    if (!Elapsed(1.5f)) break;
                    if (_npcReqCount == 0 && _clickTries < 2)
                    {
                        _clickTries++;
                        L.KV("PV-NPC-CLICK-RETRY", "tries=" + _clickTries + " grid=" + _grid
                            + " hover=" + _hover.cursor + "/" + _hover.id);
                        ArmClick();
                        _at = Time.unscaledTime;
                        break;
                    }
                    L.KV("PV-NPC-CLICK-SENT", "grid=" + _grid + " dialogs=" + _dialogCount
                        + " npcInteractRequests=" + _npcReqCount + " lastNpcId=" + _npcReqLast
                        + " clickTries=" + _clickTries
                        + " hover=" + _hover.cursor + "/" + _hover.id);
                    Next();
                    break;

                // 21) dialog open (the walk-then-talk chain completed)
                case 21:
                    if (_dialogCount > 0 || Elapsed(45f))
                    {
                        L.KV("PV-NPC-DIALOG", "dialogs=" + _dialogCount + " name=" + _dialogName
                            + " npcInteractRequests=" + _npcReqCount + " lastNpcId=" + _npcReqLast
                            + " grid=" + _grid + " verdict=" + (_dialogCount > 0 && _npcReqCount > 0
                                ? "PASS" : "FAIL(no-dialog-or-no-request-45s)"));
                        Snap("npc_dialog");
                        Next();
                    }
                    break;

                // 22) close the dialog through the production option path (0 = close)
                case 22:
                    if (Elapsed(0.8f))
                    {
                        L.Log("PV-DIALOG-CLOSE emit " + Events.DialogOptionChosen + "(0)");
                        L.Emit(Events.DialogOptionChosen, 0);
                        Next();
                    }
                    break;

                case 23:
                    if (Elapsed(1.2f))
                    {
                        L.KV("PV-DIALOG-CLOSED", "npcDialogOpen=" + Open<Diablo2.UI.NpcDialogPanel>()
                            + " dialogsSoFar=" + _dialogCount);
                        Next();
                    }
                    break;

                // 24) open the skill tree through the same entry as the T key
                case 24:
                    if (Elapsed(0.5f))
                    {
                        L.Log("PV-OPEN SkillTreePanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "SkillTreePanel");
                        Next();
                    }
                    break;

                // 25) assert the close button + raycast at its graphic centre
                case 25:
                    if (Open<Diablo2.UI.SkillTreePanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.0f)) break;
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        if (panel == null) { L.Warn("SKILL panel instance null"); Next(); break; }
                        var close = panel.transform.Find("CloseButton");
                        if (close == null) { L.Warn("SKILL no CloseButton node"); Next(); break; }
                        var img = close.GetComponent<Image>();
                        var rt = close as RectTransform;
                        var canvas = close.GetComponentInParent<Canvas>();
                        var sp = ScreenOfUi(rt, canvas);
                        L.KV("PV-SKILL-CLOSE-NODE", "name=" + close.name + " active=" + close.gameObject.activeSelf
                            + " activeInHierarchy=" + close.gameObject.activeInHierarchy
                            + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "(null)")
                            + " anchored=" + L.F(rt.anchoredPosition.x) + "," + L.F(rt.anchoredPosition.y)
                            + " size=" + L.F(rt.sizeDelta.x) + "x" + L.F(rt.sizeDelta.y)
                            + " screen=" + L.F(sp.x) + "," + L.F(sp.y)
                            + " canvas=" + (canvas != null ? canvas.renderMode.ToString() : "(none)"));
                        ReadTip(panel.transform, "PV-SKILL-TIP-BEFORE");

                        var corner = new Vector3[4];
                        rt.GetWorldCorners(corner);
                        var cmin = new Vector2(float.MaxValue, float.MaxValue);
                        var cmax = new Vector2(float.MinValue, float.MinValue);
                        for (var i = 0; i < 4; i++)
                        {
                            var c2 = corner[i];
                            cmin = new Vector2(Mathf.Min(cmin.x, c2.x), Mathf.Min(cmin.y, c2.y));
                            cmax = new Vector2(Mathf.Max(cmax.x, c2.x), Mathf.Max(cmax.y, c2.y));
                        }
                        L.KV("PV-SKILL-CLOSE-WORLD-RECT", "min=" + L.F(cmin.x) + "," + L.F(cmin.y)
                            + " max=" + L.F(cmax.x) + "," + L.F(cmax.y)
                            + " screen=" + Screen.width + "x" + Screen.height
                            + " cornerCentre=" + L.F(sp.x) + "," + L.F(sp.y)
                            + " onScreen=" + (cmin.x >= 0f && cmin.y >= 0f && cmax.x <= Screen.width && cmax.y <= Screen.height));

                        string chain;
                        var idx = RaycastChain(panel, "CloseButton", sp, out chain);
                        _skillRays = idx;
                        L.KV("PV-SKILL-RAYCAST", "screen=" + L.F(sp.x) + "," + L.F(sp.y)
                            + " closeButtonIndex=" + idx + " " + chain);

                        IlPush(new Vector2(sp.x, sp.y), 0);
                        _route = "input-system-device";
                        Next();
                        break;
                    }
                    break;

                // 26) hover the button -> ControlTip must become visible
                case 26:
                    if (!Elapsed(1.4f)) break;
                    {
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        if (panel == null) { Next(); break; }
                        var tip = panel.transform.Find("ControlTip");
                        if (tip != null && tip.gameObject.activeSelf)
                        {
                            _route = "input-system-device";
                            ReadTip(panel.transform, "PV-SKILL-TIP-AFTER");
                            Next();
                            break;
                        }
                        // fallback: the very call the input module makes
                        var close = panel.transform.Find("CloseButton");
                        var rt = close as RectTransform;
                        var canvas = close.GetComponentInParent<Canvas>();
                        var sp = ScreenOfUi(rt, canvas);
                        var ped = new PointerEventData(EventSystem.current);
                        ped.position = sp;
                        ExecuteEvents.Execute(close.gameObject, ped, ExecuteEvents.pointerEnterHandler);
                        _route = "execute-events(pointerEnter)";
                        L.KV("PV-SKILL-TIP-ROUTE", _route + " (device route produced no enter within 1.4s)");
                        Next();
                        break;
                    }

                case 27:
                    if (Elapsed(0.8f))
                    {
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        ReadTip(panel != null ? panel.transform : null, "PV-SKILL-TIP-AFTER");
                        Snap("skill_close_hover");
                        Next();
                    }
                    break;

                // 28/29) real left button down then up at the graphic centre
                case 28:
                    if (Elapsed(0.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ok = IlPush(new Vector2(sp.x, sp.y), 1);
                        L.KV("PV-SKILL-MOUSE-DOWN", "at=" + L.F(sp.x) + "," + L.F(sp.y) + " ilOk=" + ok);
                        Next();
                    }
                    break;

                case 29:
                    if (Elapsed(0.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ok = IlPush(new Vector2(sp.x, sp.y), 0);
                        L.KV("PV-SKILL-MOUSE-UP", "at=" + L.F(sp.x) + "," + L.F(sp.y) + " ilOk=" + ok);
                        Next();
                    }
                    break;

                // 30) if the panel is still open, use the uGUI dispatch call
                case 30:
                    if (!Elapsed(1.6f)) break;
                    {
                        if (!Open<Diablo2.UI.SkillTreePanel>())
                        {
                            L.KV("PV-SKILL-CLOSED", "isOpen=False route=" + _route + " (device click closed it)");
                            Next();
                            break;
                        }
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        if (close == null) { Next(); break; }
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ped = new PointerEventData(EventSystem.current);
                        ped.position = sp;
                        ExecuteEvents.Execute(close.gameObject, ped, ExecuteEvents.pointerClickHandler);
                        _route = "execute-events(pointerClick)";
                        L.KV("PV-SKILL-CLICK-FALLBACK", _route);
                        Next();
                        break;
                    }

                case 31:
                    if (Elapsed(1.2f))
                    {
                        L.KV("PV-SKILL-CLOSED", "isOpen=" + Open<Diablo2.UI.SkillTreePanel>()
                            + " route=" + _route
                            + " verdict=" + (!Open<Diablo2.UI.SkillTreePanel>() ? "PASS" : "FAIL(still-open)"));
                        Snap("skill_closed");
                        Next();
                    }
                    break;

                // 32) open the quest log through the same entry as the Q key
                case 32:
                    if (Elapsed(0.6f))
                    {
                        L.Log("PV-OPEN QuestLogPanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "QuestLogPanel");
                        Next();
                    }
                    break;

                case 33:
                    if (Open<Diablo2.UI.QuestLogPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.0f)) break;
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        if (panel == null) { L.Warn("QUEST panel instance null"); Next(); break; }
                        var close = panel.transform.Find("CloseButton");
                        if (close == null) { L.Warn("QUEST no CloseButton node"); Next(); break; }
                        var img = close.GetComponent<Image>();
                        var rt = close as RectTransform;
                        var canvas = close.GetComponentInParent<Canvas>();
                        var sp = ScreenOfUi(rt, canvas);
                        L.KV("PV-QUEST-CLOSE-NODE", "name=" + close.name + " active=" + close.gameObject.activeSelf
                            + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "(null)")
                            + " anchored=" + L.F(rt.anchoredPosition.x) + "," + L.F(rt.anchoredPosition.y)
                            + " size=" + L.F(rt.sizeDelta.x) + "x" + L.F(rt.sizeDelta.y)
                            + " screen=" + L.F(sp.x) + "," + L.F(sp.y)
                            + " canvas=" + (canvas != null ? canvas.renderMode.ToString() : "(none)"));
                        ReadTip(panel.transform, "PV-QUEST-TIP-BEFORE");

                        string chain;
                        var idx = RaycastChain(panel, "CloseButton", sp, out chain);
                        _questRays = idx;
                        L.KV("PV-QUEST-RAYCAST", "screen=" + L.F(sp.x) + "," + L.F(sp.y)
                            + " closeButtonIndex=" + idx + " " + chain);

                        IlPush(new Vector2(sp.x, sp.y), 0);
                        _route = "";
                        Next();
                        break;
                    }
                    break;

                case 34:
                    if (!Elapsed(1.4f)) break;
                    {
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        if (panel == null) { Next(); break; }
                        var tip = panel.transform.Find("ControlTip");
                        if (tip != null && tip.gameObject.activeSelf)
                        {
                            _route = "input-system-device";
                            ReadTip(panel.transform, "PV-QUEST-TIP-AFTER");
                            Next();
                            break;
                        }
                        var close = panel.transform.Find("CloseButton");
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ped = new PointerEventData(EventSystem.current);
                        ped.position = sp;
                        ExecuteEvents.Execute(close.gameObject, ped, ExecuteEvents.pointerEnterHandler);
                        _route = "execute-events(pointerEnter)";
                        L.KV("PV-QUEST-TIP-ROUTE", _route + " (device route produced no enter within 1.4s)");
                        Next();
                        break;
                    }

                case 35:
                    if (Elapsed(0.8f))
                    {
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        ReadTip(panel != null ? panel.transform : null, "PV-QUEST-TIP-AFTER");
                        Snap("quest_close_hover");
                        Next();
                    }
                    break;

                case 36:
                    if (Elapsed(0.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ok = IlPush(new Vector2(sp.x, sp.y), 1);
                        L.KV("PV-QUEST-MOUSE-DOWN", "at=" + L.F(sp.x) + "," + L.F(sp.y) + " ilOk=" + ok);
                        Next();
                    }
                    break;

                case 37:
                    if (Elapsed(0.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ok = IlPush(new Vector2(sp.x, sp.y), 0);
                        L.KV("PV-QUEST-MOUSE-UP", "at=" + L.F(sp.x) + "," + L.F(sp.y) + " ilOk=" + ok);
                        Next();
                    }
                    break;

                case 38:
                    if (!Elapsed(1.6f)) break;
                    {
                        if (!Open<Diablo2.UI.QuestLogPanel>())
                        {
                            L.KV("PV-QUEST-CLOSED", "isOpen=False route=" + _route + " (device click closed it)");
                            Next();
                            break;
                        }
                        var panel = FindPanel("Diablo2.UI.QuestLogPanel");
                        var close = panel != null ? panel.transform.Find("CloseButton") : null;
                        if (close == null) { Next(); break; }
                        var rt = close as RectTransform;
                        var sp = ScreenOfUi(rt, close.GetComponentInParent<Canvas>());
                        var ped = new PointerEventData(EventSystem.current);
                        ped.position = sp;
                        ExecuteEvents.Execute(close.gameObject, ped, ExecuteEvents.pointerClickHandler);
                        _route = "execute-events(pointerClick)";
                        L.KV("PV-QUEST-CLICK-FALLBACK", _route);
                        Next();
                        break;
                    }

                case 39:
                    if (Elapsed(1.2f))
                    {
                        L.KV("PV-QUEST-CLOSED", "isOpen=" + Open<Diablo2.UI.QuestLogPanel>()
                            + " route=" + _route
                            + " verdict=" + (!Open<Diablo2.UI.QuestLogPanel>() ? "PASS" : "FAIL(still-open)"));
                        Snap("quest_closed");
                        Next();
                    }
                    break;

                // 40) optional: open the shop and check the two bottom action
                //     buttons carry no text Label (hover tip only)
                case 40:
                    if (Elapsed(0.5f)) { OpenShop(); Next(); }
                    break;

                case 41:
                    if (Open<Diablo2.UI.ShopPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.2f)) break;
                        var panel = FindPanel("Diablo2.UI.ShopPanel");
                        if (panel == null) { L.Warn("SHOP panel instance null"); Next(); break; }
                        BottomButton(panel.transform, "RepairAll", "PV-SHOP-BTN-REPAIR");
                        BottomButton(panel.transform, "Close", "PV-SHOP-BTN-CLOSE");
                        ReadTips(panel.transform, "PV-SHOP-TIPS-BEFORE");
                        var repair = panel.transform.Find("RepairAll");
                        if (repair != null) PinWorld(ScreenOfUi(repair as RectTransform, repair.GetComponentInParent<Canvas>()));
                        Next();
                        break;
                    }
                    break;

                case 42:
                    if (Elapsed(1.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.ShopPanel");
                        ReadTips(panel != null ? panel.transform : null, "PV-SHOP-TIPS-REPAIR-HOVER");
                        Snap("shop_bottom_buttons");
                        Next();
                    }
                    break;

                case 43:
                    if (Elapsed(0.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.ShopPanel");
                        var close = panel != null ? panel.transform.Find("Close") : null;
                        if (close != null) PinWorld(ScreenOfUi(close as RectTransform, close.GetComponentInParent<Canvas>()));
                        else L.Warn("SHOP no Close node");
                        Next();
                    }
                    break;

                case 44:
                    if (Elapsed(1.4f))
                    {
                        var panel = FindPanel("Diablo2.UI.ShopPanel");
                        ReadTips(panel != null ? panel.transform : null, "PV-SHOP-TIPS-CLOSE-HOVER");
                        Snap("shop_close_button_hover");
                        Next();
                    }
                    break;

                case 45:
                    if (Elapsed(0.6f))
                    {
                        L.Log("PV-SHOP-CLOSE emit " + Events.PanelToggleRequest + "(\"ShopPanel\")");
                        L.Emit(Events.PanelToggleRequest, "ShopPanel");
                        Next();
                    }
                    break;

                case 46:
                    if (Elapsed(1.2f))
                    {
                        L.KV("PV-SHOP-CLOSED", "isOpen=" + Open<Diablo2.UI.ShopPanel>());
                        L.KV("PV-FINISH", "steps=47 walkSteps=" + _walk.Count + " dialogs=" + _dialogCount
                            + " raycast skillClose=" + _skillRays + " questClose=" + _questRays
                            + " pointerRoute=" + (_useSeam ? "attach-input-seam" : "input-system-device"));
                        _done = true;
                        L.Done();
                    }
                    break;

                default:
                    _done = true;
                    L.Done();
                    break;
            }
        }

        /// <summary>Every ControlTip directly under a root (there are two on the shop panel).</summary>
        private void ReadTips(Transform root, string tag)
        {
            if (root == null) { L.KV(tag, "root=null"); return; }
            var n = 0;
            var any = false;
            for (var i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name != "ControlTip") continue;
                var active = c.gameObject.activeSelf;
                if (active) any = true;
                L.KV(tag + "-" + n, "siblingIndex=" + c.GetSiblingIndex() + " activeSelf=" + active
                    + " anchored=" + L.F(((RectTransform)c).anchoredPosition.x) + ","
                    + L.F(((RectTransform)c).anchoredPosition.y)
                    + " label=[" + LabelUnder(c) + "]");
                n++;
            }
            if (n == 0) L.KV(tag, "ControlTip count=0 (MISSING)");
            else L.KV(tag + "-SUMMARY", "count=" + n + " anyActive=" + any);
        }

        /// <summary>Bottom action button: no text Label node, art frame + hover tip only.</summary>
        private void BottomButton(Transform root, string name, string tag)
        {
            var btn = root.Find(name);
            if (btn == null) { L.KV(tag, "node=MISSING"); return; }
            var label = btn.Find("Label");
            var img = btn.GetComponent<Image>();
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < btn.childCount; i++) sb.Append(btn.GetChild(i).name).Append(' ');
            var rt = btn as RectTransform;
            var sp = ScreenOfUi(rt, btn.GetComponentInParent<Canvas>());
            L.KV(tag, "node=" + name + " labelNode=" + (label != null ? "PRESENT(FAIL)" : "null(PASS)")
                + " children=[" + sb.ToString().Trim() + "]"
                + " active=" + btn.gameObject.activeSelf
                + " size=" + L.F(rt.sizeDelta.x) + "x" + L.F(rt.sizeDelta.y)
                + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "(null)")
                + " screen=" + L.F(sp.x) + "," + L.F(sp.y));
        }

        /// <summary>Open the shop with the real stock of the first NPC that has one.</summary>
        private void OpenShop()
        {
            var npcMod = L.CtxMember("Npc");
            var gsm = npcMod != null
                ? npcMod.GetType().GetMethod("GetShop", BindingFlags.Public | BindingFlags.Instance)
                : null;
            for (var sid = 0; sid < 6; sid++)
            {
                var cand = gsm != null ? gsm.Invoke(npcMod, new object[] { sid }) as ShopOpenArgs : null;
                if (cand == null || cand.stock == null || cand.stock.Count == 0) continue;
                L.KV("PV-SHOP", "npc=" + cand.npcName + " stock=" + cand.stock.Count
                    + " gold=" + cand.playerGold + " canRepair=" + cand.canRepair);
                L.Emit(Events.ShopOpen, cand);
                return;
            }
            L.Warn("PV-SHOP no stock found (GetShop 0..5 all empty)");
        }

        // ---- NPC geometry helpers -------------------------------------------

        /// <summary>Akara (npcId 0) cell from INpcModule.All; (-1,-1) when unavailable.</summary>
        private static Vector2Int AkaraGrid()
        {
            var npc = L.CtxMember("Npc");
            var allProp = npc != null ? npc.GetType().GetProperty("All", BindingFlags.Public | BindingFlags.Instance) : null;
            var all = allProp != null ? allProp.GetValue(npc) as IEnumerable : null;
            if (all == null) return new Vector2Int(-1, -1);
            foreach (var o in all)
            {
                if (o == null) continue;
                var idF = o.GetType().GetField("id");
                var id = idF != null ? (int)idF.GetValue(o) : -1;
                if (id != 0) continue;
                var gx = o.GetType().GetField("gridX");
                var gy = o.GetType().GetField("gridY");
                return new Vector2Int(gx != null ? (int)gx.GetValue(o) : -1, gy != null ? (int)gy.GetValue(o) : -1);
            }
            return new Vector2Int(-1, -1);
        }

        /// <summary>
        /// A walkable staging cell 5..7 (Chebyshev) away from Akara: outside her
        /// 2.40-cell talk range, so the "walk to her cell first" branch can fire.
        /// </summary>
        private static Vector2Int StageCell(IMapModule map, out string how)
        {
            how = "fallback(15,11)";
            var st = new Vector2Int(15, 11);
            var a = AkaraGrid();
            if (map == null || a.x < 0) return st;
            var best = new Vector2Int(-1, -1);
            var bestScore = float.MaxValue;
            for (var dy = -7; dy <= 7; dy++)
            {
                for (var dx = -7; dx <= 7; dx++)
                {
                    var d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (d < 5 || d > 7) continue;
                    var c = new Vector2Int(a.x + dx, a.y + dy);
                    if (!map.Walkable(c)) continue;
                    var score = Mathf.Abs(dx - 5) + Mathf.Abs(dy - 1);
                    if (score < bestScore) { bestScore = score; best = c; }
                }
            }
            if (best.x >= 0) { how = "ring5..7 nearest to akara+(5,1)"; return best; }
            return st;
        }

        /// <summary>
        /// Pass-by ground cell: 2..3 (Chebyshev) from Akara, walkable, and not on
        /// her own cell.  The click is aimed at a cell centre so the pointer never
        /// sits on her sprite rectangle.
        /// </summary>
        private static Vector2Int PassByCell(IMapModule map, out string how)
        {
            how = "none";
            var a = AkaraGrid();
            if (map == null || a.x < 0) return new Vector2Int(-1, -1);
            var offs = new int[,] { { 2, 1 }, { -2, 1 }, { 2, -1 }, { -2, -1 }, { 2, 0 }, { -2, 0 }, { 0, 2 }, { 0, -2 } };
            for (var i = 0; i < offs.GetLength(0); i++)
            {
                var c = new Vector2Int(a.x + offs[i, 0], a.y + offs[i, 1]);
                if (map.Walkable(c)) { how = "offset(" + offs[i, 0] + "," + offs[i, 1] + ") walkable"; return c; }
            }
            for (var dy = -3; dy <= 3; dy++)
            {
                for (var dx = -3; dx <= 3; dx++)
                {
                    var d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (d < 2 || d > 3) continue;
                    var c = new Vector2Int(a.x + dx, a.y + dy);
                    if (map.Walkable(c)) { how = "ring2..3 (" + dx + "," + dy + ")"; return c; }
                }
            }
            return new Vector2Int(-1, -1);
        }

        /// <summary>Akara's sprite rectangle (world xy) via IViewModule.GetView(-1-npcId).</summary>
        private static Rect? NpcRect(int npcId)
        {
            var view = L.ViewModule();
            if (view == null) return null;
            var m = view.GetType().GetMethod("GetView", BindingFlags.Public | BindingFlags.Instance);
            if (m == null) return null;
            var go = m.Invoke(view, new object[] { -1 - npcId }) as GameObject;
            if (go == null) return null;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null || sr.sprite == null) return null;
            var b = sr.bounds;
            return new Rect(b.min.x, b.min.y, b.size.x, b.size.y);
        }
    }
}
