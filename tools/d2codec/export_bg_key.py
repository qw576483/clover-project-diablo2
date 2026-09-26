# -*- coding: utf-8 -*-
"""按「帧的背景色号」重导 PNG：把 DC6 帧里那一整片背景色键成透明。

背景（D2 的 DC6 没有 alpha 通道，透明靠 RLE 的「跳过 N 像素」表达 —— 被跳过的位置留索引 0）。
**少数原版件的背景不是索引 0**：整帧被涂成另一色号铺满，RLE 里一个跳过指令都没有。
按既有的「索引 0 = 透明」口径导出，那块底色会连图案一起变成不透明 ⇒ 屏上出现一块**矩形/方块底色**。
本工具对这些件按 `dc6.background_index()`（见 `dc6.py` §背景索引：先试 0、不满足才反推、
多候选抛错）**现算**该色号并键成透明。⛔ 不写死色号。

**登记表驱动**：`background_index()` 只看「某一色号是否铺满四边多数」，它在
「整片暗色底图 / 笔画的抖色填充」上同样成立（实测：`MENU/buttontempok.DC6` 的 #176、
`MENU/EndGame.dc6` 的 #33）—— 那些色号是**画面自己**，键掉会打洞、会毁图。
所以**哪些件该键透明是逐件裁决的**，落在下面的 `REGISTRY`；工具只负责按同一口径把登记过的件
重导、并在色号对不上时**拒绝写盘**（⛔ 不猜）。

每个件用哪张 PL2 与 `export_d2ui.py` 的对应组一致（本文件不另立口径）。

用法：
  python export_bg_key.py <d2dc6 根> <client 目录>            # 重导 REGISTRY 里 write=True 的行
  python export_bg_key.py <d2dc6 根> <client 目录> --check    # 只比对，不写盘
  python export_bg_key.py <d2dc6 根> <client 目录> --scan     # 扫全部 DC6 的逐帧背景色号（只报不写）
  python export_bg_key.py <d2dc6 根> <client 目录> --audit    # 覆盖率对账：登记表 vs 生成器清单 vs 在盘件

`--audit` 的「在盘但生成器不产」一桶**不是缺件清单**，它同时装着：
  · 图集供件的件 —— `UI/Panel/overlap.png`（256×128 = 2 块 82×88 + 透明分隔列，`spriteMode: 2`
    切出子 sprite `overlap_0`/`overlap_1`，取件走整条 `LoadAll<Sprite>`；**不产单帧**）；
  · 二阶产物 —— `UI/Panel/boxframe_*.png`（← `assemble_boxpieces.py` ← 生成器产的 `UI/Menu/boxpieces_*.png`）；
  · 参考工程 `ThirdParty/Diablo2/**` 的"原样副本"（不经本工程 DC6 管线）。
判「缺件」要看的是「生成器清单 vs 在盘」那一行（`缺失 N`），不是这一桶。

**「无生成器、但已有出处判定」的件**落在 `KNOWN_ORIGINS`，`--audit` 每次重算它们的判据（见下）。
其中一条判据是**透明键指纹**：`UI/Cursor/Cursor.png` 的全透明像素底下是 `(0,255,255)`（青），
`UI/Cursor/Gaunt.png` 是 `(0,0,0)` —— 同一张原版图经两套导出器产出、渲染上完全一致，
差异只在**双方都全透明**的像素底下的 RGB（`ohand.dc6` 帧 0 与 `Cursor.png`：可见 376 像素逐值一致、
alpha 判定差 0；456 个差异全在这些不可见像素上，与调色板无关 —— 盘上 5 张调色板的**可见**差异全 0）。
本模块的 `frame_rgba` 把透明像素恒写 `(0,0,0,0)` ⇒ 产不出青键，所以 `Cursor.png` 保留参考工程副本，
`CURSOR/Gaunt.dc6` 才进 `export_d2ui.py` 的 `cursor` 组。
"""

import os
import sys
from collections import deque

import dc6

