// =============================================================================
// msc_probe.cs -- melee-samecell: drive the REAL left-click gesture from a SAME-CELL posture.
//
// WHY THIS FILE EXISTS
//   `s2_drive.cs + av2_probe.cs` (both REUSED, unchanged) already proved the production chain:
//   the player walks to the monster by itself and, at distance 0, EVERY attack used to be
//   rejected by the melee shape gate ("判定形状 拒绝 ... 偏移=(0,0)" x40, report-audioverify2 2.3).
//   After the MeleeShape fix, the same chain must RESOLVE damage.  That chain, however, only
//   reaches distance 0 by luck (the monster has to step onto the player).  This probe removes
//   the luck: it puts the player ON the monster's cell with the production API
//   IPlayerModule.TeleportTo (the same "walk ended on the monster's cell" end state), then fires
//   the REAL gesture PlayerModule.HandlePrimaryClick(grid) -- the very method InputReader calls
//   for "left click on a monster" -- and reports hp before/after per attempt.
//
//   ⛔ It does NOT re-implement the boot/menu/stage chain (that is s2_drive.cs) and does NOT
//   fake damage: it only calls the two production entries above, exactly like the earlier probe.
//
// OUTPUT (absolute paths, all under .ai-tmp):
//   <shots>/melee2_*.png        the same-cell attack frame(s)
//   <test>/msc2_summary.txt     attempts / dist0 / hp start->final / alive
//   <test>/msc2_done.txt        marker written when the timeline is over
//
// ASCII ONLY (the driver compiles this file from disk; non-ASCII would be read as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Module;
using UnityEngine;

namespace MSC
{
    internal static class P
    {
        internal const string Tag = "MSC";

        internal static string Done = string.Empty;
        internal static string ShotDir = string.Empty;
        internal static string Summary = string.Empty;
        internal static string ShotPrefix = "melee2_";

        internal static string F(float v) { return v.ToString("0.000", CultureInfo.InvariantCulture); }

        internal static void Log(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Info(Tag, msg);
            else UnityEngine.Debug.Log("[" + Tag + "] " + msg);
        }

        internal static void Warn(string msg)
        {
            var logger = Game.Logger;
            if (logger != null) logger.Warn(Tag, msg);
            else UnityEngine.Debug.LogWarning("[" + Tag + "] " + msg);
        }

        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(name);
                if (t != null) return t;
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

        internal static IMonsterModule Monsters() { return CtxMember("Monster") as IMonsterModule; }
        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }

