// =============================================================================
// uifix4_dump.cs -- ui-fix4 slice: extends the ui-fix3 node monitor with the two
// things ui-fix3's run could not answer:
//   (a) EVERY node gets its world/screen position -> so the two unexplained
//       rectangles inside uifix3_wp_panel.png (a 72x128 dark block at
//       x 781..852 / y 479..606 and a 72x16 gray block right under it) can be
//       matched to a node by geometry instead of by guesswork;
//   (b) a screen-point OWNERSHIP grid over the panel box (screen px = canvas px
//       on this project's overlay canvas, proven by BoxFrame width 518.4 ==
//       the 518 px measured in the tile) -> "who draws this pixel", both by
//       EventSystem raycast (topmost raycastable) and by rect containment
//       (topmost graphic in draw order, includes raycastTarget=false nodes).
//   (c) the ghost poll runs EVERY frame: the ui-fix3 run missed the whole drag
//       (monitor polled at 0.35 s, the drag lives ~0.25 s) so no mid-drag tile
//       and no inventory dump were produced at all.
//   (d) the label ink is sampled: for the first destination button the glyph
//       quads' uvRect + the atlas texture are logged, plus the measured ink
//       colour, so "dark glyph" vs "solid dark rectangle" is decided by data.
// Outputs: .ai-tmp/screenshots/uifix4_{wp_panel,drag,drag_after}.png
//          .ai-tmp/test/uifix4_{wp_dump,inv_dump}.txt
// Passive monitor, ASCII only, DontDestroyOnLoad.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UF4
{
    public static class Dump
    {
        public static string Install(string spec)
        {
            var go = new GameObject("UiFix4DumpDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Monitor>();
            drv.Init(string.IsNullOrEmpty(spec) ? "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp" : spec);
            Debug.Log("[UF4] DUMP-INSTALL spec=" + spec + " gameRunning=" + (CloverEngine.Game.IsRunning ? 1 : 0));
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

        public void Init(string root) { _root = root; }

        private void Update()
        {
            // (c) every frame -- the drag is far shorter than any poll interval
            if (!_wpDumped) TryWaypoint();
            if (!_afterDone) TryGhost();
        }

        private void TryWaypoint()
        {
            var panels = Resources.FindObjectsOfTypeAll<WaypointPanel>();
            WaypointPanel p = null;
            foreach (var x in panels) { if (x != null && x.gameObject.activeInHierarchy) { p = x; break; } }
            if (p == null) return;

            var file = Path.Combine(_root, @"test\uifix4_wp_dump.txt");
            StartCoroutine(CaptureThen(ScreenCapturePath("uifix4_wp_panel.png"), delegate {
                DumpTree(p.transform, "WaypointPanel", file);
                DumpCanvasOthers(p.transform, file);
                HitGrid(p.transform, file);
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
                var g = ghost;
                StartCoroutine(CaptureThen(ScreenCapturePath("uifix4_drag.png"), delegate {
                    var sr = g.GetComponent<Image>();
                    var raw = g.GetComponentInChildren<RawImage>(true);
                    Say("GHOST-MID active=1 sprite=" + SpriteName(sr) + " color=" + Col(sr) +
                        " pos=" + Pos(ghost as RectTransform) + " size=" + Size(ghost as RectTransform) +
                        " rawTex=" + (raw != null && raw.texture != null ? raw.texture.name : "none") +
                        " rawUv=" + (raw != null ? raw.uvRect.ToString() : "-") +
                        " tooltipActive=" + TooltipActive(p));
                    DumpTree(p.transform, "InventoryPanel-during-drag",
                        Path.Combine(_root, @"test\uifix4_inv_dump.txt"));
                    _ghostMidDone = true;
                }));
            }
            else if (!active && _ghostWasActive && _ghostMidDone && !_afterDone)
            {
                StartCoroutine(CaptureThen(ScreenCapturePath("uifix4_drag_after.png"), delegate {
                    var living = new List<string>();
                    CountActiveGraphics(p.transform, living);
                    Say("GHOST-AFTER active=0 activeGraphicsUnderInventory=" + living.Count);
                    foreach (var s in living) Say("GHOST-AFTER-NODE " + s);
                    DumpTree(p.transform, "InventoryPanel-after-drop",
                        Path.Combine(_root, @"test\uifix4_inv_dump.txt"));
                    _afterDone = true;
                }));
            }
        }

        private static bool TooltipActive(InventoryPanel p)
        {
            var t = p.transform.Find("ItemTooltip");
            return t != null && t.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Draw-order walk; only Graphics that actually render are listed
        /// (⚠️ BOTH conditions are required: the node must be activeInHierarchy AND
        /// alpha &gt; 0.02 — the ui-fix4a build only tested alpha, so the hidden
        /// DragGhost/DropHighlight still showed up in the list and the
        /// "residual == 0" claim could not be read off it; fixed here).
        /// </summary>
        private static void CountActiveGraphics(Transform t, List<string> into)
        {
            var g = t.GetComponent<Graphic>();
            if (g != null && g.gameObject.activeInHierarchy && g.color.a > 0.02f)
                into.Add(PathOf(t) + " color=" + Col(g) + " size=" + Size(t as RectTransform));
            for (var i = 0; i < t.childCount; i++) CountActiveGraphics(t.GetChild(i), into);
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
            if (l != null) l.Info("UF4", msg);
            else Debug.Log("[UF4] " + msg);
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

        private static string Pos(RectTransform rt)
        {
            return rt == null ? "-" : "(" + rt.position.x.ToString("0.#") + "," + rt.position.y.ToString("0.#") + ")";
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
            var line = pad + t.name + " active=" + (t.gameObject.activeInHierarchy ? 1 : 0)
                + " pos=" + Pos(rt) + " size=" + Size(rt);
            var img = t.GetComponent<Image>();
            var raw = t.GetComponent<RawImage>();
            var txt = t.GetComponent<Text>();
            if (img != null)
            {
                var mat = img.material != null && img.material.shader != null ? img.material.shader.name : "null";
                var tex = img.sprite != null && img.sprite.texture != null
                    ? img.sprite.texture.name + ":" + img.sprite.texture.width + "x" + img.sprite.texture.height : "none";
                line += " | Image sprite=" + SpriteName(img) + " tex=" + tex + " mat=" + mat
                    + " color=" + Col(img) + " type=" + img.type + " ray=" + (img.raycastTarget ? 1 : 0);
            }
            if (raw != null) line += " | RawImage tex=" + TextureName(raw) + " uv=" + raw.uvRect + " color=" + Col(raw)
                + " ray=" + (raw.raycastTarget ? 1 : 0);
            if (txt != null) line += " | Text '" + txt.text + "' color=" + Col(txt) + " fontNull=" + (txt.font == null ? 1 : 0) + " enabled=" + (txt.enabled ? 1 : 0);
            sb.AppendLine(line);
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, sb);
        }

        // every Graphic in the whole canvas whose screen rect overlaps the given
        // panel rect but is NOT under the panel root (the "who draws the box" hunt).
        private static void DumpCanvasOthers(Transform panelRoot, string file)
        {
            try
            {
                var canvas = panelRoot.GetComponentInParent<Canvas>();
                if (canvas == null) return;
                var box = panelRoot.Find("ScreenFit/BoxFrame") as RectTransform;
                if (box == null) box = panelRoot as RectTransform;
                var corners = new Vector3[4];
                box.GetWorldCorners(corners);
                var sb = new StringBuilder();
                sb.AppendLine("== canvas-scan overlap-of-" + box.name + " @ " + DateTime.UtcNow.ToString("o"));
                var all = canvas.GetComponentsInChildren<Graphic>(true);
                foreach (var g in all)
                {
                    if (g == null || !g.gameObject.activeInHierarchy) continue;
                    if (g.transform.IsChildOf(panelRoot)) continue;
                    var c2 = new Vector3[4];
                    g.rectTransform.GetWorldCorners(c2);
                    if (Overlaps(corners, c2))
                    {
                        var img = g as Image;
                        sb.AppendLine("OTHER " + PathOf(g.transform) + " type=" + g.GetType().Name
                            + " color=" + Col(g)
                            + (img != null ? " sprite=" + SpriteName(img) : "")
                            + " worldCenter=" + g.rectTransform.position);
                    }
                }
                File.AppendAllText(file, sb.ToString());
            }
            catch (Exception ex) { Say("CANVASSCAN-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// (b) 24 px ownership grid over the panel box.  Two answers per point:
        /// RAY = topmost raycastable Graphic (EventSystem.RaycastAll), OWN = last
        /// Graphic in draw order whose rect contains the point (catches
        /// raycastTarget=false decor).  Screen px == canvas px on this canvas.
        /// </summary>
        private static void HitGrid(Transform panelRoot, string file)
        {
            try
            {
                var box = panelRoot.Find("ScreenFit/BoxFrame") as RectTransform;
                if (box == null) box = panelRoot as RectTransform;
                var corners = new Vector3[4];
                box.GetWorldCorners(corners);
                var (min, max) = Bounds(corners);
                var canvas = panelRoot.GetComponentInParent<Canvas>();
                var all = canvas.GetComponentsInChildren<Graphic>(true);
                var sb = new StringBuilder();
                sb.AppendLine("== hit-grid over " + box.name + " box=" + min + ".." + max + " @ " + DateTime.UtcNow.ToString("o"));
                sb.AppendLine("== columns: x,y RAY=<topmost raycastable> OWN=<topmost graphic in draw order>");
                var ped = new PointerEventData(EventSystem.current);
                var list = new List<RaycastResult>();
                for (var y = min.y + 6f; y <= max.y; y += 24f)
                {
                    for (var x = min.x + 6f; x <= max.x; x += 24f)
                    {
                        var ray = "(none)";
                        if (EventSystem.current != null)
                        {
                            ped.position = new Vector2(x, y);
                            list.Clear();
                            EventSystem.current.RaycastAll(ped, list);
                            if (list.Count > 0) ray = PathOf(list[0].gameObject.transform);
                        }
                        string own = "(none)";
                        foreach (var g in all)
                        {
                            if (g == null || !g.gameObject.activeInHierarchy || g.color.a <= 0.02f) continue;
                            var c2 = new Vector3[4];
                            g.rectTransform.GetWorldCorners(c2);
                            var (bmin, bmax) = Bounds(c2);
                            if (x >= bmin.x && x <= bmax.x && y >= bmin.y && y <= bmax.y) own = PathOf(g.transform) + " color=" + Col(g);
                        }
                        sb.AppendLine("PT " + x.ToString("0") + "," + y.ToString("0") + " RAY=" + ray + " OWN=" + own);
                    }
                }
                File.AppendAllText(file, sb.ToString());
            }
            catch (Exception ex) { Say("HITGRID-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool Overlaps(Vector3[] a, Vector3[] b)
        {
            var (amin, amax) = Bounds(a);
            var (bmin, bmax) = Bounds(b);
            return amin.x <= bmax.x && bmin.x <= amax.x && amin.y <= bmax.y && bmin.y <= amax.y;
        }

        private static (Vector2, Vector2) Bounds(Vector3[] c)
        {
            var min = new Vector2(Mathf.Min(c[0].x, Mathf.Min(c[1].x, Mathf.Min(c[2].x, c[3].x))),
                                  Mathf.Min(c[0].y, Mathf.Min(c[1].y, Mathf.Min(c[2].y, c[3].y))));
            var max = new Vector2(Mathf.Max(c[0].x, Mathf.Max(c[1].x, Mathf.Max(c[2].x, c[3].x))),
                                  Mathf.Max(c[0].y, Mathf.Max(c[1].y, Mathf.Max(c[2].y, c[3].y))));
            return (min, max);
        }

        private static string PathOf(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
