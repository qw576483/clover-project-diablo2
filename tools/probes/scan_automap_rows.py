# -*- coding: utf-8 -*-
"""scan_automap_rows.py -- 对每个**真实出现在关卡里的** (层, Style, Sequence) 键，列出
`AutoMap.txt` 的**全部**命中行（家族 + Cel1..4），并标出：
  · 我们当前规则会选哪一行 / 哪个 cel（= `gen_automap.py:resolve_cel` 的口径：文件里最先出现的行、取第一个 CelN>=0）
  · 同一键下**还有没有**别的行给出**高 CelN 的"岩块族"** cel（判定线：CelN >= ROCK_LO，本文件里 = 1100）

为什么需要它（片 automap-redo2 第 2 轮）：全量联络图显示 `MaxiMap.dc6` 里
  · 低 CelN（0..~120）的墙族 cel = **细虚线/小碎点**（我们表用的就是这一批：10/11/18/20/60/65）
  · 高 CelN（~1144..1255）的墙族 cel = **浅灰"岩块/M 形/L 形"大块**（原版 automap 边界那一族）
⇒ 可证伪的推断：差距不在配色，而在 **同一 (LevelName, Style, Sequence) 命中多行时挑错行**。
本脚本把这条推断变成可机械核对的事实（只读；不改 `gen_automap.py`，该脚本不在本片白名单）。

用法：`python tools/probes/scan_automap_rows.py [--rock-lo 1100] [--all-keys]`
"""
import collections
import glob
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, os.path.join(ROOT, "tools"))

from d2codec import ds1 as ds1mod  # noqa: E402

AUTOMAP_TXT = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "excel", "AutoMap.txt")
TILES_ROOT = os.path.join(ROOT, "原版资源", "d2raw", "data", "global", "tiles", "ACT1")
OUT = os.path.join(ROOT, ".ai-tmp", "test", "automapredo", "row_scan.txt")
FLOOR_NAMES = frozenset(("fl", "co"))

LEVELS = collections.OrderedDict([
    ("Town", dict(level_name="1 Town", ds1=[os.path.join(TILES_ROOT, "TOWN", n) for n in
                                            ("TownN1.ds1", "TownE1.ds1", "TownS1.ds1", "TownW1.ds1")])),
    ("BloodMoor", dict(level_name="1 Wilderness",
                       ds1=sorted(glob.glob(os.path.join(TILES_ROOT, "OUTDOORS", "*.ds1"))) +
                           [os.path.join(TILES_ROOT, "TOWN", "TownETrans.ds1")])),
    ("DenOfEvil", dict(level_name="1 Cave",
                       ds1=sorted(glob.glob(os.path.join(TILES_ROOT, "CAVES", "*.ds1"))))),
])


def load_rows():
    raw = open(AUTOMAP_TXT, "rb").read().decode("latin-1")
    rows, header = [], None
    for line in raw.split("\n"):
        line = line.rstrip("\r")
        if not line.strip():
            continue
        f = line.split("\t")
        if header is None:
            header = f
            continue
        try:
            rows.append((f[0], f[1], int(f[2]), int(f[3]), int(f[4]),
                         [int(f[6]), int(f[8]), int(f[10]), int(f[12])]))
        except (IndexError, ValueError):
            pass
    return rows


def first_cel(cels):
    for c in cels:
        if c >= 0:
            return c
    return -1


