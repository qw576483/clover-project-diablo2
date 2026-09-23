// =============================================================================
// finalclose_hoverdiag.cs -- final-close (2026-09-24): runtime read-out for the
// NPC dialog option button hover state.  PROBE ONLY (it reads product state; its
// only side effect is queueing a mouse position, which the evidence needs).
//
// WHY: a pure pixel diff cannot separate "the highlight plate swapped" from "the
// mouse cursor is drawn on top of the button".  These entries answer it directly:
//   FDiag.Hover.Move "<index>|<raw|flip>" -> park the injected pointer on Option<i>
//                                             (centre from its OWN RectTransform)
//   FDiag.Hover.Read "<tag>"              -> input module / mouse position / raycast
//                                             top hit / PopupMask / per-option
//                                             Image.sprite.name + Selectable state
//                                             (state == Highlighted + sprite ==
//                                             btn_med_sel is the proof hover engaged)
//
// NOTE: run_script compiles this file STANDALONE => no X.* helpers and no game
// facade here; every dependency is a Unity/InputSystem public API.
// ASCII ONLY.
// =============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace FDiag
{
    public static class Hover
    {
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

                var mouse = Mouse.current;
                var added = 0;
                if (mouse == null) { mouse = InputSystem.AddDevice<Mouse>(); added = 1; }
                InputSystem.QueueStateEvent(mouse, new MouseState { position = pos });

                return "MOVE opt=" + idx + " flip=" + flip + " mouseAdded=" + added
                       + " rect=" + rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#")
                       + " rectPos=(" + rt.position.x.ToString("0.#") + "," + rt.position.y.ToString("0.#") + ")"
                       + " rectCenter=(" + rt.rect.center.x.ToString("0.#") + "," + rt.rect.center.y.ToString("0.#") + ")"
                       + " screen=(" + screen.x.ToString("0.#") + "," + screen.y.ToString("0.#") + ")"
                       + " queued=(" + pos.x.ToString("0.#") + "," + pos.y.ToString("0.#") + ")"
                       + " screenSize=" + Screen.width + "x" + Screen.height
                       + " canvas=" + CanvasName(rt);
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

                var mouse = Mouse.current;
                var posStr = "(no-mouse)";
                var hasPos = false;
                var pos = Vector2.zero;
                if (mouse != null)
                {
                    pos = mouse.position.ReadValue();
                    hasPos = true;
                    posStr = "(" + pos.x.ToString("0.#") + "," + pos.y.ToString("0.#") + ")";
                }
                sb.Append(" mouse.current=").Append(posStr);

                if (es != null && hasPos)
                {
                    var ped = new PointerEventData(es) { position = pos };
                    var hits = new List<RaycastResult>();
                    es.RaycastAll(ped, hits);
                    sb.Append(" hits=").Append(hits.Count);
                    if (hits.Count > 0)
                    {
                        var top = hits[0].gameObject;
                        sb.Append(" top=").Append(top == null ? "(null)" : top.name);
                        sb.Append(" topParent=").Append(top == null || top.transform.parent == null
                            ? "(none)" : top.transform.parent.name);
                        sb.Append(" topLayer=").Append(top == null ? "?" : LayerMask.LayerToName(top.layer));
                    }
                }

                var imgs = UnityEngine.Object.FindObjectsByType<Image>(FindObjectsInactive.Exclude);
                var maskInfo = "";
                for (var i = 0; i < imgs.Length; i++)
                {
                    var m = imgs[i];
                    if (m == null || m.gameObject == null) continue;
                    if (m.gameObject.name.IndexOf("Mask", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    maskInfo += " [" + m.gameObject.name + " active=" + (m.gameObject.activeInHierarchy ? 1 : 0)
                                + " raycast=" + (m.raycastTarget ? 1 : 0)
                                + " parent=" + (m.transform.parent == null ? "?" : m.transform.parent.name)
                                + " sibling=" + m.transform.GetSiblingIndex() + "]";
                }
                sb.Append(" masks=").Append(maskInfo.Length == 0 ? "(none)" : maskInfo);

                var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude);
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
                      .Append(" state=").Append(SelState(sel))
                      .Append(" hiSprite=").Append(b.spriteState.highlightedSprite == null
                          ? "(null)" : b.spriteState.highlightedSprite.name)
                      .Append(" selSprite=").Append(b.spriteState.selectedSprite == null
                          ? "(null)" : b.spriteState.selectedSprite.name)
                      .Append(" pressedSprite=").Append(b.spriteState.pressedSprite == null
                          ? "(null)" : b.spriteState.pressedSprite.name)
                      .Append(" raycastTarget=").Append(img == null ? "?" : (img.raycastTarget ? "1" : "0"))
                      .Append(" screenCenter=").Append(ScreenCenter(b.transform as RectTransform));
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

        private static string ScreenCenter(RectTransform rt)
        {
            if (rt == null) return "(no-rt)";
            var world = rt.TransformPoint(new Vector3(rt.rect.center.x, rt.rect.center.y, 0f));
            var s = RectTransformUtility.WorldToScreenPoint(null, world);
            return "(" + s.x.ToString("0.#") + "," + s.y.ToString("0.#") + ")";
        }

        private static string CanvasName(RectTransform rt)
        {
            try
            {
                var c = rt.GetComponentInParent<Canvas>();
                return c == null ? "(none)" : (c.name + "/" + c.renderMode + "/sort=" + c.sortingOrder);
            }
            catch { return "(err)"; }
        }

        /// <summary>Selectable.currentSelectionState is protected => reflect it (probe only).</summary>
        private static string SelState(Selectable s)
        {
            if (s == null) return "?";
            try
            {
                var p = typeof(Selectable).GetProperty("currentSelectionState",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (p == null) return "(no-prop)";
                var v = p.GetValue(s);
                return v == null ? "(null)" : v.ToString();
            }
            catch (Exception e) { return "EX:" + e.GetType().Name; }
        }

        private static Button FindOption(string index)
        {
            var want = "Option" + index;
            var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude);
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
            var btns = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude);
            for (var i = 0; i < btns.Length && i < 24; i++)
            {
                var b = btns[i];
                if (b == null) continue;
                sb.Append(' ').Append(b.gameObject.name).Append('/')
                  .Append(b.transform.parent == null ? "?" : b.transform.parent.name);
            }
            return sb.ToString();
        }
    }
}
