// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/ResPaths.cs
// 全部资源路径**唯一来源**（`tools/ai-skill/conventions.md`：禁止散落字面量）。
// 目的：**换素材不动逻辑** —— 素材到位/替换时只改本文件。
//
// 目录口径（与 agent-03 的落位表一致，见 `docs/agents/agent-03-原版素材落位.md` §4.1）：
//   D2/UI/Cursor/      光标                     （pixel 由 agent-03 切分）
//   D2/UI/Panel/       控制面板 / 血球 / 蓝球 / 经验条 / 背包 / 人物属性 / 买卖与金币按钮
//   D2/UI/Menu/        主菜单屏 / 职业选择屏 / 载入屏 / 多人屏 / 按钮
//   D2/UI/EquipSlot/   装备栏底图
//   D2/UI/SkillIcon/   技能图标
//   D2/Fonts/          原版位图字体 font16/24/30/42
//   D2/Tiles/ D2/Objects/ D2/Chars/ D2/Monsters/ D2/Items/   （官方本体解包，后台下载中）
//   Sound/BGM/ Sound/SFX/                                     （引擎约定路径）
//
// ⛔ 契约冻结：带「契约」标记的常量逐字来自 `docs/步骤文档.md` §3.5，**不许改**。
//    只有 `D2/UI/Menu/main_screen` 这类**已被 agent-03/agent-05 引用的具名资源**才允许增补；
//    未确认文件名的素材**不要在这里臆造常量**（先问 agent-03 的 `_dev/assetreport.txt`）。
//
// ★ agent-01 修复（`docs/agents/agent-13-修复轮.md` §C，**主 agent 明确授权**）：
//   ① 具名资源常量**必须与磁盘文件名逐字相符**（`Resources.Load` 只去扩展名、不做模糊匹配）：
//        `PanelBuySellButton` 旧值 `buysellbtn` → **真实** `buysellbtn.DC6.0`；
//        `PanelGoldCoinButton` 旧值 `goldcoinbtn` → **真实** `goldcoinbtn.dc6.0`；
//        并新增 `PanelOverlap`（`overlap`）。
//      旧值**不是报错**，是**静默返回 null** —— 这正是最贵的一类失败，故按实测改正。
//   ② 新增 5 个**多帧条带帧数**常量与 `Frame(path, index)` 帧名助手（见各自注释的实测出处）。
//   ⚠️ §3.5 的**冻结项**（`Root` / `UiPanels` / `D2Ui` / `D2Fonts` / `D2Tiles` / `D2Objects` /
//      `D2Chars` / `D2Monsters` / `D2Items` / `D2Sfx` / `D2Bgm`）**一个字符都没动**。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Def;

namespace Diablo2.Core
{
    /// <summary>资源路径常量与拼接助手。</summary>
    public static class ResPaths
    {
        // ── 契约 §3.5：根与分类（**不许改值**）────────────────────────────────
        /// <summary>契约：`CloverRes.Init("Clover")` 的 Resources 根前缀。</summary>
        public const string Root = "Clover";

        /// <summary>契约：面板预制体目录（引擎约定 `Resources/UI/{类名}`，不用 CloverRes 前缀）。</summary>
        public const string UiPanels = "UI/";

        /// <summary>契约：原版 UI 贴图根（控制面板 / 血球 / 蓝球 / 经验条 / 菜单屏 …）。</summary>
        public const string D2Ui = "D2/UI/";

        /// <summary>契约：原版位图字体（font16/24/30/42）。</summary>
        public const string D2Fonts = "D2/Fonts/";

        /// <summary>契约：等距地形瓦片（`.dt1` 解出）。</summary>
        public const string D2Tiles = "D2/Tiles/";

        /// <summary>契约：建筑 / 树 / 岩石 / 栅栏。</summary>
        public const string D2Objects = "D2/Objects/";

        /// <summary>契约：角色 8 方向动画帧（`{class}` 占位，用 <see cref="CharDir"/> 填）。</summary>
        public const string D2Chars = "D2/Chars/{class}/";

        /// <summary>契约：怪物动画帧（`{name}` 占位，用 <see cref="MonsterDir"/> 填）。</summary>
        public const string D2Monsters = "D2/Monsters/{name}/";

        /// <summary>契约：物品图标。</summary>
        public const string D2Items = "D2/Items/";

        /// <summary>契约：音效（引擎约定路径 `Sound/SFX/{name}`）。</summary>
        public const string D2Sfx = "Sound/SFX/";

        /// <summary>契约：BGM（引擎约定路径 `Sound/BGM/{name}`）。</summary>
        public const string D2Bgm = "Sound/BGM/";

        // ── D2/UI 子目录（agent-03 落位口径）──────────────────────────────────
        /// <summary>主菜单 / 职业选择 / 载入屏 / 多人屏 / 按钮。</summary>
        public const string D2UiMenu = D2Ui + "Menu/";

        /// <summary>控制面板 / 血球 / 蓝球 / 经验条 / 背包 / 人物属性 / 买卖金币按钮。</summary>
        public const string D2UiPanel = D2Ui + "Panel/";

        /// <summary>鼠标光标多帧图。</summary>
        public const string D2UiCursor = D2Ui + "Cursor/";

        /// <summary>装备栏底图。</summary>
        public const string D2UiEquipSlot = D2Ui + "EquipSlot/";

        /// <summary>技能图标。</summary>
        public const string D2UiSkillIcon = D2Ui + "SkillIcon/";

        /// <summary>语音（引擎约定；本项目暂未使用）。</summary>
        public const string SoundVoice = "Sound/Voice/";

        // ── 具名资源（文件名来自 `策划/素材调研.md` §4 与 agent-03 任务书 §3）──
        /// <summary>原版主菜单屏（动态壁纸 + 4 个菜单项）。</summary>
        public const string MenuMainScreen = D2UiMenu + "main_screen";

