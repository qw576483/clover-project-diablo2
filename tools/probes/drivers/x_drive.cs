// =============================================================================
// x_drive.cs -- X full-system tour.  ONE Play session per run tag (run1 / run2).
//
// Derived from w2_drive.cs (the same station machinery + the same two-cold-start compare);
// the X batch re-captures the whole flow with fresh tiles and adds the X cells:
//   X1  walk trace          a09_walk_1..6.png  (6 frames of ONE continuous walk, >=10 cells)
//   X2  hit trio            x_e1_hit_a/b/c.png (~0.10s apart, same damage event)
//   X3  two normal attacks  x_x3_atk1_a/b.png, x_x3_atk2_a/b.png (wind-up + strike each)
//   X4  cast + projectile   x_e4_cast_a/b.png
//   X5  inventory + equip   x_f1_inventory.png, x_f2_equip_before.png, x_f2_equip.png
//   X6  HUD full screen     x_x6_hud.png
//   X7  stats/skill/quest/minimap  x_x7_charstats.png, x_e3_skilltree.png,
//                           x_g3_questlog_notstarted.png, x_c4_minimap.png
//   X8  pause + options     x_h2_pause.png, x_h2_options_before.png
//   X9  cursor              x_x9_cursor_default.png, x_x9_cursor_hover.png
//   X10 town / moor / den   x_c1_town_default.png, x_c2_moor_default.png, x_c3_den_default.png
//   X11 run1 vs run2        x_compare.tsv (bytes + sha256 + crop diff)
//
//   B1..B5  front end    (boot / main menu / char select / char create / loading)
//   C1..C5  world        (town / wild / cave framing, minimap, area-title popup)
//   D1..D3  movement     (wall detour + NoWalk cursor, follow cam, 8 directions)
//   E1..E4  combat       (hit trio, kill->level up, skill tree, cast + missile)
//   F1..F3  items        (inventory + pickup + tooltip colours, equip + belt, gold)
//   G1..G4  npc + quest  (dialog 4 states, shop/repair, quest log 4 states, chain)
//   H1..H3  death / pause+options / save+exit -> re-enter
//
// HOW IT DRIVES (one Play session): input is injected as REAL InputSystem events
// (`QueueStateEvent` / `QueueTextEvent`) so the whole chain runs
// (InputSystem -> InputSystemUIInputModule(uGUI) / CloverInput -> InputReader -> Player/Npc).
// Tiles are written by the driver itself into <repo>/.ai-tmp/screenshots/
// (outside the Unity project => never imported => the session survives).
//   UI tiles    -> ScreenCapture.CaptureScreenshot (== --source screen)
//   world tiles -> main camera into a RenderTexture + EncodeToPNG (== --source camera)
//
// EVIDENCE SHAPE (read by tools/probes/measure/x_sheet.py -- values only, no verdicts):
//   [X] GRID=<id> tile=<file> vals="<raw measured values>"
//   [X] SHOT-OK n=<i> state=x name=<file> kind=<screen|camera> bytes=<n>
//   [X] CROP n=<i> tile=<file> node=<node> sx0=.. sy0=.. sx1=.. sy1=.. cx=.. cy=..
//   [X] <PROBE>=<verbatim runtime values>
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
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
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Dir8 = Diablo2.Def.Dir8;

namespace X
{
    /// <summary>Shared probe helpers (reflection into the project's internal types).</summary>
    public static class Drive
    {
        internal const string Tag = "X";
        internal const int ShotW = 1920;
        internal const int ShotH = 1080;

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;
        private static string _pendingFile = string.Empty;
        private static string _pendingKind = string.Empty;
        private static int _pendingIndex;
        private static bool _flipY;

        internal static string RawDir { get { return _rawDir; } }
        internal static string DonePath { get { return _done; } }
        internal static bool FlipY { get { return _flipY; } }
        internal static void SetFlipY(bool v) { _flipY = v; }

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
            foreach (var c in Checks) { if (c.EndsWith("=0")) return false; }
            return Checks.Count > 0;
        }

        /// <summary>Make a value safe for a one-line `vals="..."` field.</summary>
        internal static string Esc(string s)
        {
            if (s == null) return "(null)";
            return s.Replace('"', '\'').Replace("\r", " ").Replace("\n", "\\n").Replace("\t", " ");
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

        internal static Diablo2.Module.IPlayerModule Player() { return CtxMember("Player") as Diablo2.Module.IPlayerModule; }
        internal static Diablo2.Module.IMapModule Map() { return CtxMember("Map") as Diablo2.Module.IMapModule; }
        internal static Diablo2.Module.INpcModule Npc() { return CtxMember("Npc") as Diablo2.Module.INpcModule; }
        internal static Diablo2.Module.IQuestModule Quest() { return CtxMember("Quest") as Diablo2.Module.IQuestModule; }
        internal static Diablo2.Module.ISaveModule Save() { return CtxMember("Save") as Diablo2.Module.ISaveModule; }
        internal static Diablo2.Module.IItemModule Item() { return CtxMember("Item") as Diablo2.Module.IItemModule; }
        internal static Diablo2.Module.ISkillModule Skill() { return CtxMember("Skill") as Diablo2.Module.ISkillModule; }
        internal static Diablo2.Module.IMonsterModule Monster() { return CtxMember("Monster") as Diablo2.Module.IMonsterModule; }
        internal static Diablo2.Module.ICombatModule Combat() { return CtxMember("Combat") as Diablo2.Module.ICombatModule; }
        internal static Diablo2.Module.IAudioModule Audio() { return CtxMember("Audio") as Diablo2.Module.IAudioModule; }
        internal static object ViewModule() { return CtxMember("View"); }

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

        internal static string CallStatic(string typeName, string method, object[] args)
        {
            var t = FindType(typeName);
            if (t == null) return "(no-type:" + typeName + ")";
            var m = t.GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) return "(no-method:" + method + ")";
            var r = m.Invoke(null, args);
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

        // ---- player ------------------------------------------------------------------------
        internal static Vector3 PlayerWorld() { var p = Player(); return p != null ? p.World : Vector3.zero; }
        internal static Vector2Int PlayerGrid() { var p = Player(); return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue); }
        internal static bool PlayerMoving() { var p = Player(); return p != null && p.IsMoving; }

        internal static string PlayerSpriteName()
        {
            try
            {
                var view = Field(ViewModule(), "_player");
                var sr = Field(view, "Renderer") as SpriteRenderer;
                if (sr == null) return "(no-renderer)";
                return sr.sprite != null ? sr.sprite.name : "(no-sprite)";
            }
            catch { return "(err)"; }
        }

        /// <summary>Continuous (interpolated) cell coordinate of a world position.</summary>
        internal static bool CellPos(Vector3 world, out Vector2 cell)
        {
            cell = Vector2.zero;
            var t = FindType("Diablo2.Module.Player.PlayerMotor");
            if (t == null) return false;
            var m = t.GetMethod("CellCenterOf", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
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
        internal static T FindPanel<T>() where T : Component
        {
            var a = UnityEngine.Object.FindObjectsByType<T>(FindObjectsSortMode.None);
            return a != null && a.Length > 0 ? a[0] : null;
        }

        internal static Diablo2.UI.CharSelectPanel CharSelectPanel() { return FindPanel<Diablo2.UI.CharSelectPanel>(); }
        internal static Diablo2.UI.CharCreatePanel CharCreate() { return FindPanel<Diablo2.UI.CharCreatePanel>(); }
        internal static Diablo2.UI.NpcDialogPanel Dialog() { return FindPanel<Diablo2.UI.NpcDialogPanel>(); }
        internal static Diablo2.UI.ShopPanel Shop() { return FindPanel<Diablo2.UI.ShopPanel>(); }
        internal static Diablo2.UI.QuestLogPanel QuestLog() { return FindPanel<Diablo2.UI.QuestLogPanel>(); }
        internal static Diablo2.UI.InventoryPanel Inventory() { return FindPanel<Diablo2.UI.InventoryPanel>(); }
        internal static Diablo2.UI.CharacterPanel CharacterSheet() { return FindPanel<Diablo2.UI.CharacterPanel>(); }
        internal static Diablo2.UI.SkillTreePanel SkillTree() { return FindPanel<Diablo2.UI.SkillTreePanel>(); }
        internal static Diablo2.UI.MiniMapPanel MiniMap() { return FindPanel<Diablo2.UI.MiniMapPanel>(); }
        internal static Diablo2.UI.HudPanel Hud() { return FindPanel<Diablo2.UI.HudPanel>(); }
        internal static Diablo2.UI.LoadingPanel Loading() { return FindPanel<Diablo2.UI.LoadingPanel>(); }
        internal static Diablo2.UI.PausePanel Pause() { return FindPanel<Diablo2.UI.PausePanel>(); }
        internal static Diablo2.UI.SettingsPanel Settings() { return FindPanel<Diablo2.UI.SettingsPanel>(); }
        internal static Diablo2.UI.DeathPanel Death() { return FindPanel<Diablo2.UI.DeathPanel>(); }
        internal static Diablo2.UI.BootPanel Boot() { return FindPanel<Diablo2.UI.BootPanel>(); }
        internal static Diablo2.UI.MainMenuPanel MainMenu() { return FindPanel<Diablo2.UI.MainMenuPanel>(); }

        internal static GameObject Node(Transform root, string name)
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t != null && t.gameObject.name == name) return t.gameObject;
            }
            return null;
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

        /// <summary>Bottom-left origin screen rect of a RectTransform (no flip applied).</summary>
        internal static bool ScreenRect(RectTransform rt, out Vector2 lo, out Vector2 hi, out Vector2 center)
        {
            lo = Vector2.zero; hi = Vector2.zero; center = Vector2.zero;
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

        /// <summary>Screen point -> the value the injected mouse device must carry.</summary>
        internal static Vector2 Inject(Vector2 screen)
        {
            return _flipY ? new Vector2(screen.x, Screen.height - screen.y) : screen;
        }

        internal static Vector2 RectInjectPoint(RectTransform rt)
        {
            Vector2 lo, hi, c;
            if (!ScreenRect(rt, out lo, out hi, out c)) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            return Inject(c);
        }

        internal static Vector2 CellInjectPoint(Vector2Int g)
        {
            var cam = Camera.main;
            if (cam == null) return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            var sp = cam.WorldToScreenPoint(Iso.GridToWorld(g));
            return Inject(new Vector2(sp.x, sp.y));
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

        internal static string RectOf(Component c)
        {
            if (c == null) return "-";
            return RectOfComponent(c.transform as RectTransform);
        }

        internal static string RectOfComponent(RectTransform rt)
        {
            Vector2 lo, hi, c;
            if (!ScreenRect(rt, out lo, out hi, out c)) return "(no-rect)";
            return "screen=" + c.x.ToString("0") + "," + c.y.ToString("0")
                + " sx0=" + lo.x.ToString("0") + " sy0=" + lo.y.ToString("0")
                + " sx1=" + hi.x.ToString("0") + " sy1=" + hi.y.ToString("0")
                + " wh=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0");
        }

        internal static void DumpTextsUnder(string where, Transform root, int limit)
        {
            var n = 0;
            var sb = new StringBuilder();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Text>(FindObjectsSortMode.None))
            {
                if (t == null || t.text == null) continue;
                if (root != null && !t.transform.IsChildOf(root) && t.transform != root) continue;
                Vector2 lo, hi, c;
                var has = ScreenRect(t.rectTransform, out lo, out hi, out c);
                n++;
                if (n <= limit)
                {
                    sb.Append('|').Append(PathOf(t.transform))
                      .Append("\"").Append(Esc(t.text)).Append("\"")
                      .Append(" size=").Append(t.fontSize)
                      .Append(" color=").Append(t.color.r.ToString("0.000")).Append(',')
                      .Append(t.color.g.ToString("0.000")).Append(',').Append(t.color.b.ToString("0.000"))
                      .Append(" active=").Append(t.gameObject.activeInHierarchy ? 1 : 0);
                    if (has)
                        sb.Append(" screen=").Append(c.x.ToString("0")).Append(',').Append(c.y.ToString("0"))
                          .Append(" rect=").Append((hi.x - lo.x).ToString("0")).Append('x').Append((hi.y - lo.y).ToString("0"));
                    sb.Append(' ');
                }
            }
            KV("UITEXTS", "where=" + where + " count=" + n + " " + sb.ToString());
        }

        /// <summary>Recursive RectTransform dump (geometry + sprite + text) -- the on-grid values.</summary>
        internal static void DumpTree(string where, Transform root, int limit, int maxDepth)
        {
            if (root == null) { KV("UITREE", "where=" + where + " (no root)"); return; }
            var sb = new StringBuilder();
            var n = 0;
            DumpWalk(root, 0, maxDepth, limit, ref n, sb);
            KV("UITREE", "where=" + where + " nodes=" + n + " " + sb);
        }

