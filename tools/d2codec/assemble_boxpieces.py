# -*- coding: utf-8 -*-
"""把原版 `MENU/boxpieces.DC6` 的 22 个拼装块拼成**整幅窗框 PNG**（本项目新增的资产生成步）。

为什么需要本脚本：`export_d2ui.py` 的 `menu` 组只做**逐帧 1:1 落位**（22 帧各自一个 PNG），
而「22 帧怎么摆」需要 DC6 帧 offset —— 本机**没有** `原版资源/`（被 .gitignore 排除），
那层 offset 拿不到。本脚本用**像素自证**把它反推出来，并把结果落成整幅 PNG 供面板直接使用。

────────────────────────────────────────────────────────────────────────────
拼装口径（**全部从 PNG 实测得出，不是估的**；判据 = 「拼出来逐像素接缝连续 + 外沿是矩形」）
────────────────────────────────────────────────────────────────────────────
每帧实测 14×15，且每帧的**内容**是内部 12×12、四周页边 = 左 1 / 上 1 / 右 1 / 下 2
（x=0、x=13、y=0、y=13、y=14 全透明 —— 逐像素实测；⛔ 原一次性脚本 `.ai-tmp/test/an_box2.py` **已删、在盘无替代** ⇒ 那张逐像素表不可复跑；**现行复检** = 本文件 `self_check()`（退出码 0 = 自证通过）+ `tools/probes/hosts/uicheck` 的 `BoxFrameSide()`（`tools/probes/hosts/uicheck/W3GameCheck.cs:430`，**重新解像素**核偏移/接缝；跑法 `powershell -File tools/probes/hosts/run_all_hosts.ps1`）+ 证据图 `.ai-tmp/screenshots/w5_boxframes.png`）。
⇒ **单元格 pitch = 12**（横竖同值）。

四族（按内容形状 + 边带朝向分类，逐帧实测）：

  角块 4 个：`#0` 左上 / `#1` 右上 / `#8` 左下 / `#9` 右下
      内容 12×12；边带（2px 石色 + 1px 近黑内线）压在**外沿**（左上块 = x1..3 与 y1..3）
  上边 6 个：`#2..#7`   内容 12×3，边带在 **y1..3**（与外沿同页边）
  下边 6 个：`#16..#21` 内容 12×3，边带在 **y1..3** ⇒ 相对下外沿要 **dy = +9**
  左边 3 个：`#10..#12` 内容 3×12，边带在 **x5..7**（= 帧内居中）⇒ 相对左外沿要 **dx = -4**
  右边 3 个：`#13..#15` 内容 3×12，边带在 **x5..7**（近黑内线在 x5、石色在 x6,x7）
                        ⇒ 相对右外沿（角块 x10..12）要 **dx = +5**

**为什么这样定**（唯一能成立的读法，穷举过）：
  · 角块与上/下边块三族**一致地在"外沿 + 1px 页边"处画边带**（x1..3 / y1..3）；
    只有左/右边块把边带画在帧内**居中**（x5..7）⇒ 需要偏移的是左/右边块。
  · 反过来（把角块右移 +4 去就边块）会让**上边带比左边带多伸出去 4px**（角就破了），
    拼出来的外沿**不是矩形** ⇒ 排除。
  · 实测：按本口径拼出的整幅，四角 12×12 **逐像素等于** 4 个角块素材的内容区，
    且每条内部接缝的"不透明状态不一致像素"= 0、四条边带 0 空洞、空腔 0 杂点。

**变体怎么取**（⛔ 不许自己挑装饰排布）：上/下/左/右各 6/6/3/3 个变体**只差石纹与金色饰点**，
「哪个变体放哪一格」在原版里没有出处（DC6 offset 表不在本机）⇒ **一律取该族的第一个变体**
（上 `#2` / 下 `#16` / 左 `#10` / 右 `#13`），其余 14 帧**登记为未使用**（装饰变体）。

用法：
    python tools/d2codec/assemble_boxpieces.py            # 重建面板要用的两个尺寸
    python tools/d2codec/assemble_boxpieces.py 36 29      # 指定 (cols, rows) 单元格数
只依赖 Pillow。退出码 0 = 自证通过；1 = 自证失败（接缝有洞 / 角块对不上）。
"""
import os
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Menu")
DST_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "UI", "Panel")
SHOT_DIR = os.path.join(ROOT, ".ai-tmp", "screenshots")

