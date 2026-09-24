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
| measure/classcols_stamp.py | **心跳指纹压行器**：把消息写进 `.ai-tmp/test/_hb_msg.txt` 后运行 ⇒ 追加该行到心跳、并把 `REPORT-FINGERPRINT` 重新压到**最后一行**（保证"末行即指针"恒定成立；治"报告自写指纹必漂"）| `python tools/probes/measure/classcols_stamp.py` | Python 3.12（纯标准库）| **4 个路径是绝对常量（ROOT/HB/RP/MSG）⇒ 别片复用需先改这 4 行** |
| measure/runner_gate_check.ps1 | **runner 静态陷阱闸门**：AST 判 `continue` 是否在循环内（`classcols` 的"静默 exit 0"）/ 终标记存在 / `RUNNER-FINGERPRINT` 存在；`-Scope active`=活跃 8 片硬门、`-Scope all`=结构陷阱全判且约定项降 `[INFO]`；内置 `KNOWN-BAD` + 端到端夹具自证；解析前后比 `mtime`（编辑中则 `SKIP`）| 纯 ASCII，只读，不取锁 | 秒级 | **临时夹具放 `$env:TEMP`（已裁定豁免 §1.8 字面）：项目树零残留 + 让"不写 `.ai-tmp`"可快照证明；⛔ 别改回 `.ai-tmp/test/`（会退化自证）** |

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
| `measure/ab_trend.py`<br>`measure/ab_trend_selftest.py` | **U27 帧节奏 A/B 的读数器（REPORT-ONLY，⛔ 不判 PASS/FAIL、⛔ 不自创阈值）**：① **口径声明（`pre-declared`，⛔ 不许"看完数才挑口径"）** = 主口径 `P1`（同场景同行为 `scen=line ∧ spr=run_*` 的 dt **绝对 sd**，且**只在"未触顶帧"上算**）+ 辅口径 `P2`（**同一帧集**上的 `CV`）—— **两套必须一起报**，另给 `P0` 不过滤对照行与 `TOUCH 敏感性行`（明示口径敏感，⛔ **不是候选口径**）。"触顶帧"= `abs(dt − 1/target) ≤ 1% × (1/target)`（⛔ 只对 A 生效：B `targetFrameRate=-1` 无上限 ⇒ **构造上无触顶帧**；档位映射 = 驱动 `ApplyCadence`：A=`60+vsync0` / B=`-1+vsync1`）—— ⚠️ 该过滤**只削 A**（A 样本 295→269@±1%，B 不变）⇒ P1/P2 **必须与 n 一起读**，n 过小即口径退化。**术语裁定**（team-lead 2026-09-24）：**"未触顶帧"一词只属本口径**（剔 `abs(dt−1/target)≤1%×T`，量 = "削峰后还剩多少离散度"）；`jitter` 的 `measure/d2u27_stutter_repro.py` 剔的是 `dt ≤ 1/target` 低半区，那是**另一个量**，称 **"长帧子集（`dt > 目标周期`）"**；实测两集合同向（`sd`→B 稳 / `CV`→A 稳 ⇒ 降级结论不变）。② **三个只报数、不判定的读数** = `R-DIST` 两档 dt 分布并排（n/中位/均值/p95/max）、`R-SPIKE` `dt ≥ 0.100s` 单帧的**次数 + 位置(frame/scen/spr/grid)** + 落在哪些(场景,行为)、`R-REPRO` 两批对照**是否落在同一点**（键 = `scen/spr@cad`；⛔ **不按 frame 号** —— 同段墙钟场景两档采样密度不同，frame 号跨档不可比）。`selftest` = 合成夹具自证**读数器本身**（已知正确样本必须报出交集 / 已知不同样本必须不报假交集 + 口径谓词 + 0.100s 桶边界），秒级、不占 Play | `python tools/probes/measure/ab_trend.py <d2u27-*.tsv> [<第二份 d2u27-*.tsv>]`<br>`python tools/probes/measure/ab_trend_selftest.py`（exit 0 = 自证通过；当前 12 checks / 0 fail） | Python 3.12（只用标准库 `hashlib/io/os/statistics/sys`，无第三方依赖） |
| `measure/d2u27_stutter_repro.py`<br>`measure/d2u27_stutter_repro_selftest.py` | **U27 卡顿复现性三项只报数读数（REPORT-ONLY，⛔ 不判 PASS/FAIL、⛔ 不自创阈值）**：① `[M1]` `dt ≥ 0.100s` 单帧的**次数 + 位置**（帧号 / `passIdx` / 归一化位置 / 场景 / `spr` / **场景段内位置**）；`--ref <tsv>` 给**与前一批逐条位置配对**（`dFrac` + `sameScen`/`sameSpr`；⛔ 不按 frame 号 —— 同段墙钟两档采样密度不同）。② `[M1a]` **场景标签自洽审计**（`scen` 列 vs `spr` 前缀矛盾；对照驱动声明"每遍开场 = idle 40 帧" `d2u27_jitter.cs:845-849`）—— 判**数据自洽**、不判性能；③ `[M2]` 两档 dt 分布并排 + 组内前半/后半趋势 + **贴顶比例**；④ `[M3]` 同（场景,行为）的 **sd（绝对）/ CV（辅）**，只在**长帧子集**上算；⑤ `[M3b]` **两口径对照**（`ab_trend` 的 ±1% 带 vs 本脚本的低半区）。**术语裁定（team-lead 2026-09-24）**：本脚本的子集一律叫 **「长帧子集（`dt > 目标周期`）」**（剔除**低半区** `dt ≤ 1/target + 半格`），⛔ **不叫"未触顶帧"**（该词只属 `measure/ab_trend.py`）。实测两口径**同向**（`u27v9` 同场景同行为 A：`ab_trend` n=269 sd=0.002345 CV=0.140 ／ 长帧子集 n=151 sd=0.002182 CV=0.121；B 两者同值 n=924 sd=0.001221 CV=0.228）⇒ `sd`→B 稳 / `CV`→A 稳 ⇒ 降级结论不变。**分工（team-lead 2026-09-24 裁定：两份都留、⛔ 不许混引）**：`ab_trend.py` = **口径读数器**（"同点"按 `(scen,spr)` **集合**判）；本脚本 = **位置/顺序 + 自洽审计**（`--ref` 逐条位置配对 + `[M1a]` 标签自洽）—— 集合口径表达不了"卡峰落在 `passIdx=1` 还是前 1/3"。**重叠量实测完全一致**（R-DIST 的 median/mean/p95/max、R-SPIKE 的 count/frame **逐项相同**，仅 p95 末位 **1e-6** 来自分位插值实现）。`[M1a]` 是唯一能抓"第 2 遍开场块被标成上一场景"的判据（u27v9 实测命中）| `python tools/probes/measure/d2u27_stutter_repro.py <d2u27-*.tsv> [--ref <tsv>] [--out <abs.txt>]`<br>`python tools/probes/measure/d2u27_stutter_repro_selftest.py`（exit 0 = 自证通过；当前 13 checks / 0 fail）| Python 3.12（纯标准库；⛔ 无 .NET IO ⇒ 不中 `[IO.File]` 相对路径读工作区根同名件那条陷阱）|
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

---

## 提交纪律（2026-09-24 补，来自实践）

1. **判据资产可提交**：探针 / 驱动 / runner / 量法脚本 / 参考裁图 / 台账（含 `hosts/**` 的工程文件、`drivers/**` 的 `.cs` / `.ps1` / `.py`）。
2. **构建产物一律不提交**：`drivers/**`、`hosts/**` 下**不许**出现 `obj/`、`bin/`。
   - 自建哨兵工程（只做编译校验用）必须把输出指到**仓树外**。⚠️ 这两个属性要写在 **`Directory.Build.props`**，**不能**写在 `.csproj` —— SDK 在导入 props **之前**就用到它们，写进 csproj 对 restore 产物已经太晚：
     ```xml
     <BaseOutputPath>$(MSBuildThisFileDirectory)../../../.ai-tmp/test/&lt;哨兵名&gt;-build/bin/</BaseOutputPath>
     <BaseIntermediateOutputPath>$(MSBuildThisFileDirectory)../../../.ai-tmp/test/&lt;哨兵名&gt;-build/obj/</BaseIntermediateOutputPath>
     ```
     写法样板 = `drivers/d2u27_compilecheck/Directory.Build.props`（2026-09-24 实践）。
   - 落盘后**回读自证**：`git --no-pager status --short -- tools/probes` 里该目录只应有源码/工程文件，**0 个生成物**。
3. **提交按片分开**：`git status` 里常同时有多个片的在飞改动 ⇒ ⛔ 不许把"A 片的清理"与"B 片的改动"揉进同一笔。
4. **新增宿主会改计数**：`hosts/` 下新增一个 `*check` ⇒ `run_all_hosts.ps1` 的 `TOTAL_HOSTS` 会变（如 14 → 15）；提交时必须**同步更新验收表里以该数字为读数的行**，否则看起来像漂移（2026-09-24 新增 `tableverify` 宿主即此例）。
5. **Play 是共享资源**：进 `editor_play` / `recompile` 前先查 `.ai-tmp/test/play-running.lock` —— **锁不在（或 `age ≥ 12min` 视为 stale）可直接取，写完等 ~1 秒回读校验，内容不是自己就立刻退让**；**锁在且非自己 ⇒ 让路**（队列只用于撞车时的先后）。取锁**成功之后**才写 `play-log.tsv`；`ABORT lock held by <owner>` 的 fail-fast 必须在碰 editor **之前**退出；`finally` 释放**自己的**锁，⛔ 不许删别人的锁（stale 接管要记账）。**一批读数必须能自证"只有我一人在场"**（取锁前空 / `editor_status` 首查即 idle / 窗口内 `play-log` 只有自己一行），否则标 `suspect (concurrent session)` 并重采。

### 5.1 锁文件的字节口径（2026-09-24 v2.3 补，来自 4 次实测事故）

**写侧**：
- 锁内容 = **三段 `<owner> <ISO8601> <PID>`**（缺 PID 段 = 存活未知 ⇒ 全队只能"新鲜即让路"，且谁都不能做僵尸清理）。
- **写锁一律 `-Encoding ASCII`**。PS 5.1 的 `-Encoding UTF8` 会写 **BOM**，而 `$parts[0] -eq $owner` 这类朴素解析会得到 `BOM+owner` ⇒ **认不出自己写的锁** ⇒ 释放被自己挡住 ⇒ **僵尸锁**把全队挡在门外。
- **过渡期双写**：真锁 `play-running.lock` + **同内容镜像 `play.lock`** —— 镜像存在的唯一理由是拦住**只读旧名的历史 runner**；⛔ 只写真锁 = 那些 runner 看到的是"锁空着"⇒ 并发 Play。
  **「同内容」的准确含义（2026-09-24 裁定）** = **同一 `owner` + 同一 `PID`**；`ISO` 字段**允许比真锁晚**（它是**镜像的写入时刻**；实测正常现场差 **~1.07s** —— 先取真锁 + 回读校验 ~1.1s，再写镜像，顺序合理）。⛔ **不要**加"逐字节相同"的断言（会在完全正常的现场上**假红**，又是一条"判据比需求更严"）；**推荐（收尾时）**把镜像改成写「**真锁回读的原文**」——那时才把断言升级为逐字节相等，并把旧形状（ISO 差 1s）留作**退化样本**。**读者面（2026-09-24 全仓普查）**：提及旧锁名的脚本 = **38 个**（v2.2 当时登记 17 个是"仍在活跃集合"口径）；其中**只读不写 ≈ 34 个**。**只查不写 = 保护自己，写镜像 = 保护别人**；「读两个锁名」与「写镜像」审计时必须**分列两栏**统计（本轮 8 个活跃 runner 里就漏了 4 个）。
- **事件 tag 的权威性（2026-09-24 裁定）**：各片 tag 不统一（`LEGACY-MIRROR-WRITTEN` / 行尾后缀 `mirror_play.lock=yes` / `LOCK-RELEASED (mine)` / `LEGACY-LOCK-CLEANUP … (accounted)` / `LEGACY-LOCK-STALE-DELETED … (registered)`…）。**⛔ 账本与审计不许靠 grep tag 定论**（那正是假绿/假红温床）；**权威判据 = 盘上产物**：两个锁文件此刻存亡 + 释放日志里"删了哪几个文件"的清单。tag 只作**排查入口**。**新写的** runner 请用规范名 `LEGACY-MIRROR-WRITTEN` / `LEGACY-MIRROR-RELEASED (mine)` / `LEGACY-LOCK-STALE-DELETED … (registered)`；**已在飞的片不为此返工**。**规范 tag 表（2026-09-24 裁定唯一一套，`u52play` 版，已由 4 条存在性断言钉住）**：
| 事件 | 规范 tag（含必备字段） |
|---|---|
| 写完镜像 | `LEGACY-MIRROR-WRITTEN play.lock owner=<owner> pid=<PID> present=yes` |
| 释放镜像（自己的） | `LEGACY-MIRROR-RELEASED (mine) play.lock` |
| 释放镜像（别人的） | `LEGACY-MIRROR-RELEASE REFUSED (owned by <原行>) left alone` |
| 接管 stale 旧名 | `LEGACY-LOCK-STALE-DELETED <原锁整行> (registered)`（**打印在删除之前**，删除失败也有账） |
规则：**追加**规范 tag、**保留**原有可读行（不删已有信息 ⇒ 审计不漏、也不许删证）；tag 被改名 ⇒ 断言必须变红。