        private static void DumpWalk(Transform t, int depth, int maxDepth, int limit, ref int n, StringBuilder sb)
        {
            var rt = t as RectTransform;
            if (rt != null)
            {
                n++;
                if (n <= limit)
                {
                    Vector2 lo, hi, c;
                    var has = ScreenRect(rt, out lo, out hi, out c);
                    sb.Append('|').Append(depth).Append(':').Append(t.name);
                    sb.Append(" size=").Append(rt.sizeDelta.x.ToString("0.#")).Append('x').Append(rt.sizeDelta.y.ToString("0.#"));
                    sb.Append(" apos=").Append(rt.anchoredPosition.x.ToString("0.#")).Append(',').Append(rt.anchoredPosition.y.ToString("0.#"));
                    if (has)
                        sb.Append(" screen=").Append(c.x.ToString("0")).Append(',').Append(c.y.ToString("0"))
                          .Append(" wh=").Append((hi.x - lo.x).ToString("0")).Append('x').Append((hi.y - lo.y).ToString("0"));
                    sb.Append(" act=").Append(t.gameObject.activeInHierarchy ? 1 : 0);
                    var img = t.GetComponent<Image>();
                    if (img != null)
                        sb.Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "none")
                          .Append(" col=").Append(img.color.r.ToString("0.00")).Append('/')
                          .Append(img.color.g.ToString("0.00")).Append('/').Append(img.color.b.ToString("0.00"));
                    var txt = t.GetComponent<Text>();
                    if (txt != null)
                        sb.Append(" text=\"").Append(Esc(txt.text)).Append("\" fsize=").Append(txt.fontSize)
                          .Append(" tcol=").Append(txt.color.r.ToString("0.000")).Append('/')
                          .Append(txt.color.g.ToString("0.000")).Append('/').Append(txt.color.b.ToString("0.000"));
                    sb.Append(' ');
                }
            }
            if (depth >= maxDepth) return;
            for (var i = 0; i < t.childCount; i++) DumpWalk(t.GetChild(i), depth + 1, maxDepth, limit, ref n, sb);
        }

        internal static Button FindButton(string arg, out string diag)
        {
            diag = string.Empty;
            var pathSub = string.Empty;
            var want = arg ?? string.Empty;
            var bar = want.IndexOf('|');
            if (bar >= 0) { pathSub = want.Substring(0, bar); want = want.Substring(bar + 1); }

            var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
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

        internal static string ButtonSprite(Button b)
        {
            if (b == null) return "(no-button)";
            var img = b.targetGraphic as Image;
            if (img == null) img = b.GetComponent<Image>();
            return "name=" + b.gameObject.name + " interactable=" + (b.interactable ? 1 : 0)
                   + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "none")
                   + " " + (img != null ? RectOf(img) : "(no-img)");
        }

        internal static string ButtonLabel(Button b)
        {
            if (b == null) return "-";
            var t = b.GetComponentInChildren<Text>(true);
            return t != null ? t.text : "-";
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
                case "tab": key = Key.Tab; return true;
                case "i": key = Key.I; return true;
                case "c": key = Key.C; return true;
                case "q": key = Key.Q; return true;
                case "t": key = Key.T; return true;
                case "r": key = Key.R; return true;
                case "w": key = Key.W; return true;
                case "1": key = Key.Digit1; return true;
                case "2": key = Key.Digit2; return true;
                case "3": key = Key.Digit3; return true;
                case "4": key = Key.Digit4; return true;
                case "f1": key = Key.F1; return true;
                case "f2": key = Key.F2; return true;
                case "backspace": key = Key.Backspace; return true;
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
        internal static string World(Vector3 w) { return "(" + w.x.ToString("0.00") + "," + w.y.ToString("0.00") + ")"; }
        internal static string V(Vector2 v) { return "(" + v.x.ToString("0.0") + "," + v.y.ToString("0.0") + ")"; }
    }
}
namespace X
{
    /// <summary>Public one-shot entries for the run script.</summary>
    public static class Api2
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }

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

            var added = 0;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>(); added = 1; }
            var mouseAdded = 0;
            if (UnityEngine.InputSystem.Mouse.current == null) { InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>(); mouseAdded = 1; }

            var bgm = -1f; var sfx = -1f; var quality = -999;
            try
            {
                if (Game.Setting != null)
                {
                    bgm = Game.Setting.Get<float>(GameConst.SettingKeyBgmVolume, -1f);
                    sfx = Game.Setting.Get<float>(GameConst.SettingKeySfxVolume, -1f);
                    quality = Game.Setting.Get<int>("video/quality", -999);
                }
            }
            catch (Exception ex) { Drive.Warn("CFG setting read failed: " + ex.GetType().Name); }

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
            Drive.KV("STARTUP-SETTINGS", "bgm=" + bgm.ToString("0.00") + " sfx=" + sfx.ToString("0.00")
                + " video_quality=" + quality + " engineQuality=" + QualitySettings.GetQualityLevel()
                + " note=run1 shows the pre-existing persisted values; run2 shows the values written by run1");
            return line;
        }

        /// <summary>Queue a mouse move so the injection convention can be read back.</summary>
        public static string ProbeMouse(string spec)
        {
            var parts = (spec ?? string.Empty).Split(',');
            float x = 300f, y = 240f;
            if (parts.Length > 1) { float.TryParse(parts[0], out x); float.TryParse(parts[1], out y); }
            Drive.MouseState(new Vector2(x, y), false);
            return "PROBE-MOUSE-QUEUED x=" + x + " y=" + y;
        }

        public static string Paths(string spec) { return Drive.Paths(spec); }
    }

    /// <summary>Installer. spec = "tour|&lt;tag&gt;|&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("XEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Drive.Log("TOUR-INSTALL spec=" + spec + " sceneCount=" + UnityEngine.SceneManagement.SceneManager.sceneCount
                      + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One tour, one Play session, ~60 stations across the whole game flow.
    /// The station list is a list of `Func&lt;bool&gt;` (true = station finished), so the
    /// execution order is the source order and no step numbering can drift.
    /// </summary>
    public partial class Driver : MonoBehaviour
    {
        private string _tag = "run1";
        // station completes.  Existing 4-field specs keep the full-tour behaviour (empty = no stop).
        // Purpose: re-capture a SINGLE deep tile without re-running the whole ~520s tour, so the
        // runner never has to clear the other 82 delivered tiles.
        private string _stopAfter = string.Empty;
        private string _rawDir = string.Empty;
        private string _done = string.Empty;

        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _doneRun;
        private int _shots;
        private int _shotSeq;
        private readonly List<string> _notes = new List<string>();
        private string _timedOutAt = string.Empty;

        private string _pendTile = string.Empty;
        private string _pendKind = string.Empty;

        // ---- frame pacing ---------------------------------------------------------
        private readonly List<float> _pacing = new List<float>();
        private bool _pacingLogged;

        // ---- event counters -------------------------------------------------------
        private int _dmgDealt, _kills, _levelUps, _picks, _attacks, _questChanges, _playerDied, _moveCmds;
        private int _cursorChanges;
        private string _lastCursor = "?";
        private string _lastHover = "?";

        // ---- audio watch (real AudioSource "started playing" transitions) ---------
        private bool _audioWatch;
        private readonly HashSet<string> _audioClips = new HashSet<string>();
        private int _audioTick;

        // ---- char creation -------------------------------------------------------
        private string _newHeroName = string.Empty;
        private int _typeIdx;
        private string _typeStr = string.Empty;
        private bool _typePicked;
        private float _elapsedBoot = -1f;

        // ---- mouse UI click sequencer -------------------------------------------
        private Vector2 _pendingUiClick;
        private string _pendingUiClickName = string.Empty;
        private int _uiClickPhase;
        private int _uiClickFrames;

        // ---- ground click sequencer ---------------------------------------------
        private Vector2Int _clickWant;
        private int _ckPhase = -1;
        private int _ckFrames;
        private Vector2 _ckPos;
        private int _ckMoveBefore = -1;
        private bool _ckFlipTried;
        private bool _ckFlipY;

        // ---- hold-attack sequencer ---------------------------------------------
        private int _holdMonster = -1;
        private int _holdFrames;
        private Vector2Int _holdGrid;

        // ---- per-station scratch -------------------------------------------------
        private Vector2Int _nowalkCell = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _detourFrom = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _detourTo = new Vector2Int(int.MinValue, int.MinValue);
        private int _detourBlocked = -1;
        private int _detourOnBlocked;
        private int _pendingMonster = -1;
        private int _tiIdx;
        private int _d3Seen;
        private readonly HashSet<int> _d3Got = new HashSet<int>();
        private int _d3LegIdx;
        private float _h1GoldBefore = -1;
        private int _counterA;
        private int _counterB;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 1) _tag = parts[1];
            if (parts.Length > 2) _rawDir = parts[2];
            if (parts.Length > 3) _done = parts[3];
            if (parts.Length > 4) _stopAfter = parts[4];
            Drive.Paths(_rawDir + "|" + _done);
            Drive.Log("DRIVER-INIT spec=" + spec + " tag=" + _tag + " frame=" + Time.frameCount
                      + " rawDir=" + _rawDir + " stopAfter=" + (_stopAfter.Length > 0 ? _stopAfter : "(none)")
                      + " screen=" + Screen.width + "x" + Screen.height);
            Subscribe();
            LogDevice();
            BuildPlanAll();
        }

        /// <summary>
        /// Assemble every station group in execution order.  Each group lives in its own partial
        /// part of this file; `Add` appends in call order, so this ordering IS the tour order.
        /// </summary>
        private void BuildPlanAll()
        {
            BuildPlan();          // calibration, B1..B5, C1..C5, D1..D3
            BuildPlanItems();     // G1a/G3a, G2 shop+repair, F1 inventory+tooltips+pickup, F3 gold, G1b accept
            BuildPlanWorld();     // leave town, BloodMoor (C2/C5), E1 bare, F2 equip+belt, E1 trio, E2 level up, den entry (C3/C5)
            BuildPlanDen();       // E3 skill tree, E4 learn+cast, G4 clear the den, walk back, G1c/G3c, turn in, G1d/G3d
            BuildPlanEnd();       // H1 death+revive, H3 save&exit+re-enter, H2 pause/options/main menu/quit
            Drive.KV("PLAN", "stations=" + _plan.Count + " (one Play session per run tag)");
        }

        private void Subscribe()
        {
            if (Game.Event == null) { Drive.Warn("SUBSCRIBE Game.Event is null"); return; }
            Game.Event.On<Diablo2.Def.DamageArgs>(Diablo2.Core.Events.DamageDealt, OnDamage);
            Game.Event.On<int>(Diablo2.Core.Events.MonsterKilled, OnKilled);
            Game.Event.On<int>(Diablo2.Core.Events.LevelUp, OnLevelUp);
            Game.Event.On<Diablo2.Def.ItemStack>(Diablo2.Core.Events.ItemPicked, OnPicked);
            Game.Event.On<int>(Diablo2.Core.Events.PlayerAttacked, OnAttacked);
            Game.Event.On<Diablo2.Def.QuestStateDto>(Diablo2.Core.Events.QuestChanged, OnQuest);
            Game.Event.On(Diablo2.Core.Events.PlayerDied, OnDied);
            Game.Event.On<Vector2Int>(Diablo2.Core.Events.MoveCommand, OnMove);
            Game.Event.On<Diablo2.Def.CursorKind>(Diablo2.Core.Events.CursorChanged, OnCursor);
            Game.Event.On<Diablo2.Def.HoverTarget>(Diablo2.Core.Events.HoverTargetChanged, OnHover);
            Drive.KV("SUBSCRIBE", "events=DamageDealt,MonsterKilled,LevelUp,ItemPicked,PlayerAttacked,QuestChanged,"
                + "PlayerDied,MoveCommand,CursorChanged,HoverTargetChanged (driver assembly)");
        }

        private void OnDamage(Diablo2.Def.DamageArgs a) { _dmgDealt++; }
        private void OnKilled(int id) { _kills++; }
        private void OnLevelUp(int lv) { _levelUps++; }
        private void OnPicked(Diablo2.Def.ItemStack s) { _picks++; }
        private void OnAttacked(int id) { _attacks++; }
        private void OnQuest(Diablo2.Def.QuestStateDto q) { _questChanges++; }
        private void OnDied() { _playerDied++; }
        private void OnMove(Vector2Int g) { _moveCmds++; }
        private void OnCursor(Diablo2.Def.CursorKind k) { _cursorChanges++; _lastCursor = k.ToString(); }

        private void OnHover(Diablo2.Def.HoverTarget t)
        {
            _lastHover = "hasTarget=" + (t.hasTarget ? 1 : 0) + " cursor=" + t.cursor + " id=" + t.id
                         + " cell=(" + t.gridX + "," + t.gridY + ") name=\"" + Drive.Esc(t.name) + "\"";
        }

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
            TickUiClick();
            TickHold();
            WatchAudio();
            if (_doneRun) return;
            try
            {
                var guard = 0;
                while (!_doneRun && _pc < _plan.Count && guard++ < 4000)
                {
                    var f = _plan[_pc];
                    if (!f()) return;
                    _pc++;
                    if (_stopAfter.Length > 0 && _cur == _stopAfter)
                    {
                        Drive.KV("STOP-AFTER", "station=" + _stopAfter + " pc=" + _pc
                            + " fsm=" + Fsm() + " shots=" + _shots);
                        Finish("stop-after:" + _stopAfter);
                        return;
                    }
                    _cur = "?";
                    _at = Time.unscaledTime;
                }
                if (_pc >= _plan.Count) Finish("end");
            }
            catch (Exception ex)
            {
                Drive.Log("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Note("fatal:" + _cur + ":" + ex.GetType().Name);
                _pc++;
                _cur = "?";
                _at = Time.unscaledTime;
            }
        }

        private void LateUpdate()
        {
            try { Drive.FlushShot(); }
            catch (Exception ex) { Drive.Warn("FLUSH-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ================================================================ pacing ==========
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
                + " (ms) graphicsDeviceName=\"" + SafeDevice() + "\""
                + " resolution=" + Screen.width + "x" + Screen.height);
        }

        private static string SafeDevice()
        {
            try { return SystemInfo.graphicsDeviceName; } catch { return "(unknown)"; }
        }

        // ================================================================ audio watch =====
        private void WatchAudio()
        {
            if (!_audioWatch) return;
            _audioTick++;
            if (_audioTick % 5 != 0) return;
            try
            {
                var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
                for (var i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i];
                    if (s == null || !s.isPlaying || s.clip == null) continue;
                    if (s.time > 0.40f) continue;
                    _audioClips.Add(s.clip.name);
                }
            }
            catch { }
        }

        private string AudioList()
        {
            var arr = new string[_audioClips.Count];
            _audioClips.CopyTo(arr);
            Array.Sort(arr);
            return "[" + string.Join(",", arr) + "]";
        }

        // ================================================================ station plumbing =
        private void Note(string s)
        {
            if (_notes.Count < 200) _notes.Add(s);
            Drive.KV("NOTE", s);
        }

        private bool Elapsed(float seconds) { return Time.unscaledTime - _at >= seconds; }

        /// <summary>Timeout guard for a station: notes it and returns true (advance, never abort).</summary>
        private bool Tout(string what, float seconds)
        {
            if (!Elapsed(seconds)) return false;
            Drive.KV("STATION-TIMEOUT", "at=" + what + " elapsed=" + (Time.unscaledTime - _at).ToString("0.0")
                + " " + State());
            Note("timeout:" + what);
            if (_timedOutAt.Length == 0) _timedOutAt = what;
            return true;
        }

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
                    Drive.Log("PHASE " + nm + " " + State());
                }
                return fn();
            });
        }

        private void Log(string k, string v) { Drive.KV(k, v); }

        private string State()
        {
            var p = Drive.Player();
            var m = Drive.Map();
            return "pc=" + _pc + " fsm=" + Fsm() + " scene=" + SceneName()
                   + " area=" + (m != null ? m.Area.ToString() : "(no-map)")
                   + " map=" + (m != null ? m.Width + "x" + m.Height : "-")
                   + " grid=" + (p != null ? Drive.Grid(p.Grid) : "-")
                   + " hp=" + (p != null ? p.Life + "/" + p.MaxLife : "-")
                   + " lvl=" + (p != null ? p.Level : -1)
                   + " gold=" + (p != null ? p.Gold : -1)
                   + " moving=" + (p != null && p.IsMoving ? 1 : 0)
                   + " t=" + Time.time.ToString("0.00");
        }

        private string PLine(string where)
        {
            var p = Drive.Player();
            if (p == null) return "where=" + where + " (no player)";
            return "where=" + where + " name=\"" + Drive.Esc(p.Name) + "\" cls=" + p.Class + " lvl=" + p.Level
                + " exp=" + p.Exp + "/" + p.ExpNext
                + " str=" + p.Str + " dex=" + p.Dex + " vit=" + p.Vit + " eng=" + p.Eng
                + " life=" + p.Life + "/" + p.MaxLife + " mana=" + p.Mana + "/" + p.MaxMana
                + " stam=" + p.Stamina + "/" + p.MaxStamina
                + " def=" + p.Defense + " ar=" + p.AttackRating
                + " statPts=" + p.StatPoints + " skillPts=" + p.SkillPoints + " gold=" + p.Gold
                + " grid=" + Drive.Grid(p.Grid) + " dir=" + p.Dir
                + " moving=" + (p.IsMoving ? 1 : 0) + " running=" + (p.IsRunning ? 1 : 0)
                + " dead=" + (p.IsDead ? 1 : 0);
        }

        private static bool BootOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>(); }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool CreateOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharCreatePanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool DialogIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.NpcDialogPanel>(); }
        private static bool ShopIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.ShopPanel>(); }
        private static bool QuestLogIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.QuestLogPanel>(); }
        private static bool InvIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.InventoryPanel>(); }
        private static bool SkillTreeIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SkillTreePanel>(); }
        private static bool MiniMapIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>(); }
        private static bool PauseIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.PausePanel>(); }
        private static bool SettingsIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.SettingsPanel>(); }
        private static bool DeathIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.DeathPanel>(); }
        private static bool LoadingIsOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.LoadingPanel>(); }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }

        private Vector2 PlayerScreenPos()
        {
            var cam = Camera.main;
            if (cam == null) return Vector2.zero;
            var sp = cam.WorldToScreenPoint(Drive.PlayerWorld());
            return new Vector2(sp.x, sp.y);
        }

        // ================================================================ shot plumbing ====
        private void Shoot(string id, string tile, string kind, string vals)
        {
            Drive.Log("GRID=" + id + " tile=" + tile + " vals=\"" + Drive.Esc(vals) + "\"");
            var idx = _shotSeq++;
            Drive.BeginShot(idx, tile, kind);
            _pendTile = tile;
            _pendKind = kind;
            _shotAt = Time.unscaledTime;
        }

        /// <summary>
        /// true when the pending tile landed (or the budget ran out).
        /// The budget is measured from the shot request, NOT from the station entry, so a
        /// station that runs a loop over several tiles still times each tile out on its own.
        /// </summary>
        private bool ShotLanded(float budget)
        {
            if (_pendTile.Length == 0) return true;
            long b;
            if (Drive.ShotReady(_pendTile, out b))
            {
                _shots++;
                Drive.LogShot("x", _shotSeq - 1, _pendTile, _pendKind);
                _pendTile = string.Empty;
                return true;
            }
            if (Time.unscaledTime - _shotAt >= budget)
            {
                Drive.KV("SHOT-TIMEOUT", "tile=" + _pendTile + " waited=" + budget.ToString("0.0") + "s");
                Note("shot-timeout:" + _pendTile);
                if (_timedOutAt.Length == 0) _timedOutAt = "shot:" + _pendTile;
                _pendTile = string.Empty;
                return true;
            }
            return false;
        }

        // ================================================================ ui / ground click
        private void QueueUiClick(Vector2 pos, string nodeName)
        {
            _pendingUiClick = pos;
            _pendingUiClickName = nodeName;
            Drive.MouseState(pos, false);
            _uiClickPhase = 1;
            _uiClickFrames = 2;
        }

        private void TickUiClick()
        {
            if (_uiClickPhase == 0) return;
            _uiClickFrames--;
            if (_uiClickFrames > 0) return;
            if (_uiClickPhase == 1)
            {
                Drive.MouseState(_pendingUiClick, true);
                Drive.KV("UI-DOWN", "pos=" + Drive.V(_pendingUiClick) + " node=" + _pendingUiClickName
                    + " frame=" + Time.frameCount + " moveCmds=" + _moveCmds);
                _uiClickPhase = 2;
                _uiClickFrames = 2;
                return;
            }
            Drive.MouseState(_pendingUiClick, false);
            Drive.KV("UI-UP", "pos=" + Drive.V(_pendingUiClick) + " node=" + _pendingUiClickName
                + " frame=" + Time.frameCount + " moveCmds=" + _moveCmds);
            _uiClickPhase = 0;
            _uiClickFrames = 0;
        }

        /// <summary>Real-mouse click on a Button found by GameObject name ("path|name" filter optional).</summary>
        private bool ClickNamedRealMouse(string name, string tag)
        {
            string diag;
            var b = Drive.FindButton(name, out diag);
            if (b == null)
            {
                Drive.Warn("UI-BUTTON-MISS name=" + name + " " + diag + " -> legacy ExecuteEvents path");
                Drive.Click(name);
                return false;
            }
            var rt = b.transform as RectTransform;
            Vector2 lo, hi, c;
            Drive.ScreenRect(rt, out lo, out hi, out c);
            var p = Drive.Inject(c);
            Drive.KV("CLICKTOP", "tag=" + tag + " name=" + name + " screen=" + Drive.V(c)
                + " interactable=" + (b.interactable ? 1 : 0)
                + " label=\"" + Drive.Esc(Drive.ButtonLabel(b)) + "\""
                + " raycastTop=\"" + Drive.RaycastTop(c) + "\" " + diag);
            QueueUiClick(p, name);
            return true;
        }

        /// <summary>Real-mouse click on a named node inside a panel (no Button required).</summary>
        private bool ClickNodeRealMouse(string panelNodeName, string nodeName, string tag)
        {
            var root = FindPanelObject(panelNodeName);
            var go = root != null ? Drive.Node(root.transform, nodeName) : null;
            if (go == null) { Drive.Warn("UI-NODE-MISS root=" + panelNodeName + " node=" + nodeName); return false; }
            var rt = go.transform as RectTransform;
            Vector2 lo, hi, c;
            Drive.ScreenRect(rt, out lo, out hi, out c);
            var p = Drive.Inject(c);
            var btn = go.GetComponent<Button>();
            Drive.KV("CLICKTOP", "tag=" + tag + " panel=" + panelNodeName + " node=" + nodeName
                + " screen=" + Drive.V(c)
                + " rect=" + (hi.x - lo.x).ToString("0") + "x" + (hi.y - lo.y).ToString("0")
                + " interactable=" + (btn != null ? (btn.interactable ? 1 : 0) : -1)
                + " raycastTop=\"" + Drive.RaycastTop(c) + "\"");
            QueueUiClick(p, nodeName);
            return true;
        }

        private GameObject FindPanelObject(string rootName)
        {
            switch (rootName)
            {
                case "InventoryPanel": return Wrap(Drive.Inventory());
                case "ShopPanel": return Wrap(Drive.Shop());
                case "QuestLogPanel": return Wrap(Drive.QuestLog());
                case "SkillTreePanel": return Wrap(Drive.SkillTree());
                case "MiniMapPanel": return Wrap(Drive.MiniMap());
                case "NpcDialogPanel": return Wrap(Drive.Dialog());
                case "PausePanel": return Wrap(Drive.Pause());
                case "SettingsPanel": return Wrap(Drive.Settings());
                case "DeathPanel": return Wrap(Drive.Death());
                case "HudPanel": return Wrap(Drive.Hud());
                case "CharSelectPanel": return Wrap(Drive.CharSelectPanel());
                case "CharCreatePanel": return Wrap(Drive.CharCreate());
                case "MainMenuPanel": return Wrap(Drive.MainMenu());
                case "BootPanel": return Wrap(Drive.Boot());
                case "LoadingPanel": return Wrap(Drive.Loading());
            }
            return null;
        }

        private static GameObject Wrap(Component c) { return c != null ? c.gameObject : null; }

        private void BeginGroundClick(Vector2Int grid)
        {
            _clickWant = grid;
            _ckPhase = 0;
            _ckFrames = 0;
            _ckMoveBefore = -1;
            _ckFlipTried = false;
            _ckFlipY = Drive.FlipY;
            Drive.KV("CK-BEGIN", "want=" + Drive.Grid(grid) + " tileKind=" + Drive.TileKindAt(grid)
                + " walkable=" + (Drive.Walkable(grid) ? 1 : 0)
                + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
        }

        /// <summary>0 = running, 1 = done, -1 = gave up.</summary>
        private int TickGroundClickSeq()
        {
            var cam = Camera.main;
            if (cam == null) return 0;
            _ckFrames++;

            switch (_ckPhase)
            {
                case 0:
                    {
                        var sp = cam.WorldToScreenPoint(Iso.GridToWorld(_clickWant));
                        var raw = new Vector2(sp.x, _ckFlipY ? Screen.height - sp.y : sp.y);
                        _ckPos = raw;
                        Drive.MouseState(_ckPos, false);
                        Drive.KV("CK-POS", "want=" + Drive.Grid(_clickWant) + " worldScreen=" + Drive.V(sp)
                            + " injected=" + Drive.V(_ckPos) + " flipY=" + (_ckFlipY ? 1 : 0));
                        _ckPhase = 1;
                        _ckFrames = 0;
                        return 0;
                    }
                case 1:
                    if (_ckFrames < 3) return 0;
                    {
                        var got = Iso.ScreenToGrid(cam, Drive.MousePosNow());
                        Drive.KV("CK-PROJ", "want=" + Drive.Grid(_clickWant) + " got=" + Drive.Grid(got)
                            + " ok=" + (got == _clickWant ? 1 : 0) + " flipY=" + (_ckFlipY ? 1 : 0));
                        if (got == _clickWant) { _ckPhase = 2; _ckFrames = 0; return 0; }
                        if (!_ckFlipTried)
                        {
                            _ckFlipTried = true;
                            _ckFlipY = !_ckFlipY;
                            Drive.Warn("CK-CALIB projection mismatch -> retry flipped Y");
                            _ckPhase = 0;
                            _ckFrames = 0;
                            return 0;
                        }
                        return FallbackGroundClick("projection-mismatch");
                    }
                case 2:
                    if (_ckFrames < 2) return 0;
                    _ckMoveBefore = _moveCmds;
                    Drive.MouseState(_ckPos, true);
                    Drive.KV("CK-DOWN", "want=" + Drive.Grid(_clickWant) + " raycastTop=\"" + Drive.RaycastTop(_ckPos)
                        + "\" moveCmdsBefore=" + _ckMoveBefore + " frame=" + Time.frameCount);
                    _ckPhase = 3;
                    _ckFrames = 0;
                    return 0;
                case 3:
                    if (_ckFrames < 2) return 0;
                    Drive.MouseState(_ckPos, false);
                    _ckPhase = 4;
                    _ckFrames = 0;
                    return 0;
                case 4:
                    if (_ckFrames < 3) return 0;
                    Drive.KV("CK-DONE", "want=" + Drive.Grid(_clickWant)
                        + " walkable=" + (Drive.Walkable(_clickWant) ? 1 : 0)
                        + " tileKind=" + Drive.TileKindAt(_clickWant)
                        + " moveCmdDelta=" + (_moveCmds - _ckMoveBefore)
                        + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                        + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                    _ckPhase = -1;
                    return 1;
                case -1:
                    return 1;
            }
            return 0;
        }

        private int FallbackGroundClick(string why)
        {
            _ckMoveBefore = _moveCmds;
            Drive.HandlePrimaryClickFallback(_clickWant);
            Drive.KV("CK-FALLBACK", "want=" + Drive.Grid(_clickWant) + " why=" + why
                + " via=PlayerModule.HandlePrimaryClick(reflection)"
                + " moveCmdDelta=" + (_moveCmds - _ckMoveBefore)
                + " note=the real-mouse chain did NOT deliver this click");
            _ckPhase = -1;
            return -1;
        }

        // ================================================================ hold-attack =====
        private void BeginHold(int monsterId, string tag)
        {
            _holdMonster = monsterId;
            _holdFrames = 0;
            _subAt = Time.unscaledTime;
            var mon = Drive.Monster();
            var st = mon != null ? mon.Get(monsterId) : null;
            _holdGrid = st != null ? new Vector2Int(st.gridX, st.gridY) : new Vector2Int(int.MinValue, int.MinValue);
            Drive.KV("HOLD-BEGIN", "tag=" + tag + " m#" + monsterId
                + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                + " cell=" + Drive.Grid(_holdGrid)
                + " attacks=" + _attacks + " kills=" + _kills + " damageSeen=" + _dmgDealt
                + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                + " audio=" + AudioList());
            _audioWatch = true;
        }

        /// <summary>Hold the left button over the monster's current cell (real pointer chain).</summary>
        private void TickHold()
        {
            if (_holdMonster < 0) return;
            var mon = Drive.Monster();
            var st = mon != null ? mon.Get(_holdMonster) : null;
            if (st != null && st.alive)
            {
                _holdGrid = new Vector2Int(st.gridX, st.gridY);
                _holdFrames++;
                Drive.MouseState(Drive.CellInjectPoint(_holdGrid), true);
            }
        }

        private void EndHold(string tag)
        {
            if (_holdMonster < 0) return;
            var mon = Drive.Monster();
            var st = mon != null ? mon.Get(_holdMonster) : null;
            Drive.MouseState(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
            Drive.KV("HOLD-END", "tag=" + tag + " m#" + _holdMonster + " frames=" + _holdFrames
                + " alive=" + (st != null && st.alive ? 1 : 0)
                + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                + " attacks=" + _attacks + " kills=" + _kills + " damageSeen=" + _dmgDealt
                + " moveCmds=" + _moveCmds + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                + " audio=" + AudioList());
            _holdMonster = -1;
            _holdFrames = 0;
        }

        // ================================================================ path picking =====
        private bool PickDetourPair(out Vector2Int from, out Vector2Int to)
        {
            from = new Vector2Int(int.MinValue, int.MinValue);
            to = new Vector2Int(int.MinValue, int.MinValue);
            var map = Drive.Map();
            if (map == null) return false;
            var bestGain = 0;
            var step = Mathf.Max(1, map.Width / 28);
            for (var x = 2; x < map.Width - 2; x += step)
            {
                for (var y = 2; y < map.Height - 2; y += step)
                {
                    var a = new Vector2Int(x, y);
                    if (!map.Walkable(a)) continue;
                    for (var k = 0; k < 8; k++)
                    {
                        var del = Iso.DirectionDelta((Dir8)k);
                        var cand = new Vector2Int(x + del.x * 3, y + del.y * 3);
                        if (!map.InBounds(cand) || !map.Walkable(cand)) continue;
                        var man = Mathf.Max(Mathf.Abs(cand.x - a.x), Mathf.Abs(cand.y - a.y));
                        if (man < 2) continue;
                        var blockedOnLine = false;
                        for (var t = 1; t < man; t++)
                        {
                            var mid = new Vector2Int(a.x + (cand.x - a.x) * t / man, a.y + (cand.y - a.y) * t / man);
                            if (!map.Walkable(mid)) { blockedOnLine = true; break; }
                        }
                        if (!blockedOnLine) continue;
                        var path = map.FindPath(a, cand);
                        if (path == null || path.Count < 2) continue;
                        // !!! the A* path must NOT cross an Exit cell: stepping on one fires
                        // PlayerModule.CheckExit and switches the area mid-station (measured: a
                        // detour pair whose path went through the town gate (17,27) threw the
                        // player into BloodMoor and every later station ran in the wrong area).
                        var crossesExit = false;
                        for (var i = 0; i < path.Count && !crossesExit; i++)
                        {
                            if (map.TileAt(path[i]) == TileKind.Exit) crossesExit = true;
                        }
                        if (crossesExit) continue;
                        if (path.Count > 26) continue;                 // keep the walk local
                        var gain = (path.Count - 1) - man;
                        if (gain <= bestGain) continue;
                        bestGain = gain;
                        from = a;
                        to = cand;
                    }
                }
            }
            return to.x != int.MinValue;
        }

        private bool PickEightDirSpot(out Vector2Int spot)
        {
            spot = new Vector2Int(int.MinValue, int.MinValue);
            var map = Drive.Map();
            if (map == null) return false;
            for (var x = 3; x < map.Width - 3; x++)
            {
                for (var y = 3; y < map.Height - 3; y++)
                {
                    var ok = true;
                    for (var k = 0; k < 8 && ok; k++)
                    {
                        var d = Iso.DirectionDelta((Dir8)k);
                        for (var m = 1; m <= 2 && ok; m++)
                        {
                            var g = new Vector2Int(x + d.x * m, y + d.y * m);
                            if (!map.Walkable(g) || map.TileAt(g) == TileKind.Exit) ok = false;
                        }
                    }
                    if (!ok) continue;
                    spot = new Vector2Int(x, y);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Snap the camera rig to the player (after a probe teleport the rig lerps otherwise).</summary>
        private void SnapCam()
        {
            try
            {
                var rig = Drive.CtxMember("Camera") as Diablo2.Module.ICameraRig;
                if (rig == null) { Drive.Warn("SnapCam: ICameraRig not available"); return; }
                rig.SnapToTarget();
                Drive.KV("CAM-SNAP", "SnapToTarget called (probe positioning after a teleport)");
            }
            catch (Exception ex) { Drive.Warn("SnapCam failed: " + ex.GetType().Name); }
        }

        private bool Hop(Vector2Int g, string why)
        {
            var p = Drive.Player();
            if (p == null) { Drive.Warn("HOP no player"); return false; }
            if (!Drive.Walkable(g)) { Drive.Warn("HOP target not walkable " + Drive.Grid(g) + " why=" + why); return false; }
            p.TeleportTo(g);
            Drive.KV("HOP", "why=" + why + " to=" + Drive.Grid(g)
                + " walkable=1 tileKind=" + Drive.TileKindAt(g)
                + " from=" + Drive.Grid(Drive.PlayerGrid()));
            SnapCam();
            return true;
        }

        private int NearestAliveMonster()
        {
            return NearestAliveMonsterWhere(null);
        }

        /// <summary>Nearest alive monster whose id is not in <paramref name="skipIds"/> (-1 if none).
        /// Passing null is identical to <see cref="NearestAliveMonster"/>.</summary>
        private int NearestAliveMonsterWhere(HashSet<int> skipIds)
        {
            var mon = Drive.Monster();
            var p = Drive.Player();
            if (mon == null || p == null) return -1;
            var best = -1;
            var bestD = float.MaxValue;
            foreach (var s in mon.All)
            {
                if (!s.alive) continue;
                if (skipIds != null && skipIds.Contains(s.id)) continue;
                var d = Iso.GridDistanceEuclidean(p.Grid, new Vector2Int(s.gridX, s.gridY));
                if (d < bestD) { bestD = d; best = s.id; }
            }
            return best;
        }

        private bool HopNearMonster(int monsterId, float maxDist)
        {
            var mon = Drive.Monster();
            var st = mon != null ? mon.Get(monsterId) : null;
            if (st == null) return false;
            var mg = new Vector2Int(st.gridX, st.gridY);
            var best = new Vector2Int(int.MinValue, int.MinValue);
            var bestD = float.MaxValue;
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(mg.x + dx, mg.y + dy);
                    if (!Drive.Walkable(g)) continue;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > maxDist) continue;
                    if (d < bestD) { bestD = d; best = g; }
                }
            }
            if (best.x == int.MinValue) return false;
            return Hop(best, "near-monster-" + monsterId);
        }

        private int FirstRowIndex()
        {
            var sel = Drive.CharSelectPanel();
            if (sel == null) return -1;
            foreach (var t in sel.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || !t.name.StartsWith("Row", StringComparison.Ordinal)) continue;
                int idx;
                if (int.TryParse(t.name.Substring(3), out idx)) return idx;
            }
            return -1;
        }

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
    }
}
namespace X
{
    /// <summary>Part C: dump / value-string helpers (the on-grid numbers).</summary>
    public partial class Driver
    {
        private static string FillOf(Image img)
        {
            if (img == null) return "(no-image)";
            return "fillAmount=" + img.fillAmount.ToString("0.000") + " type=" + img.type
                + " sprite=" + (img.sprite != null ? img.sprite.name : "none")
                + " col=" + img.color.r.ToString("0.00") + "/" + img.color.g.ToString("0.00") + "/" + img.color.b.ToString("0.00");
        }

        private static string NodeRectOf(GameObject go)
        {
            if (go == null) return "-";
            var img = go.GetComponent<Image>();
            if (img != null) return Drive.RectOf(img);
            var ri = go.GetComponent<RawImage>();
            if (ri != null) return Drive.RectOf(ri);
            return Drive.RectOfComponent(go.transform as RectTransform);
        }

        private static string TextUnder(GameObject go)
        {
            if (go == null) return "-";
            var t = go.GetComponentInChildren<Text>(true);
            return t != null ? t.text : "-";
        }

        private void DumpHud(string tag)
        {
            var hud = Drive.Hud();
            if (hud == null) { Log("HUD", "tag=" + tag + " open=0"); return; }
            var life = Drive.Field(hud, "_lifeFill") as Image;
            var mana = Drive.Field(hud, "_manaFill") as Image;
            var exp = Drive.Field(hud, "_expFill") as Image;
            var lifeT = Drive.Field(hud, "_lifeText");
            var manaT = Drive.Field(hud, "_manaText");
            Log("HUD", "tag=" + tag + " open=1"
                + " lifeFill=" + FillOf(life) + " manaFill=" + FillOf(mana) + " expFill=" + FillOf(exp)
                + " lifeText=\"" + Drive.Esc(Drive.Fmt(Drive.Prop(lifeT, "text"))) + "\""
                + " manaText=\"" + Drive.Esc(Drive.Fmt(Drive.Prop(manaT, "text"))) + "\""
                + " lifeRect=" + Drive.RectOf(life) + " expRect=" + Drive.RectOf(exp));
            Drive.DumpTree("hud", hud.transform, 34, 2);
        }

        private string HudValues()
        {
            var hud = Drive.Hud();
            if (hud == null) return "hudOpen=0";
            var life = Drive.Field(hud, "_lifeFill") as Image;
            var mana = Drive.Field(hud, "_manaFill") as Image;
            var exp = Drive.Field(hud, "_expFill") as Image;
            return "hudOpen=1 lifeText=\"" + Drive.Esc(Drive.Fmt(Drive.Prop(Drive.Field(hud, "_lifeText"), "text"))) + "\""
                + " manaText=\"" + Drive.Esc(Drive.Fmt(Drive.Prop(Drive.Field(hud, "_manaText"), "text"))) + "\""
                + " lifeRatio=" + (life != null ? life.fillAmount.ToString("0.000") : "-")
                + " manaRatio=" + (mana != null ? mana.fillAmount.ToString("0.000") : "-")
                + " expRatio=" + (exp != null ? exp.fillAmount.ToString("0.000") : "-");
        }

        private void DumpQuestTree(string tag)
        {
            var q = Drive.QuestLog();
            if (q == null) { Log("QUESTTREE", "tag=" + tag + " open=0"); return; }
            var sb = new StringBuilder();
            foreach (var t in q.GetComponentsInChildren<Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                var rt = t.rectTransform;
                sb.Append('|').Append(Drive.PathOf(t.transform)).Append("\"").Append(Drive.Esc(t.text)).Append("\"")
                  .Append(" ").Append(Drive.RectOfComponent(rt));
            }
            Log("QUESTTREE", "tag=" + tag + " open=1 texts=" + sb);
            var bg = Drive.Node(q.transform, "QuestBg");
            if (bg != null) Drive.LogCrop(210, tag, "QuestBg", bg.transform as RectTransform);
        }

        private string QuestTreeValues()
        {
            var q = Drive.QuestLog();
            if (q == null) return "questLogOpen=0";
            var sb = new StringBuilder();
            foreach (var t in q.GetComponentsInChildren<Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                sb.Append('|').Append(Drive.Esc(t.text));
            }
            return "questLogOpen=1 texts=" + sb;
        }

        private void DumpDialog(string tag)
        {
            var d = Drive.Dialog();
            if (d == null) { Log("DIALOG", "tag=" + tag + " open=0"); return; }
            var a = Drive.Field(d, "_dialog");
            var opts = "(?)";
            var text = "(?)";
            var npc = "(?)";
            var canA = "(?)";
            var canT = "(?)";
            if (a != null)
            {
                var list = Drive.Field(a, "options") as IList;
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
                text = Drive.Fmt(Drive.Field(a, "text"));
                npc = Drive.Fmt(Drive.Field(a, "npcName"));
                canA = Drive.Fmt(Drive.Field(a, "canAcceptQuest"));
                canT = Drive.Fmt(Drive.Field(a, "canTurnInQuest"));
            }
            var body = Drive.Prop(Drive.Field(d, "_body"), "text");
            var speaker = Drive.Prop(Drive.Field(d, "_speaker"), "text");
            Log("DIALOG", "tag=" + tag + " open=1 npc=\"" + Drive.Esc(npc) + "\""
                + " canAccept=" + canA + " canTurnIn=" + canT
                + " options=" + opts + " text=\"" + Drive.Esc(text) + "\""
                + " bodyNodeText=\"" + Drive.Esc(Drive.Fmt(body)) + "\""
                + " speakerNodeText=\"" + Drive.Esc(Drive.Fmt(speaker)) + "\""
                + " layer=" + d.Layer);
            var art = Drive.Node(d.transform, "DialogArt");
            if (art != null) Drive.LogCrop(220, tag, "DialogArt", art.transform as RectTransform);
        }

        private string DialogValues()
        {
            var d = Drive.Dialog();
            if (d == null) return "dialogOpen=0";
            var a = Drive.Field(d, "_dialog");
            var opts = "(?)";
            var text = "(?)";
            var canA = "?";
            var canT = "?";
            if (a != null)
            {
                var list = Drive.Field(a, "options") as IList;
                var sb = new StringBuilder();
                if (list != null)
                {
                    sb.Append('[');
                    for (var i = 0; i < list.Count; i++) { if (i > 0) sb.Append(" / "); sb.Append('"').Append(list[i]).Append('"'); }
                    sb.Append(']');
                }
                opts = sb.ToString();
                text = Drive.Fmt(Drive.Field(a, "text"));
                canA = Drive.Fmt(Drive.Field(a, "canAcceptQuest"));
                canT = Drive.Fmt(Drive.Field(a, "canTurnInQuest"));
            }
            var body = Drive.Prop(Drive.Field(d, "_body"), "text");
            return "dialogOpen=1 canAccept=" + canA + " canTurnIn=" + canT
                + " options=" + opts + " bodyNodeText=\"" + Drive.Esc(Drive.Fmt(body)) + "\"";
        }

        private void DumpShop(string tag)
        {
            var s = Drive.Shop();
            if (s == null) { Log("SHOP", "tag=" + tag + " open=0"); return; }
            var title = Drive.Prop(Drive.Field(s, "_title"), "text");
            var hint = Drive.Prop(Drive.Field(s, "_hint"), "text");
            var cells = new StringBuilder();
            var occupied = 0;
            var empty = 0;
            for (var i = 0; i < 100; i++)
            {
                var cell = Drive.Node(s.transform, "Cell" + i);
                if (cell == null) continue;
                var icon = Drive.Node(cell.transform, "Icon");
                var img = icon != null ? icon.GetComponent<Image>() : null;
                var spr = img != null && img.sprite != null ? img.sprite.name : "none";
                if (spr == "none") { empty++; continue; }
                occupied++;
                if (occupied <= 12)
                    cells.Append('|').Append(i).Append('=').Append(spr)
                         .Append(" col=").Append(img.color.r.ToString("0.00")).Append('/')
                         .Append(img.color.g.ToString("0.00")).Append('/').Append(img.color.b.ToString("0.00"))
                         .Append(" ").Append(Drive.RectOf(img));
            }
            var rep = Drive.FindButton("RepairAll", out _);
            var cls = Drive.FindButton("Close", out _);
            Log("SHOP", "tag=" + tag + " open=1 title=\"" + Drive.Esc(Drive.Fmt(title)) + "\" hint=\""
                + Drive.Esc(Drive.Fmt(hint)) + "\""
                + " cellsOccupied=" + occupied + " cellsEmpty=" + empty
                + " RepairAll=" + Drive.ButtonSprite(rep) + " Close=" + Drive.ButtonSprite(cls)
                + " playerGold=" + (Drive.Player() != null ? Drive.Player().Gold : -1) + " " + cells);
            var bg = Drive.Node(s.transform, "Backdrop");
            if (bg != null) Drive.LogCrop(230, tag, "ShopBackdrop", bg.transform as RectTransform);
        }

        private string ShopValues()
        {
            var s = Drive.Shop();
            if (s == null) return "shopOpen=0";
            var title = Drive.Prop(Drive.Field(s, "_title"), "text");
            var hint = Drive.Prop(Drive.Field(s, "_hint"), "text");
            var occupied = 0;
            var empty = 0;
            var first = "-";
            for (var i = 0; i < 100; i++)
            {
                var cell = Drive.Node(s.transform, "Cell" + i);
                if (cell == null) continue;
                var icon = Drive.Node(cell.transform, "Icon");
                var img = icon != null ? icon.GetComponent<Image>() : null;
                var spr = img != null && img.sprite != null ? img.sprite.name : "none";
                if (spr == "none") { empty++; continue; }
                occupied++;
                if (first == "-") first = i + "=" + spr;
            }
            return "shopOpen=1 title=\"" + Drive.Esc(Drive.Fmt(title)) + "\" hint=\"" + Drive.Esc(Drive.Fmt(hint))
                + "\" cellsOccupied=" + occupied + " cellsEmpty=" + empty + " firstCell=" + first
                + " playerGold=" + (Drive.Player() != null ? Drive.Player().Gold : -1);
        }

        private void DumpInventory(string tag)
        {
            var inv = Drive.Inventory();
            if (inv == null) { Log("INV", "tag=" + tag + " open=0"); return; }
            var sb = new StringBuilder();
            var occ = 0;
            var nodes = 0;
            for (var i = 0; i < 40; i++)
            {
                var cell = Drive.Node(inv.transform, "Cell" + i);
                if (cell == null) continue;
                nodes++;
                var icon = Drive.Node(cell.transform, "Icon");
                var img = icon != null ? icon.GetComponent<Image>() : null;
                var spr = img != null && img.sprite != null ? img.sprite.name : "none";
                if (spr == "none") continue;
                occ++;
                if (occ <= 14)
                    sb.Append('|').Append(i).Append('=').Append(spr)
                      .Append(" wh=").Append(img.rectTransform.sizeDelta.x.ToString("0.00")).Append('x')
                      .Append(img.rectTransform.sizeDelta.y.ToString("0.00"))
                      .Append(" ").Append(Drive.RectOf(img));
            }
            var gold = Drive.Prop(Drive.Field(inv, "_goldText"), "text");
            var equip = new StringBuilder();
            var equipN = 0;
            foreach (var s in Drive.Item() != null ? Drive.Item().Equipment : (IReadOnlyList<Diablo2.Def.ItemStack>)new List<Diablo2.Def.ItemStack>())
            {
                if (s == null) continue;
                equipN++;
                equip.Append('|').Append(s.name).Append(" id=").Append(s.itemId)
                     .Append(" q=").Append(s.quality).Append(" dmg=").Append(s.dmgMin).Append('-').Append(s.dmgMax)
                     .Append(" def=").Append(s.defMin).Append('-').Append(s.defMax);
            }
            Log("INV", "tag=" + tag + " open=1 cells=" + nodes + " occupied=" + occ
                + " goldText=\"" + Drive.Esc(Drive.Fmt(gold)) + "\" equipCount=" + equipN + " equip=" + equip
                + " " + sb);
            var bg = Drive.Node(inv.transform, "InventoryBg");
            if (bg != null) Drive.LogCrop(240, tag, "InventoryBg", bg.transform as RectTransform);
        }

        private string InvValues()
        {
            var inv = Drive.Inventory();
            if (inv == null) return "inventoryOpen=0";
            var occ = 0;
            var nodes = 0;
            var sb = new StringBuilder();
            for (var i = 0; i < 40; i++)
            {
                var cell = Drive.Node(inv.transform, "Cell" + i);
                if (cell == null) continue;
                nodes++;
                var icon = Drive.Node(cell.transform, "Icon");
                var img = icon != null ? icon.GetComponent<Image>() : null;
                if (img == null || img.sprite == null) continue;
                occ++;
                if (occ <= 6) sb.Append('|').Append(i).Append('=').Append(img.sprite.name);
            }
            var gold = Drive.Prop(Drive.Field(inv, "_goldText"), "text");
            return "inventoryOpen=1 cells=" + nodes + " occupied=" + occ
                + " goldText=\"" + Drive.Esc(Drive.Fmt(gold)) + "\" items=" + sb;
        }

