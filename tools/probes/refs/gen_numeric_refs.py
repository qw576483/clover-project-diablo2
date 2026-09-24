# -*- coding: utf-8 -*-
"""为 `策划/验收表.md` 的「数值类」判定行生成**机器可读产物**（.json）。

为什么需要它（闸门 tools/verify.ps1 第 37 项 `numeric-log-only`）：
    数值类的行原来只挂 `.ai-tmp/screenshots/w1_host_*.txt` 这种**日志/文本**产物
    ⇒ 判据只能由人读那一行，闸门判 HUMAN-ONLY。
    本脚本把该行**已经引用过的**那几行读数**机械地抽出来**（行号 + 原文 + 数字字段
    + 源文件 sha256），落成 `.json`，于是同一行有了**可机械比对**的产物。

⛔ 本脚本**不新造任何数值**：它只从该行**已引用的**产物里
   （a）按行号锚点 `第 N 行` / `第 N~M 行`、（b）按该行引用的 `反引号` 片段 选行，
   逐字抄出原文，并把原文里的数字抽成数组。
⛔ 不进 Play、不读原版资源（在本机结构性缺席）。

"锚点归属到哪个文件"用的是**就近原则**：某个 `第 N 行` / 反引号片段归给它在单元格里
**最近的前一个** 被引用产物名 —— 否则会把 A 文件的锚点错按到 B 文件上。

用法：
    python tools/probes/refs/gen_numeric_refs.py            # 写 tools/probes/refs/numeric_refs/
    python tools/probes/refs/gen_numeric_refs.py --check    # 只校验产物与源同步（不写盘）

产物：
    tools/probes/refs/numeric_refs/row05.json ... row46.json
    tools/probes/refs/numeric_refs/INDEX.tsv
"""

import hashlib
import io
import json
import os
import re
import sys
import datetime

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))   # <项目根>
SPEC = os.path.join(ROOT, "\u7b56\u5212", "\u9a8c\u6536\u8868.md")   # 策划/验收表.md
OUTDIR = os.path.join(HERE, "numeric_refs")

# 首批 13 个「数值类」行（2026-09-23 早先那一片）
ROWS = [5, 8, 17, 18, 19, 20, 21, 22, 24, 31, 35, 41, 46]
# 2026-09-23 gate-close2 追加：闸门第 28 项 `coverage-acceptance` 报这 4 行「cite no on-disk path」、
# 第 37 项 `numeric-log-only` 报这 4 行「cite no on-disk artifact at all」——两者同源：
# 这 4 行的证据格只写了 `.ai-tmp/test/*.txt`，既不落进第 28 项的行级四选一正
# （`.ai-tmp/screenshots/` | `tools/probes/` | `策划/` | `w1_host_*.txt`），也没有 `png/tsv/csv/json` 结尾的产物。
# 处置 = 按本文件既有机制再抽 4 份 json（**同样只从该行已引用的产物里逐字抽，不新造任何数值**），
# 然后把这 4 行的证据列改引该 json。
ROWS += [52, 53, 54, 57]

C_NUM = "\u6570\u503c\u7c7b"                       # 数值类
C_ROW = "\u7b2c"                                   # 第
C_LINE = "\u884c"                                  # 行
MAX_PER_SNIPPET = 10
MAX_CHECKS_PER_SOURCE = 60
NUMRE = re.compile(r"-?\d+(?:\.\d+)?")
CITEDRE = re.compile(r"(\.ai-tmp/screenshots/w1_host_[A-Za-z0-9_]+\.txt)")
# 2026-09-23 gate-close2：第 52/53/54/57 行的读数挂在 `.ai-tmp/test/*.txt`（离线宿主/探针的输出）。
# 老 CITEDRE 只认 `w1_host_*.txt` ⇒ 这 4 行抽不到任何 source。追加一个「本仓 `.ai-tmp/test/` 下的 .txt」模式，
# 口径与老的完全一致（**只取该行已引用、且在盘的产物**），⛔ 不新造数值、⛔ 不把不在盘的引用当 source。
CITEDRE_LOCAL = re.compile(r"(\.ai-tmp/test/[A-Za-z0-9_\-]+\.txt)")
# 2026-09-24 rows-52-56：第 52 行的枚举产物从 `.ai-tmp/test/`（一次性区 ⇒ 已被清空、悬空）
# 迁到**判据资产区** `tools/probes/refs/n3_srcdam-out.txt`（生产者 = `gen_n3_srcdam.py`）。
# 老的两个模式都不认这个落点 ⇒ 第 52 行抽不到任何 source。口径完全一致（**只取该行已引用、
# 且在盘的产物**），⛔ 不新造数值、⛔ 不把不在盘的引用当 source。
CITEDRE_REFS = re.compile(r"(tools/probes/refs/[A-Za-z0-9_\-]+\.txt)")

