// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/ProjectileView.cs
// 投射物的**表现层**（**本项目新增**）。
//
// 为什么投射物自带表现，而不走 `IViewModule`：
//   `IViewModule`（`Module/Contracts.cs`，**契约冻结**）只有玩家/怪物/地面物品/飘字/受击/死亡，
//   **没有**投射物入口；而「不许新增门面接口」「不许改契约」两条硬约束同时成立
//   ⇒ 唯一不改契约的做法就是投射物在自己的模块里**自绘**。`IViewModule` 仍然负责其它实体。
//
// 素材策略（`tools/ai-skill/conventions.md` §素材规则）：
//   原版投射物图在 `d2data.mpq` 的 `.dcc` 里（`missile_c.cel_file` 给了名字，如 `Firebolt`/`Icebolt`），
//   本体未到手 ⇒ **按素材规定用纯色占位**（按伤害类型上色：火=橙、冰=蓝、电=黄、毒=绿、物理=灰白），
//   并已登记 `client/资源欠缺清单.md`（投射物一节）。
//   ⚠️ 换真图时**只需**：`Core/ResPaths.cs` 增一个 `D2Missiles` 常量 + 本文件 `TryLoadRealSprite` 一行，
//      **不动** `Projectile`（逻辑）与 `SkillModule`。
//
// 离线自检宿主说明：非 Unity 进程里 `new GameObject(...)` 会抛异常 ⇒ `TryCreate` **捕获并返回 false**
//   （表现缺失，逻辑照常跑），同时打一条 `[Skill]` 日志（这是"没按预期走"的分支，必须留痕）。
// ─────────────────────────────────────────────────────────────────────────────

using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;

namespace Diablo2.Module.Skill
{
    /// <summary>投射物表现工厂（自绘；素材缺失时用纯色占位）。</summary>
    internal static class ProjectileView
    {
        /// <summary>占位 sprite（懒创建；1×1 白图，pivot=中心，PPI = `GameConst.PixelsPerUnit`）。</summary>
        private static Sprite _placeholder;
        private static bool _createFailedLogged;

        /// <summary>
        /// 为投射物创建表现节点。**失败不抛**：返回 false，逻辑继续跑（离线宿主即走这条）。
        /// </summary>
        public static bool TryCreate(Projectile p)
        {
            if (p == null) return false;

            try
            {
                var go = new GameObject($"Projectile_{p.id}_{p.skillName}");
                go.transform.position = Projectile.WorldOf(p.pos);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ResolveSprite(p);
                sr.color = ColorOf(p.type);
                // 深度排序随格子变化（`constraints.md` #5）：与实体层同一基准
                sr.sortingOrder = Iso.SortOrder(p.Grid, GameConst.LayerOffsetEntity);
                // 占位体不能有 Collider（否则相机探针会打到它，见 `docs/步骤文档.md` §3.4）
                // —— SpriteRenderer 本身不生成 Collider，这里无需额外处理。

                p.View = go;
                p.Renderer = sr;
                return true;
            }
            catch (System.Exception e)
            {
                if (!_createFailedLogged)
                {
                    _createFailedLogged = true;
                    SkillLog.Warn($"ProjectileView.TryCreate 失败（{e.GetType().Name}: {e.Message}）" +
                                  "⇒ 投射物只有逻辑没有画面（离线自检宿主属正常；Unity 里出现请看这条日志）");
                }
                p.View = null;
                p.Renderer = null;
                return false;
            }
        }

        /// <summary>把表现节点同步到投射物当前位置（每帧由 `SkillModule.Tick` 调）。</summary>
        public static void Sync(Projectile p)
        {
            if (p == null || p.View == null) return;
            p.View.transform.position = Projectile.WorldOf(p.pos);
            if (p.Renderer != null) p.Renderer.sortingOrder = Iso.SortOrder(p.Grid, GameConst.LayerOffsetEntity);
        }

        /// <summary>销毁表现节点。</summary>
        public static void Destroy(Projectile p)
        {
            if (p == null) return;
            if (p.View != null) UnityEngine.Object.Destroy(p.View);
            p.View = null;
            p.Renderer = null;
        }

        /// <summary>
        /// 取投射物贴图：先按 `missile_c.cel_file` 找真图，取不到就用纯色占位。
        /// <para>
        /// ⚠️ 真图路径需要一个**尚未登记**的 `ResPaths` 常量（`Core/` 冻结，本项目不许改）
        /// ⇒ 当前**一律返回占位**，并在首次调用时说明一次。换真图时在此加一行 `Game.Res.TryGet`。
        /// </para>
        /// </summary>
        private static Sprite ResolveSprite(Projectile p)
        {
            if (p != null && !string.IsNullOrEmpty(p.celFile))
            {
                SkillLog.WarnOnce("proj.sprite.missing",
                    $"投射物贴图缺失：官方 cel_file=\"{p.celFile}\"（在 d2data.mpq 的 .dcc 里，本体未到手）" +
                    "⇒ 用纯色占位；换真图要 Core/ResPaths.cs 增 D2Missiles 常量（已登记 资源欠缺清单.md）");
            }
            return Placeholder;
        }

        /// <summary>占位图（1×1 白，pivot 中心；靠 `SpriteRenderer.color` 区分伤害类型）。</summary>
        private static Sprite Placeholder
        {
            get
            {
                if (_placeholder != null) return _placeholder;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    name = "ProjectilePlaceholder",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp,
                };
                var px = new Color32[4];
                for (var i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(px);
                tex.Apply(false, false);

                _placeholder = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f),
                    GameConst.PixelsPerUnit);
                _placeholder.name = "ProjectilePlaceholder";
                return _placeholder;
            }
        }

        /// <summary>占位色（按伤害类型；验收要求"一眼看出是什么系"）。</summary>
        public static Color ColorOf(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return new Color(1.00f, 0.45f, 0.10f);
                case DamageType.Cold: return new Color(0.45f, 0.80f, 1.00f);
                case DamageType.Lightning: return new Color(1.00f, 0.90f, 0.25f);
                case DamageType.Poison: return new Color(0.45f, 0.90f, 0.30f);
                default: return new Color(0.90f, 0.90f, 0.95f);
            }
        }
    }
}