        private string BeltValues(string tag)
        {
            var hud = Drive.Hud();
            var sb = new StringBuilder();
            var n = 0;
            if (hud != null)
            {
                for (var i = 0; i < 4; i++)
                {
                    var c = Drive.Node(hud.transform, "Belt" + i);
                    if (c == null) continue;
                    var icon = Drive.Node(c.transform, "Icon");
                    var img = icon != null ? icon.GetComponent<Image>() : null;
                    var on = icon != null && icon.activeInHierarchy;
                    if (on) n++;
                    sb.Append("|Belt").Append(i).Append(" active=").Append(on ? 1 : 0)
                      .Append(" sprite=").Append(img != null && img.sprite != null ? img.sprite.name : "none")
                      .Append(" wh=").Append(img != null
                          ? img.rectTransform.sizeDelta.x.ToString("0.0") + "x" + img.rectTransform.sizeDelta.y.ToString("0.0") : "-")
                      .Append(" ").Append(img != null ? Drive.RectOf(img) : "-");
                }
            }
            Log("BELT", "tag=" + tag + " filled=" + n + " " + sb);
            return "beltFilled=" + n + " " + sb;
        }

        private void DumpSkillTree(string tag)
        {
            var st = Drive.SkillTree();
            if (st == null) { Log("SKILLTREE", "tag=" + tag + " open=0"); return; }
            var skill = Drive.Skill();
            var sb = new StringBuilder();
            var nodes = 0;
            var noIcon = 0;
            var noName = 0;
            var bright = 0;
            for (var i = 0; i < 64; i++)
            {
                var node = Drive.Node(st.transform, "Node" + i);
                if (node == null) continue;
                nodes++;
                var icon = Drive.Node(node.transform, "Icon");
                var iimg = icon != null ? icon.GetComponent<Image>() : null;
                var spr = iimg != null && iimg.sprite != null ? iimg.sprite.name : "none";
                if (spr == "none") noIcon++;
                var nameTxt = "-";
                foreach (var t in node.GetComponentsInChildren<Text>(true))
                {
                    if (t != null && !string.IsNullOrEmpty(t.text)) { nameTxt = t.text; break; }
                }
                if (nameTxt == "-") noName++;
                var lum = iimg != null ? (iimg.color.r + iimg.color.g + iimg.color.b) : 0f;
                if (lum > 1.5f) bright++;
                if (nodes <= 30)
                    sb.Append('|').Append(i).Append(" name=\"").Append(Drive.Esc(nameTxt)).Append("\" sprite=").Append(spr)
                      .Append(" lum=").Append(lum.ToString("0.00"))
                      .Append(" ").Append(iimg != null ? Drive.RectOf(iimg) : "-");
            }
            var firstText = "-";
            foreach (var t in st.GetComponentsInChildren<Text>(true))
            {
                if (t != null && !string.IsNullOrEmpty(t.text)) { firstText = t.text; break; }
            }
            Log("SKILLTREE", "tag=" + tag + " open=1 nodes=" + nodes + " noIcon=" + noIcon + " noName=" + noName
                + " brightIcons=" + bright + " firstText=\"" + Drive.Esc(firstText) + "\""
                + " skillPoints=" + (Drive.Player() != null ? Drive.Player().SkillPoints : -1)
                + " class=" + (skill != null ? skill.Class.ToString() : "-")
                + " availableSkills=" + (skill != null ? skill.Available.Count : -1) + " " + sb);
            Drive.DumpTextsUnder("skilltree", st.transform, 10);
            var bg = Drive.Node(st.transform, "TreeBackPageK");
            if (bg != null) Drive.LogCrop(250, tag, "SkillTreeBg", bg.transform as RectTransform);
        }

        private string SkillTreeValues()
        {
            var st = Drive.SkillTree();
            if (st == null) return "skillTreeOpen=0";
            var nodes = 0;
            var noIcon = 0;
            var bright = 0;
            for (var i = 0; i < 64; i++)
            {
                var node = Drive.Node(st.transform, "Node" + i);
                if (node == null) continue;
                nodes++;
                var icon = Drive.Node(node.transform, "Icon");
                var iimg = icon != null ? icon.GetComponent<Image>() : null;
                if (iimg == null || iimg.sprite == null) noIcon++;
                else if ((iimg.color.r + iimg.color.g + iimg.color.b) > 1.5f) bright++;
            }
            var firstText = "-";
            foreach (var t in st.GetComponentsInChildren<Text>(true))
            {
                if (t != null && !string.IsNullOrEmpty(t.text)) { firstText = t.text; break; }
            }
            return "skillTreeOpen=1 nodes=" + nodes + " noIcon=" + noIcon + " brightIcons=" + bright
                + " firstText=\"" + Drive.Esc(firstText) + "\""
                + " skillPoints=" + (Drive.Player() != null ? Drive.Player().SkillPoints : -1);
        }

        private void DumpMiniMap(string tag)
        {
            var mm = Drive.MiniMap();
            if (mm == null) { Log("MINIMAP", "tag=" + tag + " open=0"); return; }
            var sb = new StringBuilder();
            var markers = 0;
            for (var i = 0; i < 16; i++)
            {
                var m = Drive.Node(mm.transform, "Marker" + i);
                if (m == null) continue;
                markers++;
                var img = m.GetComponent<Image>();
                if (markers <= 10)
                    sb.Append("|Marker").Append(i).Append(" sprite=")
                      .Append(img != null && img.sprite != null ? img.sprite.name : "none")
                      .Append(" col=").Append(img != null ? img.color.r.ToString("0.00") + "/"
                          + img.color.g.ToString("0.00") + "/" + img.color.b.ToString("0.00") : "-")
                      .Append(" ").Append(img != null ? Drive.RectOf(img) : "-");
            }
            var box = Drive.Node(mm.transform, "MiniMapBox");
            var map = Drive.Node(mm.transform, "Map");
            var dot = Drive.Node(mm.transform, "PlayerDot");
            var texts = 0;
            foreach (var t in mm.GetComponentsInChildren<Text>(true))
            {
                if (t != null && !string.IsNullOrEmpty(t.text)) texts++;
            }
            Log("MINIMAP", "tag=" + tag + " open=1 markers=" + markers + " textNodesWithText=" + texts
                + " box=" + NodeRectOf(box) + " map=" + NodeRectOf(map) + " playerDot=" + NodeRectOf(dot)
                + " " + sb);
            if (box != null) Drive.LogCrop(260, tag, "MiniMapBox", box.transform as RectTransform);
        }

        private string MiniMapValues()
        {
            var mm = Drive.MiniMap();
            if (mm == null) return "miniMapOpen=0";
            var markers = 0;
            var kinds = new List<string>();
            var side = "";
            for (var i = 0; i < 16; i++)
            {
                var m = Drive.Node(mm.transform, "Marker" + i);
                if (m == null) continue;
                markers++;
                var img = m.GetComponent<Image>();
                if (markers <= 8)
                {
                    kinds.Add(img != null && img.sprite != null ? img.sprite.name : "none");
                    if (side.Length == 0 && img != null)
                        side = img.rectTransform.sizeDelta.x.ToString("0.0") + "x" + img.rectTransform.sizeDelta.y.ToString("0.0");
                }
            }
            var texts = 0;
            foreach (var t in mm.GetComponentsInChildren<Text>(true))
            {
                if (t != null && !string.IsNullOrEmpty(t.text)) texts++;
            }
            var box = Drive.Node(mm.transform, "MiniMapBox");
            var map = Drive.Node(mm.transform, "Map");
            return "miniMapOpen=1 markers=" + markers + " markerSprites=[" + string.Join(",", kinds.ToArray()) + "]"
                + " markerSize=" + side + " textNodesWithText=" + texts
                + " box=" + NodeRectOf(box) + " map=" + NodeRectOf(map);
        }

        private void DumpDeath(string tag)
        {
            var d = Drive.Death();
            if (d == null) { Log("DEATH", "tag=" + tag + " open=0"); return; }
            var sb = new StringBuilder();
            for (var i = 0; i < 4; i++)
            {
                var n = Drive.Node(d.transform, "EndGame" + i);
                if (n == null) continue;
                var img = n.GetComponent<Image>();
                sb.Append("|EndGame").Append(i).Append(" sprite=")
                  .Append(img != null && img.sprite != null ? img.sprite.name : "none")
                  .Append(" native=").Append(img != null && img.sprite != null
                      ? img.sprite.rect.width.ToString("0") + "x" + img.sprite.rect.height.ToString("0") : "-")
                  .Append(" ").Append(img != null ? Drive.RectOf(img) : "-");
            }
            var banner = Drive.Node(d.transform, "Banner");
            var hint = Drive.Node(d.transform, "Hint");
            var shade = Drive.Node(d.transform, "Shade");
            var shImg = shade != null ? shade.GetComponent<Image>() : null;
            var cont = Drive.FindButton("Continue", out _);
            Log("DEATH", "tag=" + tag + " open=1 " + sb
                + " banner=" + NodeRectOf(banner)
                + " bannerText=\"" + Drive.Esc(TextUnder(banner)) + "\""
                + " hintText=\"" + Drive.Esc(TextUnder(hint)) + "\""
                + " continue=" + Drive.ButtonSprite(cont)
                + " shadeAlpha=" + (shImg != null ? shImg.color.a.ToString("0.000") : "-")
                + " playerDead=" + (Drive.Player() != null && Drive.Player().IsDead ? 1 : 0));
            Drive.DumpTextsUnder("death", d.transform, 10);
            if (banner != null) Drive.LogCrop(270, tag, "Banner", banner.transform as RectTransform);
        }

        private string DeathValues()
        {
            var d = Drive.Death();
            if (d == null) return "deathOpen=0";
            var banner = Drive.Node(d.transform, "Banner");
            var hint = Drive.Node(d.transform, "Hint");
            var cont = Drive.FindButton("Continue", out _);
            var e0 = Drive.Node(d.transform, "EndGame0");
            var shade = Drive.Node(d.transform, "Shade");
            var shImg = shade != null ? shade.GetComponent<Image>() : null;
            return "deathOpen=1 bannerSprite=" + (banner != null && banner.GetComponent<Image>() != null
                    && banner.GetComponent<Image>().sprite != null ? banner.GetComponent<Image>().sprite.name : "none")
                + " bannerText=\"" + Drive.Esc(TextUnder(banner)) + "\""
                + " hintText=\"" + Drive.Esc(TextUnder(hint)) + "\""
                + " continueInteractable=" + (cont != null ? (cont.interactable ? 1 : 0) : -1)
                + " endGame0=" + NodeRectOf(e0)
                + " shadeAlpha=" + (shImg != null ? shImg.color.a.ToString("0.000") : "-")
                + " playerDead=" + (Drive.Player() != null && Drive.Player().IsDead ? 1 : 0)
                + " playerLife=" + (Drive.Player() != null ? Drive.Player().Life : -1);
        }

        private void DumpSettings(string tag)
        {
            var s = Drive.Settings();
            if (s == null) { Log("SETTINGS", "tag=" + tag + " open=0"); return; }
            var sb = new StringBuilder();
            var names = new[] { "Value0", "Value1", "Minus0", "Plus0", "BgmToggle", "SfxToggle", "FullscreenToggle", "QualityToggle", "Close" };
            for (var i = 0; i < names.Length; i++)
            {
                var go = Drive.Node(s.transform, names[i]);
                if (go == null) continue;
                var btn = go.GetComponent<Button>();
                sb.Append('|').Append(names[i])
                  .Append(" act=").Append(go.activeInHierarchy ? 1 : 0)
                  .Append(" text=\"").Append(Drive.Esc(TextUnder(go))).Append("\"")
                  .Append(" interactable=").Append(btn != null ? (btn.interactable ? 1 : 0) : -1)
                  .Append(" ").Append(NodeRectOf(go));
            }
            Log("SETTINGS", "tag=" + tag + " open=1 settingBgm=" + ReadSettingF("audio/bgm_volume")
                + " settingSfx=" + ReadSettingF("audio/sfx_volume")
                + " settingQuality=" + ReadSettingI("video/quality")
                + " engineQuality=" + QualitySettings.GetQualityLevel()
                + " audioBgm=" + (Drive.Audio() != null ? Drive.Audio().BgmVolume.ToString("0.00") : "-")
                + " audioSfx=" + (Drive.Audio() != null ? Drive.Audio().SfxVolume.ToString("0.00") : "-")
                + " timeScale=" + Time.timeScale.ToString("0.##") + " " + sb);
            var box = Drive.Node(s.transform, "Box");
            if (box != null) Drive.LogCrop(280, tag, "SettingsBox", box.transform as RectTransform);
        }

        private string SettingsValues()
        {
            var s = Drive.Settings();
            if (s == null) return "optionsOpen=0";
            return "optionsOpen=1 settingBgm=" + ReadSettingF("audio/bgm_volume")
                + " settingSfx=" + ReadSettingF("audio/sfx_volume")
                + " settingQuality=" + ReadSettingI("video/quality")
                + " engineQuality=" + QualitySettings.GetQualityLevel()
                + " value0Text=\"" + Drive.Esc(TextUnder(Drive.Node(s.transform, "Value0"))) + "\""
                + " value1Text=\"" + Drive.Esc(TextUnder(Drive.Node(s.transform, "Value1"))) + "\""
                + " timeScale=" + Time.timeScale.ToString("0.##");
        }

        private static string ReadSettingF(string key)
        {
            try { return Game.Setting != null ? Game.Setting.Get<float>(key, -1f).ToString("0.00") : "(no-setting)"; }
            catch { return "(err)"; }
        }

        private static string ReadSettingI(string key)
        {
            try { return Game.Setting != null ? Game.Setting.Get<int>(key, -999).ToString() : "(no-setting)"; }
            catch { return "(err)"; }
        }

        private void DumpTooltip(string tag, int quality)
        {
            var inv = Drive.Inventory();
            var tip = inv != null ? Drive.Field(inv, "_tooltip") : null;
            if (tip == null) { Log("TOOLTIP", "tag=" + tag + " q=" + quality + " (no tooltip object)"); return; }
            var titleT = Drive.Field(tip, "_title") as Text;
            var bodyT = Drive.Field(tip, "_body") as Text;
            var root = Drive.Field(tip, "_root") as RectTransform;
            var vis = Drive.Prop(tip, "IsVisible");
            Log("TOOLTIP", "tag=" + tag + " q=" + quality
                + " visible=" + (vis is bool && (bool)vis ? 1 : 0)
                + " title=\"" + Drive.Esc(titleT != null ? titleT.text : "-") + "\""
                + " titleColorRGBA=" + ColorOf(titleT)
                + " bodyLines=" + (bodyT != null ? bodyT.text.Split('\n').Length : -1)
                + " bodyText=\"" + Drive.Esc(bodyT != null ? bodyT.text : "-") + "\""
                + " rect=" + (root != null ? Drive.RectOfComponent(root) : "-"));
            if (root != null) Drive.LogCrop(290, tag, "ItemTooltip", root);
        }

        private static string ColorOf(Text t)
        {
            if (t == null) return "-";
            return t.color.r.ToString("0.000") + "/" + t.color.g.ToString("0.000") + "/"
                   + t.color.b.ToString("0.000") + "/" + t.color.a.ToString("0.000");
        }

        private string TooltipValues(int quality)
        {
            var inv = Drive.Inventory();
            var tip = inv != null ? Drive.Field(inv, "_tooltip") : null;
            if (tip == null) return "q=" + quality + " (no tooltip)";
            var titleT = Drive.Field(tip, "_title") as Text;
            var vis = Drive.Prop(tip, "IsVisible");
            return "q=" + quality + " visible=" + (vis is bool && (bool)vis ? 1 : 0)
                + " title=\"" + Drive.Esc(titleT != null ? titleT.text : "-") + "\""
                + " titleColorRGBA=" + ColorOf(titleT);
        }

        private void DumpAreaTitle(string tag)
        {
            var hud = Drive.Hud();
            var lt = hud != null ? Drive.Field(hud, "_levelTitle") : null;
            if (lt == null) { Log("AREATITLE", "tag=" + tag + " (no level title control)"); return; }
            var showing = Drive.Prop(lt, "IsShowing");
            var label = Drive.Field(lt, "_label");
            var text = Drive.Prop(label, "text");
            var group = Drive.Field(lt, "_group") as CanvasGroup;
            var root = Drive.Field(lt, "_root") as RectTransform;
            Log("AREATITLE", "tag=" + tag
                + " showing=" + (showing is bool && (bool)showing ? 1 : 0)
                + " text=\"" + Drive.Esc(Drive.Fmt(text)) + "\""
                + " alpha=" + (group != null ? group.alpha.ToString("0.000") : "-")
                + " elapsed=" + Drive.Fmt(Drive.Field(lt, "_elapsed"))
                + " rect=" + (root != null ? Drive.RectOfComponent(root) : "-"));
            if (root != null) Drive.LogCrop(300, tag, "LevelEntryTitle", root);
        }

        private string AreaTitleValues()
        {
            var hud = Drive.Hud();
            var lt = hud != null ? Drive.Field(hud, "_levelTitle") : null;
            if (lt == null) return "(no level title control)";
            var showing = Drive.Prop(lt, "IsShowing");
            var text = Drive.Prop(Drive.Field(lt, "_label"), "text");
            var group = Drive.Field(lt, "_group") as CanvasGroup;
            var root = Drive.Field(lt, "_root") as RectTransform;
            return "showing=" + (showing is bool && (bool)showing ? 1 : 0)
                + " text=\"" + Drive.Esc(Drive.Fmt(text)) + "\""
                + " alpha=" + (group != null ? group.alpha.ToString("0.000") : "-")
                + " rect=" + (root != null ? Drive.RectOfComponent(root) : "-");
        }

        private void LogWorld(string tag)
        {
            var m = Drive.Map();
            var cam = Camera.main;
            if (m == null) { Log("WORLD", "tag=" + tag + " (no map)"); return; }
            var npcTxt = new StringBuilder();
            var npc = Drive.Npc();
            if (npc != null)
            {
                foreach (var d in npc.All)
                    npcTxt.Append('|').Append(d.name).Append('@').Append('(').Append(d.gridX).Append(',').Append(d.gridY).Append(')');
            }
            Log("WORLD", "tag=" + tag + " area=" + m.Area + " map=" + m.Width + "x" + m.Height
                + " seed=" + m.Seed + " spawn=" + Drive.Grid(m.SpawnPoint)
                + " walkable=" + m.WalkableCount + " blocked=" + m.BlockedCount
                + " exits=" + ExitsText(m)
                + " caveEntrance=" + (m.CaveEntrance.HasValue ? Drive.Grid(m.CaveEntrance.Value) : "(none)")
                + " npcPoints=" + m.NpcPoints.Count + " " + npcTxt
                + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                + " ortho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                + " visibleW=" + (cam != null ? (2f * cam.orthographicSize * cam.aspect).ToString("0.0") : "-")
                + " visibleH=" + (cam != null ? (2f * cam.orthographicSize).ToString("0.0") : "-"));
        }

        private static string ExitsText(Diablo2.Module.IMapModule m)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            for (var i = 0; i < m.Exits.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Drive.Grid(m.Exits[i])); }
            sb.Append(']');
            return sb.ToString();
        }

        private string WorldValues()
        {
            var m = Drive.Map();
            var cam = Camera.main;
            if (m == null) return "(no map)";
            return "area=" + m.Area + " map=" + m.Width + "x" + m.Height + " seed=" + m.Seed
                + " spawn=" + Drive.Grid(m.SpawnPoint) + " walkable=" + m.WalkableCount
                + " blocked=" + m.BlockedCount + " exits=" + ExitsText(m)
                + " cave=" + (m.CaveEntrance.HasValue ? Drive.Grid(m.CaveEntrance.Value) : "(none)")
                + " monstersAlive=" + (Drive.Monster() != null ? Drive.Monster().AliveCount : -1)
                + " ortho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                + " playerScreen=" + Drive.V(PlayerScreenPos())
                + " playerInFrame=" + (PlayerInFrame() ? 1 : 0);
        }

        private bool PlayerInFrame()
        {
            var sp = PlayerScreenPos();
            return sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
        }

        private void LogQuest(string tag)
        {
            var q = Drive.Quest();
            if (q == null) { Log("QUEST", "tag=" + tag + " (no quest module)"); return; }
            var sb = new StringBuilder();
            foreach (var dto in q.Quests)
                sb.Append('|').Append(dto.questId).Append('=').Append(dto.state)
                  .Append(" name=\"").Append(dto.name).Append('"')
                  .Append(" progress=").Append(dto.progress).Append('/').Append(dto.required)
                  .Append(" objective=\"").Append(dto.objective).Append('"')
                  .Append(" rewardClaimed=").Append(dto.rewardClaimed ? 1 : 0);
            Log("QUEST", "tag=" + tag + " denState=" + q.DenOfEvil + " denRemaining=" + q.DenRemaining
                + " canTurnIn=" + (q.CanTurnInDen ? 1 : 0)
                + " denAreaAlive=" + (Drive.Monster() != null ? Drive.Monster().CountInArea(AreaId.DenOfEvil) : -1)
                + " quests=" + sb);
        }

        private string QuestValues(string tag)
        {
            var q = Drive.Quest();
            if (q == null) return "tag=" + tag + " (no quest module)";
            var sb = new StringBuilder();
            foreach (var dto in q.Quests)
                sb.Append('|').Append(dto.questId).Append('=').Append(dto.state)
                  .Append(" progress=").Append(dto.progress).Append('/').Append(dto.required);
            return "tag=" + tag + " denState=" + q.DenOfEvil + " denRemaining=" + q.DenRemaining
                + " canTurnIn=" + (q.CanTurnInDen ? 1 : 0)
                + " denAreaAlive=" + (Drive.Monster() != null ? Drive.Monster().CountInArea(AreaId.DenOfEvil) : -1)
                + " quests=" + sb;
        }

        private void LogMonsters(string tag)
        {
            var mon = Drive.Monster();
            if (mon == null) { Log("MONSTERS", "tag=" + tag + " (no monster module)"); return; }
            var byKind = new Dictionary<string, int>();
            var byKindHp = new Dictionary<string, string>();
            foreach (var s in mon.All)
            {
                if (!s.alive) continue;
                int c;
                byKind.TryGetValue(s.name, out c);
                byKind[s.name] = c + 1;
                if (!byKindHp.ContainsKey(s.name))
                    byKindHp[s.name] = s.hp + "/" + s.maxHp + " dr=" + s.defense + " ar=" + s.attackRating
                        + " dmg=" + s.damageMin + "-" + s.damageMax + " exp=" + s.exp + " champ=" + (s.isChampion ? 1 : 0);
            }
            var sb = new StringBuilder();
            foreach (var kv in byKind)
                sb.Append('|').Append(kv.Key).Append(" x").Append(kv.Value).Append(" (").Append(byKindHp[kv.Key]).Append(')');
            Log("MONSTERS", "tag=" + tag + " alive=" + mon.AliveCount
                + " denArea=" + mon.CountInArea(AreaId.DenOfEvil) + " " + sb);
        }

        private string MonsterValues()
        {
            var mon = Drive.Monster();
            if (mon == null) return "(no monster module)";
            return "alive=" + mon.AliveCount + " denArea=" + mon.CountInArea(AreaId.DenOfEvil)
                + " totalTracked=" + mon.All.Count;
        }

        private void LogProgress(string tag)
        {
            var p = Drive.Player();
            var sk = Drive.Skill();
            var learned = new StringBuilder();
            if (sk != null)
            {
                foreach (var d in sk.Available)
                {
                    var lv = sk.GetLevel(d.id);
                    if (lv > 0) learned.Append('|').Append(d.id).Append(':').Append(d.name).Append(" lv=").Append(lv);
                }
            }
            Log("PROGRESS", "tag=" + tag + " lvl=" + (p != null ? p.Level : -1)
                + " exp=" + (p != null ? p.Exp + "/" + p.ExpNext : "-")
                + " statPts=" + (p != null ? p.StatPoints : -1)
                + " skillPts=" + (p != null ? p.SkillPoints : -1)
                + " gold=" + (p != null ? p.Gold : -1)
                + " kills=" + _kills + " levelUps=" + _levelUps + " picked=" + _picks
                + " learned=" + learned);
        }

        private void LogStats(string tag) { Log("STATS", PLine(tag)); }

        private string StatsValues()
        {
            var p = Drive.Player();
            if (p == null) return "(no player)";
            return "lvl=" + p.Level + " exp=" + p.Exp + "/" + p.ExpNext
                + " life=" + p.Life + "/" + p.MaxLife + " mana=" + p.Mana + "/" + p.MaxMana
                + " str=" + p.Str + " dex=" + p.Dex + " vit=" + p.Vit + " eng=" + p.Eng
                + " def=" + p.Defense + " ar=" + p.AttackRating
                + " statPts=" + p.StatPoints + " skillPts=" + p.SkillPoints + " gold=" + p.Gold
                + " grid=" + Drive.Grid(p.Grid)
                + " kills=" + _kills + " levelUps=" + _levelUps + " damageEvents=" + _dmgDealt
                + " attackEvents=" + _attacks + " picked=" + _picks
                + " questChanges=" + _questChanges + " moveCmds=" + _moveCmds;
        }
    }
}
namespace X
{
    /// <summary>Part D: the station plan, front half (calibration + B* + C* + D*).</summary>
    public partial class Driver
    {
        private void BuildPlan()
        {
            // ---------------------------------------------------------------- calibration
            Add("calib-queue", () =>
            {
                Drive.Log("CALIB-QUEUE");
                Api2.ProbeMouse("300,240");
                return true;
            });
            Add("calib-read", () =>
            {
                if (!Elapsed(0.35f)) return false;
                var got = Drive.MousePosNow();
                if (Mathf.Abs(got.y - 240f) < 3f) Drive.SetFlipY(false);
                else if (Mathf.Abs(got.y - (Screen.height - 240f)) < 3f) Drive.SetFlipY(true);
                else Drive.Warn("CALIB ambiguous: injected y=240 read back " + got.y);
                Drive.KV("CALIB", "injected=(300,240) readBack=" + Drive.V(new Vector2(got.x, got.y))
                    + " screen=" + Screen.width + "x" + Screen.height + " flipY=" + (Drive.FlipY ? 1 : 0));
                return true;
            });
            Add("pacing", () => _pacingLogged);
            Add("device", () =>
            {
                Log("RUNTIME", "frame=" + Time.frameCount + " fsm=" + Fsm() + " scene=" + SceneName()
                    + " hudOpen=" + (HudOpen() ? 1 : 0) + " bootOpen=" + (BootOpen() ? 1 : 0)
                    + " gfx=\"" + SafeDevice() + "\" " + State());
                return true;
            });

            // ================================================================ B1 boot
            Add("B1-wait-boot", () =>
            {
                if (!BootOpen()) { if (Tout("B1-wait-boot", 30f)) return true; return false; }
                if (_elapsedBoot < 0f) _elapsedBoot = Time.unscaledTime;
                if (!Elapsed(1.2f)) return false;
                var bp = Drive.Boot();
                var root = bp != null ? bp.transform : null;
                var logo = Drive.Node(root, "Logo");
                var byline = Drive.Node(root, "ByLine");
                var hint = Drive.Node(root, "Hint");
                var img = logo != null ? logo.GetComponent<Image>() : null;
                Drive.DumpTree("boot", root, 22, 3);
                Shoot("B1", "x_b1_boot.png", "screen",
                    "fsm=" + Fsm() + " logo=" + (img != null && img.sprite != null ? img.sprite.name : "none")
                    + " logoRect=" + (img != null ? img.rectTransform.sizeDelta.x.ToString("0.0") + "x"
                        + img.rectTransform.sizeDelta.y.ToString("0.0") : "-")
                    + " logoNative=" + (img != null && img.sprite != null
                        ? img.sprite.rect.width.ToString("0") + "x" + img.sprite.rect.height.ToString("0") : "-")
                    + " logoColor=" + (img != null ? img.color.r.ToString("0.00") + "/" + img.color.g.ToString("0.00")
                        + "/" + img.color.b.ToString("0.00") : "-")
                    + " byline=\"" + Drive.Esc(TextUnder(byline)) + "\""
                    + " hint=\"" + Drive.Esc(TextUnder(hint)) + "\"");
                return true;
            });
            Add("B1-shot", () => ShotLanded(20f));
            Add("B1-space", () =>
            {
                Drive.KV("BOOT-KEY", "injecting a real Keyboard Space press fsm=" + Fsm());
                Drive.KeyDown("space");
                return true;
            });
            Add("B1-space-up", () =>
            {
                if (!Elapsed(0.25f)) return false;
                Drive.KeyUp();
                return true;
            });
            Add("B1-flow", () =>
            {
                if (!MenuOpen()) { if (Tout("B1-flow", 25f)) return true; return false; }
                Log("FLOW", "BootDone -> fsm=" + Fsm() + " scene=" + SceneName()
                    + " mainMenuOpen=" + (MenuOpen() ? 1 : 0));
                return true;
            });

            // ================================================================ B2 main menu
            Add("B2", () =>
            {
                if (!Elapsed(1.2f)) return false;
                var mm = Drive.MainMenu();
                var sb = new StringBuilder();
                var names = new List<string>();
                foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    if (b == null || !b.gameObject.activeInHierarchy) continue;
                    if (mm != null && !b.transform.IsChildOf(mm.transform)) continue;
                    names.Add(b.gameObject.name);
                    sb.Append('|').Append(b.gameObject.name)
                      .Append(" label=\"").Append(Drive.Esc(Drive.ButtonLabel(b))).Append("\"")
                      .Append(" ").Append(Drive.ButtonSprite(b));
                }
                var byline = mm != null ? Drive.Node(mm.transform, "ByLine") : null;
                Drive.DumpTree("mainmenu", mm != null ? mm.transform : null, 26, 3);
                Shoot("B2", "x_b2_mainmenu.png", "screen",
                    "fsm=" + Fsm() + " buttons=" + names.Count
                    + " names=[" + string.Join(",", names.ToArray()) + "]"
                    + " byline=\"" + Drive.Esc(TextUnder(byline)) + "\""
                    + " bylineRect=" + NodeRectOf(byline) + " " + sb);
                return true;
            });
            Add("B2-shot", () => ShotLanded(20f));
            Add("B2-click-single", () =>
            {
                ClickNamedRealMouse("Single", "b2");
                return true;
            });
            Add("B2-wait-select", () =>
            {
                if (!SelectOpen()) { if (Tout("B2-wait-select", 20f)) return true; return false; }
                Log("FLOW", "single player -> fsm=" + Fsm() + " charSelectOpen=1");
                return true;
            });