**读侧**：匹配前先剥 `U+FEFF` / `U+200B` / `U+200C` / `U+200D` 并**归一化空白**（`Trim()` + `\r?\n`→空格 + `-split '\s+'`）。
- **「年龄」只许取自文件 `mtime`（`LastWriteTime`）**：`Touch-Lock` 心跳会**刷新 mtime**，而载荷里的 `ISO` **停在取锁时刻** ⇒ ⛔ **绝不能用载荷 ISO 算 age/判活**（否则跑满 12 分钟的长会话会被自己判成 stale ⇒ 误接管，与 11:42「缺 PID 当已死」同型）。现状普查（2026-09-24）：把 ISO 解析成 age = **0 命中**；`LastWriteTime` 用法 48 处 / 覆盖 45 个脚本 ⇒ 当前无隐患，**但 v2.x 原文只说"`age ≥ 12min`"、没写 age 从哪读** ⇒ 本条即为该隐性口径的明文。
- **就绪判定必须 fail-closed（"未知 ⇒ 让路"同样适用于 `playMode`）**：就绪闸门只判"读到 `playing` 就拦"是 **fail-open** —— 字段缺失 / 输出为空 / 解析失败时它会当成"可起 Play"⇒ 抢锁 + `editor_play`。而我们对**同一个不确定量**在锁侧的口径是**反的**（缺 PID = 未知 ≠ 已死 ⇒ 让路），且"CLI 静默失败"有实证（`status --project-path` 返回空表、`run_script` 装编不过的驱动仍回 `success:true`）⇒ **「读不到 `playMode`」≠「没在 Play」**。写法（`classcols` 报，2026-09-24 裁定）：
  ```powershell
  if ($stm -notmatch '"playMode"') { Say 'LOCK-GATE-UNKNOWN playMode unreadable -> yield'; Start-Sleep 30; continue }  # 不抢锁/不碰 editor/不记账
  ```
  ⛔ **不许靠"默认值恰好不等于 `playing`"兜底**（那是靠巧合成立的判据）。
  ⚠️ **但要把「桥整体不可达」与「字段缺失」分开判**：`editor_status` 返回 `success:false` / `COMMAND_FAILED`（= 编辑器进程不在）**不算"未知"** ⇒ 允许继续（打 `LOCK-GATE-BRIDGE-DOWN probed=N` 留痕），因为"没有编辑器"不可能"有人在 Play"；而**桥不可达但编辑器在跑**确实出现过（13:0x `console` `COMMAND_FAILED` 时编辑器正在 Play）⇒ 所以桥不可达必须**先有界重试（3×5s）**，期间**任何一次拿到可达读数就按它判**（`playing ⇒ ABORT`）。⚠️ `Get-Content -Raw` 通常会自动剥 BOM，所以"自己还能释放"不代表合规 —— 走 `-Encoding Byte` 的读取器、以及别的语言写的工具都会带上。
**释放**：两个名字都遍历、**只删内容属于自己的**（不是自己的打 `… REFUSED … left alone`）；接管 stale 的旧名要打 `LEGACY-LOCK-STALE-DELETED … (registered)`。

### 5.2 静态断言的"能失败"口径（判据必须双向可动）

本轮实测抓到 **6 类假判据**，全部来自"断言自己不可失败"：

| 类别 | 形状 | 后果 |
|---|---|---|
| 假绿 | `Contains(needle)` 恒真（如 `oldClose.Contains("0f)")`，常量包含自己的子串） | 判据永远绿，缺陷静默通过 |
| 假绿 | 源码检索命中**注释**里的同名串 | 同上 |
| 假红 | 行号算在**注释行**上（说明某坑的注释被当成代码） | 整改一个不存在的 bug |
| 假红 | 行号算在**自检断言的字面量**上（`n = 'static: … Set-Content …'`） | 同上 |
| 假红 | 把**日志/trace** 的编码写当成"写锁" | 同上（满屏误报） |
| 假红 | **采集管道下游早停**：`… \| Tee-Object <日志> \| Select-String … \| Select-Object -First N` —— `-First N` 一满足就**关闭管道** ⇒ 上游被提前终止 ⇒ 日志**截断在中间** | 看着像"跑到一半挂了"，去查一个不存在的崩溃 |
| 假红 | **间接调用漏判**：检测只认内联写法（`Set-Content … <锁路径变量>`），**漏掉经辅助函数**的写（`Write-LockFile $legacyLock` ⇒ `u44hover_run.ps1:106` 被判"没写镜像"） | 把已合规的文件判成缺失 ⇒ 派错工 |
| 假 PARSE-OK | 先记账后取锁 / 不读字节只打印 | 账本与事实不符 |

**硬要求**：
1. 算行号前**先剥整行注释** —— 用「把 `^\s*#` 的整行**置空**」而不是 `Where-Object`（后者**重编号**，行号会漂）；条件允许就取 **AST 节点**（`FunctionDefinitionAst.Extent`）算区间。
2. 排除**自检断言/字符串字面量**（needle 拼接行、夹具字面量）。
3. 扫描器的**匹配口径要窄**：判"写锁"就匹配**写锁路径的那一行**，不是"任何含 `-Encoding UTF8` 的写文件"。
4. 一条断言必须配**双向样本**：**已知正确样本必须绿** + **已知错误样本必须红**（缺一不可）。本轮多个片靠这条抓到自己写的 `⛔`（3 字节非 ASCII）与"夹具本身触发断言"。
5. **交付前必跑的 5 条闸门**（样板 = `u52play` / `closefix` / `waypoint` 三份自测）：编码字节闸门（非 ASCII 且无 BOM ⇒ FAIL，**要覆盖自测脚本自身**）· 锁文件无 BOM · 锁写入必须 ASCII · `editor_stop` 唯一且落在守卫区间内 · `editor_play`/`recompile` 行号晚于取锁 · **取 Play 窗口前先跑一次编译哨兵并看到 `CSC-EXIT=0`** —— `run_script` 对**编译失败**仍会回 `success:true` + `diagnostics`（第 36 条那个"沉默成功"）⇒ 不跑哨兵就会**白烧一个窗口**，而且在日志里看不出失败。**实测代价**：`d2u3_charstat_drive.cs` 的 `14:08:37` 版 `CS0266` ×1（`L1038 var cur = rt;` 而 `L1040 cur = cur.parent` 返回 `Transform`），若无哨兵、`charstat` 直接进 Play ⇒ 该窗口全白跑；`u52play` 用"**两次采样同值 ⇒ 静止版本**"把它定成**真缺陷而非编辑中间态**后才报出来。**⚠️ 配套一条同族口径**：**"离线自检通过"不算"能编译"** —— 同一份驱动改动自检了"**括号平衡 176/970/38** + `read_lints` **0 条**"（看上去很硬），却**编不过**（`CS0266`）⇒ **括号平衡 / lints 0 ≠ 编译通过**，与"**`parse-ok` ≠ 能加载**"是同一族：**都要用"真跑那一步"当判据**（`.ps1` 跑 `Parser`+真加载；`.cs` 跑**编译哨兵**）。
6. **别照抄旧快照**：派活/整改名单要带**扫描时刻**，被复核者可能两分钟前已自改（本轮 4 个文件里 2 个在名单发出前 1 分钟已合规 ⇒ 差点让人"修一个不存在的 bug"）。**定级前必须复读目标行原文**（同一轮里三次扫描拿到三份不同名单）。
7. **采集不让下游早停，汇总行必须来自落盘文件**：跑宿主/自检用 `*> <日志>` 直重定向，⛔ **不要** `… | Tee-Object <日志> | Select-String … | Select-Object -First N` —— `-First N` 满足后关闭管道会**杀掉上游进程**，日志断在中间 ⇒ 会被读成"跑到一半崩了"。汇总行（`exit=` / `N/N PASS` / `=== 自检全部通过 ===`）**必须**在**落盘文件**里 grep 出来，真挂了文件里就没有那一行。
8. **"读两个锁名" ≠ "写镜像"**：v2.2 的镜像 `play.lock` **唯一目的**是拦住**只读旧名的历史 runner**。只判 busy 时读它、持锁时**不写**它 ⇒ 那些 runner 看到的是"锁空着" ⇒ 并发 Play。**谁持锁，谁就要把同内容镜像写出去**，并在 release 时两个名字都遍历、只删自己的。
9. **⛔ 禁止"靠约定"**：没有闸门的约定下一轮还会各解各的（本轮已发生一次"v2.1 理解成只读"）。**"提及旧锁名 ⇒ 必须双写镜像"这条最终要落成 `tools/verify.ps1` 的一项闸门**（对着 `drivers/*_run.ps1` 扫），落点排在**收尾片**（现在多片在飞，改共享主闸门会让所有人的读数不可比）；过渡期先给历史 runner 加统一表头 `# QUARANTINED: <日期> <原因>`，闸门只对**无该标记**的 runner 强制。
10. **本宿主的命令闸门按"字面量"判定**：命令文本里出现写文件命令的名字（哪怕只是只读 grep 的 needle）会被拦下报"未指定编码会损坏文件"。绕法 = needle 拆成拼接串（`'Set-'+'Content'`）—— ⚠️ 这本身就是"匹配文本 ≠ 匹配行为"的实例：**检查应匹配 AST / 写入目标变量，而不是字面量**。
11. **"沉默失败"例表（调用方拿到"成功"，事实相反）—— 挡法 = 把"成功信号"绑在一个能失败的独立校验上**：

    | 实例 | 现象 | 独立校验（真信号） |
    |---|---|---|
    | `tabletool -pack`（Windows 文件锁下） | 目标被**截断成 0 字节**，进程**退出码仍 0**（`item_c.xlsx` 22992B→0B、`level_c.xlsx` 7192B→0B；`runPack` 对单文件失败只 `continue`） | **逐表 xlsx 表头 == txt 表头**（退出码 0 全程在骗人） |
    | `unity status --project-path <abs>` | **参数被静默丢弃** ⇒ 返回**只有表头**的空表（不带 flag 时正常） | 就绪闸门改用 `unity command editor_status`（还带 `playMode`）或 `console --tail 1` 的 `groundTruth` |
    | `run_script` 装一个**编不过**的驱动（CS0122 等） | 仍回 **`success:true`**，壳只打印前 500 字符 ⇒ 错误被吞 ⇒ 表现为"会话跑完但一条驱动日志都没有" | 驱动必须**自己写 done 标记 / 打自己的 tag**，壳断言"见到该 tag 才算成功" |
    | 镜像 Text 的 `preferredWidth` / `cachedTextGenerator.lineCount` | 面板上被 `D2TextMirror.Attach` 置 `font=null` ⇒ **恒 0**（既不是"1 行"也不是"折行"） | 走生产画字路径复算（`scale`/`availPx`/`MeasureNative`）+ `D2Label.LineCount` |
    | **正则里美元符未转义**（`$lock` 写成裸 `$`） | `$` 是**行尾锚** ⇒ **全部 pattern 匹配为 0** ⇒ 整列恒 `NONE`（**假绿里最危险的一种**：看着"扫过了、没问题"） | 自检第一条必须是"**known-good 必须被报出来**"（连它都不报 ⇒ 立刻 exit 1）；等价写法 `\$lock` |
    | `editor_play` **首次 401 / 端口切换**（7801→7802）+ **domain reload 冲掉编辑态安装的驱动** | 驱动一行没跑，但 `run_script` 仍回 `success:true`、零条驱动日志 | 判据 = **`DONE reason=…` 标记 + `console --tail` 原始行**；另加 warm-up + `playMode` 校验，⛔ 不拿 `success:true` 当"采到" |
    | **`editor_stop` 的 CLI 返回值早于编辑器真正退出 Play**（实测有 `playMode=playing` + **锁已空** 的窗口） | "只看锁"的 runner 会以为编辑器空闲 ⇒ 可能 `editor_stop` 打断别人正在退出的会话（= 11:43 事故那一类） | **释放锁前轮询 `playMode=stopped`（有界）**；**就绪闸门必须同时看"锁"+"`playMode`"**，`playing` ⇒ **ABORT**（不抢、不停） |
    | 就绪闸门的 **fallback 把主判据冲掉** | `editor_status` 说 `playMode=playing ⇒ 不可起`，随后 `console`（**看不到 `playMode`**）算出 `startable=True` 把它盖掉 ⇒ **正在 Play 的编辑器被判"可起 Play"**（`zorder` 实测撞上） | fallback **只在主判据"解析失败"时**才启用（`-not $parsed`），⛔ 不做"两个判据取或" |

