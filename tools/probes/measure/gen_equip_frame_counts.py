# -*- coding: utf-8 -*-
"""gen_equip_frame_counts.py — 生成 `EquipFrameCounts.generated.cs`（装备外观套的逐动作帧数）。

为什么需要它（= 为什么不许手写这份 C#）
--------------------------------------
原版**每个单位 × 每个动作的帧数都不同**（`amazon` 徒手 attack=13，装上 `jav` 后 attack=15）。
装配外观套（`Chars/{class}/equip/{key}/`）后帧数会变 ⇒ 若沿用徒手那套帧数，帧键会指向
**不存在的图** ⇒ `SpriteFrames` 静默回退纯色占位。所以帧数必须**从导出侧的 manifest 生成**，
与磁盘上的 PNG 数一一对应；手写必然与磁盘漂移（`SpriteFrameCounts.cs` 的文件头已把这条写成铁律）。

唯一数据来源 = 各套目录里的 `manifest.json` 的 `actions.{action}.frames`
（= 原版 `.cof` 的 `framesPerDirection`，由 `tools/d2codec/export_chars.py --equip-sets` 写盘）。

用法（从**仓库根**跑）
----------------------
    python tools/probes/measure/gen_equip_frame_counts.py            # 生成 + 自检，写 C# 文件
    python tools/probes/measure/gen_equip_frame_counts.py --check    # 只自检，不写文件（CI / 复检用）

自检（任一不过 ⇒ 退出码非 0，且**不写文件**）
--------------------------------------------
 B1 每套目录的 `manifest.json` 可解析，且 `equipSet` == 目录名、`equipMap` 非空
 B2 逐动作 `frames` > 0（7 个动作**全部**要有 —— 装备套是整套重导，不存在"缺动作"）
 B3 `Σ(frames × dirs)` == manifest 的 `pngCount` == 目录里实际 PNG 数
 B4 逐 (动作, 方向, 帧号) 的 PNG **都在盘上**且 > 0 字节
 B5 帧数表与"已存在的那份 C# 文件"逐格一致（未传 `--check` 时以新表为准，但仍打印差异）

⛔ 本脚本只读 `client/Assets/Resources/**`（素材）与写**一个新文件**
   `client/Assets/Scripts/Module/View/EquipFrameCounts.generated.cs`；
   不动 `SpriteFrameCounts.cs`、不动徒手套素材。
"""

import json
import os
import sys

#: 本文件 = <repo>/tools/probes/measure/xxx.py ⇒ 仓库根 = 上四级。
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
CHARS = os.path.join(REPO, 'client', 'Assets', 'Resources', 'Clover', 'D2', 'Chars')
CS_OUT = os.path.join(REPO, 'client', 'Assets', 'Scripts', 'Module', 'View',
                      'EquipFrameCounts.generated.cs')

#: 动作顺序 = `Module/View/ViewAnim` 的下标（idle/walk/attack/cast/hit/death/run）。
ACTIONS = ('idle', 'walk', 'attack', 'cast', 'hit', 'death', 'run')
DIRS = ('s', 'sw', 'w', 'nw', 'n', 'ne', 'e', 'se')


def _dirs(path):
    """子目录名（跳过 Unity 的 `*.meta` 与隐藏项 —— 它们不是套目录）。"""
    return [d for d in os.listdir(path)
            if not d.startswith('.') and not d.endswith('.meta')
            and os.path.isdir(os.path.join(path, d))]


def load_sets():
    """枚举 `Chars/{class}/equip/{key}/manifest.json` → 有序 `[(unitKey, class, key, manifest)]`。"""
    out = []
    if not os.path.isdir(CHARS):
        return out
    for cls in sorted(_dirs(CHARS)):
        eqdir = os.path.join(CHARS, cls, 'equip')
        if not os.path.isdir(eqdir):
            continue
        for key in sorted(_dirs(eqdir)):
            suite = os.path.join(eqdir, key)
            man_path = os.path.join(suite, 'manifest.json')
            if not os.path.isfile(man_path):
                out.append(('{}/equip/{}'.format(cls.lower(), key.lower()), cls, key, None, man_path))
                continue
            with open(man_path, 'rb') as fh:
                man = json.loads(fh.read().decode('utf-8'))
            out.append(('{}/equip/{}'.format(cls.lower(), key.lower()), cls, key, man, man_path))
    return out


