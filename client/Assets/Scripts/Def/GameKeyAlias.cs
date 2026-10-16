// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/GameKeyAlias.cs
// 本项目自定义界面键：**值是 `CloverEngine.GameKey`**（引擎统一按键枚举，
// `clover-client-unity-engine/Runtime/Core/Input.cs:14`）。
//
// 为什么要这层别名：
//   ① 业务/UI 只写 `GameKeyAlias.KeyInventory`，不写 `GameKey.I` —— 原版键位集中一处，
// 改键位只改本文件（项目约定「不写裸字面量」）。
//
// 键位取值 = **暗黑破坏神 II 原版默认键位**（与原版一致是保真要求）。
// 读取一律走 `Game.Input.GetKey/GetKeyDown(GameKey)`；
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;

namespace Diablo2.Def
{
    /// <summary>本项目界面/操作键位别名（值 = <see cref="GameKey"/>）。</summary>
    public static class GameKeyAlias
    {
        // ── 游戏内面板（原版键位）───────────────────────────────────────────────
        /// <summary>背包（原版 <c>I</c>）。</summary>
        public const GameKey KeyInventory = GameKey.I;

        /// <summary>人物属性（原版 <c>C</c>）。</summary>
        public const GameKey KeyCharSheet = GameKey.C;

        /// <summary>技能树（原版 <c>T</c>）。</summary>
        public const GameKey KeySkillTree = GameKey.T;

        /// <summary>自动地图 / 小地图（原版 <c>Tab</c>）。</summary>
        public const GameKey KeyMinimap = GameKey.Tab;

        /// <summary>
        /// 暂停菜单（原版 <c>Esc</c>）。**已接线** ——
        /// 消费方 = `Module/Flow/AppFlow.EscPressed()`（Stage 站点 ESC = 暂停 / Pause 站点 ESC = 继续）。
        /// 出处：`策划/策划案/暗黑破坏神2参考规格.md:140`（原版做法 = 「ESC 菜单（继续 / 选项 /
        /// 保存并退出 / 退出）」）。定点直连 `GameKey.Escape` 会绕过别名 ⇒ 键位不再是单一来源。
        /// </summary>
        public const GameKey KeyPause = GameKey.Escape;

        /// <summary>
        /// 关闭当前面板 / 返回上一级（原版 <c>Esc</c>）。**已接线** ——
        /// 消费方 = `UI/SettingsPanel.OnUpdate`（ESC 关闭选项面板）。
        /// 其关闭键沿用原版 ESC 返回上一级的口径（同 `KeyPause` 的规格行）。
        /// </summary>
        public const GameKey KeyClosePanel = GameKey.Escape;

        //   ① Space（「对话/提示推进」）**不登记**：原版 D2 的 NPC 对话**靠点选项按钮推进**
        //      （`UI/NpcDialogPanel` 的选项列本来就是鼠标点击，原版亦无键盘推进键）；
        //   ② Enter（「确认」）**不登记**：本项目所有确认交互都是**鼠标点按钮**
        //      （`UI/D2ConfirmPanel` 的 Confirm/Cancel、`UI/CharCreatePanel` 的 OK）——
        //      原版 D2 同样没有"Enter = 确认"的键盘口径（Enter 在原版是聊天输入）。
        //   两条都**不是**"接线能解决"的：接线等于**新造一个原版没有的键位行为**。

        // ── 战斗与操作（原版键位）──────────────────────────────────────────────
        /// <summary>站立不动攻击（原版 <c>Shift</c>）。</summary>
        public const GameKey KeyStandStill = GameKey.LeftShift;

        /// <summary>显示地面物品名（原版 <c>Alt</c>）。</summary>
        public const GameKey KeyShowGroundItems = GameKey.LeftAlt;

        /// <summary>走 / 跑切换开关（原版 <c>R</c>）。</summary>
        public const GameKey KeyRunToggle = GameKey.R;

        /// <summary>
        /// 切换武器组（原版 <c>W</c>）。**保留但不接线** ——
        /// 原版 D2 **确有**双武器组切换（`W` 在第一/第二套武器之间切），但本项目**没有**双武器组系统
        /// </summary>
        public const GameKey KeySwapWeapon = GameKey.W;

        // 本文件**不登记方向键移动**：原版 D2 只有「鼠标点地面移动」（没有方向键备选移动）

        // ── 腰带（4 格药水，数字键 1~4）─────────────────────────────────────────
        /// <summary>腰带第 1 格。</summary>
        public const GameKey KeyBelt1 = GameKey.Num1;
        /// <summary>腰带第 2 格。</summary>
        public const GameKey KeyBelt2 = GameKey.Num2;
        /// <summary>腰带第 3 格。</summary>
        public const GameKey KeyBelt3 = GameKey.Num3;
        /// <summary>腰带第 4 格。</summary>
        public const GameKey KeyBelt4 = GameKey.Num4;

