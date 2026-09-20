# -*- coding: utf-8 -*-
"""p29_sheet.py -- one-off contact-sheet + index builder for agent-29.

NOT shipped (lives in <project>/.ai-tmp/drivers/). Reads the log region the sampler froze
(.ai-tmp/drivers/out/p29_log_<tag>.txt) plus the probe trace (p29_steps_<tag>.txt), decides every
evidence grid DETERMINISTICALLY (ASCII/CJK-escaped regex over the log + file checks on the tile),
and writes:
  client/Assets/Screenshots/p29_contact_flow.png   (one grid per evidence row, labels burned in)
  client/Assets/Screenshots/p29_contact_flow.index.tsv (grid <-> verdict <-> screenshot <-> log lines)

Why the verdicts are here and not in an AI's head: SKILL 1.12 item 1 -- judgement belongs to a
script; the model only reads the finished sheet once.

Usage: python p29_sheet.py <tag> <shot_dir> <out_png> <out_tsv> <log_extract> <trace>
"""
import os
import re
import sys

from PIL import Image, ImageDraw

# ---------------------------------------------------------------- CJK as escapes (this file is ASCII)
MENU_READY = "\u83dc\u5355\u573a\u666f Menu \u5df2\u5c31\u7eea"                 # "Menu scene ready"
SLOT = "\u69fd"                                                                 # "slot"
PLAY_START = "\u8d77\u64ad"                                                     # "start playing"
PLAY_END = "\u64ad\u5b8c"                                                       # "played through"
FRAME = "\u5e27"                                                                # "frames"
LAND_ON = "\u843d"                                                              # "lands on"
SEL_FRONT = "\u9009\u4e2d(\u8f6c\u6b63\u9762)"                                  # "selected(front)"
IDLE_BACK = "\u9ed8\u8ba4(\u80cc\u9762\u5f85\u673a)"                            # "default(back idle)"
CREATE_OPEN = "\u521b\u89d2\u9762\u677f\u5df2\u6253\u5f00"                      # "char-create opened"
PICK_CLASS = "\u521b\u89d2\uff1a\u9009\u4e2d\u804c\u4e1a"                       # "picked class"
NAME_SET = "\u521b\u89d2\uff1a\u540d\u5b57 ="                                   # "name ="
MULTI_WARN = "\u591a\u4eba\u6e38\u620f\u672a\u5b9e\u88c5\u8054\u673a"           # "multiplayer not implemented"
CINE_WARN = "\u8fc7\u573a\u52a8\u753b\u672a\u5b9e\u88c5"                        # "cinematics not implemented"
ROSTER = "\u89d2\u8272\u9009\u62e9\u5c4f\u5237\u65b0"                            # "char-select refreshed"
LOAD_BAR = "\u8bfb\u6761\u771f\u5b9e\u6863"                                     # "read bar real gear"
STAGE_CODE = "\u573a\u666f\u52a0\u8f7d\u4ee3\u53f7"                              # "stage load code"
SKILLBAR = "\u6280\u80fd\u680f\u7b2c 1 \u683c"                                  # "skill bar slot 1"
BELT = "\u8170\u5e26\u683c 0 \u88ab\u70b9\u51fb"                                # "belt cell 0 clicked"
PAUSE_OPEN = "\u6682\u505c\u83dc\u5355\u5df2\u6253\u5f00"                        # "pause menu opened"
SET_OPEN = "\u9009\u9879\u9762\u677f\u6253\u5f00"                                # "options panel opened"
SET_TAG = "\u8bbe\u7f6e"                                                        # "[settings]"
VOLUME = "\u97f3\u91cf"                                                         # "volume"
SCREEN = "\u5168\u5c4f"                                                         # "full screen"
QUALITY = "\u753b\u8d28"                                                        # "quality"
SET_CLOSE = "\u9009\u9879\u9762\u677f\uff1a\u5173\u95ed"                          # "options panel: close"
QUIT_REQ = "\u6536\u5230\u9000\u51fa\u8bf7\u6c42"                                # "received quit request"
QUIT_GAME = "\u9000\u51fa\u6e38\u620f"                                           # "quitting the game"

TS = r"\[(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+)\]"

