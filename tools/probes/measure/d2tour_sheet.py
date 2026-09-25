# -*- coding: utf-8 -*-
"""tour contact sheets + index.

Reads the screenshots written by tools/probes/drivers/d2tour_evidence.cs and
builds three groups of contact sheets (panels / areas / zooms) plus the index
tsv that maps 格号 <-> 是什么 <-> 截图路径.

    python d2tour_sheet.py <shots_dir> <out_dir> <index_tsv>

Design rules (why it looks like this):
  * one cell per screenshot, each cell labelled with "座标 + 名字";
  * panels are split over 3 pages of 6 so no panel is shrunk into an unreadable
    block; areas stay close to native size; zooms are real magnifications
    (nearest neighbour, so no interpolation blur);
  * a missing source draws a MISSING cell and still writes its index row
    (a silent absence is the one failure this sheet exists to catch).

Judge contract: prints RESULT OK / RESULT FAIL <n missing> and exits 0 / 1.
"""

import os
import sys

from PIL import Image, ImageDraw

PANELS = [
    ('BootPanel', 'tour_panel_boot.png'),
    ('MainMenuPanel', 'tour_panel_mainmenu.png'),
    ('CharSelectPanel', 'tour_panel_charselect.png'),
    ('CharCreatePanel', 'tour_panel_charcreate.png'),
    ('LoadingPanel', 'tour_panel_loading.png'),
    ('HudPanel', 'tour_panel_hud.png'),
    ('InventoryPanel', 'tour_panel_inventory.png'),
    ('CharacterPanel', 'tour_panel_character.png'),
    ('SkillTreePanel', 'tour_panel_skilltree.png'),
    ('QuestLogPanel', 'tour_panel_questlog.png'),
    ('MiniMapPanel', 'tour_panel_minimap.png'),
    ('PausePanel', 'tour_panel_pause.png'),
    ('SettingsPanel', 'tour_panel_settings.png'),
    ('D2ConfirmPanel', 'tour_panel_confirm.png'),
    ('NpcDialogPanel', 'tour_panel_npcdialog.png'),
    ('ShopPanel', 'tour_panel_shop.png'),
    ('DeathPanel', 'tour_panel_death.png'),
    ('WaypointPanel', 'tour_panel_waypoint.png'),
]

AREAS = [
    ('Area Town', 'tour_area_town.png'),
    ('Area BloodMoor', 'tour_area_bloodmoor.png'),
    ('Area DenOfEvil', 'tour_area_den.png'),
]

# (label, source png, crop box x,y,w,h) -- the cell is 900x675, so a box smaller
# than the cell is magnified (nearest neighbour) and a bigger one is fitted.
ZOOMS = [
    ('waypoint pad + player (anchor 31,26)', 'tour_town_waypoint.png', (620, 340, 300, 225)),
    ('town ground objects', 'tour_town_waypoint.png', (600, 300, 460, 345)),
    ('town npc (hovered plate NOT visible in frame)', 'tour_nameplate_npc.png', (640, 300, 640, 480)),
    ('hover state (target 15 cells away, off frame)', 'tour_mob_hover.png', (640, 300, 640, 480)),
    ('monster move (target 2 cells)', 'tour_mob_move.png', (640, 300, 640, 480)),
    ('monster hit (target 2 cells)', 'tour_mob_hit.png', (640, 300, 640, 480)),
    ('minimap panel', 'tour_panel_minimap.png', (620, 280, 680, 510)),
    ('inventory grid', 'tour_panel_inventory.png', (900, 150, 700, 600)),
    ('inventory drag ghost', 'tour_inventory_drag.png', (900, 150, 700, 600)),
    # close buttons: crop centred on the measured hit rect (57.6x55.8 canvas px;
    # InventoryPanel img=992.8,843.5  CharacterPanel img=615.5,851 -- logged by
    # the driver as CLOSEBTN-* in d2tour_readings_tour7.txt)
    # QuestLog both states side by side (same crop): empty/NotStarted vs InProgress
    ('QuestLogPanel NotStarted', 'tour_panel_questlog.png', (620, 280, 680, 510)),
    ('QuestLogPanel InProgress', 'tour_panel_questlog_progress.png', (620, 280, 680, 510)),
    # magnified so the objective/progress line is readable (sheet-res is not enough)
    ('QuestLogPanel InProgress text', 'tour_panel_questlog_progress.png', (640, 430, 360, 270)),
    ('InventoryPanel close button (x3.75)', 'tour_panel_inventory.png', (933, 784, 180, 180)),
    ('CharacterPanel close button (x3.75)', 'tour_panel_character.png', (556, 791, 180, 180)),
]

