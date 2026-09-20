# agent-25：创角屏证据重采 + console Error 清零（收尾轮 A）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用，不必读完）：
  - **项目级（首选）**：`<项目根>/tools/ai-skill/SKILL.md` → 再按需读 `conventions.md` / `registry.md` / `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层 §0~§7 不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-tools/ai-skill/SKILL.md`
  - 都找不到 → **回报调用方要路径**，⛔ 不许凭记忆写代码
- **必读章节**：`clover-engine` 的 §1.13（四拍批次 / 编译必须先成功 / 采样器先自检）、§1.11（收尾自检）、§2（证据按类别）、§1.8（临时文件只放 `.ai-tmp/test/`、用完即删）、§1.12（验不了就 BLOCKED）；`unity-cli` 的「`--project-path` / 连接与 Safe Mode」。
- **项目内必读**：`docs/agents/_common.md`、`client/Assets/Scripts/UI/CharCreatePanel.cs`（本轮观察对象）、`.ai-tmp/test/p23_run.ps1` + `p23_boot.cs` + `p23_go.cs`（驱动模板，**用后由你统一删除**）。

## 1. 目标（一句话）

在**当前代码**上重采创角屏的三张实机证据、并让本轮 Play 的 `consoleErrors` 归零 —— 因为上一棒的代码改动（`CharCreatePanel.cs` mtime `2026-09-19 13:46:15`）**晚于它自己的证据**（`p22_*.png` 13:45:49~53），按 SKILL §1.13 第 2 条那三张证据已作废、必须重采。

## 2. 任务边界

**只做**：
- 在 `<项目根>/.ai-tmp/test/` 写一次性探针 / 驱动脚本，跑实机、采证据（**回报前删掉**）
- 覆盖 `client/Assets/Screenshots/p22_1_charcreate.png`、`p22_2_transition.png`、`p22_3_after.png` 三张；**覆盖前**把旧文件另存到 `<项目根>/.ai-tmp/test/old_p22/` 并记录 SHA256（随清场删）
- 回报里给「旧 hash → 新 hash」对照 + `console_status` 原始输出

**绝不做的**：
- ⛔ 不许改 `client/Assets/Scripts/**`（发现真缺陷 ⇒ 写进回报；唯一例外见第 6 节最后一条）
- ⛔ 不许改 `策划/验收表.md`、`docs/**`、`tools/**`、`tools/ai-skill/**`、任何 skill（含全局与宿主安装副本）
- ⛔ 不许读工作区里**其它** `clover-project-*`（源码 / `tools/ai-skill/` / `策划/` / `docs/` / Editor 生成器 / 素材全算）；只许读**本项目**、引擎源码 `clover-client-unity-engine/Runtime/**`、`clover-doc/**`、skill 的 `patterns`/`scaffold`/`experience`
- ⛔ 不许再派生任何子 agent
- ⛔ 不许删 / 改 `client/_dev/p_runbg.cs`（长期驱动脚本，验收表白名单）
- ⛔ 不许写 `docs/交接-*.md` / `NEXT.md` / `docs/进度*.md`（未完成项只写在回报消息里）

## 3. 前置依赖（已就绪，主 agent 实测）

| 事实 | 出处 |
|---|---|
| 编辑器活着 | `unity status --format json` ⇒ port 7800 / `project=client` / `state=ready` |
| 当前 console 计数 | `unity command console_status --format json` ⇒ `counts.error=1 / warn=11 / log=291`，`groundTruth.consoleErrors=1`（**这是本轮要清零的对象**） |
| 已知 Error 之一 | `client/Logs/Editor.log` 13:43:56 三条 `[Error] [Resource] 加载失败：D2/UI/FrontEnd/-1/nu{1,2,3}_0` —— 停用槽去加载 `-1` 路径；片 22 已修（`SlotIndex` / `BindSlots` 加 `NoClass` 守卫、`BuildSpots` 传空路径、`PreloadPortraits` 跳过） |
| 已知 Error 之二 | 同文件 13:46:39 `[Error] [Logger] 日志写线程异常终止：ThreadAbortException` —— 编辑器停 Play 时的日志线程中断，**非游戏错误** |
| 待重采三张 | `client/Assets/Screenshots/p22_1_charcreate.png`(13:45:49) / `p22_2_transition.png`(13:45:50) / `p22_3_after.png`(13:45:53) |
| 观察对象 | `client/Assets/Scripts/UI/CharCreatePanel.cs`（mtime `13:46:15`）：片 22 决策 = 只做 **Amazon + Barbarian**、5 槽位几何保留、停用槽 = `NoClass(-1)`、过渡帧表 `amazon/fw=54, amazon/bw=30, barbarian/fw=64, barbarian/bw=19`、@25fps |
| 驱动模板 | `.ai-tmp/test/p23_run.ps1`（`editor_stop → clear_console → editor_play → eval_file _dev/p_runbg.cs → p23_boot.cs → p23_go.cs → 截图 → console --level error`）；探针里 `Game.Event.Emit(Diablo2.Core.Events.BootDone)` / `Emit(Diablo2.Core.Events.Fsm.TriggerNewGame)` |