        /// <summary>原版职业选择屏。</summary>
        public const string MenuClassSelectScreen = D2UiMenu + "class_select_screen";

        /// <summary>原版载入屏（读条背景）。</summary>
        public const string MenuLoadScreen = D2UiMenu + "load_screen";

        /// <summary>原版多人游戏屏（本项目多人项点不进去，仅作入口美术）。</summary>
        public const string MenuMultiPlayerScreen = D2UiMenu + "multi_player_screen";

        /// <summary>原版中等宽度按钮（多帧）。</summary>
        public const string MenuButtonMedium = D2UiMenu + "button_medium";

        /// <summary>原版宽按钮（多帧）。</summary>
        public const string MenuButtonWide = D2UiMenu + "button_wide";

        /// <summary>原版生命球底图（左红球）。</summary>
        public const string PanelHealthBar = D2UiPanel + "healthbar";

        /// <summary>原版法力球底图（右蓝球）。</summary>
        public const string PanelManaBar = D2UiPanel + "manabar";

        /// <summary>原版背包面板底图。</summary>
        public const string PanelInventory = D2UiPanel + "inventory";

        /// <summary>原版人物属性面板底图。</summary>
        public const string PanelCharStat = D2UiPanel + "charstat";

        /// <summary>
        /// 原版买卖按钮（**多帧条带，22 帧** —— 见 <see cref="FrameCountBuySellButton"/> 与 <see cref="Frame"/>）。
        /// <para>⚠️ 文件名是磁盘上的真实拼写 `buysellbtn.DC6.0.png`（**DC6 大写 + `.0`**）：
        /// `Resources.Load` 的路径要去掉扩展名 ⇒ 常量值 = `D2/UI/Panel/buysellbtn.DC6.0`。
        /// 出处：`client/_dev/assetreport.txt` 实测 + `Assets/Editor/AssetImporter.cs` 的
        /// `MultiFrameStrips[0].FileName`（agent-10 实测表）。写成 `buysellbtn` 会**静默取不到图**。</para>
        /// </summary>
        public const string PanelBuySellButton = D2UiPanel + "buysellbtn.DC6.0";

        /// <summary>
        /// 原版金币按钮（**多帧条带，2 帧** —— 见 <see cref="FrameCountGoldCoinButton"/> 与 <see cref="Frame"/>）。
        /// <para>⚠️ 真实文件名 `goldcoinbtn.dc6.0.png`（**全小写 + `.0`**，与买卖按钮大小写不同，别照抄）：
        /// 常量值 = `D2/UI/Panel/goldcoinbtn.dc6.0`。</para>
        /// </summary>
        public const string PanelGoldCoinButton = D2UiPanel + "goldcoinbtn.dc6.0";

        /// <summary>
        /// 原版叠加框（背包 / 人物属性面板的子区域框；**多帧条带，2 帧** ——
        /// 见 <see cref="FrameCountOverlap"/> 与 <see cref="Frame"/>）。
        /// <para>真实文件名 `overlap.png`（单帧宽高与其它不同，也是 `Multiple` 切分）。</para>
        /// </summary>
        public const string PanelOverlap = D2UiPanel + "overlap";

        /// <summary>原版交互光标（多帧；帧序见 `Def.CursorKind`）。</summary>
        public const string Cursor = D2UiCursor + "Cursor";

        /// <summary>原版普通攻击技能图标。</summary>
        public const string SkillIconAttack = D2UiSkillIcon + "SkilliconAttack";

        // ── ★ agent-09（1:1 轮）新增：控制面板上的小箭头按钮（4 帧，逐帧文件）─────────
        //  用途：① 控制面板「展开/收起小面板」的箭头（原版 `ImageExpBarRight` 的子 Button，
        //        见 `ControlPanelNavBarOpeningHandler.ShowNavigationalBar`）；
        //        ② 人物属性面板的四维加点箭头（原版同尺寸 15×24，见 `UiLayoutGame.CharPlusSize`）。
        //
        //  ★★ w4 修（**数据源换成 DC6 导出的那一套**）：原值指向 `menubutton__0__{0..3}.png` ——
        //     那是社区复刻工程 Diablerie（`Assets/Images/ControlPanel/`）的副本，
        //     实测**同画面**但把原版"调色板索引 0 = 透明"写成了**不透明黑 (0,0,0,255)**
        //     （每帧 38 个像素 ⇒ 箭头周围一圈黑点）。现改指本项目 `tools/d2codec/export_d2ui.py`
        //     从原版 `data/global/ui/PANEL/menubutton.DC6` 直接解出的 `menubutton_{0..3}.png`
        //     （尺寸/帧序/内容**一个都没动**），并把 20 个副本文件从磁盘删除。
        //     复跑对账：`python tools/probes/measure/scan_uigame.py --pairs`。
        //     ⚠️ 运行时实际取图走 `UI/UiArt.ArrowFrame(i)`（= 同一个拼法），本 4 个常量供
        //        "路径唯一来源"与自检宿主使用；两处**必须同值**（`uicheck` ㉑ 节断言磁盘存在）。
        /// <summary>原版上箭头·常态（= `PANEL/menubutton_0.png`，DC6 直出）。</summary>
        public const string PanelArrowUp = D2UiPanel + "menubutton_0";

        /// <summary>原版上箭头·按下（= `PANEL/menubutton_1.png`）。</summary>
        public const string PanelArrowUpPressed = D2UiPanel + "menubutton_1";

        /// <summary>原版下箭头·常态（= `PANEL/menubutton_2.png`）。</summary>
        public const string PanelArrowDown = D2UiPanel + "menubutton_2";

        /// <summary>原版下箭头·按下（= `PANEL/menubutton_3.png`）。</summary>
        public const string PanelArrowDownPressed = D2UiPanel + "menubutton_3";

        /// <summary>原版位图字体 16px。</summary>
        public const string Font16 = D2Fonts + "font16";

        /// <summary>原版位图字体 24px。</summary>
        public const string Font24 = D2Fonts + "font24";

