# -*- coding: utf-8 -*-
"""
w3 片 · 流程屏（启动 / 菜单 / 选角 / 创角 / 读条 / 设置 / 暂停 / 死亡）**逐屏逐控件 UI 表现审计**。

产物：`<仓库根>/.ai-tmp/screenshots/w3_uiflow_audit.tsv`，6 列（真 TAB）：
    屏 <TAB> 控件 <TAB> 原版来源(素材文件/尺寸/offset) <TAB> 工程值(size/pos/sprite/字号) <TAB> 差异 <TAB> 一致|不一致

判据（⛔ 不自创数值，每一个数都要能指出出处）：
  ① **原版来源**三类，逐行写在表里：
     a) 原版素材帧：`client/Assets/Resources/Clover/D2/UI/**` 的 PNG —— 尺寸取 **IHDR 实测**（不是注释）；
     b) 原版 prefab 矩形/位置：社区复刻工程 `mofr/Diablerie` 的
        `Prefabs/Menu/{MainMenu,ClassSelectMenu,WideButton,MediumButton}.prefab`
        （原版位置写成本表里的**字面量**，逐条与 `UI/UiLayoutFlow.cs` 注释里的 prefab 值一致）；
     c) 本项目新增（原版没有的）⇒ 显式标「无（本项目新增，登记）」。
  ② **换算口径** = `UI/UiLayoutFlow.cs` 的 `Scale = 1080/600 = 1.8`（按高度等比 + 水平居中）。
     工程值**从 C# 源码解析回来**（不在本脚本里重抄一遍 —— 重抄两份必然漂移，那就不叫审计了）。
  ③ 每一行都重算 `工程值 == 原版值 × 1.8`（容差 0.01 画布单位）+ 素材 IHDR == 声明的原版尺寸
     + 矩形宽高比 == 素材宽高比（防非等比拉伸）。
  ④ 逐屏再做两件事：**元件两两不重叠**（透明热点/容器/底板/遮罩按既有豁免）+ **全部落在 1920×1080 画布内**。

权威复核：同一批结论在离线宿主 `tools/probes/hosts/uicheck`（节 ⑲ `W3FlowCheck.cs`）
用**编译后的真常量/真函数**再断言一遍 —— 本脚本是"人读的审计单"，宿主是"机器闸门"，两边独立实现。

用法（仓库根）：  python tools/probes/measure/w3_uiflow_audit.py
退出码：0 = 全部 `一致`；1 = 有 `不一致`。
"""

import os
import re
import struct
import sys

# Windows 控制台默认 GBK ⇒ 中文会抛 UnicodeEncodeError；输出固定 UTF-8（不改任何文件编码）
try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

# ─────────────────────────────────────────────────────────────────────────────
# 0. 路径与口径
# ─────────────────────────────────────────────────────────────────────────────
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
UI_DIR = os.path.join(REPO, "client", "Assets", "Scripts", "UI")
CLOVER = os.path.join(REPO, "client", "Assets", "Resources", "Clover")
OUT_DIR = os.path.join(REPO, ".ai-tmp", "screenshots")
OUT_TSV = os.path.join(OUT_DIR, "w3_uiflow_audit.tsv")

FLOW_SRC = os.path.join(UI_DIR, "UiLayoutFlow.cs")

SCALE = 1.8          # = 1080/600（下面从源码复核）
TOL = 0.01           # 画布单位容差


# ─────────────────────────────────────────────────────────────────────────────
# 1. C# 常量解析（含嵌套类前缀）
# ─────────────────────────────────────────────────────────────────────────────
class V(object):
    """二维值（对应 `Vector2`）。"""

    __slots__ = ("x", "y")

    def __init__(self, x, y):
        self.x = float(x)
        self.y = float(y)

    def __repr__(self):
        return "V(%g,%g)" % (self.x, self.y)

    def _both(self, o):
        return (o.x, o.y) if isinstance(o, V) else (o, o)

    def __mul__(self, o):
        a, b = self._both(o)
        return V(self.x * a, self.y * b)

    __rmul__ = __mul__

    def __truediv__(self, o):
        a, b = self._both(o)
        return V(self.x / a, self.y / b)

    def __add__(self, o):
        a, b = self._both(o)
        return V(self.x + a, self.y + b)

    __radd__ = __add__

    def __sub__(self, o):
        a, b = self._both(o)
        return V(self.x - a, self.y - b)


def _strip_literals(text):
    """去注释**与字符串/字符字面量**。

    ⚠️ 为什么要连字面量一起去：`_class_chain` 用**括号配平**求类的词法作用域，而 C# 字符串里
    满是 `{` / `}`（`string.Format("{0} 原版 ({1,7:0.##}) …")`）⇒ 不剥掉就会把作用域算乱
    （实测：嵌套类前缀拼成 8 个兄弟类串在一起）。
    """
    out = []
    i = 0
    n = len(text)
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
        if c == "@" and i + 1 < n and text[i + 1] == '"':          # 逐字字符串 @"..."
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
        if c == '"' or c == "'":                                  # 普通字符串 / 字符字面量
            q = c
            i += 1
            while i < n:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == q:
                    i += 1
                    break
                i += 1
            out.append(" ")
            continue
        out.append(c)
        i += 1
    return "".join(out)


_CLASS_RX = re.compile(r"\bclass\s+([A-Za-z_]\w*)")


def _class_chain(src):
    """[(类开括号下标, 类闭合括号下标, [最外层类名 … 本类名])]。

    只认 `class X`；用括号配平求作用域。顶层类名（本文件自己的类）会在取前缀时被去掉。
    """
    spans = []
    opens = []
    for i, ch in enumerate(src):
        if ch == "{":
            opens.append(i)
        elif ch == "}":
            if opens:
                spans.append((opens.pop(), i))

    def close_of(o):
        for a, b in spans:
            if a == o:
                return b
        return None

    out = []
    for m in _CLASS_RX.finditer(src):
        o = src.find("{", m.end())
        if o < 0:
            continue
        c = close_of(o)
        if c is None:
            continue
        # 真包含本类的那些类（含本类自己）：`o2 <= o and c2 >= c`
        chain = []
        for m2 in _CLASS_RX.finditer(src):
            o2 = src.find("{", m2.end())
            if o2 < 0:
                continue
            c2 = close_of(o2)
            if c2 is None:
                continue
            if o2 <= o and c2 >= c:
                chain.append((o2, m2.group(1)))
        chain.sort()
        out.append((o, c, [name for _, name in chain]))
    return out