            // ================================================================ B3 char select
            Add("B3", () =>
            {
                if (!Elapsed(0.9f)) return false;
                var sel = Drive.CharSelectPanel();
                var sb = new StringBuilder();
                var rows = 0;
                if (sel != null)
                {
                    foreach (var t in sel.GetComponentsInChildren<Text>(true))
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        // the row label sits somewhere under a node named Row<i> (walk the ancestors)
                        var cur = t.transform;
                        var inRow = false;
                        while (cur != null)
                        {
                            if (cur.name.StartsWith("Row", StringComparison.Ordinal)) { inRow = true; break; }
                            if (cur == sel.transform) break;
                            cur = cur.parent;
                        }
                        if (inRow)
                        {
                            rows++;
                            sb.Append('|').Append(Drive.PathOf(t.transform)).Append("\"")
                              .Append(Drive.Esc(t.text)).Append("\"").Append(" size=").Append(t.fontSize)
                              .Append(" ").Append(Drive.RectOfComponent(t.rectTransform));
                        }
                    }
                }
                var save = Drive.Save();
                var names = "(n/a)";
                if (save != null) { var l = save.List(); if (l != null) names = string.Join(",", l.ToArray()); }
                Drive.DumpTree("charselect", sel != null ? sel.transform : null, 26, 3);
                Shoot("B3", "x_b3_charselect.png", "screen",
                    "fsm=" + Fsm() + " rowLabels=" + rows + " savedNames=[" + names + "] texts=" + sb);
                return true;
            });
            Add("B3-shot", () => ShotLanded(20f));
            Add("B3-delete-click", () =>
            {
                var idx = FirstRowIndex();
                if (idx < 0) { Drive.Warn("B3 no roster row for the delete-confirm probe"); return true; }
                ClickNamedRealMouse("Row" + idx + "|Delete", "b3del");
                return true;
            });
            Add("B3-delete-shot", () =>
            {
                if (!Elapsed(0.7f)) return false;
                var confirm = Drive.FindButton("Confirm", out _);
                var cancel = Drive.FindButton("Cancel", out _);
                Shoot("B3-confirm", "x_b3_delete_confirm.png", "screen",
                    "confirmBtn=" + (confirm != null ? confirm.gameObject.name + "@" + Drive.PathOf(confirm.transform) : "(none)")
                    + " cancelBtn=" + (cancel != null ? cancel.gameObject.name + "@" + Drive.PathOf(cancel.transform) : "(none)")
                    + " note=the second-confirm dialog is on screen; the CANCEL branch is taken next so no save is deleted");
                return true;
            });
            Add("B3-delete-shot2", () => ShotLanded(20f));
            Add("B3-delete-cancel", () =>
            {
                ClickNamedRealMouse("Cancel", "b3cancel");
                return true;
            });
            Add("B3-after-cancel", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var save = Drive.Save();
                var n = save != null ? save.List().Count : -1;
                Log("B3-CANCEL", "savesStillPresent=" + n + " charSelectOpen=" + (SelectOpen() ? 1 : 0)
                    + " note=the delete confirm was dismissed with CANCEL, nothing was removed");
                return true;
            });
            Add("B3-click-create", () =>
            {
                ClickNamedRealMouse("Create", "b3create");
                return true;
            });
            Add("B3-wait-create", () =>
            {
                if (!CreateOpen()) { if (Tout("B3-wait-create", 20f)) return true; return false; }
                Log("FLOW", "NEW HERO -> fsm=" + Fsm() + " charCreateOpen=1");
                return true;
            });

            // ================================================================ B4 char create
            Add("B4", () =>
            {
                if (!Elapsed(2.2f)) return false;
                var cc = Drive.CharCreate();
                var sb = new StringBuilder();
                var spots = new List<string>();
                var spotNames = new[] { "SpotAmazon", "SpotSorceress", "SpotNecromancer", "SpotPaladin", "SpotBarbarian" };
                for (var i = 0; i < spotNames.Length; i++)
                {
                    var go = cc != null ? Drive.Node(cc.transform, spotNames[i]) : null;
                    var vis = go != null && go.activeInHierarchy;
                    if (vis) spots.Add(spotNames[i]);
                    sb.Append('|').Append(spotNames[i]).Append(" active=").Append(vis ? 1 : 0)
                      .Append(" ").Append(NodeRectOf(go));
                }
                var arr = Drive.Field(cc, "_portrait") as Image[];
                if (arr != null)
                {
                    for (var i = 0; i < arr.Length && i < 6; i++)
                    {
                        var img = arr[i];
                        sb.Append("|Portrait").Append(i).Append(" sprite=")
                          .Append(img != null && img.sprite != null ? img.sprite.name : "none")
                          .Append(" native=").Append(img != null && img.sprite != null
                              ? img.sprite.rect.width.ToString("0") + "x" + img.sprite.rect.height.ToString("0") : "-")
                          .Append(" ").Append(img != null ? Drive.RectOf(img) : "-");
                    }
                }
                var input = Drive.Field(cc, "_nameInput") as InputField;
                Drive.DumpTree("charcreate", cc != null ? cc.transform : null, 24, 2);
                Shoot("B4", "x_b4_charcreate.png", "screen",
                    "fsm=" + Fsm() + " activeSpots=[" + string.Join(",", spots.ToArray()) + "]"
                    + " classIndex=" + Drive.Fmt(Drive.Field(cc, "_classIndex"))
                    + " nameVisible=\"" + Drive.Esc(input != null ? input.text : "-") + "\""
                    + " nameBuffer=\"" + Drive.Esc(Drive.Fmt(Drive.Field(cc, "_nameBuffer"))) + "\""
                    + " " + sb);
                return true;
            });
            Add("B4-shot", () => ShotLanded(20f));
            Add("B4-select-amazon", () =>
            {
                Drive.KV("B4-SELECT", "real mouse on SpotAmazon (Amazon only + Barbarian per the 2026-09-19 decision)");
                ClickNamedRealMouse("SpotAmazon", "b4");
                return true;
            });
            Add("B4-wait-trans", () =>
            {
                if (!Elapsed(3.0f)) return false;
                var cc = Drive.CharCreate();
                Log("B4-TRANS", "classIndex=" + Drive.Fmt(Drive.Field(cc, "_classIndex"))
                    + " transSlot=" + Drive.Fmt(Drive.Field(cc, "_transitionSlot"))
                    + " transFrame=" + Drive.Fmt(Drive.Field(cc, "_transitionFrame"))
                    + " transCount=" + Drive.Fmt(Drive.Field(cc, "_transitionFrameCount")));
                return true;
            });
            Add("B4-type", () =>
            {
                if (!_typePicked)
                {
                    _typePicked = true;
                    var save = Drive.Save();
                    var stamp = DateTime.Now.Hour * 10000 + DateTime.Now.Minute * 100 + DateTime.Now.Second;
                    for (var i = 0; i < 16; i++)
                    {
                        var cand = "X" + ((stamp + i * 7) % 1000000).ToString("000000");
                        var exists = save != null && save.Exists(cand);
                        if (!exists) { _typeStr = cand; break; }
                    }
                    if (string.IsNullOrEmpty(_typeStr)) _typeStr = "X" + (stamp % 1000000).ToString("000000");
                    Drive.KV("TYPE-STR", "typed=\"" + _typeStr + "\" length=" + _typeStr.Length
                        + " saveModuleAvailable=" + (save != null ? 1 : 0));
                }
                if (_typeIdx < _typeStr.Length)
                {
                    var c = _typeStr[_typeIdx];
                    var ok = Drive.TextChar(c);
                    Drive.KV("TYPE", "char[" + _typeIdx + "]='" + c + "' queued=" + (ok ? 1 : 0));
                    _typeIdx++;
                    return false;
                }
                return true;
            });
            Add("B4-name-read", () =>
            {
                if (!Elapsed(0.5f)) return false;
                var cc = Drive.CharCreate();
                var input = Drive.Field(cc, "_nameInput") as InputField;
                var shown = input != null ? input.text : "(no-input)";
                Log("NAME", "typed=\"" + _typeStr + "\" buffer=\"" + Drive.Esc(Drive.Fmt(Drive.Field(cc, "_nameBuffer")))
                    + "\" visible=\"" + Drive.Esc(shown) + "\" caret=" + Drive.Fmt(Drive.Field(cc, "_nameCaret")));
                // the visible field carries a caret glyph; the roster row label does not
                _newHeroName = shown != null ? shown.Replace("|", string.Empty).Trim() : string.Empty;
                Drive.KV("NAME-KEY", "rosterKey=\"" + _newHeroName + "\" (the caret stripped off the visible field)");
                return true;
            });
            Add("B4-confirm", () =>
            {
                Drive.KV("B4-CONFIRM", "clicking Confirm (real mouse) hero=\"" + _newHeroName + "\"");
                ClickNamedRealMouse("Confirm", "b4ok");
                _enterClicked = false;
                return true;
            });
            Add("B4-wait-roster", () =>
            {
                if (SelectOpen() || HudOpen())
                {
                    Log("FLOW", "char created -> fsm=" + Fsm() + " selectOpen=" + (SelectOpen() ? 1 : 0)
                        + " hudOpen=" + (HudOpen() ? 1 : 0));
                    return true;
                }
                // the real-mouse Confirm click does not always land (measured on the previous
                // full-tour run: CharCreate stayed open) -> retry once through the legacy path
                if (CreateOpen() && Elapsed(2.5f) && !_enterClicked)
                {
                    _enterClicked = true;
                    Drive.Warn("CONFIRM-RETRY the real-mouse click did not close CharCreate -> legacy ExecuteEvents path");
                    Drive.Click("Confirm");
                }
                if (Tout("B4-wait-roster", 20f)) return true;
                return false;
            });

            // ================================================================ B5 loading
            Add("B5-enter", () =>
            {
                if (!SelectOpen()) return true;
                var idx = FindRowIndexOfNewHero(_newHeroName);
                if (idx < 0) idx = FindRowIndexOf(_newHeroName);
                if (idx < 0) idx = FindRowIndexOf(_typeStr);
                // Creating a hero does NOT drop you into the game: AppFlow reports
                // "create ok ... -> back to the character roster" and then waits for the roster
                // ENTER of that row.
                // Measured on the previous full-tour run: the real-mouse click on "RowN|Enter"
                // did not land (the tour never left the roster), so this uses the legacy
                // ExecuteEvents path (the same one r1 used) and falls back to the first row
                // instead of aborting.
                var row = idx >= 0 ? "Row" + idx : "Row0";
                Drive.KV("B5-ENTER", "hero=\"" + _newHeroName + "\" typed=\"" + _typeStr
                    + "\" rowIndex=" + idx + " click=\"" + row + "|Enter\""
                    + (idx < 0 ? " (fallback: first row)" : ""));
                if (idx < 0) Note("b5:no-matching-row");
                LogRoster("b5");
                Drive.Click(row + "|Enter");
                return true;
            });
            Add("B5-enter-again", () =>
            {
                if (!SelectOpen()) return true;
                if (!Elapsed(5f)) return false;
                var idx = FindRowIndexOfNewHero(_newHeroName);
                if (idx < 0) idx = FindRowIndexOf(_newHeroName);
                var row = idx >= 0 ? "Row" + idx : "Row0";
                Drive.Warn("B5-ENTER the roster is still open 5s after the row ENTER -> retry click \""
                    + row + "|Enter\" through the legacy path");
                Drive.Click(row + "|Enter");
                return true;
            });
            Add("B5-loading-a", () =>
            {
                if (!LoadingIsOpen()) { if (Tout("B5-loading-a", 20f)) return true; return false; }
                var lp = Drive.Loading();
                var art = Drive.Field(lp, "_art") as Image;
                // the 10 loading frames load asynchronously: shooting before the first one lands
                // would show a bare black screen (measured: shownFrame=-1 artSprite=none)
                var shown0 = Drive.Field(lp, "_shownFrame");
                var haveFrame = shown0 is int && (int)shown0 >= 0;
                if (!haveFrame && !Elapsed(0.6f)) return false;
                Shoot("B5", "x_b5_loading_a.png", "screen",
                    "loadingOpen=1 shownFrame=" + Drive.Fmt(Drive.Field(lp, "_shownFrame"))
                    + " wantedFrame=" + Drive.Fmt(Drive.Field(lp, "_wantedFrame"))
                    + " artSprite=" + (art != null && art.sprite != null ? art.sprite.name : "none")
                    + " artRect=" + (art != null ? Drive.RectOf(art) : "-")
                    + " framesLen=" + Drive.Fmt(Drive.Field(lp, "_framesLeft")) + " " + State());
                return true;
            });
            Add("B5-loading-a2", () => ShotLanded(15f));
            Add("B5-loading-b", () =>
            {
                if (!LoadingIsOpen()) return true;
                if (!Elapsed(0.5f)) return false;
                var lp = Drive.Loading();
                var art = Drive.Field(lp, "_art") as Image;
                Shoot("B5b", "x_b5_loading_b.png", "screen",
                    "loadingOpen=1 shownFrame=" + Drive.Fmt(Drive.Field(lp, "_shownFrame"))
                    + " wantedFrame=" + Drive.Fmt(Drive.Field(lp, "_wantedFrame"))
                    + " artSprite=" + (art != null && art.sprite != null ? art.sprite.name : "none")
                    + " artRect=" + (art != null ? Drive.RectOf(art) : "-")
                    + " loadingStillOpen=" + (LoadingIsOpen() ? 1 : 0) + " " + State());
                return true;
            });
            Add("B5-loading-b2", () => ShotLanded(15f));
            Add("B5-wait-stage", () =>
            {
                if (HudOpen() && Fsm() == "Stage")
                {
                    Log("FLOW", "stage ready fsm=" + Fsm()
                        + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-") + " " + State());
                    return true;
                }
                if (Tout("B5-wait-stage", 40f))
                {
                    Drive.KV("B5-STAGE-WEAK", "hudOpen=" + (HudOpen() ? 1 : 0)
                        + " selectOpen=" + (SelectOpen() ? 1 : 0) + " createOpen=" + (CreateOpen() ? 1 : 0)
                        + " loadingOpen=" + (LoadingIsOpen() ? 1 : 0)
                        + " fsm=" + Fsm() + " scene=" + SceneName() + " " + State());
                    // every in-world station needs a live map: without the HUD the whole tour would
                    // only sit on the roster and burn ~12 minutes of timeouts -- fail fast instead.
                    if (!HudOpen())
                    {
                        Drive.Warn("B5-STAGE-FAIL the HUD never opened -> aborting the tour "
                            + "instead of burning the whole budget on timeouts");
                        Finish("no-stage");
                    }
                    return true;
                }
                return false;
            });

            // ================================================================ C5 town area title
            Add("C5-town", () =>
            {
                if (!Elapsed(0.30f)) return false;
                DumpAreaTitle("x_c5_title_town.png");
                Shoot("C5-town", "x_c5_title_town.png", "screen",
                    "area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " " + AreaTitleValues() + " " + HudValues());
                return true;
            });
            Add("C5-town2", () => ShotLanded(15f));
            Add("town-log", () =>
            {
                LogStats("stage-entry");
                LogWorld("town");
                DumpHud("town");
                return true;
            });

            // ================================================================ C1 town framing
            Add("C1-wide", () =>
            {
                if (!Elapsed(0.4f)) return false;
                Drive.KV("WIDE", "requested ortho=30 (probe framing only; the shipped value is 3.75) rigSetOk="
                    + (Drive.RigSetOrtho(30f) ? 1 : 0));
                return true;
            });
            Add("C1-wide2", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var cam = Camera.main;
                Log("WIDE-READY", "camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                    + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                    + " visibleW=" + (cam != null ? (2f * cam.orthographicSize * cam.aspect).ToString("0.0") : "-")
                    + " visibleH=" + (cam != null ? (2f * cam.orthographicSize).ToString("0.0") : "-")
                    + " map=" + (Drive.Map() != null ? Drive.Map().Width + "x" + Drive.Map().Height : "-"));
                Shoot("C1-wide", "x_c1_town_wide.png", "camera", "wide " + WorldValues());
                return true;
            });
            Add("C1-wide3", () => ShotLanded(20f));
            Add("C1-default", () =>
            {
                Drive.RigSetOrtho(3.75f);
                if (!Elapsed(1.0f)) return false;
                var cam = Camera.main;
                Drive.KV("CAM-RESTORED", "rigOrtho=" + Drive.RigOrtho().ToString("0.###")
                    + " camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-") + " expected=3.75");
                Shoot("C1", "x_c1_town_default.png", "camera",
                    "default pxPerWorldUnit=" + (cam != null ? (Screen.height / (2f * cam.orthographicSize)).ToString("0.0") : "-")
                    + " " + WorldValues());
                return true;
            });
            Add("C1-default2", () => ShotLanded(20f));

            // ---- X batch (town): X1 walk trace, X6 HUD, X7 character stats, X9 default cursor
            BuildPlanXTown();

            // ================================================================ C4 minimap
            Add("C4-key", () => { Drive.KeyDown("tab"); return true; });
            Add("C4-keyup", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("C4-check", () =>
            {
                if (!MiniMapIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.8f)) return false;
                DumpMiniMap("x_c4_minimap.png");
                Shoot("C4", "x_c4_minimap.png", "screen", MiniMapValues());
                return true;
            });
            Add("C4-shot", () => ShotLanded(20f));
            Add("C4-close", () => { Drive.KeyDown("tab"); return true; });
            Add("C4-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("C4-closed", () =>
            {
                if (MiniMapIsOpen() && !Elapsed(3f)) return false;
                Log("MINIMAP-CLOSED", "miniMapOpen=" + (MiniMapIsOpen() ? 1 : 0)
                    + " note=Tab toggles the automap panel off again");
                return true;
            });

            // ================================================================ D1 NoWalk cursor
            Add("D1-nowalk-hover", () =>
            {
                var map = Drive.Map();
                var cam = Camera.main;
                if (map == null || cam == null) return true;
                var p = Drive.PlayerGrid();
                var target = new Vector2Int(int.MinValue, int.MinValue);
                for (var r = 2; r < 16 && target.x == int.MinValue; r++)
                {
                    for (var dx = -r; dx <= r && target.x == int.MinValue; dx++)
                    {
                        for (var dy = -r; dy <= r && target.x == int.MinValue; dy++)
                        {
                            if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                            var g = new Vector2Int(p.x + dx, p.y + dy);
                            if (!map.InBounds(g) || map.Walkable(g)) continue;
                            var sp = cam.WorldToScreenPoint(Iso.GridToWorld(g));
                            if (sp.x < 60f || sp.x > Screen.width - 60f) continue;
                            if (sp.y < 60f || sp.y > Screen.height - 60f) continue;
                            target = g;
                        }
                    }
                }
                if (target.x == int.MinValue) { Drive.Warn("D1 no non-walkable cell on screen for the NoWalk probe"); return true; }
                _nowalkCell = target;
                Drive.KV("D1-PICK", "nonWalkableCell=" + Drive.Grid(target)
                    + " tileKind=" + Drive.TileKindAt(target) + " walkable=0"
                    + " playerGrid=" + Drive.Grid(p));
                return true;
            });
            Add("D1-nowalk-read", () =>
            {
                if (_nowalkCell.x == int.MinValue) return true;
                Drive.MouseState(Drive.CellInjectPoint(_nowalkCell), false);
                if (!Elapsed(0.8f)) return false;
                Drive.KV("CURSOR", "probe=NoWalk cell=" + Drive.Grid(_nowalkCell)
                    + " tileKind=" + Drive.TileKindAt(_nowalkCell)
                    + " walkable=" + (Drive.Walkable(_nowalkCell) ? 1 : 0)
                    + " cursorNow=" + _lastCursor + " cursorChanges=" + _cursorChanges
                    + " hover=" + _lastHover);
                Shoot("D1-nowalk", "x_d1_nowalk_cursor.png", "screen",
                    "probe=NoWalk cell=" + Drive.Grid(_nowalkCell)
                    + " tileKind=" + Drive.TileKindAt(_nowalkCell) + " walkable=0"
                    + " cursorKind=" + _lastCursor + " cursorChanges=" + _cursorChanges
                    + " hoverTarget=" + _lastHover);
                return true;
            });
            Add("D1-nowalk2", () => ShotLanded(15f));

            // ================================================================ D1 detour around a wall
            Add("D1-detour-pick", () =>
            {
                Vector2Int f, t;
                if (!PickDetourPair(out f, out t))
                {
                    Drive.Warn("D1 no detour pair found (the straight line never crosses a blocked cell)");
                    Note("d1:no-detour-pair");
                    return true;
                }
                _detourFrom = f;
                _detourTo = t;
                var map = Drive.Map();
                var path = map.FindPath(f, t);
                var man = Mathf.Max(Mathf.Abs(t.x - f.x), Mathf.Abs(t.y - f.y));
                var blocked = 0;
                if (path != null)
                {
                    for (var i = 0; i < path.Count; i++) if (!Drive.Walkable(path[i])) blocked++;
                }
                _detourBlocked = blocked;
                _detourOnBlocked = 0;
                Drive.KV("D1-PICK", "from=" + Drive.Grid(f) + " to=" + Drive.Grid(t)
                    + " pathCells=" + (path != null ? path.Count : -1) + " manhattan=" + man
                    + " detourGain=" + ((path != null ? path.Count - 1 : -1) - man)
                    + " nonWalkableCellsOnPath=" + blocked
                    + " targetKind=" + Drive.TileKindAt(t) + " targetWalkable=" + (Drive.Walkable(t) ? 1 : 0));
                Hop(f, "d1-detour-start");
                return true;
            });
            Add("D1-detour-go", () =>
            {
                if (!Elapsed(0.5f)) return false;
                BeginGroundClick(_detourTo);
                return true;
            });
            Add("D1-detour-click", () => TickGroundClickSeq() != 0);
            Add("D1-detour-shot", () =>
            {
                if (!Elapsed(0.4f)) return false;
                Shoot("D1-detour-start", "x_d1_detour_start.png", "camera",
                    "from=" + Drive.Grid(_detourFrom) + " to=" + Drive.Grid(_detourTo)
                    + " nonWalkableCellsOnPath=" + _detourBlocked
                    + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0));
                return true;
            });
            Add("D1-detour-shot2", () => ShotLanded(20f));
            Add("D1-detour-move-log", () =>
            {
                // sample every frame: the player must never stand on a non-walkable cell on the way
                var g = Drive.PlayerGrid();
                if (!Drive.Walkable(g)) _detourOnBlocked++;
                if (Drive.PlayerMoving() && !Elapsed(18f)) return false;
                var m = Drive.Map();
                Drive.KV("D1-ARRIVED", "want=" + Drive.Grid(_detourTo)
                    + " playerGrid=" + Drive.Grid(g)
                    + " tileKindHere=" + Drive.TileKindAt(g)
                    + " walkableHere=" + (Drive.Walkable(g) ? 1 : 0)
                    + " framesOnNonWalkableCell=" + _detourOnBlocked
                    + " area=" + (m != null ? m.Area.ToString() : "-")
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                return true;
            });
            Add("D1-detour-shot3", () =>
            {
                Shoot("D1-detour-stop", "x_d1_detour_stop.png", "camera",
                    "want=" + Drive.Grid(_detourTo) + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                    + " nonWalkableCellsOnPath=" + _detourBlocked
                    + " framesOnNonWalkableCell=" + _detourOnBlocked
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0));
                return true;
            });
            Add("D1-detour-shot4", () => ShotLanded(20f));

            // ================================================================ D2 camera follows
            Add("D2-a", () =>
            {
                if (!Elapsed(0.3f)) return false;
                Vector2Int f, t;
                if (!PickDetourPair(out f, out t)) { Note("d2:no-pair"); return true; }
                Hop(f, "d2-walk-from");
                SnapCam();
                var cam = Camera.main;
                var p = Drive.Player();
                if (p == null) return true;
                if (!p.IsMoving) p.MoveTo(t);
                // the camera lerps after a teleport: only shoot once the hero is really on screen
                if (!PlayerInFrame() && !Elapsed(4f)) return false;
                Drive.KV("D2-A", "from=" + Drive.Grid(f) + " to=" + Drive.Grid(t)
                    + " camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                    + " camRotZ=" + (cam != null ? cam.transform.eulerAngles.z.ToString("0.0") : "-")
                    + " camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                    + " playerWorld=" + Drive.World(Drive.PlayerWorld())
                    + " playerScreen=" + Drive.V(PlayerScreenPos())
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0));
                Shoot("D2", "x_d2_follow_a.png", "camera",
                    "camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                    + " camRotZ=" + (cam != null ? cam.transform.eulerAngles.z.ToString("0.0") : "-")
                    + " camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                    + " playerScreen=" + Drive.V(PlayerScreenPos())
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0)
                    + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
                return true;
            });
            Add("D2-a2", () => ShotLanded(20f));
            Add("D2-b", () =>
            {
                if (!Elapsed(1.0f)) return false;
                var cam = Camera.main;
                Drive.KV("D2-B", "camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                    + " camRotZ=" + (cam != null ? cam.transform.eulerAngles.z.ToString("0.0") : "-")
                    + " camOrtho=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                    + " playerWorld=" + Drive.World(Drive.PlayerWorld())
                    + " playerScreen=" + Drive.V(PlayerScreenPos())
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0)
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                Shoot("D2b", "x_d2_follow_b.png", "camera",
                    "camPos=" + (cam != null ? Drive.World(cam.transform.position) : "-")
                    + " camRotZ=" + (cam != null ? cam.transform.eulerAngles.z.ToString("0.0") : "-")
                    + " playerScreen=" + Drive.V(PlayerScreenPos())
                    + " playerInFrame=" + (PlayerInFrame() ? 1 : 0)
                    + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                return true;
            });
            Add("D2-b2", () => ShotLanded(20f));
            Add("D2-stop", () =>
            {
                var p = Drive.Player();
                if (p != null && p.IsMoving) p.Stop();
                Log("D2-END", "cameraFollowed=" + (Camera.main != null ? 1 : 0) + " " + StatsValues());
                return true;
            });

            // ================================================================ D3 eight directions
            Add("D3-setup", () =>
            {
                Vector2Int spot;
                if (!PickEightDirSpot(out spot)) { Drive.Warn("D3 no 3x3 open spot with all 8 neighbours walkable"); Note("d3:no-spot"); return true; }
                _d3Spot = spot;
                Hop(spot, "d3-walk-from");
                _d3LegIdx = 0;
                _d3Seen = 0;
                _d3Got.Clear();
                _d3Continuous = 0;
                Drive.KV("D3-SPOT", "spot=" + Drive.Grid(spot)
                    + " dirs=8 legs=8 (each leg is one cell in the next Dir8 order; the next leg is issued"
                    + " while the previous one is still running so the walk stays continuous)");
                return true;
            });
            Add("D3-walk", () =>
            {
                if (_d3Spot.x == int.MinValue) return true;
                if (_d3LegIdx >= 8)
                {
                    Drive.KV("D3-DONE", "legs=8 distinctDirs=" + _d3Got.Count
                        + " samplesForcedMoving=" + _d3Continuous + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
                    return true;
                }
                var p = Drive.Player();
                if (p == null) return true;
                if (_d3Phase == 0)
                {
                    var d = (int)((Dir8)_d3LegIdx);
                    var del = Iso.DirectionDelta((Dir8)_d3LegIdx);
                    var g = Drive.PlayerGrid();
                    var target = new Vector2Int(g.x + del.x, g.y + del.y);
                    if (!Drive.Walkable(target))
                    {
                        Drive.Warn("D3 leg " + _d3LegIdx + " dir=" + (Dir8)_d3LegIdx + " target " + Drive.Grid(target) + " not walkable -> skipped");
                        _d3LegIdx++;
                        return false;
                    }
                    if (p.IsMoving) _d3Continuous++;
                    p.MoveTo(target);
                    _legAt = Time.unscaledTime;
                    Drive.KV("D3-LEG", "leg=" + _d3LegIdx + " dir=" + (Dir8)_d3LegIdx
                        + " from=" + Drive.Grid(g) + " target=" + Drive.Grid(target)
                        + " movingAtIssue=" + (p.IsMoving ? 1 : 0)
                        + " dirDeltaCheck=" + Iso.DirectionTo(g, target));
                    _d3LegTarget = target;
                    _d3Phase = 1;
                    return false;
                }
                if (_d3Phase == 1)
                {
                    var dir = (int)p.Dir;
                    if (!_d3Got.Contains(dir))
                    {
                        _d3Got.Add(dir);
                        _d3Seen = _d3Got.Count;
                        var tile = "x_d3_dir" + dir + "_" + p.Dir + ".png";
                        var sprite = Drive.PlayerSpriteName();
                        Drive.KV("D3-SAMPLE", "n=" + _d3Seen + " leg=" + _d3LegIdx + " dir=" + p.Dir + "(" + dir + ")"
                            + " sprite=" + sprite + " grid=" + Drive.Grid(Drive.PlayerGrid())
                            + " moving=" + (p.IsMoving ? 1 : 0) + " frame=" + Time.frameCount);
                        Shoot("D3-" + p.Dir, tile, "camera",
                            "dir=" + p.Dir + " dirIndex=" + dir + " sprite=" + sprite
                            + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                            + " moving=" + (p.IsMoving ? 1 : 0)
                            + " leg=" + _d3LegIdx + " distinctSoFar=" + _d3Got.Count);
                        _d3Phase = 2;
                        return false;
                    }
                    if (!p.IsMoving || Time.unscaledTime - _legAt >= 2.0f)
                    {
                        _d3LegIdx++;
                        _d3Phase = 0;
                    }
                    return false;
                }
                if (ShotLanded(15f))
                {
                    _d3LegIdx++;
                    _d3Phase = 0;
                }
                return false;
            });
        }
    }
}
namespace X
{
    /// <summary>Part E: extra fields, probe helpers, and the NPC / shop / item / gold stations.</summary>
    public partial class Driver
    {
        // ---- cross-station scratch state -----------------------------------------
        private float _shotAt;
        private float _subAt;
        private float _legAt;
        private Vector2Int _d3Spot = new Vector2Int(int.MinValue, int.MinValue);
        private Vector2Int _d3LegTarget = new Vector2Int(int.MinValue, int.MinValue);
        private int _d3Phase;
        private int _d3Continuous;
        private int _tiPhase;
        private readonly int[] _tiAnchor = new int[5] { -1, -1, -1, -1, -1 };
        private readonly int[] _tiItemId = new int[5] { 11, 43, 47, 51, 57 };
        // what the anchor ACTUALLY points at (the bag slot may hold an equivalent item, see F1-tools-setup)
        private readonly int[] _tiHitId = new int[5] { -1, -1, -1, -1, -1 };
        private readonly string[] _tiHitName = new string[5] { "-", "-", "-", "-", "-" };
        private readonly string[] _tiHitHow = new string[5] { "-", "-", "-", "-", "-" };
        // F1-tooltips readiness gate: the observable "tooltip control is up with a non-empty title"
        // event is logged once per quality (see TooltipTitleReady).
        private bool _tiReadyLogged;
        // ---------------------------------------------------------------------------------------
        // The tour runs on a PERSISTED character whose bag is FULL (measured 40/40).  A probe item
        // can then never be added, and for a quality whose item is not already in the bag the anchor
        // anchors=[5,27,-1,7,23] => no hover, no GRID=F1-q2, no x_f1_tooltip_q2.png).
        // Fix: make room by taking a stack OUT of the bag (the object is HELD, never destroyed) and
        // put it back from F1-tooltips once the last hover has been shot => the save is left as
        // found.  Every eviction / restore is logged (F1-TOOLS-EVICT / F1-TOOLS-RESTORE / F1-TOOLS).
        private readonly List<ItemStack> _tiEvicted = new List<ItemStack>();
        private readonly List<int> _tiPlantedQ = new List<int>();
        private bool _tiRestored;
        private int _groundItemCell = -1;
        private int _goldBefore = -1;
        private int _pickupAnchorBefore = -1;
        private int _moorEntryGuard;
        private int _shopBuyDelta;
        private int _shopSellDelta;
        private int _shopRepairDelta;
        private int _e1DamageBefore;
        private int _e2KillsBefore;
        private int _g4KillsBefore;
        // ids killed at least once during the G4 den clear.  Fallen Shamans revive a corpse with the
        // SAME id, so a "nearest alive" loop can pin itself on one revived zombie for ever (measured
        // died and the whole tail of the tour timed out).  Preferring an id we have never killed
        // makes the loop take out the revivers instead.
        private readonly HashSet<int> _g4Dead = new HashSet<int>();
        private bool _g4ReviveSkipLogged;
        private int _skillIdToLearn = -1;

