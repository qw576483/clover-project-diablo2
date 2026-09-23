# -*- coding: utf-8 -*-
"""把原版 `.DC6` 解成 PNG 并落进工程的 `Resources/Clover/D2/**`（§B：DC6 → PNG）。

依赖 `dc6.py`（同目录）。**只读** MPQ 解包产物，**只写**两处：
  ① `client/Assets/Resources/Clover/D2/**`（像素素材）；
  ② `<原版资源>/导出的字体映射/*.tsv`（**只有 `chifont` 组**，字体表导出的"帧→字符"映射，非像素素材）。

来源行（写进任何报告里都要带上）：
  原版素材取自 Diablo II (Blizzard North, 2000) 的 d2data.mpq / patch_d2.mpq，非商用。

调色板口径（**不是猜的**）：见 `dc6.py::read_pl2` 的 docstring —— 与 Diablerie 源仓库同图 PNG
逐像素比对全等（差异 0）后定为 `R,G,B,A`。本脚本对每类素材用哪张 pl2 逐条写在 `PL2_OF` 注释里。

用法：
  python export_d2ui.py <d2dc6 根> <工程 client 目录> [--only <组名>]

组名见 GROUPS；不带 --only = 全跑。

─────────────────────────────────────────────────────────────────────────────
本轮（片 1「原版 UI 素材地基」）新增的 4 组（**只增不改**，上面的组一个字没动）：

| 组 | 源（相对 <d2dc6>） | 输出（相对 <client>） | 帧数 | PL2 |
|---|---|---|---|---|
| `skilltree` | `data/global/ui/SPELLS/skltree_{a,b,n,p,s}_back.DC6` | `Assets/Resources/Clover/D2/UI/SkillTree/skltree_{cls}_back_{i}.png` | 每类 16 | ACT1 |
| `automap` | `data/global/ui/MINIMAP/mapicons.DC6` | `.../D2/UI/MiniMap/mapicon_{i}.png` | 8 | ACT1（**实测不影响像素**，见下） |
| `chifont` | `data/LOCAL/FONT/chi/{Font16,font24,font30,font42}.DC6` | `.../D2/Fonts/font{N}_chi.png`（**整幅图集**）+ `原版资源/导出的字体映射/font{N}_chi.tsv`（帧→字符映射） | 每种 13806 | ACT1 |
| `menu` | `data/global/ui/MENU/{boxpieces,helpborder,upgrade,okcancelbtn,buttontempok,buttontempcancel,endgameok,EndGame}.DC6` + `a{n}q{m}.dc6`(21 个) | `.../D2/UI/Menu/*.png` / `.../D2/UI/Quest/*.png` | 见 `MENU_*` 表 | ACT1；**`EndGame.dc6` 用 `EndGame/Pal.PL2`**（见下） |

本轮（片 4「启动链路三屏 1:1」）新增的 2 组（**只增不改**，上面的组一个字没动）：

| 组 | 源（相对 <d2dc6>） | 输出（相对 <client>） | 帧数 | PL2 |
|---|---|---|---|---|
| `frontend` | `data/global/ui/FrontEnd/{amazon,barbarian,necromancer,paladin,sorceress}/{CLS}{NU1,NU2,NU3}.DC6` | `Assets/Resources/Clover/D2/UI/FrontEnd/{cls}/{nu1,nu2,nu3}_{i}.png` | 每职业 3 态（26+26+18 / 16+16+26 / 26+26+9 / 12+12+12 / 32+32+12） | **fechar** |
| `logo` | `data/global/ui/Logo/logo.DC6` | `.../D2/UI/Logo/logo_{i}.png` | 1（319×177） | **fechar** |

**逐帧口径**：除 `chifont` 外，每个源 DC6 的每一帧解出**一个 PNG**，文件名 = `{输出 stem}_{DC6 帧号}.png`，
帧号从 0 起 —— 与 `python tools/d2codec/dc6.py png <源> <pl2> <临时目录> <stem>` 的产出**逐字节同名同内容**，
所以「工程内已落位的 PNG」可以对每帧做 SHA256 全等自证（见回报的逐组自证）。

**为什么 `skilltree`/`helpborder`/`EndGame` 不拼成整幅**：它们是 tile 打包（每帧 256×256 / 64×256 …），
但「怎么摆」缺权威依据（`MENU/EndGame.dc6` 甚至不知道是 320×480×2 还是 320×960）。
按任务书「语义/拼装参数搞不清的照解不误」⇒ 本轮**只做 1:1 逐帧落位**，拼装口径留给后续片。
（`panels` 组早先已按 320×432 拼过 `skltree_*_back_{0..3}.png` 到 `UI/Panel/`，本轮**没动**它。）

**`chifont` 为什么是整幅图集**：每帧只有 13×13（font16）/19×19（font24）/24×24（font30）/37×37（font42），
却有 13806 帧 —— 逐帧落盘会产生 55224 个 PNG（本项目现有一千余个，会直接压垮 Assets）。
⇒ 按**行主序规则网格**打成一张图集（列数 = 由帧数算出的因子，见运行输出；格子 = 该字体帧尺寸），
**一帧不丢也一帧不多**。帧→字符的对应关系**另有权威依据**，不是猜的：
`<原版资源>/d2raw/data/local/font/chi/font{N}.tbl`（字体指标表，格式见下方 `_read_font_tbl`），
本组同时把它导成 `font{N}_chi.tsv`（列：`frame, code, width, height, col, row`）。
"""

import os
import sys

import dc6

SRC_ROOT = None      # <d2dc6 根>，如 c:\...\_assets_src\d2dc6
DST_ROOT = None      # <工程>\client
PL2_ROOT = None      # <d2raw>\data\global\palette
RAW_ROOT = None      # <d2raw>（非 DC6 的原始产物：字体 `.tbl` 等）

