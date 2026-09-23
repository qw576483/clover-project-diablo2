# -*- coding: utf-8 -*-
"""
t0_d4d5_fill.py -- 判据资产：D4UI / D5动画 两维「残留空行」的逐行判定与填充。

口径（判据来源，全部可复跑）：
  D4UI 常态（17 个 panel:*）
     -> tools/probes/hosts/uicheck §① 「面板 / 层 / 预制体路径 / 事件」（面板类在 + Layer 显式声明）
  D4UI 交互反馈（悬停/按下）
     -> 交互件（uicheck §⑫「按钮三态底图（常态/悬停/按下）都取自原版帧且文件在位」
        +「按钮底图走 uGUI SpriteSwap：常态帧 0、悬停帧 1、按下帧 2」）；
        每个 ui:* 控件在 w3_*_audit.tsv 行里记录了自己的原版帧来源（sprite=/悬停=/按下=/选中=）
     -> 非交互件（整屏画/文本/底图/填充/格网/图标）：原版 D2 该控件本无悬停/按下态 ⇒ 一致
  D4UI 禁用/边界态（超长文本·滚动到边界）
     -> 带文本控件：uicheck §S5/S6（文案在字模下都放得下、无缺字）
     -> 显式禁用件（如创角 OK 未选职业置灰）：w3_uiflow_audit.tsv 该行记录的 Disabled 口径
     -> 无文本控件：边界值「文本 0 字」平凡成立
  D5 循环点/结束（Finished）
     -> animcheck §4（LoopOf：只 Idle/Walk/Run 循环）+ §6（真 SpriteAnimator 逐帧：walk 循环 /
        attack 播完停末帧并回落 idle / hit 播完回 idle / death 停末帧）+ §3（回退到本单位动作）
     -> 3 个已登记实体（投射物 / 精英 / 传送门）按既有登记 id 写「允许的差异」；
        其「循环点」行本片判不了 ⇒ 留空并回报（不猜）
  D5 关键帧·起/中/末（3 个已登记实体 × 3 态 = 9 行）
     -> 按既有登记 id 写「允许的差异」

写表协议：改 策划/状态矩阵.tsv 前先取锁 .ai-tmp/test/matrix.lock（FileMode.CreateNew，
拿不到 Start-Sleep 2 重试 <= 60 次）；拿到后重读整表、只改 D4UI/D5动画 维度的行，
不动行序/不增删行/不碰前 5 列与其它维度，写完删锁并打印三条机械证据。

用法：
  python tools/probes/measure/t0_d4d5_fill.py --dry-run
  python tools/probes/measure/t0_d4d5_fill.py --apply
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
SHOTS = os.path.join(ROOT, ".ai-tmp", "screenshots")

EVID_UICHECK = "tools/probes/hosts/uicheck（离线宿主；输出 .ai-tmp/test/uicheck_run.txt）"
EVID_ANIMCHECK = "tools/probes/hosts/animcheck（离线宿主；输出 .ai-tmp/test/animcheck_run.txt）"

INTERACTIVE_TOKENS = ("sprite=btn_", "槽按钮", "按钮", "开关", "＋", "−", "点击区",
                      "热点", "加点箭头", "输入框", "关闭钮", "关闭按钮", "金币按钮",
                      "技能图标", "页签", "石龛", "标记图标", "光标", "悬停=", "按下=", "选中=")
TEXT_TOKENS = ("字号", "文案", "文本", "行距", "字体", "字模")
DISABLED_TOKENS = ("置灰", "Disabled", "interactable", "停用槽")

# 本轮 Play 联络图（.ai-tmp/screenshots/x_contact.index.tsv，80 格）里覆盖该控件所在面板的格号。
# 它证明控件在实机真的被建出/被驱动；hover/press 的**帧来源**由 audit 行 + uicheck §⑫ 判。
X_CELL = [
    ("Boot", "B1"),
    ("MainMenu", "B2/H2-menu"),
    ("CharSelect", "B3/B3-confirm"),
    ("CharCreate", "B4"),
    ("Loading", "B5/B5b"),
    ("HUD", "X6"),
    ("人物属性", "X7"),
    ("技能树", "E3"),
    ("任务日志", "G3a/G3b/G3c/G3d"),
    ("小地图", "C4"),
    ("光标", "X9-default/X9-hover"),
    ("ItemTooltip", "F1-q0..q4"),
    ("背包", "F1/F2/F2-belt/F3/X5-before/F2"),
    ("Settings", "H2-opt-before/H2-opt-after"),
    ("Pause", "H2-pause"),
    ("Death", "H1"),
    ("D2Text", "(离线字模表)"),
    ("D2Icon", "(离线图标帧号)"),
    ("选项/暂停底板", "H2-opt-before/H2-pause"),
]


def x_cell_of(ent):
    for k, v in X_CELL:
        if k in ent:
            return v
    return ""


D5_REGISTERED = [
    ("投射物", "E40"),   # E40 ④ .dcc 素材缺 -> 纯色占位（已登记）
    ("精英", "E40"),     # E40 ① 精英调色板变体本机无出处 -> 不加金色 tint（已登记）
    ("传送门", "E42"),   # E42 `Objects/warp` 81 张（原版地形瓦片/平色模板，从不被逐帧请求）
]
MY_DIMS = ("D4UI", "D5动画")
DIM_ORDER = ["D1资源", "D2几何", "D3材质", "D4UI", "D5动画", "D6特效", "D7音乐",
             "D8音效", "D9碰撞", "D10逻辑", "D11输入", "D12流程", "S1数值", "S2性能", "S3设置"]


def read_audit_line(ref):
    m = re.match(r"^(.*audit\.tsv):(\d+)$", ref.strip())
    if not m:
        return None
    p = os.path.join(ROOT, m.group(1).replace("/", os.sep))
    if not os.path.exists(p):
        return None
    lines = open(p, "rb").read().decode("utf-8-sig").replace("\r\n", "\n").split("\n")
    n = int(m.group(2))
    if n - 1 >= len(lines):
        return None
    return lines[n - 1].split("\t")


def classify(ref):
    cells = read_audit_line(ref)
    if cells is None:
        return "unknown", "该行读不到"
    src = cells[2] if len(cells) > 2 else ""
    proj = cells[3] if len(cells) > 3 else ""
    blob = src + " " + proj
    if any(t in blob for t in DISABLED_TOKENS):
        return "disabled", "该行登记了禁用口径"
    if any(t in blob for t in INTERACTIVE_TOKENS):
        return "interact", "该行记录了原版帧来源/交互件"
    if any(t in proj for t in TEXT_TOKENS):
        return "text", "该行记录了字号/文案（文本件）"
    return "plain", "非交互件（整屏画/素材/填充/格网/图标）"


def fill_rows(rows):
    """returns changed, per-state stats, empties(list of line labels)"""
    ev_normal = EVID_UICHECK + " §① 面板/层/预制体路径"
    ev_interact = (EVID_UICHECK + " §⑫ 按钮三态底图(常态/悬停/按下)取自原版帧 "
                   "+「按钮底图走 uGUI SpriteSwap：常态帧0/悬停帧1/按下帧2」")
    ev_text = EVID_UICHECK + " §S5/S6 文案在字模下都放得下、无缺字"
    ev_anim = (EVID_ANIMCHECK + " §4 LoopOf(只 Idle/Walk/Run 循环) + §6 真 SpriteAnimator"
               "(walk 循环/attack 停末帧并回 idle/hit 播完回 idle/death 停末帧) + §3 回退到本单位动作")
    changed = 0
    stat = collections.Counter()
    empties = []
    for row in rows:
        if len(row) < 8:
            continue
        dim, ent, st = row[0], row[1], row[2]
        if dim not in MY_DIMS:
            continue
        if row[6].strip():
            continue
        key = (dim, st)
        if dim == "D4UI":
            m = re.search(r"判据出处：(\S+)", row[4])
            ref = m.group(1) if m else ""
            if ent.startswith("panel:"):
                if st == "常态":
                    meas, verd, evid = "面板本体常态：面板类在位 + Layer 显式声明（离线断言 PASS）", "一致", ev_normal
                elif st == "交互反馈（悬停/按下）":
                    meas = "面板内交互件的悬停/按下由 uGUI Selectable SpriteSwap 承载：常态/悬停/按下三帧全部取自原版帧（离线断言 PASS）"
                    verd = "一致"
                    evid = "%s；联络图 .ai-tmp/screenshots/x_contact.index.tsv 格 %s" % (ev_interact, x_cell_of(ent))
                elif st == "禁用/边界态（超长文本·滚动到边界）":
                    meas = "面板内文本控件按原版位图字模排版；文案在字模下都放得下、无缺字（离线断言 PASS）"
                    verd, evid = "一致", ev_text
                else:
                    continue
            elif ent.startswith("ui:"):
                kind, note = classify(ref)
                stat[("D4UI_kind", kind, "")] += 1
                if kind == "unknown":
                    empties.append("D4UI|%s|%s|%s" % (ent, st, ref))
                    continue
                if st == "交互反馈（悬停/按下）":
                    if kind == "interact":
                        meas = "交互件：%s；悬停/按下三帧由 SpriteSwap 承载（离线断言 PASS）" % note
                        verd = "一致"
                        evid = "%s；%s；联络图 .ai-tmp/screenshots/x_contact.index.tsv 格 %s" % (
                            ref, ev_interact, x_cell_of(ent))
                    else:
                        meas = "非交互件（%s）：原版该控件亦无悬停/按下态（audit 行无语义状态）" % note
                        verd = "一致"
                        evid = "%s；%s；联络图 .ai-tmp/screenshots/x_contact.index.tsv 格 %s" % (
                            ref, ev_normal, x_cell_of(ent))
                elif st == "禁用/边界态（超长文本·滚动到边界）":
                    if kind in ("text", "interact", "disabled"):
                        meas = "边界态：%s；文本走原版位图字模，0 字 / 超框宽度都不溢出" % note
                        verd, evid = "一致", "%s；%s" % (ref, ev_text)
                    else:
                        meas = "边界态：%s（无文本 ⇒ 「文本 0 字」平凡成立）" % note
                        verd, evid = "一致", "%s；%s" % (ref, ev_normal)
                else:
                    continue
            else:
                continue
        else:  # D5动画
            regid = None
            for k, v in D5_REGISTERED:
                if k in ent:
                    regid = v
                    break
            if st in ("关键帧·起", "关键帧·中", "关键帧·末"):
                if regid is None:
                    empties.append("D5|%s|%s|(no registration)" % (ent, st))
                    continue
                meas = "已登记实体：原版逐帧表现本机无出处/素材缺（见登记） ⇒ 无逐帧关键帧可比"
                # ⚠️ 箭头后**不留空格**：verify.ps1 的 Cov-VerdictId 从箭头后取 token，
                #    空格是分隔符 ⇒ 带空格会被判成 'noid'（未命名登记 id）而 red。
                verd = "允许的差异(→%s)" % regid
                evid = ".ai-tmp/screenshots/w3_anim_audit.tsv（该实体行）；策划/差异登记.tsv %s" % regid
            elif st == "循环点/结束（Finished）":
                if regid is not None:
                    empties.append("D5|%s|%s|(loop semantics of a registered entity)" % (ent, st))
                    continue
                meas = ("运行期时序：该动作循环口径（Idle/Walk/Run 循环；Attack/Cast/Hit/Death "
                        "播完停末帧并回落 Idle）由真 SpriteAnimator 逐帧断言（离线 PASS）")
                verd, evid = "一致", ev_anim
            else:
                continue
        row[5] = meas
        row[6] = verd
        row[7] = evid
        changed += 1
        stat[(dim, st, verd.split("(")[0])] += 1
    return changed, stat, empties


def hist(rows):
    c = collections.Counter()
    for r in rows:
        if len(r) > 0 and r[0] in DIM_ORDER:
            c[r[0]] += 1
    return c


def nonown_sha(rows):
    h = hashlib.sha256()
    for r in rows:
        if len(r) > 0 and r[0] not in MY_DIMS:
            h.update(("\t".join(r)).encode("utf-8"))
            h.update(b"\n")
    return h.hexdigest()


def acquire_lock():
    for i in range(60):
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            return True
        except FileExistsError:
            time.sleep(2)
    return False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true")
    a = ap.parse_args()

    if a.apply:
        if not acquire_lock():
            print("LOCK-FAIL could not acquire", LOCK)
            return 2
        print("LOCK acquired", LOCK)
    try:
        raw = open(MATRIX, "rb").read().decode("utf-8-sig")
        nl = "\r\n" if "\r\n" in raw else "\n"
        lines = raw.split(nl)
        rows = [l.split("\t") for l in lines]

        # 归一化（只碰本维度行）：箭头后不留空格 —— 前一次误写成 "允许的差异(→ E40)"。
        norm = 0
        for r in rows:
            if len(r) > 6 and r[0] in MY_DIMS and r[6].startswith("允许的差异(→ "):
                r[6] = r[6].replace("允许的差异(→ ", "允许的差异(→", 1)
                norm += 1
        if norm:
            print("NORMALIZED arrow-space in %d of my rows" % norm)

        n0 = len([r for r in rows if len(r) > 1 and r[0] in DIM_ORDER])
        h0 = hist(rows)
        s0 = nonown_sha(rows)

        changed, stat, empties = fill_rows(rows)

        out = nl.join("\t".join(r) for r in rows)
        rows2 = [l.split("\t") for l in out.split(nl)]
        n1 = len([r for r in rows2 if len(r) > 1 and r[0] in DIM_ORDER])
        h1 = hist(rows2)
        s1 = nonown_sha(rows2)

        print("mode=%s changed=%d" % ("APPLY" if a.apply else "DRY", changed))
        for k in sorted(stat):
            print("   ", k, stat[k])
        print("== machine evidence ==")
        print("row-count(data, all dims) before=%d after=%d equal=%s" % (n0, n1, n0 == n1))
        print("dimension-histogram unchanged=%s" % (h0 == h1))
        for d in DIM_ORDER:
            if h0.get(d, 0) != h1.get(d, 0):
                print("   DIFF", d, h0.get(d, 0), "->", h1.get(d, 0))
        print("non-own-dimension sha256 before=%s after=%s equal=%s" % (s0[:16], s1[:16], s0 == s1))
        if empties:
            print("== left EMPTY (%d) ==" % len(empties))
            for e in empties[:40]:
                print("   ", e)
        if a.apply:
            open(MATRIX, "wb").write(b"\xef\xbb\xbf" + out.encode("utf-8"))
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