        /// <summary>原版位图字体 30px。</summary>
        public const string Font30 = D2Fonts + "font30";

        /// <summary>原版位图字体 42px。</summary>
        public const string Font42 = D2Fonts + "font42";

        // ── 多帧条带的**帧数**（UI 按下标取帧的上界）──────────────────────────
        //  口径（**取帧三步，UI agent 照这个来**）：
        //    ① 该图在 `Assets/Editor/AssetImporter.cs` 的 `MultiFrameStrips` 表里 ⇒ 导入模式 = `Multiple`；
        //    ② 切分后每个子资源名 = `{文件名}_{帧号}`（**帧号从 0 起**，见 `AssetImporter.cs:325`
        //       `name = spec.FileName + "_" + i`，与字体图集的 `AtlasPath + "_" + i` 同口径）；
        //    ③ 取第 i 帧 = `Resources.Load<Sprite>(ResPaths.Frame(path, i))`，i ∈ [0, 下面的帧数)。
        //  也可以一次取全部：`Resources.LoadAll<Sprite>(path)`（顺序 = 帧号序）。
        //  ⚠️ 帧数是**实测值**（`tools/buildcheck/frame_probe.py` 读真实像素得出，输出留档
        //     `tools/buildcheck/frame_probe_out.txt`）。**改这里 = 改契约**：必须重跑那个脚本并同步
        //     `AssetImporter.cs` 的 `MultiFrameStrips`，否则 UI 会取到空图且**不报错**。
        /// <summary>`buysellbtn.DC6.0` 的帧数 = **22**（实测；`AssetImporter.cs` 的 `MultiFrameStrips[0]`）。</summary>
        public const int FrameCountBuySellButton = 22;

        /// <summary>`goldcoinbtn.dc6.0` 的帧数 = **2**（实测）。</summary>
        public const int FrameCountGoldCoinButton = 2;

        /// <summary>`overlap` 的帧数 = **2**（实测）。</summary>
        public const int FrameCountOverlap = 2;

        /// <summary>`button_medium` 的帧数 = **3**（normal / pressed / disabled 三态）。</summary>
        public const int FrameCountMenuButtonMedium = 3;

        /// <summary>`button_wide` 的帧数 = **3**（normal / pressed / disabled 三态）。</summary>
        public const int FrameCountMenuButtonWide = 3;

        // ═════════════════════════════════════════════════════════════════════
        // ★ agent-18 §B 新增（**只增不改**；上面一个字都没动）
        //   本轮把 `.TBL/.DC6` 解出的**原版像素**接进来（`tools/d2codec/export_d2ui.py`）。
        //   来源行：原版素材取自 Diablo II (Blizzard North, 2000) 的 d2data.mpq / patch_d2.mpq，非商用。
        //
        //   ⚠️ 为什么按钮**不用**多帧条带（`MenuButtonWide` + `Frame()`）：
        //     条带的帧矩形是"从连通块反推"的（`AssetImporter.MultiFrameStrips`），而原版
        //     `FrontEnd/WideButtonBlank.dc6` 实测就是 **4 帧 = 2 个按钮 × (256+16 两段)**
        //     ⇒ 本轮直接解出**单个按钮的整幅 PNG**（272×35），路径唯一、帧矩形不需要猜。
        // ═════════════════════════════════════════════════════════════════════

        // ── ★ agent-a3 新增（「经典 load 动画」轮；**只增不改**，上面一个字都没动）────
        //   进图读条画面 = 原版 `data/global/ui/Loading/loadingscreen.dc6` 的 **10 帧 256×256**，
        //   逐帧导出为**独立 PNG**（`UI/Menu/loadingscreen_{i}.png`，i 从 0 起）。
        //   为什么要它：原版进图/读条屏就是「**黑底 + 居中这张 256×256 图**」，
        //   进度靠**帧号**推进（门/传送门开得越大 = 越接近读完），见 Diablerie
        //   `Assets/Scripts/Diablerie/Game/UI/LoadingScreen.cs:10,43,51,58-61,66-68`；
        //   以前 `LoadingPanel` 用的是 `load_screen`（那是**前端标题画**，不是进图读条图）
        //   ⇒ 用户报「那个经典的 load 动画也没做」。
        //   取帧口径与 `AssetImporter.MultiFrameStrips` 那批条带**不同**：本批是**一帧一个文件**
        //   （不是一张条带切 N 帧）⇒ 直接用 <see cref="Frame"/> 拼帧名即可，
        //   每帧都能走 `Resources.Load&lt;Sprite&gt;`（实测：单帧 PNG 按名取稳定，
        //   条带的子 sprite 按名取在本工程会**静默取不到** —— 见 `UiArt.cs` 文件头 §C-④）。

        /// <summary>
        /// 原版进图读条图·**帧名前缀**（10 帧独立 PNG，`UI/Menu/loadingscreen_{i}`，i ∈ [0,10)）。
        /// <para>出处：`原版资源/d2dc6/data/global/ui/Loading/loadingscreen.dc6`（dir=1/fpd=10/10×256×256）
        /// → `tools/d2codec/export_d2ui.py --only loading` → `D2/UI/Menu/loadingscreen_{i}.png`。</para>
        /// </summary>
        public const string MenuLoadingScreen = D2UiMenu + "loadingscreen";

        /// <summary>原版进图读条图的帧数 = **10**（实测，见 <see cref="MenuLoadingScreen"/> 的注释）。</summary>
        public const int FrameCountLoadingScreen = 10;

        /// <summary>原版宽按钮·常态（272×35，= `WideButtonBlank.dc6` 帧 0+1 横向拼接）。</summary>
        public const string BtnWideNormal = D2UiMenu + "btn_wide_normal";

        /// <summary>原版宽按钮·按下（272×35，= `WideButtonBlank.dc6` 帧 2+3）。</summary>
        public const string BtnWidePressed = D2UiMenu + "btn_wide_pressed";