# ── 实测常量（口径见文件头；改这里 = 改契约，必须重跑本脚本的自证）─────────────
TILE_PX = 12              # 单元格 pitch（横竖同值）
FRAME_W, FRAME_H = 14, 15  # 每一帧的像素尺寸（逐帧 IHDR 实测）
CONTENT = (1, 1, 13, 13)   # 每帧内容区（左, 上, 右, 下）半开区间 —— 12×12
BORDER_THICKNESS = 4       # 窗框外沿 → 空腔的厚度（原版px）= 3px 边带 + 1px 页边

TILE_TL, TILE_TR, TILE_BL, TILE_BR = 0, 1, 8, 9
TILE_TOP, TILE_BOTTOM, TILE_LEFT, TILE_RIGHT = 2, 16, 10, 13

# 各族相对"单元格左上角"的偏移（原版px）
OFF_TOP = (0, 0)
OFF_BOTTOM = (0, 9)
OFF_LEFT = (-4, 0)
OFF_RIGHT = (5, 0)
# 整幅原点平移：让窗框**外沿**落在图像的第 0 列 / 第 0 行（角块的外沿在帧内 1..3）
ORIGIN = (-1, -1)

# 面板要用的两个尺寸（单元格数）——与 `UI/UiLayoutFlow.cs` 的 BoxSize **逐值对应**
#   settings：432×348 原版px =（内容 420×335 + 2×4 框厚 = 428×343）吸附 12 网格
#   pause   ：288×180 原版px =（4 个 WideButton 外接框 272×170 + 2×4 = 280×178）吸附 12 网格
#   ⚠️ 文件名**不带尺寸**（角色名）⇒ 尺寸的唯一来源是 `UiLayoutFlow.BoxFrame` 那两个常量，
#      两者是否一致由 `uicheck` ㉑ 节按 IHDR 断言（不一致必红）。
SIZES = [("boxframe_settings", 36, 29), ("boxframe_pause", 24, 15)]


def tile(i):
    return Image.open(os.path.join(SRC_DIR, "boxpieces_%d.png" % i)).convert("RGBA")


def plan(cols, rows):
    """列出 (帧号, dx, dy, 列, 行)。"""
    cells = []
    for j in range(rows):
        for k in range(cols):
            if (k, j) == (0, 0):
                cells.append((TILE_TL, 0, 0, k, j))
            elif (k, j) == (cols - 1, 0):
                cells.append((TILE_TR, 0, 0, k, j))
            elif (k, j) == (0, rows - 1):
                cells.append((TILE_BL, 0, 0, k, j))
            elif (k, j) == (cols - 1, rows - 1):
                cells.append((TILE_BR, 0, 0, k, j))
            elif j == 0:
                cells.append((TILE_TOP, OFF_TOP[0], OFF_TOP[1], k, j))
            elif j == rows - 1:
                cells.append((TILE_BOTTOM, OFF_BOTTOM[0], OFF_BOTTOM[1], k, j))
            elif k == 0:
                cells.append((TILE_LEFT, OFF_LEFT[0], OFF_LEFT[1], k, j))
            elif k == cols - 1:
                cells.append((TILE_RIGHT, OFF_RIGHT[0], OFF_RIGHT[1], k, j))
            # 空腔：不填
    return cells


def assemble(cols, rows):
    W, H = cols * TILE_PX, rows * TILE_PX
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    for idx, dx, dy, k, j in plan(cols, rows):
        out.alpha_composite(tile(idx), (k * TILE_PX + dx + ORIGIN[0],
                                       j * TILE_PX + dy + ORIGIN[1]))
    return out


