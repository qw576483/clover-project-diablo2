# -*- coding: utf-8 -*-
"""s1_fill_matrix.py -- 判据资产：把 S1 数值维「官方 txt ↔ 运行时表」的逐行判决写回 策划/状态矩阵.tsv。

本片 = s1-fill-official-verdicts（2026-09-22）。被阻塞两轮的闸门项 = `coverage-filled`
（`FAIL=1` 的唯一原因：`821/4965 matrix row(s) incomplete ; … L4102[S1:empty-measured]`）。
官方 1.10f 数据表已落地 `原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/`（载体出处
见 `原版资源/清单.md`），比对器跑完 821 行 = 一致 821 / 不一致 0。

── 输入（本片冻结的两份机器可读产物，同一次 `--gate-artifacts` 运行写出，两处副本字节相同）──
  * `.ai-tmp/{test/s1,screenshots}/s1_value_diff.rows.tsv`   逐矩阵行 1 行（821 行 = 行级屏）
  * `.ai-tmp/{test/s1,screenshots}/s1_value_diff.tsv`        逐 (矩阵行 × 字段) 1 行（18482 行 = 字段级）
  ⚠️ 为什么字段级也要读：`rows.tsv` 的行级「官方值 / 我们的值」两列对 `一致` 行**按设计为空** ——
  行级屏取的是「最差字段」，而 `一致` 行没有任何「最差」字段，该行退化成
  `*（全部比得了的列）` / 空 / 空 / `0`。逐字段的官方值与工程值只存在于字段级产物里，
  故本脚本两份都读，并在写表前机械断言两者互相印证
  （行级 `差值 0` ⇔ 字段级该行所有「比得了」的字段 `差值 0` 且 官方值 == 我们的值）。
  ⛔ 数值一个都不许改：本脚本只做「抄 + 拼字符串」，不重算任何值。

── 写表口径（与 t0b_fill.py / t0_d4d5_fill.py 同协议）──
  * 只改**那 821 行**的第 6/7/8 列（实测 / 结论 / 证据）；⛔ 不动前 5 列、不动其他行、不增删行。
  * 目标行 = `rows.tsv` 的 `行号` 列（逐行核对 `实体id` == 矩阵该行 `实体 + "|" + 状态`，不一致 ⇒ 停手报错）。
  * 锁：`.ai-tmp/test/matrix.lock`（`os.open(..., O_CREAT|O_EXCL)` == .NET `FileMode.CreateNew`
    的等价语义）；拿不到 `time.sleep(2)` 重试 ≤60 次；拿到后**重读整表**、写完删锁。
  * 幂等：目标行由 `rows.tsv` 的 `行号` 决定（**不看「实测是否为空」**）⇒ 填充后重跑，
    同样的输入 ⇒ 同样的文本 ⇒ 表逐字节不变。

── 判据出处怎么落到「官方 txt 的行号」──
  * 每行先按主键在官方 txt 里定位**物理行号**（0 命中 / 多命中 ⇒ 停手报错，⛔ 不许猜）：
      class       charstats.txt      列 class          ← 本表 code
      experience  experience.txt     列 Level          ← 本表 level
      level       Levels.txt         列 Name           ← 本表 code
      monster     MonStats.txt       列 Id             ← 本表 code
      skill       skills.txt         列 skill          ← 本表 code
      item        Weapons|Armor|Misc 列 code           ← 本表 code（文件由本表 source 决定）
      affix       MagicPrefix/MagicSuffix 列 Name      ← 见下（按 convert.py 的选行顺序）
      monumod     MonUMod.txt        列 uniquemod      ← 本表 code
      treasureclass TreasureClassEx.txt 列 "Treasure Class" ← 本表 name
      missile     Missiles.txt       列 Missile        ← 本表 code
  * `affix` 没有唯一键（同名词缀按物品类型重复多行）⇒ 按 `convert.py:839-844` 的**同一选行口径**
    复现序号：`MagicPrefix.txt` 然后 `MagicSuffix.txt`，各取 `spawnable==1 且 level<=AFFIX_MAX_LEVEL(=12)`
    的行，按下标累加 nid ⇒ nid 就是本项目 affix 的 `id`。
  * **逐行核对（强断言）**：把字段级产物里每个字段的 `官方来源文件:列` 标签归一成
    `<官方文件>:<列>`（`…:` 前缀按上表展开为实际文件；`Weapons|Armor|Misc.txt:` / `MagicPrefix|MagicSuffix.txt:`
    同理），凡 `<列>` 正好是该官方文件表头之一 ⇒ 断言**该文件那一物理行的这一列**的值
    （原样扫出的字符串）== 字段级产物的 `官方值`。这正是不重算任何值、只做「行号对得上」的机械证明。
  * `缺官方值` 的字段：断言其集合 == `s1_common.EXPECTED_NO_CARRIER[logical]`（本项目编号 / 中文显示名 /
    convert.py 明示的登记占位），并把「为何落缺官方值」写进该行文字（引 `s1_common.py` 的集合定义；
    monumod 的 `ac_mul` / `res_*` 7 列另引 `策划/差异登记.tsv` E44）。

用法：
  python tools/probes/measure/s1_fill_matrix.py            # DRY（默认）：只打印，不写表
  python tools/probes/measure/s1_fill_matrix.py --apply    # 取锁 + 只改那 821 行 + 删锁
"""
import argparse
import collections
import hashlib
import io
import os
import re
import sys
import time

