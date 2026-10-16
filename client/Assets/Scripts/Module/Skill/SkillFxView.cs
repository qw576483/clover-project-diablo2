// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Skill/SkillFxView.cs
// 技能效果的**表现层**（**本项目新增**）—— 与 `ProjectileView` 同一套做法与理由：
//   `IViewModule` 契约里没有"技能特效"入口，且契约冻结 ⇒ 效果在自己模块里**自绘**。
//
// 素材策略（**用原版帧图，绝不画近似图**）：
//   ① 优先 `SkillCatalog` 给的表现素材键 —— 投射物帧 = `Resources/Clover/D2/Missiles/<CelFile>/<方向>_<帧>.png`
//      （`ResPaths.MissileFrame` + `MissileFrameCounts`，与 `ProjectileView` **同一套**）；
//      `obj:` 前缀 = `Resources/Clover/D2/Objects/<dir>/NNN.png`（原版物件逐帧，如 `fx` 火焰）。
//   ② 素材缺失（`cel_file = null` 的技能 / 未导出）⇒ **程序化几何**兜底：按等距投影画**椭圆环/圆盘**
//      （运行时 `Texture2D` 生成，与 `ProjectileView` 的纯色占位同一级别"看得见的缺失信号"，
//      但环/盘能表达"范围有多大"，比 2×2 方块更接近原版读法）。**绝不静默不画**。
//
// 等距尺寸口径（为什么环是 2:1 的椭圆）：
//   `Iso.HalfW = 1.0` / `Iso.HalfH = 0.5`（`GameConst.IsoHalfW/H`）⇒ 半径 R 格的地面圆
//   在世界里正好是 `2R × R`（宽 : 高 = 2 : 1）。本文件的环/盘纹理固定 64×32（PPU 64 ⇒ 1×0.5 世界单位），
//   于是"缩放到 2R 倍"就是半径为 R 格的等距圆 —— 一个乘数，无第二份几何。
//
// 排序口径（治"特效被地面盖住"，实测：bug）：
//   特效贴图**中心 pivot** 且原版帧偏高 ⇒ 下沿探到锚点行以南 1~4 行，而地面(D+2) = 4D+104
//   **大于**实体档(D) = 4D+102 ⇒ 正前方两格的地面会把特效下半截切断（实体自己是脚底 pivot，
//   只向上长，没这问题）。⇒ 本文件不按"锚点格"取档，而按**贴图下沿所在行**取实体档，
//   见 <see cref="SortOrderKernel"/>（纯内核 + 薄壳，离线可断言）。
//
// 离线自检宿主：非 Unity 进程里 `new GameObject(...)` 抛异常 ⇒ **捕获并返回 false**（逻辑照跑），
//   只打一条 `[Skill]` 日志（"没按预期走"的分支必须留痕，与 `ProjectileView.TryCreate` 同口径）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using Diablo2.Module.View;
using UnityEngine;

namespace Diablo2.Module.Skill
{
    /// <summary>技能效果表现工厂（自绘；素材缺失时退成等距椭圆环/盘）。</summary>
    internal static class SkillFxView
    {
        /// <summary>对象逐帧（`obj:` 素材）的帧率：与投射物同为原版 16 fps（`ResPaths.MissileFps`）。</summary>
        public const float Fps = ResPaths.MissileFps;

        /// <summary>环/盘纹理的像素尺寸（64×32 在 PPU 64 下 = 1×0.5 世界单位 = **一个**格宽×半格高）。</summary>
        private const int RingTexW = 64;
        private const int RingTexH = 32;

        /// <summary>
        /// 环/盘占位贴图（<see cref="RingTexW"/>×<see cref="RingTexH"/> px）的**半高**（世界单位 = 0.25）：
        /// 原版帧异步未到位（`sprite == null`）时按它兜底估算贴图下沿，下一帧换上真图自会纠正。
        /// </summary>
        private const float RingHalfHeight = RingTexH / (2f * GameConst.PixelsPerUnit);

        /// <summary>
        /// 直线 / 连锁在世界空间的**份间距**（世界单位 = 一个格宽）。
        /// <para><b>为什么不能按"格数"算份数</b>（实测：炼狱之火成一排火球、连锁闪电只有一小段）：
        /// 4 格直线在世界空间是 <c>4·√(1²+0.5²) ≈ 4.47</c> 单位，若只放 4 份，相邻份间隔
        /// <c>4.47/3 = 1.49</c> 单位，而 `Flamethrower` 宽 80px = 1.0 单位 ⇒ 间隙 0.5 单位，
        /// 读出来就是"一排彼此分开的火球"。原版 `infernoflame1` 是沿弹道**高速连发**
        /// ⇒ 只有密到互相重叠才读作一根连续火柱。</para>
        /// </summary>
        private const float BeamPartSpacing = 0.7f;

        /// <summary>连锁折线的份间距（比直线更密：折线每一跳都要"连成一条亮链"）。</summary>
        private const float ChainPartSpacing = 0.7f;

        /// <summary>直线份的单份拉伸上限（防止细长素材被拉成一坨）。</summary>
        private const float BeamMaxStretch = 2f;

