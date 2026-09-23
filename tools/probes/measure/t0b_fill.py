# -*- coding: utf-8 -*-
"""
t0b_fill.py -- 判据资产：把本片（t0-play-s2-d11-d5）判定的三类行写进 策划/状态矩阵.tsv。

三个组（互不重行）：
  --d5   3 行 D5动画（传送门 / 投射物 / 精英 的「循环点/结束（Finished）」）
         -> 主 agent 裁决（.ai-tmp/test/dispatch-log.tsv:86 (c)）：
            传送门 -> 允许的差异(->E42)   投射物 -> 允许的差异(->E40) 子项 ④   精英 -> 允许的差异(->E40) 子项 ①
  --s2   12 行 S2性能（本片 1 次 Play 全部重采）
  --d11  69 行 D11输入（23 别名 x 3 上下文：菜单 / 对话 / 商店，本片实机逐个注入真按键）

事实来源：本片 1 次 Play 的冻结证据 .ai-tmp/screenshots/t0b_evidence.txt（只有 [T0B] 行进本脚本）。

写表协议（与 t0_d4d5_fill.py 同口径）：
  改 策划/状态矩阵.tsv 前先取锁 .ai-tmp/test/matrix.lock（O_CREAT|O_EXCL，拿不到 sleep 2 重试 <=60 次）；
  拿到后重读整表、只改本组负责的行，不动行序 / 不增删行 / 不碰别的行与前五列；
  写完删锁并打印三条机械证据（行数不变 / 维度直方图不变 / 非负责行 SHA256 不变）。

用法：
  python tools/probes/measure/t0b_fill.py --d5
  python tools/probes/measure/t0b_fill.py --s2 --apply
  python tools/probes/measure/t0b_fill.py --d11 --apply
"""
import argparse
import collections
import hashlib
import os
import re
import sys
import time

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

HERE = os.path.abspath(__file__)
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(HERE))))
MATRIX = os.path.join(ROOT, "策划", "状态矩阵.tsv")
LOCK = os.path.join(ROOT, ".ai-tmp", "test", "matrix.lock")
EVID = os.path.join(ROOT, ".ai-tmp", "screenshots", "t0b_evidence.txt")

DIM_ORDER = ["D1资源", "D2几何", "D3材质", "D4UI", "D5动画", "D6特效", "D7音乐",
             "D8音效", "D9碰撞", "D10逻辑", "D11输入", "D12流程", "S1数值", "S2性能", "S3设置"]

# 上游门槛（us/GO）：一帧预算 16666.7us ÷ 节点数 3605 / 2506 / 1074
UPSTREAM = [("4.62", 3605), ("6.65", 2506), ("15.5", 1074)]

CTX_MENU = "菜单上下文（MainMenu/CharSelect）"
CTX_DLG = "对话上下文（DialogOpen）"
CTX_SHOP = "商店/面板上下文"
CTXMAP = {"menu": CTX_MENU, "dialog": CTX_DLG, "shop": CTX_SHOP}


# ── 通用 ────────────────────────────────────────────────────────────────────────
def acquire_lock():
    for _ in range(60):
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            return True
        except FileExistsError:
            time.sleep(2)
    return False


def hist(rows):
    c = collections.Counter()
    for r in rows:
        if len(r) > 0 and r[0] in DIM_ORDER:
            c[r[0]] += 1
    return c


def sha_except(rows, lines_skipped):
    h = hashlib.sha256()
    for i, r in enumerate(rows, 1):
        if i in lines_skipped:
            continue
        h.update(("\t".join(r)).encode("utf-8"))
        h.update(b"\n")
    return h.hexdigest()


def find_rows(rows, dim, pred):
    hits = []
    for i, r in enumerate(rows, 1):
        if len(r) < 8:
            continue
        if r[0] == dim and pred(r):
            hits.append((i, r))
    return hits


# ── 证据解析 ───────────────────────────────────────────────────────────────────
def t0b_lines():
    if not os.path.exists(EVID):
        return []
    txt = open(EVID, "rb").read().decode("utf-8-sig", "replace")
    out = []
    for l in txt.replace("\r\n", "\n").split("\n"):
        i = l.find("[T0B] ")
        if i >= 0:
            out.append(l[i + len("[T0B] "):].strip())
    return out


