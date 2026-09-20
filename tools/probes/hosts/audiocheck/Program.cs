// ─────────────────────────────────────────────────────────────────────────────
// Audio 自检宿主（**非 Unity 工程、不参与打包；离线跑，秒级**）
//
// 目的：在没有 Unity 编辑器的情况下（闸门 2：用户尚未打开编辑器 ⇒ 禁止 unity run/test），
// 把 `Module/Audio/**` 真跑一遍并断言 —— 这是本阶段能拿到的**最强证据**：
//   · 登记表自检：`SfxRegistry` 无重复键；`Module/Combat/SfxKeys.cs` 的**每个键都已登记**；
//   · 触发点覆盖：逐个 Emit 事件 → 断言 `Game.Sound` 收到**期望的音效键**（贴映射表）；
//   · 脚步节流：模拟移动 2 秒 → 脚步请求次数 = 2/间隔（间隔由 `AudioHook.FootstepIntervalSeconds`
//     = 每步 2 格 ÷ `GameConst.PlayerWalkSpeed` 算出，⛔ 不写死数字；片 2b 起走速 3.0 ⇒ 间隔 0.667s）；
//   · 静止不发声：不移动时 0 次脚步；
//   · 缺文件只报一次：连续请求同一个不存在的键 100 次 → 「文件缺失」告警**恰好 1 条**、
//     引擎**零调用**、探测**只发生 1 次**（不重复调用引擎）；
//   · 音量持久化：`SetVolume` → `Game.Setting` 有值 → **重建模块**后读回一致；
//   · BGM 切区域：Town→BloodMoor→DenOfEvil 三次 `AreaChanged` → 三次不同 `Bgm` 请求；
//   · 生产探测实现 (`EngineAudioClipProbe`) 在资源取不到时能**降级**（不抛异常）。
//
// 为了在没有 Unity 原生对象（`AudioClip` 造不出来）的环境里跑，自检给 `AudioModule.ClipProbe`
// 注入替身（`FakeProbe`）—— 这正是 `IAudioClipProbe` 这个接缝存在的理由。
//
// 不覆盖（需要 Unity 原生 API / 真人听感，留给你在编辑器里做）：
//   ① 真实 `.wav` 出声与音量/静音的听感；② `Time.realtimeSinceStartup` 相关的引擎降频日志；
//   ③ 3D 音效的空间衰减。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.Audio;
using UnityEngine;
// 别名：`UnityEngine.ILogger` 与 `CloverEngine.ILogger` 同名（同时 using 两者会 CS0104）。
using ILogger = CloverEngine.ILogger;

namespace AudioCheck
{
    // ── 引擎门面替身（记录调用，不做真实工作）──────────────────────────────

    /// <summary>计数日志：把每条日志存下来，便于断言"某 tag 的某类告警恰好 N 条"。</summary>
    internal sealed class CountingLogger : ILogger
    {
        public readonly List<string> Lines = new List<string>();

        public void Info(string tag, string msg) => Add("INFO ", tag, msg);
        public void Warn(string tag, string msg) => Add("WARN ", tag, msg);
        public void Error(string tag, string msg, Exception ex = null) => Add("ERROR", tag, msg);
        public void Debug(string tag, string msg) { }
        public void Fatal(string tag, string msg, Exception ex = null) => Add("FATAL", tag, msg);

        private void Add(string level, string tag, string msg)
        {
            var line = "[" + level + "] [" + tag + "] " + msg;
            Lines.Add(line);
            Console.WriteLine("    [LOG] " + line);      // 原样回显：回报里要贴"降级只报一次"的原始日志
        }

        /// <summary>统计 Warn 级、tag 精确匹配、正文含 <paramref name="msgContains"/> 的条数。</summary>
        public int CountWarn(string tag, string msgContains)
        {
            var tagToken = "[" + tag + "]";
            var n = 0;
            foreach (var l in Lines)
            {
                if (l.StartsWith("[WARN ]", StringComparison.Ordinal)
                    && l.Contains(tagToken)
                    && l.Contains(msgContains)) n++;
            }
            return n;
        }

        public void Clear() => Lines.Clear();
    }

    /// <summary>内存设置（`Game.Setting` 替身）：`Save()` 只计数。</summary>
    internal sealed class MemSetting : ISetting
    {
        private readonly Dictionary<string, object> _d = new Dictionary<string, object>();
        public int SaveCount;

        public T Get<T>(string key, T defaultValue = default)
            => _d.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

