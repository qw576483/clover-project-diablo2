# -*- coding: utf-8 -*-
"""把原版传送台物件（`Objects.txt` Id=119 / `Name=Waypoint` / `Token=wp`）的本体动画解成 PNG 落进工程。

**包内真实路径**（按名 `SFileExtractFile` 取，实测存在）：

    动画定义  data\\global\\objects\\wp\\COF\\wp<模式>HTH.COF     （NU / OP / ON）
    本体层    data\\global\\objects\\wp\\TR\\wpTRlit<模式>HTH.dcc  （TR：石台本体）
    覆盖层    data\\global\\objects\\wp\\S1\\wpS1lit<模式>HTH.dcc  （S1：发光覆盖层，仅 OP/ON 有）
    调色板    data\\global\\palette\\units\\Pal.dat             （768 字节，每项 B,G,R）

命名法出处 = 包内这些文件名本身（按名取到并解码成功即为证）；`.dcc` 解码器 = `tools/d2codec/dcc.py`。

**合并与落位**（原版数值：不缩放、不重采样、不调色）：
  · 每层一个方向包围盒（`dcc.py` 的 `Direction.box`），原点 (0,0) = 物件脚下；
  · 同一帧把各层按 COF 的绘制顺序叠在一张画布上，画布 = 各层包围盒的并集；
  · 画布再向下补一段**透明垫片**，使原点落在画布底边上方 40 px —— 40 px = 一格菱形半高
    （`GameConst.IsoHalfH` = 0.5 世界单位，物件层按 80 px/单位解释）。
    这是为了对齐既有地图渲染口径：物件层把**图像底边**贴在格中心下方半格（前角），
    补垫片后原点正好落在格中心。

产出：`<out>/waypoint/<帧号三位>.png` + `manifest.json`（逐帧尺寸 / sha256 + 每条来源件的包内路径 / 字节数 / sha256）。

用法：
    python tools/d2codec/export_waypoint.py [--out <Resources/Clover/D2 根>] [--mpq-dir <含 D2data.mpq 的目录>]
"""
import argparse
import hashlib
import json
import os

try:
    from . import cof as cofmod
    from . import dcc as dccmod
    from . import pngio
    from . import storm
except ImportError:
    import cof as cofmod
    import dcc as dccmod
    import pngio
    import storm

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

#: 物件 token（`Objects.txt` Id=119 的 `Token` 列）。
TOKEN = 'wp'
#: 武器类别段（COF 里逐层的 `weapon_class`；物件一律 `hth`）。
WEAPON_CLASS = 'HTH'
#: 装备段（物件只有 `lit` 这一种变体）。
EQUIP = 'lit'
#: 模式：`ON` = 传送台"已激活"的常亮态（城镇里的传送台就是这个状态）。
MODE = 'ON'
#: `Objects.txt` 里 ON 模式对应的列后缀（`FrameCnt2` / `CycleAnim2`）。
MODE_INDEX = 2

#: 单位调色板（D2 单位 DCC 用的那一份；`.dat` = 768 字节，每项 B,G,R）。
PALETTE_REL = 'data/global/palette/units/Pal.dat'
#: 官方物件表（帧数从它读，不写死）。
OBJECTS_REL = 'data/global/excel/objects.txt'

#: 一格菱形的半高（px）：`GameConst.IsoHalfH` = 0.5 世界单位 × 80 px/单位。
HALF_CELL_PX = 40


def rel(*parts):
    return '\\'.join(parts)


