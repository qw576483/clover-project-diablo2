# agent-16：**`MissingReferenceException` 每帧刷屏**（★ 阻断级）

> 来源：2026-09-17 Play 实测。Console 被同一条异常刷到 seq **58423+**（几万条），
> **每帧重复抛**，并且异常从 `AppContext.Tick` 抛出 ⇒ **它之后的模块每帧都被中断**。
> 这是"降级不崩"体感的分水岭，必须修干净。

## 根因（有堆栈，不用猜）

```
MissingReferenceException: The object of type 'UnityEngine.GameObject' has been destroyed but you are still trying to access it.
UnityEngine.GameObject.get_transform ()
Diablo2.Module.View.ViewModule.TickPlayer (System.Single dt)   ← Assets/Scripts/Module/View/ViewModule.cs:392
Diablo2.Module.View.ViewModule.Tick (System.Single dt)         ← ViewModule.cs:344
Diablo2.App.AppContext.Tick (System.Single dt)                 ← AppContext.cs:168
Diablo2.App.Bootstrap.Update ()                                ← Bootstrap.cs:147
```

**触发路径**：进 Stage（创建玩家/怪物/物品视图）→ **回主菜单 / 场景卸载** ⇒ 视图 GameObject 被销毁，
但 `ViewModule` 仍持有引用并在下一帧 `Tick` 里访问 `.transform` ⇒ 每帧抛异常。

## 只许改

`client/Assets/Scripts/Module/View/**`（必要时 `Module/Monster/**`、`Module/Camera/**`、
`Module/Player/**` 里**同类**位置；这些都在本任务范围内）。

## 要做

1. **修 `ViewModule`**：
   - 所有"持有 `GameObject` / `Transform` / `SpriteRenderer` 等 Unity 对象引用并在 `Tick`/`Update` 里使用"的地方，
     **一律先判空**（注意 Unity 的"假 null"：对象被销毁后 `== null` 为 **true**，直接用 `if (go == null) return;` 即可，**不要**用 `ReferenceEquals`）；
   - **`Clear()` / `StageLeft` 时把全部引用显式置 null**（包括玩家视图、怪物视图表、地面物品表、根节点 `EntityRoot`）；
   - `Tick` 开头做一次总闸门：`if (_root == null) return;`（根节点没了就整体不 tick）。
2. **同类排查**（不许只修一处）：`Module/` 下所有缓存了 Unity 对象引用的类（`MapView` / `MonsterModule` 的视图引用 / `CameraRig` 的 `_camera` 与 `_followTarget` / `PlayerModule` 的视图引用…）
   逐处检查"对象销毁后仍访问"的可能，并加同样的防护。
3. **`MapView` 同理**：Stage 卸载后 `MapRoot` 被销毁，若 `MapModule`/`MapView` 仍在 `Tick` 里访问 tile 对象，会出同样的问题。
4. **加一条可检索日志**：引用失效时**只报一次**（不要每帧刷），文案带模块名与"已随场景卸载"，
   便于区分"正常卸载"与"真丢引用"。

## 验收（**必须进 Play 实测**）

- [ ] 离线宿主：`tools/run_all_hosts.ps1` 全绿（`.ai-tmp/hosts/fullcheck` 全量编译 0 错）
- [ ] **Play 链路**：进 Play → 到 Stage（`client/_dev/p_autostage.cs` 可一键到 Stage）→ **回主菜单**
      （`Emit(Diablo2.Core.Events.ToMainMenuRequest)`）→ 再进 Stage → 回主菜单，
      **Console 里 `MissingReferenceException` 计数 = 0**（贴 `console_status` 的 error 计数）
- [ ] **连续两次进 Play** 都要 0 异常（skill 要求）
- [ ] 回报里给出：修改点清单（文件:行号）、引用失效时的日志样例行、`console_status` 的 error 数

## 驱动编辑器（已连上）

先跑 `client/_dev/p_runbg.cs`（**失焦不 tick**，见 `tools/ai-skill/constraints.md` #11）；
截图前等 ≥6 秒否则拿过期帧；面板真值用 `Game.UI.IsOpen<T>()`（不要用 `FindObjectsByType`）。
⛔ 不许读工作区里其它 `clover-project-*`；不许改 `UI/**`（那是别人的）。
