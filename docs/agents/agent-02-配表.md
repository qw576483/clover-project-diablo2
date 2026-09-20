# agent-02：官方数据表 → 本项目配表 → 打表产物

## 0. 技能（开工必做）

先读 `<项目根>/docs/agents/_common.md` 并照它执行。项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：skill `patterns/table.md`（打表三步闭环与硬规则）、`<项目根>/docs/步骤文档.md` **§2 配表清单**。

## 1. 目标

把**暗黑2 官方数据表**（权威数值）转成本项目源表 `策划/数值文档/*_c.txt`，再打表出 C# 强类型代码 + tsv，
让后续模块**只读表、不硬编码**。

## 2. 任务边界

- **只做**：`<项目根>/tools/table-convert/**`（转换脚本）、`<项目根>/策划/数值文档/*_c.txt`、
  `<项目根>/tools/table/config.yaml`、打表产物落到 `client/Assets/Scripts/Table/**` 与运行时 tsv 目录。
- **绝不做**：不写任何 `Module/**` 实现；不碰 `UI/`、`App/`、`Editor/`；不改契约文档；不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪）

**官方原始数据表**（暗黑2 1.10f，从 mpq 解出，社区存档）：
`c:\Work\Server\full-dev\_assets_tmp\d2src\d2lod1.10txt\data\global\excel\*.txt`
（**92 个文件**；关键：`charstats.txt` `experience.txt` `MonStats.txt` `MonStats2.txt` `MonAi.txt` `AiParms.txt`
`Levels.txt` `MonLvl.txt` `skills.txt` `SkillDesc.txt` `SkillCalc.txt` `Weapons.txt` `Armor.txt` `Misc.txt`
`Inventory.txt` `MagicPrefix.txt` `MagicSuffix.txt` `MonUMod.txt` `TreasureClassEx.txt` `Missiles.txt`）

**打表工具**：`c:\Work\Server\full-dev\clover-tools\table\core`（`go run ./cmd/table -config <cfg> -batch`；
`-pack` 反向出 xlsx）。**先读 `clover-tools/table/README.md` 与 `cmd/table/main.go` 确认参数与产物路径规则**。

## 4. 产出物

### 4.1 `<项目根>/tools/table-convert/convert.py`（或 `.ps1`）

从官方 txt **程序化抽取**只属于 Act I 起始三区域的行，生成源表。**不许手抄数值。**

必须处理：
- 官方 txt 是 **tab 分隔、首行字段名**（编码 `cp1252`/`utf-8`，**先探测再用对应编码读**，读错会乱码）；
- 只保留用到的列；重新映射成项目自己的列名（见 `docs/步骤文档.md` §2 的关键列）；
- **显示名（中文）**：官方 1.10f txt 里是英文名。中文名由本项目编写（写在脚本里的映射表，标注"本项目新增"），
  写入表的 `name` 列；脚本要在注释里写清"官方 localized `.tbl` 到位后需复核"；
- 单位/公式换算要在脚本注释里写明（例：D2 的 `hp` 是"生命值"基值，需按 `MonLvl.txt` 的区域等级倍率换算）。

### 4.2 `<项目根>/策划/数值文档/` 下 10 张源表（**必须带 `_c` 后缀**）

`class_c.txt` `experience_c.txt` `monster_c.txt` `level_c.txt` `skill_c.txt` `item_c.txt` `affix_c.txt`
`monumod_c.txt` `treasureclass_c.txt` `missile_c.txt`

**格式（4 行表头，tab 分隔）**：

```
name	hp	atk
string	int	float32
c	c	c
名字	生命	攻击
亚马逊	100	10.5
```

硬规则（违反会**静默跳过整表/整列**）：
- 表名带 `_c`；第 1 列是主键且**非空**；类型只能用 `int/int32/int64/float32/float64/string/map[int]int/map[int]string/[]int/[]string/vector3`；
- 第 3 行端标记全部 `c`（单机只出客户端）；第 4 行中文注释；
- 复合类型：map 对间 `|`、kv 间 `;`；slice 元素间 `;`。

### 4.3 `<项目根>/tools/table/config.yaml`

```yaml
planning_dir: "../策划/数值文档"
client_format: "cs"
server_dir: "../_table_discard/server"
client_dir: "../../client/Assets/Scripts/Table"
default_side: "c"
mapping_file: "../_table_discard/mapping.tsv"
```
（路径**相对 config.yaml 所在目录**；单机不要服务端产物，`server_dir` 指向丢弃目录）

### 4.4 打表产物（**不许手改**）

跑 `go run ./cmd/table -config ../table/config.yaml -batch`（在 `clover-tools/table/core` 下），产出
`client/Assets/Scripts/Table/**`（`Registry.cs` + `Base/*.cs` + 上层 `<Logical>.cs` + tsv）。

**必须查清并把结论写进回报**：
1. 生成的 C# 表**运行时怎么读数据**（tsv 要从哪里加载？是否要 `CloverData.InitDataTable(dir)`？
   tsv 要放到哪个目录、是否必须在 `Resources` 下？）—— **去读生成器的源码**
   `clover-tools/table/core/internal/gen/cs.go` 与 `Runtime/Data/DataTable.cs` 得出结论，
   **不许猜**。若必须在 `Resources` 下，则把 tsv 目录也放到 `client/Assets/Resources/Table/` 并在回报里说明。
2. `Tables.Default.X.Get(id)` 的确切命名空间与调用形态（贴一段真实可编译的示例）。
3. 若打表工具因路径/配置报错，把**原始报错**贴进回报，不要自己改工具。

### 4.5 `<项目根>/docs/配表说明.md`

一张表：源表名 / 行数 / 关键列 / 生成物路径 / 运行时读取方式 / 待补项（未导入的行与原因）。

## 5. 验收标准

- [ ] 10 张 `_c.txt` 全部存在，4 行表头齐全，主键无空值无重复
- [ ] 打表命令**实际跑过**，输出里**没有"跳过"提示**；产物文件存在（贴出文件清单）
- [ ] `Tables.Default.<表>.Get(1)` 的调用示例**可编译**（在回报里贴出真实签名）
- [ ] 三处区域的怪物/物品/技能行数与官方 `MonStats.txt`/`Weapons.txt`/`skills.txt` 的对应行数对得上（给出数字）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出「运行时读取配表」的确切做法（后续 agent 照它写）

## 6. 约束

- 数值**一律来自官方 txt**，不许凭记忆/印象填；
- 官方表里 Act I 以外的行**不要导入**（保持表小、可读）；
- 所有非预期分支必须打日志或至少在脚本里 `print` 明确原因。
