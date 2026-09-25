# agent-21：**角色/怪物渲染仍是占位块**（最后一公里）+ 42 行验收表收尾

> **用户投诉**：「我要的 1:1 复刻……素材都是错了，按钮样式地图什么都是乱七八糟的」。
> 现状：**地形 / UI / HUD / 图标已经 1:1**（原版瓦片、原版雕像控制面板、原版按钮与图标），
> **角色与怪物仍是纯色方块**（玩家=黄色 / 怪物=粉色）。这是 1:1 的**最后一块**。

---

## §A 交给 `agent-07`：角色/怪物渲染接线（★ 只差这一步）

**只许改**：`Module/View/**`、`Module/Monster/**`；必要时 `Core/ResPaths.cs`（只增常量）。

### 已经确认的事实（**不要重复排查**）

| 事实 | 证据 |
| --- | --- |
| 素材**已解码并落盘** | `Assets/Resources/Clover/D2/Chars/` = **3136 PNG**（`amazon` 624 / `barbarian` 592 / `necromancer` 648 / `paladin` 648 / `sorceress` 624）；`D2/Monsters/` = **5040 PNG**（12 个原版代码目录 `bk ci cr fa fs gh ps rc si wa wr ye`） |
| **命名与代码约定一致** | 实际文件名 `attack_e_0.png` / `attack_ne_0.png`，正是 `SpriteFrames` 的 `{动作}_{方向}_{帧号}` |
| **Unity 已导入、设置正确** | 3136/3136 都有 `.meta`；`textureType: 8`(Sprite) / `filterMode: 0`(Point) / `alphaIsTransparency: 1` |
| 代码接线**看起来是完整的** | `SpriteFrames` 已有真实帧数表 `SpriteFrameCounts.Of(unitKey, anim)`（从原版 `.cof` 生成）、`Resolve(key)`、`Placeholder`、重铺机制 |
| NPC 视图**已建** | 日志：`[View] NPC 视图已建：Akara（原版代码 ps）格=(28,5) 帧目录=D2/Monsters/ps/` ×5，并注明"贴图异步到位后 Tick 会自动重铺一次" |
| 引擎侧一个已知缺陷**已由主 agent 修掉** | `ResourceBackend.cs` `BeginLoad`：`LoadAsync` 在同帧被同步取过时 `completed` 不触发（E-res-01，已修）⇒ **但它不是本问题的根因**（修后仍占位） |

### 必须用探针**逐层验证**（不许靠读代码推断）

写探针，依次打印并回报**原始值**：

1. `Diablo2.Module.View.SpriteFrameCounts.Of("amazon", ViewAnim.Walk)` = ？（**0 就说明单位键名不匹配**）
2. `SpriteFrameCounts.Of("fa", ViewAnim.Walk)`（怪物侧） = ？
3. `SpriteFrames.Keys(PlayerClass.Amazon, ViewAnim.Walk, Dir8.S)` → 打印**前 3 个帧键字符串**（看拼出来的路径对不对）
4. `Core.ResPaths.CharDir(PlayerClass.Amazon)` → 打印字符串
5. `CloverEngine.Game.Res.TryGet<Sprite>("<第 3 步的键>")` = null 吗？
6. **同步对照**：`UnityEngine.Resources.Load<Sprite>("Clover/" + "<第 3 步的键>")` = null 吗？
   （**重点怀疑**：`Game.Res` 的 prefix 是否与 `ResPaths` 的常量**叠加了两次 `Clover/`**，
   或者 `CloverRes.Init("Clover")` 的 root 与 `ResPaths.Root = "Clover"` 重复 —— 地形能显示不代表这条路对，
   因为地形走的是 `MapView` 自己的加载路径）
7. `ViewModule` 的"重铺一次"**是否真的被触发**（`SpriteFrames.ConsumeRepaintRequest()` 的返回值/日志）；
   若从未触发 ⇒ 找出为什么（异步回调没到？`TickPlayer` 没重铺？）

### 修完的验收

- [ ] 上述 7 条探针的**原始输出**贴进回报（哪一条断链了要一眼看得出来）
- [ ] **截图** `Assets/Screenshots/v8_chars.png` —— ⚠️ **必须用 `capture_game_view --source camera`**：
      `--source screen` 在编辑器失焦时**返回缓存帧**（实测三次字节数完全相同、画面没变），会给出假的"没修好"
- [ ] 截图里**能看到原版亚马逊的像素小人**（不是黄色方块）；`v8_monster.png` 能看到原版怪物
- [ ] 走一步能看到行走帧变化（贴两张不同帧的截图）
- [ ] 10 宿主全绿（`tools/probes/hosts/run_all_hosts.ps1`）

### 驱动编辑器（**照抄，别自己发明**）

```powershell
cd client
unity command editor_stop ; Start-Sleep 6 ; unity command clear_console
unity command editor_play ; Start-Sleep 15
unity command eval_file --file "_dev\p_runbg.cs"      # ★ 必须：失焦不 tick（constraints.md #11）
unity command eval_file --file "_dev\p_autostage.cs"  # 一键推进到 Stage
unity command capture_game_view --source camera --save_path Screenshots/v8_chars.png   # ★ camera 源才会重绘
powershell -File "_dev\readlog.ps1" -Tail 200 -Filter '\[View\]|SpriteFrame'
```
（编辑器还提供原生 `simulate_key` / `simulate_pointer`，比自写注入脚本可靠，可用来让角色走两步。）

---

## §B 交给 `agent-12`：**42 行验收表收尾**（交付合同）

**只许改**：`策划/验收表.md`、`策划/自审对比/**`、`App/**`（仅供打印/输出修正）。

`策划/验收表.md` 目前 **0/42 已填**。要**逐项在 Play 里真演一遍**并填满：
每格 ≥2 条独立证据（**日志行 + 截图路径**）+ 结论（`通过`/`不一致（差在哪）`）。

**重点**（从未跑过的）：点击移动 + A* 绕障、交互光标、攻击与命中三件套（飘字+音效+目标血条）、
怪物 AI（追击/远程/萨满复活/逃跑）、精英怪词缀、掉落/拾取/背包/装备/腰带、商店买卖修理、
**主线任务「邪恶洞穴」完整链**（接取→清光→交付→+1 技能点）、技能树与施法、投射物、小地图、
暂停/设置、存档往返、**回主菜单再进一次**。

**硬要求**：
- **截图自己用多模态读一遍**，并把图上数字与日志数字**对一遍**（对不上就是 bug）。
- ⚠️ **`capture_game_view --source screen` 会返回缓存帧**（失焦不重绘）⇒ 世界内容用 `--source camera`；
  确需 Overlay UI 时，先 `simulate_key`/点击触发一次 UI 变化再截。
- 机制验证用探针（给 100 万经验再杀 1 只小怪验升级），别真刷半小时。
- 拍到的坏图**不许删**，写清"坏在哪 + 改了什么 + 重拍"。
- **发现 bug 写进回报清单**（现象/证据/归口模块），**不许自己改 `Module/**`、`UI/**`**。

**验收**：`策划/验收表.md` **42 行全部填满**（结论列无空、无"未验/待验"）+ `策划/自审对比/README.md`（与暗黑2 同角度对照，只写 `一致` / `不一致（差在哪）`）。

---

## 共用
- ⛔ 不许读工作区里其它 `clover-project-*`。
- ⛔ 产出必须是原版素材，**不许重绘/AI 补图/用色块顶替成品**；缺什么登记 `client/资源欠缺清单.md`。
- 回报贴**原始输出**（探针值 / 日志行 / 截图读图结论），不要只说"修好了"。
