# -*- coding: utf-8 -*-
"""地图层**素材键像素属性审计**（判据资产；只读，可复跑）。

判什么
------
`tools/probes/refs/asset-keys-map-keys.tsv`（由 `hosts/mapcheck` 的 §36 从**生产布局生成物**
里全量枚举：营地/荒野/洞穴 × 地面层/物件层）里的每一个键，逐个判：

  ① 文件在不在（`ResPaths.Tile` / `ResPaths.ObjectSprite` 的拼法 = `D2/Tiles/<键>.png` / `D2/Objects/<键>.png`）；
  ② 不是**空图**（不透明像素 = 0 ⇒ 上屏等于空白 —— 比"缺文件"更隐蔽：菱形占位会被看成"没画"）；
  ③ 不是**平色**（唯一不透明色 = 1 且不透明像素 ≥ 8 ⇒ 静态渲染就是一块色块 = 占位观感）；
  ④ 平色若确实被引用，必须已在**既有白名单**_里处置（`MapView.PaletteCycledFlatWallTiles`
     = 原版靠调色板循环成水波的那张平色水墙；本项目无运行期循环 ⇒ 命中即**不叠**该 wall 瓦片）。
     ⛔ 本脚本不新增白名单、不自造替代图、不创颜色：它只判"白名单口径是否恰好覆盖了平色集"。

口径出处
--------
  · 键集合 / 文件拼法 = `hosts/mapcheck` 的 §36（从 `MapGenTownLayout` / `MapGenWildLayout` /
    `MapGenCaveLayout` 三张生成物解析）+ `Core/ResPaths.cs` 的 `Tile` / `ObjectSprite`
  · 白名单           = `Module/Map/MapView.cs` 的 `PaletteCycledFlatWallTiles`
  · 平色口径         = 与 `hosts/mapcheck` 的 `TryFlatColor` 同一句话：只数不透明像素；
                       不透明 < 8 的细条不算平色

怎么跑（**先跑宿主**，本脚本读它的键表）
----------------------------------------
    dotnet run --project tools/probes/hosts/mapcheck -c Release
    python tools/probes/measure/asset_key_map_audit.py
退出码：0 = PASS（0 缺文件 + 0 空图 + 0 未处置平色）；1 = FAIL。
产物：`tools/probes/refs/asset-keys-map-path.tsv`（幂等，无时间戳）。
"""

import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))


def find_root():
    d = HERE
    for _ in range(8):
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        d = os.path.dirname(d)
    raise SystemExit("找不到含 client/Assets 的仓库根")


ROOT = find_root()
KEYS_TSV = os.path.join(ROOT, "tools", "probes", "refs", "asset-keys-map-keys.tsv")
MAPVIEW = os.path.join(ROOT, "client", "Assets", "Scripts", "Module", "Map", "MapView.cs")
OUT_TSV = os.path.join(ROOT, "tools", "probes", "refs", "asset-keys-map-path.tsv")


def read_white_list():
    text = io.open(MAPVIEW, encoding="utf-8", errors="replace").read()
    # 认**声明行**（`… PaletteCycledFlatWallTiles =`），⛔ 不认注释/`<see cref>` 里的同名提及
    i = text.find("PaletteCycledFlatWallTiles =")
    if i < 0:
        raise SystemExit("MapView.cs 里找不到 PaletteCycledFlatWallTiles 的声明 ⇒ 判据失效，停下来")
    # 数组体：从 `private static readonly string[] PaletteCycledFlatWallTiles =` 的下一个 `{` 到配对的 `};`
    j = text.find("{", i)
    k = text.find("};", j)
    body = text[j:k if k > 0 else len(text)]
    keys = re.findall(r'"([a-z0-9_]+/\d{3})"', body)
    if not keys:
        raise SystemExit("白名单解析出 0 键 ⇒ 判据失效，停下来（空白名单会让下面的判决变成假红）")
    return set(keys)


