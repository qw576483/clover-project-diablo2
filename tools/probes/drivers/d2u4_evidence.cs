// =============================================================================
// d2u4_evidence.cs -- ONE Play session that opens the SKILL TREE panel and reads
//   the description window back from the live render:
//     * screenshot of the panel (ScreenCapture) + the panel root's on-screen rect;
//     * the SkillDesc label's live text / font size / wrap mode / D2Label line count
//       after forcing ShowSkill(i) for the first few skills (reflection: the panel
//       method is private and the probe must not re-implement the composition);
//     * whether the bitmap font family is degraded (`D2Text._bitmapUnavailable`)
//       and whether font8's chi slot is ready -- the run-time truth for the newly
//       exported font8 atlas/map;
//     * a shallow node dump of the panel (names / rects / sprites / text).
//
//   Injected through tools/probes/drivers/d2u4_run.ps1:
//     unity command run_script --file d2u4_evidence.cs --entry D2U4.SkillDesc.Install --args ["spec"]
//   spec = "<charName>|<donePath>|<shotDir>"
// =============================================================================
namespace D2U4
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using CloverEngine;
    using Diablo2.Core;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>Log / screenshot / reflection helpers (self-contained probe).</summary>
    public static class L
    {
        internal const string Tag = "D2U4";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[D2U4] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string m) { Log("WARN " + m); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("D2U4-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var dir = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_done, "D2U4-DONE " + DateTime.Now.ToString("o"));
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

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

        internal static string F(float v) { return v.ToString("0.##"); }

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

    /// <summary>Compile sentinel entry for the runner (run_script --entry).</summary>
    public static class Probe
    {
        public static string Ping()
        {
            return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000");
        }
    }

    /// <summary>Callable entry for the runner.</summary>
    public static class SkillDesc
    {
        public static string Install(string spec)
        {
            var go = new GameObject("D2U4Driver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("D2U4-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain: load the save, open SkillTreePanel, force ShowSkill(), read it back, shoot.</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private int _shotCount;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            L.Log("D2U4-INIT char=" + _name + " shots=" + _shotDir);
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
            _shotCount++;
            L.KV("SNAP " + leaf, L.Shot(_shotDir + "/" + leaf + ".png"));
        }

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

        /// <summary>Read a field by reflection and cast to T (missing / wrong type =&gt; null).</summary>
        private static T Field<T>(object obj, string name) where T : class
        {
            if (obj == null) return null;
            var f = obj.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null ? f.GetValue(obj) as T : null;
        }

        private static Vector3 ScreenOfUi(RectTransform rt, Canvas canvas)
        {
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        // ---- the reading that this session exists for -----------------------------
        private void ReadDescWindow(Component panel)
        {
            if (panel == null) { L.Warn("DESC panel-null"); return; }
            var pt = panel.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            var fx = pt.GetField("_desc", F);
            var desc = fx != null ? fx.GetValue(panel) as Text : null;
            var fp = pt.GetField("_points", F);
            var points = fp != null ? fp.GetValue(panel) as Text : null;

            var mShow = pt.GetMethod("ShowSkill", F);
            if (mShow == null) { L.Warn("DESC ShowSkill-not-found"); return; }

            var ft = pt.GetField("_tree", F);
            var tree = ft != null ? ft.GetValue(panel) : null;
            var nSkills = 0;
            if (tree != null)
            {
                var fsk = tree.GetType().GetField("skills", F);
                var list = fsk != null ? fsk.GetValue(tree) as IList : null;
                if (list != null) nSkills = list.Count;
            }
            L.KV("DESC-ENV", "skills=" + nSkills + " points=\"" + OneLine(points != null ? points.text : "(null)")
                 + "\" tierPoints[" + TierOf(points) + "]"
                 + " font8Ready=" + Font8Ready() + " bitmapUnavailable=" + BitmapUnavailable());

            var idx = new int[] { 0, 1, 2, 3 };
            for (var i = 0; i < idx.Length; i++)
            {
                if (idx[i] >= nSkills) break;
                try { mShow.Invoke(panel, new object[] { idx[i] }); }
                catch (Exception ex) { L.Warn("DESC ShowSkill(" + idx[i] + ") ex=" + ex.GetType().Name + ": " + ex.Message); continue; }

                var text = desc != null ? desc.text : "(null)";
                L.KV("DESC[" + idx[i] + "]",
                    "fontSize=" + (desc != null ? desc.fontSize.ToString() : "?")
                    + " wrap=" + (desc != null ? desc.horizontalOverflow.ToString() : "?")
                    + " size=" + (desc != null ? L.F(desc.rectTransform.sizeDelta.x) + "x" + L.F(desc.rectTransform.sizeDelta.y) : "?")
                    + " textLines=" + CountLines(text)
                    + " labelLines=" + LabelLineCount(desc)
                    + " tier[" + TierOf(desc) + "]"
                    + " text=\"" + OneLine(text) + "\"");
                L.KV("DESC[" + idx[i] + "]-RAW", (text ?? string.Empty).Replace("\n", "\\n"));
            }
        }

        /// <summary>
        /// The font tier a label actually renders from, plus that tier's atlas geometry:
        /// font = D2TextMirror.Font, cell = D2Text.ChiCellW/H, atlas = ChiAtlasOf(font).Atlas,
        /// ready = D2Text.ChiReady. Every number comes from the production functions --
        /// the probe recomputes nothing.
        /// </summary>
        private static string TierOf(Text t)
        {
            if (t == null) return "no-text";
            var mt = L.FindType("Diablo2.UI.D2TextMirror");
            var dt = L.FindType("Diablo2.UI.D2Text");
            if (mt == null || dt == null) return "no-types";
            var comp = t.gameObject.GetComponent(mt);
            if (comp == null) return "no-mirror";

            var pf = mt.GetProperty("Font", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var ff = mt.GetField("Font", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var font = pf != null ? pf.GetValue(comp) : (ff != null ? ff.GetValue(comp) : null);
            if (font == null) return "no-font-field";
            var ft = font.GetType();

            var mCellW = dt.GetMethod("ChiCellW", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mCellH = dt.GetMethod("ChiCellH", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mReady = dt.GetMethod("ChiReady", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mAtlas = dt.GetMethod("ChiAtlasOf", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var o = new object[] { font };
            var cw = mCellW != null ? mCellW.Invoke(null, o).ToString() : "?";
            var ch = mCellH != null ? mCellH.Invoke(null, o).ToString() : "?";
            var rd = mReady != null ? mReady.Invoke(null, o).ToString() : "?";
            var atlas = "n/a";
            if (mAtlas != null)
            {
                var slot = mAtlas.Invoke(null, o);
                if (slot != null)
                {
                    var fa = slot.GetType().GetField("Atlas", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    var tex = fa != null ? fa.GetValue(slot) as Texture2D : null;
                    atlas = tex != null ? tex.width + "x" + tex.height : "(null)";
                }
            }
            return "font=" + ft.Name + " cell=" + cw + "x" + ch + " atlas=" + atlas + " ready=" + rd;
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            var n = 1;
            for (var i = 0; i < s.Length; i++) if (s[i] == '\n') n++;
            return n;
        }

        private static string OneLine(string s)
        {
            if (s == null) return "(null)";
            return s.Replace("\r", string.Empty).Replace("\n", " | ");
        }

        /// <summary>Lines actually laid out by the production bitmap label (`D2Label.LineCount`), via the mirror.</summary>
        private static string LabelLineCount(Text t)
        {
            if (t == null) return "?";
            var mt = L.FindType("Diablo2.UI.D2TextMirror");
            if (mt == null) return "no-mirror-type";
            var comp = t.gameObject.GetComponent(mt);
            if (comp == null) return "no-mirror";
            var fl = mt.GetField("_label", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var lbl = fl != null ? fl.GetValue(comp) : null;
            if (lbl == null) return "no-label";
            var pl = lbl.GetType().GetProperty("LineCount");
            return pl != null ? pl.GetValue(lbl).ToString() : "no-linecount";
        }

        /// <summary>font8 chi slot ready? (production `D2Text.ChiReady`) -- the run-time truth for the new atlas.</summary>
        private static string Font8Ready()
        {
            var dt = L.FindType("Diablo2.UI.D2Text");
            if (dt == null) return "no-D2Text";
            var m = dt.GetMethod("ChiReady", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (m == null) return "no-ChiReady";
            var ft = L.FindType("Diablo2.UI.D2Text+D2Font");
            if (ft == null) return "no-D2Font";
            try
            {
                var v = Enum.Parse(ft, "Font8");
                return m.Invoke(null, new object[] { v }).ToString();
            }
            catch (Exception ex) { return "ex:" + ex.GetType().Name; }
        }

        /// <summary>`D2Text._bitmapUnavailable` (the global degradation switch) -- must be False.</summary>
        private static string BitmapUnavailable()
        {
            var dt = L.FindType("Diablo2.UI.D2Text");
            if (dt == null) return "no-D2Text";
            var f = dt.GetField("_bitmapUnavailable", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? f.GetValue(null).ToString() : "no-field";
        }

        private static void Dump(string tag, Component panel, int maxDepth)
        {
            if (panel == null) { L.Warn(tag + " panel-null"); return; }
            var root = panel.transform;
            var rt0 = root as RectTransform;
            var canvas = panel.GetComponentInParent<Canvas>();
            var sp = ScreenOfUi(rt0, canvas);
            L.KV(tag + "-ROOT",
                "name=" + root.name
                + " active=" + root.gameObject.activeSelf
                + " size=" + L.F(rt0.sizeDelta.x) + "x" + L.F(rt0.sizeDelta.y)
                + " screen=" + L.F(sp.x) + "," + L.F(sp.y)
                + " canvas=" + (canvas != null ? canvas.renderMode.ToString() : "(none)"));
            Walk(root, root, 0, maxDepth, tag);
        }

        private static void Walk(Transform root, Transform t, int depth, int maxDepth, string tag)
        {
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                var rt = c as RectTransform;
                var img = c.GetComponent<Image>();
                var txt = c.GetComponent<Text>();
                var sb = new StringBuilder();
                sb.Append(tag).Append("-NODE d").Append(depth + 1).Append(' ').Append(PathOf(root, c));
                sb.Append(" active=").Append(c.gameObject.activeSelf ? 1 : 0);
                if (rt != null)
                {
                    sb.Append(" pos=").Append(L.F(rt.anchoredPosition.x)).Append(',').Append(L.F(rt.anchoredPosition.y));
                    sb.Append(" size=").Append(L.F(rt.sizeDelta.x)).Append('x').Append(L.F(rt.sizeDelta.y));
                }
                if (img != null) sb.Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "(null)");
                if (txt != null) sb.Append(" text=\"").Append(OneLine(txt.text)).Append('"');
                L.Log(sb.ToString());
                if (depth + 1 < maxDepth) Walk(root, c, depth + 1, maxDepth, tag);
            }
        }

        private static string PathOf(Transform root, Transform t)
        {
            var sb = new StringBuilder(t.name);
            var p = t.parent;
            while (p != null && p != root)
            {
                sb.Insert(0, p.name + "/");
                p = p.parent;
            }
            return sb.ToString();
        }

        private void Watchdog(float seconds, string where)
        {
            if (Time.unscaledTime - _t0 > seconds) { L.Warn("WATCHDOG " + where + " -> finish"); Finish(); }
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            L.KV("D2U4-SHOTS", _shotCount.ToString());
            L.Done();
        }

        private void Update()
        {
            if (_done) return;
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
                // 0) boot
                case 0:
                    Watchdog(700f, "boot");
                    if (Open<Diablo2.UI.BootPanel>() || Fsm() == Events.Fsm.StateBoot)
                    {
                        if (!Elapsed(2f)) break;
                        L.Log("BOOT fsm=" + Fsm());
                        L.Emit(Events.BootDone);
                        Next();
                        break;
                    }
                    if (Elapsed(3f)) { L.Emit(Events.BootDone); _at = Time.unscaledTime; break; }
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
                    break;

                // 2) char select -> load the save
                case 2:
                    if (Open<Diablo2.UI.CharSelectPanel>() || Fsm() == Events.Fsm.StateCharSelect
                        || Fsm() == Events.Fsm.StateCharCreate)
                    {
                        if (!Elapsed(1.6f)) break;
                        L.KV("CHARSELECT", "fsm=" + Fsm() + " emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        L.Emit(Events.CharSelectRequest, _name);
                        Next();
                        break;
                    }
                    break;

                // 3) stage reached
                case 3:
                    if (Open<Diablo2.UI.HudPanel>() && Fsm() == Events.Fsm.StateStage)
                    {
                        if (!Elapsed(2.5f)) break;
                        L.KV("STAGE", "fsm=" + Fsm() + " hud=1");
                        Next();
                        break;
                    }
                    break;

                // 4) open the skill tree through the same entry the T key uses
                case 4:
                    if (Elapsed(0.5f))
                    {
                        L.Log("OPEN SkillTreePanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "SkillTreePanel");
                        Next();
                    }
                    break;

                // 5) wait until it is up, then read the description window back
                case 5:
                    if (Open<Diablo2.UI.SkillTreePanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(2.0f)) break;
                        var panel = FindPanel("Diablo2.UI.SkillTreePanel");
                        L.KV("PANEL", panel != null ? "found " + panel.GetType().FullName : "NOT-FOUND");
                        ReadDescWindow(panel);
                        Dump("SKILL", panel, 2);
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 6) screenshot (one frame later so the label glyphs are in the frame)
                case 6:
                    if (Elapsed(1.2f)) { Snap("d2u4_skilltree"); _at = Time.unscaledTime; Next(); }
                    break;

                // 7) close the skill tree, then open the quest log -- a 20-canvas-px text panel:
                //    its tier must NOT have moved (the new FontFor font8 tier is opt-in)
                case 7:
                    if (Elapsed(1.0f))
                    {
                        L.Log("CLOSE SkillTreePanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "SkillTreePanel");
                        Next();
                    }
                    break;

                // 8) open the quest log through the same entry the Q key uses
                case 8:
                    if (Elapsed(1.2f))
                    {
                        L.Log("OPEN QuestLogPanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "QuestLogPanel");
                        Next();
                    }
                    break;

                // 9) read the quest-log text label (fontSize / tier / atlas) and dump the panel
                case 9:
                    if (Open<Diablo2.UI.QuestLogPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(2.0f)) break;
                        var qp = FindPanel("Diablo2.UI.QuestLogPanel");
                        L.KV("QUEST-PANEL", qp != null ? "found " + qp.GetType().FullName : "NOT-FOUND");
                        var qt = Field<Text>(qp, "_text");
                        L.KV("QUEST-TEXT", "fontSize=" + (qt != null ? qt.fontSize.ToString() : "?")
                            + " tier[" + TierOf(qt) + "]"
                            + " text=\"" + OneLine(qt != null ? qt.text : "(null)") + "\"");
                        Dump("QUEST", qp, 2);
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 10) quest-log screenshot, then finish
                case 10:
                    if (Elapsed(1.2f)) { Snap("d2u4_questlog"); _at = Time.unscaledTime; Next(); }
                    break;

                case 11:
                    if (Elapsed(2.0f)) Finish();
                    break;

                default:
                    Finish();
                    break;
            }
        }
    }
}
