# -*- coding: utf-8 -*-
"""`export_d2ui.py` 落位素材的**独立复算自证**（片 1 新增；可反复跑）。

判据（两条，都必须是"全等"）：
  A. **逐帧 RGBA 逐字节**：工程内 PNG 解出来的 RGBA，必须与「源 DC6 那一帧 + 该组 PL2」算出的 RGBA 完全相同；
  B. **`dc6.py png` 独立复算 + SHA256**：另起一个进程跑
     `python tools/d2codec/dc6.py png <源> <pl2> <临时目录> <stem>`，
     把它写出的文件与工程内同名文件做 **SHA256 比对**（文件级、逐字节）。
     —— 这是"独立路径复算"，不是拿同一个函数再算一遍（§1.12 第 4 条：基准不许来自我们自己的产物）。

`chifont` 组的产物是**整幅图集**（每张 13806 帧），所以：
  · A 条对**全部** 13806 帧逐帧做（在内存里按格子切出来比，不落盘）；
  · B 条按**确定性抽样**（每字体最多 64 帧：前 16 / 均匀 / 后 16 / 固定线性同余伪随机）做文件级 SHA256，
    抽样位置由脚本算死（可复算），不是"随手挑看着对的"。

用法：
  python tools/d2codec/verify_d2ui_export.py <d2dc6 根> <工程 client 目录> [--only <组名逗号分隔>]

退出码：0 = 全等；1 = 有不一致（逐条打印）。临时文件只落 `<项目根>/.ai-tmp/test/`，跑完即删。
"""

import hashlib
import os
import shutil
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dc6               # noqa: E402
import export_d2ui as ex  # noqa: E402

SAMPLE = 64  # chifont 每字体的抽样帧数上限


def _sha256(path):
    with open(path, "rb") as f:
        return hashlib.sha256(f.read()).hexdigest()


def collect(only):
    """把 `export_d2ui` 的组函数跑一遍，但**不写盘**（拦掉 `one()` / `write_fontmap_tsv()`），
    只记录它打算写什么。这样自证脚本与生成器**永远同源**，不会两边漂移。"""
    records = []
    state = {"src": None, "palpath": {}}
    real_read, real_one = ex.read_dc6, ex.one
    real_pl2, real_tsv = dc6.read_pl2, ex.write_fontmap_tsv

    def spy_read(*parts):
        d = real_read(*parts)
        if d is not None:
            state["src"] = os.path.join(ex.SRC_ROOT, *parts)
        return d

    def spy_pl2(path):
        pal = real_pl2(path)
        state["palpath"][id(pal)] = path
        return pal

    def spy_one(frame, out_path, palette, note="", group=""):
        records.append({"group": group, "src": state["src"], "dst": out_path,
                        "frame": frame, "pal": palette,
                        "pl2": state["palpath"].get(id(palette)), "note": note})

    ex.read_dc6, ex.one, dc6.read_pl2, ex.write_fontmap_tsv = \
        spy_read, spy_one, spy_pl2, (lambda *a, **k: None)
    argv = sys.argv
    try:
        sys.argv = ["verify", ex.SRC_ROOT, ex.DST_ROOT] + (["--only", only] if only else [])
        ex.main()
    finally:
        sys.argv = argv
        ex.read_dc6, ex.one, dc6.read_pl2, ex.write_fontmap_tsv = \
            real_read, real_one, real_pl2, real_tsv
    return records


