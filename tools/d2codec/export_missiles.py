#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""导出原版投射物（法术飞行体）逐帧 PNG —— 供 `Module/Skill/ProjectileView` 取真图。

为什么要有它：`ProjectileView` 原本给每个投射物贴一张 2×2 纯色方块（**自画近似**），
原版飞行体是 `data\\global\\missiles\\<CelFile>.dcc`（官方 `Missiles.txt` 的 `CelFile` 列，
本项目运行时表 = `missile_c.cel_file`）。本脚本把 DCC 逐方向逐帧解成 PNG。

⛔ 五条硬口径（每一条都能复算，不许"看起来对"就改）
  1. **按名解包**（`storm.py` 文件头 坑 2/3）：`SFileFindFirstFile` 不可用、
     `SFileHasFile` 不可信 ⇒ 判据只有「`read_file` 解回非空字节」。
  2. **调色板 = `data\\global\\palette\\units\\Pal.dat`**（与 `export_chars.py` 同源：
     单位 / 怪物 / 飞行体共用单位调色板；`.dat` = B,G,R 反序，见 `dcc.read_pl2`）。
  3. **方向口径 = `export_chars.DIR_NAMES` + `export_chars.to_file_slot`**（**同一套**，
     不另立一份）：文件名 `{方向名}_{帧号}.png`，方向名 = 本项目 `CloverEngine.Dir8`
     （`s/sw/w/nw/n/ne/e/se`），槽位换算走 Diablerie `DirectionMapping`。
  4. **画布 = 全方向包围盒的对称外扩**，原点恒在画布正中 ⇒ pivot 恒 (0.5, 0.5)。
     理由：`ProjectileView` 按"节点位置 = 飞行体中心"贴图，各方向共用一张画布才不会
     因方向切换而抖动。
  5. **帧数自证**：DCC 每方向帧数必须等于官方 `Missiles.txt` 该 CelFile 的 `AnimLen`
     （同一 CelFile 多行时取任一非空值）—— 不等就点名报错。这是"解错文件/解错方向"
     唯一便宜的判据。

产物（`--out` 默认 `client/Assets/Resources/Clover/D2/Missiles`）
  `<Cel>/<方向名>_<帧号>.png`   8 个逻辑方向 × 每方向 `AnimLen` 帧
  `<Cel>/manifest.json`        出处 / 调色板 / 方向名 / 帧数 / 画布 / 逐方向槽位
  `manifest.json`             （根）逐 Cel 汇总

跑法（任意 cwd 都可）
  python tools/d2codec/export_missiles.py
  python tools/d2codec/export_missiles.py --only Firebolt,Icebolt
