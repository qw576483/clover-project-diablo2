# agent-14 修复轮：Play 实测缺陷（**三个任务包，按 section 分派**）

> 来源：2026-09-17 首次进 Play 的实测（截图 + 日志）。
> 通用前置仍按 `docs/agents/_common.md`（**尤其 §3.5**；自检必须用 `tools/*check` 离线宿主，改完回归全部 10 个宿主 + `tools/probes/hosts/fullcheck`）。
> ⛔ 边界同前：不许改别人的文件；发现要改别人文件 → 写进回报交给主 agent。

---

## 实测已通过的链路（**不要动这些**，改坏了要负责）

启动画面 →（真实按键）→ 主菜单 →（真实 uGUI 点击）→ 选角 → 创角（5 职业 / 四维 / 可分配 5 点 / 生命 60·法力 22·耐力 20 来自 `class_c`）
→ 建角 → **存档落盘 `char/Hero`（367 字节）** → 回选角（读到 1 个角色）→ 进入 → 读条 → **Stage**
（地图 32×32 seed=393732373 障碍 215 可走 809、NPC 5 站位、地形哈希 `F49AB4344E3D0D0A` 与离线宿主一致、
城镇刷怪 0 只符合设计、HUD 打开且用了**原版血球/蓝球/小面板贴图**、全量快照 6 条）。

---

## §A 交给 `agent-06`：**相机取景错位**（★ 最影响观感）

**现象**（截图 `client/Assets/Screenshots/p04_stage_town.png`）：进图后**等距地图只出现在屏幕下方一角，上方大片纯黑**，
玩家（亚马逊）看不见；HUD 在原位正常。

**已知数据**：`[Camera] 跟随目标格已设为 (15,19) → 世界 (-4.00,-17.50)`；`正交 size=6`、`机位 z=-10`、`rotation=identity`；
`Iso.GridToWorld` 的地图范围：城镇 32×32 格 ⇒ 等距投影后世界宽 `32*128/64 = 64`、高 `32*64/64 = 32` 世界单位，
中心约 `(0, -16)`。**焦点 (-4,-17.5) 看着不离谱，但画面明显偏移** ⇒ 怀疑：
① `CameraRig` 的正交相机 `orthographicSize` 语义（半高）与 `GameConst.IsoTilePxW/PPU` 的口径不一致；
② `Camera.main` 不是渲染世界的那台（场景里 agent-10 放了一台主相机，`CameraRig` 又可能自建/取到别的）；
③ 世界平面用了 XZ 还是 XY 与 `Iso` 不一致（`Iso` 是 XY、`z=0`）。

**只许改**：`client/Assets/Scripts/Module/Camera/**`（必要时 `Module/Input/**` 里反投影部分）。

**要做**：
1. **先量后改**：写探针（放 `.ai-tmp/test/`）打印——实际参与渲染的相机（`Camera.main` 与场景里所有 Camera 的 name/enabled/depth/orthographicSize/position）、
   地图世界包围盒（`Iso.GridToWorld` 对四角）、焦点世界坐标、`Screen.width/height`。**把数字贴进回报**。
2. 修正后要求：**玩家恒在屏幕中心附近**；城镇地图尽量铺满视野；屏幕不出现大片黑（除地图边界外）。
3. 相机仍必须**固定等距、不旋转**；缩放默认关。
4. 加离线断言：给定地图尺寸与焦点格，断言"焦点世界坐标 → 屏幕坐标 ≈ 屏幕中心"。

**验收**：离线断言过 + **进 Play 截图**（`Assets/Screenshots/fix_camera.png`）里玩家在中心、地图大面积可见。

---

## §B 交给 `agent-05`：**流程两个缺陷**（面板没关 / 日志翻倍 / 过门重复生成）

**现象 1（★）**：创角完成回选角屏后，`CharCreatePanel` **仍然开着**（与 `CharSelectPanel` 叠加）。
实测证据：`p_buttons2` 同时列出 `CharCreatePanel(Clone)` 与 `CharSelectPanel(Clone)`。
**要修**：`CharCreate → CharSelect` 迁移必须 `Game.UI.Close<CharCreatePanel>()`（并检查**所有**站点迁移的 `onExit` 有没有同类漏关）。

