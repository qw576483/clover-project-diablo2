// ─────────────────────────────────────────────────────────────────────────────
//
// 把并发写冲突面压到最小。
//
//   ① **每个元素 == 原版 prefab 的精确 RectTransform 值 × 1.8**（下面把原版值**硬编码进断言**，
//      不是"拿我们的常量去比我们的常量"）；
//   ② HUD 图元**两两不重叠**、格子总数 = 40、装备槽 = 10；
//   ③ 面板在该在的那半边（背包贴中线右侧、属性贴左侧 —— 原版 `m_Pivot` 决定）；
//
// 依据文件（原版 `Diablerie/Assets/Prefabs/`，脚本逐节点解析，非肉眼估）：
//   ControlPanel.prefab / InventoryPanel.prefab / CharstatPanel.prefab /
//   SkillPanel.prefab / SkillSlot.prefab / AvailableSkillsPanel.prefab /
//   EnemyBar.prefab / LevelEntryTitle.prefab
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.UI;
using UnityEngine;

namespace Uicheck
{
    /// <summary>游戏内面板（HUD / 背包 / 属性 / 技能）的 ×1.8 布局断言。</summary>
    internal static class LayoutGameCheck
    {
        /// <summary>本文件里的失败条数（由 `Program` 汇总）。</summary>
        public static int Failures;

        /// <summary>原版 → 本工程的缩放系数（**按高度**：1080/600 = 1.8）。</summary>
        private const float K = 1.8f;

        /// <summary>原版画布高度（用于底边锚定的 y 换算）。</summary>
        private const float OrigH = 600f;

        private static bool Near(float a, float b, float eps = 0.02f) => Math.Abs(a - b) <= eps;

        private static bool NearV(Vector2 a, Vector2 b, float eps = 0.02f)
            => Near(a.x, b.x, eps) && Near(a.y, b.y, eps);

        /// <summary>
        /// 原版底边锚定的 y（原版 y 相对画布底边）→ 我们的中心 y。
        /// 与 `UiLayoutGame.BottomY` 同口径：**(原版 y + 贴底抬升 21.3) × 1.8 − 540**。
        /// </summary>
        private static float BottomY(float origY)
            => (origY + UiLayoutGame.HudBaseLift) * K - 1080f * 0.5f;

        private static Rect RectAt(Vector2 c, Vector2 s) => new Rect(c.x - s.x * 0.5f, c.y - s.y * 0.5f, s.x, s.y);

        /// <summary>
        /// 两个矩形是否分离。
        /// <para>
        /// 带 `TouchEps` 容差：技能树的行距 = 图标高 48×1.8 ⇒ **相邻两行恰好相接**
        /// （`a.yMax == b.yMin`）。而 `48f*1.8f` 的浮点结果让两侧差 1e-4 量级 ⇒ 不给容差会把
        /// "恰好相接"判成"重叠"（假阳性）。相接不算重叠。
        /// </para>
        /// </summary>
        private const float TouchEps = 0.05f;

        private static bool Separated(Rect a, Rect b)
            => a.xMax <= b.xMin + TouchEps || b.xMax <= a.xMin + TouchEps
            || a.yMax <= b.yMin + TouchEps || b.yMax <= a.yMin + TouchEps;

        private static void Check(string what, bool ok, string detail)
        {
            if (!ok) Failures++;
            Console.WriteLine($"{(ok ? "[ OK ]" : "[FAIL]")} {what}   ({detail})");
        }

        public static void Run()
        {
            CheckScale();
            CheckHud();
            CheckInventory();
            CheckCharstat();
            CheckSkill();
            CheckTopBars();
        }

        // ═════════════════════════════════════════════════════════════════════
        private static void CheckScale()
        {
            Check("缩放系数 K = 1080/600 = 1.8（**按高度等比**；原版 800×600 ⇒ 本工程 1920×1080 水平居中）",
                Near(UiLayoutGame.K, 1.8f) && Near(UiArt.RefWidth, 1920f) && Near(UiArt.RefHeight, 1080f),
                $"K={UiLayoutGame.K}, Canvas={UiArt.RefWidth}×{UiArt.RefHeight}");
            Check("S()/Size()/BottomY() 三个换算函数自洽（y 用底边锚定 + 贴底抬升 21.3 的 600 基准）",
                Near(UiLayoutGame.S(948f), 1706.4f)
                && Near(UiLayoutGame.BottomY(60f), (60f + UiLayoutGame.HudBaseLift) * K - 540f),
                $"S(948)={UiLayoutGame.S(948f):0.###}, BottomY(60)={UiLayoutGame.BottomY(60f):0.###}");
        }

