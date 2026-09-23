# -*- coding: utf-8 -*-
"""
t0b_d11_sheet.py -- 判据资产：把本片 D11 输入的 69 行实机读数汇总成
  ① 一张联络图 .ai-tmp/screenshots/t0b_d11_contact.png（格号 + 格上实测值）
  ② 一份 格号 <-> 矩阵行号 索引 .ai-tmp/screenshots/t0b_d11_contact.index.tsv

判据来源 = 本片 1 次 Play 的冻结证据 .ai-tmp/screenshots/t0b_evidence.txt 的 [T0B] D11=ctx=... 行。
格号规则：<CTX><NN>（MENU01 / DLG01 / SHOP01 …，NN = 别名序号 01..23，与矩阵实体同序）。

用法：python tools/probes/measure/t0b_d11_sheet.py
"""
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(HERE))))
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")
EVID = os.path.join(SHOTS, "t0b_evidence.txt")
MATRIX = os.path.join(ROOT, "策划", "状态矩阵.tsv")
PNG = os.path.join(SHOTS, "t0b_d11_contact.png")
TSV = os.path.join(SHOTS, "t0b_d11_contact.index.tsv")

CTX_ORDER = [("menu", "MENU"), ("dialog", "DLG"), ("shop", "SHOP")]
CTX_FULL = {"menu": "菜单上下文（MainMenu/CharSelect）", "dialog": "对话上下文（DialogOpen）",
            "shop": "商店/面板上下文"}
BOUND = {"menu": "菜单内该键应被屏蔽", "dialog": "对话中移动指令应被吞", "shop": "面板内点地面不应移动"}


def evidence():
    txt = open(EVID, "rb").read().decode("utf-8-sig", "replace")
    out = []
    for l in txt.replace("\r\n", "\n").split("\n"):
        i = l.find("[T0B] ")
        if i < 0:
            continue
        s = l[i + len("[T0B] "):].strip()
        if s.startswith("D11=ctx="):
            out.append(s)
    return out


def parse(lines):
    recs = {}
    for l in lines:
        m = re.search(r'ctx=(\w+) entity=(\S+) key=(\S+) eff=(\d+) swallowed=(\d+) move=(\d+) '
                      r'p0=(\S+) p1=(\S+) f0=(\S+) f1=(\S+) g0=(\S+) g1=(\S+) dm=(-?\d+) '
                      r'r0=(-?\d+) r1=(-?\d+) db=(-?\d+) ds=(-?\d+)', l)
        if not m:
            continue
        recs[(m.group(1), m.group(2))] = dict(ctx=m.group(1), entity=m.group(2), key=m.group(3),
                                              eff=int(m.group(4)), sw=int(m.group(5)), mv=int(m.group(6)),
                                              p0=m.group(7), p1=m.group(8), dm=int(m.group(13)))
    return recs


def matrix_lines():
    """entity -> 1-based physical line number, for D11输入 rows."""
    raw = open(MATRIX, "rb").read().decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in raw else "\n"
    out = {}
    for i, l in enumerate(raw.split(nl), 1):
        c = l.split("\t")
        if len(c) >= 8 and c[0] == "D11输入":
            out.setdefault((c[1], c[2]), i)
    return out