FILE_TOKEN = re.compile(r"^[A-Za-z0-9_.\-]+\.txt$")

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.abspath(__file__)
MEASURE_DIR = os.path.dirname(HERE)
if MEASURE_DIR not in sys.path:
    sys.path.insert(0, MEASURE_DIR)
import s1_common as C   # noqa: E402  （EXPECTED_NO_CARRIER / FIELD_MAP 的唯一定义处）

ROOT = C.find_repo_root(MEASURE_DIR)
MATRIX = os.path.join(ROOT, "策划", "状态矩阵.tsv")
LOCK = os.path.join(ROOT, ".ai-tmp", "test", "matrix.lock")
ART_TEST = os.path.join(ROOT, ".ai-tmp", "test", "s1")
ART_SHOT = os.path.join(ROOT, ".ai-tmp", "screenshots")
ROWS_TSV = "s1_value_diff.rows.tsv"
FIELDS_TSV = "s1_value_diff.tsv"
EXCEL_REL = os.path.join("原版资源", "参考工程_Diablerie", "d2lod1.10txt",
                         "data", "global", "excel")

DIM_ORDER = ["D1资源", "D2几何", "D3材质", "D4UI", "D5动画", "D6特效", "D7音乐",
             "D8音效", "D9碰撞", "D10逻辑", "D11输入", "D12流程", "S1数值", "S2性能", "S3设置"]

# convert.py:801
AFFIX_MAX_LEVEL = 12

VERDICT_OK = "一致"
NO_CARRIER_MARK = "(官方 txt 无此列)"

# 逻辑名 → 官方主文件 / 该文件里的主键列 / 本项目运行时表里提供主键的列
PRIMARY = {
    "class": ("charstats.txt", "class", "code"),
    "experience": ("experience.txt", "Level", "level"),
    "level": ("Levels.txt", "Name", "code"),
    "monster": ("MonStats.txt", "Id", "code"),
    "skill": ("skills.txt", "skill", "code"),
    "monumod": ("MonUMod.txt", "uniquemod", "code"),
    "treasureclass": ("TreasureClassEx.txt", "Treasure Class", "name"),
    "missile": ("Missiles.txt", "Missile", "code"),
}
ITEM_FILE = {"weap": "Weapons.txt", "armo": "Armor.txt", "misc": "Misc.txt"}
AFFIX_FILE = {"pre": "MagicPrefix.txt", "suf": "MagicSuffix.txt"}
RT_NAME = {"class": "Class", "experience": "Experience", "level": "Level",
           "monster": "Monster", "skill": "Skill", "item": "Item", "affix": "Affix",
           "monumod": "Monumod", "treasureclass": "Treasureclass", "missile": "Missile"}
# 「…:」前缀 = 「就是本行实际用的那张官方表」
ELLIPSIS_PREFIXES = {"item": "Weapons|Armor|Misc.txt:", "affix": "MagicPrefix|MagicSuffix.txt:"}
E44_COLS = {"ac_mul", "res_phys", "res_magic", "res_fire", "res_light", "res_cold", "res_poison"}


class FillError(RuntimeError):
    pass


def die(msg):
    raise FillError(msg)


# ── 通用读取 ───────────────────────────────────────────────────────────────────
def read_matrix_raw():
    with open(MATRIX, "rb") as fh:
        raw = fh.read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    text = raw.decode("utf-8-sig")
    nl = "\r\n" if "\r\n" in text else "\n"
    return bom, nl, text.split(nl)


