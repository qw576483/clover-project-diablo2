# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/t0_keyfix.py
#
# **T0 实体键唯一化（一次性迁移 + 常驻对账）** —— 修 `tools/verify.ps1::coverage-rows` 的恒真红。
#
#   `coverage-rows` 的第一条子判 = 「`策划/实体清单.tsv` 行数 == `策划/状态矩阵.tsv` 去重实体数」。
#   实测清单 1370 行、去重后只有 1367 个 `(维度,实体)` ⇒ **无论矩阵填成什么样这一条都红**。
#   3 对重复（共 6 行）：
#     · `D4UI · ui:背包·装备槽·inv_weapons（整幅）` ×2   ← `w3_uigame_audit.tsv:33` 与 `:36`
#     · `D4UI · ui:背包·装备槽·inv_ring_amulet（右半）` ×2 ← `w3_uigame_audit.tsv:39` 与 `:41`
#     · `D10逻辑 · evt:Pause` ×2                        ← `Core/Events.cs:364 StatePause` 与 `:389 TriggerPause`
#
#   · 命名规则由 `enum_all.py` 生成（本脚本调它的 `discover_d4/discover_d10` 取**新名**，
#   · **只改这 6 行**：清单行数仍 1370、Σ状态数仍 4977、矩阵行数/行序/行数不变，
#     被同步改名的矩阵行**只动第 2 列（实体）**；
#   · 其余行**逐字节不变**（本脚本在迁移前后各算一遍「每行前五列 SHA256」与
#     「非 S1、非去重行 SHA256」并**自比对**，不一致就直接中止不写盘）。
#
# 复现：
#     python tools/probes/enumerate/t0_keyfix.py --verify     # 只读对账（不取锁、不写盘）
#     python tools/probes/enumerate/t0_keyfix.py --migrate    # 取 `.ai-tmp/test/matrix.lock` 后原地改名
#
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import time
import codecs
import hashlib
import collections

HERE = os.path.dirname(os.path.abspath(__file__))
if HERE not in sys.path:
    sys.path.insert(0, HERE)
import enum_all as EA  # noqa: E402

ROOT = EA.ROOT
MANIFEST = os.path.join(ROOT, '策划/实体清单.tsv')
MATRIX = os.path.join(ROOT, '策划/状态矩阵.tsv')
REGISTRY = os.path.join(ROOT, '策划/差异登记.tsv')
LOCK = os.path.join(ROOT, '.ai-tmp/test/matrix.lock')
LOCK_TRIES = 60
LOCK_WAIT = 2.0


def _safe_stdio():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass


def read_text(path):
    """⇒ (文本, 是否 BOM, 主导换行符, 原文件是否以换行结尾)。⛔ 不改盘上任何形态。"""
    rawb = io.open(path, 'rb').read()
    bom = rawb.startswith(codecs.BOM_UTF8)
    raw = rawb.decode('utf-8-sig')
    nl = '\r\n' if '\r\n' in raw else '\n'
    return raw, bom, nl, raw.endswith(('\n', '\r'))


def write_text(path, text, bom, nl, tail):
    data = ('\ufeff' if bom else '') + nl.join(text.splitlines()) + (nl if tail else '')
    with io.open(path, 'w', encoding='utf-8', newline='') as f:
        f.write(data)
    return data


def sha(s):
    return hashlib.sha256(s.encode('utf-8')).hexdigest()


def rows_of(text):
    """数据行（跳过 `#` 注释 + 空行 + 表头）⇒ [(物理行号, cells)]。"""
    out = []
    for i, l in enumerate(text.splitlines()):
        if not l.strip() or l.startswith('#'):
            continue
        c = l.split('\t')
        if c[0] in ('维度', '是什么'):
            continue
        out.append((i + 1, c))
    return out


