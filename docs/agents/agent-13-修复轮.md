# agent-13 修复轮（**三个独立任务包，按 section 分派**）

> 来源：`docs/接线报告.md` 的未决 A~E + agent-10 未决 1 + agent-11 未决 1。
> **每个 agent 只做自己那一段**；不许碰别人的文件；不许改契约签名（除本节明确写"允许"的）。
> 通用前置仍按 `docs/agents/_common.md`（**尤其 §3.5**；自检必须用  离线宿主）。
> **回归要求**：改完必须跑 `tools/probes/hosts/fullcheck`（全量编译 + 全链路）与受影响的既有宿主，**全绿才算完成**。

---

## §A 交给 `agent-06`：**玩家主动攻击 / 悬停拾取 / 光标切换**（★ 阻断验收）

**问题**：`Events.AttackRequest`、`Events.HoverTargetChanged`、`Events.CursorChanged` **全工程没有发送方**
（只有订阅方与 `CursorKind` 枚举）⇒ Play 里**玩家根本无法主动攻击**，验收表 #12/#14/#19/#20/#25 必然不过。

**只许改**：`client/Assets/Scripts/Module/Player/**`、`Module/Camera/**`、`Module/Input/**`。

**要做**：
1. `InputReader` 补：把鼠标所在的**世界点**换算成「悬停目标」——
   - 命中怪物 → `HoverTarget{kind=Monster, id, gridX, gridY}`；命中地面物品 → `GroundItem`；命中 NPC → `Npc`；否则 `None`；
   - **目标来源不许自己遍历怪物数组**（那要 `using Module.Monster`，违反分层）：改为**通过事件请求 + 由 `App` 转发**，
     或让 `InputReader` 只发「我悬停在哪一格 + 该格的世界点」，由 `App/AppEventRouting` 补全 id 后再发 `HoverTargetChanged`。
     **在回报里说明你选了哪种，并保证 ②③ 自检仍为 0 命中。**
2. 左键**点击语义**（原版）：点怪物 → `Emit(Events.AttackRequest, id)` 并走过去（走位由 Combat 负责）；
   点地面物品/NPC → 保持现有 `MoveCommand` 意图（agent-08 已有兜底自动拾取/对话）；
   点空地 → `MoveCommand`（现有）。
3. 悬停时按 `CursorKind` 发 `Events.CursorChanged`（`Attack`/`Interact`/`Pickup`/`NoWalk`/`Default`）。
4. 原版 **`Shift` 站立攻击**：按住 `Shift` 点怪 → 不移动只攻击（`Emit(Events.AttackRequest, id)` 不带走位意图）。
5. 原版 **R 键跑/走切换**：`InputReader.RunTogglePressed` 已有 → 切换 `PlayerModule` 的移动速率
   （跑 = `GameConst.PlayerWalkSpeed`，走 = 其 1/2），并 `Emit(Events.HudDirty)`（HUD 只切贴图，已实现）。

**验收**：
- [ ] 离线宿主新增断言：三种悬停各产出正确的 `HoverTargetChanged` 载荷；`CursorChanged` 随之变化
- [ ] 点击怪物 → `AttackRequest` 恰好 1 次（带正确 id）；`Shift+点击` → 只发攻击不产生移动目标
- [ ] `R` 切换后 `PlayerWalkSpeed` 生效（断言实际速率减半）
- [ ] `tools/probes/hosts/fullcheck` + `tools/probes/hosts/playercheck` 全绿；②③④⑤ 自检 0 命中（③ 用锚定版）

---

## §B 交给 `agent-09`：**UI 侧三个事件补发 + 小地图数据**

**只许改**：`client/Assets/Scripts/UI/**`（`UiArt.cs` 可读不可改）。

**要做**：
1. `InventoryPanel`：点装备槽 → `Emit(Events.UnequipRequest, …)`（现在是 Warn+Toast）；
   背包内拖放 → `Emit(Events.MoveInInventoryRequest, …)`（现在是 Warn）；拖到面板外 → `Events.ItemDropRequest`（已有）。
   **事件的参数打包格式以 `Core/Events.cs` 里 agent-12 新加的注释为准**，不得自行发明第二套。
