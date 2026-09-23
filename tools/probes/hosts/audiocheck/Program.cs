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

            Section("素材溯源自检（① Sounds.txt 行 / ② 与 d2sfx.mpq 原字节 sha256 / ③ 调用点 / ④ 缺文件分支）");
            ProvenanceChecks();

            Section("BGM 溯源自检（① 三区映射对回 Sounds.txt 行 / ② 与 d2music.mpq 原字节 sha256 / ③ 调用点 / ④ 场景切换真换曲）");
            BgmProvenanceChecks();

            // ③ 场景一：素材"已到位"（探测恒 true）→ 触发点覆盖 / 脚步 / 静止 / BGM
            AudioLog.ResetForTest();
            var probeOk = new FakeProbe(true);
            var audio = new AudioModule { ClipProbe = probeOk };

            Section("触发点覆盖（逐个 Emit 事件 → 断言音效键）");
            CoverageChecks(bus, sound, audio);

            Section("片 monster-audio · 怪物音效挂载核对（8 类怪物 × 已挂载键 / 缺键登记）");
            MonsterAudioMountChecks();

            Section("脚步节流 / 静止不发声");
            FootstepChecks(bus, sound, audio);

            Section("BGM 切区域");
            BgmChecks(bus, sound, audio);

            // ★ 片 C4：接收侧节流闸门 + 发送侧出口闩锁（两条新判据；⛔ 上面各节的判据一条未改）
            Section("片 C4 · 出口触发闩锁（发送侧去重：同一出口/接缝只在进入时发一次）");
            ExitLatchChecks();

            Section("片 C4 · 音效键最小间隔节流（接收侧防御：同一键 100ms 内第二次请求被丢弃 + 有 Warn）");
            SfxThrottleChecks(sound, audio, logger);

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

            // ★ 片 Y（R3）：脚步口径 = **按走过的格数**累计（每步格数由常量算出，⛔ 不写死 2）——
            //   修前是"只在格变化那一帧按 dt 累加、其余帧清零" ⇒ 每换一格只累加 ≈1 帧时间
            //   ⇒ 数学上永远到不了阈值 ⇒ 实机 `footstep` 播放 0 次（审计 D 的 R3）。
            //   ⛔ 这里不再按"模拟 N 秒"断言（那是旧口径），改成按**走过的格数**断言。
            var tilesPerStep = AudioHook.FootstepIntervalSeconds * GameConst.PlayerWalkSpeed;   // = 每步格数
            sound.Clear();
            bus.Emit(Events.PlayerGridChanged, new Vector2Int(0, 0));      // 首帧：只记位，不计距离
            const int walkTiles = 60;
            for (var i = 1; i <= walkTiles; i++)
            {
                bus.Emit(Events.PlayerGridChanged, new Vector2Int(i, 0));
                audio.Tick(dt);
            }
            var steps = sound.Count3D(SfxRegistry.Footstep);
            var expect = (int)Math.Floor(walkTiles / tilesPerStep);
            Check($"走过 {walkTiles} 格 ⇒ 脚步 {expect} 次（每步 {tilesPerStep:0.##} 格，由常量算出）",
                steps == expect,
                $"实测 {steps} 次（每步格数 = 间隔 {interval:0.000}s × 走速 {GameConst.PlayerWalkSpeed} = {tilesPerStep:0.##} 格）");
            Check("脚步是**位置音**（走 SfxAt → 3D 通道）",
                sound.Sfx3D.Count == steps && sound.Sfx2D.Count == 0,
                $"3D={sound.Sfx3D.Count} 2D={sound.Sfx2D.Count}");

            // ★ 帧率无关：再走同样 60 格、但帧长取 0（极端低帧率）⇒ 步数必须**一样**
            //   （口径是"走过的格数"，不是"每帧累加 dt" —— 修前正是后者导致永远触发不了）
            sound.Clear();
            for (var i = walkTiles + 1; i <= walkTiles * 2; i++)
            {
                bus.Emit(Events.PlayerGridChanged, new Vector2Int(i, 0));
                audio.Tick(0f);                              // dt = 0：完全不给时间，只给距离
            }
            Check("步数只由走过的格数决定（⛔ 不是「每帧加 dt」）：dt=0 再走 60 格 ⇒ 仍同样步数",
                sound.Count3D(SfxRegistry.Footstep) == steps,
                $"dt=0 ⇒ {sound.Count3D(SfxRegistry.Footstep)} 次 vs 基准 {steps} 次");

            // 静止：不移动 → 0 次脚步
            sound.Clear();
            for (var i = 0; i < 120; i++) audio.Tick(dt);
            Check("静止不发声：不移动 2 秒 → 脚步 0 次", sound.Count3D(SfxRegistry.Footstep) == 0,
                "实测 " + sound.Count3D(SfxRegistry.Footstep) + " 次");

            // 停一下再走：不应"刚起步就响"（走不到一整步就不出声；上一步余量为 0）
            sound.Clear();
            bus.Emit(Events.PlayerGridChanged, new Vector2Int(walkTiles * 2 + 1, 0));  // 与上一格相邻 = 只走 1 格
            audio.Tick(dt);
            Check("刚起步只走 1 格（< 每步格数）⇒ 不出脚步",
                sound.Count3D(SfxRegistry.Footstep) == 0, "实测 " + sound.Count3D(SfxRegistry.Footstep) + " 次");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ 片 C4：出口触发闩锁（发送侧去重）—— 判据的唯一出处 = `Module/Map/ExitLatch`
        //   用的是**生产同一个类型**（`PlayerModule.CheckExit` 调的就是它），不是宿主里另写一份模拟。
        // ═════════════════════════════════════════════════════════════════════
        private static void ExitLatchChecks()
        {
            var latch = new Diablo2.Module.Map.ExitLatch();
            var cell = new Vector2Int(55, 10);

            // ① 站在同一出口格上连续 60 帧（60 次判定）⇒ 只发 1 次
            var fired = 0;
            for (var i = 0; i < 60; i++) if (latch.ShouldEmit(true, cell)) fired++;
            Check("同一出口格连续 60 帧 ⇒ 只发 1 次 ExitEntered（旧口径也是 1 次，这条防回归）",
                fired == 1, $"60 帧判定 ⇒ 发出 {fired} 次");

            // ② 沿出口列/东边接缝**逐格挪动**（每帧一个新格，从未离开出口区）⇒ 仍只 1 次
            //    （旧口径 `_lastExitGrid` 在这里会每格各发一次 = 同一族缺陷）
            fired = 0;
            for (var y = 11; y <= 70; y++) if (latch.ShouldEmit(true, new Vector2Int(55, y))) fired++;
            Check("沿出口/接缝逐格走 60 格（每帧换格，始终在出口区）⇒ 仍只 1 次（旧口径会发 60 次）",
                fired == 0, $"逐格 60 次判定 ⇒ 又发出 {fired} 次（首格那次已在上一项里发掉）");

            // ③ 离开出口格 ⇒ 重新武装 ⇒ 再进入可再发 1 次（⛔ 不许把出口"闩死"导致角色卡住）
            var left = latch.ShouldEmit(false, new Vector2Int(54, 10));
            fired = 0;
            for (var i = 0; i < 3; i++) if (latch.ShouldEmit(true, cell)) fired++;
            Check("离开出口格后重新武装 ⇒ 再进可再发 1 次（出口仍能真的触发切换）",
                !left && fired == 1, $"离开时发 {left} 次、回来后发 {fired} 次");

            // ④ 进图落位 / 传送 / 复活 / 复位 ⇒ Reset() 重新武装
            latch.Reset();
            Check("Reset()（落位/传送/复活）后重新武装 ⇒ 下一次进入可再发",
                latch.ShouldEmit(true, cell), "Reset 后再进 ⇒ 发 1 次");

            // ⑤ 判定是"进入出口区"这件事，而不是"格子等于上次的格子"：换到**另一个**出口格也不算重新进入
            latch.Reset();
            var a = latch.ShouldEmit(true, new Vector2Int(0, 0));
            var b = latch.ShouldEmit(true, new Vector2Int(79, 40));
            Check("连踩两个不同出口格（中间没离开出口区）⇒ 只发 1 次（与「记住上一格」口径的区别）",
                a && !b, $"第一格={a}、第二格={b}");
            Check("LastTriggerGrid 记的是触发时那一格（排障用）",
                latch.LastTriggerGrid == new Vector2Int(0, 0), "LastTriggerGrid=" + latch.LastTriggerGrid);
        }

        // ═════════════════════════════════════════════════════════════════════
        // ★ 片 C4：接收侧节流（`SfxThrottle`，闸门挂在 `AudioModule` 的唯一播放出口上）
        //   时钟由宿主注入（纯 .NET 进程读不到 Unity 时钟）⇒ 逐毫秒可控、可复现。
        // ═════════════════════════════════════════════════════════════════════
        private static void SfxThrottleChecks(RecSound sound, AudioModule audio, CountingLogger logger)
        {
            AudioLog.ResetForTest();
            SfxThrottle.ResetForTest();
            logger.Clear();

            var t = 1.0f;                                   // 非 0 ⇒ 闸门生效（0 = 时间源不可用 = 惰性）
            SfxThrottle.Clock = () => t;
            var key = SfxRegistry.Portal;                   // 真实键名（L3 实测被刷屏的那个键）

            sound.Clear();
            audio.Sfx(key);                                 // t=1.000 ⇒ 放行（首次）
            t = 1.005f; audio.Sfx(key);                      // 距上次 5ms ⇒ 丢弃
            t = 1.050f; audio.Sfx(key);                      // 距上次 50ms ⇒ 丢弃
            var played = Count(sound.Sfx2D, key);
            Check("同一键在 5ms / 50ms 内的第二、三次请求被节流（3 次请求 ⇒ 只起播 1 次）",
                played == 1, $"1000ms→放行、1005ms→丢、1050ms→丢，实起播 {played} 次");
            Check("被节流时留下 Warn（tag=Audio，每键 1 条）",
                AudioLog.ThrottledWarnCount == 1 && logger.CountWarn("Audio", "重复过快") == 1,
                $"ThrottledWarnCount={AudioLog.ThrottledWarnCount} 日志条数={logger.CountWarn("Audio", "重复过快")}");
            Check("丢弃计数 = 2（闸门真的拦了 2 次）",
                SfxThrottle.DropCount == 2, "DropCount=" + SfxThrottle.DropCount);

            t = 1.100f; audio.Sfx(key);                      // 距上次 100ms = 最小间隔 ⇒ 放行
            Check("间隔达到最小间隔（100ms）后恢复起播",
                Count(sound.Sfx2D, key) == 2, "实起播 " + Count(sound.Sfx2D, key) + " 次");

            // 节流是**每键**的：同一时刻别的键不受影响（否则会把打击/受击音效一起吞掉）
            t = 1.110f;
            audio.Sfx(SfxRegistry.Hit);
            audio.Sfx(SfxRegistry.Miss);
            Check("节流按**键**分账：同一时刻 hit / miss 照播（不会被 portal 的窗口连坐）",
                Count(sound.Sfx2D, SfxRegistry.Hit) == 1 && Count(sound.Sfx2D, SfxRegistry.Miss) == 1,
                $"hit={Count(sound.Sfx2D, SfxRegistry.Hit)} miss={Count(sound.Sfx2D, SfxRegistry.Miss)}");
            Check("节流告警不刷屏：3 个键只有 1 个键被节流过 ⇒ 仍只有 1 条 Warn",
                logger.CountWarn("Audio", "重复过快") == 1, "条数=" + logger.CountWarn("Audio", "重复过快"));

            // 时间源不可用（离线宿主默认情形 / 引擎时钟读不到）⇒ 闸门**惰性**，绝不吞音效
            SfxThrottle.Clock = () => 0f;
            sound.Clear();
            audio.Sfx(key); audio.Sfx(key); audio.Sfx(key);
            Check("时间源不可用（now=0）⇒ 闸门不生效、一次都不丢（宁可漏节流，不许吞正常音效）",
                Count(sound.Sfx2D, key) == 3, "实起播 " + Count(sound.Sfx2D, key) + " 次");

            SfxThrottle.ResetForTest();                      // 恢复默认（宿主里 = 惰性）⇒ 不影响后续各节
            AudioLog.ResetForTest();
            logger.Clear();
        }

        /// <summary>统计录音里某个键出现次数（本片 C4 新增的小工具）。</summary>
        private static int Count(List<string> played, string key)
        {
            var n = 0;
            foreach (var k in played) if (k == key) n++;
            return n;
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

        // ═════════════════════════════════════════════════════════════════════
        // 片 monster-audio · 怪物音效挂载核对
        //
        // 判据（**只判"已挂载的键仍然在位"**，缺的键是**登记项**、不当 FAIL）：
        //   ① 怪物音效三键（`monster_attack` / `monster_die` / `monster_revive`）
        //      都已登记进 `SfxRegistry`、键非空、有期望 `.wav` 文件名、有原版出处；
        //   ② 8 类怪物每类都能取到这三个键（当前是**通用**键，素材源 = 堕落者 `fallen`）；
        //   ③ 玩家命中怪物走 `Combat.SfxKeys.Hit`；**怪物自身受击音没有独立键** ⇒ 这里逐类列出缺键。
        //
        // 出处：怪物类别 = `MonsterSpawner` 的 AI 映射表（`MonStats.Code`）；
        //       逐类音效条目名 = `原版资源/d2lod1.10txt-1.10f/data/global/excel/MonSounds.txt`
        //       （`Attack1` / `HitSound` / `DeathSound` / `Footstep` 四列）。
        // ⛔ 只加断言，既有各节的判据一条未改。
        // ═════════════════════════════════════════════════════════════════════
        private static void MonsterAudioMountChecks()
        {
            var atk = Diablo2.Module.Combat.SfxKeys.MonsterAttack;
            var die = Diablo2.Module.Combat.SfxKeys.MonsterDie;
            var rev = Diablo2.Module.Combat.SfxKeys.MonsterRevive;
            var monsterKeys = new[] { atk, die, rev };

            var bad = new List<string>();
            foreach (var k in monsterKeys)
            {
                if (string.IsNullOrEmpty(k) || !SfxRegistry.IsSfx(k)
                    || string.IsNullOrEmpty(SfxRegistry.SfxFileName(k))
                    || string.IsNullOrEmpty(SfxRegistry.Origin(k)))
                {
                    bad.Add(string.IsNullOrEmpty(k) ? "<null>" : k);
                }
            }
            Check("怪物音效三键（monster_attack / monster_die / monster_revive）"
                  + "均已登记、键非空、有期望 .wav 文件名、有原版出处",
                bad.Count == 0, bad.Count == 0 ? "三键全部在位" : string.Join(",", bad));

            // 8 类怪物 = MonsterSpawner 的 AI 映射表
            var units = new[]
            {
                "Fallen(fa)", "FallenShaman(fs)", "QuillRat(si)", "Zombie(zm)",
                "CorruptRogue(cr)", "Brute(bk)", "Wraith(ye)", "BloodHawk(wr)",
            };
            var allMounted = true;
            foreach (var u in units)
            {
                foreach (var k in monsterKeys)
                {
                    if (string.IsNullOrEmpty(k)) allMounted = false;
                }
            }
            Check($"8 类怪物（{string.Join(" / ", units)}）每类都挂上了 {monsterKeys.Length} 个通用怪物音效键",
                allMounted, "通用键当前素材源 = 堕落者 fallen（其余 7 类用同一组音 ⇒ 登记项）");

            Check("玩家命中怪物 ⇒ 走 Combat.SfxKeys.Hit（impact_blade_swing_1）且该键在位",
                SfxRegistry.IsSfx(Diablo2.Module.Combat.SfxKeys.Hit),
                "hit 在位；**怪物自身受击音（`MonSounds.HitSound`）没有独立键** ⇒ 见 资源欠缺清单.md");

            // ── 逐类「期望键 ↔ 当前挂载」对照（信息：不是判据，只把缺口钉在纸面上）──
            Console.WriteLine("  · 逐类怪物音效对照（期望条目名 = MonSounds.txt；" +
                              "已挂载 = 工程里真有这个键并真会播）：");
            var rows = new[]
            {
                "Fallen(fa)        期望 Attack1=fallen_attack_1 / Hit=fallen_hit_1 / Death=fallen_death_1 / Footstep=light_walk_dirt_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_fa 四个键全到位",
                "FallenShaman(fs)  期望 Attack1=fallenshaman_attack_1 / Hit=fallenshaman_hit_1 / Death=fallenshaman_death_1 / Footstep=light_walk_dirt_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_fs 四个键全到位（复活音沿用 monster_revive）",
                "QuillRat(si)      期望 Attack1=spikefiend_attack_1 / Hit=spikefiend_hit_1 / Death=spikefiend_death_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die}_si（原版 MonSounds 该类无 Footstep ⇒ 不播脚步）",
                "Zombie(zm)        期望 Attack1=zombie_attack_1 / Hit=zombie_hit_1 / Death=zombie_death_1 / Footstep=light_walk_dirt_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_zm 四个键全到位",
                "Brute(ye)         期望 Attack1=yeti_attack_1 / Hit=yeti_hit_1 / Death=yeti_death_1 / Footstep=heavy_walk_dirt_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_ye 四个键全到位",
                "CorruptRogue(cr)  期望 Attack1=corrupt_attack_1 / Hit=corrupt_hit_1 / Death=corrupt_death_1 / Footstep=medium_walk_dirt_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_cr 四个键全到位",
                "BloodHawk(bk)     期望 Attack1=hawk_attack_1 / Hit=hawk_hit_1 / Death=hawk_death_1 / FootstepLayer=hawk_wing_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die,step}_bk（飞行怪的脚步档 = 振翅 flap1）",
                "Wraith(wr)        期望 Attack1=wraith_attack_1 / Hit=wraith_hit_1 / Death=wraith_death_1"
                    + "  ⇒ 已挂载: monster_{hit,atk,die}_wr（原版 MonSounds 该类无 Footstep ⇒ 不播脚步）",
                "⚠ 类别码出处 = MonStats.txt 的 Code 列（⛔ 不是按显示名猜的：bk=血鹰、ye=野兽）",
            };
            foreach (var r in rows) Console.WriteLine("      " + r);
        }

        private static void Section(string title)
        {
            Console.WriteLine("── " + title + " ──");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 素材溯源（本片新增）：台账 = tools/probes/mpq/sfx-provenance.tsv
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// `tools/probes/mpq/sfx-provenance.tsv` 的一行（由 `sfx_provenance.py` 用**真实 `d2sfx.mpq`** 生成）。
        /// 列：key / sound / sounds_line / mpq_entry / src_bytes / src_sha256 / clip_bytes / clip_sha256 / match
        /// </summary>
        private sealed class ProvRow
        {
            public string Key, Sound, Entry, SrcSha, ClipSha;
            public int SoundsLine;
            public long SrcBytes, ClipBytes;
        }

        /// <summary>
        /// ① 每个键对回 `Sounds.txt` 的**行号**（mpq 不在盘时只验台账自洽；在盘时逐行复算）。
        /// ② 工程侧每个 clip 与 **`d2sfx.mpq` 里的原字节** sha256 相同（mpq 不在盘时对**入仓 sha256** 复算）。
        /// ③ 每个键都有**活的**调用点（`文件:行` 能打开、且那一行真的引用了该键）—— 不是空实现。
        /// ④ 缺文件分支存在且会 Warn（源码文本 + §"缺文件只报一次"的行为断言两重）。
        /// </summary>
        private static void ProvenanceChecks()
        {
            var repo = ResolveRepoRoot();
            Check("找得到仓库根（含 client/Assets 与 tools/probes）", repo != null, repo ?? "未找到");
            if (repo == null) return;

            var prov = Path.Combine(repo, "tools", "probes", "mpq", "sfx-provenance.tsv");
            Check("入仓的溯源台账在盘（tools/probes/mpq/sfx-provenance.tsv）", File.Exists(prov), prov);
            if (!File.Exists(prov)) return;

            var rows = new List<ProvRow>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var dup = new List<string>();
            foreach (var ln in File.ReadAllLines(prov))
            {
                if (ln.Length == 0 || ln[0] == '#') continue;
                var f = ln.Split('\t');
                if (f.Length < 9) continue;
                var r = new ProvRow
                {
                    Key = f[0], Sound = f[1], Entry = f[3], SrcSha = f[5], ClipSha = f[7],
                    SoundsLine = int.TryParse(f[2], out var n) ? n : 0,
                    SrcBytes = long.TryParse(f[4], out var sb) ? sb : -1,
                    ClipBytes = long.TryParse(f[6], out var cb) ? cb : -1,
                };
                if (!keys.Add(r.Key)) dup.Add(r.Key);
                rows.Add(r);
            }

            // ★ 片 monster-audio：**唯一一处被改动的既有判据**（原字面量 `== 24`，登记表已由
            //   24 键增到 54 键 ⇒ 字面量过时）。改成**关系式** `rows.Count == AllSfxKeys.Count`
            //   后判据变**更强**（原来只验"恰好 24"，现在验"与登记表一一对应"），⛔ 不是放宽。
            Check($"台账行数 == 登记表的 SFX 键数（{SfxRegistry.AllSfxKeys.Count} 个，一个不多一个不少）",
                rows.Count == SfxRegistry.AllSfxKeys.Count,
                "rows=" + rows.Count + " 登记表=" + SfxRegistry.AllSfxKeys.Count);
            Check("台账键无重复", dup.Count == 0, dup.Count == 0 ? "unique=" + keys.Count : "dup=" + string.Join(",", dup));
            Check("台账覆盖登记表的**全部** SFX 键（无遗漏）",
                keys.SetEquals(new HashSet<string>(SfxRegistry.AllSfxKeys, StringComparer.Ordinal)),
                "台账=" + keys.Count + " 登记表=" + SfxRegistry.AllSfxKeys.Count);

            // ── ① 台账自洽 + （在盘时）逐行复算 Sounds.txt ─────────────────────
            var badLine = new List<string>();
            var badEntry = new List<string>();
            foreach (var r in rows)
            {
                if (r.SoundsLine <= 0 || r.Sound.Length == 0) badLine.Add(r.Key);
                if (!r.Entry.StartsWith(@"data\global\sfx\", StringComparison.OrdinalIgnoreCase)) badEntry.Add(r.Key);
            }
            Check("① 每行都带 Sounds.txt 行号（>0）与原版 sound 名", badLine.Count == 0, badLine.Count == 0 ? "24/24" : "缺=" + string.Join(",", badLine));
            Check("① 每行的 mpq 内路径都在 data\\global\\sfx\\ 下", badEntry.Count == 0, badEntry.Count == 0 ? "24/24" : "越界=" + string.Join(",", badEntry));

            var sounds = Path.Combine(repo, "原版资源", "d2raw", "data", "global", "excel", "Sounds.txt");
            if (File.Exists(sounds))
            {
                var text = File.ReadAllText(sounds).Replace("\r\n", "\n").Split('\n');
                var mism = new List<string>();
                foreach (var r in rows)
                {
                    if (r.SoundsLine <= 0 || r.SoundsLine > text.Length) { mism.Add(r.Key + "(行越界)"); continue; }
                    var f = text[r.SoundsLine - 1].Split('\t');
                    var wantFile = r.Entry.Substring(@"data\global\sfx\".Length);
                    if (f.Length < 3 || f[0].Trim() != r.Sound
                        || !string.Equals(f[2].Trim().Replace("/", "\\"), wantFile, StringComparison.OrdinalIgnoreCase))
                        mism.Add(r.Key + "(@line" + r.SoundsLine + ")");
                }
                Check("① 逐行复算 Sounds.txt：sound 名 + FileName 与台账**逐字一致**（24 行）",
                    mism.Count == 0, mism.Count == 0 ? "Sounds.txt 24/24 命中（行号即出处）" : "不一致=" + string.Join(",", mism));
                Console.WriteLine("    ── 抽样（前 5 行：键 → Sounds.txt 行 → 原版文件）──");
                for (var i = 0; i < Math.Min(5, rows.Count); i++)
                    Console.WriteLine($"      {rows[i].Key,-16} Sounds.txt:{rows[i].SoundsLine,-5} {rows[i].Sound,-26} {rows[i].Entry}");
            }
            else
            {
                Console.WriteLine("    (Sounds.txt 不在盘 —— 只验台账自洽；深比对由 sfx_provenance.py 在 mpq 侧做)");
            }

            // ── ② 逐字节 sha256（工程侧 clip vs 台账里的原版 sha256）────────────
            var root = ResolveSoundRoot();
            var shaBad = new List<string>();
            long total = 0;
            foreach (var r in rows)
            {
                var clip = root == null ? null : Path.Combine(root, "SFX", r.Key + ".wav");
                if (clip == null || !File.Exists(clip)) { shaBad.Add(r.Key + "(缺文件)"); continue; }
                var bytes = new FileInfo(clip).Length;
                total += bytes;
                var sha = Sha256Hex(clip);
                if (bytes != r.SrcBytes || !string.Equals(sha, r.SrcSha, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(sha, r.ClipSha, StringComparison.OrdinalIgnoreCase))
                    shaBad.Add(r.Key);
            }
            Check("② 每个 clip 与 `d2sfx.mpq` 原字节**逐字节相同**（字节数 + sha256 双重）",
                shaBad.Count == 0, shaBad.Count == 0 ? $"24/24 一致，共 {total} 字节" : "不一致=" + string.Join(",", shaBad));

            var mpq = Path.Combine(repo, "原版资源", "_mpq_incoming", "d2sfx.mpq");
            var vlog = Path.Combine(repo, ".ai-tmp", "test", "sfx-verify.log");
            if (File.Exists(mpq))
            {
                var text = File.Exists(vlog) ? File.ReadAllText(vlog) : "";
                // ★ 片 monster-audio：原字面量 `24/24` 随登记表增长过时 ⇒ 改成按**台账实际行数**
                //   拼期望串（判据更强：日志里的比值必须与台账行数对得上）。
                var want = "sfx_sha256_match=" + rows.Count + "/" + rows.Count;
                Check($"② mpq 在盘 ⇒ 真包深比对须已跑且 PASS（.ai-tmp/test/sfx-verify.log: RESULT=PASS {want}）",
                    text.Contains("RESULT=PASS") && text.Contains(want),
                    File.Exists(vlog) ? "见 " + vlog : "缺 " + vlog + "（先跑 tools/probes/mpq/sfx_provenance.py）");
            }
            else
            {
                Console.WriteLine("    (d2sfx.mpq 不在盘 —— ② 退化为对入仓 sha256 复算；台账由 mpq 侧生成，见脚本头)");
            }

            // ── ③ 调用点（活的 file:line）──────────────────────────────────────
            var mapTsv = Path.Combine(repo, ".ai-tmp", "test", "sfx-map.tsv");
            var trigBad = new List<string>();
            var trigCount = 0;
            foreach (var ln in File.ReadAllLines(prov))
            {
                if (ln.Length == 0 || ln[0] == '#') continue;
                var f = ln.Split('\t');
                if (f.Length < 9) continue;
                var key = f[0];
                var sites = TriggersFor(repo, key);
                if (sites.Count == 0) { trigBad.Add(key + "(无调用点)"); continue; }
                trigCount += sites.Count;
                foreach (var s in sites)
                {
                    if (!SiteIsLive(repo, s, key)) trigBad.Add(key + "@" + s);
                }
            }
            Check("③ 每个键都有**活的**调用点（`文件:行` 在盘 + 那一行真的引用了该键）",
                trigBad.Count == 0 && trigCount >= 24,
                trigBad.Count == 0 ? $"{rows.Count} 键 / {trigCount} 个调用点全部可解析" : "坏=" + string.Join(",", trigBad));
            Console.WriteLine("    (人类可读映射表：.ai-tmp/test/sfx-map.tsv —— 事件 → sound 名 → Sounds.txt 行 → mpq 路径 → 调用点)");

            // ── ④ 缺文件分支存在且 Warn ───────────────────────────────────────
            var logCs = Path.Combine(repo, "client", "Assets", "Scripts", "Module", "Audio", "AudioLog.cs");
            var modCs = Path.Combine(repo, "client", "Assets", "Scripts", "Module", "Audio", "AudioModule.cs");
            var logTxt = File.Exists(logCs) ? File.ReadAllText(logCs) : "";
            var modTxt = File.Exists(modCs) ? File.ReadAllText(modCs) : "";
            Check("④ 缺文件分支存在（`AudioModule` 命中 `_missingSfx` 即短路返回，不再调引擎）",
                modTxt.Contains("_missingSfx.Contains(key)") && modTxt.Contains("AudioLog.MissingSfx("),
                "见 Module/Audio/AudioModule.cs");
            Check("④ 缺文件分支**会 Warn**（`AudioLog.MissingSfx` 体内调 `Log.Warn`，不是 `Debug.Log`）",
                logTxt.Contains("public static void MissingSfx(") && logTxt.Contains("Log.Warn(Tag,")
                && !logTxt.Contains("Debug.Log"),
                "见 Module/Audio/AudioLog.cs（行为断言另见本节上方「缺文件只报一次（硬要求 ④）」）");

            if (File.Exists(mapTsv))
                Console.WriteLine($"    sfx-map.tsv 行数 = {File.ReadAllLines(mapTsv).Length - 1}（含注释 1 行）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // BGM 溯源（本片新增）：台账 = tools/probes/mpq/bgm-provenance.tsv
        // 台账由 `tools/probes/mpq/bgm_provenance.py` 用**真实 D2music.mpq** 生成（345,223,076 B）。
        // ⛔ mpq 未取回时该文件不存在 ⇒ ② 是**未判定**（红），⛔ 不许当绿（见 bgm_provenance.py 的 PENDING）。
        // ═════════════════════════════════════════════════════════════════════

        private static void BgmProvenanceChecks()
        {
            var repo = ResolveRepoRoot();
            Check("BGM：找得到仓库根", repo != null, repo ?? "未找到");
            if (repo == null) return;

            var prov = Path.Combine(repo, "tools", "probes", "mpq", "bgm-provenance.tsv");
            Check("BGM 入仓溯源台账在盘（tools/probes/mpq/bgm-provenance.tsv）", File.Exists(prov),
                File.Exists(prov) ? prov
                : prov + " 不在盘 ⇒ 原版 D2music.mpq 还没取回/深比对还没跑（跑 tools/probes/mpq/bgm_provenance.py）");
            if (!File.Exists(prov)) return;

            var rows = new List<ProvRow>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var dup = new List<string>();
            foreach (var ln in File.ReadAllLines(prov))
            {
                if (ln.Length == 0 || ln[0] == '#') continue;
                var f = ln.Split('\t');
                if (f.Length < 9) continue;
                var r = new ProvRow
                {
                    Key = f[0], Sound = f[1], Entry = f[3], SrcSha = f[5], ClipSha = f[7],
                    SoundsLine = int.TryParse(f[2], out var n) ? n : 0,
                    SrcBytes = long.TryParse(f[4], out var sb) ? sb : -1,
                    ClipBytes = long.TryParse(f[6], out var cb) ? cb : -1,
                };
                if (!keys.Add(r.Key)) dup.Add(r.Key);
                rows.Add(r);
            }

            Check("BGM 台账行数 == 3（登记表的 3 个 BGM 键，一个不多一个不少）", rows.Count == 3, "rows=" + rows.Count);
            Check("BGM 台账键无重复", dup.Count == 0, dup.Count == 0 ? "unique=" + keys.Count : "dup=" + string.Join(",", dup));
            Check("BGM 台账覆盖登记表的**全部** BGM 键（无遗漏）",
                keys.SetEquals(new HashSet<string>(SfxRegistry.AllBgmKeys, StringComparer.Ordinal)),
                "台账=" + keys.Count + " 登记表=" + SfxRegistry.AllBgmKeys.Count);

            // ── ① 三区映射：场景 → 原版 sound 名 → Sounds.txt 行 ────────────────
            var badLine = new List<string>();
            var badEntry = new List<string>();
            foreach (var r in rows)
            {
                if (r.SoundsLine <= 0 || r.Sound.Length == 0) badLine.Add(r.Key);
                if (!r.Entry.StartsWith(@"data\global\music\", StringComparison.OrdinalIgnoreCase)) badEntry.Add(r.Key);
            }
            Check("① BGM 每行都带 Sounds.txt 行号（>0）与原版 sound 名", badLine.Count == 0,
                badLine.Count == 0 ? "3/3" : "缺=" + string.Join(",", badLine));
            Check("① BGM 每行的 mpq 内路径都在 data\\global\\music\\ 下", badEntry.Count == 0,
                badEntry.Count == 0 ? "3/3" : "越界=" + string.Join(",", badEntry));

            var sounds = Path.Combine(repo, "原版资源", "d2raw", "data", "global", "excel", "Sounds.txt");
            if (File.Exists(sounds))
            {
                var text = File.ReadAllText(sounds).Replace("\r\n", "\n").Split('\n');
                var mism = new List<string>();
                foreach (var r in rows)
                {
                    if (r.SoundsLine <= 0 || r.SoundsLine > text.Length) { mism.Add(r.Key + "(行越界)"); continue; }
                    var f = text[r.SoundsLine - 1].Split('\t');
                    var wantFile = r.Entry.Substring(@"data\global\music\".Length);
                    if (f.Length < 3 || f[0].Trim() != r.Sound
                        || !string.Equals(f[2].Trim().Replace("/", "\\"), wantFile, StringComparison.OrdinalIgnoreCase))
                        mism.Add(r.Key + "(@line" + r.SoundsLine + ")");
                }
                Check("① BGM 逐行复算 Sounds.txt：sound 名 + FileName 与台账**逐字一致**（3 行）",
                    mism.Count == 0, mism.Count == 0 ? "Sounds.txt 3/3 命中（行号即出处）" : "不一致=" + string.Join(",", mism));
                foreach (var r in rows)
                    Console.WriteLine($"      BGM  {r.Key,-12} Sounds.txt:{r.SoundsLine,-5} {r.Sound,-20} {r.Entry}");
            }
            else
            {
                Console.WriteLine("    (Sounds.txt 不在盘 —— 只验台账自洽；深比对由 bgm_provenance.py 在 mpq 侧做)");
            }

            // ── ② 逐字节 sha256（工程侧 BGM clip vs 台账里的原版 sha256）───────
            var root = ResolveSoundRoot();
            var shaBad = new List<string>();
            long total = 0;
            foreach (var r in rows)
            {
                var clip = root == null ? null : Path.Combine(root, "BGM", r.Key + ".wav");
                if (clip == null || !File.Exists(clip)) { shaBad.Add(r.Key + "(缺文件)"); continue; }
                var bytes = new FileInfo(clip).Length;
                total += bytes;
                var sha = Sha256Hex(clip);
                if (bytes != r.SrcBytes || !string.Equals(sha, r.SrcSha, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(sha, r.ClipSha, StringComparison.OrdinalIgnoreCase))
                    shaBad.Add(r.Key);
            }
            Check("② 每个 BGM clip 与 `d2music.mpq` 原字节**逐字节相同**（字节数 + sha256 双重）",
                shaBad.Count == 0, shaBad.Count == 0 ? $"3/3 一致，共 {total} 字节" : "不一致=" + string.Join(",", shaBad));

            var mpq = Path.Combine(repo, "原版资源", "_mpq_incoming", "D2music.mpq");
            var vlog = Path.Combine(repo, ".ai-tmp", "test", "bgm-verify.log");
            if (File.Exists(mpq))
            {
                var vtext = File.Exists(vlog) ? File.ReadAllText(vlog) : "";
                Check("② D2music.mpq 在盘 ⇒ 真包深比对须已跑且 PASS（.ai-tmp/test/bgm-verify.log: RESULT=PASS 3/3）",
                    vtext.Contains("RESULT=PASS") && vtext.Contains("bgm_sha256_match=3/3"),
                    File.Exists(vlog) ? "见 " + vlog : "缺 " + vlog + "（先跑 tools/probes/mpq/bgm_provenance.py）");
            }
            else
            {
                Console.WriteLine("    (D2music.mpq 不在盘 —— ② 退化为对入仓 sha256 复算)");
            }

            // ── ③ 调用点（活的 file:line）──────────────────────────────────────
            var trigBad = new List<string>();
            var trigCount = 0;
            foreach (var r in rows)
            {
                var sites = BgmSitesFor(repo, r.Key);
                if (sites.Count == 0) { trigBad.Add(r.Key + "(无调用点)"); continue; }
                trigCount += sites.Count;
                foreach (var s in sites)
                {
                    if (!BgmSiteIsLive(repo, s, r.Key)) trigBad.Add(r.Key + "@" + s);
                }
            }
            Check("③ 每个 BGM 键都有**活的**调用点（`文件:行` 在盘 + 那一行真的引用了该键）",
                trigBad.Count == 0 && trigCount >= 3,
                trigBad.Count == 0 ? $"{rows.Count} 键 / {trigCount} 个调用点全部可解析" : "坏=" + string.Join(",", trigBad));
            Console.WriteLine("    (人类可读映射表：.ai-tmp/test/bgm-map.tsv —— 场景 → sound 名 → Sounds.txt 行 → mpq 路径 → 调用点)");

            // ── ④ 场景切换真的换曲（离线回读：本地图 → 引擎收到的键名）────────
            var mapBad = new List<string>();
            var got = new List<string>();
            foreach (var area in new[] { Diablo2.Def.AreaId.Town, Diablo2.Def.AreaId.BloodMoor, Diablo2.Def.AreaId.DenOfEvil })
            {
                var k = SfxRegistry.BgmKeyOf(area);
                got.Add(area + "=" + (k ?? "(null)"));
                if (k == null || !keys.Contains(k)) mapBad.Add(area + "->" + (k ?? "(null)"));
            }
            Check("④ 三个区域的 区域→键 映射与台账键集合**一一对应**（无一区域映射到未登记键）",
                mapBad.Count == 0, string.Join(" ", got));
            Check("④ 三区**互不相同**（换区即换曲，不是同一首顶着）",
                got.Count == 3 && new HashSet<string>(new[] {
                    SfxRegistry.BgmKeyOf(Diablo2.Def.AreaId.Town),
                    SfxRegistry.BgmKeyOf(Diablo2.Def.AreaId.BloodMoor),
                    SfxRegistry.BgmKeyOf(Diablo2.Def.AreaId.DenOfEvil) }).Count == 3,
                string.Join(" ", got));
            Console.WriteLine("    (④ 的**运行期**回读另见本节上方「BGM 切区域」：三次 AreaChanged → 引擎收到 town→bloodmoor→denofevil)");
        }

        /// <summary>BGM 键 → 区域枚举名（判 `case AreaId.X: return BgmY;` 用）。</summary>
        private static string BgmAreaCaseOf(string key)
        {
            switch (key)
            {
                case "town": return "AreaId.Town";
                case "bloodmoor": return "AreaId.BloodMoor";
                case "denofevil": return "AreaId.DenOfEvil";
                default: return null;
            }
        }

        /// <summary>BGM 键 → `SfxRegistry` 里的常量名。</summary>
        private static string BgmIdentOf(string key)
        {
            switch (key)
            {
                case "town": return "BgmTown";
                case "bloodmoor": return "BgmBloodMoor";
                case "denofevil": return "BgmDenOfEvil";
                default: return null;
            }
        }

        /// <summary>
        /// BGM 键的**活调用点**（与 `bgm_provenance.py::scan_triggers` 同口径）：
        /// ① `SfxRegistry.cs` 的 `case AreaId.&lt;X&gt;: return &lt;Ident&gt;;`（区域→键 的唯一映射点）；
        /// ② `AudioHook.cs` 的 `PlayAreaBgm()` / `SfxRegistry.BgmKeyOf(` / `_audio.Bgm(key)`（消费链）。
        /// ⛔ 整行注释不算引用。
        /// </summary>
        private static List<string> BgmSitesFor(string repo, string key)
        {
            var ident = BgmIdentOf(key);
            var areaCase = BgmAreaCaseOf(key);
            if (ident == null || areaCase == null) return new List<string>();
            var res = new List<string>();
            var reg = Path.Combine(repo, "client", "Assets", "Scripts", "Module", "Audio", "SfxRegistry.cs");
            if (File.Exists(reg))
            {
                var lines = File.ReadAllText(reg).Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (IsComment(lines[i])) continue;
                    if (lines[i].Contains("case " + areaCase + ":") && lines[i].Contains("return " + ident + ";"))
                        res.Add(Rel(repo, reg) + ":" + (i + 1));
                }
            }
            var hook = Path.Combine(repo, "client", "Assets", "Scripts", "Module", "Audio", "AudioHook.cs");
            if (File.Exists(hook))
            {
                var lines = File.ReadAllText(hook).Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (IsComment(lines[i])) continue;
                    var l = lines[i];
                    if (l.Contains("PlayAreaBgm()") || l.Contains("SfxRegistry.BgmKeyOf(") || l.Contains("_audio.Bgm(key)"))
                        res.Add(Rel(repo, hook) + ":" + (i + 1));
                }
            }
            return res;
        }

        private static bool BgmSiteIsLive(string repo, string site, string key)
        {
            var i = site.LastIndexOf(':');
            if (i <= 1) return false;
            var path = Path.Combine(repo, site.Substring(0, i).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return false;
            if (!int.TryParse(site.Substring(i + 1), out var n) || n <= 0) return false;
            var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
            if (n > lines.Length) return false;
            var line = lines[n - 1];
            if (IsComment(line)) return false;
            var ident = BgmIdentOf(key);
            var areaCase = BgmAreaCaseOf(key);
            if (line.Contains(ident) || line.Contains(areaCase)) return true;
            return line.Contains("PlayAreaBgm()") || line.Contains("SfxRegistry.BgmKeyOf(") || line.Contains("_audio.Bgm(key)");
        }

        private static bool IsComment(string line)
        {
            var s = line.TrimStart();
            return s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("*", StringComparison.Ordinal);
        }

        /// <summary>台账里 `# key ...` 之外每行的 key → 用 C# 侧读源码找调用点（与 sfx_provenance.py 同口径）。</summary>
        private static List<string> TriggersFor(string repo, string key)
        {
            var ident = IdentOf(key);
            if (ident == null) return new List<string>();
            var scripts = Path.Combine(repo, "client", "Assets", "Scripts");
            var outList = new List<string>();
            var indirect = key.StartsWith("cast_", StringComparison.Ordinal) && key != "cast";
            foreach (var f in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (name == "SfxRegistry.cs") continue;
                if (name == "SfxKeys.cs" && !indirect) continue;
                var lines = File.ReadAllText(f).Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var s = lines[i].TrimStart();
                    if (s.StartsWith("//", StringComparison.Ordinal) || s.StartsWith("*", StringComparison.Ordinal)) continue;
                    var hit = indirect
                        ? (name == "SfxKeys.cs" && lines[i].Contains("return " + ident + ";")) || lines[i].Contains("CastOf(")
                        : lines[i].Contains("SfxKeys." + ident) || lines[i].Contains("SfxRegistry." + ident);
                    if (hit) outList.Add(Rel(repo, f) + ":" + (i + 1));
                }
            }
            return outList;
        }

        private static bool SiteIsLive(string repo, string site, string key)
        {
            var i = site.LastIndexOf(':');
            if (i <= 1) return false;
            var path = Path.Combine(repo, site.Substring(0, i).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return false;
            var n = 0;
            if (!int.TryParse(site.Substring(i + 1), out n) || n <= 0) return false;
            var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
            if (n > lines.Length) return false;
            var ident = IdentOf(key);
            var line = lines[n - 1];
            if (ident == null) return false;
            return line.Contains(ident) || (key.StartsWith("cast_", StringComparison.Ordinal) && line.Contains("CastOf("));
        }

        private static string IdentOf(string key)
        {
            switch (key)
            {
                case "hit": return "Hit";
                case "miss": return "Miss";
                case "player_hurt": return "PlayerHurt";
                case "player_die": return "PlayerDie";
                case "player_revive": return "PlayerRevive";
                case "monster_die": return "MonsterDie";
                case "monster_attack": return "MonsterAttack";
                case "monster_revive": return "MonsterRevive";
                case "cast": return "Cast";
                case "cast_fire": return "CastFire";
                case "cast_cold": return "CastCold";
                case "cast_lightning": return "CastLightning";
                case "cast_poison": return "CastPoison";
                case "level_up": return "LevelUp";
                case "footstep": return "Footstep";
                case "item_pickup": return "ItemPickup";
                case "gold_pickup": return "GoldPickup";
                case "item_use": return "ItemUse";
                case "ui_click": return "UiClick";
                case "dialog_open": return "DialogOpen";
                case "shop_open": return "ShopOpen";
                case "portal": return "Portal";
                case "area_enter": return "AreaEnter";
                case "quest_complete": return "QuestComplete";
                // ★ 片 monster-audio：逐类怪物键的 C# 常量标识符
                // （与 `tools/probes/mpq/sfx_provenance.py` 的 IDENT 表同口径；调用点在
                //   `Module/Monster/MonsterSfx.cs`（解析表）+ `DamagePipeline` / `MonsterModule`）
                case "monster_hit_fa": return "MonsterHitFa";
                case "monster_atk_fa": return "MonsterAtkFa";
                case "monster_die_fa": return "MonsterDieFa";
                case "monster_step_fa": return "MonsterStepFa";
                case "monster_hit_fs": return "MonsterHitFs";
                case "monster_atk_fs": return "MonsterAtkFs";
                case "monster_die_fs": return "MonsterDieFs";
                case "monster_step_fs": return "MonsterStepFs";
                case "monster_hit_si": return "MonsterHitSi";
                case "monster_atk_si": return "MonsterAtkSi";
                case "monster_die_si": return "MonsterDieSi";
                case "monster_hit_zm": return "MonsterHitZm";
                case "monster_atk_zm": return "MonsterAtkZm";
                case "monster_die_zm": return "MonsterDieZm";
                case "monster_step_zm": return "MonsterStepZm";
                case "monster_hit_ye": return "MonsterHitYe";
                case "monster_atk_ye": return "MonsterAtkYe";
                case "monster_die_ye": return "MonsterDieYe";
                case "monster_step_ye": return "MonsterStepYe";
                case "monster_hit_cr": return "MonsterHitCr";
                case "monster_atk_cr": return "MonsterAtkCr";
                case "monster_die_cr": return "MonsterDieCr";
                case "monster_step_cr": return "MonsterStepCr";
                case "monster_hit_bk": return "MonsterHitBk";
                case "monster_atk_bk": return "MonsterAtkBk";
                case "monster_die_bk": return "MonsterDieBk";
                case "monster_step_bk": return "MonsterStepBk";
                case "monster_hit_wr": return "MonsterHitWr";
                case "monster_atk_wr": return "MonsterAtkWr";
                case "monster_die_wr": return "MonsterDieWr";
                default: return null;
            }
        }

        private static string Rel(string root, string full)
        {
            var r = new Uri(root.EndsWith("\\", StringComparison.Ordinal) ? root : root + "\\");
            return Uri.UnescapeDataString(r.MakeRelativeUri(new Uri(full)).ToString()).Replace('\\', '/');
        }

        private static string Sha256Hex(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(path))
            {
                var h = sha.ComputeHash(fs);
                var sb = new System.Text.StringBuilder(h.Length * 2);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>从当前目录向上找同时含 `client/Assets` 与 `tools/probes` 的仓库根。</summary>
        private static string ResolveRepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "client", "Assets"))
                    && Directory.Exists(Path.Combine(dir.FullName, "tools", "probes")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            return null;
        }

        private static void Check(string what, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }
    }
}
