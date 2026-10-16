// =============================================================================
// artinv_evidence.cs -- ONE Play session that opens the two panels under audit
//   (InventoryPanel / QuestLogPanel) and reads them back from the live render:
//     * one screenshot per panel (ScreenCapture) + the on-screen rect of the
//       panel root, so the picture can be tied to real canvas pixels;
//     * a node dump (name / active / anchoredPosition / sizeDelta / sprite /
//       alpha / text) three levels deep -- the same numbers the eye sees;
//     * the production Ui log lines the two panels emit while opening.
//
//   Injected through tools/probes/drivers/artinv_run.ps1:
//     unity command run_script --file artinv_evidence.cs --entry ArTInv.ArtInv.Install --args ["spec"]
//   spec = "<charName>|<donePath>|<shotDir>"
// =============================================================================
namespace ArTInv
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
        internal const string Tag = "ARTINV";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[ARTINV] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string m) { Log("WARN " + m); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("ARTINV-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var dir = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_done, "ARTINV-DONE " + DateTime.Now.ToString("o"));
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
    public static class ArtInv
    {
        public static string Install(string spec)
        {
            var go = new GameObject("ArtInvDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("ARTINV-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain: load the save, open InventoryPanel, read it, open QuestLogPanel, read it.</summary>
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
            L.Log("ARTINV-INIT char=" + _name + " shots=" + _shotDir);
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

        private static Vector3 ScreenOfUi(RectTransform rt, Canvas canvas)
        {
            Camera cam = null;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(cam, rt.position);
        }

        /// <summary>Walk the live UI tree and print what the player sees (numbers, not prose).</summary>
        private static void Dump(string tag, Component panel, int maxDepth)
        {
            if (panel == null) { L.Warn(tag + " panel-null"); return; }
            var root = panel.transform;
            var rt0 = root as RectTransform;
            var canvas = panel.GetComponentInParent<Canvas>();
            var sp = ScreenOfUi(rt0, canvas);
            var canvasRt = canvas != null ? canvas.transform as RectTransform : null;
            L.KV(tag + "-ROOT",
                "name=" + root.name
                + " active=" + root.gameObject.activeSelf
                + " anchored=" + L.F(rt0.anchoredPosition.x) + "," + L.F(rt0.anchoredPosition.y)
                + " size=" + L.F(rt0.sizeDelta.x) + "x" + L.F(rt0.sizeDelta.y)
                + " pivot=" + L.F(rt0.pivot.x) + "," + L.F(rt0.pivot.y)
                + " screen=" + L.F(sp.x) + "," + L.F(sp.y)
                + " canvas=" + (canvas != null ? canvas.renderMode.ToString() : "(none)")
                + " canvasSize=" + (canvasRt != null
                    ? L.F(canvasRt.sizeDelta.x) + "x" + L.F(canvasRt.sizeDelta.y)
                    : "(none)"));
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
                sb.Append(tag).Append("-NODE ");
                sb.Append("d").Append(depth + 1).Append(' ');
                sb.Append(PathOf(root, c));
                sb.Append(" active=").Append(c.gameObject.activeSelf ? 1 : 0);
                if (rt != null)
                {
                    sb.Append(" pos=").Append(L.F(rt.anchoredPosition.x)).Append(',').Append(L.F(rt.anchoredPosition.y));
                    sb.Append(" size=").Append(L.F(rt.sizeDelta.x)).Append('x').Append(L.F(rt.sizeDelta.y));
                }
                if (img != null)
                {
                    sb.Append(" sprite=").Append(img.sprite != null ? img.sprite.name : "(null)");
                    sb.Append(" a=").Append(L.F(img.color.a));
                    sb.Append(" ray=").Append(img.raycastTarget ? 1 : 0);
                }
                if (txt != null)
                {
                    sb.Append(" text=\"").Append((txt.text ?? string.Empty).Replace("\n", "\\n")).Append('"');
                }
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
            L.KV("ARTINV-SHOTS", _shotCount.ToString());
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

                // 4) open the inventory through the same entry the I key uses
                case 4:
                    if (Elapsed(0.5f))
                    {
                        L.Log("OPEN InventoryPanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "InventoryPanel");
                        Next();
                    }
                    break;

                // 5) read the inventory panel
                case 5:
                    if (Open<Diablo2.UI.InventoryPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.6f)) break;
                        Dump("INV", FindPanel("Diablo2.UI.InventoryPanel"), 3);
                        Snap("artinv_inventory");
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 6) screenshot has to land on disk before anything else happens
                case 6:
                    if (Elapsed(1.5f))
                    {
                        L.Emit(Events.PanelToggleRequest, "InventoryPanel");
                        Next();
                    }
                    break;

                // 7) open the quest log through the same entry the Q key uses
                case 7:
                    if (Elapsed(1.0f))
                    {
                        L.Log("OPEN QuestLogPanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "QuestLogPanel");
                        Next();
                    }
                    break;

                // 8) read the quest log panel
                case 8:
                    if (Open<Diablo2.UI.QuestLogPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.6f)) break;
                        Dump("QUEST", FindPanel("Diablo2.UI.QuestLogPanel"), 3);
                        Snap("artinv_quest");
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 9) done
                case 9:
                    if (Elapsed(2.0f)) Finish();
                    break;

                default:
                    Finish();
                    break;
            }
        }
    }
}
