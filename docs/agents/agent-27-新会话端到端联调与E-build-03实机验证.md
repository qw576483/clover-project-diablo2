# agent-27：新 Play 会话端到端联调 + E-build-03 实机验证（收尾轮 C）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级（首选）：`<项目根>/tools/ai-skill/SKILL.md` → 按需 `conventions.md` / `registry.md` / `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层 §0~§7 不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-ai-skill/SKILL.md`
- **必读**：`clover-engine` 的 §1.13（四拍 / **编译必须先成功** / 采样器先自检三条 / 读日志 `FileShare.ReadWrite`）、§2（`数值类` vs `表现类` 的证据口径）、`reference/visual-loop.md` **第八节·联络图**、§1.8（临时文件）、`reference/pipeline-and-unity-cli.md`（跑 Play / 截图 / 输入注入的坑）。
- **项目内必读**：`docs/agents/_common.md`；`client/_dev/p_runbg.cs`（`runInBackground` 配方，**已被验收表白名单，不许删改**）；`client/_dev/p_key3.cs`（已白名单的输入注入配方）。

## 1. 目标（一句话）

在**一个全新的 Play 会话**（不是上一棒那个被反复驱动的会话）里，**无任何绕行**地跑通首段端到端链路，并把结果采成**一张联络图**：

```
Boot 屏 → 主菜单 → SINGLE PLAYER → 创角（点 Amazon，`fw` 54 帧过渡播完）
        → 输入名字 → OK → 读条（LoadingPanel）→ 进图（Stage）→ ESC 暂停 → 回主菜单
```

同时**实机验证 E-build-03**（引擎 `Scene.cs` 的僵尸 op 修复）：场景异步加载必须在正常时间内完成（`[Flow]` 站点迁移照常推进），不许再出现"`LoadSceneAsync` 的 `progress` 恒 0 / 场景永不就绪"。

## 2. 任务边界

**只做**：
- 在 `<项目根>/.ai-tmp/test/` 写一次性探针 / 驱动脚本，跑实机、采证据（**回报前删掉**）
- 新增证据图：`client/Assets/Screenshots/p27_*.png` + **一张联络图** `p27_contact_flow.png`（+ 索引 `p27_contact_flow.index.tsv`）

**绝不做的**：
- ⛔ 不许改 `client/Assets/Scripts/**`、`clover-client-unity-engine/Runtime/**`、`Editor/**`（发现真缺陷 ⇒ 写进回报，由主 agent 决定）
- ⛔ 不许改 `策划/验收表.md`、`docs/**`、`tools/**`、任何 skill（E 编号登记由主 agent 写）
- ⛔ 不许读工作区里其它 `clover-project-*`；不许再派子 agent
- ⛔ 不许删改 `client/_dev/p_runbg.cs` / `p_key3.cs`
- ⛔ 不许写 `docs/交接-*.md` / 进度类文档

## 3. 前置依赖（已就绪，主 agent 实测）

| 事实 | 出处 |
|---|---|
| 编辑器活着 | `unity status --format json` ⇒ port 7800 / `project=…\client` / `state=ready` |
| **当前是编辑模式**（主 agent 刚 `editor_stop`） | 编辑模式 `SceneManager.sceneCount = 1`（主 agent 用 `eval` 只读探活得到）⇒ **上一个会话的僵尸场景已随 Play 容器释放** |
| E-build-03 已落盘并编译通过 | `Runtime/Presentation/Scene.cs`：停旧轮询前先 `pending.allowSceneActivation = true` + Warn；新增 `_progressOp` / `_progressScene` 自判归属。`recompile_status = completed / failed=false / errors=[]` |
| 上一棒的"场景加载瘫痪" | 发生在**同一个被反复驱动的 Play 会话**里：`Loading scene: Menu` 永不完成、连续 10 次采样 `progress = 0.000 / isDone=False`、`sceneCount=4`（Boot + 3 僵尸 Menu）。那一棒为了取证用了反射把 `SceneModule._currentScene` 置成 `Menu` 绕行 —— **本片一律不许再用任何绕行**：场景必须真的加载完成 |
| 创角屏证据（上一棒，已重采） | `Screenshots/p22_1_charcreate.png`(15:08:23) / `p22_2_transition.png`(15:08:31) / `p22_3_after.png`(15:08:38)；`p22_1` 与 `p23_1_after_newgame.png` SHA256 相同 |
| 状态机入口（探针用） | `Game.Event.Emit(Events.BootDone)`、`Events.Fsm.TriggerNewGame`；站点迁移日志唯一出口 = `Game.Fsm.OnChange → FlowLog.Station(to)` ⇒ 验收 grep `[Flow] →`（见 `docs/agents/_common.md` §3.5） |