        /// <summary>直线份的**整体放大**（不分方向）：炼狱之火的火球原尺寸只有 0.8 世界单位宽，
        /// 放到 1.25 倍才够"一根 2.6 单位高的火柱"这个量级。</summary>
        private const float BeamPartBoost = 1.25f;

        private static bool _createFailedLogged;
        /// <summary>建视图失败过（离线宿主）⇒ <see cref="EnsureParts"/> 不再重试，免去每帧抛异常。</summary>
        private static bool _createDisabled;

        private static Sprite _ring;
        private static Sprite _disc;

        // ═════════════════════════════════════════════════════════════════════
        // 素材解析
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>该表现素材的每方向帧数（0 = 未登记 ⇒ 调用方走程序化兜底）。</summary>
        public static int FramesOf(string art)
        {
            if (string.IsNullOrEmpty(art)) return 0;
            if (art.StartsWith("obj:")) return SkillCatalog.HydraArtFrames;
            return MissileFrameCounts.Has(art) ? MissileFrameCounts.Of(art) : 0;
        }

        /// <summary>按伤害类型给**默认冲击素材**（技能没指定 `impactArt` 时用）。</summary>
        public static string DefaultImpactArt(DamageType type)
        {
            switch (type)
            {
                case DamageType.Fire: return "ExpArrowExplode";
                case DamageType.Cold: return "IceBlast";
                case DamageType.Lightning: return "LightningStrike";
                case DamageType.Poison: return "PoisonSparks";
                default: return "BloodSmall01";
            }
        }

        /// <summary>该素材在进度 <paramref name="progress"/> 时应显示的帧号（0..帧数-1）。</summary>
        public static int FrameOf(string art, float progress)
        {
            var frames = FramesOf(art);
            if (frames <= 1) return 0;
            var i = (int)(Mathf.Clamp01(progress) * frames);
            if (i >= frames) i = frames - 1;              // 末帧停住，不回卷（避免"闪一下从头来"）
            return i < 0 ? 0 : i;
        }

