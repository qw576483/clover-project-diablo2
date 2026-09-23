// =============================================================================
// blackwhy_probe.cs -- read-only reflection probe for the PAINT chain.
//
//   WHY: chunk-ctl proved the blood-moor black window is NOT "chunks missing":
//        at T1 (+4 s) both variants showed MISSING=0 / job=null and the screen
//        was still black (patched T1: ground=6 chunks, expected=(0,0)-(1,2),
//        total=6, MISSING=0).  So the question moves one link forward:
//        "the block IS built -- why is the pixel still black?"
//
//   WHAT IT READS (reflection only, no product code is touched):
//        * layer roots (_groundRoot/_objectRoot/_overlayRoot): activeInHierarchy,
//          chunk-root count, how many chunk roots are activeInHierarchy.
//        * every SpriteRenderer parented under the ground layer:
//          total / activeInHierarchy / enabled / sprite!=null / color.a>0.
//        * WHERE they are on screen: each renderable node is bucketed into a
//          4x4 screen grid by Camera.main.WorldToViewportPoint, so a black
//          region can be classified as
//             bucketAny=0            -> no node covers that screen area at all
//             bucketAny>0 render=0   -> nodes exist but are NOT renderable
//                                       (inactive / disabled / null sprite).
//        * z of the viewport point (vpZmin/vpZmax): vp.z<=0 means the node is
//          outside the camera near/far range => culled, not "missing".
//        * a coarse scan for a black full-screen cover (Canvas + Image).
//
//   Entry points:  BWy.Api.Ping  /  BWy.Api.Deep  /  BWy.Api.Shot
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace BWy
{
    public static class Api
    {
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

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
            var f = o.GetType().GetField(name, ALL);
            return f != null ? f.GetValue(o) : null;
        }

        internal static int Coll(object o, string name)
        {
            var v = F(o, name) as ICollection;
            return v != null ? v.Count : -1;
        }

        /// <summary>Find a field on the object whose type name matches (type-agnostic lookup).</summary>
        internal static object FByTypeName(object o, string typeName)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                foreach (var f in t.GetFields(ALL))
                {
                    if (f.FieldType.Name == typeName) return f.GetValue(o);
                }
                t = t.BaseType;
            }
            return null;
        }

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

        private static string Nm(Component c)
        {
            return c == null ? "(null)" : c.gameObject.name;
        }

        /// <summary>One deep reading of "built block -> screen pixel".</summary>
        public static string Deep()
        {
            var sb = new StringBuilder();
            sb.Append("t=" + Time.unscaledTime.ToString("0.000"));
            sb.Append(" frame=" + Time.frameCount);

            var map = CtxMember("Map") as Diablo2.Module.IMapModule;
            if (map == null) return "BWY-DEEP no-MapModule";

            object view = null;
            var mt = map.GetType();
            while (mt != null)
            {
                var f = mt.GetField("_view", NP);
                if (f != null) { view = f.GetValue(map); break; }
                mt = mt.BaseType;
            }
            if (view == null) return "BWY-DEEP no-_view";

            var vcomp = view as Component;
            sb.Append(" goActive=" + (vcomp != null && vcomp.gameObject.activeInHierarchy ? 1 : 0));
            sb.Append(" showing=" + F(view, "_showing"));
            sb.Append(" groundKeys=" + Coll(view, "_groundChunks"));

            var gRoot = F(view, "_groundRoot") as Transform;
            var oRoot = F(view, "_objectRoot") as Transform;
            var vRoot = F(view, "_overlayRoot") as Transform;
            sb.Append(" roots=" + A(gRoot) + A(oRoot) + A(vRoot));
            if (gRoot == null) return "BWY-DEEP " + sb + " no-groundRoot";

            // ---- chunk roots under the ground layer ---------------------------
            var chunkTotal = gRoot.childCount;
            var chunkActive = 0;
            var chunkKid0 = new StringBuilder();
            for (var i = 0; i < chunkTotal && i < 40; i++)
            {
                var c = gRoot.GetChild(i);
                if (c.gameObject.activeInHierarchy) chunkActive++;
                else if (chunkKid0.Length < 120)
                {
                    if (chunkKid0.Length > 0) chunkKid0.Append(' ');
                    chunkKid0.Append(c.name);
                }
            }
            sb.Append(" chunkRoots=" + chunkTotal + " chunkActive=" + chunkActive);
            sb.Append(" chunkInactive=[" + chunkKid0 + "]");

            // ---- pool ---------------------------------------------------------
            var pool = FByTypeName(view, "TileNodePool");
            if (pool != null)
            {
                Type pt = pool.GetType();
                int freev = -1, cre = -1, reu = -1;
                var pf = pt.GetProperty("FreeCount", ALL); if (pf != null) freev = (int)pf.GetValue(pool);
                var pc = pt.GetProperty("CreatedCount", ALL); if (pc != null) cre = (int)pc.GetValue(pool);
                var pr = pt.GetProperty("ReusedCount", ALL); if (pr != null) reu = (int)pr.GetValue(pool);
                sb.Append(" pool free=" + freev + " created=" + cre + " reused=" + reu);
            }
            else sb.Append(" pool=(none)");

            // ---- every SpriteRenderer under the ground layer -------------------
            var cam = Camera.main;
            var all = UnityEngine.Object.FindObjectsByType(typeof(SpriteRenderer), FindObjectsSortMode.None);
            var tot = 0; var act = 0; var en = 0; var spok = 0; var alpha0 = 0;
            var bAny = new int[16]; var bRend = new int[16];
            var zmin = float.MaxValue; var zmax = float.MinValue;
            var nullSample = new StringBuilder();
            var orderMin = int.MaxValue; var orderMax = int.MinValue;
            var layerId = -1; var orders = new Dictionary<int, int>();
            var wxMin = float.MaxValue; var wxMax = float.MinValue;
            var wyMin = float.MaxValue; var wyMax = float.MinValue;

            foreach (var o in all)
            {
                var sr = o as SpriteRenderer;
                if (sr == null) continue;
                if (!sr.transform.IsChildOf(gRoot)) continue;
                tot++;
                var a = sr.gameObject.activeInHierarchy;
                var e = sr.enabled;
                var s = sr.sprite != null;
                if (a) act++;
                if (e) en++;
                if (s) spok++;
                else if (nullSample.Length < 80) { if (nullSample.Length > 0) nullSample.Append(' '); nullSample.Append(Nm(sr.transform.parent)); }
                if (sr.color.a <= 0.01f) alpha0++;

                var p = sr.transform.position;
                if (p.x < wxMin) wxMin = p.x; if (p.x > wxMax) wxMax = p.x;
                if (p.y < wyMin) wyMin = p.y; if (p.y > wyMax) wyMax = p.y;

                if (sr.sortingOrder < orderMin) orderMin = sr.sortingOrder;
                if (sr.sortingOrder > orderMax) orderMax = sr.sortingOrder;
                layerId = sr.sortingLayerID;
                int cnt; orders.TryGetValue(sr.sortingOrder, out cnt); orders[sr.sortingOrder] = cnt + 1;

                if (cam == null) continue;
                var vp = cam.WorldToViewportPoint(p);
                if (vp.z < zmin) zmin = vp.z;
                if (vp.z > zmax) zmax = vp.z;
                if (vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue;
                var bx = (int)(vp.x * 4f); if (bx > 3) bx = 3;
                var by = (int)(vp.y * 4f); if (by > 3) by = 3;
                var b = bx + by * 4;
                bAny[b]++;
                if (a && e && s && sr.color.a > 0.01f) bRend[b]++;
            }

            sb.Append(" SR tot=" + tot + " act=" + act + " en=" + en + " sprite=" + spok + " alpha0=" + alpha0);
            sb.Append(" nullSample=[" + nullSample + "]");
            sb.Append(" order=" + orderMin + ".." + orderMax + " layerId=" + layerId);
            sb.Append(" world=(" + wxMin.ToString("0.0") + "," + wyMin.ToString("0.0") + ")-(" + wxMax.ToString("0.0") + "," + wyMax.ToString("0.0") + ")");
            sb.Append(" vpZ=" + zmin.ToString("0.00") + ".." + zmax.ToString("0.00"));
            sb.Append(" bAny=" + Dump(bAny));
            sb.Append(" bRend=" + Dump(bRend));

            // ---- camera --------------------------------------------------------
            if (cam != null)
            {
                sb.Append(" cam=" + cam.transform.position.ToString("0.0"));
                sb.Append(" ortho=" + (cam.orthographic ? 1 : 0) + " size=" + cam.orthographicSize.ToString("0.00"));
                sb.Append(" near=" + cam.nearClipPlane.ToString("0.0") + " far=" + cam.farClipPlane.ToString("0.0"));
                sb.Append(" mask=" + cam.cullingMask);
                sb.Append(" screen=" + Screen.width + "x" + Screen.height);
            }
            else sb.Append(" cam=(none)");

            // ---- black full-screen cover scan (Canvas/Image) --------------------
            var covers = new StringBuilder();
            foreach (var cv in UnityEngine.Object.FindObjectsByType(typeof(Canvas), FindObjectsSortMode.None))
            {
                var c2 = cv as Canvas;
                if (c2 == null) continue;
                foreach (var img in c2.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    var rt = img.transform as RectTransform;
                    if (rt == null) continue;
                    var w = rt.rect.width; var h = rt.rect.height;
                    if (w < Screen.width * 0.75f || h < Screen.height * 0.75f) continue;
                    var col = img.color;
                    if (col.r + col.g + col.b > 0.12f) continue;
                    if (covers.Length > 0) covers.Append(' ');
                    covers.Append(img.gameObject.name + "/a" + col.a.ToString("0.00")
                                  + "/act" + (img.gameObject.activeInHierarchy ? 1 : 0));
                }
            }
            sb.Append(" blackFullscreenCover=[" + covers + "]");

            return "BWY-DEEP " + sb;
        }

        private static string A(Transform t)
        {
            return (t != null && t.gameObject.activeInHierarchy) ? "1" : "0";
        }

        private static string Dump(int[] a)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < a.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(a[i]);
            }
            return "[" + sb + "]";
        }
    }
}
