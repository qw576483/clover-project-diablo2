# -*- coding: utf-8 -*-
"""Diablo II `.COF`（单位动画的"图层清单 + 绘制顺序"表）解析器（纯 Python）。

`.COF` 回答三个问题：
  ① 这个单位在这个动作（mode）+ 武器类别（weapon class）下**由哪几层组成**；
  ② 每层用哪个**组件目录**（HD/TR/LG/RA/LA/RH/LH/SH/S1..S8）；
  ③ 每一帧的**绘制顺序**（priority：按层号从后往前画）。

格式依据（**两处独立实现互相印证**）：
  * `_assets_tmp/d2src/libd2/packages/formats/src/cof.zig`
      · 头 **25 字节**：`[0]=numLayers` / `[1]=framesPerDir` / `[2]=numDirections` /
        `[3..23]` 未知 / **`[24] = speed`**（cof.zig:64-74）
      · 头之后 **3 个未知字节** ⇒ 段起点 = offset 28（`HEADER_BYTES + BODY_BYTES`，cof.zig:64-65,77）
      · 层 **9 字节**：`[0]=CompositeType` / `[1]=shadow` / `[2]=selectable` /
        `[3]=transparent` / `[4]=drawEffect` / `[5..9]=weaponClass(4 字符，NUL/空格裁剪)`
        （cof.zig:81-101）
      · 层之后 `framesPerDir` 字节（动画帧表，本项目不用）⇒ 跳过（cof.zig:104）
      · `priority` = `directions * framesPerDir * layers` 字节，
        `priority[dir][frame][slot] = CompositeType`（**从后往前的绘制顺序**，cof.zig:106-108）
      · `COMPONENT_CODES` 顺序 = `HD,TR,LG,RA,LA,RH,LH,SH,S1..S8`（cof.zig:17-20）
  * `_assets_tmp/d2src/Diablerie/Assets/Scripts/Diablerie/Engine/IO/D2Formats/COF.cs`
      · 同一条头（L60-63：3 字节 + `Seek(25)` ⇒ 段起点 28）
      · 同一套层字段（L68-93：compositIndex / shadow / selectable / transparent /
        blendMode / weaponClass(4 字节，取前 3 个字符)）
      · 同一套 `layerNames = { "HD","TR","LG","RA","LA","RH","LH","SH","S1".."S8" }`（L38）
      · `GetSpritesheetFilename(layer, equip)`：`basePath/token/layer.name/token+layer.name
        +equip+mode+layer.weaponClass`（L105-108）—— 即组件 DCC 的文件名规则
      · `PlayerModes` / `MonsterModes` 模式代号（L30/33）：
        玩家 `DT NU WL RN GH TN TW A1 A2 BL SC TH KK S1 S2 S3 S4 DD`
        怪物 `DT NU WL GH A1 A2 BL SC S1 S2 S3 S4 DD RN`

CLI：
  python cof.py info <file.cof>    打印层清单 + 每层组件/武器类别 + 第 0 帧绘制顺序
"""

import os
import sys

COMPONENT_CODES = ('HD', 'TR', 'LG', 'RA', 'LA', 'RH', 'LH', 'SH',
                   'S1', 'S2', 'S3', 'S4', 'S5', 'S6', 'S7', 'S8')

HEADER_BYTES = 25
BODY_BYTES = 3
LAYER_BYTES = 9

# ── 模式代号（plrmode.txt / monmode.txt，出处 Diablerie COF.cs:30/33）──────────
PLAYER_MODES = ('DT', 'NU', 'WL', 'RN', 'GH', 'TN', 'TW', 'A1', 'A2', 'BL', 'SC',
                'TH', 'KK', 'S1', 'S2', 'S3', 'S4', 'DD', 'GH', 'GH')
MONSTER_MODES = ('DT', 'NU', 'WL', 'GH', 'A1', 'A2', 'BL', 'SC', 'S1', 'S2', 'S3',
                 'S4', 'DD', 'GH', 'xx', 'RN')

#: 本项目 `ViewAnim` → 原版模式代号（`Module/View/ViewAnim.cs` 的 6 个动作）。
VIEW_ANIM_MODES = (
    ('idle', 'NU'),
    ('walk', 'WL'),
    ('attack', 'A1'),
    ('cast', 'SC'),
    ('hit', 'GH'),
    ('death', 'DT'),
)


