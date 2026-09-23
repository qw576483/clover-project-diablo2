// =============================================================================
// lineclear_probe.cs -- lineclear-fix: LIVE evidence for
//   "a monster whose attack line is blocked by terrain must NOT swing" (the
//   "monster swings at a wall forever" defect, registered by slice melee-ai-why).
//
// Why a Play session is required (and why the offline host is not enough here):
//   the offline host combatcheck 20 drives MonsterModule.Tick directly.  What the
//   user actually sees is the LIVE view layer: the monster's own `attacking` flag
//   (MonsterRuntime.Sync -> State.attacking) + its attack sound.  This probe
//   samples that flag FRAME BY FRAME in the real loop, on the real map, with the
//   real player standing on the far side of the wall.
//
// What it does (nothing is re-implemented, no product code is touched):
//   1. waits until the reused s2_drive.cs chain has left the player in Blood Moor
//      with monsters alive;
//   2. picks a live RANGE/SHAMAN monster, then finds a player cell inside the
//      firing band [RangedKeepDistance, RangedAttackMaxDistance] whose attack
//      line is BLOCKED -- the blocker test is the PRODUCT'S OWN ruler, called by
//      reflection (`CombatModule.AttackLineClear`, the same method
//      `RequestMonsterAttack` uses), never a copy of it;
//   3. teleports the player there through the public contract
//      (`IPlayerModule.TeleportTo`), then samples for ~12 s:
//        * blocked?                 (per frame, same ruler)
//        * `State.attacking` RISING EDGES = actual swings, attributed to the frame
//        * the monster's grid + player grid + player hp
//   4. writes 3 screenshots (placed / +2 s / +6 s) so a human can see the wall
//      between the monster and the player, then the summary + the done marker.
//
// Verdict lines (all L3 = written by the running program itself):
//   [LCL] PICK / PLACED / BLOCKED-FRAMES / SWINGS-WHILE-BLOCKED / DONE
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using UnityEngine;

namespace LCL
{
    internal static class P
    {
        internal const string Tag = "LCL";

        internal static string Summary = string.Empty;
        internal static string Done = string.Empty;
        internal static string ShotDir = string.Empty;
        internal static string ShotPrefix = "lcl_";
        internal static string Phase = "-";

        private static readonly object Gate = new object();

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

        internal static void Say(string line)
        {
            Log(line);
            if (string.IsNullOrEmpty(Summary)) return;
            try { lock (Gate) { File.AppendAllText(Summary, line + "\n"); } }
            catch (Exception ex) { Warn("SUM-FAIL " + ex.GetType().Name); }
        }

        // ---- reflection into AppContext (same shape as av2_probe.cs / s2_drive.cs) ----
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
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }

        internal delegate bool LineClearFn(Vector2Int a, Vector2Int b);

