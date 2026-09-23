# tools/probes —— 判据资产（删了就不能重判同一件事）

> 依据：全局 clover-engine skill §1.8 —— 一次性产物进 `.ai-tmp/test/`（用完即删），
> **判据资产**（探针 / 驱动 / 量法脚本 / 参考裁图 / 台账）**不算一次性** ⇒ 落 `tools/probes/` 并**入仓**。
> 本目录由「仓库卫生整理」这一轮从 `.ai-tmp/` 迁出（2026-09-20），原处不留副本。
>
> ⚠️ 这些文件是**当轮取证的原样记录**，内部写死了当时的位置（`.ai-tmp\drivers\...`）。
> **2026-09-20 只改了一处**：5 个 `*_run.ps1` 的 `$root` 从写死的 `'clover-project-diablo2'`
> 改成按脚本位置推导的仓库根（`Split-Path`×3，逐层向上）—— 其余一个字未改，
> 所以下面「怎么跑」列里 `$cs` / `$outDir` 仍写着**需要先改的那两处变量**。
>
> 共同依赖：Unity 6000.6.0f1 —— 宿主按 `tools/probes/hosts/Directory.Build.props` 解析编辑器位置
> （默认 `$(ProgramFiles)\Unity\Hub\Editor\6000.6.0f1\Editor`；换盘 / 换小版本用环境变量 `UNITY_EDITOR_ROOT` 覆盖）、
> `unity` CLI（Pipeline 包）、Python 3.12 + `Pillow`、编辑器里已打开 `client/` 工程。

## 判据资产索引

