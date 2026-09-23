// =============================================================================
// av2_probe.cs -- audio-verify2: the TWO tails play-verify could not capture.
//
//   (1) monster_hit_*  (the monster's OWN gethit sound, and the player-side impact `hit`)
//   (2) the monster HURT animation FRAME sequence (the E28 "only 3 of 7 frames" question)
//
// WHY THIS FILE EXISTS (read report-playverify.md 8.2):
//   * The previous probe called IMonsterModule.ApplyDamage(id, 3) on a 3-hp monster =>
//     instant death => no "hurt but alive" window => monster_hit_* never had a chance.
//     ALSO: ApplyDamage does NOT call IViewModule.PlayHit -- the hurt ANIMATION is started by
//     DamagePipeline.ApplyToMonster only (client/Assets/Scripts/Module/Combat/DamagePipeline.cs:119),
//     which is reachable only through the real click path (Events.AttackRequest).
//   * run_script compiles each called .cs into ITS OWN assembly, so static fields set by
//     Install() are empty in later calls (measured, see report-playverify.md 8.3).  Therefore
//     EVERYTHING here runs inside ONE assembly and ONE call: the Runner component owns the whole
//     timeline, and the two watchers are created from it.
//
// WHAT IT DOES (read-only w.r.t. product state except the two documented actions below):
//   * ClipWatch  : per-frame OFF->ON transition of every AudioSource => records the clip that
//                  REALLY started, with Time.unscaledTime (so two staggered sounds can be timed).
//   * FrameWatch : per-frame sprite (name + rect) of ONE monster's render node, through the
//                  public contract IViewModule.GetView(id).  Each CHANGE = one rendered frame.
//   * Runner     : picks the TANKIEST alive monster, then drives the REAL player gesture
//                  PlayerModule.HandlePrimaryClick(grid) -- the same method InputReader calls for
//                  "left click on a monster": it resolves the hover target at that grid, emits
//                  Events.AttackRequest AND emits a MoveCommand so the player walks adjacent.
//                  (Direct Emit(AttackRequest) alone is rejected by the melee shape gate when the
//                  player is out of range / not facing the target -- see CombatModule.cs:148-173.)
//   * Runner also does ONE non-lethal ApplyDamage(1) on the same monster as a second, independent
//     route to monster_hit_* (the hp>0 branch schedules the gethit sound with MonSounds.HitDelay).
//
// OUTPUT (all absolute paths, all inside .ai-tmp):
//   <test>/av2_events.tsv        kind  t  phase  detail...      (SFX / ANIM / CLICK / NOTE)
//   <test>/av2_anim_frames.tsv   t  sprite  rect  action  frameIdx  n   <- the required frame seq
//   <test>/av2_summary.txt       counters + the tankiest-monster pick
//   <test>/av2_done.txt          marker written when the timeline is over
//   <shots>/av2_*.png            screenshots
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII .cs as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Module;
using UnityEngine;

namespace AV2
{
    internal static class P
    {
        internal const string Tag = "AV2";

        internal static string EvTsv = string.Empty;     // av2_events.tsv
        internal static string AnimTsv = string.Empty;   // av2_anim_frames.tsv
        internal static string Summary = string.Empty;   // av2_summary.txt
        internal static string ShotDir = string.Empty;   // .ai-tmp/screenshots
        internal static string Done = string.Empty;      // av2_done.txt
        internal static string ShotPrefix = "av2_";      // screenshot name prefix
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

