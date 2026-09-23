#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""按名从原版 MPQ 取**调色板**（`.PL2`）到 `<原版资源>/d2raw/data/global/palette/<套名>/Pal.PL2`。

来历：2026-09-24 片 `dialog-options2` —— 判 `D2/UI/Menu/btn_med_sel.png`（原版
`FrontEnd/MediumSelButtonBlank.dc6` 帧 0）为什么是**麻点图**时，发现工程只解过
ACT1 / EndGame / loading / fechar 四套调色板，**没有 `menu1`**（原版 `data/global/ui/FrontEnd/**`
按惯例该用 `menu1`）⇒ 用错调色板正是"长麻点"的成因（见 `tools/d2codec/export_d2ui.py` 文件头
「背景图是 dithered 的，用错调色板不是染色而是长麻点」）。本脚本把这个取件动作固化成**可复跑判据资产**。

跑法（任意 cwd；`storm.dll` 与 MPQ 见 `tools/probes/mpq/README.md`，两者都**不入仓**）
  # ① 探名（只打印候选的字节数，不写盘）—— MPQ 的名字大小写/拼法不确定，先探
  python tools/probes/measure/extract_palette_from_mpq.py --probe menu1
  # ② 取件（写成对的那个名字）
  python tools/probes/measure/extract_palette_from_mpq.py --set menu1 --name data/global/palette/menu1/Pal.PL2

判据（⛔ 不看"脚本说成功"，看产物）
  · 产物落盘且 `> 0` 字节（这份 storm.dll 的 `SFileHasFile` **不可信**，见 storm.py 坑 3）；
  · `len == 3*256 + 256`（PL2 的常规结构：3 张 256 色表 + 256 项索引表 ⇒ 443392 B 附近；
    实测本工程已有的 ACT1/EndGame/fechar 都是 **443175**、loading 是 **442916**，
     ⇒ 同族量级（40 万级）即可，别拿精确字节数当判据）；
  · 能被 `tools/d2codec/dc6.py` 的 `read_pl2()` 读成 ≥ 256 项；
  · 打印 sha256 ⇒ 与上一轮比对，**同一份件必须同哈希**（防"解到别的东西"）。

⛔ 不 `os.remove` / 不 `rmtree`（宿主 safe-delete 守门会拦批量删除，见 extract_wanted.py 坑 4）；
   覆盖一律 `os.replace`（Windows = MoveFileEx + REPLACE_EXISTING）。
"""
import argparse
import hashlib
import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.abspath(os.path.join(_HERE, "..", "..", ".."))
sys.path.insert(0, os.path.join(_REPO, "tools", "d2codec"))
sys.path.insert(0, os.path.join(_REPO, "tools", "probes", "mpq"))

import storm  # noqa: E402  (tools/d2codec/storm.py)

MPQ_DIR = os.path.join(_REPO, "原版资源", "_mpq_incoming")
PAL_ROOT = os.path.join(_REPO, "原版资源", "d2raw", "data", "global", "palette")

#: 候选名字：目录名/文件名大小写各试一遍（StormLib 的 hash 查名**不**区分大小写，
#: 但本机这份 DLL 的 `SFileOpenFileEx` 是坏的、只有 `SFileExtractFile` 可用
#: ⇒ 实测大小写照抄原件最稳，故两种都试）。
CASES = ["Pal.PL2", "Pal.pl2"]


def candidates(dir_name):
    out = []
    for d in (dir_name, dir_name.upper(), dir_name.capitalize()):
        for f in CASES:
            out.append("data/global/palette/%s/%s" % (d, f))
    return out


def open_mpq():
    """打开 `D2data.mpq` 并挂 `Patch_D2.mpq`（补丁覆盖优先，与 extract_wanted.py 同口径）。"""
    h = storm.open_archive(os.path.join(MPQ_DIR, "D2data.mpq"), patch="Patch_D2.mpq")
    return h


def probe(dir_name, h):
    print("── 探名 %s（只打印，不写盘）──" % dir_name)
    hit = None
    for name in candidates(dir_name):
        raw = storm.read_file(h, name)
        n = len(raw) if raw else 0
        print("   %-46s %8d B  %s" % (name, n, "OK" if n else "-"))
        if n and hit is None:
            hit = (name, raw)
    return hit


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--probe", metavar="SET", help="只探名（打印候选字节数）")
    ap.add_argument("--set", dest="aset", metavar="SET", help="取件到 d2raw 的目标套名（目录名）")
    ap.add_argument("--name", help="要取的原件名（--probe 探出来的那个）")
    ap.add_argument("--out", help="覆盖落点（默认 <原版资源>/d2raw/data/global/palette/<SET>/Pal.PL2）")
    ap.add_argument("--work-dir", default=os.path.join(_REPO, ".ai-tmp", "test", "storm-tmp"))
    args = ap.parse_args()

    storm.set_work_dir(args.work_dir)
    h = open_mpq()
    try:
        if args.probe:
            hit = probe(args.probe, h)
            if not hit:
                print("[NG] 候选全落空 ⇒ 这套调色板不在这两个包里（或缺更长的候选表）")
                return 3
            print("[OK] 命中 %s（%d B）" % (hit[0], len(hit[1])))
            return 0

        if not (args.aset and args.name):
            print("用法见文件头（--probe / --set 二选一）")
            return 2

        raw = storm.read_file(h, args.name)
        if not raw:
            print("[NG] 读回 0 字节 ⇒ 视为不存在（这份 DLL 的 SFileHasFile 不可信）")
            return 3

        out = args.out or os.path.join(PAL_ROOT, args.aset, "Pal.PL2")
        os.makedirs(os.path.dirname(out), exist_ok=True)
        tmp = out + ".incoming"
        with open(tmp, "wb") as fh:
            fh.write(raw)
        os.replace(tmp, out)          # ⛔ 不用 os.remove（safe-delete 守门）

        sha = hashlib.sha256(raw).hexdigest()
        print("落点 %s" % out)
        print("字节 %d" % len(raw))
        print("sha256 %s" % sha)

        # 判据：能被 dc6.read_pl2 读成 >= 256 项
        try:
            import dc6
            pal = dc6.read_pl2(out)
            print("PL2 项数 %d" % len(pal))
            if len(pal) < 256:
                print("[NG] 项数 < 256 ⇒ 解出来的不是调色板")
                return 4
            print("前 4 项（索引 0..3）= %s" % (pal[:4],))
        except Exception as ex:                                    # noqa: BLE001
            print("[NG] read_pl2 失败：%r" % (ex,))
            return 4
        return 0
    finally:
        storm.close_archive(h)


if __name__ == "__main__":
    sys.exit(main())
