# -*- coding: utf-8 -*-
"""Durable producer for  (acceptance row 52).

WHY IT HAD TO BE REBUILT
    The row-52 evidence used to cite a now-deleted file, and
    `.ai-tmp/test/` is the skill's "one-off, delete when done" area -- that file is
    gone and the whole repo has no producer for it (see `.ai-tmp/test/report-gatefinal.md`:
    "n3_srcdam-out.txt (quan cang wu producer)").  This script is that producer, and it
    writes to a DURABLE landing point under `tools/probes/` (judging assets are committed,
    one-off products are not).

WHAT IT JUDGES (row 52: "技能·武器伤害类 -- 21 attack skills must settle by weapon damage")
    The official `skills.txt` column `SrcDam` is the game's own declaration of
    "this skill is a weapon-damage skill".  The audit finding R1/N3 was: of the rows the
    official table marks with a non-empty `SrcDam`, 21 of ours had `dmg_max = 0` AND no
    primary missile -- i.e. they had no effect at all.  This script re-derives that set
    mechanically from (a) the official txt and (b) our transcribed `skill_c`, so the
    "21 rows" claim in the acceptance table is a computed number, not a remembered one.

    It does NOT judge whether the skills now hurt: that is the runtime side and is judged
    by `tools/probes/hosts/combatcheck` Step 17 (`TryCast` end-to-end, unarmed vs armed).

SOURCES (both must be on disk; the official carrier is NOT in git, same as before)
    official : 原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/skills.txt
    ours     : 策划/数值文档/skill_c.txt

USAGE
    python tools/probes/refs/gen_n3_srcdam.py          # writes 
    python tools/probes/refs/gen_n3_srcdam.py --check  # exits 1 if the artifact is out of sync
"""

import hashlib
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))   # <project root>

OFFICIAL = os.path.join(ROOT, "\u539f\u7248\u8d44\u6e90", "\u53c2\u8003\u5de5\u7a0b_Diablerie",
                        "d2lod1.10txt", "data", "global", "excel", "skills.txt")
OURS = os.path.join(ROOT, "\u7b56\u5212", "\u6570\u503c\u6587\u6863", "skill_c.txt")
OUT = os.path.join(HERE, "n3_srcdam-out.txt")

SRCDAM_COL = "SrcDam"        # official column 219
CALC1_COL = "calc1"          # official column 186
CALC1D_COL = "*calc1 desc"   # official column 187
ETYPE_COL = "EType"          # official column 233
RANGE_COL = "range"          # official column 112


def read_tsv(path):
    with open(path, "rb") as f:
        raw = f.read()
    if raw[:2] in (b"\xff\xfe", b"\xfe\xff"):
        text = raw.decode("utf-16")
    elif raw[:3] == b"\xef\xbb\xbf":
        text = raw.decode("utf-8-sig")
    else:
        text = raw.decode("utf-8")
    rows = [ln.split("\t") for ln in text.split("\n")]
    return rows


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def idx_of(header, name):
    for i, c in enumerate(header):
        if c.strip() == name:
            return i
    raise SystemExit("column not found: %r" % name)


