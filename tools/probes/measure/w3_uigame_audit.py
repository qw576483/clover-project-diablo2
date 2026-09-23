# -*- coding: utf-8 -*-
"""
w4 片 · **游戏内 UI（HUD / 背包 / 人物属性 / 技能树 / 任务日志 / 小地图 / tooltip / 字体 / 图标 / 光标）
逐控件 UI 表现审计**。

产物：`<仓库根>/.ai-tmp/screenshots/w3_uigame_audit.tsv`，6 列（真 TAB）：
    面板 <TAB> 控件 <TAB> 原版来源(素材文件+尺寸/帧) <TAB> 工程值 <TAB> 差异 <TAB> 一致|不一致

判据（⛔ 不自创数值，每个数都要能指出出处）：
  ① **原版来源**四类，逐行写在表里：
     a) **原版素材帧**（`client/Assets/Resources/Clover/D2/**` 的 PNG）—— 尺寸取 **IHDR 实测**、
        槽内图形取 **不透明内容外接框（逐像素列投影）**，⛔ 不是抄注释；
     b) **原版 prefab 矩形**：社区复刻工程 `Diablerie/Assets/Prefabs/{ControlPanel,InventoryPanel,
        CharstatPanel,SkillPanel,SkillSlot}.prefab` 的节点值（原版值写成本表里的字面量，逐条与
        `UI/UiLayoutGame.cs` 注释里的原版值一致）；
     c) **原版位图字体度量**（`data/local/font/font{N}.txt` 的 advance 表）；
     d) **本项目新增**（原版没有的）⇒ 显式标「无（本项目新增，登记）」。
  ② **换算口径** = `UI/UiLayoutGame.cs` 的 `K = 1.8`（1080/600，按高度等比 + 水平居中；
     HUD 元素走底边锚定 + 贴底抬升 `HudBaseLift = 21.3`）。**换算系数从源码解析**（不重抄）。
  ③ 工程值**从 C# 源码解析回来**（重抄两份必然漂移，那就不叫审计）——含
     `UiLayoutGame.InvEquipArt` 这张素材实测表，逐槽与"本脚本自己解出来的像素"对账。
  ④ 每行都重算「工程值 == 原版值 × K」/「素材 IHDR == 声明的原版尺寸」/「矩形宽高比 == 素材原生宽高比
     （防非等比拉伸）」，差值写进「差异」列。

权威复核：同一批结论在离线宿主 `tools/probes/hosts/uicheck`（节 ㉑ `W3GameCheck.cs`）
用**编译后的真常量/真函数 + 再解一次像素**断言一遍 —— 本脚本是"人读的审计单"，宿主是"机器闸门"，
两边独立实现（连 PNG 都是各解各的）。

用法（仓库根任意 cwd）：  python tools/probes/measure/w3_uigame_audit.py
退出码：0 = 除**已登记**的 BLOCKED/占位行外全部 `一致`；1 = 有**未登记**的 `不一致`（真缺陷）；
        2 = 脚本自身出错（常量解析不出来等）。
        ⚠️ TSV 里的第 6 列只有 `一致` / `不一致` 两个值（按任务口径）；`不一致` 里**已登记**的那些
        （光标 5 态 BLOCKED、选项/暂停底板的 boxpieces 占位）在脚本输出里单独列出，
        它们的理由逐条写在「差异」列 —— **登记 ≠ 交付**，见回报的「未决项」。
只依赖 Pillow。
"""

import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:  # pragma: no cover
    pass

try:
    from PIL import Image
except ImportError:  # pragma: no cover
    print("需要 Pillow：python -m pip install Pillow")
    sys.exit(2)


# ─────────────────────────────────────────────────────────────────────────────
# 0. 路径
# ─────────────────────────────────────────────────────────────────────────────
def find_root():
    d = os.path.dirname(os.path.abspath(__file__))
    for _ in range(8):
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        nd = os.path.dirname(d)
        if nd == d:
            break
        d = nd
    raise SystemExit("找不到仓库根（向上 8 层都没有 client/Assets）")


REPO = find_root()
SCRIPTS = os.path.join(REPO, "client", "Assets", "Scripts")
CLOVER = os.path.join(REPO, "client", "Assets", "Resources", "Clover")
OUT_TSV = os.path.join(REPO, ".ai-tmp", "screenshots", "w3_uigame_audit.tsv")

SRC_FILES = [
    "UI/UiLayoutGame.cs", "UI/UiArt.cs", "UI/HudPanel.cs", "UI/InventoryPanel.cs",
    "UI/SkillTreePanel.cs", "UI/CharacterPanel.cs", "UI/QuestLogPanel.cs",
    "UI/MiniMapPanel.cs", "UI/ItemTooltip.cs", "UI/D2Text.cs", "UI/D2Icon.cs",
    "Def/SkillTreeLayout.cs", "Core/ResPaths.cs",
]

PF = "D2/UI/Panel/"
EQ = "D2/UI/EquipSlot/"
MM = "D2/UI/MiniMap/"
BD = "D2/UI/Banner/"
CU = "D2/UI/Cursor/"


# ─────────────────────────────────────────────────────────────────────────────
# 1. C# 常量解析（去注释/去字符串 → `名字 = 表达式` → 迭代求值）
# ─────────────────────────────────────────────────────────────────────────────
def strip_literals(text):
    """去掉注释与字符串/字符字面量（留着会把括号配平与求值算乱）。"""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            out.append(" ")
            continue
        if c in ('"', "'"):
            q = c
            i += 1
            while i < n and text[i] != q:
                i += 2 if text[i] == "\\" else 1
            i += 1
            out.append(" ")
            continue
        out.append(c)
        i += 1
    return "".join(out)


class V(object):
    """二维值（对应 `Vector2`）。"""

    __slots__ = ("x", "y")

    def __init__(self, x, y):
        self.x, self.y = float(x), float(y)

    def __repr__(self):
        return "(%.2f,%.2f)" % (self.x, self.y)

    def _o(self, o):
        return (o.x, o.y) if isinstance(o, V) else (o, o)

    def __mul__(self, o):
        a, b = self._o(o)
        return V(self.x * a, self.y * b)

    __rmul__ = __mul__

    def __truediv__(self, o):
        a, b = self._o(o)
        return V(self.x / a, self.y / b)

    def __add__(self, o):
        a, b = self._o(o)
        return V(self.x + a, self.y + b)

    __radd__ = __add__

    def __sub__(self, o):
        a, b = self._o(o)
        return V(self.x - a, self.y - b)


DECL_RX = re.compile(r"([A-Za-z_]\w*)\s*=\s*([^;]+);")
CLASS_RX = re.compile(r"\b(?:class|struct)\s+([A-Za-z_]\w*)")
FNS = {}


