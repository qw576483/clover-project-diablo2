// =============================================================================
// r1_drive.cs -- ONE Play session, batch evidence for the R1 five fixes (A1..A16).
//
//   R1-B  click on a non-walkable cell -> nearest-walkable fallback
//   R1-C  char-create: per-frame transition rectangles + panel-driven name field
//   R1-D  frame pacing pinned + per-frame move deltas
//   R1-E  dialog bar + shop coexist (S1) / close zeroes module state (S3) /
//         panel-button click produces no MoveCommand (S2) / shop title+hint (S5)
//
// HOW IT DRIVES (one play session): input is injected as REAL InputSystem events
// (`QueueStateEvent` / `QueueTextEvent`) so the full chain runs (InputSystem ->
// InputSystemUIInputModule(uGUI) / CloverInput -> InputReader -> Player/Npc).
// Tiles are written by the driver itself into <repo>/.ai-tmp/screenshots/
// (outside the Unity project => never imported => the session survives).
//   UI tiles    -> ScreenCapture.CaptureScreenshot (== --source screen)
//   world tiles -> main camera into a RenderTexture + EncodeToPNG
//                  (== --source camera)
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Dir8 = Diablo2.Def.Dir8;

namespace R1
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Drive
    {
        internal const string Tag = "R1";
        internal const int ShotW = 1920;
        internal const int ShotH = 1080;

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;

        private static string _pendingFile = string.Empty;
        private static string _pendingKind = string.Empty;
        private static int _pendingIndex;

        // MoveCommand emissions seen by this driver (the S2 / A14 numeric proof).
        private static int _moveCmds = -1;

        internal static string RawDir { get { return _rawDir; } }
        internal static string DonePath { get { return _done; } }
        internal static bool ShotPending { get { return _pendingFile.Length > 0; } }
        internal static int MoveCmds { get { return _moveCmds; } }
        internal static void SetMoveCmds(int v) { _moveCmds = v; }
        internal static void BumpMoveCmds() { if (_moveCmds < 0) _moveCmds = 0; _moveCmds++; }

        // ================================================================ logging ==========
        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Info(Tag, msg);
            else UnityEngine.Debug.Log("[" + Tag + "] " + msg);
        }

        internal static void Warn(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }

        internal static void KV(string key, string value) { Log(key + "=" + value); }

        private static readonly List<string> Checks = new List<string>();

        internal static void Check(string name, bool ok, string detail)
        {
            Checks.Add(name + "=" + (ok ? 1 : 0));
            Log("CHK name=" + name + " ok=" + (ok ? 1 : 0) + " " + detail);
        }

        internal static string CheckSummary() { return string.Join(",", Checks.ToArray()); }

        internal static bool AllChecksOk()
        {
            foreach (var c in Checks)
            {
                if (c.EndsWith("=0")) return false;
            }
            return Checks.Count > 0;
        }

        // ================================================================ files ============
        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) { Warn("MARKER path empty"); return; }
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
            }
            catch (Exception ex) { Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static bool FileExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path); } catch { return false; }
        }

        internal static long FileSize(string path)
        {
            try { return new FileInfo(path).Length; } catch { return -1; }
        }

        internal static string Paths(string arg)
        {
            var parts = (arg ?? string.Empty).Split('|');
            if (parts.Length > 0) _rawDir = parts[0];
            if (parts.Length > 1) _done = parts[1];
            Log("PATHS rawDir=" + _rawDir + " done=" + _done);
            return "PATHS-OK";
        }

        internal static string ShotFile(string name) { return _rawDir + "/" + name; }

        // ================================================================ capture ==========
        internal static void BeginShot(int index, string name, string kind)
        {
            _pendingIndex = index;
            _pendingFile = _rawDir + "/" + name;
            _pendingKind = kind;
            try { if (File.Exists(_pendingFile)) File.Delete(_pendingFile); } catch { }
            Log("SHOT-REQUEST n=" + index + " name=" + name + " kind=" + kind + " path=" + _pendingFile);
        }

        internal static void FlushShot()
        {
            if (_pendingFile.Length == 0) return;
            var file = _pendingFile;
            var kind = _pendingKind;
            var index = _pendingIndex;
            _pendingFile = string.Empty;
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (kind == "screen")
                {
                    ScreenCapture.CaptureScreenshot(file);
                    Log("SHOT-ISSUED n=" + index + " kind=screen file=" + file
                        + " frame=" + Time.frameCount + " t=" + Time.time.ToString("0.00"));
                    return;
                }

                var cam = Camera.main;
                if (cam == null) { Warn("SHOT-FAIL n=" + index + " Camera.main is null"); return; }

                RenderTexture rt = null;
                Texture2D tex = null;
                try
                {
                    rt = RenderTexture.GetTemporary(ShotW, ShotH, 24);
                    var prev = cam.targetTexture;
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = prev;

                    RenderTexture.active = rt;
                    tex = new Texture2D(ShotW, ShotH, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0f, 0f, ShotW, ShotH), 0, 0);
                    tex.Apply();
                    RenderTexture.active = null;

                    var bytes = tex.EncodeToPNG();
                    File.WriteAllBytes(file, bytes);
                    Log("SHOT-ISSUED n=" + index + " kind=camera file=" + file + " bytes=" + bytes.Length
                        + " camPos=" + World(cam.transform.position)
                        + " ortho=" + cam.orthographicSize.ToString("0.###")
                        + " aspect=" + cam.aspect.ToString("0.####")
                        + " frame=" + Time.frameCount + " t=" + Time.time.ToString("0.00"));
                }
                finally
                {
                    RenderTexture.active = null;
                    if (rt != null) RenderTexture.ReleaseTemporary(rt);
                    if (tex != null) UnityEngine.Object.Destroy(tex);
                }
            }
            catch (Exception ex)
            {
                Warn("SHOT-FAIL n=" + index + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static bool ShotReady(string name, out long bytes)
        {
            var f = ShotFile(name);
            bytes = FileExists(f) ? FileSize(f) : -1;
            return bytes > 0;
        }

        internal static void LogShot(string state, int index, string name, string kind)
        {
            long bytes;
            var ok = ShotReady(name, out bytes);
            Log("SHOT-" + (ok ? "OK" : "MISSING") + " n=" + index + " state=" + state + " name=" + name
                + " kind=" + kind + " bytes=" + bytes + " path=" + ShotFile(name));
        }

        // ================================================================ reflection =======
        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name);
                if (t != null) return t;
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

        internal static Diablo2.Module.IPlayerModule Player()
        {
            return CtxMember("Player") as Diablo2.Module.IPlayerModule;
        }

        internal static Diablo2.Module.IMapModule Map()
        {
            return CtxMember("Map") as Diablo2.Module.IMapModule;
        }

        internal static Diablo2.Module.INpcModule Npc()
        {
            return CtxMember("Npc") as Diablo2.Module.INpcModule;
        }

        internal static Diablo2.Module.IQuestModule Quest()
        {
            return CtxMember("Quest") as Diablo2.Module.IQuestModule;
        }

        internal static Diablo2.Module.ISaveModule Save()
        {
            return CtxMember("Save") as Diablo2.Module.ISaveModule;
        }

        internal static Diablo2.UI.CharSelectPanel CharSelectPanel()
        {
            var a = UnityEngine.Object.FindObjectsByType<Diablo2.UI.CharSelectPanel>();
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static object Field(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }

        internal static object Prop(object o, string name)
        {
            if (o == null) return null;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return p != null ? p.GetValue(o) : null;
        }

        internal static object StaticField(string typeName, string field)
        {
            var t = FindType(typeName);
            if (t == null) return null;
            var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return f != null ? f.GetValue(null) : null;
        }

        internal static string Call(object o, string method, object[] args)
        {
            if (o == null) return "(no-obj)";
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return "(no-method:" + method + ")";
            var r = m.Invoke(o, args);
            return r != null ? r.ToString() : "(null)";
        }

        internal static string Fmt(object o) { return o == null ? "(null)" : o.ToString(); }

        // ---- camera rig --------------------------------------------------------------------
        internal static object Rig() { return CtxMember("Camera"); }

        internal static float RigOrtho()
        {
            var v = Prop(Rig(), "OrthographicSize");
            return v is float ? (float)v : -1f;
        }

        internal static bool RigSetOrtho(float v)
        {
            var rig = Rig();
            if (rig == null) return false;
            var f = rig.GetType().GetField("_ortho", BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return false;
            f.SetValue(rig, v);
            return true;
        }

        internal static Diablo2.Module.ICameraRig RigContract()
        {
            return CtxMember("Camera") as Diablo2.Module.ICameraRig;
        }

        // ---- player ------------------------------------------------------------------------
        internal static Vector3 PlayerWorld() { var p = Player(); return p != null ? p.World : Vector3.zero; }
        internal static Vector2Int PlayerGrid() { var p = Player(); return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue); }
        internal static bool PlayerMoving() { var p = Player(); return p != null && p.IsMoving; }

        internal static Vector3 PlayerViewWorld()
        {
            var vm = CtxMember("View");
            if (vm == null) return Vector3.zero;
            var view = Field(vm, "_player");
            if (view == null) return Vector3.zero;
            var sr = Field(view, "Renderer") as SpriteRenderer;
            if (sr != null) return sr.transform.position;
            var t = Prop(view, "Transform") as Transform;
            return t != null ? t.position : Vector3.zero;
        }

        internal static bool CellPos(Vector3 world, out Vector2 cell)
        {
            cell = Vector2.zero;
            var t = FindType("Diablo2.Module.Player.PlayerMotor");
            if (t == null) return false;
            var m = t.GetMethod("CellCenterOf", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return false;
            var r = m.Invoke(null, new object[] { world });
            if (!(r is Vector2)) return false;
            cell = (Vector2)r;
            return true;
        }

        internal static bool TryGetTileKeys(int x, int y, out string ground, out string obj)
        {
            ground = null;
            obj = null;
            var map = Map();
            if (map == null) return false;
            var m = map.GetType().GetMethod("TryGetTileKeys", BindingFlags.Public | BindingFlags.Instance);
            if (m == null) return false;
            var args = new object[] { x, y, null, null };
            var ok = (bool)m.Invoke(map, args);
            ground = args[2] as string;
            obj = args[3] as string;
            return ok;
        }

        internal static TileKind TileKindAt(Vector2Int g)
        {
            var map = Map();
            return map != null ? map.TileAt(g) : TileKind.Void;
        }

        internal static bool Walkable(Vector2Int g)
        {
            var map = Map();
            return map != null && map.Walkable(g);
        }

        internal static void HandlePrimaryClickFallback(Vector2Int grid)
        {
            var p = Player();
            if (p == null) return;
            Call(p, "HandlePrimaryClick", new object[] { grid });
        }

        // ================================================================ panels ==========
        internal static Diablo2.UI.CharCreatePanel CharCreate()
        {
            var a = UnityEngine.Object.FindObjectsByType<Diablo2.UI.CharCreatePanel>();
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static Diablo2.UI.NpcDialogPanel Dialog()
        {
            var a = UnityEngine.Object.FindObjectsByType<Diablo2.UI.NpcDialogPanel>();
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static Diablo2.UI.ShopPanel Shop()
        {
            var a = UnityEngine.Object.FindObjectsByType<Diablo2.UI.ShopPanel>();
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static Diablo2.UI.QuestLogPanel QuestLog()
        {
            var a = UnityEngine.Object.FindObjectsByType<Diablo2.UI.QuestLogPanel>();
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static string PathOf(Transform t)
        {
            var sb = new StringBuilder();
            var cur = t;
            while (cur != null)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, cur.name);
                cur = cur.parent;
            }
            return sb.ToString();
        }

        internal static bool ScreenRect(RectTransform rt, out Vector2 lo, out Vector2 hi, out Vector2 center)
        {
            lo = Vector2.zero;
            hi = Vector2.zero;
            center = Vector2.zero;
            if (rt == null) return false;
            try
            {
                var a = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMin, 0f)));
                var b = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.yMax, 0f)));
                var c = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
                lo = new Vector2(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
                hi = new Vector2(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                center = new Vector2(c.x, c.y);
                return true;
            }
            catch (Exception e)
            {
                Warn("SCREEN-RECT-FAIL " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        internal static void LogCrop(int n, string tile, string node, RectTransform rt)
        {
            Vector2 lo, hi, c;
            if (!ScreenRect(rt, out lo, out hi, out c))
            {
                KV("CROP", "n=" + n + " tile=" + tile + " node=" + node + " (no rect)");
                return;
            }
            KV("CROP", "n=" + n + " tile=" + tile + " node=" + node
                + " sx0=" + lo.x.ToString("0") + " sy0=" + lo.y.ToString("0")
                + " sx1=" + hi.x.ToString("0") + " sy1=" + hi.y.ToString("0")
                + " cx=" + c.x.ToString("0") + " cy=" + c.y.ToString("0"));
        }

        internal static void DumpTextsUnder(string where, Transform root, int limit)
        {
            var n = 0;
            var sb = new StringBuilder();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Text>())
            {
                if (t == null || t.text == null) continue;
                if (root != null && !t.transform.IsChildOf(root) && t.transform != root) continue;
                Vector2 lo, hi, c;
                var has = ScreenRect(t.rectTransform, out lo, out hi, out c);
                n++;
                if (n <= limit)
                {
                    sb.Append('|').Append(PathOf(t.transform))
                      .Append("\"").Append(t.text.Replace("\n", "\\n")).Append("\"")
                      .Append(" size=").Append(t.fontSize)
                      .Append(" enabled=").Append(t.enabled ? 1 : 0)
                      .Append(" font=").Append(t.font == null ? "null" : "SET")
                      .Append(" active=").Append(t.gameObject.activeInHierarchy ? 1 : 0);
                    if (has)
                        sb.Append(" screen=").Append(c.x.ToString("0")).Append(',').Append(c.y.ToString("0"))
                          .Append(" rect=").Append((hi.x - lo.x).ToString("0")).Append('x').Append((hi.y - lo.y).ToString("0"));
                    sb.Append(' ');
                }
            }
            KV("UITEXTS", "where=" + where + " count=" + n + " " + sb.ToString());
        }

        internal static Button FindButton(string arg, out string diag)
        {
            diag = string.Empty;
            var pathSub = string.Empty;
            var want = arg ?? string.Empty;
            var bar = want.IndexOf('|');
            if (bar >= 0) { pathSub = want.Substring(0, bar); want = want.Substring(bar + 1); }

            var all = UnityEngine.Object.FindObjectsByType<Button>();
            Button pick = null;
            var n = 0;
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (pathSub.Length > 0 && PathOf(b.transform).IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                if (pick == null || PathOf(b.transform).Length > PathOf(pick.transform).Length) pick = b;
            }
            diag = "buttons=" + all.Length + " candidates=" + n;
            return pick;
        }

        internal static string Click(string arg)
        {
            if (EventSystem.current == null) { Log("ERR click name=" + arg + " eventSystem=null"); return "ERR-no-eventsystem"; }
            string diag;
            var pick = FindButton(arg, out diag);
            if (pick == null) { Log("ERR click-miss name=" + arg + " " + diag); return "ERR-click-miss"; }
            var go = pick.gameObject;
            var ped = new PointerEventData(EventSystem.current);
            ped.button = PointerEventData.InputButton.Left;
            var rt = pick.transform as RectTransform;
            if (rt != null) ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            Log("CLICK-EXEC name=" + arg + " path=" + PathOf(go.transform) + " " + diag);
            if (!pick.interactable) { Log("CLICK-SKIP name=" + arg + " (not interactable)"); return "CLICK-SKIP"; }
            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
            return "CLICKED";
        }

        internal static string RaycastTop(Vector2 screenPos)
        {
            if (EventSystem.current == null) return "(no-eventsystem)";
            var ped = new PointerEventData(EventSystem.current);
            ped.position = screenPos;
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            if (hits.Count == 0) return "(none)";
            var g = hits[0].gameObject;
            return g == null ? "(null)" : (g.name + "@" + PathOf(g.transform));
        }

        // ================================================================ input ============
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

        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = MouseDev();
            if (m == null) { Warn("MouseState: no mouse device"); return; }
            var st = new MouseState { position = pos };
            if (leftDown) st = st.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(m, st);
        }

        internal static Vector3 MousePosNow()
        {
            var input = Game.Input;
            return input != null ? input.MousePosition : Vector3.zero;
        }

        internal static string KeyDown(string arg)
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            Key key;
            if (!TryParseKey(arg, out key)) { Log("ERR keydown unknown-key=" + arg); return "ERR-unknown-key"; }
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key + " keyboard=" + kb.name);
            return "KEYDOWN-" + key;
        }

        internal static string KeyUp()
        {
            var kb = KeyboardDev();
            if (kb == null) return "ERR-no-keyboard";
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
            return "KEYUP";
        }

        private static bool TryParseKey(string arg, out Key key)
        {
            switch ((arg ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = Key.Space; return true;
                case "escape": key = Key.Escape; return true;
                case "enter": key = Key.Enter; return true;
                case "i": key = Key.I; return true;
                case "r": key = Key.R; return true;
                case "q": key = Key.Q; return true;
                case "c": key = Key.C; return true;
                case "tab": key = Key.Tab; return true;
            }
            key = Key.None;
            return false;
        }

        /// <summary>Queue ONE text character (real text path -> Keyboard.onTextInput).</summary>
        internal static bool TextChar(char c)
        {
            var kb = KeyboardDev();
            if (kb == null) { Warn("TextChar: no keyboard"); return false; }
            InputSystem.QueueTextEvent(kb, c);
            return true;
        }

        // ================================================================ formatting =======
        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string World(Vector3 w)
        {
            return "(" + w.x.ToString("0.00") + "," + w.y.ToString("0.00") + ")";
        }

        internal static string V(Vector2 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")"; }

        internal static string V3(Vector3 v)
        {
            return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")";
        }
    }
    /// <summary>Public one-shot entries for the run script.</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

        private static bool _subscribed;

        /// <summary>Editor/play setup so injected input really reaches the game.</summary>
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            // NEVER touch InputSystem.settings.updateMode (engine skill P-4 item 4).

            var added = 0;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(); added = 1; }
            var mouseAdded = 0;
            if (UnityEngine.InputSystem.Mouse.current == null) { InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); mouseAdded = 1; }

            // NOTE: the MoveCommand counter is registered by `Driver.Init` -- NOT here.
            //   Every `run_script` call compiles its own ephemeral assembly, so a static set by
            //   this invocation is NOT visible to the assembly the tour runs in (and a handler
            //   registered here would bump a counter the tour can never read).
            _ = _subscribed;
            Drive.KV("SUBS", "MoveCommand counter = registered by Driver.Init (same assembly as the tour); "
                + "statics do NOT cross run_script invocations");

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " focused=" + (Application.isFocused ? 1 : 0)
                       + " screen=" + Screen.width + "x" + Screen.height
                       + " keyboard=" + (kb != null ? kb.name : "(null)")
                       + " keyboardAdded=" + added
                       + " mouseAdded=" + mouseAdded
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)")
                       + " scene=" + (Game.Scene != null ? Game.Scene.CurrentScene : "(null)");
            Drive.Log(line);
            return line;
        }

        private static int _toggles;

        private static void OnMoveCommand(Vector2Int target) { Drive.BumpMoveCmds(); }
        private static void OnPanelToggle(string panel) { _toggles++; }

        /// <summary>spec = "raw dir|done marker".</summary>
        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Ground-click sequence state (real InputSystem mouse events, self-calibrating).</summary>
    internal class GroundClick
    {
        public Vector2Int Grid;
        public int Phase;
        public int FramesHere;
        public Vector2 Pos;
        public bool FlipTried;
        public bool FlipY;
        public bool Projected;
        public bool Fallback;
        public int MoveBefore;
        public int MoveAfter = -1;
        public string UiTop = "(none)";
        public string Result = "(pending)";
    }

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("R1EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + UnityEngine.SceneManagement.SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }
    /// <summary>
    /// One tour, one Play session: boot -> menu -> roster -> new hero (A5..A8) -> town
    /// (A1..A4) -> walk trace (A9) -> Akara dialog (A10/A11) -> shop (A12/A15) ->
    /// shop closed (A13) -> dialog button click (A14) -> quest log (A16).
    /// </summary>
    public class Driver : MonoBehaviour
    {
        // tile names --------------------------------------------------------------
        private const string T01 = "a01_town_wide.png";
        private const string T02 = "a02_town_bridge.png";
        private const string T03A = "a03a_bridge_click_050.png";
        private const string T03B = "a03b_bridge_click_150.png";
        private const string T04 = "a04_water_fallback.png";
        private const string T05A = "a05a_amazon_idle.png";
        private const string T05B = "a05b_barbarian_idle.png";
        private const string T06A = "a06a_amazon_transition.png";
        private const string T06B = "a06b_barbarian_transition.png";
        private const string T07A = "a07a_amazon_front.png";
        private const string T07B = "a07b_barbarian_front.png";
        private const string T08 = "a08_name_digits.png";
        private const string T09 = "a09_walk_";              // + 1..6 + ".png"
        private const string T10 = "a10_dialog_not_started.png";
        private const string T11 = "a11_dialog_in_progress.png";
        private const string T12 = "a12_shop_and_dialog.png";
        private const string T13 = "a13_shop_closed.png";
        private const string T14 = "a14_dialog_button_pre.png";
        private const string T15 = "a15_shop_title_hint.png";
        private const string T16 = "a16_questlog.png";

        private string _tag = "r1";
        private string _rawDir = string.Empty;
        private string _done = string.Empty;

        private int _step;
        private float _stepAt;
        private bool _doneRun;
        private string _timedOutAt = string.Empty;
        private int _shots;

        // ---- frame pacing ---------------------------------------------------------
        private readonly List<float> _pacing = new List<float>();
        private bool _pacingLogged;

        // ---- char create ----------------------------------------------------------
        private float _transStart;
        private int _transFrameSeen = -1;
        private bool _transTracing;
        private int _typeIdx;
        private float _ccOpenedAt;
        private int _a4GridAtClick;
        private Vector2Int _bridgeTarget = new Vector2Int(47, 27);
        private Vector2Int _waterTarget = new Vector2Int(int.MinValue, int.MinValue);
        private bool _hotspotRetried;
        private bool _transSeen;
        private bool _uiRetriedA11;
        private bool _uiRetriedA12;
        private bool _uiRetriedA13;
        private bool _uiRetriedA14;
        private string _typeStr = string.Empty;
        private bool _typePicked;
        private string _newHeroName = string.Empty;
        private bool _enterClicked;

        // ---- A9 walk --------------------------------------------------------------
        private int _a9Shots;
        private string _a9Pending = string.Empty;
        private int _a9LastFrame = -9999;
        private Vector3 _a9PrevWorld;
        private Vector2 _a9PrevCell;
        private bool _a9CellOk;
        private Vector2Int _a9Target = new Vector2Int(int.MinValue, int.MinValue);
        private Dir8 _a9Dir;
        private int _a9KeepWalking;

        // ---- ground click --------------------------------------------------------
        private GroundClick _ck;
        private float _ckStartedAt;
        private int _a14MoveBefore;
        private Vector2Int _a14GridBefore;

        // ---- ui click sequencer --------------------------------------------------
        private Vector2 _pendingUiClick;
        private string _pendingUiClickName = string.Empty;
        private int _uiClickPhase;
        private int _uiClickFrames;

        // ---- misc ----------------------------------------------------------------
        private Vector2Int _akara = new Vector2Int(int.MinValue, int.MinValue);

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _rawDir = parts[2];
            if (parts.Length > 3) _done = parts[3];
            Drive.Paths(_rawDir + "|" + _done);
            _step = 0;
            _stepAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount
                      + " rawDir=" + _rawDir + " screen=" + Screen.width + "x" + Screen.height);
            SubscribeMoveCounter();
            LogDevice();
        }

        /// <summary>
        /// Count `Events.MoveCommand` emissions FROM THIS ASSEMBLY (A14 / S2 numeric proof).
        /// <para>!!! Must happen inside the tour's own `run_script` invocation: statics and event
        /// handlers do not cross invocations (each compiles its own ephemeral assembly), so a
        /// counter registered from another `run_script` call stays invisible here and every
        /// `moveCmdDelta` would silently read 0.</para>
        /// </summary>
        private static void SubscribeMoveCounter()
        {
            if (Game.Event == null) { Drive.Warn("SUBSCRIBE Game.Event is null -> no MoveCommand counter"); return; }
            Drive.SetMoveCmds(0);
            Game.Event.On<Vector2Int>(Diablo2.Core.Events.MoveCommand, OnMoveCommandSeen);
            Drive.KV("SUBSCRIBE", "MoveCommand=1 (driver assembly) moveCmds=" + Drive.MoveCmds);
        }

        private static void OnMoveCommandSeen(Vector2Int target) { Drive.BumpMoveCmds(); }

        /// <summary>Performance-class evidence: device name + resolution (without them timings mean nothing).</summary>
        private void LogDevice()
        {
            var dev = "(unknown)";
            var gfx = "(unknown)";
            var vram = -1;
            try { dev = SystemInfo.graphicsDeviceName; } catch { }
            try { gfx = SystemInfo.graphicsDeviceType.ToString(); } catch { }
            try { vram = SystemInfo.graphicsMemorySize; } catch { }
            Drive.KV("DEVICE", "graphicsDeviceName=\"" + dev + "\" type=" + gfx + " vramMB=" + vram
                + " resolution=" + Screen.width + "x" + Screen.height
                + " vSync=" + QualitySettings.vSyncCount
                + " targetFrameRate=" + Application.targetFrameRate
                + " quality=" + QualitySettings.GetQualityLevel()
                + " clock=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        private void Update()
        {
            CollectPacing();
            TickTransition();
            TickUiClick();
            if (_doneRun) return;
            try { Step(); }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void LateUpdate()
        {
            try { Drive.FlushShot(); }
            catch (Exception ex) { Drive.Warn("FLUSH-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ================================================================ pacing ==========
        /// <summary>First 60 frames: min/p05/p50/p95/max of Time.unscaledDeltaTime + device name.</summary>
        private void CollectPacing()
        {
            if (_pacingLogged) return;
            _pacing.Add(Time.unscaledDeltaTime);
            if (_pacing.Count < 60) return;
            _pacingLogged = true;
            var a = new List<float>(_pacing);
            a.Sort();
            var sum = 0f;
            foreach (var v in a) sum += v;
            Drive.KV("PACING", "frames=60 min=" + (a[0] * 1000f).ToString("0.00")
                + " p05=" + (a[(int)(0.05f * (a.Count - 1))] * 1000f).ToString("0.00")
                + " p50=" + (a[(int)(0.50f * (a.Count - 1))] * 1000f).ToString("0.00")
                + " p95=" + (a[(int)(0.95f * (a.Count - 1))] * 1000f).ToString("0.00")
                + " max=" + (a[a.Count - 1] * 1000f).ToString("0.00")
                + " mean=" + ((sum / a.Count) * 1000f).ToString("0.00")
                + " (ms) vSync=" + QualitySettings.vSyncCount
                + " targetFrameRate=" + Application.targetFrameRate
                + " graphicsDeviceName=\"" + SafeDevice() + "\""
                + " resolution=" + Screen.width + "x" + Screen.height
                + " samplingEndFrame=" + Time.frameCount);
        }

        private static string SafeDevice()
        {
            try { return SystemInfo.graphicsDeviceName; } catch { return "(unknown)"; }
        }

        // ================================================================ helpers =========
        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool DialogIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>(); }
        private static bool ShopIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.ShopPanel>(); }
        private static bool QuestLogIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.QuestLogPanel>(); }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private bool Elapsed(float seconds) { return Time.unscaledTime - _stepAt >= seconds; }
        private void Next() { _step++; _stepAt = Time.unscaledTime; }

        private string State()
        {
            var p = Drive.Player();
            var m = Drive.Map();
            return "step=" + _step
                   + " fsm=" + Fsm() + " scene=" + SceneName()
                   + " area=" + (m != null ? m.Area.ToString() : "(no-map)")
                   + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                   + " grid=" + (p != null ? Drive.Grid(p.Grid) : "-")
                   + " moving=" + (p != null && p.IsMoving ? 1 : 0)
                   + " t=" + Time.time.ToString("0.00");
        }

        private bool WaitShot(int index, string name, string kind, string state, float budget)
        {
            long bytes;
            if (Drive.ShotReady(name, out bytes))
            {
                _shots++;
                Drive.LogShot(state, index, name, kind);
                Next();
                return true;
            }
            if (Elapsed(budget))
            {
                Drive.KV("SHOT-TIMEOUT", "n=" + index + " state=" + state + " name=" + name
                         + " kind=" + kind + " waited=" + budget.ToString("0.0") + "s");
                if (_timedOutAt.Length == 0) _timedOutAt = "shot-" + name;
                Next();
                return true;
            }
            return false;
        }

        private void TimeoutStep(string step, float seconds)
        {
            if (!Elapsed(seconds)) return;
            Drive.Log("STEP-TIMEOUT step=" + step + " elapsed=" + (Time.unscaledTime - _stepAt).ToString("0.0")
                      + " " + State());
            if (_timedOutAt.Length == 0) _timedOutAt = step;
            Finish("timeout-" + step);
        }

        /// <summary>Teleport the player (probe only: the walk itself is the interesting part).</summary>
        private void Hop(Vector2Int g, string why)
        {
            var p = Drive.Player();
            if (p == null) { Drive.Warn("HOP no player"); return; }
            p.TeleportTo(g);
            Drive.KV("HOP", "why=" + why + " to=" + Drive.Grid(g)
                + " walkable=" + (Drive.Walkable(g) ? 1 : 0)
                + " tileKind=" + Drive.TileKindAt(g)
                + " from=" + Drive.Grid(Drive.PlayerGrid()));
        }

        // ================================================================ ui click =========
        private void QueueUiClick(Vector2 pos, string nodeName)
        {
            _pendingUiClick = pos;
            _pendingUiClickName = nodeName;
            Drive.MouseState(pos, false);
            _uiClickPhase = 1;
            _uiClickFrames = 2;
        }

        /// <summary>Drives the queued real-mouse UI click (move -> down -> up).</summary>
        private void TickUiClick()
        {
            if (_uiClickPhase == 0) return;
            _uiClickFrames--;
            if (_uiClickFrames > 0) return;
            if (_uiClickPhase == 1)
            {
                Drive.MouseState(_pendingUiClick, true);
                Drive.KV("UI-DOWN", "pos=" + Drive.V(_pendingUiClick) + " node=" + _pendingUiClickName
                    + " frame=" + Time.frameCount + " moveCmds=" + Drive.MoveCmds);
                _uiClickPhase = 2;
                _uiClickFrames = 2;
                return;
            }
            Drive.MouseState(_pendingUiClick, false);
            Drive.KV("UI-UP", "pos=" + Drive.V(_pendingUiClick) + " frame=" + Time.frameCount
                + " moveCmds=" + Drive.MoveCmds);
            _uiClickPhase = 0;
            _uiClickFrames = 0;
        }

        /// <summary>Real-mouse click on a Button found by GameObject name.</summary>
        private void ClickNamedRealMouse(string name, string tag)
        {
            string diag;
            var b = Drive.FindButton(name, out diag);
            if (b == null)
            {
                Drive.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Drive.Click(name);
                return;
            }
            var rt = b.transform as RectTransform;
            Vector2 lo, hi, c;
            if (!Drive.ScreenRect(rt, out lo, out hi, out c)) { Drive.Warn("UI-NORECT " + name); return; }
            Drive.KV("CLICKTOP", "tag=" + tag + " name=" + name + " screen=" + Drive.V(c)
                + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0")
                + " raycastTop=\"" + Drive.RaycastTop(c) + "\" " + diag);
            QueueUiClick(c, name);
        }

        /// <summary>
        /// Real-mouse click on a dialog OPTION button identified by its index.
        /// <para>
        /// !!! This file is ASCII-only (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI),
        /// so option buttons are addressed by index and the INDEX SEMANTICS are read out of the
        /// live dialog args at run time -- exactly the order `NpcDialog.Build` wrote them:
        /// 0 = close, then the quest action (accept or turn in) if present, then the shop entry.
        /// The labels that really sit on those buttons are logged (`UI-OPTION` line) so the
        /// index &lt;-&gt; text mapping is auditable from the frozen log alone.
        /// </para>
        /// </summary>
        private void ClickOptionByIndexRealMouse(int index, string tag)
        {
            if (index < 0) { Drive.Warn("UI-OPTION-INDEX " + index + " tag=" + tag + " -> click skipped"); return; }
            var name = "Option" + index;
            string diag;
            var b = Drive.FindButton(name, out diag);
            Drive.KV("UI-OPTION", "tag=" + tag + " index=" + index + " node=" + name + " " + OptionLine() + " " + diag);
            if (b == null)
            {
                Drive.Warn("UI-OPTION-MISS node=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Drive.Click(name);
                return;
            }
            var rt = b.transform as RectTransform;
            Vector2 lo, hi, c;
            if (!Drive.ScreenRect(rt, out lo, out hi, out c)) { Drive.Warn("UI-NORECT " + name); return; }
            Drive.KV("CLICKTOP", "tag=" + tag + " index=" + index + " node=" + name
                + " screen=" + Drive.V(c)
                + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0")
                + " raycastTop=\"" + Drive.RaycastTop(c) + "\"");
            QueueUiClick(c, name);
        }

        private object DialogArgs()
        {
            var dlg = Drive.Dialog();
            return dlg != null ? Drive.Field(dlg, "_dialog") : null;
        }

        private static bool Truthy(object o) { return o is bool && (bool)o; }

        /// <summary>`options[i] = "label" ...` plus the flags the index semantics depend on.</summary>
        private string OptionLine()
        {
            var a = DialogArgs();
            if (a == null) return "(no dialog args)";
            var list = Drive.Field(a, "options") as System.Collections.IList;
            var sb = new StringBuilder();
            sb.Append("options=");
            if (list != null)
            {
                sb.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(i).Append(":\"").Append(list[i]).Append('"');
                }
                sb.Append(']');
            }
            sb.Append(" canAcceptQuest=").Append(Truthy(Drive.Field(a, "canAcceptQuest")) ? 1 : 0)
              .Append(" canTurnInQuest=").Append(Truthy(Drive.Field(a, "canTurnInQuest")) ? 1 : 0)
              .Append(" hasShop=").Append(Truthy(Drive.Field(a, "hasShop")) ? 1 : 0);
            return sb.ToString();
        }

        /// <summary>Index of the quest action button: 1 when present (close is always 0), else -1.</summary>
        private int QuestOptionIndex()
        {
            var a = DialogArgs();
            if (a == null) return -1;
            var quest = Truthy(Drive.Field(a, "canAcceptQuest")) || Truthy(Drive.Field(a, "canTurnInQuest"));
            return quest ? 1 : -1;
        }

        /// <summary>Index of the shop entry button: 2 when a quest action is present, else 1.</summary>
        private int ShopOptionIndex()
        {
            var a = DialogArgs();
            if (a == null) return -1;
            if (!Truthy(Drive.Field(a, "hasShop"))) return -1;
            var quest = Truthy(Drive.Field(a, "canAcceptQuest")) || Truthy(Drive.Field(a, "canTurnInQuest"));
            return quest ? 2 : 1;
        }

        // ================================================================ ground click =====
        private void BeginGroundClick(Vector2Int grid)
        {
            _ck = new GroundClick { Grid = grid, Phase = 0, FramesHere = 0 };
            _ckStartedAt = Time.unscaledTime;
            Drive.KV("CK-BEGIN", "want=" + Drive.Grid(grid) + " tileKind=" + Drive.TileKindAt(grid)
                + " walkable=" + (Drive.Walkable(grid) ? 1 : 0)
                + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
        }

        /// <summary>0 = running, 1 = done, -1 = gave up (fallback used).</summary>
        private int TickGroundClick(float budget)
        {
            var c = _ck;
            if (c == null) return 1;
            var cam = Camera.main;
            if (cam == null) { Drive.Warn("CK-ERR Camera.main null"); return -1; }
            c.FramesHere++;

            switch (c.Phase)
            {
                case 0:
                    {
                        var sp = cam.WorldToScreenPoint(Iso.GridToWorld(c.Grid));
                        c.Pos = new Vector2(sp.x, c.FlipY ? Screen.height - sp.y : sp.y);
                        Drive.MouseState(c.Pos, false);
                        Drive.KV("CK-POS", "want=" + Drive.Grid(c.Grid) + " worldScreen=" + Drive.V(sp)
                            + " injected=" + Drive.V(c.Pos) + " flipY=" + (c.FlipY ? 1 : 0)
                            + " screen=" + Screen.width + "x" + Screen.height
                            + " camPos=" + Drive.World(cam.transform.position)
                            + " ortho=" + cam.orthographicSize.ToString("0.###"));
                        c.Phase = 1;
                        c.FramesHere = 0;
                        return 0;
                    }
                case 1:
                    if (c.FramesHere < 3) return 0;
                    {
                        var got = Iso.ScreenToGrid(cam, Drive.MousePosNow());
                        c.Projected = got == c.Grid;
                        Drive.KV("CK-PROJ", "want=" + Drive.Grid(c.Grid) + " got=" + Drive.Grid(got)
                            + " ok=" + (c.Projected ? 1 : 0) + " flipY=" + (c.FlipY ? 1 : 0));
                        if (c.Projected) { c.Phase = 2; c.FramesHere = 0; return 0; }
                        if (!c.FlipTried)
                        {
                            c.FlipTried = true;
                            c.FlipY = !c.FlipY;
                            Drive.Warn("CK-CALIB projection mismatch -> retry with flipped Y (flipY=" + (c.FlipY ? 1 : 0) + ")");
                            c.Phase = 0;
                            c.FramesHere = 0;
                            return 0;
                        }
                        Drive.Warn("CK-PROJ-FAIL injected point never projects onto the wanted cell (both Y conventions tried)");
                        return FallbackGroundClick(c, "projection-mismatch");
                    }
                case 2:
                    if (c.FramesHere < 2) return 0;
                    c.MoveBefore = Drive.MoveCmds;
                    c.UiTop = Drive.RaycastTop(c.Pos);
                    Drive.MouseState(c.Pos, true);
                    Drive.KV("CK-DOWN", "want=" + Drive.Grid(c.Grid) + " injected=" + Drive.V(c.Pos)
                        + " raycastTop=\"" + c.UiTop + "\"" + " moveCmdsBefore=" + c.MoveBefore
                        + " frame=" + Time.frameCount);
                    c.Phase = 3;
                    c.FramesHere = 0;
                    return 0;
                case 3:
                    if (c.FramesHere < 2) return 0;
                    Drive.MouseState(c.Pos, false);
                    Drive.KV("CK-UP", "want=" + Drive.Grid(c.Grid) + " frame=" + Time.frameCount);
                    c.Phase = 4;
                    c.FramesHere = 0;
                    return 0;
                case 4:
                    if (c.FramesHere < 3) return 0;
                    c.MoveAfter = Drive.MoveCmds;
                    c.Result = "clicked";
                    Drive.KV("CK-DONE", "want=" + Drive.Grid(c.Grid)
                        + " walkable=" + (Drive.Walkable(c.Grid) ? 1 : 0)
                        + " tileKind=" + Drive.TileKindAt(c.Grid)
                        + " moveCmdDelta=" + (c.MoveAfter - c.MoveBefore)
                        + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                        + " elapsed=" + (Time.unscaledTime - _ckStartedAt).ToString("0.00"));
                    return 1;
            }
            if (Time.unscaledTime - _ckStartedAt > budget)
            {
                Drive.Warn("CK-TIMEOUT want=" + Drive.Grid(c.Grid) + " phase=" + c.Phase);
                return FallbackGroundClick(c, "timeout");
            }
            return 0;
        }

        private int FallbackGroundClick(GroundClick c, string why)
        {
            c.Fallback = true;
            c.MoveBefore = Drive.MoveCmds;
            Drive.HandlePrimaryClickFallback(c.Grid);
            c.MoveAfter = Drive.MoveCmds;
            c.Result = "fallback:" + why;
            Drive.KV("CK-FALLBACK", "want=" + Drive.Grid(c.Grid) + " why=" + why
                + " via=PlayerModule.HandlePrimaryClick(reflection)"
                + " moveCmdDelta=" + (c.MoveAfter - c.MoveBefore)
                + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                + " note=the real-mouse chain did NOT deliver this click");
            return -1;
        }

        // ================================================================ char create =====
        private bool TransitionRunning()
        {
            var p = Drive.CharCreate();
            if (p == null) return false;
            var v = Drive.Field(p, "_transitionSlot");
            return v is int && (int)v >= 0;
        }

        /// <summary>
        /// True when the transition is over.  ⛔ The click is delivered over a few frames, so on the
        /// frame right after it the transition has NOT started yet -- "not running" must therefore not
        /// be read as "finished" (that mistake let the Confirm click fire mid-transition once).
        /// </summary>
        private bool WaitTransitionEnd(string retryName)
        {
            if (TransitionRunning())
            {
                _transSeen = true;
                return Elapsed(6f);
            }
            if (!_transSeen)
            {
                // The injected click spans move -> down -> up (4 frames), so the transition is NOT
                // running yet on the frames right after it.  Retrying inside that window clicks the
                // hotspot TWICE, and a second click on an already selected class DESELECTS it
                // (measured: classIndex went 0 -> -1 and Confirm stayed disabled).
                if (!Elapsed(0.8f)) return false;
                if (!_hotspotRetried)
                {
                    _hotspotRetried = true;
                    var ci = Drive.Field(Drive.CharCreate(), "_classIndex");
                    var selected = ci is int && (int)ci >= 0;
                    if (selected)
                    {
                        Drive.Warn("CC-TRANS-RETRYSKIP " + retryName + ": class already selected (classIndex="
                            + ci + ") -> NOT clicking again (a second click would deselect it)");
                        return true;
                    }
                    Drive.Warn("CC-TRANS-RETRY " + retryName + ": no transition observed -> legacy click");
                    Drive.Click(retryName);
                    return false;
                }
                if (!Elapsed(2.5f)) return false;
                Drive.Warn("CC-TRANS-NONE " + retryName + ": no transition observed at all (proceeding)");
            }
            return true;
        }

        private void LogPortrait(int n, string tile, int slot, string label)
        {
            var p = Drive.CharCreate();
            if (p == null) { Drive.KV("PORTRAIT", "n=" + n + " slot=" + slot + " (no panel)"); return; }
            var arr = Drive.Field(p, "_portrait") as Image[];
            var img = arr != null && slot >= 0 && slot < arr.Length ? arr[slot] : null;
            var shown = Drive.Field(p, "_shownState") as int[];
            var shownV = shown != null && slot < shown.Length ? shown[slot] : -99;

            var sprite = "(no-sprite)";
            var nativeW = -1;
            var nativeH = -1;
            var rectW = -1f;
            var rectH = -1f;
            var preserve = -1;
            if (img != null)
            {
                preserve = img.preserveAspect ? 1 : 0;
                rectW = img.rectTransform.sizeDelta.x;
                rectH = img.rectTransform.sizeDelta.y;
                if (img.sprite != null)
                {
                    sprite = img.sprite.name;
                    nativeW = Mathf.RoundToInt(img.sprite.rect.width);
                    nativeH = Mathf.RoundToInt(img.sprite.rect.height);
                }
            }
            Drive.KV("PORTRAIT", "n=" + n + " which=" + label + " slot=" + slot
                + " shownState=" + shownV + " classIndex=" + Drive.Fmt(Drive.Field(p, "_classIndex"))
                + " transSlot=" + Drive.Fmt(Drive.Field(p, "_transitionSlot"))
                + " code=" + Drive.Fmt(Drive.Field(p, "_transitionCode"))
                + " frame=" + Drive.Fmt(Drive.Field(p, "_transitionFrame"))
                + "/" + Drive.Fmt(Drive.Field(p, "_transitionFrameCount"))
                + " elapsed=" + Drive.Fmt(Drive.Field(p, "_transitionElapsed"))
                + " sprite=" + sprite + " native=" + nativeW + "x" + nativeH
                + " rect=" + rectW.ToString("0.0") + "x" + rectH.ToString("0.0")
                + " nativeAspect=" + (nativeH > 0 ? ((float)nativeW / nativeH).ToString("0.0000") : "-")
                + " rectAspect=" + (rectH > 0f ? (rectW / rectH).ToString("0.0000") : "-")
                + " ratioW=" + (nativeW > 0 ? (rectW / nativeW).ToString("0.000") : "-")
                + " ratioH=" + (nativeH > 0 ? (rectH / nativeH).ToString("0.000") : "-")
                + " preserveAspect=" + preserve);
            if (img != null) Drive.LogCrop(n, tile, "Portrait", img.rectTransform);
        }

        /// <summary>Per-frame trace while a transition plays: rect vs that frame's native size.</summary>
        private void TickTransition()
        {
            if (!_transTracing) return;
            var p = Drive.CharCreate();
            if (p == null) return;
            var f = Drive.Field(p, "_transitionFrame");
            if (!(f is int)) return;
            var fi = (int)f;
            if (fi == _transFrameSeen) return;
            _transFrameSeen = fi;
            var slotV = Drive.Field(p, "_transitionSlot");
            var slot = slotV is int ? (int)slotV : -1;
            var arr = Drive.Field(p, "_portrait") as Image[];
            if (slot >= 0 && arr != null && slot < arr.Length && arr[slot] != null)
            {
                var img = arr[slot];
                var w = img.sprite != null ? Mathf.RoundToInt(img.sprite.rect.width) : -1;
                var h = img.sprite != null ? Mathf.RoundToInt(img.sprite.rect.height) : -1;
                var rw = img.rectTransform.sizeDelta.x;
                var rh = img.rectTransform.sizeDelta.y;
                Drive.KV("PFRAME", "slot=" + slot + " frame=" + fi
                    + " sprite=" + (img.sprite != null ? img.sprite.name : "(null)")
                    + " native=" + w + "x" + h
                    + " rect=" + rw.ToString("0.0") + "x" + rh.ToString("0.0")
                    + " ratioW=" + (w > 0 ? (rw / w).ToString("0.000") : "-")
                    + " ratioH=" + (h > 0 ? (rh / h).ToString("0.000") : "-")
                    + " rectAspect=" + (rh > 0f ? (rw / rh).ToString("0.0000") : "-")
                    + " nativeAspect=" + (h > 0 ? ((float)w / h).ToString("0.0000") : "-"));
            }
            else
            {
                Drive.KV("PFRAME", "slot=" + slot + " frame=" + fi + " (no image)");
            }
        }

        private string CcLine()
        {
            var p = Drive.CharCreate();
            if (p == null) return "(no-panel)";
            return "classIndex=" + Drive.Fmt(Drive.Field(p, "_classIndex"))
                + " hoverSlot=" + Drive.Fmt(Drive.Field(p, "_hoverSlot"))
                + " nameBuffer=\"" + Drive.Fmt(Drive.Field(p, "_nameBuffer")) + "\""
                + " nameCaret=" + Drive.Fmt(Drive.Field(p, "_nameCaret"))
                + " nameEditing=" + Drive.Fmt(Drive.Field(p, "_nameEditing"))
                + " transSlot=" + Drive.Fmt(Drive.Field(p, "_transitionSlot"))
                + " transFrame=" + Drive.Fmt(Drive.Field(p, "_transitionFrame"))
                + " transCount=" + Drive.Fmt(Drive.Field(p, "_transitionFrameCount"));
        }

        private void LogName(string where)
        {
            var p = Drive.CharCreate();
            if (p == null) { Drive.KV("NAME", "where=" + where + " (no panel)"); return; }
            var buf = Drive.Fmt(Drive.Field(p, "_nameBuffer"));
            var caret = Drive.Fmt(Drive.Field(p, "_nameCaret"));
            var input = Drive.Field(p, "_nameInput") as InputField;
            var shown = "(no-input)";
            var labelText = "(no-label)";
            var screen = "";
            var display = "(?)";
            if (input != null)
            {
                shown = input.text;
                if (input.textComponent != null) labelText = input.textComponent.text;
                Vector2 lo, hi, c;
                if (Drive.ScreenRect(input.textComponent != null ? input.textComponent.rectTransform : null,
                        out lo, out hi, out c))
                    screen = " screen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                             + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0");
            }
            try
            {
                var t = Drive.FindType("Diablo2.UI.CharCreatePanel");
                if (t != null)
                {
                    var m = t.GetMethod("DisplayName", BindingFlags.Public | BindingFlags.Static);
                    if (m != null)
                    {
                        int ci;
                        var c2 = int.TryParse(caret, out ci) ? ci : 0;
                        display = Drive.Fmt(m.Invoke(null, new object[] { buf, c2 }));
                    }
                }
            }
            catch { }
            Drive.KV("NAME", "where=" + where + " typed=\"" + _typeStr + "\" buffer=\"" + buf + "\""
                + " digitsInBuffer=" + (HasDigit(buf) ? 1 : 0)
                + " visible_input=\"" + shown
                + "\" visible_label=\"" + labelText + "\" display=\"" + display + "\""
                + " caret=" + caret + " maxLen=15" + screen);
        }

        // ================================================================ walk helpers =====
        /// <summary>Pick a spot from which a long straight walk exists, then start walking it.</summary>
        private bool HopToWalkSpot()
        {
            var map = Drive.Map();
            var p = Drive.Player();
            if (map == null || p == null) return false;

            var centre = new Vector2Int(map.Width / 2, map.Height / 2);
            var bestFrom = new Vector2Int(int.MinValue, int.MinValue);
            var bestTo = new Vector2Int(int.MinValue, int.MinValue);
            var bestLen = 0;
            var lens = new[] { 12, 10, 8, 6, 5, 4 };
            for (var li = 0; li < lens.Length && bestTo.x == int.MinValue; li++)
            {
                var len = lens[li];
                for (var rad = 0; rad <= 24 && bestTo.x == int.MinValue; rad += 2)
                {
                    for (var dx = -rad; dx <= rad && bestTo.x == int.MinValue; dx += 2)
                    {
                        for (var dy = -rad; dy <= rad && bestTo.x == int.MinValue; dy += 2)
                        {
                            var c = new Vector2Int(centre.x + dx, centre.y + dy);
                            if (!map.InBounds(c) || !map.Walkable(c)) continue;
                            // straight run along -y (screen "north"); a straight path keeps the
                            // per-frame displacement comparable (a corner would look like a jump)
                            var sign = 1;
                            var ok = true;
                            for (var i = 1; i <= len; i++)
                            {
                                var g = new Vector2Int(c.x, c.y - sign * i);
                                if (!map.Walkable(g) || map.TileAt(g) == TileKind.Exit) { ok = false; break; }
                            }
                            if (!ok) continue;
                            bestFrom = c;
                            bestTo = new Vector2Int(c.x, c.y - sign * len);
                            bestLen = len;
                        }
                    }
                }
            }
            if (bestTo.x == int.MinValue)
            {
                Drive.Warn("A9-SPOT none found (no straight run of 4..12 cells)");
                return false;
            }

            Hop(bestFrom, "a9-walk-from");
            _a9Target = bestTo;
            _a9Dir = Iso.DirectionTo(bestFrom, bestTo);
            _a9KeepWalking = 0;
            var pl = Drive.Player();
            pl.MoveTo(bestTo);
            _a9LastFrame = -9999;
            Drive.KV("A9-SPOT", "from=" + Drive.Grid(bestFrom) + " to=" + Drive.Grid(bestTo)
                + " cells=" + bestLen + " dir=" + _a9Dir + " running=" + (pl.IsRunning ? 1 : 0)
                + " moveSpeed=" + Drive.Fmt(Drive.Prop(pl, "MoveSpeed"))
                + " expectedCellsPerSec=3.0 (run) 1.4 (walk)");
            return true;
        }

        /// <summary>Keep the A9 walk going (the trace must span one continuous walk).</summary>
        private void KeepWalkingA9()
        {
            if (Drive.PlayerMoving()) return;
            if (_a9KeepWalking >= 4) return;
            _a9KeepWalking++;
            Drive.Player().MoveTo(_a9Target);
            Drive.KV("A9-REISSUE", "n=" + _a9KeepWalking + " target=" + Drive.Grid(_a9Target)
                + " grid=" + Drive.Grid(Drive.PlayerGrid()));
        }

        /// <summary>Teleport next to Akara (inside GameConst.TalkRange = 2.4 cells).</summary>
        private bool HopNearAkara()
        {
            var npc = Drive.Npc();
            if (npc == null) { Drive.Warn("A10 no npc module"); return false; }
            NpcDef akara = null;
            foreach (var d in npc.All)
            {
                if (d.id == (int)NpcId.Akara) { akara = d; break; }
            }
            if (akara == null) { Drive.Warn("A10 Akara not in npc list"); return false; }
            _akara = new Vector2Int(akara.gridX, akara.gridY);

            var best = new Vector2Int(int.MinValue, int.MinValue);
            var bestD = float.MaxValue;
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(_akara.x + dx, _akara.y + dy);
                    if (!Drive.Walkable(g)) continue;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 2.3f) continue;
                    if (d < bestD) { bestD = d; best = g; }
                }
            }
            if (best.x == int.MinValue) { Drive.Warn("A10 no walkable cell within TalkRange of " + Drive.Grid(_akara)); return false; }
            Drive.KV("A10-SPOT", "akara=" + Drive.Grid(_akara) + " standAt=" + Drive.Grid(best)
                + " dist=" + bestD.ToString("0.000") + " talkRange=2.4"
                + " akaraCellKind=" + Drive.TileKindAt(_akara)
                + " akaraWalkable=" + (Drive.Walkable(_akara) ? 1 : 0)
                + " standKind=" + Drive.TileKindAt(best));
            Hop(best, "a10-near-akara");
            return true;
        }

        // ================================================================ probes ===========
        private void ProbeTown(string tag)
        {
            var map = Drive.Map();
            var p = Drive.Player();
            var cam = Camera.main;
            if (map == null) { Drive.KV("TOWN", "tag=" + tag + " (no map)"); return; }
            Drive.KV("TOWN", "tag=" + tag + " area=" + map.Area + " map=" + map.Width + "x" + map.Height
                + " seed=" + map.Seed + " spawn=" + Drive.Grid(map.SpawnPoint)
                + " walkable=" + map.WalkableCount + " blocked=" + map.BlockedCount
                + " exits=" + ExitsText(map) + " npcPoints=" + map.NpcPoints.Count
                + " playerGrid=" + (p != null ? Drive.Grid(p.Grid) : "-")
                + " running=" + (p != null && p.IsRunning ? 1 : 0));
            Drive.KV("OSIZE", "tag=" + tag
                + " rigValue=" + Drive.RigOrtho().ToString("0.###")
                + " camValue=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " visibleWorldH=" + (cam != null ? (2f * cam.orthographicSize).ToString("0.###") : "-")
                + " visibleWorldW=" + (cam != null ? (2f * cam.orthographicSize * cam.aspect).ToString("0.###") : "-")
                + " pxPerWorldUnit=" + (cam != null ? (Screen.height / (2f * cam.orthographicSize)).ToString("0.0") : "-")
                + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-"));
        }

        private static string ExitsText(Diablo2.Module.IMapModule map)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < map.Exits.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Drive.Grid(map.Exits[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        /// <summary>Where the bridge / river cells land on screen (helps read the A2 framing).</summary>
        private void LogCellScreens(string tag)
        {
            var cam = Camera.main;
            if (cam == null) return;
            var sb = new StringBuilder();
            for (var x = 46; x <= 55; x++)
            {
                AppendCell(sb, cam, "bridge", x, 25);
                AppendCell(sb, cam, "bridge", x, 27);
            }
            for (var x = 47; x <= 54; x += 3)
            {
                AppendCell(sb, cam, "water", x, 20);
                AppendCell(sb, cam, "water", x, 24);
                AppendCell(sb, cam, "water", x, 29);
            }
            Drive.KV("CELLSCREEN", "tag=" + tag + " screen=" + Screen.width + "x" + Screen.height
                + " origin=bottom-left" + sb);
        }

        private static void AppendCell(StringBuilder sb, Camera cam, string what, int x, int y)
        {
            var sp = cam.WorldToScreenPoint(Iso.GridToWorld(new Vector2Int(x, y)));
            var inFrame = sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
            sb.Append(' ').Append(what).Append('(').Append(x).Append(',').Append(y).Append(")=")
              .Append(sp.x.ToString("0")).Append('@').Append(sp.y.ToString("0"))
              .Append(inFrame ? "" : "(off)");
        }

        private string NpcStateLine()
        {
            var npc = Drive.Npc();
            var cur = "(n/a)";
            if (npc != null)
            {
                var f = npc.GetType().GetField("_currentNpcId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) cur = Drive.Fmt(f.GetValue(npc));
            }
            var q = Drive.Quest();
            return "currentNpcId=" + cur
                + " denState=" + (q != null ? q.DenOfEvil.ToString() : "-")
                + " denRemaining=" + (q != null ? q.DenRemaining.ToString() : "-")
                + " canTurnIn=" + (q != null ? (q.CanTurnInDen ? 1 : 0).ToString() : "-")
                + " dialogOpen=" + (DialogIsOpen() ? 1 : 0)
                + " shopOpen=" + (ShopIsOpen() ? 1 : 0);
        }

        private void LogNpcPoints()
        {
            var npc = Drive.Npc();
            if (npc == null) { Drive.KV("NPCS", "(no npc module)"); return; }
            var sb = new StringBuilder();
            foreach (var d in npc.All)
            {
                sb.Append('|').Append(d.id).Append('=').Append(d.name)
                  .Append('@').Append(Drive.Grid(new Vector2Int(d.gridX, d.gridY)))
                  .Append(" questGiver=").Append(d.isQuestGiver ? 1 : 0)
                  .Append(" shop=").Append(d.hasShop ? 1 : 0);
            }
            Drive.KV("NPCS", "count=" + npc.All.Count + " " + NpcStateLine() + " " + sb);
        }

        private void LogDialog(string tag)
        {
            var dlg = Drive.Dialog();
            if (dlg == null) { Drive.KV("DIALOG", "tag=" + tag + " open=0"); return; }
            var args = Drive.Field(dlg, "_dialog");
            var opts = "(?)";
            var text = "(?)";
            var canA = "(?)";
            var canT = "(?)";
            var npcName = "(?)";
            if (args != null)
            {
                var list = Drive.Field(args, "options") as System.Collections.IList;
                var sb = new StringBuilder();
                if (list != null)
                {
                    sb.Append('[');
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(" / ");
                        sb.Append(i).Append(":\"").Append(list[i]).Append('"');
                    }
                    sb.Append(']');
                }
                opts = sb.ToString();
                text = Drive.Fmt(Drive.Field(args, "text"));
                npcName = Drive.Fmt(Drive.Field(args, "npcName"));
                canA = Drive.Fmt(Drive.Field(args, "canAcceptQuest"));
                canT = Drive.Fmt(Drive.Field(args, "canTurnInQuest"));
            }
            Drive.KV("DIALOG", "tag=" + tag + " open=1 npcName=\"" + npcName + "\""
                + " canAcceptQuest=" + canA + " canTurnInQuest=" + canT
                + " options=" + opts
                + " text=\"" + text + "\""
                + " layer=" + dlg.Layer + " " + NpcStateLine());
        }

        private void LogShop(string tag)
        {
            var shop = Drive.Shop();
            if (shop == null) { Drive.KV("SHOP", "tag=" + tag + " open=0"); return; }
            var title = Drive.Field(shop, "_title");
            var hint = Drive.Field(shop, "_hint");
            // D2Label's own `text` property is the authoritative value the panel wrote;
            // the uGUI Text node with the same name is the fallback/display node.
            var titleText = Drive.Fmt(Drive.Prop(title, "text"));
            var hintText = Drive.Fmt(Drive.Prop(hint, "text"));
            var titleScreen = " titleRect=(no-label)";
            var hintScreen = " hintRect=(no-label)";
            var titleRt = Drive.Prop(title, "rectTransform") as RectTransform;
            var hintRt = Drive.Prop(hint, "rectTransform") as RectTransform;
            if (titleRt == null) { var n = FindTextNode(shop.transform, "ShopTitle"); if (n != null) titleRt = n.rectTransform; }
            if (hintRt == null) { var n = FindTextNode(shop.transform, "ShopHint"); if (n != null) hintRt = n.rectTransform; }
            Vector2 lo, hi, c;
            if (Drive.ScreenRect(titleRt, out lo, out hi, out c))
                titleScreen = " titleScreen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                              + " titleRect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0");
            if (Drive.ScreenRect(hintRt, out lo, out hi, out c))
                hintScreen = " hintScreen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                             + " hintRect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0");
            var tnode = FindTextNode(shop.transform, "ShopTitle");
            var hnode = FindTextNode(shop.transform, "ShopHint");
            Drive.KV("SHOP", "tag=" + tag + " open=1 layer=" + shop.Layer
                + " title=\"" + titleText + "\" hint=\"" + hintText + "\""
                + " titleLabelNull=" + (title == null ? 1 : 0) + " hintLabelNull=" + (hint == null ? 1 : 0)
                + titleScreen + hintScreen
                + " titleNodeText=\"" + (tnode != null ? tnode.text : "(no-node)") + "\""
                + " hintNodeText=\"" + (hnode != null ? hnode.text : "(no-node)") + "\""
                + " " + NpcStateLine());
            if (titleRt != null) Drive.LogCrop(90, T15, "ShopTitle", titleRt);
            if (hintRt != null) Drive.LogCrop(91, T15, "ShopHint", hintRt);
            Drive.DumpTextsUnder("shop", shop.transform, 30);
            // hint/title canvas-space y from the layout table (the numbers the fix landed on)
            Drive.KV("SHOPYTABLE", "tag=" + tag
                + " shopTitlePosY=" + Drive.Fmt(Drive.StaticField("Diablo2.UI.UiLayoutGame", "ShopTitlePos"))
                + " shopHintPosY=" + Drive.Fmt(Drive.StaticField("Diablo2.UI.UiLayoutGame", "ShopHintPos"))
                + " shopTabBottomY=" + Drive.Fmt(Drive.StaticField("Diablo2.UI.UiLayoutGame", "ShopTabBottomY"))
                + " shopGridTopY=" + Drive.Fmt(Drive.StaticField("Diablo2.UI.UiLayoutGame", "ShopGridTopY")));
        }

        private static Text FindTextNode(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (t != null && t.gameObject.name == name) return t;
            }
            return null;
        }

        private void LogQuest(string tag)
        {
            var q = Drive.Quest();
            if (q == null) { Drive.KV("QUEST", "tag=" + tag + " (no quest module)"); return; }
            var sb = new StringBuilder();
            foreach (var dto in q.Quests)
            {
                sb.Append('|').Append(dto.questId).Append('=').Append(dto.state)
                  .Append(" name=\"").Append(dto.name).Append('"')
                  .Append(" progress=").Append(dto.progress).Append('/').Append(dto.required)
                  .Append(" objective=\"").Append(dto.objective).Append('"');
            }
            Drive.KV("QUEST", "tag=" + tag + " denState=" + q.DenOfEvil
                + " denRemaining=" + q.DenRemaining + " canTurnInDen=" + (q.CanTurnInDen ? 1 : 0)
                + " quests=" + sb);
        }

        // ================================================================ tour =============
        private void Step()
        {
            switch (_step)
            {
                // ============ 0: pacing (first 60 frames) =================================
                case 0:
                    if (!_pacingLogged) return;
                    Drive.KV("PHASE", "pacing done -> boot chain");
                    Next();
                    return;

                // ============ 1: boot -> menu ============================================
                case 1:
                    if (BootOpen())
                    {
                        if (!Elapsed(1.0f)) return;
                        Drive.KV("BOOT", "scene=" + SceneName() + " fsm=" + Fsm()
                            + " focused=" + (Application.isFocused ? 1 : 0)
                            + " runInBackground=" + (Application.runInBackground ? 1 : 0));
                        Drive.KeyDown("space");
                        Next();
                        return;
                    }
                    if (MenuOpen()) { Next(); return; }
                    TimeoutStep("boot", 40f);
                    return;

                case 2:
                    if (!MenuOpen()) { TimeoutStep("menu", 40f); return; }
                    if (!Elapsed(1.2f)) return;      // the menu art loads asynchronously
                    Drive.KV("MENU", "scene=" + SceneName() + " fsm=" + Fsm());
                    Drive.KeyUp();
                    string mdiag;
                    var single = Drive.FindButton("Single", out mdiag);
                    Drive.KV("MENU-CLICK", "target=Single found=" + (single != null ? 1 : 0) + " " + mdiag);
                    Drive.Click("Single");
                    Next();
                    return;

                // ============ 2: roster -> new hero ======================================
                case 3:
                    if (SelectOpen())
                    {
                        Drive.KV("CHARSELECT", "open=1");
                        Drive.Click("Create");
                        Next();
                        return;
                    }
                    if (CreateOpen()) { Next(); return; }
                    TimeoutStep("charselect", 25f);
                    return;

                case 4:
                    if (!CreateOpen()) { TimeoutStep("charcreate", 25f); return; }
                    if (_ccOpenedAt <= 0f) { _ccOpenedAt = Time.unscaledTime; Drive.KV("CC-OPEN", CcLine()); }
                    if (!Elapsed(2.5f)) return;              // portrait/transition preload is async
                    Drive.KV("CC-READY", CcLine());
                    Drive.DumpTextsUnder("charcreate",
                        Drive.CharCreate() != null ? Drive.CharCreate().transform : null, 20);
                    Next();
                    return;

                // ============ 3: A5 baseline (both classes untouched) ====================
                case 5:
                    LogPortrait(1, T05A, 0, "amazon-idle-baseline");
                    Drive.BeginShot(1, T05A, "screen");
                    Next();
                    return;
                case 6:
                    WaitShot(1, T05A, "screen", "a5a", 20f);
                    return;
                case 7:
                    LogPortrait(2, T05B, 2, "barbarian-idle-baseline");
                    Drive.BeginShot(2, T05B, "screen");
                    Next();
                    return;
                case 8:
                    WaitShot(2, T05B, "screen", "a5b", 20f);
                    return;

                // ============ 4: A6/A7 amazon ============================================
                case 9:
                    if (!Elapsed(0.4f)) return;
                    _transFrameSeen = -1;
                    _transTracing = true;
                    _transStart = Time.unscaledTime;
                    _hotspotRetried = false;
                    _transSeen = false;
                    Drive.KV("CC-CLICK", "target=SpotAmazon (real mouse) before=" + CcLine());
                    ClickNamedRealMouse("SpotAmazon", "a6a");
                    Next();
                    return;
                case 10:
                    if (TransitionRunning()) _transSeen = true;
                    if (!Elapsed(0.95f)) return;
                    LogPortrait(3, T06A, 0, "amazon-transition-mid");
                    Drive.BeginShot(3, T06A, "screen");
                    Next();
                    return;
                case 11:
                    WaitShot(3, T06A, "screen", "a6a", 20f);
                    return;
                case 12:
                    if (!WaitTransitionEnd("SpotAmazon")) return;
                    Drive.KV("CC-TRANS-END", "amazon after=" + CcLine());
                    LogPortrait(4, T07A, 0, "amazon-front-end");
                    Drive.BeginShot(4, T07A, "screen");
                    Next();
                    return;
                case 13:
                    WaitShot(4, T07A, "screen", "a7a", 20f);
                    return;

                // ============ 5: A6/A7 barbarian =========================================
                case 14:
                    if (!Elapsed(0.5f)) return;
                    _transFrameSeen = -1;
                    _transTracing = true;
                    _transStart = Time.unscaledTime;
                    _hotspotRetried = false;
                    _transSeen = false;
                    Drive.KV("CC-CLICK", "target=SpotBarbarian (real mouse) before=" + CcLine());
                    ClickNamedRealMouse("SpotBarbarian", "a6b");
                    Next();
                    return;
                case 15:
                    if (TransitionRunning()) _transSeen = true;
                    if (!Elapsed(0.95f)) return;
                    LogPortrait(5, T06B, 2, "barbarian-transition-mid");
                    Drive.BeginShot(5, T06B, "screen");
                    Next();
                    return;
                case 16:
                    WaitShot(5, T06B, "screen", "a6b", 20f);
                    return;
                case 17:
                    if (!WaitTransitionEnd("SpotBarbarian")) return;
                    Drive.KV("CC-TRANS-END", "barbarian after=" + CcLine());
                    LogPortrait(6, T07B, 2, "barbarian-front-end");
                    Drive.BeginShot(6, T07B, "screen");
                    Next();
                    return;
                case 18:
                    WaitShot(6, T07B, "screen", "a7b", 20f);
                    return;

                // ============ 6: A8 name with digits =====================================
                case 19:
                    if (!Elapsed(0.4f)) return;
                    _transFrameSeen = -1;
                    _transStart = Time.unscaledTime;
                    _hotspotRetried = false;
                    _transSeen = false;
                    Drive.KV("CC-CLICK", "target=SpotAmazon again (select the new hero) before=" + CcLine());
                    ClickNamedRealMouse("SpotAmazon", "a8");
                    Next();
                    return;
                case 20:
                    if (!WaitTransitionEnd("SpotAmazon")) return;
                    Drive.KV("CC-SELECTED", CcLine());
                    Next();
                    return;
                case 21:
                    {
                        // A8: type a string that CONTAINS DIGITS.  AppFlow refuses duplicate hero
                        // names ("角色名已存在"), so a previous probe run on this machine may already
                        // own "HeroAma9x" -- the driver therefore picks the first candidate that is
                        // still free and says which one it used (no save data is ever deleted).
                        if (!_typePicked)
                        {
                            _typePicked = true;
                            var def = Drive.Fmt(Drive.Field(Drive.CharCreate(), "_nameBuffer"));
                            if (def == "(null)") def = string.Empty;
                            // A8: type a string that CONTAINS DIGITS.  AppFlow refuses duplicate hero
                            // names ("角色名已存在") and a previous probe run on this machine already owns
                            // "HeroAma9x", so the two digits are taken from the clock: the name is both
                            // digit-bearing and new, and nothing in the save dir is ever deleted.
                            var save = Drive.Save();
                            var cands = new List<string>();
                            var stamp = DateTime.Now.Second * 60 + DateTime.Now.Millisecond % 60;
                            for (var i = 0; i < 8; i++)
                            {
                                var n2 = ((stamp / 7) + i * 13) % 100;
                                cands.Add("Ama" + n2.ToString("00") + "x");
                            }
                            cands.Add("Ama9x");                       // keep the task's example last
                            _typeStr = cands[cands.Count - 1];
                            for (var i = 0; i < cands.Count; i++)
                            {
                                var full = def + cands[i];
                                var exists = save != null && save.Exists(full);
                                Drive.KV("TYPE-PICK", "candidate=\"" + cands[i] + "\" fullName=\"" + full
                                    + "\" saveExists=" + (exists ? 1 : 0));
                                if (!exists) { _typeStr = cands[i]; break; }
                            }
                            Drive.KV("TYPE-STR", "typed=\"" + _typeStr + "\" defaultName=\"" + def + "\""
                                + " digitsPresent=" + (HasDigit(_typeStr) ? 1 : 0)
                                + " saveModuleAvailable=" + (save != null ? 1 : 0));
                        }
                        if (_typeIdx < _typeStr.Length)
                        {
                            var c = _typeStr[_typeIdx];
                            var queued = Drive.TextChar(c);
                            Drive.KV("TYPE", "char[" + _typeIdx + "]='" + c + "' queued=" + (queued ? 1 : 0)
                                + " via=InputSystem.QueueTextEvent -> Keyboard.onTextInput");
                            _typeIdx++;
                            return;
                        }
                        Next();
                        return;
                    }
                case 22:
                    if (!Elapsed(0.5f)) return;
                    LogName("after-typing");
                    LogPortrait(7, T08, 0, "amazon-name-frame");
                    Drive.BeginShot(7, T08, "screen");
                    Next();
                    return;
                case 23:
                    WaitShot(7, T08, "screen", "a8", 20f);
                    return;

                // ============ 7: confirm -> in game ======================================
                case 24:
                    if (!Elapsed(0.4f)) return;
                    _hotspotRetried = false;
                    _newHeroName = Drive.Fmt(Drive.Field(Drive.CharCreate(), "_nameBuffer"));
                    Drive.KV("CC-CONFIRM", "clicking Confirm (real mouse) newHeroName=\"" + _newHeroName
                        + "\" before=" + CcLine());
                    ClickNamedRealMouse("Confirm", "confirm");
                    Next();
                    return;
                case 25:
                    if (HudOpen() || !CreateOpen())          // the panel closed => the creation went through
                    {
                        Drive.KV("CONFIRM-RESULT", "hudOpen=" + (HudOpen() ? 1 : 0) + " fsm=" + Fsm()
                            + " createOpen=" + (CreateOpen() ? 1 : 0));
                        Next();
                        return;
                    }
                    if (!_hotspotRetried && Elapsed(2.5f))
                    {
                        _hotspotRetried = true;
                        Drive.Warn("CONFIRM-RETRY the real-mouse click did not close CharCreate "
                            + "-> legacy ExecuteEvents path");
                        Drive.Click("Confirm");
                    }
                    if (Elapsed(12f))
                    {
                        Drive.KV("CONFIRM-TIMEOUT", "createOpen=1 hudOpen=0 fsm=" + Fsm());
                        Next();
                    }
                    return;
                // NOTE: creating a hero does NOT drop you into the game -- AppFlow reports
                // "创建成功 ... -> 回选角屏" and waits for the roster ENTER of that row.  So this
                // step does BOTH: press ENTER on the new hero's row, then wait for Stage/Town.
                case 26:
                    if (SelectOpen())
                    {
                        if (!_enterClicked)
                        {
                            LogRoster();
                            var idx = FindRowIndexOf(_newHeroName);
                            var row = idx >= 0 ? "Row" + idx : "Row0";
                            Drive.KV("ROSTER-ENTER", "newHero=\"" + _newHeroName + "\" rowIndex=" + idx
                                + " click=\"" + row + "|Enter\"" + (idx < 0 ? " (fallback: first row)" : ""));
                            Drive.Click(row + "|Enter");
                            _enterClicked = true;
                        }
                        else if (Elapsed(20f))
                        {
                            Drive.Warn("ROSTER still open 20s after ENTER (fsm=" + Fsm() + ")");
                        }
                        return;
                    }
                    if (CreateOpen())
                    {
                        // still sitting on the create screen: the confirm was refused (e.g. duplicate
                        // hero name).  Say so and stop instead of burning the whole budget on timeouts.
                        Drive.Warn("CREATE-REFUSED the char-create panel is still open after Confirm "
                            + "(see the [Ui]/[Flow] lines: 创角失败) -> aborting the tour here");
                        Finish("create-refused");
                        return;
                    }
                    if (!HudOpen() || Fsm() != "Stage") { TimeoutStep("stage", 40f); return; }
                    {
                        var m = Drive.Map();
                        if (m == null || m.Area != AreaId.Town)
                        {
                            if (Elapsed(20f))
                            {
                                Drive.Warn("IN-GAME NOT TOWN area=" + (m != null ? m.Area.ToString() : "null"));
                                Next();
                            }
                            return;
                        }
                    }
                    Drive.KV("STAGE", "scene=" + SceneName() + " fsm=" + Fsm() + " " + State());
                    Next();
                    return;

                // ============ 8: A1 whole-level framing (probe ortho ~28) =================
                case 27:
                    if (!Elapsed(2.0f)) return;
                    ProbeTown("a1");
                    Drive.KV("WIDE", "requested ortho=28 (probe only; shipped value is 3.75) rigSetOk="
                        + (Drive.RigSetOrtho(28f) ? 1 : 0));
                    Next();
                    return;
                case 28:
                    if (!Elapsed(0.8f)) return;
                    {
                        var cam = Camera.main;
                        Drive.KV("WIDE-READY", "rigOrtho=" + Drive.RigOrtho().ToString("0.###")
                            + " camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                            + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                            + " visibleWorldH=" + (cam != null ? (2f * cam.orthographicSize).ToString("0.0") : "-")
                            + " visibleWorldW=" + (cam != null ? (2f * cam.orthographicSize * cam.aspect).ToString("0.0") : "-")
                            + " mapWorldW=94.0 mapWorldH=47.0 (56x40 iso, measured)");
                    }
                    Drive.BeginShot(10, T01, "camera");
                    Next();
                    return;
                case 29:
                    WaitShot(10, T01, "camera", "a1-town-wide", 20f);
                    return;
                case 30:
                    Drive.RigSetOrtho(3.75f);
                    Drive.KV("CAM-RESTORED", "rigOrtho=" + Drive.RigOrtho().ToString("0.###")
                        + " camOrtho=" + (Camera.main != null ? Camera.main.orthographicSize.ToString("0.###") : "-")
                        + " expected=3.75");
                    Next();
                    return;

                // ============ 9: A2 default framing at bridge + river ====================
                case 31:
                    if (!HopForBridge())
                    {
                        Drive.Warn("A2A3-SETUP no walkable standing cell with a path to the bridge deck");
                        Finish("no-bridge-stand");
                        return;
                    }
                    Next();
                    return;
                case 32:
                    if (!Elapsed(1.8f)) return;
                    ProbeTown("a2");
                    LogCellScreens("a2");
                    Drive.BeginShot(11, T02, "camera");
                    Next();
                    return;
                case 33:
                    WaitShot(11, T02, "camera", "a2-town-bridge", 20f);
                    return;

                // ============ 10: A3 click the bridge deck ==============================
                case 34:
                    if (!Elapsed(0.4f)) return;
                    BeginGroundClick(_bridgeTarget);
                    Next();
                    return;
                case 35:
                case 36:
                case 37:
                case 38:
                    {
                        var r = TickGroundClick(8f);
                        if (r == 0) return;
                        Drive.KV("A3-CLICKRESULT", "result=" + _ck.Result + " want=" + Drive.Grid(_bridgeTarget)
                            + " moveCmdDelta=" + (_ck.MoveAfter - _ck.MoveBefore)
                            + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                            + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                        Next();
                        return;
                    }
                case 39:
                    if (!Elapsed(0.5f)) return;
                    Drive.KV("A3-T0500", "playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " world=" + Drive.World(Drive.PlayerWorld())
                        + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                        + " sinceClick=" + (Time.unscaledTime - _ckStartedAt).ToString("0.00")
                        + " tileKindHere=" + Drive.TileKindAt(Drive.PlayerGrid()));
                    Drive.BeginShot(12, T03A, "camera");
                    Next();
                    return;
                case 40:
                    WaitShot(12, T03A, "camera", "a3-t0.5", 20f);
                    return;
                case 41:
                    if (!Elapsed(1.0f)) return;
                    Drive.KV("A3-T1500", "playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " world=" + Drive.World(Drive.PlayerWorld())
                        + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                        + " sinceClick=" + (Time.unscaledTime - _ckStartedAt).ToString("0.00")
                        + " tileKindHere=" + Drive.TileKindAt(Drive.PlayerGrid()));
                    Drive.BeginShot(13, T03B, "camera");
                    Next();
                    return;
                case 42:
                    WaitShot(13, T03B, "camera", "a3-t1.5", 20f);
                    return;
                case 43:
                    if (Drive.PlayerMoving() && !Elapsed(10f)) return;
                    Drive.KV("A3-ARRIVED", "playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " want=" + Drive.Grid(_bridgeTarget)
                        + " tileKindHere=" + Drive.TileKindAt(Drive.PlayerGrid())
                        + " walkable=" + (Drive.Walkable(Drive.PlayerGrid()) ? 1 : 0)
                        + " isBridgeDeck=" + (IsBridgeDeck(Drive.PlayerGrid()) ? 1 : 0));
                    Next();
                    return;
                // ============ 11: A4 click the water -> nearest-walkable fallback =======
                case 44:
                    if (!Elapsed(0.4f)) return;
                    if (!PickWaterCell())
                    {
                        Drive.Warn("A4-SETUP no water cell reachable from the bridge -> skipped");
                        Next();
                        return;
                    }
                    BeginGroundClick(_waterTarget);
                    Next();
                    return;
                case 45:
                case 46:
                case 47:
                case 48:
                    {
                        var r = TickGroundClick(8f);
                        if (r == 0) return;
                        Drive.KV("A4-CLICKRESULT", "result=" + _ck.Result + " water=" + Drive.Grid(_waterTarget)
                            + " moveCmdDelta=" + (_ck.MoveAfter - _ck.MoveBefore)
                            + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                            + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                        Next();
                        return;
                    }
                case 49:
                    if (!Elapsed(0.6f)) return;
                    Drive.KV("A4-FRAME", "playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " tileKindHere=" + Drive.TileKindAt(Drive.PlayerGrid())
                        + " world=" + Drive.World(Drive.PlayerWorld())
                        + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                    Drive.BeginShot(14, T04, "camera");
                    Next();
                    return;
                case 50:
                    WaitShot(14, T04, "camera", "a4-water-fallback", 20f);
                    return;
                case 51:
                    if (Drive.PlayerMoving() && !Elapsed(10f)) return;
                    Drive.KV("A4-ARRIVED", "playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " tileKindHere=" + Drive.TileKindAt(Drive.PlayerGrid())
                        + " landedOnWalkable=" + (Drive.Walkable(Drive.PlayerGrid()) ? 1 : 0));
                    Next();
                    return;

                // ============ 12: A9 walk trace (6 frames, one continuous walk) =========
                case 52:
                    if (!Elapsed(0.5f)) return;
                    _a9Shots = 0;
                    _a9Pending = string.Empty;
                    _a9LastFrame = -9999;
                    _a9KeepWalking = 99;                 // no walk spot => no re-issue either
                    if (!HopToWalkSpot())
                    {
                        Drive.Warn("A9 no walk spot -> the walk trace is skipped (6 frames would be static)");
                        _a9Shots = 6;
                        Drive.KV("A9-DONE", "shots=0 reason=no-straight-run");
                        Next();
                        return;
                    }
                    _a9KeepWalking = 0;
                    _a9PrevWorld = Drive.PlayerWorld();
                    _a9CellOk = Drive.CellPos(_a9PrevWorld, out _a9PrevCell);
                    Next();
                    return;
                case 53:
                    {
                        if (_a9Pending.Length > 0)
                        {
                            long b;
                            if (!Drive.ShotReady(_a9Pending, out b)) { KeepWalkingA9(); return; }
                            _shots++;
                            Drive.LogShot("a9-frame", 20 + _a9Shots, _a9Pending, "camera");
                            _a9Pending = string.Empty;
                        }
                        if (_a9Shots >= 6) { Next(); return; }
                        KeepWalkingA9();
                        if (Time.frameCount - _a9LastFrame < 3) return;
                        var w = Drive.PlayerWorld();
                        var vw = Drive.PlayerViewWorld();
                        Vector2 cell;
                        var cellOk = Drive.CellPos(w, out cell);
                        var dw = Vector3.Distance(_a9PrevWorld, w);
                        var dc = cellOk && _a9CellOk ? Vector2.Distance(_a9PrevCell, cell) : -1f;
                        Drive.KV("A9", "n=" + (_a9Shots + 1) + " frame=" + Time.frameCount
                            + " t=" + Time.unscaledTime.ToString("0.000")
                            + " motorWorld=" + Drive.World(w)
                            + " viewWorld=" + Drive.World(vw)
                            + " cell=" + (cellOk ? "(" + cell.x.ToString("0.000") + "," + cell.y.ToString("0.000") + ")" : "(?)")
                            + " dWorldFromPrev=" + dw.ToString("0.0000")
                            + " dCellFromPrev=" + dc.ToString("0.0000")
                            + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                            + " grid=" + Drive.Grid(Drive.PlayerGrid())
                            + " dt=" + Time.unscaledDeltaTime.ToString("0.00000"));
                        _a9PrevWorld = w;
                        if (cellOk) { _a9PrevCell = cell; _a9CellOk = true; }
                        _a9LastFrame = Time.frameCount;
                        _a9Shots++;
                        var tile = T09 + _a9Shots + ".png";
                        Drive.BeginShot(20 + _a9Shots, tile, "camera");
                        _a9Pending = tile;
                        return;
                    }
                case 54:
                    Drive.KV("A9-DONE", "shots=" + _a9Shots + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
                    Next();
                    return;

                // ============ 13: A10 Akara dialog (not started) =========================
                case 55:
                    if (!Elapsed(0.5f)) return;
                    if (!HopNearAkara()) { Drive.Warn("A10 no akara spot"); Next(); return; }
                    Next();
                    return;
                case 56:
                    if (!Elapsed(1.6f)) return;
                    LogNpcPoints();
                    ProbeTown("a10");
                    BeginGroundClick(_akara);
                    Next();
                    return;
                case 57:
                case 58:
                case 59:
                case 60:
                    {
                        var r = TickGroundClick(8f);
                        if (r == 0) return;
                        Drive.KV("A10-CLICKRESULT", "result=" + _ck.Result + " akara=" + Drive.Grid(_akara)
                            + " moveCmdDelta=" + (_ck.MoveAfter - _ck.MoveBefore)
                            + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
                        Next();
                        return;
                    }
                case 61:
                    if (!DialogIsOpen())
                    {
                        if (!Elapsed(8f)) return;
                        Drive.Warn("A10 dialog did not open in 8s -> Npc.Interact fallback (documented)");
                        Drive.Npc().Interact((int)NpcId.Akara);
                        Next();
                        return;
                    }
                    Next();
                    return;
                case 62:
                    if (!Elapsed(0.8f)) return;
                    LogNpcPoints();
                    LogDialog("a10");
                    Drive.BeginShot(15, T10, "screen");
                    Next();
                    return;
                case 63:
                    WaitShot(15, T10, "screen", "a10", 20f);
                    return;

                // ============ 14: A11 accept the quest through the dialog button ========
                case 64:
                    if (!Elapsed(0.4f)) return;
                    {
                        var before = Drive.MoveCmds;
                        var gridBefore = Drive.PlayerGrid();
                        Drive.KV("A11-CLICK", "clicking the quest-news option (real mouse)"
                            + " moveCmdsBefore=" + before + " playerGrid=" + Drive.Grid(gridBefore)
                            + " " + NpcStateLine());
                    }
                    ClickOptionByIndexRealMouse(QuestOptionIndex(), "a11");
                    Next();
                    return;
                case 65:
                    if (!_uiRetriedA11 && Elapsed(1.5f) && !QuestAccepted())
                    {
                        _uiRetriedA11 = true;
                        Drive.Warn("A11-CLICK-RETRY the real-mouse click did not accept the quest "
                            + "-> legacy ExecuteEvents path (see [Ui]/[Npc] lines for which one ran)");
                        Drive.Click("Option" + QuestOptionIndex());
                        return;
                    }
                    if (!Elapsed(1.2f)) return;
                    LogQuest("a11-after-accept");
                    LogDialog("a11");
                    Drive.KV("A11-STATE", "moveCmds=" + Drive.MoveCmds + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " " + NpcStateLine());
                    Drive.BeginShot(16, T11, "screen");
                    Next();
                    return;
                case 66:
                    WaitShot(16, T11, "screen", "a11", 20f);
                    return;

                // ============ 15: A12 shop + dialog bar together ========================
                case 67:
                    if (!Elapsed(0.4f)) return;
                    Drive.KV("A12-CLICK", "clicking the trade option (real mouse) moveCmdsBefore="
                        + Drive.MoveCmds + " " + NpcStateLine());
                    ClickOptionByIndexRealMouse(ShopOptionIndex(), "a12");
                    Next();
                    return;
                case 68:
                    if (!_uiRetriedA12 && !ShopIsOpen() && Elapsed(2.0f))
                    {
                        _uiRetriedA12 = true;
                        Drive.Warn("A12-CLICK-RETRY the shop did not open from the real-mouse click "
                            + "-> legacy ExecuteEvents path");
                        Drive.Click("Option" + ShopOptionIndex());
                        return;
                    }
                    if (!ShopIsOpen() && !Elapsed(8f)) return;
                    if (!Elapsed(0.8f)) return;
                    LogShop("a12");
                    Drive.KV("A12-STATE", "uiRetried=" + (_uiRetriedA12 ? 1 : 0)
                        + " shopOpen=" + (ShopIsOpen() ? 1 : 0)
                        + " dialogOpen=" + (DialogIsOpen() ? 1 : 0)
                        + " shopLayer=" + (Drive.Shop() != null ? Drive.Shop().Layer.ToString() : "-")
                        + " dialogLayer=" + (Drive.Dialog() != null ? Drive.Dialog().Layer.ToString() : "-")
                        + " moveCmds=" + Drive.MoveCmds
                        + " " + NpcStateLine());
                    Drive.BeginShot(17, T12, "screen");
                    Next();
                    return;
                case 69:
                    WaitShot(17, T12, "screen", "a12", 20f);
                    return;

                // ============ 16: A15 shop title/hint ===================================
                case 70:
                    if (!Elapsed(0.5f)) return;
                    LogShop("a15");
                    Drive.BeginShot(18, T15, "screen");
                    Next();
                    return;
                case 71:
                    WaitShot(18, T15, "screen", "a15", 20f);
                    return;

                // ============ 17: A13 close the shop, the dialog bar survives ============
                case 72:
                    if (!Elapsed(0.4f)) return;
                    Drive.KV("A13-CLICK", "clicking the shop Close button (real mouse) before=" + NpcStateLine());
                    ClickNamedRealMouse("Close", "a13");
                    Next();
                    return;
                case 73:
                    if (!_uiRetriedA13 && ShopIsOpen() && Elapsed(2.0f))
                    {
                        _uiRetriedA13 = true;
                        Drive.Warn("A13-CLICK-RETRY the shop did not close from the real-mouse click "
                            + "-> legacy ExecuteEvents path");
                        Drive.Click("Close");
                        return;
                    }
                    if (ShopIsOpen() && !Elapsed(8f)) return;
                    if (!Elapsed(0.8f)) return;
                    Drive.KV("A13-STATE", "uiRetried=" + (_uiRetriedA13 ? 1 : 0)
                        + " shopOpen=" + (ShopIsOpen() ? 1 : 0)
                        + " dialogOpen=" + (DialogIsOpen() ? 1 : 0) + " " + NpcStateLine());
                    LogDialog("a13");
                    Drive.BeginShot(19, T13, "screen");
                    Next();
                    return;
                case 74:
                    WaitShot(19, T13, "screen", "a13", 20f);
                    return;

                // ============ 18: A14 a dialog-panel button click moves nobody ==========
                case 75:
                    if (!Elapsed(0.5f)) return;
                    _a14MoveBefore = Drive.MoveCmds;
                    _a14GridBefore = Drive.PlayerGrid();
                    Drive.KV("A14-PRECLICK", "playerGrid=" + Drive.Grid(_a14GridBefore)
                        + " moveCmdsBefore=" + _a14MoveBefore
                        + " (the frame is taken with the pointer already over the dialog button) "
                        + NpcStateLine());
                    LogDialog("a14");
                    Drive.BeginShot(20, T14, "screen");
                    Next();
                    return;
                case 76:
                    WaitShot(20, T14, "screen", "a14", 20f);
                    return;
                case 77:
                    Drive.KV("A14-CLICK", "clicking the close option on the dialog bar (real mouse)");
                    ClickOptionByIndexRealMouse(0, "a14");       // index 0 = the close entry (always present)
                    Next();
                    return;
                case 78:
                    if (!_uiRetriedA14 && DialogIsOpen() && Elapsed(2.0f))
                    {
                        _uiRetriedA14 = true;
                        Drive.Warn("A14-CLICK-RETRY the dialog did not close from the real-mouse click "
                            + "-> legacy ExecuteEvents path");
                        Drive.Click("Option0");
                        return;
                    }
                    if (DialogIsOpen() && !Elapsed(6f)) return;
                    Drive.KV("A14-RESULT", "uiRetried=" + (_uiRetriedA14 ? 1 : 0)
                        + " moveCmdsBefore=" + _a14MoveBefore
                        + " moveCmdsAfter=" + Drive.MoveCmds
                        + " moveCmdDelta=" + (Drive.MoveCmds - _a14MoveBefore)
                        + " gridBefore=" + Drive.Grid(_a14GridBefore)
                        + " gridAfter=" + Drive.Grid(Drive.PlayerGrid())
                        + " gridChanged=" + (Drive.PlayerGrid() == _a14GridBefore ? 0 : 1)
                        + " playerMoving=" + (Drive.PlayerMoving() ? 1 : 0)
                        + " dialogOpen=" + (DialogIsOpen() ? 1 : 0)
                        + " note=a dialog-panel button click must produce 0 MoveCommand"
                        + " " + NpcStateLine());
                    Next();
                    return;

                // ============ 19: A16 quest log (Q) ====================================
                case 79:
                    if (!Elapsed(0.5f)) return;
                    Drive.KeyDown("q");
                    Next();
                    return;
                case 80:
                    Drive.KeyUp();
                    Next();
                    return;
                case 81:
                    if (!QuestLogIsOpen() && !Elapsed(8f)) return;
                    if (!Elapsed(0.8f)) return;
                    Drive.KV("A16-STATE", "questLogOpen=" + (QuestLogIsOpen() ? 1 : 0));
                    Drive.DumpTextsUnder("questlog",
                        Drive.QuestLog() != null ? Drive.QuestLog().transform : null, 25);
                    LogQuest("a16-questlog");
                    Drive.BeginShot(21, T16, "screen");
                    Next();
                    return;
                case 82:
                    WaitShot(21, T16, "screen", "a16", 20f);
                    return;

                default:
                    Finish("end");
                    return;
            }
        }

        /// <summary>Did the quest get accepted (the A11 effect that proves the click landed)?</summary>
        private static bool QuestAccepted()
        {
            var q = Drive.Quest();
            return q != null && q.DenOfEvil != QuestState.NotStarted;
        }

        private static bool HasDigit(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] >= '0' && s[i] <= '9') return true;
            }
            return false;
        }

        /// <summary>Dump the roster rows (name / class / level texts) so the log shows what was there.</summary>
        private void LogRoster()
        {
            var sel = Drive.CharSelectPanel();
            if (sel == null) { Drive.KV("ROSTER", "(no char-select panel)"); return; }
            var sb = new StringBuilder();
            foreach (var t in sel.GetComponentsInChildren<Text>(true))
            {
                if (t == null || t.text == null || t.text.Length == 0) continue;
                sb.Append('|').Append(Drive.PathOf(t.transform)).Append("\"").Append(t.text).Append('"');
            }
            var save = Drive.Save();
            var names = "(n/a)";
            if (save != null)
            {
                var list = save.List();
                if (list != null) names = string.Join(",", list.ToArray());
            }
            Drive.KV("ROSTER", "newHero=\"" + _newHeroName + "\" savedNames=[" + names + "] texts=" + sb);
        }

        /// <summary>Row index whose row node contains a Text equal to the hero name (-1 = not found).</summary>
        private int FindRowIndexOf(string heroName)
        {
            var sel = Drive.CharSelectPanel();
            if (sel == null || string.IsNullOrEmpty(heroName) || heroName == "(null)") return -1;
            foreach (var t in sel.GetComponentsInChildren<Text>(true))
            {
                if (t == null || t.text == null) continue;
                if (t.text.Trim() != heroName.Trim()) continue;
                var cur = t.transform;
                while (cur != null)
                {
                    if (cur.name.StartsWith("Row", StringComparison.Ordinal))
                    {
                        int idx;
                        if (int.TryParse(cur.name.Substring(3), out idx)) return idx;
                    }
                    if (cur == sel.transform) break;
                    cur = cur.parent;
                }
            }
            return -1;
        }

        /// <summary>
        /// Pick the standing cell for A2/A3: walkable, with an A* path onto the bridge deck, and
        /// close enough that the deck cell projects WELL INSIDE the frame (a real mouse click needs
        /// the target on screen: the iso projection maps a grid delta (dx,dy) to
        /// screen (+144*(dx-dy), -72*(dx+dy)) px around the frame centre).
        /// </summary>
        private bool HopForBridge()
        {
            var map = Drive.Map();
            if (map == null) return false;
            var cands = new[]
            {
                new Vector2Int(45, 29), new Vector2Int(46, 29), new Vector2Int(45, 30),
                new Vector2Int(46, 30), new Vector2Int(44, 29), new Vector2Int(44, 30),
                new Vector2Int(43, 29), new Vector2Int(48, 25), new Vector2Int(49, 25),
            };
            foreach (var c in cands)
            {
                if (!Drive.Walkable(c)) continue;
                var path = map.FindPath(c, _bridgeTarget);
                if (path == null || path.Count < 2) continue;
                if (!Drive.Walkable(_bridgeTarget)) continue;
                var dx = _bridgeTarget.x - c.x;
                var dy = _bridgeTarget.y - c.y;
                Hop(c, "a2-west-bank");
                Drive.KV("A3-SETUP", "stand=" + Drive.Grid(c) + " bridgeTarget=" + Drive.Grid(_bridgeTarget)
                    + " pathCells=" + path.Count
                    + " targetKind=" + Drive.TileKindAt(_bridgeTarget)
                    + " targetIsBridgeDeck=" + (IsBridgeDeck(_bridgeTarget) ? 1 : 0)
                    + " targetOnScreenOffset=(" + (144 * (dx - dy)) + "," + (-72 * (dx + dy)) + ")"
                    + " relToFrameCentre frame=1920x1080 (|x|<=960 |y|<=540 required for a real click)");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Pick the water cell for A4 (the R1-B fallback): NOT walkable, kind != Void, with a
        /// walkable neighbour inside the 2-cell fallback radius, reachable from the player, and
        /// projecting inside the frame.
        /// </summary>
        private bool PickWaterCell()
        {
            var map = Drive.Map();
            var p = Drive.Player();
            if (map == null || p == null) return false;
            var from = p.Grid;
            var cands = new[]
            {
                new Vector2Int(47, 30), new Vector2Int(47, 29), new Vector2Int(48, 30),
                new Vector2Int(47, 31), new Vector2Int(48, 31), new Vector2Int(46, 31),
            };
            foreach (var w in cands)
            {
                if (!map.InBounds(w)) continue;
                if (Drive.Walkable(w)) continue;
                if (map.TileAt(w) == TileKind.Void) continue;
                // nearest walkable inside the R1-B radius (mirror of PlayerModule's rule)
                var land = new Vector2Int(int.MinValue, int.MinValue);
                var best = int.MaxValue;
                for (var dx = -2; dx <= 2; dx++)
                {
                    for (var dy = -2; dy <= 2; dy++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var g = new Vector2Int(w.x + dx, w.y + dy);
                        if (!Drive.Walkable(g)) continue;
                        var d2 = dx * dx + dy * dy;
                        if (d2 >= best) continue;
                        var path = map.FindPath(from, g);
                        if (path == null || path.Count < 2) continue;
                        best = d2;
                        land = g;
                    }
                }
                if (land.x == int.MinValue) continue;
                var ddx = w.x - from.x;
                var ddy = w.y - from.y;
                var offx = 144 * (ddx - ddy);
                var offy = -72 * (ddx + ddy);
                if (Mathf.Abs(offx) > 860 || Mathf.Abs(offy) > 460) continue;
                _waterTarget = w;
                Drive.KV("A4-PICK", "waterCell=" + Drive.Grid(w)
                    + " tileKind=" + map.TileAt(w)
                    + " walkable=" + (Drive.Walkable(w) ? 1 : 0)
                    + " expectedLanding=" + Drive.Grid(land)
                    + " landingKind=" + map.TileAt(land)
                    + " landingWalkable=" + (Drive.Walkable(land) ? 1 : 0)
                    + " playerGrid=" + Drive.Grid(from)
                    + " targetOnScreenOffset=(" + offx + "," + offy + ") relToFrameCentre");
                return true;
            }
            return false;
        }

        /// <summary>Is this cell a `moor_bridge/*` deck cell (the wooden bridge over the river)?</summary>
        private static bool IsBridgeDeck(Vector2Int g)
        {
            string ground, obj;
            if (!Drive.TryGetTileKeys(g.x, g.y, out ground, out obj)) return false;
            return !string.IsNullOrEmpty(ground) && ground.IndexOf("bridge", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Finish(string why)
        {
            if (_doneRun) return;
            _doneRun = true;
            _transTracing = false;

            var p = Drive.Player();
            var cam = Camera.main;
            Drive.KV("FINISH", "why=" + why + " step=" + _step + " shots=" + _shots
                + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                + " playerDead=" + (p != null && p.IsDead ? 1 : 0)
                + " rigOrthoNow=" + Drive.RigOrtho().ToString("0.###")
                + " camOrthoNow=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " moveCmds=" + Drive.MoveCmds
                + " " + State());

            Drive.Check("osize-restored", Mathf.Abs(Drive.RigOrtho() - 3.75f) < 0.001f,
                "rigOrtho=" + Drive.RigOrtho().ToString("0.###") + " expected=3.75");
            Drive.Check("town-size", Drive.Map() != null && Drive.Map().Width == 56 && Drive.Map().Height == 40,
                "map=" + (Drive.Map() != null ? Drive.Map().Width + "x" + Drive.Map().Height : "-") + " expected=56x40");
            Drive.Check("no-timeout", _timedOutAt.Length == 0,
                "timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            Drive.Check("player-alive", p != null && !p.IsDead, "dead=" + (p != null && p.IsDead ? 1 : 0));
            Drive.Check("shots", _shots >= 20, "shots=" + _shots + " (expected >= 20 tiles)");
            Drive.Check("pacing", _pacingLogged, "pacing samples logged=" + (_pacingLogged ? 1 : 0));
            Drive.Check("device", SafeDevice().Length > 0 && SafeDevice().IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) < 0,
                "graphicsDeviceName=\"" + SafeDevice() + "\"");

            var ok = Drive.AllChecksOk();
            Drive.Log("VERDICT ok=" + (ok ? 1 : 0) + " " + Drive.CheckSummary());
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step + " shots=" + _shots
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            Drive.WriteFile(Drive.DonePath,
                "TOUR-DONE tag=" + _tag + " why=" + why + " shots=" + _shots
                + " verdict=" + (ok ? 1 : 0) + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
// end of r1_drive.cs
