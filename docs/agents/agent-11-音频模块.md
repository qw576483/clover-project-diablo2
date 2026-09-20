# agent-11：Audio 模块（音效/BGM 触发点接入）

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：`docs/步骤文档.md` §3.3（`Game.Sound` 签名）、`tools/ai-skill/conventions.md`、`constraints.md`。

## 1. 目标

实现 `IAudioModule`，并把**音效触发点**接进已有事件链（脚步/挥砍/命中/受击/死亡/拾取/升级/UI 点击/传送/进图）。
**素材（`d2sfx.mpq` 解出的 `.wav`）尚未到位** —— 本 agent 交付**能与到位后无缝对接**的实现：
音效键登记表 + 触发点接线 + 缺文件时的**静默降级与一次性告警**（不许刷屏、不许崩）。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Audio/**`、`client/Assets/Resources/Clover/Sound/{BGM,SFX}/**`（放素材，若已有）、
  `client/Assets/Resources/Clover/D2/SfxKeys/**`（若需）。
- **绝不做**：不改 `Core/`、`Def/`、`Module/Contracts.cs`、`App/`、`UI/`、`Editor/`、其它 `Module/*`；不改契约；不改 `tools/ai-skill/`。
  **触发点接线只能通过 `Game.Event` 订阅实现**（不许去改别的模块的文件）。

## 3. 前置依赖（已就绪）

- `IAudioModule`（`Module/Contracts.cs`）：`Sfx(key)` / `SfxAt(key,x,y,z)` / `Bgm(key)` / `StopBgm()` / `SetVolume(bgm,sfx)` / `SetMute(b1,b2)` / `Volume()→AudioVolumeArgs` / `Tick(float)` / `Reset()`
- 引擎：`Game.Sound` = `ISoundManager`（`PlayBGM(clipName, fade=0.5f)` / `PlaySFX(clipName)` / `PlaySFXAt(clipName, position)` / `StopBGM` / `StopAll` / `SetVolume(SoundGroup,float)`）
  —— **引擎约定的资源路径**：`Sound/BGM/{name}`、`Sound/SFX/{name}`、`Sound/Voice/{name}`（**路径前缀已由 `CloverRes.Init("Clover")` 决定**）
- 已有音效键常量：`Module/Combat/SfxKeys.cs`（agent-07 写的，**读它，不要另起一套**）
- 已有事件（`Core/Events.cs`）：`DamageDealt` / `PlayerDamaged` / `MonsterKilled` / `MonsterSpawned` / `ItemPicked` / `ItemUsed` / `LevelUp` / `PlayerDied` / `QuestChanged` / `QuestCompleted` / `StageEntered` / `StageLeft` / `AreaChanged` / `DialogOpen` / `ShopOpen` / `SkillCast`

## 4. 产出物

| 文件 | 内容 |
| --- | --- |
| `Module/Audio/AudioModule.cs` | `internal sealed class AudioModule : IAudioModule`（无参构造，供 `AutoWire`） |
| `Module/Audio/SfxRegistry.cs` | **音效键 → 文件名**的唯一登记表（键名与 `Module/Combat/SfxKeys.cs` 对齐；**不要有两套键名**）；缺失键要能 `Warn` 一次 |
| `Module/Audio/AudioHook.cs` | 事件订阅 → 触发音效（脚步按移动速度节流、挥砍/命中/死亡/升级/拾取/UI/传送/进图/BGM 切换） |
| `Module/Audio/AudioLog.cs` | tag = `Audio`；**"文件缺失"只报一次**（用私有 bool 标志，**不要用 `Core/Log.WarnOnce`** —— 它依赖 Unity 原生时钟，离线宿主会抛异常） |
| `client/资源欠缺清单.md` | **只追加**一节：需要的 `.wav` 文件名清单（按 `SfxRegistry` 生成） |

**硬要求**

1. **音量与静音**走 `Game.Sound.SetVolume(SoundGroup.BGM/SFX, v)`，并**持久化到 `Game.Setting`**（`Cfg.GameCfg.bgmVolume/sfxVolume` 为初值）。
2. **BGM 切换**：`Town` / `BloodMoor` / `DenOfEvil` 各一首（`Bgm("town")` 这种键），区域切换时切歌（`Game.Sound.PlayBGM(name, fade)`）。
3. **脚步**：按移动速度节流（例如每 0.35s 一步），**静止不发**；用 `Core/GameConst` 的速度常量算间隔。
4. **缺文件降级**：`ResPaths` 里取不到 `Sound/SFX/{name}` 时 → **只报一次 Warn** + 不重复调用引擎（避免刷屏），**不抛异常**。
5. **一个 `.cs` 一个类**；**禁止**裸 `Debug.Log`、`GameObject.Find`。

## 5. 验收标准（用离线宿主，照 `.ai-tmp/hosts/flowcheck/` 建 `.ai-tmp/hosts/audiocheck/`）

- [ ] `dotnet build` 0 错 0 警告；`dotnet run` 断言全过
- [ ] **触发点覆盖**：逐个 `Emit` 上面列出的事件，断言 `SfxRegistry` 里对应键被请求（贴每个事件 → 音效键的映射表）
- [ ] **脚步节流**：模拟移动 2 秒 → 脚步请求次数 ≈ 2/0.35（贴实测次数）
- [ ] **静止不发声**：不移动时不请求脚步（断言）
- [ ] **缺文件只报一次**：连续请求同一个不存在的键 100 次 → 日志恰好 1 条（断言）
- [ ] **音量持久化**：`SetVolume` → `Game.Setting` 有值 → 重建模块后读回一致（断言）
- [ ] **BGM 切区域**：Town→BloodMoor→DenOfEvil 三次 `AreaChanged` → 三次不同 `Bgm` 请求（断言）
- [ ] **`SfxKeys` 不重复**：`Module/Combat/SfxKeys.cs` 与本模块的键表**键名集合一致**（断言 + 贴清单）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出：音效键全清单（键名 + 期望的 `.wav` 文件名 + 触发点）

## 6. 约束

- 素材未到位时**不许用任何非暗黑2 的音频**（禁止 Kenney/Freesound 等通用素材）；只登记清单等素材到位。
- 所有非预期分支必须打日志（文件缺失、音量读写失败、事件载荷异常…）。
- 用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`。