def summary(raw, path):
    """一条来源件的登记（包内路径 / 字节数 / sha256）。"""
    return {'path': path, 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


def delta_profile(objects_bytes, col_index):
    """全表 `FrameDelta<col>` 的分布（只统计非空；`0` = 该模式没启用）。

    这是**交叉验证读数**：非零值是一档一档的"速度刻度"（不是连续时间），
    与"换算成每帧秒数后取倒数得帧率"这种读法吻合。
    """
    lines = objects_bytes.decode('latin1').splitlines()
    hdr = lines[0].split('\t')
    i = hdr.index('FrameDelta%d' % col_index)
    hist = {}
    zero = 0
    for ln in lines[1:]:
        c = ln.split('\t')
        if len(c) <= i:
            continue
        v = c[i].strip()
        if v == '':
            continue
        iv = int(v)
        if iv == 0:
            zero += 1
            continue
        hist[iv] = hist.get(iv, 0) + 1
    return zero, hist


def read_mode_frame_count(objects_bytes):
    """`Objects.txt` 里 Id=119 / Token=wp 那一行的 `FrameCnt2` / `FrameDelta2`。

    `FrameCnt2` = ON 模式实际播放的帧数；`FrameDelta2` = 该模式的**每帧推进量**（换算公式见
    `frame_rate_of`）。
    """
    lines = objects_bytes.decode('latin1').splitlines()
    hdr = lines[0].split('\t')
    i_id, i_tok = hdr.index('Id'), hdr.index('Token')
    i_cnt = hdr.index('FrameCnt%d' % MODE_INDEX)
    i_delta = hdr.index('FrameDelta%d' % MODE_INDEX)
    i_cycle = hdr.index('CycleAnim%d' % MODE_INDEX)
    for ln in lines[1:]:
        c = ln.split('\t')
        if len(c) <= i_delta:
            continue
        if c[i_id] == '119' and c[i_tok] == TOKEN:
            return int(c[i_cnt]), int(c[i_delta]), int(c[i_cycle])
    raise ValueError('objects.txt 里找不到 Id=119 / Token=%s 的行' % TOKEN)


def slot_crosscheck(handle, objects_bytes):
    """**列序自证**：`Objects.txt` 的 `FrameCnt0/1/2` 必须与三个模式各自的 `.COF` 帧数对上。

    这同时钉死"下标 2 = ON"：`FrameCnt0` 对 `NU`、`FrameCnt1` 对 `OP`、`FrameCnt2` 对 `ON`
    （模式下标 ↔ 代号见参考实现的 `StaticObjectMode.cs`：0=NU / 1=OP / 2=ON）。
    `ON` 的 `CycleAnim2 = 1` -> 循环播放**前 FrameCnt2 帧**（`.COF` 里给的是总帧数，可以更多）。
    """
    lines = objects_bytes.decode('latin1').splitlines()
    hdr = lines[0].split('\t')
    i_id, i_tok = hdr.index('Id'), hdr.index('Token')
    row = None
    for ln in lines[1:]:
        c = ln.split('\t')
        if len(c) > hdr.index('CycleAnim2') and c[i_id] == '119' and c[i_tok] == TOKEN:
            row = c
            break
    if row is None:
        raise ValueError('objects.txt 里找不到 Id=119 / Token=%s 的行' % TOKEN)

    out = []
    for i, code in ((0, 'NU'), (1, 'OP'), (2, 'ON')):
        cnt = int(row[hdr.index('FrameCnt%d' % i)])
        cycle = int(row[hdr.index('CycleAnim%d' % i)])
        cof_path = rel('data', 'global', 'objects', TOKEN, 'COF',
                       '%s%s%s.COF' % (TOKEN, code, WEAPON_CLASS))
        raw = storm.read_file(handle, cof_path)
        if not raw:
            out.append({'slot': i, 'code': code, 'frameCnt': cnt, 'cycleAnim': cycle,
                        'cofFrames': None, 'note': '该模式的 .COF 不在包内'})
            continue
        c = cofmod.parse(raw, cof_path)
        out.append({'slot': i, 'code': code, 'frameCnt': cnt, 'cycleAnim': cycle,
                    'cofFrames': c.frames_per_dir,
                    # 游戏播放的帧 = 表里的 `FrameCnt`（`CycleAnim=1` 时循环前 FrameCnt 帧；
                    # `CycleAnim=0` 时一次播完 FrameCnt 帧）⇒ `.COF` 必须有**至少**这么多帧。
                    'played': cnt,
                    'match': c.frames_per_dir >= cnt})
    return out


def frame_rate_of(frame_delta):
    """`FrameDelta` → 每秒播放帧数。

    <para>换算出处（两条都对上才用）：① 参考实现的 `frameDuration = 256/25/FrameDelta`
    （秒/帧，`ObjectInfo.cs:109`）；② 同实现里 `frame = AnimationTime * frameCount / (frameCount *
    frameDuration)`（`StaticObjectRenderer.cs:39`）-> 逐帧时长**就是** `frameDuration`
    -> **帧率 = 1 / frameDuration = 25 * FrameDelta / 256**。值越大 = 帧率越高（越快）。</para>
    """
    if frame_delta <= 0:
        raise ValueError('FrameDelta = %d（<=0）-> 算不出帧率（原版 0 = 该模式没启用）' % frame_delta)
    return 25.0 * frame_delta / 256.0


def load_layers(handle):
    """读 COF 并解出该模式的各层 DCC（按 COF 的绘制顺序返回）。"""
    cof_rel = rel('data', 'global', 'objects', TOKEN, 'COF',
                  '%s%s%s.COF' % (TOKEN, MODE, WEAPON_CLASS))
    cof_bytes = storm.read_file(handle, cof_rel)
    if not cof_bytes:
        raise OSError('包内没有 %s' % cof_rel)
    c = cofmod.parse(cof_bytes, cof_rel)

    order = c.draw_order(0, 0)
    layers = []
    for idx in order:
        comp = c.layers[idx].component_code
        dcc_rel = rel('data', 'global', 'objects', TOKEN, comp,
                      '%s%s%s%s%s.dcc' % (TOKEN, comp, EQUIP, MODE, WEAPON_CLASS))
        raw = storm.read_file(handle, dcc_rel)
        if not raw:
            raise OSError('包内没有 %s' % dcc_rel)
        d = dccmod.parse(raw, dcc_rel)
        if len(d.directions) != 1:
            raise ValueError('%s：方向数 = %d（本物件实测为 1）' % (dcc_rel, len(d.directions)))
        layers.append({'component': comp, 'raw': raw, 'rel': dcc_rel, 'dir': d.directions[0]})
    return cof_rel, cof_bytes, c, layers


def main(argv=None):
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(REPO_ROOT, 'client', 'Assets', 'Resources',
                                                  'Clover', 'D2'))
    ap.add_argument('--mpq-dir', default=os.path.join(REPO_ROOT, '原版资源', '_mpq_incoming'))
    args = ap.parse_args(argv)

    mpq = os.path.join(args.mpq_dir, 'D2data.mpq')
    if not os.path.exists(mpq):
        raise OSError('找不到 %s' % mpq)
    tmp = os.path.join(REPO_ROOT, '.ai-tmp', 'test', 'export-waypoint-tmp')
    os.makedirs(tmp, exist_ok=True)
    storm.set_work_dir(tmp)

    handle = storm.open_archive(mpq, patch='Patch_D2.mpq')
    try:
        pal_raw = storm.read_file(handle, PALETTE_REL)
        if not pal_raw:
            raise OSError('包内没有 %s' % PALETTE_REL)
        pal_tmp = os.path.join(tmp, 'units-Pal.dat')
        with open(pal_tmp, 'wb') as fh:
            fh.write(pal_raw)
        palette = dccmod.read_pl2(pal_tmp)

        objs_raw = storm.read_file(handle, OBJECTS_REL)
        if not objs_raw:
            raise OSError('包内没有 %s' % OBJECTS_REL)
        frame_cnt, frame_delta, cycle = read_mode_frame_count(objs_raw)
        fps = frame_rate_of(frame_delta)
        delta_zero, delta_hist = delta_profile(objs_raw, MODE_INDEX)
        slots = slot_crosscheck(handle, objs_raw)

        cof_rel, cof_bytes, c, layers = load_layers(handle)

        left = min(l['dir'].box.left for l in layers)
        top = min(l['dir'].box.top for l in layers)
        right = max(l['dir'].box.left + l['dir'].box.width for l in layers)
        bottom = max(l['dir'].box.top + l['dir'].box.height for l in layers)
        box_w, box_h = right - left, bottom - top
        pad = HALF_CELL_PX - (box_h + top)          # 原点距画布底边 = box_h + top
        if pad < 0:
            raise ValueError('并集框算出的垫片为负（%d）' % pad)
        canvas_h = box_h + pad

        out_dir = os.path.join(args.out, 'Objects', 'waypoint')
        os.makedirs(out_dir, exist_ok=True)
        files = []
        for fi in range(frame_cnt):
            buf = bytearray(box_w * canvas_h * 4)
            for li in c.draw_order(0, fi):
                comp = c.layers[li].component_code
                lay = next(l for l in layers if l['component'] == comp)
                dr = lay['dir']
                rgba = dccmod.frame_rgba(dr.frames[fi], palette)
                ox = dr.box.left - left
                oy = dr.box.top - top
                for y in range(dr.box.height):
                    src = y * dr.box.width * 4
                    dst = ((oy + y) * box_w + ox) * 4
                    for x in range(dr.box.width):
                        if rgba[src + x * 4 + 3] == 0:
                            continue
                        buf[dst + x * 4:dst + x * 4 + 4] = rgba[src + x * 4:src + x * 4 + 4]
            name = '%03d.png' % fi
            path = os.path.join(out_dir, name)
            n = pngio.write_rgba(path, box_w, canvas_h, bytes(buf))
            files.append({'file': name, 'w': box_w, 'h': canvas_h, 'bytes': n,
                          'sha256': hashlib.sha256(open(path, 'rb').read()).hexdigest()})

        manifest = {
            'object': {'name': 'Waypoint', 'id': 119, 'token': TOKEN},
            'mode': {
                'name': MODE, 'index': MODE_INDEX, 'frames': frame_cnt,
                'cycleAnim': cycle,
                # 帧率出处链（缺一条就不用这个值）：
                #   ① 官方 `Objects.txt` 该行 `FrameDelta2`（本模式列；下标 2 = ON，见 slotCrossCheck）
                #   ② 参考实现换算 `frameDuration = 256/25/FrameDelta`（ObjectInfo.cs:109）
                #      + 逐帧播放在 StaticObjectRenderer.cs:39 -> 帧率 = 1/frameDuration = 25*FrameDelta/256
                'frameDelta': frame_delta,
                'frameSeconds': 256.0 / 25.0 / frame_delta,
                'fps': fps,
                'fpsFormula': '25 * FrameDelta / 256',
            },
            'crossCheck': {
                'slotOrder': slots,
                'frameDeltaProfile': {
                    'column': 'FrameDelta%d' % MODE_INDEX,
                    'zeroRows': delta_zero,
                    'nonzeroHistogram': dict(sorted(delta_hist.items())),
                },
            },
            'canvas': {'w': box_w, 'h': canvas_h, 'unionH': box_h, 'padBottom': pad,
                       'originPxFromTop': [0 - left, 0 - top]},
            'layers': [{'component': l['component'],
                        'box': [l['dir'].box.left, l['dir'].box.top,
                                l['dir'].box.width, l['dir'].box.height]} for l in layers],
            'sources': {
                'cof': summary(cof_bytes, cof_rel),
                'objects': summary(objs_raw, OBJECTS_REL),
                'palette': summary(pal_raw, PALETTE_REL),
                'dcc': [summary(l['raw'], l['rel']) for l in layers],
            },
            'files': files,
        }
        with open(os.path.join(out_dir, 'manifest.json'), 'w', encoding='utf-8') as fh:
            json.dump(manifest, fh, indent=2, ensure_ascii=False)
            fh.write('\n')

        print('COF %s = %d bytes；层 = %s' % (cof_rel, len(cof_bytes),
                                              [l['component'] for l in layers]))
        print('objects.txt ON 模式：FrameCnt2=%d CycleAnim2=%d FrameDelta2=%d -> 每帧 %.6f s -> %.5f fps'
              % (frame_cnt, cycle, frame_delta, 256.0 / 25.0 / frame_delta, fps))
        for s in slots:
            print('  列序自证 slot%d(%s)：FrameCnt=%s CycleAnim=%s COF 帧数=%s played=%s match=%s'
                  % (s['slot'], s['code'], s['frameCnt'], s['cycleAnim'], s.get('cofFrames'),
                     s.get('played'), s.get('match')))
        print('  FrameDelta2 分布：0（该模式未启用）= %d 行；非零档位 = %s'
              % (delta_zero, dict(sorted(delta_hist.items()))))
        print('并集框 = %dx%d；垫片 = %d；画布 = %dx%d' % (box_w, box_h, pad, box_w, canvas_h))
        for f in files:
            print('  %s  %dx%d  %d bytes' % (f['file'], f['w'], f['h'], f['bytes']))
    finally:
        storm.close_archive(handle)


if __name__ == '__main__':
    main()
