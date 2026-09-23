# -*- coding: utf-8 -*-
"""`s1_value_diff.py` 的两次自检（§8.3 硬要求）—— 离线、秒级、可重复跑。

① 已知正确样本 ⇒ 必须判 `一致`
   夹具 = 手造一份**官方 txt**（`charstats.txt`），其值由本项目运行时表 Class.tsv 反推
   （6 个「官方注释为 in fourths」的列 ×4 还原），且只喂 `class` 那 5 行。
   它同时证伪两件事：比对器不是"永远报红"，且 convert.py 的列映射/÷4 公式被端到端走通。
② 注入缺陷 ⇒ 必须判 `不一致` 且**指出是哪一列**
   同一份夹具改 1 位（Amazon 的 LifePerLevel 8 → 12）⇒ 期望 life_per_lvl 由 2 变 3，
   与我们的 2 差 -1 ⇒ 行汇总第 3 列必须是 `charstats.txt:LifePerLevel ÷4…`。
③ （附加）反向注入 8 → 4 ⇒ 差值应为 +1，证明"差值"带符号、不是恒等式。

用法：
    python tools/probes/measure/s1_selftest.py
退出码 0 = 三件全过；非 0 = 有不过（原文会打在屏上）。
"""
import io
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import s1_common as C   # noqa: E402

DIFF = os.path.join(HERE, "s1_value_diff.py")
# charstats.txt 的 ÷4 列（官方注释 "The following are in fourths"）
QUARTER = [("LifePerLevel", "life_per_lvl"), ("ManaPerLevel", "mana_per_lvl"),
           ("StaminaPerLevel", "stam_per_lvl"), ("LifePerVitality", "life_per_vit"),
           ("ManaPerMagic", "mana_per_mag"), ("StaminaPerVitality", "stam_per_vit")]
DIRECT = [("str", "str"), ("dex", "dex"), ("vit", "vit"), ("int", "eng"),
          ("StatPerLevel", "stat_per_lvl"), ("ToHitFactor", "to_hit_factor"),
          ("WalkVelocity", "walk_velocity"), ("RunVelocity", "run_velocity"),
          ("StartSkill", "start_skill")]


def fail(msg):
    print("[FAIL] %s" % msg)
    return 1


def make_fixture(root, outdir, tweak=None):
    """把运行时 Class.tsv 反推成一份官方 charstats.txt（列名/取值口径照 convert.py:315-353）。"""
    if not os.path.isdir(outdir):
        os.makedirs(outdir)
    hdr, rows, _ = C.read_runtime_table(root, "Class")
    idx = {c: i for i, c in enumerate(hdr)}
    cols = ["class"] + [o for o, _ in DIRECT] + [o for o, _ in QUARTER]
    lines = ["\t".join(cols)]
    for r in rows:
        rec = {"class": r[idx["code"]]}
        for off, rt in DIRECT:
            rec[off] = r[idx[rt]]
        for off, rt in QUARTER:
            v = float(r[idx[rt]]) * 4
            rec[off] = str(int(v)) if v == int(v) else ("%g" % v)
        if tweak and rec["class"] == tweak[0]:
            rec[tweak[1]] = tweak[2]
        lines.append("\t".join(rec[c] for c in cols))
    p = os.path.join(outdir, "charstats.txt")
    with io.open(p, "w", encoding="cp1252", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    return p, cols, len(rows)


def run_diff(root, src, outdir, rows_file):
    cmd = [sys.executable, DIFF, "--src", src, "--rows", rows_file, "--outdir", outdir]
    pr = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace",
                        cwd=root, timeout=600)
    return pr


def show(pr, title):
    print("---- %s :: 比对器 stdout 原文 ----" % title)
    print((pr.stdout or "").rstrip())
    if pr.stderr and pr.stderr.strip():
        print("---- %s :: stderr ----" % title)
        print(pr.stderr.rstrip())
    print("---- %s :: exit=%d ----" % (title, pr.returncode))


def tsv_rows(path):
    with io.open(path, "r", encoding="utf-8") as f:
        lines = [l.rstrip("\n") for l in f if l.strip()]
    return [l.split("\t") for l in lines[1:]]


