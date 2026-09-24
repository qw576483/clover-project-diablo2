// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/ViewAnimState.cs
// **动作选择的纯函数**（**本项目新增**）：把"这一帧该播哪个动作"从 `ViewModule` 里摘出来，
// 变成可以**离线逐帧驱动**的判定（`tools/probes/hosts/animcheck`）。
//
// 为什么必须有这一层（不是为抽象而抽象）：
//   `ViewModule` 的实体视图要 `new GameObject(...)` 才能建 —— 离线自检宿主里会抛异常
//   （`EnsureRoot` 的 catch 把 `_cannotRender` 置 true）⇒ 它里面那串 `want = 三元链`
//   摘成纯函数之后，宿主可以喂**任意状态组合**、拿真 `SpriteAnimator` 逐帧推进并断言。
//
//   玩家：**Death &gt; Hit &gt; Cast &gt; Attack &gt; (Run|Walk) &gt; Idle**
//   怪物：**Death &gt; Hit &gt; Attack &gt; (Walk|Idle)**
//   原版依据：受击（GH）会打断出手与移动（hit recovery 是**不可打断**的姿态）；
//   死亡姿态不可被任何动作覆盖（尸体就是尸体）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Module.View
{
    /// <summary>动作选择的**纯函数**（无 Unity 依赖 ⇒ 可离线驱动；见文件头）。</summary>
    internal static class ViewAnimState
    {
        /// <summary>
        /// 玩家这一帧该播的动作。
        /// </summary>
        /// <param name="isDead">`IPlayerModule.IsDead`。</param>
        /// <param name="hitAnimPlaying">
        /// 受击动画**还没播完**（调用方传 `Playing == Hit &amp;&amp; !Anim.Finished`）。
        /// <para>这一项是"玩家受击动作能看见"的关键 —— 旧实现里 `TickPlayer` 的
        /// want 链**没有受击这一档**，`PlayHit` 设的 `Hit` 会在**同一帧**被覆盖成 `Idle`，
        /// 而两次贴图（`PlayHit` 的 Hit[0] 与随后的 Idle 帧）都发生在**渲染之前**
        /// ⇒ 该单位那 6 帧受击动画**一帧都不会被渲染**（离线复现：`animcheck` §6d 的旧口径反例出 0 帧）。</para>
        /// </param>
        /// <param name="castTimerActive">施法动作计时器未走完（`Events.SkillCast` 设置）。</param>
        /// <param name="attackTimerActive">挥击动作计时器未走完（`Events.PlayerAttacked` 设置）。</param>
        /// <param name="moving">`IPlayerModule.IsMoving`。</param>
        /// <param name="running">`IPlayerModule.IsRunning`（原版 `R` 键切换的**跑**）。</param>
        public static ViewAnim SelectPlayer(bool isDead, bool hitAnimPlaying, bool castTimerActive,
            bool attackTimerActive, bool moving, bool running)
        {
            if (isDead) return ViewAnim.Death;
            if (hitAnimPlaying) return ViewAnim.Hit;
            if (castTimerActive) return ViewAnim.Cast;
            if (attackTimerActive) return ViewAnim.Attack;
            if (moving) return running ? ViewAnim.Run : ViewAnim.Walk;
            return ViewAnim.Idle;
        }

        /// <summary>
        /// 受击动作**是否还在播**（= `SelectPlayer` 的 `hitAnimPlaying` 该传什么）。
        /// <para>三个条件缺一不可：① 当前播的就是 `Hit`；② 该动作**不止一帧**
        /// （`SpriteAnimator.Tick` 对 `_keys.Length &lt;= 1` 直接返回 false ⇒ `Finished` 永不为真
        /// ⇒ 只判前两条会让玩家**永远卡在受击姿态**）；③ 还没播到末帧。
        /// 用「受击动画播完」当保持结束条件 ⇒ 该 (单位, hit) 的**真实帧数 ÷ 基准帧率**就是保持时长，
        /// 不需要另写一个时长常量（原版 `AnimData.d2` 不在本机，凭空写一个数没有出处）。</para>
        /// </summary>
        public static bool IsHitHolding(ViewAnim playing, int frameCount, bool finished)
        {
            return playing == ViewAnim.Hit && frameCount > 1 && !finished;
        }

        /// <summary>
        /// 怪物这一帧该播的动作（原版怪物只有 `NU`/`WL`/`A1`/`GH`/`DT` 五个模式；
        /// `RN`/`SC` 的触发条件缺出处 ⇒ 这里**不**给它们造句，见回报的登记项）。
        /// </summary>
        /// <param name="alive">`MonsterState.alive`。</param>
        /// <param name="hitStun">`MonsterState.hitStun`（`MonsterTuning.HitStunSeconds`）。</param>
        /// <param name="attacking">`MonsterState.attacking`（`MonsterTuning.AttackAnimSeconds`）。</param>
        /// <param name="moved">本帧世界坐标是否变化（`ViewModule.UpdateMonster` 的判据）。</param>
        public static ViewAnim SelectMonster(bool alive, bool hitStun, bool attacking, bool moved)
        {
            if (!alive) return ViewAnim.Death;
            if (hitStun) return ViewAnim.Hit;
            if (attacking) return ViewAnim.Attack;
            return moved ? ViewAnim.Walk : ViewAnim.Idle;
        }
    }
}
