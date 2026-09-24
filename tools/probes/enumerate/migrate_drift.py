# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/migrate_drift.py
#
#   搬运已有结论。为什么需要（实测事故）：并发的写 `client/**` 的片改了源码（删了 2 个按键别名 +
#   1 个空素材目录、新增 2 个 Map 源文件）⇒ `enum_all.py --check` 变成 1366 / 4963，而盘上三张表
#   仍是 1370 / 4977 ⇒ **清单不能由当前源码复现**（`t0_keyfix.py --verify` 必红）。
#
# 处方（主 agent 裁决）：
#   ① `EA.build()` 生成**新骨架**（清单 / 矩阵 / 差异登记 三张，全部由脚本产出）；
#   ② 按 **四元组 (维度, 实体, 状态/事件, 边界值)** 把**旧矩阵已有结论**（第 6/7/8 列）搬进新矩阵
#      —— 不按行号（行号会平移）；实体**改名的**用改名映射（如 `sys:Map(11 cs)` → `sys:Map(13 cs)`）；
#      **消失的行**自然不在新骨架里（= 删除）；
#   ④ 取写表锁后写三张表（`策划/{实体清单,状态矩阵,差异登记}.tsv`）。
#
# 复现（`cd <项目根>`，**migrate → fill_offline 是一个单元，中间别插别的**）：
#     python tools/probes/enumerate/migrate_drift.py --dry-run   # 只打印账，不写
#     python tools/probes/enumerate/migrate_drift.py             # 取锁 → 写三张表
#     python tools/probes/enumerate/fill_offline.py              # 补离线行（含被清空的那批）
#
# 写表协议：`FileMode.CreateNew` 新建 `.ai-tmp/test/matrix.lock`；拿不到就等 2s 重试（≤60 次）；
#    拿到后**重读**、只改目标行、写完删锁。
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import time
import codecs
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
LOCK_TRIES, LOCK_WAIT = 60, 2.0

REJUDGE = [
    # D-D10：`Events.SaveDone` 已接生产者（SaveModule.SignalSaveDone）+ 消费者（AppFlow.OnSaveDone）
    ('D10逻辑', 'evt:D2.Save.Done', '有订阅者（被消费）'),
    # C-D11：KeyPause 接线到 AppFlow.EscPressed()；KeyClosePanel 接线到 SettingsPanel；KeySwapWeapon 仍 0 消费
    ('D11输入', 'key:KeyPause(Escape)', '游戏内上下文（Stage）'),
    ('D11输入', 'key:KeyClosePanel(Escape)', '游戏内上下文（Stage）'),
    ('D11输入', 'key:KeySwapWeapon(W)', '游戏内上下文（Stage）'),
    # B-S3：静音开关已接线（冷启动读回 + Set/Save 落盘 + 施加引擎）⇒ 6 行须重判
    ('S3设置', 'set:audio/bgm_mute', '默认值（首次启动）'),
    ('S3设置', 'set:audio/bgm_mute', '运行期修改生效'),
    ('S3设置', 'set:audio/bgm_mute', '重启后仍生效（持久化）'),
    ('S3设置', 'set:audio/sfx_mute', '默认值（首次启动）'),
    ('S3设置', 'set:audio/sfx_mute', '运行期修改生效'),
    ('S3设置', 'set:audio/sfx_mute', '重启后仍生效（持久化）'),
    # A-S2：帧时间 / 进图加载 / 内存 —— 代码已改（分帧补块 + 池化 + 块根先失活）⇒ 旧的 Play 读数作废。
    #   清空后 `fill_offline.py` **没有 S2 处理器** ⇒ 保持留空（红行 = 等 Play 重采片，见 t0-remaining.md）。
    ('S2性能', 'perf:帧时间(ms/frame)', '帧时间（ms/frame）'),
    ('S2性能', 'perf:帧时间(ms/frame)', '进图加载耗时（s）'),
    ('S2性能', 'perf:帧时间(ms/frame)', '内存占用（MB）'),
    # E-D3：六个 `mat:*` 的「非纯色占位」判据（唯一色 = 1）由 **E42** 定性 ——
    #   这些"实心单色"是**原版原生平色/模板帧**或**从不被请求的帧**（机械证据 =
    #   `tools/probes/enumerate/d3_flatcolor.py`，重跑 ⇒ VERDICT OK）⇒ 结论应为
    #   `允许的差异(→E42)`，不是 `不一致`（旧结论是 E42 定案**之前**填的）。
    #   清空后由 `fill_offline.py::fill_d3`（`E42_DIRS`）重填。
    ('D3材质', 'mat:Objects/warp', '非纯色占位（颜色值个数 > 1）'),
    ('D3材质', 'mat:Objects/town_trees', '非纯色占位（颜色值个数 > 1）'),
    ('D3材质', 'mat:Objects/town_fence', '非纯色占位（颜色值个数 > 1）'),
    ('D3材质', 'mat:Objects/moor_river', '非纯色占位（颜色值个数 > 1）'),
    ('D3材质', 'mat:uiarts/MiniMap', '非纯色占位（颜色值个数 > 1）'),
    ('D3材质', 'mat:uiarts/SkillIcon', '非纯色占位（颜色值个数 > 1）'),
]

