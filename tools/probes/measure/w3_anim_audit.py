# -*- coding: utf-8 -*-
"""
W5 片 · **动画 1:1 审计**（逐单位 × 逐动作：方向数 / 每向帧数 / 引用帧是否都在 / 是否全方向复用同一帧）。

产物（真 TAB）：
  ① `<仓库根>/.ai-tmp/screenshots/w3_anim_audit.tsv` —— 任务书指定的 7 列：
     单位 <TAB> 动作 <TAB> 原版方向数/每向帧数(出处行) <TAB> 工程方向数/帧数 <TAB>
     工程实际引用帧文件数 <TAB> 缺帧 <TAB> 一致|不一致
  ② `<仓库根>/.ai-tmp/screenshots/w3_anim_trigger.tsv` —— "动作是否真被触发"（4 列）：
     单位 <TAB> 动作 <TAB> 触发点(文件:行) <TAB> 结论

判据（⛔ 全部从磁盘 / 源码解析，**不抄注释**）：
  A. **原版方向数 / 每向帧数** = `client/Assets/Resources/Clover/D2/{Chars,Monsters}/<单位>/manifest.json`
     的 `actions.<动作>.dirs` / `.frames`（由原版 `.cof` 的 `framesPerDirection` 导出），
     并与生成物 `tools/probes/measure/SpriteFrameCounts.generated.cs` 的 `ByUnit` 行**逐格对账**
     （两处不一致 ⇒ 判 CRITICAL，脚本退出码 2）。
  B. **工程方向数 / 帧数** = 从 `Module/View/SpriteFrames.cs` 解析 `FrameCounts` / `ActionKeys` /
     `FallbackChain`，与 `Module/View/ViewAnim.cs` 的动作下标一起**复算** `SpriteFrames.Keys(...)`
     会拼出哪些帧键（同一套公式的第二处实现，与宿主 `animcheck` 的机器闸门互为独立实现）。
  C. **引用帧文件数 / 缺帧** = 把 B 算出的 8×N 个键逐个映射到磁盘 PNG 实测存在性。
  D. **是否全方向复用同一帧充数** = 这 8×N 张 PNG 取 md5：两条方向若**逐字节相同** ⇒
     判"复用充数"（原版 8 方向 = 8 套独立 `.cof`，必然互不相同）。
  E. **触发点** = 从 `Module/View/ViewModule.cs` 解析 `PlayAnim(...)` 的实参与**行号**（不写死行号）。

退出码：0 = 全部一致；1 = 有**未登记**的不一致（真缺陷）；2 = 脚本自身对账失败。
只依赖标准库。
"""

import hashlib
import io
import json
import os
import re
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass


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
D2 = os.path.join(REPO, "client", "Assets", "Resources", "Clover", "D2")
GEN_CS = os.path.join(REPO, "tools", "probes", "measure", "SpriteFrameCounts.generated.cs")
VIEW_DIR = os.path.join(SCRIPTS, "Module", "View")
OUT_DIR = os.path.join(REPO, ".ai-tmp", "screenshots")
OUT_AUDIT = os.path.join(OUT_DIR, "w3_anim_audit.tsv")
OUT_TRIGGER = os.path.join(OUT_DIR, "w3_anim_trigger.tsv")

#: 任务书点名的**全部单位**（顺序 = 报告顺序）。
#: (显示名, 种类, 单位键 == 磁盘目录名 == `SpriteFrameCounts.ByUnit` 的键, 资源子路径)
UNITS = [
    ("Amazon（亚马逊）", "player", "amazon", "Chars/amazon"),
    ("Barbarian（野蛮人）", "player", "barbarian", "Chars/barbarian"),
    ("Sorceress（法师）", "player", "sorceress", "Chars/sorceress"),
    ("Necromancer（死灵法师）", "player", "necromancer", "Chars/necromancer"),
    ("Paladin（圣骑士）", "player", "paladin", "Chars/paladin"),
    ("堕落者 Fallen", "monster", "fa", "Monsters/fa"),
    ("堕落萨满 FallenShaman", "monster", "fs", "Monsters/fs"),
    ("尖刺鼠 QuillRat", "monster", "si", "Monsters/si"),
    ("僵尸 Zombie", "monster", "zm", "Monsters/zm"),
    ("堕落罗格 CorruptRogue", "monster", "cr", "Monsters/cr"),
    ("血鹰 BloodHawk", "monster", "bk", "Monsters/bk"),
    ("巨兽 Brute", "monster", "ye", "Monsters/ye"),
    ("幽灵 Wraith", "monster", "wr", "Monsters/wr"),
    ("阿卡拉 Akara(NPC)", "npc", "ps", "Monsters/ps"),
    ("卡夏 Kashya(NPC)", "npc", "rc", "Monsters/rc"),
    ("恰西 Charsi(NPC)", "npc", "ci", "Monsters/ci"),
    ("基德 Gheed(NPC)", "npc", "gh", "Monsters/gh"),
    ("瓦瑞夫 Warriv(NPC)", "npc", "wa", "Monsters/wa"),
]

