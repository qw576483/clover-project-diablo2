# -*- coding: utf-8 -*-
"""r1_sheet_selftest.py -- offline self-proof for r1_sheet.py (JUDGEMENT ASSET, no Play needed).

WHY IT EXISTS (same reason as p41_sheet_selftest.py):
  `r1_sheet.py` decides what every grid of the R1 contact sheet shows by matching the driver's
  `[R1] KEY=value` lines.  A regex that silently stops matching (a renamed key, the
  `KEY=value` -> `KEY value` normalisation, a `SHOT-OK` field order change) would make the index
  look plausible while the measured values quietly became "(miss)".  This test feeds the sheet
  script a SYNTHETIC frozen log + synthetic tiles and asserts the extraction works, in ~2 s and
  without touching the editor -- so the sheet's assumptions are re-checkable forever.

Usage:
    python tools/probes/measure/r1_sheet_selftest.py      # exit 0 = the fixture extracted cleanly

It writes its fixtures into <repo>/.ai-tmp/test/r1_selftest/ (one-off scratch, safe to delete).
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

OUT = os.path.join(REPO, ".ai-tmp", "test", "r1_selftest")

TILES = (["a01_town_wide.png", "a02_town_bridge.png", "a03a_bridge_click_050.png",
          "a03b_bridge_click_150.png", "a04_water_fallback.png",
          "a05a_amazon_idle.png", "a05b_barbarian_idle.png",
          "a06a_amazon_transition.png", "a06b_barbarian_transition.png",
          "a07a_amazon_front.png", "a07b_barbarian_front.png", "a08_name_digits.png"]
         + ["a09_walk_%d.png" % i for i in range(1, 7)]
         + ["a10_dialog_not_started.png", "a11_dialog_in_progress.png",
            "a12_shop_and_dialog.png", "a13_shop_closed.png",
            "a14_dialog_button_pre.png", "a15_shop_title_hint.png", "a16_questlog.png"])

# the fixture is written in the driver's real `KEY=value` form on purpose: the sheet script's
# normalisation step must turn it into the `NAME rest=...` shape the patterns are written in.
LOG = [
    "[2026-09-20 20:00:00.000] [Info] [R1] WIDE=requested ortho=28 (probe only; shipped value is 3.75) rigSetOk=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] WIDE-READY=rigOrtho=28 camOrtho=28 camPos=(8.00,-24.00) visibleWorldH=56.0 visibleWorldW=99.6 mapWorldW=94.0 mapWorldH=47.0 (56x40 iso, measured)",
    "[2026-09-20 20:00:00.000] [Info] [R1] TOWN=tag=a1 area=Town map=56x40 seed=1 spawn=(32,28) walkable=1528 blocked=712 exits=[(17,26),(17,27),(17,28)] npcPoints=5 playerGrid=(32,28) running=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CAM-RESTORED=rigOrtho=3.75 camOrtho=28 expected=3.75",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=10 state=a1-town-wide name=a01_town_wide.png kind=camera bytes=123",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=11 state=a2-town-bridge name=a02_town_bridge.png kind=camera bytes=123",
    "[2026-09-20 20:00:00.000] [Info] [R1] OSIZE=tag=a2 rigValue=3.75 camValue=3.75 visibleWorldH=7.5 visibleWorldW=13.333 pxPerWorldUnit=144.0 camPos=(16.00,-37.50)",
    "[2026-09-20 20:00:00.000] [Info] [R1] HOP=why=a2-west-bank to=(45,29) walkable=1 tileKind=Dirt from=(45,29)",
    "[2026-09-20 20:00:00.000] [Info] [R1] CELLSCREEN=tag=a2 screen=1920x1080 origin=bottom-left bridge(46,25)=1680@756 bridge(47,27)=1536@540 water(47,29)=1248@396 water(50,29)=1680@180",
    "[2026-09-20 20:00:00.000] [Info] [R1] A3-SETUP=stand=(45,29) bridgeTarget=(47,27) pathCells=5 targetKind=Dirt targetIsBridgeDeck=1 targetOnScreenOffset=(576,0) relToFrameCentre frame=1920x1080 (|x|<=960 |y|<=540 required for a real click)",
    "[2026-09-20 20:00:00.000] [Info] [R1] CK-BEGIN=want=(47,27) tileKind=Dirt walkable=1 playerGrid=(45,29)",
    "[2026-09-20 20:00:00.000] [Info] [R1] CK-PROJ=want=(47,27) got=(47,27) ok=1 flipY=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] CK-DONE=want=(47,27) walkable=1 tileKind=Dirt moveCmdDelta=1 playerGrid=(45,29) moving=1 elapsed=0.18",
    "[2026-09-20 20:00:00.000] [Info] [Player] [Move] steps=5 path=(45,29)->(45,28) from=(45,29) to=(47,27) speed=3格/秒(跑)",
    "[2026-09-20 20:00:00.000] [Info] [R1] A3-T0500=playerGrid=(45,27) world=(17.86,-36.57) moving=1 sinceClick=0.74 tileKindHere=Dirt",
    "[2026-09-20 20:00:00.000] [Info] [R1] A3-T1500=playerGrid=(47,27) world=(16.00,-37.00) moving=0 sinceClick=1.74 tileKindHere=Dirt",
    "[2026-09-20 20:00:00.000] [Info] [R1] A3-ARRIVED=playerGrid=(47,27) want=(47,27) tileKindHere=Dirt walkable=1 isBridgeDeck=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=12 state=a3-t0.5 name=a03a_bridge_click_050.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=13 state=a3-t1.5 name=a03b_bridge_click_150.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A4-PICK=waterCell=(47,30) tileKind=Rock walkable=0 expectedLanding=(46,30) landingKind=Dirt landingWalkable=1 playerGrid=(47,27) targetOnScreenOffset=(-432,-216) relToFrameCentre",
    "[2026-09-20 20:00:00.000] [Info] [R1-B] [R1-B] 点到不可走格 ⇒ 最近可走格回退（生效）：点击 (47,30) 地形=Rock ⇒ 落到 (46,30) 地形=Dirt，路径 6 格。生效口径：仅「图内但不可走」回退，半径 ≤ 2 格",
    "[2026-09-20 20:00:00.000] [Info] [R1] CK-DONE=want=(47,30) walkable=0 tileKind=Rock moveCmdDelta=1 playerGrid=(47,27) moving=1 elapsed=0.2",
    "[2026-09-20 20:00:00.000] [Info] [R1] A4-ARRIVED=playerGrid=(46,30) tileKindHere=Dirt landedOnWalkable=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=14 state=a4-water-fallback name=a04_water_fallback.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CC-READY=classIndex=-1 hoverSlot=-1 nameBuffer=\"Hero\" nameCaret=4 nameEditing=True transSlot=-1 transFrame=-1 transCount=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=1 which=amazon-idle-baseline slot=0 shownState=0 classIndex=-1 transSlot=-1 code= frame=-1/0 elapsed=0 sprite=nu1_0 native=118x198 rect=212.4x356.4 nativeAspect=0.5960 rectAspect=0.5960 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=2 which=barbarian-idle-baseline slot=2 shownState=0 classIndex=-1 transSlot=-1 code= frame=-1/0 sprite=nu1_0 native=86x183 rect=154.8x329.4 nativeAspect=0.4699 rectAspect=0.4699 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=1 state=a5a name=a05a_amazon_idle.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=2 state=a5b name=a05b_barbarian_idle.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CC-CLICK=target=SpotAmazon (real mouse) before=classIndex=-1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CC-CLICK=target=SpotBarbarian (real mouse) before=classIndex=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] PFRAME=slot=0 frame=21 sprite=fw_21 native=215x228 rect=387.0x410.4 ratioW=1.800 ratioH=1.800 rectAspect=0.9430 nativeAspect=0.9430",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=3 which=amazon-transition-mid slot=0 shownState=1 classIndex=0 transSlot=0 code=fw frame=21/54 elapsed=0.866 sprite=fw_21 native=215x228 rect=387.0x410.4 nativeAspect=0.9430 rectAspect=0.9430 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=5 which=barbarian-transition-mid slot=2 shownState=1 classIndex=4 transSlot=2 code=fw frame=21/64 elapsed=0.866 sprite=fw_21 native=139x215 rect=250.2x387.0 nativeAspect=0.6465 rectAspect=0.6465 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=3 state=a6a name=a06a_amazon_transition.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=5 state=a6b name=a06b_barbarian_transition.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CC-TRANS-END=amazon after=classIndex=0 hoverSlot=0 nameBuffer=\"Hero\" transSlot=-1 transFrame=53 transCount=54",
    "[2026-09-20 20:00:00.000] [Info] [R1] CC-TRANS-END=barbarian after=classIndex=4 transSlot=-1 transFrame=63 transCount=64",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=4 which=amazon-front-end slot=0 shownState=2 classIndex=0 transSlot=-1 sprite=nu3_0 native=121x234 rect=217.8x421.2 nativeAspect=0.5171 rectAspect=0.5171 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] PORTRAIT=n=6 which=barbarian-front-end slot=2 shownState=2 classIndex=4 transSlot=-1 sprite=nu3_0 native=95x201 rect=171.0x361.8 nativeAspect=0.4726 rectAspect=0.4726 ratioW=1.800 ratioH=1.800 preserveAspect=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=4 state=a7a name=a07a_amazon_front.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=6 state=a7b name=a07b_barbarian_front.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] TYPE-PICK=candidate=\"Ama9x\" fullName=\"HeroAma9x\" saveExists=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] TYPE-PICK=candidate=\"Ama65x\" fullName=\"HeroAma65x\" saveExists=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] TYPE-STR=typed=\"Ama65x\" defaultName=\"Hero\" digitsPresent=1 saveModuleAvailable=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] TYPE=char[0]='A' queued=1 via=InputSystem.QueueTextEvent -> Keyboard.onTextInput",
    "[2026-09-20 20:00:00.000] [Info] [R1] TYPE=char[4]='x' queued=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] NAME=where=after-typing typed=\"Ama65x\" buffer=\"HeroAma65x\" digitsInBuffer=1 visible_input=\"HeroAma65x|\" visible_label=\"HeroAma65x|\" display=\"HeroAma65x|\" caret=10 maxLen=15 screen=933,896 rect=812x59",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=7 state=a8 name=a08_name_digits.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9-SPOT=from=(28,12) to=(28,0) cells=12 dir=NE running=1 moveSpeed=3 expectedCellsPerSec=3.0 (run) 1.4 (walk)",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=1 frame=1472 t=43.9 motorWorld=(16.06,-20.47) viewWorld=(16.06,-20.47) cell=(28.500,12.436) dWorldFromPrev=0.0717 dCellFromPrev=0.0642 moving=1 grid=(28,12) dt=0.02139",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=2 frame=1475 t=44.0 motorWorld=(16.47,-20.27) viewWorld=(16.47,-20.27) cell=(28.500,12.032) dWorldFromPrev=0.4519 dCellFromPrev=0.4041 moving=1 grid=(28,12) dt=0.01728",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=3 frame=1478 t=44.1 motorWorld=(16.86,-20.07) viewWorld=(16.86,-20.07) cell=(28.500,11.642) dWorldFromPrev=0.4357 dCellFromPrev=0.3897 moving=1 grid=(28,11) dt=0.01490",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=4 frame=1481 t=44.2 motorWorld=(17.26,-19.87) viewWorld=(17.26,-19.87) cell=(28.500,11.242) dWorldFromPrev=0.4472 dCellFromPrev=0.4000 moving=1 grid=(28,11) dt=0.01520",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=5 frame=1484 t=44.3 motorWorld=(17.66,-19.67) viewWorld=(17.66,-19.67) cell=(28.500,10.842) dWorldFromPrev=0.4472 dCellFromPrev=0.4000 moving=1 grid=(28,10) dt=0.01510",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9=n=6 frame=1487 t=44.4 motorWorld=(18.06,-19.47) viewWorld=(18.06,-19.47) cell=(28.500,10.442) dWorldFromPrev=0.4472 dCellFromPrev=0.4000 moving=1 grid=(28,10) dt=0.01540",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=21 state=a9-frame name=a09_walk_1.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=22 state=a9-frame name=a09_walk_2.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=23 state=a9-frame name=a09_walk_3.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=24 state=a9-frame name=a09_walk_4.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=25 state=a9-frame name=a09_walk_5.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=26 state=a9-frame name=a09_walk_6.png kind=camera bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A9-DONE=shots=6 playerGrid=(28,4)",
    "[2026-09-20 20:00:00.000] [Info] [R1] A10-SPOT=akara=(41,19) standAt=(41,20) dist=1.000 talkRange=2.4 akaraCellKind=Dirt akaraWalkable=1 standKind=Dirt",
    "[2026-09-20 20:00:00.000] [Info] [R1] NPCS=count=5 currentNpcId=-1 denState=NotStarted denRemaining=0 canTurnIn=0 dialogOpen=0 shopOpen=0 |0=阿卡拉@(41,19) questGiver=1 shop=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A10-CLICKRESULT=result=clicked akara=(41,19) moveCmdDelta=1 playerGrid=(41,20)",
    "[2026-09-20 20:00:00.000] [Info] [Npc] 点击 (41,19) 落在 阿卡拉 的对话范围（2.40 格）内 ⇒ 走过去后自动对话",
    "[2026-09-20 20:00:00.000] [Info] [R1] DIALOG=tag=a10 open=1 npcName=\"阿卡拉\" canAcceptQuest=True canTurnInQuest=False options=[0:\"離開\" / 1:\"重要消息\" / 2:\"交易\"] text=\"在荒地中有一個極度邪惡的地方。\" layer=Normal currentNpcId=0 denState=NotStarted dialogOpen=1 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1-E] S1 生效：对话条在 Normal 层、商店在 Popup 层",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=15 state=a10 name=a10_dialog_not_started.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] UI-OPTION=tag=a11 index=1 node=Option1 options=[0:\"離開\" / 1:\"重要消息\" / 2:\"交易\"] canAcceptQuest=1 canTurnInQuest=0 hasShop=1 buttons=13 candidates=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CLICKTOP=tag=a11 index=1 node=Option1 screen=(960.0,700.0) rect=119x26 raycastTop=\"Option1@[UI]/Normal/NpcDialogPanel(Clone)/Option1\"",
    "[2026-09-20 20:00:00.000] [Info] [R1] QUEST=tag=a11-after-accept denState=InProgress denRemaining=0 canTurnInDen=0 quests=|1=InProgress name=\"邪惡洞穴\" progress=0/0",
    "[2026-09-20 20:00:00.000] [Info] [R1] DIALOG=tag=a11 open=1 npcName=\"阿卡拉\" canAcceptQuest=False canTurnInQuest=False options=[0:\"離開\" / 1:\"交易\"] text=\"除非你殺死…\" layer=Normal currentNpcId=0 denState=InProgress dialogOpen=1 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] A11-STATE=moveCmds=3 playerGrid=(41,19) currentNpcId=0 denState=InProgress dialogOpen=1 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=16 state=a11 name=a11_dialog_in_progress.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] UI-OPTION=tag=a12 index=1 node=Option1 options=[0:\"離開\" / 1:\"交易\"] canAcceptQuest=0 canTurnInQuest=0 hasShop=1 buttons=12 candidates=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A12-STATE=uiRetried=0 shopOpen=1 dialogOpen=1 shopLayer=Popup dialogLayer=Normal moveCmds=3 currentNpcId=0 denState=InProgress dialogOpen=1 shopOpen=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOP=tag=a12 open=1 layer=Popup title=\"阿卡拉\" hint=\"买入\" titleLabelNull=0 hintLabelNull=0 titleScreen=960,861 titleRect=518x25 hintScreen=960,831 hintRect=518x25 titleNodeText=\"(no-node)\" hintNodeText=\"(no-node)\" dialogOpen=1 shopOpen=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=17 state=a12 name=a12_shop_and_dialog.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOP=tag=a15 open=1 layer=Popup title=\"阿卡拉\" hint=\"买入\" titleScreen=960,861 titleRect=518x25 hintScreen=960,831 hintRect=518x25 titleNodeText=\"(no-node)\" hintNodeText=\"(no-node)\"",
    "[2026-09-20 20:00:00.000] [Info] [R1] CROP=n=90 tile=a15_shop_title_hint.png node=ShopTitle sx0=900 sy0=840 sx1=1020 sy1=880 cx=960 cy=861",
    "[2026-09-20 20:00:00.000] [Info] [R1] CROP=n=91 tile=a15_shop_title_hint.png node=ShopHint sx0=900 sy0=810 sx1=1020 sy1=850 cx=960 cy=831",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOPYTABLE=tag=a15 shopTitlePosY=(0.00, 321.45) shopHintPosY=(0.00, 291.45) shopTabBottomY=335.7 shopGridTopY=277.2",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=18 state=a15 name=a15_shop_title_hint.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A13-CLICK=clicking the shop Close button (real mouse) before=currentNpcId=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] A13-STATE=uiRetried=0 shopOpen=0 dialogOpen=1 currentNpcId=0 denState=InProgress dialogOpen=1 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] DIALOG=tag=a13 open=1 npcName=\"阿卡拉\" options=[0:\"離開\" / 1:\"交易\"] denState=InProgress dialogOpen=1 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=19 state=a13 name=a13_shop_closed.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] A14-PRECLICK=playerGrid=(41,19) moveCmdsBefore=3 dialogOpen=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=20 state=a14 name=a14_dialog_button_pre.png kind=screen bytes=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] CLICKTOP=tag=a14 index=0 node=Option0 screen=(956.4,328.1) rect=119x26 raycastTop=\"Option0@[UI]/Normal/NpcDialogPanel(Clone)/Option0\"",
    "[2026-09-20 20:00:00.000] [Info] [R1] A14-RESULT=uiRetried=0 moveCmdsBefore=3 moveCmdsAfter=3 moveCmdDelta=0 gridBefore=(41,19) gridAfter=(41,19) gridChanged=0 playerMoving=0 dialogOpen=0 currentNpcId=-1 dialogOpen=0 shopOpen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] A16-STATE=questLogOpen=1",
    "[2026-09-20 20:00:00.000] [Info] [R1] QUEST=tag=a16-questlog denState=InProgress denRemaining=0 canTurnInDen=0",
    "[2026-09-20 20:00:00.000] [Info] [R1] SHOT-OK n=21 state=a16 name=a16_questlog.png kind=screen bytes=1",
]


def main():
    from PIL import Image
    os.makedirs(OUT, exist_ok=True)
    for i, t in enumerate(TILES):
        im = Image.new("RGB", (640, 360), (20 + (i * 5) % 200, 40, 60))
        for x in range(0, 640, 20):
            for y in range(0, 360, 20):
                if (x // 20 + y // 20) % 2 == 0:
                    im.putpixel((x, y), (240, 240 - i, 30))
        im.save(os.path.join(OUT, t))
    frozen = os.path.join(OUT, "fixture_frozen.txt")
    with open(frozen, "w", encoding="utf-8") as fh:
        fh.write("\n".join(LOG) + "\n")
    trace = os.path.join(OUT, "fixture_trace.txt")
    with open(trace, "w", encoding="utf-8") as fh:
        fh.write('SUMMARY tag=selftest cfgOk=True done=True tiles=%d/%d\n'
                 % (len(TILES), len(TILES)))
        fh.write('DEVICE graphicsDeviceName="AMD Radeon RX 5700 XT"\n')
    out_png = os.path.join(OUT, "selftest_contact.png")
    out_tsv = os.path.join(OUT, "selftest_contact.index.tsv")
    sheet = os.path.join(HERE, "r1_sheet.py")
    r = subprocess.run([sys.executable, sheet, OUT, out_png, out_tsv, frozen, trace],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    print(r.stdout.strip())
    if r.stderr.strip():
        print(r.stderr.strip())
    ok = True
    if not os.path.exists(out_png) or not os.path.exists(out_tsv):
        print("SELFTEST-FAIL sheet/index not produced")
        ok = False
    else:
        txt = open(out_tsv, encoding="utf-8").read()
        for bad in ("通过", "一致", "PASS", "FAIL"):
            if bad in txt:
                print("SELFTEST-FAIL index contains a verdict word: %s" % bad)
                ok = False
        if "(miss)" in txt:
            for line in txt.splitlines():
                if "(miss)" in line:
                    print("SELFTEST-FAIL a field did not extract: %s" % line[:160])
            ok = False
        if "missingLogPatterns" in txt:
            for line in txt.splitlines():
                if "missingLogPatterns" in line:
                    print("SELFTEST-FAIL a required log pattern is missing: %s" % line[:160])
            ok = False
    print("SELFTEST-%s (fixture %d tiles, %d log lines)"
          % ("OK" if ok else "FAIL", len(TILES), len(LOG)))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
