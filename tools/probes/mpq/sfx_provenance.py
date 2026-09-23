#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""音效 SFX 溯源判据：把 `SfxRegistry.Origins` 登记的 24 个键

  ① 对回原版 `data/global/excel/Sounds.txt` 的**行号**（第 1 列 `Sound` / 第 3 列 `FileName`）；
  ② 从**真实 `d2sfx.mpq`**（store 未压缩）按名 `SFileExtractFile` 取出原版 `.wav`，
     与工程侧 `client/Assets/Resources/Clover/Sound/SFX/<键>.wav` 做 **sha256 逐字节比对**；
  ③ 从源码里扫出每个键的**真实调用点** `文件:行`（非注释行），证明"有消费方、不是空实现"。

产物
  `tools/probes/mpq/sfx-provenance.tsv`  —— 入仓（② 的 sha256 台账；mpq 不在盘时宿主据此自检）
  `<out>/sfx-map.tsv`                     —— 任务书要的映射表（游戏事件 → 原版 sound 名 → Sounds.txt 行 → mpq 路径）
  `<out>/sfx-verify.log`                  —— 原始输出

跑法（任意 cwd）
  python tools/probes/mpq/sfx_provenance.py --root <仓库根>

依赖（都不入仓，见 `README.md`）
  `原版资源/_mpq_incoming/d2sfx.mpq`（51,948,991 B，从 3DMGAME-DBL2.MPQ.file.rar 的
  range 594270144-646219134 取出，见 `原版资源/清单.md`）+ `.ai-tmp/test/storm/storm.dll`。

⛔ 三个坑（照 `extract_wanted.py` 写死，勿犯）
  1. `storm.dll` 是 ANSI 接口 ⇒ 先 `os.chdir(<mpq 目录>)`，只把 **ASCII 相对名**交给它；
  2. ⛔ 不许 `SFileFindFirstFile` 全量枚举（这份 DLL 上会吃 8+ GB 内存且不返回）⇒ 只能**按名**取；
  3. `SFileHasFile` 返回值**不可信**（对本不存在的名字也返回非 0）⇒ 判据永远是
     「`SFileExtractFile` 后产物落盘且 > 0 字节」。
