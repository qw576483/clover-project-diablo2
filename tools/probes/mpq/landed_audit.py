#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""落地核对（只读）：`wanted.txt` 的按名清单 vs 盘上真正落地的产物。

为什么要它：`route()` 把**只差大小写**的同名文件映射到**同一个目标路径**
（Windows 不区分大小写），清单里又存在若干这类重复条目 ⇒
「解包日志里的成功计数」≠「盘上文件数」。本脚本把差额逐条算清，避免在文档里写错数。
⛔ 它也是 G-1 的判据工具：**缺名数**（`exists=0` 的行）与**逐条明细**都出自这里。

来历：2026-09-22 `mpq-unpack2` 片的一次性脚本 `.ai-tmp/test/landed_audit.py`；
2026-09-22 `g1-naming-and-mpq-asset` 片提升为**仓内判据资产**（skill §3.5）并参数化。

跑法（任意 cwd 都可）
  python tools/probes/mpq/landed_audit.py
  python tools/probes/mpq/landed_audit.py --out <路径> --wanted <路径> --refs <原版资源>

产物：`--out`（默认 `<root>/.ai-tmp/test/landed-audit.tsv`）
      列：`name<TAB>target<TAB>exists<TAB>bytes`（target 相对仓库根）
"""
import argparse
import io
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from extract_wanted import find_root, route    # 同一套落点契约（⛔ 不许两台各写一份）


def main():
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description="按名清单 vs 盘上落地产物（只读）")
    ap.add_argument("--root", default=None, help="仓库根（默认从脚本位置向上找）")
    ap.add_argument("--refs", default=None, help="解包落点根（默认 <root>/原版资源）")
    ap.add_argument("--wanted", default=None, help="按名清单（默认 <root>/.ai-tmp/test/wanted.txt）")
    ap.add_argument("--out", default=None, help="产物路径（默认 <root>/.ai-tmp/test/landed-audit.tsv）")
    args = ap.parse_args()

    root = os.path.abspath(args.root) if args.root else find_root(here)
    refs = os.path.abspath(args.refs) if args.refs else os.path.join(root, "原版资源")
    wanted = os.path.abspath(args.wanted) if args.wanted else \
        os.path.join(root, ".ai-tmp", "test", "wanted.txt")
    out = os.path.abspath(args.out) if args.out else \
        os.path.join(root, ".ai-tmp", "test", "landed-audit.tsv")

    names = [ln.strip() for ln in io.open(wanted, encoding="utf-8") if ln.strip()]
    rows = []
    for n in names:
        t = route(refs, n)
        ex = os.path.exists(t)
        rows.append((n, t, ex, os.path.getsize(t) if ex else 0))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with io.open(out, "w", encoding="utf-8", newline="\n") as f:
        f.write("name\ttarget\texists\tbytes\n")
        for n, t, ex, sz in rows:
            f.write("%s\t%s\t%s\t%d\n" % (n, os.path.relpath(t, root), "1" if ex else "0", sz))

    # 按「小写目标路径」分组 = Windows 上真正会互相覆盖的那一组
    groups = {}
    for n, t, ex, sz in rows:
        groups.setdefault(t.lower(), []).append((n, ex))
    collide = {k: v for k, v in groups.items() if len(v) > 1}
    lost = sum(len(v) - 1 for v in collide.values())

    on_disk = 0
    for pack in ("d2dc6", "d2raw", "d2text"):
        p = os.path.join(refs, pack)
        c = sum(len(fs) for _, _, fs in os.walk(p)) if os.path.isdir(p) else 0
        on_disk += c
        print("%-6s files=%d" % (pack, c))
    missrows = [(n, t) for n, t, ex, _ in rows if not ex]
    print("wanted names          =", len(names))
    print("distinct target paths =", len(groups))
    print("case-collision groups =", len(collide), " -> lost by overwrite =", lost)
    print("names 落地            =", sum(1 for _, _, ex, _ in rows if ex))
    print("names 未落地（缺名）   =", len(missrows))
    for n, t in missrows:
        print("    MISS %s" % n)
    print("盘上文件总数           =", on_disk)
    print("distinct targets - lost =", len(groups) - lost)
    return 0


if __name__ == "__main__":
    sys.exit(main())
