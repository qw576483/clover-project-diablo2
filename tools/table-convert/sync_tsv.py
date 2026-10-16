# -*- coding: utf-8 -*-
"""
把打表产物里的 tsv 同步到**运行时目录**（StreamingAssets）。

为什么需要这一步（见 `docs/配表说明.md` 的详细说明）：
  * 打表工具把 tsv 写到 `client/Assets/Scripts/Table/Tsv/`；
  * `Assets/Scripts/**` 下的裸文件**不会**进 Unity 构建；
  * 生成的 `Load(path)` 用的是 `File.ReadAllLines(path)`（真实文件系统路径，
    出处 `clover-:279`）；
  * 所以运行时目录必须是 `Assets/StreamingAssets/Table/`
    —— 编辑器与 Windows 独立版下 `Application.streamingAssetsPath` 都是**真实目录**。

用法：
    python sync_tsv.py
    python sync_tsv.py --check     # 只比对不拷贝；不一致时退出码 1（CI/自检用）
"""

import argparse
import filecmp
import os
import shutil
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SRC_DIR = os.path.join(PROJECT_ROOT, "client", "Assets", "Scripts", "Table", "Tsv")
DST_DIR = os.path.join(PROJECT_ROOT, "client", "Assets", "StreamingAssets", "Table")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true", help="只校验一致性，不拷贝")
    args = ap.parse_args()

    if not os.path.isdir(SRC_DIR):
        print(f"[fatal] 源 tsv 目录不存在：{SRC_DIR}（先跑 convert.py + 打表）")
        return 2
    files = sorted(f for f in os.listdir(SRC_DIR) if f.lower().endswith(".tsv"))
    if not files:
        print(f"[fatal] {SRC_DIR} 下没有 *.tsv")
        return 2

    if not os.path.isdir(DST_DIR):
        if args.check:
            print(f"[fail] 运行时目录不存在：{DST_DIR}")
            return 1
        os.makedirs(DST_DIR)
        print(f"[info] 创建 {DST_DIR}")

    missing, stale, same = [], [], 0
    for f in files:
        s, d = os.path.join(SRC_DIR, f), os.path.join(DST_DIR, f)
        if not os.path.exists(d):
            missing.append(f)
        elif not filecmp.cmp(s, d, shallow=False):
            stale.append(f)
        else:
            same += 1

    extra = sorted(f for f in os.listdir(DST_DIR)
                   if f.lower().endswith(".tsv") and f not in files)

    if args.check:
        print(f"[check] 一致 {same} / 缺失 {len(missing)} / 内容不同 {len(stale)} / 多余 {len(extra)}")
        for f in missing:
            print(f"   缺失：{f}")
        for f in stale:
            print(f"   内容不同：{f}")
        for f in extra:
            print(f"   多余（源目录已不存在）：{f}")
        return 0 if not (missing or stale or extra) else 1

    for f in files:
        shutil.copyfile(os.path.join(SRC_DIR, f), os.path.join(DST_DIR, f))
        print(f"  同步 {f}  ({os.path.getsize(os.path.join(DST_DIR, f))} 字节)")
    for f in extra:
        os.remove(os.path.join(DST_DIR, f))
        print(f"  删除多余 {f}")
    print(f"完成：{len(files)} 个 tsv 已同步到 {DST_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