def main():
    rc = 0
    root = C.find_repo_root(HERE)
    base = os.path.join(root, ".ai-tmp", "test", "s1", "selftest")
    print("仓库根 = %s" % root.replace("\\", "/"))
    print("比对器 = %s" % DIFF.replace("\\", "/"))

    # ── 守卫 1：无官方载体列集合必须与登记完全一致（防"悄悄放宽判据"）──────────
    print("\n===== [guard-1] 无官方载体列集合 vs 登记 =====")
    bad = []
    for lg, _, _ in C.TABLES:
        got = set(C.no_official_carrier_fields(lg))
        want = C.EXPECTED_NO_CARRIER.get(lg, set())
        if got != want:
            bad.append("%s: got=%s want=%s" % (lg, sorted(got), sorted(want)))
    if bad:
        rc |= fail("; ".join(bad))
    else:
        tot = sum(len(C.EXPECTED_NO_CARRIER[lg]) for lg, _, _ in C.TABLES)
        print("[OK] 10 张表一致（共 %d 列无官方对手：本项目编号 / 中文显示名 / monumod 登记占位）" % tot)

    # ── 守卫 2：夹具列 == FIELD_MAP 列（逐列同序）────────────────────────────
    print("\n===== [guard-2] 夹具跑出的 convert 列 vs FIELD_MAP 列 =====")
    ok_dir = os.path.join(base, "fixture_ok")
    _p, _cols, nrows = make_fixture(root, ok_dir)
    try:
        mod = C.load_converter(root)
    except C.ConverterError as e:
        print("[FAIL] 载入转换器失败：%s" % e)
        return rc | 1
    ecols, erows, err = C.expected_for_table(mod, "class", ok_dir)
    if err:
        return rc | fail("夹具跑 convert 失败：%s" % err)
    mine = [c for c, _, _, _ in C.FIELD_MAP["class"]]
    if ecols != mine:
        rc |= fail("夹具列=%r 与 FIELD_MAP 列=%r 不同" % (ecols, mine))
    else:
        print("[OK] class 列逐列同序（%d 列）" % len(ecols))

    # 顺带把 10 张表都核一遍（官方在位时才有意义；不在位就明说跳过）
    prob = []
    for lg, _, _ in C.TABLES:
        cols, rows_, err2 = C.expected_for_table(mod, lg, ok_dir)
        if err2:
            prob.append("%s: %s" % (lg, err2))
    print("[info] 其余 9 张表的列核对：%s" % ("夹具只含 charstats.txt ⇒ 预期报缺" if prob else "全过"))
    for p in prob:
        print("       %s" % p)

    # ── 守卫 3：无官方载体 ⇒ 821 行全 `缺官方值`，⛔ 一个 `一致` 都不许有 ──────
    print("\n===== [guard-3] 无官方载体 ⇒ 必须全 `缺官方值`（一个 `一致` 都不许有）=====")
    g3dir = os.path.join(base, "out_nosrc")
    pr = subprocess.run([sys.executable, DIFF, "--outdir", g3dir, "--no-cross"],
                        capture_output=True, text=True, encoding="utf-8", errors="replace",
                        cwd=root, timeout=600)
    rows = tsv_rows(os.path.join(g3dir, "s1_value_diff.rows.tsv"))
    tally = {}
    for r in rows:
        tally[r[6]] = tally.get(r[6], 0) + 1
    print("  判定行数=%d 结论分布=%s" % (len(rows), tally))
    if len(rows) != 821 or tally.get("一致", 0) != 0 or tally.get("缺官方值", 0) != 821:
        rc |= fail("guard-3：期望 821 行全 `缺官方值`、0 个 `一致`；实得 %d 行 %s" % (len(rows), tally))
    else:
        print("[OK] guard-3：821 行全 `缺官方值`、一致=0（无官方值就绝不写一致）")

    # ── 守卫 4：给了 mpq ⇒ 必须明说"先解包"且不比对，⛔ 不许假装能读 ──────────
    print("\n===== [guard-4] 官方载体是 *.mpq ⇒ 必须明说需要先解包、且不产出任何 `一致` =====")
    fake_mpq = os.path.join(base, "not-a-real.mpq")
    with io.open(fake_mpq, "w", encoding="ascii", newline="\n") as f:
        f.write("dummy\n")
    pr = subprocess.run([sys.executable, DIFF, "--src", fake_mpq,
                         "--outdir", os.path.join(base, "out_mpq"), "--no-cross"],
                        capture_output=True, text=True, encoding="utf-8", errors="replace",
                        cwd=root, timeout=600)
    out = pr.stdout or ""
    need = ["本仓没有 mpq 解包器", "data/global/excel", "--src"]
    ok4 = all(n in out for n in need)
    tally = {}
    for r in tsv_rows(os.path.join(base, "out_mpq", "s1_value_diff.rows.tsv")):
        tally[r[6]] = tally.get(r[6], 0) + 1
    if not ok4 or tally.get("一致", 0) != 0:
        rc |= fail("guard-4：mpq 分支输出不全或缺 %s；结论分布=%s"
                   % ([n for n in need if n not in out], tally))
    else:
        print("[OK] guard-4：命中 \"本仓没有 mpq 解包器\" / \"data/global/excel\" / \"--src\" 三条指引；"
              "结论分布=%s" % tally)

    # ── 自检 ①：已知正确样本 ⇒ 一致 ────────────────────────────────────────
    print("\n===== 自检① 已知正确样本 ⇒ 应判 一致 =====")
    print("夹具 = %s（由运行时 Class.tsv 反推官方值；÷4 列 ×4 还原）" % ok_dir.replace("\\", "/"))
    rf = os.path.join(base, "rows_class.txt")
    with io.open(rf, "w", encoding="utf-8", newline="\n") as f:
        for i in range(1, nrows + 1):
            f.write("Class:id=%d\n" % i)
    pr = run_diff(root, ok_dir, os.path.join(base, "out_ok"), rf)
    show(pr, "自检①")
    rows = tsv_rows(os.path.join(base, "out_ok", "s1_value_diff.rows.tsv"))
    flds = tsv_rows(os.path.join(base, "out_ok", "s1_value_diff.tsv"))
    if len(rows) != nrows:
        rc |= fail("① 判定行数 %d != %d" % (len(rows), nrows))
    ok = [r for r in rows if r[6] == "一致"]
    if len(ok) != nrows or any(r[6] != "一致" for r in rows):
        rc |= fail("① 期望 %d 行全 `一致`，实得 %s" % (nrows, [(r[0], r[6]) for r in rows]))
    else:
        print("[OK] ① %d/%d 行结论 = 一致；字段级 一致=%d / 非一致=%d"
              % (len(ok), nrows,
                 sum(1 for r in flds if r[6] == "一致"),
                 sum(1 for r in flds if r[6] != "一致")))

    # ── 自检 ②③：注入缺陷 ⇒ 不一致 + 指出列 ────────────────────────────────
    # (标签, 注入后的官方 LifePerLevel, 期望的官方 life_per_lvl, 期望差值 = 我们的值 - 官方值)
    for tag, newval, want_exp, sign in (("②", "12", "3", "-1"), ("③", "4", "1", "+1")):
        print("\n===== 自检%s 注入缺陷（Amazon LifePerLevel 8 → %s）⇒ 应判 不一致 =====" % (tag, newval))
        bad_dir = os.path.join(base, "fixture_bad%s" % tag)
        make_fixture(root, bad_dir, tweak=("Amazon", "LifePerLevel", newval))
        pr = run_diff(root, bad_dir, os.path.join(base, "out_bad%s" % tag), rf)
        show(pr, "自检%s" % tag)
        rows = tsv_rows(os.path.join(base, "out_bad%s" % tag, "s1_value_diff.rows.tsv"))
        hit = [r for r in rows if r[6] == "不一致"]
        if len(hit) != 1:
            rc |= fail("%s 期望恰好 1 行 `不一致`，实得 %d" % (tag, len(hit)))
        elif "LifePerLevel" not in hit[0][2] or hit[0][3] != want_exp or hit[0][4] != "2" \
                or hit[0][5] != sign:
            rc |= fail("%s 指错列/值：%s" % (tag, hit[0]))
        else:
            print("[OK] %s 命中 1 行 `不一致`，指出列 = `%s`；官方值=%s 我们的值=%s 差值=%s"
                  % (tag, hit[0][2], hit[0][3], hit[0][4], hit[0][5]))
        if tag == "②":
            others = [r for r in rows if r[6] != "不一致"]
            print("[info] %s 另 %d 行仍判 `一致` ⇒ 缺陷被精确定位（不是全表报红）"
                  % (tag, len(others)))

    print("\n===== 自检结论 =====")
    print("三件%s" % ("全过（比对器两向可证：正确样本 ⇒ 一致；注入缺陷 ⇒ 不一致 且指出列）"
                    if rc == 0 else "有不过 —— 见上面 [FAIL]"))
    return rc


if __name__ == "__main__":
    sys.exit(main())