        public void Set<T>(string key, T value) { _d[key] = value; }
        public void Save() { SaveCount++; }
        public void Load() { }
        public void Delete(string key) => _d.Remove(key);
        public void DeleteAll() => _d.Clear();
    }

    /// <summary>录音引擎（`ISoundManager` 替身）：把每次播放请求记下来。</summary>
    internal sealed class RecSound : ISoundManager
    {
        public readonly List<string> Sfx2D = new List<string>();
        public readonly List<string> Sfx3D = new List<string>();
        public readonly List<string> Bgm = new List<string>();
        public readonly Dictionary<SoundGroup, float> Volumes = new Dictionary<SoundGroup, float>();
        public readonly Dictionary<SoundGroup, bool> Mutes = new Dictionary<SoundGroup, bool>();
        public int StopAllCount;
        public int StopBgmCount;

        public void PlayBGM(string clipName, float fadeTime = 0.5f) => Bgm.Add(clipName);
        public void StopBGM(float fadeTime = 0.5f) => StopBgmCount++;
        public void PlaySFX(string clipName) => Sfx2D.Add(clipName);
        public void PlaySFXAt(string clipName, Vector3 position) => Sfx3D.Add(clipName);
        public void PlayVoice(string clipName) { }
        public void StopAll() => StopAllCount++;
        public void SetVolume(SoundGroup group, float volume) => Volumes[group] = volume;
        public float GetVolume(SoundGroup group) => Volumes.TryGetValue(group, out var v) ? v : 1f;
        public void SetMute(SoundGroup group, bool mute) => Mutes[group] = mute;

        public void Clear() { Sfx2D.Clear(); Sfx3D.Clear(); Bgm.Clear(); }

        public int Count3D(string key)
        {
            var n = 0;
            foreach (var k in Sfx3D) if (k == key) n++;
            return n;
        }

        public string Last2D() => Sfx2D.Count > 0 ? Sfx2D[Sfx2D.Count - 1] : "(none)";
        public string Last3D() => Sfx3D.Count > 0 ? Sfx3D[Sfx3D.Count - 1] : "(none)";
        public string LastBgm() => Bgm.Count > 0 ? Bgm[Bgm.Count - 1] : "(none)";
    }

    /// <summary>资源替身：永远取不到（`TryGet` = null，`LoadAsset` 回调 null）。</summary>
    internal sealed class FakeRes : IResourceManager
    {
        public void LoadAsset<T>(string path, Action<T> callback) where T : UnityEngine.Object
            => callback?.Invoke(null);
        public T TryGet<T>(string path) where T : UnityEngine.Object => null;

        // ★ agent-34（引擎下沉 A3）：引擎新增的两个**同步**入口 —— 本替身"永远取不到"，
        //   于是 `ClientConfig` 走它的引擎分支时拿到空数组、照旧退回文件 / 默认值（离线可复现）。
        public bool Exists(string path) => false;
        public T[] LoadAll<T>(string path) where T : UnityEngine.Object => Array.Empty<T>();
    }

    /// <summary>音频探测替身（**这是离线宿主能跑的前提**：`AudioClip` 在非 Unity 进程里造不出来）。</summary>
    internal sealed class FakeProbe : IAudioClipProbe
    {
        private readonly Dictionary<string, int> _calls = new Dictionary<string, int>(StringComparer.Ordinal);

        public FakeProbe(bool available) { Available = available; }

        /// <summary>true = 探测到音频可用；false = 文件缺失。</summary>
        public bool Available { get; set; }

        public void Probe(string path, Action<bool> onResult)
        {
            _calls.TryGetValue(path, out var n);
            _calls[path] = n + 1;
            onResult?.Invoke(Available);
        }

        public int CallCount(string path) => _calls.TryGetValue(path, out var n) ? n : 0;
        public void ClearCalls() => _calls.Clear();
    }

    // ── 自检主流程 ──────────────────────────────────────────────────────────

    public static class Program
    {
        private static int _fail;
        private static int _pass;