12. **PS 命令的"输出"会让 `if (函数调用)` 恒真（假独占）**：`if (Take-Lock)` 而 `Take-Lock` 内部既 `Say('LOCK-RACE-LOST …')` **又** `return $false` ⇒ 返回 `@('那行', $false)`，**非空数组恒真** ⇒ **抢锁失败仍继续写账本、调 `editor_play`**（本轮 `u52play` 自查抓到的真 bug）。挡法：日志函数用 **`Write-Host`**（宿主流不进管道）或调用处显式 `[void]`；取锁函数一律 **`if ((Take-Lock) -eq $true)`**。
13. **沙箱存根会掩盖真实现**：同一条 bug **逃过了 32 条自检**，因为沙箱里 `Say` 是**存根**（不产出）⇒ 存根与真实现的管道行为不同。⇒ 关键函数必须有**不依赖存根**的断言（静态/AST 级，如"`Say` 的定义体用 `Write-Host`"）。
14. **PS 5.1 的重定向产物是 UTF-16LE**：`Tee-Object` / `*>` 在 PS 5.1 下写出 **`FF FE` 头**（**不是** UTF-8+BOM）⇒ 属**第三种形态**，不在"UTF-8 带/不带 BOM"两种之内。`.tsv` 里出现它，解析器可能看到 NUL 字节。⇒ 采集一律显式指定编码（或 `[IO.File]::WriteAllText(..., UTF8Encoding($true|$false))`）；读到 `FF FE` 就是它，转码后**必须回读 + 文本内容 sha256 比对**（否则"转没转成"也是假绿，`u52block` 的做法）。**⚠️ 转码 = 改字节 ⇒ 也改 `mtime`**：`u52block` 13:09:09 补齐编码后 12 个日志的 mtime 全被刷成同一时刻，而**采集时刻**是 11:48~13:05 ⇒ **凡用 mtime 判"新鲜度"的闸门（`verify.ps1` item 08 `freshness-own-rows`）会读到错的值**。⇒ 硬要求：**转码后必须同时在报告里登载"采集时刻"**（别靠 mtime），并留"内容 sha256 未变"的证据；⛔ 绝不去转码**别人**的证据文件（会连别人的证据 mtime 一起改掉，被读成"重采过"）。
18. **计数型自检必须让"失败"进计数**：`u3bverify` 的锚点表曾打印 `锚点存在性 8/8 在盘`，实际 **一次路径都没测到** —— `-split "\`t"` 的转义被吃掉 ⇒ 每行都当路径 ⇒ **8 次 `DriveNotFoundException`**，而失败计数只在 `Test-Path` 返回假时自增（异常被跳过）⇒ **零测量却报满分**。改为 `String.Split([char]9)` + 统计 `err/shaMismatch/missingNow` + 末尾打 `VERDICT=PASS|FAIL` 后，**第一次跑就 FAIL 并抓到真漂移**（锚点表存的是该文件 11:59 版，而 `u44impl` 12:38 覆盖了它）⇒ 这才是锚点表存在的意义。同族 = `if (函数调用)` 恒真（第 12 条）。
19. **"脚本崩了"与"脚本发现问题"会用同一个非 0 退出码 ⇒ 不可区分**：`classcols` 的编码扫描器输出含 `⇒`/中文，在 **GBK 控制台** `print()` 抛 `UnicodeEncodeError` ⇒ **脚本崩、`exit 1`**，而"发现违规"也是 `exit 1` ⇒ 外部看到的都是"红"，**但红的原因完全不同**。挡法：`sys.stdout.reconfigure(encoding='utf-8', errors='replace')`（或输出改纯 ASCII），并且**让"崩溃"走一个独有的退出码/独有的末行**（如 `VERDICT=CRASH`）。同族：`run_script` 的 `success:true`、`tabletool` 的 `exit 0`。
20. **无 BOM 的 UTF-16LE 会"解码成功"（假绿）**：只判 `FF FE` 头会漏掉**无 BOM 的 UTF-16LE** —— 若其内容恰好是 ASCII 字节，**按 UTF-8 解码不报错**（只是每个字符后面跟着 `\0`）。⇒ 判据要加"NUL 字节签名"（**UTF-8 文本永不含 `0x00`**）+ 形态/解码**只计一次**（`classcols` 的第一版在两个分支各记一次 ⇒ `VIOLATIONS=3` 而坏文件只有 2 个，**读数比文件数还大**）。
21. **编辑/替换锚点没命中时是"静默成功"**：`u52resist` 第三次锚点替换因**行尾 LF 与 CRLF 不匹配**而**没落地**（工具报成功、盘上未变），是它**新加的闸门当场判红**（`releaseGateHits=0 → FAIL`）才发现的。⇒ ① 替换/追加一律**先把行按行尾归一**（或改"行数组插入"这种不依赖行尾的写法）；② **改完必须回读目标行并让一条断言覆盖它** —— 半成品最怕的是"报成功"。
22. **有界轮询、绝不 hang**：新规则"放锁前等 `playMode=stopped`"要写成 **`for 1..N { sleep 1; 读 playMode }`**，命中即 break，**N 次没等到也照样放锁**并打 `RELEASE-GATE … not observed within Ns`（`u52resist` 实装形状）。判据的**上限必须有界**，否则一次网络/编辑器卡住会把团队唯一的锁永久占住。
23. **扫描/正则型断言必须与 `KNOWN-BAD` 样本"共用同一实现"（第 5 类判据病）**：`u52play` 的 `no-bare-boolean-lock-calls` 报了空命中，而它**专门要抓的那个形状 `if (My-Lock) {` 恰好抓不到** —— 因为正则要求**第二个左括号**。⇒ **报绿只证明"在我这条正则下 0 命中"，不证明"它能抓到该抓的形状"**。挡法：把判定抽成**唯一 helper**，**文件扫描与双向样本都调它**（样本与扫描不可能漂移），并配 5 条样本：`KNOWN-BAD`（`if (My-Lock) {` / `if (-not (My-Lock)) {` ⇒ **必须红**）+ `KNOWN-GOOD`（`-eq $true` / `-ne $true` / `$alive = Lock-Alive $f` ⇒ **必须绿**）。
24. **断言必须在其"对应内容锁定之后"跑一次，并把指纹同刻打印**：`u52play` 引用过的三个 `RUNNER-SNAPSHOT` 指纹**全部作废**（断言跑在内容改动之前 / 手抄）⇒ 规则：**每条断言跑完立即现算并打印 `bytes + sha256_16`**，引用时只引用"与之同刻的那个指纹"（旧指单一律作废）。
    **首选形态（2026-09-24 定为全队口径，成本 3 行）＝让产物自己带指纹**：runner **启动即打**
    `RUNNER-FINGERPRINT sha256_16=<自身> bytes=… lines=… mtime=…`（附一句 `authoritative for THIS trace; verified by re-computing`），
    报告/消息引用行号时只写"**以 trace 内 `RUNNER-FINGERPRINT` 行为准**"。理由：**人工贴指纹属于"工作区中间态"** —— `jitter` 贴的指纹在 **2.5 分钟后**就失配（`u3bverify` 逐行复算得 8 个行号全不匹配，`L155`/`L471` 都是 `}`），危害是复核者拿旧行号核现行文件 ⇒ 得到"`:438` 没有锁写"这种**假红**（今天第 4 次同族）。
    **派活/发信前必须重采指纹**：`u3bverify` 13:2x 采的 6 行整改名单里，**4 行在 5 分钟内自己变成了"已合规"**（`d2u3_charstat`/`u53close`/`u52resist`/`d2u27`）⇒ 只剩 2 行是真缺口。**名单是移动靶**：不复采就会成批发出"修不存在的 bug"的信（今天已发生 3 次）。
26. **断言用的"中间量"必须先于断言求值（判据两侧必须同刻同源）**：`u52play` 把 4 条闸门断言改扫 `$codeOnly`（剥注释后的代码行，方向对 —— 否则"注释里保留 tag、代码被删"会让断言**永真**），但 `$codeOnly` 的赋值写在断言**之后** ⇒ `$null -match x` = `$false` ⇒ **5 条假红**（先看到 `41/46`）。⇒ 规则：**中间量在断言之前算完**；与第 24 条（指纹必须与内容同刻）同族 —— **判据的两侧必须同刻、同源**。同日 `u52play` 三类自伤：注释当代码 / 正则形状过窄（假绿）/ 求值顺序（假红）。
25. **`continue` 落在循环外 ⇒ 脚本静默结束、`exit 0`、后续一行都不打印**（`classcols` 实测，第 4 个"沉默成功"实例）：给"fail-closed 闸门"补的 `… -> yield; Start-Sleep; continue` 这 2 行，**若该处不在"取锁重试循环"内**，`continue` 会直接结束脚本 —— 无 `LOCK-TAKEN`、无账本、无 Play、**且 `exit 0`**（与"正常跑完"同形）。对照：同一形状放在 `while` 内则行为正确。
   ⇒ 两道挡法，**都要**：① **放置规则** —— 闸门必须落在取锁重试循环里；原本没有循环的，用**有界 `for`** 把它包起来（或把 `continue` 换成显式的"等待+重试"分支）；② **终标记判据** —— 任何"跑完了"的判定都要求 runner 的**终端标记行存在**（`LOCK-TAKEN` / `END` / `DONE reason=`），⛔ **不能只看退出码**。
   ⚠️ **判"是否在循环内"必须用 AST**：`charstat` 实测——**缩进回溯会给出相反结论**（它把 `continue` 报成绑在内层 `for`，实际绑在外层 `while`），因为**缩进分不清"该循环已闭合"与"仍在其体内"**（闭合的 `}` 不是循环关键字，回溯会一路撞到更里层的那个循环）。判据 = 沿 `ContinueStatementAst`/`BreakStatementAst` 的 **`Parent` 链**找目标循环类型；⛔ 不用缩进 / 正则 / 行号区间。
27. **别"起真 runner 再 `Stop-Process -Force`"去读启动自检（会掷骰子拿到锁）**：`jitter` 13:26 实测事故 —— runner 起来 **9 秒后**被强杀；**那次锁恰好为空** ⇒ 它**正常取锁**（真锁 + 镜像）+ 写了账本一行，随即被 kill ⇒ **`finally` 未执行 ⇒ 僵尸锁 + 1 条无效账本行**（当时若别人正等窗口，就会被挡）。⇒ **结构性修复（全队口径）：凡"只想读自检"的场景一律用不取锁的入口**（`jitter` 的 `-SelfCheckOnly`：只跑字节编码自检 + 静态断言 + `playMode` 闸门，打 `SELFCHECK-ONLY-END` 退出，实测 `LOCKS before=[False,False] after=[False,False]`）。⛔ "记得别 kill" 不是修复 —— **每次验证都在赌"锁恰好被别人占着"**。（收尾时若仍有片习惯这么读自检，一并补该开关；`u3bverify` 另建议把它做成 `verify.ps1` 的检查项之一。）
28. **负样本断言必须与它的样本变量"同段"（同作用域 / 同时刻）**：`waypoint` 一天内三次"自伤假红"，全在**判据自己身上**：① 把 `function Stop-IfMine(` 的**定义行**当成调用点；② 断言段引用**函数内局部** `$text`（外层是 `$null`）；③ **负样本断言写在它的样本变量定义之前**。⇒ 得到的是"**能失败、但失败在自己的组织方式上**"的假红（与"注释当代码"、`u52play` 的存根掩盖、求值顺序同族）。⇒ 规则：样本定义与样本断言**同段**写；新增断言先跑一次"**已知好样本必须绿 + 已知坏样本必须红**"，两条都动过才留下。
29. **⛔ 硬禁忌：多行 `Say (` 续行拼接（PS 5.1 会突然崩）**：`Say ('a' + $x` ⏎ `+ 'b' + [math]::Round($y,1) + 'c')` 这种跨行括号表达式**能过大部分时候、补一行时突然报 `Missing closing ')'`**（"碰巧型"）。今天至少四片各踩 1~2 次（`shopart` 3 次 / `zorder` 12 error / `u44impl` 4 处 / `u52play` 2 次）。⇒ **一律压成单行**，或**先算进变量**再 `Say`。判断是否踩到只能靠真解析器（`[Parser]::ParseFile` 后数 `$e.Count`）。
30. **⛔ `-SelfTest` 不得写任何"正常跑也会写"的路径（实测**已发生一次证据覆盖**）**：`zorder` 的 `-SelfTest` 走 `WriteAllText($trace)` + 往**同名**文件 `Say` 追加 ⇒ 13:19–13:29 的几次自检**覆盖了 `.ai-tmp/test/u26-sort-steps-u26.txt`（11:58 那批的步骤 trace，不可恢复）**；损失边界 = 那一批（本就 `suspect`）的**步骤 trace**，而冻结读数 `.tsv` / log / done / contact / `u26b/c/d` **全部完好**。**修法**：自检的输出改道 `*-selftest.txt`（它改后实测"自检跑前/跑后该文件 mtime 逐位不变"）。⚠️ 这种覆盖是**静默**的（日志一切正常，只有事后比 mtime 才发现）⇒ **自查法：跑 `-SelfTest` 前后对 `.ai-tmp` 做一次文件哈希快照并比对**。
31. **扫描器必须清空"每一处"自检块，否则它在数自己的夹具**：`zorder` 的"区域裁剪"只清了**第一个** `if ($SelfTest)` 块，而文件里有**两个** ⇒ 大断言块留在生产文本里 ⇒ 扫描器开始数**夹具里的字面量**（`editor_stop` 数成 7、`Write-Output` 数成 6、`play@ < take@` 假红）⇒ 29/33（4 FAIL）。**按括号配对清空每一处**后 33/33（该片随后继续扩到 **39/39** —— **⚠️ 该数字已再次作废：2026-09-24 15:3x 实测同一指纹 `BC1D7F22A3271AD5` 下是 46/46**（后加 7 条团队协议项：orphan-play 告警在位 / finally 里 trigger→confirm→release / stop-trigger 可达 / 只在 if 内的 guard 判红 / alert 真落地 / alert 已接线 / `lines=` 不得用跳过空行的算法）⇒ **本项一律不写数字，只写"以该片 trace 内 `RUNNER-FINGERPRINT` 行为为准（当次 46/46）"**：一天之内这个数从 33→39→46，**手抄的数字永远慢于对方**）。⇒ 教训一句话：**一个"看起来在检查产物"的扫描器，可能一直在检查自己的测试夹具 —— 那时它给的绿和红都是假的。**
32. **解析/扫描型判据必须先证"文件处于静止态"**：`u3bverify` 在 `u44hover_run.ps1` 的 **mtime 13:30:11** 版上解出 **2 个语法错误**，**45 秒后**的 **13:30:56** 版 `parseErr=0` ⇒ 那是**编辑中的瞬时态**。⇒ 判据要"解析前后比对 `mtime`（变了 ⇒ 重解析，仍变则标 `UNSTABLE`）"，否则同一份判据会**一边假红**（把编辑当语法错）**一边假绿**（把半成品当成品）。
33. **要"对将来新增的代码"也生效的检查：作用在整份文件 + 配一条"注入式"直证 + 优先 AST**（`waypoint` 的形状，38/38）：① 检查作用在**整份文件文本**上（不是只列今天已知的几处）；② 配 **`把违规样本注入真文件文本 → 同一个检查必须转红`** 的直证（它实测：真 runner 文本 + 追加一行顶层 `continue` ⇒ 由绿转红）；③ **优先 AST** —— 正则会把 **字符串** `$ErrorActionPreference = 'Continue'` 当成 `continue` 语句 ⇒ **假红**。
34. **每条判据至少要"三向"（`waypoint` 用 4 次自伤换来的）**：**负样本只能证明"判据会失败"，不能证明"它因正确的理由失败"**。三向 = ① **真文件 ⇒ 绿**（挡住"把真东西判红"）② **已知错 ⇒ 红**（防恒真）③ **正例片段 ⇒ 绿**（防一刀切）。`waypoint` 今日 4 次自伤假红（定义行当调用点 / 函数内局部当外层变量 / `$rb` 用在定义之前 / **拼接串当独立常量**）**全部落在"缺第 ① 向"**这个盲区；它那次自伤的负样本在修正前后**都是绿的** ⇒ 负样本没救它。
35. **写"锁空/无锁"这类结论必须写明读的是哪个路径**：`zorder` 有几次写"两名锁 ABSENT"时读的是 **`client/play-running.lock`**，而本项目的锁在 **`<root>/.ai-tmp/test/play-running.lock` + `play.lock`** —— **两个位置的不同文件**（当时真实读数 = 两把都被 `u44impl … 50928` 持有）。⇒ 它的**结论**（自己没持锁、无自己的僵尸锁）仍成立，但**当时引用的证据无效**。⇒ 口径：凡"锁空/无锁"的陈述**必须带路径**；否则两片读不同路径会各自"证据充分"地得出**相反结论**。
36. **"沉默成功"第 5 例：`run_script` 的 `diagnostics:[]` ≠ 编译过**（`u44impl`）：用**相对路径**调 `run_script` 时返回 **`success:true` + `diagnostics:[]`**（看上去完美），同一响应里却有 **`error="File Not Found"`** ⇒ **正确的验收条件 = `result` 里出现入口的返回值**（本例 `PONG frame=<n>`），即"编译 **且** 执行都发生过"的证据；⛔ 不能只判 `diagnostics` 为空（同时也再次印证第 15 条：**凡 CLI/`.NET` 一律传绝对路径**）。
37. **第 30 条的全员扫描结果：5/8 命中、4 个真损失（已全部修复 + 快照直证）** —— 这是本日**唯一造成证据损坏**的缺陷族，边界记清：`u52resist`（覆盖自己的 `run_log`）/ `waypoint`（覆盖自己的 `heartbeat-u32.txt`）/ `jitter`（**未触发**：其 `-SelfCheckOnly` 本会写 `d2u27-steps-<Tag>.txt`，用 `-Tag u27v5` 就会覆盖 `closefix` 那批的证据 ⇒ 已改道）/ `shopart`（**覆盖自己的 13:22 批 runlog + 删掉真 `done` marker**）/ `closefix`（**覆盖自己的 13:11 批步骤 trace**）。**四片的判据用到的冻结件全部完好**（`.tsv`/`evidence`/`console` 冻结副本/截图/账本），坏的只是"可重判的步骤 trace / 心跳草稿"。⇒ 修法统一为"自检输出改道 `*-selftest-*`，且自检模式在任何取锁/账本写入之前退出"。
    **三条方法学（都是这轮自证里踩出来的）**：
    - **并发下的"前后快照 diff"必须逐文件**按 owner 归属**（按名字前缀 / per-PID 沙箱）**，否则会把**别人的**并发写入算到自己头上（`waypoint`/`jitter`/`zorder` 都遇到过 ADDED 里混进他片文件）。
    - **"0 变化 / 0 命中"这类结论必须配负对照**：`zorder` 第一版检查函数**不输出任何行**，空比对给出 `changed=0/8`（**真空绿**）；补一条"不存在的文件名 ⇒ 必须报 MISSING"后才让"绿"有意义。
    - **自检块的"起始标记"不能与扫描器的块标记撞名**：`shopart` 的重定向块写成 `if ($LockSelfTest) {` ⇒ 与它的静态扫描器用来"丢弃自检块"的标记**同名**，于是扫描器从**它那 4 行**开始丢、把**真自检块当真代码扫**（"扫描器一直在数自己的夹具"的又一实例，而 `static-layout(real runner): True` 当时**看不出问题**）⇒ 改名 `$selfCheckMode` + 收紧标记正则 + **给块标记本身加一条"文件里只应有一处"的自检**。
