// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/EntityView.cs
// 一个实体（玩家 / 怪物 / 地面物品）的**视图记录**：节点 + 渲染器 + 头顶血条 + 逐帧动画器。
//
// 与 `ViewModule` 分开一个文件的原因：`tools/ai-skill/constraints.md` #1「一个 `.cs` 一个类」，
// 同文件多个类型会让 Unity 的预制体 `m_Script` fileID 指错（且编译不报错）。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Def;
// ★ agent-33 引擎下沉 A2：引擎侧新增了**同名**枚举 `CloverEngine.Dir8`（`Runtime/Core/Dir8.cs`），
//   本文件同时 `using CloverEngine;` ⇒ 裸 `Dir8` 会变成 CS0104 二义。
//   用别名把裸 `Dir8` 钉死为**项目枚举**（语义与序号和改动前**完全一致**）。
using Dir8 = Diablo2.Def.Dir8;
using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>实体视图（玩家 / 怪物 / 地面物品）。</summary>
    internal sealed class EntityView
    {
        /// <summary>实体 id（玩家 = `GameConst.PlayerEntityId`；地面物品 = 地面物品 id）。</summary>
        public int EntityId;

        /// <summary>是否玩家视图（玩家血条在 HUD 双球上，世界血条不建）。</summary>
        public bool IsPlayer;

        /// <summary>是否地面物品视图（不走路、不播动作、无血条）。</summary>
        public bool IsGroundItem;

        /// <summary>玩家职业（占位色用；非玩家为默认）。</summary>
        public PlayerClass Cls;

        /// <summary>怪物种类 id（`monster_c` 主键；非怪物为 0）。</summary>
        public int KindId;

        /// <summary>怪物精灵代码（`monster_c.sprite` 小写；非怪物为空）。</summary>
        public string SpriteCode;

        /// <summary>视图根节点。</summary>
        public GameObject Root;

        /// <summary>精灵渲染器。</summary>
        public SpriteRenderer Renderer;

        /// <summary>世界空间头顶血条（玩家为 null）。</summary>
        public WorldHpBar Bar;

        /// <summary>逐帧动画器（**本项目新增**）。</summary>
        public readonly SpriteAnimator Anim = new SpriteAnimator();

        /// <summary>当前朝向。</summary>
        public Dir8 Dir = Dir8.S;

        /// <summary>当前请求的动作（用于"只在动作变化时才 Play"，避免每帧被打回第 0 帧）。</summary>
        public ViewAnim Playing = ViewAnim.Idle;

        /// <summary>需要立刻重取一次贴图（朝向变了 / 贴图异步到位）。</summary>
        public bool NeedsFrameRefresh = true;

        /// <summary>占位色（素材到位后该值不再用于渲染，只作为"是否占位"的判据）。</summary>
        public Color BaseColor = Color.white;

        /// <summary>是否正在用占位图（决定要不要打 BaseColor）。</summary>
        public bool UsingPlaceholder = true;

        /// <summary>受击闪白剩余秒数。</summary>
        public float HitFlashTimer;

        /// <summary>施法动作剩余秒数（只玩家用）。</summary>
        public float CastTimer;

        /// <summary>挥击动作剩余秒数（只玩家用；由 `Events.PlayerAttacked` 设置）。</summary>
        public float AttackTimer;

        /// <summary>是否已死（尸体姿态）。</summary>
        public bool Dead;

        /// <summary>上次同步到的世界坐标（判"这一帧动没动"，决定 Walk/Idle）。</summary>
        public Vector3 LastWorld;

        /// <summary>所在格（排序用）。</summary>
        public Vector2Int Grid;

        /// <summary>已播完死亡动画、已置成"尸体半透明"（只做一次）。</summary>
        public bool CorpseFaded;
    }
}
