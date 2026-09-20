# tools/probes —— 判据资产（删了就不能重判同一件事）

> 依据：全局 clover-engine skill §1.8 —— 一次性产物进 `.ai-tmp/test/`（用完即删），
> **判据资产**（探针 / 驱动 / 量法脚本 / 参考裁图 / 台账）**不算一次性** ⇒ 落 `tools/probes/` 并**入仓**。
> 本目录由「仓库卫生整理」这一轮从 `.ai-tmp/` 迁出（2026-09-20），原处不留副本。
>
> ⚠️ 这些文件是**当轮取证的原样记录**，内部写死了当时的位置（`.ai-tmp\drivers\...`）
> 与绝对路径（`clover-project-diablo2`）。**迁移时一个字未改**，
> 所以下面的「怎么跑」列写的是**真实命令 + 需要先改的那一处变量**。
>
> 共同依赖：Unity 6000.6.0f1（`C:\Program Files\Unity\Hub\Editor\6000.6.0f1`）、
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
| `measure/scan_panel.py` | 量 `ControlPanel.png` 底图内「暗格」的真实 x 区间（判原版到底几格、在哪） | `python scan_panel.py` | `Pillow` + 该 PNG 在位 |
| `measure/scan_refhud.py`<br>`measure/scan_refhud2.py`<br>`measure/scan_refhud3.py` | 量原版 HUD 参考图（`refs/refhud_half*.png` / `refpanel_*.png`）的分区与色值 ⇒ 定我们的 HUD 尺寸/位置 | `python scan_refhud.py`（2/3 同理） | `Pillow` + `refs/` 在位 |
| `measure/crop_panel.py`<br>`measure/crop_refhud.py`<br>`measure/crop_refhud2.py`<br>`measure/crop_refhud3.py` | 把底图/参考图的左带·右带·HUD 半张**放大 3×切出来**（人眼在联络图上判细节）；产出就是 `refs/` 里那 10 张 png | `python crop_panel.py`（其余同理；脚本内 `out` 写死 `.ai-tmp\test`，迁移后要改到 `.ai-tmp\screenshots\` 或本目录） | `Pillow` + 对应底图在位 |
| `refs/refbar_0..2.png`<br>`refs/refhud_half0/1.png`<br>`refs/refpanel_0..2.png`<br>`refs/left_band.png`<br>`refs/right_band.png` | **原版参考裁图**（10 张）：血球条分帧、HUD 左右半张、控制面板 3 格、面板左右带放大图 —— 表现类判据的「原版一侧」 | 只读图；`measure/scan_*.py` 与 `crop_*.py` 拿它们当输入/产出 | 来源 = `原版资源/` 解码出的原版素材 |
| `hosts/<宿主名>/`（13 个宿主，共 40 个文件：每个 `*.cs` + `*.csproj`，`shim\*.cs` 11 个）<br>目录：audiocheck / buildcheck / combatcheck / corecheck / dircheck / flowcheck / fullcheck / itemcheck / mapcheck / movecheck / playercheck / savecheck / uicheck | **离线自检宿主**（非 Unity 工程、不参与打包，秒级出结论）：把「Unity 托管 DLL + `Assets/Scripts` 源码」直接编到 .NET 上，跑真断言（数值 / 逻辑 / 布局常量 / 配表 / 存档原子性 / 引擎下沉后的行为回归） | `dotnet run --project tools/probes/hosts/<宿主名>/<Name>.csproj`<br>例：`dotnet run --project tools/probes/hosts/uicheck/Uicheck.csproj` | .NET SDK（`net10.0`）；Unity 6000.6.0f1 的托管 DLL（csproj 里写死 `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Data\Managed\UnityEngine\*.dll`）；uicheck 还要 `client/Library/ScriptAssemblies/UnityEngine.UI.dll`（`Library/` 不入仓，需用户打开一次编辑器重建）；csproj 里对源码用 `..\..\..\client\...`、对引擎用 `..\..\..\..\clover-client-unity-engine\...`（相对深度与原 `.ai-tmp\hosts\<名>\` **相同**，故迁移后引用依然成立）；`uicheck` 复用 `..\flowcheck\shim\EngineShim.cs`（13 个宿主必须**一起**在 `hosts/` 下，否则该引用断） |
| `ledger/dispatch-log.tsv` | 派活台账（42 条数据行）：谁 / 何时 / 什么任务 / 覆盖范围 —— `tools\verify.ps1` 第 15 项 `impl-by-executor` 的判据源；`# adjudicated:` 行是例外登记通道 | 只读（`verify.ps1` 仍从 `.ai-tmp\test\` 读同名文件，本份是入仓副本） | 与 `.ai-tmp\test\dispatch-log.tsv` 内容一致 |
| `ledger/play-log.tsv` | 进 Play 台账（18 条数据行）：时间 / 执行者 / 片名 / **为什么必须进这条链** —— `verify.ps1` 第 22 项 `play-budget` 的判据源 | 只读（同上，`verify.ps1` 读 `.ai-tmp\test\play-log.tsv`） | 4 列 TSV；预算见 `verify.ps1`（`$playBudget`） |
| `interact/p_runbg.cs` | `eval_file` 片段：让 Play 在编辑器失焦时全速跑（`Application.runInBackground=true`、`vSyncCount=0`、`targetFrameRate=60`）+ 打 `[RUNBG]` / `[HB]` 行 —— 治 skill P-2「失焦不 tick」 | `unity command eval_file --path tools/probes/interact/p_runbg.cs`（原位于 `client/_dev/p_runbg.cs`，该目录本轮已删） | 编辑器已开 `client/` + `unity` CLI |

