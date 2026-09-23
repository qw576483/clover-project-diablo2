// =============================================================================
// q_save_drive.cs -- ONE Play session for slice Q (R7: silent save-load failure).
//
//   powershell -NoProfile -ExecutionPolicy Bypass -File q_save_run.ps1
//
// WHAT IT PROVES (and why it cannot be judged offline):
//   The DEFECT is "a corrupt save slot leaves the player with NO visible feedback"
//   (audit row R7, .ai-tmp/test/audit-C-logic-num.md section 2). The offline host
//   (tools/probes/hosts/savecheck, step 12) already proves the DISCRIMINATION
//   (missing vs corrupt) and that Events.LoadDone(null) is emitted; what only a live
//   session can prove is that the player-visible half actually appears on screen --
//   i.e. that UI/D2ConfirmPanel really opens with the "存档损坏" title and that the
//   window is composited over the character-select screen.
//
//   Control group (must ALSO be shown, otherwise "the dialog appears" could just mean
//   "a dialog always appears"):
//     1) healthy saves  -> TriggerContinue -> character select opens, NO dialog, and a
//        real character can be entered (FSM reaches Stage);
//     2) the same chain after corrupting exactly one slot -> the dialog IS there.
//
// WHY THE DRIVER DOES NOT CLICK:
//   Everything here is driven through the project's OWN public events
//   (Events.Fsm.TriggerContinue / Events.CharSelectRequest / Events.ToMainMenuRequest).
//   Those are the real handlers (AppFlow.OnContinueRequest / OnCharSelectRequest), i.e.
//   exactly the code path a mouse click would take -- without depending on input
//   injection, hit-testing or panel geometry.
//
// SAFETY:
//   The corrupted slot is backed up to <repo>/.ai-tmp/test/q-save-backup/ BEFORE the
//   garbage is written and restored by Restore() at the end of the run; the run script
//   also backs up the whole saves dir and restores it as a second line of defence.
//   The victim is deliberately NOT "g66" (another slice's evidence depends on it).
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).  [Q] = driver tag.
// =============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.UI;
using UnityEngine;

namespace Q
{
    /// <summary>Public one-shot entries for the run script (kept tiny and idempotent).</summary>
    public static class Api
    {
        private const string Tag = "Q";
        private const string Root = @"C:\Work\Server\f-v2\clover-project-diablo2";

        private static string SavesDir { get { return Path.Combine(Root, @"client\setting\saves"); } }
        private static string BackupDir { get { return Path.Combine(Root, @".ai-tmp\test\q-save-backup"); } }
        private static string ShotDir { get { return Path.Combine(Root, @".ai-tmp\screenshots"); } }

        /// <summary>The slot deliberately corrupted by this slice (NOT g66: another slice cites it).</summary>
        private const string Victim = "W2204727";

        /// <summary>A healthy slot used for the control group ("a real character can be entered").</summary>
        private const string Good = "g66";

        /// <summary>Garbage written over the victim slot (truncated JSON: parses as neither).</summary>
        private const string Garbage = "{\"version\":1,\"name\":\"W2204727\",\"level\":9,\"gol";

        // ---------------------------------------------------------------- helpers --
        private static void Log(string msg)
        {
            var l = Game.Logger;
            if (l != null) l.Info(Tag, msg);
            else UnityEngine.Debug.Log("[" + Tag + "] " + msg);
        }

        private static void Later(float sec, Action cb)
        {
            if (Game.Timer != null) Game.Timer.After(sec, cb);
            else cb();
        }

        private static string Fsm()
        {
            try { return Game.Fsm != null ? Game.Fsm.Current : "(no-fsm)"; }
            catch (Exception ex) { return "(fsm-err:" + ex.GetType().Name + ")"; }
        }

        private static string Scene()
        {
            try { return Game.Scene != null ? (Game.Scene.CurrentScene ?? "(null)") : "(no-scene)"; }
            catch (Exception ex) { return "(scene-err:" + ex.GetType().Name + ")"; }
        }

        /// <summary>Is the reused prompt (UI/D2ConfirmPanel) really open right now?</summary>
        private static bool DialogOpen()
        {
            try { return Game.UI != null && Game.UI.IsOpen<D2ConfirmPanel>(); }
            catch (Exception ex) { Log("Q-dialog-check-err " + ex.GetType().Name + ": " + ex.Message); return false; }
        }

        private static string SlotNames()
        {
            try
            {
                var d = new DirectoryInfo(SavesDir);
                if (!d.Exists) return "(no-saves-dir)";
                var names = new List<string>();
                foreach (var f in d.GetFiles("*.json")) names.Add(Path.GetFileNameWithoutExtension(f.Name));
                names.Sort(StringComparer.Ordinal);
                return string.Join(",", names.ToArray());
            }
            catch (Exception ex) { return "(slot-scan-err:" + ex.GetType().Name + ")"; }
        }