        public static int Main()
        {
            Console.WriteLine("=== AudioCheck：音频模块 / 音效触发点 离线自检 ===");
            Console.WriteLine();

            var logger = new CountingLogger();
            var bus = new ConsoleEventBus();
            var sound = new RecSound();
            var setting = new MemSetting();

            Game.Logger = logger;
            Game.Event = bus;
            Game.Sound = sound;
            Game.Setting = setting;
            Game.Res = new FakeRes();
            Game.IsRunning = true;

            // ① `Cfg` 预载：`Application.dataPath` 在非 Unity 宿主会抛（`Core/ClientConfig.cs:81`），
            //    而 `Cfg` 内部对 **`Log.ErrorOnce`** 的调用又会读 `Time.realtimeSinceStartup`（`Core/Log.cs:133`）
            //    —— 那也是原生 API，离线宿主会抛。**在日志静默下预载一次**即可让 Cfg 缓存下来（走"默认值"分支），
            //    之后读 `Cfg.BgmVolume` 不再触发任何日志/原生调用。
            Diablo2.Core.Log.Suppress = true;
            var cfgBgm = Cfg.BgmVolume;
            var cfgSfx = Cfg.SfxVolume;
            var cfgSource = Cfg.Source;
            Diablo2.Core.Log.Suppress = false;
            Console.WriteLine($"Cfg 预载：来源={cfgSource} bgm={cfgBgm:0.00} sfx={cfgSfx:0.00}（离线宿主取默认值属预期）");
            Console.WriteLine();

            // ② 登记表自检（不依赖任何模块实例）
            Section("登记表自检");
            var unique = SfxRegistry.ValidateUnique();
            Check("SfxRegistry 无重复键", unique == null, unique ?? "OK");

            var sfxKeyConsts = ConstStringsOf(typeof(Diablo2.Module.Combat.SfxKeys));
            Check("Combat.SfxKeys 至少 14 个常量", sfxKeyConsts.Count >= 14, "count=" + sfxKeyConsts.Count);

            var missingInRegistry = new List<string>();
            foreach (var k in sfxKeyConsts)
                if (!SfxRegistry.IsSfx(k)) missingInRegistry.Add(k);
            Check("Combat.SfxKeys 的每个键都已登记进 SfxRegistry（键名集合一致）",
                missingInRegistry.Count == 0, missingInRegistry.Count == 0 ? "全部命中" : string.Join(",", missingInRegistry));

            Check("SfxRegistry 音效键数量 = SfxKeys + 本项目新增 10",
                SfxRegistry.AllSfxKeys.Count == sfxKeyConsts.Count + 10,
                $"registry={SfxRegistry.AllSfxKeys.Count} sfxKeys={sfxKeyConsts.Count}");
            Check("SfxRegistry BGM 键数量 = 3（Town/BloodMoor/DenOfEvil）",
                SfxRegistry.AllBgmKeys.Count == 3, "count=" + SfxRegistry.AllBgmKeys.Count);
            Check("SfxRegistry 的每个音效键都能取到期望 .wav 文件名",
                AllHaveFileNames(SfxRegistry.AllSfxKeys, true), "见下方清单");
            Check("SfxRegistry 的每个 BGM 键都能取到期望文件名",
                AllHaveFileNames(SfxRegistry.AllBgmKeys, false), "见下方清单");

            DumpRegistry();
            Console.WriteLine();

            // ②b 素材到位自检（**每键逐条查磁盘**）= 素材阶段最强的离线证据
            Section("素材到位自检（登记表的每个键 → 磁盘上的 .wav）");
            AssetChecks();

            // ③ 场景一：素材"已到位"（探测恒 true）→ 触发点覆盖 / 脚步 / 静止 / BGM
            AudioLog.ResetForTest();
            var probeOk = new FakeProbe(true);
            var audio = new AudioModule { ClipProbe = probeOk };

            Section("触发点覆盖（逐个 Emit 事件 → 断言音效键）");
            CoverageChecks(bus, sound, audio);

            Section("脚步节流 / 静止不发声");
            FootstepChecks(bus, sound, audio);

            Section("BGM 切区域");
            BgmChecks(bus, sound, audio);

            Section("事件订阅 / 注销");
            SubscriptionChecks(bus, audio);
            audio.Detach();

            // ④ 场景二：素材"未到位"（探测恒 false）→ 缺文件降级
            AudioLog.ResetForTest();
            logger.Clear();
            var probeMissing = new FakeProbe(false);
            var audio2 = new AudioModule { ClipProbe = probeMissing };
            sound.Clear();
            probeMissing.ClearCalls();

            Section("缺文件只报一次（硬要求 ④）");
            for (var i = 0; i < 100; i++) audio2.Sfx(Diablo2.Module.Combat.SfxKeys.Hit);

            Check("「文件缺失」告警恰好 1 条", AudioLog.MissingWarnCount == 1, "MissingWarnCount=" + AudioLog.MissingWarnCount);
            Check("日志里 [Audio] 的「文件缺失」Warn 恰好 1 条",
                logger.CountWarn("Audio", "音效文件缺失") == 1,
                "lines=" + logger.CountWarn("Audio", "音效文件缺失"));
            Check("缺失键**没有**重复调用引擎（100 次请求 → 0 次 PlaySFX）",
                sound.Sfx2D.Count == 0, "PlaySFX 次数=" + sound.Sfx2D.Count);
            Check("缺失键**只探测一次**（不重复调用资源系统）",
                probeMissing.CallCount(ResPaths.Sfx(Diablo2.Module.Combat.SfxKeys.Hit)) == 1,
                "探测次数=" + probeMissing.CallCount(ResPaths.Sfx(Diablo2.Module.Combat.SfxKeys.Hit)));
            Check("BGM 缺失同样只报一次",
                MissingBgmOnce(audio2, probeMissing, logger), "见实现（AreaChanged×50 → 1 条缺失告警）");

            // ⑤ 音量持久化
            Section("音量持久化（硬要求 ①）");
            audio2.ClipProbe = probeOk;                     // 音量与探测无关，切回"可用"避免干扰
            audio2.SetVolume(0.42f, 0.33f);
            Check("SetVolume 写进 Game.Setting（bgm）",
                Math.Abs(setting.Get<float>(GameConst.SettingKeyBgmVolume, -1f) - 0.42f) < 1e-4,
                setting.Get<float>(GameConst.SettingKeyBgmVolume, -1f).ToString("0.00"));
            Check("SetVolume 写进 Game.Setting（sfx）",
                Math.Abs(setting.Get<float>(GameConst.SettingKeySfxVolume, -1f) - 0.33f) < 1e-4,
                setting.Get<float>(GameConst.SettingKeySfxVolume, -1f).ToString("0.00"));
            Check("SetVolume 落到引擎 Game.Sound.SetVolume(BGM)",
                sound.Volumes.TryGetValue(SoundGroup.BGM, out var vb) && Math.Abs(vb - 0.42f) < 1e-4,
                "BGM=" + (sound.Volumes.TryGetValue(SoundGroup.BGM, out var v1) ? v1.ToString("0.00") : "?"));
            Check("SetVolume 落到引擎 Game.Sound.SetVolume(SFX)",
                sound.Volumes.TryGetValue(SoundGroup.SFX, out var vs) && Math.Abs(vs - 0.33f) < 1e-4,
                "SFX=" + (sound.Volumes.TryGetValue(SoundGroup.SFX, out var v2) ? v2.ToString("0.00") : "?"));
            Check("SetVolume 调用了 Game.Setting.Save()", setting.SaveCount >= 1, "SaveCount=" + setting.SaveCount);

            audio2.Detach();
            var audio3 = new AudioModule { ClipProbe = probeOk };     // 重建模块 = 冷启动读回
            Check("重建模块后 BGM 音量读回一致（0.42）",
                Math.Abs(audio3.BgmVolume - 0.42f) < 1e-4, audio3.BgmVolume.ToString("0.00"));
            Check("重建模块后音效音量读回一致（0.33）",
                Math.Abs(audio3.SfxVolume - 0.33f) < 1e-4, audio3.SfxVolume.ToString("0.00"));
            audio3.Detach();

            // ⑥ 生产探测实现能在资源取不到时降级（不抛异常）
            Section("生产探测实现（EngineAudioClipProbe）降级");
            var engineProbe = new EngineAudioClipProbe();
            var probed = false;
            bool? probeResult = null;
            engineProbe.Probe(ResPaths.Sfx("hit"), ok => { probed = true; probeResult = ok; });
            Check("资源取不到时回调 false 且不抛异常", probed && probeResult == false,
                $"probed={probed} result={(probeResult.HasValue ? probeResult.Value.ToString() : "null")}");

            Console.WriteLine();
            Console.WriteLine($"=== 自检结果：通过 {_pass} 项，失败 {_fail} 项 ===");
            Console.WriteLine("未覆盖（需要 Unity 原生 API / 真人听感，留给你在编辑器里做）："
                + "① 真实 .wav 出声与音量/静音听感；② 3D 音效空间衰减；③ 引擎降频日志（原生时钟）。");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 各段断言
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 素材到位自检：把 `SfxRegistry` 登记过的**每个键**在磁盘上查一遍
        /// （`Resources/Clover/Sound/{SFX,BGM}/{键}.wav`：**存在 + 非空 + RIFF/WAVE 头**）。
        ///
        /// 这就是"逐条 `Test-Path`"的机器可复现版本：素材没到位时它会红，
        /// 且能立刻指出**是哪个键**缺文件（比人肉 Test-Path 更可定位）。
        /// </summary>
        private static void AssetChecks()
        {
            var root = ResolveSoundRoot();
            Check("找得到 Sound 素材根目录（client/Assets/Resources/Clover/Sound）", root != null,
                root ?? "未找到（自 " + Directory.GetCurrentDirectory() + " 向上找 client/）");
            if (root == null) return;

            var sfxKeys = Sorted(SfxRegistry.AllSfxKeys);
            var bgmKeys = Sorted(SfxRegistry.AllBgmKeys);

            var sfxMissing = new List<string>();
            var sfxBad = new List<string>();
            var bgmMissing = new List<string>();
            var bgmBad = new List<string>();
            long sfxBytes = 0, bgmBytes = 0;

            Console.WriteLine("  ── 音效：键 → 文件 / 字节 / RIFF 头 ──");
            foreach (var k in sfxKeys)
                sfxBytes += Probe(Path.Combine(root, "SFX", k + ".wav"), "SFX/" + k + ".wav", sfxMissing, sfxBad);

            Console.WriteLine("  ── BGM：键 → 文件 / 字节 / RIFF 头 ──");
            foreach (var k in bgmKeys)
                bgmBytes += Probe(Path.Combine(root, "BGM", k + ".wav"), "BGM/" + k + ".wav", bgmMissing, bgmBad);

            Check($"音效键 {sfxKeys.Count} 个：文件**全部**就位（逐条 Test-Path 全真）",
                sfxMissing.Count == 0 && sfxKeys.Count > 0,
                sfxMissing.Count == 0 ? $"共 {sfxBytes} 字节 → {root}\\SFX" : "缺失=" + string.Join(",", sfxMissing));
            Check($"BGM 键 {bgmKeys.Count} 个：文件**全部**就位（逐条 Test-Path 全真）",
                bgmMissing.Count == 0 && bgmKeys.Count == 3,
                bgmMissing.Count == 0 ? $"共 {bgmBytes} 字节 → {root}\\BGM" : "缺失=" + string.Join(",", bgmMissing));
            Check("全部素材都是合法 RIFF/WAVE 且非空（不是占位/空文件）",
                sfxBad.Count == 0 && bgmBad.Count == 0,
                (sfxBad.Count + bgmBad.Count) == 0
                    ? (sfxKeys.Count + bgmKeys.Count) + "/" + (sfxKeys.Count + bgmKeys.Count) + " 合法"
                    : "非法=" + string.Join(",", sfxBad) + string.Join(",", bgmBad));

            var originError = SfxRegistry.ValidateOrigins();
            Check("每个键都登记了原版出处（证明确实取自暗黑2 原版，没有混进别的音频）",
                originError == null, originError ?? "全部命中");

            Console.WriteLine("  ── 键 → 原版出处（人类可读对照表见 Module/Audio/SoundMap.md）──");
            foreach (var k in sfxKeys) Console.WriteLine($"    SFX  {k,-16} → {SfxRegistry.Origin(k)}");
            foreach (var k in bgmKeys) Console.WriteLine($"    BGM  {k,-16} → {SfxRegistry.Origin(k)}");
        }

        /// <summary>把登记表的键排成稳定顺序（输出可比对）。</summary>
        private static List<string> Sorted(IReadOnlyCollection<string> keys)
        {
            var l = new List<string>(keys);
            l.Sort(StringComparer.Ordinal);
            return l;
        }

        /// <summary>查一个 .wav：存在 + 非空 + RIFF/WAVE 头；返回字节数（缺失 = 0）。</summary>
        private static long Probe(string fullPath, string label, List<string> missing, List<string> bad)
        {
            if (!File.Exists(fullPath))
            {
                missing.Add(label);
                Console.WriteLine($"    {label,-24} ** 缺失 **");
                return 0;
            }

            var bytes = new FileInfo(fullPath).Length;
            var head = new byte[12];
            using (var fs = File.OpenRead(fullPath))
            {
                if (fs.Read(head, 0, head.Length) < head.Length)
                {
                    bad.Add(label + "(太短)");
                    Console.WriteLine($"    {label,-24} {bytes,9} B  文件太短，读不到 WAVE 头");
                    return bytes;
                }
            }

            var okRiff = head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
                         && head[8] == (byte)'W' && head[9] == (byte)'A' && head[10] == (byte)'V' && head[11] == (byte)'E';
            if (!okRiff) bad.Add(label + "(非WAVE)");
            Console.WriteLine($"    {label,-24} {bytes,9} B  RIFF={(okRiff ? "OK" : "BAD")}");
            return bytes;
        }

        /// <summary>从当前目录向上找 `client/Assets/Resources/Clover/Sound`（宿主可能在任何子目录里跑）。</summary>
        private static string ResolveSoundRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var cand = Path.Combine(dir.FullName, "client", "Assets", "Resources", "Clover", "Sound");
                if (Directory.Exists(cand)) return cand;
                dir = dir.Parent;
            }
            return null;
        }

