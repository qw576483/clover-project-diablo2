# =============================================================================
# auditd_sheet.py -- contact sheet + index for the audit-D (D5 anim / D6 vfx /
#                    D7 bgm / D8 sfx) runtime tiles.
#
#   python tools/probes/measure/auditd_sheet.py <shotsDir> <tag>
#
# Output: <shotsDir>/auditD_contact.png  (4 columns x N rows, each cell labelled
#         with its cell number + tile name) and <shotsDir>/auditD_contact.index.tsv
#         (# cell / file / meaning).
# ASCII only.
# =============================================================================
import os
import sys

from PIL import Image, ImageDraw

CELL_W = 480
CELL_H = 270
COLS = 4
LABEL_H = 18
BG = (18, 18, 22)
FG = (235, 235, 235)

# (suffix, meaning) -- the file name is "<tag>_<suffix>" (the driver writes _tag + "_NN_name.png")
TILES = [
    ("_00_town.png", "D5/D7 town: 5 NPC views + player idle (Town BGM started)"),
    ("_01_npc.png", "D5 NPC x5: are the NPC idle animations advancing at all"),
    ("_02_walk.png", "D5 player walk: frame group swap + cursor advance"),
    ("_03_run.png", "D5 player run (after the real R run-toggle key)"),
    ("_04_equip_drop.png", "D5 after Unequip(Weapon,0): does the appearance set change"),
    ("_05_equip_re.png", "D5 after EquipFromInventory: frame group back"),
    ("_06_moor.png", "D7 BloodMoor: BGM switched (crossfade) + monster idle/walk"),
    ("_07_attack.png", "D5/D8 attack window: player Attack anim + swing clips"),
    ("_08_combat.png", "D6 combat: damage float text + hit flash nodes"),
    ("_09_cast.png", "D5/D6/D8 cast: Cast anim + projectile node + cast clip"),
    ("_10_death.png", "D5 death: PlayDeath on the player view (see the runtime line)"),
    ("_11_pause.png", "D7 pause: the BGM keeps playing while paused"),
    ("_12_den.png", "D7 DenOfEvil: BGM switched to denofevil"),
]


def main():
    shots = sys.argv[1]
    tag = sys.argv[2] if len(sys.argv) > 2 else "d4"
    rows = (len(TILES) + COLS - 1) // COLS
    sheet = Image.new("RGB", (COLS * CELL_W, rows * (CELL_H + LABEL_H)), BG)
    draw = ImageDraw.Draw(sheet)

    idx_lines = ["# cell\ttile\tbytes\tmeaning"]
    present = 0
    for i, (suffix, meaning) in enumerate(TILES):
        name = tag + suffix
        path = os.path.join(shots, name)
        col = i % COLS
        row = i // COLS
        x = col * CELL_W
        y = row * (CELL_H + LABEL_H)
        draw.text((x + 4, y + 3), "#%d %s" % (i + 1, name), fill=FG)
        if not os.path.exists(path):
            draw.text((x + 4, y + LABEL_H + 100), "(missing)", fill=(255, 120, 120))
            idx_lines.append("%d\t%s\t0\t%s\t(MISSING)" % (i + 1, name, meaning))
            continue
        present += 1
        im = Image.open(path).convert("RGB")
        im.thumbnail((CELL_W, CELL_H), Image.LANCZOS)
        sheet.paste(im, (x + (CELL_W - im.width) // 2, y + LABEL_H + (CELL_H - im.height) // 2))
        idx_lines.append("%d\t%s\t%d\t%s" % (i + 1, name, os.path.getsize(path), meaning))

    out = os.path.join(shots, "auditD_contact.png")
    sheet.save(out)
    idx = os.path.join(shots, "auditD_contact.index.tsv")
    with open(idx, "w", encoding="utf-8") as f:
        f.write("\n".join(idx_lines) + "\n")
    print("SHEET %s cells=%d present=%d size=%dx%d" % (out, len(TILES), present, sheet.width, sheet.height))
    print("INDEX %s" % idx)


if __name__ == "__main__":
    main()
