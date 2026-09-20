# Clover × Diablo II

用 [Clover 客户端引擎](https://github.com/qw576483/clover-client-unity-engine) **1:1 复刻《暗黑破坏神 II》**的工程。

**复刻范围**：Act I —— **罗格营地 / 血腥荒野 / 邪恶洞穴** + 主线任务 1。

## 实机画面

| 罗格营地 | 血腥荒野 |
|---|---|
| ![罗格营地](docs/images/acc_30_town.png) | ![血腥荒野](docs/images/p41_07_wild_wide.png) |

| 背包 | 技能树 |
|---|---|
| ![背包](docs/images/acc_42_inventory.png) | ![技能树](docs/images/acc_44_skilltree.png) |

> 截图取自本工程复刻版的**实际运行画面**（非原版素材）。

## 怎么玩

1. Unity **6000.x** 打开 `client/`；
2. 进 Play：主菜单 → 建角色 → 进罗格营地。

## 工程结构

| 路径 | 内容 |
|---|---|
| `client/` | Unity 工程（复刻本体；`Library/` `Temp/` `Logs/` 等生成物不入库） |
| `策划/` | 复刻规格（`策划案/暗黑破坏神2参考规格.md`）、验收表、原版数值文档（item / monster / skill / class / level 等 10 类，含 `xlsx` + `txt`）、自审对比 |
| `tools/` | 判据与工具：探针宿主（`probes/hosts/`）、面板测量与切图脚本（`probes/measure/`）、打表配置、地图数据 |
| `docs/` | 接线报告、配表说明、步骤文档与并行任务书（`agents/`） |

## 版权声明

本项目**仅用于技术交流与学习**，**禁止用于任何商业用途**。

- 《Diablo II》的名称、角色、图像、音效、数据及其他内容，其著作权与商标权均归 **Blizzard Entertainment** 所有。
- 本项目仅以学习研究为目的在本地重现该游戏的部分内容，**不对任何第三方素材主张权利**；原版资源（`原版资源/`）不入库，需自行准备。
- 若权利人认为本项目存在不当之处，请联系删除。

## 相关仓库

| 仓库 | 说明 |
|---|---|
| [clover-client-unity-engine](https://github.com/qw576483/clover-client-unity-engine) | 客户端引擎（本工程的运行底座） |
| [clover-tools](https://github.com/qw576483/clover-tools) | 打表工具（本工程 `策划/数值文档` 用其生成配置代码） |
| [clover-doc](https://github.com/qw576483/clover-doc) | 框架文档 |