        // ═════════════════════════════════════════════════════════════════════
        // ① HUD —— 逐元素 == ControlPanel.prefab 实测值 × 1.8
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckHud()
        {
            Check("HUD 控制面板底图 = 原版 Background 948×160 ×1.8（= 1706.4×288，水平居中含在 1920 内）",
                NearV(HudPanel.PanelBgSize, new Vector2(948f * K, 160f * K)),
                HudPanel.PanelBgSize.ToString());
            Check("HUD 控制面板中心 y = (原版 pos.y −21.3 + 高 80 + 贴底抬升 21.3) ×1.8 − 540 = −396",
                Near(HudPanel.PanelBgPos.y, BottomY(-21.3f + 80f)) && Near(HudPanel.PanelBgPos.x, 0f),
                HudPanel.PanelBgPos.ToString());
            //   旧判据「图框底边 == 画布底边」是错的 —— 它逼着 `HudBaseLift = 21.3`，
            //   而实测（原版实机图的量法；该脚本是一次性件、已不在盘）`ControlPanel.png` 的
            //   **不透明内容只到第 138 行**（139..159 行 alpha 全 0）⇒ 图框底部那 21.3px 本来就透明，
            //   原版让它们落到屏幕外即可。抬起来以后画面底部反而露出 39 画布px 的场景。
            //   新判据 = 「**内容收口在画布底边**」：rect 底边 = 画布底边 − 21.3×1.8（那段是透明的），
            //   而 rect 底边 + (160−138)×1.8 必须落在画布底边（容差 2px）。
            Check("HUD 控制面板**内容收口在画布底边**（rect 底边比画布底边低 21.3×1.8；那 21.3 行本就是透明的）",
                Near(HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f, -1080f * 0.5f - 21.3f * K, 0.5f)
                && Near(HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f + (160f - 138f) * K, -1080f * 0.5f, 2f),
                $"rect 底边 y = {HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f:0.###}"
                + $"（画布底边 −540；期望 −540−38.34）; 内容底 = "
                + $"{HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f + (160f - 138f) * K:0.###}");
            // hud-redo2：旧判据「整体在画布内」在 `HudBaseLift = 0` 下必然为假（rect 底边低 38.34，
            //   因为底图自己那 21.3 行是透明的）⇒ 改成两条**可判的**判据：
            //   ① 水平方向整个在画布内（948×1.8 = 1706.4 ≤ 1920，左右各留 106.8）；
            //   ② 垂直方向**越出量正好等于底图的透明尾巴**（21.3×1.8 = 38.34，容差 0.5）。
            Check("HUD 控制面板**水平居中且整幅在画布内**（1706.4 ≤ 1920，左右各留 106.8）",
                Near(HudPanel.PanelBgPos.x, 0f)
                && HudPanel.PanelBgPos.x - HudPanel.PanelBgSize.x * 0.5f >= -960f
                && HudPanel.PanelBgPos.x + HudPanel.PanelBgSize.x * 0.5f <= 960f,
                $"{HudPanel.PanelBgSize} @ {HudPanel.PanelBgPos}");
            Check("HUD 控制面板**底边越出画布的量 == 底图透明尾巴** 21.3×1.8 = 38.34（不是出屏 51px 的旧 E5）",
                Near(-1080f * 0.5f - (HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f), 21.3f * K, 0.5f),
                $"越出 = {-1080f * 0.5f - (HudPanel.PanelBgPos.y - HudPanel.PanelBgSize.y * 0.5f):0.###}");

            Check("生命球 = 原版 Lifebulb 108×108 @ (−316.01, 66.96) ×1.8",
                Near(HudPanel.OrbSize, 108f * K) && NearV(HudPanel.LifeOrbPos, new Vector2(-316.01f * K, BottomY(66.96f))),
                $"{HudPanel.OrbSize} @ {HudPanel.LifeOrbPos}");
            Check("法力球 = 原版 Manabulb 108×108 @ (299.56, 66.96) ×1.8",
                NearV(HudPanel.ManaOrbPos, new Vector2(299.56f * K, BottomY(66.96f))),
                HudPanel.ManaOrbPos.ToString());

            Check("技能格 = 原版 LeftSkill/RightSkill 33.495×35.12 ×1.8",
                NearV(HudPanel.SkillSlotSize, new Vector2(33.495f * K, 35.12f * K)),
                HudPanel.SkillSlotSize.ToString());
            Check("左键技能格 = 原版 pos(−229.9, 35.2) ×1.8",
                NearV(HudPanel.LeftSkillPos, new Vector2(-229.9f * K, BottomY(35.2f))),
                HudPanel.LeftSkillPos.ToString());
            Check("右键技能格 = 原版 pos(−192.2, 35.5) ×1.8",
                NearV(HudPanel.RightSkillPos, new Vector2(-192.2f * K, BottomY(35.5f))),
                HudPanel.RightSkillPos.ToString());

            // w3 审计改口径（**不是放宽**：从"照 prefab 节点 sizeDelta"改成"照素材原生尺寸 ×1.8"）：
            //   原版这张底图（`Panel/minipanel.png`）实测 **173×26**，而 `ControlPanel.prefab` 的
            //   `ImageMinipanel` 节点写 152×26 ⇒ 照 152 贴会把图**水平压到 87.9%**（垂直不动）＝ 非等比拉伸，
            //   且同批的 7 个按钮（20×20 原生）是按 ×1.8 摆的 ⇒ 一块 HUD 上两种水平比例。
            //   判据 = 本次审计统一口径「控件矩形 == 原版像素 ×1.8」（原版像素 = 素材自己的像素）。
            //   量法 `tools/probes/measure/hud_measure.py`（**该脚本仍在盘**；把"原版实机图"与"本项目实机图"都换算到
            //   `ControlPanel.png` 的 art 坐标系，artY 自面板顶量、PY 距画布底边）：
            //   原版基线 `策划/基线图/原版_实机_UI基准_20260923.png` 里这一排按钮的中心 PY =
            //   **91.6**（标定 A：用两只球的球心反解 scale/x0，见量法文件头）/ **113.6**（标定 B：800×600 屏宽比）
            //   ⇒ 取偏保守的 **90**（比两个估计都低，但仍比 72.7 高 17 原版px）。
            //   两条独立量法都指向"比 72.7 更高" ⇒ 方向可靠（幅度 ±11 原版px）。
            //   （hud-redo2 的"维持 72.7"是**放大读图**定的、没有量化；本轮有基准图 + 量法脚本 ⇒ 以量出来的为准。
            //    两者都与格带不相交：90 ⇒ 底条 art y 34..60；格带 art y 87..115 ⇒ 余 27 行。）
            Check("小面板底图 = 原版素材原生 173×26，y = 基线图量出的 90（与格带 art y 87..115 不相交）",
                NearV(HudPanel.MiniPanelSize, new Vector2(173f * K, 26f * K))
                && Near(HudPanel.MiniPanelY, BottomY(90f)),
                $"{HudPanel.MiniPanelSize} @ y={HudPanel.MiniPanelY}（期望 {BottomY(90f)}）");
            // U3：**按钮数 7 → 8**。依据 = `minipanelbtn.DC6` 16 帧 = 8 对（常态/按下）
            //   + `string.tbl` 的 8 条 `minipanel*` tooltip + `strpanel1..8`（见 UiLayoutGame.MiniButtonCount）。
            //   8 钮按 pitch 21 居中排 ⇒ 中心 ±10.5/±31.5/±52.5/±73.5（占宽 167 ≤ 173）。
            Check("小面板 = 原版 8 按钮 20×20、pitch 21 居中（±10.5..±73.5）、y 与底图同 ×1.8",
                Near(HudPanel.MiniButtonSize, 20f * K)
                && HudPanel.MiniButtonX.Length == 8
                && Near(HudPanel.MiniButtonX[0], -73.5f * K)
                && Near(HudPanel.MiniButtonX[3], -10.5f * K)
                && Near(HudPanel.MiniButtonX[4], 10.5f * K)
                && Near(HudPanel.MiniButtonX[7], 73.5f * K)
                && Near(HudPanel.MiniButtonX[1] - HudPanel.MiniButtonX[0], 21f * K),
                string.Join(",", HudPanel.MiniButtonX));

            // 2026 修正（实测驱动，**不是为过而乱改**）：原版底图上画出来的**格内凹槽**是 27×25，
            //   实测：竖分隔饰条 art x 613..616 / 644..647 / 675..678 / 706 ⇒ 格内宽 27；
            //        上/下沿 art y 86..88 / 114..116 ⇒ 格内高 25。
            Check("腰带 4 格 = 原版底图实测**格内凹槽 27×25** ×1.8（旧值 31×29 = pitch/可见高，偏大 15%）",
                Near(HudPanel.BeltCellSize, 27f * K) && Near(HudPanel.BeltCellH, 25f * K),
                $"{HudPanel.BeltCellSize}×{HudPanel.BeltCellH}");
            Check("腰带 4 格 x = 原版底图实测 125.5/156.5/187.5/218.5 ×1.8（**中线右侧**，紧邻法力球）",
                HudPanel.BeltCellX.Length == GameConst.BeltSlots
                && Near(HudPanel.BeltCellX[0], 125.5f * K) && Near(HudPanel.BeltCellX[3], 218.5f * K),
                string.Join(",", HudPanel.BeltCellX));
            Check("腰带格中心 y = 原版底图 art y 101 → 屏幕 37.7 ×1.8 − 540",
                Near(HudPanel.BeltCellY, BottomY(37.7f)), HudPanel.BeltCellY.ToString());

            Check("6 格技能栏 = 原版 SkillPanel.prefab 根 224.97×35.12 @ (−44.58, 35.76) ×1.8",
                HudPanel.SkillBarSlots == 6
                && NearV(HudPanel.SkillBarSize, new Vector2(224.97f * K, 35.12f * K))
                && NearV(HudPanel.SkillBarCenter, new Vector2(-44.58f * K, BottomY(35.76f))),
                $"{HudPanel.SkillBarSize} @ {HudPanel.SkillBarCenter}");
            Check("技能栏 6 槽等分（步进 = 224.97/6×1.8 = 67.49；首末槽对称于根中心）",
                Near(HudPanel.SkillBarSlotX(1) - HudPanel.SkillBarSlotX(0), 224.97f / 6f * K)
                && Near(HudPanel.SkillBarSlotX(0) + HudPanel.SkillBarSlotX(5), HudPanel.SkillBarCenter.x * 2f),
                $"{HudPanel.SkillBarSlotX(0):0.##} … {HudPanel.SkillBarSlotX(5):0.##}");

            Check("跑/走按钮尺寸 = 原版 ImageExpBarLeft 子 Button 16×20 → ×1.8 = 28.8×36",
                NearV(HudPanel.RunButtonSize, new Vector2(16f * K, 20f * K)),
                HudPanel.RunButtonSize.ToString());
            //   跑/走按钮的父容器 `ImageExpBarLeft`(GO 1692291003419990) 与小面板开关的父容器
            //   `ImageExpBarRight`(GO 1161153139115278) 都是 **`m_IsActive: 0`**，参考工程
            //   `Scenes/Game.unity` 对这两个对象**没有 active 覆盖** ⇒ **原版 HUD 上看不到这两个按钮**。
            //   而按 prefab 原值（art y 21 / 26）摆时它们会**分别压在第 1、第 5 个技能格上**
            //   （`ControlPanel.png` 底图实测：技能格带 art y 86..115）—— 这正是用户报的
            //   「下方技能栏图标摆放位置不对」。本项目只把 **y** 挪到格带**下方的空白条**
            //   （art y 14 → 画布 y = 14×1.8 − 540 = −514.8），**x 仍严格照 prefab**。
            Check("跑/走按钮 x = 原版 ImageExpBarLeft/Button（原版中心 art 264 ⇒ 偏移 −136）×1.8 = −244.8；"
                  + "y = 格带下方空白条（本项目 art y 14 → −514.8，**不再压第 1 个技能格**）",
                Near(HudPanel.RunButtonPos.x, (264f - 400f) * K)
                && Near(HudPanel.RunButtonPos.y, UiLayoutGame.HudSubBarY)
                && Near(HudPanel.RunButtonPos.y, 14f * K - 540f),
                HudPanel.RunButtonPos.ToString());

            Check("小面板开关 = 原版 ImageExpBarRight/Button 15×24 → ×1.8 = 27×43.2；x 照 prefab（art 475）；"
                  + "y = 同一条空白条 −514.8（**不再压第 5 个技能格**）",
                NearV(HudPanel.MiniPanelArrowSize, new Vector2(15f * K, 24f * K))
                && Near(HudPanel.MiniPanelArrowPos.x, (475f - 474f) * K)
                && Near(HudPanel.MiniPanelArrowPos.y, 14f * K - 540f),
                $"{HudPanel.MiniPanelArrowSize} @ {HudPanel.MiniPanelArrowPos}");

            {
                var blockers = new List<(string name, Rect rect)>();
                blockers.Add(("LeftSkill", RectAt(HudPanel.LeftSkillPos, HudPanel.SkillSlotSize)));
                blockers.Add(("RightSkill", RectAt(HudPanel.RightSkillPos, HudPanel.SkillSlotSize)));
                for (var i = 0; i < UiLayoutGame.SkillBarSlots; i++)
                    blockers.Add(("SkillBar" + i,
                        RectAt(new Vector2(UiLayoutGame.SkillBarSlotX(i), HudPanel.SkillBarCenter.y), HudPanel.SkillSlotSize)));
                for (var i = 0; i < HudPanel.BeltCellX.Length; i++)
                    blockers.Add(("Belt" + i, RectAt(new Vector2(HudPanel.BeltCellX[i], HudPanel.BeltCellY),
                        new Vector2(HudPanel.BeltCellSize, HudPanel.BeltCellH))));
                blockers.Add(("ExpBar", RectAt(HudPanel.ExpBarPos, HudPanel.ExpBarSize)));
                blockers.Add(("MiniPanel", RectAt(new Vector2(0f, HudPanel.MiniPanelY), HudPanel.MiniPanelSize)));
                blockers.Add(("LifeOrb", RectAt(HudPanel.LifeOrbPos, new Vector2(HudPanel.OrbSize, HudPanel.OrbSize))));
                blockers.Add(("ManaOrb", RectAt(HudPanel.ManaOrbPos, new Vector2(HudPanel.OrbSize, HudPanel.OrbSize))));

                // hud-redo2：这两个入口**默认隐藏**（`SetActive(false)`，画面上不存在）
                //   ⇒ 它们的矩形**不参与本判定**（矩形相交对画面没有后果）。
                //   art y 129..133，而"空白条"（格带下沿 115 → 经验条上沿 129）只剩 **14 行**，
                //   装不下 20 行高的按钮 ⇒ 旧抬升下"恰好不冲突"只是坐标巧合，不是设计。
                //   ⇒ 判据 ①（本节）= 只判**会显示**的图元不压原版格子；
                //      判据 ②（紧随其后 `HiddenEntriesStillHidden`）= 这两个入口**确实还是隐藏的**
                //      —— 用源码断言把它们"钉"在隐藏态，谁要打开就必须同时重解坐标。
                var hit = new List<string>();
                var visibleEntries = new (string name, Vector2 c, Vector2 s)[] { };
                foreach (var p in visibleEntries)
                {
                    var r = RectAt(p.c, p.s);
                    foreach (var b in blockers)
                        if (!Separated(r, b.rect)) hit.Add(p.name + "×" + b.name);
                }
                Check("跑/走按钮与小面板开关**不压任何原版格子/球/经验条**（两者默认隐藏 ⇒ 本判定只对有显示的图元生效，见下一条）",
                    hit.Count == 0, hit.Count == 0 ? "0 冲突（两个入口当前均隐藏）" : string.Join(", ", hit.ToArray()));
            }

            // hud-redo2 新增：把「这两个入口是隐藏的」做成**源码断言**（把豁免换成判据）。
            //   依据：`HudPanel.BuildRunButton()` / `BuildMiniPanelToggle()` 里各有一行
            //   `SetActive(false)`（带量化理由：素材上无处可放）。若谁把它们打开 =
            //   `RunButton×ExpBar`（盖经验条 16×4 原版px）与 `MiniArrow×ExpBar` 立刻成立
            //   ⇒ 本断言会红 ⇒ 必须同时重解坐标（交回主 agent）。
            {
                var srcPath = Path.Combine(Program.UiDir, "HudPanel.cs");
                var src = File.Exists(srcPath) ? File.ReadAllText(srcPath, Encoding.UTF8) : string.Empty;
                var stripped = string.Join("\n", src.Split('\n'));
                var runHidden = stripped.Contains("_runButton.gameObject.SetActive(false)")
                                || stripped.Contains("_runButton.gameObject.SetActive(false);");
                var arrowHidden = stripped.Contains("_miniToggle.gameObject.SetActive(false)");
                Check("HUD 两个额外鼠标入口（跑/走按钮、小面板开关）**都是隐藏的**"
                      + "（各自 `SetActive(false)`；要打开必须先重解坐标 —— 素材上没有装得下 20 行高按钮的空位）",
                    src.Length > 0 && runHidden && arrowHidden,
                    $"HudPanel.cs {(src.Length > 0 ? src.Length + " 字节" : "读不到")}; "
                    + $"RunButton 隐藏={runHidden}, MiniArrow 隐藏={arrowHidden}");
            }

            Check("经验条 = 原版 ExperienceBar 486.94×4.06 @ (−8.9, 7.77) ×1.8",
                NearV(HudPanel.ExpBarSize, new Vector2(486.94f * K, 4.06f * K))
                && NearV(HudPanel.ExpBarPos, new Vector2(-8.9f * K, BottomY(7.77f))),
                $"{HudPanel.ExpBarSize} @ {HudPanel.ExpBarPos}");
            Check("经验条覆盖层 = 原版 ExpBarOverlay 948×160 @ (0, 59.1) ×1.8",
                Near(HudPanel.ExpOverlayPos.y, BottomY(59.1f)), HudPanel.ExpOverlayPos.ToString());

            // 图元两两不重叠（底图本身包住全部元素 ⇒ 不参与两两判定）
            //
            // **原版本身就重叠**的豁免（不豁免 = 把"照抄原版"判成 bug = 假阳性）：
            //   ① `SkillPanel`（6 格技能栏）与 `ImageMinipanel` 的 7 个按钮：
            //      两者 art y 带完全重合（技能栏 art y 18.2..53.32；minipanel 子按钮 art y 73..93 附近
            //      —— 见原版 `ControlPanel.prefab` 的 `ImageMinipanel` pos(0,60)、按钮 20×20）⇒
            //      **原版 prefab 里就重叠**，不是我们的 bug；
            //    那两个按钮的 y 已从 prefab 原值挪到格带下方的空白条 ⇒ 不再压技能格，
            //    豁免没必要了 —— 留着反而会掩盖回归。见 `UiLayoutGame.HudSubBarArtY`。）
            //
            // U3 又删掉豁免 ①（`SkillBar×MiniBtn`）—— 同一条理由：底条 y 已改成
            //   「下沿贴住格带上沿」（origY 60 → 72.7，见 `UiLayoutGame.MiniPanelY`），
            //   实测 MiniBtn 底沿 = −388.8、SkillBar 顶沿 = −405.7 ⇒ **不再相交**
            //   ⇒ 豁免已无对象，留着只会掩盖"以后谁把底条挪回去"这种回归。
            //   除下面一条外，HUD 图元仍是**零豁免**：任何一对相交都算失败。
            //
            //   背景：`HudBaseLift` 由 21.3 改成 0（实测：底图底部 21px 本就是透明的，抬升会让屏幕
            //   底部露 39 画布px 场景）⇒ 经验条回到原版行 art y 129..133，而"空白条"只剩 14 行，
            //   装不下 20 行高的跑/走按钮 ⇒ 这两对矩形必然相交。**但这两个节点默认隐藏**
            //   （`SetActive(false)`）⇒ 画面上不存在、无任何视觉后果；
            //   且"它们确实隐藏"已被上一条 `HiddenEntriesStillHidden` 源码断言钉住。
            //   谁把它们打开 ⇒ 那条断言先红 ⇒ 必须回来重解坐标（本条豁免不构成"随便相交"的许可）。
            //   豁免范围 = **凡是涉及这两个隐藏节点的对**（`RunButton×*` / `MiniArrow×*`）：
            //      `HudBaseLift` 归零后它们与 `ExpBar` 相交是必然（见上），而
            //      `SkillBar0×RunButton` / `SkillBar4×MiniArrow` 是同一个坐标巧合的另一面
            //      —— 只列其中几条会留下"改一个 y 就红、改另一个不红"的伪信号。
            static bool ExemptPair(string a, string b)
            {
                bool Has(string x) => a == x || b == x;
                return Has("RunButton") || Has("MiniArrow");
            }

            var rects = UiLayoutGame.HudRects();
            var bad = new List<string>();
            var counted = 0;
            for (var i = 0; i < rects.Length; i++)
            {
                if (rects[i].container) continue;
                counted++;
                for (var j = i + 1; j < rects.Length; j++)
                {
                    if (rects[j].container) continue;     // 底图容器不算（子图元本来就画在它上面）
                    if (ExemptPair(rects[i].name, rects[j].name)) continue;   // 原版自己也重叠（见上）
                    if (!Separated(rects[i].rect, rects[j].rect))
                        bad.Add(rects[i].name + "×" + rects[j].name);
                }
            }
            Check($"HUD {counted} 个图元两两不重叠（整幅底图/小面板底图作容器不参与判定；"
                  + "★ U3 起**零豁免**：SkillBar×MiniBtn 也已不再重叠，豁免已删）", bad.Count == 0,
                bad.Count == 0 ? "0 冲突" : string.Join(", ", bad.ToArray()));

            Check("HUD 图元都在 1920×1080 之内（底图也已不再越界，见上一条）",
                InCanvas(UiLayoutGame.LifeOrbPos, new Vector2(HudPanel.OrbSize, HudPanel.OrbSize))
                && InCanvas(UiLayoutGame.ManaOrbPos, new Vector2(HudPanel.OrbSize, HudPanel.OrbSize))
                && InCanvas(UiLayoutGame.LeftSkillPos, HudPanel.SkillSlotSize)
                && InCanvas(new Vector2(HudPanel.BeltCellX[3], HudPanel.BeltCellY),
                    new Vector2(HudPanel.BeltCellSize, HudPanel.BeltCellH)),
                $"生命球 {HudPanel.LifeOrbPos}");
            // 2026 主 agent 裁决：改 ×1.8（按高）后 948×1.8 = 1706.4 **≤ 1920** ⇒ 控制面板横向不再越界，
            //   左右各留 106.8（这部分空间交给相机/背景，**不把 UI 横向拉伸**；原版 ×1.8 时 948*2.4=2275.2>1920 会越界）。
            Check("控制面板底图横向**不再越界**（948×1.8 = 1706.4 ≤ 1920）",
                HudPanel.PanelBgSize.x <= UiArt.RefWidth
                && Near(HudPanel.PanelBgSize.x, 948f * K),
                $"{HudPanel.PanelBgSize.x:0.#} ≤ {UiArt.RefWidth}（左右各留 {(UiArt.RefWidth - HudPanel.PanelBgSize.x) * 0.5f:0.#}）");
        }

