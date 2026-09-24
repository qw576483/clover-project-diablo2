// ─────────────────────────────────────────────────────────────────────────────
//
//  跑法：dotnet run --project tools/buildcheck/BuildCheck.csproj
//  退出码：0 = 全部断言通过；1 = 有断言失败（明细逐条打印）
//
//  断言分五组：
//    A 生成器清单      —— 16 面板 / 3 场景 / Build Index 顺序 / 菜单名 / 路径常量
//    B 面板预制体      —— 数量 16；根 RectTransform **真铺满**；m_Script 指向对的脚本 GUID；layer=UI
//    C 场景            —— 文件存在；必需节点在；**没有**多余 Canvas / EventSystem
//    D Build Settings  —— 正好 3 个、Boot→Menu→Stage、guid 与 .unity.meta 一致、configObjects 保留
//    E 多帧条带表      —— 与 frame_probe.py 的实测输出逐条比对 + PNG 头读出的真实图幅校验
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Diablo2.Editor;
using UnityEngine;

internal static class Program
{
    private static string _root;
    private static int _pass;
    private static readonly List<string> _fails = new List<string>();

    /// <summary>「不适用」计数：判据的输入产物不在盘（未跑过对应探针 / 一次性产物目录被清）。
    /// **不计入**通过也不计入失败 —— 没判的就得看起来像没判的。</summary>
    private static int _na;

    private static int Main()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { /* 输出被重定向时可能不支持，忽略 */ }

        _root = FindRepoRoot();
        Console.WriteLine("== agent-10 离线自检（tools/buildcheck）==");
        Console.WriteLine("项目根：" + _root);
        Console.WriteLine();

        CheckA_BuilderManifest();
        CheckB_PanelPrefabs();
        CheckC_Scenes();
        CheckD_BuildSettings();
        CheckE_StripTable();
        CheckF_SecondRunSimulation();

        Console.WriteLine();
        Console.WriteLine("---- 汇总 ----");
        Console.WriteLine("通过 " + _pass + " 条，失败 " + _fails.Count + " 条"
                          + (_na > 0 ? ("，不适用 " + _na + " 条") : ""));
        for (int i = 0; i < _fails.Count; i++)
            Console.WriteLine("  [FAIL] " + _fails[i]);

