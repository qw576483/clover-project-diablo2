# 音效素材对照表（键 ↔ 原版条目 ↔ 原版文件 ↔ 触发点 ↔ 落地文件）

> 本表是 `SfxRegistry.cs`（键 → 文件名）与真实素材之间的**对照凭证**。
> 代码里的机器可读版本 = `SfxRegistry.Origins`（`SfxRegistry.Origin(key)` 可查，`ValidateOrigins()` 会自检）。
> 本表由工具输出生成（**不手抄**）：数据 = `tools/probes/mpq/sfx_provenance.py`
> （事件/键/原版条目/Sounds.txt 行/mpq 路径/调用点；逐字节 sha256 台账落 `tools/probes/mpq/sfx-provenance.tsv`），
> 字节数与音频格式 = 落地 `.wav` 现读（`wave` 头部）。

## 0. 三条口径

1. **落地文件名 = 键名 + `.wav`**（例：键 `hit` → `Sound/SFX/hit.wav`）。
   引擎按 `Resources.Load("Clover/Sound/SFX/{键}")` 取（`Runtime/Presentation/Sound.cs:107/63`），
   `SfxRegistry.SfxFiles` 里的期望文件名与之一致 ⇒ **触发点代码一行都没改**。
2. **内容是原版 `.wav` 的原字节**（未重采样 / 未转码 / 未裁剪 / 未剪短），
   来源 = 用户本机暗黑2 数据包 `_assets_src\d2mpq\d2sfx.mpq`（音效）与 `d2music.mpq`（音乐）。
3. **只导了 `SfxRegistry` 登记过的键**（53 SFX + 2 BGM）；`d2sfx.mpq` 里其余 2330 条 `.wav` 一条没导。

## 1. SFX（53 键 → `client/Assets/Resources/Clover/Sound/SFX/{键}.wav`）

