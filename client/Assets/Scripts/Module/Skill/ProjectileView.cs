// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/ProjectileView.cs
// 投射物的**表现层**（**本项目新增**）。
//
// 为什么投射物自带表现，而不走 `IViewModule`：
//   `IViewModule`（`Module/Contracts.cs`，**契约冻结**）只有玩家/怪物/地面物品/飘字/受击/死亡，
//   **没有**投射物入口；而「不许新增门面接口」「不许改契约」两条硬约束同时成立
//   ⇒ 唯一不改契约的做法就是投射物在自己的模块里**自绘**。`IViewModule` 仍然负责其它实体。
//
// 素材策略：**用原版帧图**。
//   `missile_c.cel_file` 给官方动画名（如 `Firebolt` / `SafeArrow`），帧图在
//   `D2data.mpq` 的 `data\global\missiles\<CelFile>.dcc`；由 `tools/d2codec/export_missiles.py`
//   逐方向逐帧解成 `Resources/Clover/D2/Missiles/<CelFile>/{方向}_{帧号}.png`。
//   路径唯一来源 = `Core/ResPaths.MissileFrame`；帧数 = `MissileFrameCounts`（与 PNG 同批生成）。
//   只有"CelFile 为空 / 未导出"时才退成**按伤害类型上色的纯色占位**（看得见的缺失信号）。
//
// 离线自检宿主说明：非 Unity 进程里 `new GameObject(...)` 会抛异常 ⇒ `TryCreate` **捕获并返回 false**
//   （表现缺失，逻辑照常跑），同时打一条 `[Skill]` 日志（这是"没按预期走"的分支，必须留痕）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.View;
using UnityEngine;
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义（引擎也有一个 Dir8）。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（与 `Core/Iso.cs` 同一做法）。
using Dir8 = Diablo2.Def.Dir8;

namespace Diablo2.Module.Skill
{
    /// <summary>投射物表现工厂（自绘；素材缺失时用纯色占位）。</summary>
    internal static class ProjectileView
    {
        /// <summary>占位 sprite（懒创建；1×1 白图，pivot=中心，PPI = `GameConst.PixelsPerUnit`）。</summary>
        private static Sprite _placeholder;
        private static bool _createFailedLogged;

        // 为什么需要：`Projectile.WorldOf` 的 z **恒 0**（那是**逻辑层**的返回值语义，不许改）
        //   ⇒ 同 `gx+gy` 的两个投射物「主键（`EntitySortOrder`）相等」时无键可分 ⇒ 给投射物一个
        //   **纯函数**第三键（沿用 `ViewModule.SortTieZ` 的「档底 + id × 步长」形状），
        //   不动 `Module/View/**`、不改 `Projectile.cs` 的返回值语义。
        // 取值带 = (0, 0.9]：实体档 `ViewModule.SortTieZ` 恒 ≥ 1.0001 ⇒ **投射物与实体的先后关系不变**
        //   （仍画在实体之前），只把「投射物之间」这条**无键带**补上。
        private const float SortZBase = 0.0001f;      // 档底：⛔ 不许为 0（z == 0 正是"无第三键"）
        private const int SortZModulo = 9000;         // 档内取模 ⇒ 上界 1e-4 + 8999×1e-4 = 0.9 < 1

        /// <summary>
        /// 引擎侧「同 `sortingOrder` 的确定性次级键」实例（<see cref="SortingLayers"/>）——
        /// 本项目**只借它的 <see cref="SortingLayers.TiebreakOffset(int)"/>**：取模基数换成
        /// 投射物档的 <see cref="SortZModulo"/>，步长沿用引擎默认 `DefaultTiebreakStep`（= 1e-4，
        /// 与实体档 <c>ViewModule.SortTieZ</c> **同量级**：正交相机下距离差 ~1e-4 可分辨）。
        /// <para>本工程只借引擎件 <c>TiebreakOffset</c> 的取模折回（含负值折回），
        /// 不保留第二份实现（两处结果逐位相同）。</para>
        /// <para><c>fieldHeightTiles</c> 是引擎构造必填项、本处用不到（深度序是等距格口径，
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
        /// `TryCreate` / `Sync` 都必须走这里，不许各自拼 z。</para>
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
                // 不许直接写 `Projectile.WorldOf(p.pos)`（它的 z 恒 0 = 无第三键）⇒ 走唯一产地
                go.transform.position = WorldPosOf(p);
                // 原版 80 px/单位 vs 本项目导入 PPU=64 ⇒ 缩 0.8（与实体节点同一口径，见
                // `SpriteFrames.ArtScale`）；不缩则投射物比同屏的角色/地形大 25%。
                go.transform.localScale = Vector3.one * SpriteFrames.ArtScale;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = ResolveSprite(p);
                sr.color = ColorOf(p.type);
                // 深度排序随格子变化（`constraints.md` #5）：**与实体层同一口径** ——
                // 走 `ViewModule.EntitySortOrder`（内含 deck / 桥面抬档），不许自己写
                // `Iso.SortOrder(g, LayerOffsetEntity)`：那正是审计 B 红行 R2 ——
                // 桥面格上普通实体档 `4D+102` 必被正南一格栏杆 `4D+105` 盖住。
                sr.sortingOrder = ViewModule.EntitySortOrder(p.Grid);
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
            // 同 `TryCreate`：位置走唯一产地 `WorldPosOf`（含第三键），不写裸 `Projectile.WorldOf`
            p.View.transform.position = WorldPosOf(p);
            // 排序口径走 `ViewModule.EntitySortOrder`（含 deck 抬档），不写裸实体档
            if (p.Renderer != null) p.Renderer.sortingOrder = ViewModule.EntitySortOrder(p.Grid);

