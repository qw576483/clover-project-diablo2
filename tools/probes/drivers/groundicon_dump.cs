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
        private bool _walkDone;
        private bool _walkShotDone;
        private float _walkIssuedAt = -1f;
        private Vector2Int _itemCell;
        private float _lastWalkLog = -1f;
        private bool _zoomDone;

        private static Diablo2.Module.IPlayerModule PlayerModule()
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
            var ctx = p != null ? p.GetValue(null) : null;
            if (ctx == null) return null;
            var f = ctx.GetType().GetField("Player", BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) as Diablo2.Module.IPlayerModule : null;
        }

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

            // moment 3: the tour drops the item wherever the drag release landed (measured: release
            // x = -80 screen px, i.e. OFF-CAMERA) => the item is on the ground but NOT in frame.
            // Walk the player to a DIAGONAL NEIGHBOUR of the item so the follow-camera brings it into
            // frame, then shoot. Same public event the proven s2_drive probe uses (Events.MoveCommand);
            // a diagonal neighbour (not the item's own cell) so no pickup is triggered.
            if (_lateDone && !_walkDone) { _walkDone = WalkNextToItem(); if (_walkDone) _walkIssuedAt = Time.time; }

            // moment 3: shoot as soon as the player is actually ADJACENT to the item (the tour is also
            // issuing move commands, so "4 s after asking" is not a reliable arrival test) + a hard
            // fallback at 16 s. Player grid comes from the public IPlayerModule.
            if (_walkDone && !_walkShotDone)
            {
                var p = PlayerModule();
                var pg = p != null ? p.Grid : Vector2Int.zero;
                var d = Diablo2.Core.Iso.GridDistance(pg, _itemCell);
                if (Time.time - _lastWalkLog >= 1f)
                {
                    _lastWalkLog = Time.time;
                    Say("WALK-PROGRESS player=(" + pg.x + "," + pg.y + ") item=(" + _itemCell.x + "," + _itemCell.y
                        + ") dist=" + d + " moving=" + (p != null && p.IsMoving ? 1 : 0));
                }
                if (d <= 2 || Time.time - _walkIssuedAt >= 16f)
                {
                    _walkShotDone = true;
                    _seenMoments++;
                    StartCoroutine(CaptureThen(Shot("groundicon_mon_3.png"),
                        delegate { DumpAll("moment3-arrived dist=" + d); }));
                }
            }

            // moment 4 (deterministic, independent of the tour's own navigation): a TEMPORARY zoom
            // camera aimed at the ground item, rendered to a RenderTexture -- this is the shot that
            // must show which item it is. The game view itself is never touched.
            if (_lateDone && !_zoomDone)
            {
                _zoomDone = true;
                StartCoroutine(ZoomShot(Shot("groundicon_zoom.png")));
            }
        }

        /// <summary>Temporary orthographic zoom camera over the first ground item (see moment 4).</summary>
        private System.Collections.IEnumerator ZoomShot(string path)
        {
            yield return new WaitForEndOfFrame();

            var it = ItemModule();
            var list = it != null ? it.GroundItems : null;
            var view = ViewModule();
            if (list == null || list.Count == 0 || view == null)
            {
                Say("ZOOM-SKIP no ground item / no view");
                yield break;
            }
            var target = view.GetView(list[0].Key);
            if (target == null) { Say("ZOOM-SKIP no node"); yield break; }

            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var main = Camera.main;
                if (main == null)
                {
                    var cams = Resources.FindObjectsOfTypeAll<Camera>();
                    foreach (var c in cams) { if (c != null && c.enabled && c.gameObject.activeInHierarchy) { main = c; break; } }
                }
                if (main == null) { Say("ZOOM-SKIP no camera at all"); yield break; }

                var pos = target.transform.position;
                camGo = new GameObject("GIC-ZoomCam");
                var cam = camGo.AddComponent<Camera>();
                cam.CopyFrom(main);                       // projection / clear flags / culling mask / background
                cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(0.6f, main.orthographicSize * 0.30f);
                cam.transform.rotation = main.transform.rotation;
                cam.transform.position = new Vector3(pos.x, pos.y, main.transform.position.z);
                cam.targetTexture = rt = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
                cam.depth = main.depth + 50;
                cam.Render();

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(512, 512, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;

                var png = tex.EncodeToPNG();
                File.WriteAllBytes(path, png);

                // read back the centre pixel colour as a crude "is anything drawn" probe
                var centre = tex.GetPixel(256, 256);
                Say("ZOOM-SHOT " + path + " bytes=" + png.Length + " camPos=(" + pos.x.ToString("0.##") + ","
                    + pos.y.ToString("0.##") + "," + cam.transform.position.z.ToString("0.##") + ") orthoSize="
                    + cam.orthographicSize.ToString("0.###") + " centrePx=" + C(centre)
                    + " mainCam=" + main.name + " mainOrthoSize=" + main.orthographicSize.ToString("0.###"));
            }
            catch (Exception e)
            {
                Say("ZOOM-FAIL " + e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                if (camGo != null) UnityEngine.Object.Destroy(camGo);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>Emits a move command to a diagonal neighbour of the first ground item (see moment 3).</summary>
        private bool WalkNextToItem()
        {
            try
            {
                var it = ItemModule();
                var list = it != null ? it.GroundItems : null;
                if (list == null || list.Count == 0) return false;

                var view = ViewModule();
                var go = view != null ? view.GetView(list[0].Key) : null;
                if (go == null) { Say("WALK-ABORT no node for id=" + list[0].Key); return false; }

                var cell = Diablo2.Core.Iso.WorldToGrid(go.transform.position);
                _itemCell = cell;
                var target = new Vector2Int(cell.x + 1, cell.y + 1);
                CloverEngine.Game.Event.Emit<Vector2Int>(Diablo2.Core.Events.MoveCommand, target);
                Say("WALK-TO itemCell=(" + cell.x + "," + cell.y + ") target=(" + target.x + "," + target.y + ")");
                return true;
            }
            catch (Exception e)
            {
                Say("WALK-FAIL " + e.GetType().Name + ": " + e.Message);
                return false;
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
