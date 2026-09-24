// =============================================================================
//                      (top enemy bar / hover brighten / NPC nameplate).
//
//   run_script --file tools/probes/drivers/u44hover_drive.cs --entry U44Hover.Tour.Install
//   (zero-arg: the paths below are absolute; no --args quoting needed)
//
// WHAT ONLY A LIVE SESSION CAN TELL (everything else is pinned offline by uicheck's
//   A. Does the reference shader (copied verbatim as `Sprite.shader`) ACTUALLY RENDER
//      under this project's URP 17.6 pipeline? The offline host can only prove the file
//      bytes; if URP refuses the legacy CG pass, sprites would go invisible -> that is
//      the one risk that would make C1 wrong on screen. P1 applies the highlight to a
//      REAL SpriteRenderer and reads back the property block + the shader name.
//   B. Does the hover chain really fire end to end at runtime:
//      mouse -> HoverPicker -> Events.HoverTargetChanged -> ViewModule.OnHoverChanged
//      (material swap + _Brightness 3.0) AND EnemyBarView (top bar title/value) --
//      P2 hovers a real monster with a real (Input System) mouse state event.
//   C. Un-hover restores the ORIGINAL material and 1.0/1.0 (the "not hovered ==
//      pixel-identical to before" claim) -- P3.
//   D. Kill removes the target -> bar hidden again -- P4.
//   E. NPC hover shows the world-anchored nameplate -- P5 (town only).
//
// Evidence: %ROOT%/.ai-tmp/screenshots/u44_0*.png  +  %ROOT%/.ai-tmp/test/u44hover_done.txt
//   every numeric read goes through Game.Logger (tag [U44]) so the run script can freeze it.
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module;
using Diablo2.UI;
using UnityEngine;

namespace U44Hover
{
    internal static class Drive
    {
        internal const string Tag = "U44";
        internal const string RawDir = "C:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/screenshots";
        internal const string DonePath = "C:/Work/Server/f-v2/clover-project-diablo2/.ai-tmp/test/u44hover_done.txt";