# 每类素材用哪张 PL2（**依据**）：
#   ACT1 —— 游戏内 UI（控制面板 / 背包 / 物品图标 / 技能图标）+ `data/local/ui/chi/**` 的
#           中文标题条。实测：`ctrlpnl7.DC6` / `invchar.DC6` 用 ACT1 解出的图与
#           Diablerie 源仓库同图 PNG 一致（肉眼可辨：雕像是石灰色、物品是暖褐）。
#   Menu1 —— 前端选人/创角屏（本项目现有 800×600 屏即该套色）。
#   loading —— **进图读条图**：原版 `LoadingScreen.cs:51` 明确用 `PaletteType.Loading`
#              （= `data\global\palette\loading\Pal.PL2`，见 Diablerie
#              `Engine/IO/D2Formats/PaletteType.cs:13` + `Palette.cs:20`）⇒ 读条图**必须**用这张，
#              与 ACT1 不是同一套色。
#   EndGame —— **只用于 `MENU/EndGame.dc6`**（片 1 新增）。依据三条：
#              ① 命名惯例（**有已坐实的先例**）：原版确实带 `data/global/palette/EndGame/Pal.PL2`
#                 （`PaletteType.cs:10` `EndGame = 5` / `Palette.cs:17` 给出该路径）。而
#                 `loading/Pal.PL2 ↔ Loading/loadingscreen.dc6` 这一对已由参考物源码坐实
#                 （Diablerie `LoadingScreen.cs:51` 用 `PaletteType.Loading`）⇒
#                 「调色板目录名 = 同名 DC6」是**已证惯例**，`EndGame/Pal.PL2` ↔ `MENU/EndGame.dc6` 同例。
#              ② 可观测判据：libd2 `packages/formats/src/palette.zig` L41-43
#                 ——「背景图是 dithered 的，用错调色板不是"染色"而是"**长麻点**"」。
#              ③ 实测（判定表 = `原版资源/参考图/调色板判定_EndGame对比表.png`）。度量口径
#                 （5 行可复现）：对第 0 帧，取每对「相邻且均不透明」的像素，算
#                 max(|ΔR|,|ΔG|,|ΔB|)，再求平均 = **平均颜色跳变**（越小越平滑）。
#                 `EndGame.dc6` 在 EndGame/Menu0 下 1.1/1.9、在 ACT1 下 10.4
#                 ⇒ **大差距、方向明确**（判定表里也肉眼可见：@EndGame 平滑、@ACT1/@loading 麻点）。
#                 方法学自证：该度量在**大差距**样例上挑对已知答案 —— `Loading/loadingscreen.dc6`
#                 取 loading（11.1 vs ACT1 62.1；参考物源码坐实）、`SPELLS/Skillicon.DC6` 取 ACT1
#                 （16.8 vs EndGame 26.7；与 Diablerie 同图 PNG 逐像素全等）。但在**小差距**
#                 （约 <20%）样例上会挑错（`PANEL/ctrlpnl7.DC6` 12.9 vs ACT1 14.4 会挑成 loading）
#                 ⇒ **只在大差距时才用它下结论**。
#   ⛔ 不要因为"看着像另一套色"就换调色板：换之前必须按②③带自证。
#
#   ⚠️ **BLOCKED 的部分（片 1 提出、片 5 收口一半）**：
#      ★ **已定案（片 5）**：`MENU/endgameok.dc6` → **`EndGame/Pal.PL2`**。
#        依据 = 与 `EndGame.dc6` 同一套判据且方向一致：麻点度量 ACT1 **84.5** vs EndGame **28.8**
#        （2.9 倍 = 大差距）+ 肉眼复核（@ACT1 满屏彩色噪点 / @EndGame 干净深灰石板按钮）。
#        落地见 `group_menu`（`stem == "endgameok"` 走 `endgame_pal`）。
#      ⏳ **仍未定案（保持 ACT1）**：`MENU/**` 其余文件。它们的度量差距 < 2 倍
#        （`okcancelbtn` 30.3 vs 30.9 基本持平；`buttontempok/cancel`、`boxpieces` 偏 EndGame
#        但幅度不够）⇒ 按"只在大差距时才用麻点判据"的口径**不许下结论**，
#        暂用参考物一侧的 DC6 默认口径 ACT1（Diablerie `EditorTools.cs:99` / `Spritesheet.cs:14`）。
#      **定案需要**：原版这些屏的实机截图，或 D2 客户端的「文件→调色板」表。
#   fechar —— **前端职业半身像 + D2 logo**（片 4 新增）。依据三条（逐条可复核）：
#              ① 参考物源码 `Diablerie/.../Menu/ClassSelect/ClassSelector.cs:168-175`：
#                 `Spritesheet.Load($"{classPath}NU1", PaletteType.Fechar)`（三态 + logo 同族）；
#              ② 路径出处 `Engine/IO/D2Formats/Palette.cs:19` → `data\global\palette\fechar\Pal.PL2`
#                 （`PaletteType.cs:12` `Fechar = 7`），本地实测该文件存在；
#              ③ 可复现的肉眼判别：`SONU1.DC6` 帧 0 按 fechar 解出 = 深绿斗篷 + 紫水晶法杖（正常）；
#                 按 `menu1` 解出 = 高亮发白的错色。`Logo/logo.DC6` 按 fechar 解出 = 红金火焰 DIABLO II
#                 字标（正常），按 Menu1 解出偏色。
PL2_ACT1 = "ACT1/Pal.PL2"
PL2_LOADING = "loading/Pal.PL2"
PL2_ENDGAME = "EndGame/Pal.PL2"
PL2_FECHAR = "fechar/Pal.PL2"

STATS = []


def res(*parts):
    return os.path.join(PL2_ROOT, *parts)


def src(*parts):
    return os.path.join(SRC_ROOT, *parts)


def out(*parts):
    return os.path.join(DST_ROOT, "Assets", "Resources", "Clover", "D2", *parts)


def raw(*parts):
    """`<原版资源>/d2raw/**` 下的原始产物（字体 `.tbl` 等）。"""
    return os.path.join(RAW_ROOT, *parts)


def fontmap(*parts):
    """字体映射表输出目录：`<原版资源>/导出的字体映射/`。

    为什么不放进工程：映射表**不是像素素材**，工程内 `Resources/**` 只放素材（§1.9）。
    怎么嵌进工程由后续片决定（建议走打表）。
    """
    return os.path.join(os.path.dirname(SRC_ROOT), "导出的字体映射", *parts)


def read_dc6(*parts):
    p = src(*parts)
    if not os.path.exists(p):
        print("  MISSING  %s" % p)
        return None
    return dc6.parse(open(p, "rb").read())


def one(frame, out_path, palette, note="", group=""):
    dc6.write_png_rgba(out_path, dc6.frame_rgba(frame, palette), frame.width, frame.height)
    STATS.append((group, out_path, frame.width, frame.height, note))


def compose(frames, canvas_w=None):
    f, cw = dc6.compose_frame(frames, canvas_w)
    return f