| 键 | 原版条目名（Sounds.txt 第 1 列） | 原版文件（`d2sfx.mpq:data\global\sfx\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| 键 | 原版条目名（Sounds.txt 第 1 列） | 原版文件（`d2sfx.mpq:data\global\sfx\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| 键 | 原版条目名（Sounds.txt 第 1 列） | 原版文件（`d2sfx.mpq:data\global\sfx\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| `hit` | `impact_blade_swing_1` | `combat\impact\sword1.wav` | `SFX/hit.wav` | 22314 | 22050Hz 16bit 单声道 0.50s | `Module/Combat/DamagePipeline.cs:130` + `Module/Skill/SkillModule.cs:1025`（挥砍命中，打到目标且未致死） |
| `miss` | `weapon_1hs_small_1` | `combat\weapon\one hand swing small01.wav` | `SFX/miss.wav` | 11556 | 22050Hz 16bit 单声道 0.26s | `Module/Combat/DamagePipeline.cs:210`（挥砍落空） |
| `player_hurt` | `amazon_hit_1` | `combat\player\amazon\soft4.wav` | `SFX/player_hurt.wav` | 25068 | 22050Hz 16bit 单声道 0.57s | `Module/Combat/DamagePipeline.cs:191`（玩家受击，未死） |
| `player_die` | `amazon_death_1` | `combat\player\amazon\death1.wav` | `SFX/player_die.wav` | 31916 | 22050Hz 16bit 单声道 0.72s | `Module/Combat/DamagePipeline.cs:191`（玩家死亡） |
| `player_revive` | `necromancer_revive_target` | `skill\necromancer\revivetarget.wav` | `SFX/player_revive.wav` | 44154 | 22050Hz 16bit 单声道 1.00s | `Module/Audio/AudioHook.cs:265`（玩家复活完成） ※1 原版无“玩家复活”条目 ⇒ 取 necromancer_revive_target |
| `monster_die` | `fallen_death_1` | `monster\fallen\death1.wav` | `SFX/monster_die.wav` | 42030 | 22050Hz 16bit 单声道 0.95s | `Module/Monster/MonsterModule.cs:406` + `Module/Monster/MonsterSfx.cs:38` + `Module/Monster/MonsterSfx.cs:41`（怪物死亡） |
| `monster_attack` | `fallen_attack_1` | `monster\fallen\roar1.wav` | `SFX/monster_attack.wav` | 19530 | 22050Hz 16bit 单声道 0.44s | `Module/Monster/MonsterModule.cs:562`（怪物挥击起手） |
| `monster_revive` | `fallenshaman_resurrect` | `monster\fallenshaman\resurrect.wav` | `SFX/monster_revive.wav` | 29930 | 22050Hz 16bit 单声道 0.68s | `Module/Monster/MonsterModule.cs:646`（萨满复活同伴） |
| `cast` | `amazon_magicarrow_1` | `skill\amazon\magicarrow1.wav` | `SFX/cast.wav` | 30594 | 22050Hz 16bit 单声道 0.69s | `Module/Skill/SkillModule.cs:493`（施放技能，通用回落） |
| `cast_fire` | `monster_cast_fire` | `skill\sorceress\firecast.wav` | `SFX/cast_fire.wav` | 44744 | 22050Hz 16bit 单声道 1.01s | `Module/Skill/SkillModule.cs:493` + `Module/Combat/SfxKeys.cs:175`（火系技能，火弹） |
| `cast_cold` | `monster_cast_cold` | `skill\sorceress\coldcast.wav` | `SFX/cast_cold.wav` | 32154 | 22050Hz 16bit 单声道 0.73s | `Module/Skill/SkillModule.cs:493` + `Module/Combat/SfxKeys.cs:176`（冰系技能，冰弹） |
| `cast_lightning` | `monster_cast_lightning` | `skill\sorceress\eleccast.wav` | `SFX/cast_lightning.wav` | 47628 | 22050Hz 16bit 单声道 1.08s | `Module/Skill/SkillModule.cs:493` + `Module/Combat/SfxKeys.cs:177`（电系技能） |
| `cast_poison` | `amazon_cast_poison` | `skill\amazon\poisoncast.wav` | `SFX/cast_poison.wav` | 43474 | 22050Hz 16bit 单声道 0.98s | `Module/Skill/SkillModule.cs:493` + `Module/Combat/SfxKeys.cs:178`（毒系技能） |
| `level_up` | `cursor_level_up` | `cursor\levelup.wav` | `SFX/level_up.wav` | 55672 | 22050Hz 16bit 单声道 1.26s | `Module/Audio/AudioHook.cs:256`（升级） |
| `footstep` | `light_walk_dirt_1` | `ambient\footstep\LightDirt1.wav` | `SFX/footstep.wav` | 15918 | 22050Hz 16bit 单声道 0.36s | `Module/Audio/AudioHook.cs:230`（脚步，每 2 格一步） |
| `item_pickup` | `item_pickup` | `cursor\pickup.wav` | `SFX/item_pickup.wav` | 1676 | 22050Hz 16bit 单声道 0.04s | `Module/Audio/AudioHook.cs:241`（拾取物品） |
| `gold_pickup` | `item_gold` | `item\gold.wav` | `SFX/gold_pickup.wav` | 56796 | 22050Hz 16bit 单声道 1.29s | `Module/Audio/AudioHook.cs:241`（拾取金币） |
| `item_use` | `item_potion_drink` | `item\potiondrink.wav` | `SFX/item_use.wav` | 21226 | 22050Hz 16bit 单声道 0.48s | `Module/Audio/AudioHook.cs:252`（喝药 / 用卷轴） |
| `ui_click` | `cursor_button_click` | `cursor\button.wav` | `SFX/ui_click.wav` | 3302 | 22050Hz 16bit 单声道 0.07s | `Module/Audio/AudioHook.cs:268` + `Module/Audio/AudioHook.cs:271` + `Module/Audio/AudioHook.cs:274`（UI 点击，面板/对话选项/买/卖） |
| `dialog_open` | `cursor_select` | `cursor\select.wav` | `SFX/dialog_open.wav` | 22444 | 22050Hz 16bit 单声道 0.51s | `Module/Audio/AudioHook.cs:295`（NPC 对话开始） ※2 原版开对话不播专用音 ⇒ 取 cursor_select |
| `shop_open` | `cursor_error` | `cursor\windowopen.wav` | `SFX/shop_open.wav` | 3302 | 22050Hz 16bit 单声道 0.07s | `Module/Audio/AudioHook.cs:306`（商店打开） ※2 原版开商店不播专用音 ⇒ 取 cursor_error/cursor_switch |
| `portal` | `player_townportal_cast` | `skill\misc\portalcast.wav` | `SFX/portal.wav` | 85368 | 22050Hz 16bit 单声道 1.93s | `Module/Audio/AudioHook.cs:310`（传送 / 踩出入口） |
| `area_enter` | `object_stairs` | `object\stairs.wav` | `SFX/area_enter.wav` | 22414 | 22050Hz 16bit 单声道 0.51s | `Module/Audio/AudioHook.cs:325`（进入场景，Stage） |
| `monster_hit_fa` | `fallen_hit_1` | `monster\fallen\gethit1.wav` | `SFX/monster_hit_fa.wav` | 15176 | 22050Hz 16bit 单声道 0.34s | `Module/Monster/MonsterSfx.cs:37`（沉沦魔受击） |
| `monster_atk_fa` | `fallen_attack_1` | `monster\fallen\roar1.wav` | `SFX/monster_atk_fa.wav` | 19530 | 22050Hz 16bit 单声道 0.44s | `Module/Monster/MonsterSfx.cs:37`（沉沦魔挥击起手） |
| `monster_die_fa` | `fallen_death_1` | `monster\fallen\death1.wav` | `SFX/monster_die_fa.wav` | 42030 | 22050Hz 16bit 单声道 0.95s | `Module/Monster/MonsterSfx.cs:38`（沉沦魔死亡） |
| `monster_step_fa` | `light_walk_dirt_1` | `ambient\footstep\LightDirt1.wav` | `SFX/monster_step_fa.wav` | 15918 | 22050Hz 16bit 单声道 0.36s | `Module/Monster/MonsterSfx.cs:38`（沉沦魔脚步） |
| `monster_hit_fs` | `fallenshaman_hit_1` | `monster\fallenshaman\gethit1.wav` | `SFX/monster_hit_fs.wav` | 16802 | 22050Hz 16bit 单声道 0.38s | `Module/Monster/MonsterSfx.cs:40`（沉沦魔萨满受击） |
| `monster_atk_fs` | `fallenshaman_attack_1` | `monster\fallenshaman\roar1.wav` | `SFX/monster_atk_fs.wav` | 17912 | 22050Hz 16bit 单声道 0.40s | `Module/Monster/MonsterSfx.cs:40`（沉沦魔萨满挥击起手） |
| `monster_die_fs` | `fallenshaman_death_1` | `monster\fallenshaman\death1.wav` | `SFX/monster_die_fs.wav` | 48302 | 22050Hz 16bit 单声道 1.09s | `Module/Monster/MonsterSfx.cs:41`（沉沦魔萨满死亡） |
| `monster_step_fs` | `light_walk_dirt_1` | `ambient\footstep\LightDirt1.wav` | `SFX/monster_step_fs.wav` | 15918 | 22050Hz 16bit 单声道 0.36s | `Module/Monster/MonsterSfx.cs:41`（沉沦魔萨满脚步） |
| `monster_hit_si` | `spikefiend_hit_1` | `monster\spikefiend\gethit1.wav` | `SFX/monster_hit_si.wav` | 15102 | 22050Hz 16bit 单声道 0.34s | `Module/Monster/MonsterSfx.cs:43`（尖刺鼠受击） |
| `monster_atk_si` | `spikefiend_attack_1` | `monster\spikefiend\attack1.wav` | `SFX/monster_atk_si.wav` | 34808 | 22050Hz 16bit 单声道 0.79s | `Module/Monster/MonsterSfx.cs:43`（尖刺鼠攻击） |
| `monster_die_si` | `spikefiend_death_1` | `monster\spikefiend\death1.wav` | `SFX/monster_die_si.wav` | 30520 | 22050Hz 16bit 单声道 0.69s | `Module/Monster/MonsterSfx.cs:44`（尖刺鼠死亡） |
| `monster_hit_zm` | `zombie_hit_1` | `monster\zombie\gethit1.wav` | `SFX/monster_hit_zm.wav` | 19344 | 22050Hz 16bit 单声道 0.44s | `Module/Monster/MonsterSfx.cs:46`（僵尸受击） |
| `monster_atk_zm` | `zombie_attack_1` | `monster\zombie\attack1.wav` | `SFX/monster_atk_zm.wav` | 22712 | 22050Hz 16bit 单声道 0.51s | `Module/Monster/MonsterSfx.cs:46`（僵尸攻击） |
| `monster_die_zm` | `zombie_death_1` | `monster\zombie\death1.wav` | `SFX/monster_die_zm.wav` | 44920 | 22050Hz 16bit 单声道 1.02s | `Module/Monster/MonsterSfx.cs:47`（僵尸死亡） |
| `monster_step_zm` | `light_walk_dirt_1` | `ambient\footstep\LightDirt1.wav` | `SFX/monster_step_zm.wav` | 15918 | 22050Hz 16bit 单声道 0.36s | `Module/Monster/MonsterSfx.cs:47`（僵尸脚步） |
| `monster_hit_ye` | `yeti_hit_1` | `monster\yeti\gethit1.wav` | `SFX/monster_hit_ye.wav` | 17930 | 22050Hz 16bit 单声道 0.41s | `Module/Monster/MonsterSfx.cs:49`（野兽受击） |
| `monster_atk_ye` | `yeti_attack_1` | `monster\yeti\attack1.wav` | `SFX/monster_atk_ye.wav` | 33644 | 22050Hz 16bit 单声道 0.76s | `Module/Monster/MonsterSfx.cs:49`（野兽攻击） |
| `monster_die_ye` | `yeti_death_1` | `monster\yeti\death1.wav` | `SFX/monster_die_ye.wav` | 49450 | 22050Hz 16bit 单声道 1.12s | `Module/Monster/MonsterSfx.cs:50`（野兽死亡） |
| `monster_step_ye` | `heavy_walk_dirt_1` | `ambient\footstep\HeavyDirt1.wav` | `SFX/monster_step_ye.wav` | 10952 | 22050Hz 16bit 单声道 0.25s | `Module/Monster/MonsterSfx.cs:50`（野兽脚步，重步） |
| `monster_hit_cr` | `corrupt_hit_1` | `monster\corrupt\gethit1.wav` | `SFX/monster_hit_cr.wav` | 25068 | 22050Hz 16bit 单声道 0.57s | `Module/Monster/MonsterSfx.cs:52`（腐化罗格受击） |
| `monster_atk_cr` | `corrupt_attack_1` | `monster\corrupt\attack1.wav` | `SFX/monster_atk_cr.wav` | 25508 | 22050Hz 16bit 单声道 0.58s | `Module/Monster/MonsterSfx.cs:52`（腐化罗格攻击） |
| `monster_die_cr` | `corrupt_death_1` | `monster\corrupt\die1.wav` | `SFX/monster_die_cr.wav` | 66098 | 22050Hz 16bit 单声道 1.50s | `Module/Monster/MonsterSfx.cs:53`（腐化罗格死亡） |
| `monster_step_cr` | `medium_walk_dirt_1` | `ambient\footstep\MedDirt1.wav` | `SFX/monster_step_cr.wav` | 17876 | 22050Hz 16bit 单声道 0.40s | `Module/Monster/MonsterSfx.cs:53`（腐化罗格脚步，中步） |
| `monster_hit_bk` | `hawk_hit_1` | `monster\hawk\gethit1.wav` | `SFX/monster_hit_bk.wav` | 20052 | 22050Hz 16bit 单声道 0.45s | `Module/Monster/MonsterSfx.cs:55`（血鹰受击） |
| `monster_atk_bk` | `hawk_attack_1` | `monster\hawk\attack1.wav` | `SFX/monster_atk_bk.wav` | 16994 | 22050Hz 16bit 单声道 0.38s | `Module/Monster/MonsterSfx.cs:55`（血鹰攻击） |
| `monster_die_bk` | `hawk_death_1` | `monster\hawk\death1.wav` | `SFX/monster_die_bk.wav` | 25232 | 22050Hz 16bit 单声道 0.57s | `Module/Monster/MonsterSfx.cs:56`（血鹰死亡） |
| `monster_step_bk` | `hawk_wing_1` | `monster\hawk\flap1.wav` | `SFX/monster_step_bk.wav` | 12386 | 22050Hz 16bit 单声道 0.28s | `Module/Monster/MonsterSfx.cs:56`（血鹰振翅） |
| `monster_hit_wr` | `wraith_hit_1` | `monster\wraith\gethit1.wav` | `SFX/monster_hit_wr.wav` | 29600 | 22050Hz 16bit 单声道 0.67s | `Module/Monster/MonsterSfx.cs:58`（幽灵受击） |
| `monster_atk_wr` | `wraith_attack_1` | `monster\wraith\attack1.wav` | `SFX/monster_atk_wr.wav` | 68128 | 22050Hz 16bit 单声道 1.54s | `Module/Monster/MonsterSfx.cs:58`（幽灵攻击） |
| `monster_die_wr` | `wraith_death_1` | `monster\wraith\death1.wav` | `SFX/monster_die_wr.wav` | 51892 | 22050Hz 16bit 单声道 1.18s | `Module/Monster/MonsterSfx.cs:59`（幽灵死亡） |

## 2. BGM（2 键 → `client/Assets/Resources/Clover/Sound/BGM/{键}.wav`）

| 键 | 原版条目名 | 原版文件（`d2music.mpq:data\global\music\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| `town` | `music_town_1` | `act1\town1.wav` | `BGM/town.wav` | 21805760 | 22050Hz 16bit 立体声 247.2s | `AudioHook.PlayAreaBgm` ← `Events.AreaChanged`(Town) / `Events.StageEntered` |
| `bloodmoor` | `music_wilderness` | `act1\wild.wav` | `BGM/bloodmoor.wav` | 42270644 | 22050Hz 16bit 立体声 479.3s | `AudioHook.PlayAreaBgm` ← `Events.AreaChanged`(BloodMoor) |

