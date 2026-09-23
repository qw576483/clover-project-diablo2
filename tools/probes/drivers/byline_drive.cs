// =============================================================================
// byline_drive.cs -- ONE Play session that produces the RENDERED-pixel evidence for
//   the brand byline required by SKILL 6.9 ("by clover-engine" in small letters at the
//   bottom of the home screens).  Judging criterion = the actual rendered glyphs,
//   NOT the node's text property (SKILL 6.9 forbids the node-property substitute).
//
// What it does, in ONE chain:
//   Boot screen visible -> measure the ByLine node -> capture the composited screen
//   -> inject Space -> Main menu visible -> measure the ByLine node -> capture again
//   -> done marker.
//
// Why the driver captures instead of `capture_game_view`:
//   `capture_game_view --save_path` resolves against the AUTHORING ROOT (Assets/), and a png
//   written under Assets/ while Play runs forces StopAssetImportingV2(ForceDomainReload) --
//   the session then half-initialises.  So the driver writes into <repo>/.ai-tmp/test/raw/
//   (never imported) via ScreenCapture.CaptureScreenshot == the composited backbuffer,
//   i.e. the same pixels `capture_game_view --source screen` returns.
//
// The ByLine node measurement logs, for each frame-rendered glyph child:
//   * the child's NAME (= "G" + (int)char in D2Label.PlaceLatinGlyph / PlaceChiGlyph)
//   * which atlas painted it: a RawImage ==> the ORIGINAL chi bitmap tier (has real lowercase),
//     an Image whose sprite name is "font<N>_<code>" ==> the latin tier
//   * the node's own screen-space rect, so the offline crop is aimed by runtime numbers and
//     not by a guessed constant.
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Byline
{
    /// <summary>Shared helpers (reflection into the project's internal types).</summary>
    public static class Probe
    {
        internal const string Tag = "BYLINE";
        internal const string ByLineText = "by clover-engine";

        private static string _rawDir = string.Empty;
        private static string _done = string.Empty;

        private static string _pendingFile = string.Empty;
        private static int _pendingIndex;

        internal static string RawDir { get { return _rawDir; } }
        internal static string DonePath { get { return _done; } }
        internal static bool ShotPending { get { return _pendingFile.Length > 0; } }

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

        internal static void Paths(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0) _rawDir = parts[0];
            if (parts.Length > 1) _done = parts[1];
            Log("PATHS rawDir=" + _rawDir + " done=" + _done);
        }

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

        // ---- capture -----------------------------------------------------------------------
        internal static void BeginShot(int index, string name)
        {
            _pendingIndex = index;
            _pendingFile = _rawDir + "/" + name;
            // a stale file from an earlier run must never pass for this one
            try { if (File.Exists(_pendingFile)) File.Delete(_pendingFile); } catch { }
            Log("SHOT-REQUEST n=" + index + " name=" + name + " path=" + _pendingFile);
        }

        internal static void FlushShot()
        {
            if (_pendingFile.Length == 0) return;
            var file = _pendingFile;
            var index = _pendingIndex;
            _pendingFile = string.Empty;
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                // fires at the END of the frame
                ScreenCapture.CaptureScreenshot(file);
                Log("SHOT-ISSUED n=" + index + " file=" + file + " frame=" + Time.frameCount
                    + " t=" + Time.time.ToString("0.00"));
            }
            catch (Exception ex)
            {
                Warn("SHOT-FAIL n=" + index + " " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static bool ShotReady(string name, out long bytes)
        {
            var f = _rawDir + "/" + name;
            bytes = -1;
            try { if (File.Exists(f)) bytes = new FileInfo(f).Length; } catch { }
            return bytes > 0;
        }

        // ---- reflection --------------------------------------------------------------------
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

        internal static object Field(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }

        internal static string Fmt(object o) { return o == null ? "(null)" : o.ToString(); }

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

        // ---- input -------------------------------------------------------------------------
        private static Keyboard RequireKeyboard()
        {
            var kb = Keyboard.current;
            if (kb != null) return kb;
            kb = InputSystem.AddDevice<Keyboard>();
            Log("WARN keyboard-missing added=" + (kb != null ? kb.name : "(null)"));
            return kb;
        }

        internal static void KeyDown(string name)
        {
            var kb = RequireKeyboard();
            if (kb == null) { Warn("KEYDOWN no-keyboard"); return; }
            Key key;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "space": key = Key.Space; break;
                case "escape": key = Key.Escape; break;
                case "enter": key = Key.Enter; break;
                default: Warn("KEYDOWN unknown-key=" + name); return;
            }
            InputSystem.QueueStateEvent(kb, new KeyboardState(key));
            Log("KEYDOWN key=" + key + " keyboard=" + kb.name);
        }

        internal static void KeyUp()
        {
            var kb = RequireKeyboard();
            if (kb == null) return;
            InputSystem.QueueStateEvent(kb, new KeyboardState());
            Log("KEYUP keyboard=" + kb.name);
        }

        /// <summary>Editor/play setup so the loop keeps ticking while the editor is unfocused.</summary>
        internal static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;

            var st = InputSystem.settings;
            st.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;

            var added = 0;
            var kb = Keyboard.current;
            if (kb == null) { kb = InputSystem.AddDevice<Keyboard>(); added = 1; }
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();

            var line = "CFG runInBg=" + (Application.runInBackground ? 1 : 0)
                       + " vSync=" + QualitySettings.vSyncCount
                       + " targetFps=" + Application.targetFrameRate
                       + " focused=" + (Application.isFocused ? 1 : 0)
                       + " screen=" + Screen.width + "x" + Screen.height
                       + " keyboard=" + (kb != null ? kb.name : "(null)")
                       + " keyboardAdded=" + added
                       + " gameRunning=" + (Game.IsRunning ? 1 : 0)
                       + " fsm=" + (Game.Fsm != null ? Game.Fsm.Current : "(null)")
                       + " scene=" + (Game.Scene != null ? Game.Scene.CurrentScene : "(null)");
            Log(line);
            return line;
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Cfg() { return Probe.Cfg(); }
        public static string Paths(string spec) { Probe.Paths(spec); return "PATHS-OK"; }
    }

    /// <summary>Installer. spec = "&lt;tag&gt;|&lt;raw dir&gt;|&lt;done&gt;".</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("BylineEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }
    }

    /// <summary>
    /// One Play, four steps: boot tile -> space -> menu tile -> measure -> done.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private const float BootSettle = 2.0f;
        private const float MenuSettle = 2.0f;
        // the chi atlas (font24_chi = 1.5 MB png) is loaded asynchronously; until it is ready the label
        // draws NOTHING (BuildBitmap returns false instead of showing a half-built glyph layer).  The
        // driver therefore waits for the glyph nodes to exist before it measures/captures, with a
        // bounded extra budget, so a slow load cannot silently produce an empty-byline tile.
        private const float GlyphWaitExtra = 10.0f;

        private string _tag = "b1";
        private string _rawDir = string.Empty;
        private int _step;
        private float _stepAt;
        private bool _done;
        private bool _menuSeen;
        private int _shots;
        private string _bootTile = "byline_01_boot.png";
        private string _menuTile = "byline_02_menu.png";

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _tag = parts[0];
            _rawDir = parts.Length > 1 ? parts[1] : string.Empty;
            var donePath = parts.Length > 2 ? parts[2] : string.Empty;
            Probe.Paths(_rawDir + "|" + donePath);
            _step = 0;
            _stepAt = Time.unscaledTime;
            Probe.Log("DRIVER-INIT tag=" + _tag + " frame=" + Time.frameCount
                      + " rawDir=" + _rawDir + " screen=" + Screen.width + "x" + Screen.height);
        }

        private void Update()
        {
            if (_done) return;
            try { Step(); }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private void LateUpdate()
        {
            try { Probe.FlushShot(); }
            catch (Exception ex) { Probe.Warn("FLUSH-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool BootOpen()
        {
            return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.BootPanel>();
        }

        private static bool MenuOpen()
        {
            return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>();
        }

        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static string SceneName() { return Game.Scene != null ? Game.Scene.CurrentScene : "(null)"; }
        private bool Elapsed(float s) { return Time.unscaledTime - _stepAt >= s; }
        private void Next() { _step++; _stepAt = Time.unscaledTime; }

        private bool WaitShot(int index, string name, float budget)
        {
            long bytes;
            if (Probe.ShotReady(name, out bytes))
            {
                _shots++;
                Probe.Log("SHOT-OK n=" + index + " name=" + name + " bytes=" + bytes + " path=" + _rawDir + "/" + name);
                Next();
                return true;
            }
            if (Elapsed(budget))
            {
                Probe.KV("SHOT-TIMEOUT", "n=" + index + " name=" + name + " waited=" + budget.ToString("0.0") + "s");
                Next();
                return true;
            }
            return false;
        }

        // ================================================================ byline measure ====
        private static List<Text> AllTexts()
        {
            var list = new List<Text>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Text>())
            {
                if (t != null && t.gameObject != null) list.Add(t);
            }
            return list;
        }

        private static Type MirrorType() { return Probe.FindType("Diablo2.UI.D2TextMirror"); }

        /// <summary>
        /// Log the ByLine node + every rendered glyph child: the child's NAME encodes the code point
        /// that was actually placed ("G" + (int)char, see D2Label.PlaceLatinGlyph/PlaceChiGlyph) and the
        /// component type tells which ATLAS painted it (RawImage = original chi tier, Image = latin tier).
        /// </summary>
        /// <summary>How many glyph nodes the ByLine label has drawn (0 = its atlas is still in flight).</summary>
        private int CountByLineGlyphs()
        {
            foreach (var t in AllTexts())
            {
                if (t.text == null || t.text != Probe.ByLineText) continue;
                var rt = t.transform as RectTransform;
                if (rt == null) return 0;
                var n = 0;
                foreach (Transform layer in rt)
                {
                    foreach (Transform g in layer)
                    {
                        n++;
                    }
                }
                return n;
            }
            return -1;
        }

        private void MeasureByLine(string where)
        {
            Text found = null;
            foreach (var t in AllTexts())
            {
                if (t.text != null && t.text == Probe.ByLineText) { found = t; break; }
            }
            if (found == null)
            {
                Probe.KV("BYLINE", "where=" + where + " present=0 wanted=\"" + Probe.ByLineText + "\"");
                return;
            }

            var rt = found.transform as RectTransform;
            var mirror = MirrorType();
            var m = mirror != null ? found.GetComponent(mirror) : null;
            var tier = m != null ? Probe.Fmt(Probe.Field(m, "Font")) : "(no-mirror)";

            // corner screen coordinates -> the offline crop is aimed by runtime numbers
            var corners = "(no-rt)";
            if (rt != null)
            {
                var c = new Vector3[4];
                rt.GetWorldCorners(c);
                var sb = new StringBuilder();
                for (var i = 0; i < 4; i++)
                {
                    var sp = RectTransformUtility.WorldToScreenPoint(null, c[i]);
                    if (i > 0) sb.Append(' ');
                    sb.Append("(").Append(sp.x.ToString("0.#")).Append(",").Append(sp.y.ToString("0.#")).Append(")");
                }
                corners = sb.ToString();
            }

            // painted glyph children
            var kids = new List<Transform>();
            if (rt != null)
            {
                foreach (Transform ch in rt)
                {
                    kids.Add(ch);
                }
            }

            var glyphSb = new StringBuilder();
            var codeSb = new StringBuilder();
            var latinCount = 0;
            var chiCount = 0;
            var plainCount = 0;
            var glyphsLogged = 0;
            foreach (var layer in kids)
            {
                foreach (Transform g in layer)
                {
                    var ri = g.GetComponent<RawImage>();
                    var im = g.GetComponent<Image>();
                    var kind = ri != null ? "chiRawImage(uv)" : (im != null ? "latinImage" : "plain");
                    if (ri != null) chiCount++; else if (im != null) latinCount++; else plainCount++;
                    var sprite = im != null && im.sprite != null ? im.sprite.name : "(none)";
                    if (glyphsLogged < 40)
                    {
                        if (glyphSb.Length > 0) glyphSb.Append(' ');
                        glyphSb.Append(g.name).Append('/').Append(kind).Append('/').Append(sprite);
                        glyphsLogged++;
                    }
                    // node name is "G<code>" for both atlases (see D2Label helpers)
                    if (g.name.Length > 1 && g.name[0] == 'G')
                    {
                        int code;
                        if (int.TryParse(g.name.Substring(1), out code))
                            codeSb.Append((char)code);
                        else
                            codeSb.Append('?');
                    }
                }
            }

            Probe.KV("BYLINE", "where=" + where + " present=1 path=" + Probe.PathOf(found.transform)
                     + " text=\"" + found.text + "\" tier=" + tier + " fontSize=" + found.fontSize
                     + " color=" + found.color.r.ToString("0.000") + "," + found.color.g.ToString("0.000")
                     + "," + found.color.b.ToString("0.000")
                     + " active=" + (found.gameObject.activeInHierarchy ? 1 : 0)
                     + " sizeDelta=" + (rt != null ? rt.sizeDelta.x.ToString("0.#") + "x" + rt.sizeDelta.y.ToString("0.#") : "-")
                     + " anchoredPos=" + (rt != null ? rt.anchoredPosition.x.ToString("0.#") + "," + rt.anchoredPosition.y.ToString("0.#") : "-")
                     + " screenCorners=" + corners
                     + " screen=" + Screen.width + "x" + Screen.height
                     + " mirror=" + (m != null ? 1 : 0));
            Probe.KV("BYLINE-LAYERS", "where=" + where + " childLayers=" + kids.Count
                     + " glyphNodes=" + (latinCount + chiCount + plainCount)
                     + " latinImage=" + latinCount + " chiRawImage=" + chiCount + " plain=" + plainCount);
            Probe.KV("BYLINE-GLYPHS", "where=" + where + " nodes=[" + glyphSb.ToString() + "]");
            Probe.KV("BYLINE-PLACED", "where=" + where + " codePointsFromNodeNames=\"" + codeSb.ToString()
                     + "\" (node name = \"G\"+code, D2Label.PlaceLatinGlyph:960 / PlaceChiGlyph:944)");
        }

        // ================================================================ tour ==============
        private void Step()
        {
            switch (_step)
            {
                case 0:
                    if (!BootOpen()) { TimeoutStep("boot", 40f); return; }
                    if (!Elapsed(BootSettle)) return;
                    if (CountByLineGlyphs() == 0)
                    {
                        if (!Elapsed(BootSettle + GlyphWaitExtra)) return;   // byline atlas still loading
                        Probe.Warn("BOOT byline still has 0 glyph nodes after "
                                   + (BootSettle + GlyphWaitExtra).ToString("0.0") + "s");
                    }
                    Probe.KV("GLYPHNODES", "where=boot count=" + CountByLineGlyphs());
                    Probe.KV("SCREEN", "where=boot scene=" + SceneName() + " fsm=" + Fsm()
                             + " screen=" + Screen.width + "x" + Screen.height
                             + " focused=" + (Application.isFocused ? 1 : 0)
                             + " runInBackground=" + (Application.runInBackground ? 1 : 0)
                             + " device=" + SystemInfo.graphicsDeviceName
                             + " type=" + SystemInfo.graphicsDeviceType);
                    MeasureByLine("boot");
                    Probe.BeginShot(1, _bootTile);
                    Next();
                    return;
                case 1:
                    WaitShot(1, _bootTile, 20f);
                    return;
                case 2:
                    if (!Elapsed(1.2f)) return;
                    if (BootOpen()) { Probe.KeyDown("space"); Probe.KeyUp(); }
                    else Probe.Warn("BOOT already closed before the Space injection");
                    Next();
                    return;
                case 3:
                    if (!MenuOpen() || SceneName() != "Menu") { TimeoutStep("mainmenu", 40f); return; }
                    if (!_menuSeen) { _menuSeen = true; _stepAt = Time.unscaledTime; }
                    if (!Elapsed(MenuSettle)) return;
                    if (CountByLineGlyphs() == 0)
                    {
                        if (!Elapsed(MenuSettle + GlyphWaitExtra)) return;
                        Probe.Warn("MENU byline still has 0 glyph nodes after "
                                   + (MenuSettle + GlyphWaitExtra).ToString("0.0") + "s");
                    }
                    Probe.KV("GLYPHNODES", "where=menu count=" + CountByLineGlyphs());
                    Probe.KV("SCREEN", "where=menu scene=" + SceneName() + " fsm=" + Fsm());
                    MeasureByLine("menu");
                    Probe.BeginShot(2, _menuTile);
                    Next();
                    return;
                case 4:
                    WaitShot(2, _menuTile, 20f);
                    return;
                default:
                    Finish("end");
                    return;
            }
        }

        private void TimeoutStep(string step, float seconds)
        {
            if (!Elapsed(seconds)) return;
            Probe.Log("STEP-TIMEOUT step=" + step + " elapsed=" + (Time.unscaledTime - _stepAt).ToString("0.0")
                      + " fsm=" + Fsm() + " scene=" + SceneName());
            Finish("timeout-" + step);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            Probe.KV("FINISH", "why=" + why + " step=" + _step + " shots=" + _shots
                     + " device=" + SystemInfo.graphicsDeviceName
                     + " screen=" + Screen.width + "x" + Screen.height);
            Probe.Log("TOUR-DONE why=" + why + " steps=" + _step + " shots=" + _shots);
            Probe.WriteFile(Probe.DonePath,
                "TOUR-DONE tag=" + _tag + " why=" + why + " shots=" + _shots
                + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
        }

        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