CONST_RX = (
    re.compile(r"public\s+const\s+float\s+([A-Za-z_]\w*)\s*=\s*([^;]+);"),
    re.compile(r"public\s+static\s+readonly\s+Vector2\s+([A-Za-z_]\w*)\s*=\s*([^;]+);"),
    re.compile(r"public\s+static\s+readonly\s+float\s+([A-Za-z_]\w*)\s*=\s*([^;]+);"),
    re.compile(r"public\s+const\s+int\s+([A-Za-z_]\w*)\s*=\s*([^;]+);"),
)


def parse_constants(path):
    raw = _strip_literals(open(path, encoding="utf-8").read())
    chains = _class_chain(raw)
    found = {}
    for rx in CONST_RX:
        for m in rx.finditer(raw):
            idx = m.start()
            prefix = ""
            for o, c, chain in chains:
                if o < idx < c:
                    prefix = ".".join(chain[1:])
            key = (prefix + "." + m.group(1)) if prefix else m.group(1)
            found[key] = m.group(2).strip()
    return found


def _strip_f(expr):
    return re.sub(r"(\d)[fF]\b", r"\1", expr)


FUNCS = {}


def _prep(expr):
    s = _strip_f(expr)
    s = s.replace("new Vector2", "V").replace("Vector2.one", "V(1.0, 1.0)")
    s = s.replace("Vector2.zero", "V(0.0, 0.0)")
    return s


def _identifiers(expr):
    """表达式引用的**常量基名**（`A.b.x` → 归一到 `A.b`：`.x/.y/.z` 是属性访问，不是名字的一部分）。"""
    out = []
    for m in re.finditer(r"[A-Za-z_]\w*(?:\.\w+)*", expr):
        base = m.group(0)
        while True:
            head, _, tail = base.rpartition(".")
            if head and tail in ("x", "y", "z"):
                base = head
            else:
                break
        if base in FUNCS:
            continue
        out.append(base)
    return out


class Consts(object):
    def __init__(self, path, strict=True):
        self.path = path
        self.strict = strict
        self.raw = parse_constants(path)
        self.val = {}
        self.unresolved = []
        self._resolve()

    def _resolve(self):
        done = self.val
        pending = dict(self.raw)
        for _ in range(200):
            if not pending:
                break
            progress = False
            for key in list(pending.keys()):
                expr = _prep(pending[key])
                own = key.rsplit(".", 1)[0] + "." if "." in key else ""
                ok = True
                for name in _identifiers(expr):
                    hit = name if name in done else (own + name if (own + name) in done else None)
                    if hit is not None:
                        v = done[hit]
                        lit = ("V(%r, %r)" % (v.x, v.y)) if isinstance(v, V) else repr(v)
                        expr = re.sub(r"\b" + re.escape(name) + r"\b", lit, expr)
                    elif name in FUNCS:
                        continue
                    else:
                        ok = False
                        break
                if not ok:
                    continue
                try:
                    val = eval(expr, {"__builtins__": {}}, dict(FUNCS))
                except Exception:
                    continue
                if isinstance(val, tuple) and len(val) == 2:
                    val = V(val[0], val[1])
                done[key] = val
                del pending[key]
                progress = True
            if not progress:
                break
        self.unresolved = sorted(pending.keys())
        if pending and self.strict:
            raise SystemExit("[FATAL] %s 里以下常量解析不出来：\n  " % os.path.basename(self.path)
                             + "\n  ".join("%s = %s" % (k, pending[k]) for k in self.unresolved))

    def __call__(self, expr):
        e = _prep(expr)
        for name in sorted(self.val.keys(), key=len, reverse=True):
            if re.search(r"\b" + re.escape(name) + r"\b", e):
                v = self.val[name]
                lit = ("V(%r, %r)" % (v.x, v.y)) if isinstance(v, V) else repr(v)
                e = re.sub(r"\b" + re.escape(name) + r"\b", lit, e)
        try:
            val = eval(e, {"__builtins__": {}}, dict(FUNCS))
        except Exception as ex:
            raise SystemExit("[FATAL] 表达式求值失败：%r\n  → %s\n  → %s" % (expr, e, ex))
        if isinstance(val, tuple) and len(val) == 2:
            val = V(val[0], val[1])
        return val


def _as_v(v):
    """`(319,177)` 这种 python 元组字面量也当二维值用（C# 里写的是 `new Vector2(...)`）。"""
    return V(*v) if isinstance(v, tuple) else v


FUNCS.update({
    "Px": lambda v: _as_v(v) * SCALE,
    "Orig": lambda v: _as_v(v) / SCALE,
    "V": V,
})


def _split_top_level(s):
    """按**顶层**逗号切分参数表（跳过嵌套括号里的逗号）。"""
    out, depth, cur = [], 0, []
    for ch in s:
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
        if ch == "," and depth == 0:
            out.append("".join(cur))
            cur = []
            continue
        cur.append(ch)
    out.append("".join(cur))
    return out