def check(sets):
    """返回 `(rows, problems, report_lines)`：rows = `[(unitKey, [frames...])]`。"""
    rows = []
    problems = []
    lines = []
    for unit_key, cls, key, man, man_path in sets:
        if man is None:
            problems.append('B1 {}：读不到 manifest.json（{}）'.format(unit_key, man_path))
            continue

        png_count = 0
        frames_row = []
        for act in ACTIONS:
            node = (man.get('actions') or {}).get(act)
            if not node:
                problems.append('B2 {}：manifest 缺动作 「{}」（装备套必须 7 个动作全有）'.format(unit_key, act))
                frames_row.append(0)
                continue
            frames = int(node.get('frames') or 0)
            dirs = int(node.get('dirs') or 0)
            if frames <= 0:
                problems.append('B2 {}：动作 {} 的 frames={}（必须 > 0）'.format(unit_key, act, frames))
            if dirs != len(DIRS):
                problems.append('B2 {}：动作 {} 的 dirs={}（期望 {}）'.format(unit_key, act, dirs, len(DIRS)))
            frames_row.append(frames)
            png_count += frames * dirs

            # B4 帧文件真在盘上
            missing = 0
            for d in DIRS:
                for i in range(frames):
                    p = os.path.join(os.path.dirname(man_path),
                                     '{}_{}_{}.png'.format(act, d, i))
                    if not os.path.isfile(p) or os.path.getsize(p) <= 0:
                        missing += 1
            if missing:
                problems.append('B4 {}：动作 {} 缺 {} 张帧 PNG（或 0 字节）'.format(unit_key, act, missing))

        actual = len([f for f in os.listdir(os.path.dirname(man_path)) if f.endswith('.png')])
        man_png = int(man.get('pngCount') or 0)
        if not (png_count == man_png == actual):
            problems.append('B3 {}：Σ(frames×dirs)={} vs manifest.pngCount={} vs 磁盘 PNG={}'.format(
                unit_key, png_count, man_png, actual))
        if man.get('equipSet') and str(man['equipSet']).lower() != key.lower():
            problems.append('B1 {}：manifest.equipSet={} 与目录名 {} 不一致'.format(
                unit_key, man['equipSet'], key))
        if not man.get('equipMap'):
            problems.append('B1 {}：manifest.equipMap 为空（没说明用了哪层武器/盾）'.format(unit_key))

        em = man.get('equipMap') or {}
        em_str = ','.join('{}={}'.format(k, v) for k, v in sorted(em.items()))
        lines.append('  {:<24} canvas={}x{} origin=({},{})  {}  frames={}'.format(
            unit_key, (man.get('canvas') or {}).get('w'), (man.get('canvas') or {}).get('h'),
            (man.get('canvas') or {}).get('originX'), (man.get('canvas') or {}).get('originY'),
            em_str, ','.join(str(x) for x in frames_row)))
        rows.append((unit_key, frames_row))
    return rows, problems, lines


def read_existing():
    """读已存在的 C# 文件里的 `{ "key", new[] { ... } }` 行 → dict（用于 B5 差异打印）。"""
    if not os.path.isfile(CS_OUT):
        return {}
    out = {}
    with open(CS_OUT, 'rb') as fh:
        for raw in fh.read().decode('utf-8').splitlines():
            line = raw.strip()
            if not line.startswith('{ "'):
                continue
            try:
                key = line.split('"')[1]
                nums = line.split('new[]')[1].replace('}', '').replace('{', '')
                out[key] = [int(x.strip()) for x in nums.split(',') if x.strip()]
            except (IndexError, ValueError):
                continue
    return out