SRC_ROOT = None      # <d2dc6 根>
DST_ROOT = None      # <工程>\client
PL2_ROOT = None      # <d2raw>\data\global\palette

PL2_ACT1 = "ACT1/Pal.PL2"
PL2_FECHAR = "fechar/Pal.PL2"

# (源 DC6（相对 <d2dc6 根>）, PL2, 帧号, 工程内输出（相对 <client>/Assets/Resources/Clover/D2）,
#  write, 备注)
#
#   write=False 的行 = **口径自证**：工程里那份已经是对的，本工具必须能逐像素重现它；
#   重现不出 ⇒ 报 FAIL（说明两处口径不一致，先去查清楚，⛔ 不许覆盖）。
REGISTRY = [
    # 319×177「DIABLO II 火焰字标」：整帧铺满索引 15（#0C0C08），RLE 无跳过指令。
    # 白键透明的原因：启动屏底色是 #00000D，与这片的 #0C0C08 不同 ⇒ 矩形边界可见。
    # 图案用的色号是 2/3/4/6/9/11/12/16/17/31/32（取样实测），不含 15。
    ("data/global/ui/Logo/logo.DC6", PL2_FECHAR, 0, "UI/Logo/logo_0.png", True,
     "DIABLO II 火焰字标"),
    # 34×30 攻击态光标（`ResPaths.CursorAttack`）：同一类，背景索引同为 15。
    # 正典生成器 = `export_d2ui.py --only cursor`（该组走同一口径）；write=False ⇒ 本工具只复算自证。
    ("data/global/ui/CURSOR/Gaunt.dc6", PL2_ACT1, 0, "UI/Cursor/Gaunt.png", False,
     "攻击态光标（口径自证；正典生成器 = export_d2ui.py --only cursor）"),
]


# 「无生成器、已有出处判定」的件：登记的是**可复核的判据**（`--audit` 每次重算），不是一句备注。
# `hole_rgb` = 该件**全透明像素底下的 RGB 集合** —— 导出器指纹，渲染上不可见。
KNOWN_ORIGINS = (
    {
        "path": "UI/Cursor/Cursor.png",
        "origin": "参考工程（Diablerie）原样副本",
        "hole_rgb": ((0, 255, 255),),
        "note": "可见内容 == 原版 `data/global/ui/CURSOR/ohand.dc6` 帧 0 直出（5 张调色板可见差异全 0、"
                "376 个可见像素逐值一致、alpha 判定差 0）；456 个差异全在双方都全透明的像素底下的 RGB",
    },
    {
        "path": "UI/Cursor/Gaunt.png",
        "origin": "本工程 DC6 直出（`export_d2ui.py --only cursor`，背景色号 15 键透明）",
        "hole_rgb": ((0, 0, 0),),
        "note": "== 原版 `data/global/ui/CURSOR/Gaunt.dc6` 帧 0（34×30 单帧）",
    },
)


def _hole_rgb_set(path):
    """件里**全透明像素底下的 RGB 集合**（导出器指纹）。需要 PIL；没有就返回 `(None, 'PIL-MISSING')`。"""
    try:
        from PIL import Image
    except ImportError:
        return None, "PIL-MISSING"
    im = Image.open(path).convert("RGBA")
    return set(p[:3] for p in im.getdata() if p[3] == 0), "PIL"


def _bg_components(frame, bg, min_size=2):
    """`bg` 色号像素的 4-邻接连通分量大小（降序）。用于报告「这片底色是否成片」。"""
    w, h, idx = frame.width, frame.height, frame.indices
    seen = bytearray(w * h)
    sizes = []
    for start in range(w * h):
        if seen[start] or idx[start] != bg:
            continue
        q = deque([start])
        seen[start] = 1
        n = 0
        while q:
            c = q.popleft()
            n += 1
            y, x = divmod(c, w)
            if x > 0 and not seen[c - 1] and idx[c - 1] == bg:
                seen[c - 1] = 1
                q.append(c - 1)
            if x + 1 < w and not seen[c + 1] and idx[c + 1] == bg:
                seen[c + 1] = 1
                q.append(c + 1)
            if y > 0 and not seen[c - w] and idx[c - w] == bg:
                seen[c - w] = 1
                q.append(c - w)
            if y + 1 < h and not seen[c + w] and idx[c + w] == bg:
                seen[c + w] = 1
                q.append(c + w)
        if n >= min_size:
            sizes.append(n)
    sizes.sort(reverse=True)
    return sizes