def parse_portrait_states(path):
    """解析 `public static readonly PortraitState Xxx = new PortraitState("…","…", pos, size);`

    这类**对象初始化**不是标量常量，通用常量解析器抓不到；但它们是本审计的判据
    （`ClassMenu.Spot.{Amazon,Barbarian}Nu{1,2,3}` 的 OrigPos/OrigSize/Pos/Size）⇒ 单独解析。
    字符串参数已被 `_strip_literals` 换成空格 ⇒ 第 3/4 个参数就是两个 `new Vector2(...)`。
    """
    raw = _strip_literals(open(path, encoding="utf-8").read())
    found = {}
    for m in re.finditer(r"PortraitState\s+([A-Za-z_]\w*)\s*=\s*new\s+PortraitState\s*\(", raw):
        start = m.end()
        depth = 1
        i = start
        while i < len(raw) and depth > 0:
            if raw[i] == "(":
                depth += 1
            elif raw[i] == ")":
                depth -= 1
            i += 1
        # 参数自带换行 + 缩进 ⇒ eval 前必须 `.strip()`（否则 `unexpected indent`，实测踩到）
        args = [a.strip() for a in _split_top_level(raw[start:i - 1])]
        if len(args) < 4:
            continue
        try:
            o_pos = eval(_prep(args[2]), {"__builtins__": {}}, dict(FUNCS))
            o_sz = eval(_prep(args[3]), {"__builtins__": {}}, dict(FUNCS))
        except Exception:
            continue
        o_pos, o_sz = _as_v(o_pos), _as_v(o_sz)
        if not isinstance(o_pos, V) or not isinstance(o_sz, V):
            continue
        # 所在类前缀（这些都在 `ClassMenu.Spot` 里）
        key = "ClassMenu.Spot." + m.group(1)
        found[key + ".OrigPos"] = o_pos
        found[key + ".OrigSize"] = o_sz
        found[key + ".Pos"] = o_pos * SCALE          # `PortraitState` 构造里就是 `Px(origPos)`
        found[key + ".Size"] = o_sz * SCALE
    return found


F = Consts(FLOW_SRC)
F.val.update(parse_portrait_states(FLOW_SRC))

# ── 死亡屏几何：**公式逐行转录** `UI/UiLayoutGame.cs:921-989` ──────────────────
#   不解析该文件（它引用了 `UiArt` / `SkillTreeLayout` 与局部辅助函数 `S()/BottomY()`，
#   通用解析器覆盖不到）⇒ 这里只转录公式；**权威复核在 uicheck 节 ⑲**
#   （用编译后的真函数 `DeathTilePos/DeathTileSize/DeathRowCenterY` 断言同一批值）。
DEATH_TILES = [(256, 256, 0, 0), (64, 256, 256, 0), (256, 224, 0, 256), (64, 224, 256, 256)]
DEATH_BACK_W, DEATH_BACK_H = 320.0, 480.0
DEATH_BANNER_H, DEATH_HINT_H, DEATH_BUTTON_H, DEATH_ROW_GAP = 54.0, 25.0, 32.0, 15.0
DEATH_HINT_W = DEATH_BACK_W - 48.0


def death_tile_size(i):
    w, h, _, _ = DEATH_TILES[i]
    return (w * SCALE, h * SCALE)


def death_tile_pos(i):
    w, h, x, y = DEATH_TILES[i]
    return ((x + w / 2.0 - DEATH_BACK_W / 2.0) * SCALE, -(y + h / 2.0 - DEATH_BACK_H / 2.0) * SCALE)


def death_row_center_y(row):
    hs = [DEATH_BANNER_H, DEATH_HINT_H, DEATH_BUTTON_H]
    total = sum(hs) + DEATH_ROW_GAP * (len(hs) - 1)
    y = total / 2.0
    for i in range(row):
        y -= hs[i] + DEATH_ROW_GAP
    return (y - hs[row] / 2.0) * SCALE


# 选角屏行栈（与 `UiLayoutFlow.Select.RowY` 同公式；C-1 自检会钉住它）
_SEL_LIST = F("Select.ListPos")
_SEL_ROW_H = F("Select.RowH")
_ROW_STEP = F("RowStep")


def select_row_y(row):
    y0 = (F("Select.ListSize").y - _SEL_ROW_H) * 0.5 - row * _ROW_STEP
    return _SEL_LIST.y + y0


def select_row_pos(row, dx):
    return "(%r,%r)" % (_SEL_LIST.x + dx, select_row_y(row))


# ─────────────────────────────────────────────────────────────────────────────
# 2. 素材 IHDR
# ─────────────────────────────────────────────────────────────────────────────
def png_size(res_path):
    p = os.path.join(CLOVER, res_path.replace("/", os.sep) + ".png")
    if not os.path.exists(p):
        return None
    with open(p, "rb") as f:
        head = f.read(24)
    w, h = struct.unpack(">II", head[16:24])
    return (w, h)


# ─────────────────────────────────────────────────────────────────────────────
# 3. 逐屏逐控件的审计行
#    row(屏, 控件, 原版来源, 原版尺寸, 原版位置, 工程尺寸, 工程位置, 素材, 工程值补充, 字号)
#    原版尺寸/位置 = prefab 字面量（与 UiLayoutFlow 注释逐条对齐）；工程值 = 常量名（脚本解析回来）。
# ─────────────────────────────────────────────────────────────────────────────
ROWS = []
NEW = "无（本项目新增，登记）"


def row(screen, ctrl, origin, osize, opos, psize, ppos, asset=None, extra="", font=None, reg=None):
    """`reg` = **已登记的差异**（存在但**不判** `不一致`，理由逐条写在里面；stdout 会另列一节）。"""
    ROWS.append(dict(screen=screen, ctrl=ctrl, origin=origin, osize=osize, opos=opos,
                     psize=psize, ppos=ppos, asset=asset, extra=extra, font=font, reg=reg))


# ① 启动屏 ───────────────────────────────────────────────────────────────────
row("Boot", "整屏纯色底 Bg", NEW, None, None, None, None, None, "Color(0.02,0.02,0.03,1)")
row("Boot", "原版 logo", "原版 `data/global/ui/Logo/logo.DC6` 帧 0（DIABLO II 火焰字标）",
    "319,177", "(0,100)", "Px((319,177))", "(0,180)", "D2/UI/Logo/logo_0", "sprite=logo_0")
row("Boot", "标题行（暗黑破坏神 II · 复刻）", NEW, "(700,20)", "(0,-16.6667)", None, None, None,
    "1260×36 @ (0,-30)", "font16 档（中文位图字模）")
row("Boot", "提示行（按任意键 或 点击鼠标继续）", NEW, "(500,20)", "(0,-66.6667)", None, None, None,
    "900×36 @ (0,-120)", "font16 档（中文位图字模）")
