# 音效素材对照表（键 ↔ 原版条目 ↔ 原版文件 ↔ 触发点 ↔ 落地文件）

> 本表是 `SfxRegistry.cs`（键 → 文件名）与真实素材之间的**对照凭证**。
> 代码里的机器可读版本 = `SfxRegistry.Origins`（`SfxRegistry.Origin(key)` 可查，`ValidateOrigins()` 会自检）。
> 本表由**导出脚本的实际输出**生成（不是手抄），md5 / 字节数 / 音频格式都是实测值。

## 0. 三条口径

1. **落地文件名 = 键名 + `.wav`**（例：键 `hit` → `Sound/SFX/hit.wav`）。
   引擎按 `Resources.Load("Clover/Sound/SFX/{键}")` 取（`Runtime/Presentation/Sound.cs:107/63`），
   `SfxRegistry.SfxFiles` 里的期望文件名与之一致 ⇒ **触发点代码一行都没改**。
2. **内容是原版 `.wav` 的原字节**（未重采样 / 未转码 / 未裁剪 / 未剪短），
   来源 = 用户本机暗黑2 数据包 `_assets_src\d2mpq\d2sfx.mpq`（音效）与 `d2music.mpq`（音乐）。
3. **只导了 `SfxRegistry` 登记过的键**（24 SFX + 3 BGM）；`d2sfx.mpq` 里其余 2330 条 `.wav` 一条没导。

## 1. SFX（24 键 → `client/Assets/Resources/Clover/Sound/SFX/{键}.wav`）

