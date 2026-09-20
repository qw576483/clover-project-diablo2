// ─────────────────────────────────────────────────────────────────────────────
// Core 契约自检宿主（`docs/agents/agent-13-修复轮.md` §C 的验收项逐条自证）
//
// 运行：dotnet run --project <项目根>/tools/corecheck/CoreCheck.csproj -c Release
//
// 覆盖的验收项（§C 的 4 项 + 契约冻结回归）：
//   ① 5 个多帧条带常量 → **真实磁盘文件**（逐个存在性断言 = `Test-Path` 的等价物）；
//      帧数与本项目 `Assets/Editor/AssetImporter.cs` 的 `MultiFrameStrips` 表**逐条交叉核对**
//      （常量表与切分表必须一致，否则 UI 会按下标取到空图且不报错）。
//   ② `ResPaths.Frame(path, i)` 的命名口径 == `AssetImporter.BuildStripRects` 的 `{文件名}_{帧号}`。
//   ③ `GameConst` 新增常量：`SettingKeyBgmMute` / `SettingKeySfxMute` / `UiReferenceWidth|Height`(1920×1080)。
//   ④ `Log` 降频入口在**离线宿主**（非 Unity 进程）不再抛 `SecurityException`：
//      新旧两种用法都能编过并工作；注入时钟后限频行为可复现（确定性）。
//   ⑤ 回归：`docs/步骤文档.md` §3.5 的**冻结项**（Root / UiPanels / D2Ui / … / D2Bgm）字面值未改。
//
// ⛔ 它只证明「类型 / 常量 / 离线可用性」，**不替代进 Play 实测**（闸门 2：用户尚未打开编辑器）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using CloverEngine;
using Diablo2.Core;
using ILogger = CloverEngine.ILogger;

internal static class CoreCheckProgram
{
    private const string ProjectRoot = @"C:\Work\Server\full-dev\clover-project-diablo2";
    private const string ClientAssets = ProjectRoot + @"\client\Assets";
    private const string ResourcesRoot = ClientAssets + @"\Resources\Clover\";
    private const string ImporterPath = ClientAssets + @"\Editor\AssetImporter.cs";

    private static int _fail;
    private static CountingLogger _log;

    private static void Main()
    {
        Console.WriteLine("================ CoreCheck 开始（agent-13 §C：ResPaths / GameConst / Log）================");
        Console.WriteLine($"项目根 = {ProjectRoot}");
        Console.WriteLine($"宿主进程 = 非 Unity（.NET {Environment.Version}）⇒ `Time.realtimeSinceStartup` 预期不可用，正是本宿主要验的场景");
        Console.WriteLine();

        // 换掉门面日志：把每条日志留档，供"只报一次/降级只报一次"类断言取证。
        _log = new CountingLogger();
        Game.Logger = _log;

        Section0_ContractFreeze();
        Section1_StripConstantsOnDisk();
        Section2_FrameHelperNaming();
        Section3_GameConstNewEntries();
        Section4_LogOfflineNoThrow();
        Section5_LogClockDeterminism();

        Console.WriteLine();
        Console.WriteLine(_fail == 0
            ? "================ CoreCheck 结束：全部通过 ================"
            : $"================ CoreCheck 结束：{_fail} 项失败 ================");
        if (_fail != 0) Environment.ExitCode = 1;
    }

