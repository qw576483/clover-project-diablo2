// =============================================================================
// p41_drive.cs -- one-off Pipeline probe for pass 5 (the FINAL Play of this round):
//   presentation evidence (one contact sheet) + numeric probes for the 9 user complaints,
//   all in ONE Play session.
//
// NOT shipped: lives in <project>/.ai-tmp/drivers/ and is deleted before delivery.
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
//
// ---- where the tiles go, and why NOT via `capture_game_view --save_path` ----
//   * `capture_game_view --save_path "Temp/x.png"` is resolved against the AUTHORING ROOT
//     (Assets/): measured live, the tool reported savedPath "Assets/Temp/p41_pathcheck.png".
//     A png written UNDER Assets/ while Play runs makes the asset database run
//     StopAssetImportingV2(ForceSynchronousImport|ForceDomainReload); after that forced reload the
//     play session half-initialises (Game.Res null, ticks stop) and the rest of the tour is void
//   * So the driver captures itself, into <repo>/.ai-tmp/test/raw/ (outside the Unity project, never
//     imported), and p41_run.ps1 copies the tiles into client/Assets/Screenshots/p41_*.png AFTER
//     editor_stop:
//       - UI tiles (boot / menu): `ScreenCapture.CaptureScreenshot` == the composited game view,
//         i.e. exactly what `capture_game_view --source screen` returns (it is the same mechanism
//         when the editor is unfocused, which the driver cannot check).
//       - world tiles: the main camera rendered into a RenderTexture and encoded with
//         EncodeToPNG == exactly what `capture_game_view --source camera` does (camera render only,
//         no Screen Space - Overlay UI on top).  The chain was pre-checked outside Play by
//         `p41_camprobe.cs` (written=1, centerColor=0.10,0.42,0.30, camAspect=1.778).
//
// ---- why a MonoBehaviour and no engine timer ----
//   the chain needs wall-clock pacing across many frames (11 captures, two 3 s speed windows, four
//   direction walks) -> polling Time.unscaledTime in Update is the only shape that can hold frames
//   for that long.  Camera renders happen in LateUpdate (after every Update moved the views).
//
// ---- what is asserted where ----
//   the driver prints [P41] KEY=value lines + [P41] CHK name=<0|1>; the sheet script
//   (p41_sheet.py) re-derives EVERY grid verdict from those raw values (SKILL 1.12 item 1:
//   judgement belongs to a script, not to the model).
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
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Dir8 = Diablo2.Def.Dir8;

