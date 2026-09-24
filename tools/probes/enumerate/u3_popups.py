# -*- coding: utf-8 -*-
# ─────────────────────────────────────────────────────────────────────────────
# Diablo2 · tools/probes/enumerate/u3_popups.py
#
# 「弹框/面板全集」**脚本枚举器**（片 `popupaudit`，用户第三批投诉：
#   「你检查所有弹框，认真一点好吗？？？？？」／「甚至连他妈的关闭都没有。。。」）
#
# ⛔ 硬要求：面板名**一条都不许手写** —— 全部来自下面 4 路扫盘结果：
#   ① prefabs   client/Assets/Resources/UI/*.prefab          （磁盘上的面板壳）
#   ② code      client/Assets/Scripts/UI/*.cs 里 `class X : UIPanel` 的子类
#   ③ builder   client/Assets/Editor/ProjectBuilder.cs 的 `PanelNames` 数组（buildcheck 的 18 口径）
#   ④ engine    clover-client-unity-engine/Runtime/Presentation/{UI.cs,UIWidgets.cs}
#               的通用件（ConfirmLayer / ToastLayer / LoadingLayer / FloatTextLayer / GuideLayer）
#               与 5 个层（Background/Normal/Popup/Top/System）
#
# 产出：
#   stdout  → 四路全集 + **三路差集**（prefab 有代码无 / 代码有不在构建器清单 / 构建器有磁盘无 …）
#   ① `<项目根>/.ai-tmp/test/u3_popup_roster.tsv`  —— 脚本枚举的面板名 + 每路的在否 + 层 + 出处
#   ② 逐面板证据（关闭出口代码行 / 文本写者行）直接打到 stdout，供人工判列
#
# 复现：cd <项目根>; python tools/probes/enumerate/u3_popups.py
# ─────────────────────────────────────────────────────────────────────────────
import io
import os
import re
import sys
import glob


def _safe_stdio():
    for s in (sys.stdout, sys.stderr):
        try:
            s.reconfigure(encoding='utf-8', errors='replace')
        except Exception:
            pass


ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..'))
UI_DIR = os.path.join(ROOT, 'client/Assets/Scripts/UI')
PREFAB_DIR = os.path.join(ROOT, 'client/Assets/Resources/UI')
BUILDER = os.path.join(ROOT, 'client/Assets/Editor/ProjectBuilder.cs')
ENGINE = os.path.abspath(os.path.join(ROOT, '..', 'clover-client-unity-engine/Runtime/Presentation'))
OUT_TSV = os.path.join(ROOT, '.ai-tmp/test/u3_popup_roster.tsv')


def rd(p):
    with io.open(p, encoding='utf-8-sig', errors='replace') as f:
        return f.read()


def rl(p):
    return rd(p).splitlines()


def line_of(text, pos):
    return text.count('\n', 0, pos) + 1


# ── ① prefabs ────────────────────────────────────────────────────────────────
def src_prefabs():
    out = {}
    for p in sorted(glob.glob(PREFAB_DIR + '/*.prefab')):
        n = os.path.splitext(os.path.basename(p))[0]
        s = rd(p)
        names = re.findall(r'm_Name: (.+)', s)
        out[n] = dict(
            path='client/Assets/Resources/UI/' + os.path.basename(p),
            origin='client/Assets/Resources/UI/%s (m_Name=%s, %d 节点)'
                   % (os.path.basename(p), ','.join(x.strip() for x in names), len(names)),
            nodes=[x.strip() for x in names])
    return out


# ── ② UIPanel 子类 ───────────────────────────────────────────────────────────
def src_code():
    out = {}
    for p in sorted(glob.glob(UI_DIR + '/*.cs')):
        s = rd(p)
        rel = 'client/Assets/Scripts/UI/' + os.path.basename(p)
        for m in re.finditer(r'public\s+class\s+(\w+)\s*:\s*UIPanel', s):
            cls = m.group(1)
            ln = line_of(s, m.start())
            # 层：本类 override Layer / 基类默认 Normal
            ml = re.search(r'public\s+override\s+UILayer\s+Layer\s*=>\s*UILayer\.(\w+)', s)
            layer = ('UILayerN:Normal（未 override ⇒ 基类默认，见 Runtime/Presentation/UI.cs:155 判 Popup）'
                     if not ml else 'UILayerN:' + ml.group(1))
            if ml:
                ly = line_of(s, ml.start())
                ly_src = '%s:%d' % (rel, ly)
            else:
                ly_src = rel + '（无 override；基类 `UIPanel.Layer` 默认 Normal）'
            out[cls] = dict(path=rel, origin='%s:%d (class %s : UIPanel)' % (rel, ln, cls),
                            layer=layer, layer_src=ly_src, code=s, code_path=rel)
    return out


