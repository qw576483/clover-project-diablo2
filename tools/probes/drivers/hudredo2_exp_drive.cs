// =============================================================================
// hudredo2_exp_drive.cs -- hud-redo2 slice: ONE extra step INSIDE an already
//   running Play session (the reused `fontscale_drive.cs` has already navigated
//   boot -> menu -> char select -> stage and left the HUD open; it also already
//   captured the exp = 0 frame `fontscale_01_hud.png`).
//
// WHY THIS FILE EXISTS
//   The judged question is: "does our experience bar look like the original's?"
//   With exp = 0 the bar is legitimately EMPTY, so that question is UNJUDGEABLE
//   from an exp = 0 frame (the task book forbids concluding either way). This
//   driver manufactures exp > 0 and captures one more frame in the SAME session.
//
// HOW IT GETS EXP (real chain first, never invented API)
//   1. Monster.ApplyDamage(id, big, DamageType)        (Module/Contracts.cs:1341)
//      -> Events.MonsterKilled -> DeathFlow.GrantExp (Module/Combat/DeathFlow.cs:138-159)
//      -> Player.AddExp(state.exp)                    (Module/Contracts.cs:1290)
//   2. FALLBACK only if no monster is reachable: Player.AddExp(1000).
//      The summary row records which path was used (`via=kill` / `via=inject`) so
//      an injected number can never be mistaken for a monster kill.
//
// EVIDENCE OUT
//   <rawDir>/hudredo2_exp.png          the frame (ScreenCapture == composited backbuffer)
//   <done>                             one line: exp=... before/after, expNext, via, monsters=n
//   <rawDir>/hudredo2_exp.tsv          step-by-step rows (L3: written by the probe itself)
//
// spec = "<raw dir>|<done>"
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HR2
{
    internal static class H
    {
        internal const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static;

        internal static Type T(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>`Diablo2.App.AppContext.I` —— 全项目公认的上下文单例入口（同 s3_drive.cs）。</summary>
        internal static object Ctx()
        {
            var t = T("Diablo2.App.AppContext");
            if (t == null) return null;
            var p = t.GetProperty("I", BF);
            if (p != null) return p.GetValue(null);
            var f = t.GetField("I", BF);
            return f != null ? f.GetValue(null) : null;
        }

        internal static object CtxMember(string n)
        {
            var c = Ctx();
            if (c == null) return null;
            var f = c.GetType().GetField(n, BF);
            if (f != null) return f.GetValue(c);
            var p = c.GetType().GetProperty(n, BF);
            return p != null ? p.GetValue(c) : null;
        }

        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var t = o.GetType();
            var f = t.GetField(n, BF);
            if (f != null) return f.GetValue(o);
            var p = t.GetProperty(n, BF);
            return p != null ? p.GetValue(o) : null;
        }

        internal static object Invoke(object o, string name, params object[] args)
        {
            if (o == null) return null;
            var types = new Type[args.Length];
            for (var i = 0; i < args.Length; i++) types[i] = args[i] != null ? args[i].GetType() : typeof(object);
            // 精确匹配也要 try：同一方法名有两个候选时 GetMethod 会抛 AmbiguousMatchException，
            // 不挡住它就会从 Update() 里抛出去、把整条链卡死（探针的自伤）。
            MethodInfo m = null;
            try { m = o.GetType().GetMethod(name, BF, null, types, null); }
            catch (Exception ex) { Warn("INVOKE-AMBIGUOUS " + name + ": " + ex.GetType().Name); }
            if (m == null)
            {
                foreach (var mm in o.GetType().GetMethods(BF))
                {
                    if (mm.Name != name) continue;
                    var ps = mm.GetParameters();
                    if (ps.Length != args.Length) continue;
                    var ok = true;
                    for (var i = 0; i < ps.Length; i++)
                    {
                        if (args[i] == null) continue;
                        if (!ps[i].ParameterType.IsInstanceOfType(args[i])) { ok = false; break; }
                    }
                    if (ok) { m = mm; break; }
                }
            }
            if (m == null) { Warn("INVOKE-MISS " + name + " on " + o.GetType().Name); return null; }
            try { return m.Invoke(o, args); }
            catch (Exception ex) { Warn("INVOKE-FAIL " + name + ": " + ex.GetType().Name + " " + ex.Message); return null; }
        }

        internal static long AsLong(object o, long dflt = -1)
        {
            if (o == null) return dflt;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return dflt; }
        }

        internal static int AsInt(object o, int dflt = -1) { return (int)AsLong(o, dflt); }

        internal static List<object> AsList(object o)
        {
            var list = new List<object>();
            var e = o as IEnumerable;
            if (e == null) return list;
            foreach (var x in e) list.Add(x);
            return list;
        }

        internal static object EnumValue(string typeName, string member)
        {
            var t = T(typeName);
            if (t == null) { Warn("ENUM-MISS type " + typeName); return null; }
            try { return Enum.Parse(t, member); }
            catch (Exception ex) { Warn("ENUM-FAIL " + typeName + "." + member + ": " + ex.Message); return null; }
        }

        internal static void Log(string m) { Debug.Log("[HR2] " + m); }
        internal static void Warn(string m) { Debug.LogWarning("[HR2] " + m); }
    }

    /// <summary>Public one-shot entry for `run_script`.</summary>
    public static class Tour
    {
        /// <summary>Install with spec = "&lt;raw dir&gt;|&lt;done&gt;".</summary>
        public static string Install(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            var raw = parts.Length > 0 ? parts[0] : string.Empty;
            var done = parts.Length > 1 ? parts[1] : string.Empty;
            var go = new GameObject("HudRedo2ExpDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var d = go.AddComponent<ExpDriver>();
            d.RawDir = raw;
            d.DonePath = done;
            H.Log("INSTALL raw=\"" + raw + "\" done=\"" + done + "\"");
            return "INSTALLED";
        }
    }

    /// <summary>Reads exp, manufactures exp &gt; 0, captures ONE frame, writes the done marker.</summary>
    public class ExpDriver : MonoBehaviour
    {
        public string RawDir = string.Empty;
        public string DonePath = string.Empty;

        private readonly List<string> _rows = new List<string>();
        private int _step;
        private float _at;
        private long _expBefore = -1;
        private long _expAfter = -1;
        private long _expNext = -1;
        private int _level = -1;
        private int _monsters;
        private string _via = "(none)";
        private string _shotPath = string.Empty;
        private bool _shotIssued;
        private int _shotFrame = -1;

        private void Start()
        {
            _at = Time.unscaledTime;
            Row("t", "step", "key=value");
            H.Log("START frame=" + Time.frameCount);
        }

        private void Row(params string[] cells)
        {
            _rows.Add(string.Join("\t", cells));
        }

        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }

        private void Next() { _step++; _at = Time.unscaledTime; }

        private void Update()
        {
            switch (_step)
            {
                case 0:
                    // 让 fontscale 驱动留下的局面稳定下来（它刚在 HUD 上截过图）
                    if (!Elapsed(2.0f)) return;
                    Next(); return;

                case 1:
                    {
                        var player = H.CtxMember("Player");
                        var monster = H.CtxMember("Monster");
                        if (player == null || monster == null)
                        {
                            if (Elapsed(30f)) { H.Warn("CTX-MISSING player=" + (player != null) + " monster=" + (monster != null)); Finish("ctx-missing"); }
                            return;
                        }
                        _expBefore = H.AsLong(H.Field(player, "Exp"));
                        _expNext = H.AsLong(H.Field(player, "ExpNext"));
                        _level = H.AsInt(H.Field(player, "Level"));
                        var all = H.AsList(H.Field(monster, "All"));
                        _monsters = all.Count;
                        Row("read", "expBefore=" + _expBefore + " expNext=" + _expNext
                            + " level=" + _level + " monsters=" + _monsters);

                        // 优先走**真实击杀链**：对第一只有生命的怪打一次致命伤害
                        object target = null;
                        foreach (var m in all)
                        {
                            if (H.AsInt(H.Field(m, "hp"), 0) > 0) { target = m; break; }
                        }
                        if (target != null)
                        {
                            var dmg = H.EnumValue("Diablo2.Def.DamageType", "Physical");
                            if (dmg != null)
                            {
                                var id = H.AsInt(H.Field(target, "id"));
                                Row("kill-attempt", "monster=" + H.Field(target, "name") + " id=" + id
                                    + " hp=" + H.AsLong(H.Field(target, "hp")));
                                H.Invoke(monster, "ApplyDamage", id, 1000000, dmg);
                                _via = "kill";
                            }
                            else { Row("kill-skip", "DamageType.Physical 取不到 ⇒ 走注入兜底"); }
                        }
                        else { Row("kill-skip", "当前区域没有可打的怪（monsters=" + _monsters + "）⇒ 走注入兜底"); }
                        Next(); return;
                    }

                case 2:
                    if (!Elapsed(1.5f)) return;
                    {
                        var player = H.CtxMember("Player");
                        _expAfter = H.AsLong(H.Field(player, "Exp"));

                        // 为什么杀完还要 top-up：小怪给的几十一百点经验，在 486.94 原版px 宽、填充比
                        // = exp/expNext 的条上可能只有 1~2 画布px 宽 ⇒ 判「看不看得见填充段」会变成判像素噪声。
                        // 把经验补齐到 **expNext 的 50%**（⇒ 明显可见），且**始终留在阈值以下**（不触发升级，
                        // 免得升级改动生命/法力上限把画面搅浑）。
                        // 来源必须一眼可辨：真击杀成功 = `via=kill+topup`；无怪可打 = `via=inject+topup`。
                        var want = _expNext > 0 ? (long)(_expNext * 0.5) : 0L;
                        if (want > _expAfter)
                        {
                            // 公开 API `IPlayerModule.AddExp`（Module/Contracts.cs:1290；
                            // 实现在 Module/Player/PlayerModule.cs:1294，末尾 EmitStats() ⇒ HUD 会刷新）
                            H.Invoke(player, "AddExp", (int)(want - _expAfter));
                            _via = (_via == "kill" ? "kill" : "inject") + "+topup";
                            _expAfter = H.AsLong(H.Field(player, "Exp"));
                        }
                        else if (_via == "(none)") { _via = "kill(no-topup-needed)"; }

                        Row("expAfter", "via=" + _via + " exp=" + _expBefore + "->" + _expAfter
                            + " expNext=" + _expNext + " level=" + _level
                            + " topupTarget=" + want + "(=0.5*expNext, below level-up threshold)");
                        Next(); return;
                    }

                case 3:
                    {
                        // 抓图：ScreenCapture 写的是**本帧之后**渲染出来的那帧 ⇒ 先请求，再等 WaitForEndOfFrame
                        if (!_shotIssued)
                        {
                            _shotPath = Path.Combine(RawDir, "hudredo2_exp.png");
                            try
                            {
                                if (!string.IsNullOrEmpty(RawDir) && !Directory.Exists(RawDir)) Directory.CreateDirectory(RawDir);
                                ScreenCapture.CaptureScreenshot(_shotPath);
                                _shotIssued = true;
                                _shotFrame = Time.frameCount;
                                Row("shot-issued", "file=" + _shotPath + " frame=" + _shotFrame);
                            }
                            catch (Exception ex) { Row("shot-fail", ex.GetType().Name + ": " + ex.Message); }
                            return;                       // 本帧不改任何状态
                        }
                        if (Time.frameCount - _shotFrame < 2) return;   // 等落盘
                        Row("shot-done", "exists=" + File.Exists(_shotPath) + " bytes="
                            + (File.Exists(_shotPath) ? new FileInfo(_shotPath).Length : 0));
                        Next(); return;
                    }

                default:
                    Finish("ok");
                    return;
            }
        }

        private void Finish(string reason)
        {
            var summary = "reason=" + reason + " exp=" + _expBefore + "->" + _expAfter + " expNext=" + _expNext
                          + " level=" + _level + " monsters=" + _monsters + " via=" + _via
                          + " shot=" + (File.Exists(_shotPath) ? Path.GetFileName(_shotPath) : "(missing)");
            try
            {
                if (!string.IsNullOrEmpty(RawDir))
                {
                    if (!Directory.Exists(RawDir)) Directory.CreateDirectory(RawDir);
                    File.WriteAllLines(Path.Combine(RawDir, "hudredo2_exp.tsv"), _rows.ToArray());
                }
                if (!string.IsNullOrEmpty(DonePath)) File.WriteAllText(DonePath, summary);
            }
            catch (Exception ex) { H.Warn("WRITE-FAIL " + ex.GetType().Name + ": " + ex.Message); }
            H.Log("SUMMARY " + summary);
            Destroy(gameObject);
        }
    }
}
