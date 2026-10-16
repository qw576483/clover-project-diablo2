# tools/probes —— 离线自检与素材工具

把 `client/Assets/Scripts` 的**源码**直接编到 .NET 上跑真断言（数值 / 逻辑 / 布局常量 /
配表 / 存档原子性 / 引擎下沉后的行为），**不进 Unity Play、秒级出结论**。

## 目录

| 路径 | 内容 |
|---|---|
| `hosts/` | 离线自检宿主（见下），每个宿主一个目录 + 同名 `.csproj` |
| `mpq/` | MPQ 按名清单生成 / 解包 / 来源登记工具 |
| `measure/` | 像素与尺寸量法脚本（HUD 分区、按钮亮度、图标命中率等） |
| `refs/` | 参考裁图与机器可读登记产物（`*.tsv` / `*.txt`） |
| `drivers/` | 进 Play 的取证驱动（Unity C# 片段 + 调用脚本） |
| `gen_automap.py` | 生成 `Core/AutoMapCel.generated.cs`（自动地图 Cel 表） |
| `gen_portrait_frame_table.py` | 写回 `UI/UiLayoutFlow.cs` 标记区的创角屏转身帧矩形表 |
| `ds1_containers.py` | `.ds1` 文件结构解析 |

## 离线自检宿主

| 宿主 | 管什么 |
|---|---|
| `corecheck` | 常量 / 等距投影 / 事件名 / 工具类 |
| `dircheck` | 8 方向与朝向公式 |
| `playercheck` | 主角移动 / 属性 / 技能点 / 存档读档一致性 |
| `movecheck` | A* 寻路 / 走位回退 / 渲染次序 |
| `combatcheck` | 伤害公式 / 命中 / 抗性 / 技能效果份数 |
| `skillcheck` | 技能树 / 学习限制 / 投射物与特效表 |
| `itemcheck` | 物品生成 / 品质许可 / 词缀 / 掉落 |
| `npccheck` | NPC 对话 / 商店 / 修理 |
| `mapcheck` | 地图生成 / 瓦片引用 / 出口与接缝 |
| `savecheck` | 存档原子性 / 选角列表 / 损坏档 |
| `audiocheck` | 音效键注册与触发点 |
| `animcheck` | 动画帧键与帧数表 |
| `uicheck` | 面板布局 1:1 常量 / tooltip 文案 / 图标命中率 |
| `flowcheck` | 启动 → 主菜单 → 选角 → 进图链路 |
| `fullcheck` | 上述模块的整链回归 |
| `buildcheck` | 素材导入参数与多帧条带 |
| `tableverify` | 运行时表行数 == 源表行数 |

## 跑法

```powershell
# 全部宿主（严格串行）
powershell -NoProfile -File tools/probes/hosts/run_all_hosts.ps1

# 单个宿主
dotnet run --project tools/probes/hosts/uicheck/Uicheck.csproj -c Release
```

## 环境依赖

| 依赖 | 谁需要 | 不在位时 |
|---|---|---|
| Unity **6000.6.0f1** 的托管 DLL | 全部宿主 | 编不过。默认路径 `$(ProgramFiles)\Unity\Hub\Editor\6000.6.0f1\Editor\Data\Managed\UnityEngine\*.dll`，可用环境变量 `UNITY_EDITOR_ROOT` 覆盖（见 `hosts/Directory.Build.props`） |
| `client/Library/ScriptAssemblies/UnityEngine.UI.dll` | 编 `UI/**` 的宿主 | 编译期 CS0246 —— `Library/` 不入库 ⇒ 用编辑器打开一次 `client/` 重建即可 |
| `原版资源/` | `uicheck` 的两条「原版依据在盘上」断言 | 打 `[SKIP]`（`原版资源/` 不入库，干净检出必然没有） |

## 说明

本目录的文件是**可重判同一件事的证据**，不是一次性产物：

- 探针 / 驱动 / 量法脚本 / 参考裁图 / 台账一律入仓；
- 一次性的中间产物（截图、临时日志）只落 `.ai-tmp/`（gitignore）。
