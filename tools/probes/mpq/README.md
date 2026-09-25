# tools/probes/mpq —— 原版 MPQ 按名解包工具链（判据资产）

> 依据：全局 `clover-engine` skill §3.5 —— **探针 / 驱动 / 量法脚本属判据资产** ⇒ 落 `tools/probes/` 并提交。
> 来历：2026-09-22 `mpq-unpack2` 片的一次性脚本（`.ai-tmp/test/{build_wanted,extract_wanted,landed_audit}.py`）；
> 2026-09-22 `g1-naming-and-mpq-asset` 片提升入仓（原处不留副本）并**修掉 G-1**（按名清单侧命名错误）。

## 1. 用途（一句话）

从**玩家自备的原版包**（`D2data.mpq` + `Patch_D2.mpq`）里，把我们**真的要用**的原版件按名解出来，
落到 `原版资源/{d2dc6,d2raw,d2text}/`，作为 `tools/d2codec/*` 各生成器与 `uicheck` 的输入。

⛔ 它**不是**素材来源清单：解出来的东西仍然只有被引用的那几个才准进 `client/Assets/`（路径收敛到 `Core/ResPaths.cs`）。

## 2. 三个脚本

| 脚本 | 干什么 | 判什么 / 产出 |
| --- | --- | --- |
| `build_wanted.py` | 组装**按名清单** `wanted.txt` + 逐条**来源登记** `wanted-sources.tsv` | R1/R2/R3 三条硬规则（见 §5）；另出 `wanted-dropped.tsv`（被 R1 丢掉的疑似外推名）与 `wanted-undeclared.tsv`（**官方表没声明、但工程源码在问包要**的 `.ds1`/`.dt1` ⇒ 给主 agent 裁） |
| `extract_wanted.py` | 按名 `SFileExtractFile` 解包（StormLib via ctypes） | `unpack-<tag>.log` / `unpack-<tag>-missing.txt`（**包内没有该名字**的名单）/ `unpack-<tag>-heartbeat.txt` |
| `landed_audit.py` | 只读核对「清单 vs 盘上真正落地的产物」 | `landed-audit.tsv`：`name<TAB>target<TAB>exists<TAB>bytes`。**`exists=0` 的行数就是"缺名数"** —— G-1/G-2 的判据数都出自这里（理由：`route()` 会把**只差大小写**的同名映射到同一路径 ⇒ 解包日志的成功计数 ≠ 盘上文件数） |

## 3. 复跑命令（任意 cwd 都可；脚本自己按位置向上找含 `client/Assets` 的仓库根）

```powershell
# ① 生成按名清单（需要工程内已解包的一份 Items PNG + 官方 1.10f 表在位）
python tools/probes/mpq/build_wanted.py

# ② 按名解包（需要 D2data.mpq / Patch_D2.mpq / storm.dll，见 §4）
python tools/probes/mpq/extract_wanted.py --tag unpack-g1

# ③ 落地核对（只读；缺名逐条打印）
```

产物默认落 `<仓库根>/.ai-tmp/test/`（一次性目录；⛔ 不进 `原版资源/` 之外的任何工程目录）。
`--root` / `--out-dir` / `--refs` / `--mpq-dir` / `--storm-dir` 都可用显式值覆盖。
退出码：`0` = 全成功；`2` = 有缺名 / 缺输入（**缺名不是崩溃，是判据**）。

## 4. ⛔ 本地前置（都不入仓）

| 依赖 | 放哪 | 怎么拿 |
| --- | --- | --- |
| `storm.dll`（StormLib，x86_64，**482 304 B**，头 `4D 5A`） | `.ai-tmp/test/storm/storm.dll` | 取源 = Diablerie 仓库（`mofr/Diablerie`）的 `Assets/StormLib/lib/x86_64/storm.dll`：<br>`curl -L -o .ai-tmp/test/storm/storm.dll https://cdn.jsdelivr.net/gh/mofr/Diablerie@master/Assets/StormLib/lib/x86_64/storm.dll`<br>**建议 sha256 = `642044ea1ea0863f1a5cd570608825955072cf3fa7993863199d7a7dd099e507`**（本仓实测值；同仓库 `Assets/StormLib/StormLib.cs` = 托管侧样板）。同目录另有 `StormLib.cs`（参考用，脚本不依赖） |
| `D2data.mpq`（**292 106 381 B**）<br>`Patch_D2.mpq`（**4 949 801 B**） | `原版资源/_mpq_incoming/` | 玩家自备的原版安装目录里取；身份（字节数 / SHA256 / MPQ 头）见 `原版资源/清单.md` §5.1 与 §6.1 |
| 官方 1.10f 数据表（`LvlPrest.txt` / `LvlTypes.txt` 等 92 个 txt） | `原版资源/参考工程_Diablierie/d2lod1.10txt/data/global/excel/` | 见 `原版资源/清单.md` §1（`SheXMods/d2lod1.10txt`） |

**下到 `.ai-tmp/test/storm/` 之后，§3 的命令即可直接跑**（`--storm-dir` 默认就指这里）。
⛔ **482 KB 的第三方 DLL 不提交进仓**（第三方二进制 + `原版资源/`、`.ai-tmp/` 本来就在 `.gitignore` 里）。

### 4.1 两个坑（⚠️ 勿犯；写死在脚本注释里）

