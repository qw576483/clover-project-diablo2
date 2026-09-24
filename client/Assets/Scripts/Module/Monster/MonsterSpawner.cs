// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Monster/MonsterSpawner.cs
// 按 **区域** 刷怪 + 精英词缀（全部读配表，零硬编码数值）。
//
// 数据链路：
//   `level_c`（区域）→ `monsters`（种类 id 列表）/ `mon_density`（官方 `MonDen`，千分比）/
//                       `mon_umin`·`mon_umax`（官方 `MonUMin/MonUMax`，精英群数）
//     ↓
//   `monster_c`（种类）→ hp/ac/ar/dmg/exp/ai/speed/res_*/sprite/corpse_usable/treasure_class
//     ↓
//   `monumod_c`（精英词缀）→ hp_mul/dmg_mul/ac_mul/tohit_add/res_*/cpick·upick（抽取权重）
//   `monster_c.min_grp/max_grp` → 官方"成群出现"的群大小
//
//   · **普通怪** = 原版逐格抽样命中的格（每格 `MonDen/100000`）⇒ 命中格各刷一群；
//     出处 `Diablerie/.../Engine/World/LevelBuilder.cs:225-237`；
//   · **精英群**（`mon_umin/mon_umax`）仍自己挑锚点：洞穴用 `MonsterSpawns`、
//     野外用 `RandomWalkableTile(rng)`（官方只规定"刷几群"，没规定落点）。
//      普通怪不再用它 —— 但仍由 `MapGenCave` 继续产出（契约 `IMapModule` 的字段没改、宿主仍在断言它）。
//
// 官方 `MonAi.txt` 的 AI 名 → 契约的 4 种 `MonsterAI` 的**映射表**见 `MapAi`（唯一一处）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（语义与序号和改动前**完全一致**）。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;

namespace Diablo2.Module.Monster
{
    /// <summary>按区域刷怪 + 精英词缀。</summary>
    internal sealed class MonsterSpawner
    {
        /// <summary>
        /// 逐格抽样的分母 = **100000**（原版口径，不是本项目定的）。
        /// <para>出处：参考工程 `Diablerie/.../Engine/World/LevelBuilder.cs:231-233`
        /// （`int sample = Random.Range(0, 100000); if (sample >= density) continue;`）
        /// ⇒ 每格命中概率 = `MonDen / 100000`。同一口径的第二处旁证 = 参考实现
        /// `libd2/.../game/src/monpop.zig:23`（"The per-slot density gate (game seed % 100000 <= MonDen)"）。</para>
        /// </summary>
        private const int DensitySampleRange = 100000;

        /// <summary>
        /// 【**安全阀，不是设计值**】一个区域一次最多刷多少群。
        /// <para>
        /// 官方**没有**这一项：官方就是"逐格独立抽样，命中多少格就刷多少群"
        /// （见 <see cref="DensitySampleRange"/> 的出处）⇒ 本常数只用于**防呆**
        /// （`level_c.mon_density` 多写一位数时别一次刷出上千只把帧率打死），
        /// 不许拿它当"设计上的群数上限"用。
        /// </para>
        /// <para>
        /// 取 200 的依据：80×80 血腥荒野实测平均可走 4399 格
        /// </para>
        /// </summary>
        private const int MaxPacksSafetyValve = 200;