def _read_rgba(path):
    """读 RGBA PNG（借用 `dc6._read_png_rgba`；只支持本工具与 `export_d2ui.py` 写出的那种）。"""
    return dc6._read_png_rgba(path)


def _transparent_count(rgba):
    return sum(1 for i in range(3, len(rgba), 4) if rgba[i] == 0)


def run(check_only=False):
    pal_cache = {}
    fails = 0
    for rel, pl2, fi, out_rel, write, note in REGISTRY:
        src_path = os.path.join(SRC_ROOT, *rel.split("/"))
        out_path = os.path.join(DST_ROOT, "Assets", "Resources", "Clover", "D2",
                                *out_rel.split("/"))
        print("── %s  帧 %d → %s（%s）" % (rel, fi, out_rel, note))
        if not os.path.exists(src_path):
            print("   FAIL 源件不存在：%s" % src_path)
            fails += 1
            continue
        d = dc6.parse(open(src_path, "rb").read())
        if fi >= len(d.frames):
            print("   FAIL 帧号 %d 越界（源只有 %d 帧）" % (fi, len(d.frames)))
            fails += 1
            continue
        f = d.frames[fi]
        if pl2 not in pal_cache:
            pal_cache[pl2] = dc6.read_pl2(os.path.join(PL2_ROOT, *pl2.split("/")))
        pal = pal_cache[pl2]

        try:
            bg = dc6.background_index(f)
        except ValueError as ex:
            print("   FAIL 背景色号多候选 ⇒ 不猜：%s" % ex)
            fails += 1
            continue
        if bg is None:
            print("   FAIL 判不出背景色号（连索引 0 也不满足判据）⇒ 本件不该用本工具")
            fails += 1
            continue
        if bg == 0:
            print("   FAIL 判出的背景色号是 0 ⇒ 本件不属于本工具的范围（既有口径已对）")
            fails += 1
            continue

        sizes = _bg_components(f, bg)
        n_bg = sum(1 for v in f.indices if v == bg)
        kept = f.width * f.height - n_bg
        rgba = bytes(dc6.frame_rgba(f, pal, background=bg))
        tr = _transparent_count(rgba)
        print("   %dx%d  背景色号=%d(rgb=%s) 占 %d/%d=%.1f%%；连通分量 %d 个（最大 %.1f%% 的底色）；"
              "键后不透明 %d/%d=%.1f%%"
              % (f.width, f.height, bg, pal[bg][:3], n_bg, f.width * f.height,
                 100.0 * n_bg / (f.width * f.height), len(sizes),
                 100.0 * sizes[0] / n_bg if sizes else 0.0,
                 kept, f.width * f.height, 100.0 * kept / (f.width * f.height)))
        if tr != n_bg:
            print("   FAIL 键掉的像素数 %d != 背景色号像素数 %d" % (tr, n_bg))
            fails += 1
            continue

        if check_only or not write:
            if not os.path.exists(out_path):
                print("   FAIL 工程内件不存在，无法比对：%s" % out_path)
                fails += 1
                continue
            gw, gh, got = _read_rgba(out_path)
            if (gw, gh) != (f.width, f.height):
                print("   FAIL 尺寸不符：工程 %dx%d vs 源帧 %dx%d" % (gw, gh, f.width, f.height))
                fails += 1
                continue
            diff = 0
            for i in range(0, len(rgba), 4):
                if rgba[i:i + 4] != got[i:i + 4]:
                    diff += 1
            tag = "CHECK" if check_only else "VERIFY"
            print("   %s 与工程内件逐像素比：差异 %d/%d %s"
                  % (tag, diff, f.width * f.height, "OK" if diff == 0 else "**FAIL**"))
            if diff:
                fails += 1
            continue

        dc6.write_png_rgba(out_path, rgba, f.width, f.height)
        print("   WROTE %s（透明像素 %d → %d）" % (out_path, 0, tr))

    print("\n=== 合计 FAIL %d ===" % fails)
    return 1 if fails else 0