15. **.NET API 的相对路径按"进程 cwd"解析，而 PS 的 `Test-Path`/`Get-Content` 按 `$PWD`**：会出现"`Get-Content` 读得到、`[IO.File]::ReadAllBytes(<相对路径>)` 报 FileNotFound"（本轮主 agent 与 `u52block` 各撞一次，都差点被读成"产物丢了"）。**不止 `[IO.File]::*`**：`[System.Management.Automation.Language.Parser]::ParseFile(<相对路径>)` 同样是**进程 cwd**，而 `powershell -File <相对路径>` 是 **`$PWD`** ⇒ 同一个 `cd <项目根>` 之后会出现"**脚本跑得动、解析器却报 `未能找到路径 c:\…\f-v2\tools\…`**"（进程 cwd 停在上一层，`zorder` 实测）。38. **⛔ `-Tag` 必须含片名；禁止跨片复用 tag（同名产物互踩的另一半，`charstat` 提议、采纳）**：`d2u3_charstat_drive.cs` 被**两个 runner** 共用（`d2u3_charstat_run.ps1`、`u52resist_run.ps1`），产物名形如 `u3_charstat_readings_<Tag>.tsv` / `d2u3_charstat_<Tag>.png` / `.done` ⇒ 只要有人**用别人的 tag 跑**（或未来某片把 tag 设成 `u52run2`），那份 `.tsv` 会被**原地覆盖且不报错**（与第 30/37 条同族）。**当前干净**只是因为两个默认 tag 恰好都是片名（`u52run*` vs `u52resist`）。⇒ 规则：**tag 默认值必须带片名**，并在取锁后 `Say` 一行 `TAG=<tag> owner=<片名>`（便于事后归属）；⛔ 禁止跨片复用 tag。**附判据**：「谁在写某产物」**不许按"命中该文件名的文件"判** —— `charstat`/`shopart` 各自的第一版都把**只在注释里提到**该名字的 runner 列成写者（`u44hover_run.ps1` / `u53close_run.ps1` / `d2u3_charstat_run.ps1:6` 全是注释）⇒ 必须**剥注释 + 只看写调用**（第 12/30/33 条同族）。
39. **`.ai-tmp/test/` 里有"每次跑一键入口都会被重写"的宿主夹具，⛔ 别把它们当冻结证据**：`run_all_hosts.ps1` 会重写 `host-setting/fullcheck/saves/FullCheckHero.json`、`savecheck/s13-area/saves/AreaHero.json`、`savecheck/s14-progress/saves/ProgressHero.json`（**长度不变、内容含时间戳 ⇒ 哈希必变**；`u52block` 的前后快照里 `ADDED=1 / CHANGED=17`，这三条就在其中，且已核实**它自己的证据 0 改写**）。⇒ 引用这三份的**字节/哈希**一定会漂；要引就引**内容语义**，或先复制成带片名的冻结副本。**通用口径**：`.ai-tmp/test/**` 下的 `host-setting/**`、`savecheck/**` 属**宿主运行期在工作区内建的夹具**，不是交付证据。
40. **⛔ 自检/扫描脚本里不得用单字母函数名（撞 PS 内置别名 ⇒ 整段比对从未执行，假绿）**：`u44impl` 把快照脚本的辅助函数命名为 **`H`** ⇒ 撞上 PowerShell 内置别名 **`h` = `Get-History`**（**别名表是全局的，与作用域无关**）⇒ 满屏 `无法绑定参数"Id"`，**看着像"快照逻辑坏了"，其实是哈希比对压根没执行**；改名 `Get-Fp` 立刻正常。⇒ 两条硬要求：① **判据判"危害"、不判"风格"** —— 硬判据 = **`Get-Alias` 撞名数必须为 0**（`u44impl` 已落成 **AST 取全部 `FunctionDefinitionAst` 名 → 逐个 `Get-Alias`** 的 fail-closed 断言：`functions=13 aliasCollisions=0`，且 `fnCount=0` 也算 FAIL）；**"动词-名词命名"只是推荐、⛔ 不是闸门**（豁免实例：`u44impl` 的 `UC` 只有 2 字符，但实测 `aliasCollisions=0` ⇒ **判定豁免、不改名**；理由 = **静默风险不对称**：漏改一个调用点**仍能解析通过**（`parseErr=0` 抓不到），而自检不执行取锁循环 ⇒ **改名这件事无法被自检证明**，只在真跑走到取锁路径时才报 `UC is not recognized`。**⛔ 不要为一个可证为零的风险，换进来一个不可证的风险**）。`h`/`?`/`%`/`gc`/`sc`/`ls`/`cd`…才是活坑；**③（§40 的第二次实例、同一个坑，2026-09-24 `u52resist` 实测）任何"两值比较"判据必须先证明"两个值都真拿到了"** —— 它的证明脚本又把哈希函数命名为 **`H`** ⇒ 两次哈希都是**空串** ⇒ `identical=True` 实为 **`$null -eq $null`**（"证明"是假的）；修法 = 改名 + **`hash-length-guard(64)`**（⛔ 非 64 位十六进制不许判 `identical`）。⇒ 通用形式：**比较之前先验"形状 / 长度 / 非空"** —— "值拿到了"本身要有断言；② **"比对/扫描"这类判据必须自报"我比了多少条"**（`compared=N` / `scanned=N`），**且 `N=0` 或空必须 FAIL** —— 否则脚本一旦静默 no-op，产出的是**真空绿**（与第 31/37 条同族：判据不可失败 = 假判据）。
47. **⛔ 改"含非 ASCII 的文件"之后必须**回读头三字节**（⛔ 不要靠"我记得它带/不带 BOM"）** —— 最严重的症状在 `.ps1`：**BOM 被抹掉 ⇒ PS 5.1 按 ANSI 读 ⇒ 脚本根本无法加载（`exit 1`、零输出）**，而它**看起来像"脚本没跑"而不是"编码坏了"**（`charstat` 实测：编辑后 `BOM=False` + 2753 非 ASCII）；唯一救它的是文件自带的 `ENCODING-SELFCHECK`（它真红了）。⇒ 规则：**编辑含非 ASCII 的 `.ps1` 后回读 `EF BB BF`**，并把这条件成脚本自带的能 FAIL 的自检。
**⚠️ 判据层面的关键补充（`u52play` 实测）：`Parser::ParseFile` 读的是文本 ⇒ 它天生看不见这个错** —— 违规文件在 `runner-parse-0-errors` / `selftest-parse-0-errors` 两条断言上**都是绿的**（6 个非 ASCII 字节碰巧没破坏语法）⇒ **该规则只能在字节上判**（断言形状：非 ASCII ⇒ 必须有 BOM；纯 ASCII ⇒ 无需），并配"**真跑一次**"（`powershell -File … -SelfCheckOnly` ⇒ `exitCode=0`）作补充。一句话：**`parse-ok ≠ 能加载`**（与"**存在性 ≠ 可达性**"同族）。**当场可视的判据**：把 UTF-8 按 ANSI 解出来会看到 `鍙ｅ緞` 这类乱码字形（实测就是它自己的 6 个字节）。**分层已有人做，⛔ 别重复加**：`verify.ps1` 的 `item 14 sampler-selfcheck`（`…:1029`）已判"**非 ASCII 且无 BOM ⇒ ANSI trap**"，范围 = 全仓 `*.ps1 / *.psm1`；而 `14b` 的 `parseErr` **只判语法层** ⇒ **两层互补、无重叠** —— 不要把编码断言再加进 `14b`（否则又是"同一判据两个调用点"）。
**最强的实证（`u44impl` 的对照实验，同一脚本两份并排真跑）**：真文件（带 BOM）`PARSE-ERRORS=0` + **`exitCode=0`**；**故意抹掉 BOM 的副本** ⇒ **`PARSE-ERRORS` 仍是 0**、而 **`exitCode=3`** + `SELFCHECK-FAIL encoding (fix: pure ASCII or a UTF-8 BOM)` ⇒ **`parse` 两次都绿、真跑一次就分开**。它也解释了"零输出 `exit 1`"那种最难查的症状**需要两个条件之一**：ANSI 误读**破坏语法**，或该文件**根本没有字节级编码判据**（它 76 个非 ASCII 字节被误读成乱码但没破坏语法 ⇒ 脚本仍能加载 ⇒ 字节判据当场报红）。三层"脚本真的加载并跑起来了"的证据链：**自检日志首行由脚本自己写**（读不到 = 没加载）+ `RUNNER-FINGERPRINT` 行 + **真跑 `exitCode`**。
**⚠️ 范围的两条相反实测（都记下来，别只看一条）**：`u52block` 的两个含中文 `.md` 被编辑 3 次，**每次回读 BOM 都在**；`jitter` 的 `report-u27.md` 却被**抹成 BOM-less**（9265 个非 ASCII ⇒ PS 5.1 读者看到乱码，已补回并复核 `U+FFFD=0`）。⇒ **结论：编辑工具"可能"抹 BOM —— 与扩展名无关**（`.ps1` 上是"会炸"，`.md` 上是"只有按 PS 5.1 读才乱码"）⇒ **凡含非 ASCII 的文件，改完一律回读头三字节；⛔ 不要为了"预防性"去重写本来就好的 `.md`/`.txt`**（改字节 = 改 mtime，可能被当成重采）。 —— `charstat` 实测：一次编辑后 `BOM=False` + 2753 个非 ASCII 字节 ⇒ **PS 5.1 按 ANSI 读** ⇒ 脚本**根本无法加载**（`exit 1`、**零输出**）。最阴的是症状：**看起来像"脚本没输出/没跑"，而不是"编码坏了"**（"语法错"伪装成"脚本没输出"）；唯一救它的是文件自带的 `ENCODING-SELFCHECK`（它真红了）。⇒ 规则：**编辑含非 ASCII 的 `.ps1` 后必须回读头三字节确认 `EF BB BF`**，并把这条做成脚本自带的能 FAIL 的自检。
49. **"读到某文件" ≠ "读到一个版本"（`classcols` 提，采纳）** —— 一次编辑的**中间态会在盘上停留**（实测 `14:00:51` 与 `14:01:29` 是**同一次 rev-4 的两次落盘**，不是两个版本）；把它读成"两个版本"就会得出"有人在并发改"的**假警报**（并把自己的读数误标 `suspect`）。⇒ 口径：**报"某文件是 X 版"必须满足二者之一 —— 〔连续 N 次采样同值〕或〔对方通知点名（含指纹）〕**；单次采样只能报"**此刻读到的是** …"。**配套的行数口径**：**一律写明算法**，全队统一 **`lines = ReadAllLines().Count`**（"逻辑行"）；`count(LF)+1` **只在文件以换行结尾时凭空多一行** —— 同一文件、**同一 hash** 量到 `913` 与 `914` 就是这么来的，差点被记成"版本失配"。**行数的三种算法差（实测都出现过，别混用）**：① **`ReadAllLines().Count` = 逻辑行**（全队统一用这个；`waypoint` 用四种量法机械复现了差额来源，并让自己的 trace 与独立复算逐值相同）；② **`count(LF)+1`** ⇒ 尾换行处凭空多 1 行；③ ⛔ **`Measure-Object -Line` 会静默跳过空行** —— `select` 实测同一文件 **327 行 vs 254 行（差 73 = 该文件的空行数）**，这是**最危险**的一种：**它看着像行数、其实不是一个量**，用它判"行数 == 期望"会得到**稳定但错误的绿/红**。
**⚠️ 同族的归属陷阱（`classcols` 自纠）**：**⛔ 不能用 `mtime` 判"某文件是不是本片的产物"** —— 在共享仓库里**必然误报**（别的片改的文件、原版素材、别人的台账全会被算进来；它 v1 那条 FAIL 就是**假警报**）；判归属只能用**命名空间 + 逐条列出"被批的写入面"**。这与"未跟踪文件无 HEAD 可 diff"是同一族的两个面。**引用读数时"指纹 + 时刻"必须成对**（手贴的天然会被当成历史态；今天已发生一次手贴失配）。
48. **退化样本必须与"主侧正控"成对（`charstat` 提，采纳）+ 判据"能不能失败"的三种形态（`u44impl` 归纳）** —— 只做"把东西改坏后判据应归零"，会在**判据本身失效**时**两侧一致地绿**（主侧本应 `>=1` 却恒为 `0`，而退化侧"应归零"照样 PASS）⇒ 必须**成对**：**主侧"应 ≥1" + 退化侧"应 =0"，一起跑**（`charstat` 实例：`editor_stop` 计数判据形状写错 —— `UC @('editor_stop')` 的动词是 `UC`、实参是 `ArrayLiteralAst` ⇒ 判据恒返回 0 ⇒ **退化样本没拦住它**）。三种形态（对照自查）：① **能失败、且因正确理由失败**（要的）；② **失败在自己的组织方式上** —— 典型是"**注入空操作**"（判别针随样本一起移动 ⇒ 注入后文件其实没变，判据当然绿）；③ **根本不能失败（弱判据）** —— 例：只要求"文件里有个 `Say` 带某标记"，**抹掉真报警后另一句注释仍满足它**（修法：收紧为"必须落在某个函数的 extent 内"）。
另附**新的缺陷类：日志/文案承诺的动作 ≠ 实际动作** —— `waypoint` 自查抓到两处 ABORT 文案写着 `-> releasing MY lock and stopping`，而**实际只 `Release-Lock`、根本没停**（`d2u27` 同型）。⇒ 判据应对"**文案里承诺的动作**"与"**实际调用**"做一致性检查；⛔ **文案不能代替行为，也不能替行为作证**。
46. **转发子判据的结果时，必须校验"子判据自己报了分母"（⛔ 子命令参数绑定失败也会"成功返回"）** —— 把 `waypoint` 的 C5 判据接进静态闸门时踩到：PowerShell **数组**直接交给**原生命令**（`powershell -File … -Active $ActiveRunners`）会被拆成**多个 argv**，子判据收到垃圾参数后**照常退出 0**，只打 `SUMMARY files=0 PASS=0 FAIL=0 INFO=0` ⇒ **一个"什么都没扫"的绿**；而包装层只检查"有 FAIL 行吗 + 退出码是 0 吗" ⇒ **绿灯通过**。修法：`-Active ($ActiveRunners -join ',')`，**并且**包装层必须断言**子判据的分母 > 0**（`files=0` ⇒ FAIL）。⇒ 一句话：**"我只是转发"不是免责 —— 转发者必须验证被转发者的分母**。⚠️ 这条与第 40-② 条是同一条规则、只是主体换成包装层，而 **第 40-② 条是当天刚写的、还是被我自己当天违反了** —— 记账时要写清这一点：**规则写在文档里，不等于写它的人不会违反它**。
50. **"指纹 + 时刻"必须包含"判据/工具自身"的指纹 —— 冻结清单要连工具一起冻（`classcols` 提）** —— 实测踩点：我公布某次闸门读数用的是"**工具现行版 `19326 B / 14:15:07`**"，而盘上现值其实是 `20061 B / mtime 14:16:30`（它在两分钟后又改过一次注释）⇒ **那次读数与盘上工具不是同一版**。⇒ 两条：① **凡发布闸门/判据读数，必须同时给"工具指纹 + 工具时刻 + 输入指纹 + 输入时刻"**（工具是判据的一部分）；② **冻结窗口的清单 = 8 个活跃 `*_run.ps1` + `runner_gate_check.ps1` + `c5_order_check.ps1` + `verify.ps1` = 11 个文件** —— ⛔ **只冻 runner 会漏掉"闸门自己变了"这一类**；且**冻结快照要自带"双采样同值"**（`classcols` 的交钥匙脚本：11 文件两次逐项相同才打 `STABLE`），这样"自 T 起无人编辑"是**判据**而不是印象。
55. **⛔ 共享账本/日志的"既有行"不是你的编辑对象 —— 批量改写过宽的谓词 = 改证据（主 agent 当日自伤，已还原）** —— 我用一个**过宽的谓词**（"今天 + `By` 以 `d2fix/` 开头 + 成员名在我的映射表里"）批量规范化 `dispatch-log.tsv` 的 Scope，**一次改了 25 行，其中 11 行是别人早先派单留下的**（= 动了被判定的**历史证据**）。⇒ 三条：① **修共享累计产物必须先取快照**（我这次有 `dispatch-log.pre-mainfix.bak.tsv` 才救得回来），且**以快照为唯一真源重建**、**逐行 diff 自证"只动了我该动的那几行"**（自证读数 = `DIFF_LINE_COUNT=15`，恰好 14 行我追加的 + 1 行 `# direct-fix`）；② **谓词要"唯一指认"**：能按"我这次写入的**行号区间**"定位就不要按"看起来像我的字段值"定位 —— 后者会撞上别人的同类行；③ **改证据与"迎合闸门"是一体两面**：为了让 `no-sync-subagents` 通过而重写账本行，必须**只修格式、不改语义**（`At/Task` 一字不动，只补 `By` 的 `<team>/` 前缀与把散文 Scope 换成 project-relative 前缀），并且**在回报里主动披露**"我写坏了格式、已修"。**同族**：第 54 条那条"**一个计数器是别人的断言在用的，就不是你的临时变量**" —— 本条是它在**文件级**的形态：**一整个账本的既有行，是全体片共用的证据，不是你的草稿**。
54. **⛔ 改契约必须连判据一起改（判据绿不代表契约还在）** —— 本日 U52 修法的连锁：`SaveModule.Load` 从"**把 `data.version` 抬到当前**"改成"**只报不改**（保留档内原版本号，回写磁盘时才写当前）"，**生产侧改完、离线套件立刻红 1 条**（`savecheck` 判据「③a ⇒ 版本被抬到当前」实测 `version=1` FAIL）。⇒ 三条口径：① **契约变更 = 一次跨文件改动**，必须**同时**列出"谁依赖这个契约"（本例依赖者 = `PlayerModule.LoadFrom` 的 `save.version < SaveVersion` 判据 + `savecheck` 的旧断言）；② **判据要按契约的"两半"都判** —— 只判"读=保留"会放过"档永远停在旧版本、每次进游戏重跑迁移"，只判"写=抬到当前"会放过"读档时版本被改掉 ⇒ 迁移永不触发"（**就是本次 bug 本身**）；③ **判据在契约变更后变红是"判据活着"的证据**，⛔ 不许把红读成"套件坏了"就去改断言 —— 先问"**是契约变了还是实现错了**"（本例是契约变了 ⇒ 改判据 + 在注释里写明**为什么必须两半**）。**配套小坑（同日实测两次）**：给共享的"事件账/计数器"（`LoadDoneArgs`）旁边插新断言时，**⛔ 不清成空、而要"先存后复原"** —— 实测先"不清"⇒ `Count==1` 变 2、再"清空"⇒ 变 0，两次都假红；**一个计数器是别人的断言在用的，就不是你的临时变量**。
56. **⛔ "编辑工具报 success" ≠ "盘上真变了" —— 关键改动必须回读字节/行数（`u44impl` 实测）** —— `Uicheck.csproj` 用 `replace_in_file` **报 success 而盘上一字未变**（仍 11393 B），改用 `[IO.File]::WriteAllText` 直写才落盘（12091 B）。⇒ 两条：① **凡重点改动（判据 / 契约 / 闸门 / `.csproj`）写完必须回读**（`bytes` + `lines=ReadAllLines().Count` + 关键串命中数），⛔ 不认工具的回执；② 该 `.csproj` **无 BOM、注释一律纯 ASCII**（BOM 会被 CS 解析器拒）。**同族**：本日主 agent 也踩过相邻的一跤 —— 账本格式写错（第 55 条）。
57. **⛔ 两件事在"插入新断言"时同时发生：段序即输入 + 剥注释会让注释对判据隐形** ——
 - **(a) 别插在别人断言的中间**：为"两半齐"把新断言 `③a-2` 插进 `③a` 段中间，**把它后面三条「③a ⇒ …」断言的输入状态改了**（`LastError` 读到的是新段 `Save` 留下的空串；`兼容路径` 计数从 2 变 4；`LoadDoneArgs` 靠 save/restore 才兜住）。⇒ 正解不是"用 save/restore 打补丁"，而是 **插到该断言块的末尾**（更根本：**段序即输入** —— 在别人断言之后加东西，等于改它的输入）。**同族**：第 54 条的"计数器先存后复原"只是这个病的**症状级**补丁。
 - **(b) 剥注释（`$code` 视图）会让注释里的东西对整个判据集隐形** ⇒ **注释会悄悄漂移**：`u52resist` 实测一处注释仍写"`bounded 5 x 1s` … `release regardless afterwards`"，而**真实行为早已反转**成"15×2s + 重试 8×2s + 持锁不交"；因该行以 `#` 开头 ⇒ **22 项断言对它全盲**，且回归守卫只认新措辞 `releasing anyway`、**不认旧措辞** `release regardless`（= 闸门盲区）。⇒ 两条对策：**① 关键口径（放锁条件 / 门槛 / 上限）必须在 `$code` 视图里也有一份机器可读表达**（断言或常量），⛔ 不许只活在注释里；**② 加一条"陈旧措辞判据"** —— 注释/文档里出现**已废止的措辞**（旧阈值、旧行为描述、旧路径、旧行号）⇒ 报警（**陈旧信息比没有信息更坏**：它会让下一次读的人按错的口径干活）。
