# 实测 bug 清单（agent-21 §B 发现；**agent-22 已修 B1~B4**）

| # | 现象 | 证据 | 归口模块 | 状态 | 修复 / 证据 |
|---|---|---|---|---|---|
| B1 | 等距相机不跟随主角 | 相机 pos 始终 (1,-15,-10)；acc_31 角色在左下 → acc_32 角色不在画面 | Module/Camera/CameraRig.cs | **已修** | `CameraRig.RefreshFocus()`：每帧按 ①契约 `Follow(transform)` ②主角 `IPlayerModule.World` 刷新焦点（原先焦点只在进图时设一次、`_target` 存了不读）。修后实测 `camPos=(1,-15,-10)→(-3,-6.5,-10)`，主角屏幕 `(600,1620)→(960,855)` 回到画面内（`_dev/a22_verify_out.txt`，两张 Play 一致） |
| B2 | 创角屏无法输入角色名 | 注入 A/C/C 后文本仍 'Hero' | UI/CharCreatePanel.cs | **已修** | **实测根因**：`ProjectSettings.asset` `activeInputHandler: 1`（只用新 Input System）⇒ uGUI `InputField` 走 `BaseInput → UnityEngine.Input`，直接抛 `InvalidOperationException`（见 `_dev/a22_b2b_out.txt`）。改由面板自己从 `Keyboard.onTextInput` 收字符（`#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER` 包裹，改成 Both 即自动交回 uGUI）。修后实测 `'Hero'→''→'acc'→'accAma'→退格×2 'accA'`，且 `T20 角色名='accA'` |
| B3 | 技能树格子空白灰块，无图标/名称 | 图 acc_44_skilltree.png | UI/SkillTreePanel.cs | **已修** | 三条根因：① `D2Icon` 用 `class_c.Code`（`Amazon`）拼文件名，素材是 `amaSkillicon_*`（3 字母码在 `skill_class` 列）⇒ 30 节点全「贴图缺失」；② 同档并列技能只用了 `slotRow`、x 全相同 ⇒ 完全重合；③ 技能名被**下一档**格子盖住。修后实测 `节点=30 无图标=0 无名字=0 重复格=0`，图 `Screenshots/a22_11_skilltree.png` |
| B4 | 小地图面板标题与信息文字重叠 | 图 acc_40_minimap_on.png | UI/MiniMapPanel.cs | **已修** | 标题 rect 顶边跑出画布 4px + 与图例只隔 4px ⇒ 两行压字、右端还越框。改为**表头带**（`HeaderH=46`）：两行各占一行、右端与地图框右沿对齐、不换行。修后实测 `Title 屏幕 y 0..24 / Legend 26..46`，图 `Screenshots/a22_12_minimap.png` |

> 修复详情（根因链、代码位置、日志原文）见 `策划/验收表.md` 末节「本轮修掉的 bug」。
> 本轮另发现并记入验收表的两点（非 B1~B4，属"缺实机取证"而非代码缺陷）：见 `README.md` §⑤。
