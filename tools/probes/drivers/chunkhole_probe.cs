// =============================================================================
// chunkhole_probe.cs -- read-only reflection probe for MapView's rebuild job.
//
//   WHY: S2 bisected the "blood moor black background" down to (a) "the chunks
//        that should exist were never built", and left ONE unmeasured link:
//        why `_job` stays non-null (why PumpRebuild -> SwapToBuilt never lands).
//
//   WHAT IT READS (reflection only -- NO product code is touched, no field is
//   added to MapView):  _job identity / Frames / NodesBuilt / ChunkIndex /
//   Building / CursorX,Y / Chunks.Count / buffers null?  +  _pendingChunks /
//   _retireChunks / _groundChunks / _bufGroundChunks / _chunkMin / _chunkMax /
//   MapView GameObject active+enabled / frame count / camera position /
//   expected visible chunk range (same formula as ComputeVisibleChunkRange) and
//   the list of chunks in that range that were never built.
//
//   HOW THE READINGS DECIDE THE ROOT CAUSE (-- decided BEFORE touching a line):
//     * jobId changes between samples      => StartRebuild is called again and
//                                             again (each new job wipes the
//                                             previous one + clears the pending
//                                             queue) => no job ever reaches the
//                                             swap.
//     * jobId same, frames keeps growing   => Update IS pumping, the job simply
//                                             never fills its chunk list.
//     * jobId same, frames frozen AND the
//       frame counter keeps growing        => MapView.Update never reaches
//                                             PumpRebuild (early return above).
//     * go=0 (MapRoot inactive)            => Update cannot be called at all.
//
//   Entry points:  ChH.Api.Ping  /  ChH.Api.Chunk
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;

namespace ChH
{
    public static class Api
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const int ChunkSize = 16;          // mirrors MapView.ChunkSize

