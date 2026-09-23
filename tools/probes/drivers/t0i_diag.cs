// t0i_diag.cs -- cheap DECISIVE diagnostic for one question:
//   after a T0FIX-H whole-map rebuild (buffer path), is the map actually VISIBLE on screen?
//
// It boots straight to the Stage, then:
//   1. DIAG-A  reads the visible layer roots' activeSelf, the visible ground root's chunk
//              children (count + how many are activeInHierarchy), and how many SpriteRenderers
//              under the whole MapView are activeInHierarchy.
//   2. shot A  full-screen Stage screenshot (mean luminance).
//   3. Force every descendant of the three VISIBLE layer roots to activeInHierarchy == true
//      (SetActive(true) on chunk roots + every tile GameObject) -- a pure measurement control
//      that changes NOTHING about the geometry/sprites.
//   4. DIAG-B + shot B  --> if shot A is black and shot B shows terrain, the map really is
//      invisible because the built subtree is inactive.
//
// ASCII ONLY (PS 5.1 / Roslyn read a BOM-less non-ASCII file as ANSI).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace T0IDiag
{
    public static class D
    {
        internal const string Tag = "T0ID";
        internal static string ShotDir = string.Empty;

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
        internal static Diablo2.Module.IMapModule Map() { return CtxMember("Map") as Diablo2.Module.IMapModule; }
        internal static object Field(object o, string n)
        {
            if (o == null) return null;
            var f = o.GetType().GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f != null ? f.GetValue(o) : null;
        }
        internal static int IntProp(object o, string name)
        {
            if (o == null) return -9999;
            var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (p == null) return -9999;
            var v = p.GetValue(o);
            return v is int ? (int)v : -9999;
        }
        internal static object MapViewObj()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? (object)arr[0] : null;
        }
        internal static GameObject MapViewGo()
        {
            var arr = UnityEngine.Object.FindObjectsByType<Diablo2.Module.Map.MapView>(FindObjectsSortMode.None);
            return arr != null && arr.Length > 0 ? arr[0].gameObject : null;
        }
        internal static string Fsm() { return Game.Fsm != null ? Game.Fsm.Current : "(null)"; }

        internal static int ActiveSr(Transform root)
        {
            if (root == null) return -1;
            var all = root.GetComponentsInChildren<SpriteRenderer>(true);
            var n = 0;
            for (var i = 0; i < all.Length; i++) if (all[i] != null && all[i].gameObject.activeInHierarchy) n++;
            return n;
        }
        internal static int TotalSr(Transform root)
        {
            if (root == null) return -1;
            return root.GetComponentsInChildren<SpriteRenderer>(true).Length;
        }
        internal static int ActiveChildren(Transform root)
        {
            if (root == null) return -1;
            var n = 0;
            for (var i = 0; i < root.childCount; i++) { var c = root.GetChild(i); if (c != null && c.gameObject.activeInHierarchy) n++; }
            return n;
        }
        internal static void ForceActivate(Transform root)
        {
            if (root == null) return;
            root.gameObject.SetActive(true);
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++) if (all[i] != null) all[i].gameObject.SetActive(true);
        }
    }

    public static class Api2
    {
        public static string Ping() { return "PONG frame=" + Time.frameCount; }
        public static string Cfg()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            var st = UnityEngine.InputSystem.InputSystem.settings;
            st.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
            st.editorInputBehaviorInPlayMode =
                UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            var line = "CFG gameRunning=" + (Game.IsRunning ? 1 : 0) + " fsm=" + D.Fsm()
                + " device=\"" + SystemInfo.graphicsDeviceName + "\" res=" + Screen.width + "x" + Screen.height;
            D.Log(line);
            return line;
        }
        public static string Paths(string spec) { D.ShotDir = spec ?? string.Empty; D.KV("PATHS", "shotDir=" + D.ShotDir); return "PATHS-OK"; }
    }

    public static class Tour
    {
        public static string Install(string spec)
        {
            var go = new GameObject("T0IDiagDriver");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var drv = go.AddComponent<T0IDiagDriver>();
            drv.Init(spec ?? string.Empty);
            D.Log("TOUR-INSTALL spec=" + spec);
            return "INSTALLED";
        }
    }

    public class T0IDiagDriver : MonoBehaviour
    {
        private readonly List<Func<bool>> _plan = new List<Func<bool>>();
        private int _pc;
        private string _cur = "?";
        private float _at;
        private bool _done;
        private int _stFrame = -1;
        private bool _capBusy, _capDone, _capOk;
        private string _capInfo = "";
        private string _capPending = "";
        private string _saveName = "";
        private string _shotA = "", _shotB = "";

        public void Init(string spec)
        {
            D.ShotDir = spec ?? string.Empty;
            D.KV("PATHS", "shotDir=" + D.ShotDir);
            D.Log("DRIVER-INIT frame=" + Time.frameCount + " screen=" + Screen.width + "x" + Screen.height);
            BuildPlan();
            D.KV("PLAN", "stations=" + _plan.Count);
        }

        private void Add(string name, Func<bool> f)
        {
            var nm = name;
            _plan.Add(() =>
            {
                if (_cur != nm) { _cur = nm; _at = Time.unscaledTime; _stFrame = -1; D.Log("PHASE " + nm + " fsm=" + D.Fsm()); }
                return f();
            });
        }
        private bool Elapsed(float s) { return Time.unscaledTime - _at >= s; }
        private bool WaitFrames(int n) { if (_stFrame < 0) { _stFrame = Time.frameCount; return false; } return Time.frameCount - _stFrame >= n; }

        private void Update()
        {
            if (_done) return;
            try
            {
                var guard = 0;
                while (!_done && _pc < _plan.Count && guard++ < 4000)
                {
                    if (!_plan[_pc]()) return;
                    _pc++; _cur = "?"; _at = Time.unscaledTime;
                }
                if (_pc >= _plan.Count) Finish("end");
            }
            catch (Exception e)
            {
                D.Warn("STATION-FATAL pc=" + _pc + " name=" + _cur + " ex=" + e.GetType().Name + ": " + e.Message);
                _pc++; _cur = "?"; _at = Time.unscaledTime;
            }
        }

        private IEnumerator CaptureFullRoutine(string file)
        {
            _capDone = false; _capOk = false; _capInfo = "";
            yield return new WaitForEndOfFrame();
            Texture2D full = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full == null) _capInfo = "capture-null";
                else
                {
                    var px = full.GetPixels();
                    float sum = 0f; var mx = 0f;
                    for (var i = 0; i < px.Length; i++) { var l = px[i].r + px[i].g + px[i].b; sum += l; if (l > mx) mx = l; }
                    var mean = px.Length > 0 ? sum / (px.Length * 3f) : -1f;
                    var bytes = full.EncodeToPNG();
                    File.WriteAllBytes(file, bytes);
                    _capOk = true;
                    _capInfo = "full " + full.width + "x" + full.height + " bytes=" + bytes.Length
                        + " mean=" + mean.ToString("0.0000") + " max=" + mx.ToString("0.000");
                }
            }
            catch (Exception e) { _capInfo = "EX-" + e.GetType().Name + "-" + e.Message; D.Warn("CAPTURE-FAIL " + file + " " + _capInfo); }
            finally { if (full != null) UnityEngine.Object.Destroy(full); }
            _capDone = true;
        }
        private bool CaptureFullNow(string file)
        {
            if (!_capBusy) { _capPending = file; _capBusy = true; StartCoroutine(CaptureFullRoutine(file)); return false; }
            if (!_capDone) return false;
            _capBusy = false;
            D.KV("TILE", "file=" + _capPending + " ok=" + (_capOk ? 1 : 0) + " " + _capInfo);
            _capPending = "";
            return true;
        }

        private void Diag(string who)
        {
            var mv = D.MapViewObj();
            var go = D.MapViewGo();
            var gRoot = D.Field(mv, "_groundRoot") as Transform;
            var oRoot = D.Field(mv, "_objectRoot") as Transform;
            var ovRoot = D.Field(mv, "_overlayRoot") as Transform;
            var bgRoot = D.Field(mv, "_bufGroundRoot") as Transform;
            var chunk0 = (gRoot != null && gRoot.childCount > 0) ? gRoot.GetChild(0) : null;
            var chunk0Tile0 = (chunk0 != null && chunk0.childCount > 0) ? chunk0.GetChild(0) : null;
            D.KV("DIAG-" + who, "fsm=" + D.Fsm()
                + " builtChunks=" + D.IntProp(mv, "BuiltChunkCount")
                + " inProg=" + (D.IntProp(mv, "RebuildFramesLast") >= 0 ? 0 : 0)
                + " gRoot_exists=" + (gRoot != null ? 1 : 0)
                + " gRoot_activeSelf=" + (gRoot != null ? (gRoot.gameObject.activeSelf ? 1 : 0) : -1)
                + " gRoot_activeInHierarchy=" + (gRoot != null ? (gRoot.gameObject.activeInHierarchy ? 1 : 0) : -1)
                + " gRoot_children=" + (gRoot != null ? gRoot.childCount : -1)
                + " gRoot_activeChildren=" + D.ActiveChildren(gRoot)
                + " gRoot_activeSelfChildren=" + (gRoot != null ? CountActiveSelf(gRoot) : -1)
                + " chunk0_name=" + (chunk0 != null ? chunk0.name : "-")
                + " chunk0_activeSelf=" + (chunk0 != null ? (chunk0.gameObject.activeSelf ? 1 : 0) : -1)
                + " chunk0_children=" + (chunk0 != null ? chunk0.childCount : -1)
                + " chunk0Tile0_activeSelf=" + (chunk0Tile0 != null ? (chunk0Tile0.gameObject.activeSelf ? 1 : 0) : -1)
                + " chunk0Tile0_activeInHierarchy=" + (chunk0Tile0 != null ? (chunk0Tile0.gameObject.activeInHierarchy ? 1 : 0) : -1));
            D.KV("DIAG-" + who + "-SR", "ground_totalSR=" + D.TotalSr(gRoot) + " ground_activeSR=" + D.ActiveSr(gRoot)
                + " object_totalSR=" + D.TotalSr(oRoot) + " object_activeSR=" + D.ActiveSr(oRoot)
                + " overlay_totalSR=" + D.TotalSr(ovRoot) + " overlay_activeSR=" + D.ActiveSr(ovRoot)
                + " bufGround_exists=" + (bgRoot != null ? 1 : 0)
                + " bufGround_activeInHierarchy=" + (bgRoot != null ? (bgRoot.gameObject.activeInHierarchy ? 1 : 0) : -1)
                + " wholeMapView_totalSR=" + D.TotalSr(go != null ? go.transform : null)
                + " wholeMapView_activeSR=" + D.ActiveSr(go != null ? go.transform : null));
        }
        private static int CountActiveSelf(Transform root)
        {
            var n = 0;
            for (var i = 0; i < root.childCount; i++) { var c = root.GetChild(i); if (c != null && c.gameObject.activeSelf) n++; }
            return n;
        }

        private void BuildPlan()
        {
            Add("wait60", () => WaitFrames(60));
            Add("boot", () =>
            {
                if (D.Fsm() == "MainMenu") return true;
                if (Elapsed(1.5f)) Game.Fsm.Trigger(Events.Fsm.TriggerBootDone);
                if (Elapsed(25f)) { D.Warn("boot timeout fsm=" + D.Fsm()); return true; }
                return false;
            });
            Add("charselect", () =>
            {
                if (D.Fsm() == "CharSelect") return true;
                if (Elapsed(0.8f)) Game.Fsm.Trigger(Events.Fsm.TriggerNewGame);
                if (Elapsed(20f)) { D.Warn("charselect timeout fsm=" + D.Fsm()); return true; }
                return false;
            });
            Add("loading", () =>
            {
                if (D.Fsm() == "Stage") return true;
                if (Elapsed(1.0f)) EnterStage();
                if (Elapsed(60f)) { D.Warn("loading timeout fsm=" + D.Fsm()); return true; }
                return false;
            });
            Add("stage-settle", () =>
            {
                if (D.Fsm() != "Stage" || !(Game.UI != null && Game.UI.IsOpen<Diablo2.UI.HudPanel>()))
                {
                    if (Elapsed(40f)) { D.Warn("stage wait timeout fsm=" + D.Fsm()); return true; }
                    return false;
                }
                var mv = D.MapViewObj();
                if (mv == null) return false;
                if (D.IntProp(mv, "PendingRetireChunks") > 0) { if (Elapsed(20f)) return true; return false; }
                if (!Elapsed(3.0f)) return false;
                return true;
            });
            Add("diag-a", () => { Diag("A"); return true; });
            Add("shot-a", () =>
            {
                if (string.IsNullOrEmpty(_shotA)) _shotA = D.ShotDir + "/t0i_diag_stage_asis.png";
                return CaptureFullNow(_shotA);
            });
            Add("force-activate", () =>
            {
                var mv = D.MapViewObj();
                var gRoot = D.Field(mv, "_groundRoot") as Transform;
                var oRoot = D.Field(mv, "_objectRoot") as Transform;
                var ovRoot = D.Field(mv, "_overlayRoot") as Transform;
                D.ForceActivate(gRoot); D.ForceActivate(oRoot); D.ForceActivate(ovRoot);
                D.KV("FORCE-ACTIVATE", "activated the three VISIBLE layer roots' whole subtrees (geometry/sprites untouched)");
                return true;
            });
            Add("wait-after", () => WaitFrames(3));
            Add("diag-b", () => { Diag("B"); return true; });
            Add("shot-b", () =>
            {
                if (string.IsNullOrEmpty(_shotB)) _shotB = D.ShotDir + "/t0i_diag_stage_forced.png";
                return CaptureFullNow(_shotB);
            });
            Add("finish", () => { Finish("plan-end"); return true; });
            Add("hold", () => true);
        }

        private void EnterStage()
        {
            var save = D.CtxMember("Save") as Diablo2.Module.ISaveModule;
            if (save != null)
            {
                var list = save.List();
                if (list != null && list.Count > 0) _saveName = list[0];
            }
            if (string.IsNullOrEmpty(_saveName)) { D.Warn("no save; using X162012"); _saveName = "X162012"; }
            D.KV("ENTER-STAGE", "save=\"" + _saveName + "\"");
            Game.Event.Emit<string>(Events.CharSelectRequest, _saveName);
        }

        private void Finish(string why)
        {
            if (_done) return;
            _done = true;
            D.KV("FINISH", "why=" + why + " pc=" + _pc + " fsm=" + D.Fsm());
            var done = D.ShotDir + "/../test/t0i_diag_done.txt";
            try { File.WriteAllText(done, "T0ID-DONE why=" + why); } catch { }
        }
        private void OnApplicationQuit() { Finish("appquit"); }
    }
}
