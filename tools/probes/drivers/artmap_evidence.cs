// =============================================================================
// artmap_evidence.cs -- ONE Play session that opens the automap (Tab), the pause
//   menu and leaves the HUD minipanel bar on screen, and reads each of them back
//   from the live render:
//     * one screenshot per step (ScreenCapture), so the picture can be tied to
//       real canvas pixels;
//     * a node dump (name / active / anchoredPosition / sizeDelta / sprite /
//       alpha / text) three levels deep -- the same numbers the eye sees;
//     * the automap's own counters (drawn cells / opaque pixels / marker count /
//       texture size) read off the live instance.
//
//   Injected through tools/probes/drivers/artmap_run.ps1:
//     unity command run_script --file artmap_evidence.cs --entry ArTMap.ArtMap.Install --args ["spec"]
//   spec = "<charName>|<donePath>|<shotDir>"
//   ASCII only (the runner self-checks it).
// =============================================================================
namespace ArTMap
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
    using UnityEngine.EventSystems;
    using UnityEngine.UI;

    /// <summary>Log / screenshot / reflection helpers (self-contained probe).</summary>
    public static class L
    {
        internal const string Tag = "ARTMAP";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else Debug.LogWarning("[ARTMAP] " + msg);
        }

        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static void Warn(string m) { Log("WARN " + m); }
        internal static void Paths(string donePath) { _done = donePath ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("ARTMAP-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var dir = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(_done, "ARTMAP-DONE " + DateTime.Now.ToString("o"));
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

        /// <summary>Read a private/protected field or property off a live component (null-safe).</summary>
        internal static object Field(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                try
                {
                    var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (f != null) return f.GetValue(o);
                    var p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (p != null) return p.GetValue(o, null);
                }
                catch { }
                t = t.BaseType;
            }
            return null;
        }

        internal static string FieldStr(object o, string name)
        {
            var v = Field(o, name);
            return v == null ? "(null)" : v.ToString();
        }

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
    public static class ArtMap
    {
        public static string Install(string spec)
        {
            var go = new GameObject("ArtMapDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            L.Log("ARTMAP-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    /// <summary>One chain: stage -> automap -> pause -> HUD minipanel bar.</summary>
    public class Driver : MonoBehaviour
    {
        private string _name = "S2203805";
        private string _shotDir = string.Empty;
        private int _step;
        private float _at;
        private float _t0;
        private bool _done;
        private bool _menuShot;
        private int _shotCount;

        public void Init(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            if (p.Length > 0 && p[0].Length > 0) _name = p[0];
            if (p.Length > 1) L.Paths(p[1]);
            if (p.Length > 2) _shotDir = p[2];
            _t0 = Time.unscaledTime;
            L.Log("ARTMAP-INIT char=" + _name + " shots=" + _shotDir);
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

        /// <summary>Find a live Button node by name under <paramref name="panel"/> (deep, first hit).</summary>
        private static Button FindButton(Component panel, string nodeName)
        {
            if (panel == null) return null;
            var all = panel.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < all.Length; i++)
                if (all[i].name == nodeName) return all[i];
            return null;
        }

        /// <summary>The sprite the button is currently showing (empty string = none).</summary>
        private static string SpriteOf(Button b)
        {
            if (b == null) return "(button-null)";
            // Unity's SpriteSwap writes Image.overrideSprite (NOT Image.sprite), so read both:
            // reading only .sprite reports "never changes" while the picture does change.
            var g = b.targetGraphic as Image;
            var shown = g == null ? null : (g.overrideSprite != null ? g.overrideSprite : g.sprite);
            return "sprite=" + (shown != null ? shown.name : "(none)")
                + " override=" + (g != null && g.overrideSprite != null ? g.overrideSprite.name : "(null)")
                + " transition=" + b.transition;
        }

        private static void Fire<T>(GameObject go, ExecuteEvents.EventFunction<T> f) where T : IEventSystemHandler
        {
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), f);
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
                var raw = c.GetComponent<RawImage>();
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
                    sb.Append(" rgb=").Append(L.F(img.color.r)).Append(',').Append(L.F(img.color.g)).Append(',').Append(L.F(img.color.b));
                    sb.Append(" ray=").Append(img.raycastTarget ? 1 : 0);
                }
                if (raw != null)
                {
                    sb.Append(" rawTex=").Append(raw.texture != null
                        ? raw.texture.width + "x" + raw.texture.height : "(null)");
                    sb.Append(" ray=").Append(raw.raycastTarget ? 1 : 0);
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

        /// <summary>The automap's own numbers, off the live instance (no second render path).</summary>
        private static void DumpAutomap()
        {
            var p = FindPanel("Diablo2.UI.MiniMapPanel");
            if (p == null) { L.Warn("AUTOMAP panel-null"); return; }
            var raw = new StringBuilder();
            raw.Append("DrawnCells=").Append(L.FieldStr(p, "DrawnCells"));
            raw.Append(" CellsWithCel=").Append(L.FieldStr(p, "CellsWithCel"));
            raw.Append(" OpaquePixels=").Append(L.FieldStr(p, "OpaquePixels"));
            raw.Append(" ExploredInjected=").Append(L.FieldStr(p, "ExploredInjected"));
            raw.Append(" texW=").Append(L.FieldStr(p, "_texW"));
            raw.Append(" texH=").Append(L.FieldStr(p, "_texH"));
            raw.Append(" markers=");
            var list = L.Field(p, "_markers") as System.Collections.ICollection;
            raw.Append(list != null ? list.Count.ToString() : "(null)");
            var map = L.Field(p, "_map");
            if (map != null)
            {
                raw.Append(" map=").Append(L.FieldStr(map, "width")).Append('x').Append(L.FieldStr(map, "height"));
                raw.Append(" player=").Append(L.FieldStr(map, "playerX")).Append(',').Append(L.FieldStr(map, "playerY"));
                raw.Append(" markerKinds=");
                var mk = L.Field(map, "markerKind") as System.Collections.ICollection;
                raw.Append(mk != null ? mk.Count.ToString() : "(null)");
                raw.Append(" cels=");
                var cels = L.Field(map, "cels") as System.Collections.ICollection;
                raw.Append(cels != null ? cels.Count.ToString() : "(null)");
            }
            L.KV("AUTOMAP-COUNT", raw.ToString());
            Dump("AUTOMAP", p, 2);
        }

        private void Watchdog(float seconds, string where)
        {
            if (Time.unscaledTime - _t0 > seconds) { L.Warn("WATCHDOG " + where + " -> finish"); Finish(); }
        }

        private void Finish()
        {
            if (_done) return;
            _done = true;
            L.KV("ARTMAP-SHOTS", _shotCount.ToString());
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
                    Watchdog(300f, "boot");
                    // 开机画面可能在本驱动被安装之前就已经结束（`editor_play` 一提交、Play 就开始跑）
                    // ⇒ 主菜单已经起来 = boot 已经结束，同样放行（否则本步要等满 300s 看门狗）。
                    if (Open<Diablo2.UI.MainMenuPanel>() || Fsm() == Events.Fsm.StateMainMenu)
                    {
                        L.Log("BOOT-SKIPPED fsm=" + Fsm() + " (menu already up)");
                        Next();
                        break;
                    }
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
                        // snap the main menu first (the shot lands on disk over the next frames),
                        // only then leave it for char select
                        if (!_menuShot)
                        {
                            Dump("MAINMENU", FindPanel("Diablo2.UI.MainMenuPanel"), 3);
                            Snap("artmap_mainmenu");
                            _menuShot = true;
                            _at = Time.unscaledTime;
                            break;
                        }
                        if (!Elapsed(1.6f)) break;
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

                // 4) the bare HUD: the minipanel bar as the player first sees it
                case 4:
                    if (Elapsed(1.0f))
                    {
                        Dump("HUD", FindPanel("Diablo2.UI.HudPanel"), 2);
                        Snap("artmap_hud_minipanel");
                        _at = Time.unscaledTime;
                        Next();
                    }
                    break;

                // 5) open the automap through the same entry Tab uses
                case 5:
                    if (Elapsed(1.5f))
                    {
                        L.Log("OPEN MiniMapPanel emit " + Events.PanelToggleRequest);
                        L.Emit(Events.PanelToggleRequest, "MiniMapPanel");
                        Next();
                    }
                    break;

                // 6) read the automap
                case 6:
                    if (Open<Diablo2.UI.MiniMapPanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(2.0f)) break;
                        DumpAutomap();
                        Snap("artmap_automap");
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 7) close the automap
                case 7:
                    if (Elapsed(1.5f))
                    {
                        L.Emit(Events.PanelToggleRequest, "MiniMapPanel");
                        Next();
                    }
                    break;

                // 8) open the pause menu the way the gear button / Esc does
                case 8:
                    if (Elapsed(1.5f))
                    {
                        L.Log("OPEN pause emit " + Events.PauseRequest);
                        L.Emit(Events.PauseRequest);
                        Next();
                    }
                    break;

                // 9) read the pause menu
                case 9:
                    if (Open<Diablo2.UI.PausePanel>() || Elapsed(12f))
                    {
                        if (!Elapsed(1.6f)) break;
                        Dump("PAUSE", FindPanel("Diablo2.UI.PausePanel"), 3);
                        Snap("artmap_pause");
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 10) click the pause menu's 4th slot -> the confirm dialog (its buttons are the
                //     medium 3-state art: normal / highlighted / pressed)
                case 10:
                    if (Elapsed(1.2f))
                    {
                        var toMain = FindButton(FindPanel("Diablo2.UI.PausePanel"), "ToMain");
                        if (toMain == null) L.Warn("PAUSE ToMain button not found -> no confirm dialog");
                        else
                        {
                            L.KV("CLICK", "ToMain " + SpriteOf(toMain));
                            Fire<IPointerClickHandler>(toMain.gameObject, ExecuteEvents.pointerClickHandler);
                        }
                        _at = Time.unscaledTime;
                        Next();
                    }
                    break;

                // 11) the confirm dialog: pointer-ENTER the cancel button (state only, no shot)
                case 11:
                    if (FindPanel("Diablo2.UI.D2ConfirmPanel") != null || Elapsed(10f))
                    {
                        if (!Elapsed(1.6f)) break;
                        var panel = FindPanel("Diablo2.UI.D2ConfirmPanel");
                        if (panel == null) { L.Warn("CONFIRM panel never opened"); Next(); break; }
                        Dump("CONFIRM", panel, 3);
                        var cancel = FindButton(panel, "Cancel");
                        if (cancel == null) { L.Warn("CONFIRM Cancel button not found"); Next(); break; }
                        Fire<IPointerEnterHandler>(cancel.gameObject, ExecuteEvents.pointerEnterHandler);
                        _at = Time.unscaledTime;
                        Next();
                        break;
                    }
                    break;

                // 12) HOVER frame: read the shown sprite and shoot (no state change in this frame --
                //     ScreenCapture lands at the END of the frame, so the press must come later)
                case 12:
                    if (Elapsed(1.2f))
                    {
                        var cancel = FindButton(FindPanel("Diablo2.UI.D2ConfirmPanel"), "Cancel");
                        if (cancel != null)
                        {
                            L.KV("BTN-HOVER", SpriteOf(cancel));
                            Snap("artmap_btn_hover");
                            _at = Time.unscaledTime;
                            Next();
                        }
                    }
                    break;

                // 13) press, and one frame later read + shoot the PRESSED frame
                case 13:
                    if (Elapsed(1.2f))
                    {
                        var cancel = FindButton(FindPanel("Diablo2.UI.D2ConfirmPanel"), "Cancel");
                        if (cancel != null) Fire<IPointerDownHandler>(cancel.gameObject, ExecuteEvents.pointerDownHandler);
                        _at = Time.unscaledTime;
                        Next();
                    }
                    break;

                // 14) PRESSED frame: read + shoot, then release
                case 14:
                    if (Elapsed(1.2f))
                    {
                        var cancel = FindButton(FindPanel("Diablo2.UI.D2ConfirmPanel"), "Cancel");
                        if (cancel != null)
                        {
                            L.KV("BTN-PRESSED", SpriteOf(cancel));
                            Snap("artmap_btn_pressed");
                            Fire<IPointerUpHandler>(cancel.gameObject, ExecuteEvents.pointerUpHandler);
                            Fire<IPointerExitHandler>(cancel.gameObject, ExecuteEvents.pointerExitHandler);
                        }
                        _at = Time.unscaledTime;
                        Next();
                    }
                    break;

                // 15) back to NORMAL: read + shoot (proves the swap is not "stuck pressed")
                case 15:
                    if (Elapsed(1.2f))
                    {
                        var cancel = FindButton(FindPanel("Diablo2.UI.D2ConfirmPanel"), "Cancel");
                        if (cancel != null)
                        {
                            L.KV("BTN-NORMAL", SpriteOf(cancel));
                            Snap("artmap_btn_normal");
                        }
                        _at = Time.unscaledTime;
                        Next();
                    }
                    break;

                // 16) done
                case 16:
                    if (Elapsed(2.0f)) Finish();
                    break;

                default:
                    Finish();
                    break;
            }
        }
    }
}