# `--check` 的比对口径：把每一处 `generated`（生成时刻戳）的值换成占位符 `"<ignored>"`
# 之后**逐字节**比（见 cmp_text）——时间戳本身不参与比对，所以 `--check` **不是**恒红。
# ⛔ 只影响**比对**：写盘内容 / 字段名 / 路径一律不变（`generated` 照旧写进产物）。
#
# 真实语义与使用口径（2026-09-24 按代码校对；此处原先写着「连它一起比 ⇒ 两次运行之间恒报
# STALE」，与实现不符，已改正）：
#   * 逐字节比的是「**除 `generated` 外**」的全部内容；
#   * 而 `spec_sha256` / `spec_md_line` 取自**整份 `策划/验收表.md`** ⇒ **表一被编辑，
#     17 份 artifact 会同时判 STALE**（那是「表变了」，不是「产物内容错了」）；
#   * ⇒ **正确用法**：**先把表改完并定稿，最后才重生成 numeric_refs**，然后在提交前跑一次
#     `--check`，应当 **0 STALE**；若表还会再改，别把 `--check` 当必绿闸门用。
GENRE = re.compile(r'"generated":\s*"[^"]*"')


def cmp_text(text):
    """比对用文本：只把 `generated` 的值换成占位符，其余**逐字节**保留。"""
    return GENRE.sub('"generated": "<ignored>"', text)


def read_text_any(path):
    """按 BOM 判编码读文本，返回 (text, encoding_name)。

    2026-09-23 gate-close2：本工程的历史产物编码并不统一（实测：`.ai-tmp/test/O-itemcheck-run2.txt`
    以 `FF FE` 开头 = UTF-16LE；另一些是 UTF-8 无 BOM / 带 BOM）。老版本一律按 `utf-8-sig` 读，
    撞上 UTF-16LE 直接 `UnicodeDecodeError`。这里只按 BOM 选解码器，**解码后文本逐字保留**
    （不替换字符、不丢行），并把所用编码如实写进产物。
    """
    with open(path, "rb") as f:
        raw = f.read()
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        return raw.decode("utf-16"), "utf-16"
    if raw[:3] == b"\xef\xbb\xbf":
        return raw.decode("utf-8-sig"), "utf-8-sig"
    try:
        return raw.decode("utf-8"), "utf-8"
    except UnicodeDecodeError:
        # 兜底：非预期分支必须留痕（不静默丢字节）。用 replace 保证行数/行号仍可机械复算。
        return raw.decode("utf-8", "replace"), "utf-8-replace"


def read_text(path):
    return read_text_any(path)[0]


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def mtime_str(path):
    return datetime.datetime.fromtimestamp(os.path.getmtime(path)).strftime("%Y-%m-%d %H:%M:%S")


def spec_rows():
    """策划/验收表.md 的编号判定行 -> {行号: (md 行号, 单元格数组)}。"""
    txt = read_text(SPEC)
    out = {}
    for i, line in enumerate(txt.split("\n"), start=1):
        s = line.strip()
        m = re.match(r"^\|\s*(\d+)\s*\|", s)
        if not m:
            continue
        out[int(m.group(1))] = (i, s.rstrip("|").split("|"))
    return out


def owner_of(cell, pos, cited, default):
    """就近原则：pos 之前最近的一个被引用产物名。"""
    best = default
    bestat = -1
    for rel in cited:
        at = cell.rfind(rel, 0, pos)
        if at > bestat:
            bestat = at
            best = rel
    return best


def scoped_anchors(cell, cited):
    """{rel: set(line)}；归属不明的锚点记到 `unscoped`。"""
    out = {r: set() for r in cited}
    unscoped = []
    for m in re.finditer(C_ROW + r"\s*([\d/]+)\s*" + C_LINE, cell):
        nums = [int(t) for t in m.group(1).split("/") if t.isdigit()]
        own = owner_of(cell, m.start(), cited, None)
        for n in nums:
            if own is None:
                unscoped.append(n)
            else:
                out[own].add(n)
    for m in re.finditer(C_ROW + r"\s*(\d+)\s*[~\-\u2014]\s*(\d+)\s*" + C_LINE, cell):
        a, b = int(m.group(1)), int(m.group(2))
        if 0 < a <= b and (b - a) < 200:
            own = owner_of(cell, m.start(), cited, None)
            for n in range(a, b + 1):
                if own is None:
                    unscoped.append(n)
                else:
                    out[own].add(n)
    return out, sorted(set(unscoped))


