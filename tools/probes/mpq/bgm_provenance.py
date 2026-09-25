#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""BGM 溯源判据：把 `SfxRegistry.BgmFiles` 登记的 3 个 BGM 键

  ① 对回原版 `data/global/excel/Sounds.txt` 的**行号**（第 1 列 `Sound` / 第 3 列 `FileName`）；
  ② 从**真实 `d2music.mpq`** 按名 `SFileExtractFile` 取出原版 `.wav`，
     与工程侧 `client/Assets/Resources/Clover/Sound/BGM/<键>.wav` 做 **sha256 逐字节比对**；
  ③ 从源码里扫出每个键的**真实调用点** `文件:行`（非注释行），证明"有消费方、不是空实现"。

产物
  `tools/probes/mpq/bgm-provenance.tsv`  —— 入仓（② 的 sha256 台账；mpq 不在盘时宿主据此自检）
  `<out>/bgm-map.tsv`                     —— 任务书要的映射表（场景 → 原版 sound 名 → Sounds.txt 行 → mpq 路径 → 调用点）
  `<out>/bgm-verify.log`                  —— 原始输出

跑法（任意 cwd）
  python tools/probes/mpq/bgm_provenance.py --root <仓库根>
  python tools/probes/mpq/bgm_provenance.py --no-mpq     # 只重算工程侧 + 对回已入仓的台账

依赖（都不入仓，见 `README.md`）
  `原版资源/_mpq_incoming/D2music.mpq`（345,223,076 B；从 3DMGAME-DBL2.MPQ.file.rar 的
  range 250852627-594270093 取出并本地重建最小 RAR4 归档后解出，见 `原版资源/清单.md`）
  + `.ai-tmp/test/storm/storm.dll`。

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

MUS_PREFIX = "data\\global\\music\\"

# key -> (mpq 内相对路径去掉 data\global\music\ 前缀的部分, Sounds.txt 第 1 列条目名)
KEYS = [
    ("town",       r"act1\town1.wav", "music_town_1"),
    ("bloodmoor",  r"act1\wild.wav",  "music_wilderness"),
    ("denofevil",  r"act1\caves.wav", "music_caves"),
]

# key -> C# 常量标识符（SfxRegistry 里的名字；用于在源码里找真实调用点）
IDENT = {
    "town": "BgmTown",
    "bloodmoor": "BgmBloodMoor",
    "denofevil": "BgmDenOfEvil",
}

