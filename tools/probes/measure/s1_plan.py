# -*- coding: utf-8 -*-
"""生成 `tools/probes/measure/s1_plan.tsv` —— S1 数值维 821 个空行的**逐行比对计划**。

输入：`策划/状态矩阵.tsv` 里 S1数值 维**全部空行**（`实测` 为空的那些行）。
输出：`行号 <TAB> 实体id <TAB> 需要哪个官方表的哪一列/公式 <TAB> 我们这边的来源(文件:行 或 "空") <TAB> 判据`

⛔ 本文件只写"要比什么、官方那一列在哪、判据是什么"，**不写任何推定值**：
   数值一律等官方载体到位后由 `s1_value_diff.py` 现场算（它直接调 convert.py 的 build_*）。

用法：
    python tools/probes/measure/s1_plan.py            # 重新生成 s1_plan.tsv（幂等）
    python tools/probes/measure/s1_plan.py --print    # 只打印不落盘
"""
import argparse
import io
import os
import re
import sys
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import s1_common as C   # noqa: E402

OUT = os.path.join(HERE, "s1_plan.tsv")
HEADER = ["行号", "实体id", "需要哪个官方表的哪一列/公式",
          "我们这边的来源(文件:行 或 \"空\")", "判据"]
SRC_RE = re.compile(u"（出处：(.+?)）")


def official_need(logical):
    """该表要判的官方列/公式清单（去重、保序）；无官方对手的列不出现在这里（另见 UNBLOCK ④）。"""
    seen, out = set(), []
    for col, kind, label, _crit in C.FIELD_MAP[logical]:
        if kind == "none":
            continue
        if label not in seen:
            seen.add(label)
            out.append(label)
    return out


def criteria(logical):
    kinds = {k for _, k, _, _ in C.FIELD_MAP[logical] if k != "none"}
    return u"公式求值相等" if "formula" in kinds else u"数值相等"


def build(root=None, want_rows=None):
    root = root or C.find_repo_root(HERE)
    rows = C.s1_empty_rows(root)
    if want_rows is not None and len(rows) != want_rows:
        raise SystemExit("[fatal] S1 空行数 %d != 期望 %d —— 状态矩阵变了？先查为什么"
                         % (len(rows), want_rows))

    out = []
    per_table = collections.Counter()
    per_crit = collections.Counter()
    no_src = 0
    for r in rows:
        lg = C.logical_of_matrix_entity(r["entity"])
        if lg is None:
            raise SystemExit("[fatal] 不认识的 S1 实体：%s" % r["entity"])
        eid = "%s|%s" % (r["entity"], r["state"])
        m = SRC_RE.search(r["expect"])
        src = m.group(1).strip() if m else u"空"
        if src == u"空":
            no_src += 1
        crit = criteria(lg)
        out.append([str(r["line"]), eid, "; ".join(official_need(lg)), src, crit])
        per_table[lg] += 1
        per_crit[crit] += 1
    return out, per_table, per_crit, no_src


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--print", action="store_true", dest="do_print")
    ap.add_argument("--expect-rows", type=int, default=821,
                    help="期望的空行数（默认 821 = 闸门 coverage-filled 报的那个数）")
    args = ap.parse_args()

    rows, per_table, per_crit, no_src = build(want_rows=args.expect_rows)
    print("行数 = %d（期望 %d）%s" % (len(rows), args.expect_rows,
                                     "OK" if len(rows) == args.expect_rows else "**不符**"))
    print("按表分布：")
    for lg, n in per_table.most_common():
        print("    %-14s %d" % (lg, n))
    print("按判据分布：%s" % dict(per_crit))
    print("来源取不到（写成 空）的行数 = %d" % no_src)

    if args.do_print:
        for r in rows[:5]:
            print(" | ".join(r[:2] + [r[3], r[4]]))
        return 0

    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(HEADER) + "\n")
        for r in rows:
            f.write("\t".join(str(x).replace("\t", " ") for x in r) + "\n")
    print("写出 %s" % OUT.replace("\\", "/"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