row("Boot", "署名行 by clover-engine", "skill §1.6 硬规则（本项目新增，几何 = Brand 共用）",
    "Brand.ByLineOrigSize", "Brand.ByLineOrigPos", "Brand.ByLineSize", "Brand.ByLinePos", None,
    "900×54 @ (0,-462)", "font24 档（中文位图字模）")
row("Boot", "版权行", NEW, "(700,40)", "(0,-166.6667)", None, None, None, "1260×72 @ (0,-300)", "font16 档")
row("Boot", "版本行", NEW, "(300,20)", "(-230,-204.1667)", None, None, None,
    "540×36 @ (-414,-367.5)", "font16 档")

# ② 主菜单 ───────────────────────────────────────────────────────────────────
row("MainMenu", "整屏贴图", "原版 `data/global/ui/MENU/main_screen`（前端主菜单画）",
    "800,600", "(0,0)", "(OrigWidth*Scale,OrigHeight*Scale)", "(0,0)",
    "D2/UI/Menu/main_screen", "sprite=main_screen（居中 1440×1080，**不横向拉伸**）")
row("MainMenu", "SINGLE PLAYER 按钮", "原版 `MainMenu.prefab` SinglePlayerButton 槽（m_Text=SINGLE PLAYER）",
    "WideButtonOrig", "(0,-17.5)", "WideButton", "Menu.SinglePos",
    "D2/UI/Menu/btn_wide_normal", "sprite=btn_wide_normal；按下=btn_wide_pressed",
    "原版字号 18（ButtonFontScale=18/16）")
row("MainMenu", "EXIT 按钮", "原版 `MainMenu.prefab` ExitButton 槽（m_Text=EXIT）",
    "WideButtonOrig", "(0,-152.5)", "WideButton", "Menu.ExitPos",
    "D2/UI/Menu/btn_wide_normal", "sprite=btn_wide_normal", "原版字号 18")
row("MainMenu", "原版第 2/3 槽（MULTIPLAYER / CINEMATICS）",
    "原版 `MainMenu.prefab` 有这两项（槽位 = 272×35 @ (0,-62.5) 与 (0,-107.5)）",
    "WideButtonOrig", "(0,-62.5)", None, None, None,
    "**按钮不建**（用户 2026-09-19 点名删；`Menu.MultiPos`/`Menu.CinematicsPos` 槽位常量留在对照表 "
    "⇒ 这条偏离可被离线断言看见）")
row("MainMenu", "署名行 by clover-engine", "skill §1.6 硬规则（本项目新增）",
    "Brand.ByLineOrigSize", "Brand.ByLineOrigPos", "Brand.ByLineSize", "Brand.ByLinePos", None,
    "900×54 @ (0,-462)", "font24 档")

# ③ 选角屏 ───────────────────────────────────────────────────────────────────
row("CharSelect", "整屏贴图", "原版 `data/global/ui/MENU/class_select_screen`", "800,600", "(0,0)",
    "(OrigWidth*Scale,OrigHeight*Scale)", "(0,0)", "D2/UI/Menu/class_select_screen",
    "sprite=class_select_screen")
row("CharSelect", "标题（SELECT HERO）", "原版 `ClassSelectMenu.prefab` SelectHeroClass 文本框",
    "(493,30)", "(0,267)", "ClassMenu.TitleSize", "ClassMenu.TitlePos", None,
    "887.4×54 @ (0,480.6)", "font24 档，UpperLeft")
row("CharSelect", "角色信息行", "原版 `ClassSelectMenu.prefab` ClassName 文本框",
    "(493,30)", "(0,198)", "ClassMenu.NameRowSize", "ClassMenu.NameRowPos", None,
    "887.4×54 @ (0,356.4)", "font24 档，UpperLeft")
row("CharSelect", "职业说明行", "原版 `ClassSelectMenu.prefab` ClassDescription 文本框",
    "(300,50)", "(1,155)", "ClassMenu.DescSize", "ClassMenu.DescPos", None,
    "540×90 @ (1.8,279)", "font16 档，MiddleCenter")
row("CharSelect", "角色列表容器", NEW, "(714,322.5)", "(13,-51.25)", "Select.ListSize", "Select.ListPos",
    None, "1285.2×580.5 @ (23.4,-92.25)")
# 角色行是**本项目新增**元件（原版屏上没有角色列表）⇒ 没有"原版值"可对照，
# 但它的**几何必须能被算出来并落在容器内**（这才是"该有却没有/越界/重叠"的判据）
# ⇒ 这里登记的是**画布绝对坐标**（容器中心 + 行内偏移），由 select_row_pos() 统一算出。
row("CharSelect", "角色行内·角色名（第 1 行）", NEW, None, None, "Select.RowNameSize",
    select_row_pos(0, -450.0), None, "行中心 y = Select.ListPos.y + Select.RowY(0)", "font24 档")
row("CharSelect", "角色行内·职业（第 1 行）", NEW, None, None, "Select.RowClassSize",
    select_row_pos(0, -171.0), None, "同上", "font16 档")
row("CharSelect", "角色行内·等级（第 1 行）", NEW, None, None, "Select.RowLevelSize",
    select_row_pos(0, 45.0), None, "同上", "font16 档")
row("CharSelect", "角色行内·ENTER（第 1 行）", NEW, None, None, "MediumButton",
    select_row_pos(0, 261.0), "D2/UI/Menu/btn_med_normal",
    "sprite=btn_med_normal；悬停=btn_med_sel；行内 x 是本项目新增排布（口径 = 原版按钮宽 128 ×1.8 = 230.4 依次排开）",
    "原版字号 18")
row("CharSelect", "角色行内·DELETE（第 1 行）", NEW, None, None, "MediumButton",
    select_row_pos(0, 522.0), "D2/UI/Menu/btn_med_normal", "同 ENTER", "原版字号 18")
row("CharSelect", "角色行栈·最后一行（第 7 行）", NEW, None, None, "Select.RowSize",
    select_row_pos(6, 0.0), None,
    "行栈上下极端都登记（首行 = 容器顶边内侧；末行底边 = %0.1f）"
    % (select_row_y(6) - _SEL_ROW_H / 2.0))