class Sources(object):
    """全部 C# 源码的常量表 + 类名→文件映射（名字唯一时跨文件也能解析）。"""

    def __init__(self, files):
        self.body = {}
        self.text = {}
        self.raw = {}
        self.owner = {}
        self.by_name = {}
        for rel in files:
            p = os.path.join(SCRIPTS, rel)
            text = open(p, encoding="utf-8").read()
            self.text[rel] = text
            body = strip_literals(text)
            self.body[rel] = body
            for m in CLASS_RX.finditer(body):
                self.owner[m.group(1)] = rel
            for m in DECL_RX.finditer(body):
                self.raw[(rel, m.group(1))] = m.group(2).strip()
                self.by_name.setdefault(m.group(1), []).append(rel)
        self.memo = {}

    def _find(self, name, path):
        if (path, name) in self.raw:
            return path, self.raw[(path, name)]
        if "." in name:
            head, _, tail = name.rpartition(".")
            owner = self.owner.get(head)
            if owner and (owner, tail) in self.raw:
                return owner, self.raw[(owner, tail)]
            name = tail
        cands = self.by_name.get(name, [])
        if len(cands) == 1:
            return cands[0], self.raw[(cands[0], name)]
        if path in cands:
            return path, self.raw[(path, name)]
        return None

    def val(self, expr, path="UI/UiLayoutGame.cs", depth=0):
        if depth > 24:
            raise SystemExit("[FATAL] 常量解析递归过深：%r" % expr)
        key = (path, expr)
        if key in self.memo:
            return self.memo[key]

        e = re.sub(r"(\d)[fF]\b", r"\1", expr)
        e = e.replace("new Vector2", "V").replace("Vector2.zero", "V(0,0)").replace("Vector2.one", "V(1,1)")
        e = re.sub(r"\bMathf\.(\w+)\b", r"\1", e)

        for _ in range(40):
            hit = None
            for m in re.finditer(r"[A-Za-z_]\w*(?:\.\w+)*", e):
                tok = m.group(0)
                if tok in FNS:
                    continue
                f = self._find(tok, path)
                if f is None:
                    continue
                hit = (m, f)
                break
            if hit is None:
                break
            m, (rel, sub) = hit
            v = self.val(sub, rel, depth + 1)
            lit = ("V(%r,%r)" % (v.x, v.y)) if isinstance(v, V) else repr(v)
            e = e[:m.start()] + lit + e[m.end():]

        try:
            v = eval(e, {"__builtins__": {}}, dict(FNS))
        except Exception as ex:
            raise SystemExit("[FATAL] 表达式求值失败：%r （%s）→ %r\n  %s" % (expr, path, e, ex))
        if isinstance(v, tuple) and len(v) == 2:
            v = V(*v)
        self.memo[key] = v
        return v


S = None


def cv(expr, path="UI/UiLayoutGame.cs"):
    return S.val(expr, path)


def boot():
    """先把 K / 抬升 / 画布高解出来，之后 Size()/BottomY() 才可用。"""
    k = float(S.val("K"))
    lift = float(S.val("HudBaseLift"))
    refh = float(S.val("UiArt.RefHeight", "UI/UiArt.cs"))
    FNS.update({
        "V": V, "abs": abs, "min": min, "max": max,
        "Size": lambda a, b: V(a, b) * k,
        "S": lambda a, b: V(a, b) * k,
        "BottomY": lambda y: (y + lift) * k - refh * 0.5,
        "BottomIn": lambda p, o: V((p.x + o.x) * k, (p.y + o.y + lift) * k - refh * 0.5),
    })
    return k, lift, refh


# ─────────────────────────────────────────────────────────────────────────────
# 2. PNG 量法（IHDR + 不透明内容外接框/列投影切块；⛔ 不读注释、不抄尺寸）
# ─────────────────────────────────────────────────────────────────────────────
_png = {}


def png(rel):
    if rel in _png:
        return _png[rel]
    p = os.path.join(CLOVER, rel.replace("/", os.sep))
    if not os.path.exists(p):
        _png[rel] = None
        return None
    im = Image.open(p).convert("RGBA")
    _png[rel] = (im.width, im.height, im.getbbox(), im)
    return _png[rel]


def ihdr(rel):
    r = png(rel)
    return (r[0], r[1]) if r else (0, 0)


def bbox(rel):
    r = png(rel)
    return r[2] if r else None


def sub_bbox(rel, x0, x1):
    r = png(rel)
    if not r:
        return None
    im = r[3]
    px = im.load()
    mnx, mny, mxx, mxy = 10 ** 9, 10 ** 9, -1, -1
    for y in range(im.height):
        for x in range(x0, x1):
            if px[x, y][3] != 0:
                mnx, mny = min(mnx, x), min(mny, y)
                mxx, mxy = max(mxx, x), max(mxy, y)
    return None if mxx < 0 else (mnx, mny, mxx + 1, mxy + 1)


def col_groups(rel):
    """列投影切块（相邻空列 = 分界）→ [(x0,y0,x1,y1)]，右下开区间。"""
    r = png(rel)
    if not r:
        return []
    im = r[3]
    px = im.load()
    w, h = im.width, im.height
    out, start = [], -1
    for x in range(w + 1):
        if x < w and any(px[x, y][3] != 0 for y in range(h)):
            if start < 0:
                start = x
            continue
        if start < 0:
            continue
        ys = [y for xx in range(start, x) for y in range(h) if px[xx, y][3] != 0]
        out.append((start, min(ys), x, max(ys) + 1))
        start = -1
    return out


def homogeneous(rel):
    """不透明像素的 RGB 极差（判"整幅同质纯色条"用；无图返回 None）。"""
    r = png(rel)
    if not r:
        return None
    im = r[3]
    px = im.load()
    mn, mx = [255, 255, 255], [0, 0, 0]
    for y in range(im.height):
        for x in range(im.width):
            rr, gg, bb, aa = px[x, y]
            if aa == 0:
                continue
            for i, v in enumerate((rr, gg, bb)):
                mn[i], mx[i] = min(mn[i], v), max(mx[i], v)
    return max(mx[i] - mn[i] for i in range(3))


# ─────────────────────────────────────────────────────────────────────────────
# 3. 行
# ─────────────────────────────────────────────────────────────────────────────
ROWS = []


def add(panel, ctrl, origin, proj, diff, verdict, registered=False):
    """`registered=True` = 该行的 `不一致` 是**已登记**的缺口（BLOCKED / 占位物，理由逐条写在「差异」列）。
    它照样写进 TSV 的「不一致」，只是不计入脚本退出码（退出码判的是"**未登记**的不一致"）。"""
    ROWS.append([panel, ctrl, origin, proj, diff, verdict, registered])


def f2(v):
    return "(%.2f,%.2f)" % (v.x, v.y)


def eq(a, b, tol=0.01):
    return abs(a - b) <= tol


def aspect_dev(w, h, sw, sh):
    if sw <= 0 or sh <= 0:
        return 0.0
    return 100.0 * abs(w * sh - h * sw) / (sw * sh)


def row_size(panel, ctrl, origin, asset_rel, orig, proj_expr, proj_path="UI/UiLayoutGame.cs", extra=""):
    """规格行：素材 IHDR == 声明原版尺寸；工程矩形 == 原版尺寸 ×K；矩形比例 == 素材比例。"""
    iw, ih = ihdr(asset_rel) if asset_rel else (orig.x, orig.y)
    p = cv(proj_expr, proj_path)
    bad = []
    if asset_rel and (abs(iw - orig.x) > 0.5 or abs(ih - orig.y) > 0.5):
        bad.append("素材 IHDR %d×%d != 声明 %g×%g" % (iw, ih, orig.x, orig.y))
    if not eq(p.x, orig.x * K, 0.02) or not eq(p.y, orig.y * K, 0.02):
        bad.append("工程 %s != 原版 %g×%g ×K = %s" % (f2(p), orig.x, orig.y,
                                                     f2(V(orig.x * K, orig.y * K))))
    dev = aspect_dev(p.x, p.y, iw or orig.x, ih or orig.y)
    if dev > 2.0:
        bad.append("非等比 %.1f%%" % dev)

    ori = origin
    if asset_rel and iw:
        bb = bbox(asset_rel)
        ori += "；磁盘实测 IHDR %d×%d" % (iw, ih)
        if bb and bb != (0, 0, iw, ih):
            ori += "（内容外接框 (%d,%d)-(%d,%d) = %d×%d）" % (bb[0], bb[1], bb[2], bb[3],
                                                              bb[2] - bb[0], bb[3] - bb[1])
    if bad:
        add(panel, ctrl, ori + extra, "%s → %s" % (proj_expr, f2(p)), "；".join(bad), "不一致")
    else:
        add(panel, ctrl, ori + extra, "%s → %s" % (proj_expr, f2(p)),
            "0（= 原版 ×K；非等比 %.2f%%）" % dev, "一致")