## 3. 两处"没有原版一一对应"的说明（**不许含糊**）

- **※1 `player_revive`**：原版**没有**"玩家复活"专用音效条目（`Sounds.txt` 里 `player_*` 只有 `player_townportal_cast/_enter`）。
  取语义最近的**原版音** `necromancer_revive_target`（死灵法师·重生 的目标音）。
- **※2 `dialog_open` / `shop_open`**：原版开对话/开商店**不播专用音**（原版这部分是 NPC 语音，属 `d2speech.mpq`，本阶段不做语音）。
  取原版 UI 音：对话框 = `cursor_select`（点选），商店 = `cursor_error`/`cursor_switch`（开窗口 `cursor\windowopen.wav`）。

## 3.5 验证记录（素材到位后实测）

**① 逐条 Test-Path（57/57 全真）**：见 `tools/probes/hosts/audiocheck` 的「素材到位自检」段（机器可复现版：逐键查存在 + 非空 + `RIFF/WAVE` 头 + 出处覆盖），
现测输出（2026-09-26）：
```
[ OK ] SfxRegistry 音效键数量 = SfxKeys + 本项目新增 9   (registry=53 sfxKeys=44)
[ OK ] 音效键 53 个：文件**全部**就位（逐条 Test-Path 全真）   (共 1676044 字节)
[ OK ] BGM 键 3 个：文件**全部**就位（逐条 Test-Path 全真）    (共 84594416 字节)
[ OK ] 全部素材都是合法 RIFF/WAVE 且非空（不是占位/空文件）    (57/57 合法)
```