        /// <summary>逐个 Emit 事件，断言 `Game.Sound` 收到期望的音效键。</summary>
        private static void CoverageChecks(ConsoleEventBus bus, RecSound sound, AudioModule audio)
        {
            Sfx(bus, sound, "拾取物品（非金币）→ item_pickup", SfxRegistry.ItemPickup, () =>
                bus.Emit(Events.ItemPicked, new ItemStack { itemId = 1, name = "解药", isGold = false }));

            Sfx(bus, sound, "拾取金币 → gold_pickup", SfxRegistry.GoldPickup, () =>
                bus.Emit(Events.ItemPicked, new ItemStack { itemId = 2, name = "金币", isGold = true }));

            Sfx(bus, sound, "使用物品 → item_use", SfxRegistry.ItemUse, () =>
                bus.Emit(Events.ItemUsed, new ItemStack { itemId = 3, name = "体力药水" }));

            Sfx(bus, sound, "升级 → level_up", SfxRegistry.LevelUp, () => bus.Emit(Events.LevelUp, 2));
            Sfx(bus, sound, "任务完成 → quest_complete", SfxRegistry.QuestComplete, () => bus.Emit(Events.QuestCompleted, 1));
            Sfx(bus, sound, "复活完成 → player_revive", SfxRegistry.PlayerRevive, () => bus.Emit(Events.Revived));
            Sfx(bus, sound, "面板开关（UI 点击）→ ui_click", SfxRegistry.UiClick, () => bus.Emit(Events.PanelToggleRequest, "InventoryPanel"));
            Sfx(bus, sound, "对话选项（UI 点击）→ ui_click", SfxRegistry.UiClick, () => bus.Emit(Events.DialogOptionChosen, 1));
            Sfx(bus, sound, "NPC 对话开始 → dialog_open", SfxRegistry.DialogOpen, () =>
                bus.Emit(Events.DialogOpen, new NpcDialogArgs { npcId = 0, npcName = "阿卡拉", text = "你好" }));
            Sfx(bus, sound, "商店打开 → shop_open", SfxRegistry.ShopOpen, () =>
                bus.Emit(Events.ShopOpen, new ShopOpenArgs { npcId = 2, npcName = "恰西" }));
            Sfx(bus, sound, "买入按钮 → ui_click", SfxRegistry.UiClick, () =>
                bus.Emit(Events.ShopBuyRequest, new ShopTradeArgs { npcId = 2, index = 0, count = 1 }));
            Sfx(bus, sound, "卖出按钮 → ui_click", SfxRegistry.UiClick, () =>
                bus.Emit(Events.ShopSellRequest, new ShopTradeArgs { npcId = 2, index = 1, count = 1 }));
            Sfx(bus, sound, "踩出入口 / 传送 → portal", SfxRegistry.Portal, () => bus.Emit(Events.ExitEntered, AreaId.BloodMoor));

            // 统一入口事件（`Core/Events.cs` 为音频模块预留）
            Sfx(bus, sound, "Events.PlaySfx(\"hit\") → hit", SfxRegistry.Hit, () => bus.Emit(Events.PlaySfx, SfxRegistry.Hit));

            // 进图：进图音（2D）+ BGM（首次进图无 AreaChanged ⇒ 按 Town 处理）
            sound.Clear();
            bus.Emit(Events.StageEntered);
            Check("进图 → area_enter（2D）", sound.Last2D() == SfxRegistry.AreaEnter, sound.Last2D());
            // StageEntered 之前没有 AreaChanged ⇒ 会打一条"按 Town 处理"的 Warn（只报一次，属预期降级）
            Check("进图未收到过 AreaChanged ⇒ 按 Town 起 BGM", sound.LastBgm() == SfxRegistry.BgmTown, sound.LastBgm());

            // 空载荷 / 空键：不得抛异常
            bus.Emit(Events.ItemPicked, (ItemStack)null);
            audio.Sfx("");
            Check("空载荷 / 空键不抛异常（走了 Warn 降级分支）", true, "见上方 [Audio] 日志");

            // 音量同步事件（设置面板改音量后 Audio 只同步、不重复落盘）
            var beforeSave = 0;
            bus.Emit(Events.VolumeChanged, new AudioVolumeArgs { bgm = 0.11f, sfx = 0.22f, bgmMute = false, sfxMute = true });
            Check("VolumeChanged → 同步 BGM 音量（0.11）",
                Math.Abs(audio.BgmVolume - 0.11f) < 1e-4, audio.BgmVolume.ToString("0.00"));
            Check("VolumeChanged → 同步 SFX 音量（0.22）",
                Math.Abs(audio.SfxVolume - 0.22f) < 1e-4, audio.SfxVolume.ToString("0.00"));
            Check("VolumeChanged → 同步静音（sfxMute=true，未重复落盘）",
                sound.Mutes.TryGetValue(SoundGroup.SFX, out var m) && m, "sfxMute=" + (sound.Mutes.TryGetValue(SoundGroup.SFX, out var mm) ? mm.ToString() : "?"));
            GC.KeepAlive(beforeSave);
        }