        // ================================================================ probe helpers =====
        /// <summary>Build a specific item through the project's own ItemFactory (internal => reflection).</summary>
        private Diablo2.Def.ItemStack MakeItem(int itemId, int level, int quality, bool worn)
        {
            try
            {
                var t = Drive.FindType("Diablo2.Module.Item.ItemFactory");
                if (t == null) { Drive.Warn("MakeItem: ItemFactory type not found"); return null; }
                var fac = Activator.CreateInstance(t);
                var m = t.GetMethod("Create", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) { Drive.Warn("MakeItem: no Create method"); return null; }
                var rng = new Rng(itemId * 131 + quality * 17 + level + 3);
                var r = m.Invoke(fac, new object[] { itemId, level, (Diablo2.Def.ItemQuality)quality, rng, worn });
                return r as Diablo2.Def.ItemStack;
            }
            catch (Exception ex)
            {
                Drive.Warn("MakeItem failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private int FindAnchorCellIndex(int itemId, int quality)
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            foreach (var s in inv.Inventory)
            {
                if (s == null || !s.isAnchor || s.item == null) continue;
                if (s.item.itemId != itemId) continue;
                if ((int)s.item.quality != quality) continue;
                return s.index;
            }
            return -1;
        }

        private GameObject CellNodeOfInventory(int anchorIndex)
        {
            var inv = Drive.Inventory();
            if (inv == null || anchorIndex < 0) return null;
            return Drive.Node(inv.transform, "Cell" + anchorIndex);
        }

        /// <summary>First BAG anchor holding an item of the given quality (any id).  Used as the
        /// uniform fallback in F1-tools-setup: the judging value of rows 34/35 is the QUALITY
        /// (the name colour), so a bag item of that quality is a valid hover target even when
        /// the requested id is not a bag anchor at all.</summary>
        private int FindAnyAnchorOfQuality(int quality)
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            foreach (var s in inv.Inventory)
            {
                if (s == null || !s.isAnchor || s.item == null) continue;
                if ((int)s.item.quality != quality) continue;
                return s.index;
            }
            return -1;
        }

        /// <summary>Bag anchor cell holding the given item id, -1 when absent.</summary>
        private int CellOfItemId(int itemId)
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            foreach (var s in inv.Inventory)
            {
                if (s == null || !s.isAnchor || s.item == null) continue;
                if (s.item.itemId == itemId) return s.index;
            }
            return -1;
        }

        /// <summary>First bag anchor cell whose item is NOT one of the five probe ids (the eviction
        /// victims used to make room: player items are only ever HELD and put back).</summary>
        private int CellOfNonProbe()
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            foreach (var s in inv.Inventory)
            {
                if (s == null || !s.isAnchor || s.item == null) continue;
                var probe = false;
                for (var i = 0; i < _tiItemId.Length; i++) if (_tiItemId[i] == s.item.itemId) probe = true;
                if (!probe) return s.index;
            }
            return -1;
        }

        /// <summary>Take the stack at a bag anchor cell out of the bag.  The ItemStack object is KEPT
        /// in _tiEvicted (backup) so RestoreEvicted can put it back; nothing is deleted or dropped.</summary>
        private ItemStack EvictCell(int cellIndex, string why)
        {
            var inv = Drive.Item();
            if (inv == null || cellIndex < 0) return null;
            ItemStack it = null;
            foreach (var s in inv.Inventory)
            {
                if (s != null && s.index == cellIndex && s.item != null) { it = s.item; break; }
            }
            if (it == null) return null;
            if (!inv.RemoveFromInventory(cellIndex))
            {
                Drive.Warn("F1-tools: RemoveFromInventory refused cell=" + cellIndex + " (" + why + ")");
                return null;
            }
            _tiEvicted.Add(it);
            Drive.KV("F1-TOOLS-EVICT", "cell=" + cellIndex + " id=" + it.itemId
                + " name=\"" + Drive.Esc(it.name) + "\" why=" + why + " held=" + _tiEvicted.Count);
            return it;
        }

        /// <summary>Evict every bag stack that OVERLAPS the <paramref name="w"/>x<paramref name="h"/>
        /// rectangle whose top-left cell is <paramref name="anchorIndex"/>.
        /// <para>WHY A RECTANGLE AND NOT ONE CELL: `Inventory.TryPlace` is a row-major first-fit scan
        /// 1x3 probe but the Rare probe (armour, 2x2/2x3) was still refused -- "背包已满" was the old bag
        /// state, so the 08:56 re-run planted the Normal probe instead).  Since the bag is FULL, freeing
        /// exactly this rectangle leaves it as the only free space, and the row-major scan therefore
        /// anchors the probe at its top-left cell.</para></summary>
        private int EvictRect(int anchorIndex, int w, int h, string why)
        {
            var inv = Drive.Item();
            if (inv == null || anchorIndex < 0) return 0;
            var cols = GameConst.InventoryCols;
            var x0 = anchorIndex % cols;
            var y0 = anchorIndex / cols;
            if (w < 1) w = 1;
            if (h < 1) h = 1;
            var n = 0;
            for (var pass = 0; pass < 8; pass++)
            {
                var victim = -1;
                foreach (var s in inv.Inventory)
                {
                    if (s == null || !s.isAnchor || s.item == null) continue;
                    var sw = s.item.gridW > 0 ? s.item.gridW : 1;
                    var sh = s.item.gridH > 0 ? s.item.gridH : 1;
                    var sx = s.index % cols;
                    var sy = s.index / cols;
                    if (sx < x0 + w && sx + sw > x0 && sy < y0 + h && sy + sh > y0) { victim = s.index; break; }
                }
                if (victim < 0) break;
                if (EvictCell(victim, why) == null) break;
                n++;
            }
            Drive.KV("F1-TOOLS-EVICT-RECT", "anchor=" + anchorIndex + " size=" + w + "x" + h
                + " stacks=" + n + " why=" + why);
            return n;
        }

        /// <summary>Put every held stack back into the bag.  The two probe stacks this station planted
        /// are removed first, so the bag ends with exactly the multiset of items it started with.</summary>
        private bool RestoreEvicted()
        {
            if (_tiRestored) return true;
            var inv = Drive.Item();
            if (inv == null) { _tiRestored = true; return true; }
            var dropped = 0;
            foreach (var q in _tiPlantedQ)
            {
                var c = FindAnchorCellIndex(_tiItemId[q], q);
                if (c >= 0 && inv.RemoveFromInventory(c)) dropped++;
                else Drive.Warn("F1-tools: could not remove the planted probe q=" + q + " cell=" + c);
            }
            var back = 0;
            foreach (var it in _tiEvicted)
            {
                if (it == null) continue;
                if (inv.AddToInventory(it)) back++;
                else Drive.Warn("F1-tools: could not put back the evicted id=" + it.itemId
                    + " name=\"" + Drive.Esc(it.name) + "\"");
            }
            Drive.KV("F1-TOOLS-RESTORE", "plantedRemoved=" + dropped + " evictedHeld=" + _tiEvicted.Count
                + " restored=" + back + " bagFull=" + (inv.IsFull ? 1 : 0));
            _tiRestored = true;
            return true;
        }

        /// <summary>Observable readiness event for F1-tooltips: the ItemTooltip control exists,
        /// reports IsVisible, and its title text is non-empty -- i.e. the injected hover really
        /// landed on an item and the tooltip has been built.  This replaces the old flat
        /// wall-clock wait as the gate for shooting.</summary>
        private bool TooltipTitleReady()
        {
            var inv = Drive.Inventory();
            var tip = inv != null ? Drive.Field(inv, "_tooltip") : null;
            if (tip == null) return false;
            var vis = Drive.Prop(tip, "IsVisible");
            if (!(vis is bool) || !(bool)vis) return false;
            var titleT = Drive.Field(tip, "_title") as Text;
            return titleT != null && !string.IsNullOrEmpty(titleT.text);
        }

        private bool HopNearNpc(int npcId, string why)
        {
            var npc = Drive.Npc();
            if (npc == null) return false;
            Diablo2.Def.NpcDef def = null;
            foreach (var d in npc.All) { if (d.id == npcId) { def = d; break; } }
            if (def == null) { Drive.Warn("HopNearNpc: npc " + npcId + " not in the list"); return false; }
            var npcGrid = new Vector2Int(def.gridX, def.gridY);
            var best = new Vector2Int(int.MinValue, int.MinValue);
            var bestD = float.MaxValue;
            for (var dx = -2; dx <= 2; dx++)
            {
                for (var dy = -2; dy <= 2; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(npcGrid.x + dx, npcGrid.y + dy);
                    if (!Drive.Walkable(g)) continue;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 2.3f) continue;
                    if (d < bestD) { bestD = d; best = g; }
                }
            }
            if (best.x == int.MinValue) { Drive.Warn("HopNearNpc: no walkable stand cell within TalkRange of " + Drive.Grid(npcGrid)); return false; }
            Drive.KV("NPC-SPOT", "why=" + why + " npc=" + def.name + " id=" + npcId
                + " npcGrid=" + Drive.Grid(npcGrid) + " standAt=" + Drive.Grid(best)
                + " dist=" + bestD.ToString("0.000") + " talkRange=2.4");
            return Hop(best, why);
        }

        private Vector2Int NpcGrid(int npcId)
        {
            var npc = Drive.Npc();
            if (npc == null) return new Vector2Int(int.MinValue, int.MinValue);
            foreach (var d in npc.All) if (d.id == npcId) return new Vector2Int(d.gridX, d.gridY);
            return new Vector2Int(int.MinValue, int.MinValue);
        }

        private object DialogArgs()
        {
            var d = Drive.Dialog();
            return d != null ? Drive.Field(d, "_dialog") : null;
        }

        private static bool Truthy(object o) { return o is bool && (bool)o; }

        private int QuestOptionIndex()
        {
            var a = DialogArgs();
            if (a == null) return -1;
            var quest = Truthy(Drive.Field(a, "canAcceptQuest")) || Truthy(Drive.Field(a, "canTurnInQuest"));
            return quest ? 1 : -1;
        }

        private int ShopOptionIndex()
        {
            var a = DialogArgs();
            if (a == null) return -1;
            if (!Truthy(Drive.Field(a, "hasShop"))) return -1;
            var quest = Truthy(Drive.Field(a, "canAcceptQuest")) || Truthy(Drive.Field(a, "canTurnInQuest"));
            return quest ? 2 : 1;
        }

        private string OptionLabels()
        {
            var a = DialogArgs();
            if (a == null) return "(no dialog args)";
            var list = Drive.Field(a, "options") as IList;
            var sb = new StringBuilder();
            sb.Append('[');
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++) { if (i > 0) sb.Append(" / "); sb.Append(i).Append(":\"").Append(list[i]).Append('"'); }
            }
            sb.Append(']');
            return sb.ToString();
        }

        private void ClickDialogOption(int index, string tag)
        {
            if (index < 0) { Drive.Warn("ClickDialogOption index=" + index + " tag=" + tag + " -> skipped"); return; }
            Drive.KV("UI-OPTION", "tag=" + tag + " index=" + index + " options=" + OptionLabels());
            ClickNodeRealMouse("NpcDialogPanel", "Option" + index, tag);
        }

        private void GrantGold(int amount)
        {
            var p = Drive.Player();
            if (p == null) return;
            var before = p.Gold;
            var ok = p.AddGold(amount);
            Drive.KV("GRANT-GOLD", "requested=" + amount + " ok=" + (ok ? 1 : 0)
                + " before=" + before + " after=" + p.Gold
                + " via=IPlayerModule.AddGold (a probe grant so the buy/sell/repair settlement is observable)");
        }

        private void DropGoldStack(int amount, Vector2Int cell)
        {
            var item = Drive.Item();
            if (item == null) return;
            var st = new Diablo2.Def.ItemStack
            {
                itemId = 0,
                name = "gold-probe",
                type = ItemType.Misc,
                quality = ItemQuality.Normal,
                count = amount,
                gridW = 1,
                gridH = 1,
                price = 0,
                isGold = true,
            };
            item.DropToGround(st, cell);
            Drive.KV("DROP-GOLD", "amount=" + amount + " cell=" + Drive.Grid(cell)
                + " walkable=" + (Drive.Walkable(cell) ? 1 : 0)
                + " playerGold=" + (Drive.Player() != null ? Drive.Player().Gold : -1));
        }

        private int GroundItemIdNear(Vector2Int cell)
        {
            var item = Drive.Item();
            if (item == null) return -1;
            foreach (var kv in item.GroundItems)
            {
                if (kv.Value == null) continue;
                return kv.Key;
            }
            return -1;
        }

        // ================================================================ stations =========
        private void BuildPlanItems()
        {
            // ---------------------------------------------------------------- G1a dialog: quest NOT started
            Add("G1a-walk", () =>
            {
                if (!Elapsed(0.4f)) return false;
                LogQuest("g1a-before");
                HopNearNpc((int)NpcId.Akara, "g1a-akara");
                return true;
            });
            Add("G1a-click", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var g = NpcGrid((int)NpcId.Akara);
                if (g.x == int.MinValue) { Note("g1a:no-akara"); return true; }
                BeginGroundClick(g);
                return true;
            });
            Add("G1a-click2", () => TickGroundClickSeq() != 0);
            Add("G1a-open", () =>
            {
                if (!DialogIsOpen()) { if (Tout("G1a-open", 8f)) return true; return false; }
                Log("FLOW", "walked onto Akara -> dialog auto-opened (real ground click on her cell)");
                return true;
            });
            Add("G1a-dump", () =>
            {
                if (!Elapsed(0.8f)) return false;
                DumpDialog("x_g1_dialog_notstarted.png");
                Shoot("G1a", "x_g1_dialog_notstarted.png", "screen",
                    "state=NotStarted " + DialogValues() + " " + QuestValues("g1a"));
                return true;
            });
            Add("G1a-shot", () => ShotLanded(20f));

            // ---------------------------------------------------------------- G3a quest log (NOT started)
            Add("G3a-key", () => { Drive.KeyDown("q"); return true; });
            Add("G3a-keyup", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3a-dump", () =>
            {
                if (!QuestLogIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.7f)) return false;
                DumpQuestTree("x_g3_questlog_notstarted.png");
                Shoot("G3a", "x_g3_questlog_notstarted.png", "screen",
                    "state=NotStarted " + QuestTreeValues() + " " + QuestValues("g3a"));
                return true;
            });
            Add("G3a-shot", () => ShotLanded(20f));
            Add("G3a-close", () => { Drive.KeyDown("q"); return true; });
            Add("G3a-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3a-closed", () =>
            {
                if (QuestLogIsOpen() && !Elapsed(3f)) return false;
                Log("PANEL-CLOSED", "questLogOpen=" + (QuestLogIsOpen() ? 1 : 0) + " dialogOpen=" + (DialogIsOpen() ? 1 : 0));
                return true;
            });

            // ---------------------------------------------------------------- G2 shop + repair (Charsi)
            Add("G2-gold", () =>
            {
                GrantGold(40000);
                return true;
            });
            Add("G2-close-dialog", () =>
            {
                if (DialogIsOpen()) { ClickDialogOption(0, "g2-close"); return true; }
                return true;
            });
            Add("G2-close-dialog2", () => { if (!Elapsed(0.7f)) return false; return true; });
            Add("G2-hop", () =>
            {
                HopNearNpc((int)NpcId.Charsi, "g2-charsi");
                return true;
            });
            Add("G2-click", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var g = NpcGrid((int)NpcId.Charsi);
                if (g.x == int.MinValue) { Note("g2:no-charsi"); return true; }
                BeginGroundClick(g);
                return true;
            });
            Add("G2-click2", () => TickGroundClickSeq() != 0);
            Add("G2-open-dialog", () =>
            {
                if (!DialogIsOpen()) { if (Tout("G2-open-dialog", 8f)) return true; return false; }
                if (!Elapsed(0.6f)) return false;
                Drive.KV("G2-DIALOG", "charsi dialog " + OptionLabels() + " hasShop="
                    + Drive.Fmt(Drive.Field(DialogArgs(), "hasShop")));
                ClickDialogOption(ShopOptionIndex(), "g2-trade");
                return true;
            });
            Add("G2-open-shop", () =>
            {
                if (!ShopIsOpen() && !Elapsed(6f)) return false;
                if (!Elapsed(0.8f)) return false;
                DumpShop("x_g2_shop_buy.png");
                Shoot("G2-buy", "x_g2_shop_buy.png", "screen", "page=buy " + ShopValues());
                return true;
            });
            Add("G2-buy-shot", () => ShotLanded(20f));
            Add("G2-buy-click", () =>
            {
                _goldBefore = Drive.Player() != null ? Drive.Player().Gold : -1;
                ClickNodeRealMouse("ShopPanel", "Cell0", "g2-buy");
                return true;
            });
            Add("G2-buy-read", () =>
            {
                if (!Elapsed(1.0f)) return false;
                var g = Drive.Player() != null ? Drive.Player().Gold : -1;
                _shopBuyDelta = _goldBefore - g;
                if (_shopBuyDelta == 0)
                {
                    Drive.Warn("G2-BUY-FALLBACK the real-mouse cell click produced no gold change -> INpcModule.Buy");
                    var npc = Drive.Npc();
                    if (npc != null) npc.Buy((int)NpcId.Charsi, 0, 1);
                    g = Drive.Player() != null ? Drive.Player().Gold : -1;
                    _shopBuyDelta = _goldBefore - g;
                }
                Drive.KV("G2-BUY", "goldBefore=" + _goldBefore + " goldAfter=" + g
                    + " delta=" + _shopBuyDelta
                    + " inventoryOccupied=" + CountInventoryOccupied());
                return true;
            });
            Add("G2-sell-tab", () =>
            {
                ClickNodeRealMouse("ShopPanel", "Tab1", "g2-tab1");
                return true;
            });
            Add("G2-sell-shot", () =>
            {
                if (!Elapsed(0.9f)) return false;
                DumpShop("x_g2_shop_sell.png");
                Shoot("G2-sell", "x_g2_shop_sell.png", "screen", "page=tab1 " + ShopValues());
                return true;
            });
            Add("G2-sell-shot2", () => ShotLanded(20f));
            Add("G2-sell-click", () =>
            {
                _goldBefore = Drive.Player() != null ? Drive.Player().Gold : -1;
                var anchor = FirstInventoryAnchor();
                _pickupAnchorBefore = anchor;
                Drive.KV("G2-SELL-TARGET", "anchorCell=" + anchor + " gold=" + _goldBefore);
                ClickNodeRealMouse("ShopPanel", "Cell0", "g2-sell");
                return true;
            });
            Add("G2-sell-read", () =>
            {
                if (!Elapsed(1.0f)) return false;
                var g = Drive.Player() != null ? Drive.Player().Gold : -1;
                _shopSellDelta = g - _goldBefore;
                if (_shopSellDelta == 0 && _pickupAnchorBefore >= 0)
                {
                    Drive.Warn("G2-SELL-FALLBACK the real-mouse cell click produced no gold change -> INpcModule.Sell");
                    var npc = Drive.Npc();
                    if (npc != null) npc.Sell((int)NpcId.Charsi, _pickupAnchorBefore);
                    g = Drive.Player() != null ? Drive.Player().Gold : -1;
                    _shopSellDelta = g - _goldBefore;
                }
                Drive.KV("G2-SELL", "goldBefore=" + _goldBefore + " goldAfter=" + g
                    + " delta=" + _shopSellDelta + " anchorCell=" + _pickupAnchorBefore);
                return true;
            });
            Add("G2-worn", () =>
            {
                var it = MakeItem(47, 5, 0, true);
                if (it == null) { Note("g2:no-worn-item"); return true; }
                var ok = Drive.Item() != null && Drive.Item().AddToInventory(it);
                Drive.KV("G2-WORN", "item=" + it.name + " durability=" + it.durability + "/" + it.maxDurability
                    + " added=" + (ok ? 1 : 0) + " repairAllCost="
                    + (Drive.Item() != null ? Drive.Item().GetRepairAllCost() : -1));
                return true;
            });
            Add("G2-repair-click", () =>
            {
                _goldBefore = Drive.Player() != null ? Drive.Player().Gold : -1;
                ClickNamedRealMouse("RepairAll", "g2-repair");
                return true;
            });
            Add("G2-repair-read", () =>
            {
                if (!Elapsed(1.0f)) return false;
                var g = Drive.Player() != null ? Drive.Player().Gold : -1;
                _shopRepairDelta = _goldBefore - g;
                if (_shopRepairDelta == 0)
                {
                    Drive.Warn("G2-REPAIR-FALLBACK the real-mouse RepairAll click produced no gold change -> INpcModule.Repair(-1)");
                    var npc = Drive.Npc();
                    if (npc != null) npc.Repair((int)NpcId.Charsi, -1);
                    g = Drive.Player() != null ? Drive.Player().Gold : -1;
                    _shopRepairDelta = _goldBefore - g;
                }
                Drive.KV("G2-REPAIR", "goldBefore=" + _goldBefore + " goldAfter=" + g
                    + " delta=" + _shopRepairDelta);
                DumpShop("x_g2_shop_repair.png");
                Shoot("G2-repair", "x_g2_shop_repair.png", "screen", "page=repair " + ShopValues());
                return true;
            });
            Add("G2-repair-shot", () => ShotLanded(20f));
            Add("G2-summary", () =>
            {
                Drive.KV("G2-SUMMARY", "buyDelta=" + _shopBuyDelta + " sellDelta=" + _shopSellDelta
                    + " repairDelta=" + _shopRepairDelta
                    + " goldNow=" + (Drive.Player() != null ? Drive.Player().Gold : -1));
                ClickNamedRealMouse("Close", "g2-close");
                return true;
            });
            Add("G2-closed", () =>
            {
                if (ShopIsOpen() && !Elapsed(5f)) return false;
                Log("SHOP-CLOSED", "shopOpen=" + (ShopIsOpen() ? 1 : 0) + " dialogOpen=" + (DialogIsOpen() ? 1 : 0));
                return true;
            });
            Add("G2-close-dialog3", () =>
            {
                if (DialogIsOpen()) { ClickDialogOption(0, "g2-close2"); return true; }
                return true;
            });
            Add("G2-settle", () => { if (!Elapsed(0.8f)) return false; return true; });

            // ---------------------------------------------------------------- F1 inventory + tooltips + pickup
            Add("F1-open", () => { Drive.KeyDown("i"); return true; });
            Add("F1-open2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("F1-dump", () =>
            {
                if (!InvIsOpen() && !Elapsed(5f)) return false;
                if (!Elapsed(0.9f)) return false;
                DumpInventory("x_f1_inventory.png");
                Shoot("F1", "x_f1_inventory.png", "screen", InvValues() + " " + StatsValues());
                return true;
            });
            Add("F1-shot", () => ShotLanded(20f));
            Add("F1-tools-setup", () =>
            {
                var added = 0;
                // FULL (40/40) so exactly one cell has to be freed, and AddToInventory fills the lowest
                // free cell -- planting the Rare probe first therefore puts it in the freed cell, which is
                // the only way to reproduce the 09-21 row shape (anchorCell=5 id=47 resolve=exact).
                var order = new[] { 2, 0, 1, 3, 4 };
                for (var oi = 0; oi < order.Length; oi++)
                {
                    var q = order[oi];
                    var it = MakeItem(_tiItemId[q], 6, q, false);
                    if (it == null)
                    {
                        Drive.Warn("F1-tools: MakeItem returned null for q=" + q + " id=" + _tiItemId[q]);
                        _tiAnchor[q] = -1;
                        _tiHitHow[q] = "no-item";
                        continue;
                    }
                    if (Drive.Item() != null && Drive.Item().AddToInventory(it)) added++;
                    // The tour's own save carries a FULL bag, so AddToInventory may refuse -- and the
                    // probe item may already sit in the bag from an earlier run.  Either way the hover
                    // target must be a BAG slot (the only cells CellNodeOfInventory can point at), so the
                    // anchor is resolved the SAME way for all five qualities: exact id+quality first, then
                    // -- id 11, the short sword -- lived in the EQUIP slot, so its exact lookup returned -1
                    // and F1-tooltips skipped it outright: no hover, no shot, no x_f1_tooltip_q0.png.)
                    var anchor = FindAnchorCellIndex(_tiItemId[q], q);
                    var how = "exact";
                    if (anchor < 0)
                    {
                        anchor = FindAnyAnchorOfQuality(q);
                        how = anchor >= 0 ? "quality-fallback" : "none";
                    }
                    // An exact id+quality anchor is the only thing that makes this row judge the REQUESTED
                    // item.  When it is missing (bag full => the probe never made it in), make room ONCE:
                    // evict the cell holding probe id 0 (its own anchor is re-resolved right here) or, if
                    // that is gone, the first non-probe cell; re-add a fresh probe and re-resolve.
                    if (how != "exact" && Drive.Item() != null && !_tiRestored)
                    {
                        var spot = CellOfItemId(_tiItemId[0]);
                        if (spot < 0) spot = CellOfNonProbe();
                        var w = it.gridW > 0 ? it.gridW : 1;
                        var h = it.gridH > 0 ? it.gridH : 1;
                        var freed = EvictRect(spot, w, h, "make-room-q" + q);
                        if (freed > 0)
                        {
                            var it2 = MakeItem(_tiItemId[q], 6, q, false);
                            if (it2 != null && Drive.Item().AddToInventory(it2))
                            {
                                added++;
                                _tiPlantedQ.Add(q);
                            }
                            anchor = FindAnchorCellIndex(_tiItemId[q], q);
                            how = anchor >= 0 ? "exact" : "none";
                            if (anchor < 0)
                            {
                                anchor = FindAnyAnchorOfQuality(q);
                                how = anchor >= 0 ? "quality-fallback" : "none";
                            }
                        }
                    }
                    _tiAnchor[q] = anchor;
                    _tiHitHow[q] = how;
                    var slots = Drive.Item() != null ? Drive.Item().Inventory : null;
                    if (anchor >= 0 && slots != null && anchor < slots.Count
                        && slots[anchor] != null && slots[anchor].item != null)
                    {
                        _tiHitId[q] = slots[anchor].item.itemId;
                        _tiHitName[q] = slots[anchor].item.name;
                    }
                }
                Drive.KV("F1-TOOLS", "added=" + added
                    + " requestedIds=[" + _tiItemId[0] + "," + _tiItemId[1] + "," + _tiItemId[2] + ","
                    + _tiItemId[3] + "," + _tiItemId[4] + "]"
                    + " anchors=[" + _tiAnchor[0] + "," + _tiAnchor[1] + "," + _tiAnchor[2] + ","
                    + _tiAnchor[3] + "," + _tiAnchor[4] + "]"
                    + " hitIds=[" + _tiHitId[0] + "," + _tiHitId[1] + "," + _tiHitId[2] + ","
                    + _tiHitId[3] + "," + _tiHitId[4] + "]"
                    + " hitNames=[" + _tiHitName[0] + "," + _tiHitName[1] + "," + _tiHitName[2] + ","
                    + _tiHitName[3] + "," + _tiHitName[4] + "]"
                    + " resolve=[" + _tiHitHow[0] + "," + _tiHitHow[1] + "," + _tiHitHow[2] + ","
                    + _tiHitHow[3] + "," + _tiHitHow[4] + "]"
                    + " qualitiesApplied=[Normal,Magic,Rare,Set,Unique] via ItemFactory.Create(reflection)");
                // explicit SKIP roll-up: a quality with no bag anchor is dropped by F1-tooltips, so the
                // caller must be able to see that without diffing GRID= lines by hand
                var skipped = "";
                for (var q = 0; q < 5; q++) if (_tiAnchor[q] < 0) skipped += (skipped.Length > 0 ? "," : "") + q;
                Drive.KV("F1-TOOLS-SKIP", "count=" + (skipped.Length == 0 ? 0 : skipped.Split(',').Length)
                    + " qualities=[" + skipped + "] (no bag anchor => F1-tooltips drops the row)");
                _tiIdx = 0;
                _tiPhase = 0;
                _tiReadyLogged = false;
                return true;
            });
            Add("F1-tooltips", () =>
            {
                // last hover shot => put the evicted stacks back BEFORE the tour stops (the runner uses
                // stopAfter=F1-tooltips, so this branch is the only place the restore can still run)
                if (_tiIdx >= 5) return RestoreEvicted();
                if (_tiPhase == 2)
                {
                    if (!ShotLanded(15f)) return false;
                    _tiPhase = 0;
                    _tiIdx++;
                    _tiReadyLogged = false;
                    return false;
                }
                if (_tiPhase == 1)
                {
                    // EVENT-DRIVEN gate, identical for all five qualities: shoot only once the
                    // ItemTooltip control is actually up with a non-empty title (the hover landed).
                    // The event is logged the first time it is seen; the wall-clock number below is
                    // only a last-resort safety bound so the station can never hang (it Warns when it
                    // fires) -- it is NOT the gate.
                    var ready = TooltipTitleReady();
                    if (!ready && Time.unscaledTime - _subAt < 2.0f)
                    {
                        if (!_tiReadyLogged)
                        {
                            Drive.KV("F1-TOOLTIP-WAIT", "q=" + _tiIdx + " anchor=" + _tiAnchor[_tiIdx]
                                + " itemId=" + _tiHitId[_tiIdx]
                                + " reason=ItemTooltip not yet visible with a non-empty title");
                            _tiReadyLogged = true;
                        }
                        return false;
                    }
                    if (!_tiReadyLogged)
                    {
                        if (ready)
                        {
                            Drive.KV("F1-TOOLTIP-READY", "q=" + _tiIdx + " anchor=" + _tiAnchor[_tiIdx]
                                + " itemId=" + _tiHitId[_tiIdx] + " name=\"" + Drive.Esc(_tiHitName[_tiIdx]) + "\""
                                + " resolve=" + _tiHitHow[_tiIdx]
                                + " dt=" + (Time.unscaledTime - _subAt).ToString("0.000"));
                            _tiReadyLogged = true;
                            return false;   // let one more rendered frame pass with the tooltip on screen
                        }
                        Drive.Warn("F1-TOOLTIP-BOUND q=" + _tiIdx + " anchor=" + _tiAnchor[_tiIdx]
                            + " the ready event was not observed within the uniform 2s safety bound -> shooting anyway");
                        _tiReadyLogged = true;
                    }
                    var tile = "x_f1_tooltip_q" + _tiIdx + ".png";
                    DumpTooltip(tile, _tiIdx);
                    Shoot("F1-q" + _tiIdx, tile, "screen",
                        TooltipValues(_tiIdx) + " anchorCell=" + _tiAnchor[_tiIdx]
                        + " itemId=" + _tiHitId[_tiIdx] + " requestedId=" + _tiItemId[_tiIdx]
                        + " resolve=" + _tiHitHow[_tiIdx]);
                    _tiPhase = 2;
                    return false;
                }
                var cell = CellNodeOfInventory(_tiAnchor[_tiIdx]);
                if (cell == null)
                {
                    Drive.Warn("F1 no anchor cell node for quality " + _tiIdx + " anchor=" + _tiAnchor[_tiIdx]
                        + " requestedId=" + _tiItemId[_tiIdx] + " resolve=" + _tiHitHow[_tiIdx]);
                    _tiIdx++;
                    return false;
                }
                Drive.MouseState(Drive.RectInjectPoint(cell.transform as RectTransform), false);
                _subAt = Time.unscaledTime;
                _tiPhase = 1;
                return false;
            });
            Add("F1-pickup-drop", () =>
            {
                var p = Drive.PlayerGrid();
                var map = Drive.Map();
                var cell = new Vector2Int(int.MinValue, int.MinValue);
                for (var k = 0; k < 8 && cell.x == int.MinValue; k++)
                {
                    var del = Iso.DirectionDelta((Dir8)k);
                    var g = new Vector2Int(p.x + del.x, p.y + del.y);
                    if (map != null && map.Walkable(g) && map.TileAt(g) != TileKind.Exit) cell = g;
                }
                if (cell.x == int.MinValue) { Note("f1:no-drop-cell"); return true; }
                _groundItemCell = cell.x * 1000 + cell.y;
                var it = MakeItem(11, 4, 0, false);
                if (it == null) { Note("f1:no-item"); return true; }
                if (Drive.Item() != null) Drive.Item().DropToGround(it, cell);
                _pickupAnchorBefore = CountInventoryOccupied();
                Drive.KV("F1-DROP", "item=" + it.name + " id=" + it.itemId + " cell=" + Drive.Grid(cell)
                    + " walkable=" + (Drive.Walkable(cell) ? 1 : 0)
                    + " inventoryBefore=" + _pickupAnchorBefore);
                return true;
            });
            Add("F1-pickup-click", () =>
            {
                if (_groundItemCell == int.MinValue) return true;
                var g = new Vector2Int(_groundItemCell / 1000, _groundItemCell % 1000);
                Drive.KV("F1-PICK", "real ground click on the ground item cell " + Drive.Grid(g)
                    + " picksBefore=" + _picks);
                BeginGroundClick(g);
                return true;
            });
            Add("F1-pickup-click2", () => TickGroundClickSeq() != 0);
            Add("F1-pickup-read", () =>
            {
                if (_picks == 0 && !Elapsed(6f)) return false;
                if (!Elapsed(1.0f)) return false;
                var after = CountInventoryOccupied();
                if (_picks == 0)
                {
                    Drive.Warn("F1-PICK-FALLBACK the walk-on pickup did not fire -> IItemModule.PickupNearest"
                        + " (measured from the PLAYER grid, which is what the pickup range is about)");
                    if (Drive.Item() != null) Drive.Item().PickupNearest(Drive.PlayerGrid(), 1.6f);
                    after = CountInventoryOccupied();
                }
                Drive.KV("F1-PICKUP", "picksSeen=" + _picks + " inventoryBefore=" + _pickupAnchorBefore
                    + " inventoryAfter=" + after + " groundItemsLeft=" + GroundCount());
                return true;
            });
            Add("F1-final-shot", () =>
            {
                Shoot("F1-after-pickup", "x_f1_inventory_after_pickup.png", "screen",
                    InvValues() + " picksSeen=" + _picks + " groundItemsLeft=" + GroundCount());
                return true;
            });
            Add("F1-final-shot2", () => ShotLanded(20f));

            // ---------------------------------------------------------------- F3 gold pickup + display
            Add("F3-drop", () =>
            {
                var p = Drive.PlayerGrid();
                var map = Drive.Map();
                var cell = new Vector2Int(int.MinValue, int.MinValue);
                for (var k = 7; k >= 0 && cell.x == int.MinValue; k--)
                {
                    var del = Iso.DirectionDelta((Dir8)k);
                    var g = new Vector2Int(p.x + del.x, p.y + del.y);
                    if (map != null && map.Walkable(g) && map.TileAt(g) != TileKind.Exit) cell = g;
                }
                if (cell.x == int.MinValue) { Note("f3:no-cell"); return true; }
                _groundItemCell = cell.x * 1000 + cell.y;
                _goldBefore = Drive.Player() != null ? Drive.Player().Gold : -1;
                DropGoldStack(1234, cell);
                return true;
            });
            Add("F3-click", () =>
            {
                if (_groundItemCell == int.MinValue) return true;
                var g = new Vector2Int(_groundItemCell / 1000, _groundItemCell % 1000);
                BeginGroundClick(g);
                return true;
            });
            Add("F3-click2", () => TickGroundClickSeq() != 0);
            Add("F3-read", () =>
            {
                if (!Elapsed(1.2f)) return false;
                var g = Drive.Player() != null ? Drive.Player().Gold : -1;
                if (g == _goldBefore)
                {
                    Drive.Warn("F3-PICK-FALLBACK no gold change from the walk-on pickup -> IItemModule.PickupNearest");
                    var c = new Vector2Int(_groundItemCell / 1000, _groundItemCell % 1000);
                    if (Drive.Item() != null) Drive.Item().PickupNearest(c, 2.0f);
                    g = Drive.Player() != null ? Drive.Player().Gold : -1;
                }
                Drive.KV("F3-GOLD", "goldBefore=" + _goldBefore + " goldAfter=" + g
                    + " delta=" + (g - _goldBefore) + " requested=1234"
                    + " goldTextInPanel=\"" + Drive.Esc(Drive.Fmt(Drive.Prop(Drive.Field(Drive.Inventory(), "_goldText"), "text"))) + "\"");
                Shoot("F3", "x_f3_gold.png", "screen",
                    "goldBefore=" + _goldBefore + " goldAfter=" + g + " delta=" + (g - _goldBefore)
                    + " " + InvValues());
                return true;
            });
            Add("F3-shot", () => ShotLanded(20f));
            Add("F3-close", () => { Drive.KeyDown("i"); return true; });
            Add("F3-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("F3-closed", () =>
            {
                if (InvIsOpen() && !Elapsed(4f)) return false;
                Log("PANEL-CLOSED", "inventoryOpen=" + (InvIsOpen() ? 1 : 0));
                return true;
            });

            // ---------------------------------------------------------------- G1b accept the quest -> InProgress
            Add("G1b-walk", () =>
            {
                HopNearNpc((int)NpcId.Akara, "g1b-akara");
                return true;
            });
            Add("G1b-click", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var g = NpcGrid((int)NpcId.Akara);
                if (g.x == int.MinValue) { Note("g1b:no-akara"); return true; }
                BeginGroundClick(g);
                return true;
            });
            Add("G1b-click2", () => TickGroundClickSeq() != 0);
            Add("G1b-open", () =>
            {
                if (!DialogIsOpen()) { if (Tout("G1b-open", 8f)) return true; return false; }
                Log("FLOW", "Akara dialog reopened (real ground click) " + OptionLabels());
                return true;
            });
            Add("G1b-accept", () =>
            {
                if (!Elapsed(0.6f)) return false;
                Drive.KV("G1b-CLICK", "clicking the quest option (real mouse) questIndex=" + QuestOptionIndex()
                    + " options=" + OptionLabels() + " " + QuestValues("g1b-before"));
                ClickDialogOption(QuestOptionIndex(), "g1b-accept");
                return true;
            });
            Add("G1b-wait", () =>
            {
                var q = Drive.Quest();
                if (q == null) return true;
                if (q.DenOfEvil == QuestState.NotStarted && !Elapsed(8f)) return false;
                if (!Elapsed(1.0f)) return false;
                return true;
            });
            Add("G1b-dump", () =>
            {
                DumpDialog("x_g1_dialog_inprogress.png");
                Shoot("G1b", "x_g1_dialog_inprogress.png", "screen",
                    "state=InProgress " + DialogValues() + " " + QuestValues("g1b-after"));
                return true;
            });
            Add("G1b-shot", () => ShotLanded(20f));
            Add("G3b-key", () => { Drive.KeyDown("q"); return true; });
            Add("G3b-keyup", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3b-dump", () =>
            {
                if (!QuestLogIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.7f)) return false;
                DumpQuestTree("x_g3_questlog_inprogress.png");
                Shoot("G3b", "x_g3_questlog_inprogress.png", "screen",
                    "state=InProgress " + QuestTreeValues() + " " + QuestValues("g3b"));
                return true;
            });
            Add("G3b-shot", () => ShotLanded(20f));
            Add("G3b-close", () => { Drive.KeyDown("q"); return true; });
            Add("G3b-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3b-closed", () =>
            {
                if (QuestLogIsOpen() && !Elapsed(3f)) return false;
                if (DialogIsOpen()) { ClickDialogOption(0, "g1b-close"); return true; }
                return true;
            });
            Add("G3b-settle", () => { if (!Elapsed(0.8f)) return false; return true; });
        }

        private int CountInventoryOccupied()
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            var n = 0;
            foreach (var s in inv.Inventory) { if (s != null && s.isAnchor && s.item != null) n++; }
            return n;
        }

        private int FirstInventoryAnchor()
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            foreach (var s in inv.Inventory) { if (s != null && s.isAnchor && s.item != null) return s.index; }
            return -1;
        }

        private int GroundCount()
        {
            var inv = Drive.Item();
            if (inv == null) return -1;
            var n = 0;
            foreach (var kv in inv.GroundItems) n++;
            return n;
        }
    }
}
namespace X
{
    /// <summary>Part F: walking out of town, the wilderness combat stations (E1/E2), and the den entry.</summary>
    public partial class Driver
    {
        private bool MonsterAlive(int id)
        {
            var mon = Drive.Monster();
            return mon != null && mon.IsAlive(id);
        }