# Cells whose picture shows something plainly wrong. Kept here (not hand-edited into
# the tsv) so the note survives every re-run of this script and the next rescan.
SUSPECTS = {
    ('tour_sheet_panels_1of3.png', 'r1c1'):
        ('SUSPECT-1', 'BootPanel 面板区只有一块白矩形，无启动画面贴图'),
    ('tour_sheet_panels_3of3.png', 'r2c2'):
        ('SUSPECT-2', 'ShopPanel 商品图标格是白色方块占位（网格左上两格）'),
    ('tour_sheet_panels_2of3.png', 'r2c2'):
        ('SUSPECT-3', 'QuestLogPanel 标题下内容区大块空白，只有一行「查看任务的细节…」'),
    ('tour_sheet_panels_3of3.png', 'r3c1'):
        ('SUSPECT-4', 'DeathPanel 横幅上写的是「你損失金錢數量」'),
}

CELL_BG = (34, 34, 42, 255)
LABEL_BG = (18, 18, 22, 255)
LABEL_FG = (255, 240, 120, 255)
MISS_FG = (255, 90, 90, 255)


def load_shot(shots_dir, name):
    p = os.path.join(shots_dir, name)
    if not os.path.exists(p):
        return None
    try:
        return Image.open(p).convert('RGB')
    except Exception:
        return None


def missing_cell(w, h, text):
    im = Image.new('RGB', (w, h), (60, 20, 20))
    d = ImageDraw.Draw(im)
    d.text((8, 8), 'MISSING: ' + text, fill=(255, 120, 120))
    return im


def fit(im, cw, ch):
    r = min(cw / float(im.width), ch / float(im.height))
    return im.resize((max(1, int(im.width * r)), max(1, int(im.height * r))), Image.LANCZOS)


def crop_zoom(im, box, scale):
    x, y, w, h = box
    c = im.crop((x, y, x + w, y + h))
    return c.resize((w * scale, h * scale), Image.NEAREST)


