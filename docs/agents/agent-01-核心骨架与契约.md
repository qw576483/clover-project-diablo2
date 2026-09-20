# agent-01：工程骨架 · Core/Def · **模块契约** · 配置

## 0. 技能（开工必做）

先读 `<项目根>/docs/agents/_common.md` 并**照它执行**（拿 skill → 读契约 → 红线 → 日志 → 分层自检 → 回报格式）。
项目根 = `clover-project-diablo2`。

## 1. 目标

搭出可编译的工程骨架，并把**全部模块接口与 DTO**（契约）一次性冻结落盘 —— 后续 agent 都按它实现。

## 2. 任务边界

- **只做**：`client/Assets/Scripts/{Diablo2.asmdef, Def/, Core/, Module/Contracts.cs}`、`client/Assets/Configs/config.json`、
  `client/Packages/manifest.json`（依赖清单）。
- **绝不做**：不写任何 `Module/{Flow,Map,Player,Combat,Monster,Skill,Item,Quest,Npc,Input,Camera,View,Audio,Save}` 的**实现**；
  不写 `UI/`；不写 `App/`；不碰 `Editor/`；不改 `tools/ai-skill/`；不改契约文档。

## 3. 前置依赖（已就绪）

- 引擎包：`clover-client-unity-engine`
- Unity 工程：`<项目根>/client`（6000.6.0f1，`com.unity.template.universal-2d`）
- 契约：`<项目根>/docs/步骤文档.md` **§3（引擎 API，含出处行号）**、§3.5（常量与 ResPaths）、§3.6（场景与面板）
- 命名与目录：`<项目根>/tools/ai-skill/conventions.md`

## 4. 产出物

### 4.1 `client/Assets/Scripts/Diablo2.asmdef`

```json
{ "name": "Diablo2", "rootNamespace": "Diablo2",
  "references": ["CloverEngine.Core","CloverEngine.Network","CloverEngine.Data","CloverEngine.Resource","CloverEngine.Presentation"],
  "includePlatforms": [], "excludePlatforms": [], "allowUnsafeCode": false,
  "overrideReferences": false, "precompiledReferences": [], "autoReferenced": true,
  "defineConstraints": [], "versionDefines": [], "noEngineReferences": false }
```

### 4.2 `client/Assets/Scripts/Def/`（纯枚举与纯数据，**无 Unity 依赖**）

`Enums.cs` 一个文件集中放（一个 enum 一个文件也行，但**不许放 MonoBehaviour**）：
`PlayerClass{Amazon,Sorceress,Necromancer,Paladin,Barbarian}` ·
`DamageType{Physical,Fire,Cold,Lightning,Poison}` · `ItemQuality{Normal,Magic,Rare,Set,Unique}` ·
`ItemSlot{None,Helm,Armor,Weapon,Shield,Gloves,Boots,Belt,Amulet,Ring}` ·
`MonsterAI{Melee,Range,Shaman,Coward}` · `QuestState{NotStarted,InProgress,ReadyToTurnIn,Done}` ·
`TileKind{Void,Grass,Dirt,Road,Rock,Tree,Fence,Wall,CaveFloor,CaveWall,Exit,TownFloor}` ·
`AreaId{Town=0,BloodMoor=1,DenOfEvil=2}`
`GameKeyAlias.cs`：本项目自定义界面键（`KeyInventory`, `KeySkillTree`, `KeyQuestLog`, `KeyMinimap`, `KeyCharSheet` …）—— **值为 `CloverEngine.GameKey`**。

### 4.3 `client/Assets/Scripts/Core/`

| 文件 | 内容（**严格照 `docs/步骤文档.md` §3.5**） |
| --- | --- |
| `GameConst.cs` | `TileSize` / `IsoTilePxW=128` / `IsoTilePxH=64` / `PixelsPerUnit=64` / `PlayerWalkSpeed=6f` / `InventoryCols=10` / `InventoryRows=4` / `BeltSlots=4` / 地图尺寸范围 / 距离常量 |
| `Events.cs` | 全部事件名常量，**`D2.` 前缀**（照 §3.5 的清单，可增补但**不许用裸字符串**） |
| `ResPaths.cs` | 全部资源路径（照 §3.5；原版素材 `D2/UI/`、`D2/Fonts/`、`D2/Tiles/`、`D2/Objects/`、`D2/Chars/`、`D2/Monsters/`、`D2/Items/`；音效 `Sound/BGM`、`Sound/SFX`） |
| `SceneNames.cs` | `Boot` / `Menu` / `Stage` |
| `ClientConfig.cs` | 静态类 `Cfg`：读 `Assets/Configs/config.json`（`JsonUtility`），字段 `game{ defaultPlayerName, bgmVolume, sfxVolume, fullscreen, useWasdMove }`；**解析失败回退默认值 + Warn，绝不抛异常** |
| `Iso.cs` | `GridToWorld(Vector2Int)` / `WorldToGrid(Vector3)`（**`Mathf.FloorToInt`**）/ `SortOrder(Vector2Int)` / `ScreenToGrid(Camera, Vector3 screenPos)` |
| `AStar.cs` | 格子 A*：`Find(Func<Vector2Int,bool> walkable, Vector2Int from, Vector2Int to, int maxNodes)` → `List<Vector2Int>`（8 邻接、对角需两侧都可走、路径平滑可选） |
| `Rng.cs` | 注入式 `System.Random` 包装（`Seed` / `Next` / `NextFloat` / `Range` / `Pick<T>`），**禁止 UnityEngine.Random** |
| `Log.cs` | 轻量日志门面：`Log.Info/Warn/Error(string tag, string msg)` → 转发 `Game.Logger`（tag 前缀统一）；**防刷屏**（同一条降频） |

