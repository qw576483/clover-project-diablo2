// =============================================================================
//   主题：把「走到传送台 ⇒ 面板打开 ⇒ 选目标区 ⇒ 落地」这一整条链**一次跑完**，
//        并同时采齐：`areaId` · 激活列表 · 缺块 / 黑窗读数 · **截图格号**（截图↔格号对照表）。
//
//   WHY 还需要它（`travelblack_drive.cs` 已经跑过其中一段）：
//     · 既有那条链的"激活目的地"是**反射往 `AppWaypoint.Visited` 里塞**的（`PROBE-VISITED-ADD`）
//       ⇒ 它证明了"面板+切区"，**没有**证明"激活列表由生产代码写出"这条；
//     · 它没有采 `areaId` 与激活列表读数，也没有把"截图 ↔ 当时格号"绑成一张对照表
//       ⇒ 缺块/黑窗的图**没法按格号复核**。
//     ⇒ 本驱动 = 同一条链的**收口版**，改动只有三处（其余逐条复用，不从零重写）：
//         ① 激活来源改成**生产路径**：`ExitEntered` 走两次真实换区（`AppFlow.EnterArea` ⇒
//            `AreaChanged` ⇒ `AppWaypoint.RecordVisited`）。**不再反射改任何静态字段**；
//         ② 面板打开判据换成 `Game.UI.IsOpen<WaypointPanel>()`（真值口径；`constraints.md` #11：
//            失焦停帧时 `FindObjectsByType` 会给假的"面板残留/没开"）；面板**实际列出的目的地**
//            从面板实例的 `_current.dests` 读（= 所见即所读）；
//         ③ 截图带**格号对照表** `u32_shots_<tag>.tsv`（文件名 / areaId / 玩家格 / 相机格 /
//            可见块区间 / total / actCov / missAct）。
//
//   反射口径（字段名 / 同公式）逐条复用 `travelblack_drive.cs` 与 `chunkhole_probe.cs`，
//   不另立一套；区块反射读的是 `MapView` 的既有私有字段。
//
//   判据（动手前定死，不许事后编）：
//     · `WP-PANEL dests=` 必须**含** dest 且**不含**当前区域（面板列的就是已激活 − 当前）；
//     · `PROD-VISITED` 的两个区域必须由 `RecordVisited` 写出（日志里必须出现
//       `传送点：新区域已激活`，即生产留痕）⇒ 只要它没出现，"激活列表由生产写出"这条**不成立**；
//     · 落地：`areaId` 必须等于 dest；`missAct` 在落地后必须**回落到 0**（黑窗 = 临时窗口，不是永久）；
//       把"落地帧起 `missAct>0` 的持续秒数"记进 `SHOT-MANIFEST` 的备注列。
//     · 若上面任一条不成立 ⇒ 如实写"不成立 + 实测值"，不许拿别的读数为它背书。
//
//   Entry: U32Drv.Api.Ping()          —— 编译闸门 + 就绪自检（进 Play 前调）
//          U32Drv.Api.Go(spec)        —— spec = "<outDir>|<tag>"
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

namespace U32Drv
{
    public static class Api
    {
        internal const string Tag = "U32";
        internal static string OutDir = "c:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots";
        internal static string TagName = "u32";

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

        /// <summary>编译闸门 + 就绪自检（`Go` 之前调一次）。见 `travelblack_drive.cs` 的同名方法。</summary>
        public static string Ping()
        {
            var ctx = FindType("Diablo2.App.AppContext") != null ? 1 : 0;
            return "PONG fsm=" + Fsm() + " ctxType=" + ctx
                   + " res=" + (Game.Res != null ? 1 : 0)
                   + " logger=" + (Game.Logger != null ? 1 : 0)
                   + " ui=" + (Game.UI != null ? 1 : 0)
                   + " frame=" + Time.frameCount;
        }

        internal static Type FindType(string full)
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
            var go = new GameObject("U32DrvHost");
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
        private const BindingFlags NPS = BindingFlags.NonPublic | BindingFlags.Static;
        private const int ChunkSize = 16;               // = MapView.ChunkSize（同 travelblack_drive）

        private string _done;
        private string _log;
        private string _shotsDir;
        private readonly List<string> _shotRows = new List<string>();