        internal static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
            return p != null ? p.GetValue(null) : null;
        }

        internal static object CtxMember(string name)
        {
            var ctx = Ctx();
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(ctx) : null;
        }

        internal static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(full); } catch { t = null; }
                if (t != null) return t;
            }
            return null;
        }

        internal static object F(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, NP);
            return f != null ? f.GetValue(o) : null;
        }

        /// <summary>Same as F but also matches PUBLIC fields (RebuildJob's fields are public).</summary>
        internal static object F2(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, ALL);
            return f != null ? f.GetValue(o) : null;
        }

        internal static Type TypeOfMapView()
        {
            var t = FindType("Diablo2.Module.Map.MapView");
            return t != null ? t : typeof(MonoBehaviour);
        }

        internal static int Coll(object o, string name)
        {
            var v = F(o, name) as ICollection;
            return v != null ? v.Count : -1;
        }

        internal static List<Vector2Int> Keys(object o, string name)
        {
            var res = new List<Vector2Int>();
            var d = F(o, name) as IDictionary;
            if (d == null) return res;
            foreach (var k in d.Keys) { if (k is Vector2Int v) res.Add(v); }
            return res;
        }

        /// <summary>Capture one screenshot to an absolute path (rendered at end of frame).</summary>
        public static string Shot(string path)
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                return "SHOT-OK " + path + " frame=" + Time.frameCount;
            }
            catch (Exception ex) { return "SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message; }
        }

        public static string Ping()
        {
            return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000");
        }

        /// <summary>One deep reading of the MapView rebuild state machine.</summary>
        public static string Chunk()
        {
            var sb = new StringBuilder();
            sb.Append("t=" + Time.unscaledTime.ToString("0.000"));
            sb.Append(" frame=" + Time.frameCount);

            var map = CtxMember("Map") as Diablo2.Module.IMapModule;
            if (map == null) return "CHUNK-DEEP no-MapModule";

            object view = null;
            var mt = map.GetType();
            while (mt != null)
            {
                var f = mt.GetField("_view", NP);
                if (f != null) { view = f.GetValue(map); break; }
                mt = mt.BaseType;
            }
            if (view == null) return "CHUNK-DEEP no-_view";

            var comp = view as Component;
            var bh = view as Behaviour;                  // Component has no `enabled`; Behaviour does
            sb.Append(" goActive=" + (comp != null && comp.gameObject.activeInHierarchy ? 1 : 0));
            sb.Append(" goEnabled=" + (bh != null && bh.enabled ? 1 : 0));

            sb.Append(" map=" + map.Width + "x" + map.Height);
            sb.Append(" area=" + AreaLabel(map.Area));
            sb.Append(" chunked=" + ((F(view, "_chunked") is bool b1 && b1) ? 1 : 0));
            sb.Append(" chunkMin=" + F(view, "_chunkMin"));
            sb.Append(" chunkMax=" + F(view, "_chunkMax"));
            sb.Append(" ground=" + Coll(view, "_groundChunks"));
            sb.Append(" bufGround=" + Coll(view, "_bufGroundChunks"));
            sb.Append(" pending=" + Coll(view, "_pendingChunks"));
            sb.Append(" retire=" + Coll(view, "_retireChunks"));

            var job = F(view, "_job");
            if (job == null)
            {
                sb.Append(" job=null");
            }
            else
            {
                sb.Append(" jobId=" + RuntimeHelpers.GetHashCode(job));
                sb.Append(" jobFrames=" + F2(job, "Frames"));
                sb.Append(" nodesBuilt=" + F2(job, "NodesBuilt"));
                sb.Append(" building=" + F2(job, "Building"));
                sb.Append(" chunkIdx=" + F2(job, "ChunkIndex"));
                var chunks = F2(job, "Chunks") as ICollection;
                sb.Append(" jobChunks=" + (chunks != null ? chunks.Count : -1));
                sb.Append(" cursor=(" + F2(job, "CursorX") + "," + F2(job, "CursorY") + ")");
                var g0 = F2(job, "GroundRoot") == null ? 1 : 0;
                var o0 = F2(job, "ObjectRoot") == null ? 1 : 0;
                var v0 = F2(job, "OverlayRoot") == null ? 1 : 0;
                sb.Append(" rootsNull=" + g0 + o0 + v0);
            }
            sb.Append(" showing=" + F2(view, "_showing"));

            // how many MapView instances are alive? (a stale one with a stuck job would
            // freeze THAT instance's Update while the module already points elsewhere)
            var views = UnityEngine.Object.FindObjectsByType(TypeOfMapView());
            sb.Append(" viewCount=" + views.Length);
            for (var i = 0; i < views.Length && i < 4; i++)
            {
                var v = views[i];
                var c2 = v as Component;
                var j2 = F2(v, "_job");
                sb.Append(" #v" + i + "=" + (c2 != null && c2.gameObject.activeInHierarchy ? "A" : "i")
                          + "/job=" + (j2 == null ? "null" : "RUN")
                          + "/built=" + Coll(v, "_groundChunks"));
            }

            // ---- expected visible chunk range (same formula as the product) ----
            var cam = Camera.main;
            sb.Append(" cam=" + (cam != null ? cam.transform.position.ToString("0.0") : "(none)"));
            var built0 = Keys(view, "_groundChunks");
            if (cam != null)
            {
                var cx = (map.Width + ChunkSize - 1) / ChunkSize;
                var cy = (map.Height + ChunkSize - 1) / ChunkSize;
                var minX = int.MaxValue; var minY = int.MaxValue;
                var maxX = int.MinValue; var maxY = int.MinValue;
                for (var i = 0; i < 4; i++)
                {
                    var sx = (i & 1) == 0 ? 0f : Screen.width;
                    var sy = (i & 2) == 0 ? 0f : Screen.height;
                    var g = Diablo2.Core.Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                    minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
                    maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
                }
                var e0x = Mathf.Clamp(minX / ChunkSize - 1, 0, cx - 1);
                var e0y = Mathf.Clamp(minY / ChunkSize - 1, 0, cy - 1);
                var e1x = Mathf.Clamp(maxX / ChunkSize + 1, 0, cx - 1);
                var e1y = Mathf.Clamp(maxY / ChunkSize + 1, 0, cy - 1);
                var built = Keys(view, "_groundChunks");
                var pen = Keys(view, "_pendingChunks");
                var miss = new StringBuilder();
                var n = 0;
                var total = 0;
                for (var cx2 = e0x; cx2 <= e1x; cx2++)
                {
                    for (var cy2 = e0y; cy2 <= e1y; cy2++)
                    {
                        total++;
                        var c = new Vector2Int(cx2, cy2);
                        var found = built.Contains(c) || pen.Contains(c);
                        if (found) continue;
                        n++;
                        if (miss.Length < 200)
                        {
                            if (miss.Length > 0) miss.Append(' ');
                            miss.Append("(" + cx2 + "," + cy2 + ")");
                        }
                    }
                }
                sb.Append(" expected=(" + e0x + "," + e0y + ")-(" + e1x + "," + e1y + ")"
                          + " total=" + total + " MISSING=" + n + " [" + miss + "]");
                sb.Append(" corners=[" + minX + "," + minY + ".." + maxX + "," + maxY + "]");
                sb.Append(" player=" + PlayerGrid());
                var ks = new StringBuilder();
                foreach (var k in built0) { if (ks.Length > 0) ks.Append(' '); ks.Append(k.x + ":" + k.y); }
                sb.Append(" keys=[" + ks + "]");
            }

            return "CHUNK-DEEP " + sb;
        }

        private static string PlayerGrid()
        {
            try
            {
                var p = CtxMember("Player");
                if (p == null) return "(none)";
                var pr = p.GetType().GetProperty("Grid");
                if (pr == null) return "(no-Grid)";
                return pr.GetValue(p).ToString();
            }
            catch (Exception ex) { return "(" + ex.GetType().Name + ")"; }
        }

        private static string AreaLabel(object area)
        {
            if (area == null) return "?";
            try { return Convert.ToInt32(area).ToString(); } catch { return area.ToString(); }
        }
    }
}