_NORM = re.compile(r'^([^()]+)\(\d+ cs\)$')     # `sys:Map(11 cs)` → `sys:Map`


def _safe_stdio():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass


def read_text(path):
    rawb = io.open(path, 'rb').read()
    bom = rawb.startswith(codecs.BOM_UTF8)
    raw = rawb.decode('utf-8-sig')
    return raw, bom


def data_rows(raw, ncols):
    """数据行 ⇒ [(物理行号, cells)]（跳过 `#` / 空行 / 表头）。"""
    out = []
    for i, l in enumerate(raw.splitlines()):
        if not l.strip() or l.startswith('#'):
            continue
        c = l.split('\t')
        if len(c) < ncols or c[0] in ('维度', '是什么'):
            continue
        out.append((i + 1, c))
    return out


def lock():
    for i in range(LOCK_TRIES):
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, ('migrate_drift pid=%d\n' % os.getpid()).encode('utf-8'))
            os.close(fd)
            return i
        except FileExistsError:
            time.sleep(LOCK_WAIT)
    raise SystemExit('[fatal] 取锁失败：%s 被占用 %d×%.1fs' % (LOCK, LOCK_TRIES, LOCK_WAIT))


def unlock():
    try:
        os.remove(LOCK)
    except OSError:
        pass


def rename_map(old_keys, new_keys):
    """旧 (维度,实体) → 新 (维度,实体) 的**改名**映射（只认同一维度、同一 `<前缀>(<n> cs)` 家族）。"""
    removed = old_keys - new_keys
    added = new_keys - old_keys
    bydim = collections.defaultdict(dict)
    for (dim, ent) in added:
        m = _NORM.match(ent)
        if m:
            bydim[dim][m.group(1)] = (dim, ent)
    out = {}
    for (dim, ent) in removed:
        m = _NORM.match(ent)
        if m and m.group(1) in bydim.get(dim, {}):
            out[(dim, ent)] = bydim[dim][m.group(1)]
    return out, removed, added