> ⚠️ 驱动入口（点选某个职业槽）**自己去源码查真实入口**（`Core/Events.cs` + `UI/CharCreatePanel.cs` 的公开入口）——⛔ 不许编 API 名；片 22 的实机是"注入 A/C/C + 真鼠标点"那条路，用哪条由你按源码定，但**必须是生产入口**。

## 4. 产出物

- 覆盖后的三张：`client/Assets/Screenshots/p22_1_charcreate.png` / `p22_2_transition.png` / `p22_3_after.png`（mtime **必须** > `2026-09-19 13:46:15`）
- 探针 / 驱动脚本：`<项目根>/.ai-tmp/test/` 下（**回报前删除**，只留 `dispatch-log.tsv`）
- 回报（消息）：产出物路径 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查，全过才算完成）

- [ ] 三张图存在且 mtime > `2026-09-19 13:46:15`
- [ ] 日志（`client/Logs/Editor.log`，读法按 SKILL §1.13 第 5 条用 `FileShare.ReadWrite`）出现：`创角面板已打开：屏上可选 2 个职业`、`创角：[过渡] 槽 0（Amazon）起播`（`fw` / 54 帧 / @25fps）、过渡播完落「选中(转正面)」
- [ ] 本轮日志里**不出现** `FrontEnd/-1/` 加载失败
- [ ] 顺序 `editor_stop → clear_console → editor_play → 跑链 → 读`，`unity command console_status --format json` ⇒ `groundTruth.consoleErrors == 0`
- [ ] 活动按钮 dump = 只有 `Back` / `SpotAmazon` / `Confirm` / `SpotBarbarian`
- [ ] **连续两次进 Play 同结论**（SKILL §2 硬性判定第 3 条）
- [ ] 三张图**自己读过**：先做图像通道自检（拿一张已知内容的图确认真收到内容），读不到 ⇒ `BLOCKED：图像通道不可用`，⛔ 不许硬写画面描述
- [ ] 回报前 `.ai-tmp/test/` 只剩 `dispatch-log.tsv`（探针 / `old_p22/` / 临时日志全删）
- [ ] 跑 `powershell -NoProfile -ExecutionPolicy Bypass -File tools\verify.ps1`（**只读**，它不改任何东西），把 summary 行与逐行输出贴进回报

## 6. 约束

- **批次流水线（SKILL §1.13）**：探针**一次写完** → 编译一次 → 跑一轮链条 → **集中采证据**；⛔ 不许"改一处跑一次 Play"。
- **采样器先自检（§1.13 第 5 条）**：`.ps1` 语法 0 错（`Parser::ParseInput`）；**纯 ASCII**（含 CJK 必须带 BOM，建议干脆不写中文）；完成判据 = **被测程序自己写的标记 + 新增次数**（防读到上一轮旧标记），超时只当兜底；读日志必须 `FileShare.ReadWrite`。
- 探针日志统一 `[P25]` 前缀（便于检索）；业务日志走 `Game.Logger.Info/Warn/Error(tag,msg)`，⛔ 禁止裸 `Debug.Log` 打业务日志。
- 一切非预期分支必须留日志（兜底判空 / 资源加载失败 / 非法状态）。
- ⛔ 每条 `unity` 命令都带 `--project-path <项目根>/client`（或在 `client/` 目录里跑）—— 见 `clover-engine` §4 闸门 2b。
- 若发现真缺陷（例如 `-1` 加载失败复现、过渡不播）：**不要自己改业务代码**，写进回报「未决」。**唯一例外**：缺陷是单文件、净改动 ≤ 20 行、且属同一处的点状修复 ⇒ 可改，但必须在回报里逐行列出改了什么 + 为什么，并跑 `tools/verify.ps1` 证明硬规则仍 0 命中。

## 7. 回报格式（做完一次性回报，中途不播报）

```
产出物：<绝对路径清单 + 旧hash→新hash>
自检：<实际命令 + 原始输出关键行：console_status 原始 JSON、verify.ps1 的 summary 行、日志证据行>
未决：无 / <具体条目>
```
