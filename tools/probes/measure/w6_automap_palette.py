# -*- coding: utf-8 -*-
"""w6 · **自动地图配色出处量法**（可复现；只读，不写任何工程产物）。

⚠️⚠️ **已过时（deprecated，2026-09-23 / 片 S1 归一）—— 不再作为判据，只作历史留痕** ⚠️⚠️
  本文件的**断言段**判的是 `ColBackdrop(0x1C1C1C) / ColFloor(0x484848) / ColWallLine(0xC4C4C4)`
  这三个常量 —— 它们来自「程序化点阵 + 本项目自选配色」的**旧口径**，**在产品里早已删除**
  （现在只在 `MiniMapPanel.cs` 的注释里作为"旧版"出现；`uicheck/P5Check.cs` 另有断言要求这三个名字 **0 命中**）。
  ⇒ 本文件的自断言**恒绿**（它断言的色值来自 `Banner/mapicon` 的 PNG 像素，与产品当前画法无关）
  ⇒ 属"**能靠改一个数字变绿**"的假判据，⛔ 不许接进任何闸门。
  另：文件头原先写的"原版 automap 图块表在本机不存在（`原版资源/` 不存在）"**已不成立**
  （`原版资源/d2dc6/.../AUTOMAP/MaxiMap.dc6`、`d2raw/.../excel/AutoMap.txt`、`ACT1/Pal.PL2` 都在位）。

  **分工（同类量法只留一处权威）**：
    · **配色出处权威 = `pal_probe.py`**（片 S1）：读**原版载体**（`ACT1/Pal.PL2` 的切片 +
      `AUTOMAP/MaxiMap.dc6` 真正使用的 198 个索引及其亮度 + `MINIMAP/mapicons.DC6` 的白色模板锚点）
      ⇒ 判"我们的 automap 画出去的 RGB 是否与原版 cel 数据同源"。
    · **本文件唯一仍有价值的部分**：它对 `D2/UI/MiniMap/mapicon_*.png` 的**像素直方图** ——
      实测 8 帧唯一色 = `#F4F4F4`，这是一条**独立的第二条路径**，与 `pal_probe.py` 的
      `pal[32]=(244,244,244)` **互相印证**（白色模板锚点成立）。⇒ 所以**保留本文件**（不删），
      但按上面的定位使用：**当交叉印证看，不当判据。**

为什么有这个脚本：`client/Assets/Scripts/UI/MiniMapPanel.cs` 的三个配色常量
（暗底 `#1C1C1C` / 地板点 `#484848` / 墙线 `#C4C4C4`）**不是自选配色**，
而是**盘上原版 automap 家族素材的实测像素** —— 本脚本就是那次量的**可复现命令行**：

    python tools/probes/measure/w6_automap_palette.py

它做两件事：
  ① 打印 `D2/UI/Banner/{automap_0,automap_1,AutoMapCenter_0,AutoMapParty_0,AutoMapOptions_0}.png`
     （原版 `data/local/ui/chi/*.dc6`）与 `D2/UI/MiniMap/mapicon_{0..7}.png`
     （原版 `MINIMAP/mapicons.DC6`）的**像素直方图**（前 N 色 + 不透明比例）；
  ② **自断言**：三个被引用的色值必须真的出现在原版素材里（否则退出码 1）——
     颜色被改动而注释没跟着改时，这条会红。

背景（结论，详见 `client/Assets/Scripts/UI/MiniMapPanel.cs` 文件头与 `策划/验收表.md` 的 E23/E25）：
  · 原版 automap 的**图块表** `data/global/ui/AUTOMAP/{MaxiMap,Act2Map,Act4Map}.dc6`
    与逐格 Cel 表 `AutoMap.txt` **在本机不存在**（`<仓库根>/原版资源/` 目录不存在）⇒
    拿不到原版 blit 口径的素材；能用的只有上面那两组「横幅 + 标记图标」。
  · 所以现行画法 = 用**原版 automap 家族自己的配色**做"暗底 + 小点/小色块"的程序化近似。
"""

import collections
import os
import sys

from PIL import Image

# 本文件在 <仓库根>/tools/probes/measure/ ⇒ 往上是 4 层才到仓库根
_HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(os.path.dirname(_HERE)))
UI = os.path.join(REPO, "client", "Assets", "Resources", "Clover", "D2", "UI")

try:                       # 控制台按 UTF-8 打中文（Windows 默认 cp936 会乱码）
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

if not os.path.isdir(UI):
    print("找不到原版 UI 素材目录：%s" % UI)
    sys.exit(2)

BANNERS = ["automap_0", "automap_1", "AutoMapCenter_0", "AutoMapParty_0", "AutoMapOptions_0"]

# MiniMapPanel 引用/注释里的三个色值（RGB）→ 必须能在原版素材里找到
CLAIMED = {
    (0x1C, 0x1C, 0x1C): "MiniMapPanel.ColBackdrop（暗底）",
    (0x48, 0x48, 0x48): "MiniMapPanel.ColFloor（可行走小点）",
    (0xC4, 0xC4, 0xC4): "MiniMapPanel.ColWallLine（阻挡小色块 / 出入口色块）",
}


def hist(path, top=12):
    if not os.path.exists(path):
        print("MISS %s" % path)
        return None
    im = Image.open(path).convert("RGBA")
    px = list(im.getdata())
    cnt = collections.Counter(px)
    n = len(px)
    op = [c for c in px if c[3] > 0]
    print("== %s  size=%s  unique=%d  不透明=%d/%d(%.1f%%)"
          % (os.path.relpath(path, UI), im.size, len(cnt), len(op), n, 100.0 * len(op) / n))
    for c, k in cnt.most_common(top):
        print("   #%02X%02X%02X a=%3d  n=%6d  %.1f%%" % (c[0], c[1], c[2], c[3], k, 100.0 * k / n))
    return set((c[0], c[1], c[2]) for c in op)


def main():
    found = set()
    for name in BANNERS:
        s = hist(os.path.join(UI, "Banner", name + ".png"))
        if s:
            found |= s
        print()

    icons = 0
    icon_colors = set()
    for i in range(8):
        p = os.path.join(UI, "MiniMap", "mapicon_%d.png" % i)
        if not os.path.exists(p):
            print("MISS %s" % p)
            continue
        icons += 1
        icon_colors |= hist(p, top=3) or set()
    print("\n8 帧 mapicon 的唯一色集合 = %s" % sorted("#%02X%02X%02X" % c for c in icon_colors))

    print("\n── 自断言：MiniMapPanel 引用的三个色值必须来自原版素材的实测像素 ──")
    fail = 0
    for rgb, where in CLAIMED.items():
        ok = rgb in found
        if not ok:
            fail += 1
        print("%s #%02X%02X%02X  %s" % ("[ OK ]" if ok else "[FAIL]", rgb[0], rgb[1], rgb[2], where))
    if icons != 8:
        fail += 1
        print("[FAIL] mapicon 帧数 = %d（应为 8）" % icons)

    print("\n结论：%s" % ("三个色值全部来自盘上原版 automap 素材的实测像素" if fail == 0
                         else "有 %d 项不成立 —— 配色注释与素材脱节，必须修" % fail))
    return 1 if fail else 0


if __name__ == "__main__":
    sys.exit(main())