        // ── 技能热键（原版 F1~F8）──────────────────────────────────────────────
        //   ⚠️ 口径纠正（提问「F1-F5 原版也是这样的吗？」）：
        //   原版 `F1`~`F8` 是「**读（readies）/ 设（or sets）**关联的左右键技能」——
        //     ① **设置**：打开技能速度棒（`S`，见 `KeySkillSpeedBar`），把鼠标**悬停在技能图标上**，
        //        按 `F1`~`F8` 里的任意一个 ⇒ 该热键记住这个技能；
        //     ② **切换**：平时按 `F1`~`F8` ⇒ 把该热键记住的技能 ready 成鼠标键技能（施放仍要点鼠标）。
        //   出处：① 官方中文字串表 `原版资源/d2text/chi_string.txt:4205`
        //     「利用F1-F8鍵，來設定左側或右側的滑鼠按鈕技能。」（原版帮助画面 H 的原文）；
        //   ② 凯恩之角 `wiki.d.163.com/In_Game_control_(Diablo_II)`：「如何使用的技能设置热键：
        //     你可以设置 16 个热键来切换选择鼠标左键和右键要使用的技能。打开技能选项，把鼠标移动到
        //     你要设置的技能的图标上，然后按 "F1" 到 "F8" 键中的任何一个（另外 8 个热键需要在选项菜单中设定）」；
        //   ③ Wowhead（D2R 口径，与①同构）：「Press S to open "Skill Speed Bar" / Hover over a Skill Icon
        //     and Press any of the Function keys / Press any of the Function keys to instantly swap your
        //     right click ability」。
        //   ⛔ 本工程**旧**口径（"按 F 键 = 把**已学技能表里第 N 个**绑到左右键"、`F1`~`F4` 左 / `F5`~`F8` 右）
        //     无任何出处，已删除（见 `SkillHotkeyRequest` 的注释）。

        /// <summary>技能热键 1（原版 <c>F1</c>）。</summary>
        public const GameKey KeySkillSlot1 = GameKey.F1;
        /// <summary>技能槽 2（原版 <c>F2</c>）。</summary>
        public const GameKey KeySkillSlot2 = GameKey.F2;
        /// <summary>技能槽 3（原版 <c>F3</c>）。</summary>
        public const GameKey KeySkillSlot3 = GameKey.F3;
        /// <summary>技能槽 4（原版 <c>F4</c>）。</summary>
        public const GameKey KeySkillSlot4 = GameKey.F4;
        /// <summary>技能槽 5（原版 <c>F5</c>）。</summary>
        public const GameKey KeySkillSlot5 = GameKey.F5;
        /// <summary>技能槽 6（原版 <c>F6</c>）。</summary>
        public const GameKey KeySkillSlot6 = GameKey.F6;
        /// <summary>技能槽 7（原版 <c>F7</c>）。</summary>
        public const GameKey KeySkillSlot7 = GameKey.F7;
        /// <summary>技能热键 8（原版 <c>F8</c>）。</summary>
        public const GameKey KeySkillSlot8 = GameKey.F8;

        /// <summary>
        /// 技能热键个数 = **8**（原版默认 `F1`~`F8`）。
        /// <para>出处：官方中文字串表 `chi_string.txt:3936-3943`「技能1」~「技能8」—— 原版
        /// 「选项 → 指令」里的 8 条技能命令（默认键位 `F1`~`F8`）；凯恩之角同页说明资料片共 16 个
        /// （另外 8 个需在选项菜单设定）⇒ **默认就是 8 个**。本项目只实现默认的 8 个；
        /// 「选项菜单里再配 8 个」是按键自定义功能，本项目没有那个界面。</para>
        /// </summary>
        public const int SkillSlotCount = 8;

        /// <summary>
        /// 技能速度棒（原版 <c>S</c>）—— **设置**技能热键的入口（"打开技能选项"）。
        /// <para>出处：① 官方中文字串表 `chi_string.txt:3935`「技能速度棒」（原版「选项 → 指令」里的
        /// 键位命令项，与「技能樹」= `T` 同表）；② GameFAQs 原版 FAQ「Basic Commands: S - Opens the
        /// Skills Speed Bar」；③ Wowhead（D2R）「Press S to open "Skill Speed Bar"」。</para>
        /// </summary>
        public const GameKey KeySkillSpeedBar = GameKey.S;

        /// <summary>
        /// 选择**前一项**技能（原版鼠标滚轮向上）。
        /// <para>出处：官方中文字串表 `chi_string.txt:3944`「選擇前一項技能」（与原版「指令」表里的
        /// 技能命令同表）+ 原版手册「Mouse wheel up/down – Scrolls through all hotkeyed skills in the
        /// Right Mouse Button slot」⇒ 滚轮在**已设热键的技能**里循环切换**右键**技能。</para>
        /// </summary>
        public const GameKey KeySkillPrev = GameKey.MouseScrollUp;

        /// <summary>选择**下一项**技能（原版鼠标滚轮向下）。出处同 <see cref="KeySkillPrev"/>（串表 3945）。</summary>
        public const GameKey KeySkillNext = GameKey.MouseScrollDown;

        /// <summary>把 <c>1..8</c> 槽号（1-based）映射成键位；越界返回 <see cref="GameKey.None"/>。</summary>
        /// <param name="slot1Based">1..8</param>
        public static GameKey SkillSlotKey(int slot1Based)
        {
            switch (slot1Based)
            {
                case 1: return KeySkillSlot1;
                case 2: return KeySkillSlot2;
                case 3: return KeySkillSlot3;
                case 4: return KeySkillSlot4;
                case 5: return KeySkillSlot5;
                case 6: return KeySkillSlot6;
                case 7: return KeySkillSlot7;
                case 8: return KeySkillSlot8;
                default: return GameKey.None;
            }
        }

        /// <summary>把腰带格号（0-based，0..3）映射成键位；越界返回 <see cref="GameKey.None"/>。</summary>
        /// <param name="index0Based">0..3</param>
        public static GameKey BeltKey(int index0Based)
        {
            switch (index0Based)
            {
                case 0: return KeyBelt1;
                case 1: return KeyBelt2;
                case 2: return KeyBelt3;
                case 3: return KeyBelt4;
                default: return GameKey.None;
            }
        }
    }
}
