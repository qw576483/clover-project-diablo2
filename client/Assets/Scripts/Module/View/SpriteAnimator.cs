// ═════════════════════════════════════════════════════════════════════════════
// 本项目新增 Module/View/SpriteAnimator.cs
// **业务自写的逐帧 Sprite 动画**。
//
// 为什么必须自己写（不是重造轮子，是引擎没有这条路径）：
//   引擎的动画能力是 `Game.Anim`（`IAnimationManager.CreateAnimator(go, RuntimeAnimatorController)`
//   → `IAnimPlayer.Play/CrossFade/SetBool…`），底层是 Unity 的 **Animator 状态机**；
//   它要求先有 `AnimatorController` 资产，而 `AnimatorController` **只能在 Editor 侧生成**
//   原版暗黑2 的角色/怪物是 **`.dcc` 解码出来的逐帧 PNG 序列**（动作 × 8 方向 × N 帧），
//   语义上就是"按固定帧率轮播一组贴图"，用 Animator 反而要为一帧一张图建状态机（不可行）。
//   ⇒ 本项目自写 `SpriteAnimator`（逐帧切图），**不使用 `Game.Anim`**。
//
//   引擎件 = 「**已加载的 `Sprite[]` + 帧段下标」推进并**直接写 `SpriteRenderer.sprite`**
//            （`Play(frames, int[] indices, …)` / `PlayOnce` / `PlayStill` / `SwitchTo` 滞回 / `Advance`）；
//   本件   = 「**帧键（string）**」的时间轴（动作 × 朝向 × 帧号 → 键），**不持有 Sprite**、
//            不碰任何 Unity 对象 ⇒ 可在离线宿主里直接驱动断言，且帧还没贴图到位时也能正确计时。
//   ⇒ 两者的差异是**语义差异不是写法差异**：把本件换成引擎件，等于要求"贴图必须先同步加载完"
//     （本项目是异步逐帧加载 + 占位），会把"帧未到位也能推进"这条性质丢掉。
//   ⇒ 引擎件真正能替代的是本件**之外**的那半：帧段展开（`ExpandRuns` 的 `182-230 ∪ 247-251` 这类
//     非连续区间）。未做强行合并（会退化），如需体验引擎件请用在**已同步拿到 Sprite** 的场景。
//
// 设计：**只做时间 → 帧号的纯逻辑**（帧用"键"表示，贴图解析交给 `SpriteFrames`），
//   这样它不依赖任何 Unity 对象 ⇒ 可被离线自检宿主直接驱动与断言（`tools/probes/hosts/combatcheck`）。
//
// 用法：
//   anim.Play(ViewAnim.Walk, keys, fps: 12f, loop: true);
//   if (anim.Tick(dt)) renderer.sprite = SpriteFrames.Resolve(anim.CurrentKey);
// ═════════════════════════════════════════════════════════════════════════════

using System;

namespace Diablo2.Module.View
{
    /// <summary>逐帧精灵动画器（**本项目新增**；纯逻辑，可离线驱动）。</summary>
    public sealed class SpriteAnimator
    {
        private string[] _keys;
        private float _fps = 8f;
        private float _accum;
        private bool _loop = true;

        /// <summary>当前动作。</summary>
        public ViewAnim Anim { get; private set; }

        /// <summary>当前帧下标（`FrameCount == 0` 时为 0）。</summary>
        public int FrameIndex { get; private set; }

        /// <summary>当前动作的帧数。</summary>
        public int FrameCount { get { return _keys != null ? _keys.Length : 0; } }

        /// <summary>当前动作是否循环。</summary>
        public bool Loop { get { return _loop; } }

        /// <summary>非循环动画是否已播到最后一帧。</summary>
        public bool Finished { get; private set; }

        /// <summary>播放速度倍率（1 = 配表/默认帧率）。</summary>
        public float SpeedScale { get; set; }

        /// <summary>当前帧的键（无帧时返回 null）。</summary>
        public string CurrentKey
        {
            get
            {
                if (_keys == null || _keys.Length == 0) return null;
                var i = FrameIndex;
                if (i < 0) i = 0;
                if (i >= _keys.Length) i = _keys.Length - 1;
                return _keys[i];
            }
        }

        /// <summary>构造：默认待机、速度倍率 1。</summary>
        public SpriteAnimator()
        {
            SpeedScale = 1f;
            Anim = ViewAnim.Idle;
            _fps = DefaultFps;
            // `Finished` 必须**显式**置 true：新对象的 `_keys` 是 null（一帧都没有），
            //    而 `Finished` 的 bool 默认值是 false —— 那会让 `Play()` 的早退判据误以为
            Finished = true;
        }

        /// <summary>手上是否真有帧可用（`_keys` 非 null 且非空）—— `Play()` 早退判据的一部分。</summary>
        private bool HasKeys => _keys != null && _keys.Length > 0;