1. **`storm.dll` 是 ANSI 接口** ⇒ 调它之前先 `os.chdir(<mpq 目录>)`，并且**只用 ASCII 相对名**
   （`SFileOpenArchive("D2data.mpq")`）。⛔ 不许把带中文的绝对路径交给它
   （本仓库根 `…/f-v2/clover-project-diablo2` 恰好全 ASCII，但**别依赖这一点**）。
2. **⛔ 不许 `SFileFindFirstFile` 全量枚举**：实测在这份 DLL 上会**吃到 8+ GB 内存且不返回**
   （产物文件 0 字节）；**按索引也拿不到名字**（`SFileFindNextFile` 的 `cFileName` 不可靠）
   ⇒ 唯一可行路线 = **按名** `SFileExtractFile`，所以按名清单是**前置条件**。

另两条同源实测（同样写进 `extract_wanted.py` 头）：
- **`SFileHasFile` 返回值不可信**：对绝不可能存在的名字也返回非 0（实测 `-803602432`）
  ⇒ 判据永远是「`SFileExtractFile` 后**产物落盘且 > 0 字节**」。
- **宿主 safe-delete 守门**：同一轮累计 **> 500 次删除**会被拦下并让脚本崩
  ⇒ 覆盖落位一律 `os.replace`（Windows = `MoveFileEx + REPLACE_EXISTING`，不产生 unlink），
  ⛔ 不许 `os.remove` 目标 / `rmtree` 临时树。

## 5. `build_wanted.py` 的三条硬规则（G-1 修的就是这三条）

> 完整动机见脚本 docstring。改这三条之前先读 `原版资源/清单.md` §6.4 的 G-1 / G-2 行。

- **R1「只照表声明」**：`.ds1` / `.dt1` 只许来自 **官方表声明列**
  （`LvlPrest.txt` 的 `File1..FileN`、`LvlTypes.txt` 的 `File 1..File 32`；Act=1 且 Expansion=0）
  **或** 工程源码里的字面量（活代码在问包要文件）。散文（`.md`/`.tsv`/`.txt`）与一次性手写清单里
  而表里没声明的 `.ds1`/`.dt1` **一律丢弃**并记进 `wanted-dropped.tsv`。
  ⇒ 修掉了「按 `Bord1..4` 的字母后缀外推出 `Bord5..12{b,c,o,oe}`」那 32 个假名
  （官方表里 Border 5..12 只有单文件）。
- **R2「不许编名字」**：名字必须是字节可查的路径字面量，且**不许含空白字符**
  （旧正则的字符类含空格，会把 `docs/**/*.md` 里跨行的 `data\\local\\font\\  font16.DC6` 切成"名字"）。
- **R3「注释不算引用」**：源码里命中的路径**只在非注释行**才算数 —— `//` / `///` / `#` 是散文。
  实测依据：`MapGenWildLayout.cs` 的「剔除记录」注释块列了 32 个**不存在**的 `.ds1`；
  `NpcDialog.cs` 等处的「键名对照」注释带一个**跟随不到**的 `data/local/string.txt`。

**来源登记**（每条带 `文件:行`，见 `wanted-sources.tsv`）：
① `official-table` 官方表声明列 · ② `code-literal` 源码非注释行 · ③ `data-literal` `.tsv/.json/.txt/.asset/.prefab`
· ④ `doc-literal` `.md` · ⑤ `d2ui-table` `export_d2ui.py` 的源表 · ⑥ `items-png` Items PNG 名反推
· ⑦ `named-in-code` 逐条带出处的补登项 · ⑧ `tbl-set` 串表三件套。

## 6. 落点契约（`extract_wanted.py` 的 `route()`）—— ⛔ 换布局 = 让下游生成器全部找不到文件

| 落点 | 装什么 | 为什么 |
| --- | --- | --- |
| `原版资源/d2dc6/<原名>` | 所有 `.dc6`（`data/global/ui/**`、`data/LOCAL/UI/chi/**`、`data/LOCAL/FONT/chi/**`、`data/global/items/**`） | 这一层是**逐帧图**的来源，只给 `export_d2ui.py` 读 |
| `原版资源/d2raw/<原名>` | `data/global/{tiles,palette,excel}/**` **和** `data/local/font/chi/*.tbl` | 这一层是**原始二进制**（`.dt1`/`.ds1`/`.pl2`/`.txt`/字体步进表），只给 `export_tiles.py` / `export_wild_layout.py` / 表转换读，不产出图 |
| `原版资源/d2text/_src/<原名>` | 其余 `data/local/**`（串表 `.tbl`） | 串表要经 `tools/d2codec/tbl.py` 再生成派生件 `原版资源/d2text/chi_string.txt`（`uicheck` 的判据源）⇒ 源码件放 `_src/`，与派生件分层 |
| 其它（不在上面三类） | 兜底进 `d2raw/` | — |

## 7. 一条已知的「清单比盘上少 2 个目标」的事（2026-09-22 G-1 片的 R3 副作用）

R3 生效后，`data\global\excel\Sounds.txt` 与 `data\global\ui\PANEL\minipanel.DC6` 不再被清单声称
（它们在本仓里**只被注释点名过**，另一来源是已撤掉的一次性手写清单）。
两只都是**真实存在**的包内条目（改前靠它们解出过，盘上仍在 `原版资源/d2raw`、`原版资源/d2dc6`），
⇒ 功能上**无变化**（解包不删任何东西），但**若以后清空 `原版资源/` 重解，这两只不会被重取**。
要恢复 = 在 `build_wanted.py` 的 `NAMED_IN_CODE` 里各加一条带出处的登记项（⛔ 别把名字硬塞回采集路径）。
