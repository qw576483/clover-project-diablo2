# agent-05：Flow 模块 + 启动/菜单链路面板 + App 装配

## 0. 技能（开工必做）

先读 `<项目根>/docs/agents/_common.md` 并照它执行。项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：skill **`patterns/client/app-flow.md`**（本 agent 的主要范式）、`patterns/client/ui.md`、
`docs/步骤文档.md` §3.3（UI 签名）/§3.6（场景与面板）、`tools/ai-skill/constraints.md` #2 #7。

## 1. 目标

交付**交付三段式里的第①段**：启动画面 → 主菜单 → 角色选择 → 创建角色 → 读条进图 → 游戏内暂停 → 回主菜单 → **能再进一次**。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Flow/**`、`client/Assets/Scripts/UI/{BootPanel,MainMenuPanel,SettingsPanel,CharSelectPanel,CharCreatePanel,LoadingPanel,PausePanel}.cs`、
  `client/Assets/Scripts/App/**`。
- **绝不做**：不写其它 `Module/*` 的实现；不写其它面板；不碰 `Editor/`；不改 `Core/`（常量/事件/路径已冻结）；
  不改契约；不改 `tools/ai-skill/`。
- **App 装配**：`App/Bootstrap.cs` 只做 `Game.Launch` + `CloverRes.Init` + `CloverInput.Init` + 模块装配 + `Flow.Enter()`；
  **≤ 200 行**；其它模块通过 `AppContext` 注入（**未实现的模块用 `null` 容忍 + Warn**，等后续 agent 接上）。

## 3. 前置依赖（已就绪）

- `Core/{GameConst,Events,ResPaths,SceneNames,ClientConfig,Log}.cs`、`Module/Contracts.cs`（agent-01）
- `Module/Map` 的 `IMapModule`（agent-04，可能晚于本 agent → 用 `AppContext.Map` 可空）
- 配表：`class_c`（5 职业初始属性）
- **原版素材**（agent-03）：`ResPaths.D2Ui + "Menu/main_screen"`、`"Menu/class_select_screen"`、`"Menu/load_screen"`、
  `"Menu/button_medium"`、`"Menu/button_wide"`、`"Panel/healthbar"` 等 —— **路径常量已在 `ResPaths`，不要改**

## 4. 产出物

### 4.1 `Module/Flow/`

| 文件 | 内容 |
| --- | --- |
| `IAppFlow.cs` | 门面：`void Enter();` / `string CurrentState { get; }` / `void GoStage(AreaId area);` / `void BackToMain();` / `void QuitGame();` |
| `AppFlow.cs` | `internal` 实现：`Game.Fsm` 站点 + 面板/场景编排 |
| `FlowLog` | tag = `Flow`；**每次站点切换打一条 `[Flow] → <站点>`**（验收要抄日志） |

站点与迁移（**照 `docs/步骤文档.md` / `registry.md` 的表，不许改名**）：

```
Boot →(BootDone)→ MainMenu
MainMenu →(NewGame)→ CharSelect        // 无角色时 → CharCreate
MainMenu →(Continue)→ CharSelect
CharSelect →(NeedCreate)→ CharCreate
CharCreate →(Created)→ CharSelect
CharSelect →(EnterStage)→ Loading →(StageReady)→ Stage
Stage →(Pause)→ Pause →(Resume)→ Stage / →(ToMain)→ MainMenu
```

- **菜单类站点共用一个 UI 场景 `Menu`**（只切面板，**不切场景**，否则切界面要等读条、很假）；
- `Boot` 场景只放 `Bootstrap`；
- 进图：`Game.UI.Open<LoadingPanel>()` → `Game.Scene.Load(SceneNames.Stage, p => loading.SetProgress(p), () => { Close; Trigger("StageReady"); })`
  —— **进度必须真来自回调**；
- `LeaveStage()` **清场清单**（照 `app-flow.md` §5）：`Game.UI.CloseAll()` / `Game.Entity.ClearAll()` /
  `Game.Pool.ClearAll()` / `Game.Timer.StopScope("stage")` / `Game.Sound.StopAll()` / `Game.Event.Off(...)`（**同方法引用**）；
- `Game.Event` **没有句柄**：Flow 长驻，订阅一律用**具名私有方法**，不许用匿名 lambda（否则 `Off` 不掉）。

### 4.2 `App/`

| 文件 | 内容 |
| --- | --- |
| `Bootstrap.cs` | `MonoBehaviour`，`Start()` 里：`Game.Launch` → `CloverRes.Init("Clover")` → `CloverInput.Init()` → `CloverData.InitDataTable(...)`（若 agent-02 给出目录）→ 装配 `AppContext` → `Game.Fsm.Force("Boot")` → `Flow.Enter()`；`Update()` 只转发（引擎自驱，通常不需要）；**单实例守卫**（`FindObjectsByType<Bootstrap>() > 1` 自毁重复的） |
| `AppContext.cs` | 模块注册表：`IMapModule Map / IPlayerModule Player / …`（**全部字段**，照 `registry.md` 模块表）；`internal static AppContext I`；`void Tick(float dt)` 只转发已注册模块 |

### 4.3 `UI/`（流程面板，**一个文件一个类**）

| 面板 | 层 | 要点 |
| --- | --- | --- |
| `BootPanel` | Normal | 原版启动屏 + 任意键/点击继续 → `Emit(Events.BootDone)`；初始化提示文案 |
| `MainMenuPanel` | Normal | **原版 `main_screen` 贴图**；4 项：单人游戏 / 多人游戏 / 设置 / 退出；**多人项点击给 Toast「本版本未实装联机」并打 Warn**；「继续」按钮仅在有存档时可点（真判断） |
| `SettingsPanel` | Popup | 真能改：BGM/音效音量（`Game.Sound.SetVolume`）、全屏、WASD 开关；**存 `Game.Setting`**，重进仍在 |
| `CharSelectPanel` | Normal | **原版 `class_select_screen` 贴图**；列出存档角色（职业 + 名字 + 等级）；进入 / 删除（`Game.UI.Confirm` 二次确认）/ 新建 / 返回 |
| `CharCreatePanel` | Normal | 5 职业选择；名字输入；**四维属性点分配（加减按钮）**；实时预览生命/法力/耐力（用 `class_c` + 官方成长公式）；确定 → 建角 → 回选角 |
| `LoadingPanel` | System | 真进度条（**用锚点宽度，不要 `fillAmount`+空 sprite**，见 `constraints.md` #3）+ 提示文案；`SetProgress(float)` |
| `PausePanel` | Top | 继续 / 选项 / 保存并退出 / 回主菜单（二次确认）；**暂停用 `timeScale = 0`，自身定时器必须 `*Unscaled`** |

**UI 硬规则**（违反即返工）：
- 面板**预制体名 = 类名**，放 `Assets/Resources/UI/{类名}`（由 agent-10 的生成器产生；本 agent **只写脚本**，不手工造预制体）；
- 面板**只发/收事件**，**禁止** `using Diablo2.Module.*`（需要数据用 `OnOpen(param)` 传入）；
- 面板内容用 **`CloverEngine.UIFactory`** 代码搭（`Stretch` / `CreateCentered` / `CreateText` / `CreateButton`），
  **禁止**自写锚点/铺满工具；位置一律写成"相对父层中心的偏移"；
- **状态值一律 `OnOpen(param)` 里刷**，缺失参数要 `Log.Warn`；**禁止在 `Awake` 里读上下文**。

## 5. 验收标准

- [ ] `dotnet build` 本 agent 新增文件零报错
- [ ] `App/Bootstrap.cs` **行数 ≤ 200**（贴 `Measure-Object -Line` 输出）
- [ ] 站点迁移完整：`[Flow] → Boot/MainMenu/CharSelect/CharCreate/Loading/Stage/Pause` 各出现一次（贴日志）
- [ ] 每个面板都有**入口与返回路径**（无单向死路）
- [ ] 「设置」真能改且**重进后仍在**（贴 `Game.Setting` 读写日志）
- [ ] `LeaveStage()` 清场清单 7 项逐条实现（回报里逐条对照）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出 **`AppContext` 全部字段名与 `IAppFlow` 最终签名**（后续 agent 要用）

## 6. 约束

- 所有非预期分支必须打日志（读档失败、素材缺失、面板打开失败、参数缺失…）。
- 一个 `.cs` 一个类；**禁止** `GameObject.Find` / `FindObjectOfType`；模块依赖只经 `AppContext` 注入。
- 用户尚未打开编辑器 ⇒ **禁止**跑 `unity run/test/batchmode`；语法校验走 `dotnet build`。
