# 实机-C：验收表第 38 行「任务：邪恶洞穴」完整链（agent-26）

> 结论：**通过**。整条链（接取 → 找洞 → 清光 → 交任务 → 技能点 +1 → 日志完成）在**实机 Play** 里
> 一轮跑完，9 个环节各有**实机截图 + 对应的状态迁移日志行**。
>
> ★ **连续 3 轮**在实机里整链跑通（不是单次侥幸；抽出的关键行见 `client/_dev/log_a26_tworuns.txt`）：
> ① `16:46:08 STEP24 … 技能点 1 → 2（Δ=1） 断言=PASS` + `16:46:14 CHAIN DONE 技能点=2 任务=Done 奖励已领=True 进度=11/11`（修 Npc 前）
> ② `16:52:04 … 技能点 0 → 1（Δ=1） 断言=PASS` + `16:52:10 CHAIN DONE 技能点=1 任务=Done 奖励已领=True 进度=11/11`（修 Npc 后）
> ③ `16:58:13 … 技能点 0 → 1（Δ=1） 断言=PASS` + `16:58:33 CHAIN DONE 技能点=1 任务=Done 奖励已领=True 进度=11/11`（**全部改动后**，本轮截图的来源）
>
> 证据时间窗：`2026-09-17 16:57:49 ~ 16:58:33`（本地时间）。
> 原始日志：`client/_dev/log_a26_chain.txt`（本轮自落盘，带毫秒时间戳）+
> `client/_dev/log_a26_editor.txt`（从 `client/Logs/Editor.log` 抽出的 `[A26C]/[Quest]/[Npc]/[Player]/[Flow]` 行）。
> 截图目录：`client/Screenshots/a26_*.png`（1920×1080，`ScreenCapture.CaptureScreenshot` 由驱动脚本自拍）。

## 0. 本轮怎么跑的（可复现）

| 文件 | 作用 |
|---|---|
| `client/_dev/p_a26_chain.cs` | **一体机驱动**：一次 `eval_file` 跑完整条链（内部 `Game.Timer` 推进 + 自己截图），含引导/断点续跑/打断重试 |
| `client/_dev/p_a26_do.cs` | 手动单步驱动（`dump` / `click_npc` / `ui_click` / `key` / `move` / `travel` / `killall` …），调试用 |
| `client/_dev/p_a26_setup.cs` | 进 Play 后第一件事：`runInBackground` + InputSystem 后台/编辑器注入设置 |
| `client/_dev/a26_ensure.ps1` / `a26_step.ps1` / `a26_shot.ps1` / `a26_log.ps1` / `a26_crop.ps1` | 外部外壳（重启并进 Play / 单步 / 截图+缩图 / 拉日志 / 裁剪读图） |

跑法：`editor_stop → clear_console → editor_play → p_a26_setup.cs → p_a26_chain.cs`，然后看 `_dev/log_a26_chain.txt`。

⚠️ **本机 Unity 编辑器被多个 agent 共用**（同一 Play 会话里还有 A24/A25/B25 的驱动在跑）。
本轮实测被它们的 `Fsm.Trigger` / `ToMainMenuRequest` 打断过 5 次，因此一体机里加了：
`-1` 引导步（把流程推到 Stage）+ 打断检测（站点变了就自动重来）+ **断点续跑**（任务已 InProgress/ReadyToTurnIn 时按状态跳到对应环节）。

关键动作**全部走真实输入/真实出口**：
* 找 NPC = 鼠标点 NPC 屏幕坐标 → `Events.MoveCommand` → 走到 `TalkRange` 内由 `NpcModule` 自动交互开对话；
* 接取/交付 = 在对话面板按钮的**屏幕位置注入真实鼠标左键**（`InputSystem.QueueStateEvent(MouseState)`）；
* 开任务日志/技能树 = 注入真实 `Q` / `T` 键（`KeyQuestLog`/`KeySkillTree`，HUD 的 `OnUpdate` 轮询到）；
* 出城/进洞 = 传送到出口格邻格后**发 `MoveCommand` 真走最后一步** ⇒ `PlayerModule.CheckExit` → `ExitEntered` → `AppFlow.EnterArea`。

## 1. 逐环节证据