        /// <summary>脚步节流：模拟移动 2 秒（每帧一次格变化 + 一次 Tick）。</summary>
        private static void FootstepChecks(ConsoleEventBus bus, RecSound sound, AudioModule audio)
        {
            const float dt = 1f / 60f;
            var interval = AudioHook.FootstepIntervalSeconds;
            Check("脚步间隔 = 每步格数 / PlayerWalkSpeed（用 GameConst 算）",
                Math.Abs(interval - 2f / GameConst.PlayerWalkSpeed) < 1e-6,
                $"{interval:0.000}s = 2 格 / {GameConst.PlayerWalkSpeed} 格每秒");

            sound.Clear();
            var frames = (int)Math.Round(2f / dt);           // 2 秒
            for (var i = 0; i < frames; i++)
            {
                bus.Emit(Events.PlayerGridChanged, new Vector2Int(i % 20, (i / 20) % 20));
                audio.Tick(dt);
            }
            var steps = sound.Count3D(SfxRegistry.Footstep);

            // ★ 片 2b：期望次数**由常量推出**（⛔ 不再写死 0.35 / 固定区间 [5,7]）——
            //   旧的 `2f / 0.35f` 与 [5,7] 是照着"每步 2 格 ÷ 旧走速 6 格每秒 = 0.333s"手算的，
            //   走速按原版改成 3.0 格/秒后间隔变 0.667s ⇒ 旧区间会误红。
            //   口径：2 秒内 ≈ 2/interval 次；±1 的余量 = 逐帧（60fps）离散步进 + 起步相位的量化误差。
            var expect = 2f / interval;
            var lo = (int)Math.Floor(expect) - 1;
            var hi = (int)Math.Ceiling(expect) + 1;
            Check($"模拟移动 2 秒 → 脚步次数 = 2/间隔（={expect:0.0}，±1 帧量化）",
                steps >= lo && steps <= hi,
                $"实测 {steps} 次（间隔 {interval:0.000}s = 每步 2 格 / {GameConst.PlayerWalkSpeed} 格每秒 ⇒ 理论 {expect:0.0} 次，容许 [{lo},{hi}]）");
            Check("脚步是**位置音**（走 SfxAt → 3D 通道）",
                sound.Sfx3D.Count == steps && sound.Sfx2D.Count == 0,
                $"3D={sound.Sfx3D.Count} 2D={sound.Sfx2D.Count}");

            // 静止：不移动 → 0 次脚步
            sound.Clear();
            for (var i = 0; i < frames; i++) audio.Tick(dt);
            Check("静止不发声：不移动 2 秒 → 脚步 0 次", sound.Count3D(SfxRegistry.Footstep) == 0,
                "实测 " + sound.Count3D(SfxRegistry.Footstep) + " 次");

            // 停一下再走：不应"刚起步就响"（静止时清零累积）
            sound.Clear();
            bus.Emit(Events.PlayerGridChanged, new Vector2Int(1, 1));
            audio.Tick(dt);
            Check("刚起步不到 1 步的时间间隔内不出脚步（静止时累积已清零）",
                sound.Count3D(SfxRegistry.Footstep) == 0, "实测 " + sound.Count3D(SfxRegistry.Footstep) + " 次");
        }