def build_sheet(rows, cols, cw, ch, title, items, shots_dir, base_dir, missing, index_rows,
                sheet_name, crop=None):
    """items: list of (label, png name). crop=None -> fit whole shot into the cell."""
    pad = 6
    labelh = 18
    cw_tot = cw + pad * 2
    ch_tot = ch + labelh + pad * 2
    W = cols * cw_tot
    H = rows * ch_tot + 30
    sheet = Image.new('RGB', (W, H), CELL_BG)
    d = ImageDraw.Draw(sheet)
    d.rectangle([0, 0, W, 26], fill=LABEL_BG)
    d.text((8, 7), title, fill=LABEL_FG)

    for i, (label, name) in enumerate(items):
        col = i % cols
        row = i // cols
        ox = col * cw_tot + pad
        oy = 30 + row * ch_tot + labelh + pad
        d.text((ox, oy - labelh + 2), label, fill=LABEL_FG)
        im = load_shot(shots_dir, name)
        if im is None:
            cell = missing_cell(cw, ch, name)
            missing.append(name)
        elif crop is not None:
            cell = crop_zoom(im, crop[0], crop[1])
            if cell.width > cw or cell.height > ch:
                cell = fit(cell, cw, ch)
        else:
            cell = fit(im, cw, ch)
        sheet.paste(cell, (ox + (cw - cell.width) // 2, oy + (ch - cell.height) // 2))
        index_rows.append((sheet_name, 'r%dc%d' % (row + 1, col + 1), label, name))

    out = os.path.join(base_dir, sheet_name)
    sheet.save(out)
    return out, sheet.size


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 2
    shots_dir = argv[1]
    out_dir = argv[2]
    index_tsv = argv[3]
    if not os.path.isdir(out_dir):
        os.makedirs(out_dir)

    missing = []
    index_rows = []
    sizes = []

    pages = [(PANELS[i:i + 6], 'panels %d/3' % (i // 6 + 1)) for i in range(0, 18, 6)]
    for items, tag in pages:
        name = 'tour_sheet_panels_%s.png' % tag.split()[1].replace('/', 'of')
        out, size = build_sheet(3, 2, 900, 506, 'tour contact sheet - panels (%s)' % tag,
                                items, shots_dir, out_dir, missing, index_rows, name)
        sizes.append((out, size))
        print('%s %dx%d' % (out, size[0], size[1]))

    out, size = build_sheet(3, 1, 1280, 720, 'tour contact sheet - in-game areas',
                            AREAS, shots_dir, out_dir, missing, index_rows, 'tour_sheet_areas.png')
    sizes.append((out, size))
    print('%s %dx%d' % (out, size[0], size[1]))

    # the zooms sheet uses a different crop box per cell, so it is built inline
    sheet_name = 'tour_sheet_zooms.png'
    pad, labelh = 6, 18
    cols = 2
    cw, ch = 900, 675
    rows = (len(ZOOMS) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * (cw + pad * 2), rows * (ch + labelh + pad * 2) + 30), CELL_BG)
    d = ImageDraw.Draw(sheet)
    d.rectangle([0, 0, sheet.width, 26], fill=LABEL_BG)
    d.text((8, 7), 'tour contact sheet - element zooms (nearest neighbour when magnified)', fill=LABEL_FG)
    for i, (label, name, box) in enumerate(ZOOMS):
        col, row = i % cols, i // cols
        ox = col * (cw + pad * 2) + pad
        oy = 30 + row * (ch + labelh + pad * 2) + labelh + pad
        im = load_shot(shots_dir, name)
        if im is None:
            cell = missing_cell(cw, ch, name)
            missing.append(name)
            mag = 0.0
        else:
            x, y, w, h = box
            cell = im.crop((x, y, x + w, y + h))
            mag = min(cw / float(w), ch / float(h))
            if mag >= 1.0:
                cell = cell.resize((max(1, int(w * mag)), max(1, int(h * mag))), Image.NEAREST)
            else:
                cell = cell.resize((max(1, int(w * mag)), max(1, int(h * mag))), Image.LANCZOS)
        d.text((ox, oy - labelh + 2), '%s  box=%dx%d  x%.2f' % (label, box[2], box[3], mag), fill=LABEL_FG)
        sheet.paste(cell, (ox + (cw - cell.width) // 2, oy + (ch - cell.height) // 2))
        index_rows.append((sheet_name, 'r%dc%d' % (row + 1, col + 1),
                           '%s box=%d,%d,%d,%d x%.2f' % ((label,) + box + (mag,)), name))
    index_rows = index_rows
    out = os.path.join(out_dir, sheet_name)
    sheet.save(out)
    sizes.append((out, sheet.size))
    print('%s %dx%d' % (out, sheet.size[0], sheet.size[1]))

    with open(index_tsv, 'w', encoding='utf-8', newline='\n') as f:
        f.write('sheet\tcell\twhat\tshot_path\tnote\n')
        for r in index_rows:
            note = SUSPECTS.get((r[0], r[1]))
            f.write('%s\t%s\t%s\t%s\t%s\n' % (r + ('%s %s' % note if note else '',)))

    for out, size in sizes:
        print('SHEET %s %dx%d' % (out, size[0], size[1]))
    uniq = sorted(set(missing))
    print('MISSING %d %s' % (len(uniq), ','.join(uniq) if uniq else '(none)'))
    print('RESULT %s' % ('OK' if not uniq else 'FAIL %d' % len(uniq)))
    return 0 if not uniq else 1


if __name__ == '__main__':
    sys.exit(main(sys.argv))