    // ── 0. 契约冻结回归：§3.5 的字面值一个字符都不许动 ────────────────────────
    private static void Section0_ContractFreeze()
    {
        Section("0. 契约冻结回归（`docs/步骤文档.md` §3.5：这些值**不许改**）");

        var frozen = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Root", ResPaths.Root),
            new KeyValuePair<string, string>("UiPanels", ResPaths.UiPanels),
            new KeyValuePair<string, string>("D2Ui", ResPaths.D2Ui),
            new KeyValuePair<string, string>("D2Fonts", ResPaths.D2Fonts),
            new KeyValuePair<string, string>("D2Tiles", ResPaths.D2Tiles),
            new KeyValuePair<string, string>("D2Objects", ResPaths.D2Objects),
            new KeyValuePair<string, string>("D2Chars", ResPaths.D2Chars),
            new KeyValuePair<string, string>("D2Monsters", ResPaths.D2Monsters),
            new KeyValuePair<string, string>("D2Items", ResPaths.D2Items),
            new KeyValuePair<string, string>("D2Sfx", ResPaths.D2Sfx),
            new KeyValuePair<string, string>("D2Bgm", ResPaths.D2Bgm),
        };
        var expected = new List<string>
        {
            "Clover", "UI/", "D2/UI/", "D2/Fonts/", "D2/Tiles/", "D2/Objects/",
            "D2/Chars/{class}/", "D2/Monsters/{name}/", "D2/Items/", "Sound/SFX/", "Sound/BGM/",
        };
        var bad = new List<string>();
        for (int i = 0; i < frozen.Count; i++)
        {
            Console.WriteLine($"    ResPaths.{frozen[i].Key,-12} = \"{frozen[i].Value}\"");
            if (!string.Equals(frozen[i].Value, expected[i], StringComparison.Ordinal))
                bad.Add($"{frozen[i].Key} 期望 \"{expected[i]}\" 实得 \"{frozen[i].Value}\"");
        }
        Check("§3.5 的 11 个冻结常量逐字未改", bad.Count == 0,
            bad.Count == 0 ? "全部一致" : string.Join("；", bad.ToArray()));
        Console.WriteLine();
    }

    // ── 1. 5 个条带常量 → 真实磁盘文件 + 帧数交叉核对 ────────────────────────
    private sealed class StripFact
    {
        public string Label;      // 常量名（回报里要列的东西）
        public string Const;      // 常量值（Resources 相对路径，不含扩展名）
        public string FileName;   // 文件名（= AssetImporter 的 StripSpec.FileName 口径）
        public int Frames;        // 帧数常量值
        public int ExpectedFrames;
        public string Disk;       // 磁盘绝对路径（.png）
    }

    private static void Section1_StripConstantsOnDisk()
    {
        Section("1. 5 个多帧条带：常量 → 真实磁盘文件（逐个存在性断言）+ 帧数交叉核对");

        var strips = new List<StripFact>
        {
            NewStrip("PanelBuySellButton", ResPaths.PanelBuySellButton, ResPaths.FrameCountBuySellButton, 22),
            NewStrip("PanelGoldCoinButton", ResPaths.PanelGoldCoinButton, ResPaths.FrameCountGoldCoinButton, 2),
            NewStrip("PanelOverlap", ResPaths.PanelOverlap, ResPaths.FrameCountOverlap, 2),
            NewStrip("MenuButtonMedium", ResPaths.MenuButtonMedium, ResPaths.FrameCountMenuButtonMedium, 3),
            NewStrip("MenuButtonWide", ResPaths.MenuButtonWide, ResPaths.FrameCountMenuButtonWide, 3),
        };

        // ① 常量值必须与磁盘文件名逐字相符（Resources.Load 不做模糊匹配）
        foreach (var s in strips)
        {
            var exists = File.Exists(s.Disk);
            Console.WriteLine($"    ResPaths.{s.Label,-20} = \"{s.Const}\"");
            Console.WriteLine($"      → 磁盘 {s.Disk}");
            Console.WriteLine($"      → {(exists ? "存在，" + new FileInfo(s.Disk).Length + " bytes" : "**不存在**")}");
            Check($"ResPaths.{s.Label} 指向真实存在的磁盘文件", exists, s.Disk);
        }
        Console.WriteLine();

        // ② 帧数常量 == 实测期望
        foreach (var s in strips)
            Check($"ResPaths.{s.Label} 的帧数常量 = {s.Frames}（实测期望 {s.ExpectedFrames}）",
                s.Frames == s.ExpectedFrames, $"FrameCount = {s.Frames}");
        Console.WriteLine();

        // ③ 与切分表逐条交叉核对：ResPaths 的帧数必须 == AssetImporter.MultiFrameStrips 的 Rect 数
        //    （两处不一致 = UI 按下标取帧会取到空图，且**不报任何错**）
        var table = ParseImporterFrames();
        Check("能在 AssetImporter.cs 里定位 `MultiFrameStrips` 数组字面量（否则计数会跑偏到别处的 `new Rect`）",
            table.RegionFound, table.Note);
        Check($"解析出的切分表条带数 = {table.Order.Count}（期望 5）", table.Order.Count == 5,
            "条带：" + string.Join(", ", table.Order.ToArray()));
        foreach (var s in strips)
        {
            int n;
            var found = table.Frames.TryGetValue(s.FileName, out n);
            Check($"切分表里的 \"{s.FileName}\" 帧数 {n} == ResPaths.FrameCount* = {s.Frames}",
                found && n == s.Frames, found ? $"table={n}" : "切分表里**找不到**该文件名");
        }
        Console.WriteLine();
    }

    private static StripFact NewStrip(string label, string path, int frames, int expectedFrames)
    {
        var fileName = path.Substring(path.LastIndexOf('/') + 1);
        return new StripFact
        {
            Label = label,
            Const = path,
            FileName = fileName,
            Frames = frames,
            ExpectedFrames = expectedFrames,
            Disk = ResourcesRoot + path.Replace('/', '\\') + ".png",
        };
    }

    /// <summary>`AssetImporter.MultiFrameStrips` 的解析结果（`文件名 → 帧矩形数`）。</summary>
    private sealed class ImporterTable
    {
        public bool RegionFound;
        public string Note;
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, int> Frames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 从 `AssetImporter.cs` 源码解析 `MultiFrameStrips`：`文件名 → 帧矩形数`。
    /// <para>**只在数组字面量区域内计数**（`StripSpec[] MultiFrameStrips` → 第一个 `};`）——
    /// 否则最后一个条带的"块"会一直延伸到文件尾，把 `BuildFontRects` 里的 `new Rect(` 也算进来
    /// （本宿主第一版就是这么错的：`button_wide` 被数成 4 帧）。</para>
    /// </summary>
    private static ImporterTable ParseImporterFrames()
    {
        var table = new ImporterTable();
        if (!File.Exists(ImporterPath))
        {
            Log.Error("D2", $"CoreCheck 找不到 {ImporterPath} ⇒ 无法交叉核对切分表（路径写错了？）");
            table.Note = "**文件不存在**：" + ImporterPath;
            return table;
        }

        var src = File.ReadAllText(ImporterPath);
        var decl = src.IndexOf("StripSpec[] MultiFrameStrips", StringComparison.Ordinal);
        if (decl < 0)
        {
            Log.Error("D2", "CoreCheck 在 AssetImporter.cs 里找不到 `StripSpec[] MultiFrameStrips` 声明 ⇒ 交叉核对无法进行");
            table.Note = "**找不到数组声明**（AssetImporter.cs 被改过？）";
            return table;
        }

        var close = src.IndexOf("};", decl, StringComparison.Ordinal);
        if (close <= decl)
        {
            Log.Error("D2", "CoreCheck 找不到 MultiFrameStrips 数组的结束 `};` ⇒ 交叉核对无法进行");
            table.Note = "**找不到数组结束 `};`**";
            return table;
        }

        table.RegionFound = true;
        var region = src.Substring(decl, close - decl);
        var m = Regex.Matches(region, "new StripSpec\\(\"([^\"]+)\"");
        for (int i = 0; i < m.Count; i++)
        {
            var name = m[i].Groups[1].Value;
            var start = m[i].Index;
            var end = i + 1 < m.Count ? m[i + 1].Index : region.Length;
            var block = region.Substring(start, end - start);
            table.Frames[name] = Regex.Matches(block, "new Rect\\(").Count;
            table.Order.Add(name);
        }
        table.Note = $"数组区域 {decl}..{close}（长度 {region.Length}），解析到 {table.Order.Count} 个条带";
        return table;
    }

    // ── 2. 帧名助手口径 ─────────────────────────────────────────────────────
    private static void Section2_FrameHelperNaming()
    {
        Section("2. 帧名助手 Frame() 与 AssetImporter 的命名口径一致（`{文件名}_{帧号}`，帧号从 0 起）");

        var f0 = ResPaths.Frame(ResPaths.PanelBuySellButton, 0);
        var f21 = ResPaths.Frame(ResPaths.PanelBuySellButton, 21);
        var fGold = ResPaths.Frame(ResPaths.PanelGoldCoinButton, 1);
        Console.WriteLine($"    Frame(PanelBuySellButton, 0)  = \"{f0}\"");
        Console.WriteLine($"    Frame(PanelBuySellButton, 21) = \"{f21}\"（末帧，下标 = FrameCount-1）");
        Console.WriteLine($"    Frame(PanelGoldCoinButton, 1) = \"{fGold}\"");

        Check("Frame(path, i) == path + \"_\" + i（帧号从 0 起）",
            f0 == ResPaths.PanelBuySellButton + "_0" && f21 == ResPaths.PanelBuySellButton + "_21",
            f0 + " / " + f21);

        // 与切分表的口径对齐（源码级证据：`name = spec.FileName + "_" + i`）
        var src = File.Exists(ImporterPath) ? File.ReadAllText(ImporterPath) : string.Empty;
        Check("AssetImporter 的子资源命名确实是 `spec.FileName + \"_\" + i`（Frame 的口径来源）",
            src.Contains("spec.FileName + \"_\" + i"),
            "见 AssetImporter.cs 的 BuildStripRects（Multiple 切分）");

        // 非预期分支（负帧号）必须留日志，且**返回值保持忠实拼接**（不偷偷换成第 0 帧）
        _log.Clear();
        var bad = ResPaths.Frame(ResPaths.PanelOverlap, -1);
        Check("负帧号：返回值忠实拼接（不静默换成第 0 帧）", bad == ResPaths.PanelOverlap + "_-1", bad);
        Check("负帧号：留下一条 Warn（参数校验失败不静默）", _log.Count("WARN ", "ResPaths.Frame 参数不合法") == 1,
            "Warn 条数 = " + _log.Count("WARN ", "ResPaths.Frame 参数不合法"));

        // 只报一次：再来 100 次不该再刷屏
        for (int i = 0; i < 100; i++) ResPaths.Frame(null, -5);
        Check("负帧号重复 100 次仍只报 1 条（WarnOnce 不刷屏）",
            _log.Count("WARN ", "ResPaths.Frame 参数不合法") == 1,
            "Warn 条数 = " + _log.Count("WARN ", "ResPaths.Frame 参数不合法"));
        Console.WriteLine();
    }

    // ── 3. GameConst 新增常量 ───────────────────────────────────────────────
    private static void Section3_GameConstNewEntries()
    {
        Section("3. GameConst 新增常量（agent-11 静音持久化用的两个键 + UI 参考分辨率改正）");

        Console.WriteLine($"    SettingKeyBgmVolume = \"{GameConst.SettingKeyBgmVolume}\"（既有）");
        Console.WriteLine($"    SettingKeyBgmMute   = \"{GameConst.SettingKeyBgmMute}\"（新增）");
        Console.WriteLine($"    SettingKeySfxVolume = \"{GameConst.SettingKeySfxVolume}\"（既有）");
        Console.WriteLine($"    SettingKeySfxMute   = \"{GameConst.SettingKeySfxMute}\"（新增）");
        Console.WriteLine($"    UiReferenceWidth    = {GameConst.UiReferenceWidth}（原 1280 → 改 1920）");
        Console.WriteLine($"    UiReferenceHeight   = {GameConst.UiReferenceHeight}（原  720 → 改 1080）");

        Check("SettingKeyBgmMute 存在且与既有音量键同前缀口径（audio/…_mute）",
            GameConst.SettingKeyBgmMute == "audio/bgm_mute", GameConst.SettingKeyBgmMute);
        Check("SettingKeySfxMute 存在且与既有音量键同前缀口径（audio/…_mute）",
            GameConst.SettingKeySfxMute == "audio/sfx_mute", GameConst.SettingKeySfxMute);
        Check("两个静音键**不等于**音量键（静音与音量为两个维度，互不覆盖）",
            GameConst.SettingKeyBgmMute != GameConst.SettingKeyBgmVolume
            && GameConst.SettingKeySfxMute != GameConst.SettingKeySfxVolume, "四键互异");

        // 引擎出处必须是钉死的 1920×1080（否则面板会整体错位）
        var engineUi = @"C:\Work\Server\full-dev\clover-client-unity-engine\Runtime\Presentation\UI.cs";
        Check("UiReferenceWidth/Height = 1920/1080（引擎 `Runtime/Presentation/UI.cs:52-56` 写死的值）",
            GameConst.UiReferenceWidth == 1920 && GameConst.UiReferenceHeight == 1080,
            $"{GameConst.UiReferenceWidth}x{GameConst.UiReferenceHeight}");
        if (File.Exists(engineUi))
        {
            var line = File.ReadAllText(engineUi);
            Check("引擎源码里确实是 `referenceResolution = new Vector2(1920f, 1080f)`（注释里的出处可复查）",
                line.Contains("new Vector2(1920f, 1080f)"), engineUi + ":52-56");
        }
        else
        {
            Console.WriteLine($"    ⚠️ 引擎源码不在本机预期路径（{engineUi}）⇒ 跳过「出处可复查」这一条（不算失败）");
        }
        Console.WriteLine();
    }

    // ── 4. Log 在离线宿主不再抛异常（新旧两种用法都要能编过且工作）───────────
    private static void Section4_LogOfflineNoThrow()
    {
        Section("4. Log 降频入口在离线宿主（非 Unity 进程）**不再抛异常**（§C 的核心要求）");

        Console.WriteLine($"    （探针）直接调 `Time.realtimeSinceStartup`…");
        var unityOk = true;
        string unityErr = null;
        try
        {
            var t = UnityEngine.Time.realtimeSinceStartup;
            Console.WriteLine($"      → 居然可用：{t}（本宿主不是纯离线环境？）");
        }
        catch (Exception ex)
        {
            unityOk = false;
            unityErr = ex.GetType().Name + ": " + ex.Message;
            Console.WriteLine($"      → 不可用：{unityErr}");
        }
        Check("本宿主确实是「Unity 原生 API 不可用」的离线环境（否则下面几条没有意义）", !unityOk,
            unityOk ? "原生 API 意外可用 ⇒ 本宿主不该用来验离线降级" : unityErr);
        Console.WriteLine();

        Console.WriteLine($"    调用前：Log.ClockSource = \"{Log.ClockSource}\" / UnityClockUnavailable = {Log.UnityClockUnavailable}");

        // 新用法 ①：WarnOnce —— 以前这里必抛 SecurityException
        var threw = (string)null;
        var r1 = false;
        try { r1 = Log.WarnOnce("D2", "corecheck.once", "CoreCheck：WarnOnce 在离线宿主里的第一次调用"); }
        catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }

        Check("新用法：Log.WarnOnce(tag,key,msg) 在离线宿主**未抛异常**", threw == null, threw ?? "未抛");
        Check("新用法：首次调用返回 true（真的输出了）", r1, "returns=" + r1);
        Check("自动降级已生效：UnityClockUnavailable = true（证明真的探测过 Unity 时钟并接住了异常）",
            Log.UnityClockUnavailable, "UnityClockUnavailable=" + Log.UnityClockUnavailable);
        Console.WriteLine($"    调用后：Log.ClockSource = \"{Log.ClockSource}\"");

        // 只报一次语义在离线宿主同样成立
        var r2 = Log.WarnOnce("D2", "corecheck.once", "CoreCheck：同 key 第二次（应被闸门拦下）");
        Check("WarnOnce 语义：同 key 第二次返回 false（只报一次，不刷屏）", !r2, "returns=" + r2);

        // 降级只报一条（不刷屏）。
        // ★ 口径收紧（agent-32「引擎下沉 B1b」）：**不能只按正文「Unity 时钟不可用」计数** ——
        //   项目 `Log` 的限频与时钟已下沉到引擎 `CloverEngine.LogThrottle`，那条降级告警现在由
        //   `LogThrottle` 自己发出（tag = `[LogThrottle]`，正文与项目侧同源 ⇒ 含同样字样）。
        //   只认正文时阈值会被「哪一层先探测」影响：corecheck 将来若先真正调用引擎 `Rng` /
        //   `LogThrottle`（它们一被调用就可能触发降级），或该告警早于 `Game.Logger = _log` 安装而被丢
        //   （引擎侧走 `Game.Logger?.Warn`，null 时静默丢弃）⇒ 计数变 0/2 而**误红**。
        //   现在锚定到「**引擎 `LogThrottle` 那一条**」：级别 WARN + tag `[LogThrottle]` + 降级字样，
        //   与调用层、调用次数无关；项目侧 `Log` 已不再自己发这条（转发给同一个 `LogThrottle`）。
        Check("时钟降级告警只有引擎 `LogThrottle` 那一条（tag=[LogThrottle]，不随调用层/次数刷屏）",
            _log.Count("WARN ", "[LogThrottle] Unity 时钟不可用") == 1,
            "Warn 条数 = " + _log.Count("WARN ", "[LogThrottle] Unity 时钟不可用"));
        Console.WriteLine();

        // 新用法 ②：WarnThrottled / ErrorThrottled / ErrorOnce（4 参 + 3 参两种形态都编过）
        threw = null;
        try
        {
            Log.WarnThrottled("D2", "corecheck.thr", "CoreCheck：WarnThrottled 4 参形态");
            Log.WarnThrottled("D2", "corecheck.thr2", "CoreCheck：WarnThrottled 3 参形态（走默认 5 秒）");
            Log.ErrorThrottled("D2", "corecheck.errthr", "CoreCheck：ErrorThrottled");
            Log.ErrorOnce("D2", "corecheck.erronce", "CoreCheck：ErrorOnce");
            Log.Info("D2", "CoreCheck：Log.Info");
            Log.Warn("D2", "CoreCheck：Log.Warn");
            Log.Error("D2", "CoreCheck：Log.Error");
            Log.Debug("D2", "CoreCheck：Log.Debug");
        }
        catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }

        Check("新用法：WarnThrottled / ErrorThrottled / ErrorOnce（4 参与 3 参形态）+ Info/Warn/Error/Debug 全部未抛异常",
            threw == null, threw ?? "全部未抛");

        // 旧用法（签名未变 ⇒ 向后兼容）：`ShouldLog` / `ResetThrottle` / `IsKnownTag` 的原有形态
        threw = null;
        var gate = false;
        try
        {
            Log.ResetThrottle();
            gate = Log.ShouldLog("corecheck.gate");            // 旧用法：单参
            var gate2 = Log.ShouldLog("corecheck.gate", 5f);   // 旧用法：双参
            var empty = Log.ShouldLog(string.Empty);           // 空 key ⇒ 按不允许（且只报一次）
            Check("旧用法：ShouldLog 首调 true、同 key 紧接 false（限频闸门语义未变）",
                gate && !gate2 && !empty, $"gate={gate} gate2={gate2} emptyKey={empty}");
            Check("旧用法：Log.IsKnownTag(\"D2\") = true（白名单未变）", Log.IsKnownTag("D2"), "IsKnownTag(D2)");
            Log.ResetThrottle();
        }
        catch (Exception ex) { threw = ex.GetType().Name + ": " + ex.Message; }
        Check("旧用法：ShouldLog / ResetThrottle / IsKnownTag 未抛异常（向后兼容）", threw == null, threw ?? "未抛");
        Console.WriteLine();
    }

    // ── 5. 可注入时钟：限频行为可复现 ────────────────────────────────────────
    private static void Section5_LogClockDeterminism()
    {
        Section("5. 可注入时钟：注入后限频行为**确定可复现**（离线宿主请注入时钟）");

        var fake = 0f;
        Log.Clock = () => fake;
        Console.WriteLine($"    注入 `Log.Clock = () => fake` ⇒ ClockSource = \"{Log.ClockSource}\"");
        Check("注入后 ClockSource = \"Injected\"（覆盖了 Unity/降级时钟）", Log.ClockSource == "Injected", Log.ClockSource);

        Log.ResetThrottle();

        var a = Log.WarnThrottled("D2", "corecheck.inj", "t=0 第一次", 5f);
        fake = 1f;
        var b = Log.WarnThrottled("D2", "corecheck.inj", "t=1（1s < 5s ⇒ 拦下）", 5f);
        fake = 4.99f;
        var c = Log.WarnThrottled("D2", "corecheck.inj", "t=4.99（仍 < 5s ⇒ 拦下）", 5f);
        fake = 5f;
        var d = Log.WarnThrottled("D2", "corecheck.inj", "t=5（== 5s ⇒ 放行）", 5f);
        fake = 6f;
        var e = Log.WarnThrottled("D2", "corecheck.inj", "t=6（1s < 5s ⇒ 拦下）", 5f);

        Console.WriteLine($"    t=0 → {a} / t=1 → {b} / t=4.99 → {c} / t=5 → {d} / t=6 → {e}");
        Check("限频语义完全由注入时钟决定（true/false/false/true/false ⇒ 时间可控、可复现）",
            a && !b && !c && d && !e, $"{a}/{b}/{c}/{d}/{e}");

        // 注入时钟后不再触碰 Unity 原生 API ⇒ 注入本身也是"离线友好"的证明
        Log.Clock = null;
        Console.WriteLine($"    还原 `Log.Clock = null` ⇒ ClockSource = \"{Log.ClockSource}\"（已探测过 ⇒ 不再回头调 Unity）");
        Check("还原注入后仍是可用状态（不会因还原而重新踩 Unity 原生 API）",
            Log.ClockSource == "Process" && Log.UnityClockUnavailable, Log.ClockSource);

        Log.ResetThrottle();
        Console.WriteLine();
    }

    // ── 工具 ────────────────────────────────────────────────────────────────
    private static void Section(string title)
    {
        Console.WriteLine("── " + title + " " + new string('─', Math.Max(0, 100 - title.Length)));
    }

    private static void Check(string what, bool ok, string detail)
    {
        if (!ok) _fail++;
        Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
    }

    /// <summary>计数日志（`Game.Logger` 替身）：留档每条日志，供"只报一次"类断言取证。</summary>
    private sealed class CountingLogger : ILogger
    {
        private readonly List<string> _lines = new List<string>();

        public void Info(string tag, string msg) => Add("INFO ", tag, msg);
        public void Warn(string tag, string msg) => Add("WARN ", tag, msg);
        public void Error(string tag, string msg, Exception ex = null) => Add("ERROR", tag, msg);
        public void Debug(string tag, string msg) => Add("DEBUG", tag, msg);
        public void Fatal(string tag, string msg, Exception ex = null) => Add("FATAL", tag, msg);

        private void Add(string level, string tag, string msg)
        {
            _lines.Add("[" + level + "] [" + tag + "] " + msg);
            Console.WriteLine("      [LOG] [" + level + "] [" + tag + "] " + msg);
        }

        /// <summary>统计级别前缀 + 正文包含指定片段的条数。</summary>
        public int Count(string levelPrefix, string msgContains)
        {
            var n = 0;
            foreach (var l in _lines)
            {
                if (l.StartsWith("[" + levelPrefix, StringComparison.Ordinal) && l.Contains(msgContains)) n++;
            }
            return n;
        }

        public void Clear() => _lines.Clear();
    }
}