def main():
    if not os.path.isfile(OFFICIAL):
        print("BLOCKED official carrier missing: %s" % OFFICIAL)
        return 2
    if not os.path.isfile(OURS):
        print("BLOCKED our table missing: %s" % OURS)
        return 2

    off = read_tsv(OFFICIAL)
    oh = off[0]
    c_id = idx_of(oh, "Id")
    c_skill = idx_of(oh, "skill")
    c_sd = idx_of(oh, SRCDAM_COL)
    c_c1 = idx_of(oh, CALC1_COL)
    c_c1d = idx_of(oh, CALC1D_COL)
    c_et = idx_of(oh, ETYPE_COL)
    c_rg = idx_of(oh, RANGE_COL)

    by_official = {}
    by_name = {}
    for r in off[1:]:
        if len(r) <= c_sd:
            continue
        oid = r[c_id].strip() if len(r) > c_id else ""
        if oid.isdigit():
            by_official[int(oid)] = r
        nm = r[c_skill].strip() if len(r) > c_skill else ""
        if nm and nm not in by_name:
            by_name[nm] = r

    ours = read_tsv(OURS)
    h_index = {}
    header_row = None
    for i, r in enumerate(ours[:6]):
        if r and r[0].strip() == "id":
            header_row = i
            break
    if header_row is None:
        raise SystemExit("skill_c.txt header row not found")
    hh = ours[header_row]
    for name in ("id", "class", "name", "code", "official_id", "dmg_max", "missile", "src_dam"):
        h_index[name] = idx_of(hh, name)

    data = []
    for r in ours[header_row + 1:]:
        if not r or not r[0].strip():
            continue
        v = r[0].strip()
        if not v.lstrip("-").isdigit():
            continue
        data.append(r)

    def cell(r, name):
        i = h_index[name]
        return r[i].strip() if len(r) > i else ""

    # rows whose OFFICIAL SrcDam is non-empty == the official "weapon damage skill" set
    armed_all = []      # official says weapon-damage
    no_effect = []      # ... and ours had dmg_max == 0 and no primary missile (the R1/N3 family)
    executable = []     # ... and ours had dmg_max > 0
    for r in data:
        oid = cell(r, "official_id")
        code = cell(r, "code")
        row = None
        if oid.isdigit() and int(oid) in by_official:
            row = by_official[int(oid)]
        elif code in by_name:
            row = by_name[code]
        if row is None:
            continue
        sd = row[c_sd].strip() if len(row) > c_sd else ""
        if sd == "":
            continue
        armed_all.append((r, row, sd))
        dmg_max = cell(r, "dmg_max")
        missile = cell(r, "missile")
        try:
            dm = int(dmg_max) if dmg_max else 0
        except ValueError:
            dm = 0
        if dm <= 0 and missile == "":
            no_effect.append((r, row, sd))
        elif dm > 0:
            executable.append((r, row, sd))

    def line_of(r, row, sd):
        return ("cls=%s id=%3s %-10s official_id=%3s %-22s SrcDam=%4s range=%-5s EType='%s' "
                "calc1desc='%s' calc1=%s") % (
            cell(r, "class"), cell(r, "id"), cell(r, "name"),
            cell(r, "official_id"), row[c_skill].strip(),
            sd, row[c_rg].strip() if len(row) > c_rg else "",
            row[c_et].strip() if len(row) > c_et else "",
            row[c_c1d].strip() if len(row) > c_c1d else "",
            row[c_c1].strip() if len(row) > c_c1 else "")

    out = []
    out.append("=" * 78)
    out.append("N3 root cause (criterion = official skills.txt column %d `SrcDam` non-empty = weapon-damage class; "
               "our column `skill_c.src_dam` is the same value)" % c_sd)
    out.append("official = %s (sha256 %s)" % (os.path.relpath(OFFICIAL, ROOT).replace("\\", "/"),
                                              sha256_file(OFFICIAL)))
    out.append("ours     = %s (sha256 %s)" % (os.path.relpath(OURS, ROOT).replace("\\", "/"),
                                              sha256_file(OURS)))
    out.append("")
    out.append("skill_c rows whose OFFICIAL SrcDam is non-empty      = %d rows" % len(armed_all))
    out.append("of those, ours dmg_max=0 and missile empty          = %d rows * (the R1/N3 family, all no-effect)"
               % len(no_effect))
    out.append("")
    out.append("per row (class / skill_c.id / ours name / official_id / official skill / SrcDam / range / EType / "
               "official calc1 desc / official calc1):")
    for r, row, sd in no_effect:
        out.append(line_of(r, row, sd))
    out.append("")
    out.append("SrcDam non-empty and ours dmg_max>0 (executable, reference only) = %d" % len(executable))
    for r, row, sd in executable:
        out.append(line_of(r, row, sd))
    out.append("")
    rest = len(armed_all) - len(no_effect) - len(executable)
    out.append("remainder (SrcDam non-empty, ours dmg_max=0 but a primary missile exists) = %d rows"
               % rest)
    for r, row, sd in armed_all:
        if (r, row, sd) in no_effect or (r, row, sd) in executable:
            continue
        out.append(line_of(r, row, sd))
    out.append("")

    body = "\n".join(out)
    if "--check" in sys.argv:
        cur = open(OUT, encoding="utf-8").read() if os.path.isfile(OUT) else None
        if cur != body:
            print("STALE %s" % OUT)
            return 1
        print("OK %s in sync (%d rows no-effect / %d rows executable)" % (OUT, len(no_effect), len(executable)))
        return 0

    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    print("wrote %s" % OUT)
    print("  official SrcDam non-empty = %d ; no-effect = %d ; executable = %d"
          % (len(armed_all), len(no_effect), len(executable)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
