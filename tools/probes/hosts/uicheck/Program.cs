// ─────────────────────────────────────────────────────────────────────────────
// UI 自检宿主（**非 Unity 工程、不参与打包；离线跑，秒级**）
//
// 为什么是"真断言"而不是"看起来 0 错误"：
//   用户尚未打开编辑器 ⇒ 面板实例造不出来（`new GameObject()` 需要原生运行时），
//   所以本宿主断言的是**能离线验证、且正是验收表的判据**的那些东西：
//     ① 每个面板：类名 ↔ 预制体路径（`UI/{类名}`）↔ 层（Normal/Popup/Top）↔ 监听/发出的 `Events.*` 常量
//        —— 逐条**反射 + 读源码文本**双向核对（报告的"逐面板列出"一栏由此可自证）；
//     ② 分层自检 ③：`UI/**` 里 `using Diablo2.Module` **0 命中**（锚定正则，排除注释里的禁令）
//        外加「无 `GameObject.Find` / `FindObjectOfType` / 裸 `Debug.Log`」；
//     ③ 背包格：与 `GameConst` 一致（40 格）**且**与 `inventory.png` 实测格线一致（292×117，步进 29.2/29.25）；
//     ④ `constraints.md` #3：`UiBar.Decide(无 sprite) == 锚点填充`（**永不允许 Filled + 空 sprite**）；
//     ⑤ 品质配色 5 色互不相同（贴颜色值）；
//     ⑥ 位图字体 advance 表：与 `ThirdParty/.../font{N}.txt` 抽出的**已知值**逐点核对；
//     ⑦ `OnOpen` 漏参数：`UiLog.Require<T>` 必须**打 Warn 且返回 null**（面板据此降级、不崩）；
//     ⑧ 所有引用到的 `ResPaths.*` 贴图**真在磁盘上**（防止路径写错却编译通过）。
//
// 不覆盖（需要 Unity 原生 / 由主 agent 进 Play 后验）：
//   · 面板实例化、构件层级与像素布局；· 贴图异步加载完成后的观感；· 球/条真的在动（看图）
//   · `Time.timeScale` 与 `*Unscaled` 定时器的实际触发（`tools/flowcheck` 同样标注）。 
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>收集日志的替身（用于断言"漏参数必须打 Warn"）。</summary>
    internal sealed class CaptureLogger : CloverEngine.ILogger
    {
        public sealed class Entry
        {
            public string Level;
            public string Tag;
            public string Msg;
            public override string ToString() => $"[{Level}][{Tag}] {Msg}";
        }

        public readonly List<Entry> All = new List<Entry>();

        public void Info(string tag, string msg) => All.Add(new Entry { Level = "INFO", Tag = tag, Msg = msg });
        public void Warn(string tag, string msg) => All.Add(new Entry { Level = "WARN", Tag = tag, Msg = msg });
        public void Error(string tag, string msg, Exception ex = null) => All.Add(new Entry { Level = "ERROR", Tag = tag, Msg = msg });
        public void Debug(string tag, string msg) => All.Add(new Entry { Level = "DEBUG", Tag = tag, Msg = msg });
        public void Fatal(string tag, string msg, Exception ex = null) => All.Add(new Entry { Level = "FATAL", Tag = tag, Msg = msg });

        /// <summary>是否存在一条满足谓词的日志。</summary>
        public bool Has(string level, string tag, string mustContain)
        {
            for (var i = 0; i < All.Count; i++)
            {
                var e = All[i];
                if (e.Level != level) continue;
                if (tag != null && e.Tag != tag) continue;
                if (mustContain != null && (e.Msg == null || !e.Msg.Contains(mustContain))) continue;
                return true;
            }
            return false;
        }

        public void Clear() => All.Clear();
    }

    /// <summary>一个面板的交付契约（报告里"逐面板列出"的那张表）。</summary>
    internal sealed class PanelSpec
    {
        public Type Type;
        public string Layer;
        public string[] Events;
    }

    public static class Program
    {
        // ★ agent-a3：下面几项由 `private` 放宽到 `internal`，只为了新加的
        //   `LoadingCheck.cs`（进图读条画面 + 区域名弹出）能复用同一套路径与 Check()/失败计数。
        //   行为零变化。
        internal static readonly string ProjectRoot = ResolveProjectRoot();
        internal static readonly string UiDir =
            Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "UI");
        internal static readonly string ResourceRoot =
            Path.Combine(ProjectRoot, "client", "Assets", "Resources");

        /// <summary>
        /// 原版素材/串表树（skill §1.9：下载 / 解包素材的唯一落点）。**`.gitignore` 里明确「不进 git」**
        /// （体积大 + 版权物）⇒ 干净检出 / 未下载素材的机器上它必然不存在。见 <see cref="CheckOriginalRes"/>。
        /// </summary>
        internal static readonly string OriginalResDir = Path.Combine(ProjectRoot, "原版资源");

        /// <summary>
        /// 从宿主自己的可执行目录向上找“含 client/Assets 的那一层” = 仓库根。
        /// 宿主位于 tools/probes/hosts/&lt;名&gt;/bin/&lt;cfg&gt;/&lt;tfm&gt;/；若按调用方 cwd 定位，
        /// 从仓库根运行时会被拼成 &lt;仓库根&gt;/clover-project-diablo2/client/...（一个文件都找不到）。
        /// 找不到就回退成原来的相对写法，保持“从仓库上一级目录运行”的老用法不变。
        /// </summary>
        private static string ResolveProjectRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                if (System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "client", "Assets")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            Console.WriteLine("[warn] 未从可执行目录向上找到含 client/Assets 的仓库根，回退相对路径 clover-project-diablo2");
            return @"clover-project-diablo2";
        }

        internal static CaptureLogger _logger;
        internal static int _fail;
        /// <summary>因「原版资源/ 不在本机」而**跳过**的断言数（环境依赖，不计失败）。</summary>
        internal static int _skip;

        public static int Main()
        {
            _logger = new CaptureLogger();
            Game.Logger = _logger;

            Console.WriteLine("=== Uicheck：HUD 与游戏内面板 离线自检 ===");
            Console.WriteLine();

            CheckPanelsAndLayers();
            CheckWaypoint();                // ★ 片 S2：传送点（激活列表口径 + 接线守卫 + 预制体在盘上）
            CheckLayeringAndForbiddenApis();
            CheckNoDirectResourcesLoad();   // ★ agent-34：E1 例外收口（项目侧 0 命中直连 Resources）
            CheckNoDirectionKeyMove();   // ★ 片 8：原版没有方向键移动 ⇒ 全仓源码 0 命中（验收表 U-1）
            CheckInventoryGrid();
            CheckEquipSlots();
            CheckBarFillDecision();
            CheckQualityColors();
            CheckBitmapFont();
            CheckArgValidation();
            CheckQuestLogic();
            CheckQuestDialogTexts();    // ★ 本片：任务/对话文案 ↔ 原版串表逐条对账（正向 + 反向）
            CheckHudOriginalLayout();
            CheckPanelWiring();
            CheckSpritePathsExist();
            CheckMenuArtBrightness();
            CheckFlowMenuLayout();      // ★ agent-15 §A：流程面板 1:1（原版 800×600 → 1920×1080 逐元素 ×1.8 居中）
            CheckCharCreateR1C();       // ★ R1-C：创角屏过渡逐帧矩形（不变形）+ 名字输入由面板驱动
            CheckNameDefaultReplace();  // ★ R1-F：默认名「Hero」是整体单元（首次键入整体替换 ⇒ 不再粘连）
            CheckHoverRoundTrip();      // ★ hover-probe 片：悬停事件 D2.Input.HoverChanged 往返（消费侧）
            LoadingCheck.Run();         // ★ agent-a3：进图读条画面（原版 10 帧动画）+ 区域名弹出（LevelEntryTitle）
            P5Check.Run();              // ★ 片 5：死亡屏（EndGame）拼装+布局 / 小地图（原版 mapicon、标题已删）
                                        //   ★ w6：automap 素材「在不在」（AUTOMAP 图块表 + AutoMap.txt 不在本机；
                                        //     在的只有横幅 5 张 + mapicons 8 帧）+ 现行画法口径（暗底 #1C1C1C /
                                        //     格心小点 #484848 / 格心小色块 #C4C4C4、超采样 4）+ 与原版的 3 条差异项
            CheckR1EDialogUi();         // ★ R1-E：对话/商店 UI 逻辑（S1~S7，引擎互斥 + 几何 + 字模宽度）
            W3FlowCheck.Run();          // ★ w3：流程屏逐控件审计（原版素材 IHDR ↔ 常量表 ↔ 面板源码）
            W3GameCheck.Run();          // ★ w3：游戏内 UI 逐控件审计（HUD/背包/属性/技能树/任务/图标/字模）
            ShopGridCheck.Run();        // ★ 片 impl-shop：商店 10×10 格盘几何（解原版 PNG 像素）+ 按物品占格摆放 + 边界
            GroundItemLabelCheck.Run(); // ★ 片 impl-K-ui：R8 底图射线全量表（含表完整性）+ R5 地面物品名牌纯函数/接线
            U4Check.Run();              // ★ 片 U4：automap 不压暗 / 悬停世界→格换算近距精确 / tooltip 折行 / 拖拽落点 PlanDrop
            V6Check.Run();              // ★ 片 V6：6 条实机缺陷的离线判据（对话框几何包含/字模降级/悬停字号/商店关闭/automap/拖拽高亮）
            FontScaleCheck.Run();       // ★ 片 font-scale：全仓 `D2Label.Create` 零处漏字号 + 字号唯一出处（FontPx*）

            Console.WriteLine();
            Console.WriteLine("未覆盖（需要 Unity 原生，留给主 agent 进 Play 后验）："
                + "① 面板实例化与构件层级；② 贴图像素对齐与观感（含球/条填充真的在动）；"
                + "③ `Time.timeScale=0` 下 `AfterUnscaled` 的实际触发；④ 拖放手感。");
            Console.WriteLine();
            if (_skip > 0)
            {
                Console.WriteLine($"（另有 {_skip} 项**环境依赖**断言被跳过：原版资源/ 不在本机 —— "
                    + "它不是仓库内容（`.gitignore` 明确排除），恢复办法见 tools/probes/README.md）");
            }
            Console.WriteLine(_fail == 0 ? "=== 自检全部通过 ===" : $"=== 自检失败 {_fail} 项 ===");
            return _fail == 0 ? 0 : 1;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① 面板 ↔ 预制体 ↔ 层 ↔ 事件
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPanelsAndLayers()
        {
            Console.WriteLine("── ① 面板 / 层 / 预制体路径 / 事件 ──");

            var specs = new[]
            {
                new PanelSpec
                {
                    Type = typeof(HudPanel), Layer = "Normal",
                    Events = new[]
                    {
                        "StageEntered", "StageLeft", "HudDirty", "PlayerStatsChanged", "LevelUp",
                        "InventoryChanged", "EquipChanged", "SkillTreeChanged", "QuestChanged",
                        "MapGenerated",     // agent-13 §B-3：缓存 MinimapArgs 并在打开小地图时传入
                        "AreaChanged",      // agent-a3：过门换区 ⇒ 首次进入该区域时弹区域名（LevelEntryTitle）
                        "PanelToggleRequest", "DialogOpen", "ShopOpen", "PlayerDied",
                        // HUD 是游戏内面板入口 ⇒ 还要发出这些请求
                        "UseBeltRequest", "PauseRequest",
                    },
                },
                new PanelSpec
                {
                    Type = typeof(MiniMapPanel), Layer = "Normal",
                    Events = new[] { "MapGenerated", "PlayerGridChanged" },
                },
                new PanelSpec
                {
                    Type = typeof(InventoryPanel), Layer = "Popup",
                    Events = new[]
                    {
                        "InventoryChanged", "EquipChanged", "InventoryFull",
                        // ⚠️ `UseBeltRequest` 已从本面板移除：原版 `InventoryPanel.prefab` **没有腰带行**
                        //    （腰带只在底部控制面板上）⇒ 1:1 轮把面板里那 4 格删了，
                        //    发 `UseBeltRequest` 的只剩 `HudPanel`（它的 spec 里仍列着这条）。
                        "EquipToggleRequest", "ItemDropRequest",
                        // agent-13 §B-1：点装备槽卸下 / 背包内拖放（载荷口径见 Core/Events.cs 注释）
                        "UnequipRequest", "MoveInInventoryRequest",
                    },
                },
                new PanelSpec
                {
                    Type = typeof(CharacterPanel), Layer = "Popup",
                    Events = new[] { "HudDirty", "PlayerStatsChanged", "LevelUp", "StatAllocateRequest" },
                },
                new PanelSpec
                {
                    Type = typeof(SkillTreePanel), Layer = "Popup",
                    Events = new[] { "SkillTreeChanged", "SkillLearned", "SkillLearnRequest", "SkillSelected" },
                },
                new PanelSpec
                {
                    Type = typeof(QuestLogPanel), Layer = "Popup",
                    // ★ 本轮（UI 全量对照）改口径：**接取/交付任务只走 NPC 对话**（原版就没有任务面板按钮），
                    //   这两个事件归 `NpcDialogPanel`（下面的 spec 里仍然核对）⇒ 本面板不再引用它们。
                    //   本面板只收 QuestChanged（刷新）+ QuestCompleted / QuestTurnInDenied（给玩家回馈）。
                    Events = new[] { "QuestChanged", "QuestCompleted", "QuestTurnInDenied" },
                },
                new PanelSpec
                {
                    // ★ R1-E 的 S1（2026-09-20）改层：`Popup` → **`Normal`**。
                    //   为什么必须改：`Popup` 是引擎的**互斥层**（`UIManager.Open` 打开任何 Popup 面板
                    //   时会把同层其它面板全部 `Close` = `Object.Destroy`）⇒ 点商店的「交易」会把对话条
                    //   销毁，模块侧 `_currentNpcId` 残留（本文件 ⑬ 有逐条锚定）。原版行为是**共存**
                    //   （商店打开、对话条仍在）⇒ 只能让对话条降到 `Normal`、商店留在 `Popup`
                    //   （遮罩挂在 Popup 层 ⇒ 遮罩之上的商店才点得动）。依据见 `UI/NpcDialogPanel.cs` 文件头 S1。
                    Type = typeof(NpcDialogPanel), Layer = "Normal",
                    // ★ 本片改口径：面板**不再直接发** `QuestAcceptRequest` / `QuestTurnInRequest` /
                    //   `ShopOpenRequest` —— 面板只发**选项下标**（`DialogOptionChosen`），
                    //   由 `NpcModule.ChooseOption` 反解成接取/交付/开商店（**一条路径**，
                    //   不再有"面板按钮绕开模块"的第二条路径）。动作语义见 `Module/Npc/NpcModule.cs`。
                    Events = new[] { "DialogOpen", "DialogClose", "DialogOptionChosen" },
                },
                new PanelSpec
                {
                    Type = typeof(ShopPanel), Layer = "Popup",
                    Events = new[]
                    {
                        "ShopOpen", "ShopChanged", "ShopClose",
                        "ShopBuyRequest", "ShopSellRequest", "ShopRepairRequest",
                    },
                },
                // ★ 片 S2（2026-09-23，用户「传送点没效果」）：`WaypointPanel` 之前**不在这张表里**
                //   ⇒ 「是 UIPanel 子类 / 覆写了 Layer / 声明的层 / 事件双向核对 / 一个文件一个
                //   MonoBehaviour」这五类契约断言对它是**空集**（本项目最高频的缺陷形态）。
                //   ⚠️ 它**不收**任何事件（打开参数由 `App/AppWaypoint` 直接传给 `Game.UI.Open`），
                //   只**发** `WaypointTravelRequest`（点列表里的一条目的地）。
                new PanelSpec
                {
                    Type = typeof(WaypointPanel), Layer = "Popup",
                    Events = new[] { "WaypointTravelRequest" },
                },
                new PanelSpec
                {
                    Type = typeof(DeathPanel), Layer = "Top",
                    Events = new[] { "PlayerDied", "StageLeft", "ReviveRequest", "Revived" },
                },
                // ★★ 本片（w3 流程屏逐控件审计 I3）：**7 个流程屏原先根本不在这张表里** ⇒
                //   「预制体路径 / 是 UIPanel 子类 / 覆写了 Layer / 源文件存在 / 声明的层 / 事件常量双向核对 /
                //     一个文件一个 MonoBehaviour」这**七类契约断言对流程屏全部是空集**。
                //   这正是本项目最高频的缺陷形态（"定义了但没人查"）。
                //   ⚠️ 其中 4 个屏（Boot / MainMenu / CharSelect / CharCreate）修前**没有显式 override Layer**
                //   —— 靠基类默认值（`PresentationContracts.cs:174` = Normal）走通，文件头那句「层：Normal」
                //   只是**散文**。本片给这 4 个屏补了显式 override（值 = 基类默认值 ⇒ 行为零变化），
                //   下面这条断言才立得起来。
                //   ⚠️ 事件列只放**顶层字符串常量**（`typeof(Events).GetField` 查不到嵌套类 `Events.Fsm.*`）
                //   ⇒ `Fsm.TriggerNewGame` 这类触发器不在列内，由第 ⑲ 节单独断言。
                new PanelSpec
                {
                    Type = typeof(BootPanel), Layer = "Normal",
                    Events = new[] { "BootDone" },
                },
                new PanelSpec
                {
                    Type = typeof(MainMenuPanel), Layer = "Normal",
                    // `Events.MultiplayerUnavailable` **故意不列**：它的收方（AppFlow）与常量都还在
                    // （用户点名删的是**按钮**），但本屏已无任何发方 ⇒ 列进来就成了"只靠注释命中"的假通过。
                    Events = new[] { "QuitRequest" },
                },
                new PanelSpec
                {
                    Type = typeof(CharSelectPanel), Layer = "Normal",
                    Events = new[] { "CharSelectRequest", "CharDeleteRequest", "ToMainMenuRequest" },
                },
                new PanelSpec
                {
                    Type = typeof(CharCreatePanel), Layer = "Normal",
                    Events = new[] { "CharCreateRequest", "CharSelectRequest" },
                },
                new PanelSpec
                {
                    // 读条屏**不发也不收任何 `Events.*`**（纯呈现：进度由 `AppFlow` 直接调 `SetProgress`）
                    // ⇒ 事件列空着是**事实**，第 ⑲ 节另有一条正面断言把它钉住。
                    Type = typeof(LoadingPanel), Layer = "System",
                    Events = new string[0],
                },
                new PanelSpec
                {
                    Type = typeof(SettingsPanel), Layer = "Top",
                    Events = new[] { "VolumeChanged" },
                },
                new PanelSpec
                {
                    Type = typeof(PausePanel), Layer = "Top",
                    Events = new[] { "ResumeRequest", "SaveAndExitRequest", "ToMainMenuRequest" },
                },
                new PanelSpec
                {
                    // ★ 本轮新增：原版风格的二次确认弹窗（替掉引擎默认 uGUI 弹窗）。
                    //   它**不发也不收任何 `Events.*`**（确认/取消走构造时传入的 `Action` 回调，
                    //   动作由调用方——选角屏 / 暂停菜单——自己 `Emit`）⇒ 事件列空着是**事实**。
                    //   层 = `Top`：与引擎确认框同层（`UI.cs:93` 的整段注释解释了为什么必须是最上层）。
                    Type = typeof(D2ConfirmPanel), Layer = "Top",
                    Events = new string[0],
                },
            };

            foreach (var spec in specs)
            {
                var name = spec.Type.Name;
                var prefab = ResPaths.UiPanels + name;
                Check($"{name}：预制体路径 = UI/{name}（引擎约定 Resources/UI/{{类名}}）",
                    prefab == "UI/" + name, prefab);

                Check($"{name}：是 UIPanel 子类", spec.Type.IsSubclassOf(typeof(UIPanel)), spec.Type.BaseType?.Name);

                var layerProp = spec.Type.GetProperty("Layer");
                Check($"{name}：覆写了 Layer",
                    layerProp != null && layerProp.DeclaringType == spec.Type,
                    layerProp?.DeclaringType?.Name);

                var file = Path.Combine(UiDir, name + ".cs");
                if (!File.Exists(file))
                {
                    Check($"{name}：源文件存在", false, file);
                    continue;
                }

                var text = File.ReadAllText(file);
                Check($"{name}：声明的层 = {spec.Layer}",
                    text.Contains("override UILayer Layer => UILayer." + spec.Layer),
                    "在 " + name + ".cs 内检索");

                var missing = new List<string>();
                foreach (var evt in spec.Events)
                {
                    var field = typeof(Events).GetField(evt, BindingFlags.Public | BindingFlags.Static);
                    if (field == null || field.FieldType != typeof(string))
                        missing.Add(evt + "(Events.cs 无此常量)");
                    else if (!text.Contains("Events." + evt))
                        missing.Add(evt + "(文件里没用到)");
                }
                Check($"{name}：事件常量 {spec.Events.Length} 条双向核对", missing.Count == 0,
                    missing.Count == 0 ? "全部命中 Core/Events.cs" : string.Join(", ", missing.ToArray()));

                Check($"{name}：一个文件一个 MonoBehaviour",
                    CountOf(text, ": UIPanel") + CountOf(text, ": MonoBehaviour") <= 1,
                    "UIPanel/MonoBehaviour 出现次数");
            }

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ①-b ★ 片 S2（2026-09-23）：传送点（用户报「传送点没效果」）
        //   判据 2/3 = **面板列表 == 已激活目的地**（= 已去过 − 当前区域；纯函数逐条断言）。
        //   判据 1/3（生成 1 个且坐标与原版表一致）在 `mapcheck Step25`；
        //   判据 3/3（点锚点 ⇒ 面板开 / 选目的地 ⇒ 区域切换）由 Play 驱动采（离线造不出 `Game.Event`）。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckWaypoint()
        {
            Console.WriteLine("── ①-b 传送点：激活列表口径 + 接线守卫 + 预制体在盘上 ──");

            // ① 预制体必须在盘上（引擎约定 `Resources/UI/{类名}`）：找不到 = 面板打不开，而且**不报错**
            var prefab = Path.Combine(ResourceRoot, "UI", "WaypointPanel.prefab");
            Check("WaypointPanel：预制体在盘上（Resources/UI/WaypointPanel.prefab）",
                File.Exists(prefab), prefab);

            // ② 列表口径 = 已去过 − 当前区域，顺序 = AreaId 枚举序（稳定可断言）
            var all = new[] { (int)AreaId.Town, (int)AreaId.BloodMoor, (int)AreaId.DenOfEvil };
            var d1 = WaypointPanel.PlanDests(all, (int)AreaId.Town);
            Check("PlanDests(去过 3 个, 当前=营地) ⇒ 恰 2 条、不含当前区域、按枚举序",
                d1.Count == 2 && d1[0].area == (int)AreaId.BloodMoor && d1[1].area == (int)AreaId.DenOfEvil,
                $"count={d1.Count} [{string.Join(",", d1.ConvertAll(x => x.area + ":" + x.name).ToArray())}]");

            var d2 = WaypointPanel.PlanDests(new[] { (int)AreaId.Town }, (int)AreaId.Town);
            Check("PlanDests(只去过营地, 当前=营地) ⇒ 0 条（= 原版「尚未啟動其他傳送點」）",
                d2.Count == 0, $"count={d2.Count}");

            var d3 = WaypointPanel.PlanDests(new[] { (int)AreaId.Town, (int)AreaId.BloodMoor }, (int)AreaId.BloodMoor);
            Check("PlanDests(去过 营地+野, 当前=野) ⇒ 恰 1 条 = 营地（列表 == 已去过 − 当前）",
                d3.Count == 1 && d3[0].area == (int)AreaId.Town, $"count={d3.Count}");

            var d4 = WaypointPanel.PlanDests(new[] { 999 }, (int)AreaId.Town);
            Check("PlanDests(表外的号 999) ⇒ 0 条（⛔ 不自创目的地集合）", d4.Count == 0, $"count={d4.Count}");

            var d5 = WaypointPanel.PlanDests(null, (int)AreaId.Town);
            Check("PlanDests(null) ⇒ 0 条（不抛）", d5.Count == 0, $"count={d5.Count}");

            Check("区域名 = 原版三张图；表外号给占位名（不猜）",
                WaypointPanel.NameOf((int)AreaId.Town) == "罗格营地"
                && WaypointPanel.NameOf((int)AreaId.BloodMoor) == "血腥荒野"
                && WaypointPanel.NameOf((int)AreaId.DenOfEvil) == "邪恶洞穴"
                && WaypointPanel.NameOf(999).StartsWith("区域#"),
                "罗格营地 / 血腥荒野 / 邪恶洞穴 / 999→占位名");

            // ③ 接线守卫（**回归闸门**）：`App/AppWaypoint.cs` 写好但没人调 `Install` ⇒ 点击 / 到达 /
            //   面板 / 选目的地四条订阅一个都不存在 —— 类在、编译过、日志干净，功能整条静默失效。
            //   ⚠️ 这是**源码级**断言（弱于行为断言），但本片撞上的正是这个失败形态。
            var wiring = File.ReadAllText(
                Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "App", "AppWiring.cs"));
            Check("AppWiring.Install 调了 AppWaypoint.Install（否则整条传送链路静默失效）",
                wiring.Contains("AppWaypoint.Install(ctx)"), "在 App/AppWiring.cs 内检索");
            Check("AppWiring 复位调了 AppWaypoint.ResetStaticForNewPlaySession（防第二局带上一局的已激活集）",
                wiring.Contains("AppWaypoint.ResetStaticForNewPlaySession()"), "在 App/AppWiring.cs 内检索");

            // ④ ★ ui-fix3（实机"面板底图是一整块纯白矩形"）：底图必须引用**原版窗框素材**，
            //    且加载在途的占位底色**不得是白色**（白色兜底就是实机那块白矩形的来源 ——
            //    `UiArt.Art` 缺省兜底 = 白，贴图异步在途的头几帧整块画白）。
            var wpSrc = File.ReadAllText(Path.Combine(UiDir, "WaypointPanel.cs"));
            Check("WaypointPanel：底图引用原版窗框（ResPaths.PanelBoxFrameSettings，⛔ 不许纯色/自画顶替）",
                wpSrc.Contains("ResPaths.PanelBoxFrameSettings"), "在 UI/WaypointPanel.cs 内检索");
            Check("WaypointPanel：加载在途占位底色 = UiArt.PanelBg（深色，⛔ 不是 UiArt.Art 的白色兜底）",
                wpSrc.Contains("boxFrame.color = UiArt.PanelBg"), "在 UI/WaypointPanel.cs 内检索");

            // ⑤ ★ ui-fix3：原版窗框素材本体在盘上且不是纯色块（IHDR 尺寸 = 432×348，d2codec 实测）。
            var framePng = Path.Combine(ResourceRoot, "Clover", "D2", "UI", "Panel", "boxframe_settings.png");
            Check("原版窗框 boxframe_settings.png 在盘上（Resources/Clover/D2/UI/Panel）", File.Exists(framePng), framePng);
            if (File.Exists(framePng))
            {
                int w, h;
                PngSize(framePng, out w, out h);
                Check("原版窗框尺寸 = 432×348（d2codec 拼装实测；≠ 纯色占位）", w == 432 && h == 348, w + "x" + h);
            }

            // ⑥ ★ ui-fix3（拖影三条）：① 图标 = 被拖物品的原版图标（同一条 `D2Icon.ItemIconPath`）；
            //    ② 拖影保持半透明（⛔ 不是实心色块）；③ 拖拽结束拖影/高亮必然隐藏（⛔ 无残留节点）。
            var invSrc = File.ReadAllText(Path.Combine(UiDir, "InventoryPanel.cs"));
            Check("InventoryPanel：拖影贴被拖物品的原版图标（UiArt.SetSprite(_ghost, iconPath)）",
                invSrc.Contains("UiArt.SetSprite(_ghost, iconPath)"), "在 UI/InventoryPanel.cs OnBeginDrag 内检索");
            Check("InventoryPanel：拖影 = 半透明副本（0.65 = _ghost 建立时的既有 alpha，⛔ 不改实心）",
                invSrc.Contains("new Color(ghostTint.r, ghostTint.g, ghostTint.b, 0.65f)"),
                "在 UI/InventoryPanel.cs OnBeginDrag 内检索");
            Check("InventoryPanel：OnEndDrag 隐藏拖影 + 目标格高亮（结束无残留）",
                invSrc.Contains("_ghost.gameObject.SetActive(false)")
                && invSrc.Contains("_dropHighlight.gameObject.SetActive(false)"),
                "在 UI/InventoryPanel.cs OnEndDrag 内检索");

            // ⑦ ★ btn-label-fix（2026-09-23，实机「目的地按钮上的字读不出来」）：**字色可读性**离线断言。
            //   事故形态：`FlowButton` 的默认字色 = 原版 `WideButton.prefab` 的 #191919（0.098 近黑），
            //   而本屏用的原版中等按钮底图是**深板岩灰** ⇒ 字与底图都暗，13px 中文密笔画糊成一块黑。
            //   门槛口径（两条都可复算，⛔ 不是拍的数）：
            //     ① 底板亮度 = `tools/probes/measure/btn_plate_luma.py` 对 `Menu/btn_med_normal.png`
            //        内区（x 22..78% / y 25..75%，alpha>200）实测的**平均 sRGB 亮度** ⇒ 落 btn_plate_luma.tsv；
            //     ② 门槛 = **WCAG 2.1 AA 正文**对比度 **4.5:1**（按钮字按原版 18px Bold 渲染，
            //        18px < 大号文本阈值 18.66px ⇒ 取正文档，⛔ 不取宽松的 3:1）。
            //   对照实测：改动前默认字色（照抄原版 prefab 的 #191919 = 0.098）= 2.79:1 ✗；
            //             改动后 `UiArt.TitleColor`(0.95/0.87/0.60) = 4.70:1 ✓。
            var plateTsv = Path.Combine(ProjectRoot, "tools", "probes", "measure", "btn_plate_luma.tsv");
            var platePy = Path.Combine(ProjectRoot, "tools", "probes", "measure", "btn_plate_luma.py");
            Check("传送点按钮字色：底板亮度实测表 btn_plate_luma.tsv 在位（量法脚本可原地复跑）",
                File.Exists(plateTsv) && File.Exists(platePy),
                File.Exists(plateTsv)
                    ? Path.GetFileName(plateTsv) + " + " + Path.GetFileName(platePy)
                    : "缺 " + plateTsv + "（恢复 = python tools/probes/measure/btn_plate_luma.py）");

            var plateLuma = ReadPlateLuma();

            var wpColor = WaypointPanel.DestLabelColor;
            var wpRatio = ContrastRatioSrgb(wpColor, plateLuma);
            // 已知坏样本 = 本次缺陷的原值（照抄原版 prefab 的 #191919）—— 用它当"判据自检"的负样本
            var oldRatio = ContrastRatioSrgb(OldPrefabButtonText, plateLuma);
            Check("传送点面板每颗目的地按钮的 glyph 颜色：对按钮底图的对比度 ≥ 4.5:1（WCAG 2.1 AA 正文）",
                plateLuma > 0.01f && wpRatio >= 4.5f,
                $"字色 {Describe(wpColor)} vs 底板亮度 {plateLuma:0.###} ⇒ {wpRatio:0.00}:1"
                + $"（门槛 4.5:1；对照：改动前近黑 ButtonText = {oldRatio:0.00}:1 ✗）");
            Check("传送点按钮字色 = 既有配色常量 UiArt.TitleColor（⛔ 不新造颜色）",
                wpColor == UiArt.TitleColor, Describe(wpColor));

            // 源码点计数（⚠️ 不是运行期颗数）：目的地那一处在 for 循环里**只写一次**（覆盖
            // `MaxDests` 颗），加关闭钮一处 ⇒ 源码里应当**恰好 2 处** `FlowButton.Create(`，
            // 且**每一处**都把 `DestLabelColor` 作为实参传进去（少传 = 那颗按钮又变回近黑）。
            var flowCreateN = System.Text.RegularExpressions.Regex.Matches(wpSrc, @"FlowButton\.Create\(").Count;
            var flowColorN = System.Text.RegularExpressions.Regex.Matches(wpSrc, @"DestLabelColor\)").Count;
            Check("WaypointPanel：每处按钮构建都显式传可读字色（目的地循环 ×MaxDests="
                + WaypointPanel.MaxDests + " + 关闭钮 = 2 处源码点）",
                flowCreateN >= 2 && flowColorN == flowCreateN,
                $"{flowColorN}/{flowCreateN} 处带 DestLabelColor");

            // ⑧ ★ btn-label-fix：字模 uvRect 面积 == 图集单格面积（防「uv 取到空白/邻格 ⇒ 看着像黑块」）。
            //   判据全是磁盘事实：`font16_chi_map.txt` 表头 COLS/CELL + 图集 PNG 的 IHDR（无图像库解密）。
            var chiMap = Path.Combine(ResourceRoot, "Clover", "D2", "Fonts", "font16_chi_map.txt");
            var chiAtlas = Path.Combine(ResourceRoot, "Clover", "D2", "Fonts", "font16_chi.png");
            var cols = 0; var cellW = 0; var cellH = 0; var glyphN = 0; var maxCol = -1; var maxRow = -1;
            if (File.Exists(chiMap))
            {
                foreach (var line in File.ReadAllLines(chiMap))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split(' ');
                    if (f[0] == "COLS" && f.Length >= 2) { int.TryParse(f[1], out cols); continue; }
                    if (f[0] == "CELL" && f.Length >= 3)
                    {
                        int.TryParse(f[1], out cellW); int.TryParse(f[2], out cellH); continue;
                    }
                    if (f[0] == "COUNT" && f.Length >= 2) { int.TryParse(f[1], out glyphN); continue; }
                    if (f.Length >= 5)
                    {
                        int mCol, mRow;
                        if (int.TryParse(f[3], out mCol) && int.TryParse(f[4], out mRow))
                        {
                            if (mCol > maxCol) maxCol = mCol;
                            if (mRow > maxRow) maxRow = mRow;
                        }
                    }
                }
            }
            int aw = -1, ah = -1;
            if (File.Exists(chiAtlas)) PngSize(chiAtlas, out aw, out ah);
            Check("字模 uvRect 面积 == 图集单格面积（图集宽 = COLS×格宽、高按格高整分 ⇒ CellUv 每格恰取一格）",
                cellW > 0 && cellH > 0 && cols > 0 && aw > 0 && ah > 0
                && aw % cellW == 0 && ah % cellH == 0 && aw / cellW == cols,
                $"图集 {aw}x{ah} / 格 {cellW}x{cellH} cols={cols}"
                + (aw > 0 ? $"（uv 步长 {cellW / (float)aw:0.######} × {cellH / (float)ah:0.######}）" : ""));
            Check("字模表每条的 (col,row) 都落在图集内 ⇒ uvRect 不越界、不取到图集外空白",
                cellW > 0 && cols > 0 && maxCol >= 0 && maxCol < cols && (maxRow + 1) * cellH <= ah,
                $"maxCol={maxCol} < cols={cols}；maxRow={maxRow} ⇒ 末行底边 {(maxRow + 1) * cellH} ≤ {ah}；表 COUNT={glyphN}");

            Console.WriteLine();
        }

        /// <summary>
        /// WCAG 2.1 对比度 = (L_亮+0.05)/(L_暗+0.05)，L = 相对亮度 0.2126R+0.7152G+0.0722B
        /// （每个 sRGB 分量先按 WCAG 2.1 §相对亮度 线性化）。
        /// <paramref name="bgSrgbLuma"/> = 背景的 **sRGB 域**亮度（0..1，来自 `btn_plate_luma.tsv`）：
        /// 原版石牌是中性灰 ⇒ 用该灰度反推线性亮度即可（不必逐通道）。
        /// </summary>
        private static float ContrastRatioSrgb(Color fg, float bgSrgbLuma)
        {
            var lf = Lin(fg.r) * 0.2126f + Lin(fg.g) * 0.7152f + Lin(fg.b) * 0.0722f;
            var lb = Lin(bgSrgbLuma);
            var hi = Math.Max(lf, lb);
            var lo = Math.Min(lf, lb);
            return (hi + 0.05f) / (lo + 0.05f);
        }

        /// <summary>sRGB 分量 → 线性（WCAG 2.1 / IEC 61966-2-1 的分段函数）。</summary>
        private static float Lin(float c)
        {
            if (c <= 0f) return 0f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        /// <summary>
        /// 本次缺陷的原值 = 照抄原版 `WideButton.prefab` 的 `Text.m_Color`（#191919 = 0.098）。
        /// <para>⚠️ **它不是任何地方的"当前口径"** —— 只作为「判据自检」的**负样本**存在
        /// （SKILL §8.3：改判据必须证明它"对已知坏样本会红"）。⛔ 不要把它拿去当按钮字色。</para>
        /// </summary>
        private static readonly Color OldPrefabButtonText
            = new Color(0.09803922f, 0.09803922f, 0.09803922f, 1f);

        /// <summary>
        /// 读 `tools/probes/measure/btn_plate_luma.tsv` 里 `btn_med_normal.png` 的**平均 sRGB 亮度**。
        /// 返回 -1 = 读不到（缺表/缺行/解析失败）⇒ 依赖它的断言一律红，并给出复跑命令。
        /// <para>为什么走文件而不是在 C# 里写死：uicheck 是**无图像库**的控制台宿主（不引 PNG 解码），
        /// 所以"原版素材到底多亮"这件事由 `btn_plate_luma.py` 量、落盘，本宿主只读结论。</para>
        /// </summary>
        private static float ReadPlateLuma()
        {
            var tsv = Path.Combine(ProjectRoot, "tools", "probes", "measure", "btn_plate_luma.tsv");
            if (!File.Exists(tsv)) return -1f;
            foreach (var line in File.ReadAllLines(tsv))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                if (f.Length < 2 || !f[0].EndsWith("btn_med_normal.png")) continue;
                float v;
                if (float.TryParse(f[1], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out v))
                    return v;
            }
            return -1f;
        }

        /// <summary>读 PNG IHDR 宽高（uicheck 是无依赖控制台宿主，不引图像库）。</summary>
        private static void PngSize(string path, out int w, out int h)
        {
            w = h = -1;
            using (var fs = File.OpenRead(path))
            {
                var buf = new byte[24];
                if (fs.Read(buf, 0, 24) < 24) return;
                w = (buf[16] << 24) | (buf[17] << 16) | (buf[18] << 8) | buf[19];
                h = (buf[20] << 24) | (buf[21] << 16) | (buf[22] << 8) | buf[23];
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 分层自检 ③ + 禁用 API
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckLayeringAndForbiddenApis()
        {
            Console.WriteLine("── ② 分层：UI/** 不许 using Diablo2.Module；禁用 API ──");

            var files = Directory.GetFiles(UiDir, "*.cs", SearchOption.AllDirectories);
            var moduleHits = new List<string>();
            var findHits = new List<string>();
            var debugHits = new List<string>();

            foreach (var f in files)
            {
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    // 锚定版（`_common.md` §4 提醒：非锚定会命中注释里的禁令）
                    if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*using\s+Diablo2\.Module"))
                        moduleHits.Add(Path.GetFileName(f) + ":" + (i + 1));
                    if (line.Contains("GameObject.Find") || line.Contains("FindObjectOfType") || line.Contains("FindObjectsOfType"))
                        findHits.Add(Path.GetFileName(f) + ":" + (i + 1));
                    if (line.Contains("Debug.Log"))
                        debugHits.Add(Path.GetFileName(f) + ":" + (i + 1));
                }
            }

            Check($"UI 文件数 = {files.Length}；`using Diablo2.Module`（锚定）命中 0",
                moduleHits.Count == 0, moduleHits.Count == 0 ? "0 命中" : string.Join(", ", moduleHits.ToArray()));
            Check("UI 里 `GameObject.Find`/`FindObjectOfType` 命中 0", findHits.Count == 0,
                findHits.Count == 0 ? "0 命中" : string.Join(", ", findHits.ToArray()));
            Check("UI 里裸 `Debug.Log` 命中 0", debugHits.Count == 0,
                debugHits.Count == 0 ? "0 命中" : string.Join(", ", debugHits.ToArray()));

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ②-c ★ agent-34（引擎下沉 A3）：**项目侧不许再直连 Unity 的 `Resources.Load*`**
        //
        // 为什么做成脚本：验收表 **E1** 登记的唯一例外就是「为绕开 `Game.Res` 而直连 `Resources.Load*`」，
        //   根因是引擎缺两个能力（① 条带子 sprite 按名取不到、只有整条取才拿得到；② 需要**同步**回答
        //   "这条原版图到底在不在"）。引擎补齐 `IResourceManager.Exists` / `LoadAll<T>` 后项目侧已全部
        //   切走 ⇒ 这条从"例外"升级为**硬判据（0 命中）**：下次谁图省事直连回去，资源根前缀 / 缓存 /
        //   卸载策略 / 热更后端就会静默失效（而画面往往看起来还正常）。
        // 与 `tools/verify.ps1` 的 `hard-rules:Resources.Load` 同口径，但那个只给汇总行；
        //   这里按**文件:行**逐条点名（改错时能直接定位），并顺带断言接口真有这两个能力。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckNoDirectResourcesLoad()
        {
            Console.WriteLine("── ②-c 项目侧禁止直连 Unity 的 `Resources.Load*`（E1 例外收口）──");

            var scriptsDir = Path.Combine(ProjectRoot, "client", "Assets", "Scripts");
            var hits = new List<string>();
            foreach (var f in Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                {
                    var t = lines[i].TrimStart();
                    // 与 verify.ps1 的 `-skipComment` 同口径：说明性注释会**引用**被禁的写法，排除掉
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                    if (System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"Resources\.Load"))
                        hits.Add(Path.GetFileName(f) + ":" + (i + 1));
                }
            }
            Check("`Assets/Scripts` 里直连 `Resources.Load*` 命中 0（E1 例外已消）",
                hits.Count == 0, hits.Count == 0 ? "0 命中" : string.Join(", ", hits.ToArray()));

            // 哨兵：接口必须真的提供这两个同步能力（业务侧调的正是它们；本宿主用替身绑定，签名对齐）
            var resType = typeof(IResourceManager);
            var exists = resType.GetMethod("Exists", new[] { typeof(string) });
            var loadAll = resType.GetMethod("LoadAll");
            Check("`IResourceManager` 有 `bool Exists(string)`（同步存在性：只回答，不加载不驻留）",
                exists != null && exists.ReturnType == typeof(bool),
                exists != null ? exists.ToString() : "(missing)");
            Check("`IResourceManager` 有 `T[] LoadAll<T>(string)`（同步批量取：取不到＝空数组）",
                loadAll != null && loadAll.IsGenericMethodDefinition && loadAll.ReturnType.IsArray
                && loadAll.GetParameters().Length == 1,
                loadAll != null ? loadAll.ToString() : "(missing)");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ②-b ★ 片 8：**原版没有方向键移动**（全局 skill §0「A 没有 ⇒ 不加」）
        //
        // 为什么做成脚本而不是"写在 skill 里"（§0.6：提示词是请求，闸门才是保证）：
        //   这一族东西横跨 6 个文件（键位别名 / 输入读取器 / Player 开关 / 配置字段 /
        //   `Game.Setting` 键 / 选项面板行），任何一处漏删都会"看着已经删了"。
        //   判据 = **全仓源码 0 命中**（注释里也不留），并且**不许删过头**：
        //   `KeySwapWeapon`（原版 W = 切换武器组）必须仍在。
        // ⚠️ 判据字符串只在**本宿主**里出现（`.ai-tmp/` 不进交付）⇒ 不影响"源码 0 命中"。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckNoDirectionKeyMove()
        {
            Console.WriteLine("── ②-b 方向键移动：源码 0 命中（原版只有鼠标点地面移动）──");

            var scriptsDir = Path.Combine(ProjectRoot, "client", "Assets", "Scripts");
            var banned = new[] { "wasd", "use_wasd_move", "TryGetWasdDir", "StepTowards", "UseWasdMove" };
            var hits = new List<string>();
            var fileCount = 0;

            foreach (var f in Directory.GetFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
            {
                fileCount++;
                var lines = File.ReadAllLines(f);
                for (var i = 0; i < lines.Length; i++)
                {
                    // ⚠️ 跳过注释行（skill 模板硬坑 3：「`Select-String` 会给注释行也报命中」）：
                    //   说明性注释会**引用**被禁的名字 —— 例 `PlayerMotor.cs` 写着
                    //   「这里**没有** `StepTowards`：已按 §0 删除」⇒ 扫进去就是**假阳性**，
                    //   而会误报的检查比没有检查更糟（模板第 11 条的教训）。
                    var t = lines[i].TrimStart();
                    if (t.StartsWith("//") || t.StartsWith("*") || t.StartsWith("/*")) continue;
                    foreach (var b in banned)
                    {
                        if (lines[i].IndexOf(b, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        hits.Add(Path.GetFileName(f) + ":" + (i + 1) + " (" + b + ")");
                    }
                }
            }

            Check($"源码 {fileCount} 个 .cs：方向键移动族 0 命中",
                hits.Count == 0, hits.Count == 0 ? "0 命中" : string.Join(", ", hits.ToArray()));

            // 反向判据：**不许删过头** —— 原版 W = 切换武器组必须还在（`GameKey.W`）
            var alias = File.ReadAllText(Path.Combine(scriptsDir, "Def", "GameKeyAlias.cs"));
            Check("`KeySwapWeapon = GameKey.W`（原版 W=切换武器组）仍在",
                alias.Contains("KeySwapWeapon") && alias.Contains("GameKey.W"),
                alias.Contains("KeySwapWeapon") ? "在位" : "**被误删**");
            // 方向键别名不许以"改名"的方式回来：GameKeyAlias 里不许再出现 `GameKey.A/.S/.D/.W` 之外的
            // 裸方向键别名（白名单 = 面板/战斗那几个原版键位）。
            var dirAlias = System.Text.RegularExpressions.Regex.Matches(
                alias, @"public\s+const\s+GameKey\s+Key([WASD])\s*=");
            Check("`GameKeyAlias` 无方向键别名（KeyW/KeyA/KeyS/KeyD）",
                dirAlias.Count == 0, "命中 " + dirAlias.Count);

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③ 背包格：与 GameConst 一致 + 与底图实测对齐
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckInventoryGrid()
        {
            // ③ 的逐条断言已收进 `LayoutGameCheck.CheckInventory()`（agent-09 §B：×1.8 口径）。
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ③b 装备栏 10 槽（含原版双槽拼图的取半逻辑与贴图存在性）
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckEquipSlots()
        {
            Console.WriteLine("── ③b 装备栏 10 槽 ──");

            var slots = InventoryPanel.EquipSlots;
            Check("槽位数 = 10", slots.Length == 10, slots.Length.ToString());

            var seen = new HashSet<string>();
            var spriteOk = true;
            var ringCount = 0;
            foreach (var s in slots)
            {
                seen.Add(s.slot + "#" + s.slotIndex);
                if (s.slot == ItemSlot.Ring) ringCount++;
                if (!s.sprite.StartsWith(ResPaths.D2UiEquipSlot, StringComparison.Ordinal)) spriteOk = false;
                if (s.half < 0 || s.half > 2) spriteOk = false;
            }

            Check("覆盖 Helm/Armor/Weapon/Shield/Gloves/Boots/Belt/Amulet + 双戒指",
                seen.Count == 10 && ringCount == 2, string.Join(", ", seen));

            Check("底图全部来自 ResPaths.D2UiEquipSlot，half ∈ {0,1,2}", spriteOk, "见 InventoryPanel.EquipSlots");

            // 戒指两枚要能被正确区分（同槽多件靠 slotIndex）
            var equip = new List<ItemStack>
            {
                new ItemStack { name = "戒指A", type = ItemType.Armor, gridW = 1, gridH = 1 },
                new ItemStack { name = "戒指B", type = ItemType.Armor, gridW = 1, gridH = 1 },
            };
            var r0 = InventoryPanel.FindEquipped(equip, ItemSlot.Ring, 0);
            var r1 = InventoryPanel.FindEquipped(equip, ItemSlot.Ring, 1);
            Check("同槽多件（双戒指）按 slotIndex 取到不同物品",
                r0 != null && r1 != null && r0 != r1 && r0.name == "戒指A" && r1.name == "戒指B",
                $"{r0?.name}/{r1?.name}");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ constraints.md #3：血球/经验条填充
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckBarFillDecision()
        {
            Console.WriteLine("── ④ 血球/经验条填充（constraints.md #3）──");

            Check("无 sprite ⇒ 锚点填充（**绝不用 Filled + 空 sprite**）",
                UiBar.Decide(false) == FillMode.Anchor, UiBar.Decide(false).ToString());

            Check("有 sprite ⇒ Filled + fillAmount（与原版 ControlPanel.prefab 一致）",
                UiBar.Decide(true) == FillMode.FilledSprite, UiBar.Decide(true).ToString());

            // 代码位置自证（报告里要贴的那两处）
            var hud = File.ReadAllText(Path.Combine(UiDir, "HudPanel.cs"));
            // `UiBar.Set(` 在源码里出现 2 处：BuildOrb（两颗球共用这一处）+ BuildExpBar（经验条）；
            // 断言从 3 改成 2 是因为球那处已抽成共用函数（行为不变，仍是三根条都过 UiBar）。
            Check("HUD 的球/经验条都经 UiBar 设置（不是自己写 fillAmount）",
                CountOf(hud, "UiBar.Set(") == 2 && CountOf(hud, "fillAmount") == 0,
                $"UiBar.Set×{CountOf(hud, "UiBar.Set(")}（BuildOrb 共用一处 + 经验条一处），"
                + $"裸 fillAmount×{CountOf(hud, "fillAmount")}");

            var bar = File.ReadAllText(Path.Combine(UiDir, "UiBar.cs"));
            Check("UiBar 内部：Filled 分支与锚点分支并存，且 Filled 只在有 sprite 时走",
                bar.Contains("img.type = Image.Type.Filled") && bar.Contains("anchorMax = state.Horizontal"),
                "见 UI/UiBar.cs Apply()");

            // 几何契约：锚点模式以**父节点**为基准 ⇒ 球/条必须有同尺寸容器，否则进度会变成整屏色块
            Check("UiBar 两模式都先把矩形归一成「铺满父节点」",
                bar.Contains("rt.anchorMax = Vector2.one;") && bar.Contains("rt.offsetMin = Vector2.zero;"),
                "见 UI/UiBar.cs Apply()");
            Check("HUD 的球建了同尺寸容器（Holder），不是直接挂面板根",
                hud.Contains("Holder") && hud.Contains("UIFactory.CreateCentered(name + \"Holder\""),
                "见 HudPanel.BuildOrb()");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 品质配色 5 色
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckQualityColors()
        {
            Console.WriteLine("── ⑤ 物品品质配色（白/蓝/金/绿/暗金）──");

            var colors = new Dictionary<ItemQuality, Color>();
            foreach (ItemQuality q in Enum.GetValues(typeof(ItemQuality)))
                colors[q] = ItemQualityColor.Of(q);

            var distinct = true;
            foreach (var a in colors)
                foreach (var b in colors)
                {
                    if (a.Key == b.Key) continue;
                    if (ChannelDistance(a.Value, b.Value) < 0.15f) distinct = false;
                }

            Check("5 种品质颜色互不相同", distinct && colors.Count == 5, string.Join("  ", Describe(colors)));

            Check("蓝(魔法) 偏蓝：b 最大", colors[ItemQuality.Magic].b > 0.8f
                && colors[ItemQuality.Magic].b > colors[ItemQuality.Magic].r, Hex(colors[ItemQuality.Magic]));

            Check("金(稀有) 偏黄：r≈g 且 b 低", colors[ItemQuality.Rare].r > 0.9f
                && colors[ItemQuality.Rare].g > 0.9f && colors[ItemQuality.Rare].b < 0.6f,
                Hex(colors[ItemQuality.Rare]));

            Check("绿(套装) 偏绿：g 最大且 r 最低", colors[ItemQuality.Set].g > 0.9f
                && colors[ItemQuality.Set].r < 0.2f, Hex(colors[ItemQuality.Set]));

            Check("暗金(唯一) 偏褐金：r>g>b 且不是亮黄", colors[ItemQuality.Unique].r > 0.6f
                && colors[ItemQuality.Unique].r > colors[ItemQuality.Unique].g
                && colors[ItemQuality.Unique].g > colors[ItemQuality.Unique].b, Hex(colors[ItemQuality.Unique]));

            Check("白(普通) 接近纯白", colors[ItemQuality.Normal].r > 0.9f
                && colors[ItemQuality.Normal].g > 0.9f && colors[ItemQuality.Normal].b > 0.9f,
                Hex(colors[ItemQuality.Normal]));

            // 未知品质的分支必须留下日志。
            // ⚠️ 这里**不能真调** `Of((ItemQuality)99)`：它会走 `Log.WarnOnce` → `Log.ShouldLog`
            //    → `Time.realtimeSinceStartup`（Unity 原生 API），离线宿主调用会抛
            //    SecurityException（与 `tools/flowcheck` 注释里那条同一个原因）。
            //    故改为核对源码里的降级分支（`UiLog.Require` 的 Warn 那条已真跑，证明日志链可用）。
            var tooltipSrc = File.ReadAllText(Path.Combine(UiDir, "ItemTooltip.cs"));
            Check("未知品质有降级分支且打 WarnOnce（不静默）",
                tooltipSrc.Contains("default:") && tooltipSrc.Contains("UiLog.WarnOnce") && tooltipSrc.Contains("品质"),
                "见 ItemTooltip.cs ItemQualityColor.Of()");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑥ 位图字体 advance 表
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckBitmapFont()
        {
            Console.WriteLine("── ⑥ 位图字体（advance 表 / 只渲染拉丁）──");

            foreach (D2Text.D2Font f in Enum.GetValues(typeof(D2Text.D2Font)))
            {
                var table = D2Text.Table(f);
                var ok = table.Length == D2Text.GlyphCount;
                for (var i = 0; i < table.Length && ok; i++)
                    if (table[i] < 1 || table[i] > D2Text.CellWidth(f) + 2) ok = false;

                Check($"{f}：advance 表长度 = 95（32..126）且取值合理", ok,
                    $"len={table.Length}, cell={D2Text.CellWidth(f)}×{D2Text.CellHeight(f)}");
            }

            // 与源产物 `font16.txt` 的已知项逐点核对（该表的值由本次提取命令产出）
            var known = new (char c, int advance)[]
            {
                ('0', 12), ('1', 5), ('A', 12), ('i', 4), ('W', 16), ('/', 9), (' ', 8),
            };
            var knownOk = true;
            var detail = new List<string>();
            foreach (var (c, adv) in known)
            {
                var got = D2Text.Advance(D2Text.D2Font.Font16, c);
                detail.Add($"'{c}'={got}");
                if (got != adv) knownOk = false;
            }
            Check("font16 逐字形 advance 与 font16.txt 抽取值一致", knownOk, string.Join(", ", detail.ToArray()));

            Check("Measure(font16,\"0/0\") = 12+9+12 = 33", D2Text.Measure(D2Text.D2Font.Font16, "0/0") == 33,
                D2Text.Measure(D2Text.D2Font.Font16, "0/0").ToString());

            Check("中文不可用位图字体（无汉字字形）⇒ IsLatinOnly(\"你\") = false",
                !D2Text.IsLatinOnly("你") && !D2Text.IsLatinOnly("生命12") && !D2Text.IsLatinOnly(string.Empty),
                "中文走 UIFactory.DefaultFont()");

            Check("数字/英文可用位图字体", D2Text.IsLatinOnly("12345") && D2Text.IsLatinOnly("Lv 3 / HP"),
                "HUD 球内数字、金币、等级");

            // 格子参数必须与 Assets/Editor/AssetImporter.cs 的 FontGrids 一致（否则切图与排版错位）
            Check("字号格子参数与 AssetImporter.FontGrids 一致（16×18/24×28/31×32/41×43）",
                D2Text.CellWidth(D2Text.D2Font.Font16) == 16 && D2Text.CellHeight(D2Text.D2Font.Font16) == 18
                && D2Text.CellWidth(D2Text.D2Font.Font24) == 24 && D2Text.CellHeight(D2Text.D2Font.Font24) == 28
                && D2Text.CellWidth(D2Text.D2Font.Font30) == 31 && D2Text.CellHeight(D2Text.D2Font.Font30) == 32
                && D2Text.CellWidth(D2Text.D2Font.Font42) == 41 && D2Text.CellHeight(D2Text.D2Font.Font42) == 43,
                "见 D2Text.CellWidth/CellHeight");

            // ★ agent-15 §A 修复回归：位图字模兜底必须用**图集文件名**做前缀。
            //   实测（Play 2026-09-17，`client/_dev/p_font.cs`）：`LoadAll<Sprite>("Clover/D2/Fonts/font42")`
            //   = 256 个子 sprite，名字是 `font42_0..font42_255`（**不含路径**）；旧写法
            //   `AtlasPath(font) + "_"` = `"D2/Fonts/font42_"` ⇒ taken==0 ⇒ 整体降级为默认字体。
            var d2TextSrc = File.ReadAllText(Path.Combine(UiDir, "D2Text.cs"));
            Check("位图字模兜底的前缀 = 图集**文件名**（子 sprite 名 `font42_67` 不含 `D2/Fonts/`）",
                d2TextSrc.Contains("prefix.LastIndexOf('/') + 1")
                && !d2TextSrc.Contains("var prefix = D2Text.AtlasPath(font) + \"_\";"),
                "见 D2Text.TryBulkLoad");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑦ OnOpen 漏参数：Warn 且不崩
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckArgValidation()
        {
            Console.WriteLine("── ⑦ OnOpen 漏参数（constraints.md #7）──");

            _logger.Clear();
            var none = UiLog.Require<PlayerStatsDto>(null, "HudPanel");
            Check("param=null ⇒ 返回 null（面板按空数据打开，不崩）", none == null, "null");
            Check("param=null ⇒ 打 Warn 且点名面板 + 说明缺参数",
                _logger.Has("WARN", UiLog.Tag, "HudPanel") && _logger.Has("WARN", UiLog.Tag, "参数"),
                First(_logger, "WARN"));

            _logger.Clear();
            var wrong = UiLog.Require<PlayerStatsDto>("不是 DTO", "InventoryPanel");
            Check("类型不符 ⇒ 返回 null",
                wrong == null, "null");
            Check("类型不符 ⇒ Warn 里同时写出期望与实际类型",
                _logger.Has("WARN", UiLog.Tag, "PlayerStatsDto") && _logger.Has("WARN", UiLog.Tag, "String"),
                First(_logger, "WARN"));

            _logger.Clear();
            var okObj = new PlayerStatsDto { level = 7 };
            var same = UiLog.Require<PlayerStatsDto>(okObj, "HudPanel");
            Check("参数正确 ⇒ 原样返回且**不打**日志", ReferenceEquals(same, okObj) && _logger.All.Count == 0,
                $"level={same?.level}, logs={_logger.All.Count}");

            _logger.Clear();
            var fallback = UiLog.RequireInt(null, "SkillTreePanel", -1);
            Check("整型载荷缺失 ⇒ 用兜底值 + Warn", fallback == -1 && _logger.Has("WARN", UiLog.Tag, "SkillTreePanel"),
                fallback.ToString());

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑧ 任务日志的状态口径
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckQuestLogic()
        {
            Console.WriteLine("── ⑧ 任务（邪恶洞穴）进度口径 ──");

            var inProgress = new QuestStateDto
            {
                questId = (int)QuestId.DenOfEvil, name = "邪恶洞穴",
                state = QuestState.InProgress, progress = 3, required = 6,
            };
            Check("进行中（3/6）⇒ 剩余 3，不可交付",
                QuestLogPanel.Remaining(inProgress) == 3 && !QuestLogPanel.CanTurnIn(inProgress),
                $"remaining={QuestLogPanel.Remaining(inProgress)}");

            inProgress.progress = 6;
            Check("清光（6/6）⇒ 剩余 0，可交付（原版硬条件）",
                QuestLogPanel.Remaining(inProgress) == 0 && QuestLogPanel.CanTurnIn(inProgress),
                $"remaining={QuestLogPanel.Remaining(inProgress)}");

            var done = new QuestStateDto { state = QuestState.Done, progress = 6, required = 6, rewardClaimed = true };
            Check("已完成 ⇒ Remaining 不出现负数", QuestLogPanel.Remaining(done) == 0, "0");

            // ★ 本片改口径：正文**不再有自写的"状态：X"行**，改成原版串表的任务条目行
            //   （未接取 = `noactivequest` 3723；进行中 = 任务名 + 目标 + 进度行；…见 UI对照.md §②）
            Check("正文 = 原版串：未接取 / 进行中（含进度行）/ 可交付 / 已完成 四态逐字正确",
                QuestLogPanel.TextOf(null) == QuestLogPanel.TextNoActiveQuest
                && QuestLogPanel.TextOf(new QuestStateDto
                {
                    questId = 1, name = "邪惡洞穴", state = QuestState.NotStarted,
                }) == "沒有進行中的任務。"
                && QuestLogPanel.TextOf(new QuestStateDto
                {
                    questId = 1, name = "邪惡洞穴", state = QuestState.InProgress,
                    progress = 3, required = 6, objective = "A\nB",
                }) == "邪惡洞穴\nA\nB\n剩下的怪物：3"
                && QuestLogPanel.TextOf(new QuestStateDto
                {
                    questId = 1, name = "邪惡洞穴", state = QuestState.InProgress,
                    progress = 5, required = 6, objective = "A",
                }) == "邪惡洞穴\nA\n還有一個怪物。"
                && QuestLogPanel.TextOf(new QuestStateDto
                {
                    questId = 1, name = "邪惡洞穴", state = QuestState.ReadyToTurnIn,
                    progress = 6, required = 6, objective = "C",
                }) == "邪惡洞穴\nC"
                && QuestLogPanel.TextOf(new QuestStateDto
                {
                    questId = 1, name = "邪惡洞穴", state = QuestState.Done, objective = "D",
                }) == "邪惡洞穴\nD",
                "见 UI/QuestLogPanel.TextOf（串 3723 / 3738 / 3739）");

            // ── 版面（**依据 = 原版 `MENU/questbackground.dc6` 实测分区**，见 `UiLayoutGame` §⑥）──
            const float K = UiLayoutGame.K;
            Check("任务面板尺寸 = 原版 320×432 ×1.8 = 576×777.6",
                Math.Abs(UiLayoutGame.QuestPanelSize.x - 320f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestPanelSize.y - 432f * K) < 0.01f,
                $"{UiLayoutGame.QuestPanelSize.x}×{UiLayoutGame.QuestPanelSize.y}");

            Check("章节页签 = 原版 4 个 78×30（4×78 = 312 ≈ 320 窄带）",
                UiLayoutGame.QuestActCount == 4
                && Math.Abs(UiLayoutGame.QuestTabSize.x - 78f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestTabSize.y - 30f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestTabX(0) - (43f - 160f) * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestTabX(3) - (277f - 160f) * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestTabY - (216f - 15f) * K) < 0.01f,
                $"x={UiLayoutGame.QuestTabX(0)},{UiLayoutGame.QuestTabX(3)} y={UiLayoutGame.QuestTabY}");

            // ★ 2026「任务框」轮：石纹区实测净高 = 200（满宽金线 y=28/230）⇒ 2×95 上下各余 5
            //   ⇒ 行心 82.5 / 177.5（旧断言按"y28..230 + 各留 6"取 81.5/176.5，差 1px，已按实测改准）。
            Check("任务格 = 原版 3×2 个 80×95（240×190 嵌进石纹区实测净高 200）",
                UiLayoutGame.QuestSlotCount == 6
                && Math.Abs(UiLayoutGame.QuestSlotSize.x - 80f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestSlotSize.y - 95f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestSlotX(0) - (80f - 160f) * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestSlotX(2) - (240f - 160f) * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestSlotY(0) - (216f - 82.5f) * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestSlotY(1) - (216f - 177.5f) * K) < 0.01f,
                $"colX={UiLayoutGame.QuestSlotX(0)}/{UiLayoutGame.QuestSlotX(2)} " +
                $"rowY={UiLayoutGame.QuestSlotY(0)}/{UiLayoutGame.QuestSlotY(1)}");

            // ★ 本片改口径：石龛里画的是**原版任务图**（`a{章}q{序号}.dc6` / `questdone.dc6`，72×86），
            //   石龛 80×95 ⇒ (80−72)/2=4、(95−86)/2=4.5 ⇒ **居中**（偏移 0）。
            //   ⛔ 旧的 `questicon`（50×52 徽记板 + (−2,+11) 偏移）已不再使用（见 UI对照.md §⑥）。
            Check("任务图 72×86 在石龛 80×95 里居中（偏移 0）",
                Math.Abs(UiLayoutGame.QuestArtSize.x - 72f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestArtSize.y - 86f * K) < 0.01f
                && Math.Abs(UiLayoutGame.QuestArtPos.x) < 0.01f
                && Math.Abs(UiLayoutGame.QuestArtPos.y) < 0.01f,
                $"artSize={UiLayoutGame.QuestArtSize} artPos={UiLayoutGame.QuestArtPos}");

            // ★ 本片改口径：正文 = **一个文本框**（不再是 5 个手填魔数行）⇒ 断言"框在黑芯内 + 行距口径"。
            {
                var boxH = UiLayoutGame.QuestTextBoxSize.y;
                var top = UiLayoutGame.QuestTextTopY;
                var bottom = UiLayoutGame.QuestTextBoxPos.y - boxH * 0.5f;
                var textTop = UiLayoutGame.QuestTextPos.y + UiLayoutGame.QuestTextH * 0.5f;
                var textBottom = UiLayoutGame.QuestTextPos.y - UiLayoutGame.QuestTextH * 0.5f;
                Check("正文字框在黑芯（316×129 原版px）内、上下各留 2 原版px、行宽 300 原版px",
                    Math.Abs(UiLayoutGame.QuestTextBoxSize.x - 316f * K) < 0.01f
                    && Math.Abs(boxH - 129f * K) < 0.01f
                    && Math.Abs(UiLayoutGame.QuestTextH - (129f - 2f * UiLayoutGame.QuestTextPadY) * K) < 0.01f
                    && textTop <= top + 0.01f && textBottom >= bottom - 0.01f
                    && Math.Abs(UiLayoutGame.QuestTextWidth - (316f - 2f * UiLayoutGame.QuestTextPadX) * K) < 0.01f,
                    $"box={UiLayoutGame.QuestTextBoxSize.x}×{boxH} textH={UiLayoutGame.QuestTextH} "
                    + $"textW={UiLayoutGame.QuestTextWidth}");
            }

            // 页签 / 石龛 / 正文黑区：两两不重叠，且都落在面板矩形（±288, ±388.8）内
            static UnityEngine.Rect Deflate(UnityEngine.Rect r, float d)
                => new UnityEngine.Rect(r.x + d, r.y + d, r.width - 2f * d, r.height - 2f * d);

            var qr = UiLayoutGame.QuestRects();
            var overlap = "";
            for (var i = 0; i < qr.Length && overlap.Length == 0; i++)
                for (var j = i + 1; j < qr.Length; j++)
                    // 各内缩 0.5：石龛行是"刚好相接"（202−190=12 ⇒ 上下各 6），
                    // 直接 Overlaps 会被浮点误差（156.59999 vs 156.600006）判成重叠。
                    if (Deflate(qr[i].rect, 0.5f).Overlaps(Deflate(qr[j].rect, 0.5f)))
                    {
                        overlap = qr[i].name + " × " + qr[j].name;
                        break;
                    }
            Check("任务面板图元两两不重叠（页签 4 / 石龛 6 / 正文黑区 1）",
                overlap.Length == 0 && qr.Length == 11, overlap.Length == 0 ? $"{qr.Length} 个矩形" : overlap);

            var outside = "";
            foreach (var (n, r) in qr)
                if (r.xMin < -320f * 0.9f || r.xMax > 320f * 0.9f
                    || r.yMin < -432f * 0.9f || r.yMax > 432f * 0.9f)
                {
                    outside = n;
                    break;
                }
            Check("任务面板图元都落在原版面板矩形内（±288 / ±388.8）", outside.Length == 0,
                outside.Length == 0 ? "全部在内" : outside);

            // 标题条在**面板外**（面板顶沿上方），不与页签行相撞
            var bannerBottom = UiLayoutGame.QuestBannerY - UiLayoutGame.QuestBannerBox.y * 0.5f;
            var tabTop = UiLayoutGame.QuestTabY + UiLayoutGame.QuestTabSize.y * 0.5f;
            Check("标题条整条在面板上方（不与页签行/石龛相撞）",
                bannerBottom >= tabTop - 0.01f && bannerBottom >= UiLayoutGame.QuestPanelSize.y * 0.5f - 0.01f,
                $"bannerBottom={bannerBottom} tabTop={tabTop} panelTop={UiLayoutGame.QuestPanelSize.y * 0.5f}");

            // ★ 本片新增：NPC 对话面板 = **原版石框实测分区**（台词槽/下带），菜单项落在下带内
            {
                // ★ U3 改口径（**不是放宽**）：底图按 `DialogArtScale`(=2) 画 —— 判据改成
                //   「整幅尺寸 == 210×158 × DialogArtScale × K」+「内容行宽 == (205−2×6) × DialogArtScale × K」，
                //   石框下沿钉在 HUD 控制面板上沿（−252）。依据/推导见 `NpcDialogPanel.DialogArtScale`。
                var sc = NpcDialogPanel.DialogArtScale;
                Check("对话面板：整幅 = 原版 210×158 × DialogArtScale(2) ×1.8 = 756×568.8；内容行宽 = (205−12)×2×1.8",
                    Math.Abs(NpcDialogPanel.DialogArtSize.x - 210f * sc * K) < 0.01f
                    && Math.Abs(NpcDialogPanel.DialogArtSize.y - 158f * sc * K) < 0.01f
                    && Math.Abs(NpcDialogPanel.ContentW - (205f - 2f * NpcDialogPanel.TextPadX) * sc * K) < 0.01f
                    && Math.Abs(NpcDialogPanel.FrameBottom - (-252f)) < 0.01f
                    && Math.Abs(NpcDialogPanel.FrameTop
                        - (NpcDialogPanel.FrameBottom + 158f * sc * K)) < 0.01f,
                    $"art={NpcDialogPanel.DialogArtSize} W={NpcDialogPanel.ContentW} top={NpcDialogPanel.FrameTop} bottom={NpcDialogPanel.FrameBottom}");

                var bodyTop = NpcDialogPanel.BodyY + NpcDialogPanel.BodyH * 0.5f;
                var bodyBottom = NpcDialogPanel.BodyY - NpcDialogPanel.BodyH * 0.5f;
                Check("对话面板：台词框（名 + 台词）整块落在石框内、且覆盖实测长槽（原版 y 3..90）",
                    Math.Abs(NpcDialogPanel.TextH
                        - (NpcDialogPanel.TextBottomOrigY - NpcDialogPanel.TextTopOrigY) * sc * K) < 0.01f
                    && bodyTop <= NpcDialogPanel.FrameTop + 0.01f
                    && bodyBottom >= NpcDialogPanel.FrameBottom - 0.01f
                    && bodyBottom <= NpcDialogPanel.Cy(NpcDialogPanel.TextBottomOrigY) + 0.01f,
                    $"bodyTop={bodyTop} bodyBottom={bodyBottom} textH={NpcDialogPanel.TextH}");

                var optBad = "";
                var bandTop = NpcDialogPanel.Cy(NpcDialogPanel.LowerBandY0);
                for (var i = 0; i < NpcDialogPanel.MaxOptions; i++)
                {
                    var cy = NpcDialogPanel.OptionY(i);
                    if (cy + NpcDialogPanel.OptionSize.y * 0.5f > bandTop + 0.01f
                        || cy - NpcDialogPanel.OptionSize.y * 0.5f < NpcDialogPanel.FrameBottom - 0.01f)
                        optBad += i + ":越界 ";
                    if (i > 0 && (NpcDialogPanel.OptionY(i - 1) - cy) < NpcDialogPanel.OptionSize.y - 0.01f)
                        optBad += i + ":重叠 ";
                }
                Check("对话面板：菜单项（最多 3 个）全部落在下带内、两两不重叠、不越石框底沿",
                    optBad.Length == 0, optBad.Length == 0 ? "3 行都在下带内" : optBad);

                Check("对话面板：实测两个 34×34 雕槽常量 = 左 x34..67 / 右 x139..172 / y115..148",
                    Math.Abs(NpcDialogPanel.SlotCellSize - 34f) < 0.01f
                    && NpcDialogPanel.SlotCellLeftX0 == 34f && NpcDialogPanel.SlotCellLeftX1 == 67f
                    && NpcDialogPanel.SlotCellRightX0 == 139f && NpcDialogPanel.SlotCellRightX1 == 172f
                    && NpcDialogPanel.SlotCellY0 == 115f && NpcDialogPanel.SlotCellY1 == 148f
                    && NpcDialogPanel.SlotOuterX0 == 29f && NpcDialogPanel.SlotOuterX1 == 197f
                    && NpcDialogPanel.SlotOuterY0 == 67f && NpcDialogPanel.SlotOuterY1 == 91f,
                    "口径见 UI对照.md §③（逐像素扫金线）");
            }

            Check("石龛序号口径 = questId−1（第一章 6 个任务），越界不就近塞格",
                QuestLogPanel.SlotOf((int)QuestId.DenOfEvil) == 0
                && QuestLogPanel.SlotOf(6) == 5
                && QuestLogPanel.SlotOf(7) == -1
                && QuestLogPanel.SlotOf(0) == -1,
                $"DenOfEvil→{QuestLogPanel.SlotOf((int)QuestId.DenOfEvil)} 7→{QuestLogPanel.SlotOf(7)}");

            // ★ 本片改口径：四态 → **原版任务图**（`a1q1` / `questdone` / 不画）+ 石龛金框（可交付）
            Check("四态 → 原版任务图：未接取=不画 / 进行中=a1q1 / 可交付=a1q1+金框 / 已完成=questdone",
                QuestLogPanel.SlotArtPathOf(1, QuestState.NotStarted) == null
                && QuestLogPanel.SlotArtPathOf(1, QuestState.InProgress) == ResPaths.QuestImage("a1q1", 0)
                && QuestLogPanel.SlotArtPathOf(1, QuestState.ReadyToTurnIn) == ResPaths.QuestImage("a1q1", 0)
                && QuestLogPanel.SlotArtPathOf(1, QuestState.Done) == ResPaths.Frame(ResPaths.PanelQuestDone, 0)
                && QuestLogPanel.SocketFrameOf(new QuestStateDto { state = QuestState.ReadyToTurnIn }) == 1
                && QuestLogPanel.SocketFrameOf(new QuestStateDto { state = QuestState.InProgress, progress = 6, required = 6 }) == 1
                && QuestLogPanel.SocketFrameOf(new QuestStateDto { state = QuestState.InProgress, progress = 1, required = 6 }) == 0
                && QuestLogPanel.SocketFrameOf(new QuestStateDto { state = QuestState.Done }) == 0,
                "见 UI/QuestLogPanel.cs::SlotArtPathOf / SocketFrameOf");

            Check("任务 id → 原版任务图文件名 = a{章}q{章内序号}（21 张：Act I~III 各 6、Act IV 3）",
                QuestLogPanel.QuestArtFileOf(1) == "a1q1"
                && QuestLogPanel.QuestArtFileOf(2) == "a1q2"
                && QuestLogPanel.QuestArtFileOf(6) == "a1q6"
                && QuestLogPanel.QuestArtFileOf(7) == "a2q1"
                && QuestLogPanel.QuestArtFileOf(19) == "a4q1"
                && QuestLogPanel.QuestArtFileOf(21) == "a4q3"
                && QuestLogPanel.QuestArtFileOf(22) == null
                && QuestLogPanel.QuestArtFileOf(0) == null,
                $"1→{QuestLogPanel.QuestArtFileOf(1)} 19→{QuestLogPanel.QuestArtFileOf(19)} "
                + $"21→{QuestLogPanel.QuestArtFileOf(21)} 22→{QuestLogPanel.QuestArtFileOf(22)}");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑧-2 任务日志 / NPC 对话的**画面文案 ↔ 原版串表（TBL）逐条对账**（本片新增）
        //
        // 判据来源（任务书）：「面板里出现的每一句中文都能指出 TBL 串 id（给 id 清单）；
        //   ⛔ 自写文案 0 条」。⇒ 把它做成脚本判定（§1.12：判定权交给脚本，不靠自觉）：
        //   ① **正向**：代码里逐句声明"这句是串 id N" ⇒ 去串表按 N 取原文，逐字（忽略空白/换行）比对；
        //   ② **反向**：两个面板 `.cs` 里**所有含中日韩字符的字符串字面量**都必须能在串表里找到
        //      —— 唯一豁免 = 控制台日志（行内含 `UiLog.` / `Log.`）与引擎 Toast（行内含 `Toast(`），
        //      理由：它们不上"面板"，且 Toast 的中文字体问题已登记在验收表 **E19**。
        // 串表口径：`原版资源/d2text/chi_string.txt` = `id<TAB>[<换行数>\n]<文本>`（换行是**字面** `\n`）；
        //   键名对照 `原版资源/参考工程_Diablierie/Diablerie/Assets/StreamingAssets/data/local/string.txt`。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckQuestDialogTexts()
        {
            Console.WriteLine("── ⑧-2 任务日志 / NPC 对话文案 ↔ 原版串表（TBL）逐条对账 ──");

            var tblPath = Path.Combine(OriginalResDir, "d2text", "chi_string.txt");
            if (!File.Exists(tblPath))
            {
                CheckOriginalRes("原版串表 chi_string.txt 在磁盘上", tblPath);
                Console.WriteLine("      ⇒ 本节 ①正向（逐条按串 id 取原文比对）/ ②反向（面板里每个含中日韩字符的"
                    + "字面量都必须能在串表里找到）一并跳过 —— 判据源不在位时这两条**无法判定**（不是通过）。");
                return;
            }

            var tbl = new Dictionary<int, string>();
            foreach (var line in File.ReadAllLines(tblPath, Encoding.UTF8))
            {
                var tab = line.IndexOf('\t');
                if (tab <= 0 || !int.TryParse(line.Substring(0, tab), out var id)) continue;
                tbl[id] = NormTblText(line.Substring(tab + 1));
            }
            Check("原版串表已解析（5391 条）", tbl.Count >= 5391, $"{tbl.Count} 条");

            // ① 正向：逐条把"代码里声称的串 id"拿到串表里核对
            var claims = new (int id, string key, string where)[]
            {
                (3714, "qstsa1q1", "任务名（DenOfEvilQuest.Name）"),
                (3735, "qstsa1q11", "目标行·找洞（ObjectiveLookForDen）"),
                (3736, "qstsa1q12", "目标行·杀光（ObjectiveKillAll）"),
                (3740, "qstsa1q15", "目标行·领赏（ObjectiveReturnForReward）"),
                (3726, "qstsComplete", "目标行·結束（ObjectiveComplete）"),
                (3723, "noactivequest", "未接取整屏（TextNoActiveQuest）"),
                (3738, "qstsa1q14", "进度前缀（TextMonstersRemainingPrefix）"),
                (3739, "qstsa1q140", "只剩一只（TextOneMonsterLeft）"),
                (64, "A1Q1InitAkara", "阿卡拉·未接取"),
                (71, "A1Q1EarlyReturnAkara", "阿卡拉·进行中"),
                (76, "A1Q1SuccessfulAkara", "阿卡拉·可交付/已完成"),
                (66, "A1Q1AfterInitKashya", "卡夏·未完成"),
                (77, "A1Q1SuccessfulKashya", "卡夏·已完成"),
                (67, "A1Q1AfterInitCharsiMain", "恰西·未完成"),
                (78, "A1Q1SuccessfulCharsi", "恰西·已完成"),
                (69, "A1Q1AfterInitGheed", "基得·未完成"),
                (79, "A1Q1SuccessfulGheed", "基得·已完成"),
                (70, "A1Q1AfterInitWarriv", "瓦瑞夫·未完成"),
                (80, "A1Q1SuccessfulWarriv", "瓦瑞夫·已完成"),
                (2892, "Akara", "NPC 名·阿卡拉"),
                (2893, "Kashya", "NPC 名·卡夏"),
                (2894, "Charsi", "NPC 名·恰西"),
                (2891, "Gheed", "NPC 名·基得"),
                (2896, "Warriv", "NPC 名·瓦瑞夫"),
                (3394, "NPCMenuLeave", "选项·離開"),
                (3386, "NPCMenuNews0", "选项·重要消息"),
                (3334, "NPCMenuTradeRepair", "选项·交易/修理"),
                (3396, "NPCMenuTrade", "选项·交易"),
            };

            var sources = new (string file, string tag)[]
            {
                (Path.Combine(UiDir, "QuestLogPanel.cs"), "UI/QuestLogPanel.cs"),
                (Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "Module", "Quest", "DenOfEvilQuest.cs"),
                    "Module/Quest/DenOfEvilQuest.cs"),
                (Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "Module", "Npc", "NpcDialog.cs"),
                    "Module/Npc/NpcDialog.cs"),
                (Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "Module", "Npc", "NpcModule.cs"),
                    "Module/Npc/NpcModule.cs"),
            };

            var srcNorm = new Dictionary<string, string>();
            foreach (var (file, tag) in sources)
                srcNorm[tag] = NormSource(File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "");

            var missTbl = new List<string>();
            var missSrc = new List<string>();
            foreach (var (id, key, where) in claims)
            {
                if (!tbl.TryGetValue(id, out var text) || text.Length == 0)
                {
                    missTbl.Add($"id={id} 串表里没有原文（{where}）");
                    continue;
                }
                var found = false;
                foreach (var (tag, norm) in srcNorm)
                    if (norm.Contains(text)) { found = true; break; }
                if (!found) missSrc.Add($"id={id}({key}) 的原文没出现在代码里：{where}");
            }

            Check("① 正向：代码里声称的 28 条原版串 id，逐条能在串表里取到原文", missTbl.Count == 0,
                missTbl.Count == 0 ? $"28/28 命中（串 id 清单见 UI对照.md §⑦）" : string.Join(" | ", missTbl.ToArray()));
            Check("② 正向：这 28 条原文逐字出现在 UI/Module 源码里（忽略空白/换行/拼接）", missSrc.Count == 0,
                missSrc.Count == 0 ? "28/28 逐字命中" : string.Join(" | ", missSrc.ToArray()));

            // ② 反向：两个面板里**所有**含中日韩字符的字面量都必须来自串表（豁免 = 日志 / Toast）
            var allowed = new List<string>(tbl.Values);
            var offenders = new List<string>();
            foreach (var panel in new[] { "QuestLogPanel.cs", "NpcDialogPanel.cs" })
            {
                var path = Path.Combine(UiDir, panel);
                if (!File.Exists(path)) continue;

                // ⚠️ 必须**按语句**分组扫描（不能按行）：日志/Toast 的中文经常跨行拼接
                //    （`UiLog.Warn(...` + 下一行的 `+ $"…"`），按行会把续行误判成"画面文案"。
                var buf = new StringBuilder();
                foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    // 先去注释（`//` 到行尾，含 `///` 文档注释）——注释里的中文不算"画面文案"
                    var cut = raw.IndexOf("//", StringComparison.Ordinal);
                    var code = cut >= 0 ? raw.Substring(0, cut) : raw;
                    buf.Append(code).Append('\n');
                    if (code.IndexOf(';') < 0) continue;             // 语句还没结束
                    var stmt = buf.ToString();
                    buf.Clear();

                    if (stmt.Trim().Length == 0) continue;
                    // 豁免：控制台日志（不上屏）+ 引擎 Toast（中文字体问题已登记 E19）
                    if (stmt.Contains("UiLog.") || stmt.Contains("Log.") || stmt.Contains("Toast(")) continue;

                    foreach (Match m in Regex.Matches(stmt, "\"([^\"\\\\]|\\\\.)*\""))
                    {
                        var lit = m.Value.Trim('"');
                        if (!HasCjk(lit)) continue;
                        var n = lit.Replace("\\n", "").Replace(" ", "");
                        if (n.Length == 0) continue;
                        var ok = false;
                        foreach (var a in allowed)
                            if (a.Contains(n)) { ok = true; break; }
                        if (!ok) offenders.Add(panel + "：「" + lit + "」");
                    }
                }
            }
            Check("③ 反向：两个面板里画面中文字面量全部来自原版串表（自写文案 = 0 条）",
                offenders.Count == 0,
                offenders.Count == 0 ? "0 条自写（豁免：UiLog/Log/Toast 三类上不到面板的中文，见 E19）"
                    : string.Join(" | ", offenders.ToArray()));

            Console.WriteLine();
        }

        /// <summary>串表条目归一化：去掉出口前缀（`<换行数>\n`）、字面 `\n` 转义与空白。</summary>
        private static string NormTblText(string s)
            => Regex.Replace(s, @"^\d+\\n", "").Replace("\\n", "").Replace(" ", "").Trim();

        /// <summary>源码归一化：去掉注释里的字面 `\n`、引号与拼接符与空白 ⇒ 相邻字面量自然接上。</summary>
        private static string NormSource(string s)
            => s.Replace("\\n", "").Replace("\"", "").Replace("+", "")
                .Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");

        /// <summary>是否含中日韩字符。</summary>
        private static bool HasCjk(string s)
        {
            foreach (var c in s)
                if (c >= 0x3400 && c <= 0x9FFF) return true;
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑨ HUD 布局：原版实测值 → 引擎中心坐标
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckHudOriginalLayout()
        {
            // ⑨ 的逐条断言已收进 `LayoutGameCheck`（agent-09 §B：HUD / 背包 / 属性 / 技能 / 顶栏，
            //    全部按「原版 prefab 精确 RectTransform × 1.8」判）。
            LayoutGameCheck.Run();
            _fail += LayoutGameCheck.Failures;
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑩ 引用的贴图必须真在磁盘上
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckSpritePathsExist()
        {
            Console.WriteLine("── ⑩ 引用的原版贴图存在于 Resources ──");

            var paths = new List<string>
            {
                ResPaths.D2UiPanel + "ControlPanel",
                ResPaths.PanelHealthBar,
                ResPaths.PanelManaBar,
                ResPaths.D2UiPanel + "ExperienceBar",
                ResPaths.D2UiPanel + "ExperienceBarOverlay",
                ResPaths.PanelInventory,
                ResPaths.PanelCharStat,
                ResPaths.D2UiPanel + "minipanel",
                // ★ w3 审计：帧名改走 `UiArt` 的唯一来源（= DC6 直出那一套，索引 0 = 透明）。
                //   旧值 `menubutton__0__0` / `runbutton_{run,walk}_NotPressed` 是 Diablerie 副本，
                //   同画面但把原版透明像素写成不透明黑（判据见 §㉑ 与 `scan_uigame.py --pairs`）。
                UiArt.ArrowFrame(0),
                UiArt.RunButtonRunFrame,
                UiArt.RunButtonWalkFrame,
                ResPaths.SkillIconAttack,
            };
            foreach (var s in InventoryPanel.EquipSlots)
                if (!paths.Contains(s.sprite)) paths.Add(s.sprite);
            for (var i = 0; i <= 14; i += 2)
                paths.Add(UiArt.MiniPanelBtnFrame(i));

            var missing = new List<string>();
            foreach (var p in paths)
            {
                var file = Path.Combine(ResourceRoot, "Clover", p.Replace('/', Path.DirectorySeparatorChar) + ".png");
                if (!File.Exists(file)) missing.Add(p);
            }

            Check($"{paths.Count} 个贴图路径全部命中磁盘文件", missing.Count == 0,
                missing.Count == 0 ? "0 缺失" : string.Join(", ", missing.ToArray()));

            // 位图字体图集（8 张：4 套 × 1 张）
            var fontMissing = new List<string>();
            foreach (D2Text.D2Font f in Enum.GetValues(typeof(D2Text.D2Font)))
            {
                var p = D2Text.AtlasPath(f);
                var file = Path.Combine(ResourceRoot, "Clover", p.Replace('/', Path.DirectorySeparatorChar) + ".png");
                if (!File.Exists(file)) fontMissing.Add(p);
            }
            Check("4 套位图字体图集存在", fontMissing.Count == 0,
                fontMissing.Count == 0 ? "font16/24/30/42" : string.Join(", ", fontMissing.ToArray()));

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑪ agent-13 §B：UI 侧事件补发 + 小地图数据
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckPanelWiring()
        {
            Console.WriteLine("── ⑪ §B：UI 事件补发（Unequip/Move/Drop/ShopOpen）+ 小地图参数 ──");

            // 换一条干净的事件总线，逐条捕获「四条 Emit 路径」的产出
            Game.Event = new ConsoleEventBus();

            var unequipCount = 0; var unequipPayload = 0;
            var moveCount = 0; var movePayload = 0;
            var dropCount = 0; var dropPayload = 0;
            var strayCount = 0;

            Game.Event.On<int>(Events.UnequipRequest, v => { unequipCount++; unequipPayload = v; });
            Game.Event.On<int>(Events.MoveInInventoryRequest, v => { moveCount++; movePayload = v; });
            Game.Event.On<int>(Events.ItemDropRequest, v => { dropCount++; dropPayload = v; });
            // 本片起**对话面板不再直接发** ShopOpenRequest（见下面 ④ 的源码层断言）
            Game.Event.On<int>(Events.ShopOpenRequest, _ => { });
            Game.Event.On<string>(Events.PanelToggleRequest, _ => strayCount++);   // 不该在这四条路径里出现

            // ① 点装备槽 → UnequipRequest（载荷 = ((int)slot & 0xFF) | (slotIndex << 8)）
            InventoryPanel.EmitUnequipRequest(ItemSlot.Helm, 0);
            Check("点装备槽（头盔）⇒ UnequipRequest 恰好 1 次，载荷 = (int)ItemSlot.Helm",
                unequipCount == 1 && unequipPayload == ((int)ItemSlot.Helm & 0xFF),
                $"count={unequipCount} payload={unequipPayload}");

            InventoryPanel.EmitUnequipRequest(ItemSlot.Ring, 1);
            Check("点第二枚戒指 ⇒ 载荷高位带 slotIndex（= (int)Ring | (1<<8)），可解回原槽/下标",
                unequipCount == 2
                && unequipPayload == (((int)ItemSlot.Ring & 0xFF) | (1 << 8))
                && (unequipPayload & 0xFF) == (int)ItemSlot.Ring && (unequipPayload >> 8) == 1,
                $"payload={unequipPayload}（解码 slot={(ItemSlot)(unequipPayload & 0xFF)} idx={unequipPayload >> 8}）");

            Check("UnequipRequest 打包与 AppEventRouting 解码同口径（逐槽 round-trip）",
                InventoryPanel.PackUnequip(ItemSlot.Weapon, 0) == (int)ItemSlot.Weapon
                && InventoryPanel.PackUnequip(ItemSlot.Amulet, 0) == (int)ItemSlot.Amulet
                && InventoryPanel.PackUnequip(ItemSlot.Ring, 1) == (int)ItemSlot.Ring + 256,
                $"{ItemSlot.Weapon}={InventoryPanel.PackUnequip(ItemSlot.Weapon, 0)} "
                + $"{ItemSlot.Ring}#1={InventoryPanel.PackUnequip(ItemSlot.Ring, 1)}");

            // ② 背包内拖放 → MoveInInventoryRequest（载荷 = fromAnchor | (toAnchor << 16)）
            InventoryPanel.EmitMoveInInventoryRequest(2, 5);
            Check("背包内拖放 ⇒ MoveInInventoryRequest 恰好 1 次，载荷 = 2 | (5<<16)，可解回 from/to",
                moveCount == 1 && movePayload == (2 | (5 << 16))
                && (movePayload & 0xFFFF) == 2 && (movePayload >> 16) == 5,
                $"payload={movePayload}（解码 from={movePayload & 0xFFFF} to={movePayload >> 16}）");

            // ③ 拖到面板外 → ItemDropRequest（原有兜底，顺带回归）
            InventoryPanel.EmitItemDropRequest(7);
            Check("拖到面板外 ⇒ ItemDropRequest 恰好 1 次，载荷 = 锚点 7",
                dropCount == 1 && dropPayload == 7, $"payload={dropPayload}");

            // ④ 本片改口径（**删除项的两层证据之一：源码层**）：对话面板**不再直接发**商店/任务请求，
            //    只发选项下标（`DialogOptionChosen`），动作由 `NpcModule.ChooseOption` 反解。
            //    ⇒ 这里断言的正是"那三条 Emit 已从面板消失"（另一层证据 = 上面 PanelSpec 的事件表）。
            {
                var dlgSrc = File.ReadAllText(Path.Combine(UiDir, "NpcDialogPanel.cs"));
                var gone = new[]
                {
                    "Events.ShopOpenRequest", "Events.QuestAcceptRequest", "Events.QuestTurnInRequest",
                    "EmitShopOpenRequest", "CanOpenShop", "_shopButton", "_acceptButton", "_turnInButton",
                    "_shopHint",
                };
                var still = "";
                foreach (var g in gone)
                    if (dlgSrc.Contains(g)) still += g + " ";
                Check("对话面板：自造的动作按钮/提示行与三条直发请求已全部删除（只留 DialogOptionChosen）",
                    still.Length == 0
                    && dlgSrc.Contains("Events.DialogOptionChosen")
                    && !dlgSrc.Contains("\"接受任务\"") && !dlgSrc.Contains("\"交付任务\"")
                    && !dlgSrc.Contains("\"结束对话\""),
                    still.Length == 0 ? "0 命中" : "仍存在：" + still);
            }

            Check("三条 Emit 路径各行其是（没有误发别的请求事件）", strayCount == 0,
                "PanelToggleRequest=" + strayCount);

            // ⑤ 拖放落点的锚点换算（纯函数）
            var inv = new InventoryChangedArgs();
            for (var i = 0; i < GameConst.InventoryCellCount; i++)
                inv.inventory.Add(new InventorySlot
                {
                    index = i, x = i % GameConst.InventoryCols, y = i / GameConst.InventoryCols,
                });
            inv.inventory[10].occupied = true; inv.inventory[10].anchorIndex = 10;
            inv.inventory[10].item = new ItemStack { name = "占用锚点" };
            inv.inventory[11].occupied = true; inv.inventory[11].anchorIndex = 10;   // 10 号物品的第 2 格

            Check("落点锚点换算：空格的落点 = 自身下标",
                InventoryPanel.DropAnchorOf(inv, 3) == 3, InventoryPanel.DropAnchorOf(inv, 3).ToString());
            Check("落点锚点换算：被占用格的落点 = 该物品锚点",
                InventoryPanel.DropAnchorOf(inv, 10) == 10 && InventoryPanel.DropAnchorOf(inv, 11) == 10,
                $"{InventoryPanel.DropAnchorOf(inv, 10)}/{InventoryPanel.DropAnchorOf(inv, 11)}");
            Check("落点锚点换算：越界 / 空数据 = -1",
                InventoryPanel.DropAnchorOf(inv, 999) == -1 && InventoryPanel.DropAnchorOf(null, 0) == -1, "-1");

            // ⑥ 小地图参数：HUD 缓存 → 打开时传给 MiniMapPanel（agent-13 §B-3）
            var map = new MinimapArgs { areaId = (int)AreaId.Town, width = 32, height = 32, seed = 20260311 };
            Check("HUD 对 MiniMapPanel 的打开参数 = 缓存的 MinimapArgs（非 null）",
                ReferenceEquals(HudPanel.PanelParam(nameof(MiniMapPanel), null, null, null, null, map), map),
                "PanelParam(MiniMapPanel, …, map) == map");

            Check("HUD 打开参数映射：其余面板各取自己的快照（互不串），未知面板 = null",
                ReferenceEquals(HudPanel.PanelParam(nameof(InventoryPanel), null, inv, null, null, map), inv)
                && HudPanel.PanelParam(nameof(CharacterPanel), new PlayerStatsDto(), inv, null, null, map) is PlayerStatsDto
                && HudPanel.PanelParam("UnknownPanel", null, inv, null, null, map) == null,
                "Inventory→inv / Character→stats / 未知→null");

            Check("MiniMapPanel.OnOpen 的参数校验：传非 null MinimapArgs 原样收下（不会降级成「无数据」）",
                ReferenceEquals(UiLog.Require<MinimapArgs>(map, nameof(MiniMapPanel)), map),
                "UiLog.Require<MinimapArgs>(map) == map");

            var hudSrc = File.ReadAllText(Path.Combine(UiDir, "HudPanel.cs"));
            Check("HUD 源码：订阅 Events.MapGenerated 并缓存到 _minimap",
                hudSrc.Contains("Events.MapGenerated") && hudSrc.Contains("_minimap = map;"),
                "见 HudPanel.OnMapGenerated");
            Check("HUD 源码：不再有 `Toggle<MiniMapPanel>(null)`",
                !hudSrc.Contains("Toggle<MiniMapPanel>(null)"), "已改为传入缓存的 MinimapArgs");

            var deathSrc = File.ReadAllText(Path.Combine(UiDir, "DeathPanel.cs"));
            Check("死亡面板源码：订阅 Events.Revived 并据此关闭（不再点一下就走）",
                deathSrc.Contains("Game.Event.On(Events.Revived, OnRevived)"), "见 DeathPanel.OnRevived");

            Game.Event = new ConsoleEventBus();      // 复位，不影响后续检查
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑫ agent-14 §C：菜单/创角屏的「原版亮度 + 原版底图」保真断言
        //    为什么这些可以离线断言：
        //      · 「背景太暗」的根因是 **Image.color 是颜色乘数**（贴图到位后仍乘着深色占位底），
        //        这条是**纯数据**（色调常量 + 源码顺序），不需要 GameObject 就能钉死；
        //      · 「不受 2D 光照」的判据是**shader 名判定**（纯函数 `UiArt.IsLitShader`）；
        //      · 「按钮用原版帧」的判据是**帧名/帧数/条带导入模式**——可直接对磁盘上的
        //        `.png.meta`（Multiple 的子 sprite 名 = `ResPaths.Frame` 的产物）逐条核对。
        //    仍然不能离线验证的：贴图像素在屏幕上的实际观感 ⇒ 由主 agent 进 Play 截图（本文件末尾已声明）。
        // ═════════════════════════════════════════════════════════════════════
        // ═════════════════════════════════════════════════════════════════════
        // 悬停事件 `D2.Input.HoverChanged` **往返**（★ hover-probe 片，消费侧）
        //
        //   为什么补它：状态矩阵 L3801/L3802 判「有订阅者（被消费）」，旧证据只引
        //   `Core/Events.cs`（事件的**声明处本身**）⇒ 只证得出"事件名存在"，证不出"真有人收到"
        //   （而且它正是被 freshness 比对的文件 ⇒ 恒红，见 report-hoverprobe.md）。
        //   本宿主编的是 `UI/**` ⇒ 断言**消费侧**：① 常量 == 矩阵实体 id；② 真实订阅回调
        //   真收到派发；③ 摘掉订阅方 ⇒ 收不到（退化校验，证明断言不是摆设）；
        //   ④ 真实消费点 `UI/EntityTooltip.OnHoverChanged(Diablo2.Def.HoverTarget)` 存在
        //      —— 反射查**编译产物**（不是 grep 文本）⇒ 改签名 / 删订阅必红。
        //
        //   ⚠️ 载荷类型必须写全限定名 `Diablo2.Def.HoverTarget`：本宿主 `using Diablo2.UI;`，
        //      而 `UI/HoverTarget.cs` 里另有一个同名 MonoBehaviour ⇒ 裸写 `HoverTarget` 是 CS0104。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckHoverRoundTrip()
        {
            Console.WriteLine("── 悬停事件 D2.Input.HoverChanged 往返（消费侧）──");

            Check("事件名常量 == 状态矩阵实体 id `D2.Input.HoverChanged`",
                Events.HoverTargetChanged == "D2.Input.HoverChanged", Events.HoverTargetChanged);

            var received = 0;
            Diablo2.Def.HoverTarget got = null;
            Action<Diablo2.Def.HoverTarget> onHover = h => { received++; got = h; };

            Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
            var payload = new Diablo2.Def.HoverTarget
            {
                hasTarget = true, cursor = CursorKind.Attack, id = 4242,
                name = "悬停自检", gridX = 7, gridY = 9,
            };
            Game.Event.Emit(Events.HoverTargetChanged, payload);

            Check("★ 往返：真实订阅回调收到 `D2.Input.HoverChanged`（1 次 + 载荷同一实例）",
                received == 1 && ReferenceEquals(got, payload),
                got == null ? "回调收到 null"
                            : $"回调 {received} 次 id={got.id} cursor={got.cursor} 同一实例={ReferenceEquals(got, payload)}");

            // 退化校验：摘掉订阅方 ⇒ 同一条派发不再进回调（否则上面那条是恒绿的摆设）
            Game.Event.Off<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, onHover);
            var afterOff = received;
            Game.Event.Emit(Events.HoverTargetChanged, payload);
            Check("★ 退化：摘掉订阅方（Off）⇒ 同一条派发不再进回调（断言不是摆设）",
                received == afterOff, $"回调 {afterOff} → {received}");

            var mi = typeof(EntityTooltip).GetMethod("OnHoverChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var ps = mi != null ? mi.GetParameters() : new ParameterInfo[0];
            Check("★ 消费点存在：`UI/EntityTooltip.OnHoverChanged(Diablo2.Def.HoverTarget)`",
                mi != null && ps.Length == 1 && ps[0].ParameterType == typeof(Diablo2.Def.HoverTarget),
                mi == null ? "找不到 OnHoverChanged 方法"
                           : $"{mi.Name}({(ps.Length == 1 ? ps[0].ParameterType.FullName : "参数个数=" + ps.Length)})");
            Console.WriteLine();
        }

        private static void CheckMenuArtBrightness()
        {
            Console.WriteLine("── ⑫ §C：原版屏亮度（Image.color 乘数）+ 原版按钮帧底图 ──");

            // ── 1. 原版亮度常量 ──
            Check("原版亮度常量 = 纯白（= 不对素材做任何压暗）",
                UiArt.ArtFullBright == Color.white, Describe(UiArt.ArtFullBright));
            Check("未选中色调比原版亮度暗、且与占位底色不同（不是「看不出来」的微调）",
                UiArt.ArtDim.r < 0.7f && UiArt.ArtDim != UiArt.ButtonBg && UiArt.ArtDim != Color.white,
                "ArtDim=" + Describe(UiArt.ArtDim) + " / ButtonBg=" + Describe(UiArt.ButtonBg));

            // ── 2. SetSprite：成功才套亮度，失败保留纯色占位 ──
            var uiArt = File.ReadAllText(Path.Combine(UiDir, "UiArt.cs"));
            var setSprite = Body(uiArt, "public static void SetSprite(");
            Check("SetSprite 源码取到（反射无法离线构造 Image ⇒ 这里按源码断言）",
                setSprite.Length > 0, $"{setSprite.Length} 字符");
            Check("SetSprite：**贴图加载成功**才把 color 设成记录色调（原版亮度）",
                setSprite.Contains("img.color = state.Tint;"), "见 UiArt.SetSprite 的加载回调");
            Check("SetSprite：**贴图缺失**分支不套亮度（保留占位底色，纯色占位仍可见）",
                setSprite.IndexOf("sp == null", StringComparison.Ordinal) >= 0
                && setSprite.IndexOf("sp == null", StringComparison.Ordinal)
                   < setSprite.IndexOf("img.color = state.Tint;", StringComparison.Ordinal),
                "`sp == null` 分支在套色之前 ⇒ 失败不改色");
            Check("SetSprite 里 Image 也过 EnsureUnlit（只有受光材质才动手）",
                setSprite.Contains("EnsureUnlit("), "见 UiArt.SetSprite");

            // ── 3. 不受 2D 光照：shader 名判定表（纯函数）──
            var litCases = new (string shader, bool lit)[]
            {
                ("Sprite-Lit-Default", true),
                ("Universal Render Pipeline/2D/Sprite-Lit-Default", true),
                ("Sprites/Lit", true),
                ("UI/Default", false),
                ("Sprite-Unlit-Default", false),
                ("Unlit/Color", false),
                ("Sprites/Default", false),
                ("", false),
                (null, false),
            };
            var litOk = true;
            var litDetail = new List<string>();
            foreach (var (shader, lit) in litCases)
            {
                var got = UiArt.IsLitShader(shader);
                litDetail.Add($"\"{shader}\"⇒{got}");
                if (got != lit) litOk = false;
            }
            Check("IsLitShader：受光 shader 判定正确（Unlit 优先于 Lit 子串 ⇒ Sprite-**Unlit**-Default 不被误判）",
                litOk, string.Join(" ", litDetail.ToArray()));

            Check("UiArt.Panel / FullPanel 造的每个 Image 都过 EnsureUnlit",
                Body(uiArt, "public static Image Panel(").Contains("EnsureUnlit(")
                && Body(uiArt, "public static Image FullPanel(").Contains("EnsureUnlit("),
                "见 UiArt.Panel / UiArt.FullPanel");
            Check("UiArt 造的按钮也过 EnsureUnlit（含补图后那一次）",
                Body(uiArt, "public static Image Button(").Contains("EnsureUnlit(")
                && Body(uiArt, "private static void ApplyButtonFrame(").Contains("EnsureUnlit("),
                "见 UiArt.Button / UiArt.ApplyButtonFrame");

            // UI 层不可能挂上受光材质（连 Shader.Find 都没有 ⇒ 只能用 Canvas 默认的 UI/Default）
            var shaderFind = 0;
            foreach (var f in Directory.GetFiles(UiDir, "*.cs", SearchOption.AllDirectories))
            {
                var t = File.ReadAllText(f);
                shaderFind += CountOf(t, "Shader.Find(");
                shaderFind += CountOf(t, "new Material(");
            }
            Check("UI/** 里 `Shader.Find(` / `new Material(` 命中 0（⇒ 只能走 Canvas 默认材质 UI/Default，unlit）",
                shaderFind == 0, shaderFind + " 命中");

            // ── 4. 背景贴图：4 个原版屏都接上了 ──
            var backdrops = new (string file, string pathConst, string note)[]
            {
                ("MainMenuPanel.cs", "ResPaths.MenuMainScreen", "主菜单 main_screen"),
                ("CharCreatePanel.cs", "ResPaths.MenuClassSelectScreen", "创角屏 class_select_screen"),
                ("CharSelectPanel.cs", "ResPaths.MenuClassSelectScreen", "选角屏 class_select_screen"),
                // ★ agent-a3：`LoadingPanel.cs` 从这条里**移出** —— 读条屏不再是"整屏贴图背景"，
                //   改成原版进图画面「黑底 + 居中 10 帧读条图」（见 `LoadingCheck.CheckLoadingScreen`）。
                //   原来用的 `load_screen`（EXPANSION SET 标题画）是**前端标题画**，不是进图读条图。
            };
            foreach (var (file, pathConst, note) in backdrops)
            {
                var src = File.ReadAllText(Path.Combine(UiDir, file));
                // agent-15 §A / 2026 主 agent 裁决：整屏贴图改由 `UiLayoutFlow.BackdropArt` 建
                // （原版 800×600 → **按高度 ×1.8 = 1440×1080、水平居中** + 左右留白纯色底；
                //  不再用"铺满"的 FullPanel：那会因为 800×600 与 1920×1080 纵横比不同而**拉长素材**）。
                // 两条路径都不改色（`UiArt.SetSprite` 到位后一律套原版亮度白）⇒ 断言接受二者之一。
                Check($"{file}：用原版贴图 {pathConst}（{note}）做整屏背景，贴图到位后套原版亮度（不压暗）",
                    (src.Contains("UiArt.Backdrop(") || src.Contains("UiLayoutFlow.BackdropArt("))
                    && src.Contains(pathConst),
                    "见 " + file + " 的 Build()");
            }

            // ── 5. 原版按钮帧底图 ──
            Check("按钮条带选择：≥200 宽用 button_wide、窄按钮用 button_medium",
                UiArt.ButtonStripFor(new Vector2(272f, 35f)) == ResPaths.MenuButtonWide
                && UiArt.ButtonStripFor(new Vector2(200f, 40f)) == ResPaths.MenuButtonWide
                && UiArt.ButtonStripFor(new Vector2(180f, 34f)) == ResPaths.MenuButtonMedium
                && UiArt.ButtonStripFor(new Vector2(40f, 30f)) == ResPaths.MenuButtonMedium,
                $"272⇒wide / 200⇒wide / 180⇒medium / 40⇒medium（阈值 {UiArt.WideButtonMinWidth}）");

            Check("条带帧数 = 3（常态/悬停/按下），与 ResPaths 的实测帧数常量一致",
                UiArt.ButtonStripFrames(ResPaths.MenuButtonWide) == 3
                && UiArt.ButtonStripFrames(ResPaths.MenuButtonWide) == ResPaths.FrameCountMenuButtonWide
                && UiArt.ButtonStripFrames(ResPaths.MenuButtonMedium) == 3
                && UiArt.ButtonStripFrames(ResPaths.MenuButtonMedium) == ResPaths.FrameCountMenuButtonMedium,
                $"wide={UiArt.ButtonStripFrames(ResPaths.MenuButtonWide)} medium={UiArt.ButtonStripFrames(ResPaths.MenuButtonMedium)}");

            Check("取帧名 = `{条带}_{帧号}`（帧号从 0 起）：button_wide_0/1/2",
                ResPaths.Frame(ResPaths.MenuButtonWide, 0) == ResPaths.D2UiMenu + "button_wide_0"
                && ResPaths.Frame(ResPaths.MenuButtonWide, 1) == ResPaths.D2UiMenu + "button_wide_1"
                && ResPaths.Frame(ResPaths.MenuButtonWide, 2) == ResPaths.D2UiMenu + "button_wide_2",
                ResPaths.Frame(ResPaths.MenuButtonWide, 0) + " … " + ResPaths.Frame(ResPaths.MenuButtonWide, 2));

            Check("按钮尺寸 = 条带实测帧尺寸（wide 272×35 / medium 128×35，与 AssetImporter.MultiFrameStrips 同值）",
                UiArt.MenuButtonSize == new Vector2(272f, 35f) && UiArt.MenuButtonMediumSize == new Vector2(128f, 35f),
                $"wide={UiArt.MenuButtonSize} medium={UiArt.MenuButtonMediumSize}");

            var importerSrc = File.ReadAllText(Path.Combine(ProjectRoot, "client", "Assets", "Editor", "AssetImporter.cs"));
            Check("AssetImporter 里两张条带的实测帧矩形与上面的按钮尺寸一致（272×35 / 128×35、3 帧）",
                importerSrc.Contains("new StripSpec(\"button_wide\", 816, 35")
                && importerSrc.Contains("new Rect(0, 0, 272, 35)")
                && importerSrc.Contains("new StripSpec(\"button_medium\", 384, 35")
                && importerSrc.Contains("new Rect(0, 0, 128, 35)"),
                "见 client/Assets/Editor/AssetImporter.cs 的 MultiFrameStrips");

            // 磁盘核对：条带是 Multiple（子 sprite 名 = 取帧名）；两张原版屏是 Single（主 sprite 可直接 Load）
            var menuDir = Path.Combine(ResourceRoot, "Clover", "D2", "UI", "Menu");
            var stripMetaOk = true;
            var stripDetail = new List<string>();
            foreach (var (file, frames) in new (string, int)[] { ("button_wide", 3), ("button_medium", 3) })
            {
                var png = Path.Combine(menuDir, file + ".png");
                var meta = Path.Combine(menuDir, file + ".png.meta");
                if (!File.Exists(png) || !File.Exists(meta)) { stripMetaOk = false; stripDetail.Add(file + "=缺文件"); continue; }
                var m = File.ReadAllText(meta);
                var multiple = m.Contains("spriteMode: 2");
                var named = true;
                for (var i = 0; i < frames; i++)
                    if (!m.Contains("name: " + file + "_" + i)) named = false;
                stripDetail.Add($"{file}: mode=Multiple({multiple}) 子帧名({named})");
                if (!multiple || !named) stripMetaOk = false;
            }
            Check("磁盘核对：button_wide/medium 都是 Multiple 且子 sprite 名 = 取帧名（否则 `Resources.Load<Sprite>(Frame(...))` 静默取空）",
                stripMetaOk, string.Join("；", stripDetail.ToArray()));

            var singleOk = true;
            var singleDetail = new List<string>();
            foreach (var file in new[] { "main_screen", "class_select_screen", "load_screen" })
            {
                var meta = Path.Combine(menuDir, file + ".png.meta");
                var m = File.Exists(meta) ? File.ReadAllText(meta) : string.Empty;
                var single = m.Contains("spriteMode: 1");
                singleDetail.Add($"{file}: Single({single})");
                if (!single) singleOk = false;
            }
            Check("磁盘核对：3 张原版屏都是 Single（`Resources.Load<Sprite>(主路径)` 能命中）",
                singleOk, string.Join("；", singleDetail.ToArray()));

            // 按钮走 uGUI SpriteSwap，帧序 0/1/2 = 常态/悬停/按下
            var applyFrame = Body(uiArt, "private static void ApplyButtonFrame(");
            Check("按钮底图走 uGUI SpriteSwap：常态帧 0、悬停帧 1、按下帧 2",
                applyFrame.Contains("Selectable.Transition.SpriteSwap")
                && applyFrame.Contains("highlightedSprite = set.Frames[1]")
                && applyFrame.Contains("pressedSprite = set.Frames[2]")
                && applyFrame.Contains("img.sprite = normal"),
                "见 UiArt.ApplyButtonFrame（normal=set.Frames[0]）");
            Check("UiArt.Button 接上原版帧（选条带 + 挂帧，未到位时先纯色块）",
                Body(uiArt, "public static Image Button(").Contains("RequestFrames(")
                && Body(uiArt, "public static Image Button(").Contains("ApplyButtonFrame("),
                "见 UiArt.Button");

            // ★ Play 实测（2026-09-17，`client/_dev/p_frames.cs`）：本工程这套导入设置下，
            //   子 sprite **按名加载失效**（`LoadAsset<Sprite>("…/button_wide_0") == null`），
            //   只有整条取（`Game.Res.LoadAll<Sprite>(条带)`）才能拿到 3 帧。离线这里钉死"兜底必须存在"，
            //   否则下次有人删掉兜底就会静默退回纯色块（Play 里才看得出来）。
            //   ⚠️ 片 34 起这条兜底走**引擎资源模块**（不再是 Unity 的 `Resources.LoadAll` ——
            //      那条路是验收表 E1 的例外，已收口；见 ②-c 的 0 命中判据）。
            var bulk = Body(uiArt, "private static bool TryBulkLoad(");
            Check("取帧有「整条 LoadAll」兜底（本工程逐帧按名加载实测取不到）+ 按 `{条带名}_{帧号}` 装帧",
                bulk.Contains("Game.Res.LoadAll<Sprite>(")
                && bulk.Contains("StartsWith(prefix")
                && bulk.Contains("int.TryParse(")
                && Body(uiArt, "private static void CompleteFrames(").Contains("TryBulkLoad(set)"),
                "见 UiArt.TryBulkLoad / CompleteFrames（同 UI/D2Text.cs 的字模兜底）");

            // ★ 片 8（B33）新增不变量：**先跑能取到图的那条路**。
            //   根因：原先 `RequestFrames` 无条件先逐帧 `Game.Res.LoadAsset<Sprite>(…)`，
            //   而那条路在本工程必然失败、引擎对每次失败都 `Log.Error("[Resource] 加载失败：…")`
            //   ⇒ 每建一次 HUD（= 每次进 Stage）白刷 2 条 Error（实测 `D2/UI/Panel/overlap_{0,1}`）。
            //   判据 = 在 `RequestFrames` 体内 `TryBulkLoad(set)` 的**位置必须早于** `Game.Res.LoadAsset<Sprite>(`。
            var reqFrames = Body(uiArt, "private static FrameSet RequestFrames(");
            var iBulk = reqFrames.IndexOf("TryBulkLoad(set)");
            var iEngine = reqFrames.IndexOf("Game.Res.LoadAsset<Sprite>(");
            Check("取帧顺序 = 先整条 LoadAll（能取到），逐帧按名（引擎资源模块）只在它失败时走",
                iBulk >= 0 && iEngine >= 0 && iBulk < iEngine,
                "见 UiArt.RequestFrames（B33：known-failing path must not run first）iBulk=" + iBulk + " iEngine=" + iEngine);

            Check("逐帧按名那条路仍原地保留（真取不到时照样走它 + 结算只判一次）",
                reqFrames.Contains("Game.Res.LoadAsset<Sprite>(") && reqFrames.Contains("ResPaths.Frame(")
                && reqFrames.Contains("OnFrameLoaded(set, index, sp)")
                && Body(uiArt, "private static void CompleteFrames(").Contains("set.Ready = true;"),
                "见 UiArt.RequestFrames / CompleteFrames（⛔ 不是把 Error 降级，是别预先跑已知取不到的路径）");

            // 异步竞态：面板在贴图回来之后才表达"选中"时不能被覆盖（**片 4 起：创角屏 5 个职业半身像**）
            // （agent-15 §A 曾用"底图色调"表达选中；片 4 换成**原版三态** NU1/NU2/NU3 换图 ⇒
            //   异步竞态的防法也变了：**进屏预热 15 张**（命中引擎缓存 ⇒ 换图同步生效），断言改成这一条。）
            var createSrc = File.ReadAllText(Path.Combine(UiDir, "CharCreatePanel.cs"));
            Check("创角屏半身像三态：进屏预热 5 槽 × 3 态 ⇒ 换图走缓存同步生效（不会因异步乱序停在错态）",
                createSrc.Contains("PreloadPortraits()")
                && createSrc.Contains("for (var st = ResPaths.Portrait.Idle; st <= ResPaths.Portrait.Front; st++)"),
                "见 CharCreatePanel.BuildSpots ④ / PreloadPortraits");

            var setTint = Body(uiArt, "public static void SetArtTint(");
            Check("SetArtTint：贴图未到 ⇒ 记在 Image 上待套用（不是丢掉）；已在 ⇒ 立即生效",
                setTint.Contains("StateOf(img).Tint = tint") && setTint.Contains("if (img.sprite != null) img.color = tint;"),
                "见 UiArt.SetArtTint");

            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑬ ★ agent-15 §A：**流程面板 1:1 复刻**的布局口径
        //
        // 为什么这些能离线断言：
        //   · 依据是**原版 prefab 的真实 RectTransform**（`Prefabs/Menu/MainMenu.prefab` /
        //     `ClassSelectMenu.prefab` / `WideButton.prefab` / `MediumButton.prefab`），
        //     换算口径唯一（原版 800×600 → 画布 1920×1080，**按高度** ×1080/600 = 1.8 + 水平居中）
        //     ⇒ `UiLayoutFlow.Table` 每条都能用纯算术核对「常量 == 原版值 × 1.8」，改错一个数就红；
        //   · 「文字走原版位图字体 / 中文回退默认字体」「色调 = 原版亮度」「面板只用常量表
        //     不散落魔数」都是**源码事实**，可直接 grep。
        // 不能离线验证的（留给 Play 截图并排比对）：字模/贴图在屏幕上的实际观感、点击热区手感。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckFlowMenuLayout()
        {
            Console.WriteLine("── ⑬ §A：流程面板 1:1（原版 800×600 → 1920×1080，逐元素 ×1.8 水平居中）──");

            // ★ 2026 主 agent 裁决（取代旧的宽度比 ×2.4）：原版是 **4:3（800×600）**，画布是 **16:9（1920×1080）**
            //   ⇒ **缩放系数 = 1080/600 = 1.8（按高度等比）**，水平居中，左右多出的空间交给相机/背景。
            //   ⛔ 旧口径 ×2.4（= 1920/800，宽度比）会让 600×2.4 = 1440 > 1080 ⇒ 纵向必然溢出
            //   （实测代价：HUD 控制面板底边出屏 51px、选角屏标题与底部按钮被顶出画布）。
            Check("缩放口径 = 1080/600 = **1.8**（按高度等比；原版 |y| ≤ 300 ⇒ 全部落在 ±540 内）",
                Math.Abs(UiLayoutFlow.Scale - 1.8f) < 1e-6f
                && Math.Abs(UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale - UiLayoutFlow.RefHeight) < 1e-3f
                && Math.Abs(UiLayoutFlow.RefHeight - 1080f) < 1e-3f,
                $"高：原版 {UiLayoutFlow.OrigHeight}×{UiLayoutFlow.Scale}={UiLayoutFlow.OrigHeight * UiLayoutFlow.Scale} = 画布高 {UiLayoutFlow.RefHeight}（正好铺满）");
            Check("水平居中：原版整屏 800×1.8 = 1440 ≤ 画布宽 1920（左右各留 240 交给相机/背景，**不横向拉伸**）",
                Math.Abs(UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale - 1440f) < 1e-3f
                && UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale <= UiLayoutFlow.RefWidth,
                $"宽：{UiLayoutFlow.OrigWidth}×{UiLayoutFlow.Scale}={UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale} ≤ {UiLayoutFlow.RefWidth}"
                + $"（左右各留 {(UiLayoutFlow.RefWidth - UiLayoutFlow.OrigWidth * UiLayoutFlow.Scale) * 0.5f:0.#}）");

            var table = UiLayoutFlow.Table;
            var origRows = 0;
            var addedRows = 0;
            var bad = new List<string>();
            foreach (var e in table)
            {
                if (e.FromOriginal) origRows++; else addedRows++;
                if (!e.PosOk)
                    bad.Add($"{e.Node} 位置 {e.Pos} ≠ 原版 {e.OrigPos}×1.8={UiLayoutFlow.Px(e.OrigPos)}");
                if (!e.SizeOk)
                    bad.Add($"{e.Node} 尺寸 {e.Size} ≠ 原版 {e.OrigSize}×1.8={UiLayoutFlow.Px(e.OrigSize)}");
            }

            Check($"对照表 {table.Length} 条（原版节点 {origRows} + 本项目新增 {addedRows}）**逐条**满足 常量 == 原版值 × 1.8",
                bad.Count == 0 && table.Length >= 30,
                bad.Count == 0 ? $"0 条不符（{origRows}+{addedRows}）" : string.Join("；", bad.ToArray()));

            // 验收要的那张对照表：面板 → 依据节点名 → 原版坐标 → 我们的坐标（逐行打印）
            Console.WriteLine("      ┌ 面板 → 依据节点 → 原版坐标/尺寸 → 本工程坐标/尺寸 ─────────────────────────");
            foreach (var e in table) Console.WriteLine("      │ " + e.Line);

            // 与 prefab 的原始数字逐点核对（**不许靠记忆**：这些数字全部来自 prefab 文本：
            //   `WideButton.prefab` 的 `m_SizeDelta: {x: 272, y: 35}` /
            //   `MediumButton.prefab` 的 `m_SizeDelta: {x: 128, y: 35}`；
            //   `MainMenu.prefab` 的 `Buttons` 容器 anchor(0.5,0.5) pos(0,-100) size(272,200) 且
            //   `VerticalLayoutGroup.m_ChildAlignment: 1`(UpperCenter)、`m_Spacing: 10`、
            //   `m_ChildControlHeight: 0` ⇒ 首行中心 = 容器顶边 0 − 35/2 = −17.5，行节奏 35+10 = 45。
            // ⚠️ 浮点：`272f*1.8f` 的结果与字面量 `489.6f` 不完全相等 ⇒ 用带容差的 `Near2` 比。）
            Check("主菜单/暂停按钮 = WideButton.prefab 272×35 → ×1.8 = 489.6×63",
                Near2(UiLayoutFlow.WideButtonOrig, new Vector2(272f, 35f))
                && Near2(UiLayoutFlow.WideButton, new Vector2(489.6f, 63f)),
                $"{UiLayoutFlow.WideButtonOrig} → {UiLayoutFlow.WideButton}");

            Check("选角/创角按钮 = MediumButton.prefab 128×35 → ×1.8 = 230.4×63",
                Near2(UiLayoutFlow.MediumButtonOrig, new Vector2(128f, 35f))
                && Near2(UiLayoutFlow.MediumButton, new Vector2(230.4f, 63f)),
                $"{UiLayoutFlow.MediumButtonOrig} → {UiLayoutFlow.MediumButton}");

            // ★ 片 4b 修：片 4 把 `Menu.SettingsPos` 改名成了 `CinematicsPos`（那一槽本来
            //   就是原版的 `CINEMATICS`），但本宿主没跟着改 ⇒ 引用不存在的成员、uicheck 编译不过。
            Check("主菜单 4 个原版槽位中心 = 原版 -17.5/-62.5/-107.5/-152.5 → -31.5/-112.5/-193.5/-274.5",
                Near2(UiLayoutFlow.Menu.SinglePos, new Vector2(0f, -31.5f))
                && Near2(UiLayoutFlow.Menu.MultiPos, new Vector2(0f, -112.5f))
                && Near2(UiLayoutFlow.Menu.CinematicsPos, new Vector2(0f, -193.5f))
                && Near2(UiLayoutFlow.Menu.ExitPos, new Vector2(0f, -274.5f)),
                "SinglePlayer/MultiPlayer/Cinematics/Exit 按钮槽");

            Check("行节奏 = 原版(按钮高 35 + spacing 10) × 1.8 = 81",
                Math.Abs(UiLayoutFlow.RowStep - UiLayoutFlow.RowStepOrig * UiLayoutFlow.Scale) < 1e-3f
                && Math.Abs(UiLayoutFlow.RowStep - 81f) < 1e-3f,
                $"{UiLayoutFlow.RowStepOrig} → {UiLayoutFlow.RowStep}");

            Check("创角 5 个热点 = 原版 7 个里本项目有的 5 个（Amazon/Necromancer/Barbarian/Paladin/Sorceress）",
                ClassSpotOk(), "见 UiLayoutFlow.ClassMenu.xxxPos");

            Check("选角列表带 = 原版元素边界之间（原版 x -344..370、y -212.5..110 ⇒ 714×322.5）",
                Math.Abs(UiLayoutFlow.Orig(UiLayoutFlow.Select.ListSize.x) - 714f) < 0.01f
                && Math.Abs(UiLayoutFlow.Orig(UiLayoutFlow.Select.ListSize.y) - 322.5f) < 0.01f
                && Math.Abs(UiLayoutFlow.Orig(UiLayoutFlow.Select.ListPos.y) - (-51.25f)) < 0.01f,
                $"{UiLayoutFlow.Select.ListPos} {UiLayoutFlow.Select.ListSize}");

            // ── 文字：英文/数字走原版位图字体，中文回退默认字体 ──
            var flowSrc = File.ReadAllText(Path.Combine(UiDir, "UiLayoutFlow.cs"));
            // ★ 片 4b 修：下面 1214 行那组断言引用了 `createSrc`，但它只在前一个方法
            //   （`CheckFlowClass` 附近的 1075 行）里声明过 ⇒ **本宿主整体编译不过**（CS0103），
            //   即 `run_all_hosts.ps1` 的 uicheck 一直 FAIL。这里补上同口径的声明。
            var createSrc = File.ReadAllText(Path.Combine(UiDir, "CharCreatePanel.cs"));
            // ★ 片 4b 修：这条断言里有**两句恒不成立的字面量**，把它钉死成 FAIL：
            //   ① `flowSrc.Contains("UiArt.Label(")` —— `UiLayoutFlow.cs` 里 `UiArt.Label(` 出现 **0 次**
            //      （那是片 3 之前的载体；片 3 起流程面板文字一律走 `D2Label.Create`）；
            //   ② `flowSrc.Contains("D2Text.IsLatinOnly(")` —— 出现 **0 次**（中英分派在 `D2Text.cs` 的
            //      `D2Label.Render` 里，不在本文件）。
            //   ⇒ 换成当前**真实存在**的四个字面量（都已在源码里核对过次数）。
            Check("1:1 文字层：流程面板文字走原版位图字模（D2Label + 档位 ×1.8；拉丁 AtlasPath / 中文 FontChi）",
                flowSrc.Contains("D2Label.Create(")
                && flowSrc.Contains("D2Text.AtlasPath(") && flowSrc.Contains("ResPaths.FontChi(")
                && flowSrc.Contains("ChineseFontSize"),
                "见 UiLayoutFlow.FlowLabel.Render");

            // ★ 片 4b 改口径：整体缩放从「恒定 ×1.8」改成「屏换算 ×1.8 × 文字级缩放」（按钮 = 原版字号 18/16），
            //   所以这行断言跟着改成新表达式 + 顺带断言换算系数本身（`ButtonFontScale`）。
            Check("位图字模按**原版 px** 排版后整体 ×1.8（与整屏原版贴图的放大倍率一致），按钮再乘原版字号换算",
                flowSrc.Contains("var s = Scale * _fontScale;")
                && flowSrc.Contains("_bitmap.Root.localScale = new Vector3(s, s, 1f)"),
                "见 UiLayoutFlow.FlowLabel.Render");

            // ★ 片 4b 新增断言：按钮字号换算 = 原版字号 18 ÷ 位图档 font16 名义字号 16（口径与出处见常量注释）。
            Check("按钮字号换算 ButtonFontScale = 原版 18 / font16 名义 16 = 1.125（原版 WideButton/MediumButton 的 m_FontSize:18）",
                Math.Abs(UiLayoutFlow.ButtonFontScale - 1.125f) < 1e-6f
                && flowSrc.Contains("ButtonFontScale = 18f / 16f"),
                $"ButtonFontScale={UiLayoutFlow.ButtonFontScale}（字模格子 18 → {18f * UiLayoutFlow.ButtonFontScale} 原版px）");

            Check("中文回退字号 = 档位 ×1.8（16→29 / 24→43 / 30→54 / 42→76）",
                UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font16) == 29
                && UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font24) == 43
                && UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font30) == 54
                && UiLayoutFlow.ChineseFontSize(D2Text.D2Font.Font42) == 76,
                "见 UiLayoutFlow.ChineseFontSize");

            // ── 按钮：原版帧底图 + 文字层接管 + 色调 ──
            Check("1:1 按钮：底图走 UiArt 的原版帧（含 SpriteSwap），并接管文字层（关掉 uGUI 内置字体那层）",
                flowSrc.Contains("UiArt.Button(") && flowSrc.Contains("UiArt.ButtonLabel(")
                && flowSrc.Contains("builtin.gameObject.SetActive(false)"),
                "见 UiLayoutFlow.FlowButton.Create");

            // ── 按钮字色：**判过程**（⛔ 不再判"常量有没有被动过"）───────────────────────────
            // ★ btn-label-fix（2026-09-23，主 agent 裁决 ①）：
            //   旧断言 = 「`UiLayoutFlow.ButtonText` 字面等于原版 prefab 的 #191919」——
            //   它判的是"**常量没被人改过**"，**不判"字读不读得出来"**。这是 SKILL §4.7 点名的
            //   脆弱判据形态：改一个数字就能让它变绿/变红，而那个数字与用户看到的东西没有必然关系
            //   （实测：它一直是绿的，而实机图里按钮字糊成一块黑 —— `.ai-tmp/screenshots/uifix4_z_before_btn1.png`）。
            //   新口径 = **对比度**：字色 vs **按钮底图实测亮度**（量法 `btn_plate_luma.py`，可原地复跑）
            //   ≥ **4.5:1**（WCAG 2.1 AA 正文；按钮字按原版 18px **Bold** 渲染，18px < 大号文本阈值
            //   18.66px ⇒ 取更严的正文档 4.5:1，⛔ 不取宽松的 3:1）。
            //   全项目只有两条按钮字色来源：① `UiLayoutFlow.ButtonText`（FlowButton 默认，7 个流程屏）
            //   ② `UiArt.ButtonText`（UiArt.Button/SquareButton/OrigButton：NPC 对话/商店/死亡屏）——两条都判。
            var btnLuma = ReadPlateLuma();
            var artSrc = File.ReadAllText(Path.Combine(UiDir, "UiArt.cs"));
            Check("全项目按钮字色①：FlowButton 默认字色（主菜单/暂停/设置/创角/选角/二次确认/传送点）vs 按钮底图 ≥ 4.5:1",
                btnLuma > 0.01f && ContrastRatioSrgb(UiLayoutFlow.ButtonText, btnLuma) >= 4.5f,
                $"字色 {Describe(UiLayoutFlow.ButtonText)} vs 底板实测 {btnLuma:0.###} ⇒ {ContrastRatioSrgb(UiLayoutFlow.ButtonText, btnLuma):0.00}:1（门槛 4.5:1）");
            Check("全项目按钮字色②：UiArt 三条按钮工厂（NPC 对话 / 商店 / 死亡屏）vs 按钮底图 ≥ 4.5:1",
                btnLuma > 0.01f && ContrastRatioSrgb(UiArt.ButtonText, btnLuma) >= 4.5f,
                $"字色 {Describe(UiArt.ButtonText)} vs 底板实测 {btnLuma:0.###} ⇒ {ContrastRatioSrgb(UiArt.ButtonText, btnLuma):0.00}:1（门槛 4.5:1）");
            Check("两条按钮字色**同源**（FlowButton 默认派生自 UiArt.ButtonText；⛔ 不再两条路径各写一个数）",
                UiLayoutFlow.ButtonText == UiArt.ButtonText
                && flowSrc.Contains("public static readonly Color ButtonText = UiArt.ButtonText;"),
                Describe(UiLayoutFlow.ButtonText));
            Check("禁用态字色低于 4.5:1 = **登记过的有意差异**（WCAG 2.1 §1.4.3：inactive UI component 不设对比度要求）",
                UiArt.ButtonTextDisabled != UiArt.ButtonText && artSrc.Contains("WCAG 2.1 §1.4.3"),
                $"禁用态 {Describe(UiArt.ButtonTextDisabled)}（登记在 UiArt.ButtonTextDisabled 的注释里）");

            // ── 逐屏列数：证明**每一屏**的按钮都走这两条字色路（不是只判两个常量就完事）──
            //   扫描口径（机械）：`UI/*.cs` 里凡出现按钮工厂调用 ⇒ 该屏 label 字色 = 该工厂的字色来源；
            //   显式传色（本工程只有 `WaypointPanel.DestLabelColor`）按实参算。
            System.Func<Color, string> ratioOf = c => ContrastRatioSrgb(c, btnLuma).ToString("0.00") + ":1";
            var scanRows = new List<string>();
            var scanBad = new List<string>();
            var panelFiles = Directory.GetFiles(UiDir, "*.cs", SearchOption.TopDirectoryOnly);
            foreach (var pf in panelFiles)
            {
                var name = Path.GetFileName(pf);
                if (name == "UiArt.cs" || name == "UiLayoutFlow.cs") continue;   // 定义处，不算"屏"
                var src = File.ReadAllText(pf);
                var nFlow = System.Text.RegularExpressions.Regex.Matches(src, @"FlowButton\.Create\(").Count;
                var nArt = System.Text.RegularExpressions.Regex.Matches(src, @"UiArt\.(Button|SquareButton|OrigButton)\(").Count;
                if (nFlow == 0 && nArt == 0) continue;
                if (nFlow > 0)
                {
                    var c = src.Contains("DestLabelColor") ? WaypointPanel.DestLabelColor : UiLayoutFlow.ButtonText;
                    var ok = ContrastRatioSrgb(c, btnLuma) >= 4.5f;
                    scanRows.Add(name + ": FlowButton×" + nFlow + "→" + ratioOf(c));
                    if (!ok) scanBad.Add(name + "/FlowButton");
                }
                if (nArt > 0)
                {
                    var ok = ContrastRatioSrgb(UiArt.ButtonText, btnLuma) >= 4.5f;
                    scanRows.Add(name + ": UiArt按钮×" + nArt + "→" + ratioOf(UiArt.ButtonText));
                    if (!ok) scanBad.Add(name + "/UiArtButton");
                }
            }
            Check($"按钮 label 对比度 ≥ 4.5:1 **逐屏**（扫到 {scanRows.Count} 个屏/路径；⛔ 扫描口径失效=永真，故同时要求行数下限）",
                scanRows.Count >= 8 && scanBad.Count == 0,
                scanRows.Count == 0
                    ? "扫描 0 行 —— 扫描口径已失效（这是「永真」形态，必须修）"
                    : string.Join(" ¦ ", scanRows.ToArray()));

            // ⛔ 不许在调用点自造按钮字色（粗筛：工厂实参窗口里出现 `new Color(` ⇒ 绕过了唯一真源）
            var literalHits = new List<string>();
            foreach (var pf in panelFiles)
            {
                var src = File.ReadAllText(pf);
                var m = System.Text.RegularExpressions.Regex.Matches(src,
                    @"(FlowButton\.Create\(|UiArt\.(Button|SquareButton|OrigButton)\()[^;]{0,240}?new Color\(");
                if (m.Count > 0) literalHits.Add(Path.GetFileName(pf) + "×" + m.Count);
            }
            Check("⛔ 按钮 label 字色不许在调用点自造（按钮工厂实参窗口内 `new Color(` = 0 处）",
                literalHits.Count == 0,
                literalHits.Count == 0 ? "0 处" : string.Join(", ", literalHits.ToArray()));

            // ── 判据自检（SKILL §8.3：改判据必须做**两次**自检）────────────────────────────
            //   ① 已知**好**样本（= 生产现值）必须 PASS；
            //   ② 已知**坏**样本（= 本次缺陷原值 #191919）必须 FAIL —— 证明这条判据**不是永真**。
            Check("(判据自检①) 已知好样本 = 现值按钮字色 ⇒ PASS",
                ContrastRatioSrgb(UiArt.ButtonText, btnLuma) >= 4.5f,
                $"{ratioOf(UiArt.ButtonText)} ≥ 4.5:1");
            Check("(判据自检②) 已知坏样本 = 旧值 #191919 ⇒ FAIL（判据非永真）",
                !(ContrastRatioSrgb(OldPrefabButtonText, btnLuma) >= 4.5f),
                $"{ratioOf(OldPrefabButtonText)} < 4.5:1（旧断言把它判绿 ⇒ 这就是换判据的原因）");

            Check("色调 = 原版亮度（白）：UiArt.ArtFullBright == Color.white（贴图加载成功后套的色）",
                UiArt.ArtFullBright == Color.white, Describe(UiArt.ArtFullBright));

            // ★ 片 4 改口径：职业屏的"选中态"不再用色调近似（`FlowButton.SetSelected` + `UiLayoutFlow.ArtDim`
            //   已删），改用**原版自己的三态** `NU1/NU2/NU3`。断言改成"旧机制确实没了 + 新机制在"。
            Check("职业屏选中态 = 原版三态（NU1/NU2/NU3），不再是色调近似：旧 `SetSelected`/`ArtDim` 已删干净",
                !flowSrc.Contains("public void SetSelected(") && !flowSrc.Contains("public static Color ArtDim")
                && flowSrc.Contains("PortraitState") && flowSrc.Contains("Spot.Of(")
                && createSrc.Contains("ResPaths.Portrait.Front") && createSrc.Contains("ResPaths.Portrait.Hover"),
                "见 UiLayoutFlow.ClassMenu.Spot + CharCreatePanel.Refresh/ShowPortrait");

            // ── 7 个流程面板都必须用常量表（不许散落魔数）──
            var flowPanels = new[]
            {
                "BootPanel.cs", "MainMenuPanel.cs", "SettingsPanel.cs", "CharSelectPanel.cs",
                "CharCreatePanel.cs", "LoadingPanel.cs", "PausePanel.cs",
            };
            var noConst = new List<string>();
            var magic = new List<string>();
            foreach (var f in flowPanels)
            {
                var src = File.ReadAllText(Path.Combine(UiDir, f));
                if (!src.Contains("UiLayoutFlow.")) noConst.Add(f);
                // 换算常量（旧 ×2.4 / 新 ×1.8）只许出现在 UiLayoutFlow.cs（换算是唯一出口）
                if (src.Contains("2.4f") || src.Contains("1.8f")) magic.Add(f);
            }

            Check("7 个流程面板都引用 UiLayoutFlow 常量表", noConst.Count == 0,
                noConst.Count == 0 ? string.Join(" / ", flowPanels) : string.Join(", ", noConst.ToArray()));
            Check("流程面板里 `2.4f` / `1.8f` 命中 0（换算只在 UiLayoutFlow 一处）", magic.Count == 0,
                magic.Count == 0 ? "0 命中" : string.Join(", ", magic.ToArray()));

            // ── 主菜单屏不得再出现"自画的 logo/页脚"（原版 LogoPlaceholder 是透明空 Image，
            //    main_screen 贴图里已烘字标 ⇒ 画了就是与原版不一致）──
            var mainSrc = File.ReadAllText(Path.Combine(UiDir, "MainMenuPanel.cs"));
            Check("主菜单不再自画 logo / 页脚 / 提示行（原版只有背景 + 4 个按钮）",
                !mainSrc.Contains("\"Logo\"") && !mainSrc.Contains("\"Footer\"") && !mainSrc.Contains("\"Tip\""),
                "见 MainMenuPanel.Build");

            // ── 布局对照表必须能进日志（验收要"面板 → 原版坐标 → 我们的坐标 → 依据节点名"）──
            Check("对照表有一次性日志出口（每个面板打一次，行格式 = 对照表本身）",
                flowSrc.Contains("public static void LogTable(") && flowSrc.Contains("Logged.Add(panel)"),
                "见 UiLayoutFlow.LogTable");

            // ── 屏适配：原版 4:3 整屏 vs 画布 16:9 的处置 ──
            //   改按高度 ×1.8 后：原版 600×1.8 = 1080 = 画布高 ⇒ **所有屏都不需要再压**
            //   （`FitClass` 从旧的 0.75 = 1080/1440 改回 1；旧 0.75 是为"宽度比 ×2.4 后 1440 高"服务的，
            //    那个口径会把 4:3 素材/坐标纵向撑出画布 —— 实测：选角屏标题与 NEW HERO/MAIN MENU 跑出画面）。
            // ★ 本片（w3 流程屏审计）：屏分组从 6 个变 7 个 —— 选角屏的**专属**元件从 `Class` 组
            //   分到新组 `CharSelect`（理由见 `UiLayoutFlow.Panel.CharSelect` 的注释：`Class` 组同时装着
            //   选角 + 创角两屏的元件，跨屏的"两两重叠"比较毫无意义）。
            Check("屏适配系数：**8 个流程屏/弹窗分组全部 = 1**（按高度 ×1.8 后原版整屏正好 1080 高，无需再压）",
                UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Menu) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Pause) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Settings) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Confirm) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Loading) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Boot) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Class) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.CharSelect) == 1f,
                $"Menu={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Menu)} Class={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Class)}"
                + $" CharSelect={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.CharSelect)}"
                + $" Confirm={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Confirm)}"
                + $" Boot={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Boot)}");

            var panels = new[]
            {
                UiLayoutFlow.Panel.Menu, UiLayoutFlow.Panel.Class, UiLayoutFlow.Panel.CharSelect,
                UiLayoutFlow.Panel.Pause, UiLayoutFlow.Panel.Confirm,
                UiLayoutFlow.Panel.Settings, UiLayoutFlow.Panel.Loading, UiLayoutFlow.Panel.Boot,
            };
            var outside = new List<string>();
            foreach (var p in panels)
            {
                var fit = UiLayoutFlow.FitOf(p);
                var b = UiLayoutFlow.BoundsOf(p);
                // 乘适配系数后必须落在 1920×1080 参考画布内（= 任何 16:9 分辨率下都看得见）
                if (b.xMin * fit < -UiLayoutFlow.RefHalf.x || b.xMax * fit > UiLayoutFlow.RefHalf.x
                    || b.yMin * fit < -UiLayoutFlow.RefHalf.y || b.yMax * fit > UiLayoutFlow.RefHalf.y)
                {
                    outside.Add($"{p}: 适配后 x[{b.xMin * fit:0.#},{b.xMax * fit:0.#}] y[{b.yMin * fit:0.#},{b.yMax * fit:0.#}]");
                }
                else
                {
                    Console.WriteLine($"      │ 屏 {p,-9} 适配 ×{fit:0.##} ⇒ 内容范围 x[{b.xMin * fit,7:0.#},{b.xMax * fit,7:0.#}] " +
                                      $"y[{b.yMin * fit,7:0.#},{b.yMax * fit,7:0.#}]（画布 ±{UiLayoutFlow.RefHalf.x:0}×±{UiLayoutFlow.RefHalf.y:0}）");
                }
            }
            Check($"{panels.Length} 个流程屏的元素**乘适配系数后**全部落在参考画布 1920×1080 内（无元素跑出画面）",
                outside.Count == 0, outside.Count == 0
                    ? $"{panels.Length}/{panels.Length} 屏在界内"
                    : string.Join("；", outside.ToArray()));

            // 整屏贴图口径（2026 主 agent 裁决）：原版 800×600 → **按高度 ×1.8 = 1440×1080、水平居中**，
            //   左右各留 240 由纯色底（`BackdropFill`）补 —— ⛔ 不许"铺满画布"（那会把 4:3 素材横向拉伸 1.33×）。
            Check("整屏贴图走 BackdropArt：原版整屏 ×1.8 = 1440×1080 居中 + 左右留白纯色底（**不横向拉伸**）",
                flowSrc.Contains("public static Image BackdropArt(")
                && flowSrc.Contains("OrigWidth * Scale, OrigHeight * Scale")
                && flowSrc.Contains("BackdropFill")
                && !flowSrc.Contains("return UiArt.Backdrop(parent, spritePath, fallback);"),
                "见 UiLayoutFlow.BackdropArt（1440×1080 居中 + BackdropFill；旧的「铺满画布 / 横向拉伸 1.33×」写法已删）");

            // ── 同屏元素**两两不重叠**（离线拦住"两根文本框压在一起"这类"界面乱七八糟"）──
            //   ★ 为什么加这一条：实测漏过一个真缺陷 —— 启动屏的「署名行」当初**没进这张表**
            //     ⇒ 画布断言只查"在不在画面内"，压字完全查不出来（它与版权行重叠 48px）。
            //   豁免（都是"原版/本项目有意为之"，不是压字）：
            //     · 度量行（尺寸/节奏行，不是真实元素）；· 零宽/零高行；· 透明热点（原版点击区，压在职业按钮下面）；
            //     · 容器行（角色列表容器 / 选项面板底板，本来就包住子元素）；
            //     · 原版 `ClassName` 行（本项目的名字输入框**故意贴在同一行**）。
            static bool SkipForOverlap(FlowLayoutEntry r)
            {
                if (r.Size.x <= 0f || r.Size.y <= 0f) return true;              // 零宽/零高的度量行
                var text = r.Node + "|" + r.Use;
                if (text.Contains("尺寸") || text.Contains("节奏")) return true;  // 度量行（不是真实元素）
                if (text.Contains("(热点)")) return true;                        // 透明热点（原版点击区）
                if (text.Contains("容器") || text.Contains("底板")) return true;   // 容器（本来就包住子元素）
                if (r.Node.Contains("/ClassName")) return true;                  // 原版 ClassName 行（名字输入框故意同排）
                // ★ 片 4b 补：职业半身像的**三态是互斥显示**（同一槽位同一时刻只显示 NU1/NU2/NU3 之一），
                //   所以三行的矩形互相交叠是**设计如此**、不是压字 —— 片 4 加这三态行时就在表里给它们
                //   标了「半身像」后缀并写明"被重叠检查按互斥显示豁免"，但**这一句豁免当时没写进来**
                //   ⇒ uicheck 一直报 24 处"重叠"（全是同一槽位的三态两两相交）。这里按原意补上。
                if (text.Contains("半身像")) return true;
                return false;
            }

            static Rect RectOf(FlowLayoutEntry r)
                => Rect(r.Pos.x, r.Pos.y, r.Size.x, r.Size.y);

            var overlaps = new List<string>();
            var checkedCount = 0;
            foreach (var p in panels)
            {
                var rows = new List<FlowLayoutEntry>();
                foreach (var e in table)
                    if (e.Panel == p && !SkipForOverlap(e)) rows.Add(e);

                for (var i = 0; i < rows.Count; i++)
                {
                    checkedCount++;
                    for (var j = i + 1; j < rows.Count; j++)
                        if (!Separated(RectOf(rows[i]), RectOf(rows[j])))
                            overlaps.Add($"{p}: {rows[i].Node} × {rows[j].Node}");
                }
            }
            Check($"{panels.Length} 个流程屏分组共 {checkedCount} 个元素**两两不重叠**（度量行/热点/容器/原版 ClassName 行已豁免）",
                overlaps.Count == 0, overlaps.Count == 0 ? "0 冲突" : string.Join("；", overlaps.ToArray()));

            // ── 启动屏「署名行」（规范 §1.6：`by clover-engine` 居底居中）必须在表里 ──
            FlowLayoutEntry byLine = null;
            for (var i = 0; i < table.Length; i++)
                if (table[i].Node.Contains("署名")) { byLine = table[i]; break; }
            Check("启动屏署名行在对照表内（`by clover-engine` 居底居中，与版权行/版本行不重叠）",
                byLine != null && !SkipForOverlap(byLine)
                && byLine.Pos.y < 0f && byLine.Size.x > 0f
                && !string.IsNullOrEmpty(byLine.Use) && byLine.Use.Contains("clover-engine"),
                byLine == null ? "表里找不到 署名行" : byLine.Line.Trim());

            // ═════════════════════════════════════════════════════════════════════
            // ★ 本轮新增：二次确认弹窗（`D2ConfirmPanel`）= 原版窗框 + 原版中等按钮
            //   缺陷（实机图 `.ai-tmp/screenshots/x_b3_delete_confirm.png`）：选角屏 / 暂停菜单的
            //   二次确认原先走引擎默认 uGUI 弹窗（深灰方块 + 纯蓝按钮，`UIWidgets.cs:628-649`），
            //   与同一屏的原版石雕按钮**两种风格**。
            //   本节能离线判的：素材在位 / IHDR / 矩形 == 原版×1.8 / 常量互推一致 / 源码真的换过去了；
            //   只剩"画面上两种风格是否已统一"要看图（留给主 agent 采联络图）。
            //   ⛔ 为什么不是"给引擎 `Confirm` 传参数换皮"：见 `UI/D2ConfirmPanel.cs` 文件头（签名无外观参数）。
            // ═════════════════════════════════════════════════════════════════════

            // ① 底板素材：原版 `MENU/boxpieces.DC6` 拼装的那块窗框（= 暂停菜单同一张，IHDR 288×180）
            var boxPng = PngSizeOf(Path.Combine(ResourceRoot, "Clover",
                ResPaths.PanelBoxFramePause.Replace('/', Path.DirectorySeparatorChar) + ".png"));
            Check("确认弹窗底板 = 原版窗框 `boxframe_pause` 在职且 IHDR == 288×180 原版px（= 暂停底板同值）",
                Near2(boxPng, new Vector2(288f, 180f)) && Near2(UiLayoutFlow.Confirm.BoxSizeOrig, boxPng),
                $"{ResPaths.PanelBoxFramePause}.png = {boxPng.x}×{boxPng.y}；常量 {UiLayoutFlow.Confirm.BoxSizeOrig}");

            // ② 按钮素材：原版中等按钮三态（128×35）—— `UiArt.ButtonSpritesFor` 对 128 宽走的正是这一支
            var btnBad = new List<string>();
            foreach (var p in new[] { ResPaths.BtnMedNormal, ResPaths.BtnMedPressed, ResPaths.BtnMedSel })
            {
                var s = PngSizeOf(Path.Combine(ResourceRoot, "Clover",
                    p.Replace('/', Path.DirectorySeparatorChar) + ".png"));
                if (!Near2(s, new Vector2(128f, 35f))) btnBad.Add($"{p} = {s.x}×{s.y}");
            }
            Check("确认弹窗按钮 = 原版中等按钮三态素材在职且 IHDR == 128×35（btn_med_normal / _pressed / _sel）",
                btnBad.Count == 0, btnBad.Count == 0 ? "3/3 = 128×35" : string.Join("；", btnBad.ToArray()));

            // ③ 矩形 == 原版 × 1.8（逐个写死期望值，⛔ 不引用常量自证）
            //   ⚠️ 不能写 `var cc = UiLayoutFlow.Confirm;`：`Confirm` 是嵌套**类型**，
            //      用 `var` 接类型名 = CS0119（本宿主首次就踩到）。
            Check("确认弹窗 5 个矩形 == 原版 × 1.8（底板 288×180 / 标题 (0,67.5) / 正文 (0,0) / 两钮 (±72,−67.5)）",
                Near2(UiLayoutFlow.Confirm.BoxSize, new Vector2(518.4f, 324f))
                && Near2(UiLayoutFlow.Confirm.TitlePos, new Vector2(0f, 121.5f))
                && Near2(UiLayoutFlow.Confirm.MessagePos, Vector2.zero)
                && Near2(UiLayoutFlow.Confirm.CancelPos, new Vector2(-129.6f, -121.5f))
                && Near2(UiLayoutFlow.Confirm.ConfirmPos, new Vector2(129.6f, -121.5f))
                && Near2(UiLayoutFlow.Confirm.TitleSize, new Vector2(489.6f, 54f))
                && Near2(UiLayoutFlow.Confirm.MessageSize, new Vector2(489.6f, 162f)),
                $"底板 {UiLayoutFlow.Confirm.BoxSize}；标题 {UiLayoutFlow.Confirm.TitlePos}；"
                + $"正文 {UiLayoutFlow.Confirm.MessagePos}；钮 {UiLayoutFlow.Confirm.CancelPos}/{UiLayoutFlow.Confirm.ConfirmPos}");

            // ④ 派生关系自证（⛔ 不许自己拍几何）：底板 = 暂停底板；行位 = 暂停按钮行相对底板的偏移；
            //    两钮横向偏移 = ±(原版宽按钮 272 − 原版中等按钮 128)/2 = ±72 原版px。
            var halfGap = (UiLayoutFlow.WideButtonOrig.x - UiLayoutFlow.MediumButtonOrig.x) * 0.5f;
            Check("确认弹窗几何**由暂停菜单那一套原版度量派生**（底板同值 / 行位 = Pause 行 − Pause 底板 / 钮距 ±72 原版px）",
                Near2(UiLayoutFlow.Confirm.BoxSizeOrig, UiLayoutFlow.Orig(UiLayoutFlow.Pause.BoxSize), 0.01f)
                && Near2(UiLayoutFlow.Confirm.TitlePos, UiLayoutFlow.Pause.ResumePos - UiLayoutFlow.Pause.BoxPos)
                && Near2(UiLayoutFlow.Confirm.CancelPos, UiLayoutFlow.Pause.ToMainPos - UiLayoutFlow.Pause.BoxPos
                    - new Vector2(halfGap * UiLayoutFlow.Scale, 0f))
                && Near2(UiLayoutFlow.Confirm.ConfirmPos, UiLayoutFlow.Pause.ToMainPos - UiLayoutFlow.Pause.BoxPos
                    + new Vector2(halfGap * UiLayoutFlow.Scale, 0f)),
                $"Pause 底板原版值 = {UiLayoutFlow.Orig(UiLayoutFlow.Pause.BoxSize)}；halfGap = {halfGap}");

            // ⑤ 源码侧：面板真的在加载原版窗框 + 原版中等按钮；两个调用点**不再**调引擎默认弹窗
            var confirmSrc = NoComments(File.ReadAllText(Path.Combine(UiDir, "D2ConfirmPanel.cs"), Encoding.UTF8));
            var pauseCode = NoComments(File.ReadAllText(Path.Combine(UiDir, "PausePanel.cs"), Encoding.UTF8));
            var selectCode = NoComments(File.ReadAllText(Path.Combine(UiDir, "CharSelectPanel.cs"), Encoding.UTF8));
            Check("二次确认弹窗：面板加载原版窗框 + 原版中等按钮，两个调用点都换成 `D2ConfirmPanel.Show`（0 处 `Game.UI.Confirm`）",
                confirmSrc.Contains("ResPaths.PanelBoxFramePause")
                && confirmSrc.Contains("UiLayoutFlow.MediumButtonOrig")
                && confirmSrc.Contains("UiLayoutFlow.FlowButton.Create(")
                && pauseCode.Contains("D2ConfirmPanel.Show(") && !pauseCode.Contains("Game.UI.Confirm")
                && selectCode.Contains("D2ConfirmPanel.Show(") && !selectCode.Contains("Game.UI.Confirm"),
                "PausePanel / CharSelectPanel 各 1 处 → D2ConfirmPanel（PanelBoxFramePause + MediumButtonOrig）");

            Console.WriteLine("      └────────────────────────────────────────────────────────────────────");
            Console.WriteLine();
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑰ ★ R1-C：创角屏两条修复的离线断言
        //   用户 2026-09-20 原话：「创建人物时候，点击人物动画变形，很诡异」+
        //                     「输入框输入…数字的时候会有奇怪的粘连」
        //
        // 为什么这两条**能离线判**（而不是"只能进 Play 看"）：
        //   ① 过渡几何 = `UiLayoutFlow.ClassMenu.Transition` 里的**纯数据**（生成器写入，出处 = 导出 PNG）
        //      ⇒ 逐帧与磁盘 PNG 的 IHDR 宽高核对，并断言「矩形宽高比 == 该帧原生宽高比」——
        //      **这就是"变形"的定义**（矩形比例 ≠ 素材比例 ⇒ 被拉伸）；
        //   ② 名字输入 = `CharCreatePanel.DisplayName` / `EditName` / `IsNameCharAllowed` 三个**纯函数**
        //      ⇒ 直接喂击键序列断言"文本顺序 / 插入点位置"，覆盖字母/数字/退格/方向键/上限；
        //   ③ "uGUI 那两条抛点不可达" = **源码事实**（可 grep）：本工程 `UiArt.Input` 关掉射线命中与
        //      键盘导航（永远成不了 EventSystem 选中项）、面板里 0 处 `caretPosition`；并读 uGUI 源码
        //      把「哪两行读 `Input.compositionString`」**逐行点名**出来（不是凭记忆说"会抛"）。
        // 仍需进 Play 的（`表现类`，留给下一批采联络图）：逐帧矩形在屏幕上的观感、点击半身像的动画手感、
        //   `|` 光标标记的实际显示。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckCharCreateR1C()
        {
            Console.WriteLine("── ⑰ ★ R1-C：创角屏「过渡逐帧矩形」+「名字输入由面板驱动」──");
            CheckTransitionFrameRects();
            CheckNameInputDriven();
            Console.WriteLine();
        }

        /// <summary>过渡**逐帧矩形**：尺寸 == 磁盘 PNG、比例 == 原生比例、两端与三态一致、全在画布内。</summary>
        private static void CheckTransitionFrameRects()
        {
            var frontDir = Path.Combine(ResourceRoot, "Clover", "D2", "UI", "FrontEnd");
            // 槽位 → 目录名 + 期望帧数（帧数出处 = 导出器输出，见 CharCreatePanel 旧注释里的
            //   `export_d2ui.py --only frontend` 统计；这里作为**独立第二判据**再核一遍磁盘）
            var live = new[]
            {
                (slot: 0, cls: "amazon", fw: 54, bw: 30),
                (slot: 2, cls: "barbarian", fw: 64, bw: 19),
            };
            var codes = UiLayoutFlow.ClassMenu.Transition.Codes;

            var frames = 0;
            var countBad = new List<string>();
            var sizeBad = new List<string>();
            var ratioBad = new List<string>();
            var outBad = new List<string>();

            foreach (var c in live)
            {
                foreach (var code in codes)
                {
                    var want = code == ResPaths.Portrait.TransitionFront ? c.fw : c.bw;
                    var n = UiLayoutFlow.ClassMenu.Transition.FrameCount(c.slot, code);
                    var onDisk = Directory.Exists(Path.Combine(frontDir, c.cls))
                        ? Directory.GetFiles(Path.Combine(frontDir, c.cls), code + "_*.png").Length : 0;
                    if (n != want || onDisk != want)
                        countBad.Add($"{c.cls}/{code}: 表={n} 磁盘={onDisk} 期望={want}");

                    for (var f = 0; f < n; f++)
                    {
                        var ps = UiLayoutFlow.ClassMenu.Transition.Of(c.slot, code, f);
                        var png = PngSizeOf(Path.Combine(frontDir, c.cls, code + "_" + f + ".png"));
                        frames++;
                        if (ps == null) { sizeBad.Add($"{c.cls}/{code}#{f} Transition.Of 返回 null"); continue; }

                        // ① 表的尺寸 == 磁盘 PNG 实测（逐个像素级相等，这是"表不漂移"的判据）
                        if (!Near2(ps.OrigSize, png, 0.001f))
                            sizeBad.Add($"{c.cls}/{code}#{f} 表 {ps.OrigSize.x:0}×{ps.OrigSize.y:0} ≠ PNG {png.x:0}×{png.y:0}");

                        // ② 画布尺寸 == 原版尺寸 × 1.8，且**矩形宽高比 == 该帧原生宽高比**（= 不变形，
                        //    这正是用户报的那条：215×228 的帧被塞进 118×198 的框）
                        if (!Near2(ps.Size, png * UiLayoutFlow.Scale, 0.01f))
                            ratioBad.Add($"{c.cls}/{code}#{f} 画布 {ps.Size.x:0.##}×{ps.Size.y:0.##} ≠ 原生×1.8 " +
                                         $"{png.x * UiLayoutFlow.Scale:0.##}×{png.y * UiLayoutFlow.Scale:0.##}");
                        var native = png.x / png.y;
                        var rect = ps.Size.x / ps.Size.y;
                        if (Math.Abs(native - rect) > 1e-4f)
                            ratioBad.Add($"{c.cls}/{code}#{f} 原生比 {native:0.0000} ≠ 矩形比 {rect:0.0000}");

                        // ③ 每一帧都必须落在 1920×1080 参考画布内（含放大后的边）
                        if (Math.Abs(ps.Pos.x) + ps.Size.x * 0.5f > UiLayoutFlow.RefHalf.x + 0.01f
                            || Math.Abs(ps.Pos.y) + ps.Size.y * 0.5f > UiLayoutFlow.RefHalf.y + 0.01f)
                            outBad.Add($"{c.cls}/{code}#{f} 出画布（{ps.Pos.x:0.#},{ps.Pos.y:0.#}）{ps.Size.x:0.#}×{ps.Size.y:0.#}");
                    }
                }
            }

            Check($"过渡序列帧数：表 == 磁盘 PNG == 导出器统计（{live.Length} 职业 × {codes.Length} 段）",
                countBad.Count == 0, countBad.Count == 0
                    ? $"amazon 54+30 / barbarian 64+19 = {54 + 30 + 64 + 19}"
                    : string.Join("；", countBad.ToArray()));

            Check($"过渡逐帧尺寸 == 磁盘 PNG 实测（共 {frames} 帧，0 例外）",
                sizeBad.Count == 0 && frames > 0, sizeBad.Count == 0
                    ? $"{frames} 帧全部相等" : string.Join("；", sizeBad.ToArray()));

            Check($"过渡逐帧：**矩形宽高比 == 该帧原生宽高比**（共 {frames} 帧，0 例外 —— 这就是\"变形\"的判据）",
                ratioBad.Count == 0, ratioBad.Count == 0
                    ? $"{frames} 帧比例全等（画布尺寸也 = 原生 ×{UiLayoutFlow.Scale}）"
                    : string.Join("；", ratioBad.ToArray()));

            Check($"过渡逐帧矩形全部落在 1920×1080 参考画布内（共 {frames} 帧）",
                outBad.Count == 0, outBad.Count == 0 ? "0 帧出画布" : string.Join("；", outBad.ToArray()));

            // ── 两端与三态矩形的衔接（"两头无缝"的判据）──
            //   fw：首帧 == `NU1`（背面待机）、末帧 == `NU3`（正面待机），**逐像素一致**（两端锚点就是它们）；
            //   bw：首帧 ≈ `NU3`、末帧 ≈ `NU1`，**≤4 原版px**（`bw_0` = 121×233 vs NU3 121×234、
            //       `bw_29` = 115×198 vs NU1 118×198 —— 原版这两段的端点帧与该态待机帧本身差 1~3px，
            //       见 `UiLayoutFlow.ClassMenu.Transition` 的类注释；位置差 ≤ 尺寸差的一半）。
            var seamBad = new List<string>();
            foreach (var c in live)
            {
                var idle = UiLayoutFlow.ClassMenu.Spot.Of(c.slot, ResPaths.Portrait.Idle);
                var front = UiLayoutFlow.ClassMenu.Spot.Of(c.slot, ResPaths.Portrait.Front);
                var fw0 = UiLayoutFlow.ClassMenu.Transition.Of(c.slot, ResPaths.Portrait.TransitionFront, 0);
                var fwN = UiLayoutFlow.ClassMenu.Transition.Of(c.slot, ResPaths.Portrait.TransitionFront, c.fw - 1);
                var bw0 = UiLayoutFlow.ClassMenu.Transition.Of(c.slot, ResPaths.Portrait.TransitionBack, 0);
                var bwN = UiLayoutFlow.ClassMenu.Transition.Of(c.slot, ResPaths.Portrait.TransitionBack, c.bw - 1);

                if (!Near2(fw0.OrigSize, idle.OrigSize, 0.001f) || !Near2(fw0.OrigPos, idle.OrigPos, 0.001f))
                    seamBad.Add($"{c.cls} fw 首帧 ≠ NU1 矩形");
                if (!Near2(fwN.OrigSize, front.OrigSize, 0.001f) || !Near2(fwN.OrigPos, front.OrigPos, 0.001f))
                    seamBad.Add($"{c.cls} fw 末帧 ≠ NU3 矩形");
                if (!Near2(bw0.OrigSize, front.OrigSize, 4f) || !Near2(bw0.OrigPos, front.OrigPos, 4f))
                    seamBad.Add($"{c.cls} bw 首帧与 NU3 差 >4 原版px");
                if (!Near2(bwN.OrigSize, idle.OrigSize, 4f) || !Near2(bwN.OrigPos, idle.OrigPos, 4f))
                    seamBad.Add($"{c.cls} bw 末帧与 NU1 差 >4 原版px");
            }
            Check("过渡两端与三态矩形衔接：fw 两端**逐像素一致**（首 NU1 / 末 NU3）；bw 两端 ≤4 原版px",
                seamBad.Count == 0, seamBad.Count == 0
                    ? "amazon/barbarian 两段都接得上（播完不会跳）" : string.Join("；", seamBad.ToArray()));

            // ── 未导出素材的槽位：必须**明确没有**过渡（面板据此退回"不播过渡、直接落终态"）──
            var others = new List<string>();
            foreach (var slot in new[] { 1, 3, 4 })
                foreach (var code in codes)
                    if (UiLayoutFlow.ClassMenu.Transition.FrameCount(slot, code) != 0
                        || UiLayoutFlow.ClassMenu.Transition.Has(slot))
                        others.Add($"{UiLayoutFlow.ClassMenu.Spot.NameOf(slot)}/{code}");
            Check("未导出素材的三个槽（Necromancer/Paladin/Sorceress）过渡帧数 = 0（不静默播错序列）",
                others.Count == 0, others.Count == 0 ? "0 命中" : string.Join("；", others.ToArray()));

            // ── 面板侧源码：逐帧矩形确实被用上了（且不许再出现"固定画框"/第二张帧数表）──
            var createSrc = File.ReadAllText(Path.Combine(UiDir, "CharCreatePanel.cs"));
            var showFrame = Body(createSrc, "private void ShowTransitionFrame(");
            Check("过渡逐帧同时同步 sizeDelta + anchoredPosition，几何取自 UiLayoutFlow.ClassMenu.Transition.Of(",
                showFrame.Contains("UiLayoutFlow.ClassMenu.Transition.Of(")
                && showFrame.Contains("sizeDelta = ps.Size")
                && showFrame.Contains("anchoredPosition = ps.Pos"),
                "见 CharCreatePanel.ShowTransitionFrame");
            Check("面板里**只有一处**过渡几何出处（旧的 `TransitionFrames` 帧数表已删，避免两张表漂移）",
                !createSrc.Contains("TransitionFrames")
                && Body(createSrc, "private void BeginTransition(").Contains("UiLayoutFlow.ClassMenu.Transition.FrameCount("),
                "见 CharCreatePanel.BeginTransition");
            Check("过渡帧不设 preserveAspect（矩形已按该帧原生比例给 ⇒ 再等比内缩只会缩小画面）",
                !showFrame.Contains("preserveAspect")
                && Body(File.ReadAllText(Path.Combine(UiDir, "UiArt.cs")), "public static Image Art(")
                    .Contains("SetSprite(img, spritePath);"),
                "见 CharCreatePanel.ShowTransitionFrame / UiArt.Art");
        }

        /// <summary>名字输入：显示串（含插入点光标）是纯函数 + 击键序列 round-trip + 两条抛点不可达。</summary>
        private static void CheckNameInputDriven()
        {
            // ── ① 显示串 = 缓冲 + 插入点光标标记（纯函数，逐例）──
            var mark = CharCreatePanel.CaretMark;
            Check("名字显示串 = 缓冲在**插入点**插一个光标标记（纯函数，逐例）",
                CharCreatePanel.DisplayName("Hero", 4) == "Hero" + mark
                && CharCreatePanel.DisplayName("Hero", 2) == "He" + mark + "ro"
                && CharCreatePanel.DisplayName("Hero", 0) == mark + "Hero"
                && CharCreatePanel.DisplayName("a", 99) == "a" + mark          // 越界钳到末尾
                && CharCreatePanel.DisplayName("a", -5) == mark + "a"           // 越界钳到串首
                && CharCreatePanel.DisplayName("", 0) == string.Empty          // 空 ⇒ 留空（占位提示按原口径显示）
                && CharCreatePanel.DisplayName(null, 3) == string.Empty,
                $"CaretMark=\"{mark}\"；Hero@2 → \"{CharCreatePanel.DisplayName("Hero", 2)}\"");

            // ── ② 击键序列 round-trip（字母 / 数字 / 退格 / Delete / 方向键 / 中间插入 / 上限）──
            var text = string.Empty;
            var caret = 0;
            var bad = new List<string>();
            void Key(string op, string arg = null)
            {
                text = CharCreatePanel.EditName(text, caret, op, arg, 15, out var nc);
                caret = nc;
            }
            void Expect(string what, string wantText, int wantCaret)
            {
                if (text != wantText || caret != wantCaret)
                    bad.Add($"{what}: 得「{text}」@{caret} 期望「{wantText}」@{wantCaret}");
            }

            Key("ins", "H"); Key("ins", "e"); Key("ins", "3");
            Expect("键入 H/e/3（含数字）", "He3", 3);
            Key("left");
            Expect("← 光标左移", "He3", 2);
            Key("ins", "X");
            Expect("在中间插入 X", "HeX3", 3);
            Key("back");
            Expect("退格删插入点左边", "He3", 2);
            Key("del");
            Expect("Delete 删插入点右边", "He", 2);
            Key("home"); Key("ins", "A");
            Expect("Home 后插入到串首", "AHe", 1);
            Key("end"); Key("ins", "9");
            Expect("End 后追加数字", "AHe9", 4);
            Check("击键序列 round-trip：字符顺序 + 插入点位置逐击一致（含被放行的数字）",
                bad.Count == 0, bad.Count == 0 ? $"最终「{text}」@{caret}" : string.Join("；", bad.ToArray()));

            Check("数字被放行（用户就是在输入数字时踩到粘连）",
                CharCreatePanel.IsNameCharAllowed('0') && CharCreatePanel.IsNameCharAllowed('9')
                && CharCreatePanel.IsNameCharAllowed('a') && !CharCreatePanel.IsNameCharAllowed('/')
                && !CharCreatePanel.IsNameCharAllowed('\n'),
                "见 CharCreatePanel.IsNameCharAllowed（存档槽位键 = 角色名 ⇒ 挡文件系统非法字符）");

            var l15 = string.Empty;
            var c15 = 0;
            for (var i = 0; i < 15; i++) l15 = CharCreatePanel.EditName(l15, c15, "ins", "x", 15, out c15);
            var l16 = CharCreatePanel.EditName(l15, c15, "ins", "x", 15, out c15);
            Check("长度上限 15：满 15 字后第 16 个字符不进入、插入点不动",
                l15.Length == 15 && l16 == l15 && c15 == 15, $"{l15.Length} → {l16.Length} @{c15}");

            // ── ③ 源码层：两条抛点不可达（去注释后再判，避免说明性注释误报）──
            var createSrc = File.ReadAllText(Path.Combine(UiDir, "CharCreatePanel.cs"));
            var code = StripCommentLines(createSrc);
            Check("面板源码（去注释）里 0 处输入框光标 setter —— 该 setter 在本工程配置下**必抛**（已彻底移除）",
                !code.Contains(".caretPosition"),
                "旧写法 `_nameInput." + "caretPosition = _nameCaret;` 已删；光标改由显示串承载");

            Check("OnClose 置 `_nameEditing = false`（面板关了不再吃全局键入）",
                Body(createSrc, "public override void OnClose()").Contains("_nameEditing = false;"),
                "见 CharCreatePanel.OnClose");

            Check("OnConfirm 读**缓冲**（真值）而不是输入框（输入框里现在是含光标标记的显示串）",
                Body(createSrc, "private void OnConfirm()").Contains("_nameBuffer.Trim()")
                && !Body(createSrc, "private void OnConfirm()").Contains("_nameInput.text"),
                "见 CharCreatePanel.OnConfirm");

            // ── ③b 反复开关屏：OnOpen 必须**重新装配**缓冲/插入点/可编辑标志并立刻刷显示（不许沿用上次的）──
            var open = Body(createSrc, "public override void OnOpen(");
            Check("重新打开面板：OnOpen 重建缓冲（`_nameBuffer = args…`）+ 插入点 = 长度 + `_nameEditing = true` + 立刻刷显示",
                open.Contains("_nameBuffer = args") && open.Contains("_nameCaret = _nameBuffer.Length;")
                && open.Contains("_nameEditing = true;") && open.Contains("PushName(false);"),
                "见 CharCreatePanel.OnOpen（配合 OnClose 置 false ⇒ 关屏不吃键、开屏即同步）");

            var push = Body(createSrc, "private void PushName(");
            var iInput = push.IndexOf("_nameInput.text = display", StringComparison.Ordinal);
            var iShow = push.IndexOf("textComponent.text = display", StringComparison.Ordinal);
            Check("PushName：① 同步输入框（m_Text = 显示串，占位判据 + 重绘回调都写回同一串）" +
                  "→ ② 最后兜写 `textComponent.text`（①抛了也不影响可见文本）",
                iInput >= 0 && iShow > iInput,
                $"iInput={iInput} < iShow={iShow}");

            // ── ④ 输入框退出交互：uGUI 的抛点只在"它是 EventSystem 选中项"时才可达 ──
            var uiArtSrc = File.ReadAllText(Path.Combine(UiDir, "UiArt.cs"));
            var inputBody = Body(uiArtSrc, "public static InputField Input(");
            Check("输入框退出交互：底图不吃射线 + 子 Text/占位不吃射线 + 无 targetGraphic + 关键盘导航" +
                  "（⇒ 永远成不了 EventSystem 选中项 ⇒ 抛点不可达）",
                inputBody.Contains("InputBg, false")
                && inputBody.Contains("text.raycastTarget = false;")
                && inputBody.Contains("ph.raycastTarget = false;")
                && inputBody.Contains("input.targetGraphic = null;")
                && inputBody.Contains("Navigation.Mode.None"),
                "见 UiArt.Input");

            var setSprite = Body(uiArtSrc, "public static void SetSprite(");
            var iGuard = setSprite.IndexOf("state.Request != request", StringComparison.Ordinal);
            var iApply = setSprite.IndexOf("img.sprite = sp;", StringComparison.Ordinal);
            Check("UiArt.SetSprite 有**请求守卫**：同一 Image 只有最新一次请求的回调会落地（过期回调丢弃）",
                setSprite.Contains("var request = ++state.Request;") && iGuard >= 0 && iApply >= 0 && iGuard < iApply,
                $"见 UiArt.SetSprite（iGuard={iGuard} < iApply={iApply}）");

            // ── ⑤ 抛点**逐行点名**（读 uGUI 源码，不靠记忆）：`activeInputHandler: 1` + 两处 compositionString 读 ──
            var projSettings = Path.Combine(ProjectRoot, "client", "ProjectSettings", "ProjectSettings.asset");
            Check("本工程 `ProjectSettings.asset` = `activeInputHandler: 1`（只用新 Input System —— 抛点的前提）",
                File.Exists(projSettings) && File.ReadAllText(projSettings).Contains("activeInputHandler: 1"),
                "client/ProjectSettings/ProjectSettings.asset");

            var uguiDir = Path.Combine(ProjectRoot, "client", "Library", "PackageCache");
            var uguiHit = Directory.Exists(uguiDir)
                ? Directory.GetDirectories(uguiDir, "com.unity.ugui@*") : Array.Empty<string>();
            if (uguiHit.Length == 0)
            {
                Console.WriteLine("[SKIP] uGUI 源码不在本机（Library/PackageCache 未生成）⇒ 跳过" +
                                  "「抛点逐行点名」这一条（本工程未打开编辑器时才会这样）");
            }
            else
            {
                var ifPath = Path.Combine(uguiHit[0], "Runtime", "UGUI", "UI", "Core", "InputField.cs");
                var ifLines = File.Exists(ifPath) ? File.ReadAllLines(ifPath) : Array.Empty<string>();
                var lineComposition = LineOf(ifLines, "input != null ? input.compositionString : Input.compositionString");
                var lineCaret = LineOf(ifLines, "public int caretPosition");
                var lineAnchor = LineOf(ifLines, "if (compositionString.Length != 0)");
                var lineUpdate = LineOf(ifLines, "gameObject == EventSystem.current.currentSelectedGameObject");
                Check("抛点 ①：`caretPosition` setter → `selectionAnchorPosition` setter → 读 `compositionString`" +
                      "（⇒ `UnityEngine.Input.compositionString` ⇒ InvalidOperationException）",
                    lineComposition > 0 && lineCaret > 0 && lineAnchor > 0,
                    $"{Path.GetFileName(uguiHit[0])}/…/InputField.cs:{lineCaret} setter → :{lineAnchor} 读 compositionString" +
                    $"，该属性 = :{lineComposition}");
                Check("抛点 ②：`text` setter → `SetText` → `UpdateLabel` 读同一处，但**只在该框是选中项时**" +
                      "（`gameObject == currentSelectedGameObject`）",
                    lineUpdate > 0, $"InputField.cs:{lineUpdate}（有短路 ⇒ 只要它不是选中项就不抛）");
            }

            // ── ⑥ 「只报一次」的 R1-C 日志必须真在代码里（下一批进 Play 按 tag 检索数值证据）──
            Check("R1-C 三条生效口径日志在位（过渡逐帧矩形 / 名字输入 / SetSprite 请求守卫）且 tag 已在 `Core/Log.cs` 白名单",
                CountOf(createSrc, "\"R1-C\"") >= 2 && CountOf(uiArtSrc, "\"R1-C\"") >= 1
                && File.ReadAllText(Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "Core", "Log.cs"))
                    .Contains("\"R1-C\","),
                $"CharCreatePanel {CountOf(createSrc, "\"R1-C\"")} 处 / UiArt {CountOf(uiArtSrc, "\"R1-C\"")} 处"
                + "（不登记 tag 会多一条「tag 不在白名单」的 Warn）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑱ ★ R1-F：默认名「Hero」是**整体单元**（用户报的"输入框粘连"残留的那一半）
        //
        // 为什么单独一节（而不是并进 ⑰）：⑰ 修的是"字符丢 / 光标不动"（uGUI 那两条抛点），
        //   本节修的是**默认名与用户输入共用一个字符串**这个语义缺陷 —— 判据 = `EditNameDefault`
        //   这个纯函数的击键序列（离线可逐击断言），与"抛点不可达 / 显示串由面板驱动"是两件事。
        //
        // 修复前的实测出处（逐行可点，Play 原始日志）：
        //   `.ai-tmp/screenshots/r1_evidence_r1.txt:325` `CC-OPEN … nameBuffer="Hero" nameCaret=4`
        //   `.ai-tmp/screenshots/r1_evidence_r1.txt:752`
        //     `NAME where=after-typing typed="Ama65x" buffer="HeroAma65x" digitsInBuffer=1 visible_input="HeroAma65x|" caret=10`
        //   → 截图 `.ai-tmp/screenshots/a08_name_digits.png` 上写着 `NAME: HeroAma65x|`。
        // =====================================================================
        private static void CheckNameDefaultReplace()
        {
            Console.WriteLine("── ⑱ ★ R1-F：创角名字输入「默认名 Hero 不再粘连」（首次键入**整体替换**）──");

            const string Def = "Hero";        // = client/Assets/Configs/config.json 的 game.default_player_name
            var mark = CharCreatePanel.CaretMark;

            // ── ① 默认态：开屏可见文本 == `Hero|`（既有口径，本片未改动）──
            Check("① 默认态：缓冲 == 默认名「Hero」、插入点 == 4、可见文本 == 「Hero" + mark + "」",
                CharCreatePanel.DisplayName(Def, Def.Length) == Def + mark,
                $"DisplayName(\"{Def}\",4)=\"{CharCreatePanel.DisplayName(Def, Def.Length)}\"");

            // ── ② 首次键入 'A' ⇒ 整个缓冲被替换（不含 Hero）──
            int caret;
            bool pending;
            var one = CharCreatePanel.EditNameDefault(Def, Def.Length, true, "ins", "A", 15, out caret, out pending);
            Check("② 首次键入 'A' ⇒ 缓冲 == 「A」（默认名**整体**被替换）、插入点 1、pending 落 false",
                one == "A" && caret == 1 && !pending, $"buffer=\"{one}\" caret={caret} pending={(pending ? 1 : 0)}");
            Check("②b 首次键入后的可见文本 == 「A" + mark + "」（**不含** Hero）",
                CharCreatePanel.DisplayName(one, caret) == "A" + mark
                && CharCreatePanel.DisplayName(one, caret).IndexOf(Def, StringComparison.Ordinal) < 0,
                $"visible=\"{CharCreatePanel.DisplayName(one, caret)}\"");

            // ── ③ 连续键入「Ama65x」（含数字）⇒ 完整键入串；**任何一击都不出现 Hero** ──
            const string Typed = "Ama65x";
            var buf = Def;
            var cur = Def.Length;
            var pend = true;
            var bad = new List<string>();
            for (var i = 0; i < Typed.Length; i++)
            {
                buf = CharCreatePanel.EditNameDefault(buf, cur, pend, "ins", Typed[i].ToString(), 15,
                    out cur, out pend);
                var want = Typed.Substring(0, i + 1);
                if (buf != want || cur != want.Length)
                    bad.Add($"第{i + 1}击：得「{buf}」@{cur} 期望「{want}」@{want.Length}");
                if (buf.IndexOf(Def, StringComparison.Ordinal) >= 0)
                    bad.Add($"第{i + 1}击：缓冲里出现了默认名 ⇒「{buf}」");
            }
            Check($"③ 连续键入「{Typed}」逐击到位（结果「{buf}」@{cur}），且**从未**出现「{Def}{Typed}」",
                bad.Count == 0 && buf == Typed,
                bad.Count == 0 ? $"buffer=\"{buf}\" caret={cur} 默认名出现次数=0" : string.Join("；", bad.ToArray()));
            Check("③b 最终缓冲里 0 个「" + Def + "」—— ⛔ 断言明确排除修复前的「" + Def + Typed + "」",
                buf.IndexOf(Def, StringComparison.Ordinal) < 0 && buf != Def + Typed,
                $"buffer=\"{buf}\"；修复前的值 = " + Def + Typed);

            // ── ④ 退格 / Delete / 方向键**不触发**首次替换，且不把默认名删成残缺 ──
            int c4;
            bool p4;
            var back = CharCreatePanel.EditNameDefault(Def, Def.Length, true, "back", null, 15, out c4, out p4);
            Check("④a 未首次键入时退格：默认名保持完整（「Hero」@4）—— ⛔ 不是「Her」，pending 仍 true",
                back == Def && c4 == Def.Length && p4 && back != "Her",
                $"buffer=\"{back}\" caret={c4} pending={(p4 ? 1 : 0)}（规则 = 预填默认名当\"空字段\"，退格不生效）");

            var del = CharCreatePanel.EditNameDefault(Def, Def.Length, true, "del", null, 15, out c4, out p4);
            Check("④b 未首次键入时 Delete：同样不动文本（插入点在末尾，右侧本无字符）",
                del == Def && c4 == Def.Length && p4, $"buffer=\"{del}\" caret={c4} pending={(p4 ? 1 : 0)}");

            var left = CharCreatePanel.EditNameDefault(Def, Def.Length, true, "left", null, 15, out c4, out p4);
            Check("④c 未首次键入时 ←：只移动插入点（Hero @3），文本不变、pending 仍 true",
                left == Def && c4 == Def.Length - 1 && p4, $"buffer=\"{left}\" caret={c4} pending={(p4 ? 1 : 0)}");

            var mid = CharCreatePanel.EditNameDefault(Def, 2, true, "ins", "A", 15, out c4, out p4);
            Check("④d 插入点被移到中间(2)后首次键入 ⇒ 仍是**整体替换**（「A」@1）—— ⛔ 不是「HeAro」",
                mid == "A" && c4 == 1 && !p4, $"buffer=\"{mid}\" caret={c4}");

            var after = CharCreatePanel.EditNameDefault("A", 1, false, "back", null, 15, out c4, out p4);
            Check("④e 首次键入之后（pending=false）：删除键恢复常规语义（「A」→「」@0）",
                after == string.Empty && c4 == 0 && !p4, $"buffer=\"{after}\" caret={c4}");

            var end = CharCreatePanel.EditNameDefault(Def, Def.Length, true, "end", null, 15, out c4, out p4);
            Check("④f Home/End/←→ 都**不清** pending（否则「先按一下方向键再打字」会退化成追加粘连）",
                end == Def && p4 && left == Def, $"end ⇒ buffer=\"{end}\" pending={(p4 ? 1 : 0)}");

            // ── ⑤ 源码层：0 处输入框光标 setter（该 setter 在本工程配置下必抛）+ 面板确实走了新纯函数 ──
            var createSrc = File.ReadAllText(Path.Combine(UiDir, "CharCreatePanel.cs"));
            var code = StripCommentLines(createSrc);
            Check("⑤ 面板源码（去注释）里 0 处输入框光标 setter —— ⛔ 本片不许恢复该写法",
                !code.Contains(".caretPosition"),
                "光标仍由显示串（DisplayName）承载；见 UiArt.Input 的抛点说明");

            Check("⑤b 面板实际走 `EditNameDefault`（⛔ 不许绕开它直连 EditName ⇒ 语义会退回粘连）" +
                  "，且 OnOpen 设置了 `_nameDefaultPending`",
                Body(createSrc, "private void ApplyNameEdit(").Contains("EditNameDefault(")
                && Body(createSrc, "public override void OnOpen(").Contains("_nameDefaultPending"),
                "见 CharCreatePanel.ApplyNameEdit / OnOpen");

            // ── ⑥ 「Hero」的出处链：值来自配置，不是面板写死、也不是本片新加 ──
            var cfg = File.ReadAllText(Path.Combine(ProjectRoot, "client", "Assets", "Configs", "config.json"));
            Check("⑥ 「Hero」来自 `config.json` 的 `game.default_player_name`（面板不写死默认名）",
                cfg.Contains("\"default_player_name\"") && cfg.Contains("\"Hero\""),
                "client/Assets/Configs/config.json → Core/ClientConfig.cs:DefaultPlayerName → AppFlow.BuildCharCreateArgs → Args.defaultName");

            Console.WriteLine();
        }

        /// <summary>某行首次出现 <paramref name="needle"/> 的行号（1 起；找不到 = 0）。</summary>
        private static int LineOf(string[] lines, string needle)
        {
            for (var i = 0; i < lines.Length; i++)
                if (lines[i].Contains(needle)) return i + 1;
            return 0;
        }

        /// <summary>
        /// 丢掉**整行注释**后的源码（`//` / `*` / `/*` 开头）——本宿主的既有做法（见方向键移动那条检查）：
        /// 说明性注释会**引用**被删掉的写法，扫进去就是假阳性，而会误报的检查比没有检查更糟。
        /// </summary>
        private static string StripCommentLines(string text)
        {
            var sb = new StringBuilder();
            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                var t = line.TrimStart();
                if (t.StartsWith("//", StringComparison.Ordinal) || t.StartsWith("*", StringComparison.Ordinal)
                    || t.StartsWith("/*", StringComparison.Ordinal)) continue;
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>读 PNG 的宽高（只解析签名 + IHDR；同 `P5Check.PngSize` 的判据，跨文件各留一份避免互改）。</summary>
        private static Vector2 PngSizeOf(string path)
        {
            if (!File.Exists(path)) return new Vector2(-1f, -1f);
            var b = File.ReadAllBytes(path);
            if (b.Length < 24) return new Vector2(-1f, -1f);
            var w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            var h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return new Vector2(w, h);
        }

        /// <summary>
        /// 创角热点是不是"原版 7 个里本项目有的那 5 个"（原版矩形中心 ×1.8）。
        /// <para>原版值出处 = `ClassSelectMenu.prefab` 每个热点的
        /// `m_AnchoredPosition` / `m_SizeDelta` / `m_Pivot`（非中心 pivot ⇒ 先折算成矩形中心，
        /// 见 `UiLayoutFlow.ClassMenu` 的注释）。</para>
        /// </summary>
        private static bool ClassSpotOk()
        {
            return UiLayoutFlow.ClassMenu.AmazonPos == new Vector2(-538.2f, -28.8f)      // 原版中心 (-299,-16)
                && UiLayoutFlow.ClassMenu.NecromancerPos == new Vector2(-178.2f, -19.8f) // 原版中心 (-99,-11)
                && UiLayoutFlow.ClassMenu.BarbarianPos == new Vector2(1.8f, 0f)          // 原版中心 (1,0)
                && UiLayoutFlow.ClassMenu.PaladinPos == new Vector2(209.7f, -14.4f)      // 原版中心 (116.5,-8)
                && UiLayoutFlow.ClassMenu.SorceressPos == new Vector2(399.15f, -39.6f)   // 原版中心 (221.75,-22)
                && UiLayoutFlow.ClassMenu.SpotSize == new Vector2(162f, 360f)            // 90×200 ×1.8
                && UiLayoutFlow.ClassMenu.SpotSizeWide == new Vector2(198f, 360f)        // 110×200 ×1.8
                && UiLayoutFlow.ClassMenu.SpotSizeNarrow == new Vector2(153f, 360f);     // 85×200 ×1.8
        }

        // ═════════════════════════════════════════════════════════════════════
        // 工具
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 从源码里截出某个方法的完整方法体（从签名开始，到下一个 8 空格缩进的 `}` 为止）。
        /// 用途：面板实例在离线进程造不出来（`new GameObject()` 需要原生运行时），
        /// 与「贴图/色调/过渡」相关的行为只能按源码断言（本宿主原有做法，见 ④/⑪）。
        /// </summary>
        private static string Body(string text, string signature)
        {
            var start = text.IndexOf(signature, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            var end = text.IndexOf("\n        }", start, StringComparison.Ordinal);
            if (end < 0) end = text.Length - 1;
            return text.Substring(start, end - start + 1);
        }

        private static string Describe(Color c) => "(r,g,b,a)=(" +
            $"{c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})";

        private static int CountOf(string text, string needle)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0) break;
                count++;
                index += needle.Length;
            }
            return count;
        }

        private static string Describe(Dictionary<ItemQuality, Color> colors)
        {
            var parts = new List<string>();
            foreach (var kv in colors)
                parts.Add(kv.Key + "=" + Hex(kv.Value));
            return string.Join("  ", parts.ToArray());
        }

        /// <summary>按中心点 + 尺寸造矩形（y 向上；与 uGUI 的中心坐标一致）。</summary>
        private static Rect Rect(float cx, float cy, float w, float h)
            => new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h);

        /// <summary>两个矩形是否分离（不重叠）。</summary>
        private static bool Separated(Rect a, Rect b)
            => a.xMax <= b.xMin || b.xMax <= a.xMin || a.yMax <= b.yMin || b.yMax <= a.yMin;

        /// <summary>
        /// 两个 Vector2 是否相等（带容差）。
        /// 为什么不能用 `==`：`272f * 2.4f` 的 float 结果是 652.80005，而字面量 `652.8f` 是 652.79999
        /// ⇒ 用 `==` 判「常量 == 原版值 × 系数」会**恒假**（与数值对不对无关）。换到 ×1.8 同理：
        /// `489.6f` 这类字面量也不会与 `272f * 1.8f` 逐位相等，故一律用本方法带容差比。
        /// </summary>
        private static bool Near2(Vector2 a, Vector2 b, float eps = 1e-2f)
            => Math.Abs(a.x - b.x) <= eps && Math.Abs(a.y - b.y) <= eps;

        /// <summary>两个颜色的通道差之和（`Color` 没有 `magnitude` 成员，故自己算）。</summary>
        private static float ChannelDistance(Color a, Color b)
            => Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b);

        private static string Hex(Color c)
            => $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";

        private static string First(CaptureLogger logger, string level)
        {
            for (var i = 0; i < logger.All.Count; i++)
                if (logger.All[i].Level == level)
                    return logger.All[i].ToString();
            return "(无)";
        }

        // ═════════════════════════════════════════════════════════════════════
        // ⑬ ★ R1-E（「人物对话时 UI 逻辑乱七八糟」）的 7 条修复：能离线判的那部分
        //
        // 为什么这些可以离线判：
        //   · S1 / S3 / S4 / S5 / S7 的判据是**源码语义 + 引擎语义**（谁的层是什么、谁补发哪个事件、
        //     谁订阅了谁、空参数走哪一支）⇒ 读源码文本 + 读**引擎 `UI.cs`** 文本逐条锚定；
        //   · S6 / S5 还有**几何**判据（选项行 vs 雕槽、标题/提示行 vs 页签带与格区）⇒ 纯矩形运算；
        //   · 「文案放不放得下」⇒ 用**磁盘上的原版字模表**（`font16_chi_map.txt` + `font_chi_s2t.txt`）
        //     按 `UI/D2Text` 的口径（advance 求和 × 字号/格高）**实测**，不是"看着差不多"。
        // 判不了的（**留给下一批进 Play**，已在回报里列明）：真点击穿透 / 真层级观感 / 实机日志。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckR1EDialogUi()
        {
            Console.WriteLine("── ⑬ R1-E：对话 / 商店 UI 逻辑（S1~S7）──");

            var dlgSrc = File.ReadAllText(Path.Combine(UiDir, "NpcDialogPanel.cs"), Encoding.UTF8);
            var shopSrc = File.ReadAllText(Path.Combine(UiDir, "ShopPanel.cs"), Encoding.UTF8);
            var hudSrc = File.ReadAllText(Path.Combine(UiDir, "HudPanel.cs"), Encoding.UTF8);
            var dialogSrc = File.ReadAllText(Path.Combine(
                ProjectRoot, "client", "Assets", "Scripts", "Module", "Npc", "NpcDialog.cs"), Encoding.UTF8);
            var npcSrc = File.ReadAllText(Path.Combine(
                ProjectRoot, "client", "Assets", "Scripts", "Module", "Npc", "NpcModule.cs"), Encoding.UTF8);
            var inputSrc = File.ReadAllText(Path.Combine(
                ProjectRoot, "client", "Assets", "Scripts", "Module", "Input", "InputReader.cs"), Encoding.UTF8);

            // ── S1：Popup 互斥 ⇒ 只有"对话条让位"这一解（引擎语义逐行锚定）────────────
            var engineUi = Path.Combine(ProjectRoot, "..", "clover-client-unity-engine",
                "Runtime", "Presentation", "UI.cs");
            if (!File.Exists(engineUi))
            {
                _skip++;
                Console.WriteLine($"[SKIP] S1 的引擎语义锚点（{engineUi} 不在本机 ⇒ 跳过）");
            }
            else
            {
                var ui = File.ReadAllText(engineUi, Encoding.UTF8);
                var mutex = MethodBody(ui, "private void CloseMutexPanels()");
                var open = MethodBody(ui, "public void Open<T>(object param = null)");
                Check("S1：引擎 `CloseMutexPanels` 只关 `Layer == UILayer.Popup` 的面板（互斥语义）",
                    mutex.Contains("kv.Value.Layer == UILayer.Popup"),
                    "见 clover-client-unity-engine/Runtime/Presentation/UI.cs");
                Check("S1：引擎 `Open<T>` 只在 `panel.Layer == UILayer.Popup` 时调互斥 + 显示遮罩",
                    open.Contains("panel.Layer == UILayer.Popup")
                    && open.Contains("CloseMutexPanels()") && open.Contains("ShowMask()"),
                    "见 UI.cs Open<T>");
                var closeMethod = MethodBody(ui, "public void Close(string panelName)");
                Check("S1：引擎 `Close(string)` **不发**任何事件、直接销毁面板根（所以模块状态只能由面板补发）",
                    closeMethod.Contains("Object.Destroy(root)") && !closeMethod.Contains("Emit("),
                    "见 UI.cs Close(string)");
            }
            Check("S1：对话条层 = Normal、商店层 = Popup（＝共存时唯一可行组合）",
                dlgSrc.Contains("override UILayer Layer => UILayer.Normal")
                && shopSrc.Contains("override UILayer Layer => UILayer.Popup"),
                "对话 Normal（遮罩之下，被压暗不可点=模态）/ 商店 Popup（遮罩之上，点得动）");

            // ── S2：点 UI 不产生地面意图（两个入口都过纯判定；判定源不是裸 UnityEngine.Input）──
            //   ⚠️ 判据必须锚在**代码**上：本文件的头注释里**引用**了被禁的写法
            //      （"直连 `UnityEngine.Input` / `Keyboard.current` 会静默失效"）⇒ 不剥注释就是假阳性。
            var inputCode = NoComments(inputSrc);
            Check("S2：两个取点入口都过 UI 命中判定（单击 + 按住）",
                CountOf(inputCode, "UiEatsIntent(_down,") == 1 && CountOf(inputCode, "UiEatsIntent(_held,") == 1,
                $"UiEatsIntent 出现 {CountOf(inputCode, "UiEatsIntent(")} 处（含定义 1 处）");
            Check("S2：UI 命中判定是纯函数 `UiEatsIntent(pressed, pointerOverUi)` ⇒ 宿主可逐行断言两种情形",
                inputCode.Contains("public static bool UiEatsIntent(bool pressed, bool pointerOverUi) => pressed && pointerOverUi"),
                "见 InputReader.UiEatsIntent");
            Check("S2：判定源 = UnityEngine.EventSystems（反射）+ 可注入替身，⛔ 代码里没有裸 UnityEngine.Input / Keyboard.current",
                inputCode.Contains("PointerOverUi = UiPointerProbe.PointerOverUi")
                && inputCode.Contains("EventSystem")
                && !inputCode.Contains("UnityEngine.Input.")
                && !inputCode.Contains("Keyboard.current") && !inputCode.Contains("Mouse.current"),
                "见 InputReader.PointerOverUi / UiPointerProbe（反射解析 `UnityEngine.EventSystems.EventSystem`）");

            // ── S3：面板关闭即让模块侧状态归零（补发 DialogClose；先退订再发，无回环）──────
            var onClose = MethodBody(dlgSrc, "public override void OnClose()");
            Check("S3：对话面板 `OnClose` 补发 `Events.DialogClose`（引擎 Close 不补发 ⇒ 只能由这里补）",
                onClose.Contains("Game.Event?.Emit(Events.DialogClose)"),
                "见 NpcDialogPanel.OnClose");
            Check("S3：先 `Unsubscribe()` 再 Emit（顺序 ⇒ 不会收到自己发的那条，无回环）",
                onClose.IndexOf("Unsubscribe();", StringComparison.Ordinal) >= 0
                && onClose.IndexOf("Unsubscribe();", StringComparison.Ordinal)
                   < onClose.IndexOf("Emit(Events.DialogClose)", StringComparison.Ordinal),
                "见 NpcDialogPanel.OnClose 的语句顺序");
            Check("S3：模块侧 `_currentNpcId` 由 `DialogClose` 清（面板/模块两条通道都幂等）",
                npcSrc.Contains("private void OnDialogClose()") && npcSrc.Contains("_currentNpcId = (int)NpcId.None;"),
                "见 NpcModule.OnDialogClose");
            Check("S3：`QuestChanged` 的唯一开面板门槛 = `_currentNpcId != None`（= 面板确实开着）",
                MethodBody(npcSrc, "private void OnQuestChanged(QuestStateDto quest)")
                    .Contains("if (_currentNpcId == (int)NpcId.None) return;"),
                "见 NpcModule.OnQuestChanged");
            var resolveBody = MethodBody(npcSrc, "private int ResolveNpcId(int fromArgs)");
            Check("S3：交易请求不再用阿卡拉兜底（npcId 缺失 ⇒ 返回 None + Warn ⇒ 交易失败可定位）",
                resolveBody.Contains("return (int)NpcId.None;")
                && !resolveBody.Contains("return (int)NpcId.Akara;"),
                "见 NpcModule.ResolveNpcId");

            // ── S4：一次 DialogOpen 只 Rebuild 一次（唯一权威路径）────────────────────
            Check("S4：打开路径唯一 = `HudPanel.OnDialogOpen` → `Game.UI.Open<NpcDialogPanel>(args)`",
                hudSrc.Contains("Game.UI.Open<NpcDialogPanel>(args)")
                && hudSrc.Contains("Game.Event.On<NpcDialogArgs>(Events.DialogOpen, OnDialogOpen)"),
                "见 HudPanel.OnDialogOpen");
            Check("S4：对话面板**不再**订阅 `Events.DialogOpen`（重复订阅已删除）",
                !dlgSrc.Contains("Game.Event.On<NpcDialogArgs>(Events.DialogOpen")
                && !dlgSrc.Contains("private void OnDialogOpen("),
                "见 NpcDialogPanel.Subscribe");
            Check("S4：面板仍保留 `Events.DialogClose` 订阅（模块要求关面板时的唯一回程）",
                dlgSrc.Contains("Game.Event.On(Events.DialogClose, OnDialogCloseEvent)"),
                "见 NpcDialogPanel.Subscribe");

            // ── S6：菜单项挪出雕花方槽（几何）────────────────────────────────────────
            // ★ U3：雕槽也随底图一起按 `DialogArtScale` 放大 ⇒ 矩形尺寸 = 34 × K × scale
            //   （`OptionY/OptionX/Cx/Cy` 已内含 scale，两侧同倍 ⇒ 不相交的判定口径不变）。
            var k = UiLayoutGame.K * NpcDialogPanel.DialogArtScale;
            var slotSize = new Vector2(NpcDialogPanel.SlotCellSize * k, NpcDialogPanel.SlotCellSize * k);
            var slotL = RectAt(NpcDialogPanel.Cx((NpcDialogPanel.SlotCellLeftX0 + NpcDialogPanel.SlotCellLeftX1) * 0.5f),
                NpcDialogPanel.Cy((NpcDialogPanel.SlotCellY0 + NpcDialogPanel.SlotCellY1) * 0.5f), slotSize);
            var slotR = RectAt(NpcDialogPanel.Cx((NpcDialogPanel.SlotCellRightX0 + NpcDialogPanel.SlotCellRightX1) * 0.5f),
                NpcDialogPanel.Cy((NpcDialogPanel.SlotCellY0 + NpcDialogPanel.SlotCellY1) * 0.5f), slotSize);
            var optBad = "";
            var opts = new Rect[NpcDialogPanel.MaxOptions];
            for (var i = 0; i < opts.Length; i++)
                opts[i] = RectAt(NpcDialogPanel.OptionX, NpcDialogPanel.OptionY(i), NpcDialogPanel.OptionSize);
            for (var i = 0; i < opts.Length; i++)
            {
                if (opts[i].Overlaps(slotL) || opts[i].Overlaps(slotR)) optBad += i + ":压雕槽 ";
                if (i > 0 && opts[i - 1].Overlaps(opts[i])) optBad += i + ":相互重叠 ";
            }
            Check("S6：菜单项（最多 3 个）与两个 34×34 雕花方槽**二维不相交**、彼此不重叠",
                optBad.Length == 0,
                optBad.Length == 0
                    ? $"选项列 x={opts[0].xMin:0.#}..{opts[0].xMax:0.#} / 雕槽左 {slotL.xMin:0.#}..{slotL.xMax:0.#} 右 {slotR.xMin:0.#}..{slotR.xMax:0.#}"
                    : optBad);

            var vx0 = NpcDialogPanel.Cx(2f);
            var vx1 = NpcDialogPanel.Cx(207f);
            var vyTop = NpcDialogPanel.Cy(2f);
            var vyBottom = NpcDialogPanel.Cy(155f);
            var outBad = "";
            for (var i = 0; i < opts.Length; i++)
            {
                if (opts[i].xMin < vx0 - 0.01f || opts[i].xMax > vx1 + 0.01f
                    || opts[i].yMax > vyTop + 0.01f || opts[i].yMin < vyBottom - 0.01f)
                    outBad += i + ":出石框 ";
            }
            Check("S6：菜单项仍全部落在石框**可见区**（原版 x 2..207 / y 2..155）内、不越底沿",
                outBad.Length == 0,
                outBad.Length == 0
                    ? "可见区 x[" + vx0.ToString("0.#") + "," + vx1.ToString("0.#") + "] y["
                      + vyBottom.ToString("0.#") + "," + vyTop.ToString("0.#") + "] 行心原版y="
                      + NpcDialogPanel.OptionOrigY(0).ToString("0.0") + " / "
                      + NpcDialogPanel.OptionOrigY(1).ToString("0.0") + " / "
                      + NpcDialogPanel.OptionOrigY(2).ToString("0.0")
                    : outBad);
            Check("S6：选项行宽 = 66 原版px（两雕槽之间净宽 72 内缩 3）、列心 = 原版 x 103",
                Math.Abs(NpcDialogPanel.OptionW - 66f) < 0.01f
                && Math.Abs(NpcDialogPanel.OptionX - NpcDialogPanel.Cx(103f)) < 0.01f,
                "W=" + NpcDialogPanel.OptionW + " OptionX=" + NpcDialogPanel.OptionX.ToString("0.#"));

            // ── S7：漏参数不复用旧状态 ─────────────────────────────────────────────
            //   ⚠️ 同样必须剥注释：本文件头的 S7 说明里**引用**了改前那行写法。
            var dlgCode = NoComments(dlgSrc);
            Check("S7：`OnOpen` 的 null 参数分支置空态（`_dialog = null`）、且**没有**复用旧值那行",
                dlgCode.Contains("_dialog = null;") && !dlgCode.Contains("if (dialog != null) _dialog = dialog;"),
                "见 NpcDialogPanel.OnOpen");

            // ── S5：商店标题/提示行建出 + 落位 + 文案口径 ──────────────────────────────
            Check("S5：`_title` / `_hint` 真被创建（`BuildTitleLines()` 由 `Build()` 调用）",
                shopSrc.Contains("BuildTitleLines();")
                && CountOf(shopSrc, "D2Label.Create(transform, \"ShopTitle\"") == 1
                && CountOf(shopSrc, "D2Label.Create(transform, \"ShopHint\"") == 1,
                "见 ShopPanel.BuildTitleLines");
            var applyTitle = MethodBody(shopSrc, "private void ApplyTitle()");
            Check("S5：`ApplyTitle()` 同时写标题行与提示行（⛔ 不再是恒空转）",
                applyTitle.Contains("_title.SetText(name)") && applyTitle.Contains("_hint.SetText(page)"),
                "见 ShopPanel.ApplyTitle");
            var st = RectAt(UiLayoutGame.ShopTitlePos.x, UiLayoutGame.ShopTitlePos.y, UiLayoutGame.ShopInfoLineSize);
            var sh = RectAt(UiLayoutGame.ShopHintPos.x, UiLayoutGame.ShopHintPos.y, UiLayoutGame.ShopInfoLineSize);
            Check("S5：标题行 / 提示行落在「页签带底沿 ~ 格区顶沿」的空带里、两行不重叠、不越面板",
                !st.Overlaps(sh)
                && st.yMax <= UiLayoutGame.ShopTabBottomY + 0.01f
                && sh.yMin >= UiLayoutGame.ShopGridTopY - 0.01f
                && sh.yMin >= -ShopPanel.PanelSize.y * 0.5f
                && st.yMax <= ShopPanel.PanelSize.y * 0.5f
                && st.xMin >= -ShopPanel.PanelSize.x * 0.5f && st.xMax <= ShopPanel.PanelSize.x * 0.5f,
                "标题 " + st.yMin.ToString("0.#") + ".." + st.yMax.ToString("0.#")
                + " / 提示 " + sh.yMin.ToString("0.#") + ".." + sh.yMax.ToString("0.#")
                + " / 空带 " + UiLayoutGame.ShopGridTopY.ToString("0.#") + ".."
                + UiLayoutGame.ShopTabBottomY.ToString("0.#"));

            // ── S5/S6 的文案必须**放得下**（用磁盘上的原版字模表实测，不靠眼估）────────────
            var cm = LoadChiMetrics();
            Check("S5/S6：原版 font16 中文字模表已在磁盘上解析（13800 码位 + 简繁映射）",
                cm.Advance.Count >= 13800 && cm.S2T.Count >= 3000,
                "advance=" + cm.Advance.Count + " s2t=" + cm.S2T.Count + " cell=" + cm.Cell);

            var optionTexts = new[] { "離開", "重要消息", "交易", "交易/修理" };
            var wideBad = "";
            foreach (var t in optionTexts)
            {
                string miss;
                var w = ChiWidth(cm, t, 20, out miss);
                if (w < 0f) { wideBad += "「" + t + "」缺字(" + miss + ") "; continue; }
                if (w > NpcDialogPanel.OptionSize.x + 0.01f)
                    wideBad += "「" + t + "」" + w.ToString("0.#") + ">" + NpcDialogPanel.OptionSize.x.ToString("0.#") + " ";
            }
            var widest = ChiWidth(cm, "交易/修理", 20, out _);
            Check("S6：对话菜单项文案在字模下都放得下（最宽的一条 ≤ 选项行宽，不换行/不溢出）",
                wideBad.Length == 0,
                wideBad.Length == 0
                    ? "「交易/修理」" + widest.ToString("0.#") + " ≤ 行宽 " + NpcDialogPanel.OptionSize.x.ToString("0.#") + " 画布px"
                    : wideBad);

            var shopTexts = new[] { "阿卡拉", "卡夏", "恰西", "基得", "瓦瑞夫", "买入", "卖出" };
            var shopBad = "";
            foreach (var t in shopTexts)
            {
                // `_title` / `_hint` 与 `_gold` 同一套口径：D2Label 不传 fontSize ⇒ 缩放 1（原生档）
                string miss;
                var w = ChiWidth(cm, t, cm.Cell, out miss);
                if (w < 0f) { shopBad += "「" + t + "」缺字(" + miss + ") "; continue; }
                if (w > UiLayoutGame.ShopInfoLineSize.x + 0.01f)
                    shopBad += "「" + t + "」" + w.ToString("0.#") + ">" + UiLayoutGame.ShopInfoLineSize.x.ToString("0.#") + " ";
            }
            Check("S5：商店标题/提示文案在字模下都放得下、且**无缺字**（缺字按原版行为不画）",
                shopBad.Length == 0,
                shopBad.Length == 0
                    ? "最长「阿卡拉」" + ChiWidth(cm, "阿卡拉", cm.Cell, out _).ToString("0.#")
                      + " ≤ 行宽 " + UiLayoutGame.ShopInfoLineSize.x.ToString("0.#") + " 画布px"
                    : shopBad);

            // ── 7 条修复各有「只报一次」的 R1-E 日志（实机对账用）────────────────────
            var all = dlgSrc + shopSrc + inputSrc + npcSrc;
            var noLog = "";
            for (var s = 1; s <= 7; s++)
                if (!all.Contains("[R1-E] S" + s + " ")) noLog += "S" + s + " ";
            Check("R1-E：7 条修复点各有「只报一次」的 `[R1-E] S<n>` Info 日志（写清生效口径）",
                noLog.Length == 0, noLog.Length == 0 ? "S1~S7 全在" : "缺：" + noLog);

            // ── 回归：不许把"本来已经对的三条"改回去（上一轮已修好）──────────────────
            //   判据 = `NpcDialog.cs` 的**字符串字面量**（⛔ 不扫注释：文档注释里满是 markdown 的 `**`）
            // 先剥注释再取字面量：`NpcDialog.cs` 的文档注释里**引用了**"接受任务 / 交付任务 / 结束对话"
            // 这三个自造词（作为禁令说明），直接扫原文会把注释里的引号当字面量 ⇒ 假阳性。
            var dlgLits = Literals(NoComments(dialogSrc));
            var badLit = "";
            foreach (var lit in dlgLits)
            {
                if (lit.Contains("接受任务") || lit.Contains("交付任务") || lit.Contains("结束对话")
                    || lit.Contains("**")) badLit += "「" + lit + "」 ";
            }
            var optionConsts =
                dialogSrc.Contains("OptionClose = \"離開\"")
                && dialogSrc.Contains("OptionQuestNews = \"重要消息\"")
                && dialogSrc.Contains("OptionShopBlacksmith = \"交易/修理\"")
                && dialogSrc.Contains("OptionShopGoods = \"交易\"");
            Check("回归：原版串口径仍在（`NpcDialog.cs` 的字面量里没有自造按钮字样、没有字面 `**`；4 条选项串在位）",
                badLit.Length == 0 && optionConsts && dlgSrc.Contains("Events.DialogOptionChosen"),
                (badLit.Length == 0 ? $"字面量 {dlgLits.Count} 条全部干净" : badLit)
                + (optionConsts ? "；4 条选项串（離開/重要消息/交易·修理/交易）在位" : "；**选项串被改过**"));

            Console.WriteLine();
        }

        /// <summary>
        /// 取某个声明（方法 / 属性）之后**配对花括号**包起来的那一段（含声明行）。
        /// 为什么不用"截到下一个空行"或"截到下一个 8 空格缩进的 `}`"（那是本文件既有的 `Body`）：
        /// 前者的方法体里本来就有空行（注释块之间）⇒ 会截短 ⇒ 假 FAIL。本方法按**花括号配对**切，
        /// 对"方法体内还有 lambda / 嵌套块"的写法也稳。
        /// </summary>
        private static string MethodBody(string src, string marker)
        {
            var i = src.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return string.Empty;
            var open = src.IndexOf('{', i);
            if (open < 0) return string.Empty;

            var depth = 0;
            for (var j = open; j < src.Length; j++)
            {
                if (src[j] == '{') depth++;
                else if (src[j] == '}')
                {
                    depth--;
                    if (depth == 0) return src.Substring(i, j - i + 1);
                }
            }
            return src.Substring(i);
        }

        /// <summary>
        /// 去掉注释（`//…` 与 `/*…*/`，字符串里的不算）后的源码。
        /// 为什么必须有它：这些源码的**文档注释里会引用被禁的写法**（"⛔ 一律走 `Game.Input`，
        /// 直连 `UnityEngine.Input` 会静默失效"、"改前是 `if (dialog != null) _dialog = dialog;`"）
        /// ⇒ 拿整份文本做 `!Contains(...)` 就是**假阳性**，会误报成"没修"。
        /// </summary>
        private static string NoComments(string src)
        {
            var sb = new StringBuilder(src.Length);
            for (var i = 0; i < src.Length; i++)
            {
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '/')
                {
                    while (i < src.Length && src[i] != '\n') i++;
                    sb.Append('\n');
                    continue;
                }
                if (src[i] == '/' && i + 1 < src.Length && src[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < src.Length && !(src[i] == '*' && src[i + 1] == '/')) i++;
                    i++;
                    continue;
                }
                if (src[i] == '"')
                {
                    sb.Append(src[i]);
                    i++;
                    while (i < src.Length && src[i] != '"')
                    {
                        if (src[i] == '\\' && i + 1 < src.Length) { sb.Append(src[i]); i++; }
                        if (i < src.Length) { sb.Append(src[i]); i++; }
                    }
                    if (i < src.Length) sb.Append(src[i]);
                    continue;
                }
                sb.Append(src[i]);
            }
            return sb.ToString();
        }

        /// <summary>源码里的字符串字面量（含转义序列原样保留；用于"这句画面文案是不是自造的"判定）。</summary>
        private static List<string> Literals(string src)
        {
            var res = new List<string>();
            foreach (Match m in Regex.Matches(src, "\"([^\"\\\\]|\\\\.)*\""))
                res.Add(m.Value.Trim('"'));
            return res;
        }

        /// <summary>以**中心**与尺寸建矩形（本项目所有面板常量都是中心语义）。</summary>
        private static Rect RectAt(float cx, float cy, Vector2 size)
            => new Rect(cx - size.x * 0.5f, cy - size.y * 0.5f, size.x, size.y);

        /// <summary>原版中文字模度量（font16）：码位 → 步进（px）+ 简繁映射 + 格高。</summary>
        // ★ w3（游戏内 UI 审计）：由 `private` 放宽到 `internal` —— 供 `W3GameCheck` 复用同一份
        //   字模度量（**单源**：不让第二个宿主各自解析一遍 font16_chi_map.txt）。
        internal sealed class ChiMetrics
        {
            public readonly Dictionary<int, int> Advance = new Dictionary<int, int>();
            public readonly Dictionary<int, int> S2T = new Dictionary<int, int>();
            public int Cell = 13;
        }

        /// <summary>
        /// 解析磁盘上的原版中文字模表（`Resources/Clover/D2/Fonts/font16_chi_map.txt` +
        /// `font_chi_s2t.txt`）—— 口径与 `UI/D2Text` 运行期一致（advance = 表里的 `width`）。
        /// </summary>
        internal static ChiMetrics LoadChiMetrics()
        {
            var m = new ChiMetrics();
            var fontDir = Path.Combine(ResourceRoot, "Clover", "D2", "Fonts");

            var map = Path.Combine(fontDir, "font16_chi_map.txt");
            if (File.Exists(map))
            {
                foreach (var line in File.ReadAllLines(map, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    if (line.StartsWith("CELL", StringComparison.Ordinal))
                    {
                        var cp = line.Split(' ');
                        if (cp.Length >= 2 && int.TryParse(cp[1], out var cell)) m.Cell = cell;
                        continue;
                    }
                    if (line.StartsWith("COLS", StringComparison.Ordinal)
                        || line.StartsWith("COUNT", StringComparison.Ordinal)) continue;

                    var f = line.Split(' ');
                    if (f.Length < 5) continue;
                    if (int.TryParse(f[0], out var code) && int.TryParse(f[2], out var adv))
                        m.Advance[code] = adv;
                }
            }

            var s2t = Path.Combine(fontDir, "font_chi_s2t.txt");
            if (File.Exists(s2t))
            {
                foreach (var line in File.ReadAllLines(s2t, Encoding.UTF8))
                {
                    if (line.Length == 0 || line[0] == '#') continue;
                    var f = line.Split(' ', '\t');
                    if (f.Length < 2) continue;
                    if (int.TryParse(f[0], out var from) && int.TryParse(f[1], out var to))
                        m.S2T[from] = to;
                }
            }
            return m;
        }

        /// <summary>
        /// 某串在 chi 字模下的**画布宽度**：Σ advance × (字号/格高)（= `UI/D2Text.ScaleFor` 的口径）；
        /// 字模表与简繁映射都补不到的字 ⇒ 返回 -1 并点名（原版行为是"不画"，这里当失败）。
        /// </summary>
        private static float ChiWidth(ChiMetrics m, string text, int canvasFontSize, out string missing)
        {
            missing = "";
            var native = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var c = (int)text[i];
                if (m.Advance.TryGetValue(c, out var a)) { native += a; continue; }
                if (m.S2T.TryGetValue(c, out var t) && m.Advance.TryGetValue(t, out var a2)) { native += a2; continue; }
                missing += text[i];
            }
            if (missing.Length > 0) return -1f;
            return native * (canvasFontSize / (float)m.Cell);
        }

        internal static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }

        /// <summary>
        /// 「原版依据在磁盘上」这一类断言（原版串表 / 原版 DC6 等）。
        /// 为什么要有这条分支：`原版资源/` **按约定不进 git**（`.gitignore` 末段："下载/解包素材唯一来源，
        /// 体积大 + 版权物，不进 git"）⇒ 干净检出、或没下载过素材的机器上它必然不存在，**那不是缺陷**。
        /// 口径（2026-09-20 定）：
        ///   · 原版资源树**在位** ⇒ 照旧逐条 `Check(File.Exists(...))`（⛔ 在位就必须过，没有放宽）；
        ///   · 原版资源树**不在位** ⇒ 打 `[SKIP]` + 期望路径，**不计失败**（否则这台机器永远到不了 FAILED=0，
        ///     而"红"表达的是「素材没下载」这件与代码无关的事）。
        /// 实测依据：本机 `原版资源/` 不存在（`Test-Path` 假、`git ls-files 原版资源` 空、全工作区搜
        /// `chi_string.txt` / `loadingscreen.dc6` 各 0 命中）⇒ 旧口径下 uicheck 恒 2 项红。
        /// </summary>
        internal static void CheckOriginalRes(string what, string path)
        {
            if (!Directory.Exists(OriginalResDir))
            {
                _skip++;
                Console.WriteLine($"[SKIP] {what}   (原版资源/ 不在本机 ⇒ 跳过；期望路径 {path}；"
                    + "恢复 = 按 tools/probes/README.md 的素材落位说明下载到 <仓库根>/原版资源/)");
                return;
            }
            Check(what, File.Exists(path), File.Exists(path) ? Path.GetFileName(path) : ("缺 " + path));
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // ★ 片 U4：悬停（物品/怪物）/ 拖拽 / 自动地图 —— 离线断言节
    //
    //  判什么（对应用户本轮报的 6 条里归 U4 的那几条）：
    //   ① **自动地图不压暗**：`MiniMapPanel.BackdropAlpha == 0`
    //      （判据原话 =「遮罩 alpha == 0 或不存在压暗层」；`Build()` 在 alpha<=0 时根本不建该节点）。
    //   ② **悬停感应（世界→格换算）在「离玩家 1 格内」也准**：玩家格 + 8 邻域逐格断言
    //      `Iso.WorldToGrid(Iso.GridToWorld(g)) == g`，再断言远处（8 格外）与负数格同样精确
    //      ⇒ 反证不存在「必须把鼠标拉远才生效」的吸附/偏移。⛔ 断言的是**具体格号相等**，
    //      任何吸附或半格偏移都会红（不放松成"任意距离都算过"）。
    //   ③ **物品 tooltip 有名字 + 会折行**：短名 1 行、长魔法名 >= 2 行、空名兜底 1 行；
    //      并断言悬停载荷带 `name` 字段。
    //   ④ **拖拽的「按下 → 移动 → 松手」判定**（判过程不判结果）：`InventoryPanel.PlanDrop`
    //      的 6 种落点组合逐条核对 + 载荷 `fromAnchor | (toAnchor << 16)` 编解码往返一致。
    //
    //  为什么能离线判：以上全是**纯函数 / 常量 / 字段存在性**，不需要 Unity 运行时。
    //  面板实例与像素观感进 Play（本片实机取证那一条链）。
    //
    //  为什么写在 `Program.cs` 里而不是新开 `U4Check.cs`：本目录的编译清单是**白名单**
    //  （`EnableDefaultCompileItems=false` + 逐个 `<Compile Include>`）⇒ 新文件必须同时改
    //  `UiCheck.csproj`；本片对本机 `UiCheck.csproj` 的写入**不落盘**（实测两次 `replace`/一次
    //  整体重写都"成功"返回但内容不变，疑似另一片并发重写该文件）⇒ 退回到「宿主已有的编译单元」
    //  里加一个兄弟类（`Program.cs` 已在清单里，且同文件已有 `CheckR1EDialogUi` 这类先例）。
    // ═════════════════════════════════════════════════════════════════════════
    internal static class U4Check
    {
        /// <summary>转发宿主统一的断言出口（`Program.Check` 负责计数与 `[ OK ]/[FAIL]` 打印）。</summary>
        private static void Check(string what, bool ok, string detail) => Program.Check(what, ok, detail);

        public static void Run()
        {
            CheckBackdrop();
            CheckHoverRange();
            CheckTooltip();
            CheckDrag();
        }

        // ① 自动地图不压暗 ────────────────────────────────────────────────────
        private static void CheckBackdrop()
        {
            Check("U4/automap 不压暗（遮罩 alpha == 0）",
                Diablo2.UI.MiniMapPanel.BackdropAlpha == 0f,
                $"MiniMapPanel.BackdropAlpha = {Diablo2.UI.MiniMapPanel.BackdropAlpha}"
                + "（必须 == 0；>0 时 Build() 会建满屏黑块）");

            Check("U4/automap 压暗关闭方式 = 不建层（alpha <= 0）",
                !(Diablo2.UI.MiniMapPanel.BackdropAlpha > 0f),
                "alpha <= 0 ⇒ Build() 不创建 Backdrop 节点 ⇒ 节点树里不存在压暗层");
        }

        // ② 悬停感应（世界→格换算）────────────────────────────────────────────
        private static void CheckHoverRange()
        {
            var player = new UnityEngine.Vector2Int(37, 52);   // 格号非零，防"默认值相同"掩盖

            var nearOk = true;
            var firstBad = string.Empty;

            for (var dx = -1; dx <= 1 && nearOk; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var g = new UnityEngine.Vector2Int(player.x + dx, player.y + dy);
                    var back = Diablo2.Core.Iso.WorldToGrid(Diablo2.Core.Iso.GridToWorld(g));
                    if (back != g)
                    {
                        nearOk = false;
                        firstBad = $"格({g.x},{g.y}) 反解回 ({back.x},{back.y})";
                        break;
                    }
                }
            }

            Check("U4/悬停：离玩家 1 格内世界坐标 → 格号精确命中（不吸附到玩家格）",
                nearOk,
                nearOk ? $"玩家格 ({player.x},{player.y}) 及 8 邻域共 9 格逐格往返一致"
                       : $"不一致：{firstBad}");

            var far = new UnityEngine.Vector2Int(player.x + 8, player.y - 8);
            var farBack = Diablo2.Core.Iso.WorldToGrid(Diablo2.Core.Iso.GridToWorld(far));
            Check("U4/悬停：远离玩家 8 格处同样精确（不是「必须远才生效」）",
                farBack == far && farBack != player,
                $"远处格({far.x},{far.y}) 反解回 ({farBack.x},{farBack.y})（必须 == 自己，且 != 玩家格）");

            var nega = new UnityEngine.Vector2Int(-3, 2);
            var negBack = Diablo2.Core.Iso.WorldToGrid(Diablo2.Core.Iso.GridToWorld(nega));
            Check("U4/悬停：负数格号往返一致（Floor 而非截断）",
                negBack == nega,
                $"格({nega.x},{nega.y}) 反解回 ({negBack.x},{negBack.y})");
        }

        // ③ 物品 tooltip：有名字 + 会折行 ────────────────────────────────────
        private static void CheckTooltip()
        {
            // ⚠️ 必须全限定：`Diablo2.Def` 与 `Diablo2.UI` 两个命名空间各有一个 `HoverTarget`
            //   （前者 = 悬停快照载荷，后者 = `UI/HoverTarget.cs` 的 uGUI 指针接线件）。
            var t = typeof(Diablo2.Def.HoverTarget);
            var fName = t.GetField("name");
            Check("U4/tooltip：悬停载荷带 `name` 字段（hover 物品/怪物 ⇒ 有名字）",
                fName != null && fName.FieldType == typeof(string),
                fName != null ? $"Def.HoverTarget.name : {fName.FieldType.Name}" : "找不到 Def.HoverTarget.name");

            var shortLines = Diablo2.UI.ItemTooltip.TitleLineCount("短劍");
            var longName = "傷害強化 21~30 阔斧 之 最小傷害 1~2 最大傷害 3~4";
            var longLines = Diablo2.UI.ItemTooltip.TitleLineCount(longName);
            // ⚠️ 口径修正（2026-09-23，片 U4 实测）：原计划离线断言「长魔法名折成 >= 2 行」，
            //   实测恒为 1 行 —— 原因是**本宿主取不到字模指标**：`D2Text.FontFor(26)` 走引擎
            //   `Resources/Clover/...`（`D2Text.cs:133/587`），离线进程没有 Unity Resources ⇒
            //   字模 advance 表为空 ⇒ `CountLines` 无法判满行、只能返回 1。
            //   ⇒ 离线只能判「不崩 + 单调不减」；**折行判据归实机截图行**
            //   （长魔法名悬停截图，见本片回报的 `u4_*` 图）。⛔ 不把这条改成"任意值都算过"。
            Check("U4/tooltip：短名 1 行、且行数随名字变长单调不减（折行本体的判据见实机截图）",
                shortLines == 1 && longLines >= shortLines,
                $"「短劍」={shortLines} 行；长名={longLines} 行"
                + "（离线无字模 ⇒ 折行数值测不到，实机截图行判）");

            Check("U4/tooltip：空名兜底为 1 行（不返回 0）",
                Diablo2.UI.ItemTooltip.TitleLineCount(null) == 1
                && Diablo2.UI.ItemTooltip.TitleLineCount(string.Empty) == 1,
                $"null={Diablo2.UI.ItemTooltip.TitleLineCount(null)} 行，"
                + $"空串={Diablo2.UI.ItemTooltip.TitleLineCount(string.Empty)} 行");
        }

        // ④ 拖拽：按下 → 移动 → 松手（判过程）────────────────────────────────
        private static void CheckDrag()
        {
            var data = BuildSnapshot(out var armorAnchor, out var potionAnchor);

            var a = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, 8, -1, true);
            Check("U4/拖拽：拿起物品 → 松在空格 ⇒ Move(目标格)",
                a.kind == Diablo2.UI.DropKind.Move && a.value == 8,
                $"PlanDrop(from={armorAnchor}, cell=8) = ({a.kind}, {a.value})，期望 (Move, 8)");

            var b = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, potionAnchor, -1, true);
            Check("U4/拖拽：松在另一件物品上 ⇒ Move(该件锚点格)（= 交换）",
                b.kind == Diablo2.UI.DropKind.Move && b.value == potionAnchor,
                $"PlanDrop(from={armorAnchor}, cell={potionAnchor}) = ({b.kind}, {b.value})，"
                + $"期望 (Move, {potionAnchor})");

            var c = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, armorAnchor, -1, true);
            Check("U4/拖拽：松回原格 ⇒ Ignore（不发移动请求）",
                c.kind == Diablo2.UI.DropKind.Ignore,
                $"PlanDrop(from={armorAnchor}, cell={armorAnchor}) = ({c.kind}, {c.value})，期望 (Ignore, _)");

            var d = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, -1, 3, true);
            Check("U4/拖拽：松在装备槽 ⇒ Equip(槽下标)",
                d.kind == Diablo2.UI.DropKind.Equip && d.value == 3,
                $"PlanDrop(from={armorAnchor}, equip=3) = ({d.kind}, {d.value})，期望 (Equip, 3)");

            var e = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, -1, -1, false);
            Check("U4/拖拽：松在面板外 ⇒ DropToGround",
                e.kind == Diablo2.UI.DropKind.DropToGround,
                $"PlanDrop(from={armorAnchor}, insidePanel=false) = ({e.kind}, {e.value})，"
                + $"期望 (DropToGround, {armorAnchor})");

            var f = Diablo2.UI.InventoryPanel.PlanDrop(data, armorAnchor, -1, -1, true);
            Check("U4/拖拽：松在面板内空白处 ⇒ Ignore",
                f.kind == Diablo2.UI.DropKind.Ignore,
                $"PlanDrop(from={armorAnchor}, insidePanel=true) = ({f.kind}, {f.value})，期望 (Ignore, -1)");

            var packed = Diablo2.UI.InventoryPanel.PackMoveInInventory(armorAnchor, potionAnchor);
            var decFrom = packed & 0xFFFF;
            var decTo = (packed >> 16) & 0xFFFF;
            Check("U4/拖拽：MoveInInventoryRequest 载荷 from | (to << 16) 往返一致",
                decFrom == armorAnchor && decTo == potionAnchor,
                $"packed({armorAnchor}→{potionAnchor})={packed} ⇒ 解回 from={decFrom} to={decTo}");

            var noData = Diablo2.UI.InventoryPanel.PlanDrop(null, 5, 6, -1, true);
            Check("U4/拖拽：背包快照为 null ⇒ 不 Move（降级忽略）",
                noData.kind == Diablo2.UI.DropKind.Ignore,
                $"PlanDrop(data=null, cell=6) = ({noData.kind}, {noData.value})，期望 Ignore");
        }

        /// <summary>造一份最小背包快照：5 号格 = 2×2 盔甲（占 5,6,15,16），12 号格 = 1×1 药水，其余空格。</summary>
        private static Diablo2.Def.InventoryChangedArgs BuildSnapshot(out int armorAnchor, out int potionAnchor)
        {
            const int cols = 10;
            var inv = new System.Collections.Generic.List<Diablo2.Def.InventorySlot>();
            for (var i = 0; i < Diablo2.Core.GameConst.InventoryCellCount; i++)
            {
                inv.Add(new Diablo2.Def.InventorySlot
                {
                    index = i, x = i % cols, y = i / cols, occupied = false, anchorIndex = -1,
                });
            }

            armorAnchor = 5;
            var armor = new Diablo2.Def.ItemStack
            {
                itemId = 900, name = "镶甲", type = Diablo2.Def.ItemType.Armor,
                gridW = 2, gridH = 2, quality = Diablo2.Def.ItemQuality.Magic,
            };
            var armorCells = new[] { 5, 6, 15, 16 };
            for (var k = 0; k < armorCells.Length; k++)
            {
                inv[armorCells[k]].occupied = true;
                inv[armorCells[k]].anchorIndex = armorAnchor;
            }
            inv[armorAnchor].item = armor;
            inv[armorAnchor].isAnchor = true;

            potionAnchor = 12;
            inv[potionAnchor].occupied = true;
            inv[potionAnchor].anchorIndex = potionAnchor;
            inv[potionAnchor].isAnchor = true;
            inv[potionAnchor].item = new Diablo2.Def.ItemStack
            {
                itemId = 901, name = "治疗药水", type = Diablo2.Def.ItemType.Misc,
                gridW = 1, gridH = 1, quality = Diablo2.Def.ItemQuality.Normal,
            };

            return new Diablo2.Def.InventoryChangedArgs { gold = 0, inventory = inv };
        }
    }
}
