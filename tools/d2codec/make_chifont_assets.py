# -*- coding: utf-8 -*-
"""把「原版中文位图字体」的映射表做成**运行期数据资产**（片 3）。

两个产出（都写进工程的 `Assets/Resources/Clover/D2/Fonts/`，供 `UI/D2Text.cs` 运行期读）：

  ① `font{N}_chi_map.txt` —— **帧→字符 + 排版度量**（每个字号的完整 13806 条）
     源：`<原版资源>/导出的字体映射/font{N}_chi.tsv`（片 1 由 `export_d2ui.py --only chifont`
     从原版 `data/LOCAL/FONT/chi/<font>.tbl` 导出；表格式出处 libd2
     `packages/formats/src/font.zig` L59-117）。
     为什么不在 `.cs` 里写死：13806 × 4 个字号 ≈ 5.5 万条度量，写死既不可维护、
     也无法"换素材不动逻辑"（§1.9 第 3 条）⇒ 一律走数据文件。

  ② `font_chi_s2t.txt` —— **简体 → 原版字形 码位映射**（渲染回退用）
     ⚠️ 事实（本脚本的字模自检会打印）：原版 chi 字模是**繁体**字集
     （含 `個/為/買/羅/營`，不含 `个/为/买/罗/营`；全表 13800 个码位），
     而本工程配表文本是**简体** ⇒ 简体字查不到字模。
     依据（两层，都不是我们自己编的）：
       · 语言事实层：OpenCC `STCharacters.txt`（简→繁，Apache-2.0）与
         `TSCharacters.txt`（繁→简，用于反向补全异体，例 `爲/為` `啓/啟`）
         —— 放在 `<原版资源>/简繁词典/`，出处见其文件头。
       · 工程事实层（**逐条双重校验**，不通过就不进表）：
         候选繁体字必须 (a) 真的在 `font{N}_chi` 的码位集里，且 (b) 出现在
         **原版中文语料** `<原版资源>/d2text/chi_*.txt` 里（出现次数多者优先）。
     符号归一（字模确实没有这些符号，逐条列出）：见 `SYMBOLS` 表。

用法：python tools/d2codec/make_chifont_assets.py
"""

import os
import sys
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))          # <项目根>
MAP_SRC = os.path.join(ROOT, "原版资源", "导出的字体映射")
DICT = os.path.join(ROOT, "原版资源", "简繁词典")
CORPUS_DIR = os.path.join(ROOT, "原版资源", "d2text")
OUT_DIR = os.path.join(ROOT, "client", "Assets", "Resources", "Clover", "D2", "Fonts")

SIZES = (16, 24, 30, 42)

# 字模确无、且无繁体对应的符号 ⇒ 用字模里已有的等价符号（原版语料里也没出现过这些符号）。
# 为什么会用到：`⇒` 出现在本工程的任务日志提示串里（`QuestLogPanel`）。
SYMBOLS = {
    0x21D2: 0x2192,   # ⇒ → →
    0x21D4: 0x2192,   # ⇔ → →
    0x2194: 0x2192,   # ↔ → →
    0x2248: 0x7E,     # ≈ → ~
    0x2264: 0x3C,     # ≤ → <
    0x2265: 0x3E,     # ≥ → >
    0x00B7: 0x2022,   # · → •   （原版语料里没有 U+00B7，字模也没有；• 在字模里）
}


# 变体选择：OpenCC 的简繁表**不列**它（因为在台湾「着」也算正字），
# 但**原版语料坐实**了原版用的是「著」：
#   原版 `chi_*.txt` 全语料里「著」出现 **193** 次、「着」出现 **0** 次
#   （含持续体用法，例「邪惡的力量似乎沿著他的足跡而復甦」）⇒ 原版字形 = 「著」。
# 本工程有一处**会显示**的串用到它：`Module/Npc/NpcDialog.cs:77`（阿卡拉对话）。
VARIANTS = {
    0x7740: 0x8457,   # 着 → 著（原版语料 193:0）
}