| 环节 | 日志行（原文，`log_a26_chain.txt`） | 截图（`client/Screenshots/`） |
|---|---|---|
| ① 进罗格营地 | `16:57:52 BOOTSTRAP 站点=MainMenu … ⇒ 推进流程`；`16:57:53 BOOTSTRAP 已就位：Stage（第 1 次尝试）区域=Town 任务=NotStarted 剩余=0`；`16:57:54 DUMP(进入罗格营地 baseline) 站点=Stage 区域=Town 玩家格=(15, 14) 技能点=0 任务=NotStarted`；`16:57:57 STEP1 走到阿卡拉身旁 ⇒ 对话面板已打开（用时 2.50s，玩家格=(27, 6)）` | `a26_01_town_baseline.png`（营地+主角）、`a26_04_quest_accepted.png`（阿卡拉对话条） |
| ② 任务日志出现条目 | `16:57:57 STEP2 点『接受任务』（真鼠标注入）'Accept' 屏幕=(865,214) 可交互=True`；`16:57:58 STEP3 『接受任务』成功：任务状态 InProgress（真鼠标点击生效）`；`16:58:03 STEP6 任务日志面板已打开（真按键 Q 生效）：任务=InProgress 进度=11 剩余=11` | `a26_05_questlog_inprogress.png`（读图结论：原版任务日志底图 + 位图标题「任務」＋`邪恶洞穴` / `状态：进行中` / `进度：0/11  剩余怪物：11`，无重复标题） |
| ③ 出城到血腥荒野 | `16:58:04 STEP7 走向出城口格 (5,7)`；`16:58:04 STEP7 已到血腥荒野：种子=1223529014 尺寸=57x70 障碍=469 出口格=2 洞入口=(56,36)` | `a26_06_bloodmoor.png` |
| ④ 进邪恶洞穴 | `16:58:05 STEP8 已进入邪恶洞穴：种子=1231992987 尺寸=40x63 障碍=2144 刷怪点=12 洞内剩余=13` | `a26_07_den_entered.png` |
| ⑤ 清光洞内全部怪物 | 每个怪先走真实普攻（`[Combat] [普攻] m#… 命中率… ⇒ 命中/未命中`），再用主 agent 允许的探针收尾：`STEP10 探针收尾 m#1025（堕落萨满）hp=4/6 ⇒ IMonsterModule.ApplyDamage（真实击杀链：抗性结算 → Kill → Events.MonsterKilled）`；收尾行：`16:58:06 STEP10 清怪完成：洞内剩余=0` | `a26_08_den_cleared.png` |
| ⑥ 状态变「可交付」 | `16:58:08 STEP11 任务状态=ReadyToTurnIn 可交付=True 剩余=0`；`DUMP(清怪后) … 任务=ReadyToTurnIn 剩余=0 可交付=True 目标=洞穴已清理干净，回罗格营地找阿卡拉复命。` | 同 `a26_08_den_cleared.png` |
| ⑦ 回城找阿卡拉交付 | `16:58:09 SHOT a26_09_back_town.png`（`DUMP(回到罗格营地) … 任务=ReadyToTurnIn 可交付=True`）；`16:58:10 STEP21 阿卡拉对话已打开（用时 1.25s）可交付=True`；`16:58:10 STEP22 点『交付任务』（真鼠标注入）'TurnIn' 屏幕=(1075,214) 可交互=True`；`16:58:13 STEP23 『交付任务』成功（真鼠标点击生效）：任务=Done` | `a26_09_back_town.png`、`a26_10_dialog_turnin.png`（读图结论：营地里阿卡拉对话条 + 选项「洞穴已经清理干净了（交付任务）」）、`a26_11_after_turnin.png` |
| ⑧ 奖励 +1 技能点 | `16:58:00 技能点前值=0` → `16:58:13 STEP24 交付结果：任务=Done 技能点 0 → 1（Δ=1，期望 +1） 断言=PASS`；`16:58:14 STEP26 技能树面板已打开 —— 技能点=1（面板里应能点出可学节点）` | `a26_12_skilltree.png`（读图结论：标题行 **「技能树 · 亚马逊　剩余技能点：1」**，三系节点均以金色边框呈现=可点） |
| ⑨ 任务日志标记完成 | `16:58:33 CHAIN DONE 技能点=1 任务=Done 奖励已领=True 进度=11/11`；`DUMP(任务日志（完成态）打开) … 任务=Done 剩余=0 目标=已完成：邪恶洞穴里的怪物已被清光。` | `a26_13_questlog_done.png`（读图结论：`邪恶洞穴` / `状态：已完成` / `已完成：邪恶洞穴里的怪物已被清光。` / `进度：11/11  剩余怪物：0` / 底部「任务已完成（奖励已领取）」） |

### 读图清单（本轮自己读过，均先裁剪/缩小，未读 1.5MB 原图）