**现象 2**：启动时 `[Flow] → Boot` 打了**两条**（seq 47/48）。说明 `Game.Fsm.OnChange` 被注册了两次。
**要修**：查清谁注册了两次（`AppFlow` 与 `App/AppWiring`？），只保留一条；站点日志必须**每个站点恰好一条**
（验收要 grep `[Flow] →` 计数）。

**现象 3**：`[Warn] [App] [Assert] 本次过门已第 2 次收到 D2.Map.Generated（>1）⇒ 重复生成！`
**要修**：一次过门只允许重生成一次地图（`ExitEntered` 的处理路径去重）。**修好前不要删那条断言**，它是证据。

**只许改**：`client/Assets/Scripts/Module/Flow/**`、`client/Assets/Scripts/App/**`（`App` 归你与本轮）。

**验收**：离线宿主断言（站点日志计数=1、面板关闭、`Map.Generated` 每次过门=1）+ 回归全部宿主。Play 侧由主 agent 复测。

---

## §C 交给 `agent-09`：**界面保真：背景太暗 / 未用原版贴图**

**现象**（截图 `p02_mainmenu.png`、`p03b_charcreate.png`）：
- 主菜单用了原版 `main_screen` 贴图（能看到 "EXPANSION SET / LORD OF DESTRUCTION"），但**整体极暗**，背景几乎看不出；
- 创角屏是**纯黑底**，没有用原版 `class_select_screen` 贴图；
- 按钮是纯色块（原版有像素风按钮底图；`ResPaths.Frame(ResPaths.MenuButtonWide, i)` 现在可取帧）。

**诊断方向**：项目是 URP 2D，精灵默认材质 `Sprite-Lit-Default` 受 2D 光照影响 ⇒ UI 用 Image 显示的原版贴图也被光照压暗。
**要修**：
1. 让**所有 UI 的 Image** 不受 2D 光照影响（用 Unlit 的 UI 材质/`Sprite-Unlit-Default`，或确保 UI Canvas 的 `CanvasRenderer` 走 UI 默认材质）；
2. `MainMenuPanel` 背景用原版贴图并按原版亮度显示；
3. `CharCreatePanel` 背景改用 `ResPaths.MenuClassSelectScreen`；
4. 按钮底图接上原版帧（`ResPaths.Frame(ResPaths.MenuButtonWide, 0/1/2)` = 常态/悬停/按下）。

**只许改**：`client/Assets/Scripts/UI/**`（`UiArt.cs` 可改，本轮放开）。

**验收**：离线断言（背景贴图路径、按钮取帧路径、材质/着色器选择）+ 回归宿主。
**Play 侧截图对照由主 agent 做**（要看到：主菜单背景能看清、创角屏有原版背景、按钮有原版底图）。

---

## §D 主 agent 自己处理（不下发）

| 项 | 说明 |
| --- | --- |
| **P-2「失焦不 tick」** | 已确认：编辑器失焦时 `unscaledDeltaTime=13.75`、`deltaTime=0.02` ⇒ 引擎 `Timer`（用 `Time.deltaTime`）几乎不推进，**读条门控永不满足**。对策：驱动编辑器前先 `UnityEngine.Application.runInBackground = true`（探针 `tools/probes/interact/p_runbg.cs`）。**要写进 `tools/ai-skill/constraints.md`** |
| `Def.AreaId` 0 基 vs `level_c.id` 1 基 | 已有 `AreaLevelTable.Resolve` 兼容 + Warn；**修正 `Def/Enums.cs` 注释**与 `docs/步骤文档.md` 口径（属契约，主 agent 裁决） |
| `StageRoots` 未挂到 Stage 场景 | agent-12 建了 `App/StageRoots.cs`、agent-10 建了 `MapRoot`/`EntityRoot` 节点，但两者没接上 ⇒ 由 agent-10 补（写进后续轮） |
| 官方素材（角色/怪物/地形/音效） | 后台下载**中断**：`_assets_src/d2_d2es_zh-tw.zip.part` 2241641414 字节（超出预期 2145506049，因分块重试时未回滚已写字节而损坏）⇒ 需修 `d2fetch.ps1`（每次重试前 `SetLength(offset)` 截断）后重下 |
