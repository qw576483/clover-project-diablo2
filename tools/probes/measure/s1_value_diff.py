# -*- coding: utf-8 -*-
"""S1 数值维比对器 —— 「官方 txt ↔ 我们的运行时表」逐字段对账（纯离线、幂等、可只跑子集）。

用法（一行）：
    python tools/probes/measure/s1_value_diff.py --src "<官方 txt 根|*.mpq>" [--rows <file>] [--probe]

判定口径（不许放宽，详见同目录 s1_common.py 的模块注释）：
  * **官方值只认官方载体**（<根>/data/global/excel/*.txt，或由 *.mpq 解出来的同一批 txt）。
  * `策划/数值文档/*_c.txt` 是**转写件**，与 `client/Assets/StreamingAssets/Table/*.tsv`
    逐格相同 ⇒ 拿它当官方值就是同义反复，**不进结论**；只用于 `--cross-transcription` 交叉校验。
  * 官方载体不在位 ⇒ 每一行都 `缺官方值`，⛔ 一个 `一致` 都不许有。
  * 期望值**不重写公式**：直接调 `tools/table-convert/convert.py` 的 build_*（列映射/公式/取整都在它里面）。
  * `差值` 为 `0` 才允许 `一致`；官方值缺失 ⇒ `缺官方值`（⛔ 不许推定、不许默认通过）。
  * 官方 txt 里**没有**对应对手的列（本项目编号 / 中文显示名 / convert.py 明示的登记占位）
    ⇒ 单列结论 = `缺官方值`，且**不计入该行结论**（它们天然比不了，不是"放过"）；
    逐列照样落盘可见，集合由 `s1_common.EXPECTED_NO_CARRIER` 钉死。

输出（默认 `<仓库根>/.ai-tmp/test/s1/`，加 `--gate-artifacts` 时同时落 `.ai-tmp/screenshots/`）：
  * `s1_value_diff.tsv`       逐 (矩阵行 × 字段) 一行：行号/实体id/官方来源文件:列/官方值/我们的值/差值/结论
  * `s1_value_diff.rows.tsv`  逐矩阵行一行（= 821 行的汇总屏）
  * `s1_value_diff.json`      机器可读汇总（含每张表计数 / 缺官方值原因）
  * `s1_value_diff.trans.tsv` （官方在位时）官方 ↔ 转写件 的漂移清单
⛔ 产物内**不含时间戳** ⇒ 同一输入再跑一次逐字节一致。
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)

import s1_common as C   # noqa: E402

VERDICT_OK = "一致"
VERDICT_BAD = "不一致"
VERDICT_NO_OFFICIAL = "缺官方值"
VERDICT_NO_OURS = "缺我们的值"

FIELDS_TSV = "s1_value_diff.tsv"
ROWS_TSV = "s1_value_diff.rows.tsv"
JSON_OUT = "s1_value_diff.json"
TRANS_TSV = "s1_value_diff.trans.tsv"

HEADER = ["行号", "实体id", "官方来源文件:列", "官方值", "我们的值", "差值", "结论"]


# ─────────────────────────────────────────────────────────────────────────────
# 矩阵行 → (表, 主键)
# ─────────────────────────────────────────────────────────────────────────────
def split_state(state):
    """`id=1` → ('id', '1')；`id=Potion 1` → ('id', 'Potion 1')。"""
    if "=" in state:
        k, v = state.split("=", 1)
        return k.strip(), v.strip()
    return "id", state.strip()


def load_rows_filter(path):
    """子集文件：每行一个 token —— 矩阵行号 / `Affix:id=1` / `tbl:Affix(301 id)|id=1`。"""
    if not path:
        return None
    with open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        toks = [l.strip() for l in f if l.strip() and not l.startswith("#")]
    lines, keys = set(), set()
    for t in toks:
        if t.isdigit():
            lines.add(int(t))
            continue
        if "|" in t:
            t = t.split("|", 1)[1]
        if ":" in t:
            lg, st = t.split(":", 1)
            lg = lg.replace("tbl:", "").split("(")[0].strip().lower()
            keys.add((lg, split_state(st)[1]))
    return lines, keys


# ─────────────────────────────────────────────────────────────────────────────
# 核心对账
# ─────────────────────────────────────────────────────────────────────────────
def run(src, rows_filter, out_dirs, probe_only=False, cross=True):
    root = C.find_repo_root(HERE)
    rows = C.s1_empty_rows(root)
    if not rows:
        raise SystemExit("[fatal] 状态矩阵里没有 S1数值 的空行 —— 先确认 策划/状态矩阵.tsv 在位")

    srcinfo = C.resolve_official_src(src, root)
    excel = srcinfo["excel_dir"]

    # ---- 官方载体定位（三种情况都明说，不静默降级）-------------------------
    present, missing_files = ([], C.ALL_OFFICIAL_FILES)
    if excel:
        present, missing_files = C.probe_official_dir(excel)

    if probe_only:
        print("[probe] 官方 txt 点名：%s" % (excel or "(无目录)"))
        print("  在位 %d / %d" % (len(present), len(C.ALL_OFFICIAL_FILES)))
        for f in present:
            print("    [OK]   %s" % f)
        for f in missing_files:
            print("    [MISS] %s" % f)
        return 0 if not missing_files else 1

    if srcinfo["kind"] == "mpq":
        print("\n".join(C.MPQ_HELP_LINES))
        print("")
        print("[BLOCKED] 官方载体 = %s（未解包）⇒ 821 行全部 `缺官方值`" % srcinfo["raw"])
    elif srcinfo["kind"] == "missing":
        print("[BLOCKED] %s" % srcinfo["msg"])
        print("          官方载体不在位 ⇒ 821 行全部 `缺官方值`")
        print("          缺什么 / 放哪 / 到手后跑什么 ⇒ .ai-tmp/test/s1/UNBLOCK.md")
    else:
        print("[src] 官方 txt 目录：%s  （在位 %d/%d）"
              % (excel, len(present), len(C.ALL_OFFICIAL_FILES)))
        if missing_files:
            print("[warn] 还缺 %d 个官方 txt：%s" % (len(missing_files), ", ".join(missing_files)))

    # 输入③：转写件（只作交叉校验；官方不在位时**不进结论**）
    trans_dir = os.path.join(root, C.DOC_DIR_REL)
    trans_tables = [lg for lg, _, _ in C.TABLES
                    if os.path.isfile(os.path.join(trans_dir, lg + "_c.txt"))]
    print("[trans] 输入③ 转写件在位 %d/10 张 —— 与运行时表逐格相同，**不是**官方载体 ⇒ 不进结论"
          % len(trans_tables))

    # ---- 载入期望值（官方在位时）------------------------------------------
    mod = None
    expected = {}          # logical -> (cols, {key: row}) / None
    table_err = {}         # logical -> 原因（缺官方值的原因）
    if excel:
        try:
            mod = C.load_converter(root)
        except C.ConverterError as e:
            mod = None
            for lg, _, _ in C.TABLES:
                table_err[lg] = str(e)
        if mod is not None:
            for lg, _, _ in C.TABLES:
                cols, erows, err = C.expected_for_table(mod, lg, excel)
                if err:
                    table_err[lg] = err
                    expected[lg] = None
                    continue
                pk = cols[0]
                expected[lg] = ({r[0]: r for r in erows}, cols, pk)
    else:
        for lg, _, _ in C.TABLES:
            table_err[lg] = "官方载体不在位"

    # ---- 读出我们的值 ------------------------------------------------------
    ours = {}
    for lg, rt, _ in C.TABLES:
        hdr, drows, _ = C.read_runtime_table(root, rt)
        ours[lg] = ({r[0]: r for r in drows}, hdr)

    # ---- 逐矩阵行逐字段对账 ------------------------------------------------
    field_lines = []
    row_lines = []
    per_table = {}
    per_verdict = {VERDICT_OK: 0, VERDICT_BAD: 0, VERDICT_NO_OFFICIAL: 0, VERDICT_NO_OURS: 0}
    per_field_verdict = dict(per_verdict)

    for r in rows:
        if rows_filter:
            lines, keys = rows_filter
            if r["line"] not in lines and (C.logical_of_matrix_entity(r["entity"]),
                                           split_state(r["state"])[1]) not in keys:
                continue
        lg = C.logical_of_matrix_entity(r["entity"])
        if lg is None:
            raise SystemExit("[fatal] 状态矩阵里有本比对器不认识的 S1 实体：%s" % r["entity"])
        key = split_state(r["state"])[1]
        eid = "%s|%s" % (r["entity"], r["state"])
        ohdr = ours[lg][1]
        orow = ours[lg][0].get(key)
        exp = expected.get(lg)

        row_verdict = None
        worst = None       # (label, expval, ourval, diff)
        stats = per_table.setdefault(lg, {
            "rows": 0, "fields": 0, VERDICT_OK: 0, VERDICT_BAD: 0,
            VERDICT_NO_OFFICIAL: 0, VERDICT_NO_OURS: 0,
            "reason": table_err.get(lg, "")})

        for ci, (col, kind, label, _crit) in enumerate(C.FIELD_MAP[lg]):
            if ci >= len(ohdr) or ohdr[ci] != col:
                ourval = ""
                ocol_ok = False
            else:
                ocol_ok = True
                ourval = orow[ci] if orow is not None else ""
                if orow is None:
                    ourval = ""

            if not ocol_ok or orow is None:
                v, ev, df = VERDICT_NO_OURS, "", ""
                if orow is None:
                    label = "%s（运行时表 %s.tsv 无主键 %r）" % (label, C.entity_table(lg), key)
            elif kind == "none":
                v, ev, df = VERDICT_NO_OFFICIAL, "(官方 txt 无此列)", "无"
            elif exp is None:
                v, ev, df = VERDICT_NO_OFFICIAL, "", ""
            else:
                ecols = exp[1]
                if col not in ecols:
                    v, ev, df = VERDICT_NO_OFFICIAL, "(转换器未产出该列)", ""
                else:
                    eval_ = exp[0].get(key)
                    if eval_ is None:
                        v, ev, df = VERDICT_NO_OFFICIAL, "(官方无该主键行)", ""
                    else:
                        ev = eval_[ecols.index(col)]
                        df = C.fmt_diff(ourval, ev)
                        v = VERDICT_OK if df == "0" else VERDICT_BAD

            field_lines.append([str(r["line"]), eid, label, ev, ourval, df, v])
            per_field_verdict[v] = per_field_verdict.get(v, 0) + 1
            stats["fields"] += 1

            # 行结论：只看"比得了"的列（kind != none）
            if kind != "none":
                if v == VERDICT_BAD:
                    row_verdict = VERDICT_BAD if row_verdict != VERDICT_BAD else row_verdict
                    if worst is None:
                        worst = (label, ev, ourval, df)
                elif v == VERDICT_NO_OURS:
                    row_verdict = VERDICT_NO_OURS if row_verdict not in (VERDICT_BAD,) else row_verdict
                elif v == VERDICT_NO_OFFICIAL:
                    if row_verdict is None:
                        row_verdict = VERDICT_NO_OFFICIAL
                elif v == VERDICT_OK and row_verdict is None:
                    row_verdict = VERDICT_OK

        if row_verdict is None:
            row_verdict = VERDICT_NO_OFFICIAL       # 该表全部列都无官方对手
        if worst is None:
            row_lines.append([str(r["line"]), eid, "*（全部比得了的列）", "", "", "0"
                              if row_verdict == VERDICT_OK else "", row_verdict])
        else:
            row_lines.append([str(r["line"]), eid, worst[0], worst[1], worst[2], worst[3], row_verdict])

        per_verdict[row_verdict] = per_verdict.get(row_verdict, 0) + 1
        stats["rows"] += 1
        stats[row_verdict] += 1

    # ---- 落盘（无时间戳 ⇒ 逐字节可复跑）-----------------------------------
    written = []
    for d in out_dirs:
        if not os.path.isdir(d):
            os.makedirs(d)
        _dump_tsv(os.path.join(d, FIELDS_TSV), HEADER, field_lines)
        _dump_tsv(os.path.join(d, ROWS_TSV), HEADER, row_lines)
        written.append(os.path.join(d, FIELDS_TSV))
        written.append(os.path.join(d, ROWS_TSV))

    summary = {
        "official_source": {"kind": srcinfo["kind"], "raw": srcinfo["raw"],
                            "excel_dir": excel,
                            "present_files": sorted(present),
                            "missing_files": sorted(missing_files)},
        "transcription": {"dir": trans_dir.replace("\\", "/"),
                          "tables": sorted(trans_tables),
                          "used_in_verdict": False},
        "matrix_rows_total": len(C.s1_rows(root)),
        "matrix_rows_judged": len(row_lines),
        "row_verdicts": per_verdict,
        "field_verdicts": per_field_verdict,
        "per_table": per_table,
        "no_official_carrier_fields": {lg: sorted(C.no_official_carrier_fields(lg))
                                       for lg, _, _ in C.TABLES
                                       if C.no_official_carrier_fields(lg)},
        "note": "官方载体不在位 ⇒ 行结论一律 缺官方值；转写件不进结论（与运行时表逐格相同，同义反复）",
    }
    for d in out_dirs:
        with open(os.path.join(d, JSON_OUT), "w", encoding="utf-8", newline="\n") as f:
            json.dump(summary, f, ensure_ascii=False, indent=2, sort_keys=True)
            f.write("\n")
        written.append(os.path.join(d, JSON_OUT))

    # ---- 交叉校验：官方 ↔ 转写件（只在官方在位时有意义）-------------------
    if cross and mod is not None:
        trans_lines = cross_transcription(root, mod, excel, expected)
        per_delta = {}
        for d in out_dirs:
            _dump_tsv(os.path.join(d, TRANS_TSV),
                      ["表", "主键", "列", "官方值", "转写值", "差值"], trans_lines)
            written.append(os.path.join(d, TRANS_TSV))
        per_delta = {}
        for t in trans_lines:
            per_delta[t[0]] = per_delta.get(t[0], 0) + 1
        summary["transcription_drift"] = {"rows": len(trans_lines), "per_table": per_delta}
        for d in out_dirs:
            with open(os.path.join(d, JSON_OUT), "w", encoding="utf-8", newline="\n") as f:
                json.dump(summary, f, ensure_ascii=False, indent=2, sort_keys=True)
                f.write("\n")
        print("[cross] 官方 ↔ 转写件 漂移 %d 处%s"
              % (len(trans_lines), (": " + str(per_delta)) if per_delta else "（0 处 ⇒ 转写件忠实）"))

    # ---- 屏上汇总 ----------------------------------------------------------
    print("")
    print("===== s1_value_diff =====")
    print("  判定行数（状态矩阵 S1 空行）= %d" % len(row_lines))
    print("  行结论：一致=%d 不一致=%d 缺官方值=%d 缺我们的值=%d"
          % (per_verdict[VERDICT_OK], per_verdict[VERDICT_BAD],
             per_verdict[VERDICT_NO_OFFICIAL], per_verdict[VERDICT_NO_OURS]))
    print("  字段结论：一致=%d 不一致=%d 缺官方值=%d 缺我们的值=%d"
          % (per_field_verdict[VERDICT_OK], per_field_verdict[VERDICT_BAD],
             per_field_verdict[VERDICT_NO_OFFICIAL], per_field_verdict[VERDICT_NO_OURS]))
    for lg, _, _ in C.TABLES:
        s = per_table.get(lg)
        if s is None:                                   # 本表无被判定行（--rows 子集没选中）
            print("    %-14s （本次未判定该表）%s" % (lg, table_err.get(lg, "")))
            continue
        print("    %-14s rows=%-4d fields=%-5d 一致=%-4d 不一致=%-3d 缺官方值=%-4d%s"
              % (lg, s["rows"], s["fields"], s[VERDICT_OK], s[VERDICT_BAD],
                 s[VERDICT_NO_OFFICIAL],
                 ("  原因=" + s["reason"]) if s["reason"] else ""))
    print("  产物：")
    for w in written:
        print("    %s" % w.replace("\\", "/"))
    # 退出码三分（⛔ "没比过" 不算通过）：
    #   0 = 比过了，且 0 个 `不一致`；1 = 有 `不一致`；2 = 官方载体不在位/未解包（BLOCKED，根本没比）
    if srcinfo["kind"] != "txt" or (excel and len(present) == 0):
        print("  [exit=2] 官方载体不在位 ⇒ 本次**没有比过**任何一行（全部 `缺官方值`）")
        return 2
    return 0 if per_verdict[VERDICT_BAD] == 0 else 1


def _dump_tsv(path, header, rows):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(header) + "\n")
        for r in rows:
            cells = [str(x).replace("\t", " ").replace("\n", " ") for x in r]
            f.write("\t".join(cells) + "\n")


def cross_transcription(root, mod, excel, expected):
    """官方（convert 结果）↔ `策划/数值文档/<lg>_c.txt`（转写件）逐格对账。"""
    out = []
    for lg, _, _ in C.TABLES:
        exp = expected.get(lg)
        if exp is None:
            continue
        cpath = os.path.join(root, C.DOC_DIR_REL, lg + "_c.txt")
        if not os.path.isfile(cpath):
            continue
        _chdr, crows, _ = C.read_source_table(root, lg)
        cmap = {r[0]: r for r in crows}
        ecols = exp[1]
        for key, erow in sorted(exp[0].items()):
            crow = cmap.get(key)
            if crow is None:
                out.append([lg, key, "*", "(转写件无该行)", "", ""])
                continue
            for ci, col in enumerate(ecols):
                tv = crow[ci] if ci < len(crow) else ""
                if erow[ci] != tv:
                    out.append([lg, key, col, erow[ci], tv, C.fmt_diff(tv, erow[ci])])
    return out


def main():
    ap = argparse.ArgumentParser(description="S1 数值维：官方 txt ↔ 运行时表 逐字段对账（纯离线）")
    ap.add_argument("--src", default=os.environ.get("D2SRC_DIR", ""),
                    help="官方 1.10f txt 根（含 data/global/excel/*.txt；直接给 excel 目录也行）"
                         "，或 *.mpq。默认取环境变量 D2SRC_DIR")
    ap.add_argument("--rows", default="", metavar="FILE",
                    help="只跑子集：每行一个 token（矩阵行号 / `Affix:id=1`）")
    ap.add_argument("--outdir", default="", help="产物目录（默认 <仓库根>/.ai-tmp/test/s1）")
    ap.add_argument("--gate-artifacts", action="store_true",
                    help="同时把机器可读产物落 <仓库根>/.ai-tmp/screenshots/（闸门 numeric-log-only 认该目录）")
    ap.add_argument("--probe", action="store_true", help="只点名官方 txt（不比对）")
    ap.add_argument("--no-cross", action="store_true", help="不做 官方↔转写件 交叉校验")
    args = ap.parse_args()

    root = C.find_repo_root(HERE)
    outs = [args.outdir or os.path.join(root, ".ai-tmp", "test", "s1")]
    if args.gate_artifacts:
        outs.append(os.path.join(root, ".ai-tmp", "screenshots"))
    return run(args.src, load_rows_filter(args.rows), outs,
               probe_only=args.probe, cross=not args.no_cross)


if __name__ == "__main__":
    sys.exit(main())