def main(argv):
    rock_lo = 1100
    i = 1
    while i < len(argv):
        if argv[i] == "--rock-lo":
            rock_lo = int(argv[i + 1]); i += 2
        else:
            i += 1

    rows = load_rows()
    by_level = collections.defaultdict(list)
    for r in rows:
        by_level[r[0]].append(r)

    log = []

    def say(s):
        print(s)
        log.append(s)

    say("ROCK_LO = %d  (高 CelN 墙族 = 联络图里那一族浅灰岩块；低 CelN 墙族 = 细虚线)" % rock_lo)

    total_keys = 0
    keys_with_alt = 0
    for tag, cfg in LEVELS.items():
        keys = collections.Counter()
        for path in cfg["ds1"]:
            if not os.path.exists(path):
                continue
            d = ds1mod.load_ds1(path)
            for layer, cells, is_floor in (("ground", d.floors[0] if d.floors else [], True),
                                           ("object", d.walls[0] if d.walls else [], False)):
                for c in cells:
                    if c.is_empty:
                        continue
                    keys[(is_floor, c.prop3 & 0x0F, c.prop2)] += 1
        say("")
        say("=== %s (LevelName=%s)  ds1=%d  distinct (floor,style,seq) keys=%d ===" %
            (tag, cfg["level_name"], len(cfg["ds1"]), len(keys)))
        for (is_floor, style, seq), n in sorted(keys.items(), key=lambda kv: -kv[1]):
            hits = [r for r in by_level.get(cfg["level_name"], ())
                    if r[2] == style and (r[3] < 0 or r[4] < 0 or r[3] <= seq <= r[4])
                    and ((r[1] in FLOOR_NAMES) == is_floor)]
            total_keys += 1
            if not hits:
                say("  %-5s style=%-2d seq=%-3d cells=%-5d  NO-HIT" %
                    ("floor" if is_floor else "wall", style, seq, n))
                continue
            pick = hits[0]
            pick_cel = first_cel(pick[5])
            alts = [(r, first_cel(r[5])) for r in hits
                    if first_cel(r[5]) >= rock_lo]
            if alts and pick_cel < rock_lo:
                keys_with_alt += 1
            say("  %-5s style=%-2d seq=%-3d cells=%-5d  hits=%d" %
                ("floor" if is_floor else "wall", style, seq, n, len(hits)))
            say("      PICK      family=%-5s seq=[%d,%d] cels=%s -> cel %d%s" %
                (pick[1], pick[3], pick[4], pick[5], pick_cel,
                 "  <-- ROCK-CHUNK" if pick_cel >= rock_lo else "  (细虚/碎点族)"))
            for r, c in alts:
                say("      ALT       family=%-5s seq=[%d,%d] cels=%s -> cel %d  <-- ROCK-CHUNK" %
                    (r[1], r[3], r[4], r[5], c))
            for r in hits:
                if r is pick:
                    continue
                c = first_cel(r[5])
                if c < rock_lo:
                    say("      alt(low)  family=%-5s seq=[%d,%d] cels=%s -> cel %d" %
                        (r[1], r[3], r[4], r[5], c))

        # ---- 推断 B 的假设检验：把 ground 键的 seq 平移 -1 / 0 / +1，看 NO-HIT 率 ----
        say("")
        say("  [shift test] 把 key 里的 Sequence 平移后重算 NO-HIT（只对 floor/wall 分开统计格数）")
        for is_floor in (True, False):
            row = []
            for shift in (-1, 0, 1):
                tot = 0
                miss = 0
                for (kf, style, seq), n in keys.items():
                    if kf != is_floor:
                        continue
                    s = seq + shift
                    tot += n
                    hit = any(r[2] == style and (r[3] < 0 or r[4] < 0 or r[3] <= s <= r[4])
                              and ((r[1] in FLOOR_NAMES) == is_floor)
                              for r in by_level.get(cfg["level_name"], ()))
                    if not hit:
                        miss += n
                row.append("shift%+d: %d/%d NO-HIT (%.1f%%)"
                           % (shift, miss, tot, (100.0 * miss / tot) if tot else 0.0))
            say("    %-5s  %s" % ("floor" if is_floor else "wall", "   ".join(row)))

        say("")
        say("  [shift detail] floor 键按格数排序前 12 个：在 shift 0 / +1 下各自命中哪个 cel")
        fkeys = [((kf, style, seq), n) for (kf, style, seq), n in keys.items() if kf]
        fkeys.sort(key=lambda kv: -kv[1])
        for (kf, style, seq), n in fkeys[:12]:
            cells = []
            for shift in (0, 1):
                s = seq + shift
                h = [r for r in by_level.get(cfg["level_name"], ())
                     if r[2] == style and (r[3] < 0 or r[4] < 0 or r[3] <= s <= r[4])
                     and (r[1] in FLOOR_NAMES)]
                cells.append("shift%d->%s" % (shift, ("cel %d" % first_cel(h[0][5])) if h
                                              else "NO-HIT"))
            say("    floor style=%-2d seq=%-3d cells=%-6d  %s" %
                (style, seq, n, "   ".join(cells)))

    say("")
    say("SUMMARY keys=%d  keys where a ROCK-CHUNK row exists but the PICK is not one = %d"
        % (total_keys, keys_with_alt))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="") as fh:
        fh.write("\n".join(log) + "\n")
    say("log -> %s" % OUT)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