| 资产路径 | 判什么 | 怎么跑 | 依赖 |
| --- | --- | --- | --- |
| `drivers/p29_drive.cs`<br>`drivers/p29_run.ps1`<br>`drivers/p29_sheet.py` | agent-29：`CloverEngine.Timer` id-0 墓碑修复（本会话第一次 `Game.Scene.Load`）+ 创角翻面动画 + 全部 UI 按钮**真点击**；末屏联络图判据 | `powershell -NoProfile -ExecutionPolicy Bypass -File p29_run.ps1 -Tag r1`<br>（先改脚本第 31 行 `$cs = <root>\.ai-tmp\drivers\p29_drive.cs` → `tools\probes\drivers\`；`$outDir` 同样） | 编辑器已开 `client/`；`unity` CLI；脚本靠 `eval_file` 注入 `p29_drive.cs`；输出面原为 `.ai-tmp/drivers/out/`（本轮已删，重建需重跑） |
| `drivers/p34_drive.cs`<br>`drivers/p34_run.ps1`<br>`drivers/p34_sheet.py` | pass 3/4：UI 资源接入后的实机表现 + 数值断言，一张联络图定判 | 同上形状：`-File p34_run.ps1 -Tag r1`（同样要先把 `$cs`/`$outDir` 从 `.ai-tmp\drivers\` 改到本目录） | 同上 |
| `drivers/p36_drive.cs`<br>`drivers/p36_run.ps1` | pass 3/6：一条链路里的数值断言（探针自写 `p36_done_*.txt` 作完成信号） | `-File p36_run.ps1`（同样要改 `$cs`/`$outDir`） | 同上 |
| `drivers/p38_drive.cs`<br>`drivers/p38_run.ps1` | 移动黄方块占位：走完 8 方向前后读 `ViewModule.PlaceholderTicksOf` 计数 + 一帧相机实拍（原版帧 vs 占位方块） | `-File p38_run.ps1`（同样要改 `$cs`/`$outDir`） | 同上 |
| `drivers/p41_camprobe.cs`<br>`drivers/p41_drive.cs`<br>`drivers/p41_run.ps1` | 收尾轮 pass 5：9 条用户投诉的表现类 + 数值类证据，一次进 Play 全取；驱动**自己**写瓦片到 `.ai-tmp/test/raw/`（因为 `capture_game_view --save_path` 会触发 `Assets/` 重导入 ⇒ 打断 Play 会话） | `-File p41_run.ps1 -Tag r1`；脚本第 161/266 行调 `p41_sheet.py`（`$sheetDir` 需指向 `tools\probes\measure\`） | 同上 + `Pillow` |
| `measure/p41_sheet.py`<br>`measure/p41_sheet_selftest.py` | 联络图生成器 + **判决引擎**：正则读冻结日志（`[P41]` 行）+ 逐瓦片真检查 ⇒ 写出 `p41_index.tsv`（格 ↔ 判决 ↔ 截图 ↔ 日志行）。`selftest` 用**合成夹具**离线自证正则/格式假设（秒级，不占 Play） | `python p41_sheet.py <shot_dir> <out_png> <out_tsv> <frozen_log> <trace>`<br>`python p41_sheet_selftest.py` | Python 3.12 + `Pillow` |
| `measure/p41_index.tsv` | 上述判决表的**冻结产物**（13727 字节，格号/判决/截图/日志行一一对应） | 只读，随联络图一起看 | — |
| `measure/verify_final.txt`<br>`measure/assert_final.txt` | 收尾轮的**冻结验证输出 / 断言输出**原文（数值类证据的来源行） | 只读；重判同一件事要重跑 `tools/verify.ps1` 与收尾轮驱动 | — |
| `measure/SpriteFrameCounts.generated.cs` | 原版 `.cof` 的 `framesPerDirection` 生成物（逐单位 × 逐动作真实帧数；**禁止手改**，生成器 `tools/d2codec/export_chars.py --emit-cs`）——是「帧键指向不存在的图」这类静默缺图的判据 | 只读；重生成：`python tools/d2codec/export_chars.py --emit-cs` | 原版资源 `原版资源/d2raw/...`（`.cof` + `manifest.json`）+ Python |
| `measure/scan_frontend.py` | 量 `Resources\Clover\D2\UI\FrontEnd\{amazon,barbarian}` 各帧 PNG 的**实际像素尺寸** ⇒ 判导出是否把每帧 offset 烘进画布 | `python scan_frontend.py` | 上述资源目录在位 + Python（只用 `struct`，无第三方依赖） |
| `gen_portrait_frame_table.py` | **R1-C 生成器**：读 `Resources/Clover/D2/UI/FrontEnd/{amazon,barbarian}/{fw,bw}_{i}.png` 的 IHDR 宽高 ⇒ 把「创角屏**转身过渡逐帧矩形**」的尺寸表**就地重写进** `client/Assets/Scripts/UI/UiLayoutFlow.cs` 的 `ClassMenu.Transition` 标记区（167 帧 = Amazon 54+30 / Barbarian 64+19）。为什么它是判据资产：这张表就是「点人物动画不再变形」（用户 2026-09-20 投诉）这条判据的**唯一依据**，删了就得手抄 334 个数。复核 = `uicheck` 第 ⑰ 节逐帧回读 PNG 比对（167 帧、0 例外才算过） | `python tools/probes/gen_portrait_frame_table.py`（幂等；只改两个标记之间；`--print` 只打印不落盘） | Python 3.12（只用 `struct`，无第三方依赖）+ 上述 PNG 在位 |
| `measure/r1b_water_tiles.py` | **R1-B 量法脚本**：逐格解 `MapGenTownLayout` 的 floor/object 键 ⇒ ① 被引用但取不到文件的键（判 `MapView` 会不会走占位菱形）② 每张被引用 PNG 的**唯一色数/均值 RGB/不透明像素覆盖率**（唯一色数=1 ⇒ 平色瓦片 ⇒ 静态渲染必然像占位色块）③ 与 `GroundColor(Exit)` 亮青 (0,217,255) 对账（排除"出口占位"假设）。结论：河带 wall 层 `moor_river/028` 是**唯一**的平色被引用瓦片（RGBA(0,32,68)，49 格 x=47/54）；引用键 295 个、取不到 **0** 个 ⇒ 亮青占位在营地无处触发 | `python tools/probes/measure/r1b_water_tiles.py`（脚本自己向上找含 `client/Assets` 的仓库根，任意 cwd 都可） | Python 3.12 + `Pillow` + `client/Assets/Resources/Clover/D2/{Tiles,Objects}` 在位 |
| `hosts/mapcheck/Program.cs` `Step15_FlatWaterWallNotOverlaid`<br>`hosts/playercheck/Program.cs` `Step4_Unreachable` / `Step4b_BridgeWaterFallback` | **R1-B 的对账断言**：mapcheck 钉住「平色水墙瓦片不叠」的白名单（恰 1 个键）/ 命中 **49 格**全在 x=47,54 / 反例全 false / manifest 与 PNG 字节数代理证据 / 这 49 格**仍是水=阻挡**；playercheck 钉住「点不可走格 ⇒ 最近可走格回退（≤2 格）」的三条边界（图外 + 图内 Void + 河中央仍拒绝）与桥/水专项（点栏杆 ⇒ 走桥面、点水面 ⇒ 走岸上） | `dotnet run --project tools/probes/hosts/mapcheck -c Release`<br>`dotnet run --project tools/probes/hosts/playercheck -c Release` | 同上面「hosts/<宿主名>/」那一行 |
| `measure/scan_panel.py` | 量 `ControlPanel.png` 底图内「暗格」的真实 x 区间（判原版到底几格、在哪） | `python scan_panel.py` | `Pillow` + 该 PNG 在位 |
| `measure/scan_refhud.py`<br>`measure/scan_refhud2.py`<br>`measure/scan_refhud3.py` | 量原版 HUD 参考图（`refs/refhud_half*.png` / `refpanel_*.png`）的分区与色值 ⇒ 定我们的 HUD 尺寸/位置 | `python scan_refhud.py`（2/3 同理） | `Pillow` + `refs/` 在位 |
| `measure/crop_panel.py`<br>`measure/crop_refhud.py`<br>`measure/crop_refhud2.py`<br>`measure/crop_refhud3.py` | 把底图/参考图的左带·右带·HUD 半张**放大 3×切出来**（人眼在联络图上判细节）；产出就是 `refs/` 里那 10 张 png | `python crop_panel.py`（其余同理；脚本内 `out` 写死 `.ai-tmp\test`，迁移后要改到 `.ai-tmp\screenshots\` 或本目录） | `Pillow` + 对应底图在位 |
| `refs/refbar_0..2.png`<br>`refs/refhud_half0/1.png`<br>`refs/refpanel_0..2.png`<br>`refs/left_band.png`<br>`refs/right_band.png` | **原版参考裁图**（10 张）：血球条分帧、HUD 左右半张、控制面板 3 格、面板左右带放大图 —— 表现类判据的「原版一侧」 | 只读图；`measure/scan_*.py` 与 `crop_*.py` 拿它们当输入/产出 | 来源 = `原版资源/` 解码出的原版素材 |
| `hosts/<宿主名>/`（13 个宿主，共 40 个文件：每个 `*.cs` + `*.csproj`，`shim\*.cs` 11 个）<br>目录：audiocheck / buildcheck / combatcheck / corecheck / dircheck / flowcheck / fullcheck / itemcheck / mapcheck / movecheck / playercheck / savecheck / uicheck | **离线自检宿主**（非 Unity 工程、不参与打包，秒级出结论）：把「Unity 托管 DLL + `Assets/Scripts` 源码」直接编到 .NET 上，跑真断言（数值 / 逻辑 / 布局常量 / 配表 / 存档原子性 / 引擎下沉后的行为回归） | `dotnet run --project tools/probes/hosts/<宿主名>/<Name>.csproj`<br>例：`dotnet run --project tools/probes/hosts/uicheck/Uicheck.csproj` | .NET SDK（`net10.0`）；Unity 6000.6.0f1 的托管 DLL（Unity 托管 DLL 由 `hosts/Directory.Build.props` 统一解析（默认 `$(ProgramFiles)\Unity\Hub\Editor\6000.6.0f1\Editor\Data\Managed\UnityEngine\*.dll`，可用 `UNITY_EDITOR_ROOT` 覆盖））；编 `UI/**` 的宿主（uicheck / flowcheck / fullcheck / combatcheck / itemcheck / corecheck / savecheck / buildcheck…）还要 `client/Library/ScriptAssemblies/UnityEngine.UI.dll`（`Library/` 不入仓 ⇒ 需用户打开一次编辑器重建；另见下面「宿主的两类环境依赖」）；csproj 里对源码用 `..\..\..\..\client\...`、对引擎用 `..\..\..\..\..\clover-client-unity-engine\...`（**4 层 / 5 层** —— 宿主在 `tools/probes/hosts/<名>/` 比原 `.ai-tmp/hosts/<名>/` **深了一层**；旧文写「相对深度相同、迁移后引用依然成立」是**错的**，那正是 8 个宿主 CS0246 的成因，2026-09-20 已修）；`uicheck` 复用 `..\flowcheck\shim\EngineShim.cs`（13 个宿主必须**一起**在 `hosts/` 下，否则该引用断） |
| `hosts/buildcheck/frame_probe_out.txt` | **探针实测产物 = buildcheck E 组（多帧条带帧矩形）的比对基准**：`Assets/Editor/AssetImporter.cs` 的 `MultiFrameStrips` 表必须与它**逐条相同**（代码 == 实测，不许转录误差）。删了 / 没生成 ⇒ buildcheck 报 `[FAIL] frame_probe_out.txt（实测输出）存在`（实测 2026-09-20）。**是判据资产 ⇒ 随宿主入仓**，不是一次性产物 | 重生成：`python tools/probes/hosts/buildcheck/frame_probe.py`（产物写在**脚本所在目录**；退出码 0 = 自证通过「每帧窗口含本帧内容且不与邻帧重叠」） | Python 3.12 + `Pillow` + `client/Assets/Resources/Clover/D2/UI/{Panel,Menu}` 下那 5 张条带 PNG 在位 |
| `ledger/dispatch-log.tsv` | 派活台账（**49 条数据行**，含 R1 七片：R1-A..R1-F + 证据批）：谁 / 何时 / 什么任务 / 覆盖范围 —— `tools\verify.ps1` 第 15 项 `impl-by-executor` 的判据源；`# adjudicated:` 行是例外登记通道 | 只读（`verify.ps1` 仍从 `.ai-tmp\test\` 读同名文件，本份是入仓副本） | 与 `.ai-tmp\test\dispatch-log.tsv` 内容一致（**最近一次逐字节同步：2026-09-20 play-budget 对齐轮**）；现存 2 条生效的 `# adjudicated:`：`path-reachability:reference-pictures` / `freeze-before-capture`；原 3 条 `# adjudicated: play-budget` **已撤**（阈值按 skill §2.6 废除，理由改由 `verify.ps1` 第 22 项逐行判），原理由转为 `#` 留痕注释 |
| `ledger/play-log.tsv` | 进 Play 台账（**23 条数据行**，含 R1 批次的 4 条 `r1-batch-evidence`）：时间 / 执行者 / 片名 / **为什么必须进这条链** —— `verify.ps1` 第 22 项 `play-budget` 的判据源 | 只读（同上，`verify.ps1` 读 `.ai-tmp\test\play-log.tsv`） | 4 列 TSV；**行数不设上限**（skill §2「进 Play 记账，但不设上限」）—— `verify.ps1` 第 22 项只判「每行第 4 列是否写了理由（≥4 字）」，行数仅作 INFO 打印；原 `$playBudget = 8` 阈值与其 `# adjudicated: play-budget` 静默通道**已按 skill §2.6 废除** |
| `interact/p_runbg.cs` | `eval_file` 片段：让 Play 在编辑器失焦时全速跑（`Application.runInBackground=true`、`vSyncCount=0`、`targetFrameRate=60`）+ 打 `[RUNBG]` / `[HB]` 行 —— 治 skill P-2「失焦不 tick」 | `unity command eval_file --path tools/probes/interact/p_runbg.cs`（原位于 `client/_dev/p_runbg.cs`，该目录本轮已删） | 编辑器已开 `client/` + `unity` CLI |
| `measure/s1_common.py`<br>`measure/s1_value_diff.py`<br>`measure/s1_selftest.py`<br>`measure/s1_plan.py`<br>`measure/s1_plan.tsv` | **S1 数值维比对器全套**（「官方 1.10f txt ↔ 我们的运行时表」逐字段对账）：`s1_common.py` = 字段映射表（运行时列 ↔ 官方 `文件:列/公式`，与 `convert.py` 的 build_* 一一对应）+ 官方载体定位；`s1_value_diff.py` = 比对器（**期望值直接调 `tools/table-convert/convert.py` 的 build_***，不重写任何公式；官方载体不在位 ⇒ 行结论一律 `缺官方值`，⛔ 一个 `一致` 都不许有）；`s1_plan.py` → `s1_plan.tsv` = **821 行**逐行比对计划（行数 == 闸门 `coverage-filled` 报的 821；⛔ 只写"要比什么"，不填任何推定值）；`s1_selftest.py` = 两次自检 + 两条守卫 | 比对器：`python tools/probes/measure/s1_value_diff.py [--src "<txt 根\|*.mpq>"] [--rows <file>] [--probe] [--gate-artifacts]`（`--src` 省略时先试环境变量 `D2SRC_DIR`，再试项目内默认落点 `原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel`）<br>只重生成计划：`python tools/probes/measure/s1_plan.py`<br>自检：`python tools/probes/measure/s1_selftest.py`（退出码 0 = 全过）<br>**退出码三分**：`0` = 比过且 0 个 `不一致` / `1` = 有 `不一致` / `2` = 官方载体不在位（BLOCKED，**根本没比**——⛔ 别把 2 当成通过） | Python 3.12（只用标准库）；`--src` 需官方 1.10f 的 17 个 txt（缺哪个点名报哪个；`*.mpq` 需**先解包**，本仓无 MPQ 解包器 ⇒ 只打印指引）。⚠️ `策划/数值文档/*_c.txt`（转写件）与 `client/Assets/StreamingAssets/Table/*.tsv` **逐格相同**（实测 10/10 表 row-level-diff=0）⇒ 转写件**不进结论**，只作 `s1_value_diff.trans.tsv` 的漂移交叉校验（与运行时表互比是同义反复，一个总能变绿的"判据"等于没判） |
| `.ai-tmp/test/s1/UNBLOCK.md`（**一次性产物，不入仓**） | S1 阻塞点可执行清单：缺的 3 样东西放哪 / 每样到手后跑的确切命令 / 影响 821 行 / 「官方到手也仍判不了」的列清单（本项目编号、中文名、monumod 的 7 个登记占位）+ `id`↔`official_id` 这类口径说明 | 只读 | — |
| `.ai-tmp/test/s1/s1_selftest.md`（**一次性产物，不入仓**） | 自检记录：两次自检 + 两条守卫的**完整原文** + 注入缺陷的定位行 + 转写漂移清单 | 只读（重生成：`s1_selftest.py` 后按 `_evidence.py` 的写法汇总；正式判据是 `s1_selftest.py` 本身） | — |
| `.ai-tmp/screenshots/s1_value_diff.{tsv,rows.tsv,json}`<br>`.ai-tmp/test/s1/s1_value_diff.{tsv,rows.tsv,json}` | 比对器产物（**机器可读**：tsv/json ⇒ 闸门 `numeric-log-only` 认）：`*.tsv` = 逐 (矩阵行 × 字段) 的 `行号/实体id/官方来源文件:列/官方值/我们的值/差值/结论`；`*.rows.tsv` = 逐矩阵行一行（821 行）；`*.json` = 汇总结论 | 只读；重生成 = 跑比对器（`--gate-artifacts` 同时落 `.ai-tmp/screenshots/`） | ⛔ 产物内**无时间戳** ⇒ 同一输入复跑逐字节一致（幂等，可机械复检） |

### 宿主的两类「环境依赖」（不是缺陷，别当成红去修代码）

| 依赖 | 谁需要 | 不在位时的正确表现 |
| --- | --- | --- |
| `client/Library/ScriptAssemblies/UnityEngine.UI.dll`（+ `Unity.2D.Sprite.Editor.dll`） | 编 `UI/**` 的宿主（uicheck / flowcheck / fullcheck / combatcheck / itemcheck / corecheck / savecheck / buildcheck…） | **编译期硬失败**（CS0246）—— `client/Library/` 不入仓（`.gitignore`）⇒ 让用户打开一次编辑器重建，再跑。**别去改断言** |
| `原版资源/`（skill §1.9 规定的下载/解包素材落点） | `uicheck` 的两条「原版依据在磁盘上」断言（`原版资源/d2text/chi_string.txt`、`原版资源/d2dc6/.../loadingscreen.dc6`） | 打 **`[SKIP]`**（= `Program.CheckOriginalRes`）：`原版资源/` 在 `.gitignore` 里明确**不进 git** ⇒ 干净检出必然没有，**计失败就等于"这台机器永远到不了 FAILED=0"**。资源**在位时照旧断言存在**（⛔ 在位不过就是真红）。2026-09-20 实测本机不在位（`Test-Path` 假 / `git ls-files 原版资源` 空 / 全工作区搜这两个文件各 0 命中），故 uicheck 的这两条走 `[SKIP]` |

### 与迁移强相关的两条事实

1. **`run_all_hosts.ps1` 就在 `tools/probes/hosts/run_all_hosts.ps1`**（旧文本写它还在 `.ai-tmp/hosts/` —— 那个目录现已不存在）。
   它按 `$PSScriptRoot\<宿主名>` 逐个 `dotnet run` ⇒ **位置是对的，11 个宿主源码目录都找得到**。
   跑法（在**仓库根**执行）：`powershell -NoProfile -ExecutionPolicy Bypass -File tools/probes/hosts/run_all_hosts.ps1`，
   末行 `TOTAL_HOSTS=11 FAILED=0` = 全绿（`tools/verify.ps1` 第 18 项 `offline-hosts` 的判据）。

   **⛔ 本节下面这段旧结论已被实测推翻（2026-09-20 就地更正，原文保留在后面作反面对照）**：

   > 旧文：「失败只剩 Library 不入仓这一条外部原因」+「mapcheck/playercheck 从仓库根跑就没事」。

   实测结论（三条，逐条有证据）：
   - **`client/Library/` 不是原因**：`client/Library/ScriptAssemblies/UnityEngine.UI.dll` **在盘上**
     （325632 字节，用户已开过一次编辑器）。当时 8 个宿主 CS0246 找不到 `Image`/`Text` 的真因是
     **csproj 的 HintPath 少一级 `..\`**（写 `..\..\..\client\...`，宿主迁移后应为 `..\..\..\..\client\...`）；
     另有 `Directory.Build.props` 第 31 行的裸 `<`（XML 非法 ⇒ MSB4024，13 个宿主**全**编不过）。
     两者均已修 ⇒ 与 Library 无关。
   - **cwd 相对写法不止 2 个宿主**：`ClientAssets = @"client\Assets"` / `ClientDataPath = @"client\Assets"`
     存在于 **4 个**宿主（`playercheck` / `combatcheck` / `flowcheck` / `itemcheck`；**`mapcheck` 里没有**这个常量）。
     `run_all_hosts.ps1` 用 `Push-Location <宿主目录>` 驱动 ⇒ 被解析成 `<宿主目录>\client\Assets`（不存在）
     ⇒ 配表 0 行 ⇒ 断言红；`itemcheck` 还会因此抛 `NullReferenceException`（`Program.cs:734`，
     进程 exit = `-1073741819`）。
   - **`mapcheck` 的真因是写死的层数**：`ReadTsv` 按「`AppContext.BaseDirectory` 上数 6 层 = 仓库根」取
     `策划/数值文档/*.txt`；那是宿主还在 `.ai-tmp/hosts/<名>/bin/<cfg>/<tfm>/` 时的层数，
     迁到 `tools/probes/hosts/<名>/bin/<cfg>/<tfm>/` 后**少一层** ⇒ 落点变成 `<仓库根>\tools`
     ⇒ `[WARN] 配表源文件不存在` + `读不到 策划/数值文档/{level_c,monster_c}.txt`（与 cwd 无关，两种跑法都红）。

   **口径已统一（本目录内不再有第三种写法）**：4 个 cwd 相对常量 + `mapcheck.ReadTsv` 的写死层数
   全部改成「从 `AppContext.BaseDirectory` 逐级向上找**含 `client/Assets` 的那一层**」= 仓库根
   （与 `corecheck` / `fullcheck` / `savecheck` / `uicheck` 已有的 `ResolveProjectRoot()`、
   `buildcheck` 的 `FindRepoRoot()` 同一套）；Python 侧 `frame_probe.py` / `gen_assets.py` 用同样的
   `_find_root()`（旧写法 `HERE/../..` 在宿主搬家后指到 `tools/probes`）。
   ⇒ **从仓库根跑、`Push-Location` 到宿主目录跑，结果一致**（`verify.ps1` 驱动的是后者）。
2. **`net10.0\*.cs` 不在本目录**：那 19 个是 MSBuild 生成的
   `obj\Debug|Release\net10.0\.NETCoreApp,Version=v10.0.AssemblyAttributes.cs`（每个宿主 1 份、uicheck 2 份），
   属 `obj/` 构建产物，已随 P6 一起删除（不进仓）。

## 宿主的「沙盒落点」（2026-09-20 闸门/卫生对齐轮）

**槽位档（`saves/*.json`）不再落仓库 —— 落 `<仓库根>/.ai-tmp/test/host-setting/<宿主名>/saves/`**，
由宿主自己**每次跑前清空**（`HostSandboxSettingDir()`，就在各自的 `ResolveProjectRoot()` 旁边）。

- **为什么**：`Module/Save/SaveModule.cs:96-98` 在 `Game.Config` 为空时回落**相对目录** `"setting"`，
  而 `itemcheck` / `fullcheck` 原先**从不给** `Game.Config` ⇒ 槽位档落在 `<调用方 cwd>/setting/saves/`。
  三个后果都实测到了（2026-09-20）：
  1. 从仓库根跑 `dotnet run --project tools/probes/hosts/fullcheck` ⇒ 在**仓库根**留一份**未入仓**的
     `setting/saves/{Broken,OldHero,FullCheckHero}.json`（违 skill §1.8「一次性产物只许 `.ai-tmp/test/`」，
     `git status` 里是 `?? setting/`）；
  2. `run_all_hosts.ps1`（`Push-Location <宿主目录>`）⇒ 写进宿主目录下那份**已入仓**的
     `setting/saves/`，即「**验证器每次跑都改脏它验证的检出**」（`git status` 每次多一条
     ` M tools/probes/hosts/fullcheck/setting/saves/FullCheckHero.json`）；
  3. 上一次跑剩下的槽位文件会让 `itemcheck` 的「旧键懒迁移」断言**假通过**（先读到已存在的槽位档就不再迁移）。
- **处置**：两个宿主在 `Main`/`Step0_Host` 里显式
  `Game.Config = new GameConfig { SettingDir = HostSandboxSettingDir("<宿主名>") }`
  ⇒ 不依赖 cwd、不留仓库残留、断言真正从零开始。**业务断言一字未改**。
- **顺带删掉 4 个「误入仓的宿主产物」**（不是夹具：全仓 grep 无任何脚本读它们，且宿主会自己重写）：
  `hosts/itemcheck/setting/saves/{OldHero.json,Broken.json,Broken.json.corrupt}`、
  `hosts/fullcheck/setting/saves/FullCheckHero.json`。
  ⛔ 日后若要给宿主带「预置存档」跑，请把夹具放进 `.ai-tmp/test/` 并在脚本里显式指定，别再落回宿主目录。

## ✅ 清理留痕

本轮（仓库卫生整理）把「取证产物」和「判据资产」分开落位，留下的痕迹如下 —— 两者都在 `.gitignore` 之外**有意保留**：

| 目录 | 是什么 | 来历 |
| --- | --- | --- |
| `.ai-tmp/screenshots/` | **410 个文件 / 380.0 MB**：401 张 png（400 张实机取证 + `p41_pathcheck.png`）+ 5 个 `*.index.tsv`（联络图索引）+ 4 个探针原文 txt（`p8_keys.txt` / `p8_click.txt` / `p42_evidence_run1.txt` / `p42_evidence_run2.txt`） | 原先全在 `client/Assets/Screenshots/`（+`Assets/Temp/p41_pathcheck.png`）—— 那是 skill §8 明令**禁止**的位置（⛔ 取证截图不进 `client/Assets/**`）。本轮整体搬到 `.ai-tmp/screenshots/`（**保留原文件名**），并把 `策划/验收表.md` 里 142 处 `Screenshots/xxx` 引用机械改写成 `.ai-tmp/screenshots/xxx`。原 `*.png.meta`（400）/`*.index.tsv.meta`（5）/`*.txt.meta`（4）随 Unity 目录一起删掉；`Assets/Screenshots/`、`Assets/Temp/` 两个目录（含 `.meta`）已整体删除。`.ai-tmp/` 是 gitignore 目录 ⇒ 这批图**不入仓**，只作本机留档 |
| `tools/probes/ledger/` | 两份台账的**入仓副本**（`dispatch-log.tsv` / `play-log.tsv`） | `verify.ps1` 第 15/22 项仍从 `.ai-tmp/test/` 读它们，而 `.ai-tmp/` 不入仓 ⇒ 本轮**复制**一份到这里（原处保留），让"谁派了什么活 / 进了几次 Play"这条判据在仓库里可追溯。**2026-09-20 闸门对齐轮再次逐字节同步**（并撤下两条已过时的 `# adjudicated:`：`freshness:u1-rows` / `freshness:u2-rows` —— 两项判据已改成按验收表行取证据名，静默通道不再需要）。**2026-09-20 play-budget 对齐轮第三次逐字节同步**（并撤下 3 条已失效的 `# adjudicated: play-budget`：阈值按 skill §2.6 废除，理由改由 `verify.ps1` 第 22 项逐行判） |
| `.ai-tmp/test/host-setting/<宿主名>/` | **宿主槽位档沙盒**（`itemcheck` / `fullcheck` 每次跑前清空重建；宿主自己建，不需手工准备） | 2026-09-20 闸门/卫生对齐轮：此前它落在 `<调用方 cwd>/setting/` —— 从仓库根跑就在**仓库根**留残留、被 `run_all_hosts.ps1` 驱动就改脏**已入仓**的宿主目录。详见上面「宿主的沙盒落点」 |

> ⚠️ `.ai-tmp/screenshots/` 里的 png 是**当轮冻结**的证据（迁移用 `File.Move`，**修改时间原样保留**）。
> `tools/verify.ps1` 本轮只改了 **一行路径**：`$shots = Join-Path $client 'Assets\Screenshots'` → `Join-Path $root '.ai-tmp\screenshots'`
> （P15/P16 把那个目录整体搬走/删掉了），**检查逻辑一个字未改**。由此产生一个已知副作用，供复核：
> 第 7 项 `path-reachability` 用**区分大小写**的正则 `Screenshots/(...\.png|txt)` 抓引用，
> 而 P17 把表里的引用改成了小写 `.ai-tmp/screenshots/...` ⇒ 该项现在报 `0 screenshot reference(s)`（空判）。
> **人工复核结果**（同一套正则 + 区间展开，把 `$shots` 指到 `.ai-tmp\screenshots`）：引用 raw=147 / 唯一 86 条，
> `Test-Path` **缺 0 条**。⇒ 路径本身是可达的，只是自动判据眼下看不见它们。

## 覆盖率闸门：`tools/verify.ps1` 第 25~29 项（T0 穷举覆盖）

> 依据 `patterns/full-coverage-audit.md` §7 + `scaffold/coverage-matrix.md`。这 5 项原先只写在**提示词**里
> ⇒ 结构上永远不会被执行；现在落成闸门里**能测红**的条目（SKILL §0.5「提示词是请求，闸门才是保证」）。
> 列名按派活契约：`策划/实体清单.tsv`（维度/实体/载体·路径/出处/状态数/判据类型/归属片）、
> `策划/状态矩阵.tsv`（维度/实体/状态·事件/边界值/期望表现(出处)/实测/结论/证据）、
> `策划/差异登记.tsv`（是什么/为什么/出处/何时消除）；`#` 开头为注释行。

| 项 | 名字 | 判据（一句话） | 怎么复现红 |
| --- | --- | --- | --- |
| 25 | `coverage-rows` | 清单数据行数 == 矩阵去重(维度,实体)数，**且**矩阵数据行数 == 清单 Σ状态数 | 改一个「状态数」，或从矩阵删/加一行 |
| 26 | `coverage-filled` | 矩阵零空行：每行 `实测`/`结论`/`证据` 非空，且 `结论` 属三种合法取值 | 删掉某行最后一列（证据） |
| 27 | `coverage-diff` | `不一致*` 计数 == 0；每条 `允许的差异*` 都按「是什么」在 `差异登记.tsv` 找到四要素齐的登记行 | 写一条 `不一致(...)`；或把登记的「何时消除」留空 |
| 28 | `coverage-acceptance` | `策划/验收表.md` 每条编号判定行都至少引用一个在盘路径（`.ai-tmp/screenshots/` / `tools/probes/` / `策划/` / `w1_host_*.txt`） | 删掉某行正文里的路径引用 |
| 29 | `coverage-dimensions` | 15 个维度码（D1..D12 / S1..S3）在实体清单里每个 ≥1 行 | 删掉某维度的全部行 |

- ⛔ **不设裁定通道**：`策划/实体清单.tsv` 或 `策划/状态矩阵.tsv` 不存在 ⇒ 这 5 项**一律 FAIL**
  （不许 skip / PASS / 降级 INFO，也不给 `# adjudicated:` 豁免）—— 表还没产出就是「没做完」，红得对。
- **沙盒复现法（⛔ 不动真表）**：把 `tools/verify.ps1` 复制到 `<临时目录>/tools/verify.ps1`，
  在同级建 `策划/` 放三张表 + `验收表.md`，直接跑那个副本 —— `$root` 按脚本位置推导，整套检查都在沙盒里跑。
  本轮的通过/失败夹具就是这么造的一次性产物（按 §1.8 用完即删）。
- **一项已登记的口径差**：模板 §7 第 1 条的字面是「实体清单行数 == 验收表判定行数」，而本项目验收表是
  47 行**系统级**汇总、矩阵是**实体级**穷举 ⇒ 第 25 项按「清单 ↔ 矩阵」对账（输出里带
  `note=grain: manifest<->matrix ...`），验收表那侧的关系由第 28 项兜住。

## 采样档位（scale-tier）—— 本项目闸门第 38 项的判据源

> **本工程的采样档位声明已归位到规格文档**（模板 `reference/verify-template.md` 要求的「闸门 0 只判一次」位置）：
> **`策划/策划案/暗黑破坏神2参考规格.md` → `## 0. 形态（闸门 0，只判一次）`** 的表格内。
> 本文件**不再重复声明**（避免两处漂移）；`tools/verify.ps1` 第 38 项 `scale-tier` 读 `策划/策划案/*.md`，照旧 PASS。
> （2026-09-22：原「档位声明」一小段由本轮移入规格文档，本处改为指向规格文档的一行指针。）

## 闸门与模板对齐（2026-09-22 闸门对齐片）

`scripts/gate-sync.ps1` 要求「本项目实现的检查项 **⊇** 模板 GATE-ITEMS 清单」。
原先 `tools/verify.ps1` 用 `Pass/Fail/HumanOnly` 包装函数输出，而 gate-sync 按
`Say '<STATUS>' '<name>'` 抽项名 ⇒ **0 命中**、30 项全报 missing（**闸门与模板脱节**）。
本轮把输出形态改成 `Say '<STATUS>' '<name>' <detail>`（**判据一字未放宽**：只改形态 / 改名），并补齐真缺的项。

| 模板项名 | 本项目原名 | 处置 |
| --- | --- | --- |
| `acceptance-table` | `table-summary` | 改名（判据不变：汇总数 == 表体行数） |
| `allowed-diff` | `allow-diff-registry` | 改名 |
| `screenshot-refs` | `path-reachability` | 改名（子项 `path-reachability:reference-pictures` 保留原判据） |
| `evidence-freshness` | `freshness:u2-rows` | 改名（按行判：该行证据晚于该行实现文件） |
| `reference-table` | `six-dim-parity` | 改名（原版值(出处) / 我们的值 / 差值） |
| `row-category` | `table-category` | 改名 |
| `play-ledger` | `play-budget` | 改名（不设次数上限，只判每行第 4 列有理由） |
| `engine-credit` | `engine-selfname` | **替换**（后者是源码 grep ⇒ 永久 HUMAN-ONLY；新项 = 半机判 + 半 HUMAN-ONLY） |
| `numeric-log-only` / `verify-entry` / `spec-doc` / `asset-research-doc` / `baseline-images` / `no-assets-screenshots` / `no-team-sessions` / `graphics-device` / `scale-tier` / `impact-radius` | （无） | **新增**（判据照 `reference/verify-template.md`，接到本项目真实数据） |

新增项的判据源速查：

| 项 | 判据源 / 怎么复现红 |
| --- | --- |
| `baseline-images` | `tools/probes/refs/*.png`（**原版参考裁图** 10 张，本文件上面已登记）；0 张 ⇒ FAIL |
| `graphics-device` | `client/Logs/Editor.log` 的 `[D3D12 Device Filter] Device Name:` 行；命中 `WARP`/`Basic*` ⇒ FAIL；无该行 ⇒ HUMAN-ONLY |
| `engine-credit` | `tools/probes/refs/engine_credit.txt`（**实机跑出来的 UI 标签文本回读**）；文件不存在 ⇒ HUMAN-ONLY（⛔ 不许用源码 grep 冒充实机判据）；文本不含 `by clover-engine` 逐字 ⇒ FAIL |
| `scale-tier` | 本文件上面的「采样档位」段 + `策划/策划案/*.md` |
| `numeric-log-only` | `策划/验收表.md` 的 `数值类` 行：只挂日志行 ⇒ HUMAN-ONLY；一个在盘产物都不挂 ⇒ FAIL |
| `spec-doc` / `asset-research-doc` | `策划/策划案/*.md` / `策划/素材调研.md` 存在且非空 |
| `no-assets-screenshots` | `client/Assets/Screenshots` 目录存在 ⇒ FAIL（只看这一个目录，别按 `client/Assets/**` 下的 png 判 —— 本工程那里有正式美术 png，那样判是假红） |
| `no-team-sessions` | `.codebuddy/teams/` 下的**会话产物文件**数 > 0 ⇒ FAIL（空目录不算 —— 模板第 11 条：判真实产物，不判「目录存在」） |
| `verify-entry` | `tools/verify.ps1` 存在、非空、**语法 0 错** |
| `impact-radius` | `.ai-tmp/test/impact-radius.tsv` 每行 ≥ 3 列 |

## S1 数值维比对器（2026-09-22 S1 片）—— 判据**不许**放宽的三条

> 这一节是给**下一个读这份 README 的人**看的：下面三条都是实测踩出来的，改掉任何一条 =
> 让 821 行红行"假装变绿"，比不判更糟。

1. **官方值只认官方载体**：`<根>/data/global/excel/*.txt`（或由 `*.mpq` 解出来的同一批 txt）。
   ⛔ **不许**拿 `策划/数值文档/*_c.txt`（转写件）当官方值 —— 实测它与
   `client/Assets/StreamingAssets/Table/*.tsv`（运行时表）**逐格相同**（10/10 张表 row-level-diff=0），
   二者是同一份转写的两个落点；拿它去比运行时表是同义反复，**永远只会报"一致"**。
   转写件的正确用途只有一个：官方载体到位后做**漂移交叉校验**（`s1_value_diff.trans.tsv`）。
2. **期望值不重写公式**：比对器直接 `import` `tools/table-convert/convert.py` 并调它的 `build_*`。
   ⛔ 不许在比对器里再抄一遍列映射/公式/取整口径（两边各写一遍 = 各错一半，且判据会漂移）。
   `s1_common.FIELD_MAP` 只管**标签**（"官方哪张表哪一列/什么公式"），**不管取值**；
   自检 `[guard-2]` 会核对它的列序与 `convert.py` 的 `Sheet.cols` 完全一致。
3. **`缺官方值` ≠ `一致`**：官方载体不在位 ⇒ 821 行全部 `缺官方值`，比对器退出码 `2`（BLOCKED，根本没比）。
   `id`（本项目顺序编号）/ `name`（中文显示名）/ `skill_class` / monumod 的 7 个登记占位
   **天然**没有官方对手 ⇒ 单列落 `缺官方值` 且不计入行结论；该集合钉死在
   `s1_common.EXPECTED_NO_CARRIER`，自检 `[guard-1]` 每次核对（偷偷放宽 ⇒ 立刻报 FAIL）。
   逐列明细仍全部落盘可见（`s1_value_diff.tsv`），不是在藏。
   ⇒ 用户拿到官方表的**一把钥匙**：缺什么 / 放哪 / 到手后跑什么 ⇒ `.ai-tmp/test/s1/UNBLOCK.md`。