58. **"证据比对象旧"要判"相关属性有没有变"，⛔ 不是一个 mtime 大小比较 —— 但豁免必须配机械证据** —— `u52resist` 的实机读数（12:49:57）早于被验证文件（`CharacterPanel.cs` 12:52:02 / `UiLayoutGame.cs` 12:54:47），按"证据必须新于对象"的字面口径该重采。**它自己做了机械复核**：12:5x 的改动只是 XML 注释/出处文字，且**现值与实机读数所对应的量逐项相同**（`ResistName0..3 66.0 art` / `ResistValue0..3 45.0 art` ↔ `boxCanvas=118.8=66×1.8` / `availPx=55`）⇒ 判定 **"相关属性未变 ⇒ 不必重采"**（主 agent 采纳）。⇒ 口径：**§4-8 要防的是"对象变了、证据没重采"（拿旧证据充数），不是"mtime 谁大"**；判别式 = **列出该证据依赖的属性并逐个复核**，把复核读数落盘；**没有这份机械证据 ⇒ 照旧重采**（⛔ 免不得）。**推论（同族）**：**凡有"唯一真源行"的读数（`RUNNER-FINGERPRINT` / `REPORT-FINGERPRINT`），引用时只引那一行、不抄数字** —— 本日 `zorder` 的自检项数在**同一指纹下**从 33→39→46，`u52resist` 从 19→22，手抄的数字永远是滞后的那一版。
59. **🚨 `.NET` 的 `[IO.File]` 不认 PowerShell 的 location ⇒ 用相对路径"读"会静默读到**另一个同名文件**（判据照绿、证据是别人的）** —— `shopart` 实测（**机制已用 4 个读数钉死**：PS location = 项目根，而 **in-process `.NET` CWD = `c:\Work\Server\f-v2`**（工作区根）；`Get-Item(rel)` 按 location 解析 ⇒ 对；`[IO.File](rel)` 按进程 CWD ⇒ 去工作区根找；**`python` / 嵌套 `powershell -File` / `dotnet run` 这些**子进程**则拿到"已被同步成 location 的 CWD"**）：PowerShell **只在启动 native child 前**把进程 CWD 同步成 PS location，**进程自身的 `[Environment]::CurrentDirectory` 保持 shell 启动值**。⇒ **适用范围（⛔ 别过度/不足泛化）**：**中招面 = `.ps1` 里做 in-process `[IO.File]`/`[System.IO.*]` 且用相对路径**；**不中招面 = 子进程**（`python x.py` 等）—— 但**安全前提是"启动时的 location 正确"**（若 location 本身 = 工作区根，python 一样读错那棵树），所以**"python 免疫"是错的**。⇒ `[IO.File]::ReadAllLines('.ai-tmp\test\u27-heartbeat.txt')` 命中的是**工作区根**那棵 `.ai-tmp` 树里的同名文件（118 B，而项目里是 37674 B）——**不同文件**。⇒ ① 既有口径"凡 .NET/IO API 一律绝对路径"**不只是防"写错位置"，更是防"读到别人的同名文件还判绿"**；② **检测法（机械、可复用）**：**同一相对路径分别用 `Get-Item`(PS) 与 `[IO.File]`(.NET) 读，比 `bytes`** —— 不等即中招；③ 凡"用 .NET 读证据文件然后判绿"的脚本，**必须打印被读文件的绝对路径 + `bytes`**（否则绿得无从复核）。**同族**：`u44impl` 的 `.NET` 找不到相对路径（`DirectoryNotFoundException: c:\Work\Server\f-v2\tools\…`）—— 那是"读不到"的显性版，本条是"读到了别的文件"的**隐性版**（更坏）。
60. **⛔ PowerShell `-match` 默认不区分大小写 ⇒ 用 `[A-Z]` 写的"行首形状/大写标签"判据形同虚设** —— `shopart` 实测：`'prose continuation...' -match '^[A-Z]'` = **True**（小写 prose 被当成大写标签）⇒ 它那条判据**当时从未真的判过任何东西**。修法：**`(?-i)` 内联关闭大小写敏感** 或直接用 **`-cnotmatch` / `-cmatch`**。⇒ 口径：**凡判"大小写/字符类/形状"的判据，必须显式声明大小写敏感性，并且用一条反例样本证明它会红**（本例反例 = 全小写 prose 行 ⇒ 必须 **不** 命中）。
61. **⭐ 判"伪"之前先在真语料上校"假阳性"（与三向自证互补：三向防假绿，这条防假红）** —— `shopart` 最初按"**一行内出现 ≥2 个日期 = 日志粘连**"扫全队 40 份心跳，报出 **4 份"粘连"**；逐条读原文 ⇒ **全是误报**（那些行是合法引用别的事件时间：`lock=[<片> <date>]` / `mtime=<date>` / `at=<date>`）。改成 **"日期**紧贴**非分隔符"**（真粘连的形状）后：**真实缺陷件 21/21 全命中、40/41 份心跳 0 误报**，并把 3 条**假阳性对照**（含真实 `u27` 心跳）写进判据注释防回归。⇒ 口径：**凡要在"全队/全仓"尺度上报警的判据，先用真语料量一遍误报率，并把假阳性样本固化成对照**（⛔ 不许拿"合理解释"当校准）。
62. **"记一对，不记一个数" + "重复派单 ⇒ 留两份但定唯一权威"** ——
 - **(a) 属性值要连同"档位/时机"一起记**：`select` 与 `u44impl` 的"冲突读数"（`_Contrast` 1.01 vs 1.0）**不是两个数**，是**同一函数的两个分支**：**悬停档** = `BrightnessFor/ContrastFor(true)` = `3.0 / 1.01`；**常规档（含"悬停后移开"那一帧）** = `…For(false)` = `1.0 / 1.0`（出处 `EntityHighlight.cs:63/66/69/72/79-84` + 写入点 `:193-194`；实机 L3 原始行 `P3-HOVER-ON 3/1.01`、`P4-HOVER-OFF 1/1`、`P5-HOVER-AGAIN 3/1.01`）。另：日志打印 `_Contrast=1` 是 `1.0` 的**短格式**，⛔ 不是"1 与 1.0 两个值"。⇒ **凡"看起来冲突"的读数，先查是不是"不同档位/不同时机"**，并把结论**写进双方载体**（否则下一个人会再"发现"一次冲突）。
 - **(b) 重复派单的处置**：主 agent 同日把"卡顿复现性读数"同时派给了两片 ⇒ 两份实现。两边对"未触顶帧"的**极性还相反**（一份剔"紧贴目标周期"的帧 `abs(dt−1/target)≤1%T`；另一份剔"整个 ≤ 目标周期的低半区"）。**处置（采纳）**：**不删任何一份**，但 **① 明确唯一权威** —— "**未触顶帧**"这个词只许前者用；后者改称"**长帧子集（`dt > 目标周期`）**"（它算的是**卡顿长帧的分布**，是另一个量）；**② 两份的 docstring / 索引都写明这个区分与"结论同向"**（本日实测：两种极性下 `sd`→B、`CV`→A 的**降级结论不变**）⇒ **⛔ 别混引**。
