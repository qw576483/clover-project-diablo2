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
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import storm
import export_chars as chars

PALETTE_REL = 'data\\global\\palette\\units\\Pal.dat'
DCC_PREFIX = 'data\\global\\missiles\\'
OVERLAY_PREFIX = 'data\\global\\overlays\\'
OFFICIAL_TABLE = ('\u539f\u7248\u8d44\u6e90', 'd2lod1.10txt-1.10f', 'data', 'global', 'excel',
                  'Missiles.txt')
OVERLAY_TABLE = ('\u539f\u7248\u8d44\u6e90', 'd2lod1.10txt-1.10f', 'data', 'global', 'excel',
                 'Overlay.txt')

# ── 加色素材：黑 = 不发光，导出时必须把黑压成透明 ────────────────────────────
#   原版导弹/叠加层是按**加色**画的（黑像素 = 那一块不发亮 = 看不见）。
#   Unity 的 SpriteRenderer 走 alpha 混合 ⇒ 那些黑像素原样画出来就是**黑斑**。
#   实测：
#       FrozenArmor      75.0% 不透明像素是近黑  → 整张图糊成一坨黑影
#       LightningStrike  41.7%                  → 电弧读成"一串黑豆子"
#   修法 = 导出时按亮度重算 alpha（`a' = a · min(1, lum·boost)`），等价于加色：
#   黑边彻底透明，只剩真正发亮的冰晶 / 电弧。
#   值 = (alpha 亮度系数, RGB 增益)
#     · FrozenArmor 的调色板索引是**暗蓝**（典型 (12,12,40)）：原版靠运行时提亮才读成冰，
#       直接搬过来是"几乎看不见"（实测：）⇒ alpha 系数 1.7 压掉黑边 + RGB 增益 4.5 提亮成冰蓝。
#     · LightningStrike 本来就是亮电弧，只需要压黑边。
GLOW_CELS = {
    'FrozenArmor': (1.7, 4.5),        # 护甲冰晶（官方 Overlay.txt `frozenarmor`）
    'LightningStrike': (1.6, 1.12),   # 连锁闪电 / 闪电折线（官方 `chainlightning` 的 CelFile）
}