        /// <summary>原版中等按钮·常态（128×35，= `MediumButtonBlank.dc6` 帧 0）。</summary>
        public const string BtnMedNormal = D2UiMenu + "btn_med_normal";

        /// <summary>原版中等按钮·按下（128×35，= `MediumButtonBlank.dc6` 帧 1）。</summary>
        public const string BtnMedPressed = D2UiMenu + "btn_med_pressed";

        /// <summary>原版中等按钮·高亮（悬停）态（= `MediumSelButtonBlank.dc6` 帧 0）。</summary>
        public const string BtnMedSel = D2UiMenu + "btn_med_sel";

        /// <summary>原版中等按钮·高亮 + 按下（= `MediumSelButtonBlank.dc6` 帧 1）。</summary>
        public const string BtnMedSelPressed = D2UiMenu + "btn_med_sel_pressed";

        /// <summary>原版任务日志底图（320×432，= `MENU/questbackground.dc6` 的 tile 拼装）。</summary>
        public const string PanelQuestBack = D2UiPanel + "quest_back";

        /// <summary>原版 NPC 对话框底图（210×158，= `MENU/dialogbackground.DC6`）。</summary>
        public const string PanelDialogBack = D2UiPanel + "dialog_back";

        /// <summary>原版「买卖」页底图（320×432，= `PANEL/buysell.DC6` 的 tile 拼装）。</summary>
        public const string PanelBuySellBack = D2UiPanel + "buysell_back";

        /// <summary>原版交易页底图（320×432，= `PANEL/trade.DC6` 的 tile 拼装）。</summary>
        public const string PanelTradeBack = D2UiPanel + "trade_back";

        /// <summary>原版技能页签按钮（= `MENU/questbutton.DC6`）。</summary>
        public const string PanelQuestTabButton = D2UiPanel + "questbutton";

        /// <summary>原版商店页签（= `PANEL/buyselltabs.DC6`，8 帧 79×31，帧名前缀）。</summary>
        public const string PanelBuySellTabs = D2UiPanel + "buyselltabs";

        /// <summary>原版交易小按钮（= `PANEL/tradebtn.DC6`，2 帧 77×17）。</summary>
        public const string PanelTradeButton = D2UiPanel + "tradebtn";

        /// <summary>原版任务页签（= `MENU/questtabs.dc6`，8 帧 78×30）。</summary>
        public const string PanelQuestTabs = D2UiPanel + "questtab";

        /// <summary>
        /// 原版任务「已完成」图（= `MENU/questdone.dc6`，**21 帧 72×86**）。
        /// <para>出处：`原版资源/d2dc6/data/global/ui/MENU/questdone.dc6`（`dc6.py info` 实测 `dir=1 fpd=21
        /// frames=21 72x86 …`）→ `tools/d2codec/export_d2ui.py` 的 menu 组 → `D2/UI/Panel/questdone_{0..20}.png`。
        /// 与 `MENU/a{章}q{序号}.dc6` 的任务图**同尺寸（72×86）且不透明形状逐像素相同**（实测 alpha 掩码差异 = 0 像素）、
        /// 只是配色为灰 ⇒ 同一图标的「完成」变体。用法见 `UI/QuestLogPanel.SlotArtPathOf`。</para>
        /// </summary>
        public const string PanelQuestDone = D2UiPanel + "questdone";

        /// <summary>
        /// 原版任务格石龛（= `MENU/questsockets.dc6`，2 帧 80×95：帧 0 = 银灰常态、帧 1 = 金框选中）。
        /// <para>出处：`原版资源/d2dc6/data/global/ui/MENU/questsockets.dc6`（`dc6.py info` 实测 dir=1 fpd=2
        /// 两帧均 80×95）→ `tools/d2codec/export_d2ui.py` 的 misc 组 → `D2/UI/Panel/questsocket_{0,1}.png`。</para>
        /// </summary>
        public const string PanelQuestSocket = D2UiPanel + "questsocket";

        /// <summary>中文标题条目录（`data/local/ui/chi/**` 解出的原版**繁体中文**界面标题）。</summary>
        public const string D2UiBanner = D2Ui + "Banner/";

        /// <summary>小地图标记目录（`MINIMAP/mapicons.DC6` 解出，16×16 ×8）。</summary>
        public const string D2UiMiniMap = D2Ui + "MiniMap/";

        /// <summary>中文标题条路径，例：`Banner("inventory")` → `D2/UI/Banner/inventory`。</summary>
        public static string Banner(string name) => D2UiBanner + name;

        /// <summary>小地图标记路径，例：`MiniMapIcon(3)` → `D2/UI/MiniMap/mapicon_3`。</summary>
        public static string MiniMapIcon(int index) => D2UiMiniMap + "mapicon_" + index;

        /// <summary>`MINIMAP/mapicons.DC6` 的帧数 = **8**（16×16 ×8；实测，8 帧全用得上）。</summary>
        public const int FrameCountMiniMapIcon = 8;

        /// <summary>
        /// ★ 片 5：小地图标记**统一使用的是哪一帧**。
        /// <para>
        /// ⚠️ **BLOCKED（片 5，语义出处缺失）**：`MINIMAP/mapicons.DC6` 的 8 帧**没有任何权威
        /// 语义映射**（没有 txt 表、参考工程 `Diablerie/Assets/**` 与 `libd2/**` **都不引用**
        /// 这个 DC6 —— 全仓 grep `mapicons|MINIMAP` 只在 `原版 d2dc6` 里命中），
        /// 而 8 帧是**白色模板**（实测 8 帧只用调色板索引 32 = `#F4F4F4`，原版在运行期用色表
        /// shift 给它们上色）⇒ **光看形状不能断定"哪一帧 = 出入口 / NPC / 玩家"**，
        /// 按任务书「⛔ 不许自己指定语义」⇒ **统一用帧 0**（`mapicon_0`）并把语义登记为 BLOCKED。
        /// 拿到语义后**只改这一个常量**即可（面板代码不用动）。
        /// </para>
        /// </summary>
        public const int MiniMapMarkerFrame = 0;