# ═══════════════════════════════════════════════════════════════════════════
# 旧名 → 新名（新名**由 enum_all 现算**；旧名 = 去掉后缀后的名字）
# ═══════════════════════════════════════════════════════════════════════════
def rename_map():
    """⇒ {(维度, 旧实体名): [新名, …]}（按扫盘顺序）。

    口径：`enum_all.discover_d4/discover_d10` 产出的实体名里，凡**带 `@<文件>:<行>` 后缀**的，
    其"去掉后缀"就是旧名；同一个旧名下的全部新名按**出现顺序**收集 ⇒ 与矩阵里的行块顺序一致。"""
    pairs = collections.OrderedDict()
    for dim, fn in (('D4UI', EA.discover_d4), ('D10逻辑', EA.discover_d10)):
        for e in fn():
            m = re.match(r'^(.*)@([^@]*):(\d+)$', e['ent'])
            if not m:
                continue
            pairs.setdefault((dim, m.group(1)), []).append(e['ent'])
    return pairs


def unlock():
    try:
        os.remove(LOCK)
    except OSError:
        pass


def lock():
    for i in range(LOCK_TRIES):
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ('t0_keyfix pid=%d\n' % os.getpid()).encode('utf-8'))
            os.close(fd)
            return i
        except FileExistsError:
            time.sleep(LOCK_WAIT)
    raise SystemExit('[fatal] 取锁失败：%s 已被占用 %d×%.1fs' % (LOCK, LOCK_TRIES, LOCK_WAIT))


# ═══════════════════════════════════════════════════════════════════════════
# 证据：逐行前五列 SHA256 / 非 S1 且非去重行 SHA256
# ═══════════════════════════════════════════════════════════════════════════
def skipped_rows(text, dims_skip, names_skip):
    """⇒ (要排除的**物理行号**集合, 行数)。⚠️ 必须用**行号**而不是名字做排除：
    改名之后同名行已经不存在了 —— 按名字排除会让"改前/改后"排掉不同的行，自比对假红。"""
    idx = set()
    for ln, c in rows_of(text):
        if c[0] in dims_skip:
            idx.add(ln)
        elif len(c) > 1 and c[1] in names_skip:
            idx.add(ln)
    return idx, len(idx)


def fingerprint(text, skip_idx):
    rows = rows_of(text)
    hist = collections.Counter(c[0] for _ln, c in rows)
    keep = [(ln, c) for ln, c in rows if ln not in skip_idx]
    return dict(
        n=len(rows),
        hist=' '.join('%s=%d' % (d, hist[d]) for d in EA.DIMS if hist[d]),
        all_first5=sha('\n'.join('\t'.join(c[:5]) for _ln, c in rows)),
        excl_n=len(keep),
        excl_all=sha('\n'.join('\t'.join(c) for _ln, c in keep)),
        excl_first5=sha('\n'.join('\t'.join(c[:5]) for _ln, c in keep)),
    )


def show_fp(tag, fp):
    print('%s  数据行=%d  维度直方图=%s' % (tag, fp['n'], fp['hist']))
    print('%s  全行前五列 SHA256   = %s' % (tag, fp['all_first5']))
    print('%s  非S1且非去重行 %d 行：整行 SHA256 = %s ｜ 前五列 SHA256 = %s'
          % (tag, fp['excl_n'], fp['excl_all'], fp['excl_first5']))


def cmp_fp(a, b, expect_change):
    ok = True
    if a['n'] != b['n'] or a['hist'] != b['hist']:
        print('  ⛔ 行数/维度直方图变了'); ok = False
    if expect_change:
        if a['all_first5'] == b['all_first5']:
            print('  ⛔ 去重行的前五列应当变化，但 SHA 相同（改名没生效？）'); ok = False
        if a['excl_all'] != b['excl_all'] or a['excl_first5'] != b['excl_first5'] or a['excl_n'] != b['excl_n']:
            print('  ⛔ 非S1且非去重行**被改动了**（必须逐字节不变）'); ok = False
    else:
        if a['all_first5'] != b['all_first5'] or a['excl_all'] != b['excl_all']:
            print('  ⛔ 指纹应当相同却没相同'); ok = False
    return ok