def read_tsv_physical(path):
    """读一份 TSV，返回 [(物理行号, cells)]（空行计入行号但结果里略去）。"""
    with io.open(path, "r", encoding="utf-8-sig", errors="replace") as f:
        t = f.read().replace("\r\n", "\n").replace("\r", "\n")
    out = []
    for i, l in enumerate(t.split("\n"), 1):
        if l.strip() == "":
            continue
        out.append((i, l.split("\t")))
    return out


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ── 官方 txt（读取口径与 convert.py:table() 逐字一致，只多记物理行号）────────────
_OFF = {}


def official(name):
    """→ dict(hdr, rows=[(物理行号, cells)], blank_lines=内层空行物理行号)"""
    if name in _OFF:
        return _OFF[name]
    path = os.path.join(ROOT, EXCEL_REL, name)
    if not os.path.isfile(path):
        die("官方 txt 不在位：%s（载体出处见 原版资源/清单.md）" % path)
    raw = open(path, "rb").read()
    text = None
    for enc in ("cp1252", "utf-8"):
        try:
            text = raw.decode(enc)
            break
        except UnicodeDecodeError:
            continue
    if text is None:
        text = raw.decode("latin-1")
    lines = text.replace("\r\n", "\n").split("\n")
    rows, blanks = [], []
    for i, l in enumerate(lines, 1):
        if l.strip() == "":
            if i != len(lines) or rows:            # 只有「数据行之间的空行」才算内层空行
                blanks.append(i)
            continue
        rows.append((i, l.split("\t")))
    hdr = rows[0][1]
    _OFF[name] = {"hdr": hdr, "rows": rows[1:], "blank_lines": blanks}
    return _OFF[name]


def hdr_index(hdr, colname):
    """大小写不敏感的表头查列（返回下标或 -1）。"""
    low = [h.strip().lower() for h in hdr]
    c = colname.strip().lower()
    return low.index(c) if c in low else -1


def cell(cells, i):
    return cells[i] if 0 <= i < len(cells) else ""


def as_int(v):
    """convert.py:as_int 等价（int(float(v))，非法/空 ⇒ 0）。"""
    v = (v or "").strip()
    if v == "":
        return 0
    try:
        return int(float(v))
    except ValueError:
        return 0


def find_unique(fname, colname, val, what):
    """在官方 txt 里按列值定位唯一物理行；0 或多命中 ⇒ 停手。"""
    o = official(fname)
    ci = hdr_index(o["hdr"], colname)
    if ci < 0:
        die("%s：官方 %s 没有列 %r（表头 %s）" % (what, fname, colname, o["hdr"][:8]))
    hits = [(ln, cells) for ln, cells in o["rows"] if cell(cells, ci).strip() == val]
    if len(hits) != 1:
        die("%s：在 %s 的列 %r 上按值 %r 命中 %d 行（必须恰好 1）"
            % (what, fname, colname, val, len(hits)))
    return hits[0]


def build_affix_lines():
    """复现 convert.py:838-859 的选行顺序 ⇒ affix id → (文件, 物理行号, cells)。"""
    out = {}
    nid = 0
    for fname, _kind in (("MagicPrefix.txt", "pre"), ("MagicSuffix.txt", "suf")):
        o = official(fname)
        i_sp = hdr_index(o["hdr"], "spawnable")
        i_lv = hdr_index(o["hdr"], "level")
        if i_sp < 0 or i_lv < 0:
            die("%s 没有 spawnable / level 列" % fname)
        for ln, cells in o["rows"]:
            if as_int(cell(cells, i_sp)) == 1 and as_int(cell(cells, i_lv)) <= AFFIX_MAX_LEVEL:
                nid += 1
                out[nid] = (fname, ln, cells)
    return out