        private static string SlotState(string name)
        {
            try
            {
                var p = Path.Combine(SavesDir, name + ".json");
                if (!File.Exists(p)) return "missing";
                var text = File.ReadAllText(p);
                var ok = SaveJsonParseable(text) ? "parses" : "CORRUPT";
                return "len=" + text.Length + " " + ok;
            }
            catch (Exception ex) { return "err:" + ex.GetType().Name; }
        }

        /// <summary>Same judgement the engine's FileSlotStore uses: MiniJson must parse it.</summary>
        private static bool SaveJsonParseable(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try { CloverEngine.MiniJson.Parse(text); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Type lookup for the project's INTERNAL types (same helper shape as x_drive's Drive.FindType).</summary>
        private static Type FindType(string name)
        {
            var t = Type.GetType(name + ", Assembly-CSharp");
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { var x = asm.GetType(name); if (x != null) return x; }
                catch (Exception) { }
            }
            return null;
        }

        /// <summary>
        /// `AppContext.I.Save.LastError` -- both `AppContext` (internal) and `ISaveModule`'s impl are
        /// reached by reflection, the same way the other probes read internal state.
        /// </summary>
        private static string SaveError()
        {
            try
            {
                var t = FindType("Diablo2.App.AppContext");
                if (t == null) return "(no-appcontext-type)";
                var pi = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static);
                var ctx = pi != null ? pi.GetValue(null) : null;
                if (ctx == null) return "(ctx-null)";
                var f = ctx.GetType().GetField("Save", BindingFlags.Public | BindingFlags.Instance);
                var save = f != null ? f.GetValue(ctx) : null;
                if (save == null) return "(no-save-module)";
                var le = save.GetType().GetProperty("LastError", BindingFlags.Public | BindingFlags.Instance);
                var v = le != null ? le.GetValue(save) : null;
                return (v as string) ?? "";
            }
            catch (Exception ex) { return "(err:" + ex.GetType().Name + ")"; }
        }

