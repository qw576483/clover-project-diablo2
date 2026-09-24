// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Audio/AudioModule.cs
// `IAudioModule` 的**唯一实现**（门面，internal，无参构造 ⇒ `AppContext.AutoWire` 能反射创建）。
//
// 分工：
//   · 本类  = 播放出口（Sfx / SfxAt / Bgm / StopBgm / 音量与静音）+ **缺文件降级**；
//   · `SfxRegistry` = 音效键 → 期望文件名的唯一登记表；
//   · `IAudioClipProbe` = 唯一的资源**存在性**接缝（生产 = `EngineAudioClipProbe`，薄转发到引擎
//     `Game.Res.Exists`）。
//
//   （`_missingSfx` / `_missingBgm`）＋配套的两张「已探测过」表（`_probedSfx` / `_probedBgm`），
//   用来做「缺文件只 Warn 一次 + 之后不再调用引擎」。这两件事**各有现成口径**，四张表已全部删除：
//     · **存在性**：不再自建异步探测（原 `LoadAsset<AudioClip>` ＋自己算缓存），改用引擎既有入口
//       `Game.Res.Exists`（`IResourceManager.Exists` 声明于 `Runtime/Core/Contracts.cs:1287`，
//       实现在 `Runtime/Resource/ResourceManager.cs:251`，**按路径缓存** —— 重复问 = 字典命中，
//     · **只 Warn 一次**：交给日志层唯一那份静态集合（`AudioLog.cs:31-34` 的
//       `MissingSfxWarned` / `MissingBgmWarned`），本类不再各存一份；
//       引擎 `Sound.cs` 对「真的走到加载」的 `clip == null` 另有整进程一次的
//       `LogThrottle.WarnOnce`（`Sound.cs:272/336/361/387`）作第二道网（被存在性闸门拦住时走不到）；
//     · **不再调用引擎**：存在性为假 ⇒ 本类**不调** `Game.Sound`（原行为逐字保留）。
//   副作用（有意为之）：`_probed*` 消失后，同一缺失键的每次请求都会问一次存在性 ——
//   生产里那是引擎 `_existsCache` 的一次字典命中（不打盘、不加载），**不是**恢复成"每次读盘"。
//
// 触发点现状（**避免重复发声**，先读了一遍已有代码）：
//   命中/未命中/玩家受击/玩家死亡/怪物死亡 → 已由 `Module/Combat/DamagePipeline.cs`
//       **直连** `ctx.Audio.SfxAt(SfxKeys.…)` 发出；
//   怪物攻击 / 萨满复活 → `Module/Monster/MonsterModule.cs` 直连；
//   技能施放 → `Module/Skill/SkillModule.cs` 直连（按 `DamageType` 选键）。
//   ⇒ 本模块**不再**为这些事件另订阅一遍（否则同一次命中出两声）。`AudioHook` 只接
//     **尚未覆盖**的那些：脚步 / 拾取 / 使用 / 升级 / 任务完成 / 复活 / UI / 对话 / 商店 / 进图 / 传送 / BGM。
//
// 缺文件降级（硬要求 ④）：存在性为假 ⇒ **每个键只 Warn 一次** + 之后**静默**，且**不再调用引擎**，
//   **不抛异常**（「只 Warn 一次」的存放处 = `AudioLog` 的静态集合，见上方 段）。
//   素材到位后无需改代码，自动出声。
//
// 音量（硬要求 ①）：`Game.Sound.SetVolume(SoundGroup.BGM/SFX, v)` + `Game.Sound.SetMute(…)`，
//   并持久化到 `Game.Setting`（键 `GameConst.SettingKeyBgmVolume/SfxVolume`，初值取 `Cfg`）。
//   早已过期 —— 键在 `Core/GameConst.cs:217/220` 就有）：
//     · 冷启动读回：`LoadVolumeFromSettings` 读 `SettingKeyBgmMute/SfxMute`（缺项 ⇒ false）；
//     · 生效：`ApplyVolumeToEngine` 把两个开关施加到 `Sound.SetMute(BGM/SFX, …)`；
//     · 运行期修改：`SetMute` 落盘（`Setting.Set` + `Save()`）再施加 ⇒ 重启仍在。
//   语义 = 与音量**两个维度、互不覆盖**（`GameConst.SettingKeyBgmMute` 的注释已定口径：
//   音量 0 与静音是两回事，取消静音要能还原到静音前的音量）。
//   目前**没有 UI 调用方**（原版设置屏的静音开关在 `Menu/SoundOptions` 素材侧无逐帧出处
//   ⇒ 按"A 没有的不加"不新增 UI 元件）；调用入口 = `IAudioModule.SetMute`（契约已有），
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Audio
{
    /// <summary>音效门面实现（触发点接线在 <see cref="AudioHook"/>）。</summary>
    internal sealed class AudioModule : IAudioModule
    {
        /// <summary>BGM 切歌淡入淡出时长（秒；引擎 `PlayBGM(clip, fade)` 的 fade 参数）。</summary>
        private const float BgmFadeSeconds = 0.6f;

        //   `AudioLog` 的静态集合负责。见文件头 段。

        /// <summary>事件触发点接线器（与本类同属 Audio 模块，直接持有本类引用不违反跨模块约定）。</summary>
        private readonly AudioHook _hook;

        private float _bgm;
        private float _sfx;
        private bool _bgmMute;
        private bool _sfxMute;

        /// <summary>当前正在播的 BGM 键（null = 没在播；用于"同一首不重播"）。</summary>
        private string _currentBgm;

        /// <inheritdoc />
        public float BgmVolume => _bgm;

        /// <inheritdoc />
        public float SfxVolume => _sfx;

        /// <summary>
        /// 音频资源探测接缝（生产用 <see cref="EngineAudioClipProbe"/>）。
        /// `internal set` **仅供离线自检宿主**（`tools/audiocheck`）注入替身。
        /// </summary>
        internal IAudioClipProbe ClipProbe { get; set; }

        /// <summary>构造：登记表自检 → 载入音量并施加 → 接线事件触发点（无参构造，供 AutoWire）。</summary>
        public AudioModule()
        {
            ClipProbe = new EngineAudioClipProbe();

            // 登记表自检（键重名 = 静默互覆，最难查）：失败不拦启动，只留一条可定位的 Error。
            var uniqueError = SfxRegistry.ValidateUnique();
            if (uniqueError != null) AudioLog.Error("音效键登记表自检失败：" + uniqueError);

            // 素材出处自检（每个键都要对得上暗黑2 原版素材）：失败同样只留 Error，不拦启动。
            var originError = SfxRegistry.ValidateOrigins();
            if (originError != null) AudioLog.Error("音效素材出处自检失败：" + originError);
            AudioLog.Info("音效键登记表已加载：" + SfxRegistry.Dump());

            LoadVolumeFromSettings();
            ApplyVolumeToEngine();

            _hook = new AudioHook(this);
            _hook.Attach();
        }

        // ═════════════════════════════════════════════════════════════════════
        // IAudioModule：播放
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Sfx(string key) => PlaySfxInternal(key, false, Vector3.zero);

        /// <inheritdoc />
        public void SfxAt(string key, float worldX, float worldY, float worldZ)
            => PlaySfxInternal(key, true, new Vector3(worldX, worldY, worldZ));

        /// <inheritdoc />
        public void Bgm(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                StopBgm();
                return;
            }

            if (!SfxRegistry.IsBgm(key)) AudioLog.UnregisteredKey(key, true);
            if (_currentBgm == key) return;                       // 同一首不重播（换区域才切歌）

            var path = ResPaths.Bgm(key);
            var file = SfxRegistry.BgmFileName(key) ?? (key + SfxRegistry.BgmExtension);
            var probe = ClipProbe;
            if (probe == null)
            {
                PlayBgmNow(key);
                return;
            }

            // 存在性问引擎（`Game.Res.Exists`，按路径缓存）；缺失 ⇒ 日志层只报一次 + 本次不调引擎。
            probe.Probe(path, ok =>
            {
                if (!ok)
                {
                    AudioLog.MissingBgm(key, file, path);
                    return;
                }
                PlayBgmNow(key);
            });
        }

        /// <inheritdoc />
        public void StopBgm()
        {
            _currentBgm = null;
            var sound = Game.Sound;
            if (sound == null)
            {
                AudioLog.NoSoundManager();
                return;
            }
            sound.StopBGM();
        }

        // ═════════════════════════════════════════════════════════════════════
        // IAudioModule：音量 / 静音
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void SetVolume(float bgm, float sfx)
        {
            _bgm = Mathf.Clamp01(bgm);
            _sfx = Mathf.Clamp01(sfx);

            ApplyVolumeToEngine();
            PersistVolume();
            AudioLog.Info($"音量已设置：bgm={_bgm:0.00} sfx={_sfx:0.00}" +
                          $"（已写 Game.Setting[\"{GameConst.SettingKeyBgmVolume}\"/\"{GameConst.SettingKeySfxVolume}\"] 并 Apply）");

            var bus = Game.Event;
            if (bus != null) bus.Emit(Events.VolumeChanged, GetVolume());
        }

        /// <inheritdoc />
        public void SetMute(bool bgmMute, bool sfxMute)
        {
            _bgmMute = bgmMute;
            _sfxMute = sfxMute;
            ApplyVolumeToEngine();
            PersistMute();                                   // ★ T0FIX-B：落盘（旧口径只改内存字段）
            AudioLog.Info($"[T0FIX] 静音已设置：bgm={_bgmMute} sfx={_sfxMute}" +
                          $"（已施加到 Sound.SetMute(BGM/SFX) 并写 Game.Setting" +
                          $"[\"{GameConst.SettingKeyBgmMute}\"/\"{GameConst.SettingKeySfxMute}\"] + Save() ⇒ 重启仍在）");
        }

        /// <inheritdoc />
        public AudioVolumeArgs GetVolume()
        {
            return new AudioVolumeArgs
            {
                bgm = _bgm,
                sfx = _sfx,
                bgmMute = _bgmMute,
                sfxMute = _sfxMute,
            };
        }

        // ═════════════════════════════════════════════════════════════════════
        // IAudioModule：Tick / Reset
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (_hook != null) _hook.Tick(dt);
        }

        /// <inheritdoc />
        public void Reset()
        {
            StopBgm();
            if (_hook != null) _hook.ResetMotion();
            AudioLog.Info("音频模块已复位（停 BGM / 清脚步节流状态）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部：播放
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// SFX 统一出口：登记 → 节流 → **存在性（问引擎 `Game.Res.Exists`）** → 播放。
        /// 不存在 ⇒ 只 Warn 一次（`AudioLog` 的静态集合）且**不调引擎**。
        /// </summary>
        private void PlaySfxInternal(string key, bool positional, Vector3 pos)
        {
            if (string.IsNullOrEmpty(key))
            {
                AudioLog.EmptyKey();
                return;
            }

            if (!SfxRegistry.IsSfx(key)) AudioLog.UnregisteredKey(key, false);

            //   （`AudioHook` 的事件触发与 `DamagePipeline` / `MonsterModule` 的直连 `SfxAt` 都走这里），
            //   所以闸门放在这里才能真正防住"某个键把 32 个音源占满 ⇒ 别的音效被静默丢弃"。
            //   口径与推导见 `SfxThrottle` 文件头（含"时间源不可用 ⇒ 闸门惰性"）。
            float throttledSince;
            if (SfxThrottle.ShouldDrop(key, out throttledSince))
            {
                AudioLog.WarnThrottledSfx(key, throttledSince);
                return;
            }

            var path = ResPaths.Sfx(key);
            var file = SfxRegistry.SfxFileName(key) ?? (key + SfxRegistry.SfxExtension);
            var probe = ClipProbe;
            if (probe == null)
            {
                PlaySfxNow(key, positional, pos);
                return;
            }

            // 存在性问引擎（`Game.Res.Exists`，按路径缓存）；缺失 ⇒ 日志层只报一次 + 本次不调引擎。
            probe.Probe(path, ok =>
            {
                if (!ok)
                {
                    AudioLog.MissingSfx(key, file, path);
                    return;
                }
                PlaySfxNow(key, positional, pos);
            });
        }

        /// <summary>真正调引擎（键即资源名，引擎自带"文件不存在时静默"）。</summary>
        private void PlaySfxNow(string key, bool positional, Vector3 pos)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                AudioLog.NoSoundManager();
                return;
            }

            if (positional) sound.PlaySFXAt(key, pos);
            else sound.PlaySFX(key);
        }

        /// <summary>真正调引擎切 BGM。</summary>
        private void PlayBgmNow(string key)
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                AudioLog.NoSoundManager();
                return;
            }

            _currentBgm = key;
            sound.PlayBGM(key, BgmFadeSeconds);
            AudioLog.Info($"BGM 切歌：\"{key}\"（fade={BgmFadeSeconds:0.0}s）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部：音量
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>初值：`Game.Setting` 优先，缺项回落 `Cfg`（配置文件里的默认音量）。</summary>
        private void LoadVolumeFromSettings()
        {
            var setting = Game.Setting;
            if (setting == null)
            {
                AudioLog.NoSetting();
                _bgm = Mathf.Clamp01(Cfg.BgmVolume);
                _sfx = Mathf.Clamp01(Cfg.SfxVolume);
                _bgmMute = false;                            // ★ T0FIX-B：没设置后端 ⇒ 静音取默认 false
                _sfxMute = false;
                return;
            }

            _bgm = Mathf.Clamp01(setting.Get<float>(GameConst.SettingKeyBgmVolume, Cfg.BgmVolume));
            _sfx = Mathf.Clamp01(setting.Get<float>(GameConst.SettingKeySfxVolume, Cfg.SfxVolume));
            // T0FIX-B：静音开关的**冷启动读回**（缺项 = false = 原版默认不静音）
            _bgmMute = setting.Get<bool>(GameConst.SettingKeyBgmMute, false);
            _sfxMute = setting.Get<bool>(GameConst.SettingKeySfxMute, false);
            AudioLog.Info($"音量初值：bgm={_bgm:0.00} sfx={_sfx:0.00}" +
                          $"（读 \"{GameConst.SettingKeyBgmVolume}\" / \"{GameConst.SettingKeySfxVolume}\"，缺项回落 Cfg）");
            AudioLog.Info($"[T0FIX] 静音初值（冷启动读回）：bgmMute={_bgmMute} sfxMute={_sfxMute}" +
                          $"（读 \"{GameConst.SettingKeyBgmMute}\" / \"{GameConst.SettingKeySfxMute}\"，缺项默认 false；" +
                          "旧口径不落盘 ⇒ 每次启动都回到 false）");
        }

        /// <summary>把当前音量/静音施加到引擎。</summary>
        private void ApplyVolumeToEngine()
        {
            var sound = Game.Sound;
            if (sound == null)
            {
                AudioLog.NoSoundManager();
                return;
            }

            sound.SetVolume(SoundGroup.BGM, _bgm);
            sound.SetVolume(SoundGroup.SFX, _sfx);
            sound.SetMute(SoundGroup.BGM, _bgmMute);
            sound.SetMute(SoundGroup.SFX, _sfxMute);
        }

        /// <summary>音量落盘（键来自 `Core/GameConst`，不写裸字面量）。</summary>
        private void PersistVolume()
        {
            var setting = Game.Setting;
            if (setting == null)
            {
                AudioLog.NoSetting();
                return;
            }

            setting.Set(GameConst.SettingKeyBgmVolume, _bgm);
            setting.Set(GameConst.SettingKeySfxVolume, _sfx);
            setting.Save();
        }

        /// <summary>
        /// T0FIX-B：静音开关落盘（键 = `Core/GameConst.cs:217/220` 的
        /// `SettingKeyBgmMute` / `SettingKeySfxMute`）。与 <see cref="PersistVolume"/> 分开：
        /// 音量行与静音开关是**两个维度**（`GameConst.SettingKeyBgmMute` 的注释定的口径），
        /// 谁改谁写、互不覆盖。
        /// </summary>
        private void PersistMute()
        {
            var setting = Game.Setting;
            if (setting == null)
            {
                AudioLog.NoSetting();
                return;
            }

            setting.Set(GameConst.SettingKeyBgmMute, _bgmMute);
            setting.Set(GameConst.SettingKeySfxMute, _sfxMute);
            setting.Save();
        }

        /// <summary>
        /// 别处（如设置面板）改了音量、已自行落盘并 Emit `Events.VolumeChanged` ⇒ 本模块只同步缓存 + 施加，
        /// **不重复写 Setting**（避免双向写造成"谁最后写谁赢"）。
        /// </summary>
        internal void SyncVolumeFromEvent(AudioVolumeArgs args)
        {
            if (args == null)
            {
                AudioLog.NullPayload("AudioVolumeArgs");
                return;
            }

            _bgm = Mathf.Clamp01(args.bgm);
            _sfx = Mathf.Clamp01(args.sfx);
            _bgmMute = args.bgmMute;
            _sfxMute = args.sfxMute;
            ApplyVolumeToEngine();
        }

        /// <summary>
        /// 注销事件订阅。**仅供离线自检宿主**：同一进程里构造多个 `AudioModule` 时避免事件被重复消费；
        /// 生产流程不会调用（`AppContext.AutoWire` 只创建一个实例，生命周期与进程一致）。
        /// </summary>
        internal void Detach()
        {
            if (_hook != null) _hook.Detach();
        }
    }
}
