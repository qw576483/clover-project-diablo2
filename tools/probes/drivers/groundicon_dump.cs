// =============================================================================
// groundicon_dump.cs -- ground-item-icon slice: PASSIVE monitor that answers
// "does a ground item actually render the ORIGINAL item icon (same art as the
// backpack), and is its SpriteRenderer no longer the solid quality-colour quad?"
//
// Why a new file (and not just s2_drive.cs): the tour drives the drag/drop and
// only takes tiles; the runtime NODE state (sprite name / texture / rect / colour)
// has to be read off the live objects, which is what this monitor does -- it is
// modelled on the proven tools/probes/drivers/uifix4_dump.cs (same install entry
// / same CaptureThen / same Say-to-Editor.log scheme), NOT a fresh chain.
//
// Outputs:
//   .ai-tmp/test/groundicon_nodes.txt         (per-node runtime dump, both moments)
//   .ai-tmp/screenshots/groundicon_mon_1.png  (first frame with a ground item)
//   .ai-tmp/screenshots/groundicon_mon_2.png  (2 s later: icon load is async)
//   Editor.log lines prefixed [GIC]
//
// Passive, ASCII only, DontDestroyOnLoad.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace GIC
{
    public static class Dump
    {
        /// <summary>
        /// Compile-only entry: proves this driver file compiles (run_script reports the Roslyn
        /// diagnostics) **without** touching the scene -- calling Install outside Play mode would
        /// create a stray GameObject in the opened scene. Use:
        ///   unity command run_script --file tools/probes/drivers/groundicon_dump.cs --entry GIC.Dump.SelfTest
        /// </summary>
        public static string SelfTest()
        {
            return "GIC-SELFTEST-OK placeholder=" + Monitor.PlaceholderNameForProbe();
        }

        public static string Install(string spec)
        {
            var go = new GameObject("GroundIconDumpDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Monitor>();
            drv.Init(string.IsNullOrEmpty(spec) ? "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp" : spec);
            Debug.Log("[GIC] DUMP-INSTALL spec=" + spec + " gameRunning=" + (CloverEngine.Game.IsRunning ? 1 : 0));
            return "DUMP-INSTALLED";
        }
    }

    public class Monitor : MonoBehaviour
    {
        /// <summary>The runtime placeholder texture/sprite names (ViewModule/SpriteFrames.Placeholder).</summary>
        private const string PlaceholderName = "D2CharPlaceholder";

        /// <summary>Compile-time probe hook for `GIC.Dump.SelfTest` (see that method).</summary>
        internal static string PlaceholderNameForProbe() { return PlaceholderName; }

        private string _root;
        private int _seenMoments;
        private float _firstSeenAt = -1f;
        private bool _quickDone;
        private bool _lateDone;
        private int _lastLoggedCount = -1;

        public void Init(string root) { _root = root; }

        private void Update()
        {
            var count = GroundCount();
            if (count != _lastLoggedCount)
            {
                _lastLoggedCount = count;
                Say("GROUND-COUNT " + count);
            }
            if (count <= 0) return;

            if (_firstSeenAt < 0f) _firstSeenAt = Time.time;

            // moment 1: as soon as an item exists (proves the pre-load state is invisible, not a gray quad)
            if (!_quickDone)
            {
                _quickDone = true;
                _seenMoments++;
                StartCoroutine(CaptureThen(Shot("groundicon_mon_1.png"), delegate { DumpAll("moment1"); }));
            }

            // moment 2: 2 s later -- Game.Res.LoadAsset is async, so the icon lands a frame or two later
            if (!_lateDone && Time.time - _firstSeenAt >= 2f)
            {
                _lateDone = true;
                _seenMoments++;
                StartCoroutine(CaptureThen(Shot("groundicon_mon_2.png"), delegate { DumpAll("moment2"); }));
            }
        }

        /// <summary>
        /// `AppContext` is **internal** ⇒ a run_script assembly cannot name it (CS0122, hit on the first
        /// attempt). Same reflection route the proven s2_drive.cs probe uses (`Type.GetType(name + ",
        /// Assembly-CSharp")` then the public `I` property + fields) -- the interfaces themselves are public.
        /// </summary>
        private static object Ctx()
        {
            var t = Type.GetType("Diablo2.App.AppContext, Assembly-CSharp");
            if (t == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    t = asm.GetType("Diablo2.App.AppContext");
                    if (t != null) break;
                }
            }
            if (t == null) return null;
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            return p != null ? p.GetValue(null) : null;
        }

        private static object CtxMember(string name)
        {
            var ctx = Ctx();
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) : null;
        }

        private static Diablo2.Module.IItemModule ItemModule()
        {
            return CtxMember("Item") as Diablo2.Module.IItemModule;
        }

        private static Diablo2.Module.IViewModule ViewModule()
        {
            return CtxMember("View") as Diablo2.Module.IViewModule;
        }

        private static int GroundCount()
        {
            try
            {
                var it = ItemModule();
                if (it == null) return -1;
                return it.GroundItems != null ? it.GroundItems.Count : -1;
            }
            catch (Exception e)
            {
                Say("GROUND-COUNT-FAIL " + e.GetType().Name + ": " + e.Message);
                return -1;
            }
        }

        /// <summary>
        /// Two independent sources, so a broken dictionary lookup cannot hide the defect:
        ///   (a) the contract list `IItemModule.GroundItems` -> `IViewModule.GetView(id)`;
        ///   (b) every active SpriteRenderer in the scene whose sprite is null or is the
        ///       runtime placeholder (the old "quality-colour quad" signature).
        /// </summary>
        private void DumpAll(string moment)
        {
            var sb = new StringBuilder();
            sb.AppendLine("== ground items @" + moment + " " + DateTime.UtcNow.ToString("o")
                + " frames-since-first-seen=" + (Time.time - _firstSeenAt).ToString("0.00") + "s");

            int n = 0, nullSprite = 0, placeholders = 0, withIcon = 0;
            var iconNames = new List<string>();

            try
            {
                var it = ItemModule();
                var view = ViewModule();
                var list = it != null ? it.GroundItems : null;
                if (list != null)
                {
                    for (var i = 0; i < list.Count; i++)
                    {
                        var kv = list[i];
                        var go = view != null ? view.GetView(kv.Key) : null;
                        var sr = go != null ? go.GetComponent<SpriteRenderer>() : null;
                        var item = kv.Value;
                        var spriteName = sr != null && sr.sprite != null ? sr.sprite.name : "NULL";
                        var texName = sr != null && sr.sprite != null && sr.sprite.texture != null
                            ? sr.sprite.texture.name : "none";
                        var rect = sr != null && sr.sprite != null
                            ? sr.sprite.rect.width.ToString("0") + "x" + sr.sprite.rect.height.ToString("0") : "-";
                        var col = sr != null ? C(sr.color) : "-";
                        var pos = go != null ? "(" + go.transform.position.x.ToString("0.##") + ","
                            + go.transform.position.y.ToString("0.##") + ")" : "-";
                        var scale = go != null ? go.transform.localScale.x.ToString("0.###") : "-";

                        n++;
                        if (sr == null || sr.sprite == null) nullSprite++;
                        else if (sr.sprite.name == PlaceholderName) placeholders++;
                        else { withIcon++; iconNames.Add(spriteName + "|" + texName + "|" + rect); }

                        var line = "GIC-NODE id=" + kv.Key
                            + " itemId=" + (item != null ? item.itemId : -1)
                            + " name=" + (item != null ? item.name : "?")
                            + " quality=" + (item != null ? item.quality.ToString() : "?")
                            + " node=" + (go != null ? go.name : "NO-NODE")
                            + " sprite=" + spriteName + " tex=" + texName + " rect=" + rect
                            + " color=" + col + " pos=" + pos + " scale=" + scale;
                        sb.AppendLine("  " + line);
                        Say(line);
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine("  (list-dump failed: " + e.GetType().Name + ": " + e.Message + ")");
                Say("GIC-DUMP-FAIL " + e.GetType().Name + ": " + e.Message);
            }

            // (b) scene-wide: the old defect signature is "active E_* node with NULL / placeholder sprite"
            try
            {
                var all = Resources.FindObjectsOfTypeAll<SpriteRenderer>();
                var strays = 0;
                foreach (var sr in all)
                {
                    if (sr == null || !sr.gameObject.activeInHierarchy) continue;
                    if (!sr.gameObject.name.StartsWith("E_")) continue;
                    if (sr.sprite == null)
                    {
                        strays++;
                        sb.AppendLine("  STRAY-NULL " + sr.gameObject.name + " color=" + C(sr.color)
                            + " pos=" + sr.transform.position.ToString("0.##"));
                    }
                    else if (sr.sprite.name == PlaceholderName)
                    {
                        strays++;
                        sb.AppendLine("  STRAY-PLACEHOLDER " + sr.gameObject.name + " color=" + C(sr.color)
                            + " pos=" + sr.transform.position.ToString("0.##"));
                    }
                }
                sb.AppendLine("  scene-scan E_* nodes with NULL/placeholder sprite = " + strays);
            }
            catch (Exception e) { sb.AppendLine("  (scene-scan failed: " + e.Message + ")"); }

            sb.AppendLine("GIC-SUMMARY moment=" + moment + " groundItems=" + n
                + " withOriginalIcon=" + withIcon + " nullSprite=" + nullSprite
                + " placeholderSprite=" + placeholders);
            Say("GIC-SUMMARY moment=" + moment + " groundItems=" + n
                + " withOriginalIcon=" + withIcon + " nullSprite=" + nullSprite
                + " placeholderSprite=" + placeholders
                + " icons=" + string.Join(" ; ", iconNames.ToArray()));

            try { File.WriteAllText(Path.Combine(_root, @"test\groundicon_nodes.txt"), sb.ToString()); }
            catch (Exception e) { Say("GIC-WRITE-FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        // ---- helpers (same scheme as uifix4_dump.cs) -------------------------
        private string Shot(string name)
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
            if (l != null) l.Info("GIC", msg);
            else Debug.Log("[GIC] " + msg);
        }

        private static string C(Color c)
        {
            return c.r.ToString("0.000") + "," + c.g.ToString("0.000") + "," + c.b.ToString("0.000")
                + "," + c.a.ToString("0.000");
        }
    }
}
