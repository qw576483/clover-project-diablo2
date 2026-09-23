// =============================================================================
// pv_audio_probe.cs -- play-verify group 4: MONSTER SFX evidence.
//
// WHY THIS FILE EXISTS: the per-monster SFX wiring (d2sfx.mpq -> SfxKeys/SfxRegistry ->
// DamagePipeline / MonsterModule) is green OFFLINE (audiocheck 98 / animcheck 87 / recompile
// completed) but had never been observed in a live session.  Only a live session proves that the
// REAL clips are pulled off the pool and start playing at the real moments.
//
// WHAT IT DOES (and does NOT do):
//   * It contains NO boot/menu/stage chain.  It is installed INTO a Play session that is already
//     in Stage (this file is used together with s2_drive.cs, whose Tour reaches Blood Moor).
//   * Every observation is read-only: AudioSource transitions (which clip STARTED playing),
//     and the monster state through the PUBLIC contract types
//     (Diablo2.Module.IMonsterModule.ApplyDamage / NotifyAttacked / Get / All).
//   * Nothing here writes game state except through those two PUBLIC contract methods, which are
//     exactly what the user's own action does (hit a monster / make it hostile).
//
// EVIDENCE SHAPE (runtime log lines, L3 = written by the running game itself):
//   [PVA] SNAP area=.. alive=n player=(gx,gy) monsters=[id:kindId:name:hp/maxHp:(gx,gy) ...]
//   [PVA] MARK phase=<label> frame=<n>
//   [PVA] SFX-START phase=<label> frame=<n> t=<sec> clip=<name> loop=0 vol=..
//   [PVA] SINCE phase=<label> n=<k> clips=[<phase>|<clip> ...]
//   [PVA] HIT id=<n> amount=<k> before=<hp> after=<hp> alive=<0|1>
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

namespace PVA
{
    public static class Probe
    {
        internal const string Tag = "PVA";

        internal static string _done = string.Empty;
        internal static string _raw = string.Empty;

        // one entry per clip that STARTED playing, oldest first ("<phase>|<clip>")
        internal static readonly List<string> Plays = new List<string>();
        internal static string _phase = "-";

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

        internal static void Finish(string why)
        {
            Log("PVA-FINISH why=" + why + " plays=" + Plays.Count);
            if (string.IsNullOrEmpty(_done)) { Warn("MARKER path empty"); return; }
            try
            {
                var dir = Path.GetDirectoryName(_done);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_done, "PV-AUDIO-DONE why=" + why + " plays=" + Plays.Count + " clock=" + DateTime.Now.ToString("HH:mm:ss.fff"));
            }
            catch (Exception ex) { Log("MARKER-FAIL " + ex.GetType().Name + ": " + ex.Message); }
        }

