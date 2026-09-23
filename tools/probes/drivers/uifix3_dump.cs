// =============================================================================
// uifix3_dump.cs -- ui-fix3 slice: live node-level evidence for the three UI
// defects seen in pv_* screenshots (white waypoint panel box, black block over
// the first destination label, gray box left after dragging an item out).
//
// Install this DURING the same Play session that runs the S2 tour (s2_drive.cs).
// It is a passive monitor:
//   - when WaypointPanel is open  -> capture uifix3_wp_panel.png + dump every
//     Graphic under it (node path, type, sprite/texture, color, size, uvRect)
//     to .ai-tmp/test/uifix3_wp_dump.txt
//   - when InventoryPanel's DragGhost goes active -> capture uifix3_drag_mid.png
//     and dump the ghost/tooltip state
//   - right after the ghost goes inactive again -> capture uifix3_drag_after.png
//     and dump every active Graphic under InventoryPanel (residue hunt)
//
// All rows also go to the runtime log tagged [UF3] so the frozen evidence file
// carries them.  ASCII only.
// =============================================================================
using System;
using System.IO;
using System.Text;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace UF3
{
    public static class Dump
    {
        public static string Install(string spec)
        {
            var go = new GameObject("UiFix3DumpDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Monitor>();
            drv.Init(string.IsNullOrEmpty(spec) ? "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp" : spec);
            Debug.Log("[UF3] DUMP-INSTALL spec=" + spec + " gameRunning=" + (CloverEngine.Game.IsRunning ? 1 : 0));
            return "DUMP-INSTALLED";
        }
    }

    public class Monitor : MonoBehaviour
    {
        private string _root;
        private bool _wpDumped;
        private bool _ghostMidDone;
        private bool _invSeenLogged;
        private bool _ghostWasActive;
        private bool _afterDone;
        private float _next;

        public void Init(string root) { _root = root; }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.35f;

            if (!_wpDumped) TryWaypoint();
            if (!_afterDone) TryGhost();
        }

        private void TryWaypoint()
        {
            var panels = Resources.FindObjectsOfTypeAll<WaypointPanel>();
            WaypointPanel p = null;
            foreach (var x in panels) { if (x != null && x.gameObject.activeInHierarchy) { p = x; break; } }
            if (p == null) return;

            StartCoroutine(CaptureThen(ScreenCapturePath("uifix3_wp_panel.png"), delegate {
                DumpTree(p.transform, "WaypointPanel",
                    Path.Combine(_root, @"test\uifix3_wp_dump.txt"));
                Say("WP-DUMP-WRITTEN nodes-see-file");
                _wpDumped = true;
            }));
        }

        private void TryGhost()
        {
            var panels = Resources.FindObjectsOfTypeAll<InventoryPanel>();
            InventoryPanel p = null;
            foreach (var x in panels) { if (x != null && x.gameObject.activeInHierarchy) { p = x; break; } }
            if (p == null) return;

            var ghost = p.transform.Find("DragGhost");
            if (!_invSeenLogged)
            {
                _invSeenLogged = true;
                Say("INV-SEEN ghostNode=" + (ghost != null ? 1 : 0));
            }
            if (ghost == null) { Say("GHOST-MISSING (no DragGhost node under InventoryPanel)"); _afterDone = true; return; }

            var active = ghost.gameObject.activeInHierarchy;
            if (active && !_ghostMidDone)
            {
                _ghostWasActive = true;
                StartCoroutine(CaptureThen(ScreenCapturePath("uifix3_drag_mid.png"), delegate {
                    var sr = ghost.GetComponent<Image>();
                    Say("GHOST-MID active=1 sprite=" + SpriteName(sr) + " color=" + Col(sr) +
                        " size=" + Size(ghost as RectTransform) + " tooltipActive=" + TooltipActive(p));
                    DumpTree(p.transform, "InventoryPanel-during-drag",
                        Path.Combine(_root, @"test\uifix3_inv_dump.txt"));
                    _ghostMidDone = true;
                }));
            }
            else if (!active && _ghostWasActive && !_ghostMidDone)
            {
                _ghostWasActive = false;    // drag ended before we caught it mid-flight
            }
            else if (!active && _ghostWasActive && _ghostMidDone && !_afterDone)
            {
                StartCoroutine(CaptureThen(ScreenCapturePath("uifix3_drag_after.png"), delegate {
                    Say("GHOST-AFTER active=0");
                    DumpTree(p.transform, "InventoryPanel-after-drop",
                        Path.Combine(_root, @"test\uifix3_inv_dump.txt"));
                    _afterDone = true;
                }));
            }
        }

        private static bool TooltipActive(InventoryPanel p)
        {
            var t = p.transform.Find("ItemTooltip");
            return t != null && t.gameObject.activeInHierarchy;
        }

        // ---- helpers ---------------------------------------------------------
        private string ScreenCapturePath(string name)
        {
            return Path.Combine(_root, "screenshots") + Path.DirectorySeparatorChar + name;
        }

        private System.Collections.IEnumerator CaptureThen(string path, Action done)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                File.WriteAllBytes(path, png);
                UnityEngine.Object.Destroy(tex);
                Say("SHOT " + path + " bytes=" + (png != null ? png.Length : 0));
            }
            catch (Exception ex) { Say("SHOT-FAIL " + path + " " + ex.GetType().Name + ": " + ex.Message); }
            if (done != null) done();
        }

        private static void Say(string msg)
        {
            var l = CloverEngine.Game.Logger;
            if (l != null) l.Info("UF3", msg);
            else Debug.Log("[UF3] " + msg);
        }

        private static string SpriteName(Image i)
        {
            if (i == null) return "noImage";
            return i.sprite != null ? i.sprite.name : "NULL";
        }

        private static string TextureName(RawImage r)
        {
            if (r == null) return "noRaw";
            return r.texture != null ? r.texture.name : "NULL";
        }

        private static string Col(Graphic g)
        {
            if (g == null) return "-";
            var c = g.color;
            return c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000") + "," + c.a.ToString("0.000");
        }

        private static string Size(RectTransform rt)
        {
            return rt == null ? "-" : rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#");
        }

        private static void DumpTree(Transform root, string label, string file)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== " + label + " @ " + DateTime.UtcNow.ToString("o"));
            Walk(root, 0, sb);
            try { File.WriteAllText(file, sb.ToString()); }
            catch (Exception ex) { Say("DUMP-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static void Walk(Transform t, int depth, StringBuilder sb)
        {
            var pad = new string(' ', depth * 2);
            var rt = t as RectTransform;
            var line = pad + t.name + " active=" + (t.gameObject.activeInHierarchy ? 1 : 0) + " size=" + Size(rt);
            var img = t.GetComponent<Image>();
            var raw = t.GetComponent<RawImage>();
            var txt = t.GetComponent<Text>();
            if (img != null) line += " | Image sprite=" + SpriteName(img) + " color=" + Col(img) + " raycast=" + img.raycastTarget;
            if (raw != null) line += " | RawImage tex=" + TextureName(raw) + " uv=" + raw.uvRect + " color=" + Col(raw);
            if (txt != null) line += " | Text '" + txt.text + "' color=" + Col(txt) + " fontNull=" + (txt.font == null ? 1 : 0) + " enabled=" + (txt.enabled ? 1 : 0);
            sb.AppendLine(line);
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }
    }
}
