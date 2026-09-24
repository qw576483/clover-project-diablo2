#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
gen_automap_readback.py -- durable re-production of the automap Play readback that
                           acceptance row 11 (策划/验收表.md) rests on.

WHY THIS FILE EXISTS
--------------------
Row 11 used to cite `.ai-tmp/test/automap/automap_log_b1.txt`, a one-off Play log
that was swept away with the rest of .ai-tmp/test.  The SAME session's `[AUTOMAP]`
log lines survive verbatim inside a durable, on-disk artifact that row 11 already
cites -- `.ai-tmp/screenshots/automap_contact_b1.index.tsv` (the contact-sheet
index; its `log_evidence(verbatim)` column is the raw log line, not a paraphrase).

This script copies those lines back out, verbatim, into a single durable text file
under tools/probes/refs/ so the citation points at a location that is not part of
the throw-away .ai-tmp/test tree.

NO TEXT IS RETYPED: every emitted line comes from a ` || `-separated slice of the
index file that itself starts with a timestamp and carries `[AUTOMAP]`.

Usage:  python tools/probes/refs/gen_automap_readback.py
"""

import hashlib
import io
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..', '..'))
SRC = os.path.join(ROOT, '.ai-tmp', 'screenshots', 'automap_contact_b1.index.tsv')
OUT = os.path.join(HERE, 'automap_readback_b1.txt')

STAMP = re.compile(r'^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[Info\] \[AUTOMAP\]')
NODE = re.compile(r'^(PANELROOT|BACKDROP|OVERLAY|CANVAS)\b')


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    with io.open(SRC, 'r', encoding='utf-8') as f:
        rows = f.read().split('\n')

    header = rows[0].split('\t')
    col = header.index('log_evidence(verbatim)')

    lines = []
    for row in rows[1:]:
        if row.strip() == '':
            continue
        cells = row.split('\t')
        if len(cells) <= col:
            continue
        for piece in cells[col].split(' || '):
            p = piece.strip().strip('|').strip()
            if STAMP.match(p):
                lines.append(p)
            elif NODE.match(p) and lines:
                lines[-1] = lines[-1] + ' || ' + p

    # The same physical log line shows up in more than one index cell -- once bare and
    # once with its ` || PANELROOT...` continuation.  Collapse by the line's identity
    # (timestamp + AUTOMAP tag + GRID=<tile>/SWEEP <area>), keeping the LONGEST variant
    # so the node-tree continuation is not lost, and keeping first-seen order.
    KEY = re.compile(r'^(\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}\] \[Info\] \[AUTOMAP\] '
                     r'(?:GRID=\S+|SWEEP \S+))')
    order = []
    best = {}
    for ln in lines:
        m = KEY.match(ln)
        k = m.group(1) if m else ln
        if k not in best:
            order.append(k)
            best[k] = ln
        elif len(ln) > len(best[k]):
            best[k] = ln
    uniq = [best[k] for k in order]

    out = []
    out.append('=' * 78)
    out.append('自动地图（Tab）实机读数 —— 重产件，服务 策划/验收表.md 第 11 行')
    out.append('=' * 78)
    out.append('来源（在盘，耐用）: .ai-tmp/screenshots/automap_contact_b1.index.tsv')
    out.append('来源sha256        : ' + sha256_of(SRC))
    out.append('来源列            : log_evidence(verbatim) —— 原始 [AUTOMAP] 行，非转述')
    out.append('生成器            : tools/probes/refs/gen_automap_readback.py（本文件即其输出）')
    out.append('原件              : .ai-tmp/test/automap/automap_log_b1.txt 已随 .ai-tmp/test')
    out.append('                    清空消失（本片 2026-09-24 实测该目录为空）')
    out.append('等价性            : 同一次 Play 会话的同一批 [AUTOMAP] 行，逐字搬移；')
    out.append('                    本文件的行 = 上表 log_evidence(verbatim) 单元格内容本身。')
    out.append('已知缺口          : 索引表里没有 `GEOM=` 前缀行（该前缀只出现在')
    out.append('                    tools/probes/automap_sheet.py / drivers 源码里）；')
    out.append('                    逐格节点树 dump（PANELROOT / BACKDROP / OVERLAY / CANVAS）')
    out.append('                    以 ` || ` 续行形式保留在对应 GRID= 行末尾。')
    out.append('⛔ 本文件未新造任何文本：全部逐字取自上述在盘索引表。')
    out.append('')
    out.append('提取到的 [AUTOMAP] 行 = %d' % len(uniq))
    out.append('')
    for ln in uniq:
        out.append(ln)
    out.append('')

    with io.open(OUT, 'w', encoding='utf-8', newline='\n') as f:
        f.write('\n'.join(out))

    kinds = {}
    for ln in uniq:
        m = re.search(r'\[AUTOMAP\] ([A-Z]+)', ln)
        k = m.group(1) if m else '?'
        kinds[k] = kinds.get(k, 0) + 1
    print('wrote %s' % OUT)
    print('lines=%d kinds=%s' % (len(uniq), sorted(kinds.items())))
    print('out sha256 = %s' % sha256_of(OUT))


if __name__ == '__main__':
    main()