def scan():
    """扫 <d2dc6 根> 下全部 DC6，只报「背景色号 != 0」的帧（不写盘）。

    索引 0 满足判据 = 既有口径已对；判不出（None）= 帧里没有「铺满四边的单一底色」，
    索引 0 口径本来就对；两者都不列。多候选 = 必须人工裁决，单独列。
    """
    nfiles = nframes = n0 = nnone = 0
    hits = []
    for dirpath, _dn, fns in os.walk(SRC_ROOT):
        for fn in sorted(fns):
            if not fn.lower().endswith(".dc6"):
                continue
            nfiles += 1
            p = os.path.join(dirpath, fn)
            rel = os.path.relpath(p, SRC_ROOT).replace("\\", "/")
            d = dc6.parse(open(p, "rb").read())
            for i, f in enumerate(d.frames):
                nframes += 1
                try:
                    bg = dc6.background_index(f)
                except ValueError:
                    bg = "MULTI"
                if bg == 0:
                    n0 += 1
                elif bg is None:
                    nnone += 1
                else:
                    n = sum(1 for v in f.indices if v == bg)
                    hits.append((rel, i, f.width, f.height, bg,
                                 100.0 * n / (f.width * f.height)))
    print("DC6 %d 个 / 帧 %d 个；背景色号=0 %d 帧；判不出 %d 帧；**背景色号!=0 %d 帧**"
          % (nfiles, nframes, n0, nnone, len(hits)))
    for rel, i, w, h, bg, pct in hits:
        print("  %-56s 帧%-4d %4dx%-4d 背景色号=%-6s 占 %.1f%%" % (rel, i, w, h, bg, pct))
    return 0


