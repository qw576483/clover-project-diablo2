# -*- coding: utf-8 -*-
"""临时探针（片 6）：把 `策划/数值文档/monster_c.txt` 的 8 列数值**逐条复算**回官方公式，
与官方 LoD 1.10 表（`原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/`）比对。

官方公式（引擎 `MONSTER_CalculateLevelScaledStats @006538a0`；参考实现
`libd2/packages/game/src/montable.zig:256-286` + `libd2/packages/drlg/src/drlg/monpop.zig:399-411`）：
    level   = 区域等级（= Levels.txt MonLvl1）
    AC      = MonLvl.AC[level]  * MonStats.AC      / 100     （整数截断）
    AR      = MonLvl.TH[level]  * MonStats.A1TH    / 100
    dmg_min = MonLvl.DM[level]  * MonStats.A1MinD  / 100
    dmg_max = MonLvl.DM[level]  * MonStats.A1MaxD  / 100
    exp     = MonLvl.XP[level]  * MonStats.Exp     / 100
    hp_min  = MonLvl.HP[level]  * MonStats.minHP   / 100
    hp_max  = MonLvl.HP[level]  * MonStats.maxHP   / 100
    hp      = (hp_min + hp_max) / 2  （四舍五入；本项目口径，见下）

用完即删（skill §1.8）。
"""
import os, sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

ROOT = r"c:\Work\Server\full-dev\clover-project-diablo2"
OFF = os.path.join(ROOT, "原版资源", "参考工程_Diablerie", "d2lod1.10txt", "data", "global", "excel")


def load(path, enc="cp1252"):
    with open(path, "r", encoding=enc, newline="") as f:
        rows = [l.rstrip("\r").split("\t") for l in f.read().split("\n")]
    return rows[0], rows[1:]


def cell(hdr, row, name):
    i = hdr.index(name)
    v = row[i] if i < len(row) else ""
    return v.strip()


MH, MROWS = load(os.path.join(OFF, "MonStats.txt"))
LH, LROWS = load(os.path.join(OFF, "MonLvl.txt"))
CH, CROWS = load(os.path.join(ROOT, "策划", "数值文档", "monster_c.txt"), enc="utf-8-sig")


def monlvl(level):
    for r in LROWS:
        if r and r[0].strip() == str(level):
            return {k: int(cell(LH, r, k) or 0) for k in ("AC", "TH", "HP", "DM", "XP")}
    raise SystemExit("MonLvl 找不到 level=%s" % level)


def monget(mon_id, key):
    for r in MROWS:
        if r and r[0].strip() == mon_id:
            return cell(MH, r, key)
    raise SystemExit("MonStats 找不到 %s" % mon_id)


def main():
    only = sys.argv[1] if len(sys.argv) > 1 else None

    # load() 已把第 0 行当表头返回 ⇒ CROWS[0] 是**列类型行**、CROWS[1] 是**说明行**、CROWS[2:] 是数据。
    cols = CH
    body = [r for r in CROWS if r and len(r) > 1 and r[0].isdigit()]

    print("列：", ", ".join(cols))
    bad = 0
    n = 0
    for row in body:
        c = {cols[i]: (row[i] if i < len(row) else "") for i in range(len(cols))}
        mid = c["code"]
        level = int(c["level"])
        ml = monlvl(level)

        def pct(col, mul):
            return int(mul) * int(monget(mid, col) or 0) // 100

        exp_hp_min = pct("minHP", ml["HP"])
        exp_hp_max = pct("maxHP", ml["HP"])
        exp_hp = (exp_hp_min + exp_hp_max + 1) // 2 if (exp_hp_min + exp_hp_max) % 2 else (exp_hp_min + exp_hp_max) // 2
        want = {
            "hp_min": exp_hp_min, "hp_max": exp_hp_max, "hp": exp_hp,
            "ac": pct("AC", ml["AC"]), "ar": pct("A1TH", ml["TH"]),
            "dmg_min": pct("A1MinD", ml["DM"]), "dmg_max": pct("A1MaxD", ml["DM"]),
            "exp": pct("Exp", ml["XP"]),
        }
        print("─" * 100)
        print("m#%s %s（官方 id=%s，区域等级=%d ⇒ MonLvl 行 AC/TH/HP/DM/XP = %s/%s/%s/%s/%s）"
              % (c["id"], c["name"], mid, level, ml["AC"], ml["TH"], ml["HP"], ml["DM"], ml["XP"]))
        for k in ("hp_min", "hp_max", "hp", "ac", "ar", "dmg_min", "dmg_max", "exp"):
            got = int(c[k])
            n += 1
            ok = got == want[k]
            if not ok:
                bad += 1
            print("   %-8s 表=%-5d 官方公式=%-5d %s" % (k, got, want[k], "OK" if ok else "  <<< 不一致"))

    print("═" * 100)
    print("怪物数值逐条复算：%d 项，不一致 %d 项" % (n, bad))
    return 1 if bad else 0


sys.exit(main())
