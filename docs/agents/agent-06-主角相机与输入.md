# agent-06：Player（属性/点击移动）+ Camera（等距跟随）+ 输入读取

## 0. 技能（开工必做）

先读并照做：`<项目根>/docs/agents/_common.md`（**尤其 §3.5 第一批已落盘的事实**）。
项目根 = `clover-project-diablo2`。
另必读：`docs/步骤文档.md` §3.3/§3.5、`tools/ai-skill/conventions.md`（**等距坐标**）、`constraints.md` #4 #5 #6。

## 1. 目标

实现 `IPlayerModule` 与 `ICameraRig`：**鼠标点击移动（A\* 寻路 + 沿路点本地移动）**、8 方向朝向、四维属性与派生属性、
经验升级、受伤与死亡状态；以及**固定等距角度的跟随相机**。输入读取写成内部助手（**契约只有 12 个门面，不要新增接口**）。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/Module/Player/**`、`client/Assets/Scripts/Module/Camera/**`、
  `client/Assets/Scripts/Module/Input/**`（**仅内部助手类，不是模块接口**）。
- **绝不做**：不改 `Core/`、`Def/`、`Module/Contracts.cs`、`App/`（**Bootstrap 由集成 agent 接线**）、`UI/`、`Editor/`；
  不改其它 `Module/*`；不改契约；不改 `tools/ai-skill/`。

## 3. 前置依赖（已就绪，**直接用，不许重写**）

- `Core/Iso.cs`：`WorldToGrid` / `GridToWorld` / `ScreenToWorldOnGround(Camera, Vector3)` / `ScreenToGrid` / `SortOrder` / `DirectionTo`
- `Core/AStar.cs`：`Find` / `FindSmoothed` / `HasLineOfSight`
- `Core/Log.cs`（tag 用 `Player` / `Camera`）、`Core/Rng.cs`、`Core/Events.cs`、`Core/GameConst.cs`、`Core/ResPaths.cs`、`Core/Cfg.cs`
- `Def/Enums.cs`：`PlayerClass` / `StatKind` / `DamageType` / `Dir8`
- `Module/Contracts.cs`：`IMapModule`（`Walkable` / `FindPath` / `SpawnPoint` / `Exits` / `CaveEntrance`）、`IPlayerModule`、`ICameraRig`、DTO `PlayerStatsDto` / `CharacterSave` / `DamageArgs`
- 配表：`Table.Tables.Default.Class.Get((int)cls)`（`Str/Dex/Vit/Eng`、`LifePerLvl/ManaPerLvl/StamPerLvl`、每点体力/精力换算列）；
  `Table.Tables.Default.Experience.Get(level)`（`Exp`，**int64 ⇒ long**）
- 注意：agent-05 的 `CharCreatePanel` 用公式
  `生命 = 体力 × life_per_vit + (等级-1) × life_per_lvl`，**你必须用同一公式**，否则创角预览与进图后数字对不上（验收 #17）。

## 4. 产出物

| 文件 | 内容 |
| --- | --- |
| `Module/Player/PlayerModule.cs` | `internal sealed class PlayerModule : IPlayerModule`（**无参构造**，供 `AppContext.AutoWire` 反射创建） |
| `Module/Player/PlayerStats.cs` | 四维/派生属性计算（生命/法力/耐力/防御/AR/四抗），**全部从配表 + 官方公式来**，注释写清公式出处 |
| `Module/Player/PlayerMotor.cs` | 移动：目标格 → `IMapModule.FindPath` → 路点列表 → 沿路点推进（速率 = `GameConst.PlayerWalkSpeed`，格/秒）；到达/受阻/目标不可达的处理；`Dir8` 朝向；`World`/`Grid` 同步 |
| `Module/Camera/CameraRig.cs` | `internal sealed class CameraRig : ICameraRig`：**固定等距角度**（不旋转）、平滑跟随（`LateUpdate` 或由 `Tick` 驱动）、可选边缘滚动 + 滚轮缩放（缩放必须同步调整 `Iso` 归属的渲染尺度，或明确不支持缩放并在回报里说明）；**不许**调用引擎 `Game.Camera.Follow`（锁 Z 的简单跟随，不适用） |
| `Module/Input/InputReader.cs` | 内部助手（`internal static` 或 `internal sealed`）：唯一读输入的地方，**一律走 `Game.Input`**（`GetMouseButton/Down/Up`、`MousePosition`、`GetKeyDown(GameKey)`）；提供 `TryGetGroundClick(out Vector2Int)` / `HoverGrid` / 快捷键（`Def/GameKeyAlias.cs`）；**禁止** `UnityEngine.Input`、`Keyboard.current` |
| `Module/Player/PlayerLog.cs` | tag 常量 + 日志门面 |

**硬要求**

1. **点击移动**：`Camera.main.ScreenToWorldPoint` 时必须给正确的 `z`（到地面的距离，见 `constraints.md` #6），
   再 `Iso.WorldToGrid`。**不许**直接拿屏幕坐标当世界坐标。
2. **不穿墙**：沿路点推进时**每步都要校验**下一格 `Walkable`（路点是格中心，跨格边界要检查），
   被挡则停下并 `Log.Warn`（带起终点格）。
3. **不可达**：`FindPath` 返回 `null`/空 → 记为"不可达"，不移动，`Log.Info` 一条（**降频**）。
4. **按住左键持续走**：目标格变化超过 1 格才重算路径（避免每帧 A*）。
5. **死亡/复活**：`ApplyDamage` 归零 → `IsDead = true` + `Emit(Events.PlayerDied)`；`Revive()` 回出生点满血。
6. **升级**：`AddExp` 累加，按 `experience_c` 连续判级；每级 `StatPoints += 5`、`SkillPoints += 1`，
   发 `Events.LevelUp` 与 `Events.HudDirty`；**日志带等级前后**。
7. **属性点分配**：`AllocateStat` 改四维后**必须重算派生值**并 `Emit(Events.HudDirty)`。

## 5. 验收标准（**必须用离线宿主**，照 `tools/probes/hosts/mapcheck` 或 `tools/probes/hosts/flowcheck` 建 `tools/probes/hosts/playercheck`）

- [ ] 离线宿主：`dotnet build` 0 错 0 警告；`dotnet run` 断言全过
- [ ] **点击移动**：给 `IMapModule`（用 agent-04 的 `MapModule`）在罗格营地生成 → 设目标格 → 逐帧 `Tick(0.02f)` →
      玩家最终到达目标格（贴出起点/终点/路径长度/实际帧数）
- [ ] **绕障**：目标在障碍另一侧 → 路径非直连且**每一格都可走**；玩家全程 `Walkable(Grid) == true`
- [ ] **不可达**：目标设为 `TileKind.Wall` 格 → 不移动 + 有日志
- [ ] **升级公式**：`AddExp` 到阈值 → 等级 +1、生命上限按配表增加（贴日志的等级前后数值）
- [ ] **属性点**：`AllocateStat(Vitality, 5)` → `MaxLife` 增量 == 配表 `life_per_vit × 5`（贴数字）
- [ ] **配表一致性**：5 职业的 1 级 `MaxLife/MaxMana/MaxStamina` 与 `class_c` 计算值逐一相等（贴表）
- [ ] 分层自检 ②③④⑤ 全 0 命中（③ 用锚定版 pattern）
- [ ] 回报里给出：`IPlayerModule` 的可调用点、`InputReader` 的公开方法签名、`CameraRig` 的等距参数（角度/距离/正交尺寸）

## 6. 约束

- 所有非预期分支必须打日志。**禁止** `UnityEngine.Random`、`GameObject.Find`、`FindObjectOfType`、裸 `Debug.Log`。
- 一个 `.cs` 一个类；**不许新增门面接口**（契约冻结）。
- 用户未打开编辑器 ⇒ **禁止** `unity run/test/batchmode`；自检走离线宿主。
