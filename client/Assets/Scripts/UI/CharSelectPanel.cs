// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/CharSelectPanel.cs
// 站点：CharSelect。层：Normal。预制体：`Resources/UI/CharSelectPanel`。
//
// ★ agent-15 §A：**1:1 复刻**原版职业选择屏的骨架（依据 `Prefabs/Menu/ClassSelectMenu.prefab`）：
//     · 背景：原版 `class_select_screen`（800×600）按高度 ×1.8 = 1440×1080 水平居中（不横向拉伸）
//     · 标题行 = 原版 `SelectHeroClass` 文本框 (0,267) 493×30 → 本工程 (0,480.6) 887.4×54
//     · 角色信息行 = 原版 `ClassName` 文本框 (0,198) 493×30 → 本工程 (0,356.4) 887.4×54
//     · 说明行 = 原版 `ClassDescription` 文本框 (1,155) 300×50 → 本工程 (1.8,279) 540×90
//     · 列表容器 = 原版元素围出的"自由带"（热点带左/右 + 说明行底 + 按钮行顶，见 UiLayoutFlow.Select）
//     · 底部两个按钮 = 原版 `ExitButton`(-300,-250) / `OkButton`(300,-250) 的**精确**位置与尺寸
//     · 行内小按钮 = 原版中等按钮（128×35）
//     · 英文/数字（SELECT HERO / 职业名 / LV n / ENTER / DELETE）走**原版位图字体**；
//       中文（角色名等）走原版位图字模（片 3：`D2/Fonts/font16_chi`）；色调 = 原版亮度（白）
// ★ 片 4：**删掉本项目自加的说明行** —— 旧版说明行写的是「存档角色 N 个（一屏最多 7 个）。」，
//   原版屏上没有这行（那个文本框是 `ClassDescription` = 职业说明）⇒ 现在说明行按原版语义
//   显示**高亮角色的职业说明**（英文，文案出处 = 参考工程 `ClassSelectInfo.cs`，
//   经 `UiLayoutFlow.ClassText.Description`；无高亮项时留空 = 原版 `UpdateUi` 给 `string.Empty`）。
//   · 角色数据**只能**由 Flow 通过 `OnOpen(param)` 传入（`Args.entries`）——
//     面板不持有存档模块、不读文件（`constraints.md` #7）。
//   · 删除用引擎 `Game.UI.Confirm` 二次确认。
// 入口：MainMenu 的「单人游戏 / 继续」；出口：进入 → Loading（Flow 驱动）、
//       新建 → CharCreate、返回 → 主菜单。
// ⛔ 不引用任何业务模块。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>角色选择面板（1:1 复刻原版选角屏骨架 + 本项目新增的角色行）。</summary>
    public class CharSelectPanel : UIPanel
    {
        /// <summary>列表里一行角色卡片的数据（由 Flow 组装，面板只显示）。</summary>
        public class Entry
        {
            /// <summary>角色名（唯一键）。</summary>
            public string name;

            /// <summary>职业中文名（来自配表 `class_c.name`，由 Flow 查表后填入）。</summary>
            public string className;

            /// <summary>等级。</summary>
            public int level;

            /// <summary>职业枚举（界面只用于文案兜底）。</summary>
            public PlayerClass cls;
        }

        /// <summary>`OnOpen(param)` 参数。</summary>
        public class Args
        {
            /// <summary>存档角色列表（可为空列表，不可为 null）。</summary>
            public List<Entry> entries = new List<Entry>();
        }

        /// <summary>行内按钮文案（英文 = 走原版位图字体）。</summary>
        private static class Text
        {
            public const string Title = "SELECT HERO";
            public const string NoSelection = "SELECT A HERO";
            public const string Enter = "ENTER";
            public const string Delete = "DELETE";
            public const string NewHero = "NEW HERO";
            public const string Back = "MAIN MENU";
        }

        private RectTransform _listRoot;
        private UiLayoutFlow.FlowLabel _infoLabel;
        private UiLayoutFlow.FlowLabel _descLabel;
        private readonly List<UiLayoutFlow.FlowLabel> _rowLabels = new List<UiLayoutFlow.FlowLabel>();
        private bool _built;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();

            var args = param as Args;
            if (args == null)
            {
                Log.Warn("Ui", "CharSelectPanel.OnOpen 未收到 Args（应为 CharSelectPanel.Args）⇒ 角色列表按空处理");
            }

            Rebuild(args != null && args.entries != null ? args.entries : new List<Entry>());
        }

        // ── 列表 ────────────────────────────────────────────────────────────

        private void Rebuild(List<Entry> entries)
        {
            ClearRows();

            if (entries.Count == 0)
            {
                // ★ 片 4：说明行**按原版语义**（`ClassDescription` 行 = 当前高亮项的职业说明）
                //   —— 没有高亮项 ⇒ 留空（原版 `ClassSelectMenu.UpdateUi` 也是给 `string.Empty`）。
                SetInfo(Text.NoSelection);
                SetDesc(string.Empty);
                Log.Info("Ui", "角色选择屏：无存档角色（说明行留空 = 原版「无高亮项」的取值；" +
                               "左下 NEW HERO 可建角）");
                return;
            }

            if (entries.Count > UiLayoutFlow.Select.MaxRows)
            {
                Log.Warn("Ui", $"存档角色 {entries.Count} 个超过一屏可显示数 {UiLayoutFlow.Select.MaxRows}" +
                               $"（原版行节奏 45 原版px，容器高 322.5 原版px）⇒ 只显示前 {UiLayoutFlow.Select.MaxRows} 个");
            }

            var shown = Mathf.Min(entries.Count, UiLayoutFlow.Select.MaxRows);

            // 行中心 y（相对容器中心）：首行 = 容器顶边内侧；步进 = 原版行节奏 45 原版px → ×1.8 = 81
            var rowH = UiArt.MenuButtonMediumSize.y * UiLayoutFlow.Scale;   // 35 原版px ×1.8 = 63
            var startY = (UiLayoutFlow.Select.ListSize.y - rowH) * 0.5f;    // = (580.5-63)/2 = 258.75
            var step = UiLayoutFlow.RowStep;                                // = 81

            for (var i = 0; i < shown; i++)
            {
                var e = entries[i];
                if (e == null || string.IsNullOrEmpty(e.name))
                {
                    Log.Warn("Ui", $"角色卡片 #{i} 数据非法（name 为空），跳过");
                    continue;
                }

                BuildRow(i, e, startY - i * step);
            }

            if (shown > 0) Highlight(entries[0]);
            else SetInfo(Text.NoSelection);
            // ★ 片 4 删除：这里原来还有一行**本项目自加**的「存档角色 N 个（一屏最多 7 个）。」
            //   —— 原版屏上没有这行（`Prefabs/Menu/ClassSelectMenu.prefab` 的那个文本框是
            //   `ClassDescription` = 职业说明）。现在这一行改回原版语义：见 `Highlight`。
            Log.Info("Ui", $"角色选择屏刷新：显示 {shown} 个存档角色（共 {entries.Count}）；" +
                           $"说明行 = 高亮角色的职业说明（原版 ClassDescription 行的语义）");
        }

        /// <summary>
        /// 高亮某个角色：`ClassName` 行 = 该角色身份，`ClassDescription` 行 = **该职业的原版说明**
        /// （文案出处 = 参考工程 `ClassSelectInfo.cs` 的 `Description`，见 `UiLayoutFlow.ClassText`）。
        /// <para>★ 片 4：说明行以前写的是"存档角色 N 个"（本项目自加，原版没有）⇒ 已按原版语义改回。</para>
        /// </summary>
        private void Highlight(Entry e)
        {
            SetInfo(Describe(e));
            SetDesc(UiLayoutFlow.ClassText.Description(ClassNameLatin(e)));
        }

        /// <summary>一行：透明行热点（点选）+ 名字 / 职业 / 等级 + ENTER / DELETE（原版中等按钮）。</summary>
        private void BuildRow(int index, Entry e, float y)
        {
            // 行矩形 = 容器宽 × 原版按钮高 35 原版px（×1.8 = 63），中心 y = 传入值
            var rowOrigSize = new Vector2(UiLayoutFlow.Orig(UiLayoutFlow.Select.ListSize.x), 35f);
            var row = UIFactory.CreateCentered($"Row{index}", _listRoot, UiLayoutFlow.Px(rowOrigSize),
                new Vector2(0f, y));

            // ① 行热点（**先建** ⇒ 层级最低，压不住行内按钮）：点击 = 选中该角色（等价原版"点职业热点"）
            var name = e.name;
            var entry = e;
            UiLayoutFlow.Hotspot(row, "RowHotspot", rowOrigSize, Vector2.zero, () => Highlight(entry));

            // ② 名字（中文 ⇒ 默认字体）
            _rowLabels.Add(UiLayoutFlow.FlowLabel.Create(row, "Name", e.name, D2Text.D2Font.Font24,
                TextAnchor.MiddleLeft, UiArt.ButtonText, UiLayoutFlow.Orig(UiLayoutFlow.Select.RowNameSize),
                UiLayoutFlow.Select.RowNamePos));

            // ③ 职业（英文 ⇒ 原版位图字体）
            var cls = ClassNameLatin(e);
            _rowLabels.Add(UiLayoutFlow.FlowLabel.Create(row, "Class", cls, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor, UiLayoutFlow.Orig(UiLayoutFlow.Select.RowClassSize),
                UiLayoutFlow.Select.RowClassPos));

            // ④ 等级（数字 ⇒ 原版位图字体）
            _rowLabels.Add(UiLayoutFlow.FlowLabel.Create(row, "Level", $"LV {e.level}", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.ButtonText, UiLayoutFlow.Orig(UiLayoutFlow.Select.RowLevelSize),
                UiLayoutFlow.Select.RowLevelPos));

            // ⑤ 进入 / 删除（原版中等按钮 128×35 → ×1.8 = 230.4×63）
            UiLayoutFlow.FlowButton.Create(row, "Enter", Text.Enter, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.Select.RowEnterPos, () =>
                {
                    Log.Info("Ui", $"角色选择屏：请求进入角色「{name}」");
                    Game.Event.Emit(Events.CharSelectRequest, name);
                });

            UiLayoutFlow.FlowButton.Create(row, "Delete", Text.Delete, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.Select.RowDeletePos, () =>
                {
                    // 二次确认（引擎通用件；同时只显示一个，后到的排队）
                    Game.UI.Confirm("删除角色", $"确定删除角色「{name}」？该操作不可撤销。", () =>
                    {
                        Log.Info("Ui", $"角色选择屏：已确认删除「{name}」");
                        Game.Event.Emit(Events.CharDeleteRequest, name);
                    }, () => Log.Info("Ui", $"角色选择屏：取消删除「{name}」"));
                });
        }

        /// <summary>职业名的拉丁写法（原版屏上就是英文职业名；没有就用枚举名兜底）。</summary>
        private static string ClassNameLatin(Entry e)
        {
            if (e.cls != 0) return e.cls.ToString().ToUpperInvariant();
            Log.Warn("Ui", $"角色「{e.name}」的职业枚举为空 ⇒ 职业列显示 NONE（数据来源见 Flow）");
            return "NONE";
        }

        private static string Describe(Entry e)
        {
            if (e == null) return Text.NoSelection;
            return $"{ClassNameLatin(e)}  LV {e.level}";
        }

        private void SetInfo(string text)
        {
            if (_infoLabel != null) _infoLabel.SetText(text);
        }

        private void SetDesc(string text)
        {
            if (_descLabel != null) _descLabel.SetText(text);
        }

        private void ClearRows()
        {
            _rowLabels.Clear();
            if (_listRoot == null) return;
            for (var i = _listRoot.childCount - 1; i >= 0; i--)
            {
                var child = _listRoot.GetChild(i).gameObject;
                child.SetActive(false);          // 立即不再显示（Destroy 要到帧末才生效）
                Destroy(child);
            }
        }

        // ── 构建（只搭原版骨架，逐行内容在 Rebuild 里填）────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;

            // 原版职业选择屏贴图（800×600）**按高度 ×1.8 = 1440×1080 水平居中**
            // （挂在**面板根**上；左右各留 240 由纯色底补，**不把 4:3 素材横向拉伸**）；
            // ⚠️ **必须先建**（它是同层第一个子节点 ⇒ 层级最低 ⇒ 不会盖住后面的 UI 元素）。
            UiLayoutFlow.BackdropArt(transform, ResPaths.MenuClassSelectScreen, new Color(0.04f, 0.04f, 0.05f, 1f));

            // 屏适配容器：改按高度 ×1.8 后原版整屏正好 1080 高（标题 480.6 / 底部按钮 -450 都在 ±540 内）
            // ⇒ 系数 = 1（`FitClass`），**不再需要旧的 0.75**（那是为"×2.4 后 1440 高"压进 1080 用的）。
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitClass);

            // 标题：原版 `SelectHeroClass` 文本框（493×30 原版px，alignment=UpperLeft）
            // ⚠️ `FlowLabel.Create` 的尺寸参数是**原版 px**（位图字模按原版像素排版）⇒ 常量表（Canvas 单位）
            //    统一经 `Orig()` 折回原版 px。
            UiLayoutFlow.FlowLabel.Create(screen, "Title", Text.Title, D2Text.D2Font.Font24,
                TextAnchor.UpperLeft, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.TitleSize),
                UiLayoutFlow.ClassMenu.TitlePos);

            // 角色信息行：原版 `ClassName` 文本框（493×30 原版px，alignment=UpperLeft）
            _infoLabel = UiLayoutFlow.FlowLabel.Create(screen, "Info", Text.NoSelection, D2Text.D2Font.Font24,
                TextAnchor.UpperLeft, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.NameRowSize),
                UiLayoutFlow.ClassMenu.NameRowPos);

            // 说明行：原版 `ClassDescription` 文本框（300×50 原版px，alignment=MiddleCenter；中文 ⇒ 默认字体）
            _descLabel = UiLayoutFlow.FlowLabel.Create(screen, "Desc", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, UiArt.TextColor, UiLayoutFlow.Orig(UiLayoutFlow.ClassMenu.DescSize),
                UiLayoutFlow.ClassMenu.DescPos);

            // 角色列表容器（原版元素边界围出的自由带，见 UiLayoutFlow.Select 的推导注释）
            _listRoot = UIFactory.CreateCentered("List", screen, UiLayoutFlow.Select.ListSize,
                UiLayoutFlow.Select.ListPos);

            // 底部两个按钮 = 原版 `ExitButton` / `OkButton` 的精确矩形（原版中等按钮）
            UiLayoutFlow.FlowButton.Create(screen, "Create", Text.NewHero, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.ClassMenu.ExitPos, () => Game.Event.Emit(Events.Fsm.TriggerNeedCreate));

            UiLayoutFlow.FlowButton.Create(screen, "Back", Text.Back, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.ClassMenu.OkPos, () => Game.Event.Emit(Events.ToMainMenuRequest));

            UiLayoutFlow.LogTable(nameof(CharSelectPanel));
        }
    }
}