        /// <summary>
        /// 技能图标路径（原版 48×48 单帧）。
        /// <para>职业码 = `Table/Class.tsv` 的 `code`（ama/sor/nec/pal/bar）；
        /// 帧号 = `(official_id − (6 + 30×(class−1))) × 2`（+1 = 灰化帧），
        /// 依据见 `tools/d2codec/export_d2ui.py::group_skillicons` 的注释。</para>
        /// </summary>
        public static string SkillIcon(string classCode, int frame)
            => D2UiSkillIcon + classCode + "Skillicon_" + frame;

        /// <summary>物品图标路径：原版文件名 = `inv<code>.DC6`（小写），例 `ItemIcon("hax")` → `D2/Items/invhax`。</summary>
        public static string ItemIcon(string code) => D2Items + "inv" + code;

        // ── 配置文件（`Core/ClientConfig.cs` 用）──────────────────────────────
        // ── ★ 片 1（「原版 UI 素材地基」轮）新增：**只增不改**，上面一个字都没动 ────────
        //  出处：`tools/d2codec/export_d2ui.py` 的 `skilltree` / `chifont` / `menu` 组
        //        （`--only skilltree` / `--only chifont` / `--only menu`）。
        //  口径：本轮**只做 1:1 逐帧落位**（不拼 tile），文件名 = `{stem}_{DC6 帧号}`，帧号从 0 起。
        //  取帧：单帧 PNG ⇒ 直接用 <see cref="Frame"/> 拼帧名（与 `loadingscreen` 同口径；
        //        条带的子 sprite 按名取在本工程会静默取不到，见 `UI/UiArt.cs` 文件头 §C-④）。

        /// <summary>
        /// 技能树底图目录（原版 `SPELLS/skltree_{a,b,n,p,s}_back.DC6` 逐帧解出，每类 **16 帧**）。
        /// <para>帧尺寸循环 256×256 / 64×256 / 256×176 / 64×176 ⇒ 4 帧一"页"（320×432）；
        /// **拼页口径未定**（原版是 tile 打包，怎么摆缺权威依据）⇒ 本轮只逐帧落位，
        /// 拼装留给后续片。早先 `panels` 组已拼过 4 页到 `D2/UI/Panel/skltree_*_back_{0..3}.png`。</para>
        /// </summary>
        public const string D2UiSkillTree = D2Ui + "SkillTree/";

        /// <summary>任务说明图目录（原版 `MENU/a{n}q{m}.dc6` 21 个文件 × 27 帧，72×86）。</summary>
        public const string D2UiQuest = D2Ui + "Quest/";

        /// <summary>技能树底图帧数（每职业 16，= 源 DC6 帧数）。</summary>
        public const int FrameCountSkillTreeBack = 16;

        /// <summary>
        /// 技能树底图路径：例 `SkillTreeBack("a", 4)` → `D2/UI/SkillTree/skltree_a_back_4`。
        /// <para>职业字母 = 源 DC6 文件名里的 `{cls}`（a/b/n/p/s，见
        /// `UI/D2Icon.SkillTreeBackPath` 的字母↔职业映射）；帧号 = 源 DC6 帧号，`[0, FrameCountSkillTreeBack)`。</para>
        /// </summary>
        public static string SkillTreeBack(string clsLetter, int frame)
            => D2UiSkillTree + "skltree_" + clsLetter + "_back_" + frame;

        /// <summary>
        /// 任务说明图路径：例 `QuestImage("a1q1", 0)` → `D2/UI/Quest/a1q1_0`。
        /// <para>`file` = 源 DC6 名（小写，`a1q1`..`a4q3`，共 21 个）；帧号从 0 起。
        /// 原版每文件 27 帧 72×86（"哪一帧是哪个任务"的口径未定 ⇒ 本轮只落位，用法留给后续片）。</para>
        /// </summary>
        public static string QuestImage(string file, int frame)
            => D2UiQuest + file + "_" + frame;

        /// <summary>
        /// 中文位图字体图集路径：例 `FontChi(16)` → `D2/Fonts/font16_chi`（与 latin 的 `font16` 同目录、不同文件）。
        /// <para>源：`data/LOCAL/FONT/chi/Font{N}.DC6`（每种 **13806 帧**，整幅打进一张图集，
        /// 列/行与格子尺寸见 `<原版资源>/导出的字体映射/font{N}_chi.tsv` 头部注释与
        /// `tools/d2codec/export_d2ui.py::group_chifont`）。</para>
        /// <para>⚠️ 当前**没有任何 UI 代码在用它**（中文位图字体的排版实现尚未开始）；
        /// 且此图集在 `AssetImporter` 里按 Single 导入（未切子 sprite）——
        /// 将来接排版时要么走 `Texture2D` + UV，要么补切分表。</para>
        /// </summary>
        public static string FontChi(int size) => D2Fonts + "font" + size + "_chi";

        // ── ★ 片 3（「原版中文位图字体接入」轮）新增：**只增不改**，上面一个字都没动 ────
        //  出处：`tools/d2codec/make_chifont_assets.py`（把片 1 的 `font{N}_chi.tsv` 转成
        //        运行期数据资产）。两个文件都在 `Assets/Resources/Clover/D2/Fonts/` 下。
        /// <summary>
        /// 中文位图字体的 **帧→字符 + 排版度量** 表：例 `FontChiMap(16)` → `D2/Fonts/font16_chi_map`。
        /// <para>格式（`font{N}_chi_map.txt`，TextAsset）：`COLS` / `CELL` / `COUNT` 三行表头 +
        /// 每行 `code frame advance col row`。**为什么走数据文件而不是 `.cs` 常量**：
        /// 每字号 13806 条 × 4 字号 ≈ 5.5 万条度量，写死既不可维护，也无法"换素材不动逻辑"。</para>
        /// </summary>
        public static string FontChiMap(int size) => FontChi(size) + "_map";

