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
        internal const string ProjectRoot = @"C:\Work\Server\full-dev\clover-project-diablo2";
        internal static readonly string UiDir =
            Path.Combine(ProjectRoot, "client", "Assets", "Scripts", "UI");
        internal static readonly string ResourceRoot =
            Path.Combine(ProjectRoot, "client", "Assets", "Resources");

        internal static CaptureLogger _logger;
        internal static int _fail;

        public static int Main()
        {
            _logger = new CaptureLogger();
            Game.Logger = _logger;

            Console.WriteLine("=== Uicheck：HUD 与游戏内面板 离线自检 ===");
            Console.WriteLine();

            CheckPanelsAndLayers();
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
            LoadingCheck.Run();         // ★ agent-a3：进图读条画面（原版 10 帧动画）+ 区域名弹出（LevelEntryTitle）
            P5Check.Run();              // ★ 片 5：死亡屏（EndGame）拼装+布局 / 小地图（原版 mapicon、标题已删）

            Console.WriteLine();
            Console.WriteLine("未覆盖（需要 Unity 原生，留给主 agent 进 Play 后验）："
                + "① 面板实例化与构件层级；② 贴图像素对齐与观感（含球/条填充真的在动）；"
                + "③ `Time.timeScale=0` 下 `AfterUnscaled` 的实际触发；④ 拖放手感。");
            Console.WriteLine();
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
                    Type = typeof(NpcDialogPanel), Layer = "Popup",
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
                new PanelSpec
                {
                    Type = typeof(DeathPanel), Layer = "Top",
                    Events = new[] { "PlayerDied", "StageLeft", "ReviveRequest", "Revived" },
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
                Check("对话面板：可见区/内容行宽 = 石框内沿 x 2..207 内缩 6 ⇒ 347.4 画布px",
                    Math.Abs(NpcDialogPanel.ContentW - (205f - 2f * NpcDialogPanel.TextPadX) * K) < 0.01f
                    && Math.Abs(NpcDialogPanel.FrameTop - (-172.5f + 158f * K * 0.5f)) < 0.01f
                    && Math.Abs(NpcDialogPanel.FrameBottom - (-172.5f - 158f * K * 0.5f)) < 0.01f,
                    $"W={NpcDialogPanel.ContentW} top={NpcDialogPanel.FrameTop} bottom={NpcDialogPanel.FrameBottom}");

                var bodyTop = NpcDialogPanel.BodyY + NpcDialogPanel.BodyH * 0.5f;
                var bodyBottom = NpcDialogPanel.BodyY - NpcDialogPanel.BodyH * 0.5f;
                Check("对话面板：台词框（名 + 台词）整块落在石框内、且覆盖实测长槽（原版 y 3..90）",
                    Math.Abs(NpcDialogPanel.TextH
                        - (NpcDialogPanel.TextBottomOrigY - NpcDialogPanel.TextTopOrigY) * K) < 0.01f
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

            var tblPath = Path.Combine(ProjectRoot, "原版资源", "d2text", "chi_string.txt");
            if (!File.Exists(tblPath))
            {
                Check("原版串表 chi_string.txt 在磁盘上", false, tblPath);
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
                ResPaths.D2UiPanel + "menubutton__0__0",
                ResPaths.D2UiPanel + "runbutton_run_NotPressed",
                ResPaths.D2UiPanel + "runbutton_walk_NotPressed",
                ResPaths.SkillIconAttack,
            };
            foreach (var s in InventoryPanel.EquipSlots)
                if (!paths.Contains(s.sprite)) paths.Add(s.sprite);
            for (var i = 0; i <= 14; i += 2)
                paths.Add(ResPaths.D2UiPanel + "minipanelbtn__00__" + i.ToString("00"));

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

            Check("按钮文字色 = 原版 WideButton.prefab 的 Text.m_Color（#191919），**不提亮不压暗**",
                Math.Abs(UiLayoutFlow.ButtonText.r - 0.09803922f) < 1e-6f
                && UiLayoutFlow.ButtonText.g == UiLayoutFlow.ButtonText.r
                && UiLayoutFlow.ButtonText.b == UiLayoutFlow.ButtonText.r,
                $"#{Mathf.RoundToInt(UiLayoutFlow.ButtonText.r * 255f):X2}");

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
            Check("屏适配系数：**6 个流程屏全部 = 1**（按高度 ×1.8 后原版整屏正好 1080 高，无需再压）",
                UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Menu) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Pause) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Settings) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Loading) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Boot) == 1f
                && UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Class) == 1f,
                $"Menu={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Menu)} Class={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Class)}"
                + $" Boot={UiLayoutFlow.FitOf(UiLayoutFlow.Panel.Boot)}");

            var panels = new[]
            {
                UiLayoutFlow.Panel.Menu, UiLayoutFlow.Panel.Class, UiLayoutFlow.Panel.Pause,
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
            Check($"6 个流程屏共 {checkedCount} 个元素**两两不重叠**（度量行/热点/容器/原版 ClassName 行已豁免）",
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

            Console.WriteLine("      └────────────────────────────────────────────────────────────────────");
            Console.WriteLine();
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

        internal static void Check(string what, bool ok, string detail)
        {
            if (!ok) _fail++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }
    }
}