        /// <summary>Is the area-title control (LevelEntryTitle) currently on screen?</summary>
        private bool AreaTitleShowing()
        {
            var hud = Drive.Hud();
            var lt = hud != null ? Drive.Field(hud, "_levelTitle") : null;
            if (lt == null) return false;
            var v = Drive.Prop(lt, "IsShowing");
            return v is bool && (bool)v;
        }

        /// <summary>
        /// Re-trigger the area title through the control's own entry point.  Needed when the natural
        /// popup (3.75 s, fired at the area change) has already elapsed before the station samples
        /// it; the call is logged so the reader can tell a re-trigger from the natural one.
        /// </summary>
        private void NudgeAreaTitle(int areaId)
        {
            var hud = Drive.Hud();
            var lt = hud != null ? Drive.Field(hud, "_levelTitle") : null;
            if (lt == null) { Drive.Warn("NudgeAreaTitle: no level title control"); return; }
            try
            {
                // ShowForFirstEntry dedupes per area, so the "already shown" set has to be cleared
                // first for the re-trigger to actually happen
                var shown = Drive.Field(lt, "_shown");
                if (shown != null)
                {
                    var clear = shown.GetType().GetMethod("Clear", Type.EmptyTypes);
                    if (clear != null) { clear.Invoke(shown, null); Drive.KV("AREATITLE-NUDGE-CLEAR", "cleared the shown-set"); }
                }
                var m = lt.GetType().GetMethod("ShowForFirstEntry", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m == null) { Drive.Warn("NudgeAreaTitle: ShowForFirstEntry not found"); return; }
                var r = m.Invoke(lt, new object[] { (AreaId)areaId });
                Drive.Warn("AREATITLE-NUDGE the natural popup had already elapsed; re-triggered"
                    + " ShowForFirstEntry(" + (AreaId)areaId + ") -> returned " + r
                    + " (a probe nudge, NOT the natural popup)");
            }
            catch (Exception ex) { Drive.Warn("NudgeAreaTitle failed: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private bool HopNearCell(Vector2Int cell, string why)
        {
            var map = Drive.Map();
            if (map == null) return false;
            for (var r = 1; r <= 4; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                        var g = new Vector2Int(cell.x + dx, cell.y + dy);
                        if (!map.InBounds(g) || !map.Walkable(g)) continue;
                        if (map.TileAt(g) == TileKind.Exit) continue;
                        var path = map.FindPath(g, cell);
                        if (path == null || path.Count < 2) continue;
                        return Hop(g, why);
                    }
                }
            }
            return false;
        }

        private bool HopAtRange(int monsterId, int ring, string why)
        {
            var mon = Drive.Monster();
            var st = mon != null ? mon.Get(monsterId) : null;
            var map = Drive.Map();
            if (st == null || map == null) return false;
            var mg = new Vector2Int(st.gridX, st.gridY);
            var best = new Vector2Int(int.MinValue, int.MinValue);
            for (var dx = -ring; dx <= ring; dx++)
            {
                for (var dy = -ring; dy <= ring; dy++)
                {
                    var d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                    if (d != ring) continue;
                    var g = new Vector2Int(mg.x + dx, mg.y + dy);
                    if (!map.InBounds(g) || !map.Walkable(g)) continue;
                    var path = map.FindPath(g, mg);
                    if (path == null || path.Count < 2) continue;
                    if (best.x == int.MinValue) best = g;
                }
            }
            if (best.x == int.MinValue) return false;
            return Hop(best, why);
        }

        private Vector2Int MoorTownExit()
        {
            var m = Drive.Map();
            if (m == null) return new Vector2Int(int.MinValue, int.MinValue);
            var cave = m.CaveEntrance.HasValue ? m.CaveEntrance.Value : new Vector2Int(int.MinValue, int.MinValue);
            for (var i = 0; i < m.Exits.Count; i++)
            {
                if (m.Exits[i] == cave) continue;
                return m.Exits[i];
            }
            return m.Exits.Count > 0 ? m.Exits[0] : new Vector2Int(int.MinValue, int.MinValue);
        }

        private void BuildPlanWorld()
        {
            // ---------------------------------------------------------------- leave town -> Blood Moor
            Add("Moor-leave", () =>
            {
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.Town) return true;
                if (m.Exits.Count == 0) { Note("moor:no-town-exit"); return true; }
                var exit = m.Exits[0];
                Drive.KV("MOOR-LEAVE", "townExit=" + Drive.Grid(exit) + " kind=" + Drive.TileKindAt(exit)
                    + " exits=" + ExitsText(m) + " note=hop next to it, then WALK onto it (real CheckExit chain)");
                HopNearCell(exit, "town-exit-stand");
                return true;
            });
            Add("Moor-leave2", () =>
            {
                if (!Elapsed(0.5f)) return false;
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.Town || m.Exits.Count == 0) return true;
                var p = Drive.Player();
                if (p == null) return true;
                p.MoveTo(m.Exits[0]);
                return true;
            });
            Add("Moor-wait", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor)
                {
                    Log("FLOW", "entered BloodMoor map=" + m.Width + "x" + m.Height + " seed=" + m.Seed
                        + " grid=" + Drive.Grid(Drive.PlayerGrid()));
                    return true;
                }
                if (Tout("Moor-wait", 45f)) return true;
                return false;
            });
            Add("Moor-away", () =>
            {
                var m = Drive.Map();
                if (m == null) return true;
                var cave = m.CaveEntrance.HasValue ? m.CaveEntrance.Value : new Vector2Int(int.MinValue, int.MinValue);
                var spawn = m.SpawnPoint;
                var p = Drive.PlayerGrid();
                var town = MoorTownExit();
                if (p == town)
                {
                    HopNearCell(spawn, "moor-step-away-from-the-town-exit");
                }
                _moorEntryGuard = 1;
                Log("MOOR-ENTRY", "spawn=" + Drive.Grid(spawn) + " playerGrid=" + Drive.Grid(p)
                    + " townExit=" + Drive.Grid(town) + " caveEntrance=" + Drive.Grid(cave)
                    + " monsterSpawns=" + m.MonsterSpawns.Count);
                return true;
            });
            Add("C5-moor", () =>
            {
                if (!Elapsed(0.30f)) return false;
                if (!AreaTitleShowing())
                {
                    if (!Elapsed(1.4f)) return false;
                    NudgeAreaTitle((int)AreaId.BloodMoor);
                    if (!Elapsed(1.7f)) return false;
                }
                DumpAreaTitle("x_c5_title_moor.png");
                Shoot("C5-moor", "x_c5_title_moor.png", "screen",
                    "area=BloodMoor " + AreaTitleValues());
                return true;
            });
            Add("C5-moor2", () => ShotLanded(15f));
            Add("C2-wide", () =>
            {
                if (!Elapsed(0.4f)) return false;
                Drive.RigSetOrtho(30f);
                return true;
            });
            Add("C2-wide2", () =>
            {
                if (!Elapsed(0.6f)) return false;
                LogWorld("moor");
                LogMonsters("moor");
                Shoot("C2-wide", "x_c2_moor_wide.png", "camera", "wide " + WorldValues());
                return true;
            });
            Add("C2-wide3", () => ShotLanded(20f));
            Add("C2-default", () =>
            {
                Drive.RigSetOrtho(3.75f);
                if (!Elapsed(1.0f)) return false;
                Shoot("C2", "x_c2_moor_default.png", "camera", "default " + WorldValues());
                return true;
            });
            Add("C2-default2", () => ShotLanded(20f));

            // ---------------------------------------------------------------- E1 bare-hand baseline
            Add("E1bare-hop", () =>
            {
                var id = NearestAliveMonster();
                if (id < 0) { Note("e1bare:no-monster"); return true; }
                if (!HopNearMonster(id, 1.5f)) { Note("e1bare:no-stand-cell"); return true; }
                _pendingMonster = id;
                return true;
            });
            Add("E1bare-hold", () =>
            {
                if (_pendingMonster < 0) return true;
                if (!Elapsed(0.4f)) return false;
                BeginHold(_pendingMonster, "e1-bare-hands");
                _e1DamageBefore = _dmgDealt;
                return true;
            });
            Add("E1bare-wait", () =>
            {
                if (_holdMonster < 0) return true;
                var p = Drive.Player();
                if (p != null && p.Gold >= 0 && p.Exp > 0) { /* keep holding */ }
                if (_dmgDealt > _e1DamageBefore || Time.unscaledTime - _subAt > 12f) return true;
                if (!Elapsed(20f)) return false;
                return true;
            });
            Add("E1bare-end", () =>
            {
                EndHold("e1-bare-hands");
                Log("E1-BARE", "damageEvents=" + _dmgDealt + " attacks=" + _attacks
                    + " note=the [Combat] basic-attack line carries weapon 1-2 while no weapon is equipped"
                    + " audio=" + AudioList() + " " + StatsValues());
                return true;
            });

            // ---------------------------------------------------------------- F2 equip + belt
            Add("F2-open", () => { Drive.KeyDown("i"); return true; });
            Add("F2-open2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("F2-prepare", () =>
            {
                if (!InvIsOpen() && !Elapsed(5f)) return false;
                if (!Elapsed(0.8f)) return false;
                var item = Drive.Item();
                if (item == null) { Note("f2:no-item-module"); return true; }
                // X5 needs a real before/after pair.  Measured: itemId 11 (the "short sword" the
                // original probe assumed) never lands in the bag.  The F1 stations run just before
                // this one and already put four EQUIPPABLE items in the bag (hat / leather armour /
                // round shield / leather boots at qualities 1..4), so reuse one of those instead of
                // creating anything: an earlier version of this station that looped over
                // ItemFactory.Create destabilised the Play session.
                var ids = new[] { 43, 47, 51, 57 };
                var quals = new[] { 1, 2, 3, 4 };
                var anchor = -1;
                var usedId = -1;
                var usedQ = -1;
                for (var i = 0; i < ids.Length && anchor < 0; i++)
                {
                    var a = FindAnchorCellIndex(ids[i], quals[i]);
                    if (a >= 0) { anchor = a; usedId = ids[i]; usedQ = quals[i]; }
                }
                for (var i = 0; i < ids.Length && anchor < 0; i++)
                {
                    var a = FindAnchorCellIndex(ids[i], 0);
                    if (a >= 0) { anchor = a; usedId = ids[i]; usedQ = 0; }
                }
                if (anchor < 0)
                {
                    var it = MakeItem(11, 5, 0, false);
                    if (it != null && item.AddToInventory(it))
                    {
                        var a2 = FindAnchorCellIndex(11, 0);
                        if (a2 >= 0) { anchor = a2; usedId = 11; usedQ = 0; Drive.KV("F2-ADD", "created item 11 anchor=" + a2); }
                    }
                }
                _pickupAnchorBefore = anchor;
                Drive.KV("F2-PRE", "anchorCell=" + anchor + " itemId=" + usedId + " quality=" + usedQ
                    + " " + StatsValues());
                return true;
            });
            // X5 (before): bag + equipment slots BEFORE the equip (equipmentCount is the "before" value)
            Add("F2-before-shot", () =>
            {
                if (!Elapsed(0.6f)) return false;
                DumpInventory("x_f2_equip_before.png");
                Shoot("X5-before", "x_f2_equip_before.png", "screen",
                    "phase=before-equip anchorCell=" + _pickupAnchorBefore
                    + " equipmentCount=" + (Drive.Item() != null ? Drive.Item().Equipment.Count : -1)
                    + " " + InvValues() + " " + EquipmentValues() + " " + StatsValues());
                return true;
            });
            Add("F2-before-shot2", () => ShotLanded(20f));
            Add("F2-equip", () =>
            {
                var item = Drive.Item();
                if (item == null || _pickupAnchorBefore < 0) { Note("f2:no-equippable-item"); return true; }
                var before = item.Equipment.Count;
                var ok = item.EquipFromInventory(_pickupAnchorBefore);
                Drive.KV("F2-EQUIP", "anchorCell=" + _pickupAnchorBefore + " ok=" + (ok ? 1 : 0)
                    + " equipmentCountBefore=" + before + " equipmentCount=" + item.Equipment.Count);
                return true;
            });
            Add("F2-shot", () =>
            {
                if (!Elapsed(0.6f)) return false;
                DumpInventory("x_f2_equip.png");
                Shoot("F2", "x_f2_equip.png", "screen",
                    "equipmentCount=" + (Drive.Item() != null ? Drive.Item().Equipment.Count : -1)
                    + " " + EquipmentValues() + " " + StatsValues());
                return true;
            });
            Add("F2-shot2", () => ShotLanded(20f));
            Add("F2-belt", () =>
            {
                var item = Drive.Item();
                if (item == null) { Note("f2:no-item-module"); return true; }
                var n = 0;
                var potIds = new[] { 72, 74 };
                for (var i = 0; i < potIds.Length; i++)
                {
                    var pot = MakeItem(potIds[i], 3, 0, false);
                    if (pot == null) continue;
                    if (item.AddToBelt(pot, -1)) n++;
                }
                Drive.KV("F2-BELT", "potionsAdded=" + n + " " + BeltValues("f2-belt")
                    + " life=" + (Drive.Player() != null ? Drive.Player().Life + "/" + Drive.Player().MaxLife : "-"));
                if (n == 0) Drive.Warn("F2-BELT the potion item ids did not resolve -> belt demo limited");
                return true;
            });
            Add("F2-belt-shot", () =>
            {
                if (!Elapsed(0.6f)) return false;
                Shoot("F2-belt", "x_f2_belt_before.png", "screen",
                    "phase=before " + BeltValues("f2-before") + " " + StatsValues());
                return true;
            });
            Add("F2-belt-shot2", () => ShotLanded(20f));
            Add("F2-hurt", () =>
            {
                var p = Drive.Player();
                if (p == null) return true;
                var before = p.Life;
                p.ApplyDamage(18, DamageType.Physical);
                Drive.KV("F2-HURT", "applied=18 before=" + before + "/" + p.MaxLife
                    + " after=" + p.Life + "/" + p.MaxLife);
                return true;
            });
            Add("F2-drink", () =>
            {
                if (!Elapsed(0.5f)) return false;
                var before = Drive.Player() != null ? Drive.Player().Life : -1;
                Drive.KeyDown("1");
                _counterA = before;
                return true;
            });
            Add("F2-drink2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("F2-drink-read", () =>
            {
                if (!Elapsed(0.9f)) return false;
                var p = Drive.Player();
                Drive.KV("F2-DRINK", "lifeBefore=" + _counterA + " lifeAfter=" + (p != null ? p.Life : -1)
                    + " maxLife=" + (p != null ? p.MaxLife : -1) + " " + BeltValues("f2-after"));
                Shoot("F2-belt-after", "x_f2_belt_after.png", "screen",
                    "phase=after " + BeltValues("f2-after") + " " + StatsValues());
                return true;
            });
            Add("F2-belt-shot3", () => ShotLanded(20f));
            Add("F2-close", () => { Drive.KeyDown("i"); return true; });
            Add("F2-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("F2-closed", () => { if (InvIsOpen() && !Elapsed(4f)) return false; return true; });

            // ---------------------------------------------------------------- E1 hit trio with the sword
            Add("E1-hop", () =>
            {
                var id = NearestAliveMonster();
                if (id < 0) { Note("e1:no-monster"); return true; }
                if (!HopNearMonster(id, 1.5f)) { Note("e1:no-stand-cell"); return true; }
                _pendingMonster = id;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(id) : null;
                Drive.KV("E1-SETUP", "m#" + id + " " + (st != null ? st.name : "-")
                    + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " cell=" + (st != null ? "(" + st.gridX + "," + st.gridY + ")" : "-")
                    + " playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                    + " dist=" + (st != null ? Iso.GridDistanceEuclidean(Drive.PlayerGrid(), new Vector2Int(st.gridX, st.gridY)).ToString("0.000") : "-")
                    + " meleeRange=1.6 " + StatsValues());
                return true;
            });
            Add("E1-hold", () =>
            {
                if (_pendingMonster < 0) return true;
                if (!Elapsed(0.4f)) return false;
                _e1DamageBefore = _dmgDealt;
                _audioClips.Clear();
                BeginHold(_pendingMonster, "e1-hit");
                return true;
            });
            Add("E1-hit-shot", () =>
            {
                if (_holdMonster < 0) return true;
                if (_dmgDealt <= _e1DamageBefore)
                {
                    if (Tout("E1-hit-shot", 15f)) { EndHold("e1-no-damage"); return true; }
                    return false;
                }
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_holdMonster) : null;
                Drive.KV("E1-HIT", "m#" + _holdMonster + " damageEvents=" + _dmgDealt
                    + " attacks=" + _attacks + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " audio=" + AudioList());
                Shoot("E1-a", "x_e1_hit_a.png", "camera",
                    "phase=on-hit m#" + _holdMonster + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " attackEvents=" + _attacks
                    + " audio=" + AudioList() + " " + StatsValues());
                return true;
            });
            Add("E1-hit-shot2", () => ShotLanded(15f));
            // X2: the hit trio -- 3 frames ~0.10s apart around the SAME damage event
            Add("E1-hit-b", () =>
            {
                if (_holdMonster < 0) return true;
                if (!Elapsed(0.10f)) return false;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_holdMonster) : null;
                Drive.KV("E1-HIT-B", "m#" + _holdMonster + " damageEvents=" + _dmgDealt
                    + " attacks=" + _attacks + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " audio=" + AudioList());
                Shoot("E1-b", "x_e1_hit_b.png", "camera",
                    "phase=hit+0.10s m#" + _holdMonster + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " attackEvents=" + _attacks
                    + " audio=" + AudioList() + " " + StatsValues());
                return true;
            });
            Add("E1-hit-shot2b", () => ShotLanded(15f));
            Add("E1-hit-c", () =>
            {
                if (_holdMonster < 0) return true;
                if (!Elapsed(0.10f)) return false;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_holdMonster) : null;
                Drive.KV("E1-HIT-C", "m#" + _holdMonster + " damageEvents=" + _dmgDealt
                    + " attacks=" + _attacks + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " audio=" + AudioList());
                Shoot("E1-c", "x_e1_hit_c.png", "camera",
                    "phase=hit+0.20s m#" + _holdMonster + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " attackEvents=" + _attacks
                    + " audio=" + AudioList() + " " + StatsValues());
                return true;
            });
            Add("E1-hit-shot2c", () => ShotLanded(15f));
            Add("E1-hit-hold", () =>
            {
                if (_holdMonster < 0) return true;
                if (!Elapsed(0.6f)) return false;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_holdMonster) : null;
                Drive.KV("E1-AFTER", "m#" + _holdMonster + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " audio=" + AudioList());
                Shoot("E1-after", "x_e1_hit_after.png", "camera",
                    "phase=after m#" + _holdMonster + " hp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " attackEvents=" + _attacks
                    + " audio=" + AudioList() + " " + StatsValues());
                return true;
            });
            Add("E1-hit-shot3", () => ShotLanded(15f));
            Add("E1-end", () =>
            {
                EndHold("e1-hit");
                Drive.KV("E1-TRIO", "damageEvents=" + _dmgDealt + " attackEvents=" + _attacks
                    + " audioClips=" + AudioList()
                    + " note=the swing sound comes from Module/Combat/DamagePipeline via ctx.Audio.SfxAt;"
                    + " the damage float text is world space (ViewModule.ShowFloatingText)");
                return true;
            });
            // X3 (two consecutive normal attacks) + X9 (hover cursor) both need a live monster
            BuildPlanXAttack();
            BuildPlanXHoverCursor();

            // ---------------------------------------------------------------- E2 kill -> level up
            Add("E2-kill", () =>
            {
                var p = Drive.Player();
                if (p == null) return true;
                if (p.Level >= 2)
                {
                    EndHold("e2-done");
                    Drive.KV("E2-LEVELUP", "level=" + p.Level + " exp=" + p.Exp + "/" + p.ExpNext
                        + " statPts=" + p.StatPoints + " skillPts=" + p.SkillPoints
                        + " kills=" + _kills + " levelUps=" + _levelUps);
                    Shoot("E2", "x_e2_levelup.png", "screen",
                        "level=" + p.Level + " exp=" + p.Exp + "/" + p.ExpNext
                        + " statPts=" + p.StatPoints + " skillPts=" + p.SkillPoints
                        + " kills=" + _kills + " levelUps=" + _levelUps + " " + HudValues());
                    return true;
                }
                if (Elapsed(150f))
                {
                    EndHold("e2-budget");
                    Drive.KV("E2-NOLEVELUP", "level=" + p.Level + " exp=" + p.Exp + "/" + p.ExpNext
                        + " kills=" + _kills + " note=the kill budget ran out before the level threshold");
                    return true;
                }
                if (_holdMonster >= 0)
                {
                    if (!MonsterAlive(_holdMonster))
                    {
                        EndHold("e2-killed");
                        Drive.KV("E2-KILL", "kills=" + _kills + " level=" + p.Level + " exp=" + p.Exp + "/" + p.ExpNext);
                        return false;
                    }
                    if (Time.unscaledTime - _subAt > 30f)
                    {
                        EndHold("e2-timeout");
                        return false;
                    }
                    return false;
                }
                var id = NearestAliveMonster();
                if (id < 0)
                {
                    if (Tout("E2-kill-nomonster", 20f)) return true;
                    return false;
                }
                _e2KillsBefore = _kills;
                if (!HopNearMonster(id, 1.5f))
                {
                    Drive.Warn("E2 no stand cell next to m#" + id);
                    return false;
                }
                _subAt = Time.unscaledTime;
                BeginHold(id, "e2-kill");
                return false;
            });
            Add("E2-shot", () => ShotLanded(20f));
            Add("E2-log", () =>
            {
                LogProgress("after-levelup");
                LogMonsters("moor");
                return true;
            });

            // ---------------------------------------------------------------- enter the den
            Add("Den-enter", () =>
            {
                var m = Drive.Map();
                var cave = m != null && m.CaveEntrance.HasValue ? m.CaveEntrance.Value : new Vector2Int(int.MinValue, int.MinValue);
                if (cave.x == int.MinValue) { Note("den:no-cave-entrance"); return true; }
                Drive.KV("DEN-ENTER", "caveEntrance=" + Drive.Grid(cave) + " kind=" + Drive.TileKindAt(cave)
                    + " note=hop next to it, then WALK onto the cave mouth (real CheckExit chain)");
                HopNearCell(cave, "cave-mouth-stand");
                _pendingMonster = cave.x * 1000 + cave.y;
                return true;
            });
            Add("Den-enter2", () =>
            {
                if (_pendingMonster == int.MinValue) return true;
                if (!Elapsed(0.5f)) return false;
                var p = Drive.Player();
                if (p == null) return true;
                p.MoveTo(new Vector2Int(_pendingMonster / 1000, _pendingMonster % 1000));
                return true;
            });
            Add("Den-wait", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.DenOfEvil)
                {
                    Log("FLOW", "entered DenOfEvil map=" + m.Width + "x" + m.Height + " seed=" + m.Seed
                        + " grid=" + Drive.Grid(Drive.PlayerGrid()));
                    return true;
                }
                if (Tout("Den-wait", 45f)) return true;
                return false;
            });
            Add("Den-log", () =>
            {
                LogStats("den-entry");
                LogWorld("den");
                LogMonsters("den");
                LogQuest("den-entry");
                return true;
            });
            Add("C5-den", () =>
            {
                if (!Elapsed(0.30f)) return false;
                if (!AreaTitleShowing())
                {
                    if (!Elapsed(1.4f)) return false;
                    NudgeAreaTitle((int)AreaId.DenOfEvil);
                    if (!Elapsed(1.7f)) return false;
                }
                DumpAreaTitle("x_c5_title_den.png");
                Shoot("C5-den", "x_c5_title_den.png", "screen", "area=DenOfEvil " + AreaTitleValues());
                return true;
            });
            Add("C5-den2", () => ShotLanded(15f));
            Add("C3-wide", () =>
            {
                if (!Elapsed(0.4f)) return false;
                Drive.RigSetOrtho(30f);
                return true;
            });
            Add("C3-wide2", () =>
            {
                if (!Elapsed(0.6f)) return false;
                Shoot("C3-wide", "x_c3_den_wide.png", "camera", "wide " + WorldValues());
                return true;
            });
            Add("C3-wide3", () => ShotLanded(20f));
            Add("C3-default", () =>
            {
                Drive.RigSetOrtho(3.75f);
                if (!Elapsed(1.0f)) return false;
                Shoot("C3", "x_c3_den_default.png", "camera", "default " + WorldValues());
                return true;
            });
            Add("C3-default2", () => ShotLanded(20f));
        }

        private string EquipmentValues()
        {
            var item = Drive.Item();
            if (item == null) return "(no item module)";
            var sb = new StringBuilder();
            var n = 0;
            foreach (var s in item.Equipment)
            {
                if (s == null) continue;
                n++;
                sb.Append('|').Append(s.name).Append(" id=").Append(s.itemId)
                  .Append(" q=").Append(s.quality).Append(" dmg=").Append(s.dmgMin).Append('-').Append(s.dmgMax)
                  .Append(" dur=").Append(s.durability).Append('/').Append(s.maxDurability);
            }
            return "equipmentCount=" + n + " equipment=" + sb;
        }
    }
}
namespace X
{
    /// <summary>Part G: skill tree + cast, the den clear, the walk back, and the turn-in.</summary>
    public partial class Driver
    {
        private void BuildPlanDen()
        {
            // ---------------------------------------------------------------- E3 skill tree
            Add("E3-open", () => { Drive.KeyDown("t"); return true; });
            Add("E3-open2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("E3-dump", () =>
            {
                if (!SkillTreeIsOpen() && !Elapsed(5f)) return false;
                if (!Elapsed(0.9f)) return false;
                DumpSkillTree("x_e3_skilltree.png");
                Shoot("E3", "x_e3_skilltree.png", "screen",
                    SkillTreeValues() + " " + SkillsCatalog());
                return true;
            });
            Add("E3-shot", () => ShotLanded(20f));
            Add("E3-close", () => { Drive.KeyDown("t"); return true; });
            Add("E3-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("E3-closed", () => { if (SkillTreeIsOpen() && !Elapsed(4f)) return false; return true; });

            // ---------------------------------------------------------------- E4 learn + cast a missile skill
            Add("E4-learn", () =>
            {
                var sk = Drive.Skill();
                if (sk == null) { Note("e4:no-skill-module"); return true; }
                if (_skillIdToLearn < 0)
                {
                    Diablo2.Def.SkillDef best = null;
                    foreach (var d in sk.Available)
                    {
                        if (d == null || !sk.CanLearn(d.id)) continue;
                        if (d.target == SkillTarget.Ground) { best = d; break; }
                        if (best == null) best = d;
                    }
                    if (best != null)
                    {
                        _skillIdToLearn = best.id;
                        Drive.KV("E4-PICK", "skillId=" + best.id + " name=\"" + best.name + "\" tree=" + best.tree
                            + " reqLevel=" + best.reqLevel + " manaCost=" + best.manaCost
                            + " dmg=" + best.dmgMin + "-" + best.dmgMax + " dmgType=" + best.dmgType
                            + " target=" + best.target);
                        var ok = sk.Learn(_skillIdToLearn);
                        if (ok)
                        {
                            sk.SelectSkill(_skillIdToLearn);
                            sk.AssignToButton(1, _skillIdToLearn);
                        }
                        Drive.KV("E4-LEARN", "skillId=" + _skillIdToLearn + " ok=" + (ok ? 1 : 0)
                            + " level=" + sk.GetLevel(_skillIdToLearn)
                            + " selected=" + sk.SelectedSkillId
                            + " rightButton=" + sk.GetButtonSkill(1)
                            + " skillPtsLeft=" + (Drive.Player() != null ? Drive.Player().SkillPoints : -1));
                        return true;
                    }
                    // No learnable skill (measured: a LOADED hero arrives with skillPts=0) -> fall
                    // back to a skill the hero already knows, so the cast station still produces
                    // the cast + missile frames instead of skipping X4 entirely.
                    var known = sk.GetButtonSkill(1);
                    if (known <= 0) known = sk.SelectedSkillId;
                    if (known <= 0)
                    {
                        Drive.Warn("E4 no learnable skill and the hero knows none (skillPoints="
                            + (Drive.Player() != null ? Drive.Player().SkillPoints : -1) + ")");
                        Note("e4:no-learnable-skill");
                        return true;
                    }
                    _skillIdToLearn = known;
                    sk.SelectSkill(known);
                    sk.AssignToButton(1, known);
                    Drive.KV("E4-KNOWN", "no learnable skill (skillPts="
                        + (Drive.Player() != null ? Drive.Player().SkillPoints : -1)
                        + ") -> using the skill the hero already knows id=" + known
                        + " level=" + sk.GetLevel(known) + " selected=" + sk.SelectedSkillId
                        + " rightButton=" + sk.GetButtonSkill(1));
                    return true;
                }
                var ok2 = sk.Learn(_skillIdToLearn);
                Drive.KV("E4-LEARN", "skillId=" + _skillIdToLearn + " ok=" + (ok2 ? 1 : 0)
                    + " level=" + sk.GetLevel(_skillIdToLearn)
                    + " selected=" + sk.SelectedSkillId
                    + " rightButton=" + sk.GetButtonSkill(1)
                    + " skillPtsLeft=" + (Drive.Player() != null ? Drive.Player().SkillPoints : -1));
                return true;
            });
            Add("E4-position", () =>
            {
                if (_skillIdToLearn < 0) return true;
                if (!Elapsed(0.5f)) return false;
                var id = NearestAliveMonster();
                if (id < 0) { Note("e4:no-monster"); return true; }
                _pendingMonster = id;
                if (!HopAtRange(id, 5, "e4-cast-range")) { Drive.Warn("E4 no stand cell at ring 5"); return true; }
                return true;
            });
            Add("E4-cast", () =>
            {
                if (_skillIdToLearn < 0 || _pendingMonster < 0) return true;
                if (!Elapsed(0.7f)) return false;
                var sk = Drive.Skill();
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_pendingMonster) : null;
                if (sk == null || st == null) return true;
                var mg = new Vector2Int(st.gridX, st.gridY);
                var p = Drive.Player();
                Drive.KV("E4-CAST-PRE", "m#" + _pendingMonster + " hp=" + st.hp + "/" + st.maxHp
                    + " cell=" + Drive.Grid(mg) + " dist="
                    + Iso.GridDistanceEuclidean(p != null ? p.Grid : mg, mg).ToString("0.000")
                    + " mana=" + (p != null ? p.Mana + "/" + p.MaxMana : "-")
                    + " cooldown=" + sk.GetCooldownRemain(_skillIdToLearn).ToString("0.00"));
                var ok = sk.TryCast(_skillIdToLearn, mg);
                Drive.KV("E4-CAST", "skillId=" + _skillIdToLearn + " ok=" + (ok ? 1 : 0)
                    + " knownLevel=" + sk.GetLevel(_skillIdToLearn)
                    + " manaAfter=" + (p != null ? p.Mana + "/" + p.MaxMana : "-")
                    + " cooldownAfter=" + sk.GetCooldownRemain(_skillIdToLearn).ToString("0.00")
                    + " damageEvents=" + _dmgDealt + " audio=" + AudioList());
                if (!ok && _counterB < 2)
                {
                    // one retry, so a first-frame mana/cooldown refusal does not swallow the station
                    _counterB++;
                    Drive.Warn("E4-CAST-RETRY n=" + _counterB + " TryCast returned false -> retrying after 0.6s");
                    _subAt = Time.unscaledTime;
                    return false;
                }
                if (!ok && Time.unscaledTime - _subAt < 0.6f) return false;
                Shoot("E4-a", "x_e4_cast_a.png", "camera",
                    "phase=cast t=0 skillId=" + _skillIdToLearn + " ok=" + (ok ? 1 : 0)
                    + " knownLevel=" + sk.GetLevel(_skillIdToLearn)
                    + " mana=" + (p != null ? p.Mana + "/" + p.MaxMana : "-")
                    + " target=" + Drive.Grid(mg) + " targetHp=" + st.hp + "/" + st.maxHp);
                return true;
            });
            Add("E4-cast2", () => ShotLanded(12f));
            Add("E4-flight", () =>
            {
                if (_skillIdToLearn < 0) return true;
                if (!Elapsed(0.18f)) return false;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_pendingMonster) : null;
                Shoot("E4-b", "x_e4_cast_b.png", "camera",
                    "phase=in-flight t=0.18 skillId=" + _skillIdToLearn
                    + " targetHp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " audio=" + AudioList());
                return true;
            });
            Add("E4-flight2", () => ShotLanded(12f));
            Add("E4-result", () =>
            {
                if (_skillIdToLearn < 0) return true;
                if (!Elapsed(1.6f)) return false;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(_pendingMonster) : null;
                var p = Drive.Player();
                Drive.KV("E4-RESULT", "skillId=" + _skillIdToLearn
                    + " targetHp=" + (st != null ? st.hp + "/" + st.maxHp : "-")
                    + " damageEvents=" + _dmgDealt + " mana=" + (p != null ? p.Mana + "/" + p.MaxMana : "-")
                    + " cooldown=" + (Drive.Skill() != null ? Drive.Skill().GetCooldownRemain(_skillIdToLearn).ToString("0.00") : "-")
                    + " audio=" + AudioList());
                return true;
            });

            // ---------------------------------------------------------------- G4 clear the den
            Add("G4-clear", () =>
            {
                var mon = Drive.Monster();
                var p = Drive.Player();
                if (mon == null) return true;
                var left = mon.CountInArea(AreaId.DenOfEvil);
                if (left == 0)
                {
                    EndHold("g4-done");
                    Drive.KV("G4-CLEARED", "denAreaAlive=0 kills=" + _kills + " levelUps=" + _levelUps
                        + " " + QuestValues("g4-cleared"));
                    Shoot("G4", "x_g4_den_cleared.png", "camera",
                        "phase=cleared denAreaAlive=0 " + QuestValues("g4-cleared") + " " + StatsValues());
                    return true;
                }
                if (Elapsed(300f))
                {
                    EndHold("g4-budget");
                    Drive.KV("G4-BUDGET", "denAreaAlive=" + left + " kills=" + _kills
                        + " note=the clear budget ran out; the remaining count is the honest state");
                    return true;
                }
                if (_holdMonster >= 0)
                {
                    if (!MonsterAlive(_holdMonster))
                    {
                        _g4Dead.Add(_holdMonster);
                        EndHold("g4-killed");
                        Drive.KV("G4-PROGRESS", "denAreaAlive=" + mon.CountInArea(AreaId.DenOfEvil)
                            + " kills=" + _kills + " level=" + (p != null ? p.Level : -1)
                            + " uniqueKilled=" + _g4Dead.Count
                            + " " + QuestValues("g4-step"));
                        return false;
                    }
                    if (Time.unscaledTime - _subAt > 30f) { EndHold("g4-timeout"); return false; }
                    return false;
                }
                // Prefer a monster we have never killed: a Shaman revives a corpse under the same id,
                // so plain "nearest alive" can hold on one revived zombie for ever and never reach the
                // reviver.  Only when every survivor has already been killed once do we fall back to
                // plain nearest (by then the revivers are gone and the count can only drop).
                var id = NearestAliveMonsterWhere(_g4Dead);
                if (id < 0)
                {
                    id = NearestAliveMonster();
                    if (id >= 0 && !_g4ReviveSkipLogged)
                    {
                        _g4ReviveSkipLogged = true;
                        Drive.Warn("G4-REVIVE-SKIP every living monster in the den has already been killed once"
                            + " -> falling back to plain nearest (shaman revival is being out-paced)"
                            + " uniqueKilled=" + _g4Dead.Count + " denAreaAlive=" + left);
                    }
                }
                if (id < 0)
                {
                    if (Tout("G4-nomonster", 20f)) return true;
                    return false;
                }
                if (!HopNearMonster(id, 1.5f))
                {
                    Drive.Warn("G4 no stand cell next to m#" + id + " -> skipping it this frame");
                    return false;
                }
                _subAt = Time.unscaledTime;
                BeginHold(id, "g4-clear");
                return false;
            });
            Add("G4-shot", () => ShotLanded(20f));
            Add("G4-log", () =>
            {
                LogProgress("after-clear");
                LogQuest("after-clear");
                LogMonsters("den");
                return true;
            });

            // ---------------------------------------------------------------- walk back to town
            Add("Back-1", () =>
            {
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.DenOfEvil) return true;
                if (m.Exits.Count == 0) { Note("back:den-no-exit"); return true; }
                Drive.KV("BACK-1", "denExit=" + Drive.Grid(m.Exits[0]) + " kind=" + Drive.TileKindAt(m.Exits[0])
                    + " exits=" + ExitsText(m));
                HopNearCell(m.Exits[0], "den-exit-stand");
                return true;
            });
            Add("Back-2", () =>
            {
                if (!Elapsed(0.5f)) return false;
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.DenOfEvil || m.Exits.Count == 0) return true;
                var p = Drive.Player();
                if (p != null) p.MoveTo(m.Exits[0]);
                return true;
            });
            Add("Back-3", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.BloodMoor)
                {
                    Log("FLOW", "back in BloodMoor grid=" + Drive.Grid(Drive.PlayerGrid()));
                    return true;
                }
                if (Tout("Back-3", 45f)) return true;
                return false;
            });
            Add("Back-4", () =>
            {
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.BloodMoor) return true;
                var town = MoorTownExit();
                if (town.x == int.MinValue) { Note("back:no-town-exit"); return true; }
                Drive.KV("BACK-4", "moorTownExit=" + Drive.Grid(town) + " kind=" + Drive.TileKindAt(town));
                HopNearCell(town, "moor-town-exit-stand");
                return true;
            });
            Add("Back-5", () =>
            {
                if (!Elapsed(0.5f)) return false;
                var m = Drive.Map();
                if (m == null || m.Area != AreaId.BloodMoor) return true;
                var town = MoorTownExit();
                if (town.x == int.MinValue) return true;
                var p = Drive.Player();
                if (p != null) p.MoveTo(town);
                return true;
            });
            Add("Back-6", () =>
            {
                var m = Drive.Map();
                if (m != null && m.Area == AreaId.Town)
                {
                    Log("FLOW", "back in Rogue Encampment grid=" + Drive.Grid(Drive.PlayerGrid()));
                    return true;
                }
                if (Tout("Back-6", 45f)) return true;
                return false;
            });

            // ---------------------------------------------------------------- G1c ready to turn in
            Add("G1c-walk", () =>
            {
                HopNearNpc((int)NpcId.Akara, "g1c-akara");
                return true;
            });
            Add("G1c-click", () =>
            {
                if (!Elapsed(0.6f)) return false;
                var g = NpcGrid((int)NpcId.Akara);
                if (g.x == int.MinValue) { Note("g1c:no-akara"); return true; }
                BeginGroundClick(g);
                return true;
            });
            Add("G1c-click2", () => TickGroundClickSeq() != 0);
            Add("G1c-open", () =>
            {
                if (!DialogIsOpen()) { if (Tout("G1c-open", 8f)) return true; return false; }
                Log("FLOW", "Akara dialog (ready-to-turn-in) " + OptionLabels());
                return true;
            });
            Add("G1c-dump", () =>
            {
                if (!Elapsed(0.8f)) return false;
                DumpDialog("x_g1_dialog_ready.png");
                Shoot("G1c", "x_g1_dialog_ready.png", "screen",
                    "state=ReadyToTurnIn " + DialogValues() + " " + QuestValues("g1c"));
                return true;
            });
            Add("G1c-shot", () => ShotLanded(20f));
            Add("G3c-key", () => { Drive.KeyDown("q"); return true; });
            Add("G3c-keyup", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3c-dump", () =>
            {
                if (!QuestLogIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.7f)) return false;
                DumpQuestTree("x_g3_questlog_ready.png");
                Shoot("G3c", "x_g3_questlog_ready.png", "screen",
                    "state=ReadyToTurnIn " + QuestTreeValues() + " " + QuestValues("g3c"));
                return true;
            });
            Add("G3c-shot", () => ShotLanded(20f));
            Add("G3c-close", () => { Drive.KeyDown("q"); return true; });
            Add("G3c-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3c-closed", () => { if (QuestLogIsOpen() && !Elapsed(3f)) return false; return true; });

            // ---------------------------------------------------------------- turn in -> Done
            Add("G1d-turnin", () =>
            {
                if (!DialogIsOpen()) { if (Tout("G1d-turnin-dialog", 6f)) return true; return false; }
                var before = Drive.Player() != null ? Drive.Player().SkillPoints : -1;
                _goldBefore = before;
                Drive.KV("G1d-CLICK", "clicking the turn-in option (real mouse) index=" + QuestOptionIndex()
                    + " options=" + OptionLabels() + " skillPtsBefore=" + before);
                ClickDialogOption(QuestOptionIndex(), "g1d-turnin");
                return true;
            });
            Add("G1d-wait", () =>
            {
                if (!Elapsed(1.2f)) return false;
                var q = Drive.Quest();
                var p = Drive.Player();
                Drive.KV("G1d-RESULT", "denState=" + (q != null ? q.DenOfEvil.ToString() : "-")
                    + " canTurnIn=" + (q != null && q.CanTurnInDen ? 1 : 0)
                    + " skillPtsBefore=" + _goldBefore
                    + " skillPtsAfter=" + (p != null ? p.SkillPoints : -1)
                    + " delta=" + (p != null ? p.SkillPoints - _goldBefore : int.MinValue));
                return true;
            });
            Add("G1d-dump", () =>
            {
                DumpDialog("x_g1_dialog_done.png");
                Shoot("G1d", "x_g1_dialog_done.png", "screen",
                    "state=Done " + DialogValues() + " " + QuestValues("g1d"));
                return true;
            });
            Add("G1d-shot", () => ShotLanded(20f));
            Add("G3d-key", () => { Drive.KeyDown("q"); return true; });
            Add("G3d-keyup", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3d-dump", () =>
            {
                if (!QuestLogIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.7f)) return false;
                DumpQuestTree("x_g3_questlog_done.png");
                Shoot("G3d", "x_g3_questlog_done.png", "screen",
                    "state=Done " + QuestTreeValues() + " " + QuestValues("g3d"));
                return true;
            });
            Add("G3d-shot", () => ShotLanded(20f));
            Add("G3d-close", () => { Drive.KeyDown("q"); return true; });
            Add("G3d-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("G3d-closed", () =>
            {
                if (QuestLogIsOpen() && !Elapsed(3f)) return false;
                if (DialogIsOpen()) { ClickDialogOption(0, "g1d-close"); return true; }
                return true;
            });
            Add("G3d-settle", () =>
            {
                if (!Elapsed(0.8f)) return false;
                LogProgress("after-turnin");
                return true;
            });
        }

        private string SkillsCatalog()
        {
            var sk = Drive.Skill();
            if (sk == null) return "(no skill module)";
            var p = Drive.Player();
            var sb = new StringBuilder();
            var n = 0;
            foreach (var d in sk.Available)
            {
                if (d == null) continue;
                n++;
                if (n > 12) continue;
                sb.Append('|').Append(d.id).Append(":\"").Append(d.name).Append("\" tree=").Append(d.tree)
                  .Append(" reqLv=").Append(d.reqLevel).Append(" mana=").Append(d.manaCost)
                  .Append(" dmg=").Append(d.dmgMin).Append('-').Append(d.dmgMax)
                  .Append(" type=").Append(d.dmgType).Append(" target=").Append(d.target)
                  .Append(" canLearn=").Append(sk.CanLearn(d.id) ? 1 : 0)
                  .Append(" known=").Append(sk.GetLevel(d.id));
            }
            return "skillPoints=" + (p != null ? p.SkillPoints : -1)
                + " level=" + (p != null ? p.Level : -1)
                + " availableCount=" + sk.Available.Count
                + " selected=" + sk.SelectedSkillId + " catalog=" + sb;
        }
    }
}
namespace X
{
    /// <summary>Part H: death + revive, save-and-exit + re-enter, pause/options, quit, and Finish.</summary>
    public partial class Driver
    {
        private int _h3Level = -1, _h3Gold = -1, _h3Bag = -1, _h3DenState = -1, _h3GridX = int.MinValue, _h3GridY = int.MinValue;
        private int _h1GoldAfterDeath = -1;