"""
import argparse
import io
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import storm
import export_chars as chars

PALETTE_REL = 'data\\global\\palette\\units\\Pal.dat'
DCC_PREFIX = 'data\\global\\missiles\\'
OFFICIAL_TABLE = ('\u539f\u7248\u8d44\u6e90', 'd2lod1.10txt-1.10f', 'data', 'global', 'excel',
                  'Missiles.txt')


def find_root(start):
    d = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(d, 'client', 'Assets')):
            return d
        parent = os.path.dirname(d)
        if parent == d:
            raise SystemExit('找不到仓库根（向上一直没看到 client/Assets）')
        d = parent


def read_tsv_cel_files(path):
    """读运行时表 `Missile.tsv` 的 `cel_file` 列（去重、去空、去 'null'）。"""
    out = []
    with io.open(path, 'r', encoding='utf-8', newline='') as fh:
        head = fh.readline().rstrip('\r\n').split('\t')
        ci = head.index('cel_file')
        for line in fh:
            line = line.rstrip('\r\n')
            if not line:
                continue
            cols = line.split('\t')
            if ci >= len(cols):
                continue
            v = cols[ci].strip()
            if not v or v.lower() == 'null':
                continue
            if v not in out:
                out.append(v)
    return out


def read_official_anim_len(root):
    """官方 `Missiles.txt` 的 `CelFile → {AnimLen…}`（**集合**）。

    为什么是集合：同一个 CelFile 出现在**多行**且各行的 `AnimLen` 可以不同
    （实测 `SafeArrow`：`magicarrow` 行 = 1、`skbowarrow8` 行 = 8，而 DCC 每方向 8 帧）。
    判据 ⇒ "DCC 帧数 ∈ 该 CelFile 的 AnimLen 取值集合"。
    """
    path = os.path.join(root, *OFFICIAL_TABLE)
    if not os.path.isfile(path):
        return {}
    out = {}
    with io.open(path, 'r', encoding='latin1') as fh:
        head = fh.readline().rstrip('\r\n').split('\t')
        ci, ai = head.index('CelFile'), head.index('AnimLen')
        for line in fh:
            cols = line.rstrip('\r\n').split('\t')
            if len(cols) <= max(ci, ai):
                continue
            cel, ln = cols[ci].strip(), cols[ai].strip()
            if not cel or not ln.isdigit():
                continue
            out.setdefault(cel.lower(), set()).add(int(ln))
    return out


CS_PATH = ('client', 'Assets', 'Scripts', 'Module', 'Skill', 'MissileFrameCounts.generated.cs')
CS_HEADER = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/MissileFrameCounts.generated.cs  【生成文件，勿手改】
// 由 `python tools/d2codec/export_missiles.py` 写出（数据源 = 原版 `data\\global\\missiles\\
// <CelFile>.dcc` 的每方向帧数，与本目录下 `<CelFile>/` 里同名的 `{方向}_{帧号}.png` 一一对应）。
//
// 为什么需要它：`ProjectileView` 要按"飞行距离 ÷ 速度"算帧号并**回绕**，回绕上界就是这里的帧数；
//   猜一个上界 ⇒ 帧键指向不存在的图 ⇒ 静默退成占位方块。
// 与 `manifest.json`（同目录产物）必须一致：改任何一侧都要重跑导出器。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;

namespace Diablo2.Module.Skill
{
    /// <summary>投射物 CelFile → 每方向帧数（原版 `.dcc` 帧数 = 官方 `Missiles.txt` 的 `AnimLen`）。</summary>
    internal static class MissileFrameCounts
    {
        /// <summary>未登记 CelFile 的兜底帧数（= 1；调用方打一次 Warn）。</summary>
        public const int Fallback = 1;

        private static readonly Dictionary<string, int> Counts =
            new Dictionary<string, int>(StringComparer.Ordinal)
        {
%s        };

        /// <summary>该 CelFile 的每方向帧数（未登记 ⇒ <see cref="Fallback"/>）。</summary>
        public static int Of(string celFile)
        {
            if (string.IsNullOrEmpty(celFile)) return Fallback;
            return Counts.TryGetValue(celFile, out var n) ? n : Fallback;
        }

        /// <summary>该 CelFile 是否已登记（调用方据此决定要不要打一次 Warn）。</summary>
        public static bool Has(string celFile)
        {
            return !string.IsNullOrEmpty(celFile) && Counts.ContainsKey(celFile);
        }
    }
}
'''


def emit_cs(root, report):
    """把导出的帧数写成 C# 表（与 PNG 同批产出 ⇒ 不会两边不一致）。"""
    rows = []
    for m in report:
        if m.get('ok', True) and m.get('framesPerDir'):
            rows.append('            { "%s", %d },' % (m['cel'], m['framesPerDir']))
    path = os.path.join(root, *CS_PATH)
    body = CS_HEADER.replace('%s', '\n'.join(rows) + '\n')
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(body)
    print('已写出 %d 条帧数表：%s' % (len(rows), os.path.join(*CS_PATH)))