        /// <summary>该素材某一帧的资源键（`obj:` 走物件目录；否则投射物帧）。</summary>
        public static string FrameKeyOf(string art, Dir8 dir, int frame)
        {
            if (string.IsNullOrEmpty(art)) return string.Empty;
            if (art.StartsWith("obj:")) return ResPaths.ObjectSprite(ResPaths.DecoFrame(art.Substring(4), frame));
            return ResPaths.MissileFrame(art, dir, frame);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 建 / 同步 / 销毁
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>该效果需要几个绘制份数（场 = 逐格、直线 / 连锁 = 沿线的**世界长度 ÷ 间距**）。</summary>
        public static int PartCount(SkillEffect e)
        {
            if (e == null) return 0;
            switch (e.kind)
            {
                case SkillFxKind.Field: return Mathf.Max(1, e.points.Count);
                case SkillFxKind.Beam: return BeamPartCount(e);
                case SkillFxKind.Chain: return ChainPartCount(e);
                case SkillFxKind.Falling: return 2;      // 0 = 火球（`Meteor`），1 = 尾迹（`MeteorTail`）
                case SkillFxKind.Summon: return Mathf.Max(1, e.count);   // 多头蛇的"几个蛇口"（纯表现）
                default: return 1;
            }
        }

        /// <summary>直线（炼狱之火 / 闪电）沿线的份数 = <c>世界长度 ÷ <see cref="BeamPartSpacing"/> + 1</c>。</summary>
        public static int BeamPartCount(SkillEffect e)
        {
            if (e == null) return 2;
            var len = WorldLen(e.from, e.to);
            return Mathf.Clamp(Mathf.RoundToInt(len / BeamPartSpacing) + 1, 2, 32);
        }

        /// <summary>
        /// 连锁折线的份数 = <c>折线世界总长 ÷ <see cref="ChainPartSpacing"/> + 1</c>，且**每跳至少 1 份**。
        /// <para>旧口径是 `points.Count - 1`（一跳一份）：一跳跨 5 格时线段世界长 ≈ 5.6 单位，
        /// 而 `LightningStrike` 只有 148px = 1.85 单位宽 ⇒ 一整跳只在**中点**画一小团电弧，
        /// 读作"一小段蓝弧"而非"从法师亮到怪的一整条链"（实测：）。</para>
        /// </summary>
        public static int ChainPartCount(SkillEffect e)
        {
            if (e == null) return 1;
            var segs = Mathf.Max(1, e.points.Count - 1);
            var total = 0f;
            for (var k = 0; k + 1 < e.points.Count; k++) total += WorldLen(e.points[k], e.points[k + 1]);
            var byLength = Mathf.RoundToInt(total / ChainPartSpacing) + 1;
            return Mathf.Clamp(Mathf.Max(segs, byLength), 1, 40);
        }

        /// <summary>两点的**世界**距离（格 → 世界的映射见 <see cref="Projectile.WorldOf"/>）。</summary>
        public static float WorldLen(Vector2 a, Vector2 b)
        {
            var wa = Projectile.WorldOf(a);
            var wb = Projectile.WorldOf(b);
            return new Vector2(wb.x - wa.x, wb.y - wa.y).magnitude;
        }

        /// <summary>为效果创建表现节点。**失败不抛**：返回 false，逻辑继续跑（离线宿主即走这条）。</summary>
        public static bool TryCreate(SkillEffect e)
        {
            if (e == null) return false;
            try
            {
                var go = new GameObject($"Spell_{e.id}_{e.skillName}");
                go.transform.position = WorldPosOf(e, e.pos);
                go.transform.localScale = Vector3.one * SpriteFrames.ArtScale;

                var n = PartCount(e);
                e.renderers = new List<SpriteRenderer>(n);
                for (var i = 0; i < n; i++)
                {
                    // ⛔ 每份（含 0）都是**子节点**：旧写法让第 0 份直接用根 `go`，
                    // 于是"第 0 份的 position/rotation/localScale"会带动**全部**份
                    // （直线/连锁要逐份旋转 + 横向拉伸时会把整条线塌到第 0 份上）。
                    var child = new GameObject("part" + i);
                    child.transform.SetParent(go.transform, false);
                    var sr = child.AddComponent<SpriteRenderer>();
                    sr.sprite = ResolveSprite(e, i);
                    sr.color = ColorOf(e, i);
                    // 建节点当帧就按"贴图下沿行"落档（首帧 Sync 会按真实缩放再刷一次）
                    sr.sortingOrder = SortOrderOf(sr, go.transform.position, e.Grid);
                    e.renderers.Add(sr);
                }

                e.view = go;
                return true;
            }
            catch (System.Exception ex)
            {
                if (!_createFailedLogged)
                {
                    _createFailedLogged = true;
                    SkillLog.Warn($"SkillFxView.TryCreate 失败（{ex.GetType().Name}: {ex.Message}"
                                  + (ex.InnerException != null
                                      ? $" ← {ex.InnerException.GetType().Name}: {ex.InnerException.Message}"
                                      : string.Empty) + "）"
                                  + "⇒ 技能效果只有逻辑没有画面（离线自检宿主属正常；Unity 里出现请看这条日志）");
                }
                e.view = null;
                e.renderers = null;
                _createDisabled = true;   // 建不出来（离线宿主）⇒ 后面别再试，省掉每帧一次异常
                return false;
            }
        }

        /// <summary>
        /// **份数补齐**：
        /// <para>
        /// `SpawnField` / `FireChain` 的顺序是**先** <see cref="SpawnEffect"/>（内部调
        /// <see cref="TryCreate"/> 建视图）**后** `e.points.AddRange(pts)` ⇒ 建视图那刻
        /// `points` 还是空的，<see cref="PartCount"/> 按 0 算 ⇒ 只建了 **1 份** SpriteRenderer。
        /// 于是：火墙 5 格只画 1 簇火、暴风雪 ~10 格只画 1 坨冰、连锁闪电 N 跳只画 1 段。
        /// </para>
        /// 修法 = 每帧 <see cref="Sync"/> 前按当前 <see cref="PartCount"/> **原地补齐**子节点
        /// （只增不删；数量一致时零开销）。不改成"先灌 points 再建视图"，是因为
        /// `SpawnEffect` 有 18 个调用点，逐个传 points 面更大；且原地补齐对"points 之后又被
        /// 改写"的任何路径都成立。
        /// </summary>
        public static void EnsureParts(SkillEffect e)
        {
            if (e == null) return;
            if (e.view == null || e.renderers == null)
            {
                // 离线宿主（非 Unity 进程）里 `new GameObject` 必抛 ⇒ 成功过一次就不再重试，
                // 否则每个效果每帧都要抛一次异常（`SpawnEffect` 已经试过一次了）。
                if (!_createDisabled) TryCreate(e);
                return;
            }
            var want = PartCount(e);
            if (e.renderers.Count >= want) return;
            for (var i = e.renderers.Count; i < want; i++)
            {
                var child = new GameObject("part" + i);
                child.transform.SetParent(e.view.transform, false);
                var sr = child.AddComponent<SpriteRenderer>();
                sr.sprite = ResolveSprite(e, i);
                sr.color = ColorOf(e, i);
                // 补齐份的首帧档位由紧随其后的 ApplyFrame/SetOrder 重设，这里只给个不撞地面的初值
                sr.sortingOrder = SortOrderOf(sr, e.view.transform.position, e.Grid);
                e.renderers.Add(sr);
            }
        }

        /// <summary>把表现节点同步到效果当前状态（每帧由 `SkillModule.TickEffects` 调）。</summary>
        public static void Sync(SkillEffect e)
        {
            if (e == null) return;
            // 份数对齐必须在任何分支之前：`points` 是建视图之后才灌的（见 EnsureParts 文档）
            EnsureParts(e);
            if (e.view == null || e.renderers == null) return;

            var progress = e.Progress;
            var alpha = AlphaOf(e, progress);
            var scale = ScaleOf(e, progress);
            e.view.transform.localScale = Vector3.one * SpriteFrames.ArtScale * scale;

            switch (e.kind)
            {
                case SkillFxKind.Field:
                    for (var i = 0; i < e.renderers.Count && i < e.points.Count; i++)
                    {
                        var r = e.renderers[i];
                        var p = e.points[i];
                        r.transform.position = WorldPosOf(e, p);
                        // 先上贴图再算档：`SortOrderOf` 要读 `sprite.bounds`（占位图也有 bounds）
                        ApplyFrame(e, r, i, progress, alpha);
                        SetOrder(r, r.transform.position, CloverEngine.ProjectileRuntime.GridOf(p));
                    }
                    break;

                case SkillFxKind.Beam:
                    SyncBeam(e, progress, alpha);
                    break;

                case SkillFxKind.Chain:
                    SyncChain(e, progress, alpha);
                    break;

                case SkillFxKind.Falling:
                    SyncFalling(e, progress, alpha);
                    break;

                case SkillFxKind.Summon:
                    SyncSummon(e, progress, alpha);
                    break;

                default:
                    // 缩放每帧都在变（新星扩散 / 冲击放大 / 光环呼吸 / 余烬淡出）⇒ 贴图下沿行每帧
                    // 都在动，排序必须跟着刷（旧代码这里**只**在建节点时设过一次 ⇒ 扩散后被地面切）。
                    e.view.transform.position = WorldPosOf(e, e.pos);
                    for (var i = 0; i < e.renderers.Count; i++)
                    {
                        ApplyFrame(e, e.renderers[i], i, progress, alpha);
                        // 子节点 localPosition = 0 ⇒ 世界位置就是父节点位置
                        SetOrder(e.renderers[i], e.view.transform.position, e.Grid);
                    }
                    break;
            }
        }

        /// <summary>销毁表现节点。</summary>
        public static void Destroy(SkillEffect e)
        {
            if (e == null) return;
            if (e.view != null) UnityEngine.Object.Destroy(e.view);
            e.view = null;
            e.renderers = null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 位置 / 缩放 / 透明度
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 效果的世界坐标 = 连续格 → 世界（`Projectile.WorldOf`，与投射物**同一口径**）
        /// + **第三键**（z = `ProjectileView.SortZFor(id)`：同 `gx+gy` 的两个效果可决胜，
        /// 且仍画在实体之前 —— 口径与投射物共用一套，不新开一档）。
        /// </summary>
        public static Vector3 WorldPosOf(SkillEffect e, Vector2 p)
        {
            var w = Projectile.WorldOf(p);
            w.z = ProjectileView.SortZFor(e != null ? e.id : 0);
            return w;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 排序：按**贴图下沿所在行**取实体档（治"特效被地面盖住"）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **纯内核**（不碰渲染器/贴图 ⇒ 离线宿主可逐条断言）：按"贴图下沿所在行"算 sortingOrder。
        ///
        /// <para><b>为什么不能直接用锚点格的实体档</b>（<see cref="ViewModule.EntitySortOrder(Vector2Int)"/>）：
        /// 特效贴图是**中心 pivot**（导弹帧导入口径 —— <see cref="ResolveSprite"/> 取的都在
        /// `Resources/Clover/D2/Missiles/**`，环/盘占位同样是中心 pivot）且原版帧偏高：
        /// 实测 `ExpArrowExplode` 246 px、`IceBlast` 156 px、`Firebolt` 152 px（PPU 64 ×
        /// `SpriteFrames.ArtScale` 0.8 ⇒ 世界半高 0.69~1.92）⇒ 贴图下沿探到锚点行**以南 1~4 行**。
        /// 而分层是 地面(D)=4D+96、物件(D)=4D+101、实体(D)=4D+102 ⇒ **正南两格的地面
        /// 4D+104 就盖得住实体档的特效** —— 截图里"脚下冰霜特效被瓦片网格线切断"正是这条。
        /// 实体（角色/怪物）自己没这问题：实体贴图是**脚底 pivot**，只向上长，探不到南边。</para>
        ///
        /// <para><b>口径</b>：把特效当成"站在它贴图下沿那一行的实体" ⇒ 取该行实体档（= 该行物件层 + 1
        /// = 4·行+102）。于是 ① 压得住它视觉覆盖到的**全部**地面（每行地面 4D+96 &lt; 4·行+102，
        /// 含锚点行与下沿行）；② 仍被下沿行**再往南**一格的物件(4·行+105)/实体(4·行+106)/
        /// 迷雾(4·行+107) 盖住 ⇒ 原版"格 y 越大越靠前"的遮挡关系不破；③ 与同格实体并列时
        /// 特效 z（≤0.9）&lt; 实体 z（≥1.0001）⇒ 特效画在实体之前（命中闪光盖在怪身上 = 原版读法）。</para>
        ///
        /// <para><b>行怎么算</b>：贴图下沿是世界 y 上的一条水平线，行坐标 `fy0 = −下沿y / HalfH`；
        /// 格 (gx,gy) 的菱形占 `fy ∈ [D, D+2]`（D = gx+gy；由 `MapView.PlaceOfPx` 得标准地砖
        /// 中心 = `Iso.GridToWorld(g).y = −(D+1)·HalfH`、图高 = 2·HalfH）⇒ 该线穿过的格
        /// `D ∈ [ceil(fy0−2), floor(fy0)]`（**暴力复算过**：沿线采样取 `Iso.WorldToGrid` 的
        /// 最大 `gx+gy` 与 `floor(fy0)` 逐例相等，含 fy0 恰为整数的边界）。
        /// 本方法取 `floor(fy0)−1`（比真实最大行**少一行**）⇒ 档位 4·floor(fy0)+98 仍压得住
        /// 全部被穿到的地面（最高的 ground(k) = 4k+96 &lt; 4k+98），同时让**下沿行本身**的
        /// 物件(4k+101)/实体(4k+106) 盖住特效下半截 —— 更接近原版"前面的墙压住后面的法术"。</para>
        /// </summary>
        internal static int SortOrderKernel(float anchorWorldY, float dropWorld, Vector2Int anchorGrid)
        {
            var fy0 = -(anchorWorldY - dropWorld) / Iso.HalfH;          // 下沿的行坐标（连续）
            var frontRow = Mathf.FloorToInt(fy0 - 1f);                 // 比真实最大行少一行（见上）
            // 同列、下沿行（排序只认 gx+gy，列取锚点列 ⇒ 仍是图上真实存在的那一格）
            var frontGrid = new Vector2Int(anchorGrid.x, frontRow - anchorGrid.x);
            return ViewModule.EntitySortOrder(frontGrid);
        }

        /// <summary>
        /// <see cref="SortOrderKernel"/> 的薄壳：从渲染器取"贴图下沿在锚点下方多少世界单位"。
        /// <para>`Sprite.bounds.min.y` 是 **pivot 相对量**（编辑器实测：中心 pivot = −半高、
        /// 脚底/左下 pivot = 0）⇒ 取负再乘节点**实际缩放**（`lossyScale`）就是世界单位的下沿落差，
        /// 换任何 pivot 的贴图都对，也不怕调用方传错缩放。</para>
        /// </summary>
        public static int SortOrderOf(SpriteRenderer r, Vector3 world, Vector2Int anchorGrid)
        {
            var scale = r != null ? r.transform.lossyScale.x : 1f;
            var sp = r != null ? r.sprite : null;
            var drop = sp != null ? -sp.bounds.min.y * scale : RingHalfHeight * scale;
            return SortOrderKernel(world.y, drop, anchorGrid);
        }

        /// <summary>按贴图下沿行刷新该渲染器的排序（值没变就不写 ⇒ 不白脏渲染器）。</summary>
        private static void SetOrder(SpriteRenderer r, Vector3 world, Vector2Int anchorGrid)
        {
            if (r == null) return;
            var o = SortOrderOf(r, world, anchorGrid);
            if (r.sortingOrder != o) r.sortingOrder = o;
        }

        /// <summary>
        /// 缩放：半径 R 格的地面圆 = 世界里 `2R × R` ⇒ 环/盘纹理（1×0.5 世界单位）**均匀**放大 `2R` 倍。
        /// 新星按进度从 `0.35R` 扩到 `1R`（"扩散"读法）；其余基准 1。
        /// </summary>
        public static float ScaleOf(SkillEffect e, float progress)
        {
            if (e == null) return 1f;
            if (e.kind == SkillFxKind.Nova)
            {
                var r = Mathf.Max(1, e.radius);
                return r * 2f * (0.35f + 0.65f * Mathf.Clamp01(progress));
            }
            if (e.kind == SkillFxKind.Aura) return AuraScale(e) * (1f + 0.07f * Mathf.Sin(e.lifeLeft * 6f));   // 轻微呼吸脉动（原版护甲/光环缠绕的近似）
            if (e.kind == SkillFxKind.Summon) return 0.75f;                                       // 喷火口：原版 `groundFireBig` 3.1 单位高，缩到 2.3 单位才像"地上一个口"
            if (e.kind == SkillFxKind.Impact) return 1f + 0.35f * Mathf.Clamp01(progress);
            // 坠落物：远小近大（纵深读法），落地一刻放到 1.25 倍与爆炸同步放大
            if (e.kind == SkillFxKind.Falling) return Mathf.Lerp(0.8f, 1.25f, Mathf.Clamp01(progress));
            return 1f;
        }

        /// <summary>
        /// 光环的基础缩放。**按素材给**，因为"原版图里真正有东西的那块"大小差别很大：
        /// `FrozenArmor`（三护甲共用）的可见冰晶只占 72×214 画布里中间 61×90 的一条
        /// ⇒ 2.2 倍下只有 <b>1.3 世界单位宽</b>的一小撮碎光，读出来就是"几乎看不见"（实测：）；
        /// 而 `FireEnchant` / `ManaShieldBall` 的图形本身就是环绕一圈的大环，2.2 倍正合适。
        /// </summary>
        private static float AuraScale(SkillEffect e)
        {
            var art = ArtOf(e, 0);
            float s = 2.2f;
            if (art == "FrozenArmor") s = 3.4f;
            return s;
        }

        /// <summary>透明度：前 15% 淡入、后 35% 淡出（避免"啪一下消失"）。</summary>
        public static float AlphaOf(SkillEffect e, float progress)
        {
            if (e == null) return 1f;
            if (e.kind == SkillFxKind.Aura) return 1f;   // 原版护甲/护盾/附魔是**不透明**的角色叠加层，0.55 等于"看不清"
            // 坠落物：只在前 10% 淡入，之后恒 1（落地那一刻必须与爆炸同步消失，不能提前淡出）
            if (e.kind == SkillFxKind.Falling)
            {
                var q = Mathf.Clamp01(progress);
                return q < 0.10f ? Mathf.Lerp(0.2f, 1f, q / 0.10f) : 1f;
            }
            var p = Mathf.Clamp01(progress);
            if (p < 0.15f) return Mathf.Lerp(0.15f, 1f, p / 0.15f);
            if (p > 0.65f) return Mathf.Lerp(1f, 0f, (p - 0.65f) / 0.35f);
            return 1f;
        }

        /// <summary>该效果第 i 份的底色（有原版帧图 ⇒ 白，保素材本色；否则按伤害类型上色）。</summary>
        public static Color ColorOf(SkillEffect e, int part)
        {
            var art = ArtOf(e, part);
            return FramesOf(art) > 0 ? Color.white : ProjectileView.ColorOf(e.type);
        }

        /// <summary>该效果第 i 份用的素材键（直线/连锁取冲击素材，坠落物 0=火球 1=尾迹，其余取本体素材）。</summary>
        public static string ArtOf(SkillEffect e, int part)
        {
            if (e == null) return string.Empty;
            if (e.kind == SkillFxKind.Falling)
            {
                return part == 0 ? e.art : (string.IsNullOrEmpty(e.impactArt) ? e.art : e.impactArt);
            }
            if (e.kind == SkillFxKind.Beam || e.kind == SkillFxKind.Chain || e.kind == SkillFxKind.Impact)
            {
                return string.IsNullOrEmpty(e.impactArt) ? DefaultImpactArt(e.type) : e.impactArt;
            }
            return e.art;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// 坠落物（陨石）：`renderers[0]` = 火球（`Meteor`）在当前位置；`renderers[1]` = 尾迹
        /// （`MeteorTail`）贴在火球"上方"（沿下落方向回退一小段 ⇒ 越接近地面尾迹越短，读作拖尾）。
        /// 两者都按**各自贴图下沿行**刷 sortingOrder（火球 110 px、尾迹同 ⇒ 下沿探到锚点南两行；
        /// 高空时画在实体之前，落地一刻与爆炸同层）。
        /// </summary>
        private static void SyncFalling(SkillEffect e, float progress, float alpha)
        {
            if (e.renderers.Count < 2) return;

            var head = e.renderers[0];
            head.transform.position = WorldPosOf(e, e.pos);
            ApplyFrame(e, head, 0, progress, alpha);
            SetOrder(head, head.transform.position, CloverEngine.ProjectileRuntime.GridOf(e.pos));

            // 尾迹：沿"起点→落点"反方向回退；下落到后段（progress→1）时收短，视觉上"扎进地里"。
            var drop = e.landAt - e.fallFrom;
            if (drop.sqrMagnitude < 1e-6f) drop = Vector2.down;
            drop.Normalize();
            var back = 1.6f * (1f - 0.55f * Mathf.Clamp01(progress));
            var tailPos = e.pos - drop * back;

            var tail = e.renderers[1];
            tail.transform.position = WorldPosOf(e, tailPos);
            ApplyFrame(e, tail, 1, progress, alpha);
            SetOrder(tail, tail.transform.position, CloverEngine.ProjectileRuntime.GridOf(tailPos));
        }

        /// <summary>直线：把 `count` 份均匀铺在 `from → to` 上（末帧进度留一点错位 = "电流感"）。</summary>
        private static void SyncBeam(SkillEffect e, float progress, float alpha)
        {
            var n = e.renderers.Count;
            if (n <= 0) return;
            var len = Mathf.Max(WorldLen(e.from, e.to), 1e-4f);
            var spacing = len / (n - 1 <= 0 ? 1 : n - 1);   // 相邻份的世界间距
            // 素材世界宽度（首帧拿真图量一次后按素材键缓存；量不到就按 1 单位宽估）
            var art0 = ArtOf(e, 0);
            var artW = ArtWorldWidth(art0, n > 0 ? e.renderers[0] : null);
            // 单份横向拉伸：让相邻份**互相重叠**（间距 > 素材宽时必须拉），否则读作"一排分离的弹"。
            // 1.6 的重叠系数是对着"炼狱之火"的真素材调的：`Flamethrower` 只有 0.8 世界单位宽，
            // 0.7 间距下要拉到 ~1.4 倍才连成一根火柱（实测：过"一排火球"）。
            var cover = spacing / Mathf.Max(artW * SpriteFrames.ArtScale, 0.05f) * 1.6f;
            var stretch = Mathf.Clamp(cover, 1f, BeamMaxStretch);

            for (var i = 0; i < n; i++)
            {
                var t = n <= 1 ? 0f : i / (float)(n - 1);
                var p = Vector2.Lerp(e.from, e.to, t);
                PlacePart(e, e.renderers[i], p, StretchAngle(e.from, e.to), stretch, BeamPartBoost,
                    progress, alpha, i * 3);
            }
        }

        /// <summary>
        /// 连锁折线：沿 <see cref="SkillEffect.points"/> 的**世界总长**密铺，每份旋转到所在线段的方向。
        /// <para>旧口径"每跳一份、钉在线段中点"（实测：只有一小段蓝色电弧）—— 一跳跨 5 格时
        /// 中点那一小团贴图既盖不到线段两头，也没有任何方向感。现在按 <see cref="ChainPartSpacing"/>
        /// 均匀铺满整条折线，拐弯处逐段换向 ⇒ 从施法者一路亮到最后一个目标。</para>
        /// </summary>
        private static void SyncChain(SkillEffect e, float progress, float alpha)
        {
            var n = e.renderers.Count;
            if (n <= 0) return;
            var pts = e.points;
            if (pts.Count < 2)
            {
                // "没目标也要留画面"的兜底路径只塞了 1 个点 ⇒ 钉在锚点上
                for (var i = 0; i < n; i++)
                    PlacePart(e, e.renderers[i], e.pos, 0f, 1f, 1f, progress, alpha, i * 3);
                return;
            }

            var total = 0f;
            for (var k = 0; k + 1 < pts.Count; k++) total += WorldLen(pts[k], pts[k + 1]);
            if (total <= 1e-4f) total = 1e-4f;

            for (var i = 0; i < n; i++)
            {
                var d = n <= 1 ? 0f : total * i / (n - 1f);
                Vector2 gp;
                float ang;
                PointAlong(pts, d, out gp, out ang);
                PlacePart(e, e.renderers[i], gp, ang, 1f, 1f, progress, alpha, i * 3);
            }
        }

        private static readonly Vector2[] SummonHeadOffset =
        {
            new Vector2(0f, 0f),            // 主蛇口
            new Vector2(-0.42f, -0.30f),    // 左后小蛇口
            new Vector2(0.42f, -0.30f),     // 右后小蛇口
        };

        /// <summary>
        /// 多头蛇：主蛇口 + 两侧小一号的小蛇口，各播自己的帧相位 ⇒ 读作"多头"而不是一簇火。
        /// <para>原版是召唤怪物（本工程没有召唤怪物精灵管线）⇒ 蛇身画不出来；但"原地不动的喷火口"
        /// 是它最认得出的特征，所以用三根持续燃烧的原版高火柱（`groundFireBig`）顶替，
        /// 开火仍由 `SkillModule.ResolveEffectTick` 单独驱动（**份数是纯表现，不影响伤害**）。</para>
        /// </summary>
        private static void SyncSummon(SkillEffect e, float progress, float alpha)
        {
            for (var i = 0; i < e.renderers.Count; i++)
            {
                var r = e.renderers[i];
                var off = SummonHeadOffset[i % SummonHeadOffset.Length];
                var p = new Vector2(e.pos.x + off.x, e.pos.y + off.y);
                var k = i == 0 ? 1f : 0.62f;
                r.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                r.transform.localScale = new Vector3(1f, k, 1f);
                r.transform.position = WorldPosOf(e, p);
                ApplyFrame(e, r, i * 7, progress, alpha);
                SetOrder(r, r.transform.position, CloverEngine.ProjectileRuntime.GridOf(p));
            }
        }

        /// <summary>沿折线取距起点 <paramref name="dist"/>（世界单位）处的**格**坐标与该段的世界角度。</summary>
        private static void PointAlong(System.Collections.Generic.List<Vector2> pts, float dist,
            out Vector2 gridPos, out float angleDeg)
        {
            gridPos = pts[0];
            angleDeg = 0f;
            for (var k = 0; k + 1 < pts.Count; k++)
            {
                var a = pts[k];
                var b = pts[k + 1];
                var wa = Projectile.WorldOf(a);
                var wb = Projectile.WorldOf(b);
                var dx = wb.x - wa.x;
                var dy = wb.y - wa.y;
                var len = Mathf.Sqrt(dx * dx + dy * dy);
                if (len <= 1e-5f) continue;
                if (dist <= len || k + 2 == pts.Count)
                {
                    var t = Mathf.Clamp01(dist / len);
                    gridPos = new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
                    angleDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    return;
                }
                dist -= len;
            }
        }

        /// <summary>两点所在直线段的**世界**角度（度）：把素材的 +X 轴转向该方向。</summary>
        private static float StretchAngle(Vector2 from, Vector2 to)
        {
            var wa = Projectile.WorldOf(from);
            var wb = Projectile.WorldOf(to);
            var dx = wb.x - wa.x;
            var dy = wb.y - wa.y;
            if (dx * dx + dy * dy <= 1e-8f) return 0f;
            return Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        }

        /// <summary>把一份摆到 <paramref name="gridPos"/>（+ 旋转 + 拉伸/放大）并刷帧/刷档位。</summary>
        private static void PlacePart(SkillEffect e, SpriteRenderer r, Vector2 gridPos, float angleDeg,
            float stretch, float boost, float progress, float alpha, int part)
        {
            if (r == null) return;
            r.transform.position = WorldPosOf(e, gridPos);
            r.transform.localRotation = Quaternion.Euler(0f, 0f, angleDeg);
            r.transform.localScale = new Vector3(stretch * boost, boost, 1f);
            ApplyFrame(e, r, part, progress, alpha);
            SetOrder(r, r.transform.position, CloverEngine.ProjectileRuntime.GridOf(gridPos));
        }

        /// <summary>素材在世界空间的宽度（`Sprite.bounds` 已按 PPU 折算）。按素材键缓存，量不到按 1 单位估。</summary>
        private static readonly System.Collections.Generic.Dictionary<string, float> _artWidth
            = new System.Collections.Generic.Dictionary<string, float>();

        private static float ArtWorldWidth(string art, SpriteRenderer probe)
        {
            if (string.IsNullOrEmpty(art)) return 1f;
            if (_artWidth.TryGetValue(art, out var cached)) return cached;
            var sp = probe != null ? probe.sprite : null;
            var w = 0f;
            if (sp != null && FramesOf(art) > 0)
            {
                try { w = sp.bounds.size.x; } catch (System.Exception) { w = 0f; }
            }
            if (w <= 0.0001f) return 1f;      // 首帧占位图 / 离线宿主：先不写缓存，下一帧量真图
            _artWidth[art] = w;
            return w;
        }

        /// <summary>取一张帧图（异步未到位 ⇒ 返回 null，保留上一张，与 `ProjectileView.Sync` 同口径）。</summary>
        private static void ApplyFrame(SkillEffect e, SpriteRenderer r, int part, float progress, float alpha)
        {
            if (r == null) return;

            var art = ArtOf(e, part);
            var frames = FramesOf(art);
            if (frames > 0)
            {
                // 逐份错开半帧：一排火墙看起来才不是"同一张图复印"
                var shift = frames > 1 ? (part % frames) : 0;
                var f = (FrameOf(art, progress) + shift) % frames;
                var key = FrameKeyOf(art, Dir8.S, f);
                var sprite = SpriteFrames.Resolve(key);
                if (sprite != null && r.sprite != sprite) r.sprite = sprite;
                if (r.sprite == null) r.sprite = Disc;        // 首帧未到位：先给"看得见的占位"，下一帧就换真图
            }
            else
            {
                r.sprite = e.kind == SkillFxKind.Impact ? Disc : Ring;
            }

            var c = ColorOf(e, part);
            c.a *= alpha;
            r.color = c;
        }

        /// <summary>
        /// 该效果现在该取的贴图（建节点当帧用；与 `ProjectileView.ResolveSprite` 同口径：
        /// 有原版帧就先 `Resolve`，未到位 ⇒ 环/盘占位）。
        /// </summary>
        private static Sprite ResolveSprite(SkillEffect e, int part)
        {
            var art = ArtOf(e, part);
            if (FramesOf(art) > 0)
            {
                var sprite = SpriteFrames.Resolve(FrameKeyOf(art, Dir8.S, 0));
                if (sprite != null) return sprite;
                return e.kind == SkillFxKind.Impact ? Disc : Ring;
            }

            SkillLog.WarnOnce("fx.art.missing." + (art ?? "null"),
                $"技能效果素材 \"{art}\" 缺失（未导出 / `cel_file` 为空）⇒ 该技能退成**程序化等距环/盘**" +
                "（看得见的缺失信号；补图 = 重跑 tools/d2codec/export_missiles.py）");
            return e.kind == SkillFxKind.Impact ? Disc : Ring;
        }

        /// <summary>等距**椭圆环**（64×32；空心中等粗，配 `SpriteRenderer.color` 上色）。</summary>
        private static Sprite Ring
        {
            get
            {
                if (_ring != null) return _ring;
                _ring = BuildEllipse(inner: 0.62f, edge: 0.22f);
                _ring.name = "SkillFxRing";
                return _ring;
            }
        }

        /// <summary>等距**椭圆盘**（64×32；心部实、边缘渐隐，用于冲击闪光）。</summary>
        private static Sprite Disc
        {
            get
            {
                if (_disc != null) return _disc;
                _disc = BuildEllipse(inner: 0f, edge: 0.95f);
                _disc.name = "SkillFxDisc";
                return _disc;
            }
        }

        /// <summary>
        /// 生成一张等距椭圆（`inner` = 实心半径比例，`inner=0` ⇒ 实心盘；`edge` = 之后到边缘的渐隐宽度）。
        /// 纯函数式绘制，无随机 ⇒ 同一参数每次结果逐像素相同。
        /// </summary>
        private static Sprite BuildEllipse(float inner, float edge)
        {
            var tex = new Texture2D(RingTexW, RingTexH, TextureFormat.RGBA32, false)
            {
                name = "SkillFxEllipse",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[RingTexW * RingTexH];
            var cx = (RingTexW - 1) * 0.5f;
            var cy = (RingTexH - 1) * 0.5f;
            var rx = RingTexW * 0.5f - 1f;
            var ry = RingTexH * 0.5f - 1f;

            for (var y = 0; y < RingTexH; y++)
            {
                for (var x = 0; x < RingTexW; x++)
                {
                    var dx = (x - cx) / rx;
                    var dy = (y - cy) / ry;
                    var d = Mathf.Sqrt(dx * dx + dy * dy);          // 0 = 圆心，1 = 边缘
                    float a;
                    if (inner > 0f)
                    {
                        // 环：inner 之外开始出现，edge 段内渐隐到 0
                        var t = (d - inner) / Mathf.Max(0.0001f, (1f - inner) * edge + 0.0001f);
                        a = d < inner ? 0f : Mathf.Clamp01(1f - t);
                    }
                    else
                    {
                        a = d <= 1f ? Mathf.Clamp01(1f - Mathf.Pow(d, 3f)) : 0f;
                    }
                    px[y * RingTexW + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return Sprite.Create(tex, new Rect(0f, 0f, RingTexW, RingTexH), new Vector2(0.5f, 0.5f),
                GameConst.PixelsPerUnit);
        }
    }
}