        private float _landAt = -1f;
        private bool _shotPanel, _shot0, _shot05, _shot2;
        private float _blackFrom = -1f;                 // 落地后首次 missAct>0 的时刻
        private float _blackTo = -1f;                   // 落地后首次回到 missAct==0 的时刻
        private bool _areaChanged;

        private void Start() { StartCoroutine(Chain()); }

        private IEnumerator Chain()
        {
            _done = Api.OutDir + "/u32_done_" + Api.TagName + ".txt";
            _log = Api.OutDir + "/u32_" + Api.TagName + ".log";
            _shotsDir = Api.OutDir;
            if (File.Exists(_done)) File.Delete(_done);

            var tW = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tW < 40f && (Ctx() == null || Api.Fsm().StartsWith("("))) yield return null;
            Write("READY fsm=" + Api.Fsm() + " ctx=" + (Ctx() != null ? 1 : 0));
            yield return new WaitForSeconds(1.5f);

            // ① boot 到 Stage（链逐条复用 `travelblack_drive.cs`，不另写一份）
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
                  + " spawn=" + Fmt(map.SpawnPoint) + " player=" + Fmt(player.Grid));
            Write("AREAID 来源=初始进图 mapArea=" + (int)map.Area + " selectedAreaId=" + SelectedAreaId()
                  + " visited=" + VisitedLine());

            var dest = PickDest((int)map.Area);
            if (dest < 0) { Fail("no-dest"); yield break; }