# ═════════════════════════════════════════════════════════════════════════════
# 组 1：按钮（原版三态）—— 唯一来源是原版 DC6 的帧，**不是**拼出来的近似
# ═════════════════════════════════════════════════════════════════════════════
def group_buttons(pal):
    """按钮：`FrontEnd/WideButtonBlank.dc6`(4 帧) / `MediumButtonBlank.dc6`(2) /
    `MediumSelButtonBlank.dc6`(2) / `CharSelect/ShortButtonBlank.dc6`(2) /
    `FrontEnd/{CancelButtonBlank,OkCancelButtonBlank}.dc6`(2)。

    ★ 原版布局（**实测帧尺寸**，不是估的）：
      WideButtonBlank.dc6  = 256×35 + 16×35 + 256×35 + 16×35
        ⇒ 第 1 个按钮 = 帧 0 与帧 1 **横向拼接** = 272×35；第 2 个 = 帧 2+3 = 272×35。
          两个按钮 = 原版的「常态 / 按下」两态（原版 `WideButton.prefab` 的
          `m_SpriteState.m_PressedSprite` 就是第二个）。
      MediumButtonBlank.dc6 = 128×35 × 2 ⇒ 常态 / 按下。
      MediumSelButtonBlank.dc6 = 128×35 × 2 ⇒ **选中（高亮）**态的常态 / 按下。
    """
    bar = res(*PL2_ACT1.split("/"))

    d = read_dc6("data", "global", "ui", "FrontEnd", "WideButtonBlank.dc6")
    if d and len(d.frames) >= 4:
        one(dc6.stack_h([d.frames[0], d.frames[1]]), out("UI", "Menu", "btn_wide_normal.png"), pal,
            "原版 WideButtonBlank.dc6 帧 0+1（272×35）", "buttons")
        one(dc6.stack_h([d.frames[2], d.frames[3]]), out("UI", "Menu", "btn_wide_pressed.png"), pal,
            "原版 WideButtonBlank.dc6 帧 2+3（272×35，按下）", "buttons")

    d = read_dc6("data", "global", "ui", "FrontEnd", "MediumButtonBlank.dc6")
    if d and len(d.frames) >= 2:
        one(d.frames[0], out("UI", "Menu", "btn_med_normal.png"), pal,
            "原版 MediumButtonBlank.dc6 帧 0（128×35）", "buttons")
        one(d.frames[1], out("UI", "Menu", "btn_med_pressed.png"), pal,
            "原版 MediumButtonBlank.dc6 帧 1（按下）", "buttons")

    # ★★ dialog-options（2026-09-24 定案）：**这一组的调色板与其他按钮不同** —— 别改回 `pal`(ACT1)。
    #   依据（可复跑：`tools/probes/measure/probe_med_sel_palette.py`，麻点判据 = 孤立高饱和像素占比）：
    #     · 同目录的**共享**按钮只有 ACT1 干净：WideButtonBlank 0.017 / MediumButtonBlank 0.040 /
    #       CancelButtonBlank 0.045，用 `fechar` 反而 0.104~0.177 起麻点；
    #     · 而 **FrontEnd 专属的 `MediumSelButtonBlank.dc6` 反过来**：ACT1 解出 **0.424**（麻点，历史缺陷：
    #       一悬停就把中等按钮的文案糊掉），**`fechar` 解出 0.054 / 0.055**（干净）。
    #     · 与本文件开头「frontend 组用 PL2_FECHAR」的既有惯例一致（类选人像那组就是 fechar）。
    #     · 曾猜过的 `menu1` 已被 15 套全量扫描**证伪**（0.271）。
    #   ⇒ 口径 =「同一个 DC6 目录里，共享按钮 ACT1 / FrontEnd 专属按钮 fechar」。
    pal_sel = dc6.read_pl2(res(*PL2_FECHAR.split("/")))
    d = read_dc6("data", "global", "ui", "FrontEnd", "MediumSelButtonBlank.dc6")
    if d and len(d.frames) >= 2:
        one(d.frames[0], out("UI", "Menu", "btn_med_sel.png"), pal_sel,
            "原版 MediumSelButtonBlank.dc6 帧 0（高亮/悬停；FrontEnd 组 ⇒ 调色板 %s）" % PL2_FECHAR, "buttons")
        one(d.frames[1], out("UI", "Menu", "btn_med_sel_pressed.png"), pal_sel,
            "原版 MediumSelButtonBlank.dc6 帧 1（高亮 + 按下；调色板 %s）" % PL2_FECHAR, "buttons")

    d = read_dc6("data", "global", "ui", "CharSelect", "ShortButtonBlank.dc6")
    if d:
        for i, f in enumerate(d.frames):
            one(f, out("UI", "Menu", "btn_short_%d.png" % i), pal,
                "原版 CharSelect/ShortButtonBlank.dc6 帧 %d" % i, "buttons")

    d = read_dc6("data", "global", "ui", "FrontEnd", "CancelButtonBlank.dc6")
    if d:
        for i, f in enumerate(d.frames):
            one(f, out("UI", "Menu", "btn_cancel_%d.png" % i), pal,
                "原版 FrontEnd/CancelButtonBlank.dc6 帧 %d" % i, "buttons")

    d = read_dc6("data", "global", "ui", "FrontEnd", "OkCancelButtonBlank.dc6")
    if d:
        for i, f in enumerate(d.frames):
            one(f, out("UI", "Menu", "btn_okcancel_%d.png" % i), pal,
                "原版 FrontEnd/OkCancelButtonBlank.dc6 帧 %d" % i, "buttons")

    # 面板上的小按钮（背包/腰带/小面板 7 键）—— 原版单帧图，逐一落位（UI 侧按原名取）
    small = [
        ("data/global/ui/PANEL/goldbtn.DC6", "Panel/goldbtn", 4),
        ("data/global/ui/PANEL/buysellbtn.DC6", "Panel/buysellbtn", 18),
        ("data/global/ui/PANEL/goldcoinbtn.dc6", "Panel/goldcoinbtn", 2),
        ("data/global/ui/PANEL/minipanelbtn.DC6", "Panel/minipanelbtn", 16),
        ("data/global/ui/PANEL/menubutton.DC6", "Panel/menubutton", 4),
        ("data/global/ui/PANEL/runbutton.dc6", "Panel/runbutton", 4),
    ]
    for path, stem, _n in small:
        d = read_dc6(*path.split("/"))
        if not d:
            continue
        for i, f in enumerate(d.frames):
            one(f, out("UI", *stem.split("/")[:-1], "%s_%d.png" % (stem.split("/")[-1], i)), pal,
                "原版 %s 帧 %d" % (path, i), "buttons")


# ═════════════════════════════════════════════════════════════════════════════
# 组 2：面板底图（拼装 DC6 的 tile → 整幅）
# ═════════════════════════════════════════════════════════════════════════════
PANELS = [
    # (dc6 相对路径, 输出名, 画布宽（None = 自动反推）, 备注)
    ("data/global/ui/MENU/questbackground.dc6", "quest_back.png", 320, "任务日志底图（与背包同为 320×432 口径）"),
    ("data/global/ui/MENU/dialogbackground.DC6", "dialog_back.png", None, "NPC 对话框底图（210×158 单帧）"),
    ("data/global/ui/PANEL/buysell.DC6", "buysell_back.png", 320, "商店「买卖」页底图"),
    ("data/global/ui/PANEL/trade.DC6", "trade_back.png", 320, "交易面板底图"),
]


def group_panels(pal):
    for path, name, cw, note in PANELS:
        d = read_dc6(*path.split("/"))
        if not d:
            continue
        f = compose(d.frames, cw)
        if f is None:
            print("  SKIP (不是 tile 打包) %s" % path)
            continue
        one(f, out("UI", "Panel", name), pal, "原版 %s（tile 拼装）%s" % (path, note), "panels")

    # 技能树底图：每类 16 帧 = 4 张 320×432（按 tile 拼装后纵向切成 4 片）
    for cls, stem in (("s", "skltree_s_back.DC6"), ("a", "skltree_a_back.DC6"),
                      ("n", "skltree_n_back.DC6"), ("p", "skltree_p_back.DC6"),
                      ("b", "skltree_b_back.DC6")):
        d = read_dc6("data", "global", "ui", "SPELLS", stem)
        if not d:
            continue
        f = compose(d.frames, 320)
        if f is None:
            continue
        page_h = 432
        pages = f.height // page_h if f.height >= page_h else 1
        for k in range(pages):
            sub = dc6.Frame(f.width, page_h, 0, 0, 0,
                            f.indices[k * page_h * f.width:(k + 1) * page_h * f.width])
            one(sub, out("UI", "Panel", "skltree_%s_back_%d.png" % (cls, k)), pal,
                "原版 SPELLS/%s 的 tile 拼装结果第 %d 片（320×432）" % (stem, k), "panels")


# ═════════════════════════════════════════════════════════════════════════════
# 组 3：技能图标（原版 48×48，每技能 **2 帧**：常态 + 灰化）
# ═════════════════════════════════════════════════════════════════════════════
#  依据：`SPELLS/AmSkillicon.DC6` = 60 帧、`So/Ne/Pa` = 60 帧、`Ba` = 156 帧，
#        全是 48×48 —— 与原版 Skill.txt 的「每职业 30 技能 × 2 帧」一致
#        （本项目 `Table/Skill.tsv` 实测：class 1..5 的 official_id 依次 6-35 / 36-65 /
#          66-95 / 96-125 / 126-155）。
#  ⇒ 帧号 = (official_id − (6 + 30×(class−1))) × 2 (+1 = 灰化帧)。
SKILL_ICON_FILES = {
    "ama": "AmSkillicon.DC6",
    "sor": "SoSkillicon.DC6",
    "nec": "NeSkillicon.DC6",
    "pal": "PaSkillicon.DC6",
    "bar": "BaSkillicon.DC6",
}