# ── 值比较（只做等价判断，不重算）──────────────────────────────────────────────
def resolve_label(logical, label, fname):
    """字段级产物的「官方来源文件:列」标签 → (官方文件, 列名)；无法唯一归一 ⇒ (None, None)。

    归一规则（都只把「泛指」换成「本行实际用的那张表」，⛔ 不猜列）：
      `…:`                         → 本行实际官方表（FIELD_MAP 里 item/affix 用这个写法）
      `Weapons|Armor|Misc.txt:`    → 同上（已按本表 source 定表）
      `MagicPrefix|MagicSuffix.txt:` → 同上
      `Weapons.txt:` / `Armor.txt:` / `Misc.txt:` / `MagicPrefix.txt:` / `MagicSuffix.txt:`
                                   → 就是写死的那个文件
      `experience.txt:Amazon（…）`  → 官方 experience.txt 的 `Amazon` 列
      其余 `<xxx>.txt:<列>`         → 原样（`<xxx>.txt` 必须是纯文件名 token，像 `max(Levels.txt` 这种
                                    被公式包住的**不算** ⇒ 跳过，⛔ 不猜）
    带括号说明的列名（如 `…:mindam（两侧皆 0 ⇒ 回退 2handmindam）`）**不**去括号 ⇒ 不算表头 ⇒ 跳过核对。
    """
    if label.startswith("…:"):
        return fname, label[2:]
    pre = ELLIPSIS_PREFIXES.get(logical)
    if pre and label.startswith(pre):
        return fname, label[len(pre):]
    if label.startswith(("Weapons.txt:", "Armor.txt:", "Misc.txt:",
                         "MagicPrefix.txt:", "MagicSuffix.txt:")):
        f, cp = label.split(":", 1)
        return f, cp
    if logical == "experience" and label.startswith("experience.txt:Amazon"):
        return "experience.txt", "Amazon"
    if ":" in label:
        f, cp = label.split(":", 1)
        if FILE_TOKEN.match(f):
            return f, cp
    return None, None


def same_value(official_raw, artifact_val):
    a = (official_raw or "").strip()
    b = (artifact_val or "").strip()
    if a == b:
        return True
    # 官方该格为空 ⇒ convert.py 的 as_int/as_float(default=0) 取 0（这是 convert.py 的既定口径，
    # 不是我这里改数）。仅此一种放宽；其余一律要求逐字符或数值相等。
    if a == "" and b in ("", "0", "0.0"):
        return True
    if b == "" and a in ("", "0", "0.0"):
        return True
    try:
        return float(a) == float(b)
    except (TypeError, ValueError):
        return False


class Stats(object):
    def __init__(self):
        self.checked = collections.Counter()      # logical -> 断言过的字段数
        self.rows = collections.Counter()         # logical -> 目标行数
        self.nocarrier = collections.Counter()    # logical -> 缺官方值字段数
        self.e44_rows = 0


