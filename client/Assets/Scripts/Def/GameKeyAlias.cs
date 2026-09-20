// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/GameKeyAlias.cs
// 本项目自定义界面键：**值是 `CloverEngine.GameKey`**（引擎统一按键枚举，
// `clover-client-unity-engine/Runtime/Core/Input.cs:14`）。
//
// 为什么要这层别名：
//   ① 业务/UI 只写 `GameKeyAlias.KeyInventory`，不写 `GameKey.I` —— 原版键位集中一处，
//      改键位只改本文件（`tools/ai-skill/conventions.md`「不写裸字面量」）；
//   ② `GameKey` 是引擎枚举，本项目**不许**再定义一个自己的按键枚举（会两处漂移）。
//
// 键位取值 = **暗黑破坏神 II 原版默认键位**（与原版一致是保真要求）。
// 读取一律走 `Game.Input.GetKey/GetKeyDown(GameKey)`；
// ⛔ 禁止直连 `UnityEngine.Input` / `Keyboard.current`（引擎已封装，旧输入后端下会静默失效）。
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

        /// <summary>暂停菜单（原版 <c>Esc</c>）。</summary>
        public const GameKey KeyPause = GameKey.Escape;

        /// <summary>关闭当前面板 / 返回上一级（原版 <c>Esc</c>）。</summary>
        public const GameKey KeyClosePanel = GameKey.Escape;

        /// <summary>对话/提示推进（原版 <c>Space</c>）。</summary>
        public const GameKey KeyDialogAdvance = GameKey.Space;

        /// <summary>确认（原版 <c>Enter</c>）。</summary>
        public const GameKey KeyConfirm = GameKey.Enter;

        // ── 战斗与操作（原版键位）──────────────────────────────────────────────
        /// <summary>站立不动攻击（原版 <c>Shift</c>）。</summary>
        public const GameKey KeyStandStill = GameKey.LeftShift;

        /// <summary>显示地面物品名（原版 <c>Alt</c>）。</summary>
        public const GameKey KeyShowGroundItems = GameKey.LeftAlt;

        /// <summary>走 / 跑切换开关（原版 <c>R</c>）。</summary>
        public const GameKey KeyRunToggle = GameKey.R;

        /// <summary>切换武器组（原版 <c>W</c>）。</summary>
        public const GameKey KeySwapWeapon = GameKey.W;

        // ⛔ 本文件**不登记方向键移动**：原版 D2 只有「鼠标点地面移动」（没有方向键备选移动）
        //   ⇒ 按全局 skill §0「A 没有 ⇒ 不加」，曾经那一族方向键常量已删除
        //   （删除理由与 grep 判据见 `策划/验收表.md` 自审对比段与 §本轮修掉的 bug B35）。

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