`cr_05_questlog.png`、`cr_13_questlog_done.png`、`cr_10_dialog.png`、`cr_12_skilltree.png`、
`cr_12_skillhead.png`、`cr_01_town.png`、`cr_08_cleared.png`（都在 `client/_dev/`，由 `a26_crop.ps1` 生成）。

## 2. 本轮改的代码（都落在任务书给的归口里）

| 文件 | 改了什么 | 为什么（实机现象） |
|---|---|---|
| `client/Assets/Scripts/Module/Npc/NpcModule.cs` | 新增 `InTownForNpc()`；`FindNearest` / `Interact` 在**非罗格营地**直接拒绝（带日志） | ⚠️ **真缺陷**：非城镇区域 `IMapModule.NpcPoints` 是空列表 ⇒ `EnsureBuilt` 把 5 个 NPC 站位退化成 (0,0)。洞穴里一旦 `MoveCommand` 落点靠近 (0,0) 就会**误开阿卡拉的对话**。实机复现：进洞后弹出对话面板且带着"未清光"的旧参数 ⇒ 回城交付时『交付任务』按钮是灰的、真鼠标点击无效（上一轮只能靠 invoke 兜底） |
| `client/Assets/Scripts/UI/QuestLogPanel.cs` | ① 删掉压在原版位图标题条上的多余文本 `任 务 日 志`；② `required == 0`（还没进过洞）时进度/提示改写为「进度：—（尚未进入洞穴…）」/「还没进入洞穴 ⇒ 洞内怪物数尚未统计」，不再写"还有 0 只怪物/剩余 0" | 实机截图里：标题两行字叠在一起；刚接任务时显示「清理邪恶洞穴中的怪物（剩余 0）」+「洞内还有 0 只怪物，清光后才能交付」——**语义相反**（会被读成"洞已清空/可以交了"） |
| `client/Assets/Scripts/Module/Quest/DenOfEvilQuest.cs` | `Objective()` 的 `InProgress` 分支：`Required <= 0` 时不再拼「（剩余 0）」，改为「去血腥荒野的邪恶洞穴，把里面的怪物清光。」 | 同上（任务日志第一格就是这条 object ive 文本） |

> 上述三处都不动契约（消息号/字段/接口签名/常量/场景名/面板名），只改行为与文案。
> 未改 `Module/Quest/QuestModule.cs`（状态机本身实测正确：`NotStarted→InProgress→ReadyToTurnIn→Done`，奖励只发一次）。

## 3. 观察到的其它问题（**未改**，供主 agent 决定）

1. **清怪必须优先杀堕落萨满**：萨满会 `ConsumeCorpse` 复活小怪。第一次实机清怪时"剩余 5"反复不降
   （日志：反复击杀同一只 `巨兽`、计数不动）。一体机里已内建"优先萨满"策略；**原版玩法本来也该先杀萨满**，
   但如果想让"剩 1 只也能清完"，需要确认这不是模块缺陷。
2. **1 级空手 DPS 太低**：每次命中 1~2 点，30HP 精英怪要 ~20 次。11~13 只怪纯实打要 ~3 分钟。
   本轮按任务书允许的方式"探针临时加输出"（每只怪先真实普攻、再用 `IMonsterModule.ApplyDamage` 收尾），
   日志里两种路径都留了行。若要"纯实打"版本，需要更长时间的不被打断窗口（本机编辑器被多 agent 共用，做不到）。
3. **进洞前洞内怪物数不知为何已统计到**（`a26_05` 显示 `进度：0/11`，而 STEP0 的 dump 里 `剩余=0`，
   进洞时又是 13 只）。不影响本行判定（`CanTurnInDen` 要求"清光"），但**建议主 agent 复查**
   `MonsterModule.SpawnArea/CountInArea` 与 `QuestModule.EnsureRequired` 的时序（疑似上一局的怪没被 `DespawnAll` 清掉，
   或本次 EnterArea 之前已为 DenOfEvil 刷过一次）。
4. **`PanelToggleRequest` 偶尔 10~17s 不打开面板**（本轮 STEP28 重试了 20 次才打开任务日志）；
   同一时刻真实 `Q` 键也没生效 ⇒ 疑似 HUD `OnUpdate`/`Game.Input` 被别的 agent 的按键注入干扰（共用编辑器），
   也可能是真缺陷。本轮截图最终拿到了，故未深挖。

## 4. 未决

- 无"本行未完成"项：9 个环节全部有实机截图 + 状态迁移日志，且**同一轮**内完成（非拼接）。
- 上面 §3 的 4 条是**别的行/别的模块**的观察，已如实登记，未擅自扩大改动范围。