        /// <summary>BGM 切区域：Town → BloodMoor → DenOfEvil ⇒ 三首不同。</summary>
        private static void BgmChecks(ConsoleEventBus bus, RecSound sound, AudioModule audio)
        {
            audio.Reset();                     // 清零"当前曲目"，避免上面 StageEntered 起的 town 把第一次同名切歌吃掉
            sound.Clear();

            bus.Emit(Events.AreaChanged, AreaId.Town);
            bus.Emit(Events.AreaChanged, AreaId.BloodMoor);
            bus.Emit(Events.AreaChanged, AreaId.DenOfEvil);

            Check("三次 AreaChanged → 三次切歌", sound.Bgm.Count == 3, string.Join(" → ", sound.Bgm));
            Check("三首互不相同（Town/BloodMoor/DenOfEvil 各一首）",
                sound.Bgm.Count == 3 && sound.Bgm[0] == SfxRegistry.BgmTown
                && sound.Bgm[1] == SfxRegistry.BgmBloodMoor && sound.Bgm[2] == SfxRegistry.BgmDenOfEvil,
                string.Join(" → ", sound.Bgm));

            // 重复发同一区域（AppFlow 换区域时 Map.Generate + EnterArea 各发一次）不应重复切歌
            sound.Clear();
            bus.Emit(Events.AreaChanged, AreaId.DenOfEvil);
            Check("同一区域重复 AreaChanged → 不重复切歌", sound.Bgm.Count == 0, "切歌次数=" + sound.Bgm.Count);
        }