def main():
    _safe_stdio()
    dry = '--dry-run' in sys.argv

    old_mf_raw, _ = read_text(MANIFEST)
    old_mx_raw, _ = read_text(MATRIX)
    old_mf = data_rows(old_mf_raw, 7)
    old_mx = data_rows(old_mx_raw, 8)
    old_keys = set((c[0], c[1]) for _l, c in old_mf)

    tbl = EA.build()
    h1, rows1 = tbl['manifest']
    h2, rows2 = tbl['matrix']
    h3, rows3 = tbl['registry']
    new_keys = set((r[0], r[1]) for r in rows1)

    rens, removed, added = rename_map(old_keys, new_keys)
    print('== 一、实体集合差异 ==')
    print('旧清单实体数=%d  新清单实体数=%d' % (len(old_keys), len(new_keys)))
    print('改名映射（%d 条）：%s' % (len(rens), '、'.join('%s/%s → %s/%s' % (k[0], k[1], v[0], v[1])
          for k, v in sorted(rens.items())) or '无'))
    print('消失的实体（%d 个）：%s' % (len(removed - set(rens)), '、'.join('%s/%s' % k for k in sorted(removed - set(rens))) or '无'))
    print('新增的实体（%d 个）：%s' % (len(added - set(rens.values())), '、'.join('%s/%s' % k for k in sorted(added - set(rens.values()))) or '无'))
    print()

    # 旧矩阵行按四元组索引（实体已套改名）
    old_filled = [(l, c) for l, c in old_mx if all(c[i].strip() for i in (5, 6, 7))]
    print('== 二、四元组迁移 ==')
    print('旧矩阵已填行数（三列全非空） = %d' % len(old_filled))

    new_rows = [list(r) for r in rows2]
    idx = {}
    for j, r in enumerate(new_rows):
        k4 = (r[0], r[1], r[2], r[3])
        idx.setdefault(k4, []).append(j)

    matched = 0
    unmatched = []
    used = set()
    for (ln, c) in old_filled:
        dim, ent = c[0], c[1]
        if (dim, ent) in rens:
            dim, ent = rens[(dim, ent)]
        k4 = (dim, ent, c[2], c[3])
        cand = [j for j in idx.get(k4, []) if j not in used]
        if cand:
            j = cand[0]
            used.add(j)
            new_rows[j][5] = c[5]
            new_rows[j][6] = c[6]
            new_rows[j][7] = c[7]
            matched += 1
        else:
            unmatched.append((ln, dim, ent, c[2], c[3], c[6]))
    print('迁移成功（四元组命中） = %d' % matched)
    print('未匹配（旧已填但新骨架无此四元组） = %d' % len(unmatched))
    print('未匹配清单：')
    for (ln, dim, ent, st, bd, verd) in unmatched:
        print('   L%-5d %s | %s | %s | %s | 旧结论=%s' % (ln, dim, ent, st, bd, verd[:40]))

    # 三、清空须重判的行
    print()
    print('== 三、清空「须按改后代码重判」的行（交给 fill_offline.py 重填）==')
    cleared = 0
    for (dim, ent, st) in REJUDGE:
        hit = 0
        for r in new_rows:
            if r[0] == dim and r[1] == ent and r[2] == st:
                hit += 1
                if any(r[i].strip() for i in (5, 6, 7)):
                    r[5] = r[6] = r[7] = ''
                    cleared += 1
        if hit == 0:
            print('   ⚠️ 新骨架里找不到 %s | %s | %s（可能实体已消失）' % (dim, ent, st))
    print('清空 %d 行' % cleared)
    print('新矩阵行数 = %d（应 == Σ状态数 %d）' % (len(new_rows), sum(int(r[4]) for r in rows1)))
    print('迁移后立即可填（三列非空） = %d' % sum(1 for r in new_rows if all(r[i].strip() for i in (5, 6, 7))))
    print()

    if dry:
        print('--dry-run：未写文件。')
        return

    try:
        w = lock()
        print('[lock] 已取得 %s（等了 %d×%.1fs）' % (os.path.relpath(LOCK, ROOT), w, LOCK_WAIT))
        EA.write_tsv(MANIFEST, h1, rows1)
        EA.write_tsv(MATRIX, h2, [tuple(r) for r in new_rows])
        EA.write_tsv(REGISTRY, h3, rows3)
        print('[write] 已写三张表：%s / %s / %s（形态 = enum_all 口径：utf-8 BOM + CRLF）'
              % (os.path.relpath(MANIFEST, ROOT), os.path.relpath(MATRIX, ROOT), os.path.relpath(REGISTRY, ROOT)))
    finally:
        unlock()
        print('[lock] 已释放')


if __name__ == '__main__':
    main()