row("CharSelect", "Exit 槽按钮（MAIN MENU）",
    "原版 `ClassSelectMenu.prefab` ExitButton（m_Text=EXIT）", "MediumButtonOrig", "(-300,-250)",
    "MediumButton", "ClassMenu.ExitPos", "D2/UI/Menu/btn_med_normal",
    "sprite=btn_med_normal；动作=ToMainMenuRequest（**本片修正**：修前这一槽放的是「新建」）")
row("CharSelect", "Ok 槽按钮（NEW HERO）",
    "原版 `ClassSelectMenu.prefab` OkButton（m_Text=OK）", "MediumButtonOrig", "(300,-250)",
    "MediumButton", "ClassMenu.OkPos", "D2/UI/Menu/btn_med_normal",
    "sprite=btn_med_normal；动作=Fsm.TriggerNeedCreate（**本片修正**）")

# ④ 创角屏 ───────────────────────────────────────────────────────────────────
row("CharCreate", "整屏贴图", "原版 `data/global/ui/MENU/class_select_screen`", "800,600", "(0,0)",
    "(OrigWidth*Scale,OrigHeight*Scale)", "(0,0)", "D2/UI/Menu/class_select_screen",
    "sprite=class_select_screen")
row("CharCreate", "标题（SELECT HERO CLASS）", "原版 `ClassSelectMenu.prefab` SelectHeroClass 文本框",
    "(493,30)", "(0,267)", "ClassMenu.TitleSize", "ClassMenu.TitlePos", None,
    "887.4×54 @ (0,480.6)", "font24 档")
row("CharCreate", "职业说明行", "原版 `ClassSelectMenu.prefab` ClassDescription 文本框",
    "(300,50)", "(1,155)", "ClassMenu.DescSize", "ClassMenu.DescPos", None,
    "540×90 @ (1.8,279)", "font16 档；文案 = 参考工程 ClassSelectInfo.cs 的原版英文")
row("CharCreate", "名字输入框", NEW, "(462,35)", "(-15,198)", "New.NameInputSize", "New.NameInputPos",
    None, "831.6×63 @ (-27,356.4)", "中文占位走字模；输入框本身是显示层")
row("CharCreate", "NAME: 标签", NEW, "(140,35)", "(-330,198)", "New.NameLabelSize", "New.NameLabelPos",
    None, "252×63 @ (-594,356.4)", "font16 档，MiddleRight")
for nm, slot, cls in (("Amazon", 0, "amazon"), ("Barbarian", 2, "barbarian")):
    for st, lbl, code in ((1, "默认(背面待机)", "nu1"), (2, "悬停", "nu2"), (3, "选中(转正面)", "nu3")):
        row("CharCreate", "%s 半身像·%s" % (nm, lbl),
            "原版 `data/global/ui/FrontEnd/{0}/{1}NU{2}.DC6` 帧 0（热点 slot={3}）".format(cls, code.upper(), st, slot),
            "ClassMenu.Spot.%sNu%d.OrigSize" % (nm, st),
            "ClassMenu.Spot.%sNu%d.OrigPos" % (nm, st),
            "ClassMenu.Spot.%sNu%d.Size" % (nm, st),
            "ClassMenu.Spot.%sNu%d.Pos" % (nm, st),
            "D2/UI/FrontEnd/%s/%s_0" % (cls, code), "sprite=%s_0（原版素材 1:1 ×1.8）" % code)
HOT = (("Amazon", 0, "(-299,-16)", "(90,200)", "ClassMenu.AmazonPos", "ClassMenu.SpotSize"),
       ("Necromancer", 1, "(-99,-11)", "(90,200)", "ClassMenu.NecromancerPos", "ClassMenu.SpotSize"),
       ("Barbarian", 2, "(1,0)", "(90,200)", "ClassMenu.BarbarianPos", "ClassMenu.SpotSize"),
       ("Paladin", 3, "(116.5,-8)", "(110,200)", "ClassMenu.PaladinPos", "ClassMenu.SpotSizeWide"),
       ("Sorceress", 4, "(221.75,-22)", "(85,200)", "ClassMenu.SorceressPos", "ClassMenu.SpotSizeNarrow"))
for nm, slot, opos, osz, ppos, psz in HOT:
    note = ("**停用槽**：用户 2026-09-19 决策只做 Amazon + Barbarian ⇒ 素材已删、槽位保留原版几何（不重排）"
            if slot in (1, 3, 4) else "透明点击区（原版热点矩形本身）")
    row("CharCreate", "职业热点·" + nm,
        "原版 `ClassSelectMenu.prefab` 热点（pivot 非中心，已折成矩形中心）", osz, opos, psz, ppos, None, note)
row("CharCreate", "Exit 槽按钮（BACK）", "原版 `ClassSelectMenu.prefab` ExitButton", "MediumButtonOrig",
    "(-300,-250)", "MediumButton", "ClassMenu.ExitPos", "D2/UI/Menu/btn_med_normal",
    "sprite=btn_med_normal；动作=CharSelectRequest")
row("CharCreate", "Ok 槽按钮（OK）", "原版 `ClassSelectMenu.prefab` OkButton", "MediumButtonOrig",
    "(300,-250)", "MediumButton", "ClassMenu.OkPos", "D2/UI/Menu/btn_med_normal",
    "sprite=btn_med_normal；未选职业时置灰（原版 okButton.Disabled）")

# ⑤ 读条屏 ───────────────────────────────────────────────────────────────────
row("Loading", "整屏黑底", "原版 `LoadingScreen.cs:41-49` Background = Color.black（铺满整屏）",
    None, None, None, None, None, "Color(0,0,0,1)")
row("Loading", "原版读条图（10 帧）",
    "原版 `data/global/ui/Loading/loadingscreen.dc6` 10 帧（`LoadingScreen.cs:51-52/66-68`）",
    "Loading.ArtOrigSize", "(0,0)", "Loading.ArtSize", "Loading.ArtPos",
    "D2/UI/Menu/loadingscreen_0", "sprite=loadingscreen_{i}，中心 = 画布正中；帧号 = 真实里程碑档位")

