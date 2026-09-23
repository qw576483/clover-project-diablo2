#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按名解包 mpq（照 `wanted.txt` 逐条 `SFileHasFile` + `SFileExtractFile`）。

来历：2026-09-22 `mpq-unpack2` 片的一次性脚本 `.ai-tmp/test/extract_wanted.py`；
2026-09-22 `g1-naming-and-mpq-asset` 片提升为**仓内判据资产**（skill §3.5），
并把写死的 `c:\\Work\\Server\\f-v2\\…` 换成「从脚本位置向上找仓库根 / 命令行参数」。

⛔ 四个坑（勿犯；前两条是 StormLib 的，后两条是这台宿主 + 这份 DLL 的）

  1. **`storm.dll` 是 ANSI 接口** ⇒ 调它之前先 `os.chdir(<mpq 目录>)`，并且**只用 ASCII 相对名**
     传给它（`SFileOpenArchive("D2data.mpq")` 这种）。⛔ 不许把带中文的绝对路径交给它
     （本仓库根 `…/f-v2/clover-project-diablo2` 恰好全 ASCII，但**别依赖这一点**）。
  **2. ⛔ 不许 `SFileFindFirstFile` 全量枚举**：实测在这份 `storm.dll` 上会吃到 **8+ GB 内存且不返回**
     （产出文件 0 字节）。**按索引也拿不到名字**（`SFileFindNextFile` 的 `cFileName` 对解出的条目
     不可靠）⇒ 唯一可行路线 = **按名** `SFileExtractFile`，所以按名清单 `wanted.txt` 是前置条件。
  3. **`SFileHasFile` 在这份 DLL 上不可信**：对绝不可能存在的名字也返回**非 0**
     （实测 `-803602432` / `-803602431`）⇒ 它**不能**当"包里有这个文件"的判据。
     本脚本因此**一律以「产物落盘且 > 0 字节」为准**（`SFileHasFile` 只用来打提示）。
  4. **宿主的 safe-delete 守门**：同一轮累计 **> 500 次删除**会被拦下并让脚本崩
     （`[safe-delete][SAFE_DELETE_BULK_CONFIRM_REQUIRED] count=500`）⇒
     ⛔ 不许 `os.remove` 目标 / `rmtree` 临时树；覆盖落位一律用 **`os.replace`**
     （Windows = `MoveFileEx + REPLACE_EXISTING`，不产生 unlink）。

落点契约（`route()`；⛔ 三个子树各装什么是有理由的，换布局 = 让下游生成器全部找不到文件）
  `原版资源/d2dc6/<原名>`      —— 所有 `.dc6`（`data/global/ui/**`、`data/LOCAL/UI/chi/**`、
                                 `data/LOCAL/FONT/chi/**`、`data/global/items/**`）。
                                 理由：这一层是**逐帧图**的来源，只给 `export_d2ui.py` 读。
  `原版资源/d2raw/<原名>`      —— `data/global/{tiles,palette,excel}/**` **和** `data/local/font/chi/*.tbl`。
                                 理由：这一层是**原始二进制**（`.dt1`/`.ds1`/`.pl2`/`.txt`/字体步进表），
                                 只给 `export_tiles.py` / `export_wild_layout.py` / 表转换读，不产出图。
  `原版资源/d2text/_src/<原名>` —— 其余 `data/local/**`（串表 `.tbl`）。
                                 理由：串表要经 `tools/d2codec/tbl.py` 再生成派生的
                                 `原版资源/d2text/chi_string.txt`（`uicheck` 的判据源）⇒
                                 源码件放 `_src/` 与派生件分层，避免混在一起。
  其它（不在上面三类）⇒ 兜底进 `d2raw/`。

产物（默认写 `<root>/.ai-tmp/test/`）
  `<tag>.log`（进度 + 汇总）/ `<tag>-missing.txt`（包内没有该名字的清单）
  `<tag>-heartbeat.txt`（心跳；长跑时先落盘再继续）

跑法（任意 cwd 都可）
  python tools/probes/mpq/extract_wanted.py --tag unpack-g1
  # 参数：--root <仓库根> / --mpq-dir <含 mpq 的目录> / --storm-dir <含 storm.dll 的目录>
  #       --refs <原版资源> / --wanted <清单> / --out-dir <产物目录> / --tag <前缀>

依赖：Python 3.12 + `storm.dll`（见 `README.md`：**不入仓**，下载到 `.ai-tmp/test/storm/`）。
      `原版资源/_mpq_incoming/` 下的 `D2data.mpq` + `Patch_D2.mpq`（同样不入仓）。