# ── 逐行构造 6/7/8 列 ─────────────────────────────────────────────────────────
def build_row_text(ln, entity, state, rowrec, fieldrecs, ours, affix_lines, st):
    logical = C.logical_of_matrix_entity(entity)
    if logical is None:
        die("L%d：比对器不认识的 S1 实体 %r" % (ln, entity))
    eid_expect = "%s|%s" % (entity, state)
    if rowrec[1] != eid_expect:
        die("L%d：实体 id 对不上 —— 矩阵 %r / 行级产物 %r（⛔ 不许猜测性写入）"
            % (ln, eid_expect, rowrec[1]))

    # ① 行级断言：结论必须就是 `一致`，且差值 0
    if rowrec[6] != VERDICT_OK:
        die("L%d：行级产物结论 = %r（本脚本只允许把 `一致` 写成 `一致`）" % (ln, rowrec[6]))
    if rowrec[5] != "0":
        die("L%d：行级产物差值 = %r（必须 0）" % (ln, rowrec[5]))

    # ② 字段级断言：字段级产物逐 (矩阵行 × 字段) 与 FIELD_MAP **同序等长**（⛔ 不靠标签反查 ——
    #    monumod 的 6 条 res_* 标签完全相同，按标签反查必然歧义）；0 个 `不一致`；
    #    每个 `一致` 字段两侧同值；缺官方值字段集合 == EXPECTED_NO_CARRIER
    fm = C.FIELD_MAP[logical]
    if len(fieldrecs) != len(fm):
        die("L%d：字段级产物 %d 行 != FIELD_MAP[%r] 的 %d 列（同序等长前提被破坏）"
            % (ln, len(fieldrecs), logical, len(fm)))
    ok_fields, nocarrier_cols = [], []
    got_nc = set()
    for i, (colname, kind, label, _crit) in enumerate(fm):
        rec = fieldrecs[i]
        if rec[2] != label:
            die("L%d：字段级产物第 %d 行标签 %r != FIELD_MAP 的 %r（同序前提被破坏）"
                % (ln, i + 1, rec[2], label))
        v = rec[6]
        if v == "不一致" or v.startswith("不一致"):
            die("L%d：字段 %r 结论 = %r（本片不允许任何 `不一致`）" % (ln, colname, v))
        if v == VERDICT_OK:
            if kind == "none":
                die("L%d：列 %r 是 kind=none（官方无此列）却被判 `一致`" % (ln, colname))
            if rec[3] != rec[4]:
                die("L%d：字段 %r 差值 0 但两侧值不等（官方 %r / 工程 %r）"
                    % (ln, colname, rec[3], rec[4]))
            ok_fields.append(rec)
        elif v == "缺官方值":
            if kind != "none":
                die("L%d：列 %r 不是 kind=none 却落 `缺官方值`" % (ln, colname))
            if rec[3] != NO_CARRIER_MARK:
                die("L%d：字段 %r 落 `缺官方值` 但官方值 = %r（不是 `%s`）⇒ 不是「官方无此列」，"
                    "本脚本不许把别的缺失原因写成缺官方值" % (ln, colname, rec[3], NO_CARRIER_MARK))
            nocarrier_cols.append((colname, label, rec))
            got_nc.add(colname)
        else:
            die("L%d：字段 %r 结论 = %r（既非 `一致` 也非 `缺官方值`）" % (ln, colname, v))
    expect_nc = set(C.EXPECTED_NO_CARRIER.get(logical, set()))
    if got_nc != expect_nc:
        die("L%d：缺官方值列集合 %s != s1_common.EXPECTED_NO_CARRIER[%r] = %s"
            % (ln, sorted(got_nc), logical, sorted(expect_nc)))
    st.nocarrier[logical] += len(got_nc)
    st.rows[logical] += 1
    if got_nc & E44_COLS:
        st.e44_rows += 1

    # ③ 官方 txt 物理行（主键定位；affix 走选行序号）
    if logical == "affix":
        nid = int(state.split("=", 1)[1])
        if nid not in affix_lines:
            die("L%d：affix id=%s 不在 convert.py 选行口径的序号里（共 %d 个 nid）"
                % (ln, nid, len(affix_lines)))
        fname, oline, ocells = affix_lines[nid]
        keycol, keyval = "Name", ours["name"]
        if cell(ocells, hdr_index(official(fname)["hdr"], "Name")).strip() != keyval.strip():
            die("L%d：affix id=%s 定位到 %s:%d 的 Name=%r != 本表 name=%r（选行口径复现失败）"
                % (ln, nid, fname, oline,
                   cell(ocells, hdr_index(official(fname)["hdr"], "Name")), keyval))
        cite_note = "%s:%d（affix id=%d = 该文件第 %d 个 `spawnable=1 且 level<=%d` 的行，`Name=%s`）" % (
            fname, oline, nid, nid, AFFIX_MAX_LEVEL, keyval)
    elif logical == "item":
        src = ours.get("source", "")
        fname = ITEM_FILE.get(src)
        if not fname:
            die("L%d：item source=%r 不在 weap/armo/misc 里" % (ln, src))
        oline, ocells = find_unique(fname, "code", ours["code"], "L%d item" % ln)
        keycol, keyval = "code", ours["code"]
        cite_note = "%s:%d（code=%s）" % (fname, oline, keyval)
    else:
        fname, keycol, ourcol = PRIMARY[logical]
        kv = ours.get(ourcol, "")
        if kv.strip() == "":
            die("L%d：%s 的主键列 %s 在本表里为空（无法定位官方行）" % (ln, logical, ourcol))
        oline, ocells = find_unique(fname, keycol, kv.strip(), "L%d %s" % (ln, logical))
        keyval = kv.strip()
        cite_note = "%s:%d（%s=%s）" % (fname, oline, keycol, keyval)

    ohdr = official(fname)["hdr"]

    # ④ 逐字段核对：字段级产物的标签 → 该官方文件该物理行的这一列
    primary_cols, other_files, empty_cols = [], set(), []
    for rec in ok_fields:
        f, colpart = resolve_label(logical, rec[2], fname)
        if f is None:
            continue
        if f != fname:
            other_files.add(f)
            continue
        ci = hdr_index(ohdr, colpart)
        if ci >= 0:
            if not same_value(cell(ocells, ci), rec[3]):
                die("L%d：%s:%d 的列 %r = %r != 字段级产物官方值 %r（行号对不上）"
                    % (ln, fname, oline, colpart, cell(ocells, ci), rec[3]))
            if cell(ocells, ci).strip() == "" and rec[3].strip() in ("0", "0.0"):
                empty_cols.append(ohdr[ci])     # 官方该格为空 ⇒ convert.py 的 as_int/as_float 默认 0
            primary_cols.append(ohdr[ci])
            st.checked[logical] += 1
    other_files.discard(fname)
    if not primary_cols:
        die("L%d：一行都没能落到 %s:%d 的列上核对（判据出处不可机械验证）" % (ln, fname, oline))

    # ⑤ 拼文本
    vals = "；".join("%s=%s" % (rec[2], rec[3]) for rec in ok_fields)
    meas = ("工程值（client/Assets/StreamingAssets/Table/%s.tsv:%d）↔ 原版值（官方 txt）逐字段"
            "（差值全 0 ⇒ 逐字段两侧同值；值照 .ai-tmp/screenshots/%s 抄，⛔ 未改字/未四舍五入）：%s"
            % (RT_NAME[logical], ours["_rtline"], FIELDS_TSV, vals))
    if nocarrier_cols:
        setlit = "{%s}" % ", ".join("'%s'" % c for c in sorted(expect_nc))
        items = ["%s（%s）" % (cn, lb) for cn, lb, _rec in nocarrier_cols]
        meas += ("；官方无此列（缺官方值，⛔ 非「放过」：官方 txt 确无对应对手 ⇒ 天然比不了；"
                 "集合见 tools/probes/measure/s1_common.py 的 EXPECTED_NO_CARRIER[%r] = %s）：%s"
                 % (logical, setlit, "、".join(items)))
        if got_nc & E44_COLS:
            meas += ("；其中 %s 7 列 = 本项目登记占位（官方 MonUMod.txt 只给 constants + *constant desc，"
                     "护甲倍率 / 抗性加成在原版引擎里硬编码 ⇒ 官方载体抽不到）⇒ 逐条登记见 策划/差异登记.tsv E44"
                     % "/".join(sorted(E44_COLS)))
    ev = ("判据出处：原版资源/参考工程_Diablerie/d2lod1.10txt/data/global/excel/%s；"
          "本行列级核对列（该官方行的这些列逐格 == 字段级产物官方值）：%s"
          % (cite_note, "/".join(primary_cols)))
    if other_files:
        ev += "；另有官方来源 %s（公式列，逐字段出处见字段级产物）" % " / ".join(sorted(other_files))
    if empty_cols:
        ev += ("；披露：%s 这几列官方该格为空 ⇒ convert.py 的 as_int/as_float 默认取 0"
               "（本项目既定口径，非本脚本改数），本行按「空 ⇔ 0」核过"
               % "/".join(empty_cols))
    ev += ("；产物：.ai-tmp/screenshots/%s:%d-%d（%d 字段：一致 %d / 缺官方值 %d）"
           " + .ai-tmp/screenshots/%s:%d（行结论=%s 差值=%s）"
           % (FIELDS_TSV, fieldrecs[0][8], fieldrecs[-1][8],
              len(fieldrecs), len(ok_fields), len(nocarrier_cols),
              ROWS_TSV, rowrec[8], rowrec[6], rowrec[5]))
    return meas, rowrec[6], ev