def emit(rows):
    """写 C# 生成物（幂等：同样的输入 ⇒ 逐字节同样的输出）。"""
    body = []
    for unit_key, frames in rows:
        body.append('                {{ "{0}", new[] {{ {1} }} }},'.format(
            unit_key, ', '.join(str(x) for x in frames)))
    text = '''// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/EquipFrameCounts.generated.cs
// ⚠️ **本文件是生成物，禁止手改**（生成器：`tools/probes/measure/gen_equip_frame_counts.py`）。
//
// 数据来源 = 各装备外观套目录 `Chars/{class}/equip/{key}/manifest.json` 的
//   `actions.{action}.frames`（= 原版 `.cof` 的 `framesPerDirection`；导出器
//   `tools/d2codec/export_chars.py --equip-sets` 写盘）。原版素材取自
//   Diablo II (Blizzard North, 2000) 的 d2char.mpq / d2data.mpq，本项目非商用。
//
// 为什么必须生成（= 为什么不能沿用 `SpriteFrameCounts` 的徒手帧数）：
//   原版**逐单位 × 逐动作**帧数都不同，而同一个职业**装上武器后帧数还会变**
//   （实测：amazon 徒手 attack=13，`amazon/equip/jav` attack=15；barbarian 徒手 attack=12，
//    `barbarian/equip/hax` attack=16）。沿用徒手帧数 ⇒ 帧键指向不存在的图 ⇒ 静默退成纯色占位。
//
// 键（unitKey）= `"{class}/equip/{key}"`（小写；与 `SpriteFrames.UnitKeyOfEquip` 同值，
//   `key` = 装备外观套目录名，见 `Module/View/EquipVisual.KeyOf` 的拼法）。
// 值 = 7 个动作的帧数，**下标 = `ViewAnim`**（idle=0 / walk=1 / attack=2 / cast=3 /
//   hit=4 / death=5 / run=6）——顺序与 `ActionNames` 逐字一致。
//
// ⛔ 重跑口径：素材变了（补导同框套 `jav_buc` / `hax_buc`、或重导某套）⇒ 重跑生成器；
//    手改必然与磁盘上的 PNG 数量对不上。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;

namespace Diablo2.Module.View
{
    /// <summary>装备外观套的逐动作真实帧数（生成物，见文件头）。</summary>
    internal static class EquipFrameCounts
    {
        /// <summary>动作名（下标顺序 = <see cref="ViewAnim"/>，与 <see cref="SpriteFrames.Keys"/> 拼法一致）。</summary>
        public static readonly string[] ActionNames =
        {
            "idle", "walk", "attack", "cast", "hit", "death", "run",
        };

        /// <summary>
        /// `装备外观套 unitKey（"{class}/equip/{key}"） → 7 个动作的帧数`。
        /// <para>**只登记磁盘上真的存在的套**：某 key 不在表里 ⇒ `SpriteFrames` 按
        /// `EquipVisual.FallbackChain` 逐级回退（`{w}_{s}` → `{w}` → `{s}` → 徒手）。</para>
        /// </summary>
        public static readonly Dictionary<string, int[]> ByUnit =
            new Dictionary<string, int[]>
            {
%s
            };

        /// <summary>该装备外观套是否已导出（有 manifest ⇒ 生成物里就有一行）。</summary>
        public static bool Has(string unitKey)
        {
            return !string.IsNullOrEmpty(unitKey) && ByUnit.ContainsKey(unitKey.ToLowerInvariant());
        }

        /// <summary>取某套某动作的帧数；未登记 / 越界返回 0（调用方按"缺图"处理）。</summary>
        public static int Of(string unitKey, ViewAnim anim)
        {
            if (string.IsNullOrEmpty(unitKey)) return 0;
            int[] counts;
            if (!ByUnit.TryGetValue(unitKey.ToLowerInvariant(), out counts)) return 0;
            var i = (int)anim;
            if (counts == null || i < 0 || i >= counts.Length) return 0;
            return counts[i];
        }
    }
}
''' % ('\n'.join(body))
    with open(CS_OUT, 'wb') as fh:
        fh.write(text.replace('\n', '\r\n').encode('utf-8'))


def main():
    only_check = '--check' in sys.argv[1:]
    sets = load_sets()

    print('=== EquipFrameCounts 生成器（{} 套目录）==='.format(len(sets)))
    rows, problems, lines = check(sets)
    for ln in lines:
        print(ln)

    if not rows:
        print('!! 一套装备外观套都没找到（{}）'.format(CHARS))
        return 1

    existing = read_existing()
    new_map = dict(rows)
    added = sorted(set(new_map) - set(existing))
    removed = sorted(set(existing) - set(new_map))
    changed = sorted(k for k in set(new_map) & set(existing) if new_map[k] != existing[k])
    print('-- 与现有生成物对比：新增 {} / 删除 {} / 帧数变化 {}'.format(
        added or '无', removed or '无', changed or '无'))
    for k in changed:
        print('   {}: {} -> {}'.format(k, existing[k], new_map[k]))

    if problems:
        print('\n!! 自检不过（{} 项），**不写文件**：'.format(len(problems)))
        for p in problems:
            print('   ' + p)
        return 1

    print('\n自检 B1~B4 全过。')
    if only_check:
        print('--check：不写文件。')
        return 0 if not (added or removed or changed) else 1

    emit(rows)
    print('已写：{}'.format(CS_OUT))
    print('注意：同框套 Chars/{class}/equip/{w}_{s} 若尚未导出，生成物里就没有这些行；')
    print('   运行时会按回退链落到 {w} 或 {s} 并用 WarnOnce 说明；补导后重跑本脚本即可，代码零改动。')
    return 0


if __name__ == '__main__':
    # Windows 控制台默认 GBK：本脚本会打印中文/箭头 ⇒ 显式转 UTF-8（失败也不影响判定）。
    try:
        sys.stdout.reconfigure(encoding='utf-8', errors='replace')
        sys.stderr.reconfigure(encoding='utf-8', errors='replace')
    except (AttributeError, ValueError):
        pass
    sys.exit(main())
