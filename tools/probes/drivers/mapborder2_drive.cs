// =============================================================================
// mapborder2_drive.cs -- 片 map-border2 的实机取证驱动（裁决第 3 条：城镇边界封环的画面）
//
//   WHY: 主 agent 二次裁决要求"玩家站在城镇最边上的可走格"的**实机图** + 读图判定
//        "画面里能看到多少地图外虚空"。离线 `mapcheck §32` 已给出 167/638 格、最大 1.1%，
//        但离线数字**不能冒充画面** ⇒ 必须进一次 Play，把机位摆到三个位置各拍一张。
//
//   WHAT IT DOES (boot 链**复用** automap_drive.cs / blackwhy_probe.cs 的既有写法，⛔ 不从零写)：
//        ① 反射把 FSM 走到 Stage（`_roster` Load 第 1 个存档 → `_selected` → `GoStage(Town)`）；
//        ② 等 `AppContext.I.Map.Generated`；
//        ③ 三个机位各：TeleportTo → 等 0.5s → 记一行（格 / 相机世界坐标 / **同口径地图外占比**）
//           → `ScreenCapture.CaptureScreenshot`；
//        ④ 写 done 标记 + 日志。
//
//   Entry: MapBorder2Drv.Api.Go(spec)   —— spec = "输出目录"（可空，默认 .ai-tmp/screenshots）
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace MapBorder2Drv
{
    public static class Api
    {
        internal const string Tag = "MB2";
        internal static string OutDir = string.Empty;

        internal static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else Debug.Log("[" + Tag + "] " + m);
        }

        internal static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else Debug.LogWarning("[" + Tag + "] " + m);
        }

        /// <summary>Entry: install the chain host. spec = "outDir" (optional).</summary>
        public static string Go(string spec)
        {
            OutDir = string.IsNullOrEmpty(spec)
                ? "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots"
                : spec;
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
            var go = new GameObject("MapBorder2DrvHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Host>();
            Log("INSTALL outDir=" + OutDir + " fsm=" + Fsm());
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
        private const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Want =
            { "营地内框贴边格", "最西侧可走格(紧邻已封树线)", "营地中央/出生点", "东边界接缝桥面(§24保护)" };

        private void Start() { StartCoroutine(Chain()); }

        private IEnumerator Chain()
        {
            var done = Api.OutDir + "/mapborder2_done.txt";
            var log = Api.OutDir + "/mapborder2_drive.log";
            if (File.Exists(done)) File.Delete(done);

            yield return new WaitForSeconds(1.5f);

            // ① boot 到 Stage（FSM 只允许 CharSelect -> GoStage；先按名触发走到 CharSelect）
            if (Api.Fsm() != "Stage")
            {
                if (!EnterStage()) { File.WriteAllText(done, "enter-failed fsm=" + Api.Fsm()); yield break; }
                var t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && Api.Fsm() != "Stage") yield return null;
                if (Api.Fsm() != "Stage") { File.WriteAllText(done, "enter-timeout fsm=" + Api.Fsm()); yield break; }
            }

            // ② 等地图生成完（Stage 里 MapModule 可能在 Loading 中）
            var t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 60f)
            {
                var m0 = CtxMap();
                // `IMapModule` 上没有 `Generated`（那是 GridMap 的）⇒ 用「已铺出可走格」当就绪信号
                if (m0 != null && m0.Width > 0 && m0.WalkableCount > 0 && CtxPlayer() != null) break;
                yield return null;
            }

            // ⛔ `Diablo2.App.AppContext` 是 **internal**（`public` 只在同程序集内可见）⇒ 只能反射拿
            //    （同 `blackwhy_probe.cs` 的 `Ctx()/CtxMember()` 写法，不是自创）。
            var map = CtxMap();
            var player = CtxPlayer();
            if (map == null || player == null)
            {
                File.WriteAllText(done, "no-context map=" + (map != null) + " player=" + (player != null));
                yield break;
            }
            Write(log, "STAGE area=" + map.Area + " size=" + map.Width + "x" + map.Height
                       + " walkable=" + map.WalkableCount + " spawn=" + map.SpawnPoint
                       + " playerGrid=" + player.Grid + " fsm=" + Api.Fsm());

            var halfW = 0f;
            var halfH = 0f;
            var cam = Camera.main;
            if (cam != null)
            {
                halfW = cam.orthographicSize * ((float)Screen.width / Screen.height);
                halfH = cam.orthographicSize;
                Write(log, "CAM ortho=" + cam.orthographicSize.ToString("0.###") + " screen=" + Screen.width + "x"
                           + Screen.height + " halfW=" + halfW.ToString("0.###") + " halfH=" + halfH.ToString("0.###"));
            }
            else
            {
                Api.Warn("NO-Camera.main ⇒ 地图外占比无法现算（只拍图）");
            }

            // ③ 三个机位
            var targets = new List<Vector2Int>();
            var campEdge = CampFrameEdgeWalkable(map);
            if (campEdge.HasValue) targets.Add(campEdge.Value);   // ① 营地内框最贴边可走格
            targets.Add(WestmostWalkable(map));                   // ② 玩家能走到的最外侧（营地外）可走格
            targets.Add(map.SpawnPoint);                          // ③ 营地中央（正常游玩位置）
            targets.Add(new Vector2Int(55, 27));                  // ④ 东边界接缝桥面（§24 保护、d=0 的残留）

            for (var i = 0; i < targets.Count; i++)
            {
                player.TeleportTo(targets[i]);
                var t2 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t2 < 1.2f) yield return null;   // 让相机跟随收敛

                var grid = player.Grid;
                var camPos = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
                float fracRaw = -1f, fracClamped = -1f;
                var clamped = new Vector2(camPos.x, camPos.y);
                if (Camera.main != null)
                {
                    fracRaw = OffMapFraction(camPos, halfW, halfH, map.Width, map.Height);
                    clamped = CameraBounds.ClampFocusGrid(new Vector2(camPos.x, camPos.y),
                        map.Width, map.Height, halfW, halfH);
                    fracClamped = OffMapFraction(new Vector3(clamped.x, clamped.y, camPos.z),
                        halfW, halfH, map.Width, map.Height);
                }
                Write(log, "CASE " + i + " " + Want[i] + " target=" + targets[i] + " playerGrid=" + grid
                           + " campInterior=" + IsCampInterior(grid)
                           + " cam=" + Fmt(camPos) + " clampDelta=" + Fmt(new Vector3(clamped.x - camPos.x, clamped.y - camPos.y, 0f))
                           + " offMapRaw=" + Pct(fracRaw) + " offMapClamped=" + Pct(fracClamped)
                           + " clamped==cam=" + (Mathf.Abs(clamped.x - camPos.x) < 1e-3f && Mathf.Abs(clamped.y - camPos.y) < 1e-3f));

                var file = Api.OutDir + "/mapborder2_case" + i + ".png";
                if (File.Exists(file)) File.Delete(file);
                ScreenCapture.CaptureScreenshot(file);
                Write(log, "SHOT case=" + i + " file=" + file + " frame=" + Time.frameCount);
                yield return new WaitForSeconds(0.8f);
            }

            Write(log, "CHAIN-COMPLETE shots=" + targets.Count);
            File.WriteAllText(done, "ok shots=" + targets.Count);
        }

        // ---- 机位挑选 ------------------------------------------------------
        /// <summary>营地内框里"最贴边"的可走格（d 最小；并列取 x+y 最小 = 稳定）。</summary>
        private static Vector2Int? CampFrameEdgeWalkable(IMapModule map)
        {
            Vector2Int? best = null;
            var bestD = int.MaxValue;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    if (!IsCampInterior(g)) continue;
                    var d = Mathf.Min(Mathf.Min(x, map.Width - 1 - x), Mathf.Min(y, map.Height - 1 - y));
                    if (d < bestD) { bestD = d; best = g; }
                }
            }
            Api.Log("CAMP-EDGE pick=" + (best.HasValue ? best.Value.ToString() : "(none)") + " d=" + bestD);
            return best;
        }

        /// <summary>最小 x 的可走格（并列取离营地西门 y=27 最近的）= 玩家能走到的最外侧。</summary>
        private static Vector2Int WestmostWalkable(IMapModule map)
        {
            var minX = int.MaxValue;
            var best = Vector2Int.zero;
            var bestDy = int.MaxValue;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g)) continue;
                    var dy = Mathf.Abs(y - 27);
                    if (x < minX || (x == minX && dy < bestDy)) { minX = x; bestDy = dy; best = g; }
                }
            }
            Api.Log("WESTMOST pick=" + best + " x=" + minX + " d=" +
                    Mathf.Min(Mathf.Min(best.x, map.Width - 1 - best.x), Mathf.Min(best.y, map.Height - 1 - best.y)));
            return best;
        }

        /// <summary>离 (x,y) 最近的可走格（环形搜索）。</summary>
        private static Vector2Int NearestWalkable(IMapModule map, int cx, int cy)
        {
            for (var r = 0; r < 30; r++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    for (var dy = -r; dy <= r; dy++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                        var g = new Vector2Int(cx + dx, cy + dy);
                        if (map.Walkable(g)) return g;
                    }
                }
            }
            Api.Warn("NEAREST-FAIL around (" + cx + "," + cy + ")");
            return map.SpawnPoint;
        }

        private static bool IsCampInterior(Vector2Int g) => g.x > 17 && g.x < 47 && g.y > 16 && g.y < 39;

        // ---- 与 `mapcheck §31/§32` 同口径的"屏幕里地图外占比" ---------------
        internal static float OffMapFraction(Vector3 cam, float halfW, float halfH, int mw, int mh)
        {
            const int n = 96;
            var off = 0;
            var tot = 0;
            for (var i = 0; i < n; i++)
            {
                for (var j = 0; j < n; j++)
                {
                    var wx = cam.x - halfW + 2f * halfW * (i / (float)(n - 1));
                    var wy = cam.y - halfH + 2f * halfH * (j / (float)(n - 1));
                    var g = Iso.WorldToGridContinuous(new Vector3(wx, wy, 0f));
                    tot++;
                    const float eps = 1e-3f;
                    if (g.x < -eps || g.y < -eps || g.x > mw - 1 + eps || g.y > mh - 1 + eps) off++;
                }
            }
            return tot == 0 ? 0f : off / (float)tot;
        }

        private static string Pct(float f) => f < 0f ? "(n/a)" : (f * 100f).ToString("0.#") + "%";
        private static string Fmt(Vector3 v) => "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + ")";
        private static void Write(string path, string line)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); } catch { }
            Api.Log(line);
        }

        // ---- boot（复用 automap_drive.cs 的既有链）-------------------------
        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        /// <summary>`Diablo2.App.AppContext.I`（internal 类型 ⇒ 反射；同 `blackwhy_probe.cs`）。</summary>
        private static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) { return null; }
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p != null ? p.GetValue(null, null) : null;
        }

        /// <summary>取 AppContext 的公开字段（Map / Player / Flow …）。</summary>
        private static object CtxMember(string name)
        {
            var c = Ctx();
            if (c == null) { return null; }
            var f = c.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(c) : null;
        }

        private static IMapModule CtxMap() { return CtxMember("Map") as IMapModule; }
        private static IPlayerModule CtxPlayer() { return CtxMember("Player") as IPlayerModule; }

        private static object Member(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            var fi = t.GetField(name, ALL);
            if (fi != null) return fi.GetValue(o);
            var pi = t.GetProperty(name, ALL);
            return pi != null ? pi.GetValue(o, null) : null;
        }

        private static void SetMember(object o, string name, object v)
        {
            var t = o.GetType();
            var fi = t.GetField(name, ALL);
            if (fi != null) { fi.SetValue(o, v); return; }
            var pi = t.GetProperty(name, ALL);
            if (pi != null && pi.CanWrite) pi.SetValue(v, null);
        }

        private bool EnterStage()
        {
            if (Api.Fsm() == "Stage") return true;
            var flow = CtxMember("Flow");
            if (flow == null) { Api.Warn("NO-FLOW"); return false; }
            var ft = flow.GetType();

            var selF = ft.GetField("_selected", BindingFlags.Instance | BindingFlags.NonPublic);
            if (selF == null) { Api.Warn("NO-_selected"); return false; }
            if (selF.GetValue(flow) == null)
            {
                var roster = Member(flow, "_roster");
                if (roster == null) { Api.Warn("NO-_roster"); return false; }
                var rt = roster.GetType();
                var listM = rt.GetMethod("ListAll", ALL) ?? rt.GetMethod("List", ALL);
                string pick = null;
                var n = 0;
                if (listM != null)
                {
                    var items = listM.Invoke(roster, null) as IEnumerable;
                    if (items != null)
                        foreach (var it in items)
                        {
                            n++;
                            if (pick == null && it != null)
                            {
                                pick = it as string;
                                if (pick == null) pick = Member(it, "name") as string;
                            }
                        }
                }
                Api.Log("ROSTER count=" + n + " pick=" + (pick ?? "(none)"));
                if (string.IsNullOrEmpty(pick)) { Api.Warn("ROSTER-EMPTY"); return false; }
                var loadM = rt.GetMethod("Load", ALL);
                if (loadM == null) { Api.Warn("NO-Load"); return false; }
                var save = loadM.Invoke(roster, new object[] { pick });
                if (save == null) { Api.Warn("LOAD-NULL " + pick); return false; }
                selF.SetValue(flow, save);
                Api.Log("SELECTED " + pick);
            }

            AdvanceFsmToCharSelect();
            var goM = ft.GetMethod("GoStage", ALL);
            if (goM == null) { Api.Warn("NO-GoStage"); return false; }
            goM.Invoke(flow, new object[] { AreaId.Town });
            Api.Log("ENTER-ISSUED area=Town fsm=" + Api.Fsm());
            return true;
        }

        private static void AdvanceFsmToCharSelect()
        {
            var cur = Api.Fsm();
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
                    var now = Api.Fsm();
                    if (now == "CharSelect" || now == "Stage") return;
                    try
                    {
                        Game.Fsm.Trigger((string)f.GetValue(null));
                        Api.Log("TRIGGER " + f.Name + " -> fsm=" + Api.Fsm());
                    }
                    catch (Exception ex) { Api.Warn("TRIGGER-FAIL " + f.Name + " " + ex.GetType().Name); }
                }
            }
        }
    }
}
