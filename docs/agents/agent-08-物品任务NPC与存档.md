# agent-08：Item（掉落/背包/装备）+ Quest（邪恶洞穴主线）+ Npc（对话/商店）+ Save（存档）

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5**）。
项目根 = `c:\Work\Server\full-dev\clover-project-diablo2`。
另必读：`策划/策划案/暗黑破坏神2参考规格.md` **§3.6/§3.7/§3.8**（物品、NPC、主线任务的保真要求）、
`docs/步骤文档.md` §2/§3、`tools/ai-skill/conventions.md`、`constraints.md`。

## 1. 目标

实现 `IItemModule` / `IQuestModule` / `INpcModule` / `ISaveModule`：
**掉落与拾取、背包与装备（含词缀与品质）、腰带药水、金币、5 个 NPC 与对话、商店买卖与修理、
主线任务「邪恶洞穴」的完整状态机、以及多角色存档**。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Item/**`、`Module/Quest/**`、`Module/Npc/**`、`Module/Save/**`。
- **绝不做**：不改 `Core/`、`Def/`、`Module/Contracts.cs`、`App/`、`UI/`、`Editor/`、其它 `Module/*`；不改契约；不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪）

- `Module/Contracts.cs`：四个门面接口 + DTO `ItemStack` / `ItemAffix` / `InventorySlot` / `QuestStateDto` / `CharacterSave` / `NpcDef` / `ShopEntry` / `ShopOpenArgs` / `ShopTradeArgs` / `InventoryChangedArgs`
- `Core`：`Rng`（注入式）/ `Log` / `Events` / `GameConst.{InventoryCols,InventoryRows,BeltSlots}` / `ResPaths`
- `IMapModule`：`SpawnPoint` / `NpcPoints`（下标 = `(int)NpcId`）/ `RandomWalkableTile`
- `IPlayerModule`（agent-06）：`AddGold/AddExp/Heal/RestoreMana/Grid/Level/GetResist…`
- `IMonsterModule`（agent-07）：`ConsumeCorpse`（Shaman 复活用）
- **`ISaveModule` 是 agent-05 的 `CharRoster` 依赖**：`HasAny/ListAll/Load/Exists/Create/Delete` 必须与 agent-05 的降级接口**同形**
  （去读 `Module/Flow/CharRoster.cs`，按它调用的方法名与签名实现，**不许让 Flow 改代码**）
- 配表：`item_c`（`Name/Type/GridW/GridH/LvlReq/StrReq/DmgMin/DmgMax/DefMin/DefMax/Price/Stack`）、
  `affix_c`（`Kind/Name/Lvl/Mod/Min/Max/ItemTypes`）、`treasureclass_c`（`Picks/Drops/NextTc`）、
  `level_c`（区域）、`monster_c`

## 4. 产出物

| 文件 | 内容 |
| --- | --- |
| `Module/Item/ItemModule.cs` | `internal sealed class ItemModule : IItemModule`（无参构造，供 AutoWire） |
| `Module/Item/ItemFactory.cs` | 物品生成：品质判定（普通/魔法/稀有/套装/暗金）、**词缀组合来自 `affix_c`**（等级筛选 + `ItemTypes` 匹配 + 前后缀搭配）、名字按 `name_cn`（无则英文名） |
| `Module/Item/LootRoller.cs` | 掉落：按 `treasureclass_c` 递归（`Picks`/`Drops`/`NextTc`），`monster_c` 的 TC 列决定怪物掉落；金币另算 |
| `Module/Item/Inventory.cs` | **10×4 格子放置**（`gridW/gridH` 占位、重叠检测、锚点格 `isAnchor`）；入包失败要**明确返回失败**（不许假装成功） |
| `Module/Item/Equipment.cs` | 装备/卸下，装备后**改属性**（走 `IPlayerModule` 派生值重算），`ItemSlot` 含双戒指 |
| `Module/Item/Belt.cs` | 4 格腰带 + 数字键 1~4 喝药（生命/法力药水） |
| `Module/Item/GroundItems.cs` | 地面物品（拾取距离校验；拾取失败物品**必须留在原地**） |
| `Module/Quest/QuestModule.cs` | `internal sealed class QuestModule : IQuestModule` |
| `Module/Quest/DenOfEvilQuest.cs` | **主线任务状态机**：`NotStarted →(Akara 接取) InProgress →(清光洞穴) ReadyToTurnIn →(回城交付) Done`；`DenRemaining` 随 `NotifyMonsterKilled`（**仅洞穴区域**）递减；交付 `+1 技能点`；状态变化发 `Events.QuestChanged` |
| `Module/Npc/NpcModule.cs` | `internal sealed class NpcModule : INpcModule`；5 个 NPC（Akara/Kashya/Charsi/Gheed/Warriv）站位来自 `IMapModule.NpcPoints` |
| `Module/Npc/NpcDialog.cs` | 对话文本**按任务阶段切换**（接取前/进行中/可交付/已完成各不同；Akara 是任务发布者）；文本为中文（**标注"本项目新增/待官方 .tbl 复核"**） |
| `Module/Npc/NpcShop.cs` | 恰西（铁匠：武器/防具/修理）、基德（商人：杂货/赌博**不做**）；**真金币结算**，钱不够/背包满要失败并提示 |
| `Module/Save/SaveModule.cs` | `internal sealed class SaveModule : ISaveModule`：走 `Game.Setting`（JSON），多角色（key 前缀 `char/*`）；`ApplyToModules` 把角色数据灌进 Player/Item/Quest/Skill |

**硬要求**

1. **数值一律来自配表**，不许硬编码价格/掉率/词缀数值。
2. **拾取距离校验**：超出范围 → 不拾取（由 UI 先发出移动意图）。
3. **背包满**：`Pickup` 失败返回 false，**物品留在原地**，并给出可读原因（`Log.Warn`）。
4. **存档版本号**：`CharacterSave.version`，读档时版本不符要**降级处理 + Warn**（不许崩）。
5. **存档往返一致**：`Save → Load → Save` 两次结果相同（自证断言）。
6. 任务进度**只认洞穴区域的击杀**（`AreaId.DenOfEvil`），野外杀怪不计数。

## 5. 验收标准（**必须用离线宿主**，照 `.ai-tmp/hosts/mapcheck/` 或 `.ai-tmp/hosts/flowcheck/` 建 `.ai-tmp/hosts/itemcheck/`）

- [ ] 离线宿主 `dotnet build` 0 错 0 警告；`dotnet run` 断言全过
- [ ] **掉落**：跑 1000 次 `DropLoot`，统计品质分布与金币（贴数字）；所有掉落格 `Walkable == true`
- [ ] **背包格子**：放一个 2×3 物品 → 占 6 格且锚点正确；要塞进剩余 1 格空隙 → **失败**且提示"背包已满"
- [ ] **词缀**：生成的魔法物品有 1~2 个词缀，词缀等级 ≤ 物品等级（贴 3 个样例物品的完整属性行）
- [ ] **装备生效**：装武器 → `AttackRating`/伤害变化；装防具 → `Defense` 变化（贴前后数字）
- [ ] **药水**：受伤后 `UseBeltSlot` → 生命上升、腰带计数 -1（贴数字）
- [ ] **任务链完整**：`AcceptDen → NotifyMonsterKilled ×N → DenRemaining==0 → CanTurnInDen==true → TurnInDen → 技能点 +1`（贴每一步的状态与数字）
- [ ] **任务只认洞穴**：在 `AreaId.BloodMoor` 调 `NotifyMonsterKilled` → `DenRemaining` **不变**（贴断言）
- [ ] **NPC 对话随阶段变化**：4 个阶段各取一次 Akara 的对话，文本互不相同（贴 4 段文本）
- [ ] **商店**：买入 → 金币减少、背包 +1；钱不够 → 失败且金币不变；卖出 → 金币增加；修理 → 扣金币（贴数字）
- [ ] **存档往返**：`Save → Load → Save` 两次 JSON 等价；删除角色后 `ListAll` 少一个（贴断言）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出：四个门面的**全部公开方法签名**（UI agent 照它写面板）与 `ISaveModule` 与 `CharRoster` 的对接确认

## 6. 约束

- 所有非预期分支必须打日志（物品生成失败、格子放置失败、钱不够、背包满、存档版本不符、读档缺字段…）。
- **禁止** `UnityEngine.Random`、`GameObject.Find`、`FindObjectOfType`、裸 `Debug.Log`。
- 一个 `.cs` 一个类；**不许新增门面接口**。
- 用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`。