### 4.4 `client/Assets/Scripts/Module/Contracts.cs`（★ **全部模块接口 + DTO，冻结**）

只放 `public interface` 与可序列化 DTO，**不放实现**。必须包含（命名以 `tools/ai-skill/registry.md` 为准）：

```csharp
public interface IMapModule {            // 格子地图
    int Width { get; } int Height { get; } AreaId Area { get; }
    bool Walkable(Vector2Int g); bool InBounds(Vector2Int g);
    TileKind TileAt(Vector2Int g);
    System.Collections.Generic.List<Vector2Int> FindPath(Vector2Int from, Vector2Int to);
    void Generate(AreaId area, int seed);
    Vector2Int RandomWalkableTile(Core.Rng rng);
    int BlockedCount { get; } int Seed { get; }
}
public interface IPlayerModule {
    PlayerClass Class { get; } string Name { get; } int Level { get; }
    int Str/Dex/Vit/Eng; int Life/MaxLife/Mana/MaxMana; long Exp;
    int StatPoints/SkillPoints; Vector2Int Grid; Vector3 World;
    void MoveTo(Vector2Int target); void Tick(float dt);
    bool ApplyDamage(int amount, DamageType type);
    void AddExp(int amount); void AllocateStat(StatKind kind, int delta);
}
public interface IMonsterModule {
    int AliveCount { get; } int CountInArea(AreaId area);
    void SpawnArea(AreaId area); void Tick(float dt);
    System.Collections.Generic.IReadOnlyList<MonsterState> All { get; }
}
public interface ICombatModule { void RequestAttack(int monsterId); void Tick(float dt); }
public interface ISkillModule { /* 技能树：Learn/TryCast/SelectedSkill/Cooldown */ }
public interface IItemModule { /* 掉落/拾取/背包/装备/腰带/金币 */ }
public interface IQuestModule { QuestState DenOfEvil { get; } int DenRemaining { get; } void AcceptDen(); void TurnInDen(); }
public interface INpcModule { /* NpcId 定义 / 对话文本 / 商店 / 修理 */ }
public interface ICameraRig { void Follow(Transform t); void Tick(float dt); }
public interface IViewModule { /* 创建/更新实体视图 / 飘字 / 头顶血条 */ }
public interface IAudioModule { void Sfx(string key); void Bgm(string key); void SetVolume(...); }
public interface ISaveModule { /* Save/Load/Delete/List */ }
```

DTO（`[System.Serializable]`、字段名小写驼峰、`List<T>` 不用数组）：`MonsterState`、`ItemStack`、`InventorySlot`、`DamageArgs`、`QuestStateDto`、`NpcDef`、`SkillDef`、`CharacterSave`。
**DTO 的字段名 = 契约**：后续 agent 与服务端无关，两端一致即可；**写好后不许改**。

### 4.5 `client/Assets/Configs/config.json`

```json
{ "game": { "default_player_name": "Hero", "bgm_volume": 0.7, "sfx_volume": 0.8, "fullscreen": false, "use_wasd_move": false } }
```

### 4.6 `client/Packages/manifest.json`

在 `dependencies` 里加入（**保留模板已有项**）：
`"com.clover.unity-engine": "https://github.com/qw576483/clover-client-unity-engine.git"`（分发用 git URL；本地联调见 scaffold/new-project.md §2.3，基准是 client/Packages/）、
`"com.unity.pipeline": "0.7.0-exp.1"`、
`"com.unity.test-framework": "1.4.5"`、`"com.unity.ugui": "2.0.0"`、
以及引擎必需的内置模块：`com.unity.modules.animation / assetbundle / audio / director / imageconversion / imgui / jsonserialize / physics / physics2d / screencapture / ui / uielements / unitywebrequest / unitywebrequestassetbundle / unitywebrequesttexture / unitywebrequestwww / video / xr`（全部 `1.0.0`），
并加 `"testables": ["com.clover.unity-engine"]`。
（模板是 2D 模板，已含 2D 相关包，**不要删**。）

## 5. 验收标准

- [ ] `client/Assets/Scripts/Diablo2.asmdef` 存在且 references 为上面 5 个 `CloverEngine.*`
- [ ] `Core/` 9 个文件 + `Def/Enums.cs` + `Module/Contracts.cs` 全部落盘，**无占位/无 TODO**
- [ ] `Module/Contracts.cs` 里**没有任何实现**，仅接口 + DTO
- [ ] `client/Packages/manifest.json` 含 `com.clover.unity-engine` **与** `com.unity.pipeline`
- [ ] `dotnet build "<项目根>\client\<slnx>"` 里**本 agent 新增文件零报错**（其它 agent 未产出的模块缺失报错可忽略，须在回报里列出）
- [ ] 分层自检 ②③④⑤ 全 0 命中
- [ ] 回报里给出**全部接口与 DTO 的最终签名清单**（后续 agent 照它实现）

## 6. 约束

- 所有非预期分支必须打日志（见 `_common.md` §3）。
- **契约一旦落盘即冻结**；发现自己写的契约有问题 → 停下来回报主 agent，不要自己改。