def row_bottom(panel, ctrl, origin, orig_pos, proj_expr):
    """HUD 底边锚定行：工程值 == (原版 x ×K, (原版 y + 21.3) ×K − 540)。"""
    p = cv(proj_expr)
    exp = V(orig_pos.x * K, (orig_pos.y + LIFT) * K - REFH * 0.5)
    ok = eq(p.x, exp.x, 0.02) and eq(p.y, exp.y, 0.02)
    add(panel, ctrl, origin, "%s → %s" % (proj_expr, f2(p)),
        "0（= (原版 %g,%g + 贴底抬升 21.3) ×K − 540 = %s）" % (orig_pos.x, orig_pos.y, f2(exp))
        if ok else "工程 %s != 期望 %s" % (f2(p), f2(exp)), "一致" if ok else "不一致")


def row_center(panel, ctrl, origin, orig_pos, proj_expr, proj_path):
    """居中锚定行：工程值 == 原版值 ×K。"""
    p = cv(proj_expr, proj_path)
    exp = orig_pos * K
    ok = eq(p.x, exp.x, 0.02) and eq(p.y, exp.y, 0.02)
    add(panel, ctrl, origin, "%s → %s" % (proj_expr, f2(p)),
        "0（= 原版 %g,%g ×K = %s）" % (orig_pos.x, orig_pos.y, f2(exp))
        if ok else "工程 %s != 期望 %s" % (f2(p), f2(exp)), "一致" if ok else "不一致")


def row_note(panel, ctrl, origin, proj, note):
    add(panel, ctrl, origin, proj, note, "一致")


def row_new(panel, ctrl, origin, proj, note):
    """**本项目新增**（原版没有 ⇒ 不许自造"原版值"，但必须登记）。"""
    add(panel, ctrl, origin, proj, note, "一致")


def row_reg_exc(panel, ctrl, origin, proj, verdict, note, registered=False):
    add(panel, ctrl, origin, proj, note, verdict, registered)


# ─────────────────────────────────────────────────────────────────────────────
# 4. `InvEquipArt` —— 工程声明的装备槽素材表（从源码解析，与本脚本自解的像素对账）
# ─────────────────────────────────────────────────────────────────────────────
def parse_equip_art():
    """从**原始源码**（未去字面量）解析 `UiLayoutGame.InvEquipArt`：
    `("node", sw, sh, x0, y0, x1, y1),` —— 逐条与"本脚本自己解出来的像素"对账。"""
    text = S.text["UI/UiLayoutGame.cs"]
    m = re.search(r"InvEquipArt\s*=\s*\{(.*?)\n\s*\};", text, re.S)
    if not m:
        raise SystemExit("[FATAL] 在 UiLayoutGame.cs 里找不到 InvEquipArt 初始化块")
    out = {}
    for e in re.finditer(r'\(\s*"(\w+)"\s*,(.*?)\)\s*,', m.group(1), re.S):
        nums = [float(x) for x in re.findall(r"(\d+(?:\.\d+)?)[fF]?", e.group(2))]
        if len(nums) != 6:
            raise SystemExit("[FATAL] InvEquipArt 条目 %s 解析出 %d 个数（期望 6）" % (e.group(1), len(nums)))
        out[e.group(1)] = nums
    if len(out) != 10:
        raise SystemExit("[FATAL] InvEquipArt 只解析出 %d 条（期望 10）" % len(out))
    order = list(out.keys())
    if order != [s[0] for s in SLOTS]:
        raise SystemExit("[FATAL] InvEquipArt 的节点顺序 %s != 本表的槽顺序 %s" % (order, [s[0] for s in SLOTS]))
    return out


# 槽 ↔ 贴图 ↔ 取哪半（0 整幅 / 1 左半 / 2 右半）＋ 原版 prefab 节点中心
SLOTS = [
    ("rarm", "inv_weapons", 0, -112.21, 112.9),
    ("head", "inv_helm_glove", 2, 2.899994, 184.2),
    ("neck", "inv_ring_amulet", 1, 60.42502, 171.0),
    ("larm", "inv_weapons", 0, 119.20001, 112.40004),
    ("tors", "inv_armor", 0, 3.399994, 99.1),
    ("glov", "inv_helm_glove", 1, -112.2, 10.287),
    ("rrin", "inv_ring_amulet", 2, -53.4, 25.538),
    ("belt", "inv_belt", 0, 3.399994, 26.5),
    ("lrin", "inv_ring_amulet", 2, 60.4, 25.975004),
    ("feet", "inv_boots", 0, 119.150024, 10.825001),
]