DIRS = ["s", "sw", "w", "nw", "n", "ne", "e", "se"]

CRITICAL = []     # 脚本自身对账失败（两处判据互相矛盾）
BAD = []          # 未登记的 不一致（真缺陷）


def disk_png(kind, unit, name):
    sub = "Chars" if kind == "player" else "Monsters"
    return os.path.join(D2, sub, unit, name + ".png")


def rel(p):
    return os.path.relpath(p, REPO).replace("\\", "/")


# ─────────────────────────────────────────────────────────────────────────────
# 1. 生成物 SpriteFrameCounts.generated.cs（原版帧数判据 ①）
# ─────────────────────────────────────────────────────────────────────────────
def parse_generated():
    text = io.open(GEN_CS, encoding="utf-8").read()
    lines = text.split("\n")

    m = re.search(r"ActionNames\s*=\s*\{(.*?)\};", text, re.S)
    if not m:
        raise SystemExit("CRITICAL: 解析不出 SpriteFrameCounts.ActionNames")
    actions = re.findall(r'"([a-z]+)"', m.group(1))

    by_unit, line_of = {}, {}
    for i, ln in enumerate(lines, 1):
        mm = re.match(r'\s*\{\s*"([A-Za-z0-9_]+)",\s*new\[\]\s*\{([^}]*)\}\s*\}', ln)
        if not mm:
            continue
        by_unit[mm.group(1)] = [int(x) for x in re.findall(r"-?\d+", mm.group(2))]
        line_of[mm.group(1)] = i
    if not by_unit:
        raise SystemExit("CRITICAL: 解析不出 SpriteFrameCounts.ByUnit")
    return actions, by_unit, line_of


# ─────────────────────────────────────────────────────────────────────────────
# 2. manifest.json（原版帧数判据 ② + "出处行"）
# ─────────────────────────────────────────────────────────────────────────────
def parse_manifest(kind, unit):
    sub = "Chars" if kind == "player" else "Monsters"
    path = os.path.join(D2, sub, unit, "manifest.json")
    if not os.path.isfile(path):
        CRITICAL.append("manifest 不存在：%s" % rel(path))
        return None
    text = io.open(path, encoding="utf-8").read()
    try:
        data = json.loads(text)
    except Exception as e:                                  # pragma: no cover
        CRITICAL.append("manifest 解析失败 %s：%s" % (rel(path), e))
        return None

    action_line = {}
    for i, ln in enumerate(text.split("\n"), 1):
        mm = re.match(r'\s*"([a-z]+)"\s*:\s*\{\s*$', ln)
        if mm:
            action_line[mm.group(1)] = i
    return {"path": rel(path), "data": data, "actionLine": action_line}