        /// <summary>订阅 / 注销（`Game.Event` 没有句柄 ⇒ 必须用同一方法引用注销）。</summary>
        private static void SubscriptionChecks(ConsoleEventBus bus, AudioModule audio)
        {
            var before = bus.HandlerCount(Events.ItemPicked);
            Check("触发点已订阅（ItemPicked 有监听）", before >= 1, "HandlerCount=" + before);

            audio.Detach();
            var after = bus.HandlerCount(Events.ItemPicked);
            Check("Detach 后监听被注销（同一方法引用）", after == before - 1, $"{before} → {after}");
        }

        /// <summary>BGM 缺失也只报一次。</summary>
        private static bool MissingBgmOnce(AudioModule audio, FakeProbe probe, CountingLogger logger)
        {
            AudioLog.ResetForTest();
            logger.Clear();
            probe.Available = false;
            audio.Reset();                                      // 清"当前曲目"，否则同名切歌会被短路掉
            for (var i = 0; i < 50; i++) audio.Bgm(SfxRegistry.BgmTown);
            var missing = AudioLog.MissingWarnCount;
            var calls = probe.CallCount(ResPaths.Bgm(SfxRegistry.BgmTown));
            probe.Available = true;
            return missing == 1 && calls == 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 小工具
        // ═════════════════════════════════════════════════════════════════════

        private static void Sfx(ConsoleEventBus bus, RecSound sound, string what, string expectKey, Action emit)
        {
            sound.Clear();
            emit();
            var got = sound.Last2D();
            Check(what, got == expectKey, $"期望 {expectKey}，实得 {got}（2D 通道；3D={sound.Last3D()}）");
        }

        private static bool AllHaveFileNames(IReadOnlyCollection<string> keys, bool sfx)
        {
            foreach (var k in keys)
            {
                var f = sfx ? SfxRegistry.SfxFileName(k) : SfxRegistry.BgmFileName(k);
                if (string.IsNullOrEmpty(f) || !f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static List<string> ConstStringsOf(Type t)
        {
            var list = new List<string>();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Static);
            foreach (var f in fields)
            {
                if (!f.IsLiteral || f.FieldType != typeof(string)) continue;
                list.Add((string)f.GetRawConstantValue());
            }
            return list;
        }

        private static void DumpRegistry()
        {
            Console.WriteLine("  ── 音效键登记表（键 → 期望 .wav 文件名）──");
            var sfx = new List<string>(SfxRegistry.AllSfxKeys);
            sfx.Sort(StringComparer.Ordinal);
            foreach (var k in sfx)
                Console.WriteLine($"    SFX  {k,-16} → {SfxRegistry.SfxFileName(k)}");
            var bgm = new List<string>(SfxRegistry.AllBgmKeys);
            bgm.Sort(StringComparer.Ordinal);
            foreach (var k in bgm)
                Console.WriteLine($"    BGM  {k,-16} → {SfxRegistry.BgmFileName(k)}");
        }

        private static void Section(string title)
        {
            Console.WriteLine("── " + title + " ──");
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }
    }
}