def main():
    root = find_root(HERE)
    ap = argparse.ArgumentParser(description='导出原版投射物 DCC → PNG')
    ap.add_argument('--only', default='', help='只导这些 CelFile（逗号分隔）')
    ap.add_argument('--out', default=os.path.join(root, 'client', 'Assets', 'Resources',
                                                  'Clover', 'D2', 'Missiles'))
    ap.add_argument('--raw-out', default=os.path.join(root, '\u539f\u7248\u8d44\u6e90', 'd2raw',
                                                      'data', 'global', 'missiles'))
    args = ap.parse_args()

    mpq_dir = os.path.join(root, '\u539f\u7248\u8d44\u6e90', '_mpq_incoming')
    storm.set_work_dir(os.path.join(root, '.ai-tmp', 'test', 'storm-tmp'))

    names = ([n for n in args.only.split(',') if n]
             or read_tsv_cel_files(os.path.join(root, 'client', 'Assets', 'StreamingAssets',
                                                'Table', 'Missile.tsv')))
    anim_len = read_official_anim_len(root)

    h = storm.open_archive(os.path.join(mpq_dir, 'D2data.mpq'), patch='Patch_D2.mpq')
    try:
        import dcc as dccmod

        pal_bytes = storm.read_file(h, PALETTE_REL)
        if not pal_bytes:
            raise SystemExit('调色板解不出来：' + PALETTE_REL)
        tmp_pal = os.path.join(root, '.ai-tmp', 'test', 'pal-units.dat')
        with open(tmp_pal, 'wb') as fh:
            fh.write(pal_bytes)
        palette = dccmod.read_pl2(tmp_pal)
        print('调色板 %s（%d 字节）' % (PALETTE_REL, len(pal_bytes)))

        os.makedirs(args.raw_out, exist_ok=True)
        report = []
        bad = 0
        for name in names:
            rel = DCC_PREFIX + name + '.dcc'
            data = storm.read_file(h, rel)
            if not data:
                print('!! 解不到：%s' % rel)
                report.append({'cel': name, 'ok': False})
                bad += 1
                continue

            with open(os.path.join(args.raw_out, name + '.dcc'), 'wb') as fh:
                fh.write(data)

            dec = dccmod.parse(data, rel)
            dirs = dec.directions
            fpd = dec.frames_per_dir
            n = dec.direction_count()

            want_lens = anim_len.get(name.lower())
            if want_lens and fpd not in want_lens:
                print('!! 帧数不符：%s 的 DCC 每方向 %d 帧，官方 AnimLen 取值 %s ⇒ 拒绝导出'
                      % (name, fpd, sorted(want_lens)))
                report.append({'cel': name, 'ok': False, 'dccFrames': fpd,
                               'animLen': sorted(want_lens)})
                bad += 1
                continue

            # 全方向包围盒 → 对称画布（原点居中）
            min_left = min_top = -1
            max_right = max_bottom = 1
            for d in dirs:
                b = d.box
                min_left = min(min_left, b.left)
                min_top = min(min_top, b.top)
                max_right = max(max_right, b.left + b.width)
                max_bottom = max(max_bottom, b.top + b.height)
            canvas_w = max(2, 2 * max(-min_left, max_right))
            canvas_h = max(2, 2 * max(-min_top, max_bottom))
            ox, oy = canvas_w // 2, canvas_h // 2

            out_dir = os.path.join(args.out, name)
            os.makedirs(out_dir, exist_ok=True)
            slots = {}
            total = 0
            for logical in range(chars.LOGICAL_DIRS):
                slot = chars.to_file_slot(n, logical)
                slots[chars.DIR_NAMES[logical]] = slot
                d = dirs[slot]
                bw = d.box.width
                dst_x = ox + d.box.left
                dst_y = oy + d.box.top
                for fi in range(len(d.frames)):
                    idx = bytearray(canvas_w * canvas_h)
                    fr = d.frames[fi]
                    for y in range(d.box.height):
                        dy = dst_y + y
                        if dy < 0 or dy >= canvas_h:
                            continue
                        srow = y * bw
                        drow = dy * canvas_w + dst_x
                        for x in range(bw):
                            v = fr[srow + x]
                            if v:
                                dx = dst_x + x
                                if 0 <= dx < canvas_w:
                                    idx[drow + x] = v
                    dccmod.write_png_rgba(
                        os.path.join(out_dir, '%s_%d.png' % (chars.DIR_NAMES[logical], fi)),
                        dccmod.frame_rgba(idx, palette), canvas_w, canvas_h)
                    total += 1

            manifest = {
                'cel': name,
                'sourceMpq': 'D2data.mpq',
                'sourcePath': rel,
                'palette': PALETTE_REL,
                'dccDirections': n,
                'framesPerDir': fpd,
                'officialAnimLen': sorted(want_lens) if want_lens else None,
                'dirs': list(chars.DIR_NAMES),
                'fileSlots': slots,
                'canvas': {'w': canvas_w, 'h': canvas_h, 'originX': ox, 'originY': oy,
                           'pivot': [0.5, 0.5]},
                'pngCount': total,
            }
            with open(os.path.join(out_dir, 'manifest.json'), 'w', encoding='utf-8') as fh:
                json.dump(manifest, fh, ensure_ascii=False, indent=1)
            print('== %-22s DCC方向 %2d → 导出 8 逻辑方向 × %2d 帧 = %3d 张，画布 %dx%d'
                  % (name, n, fpd, total, canvas_w, canvas_h))
            report.append(manifest)

        with open(os.path.join(args.out, 'manifest.json'), 'w', encoding='utf-8') as fh:
            json.dump(report, fh, ensure_ascii=False, indent=1)

        emit_cs(root, report)
        print('共 %d 个 CelFile，失败 %d' % (len(names), bad))
    finally:
        storm.close_archive(h)
    print('done')


if __name__ == '__main__':
    main()