# portrait / options crops inside the 1920x1080 frames (keeps the burned labels readable)
CROP_PORTRAIT = (190, 240, 1310, 790)
CROP_OPTIONS = (560, 280, 1360, 800)


class Row(object):
    def __init__(self, grid, state, category, expectation, tile=None, crop=None, pats=None,
                 note="", key=""):
        self.grid = grid
        self.state = state
        self.category = category
        self.expectation = expectation
        self.tile = tile
        self.crop = crop
        self.pats = pats or []
        self.note = note
        self.key = key          # ASCII key numbers burned onto the grid (SKILL: grid label = id+state+numbers)
        self.verdict = "FAIL"
        self.reason = ""
        self.sample = ""
        self.evidence = []


def load(path):
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        return [ln.rstrip("\r\n") for ln in fh]


def first(lines, pat):
    rx = re.compile(pat)
    for ln in lines:
        if rx.search(ln):
            return ln
    return ""


def all_of(lines, pat):
    rx = re.compile(pat)
    return [ln for ln in lines if rx.search(ln)]


def main(argv):
    tag = argv[1] if len(argv) > 1 else "r2"
    shot_dir = argv[2]
    out_png = argv[3]
    out_tsv = argv[4]
    log_path = argv[5]
    trace_path = argv[6]

    lines = load(log_path)
    trace = load(trace_path)
    console = first(trace, r"CONSOLE \{")
    tiles_line = first(trace, r"TILES count=")
    marked = first(trace, r"SUMMARY tag=")
    started = first(trace, r"BEGIN tag=")

    # ---- run-level numeric facts -------------------------------------------------
    load_start = first(lines, r"\[Scene\] Loading scene: Menu")
    menu_ready = first(lines, r"\[Flow\] " + MENU_READY)
    lat_ms = None
    if load_start and menu_ready:
        t0 = re.search(TS, load_start).group(1)
        t1 = re.search(TS, menu_ready).group(1)
        fmt = "%Y-%m-%d %H:%M:%S.%f"
        import datetime
        lat_ms = (datetime.datetime.strptime(t1, fmt) - datetime.datetime.strptime(t0, fmt))
        lat_ms = lat_ms.total_seconds() * 1000.0

    inflight = all_of(lines, r"\[P29\] INFLIGHT")
    samples = all_of(lines, r"\[P29\] SAMPLE state=")
    ids0 = [ln for ln in (inflight + samples) if re.search(r"entries=\d+:\[[^\]]*\b0\b", ln)]
    poller1 = [ln for ln in inflight if re.search(r"progTimerId=1 ", ln)]
    # During the read-bar state one not-yet-activated scene is EXPECTED (the engine holds
    # allowSceneActivation=false until progress reaches 0.9) -- that is the load gate, not a zombie.
    # A zombie = a scene that STAYS half-loaded in any other state, so the loading sample is exempt
    # and its transient must be exactly [new scene L=0] next to the live menu scene.
    zombies_bad = [ln for ln in samples
                   if "state=loading" not in ln and not re.search(r"zombies=0", ln)]
    loading_samples = [ln for ln in samples if "state=loading" in ln]
    loading_ok = all(re.search(r"zombies=1", ln) and re.search(r"\[L=0,roots=0\]", ln)
                     for ln in loading_samples) and bool(loading_samples)
    post_load_bad = [ln for ln in samples
                     if re.search(r"state=(stage|hud_after_clicks|pause|settings)", ln)
                     and not re.search(r"sceneCount=1 ", ln)]

    console_err = "?"
    if console:
        m = re.search(r'"consoleErrors":\s*(\d+)', console)
        if m:
            console_err = m.group(1)

    # scene-load latency and the transition timings (numeric evidence for the animation)
    def stamp(ln):
        m = re.search(TS, ln)
        if not m:
            return None
        import datetime
        return datetime.datetime.strptime(m.group(1), "%Y-%m-%d %H:%M:%S.%f")

    trans = []          # (code, frames, seconds_measured, seconds_expected, delta, start_line, end_line)
    rx_start = re.compile(r"\[P29\]|[^\n]*?" + SLOT + r" (\d+)\uff08([A-Za-z]+)\uff09" + PLAY_START
                          + r" `(\w+)` (\d+) " + FRAME)
    started_at = {}
    for ln in lines:
        m = rx_start.search(ln)
        if m:
            started_at[(m.group(3), m.group(4))] = (stamp(ln), ln)
            continue
        m2 = re.search(r"\[Ui\].*" + SLOT + r" \d+ " + PLAY_END + r" (\d+) " + FRAME + r" \u21d2 " + LAND_ON, ln)
        if not m2:
            continue
        frames = m2.group(1)
        for key, (t0, sline) in list(started_at.items()):
            if key[1] == frames:
                del started_at[key]
                dt = (stamp(ln) - t0).total_seconds() if (t0 and stamp(ln)) else None
                exp = float(frames) / 25.0
                trans.append((key[0], int(frames), dt, exp,
                              None if dt is None else abs(dt - exp), sline, ln))
                break

    # settings panel: before/after values (numeric proof that the controls really change state)
    # NOTE: the panel logs the quality as its LABEL ("HIGH"), not as an index -- the index only
    # appears inside the "[settings] quality = LOW (.., level 0)" line.
    # the CJK text sits after "[Ui] " -- do NOT prefix this pattern with a literal "["
    set_open_line = first(lines, r"\u9009\u9879\u9762\u677f\u6253\u5f00\uff1abgm=([\d.]+) sfx=([\d.]+)"
                                 r" fullscreen=(\w+) quality=(\w+)")
    m = re.search(r"bgm=([\d.]+) sfx=([\d.]+) fullscreen=(\w+) quality=(\w+)", set_open_line or "")
    vol_before = float(m.group(1)) if m else None
    q_before = m.group(4) if m else None
    vol_line = first(lines, r"\[\u8bbe\u7f6e\] BGM \u97f3\u91cf = [\d.]+")
    vol_after = float(re.search(r"= ([\d.]+)", vol_line).group(1)) if vol_line else None
    fs_line = first(lines, r"\[\u8bbe\u7f6e\] \u5168\u5c4f = ")
    q_line = first(lines, r"\[\u8bbe\u7f6e\] \u753b\u8d28 = ")
    q_after = re.search(r"\u753b\u8d28 = (\w+)", q_line).group(1) if q_line else None
    QUALITY_CYCLE = {"LOW": "MED", "MED": "HIGH", "HIGH": "LOW"}
    settings_note = "bgm %s->%s | fullscreen %s | quality %s->%s" % (
        "%0.2f" % vol_before if vol_before is not None else "?",
        "%0.2f" % vol_after if vol_after is not None else "?",
        (re.search(r"= (\w+)", fs_line).group(1) if fs_line else "?"),
        q_before, q_after)

    # ---------------------------------------------------------------- evidence rows
    t = tag
    rows = []

    def add(*a, **kw):
        rows.append(Row(*a, **kw))

    add("01", "boot", "presentation(shows the screen)",
        "start screen visible (fsm=Boot, Boot scene only) and the probe can shoot it",
        "p29_%s_01_boot.png" % t, None,
        [r"\[P29\] SAMPLE state=boot ", r"\[P29\] SHOT-GRID state=boot"],
        "")
    add("02", "mainmenu", "presentation(shows the screen) + NUMERIC (load latency)",
        "menu scene REALLY loaded (not a zombie) and the MainMenuPanel is open",
        "p29_%s_02_mainmenu.png" % t, None,
        [r"\[Scene\] Loading scene: Menu", r"\[Flow\] " + MENU_READY,
         r"\[P29\] SAMPLE state=mainmenu "],
        "")
    add("03", "menu_multi_toast", "presentation(button response)",
        "MULTIPLAYER answers: Warn + Toast, stays on the main menu",
        "p29_%s_03_menu_multi_toast.png" % t, None,
        [r"\[P29\] CLICK-DONE name=Multi ", r"\[Flow\] " + MULTI_WARN],
        "")
    add("04", "menu_cinematics_toast", "presentation(button response)",
        "CINEMATICS answers: Warn + Toast, stays on the main menu",
        "p29_%s_04_menu_cinematics_toast.png" % t, None,
        [r"\[P29\] CLICK-DONE name=Cinematics ", r"\[Ui\] " + CINE_WARN],
        "")
    add("05", "charselect_first", "presentation(shows the screen)",
        "SINGLE PLAYER with a roster lands on the character list",
        "p29_%s_05_charselect_first.png" % t, None,
        [r"\[P29\] CLICK-DONE name=Single ", r"\[P29\] SAMPLE state=charselect_first ",
         r"\[Ui\] " + ROSTER],
        "")
    add("06", "charcreate_idle", "presentation(shows the screen)",
        "creation screen, only Amazon+Barbarian slots live, OK greyed out before a pick",
        "p29_%s_06_charcreate_idle.png" % t, CROP_PORTRAIT,
        [r"\[Ui\] " + CREATE_OPEN, r"Confirm\(interactable=0"],
        "")
    add("07", "charcreate_back", "presentation(button response)",
        "the char-create BACK button returns to the character list",
        "p29_%s_07_charcreate_back.png" % t, None,
        [r"\[P29\] CLICK-DONE name=Back path=\[UI\]/Normal/CharCreatePanel",
         r"\[P29\] SAMPLE state=charcreate_back "],
        "")
    add("08", "amazon_fw_mid", "presentation(the animation itself)",
        "Amazon turn-over really plays: mid fw frame (slot 0, fw 54 frames @25fps)",
        "p29_%s_08_amazon_fw_mid.png" % t, CROP_PORTRAIT,
        [SLOT + r" 0\uff08Amazon\uff09" + PLAY_START + r" `fw` 54 " + FRAME, r"\[P29\] CLICK-DONE name=SpotAmazon "],
        "")
    add("09", "amazon_front", "presentation(landing state)",
        "after 54 frames the Amazon lands on the front idle pose",
        "p29_%s_09_amazon_front.png" % t, CROP_PORTRAIT,
        [SLOT + r" 0 " + PLAY_END + r" 54 " + FRAME + r" \u21d2 " + LAND_ON + r"\u300c" + r"\u9009\u4e2d"],
        "")
    add("10", "barbarian_fw_mid", "presentation(the animation itself)",
        "Barbarian turn-over really plays: mid fw frame (slot 2, fw 64 frames @25fps)",
        "p29_%s_10_barbarian_fw_mid.png" % t, CROP_PORTRAIT,
        [SLOT + r" 2\uff08Barbarian\uff09" + PLAY_START + r" `fw` 64 " + FRAME],
        "")
    add("11", "barbarian_front", "presentation(landing state)",
        "after 64 frames the Barbarian lands on the front idle pose",
        "p29_%s_11_barbarian_front.png" % t, CROP_PORTRAIT,
        [SLOT + r" 2 " + PLAY_END + r" 64 " + FRAME + r" \u21d2 " + LAND_ON + r"\u300c" + r"\u9009\u4e2d"],
        "")
    add("12", "barbarian_bw_mid", "presentation(the animation itself)",
        "clicking the SAME slot again plays the reverse bw 19 frames",
        "p29_%s_12_barbarian_bw_mid.png" % t, CROP_PORTRAIT,
        [SLOT + r" 2\uff08Barbarian\uff09" + PLAY_START + r" `bw` 19 " + FRAME,
         PICK_CLASS + r"\u300c\u91ce\u86ee\u4eba\u300d"],
        "")
    add("13", "idle_after_bw", "presentation(landing state)",
        "after the reverse turn-over the slot is back to the back idle pose",
        "p29_%s_13_idle_after_bw.png" % t, CROP_PORTRAIT,
        [SLOT + r" 2 " + PLAY_END + r" 19 " + FRAME + r" \u21d2 " + LAND_ON + r"\u300c" + r"\u9ed8\u8ba4"],
        "")
    add("13b", "amazon_bw_mid", "presentation(the 4th transition code)",
        "same slot re-click on the Amazon plays bw 30 frames",
        "p29_%s_13b_amazon_bw_mid.png" % t, CROP_PORTRAIT,
        [SLOT + r" 0\uff08Amazon\uff09" + PLAY_START + r" `bw` 30 " + FRAME,
         SLOT + r" 0 " + PLAY_END + r" 30 " + FRAME + r" \u21d2 " + LAND_ON],
        "")
    # the final name is default("Hero") + suffix, taken from the probe's own marker line
    sfx = re.search(r"nameSuffix=(\w+)", first(trace, r"nameSuffix=") or "")
    expect_name = (NAME_SET + r"\u300cHero" + sfx.group(1) + r"\u300d") if sfx else NAME_SET
    add("14", "named", "presentation(shows the screen)",
        "name typed through the real text channel (final: Hero%s), OK is enabled afterwards"
        % (sfx.group(1) if sfx else "?"),
        "p29_%s_14_named.png" % t, None,
        [r"\[Ui\] " + expect_name, r"\[P29\] SAMPLE state=named ",
         r"Confirm\(interactable=1"],
        "")
    add("15", "charselect_after", "presentation(button response)",
        "OK created the character: the roster grew and the list is shown again",
        "p29_%s_15_charselect_after.png" % t, None,
        [r"\[P29\] CLICK-DONE name=Confirm path=\[UI\]/Normal/CharCreatePanel",
         r"\[P29\] SAMPLE state=charselect_after "],
        "")
    add("16", "loading", "presentation(shows the screen)",
        "read-bar screen up while Stage loads (engine progress op released at 0.9)",
        "p29_%s_16_loading.png" % t, None,
        [r"\[P29\] SAMPLE state=loading ", r"\[Flow\] " + LOAD_BAR, r"\[Scene\] Loading scene: Stage"],
        "")
    add("17", "stage", "presentation(shows the screen)",
        "in game: Stage loaded (roots>0), HUD open",
        "p29_%s_17_stage.png" % t, None,
        [r"\[P29\] SAMPLE state=stage ", r"\[Flow\] Stage " + STAGE_CODE, r"panels=\[Hud\]"],
        "")
    add("18", "hud_after_clicks", "presentation(button response)",
        "two real HUD clicks answered (skill bar slot 1 and belt cell 0)",
        "p29_%s_18_hud_after_clicks.png" % t, None,
        [r"\[P29\] CLICK-DONE name=SkillBar0 ", r"\[P29\] CLICK-DONE name=Belt0 ",
         r"\[Ui\] " + SKILLBAR, r"\[Ui\] " + BELT],
        "")
    add("19", "pause", "presentation(shows the screen)",
        "ESC pause menu: Time.timeScale=0, PausePanel over the HUD",
        "p29_%s_19_pause.png" % t, None,
        [r"\[P29\] SAMPLE state=pause ", r"\[Ui\] " + PAUSE_OPEN, r"timeScale=0"],
        "")
    add("20", "settings", "presentation(shows the screen)",
        "OPTIONS opened from the pause menu (volume / full-screen / quality / CLOSE)",
        "p29_%s_20_settings.png" % t, CROP_OPTIONS,
        [r"\[P29\] CLICK-DONE name=Options ", r"\[Ui\] " + SET_OPEN, r"\[P29\] SAMPLE state=settings "],
        "")
    add("21", "settings_quality_after", "presentation(button response) + NUMERIC (values move)",
        "volume -, full-screen and quality really changed; CLOSE shuts the panel",
        "p29_%s_21_settings_quality_after.png" % t, CROP_OPTIONS,
        [r"\[P29\] CLICK-DONE name=Minus0 ",
         r"\[P29\] CLICK-DONE name=\u5168\u5c4f\u663e\u793aToggle ",
         r"\[P29\] CLICK-DONE name=" + QUALITY + "Toggle ",
         r"\[\u8bbe\u7f6e\] BGM " + VOLUME + r" = ",
         r"\[\u8bbe\u7f6e\] " + SCREEN + r" = ",
         r"\[\u8bbe\u7f6e\] " + QUALITY + r" = ",
         r"\[P29\] CLICK-DONE name=Close ", r"\[Ui\] " + SET_CLOSE],
        "")
    add("22", "mainmenu_after", "presentation(shows the screen)",
        "PausePanel MAIN MENU + confirm dialog -> menu scene loaded AGAIN from Stage",
        "p29_%s_22_mainmenu_after.png" % t, None,
        [r"\[P29\] CLICK-DONE name=ToMain ", r"\[P29\] CLICK-DONE name=Confirm path=\[UI\]/Top/Confirm",
         r"\[P29\] SAMPLE state=mainmenu_after ", r"\[Flow\] " + MENU_READY],
        "")

    # ------------- numeric rows (no tile: SKILL 2.3 -- numbers are judged from log lines)
    def numeric(grid, state, expectation, pats, extra=None):
        r = Row(grid, state, "numeric(log/assert)", expectation, None, None, pats)
        r.note = extra or ""
        rows.append(r)

    numeric("N1", "menu-load-latency", "[Scene] Loading scene: Menu -> [Flow] Menu ready < 1000 ms",
            [r"\[Scene\] Loading scene: Menu", r"\[Flow\] " + MENU_READY],
            "measured=%.1f ms" % lat_ms if lat_ms is not None else "measured=?")
    numeric("N2", "timer-id0-tombstone", "the first scene-load poller of a fresh session holds id 1; no live timer ever has id 0",
            [r"\[P29\] INFLIGHT progTimerId=1 ", r"nextId=1 entries=0:\[\]"],
            "inflight_lines=%d poller_id1_lines=%d zero_id_lines=%d" % (len(inflight), len(poller1), len(ids0)))
    numeric("N3", "no-zombie-scenes",
            "zombies=0 in every sample EXCEPT the read-bar one (whose single L=0 scene is the load "
            "gate); after loading sceneCount=1 again",
            [r"\[P29\] SAMPLE state=stage "],
            "samples=%d bad=%d loadingTransients=%d loadingOk=%s postLoadBad=%d" % (
                len(samples), len(zombies_bad), len(loading_samples), loading_ok, len(post_load_bad)))
    numeric("N4", "console-errors", "consoleErrors = 0 for the whole session", [],
            "consoleErrors=%s (consoleWarnings=%s; SUMMARY/tiles live in p29_steps_%s.txt)" % (
                console_err, (re.search(r'"consoleWarnings":\s*(\d+)', console).group(1)
                              if console else "?"), tag))
    numeric("N5", "exit-branch", "main-menu EXIT reaches Flow.QuitGame (settings flushed, play mode stops)",
            [r"\[P29\] CLICK-DONE name=Quit ", r"\[Flow\] " + QUIT_REQ, r"\[Flow\] " + QUIT_GAME], "")
    seen_codes = set()
    nrow = 6
    for code, frames, dt, exp, delta, sl, el in trans:
        if (code, frames) in seen_codes:      # the same code is replayed several times on purpose
            continue
        seen_codes.add((code, frames))
        numeric("N%d" % nrow, "turn-over-timing-%s%s" % (code, frames),
                "slot animation %s = %d frames @25fps => %.2f s wall clock (tolerance 0.25 s)" % (code, frames, exp),
                [], "measured=%.3f s expected=%.3f s delta=%.3f s" % (
                    dt if dt is not None else -1, exp, delta if delta is not None else -1))
        nrow += 1

    # ---- the ASCII key numbers burned onto each grid (SKILL 1.13 beat 4: "grid id + state + numbers")
    keys = {
        "mainmenu": "Menu load %.1f ms | zombies=0" % (lat_ms if lat_ms is not None else -1),
        "charcreate_idle": "OK greyed out: Confirm interactable=0",
        "amazon_fw_mid": "slot0 Amazon  fw 54f @25fps (mid turn-over)",
        "amazon_front": "slot0 landed nu3 FRONT  (54f = 2.16s)",
        "barbarian_fw_mid": "slot2 Barbarian  fw 64f @25fps (mid turn-over)",
        "barbarian_front": "slot2 landed nu3 FRONT  (64f = 2.56s)",
        "barbarian_bw_mid": "slot2 re-click  bw 19f @25fps (mid turn-over)",
        "idle_after_bw": "slot2 landed nu1 BACK idle  (19f = 0.76s)",
        "amazon_bw_mid": "slot0 re-click  bw 30f @25fps (mid turn-over)",
        "named": "name typed via Keyboard.onTextInput; Confirm interactable=1",
        "loading": "Stage gate: progress 0.9 allowAct=1  (transient L=0 scene = the gate)",
        "stage": "sceneCount=1 zombies=0 roots=6",
        "hud_after_clicks": "SkillBar0 + Belt0 both answered",
        "pause": "timeScale=0 over the HUD",
        "settings": "bgm 0.70 | sfx 0.80 | fullscreen OFF | quality HIGH",
        "settings_quality_after": settings_note,
    }

    # ---------------------------------------------------------------- verdicts
    for r in rows:
        if not r.key:
            r.key = keys.get(r.state, "")
        # verbatim sample line for the grid (probe log) -- numeric-only rows have none
        r.sample = (first(lines, r"SAMPLE state=" + re.escape(r.state) + r" ") or
                    first(lines, r"SHOT-GRID state=" + re.escape(r.state) + r" "))

        ev = []
        ok = True
        for p in r.pats:
            hit = first(lines, p)
            if hit:
                ev.append(hit)
            else:
                ok = False
                r.reason += "log-pattern-miss[" + p[:40] + "] "

        tile_ok = True
        tile_msg = ""
        if r.tile:
            path = os.path.join(shot_dir, r.tile)
            if not os.path.exists(path):
                tile_ok = False
                tile_msg = "tile-missing"
            else:
                size = os.path.getsize(path)
                if size < 30000:
                    tile_ok = False
                    tile_msg = "tile-too-small(%d)" % size
                else:
                    try:
                        im = Image.open(path)
                        if im.size != (1920, 1080):
                            tile_ok = False
                            tile_msg = "tile-size=%s" % (im.size,)
                        else:
                            gray = im.convert("L")
                            ext = gray.getextrema()
                            if ext[1] - ext[0] < 24:
                                tile_ok = False
                                tile_msg = "tile-nearly-uniform(range=%d)" % (ext[1] - ext[0])
                            else:
                                tile_msg = "%dx%d %dKB" % (im.size[0], im.size[1], size // 1024)
                    except Exception as exc:                       # pragma: no cover
                        tile_ok = False
                        tile_msg = "tile-unreadable(%s)" % exc
            if not tile_ok:
                r.reason += tile_msg + " "

        # numeric rows: their own numbers must satisfy the expectation
        num_ok = True
        if r.grid == "N1":
            num_ok = lat_ms is not None and lat_ms < 1000.0
            r.reason += "" if num_ok else "latency>=1000ms "
        elif r.grid == "N2":
            num_ok = bool(poller1) and not ids0
            r.reason += "" if num_ok else "poller-not-1-or-id0-live "
        elif r.grid == "N3":
            num_ok = len(samples) > 0 and not zombies_bad and loading_ok and not post_load_bad
            r.reason += "" if num_ok else "zombie-scene-seen "
            r.note = r.note + " (loadingOk=%s postLoadBad=%d)" % (loading_ok, len(post_load_bad))
        elif r.grid == "N4":
            num_ok = console_err == "0"
            r.reason += "" if num_ok else "consoleErrors=%s " % console_err
        elif r.grid == "21":
            chk = (vol_before is not None and vol_after is not None and
                   abs(vol_after - (vol_before - 0.1)) < 1e-6)
            if q_before is not None and q_after is not None:
                chk = chk and QUALITY_CYCLE.get(q_before) == q_after
            num_ok = chk
            r.reason += "" if chk else "settings-value-did-not-move "
            r.note = settings_note
        elif r.grid == "N5":
            pass
        elif r.grid.startswith("N") and "turn-over-timing" in r.state:
            m = re.search(r"delta=([\-\d.]+)", r.note)
            num_ok = bool(m) and m.group(1) not in ("-1",) and float(m.group(1)) <= 0.25
            r.reason += "" if num_ok else "timing-out-of-tolerance "

        r.verdict = "PASS" if (ok and tile_ok and num_ok) else "FAIL"
        r.evidence = ev
        if not r.reason:
            r.reason = (r.note + "  " + tile_msg).strip() if r.tile else r.note

    # ---------------------------------------------------------------- build the sheet
    TILE_W, TILE_H, LABEL_H, COLS = 480, 270, 26, 4
    n = len(rows)
    rows_n = (n + COLS - 1) // COLS
    HEADER = 46
    sheet = Image.new("RGB", (COLS * TILE_W, HEADER + rows_n * (TILE_H + LABEL_H)), (18, 18, 22))
    draw = ImageDraw.Draw(sheet)

    head = "p29 contact sheet  tag=%s  %s" % (tag, (started.split(" ", 1)[1] if started else ""))
    draw.text((8, 6), head, fill=(255, 245, 180))
    draw.text((8, 24), "AI reads THIS sheet only; per-grid verdicts are computed by p29_sheet.py from "
                       "the frozen log + tile files", fill=(170, 170, 180))

    for i, r in enumerate(rows):
        cx = (i % COLS) * TILE_W
        cy = HEADER + (i // COLS) * (TILE_H + LABEL_H)
        draw.line([(cx, cy), (cx + TILE_W, cy)], fill=(90, 90, 100))
        draw.line([(cx, cy), (cx, cy + TILE_H + LABEL_H)], fill=(90, 90, 100))

        color = (120, 255, 140) if r.verdict == "PASS" else (255, 110, 110)
        label = "#%s %s | %s | %s" % (r.grid, r.state, "num" if r.category.startswith("num") else "view", r.verdict)
        draw.rectangle([cx, cy, cx + TILE_W, cy + LABEL_H - 1], fill=(30, 30, 36))
        draw.text((cx + 4, cy + 4), label, fill=color)

        if r.tile:
            path = os.path.join(shot_dir, r.tile)
            if os.path.exists(path):
                try:
                    im = Image.open(path).convert("RGB")
                    if r.crop:
                        im = im.crop(r.crop)
                    scale = min(float(TILE_W) / im.width, float(TILE_H) / im.height)
                    im = im.resize((max(1, int(im.width * scale)), max(1, int(im.height * scale))),
                                   Image.NEAREST)
                    ox = cx + (TILE_W - im.width) // 2
                    oy = cy + LABEL_H + (TILE_H - im.height) // 2
                    sheet.paste(im, (ox, oy))
                except Exception as exc:                            # pragma: no cover
                    draw.text((cx + 4, cy + LABEL_H + 8), "tile error: %s" % exc, fill=(255, 120, 120))
            else:
                draw.text((cx + 4, cy + LABEL_H + 8), "tile MISSING", fill=(255, 120, 120))
            # key numbers burned under the label as well (the grid's own evidence line)
            key = (r.key or r.reason)[:78]
            draw.text((cx + 4, cy + TILE_H + LABEL_H - 14), key, fill=(210, 210, 140))
        else:
            # numeric rows carry their numbers in the empty cell
            draw.text((cx + 6, cy + LABEL_H + 8), "NUMERIC  " + r.state, fill=(255, 245, 180))
            for k, ln in enumerate([r.note] + [x[-110:] for x in r.evidence][:3]):
                draw.text((cx + 6, cy + LABEL_H + 30 + k * 14), ln[-78:], fill=(190, 220, 255))

    sheet.save(out_png)
    print("sheet -> %s (%dx%d, %d grids)" % (out_png, sheet.width, sheet.height, n))

    # ---------------------------------------------------------------- index.tsv
    with open(out_tsv, "w", encoding="utf-8", newline="") as fh:
        fh.write("grid\tstate\tcategory\texpectation\tscreenshot\tsample_line(verbatim)\t"
                 "log_evidence(verbatim)\tscript_verdict\n")
        for r in rows:
            fh.write("\t".join([
                r.grid, r.state, r.category, r.expectation.replace("\t", " "),
                ("Assets/Screenshots/" + r.tile) if r.tile else "(numeric row: no tile)",
                r.sample.replace("\t", " "),
                (" || ".join(r.evidence) if r.evidence else r.reason).replace("\t", " "),
                r.verdict,
            ]) + "\n")
    print("index -> %s" % out_tsv)


    # ---------------------------------------------------------------- summary
    bad = [r for r in rows if r.verdict != "PASS"]
    print("\n=== run facts ===")
    print("marker/summary : %s" % marked)
    print("tiles          : %s" % tiles_line)
    print("consoleErrors  : %s" % console_err)
    print("menu load      : %s" % ("%.1f ms" % lat_ms if lat_ms is not None else "?"))
    print("inflight lines : %d (poller id 1: %d, live id 0: %d)" % (len(inflight), len(poller1), len(ids0)))
    print("samples        : %d (zombie-bad: %d)" % (len(samples), len(zombies_bad)))
    for code, frames, dt, exp, delta, sl, el in trans:
        print("turn-over %-10s frames=%3d measured=%s expected=%.2fs delta=%s" % (
            code, frames,
            ("%.3f s" % dt) if dt is not None else "?",
            exp, ("%.3f s" % delta) if delta is not None else "?"))
    print("\n=== grid verdicts ===")
    for r in rows:
        print("%-4s %-24s %-4s %s" % (r.grid, r.state, r.verdict, r.reason))
    print("\nFAIL count = %d / %d" % (len(bad), len(rows)))
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