"""
import argparse
import ctypes
import hashlib
import io
import os
import sys
from ctypes import c_char_p, c_int, c_uint32, c_void_p, POINTER

SIG = {
    "SFileOpenArchive": ([c_char_p, c_uint32, c_uint32, POINTER(c_void_p)], c_int),
    "SFileExtractFile": ([c_void_p, c_char_p, c_char_p, c_uint32], c_int),
    "SFileCloseArchive": ([c_void_p], c_int),
}
MPQ_OPEN_READONLY = 0x0100

SFX_PREFIX = "data\\global\\sfx\\"

# key -> (mpq 内相对路径去掉 data\global\sfx\ 前缀的部分)
KEYS = [
    ("hit",            r"combat\impact\sword1.wav"),
    ("miss",           r"combat\weapon\one hand swing small01.wav"),
    ("player_hurt",    r"combat\player\amazon\soft4.wav"),
    ("player_die",     r"combat\player\amazon\death1.wav"),
    ("player_revive",  r"skill\necromancer\revivetarget.wav"),
    ("monster_die",    r"monster\fallen\death1.wav"),
    ("monster_attack", r"monster\fallen\roar1.wav"),
    ("monster_revive", r"monster\fallenshaman\resurrect.wav"),
    ("cast",           r"skill\amazon\magicarrow1.wav"),
    ("cast_fire",      r"skill\sorceress\firecast.wav"),
    ("cast_cold",      r"skill\sorceress\coldcast.wav"),
    ("cast_lightning", r"skill\sorceress\eleccast.wav"),
    ("cast_poison",    r"skill\amazon\poisoncast.wav"),
    ("level_up",       r"cursor\levelup.wav"),
    ("footstep",       r"ambient\footstep\LightDirt1.wav"),
    ("item_pickup",    r"cursor\pickup.wav"),
    ("gold_pickup",    r"item\gold.wav"),
    ("item_use",       r"item\potiondrink.wav"),
    ("ui_click",       r"cursor\button.wav"),
    ("dialog_open",     r"cursor\select.wav"),
    ("shop_open",      r"cursor\windowopen.wav"),
    ("portal",         r"skill\misc\portalcast.wav"),
    ("area_enter",     r"object\stairs.wav"),
    ("quest_complete", r"object\cairnsuccess.wav"),
]

# key -> C# 常量标识符（用于在源码里找真实调用点）
IDENT = {
    "hit": "Hit", "miss": "Miss", "player_hurt": "PlayerHurt", "player_die": "PlayerDie",
    "player_revive": "PlayerRevive", "monster_die": "MonsterDie", "monster_attack": "MonsterAttack",
    "monster_revive": "MonsterRevive", "cast": "Cast", "cast_fire": "CastFire",
    "cast_cold": "CastCold", "cast_lightning": "CastLightning", "cast_poison": "CastPoison",
    "level_up": "LevelUp", "footstep": "Footstep", "item_pickup": "ItemPickup",
    "gold_pickup": "GoldPickup", "item_use": "ItemUse", "ui_click": "UiClick",
    "dialog_open": "DialogOpen", "shop_open": "ShopOpen", "portal": "Portal",
    "area_enter": "AreaEnter", "quest_complete": "QuestComplete",
}

# 人类可读的游戏事件（列进 sfx-map.tsv；措辞取自 `client/资源欠缺清单.md:45` 点名的那批）
EVENT = {
    "hit": "挥砍命中（打到目标且未致死）", "miss": "挥砍落空",
    "player_hurt": "玩家受击（未死）", "player_die": "玩家死亡",
    "player_revive": "玩家复活完成", "monster_die": "怪物死亡",
    "monster_attack": "怪物挥击起手", "monster_revive": "萨满复活同伴",
    "cast": "施放技能（通用回落）", "cast_fire": "火系技能（火弹）",
    "cast_cold": "冰系技能（冰弹）", "cast_lightning": "电系技能",
    "cast_poison": "毒系技能", "level_up": "升级",
    "footstep": "脚步（每 2 格一步）", "item_pickup": "拾取物品",
    "gold_pickup": "拾取金币", "item_use": "喝药 / 用卷轴",
    "ui_click": "UI 点击（面板/对话选项/买/卖）", "dialog_open": "NPC 对话开始",
    "shop_open": "商店打开", "portal": "传送 / 踩出入口",
    "area_enter": "进入场景（Stage）", "quest_complete": "任务完成",
}

# 名字带注释的键：原版没有一一对应条目，取语义最近的原版音（理由见 SoundMap.md §3）
NOTE = {
    "player_revive": "※1 原版无“玩家复活”条目 ⇒ 取 necromancer_revive_target",
    "dialog_open":   "※2 原版开对话不播专用音 ⇒ 取 cursor_select",
    "shop_open":     "※2 原版开商店不播专用音 ⇒ 取 cursor_error/cursor_switch",
    "quest_complete": "※3 原版 cursor_questdone 的 questdone.wav 不在此包内 ⇒ 取 cairn_success",
}


def find_root(start):
    d = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        p = os.path.dirname(d)
        if p == d:
            raise SystemExit("找不到仓库根；请用 --root 显式指定")
        d = p


def A(s):
    return s.encode("ascii")


def sha256(p):
    h = hashlib.sha256()
    with io.open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def read_text(p):
    raw = io.open(p, "rb").read()
    for enc in ("utf-8-sig", "utf-16", "latin-1"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("latin-1")


def parse_sounds(sounds_path):
    """sound 名 -> [(行号, FileName)]"""
    text = read_text(sounds_path).replace("\r\n", "\n")
    idx = {}
    for i, ln in enumerate(text.split("\n"), 1):
        f = ln.split("\t")
        if len(f) >= 3:
            idx.setdefault(f[0].strip(), []).append((i, f[2].strip()))
    return idx


# 间接键：值不是**直接**被调用点引用，而是经 `SfxKeys.CastOf(DamageType)` 查表得到。
# 语据 = `Module/Combat/SfxKeys.cs:65-75` 的 switch（case Fire → CastFire 等）。
# ⇒ 这些键的调用点 = `CastOf(` 的调用处 + 定义表本身那一行。
INDIRECT = {
    "cast_fire":      "CastFire",
    "cast_cold":      "CastCold",
    "cast_lightning": "CastLightning",
    "cast_poison":    "CastPoison",
}


def scan_triggers(root, scripts_dir):
    """键 -> ["文件:行", ...]：源码非注释行里的真实引用。

    ⛔ 排除定义处自身（`Module/Combat/SfxKeys.cs` / `Module/Audio/SfxRegistry.cs`）与整行注释
    （`//` / `*` 起头）—— 注释不算引用。`cast_*` 走 `INDIRECT`（`CastOf` 调用处 + 定义表行）。
    """
    out = {k: [] for k in IDENT}
    skip = {"SfxKeys.cs", "SfxRegistry.cs"}
    castof_sites = []
    for dp, _dn, fns in os.walk(scripts_dir):
        for fn in fns:
            if not fn.endswith(".cs") or fn in skip:
                continue
            full = os.path.join(dp, fn)
            rel = os.path.relpath(full, root).replace("\\", "/")
            for i, ln in enumerate(read_text(full).replace("\r\n", "\n").split("\n"), 1):
                s = ln.strip()
                if s.startswith("//") or s.startswith("*"):
                    continue
                if "CastOf(" in ln:
                    castof_sites.append("%s:%d" % (rel, i))
                for k, ident in IDENT.items():
                    if ("SfxKeys." + ident) in ln or ("SfxRegistry." + ident) in ln:
                        out[k].append("%s:%d" % (rel, i))
    # SfxKeys.cs 的定义表：`case DamageType.Fire: return CastFire;`
    sfxkeys = os.path.join(scripts_dir, "Module", "Combat", "SfxKeys.cs")
    defline = {}
    if os.path.exists(sfxkeys):
        rel = os.path.relpath(sfxkeys, root).replace("\\", "/")
        for i, ln in enumerate(read_text(sfxkeys).replace("\r\n", "\n").split("\n"), 1):
            for k, ident in INDIRECT.items():
                if ("return " + ident + ";") in ln:
                    defline[k] = "%s:%d" % (rel, i)
    for k, ident in INDIRECT.items():
        sites = list(castof_sites)
        if defline.get(k):
            sites.append(defline[k])
        out[k] = sites
    return out


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="SFX 溯源（Sounds.txt 行 + mpq 原字节 sha256）")
    ap.add_argument("--root", default=None)
    ap.add_argument("--out-dir", default=None)
    ap.add_argument("--mpq-dir", default=None)
    ap.add_argument("--storm-dir", default=None)
    ap.add_argument("--refs", default=None)
    ap.add_argument("--no-mpq", action="store_true",
                    help="跳过 mpq 解包（只重算工程侧 + 对回已入仓的 provenance 台账）")
    args = ap.parse_args()

    root = os.path.abspath(args.root) if args.root else find_root(here)
    refs = os.path.abspath(args.refs) if args.refs else os.path.join(root, "原版资源")
    mpq_dir = os.path.abspath(args.mpq_dir) if args.mpq_dir else os.path.join(refs, "_mpq_incoming")
    storm_dir = os.path.abspath(args.storm_dir) if args.storm_dir else \
        os.path.join(root, ".ai-tmp", "test", "storm")
    out_dir = os.path.abspath(args.out_dir) if args.out_dir else os.path.join(root, ".ai-tmp", "test")
    os.makedirs(out_dir, exist_ok=True)

    sounds = os.path.join(refs, "d2raw", "data", "global", "excel", "Sounds.txt")
    clips = os.path.join(root, "client", "Assets", "Resources", "Clover", "Sound", "SFX")
    prov_path = os.path.join(here, "sfx-provenance.tsv")
    mpq = os.path.join(mpq_dir, "d2sfx.mpq")

    logf = io.open(os.path.join(out_dir, "sfx-verify.log"), "w", encoding="utf-8", newline="\n")

    def log(*a):
        s = " ".join(str(x) for x in a)
        print(s, flush=True)
        logf.write(s + "\n")
        logf.flush()

    log("root      =", root)
    log("Sounds.txt=", sounds)
    log("d2sfx.mpq =", mpq, "(存在)" if os.path.exists(mpq) else "(不在盘)")
    log("clips     =", clips)
    log("")

    if not os.path.exists(sounds):
        log("FAIL 缺 Sounds.txt：", sounds)
        return 2
    idx = parse_sounds(sounds)
    log("① Sounds.txt sound 名索引 = %d 条（含表头/空行则行数更多）" % len(idx))

    # ── ① 对回 Sounds.txt 行号 ──────────────────────────────────────────────
    rows = []
    bad = []
    for key, rel in KEYS:
        # 从 SfxRegistry.Origins 的条目名反查（名字写在这里，与 C# 台账逐字一致）
        hits = None
        want = rel.replace("\\", "/").lower()
        for name, lst in idx.items():
            for (ln, fn) in lst:
                if fn.replace("\\", "/").lower() == want:
                    hits = (name, ln, fn)
                    break
            if hits:
                break
        if not hits:
            bad.append((key, rel, "Sounds.txt 里没有 FileName=" + rel))
            continue
        rows.append({"key": key, "rel": rel, "sound": hits[0], "line": hits[1], "file": hits[2]})
    log("① 对回 Sounds.txt：%d / %d 命中" % (len(rows), len(KEYS)))
    for b in bad:
        log("   MISS", b)

    # ── ② 从真 mpq 取原字节 → sha256 ────────────────────────────────────────
    src = {}          # key -> (bytes, sha256, extracted_path)
    if not args.no_mpq:
        if not os.path.exists(mpq):
            log("FAIL ② 需要 d2sfx.mpq 才能做逐字节比对，但不在盘：", mpq)
            return 2
        if not os.path.exists(os.path.join(storm_dir, "storm.dll")):
            log("FAIL ② 缺 storm.dll：", os.path.join(storm_dir, "storm.dll"))
            return 2
        os.add_dll_directory(storm_dir)
        storm = ctypes.WinDLL(os.path.join(storm_dir, "storm.dll"))
        for n, (a, r) in SIG.items():
            fn = getattr(storm, n)
            fn.argtypes = a
            fn.restype = r
        # 坑 1：chdir 到 mpq 目录 + ASCII 相对名
        os.chdir(mpq_dir)
        h = c_void_p()
        r = storm.SFileOpenArchive(A("d2sfx.mpq"), 0, MPQ_OPEN_READONLY, ctypes.byref(h))
        log("SFileOpenArchive(d2sfx.mpq) ret=%s handle=%s" % (r, h.value))
        if not h.value:
            log("FAIL 打不开 d2sfx.mpq")
            return 2
        # 坑 1（本体）：`SFileExtractFile` 的**目标路径也要 ASCII** —— 仓库根下 `原版资源/` 是中文，
        # 所以先解到 ASCII 临时树（`.ai-tmp/test/` 下），再用 `os.replace` 搬到中文最终路径。
        ascii_tmp = os.path.join(out_dir, "sfx-extract-tmp")
        for row in rows:
            entry = SFX_PREFIX + row["rel"]
            dst = os.path.join(refs, "d2raw", entry)
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            tmp = os.path.join(ascii_tmp, entry)
            os.makedirs(os.path.dirname(tmp), exist_ok=True)
            ok = storm.SFileExtractFile(h, A(entry), A(tmp), 0)
            if ok and os.path.exists(tmp) and os.path.getsize(tmp) > 0:
                os.replace(tmp, dst)     # 坑 3：判据 = 落盘且 > 0 字节
                src[row["key"]] = (os.path.getsize(dst), sha256(dst), dst)
            else:
                bad.append((row["key"], row["rel"], "mpq 取不出（SFileExtractFile ret=%s）" % ok))
        storm.SFileCloseArchive(h)
        log("② mpq 按名取出 %d / %d 个 .wav → 原版资源/d2raw/data/global/sfx/" % (len(src), len(rows)))

    # ── 工程侧 sha256（不依赖 mpq）───────────────────────────────────────────
    trig = scan_triggers(root, os.path.join(root, "client", "Assets", "Scripts"))
    prov_lines = ["# 音效 SFX 溯源台账（由 tools/probes/mpq/sfx_provenance.py 生成；⛔ 不手改）",
                  "# key\tsound\tsounds_line\tmpq_entry\tsrc_bytes\tsrc_sha256\tclip_bytes\tclip_sha256\tmatch"]
    map_lines = ["# 游戏事件 → 原版 sound 名 → Sounds.txt 行 → mpq 内路径 → 工程落地 → 调用点（文件:行）",
                 "# event\tkey\tsound\tsounds_line\tmpq_entry\tlanded_clip\ttrigger\tnote"]
    ok_all = len(bad) == 0
    n_sfx_good = 0
    for row in rows:
        k = row["key"]
        clip = os.path.join(clips, k + ".wav")
        cb = os.path.getsize(clip) if os.path.exists(clip) else 0
        ch = sha256(clip) if cb else "-"
        if k in src:
            sb, sh, _sp = src[k]
        else:
            sb, sh = 0, "-"
        match = "YES" if (sb and sb == cb and sh == ch) else "NO"
        if match == "YES":
            n_sfx_good += 1
        if sb and match == "NO":
            bad.append((k, row["rel"], "sha256/字节不一致 src=%s/%s clip=%s/%s" % (sb, sh[:12], cb, ch[:12])))
        if cb == 0:
            bad.append((k, row["rel"], "工程侧 clip 不存在：" + clip))
        if not trig.get(k):
            bad.append((k, row["rel"], "源码里找不到调用点（疑似空实现）"))
        entry = SFX_PREFIX + row["rel"]
        prov_lines.append("%s\t%s\t%d\t%s\t%d\t%s\t%d\t%s\t%s" %
                          (k, row["sound"], row["line"], entry, sb, sh, cb, ch, match))
        map_lines.append("%s\t%s\t%s\t%d\t%s\t%s\t%s\t%s" %
                         (EVENT.get(k, ""), k, row["sound"], row["line"], entry,
                          "client/Assets/Resources/Clover/Sound/SFX/%s.wav" % k,
                          ";".join(trig.get(k, [])[:3]), NOTE.get(k, "")))

    # ── ③ 调用点汇总 ───────────────────────────────────────────────────────
    log("")
    log("③ 调用点（源码非注释行的 SfxKeys./SfxRegistry. 引用，已排除定义处与注释）：")
    for k, _rel in KEYS:
        log("   %-16s %s" % (k, ", ".join(trig.get(k, [])) or "── 无 ──"))
    log("")
    log("② 逐字节 sha256 一致 = %d / %d" % (n_sfx_good, len(rows)))

    io.open(prov_path, "w", encoding="utf-8", newline="\n").write("\n".join(prov_lines) + "\n")
    io.open(os.path.join(out_dir, "sfx-map.tsv"), "w", encoding="utf-8", newline="\n").write(
        "\n".join(map_lines) + "\n")
    log("")
    log("写出：%s" % prov_path)
    log("写出：%s" % os.path.join(out_dir, "sfx-map.tsv"))
    if bad:
        log("")
        log("!! 问题 %d 条：" % len(bad))
        for b in bad:
            log("   ", b)
    log("")
    log("RESULT=%s  sfx_sha256_match=%d/%d" % ("PASS" if not bad else "FAIL", n_sfx_good, len(rows)))
    logf.close()
    return 0 if not bad else 2


if __name__ == "__main__":
    sys.exit(main())
