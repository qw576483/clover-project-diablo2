# -*- coding: utf-8 -*-
"""地面物品 / 图标面**素材键机械审计**（判据资产；只读，可复跑）。

判什么
------
`Module/View/GroundItemVisual.IconPathOf` 是地面物品取图的**唯一入口**：它把 `itemId`
交给 `UI/D2Icon.ItemIconPath`（配表 `code` → 别名表 `IconFileAlias`）拼出 `D2/Items/inv{code}`。
本脚本把这条路径上**每一个可能被请求的键**逐个落到磁盘上判：

  ① 文件在不在（`client/Assets/Resources/Clover/D2/Items/<key>.png`）；
  ② 不是**空图**（不透明像素 = 0 ⇒ 上屏等于空白，比"缺文件"更隐蔽）；
  ③ 不是**平色**（唯一不透明色 = 1 且不透明像素 ≥ 8 ⇒ 静态渲染就是一块色块 = 占位观感）；
  ④ 缺文件的行**必须同时满足"永不进生成/掉落/商店池"**（`item_c.type` ∈ 资料片专属 6 类，
     判据出处 `Module/Item/ItemIconAvailability.cs` 的 `ExpansionOnlyTypes`）——
     否则就是「会进池、又取不到图」= 真缺口（玩家会撞见暗色占位）。

口径出处（只引用、不另抄一份）
-----------------------------
  · 别名表      = `client/Assets/Scripts/UI/D2Icon.cs` 的 `IconFileAlias`（50 条；原版
                  `Weapons/Armor/Misc.txt` 的 `invfile` 列）
  · 物品全集    = `client/Assets/StreamingAssets/Table/Item.tsv` 的 `id`/`code`/`type` 列
  · 剔除集合    = `client/Assets/Scripts/Module/Item/ItemIconAvailability.cs` 的 `ExpansionOnlyTypes`
  · 金币三档    = `Module/View/GroundItemVisual.cs` 的金币分支（`ResPaths.ItemIcon("gld")`）
  · 平色口径    = 与 `tools/probes/hosts/mapcheck` 的 `TryFlatColor` 同口径
                  （只数不透明像素；不透明 < 8 的细条不算平色）

怎么跑
------
    python tools/probes/measure/asset_key_item_audit.py
退出码：0 = PASS（0 空图 + 0 平色 + "缺图的行"全部被剔除集合覆盖）；1 = FAIL。
产物：`tools/probes/refs/asset-keys-items-path.tsv`（幂等，无时间戳）。
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
    raise SystemExit("找不到含 client/Assets 的仓库根（脚本位置不对？）")


ROOT = find_root()
D2ICON = os.path.join(ROOT, "client", "Assets", "Scripts", "UI", "D2Icon.cs")
AVAIL = os.path.join(ROOT, "client", "Assets", "Scripts", "Module", "Item", "ItemIconAvailability.cs")
ITEM_TSV = os.path.join(ROOT, "client", "Assets", "StreamingAssets", "Table", "Item.tsv")
RES = os.path.join(ROOT, "client", "Assets", "Resources", "Clover")
OUT_TSV = os.path.join(ROOT, "tools", "probes", "refs", "asset-keys-items-path.tsv")

PAIR = re.compile(r'\{\s*"([a-z0-9_]+)"\s*,\s*"([a-z0-9_]+)"\s*\}')
QUOTED = re.compile(r'"([a-z0-9_]+)"')
PATH_COL = "ViewModule.GroundItemVisual→D2/Items"


def read_alias():
    text = io.open(D2ICON, encoding="utf-8", errors="replace").read()
    i = text.find("IconFileAlias")
    if i < 0:
        raise SystemExit("D2Icon.cs 里找不到 IconFileAlias（口径变了？本脚本必须停下来）")
    j = text.find("};", i)
    pairs = PAIR.findall(text[i:j if j > 0 else len(text)])
    if not pairs:
        raise SystemExit("IconFileAlias 解析出 0 条（正则与源码形状不符）⇒ 判据失效，停下来")
    return dict(pairs), len(pairs)


def read_expansion_types():
    text = io.open(AVAIL, encoding="utf-8", errors="replace").read()
    i = text.find("ExpansionOnlyTypes")
    if i < 0:
        raise SystemExit("ItemIconAvailability.cs 里找不到 ExpansionOnlyTypes ⇒ 判据失效，停下来")
    j = text.find("};", i)
    body = text[i:j if j > 0 else len(text)]
    names = [m for m in QUOTED.findall(body) if m != "ExpansionOnlyTypes"]
    if not names:
        raise SystemExit("ExpansionOnlyTypes 解析出 0 条 ⇒ 判据失效，停下来")
    return set(names)


def read_items():
    rows = []
    with io.open(ITEM_TSV, encoding="utf-8", errors="replace") as f:
        header = f.readline().rstrip("\r\n").split("\t")
        need = ["id", "code", "type"]
        idx = {}
        for c in need:
            if c not in header:
                raise SystemExit("Item.tsv 表头缺少 %s 列 ⇒ 判据失效" % c)
            idx[c] = header.index(c)
        for line in f:
            c = line.rstrip("\r\n").split("\t")
            if len(c) <= max(idx.values()) or not c[idx["id"]].strip():
                continue
            rows.append((int(c[idx["id"]]), c[idx["code"]], c[idx["type"]]))
    return rows


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
    alias, alias_n = read_alias()
    expansion = read_expansion_types()
    items = read_items()

    keys = []                       # (键, 来源, type, itemId)
    for iid, code, typ in items:
        keys.append(("inv" + alias.get(code, code), "item_c#%d code=%s type=%s" % (iid, code, typ), typ, iid))
    keys.append(("invgld", "金币（GroundItemVisual 的金币分支；非 item_c 行）", "-", 0))
    info_only = ["invgldm", "invgldh"]      # 金币另两档：当前无引用方，仅作信息

    seen = set()
    rows = []
    missing = []            # (键, type) —— 缺文件
    blanks, flats = [], []
    for key, src, typ, _iid in keys:
        if key in seen:
            continue
        seen.add(key)
        path = os.path.join(RES, "D2", "Items", key + ".png")
        if not os.path.isfile(path) or os.path.getsize(path) <= 0:
            pooled = "剔除(type=%s)" % typ if typ in expansion else "**会进池**"
            rows.append(("-", PATH_COL, key, "缺文件", pooled, "", "", "", "MISSING"))
            missing.append((key, typ))
            continue
        w, h, opaque, uniq = png_stats(path)
        if opaque == 0:
            rows.append(("-", PATH_COL, key, "在盘", "-", "%dx%d" % (w, h), 0, 0, "BLANK"))
            blanks.append(key)
        elif uniq == 1 and opaque >= 8:
            rows.append(("-", PATH_COL, key, "在盘", "-", "%dx%d" % (w, h), opaque, uniq, "FLAT"))
            flats.append(key)
        else:
            rows.append(("-", PATH_COL, key, "在盘", "-", "%dx%d" % (w, h), opaque, uniq, "ok"))

    for key in info_only:
        p = os.path.join(RES, "D2", "Items", key + ".png")
        rows.append(("-", "（金币另两档，当前无引用方）", key,
                     "在盘" if os.path.isfile(p) else "缺文件", "-", "", "", "", "info"))

    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with io.open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write("区域\t路径\t素材键\t文件\t进池\t尺寸\t不透明像素\t唯一色\t判决\n")
        for r in rows:
            f.write("\t".join(str(x) for x in r) + "\n")

    # 进池的键 = `item_c.type ∉ 剔除集合` ⇒ 会被生成/掉落/商店请求 ⇒ 那时缺图 = 真缺口
    pooled_missing = [k for k, t in missing if t not in expansion]

    print("== 地面物品 / 图标面素材键机械审计 ==")
    print("  别名表（解析自 UI/D2Icon.cs）：%d 条" % alias_n)
    print("  剔除集合（解析自 ItemIconAvailability.cs）：%s（%d 类）" % (",".join(sorted(expansion)), len(expansion)))
    print("  物品行数（Item.tsv）：%d；被请求键（去重）：%d" % (len(items), len(seen)))
    print("  缺文件 %d 个，全部落在剔除集合内：%s" % (len(missing), pooled_missing == []))
    print("    缺图明细：%s" % ",".join(sorted(k for k, _t in missing)))
    print("    ⛔ 会进池却缺图（= 真缺口）：%d %s" % (len(pooled_missing), ",".join(sorted(pooled_missing)) if pooled_missing else "-"))
    print("  空图（不透明像素 = 0）：%d %s" % (len(blanks), ",".join(blanks) if blanks else "-"))
    print("  平色（唯一色 = 1 且不透明 ≥ 8）：%d %s" % (len(flats), ",".join(flats) if flats else "-"))
    print("  产物：%s（%d 行）" % (OUT_TSV, len(rows)))
    ok = (not pooled_missing) and (not blanks) and (not flats)
    print("VERDICT=%s" % ("PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
