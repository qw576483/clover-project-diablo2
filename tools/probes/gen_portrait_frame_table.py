# -*- coding: utf-8 -*-
"""判据资产：**创角屏转身过渡逐帧矩形表**的生成器（唯一来源 = 原版 DC6 帧头 + 工程内导出 PNG）。

为什么需要它（不是"顺手写的脚本"）：
  用户在 2026-09-20 报「创建人物时候，点击人物动画变形，很诡异」——
  根因是 `CharCreatePanel.ShowTransitionFrame` 把**每一过渡帧**都塞进"帧 0 那张画框"里，
  而原版过渡帧的尺寸**逐帧不同**（实测 Amazon `fw_21` = 215×228，`fw_0` = 118×198 ⇒ 被横向压掉 45%）。
  修法是逐帧按该帧原生尺寸 ×1.8 同步矩形 ⇒ 需要一张**逐帧尺寸表**（167 帧 / 4 段序列）。

  位置的第二刀：尺寸逐帧给对了以后，**位置**仍按两端线性插值摆（原版 DC6 不在本机，拿不到逐帧
  offset）。原版包到位后 ⇒ 改为按**逐帧真实帧头 offset** 摆，公式与三态待机同源：

     矩形中心 = 热点 anchoredPosition + (offX + w/2, h/2 − offY)

  （推导出处 = 参考物 `Diablerie/Engine/IO/D2Formats/DC6.cs:197-198` 的
   `pivot = (-offsetX/width, offsetY/height)` + `ClassSelector.cs:237-245` 把对象位置设为
   `RectTransform.position`；逐值复核见 `UiLayoutFlow.ClassMenu.Spot` 里三态那几条注释。）

  ⇒ 本脚本读**原版 DC6 的逐帧帧头** `(w, h, offX, offY)`，同时与工程内 PNG 的 IHDR 宽高对账
     （两边必须逐帧相等，否则报错不落盘），把表生成进 `client/Assets/Scripts/UI/UiLayoutFlow.cs`
     的 `ClassMenu.Transition` 标记区；`uicheck` 的同一条断言再**逐帧回读 PNG** 复核。

用法（幂等；只改两个标记之间的内容）：
    python tools/probes/gen_portrait_frame_table.py            # 就地重写表
    python tools/probes/gen_portrait_frame_table.py --print    # 只打印，不落盘
"""

import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(os.path.dirname(HERE))          # <仓库根>/clover-project-diablo2
sys.path.insert(0, os.path.join(PROJECT, "tools", "d2codec"))
import dc6  # noqa: E402

FRONTEND_PNG = os.path.join(PROJECT, "client", "Assets", "Resources", "Clover", "D2", "UI", "FrontEnd")
FRONTEND_DC6 = os.path.join(PROJECT, "原版资源", "d2dc6", "data", "global", "ui", "FrontEnd")
FLOW = os.path.join(PROJECT, "client", "Assets", "Scripts", "UI", "UiLayoutFlow.cs")

# 标记要**带缩进**（16 空格 = `Transition` 类成员缩进）：脚本是"就地替换标记之间"的写法，
#    不带缩进会把标记行前面的 16 个空格一起吃掉。
IND = " " * 16
BEGIN = IND + "// >>> portrait transition frame table (generated) >>>"
END = IND + "// <<< portrait transition frame table <<<"

# 与 `tools/d2codec/export_d2ui.py` 的 FRONTEND_TRANSITIONS / FRONTEND_CLASSES 同序：
#   class 目录名 / 原版文件名前缀 / 序列码 / C# 数组名
SEQS = (
    ("amazon", "AM", "fw", "AmazonFw"),
    ("amazon", "AM", "bw", "AmazonBw"),
    ("barbarian", "BA", "fw", "BarbarianFw"),
    ("barbarian", "BA", "bw", "BarbarianBw"),
)


def png_size(path):
    """读 PNG 的 IHDR 宽高（`dc6.py::write_png_rgba` 的产出：8 字节签名 + IHDR）。"""
    with open(path, "rb") as f:
        head = f.read(24)
    if len(head) < 24 or head[:8] != b"\x89PNG\r\n\x1a\n":
        raise ValueError("不是 PNG：%s" % path)
    w, h = struct.unpack(">II", head[16:24])
    return w, h


def find_ci(directory, name):
    """大小写不敏感找文件（原版这批文件名四种写法混用，见 export_d2ui.py 的 _find_ci）。"""
    for f in os.listdir(directory):
        if f.lower() == name.lower():
            return os.path.join(directory, f)
    return None