# ═══════════════════════════════════════════════════════════════════════════
# 迁移
# ═══════════════════════════════════════════════════════════════════════════
def migrate():
    pairs = rename_map()
    if not pairs:
        raise SystemExit('[fatal] enum_all 没有任何带后缀的实体 ⇒ 迁移无事可做（是不是已改过？）')
    print('== 旧名 → 新名（新名由 enum_all 现算）==')
    old_names = set()
    for (dim, old), news in pairs.items():
        old_names.add(old)
        print('%s  %s' % (dim, old))
        for n in news:
            print('      ⇒ %s' % n)
    print()

    try:
        waited = lock()
        print('[lock] 已取得 %s（等了 %d×%.1fs）' % (os.path.relpath(LOCK, ROOT), waited, LOCK_WAIT))
        mf_raw, mf_bom, mf_nl, mf_tail = read_text(MANIFEST)
        mx_raw, mx_bom, mx_nl, mx_tail = read_text(MATRIX)
        print('[lock] 已在锁内重读两表：清单换行=%s BOM=%s ｜ 矩阵换行=%s BOM=%s'
              % (repr(mf_nl), mf_bom, repr(mx_nl), mx_bom))

        mf_skip, _ = skipped_rows(mf_raw, ('S1数值',), old_names)
        mx_skip, _ = skipped_rows(mx_raw, ('S1数值',), old_names)
        mf_before = fingerprint(mf_raw, mf_skip)
        mx_before = fingerprint(mx_raw, mx_skip)
        print('（自比对口径：排除 **S1 行** 与 **本次改名的 6/16 行**，按**物理行号**排除 —— 改名后行号不变）')
        print()

        # 幂等：已经迁移过 ⇒ 旧名 0 命中 ⇒ 直接退出（**不写盘**），第二次跑必然字节不变
        hits_mf = sum(1 for l in mf_raw.splitlines() if len(l.split('\t')) > 1
                      and (l.split('\t')[0], l.split('\t')[1]) in pairs)
        hits_mx = sum(1 for l in mx_raw.splitlines() if len(l.split('\t')) > 1
                      and (l.split('\t')[0], l.split('\t')[1]) in pairs)
        if hits_mf == 0 and hits_mx == 0:
            print('[migrate] 旧实体名在盘上 0 命中（清单 0 / 矩阵 0）⇒ **已是唯一键状态，本此不写盘**（幂等无操作）')
            return
        show_fp('[改前 清单]', mf_before)
        show_fp('[改前 矩阵]', mx_before)
        print()

        # ---- ① 实体清单：只改第 2 列 ----
        mf_lines = mf_raw.splitlines()
        mf_cnt = collections.Counter()
        for i, l in enumerate(mf_lines):
            if not l.strip() or l.startswith('#'):
                continue
            c = l.split('\t')
            if len(c) < 7 or c[0] == '维度':
                continue
            key = (c[0], c[1])
            if key in pairs:
                k = mf_cnt[key]
                if k >= len(pairs[key]):
                    raise SystemExit('[fatal] 清单里 %s 出现次数多于 enum_all 给出的新名数' % (key,))
                c[1] = pairs[key][k]
                mf_cnt[key] += 1
                mf_lines[i] = '\t'.join(c)
        for key, news in pairs.items():
            if mf_cnt[key] != len(news):
                raise SystemExit('[fatal] 清单里 %s 命中 %d 行，期望 %d 行'
                                 % (key, mf_cnt[key], len(news)))

        # ---- ② 状态矩阵：只改第 2 列；同名行块按顺序切分给各新名 ----
        mx_lines = mx_raw.splitlines()
        blocks = collections.defaultdict(list)
        for i, l in enumerate(mx_lines):
            if not l.strip() or l.startswith('#'):
                continue
            c = l.split('\t')
            if len(c) < 8 or c[0] == '维度':
                continue
            if (c[0], c[1]) in pairs:
                blocks[(c[0], c[1])].append(i)
        mx_renamed = 0
        for key, news in pairs.items():
            if key not in blocks:
                raise SystemExit('[fatal] 矩阵里找不到旧实体：%s' % (key,))
            idxs = blocks[key]
            if len(idxs) % len(news) != 0:
                raise SystemExit('[fatal] 矩阵里 %s 有 %d 行，不能被 %d 个新名均分'
                                 % (key, len(idxs), len(news)))
            per = len(idxs) // len(news)
            for k, i in enumerate(idxs):
                c = mx_lines[i].split('\t')
                c[1] = news[k // per]
                mx_lines[i] = '\t'.join(c)
                mx_renamed += 1
        print('[migrate] 清单改名 %d 行 ｜ 矩阵改名 %d 行（每对均分：%s）'
              % (sum(mf_cnt.values()), mx_renamed,
                 ', '.join('%s→%d 行' % (len(v), len(blocks[k]) // len(v)) for k, v in pairs.items())))

        mf_after_text = '\n'.join(mf_lines)
        mx_after_text = '\n'.join(mx_lines)
        mf_after = fingerprint(mf_after_text, mf_skip)
        mx_after = fingerprint(mx_after_text, mx_skip)
        print()
        show_fp('[改后 清单]', mf_after)
        show_fp('[改后 矩阵]', mx_after)
        print()
        print('== 自比对 ==')
        ok = cmp_fp(mf_before, mf_after, True) and cmp_fp(mx_before, mx_after, True)
        # 矩阵行数/行序不变（只改第 2 列：逐行断言其余列逐字节相同）
        if len(mf_lines) != len(mf_raw.splitlines()) or len(mx_lines) != len(mx_raw.splitlines()):
            print('  ⛔ 物理行数变了'); ok = False
        for tag, before, after in (('清单', mf_raw, mf_after_text), ('矩阵', mx_raw, mx_after_text)):
            b, a = before.splitlines(), after.splitlines()
            if len(b) != len(a):
                print('  ⛔ %s 行数变了' % tag); ok = False
                continue
            diff = [i + 1 for i in range(len(b)) if b[i] != a[i]]
            bad = []
            for i in diff:
                cb, ca = b[i].split('\t'), a[i].split('\t')
                if len(cb) != len(ca) or any(cb[j] != ca[j] for j in range(len(cb)) if j != 1):
                    bad.append(i + 1)
            print('  %s：物理行 %d，其中 %d 行有差异（全部只动第 2 列：%s）'
                  % (tag, len(b), len(diff), '是' if not bad else '⛔ 否 %s' % bad))
            if bad:
                ok = False
        if not ok:
            raise SystemExit('⛔ 自比对失败 ⇒ 不写盘（盘上两表保持原样）')

        write_text(MANIFEST, mf_after_text, mf_bom, mf_nl, mf_tail)
        write_text(MATRIX, mx_after_text, mx_bom, mx_nl, mx_tail)
        print()
        print('[write] 已写回 %s + %s（形态：换行 %s / BOM %s 与改前一致）'
              % (os.path.relpath(MANIFEST, ROOT), os.path.relpath(MATRIX, ROOT), repr(mx_nl), mx_bom))
    finally:
        unlock()
        print('[lock] 已释放 %s' % os.path.relpath(LOCK, ROOT))


# ═══════════════════════════════════════════════════════════════════════════
# 对账（只读；供收尾自检与后续片复用）
# ═══════════════════════════════════════════════════════════════════════════
def verify():
    tbl = EA.build()
    h1, rows1 = tbl['manifest']
    h2, rows2 = tbl['matrix']
    h3, rows3 = tbl['registry']

    keys1 = collections.Counter((r[0], r[1]) for r in rows1)
    dup1 = [k for k, v in keys1.items() if v > 1]
    sum_states = sum(int(r[4]) for r in rows1)
    hist1 = collections.Counter(r[0] for r in rows1)
    missing_dims = [d for d in EA.DIMS if hist1[d] == 0]

    mf_raw, _b, _n, _t = read_text(MANIFEST)
    mx_raw, _b2, _n2, _t2 = read_text(MATRIX)
    mx_rows = rows_of(mx_raw)
    # 矩阵里同一实体本来就展开成 N 行（N = 清单的 `状态数`）⇒ 去重数按 (维度,实体) 数，
    # 「每实体行数」另与清单的 `状态数` 逐条比（这才是 coverage-rows 的第二条子判）。
    mxcnt = collections.Counter((c[0], c[1]) for _l, c in mx_rows)
    states1 = {(r[0], r[1]): int(r[4]) for r in rows1}
    badcnt = [(k, mxcnt.get(k, 0), v) for k, v in states1.items() if mxcnt.get(k, 0) != v]
    extra = [k for k in mxcnt if k not in states1]

    # enum_all.write_tsv 的口径：utf-8-sig（恒带 BOM）+ CRLF ⇒ 比对时**连 BOM 一起比**
    # （`read_text` 用 utf-8-sig 会吃掉 BOM ⇒ 这里另取一份不去 BOM 的文本）
    mf_nobom = io.open(MANIFEST, 'rb').read().decode('utf-8')
    exp = '\ufeff' + '\r\n'.join(h1) + '\r\n' + \
        '\r\n'.join('\t'.join(str(x) for x in r) for r in rows1) + '\r\n'
    mf_ok = (exp == mf_nobom)

    first5_disk = ['\t'.join(c[:5]) for _l, c in mx_rows]
    first5_exp = ['\t'.join(str(x) for x in r[:5]) for r in rows2]
    mx_ok = (first5_disk == first5_exp)

    print('== t0_keyfix --verify：T0 三张表的自洽数 ==')
    print('清单：数据行=%d  去重(维度,实体)=%d  重复键=%s  Σ状态数=%d  15维度缺=%s'
          % (len(rows1), len(keys1), (dup1 or '无'), sum_states, (missing_dims or '无')))
    print('矩阵：数据行=%d  去重(维度,实体)=%d  每实体行数≠清单状态数的实体=%s  矩阵多出的实体=%s'
          % (len(mx_rows), len(mxcnt), (badcnt or '无'), (extra or '无')))
    print('coverage-rows 三条子判：清单行数(%d)==矩阵去重实体数(%d) ⇒ %s ；矩阵行数(%d)==Σ状态数(%d) ⇒ %s ；状态数全数字 ⇒ %s'
          % (len(rows1), len(mxcnt), len(rows1) == len(mxcnt),
             len(mx_rows), sum_states, len(mx_rows) == sum_states,
             all(str(r[4]).isdigit() for r in rows1)))
    print('清单可复现（enum_all.build() 现算 == 盘上文件，逐字节）⇒ %s' % mf_ok)
    print('矩阵前五列可复现（enum_all.build() 现算 == 盘上文件，逐行逐字节）⇒ %s' % mx_ok)
    reg_raw, _b, _n, _t = read_text(REGISTRY)
    print('差异登记：盘上数据行=%d ｜ enum_all 现算=%d ⇒ %s'
          % (len(rows_of(reg_raw)), len(rows3), len(rows_of(reg_raw)) == len(rows3)))
    #   实体清单 1367 / Σ状态数 4965（= 矩阵行数）。
    #   上一版 1366 / 4963 是「T0FIX 片改 client 之后、T0GAP 片改 client 之前」的快照；
    #   T0GAP 片（死亡扣金币 + 双武器组）又改了 `client/**` ⇒ 实体集合再漂一次：
    #     + `Core/Events.cs:152` 新增 `Events.SwapWeaponRequest` ⇒ **+1 实体**
    #       （`D10逻辑 · evt:D2.Item.SwapWeaponRequest`，2 状态行）
    #     ⇒ 1366 + 1 = **1367** 实体 / 4963 + 2 = **4965** 状态行。
    #   （`Def/GameKeyAlias.cs` 删的 2 个别名 / `D2/UI/UI` 空目录 / `Module/Map` 新增 2 文件
    #     —— 那批的收口见 `migrate_drift.py` 的改名映射 `sys:Map(11 cs) → sys:Map(13 cs)`。）
    #   这两个数只在「实体集合变化」时改，且必须同时改本行 + 说明原因（否则闸门会变成假红/假绿）。
    EXP_ENTITIES, EXP_STATES = 1367, 4965
    good = (not dup1) and (not badcnt) and (not extra) and len(rows1) == EXP_ENTITIES and sum_states == EXP_STATES \
        and len(mx_rows) == EXP_STATES and len(mxcnt) == EXP_ENTITIES and mf_ok and mx_ok and not missing_dims
    print('VERDICT: %s' % ('PASS' if good else 'FAIL'))
    return 0 if good else 1


if __name__ == '__main__':
    _safe_stdio()
    if '--migrate' in sys.argv:
        migrate()
    elif '--verify' in sys.argv:
        sys.exit(verify())
    else:
        print(__doc__ or '')
        print('用法：--verify（只读对账） / --migrate（取锁后原地改名）')