namespace P41
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Drive
    {
        internal const string Tag = "P41";

        /// <summary>Sprite name of `SpriteFrames.Placeholder` (a placeholder on screen is a FAIL).</summary>
        internal const string PlaceholderSpriteName = "D2CharPlaceholder";

        /// <summary>Exact brand byline required by the skill (must be readable in the boot/menu picture).</summary>
        internal const string ByLineText = "by clover-engine";

        internal const int ShotW = 1920;
        internal const int ShotH = 1080;

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;

        // ---- pending capture (performed in LateUpdate) ------------------------------------
        private static string _pendingFile = string.Empty;
        private static string _pendingKind = string.Empty;
        private static int _pendingIndex;

        internal static string RawDir { get { return _rawDir; } }
        internal static string DonePath { get { return _done; } }
        internal static bool ShotPending { get { return _pendingFile.Length > 0; } }

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

        /// <summary>`[P41] KEY=value` line.</summary>
        internal static void KV(string key, string value) { Log(key + "=" + value); }

        private static readonly List<string> Checks = new List<string>();

        /// <summary>`[P41] CHK name=... ok=<0|1> detail` -- the driver's own (coarse) assertion line.</summary>
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
            // arg = "rawDir|done"
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
            // a stale file from an earlier run must never pass for this one
            try { if (File.Exists(_pendingFile)) File.Delete(_pendingFile); } catch { }
            Log("SHOT-REQUEST n=" + index + " name=" + name + " kind=" + kind + " path=" + _pendingFile);
        }

        /// <summary>Called from LateUpdate: performs the pending capture (camera render or screen).</summary>
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
                    // fires at the END of the frame (that is why every capture has its own step and
                    // no state mutation happens in the same frame -- see p34_drive.cs)
                    ScreenCapture.CaptureScreenshot(file);
                    Log("SHOT-ISSUED n=" + index + " kind=screen file=" + file
                        + " frame=" + Time.frameCount + " t=" + Time.time.ToString("0.00"));
                    return;
                }

                var cam = Camera.main;
                if (cam == null) { Warn("SHOT-FAIL n=" + index + " Camera.main is null (camera render impossible)"); return; }

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

        /// <summary>True when the named tile is on disk (the waiting step then logs it and moves on).</summary>
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

        internal static Diablo2.Module.IMonsterModule Monsters()
        {
            return CtxMember("Monster") as Diablo2.Module.IMonsterModule;
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

        internal static string Call(object o, string method, object[] args)
        {
            if (o == null) return "(no-obj)";
            var m = o.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) return "(no-method:" + method + ")";
            var r = m.Invoke(o, args);
            return r != null ? r.ToString() : "(null)";
        }

        // ---- camera rig --------------------------------------------------------------------
        internal static object Rig() { return CtxMember("Camera"); }

        internal static float RigOrtho()
        {
            var v = Prop(Rig(), "OrthographicSize");
            return v is float ? (float)v : -1f;
        }

        /// <summary>Probe-only ortho override (writes the rig's private `_ortho`; restored right after).</summary>
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

        // ---- player view / animator --------------------------------------------------------
        internal static object PlayerView()
        {
            var vm = CtxMember("View");
            if (vm == null) return null;
            return Field(vm, "_player");
        }

        internal static string PlayerSpriteName()
        {
            var sr = Field(PlayerView(), "Renderer") as SpriteRenderer;
            if (sr == null) return "(no-sr)";
            return sr.sprite == null ? "(null)" : sr.sprite.name;
        }

        internal static string PlayerAnimLine()
        {
            var v = PlayerView();
            if (v == null) return "anim=(no-player-view)";
            var anim = Field(v, "Anim");
            if (anim == null) return "anim=(no-anim)";
            return "anim=" + Fmt(Prop(anim, "Anim"))
                   + " key=" + Fmt(Prop(anim, "CurrentKey"))
                   + " frame=" + Fmt(Prop(anim, "FrameIndex")) + "/" + Fmt(Prop(anim, "FrameCount"))
                   + " baseFps=" + Fmt(Field(anim, "_fps"))
                   + " speedScale=" + Fmt(Prop(anim, "SpeedScale"))
                   + " loop=" + Fmt(Prop(anim, "Loop"));
        }

        internal static int PlayerFrameIndex()
        {
            var v = Prop(Field(PlayerView(), "Anim"), "FrameIndex");
            return v is int ? (int)v : -1;
        }

        internal static int PlayerFrameCount()
        {
            var v = Prop(Field(PlayerView(), "Anim"), "FrameCount");
            return v is int ? (int)v : -1;
        }

        internal static float PlayerSpeedScale()
        {
            var v = Prop(Field(PlayerView(), "Anim"), "SpeedScale");
            return v is float ? (float)v : -1f;
        }

        internal static float PlayerBaseFps()
        {
            var v = Field(Field(PlayerView(), "Anim"), "_fps");
            return v is float ? (float)v : -1f;
        }

        internal static string Fmt(object o) { return o == null ? "(null)" : o.ToString(); }

        /// <summary>
        /// The world position expressed in CONTINUOUS CELL-CENTER space -- the space the motor moves in
        /// (cell (gx,gy) centre == (gx+0.5, gy+0.5), see PlayerMotor's file header).  `GameConst.PlayerWalkSpeed`
        /// is cells/second in THIS space, while one grid step is 1.0 or 1.414 WORLD units depending on the
        /// isometric projection of that step (Δ(1,1) -> (0,-1), Δ(1,-1) -> (2,0) with HalfW=1/HalfH=0.5)
        /// => integrating WORLD distance and comparing it with the constant is off by a per-direction
        /// factor (measured 2.121 for a "3.0" run = 3.0/sqrt(2)).  Reached through
        /// `PlayerMotor.CellCenterOf(Vector3)` (public static on an internal class).
        /// </summary>
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

        internal static string PlayerMoveSpeed()
        {
            var v = Prop(Player(), "MoveSpeed");
            return v == null ? "(no-prop)" : v.ToString();
        }

        internal static string SetRunning(bool run) { return Call(Player(), "SetRunning", new object[] { run }); }

        /// <summary>`MapModule.TryGetTileKeys(x, y, out ground, out object)` (non-contract entry).</summary>
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

        // ================================================================ input ============
        private static string PathOf(Transform t)
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

        /// <summary>Click a Button by GameObject name (optionally restricted to a path substring).</summary>
        internal static string Click(string arg)
        {
            if (EventSystem.current == null)
            {
                Log("ERR click name=" + arg + " eventSystem=null");
                return "ERR-no-eventsystem";
            }

            var pathSub = string.Empty;
            var want = arg ?? string.Empty;
            var bar = want.IndexOf('|');
            if (bar >= 0) { pathSub = want.Substring(0, bar); want = want.Substring(bar + 1); }

            var all = UnityEngine.Object.FindObjectsByType<Button>();
            var picks = new List<Button>();
            foreach (var b in all)
            {
                if (b == null || b.gameObject == null) continue;
                if (!string.Equals(b.gameObject.name, want, StringComparison.OrdinalIgnoreCase)) continue;
                if (!b.gameObject.activeInHierarchy) continue;
                if (pathSub.Length > 0 && PathOf(b.transform).IndexOf(pathSub, StringComparison.OrdinalIgnoreCase) < 0) continue;
                picks.Add(b);
            }

            if (picks.Count == 0)
            {
                Log("ERR click-miss name=" + arg + " buttons=" + all.Length);
                return "ERR-click-miss";
            }

            var pick = picks[0];
            foreach (var c in picks)
                if (PathOf(c.transform).Length > PathOf(pick.transform).Length) pick = c;

            var go = pick.gameObject;
            var ped = new PointerEventData(EventSystem.current);
            ped.button = PointerEventData.InputButton.Left;
            var rt = pick.transform as RectTransform;
            if (rt != null)
                ped.position = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(ped, hits);
            var top = hits.Count > 0 && hits[0].gameObject != null ? hits[0].gameObject.name : "(none)";
            var path = PathOf(go.transform);
            Log("CLICK name=" + want + " path=" + path + " candidates=" + picks.Count
                + " raycastTop=" + top + " hitSelf=" + (hits.Count > 0 && hits[0].gameObject == go ? 1 : 0));

            if (!pick.interactable)
            {
                Log("CLICK-SKIP name=" + want + " path=" + path + " (not interactable)");
                return "CLICK-SKIP";
            }

            ExecuteEvents.Execute(go, ped, ExecuteEvents.pointerClickHandler);
            Log("CLICK-DONE name=" + want + " path=" + path);
            return "CLICKED";
        }

        internal static string KeyDown(string arg)
        {
            var kb = RequireKeyboard();
            if (kb == null) return "ERR-no-keyboard";
            Key key;
            switch ((arg ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = Key.Space; break;
                case "escape": key = Key.Escape; break;
                case "enter": key = Key.Enter; break;
                case "i": key = Key.I; break;
                case "r": key = Key.R; break;
                default:
                    Log("ERR keydown unknown-key=" + arg);
                    return "ERR-unknown-key";
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key + " keyboard=" + kb.name);
            return "KEYDOWN-" + key;
        }

        internal static string KeyUp()
        {
            var kb = RequireKeyboard();
            if (kb == null) return "ERR-no-keyboard";
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
            return "KEYUP";
        }

        internal static string MouseEvent(bool down)
        {
            var mouse = Mouse.current;
            if (mouse == null) { mouse = InputSystem.AddDevice<Mouse>(); Warn("mouse device missing -> added"); }
            if (mouse == null) return "ERR-no-mouse";
            // bottom-left corner on purpose: screen centre lands on UI hotspots and would leave
            // hover states in the screenshots (see p34_drive.cs for the full story).
            var state = new MouseState { position = new Vector2(60f, 60f) };
            if (down) state = state.WithButton(MouseButton.Left);
            InputSystem.QueueStateEvent(mouse, state);
            return down ? "MDOWN" : "MUP";
        }

        private static Keyboard RequireKeyboard()
        {
            var kb = Keyboard.current;
            if (kb != null) return kb;
            kb = InputSystem.AddDevice<Keyboard>();
            Log("WARN keyboard-missing added=" + (kb != null ? kb.name : "(null)"));
            return kb;
        }

        // ================================================================ one-shots =========
        internal static string Ping()
        {
            Log("PONG frame=" + Time.frameCount + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
            return "PONG";
        }

        /// <summary>Editor/play setup so injected input really reaches the game (same as p34/p38).</summary>
        internal static string Cfg()
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
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); added = 1; }
            var mouseAdded = 0;
            if (Mouse.current == null) { InputSystem.AddDevice<Mouse>(); mouseAdded = 1; }

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
            Log(line);
            return line;
        }

        // ---- formatting ---------------------------------------------------------------------
        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }

        internal static string World(Vector3 w)
        {
            return "(" + w.x.ToString("0.00") + "," + w.y.ToString("0.00") + ")";
        }

        internal static string ExitsText(Diablo2.Module.IMapModule map)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < map.Exits.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Grid(map.Exits[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    /// <summary>Public one-shot entries for the run script (`run_script` needs a public static method).</summary>
    public static class Api
    {
        public static string Ping() { return Drive.Ping(); }

        public static string Cfg() { return Drive.Cfg(); }

        /// <summary>spec = "raw dir|done marker".</summary>
        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("P41EvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One tour, one Play session:
    ///   Boot(tile) -> MainMenu(tile) -> SINGLE PLAYER -> roster row 0 ENTER -> Stage(town)
    ///   -> town overview(tile) + town wide(tile) -> walk to the exit gap -> exit close-up(tile)
    ///   -> step on the exit -> BloodMoor -> wilderness(tile) + wilderness wide(tile)
    ///   -> run speed window -> walk speed window -> 4 direction walks (4 tiles) -> VERDICT.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        // the 4 cardinal SCREEN directions; every one is a GRID DIAGONAL (Iso.DirectionTo table)
        private static readonly Dir8[] CardinalDirs = { Dir8.S, Dir8.N, Dir8.E, Dir8.W };
        private static readonly string[] CardinalTiles =
        {
            "p41_08_dir_s.png", "p41_09_dir_n.png", "p41_10_dir_e.png", "p41_11_dir_w.png",
        };

        private const float ClearSettleSeconds = 4.0f;
        private const float AreaSettleSeconds = 3.0f;
        private const float SpeedWindowSeconds = 3.0f;
        private const float DirWarmSeconds = 0.70f;
        private const float MoveRepeatSeconds = 0.40f;
        private const float WideOrtho = 12f;      // = CameraRig.MaxOrthographicSize (documented bound)

        private string _tag = "r1";
        private string _rawDir = string.Empty;

        private int _step;
        private float _stepAt;
        private bool _done;
        private string _timedOutAt = string.Empty;
        private int _shots;

        // ---- captured probe values ----------------------------------------------------------
        private float _orthoAtTown = -1f;
        private float _camOrthoAtTown = -1f;
        private float _aspectAtTown = -1f;
        private int _townW, _townH, _wildW, _wildH;
        private int _aliveCount = -1;
        private int _inViewCount = -1;
        private string _exitTileLine = string.Empty;
        private int _exitTilesOk;
        private float _speedRun = -1f, _speedWalk = -1f;
        private float _fpsRunMeasured = -1f, _fpsWalkMeasured = -1f;
        private int _dirShotsOk;
        private readonly List<string> _dirNotes = new List<string>();
        private bool _byLineBootOk, _byLineMenuOk, _menuOk;
        private bool _playerDead;

        // ---- speed window state --------------------------------------------------------------
        private bool _measuring;
        private float _measureStart;
        private float _measureDist;      // cell-center units (the project's "cells")
        private float _measureWorld;     // world units (logged for transparency)
        private float _lastFrameAt;
        private double _frameAdvances;
        private int _lastFrameIndex = -1;
        private Vector3 _lastPos;
        private Vector2 _lastCell;
        private bool _cellSpaceOk;
        private Dir8 _measureDir;
        private int _movingFrames, _totalFrames;

        // ---- keep-walking state --------------------------------------------------------------
        private bool _keepWalking;
        private Dir8 _keepDir;

        // ---- direction-shot state ------------------------------------------------------------
        private int _dirPhase;
        private float _dirWarmStart;
        private Dir8 _dirWant;
        private string _dirTile = string.Empty;

        private Transform _anchor;
        private bool _walkIssued;
        private float _walkAt;
        private bool _menuSeen;
        private Vector2Int? _dirSpot;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _rawDir = parts[2];
            // MUST set the statics from HERE (not from a separate `run_script` call): each
            //    run_script invocation compiles+loads its own ephemeral assembly, so a static set by
            //    `P41.Api.Paths` in an earlier invocation is NOT visible to this one (measured: the
            //    tiles went to "/p41_03_town.png", i.e. the drive root, and the done marker never
            //    appeared).  The tour runs inside THIS invocation, so its statics are alive here.
            var rawDir = parts.Length > 2 ? parts[2] : string.Empty;
            var donePath = parts.Length > 3 ? parts[3] : string.Empty;
            Drive.Paths(rawDir + "|" + donePath);
            _step = 0;
            _stepAt = Time.unscaledTime;
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount
                      + " rawDir=" + _rawDir + " screen=" + Screen.width + "x" + Screen.height);
        }

        private void Update()
        {
            if (_done) return;
            try { Step(); }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        /// <summary>Camera renders happen here: every Update (including the module ticks) has run.</summary>
        private void LateUpdate()
        {
            try { Drive.FlushShot(); }
            catch (Exception ex) { Drive.Warn("FLUSH-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ================================================================ helpers ===========
        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
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
                   + " dir=" + (p != null ? p.Dir.ToString() : "-")
                   + " moving=" + (p != null && p.IsMoving ? 1 : 0)
                   + " running=" + (p != null && p.IsRunning ? 1 : 0)
                   + " t=" + Time.time.ToString("0.00");
        }

        /// <summary>Wait for a capture file; never blocks the tour forever, always leaves a log line.</summary>
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

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            _keepWalking = false;

            var p = Drive.Player();
            _playerDead = p != null && p.IsDead;
            var orthoNow = Drive.RigOrtho();
            var cam = Camera.main;

            Drive.KV("FINISH", "why=" + why + " step=" + _step + " shots=" + _shots
                     + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                     + " playerDead=" + (_playerDead ? 1 : 0)
                     + " rigOrthoNow=" + orthoNow.ToString("0.###")
                     + " camOrthoNow=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                     + " " + State());

            Drive.Check("osize", Mathf.Abs(_orthoAtTown - 3.75f) < 0.001f
                        && Mathf.Abs(_camOrthoAtTown - 3.75f) < 0.001f
                        && Mathf.Abs(2f * _orthoAtTown - 7.5f) < 0.01f,
                "rig=" + _orthoAtTown.ToString("0.###") + " cam=" + _camOrthoAtTown.ToString("0.###")
                + " visibleWorldH=" + (2f * _orthoAtTown).ToString("0.###")
                + " visibleGridRows=" + (2f * _orthoAtTown / GameConst.TileSize).ToString("0.###"));
            Drive.Check("osize-restored", Mathf.Abs(orthoNow - 3.75f) < 0.001f,
                "afterWide value=" + orthoNow.ToString("0.###"));

            Drive.Check("speedconst", Mathf.Abs(GameConst.PlayerWalkSpeed - 3f) < 0.0001f
                        && Mathf.Abs(GameConst.PlayerWalkSpeedFactor - (7f / 15f)) < 0.0001f,
                "PlayerWalkSpeed=" + GameConst.PlayerWalkSpeed.ToString("0.###")
                + " factor=" + GameConst.PlayerWalkSpeedFactor.ToString("0.#####"));

            Drive.Check("cell-space", _cellSpaceOk,
                "speed measured in PlayerMotor's cell-center space (PlayerMotor.CellCenterOf) = " + (_cellSpaceOk ? 1 : 0)
                + "; world distance is NOT comparable with the cells/s constant (see Drive.CellPos)");
            Drive.Check("speed-run", _speedRun > 0f && Mathf.Abs(_speedRun - 3f) <= 0.30f,
                "measured=" + _speedRun.ToString("0.000") + " expected=3.000");
            Drive.Check("speed-walk", _speedWalk > 0f && Mathf.Abs(_speedWalk - 1.4f) <= 0.25f,
                "measured=" + _speedWalk.ToString("0.000") + " expected=1.400");
            Drive.Check("anim-run-fps", _fpsRunMeasured >= 19f && _fpsRunMeasured <= 29f,
                "measured=" + _fpsRunMeasured.ToString("0.00") + " expected=24.00 (8 frames x 3.0 cells/s)");
            Drive.Check("anim-walk-fps", _fpsWalkMeasured >= 8.5f && _fpsWalkMeasured <= 14f,
                "measured=" + _fpsWalkMeasured.ToString("0.00") + " expected=11.20 (8 frames x 1.4 cells/s)");

            Drive.Check("town-size", _townW == 56 && _townH == 40,
                "map=" + _townW + "x" + _townH + " expected=56x40");
            Drive.Check("wild-size", _wildW == 80 && _wildH == 80,
                "map=" + _wildW + "x" + _wildH + " expected=80x80");
            Drive.Check("exit-tiles", _exitTilesOk == 3 && _exitTileLine.Length > 0,
                "kindExitCells=" + _exitTilesOk + " " + _exitTileLine);
            Drive.Check("wild-monsters", _aliveCount >= 20 && _aliveCount <= 90,
                "alive=" + _aliveCount + " expected~41.9 (walkable x MonDen 520/100000 x avgGroup 1.83)");
            Drive.Check("dir-shots", _dirShotsOk == 4,
                "ok=" + _dirShotsOk + "/4 " + string.Join(";", _dirNotes.ToArray()));
            Drive.Check("byline", _byLineBootOk && _byLineMenuOk,
                "boot=" + (_byLineBootOk ? 1 : 0) + " menu=" + (_byLineMenuOk ? 1 : 0));
            Drive.Check("menu-items", _menuOk, "see MENU-BUTTONS above");
            Drive.Check("no-timeout", _timedOutAt.Length == 0, "timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            Drive.Check("player-alive", !_playerDead, "playerDead=" + (_playerDead ? 1 : 0));

            var ok = Drive.AllChecksOk();
            Drive.Log("VERDICT ok=" + (ok ? 1 : 0) + " " + Drive.CheckSummary());
            Drive.Log("TOUR-DONE ok=" + why + " steps=" + _step + " shots=" + _shots
                      + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            Drive.WriteFile(Drive.DonePath,
                "TOUR-DONE tag=" + _tag + " ok=" + why + " shots=" + _shots
                + " verdict=" + (ok ? 1 : 0) + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void TimeoutStep(string step, float seconds)
        {
            if (!Elapsed(seconds)) return;
            Drive.Log("STEP-TIMEOUT step=" + step + " elapsed=" + (Time.unscaledTime - _stepAt).ToString("0.0")
                      + " " + State());
            if (_timedOutAt.Length == 0) _timedOutAt = step;
            Finish("timeout-" + step);
        }

        // ================================================================ UI probes =========
        private static List<Text> AllTexts()
        {
            var list = new List<Text>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Text>())
            {
                if (t != null && t.gameObject != null) list.Add(t);
            }
            return list;
        }

        private static string PathOf(Transform t)
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

        private static Type MirrorType() { return Drive.FindType("Diablo2.UI.D2TextMirror"); }

        /// <summary>Dump every on-screen text node (the data holder + whether the bitmap mirror draws it).</summary>
        private void DumpTexts(string where)
        {
            var mirror = MirrorType();
            var sb = new StringBuilder();
            var n = 0;
            foreach (var t in AllTexts())
            {
                if (t.text == null) continue;
                var m = mirror != null ? t.GetComponent(mirror) : null;
                var tier = m != null ? Drive.Fmt(Drive.Field(m, "Font")) : "(no-mirror)";
                n++;
                if (n <= 40)
                {
                    sb.Append('|').Append(PathOf(t.transform)).Append('"').Append(t.text.Replace("\n", "\\n")).Append('"')
                      .Append(" size=").Append(t.fontSize)
                      .Append(" col=").Append(t.color.r.ToString("0.00")).Append(',')
                      .Append(t.color.g.ToString("0.00")).Append(',').Append(t.color.b.ToString("0.00"))
                      .Append(" enabled=").Append(t.enabled ? 1 : 0)
                      .Append(" font=").Append(t.font == null ? "null" : "SET")
                      .Append(" mirror=").Append(m != null ? 1 : 0)
                      .Append(" tier=").Append(tier)
                      .Append(" active=").Append(t.gameObject.activeInHierarchy ? 1 : 0)
                      .Append(' ');
                }
            }
            Drive.KV("UITEXTS", "where=" + where + " count=" + n + " " + sb.ToString());
        }

        private Text FindText(string exact)
        {
            foreach (var t in AllTexts())
            {
                if (t.text != null && t.text == exact) return t;
            }
            return null;
        }

        private void ProbeByLine(string where, out bool ok)
        {
            ok = false;
            var t = FindText(Drive.ByLineText);
            if (t == null)
            {
                Drive.KV("BYLINE", "where=" + where + " present=0 wanted=\"" + Drive.ByLineText + "\"");
                return;
            }
            var mirror = MirrorType();
            var m = mirror != null ? t.GetComponent(mirror) : null;
            var tier = m != null ? Drive.Fmt(Drive.Field(m, "Font")) : "(no-mirror)";
            ok = t.gameObject.activeInHierarchy && m != null && tier == "Font24";
            Drive.KV("BYLINE", "where=" + where + " present=1 path=" + PathOf(t.transform)
                     + " text=\"" + t.text + "\" tier=" + tier + " size=" + t.fontSize
                     + " color=" + t.color.r.ToString("0.00") + "," + t.color.g.ToString("0.00")
                     + "," + t.color.b.ToString("0.00")
                     + " active=" + (t.gameObject.activeInHierarchy ? 1 : 0)
                     + " ok=" + (ok ? 1 : 0));
        }

        private void ProbeMenuButtons()
        {
            var names = new List<string>();
            var labels = new List<string>();
            var forbidden = 0;
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>())
            {
                if (b == null || b.gameObject == null || !b.gameObject.activeInHierarchy) continue;
                var n = b.gameObject.name;
                names.Add(n);
                var label = "(no-text)";
                // includeInactive: the uGUI Text nodes are data holders (font=null, enabled=false) --
                // the pixels come from D2TextMirror -- so a default GetComponentInChildren returns null.
                var txts = b.GetComponentsInChildren<Text>(true);
                foreach (var t in txts)
                {
                    if (t != null && t.text != null && t.text.Length > 0) { label = t.text; break; }
                }
                labels.Add(n + "=" + label);
                var up = (n + "|" + label).ToUpperInvariant();
                if (up.IndexOf("MULTIPLAYER") >= 0 || up.IndexOf("CINEMATIC") >= 0) forbidden++;
            }
            names.Sort();
            labels.Sort();

            // independent of the Button components: how many on-screen texts say SINGLE PLAYER / EXIT /
            // the two removed items (the pixels are drawn from these very strings)
            var wanted = 0;
            var textCsv = 0;
            var seen = new List<string>();
            foreach (var t in AllTexts())
            {
                if (t.text == null) continue;
                var up = t.text.ToUpperInvariant().Trim();
                if (up == "SINGLE PLAYER" || up == "EXIT") { wanted++; if (!seen.Contains(up)) seen.Add(up); }
                if (up == "MULTIPLAYER" || up == "CINEMATICS") textCsv++;
            }
            seen.Sort();
            _menuOk = names.Count == 2 && names.Contains("Single") && names.Contains("Quit")
                      && forbidden == 0 && seen.Count == 2 && textCsv == 0;
            Drive.KV("MENU-BUTTONS", "active=" + names.Count + " names=[" + string.Join(",", names.ToArray())
                     + "] labels=[" + string.Join(",", labels.ToArray()) + "]");
            Drive.KV("MENU-FORBIDDEN", "multiplayerOrCinematics=" + forbidden + " expected=0");
            Drive.KV("MENU-TEXTS", "labelTexts=[" + string.Join(",", seen.ToArray()) + "] hits=" + wanted
                     + " forbiddenTexts=" + textCsv + " ok=" + (seen.Count == 2 && textCsv == 0 ? 1 : 0));
        }

        private void ProbeTown()
        {
            var map = Drive.Map();
            _townW = map.Width;
            _townH = map.Height;
            _orthoAtTown = Drive.RigOrtho();
            var cam = Camera.main;
            _camOrthoAtTown = cam != null ? cam.orthographicSize : -1f;
            _aspectAtTown = cam != null ? cam.aspect : -1f;

            Drive.KV("TOWN", "area=" + map.Area + " map=" + _townW + "x" + _townH
                     + " seed=" + map.Seed + " spawn=" + Drive.Grid(map.SpawnPoint)
                     + " walkable=" + map.WalkableCount + " blocked=" + map.BlockedCount
                     + " exits=" + Drive.ExitsText(map) + " npcPoints=" + map.NpcPoints.Count);
            Drive.KV("OSIZE", "rigValue=" + _orthoAtTown.ToString("0.###")
                     + " camValue=" + _camOrthoAtTown.ToString("0.###")
                     + " visibleWorldH=" + (2f * _orthoAtTown).ToString("0.###")
                     + " tileSize=" + GameConst.TileSize
                     + " visibleGridRows=" + (2f * _orthoAtTown / GameConst.TileSize).ToString("0.###")
                     + " aspect=" + _aspectAtTown.ToString("0.####")
                     + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                     + " playerWorld=" + Drive.World(Drive.Player().World));
            Drive.KV("SPEEDCONST", "PlayerWalkSpeed=" + GameConst.PlayerWalkSpeed.ToString("0.###")
                     + " walkFactor=" + GameConst.PlayerWalkSpeedFactor.ToString("0.#####")
                     + " walkSpeedDerived=" + (GameConst.PlayerWalkSpeed * GameConst.PlayerWalkSpeedFactor).ToString("0.####")
                     + " playerMoveSpeed=" + Drive.PlayerMoveSpeed()
                     + " running=" + (Drive.Player().IsRunning ? 1 : 0));

            var sb = new StringBuilder();
            _exitTilesOk = 0;
            foreach (var g in map.Exits)
            {
                string ground, obj;
                var has = Drive.TryGetTileKeys(g.x, g.y, out ground, out obj);
                var kind = map.TileAt(g);
                if (kind == TileKind.Exit) _exitTilesOk++;
                var line = "g=(" + g.x + "," + g.y + ") kind=" + kind + " keysAvailable=" + (has ? 1 : 0)
                           + " ground=" + (string.IsNullOrEmpty(ground) ? "(none)" : ground)
                           + " object=" + (string.IsNullOrEmpty(obj) ? "(none)" : obj);
                Drive.Log("EXITTILE " + line);
                if (sb.Length > 0) sb.Append(" ; ");
                sb.Append(line);
            }
            _exitTileLine = sb.ToString();
            Drive.KV("EXITTILES", "count=" + map.Exits.Count + " kindExitCells=" + _exitTilesOk);
        }

        private void ProbeWilderness()
        {
            var map = Drive.Map();
            var mon = Drive.Monsters();
            _wildW = map.Width;
            _wildH = map.Height;
            _aliveCount = mon != null ? mon.AliveCount : -1;

            var cam = Camera.main;
            var camPos = cam != null ? cam.transform.position : Vector3.zero;
            var halfH = cam != null ? cam.orthographicSize : 0f;
            var halfW = halfH * (cam != null ? cam.aspect : 1f);
            var inView = 0;
            var near = 0;
            var aliveRows = 0;
            if (mon != null)
            {
                foreach (var st in mon.All)
                {
                    if (!st.alive) continue;
                    aliveRows++;
                    if (Mathf.Abs(st.worldX - camPos.x) <= halfW && Mathf.Abs(st.worldY - camPos.y) <= halfH) inView++;
                    var d = Mathf.Max(Mathf.Abs(st.gridX - Drive.Player().Grid.x),
                                      Mathf.Abs(st.gridY - Drive.Player().Grid.y));
                    if (d <= 20) near++;
                }
            }
            _inViewCount = inView;

            var expected = map.WalkableCount * 520f / 100000f * 1.83f;
            Drive.KV("WILD", "area=" + map.Area + " map=" + _wildW + "x" + _wildH
                     + " seed=" + map.Seed + " spawn=" + Drive.Grid(map.SpawnPoint)
                     + " walkable=" + map.WalkableCount + " blocked=" + map.BlockedCount
                     + " exits=" + Drive.ExitsText(map)
                     + " caveEntrance=" + (map.CaveEntrance.HasValue ? Drive.Grid(map.CaveEntrance.Value) : "(none)")
                     + " monsterSpawnPoints=" + map.MonsterSpawns.Count);
            Drive.KV("MONSTERS", "aliveCount=" + _aliveCount + " aliveRows=" + aliveRows
                     + " rows=" + (mon != null ? mon.All.Count : -1)
                     + " inOneScreen=" + inView + " within20Cells=" + near
                     + " walkable=" + map.WalkableCount
                     + " expectedFromMonDen=" + expected.ToString("0.0")
                     + " formula=walkable*520/100000*1.83");
        }

        // ================================================================ camera ============
        /// <summary>
        /// PROBE-ONLY wide shot. The shipped ortho is 3.75 (one screen is about 7.5 grid rows), which
        /// cannot show an 80x80 map, so for the two overview tiles we set the rig's ortho to
        /// `WideOrtho` (= the engine's documented MaxOrthographicSize 12) and centre the camera on the
        /// map centre through the CONTRACT entry `ICameraRig.Follow` (a probe anchor transform).
        /// `RestoreCamera()` puts both back; the restored value is asserted in the verdict.
        /// </summary>
        private void WideShot(string which)
        {
            var rig = Drive.RigContract();
            var map = Drive.Map();
            if (rig == null || map == null) { Drive.Warn("WIDE-SHOT no rig/map"); return; }
            if (_anchor == null)
            {
                var go = new GameObject("P41ProbeAnchor");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _anchor = go.transform;
            }
            // town: the level centre.  wilderness: the CENTROID of the alive monsters, so the overview
            // tile actually shows the monster spread (the level centre of the 80x80 map can easily have
            // no monster inside the frame at all -- measured 0 in run r2).
            var c = new Vector2Int(map.Width / 2, map.Height / 2);
            var anchorNote = "levelCentre";
            var mon = Drive.Monsters();
            if (which == "wild" && mon != null && mon.AliveCount > 0)
            {
                var sx = 0;
                var sy = 0;
                var n = 0;
                foreach (var st in mon.All)
                {
                    if (!st.alive) continue;
                    sx += st.gridX;
                    sy += st.gridY;
                    n++;
                }
                if (n > 0)
                {
                    c = new Vector2Int(sx / n, sy / n);
                    anchorNote = "monsterCentroid(n=" + n + ")";
                }
            }

            _anchor.position = Iso.GridToWorld(c);
            var ok = Drive.RigSetOrtho(WideOrtho);
            rig.Follow(_anchor);

            var camPos = Iso.GridToWorld(c);
            var halfH = WideOrtho;
            var halfW = WideOrtho * (Camera.main != null ? Camera.main.aspect : 16f / 9f);
            var inFrame = 0;
            if (mon != null)
            {
                foreach (var st in mon.All)
                {
                    if (!st.alive) continue;
                    if (Mathf.Abs(st.worldX - camPos.x) <= halfW && Mathf.Abs(st.worldY - camPos.y) <= halfH) inFrame++;
                }
            }
            Drive.KV("WIDE-SHOT", "which=" + which + " orthoOverride=" + WideOrtho.ToString("0.##")
                     + " overrideOk=" + (ok ? 1 : 0) + " anchor=" + anchorNote + " center=" + Drive.Grid(c)
                     + " camPos=" + Drive.World(_anchor.position)
                     + " visibleWorld=" + (2f * halfW).ToString("0.0") + "x" + (2f * halfH).ToString("0.0")
                     + " aliveInFrame=" + inFrame
                     + " note=probe-only (shipped value is 3.75; restored right after this tile)");
        }

        private void RestoreCamera()
        {
            var rig = Drive.RigContract();
            if (rig != null) rig.Unfollow();
            Drive.RigSetOrtho(3.75f);
            var cam = Camera.main;
            Drive.KV("CAM-RESTORED", "rigValue=" + Drive.RigOrtho().ToString("0.###")
                     + " camValue=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                     + " expected=3.75");
        }

        // ================================================================ walking ===========
        private List<Vector2Int> PathTo(Vector2Int to, Vector2Int delta, out bool straight, out bool touchesExit)
        {
            straight = false;
            touchesExit = false;
            var map = Drive.Map();
            var p = Drive.Player();
            if (map == null || p == null || !map.IsGenerated) return null;
            var path = map.FindPath(p.Grid, to);
            if (path == null || path.Count < 2) return null;
            straight = true;
            for (var i = 1; i < path.Count; i++)
            {
                if (path[i] - path[i - 1] != delta) straight = false;
                if (map.TileAt(path[i]) == TileKind.Exit) touchesExit = true;
            }
            return path;
        }

        /// <summary>
        /// A target `r` grid steps along `delta` from `from` whose A* path never steps on an exit tile
        /// (stepping on one would switch area and destroy the rest of the tour).  `requireStraight` also
        /// demands that every step of the path is `delta` -- needed for the DIRECTION tiles (the sprite
        /// must keep facing the walked way), NOT needed for the speed windows (those integrate the
        /// travelled distance in cell space, so the path shape is irrelevant).
        /// </summary>
        private Vector2Int? PickStraightFrom(Vector2Int from, Vector2Int delta, int minR, int maxR,
            bool requireStraight)
        {
            var map = Drive.Map();
            if (map == null || !map.IsGenerated) return null;
            for (var r = maxR; r >= minR; r--)
            {
                var to = from + delta * r;
                if (!map.InBounds(to) || !map.Walkable(to)) continue;
                var path = map.FindPath(from, to);
                if (path == null || path.Count < 2) continue;
                var ok = true;
                for (var i = 1; i < path.Count; i++)
                {
                    if (map.TileAt(path[i]) == TileKind.Exit) { ok = false; break; }
                    if (requireStraight && path[i] - path[i - 1] != delta) { ok = false; break; }
                }
                if (ok) return to;
            }
            return null;
        }

        private Vector2Int? PickStraight(Vector2Int delta, int minR, int maxR, out bool straight)
        {
            var p = Drive.Player();
            straight = false;
            if (p == null) return null;
            var t = PickStraightFrom(p.Grid, delta, minR, maxR, true);
            straight = t.HasValue;
            return t;
        }

        /// <summary>
        /// A cell from which ALL FOUR cardinal screen directions offer a straight walk of >= `minR`
        /// cells.  The direction tiles MUST show the character walking that way, and from a spot on the
        /// map border that is impossible (measured in run r2: the wilderness spawn (1,36) sits on the
        /// west edge, so N and W had no target at all -> those two tiles showed the PREVIOUS facing and
        /// the walk-speed window could not run either).  Scans rings around the map centre and returns
        /// the first cell that satisfies all four; falls back to the spawn.
        /// </summary>
        private Vector2Int FindDirSpot()
        {
            // try 5, then 4, then 3 straight steps: on a map that is ~2/3 walkable the chance that ONE
            // diagonal line of 5 is clear is ~0.13, so "all four directions clear" needs a few hundred
            // candidates at 5 but is essentially free at 3.  Logged so the report can say which applied.
            var tries = new[] { 5, 4, 3 };
            for (var k = 0; k < tries.Length; k++)
            {
                var c = ScanDirSpot(tries[k]);
                if (c.HasValue)
                {
                    Drive.KV("DIR-SPOT", "cell=" + Drive.Grid(c.Value) + " minStraight=" + tries[k]
                             + " allFourDirections=1");
                    return c.Value;
                }
            }
            var map = Drive.Map();
            Drive.Warn("DIR-SPOT none found for minStraight 5/4/3; falling back to the spawn");
            return map != null ? map.SpawnPoint : Vector2Int.zero;
        }

        private Vector2Int? ScanDirSpot(int minR)
        {
            var map = Drive.Map();
            if (map == null) return null;
            var cx = map.Width / 2;
            var cy = map.Height / 2;
            var maxRad = Mathf.Max(map.Width, map.Height);
            for (var rad = 0; rad <= maxRad; rad += 2)
            {
                for (var dx = -rad; dx <= rad; dx += 2)
                {
                    for (var dy = -rad; dy <= rad; dy += 2)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != rad) continue;
                        var c = new Vector2Int(cx + dx, cy + dy);
                        if (!map.InBounds(c) || !map.Walkable(c)) continue;
                        var ok = true;
                        for (var i = 0; i < CardinalDirs.Length && ok; i++)
                        {
                            var delta = Iso.DirectionDelta(CardinalDirs[i]);
                            if (!PickStraightFrom(c, delta, minR, minR + 3, true).HasValue) ok = false;
                        }
                        if (ok) return c;
                    }
                }
            }
            return null;
        }

        private void KeepWalkingStep()
        {
            if (!_keepWalking) return;
            if (Time.unscaledTime - _lastFrameAt < MoveRepeatSeconds) return;
            _lastFrameAt = Time.unscaledTime;
            bool st;
            var t = PickStraight(Iso.DirectionDelta(_keepDir), 3, 9, out st);
            var p = Drive.Player();
            if (t.HasValue && p != null) p.MoveTo(t.Value);
        }

        /// <summary>MoveTo a cell and wait until arrived. Returns true when the wait is over.</summary>
        private bool WalkTo(Vector2Int target, float budget)
        {
            var p = Drive.Player();
            if (p == null) return true;
            if (!_walkIssued)
            {
                p.MoveTo(target);
                _walkIssued = true;
                _walkAt = Time.unscaledTime;
                Drive.Log("WALKTO-BEGIN target=" + Drive.Grid(target) + " from=" + Drive.Grid(p.Grid));
            }
            if (p.Grid == target) { _walkIssued = false; return true; }
            if (Time.unscaledTime - _walkAt > budget)
            {
                Drive.Warn("WALKTO-TIMEOUT target=" + Drive.Grid(target) + " grid=" + Drive.Grid(p.Grid));
                _walkIssued = false;
                return true;
            }
            if (!p.IsMoving && Time.unscaledTime - _walkAt > 2f)
            {
                p.MoveTo(target);
                _walkAt = Time.unscaledTime;
            }
            return false;
        }

        // ================================================================ measure ===========
        private void BeginMeasure(string mode)
        {
            var p = Drive.Player();
            if (p == null) { Drive.Warn("MEASURE no player (window skipped)"); return; }

            var run = mode == "run";
            Drive.SetRunning(run);
            _keepWalking = false;

            // start from a cell where a LONG walk in that direction really exists (see FindDirSpot)
            if (!_dirSpot.HasValue) _dirSpot = FindDirSpot();
            if (_dirSpot.HasValue && p.Grid != _dirSpot.Value)
            {
                p.TeleportTo(_dirSpot.Value);
                Drive.Log("MEASURE-HOP to " + Drive.Grid(_dirSpot.Value));
            }

            var dir = run ? CardinalDirs[0] : CardinalDirs[2];       // S then E
            var delta = Iso.DirectionDelta(dir);
            // NOTE: no straight-line requirement here -- the window integrates the travelled distance
            // in cell space, so the shape of the path does not matter, only that it keeps going.
            var t = PickStraightFrom(p.Grid, delta, 12, 30, false);
            if (!t.HasValue) t = PickStraightFrom(p.Grid, delta, 4, 11, false);
            if (!t.HasValue)
            {
                Drive.KV("SPEED", "mode=" + mode + " ERROR=no-target dir=" + dir
                         + " grid=" + Drive.Grid(p.Grid));
                return;
            }

            p.MoveTo(t.Value);
            _measuring = true;
            _measureStart = Time.unscaledTime;
            _measureDist = 0f;
            _measureWorld = 0f;
            _lastPos = p.World;
            _cellSpaceOk = Drive.CellPos(p.World, out _lastCell);
            _lastFrameAt = Time.unscaledTime;
            _frameAdvances = 0;
            _lastFrameIndex = Drive.PlayerFrameIndex();
            _movingFrames = 0;
            _totalFrames = 0;
            _measureDir = dir;
            Drive.KV("MEASURE-BEGIN", "mode=" + mode + " running=" + (p.IsRunning ? 1 : 0)
                     + " dir=" + dir + " delta=(" + delta.x + "," + delta.y + ")"
                     + " target=" + Drive.Grid(t.Value) + " from=" + Drive.Grid(p.Grid)
                     + " straightRequired=0 (the window integrates the travelled distance in cell space)"
                     + " expect=" + (run ? 3.0f : 1.4f).ToString("0.00")
                     + " window=" + SpeedWindowSeconds.ToString("0.0") + "s"
                     + " animAtStart=" + Drive.PlayerAnimLine());
        }

        /// <summary>Returns true when the window is over (numbers logged).</summary>
        private bool TickMeasure(string mode)
        {
            var p = Drive.Player();
            if (p == null || !_measuring) return true;

            var now = Time.unscaledTime;
            var w = p.World;
            _measureWorld += Vector3.Distance(_lastPos, w);
            _lastPos = w;
            Vector2 c;
            var cellOk = Drive.CellPos(w, out c);
            if (cellOk && _cellSpaceOk) _measureDist += Vector2.Distance(_lastCell, c);
            if (cellOk) _lastCell = c;
            _lastFrameAt = now;
            _totalFrames++;
            if (p.IsMoving) _movingFrames++;

            var fi = Drive.PlayerFrameIndex();
            var fc = Mathf.Max(1, Drive.PlayerFrameCount());
            if (fi != _lastFrameIndex && _lastFrameIndex >= 0)
            {
                var d = fi - _lastFrameIndex;
                if (d < 0) d += fc;
                _frameAdvances += d;
            }
            _lastFrameIndex = fi;

            if (!p.IsMoving && now - _measureStart < SpeedWindowSeconds)
            {
                bool st;
                var t = PickStraight(Iso.DirectionDelta(_measureDir), 12, 28, out st);
                if (!t.HasValue) t = PickStraight(Iso.DirectionDelta(_measureDir), 3, 9, out st);
                if (t.HasValue) p.MoveTo(t.Value);
            }

            var elapsed = now - _measureStart;
            if (elapsed < SpeedWindowSeconds) return false;

            _measuring = false;
            var speed = _measureDist / elapsed;
            var measuredFps = (float)(_frameAdvances / (double)elapsed);
            var baseFps = Drive.PlayerBaseFps();
            var scale = Drive.PlayerSpeedScale();
            var effFps = baseFps * scale;

            if (mode == "run") { _speedRun = speed; _fpsRunMeasured = measuredFps; }
            else { _speedWalk = speed; _fpsWalkMeasured = measuredFps; }

            Drive.KV("SPEED", "mode=" + mode
                     + " cells=" + _measureDist.ToString("0.000")
                     + " secs=" + elapsed.ToString("0.000")
                     + " speed=" + speed.ToString("0.000")
                     + " expect=" + (mode == "run" ? 3.0f : 1.4f).ToString("0.000")
                     + " worldUnits=" + _measureWorld.ToString("0.000")
                     + " cellSpace=" + (_cellSpaceOk ? 1 : 0)
                     + " movingFrames=" + _movingFrames + "/" + _totalFrames
                     + " cellSize=" + GameConst.TileSize);
            Drive.KV("ANIM-" + mode.ToUpperInvariant(),
                     "anim=" + Drive.Fmt(Drive.Prop(Drive.Field(Drive.PlayerView(), "Anim"), "Anim"))
                     + " frames=" + Drive.PlayerFrameCount()
                     + " baseFps=" + baseFps.ToString("0.00")
                     + " speedScale=" + scale.ToString("0.000")
                     + " effFps=" + effFps.ToString("0.00")
                     + " measuredFrameAdvancePerSec=" + measuredFps.ToString("0.00")
                     + " " + Drive.PlayerAnimLine()
                     + " sprite=" + Drive.PlayerSpriteName());
            return true;
        }

        // ================================================================ direction shots ====
        private void BeginDirShot(int index)
        {
            _dirIndex = index;
            _dirWant = CardinalDirs[index];
            _dirTile = CardinalTiles[index];
            _dirPhase = 0;
            var p = Drive.Player();
            if (p == null) { Drive.Warn("DIR no player"); Finish("no-player"); return; }

            // every direction tile starts from THE SAME cell (the spot where all four directions are
            // walkable in a straight line) -> the four pictures are comparable, and no direction can
            // fail because the previous walk left the player against the map border.
            if (!_dirSpot.HasValue) _dirSpot = FindDirSpot();
            if (_dirSpot.HasValue && p.Grid != _dirSpot.Value) p.TeleportTo(_dirSpot.Value);

            var delta = Iso.DirectionDelta(_dirWant);
            bool st;
            var t = PickStraight(delta, 4, 10, out st);
            if (!t.HasValue)
            {
                var map = Drive.Map();
                if (map != null)
                {
                    p.TeleportTo(map.SpawnPoint);
                    Drive.Log("DIR-HOP to spawn " + Drive.Grid(map.SpawnPoint) + " (no straight target)");
                }
                t = PickStraight(delta, 4, 10, out st);
            }
            if (t.HasValue) p.MoveTo(t.Value);
            _keepWalking = true;
            _keepDir = _dirWant;
            _lastFrameAt = Time.unscaledTime;
            _dirWarmStart = Time.unscaledTime;
            Drive.KV("DIR-BEGIN", "n=" + (index + 1) + " want=" + _dirWant
                     + " delta=(" + delta.x + "," + delta.y + ")"
                     + " target=" + (t.HasValue ? Drive.Grid(t.Value) : "(none)")
                     + " straightTarget=" + (st ? 1 : 0)
                     + " grid=" + Drive.Grid(p.Grid));
        }

        /// <summary>Returns true when this direction tile is finished.</summary>
        private bool TickDirShot(int index)
        {
            var p = Drive.Player();
            if (p == null) { Finish("no-player"); return true; }

            if (_dirPhase == 0)
            {
                KeepWalkingStep();
                if (Time.unscaledTime - _dirWarmStart < DirWarmSeconds) return false;
                Drive.BeginShot(20 + index, _dirTile, "camera");
                _dirPhase = 1;
                return false;
            }

            // waiting for the file: KEEP WALKING so the character is mid-stride and facing `_dirWant`
            KeepWalkingStep();
            long bytes;
            if (!Drive.ShotReady(_dirTile, out bytes))
            {
                if (Time.unscaledTime - _dirWarmStart > 60f)
                {
                    Drive.KV("DIR-TIMEOUT", "n=" + (index + 1) + " name=" + _dirTile);
                    _dirPhase = 0;
                    _keepWalking = false;
                    return true;
                }
                return false;
            }
            _shots++;
            Drive.LogShot("dir-" + _dirWant, 20 + index, _dirTile, "camera");

            var key = Drive.Fmt(Drive.Prop(Drive.Field(Drive.PlayerView(), "Anim"), "CurrentKey"));
            var sprite = Drive.PlayerSpriteName();
            var lower = _dirWant.ToString().ToLowerInvariant();
            var keyOk = key.IndexOf("_" + lower + "_", StringComparison.Ordinal) >= 0;
            var dirOk = p.Dir == _dirWant;
            var spriteReal = sprite != Drive.PlaceholderSpriteName;
            if (dirOk && keyOk && spriteReal) _dirShotsOk++;
            _dirNotes.Add("n=" + (index + 1) + " want=" + _dirWant + " dir=" + p.Dir + " key=" + key
                          + " sprite=" + sprite + " ok=" + ((dirOk && keyOk && spriteReal) ? 1 : 0));

            Drive.KV("DIR-SHOT", "n=" + (index + 1) + " tile=" + _dirTile + " want=" + _dirWant
                     + " dir=" + p.Dir + " dirOk=" + (dirOk ? 1 : 0)
                     + " key=" + key + " keyOk=" + (keyOk ? 1 : 0)
                     + " sprite=" + sprite + " spriteOk=" + (spriteReal ? 1 : 0)
                     + " moving=" + (p.IsMoving ? 1 : 0)
                     + " grid=" + Drive.Grid(p.Grid) + " " + Drive.PlayerAnimLine());
            _dirPhase = 0;
            _keepWalking = false;
            p.Stop();
            return true;
        }

        private int _dirIndex;

        // ================================================================ tour ==============
        private void Step()
        {
            switch (_step)
            {
                // ---- 00 Boot screen: the byline must be readable ------------------------------
                case 0:
                    if (!BootOpen()) { TimeoutStep("boot", 40f); return; }
                    if (!Elapsed(0.6f)) return;      // the original logo art loads asynchronously
                    Drive.KV("BOOT", "scene=" + SceneName() + " fsm=" + Fsm()
                             + " screen=" + Screen.width + "x" + Screen.height
                             + " focused=" + (Application.isFocused ? 1 : 0)
                             + " runInBackground=" + (Application.runInBackground ? 1 : 0));
                    DumpTexts("boot");
                    ProbeByLine("boot", out _byLineBootOk);
                    Drive.BeginShot(1, "p41_01_boot.png", "screen");
                    Next();
                    return;
                case 1:
                    if (!WaitShot(1, "p41_01_boot.png", "screen", "boot", 20f)) return;
                    return;   // WaitShot already advanced
                case 2:
                    if (!Elapsed(1.2f)) return;
                    if (BootOpen()) { Drive.KeyDown("space"); Drive.KeyUp(); }
                    Next();
                    return;

                // ---- 01 Main menu: SINGLE PLAYER / EXIT only + byline -------------------------
                case 3:
                    if (!MenuOpen() || SceneName() != "Menu") { TimeoutStep("mainmenu", 40f); return; }
                    // settle: the menu art (backdrop + original button frames) is loaded through
                    //    Game.Res.LoadAsset, i.e. asynchronously.  Captured immediately the menu is a
                    //    white panel with bare labels (measured in run r1: 38 KB tile) instead of the
                    //    original art -- so wait for it, timed from the moment the panel appeared.
                    if (!_menuSeen) { _menuSeen = true; _stepAt = Time.unscaledTime; }
                    if (!Elapsed(1.6f)) return;
                    Drive.KV("MENU", "scene=" + SceneName() + " fsm=" + Fsm());
                    DumpTexts("menu");
                    ProbeByLine("menu", out _byLineMenuOk);
                    ProbeMenuButtons();
                    Drive.BeginShot(2, "p41_02_menu.png", "screen");
                    Next();
                    return;
                case 4:
                    WaitShot(2, "p41_02_menu.png", "screen", "menu", 20f);
                    return;
                case 5:
                    if (!Elapsed(0.5f)) return;
                    Drive.Click("Single");
                    Next();
                    return;

                // ---- 02 roster -> Stage -------------------------------------------------------
                case 6:
                    if (SelectOpen())
                    {
                        Drive.KV("CHARSELECT", "open=1");
                        Drive.Click("Row0|Enter");
                        Next();
                        return;
                    }
                    if (CreateOpen())
                    {
                        Drive.Log("CHAIN no-roster -> CharCreate; aborting (a saved hero is required)");
                        Finish("no-roster");
                        return;
                    }
                    TimeoutStep("charselect", 25f);
                    return;

                case 7:
                    if (!HudOpen() || Fsm() != "Stage") { TimeoutStep("stage", 60f); return; }
                    Drive.KV("STAGE", "scene=" + SceneName() + " fsm=" + Fsm());
                    Next();
                    return;

                // ---- 03 town overview ---------------------------------------------------------
                // ONE action per step.  `WaitShot` advances the step itself, so two `Next()` calls
                //    in one case silently SKIPPED the following case (measured in run r1: the restore
                //    steps 10/17 were never reached, so the camera kept the probe ortho 12 and the
                //    anchor focus for the rest of the tour -> 6 of the tiles were framed wrongly).
                case 8:
                    if (!Elapsed(ClearSettleSeconds)) return;
                    ProbeTown();
                    Drive.BeginShot(3, "p41_03_town.png", "camera");
                    Next();
                    return;
                case 9:
                    WaitShot(3, "p41_03_town.png", "camera", "town", 20f);
                    return;

                // ---- 03b town wide: let the rig APPLY the override before capturing ----------
                case 10:
                    if (!Elapsed(0.4f)) return;
                    WideShot("town");
                    Next();
                    return;
                case 11:
                    if (!Elapsed(0.4f)) return;
                    Drive.BeginShot(4, "p41_04_town_wide.png", "camera");
                    Next();
                    return;
                case 12:
                    WaitShot(4, "p41_04_town_wide.png", "camera", "town-wide", 20f);
                    return;
                case 13:
                    if (!Elapsed(0.3f)) return;
                    RestoreCamera();
                    Next();
                    return;

                // ---- 04 walk to the exit gap, close-up tile ----------------------------------
                case 14:
                    if (!Elapsed(0.5f)) return;
                    if (!WalkTo(new Vector2Int(19, 27), 30f)) return;
                    Drive.KV("WALKTO", "target=(19,27) arrived=1 grid=" + Drive.Grid(Drive.Player().Grid));
                    Next();
                    return;
                case 15:
                    if (!Elapsed(0.6f)) return;
                    Drive.BeginShot(5, "p41_05_town_exit.png", "camera");
                    Next();
                    return;
                case 16:
                    WaitShot(5, "p41_05_town_exit.png", "camera", "town-exit", 20f);
                    return;
                case 17:
                    Drive.Player().MoveTo(new Vector2Int(17, 27));
                    Drive.KV("EXITSTEP", "target=(17,27) from=" + Drive.Grid(Drive.Player().Grid)
                             + " tileAtTarget=" + Drive.Map().TileAt(new Vector2Int(17, 27)));
                    Next();
                    return;
                case 18:
                    var m = Drive.Map();
                    if (m == null || m.Area != AreaId.BloodMoor) { TimeoutStep("enter-wild", 60f); return; }
                    Drive.KV("EXITSTEP-DONE", "area=" + m.Area + " map=" + m.Width + "x" + m.Height
                             + " spawn=" + Drive.Grid(m.SpawnPoint) + " grid=" + Drive.Grid(Drive.Player().Grid));
                    Next();
                    return;

                // ---- 05 wilderness overview --------------------------------------------------
                case 19:
                    if (!Elapsed(AreaSettleSeconds)) return;
                    ProbeWilderness();
                    Drive.BeginShot(6, "p41_06_wild.png", "camera");
                    Next();
                    return;
                case 20:
                    WaitShot(6, "p41_06_wild.png", "camera", "wild", 20f);
                    return;

                // ---- 05b wilderness wide ------------------------------------------------------
                case 21:
                    if (!Elapsed(0.4f)) return;
                    WideShot("wild");
                    Next();
                    return;
                case 22:
                    if (!Elapsed(0.4f)) return;
                    Drive.BeginShot(7, "p41_07_wild_wide.png", "camera");
                    Next();
                    return;
                case 23:
                    WaitShot(7, "p41_07_wild_wide.png", "camera", "wild-wide", 20f);
                    return;
                case 24:
                    if (!Elapsed(0.3f)) return;
                    RestoreCamera();
                    Next();
                    return;

                // ---- 06 speed windows (run / walk) --------------------------------------------
                case 25:
                    if (!Elapsed(0.5f)) return;
                    BeginMeasure("run");
                    Next();
                    return;
                case 26:
                    if (!TickMeasure("run")) return;
                    Next();
                    return;
                case 27:
                    if (!Elapsed(0.5f)) return;
                    BeginMeasure("walk");
                    Next();
                    return;
                case 28:
                    if (!TickMeasure("walk")) return;
                    Drive.SetRunning(true);
                    Drive.KV("RUNRESTORED", "running=" + (Drive.Player().IsRunning ? 1 : 0));
                    Next();
                    return;

                // ---- 07 four cardinal direction walks (4 tiles) --------------------------------
                case 29:
                case 31:
                case 33:
                case 35:
                    if (!Elapsed(0.3f)) return;
                    BeginDirShot((_step - 29) / 2);
                    Next();
                    return;
                case 30:
                case 32:
                case 34:
                case 36:
                    if (!TickDirShot((_step - 30) / 2)) return;
                    Next();
                    return;

                default:
                    Finish("end");
                    return;
            }
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