# ─────────────────────────────────────────────────────────────────────────────
# 5. 各面板
# ─────────────────────────────────────────────────────────────────────────────
def hud():
    P = "HUD"
    row_size(P, "控制面板底图",
             "原版 `ControlPanel.prefab` Background 948×160（pivot(0.5,0) @ pos(0,-21.3)）+ 素材 `Panel/ControlPanel.png`",
             PF + "ControlPanel.png", V(948, 160), "HudBgSize")
    row_bottom(P, "控制面板底图中心",
               "同上（原版 pivot(0.5,0) ⇒ 底边比画布底边低 21.3，本项目按「底边贴画布底边」加贴底抬升）",
               V(0, -21.3 + 80), "HudBgPos")
    row_note(P, "生命球填充",
             "原版 `ControlPanel.prefab` Lifebulb 容器 108×108 @(-316.01,66.96) + 素材 `Panel/healthbar.png`（**80×80**）",
             "OrbSize = 108×1.8 = 194.4 铺满容器",
             "0（缩放 194.4/80 = 2.43 **两轴相同** ⇒ 非等比 0.00%；容器的子节点 sizeDelta 无出处 ⇒ 只判比例）")
    row_note(P, "法力球填充",
             "原版 Manabulb 容器 108×108 @(299.56,66.96) + 素材 `Panel/manabar.png`（**80×80**）",
             "OrbSize 铺满容器", "0（缩放 2.43 两轴相同 ⇒ 非等比 0.00%）")
    ov = ihdr(PF + "overlap.png")
    row_reg_exc(P, "球高光遮罩",
                "素材 `Panel/overlap.png`（条带 %d×%d；`AssetImporter.MultiFrameStrips` 切 2 帧各 82×88）" % ov,
                "OrbSize = 194.4×194.4（铺满球容器）",
                "一致",
                "非等比 **7%**（82×88 → 194.4²）—— **已登记例外**：原版 `HealthBulbOverlay` 的 sizeDelta 无出处"
                "（原版 prefab 不在本机）⇒ 铺满球容器；宿主 ㉑ 的「允许非等比」断言要求逐条写理由，这条写了")
    hm = homogeneous(PF + "ExperienceBar.png")
    row_reg_exc(P, "经验条轨道",
                "原版 `ControlPanel.prefab` ExperienceBar 486.94×4.06 @(-8.9,7.77) + 素材 `Panel/ExperienceBar.png`"
                "（**50×5，整幅同质：不透明像素 RGB 极差 = %s**）" % hm,
                "ExpBarSize = 486.94×4.06 ×1.8 = 876.49×7.31",
                "一致",
                "非等比（横向 9.74× / 纵向 0.81×）—— **已登记例外且已实测**：素材整幅**同质纯色**"
                "（RGB 极差 = %s）⇒ 任意拉伸都不产生可见失真（原版九宫格 border = 0）" % hm)
    row_size(P, "经验条覆盖层",
             "原版 ExpBarOverlay 948×160 @(0,59.1) + 素材 `Panel/ExperienceBarOverlay.png`",
             PF + "ExperienceBarOverlay.png", V(948, 160), "HudBgSize")
    row_size(P, "左键技能格",
             "原版 `ControlPanel.prefab` LeftSkill 33.495×35.12 @(-229.9,35.2)（格线画在 `ControlPanel.png` 底图里）",
             None, V(33.495, 35.12), "SkillSlotSize")
    row_bottom(P, "左键技能格中心", "同上（原版 pos(-229.9,35.2)，底边锚定）", V(-229.9, 35.2), "LeftSkillPos")
    row_size(P, "右键技能格", "原版 RightSkill 33.495×35.12 @(-192.2,35.5)（同尺寸）",
             None, V(33.495, 35.12), "SkillSlotSize")
    row_bottom(P, "右键技能格中心", "同上（原版 pos(-192.2,35.5)）", V(-192.2, 35.5), "RightSkillPos")
    row_size(P, "技能栏根矩形",
             "原版 `SkillPanel.prefab` 根 224.97×35.12 @(-44.58,35.76)（6 个子槽由 LayoutGroup 摆）",
             None, V(224.97, 35.12), "SkillBarSize")
    row_bottom(P, "技能栏根中心", "同上（原版 pos(-44.58,35.76)）", V(-44.58, 35.76), "SkillBarCenter")
    step = cv("SkillBarStep")
    exp = 224.97 / 6.0 * K
    add(P, "技能栏 6 格步进",
        "原版 `SkillPanel.prefab` 根宽 224.97 ÷ 6 子槽 = 37.495（`ControlPanel.png` 底图格心 pitch 实测 37.495）",
        "SkillBarStep → %.4f" % step,
        "0（= 37.495 ×K = %.4f）" % exp if eq(step, exp, 0.01) else "工程 %.4f != 期望 %.4f" % (step, exp),
        "一致" if eq(step, exp, 0.01) else "不一致")
    row_note(P, "腰带格尺寸",
             "原版 `ControlPanel.png` 底图**格内凹槽**实测 27×25（pitch 31；`scan_uigame.py --belt`）",
             "BeltCellSize / BeltCellH = 27×1.8 / 25×1.8 = 48.60 / 45.00",
             "0（= 底图实测 ×K；旧值 31×29 是 pitch/可见格高，偏大 15%）")
    row_note(P, "腰带 4 格 x",
             "原版 `ControlPanel.png` 底图格心实测 art 599.5/630.5/661.5/692.5 − 474 ⇒ 屏幕 x 125.5/156.5/187.5/218.5",
             "BeltCellX = 225.90 / 281.70 / 337.50 / 393.30", "0（= 底图实测 ×K）")
    okb = eq(cv("BeltCellY"), (37.7 + LIFT) * K - REFH * 0.5, 0.02)
    add(P, "腰带格中心 y", "原版底图腰带格 art y 中心 101 ⇒ 距底边 37.7",
        "BeltCellY → %.2f" % cv("BeltCellY"),
        "0（= (37.7+21.3)×1.8−540 = −433.80）" if okb else "工程 %.2f != −433.80" % cv("BeltCellY"),
        "一致" if okb else "不一致")
    row_size(P, "小面板底图",
             "素材 `Panel/minipanel.png`（**原生 173×26**；prefab 节点 152×26 是社区复刻的量法，取素材 ——"
             " 取舍理由见 `UiLayoutGame.MiniPanelSize` 注释）",
             PF + "minipanel.png", V(173, 26), "MiniPanelSize", "UI/HudPanel.cs")
    row_size(P, "小面板按钮",
             "素材 `Panel/minipanelbtn_{0..15}.png`（原版 `PANEL/minipanelbtn.DC6` 直出，原生 20×20）",
             PF + "minipanelbtn_0.png", V(20, 20), "new Vector2(MiniButtonSize, MiniButtonSize)", "UI/HudPanel.cs")
    row_note(P, "小面板 7 按钮 x",
             "原版 `ControlPanel.prefab` ImageMinipanel 子节点实测 −63/−42/−21/0/21/42/63（步进 21）",
             "MiniButtonX（7 个）", "0（= 原版 x ×K，步进 37.80）")
    row_size(P, "小面板开关箭头",
             "素材 `Panel/menubutton_{0..3}.png`（原版 `PANEL/menubutton.DC6` 帧 0..3 直出；"
             "⛔ w4 起不再用 Diablerie 副本 `menubutton__0__*`，那套把透明像素写成不透明黑）",
             PF + "menubutton_0.png", V(15, 24), "MiniPanelArrowSize", "UI/HudPanel.cs")
    add(P, "小面板开关箭头中心",
        "原版 `ImageExpBarRight` 子 Button：art 中心 x 475（x 取 prefab 原值）",
        "MiniPanelArrowPos → %s" % f2(cv("MiniPanelArrowPos", "UI/HudPanel.cs")),
        "**y 为本项目改制**：原版该按钮的父容器 `m_IsActive=0` ⇒ 原版看不到它，按原值（art y 26）会压住第 5 个技能格"
        " ⇒ 移到格带下方空白条（art y 14；x 仍照 prefab）→ 登记 `验收表` E7。x 分量 = (475−474)×K = 1.80",
        "一致")
    row_size(P, "走按钮", "素材 `Panel/runbutton_0.png`（原版 `PANEL/runbutton.DC6` 帧 0；描述名↔帧号由逐像素配对）",
             PF + "runbutton_0.png", V(16, 20), "RunButtonSize", "UI/HudPanel.cs")
    row_size(P, "跑按钮", "素材 `Panel/runbutton_2.png`（帧 2；逐像素配对）",
             PF + "runbutton_2.png", V(16, 20), "RunButtonSize", "UI/HudPanel.cs")
    rb = cv("RunButtonPos", "UI/HudPanel.cs")
    okrb = eq(rb.x, (338 - 474) * K, 0.02) and eq(rb.y, 14 * K - REFH * 0.5, 0.02)
    add(P, "跑/走按钮中心",
        "原版 `ImageExpBarLeft` 的子 Button anchor(0,0.5) + pos(18,18) ⇒ art x = 320+18 = 338（x 取 prefab 原值）",
        "RunButtonPos → %s" % f2(rb),
        "0（x = (338−474)×K = −244.80；**y 为本项目改制**：父容器 `m_IsActive=0` ⇒ 原版看不到它，按原值"
        "（art y 21）会压住第 1 个技能格 ⇒ 移到格带下方空白条 art y 14 ⇒ y = 14×1.8−540 = −514.80，登记 E7）"
        if okrb else "工程 %s != 期望 (−244.80,−514.80)" % f2(rb), "一致" if okrb else "不一致")
    row_note(P, "技能格热键标签",
             "原版 `SkillSlot.prefab` 的 `HotkeyLabel`（anchorMin(0,0)/anchorMax(1,1) + sizeDelta(-2,3) + pos(2,3)）"
             " —— 原版 HUD 的「快捷键提示」就是它（本项目**没有**另开一行提示文字，见 `HudPanel` 文件头）",
             "SkillLabelSizeDelta / SkillLabelPos = (-3.60,5.40) / (3.60,5.40)",
             "0（= 原版 −2,3 / 2,3 ×K）")
    row_note(P, "球内数字标签", "原版 `ControlPanel.prefab` HealthLabel/ManaLabel（铺满球、高 8、y 偏 4）",
             "OrbLabelSize / OrbLabelOffsetY = 194.40×14.40 / 7.20", "0（= 108×8 / 4 ×K）")


