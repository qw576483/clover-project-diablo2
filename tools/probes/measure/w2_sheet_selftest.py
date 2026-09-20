# -*- coding: utf-8 -*-
"""w2_sheet_selftest.py -- offline self-proof for w2_sheet.py (JUDGEMENT ASSET, no Play needed).

WHY IT EXISTS (same reason as r1_sheet_selftest.py):
  `w2_sheet.py` decides what every cell of the W2 contact sheets shows by matching the driver's
  `[W2] GRID=... tile=... vals="..."` lines and pairing them with `[W2] SHOT-OK ... bytes=` lines.
  A regex that silently stops matching (a renamed key, an extra log tag, a changed field order)
  would make the index look plausible while the measured values quietly became empty.  This test
  feeds the sheet script a SYNTHETIC frozen log + synthetic tiles and asserts the extraction works,
  in ~2 s and without touching the editor -- so the sheet's assumptions stay re-checkable forever.

Usage:
    python tools/probes/measure/w2_sheet_selftest.py      # exit 0 = the fixture extracted cleanly

It writes its fixtures into <repo>/.ai-tmp/test/w2_selftest/ (one-off scratch, safe to delete).
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = HERE
while REPO and not os.path.isdir(os.path.join(REPO, "client", "Assets")):
    parent = os.path.dirname(REPO)
    if parent == REPO:
        raise SystemExit("cannot find the repo root (from %s)" % HERE)
    REPO = parent

OUT = os.path.join(REPO, ".ai-tmp", "test", "w2_selftest")

TILES = [
    ("B1", "w2_b1_boot.png", "screen",
     "fsm=Boot logo=logo_0 logoRect=574.2x318.6 logoNative=319x177 byline=\"by clover-engine\" hint=\"press any key\""),
    ("B2", "w2_b2_mainmenu.png", "screen",
     "fsm=MainMenu buttons=2 names=[Single,Quit] byline=\"by clover-engine\""),
    ("B3", "w2_b3_charselect.png", "screen",
     "fsm=CharSelect rowLabels=7 savedNames=[Hero,W2Hero] texts=|Row0/Name\"Hero\""),
    ("B3-confirm", "w2_b3_delete_confirm.png", "screen",
     "confirmBtn=Confirm@[UI]/Top/ConfirmBox/Cancel cancelBtn=Cancel@[UI]/Top/ConfirmBox/Cancel"),
    ("B4", "w2_b4_charcreate.png", "screen",
     "fsm=CharCreate activeSpots=[SpotAmazon,SpotBarbarian] classIndex=-1 nameVisible=\"Hero\""),
    ("B5", "w2_b5_loading_a.png", "screen", "loadingOpen=1 shownFrame=0 artSprite=loadingscreen_0"),
    ("B5b", "w2_b5_loading_b.png", "screen", "loadingOpen=1 shownFrame=6 artSprite=loadingscreen_6"),
    ("C5-town", "w2_c5_title_town.png", "screen", "showing=1 text=\"Entering Rogue Encampment\" alpha=1.000"),
    ("C1", "w2_c1_town_default.png", "camera", "area=Town map=56x40 seed=11 walkable=1528"),
    ("C1-wide", "w2_c1_town_wide.png", "camera", "area=Town map=56x40 ortho=30.000"),
    ("C4", "w2_c4_minimap.png", "screen", "miniMapOpen=1 markers=8 markerSprites=[mapicon_0]"),
    ("D1-nowalk", "w2_d1_nowalk_cursor.png", "screen",
     "probe=NoWalk cell=(20,20) tileKind=Rock walkable=0 cursorKind=NoWalk"),
    ("D1-detour-start", "w2_d1_detour_start.png", "camera", "from=(10,10) to=(13,13) pathCells=9"),
    ("D1-detour-stop", "w2_d1_detour_stop.png", "camera", "want=(13,13) playerGrid=(13,13) nonWalkableCellsOnPath=0"),
    ("D2", "w2_d2_follow_a.png", "camera", "camPos=(1.00,-15.00) camRotZ=0.0 playerInFrame=1"),
    ("D2b", "w2_d2_follow_b.png", "camera", "camPos=(-3.00,-6.50) camRotZ=0.0 playerInFrame=1"),
    ("D3-S", "w2_d3_dir0_S.png", "camera", "dir=S dirIndex=0 sprite=run_s_1 playerGrid=(20,20) moving=1"),
    ("D3-SW", "w2_d3_dir1_SW.png", "camera", "dir=SW dirIndex=1 sprite=run_sw_1 playerGrid=(20,21) moving=1"),
    ("E1-a", "w2_e1_hit_a.png", "camera", "phase=on-hit m#1000 hp=8/10 damageEvents=1 audio=[swingx1]"),
    ("E2", "w2_e2_levelup.png", "screen", "level=2 exp=0/1500 statPts=5 skillPts=1 kills=19 levelUps=1"),
    ("E3", "w2_e3_skilltree.png", "screen", "skillTreeOpen=1 nodes=30 noIcon=0 brightIcons=6 firstText=\"skill tree\""),
    ("E4-a", "w2_e4_cast_a.png", "camera", "phase=cast t=0 skillId=1 ok=1 mana=20/22"),
    ("F1", "w2_f1_inventory.png", "screen", "inventoryOpen=1 cells=40 occupied=1 goldText=\"40000\""),
    ("F1-q0", "w2_f1_tooltip_q0.png", "screen", "q=0 visible=1 title=\"short sword\" titleColorRGBA=1.000/1.000/1.000/1.000"),
    ("F1-q1", "w2_f1_tooltip_q1.png", "screen", "q=1 visible=1 title=\"magic sword\" titleColorRGBA=0.412/0.412/1.000/1.000"),
    ("F2", "w2_f2_equip.png", "screen", "equipmentCount=1 equipment=|short sword id=11 q=0 dmg=2-7 dur=24/24"),
    ("F3", "w2_f3_gold.png", "screen", "goldBefore=40000 goldAfter=41234 delta=1234"),
    ("G1a", "w2_g1_dialog_notstarted.png", "screen", "state=NotStarted dialogOpen=1 canAccept=True canTurnIn=False"),
    ("G1b", "w2_g1_dialog_inprogress.png", "screen", "state=InProgress dialogOpen=1 canAccept=False canTurnIn=False"),
    ("G1c", "w2_g1_dialog_ready.png", "screen", "state=ReadyToTurnIn dialogOpen=1 canAccept=False canTurnIn=True"),
    ("G1d", "w2_g1_dialog_done.png", "screen", "state=Done dialogOpen=1 canAccept=False canTurnIn=False"),
    ("G2-buy", "w2_g2_shop_buy.png", "screen", "page=buy shopOpen=1 title=\"Charsi\" cellsOccupied=8 playerGold=40000"),
    ("G2-sell", "w2_g2_shop_sell.png", "screen", "page=tab1 shopOpen=1 cellsOccupied=4 playerGold=39519"),
    ("G2-repair", "w2_g2_shop_repair.png", "screen", "page=repair shopOpen=1 playerGold=39451"),
    ("G3a", "w2_g3_questlog_notstarted.png", "screen", "state=NotStarted questLogOpen=1 texts=|not accepted"),
    ("G3b", "w2_g3_questlog_inprogress.png", "screen", "state=InProgress questLogOpen=1 texts=|in progress"),
    ("G3c", "w2_g3_questlog_ready.png", "screen", "state=ReadyToTurnIn questLogOpen=1 texts=|ready"),
    ("G3d", "w2_g3_questlog_done.png", "screen", "state=Done questLogOpen=1 texts=|done"),
    ("G4", "w2_g4_den_cleared.png", "camera", "phase=cleared denAreaAlive=0 denState=ReadyToTurnIn"),
    ("H1", "w2_h1_death.png", "screen", "deathOpen=1 bannerSprite=youdiedsoftcore_0 goldBefore=41234 goldAfterDeath=37110"),
    ("H2-pause", "w2_h2_pause.png", "screen", "pauseOpen=1 timeScale=0.00 fsm=Pause"),
    ("H2-opt-before", "w2_h2_options_before.png", "screen", "phase=before optionsOpen=1 settingBgm=0.70"),
    ("H2-opt-after", "w2_h2_options_after.png", "screen", "phase=after optionsOpen=1 settingBgm=0.40 settingQuality=2"),
    ("H2-menu", "w2_h2_mainmenu.png", "screen", "fsm=MainMenu settingBgm=0.40 settingQuality=2"),
    ("H3", "w2_h3_reentry.png", "screen", "level=2->2 gold=37110->37110 bagAnchors=2->2 denState=3->3"),
]

# the fixture is written in the driver's real `[ts] [Level] [W2] KEY=value` shape on purpose:
# the sheet script must find the `[W2]` tag in the middle of the line, not at its start.
LOG = []
for i, (gid, tile, kind, vals) in enumerate(TILES):
    LOG.append("[2026-09-21 10:00:%02d.%03d] [Info] [W2] GRID=%s tile=%s vals=\"%s\""
               % (i % 60, i, gid, tile, vals))
    LOG.append("[2026-09-21 10:00:%02d.%03d] [Info] [W2] SHOT-OK n=%d state=w2 name=%s kind=%s bytes=%d path=shots/%s"
               % (i % 60, i + 1, i, tile, kind, 200000 + i * 37, tile))
LOG.append("[2026-09-21 10:00:30.000] [Info] [W2] CROP n=260 tile=w2_c4_minimap.png node=MiniMapBox "
           "sx0=1560 sy0=818 sx1=1920 sy1=1080 cx=1740 cy=949")
LOG.append("[2026-09-21 10:00:31.000] [Info] [W2] CROP n=220 tile=w2_g1_dialog_notstarted.png node=DialogArt "
           "sx0=774 sy0=345 sx1=1146 sy1=737 cx=960 cy=541")


def main():
    from PIL import Image
    os.makedirs(OUT, exist_ok=True)
    for i, (gid, tile, kind, vals) in enumerate(TILES):
        r = 20 + (i * 7) % 180
        im = Image.new("RGB", (1920, 1080), (r, 40, 60))
        for x in range(0, 1920, 40):
            for y in range(0, 1080, 40):
                if (x // 40 + y // 40) % 2 == 0:
                    im.putpixel((x, y), (240, 240 - i, 30))
        im.save(os.path.join(OUT, tile))
    frozen = os.path.join(OUT, "fixture_frozen.txt")
    with open(frozen, "w", encoding="utf-8") as fh:
        fh.write("\n".join(LOG) + "\n")
    trace = os.path.join(OUT, "fixture_trace.txt")
    with open(trace, "w", encoding="utf-8") as fh:
        fh.write('SUMMARY tag=selftest shots=%d grids=%d\n' % (len(TILES), len(TILES)))
        fh.write('DEVICE graphicsDeviceName="AMD Radeon RX 5700 XT"\n')

    sheet = os.path.join(HERE, "w2_sheet.py")
    r = subprocess.run([sys.executable, sheet, OUT, frozen, trace, OUT, "selftest"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(r.stdout.strip())
    if r.stderr.strip():
        print(r.stderr.strip())

    ok = True
    index = os.path.join(OUT, "w2_contact.index.tsv")
    flow = os.path.join(OUT, "w2_contact_flow.png")
    game = os.path.join(OUT, "w2_contact_game.png")
    for p in (index, flow, game):
        if not os.path.exists(p):
            print("SELFTEST-FAIL not produced: %s" % p)
            ok = False
    if ok:
        txt = open(index, encoding="utf-8").read()
        for bad in ("通过", "一致", "PASS", "FAIL"):
            if bad in txt:
                print("SELFTEST-FAIL index contains a verdict word: %s" % bad)
                ok = False
        rows = [l for l in txt.splitlines() if l and not l.startswith("#") and not l.startswith("grid\t")]
        if len(rows) != len(TILES):
            print("SELFTEST-FAIL index rows=%d expected=%d" % (len(rows), len(TILES)))
            ok = False
        for l in rows:
            cols = l.split("\t")
            if len(cols) != 5:
                print("SELFTEST-FAIL row does not have 5 columns: %s" % l[:160])
                ok = False
                continue
            if not cols[1].strip():
                print("SELFTEST-FAIL row has empty measured values: %s" % cols[0])
                ok = False
            if int(cols[3]) <= 0:
                print("SELFTEST-FAIL row has no byte count: %s" % cols[0])
                ok = False
            if not cols[4].split(":", 1)[1].startswith("[W2] GRID="):
                print("SELFTEST-FAIL row does not cite its own GRID line: %s" % cols[0])
                ok = False
    print("SELFTEST-%s (fixture %d tiles, %d log lines)" % ("OK" if ok else "FAIL", len(TILES), len(LOG)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