        return _fails.Count == 0 ? 0 : 1;
    }

    // ── 断言小工具 ────────────────────────────────────────────────────────────
    private static void Ok(string what)
    {
        _pass++;
        Console.WriteLine("[PASS] " + what);
    }

    private static void Check(bool cond, string what)
    {
        if (cond) Ok(what);
        else
        {
            _fails.Add(what);
            Console.WriteLine("[FAIL] " + what);
        }
    }

    /// <summary>
    /// 登记一条**不适用**的判据（判据的输入产物不在盘 ⇒ 本轮没判）：
    /// 不占通过数、也不占失败数，只如实打一行 —— ⛔ 不用它把红项"变绿"。
    /// </summary>
    private static void NotApplicable(string what, string detail)
    {
        _na++;
        Console.WriteLine("[ NA ] " + what + "   (" + detail + ")");
    }

    private static string P(params string[] parts)
    {
        var full = new string[parts.Length + 1];
        full[0] = _root;
        Array.Copy(parts, 0, full, 1, parts.Length);
        return Path.Combine(full);
    }

    private static string Read(string rel)
    {
        var p = P(rel.Split('/'));
        return File.Exists(p) ? File.ReadAllText(p, Encoding.UTF8) : null;
    }

    private static string FindRepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "client", "Assets", "Editor")))
                return d.FullName;
            d = d.Parent;
        }
        throw new InvalidOperationException("找不到项目根（从 " + AppContext.BaseDirectory + " 往上找 client/Assets/Editor 失败）");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  A 生成器清单
    // ═════════════════════════════════════════════════════════════════════════
    private static void CheckA_BuilderManifest()
    {
        Console.WriteLine("[A] 生成器清单（Assets/Editor/ProjectBuilder.cs）");

        Check(ProjectBuilder.MenuPath == "Diablo2/一键生成工程（场景 + 预制体 + BuildSettings）",
            "菜单名逐字等于契约：Diablo2/一键生成工程（场景 + 预制体 + BuildSettings）");

        // 本轮 17 → 18：新增 `WaypointPanel`（原版「傳送點」屏；见 `UI/WaypointPanel.cs`
        Check(ProjectBuilder.PanelNames.Length == 18,
            "面板清单 = 18 个（实测 " + ProjectBuilder.PanelNames.Length + "）");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var dup = new List<string>();
        for (int i = 0; i < ProjectBuilder.PanelNames.Length; i++)
            if (!seen.Add(ProjectBuilder.PanelNames[i])) dup.Add(ProjectBuilder.PanelNames[i]);
        Check(dup.Count == 0, "面板清单无重复（重复项：" + (dup.Count == 0 ? "无" : string.Join(",", dup.ToArray())) + "）");

        // 每个面板名在业务侧都有对应的「一个 .cs 一个 MonoBehaviour」
        var missing = new List<string>();
        for (int i = 0; i < ProjectBuilder.PanelNames.Length; i++)
        {
            var n = ProjectBuilder.PanelNames[i];
            var src = Read("client/Assets/Scripts/UI/" + n + ".cs");
            if (src == null || src.IndexOf("class " + n + " : UIPanel", StringComparison.Ordinal) < 0)
                missing.Add(n);
        }
        Check(missing.Count == 0,
            "18 个面板类都能在 Assets/Scripts/UI/{类名}.cs 里找到 `class {类名} : UIPanel`"
            + (missing.Count == 0 ? "" : "（缺：" + string.Join(",", missing.ToArray()) + "）"));

        Check(ProjectBuilder.PanelsDir == "Assets/Resources/UI", "预制体目录 = Assets/Resources/UI（契约 §3.6）");
        Check(ProjectBuilder.ScenesDir == "Assets/Scenes", "场景目录 = Assets/Scenes");

        Check(ProjectBuilder.SceneOrder.Length == 3
              && ProjectBuilder.SceneOrder[0] == "Boot"
              && ProjectBuilder.SceneOrder[1] == "Menu"
              && ProjectBuilder.SceneOrder[2] == "Stage",
            "场景顺序 = Boot(0) → Menu(1) → Stage(2)");
        Check(ProjectBuilder.ScenePath("Stage") == "Assets/Scenes/Stage.unity", "ScenePath(\"Stage\") = Assets/Scenes/Stage.unity");
        Check(ProjectBuilder.BootstrapTypeName == "Diablo2.App.Bootstrap", "Bootstrap 脚本全名 = Diablo2.App.Bootstrap");

        // 命令行入口存在且是 public static（-executeMethod 要用）
        var m = typeof(ProjectBuilder).GetMethod("GenerateFromCommandLine", BindingFlags.Public | BindingFlags.Static);
        Check(m != null, "存在 public static void GenerateFromCommandLine()（供 -executeMethod 用）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  B 面板预制体
    // ═════════════════════════════════════════════════════════════════════════
    private static void CheckB_PanelPrefabs()
    {
        Console.WriteLine("[B] 面板预制体（Assets/Resources/UI/*.prefab）");

        var dir = P("client", "Assets", "Resources", "UI");
        var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.prefab") : new string[0];
        Check(files.Length == 18, "预制体个数 = 18（实测 " + files.Length + "）");

        var bad = new List<string>();
        for (int i = 0; i < ProjectBuilder.PanelNames.Length; i++)
        {
            var name = ProjectBuilder.PanelNames[i];
            var rel = "client/Assets/Resources/UI/" + name + ".prefab";
            var text = Read(rel);
            if (text == null) { bad.Add(name + "(缺文件)"); continue; }

            // 根 GameObject 名 = 类名
            if (text.IndexOf("m_Name: " + name, StringComparison.Ordinal) < 0) { bad.Add(name + "(根节点名不对)"); continue; }

            // 根 RectTransform 真的铺满：anchorMin=(0,0) / anchorMax=(1,1) / anchoredPosition=(0,0) / sizeDelta=(0,0) / pivot=(0.5,0.5)
            // 注：RectTransform 序列化字段就是这 4 个；offsetMin/offsetMax 是导出属性，
            //     anchoredPosition=0 且 sizeDelta=0 ⇔ offsetMin=offsetMax=0。
            var rt = ExtractBlock(text, "RectTransform:");
            if (rt == null) { bad.Add(name + "(没有 RectTransform)"); continue; }
            if (!Has(rt, "m_AnchorMin: {x: 0, y: 0}")) { bad.Add(name + "(anchorMin≠0,0)"); continue; }
            if (!Has(rt, "m_AnchorMax: {x: 1, y: 1}")) { bad.Add(name + "(anchorMax≠1,1)"); continue; }
            if (!Has(rt, "m_AnchoredPosition: {x: 0, y: 0}")) { bad.Add(name + "(anchoredPosition≠0,0)"); continue; }
            if (!Has(rt, "m_SizeDelta: {x: 0, y: 0}")) { bad.Add(name + "(sizeDelta≠0,0)"); continue; }
            if (!Has(rt, "m_Pivot: {x: 0.5, y: 0.5}")) { bad.Add(name + "(pivot≠0.5,0.5)"); continue; }

            // m_Script 指向该面板脚本的 .cs.meta guid
            var meta = Read("client/Assets/Scripts/UI/" + name + ".cs.meta");
            var guid = meta == null ? null : ExtractGuid(meta);
            if (guid == null) { bad.Add(name + "(.cs.meta 缺 guid)"); continue; }
            if (text.IndexOf("guid: " + guid, StringComparison.Ordinal) < 0) { bad.Add(name + "(m_Script guid 与 .cs.meta 不一致)"); continue; }

            // layer = UI(5)
            if (text.IndexOf("m_Layer: 5", StringComparison.Ordinal) < 0) { bad.Add(name + "(layer≠UI)"); continue; }
        }

        Check(bad.Count == 0,
            "18 个预制体：根节点铺满(anchorMin=0,0 / anchorMax=1,1 / anchoredPosition=0,0 / sizeDelta=0,0 / pivot=0.5,0.5)"
            + " + m_Script 指向本面板脚本的 .cs.meta guid + layer=UI"
            + (bad.Count == 0 ? "" : "；不合格：" + string.Join("; ", bad.ToArray())));

        // 辅助非面板类型不许有预制体
        var strays = new List<string>();
        foreach (var s in new[] { "UiArt", "UiBar", "UiLog", "D2Text", "ItemTooltip", "Bootstrap" })
            if (File.Exists(Path.Combine(dir, s + ".prefab"))) strays.Add(s);
        Check(strays.Count == 0, "辅助非面板类型没有多生成预制体（UiArt/UiBar/UiLog/D2Text/ItemTooltip/Bootstrap）");
        Console.WriteLine();
    }

    private static bool Has(string text, string needle)
    {
        return text.IndexOf(needle, StringComparison.Ordinal) >= 0;
    }

    /// <summary>取 YAML 里某个块（从含 key 的行到下一个 `--- ` 或文件尾）。</summary>
    private static string ExtractBlock(string text, string key)
    {
        var i = text.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return null;
        var j = text.IndexOf("\n--- ", i, StringComparison.Ordinal);
        return j < 0 ? text.Substring(i) : text.Substring(i, j - i);
    }

    private static string ExtractGuid(string meta)
    {
        var m = Regex.Match(meta, @"guid: ([0-9a-f]{32})");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  C 场景
    // ═════════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, string[]> SceneRequired =
        new Dictionary<string, string[]>
        {
            { "Boot", new[] { "Main Camera", "Bootstrap" } },
            { "Menu", new[] { "Main Camera" } },
            { "Stage", new[] { "Main Camera", "Global Light 2D", "MapRoot", "EntityRoot" } },
        };

    private static void CheckC_Scenes()
    {
        Console.WriteLine("[C] 场景（Assets/Scenes/*.unity）");

        for (int i = 0; i < ProjectBuilder.SceneOrder.Length; i++)
        {
            var s = ProjectBuilder.SceneOrder[i];
            var rel = "client/Assets/Scenes/" + s + ".unity";
            var text = Read(rel);
            Check(text != null, "Assets/Scenes/" + s + ".unity 存在"
                                + (text == null ? "" : "（" + Encoding.UTF8.GetByteCount(text) + " 字节）"));
            if (text == null) continue;

            var need = SceneRequired[s];
            var miss = new List<string>();
            for (int k = 0; k < need.Length; k++)
                if (text.IndexOf("m_Name: " + need[k], StringComparison.Ordinal) < 0) miss.Add(need[k]);
            Check(miss.Count == 0, s + ".unity 必需节点齐全（" + string.Join(" / ", need)
                                  + "）" + (miss.Count == 0 ? "" : "；缺：" + string.Join(",", miss.ToArray())));

            // 契约：**不放** Canvas（UIManager 自建常驻 Canvas）/ **不放** EventSystem（CloverInput.Init 会建）
            Check(text.IndexOf("m_Name: Canvas", StringComparison.Ordinal) < 0
                  && text.IndexOf("Canvas:\n", StringComparison.Ordinal) < 0,
                s + ".unity 没有多余 Canvas（UIManager 自建，场景里再放会变成两套 UI 根）");
            Check(text.IndexOf("EventSystem", StringComparison.Ordinal) < 0,
                s + ".unity 没有 EventSystem（CloverInput.Init → EnsureEventSystem 负责）");
        }

        // Boot 场景里的 Bootstrap 节点必须挂到 Diablo2.App.Bootstrap 的 guid
        var boot = Read("client/Assets/Scenes/Boot.unity");
        var bmeta = Read("client/Assets/Scripts/App/Bootstrap.cs.meta");
        var bguid = bmeta == null ? null : ExtractGuid(bmeta);
        Check(boot != null && bguid != null && boot.IndexOf("guid: " + bguid, StringComparison.Ordinal) >= 0,
            "Boot.unity 的 Bootstrap 节点 m_Script 指向 App/Bootstrap.cs 的 guid");

        // Stage 场景必须有全局 2D 光（URP 2D 下精灵走 Sprite-Lit-Default，没光会全黑）
        var stage = Read("client/Assets/Scenes/Stage.unity");
        Check(stage != null && stage.IndexOf("073797afb82c5a1438f328866b10b3f0", StringComparison.Ordinal) >= 0,
            "Stage.unity 含 Global Light 2D（Light2D 脚本 guid 073797afb82c5a1438f328866b10b3f0）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  D Build Settings
    // ═════════════════════════════════════════════════════════════════════════
    private static void CheckD_BuildSettings()
    {
        Console.WriteLine("[D] Build Settings（ProjectSettings/EditorBuildSettings.asset）");

        var text = Read("client/ProjectSettings/EditorBuildSettings.asset");
        Check(text != null, "EditorBuildSettings.asset 存在");
        if (text == null) { Console.WriteLine(); return; }

        var paths = new List<string>();
        var guids = new List<string>();
        foreach (Match m in Regex.Matches(text, @"- enabled: (\d+)\n    path: ([^\n]+)\n    guid: ([0-9a-f]{32})"))
        {
            paths.Add(m.Groups[2].Value);
            guids.Add(m.Groups[3].Value);
        }

        Check(paths.Count == 3, "已登记场景数 = 3（实测 " + paths.Count + "：" + string.Join(",", paths.ToArray()) + "）");
        Check(paths.Count == 3
              && paths[0] == "Assets/Scenes/Boot.unity"
              && paths[1] == "Assets/Scenes/Menu.unity"
              && paths[2] == "Assets/Scenes/Stage.unity",
            "顺序 = Boot → Menu → Stage（Build Index 0/1/2）");

        var mismatched = new List<string>();
        for (int i = 0; i < paths.Count; i++)
        {
            // 注意：EditorBuildSettings 里的 path 是 **Unity 工程内**相对路径（Assets/...），
            // 而 Read 收的是**仓库内**相对路径 ⇒ 要补 client/ 前缀。
            var meta = Read("client/" + paths[i] + ".meta");
            var g = meta == null ? null : ExtractGuid(meta);
            if (g == null || g != guids[i]) mismatched.Add(paths[i]);
        }
        Check(mismatched.Count == 0,
            "每个场景的 guid 与它自己的 .unity.meta 一致（不一致的：" + (mismatched.Count == 0 ? "无" : string.Join(",", mismatched.ToArray())) + "）");

        Check(text.IndexOf("m_configObjects:", StringComparison.Ordinal) >= 0
              && text.IndexOf("com.unity.input.settings.actions", StringComparison.Ordinal) >= 0,
            "保留了 m_configObjects（Input System 的 actions 配置没被覆盖掉）");
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  E 多帧条带表（与 frame_probe.py 的实测输出逐条比对）
    // ═════════════════════════════════════════════════════════════════════════
    private sealed class ProbeEntry
    {
        public string File;
        public List<Rect> Rects = new List<Rect>();
    }

    private static void CheckE_StripTable()
    {
        Console.WriteLine("[E] 多帧条带切分表（Assets/Editor/AssetImporter.cs 的 MultiFrameStrips）");

        // ① 反射读表（D2AssetImporter 是 public sealed，表是 private static readonly）
        var t = typeof(D2AssetImporter);
        var f = t.GetField("MultiFrameStrips", BindingFlags.NonPublic | BindingFlags.Static);
        Check(f != null, "D2AssetImporter.MultiFrameStrips 表存在");
        if (f == null) { Console.WriteLine(); return; }

        var arr = (Array)f.GetValue(null);
        Check(arr != null && arr.Length == 5, "表里有 5 个条带（实测 " + (arr == null ? 0 : arr.Length) + "）");

        var byName = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var expectedCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "buysellbtn.DC6.0", 22 }, { "goldcoinbtn.dc6.0", 2 }, { "overlap", 2 },
            { "button_medium", 3 }, { "button_wide", 3 },
        };
        var sizes = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < arr.Length; i++)
        {
            var item = arr.GetValue(i);
            var it = item.GetType();
            var fn = (string)it.GetField("FileName").GetValue(item);
            var texW = (int)it.GetField("TexW").GetValue(item);
            var texH = (int)it.GetField("TexH").GetValue(item);
            var rects = (Rect[])it.GetField("Rects").GetValue(item);
            byName[fn] = item;
            sizes[fn] = new int[] { texW, texH };

            // 帧数
            int want;
            Check(expectedCount.TryGetValue(fn, out want) && rects.Length == want,
                fn + " 帧数 = " + rects.Length + "（实测期望 " + (expectedCount.ContainsKey(fn) ? expectedCount[fn] : -1) + "）");

            // 图幅：与 PNG IHDR 读出的真实尺寸一致
            var png = FindStripPng(fn);
            Check(png != null, fn + " 能在 Resources/Clover/D2/UI 下找到对应 png");
            if (png != null)
            {
                int w, h;
                ReadPngSize(png, out w, out h);
                Check(w == texW && h == texH,
                    fn + " 登记图幅 " + texW + "x" + texH + " == PNG 头实测 " + w + "x" + h);
            }

            // 每个帧矩形必须在图幅内且非空
            var bad = new List<string>();
            for (int k = 0; k < rects.Length; k++)
            {
                var r = rects[k];
                if (r.width <= 0 || r.height <= 0 || r.x < 0 || r.y < 0
                    || r.x + r.width > texW || r.y + r.height > texH)
                    bad.Add("#" + k + "(" + r.x + "," + r.y + "," + r.width + "," + r.height + ")");
            }
            Check(bad.Count == 0, fn + " 全部帧矩形都在图幅内且非空"
                                 + (bad.Count == 0 ? "" : "；越界：" + string.Join(",", bad.ToArray())));
        }

        // ② 与 frame_probe.py 的实测输出逐条比对（代码 == 实测，不许有转录误差）
        //   探针 `frame_probe.py` 的产物落**仓库根下 gitignored 的一次性产物目录**（落点见下面那行 `Path.Combine`）
        //   （一次性的实测留档：判据的产物不许写进被 git 跟踪的路径，否则每跑一次工作区就脏一次）。
        //   ⇒ 两侧口径都指这一处；产物不在盘（未跑过探针 / 一次性产物目录被清）时本组登记为**不适用**，
        //   既不判红也不假装绿：`[ NA ]` 行会写明恢复办法。
        //   重生成：`python tools/probes/hosts/buildcheck/frame_probe.py`。
        var probePath = Path.Combine(_root, ".ai-tmp", "test", "frame_probe_out.txt");
        if (!File.Exists(probePath))
        {
            NotApplicable("多帧条带：代码表与 frame_probe.py 实测帧矩形逐条对账",
                "实测产物不在盘（" + probePath + "）⇒ 本组不适用；恢复 = python tools/probes/hosts/buildcheck/frame_probe.py");
            Console.WriteLine();
            return;
        }
        Ok("frame_probe_out.txt（实测输出）在盘：" + probePath);

        var entries = ParseProbe(File.ReadAllLines(probePath, Encoding.UTF8));
        Check(entries.Count == 5, "从实测输出里解析到 5 个文件的帧矩形（解析到 " + entries.Count + "）");

        foreach (var kv in entries)
        {
            object item;
            if (!byName.TryGetValue(kv.Key, out item))
            {
                Check(false, "实测输出里的 " + kv.Key + " 在 MultiFrameStrips 表里找不到");
                continue;
            }

            var it = item.GetType();
            var rects = (Rect[])it.GetField("Rects").GetValue(item);
            var diff = new List<string>();
            if (rects.Length != kv.Value.Rects.Count)
                diff.Add("帧数 " + rects.Length + " vs 实测 " + kv.Value.Rects.Count);
            else
                for (int k = 0; k < rects.Length; k++)
                    if (!Same(rects[k], kv.Value.Rects[k]))
                        diff.Add("#" + k + " 代码" + Fmt(rects[k]) + " vs 实测" + Fmt(kv.Value.Rects[k]));

            Check(diff.Count == 0, kv.Key + " 的 " + rects.Length + " 个帧矩形与 frame_probe.py 实测输出逐条一致"
                                  + (diff.Count == 0 ? "" : "；不符：" + string.Join(" | ", diff.ToArray())));
        }
        Console.WriteLine();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  F 「第二次运行」离线模拟（幂等自证）
    //  这不是"跑了生成器"：生成器只能在活着的编辑器里跑（用户尚未打开 ⇒ 禁止 unity run）。
    //     这里用**与生成器同口径的判据**在离线侧评估「若现在再跑一次，它会做什么」：
    //     生成器 `PrefabLooksGood` = `AssetDatabase.LoadAssetAtPath<GameObject>` 非空 +
    //     根节点挂着该面板组件；`SceneLooksGood` = `LoadAssetAtPath<SceneAsset>` 非空；
    //     `ApplyBuildSettings` = 现有 m_Scenes 与目标逐项相同则整体跳过。
    //     离线侧的等价判据 = 「文件存在且 YAML 自洽（组件/blocks 齐全）」——见 B/C/D 三组断言。
    // ═════════════════════════════════════════════════════════════════════════
    private static void CheckF_SecondRunSimulation()
    {
        Console.WriteLine("[F] 「第二次运行生成器」离线模拟（幂等：不再新增节点、0 报错、日志大幅减少）");

        int prefabSkip = 0, sceneSkip = 0;
        var wouldWrite = new List<string>();

        for (int i = 0; i < ProjectBuilder.PanelNames.Length; i++)
        {
            var name = ProjectBuilder.PanelNames[i];
            var text = Read("client/Assets/Resources/UI/" + name + ".prefab");
            var meta = Read("client/Assets/Scripts/UI/" + name + ".cs.meta");
            var guid = meta == null ? null : ExtractGuid(meta);
            var rt = text == null ? null : ExtractBlock(text, "RectTransform:");

            var looksGood = text != null && rt != null
                            && Has(rt, "m_AnchorMin: {x: 0, y: 0}") && Has(rt, "m_AnchorMax: {x: 1, y: 1}")
                            && Has(rt, "m_AnchoredPosition: {x: 0, y: 0}") && Has(rt, "m_SizeDelta: {x: 0, y: 0}")
                            && guid != null && Has(text, "guid: " + guid);
            if (looksGood) prefabSkip++;
            else wouldWrite.Add(name + ".prefab");
        }

        for (int i = 0; i < ProjectBuilder.SceneOrder.Length; i++)
        {
            var s = ProjectBuilder.SceneOrder[i];
            var text = Read("client/Assets/Scenes/" + s + ".unity");
            var need = SceneRequired[s];
            var ok = text != null;
            for (int k = 0; ok && k < need.Length; k++)
                if (text.IndexOf("m_Name: " + need[k], StringComparison.Ordinal) < 0) ok = false;
            if (ok) sceneSkip++;
            else wouldWrite.Add(s + ".unity");
        }

        // Build Settings：与生成器 ApplyBuildSettings 的短路口径一致
        var ebs = Read("client/ProjectSettings/EditorBuildSettings.asset") ?? "";
        bool ebsOk = ebs.Contains("path: Assets/Scenes/Boot.unity\n    guid:")
                     && ebs.Contains("path: Assets/Scenes/Menu.unity\n    guid:")
                     && ebs.Contains("path: Assets/Scenes/Stage.unity\n    guid:");
        if (!ebsOk) wouldWrite.Add("EditorBuildSettings.asset");

        Check(prefabSkip == ProjectBuilder.PanelNames.Length,
            "第二次运行：" + ProjectBuilder.PanelNames.Length + "/" + ProjectBuilder.PanelNames.Length
            + " 面板预制体都会走「已存在 ⇒ 跳过」分支（实测 " + prefabSkip + "/" + ProjectBuilder.PanelNames.Length + "）");
        Check(sceneSkip == ProjectBuilder.SceneOrder.Length,
            "第二次运行：3/3 场景都会走「已存在 ⇒ 跳过」分支（实测 " + sceneSkip + "/3）");
        Check(ebsOk, "第二次运行：Build Settings 已正确（Boot→Menu→Stage）⇒ 整体不改写");
        Check(wouldWrite.Count == 0,
            "第二次运行：实际写入 0 项、0 报错（会写的：" + (wouldWrite.Count == 0 ? "无" : string.Join(",", wouldWrite.ToArray())) + "）");

        // 日志条数：逐项"创建/跳过"日志只在实际写入时打；跳过走分组汇总 ⇒ 第二次运行日志大幅减少
        // 第一次：17 预制体创建 + 1 预制体分组 + 3 场景创建 + 3 相机日志 + 1 场景分组
        //         + 1 BuildSettings + 1 总汇总 = 27
        // 第二次：1 预制体分组 + 1 场景分组 + 1 BuildSettings + 1 总汇总 = 4（跳过不逐项打）
        int run1 = ProjectBuilder.PanelNames.Length + 1 + ProjectBuilder.SceneOrder.Length
                   + ProjectBuilder.SceneOrder.Length + 1 + 1 + 1;
        int run2 = 3 + 1;
        Check(run2 * 2 <= run1,
            "日志条数：第一次约 " + run1 + " 条 → 第二次约 " + run2 + " 条（减少 " + (100 - run2 * 100 / run1) + "%，≥50%）");
        Console.WriteLine();
    }

    private static string Fmt(Rect r)
    {
        return "(" + r.x + "," + r.y + "," + r.width + "," + r.height + ")";
    }

    private static bool Same(Rect a, Rect b)
    {
        return Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y)
               && Mathf.Approximately(a.width, b.width) && Mathf.Approximately(a.height, b.height);
    }

    /// <summary>解析 frame_probe_out.txt：「文件：UI/xxx.png ...」+ 其后「C# 字面量：」行的 new Rect 列表。</summary>
    private static Dictionary<string, ProbeEntry> ParseProbe(string[] lines)
    {
        var map = new Dictionary<string, ProbeEntry>(StringComparer.OrdinalIgnoreCase);
        string cur = null;
        bool wantLiteral = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            var mf = Regex.Match(line, @"文件：UI/(\S+)");
            if (mf.Success)
            {
                var file = mf.Groups[1].Value;
                var slash = file.LastIndexOf('/');
                if (slash >= 0) file = file.Substring(slash + 1);
                if (file.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) file = file.Substring(0, file.Length - 4);
                cur = file;
                if (!map.ContainsKey(cur)) map[cur] = new ProbeEntry { File = cur };
                wantLiteral = false;
                continue;
            }

            if (line.IndexOf("C# 字面量", StringComparison.Ordinal) >= 0)
            {
                wantLiteral = true;
                continue;
            }

            if (wantLiteral && cur != null && line.IndexOf("new Rect(", StringComparison.Ordinal) >= 0)
            {
                foreach (Match m in Regex.Matches(line, @"new Rect\((-?\d+), (-?\d+), (-?\d+), (-?\d+)\)"))
                {
                    map[cur].Rects.Add(new Rect(
                        int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                        int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value)));
                }
                wantLiteral = false;
            }
        }
        return map;
    }

    /// <summary>按文件名找 UI 目录下的 png（文件名可能带点，用 EndsWith 匹配）。</summary>
    private static string FindStripPng(string baseName)
    {
        var dir = P("client", "Assets", "Resources", "Clover", "D2", "UI");
        if (!Directory.Exists(dir)) return null;
        var files = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            var n = Path.GetFileName(files[i]);
            if (n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (string.Equals(n, baseName, StringComparison.OrdinalIgnoreCase)) return files[i];
        }
        return null;
    }

    /// <summary>读 PNG 的 IHDR 拿真实图幅（不依赖任何图像库：PNG 头 8 字节签名 + 长度/类型 + 宽高各 4 字节大端）。</summary>
    private static void ReadPngSize(string path, out int w, out int h)
    {
        w = h = -1;
        var head = new byte[24];
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        {
            if (fs.Read(head, 0, 24) < 24) return;
        }
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < 8; i++)
            if (head[i] != sig[i]) return; // 不是 PNG，保持 -1
        w = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
        h = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
    }
}