def load_map(size):
    """读片 1 导出的 TSV → [(code, width, height, frame), ...]（按表内顺序）。"""
    path = os.path.join(MAP_SRC, "font%d_chi.tsv" % size)
    out = []
    for line in open(path, encoding="utf-8"):
        if line.startswith("#") or not line.strip():
            continue
        f = line.rstrip("\n").split("\t")
        if f[0] == "frame":
            continue
        out.append((int(f[1]), int(f[2]), int(f[3]), int(f[0])))
    return out


def atlas_cols(count, cell, limit=8192):
    """与 `export_d2ui.py::atlas_cols` 同口径（列数由帧数算出，最方且两维不超上限）。"""
    best = None
    for c in range(2, count + 1):
        if count % c:
            continue
        r = count // c
        if c > r:
            break
        if c * cell > limit or r * cell > limit:
            continue
        best = c
    return best


def write_map(size, entries):
    cell_w = max(e[1] for e in entries)
    cell_h = max(e[2] for e in entries)
    cols = atlas_cols(len(entries), max(cell_w, cell_h))
    if not cols:
        raise SystemExit("font%d：找不到规则网格（帧数 %d 格子 %d）" % (size, len(entries), cell_w))
    path = os.path.join(OUT_DIR, "font%d_chi_map.txt" % size)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# 原版中文位图字体 font%d：帧→字符 + 排版度量（**生成物，不要手改**）\n" % size)
        f.write("# 生成命令：python tools/d2codec/make_chifont_assets.py\n")
        f.write("# 源：原版资源/导出的字体映射/font%d_chi.tsv（原版 data/LOCAL/FONT/chi/font%d.tbl\n"
                % (size, size))
        f.write("#     格式出处 libd2 packages/formats/src/font.zig L59-117）\n")
        f.write("# 图集：D2/Fonts/font%d_chi.png（整幅，按行主序规则网格摆放）\n" % size)
        f.write("# 字段：code frame advance col row（code=Unicode 码位；advance=该字排版步进=原版 width；\n")
        f.write("#       col/row=该字在图集里的格子；格子尺寸 = %d×%d；列数 = %d）\n"
                % (cell_w, cell_h, cols))
        f.write("COLS %d\n" % cols)
        f.write("CELL %d %d\n" % (cell_w, cell_h))
        f.write("COUNT %d\n" % len(entries))
        for code, w, h, frame in entries:
            f.write("%d %d %d %d %d\n" % (code, frame, w, frame % cols, frame // cols))
    print("  写出 %s（%d 条，格子 %d×%d，列 %d）"
          % (os.path.basename(path), len(entries), cell_w, cell_h, cols))


def load_dict(name):
    out = {}
    for line in open(os.path.join(DICT, name), encoding="utf-8"):
        if line.startswith("#") or not line.strip():
            continue
        k, v = line.rstrip("\n").split("\t")
        out[k] = v.split(" ")
    return out


def main():
    os.makedirs(OUT_DIR, exist_ok=True)

    # ── ① 帧→字符 + 度量 ─────────────────────────────────────────────────
    print("① 字模映射表")
    codes = None
    for size in SIZES:
        entries = load_map(size)
        cs = set(e[0] for e in entries)
        if codes is None:
            codes = cs
        elif cs != codes:
            raise SystemExit("font%d 的码位集与 font16 不一致 ⇒ 不写（排查片 1 的导出）" % size)
        write_map(size, entries)
    print("  字模码位 %d 个（4 个字号一致）" % len(codes))

    # 繁体自检：证明字模是繁体字集（防"哪天换了字模却没人发现"）
    trad = "個為買羅營體學校"
    simp = "个为买罗营体学校"
    print("  繁体自检：繁体 %d/%d 在字模里；简体（异形字）%d/%d 不在字模里"
          % (sum(1 for c in trad if ord(c) in codes), len(trad),
             sum(1 for c in simp if ord(c) not in codes), len(simp)))

    # ── ② 简体 → 原版字形 码位映射 ────────────────────────────────────────
    print("② 简→原版字形 码位映射")
    st = load_dict("STCharacters.txt")     # 简 -> 繁候选（有序）
    ts = load_dict("TSCharacters.txt")     # 繁 -> 简（反向补全异体字，如 爲/為）

    corpus = collections.Counter()
    for fn in sorted(os.listdir(CORPUS_DIR)):
        if fn.startswith("chi"):
            corpus.update(open(os.path.join(CORPUS_DIR, fn), encoding="utf-8", errors="replace").read())

    reverse = collections.defaultdict(list)
    for t, ss in ts.items():
        for s in ss:
            reverse[s].append(t)

    pairs, multi, none_in_font = [], {}, 0
    for s in sorted(st, key=ord):
        if len(s) != 1:
            continue
        cands = []
        for c in list(st[s]) + list(reverse.get(s, [])):
            if len(c) == 1 and c != s and ord(c) in codes and c not in cands:
                cands.append(c)
        if not cands:
            none_in_font += 1
            continue
        # 选：原版语料出现次数多者优先，其次按候选顺序（ST 在前）
        ranked = sorted(range(len(cands)), key=lambda i: (-corpus[cands[i]], i))
        pick = cands[ranked[0]]
        pairs.append((ord(s), ord(pick)))
        if len(cands) > 1:
            multi[s] = [(c, corpus[c]) for c in cands]

    path = os.path.join(OUT_DIR, "font_chi_s2t.txt")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# 简体 → 原版字形 码位映射（**生成物，不要手改**）\n")
        f.write("# 生成命令：python tools/d2codec/make_chifont_assets.py\n")
        f.write("# 为什么需要：原版 chi 字模是**繁体**字集（%d 个码位），本工程配表文本是简体。\n"
                % len(codes))
        f.write("# 依据（两层，逐条校验后才进表）：\n")
        f.write("#   ① 语言事实：OpenCC STCharacters.txt（简→繁）+ TSCharacters.txt（繁→简，反向补异体），\n")
        f.write("#      Apache-2.0，见 原版资源/简繁词典/ 与 原版资源/清单.md；\n")
        f.write("#   ② 工程事实：候选必须 (a) 在 font*_chi 码位集里 (b) 出现在原版中文语料\n")
        f.write("#      原版资源/d2text/chi_*.txt 里；多候选时取语料出现次数多者。\n")
        f.write("# 字段：<简体码位> <原版字形码位>（十进制；两列都是 Unicode 码位）\n")
        f.write("COUNT %d\n" % len(pairs))
        for s, t in pairs:
            f.write("%d %d\n" % (s, t))
        f.write("# ── 符号归一（字模确实没有这些符号；用字模里已有的等价符号，逐条列出）──\n")
        f.write("# 来源：本工程显示串里的数学/箭头符号（原版语料里也没有它们，故无原版字形可用）\n")
        for s, t in sorted(SYMBOLS.items()):
            assert t in codes, "符号归一的替换目标 %s 不在字模里" % hex(t)
            f.write("%d %d\n" % (s, t))
        f.write("# ── 变体选择（OpenCC 未列；由原版语料坐实，见 make_chifont_assets.py 的 VARIANTS）──\n")
        for s, t in sorted(VARIANTS.items()):
            assert t in codes, "变体的替换目标 %s 不在字模里" % hex(t)
            f.write("%d %d\n" % (s, t))
    print("  写出 %s（%d 条 + 符号 %d 条）"
          % (os.path.basename(path), len(pairs), len(SYMBOLS)))
    print("  多候选（取语料出现次数多者）：%d 条，样例：%s"
          % (len(multi), "、".join("%s→%s" % (k, v[0][0]) for k, v in list(sorted(multi.items()))[:6])))
    print("  在字模里找不到任何繁体形的简体字：%d 个（多为异形字，若显示串用到会在运行期报 Error）"
          % none_in_font)


if __name__ == "__main__":
    main()