| 键 | 原版条目名（Sounds.txt 第 1 列） | 原版文件（`d2sfx.mpq:data\global\sfx\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| `hit` | `impact_blade_swing_1` | `combat\impact\sword1.wav` | `SFX/hit.wav` | 22314 | 22050Hz 16bit 单声道 0.50s | `Module/Combat/DamagePipeline.cs:121`（命中且未致死，`SfxAt`） |
| `miss` | `weapon_1hs_small_1` | `combat\weapon\one hand swing small01.wav` | `SFX/miss.wav` | 11556 | 22050Hz 16bit 单声道 0.26s | `DamagePipeline.cs:199`（挥空，`SfxAt`） |
| `player_hurt` | `amazon_hit_1` | `combat\player\amazon\soft4.wav` | `SFX/player_hurt.wav` | 25068 | 22050Hz 16bit 单声道 0.57s | `DamagePipeline.cs:180`（玩家受伤未死） |
| `player_die` | `amazon_death_1` | `combat\player\amazon\death1.wav` | `SFX/player_die.wav` | 31916 | 22050Hz 16bit 单声道 0.72s | `DamagePipeline.cs:180`（玩家死亡） |
| `player_revive` | `necromancer_revive_target` ※1 | `skill\necromancer\revivetarget.wav` | `SFX/player_revive.wav` | 44154 | 22050Hz 16bit 单声道 1.00s | `AudioHook.OnRevived` ← `Events.Revived` |
| `monster_die` | `fallen_death_1` | `monster\fallen\death1.wav` | `SFX/monster_die.wav` | 42030 | 22050Hz 16bit 单声道 0.95s | `DamagePipeline.cs:121`（怪物致死） |
| `monster_attack` | `fallen_attack_1` | `monster\fallen\roar1.wav` | `SFX/monster_attack.wav` | 19530 | 22050Hz 16bit 单声道 0.44s | `Module/Monster/MonsterModule.cs:490`（怪物起手） |
| `monster_revive` | `fallenshaman_resurrect` | `monster\fallenshaman\resurrect.wav` | `SFX/monster_revive.wav` | 29930 | 22050Hz 16bit 单声道 0.68s | `MonsterModule.cs:554`（萨满复活同伴） |
| `cast` | `amazon_magicarrow_1` | `skill\amazon\magicarrow1.wav` | `SFX/cast.wav` | 30594 | 22050Hz 16bit 单声道 0.69s | `Module/Skill/SkillModule.cs:324`（`CastOf` 默认回落） |
| `cast_fire` | `monster_cast_fire` | `skill\sorceress\firecast.wav` | `SFX/cast_fire.wav` | 44744 | 22050Hz 16bit 单声道 1.01s | `SkillModule.cs:324`（火系） |
| `cast_cold` | `monster_cast_cold` | `skill\sorceress\coldcast.wav` | `SFX/cast_cold.wav` | 32154 | 22050Hz 16bit 单声道 0.73s | `SkillModule.cs:324`（冰系） |
| `cast_lightning` | `monster_cast_lightning` | `skill\sorceress\eleccast.wav` | `SFX/cast_lightning.wav` | 47628 | 22050Hz 16bit 单声道 1.08s | `SkillModule.cs:324`（电系） |
| `cast_poison` | `amazon_cast_poison` | `skill\amazon\poisoncast.wav` | `SFX/cast_poison.wav` | 43474 | 22050Hz 16bit 单声道 0.98s | `SkillModule.cs:324`（毒系） |
| `level_up` | `cursor_level_up` | `cursor\levelup.wav` | `SFX/level_up.wav` | 55672 | 22050Hz 16bit 单声道 1.26s | `AudioHook.OnLevelUp` ← `Events.LevelUp` |
| `footstep` | `light_walk_dirt_1` | `ambient\footstep\LightDirt1.wav` | `SFX/footstep.wav` | 15918 | 22050Hz 16bit 单声道 0.36s | `AudioHook.Tick` ← `Events.PlayerGridChanged`（每 2 格一步，3D 定位） |
| `item_pickup` | `item_pickup` | `cursor\pickup.wav` | `SFX/item_pickup.wav` | 1676 | 22050Hz 16bit 单声道 0.04s | `AudioHook.OnItemPicked` ← `Events.ItemPicked`（非金币） |
| `gold_pickup` | `item_gold` | `item\gold.wav` | `SFX/gold_pickup.wav` | 56796 | 22050Hz 16bit 单声道 1.29s | `AudioHook.OnItemPicked` ← `Events.ItemPicked`（金币） |
| `item_use` | `item_potion_drink` | `item\potiondrink.wav` | `SFX/item_use.wav` | 21226 | 22050Hz 16bit 单声道 0.48s | `AudioHook.OnItemUsed` ← `Events.ItemUsed` |
| `ui_click` | `cursor_button_click` | `cursor\button.wav` | `SFX/ui_click.wav` | 3302 | 22050Hz 16bit 单声道 0.07s | `AudioHook` ← `PanelToggleRequest` / `DialogOptionChosen` / `ShopBuyRequest` / `ShopSellRequest` |
| `dialog_open` | `cursor_select` ※2 | `cursor\select.wav` | `SFX/dialog_open.wav` | 22444 | 22050Hz 16bit 单声道 0.51s | `AudioHook.OnDialogOpen` ← `Events.DialogOpen` |
| `shop_open` | `cursor_error` / `cursor_switch` ※2 | `cursor\windowopen.wav` | `SFX/shop_open.wav` | 3302 | 22050Hz 16bit 单声道 0.07s | `AudioHook.OnShopOpen` ← `Events.ShopOpen` |
| `portal` | `player_townportal_cast` | `skill\misc\portalcast.wav` | `SFX/portal.wav` | 85368 | 22050Hz 16bit 单声道 1.93s | `AudioHook.OnExitEntered` ← `Events.ExitEntered` |
| `area_enter` | `object_stairs` | `object\stairs.wav` | `SFX/area_enter.wav` | 22414 | 22050Hz 16bit 单声道 0.51s | `AudioHook.OnStageEntered` ← `Events.StageEntered` |
| `quest_complete` | `cairn_success` ※3 | `object\cairnsuccess.wav` | `SFX/quest_complete.wav` | 117112 | 22050Hz 16bit 单声道 2.65s | `AudioHook.OnQuestCompleted` ← `Events.QuestCompleted` |

## 2. BGM（3 键 → `client/Assets/Resources/Clover/Sound/BGM/{键}.wav`）

| 键 | 原版条目名 | 原版文件（`d2music.mpq:data\global\music\` 下） | 落地文件 | 字节 | 音频格式 | 触发点 |
| --- | --- | --- | --- | --- | --- | --- |
| `town` | `music_town_1` | `act1\town1.wav` | `BGM/town.wav` | 21805760 | 22050Hz 16bit 立体声 247.2s | `AudioHook.PlayAreaBgm` ← `Events.AreaChanged`(Town) / `Events.StageEntered` |
| `bloodmoor` | `music_wilderness` | `act1\wild.wav` | `BGM/bloodmoor.wav` | 42270644 | 22050Hz 16bit 立体声 479.3s | `AudioHook.PlayAreaBgm` ← `Events.AreaChanged`(BloodMoor) |
| `denofevil` | `music_caves` | `act1\caves.wav` | `BGM/denofevil.wav` | 20518012 | 22050Hz 16bit 立体声 232.6s | `AudioHook.PlayAreaBgm` ← `Events.AreaChanged`(DenOfEvil) |

## 3. 三处"没有原版一一对应"的说明（**不许含糊**）

- **※1 `player_revive`**：原版**没有**"玩家复活"专用音效条目（`Sounds.txt` 里 `player_*` 只有 `player_townportal_cast/_enter`）。
  取语义最近的**原版音** `necromancer_revive_target`（死灵法师·重生 的目标音）。
- **※2 `dialog_open` / `shop_open`**：原版开对话/开商店**不播专用音**（原版这部分是 NPC 语音，属 `d2speech.mpq`，本阶段不做语音）。
  取原版 UI 音：对话框 = `cursor_select`（点选），商店 = `cursor_error`/`cursor_switch`（开窗口 `cursor\windowopen.wav`）。
- **※3 `quest_complete`**：原版条目 `cursor_questdone` 指向 `cursor\questdone.wav`，
  但该文件**不在本机 `d2sfx.mpq` 里**（已用"把 `Sounds.txt` 全部 4699 条逐个探测该包"的办法确认：`FOUND=2262 / MISSING=2437`，
  `cursor\questdone.wav` 属 MISSING）。改用同语义的**原版任务完成音** `cairn_success`（`object\cairnsuccess.wav`，石冢任务达成音）。

## 3.5 验证记录（素材到位后实测）

**① 逐条 Test-Path（27/27 全真）**：见 `.ai-tmp/hosts/audiocheck` 的「素材到位自检」段（机器可复现版：逐键查存在 + 非空 + `RIFF/WAVE` 头 + 出处覆盖），
输出 `[ OK ] 音效键 24 个：文件全部就位 / [ OK ] BGM 键 3 个：文件全部就位 / [ OK ] 27/27 合法 / [ OK ] 每个键都登记了原版出处`。

**② Play 里真的出声（27/27 键实际播过）**：取证脚本 `client/_dev/p_a11_audio2.cs`，原始报告 `client/_dev/a11_report.txt`。
监听口径 = **扫 Unity 真实 `AudioSource` 的"开始播"跃迁**（不是"代码里调了 PlaySFX"）：

```
♪ 真的响过的 clip（27 种）：area_enterx2 bloodmoorx2 castx1 cast_coldx1 cast_firex1 cast_lightningx1
cast_poisonx1 denofevilx1 dialog_openx1 footstepx1 gold_pickupx1 hitx2 item_pickupx2 item_usex1 level_upx9
missx95 monster_attackx100 monster_diex1 monster_revivex1 player_diex1 player_hurtx6 player_revivex1
portalx2 quest_completex1 shop_openx1 townx2 ui_clickx3
AudioLog.MissingWarnCount=0        ← 与 Console 的「[Audio] 音效文件缺失」对应：一条都没有
```

七个验收点名触发点全部响过：**挥砍** `miss`×95 / **命中** `hit`×2 / **怪物叫** `monster_attack`×100 /
**拾取** `item_pickup`×2 + `gold_pickup`×1 / **升级** `level_up`×9 / **UI 点击** `ui_click`×3 / **进图** `area_enter`×2；
BGM 三首也都切到过（`town` / `bloodmoor` / `denofevil`）。

> **附：一处引擎侧静默失败（本轮未触发，但值得修）** ——
> `Runtime/Resource/ResourceBackend.cs:125` 的 `ResourcesBackend.BeginLoad` 只靠 `req.completed += …` 交付结果；
> 若该路径**同一帧内先被同步 `Resources.Load` 取过**，`Resources.LoadAsync` 的请求会"asset 已有、`isDone` 永不置真、`completed` 也不触发"
> ⇒ `ResourceManager` 的 pending 永远留在 `_inflight`，**该路径此后一直加载不出来**（复现：`client/_dev/p_a11_probe2.cs` + `a11_probe2.txt`）。
> 修法建议（一行）：`BeginLoad` 订阅后补 `if (req.isDone) onDone?.Invoke(req.asset);`（或让 `ResourceManager` 每帧兜一次 `pending.Operation.isDone`）。
> 本轮**没有改引擎**（任务书限定只许改 `Module/Audio/**`），交给主 agent 归口。

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
- 导出前**逐条探测**过 MPQ 内路径（`data\global\sfx\` + FileName / `data\global\music\` + FileName），27/27 命中。
- 每个落地文件都验过 `RIFF/WAVE` 头 + `fmt` 块（见上表"音频格式"列，全部合法）。