# ── ③ 构建器清单 ─────────────────────────────────────────────────────────────
def src_builder():
    s = rd(BUILDER)
    m = re.search(r'PanelNames\s*=\s*\{(.*?)\};', s, re.S)
    ln = line_of(s, m.start())
    body = m.group(1)
    # 逐行给每个名字的行号
    out = {}
    for line_i, line in enumerate(body.splitlines()):
        code_only = line.split('//')[0]          # ⛔ 先剥注释（注释里也有引号字符串）
        for nm in re.findall(r'"([^"]+)"', code_only):
            out[nm] = 'client/Assets/Editor/ProjectBuilder.cs:%d' % (ln + line_i)
    return out, ln


# ── ③b UI/ 下**全部** public 类（含非 UIPanel：Tooltip / Icon / 光标等） ──────
def src_all_classes():
    out = {}
    for p in sorted(glob.glob(UI_DIR + '/*.cs')):
        s = rd(p)
        rel = 'client/Assets/Scripts/UI/' + os.path.basename(p)
        for m in re.finditer(r'public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*'
                             r'(class|struct|enum|interface)\s+(\w+)', s):
            out.setdefault(m.group(2), '%s:%d (%s)' % (rel, line_of(s, m.start()), m.group(1)))
    return out


# ── ④ 引擎通用件 + 层 ────────────────────────────────────────────────────────
ENGINE_WIDGETS = ['ConfirmLayer', 'ToastLayer', 'LoadingLayer', 'FloatTextLayer', 'GuideLayer',
                  'RedDotRegistry', 'SafeAreaFitter']
ENGINE_LAYERS = ['Background', 'Normal', 'Popup', 'Top', 'System']


def src_engine():
    out = {}
    for fn in ('UI.cs', 'UIWidgets.cs'):
        p = os.path.join(ENGINE, fn) if ENGINE.startswith(ROOT) else \
            os.path.join(os.path.dirname(ROOT), 'clover-client-unity-engine/Runtime/Presentation', fn)
        if not os.path.exists(p):
            continue
        s = rd(p)
        for w in ENGINE_WIDGETS:
            m = re.search(r'(?:class|sealed class)\s+' + w + r'\b', s)
            if m:
                out[w] = 'Runtime/Presentation/%s:%d' % (fn, line_of(s, m.start()))
    return out


# ── 每面板证据：关闭出口 / 文本写者 ──────────────────────────────────────────
CLOSE_PAT = re.compile(r'关闭|Close|ClosePanel|面板开关|PanelToggleRequest|Escape|KeyClosePanel')
TEXT_PAT = re.compile(r'\.text\s*=|\.SetText\(|\.SetLabel\(|UiArt\.Label\(|FlowLabel\.Create\(|'
                      r'D2Text\.\w*Label\(|UiText\.|SetDisabledTint|\.SetValue\(')


def panel_close_lines(code, code_path):
    out = []
    for i, l in enumerate(code.splitlines(), 1):
        if CLOSE_PAT.search(l) and not l.strip().startswith('//'):
            out.append('%s:%d| %s' % (code_path, i, l.strip()[:150]))
    return out


def panel_text_sites(code, code_path):
    out = []
    for i, l in enumerate(code.splitlines(), 1):
        if TEXT_PAT.search(l) and not l.strip().startswith('//'):
            out.append('%s:%d| %s' % (code_path, i, l.strip()[:160]))
    return out