        /// <summary>
        /// 为某区域生成全部怪物（**不**发事件、**不**建视图 —— 那是 `MonsterModule` 的事）。
        /// </summary>
        public void Build(MonsterModule owner, AreaId area, Rng rng, List<MonsterRuntime> into)
        {
            // `AreaId`（0 基）与 `level_c.id`（1 基）口径不一致 ⇒ 统一经 AreaLevelTable 解析
            var areaRow = AreaLevelTable.Resolve(area, false);
            if (areaRow == null)
            {
                MonsterLog.Error($"Build({area})：解析不到 level_c 行（配表未加载？）⇒ 本次不刷怪");
                return;
            }

            var species = areaRow.Monsters;
            if (species == null || species.Length == 0)
            {
                MonsterLog.Info($"SpawnArea({area}={areaRow.Name})：配表 monsters 列为空 ⇒ 本区域无怪（罗格营地即如此）");
                return;
            }
            if (areaRow.MonDensity <= 0)
            {
                MonsterLog.Info($"SpawnArea({area}={areaRow.Name})：mon_density={areaRow.MonDensity} ⇒ 本区域无怪");
                return;
            }

            var playerSpawn = owner.Map != null ? owner.Map.SpawnPoint : Vector2Int.zero;
            var occupied = new HashSet<Vector2Int>();

            // ── 群：**原版口径 = 逐格抽样**（不是"密度 ÷ 常数 = 群数"）────────────────
            //   出处：参考工程 `Diablerie/.../Engine/World/LevelBuilder.cs:225-237`
            //     int density = info.monDen[0];
            //     for (x…) for (y…) { int sample = Random.Range(0, 100000);
            //                         if (sample >= density) continue;  ← 命中
            //                         Spawn(monStats[Random.Range(0, monStats.Length)], x, y, …); }
            //   —— `LevelBuilder.Spawn`（同文件 :242-264）拿到命中格后再做一次**可通行检查**
            //   （不可通行直接 return，不刷），然后 `count = Random.Range(minGrp, maxGrp + 1)` 在**同一格**刷一群。
            //   ⇒ 每格命中概率 = `MonDen / 100000`；群大小 = `monster_c.min_grp..max_grp`（配表）。
            //   旁证（同一口径的第二处出处）：`libd2/.../game/src/monpop.zig:23`
            //     "The per-slot density gate (game seed % 100000 <= MonDen)"。
            if (owner.Map == null || !owner.Map.IsGenerated || owner.Map.Width * owner.Map.Height <= 0)
            {
                MonsterLog.Error($"SpawnArea({area})：地图未生成（无格可抽样）⇒ 本次不刷怪");
                return;
            }

            var map = owner.Map;
            var mapSize = map.Width * map.Height;
            var walkable = map.WalkableCount;

            var hits = new List<Vector2Int>();     // 命中的格 = 群锚点
            var hitBlocked = 0;                    // 命中但不可通行（原版同样在该格放弃）
            var hitTooClose = 0;                   // 命中但离玩家出生点太近（本项目守卫，见 MonsterTuning）
            for (var gx = 0; gx < map.Width; gx++)
            {
                for (var gy = 0; gy < map.Height; gy++)
                {
                    // 命中判定与官方**逐字同构**：`sample = rand(0,100000); if (sample >= MonDen) continue;`
                    if (rng.Next(0, DensitySampleRange) >= areaRow.MonDensity) continue;

                    var g = new Vector2Int(gx, gy);
                    if (!map.Walkable(g)) { hitBlocked++; continue; }
                    if (Iso.GridDistanceEuclidean(g, playerSpawn) < MonsterTuning.SpawnMinDistanceFromPlayer)
                    {
                        hitTooClose++;
                        continue;
                    }
                    hits.Add(g);
                }
            }
            var sampled = hits.Count;

            // ── 安全阀（**不是设计值**）：官方没有"每区域最多几群"这一项，命中多少就刷多少 ──
            if (hits.Count > MaxPacksSafetyValve)
            {
                MonsterLog.Warn($"SpawnArea({area}={areaRow.Name})：逐格抽样命中 {hits.Count} 群 > 安全阀 " +
                                $"{MaxPacksSafetyValve} 群 ⇒ 只保留前 {MaxPacksSafetyValve} 群。" +
                                "⚠️ 这是**安全阀、不是设计值**（官方按 MonDen 逐格抽样，命中多少刷多少）；" +
                                "命中数这么高通常是 level_c.mon_density 多写了一位数");
                hits.RemoveRange(MaxPacksSafetyValve, hits.Count - MaxPacksSafetyValve);
            }

            // ── 本区域可用的怪种（原版：命中格从 `level_c.monsters` 里**随机挑一种**）──
            //   出处：`LevelBuilder.cs:235` `monStats[Random.Range(0, monStats.Length)]`
            //   （Diablerie 先按 `NumMon` 从 `info.monsters` 里抽不重复的 N 种，本项目的
            //    `level_c.monsters` 恰好就是那 N 种 ⇒ 这里直接在本区域怪种里均匀抽）。
            var rows = new List<Table.BaseMonsterRow>();
            for (var s = 0; s < species.Length; s++)
            {
                var row = Table.Tables.Default.Monster.Get(species[s]);
                if (row == null)
                {
                    MonsterLog.Warn($"SpawnArea({area})：monster_c 里找不到种类 id={species[s]}（level_c.monsters 列）⇒ 跳过");
                    continue;
                }
                rows.Add(row);
            }
            if (rows.Count == 0)
            {
                MonsterLog.Error($"SpawnArea({area})：level_c.monsters 里的种类在 monster_c 里一个都没有 ⇒ 本次不刷怪");
                return;
            }

            // ── 精英群数（官方 MonUMin/MonUMax）；本表三区域都是 0 ⇒ 保底 1 群，否则词缀永不可见 ──
            var elitePacks = areaRow.MonUmax;
            if (elitePacks < areaRow.MonUmin) elitePacks = areaRow.MonUmin;
            if (elitePacks > MonsterTuning.MaxElitePacks) elitePacks = MonsterTuning.MaxElitePacks;
            if (elitePacks <= 0)
            {
                elitePacks = MonsterTuning.ElitePackFallback;
                MonsterLog.Warn($"精英怪保底：level_c.mon_umin/mon_umax={areaRow.MonUmin}/{areaRow.MonUmax}（官方实际值）" +
                                $"⇒ **本项目新增**保底刷 {elitePacks} 群精英，否则「精英词缀」在整个 Act I 都不可见");
            }

            var normal = 0;
            var elite = 0;
            var eliteMods = new List<string>();

            // ── 普通怪：**每个命中的格刷一群**（怪种在该区域的怪种里随机挑）──
            for (var p = 0; p < hits.Count; p++)
            {
                var row = rows[rng.Next(rows.Count)];
                normal += SpawnPackAt(owner, row, area, false, null, rng, hits[p], playerSpawn, occupied, into);
            }

            // ── 精英怪：用第一个种类刷若干群 ──
            if (species.Length > 0)
            {
                var eliteRow = Table.Tables.Default.Monster.Get(species[0]);
                if (eliteRow == null)
                {
                    MonsterLog.Warn($"SpawnArea({area})：精英群所需的 monster_c id={species[0]} 不存在 ⇒ 不刷精英");
                }
                else
                {
                    for (var p = 0; p < elitePacks; p++)
                    {
                        var kind = p % 2;      // 0 = 冠军怪 / 1 = 精英（唯一）怪，交替（官方两类都有）
                        var mod = PickEliteMod(rng, kind);
                        if (mod == null)
                        {
                            MonsterLog.Warn($"SpawnArea({area})：monumod_c 里没有 kind={kind} 且权重 > 0 的词缀 ⇒ 本群按普通怪刷");
                            normal += SpawnPack(owner, eliteRow, area, false, null, rng, playerSpawn, occupied, into);
                            continue;
                        }
                        var n = SpawnPack(owner, eliteRow, area, true, mod, rng, playerSpawn, occupied, into);
                        elite += n;
                        if (n > 0) eliteMods.Add($"{mod.Name}(id={mod.Id},{(mod.Kind == 0 ? "冠军" : "精英")})");
                    }
                }
            }

            var expected = walkable * areaRow.MonDensity / (double)DensitySampleRange;
            MonsterLog.Info($"SpawnArea({area}={areaRow.Name})完成：普通 {normal} 只 + 精英 {elite} 只 = {normal + elite} 只" +
                            $"（**原版逐格抽样**：扫 {mapSize} 格（可走 {walkable}）× 每格 {areaRow.MonDensity}/{DensitySampleRange}" +
                            $" = {areaRow.MonDensity / (double)DensitySampleRange:P3} ⇒ 命中 {sampled} 群" +
                            $"（其中不可走 {hitBlocked} / 离出生点 < {MonsterTuning.SpawnMinDistanceFromPlayer:0.#} 格 {hitTooClose}）；" +
                            $"落到 {hits.Count} 群，理论期望 {expected:F1} 群；精英 {elitePacks} 群；" +
                            $"怪种 {rows.Count}/{species.Length}（level_c.monsters={string.Join(";", species)}）；" +
                            $"区域等级 {areaRow.AreaLevel}；精英词缀={string.Join("、", eliteMods)}）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 群 / 单只
        // ═════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 刷一群（官方：`min_grp`~`max_grp` 只聚在一起）——**锚点由调用方给定**。
        /// <para>
        /// 锚点 = 原版逐格抽样**命中的那一格**（`LevelBuilder.cs:227-236` 的 `x, y` 直接交给
        /// `Spawn(monStat, x, y, …)`）⇒ 群落在命中格及其 8 邻/半径 2（见 <see cref="TryPlaceNear"/>，
        /// 它保证成员都落在可走且未被占的格上）。
        /// </para>
        /// </summary>
        private static int SpawnPackAt(MonsterModule owner, Table.BaseMonsterRow row, AreaId area, bool isElite,
            Table.BaseMonumodRow mod, Rng rng, Vector2Int anchor, Vector2Int playerSpawn,
            HashSet<Vector2Int> occupied, List<MonsterRuntime> into)
        {
            var minGrp = row.MinGrp < 1 ? 1 : row.MinGrp;
            var maxGrp = row.MaxGrp < minGrp ? minGrp : row.MaxGrp;
            var count = maxGrp > minGrp ? rng.Next(minGrp, maxGrp + 1) : minGrp;

            var spawned = 0;
            for (var i = 0; i < count; i++)
            {
                if (!TryPlaceNear(owner, rng, anchor, i, playerSpawn, occupied, out var grid)) break;

                var m = Create(owner, row, area, grid, isElite ? mod : null, rng);
                into.Add(m);
                occupied.Add(grid);
                spawned++;
            }
            return spawned;
        }

        /// <summary>
        /// 刷一群，**锚点自己挑**（`PickAnchor`）。
        /// <para>
        /// 只有**精英群**走这条（官方 `MonUMin/MonUMax` 只说"刷几群精英"，没写落点规则）
        /// —— 普通怪走 <see cref="SpawnPackAt"/>，锚点由原版逐格抽样给定。
        /// </para>
        /// </summary>
        private static int SpawnPack(MonsterModule owner, Table.BaseMonsterRow row, AreaId area, bool isElite,
            Table.BaseMonumodRow mod, Rng rng, Vector2Int playerSpawn, HashSet<Vector2Int> occupied,
            List<MonsterRuntime> into)
        {
            var anchor = PickAnchor(owner, rng, playerSpawn);
            if (!anchor.HasValue)
            {
                MonsterLog.WarnThrottled("spawn.noanchor",
                    $"SpawnPack: 找不到可用的刷新点（区域 {area}，地图未生成？）⇒ 本群跳过");
                return 0;
            }
            return SpawnPackAt(owner, row, area, isElite, mod, rng, anchor.Value, playerSpawn, occupied, into);
        }

        /// <summary>
        /// 选一个群锚点（**只给精英群用**）：洞穴用 `MonsterSpawns`，野外用 `RandomWalkableTile`；
        /// 并保证离玩家出生点 ≥ `MonsterTuning.SpawnMinDistanceFromPlayer`。
        /// <para>
        /// </para>
        /// </summary>
        private static Vector2Int? PickAnchor(MonsterModule owner, Rng rng, Vector2Int playerSpawn)
        {
            var map = owner.Map;
            if (map == null || !map.IsGenerated) return null;

            var spawns = map.MonsterSpawns;
            var useSpawns = spawns != null && spawns.Count > 0;

            for (var attempt = 0; attempt < MonsterTuning.SpawnPlaceAttempts; attempt++)
            {
                Vector2Int g;
                if (useSpawns)
                {
                    g = spawns[rng.Next(spawns.Count)];
                }
                else
                {
                    g = map.RandomWalkableTile(rng);
                }

                if (!map.Walkable(g)) continue;
                if (Iso.GridDistanceEuclidean(g, playerSpawn) < MonsterTuning.SpawnMinDistanceFromPlayer)
                {
                    // 太贴脸：再抽一次（洞穴的刷新点是地图给的，抽到近处就多试几次）
                    continue;
                }
                return g;
            }

            var fallback = useSpawns ? spawns[rng.Next(spawns.Count)] : map.RandomWalkableTile(rng);
            if (map.Walkable(fallback))
            {
                MonsterLog.WarnThrottled("spawn.nearplayer",
                    $"PickAnchor: {MonsterTuning.SpawnPlaceAttempts} 次都没找到离出生点 " +
                    $"{MonsterTuning.SpawnMinDistanceFromPlayer:0.0} 格以外的落点 ⇒ 接受 {fallback}（可能贴脸）");
                return fallback;
            }

            MonsterLog.Warn("PickAnchor: 随机落点全部不可走（地图异常）⇒ 本群跳过");
            return null;
        }

        /// <summary>在锚点附近找一个**可走且未被占**的格（锚点 → 8 邻 → 半径 2 → 随机兜底）。</summary>
        private static bool TryPlaceNear(MonsterModule owner, Rng rng, Vector2Int anchor, int index,
            Vector2Int playerSpawn, HashSet<Vector2Int> occupied, out Vector2Int grid)
        {
            var map = owner.Map;
            grid = anchor;

            if (index == 0 && map.Walkable(anchor) && !occupied.Contains(anchor)) return true;

            if (TryNeighbors(map, anchor, occupied, out grid)) return true;
            if (TryRing(map, anchor, 2, occupied, out grid)) return true;

            for (var attempt = 0; attempt < MonsterTuning.SpawnPlaceAttempts; attempt++)
            {
                var g = map.RandomWalkableTile(rng);
                if (!map.Walkable(g) || occupied.Contains(g)) continue;
                if (Iso.GridDistanceEuclidean(g, playerSpawn) < MonsterTuning.SpawnMinDistanceFromPlayer) continue;
                grid = g;
                return true;
            }

            MonsterLog.WarnThrottled("spawn.noplace",
                $"TryPlaceNear: 锚点 {anchor} 附近没有空闲可走格（群成员放不下）⇒ 本群提前结束");
            return false;
        }

        private static bool TryNeighbors(IMapModule map, Vector2Int c, HashSet<Vector2Int> occupied, out Vector2Int grid)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    var g = new Vector2Int(c.x + dx, c.y + dy);
                    if (!map.Walkable(g) || occupied.Contains(g)) continue;
                    grid = g;
                    return true;
                }
            }
            grid = c;
            return false;
        }