# ⑥ 选项屏 ───────────────────────────────────────────────────────────────────
row("Settings", "整屏遮罩", NEW, None, None, None, None, None, "UiArt.Overlay 全屏（吃掉点击）")
row("Settings", "底板",
    "无（原版 options 屏 prefab 不在参考工程里；原版 `MENU/boxpieces.DC6` 九宫格框 22 帧未接 ⇒ 见回报）",
    "(420,335)", "(0,-42.5)", "Settings.BoxSize", "Settings.BoxPos", None,
    "756×603 @ (0,-76.5) 纯色底 UiArt.PanelBg", None,
    "选项屏底板目前是**纯色块**，而原版那套**九宫格石框素材就在磁盘上**"
    "（`D2/UI/Menu/boxpieces_0..21.png` = 原版 `data/global/ui/MENU/boxpieces.DC6` 22 帧 14×15）"
    "却**零引用**。⛔ 本片**不接**：`export_d2ui.py` 自己写着这批 tile 的「怎么摆缺权威依据」，"
    "参考工程也没有 options 屏 prefab ⇒ 接进来就是自创布局（§0.5）。"
    "按 §0.1② 的口径这**算一处待裁决的不一致**，缺的是主 agent 的拼装口径 ⇒ 见回报「需他片处理」")
row("Settings", "标题（OPTIONS）", "本项目新增（行节奏 45 原版px 推导）", "(300,30)", "(0,110)",
    "Settings.TitleSize", "Settings.TitlePos", None, "540×54 @ (0,198)", "font24 档")
for i, oy in (("1", "60"), ("2", "15")):
    row("Settings", "第 %s 行·标签" % i, "本项目新增（行节奏 45 推导）", "(160,35)", "(-140,%s)" % oy,
        "Settings.LabelSize", "(Settings.LabelPos.x,Settings.Row%sY)" % i, None, "288×63 → −252", "font16 档")
    row("Settings", "第 %s 行·−" % i, "本项目新增（边长 = 原版按钮行高 35×35）", "(35,35)", "(0,%s)" % oy,
        "Px((35,35))", "(Settings.MinusPos.x,Settings.Row%sY)" % i, None,
        "63×63；底图 = 原版中等按钮帧 btn_med_normal(128×35) + preserveAspect（不拉伸，按比例内缩）",
        None, "± 的矩形是 63×63 方框、底图是 128×35 的**横条** ⇒ 可见图形按比例内缩成 **63×17.2**"
              "（矩形与素材宽高比不同，靠 preserveAspect 保证**不拉伸**）。"
              "原版**没有 options 屏**可对照（参考工程无该 prefab）⇒ 尺寸口径只能登记；"
              "观感（横条被缩成矮条是否可接受）属**表现类**，待实机确认")
    row("Settings", "第 %s 行·数值" % i, "本项目新增（行内位置 45 推导）", "(50,35)", "(45,%s)" % oy,
        "Settings.ValueSize", "(Settings.ValuePos.x,Settings.Row%sY)" % i, None, "90×63 → 81", "font16 档")
    row("Settings", "第 %s 行·+" % i, "本项目新增（边长 = 原版按钮行高 35×35）", "(35,35)", "(90,%s)" % oy,
        "Px((35,35))", "(Settings.PlusPos.x,Settings.Row%sY)" % i, None,
        "63×63；底图 = 原版中等按钮帧 btn_med_normal(128×35) + preserveAspect（不拉伸，按比例内缩）",
        None, "同「−」：矩形 63×63 vs 素材 128×35 ⇒ 可见图形按比例内缩成 63×17.2；"
              "原版无 options 屏可对照 ⇒ 登记，观感待实机确认")
    row("Settings", "第 %s 行·音量条" % i, "本项目新增（原版 240×6 口径）", "(240,6)", "(-40,%s-24)" % oy,
        "Settings.BarSize", "(Settings.BarPos.x,Settings.Row%sY+Settings.BarPos.y)" % i, None,
        "432×10.8（锚点宽度填充，不换行不数字）")
for i, oy in (("3", "-30"), ("4", "-75")):
    row("Settings", "第 %s 行·标签" % i, "本项目新增（行节奏 45 推导）", "(160,35)", "(-140,%s)" % oy,
        "Settings.LabelSize", "(Settings.LabelPos.x,Settings.Row%sY)" % i, None, "288×63 → −252", "font16 档")
    row("Settings", "第 %s 行·开关" % i, "本项目新增（底图 = 原版中等按钮 128×35）", "MediumButtonOrig",
        "(60,%s)" % oy, "MediumButton", "(Settings.TogglePos.x,Settings.Row%sY)" % i,
        "D2/UI/Menu/btn_med_normal", "sprite=btn_med_normal；ON/OFF / LOW·MED·HIGH", "原版字号 18")
row("Settings", "关闭钮（CLOSE）", "本项目新增（底图 = 原版中等按钮 128×35）", "MediumButtonOrig",
    "(0,-170)", "MediumButton", "Settings.ClosePos", "D2/UI/Menu/btn_med_normal", "sprite=btn_med_normal")
row("Settings", "脚注", NEW, "(400,20)", "(0,-200)", "Settings.FootSize", "Settings.FootPos", None,
    "720×36 @ (0,-360)", "font16 档")

# ⑦ 暂停菜单 ─────────────────────────────────────────────────────────────────
row("Pause", "整屏遮罩", NEW, None, None, None, None, None, "UiArt.Overlay 全屏（吃掉点击）")
for text, posc, oy in (("CONTINUE", "Pause.ResumePos", "-17.5"), ("OPTIONS", "Pause.OptionsPos", "-62.5"),
                       ("SAVE & EXIT", "Pause.SaveExitPos", "-107.5"), ("MAIN MENU", "Pause.ToMainPos", "-152.5")):
    row("Pause", "按钮 " + text, "原版 ESC 菜单无独立 prefab ⇒ 复用 `MainMenu.prefab` 该槽 272×35",
        "WideButtonOrig", "(0,%s)" % oy, "WideButton", posc, "D2/UI/Menu/btn_wide_normal",
        "sprite=btn_wide_normal", "原版字号 18")