2. 商店入口：`NpcDialogPanel` 在"交易"选项上 → `Emit(Events.ShopOpenRequest, npcId)`（现在只显示提示行）。
3. `HudPanel`：打开 `MiniMapPanel` 时**必须传 `MinimapArgs`**（现在传 null，靠下一帧补发绕过）——
   改为 HUD 缓存最后一次 `MapGenerated` 的 `MinimapArgs` 并在 `Open` 时传入。
4. 死亡面板：复活后不再依赖 `PlayerDied` 关闭（若 `Events.Revived` 已可订阅，用它；否则保持现状并在回报里说明）。

**验收**：
- [ ] 离线宿主断言：4 条 Emit 路径各自产出正确事件与载荷（贴断言）
- [ ] `MiniMapPanel` 打开时 `OnOpen` 收到非 null `MinimapArgs`（断言）
- [ ] `tools/probes/hosts/fullcheck` + `tools/probes/hosts/uicheck` 全绿；②③④⑤ 0 命中（③ 锚定版）

---

## §C 交给 `agent-01`（骨架/Core 契约维护者）：**资源常量与设置键补正**

**只许改**：`client/Assets/Scripts/Core/**`（`ResPaths.cs` / `GameConst.cs`）。

**要做**：
1. **`ResPaths` 文件名补正**（agent-10 实测）：
   - `PanelBuySellButton` 的真实文件是 `buysellbtn.DC6.0.png`，`PanelGoldCoinButton` 是 `goldcoinbtn.dc6.0.png`
     ⇒ 常量改成**真实文件名**，并**新增帧名助手**（`Frame(path, index)` → `"{path}_{index}"`，用于 `Multiple` 切分后的子资源名）；
   - 新增 `PanelOverlap`（`overlap.png`）；
   - 5 个条带的**帧数**也要有常量（实测：`buysellbtn` 22 帧、`goldcoinbtn` 2、`overlap` 2、
     `button_medium` 3、`button_wide` 3），供 UI 按下标取帧。
2. **`Log` 的无时钟降频**：`Core/Log.cs` 的 `WarnOnce/WarnThrottled` 依赖 `UnityEngine.Time.realtimeSinceStartup`，
   在离线宿主里抛 `SecurityException`（多个 agent 已被迫各自实现一套）。**给 `Log` 增加不依赖时钟的实现**
   （例如可注入的 `Func<float>` 时钟，默认取 Unity，离线宿主可注入自己的），并**保留现有签名不变**；
   在文件注释里写明"离线宿主请注入时钟"。
3. **`GameConst` 补两个静音设置键**：`SettingKeyBgmMute` / `SettingKeySfxMute`（agent-11 要持久化静音）。
4. **`GameConst.UiReferenceWidth/Height`**：引擎实际固定 1920×1080 ⇒ **改成 1920/1080** 并在注释里写明出处
   （`clover-client-unity-engine/Runtime/Presentation/UI.cs:52-56`），避免后续 AI 按 1280×720 布局。

**验收**：
- [ ] 5 个条带常量指向**真实存在的磁盘文件**（逐个 `Test-Path` 断言，贴输出）
- [ ] `Log` 的新旧两种用法都能编过，且离线宿主下 `WarnOnce` 不再抛异常（新断言）
- [ ] `GameConst` 四个新常量存在且被注释说明
- [ ] `tools/probes/hosts/fullcheck` + 全部 7 个宿主全绿；②③④⑤ 0 命中
- [ ] 回报里列出 `ResPaths` 的**最终常量名与值**（UI agent 后续照它取帧）

---

## §D 遗留（**本轮不做，登记等主 agent 裁决**）

| 项 | 说明 |
| --- | --- |
| `IItemModule` 缺 `Move/Swap` | `MoveInInventoryRequest` 只能在 UI 侧发出、门面无法执行 ⇒ §B 先接事件，执行留给后续 |
| `ISaveModule` 静音持久化 | 依赖 §C 的第 3 项，之后由 agent-11 收尾 |
| `MapModule.SetFogOfWar(true)` | 验收表若要求"未探索黑幕"，在 `App` 侧加一行；当前关闭 |
| 官方素材（角色/怪物/地形/音效） | 后台分块下载中；到位后由新 agent 做"解包 → 转 PNG/WAV → 落 `ResPaths` 目录 → 只换资源" |
| `MinimapArgs` / `SkillDef` DTO 缺字段（系名、职业名、sellPrice、slot） | 需要契约新增字段，**主 agent 裁决后再动** |