        private static bool TryRing(IMapModule map, Vector2Int c, int r, HashSet<Vector2Int> occupied, out Vector2Int grid)
        {
            for (var x = c.x - r; x <= c.x + r; x++)
            {
                for (var y = c.y - r; y <= c.y + r; y++)
                {
                    var g = new Vector2Int(x, y);
                    if (!map.Walkable(g) || occupied.Contains(g)) continue;
                    grid = g;
                    return true;
                }
            }
            grid = c;
            return false;
        }

        // ═════════════════════════════════════════════════════════════════════
        // 单只怪物（属性 / 精英词缀）
        // ═════════════════════════════════════════════════════════════════════
        private static MonsterRuntime Create(MonsterModule owner, Table.BaseMonsterRow row, AreaId area,
            Vector2Int grid, Table.BaseMonumodRow mod, Rng rng)
        {
            var state = new MonsterState
            {
                id = owner.NextMonsterId(),
                kindId = row.Id,
                name = row.Name,
                // 怪物等级：`monster_c.level` 就是"按区域等级换算后的等级"（官方 MonLvl 倍率已在打表时折进数值）
                level = row.Level,
                hp = row.Hp,
                maxHp = row.Hp,
                ai = MapAi(row.Ai),
                alive = true,
                isChampion = false,
                modId = 0,
                modName = string.Empty,
                damageMin = row.DmgMin,
                damageMax = row.DmgMax,
                defense = row.Ac,
                attackRating = row.Ar,
                exp = row.Exp,
                dir = (Dir8)rng.Next(0, 8),
                corpseUsable = row.CorpseUsable != 0,
                attacking = false,
                hitStun = false,
            };

            var m = new MonsterRuntime
            {
                State = state,
                Row = row,
                Area = area,
                Home = grid,
                //   顺序**必须**与 `Def.DamageType` 逐值一致：物(0)/火(1)/冰(2)/电(3)/毒(4)/魔(5)
                //   —— `MonsterRuntime.ResistOf` 就是按 `(int)type` 下标取的。
                Resists = new[]
                {
                    row.ResPhys, row.ResFire, row.ResCold, row.ResLight, row.ResPoison, row.ResMagic,
                },
            };
            m.SnapTo(grid);
            m.Sync();                       // 写回 gridX/gridY/worldX/Y/Z/dir

            var baseHp = state.maxHp;
            var baseDmg = state.damageMin + "-" + state.damageMax;
            var baseAr = state.attackRating;

            if (mod != null) ApplyElite(m, mod);

            MonsterLog.Info($"spawn m#{state.id} {state.name} lvl={state.level} hp={state.hp}/{state.maxHp} " +
                            $"dmg={state.damageMin}-{state.damageMax} ac={state.defense} ar={state.attackRating} " +
                            $"exp={state.exp} ai={state.ai} speed={row.Speed:0.0} 格=({grid.x},{grid.y}) " +
                            $"区域={area} 尸体可用={state.corpseUsable} 抗性=[物{row.ResPhys} 火{row.ResFire} " +
                            $"冰{row.ResCold} 电{row.ResLight} 毒{row.ResPoison} 魔{row.ResMagic}]");

            if (mod != null)
            {
                MonsterLog.Info($"elite m#{state.id}「{state.name}」词缀={state.modName}(id={state.modId}) " +
                                $"hp {baseHp}→{state.maxHp}（×{mod.HpMul:0.0}） dmg {baseDmg}→" +
                                $"{state.damageMin}-{state.damageMax}（×{mod.DmgMul:0.0}） ar {baseAr}→{state.attackRating}" +
                                $"（+{mod.TohitAdd}%） ac ×{mod.AcMul:0.0}");
            }

            return m;
        }