        // ---- reflection into AppContext (same shape as s2_drive.cs 118-144) --------------
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
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }
        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }

        internal static string F(float v) { return v.ToString("0.00", CultureInfo.InvariantCulture); }
    }

    /// <summary>
    /// Watches every AudioSource and records every clip that STARTS playing.  `isPlaying` alone is
    /// not enough (it stays true for the whole clip), so this tracks the OFF -> ON transition per
    /// source and logs it once, with the clip name.
    /// </summary>
    public class ClipListener : MonoBehaviour
    {
        // Keyed by the AudioSource reference itself: GetInstanceID()/GetEntityId() are both
        // version-dependent here, whereas reference identity is stable within one Play session.
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
                        Probe.Plays.Add(Probe._phase + "|" + name);
                        Probe.Log("SFX-START phase=" + Probe._phase + " frame=" + Time.frameCount
                                  + " t=" + Probe.F(Time.unscaledTime) + " clip=" + name
                                  + " loop=" + (s.loop ? 1 : 0) + " vol=" + Probe.F(s.volume));
                    }
                    _prev[s] = now;
                }
            }
            catch (Exception ex)
            {
                Probe.Warn("CLIP-SCAN-FAIL " + ex.GetType().Name + ": " + ex.Message);
            }

            if (Time.unscaledTime >= _nextBeat)
            {
                _nextBeat = Time.unscaledTime + 2f;
                Probe.Log("LISTEN alive frame=" + Time.frameCount + " sources=" + _prev.Count + " plays=" + Probe.Plays.Count);
            }
        }
    }

    /// <summary>Public one-shot entries for `run_script`.</summary>
    public static class Api
    {
        public static string Ping() { return "PONG gameRunning=" + (Game.IsRunning ? 1 : 0) + " plays=" + Probe.Plays.Count; }

        /// <summary>spec = "&lt;done marker path&gt;|&lt;raw screenshot dir&gt;".  Installs the clip listener exactly once.</summary>
        public static string Install(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            Probe._done = parts.Length > 0 ? parts[0] : string.Empty;
            Probe._raw = parts.Length > 1 ? parts[1] : string.Empty;
            Probe.Log("PATHS done=" + Probe._done + " raw=" + Probe._raw);

            var old = GameObject.Find("PVAudioListener");
            if (old == null)
            {
                var go = new GameObject("PVAudioListener");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<ClipListener>();
            }
            Probe.Log("LISTENER-INSTALL frame=" + Time.frameCount + " gameRunning=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }

        /// <summary>Scene context: area, alive monsters, their ids/kindIds/hp/grid, the player grid.</summary>
        public static string Snap()
        {
            var m = Probe.Monsters();
            var map = Probe.Map();
            var pl = Probe.Player();
            var sb = new StringBuilder();
            var n = 0;
            if (m != null && m.All != null)
            {
                for (var i = 0; i < m.All.Count; i++)
                {
                    var s = m.All[i];
                    if (s == null || !s.alive) continue;
                    n++;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(s.id).Append(':').Append(s.kindId).Append(':').Append(s.name)
                      .Append(':').Append(s.hp).Append('/').Append(s.maxHp)
                      .Append(":(").Append(s.gridX).Append(',').Append(s.gridY).Append(')');
                }
            }
            Probe.Log("SNAP area=" + (map != null ? (int)map.Area : -1)
                      + " alive=" + n
                      + " player=" + (pl != null ? ("(" + pl.Grid.x + "," + pl.Grid.y + ")") : "(-)")
                      + " monsters=[" + sb + "]");
            return "SNAP-OK alive=" + n + " area=" + (map != null ? (int)map.Area : -1);
        }

        /// <summary>Start a new observation window named <paramref name="label"/>.</summary>
        public static string Mark(string label)
        {
            Probe._phase = label ?? "-";
            Probe.Plays.Clear();
            Probe.Log("MARK phase=" + Probe._phase + " frame=" + Time.frameCount);
            return "MARK-OK " + Probe._phase;
        }

        /// <summary>Dump every clip that started since the last Mark.</summary>
        public static string Since()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < Probe.Plays.Count; i++) { if (sb.Length > 0) sb.Append(' '); sb.Append(Probe.Plays[i]); }
            Probe.Log("SINCE phase=" + Probe._phase + " n=" + Probe.Plays.Count + " clips=[" + sb + "]");
            return "SINCE n=" + Probe.Plays.Count;
        }

        /// <summary>
        /// Deal damage through the PUBLIC contract `IMonsterModule.ApplyDamage(int, int, DamageType)`.
        /// Same entry the combat code uses => whatever audio hooks hang off it are the ones firing.
        /// </summary>
        public static string Hit(string spec)
        {
            var parts = (spec ?? string.Empty).Split('|');
            if (parts.Length < 2) { Probe.Warn("HIT bad spec=" + spec); return "ERR-spec"; }
            var id = 0; var amount = 0;
            if (!int.TryParse(parts[0], out id) || !int.TryParse(parts[1], out amount))
            { Probe.Warn("HIT bad numbers=" + spec); return "ERR-num"; }

            var m = Probe.Monsters();
            if (m == null) { Probe.Warn("HIT no MonsterModule"); return "ERR-nomodule"; }
            var st = m.Get(id);
            var before = st != null ? st.hp : -1;
            try { m.ApplyDamage(id, amount, Diablo2.Def.DamageType.Physical); }
            catch (Exception ex) { Probe.Warn("HIT threw " + ex.GetType().Name + ": " + ex.Message); return "ERR-throw"; }
            var now = m.Get(id);
            Probe.Log("HIT id=" + id + " amount=" + amount + " before=" + before
                      + " after=" + (now != null ? now.hp : -1)
                      + " alive=" + (now != null ? (now.alive ? 1 : 0) : -1)
                      + " frame=" + Time.frameCount);
            return "HIT-OK hp=" + (now != null ? now.hp : -1) + " alive=" + (now != null ? (now.alive ? 1 : 0) : -1);
        }

        /// <summary>The alive monster closest to the player (Chebyshev grid distance). Read-only.</summary>
        public static string Nearest()
        {
            var m = Probe.Monsters();
            var pl = Probe.Player();
            if (m == null || m.All == null) { Probe.Warn("NEAREST no MonsterModule"); return "ERR-nomodule"; }
            var best = -1; var bestD = int.MaxValue; var bestName = "-";
            for (var i = 0; i < m.All.Count; i++)
            {
                var s = m.All[i];
                if (s == null || !s.alive) continue;
                var d = 0;
                if (pl != null) d = Math.Max(Math.Abs(s.gridX - pl.Grid.x), Math.Abs(s.gridY - pl.Grid.y));
                if (d < bestD) { bestD = d; best = s.id; bestName = s.name; }
            }
            Probe.Log("NEAREST id=" + best + " dist=" + bestD + " name=" + bestName
                      + " player=" + (pl != null ? ("(" + pl.Grid.x + "," + pl.Grid.y + ")") : "(-)"));
            return "NEAREST id=" + best + " dist=" + bestD;
        }

        /// <summary>
        /// The REAL "click the monster" entry: `Game.Event.Emit&lt;int&gt;(Events.AttackRequest, monsterId)`
        /// -> CombatModule.OnAttackRequest -> RequestAttack -> DamagePipeline.  This is the only
        /// settlement entry for the player's basic attack, so whatever sounds hang off it (the impact
        /// sound and the monster's own gethit sound) are the ones we want to observe.
        /// </summary>
        public static string Attack(string spec)
        {
            int id;
            if (!int.TryParse(spec, out id)) { Probe.Warn("ATTACK bad id=" + spec); return "ERR-num"; }
            try
            {
                Game.Event.Emit<int>(Diablo2.Core.Events.AttackRequest, id);
                var m = Probe.Monsters();
                var st = m != null ? m.Get(id) : null;
                Probe.Log("ATTACK id=" + id + " hp=" + (st != null ? st.hp : -1)
                          + " alive=" + (st != null ? (st.alive ? 1 : 0) : -1)
                          + " frame=" + Time.frameCount);
                return "ATTACK-OK id=" + id;
            }
            catch (Exception ex) { Probe.Warn("ATTACK threw " + ex.GetType().Name + ": " + ex.Message); return "ERR-throw"; }
        }

        /// <summary>Make a monster hostile through the PUBLIC contract `NotifyAttacked` (=> chase / attack).</summary>
        public static string Aggro(string spec)
        {
            var ids = (spec ?? string.Empty).Split('|');
            var m = Probe.Monsters();
            if (m == null) { Probe.Warn("AGGRO no MonsterModule"); return "ERR-nomodule"; }
            var sb = new StringBuilder();
            foreach (var raw in ids)
            {
                if (string.IsNullOrEmpty(raw)) continue;
                int id;
                if (!int.TryParse(raw, out id)) continue;
                try { m.NotifyAttacked(id); if (sb.Length > 0) sb.Append(' '); sb.Append(id); }
                catch (Exception ex) { Probe.Warn("AGGRO threw " + ex.GetType().Name + ": " + ex.Message); }
            }
            Probe.Log("AGGRO ids=[" + sb + "] frame=" + Time.frameCount);
            return "AGGRO-OK [" + sb + "]";
        }

        /// <summary>Capture one screenshot (name relative to the raw dir, OR a full absolute path).</summary>
        public static string Shot(string name)
        {
            // NOTE (measured 2026-09-23): each `run_script` call compiles this file into ITS OWN
            // assembly, so the static fields set by Install() are empty here -- a relative name
            // would therefore have no directory.  Accepting a full path keeps Shot usable.
            var target = name ?? string.Empty;
            if (target.IndexOf('/') < 0 && target.IndexOf('\\') < 0)
            {
                if (string.IsNullOrEmpty(Probe._raw)) { Probe.Warn("SHOT raw dir not set"); return "ERR-nopath"; }
                target = Probe._raw + "/" + target;
            }
            try
            {
                var dir = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(target)) File.Delete(target);
                ScreenCapture.CaptureScreenshot(target);
                Probe.Log("SHOT-ISSUED file=" + target + " frame=" + Time.frameCount);
                return "SHOT-OK " + target;
            }
            catch (Exception ex) { Probe.Warn("SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message); return "ERR-throw"; }
        }

        public static string Finish(string why)
        {
            Probe.Finish(why ?? "api");
            return "FINISHED";
        }
    }
}