def scoped_snippets(cell, cited):
    """{rel: [片段]}（就近原则）；`同一个片段` 只在它归属的那个文件上找。"""
    out = {r: [] for r in cited}
    for m in re.finditer(r"`([^`]{4,160})`", cell):
        s = m.group(1).strip()
        for cut in ("\u2026", "..."):
            if cut in s:
                s = s.split(cut)[0]
        for cut in ("\uff08", "  ("):
            if cut in s:
                s = s.split(cut)[0]
        s = s.strip().strip("`").strip()
        # 纯路径类反引号不是"读数片段"，丢掉（它们只会把人引到另一个文件）
        if len(s) < 5 or s.startswith(("tools/", ".ai-tmp", "client/", "\u7b56\u5212", ".")):
            continue
        own = owner_of(cell, m.start(), cited, None)
        for rel in (cited if own is None else [own]):
            if s not in out[rel]:
                out[rel].append(s)
    return out


def select_lines(lines, anchors, snippets):
    picked = {}
    matched = 0
    for n in sorted(anchors):
        if 1 <= n <= len(lines):
            picked[n] = "anchor"
    for s in snippets:
        cnt = 0
        for i, ln in enumerate(lines, start=1):
            if s in ln:
                cnt += 1
                matched += 1
                if i not in picked:
                    picked[i] = "snippet"
                if cnt >= MAX_PER_SNIPPET:
                    break
        if cnt == 0:
            # 片段可能跨了行（或省略号截断处不在同一行）⇒ 退回按前 24 字找
            head = s[:24]
            if len(head) >= 8:
                for i, ln in enumerate(lines, start=1):
                    if head in ln:
                        matched += 1
                        if i not in picked:
                            picked[i] = "snippet-head"
                        break
    if not picked:
        for i, ln in enumerate(lines, start=1):
            if "[ OK ]" in ln or "[FAIL" in ln:
                picked[i] = "fallback-verdict-line"
                if len(picked) >= 20:
                    break
    # 每个源文件的"头/尾结论行"总是带上：宿主脚本的收尾结论（`==== 结束：全部通过 ====`）是
    # 一条与语言无关、可机械比对的判据行（mapcheck 这类宿主不用 `[ OK ]` 标记）。
    tail = 0
    for i in range(len(lines), 0, -1):
        if lines[i - 1].strip():
            tail = i
            break
    for idx, tag in ((1, "file-summary-head"), (tail, "file-summary-tail")):
        if idx >= 1 and idx not in picked and lines[idx - 1].strip():
            picked[idx] = tag

    # 2026-09-23 gate-close2：该行**既没写 `第 N 行` 锚点、也没写反引号片段**时（实测第 52 行就是这样），
    # 上面只会落 head / tail 两条 —— 产物虽然"在盘"，却几乎没夹带读数。这里退化为
    # **逐字抽『含数字的非空行』**（⛔ 仍是"从该行已引用的产物里机械抄"，不新造任何数值），
    # 上限沿用 MAX_CHECKS_PER_SOURCE。有锚点/片段的行（首 13 个产物）**完全不受影响**。
    if matched == 0 and not anchors and not snippets:
        for i, ln in enumerate(lines, start=1):
            if i in picked or not ln.strip():
                continue
            if NUMRE.search(ln):
                picked[i] = "numeric-line"
                if len(picked) >= MAX_CHECKS_PER_SOURCE:
                    break

    total = len(picked)
    if total > MAX_CHECKS_PER_SOURCE:
        keep = sorted(picked)[:MAX_CHECKS_PER_SOURCE]
        picked = dict((k, picked[k]) for k in keep)
    return picked, matched, total


