# -*- coding: utf-8 -*-
"""p41_sheet_selftest.py -- offline check of p41_sheet.py (NOT shipped; deleted before delivery).

It fabricates (a) the 11 tiles at 1920x1080, (b) a log whose [P41] lines are byte-for-byte what
p41_drive.cs emits for a HEALTHY run, (c) the trace lines the run script writes.  Running the real
sheet script over that fixture must give 0 FAIL -- if a regex or a formatting assumption in the sheet
is wrong it shows up here in seconds instead of after a Play session.

Usage: python p41_sheet_selftest.py
"""
import os
import re
import subprocess
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
FAKE = os.path.join(HERE, "selftest")
TILES = os.path.join(FAKE, "shots")
LOG = os.path.join(FAKE, "p41_log_selftest.txt")
TRACE = os.path.join(FAKE, "p41_steps_selftest.txt")
OUT = os.path.join(FAKE, "p41_contact_selftest.png")
TSV = os.path.join(FAKE, "p41_index_selftest.tsv")

TILE_NAMES = ["p41_01_boot.png", "p41_02_menu.png", "p41_03_town.png", "p41_04_town_wide.png",
              "p41_05_town_exit.png", "p41_06_wild.png", "p41_07_wild_wide.png",
              "p41_08_dir_s.png", "p41_09_dir_n.png", "p41_10_dir_e.png", "p41_11_dir_w.png"]

TS = "[2026-09-20 01:00:%02d.000] [Info] "


def L(i, msg):
    return TS % (i % 60) + msg


def make_tiles():
    if not os.path.isdir(TILES):
        os.makedirs(TILES)
    for n, name in enumerate(TILE_NAMES):
        p = os.path.join(TILES, name)
        im = Image.new("RGB", (1920, 1080), (20 + n * 5, 30, 40))
        px = im.load()
        # noise so the file is comfortably over the sheet's "too small" floor (20 KB)
        seed = 12345 + n
        for y in range(1080):
            for x in range(0, 1920, 3):
                seed = (seed * 1103515245 + 12345) & 0x7FFFFFFF
                px[x, y] = ((seed >> 8) & 0xFF, (seed >> 16) & 0xFF, 30)
        # a non-uniform band inside the byline crop box as well
        for y in range(950, 1050):
            for x in range(680, 1240, 4):
                px[x, y] = (250, 240, 200)
        im.save(p)