# ─────────────────────────────────────────────────────────────────────────────
# 3. SpriteFrames.cs / ViewAnim.cs（工程侧口径）
# ─────────────────────────────────────────────────────────────────────────────
def parse_spriteframes():
    path = os.path.join(VIEW_DIR, "SpriteFrames.cs")
    text = io.open(path, encoding="utf-8").read()

    m = re.search(r"public static readonly int\[\] FrameCounts\s*=\s*\{(.*?)\};", text, re.S)
    if not m:
        raise SystemExit("CRITICAL: 解析不出 SpriteFrames.FrameCounts")
    counts = [int(x) for x in re.findall(r"-?\d+", re.sub(r"//[^\n]*", "", m.group(1)))]

    m = re.search(r'ActionKeys\s*=\s*\{([^}]*)\};', text)
    if not m:
        raise SystemExit("CRITICAL: 解析不出 SpriteFrames.ActionKeys")
    action_keys = re.findall(r'"([a-z]+)"', m.group(1))

    m = re.search(r"FallbackChain\s*=\s*\{(.*?)\n        \};", text, re.S)
    fallback = []
    if m:
        for row in re.findall(r"new\[\]\s*\{([^}]*)\}", m.group(1)):
            fallback.append([x.lower() for x in re.findall(r"ViewAnim\.(\w+)", row)])
    if len(fallback) != len(action_keys):
        CRITICAL.append("FallbackChain 行数 %d != 动作数 %d" % (len(fallback), len(action_keys)))
    return {"counts": counts, "actionKeys": action_keys, "fallback": fallback,
            "path": rel(path)}


def parse_viewanim():
    path = os.path.join(VIEW_DIR, "ViewAnim.cs")
    text = io.open(path, encoding="utf-8").read()
    out = {}
    for name, val in re.findall(r"(\w+)\s*=\s*(\d+)\s*,", text):
        out[name.lower()] = int(val)          # 键 = 动作名小写（与帧数表的下标口径一致）
    return out, rel(path)


# ─────────────────────────────────────────────────────────────────────────────
# 4. ViewModule.cs（触发点 + 方法行区间）
# ─────────────────────────────────────────────────────────────────────────────
def parse_viewmodule():
    path = os.path.join(VIEW_DIR, "ViewModule.cs")
    src = io.open(path, encoding="utf-8").read()
    lines = src.split("\n")

    # 每个动作的触发点（`文件:行`）—— `PlayAnim(...)` 的实参 / `want` 的三元表达式
    trig = {}
    for i, ln in enumerate(lines, 1):
        if ln.lstrip().startswith("//"):
            continue
        if "PlayAnim(" in ln:
            for a in re.findall(r"ViewAnim\.(\w+)", ln):
                trig.setdefault(a, set()).add(i)
        if "var want" in ln or "var moveAnim" in ln:
            blk = "\n".join(lines[i - 1:i + 8])
            for a in re.findall(r"ViewAnim\.(\w+)", blk):
                trig.setdefault(a, set()).add(i)

    # 方法行区间（粗粒度：方法名行 → 下一个缩进 4 空格的 `}`）
    def method_range(sig):
        for i, ln in enumerate(lines):
            if sig in ln:
                for j in range(i + 1, len(lines)):
                    if lines[j].rstrip() == "        }":
                        return i + 1, j + 1
        return None

    ranges = {}
    for key, sig in [
        ("OnStageEntered", "private void OnStageEntered"),
        ("TickNpcs", "private void TickNpcs"),
        ("TickPlayer", "private void TickPlayer"),
        ("TickOne", "private void TickOne"),
        ("UpdateMonster", "public void UpdateMonster"),
        ("PlayHit", "public void PlayHit"),
        ("PlayDeath", "public void PlayDeath"),
        ("OnSkillCast", "private void OnSkillCast"),
        ("OnPlayerAttacked", "private void OnPlayerAttacked"),
    ]:
        ranges[key] = method_range(sig)
    return trig, ranges, rel(path)


