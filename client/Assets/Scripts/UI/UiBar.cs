// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/UiBar.cs  ★ 本项目新增（agent-09）
// **血球 / 蓝球 / 经验条**的填充助手 —— 专门用来把 `constraints.md` #3 关死在门外。
//
// 约束原文（`tools/ai-skill/constraints.md` #3）：
//   `Image.Type = Filled` + **没有 sprite** ⇒ `fillAmount` **静默失效**（零报错、画面不动）。
//   血球（`healthbar.png`）/ 经验条（`ExperienceBar.png`）不要靠 fillAmount，
//   或显式给一张 1×1 白 sprite。
//
// 本助手的做法（两条路都由 <see cref="Decide"/> 决定，可被离线自检断言）：
//   · **有 sprite** ⇒ `Filled` + `fillAmount`。这与**原版**完全一致：
//     原版 `ControlPanel.prefab` 里 `HealthBar`/`ManaAnimation` 是 `m_Type=3`(Filled) +
//     `m_FillMethod=1`(Vertical) + `m_FillOrigin=0`(Bottom) + sprite=`healthbar.png`/`manabar.png`；
//     `Filler`（经验条）是 `m_Type=3` + `m_FillMethod=0`(Horizontal) + `m_FillOrigin=0`(Left) +
//     sprite=`ExperienceBar.png`。**即原版就是 Filled + 真 sprite**（禁令禁的是「Filled + 空 sprite」）。
//   · **没 sprite**（异步加载尚未回来 / 素材缺失）⇒ **绝不用 Filled**，
//     改用**锚点宽度/高度**（`anchorMax.x = ratio` / `anchorMax.y = ratio`）表达进度，
//     这样在贴图到位前后都真的有画面反馈，且不会踩静默失效。
//
// 用法（面板里只有一行）：
//   UiBar.Set(img, ResPaths.PanelHealthBar, 0.65f, horizontal: false);
//   …贴图回来时助手会自己从「锚点模式」切到「Filled 模式」并保持当前比例。
//
// ⛔ 本文件在 UI 层：只引用 `CloverEngine` / `Diablo2.Core` / UnityEngine(.UI)。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>进度条 / 球的填充实现方式（`FillMode` 的取值由 <see cref="UiBar.Decide"/> 决定）。</summary>
    internal enum FillMode
    {
        /// <summary>锚点宽度（横向）或锚点高度（纵向）——**无 sprite 时的唯一安全做法**。</summary>
        Anchor = 0,

        /// <summary>`Image.Type.Filled` + 真 sprite + `fillAmount`（原版做法）。</summary>
        FilledSprite = 1,
    }

    /// <summary>血球 / 经验条的填充助手（贴图异步到位后自动从锚点模式切到 Filled 模式）。</summary>
    internal static class UiBar
    {
        private sealed class State
        {
            public Image Target;
            public string SpritePath;
            public float Ratio;
            public bool Horizontal;
            public bool SpriteRequested;
        }

        // 以 Image 自身为键（引用相等；`UnityEngine.Object` 已重写 Equals/GetHashCode）。
        // ⚠️ 刻意不用 `Object.GetInstanceID()`：Unity 6000.6 已把它标记为 obsolete（CS0619 = 编译错误）。
        private static readonly Dictionary<Image, State> States = new Dictionary<Image, State>();

        /// <summary>
        /// 决定填充方式：**有 sprite 才允许 `Filled`**。
        /// 这是 `constraints.md` #3 的可断言形式（`tools/uicheck` 会断言 `hasSprite=false ⇒ Anchor`）。
        /// </summary>
        public static FillMode Decide(bool hasSprite) => hasSprite ? FillMode.FilledSprite : FillMode.Anchor;

        /// <summary>
        /// 绑定贴图 + 设初值。贴图缺失/未到位时立即用锚点模式显示比例（不是"等图再画"）。
        /// </summary>
        /// <param name="img">填充块（`healthbar` / `manabar` / `ExperienceBar`）。</param>
        /// <param name="spritePath">`ResPaths` 里的路径；空串 = 只做锚点填充。</param>
        /// <param name="ratio">0..1。</param>
        /// <param name="horizontal">true = 横向填充（经验条）；false = 纵向填充（血球自底向上，原版语义）。</param>
        public static void Set(Image img, string spritePath, float ratio, bool horizontal)
        {
            if (img == null)
            {
                UiLog.Error("UiBar.Set 收到 null 填充块（面板构件未创建？）⇒ 进度不会显示");
                return;
            }

            var state = Ensure(img);
            state.SpritePath = spritePath;
            state.Horizontal = horizontal;
            state.Ratio = Mathf.Clamp01(ratio);

            if (string.IsNullOrEmpty(spritePath))
            {
                UiLog.WarnOnce("bar.nosprite." + img.name,
                    $"进度条 {img.name} 没有配置 sprite ⇒ 用锚点填充（不会踩 constraints.md #3，但已退回非原版做法）");
                Apply(img, state);
                return;
            }

            if (img.sprite != null)
            {
                Apply(img, state);
                return;
            }

            // 贴图还没到：先按锚点显示，别等图（否则打开面板瞬间是空的）
            Apply(img, state);
            RequestSprite(img, state);
        }

        /// <summary>只改比例（每帧/每次刷数值都走这里）。</summary>
        public static void SetRatio(Image img, float ratio)
        {
            if (img == null) return;
            var state = Ensure(img);
            state.Ratio = Mathf.Clamp01(ratio);
            Apply(img, state);
        }

        /// <summary>面板关闭时清掉状态（避免字典随开关面板无限增长）。</summary>
        public static void Forget(Image img)
        {
            if (img == null) return;
            States.Remove(img);
            PruneDestroyed();
        }

        // ── 内部 ─────────────────────────────────────────────────────────────
        private static State Ensure(Image img)
        {
            if (States.TryGetValue(img, out var state))
            {
                state.Target = img;
                return state;
            }

            state = new State { Target = img, Ratio = 0f, Horizontal = false };
            States[img] = state;
            PruneDestroyed();
            return state;
        }

        /// <summary>清掉面板已销毁（UnityEngine 的 `== null` 语义）留下的条目。</summary>
        private static void PruneDestroyed()
        {
            if (States.Count < 32) return;      // 正常只有个位数条目，超过阈值才做一次清扫

            var dead = new List<Image>();
            foreach (var kv in States)
                if (kv.Key == null || kv.Value?.Target == null)
                    dead.Add(kv.Key);
            for (var i = 0; i < dead.Count; i++)
                States.Remove(dead[i]);
        }

        private static void RequestSprite(Image img, State state)
        {
            if (state.SpriteRequested) return;
            state.SpriteRequested = true;

            if (Game.Res == null)
            {
                UiLog.WarnOnce("bar.res.null",
                    $"Game.Res 未初始化（CloverRes.Init 未调用）⇒ {state.SpritePath} 取不到，进度条继续用锚点填充");
                return;
            }

            Game.Res.LoadAsset<Sprite>(state.SpritePath, sp =>
            {
                if (sp == null)
                {
                    UiLog.WarnOnce("bar.missing." + state.SpritePath,
                        $"原版进度贴图缺失：{state.SpritePath} ⇒ 继续用锚点填充（画面仍有进度反馈）");
                    return;
                }

                if (img == null) return;      // 面板已关闭销毁
                img.sprite = sp;
                Apply(img, state);            // 有 sprite 了 ⇒ 切到 Filled（与原版一致）
            });
        }

        /// <summary>
        /// 真正落到 `Image` 上。
        /// ⚠️ **几何契约**：填充块的父节点必须是「条/球的容器」（尺寸 = 条/球的尺寸）。
        /// 两种模式都按「铺满父节点」来算锚点，因此父节点是什么尺寸，进度就画在什么范围里
        /// —— 把填充块直接挂在铺满屏幕的面板根上会让进度条变成整屏色块（本项目已按此契约
        /// 在 `UiPanel` 侧建了容器，见 `HudPanel.BuildOrb` / 经验条的 track）。
        /// </summary>
        private static void Apply(Image img, State state)
        {
            if (img == null) return;

            var ratio = Mathf.Clamp01(state.Ratio);
            var mode = Decide(img.sprite != null);
            var rt = img.rectTransform;

            // 先把矩形归一成「铺满父节点」，两种模式都从同一几何基准出发
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;

            if (mode == FillMode.FilledSprite)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = state.Horizontal ? Image.FillMethod.Horizontal : Image.FillMethod.Vertical;
                img.fillOrigin = 0;          // 原版：Horizontal→Left、Vertical→Bottom（都是 0）
                img.fillClockwise = true;
                img.fillAmount = ratio;
                return;
            }

            // 无 sprite ⇒ **绝不用 Filled**（fillAmount 会静默失效），改锚点宽度/高度
            img.type = Image.Type.Simple;
            rt.anchorMax = state.Horizontal ? new Vector2(ratio, 1f) : new Vector2(1f, ratio);
        }
    }
}