            // ③ 激活目的地 —— **走生产路径**（不再反射改 `Visited`）：
            //    `Events.ExitEntered(dest)` 就是"城镇东侧接缝 / 传送面板"两条真实入口发的同一个事件
            //    ⇒ `AppFlow.EnterArea` 生成新图 + 落位 + 发 `AreaChanged` ⇒ `AppWaypoint.RecordVisited`
            //    把新区域写进激活列表。跑两次（去 + 回）⇒ 激活列表 = {当前区域, dest}。
            Write("PROD-ACTIVATE-START dest=" + dest + "（走 ExitEntered，与真实过门/传送同一事件）");
            Emit(Diablo2.Core.Events.ExitEntered, (AreaId)dest);
            var t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < 25f && (int)map.Area != dest) yield return null;
            yield return new WaitForSeconds(1.5f);
            Write("PROD-ACTIVATE-1 fsm=" + Api.Fsm() + " mapArea=" + (int)map.Area + " dest=" + dest
                  + " player=" + Fmt(player.Grid) + " visited=" + VisitedLine());

            Emit(Diablo2.Core.Events.ExitEntered, AreaId.Town);
            var t3 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t3 < 25f && (int)map.Area != (int)AreaId.Town) yield return null;
            yield return new WaitForSeconds(1.5f);
            Write("PROD-VISITED mapArea=" + (int)map.Area + " visited=" + VisitedLine()
                  + "（判据：必须由 `RecordVisited` 写出 —— 上面的日志里必须出现「传送点：新区域已激活」）");
            if (VisitedValues().Count < 2)
                Api.Warn("PROD-VISITED 不足 2 个 ⇒ 「激活列表由生产代码写出」这条**不成立**（面板会拒绝切区）");

            // ④ 走到传送台锚点（玩家真实路径：`MoveCommand` ⇒ `AppWaypoint` 判格 ⇒ 到 8 邻开面板）
            var pts = map.WaypointPoints;
            if (pts == null || pts.Count == 0) { Fail("no-waypoint-anchor"); yield break; }
            var wp = pts[0];
            Write("WALK-WP area=" + (int)map.Area + " anchor=" + Fmt(wp) + " player=" + Fmt(player.Grid));
            Emit(Diablo2.Core.Events.MoveCommand, wp);

            var t4 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t4 < 30f && !PanelOpen()) yield return null;
            Write("WP-PANEL open=" + (PanelOpen() ? 1 : 0) + " player=" + Fmt(player.Grid)
                  + " dist=" + Chebyshev(player.Grid, wp) + " dests=" + PanelDests());
            if (!PanelOpen()) { Fail("panel-never-opened"); yield break; }

            //   为什么：`Shot` 走 `ScreenCapture.CaptureScreenshot`，而面板的底图/钮面走
            //   `UiArt.Art` / `FlowButton` 的 `Game.Res.LoadAsset` **异步**回调 ⇒ **面板打开当帧**
            //   截到的是"素材在途"帧（外框与钮面还都是深色占位）。实测留痕：`u32_u32p2_1_panel.png`
            //   （frame=99 = 打开当帧）里钮面/外框看不到原版装饰纹理，而同屏的 `uifix4_wp_panel.png` 有。
            //   判据面（离线，`tools/probes/hosts/uicheck` ①-d 的 `ShotFrameGapOk`）：
            //   `SHOT …_1_panel.png frame=N` ⇒ `SHOT …_2_landed.png frame=M` 必须 **M ≥ N + 2**；
            //   已知坏样本 = 旧批次 `u32play`（`N = M = 192`，面板图被落地图顶掉）。
            yield return null;
            yield return null;

            Shot("u32_" + Api.TagName + "_1_panel.png", "面板已打开（走到锚点后）");

            // 实测（首次会话）：面板打开 ⇒ 发 `WaypointTravelRequest` ⇒ 落地**全在同一帧内**完成
            //    ⇒ 本帧发了两次 `ScreenCapture.CaptureScreenshot`（面板 + 落地），而它只在**帧末**写一次
            //    ⇒ `_1_panel.png` **根本没落盘**（首跑 4 张里只出现后 3 张，见 `.ai-tmp/test/report-u32.md`）。
            yield return null;
            yield return null;

            // ⑤ 面板实际列出的目的地**必须**含 dest、且不含当前区域（判据见文件头）
            var dests = PanelDestValues();
            var listsDest = dests.Contains(dest);
            var listsCur = dests.Contains((int)map.Area);
            Write("WP-DESTS-CHK dest=" + dest + " inList=" + (listsDest ? 1 : 0)
                  + " currentInList=" + (listsCur ? 1 : 0) + "（期望 1 / 0）");
            if (!listsDest || listsCur)
                Api.Warn("WP-DESTS-CHK 不通过 ⇒ 面板列的不是「已激活 − 当前」");

            // ⑥ 传送前的最后一份读数（旧区 / 旧集 / 旧相机）
            Write("PRE  " + ChunkLine());
            var oldArea = (int)map.Area;
            var preGrid = player.Grid;
            var preSelectedArea = SelectedAreaId();

            // ⑦ 发面板按钮的同一个事件（`AppWaypoint.OnTravelRequest` 收它）
            //    必须**先**把 `_areaChanged` 清掉：第 ③ 步的两次激活换区已经把它置过 true，
            //       不清就会把"上一次换区的残余"当成落地信号（→ 落地帧判早，读数全偏）。
            _areaChanged = false;
            Api.Log("TRAVEL-REQ dest=" + dest + " from=" + oldArea + " preGrid=" + Fmt(preGrid)
                    + "（与 `WaypointPanel` 目的地按钮**同一个事件**）");
            Emit(Diablo2.Core.Events.WaypointTravelRequest, dest);

            // ⑧ 落地帧起逐帧采样（`map.Area` 第一拍就变；玩家/相机**第二拍**才挪 ⇒
            //    "落地"按 `Events.AreaChanged` 判，不用"玩家格变了"——走到锚点也会变）
            var t5 = Time.realtimeSinceStartup;
            var landed = false;
            while (Time.realtimeSinceStartup - t5 < 16f)
            {
                if (!landed && _areaChanged && (int)map.Area == dest)
                {
                    landed = true;
                    _landAt = Time.realtimeSinceStartup;
                    Api.Log("LANDED frame=" + Time.frameCount + " area=" + (int)map.Area
                            + " player=" + Fmt(player.Grid) + "（第二拍：玩家/相机落位）");
                }

                var line = ChunkLine();
                Write((landed ? "F" : "p") + Rel() + " " + line);
                TrackBlackWindow(landed);

                if (landed && !_shot0) { _shot0 = true; Shot("u32_" + Api.TagName + "_2_landed.png", "落地帧"); }
                if (landed && !_shot05 && Time.realtimeSinceStartup - _landAt >= 0.5f)
                { _shot05 = true; Shot("u32_" + Api.TagName + "_3_p05.png", "落地 +0.5s"); }
                if (landed && !_shot2 && Time.realtimeSinceStartup - _landAt >= 2.0f)
                { _shot2 = true; Shot("u32_" + Api.TagName + "_4_p20.png", "落地 +2.0s"); }

                if (landed && _shot2 && Time.realtimeSinceStartup - _landAt >= 2.6f && Settled()) break;
                yield return null;
            }

            // ⑨ 收尾读数
            var t6 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t6 < 2f) yield return null;
            Write("END  " + ChunkLine());
            Write("AREAID-AFTER 来源=传送后 mapArea=" + (int)map.Area + " selectedAreaId=" + SelectedAreaId()
                  + "（期望都 = dest " + dest + "；selectedAreaId 就是下一次 `Save()` 会收进 `CharacterSave.areaId` 的那个值）");
            Write("VISITED-AFTER visited=" + VisitedLine());
            Write("BLACKWINDOW missAct>0 持续=" + BlackSeconds().ToString("0.000") + "s"
                  + "（落地帧起算；0 = 落地即满，非永久黑）");
            Write("SUMMARY area=" + (int)map.Area + " dest=" + dest + " landed=" + (landed ? 1 : 0)
                  + " shots=" + ((_shot0 ? 1 : 0) + (_shot05 ? 1 : 0) + (_shot2 ? 1 : 0))
                  + " settled=" + (Settled() ? 1 : 0) + " preSelectedAreaId=" + preSelectedArea);
            WriteShotManifest();
            File.WriteAllText(_done, "ok landed=" + (landed ? 1 : 0) + " area=" + (int)map.Area
                                     + " dest=" + dest + " black=" + BlackSeconds().ToString("0.000"));
            Api.Log("DONE area=" + (int)map.Area + " dest=" + dest + " landed=" + (landed ? 1 : 0));
        }

        private void Fail(string why)
        {
            Api.Warn("CHAIN-FAIL " + why);
            try { File.WriteAllText(_done, "fail " + why); } catch { }
        }

        private string Rel()
        {
            return _landAt < 0f ? "t=pre" : "t=+" + (Time.realtimeSinceStartup - _landAt).ToString("0.000") + "s";
        }

        /// <summary>黑窗窗口（落地后 `missAct>0` 的首/末时刻）。</summary>
        private void TrackBlackWindow(bool landed)
        {
            if (!landed) return;
            var miss = MissAct();
            if (miss > 0 && _blackFrom < 0f) _blackFrom = Time.realtimeSinceStartup;
            if (miss == 0 && _blackFrom >= 0f && _blackTo < 0f) _blackTo = Time.realtimeSinceStartup;
        }

        private float BlackSeconds()
        {
            if (_blackFrom < 0f) return 0f;
            var end = _blackTo >= 0f ? _blackTo : Time.realtimeSinceStartup;
            return Mathf.Max(0f, end - _blackFrom);
        }

        /// <summary>终态判定：不重铺、不回收入队、可见块全在生效集里（同 travelblack_drive）。</summary>
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
            sb.Append(" ground=").Append(Coll(view, "_groundChunks"));
            sb.Append(" pending=").Append(Coll(view, "_pendingChunks"));
            sb.Append(" retire=").Append(Coll(view, "_retireChunks"));

            int ex0, ey0, ex1, ey1;
            ExpectedRange(map, out ex0, out ey0, out ex1, out ey1);
            var act = Keys(view, "_groundChunks");
            var total = 0;
            var actCov = 0;
            for (var cx = ex0; cx <= ex1; cx++)
                for (var cy = ey0; cy <= ey1; cy++)
                {
                    total++;
                    if (act.Contains(new Vector2Int(cx, cy))) actCov++;
                }
            sb.Append(" exp=(").Append(ex0).Append(',').Append(ey0).Append(")-(")
              .Append(ex1).Append(',').Append(ey1).Append(")");
            sb.Append(" total=").Append(total).Append(" actCov=").Append(actCov);
            sb.Append(" missAct=").Append(total - actCov);

            var player = CtxPlayer();
            if (player != null) sb.Append(" player=").Append(Fmt(player.Grid));
            var cam = Camera.main;
            if (cam != null)
            {
                var cg = Iso.ScreenToGrid(cam, new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
                sb.Append(" cam=").Append(Fmt(cg));
            }
            return sb.ToString();
        }

        private int MissAct()
        {
            var map = CtxMap();
            var view = View();
            if (map == null || view == null) return int.MaxValue;
            var act = Keys(view, "_groundChunks");
            int ex0, ey0, ex1, ey1;
            ExpectedRange(map, out ex0, out ey0, out ex1, out ey1);
            var miss = 0;
            for (var cx = ex0; cx <= ex1; cx++)
                for (var cy = ey0; cy <= ey1; cy++)
                    if (!act.Contains(new Vector2Int(cx, cy))) miss++;
            return miss;
        }

        /// <summary>屏幕四角 → 格 → 块范围（含 1 块外扩；与生产 `ComputeVisibleChunkRange` 同公式）。</summary>
        private void ExpectedRange(IMapModule map, out int x0, out int y0, out int x1, out int y1)
        {
            var cxN = (map.Width + ChunkSize - 1) / ChunkSize;
            var cyN = (map.Height + ChunkSize - 1) / ChunkSize;
            var cam = Camera.main;
            if (cam == null) { x0 = 0; y0 = 0; x1 = cxN - 1; y1 = cyN - 1; return; }
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
            x0 = Mathf.Clamp(minX / ChunkSize - 1, 0, cxN - 1);
            y0 = Mathf.Clamp(minY / ChunkSize - 1, 0, cyN - 1);
            x1 = Mathf.Clamp(maxX / ChunkSize + 1, 0, cxN - 1);
            y1 = Mathf.Clamp(maxY / ChunkSize + 1, 0, cyN - 1);
        }

        // ── 截图 + 格号对照表 ────────────────────────────────────────────────

        private void Shot(string name, string note)
        {
            var path = _shotsDir + "/" + name;
            var map = CtxMap();
            var player = CtxPlayer();
            try
            {
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                Api.Log("SHOT " + name + " frame=" + Time.frameCount + " note=" + note);
            }
            catch (Exception ex) { Api.Warn("SHOT-FAIL " + name + " " + ex.GetType().Name); }

            // 「截图格号」= 这张图上当时站在哪一格 / 相机看哪一格（不含墨量判定，那是眼睛的事）
            var camGrid = "-";
            var cam = Camera.main;
            if (cam != null)
                camGrid = Fmt(Iso.ScreenToGrid(cam, new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f)));
            int x0, y0, x1, y1;
            if (map != null) ExpectedRange(map, out x0, out y0, out x1, out y1); else { x0 = y0 = x1 = y1 = -1; }
            _shotRows.Add(name + "\t" + (map != null ? ((int)map.Area).ToString() : "-")
                          + "\t" + (player != null ? Fmt(player.Grid) : "-") + "\t" + camGrid
                          + "\t(" + x0 + "," + y0 + ")-(" + x1 + "," + y1 + ")"
                          + "\t" + MissAct() + "\t" + note);
        }

        private void WriteShotManifest()
        {
            var p = _shotsDir + "/u32_shots_" + Api.TagName + ".tsv";
            var sb = new StringBuilder();
            sb.AppendLine("# 片 u32-close 截图↔格号对照表（列：文件名 / areaId / 玩家格 / 相机格 / 可见块区间 / missAct / 备注）");
            sb.AppendLine("# 生成 = d2u32_drive.cs（同一 Play 会话内与 `u32_" + Api.TagName + ".log` 同步写）");
            foreach (var r in _shotRows) sb.AppendLine(r);
            File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
            Api.Log("SHOT-MANIFEST " + p + " rows=" + _shotRows.Count);
        }

        private void Write(string line)
        {
            try { File.AppendAllText(_log, "[" + Api.Tag + "] " + line + Environment.NewLine); }
            catch { }
            Api.Log(line);
        }

        // ── 面板读数（真值口径 = `Game.UI.IsOpen`；列出的目的地 = 面板实例自己的 `_current`）──

        private static bool PanelOpen()
        {
            try { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.WaypointPanel>(); }
            catch { return false; }
        }

        private static object PanelInstance()
        {
            try { return Game.UI != null ? Game.UI.Get<Diablo2.UI.WaypointPanel>() : null; }
            catch { return null; }
        }

        private static List<int> PanelDestValues()
        {
            var res = new List<int>();
            var p = PanelInstance();
            if (p == null) return res;
            var args = Member(p, "_current");
            if (args == null) return res;
            var dests = Member(args, "dests") as IEnumerable;
            if (dests == null) return res;
            foreach (var d in dests)
            {
                if (d == null) continue;
                var area = Member(d, "area");
                if (area != null) res.Add(Convert.ToInt32(area));
            }
            return res;
        }

        private static string PanelDests()
        {
            var v = PanelDestValues();
            return v.Count == 0 ? "(空列表 = 原版「尚未啟動其他傳送點」)" : ("[" + string.Join(",", v) + "]");
        }

        // ── 激活列表 / areaId（**只读**反射；本驱动不改任何静态字段）──────────

        private static List<int> VisitedValues()
        {
            var res = new List<int>();
            try
            {
                var t = Api.FindType("Diablo2.App.AppWaypoint");
                if (t == null) return res;
                var m = t.GetMethod("SnapshotVisited", NPS);
                if (m == null) return res;
                var list = m.Invoke(null, null) as IEnumerable;
                if (list == null) return res;
                foreach (var v in list) res.Add(Convert.ToInt32(v));
            }
            catch (Exception ex) { Api.Warn("VISITED-READ-FAIL " + ex.GetType().Name); }
            return res;
        }

        private static string VisitedLine()
        {
            var v = VisitedValues();
            return "count=" + v.Count + " [" + string.Join(",", v) + "]";
        }

        /// <summary>`AppFlow._selected.areaId` = 下一次 `Save()` 会收进 `CharacterSave.areaId` 的那个值。</summary>
        private static string SelectedAreaId()
        {
            try
            {
                var flow = CtxMember("Flow");
                if (flow == null) return "-";
                var sel = Member(flow, "_selected");
                if (sel == null) return "-";
                var a = Member(sel, "areaId");
                return a == null ? "-" : Convert.ToInt32(a).ToString();
            }
            catch { return "-"; }
        }

        // ── 反射（**逐条复用** `travelblack_drive.cs` 的同名字段口径）──────────

        private static object Ctx()
        {
            var t = Api.FindType("Diablo2.App.AppContext");
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

        private static int Coll(object o, string name)
        {
            var v = F(o, name) as ICollection;
            return v != null ? v.Count : -1;
        }

        /// <summary>
        /// 集合里的块号集合。`Dictionary` 直接枚举出来的是 `KeyValuePair`（不是键）⇒ 必须走
        /// `IDictionary.Keys`；`Queue&lt;Vector2Int&gt;` 直接枚举就是元素。（`travelblack_drive` 第一版漏了
        /// 这一条，读数全是假的 —— 本驱动沿用其修后的口径。）
        /// </summary>
        private static List<Vector2Int> Keys(object o, string name)
        {
            var res = new List<Vector2Int>();
            var raw = F(o, name);
            var d = raw as IDictionary;
            if (d != null)
            {
                foreach (var k in d.Keys) { if (k is Vector2Int v) res.Add(v); }
                return res;
            }
            var e = raw as IEnumerable;
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

        /// <summary>目的地 = 第一个不是当前的 `AreaId`（本工程 Act I 三张图；不写死号）。</summary>
        private static int PickDest(int cur)
        {
            var t = Api.FindType("Diablo2.Def.AreaId");
            if (t == null) return -1;
            foreach (var v in Enum.GetValues(t))
            {
                var i = Convert.ToInt32(v);
                if (i != cur) return i;
            }
            return -1;
        }

        private static int Chebyshev(Vector2Int a, Vector2Int b)
            => Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

        private static string Fmt(Vector2Int v) { return "(" + v.x + "," + v.y + ")"; }

        // ── boot（复用 `travelblack_drive.cs` 的既有链）────────────────────────

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

        /// <summary>`Events.AreaChanged` 到达（换区第二拍）。</summary>
        private void OnEnable()
        {
            try
            {
                if (Game.Event != null) Game.Event.On<AreaId>(Diablo2.Core.Events.AreaChanged, OnAreaChanged);
            }
            catch (Exception ex) { Api.Warn("SUBSCRIBE-FAIL " + ex.GetType().Name); }
        }

        private void OnDisable()
        {
            try
            {
                if (Game.Event != null) Game.Event.Off<AreaId>(Diablo2.Core.Events.AreaChanged, OnAreaChanged);
            }
            catch { }
        }

        private void OnAreaChanged(AreaId a)
        {
            _areaChanged = true;
            Api.Log("AREA-CHANGED area=" + (int)a + " frame=" + Time.frameCount
                    + " t=" + Time.realtimeSinceStartup.ToString("0.000") + "（第二拍：玩家/相机落位）");
        }
    }
}
