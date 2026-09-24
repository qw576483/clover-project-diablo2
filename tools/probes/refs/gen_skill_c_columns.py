# -*- coding: utf-8 -*-
"""Baseline record for the `skill_c` table COLUMN NAMES (acceptance row 52, gap 2).

WHY
    Row 52 was parked as "未修（需先冻结 `skill_c` 列名）".  The contract freeze point is
    `docs/步骤文档.md` §2 ("配表列名 ... 冻结后不许改"): `skill_c`'s declared column list there
    is `class/tree/name/req_level/req_skill/mana_cost/dmg_min/dmg_max/dmg_type/desc`.
    The weapon-damage fix (audit R1/N3) ADDED columns to the table (`src_dam` and the
    `dmg_pct_*` family), i.e. a contract change -- and the acceptance row cannot be judged
    until the column list has a frozen, machine-checkable baseline.

    This script only REGISTERS the frozen list (names + carrier sha256); it changes no
    runtime code and no table structure.  Updating `docs/步骤文档.md` §2 itself is a
    contract-document change and is OUT of this片's whitelist (reported to the lead).

USAGE
    python tools/probes/refs/gen_skill_c_columns.py          # writes tools/probes/refs/skill_c_columns.txt
    python tools/probes/refs/gen_skill_c_columns.py --check  # exits 1 when out of sync
"""

import hashlib
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(HERE)))
CARRIER = os.path.join(ROOT, "\u7b56\u5212", "\u6570\u503c\u6587\u6863", "skill_c.txt")
OUT = os.path.join(HERE, "skill_c_columns.txt")


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main():
    if not os.path.isfile(CARRIER):
        print("BLOCKED carrier missing: %s" % CARRIER)
        return 2
    with open(CARRIER, "rb") as f:
        raw = f.read()
    text = raw.decode("utf-8-sig") if raw[:3] == b"\xef\xbb\xbf" else raw.decode("utf-8")
    lines = text.split("\n")
    header = None
    type_row = None
    for i, ln in enumerate(lines[:6]):
        cells = ln.split("\t")
        if cells and cells[0].strip() == "id":
            header = cells
            type_row = lines[i + 1].split("\t") if i + 1 < len(lines) else []
            break
    if header is None:
        raise SystemExit("skill_c.txt header row not found")

    rel = os.path.relpath(CARRIER, ROOT).replace("\\", "/")
    out = []
    out.append("=" * 78)
    out.append("skill_c column-name baseline (frozen record for acceptance row 52)")
    out.append("carrier   = %s" % rel)
    out.append("sha256    = %s" % sha256_file(CARRIER))
    out.append("bytes     = %d" % len(raw))
    out.append("columns   = %d" % len(header))
    out.append("")
    out.append("index\tcolumn\tdeclared_type")
    for i, name in enumerate(header):
        t = type_row[i].strip() if len(type_row) > i else ""
        out.append("%d\t%s\t%s" % (i, name.strip(), t))
    out.append("")
    out.append("Weapon-damage columns added by the audit R1/N3 fix (NOT in docs/步骤文档.md §2 yet):")
    for need in ("src_dam", "dmg_pct_calc", "dmg_pct_desc", "dmg_pct_base", "dmg_pct_per_lvl",
                 "dmg_pct_parsed"):
        out.append("  %s = %s" % (need, "present" if need in [h.strip() for h in header] else "ABSENT"))
    out.append("")

    body = "\n".join(out)
    if "--check" in sys.argv:
        cur = open(OUT, encoding="utf-8").read() if os.path.isfile(OUT) else None
        if cur != body:
            print("STALE %s" % OUT)
            return 1
        print("OK %s in sync (%d columns)" % (OUT, len(header)))
        return 0

    with io.open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(body)
    print("wrote %s (%d columns)" % (OUT, len(header)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