def build_row(num, mdline, cells):
    # 表头 `| # | 系统/界面 | 做法 | 验收方式 | 证据 | 结论 |`；split('|') 后
    # 0 = 空（行首竖线），1 = 行号，2 = 系统，3 = 做法，4 = 验收方式，5 = 证据，6 = 结论
    system = cells[2].strip() if len(cells) > 2 else ""
    method = cells[3].strip() if len(cells) > 3 else ""
    how = cells[4].strip() if len(cells) > 4 else ""
    evid = cells[5].strip() if len(cells) > 5 else ""
    concl = cells[6].strip() if len(cells) > 6 else ""
    if C_NUM not in concl:
        raise SystemExit("row %d is not tagged %s" % (num, C_NUM))

    cited = sorted(set(CITEDRE.findall(evid)) | set(CITEDRE_LOCAL.findall(evid))
                   | set(CITEDRE_REFS.findall(evid)))
    anchors, unscoped = scoped_anchors(evid, cited)
    snippets = scoped_snippets(evid, cited)

    sources = []
    checks = []
    for rel in cited:
        path = os.path.join(ROOT, rel.replace("/", os.sep))
        if not os.path.isfile(path):
            sources.append({"path": rel, "state": "MISSING"})
            continue
        text, enc = read_text_any(path)
        lines = text.split("\n")
        sources.append({
            "path": rel,
            "state": "on-disk",
            "encoding": enc,
            "bytes": os.path.getsize(path),
            "sha256": sha256_file(path),
            "mtime": mtime_str(path),
            "lines": len(lines),
            "ok_lines": sum(1 for x in lines if "[ OK ]" in x),
            "fail_lines": sum(1 for x in lines if "[FAIL" in x),
            "pass_marks": sum(1 for x in lines if "\u2705" in x),          # 宿主用的另一套通过标记
            "last_line": [x.strip() for x in lines[::-1] if x.strip()][:1],
        })
        picked, matched, total = select_lines(lines, anchors.get(rel, set()), snippets.get(rel, []))
        for n, how_sel in sorted(picked.items()):
            t = lines[n - 1].strip()
            checks.append({
                "source": rel,
                "line": n,
                "selected_by": how_sel,
                "text": t[:400],
                "numbers": NUMRE.findall(t),
            })
        sources[-1]["matched_lines_total"] = matched
        sources[-1]["checks_written"] = len(picked)
        sources[-1]["checks_truncated"] = (total > len(picked))

    return {
        "_comment": ("machine-readable extraction for one numeric-class acceptance row; "
                     "every value is copied verbatim from the cited on-disk evidence, never invented"),
        "row": num,
        "spec_md_line": mdline,
        "spec_sha256": sha256_file(SPEC),
        "category": C_NUM,
        "system": system,
        "method": method,
        "criterion": how,
        "conclusion": concl,
        "generator": "tools/probes/refs/gen_numeric_refs.py",
        "generated": datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "selectors": {
            "line_anchors": dict((k, sorted(v)) for k, v in anchors.items() if v),
            "unscoped_anchors": unscoped,
            "snippets": dict((k, v) for k, v in snippets.items() if v),
        },
        "sources": sources,
        "checks": checks,
    }


def main():
    rows = spec_rows()
    missing = [r for r in ROWS if r not in rows]
    if missing:
        raise SystemExit("rows not found in spec: %s" % missing)
    if not os.path.isdir(OUTDIR):
        os.makedirs(OUTDIR)
    index = []
    stale = []
    for num in ROWS:
        mdline, cells = rows[num]
        doc = build_row(num, mdline, cells)
        name = "row%02d.json" % num
        body = json.dumps(doc, ensure_ascii=False, indent=2, sort_keys=False) + "\n"
        path = os.path.join(OUTDIR, name)
        if "--check" in sys.argv:
            # 只比内容：`generated`（生成时刻戳）先剔除，否则两次运行之间恒 STALE。
            if (not os.path.isfile(path)) or cmp_text(read_text(path)) != cmp_text(body):
                print("STALE %s" % name)
                stale.append(name)
            continue
        with io.open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write(body)
        rel = "tools/probes/refs/numeric_refs/" + name
        index.append((num, rel, len(doc["checks"]), len(doc["sources"]),
                      hashlib.sha256(body.encode("utf-8")).hexdigest()[:16]))
        print("row %-3d -> %s  checks=%d sources=%d" % (num, rel, len(doc["checks"]), len(doc["sources"])))
    if "--check" in sys.argv:
        # 一行结论行：否则"全部同步"是空输出，自检拿不到可贴的原文。
        if stale:
            print("STALE %d/%d row artifact(s) out of sync: %s"
                  % (len(stale), len(ROWS), ", ".join(stale)))
        else:
            print("OK all %d row artifact(s) in sync (byte-for-byte, `generated` timestamp excluded)"
                  % len(ROWS))
    if "--check" not in sys.argv and index:
        ip = os.path.join(OUTDIR, "INDEX.tsv")
        with io.open(ip, "w", encoding="utf-8", newline="\n") as f:
            f.write("# row\tartifact\tchecks\tsources\tsha256_16\n")
            for num, rel, nc, ns, sh in index:
                f.write("%d\t%s\t%d\t%d\t%s\n" % (num, rel, nc, ns, sh))
        print("index -> tools/probes/refs/numeric_refs/INDEX.tsv (%d rows)" % len(index))
    return 0


if __name__ == "__main__":
    sys.exit(main())