**② Play 里真的出声**（口径 = 扫 Unity 真实 `AudioSource` 的"开始播"跃迁，不是"代码里调了 PlaySFX"）。
下面是**早期 24 键时代**的全键实机记录（键集已扩到 53，这条留作历史锚，**不代表当前 53 键全都在实机响过**）：
监听口径 = **扫 Unity 真实 `AudioSource` 的"开始播"跃迁**（不是"代码里调了 PlaySFX"）：

```
♪ 真的响过的 clip（25 种）：area_enterx2 bloodmoorx2 castx1 cast_coldx1 cast_firex1 cast_lightningx1
cast_poisonx1 dialog_openx1 footstepx1 gold_pickupx1 hitx2 item_pickupx2 item_usex1 level_upx9
missx95 monster_attackx100 monster_diex1 monster_revivex1 player_diex1 player_hurtx6 player_revivex1
portalx2 shop_openx1 townx2 ui_clickx3
AudioLog.MissingWarnCount=0        ← 与 Console 的「[Audio] 音效文件缺失」对应：一条都没有
```

七个验收点名触发点全部响过：**挥砍** `miss`×95 / **命中** `hit`×2 / **怪物叫** `monster_attack`×100 /
**拾取** `item_pickup`×2 + `gold_pickup`×1 / **升级** `level_up`×9 / **UI 点击** `ui_click`×3 / **进图** `area_enter`×2；
BGM 两首也都切到过（`town` / `bloodmoor`）。