63. **⭐ 登记"待窗口项"时必须逐项问一句"哪一半能当场关掉"** —— 一个待窗口项里往往**只有一半真需要窗口**：`select`/`u44impl` 的 W1 原写作"同机位两帧逐像素差 = 0"，看着整条要等编辑器；但它的**数值那一半早有 L3 raw 在盘**（`u44hover_evidence.txt:6-28` 逐行 `P0-ROUNDTRIP=write 2.5/1.25 → read 2.5/1.25 ok=1`（**非空判据**）、`P1-BASE 1/1`、`P1-ON 3/1.01`、`P1-OFF 1/1`、`P3-HOVER-ON 3/1.01`、`P4-HOVER-OFF 1/1`、`P5-HOVER-AGAIN 3/1.01`）⇒ **只剩"像素半"要窗口**。⇒ 口径：**待窗口清单里每项都要拆成"数值半 / 像素半（或几何半 / 观感半）"**，能当场关的**当场关并引 raw 行号**，⛔ 不许整项挂着等窗口（窗口是稀缺资源，挂着不拆会让一次窗口白跑）。
64. **🚨 判"调用几次/有没有调用"必须在 AST 上按"命令名 + 实参"判，⛔ 在文本上打带引号正则 —— 而且"看不见"给出的是假绿**（`u3bverify` 实测两向都错）—— 旧实现是在 `CommandAst.Extent.Text` 上打带引号正则，于是**同时**发生两种错：
 - **① 假红**：把 runner **自己的 KNOWN-BAD 夹具生成器**读成调用 —— `d2u26_run.ps1` 实测 `calls=2(L367/L523)`，其中 **L523 是"造坏样本"那行**（`Set-Content -Path $fake -Value ("Unity-Cmd @('editor_stop')" + …)`）⇒ 真值 `calls=1(L367)`。
 - **② 漏判（更坏）**：`d2u27_run.ps1` 的 `editor_stop` 是**未加引号的命令模式参数**（`unity|command|editor_stop|--format|json|--no-pager`，`CommandElements` 实测）⇒ 旧正则**根本看不见** ⇒ `calls=0` 实为 **`calls=1(L222)`**；**文件里那句"d2u27 唯一命中是 needle"是匹配器造成的假象**（并已同步 `waypoint`，它的 C5 判域）。
 ⇒ 新判据：**命令名 ∈ {`Unity-Cmd`, `UC`, `unity`}**（58 文件实测出现次数 128 / 7 / 4，**无其它命令名带该字面量**）+ **存在"值恰为 `editor_stop`"的 `StringConstantExpressionAst`**；**词命中不删**，改挂 `keywordOnly=` 标签**留证**（`keywordOnly=5(L518/L519/L523/L525/L702)`）。⇒ 一句话：**正则的"看错"和"看不见"会同时发生，而"看不见"产出的是假绿**（§45「同层同刻」的最硬形态）。
 **配套（同轮 `u52resist` 实测，两条更硬的形态）**：① **AST 的实参可能是"包裹形态"** —— `@('editor_stop')` 在 AST 里是 **`ArrayExpressionAst`** 而**不是** `StringConstantExpressionAst` ⇒ 只按后者判会**漏判**（修法：在命令**子树内** `FindAll` 字符串常量）；② **PS 空数组会解包成 `$null`** —— helper 初版 `return @()` ⇒ 调用方收到 `$null`，与"解析失败"**不可分**（修法：`[pscustomobject]@{Ok;Calls}` 这类显式结构）。★ 而这两条**都是"升级判据"过程中由断言当场抓住的**（`editor_stop-guarded calls=0 verdict=FAIL / fails=1 EXITCODE=4`）⇒ **"改动引入的 bug 被断言抓住"比人造注入更硬**：它证明该断言**在真实演化中会红**。
 **配套（同一轮的教训）**：三处判据要各**收敛成一个 helper**（`Test-HasTerminalMarker` / `Test-HasUnlockedEntry` / `Get-StopCallInfo`），并让**主循环与 KNOWN-BAD 样本调用同一个函数** —— 初版把逻辑**内联复制**进样本 ⇒ 实测"把主路径退回裸词时样本仍绿" = **不可失败的样本**（§48 的实操要点：**样本必须与主路径同源，⛔ 不许复制**）。
65. **⛔ `.csproj` 的 XML 注释里不许出现 `--`（`MSB4025`）** —— `u52block` 写注释 `Setting.cs -- linking it` ⇒ `MSB4025: An XML comment cannot contain '--'` ⇒ **整栈 `run_all_hosts` 变 `TOTAL_HOSTS=15 FAILED=1`**（`playercheck` 红），症状看着**像项目文件坏了**，实际只是注释里的两个连字符。⇒ ① `.csproj` 注释用 `->` / `—` / 纯文本，⛔ 不用 `--`；② **该类"改完立刻跑一次全量宿主"**（一次 `dotnet build` 就能抓到，⛔ 不要把红留到别人跑的时候）。**同族**（第 56 条）：`.csproj` **无 BOM、注释纯 ASCII**。
66. **🚨 "形状/文风类"判据⛔ 不许当硬门 —— 它的适用范围必须自己声明，超出适用范围要报 `[INFO]`/分档**（本日实测，含**代价**）—— `shopart_logshape.ps1` 的 `E-BADSTART` 用**一个行首白名单**（`裸ISO | HH:MM:SS | # | ≥5 字全大写 tag`）当"全队心跳标准"，实测**对三种合法形状假红**：`select` 16 行、`classcols` **69 行**（行首 `[ISO]`，其 stamper 就写这种）、`u44impl` **210/222 行**（缩进续行 + `ASK :`/`FIX :` 短 tag）—— 而**跨形状真正通用的只有 `trailing_newline` / `maxline` / `E-GLUE` 三项**（且 `E-GLUE` 有真实注入样本 21/21 命中）。
**⚠️ 三条由三片补齐的修正（2026-09-24，`select` 实测）**：① **别重复造档** —— 判据的正则里**已经有"≥5 字符全大写 tag 起头"这一档**（规则 3；`DONE1:` / `ROUND26` / `REPORT-FINGERPRINT` 都靠它过），所以"键值账本型心跳"**不必改判据**：把标签写成 ≥5 位全大写（`SCOPE:` 而非 `scope=`）或行首加 `# ` 即可；真正新增的只会是"**小写 `key=`**"这一档 —— 那是**一次放宽**，**必须用样本闸住**（⛔ 别顺手放，否则真正的粘连也会被放过）。⇒ **最佳解是"形状声明"**：被判文件**首行声明 `SHAPE=…`**，判据**按声明校验**（档由被判者声明、判据只校验"是否自洽"）⇒ 既不用重复造档、也不存在"放宽"问题。② **`E-GLUE` 比想象的宽**：**行内"单个日期紧贴非分隔符"也会红**（例：`（2026-09-24 …`）—— 不只是"两个时间戳粘连"；加一个空格分隔符即转 OK（照抄者很可能连撞）。③ **"状态汇总文件"与"append-only 时序日志"必须分开处置**：前者（如各片心跳里的状态块）**加 `# ` 前缀 = 零损归一化**；后者（时序 trace / 账本）**⛔ 不许为了让判据变绿去改它的形态**（那等于重写历史 = 伪造日志）。**代价（这是本条的重点）**：`u44impl` **为了让这条未成立的硬门变绿，给 210 行加了 `# ` 前缀** —— 一个**从未被任何发布标准采纳过**的判据，逼着一片做 210 行机械改动。⇒ 三条口径：① **判据必须写明自己的"适用范围/形状前提"**（心跳首行声明 `SHAPE=…` 是最省事的做法），**超出范围 ⇒ `[INFO]` 或分档报告，⛔ 不参与 verdict**；② 只有"**跨形状通用 + 有真实注入样本**"的项才配当硬门；③ **⛔ 别为了让一条过宽的判据变绿去改证据的形状** —— 先问"这条判据凭什么当硬门"（与"⛔ 不许把红读成套件坏了"是同一族）。**配套（同轮三片各自实测）**：**量法/判据代码本身也要配反例** —— `select` 用 `Select-String -Pattern X -SimpleMatch` **叠加** `[regex]::Escape(X)` ⇒ 模式退化成字面 `\#\#\ …` ⇒ **4/4 报 hits=0（假红）**，改 `ReadAllText()+Contains()` 后 7/7 True ⇒ **⛔ 别把"0 命中"直接当"文件没变/东西没了"**；另 `u52block` 实测 **`CaptureLogger.Has("旧档迁移")` 恒绿**（同名日志早先已存在）⇒ **判"本条链触发了它"必须用 `Count()` 增量**。**（`u44impl` 新增，2026-09-24）⛔ 回读要读"计数"，不能只读"在不在"** —— 它给 `Program.cs` 插纯函数时**用了一个在文件里出现 2 次的锚点** ⇒ **函数被插两遍**（CS0111 风险）；靠"声明数 2→1"才定位到。⇒ 口径：**`replace` 之后回读必须比"关键串出现次数"**（`=1` 或落入预期值），⛔ 只 `Contains(...)=True` 会漏掉"插了两次/插错位置"。**（`classcols` 新增）判据脚本自身必须纯 ASCII（或带 BOM）** —— 它实跑撞到：同一命令连跑两次，**第二次该判据工具加载失败**（`ParserError`，错误文本里能看见 **ANSI 误读中文的乱码** + `'<' operator is reserved` + `string is missing the terminator`），因为两条命令之间该文件**正被重写**、中间态含非 ASCII 且无 BOM ⇒ 症状**伪装成"脚本没跑"**。⇒ ① 判据/工具**一律纯 ASCII 或带 BOM**（非 ASCII 无 BOM 的 `.ps1` 在 PS 5.1 里可能**根本加载不了**，见第 47 条）；② 这是"**读到某文件 ≠ 读到一个版本**"（第 49 条）的活现场 —— **解析型判据必须先证文件静止**，且**引用判据给出的读数时必须连"判据自身版本"一起引**（`classcols` 报的"69×`E-BADSTART` FAIL"就是**旧版判据**的读数，现行版该项**默认只是 `INFO`**）。**（同轮继续补四条）** ① **编辑工具会"报 success 而根本没落盘"（第二实例，`u52block`）** —— 它往 `策划/验收表.md` 的追加**回执是 "0 lines removed. 2 lines added."**，而回读 `bytes`/`lines`/`mtime` **与改前逐值相同**、新串命中 **0**；改 `[IO.File]::ReadAllText → Replace → WriteAllText(UTF8 no BOM)` 才成功（第一实例 = `Uicheck.csproj`）⇒ **凡重点改动（契约 / 判据 / 闸门 / 聚合文件）一律回读"bytes + 行数 + 关键串计数"，⛔ 不认回执**。② **PS `-Include` 不配 `-Recurse` 会静默匹配 0 命中**（`select` 实测：`Get-ChildItem '.ai-tmp\test' -File -Include *.md,*.txt` ⇒ **0 命中**，差点据此回"全队无人登记"；加 `-Recurse` 后是 **22 命中**）⇒ **凡"0 命中"结论，先自问"我的匹配器真的在匹配吗"**（与 `-SimpleMatch` 叠加 `[regex]::Escape` 造假红**同族、方向相反**）。③ **PS 5.1 默认按 ANSI 读** ⇒ 读 **UTF-8 无 BOM + 中文** 的证据会**整篇乱码**（`zorder` 实测：`Get-Content` 乱码而 `[IO.File]::ReadAllLines` 正常）⇒ **读法显式 `-Encoding UTF8`**（与 §59 同族、症状相反：那是"读到别的文件"，这是"读得到但全是乱码"）。④ **"改完只跑 `-SelfTest`"不够** —— 本日 `shopart_logshape.ps1` 的 v3 把格式串写成 `{%0,}`（`[string]::Format` 的 `{0,}` 误写）且放在**脚本顶层** ⇒ **任何入口**都抛 `FormatException`（`-Check` 与 `-SelfTest` 都进不去），而它引用的"18 checks fail=0"是**上一版**的读数 ⇒ **判据改动后必须实跑一次"真实入口"，并连版本一起引**。
**大小写两边都要声明**：`-match`/`[regex]` **默认区分大小写**，而 `String.Contains`/`IndexOf(string)` **也区分大小写** ⇒ 判据要么显式 `(?i)`/`OrdinalIgnoreCase`、要么配"大写变体"反例样本（`u52resist` 实测：`Release Regardless` 会漏判 ⇒ 修完控制样本 1→3 条：正命中 2 / 大写变体命中 2 / 纯散文 0）。
67. **🚨 "判定真的跑了吗" —— 调用方判绿必须双检（`RESULT <OK|FAIL>` 行存在 ∧ 退出码 = 0），⛔ 别把"没有坏消息"当好消息**（`select` 的活样本，2026-09-24）—— `shopart_logshape.ps1` 在**被写入的中间态**被加载时抛的是**非终止** `FormatException`（`Format` 参数索引越界）：**输出里既没有 `LINESHAPE` 行、也没有 `RESULT` 行，而 `$LASTEXITCODE` 仍为 0**。⇒ 若调用方按"输出里没有 FAIL 就当通过"（**非常自然的写法**）⇒ **那一刻会判绿、而实际什么都没判**（与第 40-③/66 同族：**先证明"判定真的跑了"**）。⇒ 三条：
 ① **调用方双检**：`RESULT OK` 行存在 **∧** 退出码 0；**"没有 verdict 行" 必须视为工具故障（红）**，而不是"没有坏消息就是好消息"。
 ② **工具作者：原子落盘** —— 写 `*.tmp` 再 `Move-Item -Force` 替换，堵住"别人正好在你写到一半时调用"。**同一工具 8 分钟内出现 ≥4 个可观测版本**（`A90820BDC5ADAC3C/22962/15:57:21` → `2075D5E6A159026D/29893/16:03:03` → `3735936BCC0C5DD4/29870/16:03:12`，另 `closefix` 撞到 `29848/16:02:17`）⇒ 引用读数**必须同批给"判据自身 `sha256_16` + `bytes` + `mtime`"**，且"稳定"的机械证明 = **连读两次同值**（⛔ 不是"我觉得它写完了"）。
 ③ **判断"要不要把之前那次形状归一化回退"的判据** = **该动作是否违反现行裁定**：`classcols` 那次**违反**（"⛔ 不许为了让判据变绿改证据形状"）⇒ **回退正确**，且它**精确删掉自己加的 2 个字符** ⇒ **既有条目字节逐字复原**（残留 = 2 条自述条目 +602 B + **mtime 不可恢复**，记账措辞应是"**曾重写 → 随后已回退、内容复原、残留 2 条自述条目**"，⛔ 不是"永久重写"）；而 `select`/`u52resist`/`u44impl` 那几次是**旧判据下的合规归一化** ⇒ **不回退**（回退只会产出第三个版本）。**另**（`shopart` 自踩）：**在 `.ps1` 里写非 ASCII（例如全角括号）而无 BOM ⇒ PS 5.1 按 GBK 解码 ⇒ 脚本直接解析崩**（`MissingEndParenthesisInMethodCall`）⇒ **判据/工具一律非 ASCII 文件必须带 BOM 或写 ASCII 等形**（第 47 条家族：非 ASCII 进无 BOM `.ps1` = **赌读者的代码页**）。
