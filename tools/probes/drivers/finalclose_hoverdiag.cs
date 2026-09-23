// =============================================================================
// finalclose_hoverdiag.cs -- final-close (2026-09-24): runtime read-out for the
// NPC dialog option button hover state.  PROBE ONLY -- it reads, it never writes
// product state (the only side effect is queueing a mouse position, which is what
// the evidence needs).
//
// WHY: a pure pixel diff cannot separate "the hover plate swapped" from "the mouse
// cursor is drawn on top of the button".  These two entries answer it directly:
//   Hover.Move "<index>|<raw|flip>"  -> park the injected pointer on Option<index>
//                                       (centre taken from its own RectTransform)
//   Hover.Read "<tag>"               -> EventSystem / input module / the game's
//                                       mouse position / raycast top hit /
//                                       per-option sprite name + Selectable state
//                                       (currentSelectionState == Highlighted is THE
//                                       proof that hover really engaged)
//
// ASCII ONLY.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FDiag
{
    public static class Hover
    {
        /// <summary>spec = "&lt;optionIndex&gt;|&lt;raw|flip&gt;" (flip mirrors Y about Screen.height).</summary>
        public static string Move(string spec)
        {
            try
            {
                var parts = (spec ?? string.Empty).Split('|');
                var idx = parts.Length > 0 ? parts[0] : "0";
                var flip = parts.Length > 1 && parts[1] == "flip";
                var go = FindOption(idx);
                if (go == null) return "MOVE-FAIL no Option" + idx + " ; " + DumpRoots();
                var rt = go.transform as RectTransform;
                if (rt == null) return "MOVE-FAIL no RectTransform on Option" + idx;
                var world = rt.TransformPoint(new Vector3(rt.rect.center.x, rt.rect.center.y, 0f));
                var screen = RectTransformUtility.WorldToScreenPoint(null, world);
                var pos = flip ? new Vector2(screen.x, Screen.height - screen.y) : screen;
                var probe = X.Api2.ProbeMouse(pos.x.ToString("0.#") + "," + pos.y.ToString("0.#"));
                return "MOVE opt=" + idx + " flip=" + flip
                       + " rect=" + rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#")
                       + " rectCenter=(" + rt.rect.center.x.ToString("0.#") + "," + rt.rect.center.y.ToString("0.#") + ")"
                       + " screen=(" + screen.x.ToString("0.#") + "," + screen.y.ToString("0.#") + ")"
                       + " queued=(" + pos.x.ToString("0.#") + "," + pos.y.ToString("0.#") + ")"
                       + " probe=" + probe + " screenSize=" + Screen.width + "x" + Screen.height;
            }
            catch (Exception e)
            {
                return "MOVE-EX " + e.GetType().Name + ": " + e.Message;
            }
        }

        public static string Read(string tag)
        {
            var sb = new StringBuilder();
            sb.Append("READ tag=").Append(tag);
            try
            {
                var es = EventSystem.current;
                sb.Append(" eventSystem=").Append(es == null ? "NULL" : es.name);
                sb.Append(" module=").Append(es == null || es.currentInputModule == null
                    ? "NULL" : es.currentInputModule.GetType().Name);

                var mp = "(null)";
                var hasHitPos = false;
                Vector2 hitPos = Vector2.zero;
                try
                {
                    var input = Diablo2.Core.Game.Input;
                    if (input != null) { var v = input.MousePosition; hitPos = new Vector2(v.x, v.y); hasHitPos = true; mp = "(" + v.x.ToString("0.#") + "," + v.y.ToString("0.#") + ")"; }
                }
                catch (Exception e) { mp = "EX:" + e.GetType().Name; }
                sb.Append(" gameMouse=").Append(mp);

                if (es != null && hasHitPos)
                {
                    var ped = new PointerEventData(es) { position = hitPos };
                    var hits = new List<RaycastResult>();
                    es.RaycastAll(ped, hits);
                    sb.Append(" hits=").Append(hits.Count);
                    if (hits.Count > 0)
                    {
                        var top = hits[0].gameObject;
                        sb.Append(" top=").Append(top == null ? "(null)" : top.name);
                        var p = top == null ? null : top.transform.parent;
                        sb.Append(" topParent=").Append(p == null ? "(none)" : p.name);
                        sb.Append(" topLayer=").Append(top == null ? "?" : LayerMask.LayerToName(top.layer));
                    }
                }

                // popup mask (engine Runtime/Presentation/UI.cs) -- if one is up it eats
                // the pointer rays over UILayer.Normal, which is where the dialog lives
                var masks = UnityEngine.Object.FindObjectsByType<Image>(FindObjectsSortMode.None);
                var maskInfo = "";
                for (var i = 0; i < masks.Length; i++)
                {
                    var m = masks[i];
                    if (m == null || m.gameObject == null) continue;
                    if (m.gameObject.name.IndexOf("Mask", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    maskInfo += " [" + m.gameObject.name + " active=" + (m.gameObject.activeInHierarchy ? 1 : 0)
                                + " raycast=" + (m.raycastTarget ? 1 : 0)
                                + " parent=" + (m.transform.parent == null ? "?" : m.transform.parent.name)
                                + " sibling=" + m.transform.GetSiblingIndex() + "]";
                }
                sb.Append(" masks=").Append(maskInfo.Length == 0 ? "(none)" : maskInfo);

                var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
                var n = 0;
                for (var i = 0; i < btns.Length; i++)
                {
                    var b = btns[i];
                    if (b == null || b.gameObject == null) continue;
                    if (!b.gameObject.name.StartsWith("Option", StringComparison.Ordinal)) continue;
                    var img = b.GetComponent<Image>();
                    var sel = b.GetComponent<Selectable>();
                    sb.Append(" | ").Append(b.gameObject.name)
                      .Append(" active=").Append(b.gameObject.activeInHierarchy ? 1 : 0)
                      .Append(" interactable=").Append(b.interactable ? 1 : 0)
                      .Append(" sprite=").Append(img == null || img.sprite == null ? "(null)" : img.sprite.name)
                      .Append(" transition=").Append(sel == null ? "?" : sel.transition.ToString())
                      .Append(" state=").Append(sel == null ? "?" : sel.currentSelectionState.ToString())
                      .Append(" hiSprite=").Append(b.spriteState.highlightedSprite == null
                          ? "(null)" : b.spriteState.highlightedSprite.name)
                      .Append(" selSprite=").Append(b.spriteState.selectedSprite == null
                          ? "(null)" : b.spriteState.selectedSprite.name)
                      .Append(" pressedSprite=").Append(b.spriteState.pressedSprite == null
                          ? "(null)" : b.spriteState.pressedSprite.name);
                    n++;
                }
                sb.Append(" optionButtons=").Append(n);
                return sb.ToString();
            }
            catch (Exception e)
            {
                return sb.ToString() + " READ-EX " + e.GetType().Name + ": " + e.Message;
            }
        }

        /// <summary>All live option buttons (name starts with "Option" under the dialog panel).</summary>
        private static Button FindOption(string index)
        {
            var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            var want = "Option" + index;
            for (var i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null || b.gameObject == null) continue;
                if (b.gameObject.name != want) continue;
                var p = b.transform.parent;
                if (p != null && p.name.StartsWith("NpcDialogPanel", StringComparison.Ordinal)) return b;
            }
            // fall back: any Option<i> whose parent lives under a NpcDialogPanel ancestor
            for (var i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null || b.gameObject == null) continue;
                if (b.gameObject.name != want) continue;
                var t = b.transform;
                while (t != null)
                {
                    if (t.name.StartsWith("NpcDialogPanel", StringComparison.Ordinal)) return b;
                    t = t.parent;
                }
            }
            return null;
        }

        private static string DumpRoots()
        {
            var sb = new StringBuilder("buttons:");
            var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            for (var i = 0; i < btns.Length; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                sb.Append(' ').Append(b.gameObject.name).Append('/')
                  .Append(b.transform.parent == null ? "?" : b.transform.parent.name);
                if (i > 20) break;
            }
            return sb.ToString();
        }
    }
}