def audit():
    """覆盖率对账：`REGISTRY` 登记的件 vs 生成器清单 vs 盘上件。

    "生成器清单" 取自 `verify_d2ui_export.collect()`（把 `export_d2ui` 的组函数跑一遍、拦掉写盘，
    只记它打算写什么）⇒ 与生成器同源，不是本文件另列一遍目录。
    只读，不写任何图。
    """
    import verify_d2ui_export as vfy
    import export_d2ui as ex

    ex.SRC_ROOT, ex.DST_ROOT = SRC_ROOT, DST_ROOT
    ex.RAW_ROOT = os.path.dirname(SRC_ROOT)
    ex.PL2_ROOT = PL2_ROOT
    d2 = os.path.join(DST_ROOT, "Assets", "Resources", "Clover", "D2")

    def rel(p):
        return os.path.relpath(p, d2).replace("\\", "/")

    recs = vfy.collect(None)
    registered = set(r[3] for r in REGISTRY)
    missing = [rel(r["dst"]) for r in recs if not os.path.exists(r["dst"])]

    single, atlas, unsure = [], [], []
    for r in recs:
        is_atlas = r["dst"].endswith("_chi.png")
        frames = dc6.parse(open(r["src"], "rb").read()).frames if is_atlas else [r["frame"]]
        hits, bgs = 0, set()
        for i, f in enumerate(frames):
            try:
                bg = dc6.background_index(f)
            except ValueError:
                unsure.append("%s 帧 %d" % (rel(r["dst"]), i))
                continue
            if isinstance(bg, int) and bg != 0:
                hits += 1
                bgs.add(bg)
        if not hits:
            continue
        row = (rel(r["dst"]), rel(r["src"]), sorted(bgs), hits, len(frames))
        (atlas if is_atlas else single).append(row)

    unreg = [c for c in single + atlas if c[0] not in registered]
    by_src = {}
    for c in unreg:
        by_src.setdefault(c[1], []).append(c)

    produced = set(os.path.normcase(os.path.abspath(r["dst"])) for r in recs)
    extra = []
    for sub in ("UI", "Fonts"):
        for dp, _dn, fns in os.walk(os.path.join(d2, sub)):
            for fn in fns:
                if fn.lower().endswith(".png"):
                    p = os.path.join(dp, fn)
                    if os.path.normcase(os.path.abspath(p)) not in produced:
                        extra.append(rel(p))

    print("登记 %d 条：%s" % (len(REGISTRY), ", ".join(sorted(registered))))
    print("生成器清单 %d 个产物；在盘 %d；缺失 %d %s"
          % (len(recs), len(recs) - len(missing), len(missing), missing or ""))
    print("源帧「背景色号 != 0」的产物 %d 个（已登记 %d / 未登记 %d）"
          % (len(single) + len(atlas), len(single) + len(atlas) - len(unreg), len(unreg)))
    for s in sorted(by_src):
        v = by_src[s]
        bgs = sorted(set(x for b in v for x in b[2]))
        print("   未登记 <- %-46s 背景色号=%-8s 产物 %3d 个：%s%s"
              % (s, ",".join(str(x) for x in bgs), len(v),
                 ", ".join(x[0] for x in v[:2]), " …" if len(v) > 2 else ""))
    if unsure:
        print("   多候选（MULTI，必须人工裁决）%d 个：%s" % (len(unsure), unsure[:5]))

    covered = set(e["path"] for e in KNOWN_ORIGINS)
    registered_unproduced = sorted(x for x in extra if x in covered)
    print("在盘但生成器不产（UI/ 与 Fonts/ 下）%d 个（其中 %d 个已在 KNOWN_ORIGINS 登记）：%s"
          % (len(extra), len(registered_unproduced),
             ", ".join(sorted(extra)[:12]) + (" …" if len(extra) > 12 else "")))

    print("\n现算 KNOWN_ORIGINS 的指纹判据（透明键 RGB = 全透明像素底下的 RGB，渲染不可见）")
    bad = 0
    for e in KNOWN_ORIGINS:
        p = os.path.join(d2, *e["path"].split("/"))
        if not os.path.exists(p):
            print("   %-26s MISSING" % e["path"])
            bad += 1
            continue
        holes, how = _hole_rgb_set(p)
        want = set(e["hole_rgb"])
        if holes is None:
            print("   %-26s (未复核：%s) 期望透明键 RGB=%s" % (e["path"], how, sorted(want)))
            continue
        ok = holes == want
        if not ok:
            bad += 1
        print("   %-26s 透明键 RGB=%-16s 期望=%-16s %s   %s"
              % (e["path"], sorted(holes), sorted(want), "OK" if ok else "**FAIL**", e["origin"]))
    if bad:
        print("   ⇒ 指纹复核 FAIL %d 条：登记的出处口径与盘上件已不一致，需重新裁决" % bad)

    print("\n=== 一行：登记 %d / 在盘(生成器产物) %d / 源帧背景色号!=0 未登记 %d / 在盘但生成器不产 %d ==="
          % (len(registered), len(recs) - len(missing), len(unreg), len(extra)))
    return 1 if bad else 0


def main():
    global SRC_ROOT, DST_ROOT, PL2_ROOT
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    SRC_ROOT = os.path.abspath(sys.argv[1])
    DST_ROOT = os.path.abspath(sys.argv[2])
    PL2_ROOT = os.path.join(os.path.dirname(SRC_ROOT), "d2raw", "data", "global", "palette")
    args = sys.argv[3:]
    if "--scan" in args:
        return scan()
    if "--audit" in args:
        return audit()
    return run(check_only="--check" in args)


if __name__ == "__main__":
    sys.exit(main())