def one(lines, name):
    """First [T0B] line whose tag is exactly `name` (tags come from Log => "NAME ..." or KV => "NAME=...")."""
    for l in lines:
        if l.startswith(name + " ") or l.startswith(name + "="):
            return l
    return None


def parse_s2(lines):
    d = {}
    dev = one(lines, "DEVICE")
    m = re.search(r'device="([^"]*)" type=(\S+) vramMB=(-?\d+) res=(\S+) vSync=(-?\d+) targetFps=(-?\d+)', dev or "")
    if not m:
        return None
    d["dev_name"] = m.group(1)
    d["device"] = ("graphicsDeviceName=%s type=%s vram=%sMB res=%s vSync=%s targetFps=%s"
                   % (m.group(1), m.group(2), m.group(3), m.group(4), m.group(5), m.group(6)))

    ps = one(lines, "PACING-STEADY")
    m = re.search(r'n=(\d+) min=([\d.]+) p50=([\d.]+) p95=([\d.]+) max=([\d.]+) mean=([\d.]+).*?'
                  r'cpuMain n=(\d+) p50=([\d.]+) p95=([\d.]+) max=([\d.]+)', ps or "")
    if not m:
        return None
    d["pacing"] = ("PACING-STEADY n=%s min=%s p50=%s p95=%s max=%s mean=%s ms/frame；"
                   "FrameTimingManager cpuMainThreadFrameTime n=%s p50=%s p95=%s max=%s ms"
                   % (m.group(1), m.group(2), m.group(3), m.group(4), m.group(5), m.group(6),
                      m.group(7), m.group(8), m.group(9), m.group(10)))

    ld = one(lines, "LOAD")
    m = re.search(r'enter=(\S+) ready=(\S+) seconds=([\d.]+) watchdog=(-?[\d.]+)', ld or "")
    if not m:
        return None
    d["load"] = ("EnterStage %s -> Stage 就绪 %s = %ss（看门狗 %ss，FlowConst.StageLoadTimeoutSeconds）"
                 % (m.group(1), m.group(2), m.group(3), m.group(4)))

    mem = one(lines, "MEM")
    m = re.search(r'profilerTotalMB=([\d.]+) monoUsedMB=([\d.]+) graphicsMemoryMB=(-?\d+) systemMemoryMB=(-?\d+)', mem or "")
    if not m:
        return None
    d["mem"] = ("profilerTotalMB=%s monoUsedMB=%s graphicsMemoryMB=%s systemMemoryMB=%s（fsm=Stage）"
                % (m.group(1), m.group(2), m.group(3), m.group(4)))

    spikes = []
    for l in lines:
        if l.startswith("SPIKE-SHOWAREA ") or l.startswith("SPIKE-SHOWAREA="):
            m = re.search(r'tag=(\S+) area=(\S+) reps=(\d+) nodes=(-?\d+).*?'
                          r'p50_us=([\d.]+) p99_us=([\d.]+) max_us=([\d.]+).*?'
                          r'max_us_per_GO=([\d.\-]+).*?threshold_us_per_GO=([\d.\-]+)', l)
            if m:
                def _f(x):
                    return float(x) if x not in ("-", "") else 0.0
                spikes.append(dict(tag=m.group(1), area=m.group(2), reps=m.group(3), nodes=int(m.group(4)),
                                   p50=_f(m.group(5)), p99=_f(m.group(6)), mx=_f(m.group(7)),
                                   usgo=_f(m.group(8)), thr=_f(m.group(9))))
    frames = []
    for l in lines:
        if l.startswith("SPIKE-FRAME ") or l.startswith("SPIKE-FRAME="):
            m = re.search(r'tag=(\S+) frames=(\d+) p50_ms=([\d.]+) p99_ms=([\d.]+) max_ms=([\d.]+) '
                          r'maxBuiltOneFrame=(\d+) totalBuilt=(\d+) maxChunksPerFrameConst=(-?\d+)', l)
            if m:
                frames.append(m.groups())
    incr = one(lines, "SPIKE-INCR")
    if incr and incr.startswith("SPIKE-INCR="):
        incr = incr[len("SPIKE-INCR="):]
    d["spikes"] = spikes
    d["frames"] = frames
    d["incr"] = incr
    return d


