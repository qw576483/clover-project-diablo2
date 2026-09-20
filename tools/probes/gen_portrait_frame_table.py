# -*- coding: utf-8 -*-
"""★ R1-C 判据资产：**创角屏转身过渡逐帧矩形表**的生成器（唯一来源 = 工程内导出 PNG）。

为什么需要它（不是"顺手写的脚本"）：
  用户在 2026-09-20 报「创建人物时候，点击人物动画变形，很诡异」——
  根因是 `CharCreatePanel.ShowTransitionFrame` 把**每一过渡帧**都塞进"帧 0 那张画框"里，
  而原版过渡帧的尺寸**逐帧不同**（实测 Amazon `fw_21` = 215×228，`fw_0` = 118×198 ⇒ 被横向压掉 45%）。
  修法是逐帧按该帧原生尺寸 ×1.8 同步矩形 ⇒ 需要一张**逐帧尺寸表**（167 帧 / 4 段序列）。

  表的**出处只允许有一个**：工程内实际落位的 PNG（它们与 `dc6.py` 解码输出逐字节同名同内容）。
  ⇒ 本脚本读 PNG 的 IHDR 宽高，把表**生成**进 `client/Assets/Scripts/UI/UiLayoutFlow.cs` 的
  `ClassMenu.Transition` 标记区；`uicheck` 的同一条断言再**逐帧回读 PNG 复核**（防止表与素材漂移）。
  ⛔ 原版 DC6 的逐帧 `offset` **不在本机**（`原版资源/` 未下载）⇒ 位置口径见 `Transition.Of` 的注释：
     两端锚点取二者状态（NU1/NU3）的真实矩形底边中点，中途按帧号线性过渡。

用法（幂等；只改两个标记之间的内容）：
    python tools/probes/gen_portrait_frame_table.py            # 就地重写表
    python tools/probes/gen_portrait_frame_table.py --print    # 只打印，不落盘
"""

import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(HERE))          # <仓库根>/clover-project-diablo2
FRONTEND = os.path.join(PROJECT, "client", "Assets", "Resources", "Clover", "D2", "UI", "FrontEnd")
FLOW = os.path.join(PROJECT, "client", "Assets", "Scripts", "UI", "UiLayoutFlow.cs")

# ⚠️ 标记要**带缩进**（16 空格 = `Transition` 类成员缩进）：脚本是"就地替换标记之间"的写法，
#    不带缩进会把标记行前面的 16 个空格一起吃掉。
IND = " " * 16
BEGIN = IND + "// >>> R1-C portrait transition frame table (generated) >>>"
END = IND + "// <<< R1-C portrait transition frame table <<<"

# 与 `tools/d2codec/export_d2ui.py` 的 FRONTEND_TRANSITIONS / FRONTEND_CLASSES 同序：
#   class 目录名 / 序列码 / C# 数组名
SEQS = (
    ("amazon", "fw", "AmazonFw"),
    ("amazon", "bw", "AmazonBw"),
    ("barbarian", "fw", "BarbarianFw"),
    ("barbarian", "bw", "BarbarianBw"),
)


def png_size(path):
    """读 PNG 的 IHDR 宽高（`dc6.py::write_png_rgba` 的产出：8 字节签名 + IHDR）。"""
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("不是 PNG：%s" % path)
    w, h = struct.unpack(">II", head[16:24])
    return w, h


def collect():
    out = []
    for cls, code, arr in SEQS:
        d = os.path.join(FRONTEND, cls)
        files = [f for f in os.listdir(d) if f.startswith(code + "_") and f.endswith(".png")]
        # 帧号升序（字符串序会把 _10 排到 _2 前面）
        files.sort(key=lambda f: int(f[len(code) + 1:-4]))
        nums = [int(f[len(code) + 1:-4]) for f in files]
        if nums != list(range(len(nums))):
            raise ValueError("%s/%s 帧号不连续：%s" % (cls, code, nums))
        pairs = []
        for f in files:
            w, h = png_size(os.path.join(d, f))
            pairs += [w, h]
        out.append((cls, code, arr, pairs))
    return out


def render(seqs, nl):
    """生成标记区内容（缩进 = `Transition` 类成员 = 16 空格；调用方负责 CRLF）。"""
    m = " " * 16
    lines = [BEGIN.rstrip("\n")]
    lines.append(m + "// ⚠️ **本节由 `tools/probes/gen_portrait_frame_table.py` 生成，不许手改**：")
    lines.append(m + "//   数值 = 工程内导出 PNG（`D2/UI/FrontEnd/{cls}/{code}_{i}.png`）的 IHDR 实测宽高。")
    lines.append(m + "//   复算 = `python tools/probes/gen_portrait_frame_table.py`；")
    lines.append(m + "//   复核 = `uicheck`「每一过渡帧的原生宽高比 == 该帧矩形宽高比」那条断言（逐帧回读 PNG）。")
    lines.append(m + "//   帧序 = 导出器的 DC6 帧号（0 起）。每行 6 帧（= 6×2 个数）。")
    for cls, code, arr, pairs in seqs:
        n = len(pairs) // 2
        lines.append("")
        lines.append(m + "/// <summary>`%s/%s` %d 帧的（宽,高）原版像素（每帧两个数）。</summary>"
                     % (cls, code, n))
        lines.append(m + "private static readonly int[] %s =" % arr)
        lines.append(m + "{")
        row = []
        for i in range(0, len(pairs), 2):
            row.append("%d,%d" % (pairs[i], pairs[i + 1]))
            if len(row) == 6:
                lines.append(m + "    " + ", ".join(row) + ",")
                row = []
        if row:
            lines.append(m + "    " + ", ".join(row) + ",")
        lines.append(m + "};")
    lines.append(END)
    return nl.join(lines) + nl


def main():
    seqs = collect()
    # ⚠️ 行尾必须**照原样保留**（本文件是 CRLF）：不这么做会把 1447 行整体改成 LF，
    #    git diff 会变成"全文件重写"，看不出真实改动。
    with open(FLOW, "r", encoding="utf-8", newline="") as f:
        src = f.read()
    nl = "\r\n" if "\r\n" in src else "\n"
    block = render(seqs, nl)

    for cls, code, arr, pairs in seqs:
        print("  %-10s %-3s %3d 帧  %dx%d … %dx%d" % (
            cls, code, len(pairs) // 2, pairs[0], pairs[1], pairs[-2], pairs[-1]))

    if "--print" in sys.argv:
        print(block)
        return 0

    i = src.find(BEGIN)
    j = src.find(END)
    if i < 0 or j < 0 or j < i:
        raise SystemExit("UILayoutFlow.cs 里找不到 R1-C 表的两个标记（先在 Transition 里放好骨架再跑本脚本）")
    j += len(END)
    new = src[:i] + block.rstrip(nl) + src[j:]
    if new != src:
        with open(FLOW, "w", encoding="utf-8", newline="") as f:
            f.write(new)
        print("  已重写 %s（%d → %d 字节）" % (os.path.relpath(FLOW, PROJECT), len(src), len(new)))
    else:
        print("  表无变化（幂等）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