        // ------------------------------------------------------------------ setup --
        /// <summary>Editor/play setup: keep the window alive while unfocused (mirrors X.Api2.Cfg).</summary>
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var line = "Q-CFG gameRunning=" + (Game.IsRunning ? 1 : 0) + " fsm=" + Fsm() + " scene=" + Scene();
            Log(line);
            return line;
        }

        /// <summary>Baseline reading: state + which slots exist before anything is touched.</summary>
        public static string Probe()
        {
            var line = "Q-PROBE fsm=" + Fsm() + " scene=" + Scene() + " dialogOpen=" + (DialogOpen() ? 1 : 0)
                + " victim=" + Victim + "(" + SlotState(Victim) + ")"
                + " good=" + Good + "(" + SlotState(Good) + ")"
                + " slots=[" + SlotNames() + "]";
            Log(line);
            return line;
        }

        // ------------------------------------------------- control group (healthy) --
        /// <summary>Control A: healthy saves -> character select must open with NO prompt.</summary>
        public static string ControlContinue()
        {
            Log("Q-CONTROL-REQUEST emit " + Events.Fsm.TriggerContinue + " (healthy saves; expect char select, NO dialog)");
            Game.Event.Emit(Events.Fsm.TriggerContinue);
            Later(1.5f, () =>
            {
                Log("Q-CONTROL-RESULT fsm=" + Fsm() + " scene=" + Scene()
                    + " charSelectOpen=" + (Game.UI != null && Game.UI.IsOpen<CharSelectPanel>() ? 1 : 0)
                    + " dialogOpen=" + (DialogOpen() ? 1 : 0)
                    + " saveError=\"" + SaveError() + "\"");
            });
            return "CONTROL-QUEUED";
        }

        /// <summary>Control B: entering a REAL character must still reach Stage (the game plays).</summary>
        public static string EnterGood()
        {
            Log("Q-ENTER-REQUEST emit " + Events.CharSelectRequest + " name=" + Good + " (expect FSM -> Stage)");
            Game.Event.Emit(Events.CharSelectRequest, Good);
            Later(6f, () =>
            {
                Log("Q-ENTER-RESULT fsm=" + Fsm() + " scene=" + Scene() + " dialogOpen=" + (DialogOpen() ? 1 : 0)
                    + " saveError=\"" + SaveError() + "\"");
            });
            return "ENTER-QUEUED";
        }

        public static string BackToMenu()
        {
            Log("Q-BACK-REQUEST emit " + Events.ToMainMenuRequest);
            Game.Event.Emit(Events.ToMainMenuRequest);
            Later(2.5f, () => Log("Q-BACK-RESULT fsm=" + Fsm() + " scene=" + Scene()));
            return "BACK-QUEUED";
        }

        // ------------------------------------------------------- the defect itself --
        /// <summary>Back up the victim slot, then overwrite it with garbage (truncated JSON).</summary>
        public static string Corrupt()
        {
            try
            {
                var src = Path.Combine(SavesDir, Victim + ".json");
                if (!File.Exists(src)) { var m = "Q-CORRUPT-SKIP no such slot " + src; Log(m); return m; }
                if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);
                var bak = Path.Combine(BackupDir, Victim + ".json");
                File.Copy(src, bak, true);
                var before = File.ReadAllText(src);
                File.WriteAllText(src, Garbage);
                var line = "Q-CORRUPT-DONE slot=" + Victim + " backup=" + bak
                    + " beforeLen=" + before.Length + " beforeParses=" + (SaveJsonParseable(before) ? 1 : 0)
                    + " afterLen=" + Garbage.Length + " afterState=" + SlotState(Victim);
                Log(line);
                return line;
            }
            catch (Exception ex)
            {
                var m = "Q-CORRUPT-FAIL " + ex.GetType().Name + ": " + ex.Message;
                Log(m);
                return m;
            }
        }

        /// <summary>Open character select again with the victim corrupt: the prompt MUST appear.</summary>
        public static string CorruptContinue()
        {
            Log("Q-CORRUPT-REQUEST emit " + Events.Fsm.TriggerContinue + " with slot " + Victim
                + " corrupt (" + SlotState(Victim) + "; expect dialogOpen=1)");
            Game.Event.Emit(Events.Fsm.TriggerContinue);
            Later(1.5f, () =>
            {
                Log("Q-CORRUPT-RESULT fsm=" + Fsm() + " scene=" + Scene()
                    + " charSelectOpen=" + (Game.UI != null && Game.UI.IsOpen<CharSelectPanel>() ? 1 : 0)
                    + " dialogOpen=" + (DialogOpen() ? 1 : 0)
                    + " saveError=\"" + SaveError() + "\"");
                Log("Q-CORRUPT-DIALOG-NODE node=" + (Game.UI != null ? "D2ConfirmPanel" : "(no-ui)")
                    + " present=" + (DialogOpen() ? 1 : 0));
            });
            return "CORRUPT-CONTINUE-QUEUED";
        }

        /// <summary>The SECOND reading: the same corrupt slot must not pop a second dialog (per-session dedup).</summary>
        public static string ReopenCharSelect()
        {
            Log("Q-DEDUP-REQUEST emit " + Events.CharSelectRequest + " with empty name (re-open char select)");
            Game.Event.Emit(Events.CharSelectRequest, string.Empty);
            Later(1.5f, () => Log("Q-DEDUP-RESULT fsm=" + Fsm() + " dialogOpen=" + (DialogOpen() ? 1 : 0)
                + " saveError=\"" + SaveError() + "\""));
            return "DEDUP-QUEUED";
        }

        /// <summary>Screenshot into <repo>/.ai-tmp/screenshots/ (outside Assets = never imported).</summary>
        public static string Shot(string file)
        {
            try
            {
                if (!Directory.Exists(ShotDir)) Directory.CreateDirectory(ShotDir);
                var p = Path.Combine(ShotDir, file);
                if (File.Exists(p)) File.Delete(p);
                ScreenCapture.CaptureScreenshot(p);
                var line = "Q-SHOT file=" + file + " path=" + p + " fsm=" + Fsm() + " dialogOpen=" + (DialogOpen() ? 1 : 0);
                Log(line);
                return line;
            }
            catch (Exception ex)
            {
                var m = "Q-SHOT-FAIL " + ex.GetType().Name + ": " + ex.Message;
                Log(m);
                return m;
            }
        }

        /// <summary>Put the victim slot back exactly as it was (byte-for-byte from the backup).</summary>
        public static string Restore()
        {
            try
            {
                var dst = Path.Combine(SavesDir, Victim + ".json");
                var bak = Path.Combine(BackupDir, Victim + ".json");
                if (!File.Exists(bak)) { var m = "Q-RESTORE-SKIP no backup " + bak; Log(m); return m; }
                File.Copy(bak, dst, true);
                var line = "Q-RESTORE-DONE slot=" + Victim + " state=" + SlotState(Victim)
                    + " bytes=" + new FileInfo(dst).Length;
                Log(line);
                return line;
            }
            catch (Exception ex)
            {
                var m = "Q-RESTORE-FAIL " + ex.GetType().Name + ": " + ex.Message;
                Log(m);
                return m;
            }
        }

        public static string Ping() { return "PONG frame=" + UnityEngine.Time.frameCount; }
    }
}