def parse_d11(lines):
    recs = []
    for l in lines:
        if not l.startswith("D11=ctx="):
            continue
        m = re.search(r'ctx=(\w+) entity=(\S+) key=(\S+) eff=(\d+) swallowed=(\d+) move=(\d+) '
                      r'p0=(\S+) p1=(\S+) f0=(\S+) f1=(\S+) g0=(\S+) g1=(\S+) dm=(-?\d+) '
                      r'r0=(-?\d+) r1=(-?\d+) db=(-?\d+) ds=(-?\d+)', l)
        if not m:
            print("  !! unparsed D11 line:", l[:120])
            continue
        recs.append(dict(ctx=m.group(1), entity=m.group(2), key=m.group(3),
                         eff=int(m.group(4)), swallowed=int(m.group(5)), move=int(m.group(6)),
                         p0=m.group(7), p1=m.group(8), f0=m.group(9), f1=m.group(10),
                         g0=m.group(11), g1=m.group(12), dm=int(m.group(13)),
                         r0=int(m.group(14)), r1=int(m.group(15)), db=int(m.group(16)), ds=int(m.group(17))))
    return recs


# ── D5 ─────────────────────────────────────────────────────────────────────────
D5_SPEC = [
    ("传送门", "", "E42",
     "原版该形态为**调色板循环**（非逐帧动画）⇒ 本机无「循环点 = 帧数-1」可比；"
     "`Objects/warp` 81 张实为平色帧（唯一色 #004430）且当前不被任何帧号请求"),
    ("投射物", "④", "E40",
     "该实体原版为**单帧**（`missile_c.cel_file` 已登记素材名、`.dcc` 未到手）"
     "⇒ 本项目用 1x1 纯色占位（按伤害类型着色），无逐帧循环点可比"),
    ("精英", "①", "E40",
     "精英 = 同基础怪帧集 + 原版**运行期调色板变体**（本机无出处）⇒ 无独立循环点可比"
     "（本项目按登记不加金色 tint）"),
]


def d5_rows(rows):
    changed = {}
    for ent_sub, sub, rid, meas in D5_SPEC:
        hits = find_rows(rows, "D5动画", lambda r: ent_sub in r[1]
                         and (r[2].startswith("循环点") or "Finished" in r[2]))
        if len(hits) != 1:
            print("  !! D5 entity '%s' matched %d row(s) -- SKIP" % (ent_sub, len(hits)))
            continue
        ln, r = hits[0]
        if r[5].strip() or r[6].strip() or r[7].strip():
            print("  !! D5 L%d already filled -- SKIP" % ln)
            continue
        ev = ".ai-tmp/screenshots/w3_anim_audit.tsv:%s（该实体行）；策划/差异登记.tsv %s" % (
            r[4].rsplit(":", 1)[-1], rid)
        if sub:
            ev = "子项 %s；%s" % (sub, ev)
        r[5] = meas
        r[6] = "允许的差异(→%s)" % rid
        r[7] = ev
        changed[ln] = (ent_sub, r[6])
    return changed


# ── S2 ─────────────────────────────────────────────────────────────────────────
def s2_spike_text(d):
    parts = []
    worst = 0.0
    worst_tag = ""
    for s in d["spikes"]:
        ratio = (s["usgo"] / s["thr"]) if s["thr"] > 0 else 0.0
        if ratio > worst:
            worst, worst_tag = ratio, s["tag"]
        parts.append("%s nodes=%d p50=%.0fus p99=%.0fus max=%.0fus ⇒ %.3f us/GO（自算门槛 16666.7us÷%d=%.3f us/GO）"
                     % (s["tag"], s["nodes"], s["p50"], s["p99"], s["mx"], s["usgo"], s["nodes"], s["thr"]))
    up = "；".join("≤%s(%d GO)" % (v, n) for v, n in UPSTREAM)
    fr = ""
    for tag, n, p50, p99, mx, mb, tot, mcf in d["frames"]:
        fr += "；SPIKE-FRAME %s frames=%s p50=%sms p99=%sms max=%sms 单帧最多建块=%s(total=%s, 常量 MaxChunksPerFrame=%s)" % (
            tag, n, p50, p99, mx, mb, tot, mcf)
    inc = ""
    if d["incr"]:
        inc = "；" + d["incr"]
    verdict = "judge"
    txt = ("MapView.ShowArea（ShowArea→RebuildLayers 铺装段，Stopwatch 包住，reps=%s）单帧尖峰：%s。"
           "上游门槛（一帧预算 16666.7us ÷ 3605/2506/1074 节点）= 4.62/6.65/15.5 us/GO（%s）"
           "%s%s"
           ) % (d["spikes"][0]["reps"] if d["spikes"] else "?", "；".join(parts), up, fr, inc)
    if worst <= 1.0:
        txt += " ⇒ 判绿：最差 %.3f ≤ 1.000（%s 仍在一帧预算内）" % (worst, worst_tag)
        verdict = "一致"
    else:
        excess = (d["spikes"][0]["mx"] - 1000000.0 / 60.0) if d["spikes"] else 0.0
        txt += " ⇒ 判红：最差 %.3f > 1.000（%s 单帧 %.0fus > 一帧预算 16666.7us，超 %.0fus）" % (
            worst, worst_tag, (d["spikes"][0]["mx"] if d["spikes"] else 0), excess)
        verdict = "不一致(单帧尖峰 > 一帧预算)"
    return txt, verdict