        internal static void Append(string path, string line)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { lock (Gate) { File.AppendAllText(path, line + "\n"); } }
            catch (Exception ex) { Warn("TSV-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        internal static void Event(string kind, string detail)
        {
            Append(EvTsv, kind + "\t" + F(Time.unscaledTime) + "\t" + Phase + "\t" + detail);
        }

        internal static string Rect(UnityEngine.Rect r)
        {
            return ((int)r.x) + "," + ((int)r.y) + "," + ((int)r.width) + "," + ((int)r.height);
        }

        // ---- reflection into AppContext (same shape as s2_drive.cs 118-144) --------
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

        /// <summary>
        /// `Diablo2.Module.Combat.DamagePipeline.ApplyToMonster` -- the ONE settlement entry the
        /// player's basic attack uses (CombatModule.cs:216 "普攻").  The class is `internal`, so it
        /// is reached by reflection; the method itself is public and its parameter types are the
        /// public DTOs (Diablo2.Def.MonsterState / DamageType), i.e. the same instances the contract
        /// hands out.  A non-lethal raw (1) is what produces the "hurt but alive" window.
        /// Signature (DamagePipeline.cs:81): (int attackerId, MonsterState target, int raw,
        /// DamageType type, bool critical, string source).  attackerId 1 = GameConst.PlayerEntityId
        /// (Core/GameConst.cs:196).
        /// </summary>
        internal static MethodInfo DamagePipelineApplyToMonster()
        {
            var t = FindType("Diablo2.Module.Combat.DamagePipeline");
            if (t == null) return null;
            return t.GetMethod("ApplyToMonster", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(int), typeof(Diablo2.Def.MonsterState), typeof(int),
                        typeof(Diablo2.Def.DamageType), typeof(bool), typeof(string) }, null);
        }

        internal static IMonsterModule Monsters() { return CtxMember("Monster") as IMonsterModule; }
        internal static IViewModule View() { return CtxMember("View") as IViewModule; }
        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }

        /// <summary>Split "zm/hit_0_3" into action "hit" and frame index "3" (last two fields).</summary>
        internal static string ActionOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return "-";
            var s = sprite.Replace('\\', '/');
            var slash = s.LastIndexOf('/');
            var tail = slash >= 0 ? s.Substring(slash + 1) : s;
            var us = tail.LastIndexOf('_');
            if (us <= 0) return tail;
            var prev = tail.LastIndexOf('_', us - 1);
            if (prev < 0) return tail.Substring(0, us);
            return tail.Substring(0, prev);
        }