def make_log():
    lines = [
        L(0, "[P41] CFG runInBg=1 vSync=0 targetFps=60 focused=1 screen=1920x1080 keyboard=Keyboard "
              "keyboardAdded=0 mouseAdded=0 gameRunning=1 fsm=Boot scene=Boot"),
        L(1, "[P41] BOOT scene=Boot fsm=Boot screen=1920x1080 focused=1 runInBackground=1"),
        L(2, "[P41] UITEXTS where=boot count=5 |BootPanel(Clone)/Screen/ByLine\"by clover-engine\" "
              "size=43 col=0.78,0.76,0.72 enabled=0 font=null mirror=1 tier=Font24 active=1 "),
        L(3, "[P41] BYLINE where=boot present=1 path=BootPanel(Clone)/Screen/ByLine text=\"by clover-engine\" "
              "tier=Font24 size=43 color=0.78,0.76,0.72 active=1 ok=1"),
        L(4, "[P41] SHOT-OK n=1 state=boot name=p41_01_boot.png kind=screen bytes=76934 "
              "path=C:/x/.ai-tmp/test/raw/p41_01_boot.png"),
        L(5, "[P41] MENU scene=Menu fsm=MainMenu"),
        L(6, "[P41] BYLINE where=menu present=1 path=MainMenuPanel(Clone)/Screen/ByLine text=\"by clover-engine\" "
              "tier=Font24 size=43 color=0.78,0.76,0.72 active=1 ok=1"),
        L(7, "[P41] MENU-BUTTONS active=2 names=[Quit,Single] labels=[Quit=EXIT,Single=SINGLE PLAYER]"),
        L(8, "[P41] MENU-FORBIDDEN multiplayerOrCinematics=0 expected=0"),
        L(8, "[P41] MENU-TEXTS labelTexts=[EXIT,SINGLE PLAYER] hits=2 forbiddenTexts=0 ok=1"),
        L(9, "[P41] SHOT-OK n=2 state=menu name=p41_02_menu.png kind=screen bytes=580090 path=C:/x/p41_02_menu.png"),
        L(10, "[P41] CHARSELECT open=1"),
        L(11, "[P41] STAGE scene=Stage fsm=Stage"),
        L(12, "[P41] TOWN area=Town map=56x40 seed=12345 spawn=(32,28) walkable=1420 blocked=800 "
               "exits=[(17,26),(17,27),(17,28)] npcPoints=5"),
        L(13, "[P41] OSIZE rigValue=3.75 camValue=3.75 visibleWorldH=7.5 tileSize=1 visibleGridRows=7.5 "
               "aspect=1.7778 camPos=(14.75,-22.75) playerWorld=(14.75,-22.75)"),
        L(14, "[P41] SPEEDCONST PlayerWalkSpeed=3 walkFactor=0.46667 walkSpeedDerived=1.4 playerMoveSpeed=3 running=1"),
        L(15, "[P41] EXITTILE g=(17,26) kind=Exit keysAvailable=1 ground=town_floor/035 object=(none)"),
        L(16, "[P41] EXITTILE g=(17,27) kind=Exit keysAvailable=1 ground=town_floor/054 object=(none)"),
        L(17, "[P41] EXITTILE g=(17,28) kind=Exit keysAvailable=1 ground=town_floor/052 object=(none)"),
        L(18, "[P41] EXITTILES count=3 kindExitCells=3"),
        L(19, "[P41] SHOT-OK n=3 state=town name=p41_03_town.png kind=camera bytes=1200000 path=C:/x/p41_03_town.png"),
        L(20, "[P41] WIDE-SHOT which=town orthoOverride=12 overrideOk=1 center=(28,20) camPos=(0.00,0.00) "
               "note=probe-only"),
        L(21, "[P41] SHOT-OK n=4 state=town-wide name=p41_04_town_wide.png kind=camera bytes=1200000 "
               "path=C:/x/p41_04_town_wide.png"),
        L(22, "[P41] CAM-RESTORED rigValue=3.75 camValue=3.75 expected=3.75"),
        L(23, "[P41] WALKTO target=(19,27) arrived=1 grid=(19,27)"),
        L(24, "[P41] SHOT-OK n=5 state=town-exit name=p41_05_town_exit.png kind=camera bytes=1200000 "
               "path=C:/x/p41_05_town_exit.png"),
        L(25, "[P41] EXITSTEP target=(17,27) from=(19,27) tileAtTarget=Exit"),
        L(26, "[P41] EXITSTEP-DONE area=BloodMoor map=80x80 spawn=(3,8) grid=(3,8)"),
        L(27, "[P41] WILD area=BloodMoor map=80x80 seed=777 spawn=(3,8) walkable=4399 blocked=2001 "
               "exits=[(1,8),(79,79)] caveEntrance=(79,79) monsterSpawnPoints=0"),
        L(28, "[P41] MONSTERS aliveCount=43 aliveRows=43 rows=43 inOneScreen=2 within20Cells=7 walkable=4399 "
               "expectedFromMonDen=41.9 formula=walkable*520/100000*1.83"),
        L(29, "[P41] SHOT-OK n=6 state=wild name=p41_06_wild.png kind=camera bytes=1200000 path=C:/x/p41_06_wild.png"),
        L(30, "[P41] WIDE-SHOT which=wild orthoOverride=12 overrideOk=1 center=(40,40) camPos=(0.00,0.00) "
               "note=probe-only"),
        L(31, "[P41] SHOT-OK n=7 state=wild-wide name=p41_07_wild_wide.png kind=camera bytes=1200000 "
               "path=C:/x/p41_07_wild_wide.png"),
        L(32, "[P41] CAM-RESTORED rigValue=3.75 camValue=3.75 expected=3.75"),
        L(33, "[P41] MEASURE-BEGIN mode=run running=1 dir=S delta=(1,1) target=(9,14) from=(3,8) straightLine=1 "
               "expect=3.00 window=3.0s animAtStart=anim=Idle"),
        L(34, "[P41] SPEED mode=run cells=9.012 secs=3.001 speed=3.003 expect=3.000 worldUnits=6.373 "
              "cellSpace=1 movingFrames=180/180 cellSize=1"),
        L(35, "[P41] ANIM-RUN anim=Run frames=8 baseFps=24.00 speedScale=1.000 effFps=24.00 "
               "measuredFrameAdvancePerSec=23.95 anim=Run key=D2/Chars/amazon/run_s_3 frame=3/8 baseFps=24 "
               "speedScale=1 loop=True sprite=run_s_3"),
        L(36, "[P41] MEASURE-BEGIN mode=walk running=0 dir=E delta=(1,-1) target=(20,0) from=(9,14) straightLine=1 "
               "expect=1.40 window=3.0s animAtStart=anim=Walk"),
        L(37, "[P41] SPEED mode=walk cells=4.205 secs=3.002 speed=1.401 expect=1.400 worldUnits=5.947 "
              "cellSpace=1 movingFrames=180/180 cellSize=1"),
        L(38, "[P41] ANIM-WALK anim=Walk frames=8 baseFps=12.00 speedScale=0.933 effFps=11.20 "
               "measuredFrameAdvancePerSec=11.15 anim=Walk key=D2/Chars/amazon/walk_e_3 frame=3/8 baseFps=12 "
               "speedScale=0.933 loop=True sprite=walk_e_3"),
        L(39, "[P41] RUNRESTORED running=1"),
        L(40, "[P41] DIR-BEGIN n=1 want=S delta=(1,1) target=(14,19) straightTarget=1 grid=(9,14)"),
        L(41, "[P41] DIR-SHOT n=1 tile=p41_08_dir_s.png want=S dir=S dirOk=1 key=D2/Chars/amazon/walk_s_3 keyOk=1 "
               "sprite=walk_s_3 spriteOk=1 moving=1 grid=(11,16) anim=Walk key=D2/Chars/amazon/walk_s_3 "
               "frame=3/8 baseFps=12 speedScale=0.933 loop=True"),
        L(42, "[P41] SHOT-OK n=20 state=dir-S name=p41_08_dir_s.png kind=camera bytes=1200000 path=C:/x/p41_08_dir_s.png"),
        L(43, "[P41] DIR-SHOT n=2 tile=p41_09_dir_n.png want=N dir=N dirOk=1 key=D2/Chars/amazon/walk_n_0 keyOk=1 "
               "sprite=walk_n_0 spriteOk=1 moving=1 grid=(9,13) anim=Walk key=D2/Chars/amazon/walk_n_0 "
               "frame=0/8 baseFps=12 speedScale=0.933 loop=True"),
        L(44, "[P41] SHOT-OK n=21 state=dir-N name=p41_09_dir_n.png kind=camera bytes=1200000 path=C:/x/p41_09_dir_n.png"),
        L(45, "[P41] DIR-SHOT n=3 tile=p41_10_dir_e.png want=E dir=E dirOk=1 key=D2/Chars/amazon/walk_e_6 keyOk=1 "
               "sprite=walk_e_6 spriteOk=1 moving=1 grid=(11,12) anim=Walk key=D2/Chars/amazon/walk_e_6 "
               "frame=6/8 baseFps=12 speedScale=0.933 loop=True"),
        L(46, "[P41] SHOT-OK n=22 state=dir-E name=p41_10_dir_e.png kind=camera bytes=1200000 path=C:/x/p41_10_dir_e.png"),
        L(47, "[P41] DIR-SHOT n=4 tile=p41_11_dir_w.png want=W dir=W dirOk=1 key=D2/Chars/amazon/walk_w_2 keyOk=1 "
               "sprite=walk_w_2 spriteOk=1 moving=1 grid=(10,13) anim=Walk key=D2/Chars/amazon/walk_w_2 "
               "frame=2/8 baseFps=12 speedScale=0.933 loop=True"),
        L(48, "[P41] SHOT-OK n=23 state=dir-W name=p41_11_dir_w.png kind=camera bytes=1200000 path=C:/x/p41_11_dir_w.png"),
        L(49, "[P41] FINISH why=end step=30 shots=11 timeoutAt=(none) playerDead=0 rigOrthoNow=3.75 camOrthoNow=3.75 "
               "step=30 fsm=Stage scene=Stage area=BloodMoor map=80x80 grid=(10,13) dir=W moving=0 running=1 t=88.10"),
        L(50, "[P41] CHK name=osize ok=1 rig=3.75 cam=3.75 visibleWorldH=7.5"),
        L(51, "[P41] VERDICT ok=1 osize=1,osize-restored=1,speedconst=1,speed-run=1,speed-walk=1,anim-run-fps=1,"
               "anim-walk-fps=1,town-size=1,wild-size=1,exit-tiles=1,wild-monsters=1,dir-shots=1,byline=1,"
               "menu-items=1,no-timeout=1,player-alive=1"),
        L(52, "[P41] TOUR-DONE ok=end steps=30 shots=11 timeoutAt=(none)"),
    ]
    # the driver publishes `[P41] KEY=value` (Drive.KV) -- reproduce that byte shape here so the
    # fixture exercises p41_sheet.normalize() exactly like the real log does
    kv_keys = ("BOOT|MENU|MENU-BUTTONS|MENU-FORBIDDEN|TOWN|OSIZE|SPEEDCONST|WIDE-SHOT|CAM-RESTORED|"
               "WALKTO|EXITSTEP|EXITSTEP-DONE|WILD|MONSTERS|SPEED|ANIM-RUN|ANIM-WALK|DIR-BEGIN|"
               "DIR-SHOT|FINISH|MEASURE-BEGIN|RUNRESTORED|EXITTILES|BYLINE|UITEXTS|CHARSELECT|STAGE")
    rx = re.compile(r"(\[P41\] (?:" + kv_keys + r")) ")
    lines = [rx.sub(r"\1=", ln) for ln in lines]

    with open(LOG, "w", encoding="utf-8", newline="") as fh:
        fh.write("\n".join(lines) + "\n")

    trace = [
        "01:00:01.000 BEGIN tag=selftest",
        '01:05:00.000 CONSOLE {   "success": true,   "command": "command console_status",   "data": {     '
        '"result": {       "entries": [],       "counts": {         "error": 0,         "warn": 16,         '
        '"log": 511       },       "groundTruth": {         "consoleErrors": 0,         "consoleWarnings": 16 '
        '      }     }   } }',
        "01:05:01.000 TILES-LANDED 11/11",
        "01:05:01.500 SUMMARY tag=selftest cfgOk=True done=True tiles=11/11 verdicts=1 chk=16 timeouts=0 logLines=120",
    ]
    with open(TRACE, "w", encoding="utf-8", newline="") as fh:
        fh.write("\n".join(trace) + "\n")


def main():
    make_tiles()
    make_log()
    rc = subprocess.call([sys.executable, os.path.join(HERE, "p41_sheet.py"),
                          TILES, OUT, TSV, LOG, TRACE])
    print("\nselftest exit=%d (0 = every grid PASS)" % rc)
    return rc


if __name__ == "__main__":
    sys.exit(main())