def png_stats(path):
    from PIL import Image
    with Image.open(path) as im:
        im = im.convert("RGBA")
        size = im.size
        opaque = 0
        uniq = set()
        for r, g, b, a in im.getdata():
            if a == 0:
                continue
            opaque += 1
            if len(uniq) <= 1:
                uniq.add((r, g, b))
        return size[0], size[1], opaque, len(uniq)


def main():
    if not os.path.isfile(KEYS_TSV):
        raise SystemExit("缺少键表 %s ⇒ 先跑 hosts/mapcheck（§36 会写它）" % KEYS_TSV)
    white = read_white_list()
    d2 = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2")

    rows = []
    missing, blanks, flats, unhandled = [], [], [], []
    by_area = {}
    with io.open(KEYS_TSV, encoding="utf-8") as f:
        f.readline()                       # 表头
        for line in f:
            c = line.rstrip("\r\n").split("\t")
            if len(c) < 5:
                continue
            area, layer, key, rel, _src = c[0], c[1], c[2], c[3], c[4]
            rel_in_d2 = rel[len("D2/"):] if rel.startswith("D2/") else rel
            path = os.path.join(d2, rel_in_d2.replace("/", os.sep) + ".png")
            st = by_area.setdefault((area, layer), {"n": 0, "miss": 0, "blank": 0, "flat": 0, "flatOk": 0})
            st["n"] += 1
            if not os.path.isfile(path) or os.path.getsize(path) <= 0:
                rows.append((area, layer, key, rel, "缺文件", "", "", "", "MISSING"))
                missing.append(area + "/" + layer + "/" + key)
                st["miss"] += 1
                continue
            w, h, opaque, uniq = png_stats(path)
            if opaque == 0:
                rows.append((area, layer, key, rel, "在盘", "%dx%d" % (w, h), 0, 0, "BLANK"))
                blanks.append(area + "/" + layer + "/" + key)
                st["blank"] += 1
            elif uniq == 1 and opaque >= 8:
                handled = key in white
                rows.append((area, layer, key, rel, "在盘", "%dx%d" % (w, h), opaque, uniq,
                             "FLAT(已处置:白名单)" if handled else "FLAT(未处置)"))
                flats.append((area, layer, key))
                st["flat"] += 1
                if handled:
                    st["flatOk"] += 1
                else:
                    unhandled.append(area + "/" + layer + "/" + key)
            else:
                rows.append((area, layer, key, rel, "在盘", "%dx%d" % (w, h), opaque, uniq, "ok"))

    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with io.open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write("区域\t层\t素材键\t路径\t文件\t尺寸\t不透明像素\t唯一色\t判决\n")
        for r in rows:
            f.write("\t".join(str(x) for x in r) + "\n")

    print("== 地图层素材键像素属性审计（读 hosts/mapcheck §36 的键表）==")
    print("  白名单（解析自 MapView.cs）：%d 键 %s" % (len(white), ",".join(sorted(white))))
    print("  区域\t层\t被引用键\t缺文件\t空图\t平色(已处置/共)")
    for (area, layer), st in sorted(by_area.items()):
        print("  %s\t%s\t%d\t%d\t%d\t%d/%d" % (area, layer, st["n"], st["miss"], st["blank"], st["flatOk"], st["flat"]))
    print("  合计：键 %d、缺文件 %d、空图 %d、平色 %d（其中未处置 %d）"
          % (len(rows), len(missing), len(blanks), len(flats), len(unhandled)))
    if flats:
        print("    平色明细：%s" % " | ".join("%s/%s/%s" % t for t in flats))
    if unhandled:
        print("    ⛔ 未处置平色：%s" % ",".join(sorted(unhandled)))
    print("  产物：%s（%d 行）" % (OUT_TSV, len(rows)))
    ok = (not missing) and (not blanks) and (not unhandled)
    print("VERDICT=%s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