        /// <summary>
        /// 精英词缀（`monumod_c`）：**乘/加**到基础属性上。
        /// <para>
        /// 倍率列（`hp_mul/dmg_mul/ac_mul/tohit_add/res_*`）由打表脚本按官方 `MonUMod.txt` 的
        /// **引擎常数**（`champion +hp%` / `unique +hp%` / `champion +dmg%` / `+tohit%` …）程序化算出，
        /// 本文件只负责"乘上去"，不猜任何数字。
        /// </para>
        /// </summary>
        private static void ApplyElite(MonsterRuntime m, Table.BaseMonumodRow mod)
        {
            var s = m.State;

            s.isChampion = true;
            s.modId = mod.Id;
            s.modName = mod.Name;
            s.name = mod.Name + " " + m.Row.Name;        // 名字前缀（HUD 显示，验收要求"精英怪要可见"）

            s.maxHp = Mathf.Max(1, Mathf.RoundToInt(s.maxHp * mod.HpMul));
            s.hp = s.maxHp;
            s.damageMin = Mathf.Max(1, Mathf.RoundToInt(s.damageMin * mod.DmgMul));
            s.damageMax = Mathf.Max(s.damageMin, Mathf.RoundToInt(s.damageMax * mod.DmgMul));
            s.exp = Mathf.Max(1, Mathf.RoundToInt(s.exp * mod.HpMul));   // 官方：精英血量倍率同时抬高经验
            s.defense = Mathf.Max(0, Mathf.RoundToInt(s.defense * mod.AcMul));
            s.attackRating = Mathf.Max(0, Mathf.RoundToInt(s.attackRating * (1f + mod.TohitAdd / 100f)));

            // 抗性加成（官方 MonUMod 未定义该常数，打表出来是 0 ⇒ 这里做通用加法）
            // 注意：**这里不夹取** —— 官方对**怪物**的抗性不设上限（参考实现 `combat.zig:299-313`
            // "MONSTERS have NO cap"），结算处 `DamageFormula.ApplyResist` 也只在 `>= 100`（免疫）时归零。
            // 玩家侧的元素抗性上限 75% 由 `Module/Player/PlayerStats.MaxResist` 在**取值时**夹好。
            //   **不是** `monumod_c` 的列序（那张表的列序是 物/魔/火/冰/电/毒）。
            var add = new[] { mod.ResPhys, mod.ResFire, mod.ResCold, mod.ResLight, mod.ResPoison, mod.ResMagic };
            for (var i = 0; i < m.Resists.Length && i < add.Length; i++)
            {
                m.Resists[i] = m.Resists[i] + add[i];
            }

            m.Sync();
            m.ViewDirty = true;
        }