        /// <summary>`PlayerModule.HandlePrimaryClick(Vector2Int)` -- reached by reflection (its
        /// declaring type is not part of the public contract, but the method is what InputReader
        /// calls for a real left click on a monster).</summary>
        internal static MethodInfo PrimaryClick(object player)
        {
            return player == null ? null : player.GetType().GetMethod("HandlePrimaryClick",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector2Int) }, null);
        }

        /// <summary>`IPlayerModule.TeleportTo(Vector2Int)` -- same call the offline host makes
        /// (return value ignored on purpose: void or bool both compile through Invoke).</summary>
        internal static MethodInfo Teleport(object player)
        {
            return player == null ? null : player.GetType().GetMethod("TeleportTo",
                BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Vector2Int) }, null);
        }
    }

    /// <summary>The whole timeline in ONE assembly: settle -> same-cell left clicks -> summary.</summary>
    public class SameCellRunner : MonoBehaviour
    {
        private float _t0 = -1f;
        private int _step;
        private int _target = -1;
        private string _name = "-";
        private int _attempts;
        private int _dist0;
        private int _hpStart = -1;
        private int _hpMin = -1;
        private float _next;
        private int _shots;

        private void Update()
        {
            if (_t0 < 0f) { _t0 = Time.unscaledTime; return; }
            var t = Time.unscaledTime - _t0;

            if (_step == 0 && t >= 1.0f) { Begin(); _step = 1; _next = t + 1.0f; return; }
            if (_step != 1) return;

            if (_attempts < 12 && t >= _next) { Attack(); _next = t + 0.9f; return; }
            if (_attempts >= 12 || t >= 26f) { Finish(); _step = 2; }
        }

        private void Begin()
        {
            var m = P.Monsters();
            var pl = P.Player();
            if (m == null || pl == null)
            {
                P.Warn("BEGIN no modules (Monster=" + (m == null) + " Player=" + (pl == null) + ") -- retry");
                _t0 = Time.unscaledTime;      // retry from scratch
                return;
            }
            var id = Nearest(m, pl);
            if (id < 0) { P.Warn("BEGIN no alive monster near -- retry"); _t0 = Time.unscaledTime; return; }
            _target = id;
            var st = m.Get(id);
            _name = st != null ? st.name : "-";
            _hpStart = st != null ? st.hp : -1;
            _hpMin = _hpStart;
            P.Log("BEGIN target=" + id + " " + _name + " grid=(" + (st != null ? st.gridX : -1) + "," + (st != null ? st.gridY : -1)
                  + ") player=(" + pl.Grid.x + "," + pl.Grid.y + ") hp=" + _hpStart + " t=" + P.F(Time.unscaledTime));
            if (_shots < 1) { Shot("1_before.png"); }
        }

        private void Attack()
        {
            var m = P.Monsters();
            var pl = P.Player();
            if (m == null || pl == null) return;

            var st = m.Get(_target);
            if (st == null || !st.alive)
            {
                var id = Nearest(m, pl);
                if (id < 0) { P.Log("NO-TARGET left alive -- stopping"); _attempts = 12; return; }
                _target = id; st = m.Get(id); _name = st != null ? st.name : "-";
                P.Log("RETARGET -> " + _target + " " + _name);
            }

            var grid = new Vector2Int(st.gridX, st.gridY);
            var tp = P.Teleport(pl);
            if (tp == null) { P.Warn("SAME-CELL TeleportTo not found on " + pl.GetType().FullName); _attempts = 12; return; }
            try { tp.Invoke(pl, new object[] { grid }); }
            catch (Exception ex) { P.Warn("SAME-CELL TeleportTo threw " + ex.GetType().Name + ": " + ex.Message); }

            var onCell = pl.Grid.x == grid.x && pl.Grid.y == grid.y;
            if (onCell) _dist0++;

            _attempts++;
            var hpBefore = st.hp;
            var meth = P.PrimaryClick(pl);
            if (meth == null) { P.Warn("SAME-CELL HandlePrimaryClick not found on " + pl.GetType().FullName); _attempts = 12; return; }
            try { meth.Invoke(pl, new object[] { grid }); }
            catch (Exception ex) { P.Warn("SAME-CELL HandlePrimaryClick threw " + ex.GetType().Name + ": " + ex.Message); }

            var st2 = m.Get(_target);
            var hpAfter = st2 != null ? st2.hp : -1;
            var alive = st2 != null && st2.alive;
            if (hpAfter >= 0 && (hpAfter < _hpMin || _hpMin < 0)) _hpMin = hpAfter;
            P.Log("SAME-CELL n=" + _attempts + " id=" + _target + " " + _name
                  + " grid=(" + grid.x + "," + grid.y + ") player=(" + pl.Grid.x + "," + pl.Grid.y + ")"
                  + " sameCell=" + (onCell ? 1 : 0) + " dir=" + pl.Dir
                  + " hp=" + hpBefore + "->" + hpAfter + " alive=" + (alive ? 1 : 0)
                  + " t=" + P.F(Time.unscaledTime));

            if (_attempts == 2) { Shot("2_samecell.png"); }
        }

        private void Finish()
        {
            var m = P.Monsters();
            var st = m != null ? m.Get(_target) : null;
            var hpFinal = st != null ? st.hp : -1;
            var alive = st != null && st.alive;
            if (alive && _shots < 3) { Shot("3_after.png"); }
            P.Log("SUMMARY attempts=" + _attempts + " sameCellPoses=" + _dist0
                  + " target=" + _target + " " + _name
                  + " hpStart=" + _hpStart + " hpMin=" + _hpMin + " hpFinal=" + hpFinal
                  + " alive=" + (alive ? 1 : 0) + " shots=" + _shots);

            try
            {
                File.WriteAllText(P.Summary,
                    "melee-samecell summary\n"
                    + "attempts=" + _attempts + "\n"
                    + "sameCellPoses=" + _dist0 + "\n"
                    + "target id=" + _target + " name=" + _name + "\n"
                    + "hpStart=" + _hpStart + " hpMin=" + _hpMin + " hpFinal=" + hpFinal + "\n"
                    + "alive=" + (alive ? 1 : 0) + "\n"
                    + "shots=" + _shots + "\n");
            }
            catch (Exception ex) { P.Warn("SUMMARY write failed " + ex.GetType().Name); }

            try { File.WriteAllText(P.Done, "done " + P.F(Time.unscaledTime)); }
            catch (Exception ex) { P.Warn("DONE write failed " + ex.GetType().Name); }
            P.Log("DONE-FILE " + P.Done);
        }

        private void Shot(string name)
        {
            try
            {
                var p = Path.Combine(P.ShotDir, P.ShotPrefix + name);
                ScreenCapture.CaptureScreenshot(p);
                _shots++;
                P.Log("SHOT " + name);
            }
            catch (Exception ex) { P.Warn("SHOT-FAIL " + name + " " + ex.GetType().Name); }
        }

        private static int Nearest(IMonsterModule m, IPlayerModule pl)
        {
            var all = m.All;
            var best = -1;
            var bestD = int.MaxValue;
            for (var i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (s == null || !s.alive) continue;
                var d = Mathf.Max(Mathf.Abs(s.gridX - pl.Grid.x), Mathf.Abs(s.gridY - pl.Grid.y));
                if (d < bestD) { bestD = d; best = s.id; }
            }
            return best;
        }
    }

    public static class Api
    {
        /// <summary>spec = "shotDir|done|summary|prefix".</summary>
        public static string SameCell(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length < 4) return "ERR spec needs 4 fields";
            P.ShotDir = parts[0];
            P.Done = parts[1];
            P.Summary = parts[2];
            P.ShotPrefix = parts[3];

            var go = new GameObject("msc_samecell");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<SameCellRunner>();
            return "STARTED dir=" + P.ShotDir + " prefix=" + P.ShotPrefix;
        }
    }
}