        internal static void Log(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, m); else UnityEngine.Debug.Log("[" + Tag + "] " + m);
        }
        internal static void Warn(string m)
        {
            var l = Game.Logger;
            if (l != null) l.Warn(Tag, m); else UnityEngine.Debug.LogWarning("[" + Tag + "] " + m);
        }
        internal static void KV(string k, string v) { Log(k + "=" + v); }
        internal static string Esc(string s)
        {
            if (s == null) return "(null)";
            return s.Replace('"', '\'').Replace("\r", " ").Replace("\n", "\\n").Replace("\t", " ");
        }
        internal static void WriteFile(string path, string content)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var d = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) Directory.CreateDirectory(d);
                File.WriteAllText(path, content);
            }
            catch (Exception e) { Log("MARKER-FAIL " + e.GetType().Name + " " + e.Message); }
        }
        internal static string Shot(string name) { return RawDir + "/" + name; }

        // ---- reflection ----------------------------------------------------------
        internal static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(name); if (t != null) return t; }
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
            var c = Ctx();
            if (c == null) return null;
            var f = c.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(c) : null;
        }
        internal static IMonsterModule Monster() { return CtxMember("Monster") as IMonsterModule; }
        internal static IPlayerModule Player() { return CtxMember("Player") as IPlayerModule; }
        internal static IViewModule View() { return CtxMember("View") as IViewModule; }
        internal static ISaveModule Save() { return CtxMember("Save") as ISaveModule; }
        internal static INpcModule Npc() { return CtxMember("Npc") as INpcModule; }
        internal static IMapModule Map() { return CtxMember("Map") as IMapModule; }
        internal static object MapMember(string name)
        {
            var m = Map();
            if (m == null) return null;
            var p = m.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            return p != null ? p.GetValue(m) : null;
        }

        /// <summary>Call `Diablo2.Module.View.EntityHighlight.Apply(SpriteRenderer, Material, bool)` (internal).</summary>
        internal static string ApplyHighlight(SpriteRenderer sr, Material original, bool on)
        {
            var t = FindType("Diablo2.Module.View.EntityHighlight");
            if (t == null) return "TYPE-MISSING";
            var mi = t.GetMethod("Apply", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (mi == null) return "METHOD-MISSING";
            try
            {
                var r = mi.Invoke(null, new object[] { sr, original, on });
                var rs = r == null ? "(null)" : r.ToString();
                return "OK(" + (r is bool ? ((bool)r ? "true" : "false") : rs) + ")";
            }
            catch (Exception e)
            {
                var inner = e.InnerException ?? e;
                return "THREW " + inner.GetType().Name + ": " + Drive.Esc(inner.Message);
            }
        }

        // ---- renderer / property block read-back ---------------------------------
        internal static string Brightness(SpriteRenderer sr)
        {
            if (sr == null) return "(no renderer)";
            try
            {
                var mpb = new MaterialPropertyBlock();
                sr.GetPropertyBlock(mpb);
                var b = mpb.GetFloat("_Brightness");
                var c = mpb.GetFloat("_Contrast");
                var shName = sr.sharedMaterial != null && sr.sharedMaterial.shader != null
                    ? sr.sharedMaterial.shader.name : "(no-material)";
                return "_Brightness=" + b.ToString("0.###") + " _Contrast=" + c.ToString("0.###")
                    + " shader=" + shName
                    + " visible=" + (sr.enabled && sr.sprite != null ? 1 : 0)
                    + " color=" + sr.color.ToString();
            }
            catch (Exception e) { return "READ-THREW " + e.GetType().Name + ": " + Drive.Esc(e.Message); }
        }

        internal static SpriteRenderer RendererOf(int entityId)
        {
            var v = View();
            if (v == null) return null;
            try
            {
                var go = v.GetView(entityId);
                return go != null ? go.GetComponent<SpriteRenderer>() : null;
            }
            catch (Exception e) { Warn("RendererOf(" + entityId + ") threw " + e.GetType().Name); return null; }
        }

        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        /// <summary>Mouse state event (Input System) -- the same primitive the x/ v5 drivers use.</summary>
        internal static void MouseState(Vector2 pos, bool leftDown)
        {
            var m = UnityEngine.InputSystem.Mouse.current;
            if (m == null)
            {
                Warn("MouseState: no mouse device (Input System) -> hover cannot be injected");
                return;
            }
            var st = new UnityEngine.InputSystem.LowLevel.MouseState { position = pos };
            if (leftDown) st = st.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(m, st);
        }

        internal static Vector2 ScreenOf(int gx, int gy)
        {
            var cam = Camera.main;
            if (cam == null) return new Vector2(-1f, -1f);
            var w = Iso.GridToWorld(gx, gy);
            var s = cam.WorldToScreenPoint(w);
            return new Vector2(s.x, s.y);
        }
    }

    /// <summary>Zero-arg installer (absolute paths are baked in above).</summary>
    public static class Tour
    {
        public static string Install()
        {
            var go = new GameObject("U44HoverDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<Driver>();
            drv.Init();
            Drive.Log("TOUR-INSTALL running=" + (Game.IsRunning ? 1 : 0));
            return "INSTALLED";
        }

        public static string Api() { return "U44Hover-API"; }
    }

    /// <summary>
    /// Plan-and-timeout driver: boot -> stage -> shader proof -> hover on/off -> kill -> npc.
    /// </summary>
    public class Driver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _step;
        private float _stepAt;
        private float _shotAt = -1f;
        private string _shotFile = string.Empty;
        private bool _done;
        private bool _capture;

        private string _saveName = string.Empty;
        private int _monId = -1;
        private Vector2Int _monGrid;
        private string _monName = string.Empty;
        private int _monHp;
        private Material _playerOrig;
        private SpriteRenderer _playerSr;
        private string _shotTag = string.Empty;
        private string _travelFrom = string.Empty;
        private bool _broughtNear;

        private const float BootTimeout = 30f;
        private const float StageTimeout = 90f;
        private const float ShotTimeout = 15f;

        public void Init()
        {
            _step = 0;
            _stepAt = Time.unscaledTime;
            BuildPlan();
            Drive.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height
                + " plan=" + _plan.Count);
        }

        private void BuildPlan()
        {
            _plan.Add(() =>
            {
                if (Drive.Fsm() == "MainMenu") return true;
                if (Elapsed(1.5f) && Game.Fsm != null && Drive.Fsm() != "MainMenu") Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(BootTimeout)) { Drive.Warn("boot timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            _plan.Add(() =>
            {
                if (Drive.Fsm() == "Stage" && Game.UI != null && Game.UI.IsOpen<HudPanel>()) return true;
                if (Elapsed(0.5f) && _saveName.Length == 0)
                {
                    var save = Drive.Save();
                    var list = save != null ? save.List() : null;
                    if (list != null && list.Count > 0) _saveName = list[0];
                    else { _saveName = "X162012"; Drive.Warn("no save in roster; trying " + _saveName); }
                    Drive.KV("ENTER-STAGE", "save=\"" + _saveName + "\"");
                    Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
                }
                if (Elapsed(StageTimeout)) { Drive.Warn("enter-stage timeout fsm=" + Drive.Fsm()); return true; }
                return false;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(1.5f)) return false;
                var area = Drive.MapMember("Area");
                var all = Drive.Monster() != null ? Drive.Monster().All : null;
                var alive = 0;
                if (all != null) for (var i = 0; i < all.Count; i++) if (all[i] != null && all[i].alive) alive++;
                Drive.KV("STAGE", "fsm=" + Drive.Fsm() + " area=" + (area != null ? area.ToString() : "(null)")
                    + " monstersTotal=" + (all != null ? all.Count : -1) + " alive=" + alive
                    + " hpBarNode=EnemyBarView");
                var bar = EnemyBarView.Instance;
                Drive.KV("BAR-INIT", bar == null ? "Instance=NULL (component not installed!)"
                    : "Instance=OK barVisible=" + (bar.BarVisible ? 1 : 0)
                      + " plateVisible=" + (bar.NameplateVisible ? 1 : 0));
                return true;
            });

            //   "先证明读数口径对（同一个 MPB 上先用已知写入值往返一次：写 2.5 读回 2.5）再判恒等"
            //   Without this, a "1.0" reading could be a wrong-key artefact just as well as the truth.
            _plan.Add(() =>
            {
                var sr0 = Drive.RendererOf(GameConst.PlayerEntityId);
                if (sr0 == null) { Drive.Warn("P0 no player renderer -> read-back round-trip SKIPPED"); return true; }
                try
                {
                    var w = new MaterialPropertyBlock();
                    sr0.GetPropertyBlock(w);
                    w.SetFloat("_Brightness", 2.5f);
                    w.SetFloat("_Contrast", 1.25f);
                    sr0.SetPropertyBlock(w);
                    var r = new MaterialPropertyBlock();
                    sr0.GetPropertyBlock(r);
                    var gb = r.GetFloat("_Brightness");
                    var gc = r.GetFloat("_Contrast");
                    var ok = Mathf.Abs(gb - 2.5f) < 0.001f && Mathf.Abs(gc - 1.25f) < 0.001f;
                    Drive.KV("P0-ROUNDTRIP", "write _Brightness=2.5 _Contrast=1.25 -> read _Brightness="
                        + gb.ToString("0.###") + " _Contrast=" + gc.ToString("0.###") + " ok=" + (ok ? 1 : 0));
                }
                catch (Exception e) { Drive.Warn("P0 round-trip threw " + e.GetType().Name + ": " + e.Message); }
                // restore through the SAME entry the game uses (also exercises Apply(false))
                var res = Drive.ApplyHighlight(sr0, sr0.sharedMaterial, false);
                Drive.KV("P0-RESTORE", "apply(false)=" + res + " " + Drive.Brightness(sr0));
                return true;
            });

            // ---- P1: does the copied shader render at all under URP? -----------------
            _plan.Add(() =>
            {
                if (_playerSr == null)
                {
                    _playerSr = Drive.RendererOf(GameConst.PlayerEntityId);
                    if (_playerSr == null) { Drive.Warn("P1 no player renderer -> skipping shader proof"); return true; }
                    _playerOrig = _playerSr.sharedMaterial;
                    Drive.KV("P1-BASE", Drive.Brightness(_playerSr) + " originalMaterial="
                        + (_playerOrig != null ? _playerOrig.name : "(null)"));
                }
                if (!Elapsed(0.5f)) return false;
                var res = Drive.ApplyHighlight(_playerSr, _playerOrig, true);
                Drive.KV("P1-ON", "apply=" + res + " " + Drive.Brightness(_playerSr));
                Shoot("u44_01_shader_on.png");
                return true;
            });

            _plan.Add(() =>
            {
                if (_shotAt >= 0f) { if (!ShotDone()) return false; }
                if (!Elapsed(0.4f)) return false;
                var res = Drive.ApplyHighlight(_playerSr, _playerOrig, false);
                Drive.KV("P1-OFF", "apply=" + res + " " + Drive.Brightness(_playerSr));
                Shoot("u44_02_shader_off.png");
                return true;
            });

            // ---- TRAVEL: the town has 0 monsters, so walk out to the wilderness FIRST ----
            //   already walk the player out of town; reuse that instead of calling it blocked".
            //   Cheapest faithful route: the town's exit tile is `IMapModule.Exits` (TileKind.Exit)
            //   and `PlayerModule` emits `Events.ExitEntered` the moment the player stands on one
            //   (PlayerModule.cs:1047) => IPlayerModule.TeleportTo(exitTile) triggers the very same
            //   area-switch chain AppFlow.EnterArea uses (no private API, no map poking).
            _plan.Add(() =>
            {
                var mon = Drive.Monster();
                var all = mon != null ? mon.All : null;
                var alive = 0;
                if (all != null) for (var i = 0; i < all.Count; i++) if (all[i] != null && all[i].alive) alive++;
                var area = Drive.MapMember("Area");
                _travelFrom = area != null ? area.ToString() : "(null)";
                if (alive > 0)
                {
                    Drive.KV("TRAVEL-SKIP", "already standing in a monster area: area=" + _travelFrom + " alive=" + alive);
                    return true;
                }
                var map = Drive.Map();
                var exits = map != null ? map.Exits : null;
                if (exits == null || exits.Count == 0)
                {
                    Drive.Warn("TRAVEL-IMPOSSIBLE: area=" + _travelFrom + " reports 0 exit tiles (IMapModule.Exits empty)"
                        + " -- cannot reach the wilderness from a driver; raw reading kept for the report");
                    return true;
                }
                var ex = exits[0];
                var p = Drive.Player();
                if (p == null) { Drive.Warn("TRAVEL-IMPOSSIBLE: PlayerModule == null"); return true; }
                // 1st attempt (12:28 run) teleported straight ONTO the exit tile: the player did stand
                // there (TRAVEL-RESULT playerGrid == exit) but the area never switched => the town exit
                // is only claimed by the NORMAL stepping path.  So this time: teleport to the (known
                // walkable) spawn point and then WALK to the exit through the real A* chain
                // (`IPlayerModule.MoveTo`), which is the same route x_drive / d2u32 use.
                var sp = map != null ? map.SpawnPoint : ex;
                Drive.KV("TRAVEL", "area=" + _travelFrom + " aliveMonsters=" + alive + " exits=" + exits.Count
                    + " first=(" + ex.x + "," + ex.y + ") playerBefore=" + p.Grid
                    + " -> TeleportTo(spawn=(" + sp.x + "," + sp.y + ")) + MoveTo(exit) [walk, does not bypass ExitEntered]");
                try { p.TeleportTo(sp); p.MoveTo(ex); }
                catch (Exception e) { Drive.Warn("TRAVEL-MOVE-THREW " + e.GetType().Name + ": " + e.Message); }
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.6f)) return false;
                var mon = Drive.Monster();
                var all = mon != null ? mon.All : null;
                var alive = 0;
                if (all != null) for (var i = 0; i < all.Count; i++) if (all[i] != null && all[i].alive) alive++;
                var area = Drive.MapMember("Area");
                var p = Drive.Player();
                if (alive > 0 || Elapsed(40f))
                {
                    Drive.KV("TRAVEL-RESULT", "area=" + (area != null ? area.ToString() : "(null)")
                        + " aliveMonsters=" + alive + " playerGrid=" + (p != null ? p.Grid.ToString() : "(null)")
                        + " waited=" + Time.unscaledTime.ToString("0.0"));
                    Shoot("u44_08_wilderness.png");
                    return true;
                }
                return false;
            });

            // ---- P2/P3: real hover on a monster (Input System mouse) -----------------
            _plan.Add(() =>
            {
                if (_shotAt >= 0f && !ShotDone()) return false;
                if (!Elapsed(0.4f)) return false;
                var mon = Drive.Monster();
                var all = mon != null ? mon.All : null;
                if (all == null || all.Count == 0)
                {
                    Drive.Warn("P2 SKIPPED: no monster in this area (area=" + Drive.MapMember("Area") + ")");
                    return true;
                }
                var p = Drive.Player();
                var pg = p != null ? p.Grid : Vector2Int.zero;
                // pick the alive monster NEAREST the player, then make sure it is ON SCREEN:
                //   the 12:30 run hovered a monster at screen (-1920,-1044) -- far off-screen, so the
                //   injected mouse could not possibly be the cause of whatever the bar did
                //   (the reading was real but NOT attributable).  Fix: approach it first.
                var best = -1; var bestD = int.MaxValue;
                for (var i = 0; i < all.Count; i++)
                {
                    var m = all[i];
                    if (m == null || !m.alive) continue;
                    var d = Mathf.Abs(m.gridX - pg.x) + Mathf.Abs(m.gridY - pg.y);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) { Drive.Warn("P2 SKIPPED: no ALIVE monster"); return true; }
                var t = all[best];
                _monId = t.id; _monGrid = new Vector2Int(t.gridX, t.gridY); _monName = t.name; _monHp = t.hp;
                var src = Drive.ScreenOf(_monGrid.x, _monGrid.y);
                var onScreen = (src.x > 40f && src.x < Screen.width - 40f && src.y > 40f && src.y < Screen.height - 40f);
                if (!onScreen && !_broughtNear)
                {
                    _broughtNear = true;
                    Drive.KV("P2-APPROACH", "nearest alive m#" + _monId + " @(" + _monGrid.x + "," + _monGrid.y + ") screen=" + src
                        + " player=" + pg + " -> TeleportTo(tile next to it) so the camera brings it on screen");
                    try { p.TeleportTo(new Vector2Int(_monGrid.x, _monGrid.y - 1)); }
                    catch (Exception e) { Drive.Warn("P2-APPROACH-THREW " + e.GetType().Name); }
                    return false;
                }
                if (!onScreen && Elapsed(4f)) Drive.Warn("P2 target STILL off-screen after approach: src=" + src + " -> this hover read is NOT attributable (suspect)");
                Drive.MouseState(new Vector2(6f, 6f), false);          // away from anything
                Drive.KV("P2-TARGET", "m#" + _monId + " \"" + Drive.Esc(_monName) + "\" grid=(" + _monGrid.x + "," + _monGrid.y + ")"
                    + " hp=" + _monHp + " onScreen=" + (onScreen ? 1 : 0) + " src=" + src + " playerAfterApproach=" + pg);
                Shoot("u44_03_before.png");
                return true;
            });

            _plan.Add(() =>
            {
                // ISSUE here, READ on the NEXT step: the 12:28/12:30 runs showed the injected mouse
                //   state only lands on the frame AFTER the queue call, so reading in the same frame
                //   reported the previous state (looked like "hover did nothing").
                if (_shotAt >= 0f && !ShotDone()) return false;
                if (!Elapsed(0.5f)) return false;
                var sp = Drive.ScreenOf(_monGrid.x, _monGrid.y);
                Drive.MouseState(sp, false);
                Drive.KV("P2-MOUSE-ON-ISSUED", "mouse=" + sp + " (read taken on the next step)");
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.5f)) return false;
                var bar = EnemyBarView.Instance;
                Drive.KV("P3-HOVER-ON", "m#" + _monId + " " + Drive.Brightness(Drive.RendererOf(_monId)));
                Drive.KV("P3-BAR", bar == null ? "Instance=NULL"
                    : "visible=" + (bar.BarVisible ? 1 : 0) + " title=\"" + Drive.Esc(bar.TitleText)
                      + "\" value=" + bar.BarValue.ToString("0.###") + " max=" + bar.BarMaxValue.ToString("0.###")
                      + " hp=" + _monHp);
                Shoot("u44_04_hover_on.png");
                return true;
            });

            _plan.Add(() =>
            {
                if (_shotAt >= 0f && !ShotDone()) return false;
                if (!Elapsed(0.5f)) return false;
                Drive.MouseState(new Vector2(6f, 6f), false);      // away => un-hover lands next frame
                Drive.KV("P3-MOUSE-OFF-ISSUED", "mouse=(6,6)");
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.5f)) return false;
                var bar = EnemyBarView.Instance;
                Drive.KV("P4-HOVER-OFF", "m#" + _monId + " " + Drive.Brightness(Drive.RendererOf(_monId)));
                Drive.KV("P4-BAR", bar == null ? "Instance=NULL" : "visible=" + (bar.BarVisible ? 1 : 0));
                Shoot("u44_05_hover_off.png");
                return true;
            });

            // ---- P5: hover again, then kill -> the target is gone -> bar hidden ------
            _plan.Add(() =>
            {
                if (_shotAt >= 0f && !ShotDone()) return false;
                if (!Elapsed(0.5f)) return false;
                if (_monId < 0) return true;
                Drive.MouseState(Drive.ScreenOf(_monGrid.x, _monGrid.y), false);   // hover it again
                Drive.KV("P5-MOUSE-ON-AGAIN-ISSUED", "m#" + _monId);
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.6f)) return false;
                var mon = Drive.Monster();
                var bar = EnemyBarView.Instance;
                Drive.KV("P5-HOVER-AGAIN", "m#" + _monId + " " + Drive.Brightness(Drive.RendererOf(_monId))
                    + " barVisible=" + (bar != null && bar.BarVisible ? 1 : 0)
                    + " (reproducibility of the hover-on reading)");
                var hpBefore = -1;
                var st = mon != null ? mon.Get(_monId) : null;
                if (st != null) hpBefore = st.hp;
                if (mon != null && mon.IsAlive(_monId)) mon.ApplyDamage(_monId, 9999, DamageType.Physical);
                Drive.KV("P5-DAMAGE", "m#" + _monId + " hpBefore=" + hpBefore + " applied=9999");
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.8f)) return false;
                var mon = Drive.Monster();
                var alive = mon != null && mon.IsAlive(_monId);
                var bar = EnemyBarView.Instance;
                Drive.KV("P5-KILL", "m#" + _monId + " alive=" + (alive ? 1 : 0)
                    + " barVisible=" + (bar != null && bar.BarVisible ? 1 : 0)
                    + " " + Drive.Brightness(Drive.RendererOf(_monId)));
                Shoot("u44_06_killed.png");
                return true;
            });

            // ---- P5: NPC nameplate (town only) --------------------------------------
            _plan.Add(() =>
            {
                if (_shotAt >= 0f && !ShotDone()) return false;
                if (!Elapsed(0.4f)) return false;
                var area = Drive.MapMember("Area");
                var npc = Drive.Npc();
                var all = npc != null ? npc.All : null;
                if (all == null || all.Count == 0)
                {
                    Drive.Warn("P5 SKIPPED: no NPC (area=" + area + "; NPCs live in the town)");
                    return true;
                }
                var d = all[0];
                if (d == null) { Drive.Warn("P5 SKIPPED: NPC[0] null"); return true; }
                var sp = Drive.ScreenOf(d.gridX, d.gridY);
                Drive.MouseState(sp, false);
                Drive.KV("P5-NPC-HOVER", "id=" + d.id + " \"" + Drive.Esc(d.name) + "\" grid=("
                    + d.gridX + "," + d.gridY + ") mouse=" + sp);
                return true;
            });

            _plan.Add(() =>
            {
                if (!Elapsed(0.5f)) return false;
                var bar = EnemyBarView.Instance;
                Drive.KV("P5-PLATE", bar == null ? "Instance=NULL"
                    : "plateVisible=" + (bar.NameplateVisible ? 1 : 0)
                      + " plateText=\"" + Drive.Esc(bar.NameplateText) + "\""
                      + " barVisible=" + (bar.BarVisible ? 1 : 0));
                Shoot("u44_07_npc.png");
                return true;
            });

            _plan.Add(() =>
            {
                if (_shotAt >= 0f && !ShotDone()) return false;
                Drive.KV("END", "steps=" + _plan.Count + " frame=" + Time.frameCount);
                Drive.WriteFile(Drive.DonePath, "DONE step=" + _step + " frame=" + Time.frameCount
                    + " t=" + Time.time.ToString("0.00"));
                Drive.Log("DONE-MARKER-WRITTEN " + Drive.DonePath);
                _done = true;
                return true;
            });
        }

        private void Update()
        {
            if (_done) return;
            try
            {
                if (_step >= _plan.Count) return;
                if (_plan[_step]()) { _step++; _stepAt = Time.unscaledTime; }
            }
            catch (Exception ex)
            {
                Drive.Log("STEP-FATAL step=" + _step + " ex=" + ex.GetType().Name + ": " + ex.Message);
                _step++; _stepAt = Time.unscaledTime;
            }
        }

        private void LateUpdate()
        {
            if (!_capture) return;
            _capture = false;
            try
            {
                ScreenCapture.CaptureScreenshot(_shotFile);
                Drive.KV("SHOT", _shotTag + " file=" + _shotFile + " frame=" + Time.frameCount);
            }
            catch (Exception e) { Drive.Warn("SHOT-FAIL " + _shotTag + " " + e.GetType().Name); }
        }

        private void Shoot(string name)
        {
            _shotFile = Drive.Shot(name);
            _shotTag = name;
            _shotAt = Time.unscaledTime;
            _capture = true;
        }

        private bool ShotDone()
        {
            if (_shotAt < 0f) return true;
            try { if (File.Exists(_shotFile) && new FileInfo(_shotFile).Length > 0) { _shotAt = -1f; return true; } }
            catch { }
            if (Time.unscaledTime - _shotAt > ShotTimeout) { Drive.Warn("SHOT-TIMEOUT " + _shotTag); _shotAt = -1f; return true; }
            return false;
        }

        private bool Elapsed(float s) { return Time.unscaledTime - _stepAt >= s; }
    }
}