def glow_premultiply(rgba, boost, gain=1.12):
    """把"黑=不发光"的加色素材转成 alpha 混合也读得对的 RGBA（就地改写 bytearray）。

    `a' = a · min(1, max(r,g,b)/255 · boost)`；`rgb' = min(255, rgb · gain)`。
    `gain` 用来补回"原版靠运行时提亮"的暗色素材（护甲冰晶）。
    ⛔ 全透明像素原样保留（别顺手给它上色）。
    """
    for i in range(len(rgba) // 4):
        o = i * 4
        a = rgba[o + 3]
        if a == 0:
            continue
        r, g, b = rgba[o], rgba[o + 1], rgba[o + 2]
        lum = r if r > g else g
        if b > lum:
            lum = b
        k = (lum / 255.0) * boost
        if k >= 1.0:
            k = 1.0
        na = int(a * k)
        if na <= 1:
            rgba[o] = 0
            rgba[o + 1] = 0
            rgba[o + 2] = 0
            rgba[o + 3] = 0
            continue
        rgba[o] = min(255, int(r * gain))
        rgba[o + 1] = min(255, int(g * gain))
        rgba[o + 2] = min(255, int(b * gain))
        rgba[o + 3] = na
    return rgba


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


def read_overlay_frames(root):
    """官方 `Overlay.txt` 的 `Filename → {Frames}`（集合口径同 <see cref="read_official_anim_len"/>）。

    ⚠️ overlay 表 col0 = overlay **键名**（如 `frozenarmor`）、col1 = **Filename**（如 `FrozenArmor`，
    这才是 DCC 真名）—— 帧数自证必须按 Filename 索引。曾按键名试读 `fire_cast_1.dcc` 解不到，
    误判"overlay 无素材"；真名是 `FireCast_for_Sorceress.dcc`，一读就有。
    version=100 的行 Filename 带 `Expansion\\` 前缀（非本批法师素材，原样保留即可）。"""
    path = os.path.join(root, *OVERLAY_TABLE)
    if not os.path.isfile(path):
        return {}
    out = {}
    with io.open(path, 'r', encoding='latin1') as fh:
        head = fh.readline().rstrip('\r\n').split('\t')
        fi, fr = head.index('Filename'), head.index('Frames')
        for line in fh:
            cols = line.rstrip('\r\n').split('\t')
            if len(cols) <= max(fi, fr):
                continue
            fn, fv = cols[fi].strip(), cols[fr].strip()
            if not fn or fn.lower() == 'null' or not fv.isdigit():
                continue
            out.setdefault(fn.lower(), set()).add(int(fv))
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

        // 查表 = 大小写不敏感：素材键历史上有全小写（"firebolt"）与帧表键
        // （"Firebolt"）不一致的坑 ⇒ 查不到会静默退成程序化环/盘（见 `SkillCatalog` 段头注释）。
        //
        // ⚠️ 去重：帧表里同一张图会有两种写法（实测 `IceBolt` 与 `Icebolt`）
        //   ⇒ 直接塞进 `OrdinalIgnoreCase` 字典会抛 `ArgumentException`（已添加了具有相同键的项），
        //   而**静态初始化器抛异常会让整个类型永久不可用**（此后每次访问都抛
        //   `TypeInitializationException`）⇒ `ProjectileView`/`SkillFxView` 全线 TryCreate 失败并吞掉
        //   异常 ⇒ 所有投射物与技能特效"只有逻辑、没有画面"（实测「什么技能都没有效果」）。
        //   ⇒ 这里先落 `Ordinal` 原表、再按大小写不敏感**去重**（重复键保留先出现的一条）。
        //   ⛔ 别再改回"直接 new Dictionary(OrdinalIgnoreCase) { ... }"。
        private static readonly Dictionary<string, int> Counts = BuildCounts();

        private static Dictionary<string, int> BuildCounts()
        {
            var raw = new Dictionary<string, int>(StringComparer.Ordinal)
            {
%s            };

            var map = new Dictionary<string, int>(raw.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in raw)
                if (!map.ContainsKey(kv.Key)) map.Add(kv.Key, kv.Value);
            return map;
        }

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
    """把导出的帧数写成 C# 表（与 PNG 同批产出 ⇒ 不会两边不一致）。

    ⚠️ **merge 语义**：旧版"按本次 report 全量重写" ⇒
    `--only` 导子集时会把表里其余条目**全部删掉**（实测：大量技能特效静默退化成程序化
    环/盘、skillcheck 8-2/8-3 大小写判据直接红）。现在 = 读入现有表 → 本次覆盖/追加 → 写回，
    partial 导出不再连坐。

    去重口径：同一张图可能有**大小写不同的两种写法**（实测 `IceBolt` / `Icebolt`）——
    本表运行时按大小写不敏感查（见产物里的 `BuildCounts`），所以同键只保留**先出现**的那条，
    免得产物里再出现"看起来是两条、其实同一个键"的噪声。"""
    path = os.path.join(root, *CS_PATH)
    existing = {}
    if os.path.isfile(path):
        with io.open(path, 'r', encoding='utf-8') as fh:
            for m in re.finditer(r'\{\s*"([^"]+)"\s*,\s*(\d+)\s*\}', fh.read()):
                existing.setdefault(m.group(1), int(m.group(2)))
    for m in report:
        if not (m.get('ok', True) and m.get('framesPerDir')):
            continue
        key = str(m['cel'])
        hit = next((k for k in existing if k.lower() == key.lower()), None)
        if hit is not None and hit != key:
            continue          # 同键（大小写不敏感）已存在另一种写法 ⇒ 不新增重复键
        existing[key] = m['framesPerDir']
    rows = ['            { "%s", %d },' % (k, v) for k, v in existing.items()]
    body = CS_HEADER.replace('%s', '\n'.join(rows) + '\n')
    with io.open(path, 'w', encoding='utf-8', newline='\n') as fh:
        fh.write(body)
    print('已写出 %d 条帧数表（merge；本次 %d 条）：%s'
          % (len(existing), len(rows), os.path.join(*CS_PATH)))


def main():
    root = find_root(HERE)
    ap = argparse.ArgumentParser(description='导出原版投射物 DCC → PNG')
    ap.add_argument('--only', default='', help='只导这些 CelFile（逗号分隔）')
    ap.add_argument('--out', default=os.path.join(root, 'client', 'Assets', 'Resources',
                                                  'Clover', 'D2', 'Missiles'))
    ap.add_argument('--raw-out', default=os.path.join(root, '\u539f\u7248\u8d44\u6e90', 'd2raw',
                                                      'data', 'global', 'missiles'))
    ap.add_argument('--prefix', default='',
                    help='DCC 所在 MPQ 子目录（默认 data\\global\\missiles\\；'
                         'overlay 传 data\\global\\overlays\\ ⇒ 官方帧数自动改读 Overlay.txt 的 Frames 列）')
    args = ap.parse_args()

    dcc_prefix = args.prefix or DCC_PREFIX
    is_overlay = 'overlays' in dcc_prefix

    mpq_dir = os.path.join(root, '\u539f\u7248\u8d44\u6e90', '_mpq_incoming')
    storm.set_work_dir(os.path.join(root, '.ai-tmp', 'test', 'storm-tmp'))

    names = ([n for n in args.only.split(',') if n]
             or read_tsv_cel_files(os.path.join(root, 'client', 'Assets', 'StreamingAssets',
                                                'Table', 'Missile.tsv')))
    anim_len = read_overlay_frames(root) if is_overlay else read_official_anim_len(root)

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
            rel = dcc_prefix + name + '.dcc'
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
                    rgba = bytearray(dccmod.frame_rgba(idx, palette))
                    gcfg = GLOW_CELS.get(name)
                    if gcfg:
                        glow_premultiply(rgba, gcfg[0], gcfg[1])
                    dccmod.write_png_rgba(
                        os.path.join(out_dir, '%s_%d.png' % (chars.DIR_NAMES[logical], fi)),
                        rgba, canvas_w, canvas_h)
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

        # 根 manifest：**merge**（同 emit_cs，partial 不连坐；键 = cel 小写）
        root_manifest = os.path.join(args.out, 'manifest.json')
        old = []
        if os.path.isfile(root_manifest):
            try:
                with io.open(root_manifest, 'r', encoding='utf-8') as fh:
                    old = json.load(fh)
            except Exception:
                old = []
        by_cel = {}
        for m in old:
            if isinstance(m, dict) and m.get('cel'):
                by_cel[str(m['cel']).lower()] = m
        for m in report:
            if m.get('cel'):
                by_cel[str(m['cel']).lower()] = m
        with io.open(root_manifest, 'w', encoding='utf-8') as fh:
            json.dump(list(by_cel.values()), fh, ensure_ascii=False, indent=1)

        emit_cs(root, report)
        print('共 %d 个 CelFile，失败 %d' % (len(names), bad))
    finally:
        storm.close_archive(h)
    print('done')


if __name__ == '__main__':
    main()
