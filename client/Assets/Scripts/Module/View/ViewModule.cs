// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/ViewModule.cs
// `IViewModule` 的**唯一实现**（门面，internal，**无参构造** ⇒ 可被 `AppContext.AutoWire` 反射创建）。
// 刻意**不是** MonoBehaviour：它的 `Tick(dt)` 由 `AppContext.Tick` 转发（与其它模块一致），
// 这样模块生命周期只有一个来源，也不会因为场景切换丢掉挂在别处的方式。
//
// 职责：创建/更新/销毁精灵视图（玩家 8 方向、怪物、地面物品）、受击闪白、死亡表现、
//       飘字（引擎 `Game.UI.FloatText`）、逐帧动画推进。
//         只有屏幕顶部 `EnemyBar`（`MouseSelection.cs:62-65`），"怪名 + 血量"现由 `UI/EnemyBarView` 承担。
//
// 动画：**业务自写逐帧切图**（`SpriteAnimator` / `SpriteFrames` / `ViewAnim`，本项目新增）
//       —— 引擎 `Game.Anim` 是 Animator 驱动，不覆盖逐帧切图（理由见 `SpriteAnimator.cs` 文件头）。
//
// 命中反馈三件套里的两件在本模块：
//   ① 飘字（`ShowFloatingText`，由 `Module/Combat/DamagePipeline` 在同一次命中里调用）
//   ③ （原"头顶血条下降"已按 u44-C4 删除；血量改由 `UI/EnemyBarView` 在悬停时读 `Def.MonsterState`）
//   ② 音效钩子由 `DamagePipeline` 直接调 `IAudioModule`（可空）。
//
//   —— Stage 场景卸载（回主菜单 / 换场景）会把它们销毁；一旦「清场没跑到」，
//   `_player.Root.transform`（原 ViewModule.cs:392）就会**每帧**抛 `MissingReferenceException`，
//   并且该异常从 `AppContext.Tick` 抛出 ⇒ 它之后的模块（Camera/Audio…）每帧都被中断。
// 对策（三道）：
//   ① `Tick` 开头**总闸门** `if (_root == null) return;`（根没了就整体不 tick；顺带丢弃残留引用）；
//   ② 每个「持有 Unity 引用并使用」的点**使用前判空**（Unity 的 `==` 重载把已销毁对象判为 null，
//      所以用 `== null` 判断即可，**不用** `ReferenceEquals`）：`TickPlayer` / `UpdateMonster` /
//      `PlayHit` / `PlayDeath` / `GetView` / `ApplyFlash` / `PruneDeadViews`；
//   ③ `Clear()`（Flow 离场会调）与 `StageLeft`（本模块自己订阅，幂等）**显式置 null**。
// 日志：引用失效时**只报一次**（`ViewLog.WarnOnce`，key = `stale.root` / `stale.children` / …），
//   文案带模块名与「已随场景卸载」，用于区分「正常卸载」与「真丢引用」。
//
// 素材：一律经 `Core/ResPaths`；取不到 ⇒ 纯色占位（`SpriteFrames.Placeholder`），
//       已登记 `client/资源欠缺清单.md` #1 角色精灵 / #2 怪物精灵。
// 不使用 `Game.Pool` 的 `Spawn`：它内部是 `Resources.Load<GameObject>(key)`，
//   而本项目**没有**实体预制体（视图由帧序列 PNG 运行时拼）⇒ 直接建节点并自行复用/销毁
//
// ① **像素尺度**：原版单位是 **80 像素 = 1 世界单位**（Diablerie `Iso.cs:9`；本项目地形
//    也是同一尺度 —— `MapView.D2TilePixelsPerUnit = 80f` 且瓦片节点缩 64/80）。
//    贴图按契约 PPU=64 导入 ⇒ 实体节点乘 `SpriteFrames.ArtScale`(=0.8)，
//    否则角色比地形大 25%（`SpriteFrames.cs` 文件头有完整推导）。
//    本模块监听 `Events.StageEntered`，从 `IMapModule.NpcPoints`（下标 = `Def.NpcId`）
//    建 NPC 视图，帧键走 `SpriteFrames.Keys(NpcSpriteCode(id), …)`（原版 `MonStats.Code`：
//    ps/rc/ci/gh/wa）⇒ **用的是原版 NPC 动画，不是色块**。NPC 只进 `_npcs` 字典，
//    **不进 `_entities`** ⇒ 不参与战斗/血条/命中（NPC 无敌、不受伤）。
//
//   **每格只播 2 帧**（严重滑步 = 用户说的"飘着走"）；且玩家只有走路一套动画（跑也用 WL）。
//     ① **走/跑两套动画**：玩家按契约 `IPlayerModule.IsRunning` 选 `ViewAnim.Run` / `Walk`
//        （原版 `.cof` 的 RN / WL）⇒ 跑起来播的是原版跑动画；
//     ② **每格一个动画循环**：移动类动作的有效帧率 = **帧数 × 格/秒**
//        （`SpriteFrames.FpsForCycle` + `SpeedScaleForCycle`）⇒ 脚底与地面不再打滑。
//   速度来源：玩家取**契约常量**（跑 `GameConst.PlayerWalkSpeed`、走 × `PlayerWalkSpeedFactor`）；
//   怪物契约里没有速度值 ⇒ 用**本帧位移 / dt**（`_prevTickWorld` 逐帧做差，见 `TickOne`）。
//   静态动作（Idle/Attack/Cast/Hit/Death）**一律不缩放**（`SyncMoveScale` 复位成 1）。
//
//   审计口径：逐单位 × 逐动作记「方向数 / 帧数 / 帧文件 / 复用」，并单独记「动作是否真被触发」
//            （判据入口 = `tools/probes/hosts/animcheck`）。
//      （`PlayHit` 贴的 Hit[0] 与 `TickPlayer`/`TickOne` 贴回的 Idle 帧都在**渲染之前**的同一个
//      Update 里 ⇒ 亚马逊那 6 帧受击动画**一帧都不会被渲染** = "定义了但没人用"的最隐蔽形态；
//      `Hit` 排在 `Death` 之后、`Cast` 之前，保持条件 = **受击动画还没播完**（`!Anim.Finished`）。
//      改为只有 `Idle`/`Walk`/`Run` 循环（原版：一次出手 = 一套 A1，播完回静止）。
//      同时 `OnPlayerAttacked` / `OnSkillCast` / `PlayHit` 各补一次 `Anim.Replay()` ——
//      `PlayAnim` 的早退判据**不比较 loop**，连续出手/施法时不会自动重开（会接着放上一次的剩余帧）。
//   ③ **动作选择抽成纯函数** `ViewAnimState.SelectPlayer/SelectMonster`（本文件只喂状态）——
//      （这正是①能藏这么久的原因）。抽出后 `tools/probes/hosts/animcheck` 可逐帧驱动断言。
//   播放速度（`FpsOf` 的基准帧率）：原版 `AnimData.d2` 不在本机 ⇒ 无出处不许编。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.View
{
    /// <summary>精灵视图门面实现。</summary>
    internal sealed class ViewModule : IViewModule
    {
        /// <summary>受击闪白时长（秒）——顺带做一点反向位移，像素图上也看得出来（纯 tint 在白图上不可见）。</summary>
        private const float HitFlashSeconds = 0.12f;

        /// <summary>受击时的反向位移（世界单位，很小）——D2 的受击抖动。</summary>
        private const float HitKnockback = 0.06f;

        /// <summary>尸体最终不透明度（原版尸体是暗色的）。</summary>
        private const float CorpseAlpha = 0.55f;

        /// <summary>施法动作保持时长（秒）。</summary>
        private const float CastActionSeconds = 0.45f;

        /// <summary>
        /// 挥击动作保持时长（秒）。
        /// <para>口径 = <c>GameConst.PlayerAttackInterval</c>（原版普通攻击的出手间隔，见其注释
        /// 「原版攻击间隔 ~0.5s」）—— 即"一次挥击占满两次出手之间的时间"，
        /// 与怪物 AI 的出手-动作绑定口径一致（`MonsterTuning.AttackIntervalSeconds`）。
        /// 原版逐武器的攻击速度表（`Weapons.txt::speed` / `AnimData.d2`）本项目未接 ⇒ 用统一间隔。</para>
        /// <para>与 <c>GameConst.PlayerAttackInterval</c> **同源**（不另写一个魔数）。</para>
        /// </summary>
        private static readonly float AttackActionSeconds = GameConst.PlayerAttackInterval;

        private readonly Dictionary<int, EntityView> _entities = new Dictionary<int, EntityView>();
        private readonly Dictionary<int, EntityView> _groundItems = new Dictionary<int, EntityView>();

        /// <summary>
        /// <para>为什么需要它：契约里**没有"怪物速度"这个值**（`MonsterState` 只有位置/朝向），
        /// 而步频同步要的是实际速度。`MonsterModule` 每帧先调 `UpdateMonster` 把新世界坐标写进
        /// `v.LastWorld`（同一次 `AppContext.Tick` 里 Monster 在 View 之前）⇒ 这里逐帧做差就得到
        /// 实际速度，**不必新增契约字段**。玩家不用这条（它走契约速度，见 `TickPlayer`）。</para>
        /// </summary>
        private readonly Dictionary<int, Vector3> _prevTickWorld = new Dictionary<int, Vector3>();

        private readonly HashSet<int> _moveScaleLogged = new HashSet<int>();

        /// <summary>城镇 NPC 视图（键 = `(int)Def.NpcId`）。**不计入 `_entities`**（无血条/不参战）。</summary>
        private readonly Dictionary<int, EntityView> _npcs = new Dictionary<int, EntityView>();

        private bool _npcAdvanceLogged;

        private Transform _root;
        private Transform _pendingRoot;
        private EntityView _player;
        private bool _cannotRender;

        /// <summary>构造：订阅 `SkillCast`（玩家施法动作）与 `StageLeft`（离场兜底清引用）。</summary>
        public ViewModule()
        {
            if (Game.Event == null)
            {
                ViewLog.Warn("ViewModule 构造时 Game.Event 为 null（Game.Launch 未调用？）⇒ 施法动作不会播放、" +
                             "离场也不会自动清引用（仍靠 Tick 的总闸门兜底）");
                return;
            }
            Game.Event.On<int>(Events.SkillCast, OnSkillCast);
            Game.Event.On<int>(Events.PlayerAttacked, OnPlayerAttacked);
            //   而它持有的节点都属于 Stage 场景（会随场景卸载被销毁）⇒ 除 Flow 的 `Clear()` 之外，
            //   本模块自己再监听一次离场事件（幂等：`Clear()` 在已经干净时静默返回，不产生重复日志）。
            Game.Event.On(Events.StageLeft, OnStageLeft);
            // 装备变化 ⇒ 重取整套帧键（换的是"套"不是"贴图"，见 OnEquipChanged）。
            //   载荷 `Def.InventoryChangedArgs`（装备集已按双武器组收窄 = 只含**生效组**那把武器）。
            Game.Event.On<InventoryChangedArgs>(Events.EquipChanged, OnEquipChanged);
            Game.Event.On(Events.StageEntered, OnStageEntered);
            // u44（悬停选择表现 · 契约 C2）：鼠标悬停到谁 ⇒ 那个实体**整体变亮**
            //   （原版 `_Brightness` 3.0 / `_Contrast` 1.01；移开回 1.0/1.0）。
            //   驱动源 = `Events.HoverTargetChanged`（发送方 `Module/Input/InputReader.Publish`）
            //   —— 与顶部血条（`UI/EnemyBarView`）**同一个事件**、同一个载荷，两边各消费各的那一半；
            //   数值/属性名/手段的唯一真源 = `Module/View/EntityHighlight`（本文件不内联 3.0/1.01）。
            Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
        }

        // ═════════════════════════════════════════════════════════════════════
        // 悬停变亮（契约 C2；原版 `MouseSelection.HotEntity` ⇒ `COFRenderer.selected`）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>当前被点亮（悬停）的实体 id；-1 = 没有（玩家/NPC/空地悬停都不点亮）。</summary>
        private int _hoveredEntityId = -1;

        /// <summary>
        /// `Events.HoverTargetChanged` 的收方：把"上一个被悬停的实体"复位、把"这一个"点亮。
        /// <para>口径（**逐条来自参考实现，不许扩**）：
        ///   · 只有 `CursorKind.Attack`（= 指针下是**可攻击的怪物**）参与 —— 原版点亮的判据是
        ///     `entity.selected`，由 `MouseSelection.HotEntity` 驱动，而 `HotEntity` 只可能是
        ///     `CalcHotEntity` 选出的**可选中**实体；玩家被显式排除（`MouseSelection.cs:139-167`）。
        ///   · 掉落物 / NPC 不走这条路：原版对它们走 `label.Show`（世界内名字牌）而不是"变亮"，
        ///     本工程的地面物品名牌另有 `UI/GroundItemLabelView`、NPC 名字牌在 `UI/EnemyBarView`。</para>
        /// </summary>
        private void OnHoverChanged(Diablo2.Def.HoverTarget t)
        {
            var next = t != null && t.hasTarget && t.cursor == CursorKind.Attack && t.id >= 0 ? t.id : -1;
            if (next == _hoveredEntityId) return;

            var prev = _hoveredEntityId;
            _hoveredEntityId = next;

            // 先复位旧的、再点亮新的：同一帧里 id 变了也不会出现"两个同时亮"。
            SetEntityHighlighted(prev, false);
            SetEntityHighlighted(next, true);

            if (next != -1 || prev != -1)
            {
                ViewLog.Info($"[悬停变亮] {(prev == -1 ? "无" : ("m#" + prev))} → "
                    + (next == -1 ? "无" : $"m#{next}「{t.name}」")
                    + $"（{EntityHighlight.BrightnessProperty}={EntityHighlight.BrightnessFor(next != -1)}"
                    + $" / {EntityHighlight.ContrastProperty}={EntityHighlight.ContrastFor(next != -1)}"
                    + "；来源 = `D2.Input.HoverChanged`，CursorKind.Attack 才点亮）");
            }
        }

        /// <summary>把某个实体视图切到"悬停档/常规档"（视图不存在 ⇒ 静默跳过：它是正常时序）。</summary>
        private void SetEntityHighlighted(int entityId, bool highlighted)
        {
            if (entityId < 0) return;
            if (!_entities.TryGetValue(entityId, out var v) || v == null || v.Renderer == null) return;
            EntityHighlight.Apply(v.Renderer, v.OriginalMaterial, highlighted);
        }

        // ═════════════════════════════════════════════════════════════════════
        // IViewModule 之外：城镇 NPC（本项目新增，见文件头 ②）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>`Events.StageEntered`：城镇里按 `IMapModule.NpcPoints` 建 5 个 NPC 视图。</summary>
        private void OnStageEntered()
        {
            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map == null)
            {
                ViewLog.WarnOnce("npc.nomap", "StageEntered: IMapModule 未接入 ⇒ 不建 NPC 视图");
                return;
            }
            if (map.Area != AreaId.Town)
            {
                // 非城镇区域没有 NPC ⇒ 静默返回（正常路径，不是异常）
                return;
            }

            var points = map.NpcPoints;
            if (points == null || points.Count == 0)
            {
                ViewLog.Warn("StageEntered: 城镇的 NpcPoints 为空（MapGenTown 没填？）⇒ 不建 NPC 视图");
                return;
            }
            if (EnsureRoot() == null) return;

            ClearNpcs();
            for (var i = 0; i < points.Count; i++)
            {
                var id = (NpcId)i;
                var code = SpriteFrames.NpcSpriteCode(id);
                var world = Iso.GridToWorld(points[i]);
                var v = CreateEntityNode(NpcEntityId(id), world, NpcPlaceholderColor(id), "Npc_" + code,
                    EntitySortOrder(points[i]));
                if (v == null) continue;

                v.SpriteCode = code;
                v.Grid = points[i];
                v.Dir = Dir8.S;
                v.IsGroundItem = false;
                PlayAnim(v, ViewAnim.Idle, true);
                _npcs[i] = v;
                ViewLog.Info($"NPC 视图已建：{id}（原版代码 {code}）格=({points[i].x},{points[i].y}) " +
                             $"帧目录={ResPaths.MonsterDir(code)}");
            }

            // 贴图是异步加载的 ⇒ 到位后要重铺一次：交给 `SpriteFrames` 自己的
            // `ConsumeRepaintRequest()`（Tick 已经消费它，与玩家/怪物同一条通路）。
            ViewLog.Info($"NPC 视图：{_npcs.Count} 个（贴图异步到位后 Tick 会自动重铺一次）");
        }

        /// <summary>NPC 视图的实体 id（**负号区段**，与玩家 1 / 怪物 1000+ / 地面物品 100000+ 都不冲突）。</summary>
        private static int NpcEntityId(NpcId id)
        {
            return -1 - (int)id;
        }

        /// <summary>NPC 占位色（万一原版动画缺失时的兜底；5 个 NPC 各一色，便于一眼看出是哪一个）。</summary>
        private static Color NpcPlaceholderColor(NpcId id)
        {
            switch (id)
            {
                case NpcId.Akara: return new Color(0.70f, 0.55f, 0.85f);
                case NpcId.Kashya: return new Color(0.85f, 0.45f, 0.45f);
                case NpcId.Charsi: return new Color(0.85f, 0.70f, 0.35f);
                case NpcId.Gheed: return new Color(0.55f, 0.70f, 0.85f);
                case NpcId.Warriv: return new Color(0.60f, 0.80f, 0.60f);
                default: return Color.gray;
            }
        }

        /// <summary>销毁全部 NPC 视图（幂等）。</summary>
        private void ClearNpcs()
        {
            if (_npcs.Count == 0) return;
            foreach (var kv in _npcs) DestroyView(kv.Value);
            _npcs.Clear();
        }

        private void TickNpcs(float dt)
        {
            if (_npcs.Count == 0) return;

            var p = AppContext.I != null ? AppContext.I.Player : null;
            if (p == null) return;
            var pg = p.Grid;

            foreach (var kv in _npcs)
            {
                var v = kv.Value;
                if (v == null || v.Root == null) continue;

                var want = Iso.DirectionTo(v.Grid, pg);
                if (want != v.Dir)
                {
                    v.Dir = want;
                    PlayAnim(v, ViewAnim.Idle, true);
                }

                //   建视图时 `CreateEntityNode` 已把 `NeedsFrameRefresh` 置 true，但那条路上
                //   朝向稳定后**每帧跳过**整个 NPC ⇒ 该标志永远没人消费、贴图异步到位后的
                //   `RefreshAllFrames()` 也白设 ⇒ NPC 一辈子停在纯色占位块上
                //   （实测：探针读到 5 个 NPC 全部 `NeedsFrameRefresh=True` + `D2CharPlaceholder`）。
                //   改成"每帧无条件消费该标志"：帧号没变时它是 false ⇒ 不会每帧重取贴图。
                //   （帧推进只在 `TickOne`，而它只遍历 `_entities`/`_groundItems`，NPC 在 `_npcs` 里）
                //   ⇒ 5 个 NPC 永远是 idle 第 0 帧的"木头人"（实测入城 +8s 与 +44s 两次采样 `adv=0`，
                //   不把 NPC 塞进 `_entities`：那会让"给战斗实体"的逻辑（PruneDeadViews /
                //      步频同步 / 实体排序 / DrawEntityList）连 NPC 一起接管，语义会变；
                //      这里只补上缺的那一次推进（`_npcs` 的语义 = 无血条/不参战的场景装饰）。
                if (v.Anim.Tick(dt)) v.NeedsFrameRefresh = true;

                if (v.NeedsFrameRefresh)
                {
                    v.NeedsFrameRefresh = false;
                    ApplyFrame(v);
                }

                // 数值证据（只报一次）：首次读到 NPC 的帧号不为 0 ⇒ 证明推进链真的接上了。
                if (!_npcAdvanceLogged && v.Anim.FrameIndex > 0)
                {
                    _npcAdvanceLogged = true;
                    ViewLog.Info($"[NPC] 帧游标已推进（首个非 0 帧的 NPC：id={kv.Key}）：" +
                                 $"{v.Playing} 第 {v.Anim.FrameIndex}/{v.Anim.FrameCount} 帧 " +
                                 "⇒ TickNpcs 每帧在推进（修前该值恒为 0）");
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // IViewModule：玩家
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// **本项目新增的非契约方法**（`IViewModule` 上没有），与 `MapModule.AttachRoot` 同一做法：
        /// 不调也能跑（那时实体根由本模块自己创建）。
        /// </summary>
        public void AttachRoot(Transform root)
        {
            if (root == null)
            {
                ViewLog.Warn("AttachRoot(null)：忽略（实体根仍由本模块自己创建）");
                return;
            }
            if (_root != null) _root.SetParent(root, false);
            else _pendingRoot = root;
            ViewLog.Info($"AttachRoot：实体视图将挂在场景节点「{root.name}」下");
        }

        /// <inheritdoc />
        public void CreatePlayer(PlayerClass cls)
        {
            if (_cannotRender) return;
            if (EnsureRoot() == null) return;

            if (_player != null) DestroyView(_player);
            _player = null;

            var color = SpriteFrames.PlaceholderColorOfPlayer(cls);
            // 玩家初始排序：格 (0,0) 的基准 + 实体层偏移（第一帧 Tick 就会按真实格刷新）
            var v = CreateEntityNode(GameConst.PlayerEntityId, Vector3.zero, color, cls.ToString(),
                GameConst.SortOrderBase + GameConst.LayerOffsetEntity);
            if (v == null) return;

            v.IsPlayer = true;
            v.Cls = cls;
            v.Dir = Dir8.S;
            // 先把**装备外观套**定下来，再取帧 —— 帧目录与帧数都由它决定。
            v.EquipKey = ResolvePlayerEquipKey(cls);
            PlayAnim(v, ViewAnim.Idle, true);

            _player = v;
            _entities[GameConst.PlayerEntityId] = v;

            ViewLog.Info($"玩家视图已创建：职业={cls}（占位色 {color}），" +
                         $"外观套={(v.EquipKey ?? EquipVisual.BareHandLabel)}，" +
                         $"帧目录={(v.EquipKey != null ? ResPaths.CharEquipDir(cls, v.EquipKey) : ResPaths.CharDir(cls))}，" +
                         $"帧键命名 {{动作}}_{{方向}}_{{帧号}}（帧数来源 = " +
                         $"{(v.EquipKey != null ? "EquipFrameCounts（各套 manifest 生成）" : "SpriteFrameCounts（徒手）")}）");
        }

        /// <inheritdoc />
        public void CreateMonster(MonsterState state)
        {
            if (state == null)
            {
                ViewLog.WarnThrottled("create.null", "CreateMonster: state 为 null ⇒ 忽略");
                return;
            }
            if (_cannotRender) return;
            if (EnsureRoot() == null) return;

            if (_entities.TryGetValue(state.id, out var existing) && !existing.IsPlayer)
            {
                UpdateMonster(state);      // 幂等：已存在就只刷新
                return;
            }

            var code = SpriteFrames.SpriteCodeOf(state.kindId);
            var color = SpriteFrames.PlaceholderColorOfMonster(state);
            var v = CreateEntityNode(state.id, new Vector3(state.worldX, state.worldY, state.worldZ), color,
                state.name, EntitySortOrder(new Vector2Int(state.gridX, state.gridY)));
            if (v == null) return;

            v.KindId = state.kindId;
            v.SpriteCode = code;
            v.Dir = state.dir;

            //   `DefaultWidth/Height/YOffset ÷ ArtScale` + `SetHp/SetVisible`）。已按裁决**删除** ——
            //   出处：原版对可击杀怪**只有**屏幕顶部 `EnemyBar`（`MouseSelection.cs:62-65` ⇒ `ShowEnemyBar`；
            //   `EnemyBar.cs:26-35`），`select` 片真源码扫过参考实现里**没有**头顶血条 ⇒
            //   头顶那条是本工程自加件，违反"不许两份并存"。
            //   现由 `UI/EnemyBarView.cs` 独家承担"怪名 + 血量"（悬停时显示）。

            PlayAnim(v, ViewAnim.Idle, true);
            _entities[state.id] = v;

            ViewLog.Info($"怪物视图已创建：m#{state.id} {state.name} 种类={state.kindId} sprite={code} " +
                         $"精英={state.isChampion}{(state.isChampion ? "（" + state.modName + "）" : "")} " +
                         $"帧目录={ResPaths.MonsterDir(code)} 占位色={color}（血量显示 = 悬停时的顶部 EnemyBar）");
        }

        /// <inheritdoc />
        public void UpdateMonster(MonsterState state)
        {
            if (state == null) return;

            if (!_entities.TryGetValue(state.id, out var v) || v.IsPlayer)
            {
                ViewLog.WarnThrottled("update.missing",
                    $"UpdateMonster(m#{state.id})：视图不存在（应先 CreateMonster）⇒ 本次忽略");
                return;
            }

            if (v.Root == null)
            {
                _entities.Remove(state.id);
                ViewLog.WarnOnce("stale.update",
                    $"UpdateMonster(m#{state.id})：视图节点已失效（Root == null，已随场景卸载）⇒ " +
                    "已丢弃该视图引用，本次不再访问它的 transform（本条只报一次）");
                return;
            }

            var world = new Vector3(state.worldX, state.worldY, state.worldZ);
            var moved = (v.LastWorld - world).sqrMagnitude > 0.0004f;

            v.Root.transform.position = EntityWorld(v.EntityId, world) + KnockbackOffset(v);
            v.LastWorld = world;
            // 排序**只在格变化时**重算（deck 判定要查一次地图；每帧无脑重算纯属浪费）
            var newGrid = new Vector2Int(state.gridX, state.gridY);
            var gridChanged = newGrid != v.Grid;
            v.Grid = newGrid;
            if (gridChanged && v.Renderer != null) v.Renderer.sortingOrder = EntitySortOrder(v.Grid);

            // 朝向是否变化（下面决定"要不要重取整套帧键"，见 dirChanged || …）
            var dirChanged = state.dir != v.Dir;
            if (dirChanged)
            {
                v.Dir = state.dir;
                v.NeedsFrameRefresh = true;      // 换方向 = 换整套帧
            }

            if (!state.alive)
            {
                if (!v.Dead) PlayDeath(state.id);
            }
            else
            {
                //   原版怪物只有 `NU`/`WL`/`A1`/`GH`/`DT` 五个模式；`RN`/`SC` 的触发条件缺出处 ⇒
                //      这里不给它们造句（`zm`/`cr` 有 RN、`wr` 有 SC）。
                //   而怪物受击动作最长 9 帧 @12fps = 0.75s（堕落者 `fa` = 7 帧 = 0.583s）
                //   ⇒ 硬直一结束 `UpdateMonster` 就把 `Hit` 顶成 Idle/Walk，**只渲染出前 3/7 帧**。
                //   不新增任何时长常量（保持时长 = 该单位受击动作的真实帧数 ÷ 基准帧率）。
                //   `state.hitStun ||` 保留：AI 的硬直语义（不动/不出手）仍是它，且帧数=1 的单位
                //   （`IsHitHolding` 恒假）仍靠它撑住受击档。
                var hitAnimPlaying = ViewAnimState.IsHitHolding(
                    v.Playing, v.Anim.FrameCount, v.Anim.Finished);
                var want = ViewAnimState.SelectMonster(state.alive, state.hitStun || hitAnimPlaying,
                    state.attacking, moved);
                // 朝向变了也要重取帧键：原版是 8 方向逐帧动画，"动作没变但换了朝向"= 换整套帧。
                //   只按 `want != v.Playing` 判会吞掉换方向 ⇒ 怪物"朝西走却放着朝南的动画"。
                if (dirChanged || want != v.Playing) PlayAnim(v, want, SpriteFrames.LoopOf(want));
            }

            //   头顶那条已删 ⇒ 血量显示改由 `UI/EnemyBarView` 在**悬停时**从 `Events.MonsterChanged`
            //   的 `Def.MonsterState` 直接读（同一次命中 ⇒ 条与扣血同帧）。
        }

        /// <inheritdoc />
        public void RemoveMonster(int monsterId)
        {
            if (!_entities.TryGetValue(monsterId, out var v) || v.IsPlayer) return;

            DestroyView(v);
            _entities.Remove(monsterId);
            _prevTickWorld.Remove(monsterId);
            _moveScaleLogged.Remove(monsterId);
        }

        /// <inheritdoc />
        public void ClearMonsters()
        {
            var ids = new List<int>();
            foreach (var kv in _entities)
            {
                if (!kv.Value.IsPlayer) ids.Add(kv.Key);
            }
            for (var i = 0; i < ids.Count; i++) RemoveMonster(ids[i]);

            ViewLog.Info($"ClearMonsters：已清掉 {ids.Count} 个怪物视图（玩家视图保留）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // IViewModule：地面物品
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void CreateGroundItem(int groundItemId, ItemStack item, Vector2Int grid)
        {
            if (_cannotRender) return;
            if (EnsureRoot() == null) return;

            if (_groundItems.TryGetValue(groundItemId, out var old)) DestroyView(old);

            var quality = item != null ? item.quality : ItemQuality.Normal;
            var color = SpriteFrames.QualityColor(quality);

            var v = CreateEntityNode(groundItemId, Iso.GridToWorld(grid), color,
                item != null ? item.name : "item", EntitySortOrder(grid));
            if (v == null) return;

            v.IsGroundItem = true;
            v.Grid = grid;

            //   改动前这里只建了一个按品质着色的 40×79 占位四边形，整条路径不加载任何物品图
            //   见 `GroundItemVisual.cs` 文件头；`ApplyFrame` 里那条 `IsGroundItem` 早退同批改掉。
            var iconPath = GroundItemVisual.IconPathOf(item);
            var iconAvailable = !string.IsNullOrEmpty(iconPath) && Game.Res != null && Game.Res.Exists(iconPath);
            if (iconAvailable)
            {
                v.IconPath = iconPath;
                v.BaseColor = GroundItemVisual.TintOf(quality);    // 品质浅色（Normal = 纯白 = 原版像素）
                v.NeedsFrameRefresh = true;
                // 贴图是**异步**取的 ⇒ 先不画任何东西（占位块 = 白方块，正是要消掉的那块灰矩形；
                // 物品是短命的，宁可晚一两帧出现，也不闪一块灰的）。图到位后由 `ApplyGroundItemIcon` 贴上。
                if (v.Renderer != null) v.Renderer.sprite = null;
            }
            else
            {
                // 非预期分支：这张原版图不在素材库里（例：资料片职业专属装备，见
                // `Module/Item/ItemIconAvailability`）/ 配表缺行 ⇒ **保留品质色块**：
                // 它是"素材缺失"的可见信号（登记在 `client/资源欠缺清单.md`），不许静默变透明。
                v.IconPath = null;
                ViewLog.WarnOnce("grounditem.noicon",
                    $"地面物品「{(item != null ? item.name : "?")}」(id={(item != null ? item.itemId : 0)}) "
                    + $"取不到原版图（iconPath={(iconPath == null ? "null(配表缺行)" : iconPath)}，"
                    + "Game.Res=" + (Game.Res == null ? "null" : "ok") + "）⇒ 该件用**品质色块**当占位"
                    + "（口径同 D2Icon：缺图要看得见，已登记资源欠缺清单）");
            }

            _groundItems[groundItemId] = v;

            ViewLog.Info($"地面物品视图：#{groundItemId}「{(item != null ? item.name : "?")}」品质={quality}" +
                         $" 格=({grid.x},{grid.y}) " +
                         (iconAvailable
                             ? $"图={iconPath}（原版物品图，同背包；色调={v.BaseColor}）"
                             : $"图=缺失 ⇒ 品质色块 颜色={color}"));
        }

        /// <inheritdoc />
        public void RemoveGroundItem(int groundItemId)
        {
            if (!_groundItems.TryGetValue(groundItemId, out var v)) return;
            DestroyView(v);
            _groundItems.Remove(groundItemId);
        }

        // ═════════════════════════════════════════════════════════════════════
        // IViewModule：表现
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void PlayHit(int entityId)
        {
            if (!_entities.TryGetValue(entityId, out var v))
            {
                ViewLog.WarnThrottled("hit.missing", $"PlayHit({entityId})：视图不存在 ⇒ 本次受击表现跳过");
                return;
            }
            if (v.Root == null)
            {
                // 节点已随场景卸载（由 `Combat/DamagePipeline` 在命中栈里直接调进来，不经过 Tick 的总闸门）
                _entities.Remove(entityId);
                ViewLog.WarnOnce("stale.hit",
                    $"PlayHit({entityId})：视图节点已失效（Root == null，已随场景卸载）⇒ 丢弃该视图引用（只报一次）");
                return;
            }

            v.HitFlashTimer = HitFlashSeconds;
            v.NeedsFrameRefresh = true;
            PlayAnim(v, ViewAnim.Hit, false);
            //   连续受击时不会自动重开 ⇒ 显式 `Replay`）。原版语义：一次受击 = 一次 GH 动画；
            //   否则第二次受击只接着放上一次的剩余帧（末几帧），看着像"没反应"。
            //   只重开**帧号**，不动 `SpeedScale`（`PlayAnim` 已把静态动作的倍率复位成 1）。
            v.Anim.Replay();
            ApplyFrame(v);                       // 立即出闪白帧（不等下一帧）
            ApplyFlash(v);
        }

        /// <inheritdoc />
        public void PlayDeath(int entityId)
        {
            if (!_entities.TryGetValue(entityId, out var v))
            {
                ViewLog.WarnThrottled("death.missing", $"PlayDeath({entityId})：视图不存在 ⇒ 本次死亡表现跳过");
                return;
            }
            if (v.Root == null)
            {
                _entities.Remove(entityId);
                ViewLog.WarnOnce("stale.death",
                    $"PlayDeath({entityId})：视图节点已失效（Root == null，已随场景卸载）⇒ 丢弃该视图引用（只报一次）");
                return;
            }

            v.Dead = true;
            v.CorpseFaded = false;
            PlayAnim(v, ViewAnim.Death, false);
            v.Anim.Replay();
            ApplyFrame(v);

            ViewLog.Info($"播放死亡表现：实体 {entityId}（Death 动画 {v.Anim.FrameCount} 帧，非循环；" +
                         "播完隐藏血条、保留尸体节点）");
        }

        /// <inheritdoc />
        public void ShowFloatingText(float worldX, float worldY, float worldZ, string text, uint argb)
        {
            if (string.IsNullOrEmpty(text)) return;

            if (Game.UI == null)
            {
                ViewLog.WarnOnce("floattext.noui",
                    "ShowFloatingText: Game.UI 为 null（CloverInput/UI 未初始化？）⇒ 伤害飘字不可见");
                return;
            }

            Game.UI.FloatText(new Vector3(worldX, worldY, worldZ), text, UnpackArgb(argb),
                GameConst.FloatTextDuration);
        }

        /// <inheritdoc />
        public GameObject GetView(int entityId)
        {
            // 节点已销毁时 `v.Root` 是「假 null」（Unity 的 `==` 重载），返回它等于把
            //    一个碰不得的引用交给调用方（`Module/Input/HoverPicker` 会去读它的 `.transform`）
            //    ⇒ 这里判定后**显式返回真正的 null**。
            if (_entities.TryGetValue(entityId, out var v) && v.Root != null) return v.Root;
            if (_groundItems.TryGetValue(entityId, out var g) && g.Root != null) return g.Root;

            ViewLog.WarnThrottled("getview.miss", $"GetView({entityId})：没有该实体的视图（或节点已随场景卸载）⇒ 返回 null");
            return null;
        }

        // ═════════════════════════════════════════════════════════════════════
        // IViewModule：Tick / Clear
        // ═════════════════════════════════════════════════════════════════════

        /// <inheritdoc />
        public void Tick(float dt)
        {
            if (_cannotRender) return;

            //   根节点随 Stage 场景卸载被销毁时，`_root` 的 Unity 引用判为 null
            //   （Unity 的 `==` 重载会把「已销毁对象」判成 null）；而本模块是**常驻对象**，
            //   所以不设这道闸门就会每帧在 `_player.Root.transform` 上抛 `MissingReferenceException`
            //   （那次刷屏的真实堆栈：ViewModule.cs:392 → Tick → AppContext.Tick → Bootstrap.Update）。
            if (_root == null)
            {
                // 根没了却还留着视图引用 ⇒ 「场景已卸载但清场没跑到」：丢弃并**只报一次**。
                if (_player != null || _entities.Count > 0 || _groundItems.Count > 0) DropStaleViews();
                return;
            }

            if (SpriteFrames.ConsumeRepaintRequest())
            {
                RefreshAllFrames();          // 贴图异步到位 ⇒ 只重取一次，绝不每帧重铺
            }

            TickPlayer(dt);
            TickEntities(dt);
            TickNpcs(dt);      // NPC 也要吃 dt —— 帧游标由它推进
        }

        /// <inheritdoc />
        public void Clear()
        {
            // 每个 Stage 重新开一次「只报一次」的闸门：`ViewLog.OnceDone` 是**静态**的，
            //   `Core/Log.ResetThrottle()` 只管 `Core/Log` 自己那张表，不管 `ViewLog` 这张
            //   ⇒ 不在这里清的话，第二局 Play（或第二次进图）里"精灵帧缺失""占位降级"这类
            //   **只报一次**的日志永不出现，会把真实故障藏起来（诊断价值全丢）。
            //   清场时机正确：`Clear()` 只在离场/复位时走到。
            ViewLog.ResetThrottle();

            // u44：视图全没了 ⇒ "正在被点亮的实体"这个游标也必须作废（否则换场后第一个悬停
            //   会因为 `next == _hoveredEntityId` 而**不点亮**：屏幕上看就是"新地图里第一次悬停没反应"）。
            _hoveredEntityId = -1;

            // 已经干净 ⇒ 静默返回（`StageLeft` 与 Flow 的 `ResetModules()` 会各调一次本方法，正常路径
            // **不该**变成两条「清场完成」日志；异常路径（清了但没收到事件）也不会漏）。
            if (_root == null && _player == null && _entities.Count == 0 && _groundItems.Count == 0
                && _npcs.Count == 0)
            {
                _pendingRoot = null;
                SpriteFrames.Clear();
                return;
            }

            // NPC 视图先清（它们不进 `_entities`，见文件头 ②）
            var npcCount = _npcs.Count;
            ClearNpcs();

            var monsters = 0;
            var ids = new List<int>(_entities.Keys);
            for (var i = 0; i < ids.Count; i++)
            {
                var v = _entities[ids[i]];
                if (!v.IsPlayer) monsters++;
                DestroyView(v);
            }
            _entities.Clear();
            _player = null;

            var items = new List<int>(_groundItems.Keys);
            for (var i = 0; i < items.Count; i++) DestroyView(_groundItems[items[i]]);
            _groundItems.Clear();

            _prevTickWorld.Clear();
            _moveScaleLogged.Clear();

            if (_root != null) DestroyViewRoot();
            _root = null;             // 显式置 null（`DestroyViewRoot` 已做；这里再钉一次，防将来改动漏掉）
            _pendingRoot = null;      // 场景根会随场景卸载而销毁，留着它下次会被 SetParent 到已销毁节点
            SpriteFrames.Clear();

            ViewLog.Info($"Clear：清场完成（怪物 {monsters} 个 + 地面物品 {items.Count} 个 + NPC {npcCount} 个" +
                         " + 玩家视图 + 实体根节点）");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 内部
        // ═════════════════════════════════════════════════════════════════════

        private void TickPlayer(float dt)
        {
            if (_player == null) return;

            if (_player.Root == null)
            {
                _player = null;
                _entities.Remove(GameConst.PlayerEntityId);
                ViewLog.WarnOnce("stale.player",
                    "玩家视图节点已失效（Root == null，节点已随场景/父节点卸载）⇒ 已丢弃玩家视图引用，" +
                    "本帧起不再访问它的 transform（本条只报一次，避免每帧刷屏）");
                return;
            }

            // **进图首帧复核一次**装备外观套（只做一次，之后只由
            //   `Events.EquipChanged` 驱动 ⇒ 不是每帧重算）。
            //   理由：`CreatePlayer` 与"装备落进 `IItemModule`"是两条独立步骤（`AppFlow.RunBuildStep(1)`
            //   vs `SaveModule` 的读档链路）；顺序若变，`CreatePlayer` 会读到空装备 ⇒ 角色**一辈子徒手
            if (!_player.EquipKeySettled)
            {
                _player.EquipKeySettled = true;
                ApplyEquipKey(_player, ResolvePlayerEquipKey(_player.Cls), true);
            }

            var p = AppContext.I != null ? AppContext.I.Player : null;
            if (p == null)
            {
                ViewLog.WarnOnce("tick.noplayer",
                    "Tick: IPlayerModule 未接入（AppContext.Player == null）⇒ 玩家视图停在原地");
                return;
            }

            // 位置：`IPlayerModule.World` 契约原文就是"渲染插值后的实际位置" ⇒ 直接吸附，不二次插值
            var world = p.World;
            var moved = (_player.LastWorld - world).sqrMagnitude > 0.0004f;
            _player.Root.transform.position = EntityWorld(_player.EntityId, world) + KnockbackOffset(_player);
            _player.LastWorld = world;

            var grid = p.Grid;
            if (grid != _player.Grid)
            {
                _player.Grid = grid;
                if (_player.Renderer != null) _player.Renderer.sortingOrder = EntitySortOrder(grid);
            }

            var dirChanged = p.Dir != _player.Dir;
            if (dirChanged)
            {
                _player.Dir = p.Dir;
                _player.NeedsFrameRefresh = true;
            }

            if (_player.CastTimer > 0f) _player.CastTimer -= dt;
            if (_player.AttackTimer > 0f) _player.AttackTimer -= dt;

            //   理由：`PlayAnim` 有"死亡是终态"的硬闸门（死后只放行 `Death`）——
            //   若把这段留在动作选择之后，复活那一帧会先被闸门拦掉（`Dead` 还是 true），
            //   然后 `Dead` 才复位 ⇒ 复活后角色永远停在 Death 动画（静默）。
            //   先同步 ⇒ 死亡帧: Dead=true 且 want=Death（放行）；复活帧: Dead=false 且 want=Idle（放行）。
            if (p.IsDead && !_player.Dead)
            {
                _player.Dead = true;
                ViewLog.Info("玩家死亡 ⇒ 玩家视图播 Death 动画");
            }
            else if (!p.IsDead && _player.Dead)
            {
                _player.Dead = false;
                _player.CorpseFaded = false;
                _player.Anim.Replay();
                if (_player.Renderer != null) _player.Renderer.color = Color.white;
                ViewLog.Info("玩家复活 ⇒ 玩家视图从 Death 动画复位");
            }

            //     保持条件 = `ViewAnimState.IsHitHolding`（受击动画**还没播完**且不止一帧
            //     —— `LoopOf(Hit) = false` ⇒ 播到末帧 `Finished` 为真 ⇒ 自动落回后面的档），
            //     不引入任何新时长常量（受击时长 = 该单位该动作的真实帧数 ÷ 基准帧率）。
            var hitAnimPlaying = ViewAnimState.IsHitHolding(
                _player.Playing, _player.Anim.FrameCount, _player.Anim.Finished);
            var want = ViewAnimState.SelectPlayer(p.IsDead, hitAnimPlaying,
                _player.CastTimer > 0f, _player.AttackTimer > 0f, p.IsMoving, p.IsRunning);

            // 朝向变了也要重取帧键（原版 8 方向逐帧动画：换朝向 = 换整套帧）。
            //   只按 `want != _player.Playing` 判会吞掉换方向 ⇒ 玩家"朝东走却放着朝南的动画"。
            if (dirChanged || want != _player.Playing) PlayAnim(_player, want, SpriteFrames.LoopOf(want));

            //   速度取**契约常量**（不是位置做差）：跑 = `PlayerWalkSpeed`、走 = × `PlayerWalkSpeedFactor`。
            //   静态动作（Idle/Attack/Cast/Hit/Death）在 `SyncMoveScale` 里复位成 1（原版出招节奏与移动速度无关）。
            var tilesPerSecond = p.IsRunning
                ? GameConst.PlayerWalkSpeed
                : GameConst.PlayerWalkSpeed * GameConst.PlayerWalkSpeedFactor;
            SyncMoveScale(_player, want, tilesPerSecond, true);
            // 「死亡/复活状态同步」已在上面、动作选择**之前**做过（见那里的理由：PlayAnim 的死亡终态闸门）。
        }

        private void TickEntities(float dt)
        {
            if (_entities.Count == 0 && _groundItems.Count == 0) return;

            PruneDeadViews();       // ★ 先摘掉「节点已被单独销毁」的记录（不摘的话它们每帧都要判一次空）

            foreach (var kv in _entities) TickOne(kv.Value, dt);
            foreach (var kv in _groundItems) TickOne(kv.Value, dt);
        }

        /// <summary>
        /// 摘掉「视图节点已失效」的记录（`Root == null`；Unity 里已销毁对象判为 null）。
        /// 必须先收集再删：`foreach` 期间不能改字典。被摘掉的数量 &gt; 0 时**只报一次**。
        /// </summary>
        private void PruneDeadViews()
        {
            List<int> deadEntities = null;
            foreach (var kv in _entities)
            {
                if (kv.Value != null && kv.Value.Root != null) continue;
                if (deadEntities == null) deadEntities = new List<int>();
                deadEntities.Add(kv.Key);
            }

            List<int> deadItems = null;
            foreach (var kv in _groundItems)
            {
                if (kv.Value != null && kv.Value.Root != null) continue;
                if (deadItems == null) deadItems = new List<int>();
                deadItems.Add(kv.Key);
            }

            if (deadEntities == null && deadItems == null) return;

            var n = 0;
            if (deadEntities != null)
            {
                n += deadEntities.Count;
                for (var i = 0; i < deadEntities.Count; i++)
                {
                    if (deadEntities[i] == GameConst.PlayerEntityId) _player = null;
                    _entities.Remove(deadEntities[i]);
                    _prevTickWorld.Remove(deadEntities[i]);
                    _moveScaleLogged.Remove(deadEntities[i]);
                }
            }
            if (deadItems != null)
            {
                n += deadItems.Count;
                for (var i = 0; i < deadItems.Count; i++) _groundItems.Remove(deadItems[i]);
            }

            ViewLog.WarnOnce("stale.children",
                $"发现 {n} 个视图记录的节点已失效（Root == null，节点随场景/父节点卸载）⇒ 已摘除，" +
                "本帧起不再访问它们（本条只报一次；正常路径应先由 Flow 清场调 Clear()）");
        }

        private void TickOne(EntityView v, float dt)
        {
            if (v == null || v.Root == null) return;

            //   位移来源 = `MonsterModule` 每帧调 `UpdateMonster` 时写进 `v.LastWorld` 的世界坐标
            //   （同一次 `AppContext.Tick` 里 Monster 排在 View 之前 ⇒ 这里读到的就是本帧的位移）。
            //   首帧 / 站着不动 ⇒ 位移 0 ⇒ `SyncMoveScale` 收到 0 会**不缩放**（保持 1），
            //      绝不用 0 当倍率（那会让动画停住，比"帧率不准"更像"飘"）。
            //   玩家不走这条（它用契约速度，见 `TickPlayer`）⇒ 这里跳过 `IsPlayer`。
            if (!v.IsPlayer)
            {
                Vector3 prev;
                if (dt > 0f && _prevTickWorld.TryGetValue(v.EntityId, out prev))
                {
                    var moved = Vector3.Distance(prev, v.LastWorld);
                    SyncMoveScale(v, v.Playing, moved / dt, _moveScaleLogged.Add(v.EntityId));
                }
                _prevTickWorld[v.EntityId] = v.LastWorld;
            }

            if (v.HitFlashTimer > 0f)
            {
                v.HitFlashTimer -= dt;
                if (v.HitFlashTimer > 0f) ApplyFlash(v);
                else
                {
                    //   （z = 状态里的 `worldZ`，恒 0）⇒ 受击闪白结束的那一帧把 z 次级键**整条丢掉**，
                    //   该实体落回「z = 0 的无键带」：与地图瓦片/投射物等一切 z=0 的渲染器并列，
                    //   判据：`EntityWorld` 是本文件 `Root.transform.position` 的**唯一入口**（见其注释）
                    //   「唯一入口」扫描项（它逐行核 `Module/View/**` 里所有 position 赋值）。
                    v.Root.transform.position = EntityWorld(v.EntityId, v.LastWorld);   // 撤掉受击位移
                    ApplyTint(v);
                }
            }

            if (v.Anim.Tick(dt)) v.NeedsFrameRefresh = true;
            if (v.NeedsFrameRefresh)
            {
                v.NeedsFrameRefresh = false;
                ApplyFrame(v);
            }

            //   为什么还要这一处：`MonsterModule.Tick` 只在「位移 / 硬直结束」时置 `ViewDirty`，
            //   站着挨打完的怪（玩家已跑开、怪不再移动）可能**不再**收到 `UpdateMonster`
            //   ⇒ 只改 `UpdateMonster` 那一处会让它停在受击末帧。移动中的怪下一帧就会被
            //   `UpdateMonster` 顶成 Walk（Monster 排在 View 之前），所以这里回落 Idle 不会打架。
            if (!v.IsPlayer && !v.Dead && v.Playing == ViewAnim.Hit && v.Anim.Finished)
            {
                PlayAnim(v, ViewAnim.Idle, SpriteFrames.LoopOf(ViewAnim.Idle));
            }

            // 死亡动画播完 ⇒ 尸体半透明（只做一次）
            if (v.Dead && v.Anim.Finished && !v.CorpseFaded)
            {
                v.CorpseFaded = true;
                ApplyTint(v);
            }
        }

        private void OnSkillCast(int skillId)
        {
            if (_player == null || _player.Root == null) return;
            if (_player.Dead) return;
            _player.CastTimer = CastActionSeconds;
            PlayAnim(_player, ViewAnim.Cast, SpriteFrames.LoopOf(ViewAnim.Cast));
            _player.Anim.Replay();
            ViewLog.Info($"收到 SkillCast({skillId}) ⇒ 玩家视图播施法动作 {CastActionSeconds:0.00}s");
        }

        /// <summary>
        /// `Events.PlayerAttacked`（参数 = 被打的怪物 id）：玩家普攻**真的挥出一刀** ⇒
        /// 播原版 `attack` 动作（逐方向，帧键走 `SpriteFrames.Keys(职业, Attack, 朝向)`）。
        /// <para>此前**没有**这条链 —— 玩家普攻只有音效/飘字/受击闪白，
        /// 玩家自己的挥击动作从没播过（`ViewAnim.Attack` 只被怪物视图用）。</para>
        /// <para>动作从**第 0 帧重播**（每次出手一个完整挥击，不是"接力循环"）；
        /// 时长 <see cref="AttackActionSeconds"/> 与出手间隔同源。
        /// 优先级：死亡 &gt; 受击 &gt; 施法 &gt; 挥击 &gt; 走 &gt; 站立
        /// 本条只说本事件会把 `AttackTimer` 点亮）。</para>
        /// </summary>
        private void OnPlayerAttacked(int monsterId)
        {
            if (_player == null || _player.Root == null)
            {
                ViewLog.WarnThrottled("attack.noPlayerView",
                    $"收到 PlayerAttacked(m#{monsterId}) 但玩家视图不存在（未进图/已清场）⇒ 本次挥击表现跳过");
                return;
            }
            if (_player.Dead) return;                       // 死亡姿态优先，不切成挥击

            _player.AttackTimer = AttackActionSeconds;
            PlayAnim(_player, ViewAnim.Attack, SpriteFrames.LoopOf(ViewAnim.Attack));
            _player.Anim.Replay();

            // 帧数取该职业**真实 .cof 帧数**（`SpriteFrameCounts.Of`；取不到 ⇒ 兜底默认职业）
            var p = AppContext.I != null ? AppContext.I.Player : null;
            var unitKey = (p != null ? p.Class : Diablo2.Def.PlayerClass.Amazon)
                .ToString().ToLowerInvariant();
            ViewLog.Info($"收到 PlayerAttacked(m#{monsterId}) ⇒ 玩家视图播挥击动作 " +
                         $"{AttackActionSeconds:0.00}s（原版 attack，帧数 " +
                         $"{SpriteFrames.FrameCountOf(unitKey, ViewAnim.Attack)}，@ " +
                         $"{SpriteFrames.FpsOf(ViewAnim.Attack):0.#}fps）");
        }

        /// <summary>
        /// `Events.StageLeft` 兜底：离场时把全部 Unity 引用置 null（`AppFlow.LeaveStage` 也会调 `Clear()`，
        /// 本方法幂等 —— 已经干净时 `Clear()` 静默返回，不会产生第二条「清场完成」日志）。
        /// </summary>
        private void OnStageLeft()
        {
            var had = _root != null || _player != null || _entities.Count > 0 || _groundItems.Count > 0;
            Clear();
            if (had)
            {
                ViewLog.Info("随 StageLeft 复位：视图引用已全部置 null（玩家/怪物/地面物品/实体根），" +
                             "下一帧 Tick 的安全闸门会直接放行到不做事");
            }
        }

        /// <summary>
        /// 实体根已失效（Stage 场景卸载）却仍留着视图引用 ⇒ 全部丢弃并**只报一次**。
        /// <para>正常路径走不到这里（`AppFlow.LeaveStage` → `Clear()` 会先置空）；走到这里就说明
        /// 「场景已卸载、而清场没跑到」——留一条**可检索**日志，用来区分「正常卸载」与「真丢引用」。</para>
        /// <para>这里**不再** `Destroy` 任何节点：它们已经随场景销毁了，对已销毁对象调 `Destroy` 无意义。</para>
        /// </summary>
        private void DropStaleViews()
        {
            var entities = _entities.Count;
            var monsters = 0;
            foreach (var kv in _entities)
            {
                if (kv.Value != null && !kv.Value.IsPlayer) monsters++;
            }
            var items = _groundItems.Count;
            var npcs = _npcs.Count;

            _entities.Clear();
            _groundItems.Clear();
            _npcs.Clear();
            _prevTickWorld.Clear();      // 步频同步的辅助表一并丢弃
            _moveScaleLogged.Clear();
            _player = null;
            _root = null;
            _pendingRoot = null;

            ViewLog.WarnOnce("stale.root",
                $"实体根节点已随场景卸载（或 Stage 场景被重新加载；EntityRoot 引用判为 null），" +
                $"但本模块仍持有 {entities} 个实体视图（其中怪物 {monsters} 个）+ {items} 个地面物品视图 " +
                $"+ {npcs} 个 NPC 视图 " +
                "⇒ 已全部丢弃，本帧起不再访问它们的 transform。" +
                "此现象 = 「场景已经卸载/重载，而清场没跑到」（本条只报一次，绝不每帧刷屏）；" +
                "正常路径应先由 Flow 调 Clear()");
        }

        // ═════════════════════════════════════════════════════════════════════
        // 装备外观套（key 规则见 `Module/View/EquipVisual.cs` 文件头）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// `Events.EquipChanged`：装备生效集变化 ⇒ **重取整套帧键**（不是只换贴图）。
        /// <para>为什么必须"换套"而不是"换贴图"：同一职业**装上武器后逐动作帧数会变**
        /// （实测 amazon 徒手 attack=13 / `equip/jav` attack=15；barbarian 12 → 16）
        /// ⇒ 只换目录不换帧数 ⇒ 帧键指向不存在的图 ⇒ 静默退成纯色占位块。</para>
        /// <para>换套后从**第 0 帧**重播当前动作（`Replay`）：新套的帧数与内容都不同，
        /// 接着放旧帧号没有意义（`PlayAnim` 的早退判据不比较帧数，也不会自己重开）。</para>
        /// <para>外观套**没变**（例：只换了头盔/戒指，或换的是非外观槽位）⇒ **不重播**，
        /// 避免把正在播的挥击/施法打断。</para>
        /// </summary>
        private void OnEquipChanged(InventoryChangedArgs args)
        {
            if (_player == null || _player.Root == null)
            {
                ViewLog.WarnThrottled("equip.noPlayerView",
                    $"收到 {Events.EquipChanged} 但玩家视图不存在（未进图 / 已清场）⇒ 本次换装外观不刷新");
                return;
            }

            var key = ResolvePlayerEquipKey(_player.Cls);
            if (string.Equals(_player.EquipKey ?? string.Empty, key ?? string.Empty, StringComparison.Ordinal))
            {
                return;      // 外观套未变：正常路径，不重播、不刷日志
            }

            var from = _player.EquipKey;
            ApplyEquipKey(_player, key, true);
            ViewLog.Info($"[装备外观] 换套：职业={_player.Cls} 外观套 " +
                         $"{(from ?? EquipVisual.BareHandLabel)} → {(key ?? EquipVisual.BareHandLabel)}" +
                         $"（载荷装备 {(args != null && args.equip != null ? args.equip.Count : 0)} 件；" +
                         $"重取整套帧键并从头播，当前动作={_player.Playing} 帧数={_player.Anim.FrameCount}，" +
                         $"帧目录={(key != null ? ResPaths.CharEquipDir(_player.Cls, key) : ResPaths.CharDir(_player.Cls))}）");
        }

        /// <summary>
        /// 按**契约**读当前装备，算出生效的外观套 key（可能发生逐级回退并留 Warn）。
        /// <para>取数口径（分层不变：View 只读契约接口，不 `using Diablo2.Module.Item`）：
        /// `AppContext.I.Item.Equipment`（= **已生效**的装备集，双武器组只含生效组那把）
        /// + `Tables.Default.Item.Get(itemId)` 的 `Source / Type / Subtype`。</para>
        /// <para>"占哪只手"由 <see cref="EquipVisual.HandOf"/>（纯函数）判；与
        /// `Module/Item/Equipment.SlotOf(row)` 的等价性由 `animcheck §7` 在整张 `item_c` 上逐行对账。</para>
        /// </summary>
        private static string ResolvePlayerEquipKey(PlayerClass cls)
        {
            string weaponCode, shieldCode;
            ReadHandCodes(cls, out weaponCode, out shieldCode);

            string wanted;
            bool fellBack;
            //   `{class}/equip/{key}`，而 `Select` 传进来的是裸 key）——直接传 `EquipFrameCounts.Has`
            //   会让**任何** key 都查不到 ⇒ 装备外观套永远回退徒手（实机：装上武器仍走 `Chars/{class}/`，
            //   并误报"该装备外观未导出"）。适配器与断言见 `EquipVisual.ExistsAdapter`。
            var key = EquipVisual.Select(weaponCode, shieldCode, EquipVisual.ExistsAdapter(cls),
                out wanted, out fellBack);

            if (fellBack && wanted != null)
            {
                // 每个"理想 key"只报一次（用 wanted 当 key：补导同框套后重跑生成器，这条自然消失）。
                ViewLog.WarnOnce("equip.fallback." + cls + "." + wanted,
                    EquipVisual.FallbackWarnText(cls, weaponCode, shieldCode, wanted, key));
            }
            else if (fellBack && wanted == null && key == null && (weaponCode != null || shieldCode != null))
            {
                // 理论上到不了（有装备 code 时 wanted 必非 null）；真到了说明 Select 的语义被改坏 ⇒ 留痕。
                ViewLog.WarnOnce("equip.key.inconsistent." + cls,
                    $"[装备外观] 职业={cls}：装备 code=[武器={weaponCode ?? "-"}, 盾={shieldCode ?? "-"}] " +
                    "但算出的理想 key 为空 ⇒ 按徒手渲染（这是内部一致性问题，请查 EquipVisual.KeyOf）");
            }

            // 卸光装备 ⇒ key = null = 徒手：**正常路径**（走 `Chars/{class}/` 那套已验收素材），
            // 不在这里刷日志；"套变了"这件事由调用方 `OnEquipChanged` 打一条 Info 说清从哪套到哪套。
            return key;
        }

        /// <summary>
        /// 从契约读"哪件占了主手 / 副手"的**物品 code**（外观套 key 就是这两个 code 拼的）。
        /// <para>主手 = 生效武器（`IItemModule.Equipment` 已按双武器组收窄 ⇒ 该集合里的武器就是生效那把）；
        /// 副手 = 盾（`Def.ItemSlot.Shield`，`ItemSlot` 里盾的确切名字是 **Shield**，值 4）。</para>
        /// <para>非预期分支一律留痕：契约未接入 / 配表缺行（<see cref="EquipVisual.RowMissingWarnText"/>）。</para>
        /// </summary>
        private static void ReadHandCodes(PlayerClass cls, out string weaponCode, out string shieldCode)
        {
            weaponCode = null;
            shieldCode = null;

            var ctx = AppContext.I;
            var items = ctx != null ? ctx.Item : null;
            if (items == null)
            {
                ViewLog.WarnOnce("equip.noitem",
                    "[装备外观] IItemModule 未接入（AppContext.Item == null）⇒ 无法判断手上有什么 ⇒ 一直按徒手渲染");
                return;
            }

            var equip = items.Equipment;      // 契约：当前**生效**的装备集
            if (equip == null)
            {
                ViewLog.WarnOnce("equip.nolist",
                    "[装备外观] IItemModule.Equipment 返回 null ⇒ 无法判断手上有什么 ⇒ 一直按徒手渲染");
                return;
            }

            var table = Table.Tables.Default.Item;
            if (table == null)
            {
                ViewLog.WarnOnce("equip.notable",
                    "[装备外观] 配表 `item_c`（Tables.Default.Item）为 null（TableLoader 没加载？）" +
                    "⇒ 查不到装备行 ⇒ 一直按徒手渲染");
                return;
            }

            for (var i = 0; i < equip.Count; i++)
            {
                var st = equip[i];
                if (st == null) continue;

                var row = table.Get(st.itemId);
                if (row == null)
                {
                    ViewLog.WarnThrottled("equip.rowmiss." + st.itemId,
                        EquipVisual.RowMissingWarnText(cls, st.itemId, st.name));
                    continue;
                }

                switch (EquipVisual.HandOf(row.Source, row.Type, row.Subtype))
                {
                    case ItemSlot.Weapon:
                        if (weaponCode == null) weaponCode = row.Code;
                        break;
                    case ItemSlot.Shield:
                        if (shieldCode == null) shieldCode = row.Code;
                        break;
                    default:
                        break;      // 盔甲/头盔/戒指…：不影响"手上有没有东西"（正常路径）
                }
            }
        }

        /// <summary>
        /// 把 <paramref name="key"/> 应用到玩家视图：**重取整套帧键**并用新套重播当前动作。
        /// <para>key 未变且 <paramref name="force"/> 为 false ⇒ 什么都不做（不重播、不刷帧）。
        /// <paramref name="force"/> = true 用于"必须立刻显形"的两处：进图首帧复核、装备变化。</para>
        /// </summary>
        private static void ApplyEquipKey(EntityView v, string key, bool force)
        {
            if (v == null) return;

            var changed = !string.Equals(v.EquipKey ?? string.Empty, key ?? string.Empty, StringComparison.Ordinal);
            v.EquipKey = key;
            if (!changed && !force) return;

            // 这里**不是**"只换贴图"：`SpriteFrames.Keys` 会按新 key 重算
            //   帧目录（`ResPaths.CharEquipDir`）与**帧数**（`EquipFrameCounts`），
            //   并顺带把整组新帧键一次性预取（`Prefetch`）。
            PlayAnim(v, v.Playing, SpriteFrames.LoopOf(v.Playing));
            v.Anim.Replay();          // 新套的帧内容/帧数都不同 ⇒ 从第 0 帧重播
            ApplyFrame(v);            // 不等下一帧，立刻出画（换装要有可见反馈）
        }

        // ── 节点 / 帧 ────────────────────────────────────────────────────────

        /// <summary>实体根节点（**唯一**创建 GameObject 的入口 ⇒ 也是最集中的失败点）。</summary>
        private Transform EnsureRoot()
        {
            if (_root != null) return _root;

            try
            {
                var go = new GameObject("EntityRoot");
                _root = go.transform;
                if (_pendingRoot != null) _root.SetParent(_pendingRoot, false);
                ViewLog.Info("创建实体根节点 EntityRoot（玩家/怪物/地面物品的父节点）" +
                             (_pendingRoot != null ? $"，挂在场景节点「{_pendingRoot.name}」下" : ""));
                return _root;
            }
            catch (Exception e)
            {
                _cannotRender = true;
                ViewLog.Error($"ViewModule: 无法创建 GameObject（{e.GetType().Name}: {e.Message}）" +
                              "⇒ 本进程不具备渲染能力，精灵视图全部跳过" +
                              "（离线自检宿主属正常现象；Unity 里出现请看这条日志）", e);
                return null;
            }
        }

        /// <summary>是否已打过「桥面(deck)抬档生效」的一次性日志（数值证据；只报一次，不刷屏）。</summary>
        private static bool _deckSortLogged;

        /// <summary>
        /// **实体节点排序值的唯一出处**（玩家 / 怪物 / 地面物品 / NPC 全走这里）：
        /// 转 <see cref="Iso.EntitySortOrder"/>（纯函数 = 该格基准 + 实体层偏移；deck ⇒ 抬一档），
        /// deck 与否由契约 `IMapModule.IsDeckGrid` 给。
        ///
        /// <para><b>为什么</b>：桥面格的正南一格恒是桥栏杆物件，而栏杆图形向上长 ≈2 格
        /// ⇒ 普通实体档会被它盖住（用户实测：「营地出门的桥，还是从桥下走」）。
        /// 数值推导见 <see cref="GameConst.LayerOffsetDeckEntity"/>。</para>
        ///
        /// <para>deck 判定走**契约** `IMapModule.IsDeckGrid`（数据由 `Module/Map` 按"地砖取自
        /// deck 类包"登记，视图层不猜几何）。拿不到 Map（未接入 / 场景卸载中）⇒ 用普通实体档
        /// 并**留一次 Warn**（不静默）；地图未生成时 `IsDeckGrid` 本身返回 false，不必另判。</para>
        ///
        /// <para> **`internal` 而不是 `private`**：投射物表现
        /// （`Module/Skill/ProjectileView`）过去自己写 `Iso.SortOrder(g, LayerOffsetEntity)`，
        /// 抬档口径改到本方法时**漏了它** ⇒ 桥上射出的投射物仍被栏杆盖住。改成让投射物也调本方法，
        /// 使「实体排序」**全局只有一份实现**，避免再出现"改了口径没扫全路径"。</para>
        /// </summary>
        internal static int EntitySortOrder(Vector2Int g)
        {
            var map = AppContext.I != null ? AppContext.I.Map : null;
            if (map == null)
            {
                ViewLog.WarnOnce("sort.nomap",
                    "EntitySortOrder: IMapModule 未接入（AppContext.Map == null）⇒ 一律用普通实体档，" +
                    "桥面抬档不生效（桥上的角色可能被栏杆盖住）");
                return Iso.SortOrder(g, GameConst.LayerOffsetEntity);
            }

            var isDeck = map.IsDeckGrid(g);
            if (!isDeck) return Iso.EntitySortOrder(g, false);

            var order = Iso.EntitySortOrder(g, true);
            if (!_deckSortLogged)
            {
                _deckSortLogged = true;
                ViewLog.Info($"桥面(deck)抬档生效：格 ({g.x},{g.y}) 的实体排序 = {order}" +
                             $"（普通实体档 = {Iso.EntitySortOrder(g, false)}，" +
                             $"正南一格物件层 = {Iso.SortOrder(new Vector2Int(g.x, g.y + 1), GameConst.LayerOffsetObject)}" +
                             " ⇒ 不再被栏杆盖住）");
            }
            return order;
        }

        //   的两个实体数值完全相等**；两者世界坐标的 z 都是 `state.worldZ`（恒 0）⇒ Unity 只剩
        //   "到相机的距离"可判，而距离也相等 ⇒ 每帧按渲染器提交顺序交替 ⇒ 肉眼看到两个精灵互相闪。
        //   —— 层带 0/1/2/3（ground/object/entity/overlay）已被占满，塞不进第四档；
        //   而 z 次级键**只在 sortingOrder 相等时**起作用，故**不可能改变任何既有的跨格/跨层次序**。
        //   取 +z（**远离相机**）而不是 -z：等 `sortingOrder` 时让地图瓦片（含 overlay 遮蔽层）仍然
        //   画在实体之上 —— 与"overlay = 遮蔽/迷雾应盖住实体"的既有意图一致（保守）。
        // 原版口径：原版同格实体的绘制次序是**稳定的生成序号**（不是每帧重排）；这里把它落成
        //   「类型档 → id」两级键，因此**重复调用结果恒定**（`mapcheck`/`movecheck` 有断言）。
        //
        // ① **第三键的口径有出处**（不是"我们用 z 当距离"的假设）：Unity 文档 `TransparencySortMode`
        //    原文 —— "By default, perspective cameras sort objects based on distance from camera
        //    position to the object center; and **orthographic cameras sort based on distance along
        //    the view direction**"（https://docs.unity3d.com/ScriptReference/TransparencySortMode.html）。
        //    本项目相机是**正交 + rotation=identity + 机位 z 恒为 `-CameraRig.CameraDistance`**
        //    （`Module/Camera/CameraRig.cs` 的 `IsoLock`）⇒ 视图轴 = +Z ⇒ **排序距离之差 == 实体 z 之差**，
        //    与相机跟焦/抖动/插值**无关**（相机 x/y 在正交模式下根本不参与排序）。
        // ② **主排序键每帧重排**这条假设不成立：`sortingOrder` 只在**格变化**时重算；
        //    剩下无决胜键的情形 = `Module/Skill/ProjectileView.cs` 的 `Projectile.WorldOf(...)`
        //    （z=0，且与实体同档 `EntitySortOrder`）⇒ 由 `ProjectileView` 的第三键口径补上。

        /// <summary>实体次级排序**类型档**（越大越靠前）：玩家 4 &gt; 城镇 NPC 3 &gt; 怪物 2 &gt; 地面物品 1。</summary>
        internal static int SortTieRank(int entityId)
        {
            if (entityId == GameConst.PlayerEntityId) return 4;      // 玩家（视角主体，永远压住其它）
            if (entityId < 0) return 3;                               // 城镇 NPC（NpcEntityId = -1-(int)NpcId）
            if (entityId >= GameConst.GroundItemIdBase) return 1;     // 地面物品（贴地，最底）
            return 2;                                                 // 怪物（其余 id 区段）
        }

        /// <summary>
        /// 引擎侧「同 `sortingOrder` 的确定性次级键」实例（<see cref="SortingLayers"/>）——
        /// 本工程**只借它的 <see cref="SortingLayers.TiebreakOffset"/>**（实体 id → 微小 z 偏移），
        /// 「id → 次级键」这个公式在项目侧不再留第二份
        /// （投射物档同源：`Module/Skill/ProjectileView.SortZFor` 也走该引擎件，只是取模基数不同）。
        /// <para><c>fieldHeightTiles</c> 是引擎构造的**必填项**、本处用不到
        /// （<see cref="SortingLayers.DepthOrder"/> 是 `worldY` 口径，而本项目的深度序是
        /// **等距格** `(gx+gy)` 口径，见 <see cref="EntitySortOrder"/> 的说明）
        /// ⇒ 填一个**有出处**的真实值 `GameConst.MapMaxSize`（任意区域尺寸硬上限，格）。</para>
        /// <para>预算自洽：`SortTieMod × SortingLayers.DefaultTiebreakStep = 10000 × 1e-4 = 1.0`
        /// 小于本项目**一个 order 级**（`GameConst.SortOrderStep` = 4）
        /// ⇒ 次级键不可能翻转跨格 / 跨层的先后（满足引擎件文件头那条硬约束）。</para>
        /// </summary>
        private static readonly SortingLayers EntityTiebreak = new SortingLayers(
            GameConst.MapMaxSize,
            SortingLayers.DefaultDepthLevelsPerTile,
            SortTieMod,
            SortingLayers.DefaultTiebreakStep);

        /// <summary>次级键取模基数（= 原公式里的 10000：同格堆叠可区分的上限）。</summary>
        private const int SortTieMod = 10000;

        /// <summary>
        /// 实体节点的**z 次级排序键**（同 `sortingOrder` 时 Unity 按"到相机距离"决胜）：
        /// `(5 - rank) + TiebreakOffset(|id|)`（= 原 `(5 - rank) + (|id| % 10000) * 1e-4`，**逐位相同**），
        /// 恒 &gt; 0（= 在地图层之后）。同级同类按 `EntityId` 升序 ⇒ **同一个输入永远得到同一个值**（纯函数）。
        /// <para>传 `Mathf.Abs(id)` 而不是裸 `id`：引擎件按**有符号**取模（负值折回正区间），
        /// 本项目口径是 `|id| % mod` —— 城镇 NPC 的 id 是负数（`NpcEntityId = -1-(int)NpcId`）
        /// ⇒ 取绝对值后两者结果完全一致。</para>
        /// </summary>
        internal static float SortTieZ(int entityId)
        {
            var rank = SortTieRank(entityId);
            return (5 - rank) * 1f + EntityTiebreak.TiebreakOffset(Mathf.Abs(entityId));
        }

        /// <summary>实体节点的世界坐标（**唯一入口**：所有 `Root.transform.position` 赋值都经过它）。</summary>
        internal static Vector3 EntityWorld(int entityId, Vector3 w)
            => new Vector3(w.x, w.y, w.z + SortTieZ(entityId));

        private EntityView CreateEntityNode(int entityId, Vector3 world, Color color, string name, int sortOrder)
        {
            try
            {
                var go = new GameObject($"E_{entityId}_{name}");
                go.transform.SetParent(_root, false);
                go.transform.position = EntityWorld(entityId, world);

                // 原版单位 80 px/单位 vs 本项目导入 PPU=64 ⇒ 缩 0.8（推导见 SpriteFrames.cs 文件头）
                go.transform.localScale = Vector3.one * SpriteFrames.ArtScale;

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = SpriteFrames.Placeholder;
                sr.color = color;
                sr.sortingOrder = sortOrder;

                var v = new EntityView
                {
                    EntityId = entityId,
                    Root = go,
                    Renderer = sr,
                    // u44：记下**建节点时的原材质**（悬停变亮的"移开还原"就还它）——
                    //   必须在任何 `EntityHighlight.Apply(...)` 之前读，否则记下的就是被换过的那个。
                    OriginalMaterial = sr.sharedMaterial,
                    BaseColor = color,
                    LastWorld = world,
                    UsingPlaceholder = true,
                    NeedsFrameRefresh = true,
                };
                // u44：先把属性块写成"常规档"（1.0/1.0）——
                //   ① 与原版一致：`Materials.SetRendererHighlighted(r, false)` 就是这么写的；
                //   ② 实机判据要能**回读**这两个键（属性块里没写过的键 `GetFloat` 返回 0，
                //      会把"没接线"误判成"亮度 0"）⇒ 建节点即写一次，此后只由悬停改。
                EntityHighlight.Apply(sr, v.OriginalMaterial, false);
                v.Anim.SpeedScale = 1f;
                return v;
            }
            catch (Exception e)
            {
                _cannotRender = true;
                ViewLog.Error($"ViewModule: 创建实体视图 {entityId} 失败（{e.GetType().Name}: {e.Message}）⇒ 跳过该实体", e);
                return null;
            }
        }

        /// <summary>
        /// 切换动作（**只在动作变化时**调用 ⇒ 不会每帧把帧号打回 0）。
        /// <para>顺带把**整组帧键**一次性发起异步加载（<see cref="SpriteFrames.Prefetch"/>）：
        /// 逐帧首次访问的话每一帧都要各等一次异步回调 ⇒ 首圈动画逐帧闪占位色块。</para>
        /// </summary>
        private static void PlayAnim(EntityView v, ViewAnim anim, bool loop)
        {
            //   为什么钉在这里：`PlayAnim` 是全部动作切换的**唯一出口**（玩家/怪物/NPC/换装重播都走它）
            //   ⇒ 只在这一处保证"死后不再被别的动作顶掉"，比在每个调用点各写一遍更难漏。
            //   `DamagePipeline.ApplyToMonster` 又 `PlayHit` ⇒ 尸体停在受击末帧，运行时
            //   ViewAnim 观测集里 `Death` 一次都没出现。
            //   例外：`anim == ViewAnim.Death` 本身放行（`PlayDeath` 要先设 `v.Dead = true` 再播它）；
            //      玩家复活时 `TickPlayer` 会先把 `Dead` 复位（见那里的顺序）。
            if (v.Dead && anim != ViewAnim.Death)
            {
                ViewLog.WarnOnce("anim.after.death",
                    $"死亡终态拦截：实体 {v.EntityId} 已进入 Death（尸体）⇒ 本次「{anim}」动作请求被拒绝" +
                    "（死亡后不再接受 Hit/Attack/Cast/Idle 等切换；出现本条说明有调用点在死后仍刷动画）");
                return;
            }

            v.Playing = anim;
            v.NeedsFrameRefresh = true;

            var keys = v.IsGroundItem
                ? null
                : v.IsPlayer
                    // 玩家按**装备外观套**取帧（`EquipKey` 为 null = 徒手，
                    //   与改动前的行为逐字节一致）。帧数由 `EquipFrameCounts` 给（换套会换帧数）。
                    ? SpriteFrames.Keys(v.Cls, v.EquipKey, anim, v.Dir)
                    : SpriteFrames.Keys(v.SpriteCode, anim, v.Dir);

            v.Anim.Play(anim, keys, SpriteFrames.FpsOf(anim), loop);
            SpriteFrames.Prefetch(keys);      // ★ 整组帧一次性发起加载（见 SpriteFrames.Prefetch 注释）

            //   Idle/Attack/Cast/Hit/Death 上（`PlayHit`/`PlayDeath` 会在同一次调用栈里紧接着
            //   `ApplyFrame` 出画，等不到下一次 `TickOne` 复位）。移动类动作的倍率由
            //   `SyncMoveScale` 每帧按实际速度算。
            if (!SpriteFrames.IsMoveAnim(anim)) v.Anim.SpeedScale = 1f;
        }

        /// <summary>
        /// <para>口径：有效帧率 = **帧数 × 格/秒**（`SpriteFrames.FpsForCycle`，出处见该函数注释）
        /// ⇒ `SpeedScale = 该值 ÷ Play 时传的基准帧率`（`SpriteFrames.SpeedScaleForCycle`）。</para>
        /// <para>只对移动类动作（`ViewAnim.Walk` / `ViewAnim.Run`）缩放；其它动作一律**复位成 1**
        /// —— 原版出招/受击的节奏与移动速度无关（`AnimData` 里每个动作一套独立帧率）。</para>
        /// <para>`tilesPerSecond &lt;= 0`（速度未知 / 本帧没有位移）⇒ 不缩放（保持 1）。
        /// 不许缩放到 0：那会让动画完全停住，是比"帧率不准"更糟的表现。</para>
        /// </summary>
        /// <param name="verbose">是否允许打日志。玩家传 true（速度只有两档、切换时才变）；
        /// 怪物传"本实体第一条"（实测速度每帧有微小抖动，逐次记录会刷屏）。</param>
        private static void SyncMoveScale(EntityView v, ViewAnim anim, float tilesPerSecond, bool verbose)
        {
            if (v == null || v.Anim == null) return;

            if (!SpriteFrames.IsMoveAnim(anim) || tilesPerSecond <= 0f)
            {
                if (!Mathf.Approximately(v.Anim.SpeedScale, 1f)) v.Anim.SpeedScale = 1f;
                return;
            }

            var frames = v.Anim.FrameCount;                 // 该动作的**真实**帧数（回退已由 SpriteFrames 处理）
            var baseFps = SpriteFrames.FpsOf(anim);
            var scale = SpriteFrames.SpeedScaleForCycle(frames, tilesPerSecond, baseFps);

            // 变化 < 1% 视为抖动：不重设、不刷日志（怪物实测速度会在真值附近小幅摆动）
            if (Mathf.Abs(v.Anim.SpeedScale - scale) < 0.01f) return;
            v.Anim.SpeedScale = scale;

            if (!verbose) return;
            ViewLog.Info($"步频同步：实体 {v.EntityId} 动作={anim} 帧数={frames} " +
                         $"速度={tilesPerSecond:0.###} 格/秒 ⇒ 有效帧率 {SpriteFrames.FpsForCycle(frames, tilesPerSecond):0.##}fps" +
                         $"（= 帧数 × 速度；基准 {baseFps:0.#}fps × SpeedScale {scale:0.###}）；" +
                         "出处 = 原版每格一个动画循环（AnimData.referenceFrameCount + Iso.SubTileCount=5）");
        }

        /// <summary>
        /// 把当前帧的贴图取来贴上。
        /// <para>取不到贴图时**保持当前已显示的真图**（只有"从没拿到过真图"才贴纯色占位）——
        /// 见方法体里那条 2026-09-19 的缺陷定论注释。</para>
        /// </summary>
        private static void ApplyFrame(EntityView v)
        {
            if (v == null || v.Renderer == null) return;

            //   改动前这里对 `IsGroundItem` 直接 `return` ⇒ 建视图时置的 `NeedsFrameRefresh`
            if (v.IsGroundItem)
            {
                ApplyGroundItemIcon(v);
                return;
            }

            var key = v.Anim.CurrentKey;
            var sprite = SpriteFrames.Resolve(key);

            //   `Resolve` 对**首次访问**的帧键当帧一定返回 null（`Resources.LoadAsync` 是异步的，
            //   新实现：**手上已经有真图就保持不动**（宁可多停 1 帧旧图，也不闪色块），
            //   等 `Resolve` 的回调把 `_repaintRequested` 置起来、下一帧重铺时再换成真图。
            if (sprite == null)
            {
                // 从没拿到过真图（刚建视图 / 素材真的缺）⇒ 仍显式显示占位色块：
                // 它是"素材缺失"的可见信号（登记在 `client/资源欠缺清单.md` #1/#2），不能悄悄隐藏。
                if (v.UsingPlaceholder)
                {
                    v.Renderer.sprite = SpriteFrames.Placeholder;
                    ApplyTint(v);
                    CountPlaceholderTick(v.EntityId);
                }
                return;
            }

            v.UsingPlaceholder = false;
            v.Renderer.sprite = sprite;
            ApplyTint(v);
        }

        /// <summary>
        /// <para>为什么单独一路：地面物品没有逐帧动画（`Anim` 从没 `Play` 过），它只有"一张图"。
        /// 图由 `SpriteFrames.Resolve` 取（该函数自带缓存 / 异步 / 失败退避自愈），
        /// 尺寸与像素尺度由**导入设置**决定（`D2/Items/*.png`：PPU=64、pivot=中心 ⇒
        /// 节点上再乘 `SpriteFrames.ArtScale`(0.8) 后 = 原版 80px/单位的**原始尺寸**，
        /// 与 `Module/Map` 的地形同尺度，不会"地上一个巨大的图标"）。</para>
        /// <para>图没到之前**什么都不画**（`sprite == null`）+ 每帧重试：
        /// ① 不画占位块 —— 白/浅灰方块正是用户报的"地上一个灰矩形"；
        /// ② 每帧重试是**唯一**的驱动（`TickOne` 只在 `NeedsFrameRefresh` 为真时调本方法，
        ///    这里不把它置回 true 的话，第一帧没取到图就永远不会再取 = 静默消失）。</para>
        /// </summary>
        private static void ApplyGroundItemIcon(EntityView v)
        {
            if (v.Renderer == null) return;

            if (string.IsNullOrEmpty(v.IconPath))
            {
                // 建视图时 `Game.Res.Exists` 已判定"这张原版图拿不到" ⇒ 品质色块（可见的缺失信号）
                v.Renderer.sprite = SpriteFrames.Placeholder;
                ApplyTint(v);
                return;
            }

            var sprite = SpriteFrames.Resolve(v.IconPath);
            if (sprite == null)
            {
                v.NeedsFrameRefresh = true;      // 异步未到位 ⇒ 下一帧再取（到位即自愈）
                return;
            }

            v.UsingPlaceholder = false;
            v.Renderer.sprite = sprite;
            ApplyTint(v);                        // IsGroundItem ⇒ 用 BaseColor（品质浅色；Normal = 白）
        }

        /// <summary>按"是否占位 + 是否闪白 + 是否尸体"决定颜色。</summary>
        private static void ApplyTint(EntityView v)
        {
            if (v == null || v.Renderer == null) return;

            var c = v.UsingPlaceholder || v.IsGroundItem ? v.BaseColor : Color.white;
            if (v.Dead && v.CorpseFaded) c.a = CorpseAlpha;
            v.Renderer.color = c;
        }

        /// <summary>受击闪白（像素图上 tint 向白靠 + 一点反向位移，肉眼可辨）。</summary>
        private static void ApplyFlash(EntityView v)
        {
            if (v == null || v.Renderer == null || v.Root == null) return;

            var c = v.UsingPlaceholder || v.IsGroundItem ? v.BaseColor : Color.white;
            v.Renderer.color = Color.Lerp(c, Color.white, 0.85f);

            v.Root.transform.position = EntityWorld(v.EntityId, v.LastWorld) + KnockbackOffset(v);
        }

        /// <summary>受击反向位移（按朝向的格增量，乘以很小的系数）。</summary>
        private static Vector3 KnockbackOffset(EntityView v)
        {
            if (v.HitFlashTimer <= 0f) return Vector3.zero;

            var d = Iso.DirectionDelta(v.Dir);
            var pos = new Vector2(-d.x, -d.y) * HitKnockback;
            return new Vector3((pos.x - pos.y) * Iso.HalfW, -(pos.x + pos.y) * Iso.HalfH, 0f);
        }

        /// <summary>
        /// 贴图异步到位后**全量重铺一次**（只由 `SpriteFrames.ConsumeRepaintRequest()` 触发）。
        /// <para>必须**连 NPC 一起重铺**：NPC 视图不在 `_entities` 里（见文件头 ②），
        /// 只在 `_npcs` 里 —— 只铺 `_entities` 的话，NPC 的贴图异步到位后没人通知它，
        /// 它会一直停在纯色占位块上（与原版 NPC 显示色块同源）。</para>
        /// <para>这里**只置 `NeedsFrameRefresh`，绝不 `Anim.Replay()`**：重铺会在"每有一张帧贴图到位"
        /// 时被触发（走路一圈 8 帧 = 最多 8 次），Replay 会把**所有实体**的动画打回第 0 帧 ⇒ 动画
        /// 保住当前帧号即可：`ApplyFrame` 用的是 `Anim.CurrentKey`（当前帧），照样能换成新到位的真图。</para>
        /// </summary>
        private void RefreshAllFrames()
        {
            foreach (var kv in _entities)
            {
                kv.Value.NeedsFrameRefresh = true;
            }
            //   漏掉它的后果：图标回调到达时没人通知地面视图（`ApplyGroundItemIcon` 只在
            //   `NeedsFrameRefresh` 为真时才被调），而 `Resolve` 成功那一帧只是把
            //   `_repaintRequested` 置起来 —— 全量重铺是它唯一的消费点。
            foreach (var kv in _groundItems)
            {
                if (kv.Value != null) kv.Value.NeedsFrameRefresh = true;
            }
            foreach (var kv in _npcs)
            {
                var v = kv.Value;
                if (v == null) continue;
                v.NeedsFrameRefresh = true;
            }
        }

        private void DestroyView(EntityView v)
        {
            if (v == null) return;
            try
            {
                if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
            }
            catch (Exception e)
            {
                ViewLog.WarnThrottled("destroy.fail", $"销毁视图节点失败（{e.GetType().Name}: {e.Message}）");
            }
            v.Root = null;
            v.Renderer = null;
        }

        private void DestroyViewRoot()
        {
            try
            {
                UnityEngine.Object.Destroy(_root.gameObject);
            }
            catch (Exception e)
            {
                ViewLog.WarnThrottled("destroy.root.fail", $"销毁实体根节点失败（{e.GetType().Name}: {e.Message}）");
            }
            _root = null;
        }

        /// <summary>`0xAARRGGBB` → `Color`（接口层不依赖 Unity 类型，故用打包整数传色）。</summary>
        private static Color UnpackArgb(uint argb)
        {
            var a = (byte)((argb >> 24) & 0xFF);
            var r = (byte)((argb >> 16) & 0xFF);
            var g = (byte)((argb >> 8) & 0xFF);
            var b = (byte)(argb & 0xFF);
            return new Color32(r, g, b, a == 0 ? (byte)255 : a);
        }

        // ── 自证用（不在契约里）────────────────────────────────────────────────

        /// <summary>自证用：各实体「贴出纯色占位图」的累计次数（键 = 实体 id）。**不在 `IViewModule` 契约里**。</summary>
        private static readonly Dictionary<int, int> PlaceholderTicks = new Dictionary<int, int>();

        private static void CountPlaceholderTick(int entityId)
        {
            PlaceholderTicks.TryGetValue(entityId, out var n);
            PlaceholderTicks[entityId] = n + 1;
        }

        /// <summary>自证用：某实体累计贴过多少次纯色占位图。**不在 `IViewModule` 契约里**。</summary>
        internal static int PlaceholderTicksOf(int entityId)
        {
            PlaceholderTicks.TryGetValue(entityId, out var n);
            return n;
        }

        /// <summary>
        /// NPC 视图逐条状态（自证/排障用，**不在契约里**）。
        /// <para>用途：`client/_dev/p_a21_npc.cs` 用它判定「NPC 是不是还停在纯色占位块上」，
        /// （旧版本的 `ViewModule` 没有这个方法）。</para>
        /// </summary>
        internal string DumpNpcDebug()
        {
            var p = AppContext.I != null ? AppContext.I.Player : null;
            var pg = p != null ? p.Grid : Vector2Int.zero;
            var sb = new System.Text.StringBuilder();
            sb.Append($"NPC 视图 {_npcs.Count} 个（玩家格=({pg.x},{pg.y})）：");
            foreach (var kv in _npcs)
            {
                var v = kv.Value;
                if (v == null)
                {
                    sb.Append($"\n  id={kv.Key}: 记录为 null");
                    continue;
                }
                var sprite = v.Renderer != null ? v.Renderer.sprite : null;
                sb.Append($"\n  id={kv.Key} code={v.SpriteCode} 格=({v.Grid.x},{v.Grid.y}) Dir={v.Dir}")
                  .Append($" 期望Dir={Iso.DirectionTo(v.Grid, pg)}")
                  .Append($" Playing={v.Playing} 帧键={v.Anim.CurrentKey ?? "null"}")
                  .Append($" 帧={v.Anim.FrameIndex}/{v.Anim.FrameCount} NeedsFrameRefresh={v.NeedsFrameRefresh}")
                  .Append($" Renderer={(v.Renderer == null ? "无SR" : (sprite == null ? "null" : sprite.name + $"({sprite.rect.width}x{sprite.rect.height})"))}")
                  .Append($" 占位={v.UsingPlaceholder}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// **排序键逐帧采样**（自证/驱动用，**不在契约里**）。
        /// <para>为什么必须有它：`SpriteRenderer` 的**实际绘制次序没有公开读取入口**（Unity 内部按
        /// `(sortingLayer, sortingOrder, 视图轴距离)` 排）⇒ 要判"人物与 NPC 重合时闪不闪"，只能逐帧把
        /// 两个节点的这三样读出来自己比。行格式（`\n` 分隔）：`类型|id|spriteCode|格|sortingOrder|z`
        /// —— 驱动侧按 `sortingOrder` 升序、同值时 `z` 降序排，即得该帧的绘制次序（z 小 = 离正交相机近 = 画在前）。</para>
        /// </summary>
        internal string DumpSortKeys()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("sortkeys version=u26-20260924");
            AppendSortKey(sb, _player, "player");
            foreach (var kv in _entities)
            {
                if (kv.Value != null && !kv.Value.IsPlayer) AppendSortKey(sb, kv.Value, "monster");
            }
            foreach (var kv in _groundItems) AppendSortKey(sb, kv.Value, "item");
            foreach (var kv in _npcs) AppendSortKey(sb, kv.Value, "npc");
            return sb.ToString();
        }

        /// <summary>`DumpSortKeys` 的单行（`Root`/`Renderer` 失效时静默跳过 —— 只读诊断，不改状态）。</summary>
        private static void AppendSortKey(System.Text.StringBuilder sb, EntityView v, string kind)
        {
            if (v == null || v.Root == null) return;
            var order = v.Renderer != null ? v.Renderer.sortingOrder : int.MinValue;
            sb.Append('\n').Append(kind).Append('|').Append(v.EntityId)
              .Append('|').Append(v.SpriteCode ?? "-")
              .Append('|').Append(v.Grid.x).Append(',').Append(v.Grid.y)
              .Append('|').Append(order)
              .Append('|').Append(v.Root.transform.position.z.ToString("0.######"));
        }

        /// <summary>一行状态摘要。</summary>
        internal string DumpStats()
        {
            var monsters = 0;
            var dead = 0;
            foreach (var kv in _entities)
            {
                if (kv.Value.IsPlayer) continue;
                monsters++;
                if (kv.Value.Dead) dead++;
            }
            var npcAlive = 0;
            foreach (var kv in _npcs)
            {
                if (kv.Value != null && kv.Value.Root != null) npcAlive++;
            }
            var sample = "";
            foreach (var kv in _entities)
            {
                sample += $" [{kv.Key}:{kv.Value.Playing}#{kv.Value.Anim.FrameIndex}/{kv.Value.Anim.FrameCount}" +
                          $@"×{kv.Value.Anim.SpeedScale:0.###}" +
                          $"{(kv.Value.UsingPlaceholder ? "(占位)" : "")}]";
            }
            return $"视图统计：怪物视图 {monsters}（其中尸体 {dead}），地面物品 {_groundItems.Count} 个，" +
                   $"NPC 视图 {npcAlive}/{_npcs.Count}，" +
                   $"玩家视图={(_player != null ? "有" : "无")}，可渲染={!_cannotRender}；样例{sample}";
        }
    }
}