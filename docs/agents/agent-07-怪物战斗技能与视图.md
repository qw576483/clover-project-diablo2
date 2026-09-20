# agent-07：Monster（AI/精英词缀）+ Combat（伤害/命中/死亡）+ Skill（技能树/施放）+ View（精灵视图）

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：`docs/步骤文档.md` §3.3、`tools/ai-skill/conventions.md`（等距坐标）、`constraints.md` #3 #5 #8；
skill `patterns/client/entity-view.md`、`reference/engine-mental-model.md` §4（**先查引擎能力表，别重造轮子**）。

## 1. 目标

实现 `IMonsterModule` / `ICombatModule` / `ISkillModule` / `IViewModule`：怪物 AI 与精英词缀、
**官方伤害与命中公式**、受击/死亡/复活、经验与掉落触发、5 职业技能树与施法、以及精灵视图（含**业务自写的逐帧 Sprite 动画**）。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Monster/**`、`Module/Combat/**`、`Module/Skill/**`、`Module/View/**`。
- **绝不做**：不改 `Core/`、`Def/`、`Module/Contracts.cs`、`App/`、`UI/`、`Editor/`、其它 `Module/*`；不改契约；不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪）

- `Module/Contracts.cs`：`IMonsterModule` / `ICombatModule` / `ISkillModule` / `IViewModule` + DTO `MonsterState` / `SkillDef` / `DamageArgs` / `SkillTreeArgs`
- `Core`：`Iso` / `Rng` / `Log` / `Events` / `GameConst` / `ResPaths` / `AStar`
- `IMapModule`（agent-04）：`MonsterSpawns`（**仅洞穴非空**；**野外用 `RandomWalkableTile(rng)`**）、`Walkable`、`FindPath`、`SpawnPoint`
- `IPlayerModule`（agent-06）：`Grid` / `Level` / `ApplyDamage` / `AddExp` / `AttackRating` / `Defense` / `GetResist`
- `IItemModule`（agent-08，可能晚于你）→ **只通过 `AppContext.I.Item` 可空调用**（null 时不掉落 + `Log.WarnOnce`）
- 配表：`monster_c`（`Hp/Ac/Ar/DmgMin/DmgMax/Exp/Ai/Speed/ResPhys…/Sprite/Level`）、`monumod_c`、
  `skill_c`（`Class/Tree/Name/ReqLevel/ReqSkill/ManaCost/DmgMin/DmgMax/DmgType/Target`）、`missile_c`、`level_c`

## 4. 产出物

| 文件 | 内容 |
| --- | --- |
| `Module/Monster/MonsterModule.cs` | `internal sealed class MonsterModule : IMonsterModule`（无参构造） |
| `Module/Monster/MonsterAi.cs` | 4 种 AI：`Melee`（追击近战）/ `Range`（保持距离射击）/ `Shaman`（**复活已死同伴**，有冷却）/ `Coward`（低血逃跑，原版堕落者行为） |
| `Module/Monster/MonsterSpawner.cs` | 按 `level_c.Monsters` + 区域（`MonsterSpawns` 或 `RandomWalkableTile`）刷怪；精英判定（`monumod_c` 的 champion/unique 概率）→ 词缀属性倍率 |
| `Module/Combat/CombatModule.cs` | `internal sealed class CombatModule : ICombatModule` |
| `Module/Combat/DamageFormula.cs` | **官方公式**，注释写清出处：物理伤害 `= (武器伤害 × (1 + 力量/100)) × 技能倍率`；命中率 `= f(AR, Def, 等级差)` 且**夹在 5%~95%**；元素伤害按抗性减免（抗性上限）、防御按公式减伤。**必须有确定性单测**（固定输入 → 固定输出） |
| `Module/Combat/DeathFlow.cs` | 死亡→经验结算→掉落触发→（玩家）死亡屏；怪物死亡保留尸体（`corpseUsable`）供 Shaman 复活 |
| `Module/Skill/SkillModule.cs` | `ISkillModule` 实现：技能树（5 职业 × 3 系）、`CanLearn`（前置技能 + 等级 + 点数）、`Learn`（扣 1 点）、`SelectSkill`/`AssignToButton`、`TryCast`（扣法力 + 冷却 + 目标判定） |
| `Module/Skill/Projectile.cs` | 投射物（火弹/冰弹）：沿格飞行、命中判定、到达/超时消失；数值取 `missile_c` |
| `Module/View/SpriteAnimator.cs` | **★ 业务自写的逐帧 Sprite 动画**（引擎 `Game.Anim` 是 Animator 驱动，不覆盖逐帧切图，见 `reference/engine-mental-model.md` §4）。**标注"本项目新增"** |
| `Module/View/ViewModule.cs` | `internal sealed class ViewModule : IViewModule`：创建/更新/销毁精灵视图（角色 8 方向、怪物、地面物品）、`PlayHit` 闪白、`PlayDeath`、飘字（`Game.UI.FloatText`）、头顶血条（`WorldHpBar`） |
| `Module/View/SpriteFrames.cs` | 帧加载：从 `ResPaths` 取帧序列；**取不到素材时用纯色占位 sprite**（按 `TileKind`/类型上色），并在 `client/资源欠缺清单.md` 登记（**只追加，不改别人的行**） |

**硬要求**

1. **怪物 AI 走 `IMapModule.FindPath`**（不许自己实现寻路），并做**路径节流**（每 N tick 或目标格变化 > 1 时重算）。
2. **仇恨有时效**（一段时间没被打就遗忘）+ 玩家离图/死亡时清仇恨（skill `3d-mmo-basics` §0 的实测坑）。
3. **精英怪要可见**：名字前缀/颜色/词缀名写进 `MonsterState.modName`，并让 HUD 能显示。
4. **命中反馈三件套**：飘字 + 音效钩子（`IAudioModule`，可空）+ **目标头顶血条下降**，三者必须同一次命中里都发生。
5. **`SkillTreeArgs` 必须给出真实的可学/锁定状态**（前端要按它渲染）。
6. **确定性**：伤害/命中公式的随机走注入的 `Rng`，可复现（单测用固定 seed）。

## 5. 验收标准（**必须用离线宿主**，照 `.ai-tmp/hosts/mapcheck/` 或 `.ai-tmp/hosts/flowcheck/` 建 `.ai-tmp/hosts/combatcheck/`）

- [ ] 离线宿主 `dotnet build` 0 错 0 警告；`dotnet run` 断言全过
- [ ] **伤害公式单测**：固定输入 → 固定输出（贴数字）；换抗性目标伤害下降；等级差改变命中率且被夹在 5%~95%
- [ ] **命中率统计**：固定 seed 打 1000 次，命中率落在期望区间（贴实测值）
- [ ] **4 种 AI 各演一次**：Melee 会靠近并攻击 / Range 保持距离 / Shaman 复活了同伴（贴 `[Monster] revive` 日志）/ Coward 低血逃跑
- [ ] **精英怪**：刷出 champion/unique，属性按 `monumod_c` 倍率放大（贴普通 vs 精英的 hp/dmg 数字）
- [ ] **死亡链**：怪物死亡 → 经验给玩家 → 掉落被触发（`IItemModule` 为 null 时有 WarnOnce）（贴日志序列）
- [ ] **掉落位置合理**：所有掉落格 `Walkable == true`（贴断言）
- [ ] **技能树**：5 职业 × 3 系的 `SkillTreeArgs` 生成成功；`CanLearn` 在点数不足/前置未学时为 false；`Learn` 扣 1 点；`TryCast` 扣法力 + 进冷却（贴数字）
- [ ] **投射物**：火弹从 A 飞到 B 命中（贴轨迹采样与命中日志）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出：`IMonsterModule`/`ICombatModule`/`ISkillModule`/`IViewModule` 的**可供 UI 调用的方法签名**与**视图占位策略**

## 6. 约束

- 所有非预期分支必须打日志（AI 找不到路径、目标丢失、素材缺失、法力不足、冷却中…），高频路径**降频**。
- **禁止** `UnityEngine.Random`、`GameObject.Find`、`FindObjectOfType`、裸 `Debug.Log`、裸 `Instantiate`（用 `Game.Pool`）。
- 一个 `.cs` 一个类；**不许新增门面接口**。
- 用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`。