class Layer(object):
    __slots__ = ('index', 'component', 'shadow', 'selectable', 'transparent',
                 'draw_effect', 'weapon_class')

    def __init__(self, index, component, shadow, selectable, transparent, draw_effect, weapon_class):
        self.index = index
        self.component = component
        self.shadow = shadow
        self.selectable = selectable
        self.transparent = transparent
        self.draw_effect = draw_effect
        self.weapon_class = weapon_class

    @property
    def component_code(self):
        return COMPONENT_CODES[self.component] if 0 <= self.component < 16 else '??'

    def __repr__(self):
        return ('Layer(#%d %s wc=%s shadow=%d transparent=%d)'
                % (self.index, self.component_code, self.weapon_class,
                   self.shadow, self.transparent))


class Cof(object):
    __slots__ = ('num_layers', 'frames_per_dir', 'num_directions', 'speed', 'layers',
                 'priority', 'path')

    def __init__(self, num_layers, frames_per_dir, num_directions, speed, layers,
                 priority, path=None):
        self.num_layers = num_layers
        self.frames_per_dir = frames_per_dir
        self.num_directions = num_directions
        self.speed = speed
        self.layers = layers
        self.priority = priority      # flat: dir*framesPerDir*layers + frame*layers + slot
        self.path = path

    def draw_order(self, direction, frame):
        """返回该 dir/frame 的图层下标序列（**从后往前**画，先画前面的元素）。"""
        base = (direction * self.frames_per_dir + frame) * self.num_layers
        out = []
        for slot in range(self.num_layers):
            comp = self.priority[base + slot]
            # priority 里存的是 CompositeType（组件号），要映射回 layers 里那一层的下标
            for i, layer in enumerate(self.layers):
                if layer.component == comp:
                    out.append(i)
                    break
            else:
                # 非预期分支：priority 引用了 layers 里不存在的组件 ⇒ 跳过并留痕
                print('[cof] WARN %s: priority[%d] 引用未登记组件 %d'
                      % (os.path.basename(self.path or '?'), base + slot, comp))
        return out

    def __repr__(self):
        return ('COF(layers=%d fpd=%d dirs=%d speed=%d)'
                % (self.num_layers, self.frames_per_dir, self.num_directions, self.speed))


def parse(data, path=None):
    """解析 `.cof`。返回 <see cref="Cof"/>；格式不符抛 ValueError。"""
    name = path or '<bytes>'
    if len(data) < HEADER_BYTES + BODY_BYTES:
        raise ValueError('%s: 只有 %d 字节，连 COF 头都不够' % (name, len(data)))

    num_layers = data[0]
    frames_per_dir = data[1]
    num_directions = data[2]
    speed = data[24]
    if num_layers == 0 or frames_per_dir == 0 or num_directions == 0:
        raise ValueError('%s: layers=%d fpd=%d dirs=%d 非法' % (name, num_layers, frames_per_dir,
                                                            num_directions))

    off = HEADER_BYTES + BODY_BYTES
    layers = []
    for i in range(num_layers):
        if off + LAYER_BYTES > len(data):
            raise ValueError('%s: 第 %d 层越界（off=%d）' % (name, i, off))
        b = data[off:off + LAYER_BYTES]
        wc = ''.join(chr(c) for c in b[5:9] if c not in (0, 0x20))
        layers.append(Layer(i, b[0], b[1], b[2] > 0, b[3] > 0, b[4], wc))
        off += LAYER_BYTES

    off += frames_per_dir            # 动画帧表：本项目不用（cof.zig:104）

    prio_len = num_directions * frames_per_dir * num_layers
    if off + prio_len > len(data):
        raise ValueError('%s: priority 越界（需 %d，剩 %d）' % (name, prio_len, len(data) - off))
    priority = bytearray(data[off:off + prio_len])

    return Cof(num_layers, frames_per_dir, num_directions, speed, layers, priority, path)


def dcc_filename(token, layer, equip, mode):
    """组件 DCC 的文件名（**不含目录/扩展名**）。

    出处 Diablerie `COF.cs:105-108`：`token + layer.name + equip + mode + layer.weaponClass`。
    例：`("am", HD层, "", "NU")` → `amHDNUHTH`（大小写不敏感，实测 MPQ 里存的是小写）。
    """
    return token + layer.component_code + equip + mode + layer.weapon_class


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    path = argv[1]
    cof = parse(open(path, 'rb').read(), path)
    print('%s  %r' % (os.path.basename(path), cof))
    print('  层清单：')
    for l in cof.layers:
        print('    #%d %s wc=%-4s shadow=%d selectable=%d transparent=%d drawEffect=%d'
              % (l.index, l.component_code, l.weapon_class, l.shadow, l.selectable,
                 l.transparent, l.draw_effect))
    for d in range(min(cof.num_directions, 2)):
        for f in range(min(cof.frames_per_dir, 2)):
            order = cof.draw_order(d, f)
            print('  dir %d frame %d 绘制顺序（后→前）= %s'
                  % (d, f, [cof.layers[i].component_code for i in order]))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