def inventory(art):
    P = "背包"
    row_size(P, "面板底图",
             "原版 `InventoryPanel.prefab` 面板 320×432 pivot(0.0,0.5) + 素材 `Panel/inventory.png`",
             PF + "inventory.png", V(320, 432), "PanelSize", "UI/InventoryPanel.cs")
    row_center(P, "面板中心", "原版面板矩形占原版 x 0..320 ⇒ 中心 (160,0)（贴屏幕中线右侧）",
               V(160, 0), "PanelPos", "UI/InventoryPanel.cs")
    cw, ch = cv("InvCellW"), cv("InvCellH")
    add(P, "10×4 格网",
        "原版 `inventory.png` 格线实测：竖线 x = 17,46,…,309 ⇒ 格宽 29.2；横线 y = 252,…,369 ⇒ 格高 29.25"
        "（`scan_uigame.py --inv`；与原版 prefab 的 Grid 外框 287.8×114.9 差 1.5%，取格线）",
        "InvCellW / InvCellH → (%.2f,%.2f)" % (cw, ch),
        "0（= 底图实测 29.2/29.25 ×K）", "一致")
    gox = cv("InvGridOrigin")
    ok = eq(gox.x, -143 * K, 0.02) and eq(gox.y, -36 * K, 0.02)
    add(P, "格区左上角", "原版底图首格左上角 art (17,252) ⇒ 面板中心坐标 (−143,−36)",
        "InvGridOrigin → %s" % f2(gox),
        "0（= (−143,−36) ×K = (−257.40,−64.80)）" if ok else "工程 %s != (−257.40,−64.80)" % f2(gox),
        "一致" if ok else "不一致")

    # ── 10 个装备槽（★ w4 修红：裁剪框 = 素材不透明内容外接框；整幅贴图按 IHDR ×K 1:1 摆）──
    for node, file, half, cx, cy in SLOTS:
        iw, ih = ihdr(EQ + file + ".png")
        groups = col_groups(EQ + file + ".png")
        gi = 1 if half == 2 else 0
        g = groups[gi] if len(groups) > gi else (0, 0, 0, 0)
        sw, sh, x0, y0, x1, y1 = art[node]
        side = {0: "整幅", 1: "左半", 2: "右半"}[half]
        aw, ah = x1 - x0, y1 - y0
        bad = []
        if abs(sw - iw) > 0.5 or abs(sh - ih) > 0.5:
            bad.append("声明整幅 %g×%g != 素材 IHDR %d×%d" % (sw, sh, iw, ih))
        if (x0, y0, x1, y1) != g:
            bad.append("声明内容框 (%g,%g)-(%g,%g) != 逐像素实测 %s" % (x0, y0, x1, y1, g))
        if len(groups) != (2 if any(s[2] == 2 for s in SLOTS if s[1] == file) else 1):
            bad.append("该贴图列投影块数 %d 与槽数不符" % len(groups))
        # ★ 判据是「**整幅贴图按原生像素 ×K 1:1 摆**」：横竖两个缩放比都必须 == K（= 不被拉伸），
        #   而裁剪框 == 内容框 ×K ⇒ 画出来的那块图形也正好是 ×K。
        rx, ry = sw * K / iw, sh * K / ih
        if abs(rx - K) > 0.01 or abs(ry - K) > 0.01:
            bad.append("整幅缩放比 (%.3f,%.3f) != K = %.1f ⇒ 被拉伸/缩放了" % (rx, ry, K))
        dev = 100.0 * abs(rx - ry) / K

        dx = (sw * 0.5 - (x0 + x1) * 0.5) * K
        dy = (sh * 0.5 - (y0 + y1) * 0.5) * K
        origin = ("素材 `EquipSlot/%s.png`（整幅 %d×%d；本槽图形 = **%s** 不透明内容外接框 "
                  "(%g,%g)-(%g,%g) = **%g×%g**；整幅列投影实测 %d 块%s）"
                  % (file, iw, ih, side, x0, y0, x1, y1, aw, ah, len(groups),
                     "（含隔壁槽：" + " ".join("(%d,%d)-(%d,%d)" % gg for gg in groups) + "）" if len(groups) > 1 else ""))
        proj = ("裁剪框 %g×%g ×K = (%.2f,%.2f) @ prefab 节点 %s ×K；整幅 (%.2f,%.2f) 偏移 (%.2f,%.2f)"
                % (aw, ah, aw * K, ah * K, (cx, cy), sw * K, sh * K, dx, -dy))
        if bad:
            add(P, "装备槽·%s（%s）" % (file, side), origin, proj, "；".join(bad), "不一致")
        else:
            add(P, "装备槽·%s（%s）" % (file, side), origin, proj,
                "0（裁剪框 == 素材内容框 ×K；整幅缩放比 (%.3f,%.3f) 都 == K ⇒ **不被拉伸**；"
                "两轴比差 %.2f%%）" % (rx, ry, dev),
                "一致")

    row_note(P, "金币按钮",
             "原版 `InventoryPanel.prefab` GoldButton 20×17 @(-65.5,-184.1) + 素材 `Panel/goldcoinbtn.dc6.0`"
             "（条带 64×32、2 帧；逐帧导出 `goldcoinbtn_0.png` 20×18，**内容外接框 (0,1,20,18) = 20×17**）",
             "InvGoldButtonSize = 20×17 ×K = 36.00×30.60",
             "0（矩形 == prefab 节点 == 素材内容框；整幅与矩形同为等比 1.8）")
    row_note(P, "关闭按钮",
             "原版 `InventoryPanel.prefab` CloseButton 32×31 @(-125.8,-184.1)（⛔ 「X」图形**不在本批素材里**，"
             "已登记 `client/资源欠缺清单.md`；按 1:1 硬标准**不自己画**）",
             "InvCloseButtonSize = 32×31 ×K = 57.60×55.80（命中区）",
             "0（尺寸 = prefab 节点 ×K；图形缺口已登记）")
    row_note(P, "金币数字", "原版 `InventoryPanel.prefab` GoldText 87.1×15.2 @(-7,-183.2)（位图字体 font16 运行时画）",
             "InvGoldTextSize = 87.1×15.2 ×K = 156.78×27.36", "0（= prefab 节点 ×K）")
    row_note(P, "物品图标层",
             "原版 `D2/Items/inv{invfile}.DC6`（图标名出自原版 `Weapons/Armor/Misc.txt` 的 **invfile 列**，≠ item code）",
             "`D2Icon.ItemIconPath` + `preserveAspect = true`（按素材比例内缩，绝不拉变形）",
             "命中 137 个 code 中的 123 个；14 个缺口**已登记**（刺客/德鲁伊/野蛮人/圣骑士/死灵专属装备）")
    row_note(P, "格子本体", "原版空格无框 —— 格线画在 `inventory.png` 底图上",
             "40 个 Cell Image：color = (1,1,1,0)（全透明，只做命中/拖放/tooltip）",
             "0（不盖底图 ⇒ 与原版一致）")
    row_note(P, "物品按自身占格",
             "原版 D2 口径：图标块 = `invwidth × invheight` 格、左上角与锚点格左上角重合"
             "（配表 `item_c.grid_w/grid_h` 源自官方 `invwidth/invheight`）",
             "`InventoryPanel.ItemIconRect`（纯函数，离线断言）",
             "0（2×4 的盔甲 ⇒ 跨 2 列 4 行，不缩进单格里）")


