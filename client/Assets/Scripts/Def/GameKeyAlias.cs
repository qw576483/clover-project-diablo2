// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/GameKeyAlias.cs
// 本项目自定义界面键：**值是 `CloverEngine.GameKey`**（引擎统一按键枚举，
// `clover-client-unity-engine/Runtime/Core/Input.cs:14`）。
//
// 为什么要这层别名：
//   ① 业务/UI 只写 `GameKeyAlias.KeyInventory`，不写 `GameKey.I` —— 原版键位集中一处，
// 改键位只改本文件（`tools/ai-skill/conventions.md`「不写裸字面量」）。
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

        /// <summary>任务日志（原版 <c>Q</c>）。</summary>
        public const GameKey KeyQuestLog = GameKey.Q;

        /// <summary>自动地图 / 小地图（原版 <c>Tab</c>）。</summary>
        public const GameKey KeyMinimap = GameKey.Tab;

        /// <summary>
        /// 暂停菜单（原版 <c>Esc</c>）。T0FIX-C：**已接线** ——
        /// 消费方 = `Module/Flow/AppFlow.EscPressed()`（Stage 站点 ESC = 暂停 / Pause 站点 ESC = 继续）。
        /// 出处：`策划/策划案/暗黑破坏神2参考规格.md:140`（原版做法 = 「ESC 菜单（继续 / 选项 /
        /// 保存并退出 / 退出）」）。旧写法定点改直连 `GameKey.Escape`（绕过别名）⇒ 键位不再是单一来源。
        /// </summary>
        public const GameKey KeyPause = GameKey.Escape;

        /// <summary>
        /// 关闭当前面板 / 返回上一级（原版 <c>Esc</c>）。T0FIX-C：**已接线** ——
        /// 消费方 = `UI/SettingsPanel.OnUpdate`（ESC 关闭选项面板）。
        /// 其关闭键沿用原版 ESC 返回上一级的口径（同 `KeyPause` 的规格行）。
        /// </summary>
        public const GameKey KeyClosePanel = GameKey.Escape;

        //   ① `KeyDialogAdvance`（Space）= 「对话/提示推进」：原版 D2 的 NPC 对话**靠点选项按钮推进**
        //      （`UI/NpcDialogPanel` 的选项列本来就是鼠标点击，原版亦无键盘推进键）；
        //      全仓无任何出处支持"Space 推进对话" ⇒ 删。
        //   ② `KeyConfirm`（Enter）= 「确认」：本项目所有确认交互都是**鼠标点按钮**
        //      （`UI/D2ConfirmPanel` 的 Confirm/Cancel、`UI/CharCreatePanel` 的 OK）——
        //      原版 D2 同样没有"Enter = 确认"的键盘口径（Enter 在原版是聊天输入），
        //      全仓 0 处消费且无出处 ⇒ 删。
        //   两条都**不是**"接线能解决"的：接线等于**新造一个原版没有的键位行为**。

        // ── 战斗与操作（原版键位）──────────────────────────────────────────────
        /// <summary>站立不动攻击（原版 <c>Shift</c>）。</summary>
        public const GameKey KeyStandStill = GameKey.LeftShift;

        /// <summary>显示地面物品名（原版 <c>Alt</c>）。</summary>
        public const GameKey KeyShowGroundItems = GameKey.LeftAlt;

        /// <summary>走 / 跑切换开关（原版 <c>R</c>）。</summary>
        public const GameKey KeyRunToggle = GameKey.R;

        /// <summary>
        /// 切换武器组（原版 <c>W</c>）。T0FIX-C：**保留但不接线** ——
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

        // ── 技能快捷键（原版 F1~F8 切左右键技能）───────────────────────────────
        /// <summary>技能槽 1（原版 <c>F1</c>）。</summary>
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
        /// <summary>技能槽 8（原版 <c>F8</c>）。</summary>
        public const GameKey KeySkillSlot8 = GameKey.F8;

        /// <summary>技能槽总数（F1~F8）。</summary>
        public const int SkillSlotCount = 8;

        /// <summary>
        /// 技能槽里**属于左键**的个数 = 4 ⇒ `F1`~`F4` 绑左键、`F5`~`F8` 绑右键
        /// （见 <see cref="SkillSlotIsLeftHand"/>）。
        /// <para>出处/口径（impl-I-input 落地，改动前这 8 个键**全仓 0 消费**，见审计 R4）：
        /// 原版 D2 的技能栏格与 `F1`~`F8` 同源 —— 参考工程
        /// `Diablerie/Engine/PlayerController.cs` 的 `hotSkillsBindings = {F1..F6}` +
        /// `SkillPanel.SetHotKey(i, ...)`（本项目 HUD 文件头已逐字记下这条出处，技能栏 = 6 格）；
        /// 而本项目 HUD 上可绑的**鼠标技能格只有两个**
        /// （`UiLayoutGame.LeftSkillPos` / `RightSkillPos` = 原版 `ControlPanel.prefab` 的
        /// `LeftSkill` / `RightSkill`）⇒ `F` 键分两半：前半（`F1`~`F4`）绑左键、后半（`F5`~`F8`）绑右键，
        /// 每个键绑定"**已学技能表里第 N 个**可主动施放的技能"（N = <see cref="SkillSlotIndex"/> + 1）。</para>
        /// <para>与前 6 个技能栏格的关系：`F1`~`F4` = 技能栏 1~4 格（绑左键），
        /// `F5`/`F6` = 技能栏 5/6 格（绑右键），`F7`/`F8` 本工程无对应格（alias 已登记 8 个键
        /// ⇒ 照常映射为右键第 3/4 个候选，不删键位）。</para>
        /// <para>「第 N 个已学技能」而不是"键位自带技能 id"：技能树是**职业相关**的（5 职业各一套），
        /// 键位常量里写死 id 会在换职业时错位 ⇒ 由 `Module/Skill/SkillModule` 在收到
        /// `Events.SkillSlotAssignRequest` 时按**当前职业的已学技能顺序**解析。</para>
        /// </summary>
        public const int SkillSlotLeftCount = 4;

        /// <summary>该技能槽键是否绑**左键**（`F1`~`F4` = true；`F5`~`F8` = false = 右键）。</summary>
        /// <param name="slot1Based">1..8</param>
        public static bool SkillSlotIsLeftHand(int slot1Based)
            => slot1Based >= 1 && slot1Based <= SkillSlotLeftCount;

        /// <summary>
        /// 槽号 → **已学技能表里 0 基下标**（每个鼠标键 4 个槽位）：
        /// `F1`/`F5` ⇒ 0、`F2`/`F6` ⇒ 1、`F3`/`F7` ⇒ 2、`F4`/`F8` ⇒ 3。
        /// 越界（&lt;1 或 &gt;8）返回 -1。
        /// </summary>
        /// <param name="slot1Based">1..8</param>
        public static int SkillSlotIndex(int slot1Based)
        {
            if (slot1Based < 1 || slot1Based > SkillSlotCount) return -1;
            return (slot1Based - 1) % SkillSlotLeftCount;
        }

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