# ═══════════════════════════════════════════════════════════════════════════════
# 逐面板**判列数据**（片 `popupaudit` 的只读审计结论）。
#
# 口径：面板名单仍由上面 4 路扫盘得出（**脚本枚举**）；本表只填**判定列**，
#       每格都带出处（`文件:行` / 资源路径 / mpq 试探产物）。
# 键 = 面板名（必须与扫盘结果同名，否则 `write_audit` 会报错 —— 防"手写一个不存在的面板"）。
# 列：
#   orig_src   原版出处（原版 prefab 文件:行；不在盘 ⇒ 写「查不到」并把"记录值出处"另注）
#   orig_close 原版有没有关闭控件
#   our_close  我们有没有关闭控件
#   art        关闭图形在盘否
#   click      点击真能关否（离线能判的写"接线 …"；实机项写"待 Play"）
#   other      其它出口（Esc / 右键 / 再点 / 热键）
#   num        数值列来源（模块:行）
#   disp       显示问题
#   verdict    OK / 缺陷(a)关闭 / 缺陷(b)数值 / 缺陷(c)显示 / BLOCKED
#   src        本条结论的出处
# ═══════════════════════════════════════════════════════════════════════════════
JUDGE = {
 'BootPanel': dict(
   orig_src='查不到（原版无 "BootPanel" prefab；原版启动是 Logo+进度条，非本工程这个壳）',
   orig_close='无（启动屏不该有关闭控件）', our_close='无（任意键/鼠标点击即继续）',
   art='n/a', click='不适用（不需要点击关闭）',
   other='任意键或鼠标点击（BootPanel.cs:102 AnyKeyDown + :110-117 遍历 GameKey 全集）',
   num='无数字（BootPanel.cs:147/154/168/174/179 五行文案，唯一变量 = GameConst.SaveVersion）',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/BootPanel.cs:93-117'),
 'MainMenuPanel': dict(
   orig_src='原版 `Prefabs/Menu/MainMenu.prefab`（不在盘）⇒ 记录值出处 tools/probes/measure/w3_uiflow_audit.py:481 `ExitButton`(m_Text=EXIT)',
   orig_close='有（原版 `ExitButton` 槽，文案 EXIT）',
   our_close='有（`SINGLE PLAYER` / `EXIT` 两槽，文案逐字见 策划/自审对比/启动链路对照.md:22）',
   art='在盘（走原版按钮帧；`btn_wide_normal.png` 已在盘 —— 见 .ai-tmp/test/finalclose_uicheck_after.txt Ⓐ-5b）',
   click='待 Play（离线只见按钮构建与本帧无 onClick 反证）',
   other='无（流程屏，靠 EXIT 出）',
   num='无数字',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/MainMenuPanel.cs:73; 策划/自审对比/启动链路对照.md:22'),
 'SettingsPanel': dict(
   orig_src='原版无独立选项屏 prefab（原版选项在 ESC 菜单下）⇒ 底图 = 原版 MENU/boxpieces.DC6 拼装窗框（w3_uiflow_audit.py:1591）',
   orig_close='原版选项屏本身无（原版 ESC 菜单无独立 prefab）',
   our_close='有（`CLOSE` 中等按钮，SettingsPanel.cs:323-327 → Game.UI.Close<SettingsPanel>()）',
   art='在盘（原版中等按钮帧 `btn_med_normal.png`；见 .ai-tmp/test/finalclose_uicheck_after.txt Ⓐ-5a/5f）',
   click='接线已判（SettingsPanel.cs:323 onClick ⇒ :327 Close）；实机待 Play',
   other='Esc（SettingsPanel.cs:110-117 走 `GameKeyAlias.KeyClosePanel`；AppFlow.cs:1001/1016 让本面板优先吃 Esc）',
   num='BGM/SFX 音量（SettingsPanel.cs:178-179 `_bgm/_sfx.ToString("0.00")`）、全屏(:182)、画质(:183)',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/SettingsPanel.cs:43,110-117,178-183,323-327'),
 'CharSelectPanel': dict(
   orig_src='原版 `Prefabs/Menu/ClassSelectMenu.prefab`（不在盘）⇒ 记录值出处 w3_uiflow_audit.py:527-528 `ExitButton`/`OkButton` 128×35',
   orig_close='有（原版 `ExitButton`(EXIT) + `OkButton`(OK) 两槽）',
   our_close='有（Exit 槽 = MAIN MENU → ToMainMenuRequest；Ok 槽 = NEW HERO；见 策划/自审对比/bug清单.md B41）',
   art='在盘（原版中等按钮帧）', click='待 Play',
   other='无（流程屏）',
   num='行内等级 CharSelectPanel.cs:192 `$"LV {e.level}"`（DTO 字段 e.level）',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/CharSelectPanel.cs:87,192; 策划/自审对比/bug清单.md:63'),
 'CharCreatePanel': dict(
   orig_src='原版 `Prefabs/Menu/ClassSelectMenu.prefab` 同族（不在盘）⇒ w3_uiflow_audit.py:569 `ExitButton` 槽',
   orig_close='有（原版 Exit/Ok 两槽）', our_close='有（BACK / NEW HERO 两槽）',
   art='在盘（原版中等按钮帧）', click='待 Play', other='无（流程屏）',
   num='名字输入框文本（CharCreatePanel.cs:765/781）；职业说明(:1015)',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/CharCreatePanel.cs:313,765,1015'),
 'LoadingPanel': dict(
   orig_src='原版 `data/global/ui/Loading/loadingscreen.dc6`（已在盘：原版资源/d2dc6/data/global/ui/Loading/loadingscreen.dc6）',
   orig_close='无（加载屏不该有关闭控件）', our_close='无',
   art='n/a', click='不适用', other='无（进图看门狗自动过）',
   num='无数字（LoadingPanel.cs 文本写者 0 处）',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/LoadingPanel.cs:60'),
 'PausePanel': dict(
   orig_src='原版 ESC 菜单**无独立 prefab**（Diablerie `Prefabs/` 下只有 Menu/ 四个）⇒ 复用 MainMenu.prefab 的槽（PausePanel.cs:7；w3_uiflow_audit.py:632）',
   orig_close='原版 ESC 菜单无独立 prefab ⇒ 关闭 = 再按 ESC；无 X 控件',
   our_close='有（Resume / Options / SaveExit / ToMain 四槽，PausePanel.cs:81-95）',
   art='在盘（原版宽按钮帧）', click='待 Play',
   other='Esc 再按一次 = 继续（PausePanel.cs:17；AppFlow.cs:1013-1017 OnPauseTick）',
   num='无数字', disp='无', verdict='OK',
   src='client/Assets/Scripts/UI/PausePanel.cs:7,17,81-95; Runtime/... 见 Module/Flow/AppFlow.cs:1013-1017'),
 'D2ConfirmPanel': dict(
   orig_src='原版无此二次确认屏（它是引擎默认 uGUI 弹窗的原版风替身，见 D2ConfirmPanel.cs:1-60 文件头）',
   orig_close='无（原版无此屏）',
   our_close='有（确认 + 取消两颗，D2ConfirmPanel.cs:214-217 设文案；:185 `Game.UI.Close<D2ConfirmPanel>()`）',
   art='在盘（原版中等按钮帧）', click='待 Play',
   other='取消 = 走 onCancel（同一关闭路径）',
   num='无数字（title/message/confirm/cancel 四段运行时文本）', disp='无', verdict='OK',
   src='client/Assets/Scripts/UI/D2ConfirmPanel.cs:112-114,159,185,214-217'),
 'HudPanel': dict(
   orig_src='原版 `ControlPanel.prefab` + `Panel/ControlPanel.png`（w3_uigame_audit.py 首行；素材已在盘）',
   orig_close='无（HUD 是常驻层，不该有关闭控件）', our_close='无',
   art='n/a', click='不适用', other='无',
   num='生命/法力 HudPanel.cs:881-882；腰带数量 :912；均由 `PlayerStatsDto` 驱动',
   disp='无', verdict='OK', src='client/Assets/Scripts/UI/HudPanel.cs:50,267,855-912'),
 'MiniMapPanel': dict(
   orig_src='查不到（原版 prefab 不在盘；w3_uigame_audit.py 有 automap 横幅素材审计，无关闭控件行）',
   orig_close='查不到（原版自动地图靠 Tab 开合，无 X 控件）',
   our_close='无 —— ⛔ 面板上没有任何关闭控件（MiniMapPanel.cs 全文无 Button/onClick 之外无 close 节点）',
   art='n/a（无控件）',
   click='不适用（无控件可点）',
   other='Tab 热键（HudPanel.cs:331 `PollHotkey(KeyMinimap, nameof(MiniMapPanel))` ⇒ :1256-1264 Toggle）；'
         'HUD 小面板按钮（HudPanel.cs:1237-1245）—— 但本面板 Layer=Normal，**无 Popup 遮罩**，HUD 仍可点',
   num='无数字（文本写者 0 处；地形为逐格点阵贴图）',
   disp='无', verdict='缺陷(a)：无可见关闭入口（纯鼠标用户只能靠 HUD 按钮/Tab）',
   src='client/Assets/Scripts/UI/MiniMapPanel.cs:129-135; client/Assets/Scripts/UI/HudPanel.cs:331,1237-1264'),
 'InventoryPanel': dict(
   orig_src='原版 `InventoryPanel.prefab`（**不在盘**）⇒ 记录值出处 tools/probes/measure/w3_uigame_audit.py:652-656 `CloseButton` 32×31 @(-125.8,-184.1)',
   orig_close='有（原版 CloseButton 32×31，记录值出处 w3_uigame_audit.py:652）',
   our_close='**有命中区但不可见**：`UiArt.Panel(..., CloseButtonSize, ..., new Color(1,1,1,0), true)` = alpha 0（InventoryPanel.cs:538-539）',
   art='⛔ 不在盘：见 `client/资源欠缺清单.md:68`(#22) + 本片 mpq 按名试探（.ai-tmp/test/u3close-missing.txt 10 条 close 类名全 MISS；'
       '4 条对照项命中 ⇒ 链路可信）；`Resources/**` 亦无 *close*/*exit*/*cancel* 命名 PNG',
   click='接线已判（InventoryPanel.cs:540-546 Button.onClick ⇒ PanelToggleRequest）；实机待 Play',
   other='I 热键（HudPanel.cs:327）；**Esc 不关本面板**（AppFlow.cs:998-1003 只豁免 SettingsPanel ⇒ Esc 会开暂停菜单）',
   num='金币 InventoryPanel.cs:633 `data.gold`；每格堆叠数 :627',
   disp='关闭控件不可见（见 art 列）', verdict='缺陷(a)：关闭入口存在但**不可见**',
   src='client/Assets/Scripts/UI/InventoryPanel.cs:114-115,410-416,528-546,627,633'),
 'CharacterPanel': dict(
   orig_src='原版 `CharstatPanel.prefab` YAML 一手副本 = `.ai-tmp/test/cs_orig_CharstatPanel.prefab.txt`（由片 `charstat` 落盘，非本片产出）；'
            '其中 `m_Name: CloseButton` 的 RectTransform（&224502798200950928）实测 `m_SizeDelta: 32x31` / `m_AnchoredPosition: (-15.4,-188.3)`；'
            '另 记录值出处 tools/probes/measure/w3_uigame_audit.py:702-703',
   orig_close='**有**（原版 CloseButton 32×31 @(-15.4,-188.3)；出处 = 上面那份 prefab YAML 的 RectTransform 块）',
   our_close='**有命中区但不可见**：`new Color(1,1,1,0)`（CharacterPanel.cs:252-253）',
   art='⛔ 不在盘 —— **原版关闭图是 Unity sprite 资产**（prefab 里 `m_Sprite: {fileID: 21300000, guid: bd016b557dbd0934bbc9b79319d50729, type: 3}`），'
       '不是 mpq 里的 DC6（这解释了 mpq 按名 10 条全 MISS）；该 guid 在本工程 `.meta` 里 **0 命中**（图没随原版资产一起拿到）',
   click='接线已判（CharacterPanel.cs:254-260 ⇒ PanelToggleRequest）；实机待 Play',
   other='C 热键（HudPanel.cs:328）；**Esc 不关**（同上）',
   num='等级/经验/技能点 :284-286（一行拼三段）；四维 :288-291 SetStat ⇒ :322；'
       '防御/耐力/生命/法力 :297-300 ⇒ :324-328；命中/格挡 :302-303；四系抗性 :305-308；'
       '写者上游 = `Module/Player/PlayerModule.cs:304 Snapshot()` → `Module/Contracts.cs:557 PlayerStatsDto`；'
       'AR/BlockChance 算法 = `Module/Player/PlayerStats.cs:220-230`',
   disp='① 关闭控件不可见；② 右上框（原版空框 150×26）**一行塞「等级+经验 x/y+技能点」三段**（:284-286），'
        '长度无上限 ⇒ 高等级经验值下**越框/截断**风险（离线不可判死 ⇒ 待 Play 读 LineCount/宽度）；'
        '③ 四系抗性值列宽仅 45.5 画布px，`100%` 会折行（:236-238 作者已自注）',
   verdict='缺陷(a)关闭不可见 + 缺陷(c)显示（待 Play 判越框）+ 缺陷(b)数值待 `charstat` 片对账',
   src='client/Assets/Scripts/UI/CharacterPanel.cs:250-260,281-308,322-331; 策划/自审对比/bug清单.md:125(U52)'),
 'SkillTreePanel': dict(
   orig_src='查不到（原版 prefab 不在盘；w3_uigame_audit.py:706-714 只审计底图页与技能格，**无关闭控件行**）',
   orig_close='查不到（原版技能屏靠热键开合）',
   our_close='⛔ **无** —— 全文只有页签命中区（SkillTreePanel.cs:189-194 `TabHit`）与技能格点击（:432-465），**没有任何关闭控件**',
   art='n/a（无控件）',
   click='不适用（无控件可点）',
   other='T 热键（HudPanel.cs:329）；⛔ 本面板 Layer=**Popup**（:107）⇒ 引擎在 Popup 层插全屏遮罩（clover-client-unity-engine/Runtime/Presentation/UI.cs:443-461 '
         '`ShowMask`，`raycastTarget=true`）⇒ **HUD 小面板按钮点不到** ⇒ 纯鼠标用户**无任何出口**',
   num='剩余技能点 SkillTreePanel.cs:241/255 `_tree.skillPoints`；技能名/等级/说明 :482-484',
   disp='无（数值面）', verdict='缺陷(a) 最严重：Popup 面板无任何关闭入口 + 遮罩掐掉鼠标出口',
   src='client/Assets/Scripts/UI/SkillTreePanel.cs:107,123-126,189-194; clover-client-unity-engine/Runtime/Presentation/UI.cs:155-159,431-461'),
 'QuestLogPanel': dict(
   orig_src='查不到（原版 prefab 不在盘；w3_uigame_audit.py 无任务日志关闭控件行）',
   orig_close='查不到（原版任务日志靠 Q 键开合）',
   our_close='⛔ **无** —— 全文只有 Act 页签按钮（QuestLogPanel.cs:317-323），无关闭控件',
   art='n/a（无控件）', click='不适用（无控件可点）',
   other='Q 热键（HudPanel.cs:330）；⛔ Layer=**Popup**（:245）⇒ 遮罩掐掉 HUD 鼠标出口 ⇒ 纯鼠标用户无出口',
   num='任务正文 QuestLogPanel.cs:475 `TextOf(quest)`（进度数字来自 DenOfEvilQuest.ToDto，见 Module/Quest/DenOfEvilQuest.cs:183）',
   disp='无（数值面）', verdict='缺陷(a) 最严重：Popup 面板无任何关闭入口 + 遮罩掐掉鼠标出口',
   src='client/Assets/Scripts/UI/QuestLogPanel.cs:53-55,245,317-323,475'),
 'NpcDialogPanel': dict(
   orig_src='原版 `NPCDialog` 相关 prefab 不在盘 ⇒ **查不到**',
   orig_close='有（原版对话框有「離開」选项按钮）',
   our_close='有 —— 但**依赖数据侧**：`options[0]` 必须是关闭项（NpcDialogPanel.cs:448 「请 Npc 模块保证 options[0] = 关闭项」）；'
             '模块没给出口时面板**只剩 Esc/再点 NPC**（:447 Warn）',
   art='在盘（选项按钮走原版中等按钮帧；实机可读性见 .ai-tmp/test/report-finalclose.md §②）',
   click='待 Play',
   other='Esc / 再点 NPC（NpcDialogPanel.cs:447 自注）；本面板 Layer=**Normal**（:301），**不经 Popup 遮罩**',
   num='说话人/正文 :440-441（dialog.npcName/text）；选项文案 :469',
   disp='⚠️ 文件头注释写「层：Popup」（:103）而代码是 `UILayer.Normal`（:301）⇒ **注释漂移**（:296-300 已解释为何改 Normal ⇒ 低危）',
   verdict='缺陷(a)潜在：出口完全依赖数据侧 options[0]（缺该项时无可见关闭）',
   src='client/Assets/Scripts/UI/NpcDialogPanel.cs:103,296-301,434-441,448-469'),
 'ShopPanel': dict(
   orig_src='原版 `Panel/trade.DC6` / `buysell.DC6`（素材已在盘：原版资源/d2dc6/data/global/ui/Panel/）⇒ **原版关闭控件查不到**（prefab 不在盘）',
   orig_close='查不到',
   our_close='有（「关闭」按钮，ShopPanel.cs:254 `SquareButton(... "Close", "关闭", ..., OnCloseShop)`）',
   art='⚠️ 按钮**底板是纯色块**：`UiArt.SquareButton` → `UIFactory.CreateButton(..., UiArt.ButtonBg, ...)`，'
       '而 `UiArt.ButtonBg = Color(0.15,0.13,0.11,0.94)`（UiArt.cs:81，注释自称「仅贴图缺失时的纯色占位」）'
       '⇒ 文字「关闭」可见，但底图**不是原版按钮帧**',
   click='接线已判（ShopPanel.cs:730-734 OnCloseShop ⇒ ShopClose 事件 + Game.UI.Close<ShopPanel>()）；实机待 Play',
   other='Esc？⛔ 无（ShopPanel 未处理 Esc）；Layer=Popup ⇒ 遮罩掐掉 HUD 鼠标出口 ⇒ 出口只有这颗纯色块按钮',
   num='金币 ShopPanel.cs:325 `$"金币: {shop.playerGold}"`；每格数量 :351/581；标题 :369；页签 :370',
   disp='关闭按钮底板为纯色占位（1:1 硬标准点名的「纯色块/自画近似」类）',
   verdict='缺陷(c)显示：关闭按钮底板是纯色占位；且 Esc 无出口',
   src='client/Assets/Scripts/UI/ShopPanel.cs:41,123-126,240-254,320-370,730-734; client/Assets/Scripts/UI/UiArt.cs:79-81,606-617'),
 'DeathPanel': dict(
   orig_src='原版 `MENU/endgameok.dc6`（96×32 ×2 帧，已在盘：原版资源/d2dc6/data/global/ui/MENU/）',
   orig_close='有（原版 endgameok 按钮 = 「繼續」）',
   our_close='有（「繼續」按钮 `UiArt.OrigButton(... ResPaths.MenuEndGameOK ...)`，DeathPanel.cs:173-176）',
   art='在盘（endgameok.dc6 两帧）', click='接线已判（:175 OnRevive → 等 Revived 事件关闭）；实机待 Play',
   other='收到 `Events.Revived` 自动关（DeathPanel.cs:261-262）；离开 Stage 兜底关（:300-301）',
   num='倒计时提示 :108/219/254（WaitingText）；无属性数字',
   disp='无', verdict='OK（有可见出口 + 两条自动出口）',
   src='client/Assets/Scripts/UI/DeathPanel.cs:74,168-176,217-219,261-262,300-301'),
 'WaypointPanel': dict(
   orig_src='原版传送点屏 prefab **不在盘** ⇒ **查不到**',
   orig_close='查不到',
   our_close='有（「关闭」`FlowButton`，WaypointPanel.cs:278-279 → :345-348 OnCloseClicked ⇒ Game.UI.Close<WaypointPanel>()）',
   art='在盘（FlowButton 走「原版帧 + SpriteSwap」，UiLayoutFlow.cs FlowButton.Create）',
   click='接线已判（:345-348）；实机待 Play',
   other='⛔ 无 Esc 处理；Layer=**Popup**（:135）⇒ 遮罩掐掉 HUD 鼠标出口 ⇒ 出口只有这颗按钮',
   num='目的地名 WaypointPanel.cs:302；空提示 :310；标题 :313；提示 :314',
   disp='无', verdict='OK（有可见出口）',
   src='client/Assets/Scripts/UI/WaypointPanel.cs:40,135,156-158,278-279,302-314,345-348'),
}


def write_audit(panels, prefabs, code, builder):
    """把扫盘结果 + JUDGE 判列写成 `<项目根>/.ai-tmp/test/u3_popup_audit.tsv`。"""
    hdr = ['面板名', 'prefab在否', '层(Popup/Normal/Top)', '原版出处(prefab文件:行或"无")',
           '原版有没有关闭控件', '我们有没有关闭控件', '关闭图形在盘否', '点击真能关否',
           '其它出口(Esc/右键/再点)', '数值列来源(模块:行)', '显示问题', '结论(OK/缺陷/BLOCKED)', '出处']
    missing = [n for n in panels if n not in JUDGE]
    if missing:
        raise SystemExit('[fatal] JUDGE 缺这些面板的判列（⛔ 不许跳面板）: %s' % missing)
    extra = [n for n in JUDGE if n not in panels]
    if extra:
        raise SystemExit('[fatal] JUDGE 里有扫盘结果里不存在的面板（⛔ 不许手写面板名）: %s' % extra)

    rows = []
    for n in panels:
        j = JUDGE[n]
        cd = code.get(n)
        layer = '?'
        if cd:
            layer = cd['layer'].replace('UILayerN:', '')
        rows.append((n, '有' if n in prefabs else '无', layer,
                     j['orig_src'], j['orig_close'], j['our_close'], j['art'],
                     j['click'], j['other'], j['num'], j['disp'], j['verdict'], j['src']))
    out = os.path.join(ROOT, '.ai-tmp/test/u3_popup_audit.tsv')
    with io.open(out, 'w', encoding='utf-8-sig', newline='') as f:
        f.write('\r\n'.join(['\t'.join(hdr)] + ['\t'.join(r) for r in rows]) + '\r\n')
    print('审计表 = %d 行 → %s' % (len(rows), out))
    order = {'OK': 0, '缺陷(a)': 1, '缺陷(c)': 2, '缺陷(b)': 3, 'BLOCKED': 4}
    def rank(v):
        for k, r in order.items():
            if v.startswith(k):
                return r
        return 9
    bad = [r[0] for r in rows if not r[11].startswith('OK')]
    print('非 OK 面板 %d 个：%s' % (len(bad), ', '.join(bad)))


def main():
    _safe_stdio()
    prefabs = src_prefabs()
    code = src_code()
    builder, builder_ln = src_builder()
    engine = src_engine()
    allcls = src_all_classes()

    # 面板 = 「*Panel 的 UIPanel 子类 或 磁盘上 *Panel.prefab」，取并集
    names = set(prefabs) | set(code) | set(builder)
    # 只保留形如 *Panel 的（通用件/非面板件另列）
    panels = sorted(n for n in names if n.endswith('Panel'))

    print('== 四路来源规模 ==')
    print('① prefabs  = %d 个：%s' % (len(prefabs), ', '.join(sorted(prefabs))))
    print('② UIPanel 子类 = %d 个：%s' % (len(code), ', '.join(sorted(code))))
    print('③ 构建器 PanelNames = %d 个（第 %d 行起）' % (len(builder), builder_ln))
    print('④ 引擎通用件 %d 个：%s' % (len(engine), ', '.join('%s@%s' % (k, v) for k, v in sorted(engine.items()))))
    print()

    only_prefab = sorted(set(prefabs) - set(code))
    only_code = sorted(set(code) - set(prefabs))
    builder_not_disk = sorted(set(builder) - set(prefabs))
    prefab_not_builder = sorted(set(prefabs) - set(builder))
    code_not_builder = sorted(set(code) - set(builder))
    builder_not_code = sorted(set(builder) - set(code))

    print('== 三路差集 ==')
    print('A prefab 有 / 代码无 UIPanel 子类 : %s' % (only_prefab or '空'))
    print('B 代码有 UIPanel 子类 / prefab 无 : %s' % (only_code or '空'))
    print('C 构建器有 / 磁盘 prefab 无       : %s' % (builder_not_disk or '空'))
    print('D prefab 有 / 构建器清单无        : %s' % (prefab_not_builder or '空'))
    print('E 代码有 / 构建器清单无           : %s' % (code_not_builder or '空'))
    print('F 构建器有 / 代码无               : %s' % (builder_not_code or '空'))
    print()

    # 非面板（UIPanel 之外）的 UI 件：*Panel 之外还在 UI/ 下的组件
    others = sorted(n for n in allcls if not n.endswith('Panel'))
    print('== UI/ 下非 *Panel 的 class/struct/enum（Tooltip 类等，可能也是"弹框"）==')
    for n in others:
        print('  %-24s %s' % (n, allcls[n]))
    print()

    print('== 逐面板证据（供判列）==')
    for n in panels:
        pf = prefabs.get(n)
        cd = code.get(n)
        print('── %s' % n)
        print('   prefab : %s' % (pf['origin'] if pf else '⛔ 磁盘无此 prefab'))
        if cd:
            print('   layer  : %s   [%s]' % (cd['layer'], cd['layer_src']))
            cl = panel_close_lines(cd['code'], cd['code_path'])
            print('   关闭出口代码命中 %d 处：' % len(cl))
            for x in cl[:14]:
                print('       ' + x)
            ts = panel_text_sites(cd['code'], cd['code_path'])
            print('   文本写者 %d 处：（前 12）' % len(ts))
            for x in ts[:12]:
                print('       ' + x)
        else:
            print('   ⛔ 无 UIPanel 子类')
        print('   builder: %s' % builder.get(n, '⛔ 不在构建器清单'))
        print()

    write_audit(panels, prefabs, code, builder)

    # 写 roster.tsv
    hdr = ['面板名', 'prefab在否', 'UIPanel子类在否', '在构建器清单否', '层',
           'prefab出处', '代码出处', '构建器出处', '关闭出口代码命中数', '文本写者数']
    rows = []
    for n in panels:
        pf = prefabs.get(n)
        cd = code.get(n)
        rows.append((n, '有' if pf else '无', '有' if cd else '无',
                     '有' if n in builder else '无',
                     (cd['layer'] if cd else '?'),
                     (pf['origin'] if pf else ''), (cd['origin'] if cd else ''),
                     builder.get(n, ''), str(len(panel_close_lines(cd['code'], cd['code_path'])) if cd else ''),
                     str(len(panel_text_sites(cd['code'], cd['code_path'])) if cd else '')))
    with io.open(OUT_TSV, 'w', encoding='utf-8-sig', newline='') as f:
        f.write('\r\n'.join(['\t'.join(hdr)] + ['\t'.join(r) for r in rows]) + '\r\n')
    print('面板总数 = %d  → 已写 %s' % (len(panels), OUT_TSV))


if __name__ == '__main__':
    main()