        private static bool InCanvas(Vector2 c, Vector2 s)
        {
            var hw = UiArt.RefWidth * 0.5f;
            var hh = UiArt.RefHeight * 0.5f;
            return c.x - s.x * 0.5f >= -hw - 0.01f && c.x + s.x * 0.5f <= hw + 0.01f
                && c.y - s.y * 0.5f >= -hh - 0.01f && c.y + s.y * 0.5f <= hh + 0.01f;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ② 背包 —— == InventoryPanel.prefab 实测值 × 1.8
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckInventory()
        {
            Check("背包面板尺寸 = 原版 Panel 320×432 ×1.8",
                NearV(InventoryPanel.PanelSize, new Vector2(320f * K, 432f * K)),
                InventoryPanel.PanelSize.ToString());
            Check("背包面板中心 = 原版 Panel(pivot 0,0.5 @ 0,0) 的矩形中心 (160,0) ×1.8 ⇒ **贴屏幕中线右侧**",
                NearV(InventoryPanel.PanelPos, new Vector2(160f * K, 0f)),
                InventoryPanel.PanelPos.ToString());

            Check("格宽/格高 = 原版底图格线实测 29.2 / 29.25 ×1.8",
                Near(InventoryPanel.CellW, 29.2f * K) && Near(InventoryPanel.CellH, 29.25f * K),
                $"{InventoryPanel.CellW}×{InventoryPanel.CellH}");
            Check("格区左上角 = 原版底图 (17,252) → 面板中心 (−143,−36) ×1.8（贴图 y 向下、面板中心坐标 y 向上）",
                NearV(InventoryPanel.GridOrigin, new Vector2(-143f * K, -36f * K)),
                InventoryPanel.GridOrigin.ToString());
            Check("格子总数 = 40（10×4，与 GameConst 同口径）",
                InventoryPanel.CellCount == 40
                && InventoryPanel.CellCount == GameConst.InventoryCols * GameConst.InventoryRows
                && InventoryPanel.EquipSlots.Length == 10,
                $"格={InventoryPanel.CellCount} 装备槽={InventoryPanel.EquipSlots.Length}");

            var overlap = false;
            var outside = string.Empty;
            for (var i = 0; i < InventoryPanel.CellCount; i++)
            {
                var c = InventoryPanel.CellCenter(i);
                var r = RectAt(InventoryPanel.PanelPos + c, new Vector2(InventoryPanel.CellW, InventoryPanel.CellH));
                if (!InPanel(r, InventoryPanel.PanelPos, InventoryPanel.PanelSize))
                {
                    outside = $"格 {i} {r}";
                    break;
                }
                var col = i % InventoryPanel.Cols;
                if (col < InventoryPanel.Cols - 1
                    && !Near(InventoryPanel.CellCenter(i + 1).x - c.x, InventoryPanel.CellW)) overlap = true;
                if (i + InventoryPanel.Cols < InventoryPanel.CellCount
                    && !Near(c.y - InventoryPanel.CellCenter(i + InventoryPanel.Cols).y, InventoryPanel.CellH))
                    overlap = true;
            }
            Check("40 格全部落在面板矩形内", outside.Length == 0, outside.Length == 0 ? "40/40 在界内" : outside);
            Check("相邻格步进 = 格宽/格高（行优先、不重叠）", !overlap, "步进校验");

            // 装备槽：矩形必须 == 原版 prefab 节点值 ×1.8（逐槽对表；同时断言在面板内、互不重叠）
            var invoked = 0;
            var mismatch = new List<string>();
            var equipOutside = new List<string>();
            for (var i = 0; i < InventoryPanel.EquipSlots.Length; i++)
            {
                var got = InventoryPanel.EquipSlots[i];
                if (!TryFindOrig(got.slot, got.slotIndex, out var srcCenter, out var srcSize))
                {
                    mismatch.Add($"{got.slot}#{got.slotIndex}(原版表里没有)");
                    continue;
                }
                invoked++;
                if (!NearV(got.center, srcCenter * K) || !NearV(got.size, srcSize * K))
                    mismatch.Add($"{got.slot}#{got.slotIndex}={got.center}/{got.size} ≠ {srcCenter * K}/{srcSize * K}");

                var r = RectAt(InventoryPanel.PanelPos + got.center, got.size);
                if (!InPanel(r, InventoryPanel.PanelPos, InventoryPanel.PanelSize))
                    equipOutside.Add($"{got.slot}#{got.slotIndex}={r}");
            }
            Check("10 个装备槽的「中心/尺寸」== 原版 prefab 节点值 ×1.8（rarm/head/neck/tors/larm/glov/rrin/belt/lrin/feet）",
                invoked == 10 && mismatch.Count == 0,
                mismatch.Count == 0 ? "10/10 命中原版值" : string.Join(", ", mismatch.ToArray()));
            Check("10 个装备槽都在面板矩形内", equipOutside.Count == 0,
                equipOutside.Count == 0 ? "10/10 在界内" : string.Join(", ", equipOutside.ToArray()));
            Check("装备槽两两不重叠",
                NoOverlapEquip(), "10 槽两两判定");

            Check("底部三节点 = 原版 GoldButton(20×17 @−65.5,−184.1) / CloseButton(32×31 @−125.8,−184.1) "
                  + "/ GoldText(87.1×15.2 @−7,−183.2)，均 ×1.8",
                NearV(InventoryPanel.GoldButtonPos, new Vector2(-65.5f * K, -184.1f * K))
                && NearV(InventoryPanel.GoldButtonSize, new Vector2(20f * K, 17f * K))
                && NearV(InventoryPanel.CloseButtonPos, new Vector2(-125.8f * K, -184.1f * K))
                && NearV(InventoryPanel.CloseButtonSize, new Vector2(32f * K, 31f * K))
                && NearV(InventoryPanel.GoldTextPos, new Vector2(-7f * K, -183.2f * K))
                && NearV(InventoryPanel.GoldTextSize, new Vector2(87.1f * K, 15.2f * K)),
                $"goldBtn@ {InventoryPanel.GoldButtonPos} closeBtn@ {InventoryPanel.CloseButtonPos}");

            Check("原版 InventoryPanel.prefab **没有**「4 格腰带行」（原版腰带只在底部控制面板上）",
                !HasField(typeof(InventoryPanel), "BeltCell") && !HasField(typeof(InventoryPanel), "BeltY"),
                "已按原版删除（见 InventoryPanel.cs 文件头 ④）");

            {
                var r11 = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = 1, gridH = 1 });
                var r24 = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = 2, gridH = 4 });
                var r23 = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = 2, gridH = 3 });
                var r21 = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = 2, gridH = 1 });

                Check("1×1 物品图标 = 恰好 1 格、偏移 0（空格/药水不放大）",
                    NearV(r11.size, new Vector2(InventoryPanel.CellW, InventoryPanel.CellH))
                    && NearV(r11.offset, Vector2.zero),
                    $"size={r11.size} offset={r11.offset}");
                Check("2×4 大物品图标 = 2 格宽 × 4 格高（不是缩在 1 格里）",
                    NearV(r24.size, new Vector2(2f * InventoryPanel.CellW, 4f * InventoryPanel.CellH))
                    && NearV(r23.size, new Vector2(2f * InventoryPanel.CellW, 3f * InventoryPanel.CellH))
                    && NearV(r21.size, new Vector2(2f * InventoryPanel.CellW, 1f * InventoryPanel.CellH)),
                    $"2x4={r24.size} 2x3={r23.size} 2x1={r21.size}");

                // 全组合：左上角与锚点格左上角重合 + 不越出格区
                var bad = new List<string>();
                for (var w = 1; w <= 3; w++)
                {
                    for (var h = 1; h <= 4; h++)
                    {
                        var rc = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = w, gridH = h });
                        if (!Near(rc.size.x, w * InventoryPanel.CellW) || !Near(rc.size.y, h * InventoryPanel.CellH))
                            bad.Add($"size {w}x{h}");
                        var gridL = InventoryPanel.GridOrigin.x;
                        var gridT = InventoryPanel.GridOrigin.y;
                        var gridR = gridL + InventoryPanel.Cols * InventoryPanel.CellW;
                        var gridB = gridT - InventoryPanel.Rows * InventoryPanel.CellH;
                        for (var y = 0; y + h <= InventoryPanel.Rows; y++)
                        {
                            for (var x = 0; x + w <= InventoryPanel.Cols; x++)
                            {
                                var anchor = InventoryPanel.CellCenter(y * InventoryPanel.Cols + x);
                                var cx = anchor.x + rc.offset.x;
                                var cy = anchor.y + rc.offset.y;
                                if (!Near(cx - rc.size.x * 0.5f, anchor.x - InventoryPanel.CellW * 0.5f)
                                    || !Near(cy + rc.size.y * 0.5f, anchor.y + InventoryPanel.CellH * 0.5f))
                                    bad.Add($"align {w}x{h}@{x},{y}");
                                if (cx - rc.size.x * 0.5f < gridL - 0.01f || cx + rc.size.x * 0.5f > gridR + 0.01f
                                    || cy + rc.size.y * 0.5f > gridT + 0.01f || cy - rc.size.y * 0.5f < gridB - 0.01f)
                                    bad.Add($"outside {w}x{h}@{x},{y}");
                            }
                        }
                    }
                }
                Check("图标块左上角与锚点格左上角重合、且不越出 10×4 格区（w 1..3 × h 1..4 × 全部可放锚点）",
                    bad.Count == 0, bad.Count == 0 ? "全组合通过" : string.Join(", ", bad.ToArray()));

                var over = InventoryPanel.ItemIconRect(new Diablo2.Def.ItemStack { gridW = 12, gridH = 9 });
                Check("配表越界的占格（12×9）被夹到格区 10×4（并 Warn），不越出面板",
                    NearV(over.size, new Vector2(InventoryPanel.Cols * InventoryPanel.CellW,
                        InventoryPanel.Rows * InventoryPanel.CellH)),
                    over.size.ToString());
            }
        }

        /// <summary>按槽位取**原版 prefab 里的中心/尺寸**（未 ×1.8；找不到返回 false）。</summary>
        private static bool TryFindOrig(Diablo2.Def.ItemSlot slot, int idx, out Vector2 center, out Vector2 size)
        {
            center = Vector2.zero;
            size = Vector2.zero;

            var node = NodeOf(slot, idx);
            var t = UiLayoutGame.InvEquipOrig;
            for (var i = 0; i < t.Length; i++)
            {
                if (t[i].node != node) continue;
                center = t[i].center;
                size = t[i].size;
                return true;
            }
            return false;
        }

        private static string NodeOf(Diablo2.Def.ItemSlot slot, int idx)
        {
            switch (slot)
            {
                case Diablo2.Def.ItemSlot.Weapon: return "rarm";
                case Diablo2.Def.ItemSlot.Helm: return "head";
                case Diablo2.Def.ItemSlot.Amulet: return "neck";
                case Diablo2.Def.ItemSlot.Armor: return "tors";
                case Diablo2.Def.ItemSlot.Shield: return "larm";
                case Diablo2.Def.ItemSlot.Gloves: return "glov";
                case Diablo2.Def.ItemSlot.Belt: return "belt";
                case Diablo2.Def.ItemSlot.Boots: return "feet";
                case Diablo2.Def.ItemSlot.Ring: return idx == 0 ? "rrin" : "lrin";
                default: return "?";
            }
        }

        private static bool NoOverlapEquip()
        {
            var s = InventoryPanel.EquipSlots;
            for (var i = 0; i < s.Length; i++)
                for (var j = i + 1; j < s.Length; j++)
                {
                    var a = RectAt(InventoryPanel.PanelPos + s[i].center, s[i].size);
                    var b = RectAt(InventoryPanel.PanelPos + s[j].center, s[j].size);
                    if (!Separated(a, b)) return false;
                }
            return true;
        }

        private static bool InPanel(Rect r, Vector2 center, Vector2 size)
        {
            var p = RectAt(center, size);
            return r.xMin >= p.xMin - 0.01f && r.xMax <= p.xMax + 0.01f
                && r.yMin >= p.yMin - 0.01f && r.yMax <= p.yMax + 0.01f;
        }

        private static bool HasField(Type t, string name)
            => t.GetField(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) != null;

        // ═════════════════════════════════════════════════════════════════════
        // ③ 属性 —— == CharstatPanel.prefab 实测值 × 1.8
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckCharstat()
        {
            Check("属性面板尺寸 = 原版 Panel 320×432 ×1.8", NearV(CharacterPanel.PanelSize, new Vector2(320f * K, 432f * K)),
                CharacterPanel.PanelSize.ToString());
            Check("属性面板中心 = 原版 Panel(pivot 1,0.5 @ 0,0) 的矩形中心 (−160,0) ×1.8 ⇒ **贴屏幕中线左侧**",
                NearV(CharacterPanel.PanelPos, new Vector2(-160f * K, 0f)), CharacterPanel.PanelPos.ToString());

            // 行位置：`UiLayoutGame` 里存的是**原版值**（未 ×1.8），尺寸有的是 ×1.8 后的具名常量 —— 逐类断言
            var ok = UiLayoutGame.CharStatRowOrig.Length == 4 && UiLayoutGame.CharDerivedRowOrig.Length == 4;
            ok &= NearV(UiLayoutGame.CharNamePos, new Vector2(-63.1f * K, 193.2f * K))
                  && NearV(UiLayoutGame.CharNameSize, new Vector2(172.1f * K, 27.8f * K));
            ok &= NearV(UiLayoutGame.CharStatRowOrig[0], new Vector2(-115.4f, 119.1f))
                  && NearV(UiLayoutGame.CharStatRowOrig[3], new Vector2(-115.4f, -90.5f))
                  && NearV(UiLayoutGame.CharStatRowSize, new Vector2(74.3f * K, 27.9f * K));
            ok &= NearV(UiLayoutGame.CharDerivedRowOrig[0], new Vector2(56.7f, 9.5f))
                  && NearV(UiLayoutGame.CharDefenseSize, new Vector2(109.1f * K, 28.3f * K))
                  && NearV(UiLayoutGame.CharDerivedRowOrig[1], new Vector2(38.5f, -28.6f))
                  && NearV(UiLayoutGame.CharDerivedRowOrig[2], new Vector2(38.5f, -52.8f))
                  && NearV(UiLayoutGame.CharDerivedRowOrig[3], new Vector2(38.5f, -90.8f))
                  && NearV(UiLayoutGame.CharDerivedSize, new Vector2(74.3f * K, 27.9f * K));
            ok &= NearV(UiLayoutGame.CharClosePos, new Vector2(-15.4f * K, -188.3f * K))
                  && NearV(UiLayoutGame.CharCloseSize, new Vector2(32f * K, 31f * K));
            // 2026 修正（**原断言 128.5 是过期的自造值**）：
            //   原版 `CharstatPanel.prefab` 里**根本没有加点箭头节点**（该 prefab 只有 CharName /
            //   四维 4 行 / 派生 4 行 / CloseButton / DefenseLabel —— 逐节点解析过，见本文件头「依据文件」），
            //   所以唯一依据是**底图** `D2/UI/Panel/charstat.png`(320×432)：四维行右侧的三角槽实测
            //   art x ≈ 121..125（中心 ≈122.5），行中心 art x = 160 + (−115.4) = 44.6
            //   ⇒ 相对行中心的偏移 ≈ **75.4**（`UiLayoutGame.CharPlusX` 就是这个实测值）。
            //   128.5 在这两处依据里都找不到出处（128.5 ≈ 面板中线 160 − 31.5）。按"能指出出处"的口径校准。
            ok &= Near(UiLayoutGame.CharPlusX, 75.4f * K)
                  && NearV(UiLayoutGame.CharPlusSize, new Vector2(15f * K, 24f * K));
            Check("属性行逐条 == 原版 CharName/四维 4 行/派生 4 行/CloseButton 的节点值 ×1.8",
                ok, $"四维行 {UiLayoutGame.CharStatRowOrig.Length}、派生行 {UiLayoutGame.CharDerivedRowOrig.Length}");

            // 原版自己的行框：Stamina(−28.6,h27.9) 与 Life(−52.8,h27.9) 在 y 上重合 3.7px（原版数据如此），
            // 所以这里**不判"两两不重叠"**，只判"都在面板内"+"行中心互不相同"。
            var rowRects = new List<Rect>();
            for (var i = 0; i < UiLayoutGame.CharStatRowOrig.Length; i++)
                rowRects.Add(RectAt(CharacterPanel.PanelPos + UiLayoutGame.CharStatRowOrig[i] * K,
                    UiLayoutGame.CharStatRowSize));
            for (var i = 0; i < UiLayoutGame.CharDerivedRowOrig.Length; i++)
                rowRects.Add(RectAt(CharacterPanel.PanelPos + UiLayoutGame.CharDerivedRowOrig[i] * K,
                    i == 0 ? UiLayoutGame.CharDefenseSize : UiLayoutGame.CharDerivedSize));

            var dup = false;
            for (var i = 0; i < rowRects.Count; i++)
                for (var j = i + 1; j < rowRects.Count; j++)
                    if (Near(rowRects[i].center.x, rowRects[j].center.x) && Near(rowRects[i].center.y, rowRects[j].center.y))
                        dup = true;
            Check("原版 9 个行矩形中心互不相同（原版 Stamina/Life 的框本身 y 上重合 3.7px，故不判两两不重叠）",
                !dup, "9 行");

            var inside = true;
            foreach (var r in rowRects)
                if (!InPanel(r, CharacterPanel.PanelPos, CharacterPanel.PanelSize)) inside = false;
            Check("原版 9 个行矩形都在面板内", inside, "9/9 在界内");

            Check("补齐行（等级/经验/技能点/命中/格挡/四系抗性）落在原版**空框/空白区**内、不压原版行",
                UiLayoutGame.CharBottomRightOrig.Length == 2
                && UiLayoutGame.CharResistRowOrig.Length == 4
                && !OverlapsAny(UiLayoutGame.CharTopRightPos, UiLayoutGame.CharTopRightSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharBand2MidPos, UiLayoutGame.CharBand2MidSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharBand2RightPos, UiLayoutGame.CharBand2RightSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharBottomRightOrig[0] * K, UiLayoutGame.CharBottomRightSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharBottomRightOrig[1] * K, UiLayoutGame.CharBottomRightSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharResistRowOrig[0] * K, UiLayoutGame.CharResistRowSize, rowRects)
                && !OverlapsAny(UiLayoutGame.CharResistRowOrig[3] * K, UiLayoutGame.CharResistRowSize, rowRects),
                "右上凹槽 / 第二排中·右空框 / 右下两细框 / 左下空白区（本项目新增，原版 prefab 无这些行）");

            // 新增的 3 个空框必须在**面板内**（旧 CharTopRight 尺寸 150×26 时右端已到面板边缘，
            Check("新增空框（右上凹槽 / 第二排中·右）都在面板矩形内",
                InPanel(RectAt(CharacterPanel.PanelPos + UiLayoutGame.CharTopRightPos, UiLayoutGame.CharTopRightSize),
                    CharacterPanel.PanelPos, CharacterPanel.PanelSize)
                && InPanel(RectAt(CharacterPanel.PanelPos + UiLayoutGame.CharBand2MidPos, UiLayoutGame.CharBand2MidSize),
                    CharacterPanel.PanelPos, CharacterPanel.PanelSize)
                && InPanel(RectAt(CharacterPanel.PanelPos + UiLayoutGame.CharBand2RightPos, UiLayoutGame.CharBand2RightSize),
                    CharacterPanel.PanelPos, CharacterPanel.PanelSize),
                $"右上 {UiLayoutGame.CharTopRightPos}+{UiLayoutGame.CharTopRightSize}、"
                + $"中 {UiLayoutGame.CharBand2MidPos}、右 {UiLayoutGame.CharBand2RightPos}");

            // ═════════════════════════════════════════════════════════════════
            //
            // 出处 = `client/Assets/Resources/Clover/D2/UI/Panel/charstat.png`（320×432）
            //   **逐像素暗色连通域实测**（量法脚本已按设计退役、不在仓库里；口径见下），
            //   读数落一次性留档）：
            //     四维行：标签隔间 art 11..74、亮分隔条 75..79、**数值隔间 art 80..112**（中心 96）
            //     派生行 Defense：标签 161..269、**数值 271..309**（中心 290）
            //     派生行 耐力/生命/法力：标签 161..229、**两格数值 231..270 + 272..309**
            //   面板中心 = art (160,216) ⇒ node = art − 160（与 `CharStatRowOrig` 同坐标）。
            // ═════════════════════════════════════════════════════════════════
            Check("**数值列** = 底图凹槽实测中心（四维 art 80..112 / 派生 art 271..309 / cur-max 跨 art 231..309）",
                Near(UiLayoutGame.CharStatValueX, 96f - 160f) && Near(UiLayoutGame.CharStatValueW, 33f)
                && Near(UiLayoutGame.CharDefValueX, 290f - 160f) && Near(UiLayoutGame.CharDefValueW, 39f)
                && Near(UiLayoutGame.CharCurMaxX, (231f + 309f) / 2f - 160f) && Near(UiLayoutGame.CharCurMaxW, 79f),
                $"四维 x={UiLayoutGame.CharStatValueX:0.##} w={UiLayoutGame.CharStatValueW:0.##}；"
                + $"派生 x={UiLayoutGame.CharDefValueX:0.##} w={UiLayoutGame.CharDefValueW:0.##}；"
                + $"cur/max x={UiLayoutGame.CharCurMaxX:0.##} w={UiLayoutGame.CharCurMaxW:0.##}");

            // 行框净高与文字 y 修正（文字必须落在凹槽内、不压行框下沿金线）
            Check("行框净高 = 底图实测 art 18；文字 y 修正 = (prefab 矩形高 27.9 − 18)/2 = 4.95",
                Near(UiLayoutGame.CharRowSlotH, 18f)
                && Near(UiLayoutGame.CharRowTextDy, (27.9f - 18f) / 2f),
                $"slotH={UiLayoutGame.CharRowSlotH} textDy={UiLayoutGame.CharRowTextDy:0.###}");

            // ── 判据自检（**退化样本**，不许恒真）─────────────────────────────
            var oldValueX = -115.4f + 0.29f * 74.3f;
            Check("退化样本：旧数值列（标签矩形 + 0.29×宽）≠ 底图数值隔间中心（判据真的在判列位）",
                Math.Abs(oldValueX - UiLayoutGame.CharStatValueX) > 20f,
                $"旧 {oldValueX:0.##} vs 新 {UiLayoutGame.CharStatValueX:0.##}"
                + $"（差 {Math.Abs(oldValueX - UiLayoutGame.CharStatValueX):0.##} 原版px）");

            // ═════════════════════════════════════════════════════════════════
            //   本宿主（离线）判**构建口径**（面板实例要 Unity 运行时，判不到）：
            //     · 关闭节点必须用**可见的**兜底色（`UiArt.ButtonBg`，alpha 0.94 > 0.9）
            //     · 必须有原版按钮帧 + 「关闭」文案（不许自画 X）
            //     · 几何仍是原版 prefab 的 32×31（上面已逐条断言）
            // ═════════════════════════════════════════════════════════════════
            //   本判据读的是**源码文本** ⇒ 有两个必须堵的窟窿：
            //     ① **假红**：注释里出现同一个词就会被算成"代码里有"（实证：文件头注释写了
            //        `editor_play` ⇒ "必须在取锁之后"那条静态断言报 False，而真实调用点是对的）；
            //     ② **假绿**：旧的自检写成 `oldClose.Contains("0f)")` —— 常量包含自己的子串，
            //        **恒真**，等于没判。
            //   ⇒ 现在改成：**先剥 C# 注释**（`StripCsComments`）+ **双向样本**（已知正确样本必须绿、
            //      已知错误样本必须红、只写在注释里的旧写法必须仍然绿）。别再把自检写回恒真。
            var cpPath = System.IO.Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts",
                "UI", "CharacterPanel.cs");
            var cpSrc = System.IO.File.Exists(cpPath) ? System.IO.File.ReadAllText(cpPath) : string.Empty;

            Func<string, bool> closeVisible = src =>
            {
                var code = StripCsComments(src);
                return code.Contains("UiArt.Panel(transform, \"CloseButton\"")
                       && code.Contains("UiArt.ButtonBg, true)")
                       && !code.Contains("new Color(1f, 1f, 1f, 0f), true)");
            };
            Func<string, bool> closeHasOriginalArt = src =>
            {
                var code = StripCsComments(src);
                return code.Contains("UiArt.ApplyCloseButtonArt(close, button)")
                       && !code.Contains("ResPaths.BtnMedNormal")
                       && !code.Contains("\"CloseLabel\"");
            };

            // 图形帧的来源单独判一遍（读 `UiArt.cs`）：必须是**原版方钮的具名常量**，
            //   不是字面量路径、也不是别的屏的按钮底图。
            var uiArtPath = System.IO.Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts",
                "UI", "UiArt.cs");
            var uiArtSrc = System.IO.File.Exists(uiArtPath) ? System.IO.File.ReadAllText(uiArtPath) : string.Empty;
            Func<string, bool> closeArtFromOriginalFrames = src =>
            {
                var code = StripCsComments(src);
                return code.Contains("ResPaths.PanelBuySellButtonFramePrefix")
                       && code.Contains("BuySellButtonFramePrefix + ResPaths.BuySellButtonFrameClose");
            };

            Check("关闭钮：不再是 alpha 0 的隐形命中区（构建用可见兜底色 `UiArt.ButtonBg`）",
                cpSrc.Length > 0 && closeVisible(cpSrc),
                cpSrc.Length == 0 ? "读不到 CharacterPanel.cs" : "CloseButton 构建行已换为可见底色");
            Check("关闭钮：走 `UiArt.ApplyCloseButtonArt`（原版「关闭 / 取消」图形帧）+ ⛔ 不再用前端中按钮帧、"
                + "⛔ 无压住图形的文字标签",
                cpSrc.Length > 0 && closeHasOriginalArt(cpSrc),
                "命中区 32×31 几何不动（上一条已判），本判只判可见化与素材来源");
            Check("关闭钮图形帧 = 原版方钮 `PANEL/buysellbtn.DC6` 帧 10（常态）/ 11（按下）："
                + "`UiArt` 从 `ResPaths` 的具名常量取帧（⛔ 无字面量路径）",
                uiArtSrc.Length > 0 && closeArtFromOriginalFrames(uiArtSrc),
                uiArtSrc.Length == 0 ? "读不到 UiArt.cs" : "帧号与路径前缀都来自 `ResPaths` 常量");

            // ── 双向自检（三条缺一不可，任何一条恒真/恒假都说明判据坏了）──────────
            // A. 已知**错**样本：把旧写法作为**代码**注入 ⇒ 同一条判据必须变红
            var badSrc = cpSrc + "\n        var __old_close = new Color(1f, 1f, 1f, 0f), true);\n";
            Check("自检 A（已知错样本）：注入旧的 alpha 0 代码行 ⇒ `closeVisible` 必须变红",
                cpSrc.Length > 0 && closeVisible(cpSrc) && !closeVisible(badSrc),
                "正确样本绿 / 错误样本红 = 判据真的在判可见性");
            // B. **假红防护**：把同一串只写在**注释**里 ⇒ 必须仍然绿（证明剥注释生效）
            var commentSrc = cpSrc + "\n        // 旧写法曾是：new Color(1f, 1f, 1f, 0f), true)\n";
            Check("自检 B（假红防护）：同一串只出现在注释里 ⇒ 判据必须仍然绿（剥注释生效）",
                cpSrc.Length > 0 && closeVisible(commentSrc),
                "上一版会把注释当代码 ⇒ 假红；本版先剥注释");
            // C. 素材来源两条也要能红（三个方向：不绑图形帧 / 用文字标签压住图形 / 帧常量被改掉）
            var noArtSrc = cpSrc.Replace("UiArt.ApplyCloseButtonArt(close, button)", "");
            Check("自检 C-1：删掉 `UiArt.ApplyCloseButtonArt(close, button)`（不绑原版图形帧）⇒ 素材那条必须变红",
                cpSrc.Length > 0 && noArtSrc != cpSrc
                && closeHasOriginalArt(cpSrc) && !closeHasOriginalArt(noArtSrc),
                "正确样本绿 / 错误样本红");
            var withLabelSrc = cpSrc + "\n        var __old_label = UiArt.Label(transform, \"CloseLabel\", \"关闭\", "
                + "28, TextAnchor.MiddleCenter, UiArt.ButtonText, UiLayoutGame.CharCloseSize, PanelPos);\n";
            Check("自检 C-2：把旧写法（`CloseLabel` 文字压在关闭图形上）作为**代码**注入 ⇒ 素材那条必须变红",
                cpSrc.Length > 0 && closeHasOriginalArt(cpSrc) && !closeHasOriginalArt(withLabelSrc),
                "正确样本绿 / 错误样本红");
            var renamedFrameSrc = uiArtSrc.Replace("ResPaths.BuySellButtonFrameClose",
                "ResPaths.BuySellButtonFrameRenamed");
            Check("自检 C-3：把 `UiArt` 里的帧常量名改掉 ⇒ 「帧来自 ResPaths 具名常量」那条必须变红",
                uiArtSrc.Length > 0 && renamedFrameSrc != uiArtSrc
                && closeArtFromOriginalFrames(uiArtSrc) && !closeArtFromOriginalFrames(renamedFrameSrc),
                "正确样本绿 / 错误样本红");

            // ═════════════════════════════════════════════════════════════════
            //   控件级悬浮提示（**只覆盖这两个有原版 `CloseButton` 节点的面板**）
            //   出处：prefab 的 `Tooltip` 组件（`text: Close`）/ 参考实现 `Tooltip.cs` 的落点口径
            //   `(rect.center.x, rect.yMax)` / 官方中文串表 id 4167+4168 ⇒ 原版确有这件机制。
            // ═════════════════════════════════════════════════════════════════
            var invPath = System.IO.Path.Combine(Program.ProjectRoot, "client", "Assets", "Scripts", "UI",
                "InventoryPanel.cs");
            var invSrc = System.IO.File.Exists(invPath) ? System.IO.File.ReadAllText(invPath) : string.Empty;

            Func<string, bool> hoverTipWired = src =>
            {
                var code = StripCsComments(src);
                return code.Contains("ControlTip.Create(transform, close.rectTransform, WaypointPanel.CloseText)")
                       && code.Contains("AddComponent<HoverTarget>()")
                       && code.Contains("OnEnter = _closeTip.Show")
                       && code.Contains("OnExit = _closeTip.Hide")
                       && code.Contains("_closeTip?.Hide();");
            };

            Check("关闭钮 hover 提示：两个面板都接线（`ControlTip` + `HoverTarget`，文案 = 既有常量 "
                + "`WaypointPanel.CloseText`，面板 `OnClose` 收一次）",
                cpSrc.Length > 0 && invSrc.Length > 0 && hoverTipWired(cpSrc) && hoverTipWired(invSrc),
                $"CharacterPanel={hoverTipWired(cpSrc)} InventoryPanel={hoverTipWired(invSrc)}");

            var tipTop = ControlTip.TopCenterOf(new Vector2(10f, 20f), 30f);
            var tipZero = ControlTip.TopCenterOf(new Vector2(10f, 20f), 0f);
            var tipNeg = ControlTip.TopCenterOf(new Vector2(10f, 20f), -8f);
            Check("hover 提示落点 = 锚控件**顶边中点**（口径 = 参考实现 `Tooltip.cs` 的 "
                + "`new Vector2(rect.center.x, rect.yMax)`）；高度 0 / 负 ⇒ 不加偏移",
                tipTop == new Vector2(10f, 35f) && tipZero == new Vector2(10f, 20f) && tipNeg == new Vector2(10f, 20f),
                $"h=30 ⇒ {tipTop}（期望 (10.0, 35.0)）；h=0 ⇒ {tipZero}；h=-8 ⇒ {tipNeg}");

            // 退化样本：删接线 / 文案换成自造字面量 / 去掉"非正高度不偏移"这条守卫
            var noTipSrc = cpSrc.Replace("AddComponent<HoverTarget>()", "");
            Check("自检 D-1：删掉关闭钮的 hover 接线 ⇒ 接线那条必须变红",
                cdCheckDiff(cpSrc, noTipSrc) && hoverTipWired(cpSrc) && !hoverTipWired(noTipSrc),
                "正确样本绿 / 错误样本红");
            var literalTextSrc = cpSrc.Replace("WaypointPanel.CloseText", "\"关闭\"");
            Check("自检 D-2：文案由既有常量换写成自造字面量 ⇒ 接线那条必须变红",
                cdCheckDiff(cpSrc, literalTextSrc) && hoverTipWired(cpSrc) && !hoverTipWired(literalTextSrc),
                "正确样本绿 / 错误样本红");
            Func<Vector2, float, Vector2> noGuard = (c, h) => new Vector2(c.x, c.y + h * 0.5f);
            Check("自检 D-3：去掉「非正高度不加偏移」的同口径实现喂进**同一量法** ⇒ 必须被认出（h=-8 时不等）",
                noGuard(new Vector2(10f, 20f), -8f) != tipNeg,
                $"无守卫实现 h=-8 ⇒ {noGuard(new Vector2(10f, 20f), -8f)}；真值 ⇒ {tipNeg}");
        }

        /// <summary>退化样本是否真的改到了源码（防"变红来自别的原因"）。</summary>
        private static bool cdCheckDiff(string a, string b) => !string.IsNullOrEmpty(a) && a != b;

        /// <summary>
        /// 剥掉 C# 的行注释（`//…`）与块注释（`/*…*/`）。
        /// <para>为什么静态文本判据必须先剥（团队级实证 2026-09-24）：原文里搜关键词时，
        /// **注释里出现的同一个词会被当成代码** ⇒ 判据**假红**（让别的片去修一个不存在的 bug）；
        /// 反向也会**假绿**（把旧写法注释掉就"通过"）。⇒ 剥离后再搜，且配**双向样本**自检。</para>
        /// <para>★ 2026-09-24（team-lead 指派 §5.2 第 34 条）：本 helper 从 `private` 升为
        /// <c>internal</c>，作为**本宿主唯一的剥注释实现**供 `Program.cs` 复用 ——
        /// 不要再在别的 Check 类里写第 N 份副本（宿主里已各有 `StripComments`×3 / `CodeOnly`×1）。</para>
        /// </summary>
        internal static string StripCsComments(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = System.Text.RegularExpressions.Regex.Replace(s, @"/\*.*?\*/", " ",
                System.Text.RegularExpressions.RegexOptions.Singleline);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"//[^\n]*", " ");
            return s;
        }

        private static bool OverlapsAny(Vector2 centerRel, Vector2 size, List<Rect> rows)
        {
            var r = RectAt(CharacterPanel.PanelPos + centerRel, size);
            foreach (var o in rows)
                if (!Separated(r, o)) return true;
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // ④ 技能树 —— **版面来自原版底图像素**（`Def/SkillTreeLayout.cs` 是生成物）
        //    生成器：`tools/d2codec/export_skilltree_layout.py`（从
        //    `Panel/skltree_{cls}_back_{0..3}.png` 逐像素解析 + 与原版 `skilldesc.txt` 对账 15/15）。
        //    口径：面板 = 原版 320×432 ×1.8 = 576×777.6；节点框 = 原版画出的框（45×50 原版px）；
        //          技能图标 = **原版位图原生 48×48**（×1.8 = 86.4；w3 审计由"框内径 41×46"改来，
        //          理由与出处见 `UiLayoutGame.SkillIconCell`：节点处画的是 L 形管线不是插座方框，
        //          把 48×48 缩到 41 会让图标小 15% 且让管线露在图标外一圈）。
        //    上一版那套「由原版图标尺寸反推」的常量（SkillNodeSize / SkillNodeX/Y /
        //    本节的断言只覆盖**结构不变量**（表自洽、网格值、页签槽、说明区）；
        //       「框的位置确实等于底图画的那条线」由**生成器**对着 PNG 逐像素判（它不一致就 abort）。
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckSkill()
        {
            Check("技能树面板 = 原版底图一页 320×432 ×1.8 = 576×777.6",
                Near(SkillTreePanel.PanelSize.x, 320f * K) && Near(SkillTreePanel.PanelSize.y, 432f * K),
                SkillTreePanel.PanelSize.ToString());
            Check("技能树面板居中（pos = 0）",
                Near(SkillTreePanel.PanelPos.x, 0f) && Near(SkillTreePanel.PanelPos.y, 0f),
                SkillTreePanel.PanelPos.ToString());
            Check("技能树面板高 ≤ 画布高 1080（否则上下被裁）",
                SkillTreePanel.PanelSize.y <= UiArt.RefHeight, $"高 = {SkillTreePanel.PanelSize.y:0.#} ≤ 1080");

            Check("技能图标层 = 原版**位图原生 48×48** ×1.8 = 86.4×86.4（★ w3 审计由框内径 41×46 改来）",
                Near(SkillTreePanel.IconCellSize.x, UiLayoutGame.SkillIconArtPx * K)
                && Near(SkillTreePanel.IconCellSize.y, UiLayoutGame.SkillIconArtPx * K)
                && NearV(SkillTreePanel.IconCellSize, UiLayoutGame.SkillIconCell),
                SkillTreePanel.IconCellSize.ToString());

            Check("版面表：150 条（5 职业 × 3 系 × 每系 10）",
                SkillTreeLayout.Cells.Length == 150, $"Cells={SkillTreeLayout.Cells.Length}");

            Check("版面表：每条 = 框 45×50 原版px 且落在实测网格（列 x 13/82/150、行 y 15/82/152/220/287/356）",
                AllCellsOnGrid(), "150/150 在网格上");

            Check("版面表：5 职业 × 3 系 各 10 格",
                AllCountsTen(), "CountOf(cls,tree) 全 = 10");

            Check("同一系内 10 个节点框两两不重叠",
                NoOverlapWithinTree(), "15/15 系内 0 冲突");

            Check("图标中心 == 框中心（本项目映射下，原版px 偏差 = 0；判据要求 ≤1）",
                IconCenterDelta() <= 1f, $"{IconCenterDelta():0.####} 原版px");

            Check("3 个系页签槽 = 系 1→下槽(y328) / 系 2→中槽(y220) / 系 3→上槽(y112)（实测缺口 + 高亮两条特征互证）",
                SkillTreeLayout.TabSlots.Length == 3
                && Near(SkillTreeLayout.TabSlots[0].y, 328f, 0.5f)
                && Near(SkillTreeLayout.TabSlots[1].y, 220f, 0.5f)
                && Near(SkillTreeLayout.TabSlots[2].y, 112f, 0.5f)
                && Near(SkillTreeLayout.TabSlots[0].x, 235f, 0.5f)
                && Near(SkillTreeLayout.TabSlots[0].w, 82f, 0.5f)
                && Near(SkillTreeLayout.TabSlots[0].h, 100f, 0.5f),
                $"{SkillTreeLayout.TabSlots[0].y}/{SkillTreeLayout.TabSlots[1].y}/{SkillTreeLayout.TabSlots[2].y}");

            Check("3 个系页签槽两两不重叠（都在右列）",
                Separated(TabRect(0), TabRect(1)) && Separated(TabRect(1), TabRect(2)),
                "上/中/下三槽分离");

            Check("说明窗（黑窗内区）在木框可见区内",
                Inside(SkillTreeLayout.DescWindow, UiLayoutGame.SkillInfoBox),
                $"DescWindow=({SkillTreeLayout.DescWindow.x},{SkillTreeLayout.DescWindow.y},"
                + $"{SkillTreeLayout.DescWindow.w},{SkillTreeLayout.DescWindow.h})");

            Check("说明区可见区（木框内缩金饰条 4px）在面板内",
                Near(UiLayoutGame.SkillInfoBox.x, SkillTreeLayout.WoodFrame.x + SkillTreeLayout.WoodTrim)
                && Near(UiLayoutGame.SkillInfoBox.w,
                    SkillTreeLayout.WoodFrame.w - 2f * SkillTreeLayout.WoodTrim)
                && UiLayoutGame.SkillInfoBox.x >= 0f
                && UiLayoutGame.SkillInfoBox.y >= 0f
                && UiLayoutGame.SkillInfoBox.x + UiLayoutGame.SkillInfoBox.w <= SkillTreeLayout.PageW
                && UiLayoutGame.SkillInfoBox.y + UiLayoutGame.SkillInfoBox.h <= SkillTreeLayout.PageH,
                $"({UiLayoutGame.SkillInfoBox.x},{UiLayoutGame.SkillInfoBox.y},"
                + $"{UiLayoutGame.SkillInfoBox.w},{UiLayoutGame.SkillInfoBox.h})");
        }

        /// <summary>节点框是否全部落在实测网格上（列 x 与行 y 都要对得上）。</summary>
        private static bool AllCellsOnGrid()
        {
            var colX = new[] { 13f, 82f, 150f };
            var rowY = new[] { 15f, 82f, 152f, 220f, 287f, 356f };
            foreach (var c in SkillTreeLayout.Cells)
            {
                if (!Near(c.box.w, 45f, 0.01f) || !Near(c.box.h, 50f, 0.01f)) return false;
                if (c.cls < 1 || c.cls > 5 || c.tree < 1 || c.tree > 3) return false;
                if (c.row < 1 || c.row > 6 || c.col < 1 || c.col > 3) return false;
                if (!Near(c.box.x, colX[c.col - 1], 0.01f)) return false;
                if (!Near(c.box.y, rowY[c.row - 1], 0.01f)) return false;
            }
            return true;
        }

        private static bool AllCountsTen()
        {
            for (var cls = 1; cls <= 5; cls++)
                for (var tree = 1; tree <= 3; tree++)
                    if (SkillTreeLayout.CountOf(cls, tree) != 10) return false;
            return true;
        }

        private static bool NoOverlapWithinTree()
        {
            for (var cls = 1; cls <= 5; cls++)
                for (var tree = 1; tree <= 3; tree++)
                {
                    var list = new List<Rect>();
                    foreach (var c in SkillTreeLayout.Cells)
                        if (c.cls == cls && c.tree == tree)
                            list.Add(RectAt(new Vector2(c.box.x + c.box.w * 0.5f, c.box.y + c.box.h * 0.5f),
                                new Vector2(c.box.w, c.box.h)));
                    for (var i = 0; i < list.Count; i++)
                        for (var j = i + 1; j < list.Count; j++)
                            if (!Separated(list[i], list[j])) return false;
                }
            return true;
        }

        /// <summary>
        /// 图标中心与框中心的**原版px**偏差（取全表最大值）。
        /// <para>w3 审计：图标层不再是"框内缩 2px"，而是**框的原生 48×48 图标居中于框中心**
        /// （`SkillTreePanel.CreateNode` 把 Icon 建成 Hit 的居中子节点、`anchoredPosition = 0`；
        /// Hit 自己居中于 `SkillTreeCell.box` 的中心）⇒ 偏差仍恒为 0。
        /// 这里保留"逐格算一遍"的形式（不是恒返回 0），因为它是**映射规则**的等价改写：
        /// 只要将来谁把图标改成相对框的偏移，这条断言就会红。</para>
        /// </summary>
        private static float IconCenterDelta()
        {
            var worst = 0f;
            foreach (var c in SkillTreeLayout.Cells)
            {
                var boxCx = c.box.x + c.box.w * 0.5f;
                var boxCy = c.box.y + c.box.h * 0.5f;
                // 图标层 = 原生 48×48、居中于框中心 ⇒ 中心 = 框中心 + (0,0)
                var iconCx = boxCx + (UiLayoutGame.SkillIconCell.x - UiLayoutGame.SkillIconArtPx * K) * 0.5f;
                var iconCy = boxCy + (UiLayoutGame.SkillIconCell.y - UiLayoutGame.SkillIconArtPx * K) * 0.5f;
                worst = Math.Max(worst, Math.Abs(iconCx - boxCx));
                worst = Math.Max(worst, Math.Abs(iconCy - boxCy));
            }
            return worst;
        }

        private static Rect TabRect(int i)
        {
            var t = SkillTreeLayout.TabSlots[i];
            return RectAt(new Vector2(t.x + t.w * 0.5f, t.y + t.h * 0.5f), new Vector2(t.w, t.h));
        }

        /// <summary>a 是否完全落在 b 内（原版 px 矩形）。</summary>
        private static bool Inside(Diablo2.Def.SkillArtRect a, Diablo2.Def.SkillArtRect b)
            => a.x >= b.x - 0.01f && a.y >= b.y - 0.01f
            && a.x + a.w <= b.x + b.w + 0.01f && a.y + a.h <= b.y + b.h + 0.01f;

        // ═════════════════════════════════════════════════════════════════════
        // ⑤ 怪物血条 / 关卡标题 —— == EnemyBar.prefab / LevelEntryTitle.prefab × 1.8
        // ═════════════════════════════════════════════════════════════════════
        private static void CheckTopBars()
        {
            Check("怪物血条 = 原版 EnemyBar 150×20 ×1.8",
                NearV(UiLayoutGame.EnemyBarSize, new Vector2(150f * K, 20f * K)),
                UiLayoutGame.EnemyBarSize.ToString());
            Check("怪物血条中心 = 顶部居中、上沿距顶 22×1.8（原版 anchor(0.5,1) pos(0,−22) pivot(0.5,1)）",
                Near(UiLayoutGame.EnemyBarPos.x, 0f)
                && Near(UiLayoutGame.EnemyBarRect().yMax, UiArt.RefHeight * 0.5f - 22f * K),
                UiLayoutGame.EnemyBarRect().ToString());
            Check("怪物血条标题内缩 = 原版 pos.y 2 / sizeDelta.y −4 的 ×1.8",
                Near(UiLayoutGame.EnemyBarTitleOffsetY, 2f * K) && Near(UiLayoutGame.EnemyBarTitleShrinkY, 4f * K),
                $"{UiLayoutGame.EnemyBarTitleOffsetY}/{UiLayoutGame.EnemyBarTitleShrinkY}");
            Check("关卡进入标题 = 满宽 × 原版 300 高、贴顶（原版 LevelEntryTitle anchor(0,1)-(1,1) size(0,300)）",
                Near(UiLayoutGame.LevelTitleH, 300f * K)
                && Near(UiLayoutGame.LevelTitleRect().yMax, UiArt.RefHeight * 0.5f)
                && Near(UiLayoutGame.LevelTitleRect().width, UiArt.RefWidth),
                UiLayoutGame.LevelTitleRect().ToString());
        }
    }
}