def sample_indices(n):
    """确定性抽样位置（可复算）。"""
    idx = set(range(min(16, n)))
    idx |= set(range(max(0, n - 16), n))
    idx |= set(range(0, n, max(1, n // 16)))
    x = 12345
    while len(idx) < min(SAMPLE, n):
        x = (1103515245 * x + 12345) % 2147483648
        idx.add(x % n)
    return sorted(i for i in idx if i < n)


def check_frame_png(rec):
    """A 条（单帧产物）：工程内 PNG 的 RGBA vs 「源帧 + PL2」的 RGBA。"""
    w, h, got = dc6._read_png_rgba(rec["dst"])
    f = rec["frame"]
    if (w, h) != (f.width, f.height):
        return False, "尺寸不符：PNG %dx%d vs 帧 %dx%d" % (w, h, f.width, f.height)
    if got != bytes(dc6.frame_rgba(f, rec["pal"])):
        return False, "像素不一致"
    return True, ""


def check_atlas_png(rec, workdir):
    """chifont（整幅图集）：A 条全帧 + B 条抽样独立复算。"""
    aw, ah, atlas = dc6._read_png_rgba(rec["dst"])
    frames = dc6.parse(open(rec["src"], "rb").read()).frames
    n = len(frames)
    cell_w = max(f.width for f in frames)
    cell_h = max(f.height for f in frames)
    cols = ex.atlas_cols(n, cell_w)
    rows = (n + cols - 1) // cols
    bad = []
    if (aw, ah) != (cols * cell_w, rows * cell_h):
        bad.append("图集尺寸 %dx%d ≠ 列%d×格%d / 行%d×格%d" % (aw, ah, cols, cell_w, rows, cell_h))
        return bad, 0, n

    stride = aw * 4
    for i, f in enumerate(frames):
        if (f.width, f.height) != (cell_w, cell_h):
            bad.append("帧 %d 尺寸 %dx%d ≠ 格 %dx%d" % (i, f.width, f.height, cell_w, cell_h))
            continue
        ox, oy = (i % cols) * cell_w, (i // cols) * cell_h
        got = bytearray()
        for y in range(cell_h):
            s = (oy + y) * stride + ox * 4
            got += atlas[s:s + cell_w * 4]
        if bytes(got) != bytes(dc6.frame_rgba(f, rec["pal"])):
            bad.append("帧 %d（格 %d,%d）像素不一致" % (i, i % cols, i // cols))

    tmp = tempfile.mkdtemp(prefix="d2chi_", dir=workdir)
    stem = "chi" + os.path.basename(rec["src"]).lower().replace(".dc6", "")
    subprocess.check_call([sys.executable, os.path.join(HERE, "dc6.py"), "png",
                           rec["src"], rec["pl2"], tmp, stem], stdout=subprocess.DEVNULL)
    checked = 0
    for i in sample_indices(n):
        ox, oy = (i % cols) * cell_w, (i // cols) * cell_h
        got = bytearray()
        for y in range(cell_h):
            s = (oy + y) * stride + ox * 4
            got += atlas[s:s + cell_w * 4]
        mine = os.path.join(tmp, "%s_tile_%d.png" % (stem, i))
        dc6.write_png_rgba(mine, bytes(got), cell_w, cell_h)
        if _sha256(mine) != _sha256(os.path.join(tmp, "%s_%d.png" % (stem, i))):
            bad.append("帧 %d：图集格子 vs `dc6.py png` 复算 SHA256 不同" % i)
        checked += 1
    shutil.rmtree(tmp, ignore_errors=True)
    return bad, checked, n


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return 1
    ex.SRC_ROOT = os.path.abspath(sys.argv[1])
    ex.DST_ROOT = os.path.abspath(sys.argv[2])
    ex.RAW_ROOT = os.path.join(os.path.dirname(ex.SRC_ROOT), "d2raw")
    ex.PL2_ROOT = os.path.join(ex.RAW_ROOT, "data", "global", "palette")
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None

    proj = os.path.dirname(os.path.dirname(ex.SRC_ROOT))       # <项目根>
    work = os.path.join(proj, ".ai-tmp", "test", "verify_tmp")
    os.makedirs(work, exist_ok=True)
    t0 = time.time()

    print("== 收集生成清单（不写盘）==")
    records = collect(only)
    print("   共 %d 条产物" % len(records))

    groups = {}
    fail = 0
    print("\n== A 条：逐帧 RGBA 逐字节比对 ==")
    for rec in records:
        st = groups.setdefault(rec["group"], {"files": 0, "frames": 0, "bad": [], "cli": 0})
        st["files"] += 1
        if rec["dst"].endswith("_chi.png"):
            bad, checked, n = check_atlas_png(rec, work)
            st["frames"] += n
            st["cli"] += checked
        else:
            ok, why = check_frame_png(rec)
            st["frames"] += 1
            bad = [] if ok else ["%s：%s" % (os.path.basename(rec["dst"]), why)]
        st["bad"] += bad
    for g in sorted(groups):
        st = groups[g]
        print("  %-10s 产物 %4d 个 / 帧 %6d  不一致 %d" % (g, st["files"], st["frames"], len(st["bad"])))
        for b in st["bad"][:10]:
            print("      x %s" % b)
        if st["bad"]:
            fail = 1

    print("\n== B 条：`dc6.py png` 独立进程复算 + 文件级 SHA256 ==")
    for g in sorted(groups):
        srcs = {}
        for rec in records:
            if rec["group"] == g and not rec["dst"].endswith("_chi.png"):
                srcs.setdefault((rec["src"], rec["pl2"]), []).append(rec)
        eq = ne = 0
        for (src, pl2), recs in sorted(srcs.items()):
            stem = os.path.basename(recs[0]["dst"])
            stem = stem[:stem.rfind("_")]
            for i, rec in enumerate(recs):
                if os.path.basename(rec["dst"]) != "%s_%d.png" % (stem, i):
                    print("      x 输出名与帧号不符：%s（期望 %s_%d）" % (rec["dst"], stem, i))
                    fail = 1
            out = os.path.join(work, g, stem)
            subprocess.check_call([sys.executable, os.path.join(HERE, "dc6.py"), "png",
                                   src, pl2, out, stem], stdout=subprocess.DEVNULL)
            for i, rec in enumerate(recs):
                if _sha256(os.path.join(out, "%s_%d.png" % (stem, i))) == _sha256(rec["dst"]):
                    eq += 1
                else:
                    ne += 1
                    print("      x SHA256 不同：%s" % rec["dst"])
                    fail = 1
        if eq or ne:
            print("  %-10s 帧 %4d  SHA256 全等 %4d  不等 %d" % (g, eq + ne, eq, ne))
    if "chifont" in groups:
        print("  %-10s 图集 4 张 / 帧 %d（全帧内存比对）；`dc6.py png` 抽样复算 SHA256 比对 %d 帧"
              % ("chifont", groups["chifont"]["frames"], groups["chifont"]["cli"]))

    shutil.rmtree(work, ignore_errors=True)
    print("\n%s（用时 %.1fs）" % ("PASS：全部全等" if not fail else "FAIL：存在不一致", time.time() - t0))
    return fail


if __name__ == "__main__":
    sys.exit(main())