            // 帧图是**异步**取的 ⇒ 每帧重取一次（`SpriteFrames.Resolve` 自带缓存 / 在途 / 退避，
            //   到位后当帧即换；不到位的帧保留上一张图，不写 null ⇒ 不会退成占位方块）。
            var sprite = SpriteFrames.Resolve(FrameKeyOf(p));
            if (sprite != null && p.Renderer != null && p.Renderer.sprite != sprite)
            {
                p.Renderer.sprite = sprite;
                p.Renderer.color = Color.white;
                LogFirstRealSprite(p, sprite);
            }
        }

        /// <summary>销毁表现节点。</summary>
        public static void Destroy(Projectile p)
        {
            if (p == null) return;
            if (p.View != null) UnityEngine.Object.Destroy(p.View);
            p.View = null;
            p.Renderer = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 原版帧图（口径：`ResPaths.MissileFrame` + `MissileFrameCounts`）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 该投射物的**朝向**（`Def.Dir8`）：由**格空间单位向量** `Projectile.dir` 的符号取得。
        /// <para>走 `Iso.DirectionTo`（本项目"格增量 → 屏幕朝向"的**唯一**出处），
        /// ⛔ 不自己写 `switch (sign...)` 的第二份映射 —— 那正是"朝向整体错位且不报错"的成因。</para>
        /// <para>分量取整到符号：方位只有 8 档，`dir` 的小数部分不参与判定。</para>
        /// </summary>
        public static Dir8 DirOf(Projectile p)
        {
            if (p == null) return Dir8.S;
            var dx = p.dir.x > 0f ? 1 : (p.dir.x < 0f ? -1 : 0);
            var dy = p.dir.y > 0f ? 1 : (p.dir.y < 0f ? -1 : 0);
            return Iso.DirectionTo(new Vector2Int(dx, dy));
        }

        /// <summary>
        /// 该投射物当前该显示的**帧号**：`⌊已飞时间 × 帧率⌋ mod 帧数`，其中
        /// 已飞时间 = `traveled / speed`（格 ÷ 格/秒，全部来自契约字段 ⇒ 不引入新状态、可离线断言）。
        /// <para>帧率 = <see cref="ResPaths.MissileFps"/>；帧数 = `MissileFrameCounts`（生成文件）。</para>
        /// <para>`speed &lt;= 0` 或帧数未知 ⇒ 返回 0（不除零、不越界）。</para>
        /// </summary>
        public static int FrameIndexOf(Projectile p)
        {
            if (p == null || p.speed <= 0.01f) return 0;
            var frames = MissileFrameCounts.Of(p.celFile);
            if (frames <= 1) return 0;
            var seconds = p.traveled / p.speed;
            var i = (int)(seconds * ResPaths.MissileFps);
            if (i < 0) return 0;
            return i % frames;
        }

        /// <summary>该投射物当前该取的**帧键** = <see cref="ResPaths.MissileFrame"/>；无 CelFile ⇒ 空串。</summary>
        public static string FrameKeyOf(Projectile p)
        {
            if (p == null || string.IsNullOrEmpty(p.celFile)) return string.Empty;
            return ResPaths.MissileFrame(p.celFile, DirOf(p), FrameIndexOf(p));
        }

        /// <summary>是否已经打过「原版帧就位」的一次性日志（数值证据；只报一次，不刷屏）。</summary>
        private static bool _realSpriteLogged;

        /// <summary>
        /// 首张原版帧到位时记一条（证明"磁盘素材 → 运行时贴图"这条链真的通了；
        /// 缺这条就没法把"图在盘上"与"画面上真有图"分开）。
        /// </summary>
        private static void LogFirstRealSprite(Projectile p, Sprite sprite)
        {
            if (_realSpriteLogged) return;
            _realSpriteLogged = true;
            SkillLog.Info($"[投射物] 原版帧图就位：{FrameKeyOf(p)}（{sprite.rect.width:0}×{sprite.rect.height:0} 原生px，" +
                          $"CelFile={p.celFile} 朝向={DirOf(p)}，" +
                          $"帧数={MissileFrameCounts.Of(p.celFile)}@{ResPaths.MissileFps:0.#}fps，" +
                          "来源 = 原版 `data\\global\\missiles\\<CelFile>.dcc`）");
        }

        /// <summary>
        /// 建节点当帧取的贴图：有原版帧键就先 `Resolve`（首帧通常是 null —— 异步未到位，
        /// 由 <see cref="Sync"/> 每帧重取）；`celFile` 为空或未登记 ⇒ 纯色占位（**看得见的缺失**）。
        /// </summary>
        private static Sprite ResolveSprite(Projectile p)
        {
            if (p == null) return Placeholder;

            if (string.IsNullOrEmpty(p.celFile))
            {
                SkillLog.WarnOnce("proj.sprite.nocel",
                    $"投射物 #{p.id}「{p.skillName}」没有 CelFile（`missile_c.cel_file` 为空）" +
                    "⇒ 用纯色占位（配表核对）");
                return Placeholder;
            }

            if (!MissileFrameCounts.Has(p.celFile))
            {
                SkillLog.WarnOnce("proj.sprite.unknown." + p.celFile,
                    $"投射物 CelFile=\"{p.celFile}\" 不在 `MissileFrameCounts`（= 未导出原版帧）" +
                    "⇒ 用纯色占位；补图 = 重跑 tools/d2codec/export_missiles.py");
                return Placeholder;
            }

            var sprite = SpriteFrames.Resolve(FrameKeyOf(p));
            return sprite != null ? sprite : Placeholder;
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