        /// <summary>
        /// 两组帧键是否**指向同一套帧**（长度相同且逐个字符串相等）。
        /// <para>不比较"长度 + 首元素"就够了：原版不同方向的帧键只差中间那个方向串
        /// （`idle_s_0` vs `idle_e_0`），长度完全相同 ⇒ 必须逐个比。
        /// `SpriteFrames.Keys()` 每次都 new 一个数组，所以拿不到引用相等，只能按值比；
        /// 调用点只在动作/朝向变化时发生（不是每帧），代价可忽略。</para>
        /// </summary>
        private static bool SameKeys(string[] a, string[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        /// <summary>默认帧率（素材到位后按原版 `.dcc` 的帧率校准；见 `SpriteFrames`）。</summary>
        public const float DefaultFps = 8f;

        /// <summary>
        /// 播放某个动作。**同一个动作重复调用不会重置进度**（否则每帧都被打回第 0 帧，
        /// </summary>
        /// <remarks>
        /// <para>旧判据 = `anim == Anim &amp;&amp; !Finished`，而构造完的 `Anim` 就是 `ViewAnim.Idle`、
        /// `Finished` 又是 bool 默认值 `false` ⇒ 新建视图后**唯一一次** `Play(Idle, …)`
        /// （`ViewModule.CreatePlayer` / `CreateMonster` / `OnStageEntered` 建 NPC 视图那条路）
        /// 直接早退、`_keys` 永远是 null ⇒ `FrameCount == 0` ⇒ `CurrentKey == null`
        /// ⇒ `SpriteFrames.Resolve(null)` 立刻返回 null ⇒ `ViewModule.ApplyFrame` 贴
        /// `SpriteFrames.Placeholder`（**纯色块：玩家=黄 / 怪物=品红系**），而且**一条错都不报**。</para>
        /// <para>之后也**永不自愈**：`ViewModule` 只在"动作发生变化"时才再调 `Play`，
        /// 而 `Playing` 已被记成 `Idle` ⇒ 站着不动的玩家、面向正南的 NPC、原地不动的怪物
        /// 一直到退出 Play 都是色块（只有走动过 / 换过朝向的才会偶然显示出真图）。
        /// 实测证据（运行期读到）：`[1:Idle#0/0(占位)]` 且
        /// `SpriteFrames.Cache.Count = 0`（说明连一次 `Resources.Load` 都没发起）。</para>
        /// </remarks>
        public void Play(ViewAnim anim, string[] keys, float fps, bool loop)
        {
            // 口径（「人物移动抖动」的候选② = "动画被反复打回第 0 帧"）：**已离线否证**。
            //   下面的早退判据本身就保证了「**同动作 + 同帧键 + 手上真有帧 + 未播完 ⇒ 不重置进度**」；
            //   而"换朝向"会把帧键**整组换掉**（`{动作}_{方向}_{帧号}`）⇒ 走复位分支，那是原版语义
            //   （8 方向各一套 `.cof`，换方向 = 换一整套帧）**必须保留**，不许改成"跨方向保留相位"。
            //   离线证据（真 `SpriteAnimator` + 真 `PlayerMotor` 的锯齿路径逐帧驱动）：
            //
            // 早退的**完整**条件：动作没变 **且** 帧键也一模一样 **且** 手上真有帧 **且** 还没播完。
            //   `SameKeys` 不能省：原版是 8 方向逐帧动画，"动作没变但换了朝向"是一次**新动画**
            //   （帧键整组换掉）。只判 `anim == Anim` 会把换方向吞掉 ⇒ 角色"朝东走却放着朝南的动画"
            //   （`ViewModule` 侧同时改成朝向变化也调 `PlayAnim`，两处合力才对）。
            if (anim == Anim && !Finished && HasKeys && SameKeys(_keys, keys)) return;

            Anim = anim;
            _keys = keys;
            _fps = fps > 0.01f ? fps : DefaultFps;
            _loop = loop;
            _accum = 0f;
            FrameIndex = 0;
            Finished = !HasKeys;

            // —— 单位键名与 `SpriteFrameCounts` / 磁盘目录名对不上，或导出缺图。
            // 这一帧以后只会一直显示占位色块 ⇒ 只报一次（不刷屏）。
            if (!HasKeys)
            {
                ViewLog.WarnOnce("spriteanim.empty",
                    $"SpriteAnimator.Play({anim}) 拿到 0 个帧键 ⇒ 本实体只会一直显示纯色占位图" +
                    "（检查单位键名是否与 SpriteFrameCounts 的键 / 磁盘目录名一致）");
            }
        }

        /// <summary>强制重播当前动作（死亡→复活这类需要从头播的场合）。</summary>
        public void Replay()
        {
            _accum = 0f;
            FrameIndex = 0;
            Finished = _keys == null || _keys.Length == 0;
        }

        /// <summary>切帧：返回**帧号是否变化**（调用方据此换贴图，避免每帧无用赋值）。</summary>
        public bool Tick(float dt)
        {
            if (_keys == null || _keys.Length <= 1 || dt <= 0f) return false;
            if (Finished) return false;

            var scale = SpeedScale > 0.01f ? SpeedScale : 0.01f;
            _accum += dt * scale;

            var frameTime = 1f / _fps;
            var moved = false;

            while (_accum >= frameTime)
            {
                _accum -= frameTime;
                var next = FrameIndex + 1;

                if (next < _keys.Length)
                {
                    FrameIndex = next;
                    moved = true;
                    continue;
                }

                if (_loop)
                {
                    FrameIndex = 0;
                    moved = true;
                    continue;
                }

                FrameIndex = _keys.Length - 1;   // 非循环：停在最后一帧（死亡动画即"尸体"）
                Finished = true;
                moved = true;
                break;
            }

            return moved;
        }

        /// <summary>直接跳到某帧（自证/调试用）。</summary>
        public bool SetFrame(int index)
        {
            if (_keys == null || _keys.Length == 0) return false;
            var i = Math.Max(0, Math.Min(_keys.Length - 1, index));
            if (i == FrameIndex) return false;
            FrameIndex = i;
            Finished = !_loop && i == _keys.Length - 1;
            return true;
        }

        /// <summary>一行状态（日志/自证）。</summary>
        public override string ToString()
        {
            return $"Anim={Anim} frame={FrameIndex}/{FrameCount} fps={_fps:0.#} loop={_loop} " +
                   $"finished={Finished} speed={SpeedScale:0.##}";
        }
    }
}