> ⚠️ 真实入口名自己去源码查（`Core/Events.cs` / `App/` / `UI/`）——⛔ 不许编 API。点职业槽、输名字、点 OK、按 ESC、点 MAIN MENU 走**生产入口或真输入注入**（`p_key3.cs` 的配方），不许直接改面板字段。

## 4. 产出物

- `client/Assets/Screenshots/p27_contact_flow.png`：**联络图**（按 `reference/visual-loop.md` 第八节格式：N 格压一张，格上烧「格号 + 状态 + 关键数值」，脚本按格判定）
- `client/Assets/Screenshots/p27_contact_flow.index.tsv`：格号 ↔ 本行证据 ↔ 截图 ↔ 采样日志行
- 需要单张原图时另存 `client/Assets/Screenshots/p27_<状态>.png`（至少：主菜单 / 创角 / 读条 / 进图 / 暂停菜单）
- 探针 / 驱动脚本：`<项目根>/.ai-tmp/test/`（**回报前删除**）
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查，全过才算完成）

- [ ] **场景加载正常（E-build-03 实机回归）**：`[Flow] → MainMenu` / `[Flow] 菜单场景 Menu 已就绪`（文案按源码实际写的）在**正常时间内**出现；`Game.Scene.CurrentScene` 依次为 `Menu`（主菜单）→ `Stage`（进图）；**没有任何绕行**（不许反射改 `_currentScene`、不许直接调面板内部方法越过生产入口）
- [ ] 整链走通：`fsm=Boot → MainMenu → CharCreate → (Loading) → Stage`，每步都有运行时日志行；`Events.Fsm.*` 触发点与实际站点一致
- [ ] 创角屏：点 Amazon ⇒ 日志 `创角：[过渡] 槽 0（Amazon）起播 fw 54 帧 @25fps` + `播完 54 帧 ⇒ 落「选中(转正面)」`；活动按钮含 `SpotAmazon` / `SpotBarbarian`
- [ ] 输名字 → OK ⇒ 进读条 ⇒ 进图（`Stage`），日志有 `[Flow]` 迁移 + 地图生成/进图相关行
- [ ] ESC ⇒ 暂停菜单（`timeScale=0`，用 `*Unscaled` 的那套照旧生效）⇒ 点 `MAIN MENU` 回主菜单
- [ ] **联络图**：`p27_contact_flow.png` 存在、脚本按格判定全 PASS（格上能查到格号 + 状态 + 关键数值），**你只读这一张汇总图**（⛔ 不逐张读原图）；读不到图像内容 ⇒ `BLOCKED：图像通道不可用`
- [ ] **连续两次进 Play 同结论**（§2 硬性判定第 3 条）；两次都要 `clear_console → editor_play` 起新会话（这是本片的关键：验证"新会话不再瘫痪"）
- [ ] `unity command console_status --format json` ⇒ `groundTruth.consoleErrors == 0`（读之前必须 `editor_stop → clear_console → editor_play → 跑链`）
- [ ] 临时文件清空（`.ai-tmp/test/` 只留 `dispatch-log.tsv`）
- [ ] 跑一次只读的 `tools/verify.ps1`，把 summary 行与逐行输出贴进回报

## 6. 约束

- **批次流水线（§1.13）**：探针一次写完 → 编译一次（确认成功）→ 跑一轮完整链 → **集中采一次联络图**；⛔ 不许"改一处跑一次 Play"。
- **采样器先自检（§1.13 第 5 条）**：`.ps1` 语法 0 错；**纯 ASCII**（要写中文就带 BOM，建议不写）；完成判据 = **被测程序自己写的标记 + 新增次数**；读日志必须 `FileShare.ReadWrite`（游戏独占持有日志）。
- **每条 `unity` 命令带 `--project-path client`**；进 Play 前先 `Application.runInBackground = true; QualitySettings.vSyncCount = 0;`（`p_runbg.cs` 配方），否则失焦节流会让场景加载看起来"卡死"（`constraints.md` #11 —— **这一条与 E-build-03 的现象极易混淆，必须先排除**）。
- 探针日志统一 `[P27]` 前缀；业务日志走 `Game.Logger.*`，⛔ 禁止裸 `Debug.Log` 打业务日志。
- 若**新的 Play 会话仍然瘫痪**（`progress` 恒 0、场景永不就绪）：**不要改任何代码**，立刻如实回报 —— 给出「采样点 + `LoadSceneAsync.progress` 序列 + `sceneCount` + 是否已排除 `runInBackground` 节流」四项证据，并说明结论是"需要用户重启编辑器"还是"引擎修复未生效"。
- 回报格式：`产出物（含联络图格号表）/ 自检（原始输出）/ 未决`。