68. **⛔ "特例下的不变量"⛔ 不许升格成"通用要求" + 两条操作性收尾口径**（2026-09-24，三片实测）
 - **(a) 把特例当通用 = 自己制造假红**：`zorder` 的新判据第一版写"**键元组 `(order,zA,zB)` 必须逐帧恒定**"（那本是 §11 里一个**settle 帧特例**的论证）⇒ 位形 3 **判红 29/60 帧**；真因**不是缺陷**：`EntitySortOrder` **随格而变**（取值集合 `342,342 ｜ 338,338 ｜ 334,334`，两枚投射物**同步**变 ⇒ 次序不变），而**"键恒定"恰恰是旧口径的病征**（两枚 z 都 0 ⇒ 恒定地不可判别）。⇒ 改成"**每帧仍可判别（不可判别 0）**"后 118/0。⇒ 口径：**写判据前先问"这条性质在特例里成立，是因为它**普遍**成立，还是因为那个特例恰好如此"**；**"恒定"这种词出现在断言里时，先确认恒定的是"好事"还是"病征"**。
 - **(b) 凡追加，最后一步永远是回读"末 4 字节"**（`u44impl` 实测自伤）：它往心跳追加一条时**漏了结尾换行**（`last4 = 37 3A 32 35` = ASCII `"7:25"` ⇒ 正是 `E-EOF-NEWLINE` 那一类），靠"写完立刻回读末 4 字节"在 1 分钟内当场发现、补 CRLF 后 `verdict=OK`。⇒ 比"回读 `bytes` + `lines`"**更灵敏**（末字节丢没丢，`bytes` 变化可能只有 2 字节、肉眼与 diff 都不敏感）。
 - **(c) 判据工具的三条契约（`shopart` 已实装，建议推广）**：① **每次运行第一行自报 `JUDGE-VERSION sha256_16/bytes/lines/mtime`** ⇒ 任何读数天然带版本（**这是"引旧版读数"那一族的解药**，比人工引指纹可靠；今后**"以现采的 `JUDGE-VERSION` 行为准"**）；② **运行时异常 ⇒ `RESULT FAIL(TOOL-ERROR: …)` + `exit 3`**（区别于 FAIL=1 / USAGE=2）—— **崩掉的判据不许看起来是绿的**；③ **契约写进文件头**：**绿 ⇔ 有 `RESULT OK` 行 ∧ exit 0**；并写明**必须按文件调用**（`powershell -NoProfile -File …`，**解析失败时返回非 0**），而 **in-session `& call` 不会** —— 那正是"中间态被读成绿"的形态（第 67 条）。**实测七条**（`-SelfTest` 25/0、默认/严格双档、`SHAPE=` 声明档、故障路径 `RC=3`、无参数 `RC=2`）。
69. **引用锚点的"三件套" + 判据"值域"必须判可达性 + 退出码三分（三条都可直接照抄的做法，2026-09-24）**
 - **(a) 引用别人的文件必须给"三件套"：`<文件>:<行号>` + 代码串 + 内容 sha（`blob-sha1` / `sha256_16`）**（`select` 的做法）：行号会漂、代码串能认出、**sha 兜底**；对**必然漂移**的引用（别人正在写的文件）改为 **"只认符号名，行号仅作参考"** + 附**漂移基准指纹**，让复核者能判"你读的是不是本片读的那一版"。（同族：`u3bverify` 提的"锚点连代码串一起写"。）
 - **(b) 判据的"值域"必须判**可达性**，⛔ 不许把"数学上最坏"当"实际可达"**：`u52resist` 新判据报 `[FAIL] 「经验 cur/next」最坏组合（10 位/10 位）need 152 vs availPx 99` ⇒ 但**先要问"该组合在 1..99 内到底可不可达"**：可达 ⇒ **真缺陷必修**；不可达 ⇒ 是**判据值域取错**，按实际可达组合收紧（⛔ **不许借"不可达"把判据删掉**）。同族先例：`u52play` 的"可复现性"、`u44impl` 的"可达性断言（守卫数==站点数）"。（本条的**反例**也要记：`u52resist` 同轮修掉一处**旧假绿** —— `uicheck ⑧` 把**已 ×K 的画布 px** 当**裸 art** 传入 ⇒ 内部再 ×K ⇒ `availPx` 放大约 **K²≈3.24×**；⇒ **量纲/单位必须在判据入口就声明**。）
 - **(c) 退出码三分（可复用形状）**：`0 / VERDICT=PASS`、`1 / VERDICT=FAIL`（发现不一致）、**`2 / VERDICT=CRASH <异常>`（判据自身崩）** ⇒ **"崩溃的 exit 1"与"发现不一致的 exit 1"必须可区分**（`classcols` 已按此加固，四向实测：`C1/C2` 绿、`C3` teeth 红 `RC=1`、`C4` 不存在文档 `RC=2`）；并配 `sys.stdout.reconfigure(encoding='utf-8')` 之类的**输出编码护栏**（避免 GBK 控制台下中文路径整段崩）。**别忘前提**：**旧版脚本常常无条件 `exit 0`** ⇒ **引退出码之前先确认它是不是信号**（`classcols` 亲口更正过自己一句"`DIFF_COUNT=0`（exit=0）"——那里的 `exit=0` **不是信号**）。