        internal static string FrameIndexOf(string sprite)
        {
            if (string.IsNullOrEmpty(sprite)) return "-";
            var us = sprite.LastIndexOf('_');
            return us < 0 || us + 1 >= sprite.Length ? "-" : sprite.Substring(us + 1);
        }
    }

    /// <summary>Records every clip that STARTS playing (OFF -> ON transition per AudioSource).</summary>
    public class ClipWatch : MonoBehaviour
    {
        private readonly Dictionary<AudioSource, bool> _prev = new Dictionary<AudioSource, bool>();
        private float _nextBeat;

        private void Update()
        {
            try
            {
                var srcs = UnityEngine.Object.FindObjectsByType<AudioSource>();
                for (var i = 0; i < srcs.Length; i++)
                {
                    var s = srcs[i];
                    if (s == null) continue;
                    var now = s.isPlaying;
                    bool was;
                    if (!_prev.TryGetValue(s, out was)) was = false;
                    if (now && !was)
                    {
                        var clip = s.clip;
                        var name = clip != null ? clip.name : "(no-clip)";
                        P.Log("SFX-START t=" + P.F(Time.unscaledTime) + " frame=" + Time.frameCount
                              + " clip=" + name + " loop=" + (s.loop ? 1 : 0)
                              + " src=" + s.gameObject.name + " len=" + P.F(clip != null ? clip.length : 0f));
                        P.Event("SFX", name + "\tloop=" + (s.loop ? 1 : 0) + "\tlen="
                                + P.F(clip != null ? clip.length : 0f) + "\tsrc=" + s.gameObject.name);
                    }
                    _prev[s] = now;
                }
            }
            catch (Exception ex) { P.Warn("CLIP-SCAN-FAIL " + ex.GetType().Name + ": " + ex.Message); }

            if (Time.unscaledTime >= _nextBeat)
            {
                _nextBeat = Time.unscaledTime + 5f;
                P.Log("CLIP-ALIVE t=" + P.F(Time.unscaledTime) + " sources=" + _prev.Count);
            }
        }
    }

    /// <summary>
    /// Per-frame sampler of ONE monster's render node: every change of (sprite name, sprite rect)
    /// IS one rendered animation frame.  The sprite name comes from the frame KEY
    /// (".../hit_0_3"), so both the action and the frame index are readable from the name.
    /// </summary>
    public class FrameWatch : MonoBehaviour
    {
        public int Id = -1;
        public float StopAt;

        public int Samples;
        public int Changes;

        private string _sprite = string.Empty;
        private string _rect = string.Empty;
        private float _actionStart = -1f;
        private string _action = "-";
        private int _actionChanges;

        private void Update()
        {
            if (Time.unscaledTime >= StopAt)
            {
                P.Log("FRAME-END id=" + Id + " samples=" + Samples + " changes=" + Changes
                      + " lastSprite=" + _sprite);
                P.Event("ANIM-END", "id=" + Id + "\tsamples=" + Samples + "\tchanges=" + Changes + "\t" + _sprite);
                enabled = false;
                return;
            }
            try
            {
                var view = P.View();
                var go = view != null ? view.GetView(Id) : null;
                if (go == null) return;
                var sr = go.GetComponentInChildren<SpriteRenderer>(true);
                if (sr == null) return;

                Samples++;
                var sp = sr.sprite;
                var name = sp != null ? sp.name : "(none)";
                var rect = sp != null ? P.Rect(sp.rect) : "0,0,0,0";
                if (name == _sprite && rect == _rect) return;

                var act = P.ActionOf(name);
                if (act != _action)
                {
                    _action = act;
                    _actionChanges = 0;
                    _actionStart = Time.unscaledTime;
                }
                _actionChanges++;
                Changes++;
                P.Log("FRAME id=" + Id + " t=" + P.F(Time.unscaledTime) + " frame=" + Time.frameCount
                      + " sprite=" + name + " rect=(" + rect + ") action=" + act
                      + " idx=" + P.FrameIndexOf(name) + " n=" + _actionChanges);
                P.Append(P.AnimTsv, P.F(Time.unscaledTime) + "\t" + name + "\t" + rect + "\t" + act
                         + "\t" + P.FrameIndexOf(name) + "\t" + _actionChanges
                         + "\t" + P.F(_actionStart) + "\t" + P.Phase);
                P.Event("ANIM", name + "\trect=" + rect + "\t" + act
                        + "\tidx=" + P.FrameIndexOf(name) + "\tn=" + _actionChanges);
                _sprite = name;
                _rect = rect;
            }
            catch (Exception ex)
            {
                P.Warn("FRAME-FAIL " + ex.GetType().Name + ": " + ex.Message);
                enabled = false;
            }
        }
    }

    /// <summary>The whole timeline, in ONE assembly: pick tank -> watch -> real click attacks.</summary>
    public class Runner : MonoBehaviour
    {
        private float _t0 = -1f;
        private int _step;
        private int _target = -1;
        private string _targetName = "-";
        private int _hpBefore = -1;
        private int _hpAfterProbeHit = -1;
        private float _nextClick;
        private int _clicks;
        private int _clicksAccepted;
        private float _nextShot;
        private int _shots;
        private float _nextDp;
        private int _dpCount;
        private FrameWatch _fw;
        private ClipWatch _cw;
        private readonly List<string> _note = new List<string>();

        private void Update()
        {
            if (_t0 < 0f) { _t0 = Time.unscaledTime; return; }
            var t = Time.unscaledTime - _t0;
            try
            {
                if (_step == 0 && t >= 0.5f) { Begin(); _step = 1; return; }
                if (_step == 1)
                {
                    if (t >= _nextShot && _shots < 3) { Shot((++_shots) + "_fight.png"); _nextShot = t + 7f; }
                    if (t >= _nextDp && _dpCount < 3) { DpHit(); _nextDp = t + 1.5f; }
                    if (t >= _nextClick && _clicks < 12) { Click(); _nextClick = t + 0.7f; }
                    if ((_clicks >= 12 && _dpCount >= 3) || t >= 26f) { _step = 2; return; }
                    return;
                }
                if (_step == 2) { Finish(); _step = 3; }
            }
            catch (Exception ex)
            {
                P.Warn("RUNNER-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Begin()
        {
            P.Phase = "setup";
            var m = P.Monsters();
            var pl = P.Player();
            var map = P.Map();
            P.Log("SNAP t=" + P.F(Time.unscaledTime) + " area=" + (map != null ? ((int)map.Area).ToString() : "-")
                  + " alive=" + (m != null ? m.AliveCount : -1)
                  + " player=" + (pl != null ? ("(" + pl.Grid.x + "," + pl.Grid.y + ") dir=" + pl.Dir + " life=" + pl.Life) : "(-)")
                  + " monsters=[" + List(m) + "]");
            P.Event("NOTE", "setup\talive=" + (m != null ? m.AliveCount : -1) + "\tplayerGrid="
                    + (pl != null ? (pl.Grid.x + "," + pl.Grid.y) : "-"));

            _target = Tank(m);
            var st = m != null && _target >= 0 ? m.Get(_target) : null;
            _targetName = st != null ? st.name : "-";
            _hpBefore = st != null ? st.hp : -1;
            P.Log("TANK id=" + _target + " name=" + _targetName + " hp=" + _hpBefore + "/"
                  + (st != null ? st.maxHp : -1) + " kind=" + (st != null ? st.kindId : -1)
                  + " grid=" + (st != null ? ("(" + st.gridX + "," + st.gridY + ")") : "(-)"));
            P.Event("NOTE", "tank\tid=" + _target + "\tname=" + _targetName + "\thp=" + _hpBefore
                    + "\tmaxHp=" + (st != null ? st.maxHp : -1) + "\tkind=" + (st != null ? st.kindId : -1));

            var go = new GameObject("AV2Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);

            var cgo = new GameObject("AV2ClipWatch");
            UnityEngine.Object.DontDestroyOnLoad(cgo);
            _cw = cgo.AddComponent<ClipWatch>();

            var fgo = new GameObject("AV2FrameWatch");
            UnityEngine.Object.DontDestroyOnLoad(fgo);
            _fw = fgo.AddComponent<FrameWatch>();
            _fw.Id = _target;
            _fw.StopAt = Time.unscaledTime + 28f;
            P.Log("WATCHING id=" + _target + " untilT=" + P.F(_fw.StopAt));

            _nextClick = 8.0f;
            _nextShot = 1.0f;
            _nextDp = 3.0f;

            // ROUTE A (3.0/4.5/6.0s): DamagePipeline.ApplyToMonster(raw=1) -- the settlement entry
            // the player's own basic attack uses => it plays the impact `hit` AND schedules the
            // monster's own gethit sound (MonSounds.HitDelay) AND calls PlayHit (hurt animation).
            // ROUTE B (8.0..16.4s): the REAL left-click gesture (HandlePrimaryClick) -- misses are
            // expected (a level-1 player vs a 27-hp elite), but it is the honest "real input" leg.
            // One non-lethal ApplyDamage tick as ROUTE C is still available via Api.Hit.
        }

        /// <summary>Non-lethal damage on the tankiest monster: the "hurt but alive" window.</summary>
        private void ProbeHit()
        {
            var m = P.Monsters();
            if (m == null) { P.Warn("PROBEHIT no MonsterModule"); return; }
            var st = m.Get(_target);
            if (st == null || !st.alive) { P.Warn("PROBEHIT target gone id=" + _target); return; }
            P.Phase = "probehit";
            try { m.ApplyDamage(_target, 1, Diablo2.Def.DamageType.Physical); }
            catch (Exception ex) { P.Warn("PROBEHIT threw " + ex.GetType().Name + ": " + ex.Message); return; }
            var now = m.Get(_target);
            _hpAfterProbeHit = now != null ? now.hp : -1;
            P.Log("PROBEHIT id=" + _target + " amount=1 before=" + _hpBefore + " after=" + _hpAfterProbeHit
                  + " alive=" + (now != null ? (now.alive ? 1 : 0) : -1) + " t=" + P.F(Time.unscaledTime));
            P.Event("NOTE", "probehit\tid=" + _target + "\tbefore=" + _hpBefore + "\tafter=" + _hpAfterProbeHit
                    + "\talive=" + (now != null ? (now.alive ? 1 : 0) : -1));
        }

        /// <summary>
        /// ROUTE A: one non-lethal settlement through the SAME entry the player's basic attack uses
        /// (`DamagePipeline.ApplyToMonster`, CombatModule.cs:216).  raw=1 on purpose: the monster
        /// must stay alive, otherwise the `hit`/monster_hit_* pair is replaced by the death sound.
        /// </summary>
        private void DpHit()
        {
            var m = P.Monsters();
            if (m == null) { P.Warn("DPHIT no MonsterModule"); return; }
            var st = m.Get(_target);
            if (st == null || !st.alive) { P.Warn("DPHIT target gone id=" + _target); return; }
            var mi = P.DamagePipelineApplyToMonster();
            if (mi == null) { P.Warn("DPHIT DamagePipeline.ApplyToMonster not found (internal class? reflection failed)"); return; }
            _dpCount++;
            P.Phase = "dphit#" + _dpCount;
            try
            {
                var killed = (bool)mi.Invoke(null, new object[] { 1, st, 1, Diablo2.Def.DamageType.Physical, false, "av2-probe" });
                var now = m.Get(_target);
                P.Log("DPHIT n=" + _dpCount + " id=" + _target + " " + _targetName + " raw=1 killed=" + (killed ? 1 : 0)
                      + " hp=" + (now != null ? now.hp : -1) + "/" + (now != null ? now.maxHp : -1)
                      + " alive=" + (now != null ? (now.alive ? 1 : 0) : -1) + " t=" + P.F(Time.unscaledTime));
                P.Event("NOTE", "dphit\tn=" + _dpCount + "\tid=" + _target + "\tkilled=" + (killed ? 1 : 0)
                        + "\thp=" + (now != null ? now.hp : -1));
            }
            catch (Exception ex) { P.Warn("DPHIT threw " + ex.GetType().Name + ": " + ex.Message); }
        }

        /// <summary>
        /// The REAL left-click-on-a-monster gesture: PlayerModule.HandlePrimaryClick(grid) --
        /// the same method Module/Player/PlayerModule.cs calls with the clicked grid.  It resolves
        /// the grid's hover target, emits Events.AttackRequest, and emits MoveCommand so the player
        /// walks adjacent (which is what makes the melee shape gate pass).
        /// </summary>
        private void Click()
        {
            var m = P.Monsters();
            var pl = P.Player();
            if (m == null || pl == null) { P.Warn("CLICK no modules"); _clicks = 40; return; }
            var id = Nearest(m, pl);
            if (id < 0) { _clicks = 40; return; }
            var st = m.Get(id);
            if (st == null) { _clicks = 40; return; }

            if (id != _target) { P.Log("RETARGET " + _target + " -> " + id + " (" + (st != null ? st.name : "-") + ")"); }
            _target = id;
            _targetName = st.name;
            if (_fw != null) _fw.Id = id;

            P.Phase = "click#" + (_clicks + 1);
            _clicks++;
            var grid = new Vector2Int(st.gridX, st.gridY);
            try
            {
                var meth = pl.GetType().GetMethod("HandlePrimaryClick",
                    BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(Vector2Int) }, null);
                if (meth == null) { P.Warn("CLICK HandlePrimaryClick not found on " + pl.GetType().FullName); _clicks = 40; return; }
                meth.Invoke(pl, new object[] { grid });
                _clicksAccepted++;
                P.Log("CLICK n=" + _clicks + " target=" + id + " " + st.name + " grid=(" + grid.x + "," + grid.y
                      + ") player=(" + pl.Grid.x + "," + pl.Grid.y + ") dir=" + pl.Dir
                      + " dist=" + Cheb(pl.Grid, grid) + " t=" + P.F(Time.unscaledTime));
                P.Event("CLICK", "n=" + _clicks + "\ttarget=" + id + "\tgrid=" + grid.x + "," + grid.y
                        + "\tplayer=" + pl.Grid.x + "," + pl.Grid.y + "\tdir=" + pl.Dir
                        + "\tdist=" + Cheb(pl.Grid, grid));
            }
            catch (Exception ex)
            {
                P.Warn("CLICK threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void Finish()
        {
            P.Phase = "final";
            var m = P.Monsters();
            var pl = P.Player();
            var st = m != null ? m.Get(_target) : null;
            P.Log("SUMMARY clicks=" + _clicks + " accepted=" + _clicksAccepted
                  + " target=" + _target + " " + _targetName
                  + " hp=" + (st != null ? st.hp : -1) + "/" + (st != null ? st.maxHp : -1)
                  + " alive=" + (st != null ? (st.alive ? 1 : 0) : -1)
                  + " playerLife=" + (pl != null ? pl.Life : -1)
                  + " samples=" + (_fw != null ? _fw.Samples : 0) + " animChanges=" + (_fw != null ? _fw.Changes : 0));
            P.Event("NOTE", "final\ttarget=" + _target + "\thp=" + (st != null ? st.hp : -1)
                    + "\talive=" + (st != null ? (st.alive ? 1 : 0) : -1));
            try
            {
                File.WriteAllText(P.Summary,
                    "audio-verify2 summary\n"
                    + "clicks=" + _clicks + " accepted=" + _clicksAccepted + "\n"
                    + "target id=" + _target + " name=" + _targetName + "\n"
                    + "hp start=" + _hpBefore + " afterProbeHit=" + _hpAfterProbeHit
                    + " final=" + (st != null ? st.hp : -1) + "/" + (st != null ? st.maxHp : -1)
                    + " alive=" + (st != null ? (st.alive ? 1 : 0) : -1) + "\n"
                    + "playerLife=" + (pl != null ? pl.Life : -1) + "\n"
                    + "frameSamples=" + (_fw != null ? _fw.Samples : 0)
                    + " animChanges=" + (_fw != null ? _fw.Changes : 0) + "\n");
            }
            catch (Exception ex) { P.Warn("SUMMARY-FAIL " + ex.GetType().Name + ": " + ex.Message); }
            try { File.WriteAllText(P.Done, "AV2-DONE t=" + P.F(Time.unscaledTime)
                    + " clicks=" + _clicks + " target=" + _target + " hp=" + (st != null ? st.hp : -1)
                    + " samples=" + (_fw != null ? _fw.Samples : 0)
                    + " animChanges=" + (_fw != null ? _fw.Changes : 0) + "\n"); }
            catch (Exception ex) { P.Warn("DONE-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void Shot(string name)
        {
            try
            {
                var path = P.ShotDir + "/" + P.ShotPrefix + name;
                if (File.Exists(path)) File.Delete(path);
                ScreenCapture.CaptureScreenshot(path);
                P.Log("SHOT-ISSUED " + path + " t=" + P.F(Time.unscaledTime));
                P.Event("SHOT", name);
            }
            catch (Exception ex) { P.Warn("SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- picks -------------------------------------------------------------------
        private static int Tank(IMonsterModule m)
        {
            if (m == null || m.All == null) return -1;
            var best = -1; var bestHp = -1;
            for (var i = 0; i < m.All.Count; i++)
            {
                var s = m.All[i];
                if (s == null || !s.alive) continue;
                if (s.maxHp > bestHp) { bestHp = s.maxHp; best = s.id; }
            }
            return best;
        }

        private static int Nearest(IMonsterModule m, IPlayerModule pl)
        {
            var best = -1; var bestD = int.MaxValue;
            for (var i = 0; i < m.All.Count; i++)
            {
                var s = m.All[i];
                if (s == null || !s.alive) continue;
                var d = Cheb(pl.Grid, new Vector2Int(s.gridX, s.gridY));
                if (d < bestD) { bestD = d; best = s.id; }
            }
            return best;
        }

        private static int Cheb(Vector2Int a, Vector2Int b)
        {
            return Math.Max(Math.Abs(a.x - b.x), Math.Abs(a.y - b.y));
        }

        private static string List(IMonsterModule m)
        {
            if (m == null || m.All == null) return "-";
            var sb = new StringBuilder();
            var n = 0;
            for (var i = 0; i < m.All.Count; i++)
            {
                var s = m.All[i];
                if (s == null || !s.alive) continue;
                n++;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(s.id).Append(':').Append(s.kindId).Append(':').Append(s.name)
                  .Append(':').Append(s.hp).Append('/').Append(s.maxHp)
                  .Append(":(").Append(s.gridX).Append(',').Append(s.gridY).Append(')');
                if (n >= 40) { sb.Append(", ..."); break; }
            }
            return sb.ToString();
        }

        private void Awake()
        {
            // keep the note list non-null even if a later path adds to it
            if (_note == null) return;
        }
    }

    /// <summary>The single entry point (one run_script call => one assembly => statics work).</summary>
    public static class Api
    {
        public static string Ping()
        {
            return "PONG running=" + (Game.IsRunning ? 1 : 0);
        }

        /// <summary>
        /// spec = "&lt;events tsv&gt;|&lt;anim tsv&gt;|&lt;summary&gt;|&lt;shot dir&gt;|&lt;done marker&gt;".
        /// Windows paths are fine (only '|' is a separator).
        /// </summary>
        public static string Start(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length < 5) { P.Warn("START bad spec=" + spec); return "ERR-spec"; }
            P.EvTsv = parts[0];
            P.AnimTsv = parts[1];
            P.Summary = parts[2];
            P.ShotDir = parts[3];
            P.Done = parts[4];
            P.ShotPrefix = parts.Length > 5 && parts[5].Length > 0 ? parts[5] : "av2_";

            foreach (var f in new[] { P.EvTsv, P.AnimTsv })
            {
                try { var d = Path.GetDirectoryName(f); if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d); if (File.Exists(f)) File.Delete(f); }
                catch (Exception ex) { P.Warn("START clean-fail " + f + " " + ex.Message); }
            }
            P.Append(P.EvTsv, "# kind\tt\tphase\tdetail");
            P.Append(P.AnimTsv, "# t\tsprite\trect\taction\tframeIdx\tn\tactionStart\tphase");

            var go = new GameObject("AV2Root");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
            P.Log("START gameRunning=" + (Game.IsRunning ? 1 : 0)
                  + " monsters=" + (P.Monsters() != null ? P.Monsters().AliveCount : -1));
            return "STARTED";
        }

        /// <summary>Manual non-lethal hit, if the timeline needs a nudge from outside.</summary>
        public static string Hit(string spec)
        {
            var p = (spec ?? string.Empty).Split('|');
            int id, amount;
            if (p.Length < 2 || !int.TryParse(p[0], out id) || !int.TryParse(p[1], out amount))
            { P.Warn("HIT bad spec=" + spec); return "ERR-spec"; }
            var m = P.Monsters();
            if (m == null) return "ERR-nomodule";
            var before = m.Get(id);
            m.ApplyDamage(id, amount, Diablo2.Def.DamageType.Physical);
            var after = m.Get(id);
            P.Log("HIT id=" + id + " amount=" + amount + " before=" + (before != null ? before.hp : -1)
                  + " after=" + (after != null ? after.hp : -1) + " alive=" + (after != null ? (after.alive ? 1 : 0) : -1));
            return "HIT-OK hp=" + (after != null ? after.hp : -1);
        }

        public static string Snap()
        {
            var m = P.Monsters();
            var map = P.Map();
            var pl = P.Player();
            P.Log("MANUAL-SNAP area=" + (map != null ? ((int)map.Area).ToString() : "-")
                  + " alive=" + (m != null ? m.AliveCount : -1) + " playerLife=" + (pl != null ? pl.Life : -1));
            return "SNAP-OK";
        }
    }
}