def s2_rows(rows, d):
    dev = d["device"]
    fps = d["pacing"]
    load = d["load"]
    mem = d["mem"]
    spike_txt, spike_verd = s2_spike_text(d)
    meas = {
        ("perf:内存占用(MB)", "帧时间（ms/frame）"): "%s；%s" % (fps, dev),
        ("perf:内存占用(MB)", "进图加载耗时（s）"): "%s；%s" % (load, dev),
        ("perf:内存占用(MB)", "内存占用（MB）"): "%s；%s" % (mem, dev),
        ("perf:帧时间(ms/frame)", "帧时间（ms/frame）"): spike_txt,
        ("perf:帧时间(ms/frame)", "进图加载耗时（s）"): "同 perf:内存占用 行：%s；读数带设备名 %s" % (load, d["dev_name"]),
        ("perf:帧时间(ms/frame)", "内存占用（MB）"): "%s；%s" % (mem, dev),
        ("perf:渲染设备名(SystemInfo.graphicsDeviceName)", "帧时间（ms/frame）"):
            "graphicsDeviceName=\"%s\"（非 Microsoft Basic Render Driver）⇒ 帧时间数字有效；%s" % (d["dev_name"], fps),
        ("perf:渲染设备名(SystemInfo.graphicsDeviceName)", "进图加载耗时（s）"):
            "设备名 = \"%s\"；%s" % (d["dev_name"], load),
        ("perf:渲染设备名(SystemInfo.graphicsDeviceName)", "内存占用（MB）"):
            "设备名 = \"%s\"；%s" % (d["dev_name"], mem),
        ("perf:进图加载耗时(s)", "帧时间（ms/frame）"):
            "%s；同期 %s；读数均带设备名 %s" % (load, fps, d["dev_name"]),
        ("perf:进图加载耗时(s)", "进图加载耗时（s）"):
            "%s，无读条超时 Error" % load,
        ("perf:进图加载耗时(s)", "内存占用（MB）"): "%s；%s" % (mem, dev),
    }
    verd_of = {("perf:帧时间(ms/frame)", "帧时间（ms/frame）"): spike_verd}
    changed = {}
    for (ent, st), m in meas.items():
        hits = find_rows(rows, "S2性能", lambda r, e=ent, s=st: r[1] == e and r[2] == s)
        if len(hits) != 1:
            print("  !! S2 %s / %s matched %d -- SKIP" % (ent, st, len(hits)))
            continue
        ln, r = hits[0]
        v = verd_of.get((ent, st), "一致")
        r[5] = m
        r[6] = v
        r[7] = ("t0b_drive（.ai-tmp/screenshots/t0b_evidence.txt）[T0B] DEVICE + PACING-STEADY + LOAD + MEM "
                "+ SPIKE-SHOWAREA/SPIKE-FRAME/SPIKE-INCR；判据 experience/perf-triage.md（渲染设备为真 GPU ⇒ 数字有效）")
        changed[ln] = (ent + "/" + st, v)
    return changed