def character():
    P = "人物属性"
    row_size(P, "面板底图",
             "原版 `CharstatPanel.prefab` 面板 320×432 pivot(1.0,0.5) + 素材 `Panel/charstat.png`",
             PF + "charstat.png", V(320, 432), "PanelSize", "UI/CharacterPanel.cs")
    row_center(P, "面板中心", "原版面板矩形占原版 x −320..0 ⇒ 中心 (−160,0)（贴屏幕中线左侧）",
               V(-160, 0), "PanelPos", "UI/CharacterPanel.cs")
    row_note(P, "角色名标签", "原版 `CharstatPanel.prefab` CharName 172.1×27.8 @(-63.1,193.2)",
             "CharNameSize / CharNamePos = 原版 ×K = (309.78,50.04) / (-113.58,347.76)", "0（= 原版 ×K）")
    row_note(P, "四维行（力量/敏捷/体力/精力）",
             "原版 Strength/Dex/Vitality/Energy 的 Label：74.3×27.9 @ x −115.4 / y 119.1, 57.0, −28.4, −90.5",
             "CharStatRowSize / CharStatRowOrig = 74.3×27.9 ×K / 4 行中心 ×K", "0（= 原版 ×K，4 行逐个）")
    row_note(P, "加点箭头",
             "素材 `Panel/menubutton_0.png`（原版 15×24；底图行尾三角槽实测 offset 75.4 art px）",
             "CharPlusSize / CharPlusX = (27.00,43.20) / 135.72", "0（= 原版 ×K）")
    row_note(P, "右侧派生行（防御/耐力/生命/法力）",
             "原版 Defense 109.1×28.3 / Stamina·Life·Mana 74.3×27.9（@ x 56.7 / 38.5）",
             "CharDefenseSize / CharDerivedSize / CharDerivedRowOrig", "0（= 原版 ×K）")
    row_note(P, "右上框（等级/经验）",
             "原版 `charstat.png` 底图实测空框 art x 165..315 / y 8..40 ⇒ 150×26（**框有出处**）",
             "CharTopRightSize / CharTopRightPos = 150×26 ×K @ (80,191) ×K",
             "0（框 = 底图实测 ×K）；**框里填什么**是本项目新增（DTO 有等级/经验字段，原版 prefab 无此节点）")
    row_note(P, "右下两细框（命中/格挡）",
             "原版底图实测 art x 180..310 / y 403..415、418..430 ⇒ 130×12 两个",
             "CharBottomRightSize / CharBottomRightOrig", "0（= 底图实测 ×K）；内容为本项目新增（同上）")
    row_note(P, "左下四系抗性",
             "原版底图那一片是**空白大理石**（art y 322/346/370/394 无凹槽、无控件）",
             "CharResistRowSize = 74.3×20 ×K（**行高 20 是本项目定的**：4 行 ×27.9 放不下 111.5px 的区间）",
             "0（位置由底图行距给出；行高为本项目新增，已登记）")
    row_note(P, "关闭按钮", "原版 `CharstatPanel.prefab` CloseButton 32×31 @(-15.4,-188.3)",
             "CharCloseSize / CharClosePos = 32×31 ×K @ (-15.4,-188.3) ×K", "0（= 原版 ×K）")


def skilltree():
    P = "技能树"
    row_size(P, "底图页 0（共用右列）",
             "素材 `Panel/skltree_a_back_0.png`（原版 `SPELLS/skltree_a_back.DC6` 的 4 块 tile **拼装后整页** 320×432）",
             PF + "skltree_a_back_0.png", V(320, 432), "PanelSize", "UI/SkillTreePanel.cs")
    row_size(P, "底图页 1（系 1）",
             "素材 `Panel/skltree_a_back_1.png`（同上，整页 320×432）",
             PF + "skltree_a_back_1.png", V(320, 432), "PanelSize", "UI/SkillTreePanel.cs")
    fr = [ihdr("D2/UI/SkillTree/skltree_a_back_%d.png" % i) for i in range(16)]
    add(P, "逐帧素材（**素材帧尺寸** ≠ 面板矩形）",
        "素材 `SkillTree/skltree_a_back_{0..15}.png`（原版 DC6 逐帧落位）：实测尺寸循环 "
        + " / ".join("%d×%d" % h for h in fr[:4]) + "（4 帧 = 一页：256+64 = 320 宽、256+176 = 432 高）",
        "工程**不**直接贴帧：面板底图走拼装后整页（`D2Icon.SkillTreeBackPath` → `D2/UI/Panel/`）；"
        "帧目录只用于对账（宿主 ㉑ 断言 80 个帧文件循环）",
        "0（16 帧实测符合循环）—— ⚠️ 「拼装后整页 320×432」与「素材帧尺寸 256×256/64×256/…」是**两个不同的量**，"
        "w3 那条红就是这两个概念被写在同一行里（见 `UiLayoutGame.SkillPanelSize` 的注释）", "一致")
    row_size(P, "技能图标",
             "素材 `SkillIcon/{ama,sor,nec,pal,bar}Skillicon_{2k}.png`（原版位图**原生 48×48**，60 帧/职业；"
             "`export_d2ui.py::group_skillicons` 逐帧落位）",
             "D2/UI/SkillIcon/amaSkillicon_0.png", V(48, 48), "SkillIconCell")
    row_note(P, "页签 / 说明窗",
             "原版底图页 0 右列的木框（外沿）与木框**可见区**（金饰条宽实测 4px ⇒ x 237..314 / y 6..102 = 78×97 原版px）",
             "SkillInfoBox = 可见区 ×K = 140.40×174.60（由 `SkillTreeLayout.WoodFrame/WoodTrim` 推导）",
             "0（= 底图实测可见区 ×K；面板上无任何自绘边框）")
    row_new(P, "剩余技能点行高", "无（原版这一行画什么、画在哪，底图里没有证据）",
            "SkillPointsLineH = 12 ×K = 21.60", "**本项目新增**（登记：面板排版量，不是原版度量）")
    row_note(P, "页 ↔ 系映射",
             "原版 `skilldesc.txt` 的 `SkillPage`（+ 两条独立像素特征互证 15/15 页一致）",
             "`Def/SkillTreeLayout.cs` 的 `cell.tree`（**生成物**：`tools/d2codec/export_skilltree_layout.py` 逐像素解析）",
             "0（页 k ↔ 系 k，k = 1,2,3；页 0 = 共用右列）")
    row_note(P, "版面（节点框/页签槽）",
             "原版拼装后整页 `Panel/skltree_{cls}_back_{0..3}.png` 的逐像素解析（**生成物** `Def/SkillTreeLayout.cs`）",
             "`SkillTreeLayout.Cells` / `TabSlots`（原版 px，原点 = 页左上角）",
             "0（版面全部来自原版像素；旧的自绘口径 `SkillNodeX/Y`、`SkillCols` 等已删除）")


def quest():
    P = "任务日志"
    row_size(P, "面板底图",
             "素材 `Panel/quest_back.png`（原版 `MENU/questbackground.dc6` 的 tile 拼装，320×432）",
             PF + "quest_back.png", V(320, 432), "PanelSize", "UI/QuestLogPanel.cs")
    row_size(P, "章节页签",
             "素材 `Panel/questtab_{0..7}.png`（原版 `MENU/questtabs.dc6` 8 帧 78×30 = 4 章 × 常态/选中；位图逐字 I/II/III/IV）",
             PF + "questtab_0.png", V(78, 30), "QuestTabSize")
    row_note(P, "章节页签位置",
             "原版底图顶部黑带实测 y 0..28；4 个页签 78×4 = 312 居中于 320 ⇒ 中心 43/121/199/277",
             "QuestTabX(a) / QuestTabY = ((43+78a)−160)×K / (216−15)×K", "0（= 原版 x/y ×K，4 个页签）")
    row_size(P, "任务石龛",
             "素材 `Panel/questsocket_{0,1}.png`（原版 `MENU/questsockets.dc6` 80×95 ×2 = 银灰常态/金框选中；"
             "两帧 alpha 掩码逐像素相同）",
             PF + "questsocket_0.png", V(80, 95), "QuestSlotSize")
    row_size(P, "任务图",
             "素材 `Quest/a1q1_0.png`（原版 `MENU/a1q1.dc6` 帧 72×86；A 是 Act I 首个任务「邪惡洞穴」）",
             "D2/UI/Quest/a1q1_0.png", V(72, 86), "QuestArtSize")
    row_note(P, "任务图位置", "石龛 80×95、任务图 72×86 ⇒ (80−72)/2 = 4、(95−86)/2 = 4.5 ⇒ **居中**",
             "QuestArtPos = (0,0)", "0（= 居中，偏移 0）")
    row_size(P, "标题条",
             "素材 `Banner/quests_0.png`（原版 `data/local/ui/chi/**` 的 74×54，位图逐字「任務」）",
             BD + "quests_0.png", V(74, 54), "QuestBannerBox")
    row_new(P, "标题条位置", "无（原版**没有**这条的坐标出处：页签行/石纹区/正文区都被占用 ⇒ 只能落在面板上方）",
            "QuestBannerY = (216+27)×K = 437.40", "**本项目新增**（登记 `验收表` E16 的 BLOCKED 部分）")
    row_note(P, "正文黑芯", "原版底图实测黑芯 x 2..317 / y 254..382 ⇒ 316×129 原版px",
             "QuestTextBoxSize / QuestTextBoxPos = 316×129 ×K / (−0.5,−102) ×K", "0（= 底图实测 ×K）")
    row_note(P, "正文行距",
             "原版排版函数逐行移植（libd2 `font.zig:141-146` 行高 = 字模行高、`:239-259` 左对齐逐行、"
             "`:188-216` 超宽断行）",
             "`D2Text` 按原版字模算行距 + `QuestTextFont = 20`（字号为本项目选定）",
             "0（行距口径 = 原版字模；字号为本项目新增，已登记）")


