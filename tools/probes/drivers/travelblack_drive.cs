// =============================================================================
// travelblack_drive.cs -- 片 travel-black 的实机取证 + 定案驱动
//   （主题：**传送落地整屏黑** 是不是"整图重铺窗口"，以及窗口有多长）
//
//   WHY: play-verify 已证明传送逻辑全绿（`CHK ... area 0->1 ok=1`），但落地那张图
//        `pv_wp_4_arrived.png` 是**整屏黑**。chunk-ctl / chunk-hole2 已把"进区黑窗"
//        钉到 MapView 的 T0FIX-H 分帧双缓冲，但**从未量过"落地那一刻"**：
//          · 是**旧区画面**撑不到新相机位置（交换前就黑）？
//          · 还是**交换那一刻**新集不含相机可见块（交换后才黑）？
//        两者修法完全不同 ⇒ 必须先分帧量出来，⛔ 不许猜。
//
//   WHAT IT DOES（boot 链 = **复用** mapborder2_drive.cs / automap_drive.cs 的既有写法；
//                 读数反射 = **复用** chunkhole_probe.cs 的同名字段口径，⛔ 不从零写）：
//     ① 反射把 FSM 走到 Stage（`_roster` Load 第 1 个存档 → `_selected` → `GoStage(Town)`）；
//     ② 等地图生成（`WalkableCount>0` 且 `Ctx.Player != null`）；
//     ③ 探针把目标区域塞进 `AppWaypoint.Visited`（否则面板/请求都会拒绝"未激活目的地"，
//        这一段**只改探针侧静态集合**，产品代码一词不动；日志记 `PROBE-VISITED-ADD`）；
//     ④ `MoveCommand(锚点)` 走过去 ⇒ 面板开（玩家真实路径）；
//     ⑤ 发 `Events.WaypointTravelRequest(dest)` —— 与面板按钮**同一个事件**（AppWaypoint 收它才发
//        `ExitEntered`）；⛔ 未做真鼠标点击（那条链已由 play-verify 实机证绿，本片要的是落地读数）；
//     ⑥ **落地那一帧起**逐帧采样（8 s 或直到 `job=null && missAct=0` 且三张图都拍完）：
//        `job/idx/chunks` · `ground/bufGround/pending/retire` · 期望可见块范围（与生产同公式）
//        · `actCov`（可见块里**当前生效集**已建的块数）· `bufCov`（缓冲集）· `missAct`（屏上没块的块数）
//        · 相机世界坐标/格坐标 · 玩家格 · 生效集的键范围；
//     ⑦ 三张图：落地帧（+0s）/ +0.5s / +2.0s（`ScreenCapture.CaptureScreenshot`）。
//
//   判据（**动手前**就定死）：
//     · 若"交换前"就有若干帧 `actCov=0` ⇒ 根因 = **旧区画面撑不到新相机位置**（旧图范围与新区不符）；
//     · 若"交换那一帧"起 `actCov` 从 total 掉到 0、随后靠 `pending`/逐块补齐爬回 ⇒
//       根因 = **交换后的新集不含相机可见块**（登记范围是**落地前**那台相机算出来的）；
//     · 若两者都不成立而仍黑 ⇒ 如实写"假设不成立 + 测到的东西"。
//
//   Entry: TBlackDrv.Api.Go(spec)   spec = "<outDir>|<tag>"
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace TBlackDrv
{
    public static class Api
    {
        internal const string Tag = "TBLACK";
        internal static string OutDir = "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots";
        internal static string TagName = "travelblack";

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

        internal static string Fsm()
        {
            try { return Game.Fsm != null ? Game.Fsm.Current : "(no-fsm)"; }
            catch (Exception ex) { return "(fsm-" + ex.GetType().Name + ")"; }
        }

        /// <summary>
        /// 编译闸门 + 就绪自检（进 Play 前调一次，`Go` 之前调）。作用有二：
        /// ① `run_script` 会把本文件整份编译 ⇒ 任何 CS 错误在这里就暴露（不污染 Play 会话）；
        /// ② 报出会话是否已就绪（`AppContext.I` / Game.Res / Logger），并发同事搅掉会话时能看出来。
        /// </summary>
        public static string Ping()
        {
            var ctx = FindTypeStatic("Diablo2.App.AppContext") != null ? 1 : 0;
            var res = Game.Res != null ? 1 : 0;
            var logger = Game.Logger != null ? 1 : 0;
            return "PONG fsm=" + Fsm() + " ctxType=" + ctx + " res=" + res + " logger=" + logger
                   + " frame=" + Time.frameCount;
        }

        private static Type FindTypeStatic(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        /// <summary>Entry: install the chain host. spec = "&lt;outDir&gt;|&lt;tag&gt;".</summary>
        public static string Go(string spec)
        {
            if (!string.IsNullOrEmpty(spec))
            {
                var parts = spec.Split('|');
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0])) OutDir = parts[0];
                if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1])) TagName = parts[1];
            }
            if (!Directory.Exists(OutDir)) Directory.CreateDirectory(OutDir);
            var go = new GameObject("TravelBlackDrvHost");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Host>();
            Log("INSTALL outDir=" + OutDir + " tag=" + TagName + " fsm=" + Fsm());
            return "installed";
        }
    }

    internal sealed class Host : MonoBehaviour
    {
        private const BindingFlags ALL = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        private const int ChunkSize = 16;              // mirrors MapView.ChunkSize

        private string _done;
        private string _log;
        private string _shots;

        /// <summary>落地帧的时间戳（0 = 还没落地）。</summary>
        private float _landAt = -1f;
        private bool _shot0, _shot05, _shot2;

        private void Start() { StartCoroutine(Chain()); }

        private IEnumerator Chain()
        {
            _done = Api.OutDir + "/travelblack_done_" + Api.TagName + ".txt";
            _log = Api.OutDir + "/travelblack_" + Api.TagName + ".log";
            _shots = Api.OutDir;
            if (File.Exists(_done)) File.Delete(_done);

            // 并发同事会搅掉未就绪的会话（ui-fix4 的教训）⇒ 等 `AppContext.I` 与 FSM 真的就位
            var tW = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tW < 40f && (Ctx() == null || Api.Fsm().StartsWith("(")))
                yield return null;
            Write("READY fsm=" + Api.Fsm() + " ctx=" + (Ctx() != null ? 1 : 0));
            yield return new WaitForSeconds(1.5f);

            // ① boot 到 Stage（复用 mapborder2_drive.cs 的同一条链）
            if (Api.Fsm() != "Stage")
            {
                if (!EnterStage()) { Fail("enter-failed fsm=" + Api.Fsm()); yield break; }
                var t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 60f && Api.Fsm() != "Stage") yield return null;
                if (Api.Fsm() != "Stage") { Fail("enter-timeout fsm=" + Api.Fsm()); yield break; }
            }

            // ② 等地图生成
            var t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 60f)
            {
                var m0 = CtxMap();
                if (m0 != null && m0.Width > 0 && m0.WalkableCount > 0 && CtxPlayer() != null) break;
                yield return null;
            }

            var map = CtxMap();
            var player = CtxPlayer();
            if (map == null || player == null) { Fail("no-context"); yield break; }

            Write("STAGE fsm=" + Api.Fsm() + " area=" + (int)map.Area + " size=" + map.Width + "x" + map.Height
                  + " walkable=" + map.WalkableCount + " spawn=" + Fmt(map.SpawnPoint)
                  + " player=" + Fmt(player.Grid));

            // ③ 探针把"另一个区域"塞进 Visited（面板/请求都要求"已激活"；只动探针侧集合）
            var dest = PickDest((int)map.Area);
            if (dest < 0) { Fail("no-dest"); yield break; }
            var added = MarkVisited(dest);
            Write("PROBE-VISITED-ADD dest=" + dest + " ok=" + added
                  + "（⛔ 只改探针侧静态集合 `AppWaypoint.Visited`，产品代码未动）");

            // ④ 走到传送点锚点（玩家真实路径：MoveCommand ⇒ AppWaypoint 判格 ⇒ 到 8 邻开面板）
            var pts = map.WaypointPoints;
            if (pts == null || pts.Count == 0) { Fail("no-waypoint"); yield break; }
            var wp = pts[0];
            Write("WALK-WP area=" + (int)map.Area + " anchor=" + Fmt(wp) + " player=" + Fmt(player.Grid));
            Emit(Diablo2.Core.Events.MoveCommand, wp);

            var t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < 30f && !PanelOpen()) yield return null;
            Write("WP-PANEL open=" + (PanelOpen() ? 1 : 0) + " player=" + Fmt(player.Grid)
                  + " dist=" + Chebyshev(player.Grid, wp) + "（面板打开 = 玩家真实路径已走通）");
            if (!PanelOpen()) { Fail("panel-never-opened"); yield break; }

            // ⑤ 传送前的最后一份读数（旧区 / 旧集 / 旧相机）
            Write("PRE  " + ChunkLine());

            // ⑥ 发面板按钮的同一个事件（AppWaypoint.OnTravelRequest 收它）
            var oldArea = (int)map.Area;
            Api.Log("TRAVEL-REQ dest=" + dest + " from=" + oldArea
                    + "（与 WaypointPanel 按钮**同一个事件** " + Diablo2.Core.Events.WaypointTravelRequest + "）");
            Emit(Diablo2.Core.Events.WaypointTravelRequest, dest);

            // ⑦ 落地帧起逐帧采样
            var t3 = Time.realtimeSinceStartup;
            var landed = (int)map.Area == dest;
            if (landed) { _landAt = Time.realtimeSinceStartup; }
            while (Time.realtimeSinceStartup - t3 < 12f)
            {
                var now = (int)map.Area;
                if (!landed && now == dest)
                {
                    landed = true;
                    _landAt = Time.realtimeSinceStartup;
                    Api.Log("LANDED frame=" + Time.frameCount + " area " + oldArea + "->" + now);
                }

                if (landed && !_shot0) { _shot0 = true; Shot("travelblack_" + Api.TagName + "_1_landed.png"); }
                if (landed && !_shot05 && Time.realtimeSinceStartup - _landAt >= 0.5f)
                { _shot05 = true; Shot("travelblack_" + Api.TagName + "_2_p05.png"); }
                if (landed && !_shot2 && Time.realtimeSinceStartup - _landAt >= 2.0f)
                { _shot2 = true; Shot("travelblack_" + Api.TagName + "_3_p20.png"); }

                Write((landed ? "F" : "p") + Rel() + " " + ChunkLine());

                if (landed && _shot2 && Time.realtimeSinceStartup - _landAt >= 2.6f && Settled()) break;
                yield return null;
            }

            // ⑧ 收尾：再等 2 s 确认终态并写 SUMMARY
            var t4 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t4 < 2f) yield return null;
            Write("END  " + ChunkLine());
            Write("SUMMARY area=" + (int)map.Area + " dest=" + dest + " landed=" + landed
                  + " shots=" + (_shot0 ? 1 : 0) + (_shot05 ? 1 : 0) + (_shot2 ? 1 : 0)
                  + " settled=" + Settled());
            File.WriteAllText(_done, "ok landed=" + landed + " area=" + (int)map.Area);
            Api.Log("DONE area=" + (int)map.Area + " landed=" + landed);
        }

        private void Fail(string why)
        {
            Api.Warn("CHAIN-FAIL " + why);
            try { File.WriteAllText(_done, "fail " + why); } catch { }
        }

        private string Rel()
        {
            return _landAt < 0f
                ? "t=pre"
                : "t=+" + (Time.realtimeSinceStartup - _landAt).ToString("0.000") + "s";
        }

        /// <summary>终态判定：不重铺、不回收入队、可见块全在生效集里。</summary>
        private bool Settled()
        {
            var view = View();
            if (view == null) return false;
            if (F(view, "_job") != null) return false;
            if (Coll(view, "_retireChunks") > 0) return false;
            return MissAct() == 0;
        }

        // ── 读数 ─────────────────────────────────────────────────────────────

        private string ChunkLine()
        {
            var map = CtxMap();
            var view = View();
            if (map == null || view == null) return "no-map/view";
            var sb = new StringBuilder();
            sb.Append("frame=").Append(Time.frameCount);
            sb.Append(" area=").Append((int)map.Area);
            sb.Append(" size=").Append(map.Width).Append('x').Append(map.Height);

            var job = F(view, "_job");
            if (job == null)
            {
                sb.Append(" job=null");
            }
            else
            {
                var chunks = F2(job, "Chunks") as ICollection;
                sb.Append(" job=RUN idx=").Append(F2(job, "ChunkIndex")).Append('/')
                  .Append(chunks != null ? chunks.Count : -1)
                  .Append(" F=").Append(F2(job, "Frames"))
                  .Append(" building=").Append(F2(job, "Building"));
            }
            sb.Append(" ground=").Append(Coll(view, "_groundChunks"));
            sb.Append(" bufGround=").Append(Coll(view, "_bufGroundChunks"));
            sb.Append(" pending=").Append(Coll(view, "_pendingChunks"));
            sb.Append(" retire=").Append(Coll(view, "_retireChunks"));

            var act = Keys(view, "_groundChunks");
            var buf = Keys(view, "_bufGroundChunks");
            var pen = Keys(view, "_pendingChunks");

            var exp = ExpectedRange(map);
            var total = 0;
            var actCov = 0;
            var bufCov = 0;
            var penCov = 0;
            for (var cx = exp.x; cx <= exp.z; cx++)
            {
                for (var cy = exp.y; cy <= exp.w; cy++)
                {
                    total++;
                    var c = new Vector2Int(cx, cy);
                    if (act.Contains(c)) actCov++;
                    if (buf.Contains(c)) bufCov++;
                    if (pen.Contains(c)) penCov++;
                }
            }
            sb.Append(" exp=(").Append(exp.x).Append(',').Append(exp.y).Append(")-(")
              .Append(exp.z).Append(',').Append(exp.w).Append(") total=").Append(total);
            sb.Append(" actCov=").Append(actCov).Append('/').Append(total);
            sb.Append(" bufCov=").Append(bufCov);
            sb.Append(" penCov=").Append(penCov);
            sb.Append(" missAct=").Append(total - actCov);
            sb.Append(" actKeys=").Append(act.Count).Append('@').Append(Bounds(act));

            var cam = Camera.main;
            if (cam != null)
            {
                var p = cam.transform.position;
                var cg = Iso.ScreenToGrid(cam, new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
                sb.Append(" cam=(").Append(p.x.ToString("0.0")).Append(',').Append(p.y.ToString("0.0")).Append(')');
                sb.Append(" camGrid=").Append(Fmt(cg));
            }
            var player = CtxPlayer();
            if (player != null) sb.Append(" player=").Append(Fmt(player.Grid));
            return sb.ToString();
        }

        private int MissAct()
        {
            var map = CtxMap();
            var view = View();
            if (map == null || view == null) return int.MaxValue;
            var act = Keys(view, "_groundChunks");
            var exp = ExpectedRange(map);
            var miss = 0;
            for (var cx = exp.x; cx <= exp.z; cx++)
                for (var cy = exp.y; cy <= exp.w; cy++)
                    if (!act.Contains(new Vector2Int(cx, cy))) miss++;
            return miss;
        }

        /// <summary>屏幕四角 → 格 → 块范围（含 1 块外扩；与生产 `ComputeVisibleChunkRange` 同公式）。</summary>
        private Vector4 ExpectedRange(IMapModule map)
        {
            var cxN = (map.Width + ChunkSize - 1) / ChunkSize;
            var cyN = (map.Height + ChunkSize - 1) / ChunkSize;
            var cam = Camera.main;
            if (cam == null) return new Vector4(0, 0, cxN - 1, cyN - 1);
            var minX = int.MaxValue; var minY = int.MaxValue;
            var maxX = int.MinValue; var maxY = int.MinValue;
            for (var i = 0; i < 4; i++)
            {
                var sx = (i & 1) == 0 ? 0f : Screen.width;
                var sy = (i & 2) == 0 ? 0f : Screen.height;
                var g = Iso.ScreenToGrid(cam, new Vector3(sx, sy, 0f));
                minX = Mathf.Min(minX, g.x); minY = Mathf.Min(minY, g.y);
                maxX = Mathf.Max(maxX, g.x); maxY = Mathf.Max(maxY, g.y);
            }
            return new Vector4(
                Mathf.Clamp(minX / ChunkSize - 1, 0, cxN - 1),
                Mathf.Clamp(minY / ChunkSize - 1, 0, cyN - 1),
                Mathf.Clamp(maxX / ChunkSize + 1, 0, cxN - 1),
                Mathf.Clamp(maxY / ChunkSize + 1, 0, cyN - 1));
        }

        private static string Bounds(List<Vector2Int> ks)
        {
            if (ks == null || ks.Count == 0) return "-";
            var x0 = int.MaxValue; var y0 = int.MaxValue; var x1 = int.MinValue; var y1 = int.MinValue;
            foreach (var k in ks)
            {
                x0 = Mathf.Min(x0, k.x); y0 = Mathf.Min(y0, k.y);
                x1 = Mathf.Max(x1, k.x); y1 = Mathf.Max(y1, k.y);
            }
            return "(" + x0 + "," + y0 + ")..(" + x1 + "," + y1 + ")";
        }

        private static void Shot(string name)
        {
            var path = Api.OutDir + "/" + name;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                Api.Log("SHOT " + name + " frame=" + Time.frameCount);
            }
            catch (Exception ex) { Api.Warn("SHOT-FAIL " + name + " " + ex.GetType().Name); }
        }

        private void Write(string line)
        {
            try { File.AppendAllText(_log, "[" + Api.Tag + "] " + line + Environment.NewLine); }
            catch { }
            Api.Log(line);
        }

        // ── 反射读取（复用 chunkhole_probe.cs / mapborder2_drive.cs 的同名字段口径）────────

        private static Type FindType(string full)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var t = asm.GetType(full, false); if (t != null) return t; }
                catch { }
            }
            return null;
        }

        private static object Ctx()
        {
            var t = FindType("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return p != null ? p.GetValue(null, null) : null;
        }

        private static object CtxMember(string name)
        {
            var c = Ctx();
            if (c == null) return null;
            var f = c.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(c) : null;
        }

        private static IMapModule CtxMap() { return CtxMember("Map") as IMapModule; }
        private static IPlayerModule CtxPlayer() { return CtxMember("Player") as IPlayerModule; }

        /// <summary>`MapModule._view`（`MapView` 实例；internal 类型 ⇒ 反射）。</summary>
        private static object View()
        {
            var map = CtxMember("Map");
            if (map == null) return null;
            var mt = map.GetType();
            while (mt != null)
            {
                var f = mt.GetField("_view", NP);
                if (f != null) return f.GetValue(map);
                mt = mt.BaseType;
            }
            return null;
        }

        private static object F(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, NP);
            return f != null ? f.GetValue(o) : null;
        }

        private static object F2(object o, string name)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(name, ALL);
            return f != null ? f.GetValue(o) : null;
        }

        private static int Coll(object o, string name)
        {
            var v = F(o, name) as ICollection;
            return v != null ? v.Count : -1;
        }

        private static List<Vector2Int> Keys(object o, string name)
        {
            var res = new List<Vector2Int>();
            var e = F(o, name) as IEnumerable;
            if (e == null) return res;
            foreach (var k in e) { if (k is Vector2Int v) res.Add(v); }
            return res;
        }

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

        private static void Emit<T>(string evName, T payload)
        {
            try
            {
                var bus = Game.Event;
                if (bus == null) { Api.Warn("EMIT-FAIL no-bus " + evName); return; }
                bus.Emit<T>(evName, payload);
            }
            catch (Exception ex) { Api.Warn("EMIT-FAIL " + evName + " " + ex.GetType().Name + ": " + ex.Message); }
        }

        private static bool PanelOpen()
        {
            try
            {
                var ps = UnityEngine.Object.FindObjectsByType<Diablo2.UI.WaypointPanel>(FindObjectsSortMode.None);
                for (var i = 0; i < ps.Length; i++)
                    if (ps[i] != null && ps[i].gameObject.activeInHierarchy) return true;
                return false;
            }
            catch { return false; }
        }

        /// <summary>把 dest 塞进 `AppWaypoint.Visited`（探针侧；返回是否成功）。</summary>
        private static bool MarkVisited(int dest)
        {
            try
            {
                var t = FindType("Diablo2.App.AppWaypoint");
                if (t == null) return false;
                var f = t.GetField("Visited", BindingFlags.NonPublic | BindingFlags.Static);
                if (f == null) return false;
                var set = f.GetValue(null);
                if (set == null) return false;
                var areaT = FindType("Diablo2.Def.AreaId");
                var value = areaT != null ? Enum.ToObject(areaT, dest) : (object)dest;
                var add = set.GetType().GetMethod("Add");
                if (add == null) return false;
                add.Invoke(set, new[] { value });
                return true;
            }
            catch (Exception ex) { Api.Warn("VISITED-ADD-FAIL " + ex.GetType().Name); return false; }
        }

        /// <summary>目的地 = 第一个不是当前的 `AreaId`（本工程 Act I 三张图）。</summary>
        private static int PickDest(int cur)
        {
            var t = FindType("Diablo2.Def.AreaId");
            if (t == null) return -1;
            foreach (var v in Enum.GetValues(t))
            {
                var i = Convert.ToInt32(v);
                if (i != cur) return i;
            }
            return -1;
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        private static string Fmt(Vector2Int v) { return "(" + v.x + "," + v.y + ")"; }

        // ── boot（复用 mapborder2_drive.cs 的既有链）──────────────────────────

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