        /// <summary>
        /// 简体 → 原版字形 码位映射表：`D2/Fonts/font_chi_s2t`。
        /// <para>为什么需要：原版 chi 字模是**繁体**字集（13800 码位），本工程配表文本是简体；
        /// 逐条经"字模 + 原版语料"双重校验后生成（见 `tools/d2codec/make_chifont_assets.py` 文件头）。</para>
        /// </summary>
        public const string FontChiS2T = D2Fonts + "font_chi_s2t";

        // ── ★ 片 4（「启动链路三屏 1:1」轮）新增：**只增不改**，上面一个字都没动 ──────────
        //  出处：`tools/d2codec/export_d2ui.py` 的 `frontend` / `logo` 两组
        //        （`--only frontend,logo`），调色板 = `fechar/Pal.PL2`（依据见该脚本 PL2_FECHAR 注释）。
        //  取帧：**单帧 PNG**（一帧一个文件）⇒ 直接用 <see cref="Frame"/> 拼帧名
        //        （与 `loadingscreen` / `questsocket` 同口径；条带的子 sprite 按名取在本工程会静默取不到，
        //         见 `UI/UiArt.cs` 文件头 §C-④）。

        /// <summary>原版**前端职业半身像**目录根（`data/global/ui/FrontEnd/{cls}/`）。</summary>
        public const string D2UiFrontEnd = D2Ui + "FrontEnd/";

        /// <summary>原版**启动屏 / 标题 logo** 目录（`data/global/ui/Logo/`，单帧 319×177）。</summary>
        public const string D2UiLogo = D2Ui + "Logo/";

        /// <summary>
        /// 职业半身像的**状态码**（= 原版 DC6 名去掉职业前缀后的小写）。
        /// <para>三态语义的**出处**（参考物源码，不是猜的）：`Diablerie/.../Menu/ClassSelect/ClassSelector.cs`
        /// `:167-175`（`Spritesheet.Load($"{classPath}{NU1,NU2,NU3}", PaletteType.Fechar)`）、
        /// `:258`（`ChangeState(BackIdle)` ⇒ 屏上默认 = `NU1`）、`:116-122`（悬停 = `NU2`）、
        /// `:53-70` + `:208-233`（点选后转到 `FrontIdle` = `NU3`）。</para>
        /// </summary>
        public static class Portrait
        {
            /// <summary>默认态（背面待机，`{CLS}NU1`）。</summary>
            public const int Idle = 0;

            /// <summary>悬停态（`{CLS}NU2`）。</summary>
            public const int Hover = 1;

            /// <summary>选中后转正面待机（`{CLS}NU3`）。</summary>
            public const int Front = 2;

            /// <summary>
            /// ★ 片 22：**转身过渡**两段的文件名码（不是"状态"，是播完即隐藏的一次性序列）。
            /// <para>
            /// 出处 = 参考物源码 `Diablerie/.../Menu/ClassSelect/ClassSelector.cs:207-233`：
            /// `FrontTransition.Sprites = {CLS}FW`、`BackTransition.Sprites = {CLS}BW`，
            /// 两者都是 `Loop = false` / `HideOnFinish = true` / **`Fps = 25`**；
            /// 状态机见同文件 `:53-76 MainAnimatorOnFinish`
            /// （`BackIdle(NU1) → 点选 → FrontTransition(FW) → FrontIdle(NU3)`，
            ///  `FrontIdle → 再点 → BackTransition(BW) → BackIdle`）。
            /// </para>
            /// </summary>
            public const string TransitionFront = "fw";

            /// <summary>转回背面的过渡（`{CLS}BW`）；出处同上。</summary>
            public const string TransitionBack = "bw";

            /// <summary>原版过渡序列的播放帧率（`ClassSelector.cs:218/232` 的 `Fps = 25`）。</summary>
            public const float TransitionFps = 25f;

            /// <summary>状态码（用于拼文件名）：0→`nu1` / 1→`nu2` / 2→`nu3`；越界 ⇒ 0 + Warn（不静默）。</summary>
            public static string Code(int state)
            {
                switch (state)
                {
                    case Idle: return "nu1";
                    case Hover: return "nu2";
                    case Front: return "nu3";
                    default:
                        Log.WarnOnce("D2", "respath.portrait.bad_state",
                            $"ResPaths.Portrait.Code 收到未登记的状态 {state}（应为 0/1/2）⇒ 按默认态 nu1 处理");
                        return "nu1";
                }
            }

            /// <summary>状态的中文名（日志用）。</summary>
            public static string Label(int state)
            {
                switch (state)
                {
                    case Idle: return "默认(背面待机)";
                    case Hover: return "悬停";
                    case Front: return "选中(转正面)";
                    default: return "未知状态" + state;
                }
            }
        }

        /// <summary>职业半身像目录，例：`PortraitDir(PlayerClass.Amazon)` → `D2/UI/FrontEnd/amazon/`。</summary>
        public static string PortraitDir(PlayerClass cls)
            => D2UiFrontEnd + cls.ToString().ToLowerInvariant() + "/";

        /// <summary>
        /// 职业半身像帧路径，例：`ClassPortrait(PlayerClass.Amazon, ResPaths.Portrait.Idle, 0)`
        /// → `D2/UI/FrontEnd/amazon/nu1_0`。
        /// <para>帧数 = 源 DC6 的帧数（每职业每态不同，见 `export_d2ui.py::group_frontend` 的输出统计）；
        /// 本轮 UI **只取帧 0**（逐帧播放器登记为范围边界，见回报）。</para>
        /// </summary>
        public static string ClassPortrait(PlayerClass cls, int state, int frame)
            => PortraitDir(cls) + Portrait.Code(state) + "_" + frame;