def parse_view_sources():
    """扫 `Module/View/**` 全部源码：动作选择已抽到纯函数 `ViewAnimState.SelectPlayer/SelectMonster`
    （`Module/View/ViewAnimState.cs`）⇒ 触发点必须**跨文件**找，否则会误判"没有触发点"。"""
    trig, ranges, files = {}, {}, []

    # 动作选择纯函数（新文件）：把函数体里出现的 ViewAnim.X 记成一个方法内的触发点
    sel = os.path.join(VIEW_DIR, "ViewAnimState.cs")
    if os.path.isfile(sel):
        text = io.open(sel, encoding="utf-8").read()
        lines = text.split("\n")
        r = rel(sel)
        files.append(r)
        for i, ln in enumerate(lines, 1):
            if ln.lstrip().startswith("//"):
                continue
            for a in re.findall(r"ViewAnim\.(\w+)", ln):
                trig.setdefault(a, set()).add((r, i))
        for key, sig in [("SelectPlayer", "public static ViewAnim SelectPlayer"),
                         ("SelectMonster", "public static ViewAnim SelectMonster"),
                         ("IsHitHolding", "public static bool IsHitHolding")]:
            for i, ln in enumerate(lines):
                if sig in ln:
                    for j in range(i + 1, len(lines)):
                        if lines[j].rstrip() in ("        }", "    }"):
                            ranges[key] = (r, i + 1, j + 1)
                            break
                    break

    # ViewModule（原解析路径）—— 行号带文件前缀
    vm_trig, vm_ranges, vm_rel = parse_viewmodule()
    files.append(vm_rel)
    for a, lns in vm_trig.items():
        for i in lns:
            trig.setdefault(a, set()).add((vm_rel, i))
    for k, v in vm_ranges.items():
        if v is not None:
            ranges[k] = (vm_rel, v[0], v[1])

    return {"trig": trig, "ranges": ranges, "files": files}


#: 哪一类单位的运行期会走哪些方法（触发点必须落在这些方法里才算"真被触发"）。
#: `SelectPlayer` / `SelectMonster` = 片 W5 抽出的**动作选择纯函数**（分别只被 TickPlayer / UpdateMonster 调用）。
KIND_METHODS = {
    "player": ["TickPlayer", "SelectPlayer", "IsHitHolding", "OnSkillCast", "OnPlayerAttacked",
               "PlayHit", "PlayDeath"],
    "monster": ["UpdateMonster", "SelectMonster", "PlayHit", "PlayDeath"],
    "npc": ["OnStageEntered", "TickNpcs"],
}

#: NPC 在原版城镇里**只站着**（不移动、不参战、不受伤）⇒ 这些动作对它不适用。
NPC_ONLY_IDLE = ("walk", "attack", "cast", "hit", "death", "run")

#: **已登记**的触发不一致（`(种类, 动作)` → 理由）。按任务书"⛔ 拿不到出处就只登记、不许编"：
#: 这两条的根因是**原版触发条件缺出处**，不是工程漏接线 ⇒ 登记不改（登记 ≠ 交付，回报里逐条列）。
REGISTERED_TRIGGER = {
    ("monster", "run"):
        "原版怪物 `RN` 的触发条件（`monstats.txt` 的 `Run` 列语义）本机无出处 —— 素材实测只有 `zm`/`cr` "
        "有 RN，而 `Run` 列在 8 只怪上全非 0（`fa`/`si`/`bk`/`ye`/`wr` 却没有 RN）⇒ 不能用它当判据；"
        "工程只有 `TickPlayer` 请求 Run（玩家走/跑两套动画）",
    ("monster", "cast"):
        "只有 `wr`（幽灵）有 SC（16 帧）；哪只怪在什么时候用 SC 无出处（`wr` 的 AI 是 `Wraith`→近战类），"
        "工程不给它造句",
}
TRIG_BAD = []