def collect():
    out = []
    for cls, prefix, code, arr in SEQS:
        dsrc = os.path.join(FRONTEND_DC6, cls)
        dc6_path = find_ci(dsrc, prefix + code.upper() + ".DC6")
        if dc6_path is None:
            raise SystemExit("找不到原版 DC6：%s/%s%s.DC6" % (dsrc, prefix, code.upper()))
        d = dc6.parse(open(dc6_path, "rb").read())

        dpng = os.path.join(FRONTEND_PNG, cls)
        files = [f for f in os.listdir(dpng) if f.startswith(code + "_") and f.endswith(".png")]
        files.sort(key=lambda f: int(f[len(code) + 1:-4]))       # 帧号升序（字符串序会把 _10 排到 _2 前）
        nums = [int(f[len(code) + 1:-4]) for f in files]
        if nums != list(range(len(nums))):
            raise ValueError("%s/%s 帧号不连续：%s" % (cls, code, nums))
        if len(nums) != len(d.frames):
            raise SystemExit("%s/%s：工程 PNG %d 张 vs 原版 DC6 %d 帧（漂移，先重跑导出器）"
                             % (cls, code, len(nums), len(d.frames)))

        quads = []
        for i, f in enumerate(d.frames):
            pw, ph = png_size(os.path.join(dpng, files[i]))
            if pw != f.width or ph != f.height:
                raise SystemExit("%s/%s 帧 %d：PNG %dx%d ≠ DC6 %dx%d（漂移，先重跑导出器）"
                                 % (cls, code, i, pw, ph, f.width, f.height))
            quads += [f.width, f.height, f.offset_x, f.offset_y]
        out.append((cls, code, arr, quads))
    return out


def render(seqs, nl):
    """生成标记区内容（缩进 = `Transition` 类成员 = 16 空格；调用方负责 CRLF）。"""
    m = " " * 16
    lines = [BEGIN.rstrip("\n")]
    lines.append(m + "// ⚠️ **本节由 `tools/probes/gen_portrait_frame_table.py` 生成，不许手改**：")
    lines.append(m + "//   每帧 4 个数 =（宽, 高, offX, offY），全部是**原版 DC6 的帧头实测**")
    lines.append(m + "//     （`原版资源/d2dc6/data/global/ui/FrontEnd/{cls}/{CLS}{FW,BW}.DC6`）；")
    lines.append(m + "//     同时与工程内 PNG（`D2/UI/FrontEnd/{cls}/{code}_{i}.png`）的 IHDR 宽高逐帧对账，")
    lines.append(m + "//     两边不等就**报错不落盘**（生成器内即闸门）。")
    lines.append(m + "//   复算 = `python tools/probes/gen_portrait_frame_table.py`；")
    lines.append(m + "//   复核 = `uicheck`「每一过渡帧的原生宽高比 == 该帧矩形宽高比」那条断言（逐帧回读 PNG）。")
    lines.append(m + "//   帧序 = DC6 帧号（0 起）。每行 3 帧（= 3×4 个数）。")
    for cls, code, arr, quads in seqs:
        n = len(quads) // 4
        lines.append("")
        lines.append(m + "/// <summary>`%s/%s` %d 帧的（宽,高,offX,offY）原版像素（每帧四个数）。</summary>"
                     % (cls, code, n))
        lines.append(m + "private static readonly int[] %s =" % arr)
        lines.append(m + "{")
        row = []
        for i in range(0, len(quads), 4):
            row.append("%d,%d,%d,%d" % (quads[i], quads[i + 1], quads[i + 2], quads[i + 3]))
            if len(row) == 3:
                lines.append(m + "    " + ", ".join(row) + ",")
                row = []
        if row:
            lines.append(m + "    " + ", ".join(row) + ",")
        lines.append(m + "};")
    lines.append(END)
    return nl.join(lines) + nl


def main():
    seqs = collect()
    # 行尾必须**照原样保留**（本文件是 CRLF）：不这么做会把 1447 行整体改成 LF，
    #    git diff 会变成"全文件重写"，看不出真实改动。
    with open(FLOW, "r", encoding="utf-8", newline="") as f:
        src = f.read()
    nl = "\r\n" if "\r\n" in src else "\n"
    block = render(seqs, nl)

    for cls, code, arr, quads in seqs:
        print("  %-10s %-3s %3d 帧  %dx%d off=(%d,%d) … %dx%d off=(%d,%d)" % (
            cls, code, len(quads) // 4,
            quads[0], quads[1], quads[2], quads[3],
            quads[-4], quads[-3], quads[-2], quads[-1]))

    if "--print" in sys.argv:
        print(block)
        return 0

    i = src.find(BEGIN)
    j = src.find(END)
    if i < 0 or j < 0 or j < i:
        raise SystemExit("UiLayoutFlow.cs 里找不到「转身过渡逐帧矩形表」的两个标记（先在 Transition 里放好骨架再跑本脚本）")
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