**当前键集（53）的实机抽样**（2026-09-26 实机，读数 `.ai-tmp/test/fb_readings_fb4.txt`）：
`ui_click`×5（小面板两钮 + 技能页签/技能格 + 商店修理/关闭钮补齐后的起播跃迁）、
`monster_die_fa`×3、`monster_step_zm` 多次起播 ⇒ **逐类怪音链在实机上是通的（抽样，不是全键）**。

> **附：一处引擎侧静默失败（未触发，但值得修）** ——
> `Runtime/Resource/ResourceBackend.cs:125` 的 `ResourcesBackend.BeginLoad` 只靠 `req.completed += …` 交付结果；
> 若该路径**同一帧内先被同步 `Resources.Load` 取过**，`Resources.LoadAsync` 的请求会"asset 已有、`isDone` 永不置真、`completed` 也不触发"
> ⇒ `ResourceManager` 的 pending 永远留在 `_inflight`，**该路径此后一直加载不出来**。
> 修法建议（一行）：`BeginLoad` 订阅后补 `if (req.isDone) onDone?.Invoke(req.asset);`（或让 `ResourceManager` 每帧兜一次 `pending.Operation.isDone`）。

## 4. 复现方法（导出脚本 + 校验）

```powershell
# ① 列出数据包内容（可解加密 mpq）
cd _assets_src
python storm.py list   _assets_src\d2mpq\d2sfx.mpq
# ② 全部 4699 条 Sounds.txt 条目在该包内的存在性索引（→ _sfx_index.txt）
python _index_sfx.py
# ③ 只导 SfxRegistry 登记过的键（文件名 = 键名 + .wav，落进 Resources/Clover/Sound/**）
python _extract.py
```

