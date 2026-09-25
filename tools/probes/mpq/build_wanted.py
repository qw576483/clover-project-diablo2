#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""组装 mpq「按名清单」（`wanted.txt`）+ 逐条来源登记（`wanted-sources.tsv`）。

来历：2026-09-22 `mpq-unpack2` 片的一次性脚本；
2026-09-22 `g1-naming-and-mpq-asset` 片把它提升为**仓内判据资产**（skill §3.5）
并**修掉 G-1**（清单侧命名错误）⇒ 见下面 R1/R2/R3 三条硬规则。

⛔ 三条硬规则（G-1 修的就是这三条；改它们前先读 `原版资源/清单.md` §6.4 的 G-1/G-2 行）
  R1 「只照表声明」：`.ds1` / `.dt1` 的名字**只许来自官方 1.10f 表的声明列**
     （`LvlPrest.txt` 的 `File1..FileN`、`LvlTypes.txt` 的 `File 1..File 32`，
      取 Act=1 且 Expansion=0 的行）。**任何来源**给出的 `.ds1`/`.dt1`，只要表里没声明 ⇒ 丢弃，
      并记进 `wanted-dropped.tsv`（不许按 `Bord1..4` 的字母后缀规律外推出 `Bord5..12{b,c,o,oe}` ——
      官方表里 Border 5..12 只有单文件，那 32 条是测出来的假名）。
  R2 「不许编名字」：清单里的名字必须是**字节可查的路径字面量**，且**名字里不许含空白字符**
     （旧版正则的字符类含空格，会把 `docs/agents/*.md` 里跨行的
      `data\\local\\font\\  font16.DC6` 这种畸形串当成"名字"切出来）。
  R3 「注释不算引用」：源码（`.cs/.py/.ps1/.shader`）里命中的路径**只在非注释行**才算数 ——
     `//` / `///` / `#` 是散文，不是资源引用。实测依据：`client/.../MapGenWildLayout.cs`
     的「剔除记录」注释块列了 32 个**不存在**的 `.ds1`，`NpcDialog.cs` 等 3 处的「键名对照」注释
     带了个**跟随不到**的 `data/local/string.txt`；它们都不是"工程要读的路径"。

来源（优先级无关，逐条并集；每条都记进 `wanted-sources.tsv`，带 `文件:行` 出处）
  ① `official-table` 官方 1.10f 表声明列（= `.ds1`/`.dt1` 的**唯一批量**来源）
  ② `code-literal`   工程**源码**（`.cs/.py/.ps1/.shader`）**非注释行**里的 `data\\global|local\\…` 路径字面量
  ③ `data-literal`   工程**数据/资产**文件（`.tsv/.json/.txt/.asset/.prefab`；无注释语法 ⇒ 不剥注释）
  ④ `doc-literal`    工程**文档**（`.md`）里点名的同类路径（散文；**不能推翻 R1**）
  ⑤ `d2ui-table`     `tools/d2codec/export_d2ui.py` 的**源表**（用 spy 采集，与生成器同源；
                     含 BANNERS / FRONTEND_CLASSES×STATES+TRANSITIONS / MENU_QUESTS / 字体表）
  ⑥ `items-png`      `data/global/items/**`：源表是**遍历目录**（原版包里无法枚举）
                     ⇒ 用工程内既有 PNG 名反推（命名规则 = stem = DC6 文件名小写 ⇒ 反推无损）
  ⑦ `named-in-code`  注释 / 文档点名、但 ①..⑥ 覆盖不到、且**确实需要**的原版件（逐条带出处）
  ⑧ `tbl-set`        串表三件套（`string.tbl` / `patchstring.tbl` / `expansionstring.tbl`；
                     其中 `expansionstring.tbl` 本包没有 = G-2 待补，**保留在清单里**当登记项）

产物（默认落 `<root>/.ai-tmp/test/`；⛔ 本脚本是判据资产 ⇒ 落 `tools/probes/mpq/`，产物仍走一次性目录）
  `wanted.txt`（按名清单，每行一个 mpq 内相对名）
  `wanted-sources.tsv`（`path<TAB>来源`；来源 token 形如 `code-literal:client/…/X.cs:42`）
  `wanted-dropped.tsv`（被 R1 丢掉的 `.ds1`/`.dt1` + 它们的原始来源 —— ⛔ 不许静默丢）
  `build-wanted.log`（人读日志：各来源条数 / 前缀分布 / 落差明细）