"""
import argparse
import ctypes
import io
import os
import sys
import time
from ctypes import c_char_p, c_int, c_uint32, c_void_p, POINTER, Structure

SIG = {
    "SFileOpenArchive": ([c_char_p, c_uint32, c_uint32, POINTER(c_void_p)], c_int),
    "SFileOpenPatchArchive": ([c_void_p, c_char_p, c_char_p, c_uint32], c_int),
    "SFileHasFile": ([c_void_p, c_char_p], c_int),
    "SFileExtractFile": ([c_void_p, c_char_p, c_char_p, c_uint32], c_int),
    "SFileCloseArchive": ([c_void_p], c_int),
}
MPQ_OPEN_READONLY = 0x0100


def find_root(start):
    """从 start 逐级向上找「含 client/Assets 的那一层」= 仓库根。"""
    d = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(d, "client", "Assets")):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            raise SystemExit("找不到仓库根（向上一直没看到 client/Assets）；请用 --root 显式指定")
        d = parent


def A(s):
    """ANSI 接口只吃字节；⛔ 传进来的必须是 ASCII（坑 1）。"""
    return s.encode("ascii")


class Log(object):
    def __init__(self, path, heart):
        self.f = io.open(path, "w", encoding="utf-8", newline="\n")
        self.heart = heart

    def __call__(self, *a):
        s = " ".join(str(x) for x in a)
        print(s, flush=True)
        self.f.write(s + "\n")
        self.f.flush()

    def beat(self, step, n):
        with io.open(self.heart, "w", encoding="utf-8", newline="\n") as h:
            h.write("time=%s\nstep=%s\nextracted_files=%d\n"
                    % (time.strftime("%Y-%m-%d %H:%M:%S"), step, n))


def route(refs, name):
    """按落点契约把 mpq 内相对名路由到 `原版资源/` 下的子树。"""
    low = name.lower()
    if low.endswith(".dc6"):
        return os.path.join(refs, "d2dc6", name)
    if low.startswith("data\\local\\font\\chi\\") and low.endswith(".tbl"):
        return os.path.join(refs, "d2raw", name)
    if low.startswith("data\\global\\tiles\\") or low.startswith("data\\global\\palette\\") \
            or low.startswith("data\\global\\excel\\"):
        return os.path.join(refs, "d2raw", name)
    if low.startswith("data\\local\\"):
        return os.path.join(refs, "d2text", "_src", name)
    return os.path.join(refs, "d2raw", name)


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="按名解包 mpq（StormLib / ctypes）")
    ap.add_argument("--root", default=None, help="仓库根（默认从脚本位置向上找）")
    ap.add_argument("--mpq-dir", default=None, help="含 mpq 的目录（默认 <root>/原版资源/_mpq_incoming）")
    ap.add_argument("--storm-dir", default=None, help="含 storm.dll 的目录（默认 <root>/.ai-tmp/test/storm）")
    ap.add_argument("--refs", default=None, help="解包落点根（默认 <root>/原版资源）")
    ap.add_argument("--wanted", default=None, help="按名清单（默认 <out-dir>/wanted.txt）")
    ap.add_argument("--out-dir", default=None, help="产物目录（默认 <root>/.ai-tmp/test）")
    ap.add_argument("--tag", default="unpack", help="产物文件名前缀（log / -missing.txt / -heartbeat.txt）")
    ap.add_argument("--base-archive", default="D2data.mpq", help="主归档名（相对 --mpq-dir）")
    ap.add_argument("--patch-archive", default="Patch_D2.mpq", help="补丁归档名（相对 --mpq-dir）")
    args = ap.parse_args()

    root = os.path.abspath(args.root) if args.root else find_root(here)
    refs = os.path.abspath(args.refs) if args.refs else os.path.join(root, "原版资源")
    mpq_dir = os.path.abspath(args.mpq_dir) if args.mpq_dir else os.path.join(refs, "_mpq_incoming")
    storm_dir = os.path.abspath(args.storm_dir) if args.storm_dir else \
        os.path.join(root, ".ai-tmp", "test", "storm")
    out_dir = os.path.abspath(args.out_dir) if args.out_dir else os.path.join(root, ".ai-tmp", "test")
    wanted = os.path.abspath(args.wanted) if args.wanted else os.path.join(out_dir, "wanted.txt")
    os.makedirs(out_dir, exist_ok=True)

    log = Log(os.path.join(out_dir, args.tag + ".log"),
              os.path.join(out_dir, args.tag + "-heartbeat.txt"))
    tmp = os.path.join(out_dir, "unpack-tmp")
    log("root     =", root)
    log("mpq-dir  =", mpq_dir)
    log("storm    =", os.path.join(storm_dir, "storm.dll"))
    log("wanted   =", wanted)
    log("refs     =", refs)

    for p in (os.path.join(storm_dir, "storm.dll"), wanted,
              os.path.join(mpq_dir, args.base_archive)):
        if not os.path.exists(p):
            log("FAIL 缺输入：", p)
            return 2

    os.add_dll_directory(storm_dir)
    storm = ctypes.WinDLL(os.path.join(storm_dir, "storm.dll"))
    for n, (a, r) in SIG.items():
        fn = getattr(storm, n)
        fn.argtypes = a
        fn.restype = r

    names = [ln.strip() for ln in io.open(wanted, encoding="utf-8") if ln.strip()]
    log("wanted = %d 条" % len(names))
    log.beat("open archives", 0)

    # 坑 1：先 chdir 到 mpq 目录，之后只用 ASCII 相对名调 DLL
    os.chdir(mpq_dir)
    h = c_void_p()
    r = storm.SFileOpenArchive(A(args.base_archive), 0, MPQ_OPEN_READONLY, ctypes.byref(h))
    log("SFileOpenArchive(%s) ret=%s handle=%s" % (args.base_archive, r, h.value))
    if not h.value:
        log("FAIL 打不开", args.base_archive)
        return 2
    pr = storm.SFileOpenPatchArchive(h, A(args.patch_archive), b"", 0)
    log("SFileOpenPatchArchive(%s) = %s" % (args.patch_archive, bool(pr)))

    # 坑 4：⛔ 不 rmtree(tmp)（safe-delete 守门）—— 直接复用/覆盖
    os.makedirs(tmp, exist_ok=True)

    done = fail = miss = 0
    bytes_out = 0
    t0 = time.time()
    missing, fails = [], []
    for i, n in enumerate(names):
        # 坑 3：SFileHasFile 返回值不可信 ⇒ 只当提示，判据永远是"产物落盘且 > 0 字节"
        has = bool(storm.SFileHasFile(h, A(n)))
        dst = os.path.join(tmp, n)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        ok = storm.SFileExtractFile(h, A(n), A(dst), 0)
        if ok and os.path.exists(dst) and os.path.getsize(dst) > 0:
            done += 1
            bytes_out += os.path.getsize(dst)
            final = route(refs, n)
            os.makedirs(os.path.dirname(final), exist_ok=True)
            os.replace(dst, final)          # 坑 4：覆盖落位禁用 os.remove / shutil.move
        else:
            fail += 1
            if not has:
                miss += 1
            fails.append((n, "SFileExtractFile ret=%s size=%s hasFile=%s"
                          % (ok, os.path.getsize(dst) if os.path.exists(dst) else "无文件", has)))
        if (i + 1) % 25 == 0 or i == len(names) - 1:
            log("  进度 %4d/%d  成功=%d 失败=%d（其中 hasFile=False %d）  %.1f MB  %.1fs"
                % (i + 1, len(names), done, fail, miss, bytes_out / 1048576.0, time.time() - t0))
            log.beat("extract %d/%d" % (i + 1, len(names)), done)

    storm.SFileCloseArchive(h)

    with io.open(os.path.join(out_dir, args.tag + "-missing.txt"), "w",
                 encoding="utf-8", newline="\n") as f:
        f.write("# 包内没有该名字（判据 = SFileExtractFile 后产物未落盘/为 0 字节；"
                "SFileHasFile 只作提示 —— 见脚本头 坑 3）\n")
        for n, why in fails:
            f.write("%s\t%s\n" % (n, why))

    log("")
    log("解包成功 = %d ；失败 = %d （其中 hasFile=False = %d）；总字节 = %d（%.1f MB）；耗时 %.1fs"
        % (done, fail, miss, bytes_out, bytes_out / 1048576.0, time.time() - t0))
    for pack in ("d2dc6", "d2raw", "d2text"):
        p = os.path.join(refs, pack)
        cnt = sum(len(fs) for _, _, fs in os.walk(p)) if os.path.isdir(p) else 0
        sz = sum(os.path.getsize(os.path.join(dp, f)) for dp, _, fs in os.walk(p) for f in fs) \
            if os.path.isdir(p) else 0
        log("  %s：%d 个文件 / %.1f MB" % (pack, cnt, sz / 1048576.0))
    log.beat("done", done)
    log.f.close()
    return 0 if fail == 0 else 2


if __name__ == "__main__":
    sys.exit(main())
