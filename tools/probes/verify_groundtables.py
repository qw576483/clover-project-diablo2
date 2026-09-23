#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""S2 diagnosis probe: do the MapView GROUND-tile tables point at files that exist?

Why this probe exists (2026-09-23, S2 slice):
  S3's live tile `.ai-tmp/screenshots/s3_hit_before.png` shows Blood Moor with a LARGE
  BLACK background and only a few diamond-shaped ground tiles.  The wilderness path in
  `MapView.PlanCell` does NOT use per-cell ds1 keys (GridMap._tileOverrides == false for
  the wild); it takes `GroundKeyOf(kind, area, g)` == a RANDOM entry of one of the static
  tables (GrassTiles / DirtTiles / RoadTiles / ...).  If a table references a file that is
  NOT on disk, `TrySprite` returns null and the cell falls back to the flat-colour
  placeholder -> the user sees "patchy ground + black".

  So: parse `Module/Map/MapView.cs`, pull every `static readonly string[] <NAME>Tiles`
  array, and check every key against
      client/Assets/Resources/Clover/D2/Tiles/<pack>/<idx>.png
  (ground keys) -- reporting per table: total / missing / sample.

Read-only. No writes to the project. ASCII only.

Run:  python tools/probes/verify_groundtables.py
"""
import io
import os
import re
import sys

def find_root(start):
    d = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            raise SystemExit("repo root not found (no client/Assets above me)")
        d = parent


def main():
    root = find_root(os.path.dirname(os.path.abspath(__file__)))
    src = os.path.join(root, "client", "Assets", "Scripts", "Module", "Map", "MapView.cs")
    text = io.open(src, encoding="utf-8").read()

    artroot = os.path.join(root, "client", "Assets", "Resources", "Clover", "D2")
    tileroot = os.path.join(artroot, "Tiles")
    objroot = os.path.join(artroot, "Objects")

    # static readonly string[] XxxTiles = { "pack/idx", ... };
    # NOTE: the SAME `...Tiles` naming is used for the OBJECT tables (RockMoorTiles /
    # TreeMoorTiles / ...); their keys resolve under `D2/Objects/`, so a key must be looked
    # up in BOTH roots and only "in neither" counts as missing.
    pat = re.compile(r"static\s+readonly\s+string\[\]\s+(\w+)\s*=\s*\{(.*?)\};", re.S)
    total_tables = 0
    total_keys = 0
    missing_all = []
    print("MapView.cs : %s" % src)
    print("art root   : %s" % artroot)
    print("")
    for m in pat.finditer(text):
        name = m.group(1)
        body = m.group(2)
        keys = re.findall(r'"([^"]+)"', body)
        if not keys:
            continue
        total_tables += 1
        total_keys += len(keys)
        n_tiles = 0
        n_objs = 0
        missing = []
        for k in keys:
            rel = k.replace("/", os.sep) + ".png"
            if os.path.isfile(os.path.join(tileroot, rel)):
                n_tiles += 1
            elif os.path.isfile(os.path.join(objroot, rel)):
                n_objs += 1
            else:
                missing.append(k)
        print("  %-26s keys=%3d -> Tiles=%3d Objects=%3d MISSING=%3d"
              % (name, len(keys), n_tiles, n_objs, len(missing)))
        if missing:
            print("      missing (in neither root): %s" % (", ".join(missing[:24])
                                                           + (" ..." if len(missing) > 24 else "")))
            missing_all.append((name, missing))

    print("")
    print("tables=%d keys=%d tables-with-missing=%d" % (total_tables, total_keys, len(missing_all)))
    if missing_all:
        print("VERDICT: some table keys resolve in NEITHER D2/Tiles nor D2/Objects -> such a cell")
        print("         falls back to the flat-colour placeholder (MapView.TrySprite -> null).")
        return 1
    print("VERDICT: every key of every MapView art table resolves in D2/Tiles or D2/Objects")
    print("         => the Blood-Moor black background is NOT explained by missing art.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