row("Pause", "底部提示行（PRESS ESC TO CONTINUE）", NEW, "(400,20)", "(0,-197.5)", None, "Pause.HintPos",
    None, "720×36 @ (0,-355.5)", "font16 档")

row("Death", "整屏遮罩", NEW, None, None, None, None, None, "UiArt.Overlay 全屏")
for i in range(4):
    w, h, _, _ = DEATH_TILES[i]
    psz = death_tile_size(i)
    ppos = death_tile_pos(i)
    row("Death", "底图第 %d 块" % (i + 1),
        "原版 `data/global/ui/MENU/EndGame.dc6` 页 0 第 %d 块（tile 打包，8 帧 = 2 页 × 4 块）" % (i + 1),
        "%d,%d" % (w, h), None, "%r,%r" % psz, "(%r,%r)" % ppos, "D2/UI/Menu/endgame_%d" % i,
        "sprite=endgame_%d；几何 = UiLayoutGame.DeathTilePos/Size(i)" % i)
row("Death", "标题条", "原版 `data/LOCAL/UI/chi/youdiedsoftcore.dc6` 帧 0", "256,54",
    None, "%r,%r" % (256 * SCALE, 54 * SCALE), "(0,%r)" % death_row_center_y(0),
    "D2/UI/Banner/youdiedsoftcore_0", "sprite=youdiedsoftcore_0（preserveAspect）")
row("Death", "提示行（本项目新增）", NEW, None, None, "%r,25" % DEATH_HINT_W,
    "(0,%r)" % death_row_center_y(1), None, "单行不换行（B35：换行会压字压按钮）")
row("Death", "Continue 按钮",
    "原版 `data/global/ui/MENU/endgameok.dc6`（96×32 ×2 帧：常态/按下）", "96,32",
    None, "%r,%r" % (96 * SCALE, 32 * SCALE), "(0,%r)" % death_row_center_y(2),
    "D2/UI/Menu/endgameok_0", "sprite=endgameok_0；按下=endgameok_1；文字=原版串 id 3403「繼續」")


# ─────────────────────────────────────────────────────────────────────────────
# 4. 判定
# ─────────────────────────────────────────────────────────────────────────────
_PAIR_RX = re.compile(r"^\(\s*(-?[\d.]+)\s*,\s*(-?[\d.]+)\s*\)$")
_TWO_RX = re.compile(r"^(-?[\d.]+)\s*,\s*(-?[\d.]+)$")


def as_size(expr):
    if expr is None:
        return None
    s = expr.strip()
    m = _PAIR_RX.match(s) or _TWO_RX.match(s)
    if m:
        return (float(m.group(1)), float(m.group(2)))
    v = F(s)
    return (v.x, v.y) if isinstance(v, V) else (v, v)


def as_pos(expr):
    if expr is None:
        return None
    s = expr.strip()
    m = _PAIR_RX.match(s)
    if m:
        return (float(m.group(1)), float(m.group(2)))
    v = F(s)
    return (v.x, v.y) if isinstance(v, V) else (v, v)


def fmt(v):
    if v is None:
        return "—"
    if isinstance(v, tuple):
        return "(%g,%g)" % v
    return "%g" % v


out_lines = []
inconsistent = []
geom = {}
registered = []          # 已在表里写明理由的**登记差异**（不判不一致；stdout 另列一节）


def add(screen, ctrl, origin, proj, diff, verdict):
    out_lines.append([screen, ctrl, origin, proj, diff, verdict])
    if verdict != "一致":
        inconsistent.append((screen, ctrl, diff))


for r in ROWS:
    screen, ctrl = r["screen"], r["ctrl"]
    o_sz = as_size(r["osize"]) if r["osize"] else None
    o_pos = as_pos(r["opos"]) if r["opos"] else None
    p_sz = as_size(r["psize"]) if r["psize"] else None
    p_pos = as_pos(r["ppos"]) if r["ppos"] else None
    ihdr = png_size(r["asset"]) if r["asset"] else None

    diffs = []
    if o_sz and p_sz:
        exp = (o_sz[0] * SCALE, o_sz[1] * SCALE)
        if abs(p_sz[0] - exp[0]) > TOL or abs(p_sz[1] - exp[1]) > TOL:
            diffs.append("尺寸 工程 %s ≠ 原版 %s ×1.8 = %s" % (fmt(p_sz), fmt(o_sz), fmt(exp)))
    if o_pos and p_pos:
        exp = (o_pos[0] * SCALE, o_pos[1] * SCALE)
        if abs(p_pos[0] - exp[0]) > TOL or abs(p_pos[1] - exp[1]) > TOL:
            diffs.append("位置 工程 %s ≠ 原版 %s ×1.8 = %s" % (fmt(p_pos), fmt(o_pos), fmt(exp)))
    if o_sz and ihdr and (abs(ihdr[0] - o_sz[0]) > 0.5 or abs(ihdr[1] - o_sz[1]) > 0.5):
        diffs.append("素材 IHDR %d×%d ≠ 声明原版尺寸 %s" % (ihdr[0], ihdr[1], fmt(o_sz)))
    if r["asset"] and ihdr is None:
        diffs.append("素材文件不存在：%s" % r["asset"])
    if p_sz and p_sz[1] > 0 and ihdr:
        a1, a2 = p_sz[0] / p_sz[1], ihdr[0] / float(ihdr[1])
        if abs(a1 - a2) > 0.01 * max(1.0, a2):
            diffs.append("矩形宽高比 %.4f ≠ 素材宽高比 %.4f（会被拉伸）" % (a1, a2))

    proj = []
    if p_sz:
        proj.append("size=%s" % fmt(p_sz))
    if p_pos:
        proj.append("pos=%s" % fmt(p_pos))
    if ihdr:
        proj.append("素材实测=%d×%d" % ihdr)
    if r["font"]:
        proj.append("字号=%s" % r["font"])
    if r["extra"]:
        proj.append(r["extra"])
    if not proj:
        proj.append(r["extra"] or "—")

    origin = r["origin"]
    if o_sz:
        origin += "；原版尺寸 %s" % fmt(o_sz)
    if o_pos:
        origin += "；原版位置 %s" % fmt(o_pos)
    if r["asset"]:
        origin += "；素材 %s.png" % r["asset"]

    if r["reg"]:
        registered.append((screen, ctrl, r["reg"]))

    if o_sz is None and o_pos is None and r["origin"] == NEW:
        diff = "—（无原版值 ⇒ 登记为本项目新增）"
        if r["reg"]:
            diff += "；**登记差异**：" + r["reg"]
    elif diffs:
        diff = "；".join(diffs)
    else:
        parts = []
        if o_sz and p_sz:
            parts.append("尺寸 %s = %s ×1.8（差 0）" % (fmt(p_sz), fmt(o_sz)))
        if o_pos and p_pos:
            parts.append("位置 %s = %s ×1.8（差 0）" % (fmt(p_pos), fmt(o_pos)))
        if o_sz and ihdr:
            parts.append("素材 IHDR 逐值相符")
        diff = "；".join(parts) if parts else "—（无原版值 ⇒ 登记）"

    verdict = "不一致" if diffs else "一致"
    add(screen, ctrl, origin, "；".join(proj), diff, verdict)

    if p_sz and p_pos:
        geom.setdefault(screen, []).append((ctrl, p_pos, p_sz))