### 与迁移强相关的两条事实

1. **`run_all_hosts.ps1` 没有跟着搬。** 它仍在 `.ai-tmp/hosts/run_all_hosts.ps1`（宿主名写死，按 `$PSScriptRoot\<宿主名>` 逐个 `dotnet run`）。
   宿主源码搬到 `tools/probes/hosts/` 之后，那 13 个宿主目录**还在**（只剩 `out.txt` / `setting\` / `_evidence\` 之类），
   于是脚本进到空目录 `dotnet run` ⇒ **实测末行 `TOTAL_HOSTS=11 FAILED=11`**，`tools/verify.ps1` 第 18 项 `offline-hosts` 因此报 **FAIL**。
   ⇒ 要批量跑请用上表的 `dotnet run --project` 逐行跑，或把该脚本的宿主根也指到 `tools/probes/hosts/`（本轮**未改**，见回报）。
2. **`net10.0\*.cs` 不在本目录**：那 19 个是 MSBuild 生成的
   `obj\Debug|Release\net10.0\.NETCoreApp,Version=v10.0.AssemblyAttributes.cs`（每个宿主 1 份、uicheck 2 份），
   属 `obj/` 构建产物，已随 P6 一起删除（不进仓）。

## ✅ 清理留痕

本轮（仓库卫生整理）把「取证产物」和「判据资产」分开落位，留下的痕迹如下 —— 两者都在 `.gitignore` 之外**有意保留**：

| 目录 | 是什么 | 来历 |
| --- | --- | --- |
| `.ai-tmp/screenshots/` | **410 个文件 / 380.0 MB**：401 张 png（400 张实机取证 + `p41_pathcheck.png`）+ 5 个 `*.index.tsv`（联络图索引）+ 4 个探针原文 txt（`p8_keys.txt` / `p8_click.txt` / `p42_evidence_run1.txt` / `p42_evidence_run2.txt`） | 原先全在 `client/Assets/Screenshots/`（+`Assets/Temp/p41_pathcheck.png`）—— 那是 skill §8 明令**禁止**的位置（⛔ 取证截图不进 `client/Assets/**`）。本轮整体搬到 `.ai-tmp/screenshots/`（**保留原文件名**），并把 `策划/验收表.md` 里 142 处 `Screenshots/xxx` 引用机械改写成 `.ai-tmp/screenshots/xxx`。原 `*.png.meta`（400）/`*.index.tsv.meta`（5）/`*.txt.meta`（4）随 Unity 目录一起删掉；`Assets/Screenshots/`、`Assets/Temp/` 两个目录（含 `.meta`）已整体删除。`.ai-tmp/` 是 gitignore 目录 ⇒ 这批图**不入仓**，只作本机留档 |
| `tools/probes/ledger/` | 两份台账的**入仓副本**（`dispatch-log.tsv` / `play-log.tsv`） | `verify.ps1` 第 15/22 项仍从 `.ai-tmp/test/` 读它们，而 `.ai-tmp/` 不入仓 ⇒ 本轮**复制**一份到这里（原处保留），让"谁派了什么活 / 进了几次 Play"这条判据在仓库里可追溯 |

> ⚠️ `.ai-tmp/screenshots/` 里的 png 是**当轮冻结**的证据（迁移用 `File.Move`，**修改时间原样保留**）。
> `tools/verify.ps1` 本轮只改了 **一行路径**：`$shots = Join-Path $client 'Assets\Screenshots'` → `Join-Path $root '.ai-tmp\screenshots'`
> （P15/P16 把那个目录整体搬走/删掉了），**检查逻辑一个字未改**。由此产生一个已知副作用，供复核：
> 第 7 项 `path-reachability` 用**区分大小写**的正则 `Screenshots/(...\.png|txt)` 抓引用，
> 而 P17 把表里的引用改成了小写 `.ai-tmp/screenshots/...` ⇒ 该项现在报 `0 screenshot reference(s)`（空判）。
> **人工复核结果**（同一套正则 + 区间展开，把 `$shots` 指到 `.ai-tmp\screenshots`）：引用 raw=147 / 唯一 86 条，
> `Test-Path` **缺 0 条**。⇒ 路径本身是可达的，只是自动判据眼下看不见它们。
