# agent-10：Editor 工程生成器（场景 / 预制体 / BuildSettings）+ 素材切分修复

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `clover-project-diablo2`。
另必读：skill `reference/unity-cli.md` §7（场景创建与脚本挂载由 AI 自己做，不许丢给用户）、
`patterns/client/ui.md`（面板预制体 = `Resources/UI/{类名}`）、`patterns/client/app-flow.md` §4（场景划分）；
`client/Assets/Editor/AssetImporter.cs` 的导入参数表（素材实测尺寸，切分参数要看它）。

## 1. 目标

交付**一键生成工程资产**的 Editor 工具与**真实存在的场景/预制体**，让用户打开 `client/` 点 Play 就能跑：
`Boot` / `Menu` / `Stage` 三个场景 + 全部面板预制体（`Resources/UI/{类名}`）+ Build Settings 顺序。

## 2. 任务边界

- **只做**：`client/Assets/Editor/**`（生成器）、`client/Assets/Scenes/**`（场景）、`client/Assets/Resources/UI/**`（预制体）。
- **允许改**：`client/Assets/Editor/AssetImporter.cs`（agent-03 的）——**仅限"多帧条带切分"这一处修复**，
  必须把原逻辑保留并加注释说明改了什么。
- **绝不做**：不改 `Assets/Scripts/**`（业务代码是别人的）、不改 `Packages/manifest.json`、不改契约文档、不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪）

**全部面板类**（一个类一个文件，类名 = 预制体名）：

```
UI/（流程，agent-05）：BootPanel MainMenuPanel SettingsPanel CharSelectPanel CharCreatePanel LoadingPanel PausePanel
UI/（游戏内，agent-09）：HudPanel MiniMapPanel InventoryPanel CharacterPanel SkillTreePanel QuestLogPanel
                        NpcDialogPanel ShopPanel DeathPanel
App/：Bootstrap（唯一手动挂载的业务脚本）
```
辅助非面板类型（**不要给它们生成预制体**）：`UI/UiArt.cs` / `UI/UiBar.cs` / `UI/UiLog.cs` / `UI/D2Text.cs` / `UI/ItemTooltip.cs`

场景内容：
- `Boot`：空场景 + 一个 `Bootstrap` GameObject（挂 `App/Bootstrap.cs`）
- `Menu`：空场景 + `EventSystem`（由 `CloverInput.Init()` 运行时保证，但场景里也可预置一个）
- `Stage`：相机（`CameraRig` 由业务运行时挂或场景内预置）+ 「地图根」「实体根」两个空 GameObject + 一个 HUD Canvas

> 引擎的 `UIManager` 会自己建 Canvas（`Runtime/Presentation/UI.cs:52-56`，`referenceResolution` 固定 **1920×1080**）。
> **先读 `Runtime/Presentation/UI.cs` 确认它是否复用场景里已有的 Canvas**；若它自建，就不要在场景里重复放 Canvas，
> 并把结论写进回报。`Stage` 场景里的「地图根 / 实体根」由集成 agent 通过 `MapModule.AttachRoot` / `ViewModule.AttachRoot` 接上
> —— 你只需保证这两个空 GameObject **存在且命名固定**：`MapRoot` / `EntityRoot`。

## 4. 产出物

### 4.1 `client/Assets/Editor/ProjectBuilder.cs`

菜单 **`Diablo2/一键生成工程（场景 + 预制体 + BuildSettings）`**，功能：

1. **生成面板预制体**：为上面列出的**每个面板类**在 `Assets/Resources/UI/{类名}.prefab` 生成预制体：
   - 根节点 = 该面板组件 + `RectTransform`（`anchorMin=0 / anchorMax=1 / pivot=0.5 / offsetMin=offsetMax=0`，即铺满）；
   - 面板内容**不要**在预制体里摆（内容由脚本 `OnOpen` 里用 `UIFactory` 搭）——预制体只需是**可被加载的空壳**；
   - **一个 `.cs` 一个 MonoBehaviour**：用 `AddComponent(System.Type)` 按类名找类型，找不到要 `Debug.LogError` 并继续（不要整体失败）；
2. **生成场景**：`Boot` / `Menu` / `Stage`（内容见 §3），保存到 `Assets/Scenes/`；
3. **写 Build Settings**：`Boot`(0) → `Menu`(1) → `Stage`(2)；**幂等**（重复跑不重复加、不乱序）；
4. **幂等**：重复执行不产生重复节点、不覆盖用户手改过的已有预制体（已存在则跳过 + 日志）；
5. **日志**：每个生成动作打一条 `[D2.ProjectBuilder] …`；失败要 `LogError` 并能让整体以非零退出码结束
   （提供 `public static void GenerateFromCommandLine()` 供 `-executeMethod` 用）。

### 4.2 场景与预制体（**真实落盘**）

- `Assets/Scenes/Boot.unity` / `Menu.unity` / `Stage.unity`
- `Assets/Resources/UI/<16 个面板>.prefab`

### 4.3 `AssetImporter.cs` 的多帧条带修复（**只改这一处**）

导入参数表报告这三张是**多帧条带**、当前按 `Single` 导入导致取不到单帧：
- `UI/Panel/buysellbtn.DC6.0.png`（1024×64）
- `UI/Panel/goldcoinbtn.dc6.0.png`（64×32）
- `UI/Menu/button_medium.png`（384×35，**3 帧，帧宽 128**）与 `button_wide.png`（816×35，**3 帧，帧宽 272**）
- `UI/Panel/overlap.png`（256×128）

做法：在 `AssetImporter.cs` 里按**文件名 → 帧尺寸/帧数**的显式表把它设为 `Multiple` 并给出切分矩形，
**帧矩形依据必须来自实测**（读图找不透明列的边界，把脚本与结果写进回报），**不许猜**。
改完在回报里给出：每个文件的帧矩形列表 + 取帧方式（`ResPaths` 里是否已有对应常量）。

## 5. 验收标准

- [ ] `dotnet build`（用任意现有  宿主或 Unity 程序集）本 agent 新增文件**类型零错误**
- [ ] `Assets/Scenes/{Boot,Menu,Stage}.unity` **真实存在**（贴文件大小）
- [ ] `Assets/Resources/UI/*.prefab` **数量 = 16**（贴文件列表与数量）
- [ ] 预制体根节点确实铺满（贴出具名证据：读回 `.prefab` 文本里的 `m_AnchorMin/m_AnchorMax/m_OffsetMin/m_OffsetMax`）
- [ ] Build Settings 三个场景按序写入（贴 `EditorBuildSettings` 的读回结果）
- [ ] **重复跑一次生成器**：不再新增节点、不报错、日志数量减半以上（幂等自证）
- [ ] 多帧条带的帧矩形**实测**得出（贴分析脚本输出），且 `AssetImporter.cs` 已按它切分
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出：用户打开编辑器后**该点哪个菜单**、生成器**幂等性证据**、以及"未能在无编辑器环境下验证的部分"清单

## 6. 约束

- 用户尚未打开编辑器 ⇒ **禁止** `unity run` / `unity test` / `Unity.exe -batchmode`。
  若必须验证 Unity API 类型，用**离线编译**方式（照  引用 Unity 托管 DLL 的做法）。
- 生成器**不许**要求用户手动建物体/挂脚本。
- 所有非预期分支必须打日志。