- 原版条目名 / 原版路径的**名字来源**：`Sounds.txt`（1.10 LOD）第 1 列 `Sound`、第 3 列 `FileName`。
- **当前可复跑的校验入口（在仓）**：`python tools/probes/mpq/sfx_provenance.py --root <仓库根>`
  ⇒ 逐键把 mpq 内原版 `.wav` 取出、与工程落地件做 **sha256 逐字节比对**，并把每个键的**真实调用点**从源码扫出来
  （非注释行 = "有消费方、不是空实现"）。实测 `RESULT=PASS  sfx_sha256_match=53/53`；
  台账落 `tools/probes/mpq/sfx-provenance.tsv`（入仓，mpq 不在盘时宿主据此自检）。
- 上面那段 `_index_sfx.py` / `_extract.py` 是**首次导出**（24+3 = 27 键时代）的一次性脚本，已不在仓 —— 复现请用上一条的
  `sfx_provenance.py`（它同时覆盖后来扩到 53 键的逐类怪音与 `ui_click`）。
- 每个落地文件都验过 `RIFF/WAVE` 头 + `fmt` 块（见上表"音频格式"列，全部合法），
  逐条 `Test-Path` + 非空 + 头合法由离线宿主 `tools/probes/hosts/audiocheck` 每次回归复跑（现测 57/57 合法）。