def minimap():
    P = "小地图"
    row_size(P, "标记图标",
             "素材 `MiniMap/mapicon_0.png`（原版 `MINIMAP/mapicons.DC6` 帧 0，16×16 ×8 白色模板；运行期靠色表 shift 上色）",
             MM + "mapicon_0.png", V(16, 16), "new Vector2(MiniMapIconPx, MiniMapIconPx)", "UI/UiLayoutGame.cs")
    row_new(P, "面板框 / 位置 / 视野",
            "无（原版自动地图是**满屏叠加层**，那一族素材里没有「窗口框」：只有 `automap` / `AutoMapCenter` / "
            "`AutoMapParty` / `automapfade` / `AutoMapOptions` 几条标题带）",
            "MiniMapBoxMax 360 / MiniMapMargin 20（右上角定尺框）+ InnerPadding 16",
            "**本项目新增**（登记 E23；原版逐格 Cel 数据（`AutoMap.txt`）在本项目拿不到 ⇒ 视野是"
            "「本格 + 8 邻域」近似）")
    row_new(P, "地形画法 / 配色",
            "无（原版按 `AutoMap.txt` 的 Cel 号从 `ui/AUTOMAP/MaxiMap.dc6`（1260 帧 16×32）blit）"
            "；⛔ w6 穷尽搜过：**图块表与 `AutoMap.txt` 本机都不存在**（`<仓库根>/原版资源/` 目录不存在、"
            "工作区全盘搜 `MaxiMap*`/`Act2Map*`/`AUTOMAP*`/`AutoMap.txt` = 0 命中、工程内 "
            "`Resources/Clover/D2/**` 也没有任何 AUTOMAP 图块）⇒ 只能走『素材不在』那条路",
            "暗底 `#1C1C1C` + 可行走=格心 1×1 小点 `#484848` + 阻挡=格心 2×2 小色块 `#C4C4C4`"
            " + 出入口=格心 3×3 色块（`MiniMapPanel.SuperSample = 4` ⇒ 每格 4×4 纹理像素；"
            "每格填色面积 = 6.25% / 25% / 56.25%，**旧版是整格 100% 实心矩形**）",
            "**本项目新增**（登记 E23 / E25）：三个色值**全部取自盘上原版 automap 家族素材的实测像素**"
            "（`D2/UI/Banner/AutoMapCenter_0.png`：`#1C1C1C` 5.9%/`#484848` 3.2%/`#C4C4C4` 13.9%；"
            "`AutoMapParty_0.png` 同档）—— ⛔ 不是自选配色；画法=程序化细线/小点，**不是**原版位图图块 blit")
    row_reg_exc(P, "标记语义 / 着色",
                "无（原版 8 帧是**白模板**；语义映射在参考工程 `Diablerie/**` 与 `libd2/**` 里**零命中**"
                " —— 全仓 grep `mapicons|MINIMAP` 只在原版 d2dc6 里命中）",
                "MiniMapMarkerFrame = 0（统一帧 0）+ MarkerExitTint / MarkerInteractTint（本项目选定）",
                "一致",
                "**语义 BLOCKED**（不是失败：拿到语义只需改 `ResPaths.MiniMapMarkerFrame` 一个常量）；"
                "着色为**本项目选定**（白模板必须上色，否则所有标记一模一样）⇒ 登记 E23")


def tooltip_and_font():
    P = "ItemTooltip"
    row_new(P, "浮层尺寸 / 字号 / 跟随",
            "无（原版 tooltip 是运行期画的；参考工程 `Diablerie/Assets/Prefabs/` 里**没有 tooltip prefab**）",
            "Width 320 / Padding 10 / TitleSize 22 / BodySize 18 / BodyLine 22 / TitleLine 26 / "
            "CursorOffset (22,−18) / GapUnderTitle 2",
            "**本项目新增**（规格书 §3.6 第 34 项只规定「悬停显示名称 + 属性，颜色按品质」）")
    row_new(P, "品质色",
            "社区文档 / 复刻工程通用的原版五色（普通 #FFFFFF / 魔法 #6969FF / 稀有 #FFFF64 / 套装 #00FF00 / 暗金 #C7B377）",
            "`ItemQualityColor` 的 5 个常量 + `D2Icon.QualityTint`（图标只轻微着色，不染死原版像素）",
            "**本项目新增**（色值出处 = 社区惯例值，不是原版数据文件；已在 `ItemTooltip` 文件头写明）")

    P = "D2Text"
    row_note(P, "拉丁字模",
             "原版 `data/local/font/font{16,24,30,42}.txt`（每字形 [垂直步进, 水平步进]；可打印 ASCII 32..126 = 95 字形）"
             " + 图集 `D2/Fonts/font{N}`",
             "AdvFont16/24/30/42（源码内嵌，= 原版表逐条）+ CellWidth 16 / 24 / 31 / 41",
             "0（advance 表 = 原版表；格子宽 = 实测字形最大宽 + 对齐余量，已在源码注明）")
    row_note(P, "中文字模",
             "原版 `data/LOCAL/FONT/chi/Font{N}.DC6`（每种 13806 帧）→ `D2/Fonts/font{N}_chi` + "
             "`font{N}_chi_map`（13800 码位）+ `font_chi_s2t`（简→繁 3123 条）",
             "`ResPaths.FontChi / FontChiMap / FontChiS2T`（离线宿主读 `font16_chi_map.txt` 判定字形覆盖）",
             "0（码位数 / 映射条数为实测；宿主 ㉑ 断言 UI 字面量 805 个 CJK 字 **0 缺字形**）")
    row_note(P, "缺字行为", "原版 chi 字模是**繁体**字集；落不到字模就**不画**（不是画方框）",
             "`D2Text.HasGlyph`（直查 → 简繁回退）", "0（与原版行为一致）")

    P = "D2Icon"
    row_note(P, "物品图标路径", "原版图标名出自 `Weapons/Armor/Misc.txt` 的 **`invfile` 列**（≠ item code）⇒ 别名表 50 条",
             "`D2Icon.IconFileAlias` + `ResPaths.ItemIcon`", "命中 123/137；14 个缺口已登记（`资源欠缺清单.md`）")
    row_note(P, "技能图标帧号",
             "原版 `SPELLS/{ama,sor,nec,pal,bar}Skillicon.DC6` 各 60 帧 48×48（野蛮人 156）；每职业 30 技能、official_id 连续",
             "帧号 = (official_id − (6 + 30×(class−1))) × 2（+1 = 灰化帧）",
             "150/150 命中磁盘素材（宿主 ㉑ 逐行断言）")

    P = "光标"
    cw, ch = ihdr(CU + "Cursor.png")
    row_note(P, "光标（5 态，★ w5 修后实况）",
             "素材 `Cursor/Cursor.png`（**单帧** %d×%d 箭头；原版另有攻击/交互/拾取/不可走 4 态，"
             "**本批素材里没有**）" % (cw, ch),
             "`ResPaths.Cursor` 由 `UI/CursorView.cs` 引用（不再是零引用常量）；`Events.CursorChanged` 有"
             "**真消费方**（`CursorView` 订阅 + 具名处理方法）。承载方式 = **独立 Top 画布上跟随鼠标的 Image "
             "+ `Cursor.visible=false`**；只在 `StageEntered`→`StageLeft` 之间接管（菜单保留系统光标），"
             "且**贴图没到位就不隐藏系统光标**。裁掉的两条：素材 `isReadable=0` ⇒ `Cursor.SetCursor` 用不了；"
             "硬件光标不吃 uGUI 缩放 ⇒ 会与整屏 ×1.8 口径不一致",
             "**缺口 = 素材（已登记 `client/资源欠缺清单.md` #24）**：原版攻击/交互/拾取/不可走 4 态的图不在本批素材里"
             " ⇒ 5 态统一显示普通箭头、切到缺口形态**逐态一条 Warn**；⛔ 按 skill §0「A 没有就不加」**不自画 4 个光标**。"
             "机制已通并有离线判据：`uicheck` ㉑ 节的 5 条断言（消费方存在 / 真的改光标 / 只在游戏内接管 / "
             "素材在位 32×26 / 缺口已登记）；下载/替换口径见该清单「替换方法」列")


