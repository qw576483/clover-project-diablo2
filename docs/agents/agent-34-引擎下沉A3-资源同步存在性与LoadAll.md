# agent-34：引擎下沉 A3 —— 资源"同步存在性 + LoadAll"（消掉项目 E1 例外）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：
  - 项目级：`<项目根>/tools/ai-skill/SKILL.md` → `registry.md` / `conventions.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可被项目级覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`clover-engine` 的 `patterns/engine-fix.md`、§2（成本闸门：**进 Play 要记账**）、§3（四拍）、§4（证据按类别）。

## 1. 目标（一句话）

给引擎 `IResourceManager` **纯新增**两个能力 —— ① **同步**回答"这条资源在不在" `Exists(path)`；② **同步批量取** `LoadAll<T>(path)` —— 然后把项目里**为绕开 `Game.Res` 而直接调 `Resources.LoadAll/Load`** 的 4 个文件切到引擎 API，**消掉验收表里的 E1 例外**。

**为什么**：项目 E1 例外（`策划/验收表.md` 的 E1 行）记录了根因 —— ① 条带子 sprite 按名 `Load<Sprite>(...)` 取不到，只有 `LoadAll` 能拿到；② `D2Icon` 需要**同步**回答"这张原版图到底在不在"，而引擎资源模块只有异步 + `TryGet`（只能取"已驻留"的）。这是**引擎能力缺口**，不是业务偷懒。

## 2. 任务边界

**只做**：
- 引擎：`IResourceManager`（`Runtime/Core/Contracts.cs:1019`）**新增两个方法**；`Runtime/Resource/ResourceManager.cs` 与后端（`ResourceBackend.cs` / `AssetBundleBackend.cs`）**新增对应实现**（⛔ 不改既有方法签名）
- 项目：把 4 个文件里绕开 `Game.Res` 的直接 `Resources.LoadAll/Resources.Load<Sprite>` 改为引擎 API（清单：`UI/UiArt.cs`、`UI/D2Icon.cs`、`UI/D2Text.cs`、`Core/ClientConfig.cs` —— 以实际 grep 结果为准；`UiArt.cs:913-915` 的注释也要同步）
- 验收表 E1 行：**不要改**（⛔ 文档归主 agent），把"已可移除"的结论写进回报

**绝不做的**：
- ⛔ 不许改 `IResourceManager` 既有方法签名；⛔ 不许改引擎其它模块
- ⛔ 不许改 `tools/ai-skill/**`、`策划/**`、`docs/**`、任何全局 skill
- ⛔ 不许读其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许写交接/进度类文档；⛔ 不许自己写 `# adjudicated:` 裁决行

## 3. 契约（主 agent 已定；实现细节按后端能力取证后落地）

```csharp
// Runtime/Core/Contracts.cs —— IResourceManager 纯新增（不改既有成员）
/// <summary>
/// **同步**回答"这条路径在不在可加载源里"。不触发加载、不阻塞主线程（可走 manifest / 路径索引）。
/// 后端无索引能力时允许降级为一次探测，并**按路径缓存**结果（同一路径不重复探测）。
/// </summary>
bool Exists(string path);

/// <summary>
/// **同步**批量取一个路径下的全部资源（用于"条带/图集子 sprite 按名取不到"的场景）。
/// 后端不支持（例如 AssetBundle 未声明清单）⇒ 返回空数组并 **Warn 一次**（⛔ 不许抛异常、不许静默）。
/// </summary>
T[] LoadAll<T>(string path) where T : UnityEngine.Object;
```

**实现要求**：
- `ResourceManager`：`Exists` 优先查已有索引/manifest（若有）；无索引则按后端能力探测并缓存；`LoadAll` 转发给后端。
- 后端抽象 `ResourceBackend`（+`ResourcesBackend` / `AssetBundleBackend`）：新增对应虚方法；`Resources` 后端用 `Resources.LoadAll<T>(path)`；`AssetBundle` 后端若无法支持 ⇒ 空数组 + 一次 Warn（并在注释里写清）。
- ⛔ 语义与既有 `TryGet` **不冲突**：`TryGet` = 只取已驻留、不加载；`Exists` = 只回答"在不在"、不加载不驻留；`LoadAll` = **会加载**（同步）—— 三者在 XML 注释里逐条写清区别，避免后人误用。

**项目侧切换**：把 `Resources.LoadAll` / `Resources.Load<Sprite>` 换成引擎 API（`Game.Res.Exists(...)` / `Game.Res.LoadAll<Sprite>(...)`），并**保持原有降级/兜底分支**（取不到时的纯色占位、Warn 只报一次等）**逐条不变**。

## 4. 产出物

- 引擎：`Contracts.cs`（+2 方法）、`ResourceManager.cs`、`ResourceBackend.cs`、`AssetBundleBackend.cs`（新增实现）
- 项目：4 个文件切到引擎 API + 注释同步
- 证据：`client/Assets/Screenshots/p34_ui_res.png`（**UI 表现类证据**：至少覆盖"面板背景 + 图标 + 位图字模"三类，能看出与原版素材一致）
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 引擎编译绿：`recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] **既有签名零变化**：`IResourceManager` 改前/改后的成员列表**字符级对照**：旧成员一字未动、只多出 2 个新成员（贴两串）
- [ ] `.ai-tmp/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`
- [ ] 项目侧 `grep -rn "Resources\.LoadAll\|Resources\.Load<" client/Assets/Scripts` = **0 命中**（E1 例外消失的证据）
- [ ] **UI 表现类证据（本片必须进 Play）**：一轮实机里至少覆盖 —— 面板背景（原版贴图）/ 物品图标（原版 `inv*`）/ 位图字模中文（`font30` 之类）三类都**正常显示**（不是纯色占位、不是默认字体）；把图存 `Screenshots/p34_ui_res.png` 并**自己读一遍**（读不到内容 ⇒ `BLOCKED`）
- [ ] **进 Play 记账**：`.ai-tmp/test/play-log.tsv` 追加本片每一行（含理由）；本片预算 **≤ 2 次**（若超 ⇒ 停下回报，⛔ 不许自己加）
- [ ] `consoleErrors = 0`（`editor_stop → clear_console → editor_play → 跑链 → 读`）
- [ ] 数值/逻辑类（离线）：加载路径切换后，**同一批 UI 元素的取图结果与改前一致**（能离线判的用宿主断言；不能判的写进回报）
- [ ] `git diff --stat -- clover-client-unity-engine` 只多出本片的新增/改动（列出并说明每处）
- [ ] 回报里明确写：**E1 例外是否可以移除**（给出 `grep` 0 命中的原文），以及**移除后仍需人判的部分**
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项）

## 6. 约束

- 批次流水线：① 只读取证（`IResourceManager` 全量成员 + 3 个后端实现 + 4 个文件的现有绕开写法 + E1 例外原文）→ ② 一次改完 → ③ 一次编译 + 全部宿主 → ④ **一次**进 Play 采 UI 证据（⛔ 不许逐项进 Play）。
- ⛔ 项目侧**只换资源访问方式**：取不到时的降级分支、日志、占位色**逐条保留**（那是已验收的行为）。
- 发现"引擎后端结构不支持 `LoadAll`"⇒ 按契约降级（空数组 + Warn 一次）并在回报里说明；⛔ 不许为此改公开签名。
- 回报格式：`产出物 / 自检（签名对照 + 编译 + 宿主 + grep 0 命中 + UI 图 + play-log + console）/ 未决`。
