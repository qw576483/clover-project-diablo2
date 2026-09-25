# agent-18：**暗黑2 原版素材解码 → 接入工程**（解决"地图/图标乱七八糟"）

> **用户投诉**：「素材都是错了，按钮样式**地图**什么都是乱七八糟的」。
> 根因：之前瓦片/图标全用**纯色占位**。现在**原版素材已经拿到手了**，本轮把它解出来接进去。
> 规则：skill `SKILL.md::★★ 1:1 复刻硬标准`（素材必须用 A 原版的；占位色块 = 不可交付）。

---

## 0. 已经就绪的东西（**不要重复下载 / 不要重新发明**）

| 东西 | 路径 | 说明 |
| --- | --- | --- |
| **解包工具（已验证可用）** | `_assets_src\storm.py` | StormLib 的 Python ctypes 封装。`python storm.py list <mpq> [ext]` / `python storm.py extract <mpq> <outdir> <ext...>`。**能解加密 mpq**（mpyq 不行） |
| **原版数据包** | `_assets_src\d2mpq\*.mpq` | `d2data.mpq`(278MB) / `d2char.mpq`(251MB) / `d2sfx.mpq`(47MB) / `d2music.mpq`(329MB) / `patch_d2.mpq` / `d2speech.mpq` / `d2video.mpq` |
| **已导出的原料** | `_assets_src\d2raw\` | `*.dt1` 206 个（地形瓦片）/ `*.TBL` 35 个（**本地化文本，含中文**）/ `*.PL2` 15 个（调色板）。共 113MB |
| **格式参考实现（C#，有依据可查）** | `_assets_tmp\d2src\Diablerie\Assets\Scripts\**` | 社区复刻工程里有 **DC6 / DCC / DT1 / DS1 / COF / PL2 / TBL 的 C# 读取器**。**格式细节去读它**，不要凭记忆。 |

> ⛔ **许可**：本项目**非商用**，用原版 rip 素材是允许的，**只记一行来源**即可，**不要**为授权停下来问用户。
> **来源行**：`原版素材取自 Diablo II (Blizzard North, 2000) 的 d2data.mpq / d2char.mpq / d2sfx.mpq，非商用。`

---

## §A 交给 `agent-04`：**地形瓦片（DT1）+ 调色板（PL2）+ 地图布局（DS1）** ★解决"地图乱七八糟"

**只许改**：`client/Assets/Scripts/Module/Map/**`；**允许新增** `tools/d2codec/**`（Python 解码脚本，不参与 Unity 编译）；
**允许写入** `client/Assets/Resources/Clover/D2/Tiles/**` 与 `D2/Objects/**`（生成的 PNG，由你决定命名）。

**要做**：

1. **写解码器**（Python，放 `tools/d2codec/`）：
   - `.PL2` → 256 色调色板（**Act1 用哪个 pl2 要查清**，写进代码注释）；
   - `.DT1` → 等距瓦片 PNG。DT1 结构：文件头（版本/宽高/块数）→ 每块（`block`）→ 每块内多个 `tile`，
     **每个 tile 是 32×32 的像素块**，按 `5×5` 子块拼成一个 `160×80` 的等距菱形瓦片（原版口径），
     **含 RLE 压缩 + 索引色**。**按格式参考实现（`Diablerie/Assets/Scripts/` 里的 DT1 读取器）来**，不要猜。
   - `.DS1` → 原版**地图布局**（对象/瓦片摆放）。**先只做"能读出 tile 索引表"**，用于把罗格营地的**固定布局**照原版还原。
2. **落进工程**：解出的 PNG 放到 `Resources/Clover/D2/Tiles/<dt1 名>/<瓦片号>.png`（或你定的命名），
   并在 `Module/Map/MapView.cs` 的 `GroundKeyOf` / `ObjectKeyOf` 里把**占位色块路径改成真瓦片路径**
   （这两个函数就是为"换素材只改一处"设计的）。
3. **罗格营地要照原版固定布局**：用 `data/global/tiles/ACT1/TOWN/`（或等价路径）的 DS1 还原
   —— 帐篷/NPC 站位/出城口的位置**按原版**，不再用程序化随机。
   （原版城镇是**固定地图**，这是我们之前"不像"的一大原因。）
4. **血腥荒野 / 邪恶洞穴**：用 ACT1 户外与洞穴的 DT1 瓦片；布局仍可程序化，但**瓦片必须是原版的**。
5. **Play 验收**：进城镇截图 —— **不再是纯色色块**，能看出是暗黑2 的泥土路/草地/帐篷/栅栏。

**验收**：
- [ ] 解码脚本 + 生成的 PNG 数量与来源 dt1 对得上（贴统计）
- [ ] 截图 `Assets/Screenshots/v3_town_tiles.png` —— **自己读图**确认"是暗黑2 的地形，不是色块"
- [ ] 与**原版罗格营地截图**并排比对，逐项写 `一致`/`不一致（差在哪）`
- [ ] 10 宿主全绿（`tools/probes/hosts/run_all_hosts.ps1`）

---

## §B 交给 `agent-19`：**DC6 → PNG（UI 面板/按钮/图标/物品图标）+ 中文文本表（TBL）**

**只许改**：`client/Assets/Scripts/UI/**`（把占位换成真贴图）、`Core/ResPaths.cs`（**只许新增常量**）；
**允许新增** `tools/d2codec/**`；**允许写入** `client/Assets/Resources/Clover/D2/{UI,Items}/**`。

**要做**：
1. **写 DC6 解码器**（Python，`tools/d2codec/dc6.py`）：DC6 = 头(24B) + 帧指针表 + 每帧头(32B) + RLE 数据 + 索引色；
   配 `.PL2`/内嵌调色板转 RGB PNG（**带透明**：索引 0 = 透明）。
   **格式参考**：`Diablerie/Assets/Scripts/` 里的 DC6 读取器 + 网络上公开的 DC6 格式说明（`OpenDiablo2` 的 `dc6` 库）。
2. **解出并接上这些**（按优先级）：
   - **主菜单/创角/选角/Pause 用到的原版屏**（`main_screen` / `class_select_screen` 等）；
   - **原版按钮**（`menu_button_wide` / `menu_button_medium` 的常态/悬停/按下三帧）；
   - **HUD 控制面板**（`ControlPanel` / 血球/蓝球/经验条/小面板按钮）；
   - **背包/属性/技能树/任务/对话/商店面板底图**（原版 DC6）；
   - **技能图标 / 物品图标 / 金币 / 药水**（Act1 用到的即可）。
3. **把 UI 里的占位色块换成真贴图**：路径一律经 `Core/ResPaths.cs` 的常量（**只增不改**）。
4. **`.TBL` → UTF-8 中文文本**：D2 的 TBL = 头(21B) + 索引表 + 以 `\0` 分隔的字符串（**编码是 cp1252 或代码页 950**，
   中文版就是 **Big5/CP950**）。解出后放 `_assets_src/d2text/`，**用它是为了拿到原版中文台词/物品名/技能名**
   （`NpcDialog.cs` 与配表 `name_cn` 目前是本项目自写的，**换成原版文本**才算 1:1）。
5. **Play 验收**：主菜单/创角/HUD/背包截图 —— **按钮是原版底图、图标是原版图标**。

**验收**：
- [ ] 解码脚本 + PNG 统计（数量/尺寸/来源 dc6）
- [ ] 截图（主菜单/创角/HUD/背包）**自己读图**确认"是原版贴图"
- [ ] `.TBL` 解出中文样例贴出来（至少 5 条原版中文物品名/技能名）
- [ ] 与**原版截图**并排比对，逐项写 `一致`/`不一致（差在哪）`
- [ ] 10 宿主全绿

---

## 共用硬要求

- ⛔ **产出必须是原版像素**（不许重绘、不许改色、不许 AI 生成补图）。缺什么就登记 `client/资源欠缺清单.md`。
- ⛔ **调色板必须用对**（Act1 的 pl2 / DC6 自带的 palette）；颜色不对 = 1:1 失败。
- 大文件导出用 `storm.py extract`（**不要**一次性导出 `d2video.mpq` 这类无关包）。
- 驱动编辑器前先跑 `tools/probes/interact/p_runbg.cs`；截图前等 ≥6 秒。
- ⛔ 不许读工作区里其它 `clover-project-*`。
