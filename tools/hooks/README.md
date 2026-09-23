# tools/hooks — 提交前强制闸门（pre-commit gate）

> 判据资产，**必须提交**（删了它，闸门就退回“靠人想起来才跑”）。

## 1. 为什么存在（SKILL.md §0.5 元规则）

> “必须始终成立”的事（不许编数值 / 交付必过闸门 / 编译失败必停 / 外观必对照）
> ⇒ **同时**落 ① `tools/verify.ps1` 检查项 ② **强制层**（git hook / CI / 打包入口）③ 会话起始必读卡。

本目录就是第 ② 条：**提示词是请求，闸门才是保证**。
在它之前，`tools/verify.ps1` 只在“人主动想起来”时才跑 —— 那不是闸门，是提醒。
装上之后，**这条仓库里每一次 `git commit` 都会先跑 `tools/verify.ps1`**，有 FAIL 就提交不了。

## 2. 怎么装（一条命令）

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/hooks/install.ps1
```

它只做两件事（⛔ 不动别的 git config）：

1. `git config core.hooksPath .githooks` —— 把本仓库的钩子目录指到 `.githooks/`（**本地配置**，相对路径，随仓库可移植）；
2. 若 `.githooks/pre-commit` **已被跟踪**，顺手 `git update-index --chmod=+x` 记录可执行位；未跟踪则**不碰索引**（第一次提交请用 `git add --chmod=+x .githooks/pre-commit`）。

确认装好：

```powershell
git config --get core.hooksPath     # 期望输出： .githooks
```

## 3. 钩子做什么（`.githooks/pre-commit`）

| 情形 | 行为 |
|---|---|
| `tools/verify.ps1` 全 PASS | 放行，打印 `tools/verify.ps1 PASS -> commit allowed.` |
| 有 `FAIL`（verify 退出码非 0） | **阻止提交**，并把每条 `FAIL <项名>` 原样打印出来 |
| `HUMAN-ONLY` / 已登记的允许差异 | **不拦**（`verify.ps1` 自己只对真 FAIL 返回非 0，钩子不额外加码） |
| 环境里没有 `powershell` / `pwsh` | 阻止提交并说明（宁可挡住，也不静默跳过） |
| 找不到 `tools/verify.ps1` | 阻止提交（闸门本体缺失也是失败） |

钩子是 **POSIX sh**（Git for Windows 自带 `sh` 来跑钩子）：**纯 ASCII / LF 行尾 / 无 BOM**，内部显式调
`powershell -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1`。

## 4. 怎么临时绕过（逃生口）

```sh
SKIP_VERIFY_GATE=1 git commit -m "..."
```

⚠️ **逃生口只给紧急场合**（例如 `verify.ps1` 自身瘫痪、而又必须立刻落一次提交止损）。
**用一次就必须在 `.ai-tmp/test/dispatch-log.tsv` 追加一行 `# bypass:`**，写清 `谁 / 何时 / 为什么`。
不记 = 违规：闸门的全部意义就是“没人能悄悄跳过它”。

## 5. 为什么不放在 `.git/hooks/`

`.git/` 不进版本库 ⇒ 放那儿的钩子**别人 clone 下来没有**。放 `.githooks/`（进仓库）+ `core.hooksPath` 指过去，
才是**可提交、可传播**的强制层。

## 6. 行尾 / 可执行位约定

- `.githooks/pre-commit`：**纯 ASCII、LF、无 BOM**（`.sh` 由 sh 解释，Windows 下 CRLF 会破坏 shebang 解析）。
  校验命令（本仓实测通过，`CR=0 / LF=74 / BOM=False / non-ASCII=0`）：

  ```powershell
  $b=[IO.File]::ReadAllBytes("$PWD/.githooks/pre-commit")
  "CR=$(@($b|?{$_ -eq 13}).Count) LF=$(@($b|?{$_ -eq 10}).Count) BOM=$($b[0] -eq 0xEF -and $b[1] -eq 0xBB) nonASCII=$(@($b|?{$_ -gt 127}).Count)"
  ```

- 可执行位：Unix 上需要 `chmod +x`（或 `git add --chmod=+x .githooks/pre-commit`）。
  本机 checkout 的 `core.filemode=false` ⇒ **Git for Windows 不依赖该位也会调用钩子**（已实测，见下）。

## 7. 新会话必读（SKILL.md §0.5 ③）

任何新会话（人或 AI）开工前，按序读这几处，**别靠记忆**：

| 顺序 | 路径 | 是什么 |
|---|---|---|
| 1 | `tools/verify.ps1` | **唯一闸门**：交付前必跑；有 FAIL 不许说“完成”。一次跑全 8 条机械自检 + 本项目自有闸门 |
| 2 | `tools/env-check.ps1` | 环境自检（杀软 / 图形设备 / 崩溃风暴）。⚠️ **本项目 `tools/` 下没有这个脚本** —— 本机实测确认缺失；需要时从 skill 的 `scripts/env-check.ps1` 取来放 `tools/`（skill 没同步落盘是本项目的缺口，不要以为它在这儿） |
| 3 | `策划/实体清单.tsv` · `策划/状态矩阵.tsv` · `策划/差异登记.tsv` | **T0 三表**（穷举口径：实体清单行数 == 验收表判定行数，零空行 / 零 `不一致`；差异逐条登记） |
| 4 | `策划/验收表.md` | 验收判定表：每格填满 + 每行标 `数值类`/`表现类` |
| 5 | `策划/对照表.md` | 1:1 六维对照（原版值 / 我们的值 / 差值=0）；**若已产出**则必读 |
| 6 | `.ai-tmp/test/dispatch-log.tsv` | 派活台账（谁派给谁、改了哪些文件、`# direct-fix:` / `# bypass:` / `# takeover:` 记录） |

> 其余按需：`策划/策划案/{A}参考规格.md` 顶部「形态」（含**朝向**判定，横/竖 + 画布参考分辨率）是每次改 UI 前必看的一处。