def group_skillicons(pal):
    for cls, fn in SKILL_ICON_FILES.items():
        d = read_dc6("data", "global", "ui", "SPELLS", fn)
        if not d:
            continue
        for i, f in enumerate(d.frames):
            one(f, out("UI", "SkillIcon", "%sSkillicon_%d.png" % (cls, i)), pal,
                "原版 SPELLS/%s 帧 %d" % (fn, i), "skillicons")

    # 原版「普通攻击」图标（左键默认技能）：`SPELLS/Skillicon.DC6` 24 帧 48×48
    d = read_dc6("data", "global", "ui", "SPELLS", "Skillicon.DC6")
    if d:
        for i, f in enumerate(d.frames):
            one(f, out("UI", "SkillIcon", "SkilliconAttack_%d.png" % i), pal,
                "原版 SPELLS/Skillicon.DC6 帧 %d" % i, "skillicons")


# ═════════════════════════════════════════════════════════════════════════════
# 组 4：物品图标（`data/global/items/inv*.DC6` → `inv{code}.png`）
# ═════════════════════════════════════════════════════════════════════════════
#  依据：本项目 `Table/Item.tsv` 的 `code` 列 = 官方 item code（hax / axe / lax / hp1 …），
#        而原版物品图标的文件名就是 `inv<code>.DC6`（小写）⇒ 图标路径 = `D2/Items/inv{code}`。
def group_items(pal):
    root = src("data", "global", "items")
    if not os.path.isdir(root):
        print("  MISSING  %s" % root)
        return
    n = 0
    for fn in sorted(os.listdir(root)):
        if not fn.lower().endswith(".dc6"):
            continue
        stem = fn[:-4].lower()
        if not stem.startswith("inv"):
            continue
        d = dc6.parse(open(os.path.join(root, fn), "rb").read())
        if not d.frames:
            continue
        f = d.frames[0]
        # 物品图标是单帧；>1 帧的文件（少数动画）取第 0 帧并在统计里注明
        extra = "" if len(d.frames) == 1 else "（原文件 %d 帧，取第 0 帧）" % len(d.frames)
        one(f, out("Items", stem + ".png"), pal, "原版 items/%s 帧 0%s" % (fn, extra), "items")
        n += 1
    print("  物品图标 %d 张" % n)


# ═════════════════════════════════════════════════════════════════════════════
# 组 5：中文标题条（`data/local/ui/chi/**`）—— 原版中文界面上的红金标题
# ═════════════════════════════════════════════════════════════════════════════
BANNERS = [
    "inventory.dc6", "character.dc6", "quests.dc6", "skillstree.dc6", "options.dc6",
    "exit.dc6", "npcspeech.dc6", "youdiedsoftcore.dc6", "returntogame.dc6", "automap.dc6",
    "MULTIPLAYER.DC6", "SINGLEPLAYER.DC6", "ReallyExit.dc6", "cancel.dc6", "previous.dc6",
    "cfgoptions.dc6", "soundoptions.dc6", "videooptions.dc6", "selfresurrect.dc6",
    "textonly.dc6", "AutoMapOptions.dc6", "AutoMapParty.dc6", "AutoMapCenter.dc6",
]


def group_banners(pal):
    for fn in BANNERS:
        d = read_dc6("data", "LOCAL", "UI", "chi", fn)
        if not d:
            continue
        stem = fn[:-4]
        for i, f in enumerate(d.frames):
            one(f, out("UI", "Banner", "%s_%d.png" % (stem, i)), pal,
                "原版 data/local/ui/chi/%s 帧 %d（中文标题条）" % (fn, i), "banners")


# ═════════════════════════════════════════════════════════════════════════════
# 组 6：小图标（小地图标记 / 任务图标 / 商店页签 / 任务页签）
# ═════════════════════════════════════════════════════════════════════════════
MISC = [
    ("data/global/ui/MINIMAP/mapicons.DC6", "UI/MiniMap/mapicon", "小地图标记（16×16 ×8）"),
    ("data/global/ui/MENU/questicons.dc6", "UI/Panel/questicon", "任务图标（50×52 ×3）"),
    ("data/global/ui/MENU/questtabs.dc6", "UI/Panel/questtab", "任务页签（78×30 ×8）"),
    ("data/global/ui/MENU/questbutton.DC6", "UI/Panel/questbutton", "任务页签按钮"),
    ("data/global/ui/MENU/questsockets.dc6", "UI/Panel/questsocket", "任务插槽"),
    ("data/global/ui/MENU/questdone.dc6", "UI/Panel/questdone", "任务完成标记"),
    ("data/global/ui/PANEL/buyselltabs.DC6", "UI/Panel/buyselltabs", "商店页签（79×31 ×8）"),
    ("data/global/ui/PANEL/tradebtn.DC6", "UI/Panel/tradebtn", "交易小按钮（77×17 ×2）"),
    ("data/global/ui/PANEL/clickbox.dc6", "UI/Panel/clickbox", "勾选框"),
    # ★ 本轮补：`overlap.DC6` 实测 dir=1 fpd=2、两帧 82×88（球高光遮罩）。
    #   此前工程里只有一张手工导的**整张** `overlap.png`（`FrameCountOverlap=2`
    #   声明两帧）⇒ 代码请求 `overlap_0/1` 必然 MISS，每次进 Play 2 条
    #   `[Error] [Resource] 加载失败：D2/UI/Panel/overlap_0/_1`。
    #   按其它素材同口径**逐帧落位**，两个请求都命中（修复该 Error）。
    ("data/global/ui/PANEL/overlap.DC6", "UI/Panel/overlap", "球高光遮罩 82×88 ×2"),
]


def group_misc(pal):
    for path, stem, note in MISC:
        d = read_dc6(*path.split("/"))
        if not d:
            continue
        parts = stem.split("/")
        for i, f in enumerate(d.frames):
            # ⚠️ `stem` 已经是 `UI/...` 开头 ⇒ **不要再加一层 `UI`**（加了会落到 `D2/UI/UI/**`）
            one(f, out(*parts[:-1], "%s_%d.png" % (parts[-1], i)), pal,
                "原版 %s 帧 %d（%s）" % (path, i, note), "misc")


# ═════════════════════════════════════════════════════════════════════════════
# 组 7：进图读条画面（D2 经典 10 帧「门/传送门开启」动画）
# ═════════════════════════════════════════════════════════════════════════════
#  依据（原版行为，**不是猜的**）——社区复刻工程 mofr/Diablerie 的
#  `Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs`：
#    :10  SpritesheetPath = @"data\global\ui\Loading\loadingscreen"
#    :51  Spritesheet.Load(SpritesheetPath, PaletteType.Loading)     ← 调色板 = loading/Pal.PL2
#    :43  Background 是铺满整屏的**纯黑**图（color = Color.black）
#    :58-61 Image 锚点/轴心/位置全 = 屏幕中心（居中），:68 SetNativeSize()
#    :66  spriteIndex = (int)((_sprites.Length - 1) * completeness)   ← 帧号 = 真实进度
#  ⇒ 进图画面 = 黑底 + **居中 256×256 的原版读条图**，进度靠**帧号**推进（门开得越大 = 越接近读完）。
#  实测：`data/global/ui/Loading/loadingscreen.dc6` dir=1 fpd=10，10 帧，全是 256×256
#        （`data/LOCAL/UI/loadingscreen.dc6` 与本文件逐帧同尺寸，是同一张图的不同打包）。
def group_loading(pal):
    """读条图 10 帧 → `UI/Menu/loadingscreen_{i}.png`（每帧一个文件，UI 侧按帧号取）。"""
    bar = dc6.read_pl2(res(*PL2_LOADING.split("/")))
    d = read_dc6("data", "global", "ui", "Loading", "loadingscreen.dc6")
    if not d:
        return
    for i, f in enumerate(d.frames):
        one(f, out("UI", "Menu", "loadingscreen_%d.png" % i), bar,
            "原版 data/global/ui/Loading/loadingscreen.dc6 帧 %d（调色板 loading/Pal.PL2）" % i,
            "loading")