def main():
    recs = parse(evidence())
    ml = matrix_lines()
    # order of aliases = the order they first appear in the evidence (== matrix order)
    order = []
    for (ctx, ent) in recs:
        if ent not in order:
            order.append(ent)
    order.sort(key=lambda e: ml.get((e, CTX_FULL["menu"]), 10 ** 9))
    print("aliases =", len(order), " records =", len(recs))

    rows = []
    for ck, cpref in CTX_ORDER:
        for i, ent in enumerate(order, 1):
            r = recs.get((ck, ent))
            cell = "%s%02d" % (cpref, i)
            if r is None:
                rows.append((cell, ck, ent, "", "", 0, 0, 0, "MISSING"))
                continue
            if ck == "menu":
                v = "一致" if r["eff"] == 0 else "不一致"
            else:
                v = "一致" if r["mv"] == 0 else "不一致"
            line = ml.get((ent, CTX_FULL[ck]), "")
            rows.append((cell, ck, ent, r["key"], ("L%s" % line if line else ""),
                         r["eff"], r["sw"], r["mv"], v))

    with open(TSV, "w", encoding="utf-8", newline="") as fh:
        fh.write("# 格号<->矩阵行号 索引（本片 D11 输入 69 行；证据 .ai-tmp/screenshots/t0b_evidence.txt 的 [T0B] D11=ctx= 行）\n")
        fh.write("cell\tctx\tentity\tinput_key\tmatrix_line\teff\tswallowed\tmove\tverdict\tboundary\n")
        for (cell, ck, ent, key, line, eff, sw, mv, v) in rows:
            fh.write("\t".join([cell, ck, ent, key, line, str(eff), str(sw), str(mv), v, BOUND[ck]]) + "\n")
    print("wrote", TSV, "rows =", len(rows))

    # ---- contact sheet ----
    try:
        from PIL import Image, ImageDraw, ImageFont
    except Exception as e:
        print("!! PIL unavailable:", e)
        return 0

    def font(sz):
        # CJK-capable first (the entity names + the boundary text carry Chinese)
        for p in ("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/msyhl.ttc",
                  "C:/Windows/Fonts/simhei.ttf", "C:/Windows/Fonts/simsun.ttc",
                  "C:/Windows/Fonts/arial.ttf"):
            if os.path.exists(p):
                try:
                    return ImageFont.truetype(p, sz)
                except Exception:
                    pass
        return ImageFont.load_default()

    f_title = font(30)
    f_hdr = font(20)
    f_cell = font(15)
    f_med = font(17)

    cw, ch = 470, 34
    left, top = 20, 118
    cols = 3
    W = left * 2 + cols * cw + (cols - 1) * 10 + 10
    H = top + 40 + len(order) * ch + 90
    img = Image.new("RGB", (W, H), (24, 24, 30))
    d = ImageDraw.Draw(img)

    d.text((left, 18), "T0B D11 input contact sheet  --  23 aliases x 3 contexts, REAL key injection in ONE Play session",
           font=f_title, fill=(235, 235, 240))
    d.text((left, 56), "source: .ai-tmp/screenshots/t0b_evidence.txt  [T0B] D11=ctx=... lines   |   green = 一致 (verdict), red = 不一致",
           font=f_hdr, fill=(170, 175, 190))
    d.text((left, 80), "cell = <CTX><NN>   eff = any observable state change (panel set / fsm / grid / run-toggle / MoveCommand / belt / swap)   move = MoveCommand or grid change",
           font=f_cell, fill=(140, 145, 160))

    xs = {}
    for ci, (ck, cpref) in enumerate(CTX_ORDER):
        x = left + ci * (cw + 10)
        xs[ck] = x
        d.rectangle([x, top - 34, x + cw, top - 4], fill=(48, 52, 66))
        d.text((x + 8, top - 30), "%s  ctx=%s  -- %s" % (cpref, ck, BOUND[ck]), font=f_hdr, fill=(240, 240, 250))

    for ri, ent in enumerate(order):
        y = top + ri * ch
        for ci, (ck, cpref) in enumerate(CTX_ORDER):
            x = xs[ck]
            r = recs.get((ck, ent))
            if r is None:
                d.rectangle([x, y, x + cw, y + ch - 3], fill=(80, 20, 20))
                d.text((x + 6, y + 7), "MISSING", font=f_cell, fill=(255, 200, 200))
                continue
            if ck == "menu":
                ok = r["eff"] == 0
            else:
                ok = r["mv"] == 0
            bg = (26, 60, 34) if ok else (86, 26, 26)
            d.rectangle([x, y, x + cw, y + ch - 3], fill=bg)
            cell = "%s%02d" % (cpref, ri + 1)
            txt = "%s %-30s key=%-10s E%d S%d M%d" % (cell, ent, r["key"], r["eff"], r["sw"], r["mv"])
            d.text((x + 6, y + 8), txt, font=f_cell, fill=(228, 240, 228) if ok else (255, 214, 214))

    # footer stats
    fy = top + len(order) * ch + 16
    per = {}
    for (cell, ck, ent, key, line, eff, sw, mv, v) in rows:
        per.setdefault(ck, [0, 0])
        per[ck][0] += 1
        if v != "一致":
            per[ck][1] += 1
    parts = []
    for ck, _ in CTX_ORDER:
        n, bad = per.get(ck, [0, 0])
        parts.append("%s: n=%d 不一致=%d" % (ck, n, bad))
    d.text((left, fy), "   |   ".join(parts) + "   ||  total n=%d" % len(rows), font=f_med, fill=(230, 230, 240))
    d.text((left, fy + 26), "evidence lines = %d  (every cell traces to one [T0B] D11=ctx= log line);  index = .ai-tmp/screenshots/t0b_d11_contact.index.tsv"
           % len(recs), font=f_cell, fill=(160, 165, 180))

    img.save(PNG)
    print("wrote", PNG, img.size)
    return 0


if __name__ == "__main__":
    sys.exit(main())