        private void BuildPlanEnd()
        {
            // ---------------------------------------------------------------- H1 death screen + revive
            Add("H1-kill", () =>
            {
                var p = Drive.Player();
                if (p == null) { Note("h1:no-player"); return true; }
                if (p.IsDead) { if (Tout("H1-kill-already-dead", 15f)) return true; return false; }
                _h1GoldBefore = p.Gold;
                Drive.KV("H1-KILL", "calling IPlayerModule.Kill() (the documented production entry)"
                    + " goldBefore=" + _h1GoldBefore + " life=" + p.Life + "/" + p.MaxLife
                    + " grid=" + Drive.Grid(p.Grid) + " area="
                    + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-"));
                p.Kill();
                return true;
            });
            Add("H1-shot", () =>
            {
                if (!DeathIsOpen()) { if (Tout("H1-shot", 15f)) return true; return false; }
                if (!Elapsed(1.5f)) return false;
                var p = Drive.Player();
                _h1GoldAfterDeath = p != null ? p.Gold : -1;
                DumpDeath("x_h1_death.png");
                Shoot("H1", "x_h1_death.png", "screen",
                    DeathValues() + " goldBefore=" + _h1GoldBefore + " goldAfterDeath=" + _h1GoldAfterDeath
                    + " goldDelta=" + (_h1GoldAfterDeath - _h1GoldBefore));
                return true;
            });
            Add("H1-shot2", () => ShotLanded(20f));
            Add("H1-revive-click", () =>
            {
                if (!DeathIsOpen()) return true;
                var cont = Drive.FindButton("Continue", out _);
                if (cont != null && !cont.interactable && !Elapsed(8f)) return false;
                Drive.KV("H1-CLICK", "clicking Continue (real mouse) interactable="
                    + (cont != null ? (cont.interactable ? 1 : 0) : -1));
                ClickNamedRealMouse("Continue", "h1");
                return true;
            });
            Add("H1-wait-close", () =>
            {
                if (!DeathIsOpen())
                {
                    Log("H1-REVIVED", "deathPanelClosed=1 " + StatsValues()
                        + " goldAfterRevive=" + (Drive.Player() != null ? Drive.Player().Gold : -1));
                    return true;
                }
                if (Tout("H1-wait-close", 6f)) return true;
                return false;
            });
            Add("H1-nudge", () =>
            {
                if (!DeathIsOpen()) return true;
                var p = Drive.Player();
                Drive.Warn("H1-PANEL-STUCK the death panel is still open after the Continue click;"
                    + " emitting " + Diablo2.Core.Events.Revived + " (its documented close signal) as a probe nudge"
                    + " playerDead=" + (p != null && p.IsDead ? 1 : 0));
                Game.Event.Emit(Diablo2.Core.Events.Revived);
                return true;
            });
            Add("H1-nudge2", () =>
            {
                if (!Elapsed(0.8f)) return false;
                Log("H1-AFTER-NUDGE", "deathPanelOpen=" + (DeathIsOpen() ? 1 : 0) + " " + StatsValues());
                return true;
            });

            // ---------------------------------------------------------------- H3 save and exit -> re-enter
            Add("H3-pre", () =>
            {
                var p = Drive.Player();
                var q = Drive.Quest();
                if (p == null) return true;
                _h3Level = p.Level;
                _h3Gold = p.Gold;
                _h3Bag = CountInventoryOccupied();
                _h3DenState = q != null ? (int)q.DenOfEvil : -1;
                _h3GridX = p.Grid.x;
                _h3GridY = p.Grid.y;
                Drive.KV("H3-PRE", "level=" + _h3Level + " gold=" + _h3Gold + " bagAnchors=" + _h3Bag
                    + " denState=" + _h3DenState + " grid=(" + _h3GridX + "," + _h3GridY + ")"
                    + " exp=" + p.Exp + " skillPts=" + p.SkillPoints + " " + StatsValues());
                return true;
            });
            Add("H3-pause", () => { Drive.KeyDown("escape"); return true; });
            Add("H3-pause2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("H3-pause-open", () =>
            {
                if (!PauseIsOpen())
                {
                    if (!Elapsed(3f)) return false;
                    Drive.Warn("H3 ESC did not open the pause menu -> Events.PauseRequest fallback");
                    Game.Event.Emit(Diablo2.Core.Events.PauseRequest);
                    if (!Elapsed(5f)) return false;
                }
                Log("H3-PAUSE", "pauseOpen=" + (PauseIsOpen() ? 1 : 0) + " timeScale=" + Time.timeScale.ToString("0.##"));
                return true;
            });
            Add("H3-saveexit", () =>
            {
                if (!Elapsed(0.5f)) return false;
                Drive.KV("H3-SAVE-EXIT", "clicking SAVE & EXIT (real mouse) fsm=" + Fsm());
                ClickNamedRealMouse("SaveExit", "h3");
                return true;
            });
            Add("H3-wait-menu", () =>
            {
                if (!MenuOpen()) { if (Tout("H3-wait-menu", 45f)) return true; return false; }
                Log("FLOW", "SAVE & EXIT -> MainMenu fsm=" + Fsm() + " scene=" + SceneName());
                return true;
            });
            Add("H3-reenter", () =>
            {
                if (!Elapsed(1.0f)) return false;
                ClickNamedRealMouse("Single", "h3-single");
                return true;
            });
            Add("H3-wait-select", () =>
            {
                if (!SelectOpen()) { if (Tout("H3-wait-select", 25f)) return true; return false; }
                var idx = FindRowIndexOf(_newHeroName);
                var row = idx >= 0 ? "Row" + idx : "Row0";
                Drive.KV("H3-ROW", "hero=\"" + _newHeroName + "\" rowIndex=" + idx + " click=\"" + row + "|Enter\"");
                ClickNamedRealMouse(row + "|Enter", "h3-enter");
                return true;
            });
            Add("H3-wait-stage", () =>
            {
                if (!HudOpen() || Fsm() != "Stage") { if (Tout("H3-wait-stage", 45f)) return true; return false; }
                Log("FLOW", "re-entered the game fsm=" + Fsm()
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-"));
                return true;
            });
            Add("H3-verify", () =>
            {
                if (!Elapsed(1.5f)) return false;
                var p = Drive.Player();
                var q = Drive.Quest();
                if (p == null) return true;
                var bag = CountInventoryOccupied();
                var den = q != null ? (int)q.DenOfEvil : -1;
                var gx = p.Grid.x;
                var gy = p.Grid.y;
                Drive.KV("H3-VERIFY", "level=" + _h3Level + "->" + p.Level
                    + " (same=" + (_h3Level == p.Level ? 1 : 0) + ")"
                    + " gold=" + _h3Gold + "->" + p.Gold + " (same=" + (_h3Gold == p.Gold ? 1 : 0) + ")"
                    + " bagAnchors=" + _h3Bag + "->" + bag + " (same=" + (_h3Bag == bag ? 1 : 0) + ")"
                    + " denState=" + _h3DenState + "->" + den + " (same=" + (_h3DenState == den ? 1 : 0) + ")"
                    + " grid=(" + _h3GridX + "," + _h3GridY + ")->(" + gx + "," + gy + ")"
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-")
                    + " exp=" + p.Exp + " skillPts=" + p.SkillPoints);
                Shoot("H3", "x_h3_reentry.png", "screen",
                    "level=" + _h3Level + "->" + p.Level + " gold=" + _h3Gold + "->" + p.Gold
                    + " bagAnchors=" + _h3Bag + "->" + bag + " denState=" + _h3DenState + "->" + den
                    + " grid=(" + _h3GridX + "," + _h3GridY + ")->(" + gx + "," + gy + ")"
                    + " area=" + (Drive.Map() != null ? Drive.Map().Area.ToString() : "-") + " " + HudValues());
                _h3Bag = bag;
                return true;
            });
            Add("H3-shot", () => ShotLanded(20f));

            // ---------------------------------------------------------------- H2 pause + options + main menu + quit
            Add("H2-esc", () => { Drive.KeyDown("escape"); return true; });
            Add("H2-esc2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("H2-pause", () =>
            {
                if (!PauseIsOpen())
                {
                    if (!Elapsed(3f)) return false;
                    Drive.Warn("H2 ESC did not open the pause menu -> Events.PauseRequest fallback");
                    Game.Event.Emit(Diablo2.Core.Events.PauseRequest);
                    if (!Elapsed(5f)) return false;
                }
                if (!Elapsed(0.5f)) return false;
                Drive.KV("H2-PAUSE", "pauseOpen=" + (PauseIsOpen() ? 1 : 0)
                    + " timeScale=" + Time.timeScale.ToString("0.##") + " fsm=" + Fsm());
                Shoot("H2-pause", "x_h2_pause.png", "screen",
                    "pauseOpen=" + (PauseIsOpen() ? 1 : 0) + " timeScale=" + Time.timeScale.ToString("0.##")
                    + " fsm=" + Fsm() + " " + StatsValues());
                return true;
            });
            Add("H2-pause-shot", () => ShotLanded(20f));
            Add("H2-open-options", () =>
            {
                _counterA = 0;
                ClickNamedRealMouse("Options", "h2-options");
                return true;
            });
            Add("H2-options-before", () =>
            {
                if (!SettingsIsOpen() && !Elapsed(5f)) return false;
                if (!Elapsed(0.8f)) return false;
                DumpSettings("x_h2_options_before.png");
                Shoot("H2-opt-before", "x_h2_options_before.png", "screen", "phase=before " + SettingsValues());
                return true;
            });
            Add("H2-options-shot1", () => ShotLanded(20f));
            Add("H2-minus", () =>
            {
                if (_counterA < 3)
                {
                    if (Elapsed(0.3f) || _counterA == 0)
                    {
                        ClickNamedRealMouse("Minus0", "h2-minus" + _counterA);
                        _counterA++;
                        _subAt = Time.unscaledTime;
                    }
                    return false;
                }
                return true;
            });
            Add("H2-minus-wait", () => { if (Time.unscaledTime - _subAt < 0.9f) return false; return true; });
            Add("H2-quality", () =>
            {
                ClickNodeRealMouse("SettingsPanel", "画质Toggle", "h2-quality");
                return true;
            });
            Add("H2-options-after", () =>
            {
                if (!Elapsed(1.0f)) return false;
                DumpSettings("x_h2_options_after.png");
                Shoot("H2-opt-after", "x_h2_options_after.png", "screen", "phase=after " + SettingsValues());
                return true;
            });
            Add("H2-options-shot2", () => ShotLanded(20f));
            Add("H2-close-options", () =>
            {
                ClickNamedRealMouse("Close", "h2-close-options");
                return true;
            });
            Add("H2-options-closed", () =>
            {
                if (SettingsIsOpen() && !Elapsed(5f)) return false;
                Log("H2-OPTIONS-CLOSED", "optionsOpen=" + (SettingsIsOpen() ? 1 : 0)
                    + " pauseOpen=" + (PauseIsOpen() ? 1 : 0) + " timeScale=" + Time.timeScale.ToString("0.##"));
                return true;
            });
            Add("H2-recheck", () =>
            {
                if (!Elapsed(0.6f)) return false;
                Drive.KV("H2-PERSIST", "re-open check will happen via the values below;"
                    + " settingBgm=" + ReadSettingF("audio/bgm_volume")
                    + " settingQuality=" + ReadSettingI("video/quality")
                    + " engineQuality=" + QualitySettings.GetQualityLevel()
                    + " note=run2 reads the same keys at cold start (STARTUP-SETTINGS)");
                return true;
            });
            Add("H2-tomain", () =>
            {
                if (!PauseIsOpen())
                {
                    Drive.Warn("H2 pause menu not open before MAIN MENU -> re-pausing");
                    Game.Event.Emit(Diablo2.Core.Events.PauseRequest);
                    if (!Elapsed(3f)) return false;
                }
                ClickNamedRealMouse("ToMain", "h2-tomain");
                return true;
            });
            Add("H2-confirm", () =>
            {
                if (!Elapsed(0.8f)) return false;
                var c = Drive.FindButton("Confirm", out _);
                if (c == null) { Drive.Warn("H2 no confirm button for MAIN MENU"); return true; }
                Drive.KV("H2-TOMAIN-CONFIRM", "confirmPath=" + Drive.PathOf(c.transform));
                ClickNamedRealMouse("Confirm", "h2-confirm");
                return true;
            });
            Add("H2-mainmenu", () =>
            {
                if (!MenuOpen()) { if (Tout("H2-mainmenu", 45f)) return true; return false; }
                if (!Elapsed(0.8f)) return false;
                Log("H2-MAINMENU", "fsm=" + Fsm() + " scene=" + SceneName() + " timeScale=" + Time.timeScale.ToString("0.##"));
                Shoot("H2-menu", "x_h2_mainmenu.png", "screen",
                    "fsm=" + Fsm() + " scene=" + SceneName()
                    + " timeScale=" + Time.timeScale.ToString("0.##")
                    + " settingBgm=" + ReadSettingF("audio/bgm_volume")
                    + " settingQuality=" + ReadSettingI("video/quality"));
                return true;
            });
            Add("H2-mainmenu-shot", () => ShotLanded(20f));
            Add("H2-quit", () =>
            {
                Drive.KV("H2-QUIT", "emitting Events.QuitRequest (the MainMenu EXIT button emits this same constant;"
                    + " a real click would stop the Play session before the done marker is written,"
                    + " so the marker is written first and the quit is emitted right after)");
                Finish("quit-requested");
                Game.Event.Emit(Diablo2.Core.Events.QuitRequest);
                return true;
            });
        }

        // ================================================================ finish ============
        private void Finish(string why)
        {
            if (_doneRun) return;
            _doneRun = true;

            var p = Drive.Player();
            var q = Drive.Quest();
            var m = Drive.Map();
            var cam = Camera.main;
            Drive.KV("FINISH", "why=" + why + " pc=" + _pc + " shots=" + _shots
                + " gridLines=" + _shotSeq
                + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)")
                + " playerDead=" + (p != null && p.IsDead ? 1 : 0)
                + " level=" + (p != null ? p.Level : -1)
                + " kills=" + _kills + " levelUps=" + _levelUps
                + " denState=" + (q != null ? q.DenOfEvil.ToString() : "-")
                + " canTurnIn=" + (q != null && q.CanTurnInDen ? 1 : 0)
                + " area=" + (m != null ? m.Area.ToString() : "-")
                + " rigOrthoNow=" + Drive.RigOrtho().ToString("0.###")
                + " camOrthoNow=" + (cam != null ? cam.orthographicSize.ToString("0.###") : "-")
                + " audioClips=" + AudioList()
                + " " + State());

            Drive.Check("device-not-software", SafeDevice().Length > 0
                && SafeDevice().IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) < 0,
                "graphicsDeviceName=\"" + SafeDevice() + "\"");
            Drive.Check("pacing-logged", _pacingLogged, "pacing samples logged=" + (_pacingLogged ? 1 : 0));
            Drive.Check("shots", _shots >= 40, "shots=" + _shots + " (expected >= 40 tiles)");
            Drive.Check("grid-lines", _shotSeq >= 40, "gridLines=" + _shotSeq);
            Drive.Check("no-timeout", _timedOutAt.Length == 0,
                "timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            Drive.Check("level-2", p != null && p.Level >= 2, "level=" + (p != null ? p.Level : -1)
                + " kills=" + _kills + " levelUps=" + _levelUps);
            Drive.Check("den-cleared", q != null && (q.DenOfEvil == QuestState.ReadyToTurnIn || q.DenOfEvil == QuestState.Done),
                "denState=" + (q != null ? q.DenOfEvil.ToString() : "-")
                + " denAreaAlive=" + (Drive.Monster() != null ? Drive.Monster().CountInArea(AreaId.DenOfEvil) : -1));
            Drive.Check("quest-done", q != null && q.DenOfEvil == QuestState.Done,
                "denState=" + (q != null ? q.DenOfEvil.ToString() : "-"));
            Drive.Check("player-alive", p != null && !p.IsDead, "dead=" + (p != null && p.IsDead ? 1 : 0));
            Drive.Check("ortho-restored", Mathf.Abs(Drive.RigOrtho() - 3.75f) < 0.001f,
                "rigOrtho=" + Drive.RigOrtho().ToString("0.###") + " expected=3.75");

            var ok = Drive.AllChecksOk();
            Drive.Log("VERDICT ok=" + (ok ? 1 : 0) + " " + Drive.CheckSummary());
            Drive.Log("TOUR-DONE why=" + why + " pc=" + _pc + " shots=" + _shots
                + " grids=" + _shotSeq + " timeoutAt=" + (_timedOutAt.Length > 0 ? _timedOutAt : "(none)"));
            var notes = string.Join(" ; ", _notes.ToArray());
            Drive.Log("NOTES " + (notes.Length > 0 ? notes : "(none)"));
            Drive.KV("SUMMARY", "tag=" + _tag + " shots=" + _shots + " grids=" + _shotSeq
                + " timeouts=" + _notes.Count + " kills=" + _kills + " levelUps=" + _levelUps
                + " level=" + (p != null ? p.Level : -1)
                + " denState=" + (q != null ? q.DenOfEvil.ToString() : "-"));
            Drive.WriteFile(Drive.DonePath,
                "TOUR-DONE tag=" + _tag + " why=" + why + " shots=" + _shots
                + " verdict=" + (ok ? 1 : 0) + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
namespace X
{
    /// <summary>
    /// X batch additions: X1 (6-frame walk trace) / X2 (hit trio, delivered by the E1 stations
    /// above) / X3 (two consecutive normal attacks) / X5 (equip before) / X6 (HUD full screen) /
    /// X7 (character stats panel) / X9 (cursor default + hover).  Same machinery as the rest of
    /// the tour (`Add` / `Shoot` / `ShotLanded`); this part only adds the stations and the few
    /// fields they need.  Values only -- no verdict words.
    /// </summary>
    public partial class Driver
    {
        // ---- X1 walk trace -------------------------------------------------------
        private int _x1Shots;
        private int _x1LastFrame = -9999;
        private Vector3 _x1PrevWorld;
        private Vector2 _x1PrevCell;
        private bool _x1CellOk;
        private Vector2Int _x1Target = new Vector2Int(int.MinValue, int.MinValue);
        private int _x1Keep;

        // ---- X3 two attacks ------------------------------------------------------
        private int _x3AtkBase;
        private int _x3Atk1;

        // ---- front-end robustness (char create -> roster ENTER) -------------------
        private bool _enterClicked;

        /// <summary>
        /// Row index of the freshly created hero.  The roster labels are NOT `UnityEngine.UI.Text`
        /// (CharSelectPanel builds them with `UiLayoutFlow.FlowLabel`, which renders an original
        /// bitmap font) so the Text-based lookup finds nothing -- measured: rows=0, and the tour
        /// then entered an old save.  `FlowLabel` keeps its string in `_text`; the panel keeps the
        /// labels in `_rowLabels` (3 per row: Name/Class/Level), so row = index / 3.
        /// </summary>
        private int FindRowIndexOfNewHero(string heroName)
        {
            var sel = Drive.CharSelectPanel();
            if (sel == null || string.IsNullOrEmpty(heroName) || heroName == "(null)") return -1;
            var list = Drive.Field(sel, "_rowLabels") as System.Collections.IEnumerable;
            if (list == null) { Drive.Warn("ROSTER-LOOKUP `_rowLabels` not found on CharSelectPanel"); return -1; }
            var i = 0;
            foreach (var lab in list)
            {
                if (lab == null) { i++; continue; }
                var txt = Drive.Fmt(Drive.Field(lab, "_text"));
                if (txt != null && txt.Trim() == heroName.Trim()) return i / 3;
                i++;
            }
            return -1;
        }

        /// <summary>Dump the roster rows (label -> Row index) so a wrong-row ENTER is visible.</summary>
        private void LogRoster(string tag)
        {
            var sel = Drive.CharSelectPanel();
            if (sel == null) { Log("ROSTER", "tag=" + tag + " charSelectOpen=0"); return; }
            var sb = new StringBuilder();
            var n = 0;
            foreach (var t in sel.GetComponentsInChildren<Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                var cur = t.transform;
                while (cur != null)
                {
                    if (cur.name.StartsWith("Row", StringComparison.Ordinal))
                    {
                        sb.Append('|').Append(cur.name).Append("=\"").Append(Drive.Esc(t.text.Trim())).Append('"');
                        n++;
                        break;
                    }
                    if (cur == sel.transform) break;
                    cur = cur.parent;
                }
            }
            var save = Drive.Save();
            var names = "(n/a)";
            if (save != null) { var l = save.List(); if (l != null) names = string.Join(",", l.ToArray()); }
            Log("ROSTER", "tag=" + tag + " rows=" + n + " newHero=\"" + _newHeroName + "\""
                + " saved=[" + names + "]" + sb);
        }

        private static bool CharSheetIsOpen()
        {
            return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharacterPanel>();
        }

        private static int MonHp(int id)
        {
            var m = Drive.Monster();
            var s = m != null ? m.Get(id) : null;
            return s != null ? s.hp : -1;
        }

        /// <summary>Pick a straight run of >= 10 walkable cells, teleport to its head, start walking.</summary>
        private bool HopToWalkSpotX()
        {
            var map = Drive.Map();
            if (map == null) return false;
            var centre = new Vector2Int(map.Width / 2, map.Height / 2);
            var bestFrom = new Vector2Int(int.MinValue, int.MinValue);
            var bestTo = new Vector2Int(int.MinValue, int.MinValue);
            var bestLen = 0;
            var lens = new[] { 14, 12, 10 };
            for (var li = 0; li < lens.Length && bestTo.x == int.MinValue; li++)
            {
                var len = lens[li];
                for (var rad = 0; rad <= 26 && bestTo.x == int.MinValue; rad += 2)
                {
                    for (var dx = -rad; dx <= rad && bestTo.x == int.MinValue; dx++)
                    {
                        for (var dy = -rad; dy <= rad && bestTo.x == int.MinValue; dy++)
                        {
                            var c = new Vector2Int(centre.x + dx, centre.y + dy);
                            if (!map.InBounds(c) || !map.Walkable(c)) continue;
                            var ok = true;
                            for (var i = 1; i <= len; i++)
                            {
                                var g = new Vector2Int(c.x, c.y - i);   // straight run along -y
                                if (!map.InBounds(g) || !map.Walkable(g) || map.TileAt(g) == TileKind.Exit)
                                { ok = false; break; }
                            }
                            if (!ok) continue;
                            bestFrom = c;
                            bestTo = new Vector2Int(c.x, c.y - len);
                            bestLen = len;
                        }
                    }
                }
            }
            if (bestTo.x == int.MinValue)
            {
                Drive.Warn("X1-SPOT none found (no straight run of >=10 walkable cells)");
                return false;
            }
            if (!Hop(bestFrom, "x1-walk-from")) return false;
            _x1Target = bestTo;
            _x1Keep = 0;
            _x1LastFrame = -9999;
            var pl = Drive.Player();
            pl.MoveTo(bestTo);
            Drive.KV("X1-SPOT", "from=" + Drive.Grid(bestFrom) + " to=" + Drive.Grid(bestTo)
                + " cells=" + bestLen + " dir=" + Iso.DirectionTo(bestFrom, bestTo)
                + " running=" + (pl.IsRunning ? 1 : 0)
                + " moveSpeed=" + Drive.Fmt(Drive.Prop(pl, "MoveSpeed")));
            return true;
        }

        private void KeepWalkingX1()
        {
            if (Drive.PlayerMoving()) return;
            if (_x1Keep >= 4) return;
            _x1Keep++;
            var p = Drive.Player();
            if (p == null) return;
            p.MoveTo(_x1Target);
            Drive.KV("X1-REISSUE", "n=" + _x1Keep + " target=" + Drive.Grid(_x1Target)
                + " grid=" + Drive.Grid(Drive.PlayerGrid()));
        }

        private string CharStatsValues()
        {
            var sheet = Drive.CharacterSheet();
            if (sheet == null) return "charSheetOpen=0";
            var sb = new StringBuilder();
            var n = 0;
            foreach (var t in sheet.GetComponentsInChildren<Text>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.text)) continue;
                if (n++ >= 24) break;
                sb.Append('|').Append(Drive.Esc(t.text)).Append(' ').Append(Drive.RectOfComponent(t.rectTransform));
            }
            return "charSheetOpen=1 panel=" + Drive.RectOf(sheet.GetComponent<RectTransform>())
                + " texts=" + sb + " " + StatsValues();
        }

        // ================================================================ X plan pieces =====
        /// <summary>X1 walk trace + X6 HUD + X7 char stats + X9 default cursor -- all in town.</summary>
        private void BuildPlanXTown()
        {
            Add("X1-setup", () =>
            {
                _x1Shots = 0;
                _x1Keep = 99;
                if (!HopToWalkSpotX())
                {
                    _x1Shots = 6;
                    Drive.KV("X1-DONE", "shots=0 reason=no-straight-run");
                    return true;
                }
                _x1PrevWorld = Drive.PlayerWorld();
                _x1CellOk = Drive.CellPos(_x1PrevWorld, out _x1PrevCell);
                return true;
            });
            Add("X1-walk", () =>
            {
                if (_x1Shots >= 6) return true;
                if (_pendTile.Length > 0 && !ShotLanded(15f)) return false;
                KeepWalkingX1();
                if (Time.frameCount - _x1LastFrame < 3) return false;
                var w = Drive.PlayerWorld();
                Vector2 cell;
                var cellOk = Drive.CellPos(w, out cell);
                var dw = Vector3.Distance(_x1PrevWorld, w);
                var dc = cellOk && _x1CellOk ? Vector2.Distance(_x1PrevCell, cell) : -1f;
                _x1PrevWorld = w;
                if (cellOk) { _x1PrevCell = cell; _x1CellOk = true; }
                _x1LastFrame = Time.frameCount;
                _x1Shots++;
                Drive.KV("X1", "n=" + _x1Shots + " frame=" + Time.frameCount
                    + " t=" + Time.unscaledTime.ToString("0.000")
                    + " motorWorld=" + Drive.World(w)
                    + " dWorldFromPrev=" + dw.ToString("0.0000")
                    + " dCellFromPrev=" + dc.ToString("0.0000")
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0)
                    + " sprite=" + Drive.PlayerSpriteName()
                    + " grid=" + Drive.Grid(Drive.PlayerGrid())
                    + " dt=" + Time.unscaledDeltaTime.ToString("0.00000"));
                Shoot("X1-frame" + _x1Shots, "a09_walk_" + _x1Shots + ".png", "camera",
                    "n=" + _x1Shots + " grid=" + Drive.Grid(Drive.PlayerGrid())
                    + " sprite=" + Drive.PlayerSpriteName()
                    + " dWorldFromPrev=" + dw.ToString("0.0000")
                    + " dCellFromPrev=" + dc.ToString("0.0000")
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                return false;
            });
            Add("X1-done", () =>
            {
                if (_pendTile.Length > 0 && !ShotLanded(15f)) return false;
                Drive.KV("X1-DONE", "shots=" + _x1Shots + " playerGrid=" + Drive.Grid(Drive.PlayerGrid()));
                return true;
            });

            // ---- X6 HUD full screen (orbs + exp bar + skill bar + belt + mini panel + run/walk)
            Add("X6-hud", () =>
            {
                if (!Elapsed(0.4f)) return false;
                DumpHud("x_x6_hud.png");
                Shoot("X6", "x_x6_hud.png", "screen",
                    HudValues() + " " + BeltValues("x6") + " " + StatsValues());
                return true;
            });
            Add("X6-hud-shot", () => ShotLanded(20f));

            // ---- X7 character stats panel (C)
            Add("X7-stats-open", () => { Drive.KeyDown("c"); return true; });
            Add("X7-stats-open2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("X7-stats", () =>
            {
                if (!CharSheetIsOpen() && !Elapsed(4f)) return false;
                if (!Elapsed(0.8f)) return false;
                var sheet = Drive.CharacterSheet();
                Drive.DumpTree("charstats", sheet != null ? sheet.transform : null, 26, 3);
                Shoot("X7-stats", "x_x7_charstats.png", "screen", CharStatsValues());
                return true;
            });
            Add("X7-stats-shot", () => ShotLanded(20f));
            Add("X7-stats-close", () => { Drive.KeyDown("c"); return true; });
            Add("X7-stats-close2", () => { if (!Elapsed(0.2f)) return false; Drive.KeyUp(); return true; });
            Add("X7-stats-closed", () =>
            {
                if (CharSheetIsOpen() && !Elapsed(4f)) return false;
                Log("X7-STATS-CLOSED", "charSheetOpen=" + (CharSheetIsOpen() ? 1 : 0)
                    + " note=C toggles the character stats panel off again");
                return true;
            });

            // ---- X9 default cursor: park the pointer on open walkable ground
            // X9 needs a STILL camera: the injected pointer is a screen point, and while the player
            // walks the follow camera scrolls -> the same screen point resolves to a different cell
            // (measured: aimed at cell (22,21), the hover reported (22,18) and a NoWalk cursor).
            Add("X9-settle", () =>
            {
                if (Drive.PlayerMoving())
                {
                    if (Tout("X9-settle", 10f)) return true;
                    return false;
                }
                if (!Elapsed(0.8f)) return false;
                Drive.KV("X9-SETTLE", "playerStopped=1 playerGrid=" + Drive.Grid(Drive.PlayerGrid())
                    + " moving=" + (Drive.PlayerMoving() ? 1 : 0));
                return true;
            });
            Add("X9-default-move", () =>
            {
                var cam = Camera.main;
                var p = Drive.Player();
                if (cam == null || p == null) return true;
                var best = new Vector2Int(int.MinValue, int.MinValue);
                for (var r = 2; r <= 8 && best.x == int.MinValue; r++)
                {
                    for (var dx = -r; dx <= r && best.x == int.MinValue; dx++)
                    {
                        for (var dy = -r; dy <= r && best.x == int.MinValue; dy++)
                        {
                            var g = new Vector2Int(p.Grid.x + dx, p.Grid.y + dy);
                            if (!Drive.Walkable(g)) continue;
                            var sp = cam.WorldToScreenPoint(Iso.GridToWorld(g));
                            if (sp.x < 80f || sp.x > Screen.width - 80f) continue;
                            if (sp.y < 80f || sp.y > Screen.height - 80f) continue;
                            best = g;
                        }
                    }
                }
                if (best.x == int.MinValue)
                {
                    Drive.Warn("X9 no walkable cell on screen for the default-cursor probe");
                    return true;
                }
                Drive.MouseState(Drive.CellInjectPoint(best), false);
                Drive.KV("X9-DEFAULT-POS", "cell=" + Drive.Grid(best) + " tileKind=" + Drive.TileKindAt(best)
                    + " walkable=1 playerGrid=" + Drive.Grid(p.Grid));
                return true;
            });
            Add("X9-default", () =>
            {
                if (!Elapsed(0.9f)) return false;
                Shoot("X9-default", "x_x9_cursor_default.png", "screen",
                    "probe=DefaultOverGround cursorKind=" + _lastCursor
                    + " cursorChanges=" + _cursorChanges + " hover=" + _lastHover);
                return true;
            });
            Add("X9-default-shot", () => ShotLanded(20f));
        }

        /// <summary>X3: two consecutive normal attacks, 2 frames each (wind-up + strike).</summary>
        private void BuildPlanXAttack()
        {
            Add("X3-setup", () =>
            {
                // ALWAYS take a fresh LIVE monster: the one E1 hit is dead by now (measured:
                // X3-SETUP m#1001 hp=0 -> the hold produced no attacks and the station timed out).
                var id = NearestAliveMonster();
                if (id < 0) { Note("x3:no-monster"); return true; }
                if (!HopNearMonster(id, 1.5f)) { Note("x3:no-stand-cell"); return true; }
                _pendingMonster = id;
                _x3AtkBase = _attacks;
                _x3Atk1 = _attacks;
                BeginHold(_pendingMonster, "x3-two-attacks");
                Drive.KV("X3-SETUP", "m#" + _pendingMonster + " hp=" + MonHp(_pendingMonster)
                    + " attacksBefore=" + _x3AtkBase + " meleeRange=1.6 " + StatsValues());
                return true;
            });
            Add("X3-atk1-a", () =>
            {
                if (_holdMonster < 0) return true;
                if (_attacks <= _x3AtkBase) { if (Tout("X3-atk1-a", 15f)) return true; return false; }
                _x3Atk1 = _attacks;
                if (!Elapsed(0.10f)) return false;
                Shoot("X3-a1", "x_x3_atk1_a.png", "camera",
                    "attackIndex=1 phase=windup attacks=" + _attacks + " m#" + _holdMonster
                    + " hp=" + MonHp(_holdMonster) + " " + StatsValues());
                return true;
            });
            Add("X3-atk1-a2", () => ShotLanded(15f));
            Add("X3-atk1-b", () =>
            {
                if (!Elapsed(0.12f)) return false;
                Shoot("X3-a1b", "x_x3_atk1_b.png", "camera",
                    "attackIndex=1 phase=strike attacks=" + _attacks + " m#" + _holdMonster
                    + " hp=" + MonHp(_holdMonster) + " " + StatsValues());
                return true;
            });
            Add("X3-atk1-b2", () => ShotLanded(15f));
            Add("X3-atk2-a", () =>
            {
                if (_attacks <= _x3Atk1) { if (Tout("X3-atk2-a", 15f)) return true; return false; }
                if (!Elapsed(0.10f)) return false;
                Shoot("X3-a2", "x_x3_atk2_a.png", "camera",
                    "attackIndex=2 phase=windup attacks=" + _attacks + " m#" + _holdMonster
                    + " hp=" + MonHp(_holdMonster) + " " + StatsValues());
                return true;
            });
            Add("X3-atk2-a2", () => ShotLanded(15f));
            Add("X3-atk2-b", () =>
            {
                if (!Elapsed(0.12f)) return false;
                Shoot("X3-a2b", "x_x3_atk2_b.png", "camera",
                    "attackIndex=2 phase=strike attacks=" + _attacks + " m#" + _holdMonster
                    + " hp=" + MonHp(_holdMonster) + " " + StatsValues());
                return true;
            });
            Add("X3-atk2-b2", () => ShotLanded(15f));
            Add("X3-end", () =>
            {
                EndHold("x3-two-attacks");
                Drive.KV("X3-DONE", "attacks=" + _attacks + " damageEvents=" + _dmgDealt
                    + " framesPerAttack=2 (windup + strike)");
                return true;
            });
        }

        /// <summary>X9 hover cursor: park the pointer on a live monster (hover target + cursor form).</summary>
        private void BuildPlanXHoverCursor()
        {
            Add("X9-hover-settle", () =>
            {
                if (Drive.PlayerMoving())
                {
                    if (Tout("X9-hover-settle", 8f)) return true;
                    return false;
                }
                if (!Elapsed(0.6f)) return false;
                return true;
            });
            Add("X9-hover-move", () =>
            {
                var id = NearestAliveMonster();
                if (id < 0) { Note("x9hover:no-monster"); return true; }
                _pendingMonster = id;
                var mon = Drive.Monster();
                var st = mon != null ? mon.Get(id) : null;
                if (st == null) { Note("x9hover:no-state"); return true; }
                var g = new Vector2Int(st.gridX, st.gridY);
                Drive.MouseState(Drive.CellInjectPoint(g), false);
                Drive.KV("X9-HOVER-POS", "m#" + id + " cell=" + Drive.Grid(g)
                    + " name=\"" + Drive.Esc(st.name) + "\" hp=" + st.hp + "/" + st.maxHp);
                return true;
            });
            Add("X9-hover", () =>
            {
                if (!Elapsed(0.9f)) return false;
                Shoot("X9-hover", "x_x9_cursor_hover.png", "screen",
                    "probe=HoverAttackable cursorKind=" + _lastCursor
                    + " cursorChanges=" + _cursorChanges + " hover=" + _lastHover);
                return true;
            });
            Add("X9-hover-shot", () => ShotLanded(20f));
        }
    }
}
// end of x_drive.cs