        /// <summary>
        /// The PRODUCT'S own "is this attack line clear?" ruler
        /// (`Diablo2.Module.Combat.CombatModule.AttackLineClear`, internal + static) --
        /// the very method `RequestMonsterAttack` calls.  Resolved by reflection only
        /// because the class is internal; the probe never re-implements the test.
        /// </summary>
        internal static LineClearFn LineClear()
        {
            var t = FindType("Diablo2.Module.Combat.CombatModule");
            if (t == null) return null;
            var mi = t.GetMethod("AttackLineClear", BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(Vector2Int), typeof(Vector2Int) }, null);
            if (mi == null) return null;
            return (LineClearFn)mi.CreateDelegate(typeof(LineClearFn));
        }

        internal static string G(Vector2Int g) { return g.x + "," + g.y; }

        /// <summary>
        /// Move ONE monster runtime to a cell: `MonsterModule._all` (private) ->
        /// `MonsterRuntime.SnapTo(grid)` (public method, the same call the spawner makes).
        /// Needed because the wall pair has to be constructed where the wall IS, and the
        /// monster must be the actor we watch (the AI cannot be asked to walk there).
        /// </summary>
        internal static bool SnapMonsterTo(IMonsterModule monsters, int id, Vector2Int g)
        {
            var f = monsters.GetType().GetField("_all", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(monsters) as System.Collections.IEnumerable : null;
            if (list == null) { Warn("SNAP _all not found"); return false; }
            foreach (var rt in list)
            {
                if (rt == null) continue;
                var sf = rt.GetType().GetField("State", BindingFlags.Public | BindingFlags.Instance);
                var st = sf != null ? sf.GetValue(rt) as MonsterState : null;
                if (st == null || st.id != id) continue;
                var mi = rt.GetType().GetMethod("SnapTo", BindingFlags.Public | BindingFlags.Instance,
                    null, new[] { typeof(Vector2Int) }, null);
                if (mi == null) { Warn("SNAP SnapTo not found"); return false; }
                mi.Invoke(rt, new object[] { g });
                return true;
            }
            Warn("SNAP monster m#" + id + " not found in _all");
            return false;
        }
    }

    /// <summary>Places the player on the far side of a wall and samples the swing flag per frame.</summary>
    public class Sampler : MonoBehaviour
    {
        private float _start;
        private float _deadline;
        private float _warmup;
        private int _phase;                  // 0 = wait for monsters, 1 = sampling
        private MonsterState _target;
        private bool _prevAttacking;

        private int _frames;
        private int _blockedFrames;
        private int _swingsTotal;
        private int _swingsWhileBlocked;
        private int _shots;
        private float _nextShot;
        private float _nextBeat;

        private void Update()
        {
            try
            {
                if (_phase == 0) { WaitAndPlace(); return; }
                Sample();
            }
            catch (Exception ex)
            {
                P.Warn("SAMPLER-FAIL " + ex.GetType().Name + ": " + ex.Message);
                Finish("EXCEPTION " + ex.GetType().Name);
            }
        }

        private void Start()
        {
            _start = Time.unscaledTime;
            P.Log("START t=" + P.F(_start) + " waiting for the reused s2 chain to leave us in the field");
        }

        // ── phase 0: wait for monsters, then place the player behind the wall ──────
        private void WaitAndPlace()
        {
            if (Time.unscaledTime - _start < 6f) return;     // let the s2 tour settle

            var monsters = P.Monsters();
            if (monsters == null) { P.Warn("PICK no MonsterModule"); return; }

            var lc = P.LineClear();
            if (lc == null) { P.Warn("PICK AttackLineClear not found (reflection) -> abort"); Finish("NO-RULER"); return; }

            var map = P.Map();
            var player = P.Player();
            if (map == null || player == null) { P.Warn("PICK no map/player"); return; }
            if (!map.IsGenerated) { P.Warn("PICK map not generated yet"); return; }

            // the firing band, verbatim from MonsterAi: [RangedKeepDistance, min(RangedRange, RangedAttackMaxRange)]
            const float keep = 4.0f;          // MonsterTuning.RangedKeepDistance
            const float cap = 5.0f;           // MonsterTuning.RangedAttackMaxRange
            var lo = keep + 0.05f;
            var hi = cap;

            // census (diagnostics: "no ranged monster" and "no wall in the band" are different failures)
            var total = 0;
            var alive = 0;
            var ranged = 0;
            MonsterState pick = null;
            foreach (var m in monsters.All)
            {
                if (m == null) continue;
                total++;
                if (!m.alive) continue;
                alive++;
                if (m.ai != MonsterAI.Range && m.ai != MonsterAI.Shaman) continue;
                ranged++;
                if (pick == null) pick = m;
            }
            P.Say("CENSUS monsters=" + total + " alive=" + alive + " rangedOrShaman=" + ranged
                  + " | player=" + P.G(player.Grid) + " hp=" + player.Life + "/" + player.MaxLife
                  + " | mapGenerated=" + (map.IsGenerated ? 1 : 0));
            if (pick == null) { P.Warn("PICK no alive ranged/shaman monster on this map -> nothing to sample"); Finish("NO-CASE-NO-RANGED"); return; }

            // Build the pair ourselves, around the PLAYER's current grid (highest hit rate: the
            // player stands in terrain that actually has obstacles).  Ranked so the monster sits
            // near the player's screen height (|dx+dy| small in the iso projection) => the three
            // screenshots really contain both actors.
            var pg0 = player.Grid;
            var bestA = Vector2Int.zero;
            var bestB = Vector2Int.zero;
            var bestScore = int.MaxValue;
            float bestD = 0f;
            var walkableBand = 0;
            var blockedBand = 0;

            for (var ax = -12; ax <= 12; ax++)
            {
                for (var ay = -12; ay <= 12; ay++)
                {
                    var a = new Vector2Int(pg0.x + ax, pg0.y + ay);
                    if (!map.InBounds(a) || !map.Walkable(a)) continue;
                    for (var bx = -8; bx <= 8; bx++)
                    {
                        for (var by = -8; by <= 8; by++)
                        {
                            var b = new Vector2Int(a.x + bx, a.y + by);
                            if (!map.InBounds(b) || !map.Walkable(b)) continue;
                            var d = Iso.GridDistanceEuclidean(a, b);
                            if (d < lo || d > hi) continue;          // inside the firing band
                            walkableBand++;
                            if (lc(a, b)) continue;                  // line still clear -> not a case
                            blockedBand++;
                            var score = Math.Abs(bx + by);           // screen-vertical offset
                            if (score >= bestScore) continue;
                            bestScore = score;
                            bestA = a;
                            bestB = b;
                            bestD = d;
                        }
                    }
                }
            }

            P.Say("SCAN aroundPlayer=" + P.G(pg0) + " bandCells=" + walkableBand + " blockedBandCells=" + blockedBand
                  + " -> A(monster)=" + P.G(bestA) + " B(player)=" + P.G(bestB)
                  + " d=" + P.F(bestD) + " screenVertOffset=" + bestScore + " blocked=" + (bestScore == int.MaxValue ? 1 : (lc(bestA, bestB) ? 1 : 0)));
            if (bestScore == int.MaxValue)
            {
                P.Warn("PICK no (A,B) pair with a terrain-blocked line inside the firing band -> nothing to sample");
                Finish("NO-CASE-NO-BLOCKED-BAND");
                return;
            }

            // move the watched monster to A, then the player to B
            if (!P.SnapMonsterTo(monsters, pick.id, bestA))
            {
                P.Warn("PICK could not move m#" + pick.id + " to " + P.G(bestA) + " -> abort");
                Finish("NO-CASE-SNAP-FAILED");
                return;
            }
            var spot = bestB;
            P.Log("SHOT-WANT placed-after-snap");

            P.Say("PICK m#" + pick.id + " " + pick.name + " ai=" + pick.ai + " -> " + P.G(bestA)
                  + " | player->" + P.G(spot) + " | gridDist=" + P.F(Iso.GridDistanceEuclidean(bestA, spot))
                  + " | LineClear=" + lc(bestA, spot) + " (class=BLOCKED)");

            player.TeleportTo(spot);
            P.Say("PLACED player=" + P.G(player.Grid) + " hp=" + player.Life + "/" + player.MaxLife);
            P.Log("SHOT-WANT placed");

            _target = pick;
            _deadline = Time.unscaledTime + 12f;
            _warmup = Time.unscaledTime + 0.8f;      // discard frames until SnapTo has been Synced
            _nextShot = Time.unscaledTime + 0.8f;
            _nextBeat = Time.unscaledTime + 1f;
            _phase = 1;
        }

        // ── phase 1: per-frame sampling of "blocked?" and the swing rising edge ─────
        private void Sample()
        {
            var lc = P.LineClear();
            var player = P.Player();
            var m = _target;
            if (lc == null || player == null || m == null) { Finish("LOST-REFS"); return; }
            if (Time.unscaledTime < _warmup) { _prevAttacking = m.attacking; return; }   // see _warmup

            var mg = new Vector2Int(m.gridX, m.gridY);
            var pg = player.Grid;
            var blocked = !lc(mg, pg);
            var attacking = m.attacking;
            var edge = attacking && !_prevAttacking;
            _prevAttacking = attacking;

            _frames++;
            if (blocked) _blockedFrames++;
            if (edge) _swingsTotal++;
            if (blocked && edge) _swingsWhileBlocked++;

            if (Time.unscaledTime >= _nextShot && _shots < 3)
            {
                _shots++;
                Shot("lineclear_" + _shots + ".png");
                _nextShot = Time.unscaledTime + 3f;
            }

            if (Time.unscaledTime >= _nextBeat)
            {
                _nextBeat = Time.unscaledTime + 2f;
                P.Say("BEAT t=" + P.F(Time.unscaledTime) + " frames=" + _frames
                      + " blockedFrames=" + _blockedFrames
                      + " swingsWhileBlocked=" + _swingsWhileBlocked
                      + " swingsTotal=" + _swingsTotal
                      + " | m#" + m.id + " " + P.G(mg) + " attacking=" + (attacking ? 1 : 0)
                      + " | player " + P.G(pg) + " hp=" + player.Life + "/" + player.MaxLife
                      + " dead=" + (player.IsDead ? 1 : 0) + " blocked=" + (blocked ? 1 : 0));
            }

            if (Time.unscaledTime < _deadline) return;

            // ---- verdict (only the PRODUCT behaviour is judged; nothing is relaxed) ----
            P.Say("BLOCKED-FRAMES " + _blockedFrames + "/" + _frames
                  + " (a case that never blocked would be vacuous)");
            P.Say("SWINGS-WHILE-BLOCKED " + _swingsWhileBlocked
                  + " (the fix's judged quantity; must be 0)");
            P.Say("SWINGS-TOTAL " + _swingsTotal + " (clear-line swings are expected and must stay possible)");
            Finish(_blockedFrames > 0 ? "OK" : "VACUOUS-NO-BLOCKED-FRAME");
        }

        private void Finish(string why)
        {
            P.Say("DONE " + why + " t=" + P.F(Time.unscaledTime));
            try { File.WriteAllText(P.Done, "LCL-DONE " + why + " t=" + P.F(Time.unscaledTime) + "\n"); }
            catch (Exception ex) { P.Warn("DONE-FAIL " + ex.GetType().Name); }
            enabled = false;
        }

        private void Shot(string name)
        {
            try
            {
                var path = Path.Combine(P.ShotDir, P.ShotPrefix + name);
                ScreenCapture.CaptureScreenshot(path);
                P.Say("SHOT " + path);
            }
            catch (Exception ex) { P.Warn("SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }
    }

    /// <summary>Entry point: `unity command run_script --file lineclear_probe.cs --entry LCL.Api.Start`.</summary>
    public static class Api
    {
        /// <summary>spec = "&lt;summary&gt;|&lt;done marker&gt;|&lt;shot dir&gt;|&lt;shot prefix&gt;".</summary>
        public static string Start(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length < 4) return "ERR spec needs 4 fields";
            P.Summary = parts[0];
            P.Done = parts[1];
            P.ShotDir = parts[2];
            P.ShotPrefix = parts[3];

            try
            {
                if (!Directory.Exists(P.ShotDir)) Directory.CreateDirectory(P.ShotDir);
                File.WriteAllText(P.Summary, "# lineclear-fix live evidence (fresh)\n");
            }
            catch (Exception ex) { return "ERR " + ex.GetType().Name + ": " + ex.Message; }

            var go = new GameObject("LCLRoot");
            go.AddComponent<Sampler>();
            UnityEngine.Object.DontDestroyOnLoad(go);
            P.Log("STARTED summary=" + P.Summary + " shots=" + P.ShotDir);
            return "LCL-OK";
        }
    }

    internal static class MonsterStateExt
    {
        internal static Vector2Int GridOf(this MonsterState s) { return new Vector2Int(s.gridX, s.gridY); }
    }
}