# ── 机械证据 ───────────────────────────────────────────────────────────────────
def hist(lines):
    c = collections.Counter()
    for l in lines:
        cells = l.split("\t")
        if cells and cells[0] in DIM_ORDER:
            c[cells[0]] += 1
    return c


def line_hashes(lines):
    return [hashlib.sha256(l.encode("utf-8")).hexdigest() for l in lines]


def acquire_lock():
    for i in range(60):
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            return True, i
        except FileExistsError:
            time.sleep(2)
    return False, 60


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="取锁并写回 策划/状态矩阵.tsv")
    ap.add_argument("--matrix", default=MATRIX)
    ap.add_argument("--rows-tsv", default=os.path.join(ART_SHOT, ROWS_TSV))
    ap.add_argument("--fields-tsv", default=os.path.join(ART_SHOT, FIELDS_TSV))
    ap.add_argument("--show", default="", help="逗号分隔的矩阵行号：打印将写入的 6/7/8 列原文")
    a = ap.parse_args()

    # ---- 0) 产物新鲜度 + 两处副本字节相同 -------------------------------------
    print("== artifact ==")
    for name in (ROWS_TSV, FIELDS_TSV):
        p1 = os.path.join(ART_TEST, name)
        p2 = os.path.join(ART_SHOT, name)
        h1 = sha256_file(p1) if os.path.isfile(p1) else "(缺)"
        h2 = sha256_file(p2) if os.path.isfile(p2) else "(缺)"
        print("  %-24s test/s1=%s  screenshots=%s  identical=%s"
              % (name, h1[:16], h2[:16], h1 == h2))
        if h1 != h2:
            die("两处副本不一致：%s" % name)

    # ---- 1) 读产物 ------------------------------------------------------------
    rows_raw = read_tsv_physical(a.rows_tsv)
    if rows_raw[0][1][0] != "行号":
        die("行级产物表头不对：%r" % rows_raw[0][1])
    rowrecs = {}
    for ln, cells in rows_raw[1:]:
        if len(cells) < 7:
            die("行级产物第 %d 行列数 %d < 7" % (ln, len(cells)))
        rowrecs[int(cells[0])] = (cells + ["", ""])[:8] + [ln]
    fields_raw = read_tsv_physical(a.fields_tsv)
    frecs = collections.defaultdict(list)
    for ln, cells in fields_raw[1:]:
        if len(cells) < 7:
            die("字段级产物第 %d 行列数 %d < 7" % (ln, len(cells)))
        rec = (cells + ["", ""])[:8] + [ln]
        frecs[int(cells[0])].append(rec)
    print("  rows.tsv 数据行=%d   fields.tsv 数据行=%d"
          % (len(rowrecs), sum(len(v) for v in frecs.values())))

    # ---- 2) 读矩阵 + 比对目标集 ----------------------------------------------
    bom, nl, lines = read_matrix_raw()
    s1_empty = [i for i, l in enumerate(lines, 1)
                if l.split("\t")[0] == "S1数值" and len(l.split("\t")) >= 8
                and l.split("\t")[5].strip() == ""]
    print("  矩阵：S1数值 空「实测」行 = %d ；行级产物行号 = %d" % (len(s1_empty), len(rowrecs)))
    # 目标集 = 行级产物的行号（**不是**「实测为空的行」）⇒ 填充后重跑仍然命中同一批行、写同样的文本。
    # 唯一要求：矩阵里**任何**还空着的 S1 行都必须在产物里覆盖到（⛔ 不许静默漏行）。
    miss = sorted(set(s1_empty) - set(rowrecs.keys()))
    if miss:
        die("矩阵还有 %d 个空 S1 行不在行级产物里（⛔ 不许漏）：%s" % (len(miss), miss[:10]))

    # 我们的运行时表（取主键列值 + 物理行号）
    ours_cache = {}
    for lg, rt, _ in C.TABLES:
        path = os.path.join(ROOT, C.RT_DIR_REL, rt + ".tsv")
        recs = read_tsv_physical(path)
        hdr = recs[0][1]
        d = {}
        for pln, cells in recs[1:]:
            key = cell(cells, 0)
            row = {"_rtline": pln}
            for i, h in enumerate(hdr):
                row[h] = cell(cells, i)
            d[key] = row
        ours_cache[lg] = d
    affix_lines = build_affix_lines()
    print("  affix 选行口径复现（spawnable=1 且 level<=%d）= %d 个 nid" % (AFFIX_MAX_LEVEL, len(affix_lines)))
    for name, o in sorted(_OFF.items()):
        if o["blank_lines"]:
            print("  [note] %s 有内层空行（物理行号与 convert.py 的去空行下标不同）：%s"
                  % (name, o["blank_lines"][:5]))

    # ---- 3) 逐行构造 ----------------------------------------------------------
    st = Stats()
    new = {}
    for ln in sorted(rowrecs):
        cells = lines[ln - 1].split("\t")
        if cells[0] != "S1数值":
            die("L%d 不是 S1数值 行：%r" % (ln, cells[0]))
        rowrec = rowrecs[ln]
        logical = C.logical_of_matrix_entity(cells[1])
        key = cells[2].split("=", 1)[1] if "=" in cells[2] else cells[2]
        ours = ours_cache[logical].get(key)
        if ours is None:
            die("L%d：运行时表 %s 里没有主键 %r" % (ln, RT_NAME[logical], key))
        meas, verdict, ev = build_row_text(ln, cells[1], cells[2], rowrec,
                                           frecs[ln], ours, affix_lines, st)
        new[ln] = (meas, verdict, ev)

    if a.show:
        for tok in a.show.split(","):
            ln = int(tok.strip())
            meas, verdict, ev = new[ln]
            print("---- L%d ----" % ln)
            print("  [6] 实测 = %s" % meas)
            print("  [7] 结论 = %s" % verdict)
            print("  [8] 证据 = %s" % ev)

    print("== 逐行核对 ==")
    print("  目标行=%d（= 行级产物）" % len(new))
    print("  每张表：目标行 / 核对过的官方列数 / 缺官方值列数")
    for lg, _, _ in C.TABLES:
        print("    %-14s rows=%-4d cols_checked=%-5d no-carrier=%d"
              % (lg, st.rows[lg], st.checked[lg], st.nocarrier[lg]))
    print("  含 E44 占位列（monumod ac_mul/res_*）的行 = %d" % st.e44_rows)

    # ---- 4) 组装 + 机械证据 ---------------------------------------------------
    out = list(lines)
    changed = 0
    for ln, (meas, verdict, ev) in new.items():
        c = out[ln - 1].split("\t")
        if len(c) != 8:
            die("L%d 列数 %d != 8" % (ln, len(c)))
        c[5], c[6], c[7] = meas, verdict, ev
        out[ln - 1] = "\t".join(c)
        changed += 1

    h0, h1 = hist(lines), hist(out)
    lh0, lh1 = line_hashes(lines), line_hashes(out)
    target = set(new.keys())
    target_ok = all(lh0[i - 1] == lh1[i - 1] for i in target)
    others_ok = all(lh0[i - 1] == lh1[i - 1] for i in range(1, len(lines) + 1) if i not in target)
    n_other = sum(1 for i in range(1, len(lines) + 1) if i not in target)
    hsh_other = hashlib.sha256(
        b"".join(lh1[i - 1].encode() for i in range(1, len(lines) + 1) if i not in target)
    ).hexdigest()
    print("== machine evidence ==")
    print("  行数(total lines) before=%d after=%d equal=%s" % (len(lines), len(out), len(lines) == len(out)))
    print("  维度直方图 unchanged=%s" % (h0 == h1))
    for d in DIM_ORDER:
        print("     %-8s before=%-5d after=%-5d" % (d, h0.get(d, 0), h1.get(d, 0)))
    print("  目标行（%d）逐行 sha256 变化=%d（首次写入 = %d；重跑 = 0 ⇒ 幂等）"
          % (len(target), sum(1 for i in target if lh0[i - 1] != lh1[i - 1]), len(target)))
    print("  目标行重跑后与当前表内容相同（幂等）=%s" % target_ok)
    print("  非目标行（%d）逐行 sha256 不变=%s ；其 sha256 串联摘要=%s"
          % (n_other, others_ok, hsh_other[:32]))
    if not others_ok:
        die("非目标行被改动 ⇒ 停手")
    newtext = nl.join(out)
    print("  写入前全文 sha256=%s" % hashlib.sha256(
        (b"\xef\xbb\xbf" if bom else b"") + "\n".join(lines).encode("utf-8")).hexdigest()[:32])
    print("  写入后全文 sha256=%s" % hashlib.sha256(
        (b"\xef\xbb\xbf" if bom else b"") + newtext.encode("utf-8")).hexdigest()[:32])

    if not a.apply:
        print("DRY-RUN（未写盘）。--apply 才取锁写表。")
        return 0

    # ---- 5) 取锁 + 重读 + 写 + 删锁 ------------------------------------------
    ok, tries = acquire_lock()
    if not ok:
        die("LOCK-FAIL 拿不到 %s（重试 %d 次）" % (LOCK, tries))
    print("LOCK acquired %s（重试 %d 次）" % (LOCK, tries))
    try:
        bom2, nl2, lines2 = read_matrix_raw()
        if lines2 != lines:
            die("取锁后重读整表发现内容与取锁前不同 ⇒ 停手（可能有并发写）")
        print("  取锁后重读整表：%d 行，与取锁前逐行相同 = True" % len(lines2))
        with open(a.matrix, "wb") as fh:
            fh.write((b"\xef\xbb\xbf" if bom2 else b"") + newtext.encode("utf-8"))
        print("WRITTEN %s（改了 %d 行）" % (a.matrix, changed))
    finally:
        if os.path.exists(LOCK):
            os.remove(LOCK)
            print("LOCK released")
    # 写完回读校验
    bom3, nl3, lines3 = read_matrix_raw()
    if lines3 != out:
        die("写盘后回读与预期不符")
    print("  写盘后回读逐行相同 = True ；新全文 sha256=%s"
          % hashlib.sha256(open(a.matrix, "rb").read()).hexdigest())
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except FillError as e:
        print("[STOP] %s" % e)
        sys.exit(3)
