// =============================================================================
//
//   WHY a live session: "the void is not inside the view" and "the player is
//   still on screen while standing on the outermost walkable cell" are VISUAL /
//   live-state judgements; the offline hosts can only assert the pure math.
//   The numbers printed here use the SAME definitions as
//   `tools/probes/hosts/playercheck` 11.11 (vp = 0.5 + delta/(2*half)) so the two
//   halves of the evidence cannot drift apart.
//
//   Entry points (each call is self-contained: run_script compiles the file into
//   a fresh assembly per call, so NO static state may be relied upon):
//     CV.Api.Ping            map/player/camera + view numbers
//     CV.Api.Freeze          destroy the S2 tour driver (it keeps steering the
//                            player/camera and would overwrite every placement)
//     CV.Api.Sweep           outermost WALKABLE cell in 8 directions (the walkable
//                            ring is inset by Module/Map, so the map corner is NOT
//                            reachable any more)
//     CV.Api.Place "gx,gy"   teleport + snap + tick => the production framing
//     CV.Api.PinOn "gx,gy"   pin the camera to the UNCLAMPED focus in LateUpdate
//                            (the pre-fix framing: world AABB clamp was a no-op there)
//     CV.Api.PinOff          release the pin
//     CV.Api.Travel "area"   emit Events.ExitEntered (the one legitimate area-switch
//                            chain; AppWaypoint ends with exactly this call)
//
//   Product types (AppContext / MapView) are internal => reflection. Only the
//   public contracts + UnityEngine are referenced directly.
//   Screenshots reuse BWy.Api.Shot (tools/probes/drivers/blackwhy_probe.cs).
//   ASCII ONLY (run_script feeds this file through the CLI).
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace CV
{
    public static class Api
    {
        private const BindingFlags ALL = BindingFlags.Public | BindingFlags.NonPublic
                                       | BindingFlags.Instance | BindingFlags.Static;

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t;
                try { t = asm.GetType(full); } catch { t = null; }
                if (t != null) return t;
            }
            return null;
        }

        private static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", ALL);
            return p != null ? p.GetValue(null) : null;
        }

        private static object Member(object ctx, string name)
        {
            if (ctx == null) return null;
            var f = ctx.GetType().GetField(name, ALL);
            return f != null ? f.GetValue(ctx) : null;
        }

        private static Diablo2.Module.IMapModule Map() { return Member(Ctx(), "Map") as Diablo2.Module.IMapModule; }
        private static Diablo2.Module.IPlayerModule Player() { return Member(Ctx(), "Player") as Diablo2.Module.IPlayerModule; }
        private static Diablo2.Module.ICameraRig Rig() { return Member(Ctx(), "Camera") as Diablo2.Module.ICameraRig; }

        private static string F(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }
        private static string F3(float f) { return f.ToString("0.000", CultureInfo.InvariantCulture); }
        private static UnityEngine.Camera Cam() { return UnityEngine.Camera.main; }

        // ---- MapView reflection (chunked path readings) ----------------------
        private static object ViewOfMap(Diablo2.Module.IMapModule map)
        {
            if (map == null) return null;
            var f = map.GetType().GetField("_view", BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(map) : null;
        }

        private static object ViewField(object view, string name)
        {
            if (view == null) return null;
            var f = view.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(view) : null;
        }

        private static int BuiltGround(object view)
        {
            var d = ViewField(view, "_groundChunks") as IDictionary;
            return d == null ? -1 : d.Count;
        }

        /// <summary>
        /// Off-map fraction over the visible rect, two conventions:
        ///   offSpan = outside [0,W]x[0,H]  (the continuous span production clamps to)
        ///   offTile = outside [0,W)x[0,H)  (where a ground TILE actually exists == the
        ///             physically meaningful "void"/black test)
        /// gridrect = continuous grid bbox of the four screen corners.
        /// </summary>
        private static string ViewNumbers(UnityEngine.Camera cam, int mw, int mh)
        {
            if (cam == null || mw <= 0 || mh <= 0) return "view=(no-camera-or-map)";
            var halfH = cam.orthographicSize;
            var halfW = halfH * cam.aspect;
            var c = cam.transform.position;
            var lx = float.MaxValue; var ly = float.MaxValue;
            var hx = float.MinValue; var hy = float.MinValue;
            for (var i = 0; i < 2; i++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var g = Iso.WorldToGridContinuous(new Vector3(
                        c.x + (i == 0 ? -halfW : halfW), c.y + (j == 0 ? -halfH : halfH), 0f));
                    if (g.x < lx) lx = g.x;
                    if (g.x > hx) hx = g.x;
                    if (g.y < ly) ly = g.y;
                    if (g.y > hy) hy = g.y;
                }
            }
            var offSpan = 0; var offTile = 0; var tot = 0;
            const int n = 160;
            const float eps = 1e-3f;
            for (var a = 0; a < n; a++)
            {
                for (var b = 0; b < n; b++)
                {
                    var wx = c.x - halfW + 2f * halfW * (a / (float)(n - 1));
                    var wy = c.y - halfH + 2f * halfH * (b / (float)(n - 1));
                    var g = Iso.WorldToGridContinuous(new Vector3(wx, wy, 0f));
                    tot++;
                    if (g.x < -eps || g.y < -eps || g.x > mw + eps || g.y > mh + eps) offSpan++;
                    if (g.x < -eps || g.y < -eps || g.x >= mw - eps || g.y >= mh - eps) offTile++;
                }
            }
            return "gridrect=[(" + F(lx) + "," + F(ly) + ")..(" + F(hx) + "," + F(hy) + ")]"
                 + " offSpan=" + F(100f * offSpan / tot) + "% offTile=" + F(100f * offTile / tot) + "%";
        }

        /// <summary>player-vs-camera in screen space (the SAME convention as playercheck 11.11).</summary>
        private static string FollowNumbers(UnityEngine.Camera cam, Vector3 pw)
        {
            if (cam == null) return "follow=(no-camera)";
            var halfH = cam.orthographicSize;
            var halfW = halfH * cam.aspect;
            var c = cam.transform.position;
            var dx = pw.x - c.x;
            var dy = pw.y - c.y;
            var vx = 0.5f + dx / (2f * halfW);
            var vy = 0.5f + dy / (2f * halfH);
            var sx = (vx - 0.5f) * Screen.width;
            var sy = (vy - 0.5f) * Screen.height;
            var dist = Mathf.Sqrt(sx * sx + sy * sy);
            var margin = Mathf.Min(Mathf.Min(vx, 1f - vx), Mathf.Min(vy, 1f - vy));
            var inView = (vx >= 0f && vx <= 1f && vy >= 0f && vy <= 1f) ? 1 : 0;
            return "d=(" + F(dx) + "," + F(dy) + ")"
                 + " vp=(" + F3(vx) + "," + F3(vy) + ")"
                 + " scrOffset=(" + F3((vx - 0.5f) * 2f) + "," + F3((vy - 0.5f) * 2f) + ")xHalf"
                 + " screenPx=(" + F(sx) + "," + F(sy) + ") dist=" + F(dist) + "px"
                 + " margin=" + F3(margin) + " inViewport=" + inView;
        }

        private static string Read(string where)
        {
            var map = Map();
            var pl = Player();
            var cam = Cam();
            var view = ViewOfMap(map);
            var mw = map != null ? map.Width : 0;
            var mh = map != null ? map.Height : 0;
            var pw = pl != null ? pl.World : Vector3.zero;
            var c = cam != null ? cam.transform.position : Vector3.zero;
            var chunked = ViewField(view, "_chunked");
            return where
                 + " area=" + (map != null ? ((int)map.Area).ToString(CultureInfo.InvariantCulture) : "?")
                 + " map=" + mw + "x" + mh
                 + " grid=" + (pl != null ? pl.Grid.ToString() : "?")
                 + " pw=(" + F(pw.x) + "," + F(pw.y) + ")"
                 + " cam=(" + F(c.x) + "," + F(c.y) + ")"
                 + " " + FollowNumbers(cam, pw)
                 + " " + ViewNumbers(cam, mw, mh)
                 + " chunked=" + (chunked is bool && (bool)chunked ? 1 : 0)
                 + " chunkMin=" + ViewField(view, "_chunkMin") + " chunkMax=" + ViewField(view, "_chunkMax")
                 + " builtGround=" + BuiltGround(view);
        }

        public static string Ping()
        {
            var map = Map();
            return "PING map=" + (map != null && map.IsGenerated ? 1 : 0)
                 + " " + Read("READ");
        }

        /// <summary>
        /// Stop the S2 tour driver: it keeps steering player/camera every frame.
        /// NOTE parameterless on purpose: run_script answers "Bad Request" for an entry
        /// that declares a `string` parameter but is called without --args.
        /// </summary>
        public static string Freeze()
        {
            var n = 0;
            var all = UnityEngine.Object.FindObjectsByType<GameObject>();
            foreach (var go in all)
            {
                if (go != null && go.name == "S2EvidenceDriver")
                {
                    UnityEngine.Object.Destroy(go);
                    n++;
                }
            }
            return "FREEZE removed=" + n;
        }

        /// <summary>Outermost WALKABLE cell in 8 directions (the walkable ring is inset by Module/Map).</summary>
        public static string Sweep()
        {
            var map = Map();
            if (map == null || !map.IsGenerated) return "SWEEP no-map";
            var w = map.Width; var h = map.Height;
            var cells = new List<Vector2Int>();
            for (var x = 0; x < w; x++)
            {
                for (var y = 0; y < h; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (map.Walkable(g)) cells.Add(g);
                }
            }
            var gx0 = int.MaxValue; var gx1 = int.MinValue;
            var gy0 = int.MaxValue; var gy1 = int.MinValue;
            foreach (var g in cells)
            {
                if (g.x < gx0) gx0 = g.x;
                if (g.x > gx1) gx1 = g.x;
                if (g.y < gy0) gy0 = g.y;
                if (g.y > gy1) gy1 = g.y;
            }
            var midX = (w - 1) / 2; var midY = (h - 1) / 2;
            var s = "SWEEP map=" + w + "x" + h + " walkable=" + cells.Count
                  + " outerGx=[" + gx0 + ".." + gx1 + "] outerGy=[" + gy0 + ".." + gy1 + "]"
                  + " ringFromEdge=" + Mathf.Min(Mathf.Min(gx0, gy0), Mathf.Min(w - 1 - gx1, h - 1 - gy1));
            s += " | mm=" + Pick(cells, c => (long)c.x * 100000L + c.y)                          // min gx, min gy
               + " | mM=" + Pick(cells, c => (long)c.x * 100000L + (99999L - c.y))               // min gx, max gy
               + " | Mm=" + Pick(cells, c => (99999L - c.x) * 100000L + c.y)                     // max gx, min gy
               + " | MM=" + Pick(cells, c => (99999L - c.x) * 100000L + (99999L - c.y))          // max gx, max gy
               + " | Wmid=" + Pick(cells, c => (long)c.x * 100000L + Mathf.Abs(c.y - midY))      // min gx, mid gy
               + " | Emid=" + Pick(cells, c => (99999L - c.x) * 100000L + Mathf.Abs(c.y - midY))
               + " | Nmid=" + Pick(cells, c => (long)c.y * 100000L + Mathf.Abs(c.x - midX))      // min gy, mid gx
               + " | Smid=" + Pick(cells, c => (99999L - c.y) * 100000L + Mathf.Abs(c.x - midX));
            return s;
        }

        private static string Pick(List<Vector2Int> cells, Func<Vector2Int, long> key)
        {
            if (cells.Count == 0) return "none";
            var best = cells[0];
            var bestKey = key(best);
            for (var i = 1; i < cells.Count; i++)
            {
                var k = key(cells[i]);
                if (k < bestKey) { bestKey = k; best = cells[i]; }
            }
            return best.x + "," + best.y;
        }

        /// <summary>Teleport to (gx,gy) + snap the camera + tick => the production framing.</summary>
        public static string Place(string spec)
        {
            var map = Map();
            var pl = Player();
            var rig = Rig();
            if (map == null || !map.IsGenerated) return "PLACE no-map";
            if (pl == null || rig == null) return "PLACE no-player-or-rig";
            var p = (spec ?? "0,0").Split(',');
            var gx = 0; var gy = 0;
            int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out gx);
            if (p.Length > 1) int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out gy);
            var g = new Vector2Int(gx, gy);
            PinOff();
            pl.TeleportTo(g);
            rig.SetTargetGrid(g);
            rig.SnapToTarget();
            for (var i = 0; i < 10; i++) rig.Tick(0.016f);
            return "PLACE asked=" + g.ToString() + " walkable=" + (map.Walkable(g) ? 1 : 0) + " " + Read("READ");
        }

        /// <summary>
        /// Pin the camera to the UNCLAMPED focus (= "完全跟随" / cap=0 framing: the camera sits on the
        /// player, so whatever void exists at that cell is fully visible). Compare with the production
        /// reading at the same cell, which clamps the camera so the visible grid rect stays inside the map.
        /// </summary>
        public static string PinOn()
        {
            var pl = Player();
            var map = Map();
            var cam = Cam();
            if (pl == null || cam == null) return "PIN no-player-or-camera";
            var w = Iso.GridToWorld(pl.Grid);
            var z = cam.transform.position.z;
            var go = new GameObject("CVCamPin");
            var pin = go.AddComponent<CamPin>();
            pin.Pos = new Vector3(w.x, w.y, z);
            cam.transform.position = pin.Pos;
            return "PIN-ON pos=(" + F(pin.Pos.x) + "," + F(pin.Pos.y) + ") "
                 + Read("READ");
        }

        public static string PinOff()
        {
            var n = 0;
            var found = UnityEngine.Object.FindObjectsByType<CamPin>();
            foreach (var c in found)
            {
                UnityEngine.Object.Destroy(c.gameObject);
                n++;
            }
            return "PIN-OFF removed=" + n;
        }

        /// <summary>Switch area over the one legitimate chain (what AppWaypoint emits).</summary>
        public static string Travel(string spec)
        {
            var map = Map();
            if (map == null) return "TRAVEL no-map";
            var area = 0;
            int.TryParse(spec, NumberStyles.Integer, CultureInfo.InvariantCulture, out area);
            if (Game.Event == null) return "TRAVEL no-event-bus";
            Game.Event.Emit<AreaId>(Events.ExitEntered, (AreaId)area);
            return "TRAVEL emitted=" + Events.ExitEntered + " dest=" + area;
        }
    }

    public class CamPin : MonoBehaviour
    {
        public Vector3 Pos;

        private void LateUpdate()
        {
            var cam = UnityEngine.Camera.main;
            if (cam != null) cam.transform.position = Pos;
        }
    }
}