跑法（任意 cwd 都可；脚本自己按位置向上找含 `client/Assets` 的仓库根）
  python tools/probes/mpq/build_wanted.py
  python tools/probes/mpq/build_wanted.py --out-dir <目录>     # 换产物落点
  python tools/probes/mpq/build_wanted.py --root <仓库根>      # 显式给根（默认自动找）

依赖：Python 3.12；`tools/d2codec/{dc6.py,export_d2ui.py}` 在位；
      官方表 `原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/{LvlPrest,LvlTypes}.txt` 在位；
      工程已解包的 `client/Assets/Resources/Clover/D2/Items/*.png`（⑤ 的来源）。
      ⛔ 本脚本**只写 out-dir 下的文件**，不碰工程代码 / 表 / client/。
"""
import argparse
import io
import os
import re
import sys

# ── 路径常量（一律相对"仓库根"，不许写死盘符）───────────────────────────────
SKIP_DIRS = {"原版资源", ".ai-tmp", ".git", "Library", "obj", "bin", "Temp", "Logs",
             "Packages_cache", "node_modules", ".vs", ".idea", "__pycache__"}
FEXTS_CODE = (".cs", ".py", ".ps1", ".shader")
FEXTS_DATA = (".tsv", ".json", ".txt", ".asset", ".prefab")
FEXTS_DOC = (".md",)
# `strip_comments` 只许作用于**代码**扩展名：`.md` 里出现的 `/*` 不是注释开始
#   （实测：拿它去剥 `client/资源欠缺清单.md`，21701 B → 418 B，几乎整篇被吃掉）。


def find_root(start):
    """从 start 逐级向上找「含 client/Assets 的那一层」= 仓库根（与 tools/probes 下其它脚本同一套）。"""
    d = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            raise SystemExit("找不到仓库根（向上一直没看到 client/Assets）；请用 --root 显式指定")
        d = parent


# ── R2：路径正则（字符类里**不含空格**）────────────────────────────────────
PATH_RE = re.compile(
    r"data[\\/]+(?:global|local|LOCAL)[\\/]+[A-Za-z0-9_\\/\-.]+?\.(?:dc6|pl2|tbl|dt1|ds1|dcc|cof|txt)",
    re.IGNORECASE)


def norm(p):
    """统一成 mpq 内的相对名：统一反斜杠、压掉重复分隔符、去掉 `data\\global\\` 之前的前缀。"""
    p = p.strip().strip('"').strip("'")
    p = p.replace("/", "\\").replace("\\\\", "\\")
    p = re.sub(r"\\+", r"\\", p)
    low = p.lower()
    for pre in ("data\\global\\", "data\\local\\"):
        i = low.find(pre)
        if i > 0:
            p = p[i:]
            low = p.lower()
    return p


def strip_comments(text, ext):
    """R3：把注释剔掉再收字面量。

    故意用**行级**启发式（不做完整词法分析），因为它只影响"多余的名字"这一侧：
      · `/* … */` 块注释先整体删（`line` 级状态机）
      · 行首（去空白后）以 `//` / `#` / `--` 开始的整行删
      · 行内 ` #` / ` //` 之后的部分删（保留路径在 `x = "data\\…"  # 说明` 这种写法里的那一半）
    """
    out = []
    in_block = False
    for raw in text.splitlines():
        line = raw
        if in_block:
            if "*/" in line:
                line = line.split("*/", 1)[1]
                in_block = False
            else:
                out.append("")
                continue
        while "/*" in line:
            head, tail = line.split("/*", 1)
            if "*/" in tail:
                line = head + " " + tail.split("*/", 1)[1]
            else:
                line = head
                in_block = True
                break
        s = line.strip()
        if ext == ".cs":
            if s.startswith("//"):
                out.append("")
                continue
            for sep in (" //", "\t//"):
                if sep in line:
                    line = line.split(sep, 1)[0]
        elif ext in (".py", ".ps1"):
            if s.startswith("#"):
                out.append("")
                continue
            for sep in (" #", "\t#"):
                if sep in line:
                    line = line.split(sep, 1)[0]
        out.append(line)
    return "\n".join(out)


def collect_literals(root, exts, tag, strip):
    """②/③/④：walk 全仓（跳过 SKIP_DIRS）收路径字面量，按 `文件:行` 记出处。

    `strip=True` 只给**代码**（R3：注释是散文不是引用）。⛔ `.md`/`.json`/`.tsv`/`.txt`
    一律不剥（它们没有注释语法，剥了会误伤整篇）。
    """
    out = {}
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for fn in filenames:
            ext = os.path.splitext(fn)[1].lower()
            if ext not in exts:
                continue
            fp = os.path.join(dirpath, fn)
            try:
                txt = io.open(fp, encoding="utf-8", errors="ignore").read()
            except OSError:
                continue
            if strip:
                txt = strip_comments(txt, ext)
            rel = os.path.relpath(fp, root).replace("\\", "/")
            for n, line in enumerate(txt.splitlines(), 1):
                for m in PATH_RE.finditer(line):
                    out.setdefault(norm(m.group(0)), set()).add("%s:%s:%d" % (tag, rel, n))
    return out


def _rows(path):
    with io.open(path, encoding="utf-8", errors="ignore") as f:
        return [ln.rstrip("\r\n").split("\t") for ln in f]


def collect_official_tiles(root):
    """①：R1 的唯一批量来源 —— 官方 1.10f 表的**声明列**。

    LvlTypes.txt : Act=1 & Expansion=0 的行 → `File 1..File 32` 列里的 `.dt1`
    LvlPrest.txt : Name 以 `Act 1` 开头 & Expansion=0 的行 → `File1..FileN` 列里的 `.ds1`
    """
    excel = os.path.join(root, "原版资源", "参考工程_Diablerie", "d2lod1.10txt",
                         "data", "global", "excel")
    declared = {}
    out = {}

    rows = _rows(os.path.join(excel, "LvlTypes.txt"))
    hdr = rows[0]
    iact, iexp = hdr.index("Act"), hdr.index("Expansion")
    filecols = [i for i, h in enumerate(hdr) if h.startswith("File ")]
    ntypes = 0
    for r in rows[1:]:
        if len(r) <= iexp or r[iact] != "1" or r[iexp] != "0":
            continue
        ntypes += 1
        for i in filecols:
            if i >= len(r):
                continue
            v = r[i].strip()
            if not v or v == "0":
                continue
            p = norm("data\\global\\tiles\\" + v)
            declared[p.lower()] = "LvlTypes:%s" % r[0]
            out.setdefault(p, set()).add("official-table:LvlTypes.txt:%s（%s）" % (r[0], v))

    rows = _rows(os.path.join(excel, "LvlPrest.txt"))
    hdr = rows[0]
    iexp = hdr.index("Expansion")
    filecols = [i for i, h in enumerate(hdr) if re.match(r"^File\d+$", h)]
    nprest = 0
    for r in rows[1:]:
        if len(r) <= iexp or not r[0].startswith("Act 1") or r[iexp] != "0":
            continue
        nprest += 1
        for i in filecols:
            if i >= len(r):
                continue
            v = r[i].strip()
            if not v or v == "0":
                continue
            p = norm("data\\global\\tiles\\" + v)
            declared[p.lower()] = "LvlPrest:%s" % r[0]
            out.setdefault(p, set()).add("official-table:LvlPrest.txt:%s（%s）" % (r[0], v))

    print("官方表声明：LvlTypes Act1 行 = %d（.dt1 列 = %d 个） / LvlPrest Act1 行 = %d"
          % (ntypes, len(filecols), nprest))
    print("官方表声明集：.ds1 + .dt1 去重后 = %d 条" % len(declared))
    return out, declared


def collect_d2ui(root):
    """④：`export_d2ui.py` 的源表（spy 跑它的组函数，采集 read_dc6 / read_pl2 的实参）。"""
    sys.path.insert(0, os.path.join(root, "tools", "d2codec"))
    import dc6
    import export_d2ui as ex

    ex.SRC_ROOT = os.path.join(root, "原版资源", "d2dc6")
    ex.DST_ROOT = os.path.join(root, "client")
    ex.RAW_ROOT = os.path.join(root, "原版资源", "d2raw")
    ex.PL2_ROOT = os.path.join(ex.RAW_ROOT, "data", "global", "palette")

    srcs, pals = [], []

    def spy_read(*parts):
        srcs.append(os.path.join(*parts))
        return None

    def spy_pl2(path):
        pals.append(path)
        return []

    # 必须同时把 `one()` / `write_fontmap_tsv()` 打成空操作：
    #   解包产物已在盘 ⇒ group_items / group_chifont 会真的走到 `one()` ⇒ **会往 client/Assets 写 PNG**。
    ex.read_dc6 = spy_read
    dc6.read_pl2 = spy_pl2
    ex.one = lambda *a, **k: None
    ex.write_fontmap_tsv = lambda *a, **k: None
    argv = sys.argv
    try:
        sys.argv = ["spy", ex.SRC_ROOT, ex.DST_ROOT]
        ex.main()
    finally:
        sys.argv = argv

    out = {}
    for p in srcs:
        out.setdefault(norm(os.path.relpath(p, ex.SRC_ROOT)), set()).add("d2ui-table:export_d2ui.py:read_dc6")
    for p in pals:
        out.setdefault(norm(os.path.relpath(p, ex.RAW_ROOT)), set()).add("d2ui-table:export_d2ui.py:read_pl2")
    for _dc6n, _stem, tbl in ex.CHI_FONTS:
        out.setdefault(norm("data/local/font/chi/" + tbl), set()).add("d2ui-table:export_d2ui.py:CHI_FONTS")
    # 源表里**遍历目录**的两处（原版包里无法枚举 ⇒ 按声明表列全）：
    #   · group_banners：BANNERS 表（磁盘实际大小写；StormLib 查名不区分大小写）
    #   · group_frontend：FRONTEND_CLASSES × (FRONTEND_STATES + FRONTEND_TRANSITIONS)
    for fn in ex.BANNERS:
        out.setdefault(norm("data/LOCAL/UI/chi/" + fn), set()).add("d2ui-table:export_d2ui.py:BANNERS")
    for cls_dir, prefix, _out_dir, _keep in ex.FRONTEND_CLASSES:
        for state, _stem in tuple(ex.FRONTEND_STATES) + tuple(ex.FRONTEND_TRANSITIONS):
            out.setdefault(norm("data/global/ui/FrontEnd/%s/%s%s.DC6" % (cls_dir, prefix, state)),
                           set()).add("d2ui-table:export_d2ui.py:FRONTEND_CLASSES")
    # spy 的盲区（实测踩到）：`group_menu` 在 `MENU_ENDGAME` 读不到时**提前 return**
    #   ⇒ spy 模式下 `MENU_QUESTS` 那 21 张任务说明图一个都记不到 ⇒ 直接读它的表常量补齐。
    for q in ex.MENU_QUESTS:
        out.setdefault(norm("data/global/ui/MENU/" + q + ".dc6"), set()).add("d2ui-table:export_d2ui.py:MENU_QUESTS")
    # 源表 docstring 里点到的（group_loading：data/LOCAL/UI/loadingscreen.dc6 是同一张图的另一种打包）
    out.setdefault(norm("data/LOCAL/UI/loadingscreen.dc6"), set()).add("d2ui-table:export_d2ui.py:docstring")
    return out


def collect_items(root):
    """⑤：`data/global/items/**` 的**遍历目录**来源 —— 用工程既有 PNG 名反推（无损）。"""
    d = os.path.join(root, "client", "Assets", "Resources", "Clover", "D2", "Items")
    out = {}
    if not os.path.isdir(d):
        return out
    for fn in sorted(os.listdir(d)):
        if not fn.lower().endswith(".png"):
            continue
        out.setdefault(norm("data\\global\\items\\" + fn[:-4] + ".DC6"),
                       set()).add("items-png:client/Assets/Resources/Clover/D2/Items/%s" % fn)
    return out


# ⑥ 逐条带出处的"确实需要、但只有注释/文档点名"的原版件
NAMED_IN_CODE = (
    (r"data\global\ui\PANEL\ctrlpnl7.DC6",
     "tools/d2codec/dc6.py:49 与 export_d2ui.py:65,88 —— 调色板口径判定的实测件"),
    (r"data\global\ui\PANEL\invchar.DC6",
     "export_d2ui.py:621-623（ACT1 口径的实测件）、dc6.py:278（tile 拼装口径的样本）"),
    (r"data\global\ui\FrontEnd\TitleScreen.DC6", "tools/d2codec/dc6.py:278"),
    (r"data\global\ui\FrontEnd\CharacterCreate.DC6", "tools/d2codec/dc6.py:278"),
    (r"data\global\ui\AUTOMAP\MaxiMap.dc6",
     "client/资源欠缺清单.md:231（#26「原版自动地图图块」条目）+ "
     "client/Assets/Scripts/UI/MiniMapPanel.cs:11,39,46,64（注释点名的原版 automap 图块表，1260 帧 16×32）"),
    (r"data\global\ui\SPELLS\Skilltree_bg.dc6",
     "原版资源/清单.md:320（G-2 登记项：本包确实没有 ⇒ 待 USER 提供另一份归档；"
     "项目的技能树底图走 skltree_*_back.DC6，已解出且 verifier PASS）"),
)

# ⑦ 串表三件套（`chi_string.txt` 的判据源；`uicheck:986` 要的 `原版资源/d2text/chi_string.txt` 由此生成）
TBL_SET = (
    (r"data\local\LNG\CHI\string.tbl", "原版 base 串表（§5.7 的生成命令用它）"),
    (r"data\local\LNG\CHI\patchstring.tbl", "原版补丁串表（§5.7 的 --patch 用它）"),
    (r"data\local\LNG\CHI\expansionstring.tbl",
     "资料片串表 —— 本包没有 ⇒ G-2 待补（id 域从 20000 起；保留为登记项，不是外推名）"),
)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="组装 mpq 按名清单（含来源登记）")
    ap.add_argument("--root", default=None, help="仓库根（默认从脚本位置向上找含 client/Assets 的那一层）")
    ap.add_argument("--out-dir", default=None, help="产物目录（默认 <root>/.ai-tmp/test）")
    args = ap.parse_args()

    root = os.path.abspath(args.root) if args.root else find_root(here)
    out_dir = os.path.abspath(args.out_dir) if args.out_dir else os.path.join(root, ".ai-tmp", "test")
    os.makedirs(out_dir, exist_ok=True)
    print("root    =", root)
    print("out-dir =", out_dir)

    merged = {}

    def add(d):
        for k, v in d.items():
            merged.setdefault(k, set()).update(v)

    tiles, declared = collect_official_tiles(root)
    code_lit = collect_literals(root, FEXTS_CODE, "code-literal", strip=True)
    data_lit = collect_literals(root, FEXTS_DATA, "data-literal", strip=False)
    doc_lit = collect_literals(root, FEXTS_DOC, "doc-literal", strip=False)
    d2ui = collect_d2ui(root)
    items = collect_items(root)
    add(tiles)
    add(code_lit)
    add(data_lit)
    add(doc_lit)
    add(d2ui)
    add(items)
    named = {}
    for name, why in NAMED_IN_CODE:
        merged.setdefault(norm(name), set()).add("named-in-code:%s" % why)
        named[norm(name)] = why
    for name, why in TBL_SET:
        merged.setdefault(norm(name), set()).add("tbl-set:%s" % why)

    # ── R1 闸门：`.ds1` / `.dt1` 只许来自「官方表声明列」或「工程源码里的字面量」────
    #  理由：`.ds1`/`.dt1` 的批量来源只有官方表的声明列（`LvlPrest.FileN` / `LvlTypes.File N`）；
    #  散文（`.md`/`.tsv`/`.txt` 里的说明性文字）与一次性手写清单正是"按 Bord1..4 的字母后缀
    #  外推出 Bord5..12{b,c,o,oe}"那 32 个假名的来源 ⇒ 这两类一律必须**被官方表声明**才算数。
    #  `code-literal` 例外：那是**活代码在问包要文件**（如 `export_tiles.py:65` 的 warp.dt1），
    #  不是外推 —— 它照旧保留，但会被记进 `wanted-undeclared.tsv` 供主 agent 裁。
    dropped, undeclared = {}, {}
    for p in list(merged):
        low = p.lower()
        if not low.endswith((".ds1", ".dt1")):
            continue
        if low in declared:
            continue
        srcs = merged[p]
        if any(s.startswith("code-literal:") for s in srcs):
            undeclared[p] = sorted(srcs)
            continue
        dropped[p] = sorted(merged.pop(p))

    # ── R2 闸门：名字里不许有空白字符 ─────────────────────────────────────────
    bad_ws = [p for p in merged if re.search(r"\s", p)]

    paths = sorted(merged)
    with io.open(os.path.join(out_dir, "wanted.txt"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(paths) + "\n")
    with io.open(os.path.join(out_dir, "wanted-sources.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("path\tsources\n")
        for p in paths:
            f.write("%s\t%s\n" % (p, ";".join(sorted(merged[p]))))
    with io.open(os.path.join(out_dir, "wanted-dropped.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# R1 丢掉的 .ds1/.dt1 —— 官方表声明列里没有它，且它只来自散文 / 手写清单（= 疑似外推名）\n")
        f.write("# 格式：<名字>\t<原始来源（; 分隔）>\n")
        for p in sorted(dropped):
            f.write("%s\t%s\n" % (p, ";".join(dropped[p])))
    with io.open(os.path.join(out_dir, "wanted-undeclared.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("# R1 保留但**官方表没声明**的 .ds1/.dt1 —— 依据 = 工程源码里的字面量（活代码在问包要它）\n")
        f.write("# ⛔ 这一张表是给主 agent 裁的：'表里没声明但工程要读' 属矛盾，不要自己改官方表。\n")
        f.write("# 格式：<名字>\t<来源（; 分隔）>\n")
        for p in sorted(undeclared):
            f.write("%s\t%s\n" % (p, ";".join(undeclared[p])))

    log = ["wanted.txt 行数 = %d" % len(paths),
           "被 R1 丢弃的 .ds1/.dt1 = %d 条（明细 wanted-dropped.tsv）" % len(dropped),
           "R1 保留但未声明（代码字面量）的 .ds1/.dt1 = %d 条（明细 wanted-undeclared.tsv）" % len(undeclared),
           "含空白字符的名字 = %d 条（应为 0）%s" % (len(bad_ws), ("：" + ", ".join(bad_ws)) if bad_ws else ""),
           "官方表声明集（.ds1 + .dt1）= %d 条" % len(declared),
           "各来源条数（并集前；① 是唯一批量来源，⑥⑦ 是逐条登记项）:"]
    for nm, d in (("① official-table", tiles), ("② code-literal", code_lit),
                  ("③ data-literal", data_lit), ("④ doc-literal", doc_lit),
                  ("⑤ d2ui-table", d2ui), ("⑥ items-png", items),
                  ("⑦ named-in-code", NAMED_IN_CODE), ("⑧ tbl-set", TBL_SET)):
        log.append("  %-20s %4d" % (nm, len(d)))
    log.append("前缀分布:")
    pre = {}
    for p in paths:
        low = p.lower()
        if low.startswith("data\\global\\ui\\"):
            k = "ui"
        elif low.startswith("data\\global\\tiles\\"):
            k = "tiles"
        elif low.startswith("data\\global\\palette\\"):
            k = "palette"
        elif low.startswith("data\\global\\excel\\"):
            k = "excel"
        elif low.startswith("data\\global\\items\\"):
            k = "items"
        elif low.startswith("data\\local\\"):
            k = "local"
        else:
            k = "(other)"
        pre[k] = pre.get(k, 0) + 1
    for k in sorted(pre, key=lambda x: -pre[x]):
        log.append("  %-10s %4d" % (k, pre[k]))
    log.append("被丢弃的 .ds1/.dt1（R1：未声明 + 只来自散文/手写清单）：")
    for p in sorted(dropped):
        log.append("  %-52s <- %s" % (p, ";".join(dropped[p])))
    log.append("未声明但被代码读取的 .ds1/.dt1（R1 保留，待主 agent 裁）：")
    for p in sorted(undeclared):
        log.append("  %-52s <- %s" % (p, ";".join(undeclared[p])))
    with io.open(os.path.join(out_dir, "build-wanted.log"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(log) + "\n")
    print("\n".join(log))

    if bad_ws:
        print("FAIL R2：名字里出现空白字符", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