        /// <summary>
        /// ★ 片 22：**转身过渡**帧路径，例：
        /// `ClassTransition(PlayerClass.Amazon, ResPaths.Portrait.TransitionFront, 0)` → `D2/UI/FrontEnd/amazon/fw_0`。
        /// <para>语义与出处见 <see cref="Portrait.TransitionFront"/> / <see cref="Portrait.TransitionBack"/>
        /// （原版 `{CLS}FW` / `{CLS}BW`，`Loop=false`、`HideOnFinish=true`、`Fps=25`）。</para>
        /// <para>帧数**不在这里定义** —— 每职业不同（`AMFW` 54 / `bafw` 64 …），由 `UI/CharCreatePanel`
        /// 的帧数表给出（那张表的出处 = 导出器 `--only frontend` 的实际输出统计）。</para>
        /// </summary>
        public static string ClassTransition(PlayerClass cls, string code, int frame)
            => PortraitDir(cls) + code + "_" + frame;

        /// <summary>原版启动屏 / 标题 logo（`Logo/logo.DC6`，单帧 **319×177**，= DIABLO II 火焰字标）。</summary>
        public const string MenuLogo = D2UiLogo + "logo";

        /// <summary>`Logo/logo.DC6` 的帧数 = **1**（实测，见 <see cref="MenuLogo"/> 注释）。</summary>
        public const int FrameCountMenuLogo = 1;

        // ── ★ 片 5（「小地图 + 死亡屏 1:1」轮）新增：**只增不改**，上面一个字都没动 ──────────
        //  出处：`tools/d2codec/export_d2ui.py` 的 `menu` 组（`--only menu`）。
        //  取帧：**单帧 PNG**（一帧一个文件）⇒ 直接用 <see cref="Frame"/> 拼帧名
        //        （与 `loadingscreen` / `questsocket` / `logo` 同口径）。

        /// <summary>
        /// 原版**死亡屏底图**（`data/global/ui/MENU/EndGame.dc6`，tile 打包，**8 帧 = 2 页 × 4 块**）。
        /// <para>
        /// 实测（`python tools/d2codec/dc6.py info 原版资源/d2dc6/data/global/ui/MENU/EndGame.dc6`）：
        /// `dir=1 fpd=8 frames=8  256x256 64x256 256x224 64x224  256x256 64x256 256x224 64x224`
        /// ⇒ 每 4 帧一"页"：左列 256 宽 + 右列 64 宽 = **320**；上行 256 高 + 下行 224 高 = **480**
        /// ⇒ **2 页，每页 320×480**（片 1 遗留的"是 320×480×2 还是 320×960"疑问**就此定案**）。
        /// </para>
        /// <para>调色板 = `EndGame/Pal.PL2`（片 1 已定案，判据见 `export_d2ui.py` 的 PL2 注释块）。</para>
        /// </summary>
        public const string MenuEndGameBack = D2UiMenu + "endgame";

        /// <summary>`MENU/EndGame.dc6` 的帧数 = **8**（= 2 页 × 4 块；实测）。</summary>
        public const int FrameCountEndGameBack = 8;

        /// <summary>死亡屏底图**每页的块数** = **4**（2×2：左 256 / 右 64 × 上 256 / 下 224）。</summary>
        public const int EndGameBackTilesPerPage = 4;

        /// <summary>
        /// 死亡屏底图**某页第 i 块**的帧路径：例 `EndGameTile(0, 3)` → `D2/UI/Menu/endgame_3`
        /// （页 0 = 帧 0..3，页 1 = 帧 4..7）。
        /// </summary>
        public static string EndGameTile(int page, int index)
            => Frame(MenuEndGameBack, page * EndGameBackTilesPerPage + index);

        /// <summary>
        /// 原版**死亡屏按钮**（`data/global/ui/MENU/endgameok.dc6`，96×32 ×**2**：常态 / 按下）。
        /// <para>
        /// ★ 调色板 = `EndGame/Pal.PL2`，**片 5 定案**（片 1 用的是 ACT1 = 错的）：
        /// ① 麻点度量（相邻不透明像素对的平均颜色跳变，越小越平滑）实测
        ///    **ACT1 84.5 vs EndGame 28.8**（2.9 倍差距，与 `EndGame.dc6` 同一口径同一方向）；
        /// ② 肉眼复核（联络图 **`Screenshots/p5_palette_check.png`**，片 5 留档）：
        ///    @ACT1 = 满屏**彩色噪点**（错色），@EndGame = 干净的深灰石板按钮。
        /// 复跑命令：`python tools/d2codec/export_d2ui.py 原版资源/d2dc6 client --only menu`。
        /// </para>
        /// </summary>
        public const string MenuEndGameOK = D2UiMenu + "endgameok";

        /// <summary>`MENU/endgameok.dc6` 的帧数 = **2**（常态 / 按下；实测）。</summary>
        public const int FrameCountEndGameOK = 2;

        // ── ★ w5（「选项/暂停底板 = 原版窗框」轮）新增：**只增不改**，上面一个字都没动 ────
        //  出处：`tools/d2codec/assemble_boxpieces.py` —— 把**已在磁盘上的**原版
        //        `MENU/boxpieces.DC6` 22 帧（14×15，`D2/UI/Menu/boxpieces_{0..21}.png`）
        //        按**像素自证反推出来的偏移**拼成整幅窗框，输出到 `D2/UI/Panel/`。
        //  为什么不是直接从 DC6 拼：`原版资源/`（DC6 本体）被 .gitignore 排除、**本机没有**，
        //        那 22 帧的 DC6 offset 表拿不到 ⇒ 用「接缝连续 + 外沿是矩形」两条约束反推
        //        （推导与逐像素判据见该脚本文件头；离线复检在 `uicheck` ㉑ 节 `BoxFrameSide()`）。
        //  ⚠️ 文件名**不带尺寸**（角色名）⇒ 「窗框该多大」的唯一来源是
        //     `UI/UiLayoutFlow.BoxFrame` 派生的 `Settings.BoxSize` / `Pause.BoxSize`，
        //     两者是否一致由 `uicheck` 按 PNG 的 IHDR 断言（不一致必红）。
        /// <summary>选项面板的窗框（原版 `boxpieces` 拼装，432×348 = 36×29 个 12px 格）。</summary>
        public const string PanelBoxFrameSettings = D2UiPanel + "boxframe_settings";