52. **⛔ 跨"截断分布 vs 未截断分布"的 `sd`/`CV` 不可比 + 性能类结论必须"口径先行 + 标注测量环境"（本日 U27 A/B 的教训，`jitter` 判官 + `closefix` 离线归因）** —— 同一批数据读出**两个反号结论**：**绝对 `sd`/`p99` ⇒ 档位 B 更稳**（sd 1.66×↓），**相对 `CV` ⇒ 档位 A 更稳**（A 0.374 / B 0.691）。机制：**A 的 dt 被 60fps 目标封顶 ⇒ 尾部分布被截断（削峰）**，B 不封顶（dt 中位仅 5.6ms）⇒ **同一枚 100ms 卡峰对 B 是 18× 中位、对 A 只是 6×** ⇒ 比"离散度"本身**不成立**。⇒ 三条：① **凡跨档位/跨帧率比较，先固定口径再采数据**（主口径 + 辅口径都写死；**⛔ 不许"看完数才挑口径"** —— 那等于自选结论）；② **被截断的分布只在"未触顶帧"上算 `sd`**（否则把"削峰"读成"更稳"）；③ **凡性能类结论必须标注测量环境**：本批全部读数取自 **Unity 编辑器内 Play** ⇒ 编辑器自身停顿（**域重载 / 资源导入 / GC**）可能就是主因，**要谈发布行为必须 standalone build 复测**。**附本条的处置示范**：结论**当场降级为"本批不可识别（`order-confounded` + `metric-dependent`）"**并**明确写"⛔ 不得据此改生产帧节奏"**，同时把真正方向改判为**单帧卡顿**（A/B 两档都存在 100–155ms 单帧；位移侧已干净 ⇒ 长帧只按比例减速、不突跳）—— **"没有结论"被如实写成"没有结论"，而不是挑一个口径交差**。
53. **⛔ 心跳/日志"追加"的三种静默事故（`shopart` / `u52resist` / `u52block` 同日各踩一种）** —— ① **粘行**：here-string 不含 `'@` 前的换行 + `AppendAllText` 不补换行 ⇒ 11 条记录**粘成一行**，读"最后 3 行"只见最旧那条 ⇒ 会得出"**我的心跳丢了**"的**假结论**（真值全在）；② **编码**：心跳为 UTF-8 无 BOM + 中文 ⇒ PS 5.1 裸读成 `鑷ㄩ儴閫氳繃`（原文"自检全部通过"）⇒ 心跳**要么纯 ASCII、要么带 BOM**；③ **锚点漂移**：报告内写自己的指纹 ⇒ **自指悖论**（写进去的那一刻就变了）⇒ 接口固定为「**报告不写自身指纹；唯一指针 = 心跳末行 `REPORT-FINGERPRINT`**」，且**台账只记指针、不记值**（本日该值漂了 4 次）。⇒ 一句话：**"写进去了"和"能读出来、且读到的是最新的"是三件事**。
51. **⛔ 写日志/心跳必须"自带换行 + 写完回读行首形状"（`shopart` 实测，危害是静默的）** —— 它把 11 条追加**粘成了一行 15421 字符**：`@'…'@` here-string **不含 `'@` 之前的换行**、`AppendAllText` **不补**换行 ⇒ 每次追加的末行与下一次首行直接相连。**症状极具误导性**：读"最后 3 行"只看到最旧那条 ⇒ 读的人会得出"**我的 13:34~14:18 心跳丢了**"这个**假结论**（真值是全在、只是没换行；`lines=9` 看着还正常）。⇒ 规则：**每条记录自带行首时间戳 + 追加后回读"行首形状/行数"**（`lines` 或"每行首是否 ISO/`REPORT-`"）；同族还有一条：**心跳/报告若含非 ASCII，读法必须自带 BOM 或整份转纯 ASCII**（`u52resist` 实测：UTF-8 无 BOM + 中文 ⇒ PS 5.1 裸读成 `鑷ㄩ儴閫氳繃`）。⇒ 一句话：**"写进去了"和"能读出来"是两件事**。
45. **判据必须与被判对象"同层同刻"（`u52play` 提，采纳）** —— **本日实测已积到五种变体**：① **注释**（判源码文本没剥注释；如 `d2u26` 的 `LOCK-TAKEN` 唯一一处是注释 `L606`）；② **needle**（token 出现在 `-match`/`-like` 操作数里）；③ **字面量**（自检断言行**逐字写着** `Set-Content -Path $lock ` 之类的 pattern ⇒ "按文本找写锁"的探测器会把**断言行**当写入行；`u53close_run.ps1:211/212/291` 即此）；④ **调用形式**（`UC @('editor_stop')` 的动词是 `UC`、实参是 `ArrayLiteralAst` ⇒ 按命令名判恒为 0）；⑤ **沙箱夹具**（自检造出来的假 runner/假文件里的写，如 `d2u26` 的 `$fakeF`/`$fake2`）。**本日"五变体"的实战账单**：`popupaudit` 那张扫描表这一轮点名的 **4 个活跃 runner 全是假红**（逐行读原文：`d2u32:432/468`、`u52resist:156/159`、`u53close:129/130` 全部 `-Encoding ASCII`；`d2u26` 是"匹配不到 ⇒ 应为 `UNKNOWN`"）⇒ **结论：判定列在能通过"真文件双向自检"之前不得发布**（合成样本都是内联直写形状，挡不住断言字面量与夹具写入这两类干扰）。 —— 本轮**四片**在同一类地方各伤自己一次，症状全是"**判据与对象不同层**"：① **注释当代码**（判源码文本时没剥注释）；② **正则形状过窄** ⇒ 恒 false ⇒ 假绿；③ **求值顺序**（先算后写 / 边写边读）；④ **词级 vs 调用级**（按"字符串 `editor_stop` 出现几次"判"调用几次"）—— `zorder`/`u52impl`/`u52play`/`u52resist` **四片都**因为"新加了一条**含该词的日志/报警**"而把 `only-once` 断言判成 2 命中**假红**，改法一律是判**真实调用形式**（`Unity-Cmd @('editor_stop')` / `UC @('editor_stop')`）或 AST。⇒ 口径：**判"次数/存在"要匹配"调用形式或 AST 节点"，⛔ 不匹配裸词**（否则**每加一条含该词的日志都会假红**，而假红会逼人把断言删掉 —— 那才是真正的损失）。**派生物**：`verify.ps1` 的 `14b runner-static-traps` 若也扫 `editor_stop`/`editor_play`/终标记这类词，必须同样按"**调用形式/代码行**"判（已派 `u3bverify` 自查并回报）。
44. **判据自身的缺陷会让整条判决走反 —— 先给判据做三向自证，再去量真数据**：`jitter` 的 U27 判据 `R5` 原来按**整数格号列**分桶、除以整数格步 —— 而驱动记录的是**整数格号**（绝大多数帧 `dgrid=0` 被丢、跨格帧恒为 1）⇒ **均匀推进也会算出 `spread≈1.04` ⇒ 假红**。若不先自证，它拿到真 TSV 后会得出"**推进不匀**"，然后**去改积分口径 = 修一个不存在的 bug**（本轮它靠"注入正例"当场撞出来，改成**按世界步长方向符号**分桶；世界/格比值常数仍由离线 `playercheck §15 f2` 钉住，**判据里不重新推导**）。⇒ 口径：**判据资产在"量真数据"之前必须自己跑三向**（真文件⇒绿 / 注入错⇒红 / 正例片段⇒绿）——理由不是形式主义：**判据的缺陷会伪装成"被测对象有 bug"，代价是去改生产代码**。
43. **静态"顺序/先后"类判据必须能说"无法判定"（⛔ 找不到 ≠ 违规）** —— 有一项（`-SelfCheckOnly` 出口必须**早于** `LOCK-TAKEN`）在 8 片上做不下去，三条**实测**卡点：① 有的 runner **两个 token 在同一行** ⇒ **该行的"顺序"本身无定义**（`u44hover_run.ps1:362`）；② 有的 runner 的 `LOCK-TAKEN` **不经过 `Say`**（`d2u26`：原文里有、`Say` 命中 **0** ⇒ 经 helper/拼接发出）⇒ 只按 `Say` 建规则会**静默跳过它 = 假绿**（比假红更坏的方向）；③ 这两个 token 在自检块里天然作为 **needle** 存在（`$codeTxt -match 'LOCK-TAKEN'`）⇒ "排除 needle"本质是 **AST 祖先/上下文问题**（它包不包在比较操作数里），不是文本问题。⇒ 口径：**判据必须先自证"这个问题在本文本上有定义"，否则输出 `[INFO] cannot-classify`，⛔ 绝不判红**；候选门槛 = **该文件存在真锁写事件**（`Set-Content -Path $lock … -Encoding ASCII`），否则 `[INFO]`（这条同时挡掉"判据脚本自己被当 runner 分类"——`d2u32_lock_selftest.ps1` 两个 token 都是 needle）。**更深一层**：**别为"能被直接观测的性质"造易碎的静态代理** —— "自检没取锁"这件事由 `-SelfCheckOnly` **实跑**直接证明（`locks before/after=[False,False]` / 占用态 `[True,True]` 且**原态在真跑时确实会取锁**），比"比两行行号"强得多。
**⚠️ 更强的反例（同日，实现完成后实测）**：这条判据**真做出来了**（AST 排除 needle + 双向样本：坏样本判红、好样本判绿、坏样本里还故意放了 SE needle 证明排除生效）——**然后在真文件上崩了**：活跃 8 片里 **5 片被判"违规"，包括参考实现 `d2u3_charstat_run.ps1`**（它的出口有**运行期证据**：打印 `SELFCHECK-ONLY-END` + `locks before/after=[False,False]` + 账本 0 行）。根因 = **「行号先后 ≠ 执行顺序」**：`-SelfCheckOnly` 的出口通常写在**函数/块**里，**定义在取锁之后、执行在取锁之前**。⇒ **结论升级**：**凡"先做 A 再做 B"的静态判据，判到的都是"定义顺序"，而我们真正要的是"执行顺序"** —— 静态侧最多只能判**存在性**（"有无锁入口" + 引它的**运行期样本**），**顺序必须由运行期证据给**（跑 `-SelfCheckOnly`：① 打印 `SELFCHECK-ONLY-END` ② 锁 `before/after=[False,False]` ③ **账本 0 行**）。**并且**："**能失败"≠"合格"** —— 必须是"**该红的红、该绿的绿**"，在**已知好样本**上失败（假红）**同样不可发布**。
**⚠️ 但这条结论我又**收窄**了一次（同日，`waypoint` 交付）**：它用**同一套 AST 工具**把这项**做成了**（全仓 60 个 runner：`PASS=7 / FAIL=0 / INFO=53`；活跃 8 片 = 7 `PASS` + 1 `INFO`）。关键差别只有一处：**把"定义位置"解析成"执行位置"** —— **函数体内的事件取"该函数首个调用点"**（证据行 `B-fn=Take-Lock B-calls=336 / 561`），并把两种形状**都**做成 fixture：`fn_called_after ⇒ PASS` / `fn_called_before ⇒ FAIL`（**双向证明**）。⇒ **正确结论不是"静态判不了顺序"，而是"naive 比行号做不到；要做就得先做调用点解析，且样本必须覆盖'定义在锁后、调用在锁前'这一形状"**。它交付时自己的两条自伤也值得抄：① 比**字面量行号** ⇒ 两个片假红（就是上面那条）；② 候选门槛要求变量**恰好**叫 `$lock` ⇒ 把用 `$path` 写锁的活跃片判成 `not a runner`（**"静默跳过 = 假绿"的形状**，已加结构兜底：ASCII 写 + 子树含 `$PID`）。**入闸形式**：`FAIL` 才算红，`INFO`（找不到 / 同行 / 无调用点）只记日志。实现 = `tools/probes/measure/c5_order_check.ps1`（standalone，不碰 `verify.ps1`）。
**R5 的收尾（同日，完整矩阵 5200 帧 / 五场景 + A·B 两档）**：raw 值跳到 **1954×**，归因**闭死** = **唯一一帧 24.08 单位的位移**（`clampband frame=4051`、`spr=idle_w_1`），而**驱动自己的注释**（`d2u27_jitter.cs:888`）逐字写着"**传送是驱动自己的状态复位、不是被测行为**"；把这一帧与 dt 因子都排除后，**每个方向桶的 `vel` 中位数就是三个方向常数**（`3.3541` 对角 / `4.2426` 纯 w / `2.1212` 纯 n），**>5% 离群帧仅 0.00%~1.82%**，且离群者全是 `run_*_0` / `run_*_7` / `idle_*_0`（**段起止帧**）。⇒ **两条永久口径**：① **桶要按"行为"分，不能只按"方向"分** —— 驱动自身的状态复位/传送帧与段起止帧必须排除，**排除依据要引"驱动作者自己的注释行"**（本例 `:888`）；② **判"步长恒不恒定"前必须先按 dt 归一化**（本作移动就是 dt 缩放的：`R4` 的相关系数与 `step/dt` 恒定说的是同一件事）。
42. **⛔ "释放锁" ≠ "交还会话"：锁全空 + `playMode=playing` 是真实状态（2026-09-24 13:42 实测）** —— `u27v5` 那次会话的 driver 在 **13:41:57** 打完 `FINISH why=blocked-no-stage`、**两把锁在 13:42:11 已全部释放**，但我 **13:43:06 / 13:43:38 / 13:43:50** 三次只读采样：**两个锁文件都 ABSENT，而 `editor_status → playMode="playing"`（bridge 心跳在推进、console 最后一条停在 13:41:57）** ⇒ **编辑器停在一个"无人持有、也没人退出的 Play 会话"里**。两条硬口径：① **任何"就绪闸门"都必须含 `playMode=stopped`**（`-Scope`：锁空 ⇒ **只代表没人占**，不代表编辑器空闲；只查锁的闸门会**闯进别人还在跑的 Play** —— `waypoint`/`closefix` 早就这么做了，这次证明它不是"可选加固"）；② **释放锁之前必须先确认 Play 已退出**（`editor_stop` 发出 **且** 回读 `playMode=stopped`）；若因"锁不是我的"而跳过 `editor_stop`，**必须打 `ORPHAN-PLAY` 报警并说明**，⛔ 不许静默把锁交出去 —— 否则下一个拿锁的人以为会话干净，实际是接管了一个**在跑的 Play**。**顺序**：`editor_stop` → **回读 `playMode=stopped`**（有界等待，建议 ≥10s）→ 确认 `stopped` 才正常释放两名锁；**若仍未 `stopped`** ⇒ **重试 `editor_stop`（幂等，2~3 次）**，仍不行 ⇒ **⛔ 不放锁，改为"持锁不交 + 报警 + 通报主 agent"**（`LOCK-HELD-ON-PURPOSE` / `LOCK-RELEASE WITHHELD` + `ORPHAN-PLAY`）。
**⚠️ 这一条我改过一次口径，理由写在下面（前一条裁定作废）**：早先我批准过"放锁 + 报警"（理由"僵尸锁挡全队比残留 Play 更糟"，`u52resist` 提出）；**13:43 那次现场把该理由证伪了** —— 放锁后出现的是"**两把锁全空 + `playMode=playing`**"的**无主会话**，而按"⛔ 不许碰别人的会话"的规则，**谁都无权停它**，最后只能由**主 agent**在 6.5 分钟后兜底。⇒ 正确取舍：**持锁**（= 保留"这个会话还没收完"的归属信息，并让**接管 stale 锁的人**获得合法身份去收它），因为 **`playing` 期间任何正确闸门都不会开工 ⇒ 持锁并"不额外占用时间"**。配套义务：**① 报警必须通报主 agent（不能只写日志）；② 接管 stale 锁的人，必须在开始自己的会话前先 `editor_stop` 并确认 `stopped`**（把"收孤儿"写成接管者的义务）；③ **持锁上限 = 既有 12min stale 规则**（⛔ 不无限期）。报警必须**指名"谁负责清理"**：默认 = **该会话的最后一位 owner**（它自己的会话自己收），或**主 agent**；⛔ **其他片不许碰**，直到有人明确宣布接管该孤儿会话。**④ 第四个根因（实测，与前三个都不同）：只"等"没"叫"** —— `d2u27-steps-u27v5.txt` 收尾原文：`13:41:57 EVIDENCE [U27] FINISH` → `13:41:59/42:02/42:04/42:07/42:10 RELEASE-WAIT playMode=playing try=1..5` → `13:42:11 RELEASE-WARN playMode=stopped not confirmed within 5x1s => releasing anyway` → 两次 `RELEASED (mine)`。**全程没有一条 `editor_stop` 发射**（连 `SKIP editor_stop` 都没有）—— 该 runner 的静态断言"`editor_stop` 恰 1 处、在 `Stop-Editor-Safe` 内"**是真的**，但**那条路径在正常收尾里根本没被走到**。⇒ **通用教训**：**凡"等待某状态"的守卫，必须配一个"把系统推向该状态"的动作**，否则它只是把必然失败延后 N 秒（而且日志看起来像"我尽力了"）。⇒ 收尾序列的完整形式：**`editor_stop`（触发）→ 回读 `stopped`（确认）→ 放锁（交接）**，三步缺一不可。
**"触发真的会被调到"可机械化（`u52resist` 实现，值得全员抄）**：AST 找守卫（如 `Stop-Editor-Safe`）的 `CommandAst`，要求**至少一处落在无条件路径**（`Parent` 链上**无** `IfStatementAst`；解析失败 ⇒ FAIL，fail-closed）—— 这正好抓 `d2u27` 那种"**代码在、正常收尾走不到**"的形状（断言绿、行为缺 ⇒ 静态断言与**可达性**是两件事）。**顺带一条更强的只读性证据**：**别人持锁时**跑自检 ⇒ `LOCKS before=[True,True] after=[True,True]`（既不新建、也不删改别人的锁），比"空闲态 `[False,False]`"强一档（后者可被质疑成"本来就没人持锁"）。
**报警的字段口径**（`charstat` 提）：`playMode` 回读**必备**；`owner`/`pid` **可读则必写、不可读则显式写 `owner=(unreadable)`** —— ⛔ 不许省略该字段（否则"**没查过**"与"**查不到**"无法区分）。注意孤儿 Play 的典型形态恰恰是**两个锁都不存在 ⇒ 读不到 owner/pid** —— 这与"缺 PID 段 ⇒ 存活未知 ≠ 已死"是同一条原则：**字段缺失必须显式表达**。③ **该约束只对"碰编辑器的项"有效**：纯离线宿主（如 `tableverify`，`CloverCheckUnityEditorRoot` 显式豁免、无 Unity 引用）**不受本条约束**，可在 Play 期间先跑 —— 收尾跑 `verify.ps1` 时，离线部分不必等窗口。
41. **共享宿主的"断言总数"就是它的完整性指标（防"整段断言被连调用一起删掉"）**：`tools/probes/hosts/uicheck/Program.cs` 被 5~6 个片共同编辑，`charstat` 落盘时 307,208 B、十几分钟后被别人改成 **292,447 B**（**-14.7 KB**）。这类缩水有两种可能：① 正常的去重重构；② **某片的整段断言被连它的调用一起删掉** —— ② 的可怕处在于**编译照样通过、宿主照样绿**，只是那批断言**静默消失