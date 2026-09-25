# agent-03：暗黑2 原版素材落位 · 导入设置 · 资源欠缺清单

## 0. 技能（开工必做）

先读 `<项目根>/docs/agents/_common.md` 并照它执行。项目根 = `clover-project-diablo2`。
另必读：`<项目根>/策划/素材调研.md`、skill `reference/asset-sources.md`、`patterns/client/resource.md`。

## 1. 目标

把**已到手的暗黑2 原版素材**（UI 贴图 / 位图字体）按契约落进 Unity 工程的运行时路径，
并写好 `AssetPostprocessor` 保证导入参数正确（原版是 8bit 调色板像素图，默认导入会糊）。

## 2. 任务边界

- **只做**：`client/Assets/ThirdParty/Diablo2/**`（源素材留档）、`client/Assets/Resources/Clover/D2/{UI,Fonts}/**`（运行时）、
  `client/Assets/Editor/AssetImporter.cs`、`<项目根>/client/资源欠缺清单.md`。
- **绝不做**：不改任何 `Scripts/**`（除 `Assets/Editor/AssetImporter.cs`）；不生成预制体/场景；不改 manifest；
  不改契约文档；不改 `tools/ai-skill/`。**不许引入任何非暗黑2 的通用素材。**

## 3. 前置依赖（已就绪，**只许读这些**）

来源仓库（D2 原版素材，社区复刻工程 `mofr/Diablerie`）：

```
_assets_tmp\d2src\Diablerie\Assets\Images\ControlPanel\     (49 张：控制面板/血球/蓝球/经验条/菜单按钮/小面板按钮)
_assets_tmp\d2src\Diablerie\Assets\Images\Cursors\         Cursor.png
_assets_tmp\d2src\Diablerie\Assets\Images\Inventory\       6 张装备栏底图 inv_armor/inv_belt/inv_boots/inv_helm_glove/inv_ring_amulet/inv_weapons
_assets_tmp\d2src\Diablerie\Assets\Images\Menu\            6 张：main_screen / class_select_screen / load_screen / multi_player_screen / button_medium / button_wide
_assets_tmp\d2src\Diablerie\Assets\Images\Panels\          inventory.png / charstat.png / buysellbtn.DC6.0.png / goldcoinbtn.dc6.0.png
_assets_tmp\d2src\Diablerie\Assets\Images\Skills\          SkilliconAttack.png
_assets_tmp\d2src\Diablerie\Assets\Resources\Fonts\        font16.png / font24.png / font30.png / font42.png
_assets_tmp\d2src\Diablerie\Assets\StreamingAssets\data\local\font\  font16.DC6 / font24.DC6 / font30.DC6 / font42.DC6
```

## 4. 产出物

### 4.1 运行时素材（`client/Assets/Resources/Clover/D2/**`）

**注意**：`CloverRes.Init("Clover")` ⇒ `Game.Res` 的路径前缀是 `Resources/Clover/`，业务传相对路径（如 `D2/UI/healthbar`）。

| 目标路径 | 来源 | 说明 |
| --- | --- | --- |
| `D2/UI/Cursor/*` | `Images/Cursors/Cursor.png` | 光标（单张多帧时按帧切分，切分参数写进回报） |
| `D2/UI/Panel/*` | `Images/ControlPanel/*` + `Images/Panels/*` | 控制面板、血球 `healthbar`、蓝球 `manabar`、经验条、`inventory`、`charstat`、买卖/金币按钮 |
| `D2/UI/Menu/*` | `Images/Menu/*` | 主菜单屏、职业选择屏、载入屏、多人屏、按钮 |
| `D2/UI/EquipSlot/*` | `Images/Inventory/*` | 装备栏底图 |
| `D2/UI/SkillIcon/*` | `Images/Skills/*` | 技能图标 |
| `D2/Fonts/font16` … `font42` | `Resources/Fonts/*.png` | 位图字体表（**必须报告每张的宽度/高度与格子尺寸**，供 UI agent 切分） |

同时在 `client/Assets/ThirdParty/Diablo2/` 保留一份**原样副本**（含 `.DC6` 字体文件），便于将来替换。

### 4.2 `client/Assets/Editor/AssetImporter.cs`

`AssetPostprocessor`，按**目录**设置：
- `D2/**`：`filterMode = Point`、`textureType = Sprite`、`spritePixelsPerUnit = GameConst.PixelsPerUnit(=64)`、
  `alphaIsTransparency = true`、`textureCompression = Uncompressed`、`mipmapEnabled = false`；
- `D2/UI/**`：`spriteImportMode = Single`（若为多帧表则 `Multiple` + 明确切分参数）；
- `D2/Fonts/**`：`Multiple`，**按格子宽高切分**（先用图像工具/代码量出真实格子尺寸，写进注释）。
- **新增素材目录必须同步改这个处理器**（在文件顶部注释里写清这条）。

### 4.3 `<项目根>/client/资源欠缺清单.md`

按 skill `patterns/client/resource.md` 的表格模板，登记**尚未到位的素材**（角色/怪物/地形/物品/音效/BGM），
每行给出：资源名 / 类型 / 尺寸要求 / 当前状态（占位 or 待接入）/ 用户需提供 / 替换方法 / 影响范围 / 是否阻塞可玩。
并在文件顶部写清来源行（照 `策划/素材调研.md` §4）与"是否使用通用兜底素材：**否**"。

### 4.4 素材实测尺寸表

素材落位报告：每个目录的**文件数**、每张图的**实际像素尺寸**（至少 font 系列与血/蓝球/控制面板/菜单屏）、
字体表格子切分参数、**建议的九宫格边距**（用于按钮/面板拉伸）。

> 尺寸数据必须来自**真实读图**（用 PowerShell `System.Drawing` 或 Python PIL 读），**不许估**。

## 5. 验收标准

- [ ] `Resources/Clover/D2/**` 下文件数与来源目录一致（贴出 `Get-ChildItem -Recurse | Group-Object Directory` 汇总）
- [ ] 每张图尺寸实测并写入 `_dev/assetreport.txt`（贴出关键几行）
- [ ] `AssetImporter.cs` 存在且能通过 `dotnet build`（类型正确）
- [ ] `client/资源欠缺清单.md` 已生成，覆盖**角色/怪物/地形/物品/音效/BGM** 六类
- [ ] **未引入任何非暗黑2 素材**（回报里逐目录声明来源）
- [ ] 分层自检 ④⑤ 全 0 命中

## 6. 约束

- 原版素材是**暗黑2 的版权内容**，本项目非商用；只需在清单里记一行来源，**不要**为许可停下来问用户。
- 所有非预期分支必须打日志。
- 素材**文件名保持原样**（便于将来替换），只调整目录归类。