        /// <summary>暂停菜单的窗框（原版 `boxpieces` 拼装，288×180 = 24×15 个 12px 格）。</summary>
        public const string PanelBoxFramePause = D2UiPanel + "boxframe_pause";

        /// <summary>配置文件的 Resources 键（放 `Assets/Resources/Configs/config.json` 时生效）。</summary>
        public const string ConfigResourceKey = "Configs/config";

        /// <summary>配置文件相对 `Application.dataPath` 的路径（Editor / 桌面平台）。</summary>
        public const string ConfigRelativePath = "Configs/config.json";

        // ── 拼接助手（避免业务里拼字符串）────────────────────────────────────
        /// <summary>角色动画目录，例：`CharDir(PlayerClass.Amazon)` → `D2/Chars/amazon/`。</summary>
        public static string CharDir(PlayerClass cls)
        {
            return D2Chars.Replace("{class}", cls.ToString().ToLowerInvariant());
        }

        /// <summary>怪物动画目录，例：`MonsterDir("fallen")` → `D2/Monsters/fallen/`。</summary>
        public static string MonsterDir(string monsterKey)
        {
            return D2Monsters.Replace("{name}", monsterKey ?? string.Empty);
        }

        // ── ★ 片「武器外观接线」新增：角色**装备外观套**目录（**只增不改**，上面一个字都没动）──
        //  出处：`tools/d2codec/export_chars.py --equip-sets` 的产物 = 「身体层 + 武器/盾层合成一张」
        //        的整套 PNG（与徒手套同命名 `{动作}_{方向}_{帧号}.png`，每套带一份 `manifest.json`）；
        //        落位口径见判据脚本 `tools/probes/measure/d2_equip_sets_check.py` 的文件头。
        //  ⛔ 本方法 = 该目录的**路径唯一来源**（不许在别的文件里拼 `"equip"` 这段字符串）。

        /// <summary>
        /// 「装备外观套」目录的目录名模板：`{class}` 填职业小写、`{key}` 填外观 key。
        /// <para>`key` 的拼法由 `Module/View/EquipVisual.KeyOf` 给（`{武器}_{盾}` / `{武器}` / `{盾}`）。</para>
        /// </summary>
        public const string D2CharsEquip = D2Chars + "equip/{key}/";

        /// <summary>
        /// 角色**装备外观套**目录，例：`CharEquipDir(PlayerClass.Amazon, "jav")` →
        /// `D2/Chars/amazon/equip/jav/`。
        /// <para>⛔ 空 key 返回空串（徒手 = 走 <see cref="CharDir"/> 那套，调用方必须先判空）。</para>
        /// </summary>
        public static string CharEquipDir(PlayerClass cls, string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return D2CharsEquip
                .Replace("{class}", cls.ToString().ToLowerInvariant())
                .Replace("{key}", key.ToLowerInvariant());
        }

        /// <summary>物品图标路径，例：`Item("invhp1")` → `D2/Items/invhp1`。</summary>
        public static string Item(string icon) => D2Items + icon;

        /// <summary>音效路径，例：`Sfx("hit")` → `Sound/SFX/hit`。</summary>
        public static string Sfx(string key) => D2Sfx + key;

        /// <summary>BGM 路径，例：`Bgm("town")` → `Sound/BGM/town`。</summary>
        public static string Bgm(string key) => D2Bgm + key;

        /// <summary>地形瓦片路径，例：`Tile("grass")` → `D2/Tiles/grass`。</summary>
        public static string Tile(string name) => D2Tiles + name;

        /// <summary>场景物件路径，例：`ObjectSprite("tree1")` → `D2/Objects/tree1`。</summary>
        public static string ObjectSprite(string name) => D2Objects + name;

        /// <summary>
        /// **帧名助手**（多帧条带取单帧用）：`Frame(path, i)` → `"{path}_{i}"`，帧号 **i 从 0 起**。
        /// <para>依据：`Multiple` 切分后每个子资源名 = `{文件名}_{帧号}`。出处
        /// `client/Assets/Editor/AssetImporter.cs:325`（`name = spec.FileName + "_" + i`，`BuildStripRects`），
        /// 与字体图集的取法同口径（`UI/D2Text.cs` 的 `AtlasPath + "_" + i`）。</para>
        /// <para>例：`Frame(ResPaths.PanelBuySellButton, 3)` → `D2/UI/Panel/buysellbtn.DC6.0_3`
        /// —— 可直接交 `Resources.Load&lt;Sprite&gt;()`。帧数上限见 <see cref="FrameCountBuySellButton"/> 等常量。</para>
        /// </summary>
        /// <param name="path">条带路径（**不带扩展名、不带帧号**，即 <see cref="PanelBuySellButton"/> 这类常量）。</param>
        /// <param name="index">帧号，从 0 起；合法范围 `[0, FrameCount*)`。</param>
        public static string Frame(string path, int index)
        {
            if (index < 0 || string.IsNullOrEmpty(path))
            {
                // 非预期分支（参数校验失败）：不静默、不假装成功 —— 只报一次避免高频调用刷屏。
                // 返回值保持"忠实拼接"（不偷偷换成第 0 帧）：这样加载失败会落回调用方自己的
                // 「资源缺失」分支，问题不会被掩盖。
                Log.WarnOnce("D2", "respath.frame.bad_args",
                    $"ResPaths.Frame 参数不合法：path=\"{path}\" index={index}（期望 path 非空、index ≥ 0）" +
                    " ⇒ 结果一定取不到图，请检查调用方的帧号上界");
            }

            return path + "_" + index;
        }
    }
}