# ─────────────────────────────────────────────────────────────────────────────
# 5. 逐屏几何复核（独立于 uicheck 的实现：输入是"面板实际构造的元件"）
# ─────────────────────────────────────────────────────────────────────────────
# ① **全部**元件都查"落在 1920×1080 画布内"；
# ② 只有**会同时出现**的元件才做两两重叠配对 —— 两类要豁免（豁免理由逐条，与 uicheck §⑬ 同口径）：
#    · **整屏底图 / 半身像三态**：整屏贴图（1440×1080）本来就覆盖全屏，是"底"不是"压在别人上"；
#      职业半身像的 NU1/NU2/NU3 是**互斥显示**（同一槽同一时刻只显示一态）；
#    · **透明热点 / 容器 / 底板 / 遮罩 / 列表**：点击区与容器（本来就把子元素包在里面）。
NO_PAIR_TOKENS = ("热点", "容器", "底板", "遮罩", "列表", "整屏", "贴图", "底色", "半身像", "底图")
CANVAS = (1920.0, 1080.0)
geo_report = []

for screen, items in sorted(geom.items()):
    all_boxes = [(c, (p[0] - s[0] / 2.0, p[0] + s[0] / 2.0, p[1] - s[1] / 2.0, p[1] + s[1] / 2.0))
                 for c, p, s in items]
    pair_boxes = [b for b in all_boxes if not any(t in b[0] for t in NO_PAIR_TOKENS)]
    outside = ["%s x[%.1f,%.1f] y[%.1f,%.1f]" % (c, b[0], b[1], b[2], b[3]) for c, b in all_boxes
               if b[0] < -CANVAS[0] / 2 or b[1] > CANVAS[0] / 2 or b[2] < -CANVAS[1] / 2 or b[3] > CANVAS[1] / 2]
    overlaps = []
    for i in range(len(pair_boxes)):
        for j in range(i + 1, len(pair_boxes)):
            a, b = pair_boxes[i][1], pair_boxes[j][1]
            if not (a[1] <= b[0] or b[1] <= a[0] or a[3] <= b[2] or b[3] <= a[2]):
                overlaps.append("%s × %s" % (pair_boxes[i][0], pair_boxes[j][0]))
    geo_report.append((screen, len(all_boxes), len(pair_boxes), outside, overlaps))
    for o in outside:
        add(screen, "(几何复核) 越界", "画布 1920×1080 内", o, "元件矩形跑出画布", "不一致")
    for o in overlaps:
        add(screen, "(几何复核) 重叠", "同屏元件两两不重叠", o, "两个元件矩形相交", "不一致")


# ─────────────────────────────────────────────────────────────────────────────
# 6. 落盘
# ─────────────────────────────────────────────────────────────────────────────
if not os.path.isdir(OUT_DIR):
    os.makedirs(OUT_DIR)

with open(OUT_TSV, "w", encoding="utf-8", newline="\n") as f:
    f.write("屏\t控件\t原版来源(素材文件/尺寸/offset)\t工程值(size/pos/sprite/字号)\t差异\t一致|不一致\n")
    for cells in out_lines:
        f.write("\t".join(cells) + "\n")

print("=== w3 流程屏 UI 表现审计 ===")
print("常量来源：%s（脚本从源码解析，不在脚本里重抄数值）" % os.path.relpath(FLOW_SRC, REPO))
print("换算口径：Scale = %g（按高度等比 + 水平居中）" % SCALE)
print("审计行数：%d" % len(out_lines))
for screen in sorted(set(x["screen"] for x in ROWS)):
    tot = sum(1 for c in out_lines if c[0] == screen)
    bad = sum(1 for c in out_lines if c[0] == screen and c[5] != "一致")
    print("  %-11s 行数=%-3d 不一致=%d" % (screen, tot, bad))
print("逐屏几何复核（全屏底图/互斥三态/热点/容器/底板/遮罩只查越界，不参与重叠配对）：")
for screen, n, npair, outside, overlaps in geo_report:
    print("  %-11s 元件=%-3d（配对 %-3d） 越界=%d 重叠=%d"
          % (screen, n, npair, len(outside), len(overlaps)))
if registered:
    print("登记差异（**不判不一致**，理由逐条；判据同上）：")
    for screen, ctrl, reg in registered:
        print("  * %s / %s：%s" % (screen, ctrl, reg))
print("产物：%s" % os.path.relpath(OUT_TSV, REPO))

if inconsistent:
    print("\n=== 不一致清单（%d 条）===" % len(inconsistent))
    for screen, ctrl, diff in inconsistent:
        print("  - %s / %s：%s" % (screen, ctrl, diff))
    sys.exit(1)

print("\n=== 全部一致（0 条不一致）===")
sys.exit(0)