        /// <summary>按权重抽一条精英词缀（`cpick` = 冠军怪权重 / `upick` = 精英怪权重）。</summary>
        private static Table.BaseMonumodRow PickEliteMod(Rng rng, int kind)
        {
            var all = Table.Tables.Default.Monumod.All();
            if (all == null || all.Count == 0)
            {
                MonsterLog.WarnOnce("spawn.monumod.empty", "PickEliteMod: monumod_c 未加载（表为空）⇒ 不刷精英词缀");
                return null;
            }

            var candidates = new List<Table.BaseMonumodRow>();
            var weights = new List<int>();
            for (var i = 0; i < all.Count; i++)
            {
                var row = all[i];
                if (row == null || row.Kind != kind) continue;
                var w = kind == 0 ? row.Cpick : row.Upick;
                if (w <= 0) continue;
                candidates.Add(row);
                weights.Add(w);
            }

            if (candidates.Count == 0)
            {
                MonsterLog.WarnOnce("spawn.monumod.kind" + kind,
                    $"PickEliteMod: monumod_c 里没有 kind={kind} 且权重 > 0 的词缀 ⇒ 本次无词缀（配表核对）");
                return null;
            }

            // 权重列表非空且总和 > 0（上面已过滤）⇒ 不会触发 `Rng.PickWeighted` 的降频告警
            var idx = rng.PickWeighted(weights);
            if (idx < 0 || idx >= candidates.Count)
            {
                MonsterLog.Warn($"PickEliteMod: PickWeighted 返回越界下标 {idx}（候选 {candidates.Count}）⇒ 取第一条");
                idx = 0;
            }
            return candidates[idx];
        }

