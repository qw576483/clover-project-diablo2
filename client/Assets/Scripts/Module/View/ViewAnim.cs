// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/ViewAnim.cs
// 逐帧动画的**动作枚举**（**本项目新增**）。
//
// 为什么不用引擎的 `Game.Anim`（`IAnimationManager` / `IAnimPlayer`）：
//   引擎那套是 **Animator / AnimatorController 驱动**的状态机（`Runtime/Presentation` 的
//   `IAnimPlayer.Play(state)` → `Animator.Play`），而原版暗黑2的角色/怪物动画是
//   **`.dcc` 逐帧切图**（同一动作 × 8 方向 × N 帧的 PNG 序列）。
//   引擎不覆盖"逐帧切图"这条路径（`reference/engine-mental-model.md` §4 的动画一节）⇒
//   本项目按 `docs/agents/agent-07-*.md` §4 的要求，**业务自写逐帧 Sprite 动画**
//   （`Module/View/SpriteAnimator.cs`），动作名对齐原版的 `AnimData` 动作代号。
//
// 取值顺序 = 播放优先级无关，仅作标识（帧数表见 `SpriteFrames.FrameCounts`）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.View
{
    /// <summary>逐帧动画动作（对齐原版 `AnimData` 的动作代号）。</summary>
    public enum ViewAnim
    {
        /// <summary>待机（原版 NU）。</summary>
        Idle = 0,

        /// <summary>行走（原版 WL）。</summary>
        Walk = 1,

        /// <summary>攻击（原版 A1）。</summary>
        Attack = 2,

        /// <summary>施法（原版 SC）。</summary>
        Cast = 3,

        /// <summary>受击（原版 GH/DD 前段；本项目用 2 帧的短促抖动）。</summary>
        Hit = 4,

        /// <summary>死亡（原版 DT；非循环，播到最后一帧即尸体）。</summary>
        Death = 5,

        /// <summary>
        /// 跑（原版 **RN**）。
        /// <para>★ 片 2b 新增，**加在末尾**（取值 6）—— `SpriteFrameCounts.ByUnit` 的 `int[]`
        /// **下标 = 本枚举**（`SpriteFrameCounts.Of` 取 `(int)anim`）⇒ 插在中间会把整张帧数表错位。</para>
        /// <para>单位覆盖（片 2a 的导出实测，= 磁盘上的 `run_*.png`）：5 个职业各 8 帧、`zm` 8 帧、
        /// `cr` 10 帧；**其余单位原版没有 RN** ⇒ 帧数 0，请求 Run 时沿
        /// `SpriteFrames.FallbackChain` 回退（Run → Walk → Idle）。</para>
        /// </summary>
        Run = 6,
    }
}