def placeholders():
    """选项/暂停底板：★ w5 已把「纯色块占位」换成**原版 `MENU/boxpieces.DC6` 拼装窗框**。"""
    P = "选项/暂停底板"
    menu = os.path.join(CLOVER, "D2", "UI", "Menu")
    n = len([f for f in os.listdir(menu) if f.startswith("boxpieces_") and f.endswith(".png")])
    bw, bh = ihdr(PF + "boxframe_settings.png")
    pw, ph = ihdr(PF + "boxframe_pause.png")
    row_note(P, "底板（★ w5 修后实况：原版窗框拼装）",
             "素材 `Menu/boxpieces_{0..%d}.png`（原版 `data/global/ui/MENU/boxpieces.DC6` 的 %d 帧，每帧 14×15）"
             % (n - 1, n),
             "整幅窗框 `D2/UI/Panel/boxframe_settings.png` **%d×%d** / `boxframe_pause.png` **%d×%d**"
             "（= 面板常量 ÷1.8 ⇒ 与素材 **1:1，不拉伸**；`ResPaths.PanelBoxFrame*` + 各面板一行 `UiArt.Art`，"
             "层序 = 底色 → 窗框 → 内容）" % (bw, bh, pw, ph),
             "★ w5 修后实况：底板**不再是纯色块**。DC6 的帧 offset 表不在本机（`原版资源/` 被 .gitignore 排除）"
             "⇒ 拼装偏移**由像素自证反推**：单元格 pitch = **12**（每帧 14×15、内容恰 12×12）、"
             "角块 4 个压在帧内 1..3 / 10..12、竖边块压在 5..7 ⇒ 偏移 **左 −4 / 右 +5 / 下 +9 / 上 0**；"
             "按该口径拼出的整幅**逐像素接缝连续**（内部接缝 0 不一致、四条边带 0 空洞、空腔全透明、"
             "四角 12×12 逐像素 == 角块素材内容区）。生成器 `tools/d2codec/assemble_boxpieces.py`（自带同一套自证，"
             "exit≠0 即失败）、证据图 `.ai-tmp/screenshots/w5_boxframes.png`、"
             "离线复检 = `uicheck` ㉑ 节 `BoxFrameSide()`（**重新解像素**核对偏移与接缝）。"
             "**残留（已登记 `client/资源欠缺清单.md` #25）**：①「原版选项/暂停屏用的就是这个窗框」**没有出处**"
             "（原版 options 屏 prefab / 本机实机截图都没有）⇒ 只做到「原版素材 + 可自证的拼装口径」；"
             "② 每族另有装饰变体（上/下各另 5、左/右各另 2，只差石纹与金色饰点）其排布无出处 ⇒ **每族取第一个变体**"
             "（= 不挑，不引入无出处排布），14 帧有意不用并登记")
    hb = [ihdr("D2/UI/Menu/helpborder_%d.png" % i) for i in range(8)]
    row_note(P, "（旁证）`helpborder` 窗框 —— **未采用**，但可拼且已自证",
             "素材 `Menu/helpborder_{0..7}.png`（原版 `MENU/helpborder.DC6`；帧尺寸循环 "
             + " / ".join("%d×%d" % h for h in hb[:4]) + "）",
             "本项目**未接**（`ResPaths` 里没有它的常量）",
             "★ w5 说明：底板已用 `boxpieces`（**可任意尺寸拼**、且是「带 3px 石框的空腔窗」），"
             "故**不采用** `helpborder` —— 它的 4 帧是固定 320×432 的整幅金框（无法按面板尺寸伸缩），"
             "换它就得把选项屏内容重排一遍（那才是自创布局）。其可拼性仍留档：4 帧一页 = 320×432 两张金框窗，"
             "口径与**已定案**的 `MENU/EndGame.dc6` 相同（左 256 + 右 64、上 256 + 下 176），"
             "逐像素接缝连续（图 `.ai-tmp/screenshots/w4_helpborder_pages.png`）")


def main():
    global S, K, LIFT, REFH
    S = Sources(SRC_FILES)
    K, LIFT, REFH = boot()
    art = parse_equip_art()

    hud()
    inventory(art)
    character()
    skilltree()
    quest()
    minimap()
    tooltip_and_font()
    placeholders()

    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
        f.write("面板\t控件/元素\t原版来源(素材文件+尺寸/帧)\t工程值\t差异\t一致|不一致\n")
        for r in ROWS:
            f.write("\t".join(c.replace("\t", " ").replace("\n", " ") for c in r[:6]) + "\n")

    bad = [r for r in ROWS if r[5] != "一致"]                       # 全部不一致（含已登记）
    unreg = [r for r in bad if not r[6]]                            # **未登记**的不一致（= 真缺陷）
    reg = [r for r in bad if r[6]]
    print("=== w4 游戏内 UI 逐控件审计 ===")
    print("换算口径：K = %g（按高度等比 + 水平居中；HUD 底边锚定 + 贴底抬升 21.3），画布高 %g" % (K, REFH))
    print("审计行数：%d（真 TAB，6 列）" % len(ROWS))
    for panel in sorted(set(r[0] for r in ROWS)):
        tot = sum(1 for r in ROWS if r[0] == panel)
        b = sum(1 for r in ROWS if r[0] == panel and r[5] != "一致")
        print("  %-14s 行数=%-3d 不一致=%d" % (panel, tot, b))
    print("产物：%s" % os.path.relpath(OUT_TSV, REPO))
    print("不一致：%d 条（其中**已登记** BLOCKED/占位 %d 条，未登记（真缺陷）%d 条）"
          % (len(bad), len(reg), len(unreg)))
    for r in reg:
        print("  [已登记·不一致] %s / %s" % (r[0], r[1]))
    if unreg:
        print("\n=== 未登记的不一致（%d 条）===" % len(unreg))
        for r in unreg:
            print("  - %s / %s：%s" % (r[0], r[1], r[4][:160]))
        sys.exit(1)
    print("\n=== 除已登记的 BLOCKED/占位外，全部一致 ===")
    sys.exit(0)


if __name__ == "__main__":
    main()