        /// <summary>
        /// 官方 `MonAi.txt` 的 AI 名 → 契约的 4 种 `MonsterAI`（**唯一映射处**，改名只动这里）。
        /// <para>
        /// `monster_c.ai` 列存的是官方 `MonStats.AI` 原文，本项目的 8 种怪对应：
        /// Fallen→Coward（原版堕落者被打就跑）/ FallenShaman→Shaman / QuillRat→Range（尖刺鼠放风筝）/
        /// Zombie·CorruptRogue·Brute·Wraith→Melee / BloodHawk→Range（飞行怪远程啄击）。
        /// </para>
        /// </summary>
        private static MonsterAI MapAi(string ai)
        {
            if (string.IsNullOrEmpty(ai))
            {
                MonsterLog.WarnThrottled("spawn.ai.empty", "MapAi: monster_c.ai 为空 ⇒ 按 Melee 处理（配表核对）");
                return MonsterAI.Melee;
            }

            switch (ai.Trim())
            {
                case "Fallen": return MonsterAI.Coward;
                case "FallenShaman": return MonsterAI.Shaman;
                case "QuillRat": return MonsterAI.Range;
                case "BloodHawk": return MonsterAI.Range;
                case "Zombie": return MonsterAI.Melee;
                case "CorruptRogue": return MonsterAI.Melee;
                case "Brute": return MonsterAI.Melee;
                case "Wraith": return MonsterAI.Melee;
                default:
                    MonsterLog.WarnThrottled("spawn.ai.unknown",
                        $"MapAi: 官方 AI「{ai}」未登记在映射表里 ⇒ 按 Melee 处理（需要时在 MonsterSpawner.MapAi 补一行）");
                    return MonsterAI.Melee;
            }
        }
    }
}