# 人类可读的"场景"（列进 bgm-map.tsv；措辞取自 `client/资源欠缺清单.md:46` 点名的三个区）
AREA = {
    "town": "罗格营地（Town，固定布局城镇）",
    "bloodmoor": "血腥荒野（BloodMoor，随机野外）",
    "denofevil": "邪恶洞穴（DenOfEvil，随机地牢）",
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
    """sound 名 -> [(行号, 第1列, FileName)]"""
    text = read_text(sounds_path).replace("\r\n", "\n")
    idx = {}
    for i, ln in enumerate(text.split("\n"), 1):
        f = ln.split("\t")
        if len(f) >= 3:
            idx.setdefault(f[0].strip(), []).append((i, f[0].strip(), f[2].strip()))
    return idx


# key -> (区域枚举名, 区域→键 分派 case 所在文件里的标识符)
AREA_CASE = {
    "town":      ("AreaId.Town", "BgmTown"),
    "bloodmoor": ("AreaId.BloodMoor", "BgmBloodMoor"),
    "denofevil": ("AreaId.DenOfEvil", "BgmDenOfEvil"),
}


def scan_triggers(root, scripts_dir):
    """键 -> ["文件:行", ...]：源码非注释行里的**真实委托链**。

    每个键的调用链有两段（都是唯一、可逐行核对的）：
      ① 键字面量唯一映射点 = `SfxRegistry.cs` 的 `case AreaId.<X>: return Bgm<Y>;`
         （区域 → 键；`SfxRegistry.BgmKeyOf` 的 switch 体）；
      ② 消费链 = `AudioHook.cs` 的 `PlayAreaBgm()` → `SfxRegistry.BgmKeyOf(_area)` → `_audio.Bgm(key)`。
    ⛔ 整行注释不算引用。
    """
    out = {k: [] for k in IDENT}
    hook = os.path.join(scripts_dir, "Module", "Audio", "AudioHook.cs")
    reg = os.path.join(scripts_dir, "Module", "Audio", "SfxRegistry.cs")

    def scan(path, pred):
        got = []
        if not os.path.exists(path):
            return got
        rel = os.path.relpath(path, root).replace("\\", "/")
        for i, ln in enumerate(read_text(path).replace("\r\n", "\n").split("\n"), 1):
            s = ln.strip()
            if s.startswith("//") or s.startswith("*"):
                continue
            if pred(ln):
                got.append("%s:%d" % (rel, i))
        return got

    # ② 消费链（三个键共用同一条链，逐行列出）
    chain = scan(hook, lambda ln: ("PlayAreaBgm()" in ln) or ("SfxRegistry.BgmKeyOf(" in ln)
                 or ("_audio.Bgm(key)" in ln))
    for k, (area_case, ident) in AREA_CASE.items():
        # ① 分派 case 行（键字面量的唯一映射点）：`case AreaId.Town: return BgmTown;`
        site = []
        if os.path.exists(reg):
            rel = os.path.relpath(reg, root).replace("\\", "/")
            for i, ln in enumerate(read_text(reg).replace("\r\n", "\n").split("\n"), 1):
                s = ln.strip()
                if s.startswith("//") or s.startswith("*"):
                    continue
                if ("case " + area_case + ":") in ln and ("return " + ident + ";") in ln:
                    site.append("%s:%d" % (rel, i))
        seen = []
        for x in site + chain:
            if x not in seen:
                seen.append(x)
        out[k] = seen
    return out


def load_committed_ledger(prov_path):
    """已入仓台账 → key -> (src_bytes, src_sha256)（--no-mpq 时用）。"""
    got = {}
    if not os.path.exists(prov_path):
        return got
    for ln in read_text(prov_path).replace("\r\n", "\n").split("\n"):
        if not ln or ln.startswith("#"):
            continue
        c = ln.split("\t")
        if len(c) >= 6:
            got[c[0]] = (int(c[4]), c[5])
    return got


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="BGM 溯源（Sounds.txt 行 + mpq 原字节 sha256）")
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
    clips = os.path.join(root, "client", "Assets", "Resources", "Clover", "Sound", "BGM")
    prov_path = os.path.join(here, "bgm-provenance.tsv")
    mpq = os.path.join(mpq_dir, "D2music.mpq")
    if not os.path.exists(mpq):
        alt = os.path.join(mpq_dir, "d2music.mpq")
        if os.path.exists(alt):
            mpq = alt

    logf = io.open(os.path.join(out_dir, "bgm-verify.log"), "w", encoding="utf-8", newline="\n")

    def log(*a):
        s = " ".join(str(x) for x in a)
        # 控制台是 GBK（Windows 默认）：非 cp936 字符（⇒ 之类）打不出来 ⇒ stdout 用 ASCII 安全版，
        # 日志文件仍写完整 UTF-8（证据看文件，不看控制台）。
        try:
            print(s, flush=True)
        except UnicodeEncodeError:
            enc = sys.stdout.encoding or "ascii"
            print(s.encode(enc, "replace").decode(enc, "replace"), flush=True)
        logf.write(s + "\n")
        logf.flush()

    log("root       =", root)
    log("Sounds.txt =", sounds)
    log("D2music.mpq=", mpq, "(存在)" if os.path.exists(mpq) else "(不在盘)")
    log("clips      =", clips)
    log("")

    if not os.path.exists(sounds):
        log("FAIL 缺 Sounds.txt：", sounds)
        return 2
    idx = parse_sounds(sounds)
    log("① Sounds.txt sound 名索引 = %d 条" % len(idx))

    # ── ① 对回 Sounds.txt 行号（按第 1 列条目名 + 第 3 列 FileName 双条件）────────
    rows = []
    bad = []
    for key, rel, snd in KEYS:
        hits = None
        for (ln, name, fn) in idx.get(snd, []):
            if fn.replace("\\", "/").lower() == rel.replace("\\", "/").lower():
                hits = (ln, name, fn)
                break
        if not hits:
            bad.append((key, rel, "Sounds.txt 里没有 条目=%s / FileName=%s 的行" % (snd, rel)))
            continue
        rows.append({"key": key, "rel": rel, "sound": hits[1], "line": hits[0], "file": hits[2]})
    log("① 对回 Sounds.txt：%d / %d 命中" % (len(rows), len(KEYS)))
    for b in bad:
        log("   MISS", b)

    # ── ② 从真 mpq 取原字节 → sha256 ────────────────────────────────────────
    src = {}
    if not args.no_mpq:
        if not os.path.exists(mpq):
            log("FAIL ② 需要 D2music.mpq 才能做逐字节比对，但不在盘：", mpq)
            log("     ⇒ 取回方式见 `原版资源/清单.md`（按下载 state 文件续传）")
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
        r = storm.SFileOpenArchive(A(os.path.basename(mpq)), 0, MPQ_OPEN_READONLY, ctypes.byref(h))
        log("SFileOpenArchive(%s) ret=%s handle=%s" % (os.path.basename(mpq), r, h.value))
        if not h.value:
            log("FAIL 打不开 D2music.mpq")
            return 2
        # 坑 1（本体）：目标路径也要 ASCII ⇒ 先解到 ASCII 临时树，再 os.replace 搬走
        ascii_tmp = os.path.join(out_dir, "bgm-extract-tmp")
        for row in rows:
            entry = MUS_PREFIX + row["rel"]
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
        log("② mpq 按名取出 %d / %d 个 .wav → 原版资源/d2raw/data/global/music/"
            % (len(src), len(rows)))
    else:
        src_tab = load_committed_ledger(prov_path)
        log("② --no-mpq：从已入仓台账读 mpq 侧 sha256（%d 条）" % len(src_tab))
        for row in rows:
            if row["key"] in src_tab:
                sb, sh = src_tab[row["key"]]
                src[row["key"]] = (sb, sh, "(ledger)")

    # ── 工程侧 sha256（不依赖 mpq）───────────────────────────────────────────
    trig = scan_triggers(root, os.path.join(root, "client", "Assets", "Scripts"))
    prov_lines = ["# BGM 溯源台账（由 tools/probes/mpq/bgm_provenance.py 生成；⛔ 不手改）",
                  "# key\tsound\tsounds_line\tmpq_entry\tsrc_bytes\tsrc_sha256\tclip_bytes\tclip_sha256\tmatch"]
    map_lines = ["# 场景 → 原版 sound 名 → Sounds.txt 行 → mpq 内路径 → 工程落地 → 调用点（文件:行）",
                 "# area\tkey\tsound\tsounds_line\tmpq_entry\tlanded_clip\ttrigger"]
    n_good = 0
    pending = []
    for row in rows:
        k = row["key"]
        clip = os.path.join(clips, k + ".wav")
        cb = os.path.getsize(clip) if os.path.exists(clip) else 0
        ch = sha256(clip) if cb else "-"
        if k in src:
            sb, sh, _sp = src[k]
        else:
            sb, sh = 0, "-"
            pending.append(k)          # mpq 侧未知 ⇒ 这一行**未判定**（不得算绿）
        match = "YES" if (sb and sb == cb and sh == ch) else ("PENDING" if sb == 0 else "NO")
        if match == "YES":
            n_good += 1
        if sb and match == "NO":
            bad.append((k, row["rel"], "sha256/字节不一致 src=%s/%s clip=%s/%s"
                        % (sb, sh[:12], cb, ch[:12])))
        if cb == 0:
            bad.append((k, row["rel"], "工程侧 clip 不存在：" + clip))
        if not trig.get(k):
            bad.append((k, row["rel"], "源码里找不到调用点（疑似空实现）"))
        entry = MUS_PREFIX + row["rel"]
        prov_lines.append("%s\t%s\t%d\t%s\t%d\t%s\t%d\t%s\t%s" %
                          (k, row["sound"], row["line"], entry, sb, sh, cb, ch, match))
        map_lines.append("%s\t%s\t%s\t%d\t%s\t%s\t%s" %
                         (AREA.get(k, ""), k, row["sound"], row["line"], entry,
                          "client/Assets/Resources/Clover/Sound/BGM/%s.wav" % k,
                          ";".join(trig.get(k, [])[:3])))

    log("")
    log("③ 调用点（源码非注释行的 SfxRegistry.Bgm* / BgmKeyOf / AudioHook.PlayAreaBgm 引用）：")
    for k, _r, _s in KEYS:
        log("   %-12s %s" % (k, ", ".join(trig.get(k, [])) or "── 无 ──"))
    log("")
    log("② 逐字节 sha256 一致 = %d / %d（未判定 = %s）"
        % (n_good, len(rows), ",".join(pending) or "无"))

    # 台账只在**真的做过 mpq 侧比对**（无 pending）时写：否则会留下一份"看似有台账、
    #    其实 src 侧是 0"的文件，下游宿主会误以为溯源已完成。
    if pending:
        log("（pending ⇒ 不写 %s：mpq 侧未判定时留下的台账会误导下游）" % prov_path)
    else:
        io.open(prov_path, "w", encoding="utf-8", newline="\n").write("\n".join(prov_lines) + "\n")
    io.open(os.path.join(out_dir, "bgm-map.tsv"), "w", encoding="utf-8", newline="\n").write(
        "\n".join(map_lines) + "\n")
    log("")
    log("写出：%s（pending 时跳过）" % prov_path)
    log("写出：%s" % os.path.join(out_dir, "bgm-map.tsv"))
    if bad:
        log("")
        log("!! 问题 %d 条：" % len(bad))
        for b in bad:
            log("   ", b)
    log("")
    if bad:
        status, code = "FAIL", 2
    elif pending:
        # 不许把"mpq 侧未知"算成绿：这是 PENDING（未判定），不是 PASS。
        status, code = "PENDING", 3
    else:
        status, code = "PASS", 0
    log("RESULT=%s  bgm_sha256_match=%d/%d  pending=%s"
        % (status, n_good, len(rows), ",".join(pending) or "无"))
    if code == 3:
        log("PENDING 说明：d2music.mpq 未在本机 ⇒ ② 的 mpq 侧字节未判定；"
            "取回后重跑本脚本（不带 --no-mpq）即转 PASS。")
    logf.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
