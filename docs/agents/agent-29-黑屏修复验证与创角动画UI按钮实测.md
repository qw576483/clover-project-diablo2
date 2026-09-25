# agent-29：黑屏修复验证 + 创角动画 / UI 按钮实测（诊断+修复轮）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件（命中即用）：
  - 项目级（首选）：`<项目根>/tools/ai-skill/SKILL.md` → 按需 `conventions.md` / `registry.md` / `constraints.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（**规则层 §0~§7 不可被项目级覆盖**）、`~/.codebuddy/skills/unity-cli/SKILL.md`
  - 兜底：`<仓库根>/clover-ai-skill/SKILL.md`
- **必读**：全局 skill §2（**成本闸门**）、§3（四拍）、§4（证据契约）；`reference/change-loop.md`；`reference/verify-template.md` 第 16~18 项；`reference/visual-loop.md` 第八节（联络图）；`docs/agents/_common.md`。

## 1. 目标（一句话）

在**修好 Timer id 0 墓碑之后**（主 agent 已改，见 §3），用**一轮实机**把用户报的三类问题一次验完并采一张联络图：

1. **黑屏**是否消失（= 新会话第一次 `Game.Scene.Load` 是否正常完成、主菜单是否打得开）
2. **创角选择动画**是否正确（点选职业 ⇒ `fw` 正向过渡；再点同槽 ⇒ `bw` 反向过渡；播完落 NU3/NU1）
3. **各种 UI 按钮**是否都能点、都有响应（逐项真点击 + 响应日志）

发现真缺陷就修（范围见 §2），修完只重采**受影响的格**。

## 2. 任务边界

**只做**：
- 在 `<项目根>/.ai-tmp/drivers/` 复用/改造既有驱动（`p28_drive.cs` / `p28_run.ps1` 是上一棒留下的，**先读再改，⛔ 不许从零重写**）；一次性产物放 `.ai-tmp/test/`
- 新增证据：`client/Assets/Screenshots/p29_contact_flow.png`（**一张联络图**）+ `p29_contact_flow.index.tsv`
- 修业务侧缺陷：`client/Assets/Scripts/UI/**`、`client/Assets/Scripts/Module/**`（若发现）

**绝不做的**：
- ⛔ **不许改引擎** `clover-client-unity-engine/**`（Timer / Scene 已由主 agent 修好；发现引擎问题 ⇒ 写进回报）
- ⛔ 不许改 `策划/**`、`docs/**`、`tools/**`、任何 skill
- ⛔ 不许读工作区里其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档
- ⛔ **不许跑 `tools/probes/interact/p_runbg.cs`**（原因见 §3 —— 它会掩盖本片要验的缺陷）；`runInBackground` / `vSyncCount` 在**你自己的探针里**设
- ⛔ 不许删改 `tools/probes/interact/p_runbg.cs`

## 3. 前置依赖（已就绪；主 agent 已实测/已改）

| 事实 | 出处 |
|---|---|
| **Timer id 0 墓碑已修** | `Runtime/Core/Timer.cs`：`private long _nextId = 1;`（0 = 无效哨兵）+ `Stop(long id)` 首行 `if (id <= 0) return;`。留痕 = `.ai-tmp/test/dispatch-log.tsv` 的 `# direct-fix:` 行（用户原话："修复黑屏…"） |
| **编译通过** | `unity command recompile_status` ⇒ `{"status":"completed","failed":false,"errors":[],"compilationFailed":false}` |
| 编辑器活着（**用户已重启过**） | `unity status --format json` ⇒ port **7802** / `project=…\client` / `state=ready` |
| E-build-03 在位 | `Scene.cs`：停旧轮询前先放行旧 op（`_progressOp` 字段在 `:17`） |
| 上一轮证据（**不要当本片证据**） | `p27_contact_flow.png`（15:53）是在**跑过 `p_runbg` 的会话**里采的 —— 它掩盖了 id 0 缺陷；本片必须在**不跑 `p_runbg`** 的会话里重验 |
| 缺陷机理（务必理解） | `Timer` 旧实现 id 从 0 起发；`SceneModule.Load` 无条件 `Stop(_progressTimerId)` 而该字段初值 = 0 ⇒ 墓碑命中"下一个拿到 id 0 的 timer"= 场景进度轮询 ⇒ 它在首次 Tick 被移除 ⇒ `progress` 停在 **0.9**、`allowSceneActivation` 永不置 true ⇒ 场景永不激活（黑屏）。`p_runbg.cs` 的 1s 心跳 timer 会**吃掉 id 0**，所以跑它=看不见这个缺陷 |

## 4. 产出物

- `client/Assets/Screenshots/p29_contact_flow.png` + `p29_contact_flow.index.tsv`（格号 ↔ 判据 ↔ 截图 ↔ 采样/日志行逐字）
- 驱动：`.ai-tmp/drivers/*`（**本轮保留**，交付前统一清）；一次性产物：`.ai-tmp/test/*`（回报前清空到只剩 `dispatch-log.tsv` / `play-log.tsv`）
- `play-log.tsv` 追加本片每一行（含理由）
- 回报（消息）：产出物 + 判据表 + 修掉的缺陷 + 未决

## 5. 验收标准（逐条自查）

- [ ] **黑屏消失（本片核心）**：**不跑 `p_runbg`** 的全新会话（`editor_stop → clear_console → editor_play`）里：
      `[Scene] Loading scene: Menu` → `[Flow] 菜单场景 Menu 已就绪` 在 **< 1 s** 内出现；主菜单面板**打开**；不再出现"`progress=0.9` 卡住 / `sceneCount` 累积僵尸场景"
- [ ] 整链走通（**零绕行**，生产入口）：`Boot → MainMenu → SINGLE PLAYER → CharSelect（有存档时）→ CharCreate`（无存档时直接进）`→ 输名字 → OK → Loading → Stage → ESC 暂停 → MAIN MENU`
- [ ] **创角选择动画**：点 Amazon ⇒ 日志 `创角：[过渡] 槽 0（Amazon）起播 \`fw\` 54 帧 @25fps` + `播完 54 帧 ⇒ 落「选中(转正面)」`；点 Barbarian ⇒ 对应帧数（64 帧）；**再点同一槽 ⇒ 反向 `bw` 过渡**（30 / 19 帧）并落回待机 —— 三项都要有日志行 + 图上能看到姿态变化
- [ ] **UI 按钮逐项实测**（每项：真点击 + 响应日志/状态变化，逐条列进回报）：
      ① Boot 屏"任意键/点击" ② 主菜单 4 项（SINGLE PLAYER / MULTIPLAYER / CINEMATICS / EXIT）③ 创角屏 BACK / OK / 2 个职业热点 ④ 选角屏行按钮 ENTER ⑤ HUD 上的按钮 ⑥ ESC 暂停菜单 + 其中 MAIN MENU / OPTIONS ⑦ 选项面板里的音量/画质/关闭
- [ ] **联络图**：`p29_contact_flow.png` 存在；格上烧「格号 + 状态 + 关键数值」；脚本按格判定；**你只读这一张汇总图**（读不到内容 ⇒ `BLOCKED：图像通道不可用`）
- [ ] `consoleErrors = 0`（`editor_stop → clear_console → editor_play → 跑链 → 读`）
- [ ] `play-log.tsv` 记账（本片 Play 次数 **≤ 2**；诊断轮可放宽但必须写明）
- [ ] 修掉的缺陷逐条列出（`文件:行` + 现象 + 判据）；**没修的写进「未决」**
- [ ] `.ai-tmp/test/` 清空到只剩 `dispatch-log.tsv` / `play-log.tsv`
- [ ] 跑一次只读的 `tools/verify.ps1`，把 summary 行贴进回报

## 6. 约束

- **批次流水线**（§3 四拍）：① 只读取证 + 改动清单 → ② 批量改（⛔ 不编译不截图）→ ③ 一次编译 + 离线预演 → ④ **集中出证据一次**。⛔ 不许"改一处进一次 Play"。
- **采样器先自检**：`.ps1` 语法 0 错 + **纯 ASCII**；完成判据 = 驱动自己写的 marker（跑前删）；读日志必须 `FileShare.ReadWrite`；探针日志统一 `[P29]` 前缀。
- 每条 `unity` 命令都带 `--project-path client`（**闸门 2b**）。
- 一切非预期分支打 `Game.Logger.Info/Warn/Error(tag,msg)`，⛔ 禁止裸 `Debug.Log` 打业务日志。
- 发现**引擎**缺陷 ⇒ 只写回报（⛔ 不许自己改引擎）。
- 回报格式：`产出物 / 判据表（格号-状态-期望-实测）/ 修掉的缺陷 / 未决`。