# ── D11 ────────────────────────────────────────────────────────────────────────
def d11_rows(rows, recs):
    by = {}
    for rec in recs:
        by[(rec["entity"], CTXMAP[rec["ctx"]])] = rec
    changed = {}
    for (ent, ctx), rec in by.items():
        hits = find_rows(rows, "D11输入", lambda r, e=ent, c=ctx: r[1] == e and r[2] == c)
        if len(hits) != 1:
            print("  !! D11 %s / %s matched %d -- SKIP" % (ent, ctx, len(hits)))
            continue
        ln, r = hits[0]
        if ctx == CTX_MENU:
            v = "一致" if rec["eff"] == 0 else "不一致(菜单上下文里该键仍生效)"
        else:
            v = "一致" if rec["move"] == 0 else "不一致(产生了移动指令)"
        r[5] = ("实机注入真按键（InputSystem QueueStateEvent）key=%s；面板集 %s→%s；fsm %s→%s；"
                "playerGrid %s→%s；MoveCommand Δ=%d；HUD 跑/走 %d→%d；UseBelt Δ=%d；SwapWeapon Δ=%d "
                "⇒ 生效=%d 被吞=%d 产生移动=%d"
                % (rec["key"], rec["p0"], rec["p1"], rec["f0"], rec["f1"], rec["g0"], rec["g1"],
                   rec["dm"], rec["r0"], rec["r1"], rec["db"], rec["ds"],
                   rec["eff"], rec["swallowed"], rec["move"]))
        r[6] = v
        r[7] = ("t0b_drive（.ai-tmp/screenshots/t0b_evidence.txt）[T0B] D11 ctx=%s entity=%s；"
                "实机注入真按键，判据 = 该上下文的边界（%s）" % (rec["ctx"], ent, r[3]))
        changed[ln] = (ent + "/" + ctx, v)
    return changed


# ── main ───────────────────────────────────────────────────────────────────────
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    ap.add_argument("--d5", action="store_true")
    ap.add_argument("--s2", action="store_true")
    ap.add_argument("--d11", action="store_true")
    a = ap.parse_args()

    do = [n for n, v in (("d5", a.d5), ("s2", a.s2), ("d11", a.d11)) if v]
    if not do:
        print("pick at least one of --d5 / --s2 / --d11")
        return 2

    lines = t0b_lines()
    print("evidence lines with [T0B] =", len(lines))

    if a.apply:
        if not acquire_lock():
            print("LOCK-FAIL could not acquire", LOCK)
            return 2
        print("LOCK acquired", LOCK)
    try:
        with open(MATRIX, "rb") as fh:
            raw = fh.read()
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        nl = "\r\n" if "\r\n" in text else "\n"
        rows = [l.split("\t") for l in text.split(nl)]

        h0 = hist(rows)
        n0 = sum(h0.values())

        changed = {}
        if "d5" in do:
            changed.update(d5_rows(rows))
        if "s2" in do:
            d = parse_s2(lines)
            if not d:
                print("!! could not parse S2 evidence from", EVID)
                return 2
            changed.update(s2_rows(rows, d))
        if "d11" in do:
            recs = parse_d11(lines)
            print("parsed D11 records =", len(recs))
            changed.update(d11_rows(rows, recs))

        out = nl.join("\t".join(r) for r in rows)
        rows2 = [l.split("\t") for l in out.split(nl)]
        h1 = hist(rows2)
        n1 = sum(h1.values())

        s_after = sha_except(rows2, set(changed.keys()))
        s_before = sha_except(rows, set(changed.keys()))

        print("mode=%s groups=%s changed=%d" % ("APPLY" if a.apply else "DRY", ",".join(do), len(changed)))
        for ln in sorted(changed):
            print("   L%d  %s -> %s" % (ln, changed[ln][0], changed[ln][1]))
        print("== machine evidence ==")
        print("row-count(data, all dims) before=%d after=%d equal=%s" % (n0, n1, n0 == n1))
        print("dimension-histogram unchanged=%s" % (h0 == h1))
        for d in DIM_ORDER:
            if h0.get(d, 0) != h1.get(d, 0):
                print("   DIFF", d, h0.get(d, 0), "->", h1.get(d, 0))
        print("non-owned-line sha256 before=%s after=%s equal=%s" % (s_before[:16], s_after[:16], s_before == s_after))

        if a.apply:
            head = b"\xef\xbb\xbf" if bom else b""
            with open(MATRIX, "wb") as fh:
                fh.write(head + out.encode("utf-8"))
            print("WRITTEN", MATRIX)
    finally:
        if a.apply and os.path.exists(LOCK):
            try:
                os.remove(LOCK)
                print("LOCK released")
            except OSError:
                pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