# ═════════════════════════════════════════════════════════════════════════════
# 组 8：技能树大屏底图（片 1 新增）—— 逐帧 1:1 落位
# ═════════════════════════════════════════════════════════════════════════════
#  实测（`dc6.py info`，5 个职业各一份，16 帧完全同构）：
#    `SPELLS/skltree_{a,b,n,p,s}_back.DC6`  dir=1 fpd=16
#    帧尺寸循环 = 256×256 / 64×256 / 256×176 / 64×176（×4）⇒ 标准的 D2「整屏切 tile」打包
#    ⇒ 4 帧一排 = 320×432 的一"页"。**片 1 不拼页**（拼装语义见文件头：留给后续片），
#      只把 16 帧逐帧落位，帧号 = DC6 帧号（0..15）。
#  调色板 ACT1 的依据：同目录 `SPELLS/Skillicon.DC6` 用 ACT1 解出的**帧 2** 与 Diablerie 自己
#    从 MPQ 导出的 `Images/Skills/SkilliconAttack.png` **逐像素全等（差异 0/2304）** ⇒
#    `data/global/ui/SPELLS/**` 的调色板口径被参考物一侧钉死为 ACT1；Diablerie
#    `Engine/Spritesheet.cs:14` 的默认值也是 `PaletteType.Act1`。
SKILLTREE_CLASSES = ("a", "b", "n", "p", "s")


def group_skilltree(pal):
    for cls in SKILLTREE_CLASSES:
        d = read_dc6("data", "global", "ui", "SPELLS", "skltree_%s_back.DC6" % cls)
        if not d:
            continue
        for i, f in enumerate(d.frames):
            one(f, out("UI", "SkillTree", "skltree_%s_back_%d.png" % (cls, i)), pal,
                "原版 SPELLS/skltree_%s_back.DC6 帧 %d" % (cls, i), "skilltree")


# ═════════════════════════════════════════════════════════════════════════════
# 组 9：自动地图标记（片 1 新增）—— `ui/MINIMAP/**`（该目录只有这一个 DC6）
# ═════════════════════════════════════════════════════════════════════════════
#  实测：`MINIMAP/mapicons.DC6` dir=1 fpd=8，8 帧全 16×16。
#  调色板：**实测该图不影响像素** —— 8 帧只用到**索引 32** 一个值，而 15 张 PL2 在索引 32 上
#    取值全同（都是 #F4F4F4）⇒ 任何候选调色板解出的 PNG 逐字节相同（这 8 个图标是"白色模板"，
#    原版靠 shift 色表在运行时着色，同 libd2 `formats/src/font.zig` 对文字色的说明）。
#    ⇒ 用 ACT1（与既有 `misc` 组的同名落位一致）。`ui/AUTOMAP/**`（Act2Map 40 帧 / Act4Map 4 帧 /
#    MaxiMap 1260 帧）**本轮未解**：它们的调色板在现有证据下**定不下来**（那批图非 dithered，
#    「麻点判据」不适用；见回报的 BLOCKED）。
MINIMAP_FILES = [
    ("data/global/ui/MINIMAP/mapicons.DC6", "mapicon", "小地图标记 16×16 ×8"),
]


def group_automap(pal):
    for path, stem, note in MINIMAP_FILES:
        d = read_dc6(*path.split("/"))
        if not d:
            continue
        for i, f in enumerate(d.frames):
            one(f, out("UI", "MiniMap", "%s_%d.png" % (stem, i)), pal,
                "原版 %s 帧 %d（%s）" % (path, i, note), "automap")


# ═════════════════════════════════════════════════════════════════════════════
# 组 10：中文位图字体（片 1 新增）—— 整幅图集 + 帧→字符映射表
# ═════════════════════════════════════════════════════════════════════════════
#  实测：`data/LOCAL/FONT/chi/{Font16,font24,font30,font42}.DC6`
#        全是 dir=1 fpd=**13806**，每帧同尺寸：font16=13×13 / font24=19×19 / font30=24×24 / font42=37×37。
#  调色板 ACT1 的依据：Diablerie `Assets/Scripts/Editor/EditorTools.cs:129`
#    （菜单 `Assets/Create font from DC6`）显式用 `Palette.GetPalette(PaletteType.Act1)` ——
#    本项目现有的 `Fonts/font{16,24,30,42}.png` 就是那个工具的产物（与 Diablerie
#    `Assets/Resources/Fonts/font*.png` **逐字节相同**），⇒ 同一口径。
#  网格：列数由帧数算出（取满足「不超过 MAX_TEX 且尽量方」的因子），行主序摆放，
#    **一帧不丢也一帧不多**（格子数 == 帧数）。第 i 帧的格子 = (i % cols, i // cols)。
ATLAS_MAX_TEX = 8192     # font42 的图集 117×37 = 4329 宽、118×37 = 4366 高 ⇒ 必须 8192
CHI_FONTS = (
    # (DC6 文件名（照磁盘原样）, 输出 stem, 指标表文件名)
    ("Font16.DC6", "font16", "font16.tbl"),
    ("font24.DC6", "font24", "font24.tbl"),
    ("font30.DC6", "font30", "font30.tbl"),
    ("font42.DC6", "font42", "font42.tbl"),
)


def atlas_cols(n, cell, limit=ATLAS_MAX_TEX):
    """能整除 `n` 的列数里，取「两维都不超 `limit` 且最方」的那一个（没有 ⇒ None）。

    最方 = 列数 ≤ 行数 且 列数最大（即行列相差最小、列优先）；确定性、与输入无随机关系。
    """
    best = None
    for c in range(2, n + 1):
        if n % c:
            continue
        r = n // c
        if c > r:                      # 只看「列 ≤ 行」的一半，另一半点对称
            break
        if c * cell > limit or r * cell > limit:
            continue
        best = c                       # c 递增 ⇒ 循环结束时 best = 满足条件的最大 c（最方）
    return best


