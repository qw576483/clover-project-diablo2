// =============================================================================
// automap_drive.cs -- ONE Play session that produces the RENDERED evidence for the
// original-semantics automap (Tab overlay) in all three areas.
//
// Chain (single Play): enter the stage (reflection into the internal AppFlow, reusing
// the existing save roster) -> for each area in {Town, BloodMoor, DenOfEvil}:
//   emit Events.PanelToggleRequest("MiniMapPanel") -- the SAME event the HUD emits when
//   the player presses Tab (UI/HudPanel.cs:325) -> read the LIVE panel geometry off the
//   node tree -> capture the composited screen -> reveal the map the way walking does
//   (emit Events.PlayerGridChanged, the SAME event Player/PlayerModule.cs:705 emits when
//   the player's grid changes) -> capture again -> toggle the panel off -> capture again.
//
// Why the driver captures instead of `capture_game_view --save_path`: that resolves
// against the authoring root (Assets/) and a png written under Assets/ during Play
// forces a domain reload (see byline_drive.cs header). ScreenCapture.CaptureScreenshot
// writes outside the project (.ai-tmp/test/raw/) == the same composited pixels.
//
// TWO DRIVER TRAPS FIXED HERE (both produced wrong tiles in the previous piece):
//   (1) ScreenCapture.CaptureScreenshot writes the frame that is rendered AFTER the
//       current Update loop. The old chain requested the capture and, in the SAME frame,
//       either destroyed the panel (Toggle(false)) or issued GoStage(next area) -> the
//       png held the NEXT state (no overlay / the loading screen). Every capture now
//       yields WaitForEndOfFrame + one frame BEFORE the caller changes any state.
//   (2) There was no node-tree read-back of the panel, so "the overlay never rendered"
//       could not be told apart from "the capture missed it". GEOM= now dumps
//       RectTransform.rect / anchors / pivot / localScale / activeInHierarchy / Image /
//       RawImage / CanvasGroup for the panel root, Backdrop, Overlay, PlayerDot,
//       the first overlay child, plus the parent chain and the owning Canvas.
//
// EVIDENCE SHAPE (read by tools/probes/automap_sheet.py):
//   [AUTOMAP] GRID=<id> tile=<file> vals="<raw measured values>"
//   [AUTOMAP] GEOM=<id> <verbatim node/canvas dump>
//   [AUTOMAP] SHOT n=<i> name=<file> kind=screen
//   [AUTOMAP] SWEEP ...
//   [AUTOMAP] <PROBE>=<verbatim runtime values>
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace AutoMapDrv
{
    public static class Tour
    {
        internal const string Tag = "AUTOMAP";
        internal static string RawDir = string.Empty;
        internal static string DonePath = string.Empty;

        internal static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else UnityEngine.Debug.Log("[" + Tag + "] " + m);
        }

        internal static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + m);
        }

        /// <summary>Entry: install the chain host. spec = "rawDir|donePath".</summary>
        public static string Install(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            RawDir = p.Length > 0 ? p[0] : string.Empty;
            DonePath = p.Length > 1 ? p[1] : string.Empty;
            if (RawDir.Length > 0 && !Directory.Exists(RawDir)) Directory.CreateDirectory(RawDir);
            var go = new GameObject("AutoMapDrvHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Host>();
            Log("INSTALL rawDir=" + RawDir + " done=" + DonePath + " fsm=" + Fsm());
            return "installed";
        }

        internal static string Fsm()
        {
            try { return Game.Fsm != null ? Game.Fsm.Current : "(no-fsm)"; }
            catch (Exception ex) { return "(fsm-" + ex.GetType().Name + ")"; }
        }
    }

    internal sealed class Host : MonoBehaviour
    {
        private static readonly string[] Areas = { "Town", "BloodMoor", "DenOfEvil" };
        private int _shot;

        private void Start() { StartCoroutine(Chain()); }

        private IEnumerator Chain()
        {
            yield return new WaitForSeconds(1.5f);
            if (Tour.Fsm() != "Stage")
            {
                if (!EnterStage()) { Tour.Warn("ENTER-FAILED fsm=" + Tour.Fsm()); WriteDone("enter-failed"); yield break; }
                yield return StartCoroutine(WaitStage(60f, "enter"));
                if (Tour.Fsm() != "Stage") { WriteDone("enter-timeout"); yield break; }
            }

            for (var i = 0; i < Areas.Length; i++)
            {
                var area = Areas[i];
                if (!IssueStage(area)) { Tour.Warn("AREA-FAILED area=" + area + " fsm=" + Tour.Fsm()); continue; }
                yield return StartCoroutine(WaitStage(60f, area));
                if (Tour.Fsm() != "Stage") continue;
                yield return new WaitForSeconds(1.2f);

                // ---- (a) just entered: the map is almost entirely UNEXPLORED
                Toggle(true);
                yield return StartCoroutine(WaitStable(1.5f));
                yield return StartCoroutine(Shoot(area + "_on_unexpl", area, 1));

                // ---- (b) after the player has walked the map: mostly EXPLORED
                yield return StartCoroutine(SweepReveal(area));
                yield return new WaitForSeconds(0.4f);
                yield return StartCoroutine(Shoot(area + "_on_expl", area, 1));

                // ---- (c) Tab off: the same frame, overlay gone
                Toggle(false);
                yield return new WaitForSeconds(0.8f);
                yield return StartCoroutine(Shoot(area + "_off", area, 0));
            }

            WriteDone("chain-complete shots=" + _shot);
        }

        // ---- stage entry (reflection into the internal AppFlow) -------------------
        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        private static object Member(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fi = t.GetField(name, F);
            if (fi != null) return fi.GetValue(o);
            var pi = t.GetProperty(name, F);
            if (pi != null) return pi.GetValue(o, null);
            return null;
        }

        private static object SetMember(object o, string name, object v)
        {
            var t = o.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var fi = t.GetField(name, F);
            if (fi != null) { fi.SetValue(o, v); return v; }
            var pi = t.GetProperty(name, F);
            if (pi != null && pi.CanWrite) { pi.SetValue(v, null); return v; }
            return null;
        }

        private object Flow()
        {
            var ctxType = FindType("Diablo2.App.AppContext");
            if (ctxType == null) { Tour.Warn("NO-APPCONTEXT"); return null; }
            var pi = ctxType.GetProperty("I", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var ctx = pi != null ? pi.GetValue(null, null) : null;
            if (ctx == null) { Tour.Warn("NO-APPCONTEXT-I"); return null; }
            var flow = Member(ctx, "Flow");
            if (flow == null)
            {
                // tolerate a renamed field: first field whose type name mentions IAppFlow
                var t = ctx.GetType();
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (f.FieldType.Name.IndexOf("IAppFlow", StringComparison.Ordinal) >= 0) { flow = f.GetValue(ctx); break; }
            }
            return flow;
        }

        private bool EnterStage()
        {
            // already in the stage (a previous run left Play there)? nothing to do.
            if (Tour.Fsm() == "Stage") { Tour.Log("STAGE already fsm=Stage"); return true; }
            var flow = Flow();
            if (flow == null) return false;
            var ft = flow.GetType();

            var selF = ft.GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic);
            if (selF == null) { Tour.Warn("NO-_selected"); return false; }
            if (selF.GetValue(flow) == null)
            {
                var roster = Member(flow, "_roster");
                if (roster == null) { Tour.Warn("NO-_roster"); return false; }
                var rt = roster.GetType();
                const BindingFlags M = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var listM = rt.GetMethod("ListAll", M) ?? rt.GetMethod("List", M);
                string pick = null;
                var n = 0;
                if (listM != null)
                {
                    var items = listM.Invoke(roster, null) as IEnumerable;
                    if (items != null)
                        foreach (var it in items)
                        {
                            n++;
                            if (pick == null && it != null) pick = AsName(it);
                        }
                }
                Tour.Log("ROSTER method=" + (listM != null ? listM.Name : "(none)") + " count=" + n
                         + " pick=" + (pick ?? "(none)"));

                if (string.IsNullOrEmpty(pick))
                {
                    // no saved hero on this machine -> create one through the roster API
                    // (the same call the char-create screen makes), so the chain is reproducible.
                    var createM = rt.GetMethod("Create", M);
                    var saveType = FindType("Diablo2.Core.CharacterSave") ?? FindType("Diablo2.Def.CharacterSave");
                    if (createM == null || saveType == null)
                    {
                        Tour.Warn("ROSTER-EMPTY create=" + (createM != null) + " saveType=" + (saveType != null));
                        return false;
                    }
                    var data = Activator.CreateInstance(saveType);
                    SetMember(data, "name", "Ama1");
                    var clsF = saveType.GetField("cls", M);
                    if (clsF != null)
                    {
                        if (clsF.FieldType.IsEnum) clsF.SetValue(data, Enum.ToObject(clsF.FieldType, 0));
                        else clsF.SetValue(data, 0);
                    }
                    var ok = createM.Invoke(roster, new object[] { data });
                    Tour.Log("ROSTER-CREATE name=Ama1 ok=" + ok);
                    if (!(ok is bool) || !(bool)ok) { Tour.Warn("ROSTER-CREATE-FAILED"); return false; }
                    pick = "Ama1";
                }

                var loadM = rt.GetMethod("Load", M);
                if (loadM == null) { Tour.Warn("NO-Load"); return false; }
                var save = loadM.Invoke(roster, new object[] { pick });
                if (save == null) { Tour.Warn("LOAD-NULL name=" + pick); return false; }
                selF.SetValue(flow, save);
                Tour.Log("SELECTED name=" + pick);
            }

            // the FSM only allows CharSelect ->(EnterStage)-> Loading: walk Boot/MainMenu forward first.
            AdvanceFsmToCharSelect(flow);

            var goM = ft.GetMethod("GoStage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            goM.Invoke(flow, new object[] { AreaId.Town });
            Tour.Log("ENTER-ISSUED area=Town fsm=" + Tour.Fsm());
            return true;
        }

        private static string AsName(object it)
        {
            var s = it as string;
            if (s != null) return s;
            var v = Member(it, "name");
            return v as string;
        }

        /// <summary>
        /// The stage transition is only registered on CharSelect (IAppFlow header) - fire the
        /// engine's own triggers by name so the FSM walks Boot -> MainMenu -> CharSelect.
        /// </summary>
        private static void AdvanceFsmToCharSelect(object flow)
        {
            var cur = Tour.Fsm();
            if (cur == "CharSelect" || cur == "Stage") return;
            var t = typeof(Events.Fsm);
            var want = new string[] { "BootDone", "NewGame", "Continue" };
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f.FieldType != typeof(string)) continue;
                    var matched = false;
                    for (var i = 0; i < want.Length; i++)
                        if (f.Name.IndexOf(want[i], StringComparison.OrdinalIgnoreCase) >= 0) { matched = true; break; }
                    if (!matched) continue;
                    if (Tour.Fsm() == "CharSelect" || Tour.Fsm() == "Stage") return;
                    try
                    {
                        Game.Fsm.Trigger((string)f.GetValue(null));
                        Tour.Log("TRIGGER " + f.Name + " -> fsm=" + Tour.Fsm());
                    }
                    catch (Exception ex) { Tour.Warn("TRIGGER-FAIL " + f.Name + " " + ex.GetType().Name); }
                }
            }
        }

        /// <summary>
        /// Wait until the SAME panel instance has been alive (with a texture) for <paramref name="need"/>
        /// seconds. Why: `MapModule` re-emits `Events.MapGenerated` several times in a row after an area
        /// change and the HUD re-opens the panel on each event (measured: 7 events in 1.5s) - a shot taken
        /// right after a re-create catches a panel whose uGUI layout has not been computed yet (empty frame).
        /// </summary>
        private IEnumerator WaitStable(float need)
        {
            Component last = null;
            var since = Time.realtimeSinceStartup;
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 30f)
            {
                var panel = FindPanel() as Component;
                var hasTex = false;
                if (panel != null)
                {
                    var raws = panel.GetType()
                        .GetField("_raw", BindingFlags.Instance | BindingFlags.NonPublic)
                        .GetValue(panel) as UnityEngine.UI.RawImage;
                    hasTex = raws != null && raws.texture != null;
                }
                if (!ReferenceEquals(panel, last) || !hasTex)
                {
                    if (!ReferenceEquals(panel, last))
                        Tour.Log("STABLE-RESET newPanel=" + (panel != null ? panel.name : "(none)")
                                 + " hasTex=" + hasTex);
                    last = panel;
                    since = Time.realtimeSinceStartup;
                }
                if (panel != null && hasTex && Time.realtimeSinceStartup - since >= need)
                {
                    Tour.Log("STABLE panel=" + panel.name + " for=" + need.ToString("0.0") + "s");
                    yield break;
                }
                yield return null;
            }
            Tour.Warn("STABLE-TIMEOUT");
        }

        /// <summary>Frame-based wait (never block the main thread: the FSM/scene load need frames).</summary>
        private IEnumerator WaitStage(float timeout, string tag)
        {
            var t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                if (Tour.Fsm() == "Stage" && HudOpen())
                {
                    Tour.Log("STAGE-READY tag=" + tag + " fsm=" + Tour.Fsm() + " t=" + Time.time.ToString("0.00"));
                    yield break;
                }
                yield return null;
            }
            Tour.Warn("STAGE-TIMEOUT tag=" + tag + " fsm=" + Tour.Fsm());
        }

        private static bool HudOpen()
        {
            try { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
            catch { return true; }
        }

        private bool IssueStage(string area)
        {
            var a = (AreaId)Enum.Parse(typeof(AreaId), area);
            var flow = Flow();
            if (flow == null) return false;
            var goM = flow.GetType().GetMethod("GoStage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (goM == null) { Tour.Warn("NO-GoStage"); return false; }
            goM.Invoke(flow, new object[] { a });
            Tour.Log("AREA-ISSUED area=" + area + " fsm=" + Tour.Fsm());
            return true;
        }

        /// <summary>The HUD owns the toggle (UI/HudPanel.cs:325) - emit the same event it does on Tab.</summary>
        private static void Toggle(bool wantOpen)
        {
            var present = FindPanel() != null;
            if (present != wantOpen) Game.Event.Emit(Events.PanelToggleRequest, "MiniMapPanel");
            Tour.Log("TOGGLE want=" + (wantOpen ? "open" : "closed") + " presentBefore=" + (present ? 1 : 0));
        }

        private static object FindPanel()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.UI.MiniMapPanel>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? arr[0] : null;
        }

        /// <summary>
        /// Scenario generator: reveal the map the way walking does. Keeps the producer identical
        /// (emit `Events.PlayerGridChanged`, the event `PlayerModule.Tick` emits on a real grid
        /// change - NOT a teleport: the player object never leaves its cell), sweeping a 3-cell
        /// lattice so the panel's 3x3 reveal rule tiles the map without gaps, then re-emits the
        /// REAL player cell so the view recentres on where the player actually stands.
        /// </summary>
        private IEnumerator SweepReveal(string area)
        {
            var panel = FindPanel() as Component;
            if (panel == null) { Tour.Warn("SWEEP-NO-PANEL area=" + area); yield break; }
            var t = panel.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var map = t.GetField("_map", F).GetValue(panel);
            if (map == null) { Tour.Warn("SWEEP-NO-MAP area=" + area); yield break; }
            var mt = map.GetType();
            var w = (int)mt.GetField("width").GetValue(map);
            var h = (int)mt.GetField("height").GetValue(map);
            var px = (int)mt.GetField("playerX").GetValue(map);
            var py = (int)mt.GetField("playerY").GetValue(map);

            var t0 = Time.realtimeSinceStartup;
            var n = 0;
            for (var y = 0; y < h; y += 3)
            {
                for (var x = 0; x < w; x += 3)
                {
                    Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(x, y));
                    n++;
                    if ((n % 25) == 0) yield return null;
                }
            }
            Game.Event.Emit(Events.PlayerGridChanged, new Vector2Int(px, py));
            yield return null;

            var explored = t.GetField("_explored", F).GetValue(panel) as bool[];
            var c = 0;
            if (explored != null) for (var i = 0; i < explored.Length; i++) if (explored[i]) c++;
            Tour.Log("SWEEP area=" + area + " events=" + n + " step=3 exploredNow=" + c + "/" + (w * h)
                     + " recentredOn=(" + px + "," + py + ") ms=" + ((Time.realtimeSinceStartup - t0) * 1000f).ToString("0"));
        }

        /// <summary>
        /// Capture ONE frame of the state that was just measured.
        /// ScreenCapture.CaptureScreenshot writes the frame rendered AFTER the current Update
        /// loop, so the caller must not change any state in this frame (see file header).
        /// </summary>
        private IEnumerator Shoot(string id, string area, int expectOpen)
        {
            _shot++;
            var file = Tour.RawDir + "/am_" + id + ".png";
            try { if (File.Exists(file)) File.Delete(file); } catch { }
            var panel = FindPanel();
            var vals = "area=" + area + " fsm=" + Tour.Fsm()
                       + " panelOpen=" + (panel != null ? 1 : 0) + " expectOpen=" + expectOpen
                       + " " + Measure(panel);
            Tour.Log("GRID=" + id + " tile=am_" + id + ".png vals=\"" + vals + "\"");
            Geom(id, panel);
            ScreenCapture.CaptureScreenshot(file);
            yield return new WaitForEndOfFrame();   // <- the capture really happens here
            Tour.Log("SHOT n=" + _shot + " name=am_" + id + ".png kind=screen t=" + Time.time.ToString("0.00"));
            yield return null;                      // let the writer flush; next beat starts on a fresh frame
        }

        // ---------------------------------------------------------------------
        // Node-tree read-back (the "why is nothing on screen" instrument)
        // ---------------------------------------------------------------------
        private static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }
        private static string V(Vector2 v) { return "(" + F(v.x) + "," + F(v.y) + ")"; }
        private static string V3(Vector3 v) { return "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")"; }
        private static string C(Color c) { return "(" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + ")"; }

        private static string PathOf(RectTransform rt)
        {
            var s = rt.name;
            var p = rt.parent;
            while (p != null) { s = p.name + "/" + s; p = p.parent; }
            return s;
        }

        private static string NodeLine(string label, RectTransform rt)
        {
            if (rt == null) return label + "=(none)";
            var go = rt.gameObject;
            var sb = new StringBuilder();
            sb.Append(label).Append("[path=").Append(PathOf(rt));
            sb.Append(" activeSelf=").Append(go.activeSelf ? 1 : 0);
            sb.Append(" activeInHierarchy=").Append(go.activeInHierarchy ? 1 : 0);
            sb.Append(" rect=(").Append(F(rt.rect.x)).Append(",").Append(F(rt.rect.y)).Append(",")
              .Append(F(rt.rect.width)).Append(",").Append(F(rt.rect.height)).Append(")");
            sb.Append(" ancPos=").Append(V(rt.anchoredPosition));
            sb.Append(" sizeDelta=").Append(V(rt.sizeDelta));
            sb.Append(" anchor=").Append(V(rt.anchorMin)).Append("..").Append(V(rt.anchorMax));
            sb.Append(" pivot=").Append(V(rt.pivot));
            sb.Append(" localScale=").Append(V3(rt.localScale));
            sb.Append(" sibling=").Append(rt.GetSiblingIndex());
            var img = go.GetComponent<UnityEngine.UI.Image>();
            if (img != null)
                sb.Append(" Image(en=").Append(img.enabled ? 1 : 0).Append(",color=").Append(C(img.color))
                  .Append(",sprite=").Append(img.sprite != null ? img.sprite.name : "null").Append(")");
            var raw = go.GetComponent<UnityEngine.UI.RawImage>();
            if (raw != null)
                sb.Append(" RawImage(en=").Append(raw.enabled ? 1 : 0).Append(",color=").Append(C(raw.color))
                  .Append(",tex=").Append(raw.texture != null
                      ? (raw.texture.width + "x" + raw.texture.height) : "null")
                  .Append(",uv=").Append(raw.uvRect.ToString()).Append(")");
            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null)
                sb.Append(" CanvasGroup(alpha=").Append(F(cg.alpha))
                  .Append(",interactable=").Append(cg.interactable ? 1 : 0)
                  .Append(",blocksRaycasts=").Append(cg.blocksRaycasts ? 1 : 0).Append(")");
            sb.Append("]");
            return sb.ToString();
        }

        private static void Geom(string id, object panel)
        {
            if (panel == null) { Tour.Log("GEOM=" + id + " (panel-absent)"); return; }
            var comp = panel as Component;
            if (comp == null) { Tour.Log("GEOM=" + id + " (not-a-component)"); return; }
            var t = panel.GetType();
            // NOTE: named BF, not F -- a local F would shadow the F(float) formatter below.
            const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var root = comp.transform as RectTransform;

            var chain = new List<string>();
            var p = root;
            while (p != null)
            {
                chain.Add(p.name + "(" + F(p.rect.width) + "x" + F(p.rect.height) + ")");
                p = p.parent as RectTransform;
            }
            chain.Reverse();
            Tour.Log("GEOM=" + id + " CHAIN " + string.Join(" < ", chain.ToArray()));
            Tour.Log("GEOM=" + id + " SCREEN " + Screen.width + "x" + Screen.height);
            Tour.Log("GEOM=" + id + " " + NodeLine("PANELROOT", root));
            Tour.Log("GEOM=" + id + " " + NodeLine("BACKDROP", root != null ? root.Find("Backdrop") as RectTransform : null));
            var overlay = t.GetField("_overlay", BF).GetValue(panel) as RectTransform;
            Tour.Log("GEOM=" + id + " " + NodeLine("OVERLAY", overlay));
            Tour.Log("GEOM=" + id + " " + NodeLine("PLAYERDOT", t.GetField("_playerDot", BF).GetValue(panel) as RectTransform));
            if (overlay != null && overlay.childCount > 0)
                Tour.Log("GEOM=" + id + " " + NodeLine("OVERLAYCHILD0", overlay.GetChild(0) as RectTransform));

            var cv = comp.GetComponentInParent<Canvas>();
            if (cv != null)
                Tour.Log("GEOM=" + id + " CANVAS name=" + cv.name + " mode=" + cv.renderMode
                         + " sortingOrder=" + cv.sortingOrder + " enabled=" + (cv.enabled ? 1 : 0)
                         + " activeInHierarchy=" + (cv.gameObject.activeInHierarchy ? 1 : 0)
                         + " scaleFactor=" + F(cv.scaleFactor)
                         + " overrideSorting=" + (cv.overrideSorting ? 1 : 0)
                         + " pixelPerfect=" + (cv.pixelPerfect ? 1 : 0));
            else
                Tour.Log("GEOM=" + id + " CANVAS (none in parents!)");
        }

        /// <summary>Read the live automap values off the panel (private fields -> reflection).</summary>
        private static string Measure(object panel)
        {
            if (panel == null) return "measure=(panel-absent)";
            var t = panel.GetType();
            const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var map = t.GetField("_map", F).GetValue(panel);
            if (map == null) return "measure=(no-map)";
            var mt = map.GetType();
            var w = (int)mt.GetField("width").GetValue(map);
            var h = (int)mt.GetField("height").GetValue(map);
            var px = (int)mt.GetField("playerX").GetValue(map);
            var py = (int)mt.GetField("playerY").GetValue(map);
            var cels = mt.GetField("cels").GetValue(map) as IList<short>;
            var over = mt.GetField("celsOver").GetValue(map) as IList<short>;
            var explored = t.GetField("_explored", F).GetValue(panel) as bool[];
            var raws = t.GetField("_raw", F).GetValue(panel) as UnityEngine.UI.RawImage;
            var overlay = t.GetField("_overlay", F).GetValue(panel);
            var overlayRt = overlay as RectTransform;
            var vis = 0;
            var withCel = 0;
            var expCount = 0;
            for (var i = 0; i < cels.Count; i++)
            {
                if (cels[i] >= 0 || (over != null && i < over.Count && over[i] >= 0)) withCel++;
                if (explored != null && i < explored.Length && explored[i])
                {
                    expCount++;
                    if (cels[i] >= 0 || (over != null && i < over.Count && over[i] >= 0)) vis++;
                }
            }
            var pIdx = py * w + px;
            var sb = new System.Text.StringBuilder();
            sb.Append("map=").Append(w).Append("x").Append(h).Append(" seed=").Append(mt.GetField("seed").GetValue(map));
            sb.Append(" explored=").Append(expCount).Append(" cellsWithCel=").Append(withCel).Append("/").Append(cels.Count);
            sb.Append(" visibleCels=").Append(vis);
            sb.Append(" playerCel=").Append(pIdx >= 0 && pIdx < cels.Count ? cels[pIdx] : (short)-1);
            sb.Append("/").Append(pIdx >= 0 && over != null && pIdx < over.Count ? over[pIdx] : (short)-1);
            sb.Append(" tex=").Append(raws != null && raws.texture != null
                ? (raws.texture.width + "x" + raws.texture.height) : "(no-tex)");
            sb.Append(" overlaySize=").Append(overlayRt != null ? overlayRt.sizeDelta.ToString() : "(none)");
            sb.Append(" overlayPos=").Append(overlayRt != null ? overlayRt.anchoredPosition.ToString() : "(none)");
            if (pIdx >= 0 && pIdx < cels.Count && explored != null && pIdx < explored.Length)
                sb.Append(" playerExplored=").Append(explored[pIdx] ? 1 : 0);
            return sb.ToString();
        }

        private void WriteDone(string summary)
        {
            try
            {
                if (!string.IsNullOrEmpty(Tour.DonePath))
                    File.WriteAllText(Tour.DonePath, summary);
            }
            catch (Exception ex) { Tour.Warn("DONE-WRITE-FAIL " + ex.GetType().Name); }
            Tour.Log("SUMMARY " + summary);
        }
    }
}
