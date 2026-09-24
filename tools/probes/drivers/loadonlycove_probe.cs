// =============================================================================
//   **只取证**：本文件不改任何产品代码，只发**既有**事件 + 反射读面板私有读数。
//
//   为什么必须进 Play（play-log 第 4 列的理由）：
//     用户报的「读档后进度丢失」里「小地图已探索格」那一条，其**终极判据是画面**：
//     要证明画面上那些格**只能**来自存档（而不是这一局走出来的），就必须
//     ① 先在一局里走远（出半径 6）并保存退出，② 再**开一局新的 Play（内存全空）**只读档 ⇒
//     此刻画面上的探索格只可能来自 `client/setting/saves/<名>.json`。
//     离线宿主（tools/probes/hosts/savecheck）只能判"字段往返 + 旧档兼容"，
//     判不了"面板的画面上真有那批格"。
//
//   两段式（一条链跑完，不逐项进 Play）：
//     Phase A（mode=walk）    ：创角 → 进营地 → 走到距起点 9 格（> 半径 6）→ 开图采图 → 保存并退出
//     Phase B（mode=loadonly）：**新一局 Play**、BootDone → CharSelectRequest → 开图 → 读数 + 采图
//
//   配方来源（复用，不重造）：
//     · 骨架/plumbing 抄 `.ai-tmp/drivers/saveprogress_probe.cs`（含 `loadonly` 那条链、
//       "启动屏停在 Boot 必须补发 Events.BootDone"、"面板 IsOpen 为真 ≠ 已画出来，要等 1.5s" 两个坑）
//     · 走步 + automap 读数抄 `.ai-tmp/drivers/automappanel_probe.cs`（ChooseFarWalkable / ReadAutomap）
//       算出"本局新算的半径 6 兜底圈"，与面板 `_explored` 求差 ⇒
//       "超出兜底圈的格数"（= 只可能来自读档回灌的那批），并把其中距玩家 >6 格的格
//       换算成**屏幕像素坐标**（用于在图上画框指认）。判据与产品同源，不造第二份画法。
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LOC
{
    /// <summary>反射 / 日志 / 侧信道 / 截图 / 存档读数的工具（形状 = SP.Probe / AMP.Probe）。</summary>
    public static class Probe
    {
        internal const string Tag = "LOC";
        private static string _done = string.Empty;

        internal static void Log(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, msg); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }

        /// <summary>
        /// 侧信道：游戏日志线程在退出 Play 时会被中止（前片实测 ThreadAbortException），
        /// 那之后写进日志的读数全丢 ⇒ 每条读数**同时**直写磁盘（marker 同目录的 .side.txt）。
        /// </summary>
        internal static void Side(string line)
        {
            try
            {
                if (string.IsNullOrEmpty(_done)) return;
                File.AppendAllText(_done + ".side.txt", DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + "\n");
            }
            catch { }
        }

        internal static void Warn(string m) { Log("WARN " + m); Side("WARN " + m); }
        internal static void KV(string k, string v) { Log(k + "=" + v); Side(k + "=" + v); }
        internal static void Paths(string p) { _done = p ?? string.Empty; Log("PATHS done=" + _done); }

        internal static void Done()
        {
            Log("LOC-DONE");
            try
            {
                if (!string.IsNullOrEmpty(_done))
                {
                    var d = Path.GetDirectoryName(_done);
                    if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                    File.WriteAllText(_done, "LOC-DONE " + DateTime.Now.ToString("o"));
                }
            }
            catch (Exception ex) { Log("DONE-MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static string Shot(string path)
        {
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                return "SHOT-OK " + path + " frame=" + Time.frameCount
                     + " screen=" + Screen.width + "x" + Screen.height;
            }
            catch (Exception ex) { return "SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message; }
        }

        internal static void Emit<T>(string evt, T arg) { Game.Event.Emit<T>(evt, arg); }
        internal static void Emit(string evt) { Game.Event.Emit(evt); }

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

        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }

        internal static FieldInfo F(object o, string name)
        {
            if (o == null) return null;
            var t = o.GetType();
            while (t != null)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null) return f;
                t = t.BaseType;
            }
            return null;
        }

        internal static object V(object o, string name)
        {
            var f = F(o, name);
            return f != null ? f.GetValue(o) : null;
        }

        internal static string Grid(Vector2Int g) { return "(" + g.x + "," + g.y + ")"; }
        internal static Vector2Int PlayerGrid()
        {
            var p = Player();
            return p != null ? p.Grid : new Vector2Int(int.MinValue, int.MinValue);
        }

        internal static string SavePath(string name)
        {
            var proj = Path.GetDirectoryName(Application.dataPath);      // ...\client
            return Path.Combine(Path.Combine(Path.Combine(proj, "setting"), "saves"), name + ".json");
        }

        internal static string ReadAll(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : null; }
            catch (Exception ex) { return "(read-fail " + ex.GetType().Name + ")"; }
        }

        internal static string Slice(string json, string key)
        {
            if (json == null) return "(无档)";
            var i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "(缺)";
            i += key.Length;
            var j = json.IndexOf(']', i);
            return j < 0 ? "(未闭合)" : json.Substring(i, Math.Min(160, j - i + 1));
        }

        internal static string Num(string s, string key)
        {
            if (s == null) return "?";
            var i = s.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "?";
            i += key.Length;
            var j = i;
            while (j < s.Length && (char.IsDigit(s[j]) || s[j] == '-' || s[j] == '+')) j++;
            return j > i ? s.Substring(i, j - i) : "?";
        }

        internal static int CountOf(string s, string key)
        {
            if (s == null) return -1;
            var n = 0; var i = 0;
            while (true)
            {
                i = s.IndexOf(key, i, StringComparison.Ordinal);
                if (i < 0) return n;
                n++; i += key.Length;
            }
        }

        internal static string Fields(string json)
        {
            if (json == null) return "file=(missing) len=0";
            return "len=" + json.Length + " areaId=" + Num(json, "\"areaId\":")
                 + " gridX=" + Num(json, "\"gridX\":") + " gridY=" + Num(json, "\"gridY\":")
                 + " mapSeed=" + Num(json, "\"mapSeed\":")
                 + " 已探索区域数=" + CountOf(json, "\"area\":")
                 + " 传送点=" + Slice(json, "\"visitedWaypoints\":");
        }
    }

    /// <summary>`run_script` 的一次性入口。</summary>
    public static class Api
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount + " t=" + Time.unscaledTime.ToString("0.000"); }
        public static string Fields(string name) { return Probe.Fields(Probe.ReadAll(Probe.SavePath(name))); }
        public static string Dump() { return Judge.Read(); }
        public static string Shot(string path) { return Probe.Shot(path); }
    }

    /// <summary>
    /// 并按产品的贴图几何把"超出兜底圈且距玩家 >6 格"的格换算成 PNG 像素坐标。
    /// 兜底圈用产品自己的纯函数 <see cref="MiniMapPanel.Reveal"/> 算，不复制第二份口径。
    /// </summary>
    public static class Judge
    {
        private static MiniMapPanel Panel()
        {
            return Game.UI != null ? Game.UI.Get<MiniMapPanel>() : null;
        }

        internal static string Read()
        {
            var p = Panel();
            if (p == null) return "PANEL=null（MiniMapPanel 未打开）";

            var map = Probe.V(p, "_map") as MinimapArgs;
            var explored = Probe.V(p, "_explored") as bool[];
            if (map == null || explored == null) return "PANEL=open 但 _map/_explored 为 null";

            var sb = new System.Text.StringBuilder();

            var n = 0;
            for (var i = 0; i < explored.Length; i++) if (explored[i]) n++;

            sb.Append("PANEL=open DrawnCells=").Append(p.DrawnCells)
              .Append(" CellsWithCel=").Append(p.CellsWithCel)
              .Append(" OpaquePixels=").Append(p.OpaquePixels)
              .Append(" ExploredInjected=").Append(p.ExploredInjected)
              .Append(" exploredCount=").Append(n).Append("/").Append(explored.Length);

            var pmod = Probe.Player();
            var px = map.playerX; var py = map.playerY; var pg = "(n/a)";
            if (pmod != null) { var g = pmod.Grid; px = g.x; py = g.y; pg = Probe.Grid(g); }
            sb.Append(" | livePlayer=").Append(pg)
              .Append(" mapPlayer=(").Append(map.playerX).Append(",").Append(map.playerY).Append(")")
              .Append(" mapPlayer==livePlayer=")
              .Append((map.playerX == px && map.playerY == py) ? 1 : 0)
              .Append(" map=").Append(map.width).Append("x").Append(map.height)
              .Append(" screen=").Append(Screen.width).Append("x").Append(Screen.height);

            // ── 对照组：本局新算的"半径 6 兜底圈"（产品同一个纯函数）────────────────
            var fb = new bool[map.width * map.height];
            MiniMapPanel.Reveal(map, fb, px, py);
            var fbCount = 0;
            for (var i = 0; i < fb.Length; i++) if (fb[i]) fbCount++;
            sb.Append(" | 本局新算半径").Append(MiniMapPanel.RevealRadius).Append("圈兜底 fallbackOnly=").Append(fbCount);

            // ── 主体：超出兜底圈的格（= 只可能来自读档回灌的那批）──────────────────
            var beyond = 0; var beyondWithCel = 0;
            var maxMan = -1; var maxManBeyondWithCel = -1;
            var picks = new List<int>();
            for (var y = 0; y < map.height; y++)
            {
                for (var x = 0; x < map.width; x++)
                {
                    var i = y * map.width + x;
                    if (i >= explored.Length || !explored[i]) continue;
                    var man = Math.Abs(x - px) + Math.Abs(y - py);
                    if (man > maxMan) maxMan = man;
                    if (fb[i]) continue;
                    beyond++;
                    var hasCel = map.CelAt(x, y, false) >= 0 || map.CelAt(x, y, true) >= 0;
                    if (!hasCel) continue;
                    beyondWithCel++;
                    if (man > maxManBeyondWithCel) maxManBeyondWithCel = man;
                    if (man > MiniMapPanel.RevealRadius && picks.Count < 6) picks.Add(i);
                }
            }
            sb.Append(" | 超出兜底圈=").Append(beyond).Append(" 格（其中有 cel 可画 ").Append(beyondWithCel)
              .Append("）已探索格到玩家最大曼哈顿距离=").Append(maxMan)
              .Append("（超出圈且可画的最大=").Append(maxManBeyondWithCel).Append("）");

            // ── 屏幕像素坐标（图上画框用）────────────────────────────────────────
            var ov = Probe.V(p, "_overlay") as RectTransform;
            var tw = Probe.V(p, "_texW"); var th = Probe.V(p, "_texH");
            var texW = tw is int ? (int)tw : -1;
            var texH = th is int ? (int)th : -1;
            sb.Append(" | tex=").Append(texW).Append("x").Append(texH);

            var W = 16; var H = 32; var srcW = "hardcoded16";
            var ac = Probe.FindType("Diablo2.Core.AutoMapCel");
            if (ac != null)
            {
                try
                {
                    var fw = ac.GetField("W", BindingFlags.Public | BindingFlags.Static);
                    var fh = ac.GetField("H", BindingFlags.Public | BindingFlags.Static);
                    if (fw != null) { W = (int)fw.GetValue(null); srcW = "AutoMapCel"; }
                    if (fh != null) H = (int)fh.GetValue(null);
                }
                catch { srcW = "AutoMapCel-readfail"; }
            }

            if (ov == null || texW <= 0 || texH <= 0)
            {
                sb.Append(" | PICK skipped (overlay=null 或 tex 尺寸未知)");
                return sb.ToString();
            }

            var k = ov.sizeDelta.x / texW;
            var stepX = W / 2;
            var stepY = W / 4;
            var cv = ov.GetComponentInParent<Canvas>();
            sb.Append(" | overlay sizeDelta=").Append(ov.sizeDelta).Append(" k=").Append(k)
              .Append(" geometryW=").Append(srcW)
              .Append(" canvas=").Append(cv == null ? "null" : cv.renderMode.ToString());

            for (var q = 0; q < picks.Count; q++)
            {
                var i = picks[q];
                var x = i % map.width;
                var y = i / map.width;
                var man = Math.Abs(x - px) + Math.Abs(y - py);
                var tx = ((x - y) + (map.height - 1)) * stepX + W / 2;
                var ty = (x + y) * stepY + H - W / 4;
                var world = ov.TransformPoint(new Vector3((tx - texW * 0.5f) * k, (texH * 0.5f - ty) * k, 0f));
                var sNull = RectTransformUtility.WorldToScreenPoint(null, world);
                var sCam = RectTransformUtility.WorldToScreenPoint(cv != null ? cv.worldCamera : null, world);
                // PNG 像素（左上原点）= (screenX, Screen.height - screenY)
                sb.Append(" | PICK cell=(").Append(x).Append(",").Append(y).Append(") man=").Append(man)
                  .Append(" pngNoCam=(").Append((int)sNull.x).Append(",").Append((int)(Screen.height - sNull.y)).Append(")")
                  .Append(" pngCam=(").Append((int)sCam.x).Append(",").Append((int)(Screen.height - sCam.y)).Append(")");
            }

            var markerCount = Probe.V(p, "_markers") as System.Collections.ICollection;
            sb.Append(" | markers=").Append(markerCount == null ? -1 : markerCount.Count);
            return sb.ToString();
        }
    }

    /// <summary>Installer：spec = "&lt;角色名&gt;|&lt;done marker&gt;|&lt;截图目录&gt;|&lt;walk|loadonly&gt;"。</summary>
    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("LOCEvidenceDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<Driver>();
            d.Init(spec ?? string.Empty);
            Probe.Log("TOUR-INSTALL spec=" + spec);
            Probe.Side("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    public class Driver : MonoBehaviour
    {
        private string _name = "LOCChar1";
        private string _shotDir = string.Empty;
        private bool _loadOnly;
        private int _step;
        private float _at;
        private bool _done;
        private bool _created;
        private bool _walked;
        private Vector2Int _target = new Vector2Int(-1, -1);
        private Vector2Int _start = new Vector2Int(int.MinValue, int.MinValue);
        private bool _shotA;
        private bool _shotB;

        public void Init(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length > 0 && parts[0].Length > 0) _name = parts[0];
            var done = parts.Length > 1 ? parts[1] : string.Empty;
            _shotDir = parts.Length > 2 && parts[2].Length > 0 ? parts[2] : Directory.GetCurrentDirectory();
            _loadOnly = parts.Length > 3 && parts[3].Trim() == "loadonly";
            Probe.Paths(done);
            Probe.Log("DRIVER-MODE loadOnly=" + _loadOnly + " name=\"" + _name + "\" shots=" + _shotDir);
        }

        private void Update()
        {
            if (_done) return;
            try { Step(); }
            catch (Exception ex)
            {
                Probe.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Probe.Side("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                Next();
            }
        }

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private void Next() { _step++; _at = Time.unscaledTime; }
        private static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }
        private static bool MenuOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MainMenuPanel>(); }
        private static bool SelectOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.CharSelectPanel>(); }
        private static bool HudOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>(); }
        private static bool MapPanelOpen() { return Game.UI != null && Game.UI.IsOpen<Diablo2.UI.MiniMapPanel>(); }
        private static void CloseMap() { if (Game.UI != null) Game.UI.Close<Diablo2.UI.MiniMapPanel>(); }

        private string Shot(string leaf) { return Path.Combine(_shotDir, leaf + ".png"); }

        private static string Where()
        {
            var map = Probe.Map(); var p = Probe.Player();
            var area = map != null ? ((int)map.Area).ToString() : "?";
            var gen = map != null && map.IsGenerated ? "1" : "0";
            var me = p != null ? Probe.Grid(p.Grid) : "(null)";
            var sp = map != null ? Probe.Grid(map.SpawnPoint) : "(null)";
            var seed = map != null ? map.Seed.ToString() : "?";
            var explored = map != null && map.ExploredCells != null ? map.ExploredCells.Count : -1;
            return "area=" + area + " gen=" + gen + " seed=" + seed + " me=" + me + " spawn=" + sp
                 + " size=" + (map != null ? map.Width + "x" + map.Height : "?")
                 + " mapExplored=" + explored;
        }

        private static CharacterSave NewChar(string name)
        {
            return new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = name,
                cls = PlayerClass.Amazon,
                level = 1,
                exp = 0,
                str = 20, dex = 25, vit = 20, eng = 15,
                life = 60, mana = 22, stamina = 20,
                statPoints = 0, skillPoints = 0, gold = 0,
                areaId = (int)AreaId.Town,
                gridX = 0, gridY = 0,
                mapSeed = 0,
            };
        }

        /// <summary>找一个距玩家 <paramref name="dist"/> 格左右的可走格（≥4 格起步的兜底）。</summary>
        private static Vector2Int ChooseFarWalkable(int dist)
        {
            var m = Probe.Map(); var p = Probe.Player();
            if (m == null || p == null) return new Vector2Int(-1, -1);
            var me = p.Grid;
            for (var d = dist; d >= 4; d--)
            {
                var cand = new[]
                {
                    new Vector2Int(me.x + d, me.y), new Vector2Int(me.x - d, me.y),
                    new Vector2Int(me.x, me.y + d), new Vector2Int(me.x, me.y - d),
                    new Vector2Int(me.x + d, me.y + d), new Vector2Int(me.x - d, me.y - d),
                    new Vector2Int(me.x + d, me.y - d), new Vector2Int(me.x - d, me.y + d),
                };
                for (var i = 0; i < cand.Length; i++)
                    if (m.InBounds(cand[i]) && m.Walkable(cand[i])) return cand[i];
            }
            return new Vector2Int(-1, -1);
        }

        private void Step() { if (_loadOnly) StepLoadOnly(); else StepWalk(); }

        // ═════════════════════════════════════════════════════════════════════
        // Phase A：创角 → 进营地 → 走 9 格（> 半径 6）→ 开图采图 → 保存并退出
        // ═════════════════════════════════════════════════════════════════════
        private void StepWalk()
        {
            switch (_step)
            {
                case 0:
                    if (MenuOpen() || Fsm() == Events.Fsm.StateMainMenu)
                    {
                        Probe.KV("A-MENU-READY", "fsm=" + Fsm());
                        Probe.Emit(Events.Fsm.TriggerContinue);
                        Next();
                        break;
                    }
                    if (Elapsed(2f))
                    {
                        Probe.KV("A-BOOT-SKIP", "emit " + Events.BootDone + "（启动屏等任意键）");
                        Probe.Emit(Events.BootDone);
                        _at = Time.unscaledTime;
                        break;
                    }
                    if (Elapsed(120f)) { Probe.Warn("启动屏 120s 仍在 " + Fsm() + " ⇒ 硬往下走"); Next(); }
                    break;

                case 1:
                    if (SelectOpen() || Fsm() == Events.Fsm.StateCharSelect || Elapsed(30f))
                    {
                        Probe.KV("A-SELECT", "selectOpen=" + (SelectOpen() ? 1 : 0) + " fsm=" + Fsm());
                        Probe.KV("A-CHAR-CREATE", "name=\"" + _name + "\" emit " + Events.CharCreateRequest);
                        Probe.Emit(Events.CharCreateRequest, NewChar(_name));
                        Next();
                    }
                    break;

                case 2:
                    if (SelectOpen() || Elapsed(15f))
                    {
                        Probe.KV("A-CHAR-CREATED", "selectOpen=" + (SelectOpen() ? 1 : 0) + " fsm=" + Fsm()
                            + " json=" + Probe.Fields(Probe.ReadAll(Probe.SavePath(_name))));
                        Next();
                    }
                    break;

                case 3:
                    if (Elapsed(0.5f))
                    {
                        Probe.KV("A-LOAD", "emit " + Events.CharSelectRequest + "(\"" + _name + "\")  ← 进营地");
                        Probe.Emit(Events.CharSelectRequest, _name);
                        Next();
                    }
                    break;

                case 4:
                    if (HudOpen() && Fsm() == Events.Fsm.StateStage)
                    {
                        Probe.KV("A-AREA-TOWN", Where() + "（走之前：本局刚开始，只有兜底揭示）");
                        Next(); break;
                    }
                    if (Elapsed(90f)) { Probe.Warn("营地进图 90s 未就绪 ⇒ 硬往下走 " + Where()); Next(); }
                    break;

                case 5:
                    if (!_walked)
                    {
                        _start = Probe.PlayerGrid();
                        _target = ChooseFarWalkable(9);
                        if (_target.x < 0) { Probe.Warn("找不到 9 格外的可走格 ⇒ 用 (start.x-8,start.y)"); _target = new Vector2Int(_start.x - 8, _start.y); }
                        Probe.KV("A-WALK", "emit " + Events.MoveCommand + " target=" + Probe.Grid(_target)
                            + " livePlayer=" + Where());
                        Probe.Emit(Events.MoveCommand, _target);
                        _walked = true;
                        _at = Time.unscaledTime;
                        break;
                    }
                    if (Elapsed(6f))
                    {
                        var now = Probe.PlayerGrid();
                        var d = Math.Abs(now.x - _start.x) + Math.Abs(now.y - _start.y);
                        Probe.KV("A-WALKED", "from=" + Probe.Grid(_start) + " to=" + Probe.Grid(now)
                            + " target=" + Probe.Grid(_target) + " 曼哈顿位移=" + d + " " + Where());
                        Next();
                    }
                    break;

                case 6:
                    if (Elapsed(1.0f))
                    {
                        Probe.KV("A-TOGGLE-MAP", "emit " + Events.PanelToggleRequest + "(\"MiniMapPanel\")");
                        Probe.Emit(Events.PanelToggleRequest, "MiniMapPanel");
                        Next();
                    }
                    break;

                case 7:
                    if (MapPanelOpen() || Elapsed(20f))
                    {
                        Probe.KV("A-PANEL-OPEN", "isOpen=" + (MapPanelOpen() ? 1 : 0));
                        Next();
                    }
                    break;

                case 8:
                    if (Elapsed(1.5f))
                    {
                        Probe.KV("A-READINGS", Judge.Read());
                        Probe.KV("A-SHOT", Probe.Shot(Shot("loadonlycove_phaseA")));
                        _shotA = true;
                        Next();
                    }
                    break;

                case 9:
                    if (Elapsed(1.0f))
                    {
                        CloseMap();
                        Probe.KV("A-BEFORE-SAVE", Where());
                        Next();
                    }
                    break;

                // 前片实测：`Shot()` 与 `Emit(SaveAndExitRequest)` 同帧会采到全黑
                //    （`BackToMain` → `LeaveStage` 立刻清场，而 CaptureScreenshot 在帧末读回缓冲）
                //    ⇒ 截图与保存必须**隔帧**（上面 case 8 已隔，这里再隔一次）。
                case 10:
                    if (Elapsed(0.8f))
                    {
                        Probe.KV("A-SAVE-AND-EXIT", "emit " + Events.SaveAndExitRequest);
                        Probe.Emit(Events.SaveAndExitRequest);
                        Next();
                    }
                    break;

                case 11:
                    if (Fsm() == Events.Fsm.StateMainMenu || Elapsed(25f))
                    {
                        Probe.KV("A-AFTER-SAVE-EXIT", "fsm=" + Fsm() + " shotA=" + (_shotA ? 1 : 0));
                        Probe.KV("A-JSON-AFTER-SAVE", Probe.Fields(Probe.ReadAll(Probe.SavePath(_name))));
                        Next();
                    }
                    break;

                default:
                    _done = true;
                    Probe.Done();
                    break;
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // Phase B：**新一局 Play（内存全空）**只读档 → 开图 → 读数 + 采图
        // ═════════════════════════════════════════════════════════════════════
        private void StepLoadOnly()
        {
            switch (_step)
            {
                // 0) 启动屏**等任意键**（UI/BootPanel.OnAnyKey → Events.BootDone）——
                //    前片实测：不补这一下，站点永远停在 Boot，CharSelectRequest 会被丢掉。
                case 0:
                    if (Fsm() == Events.Fsm.StateMainMenu || MenuOpen())
                    {
                        Probe.KV("B-MENU-READY", "fsm=" + Fsm() + "（新一局 Play ⇒ 内存里的探索表是空的）");
                        Probe.Emit(Events.Fsm.TriggerContinue);
                        Next();
                        break;
                    }
                    if (Elapsed(2f))
                    {
                        Probe.KV("B-BOOT-SKIP", "emit " + Events.BootDone);
                        Probe.Emit(Events.BootDone);
                        _at = Time.unscaledTime;
                        break;
                    }
                    if (Elapsed(120f)) { Probe.Warn("启动屏 120s 仍在 " + Fsm() + " ⇒ 硬往下走"); Next(); }
                    break;

                case 1:
                    if (SelectOpen() || Fsm() == Events.Fsm.StateCharSelect || Elapsed(30f))
                    {
                        Probe.KV("B-SELECT", "selectOpen=" + (SelectOpen() ? 1 : 0) + " fsm=" + Fsm());
                        Probe.KV("B-LOAD", "emit " + Events.CharSelectRequest + "(\"" + _name + "\")");
                        Probe.Emit(Events.CharSelectRequest, _name);
                        Next();
                    }
                    break;

                case 2:
                    if (HudOpen() && Fsm() == Events.Fsm.StateStage)
                    {
                        Probe.KV("B-AFTER-LOAD", "读档进图（本局还没开图）" + Where()
                            + " json=" + Probe.Fields(Probe.ReadAll(Probe.SavePath(_name))));
                        Next(); break;
                    }
                    if (Elapsed(90f)) { Probe.Warn("读档进图 90s 未就绪 ⇒ 硬往下走 " + Where()); Next(); }
                    break;

                case 3:
                    if (Elapsed(1.5f))
                    {
                        Probe.KV("B-TOGGLE-MAP", "emit " + Events.PanelToggleRequest + "(\"MiniMapPanel\")");
                        Probe.Emit(Events.PanelToggleRequest, "MiniMapPanel");
                        Next();
                    }
                    break;

                // 前片实测：面板 `IsOpen` 为真 ≠ **已经画出来** ——
                //    面板懒创建 + `OnOpen` 之后 AppSnapshots 才补发「地图回声 + 已探索集合快照」，
                //    开面板当帧截图会采到背后那张世界画面 ⇒ 必须等 1.5s（等待期不许 Next）。
                case 4:
                    if (MapPanelOpen())
                    {
                        if (!_shotB)
                        {
                            if (Elapsed(1.5f))
                            {
                                Probe.KV("B-READINGS", Judge.Read());
                                Probe.KV("B-SHOT", Probe.Shot(Shot("loadonlycove_phaseB_newplay")));
                                _shotB = true;
                                _at = Time.unscaledTime;
                            }
                            break;
                        }
                        Next(); break;
                    }
                    if (Elapsed(12f)) { Probe.Warn("新局读档后 automap 12s 未开 ⇒ 硬往下走 " + Where()); Next(); }
                    break;

                case 5:
                    if (Elapsed(1.0f))
                    {
                        Probe.KV("B-FINAL", Where());
                        Probe.KV("B-READINGS2", Judge.Read());
                        CloseMap();
                        Next();
                    }
                    break;

                default:
                    _done = true;
                    Probe.Done();
                    break;
            }
        }
    }
}
