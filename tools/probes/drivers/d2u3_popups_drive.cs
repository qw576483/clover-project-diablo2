// =============================================================================
// d2u3_popups_drive.cs -- slice popupaudit (user complaint batch 3:
//   "check EVERY popup" / "there is not even a close on it").
//
// WHAT IT ANSWERS (one Play session, all panels in ONE pass):
//   per panel: (1) every VISIBLE text on screen + its value string
//              (2) every interactive control (Button/Selectable) with
//                  sprite name / colour+alpha / raycastTarget / screen rect
//                  -> "is there a close control AND is it visible"
//              (3) whether the panel went away after the close control fires
//                  -> Game.UI.IsOpen<T>() is re-read afterwards
//              (4) one screenshot per panel
//   plus  .ai-tmp/test/u3_popups_shots.tsv  (cell id <-> panel <-> shot file)
//   plus  .ai-tmp/test/u3_popups_dump.txt   (the raw per-panel dump)
//
// TWO MODES (a panel is dumped exactly once, both modes feed the same dump):
//   A. DRIVEN  -- panels the driver opens itself, through the SAME entry the
//      player uses (HudPanel's own `Events.PanelToggleRequest` path keeps the
//      cached args: InventoryChangedArgs / PlayerStatsDto / SkillTreeArgs /
//      QuestStateDto / MinimapArgs -- opening with null would print all zeros):
//        InventoryPanel CharacterPanel SkillTreePanel QuestLogPanel MiniMapPanel
//        PausePanel (Fsm TriggerPause)   SettingsPanel (Game.UI.Open)
//        D2ConfirmPanel (Game.UI.Confirm)
//   B. WATCHED -- panels that need a world precondition (walk to an NPC / a
//      waypoint / a shop / die).  The driver does NOT fake them; it polls and
//      dumps the FIRST frame each one is open.  Pair this with the X tour
//      (tools/probes/drivers/x_drive.cs) for those stations:
//        ShopPanel NpcDialogPanel WaypointPanel DeathPanel
//        BootPanel MainMenuPanel CharSelectPanel CharCreatePanel LoadingPanel HudPanel
//
// CLOSE-CLICK CONTRACT: mode A fires the close control the way a player does
// (`Button.onClick.Invoke()` on the node whose name matches a close pattern);
// it then waits 2 frames and re-reads Game.UI.IsOpen<T>().  A panel with NO
// close-like control is reported as CLOSE-CONTROL=NONE (that is the finding,
// not a driver failure).  Real pointer injection is NOT used here on purpose:
// the popup mask sits above the HUD layer, and that ownership question is
// already answered offline (Runtime/Presentation/UI.cs:431-461).
//
// HOW TO RUN (editor must already be in Play; lock protocol = same as
// tools/probes/drivers/uifix4_run.ps1):
//   unity run_script --file tools/probes/drivers/d2u3_popups_drive.cs \
//         --entry D2U3P.Dump.Install \
//         --args '["c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp"]'
//   (ASCII ONLY on purpose: engine CLI/Roslyn on this host read a BOM-less non-ASCII file as ANSI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace D2U3P
{
    public static class Dump
    {
        public static string Install(string spec)
        {
            var go = new GameObject("D2U3PopupAuditDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Monitor>();
            drv.Init(string.IsNullOrEmpty(spec)
                ? "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp"
                : spec);
            Say("DUMP-INSTALL spec=" + spec + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "D2U3P-DUMP-INSTALLED";
        }

        // ---- logging (engine logger; never bare Debug.Log except as fallback) --
        public static void Say(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Info("D2U3P", msg);
            else Debug.Log("[D2U3P] " + msg);
        }
    }

    public class Monitor : MonoBehaviour
    {
        private string _root;
        private string _dump;
        private readonly List<string> _shots = new List<string>();
        private readonly HashSet<string> _done = new HashSet<string>();
        private int _cell;

        // mode A roster (panel type name -> how to open it)
        private static readonly string[] Driven =
        {
            "InventoryPanel", "CharacterPanel", "SkillTreePanel", "QuestLogPanel", "MiniMapPanel",
            "SettingsPanel", "D2ConfirmPanel", "PausePanel"
        };

        // mode B roster (world precondition; driver only watches)
        private static readonly string[] Watched =
        {
            "BootPanel", "MainMenuPanel", "CharSelectPanel", "CharCreatePanel", "LoadingPanel",
            "HudPanel", "ShopPanel", "NpcDialogPanel", "WaypointPanel", "DeathPanel"
        };

        public void Init(string root)
        {
            _root = root;
            _dump = Path.Combine(root, @"test\u3_popups_dump.txt");
            File.WriteAllText(_dump, "== d2u3 popup audit dump ==\r\n");
        }

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            // wait until the game is actually up (Boot -> stage); HUD open = stage.
            var guard = 0f;
            while (!Game.IsRunning || !IsOpen("HudPanel"))
            {
                guard += Time.unscaledDeltaTime;
                if (guard > 120f)
                {
                    Dump.Say("ABORT no-stage: gameRunning=" + (Game.IsRunning ? 1 : 0)
                             + " hudOpen=" + (IsOpen("HudPanel") ? 1 : 0)
                             + " (run the X tour first so the stage/HUD exists)");
                    yield break;
                }
                yield return null;
            }

            Dump.Say("STAGE-READY shots-dir=" + ShotsDir());

            // ---- mode A: open each panel, dump it, click its close control ----
            for (var i = 0; i < Driven.Length; i++)
            {
                var name = Driven[i];
                Dump.Say("A-BEGIN panel=" + name);
                OpenByEntry(name);
                yield return WaitFrames(3);

                if (!IsOpen(name))
                {
                    // u53-closefix: the family entry is a TOGGLE. If the panel was ALREADY open
                    // (a previous station, or a concurrent X tour driving the same screen), the
                    // emit above CLOSED it => re-emit once so the station is measured while open.
                    Dump.Say("A-RETRY panel=" + name + " notOpen=1 (toggle closed it) => re-emit");
                    OpenByEntry(name);
                    yield return WaitFrames(3);
                }
                if (!IsOpen(name))
                {
                    Dump.Say("A-SKIP panel=" + name + " notOpen=1 (entry refused; see engine log)");
                    continue;
                }
                yield return DumpAndClose(name, true);
            }

            // ---- R8-close (slice u53-closefix): family mutex + mask + mark provenance ----
            yield return R8FamilyMutex();

            // ---- mode B: watch the world-driven panels for 90 s ----
            var t = 0f;
            while (t < 90f)
            {
                for (var i = 0; i < Watched.Length; i++)
                {
                    var name = Watched[i];
                    if (_done.Contains(name)) continue;
                    if (!IsOpen(name)) continue;
                    yield return DumpAndClose(name, false);
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            for (var i = 0; i < Watched.Length; i++)
                if (!IsOpen(Watched[i]) && !_done.Contains(Watched[i]))
                    Dump.Say("B-MISS panel=" + Watched[i] + " neverOpened=1 (needs its world precondition)");

            WriteShotsIndex();
            Dump.Say("DONE cells=" + _cell + " shots=" + _shots.Count + " dump=" + _dump);
        }

        // =====================================================================
        // open entries (the player's own paths)
        // =====================================================================
        private static void OpenByEntry(string name)
        {
            var ev = Game.Event;
            switch (name)
            {
                case "InventoryPanel":
                case "CharacterPanel":
                case "SkillTreePanel":
                case "QuestLogPanel":
                case "MiniMapPanel":
                    // HudPanel caches the open args and re-uses them (HudPanel.cs:1219-1264)
                    ev.Emit(Events.PanelToggleRequest, name);
                    return;
                case "SettingsPanel":
                    Game.UI.Open<SettingsPanel>();
                    return;
                case "PausePanel":
                    Game.Fsm.Trigger(Events.Fsm.TriggerPause);
                    return;
                case "D2ConfirmPanel":
                    Game.UI.Confirm("\u5173\u95ed\u6d4b\u8bd5", "\u70b9\u786e\u8ba4\u5e94\u5f53\u5173\u6389\u672c\u6846",
                        delegate { Dump.Say("A-CONFIRM-OK"); }, null, "\u78ba\u8a8d", "\u53d6\u6d88");
                    return;
                default:
                    Dump.Say("A-UNKNOWN-ENTRY " + name);
                    return;
            }
        }

        // =====================================================================
        // dump one panel + optionally fire its close control
        // =====================================================================
        private IEnumerator DumpAndClose(string name, bool fireClose)
        {
            var go = FindOpen(name);
            if (go == null) { Dump.Say("DUMP-MISS " + name + " (no active GameObject)"); yield break; }
            _done.Add(name);
            _cell++;

            yield return new WaitForEndOfFrame();
            var shot = Path.Combine(ShotsDir(), "u3_popups_" + _cell.ToString("00") + "_" + name + ".png");
            Capture(shot);
            _shots.Add(_cell.ToString("00") + "\t" + name + "\t" + shot);

            var sb = new StringBuilder();
            sb.AppendLine("== CELL " + _cell.ToString("00") + " panel=" + name
                          + " @" + DateTime.UtcNow.ToString("o"));
            sb.AppendLine("SHOT " + shot);
            TextWalk(go.transform, sb);
            CtrlWalk(go.transform, sb);
            var closeNode = FindCloseNode(go.transform);
            sb.AppendLine("CLOSE-NODE " + (closeNode == null ? "NONE" : PathOf(closeNode)));
            if (closeNode != null)
            {
                var img = closeNode.GetComponent<Image>();
                var txt = closeNode.GetComponentInChildren<Text>(true);
                var btn = closeNode.GetComponent<Button>();
                var nrt = closeNode as RectTransform;
                sb.AppendLine("CLOSE-VISIBLE alpha=" + (img != null ? img.color.a.ToString("0.000") : "noImage")
                              + " raycastTarget=" + (img != null && img.raycastTarget ? 1 : 0)
                              + " interactable=" + (btn != null && btn.interactable ? 1 : 0)
                              + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "NULL")
                              + " label='" + (txt != null ? txt.text : "") + "'"
                              // u53-closefix: the hit area may be deliberately transparent while the
                              // VISIBLE mark is a bitmap-font Text child => the node's own alpha alone
                              // does NOT answer "can the player see a close affordance". Print the
                              // node rect (clickable area > 0) here, and the mark's own reading below.
                              + " nodeRect=" + (nrt == null ? "-" : nrt.rect.width.ToString("0.#") + "x" + nrt.rect.height.ToString("0.#")));
                if (txt != null)
                {
                    var trt = txt.transform as RectTransform;
                    // font=<name> + fontDynamic=0 is the proof that the D2 bitmap font actually
                    // resolved (project history: U49 = bitmap font silently demoted to system TTF).
                    sb.AppendLine("CLOSE-MARK text='" + txt.text + "'"
                                  + " color=" + Col(txt)
                                  + " alpha=" + txt.color.a.ToString("0.000")
                                  + " rect=" + (trt == null ? "-" : trt.rect.width.ToString("0.#") + "x" + trt.rect.height.ToString("0.#"))
                                  + " fontSize=" + txt.fontSize
                                  + " preferredW=" + txt.preferredWidth.ToString("0.#")
                                  + " preferredH=" + txt.preferredHeight.ToString("0.#")
                                  + " lineCount=" + txt.cachedTextGenerator.lineCount
                                  + " font=" + (txt.font != null ? txt.font.name : "NULL")
                                  + " fontDynamic=" + (txt.font != null && txt.font.dynamic ? 1 : 0));
                }
            }
            File.AppendAllText(_dump, sb.ToString());
            Dump.Say("CELL " + _cell.ToString("00") + " panel=" + name
                     + " close=" + (closeNode == null ? "NONE" : PathOf(closeNode)));

            if (!fireClose)
            {
                //   does NOT click the close control -- it would disrupt the tour latter stations (closing the shop loses its own later readings).
                //   But it MUST leave an explicit marker: this used to be a silent yield break, so a reader would think CLOSE-TEST ran.
                //   => CLOSE-RESULT for those 4 mode-B panels needs a separate active-mode pass in the future run.ps1.
                Dump.Say("CLOSE-TEST " + name + " skipped (mode-B watched panel: firing would disrupt later tour stations;"
                         + " needs an active-mode pass to get CLOSE-RESULT)");
                yield break;
            }
            if (closeNode == null) { Dump.Say("CLOSE-TEST " + name + " skipped (no control)"); yield break; }

            var b = closeNode.GetComponent<Button>();
            if (b == null) { Dump.Say("CLOSE-TEST " + name + " node has no Button"); yield break; }
            var before = IsOpen(name);
            try { b.onClick.Invoke(); }
            catch (Exception ex) { Dump.Say("CLOSE-TEST " + name + " invoke-threw " + ex.GetType().Name + ": " + ex.Message); }
            yield return WaitFrames(2);
            var after = IsOpen(name);
            File.AppendAllText(_dump, "CLOSE-RESULT " + name + " before=" + (before ? 1 : 0)
                                      + " after=" + (after ? 1 : 0) + "\r\n");
            Dump.Say("CLOSE-RESULT " + name + " before=" + (before ? 1 : 0) + " after=" + (after ? 1 : 0));
        }

        // =====================================================================
        // R8-close (slice u53-closefix): the load-bearing evidence
        //   1) same-entry mutex: open Inventory, then open SkillTree through the SAME entry the
        //      T key / HUD mini button uses (Events.PanelToggleRequest) => HudPanel.CloseScreenFamily
        //      must have closed Inventory (engine CloseMutexPanels cannot: SkillTree is Normal now).
        //   2) PopupMask: with a Normal-layer panel open the engine must NOT have a modal mask
        //      (UI.cs:447 names it "PopupMask"; probe-only Find by name).
        //   3) one shot per state (player-visible proof).
        // =====================================================================
        private IEnumerator R8FamilyMutex()
        {
            Dump.Say("[R8] MUTEX-BEGIN");
            // Deterministic station: the entry is a TOGGLE, so a panel left open by someone else
            // (X tour) would be CLOSED by the emit below (measured: inv_before=0 => inconclusive).
            if (IsOpen("InventoryPanel"))
            {
                Dump.Say("[R8] MUTEX pre-close InventoryPanel (it was still open)");
                Game.Event.Emit(Events.PanelToggleRequest, "InventoryPanel");
                yield return WaitFrames(3);
            }
            Game.Event.Emit(Events.PanelToggleRequest, "InventoryPanel");
            yield return WaitFrames(3);
            var invBefore = IsOpen("InventoryPanel");

            Game.Event.Emit(Events.PanelToggleRequest, "SkillTreePanel");
            yield return WaitFrames(3);
            var invAfter = IsOpen("InventoryPanel");
            var skillOpen = IsOpen("SkillTreePanel");
            var mask = GameObject.Find("PopupMask");
            Dump.Say("[R8] MUTEX inv_before=" + (invBefore ? 1 : 0) + " inv_after=" + (invAfter ? 1 : 0)
                     + " skill=" + (skillOpen ? 1 : 0) + " expect=1/0/1 by HudPanel.CloseScreenFamily");
            Dump.Say("[R8] POPUPMASK skillTreeOpen=" + (skillOpen ? 1 : 0)
                     + " mask=" + (mask == null ? "NONE" : "PRESENT")
                     + " (SkillTree=Normal => engine must NOT insert the full-screen modal mask)");

            _cell++;
            var shotSkill = Path.Combine(ShotsDir(), "u53_r8_skilltree_normal_layer.png");
            Capture(shotSkill);
            _shots.Add(_cell.ToString("00") + "\tSkillTreePanel(Normal,no-mask)\t" + shotSkill);

            // close the skill tree through the same entry, then open the inventory and shoot the mark
            Game.Event.Emit(Events.PanelToggleRequest, "SkillTreePanel");
            yield return WaitFrames(3);
            Game.Event.Emit(Events.PanelToggleRequest, "InventoryPanel");
            yield return WaitFrames(3);
            var invShotOpen = IsOpen("InventoryPanel");
            _cell++;
            var shotInv = Path.Combine(ShotsDir(), "u53_r8_inventory_close_mark.png");
            Capture(shotInv);
            _shots.Add(_cell.ToString("00") + "\tInventoryPanel(close mark)\t" + shotInv);

            // HUD entry re-clickable while the inventory (Popup) is open? read the mask again
            var maskInv = GameObject.Find("PopupMask");
            Dump.Say("[R8] INVENTORY-OPEN inv=" + (invShotOpen ? 1 : 0)
                     + " mask=" + (maskInv == null ? "NONE" : "PRESENT")
                     + " (Inventory stays Popup => mask expected HERE, close affordance must be visible)");
            Dump.Say("[R8] SHOTS skill=" + shotSkill + " inv=" + shotInv);

            // leave the stage clean (all family screens closed)
            if (IsOpen("InventoryPanel")) Game.Event.Emit(Events.PanelToggleRequest, "InventoryPanel");
            yield return WaitFrames(3);
            Dump.Say("[R8] MUTEX-END inv=" + (IsOpen("InventoryPanel") ? 1 : 0)
                     + " skill=" + (IsOpen("SkillTreePanel") ? 1 : 0));
        }

        // =====================================================================
        // walkers
        // =====================================================================
        private static void TextWalk(Transform t, StringBuilder sb)
        {
            var txt = t.GetComponent<Text>();
            if (txt != null && txt.gameObject.activeInHierarchy && !string.IsNullOrEmpty(txt.text))
            {
                var rt = t as RectTransform;
                var w = txt.preferredWidth;
                var fits = rt == null ? true : w <= rt.rect.width + 0.5f;
                sb.AppendLine("TEXT " + PathOf(t) + " '" + txt.text + "'"
                              + " color=" + Col(txt)
                              + " size=" + (rt == null ? "-" : rt.rect.width.ToString("0.#") + "x" + rt.rect.height.ToString("0.#"))
                              + " preferredW=" + w.ToString("0.#")
                              + " fits=" + (fits ? 1 : 0)
                              + " fontSize=" + txt.fontSize
                              + " lineCount=" + txt.cachedTextGenerator.lineCount);
            }
            for (var i = 0; i < t.childCount; i++) TextWalk(t.GetChild(i), sb);
        }

        private static void CtrlWalk(Transform t, StringBuilder sb)
        {
            var s = t.GetComponent<Selectable>();
            if (s != null && s.gameObject.activeInHierarchy)
            {
                var img = s.targetGraphic as Image;
                var rt = s.transform as RectTransform;
                var corners = new Vector3[4];
                if (rt != null) rt.GetWorldCorners(corners);
                sb.AppendLine("CTRL " + PathOf(s.transform)
                              + " kind=" + s.GetType().Name
                              + " interactable=" + (s.interactable ? 1 : 0)
                              + " sprite=" + (img != null && img.sprite != null ? img.sprite.name : "NULL")
                              + " color=" + (img != null ? Col(img) : "-")
                              + " raycastTarget=" + (img != null && img.raycastTarget ? 1 : 0)
                              + " screenRect=" + (rt == null ? "-"
                                  : corners[0].x.ToString("0") + "," + corners[0].y.ToString("0") + ".."
                                    + corners[2].x.ToString("0") + "," + corners[2].y.ToString("0")));
            }
            for (var i = 0; i < t.childCount; i++) CtrlWalk(t.GetChild(i), sb);
        }

        private static readonly string[] CloseWords =
            { "close", "exit", "cancel", "back", "resume", "toMain", "quit", "\u96e2\u958b", "\u5173\u95ed", "\u95dc\u9589", "\u53d6\u6d88", "\u8fd4\u56de", "\u7e7c\u7e8c" };

        private static Transform FindCloseNode(Transform root)
        {
            return FindClose(root);
        }

        private static Transform FindClose(Transform t)
        {
            var b = t.GetComponent<Button>();
            if (b != null && b.gameObject.activeInHierarchy && LooksClose(t.name)) return t;
            for (var i = 0; i < t.childCount; i++)
            {
                var hit = FindClose(t.GetChild(i));
                if (hit != null) return hit;
            }
            return null;
        }

        private static bool LooksClose(string n)
        {
            var low = n.ToLowerInvariant();
            for (var i = 0; i < CloseWords.Length; i++)
                if (low.IndexOf(CloseWords[i].ToLowerInvariant(), StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        // =====================================================================
        // helpers
        // =====================================================================
        private string ShotsDir()
        {
            var d = Path.Combine(_root, "screenshots");
            Directory.CreateDirectory(d);
            return d;
        }

        private void WriteShotsIndex()
        {
            var f = Path.Combine(_root, @"test\u3_popups_shots.tsv");
            var sb = new StringBuilder();
            sb.AppendLine("# cell\tpanel\tshot  (cell == the 00/01/.. prefix of the shot file)");
            for (var i = 0; i < _shots.Count; i++) sb.AppendLine(_shots[i]);
            File.WriteAllText(f, sb.ToString());
            Dump.Say("SHOTS-INDEX " + f + " rows=" + _shots.Count);
        }

        private static void Capture(string path)
        {
            try
            {
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                var png = tex.EncodeToPNG();
                File.WriteAllBytes(path, png);
                UnityEngine.Object.Destroy(tex);
                Dump.Say("SHOT-OK " + path + " bytes=" + (png != null ? png.Length : 0));
            }
            catch (Exception ex) { Dump.Say("SHOT-FAIL " + path + " " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static IEnumerator WaitFrames(int n)
        {
            for (var i = 0; i < n; i++) yield return new WaitForEndOfFrame();
        }

        private static bool IsOpen(string panelTypeName)
        {
            var t = FindType(panelTypeName);
            if (t == null) { Dump.Say("TYPE-MISS " + panelTypeName); return false; }
            var m = typeof(IUIManager).GetMethod("IsOpen").MakeGenericMethod(t);
            return (bool)m.Invoke(Game.UI, null);
        }

        private static GameObject FindOpen(string panelTypeName)
        {
            var t = FindType(panelTypeName);
            if (t == null) return null;
            var all = Resources.FindObjectsOfTypeAll(t);
            for (var i = 0; i < all.Length; i++)
            {
                var c = all[i] as Component;
                if (c != null && c.gameObject.activeInHierarchy) return c.gameObject;
            }
            return null;
        }

        private static Type FindType(string simpleName)
        {
            var direct = Type.GetType("Diablo2.UI." + simpleName);
            if (direct != null) return direct;
            var asm = typeof(InventoryPanel).Assembly;
            var arr = asm.GetTypes();
            for (var i = 0; i < arr.Length; i++)
                if (arr[i].Name == simpleName) return arr[i];
            return null;
        }

        private static string Col(Graphic g)
        {
            if (g == null) return "-";
            var c = g.color;
            return c.r.ToString("0.000") + "," + c.g.ToString("0.000") + ","
                   + c.b.ToString("0.000") + "," + c.a.ToString("0.000");
        }

        private static string PathOf(Transform t)
        {
            var p = t.name;
            while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