# ─────────────────────────────────────────────────────────────────────────────
# 5. md5（"全方向复用同一帧充数"）
# ─────────────────────────────────────────────────────────────────────────────
def md5_of(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return h.hexdigest()


# ─────────────────────────────────────────────────────────────────────────────
# 6. 触发判定
# ─────────────────────────────────────────────────────────────────────────────
def trigger_verdict(kind, act, has_orig, vm, sites):
    """(结论, 理由). 结论 ∈ 一致 / 不一致 / 不适用。

    判定口径（全部可复算）：
      ① 原版就没有该动作 ⇒ 不适用；
      ② 所有触发点都**不落在**该类单位的运行期方法里（`KIND_METHODS`）⇒ 该单位从不播它；
      ③ 玩家受击特例：`PlayHit` 会设 Hit，但 `TickPlayer` 的 want 链里没有受击档 ⇒ 同帧被覆盖成 Idle
         ⇒ 受击动画一帧都不会被渲染（这是"定义了但没人用"的最隐蔽形态）。
    """
    if not has_orig:
        return "不适用", "原版无 %s 的 %s 动作（.cof 无该模式）⇒ 运行期不会请求" % (kind, act)
    if not sites:
        return "不一致", "定义了但没人用：ViewModule 里**没有任何**触发点"

    in_kind = [s for s in sites
               if any(_in_range([s], vm["ranges"].get(m)) for m in KIND_METHODS[kind])]

    if kind == "npc" and act in NPC_ONLY_IDLE:
        return "不适用", "该 NPC 在原版城镇里只站着（%s 动作不触发）" % act
    hit_ok = any(_in_range(sites, vm["ranges"].get(m))
                 for m in ("TickPlayer", "SelectPlayer", "IsHitHolding"))
    if kind == "player" and act == "hit" and not hit_ok:
        return "不一致", ("`PlayHit`(%s) 设过 Hit，但 `TickPlayer` 的 want 链"
                         "（%s / %s / %s）里没有受击档 ⇒ 同一帧内就被覆盖成 Idle"
                         " ⇒ 受击动画**一帧都不会被渲染**（离线复现见 `animcheck` §6d）"
                         % (_sites_txt(vm, sites), _rng_txt(vm, "TickPlayer"),
                            _rng_txt(vm, "SelectPlayer"), _rng_txt(vm, "SelectMonster")))
    if not in_kind:
        return "不一致", ("触发点 %s 全在**别的类单位**的路径上；%s 的运行期（%s）里没有它 "
                         "⇒ 该单位原版有 %s 但从不播（出处不明，登记）"
                         % (_sites_txt(vm, sites), kind, "/".join(KIND_METHODS[kind]), act))
    return "一致", "触发点 %s" % _sites_txt(vm, in_kind)


def _sites_txt(vm, sites):
    """`触发点(文件:行)` —— sites 元素 = (相对路径, 行号)。"""
    if not sites:
        return "—"
    return "；".join("%s:%d" % (f, ln) for f, ln in sites[:6])


def _rng_txt(vm, method):
    r = vm["ranges"].get(method)
    return "—" if not r else "%s:%d-%d" % r


def _in_range(sites, rng):
    """sites = [(文件, 行)]；rng = (文件, 起, 止)。同文件且行号落在区间内才算。"""
    if not rng or not sites:
        return False
    f, a, b = rng
    return any(sf == f and a <= sl <= b for sf, sl in sites)


# ─────────────────────────────────────────────────────────────────────────────
# 7. main
# ─────────────────────────────────────────────────────────────────────────────
def main():
    if not os.path.isdir(OUT_DIR):
        os.makedirs(OUT_DIR, exist_ok=True)

    gen_actions, gen_by_unit, gen_line = parse_generated()
    sf = parse_spriteframes()
    va, va_path = parse_viewanim()
    vm = parse_view_sources()

    anims = list(gen_actions)                 # idle walk attack cast hit death run
    idx = {a: i for i, a in enumerate(anims)}

    print("================ W5 动画 1:1 审计 ================")
    print("原版判据 ① %s（ByUnit：逐单位 × 逐动作 framesPerDirection）" % rel(GEN_CS))
    print("原版判据 ② D2/{Chars,Monsters}/<单位>/manifest.json 的 actions.<动作>.{frames,dirs}")
    print("工程判据 ③ %s（FrameCounts / ActionKeys / FallbackChain）" % sf["path"])
    print("工程判据 ④ %s" % va_path)
    print("触发点   ⑤ %s" % "、".join(vm["files"]))
    print()

    # ── 两处原版判据的动作次序必须一致（否则整张帧数表错位）──
    if sf["actionKeys"] != anims:
        CRITICAL.append("SpriteFrames.ActionKeys %s != SpriteFrameCounts.ActionNames %s"
                        % (sf["actionKeys"], anims))
    for a in anims:
        if a not in va:
            CRITICAL.append("ViewAnim 里没有动作 %s（帧数表下标会错位）" % a)
        elif va[a] != idx[a]:
            CRITICAL.append("ViewAnim.%s = %d，但帧数表里 %s 的下标是 %d"
                            % (a, va[a], a, idx[a]))
    if "amazon" in gen_by_unit and sf["counts"] != gen_by_unit["amazon"]:
        CRITICAL.append("SpriteFrames.FrameCounts %s != SpriteFrameCounts[\"amazon\"] %s"
                        % (sf["counts"], gen_by_unit["amazon"]))

    rows, trig_rows = [], []
    stat = {"units": 0, "rows": 0, "一致": 0, "不一致": 0, "帧数": 0, "文件": 0,
            "文件_原版有": 0, "文件_回退行": 0,
            "缺帧": 0, "触发一致": 0, "触发不一致": 0, "触发不适用": 0}

    for disp, kind, unit, _sub in UNITS:
        man = parse_manifest(kind, unit)
        gen = gen_by_unit.get(unit)
        if gen is None:
            CRITICAL.append("SpriteFrameCounts.ByUnit 里没有单位「%s」" % unit)
            continue
        stat["units"] += 1

        for act in anims:
            i = idx[act]
            gen_frames = gen[i]

            man_frames = man_dirs = man_line = None
            if man:
                a = (man["data"].get("actions") or {}).get(act)
                if a is not None:
                    man_frames = int(a.get("frames", 0))
                    man_dirs = int(a.get("dirs", 0))
                    man_line = man["actionLine"].get(act)
            if man_frames is not None and man_frames != gen_frames:
                CRITICAL.append("%s/%s：manifest frames=%d 但生成物 ByUnit=%d"
                                % (unit, act, man_frames, gen_frames))
            if man_dirs is not None and man_dirs != len(DIRS):
                CRITICAL.append("%s/%s：manifest dirs=%d（期望 %d）" % (unit, act, man_dirs, len(DIRS)))

            has_orig = gen_frames > 0

            # ── 工程侧：回退链 ⇒ 实际会请求的动作 + 帧数 ──
            real_act, fallback_to = act, None
            if not has_orig:
                for cand in (sf["fallback"][i] if i < len(sf["fallback"]) else []):
                    ci = idx.get(cand)
                    if ci is not None and gen[ci] > 0:
                        real_act, fallback_to = cand, cand
                        break
            frames = gen[idx[real_act]] if gen[idx[real_act]] > 0 else sf["counts"][idx[real_act]]

            # ── 逐方向 × 逐帧的键 → 磁盘 ──
            names, missing = [], []
            for d in DIRS:
                for f in range(frames):
                    n = "%s_%s_%d" % (real_act, d, f)
                    names.append(n)
                    if not os.path.isfile(disk_png(kind, unit, n)):
                        missing.append(n)
            files = len(names) - len(missing)

            # ── 方向间 md5 是否雷同（"全方向复用同一帧充数"）──
            dup = []
            if not missing and frames > 0:
                per_dir = {}
                for d in DIRS:
                    per_dir[d] = [md5_of(disk_png(kind, unit, "%s_%s_%d" % (real_act, d, f)))
                                  for f in range(frames)]
                for a1 in range(len(DIRS)):
                    for a2 in range(a1 + 1, len(DIRS)):
                        if per_dir[DIRS[a1]] == per_dir[DIRS[a2]]:
                            dup.append("%s==%s" % (DIRS[a1], DIRS[a2]))

            # ── 帧/方向/文件 的判定（col7 只放这两个值）──
            #   ⚠️ "原版无该动作" **不是** 不一致：原版没有 ⇒ 工程也不该有（铁律 1）。
            #      工程侧若被误请求，会沿 `FallbackChain` 回退到**该单位自己的**原版动画
            #      （`SpriteFrames.ResolveAnim`，每条只报一次日志），不产生占位色块。
            reasons, blocking = [], []
            if not has_orig:
                reasons.append("原版无该动作（.cof 无该模式）" +
                               ("；工程回退 → %s（FallbackChain[%d]）" % (fallback_to, i) if fallback_to else ""))
            elif frames != gen_frames:
                blocking.append("工程帧数 %d != 原版 %d" % (frames, gen_frames))
            if missing:
                blocking.append("缺帧 %d 张" % len(missing))
            if dup:
                blocking.append("方向间复用同一帧：%s" % ", ".join(dup))
            reasons += blocking

            verdict = "一致" if not blocking else "不一致"
            stat["rows"] += 1
            stat[verdict] += 1
            if not missing:
                stat["文件"] += len(names)
                stat["文件_原版有" if has_orig else "文件_回退行"] += len(names)
            stat["帧数"] += frames
            stat["缺帧"] += len(missing)
            if blocking:
                BAD.append("%s/%s：%s" % (unit, act, "；".join(blocking)))

            if has_orig and man_line:
                orig_cell = ("8 方向 × %d 帧/向（出处 manifest %s:%d 的 \"%s\" → frames=%d,dirs=%d；"
                             "生成物 %s:%d）"
                             % (gen_frames, man["path"], man_line, act, gen_frames, man_dirs or 8,
                                os.path.basename(GEN_CS), gen_line[unit]))
            elif has_orig:
                orig_cell = ("8 方向 × %d 帧/向（出处 生成物 %s:%d）"
                             % (gen_frames, os.path.basename(GEN_CS), gen_line[unit]))
            else:
                orig_cell = "无此动作（.cof 无该模式）"

            eng_cell = "8 方向 × %d 帧/向%s" % (
                frames, "" if real_act == act else "（回退到 %s）" % real_act)

            miss_cell = "0" if not missing else "%d 张（%s%s）" % (
                len(missing), ", ".join(missing[:3]), "…" if len(missing) > 3 else "")

            rows.append([disp, act, orig_cell if not reasons else orig_cell + " │ " + "；".join(reasons),
                         eng_cell, "%d/%d" % (files, len(names)), miss_cell, verdict])

            # ── 触发判定 ──
            all_sites = sorted(vm["trig"].get(act.capitalize(), set()))
            v, why = trigger_verdict(kind, act, has_orig, vm, all_sites)
            if v == "不一致":
                reg = REGISTERED_TRIGGER.get((kind, act))
                if reg:
                    why += "；【已登记（无出处）】" + reg
                else:
                    TRIG_BAD.append("%s/%s：%s" % (unit, act, why))
            stat["触发" + v] += 1
            trig_rows.append([disp, act,
                              _sites_txt(vm, all_sites) if all_sites else "—",
                              ("%s（%s）" % (v, why)) if why else v])

    # ── 投射物 / 传送门（任务书点名要求覆盖）──
    rows.append(["投射物（Table/Missile.tsv：箭矢 arrow / 火弹 / 冰弹 … 22 行）", "idle（单帧）",
                 "原版素材未到手：`d2data.mpq` 的 `.dcc`（`missile_c.cel_file` 已登记名，如 Arrow/"
                 "Firebolt）⇒ 无 `framesPerDirection` 可对 │ 已登记：`client/资源欠缺清单.md`",
                 "1 张 2×2 纯色占位（`Module/Skill/ProjectileView.cs:108-130`，按伤害类型着色）",
                 "0/1", "1 张（arrow_idle_s_0.png 等）", "不一致"])
    rows.append(["精英（champion；`MonsterSpawner.cs:472` 置 `isChampion`，8 种怪都可能镀词缀）",
                 "同基础怪的 idle/walk/attack/hit/death（×8 方向）",
                 "原版**不另出 `.cof`** —— 精英与普通的差异是**运行期调色板变体**（`Pal.PL2` 族），"
                 "而逐词缀的精英调色板本机**没有出处**（`原版资源/` 被 .gitignore 排除）│ 已登记：本片",
                 "同基础怪帧键（`SpriteFrames.SpriteCodeOf(kindId)` **不区分精英**，`Module/View/ViewModule.cs:328-333`）"
                 "+ 金色占位色（`SpriteFrames.PlaceholderColorOfMonster` 的 `isChampion` 分支；"
                 "`ApplyTint` 只在 `UsingPlaceholder` 时上色 ⇒ **真素材到位后精英与普通怪一模一样**）",
                 "同基础怪（0 缺）", "0", "不一致"])
    # ↑ 第 3 列已含判定理由；补齐"为什么登记不改"一句话（口径 = 任务书"拿不到出处不许编"）
    rows[-1][2] += (" │ 原版精英 = **调色板移位**（可见差异），本项目没有该调色板 ⇒ 「拿不到出处只登记、"
                    "不许编金色 tint」；影响域 = `Module/View/SpriteFrames.cs` + `ViewModule.ApplyTint`")
    rows.append(["传送门（D2/Objects/warp 81 瓦片；回城卷轴 item_c#89）", "无（原版为调色板循环，非逐帧）",
                 "`warp.dt1` 是**地形瓦片集**（manifest.tiles 81 条），不是逐帧动画；城镇出口原版不画物件"
                 "（`Module/Map/MapView.cs:706-713` 片 4 实测）│ 已登记：本片",
                 "未实现（片 4 删除 ExitWarpTiles；回城卷轴无传送门实体）",
                 "0/0", "0", "不一致"])
    trig_rows.append(["精英（champion）", "同基础怪（idle/walk/attack/hit/death）",
                      "`Module/View/ViewModule.cs:328-333`（与普通怪**同一条**路径）",
                      "不适用（不另出动作；缺的是**调色板**不是动画 ⇒ 行见审计表；已登记）"])
    trig_rows.append(["投射物", "idle（单帧）", "`Module/Skill/ProjectileView.cs:37` TryCreate",
                      "不适用（原版素材未到手，用纯色占位；已登记）"])
    trig_rows.append(["传送门", "无", "—", "不适用（无传送门实体；已登记，影响域 = Module/Item + Module/Map）"])

    # ── 写 TSV 之前先钉住**表格形状**（列数写错 = 交付物不合口径，必须判红）──
    bad_a = [r[0] for r in rows if len(r) != 7]
    bad_t = [r[0] + "/" + r[1] for r in trig_rows if len(r) != 4]
    if bad_a:
        CRITICAL.append("审计表里有 %d 行的列数不是 7：%s" % (len(bad_a), "、".join(bad_a[:3])))
    if bad_t:
        CRITICAL.append("触发表里有 %d 行的列数不是 4：%s" % (len(bad_t), "、".join(bad_t[:3])))

    # ── 写 TSV ──
    with io.open(OUT_AUDIT, "w", encoding="utf-8", newline="\n") as f:
        f.write("单位\t动作\t原版方向数/每向帧数(出处行)\t工程方向数/帧数\t工程实际引用帧文件数\t缺帧\t一致|不一致\n")
        for r in rows:
            f.write("\t".join(r) + "\n")

    with io.open(OUT_TRIGGER, "w", encoding="utf-8", newline="\n") as f:
        f.write("单位\t动作\t触发点(文件:行)\t结论\n")
        for r in trig_rows:
            f.write("\t".join(r) + "\n")

    # ── 小结 ──
    print("单位 %d 个 / 逐单位行 %d 条（一致 %d，不一致 %d）+ 专项行 %d 条（精英 / 投射物 / 传送门）"
          " ⇒ TSV 共 %d 行"
          % (stat["units"], stat["rows"], stat["一致"], stat["不一致"], len(rows) - stat["rows"],
             len(rows)))
    print("工程引用帧文件 %d 张（原版有该动作的行 %d 张 + 原版没有该动作的回退行 %d 张），缺帧 %d 张"
          % (stat["文件"], stat["文件_原版有"], stat["文件_回退行"], stat["缺帧"]))
    print("触发判定：一致 %d / 不一致 %d / 不适用 %d"
          % (stat["触发一致"], stat["触发不一致"], stat["触发不适用"]))
    print()
    print("产出 %s" % rel(OUT_AUDIT))
    print("产出 %s" % rel(OUT_TRIGGER))
    if CRITICAL:
        print()
        print("!! CRITICAL（脚本自身对账失败，必须先修数据源）%d 条：" % len(CRITICAL))
        for c in CRITICAL:
            print("   ! " + c)
    print()
    print("未登记的不一致（帧 / 方向 / 缺帧 / 方向复用）%d 条：" % len(BAD))
    for b in BAD:
        print("   - " + b)
    print("未登记的不一致（动作未被触发）%d 条：" % len(TRIG_BAD))
    for b in TRIG_BAD:
        print("   - " + b)
    print("已登记的触发不一致（无出处，登记 ≠ 交付）%d 条：" % (stat["触发不一致"] - len(TRIG_BAD)))
    for k, why in REGISTERED_TRIGGER.items():
        print("   * monster/%s：%s" % (k[1], why))
    print("================ 审计结束 ================")
    return 2 if CRITICAL else (1 if (BAD or TRIG_BAD) else 0)


if __name__ == "__main__":
    sys.exit(main())