# ── 自证（三条判据，全部逐像素）──────────────────────────────────────────────
def prove(im, cols, rows, name):
    W, H = im.size
    px = im.load()
    errs = []

    # ① 内部接缝连续：缝两侧"不透明与否"不一致的像素数必须为 0
    for k in range(1, cols):
        n = sum(1 for y in range(H) if (px[k * TILE_PX - 1, y][3] > 0) != (px[k * TILE_PX, y][3] > 0))
        if n:
            errs.append("竖缝 x=%d 有 %d 个不一致像素" % (k * TILE_PX, n))
    for j in range(1, rows):
        n = sum(1 for x in range(W) if (px[x, j * TILE_PX - 1][3] > 0) != (px[x, j * TILE_PX][3] > 0))
        if n:
            errs.append("横缝 y=%d 有 %d 个不一致像素" % (j * TILE_PX, n))

    # ② 四条边带无空洞（0..2 行/列 必须逐像素不透明）
    for label, coords in (
            ("上", [(x, y) for x in range(W) for y in range(3)]),
            ("下", [(x, y) for x in range(W) for y in range(H - 3, H)]),
            ("左", [(x, y) for y in range(H) for x in range(3)]),
            ("右", [(x, y) for y in range(H) for x in range(W - 3, W)])):
        n = sum(1 for c in coords if px[c][3] == 0)
        if n:
            errs.append("%s边带有 %d 个透明空洞" % (label, n))

    # ③ 四角 12×12 逐像素 == 4 个角块素材的内容区（证明角块来自原版素材、没被错位）
    for label, idx, xy in (("左上", TILE_TL, (0, 0)), ("右上", TILE_TR, (W - 12, 0)),
                           ("左下", TILE_BL, (0, H - 12)), ("右下", TILE_BR, (W - 12, H - 12))):
        want = list(tile(idx).crop(CONTENT).getdata())
        got = [px[xy[0] + x, xy[1] + y] for y in range(12) for x in range(12)]
        if want != got:
            errs.append("%s 角 12×12 与 boxpieces_%d 内容区不一致" % (label, idx))

    # ④ 空腔必须全透明（窗框里面不许有像素）
    n = sum(1 for y in range(BORDER_THICKNESS, H - BORDER_THICKNESS)
            for x in range(BORDER_THICKNESS, W - BORDER_THICKNESS) if px[x, y][3] > 0)
    if n:
        errs.append("空腔里有 %d 个非透明像素" % n)

    print("  [%s] %d×%d（%d×%d 格） 自证：%s"
          % (name, W, H, cols, rows, "通过" if not errs else "失败 -> " + "；".join(errs)))
    return errs


def flat(im):
    """透明画成洋红（人眼一眼看"框有没有断 / 角是不是干净"）。"""
    return Image.alpha_composite(Image.new("RGBA", im.size, (255, 0, 255, 255)), im)


def contact_sheet(images, path):
    """拼一张证据图（1:1 整幅）。"""
    pad, label_h = 12, 14
    W = sum(im.size[0] + pad for im in images) + pad
    H = max(im.size[1] for im in images) + pad * 2 + label_h
    sheet = Image.new("RGBA", (W, H), (24, 24, 28, 255))
    x = pad
    for im in images:
        sheet.alpha_composite(flat(im), (x, pad + label_h))
        x += im.size[0] + pad
    sheet.save(path)
    print("  证据图（1:1 整幅）：%s" % path)


def zoom_sheet(images, path, z=8):
    """四角 + 上下左右各一条边**放大 z 倍**的证据图（1:1 太小看不清 3px 边带是否连续）。"""
    views = []
    for name, im in zip([s[0] for s in SIZES] if len(images) == len(SIZES) else
                        ["box%d" % i for i in range(len(images))], images):
        w, h = im.size
        f = flat(im)
        boxes = [("TL", (0, 0, 26, 26)), ("TR", (w - 26, 0, w, 26)),
                 ("BL", (0, h - 26, 26, h)), ("BR", (w - 26, h - 26, w, h)),
                 ("top", (w // 2 - 13, 0, w // 2 + 13, 14)),
                 ("left", (0, h // 2 - 13, 14, h // 2 + 13))]
        for label, box in boxes:
            t = f.crop(box).resize(((box[2] - box[0]) * z, (box[3] - box[1]) * z), Image.NEAREST)
            views.append(t)
    cols = 6
    rows = (len(views) + cols - 1) // cols
    cw = max(v.size[0] for v in views) + 10
    ch = max(v.size[1] for v in views) + 10
    sheet = Image.new("RGBA", (cols * cw + 10, rows * ch + 10), (24, 24, 28, 255))
    for i, v in enumerate(views):
        sheet.alpha_composite(v, (10 + (i % cols) * cw, 10 + (i // cols) * ch))
    sheet.save(path)
    print("  证据图（四角/四边 ×%d 放大）：%s" % (z, path))


def main(argv):
    if not os.path.isdir(SRC_DIR):
        print("MISSING  %s" % SRC_DIR)
        return 2

    sizes = SIZES
    if len(argv) >= 3:
        sizes = [("custom", int(argv[1]), int(argv[2]))]

    os.makedirs(DST_DIR, exist_ok=True)
    made, errs = [], []
    for name, cols, rows in sizes:
        im = assemble(cols, rows)
        path = os.path.join(DST_DIR, name + ".png")
        im.save(path)
        print("  写出 %s（%d×%d）" % (path, im.size[0], im.size[1]))
        errs += prove(im, cols, rows, name)
        made.append(im)

    os.makedirs(SHOT_DIR, exist_ok=True)
    contact_sheet(made, os.path.join(SHOT_DIR, "w5_boxframes.png"))
    zoom_sheet(made, os.path.join(SHOT_DIR, "w5_boxframes_zoom.png"))

    print("自证结论：%s" % ("全部通过" if not errs else "失败 %d 条" % len(errs)))
    return 0 if not errs else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