def build_atlas(frames, cols):
    """按行主序把 frames 摆成一张索引图（cell = 该字体最大帧尺寸，小帧左上对齐）。"""
    cw = max(f.width for f in frames)
    ch = max(f.height for f in frames)
    rows = (len(frames) + cols - 1) // cols
    canvas = bytearray(cw * cols * ch * rows)
    W = cw * cols
    for i, f in enumerate(frames):
        ox = (i % cols) * cw
        oy = (i // cols) * ch
        for y in range(f.height):
            src = y * f.width
            dst = (oy + y) * W + ox
            canvas[dst:dst + f.width] = f.indices[src:src + f.width]
    return dc6.Frame(W, ch * rows, 0, 0, 0, canvas), cw, ch


def _read_font_tbl(path):
    """读字体指标表 → [(code, width, height, frame), ...]。**格式有出处，不是猜的**：

    libd2 `packages/formats/src/font.zig` L59-117（该文件头一句就写明「Diablo II bitmap fonts:
    a `.tbl` of glyph metrics beside a `.dc6` of glyph images」—— 表与图同目录同名成对）：
      · L59 签名 `Woo!`；L62 头 12 字节；L63 每条 14 字节；
      · L100 条目数 = u16 LE @ 头偏移 8；
      · L110/L111/L112/L113 条目字段：code = u16 LE @e[0..2]、width = e[3]、height = e[4]、
        frame = u16 LE @e[8..10]；
      · L71 注释：「Which DC6 frame holds the picture. Usually the code itself, but not promised to be.」
        ⇒ 表是**按 code 排序**的，而 `frame` 字段才是「这字在哪一帧」的权威答案。
    """
    import struct
    b = open(path, "rb").read()
    if b[:4] != b"Woo!":
        print("  ERROR 字体表签名不是 Woo!：%s（%r）" % (path, b[:4]))
        return None
    count = struct.unpack_from("<H", b, 8)[0]
    if len(b) < 12 + count * 14:
        print("  ERROR 字体表长度不足：%s（声称 %d 条，实际 %d 字节）" % (path, count, len(b)))
        return None
    out = []
    for i in range(count):
        e = b[12 + i * 14: 12 + (i + 1) * 14]
        out.append((struct.unpack_from("<H", e, 0)[0], e[3], e[4],
                    struct.unpack_from("<H", e, 8)[0]))
    return out


def write_fontmap_tsv(path, entries, cols, cell_w, cell_h):
    d = os.path.dirname(path)
    if d:
        os.makedirs(d, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("# 原版中文位图字体 帧→字符 映射（**生成物，不要手改**）\n")
        f.write("# 源：<原版资源>/d2raw/data/local/font/chi/<font>.tbl（格式出处：libd2\n")
        f.write("#     packages/formats/src/font.zig L59-117；frame 字段 = 「该字在哪一帧」）\n")
        f.write("# 图集：<工程>/Assets/Resources/Clover/D2/Fonts/font{N}_chi.png\n")
        f.write("#      格子 = %d×%d，列数 = %d，行主序 ⇒ 第 i 帧的格子 = (i %% %d, i // %d)\n"
                 % (cell_w, cell_h, cols, cols, cols))
        f.write("# 生成命令：python tools/d2codec/export_d2ui.py <d2dc6> client --only chifont\n")
        f.write("frame\tcode\twidth\theight\tcol\trow\n")
        for i, (code, w, h, frame) in enumerate(entries):
            f.write("%d\t%d\t%d\t%d\t%d\t%d\n" % (frame, code, w, h, frame % cols, frame // cols))


def group_chifont(pal):
    for dc6_name, stem, tbl_name in CHI_FONTS:
        d = read_dc6("data", "LOCAL", "FONT", "chi", dc6_name)
        if not d:
            continue
        n = len(d.frames)
        cell = max(f.width for f in d.frames)
        cols = atlas_cols(n, cell)
        if not cols:
            # 非预期分支：找不到满足尺寸上限的规则网格 ⇒ 不硬上（宁可报错，也不产出错位的图集）
            print("  ERROR 字体 %s 帧数 %d / 格子 %dpx 找不到规则网格（上限 %d）"
                  % (dc6_name, n, cell, ATLAS_MAX_TEX))
            continue
        big, cw, ch = build_atlas(d.frames, cols)
        one(big, out("Fonts", stem + "_chi.png"), pal,
            "原版 data/LOCAL/FONT/chi/%s：%d 帧 %d×%d 打成长 %d×宽 %d 的图集（列 %d，行主序）"
            % (dc6_name, n, cw, ch, big.width, big.height, cols), "chifont")

        entries = _read_font_tbl(raw("data", "local", "font", "chi", tbl_name))
        if entries is None:
            print("  BLOCKED %s：字体表读不出 ⇒ 帧→字符映射无法给出（图集已照解）" % tbl_name)
            continue
        # 自检：条目数 == DC6 帧数，且 frame 字段正好覆盖 0..n-1（一帧不丢、不重）
        frames = sorted(e[3] for e in entries)
        if len(entries) != n or frames != list(range(n)):
            print("  ERROR %s 与 %s 不匹配：表 %d 条（frame %d..%d）vs DC6 %d 帧 ⇒ 不写映射表"
                  % (tbl_name, dc6_name, len(entries), frames[0], frames[-1], n))
            continue
        write_fontmap_tsv(fontmap(stem + "_chi.tsv"), entries, cols, cw, ch)
        print("  映射表 %s：%d 条（code %d..%d）" % (stem + "_chi.tsv", len(entries),
                                              entries[0][0], entries[-1][0]))


# ═════════════════════════════════════════════════════════════════════════════
# 组 11：MENU/ 下"项目曾登记为无出处"的那批（片 1 新增）—— 逐帧 1:1 落位
# ═════════════════════════════════════════════════════════════════════════════
#  实测帧数/尺寸（`dc6.py probe`）：
#    boxpieces.DC6        22 帧 14×15      —— 九宫格黑框的拼装块
#    helpborder.dc6        8 帧（tile 打包，320×432×2 页）
#    upgrade.DC6           1 帧 119×184
#    okcancelbtn.dc6       2 帧  96×32
#    endgameok.dc6         2 帧  96×32
#    buttontempok.DC6      2 帧  78×36
#    buttontempcancel.DC6  2 帧  93×36
#    a{n}q{m}.dc6        21 个文件 × 27 帧 72×86  —— 任务说明图（act1..4 的 6/6/6/3 个任务）
#    EndGame.dc6           8 帧（tile 打包，320×480×2 页）—— 调色板 = EndGame/Pal.PL2（见上）
#  调色板 ACT1 的依据（同族实测，参考物一侧）：`PANEL/invchar.DC6` 用 ACT1 解出并与 Diablerie
#    自己导出的 `Images/Panels/inventory.png` / `charstat.png` **逐字节相同（SHA256 全等）**
#    ⇒ `data/global/ui/**` 游戏内 UI 全族 = ACT1；Diablerie `Spritesheet.cs:14` 默认 Act1。
MENU_ONE_FRAME = [
    # (源相对路径, 输出目录, 输出 stem, 备注)
    ("data/global/ui/MENU/boxpieces.DC6", "UI/Menu", "boxpieces", "九宫格黑框拼装块 14×15 ×22"),
    ("data/global/ui/MENU/helpborder.dc6", "UI/Menu", "helpborder", "帮助框（tile 打包）"),
    ("data/global/ui/MENU/upgrade.DC6", "UI/Menu", "upgrade", "升级提示底图 119×184"),
    ("data/global/ui/MENU/okcancelbtn.dc6", "UI/Menu", "okcancelbtn", "确定/取消 96×32 ×2"),
    ("data/global/ui/MENU/endgameok.dc6", "UI/Menu", "endgameok", "结束屏 OK 96×32 ×2"),
    ("data/global/ui/MENU/buttontempok.DC6", "UI/Menu", "buttontempok", "按钮模板 OK 78×36 ×2"),
    ("data/global/ui/MENU/buttontempcancel.DC6", "UI/Menu", "buttontempcancel", "按钮模板 Cancel 93×36 ×2"),
]

MENU_ENDGAME = ("data/global/ui/MENU/EndGame.dc6", "UI/Menu", "endgame", "结束屏底图（tile 打包）")

MENU_QUESTS = tuple("a%dq%d" % (a, q)
                    for a, n in ((1, 6), (2, 6), (3, 6), (4, 3)) for q in range(1, n + 1))


def group_menu(pal):
    endgame_pal = dc6.read_pl2(res(*PL2_ENDGAME.split("/")))
    for path, dstdir, stem, note in MENU_ONE_FRAME:
        d = read_dc6(*path.split("/"))
        if not d:
            continue
        # ★ 片 5 定案（原 BLOCKED 收口）：`endgameok.dc6` 是**死亡屏（EndGame）那一屏自己的按钮**
        #   ⇒ 用 `EndGame/Pal.PL2`（片 1 用的 ACT1 是错的）。两条独立判据：
        #     ① 麻点度量（相邻不透明像素对的平均颜色跳变）ACT1 = **84.5** vs EndGame = **28.8**
        #        （2.9 倍差距、方向明确；与 `EndGame.dc6` 定案时同一口径同一方向）；
        #     ② 肉眼复核（⛔ 原一次性联络图 `.ai-tmp/test/sheet_deathui.png` 与探针 `.ai-tmp/test/p5_probe.py` **均已删、在盘无替代** ⇒ 不可复跑；口径见本文件 `:80-89` 与定案登记行 `策划/验收表.md:433`（BL-4））：
        #        @ACT1 = 满屏彩色噪点（错色）、@EndGame = 干净的深灰石板按钮。
        #   其余 `MENU/**` 文件仍是 **BLOCKED** —— 它们的麻点度量差距 < 2 倍（判据不成立）：
        #     `okcancelbtn` 30.3(ACT1) vs 30.9(EndGame) 基本持平；
        #     `buttontempok` 23.6→17.0 / `buttontempcancel` 23.4→16.9 / `boxpieces` 26.1→15.5 偏 EndGame
        #     但都达不到「大差距」门槛 ⇒ 保持 ACT1，等原版该屏实机截图或客户端的「文件→调色板」表。
        use_pal = endgame_pal if stem == "endgameok" else pal
        for i, f in enumerate(d.frames):
            one(f, out(*dstdir.split("/"), "%s_%d.png" % (stem, i)), use_pal,
                "原版 %s 帧 %d（%s；调色板 %s）"
                % (path, i, note, PL2_ENDGAME if stem == "endgameok" else PL2_ACT1), "menu")

    path, dstdir, stem, note = MENU_ENDGAME
    d = read_dc6(*path.split("/"))
    if not d:
        return
    for i, f in enumerate(d.frames):
        one(f, out(*dstdir.split("/"), "%s_%d.png" % (stem, i)), endgame_pal,
            "原版 %s 帧 %d（%s；调色板 %s）" % (path, i, note, PL2_ENDGAME), "menu")

    for stem in MENU_QUESTS:
        d = read_dc6("data", "global", "ui", "MENU", stem + ".dc6")
        if not d:
            continue
        for i, f in enumerate(d.frames):
            one(f, out("UI", "Quest", "%s_%d.png" % (stem, i)), pal,
                "原版 MENU/%s.dc6 帧 %d（任务说明图 72×86 ×27）" % (stem, i), "menu")


# ═════════════════════════════════════════════════════════════════════════════
# 组 12：前端**职业半身像**（片 4 新增）—— 创角/选角屏上那一排站着的人物
# ═════════════════════════════════════════════════════════════════════════════
#  源：`data/global/ui/FrontEnd/{cls}/{CLS}{NU1,NU2,NU3}.DC6`（**逐帧 1:1**，一帧一个 PNG）。
#  「哪三个序列是屏上那三态」的依据 = **参考物源码**（社区复刻工程 mofr/Diablerie）：
#    `Diablerie/Assets/Scripts/Diablerie/Game/UI/Menu/ClassSelect/ClassSelector.cs:167-175`
#      var classPath = $@"{BasePath}\{ClassName}\{Token}";     // BasePath = data\global\ui\FrontEnd
#      backIdleSprites       = Spritesheet.Load($"{classPath}NU1", PaletteType.Fechar);
#      backIdleHoverSprites  = Spritesheet.Load($"{classPath}NU2", PaletteType.Fechar);
#      frontIdleSprites      = Spritesheet.Load($"{classPath}NU3", PaletteType.Fechar);
#    `:258  ChangeState(ClassSelectorState.BackIdle);`  ⇒ **屏上默认态 = NU1**
#    `:116-122 ToggleHover()`                            ⇒ **悬停态 = NU2**
#    `:53-70 MainAnimatorOnFinish` + `:208-233` 的状态表  ⇒ 点选后转到 `FrontIdle` = **NU3**
#  ⛔ 本轮**只落 NU1/NU2/NU3 三态**：`{CLS}FW`（正面转身过渡，如 AMFW 54 帧）、`{CLS}BW`（背面转身过渡）、
#     以及 `{CLS}FWs`/`{CLS}BWs`/`{CLS}NU3s`（叠加层，需要 SoftAdditive 混合材质）**本轮不落位**，
#     因为它们属于"逐帧播放器 + 叠加材质 + 选人音效"那一整块（登记为范围边界，见回报）。
#  PL2 = **fechar/Pal.PL2**（**逐条核实过，不是照抄**）：
#    ① 参考物源码显式指定：`ClassSelector.cs:168-175` 全部传 `PaletteType.Fechar`；
#    ② 路径出处：`Engine/IO/D2Formats/Palette.cs:19` → `data\global\palette\fechar\Pal.PL2`
#       （`PaletteType.cs:12` `Fechar = 7`），本地实测该文件存在（`<d2raw>/data/global/palette/fechar/`）；
#    ③ 实测佐证（可复现）：同族 `SONU1.DC6` 分别按 fechar / menu1 解出同一帧 —— fechar 是
#       深绿斗篷 + 紫水晶法杖的正常配色，menu1 是"高亮到发白"的错色（判别口径见下条注释）。
#    ④ 不采用 `Menu1`：任务书提示"前端选人屏另有 Menu1 口径"，但**该口径在参考物源码里没有对应** ——
#       `ClassSelector.cs` 是本项目唯一能拿到的"前端选人屏实现"，它写的是 Fechar。按 §0.5"出处优先"取 Fechar。
# ★★ 片 22（**用户 2026-09-19 直接决策**）：**只做 Amazon + Barbarian 两个职业** ——
#    其余三个（Necromancer / Paladin / Sorceress）**素材不再落位**（工程侧目录也已删）。
#    第 4 列 = 是否落位（`True` 才解）。保留全部 5 行的名字与前缀是为了：
#    ① 工具自身仍能完整表达"原版有哪 5 个职业"；② 将来恢复某个职业只改这一个布尔。
#    ⛔ **注意**：`Def.PlayerClass` 的枚举值**一个都不许删**（存档 `cls` 字段与配表 `class` 列依赖它）
#       —— 这里是**素材/UI 层面的收窄**，不是"这个职业不存在"。
FRONTEND_CLASSES = (
    # (目录名, 文件名前缀（照磁盘原样，大小写可能混用）, 输出子目录, 是否落位)
    ("amazon", "AM", "amazon", True),
    ("barbarian", "BA", "barbarian", True),
    ("necromancer", "NE", "necromancer", False),
    ("paladin", "PA", "paladin", False),
    ("sorceress", "SO", "sorceress", False),
)

# 三态：屏上默认（背面待机）/ 悬停 / 选中后转正面待机。键 = 输出文件名里的状态码。
FRONTEND_STATES = (("NU1", "nu1"), ("NU2", "nu2"), ("NU3", "nu3"))

# ★ 片 22 新增：**转身过渡**两段（原版创角屏"人物走上前 / 转回背面"那一整块动画）。
#   出处 = 参考物源码（社区复刻工程）`Diablerie/.../Menu/ClassSelect/ClassSelector.cs:207-233`：
#     · `FrontTransition`：`Sprites = {CLS}FW`、`Loop = false`、`HideOnFinish = true`、
#       `SortingOrderShift = 10`、**`Fps = 25`**、`SfxPath = data\global\sfx\cursor\intro\{cls} select.wav`；
#     · `BackTransition` ：`Sprites = {CLS}BW`、`Loop = false`、`HideOnFinish = true`、**`Fps = 25`**、
#       `SfxPath = ...{cls} deselect.wav`。
#   状态机全貌（同文件 `:53-76 MainAnimatorOnFinish`）：
#     `BackIdle(NU1, loop) --点选--> FrontTransition(FW, 25fps, 播完隐藏) --> FrontIdle(NU3, loop)`
#     `FrontIdle --再点--> BackTransition(BW, 25fps) --> BackIdle`
#   ⚠️ 另有一套**叠加层** `{CLS}FWs` / `{CLS}BWs` / `{CLS}NU3s`（需 SoftAdditive 材质 + 排序 +10），
#      本轮**仍不落位**（登记为范围边界）—— ⛔ 不许拿它当"过渡"的替代品。
FRONTEND_TRANSITIONS = (("FW", "fw"), ("BW", "bw"))


def _find_ci(dir_path, name):
    """在目录里按**大小写不敏感**找文件名，返回真实文件名（找不到返回 None）。

    为什么需要：原版这批文件名大小写不一致（`AMNU1.DC6` / `banu1.DC6` / `NENU1.DC6` /
    `PANU1.DC6` / `SONU1.DC6` —— 同一个家族四种写法）。写死大小写在本机能过（Windows 不敏感），
    换台机器/Mac 就 MISS，而且是**静默跳过**。这里显式匹配并返回真实名，写进统计备注里便于复查。
    """
    if not os.path.isdir(dir_path):
        return None
    want = name.lower()
    for fn in sorted(os.listdir(dir_path)):
        if fn.lower() == want:
            return fn
    return None


def group_frontend(pal):
    """职业半身像三态（NU1/NU2/NU3）逐帧落位 → `D2/UI/FrontEnd/{cls}/{prefix}{state}_{i}.png`。

    `pal` 参数不用（本组的调色板是 **fechar**，不是 ACT1），保留签名是为了与其它组一致。
    """
    bar = dc6.read_pl2(res(*PL2_FECHAR.split("/")))
    total = 0
    kept = 0
    for cls_dir, prefix, out_dir, keep in FRONTEND_CLASSES:
        src_dir = src("data", "global", "ui", "FrontEnd", cls_dir)
        if not os.path.isdir(src_dir):
            print("  MISSING  %s" % src_dir)
            continue
        if not keep:
            # ★ 片 22：用户决策只做 2 个职业 ⇒ 其余三个素材**不落位**（工程侧目录也已删）
            print("  SKIP     %-12s（用户决策：只做 Amazon + Barbarian）" % cls_dir)
            continue
        kept += 1
        for state, stem in FRONTEND_STATES + FRONTEND_TRANSITIONS:
            real = _find_ci(src_dir, prefix + state + ".DC6")
            if real is None:
                print("  MISSING  %s/%s%s.DC6" % (cls_dir, prefix, state))
                continue
            d = dc6.parse(open(os.path.join(src_dir, real), "rb").read())
            for i, f in enumerate(d.frames):
                one(f, out("UI", "FrontEnd", out_dir, "%s_%d.png" % (stem, i)), bar,
                    "原版 FrontEnd/%s/%s 帧 %d（%dx%d，调色板 %s）%s" % (
                        cls_dir, real, i, f.width, f.height, PL2_FECHAR,
                        "" if real == prefix + state + ".DC6" else "【磁盘实际大小写：%s】" % real),
                    "frontend")
                total += 1
            print("  %-12s %-12s %4d 帧  %dx%d" % (cls_dir, real, len(d.frames),
                                                   d.frames[0].width, d.frames[0].height))
    print("  职业半身像/过渡 %d 张（%d 个职业 ×（3 态 + 2 过渡））" % (total, kept))


# ═════════════════════════════════════════════════════════════════════════════
# 组 13：启动屏 / 标题 logo（片 4 新增）
# ═════════════════════════════════════════════════════════════════════════════
#  源：`data/global/ui/Logo/logo.DC6`（实测 dir=1 fpd=1，单帧 **319×177**）。
#  内容（读图确认，不是看文件名猜的）：`DIABLO II` 火焰字标（红金+黑边）—— 原版前端／启动画面上的主 logo。
#  为什么用 fechar：与组 12 同源同族（都是 `ui/FrontEnd` 的前端画面家族）。判别口径 =
#    `dc6.py` 的调色板判定（同上 docstring 的 ③）。
#  ⚠️ 本项目**没有** `FrontEnd/blizno.DC6`（Blizzard North 开机 logo，640×480 整屏 tile 打包）
#     的接入需求登记 —— 启动屏用的是这张 D2 logo（见回报「未决」）。
def group_logo(pal):
    bar = dc6.read_pl2(res(*PL2_FECHAR.split("/")))
    d = read_dc6("data", "global", "ui", "Logo", "logo.DC6")
    if not d:
        return
    for i, f in enumerate(d.frames):
        one(f, out("UI", "Logo", "logo_%d.png" % i), bar,
            "原版 Logo/logo.DC6 帧 %d（DIABLO II 火焰字标 %dx%d，调色板 %s）" % (
                i, f.width, f.height, PL2_FECHAR), "logo")


GROUPS = {
    "buttons": group_buttons,
    "panels": group_panels,
    "skillicons": group_skillicons,
    "items": group_items,
    "banners": group_banners,
    "misc": group_misc,
    "loading": group_loading,
    "skilltree": group_skilltree,
    "automap": group_automap,
    "chifont": group_chifont,
    "menu": group_menu,
    "frontend": group_frontend,
    "logo": group_logo,
}


def main():
    global SRC_ROOT, DST_ROOT, PL2_ROOT, RAW_ROOT
    if len(sys.argv) < 3:
        print(__doc__)
        return 1

    SRC_ROOT = os.path.abspath(sys.argv[1])
    DST_ROOT = os.path.abspath(sys.argv[2])
    RAW_ROOT = os.path.join(os.path.dirname(SRC_ROOT), "d2raw")
    PL2_ROOT = os.path.join(RAW_ROOT, "data", "global", "palette")

    only = None
    if "--only" in sys.argv:
        only = sys.argv[sys.argv.index("--only") + 1].split(",")

    pal = dc6.read_pl2(res(*PL2_ACT1.split("/")))
    print("调色板：%s（%d 项，索引 0 = 透明）" % (PL2_ACT1, len(pal)))
    print("DC6 源：%s\n工程：%s" % (SRC_ROOT, DST_ROOT))

    for name, fn in GROUPS.items():
        if only and name not in only:
            continue
        print("── 组 %s ──" % name)
        fn(pal)

    # 统计
    by_group = {}
    for g, _p, w, h, _n in STATS:
        by_group.setdefault(g, []).append((w, h))
    print("\n=== 写出统计 ===")
    for g in sorted(by_group):
        sz = {}
        for w, h in by_group[g]:
            sz["%dx%d" % (w, h)] = sz.get("%dx%d" % (w, h), 0) + 1
        top = sorted(sz.items(), key=lambda kv: -kv[1])[:4]
        print("%-11s %4d 个   尺寸: %s" % (g, len(by_group[g]),
                                          ", ".join("%s×%d" % (k, v) for k, v in top)))
    print("合计 %d 个 PNG" % len(STATS))
    return 0


if __name__ == "__main__":
    sys.exit(main())
