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

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.View;
using UnityEngine;

namespace Diablo2.Module.Skill
{
    /// <summary>投射物表现工厂（自绘；素材缺失时用纯色占位）。</summary>
    internal static class ProjectileView
    {
        /// <summary>占位 sprite（懒创建；1×1 白图，pivot=中心，PPI = `GameConst.PixelsPerUnit`）。</summary>
        private static Sprite _placeholder;
        private static bool _createFailedLogged;

        // ── U26/U36 **同族**：投射物的第三键（z 次级排序键）—— team-lead 2026-09-24 裁决 P1 ──────
        // 为什么需要：`Projectile.WorldOf` 的 z **恒 0**（`Projectile.cs:141-144`；那是**逻辑层**的
        //   返回值语义，⛔ 按裁决不许改）⇒ 同 `gx+gy` 的两个投射物「主键（`EntitySortOrder`）相等
        //   且第三键也相等」⇒ 次序未定义，交给渲染器内部提交顺序（就是 U26/U36 那条根因的同族）。
        // 口径：给投射物一个**纯函数**第三键（沿用 `ViewModule.SortTieZ` 的「档底 + id × 步长」形状），
        //   ⛔ 不动 `Module/View/**`、⛔ 不改 `Projectile.cs` 的返回值语义。
        // 取值带 = (0, 0.9]：实体档 `ViewModule.SortTieZ` 恒 ≥ 1.0001 ⇒ **投射物与实体的先后关系不变**
        //   （仍画在实体之前），只把「投射物之间」这条**无键带**补上 —— 要改"投射物 vs 实体"的遮挡
        //   关系是**表现类**，留给台账「待 Unity 窗口」总表 **W7**（看同格 / 相邻格两态各一图）。
        private const float SortZBase = 0.0001f;      // 档底：⛔ 不许为 0（z == 0 正是"无第三键"）
        private const int SortZModulo = 9000;         // 档内取模 ⇒ 上界 1e-4 + 8999×1e-4 = 0.9 < 1

        /// <summary>
        /// 引擎侧「同 `sortingOrder` 的确定性次级键」实例（<see cref="SortingLayers"/>）——
        /// 本项目**只借它的 <see cref="SortingLayers.TiebreakOffset(int)"/>**：取模基数换成
        /// 投射物档的 <see cref="SortZModulo"/>，步长沿用引擎默认 `DefaultTiebreakStep`（= 1e-4，
        /// 与实体档 <c>ViewModule.SortTieZ</c> **同量级**：正交相机下距离差 ~1e-4 可分辨）。
        /// <para>原先项目侧那段 `n = id % Mod; if (n &lt; 0) n += Mod; return n * step;`
        /// 与引擎件 <c>TiebreakOffset</c> 的实现**逐字同源**（含负值折回）⇒ 本工程不再保留第二份，
        /// 值**逐位不变**（数值等价证据：`.ai-tmp/test/d2view-sortkey/`）。</para>
        /// <para>⛔ <c>fieldHeightTiles</c> 是引擎构造必填项、本处用不到（深度序是等距格口径，
        /// 见 `ViewModule.EntitySortOrder`）⇒ 填有出处的 `GameConst.MapMaxSize`。</para>
        /// </summary>
        private static readonly SortingLayers SortZTiebreak = new SortingLayers(
            GameConst.MapMaxSize,
            SortingLayers.DefaultDepthLevelsPerTile,
            SortZModulo,
            SortingLayers.DefaultTiebreakStep);

        /// <summary>
        /// 投射物节点的**第三键**（z 次级排序键）：**纯函数**（同一 id ⇒ 同一值）；同一 `gx+gy` 上
        /// 不同 id ⇒ 值不等（可决胜）。见上方「U26/U36 同族」注释。
        /// </summary>
        public static float SortZFor(int projectileId)
        {
            return SortZBase + SortZTiebreak.TiebreakOffset(projectileId);
        }

        /// <summary>
        /// 投射物节点的世界坐标 = `Projectile.WorldOf(p.pos)` + **第三键**（z = `SortZFor(p.id)`）。
        /// <para>本文件是投射物表现层位置的**唯一产地**（同 `ViewModule.EntityWorld` 之于实体）：
        /// ⛔ `TryCreate` / `Sync` 都必须走这里，不许各自拼 z。</para>
        /// </summary>
        public static Vector3 WorldPosOf(Projectile p)
        {
            var w = Projectile.WorldOf(p.pos);
            w.z = SortZFor(p.id);
            return w;
        }

        /// <summary>
        /// 为投射物创建表现节点。**失败不抛**：返回 false，逻辑继续跑（离线宿主即走这条）。
        /// </summary>
        public static bool TryCreate(Projectile p)
        {
            if (p == null) return false;

            try
            {
                var go = new GameObject($"Projectile_{p.id}_{p.skillName}");
                // ⛔ 不许直接写 `Projectile.WorldOf(p.pos)`（它的 z 恒 0 = 无第三键）⇒ 走唯一产地
                go.transform.position = WorldPosOf(p);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ResolveSprite(p);
                sr.color = ColorOf(p.type);
                // 深度排序随格子变化（`constraints.md` #5）：**与实体层同一口径** ——
                // 走 `ViewModule.EntitySortOrder`（内含 deck / 桥面抬档），⛔ 不许自己写
                // `Iso.SortOrder(g, LayerOffsetEntity)`：那正是审计 B 红行 R2 ——
                // 桥面格上普通实体档 `4D+102` 必被正南一格栏杆 `4D+105` 盖住。
                sr.sortingOrder = ViewModule.EntitySortOrder(p.Grid);
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
            // 同 `TryCreate`：位置走唯一产地 `WorldPosOf`（含第三键），⛔ 不写裸 `Projectile.WorldOf`
            p.View.transform.position = WorldPosOf(p);
            // 排序口径走 `ViewModule.EntitySortOrder`（含 deck 抬档），⛔ 不写裸实体档
            if (p.Renderer != null) p.Renderer.sortingOrder = ViewModule.EntitySortOrder(p.Grid);
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
