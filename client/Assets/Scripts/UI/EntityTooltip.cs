// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/EntityTooltip.cs  ★ U4 新增（用户报「鼠标放在怪身上也没显示信息」）
//
// 做的事（原版 D2 的两条表现）：
//   ① 鼠标悬停一只怪物 ⇒ 该怪物**头顶上方**显示它的**名字**（`monster_c.name` 的中文名，
//      与原版怪物名同一来源 = `Def.MonsterState.name`）；
//   ② 同一处再显示一行 **血量 `当前/上限`**。
//
// 数据来源（**零 `using Diablo2.Module`**，分层自检 ③；全部走 `Diablo2.Def` 载荷）：
//   · 悬停目标 = `Events.HoverTargetChanged`（载荷 `Def.HoverTarget`；`cursor == Attack` 即"指针下有怪物"）
//     —— 发送方 `Module/Input/InputReader.UpdateHover`（每帧把 `HoverGrid` 交给 `HoverPicker.Resolve`）。
//   · 血量 = `Events.MonsterSpawned` / `Events.MonsterChanged`（载荷 `Def.MonsterState`，**引用类型、
//     模块原地更新**，故本类只缓存引用、每次画的时候现读 hp/maxHp，绝不缓存副本）。
//     ⚠️ 为什么不去问 `IMonsterModule.All`：那个接口声明在 `Diablo2.Module` 里，`UI/**` 不许引用
//     （分层硬规则）；而 `Def.MonsterState` 正好是同一个对象 ⇒ 走事件既合法又拿得到实时血量。
//
// 装配方式（⛔ 不碰 `UI/HudPanel.cs`：本轮另有片在改它）：
//   沿用本项目**已有的自安装**先例 `UI/CursorView.cs`（`[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`
//   + `DontDestroyOnLoad` 的独立画布）—— 不必让用户先跑一次编辑器菜单生成预制体。
//
// 「按某键看血量」的口径（⛔ 未实装，**不是遗漏**）：任务书原话是"鼠标悬停 + 可按某键看血量"，
//   但**该键位在原版数据里没有载体**（`原版资源/d2text` / `d2lod1.10txt-1.10f` 无怪物血条键位定义），
//   故本轮**不发明键位**（skill §0 铁律 3「写不出出处的量不许进工程」）⇒ 血量**随悬停常显**，
//   键位口径登记在 `.ai-tmp/test/report-U4.md` 的「未决」。拿到出处后只改本文件一处即可。
//
// ⛔ 一个 `.cs` 一个 MonoBehaviour；日志一律 `UiLog.*`（不裸 `UnityEngine.Debug` 那族）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>
    /// 怪物悬停提示层（名字 + 血量；见文件头）。独立画布、常驻、按事件驱动（不是 MonoBehaviour 面板）。
    /// </summary>
    public class EntityTooltip : MonoBehaviour
    {
        /// <summary>画布 `sortingOrder`：与 `UI/CursorView` 同档（引擎常驻画布是 0）。</summary>
        private const int CanvasSortingOrder = 1;

        /// <summary>
        /// 名字牌在怪物格中心之上抬多高（世界单位）= 3 × `GameConst.IsoHalfH`（= 1.5 格）。
        /// 为什么是 3：怪物精灵的 **pivot 在脚底**（`Iso.GridToWorld` = 格中心 = 脚下），图形自格心向上长
        /// ≥2 格（`D2/Data/Global/Monsters/**` 的精灵高度），抬 1.5 格才落在头顶上方而不盖住身体。
        /// **本项目选定值**（原版该偏移无载体；同 `GroundItemLabelView.LabelSize` 的处置方式）。
        /// </summary>
        private const float NameLiftHalfTiles = 3f;

        private static EntityTooltip _instance;

        /// <summary>新一局 Play 复位静态闸门（工程可能开了「不重载域」）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForNewPlaySession()
        {
            _instance = null;
        }

        /// <summary>建常驻画布（与 `UI/CursorView.Install` 同一处置）。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;

            var go = new GameObject("[EntityTooltip]");
            Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<EntityTooltip>();
        }

        // ── 状态 ─────────────────────────────────────────────────────────────
        private RectTransform _canvasRt;
        private D2Label _name;
        private D2Label _hp;
        private bool _built;
        private bool _subscribed;
        private bool _stageActive;

        /// <summary>`Def.MonsterState` 引用表（**不拷贝**：模块原地更新同一个对象）。</summary>
        private readonly Dictionary<int, MonsterState> _states = new Dictionary<int, MonsterState>();

        /// <summary>上一次已打日志的悬停怪物 id（悬停不变则不打第二条）。</summary>
        private int _loggedHoverId = int.MinValue;

        private bool _noCanvasLogged;
        private bool _noCamLogged;

        /// <summary>当前是否正显示（自证/断言用）。</summary>
        public bool Showing { get; private set; }

        private void Awake() => Build();

        private void OnDestroy()
        {
            Unsubscribe();
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            // 引擎门面（`Game.Event`）在 `Game.Launch` 之后才有 ⇒ 懒挂接（同 CursorView）。
            if (!_subscribed) TrySubscribe();
        }

        // ── 构建（独立画布 + 两行位图字）──────────────────────────────────────
        private void Build()
        {
            if (_built) return;
            _built = true;

            var canvasNode = UIFactory.CreateNode("EntityTooltipCanvas", transform);
            _canvasRt = canvasNode;
            var canvas = canvasNode.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;

            var scaler = canvasNode.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = UiLayoutGame.CursorCanvasRef;
            scaler.matchWidthOrHeight = UiLayoutGame.CursorCanvasMatch;

            // 名字牌：字体/字号沿用项目 UI 口径（`D2Text.D2Font.Font16` = 原版最小号位图字体，
            // 与地面物品名牌 `GroundItemLabelView` 同一号）。
            //
            // ★★ V6 修（缺陷 3：实机图 `v5_14_monster_hover.png` 里名字/血量画成**散落的黄色小字块**）：
            //   根因 = 这两条 `D2Label.Create` **没给字号**（`fontSize` 走默认 0 ⇒ `D2Label` 用 1:1
            //   **原版 px** 画（chi 字模格只有 13px）；而本画布参考分辨率是 **1920×1080**
            //   （`UiLayoutFlow.RefWidth/Height`）⇒ 画出来的字只有同屏其它 UI 文字的 13/28.8 ≈ **45%**，
            //   肉眼看就是"一堆小字块"。
            //   同时**排版框**原来传的是**世界格 px**（`GameConst.IsoTilePxW = 128`）—— 与字号的画布单位
            //   混用两套单位 ⇒ 换行宽度错（血量那一行会被提前折行）。
            //   现按 `UiArt.Label` 的调用方口径统一到**画布单位**：
            //     字号 = `(int)UiLayoutGame.FontPx16`（原版 font16 的原生档 = 28.8 画布px，与对话面板 / HUD 同档）；
            //     排版框 = 同一块"一格"但换算到画布 px（× `UiLayoutGame.K` = 1.8）。
            //   判据：`uicheck` 的实体提示断言（字号档 = FontPx16、框 = 格 × K）。
            var box = new Vector2(GameConst.IsoTilePxW * UiLayoutGame.K, GameConst.HalfTilePxH * UiLayoutGame.K);
            var fontPx = (int)UiLayoutGame.FontPx16;
            _name = D2Label.Create(canvasNode, "EntityName", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.LowerCenter, Color.white, box, Vector2.zero, fontPx);
            _hp = D2Label.Create(canvasNode, "EntityHp", string.Empty, D2Text.D2Font.Font16,
                TextAnchor.LowerCenter, new Color(0.95f, 0.85f, 0.45f, 1f), box, Vector2.zero, fontPx);

            if (_name == null || _hp == null)
            {
                UiLog.Error("怪物悬停提示节点没建出来（`D2Label.Create` 返回 null）⇒ 本局不显示怪物名/血量");
                return;
            }

            _name.SetActive(false);
            _hp.SetActive(false);
        }

        // ── 订阅 ─────────────────────────────────────────────────────────────
        private void TrySubscribe()
        {
            if (Game.Event == null) return;
            _subscribed = true;

            Game.Event.On<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
            Game.Event.On<MonsterState>(Events.MonsterSpawned, OnMonsterState);
            Game.Event.On<MonsterState>(Events.MonsterChanged, OnMonsterState);
            Game.Event.On(Events.StageEntered, OnStageEntered);
            Game.Event.On(Events.StageLeft, OnStageLeft);

            UiLog.Info($"怪物悬停提示已就位并订阅：{Events.HoverTargetChanged}（悬停目标）/"
                + $"{Events.MonsterSpawned}·{Events.MonsterChanged}（血量来源）/StageEntered·StageLeft");
        }

        private void Unsubscribe()
        {
            if (!_subscribed || Game.Event == null) return;
            _subscribed = false;

            Game.Event.Off<Diablo2.Def.HoverTarget>(Events.HoverTargetChanged, OnHoverChanged);
            Game.Event.Off<MonsterState>(Events.MonsterSpawned, OnMonsterState);
            Game.Event.Off<MonsterState>(Events.MonsterChanged, OnMonsterState);
            Game.Event.Off(Events.StageEntered, OnStageEntered);
            Game.Event.Off(Events.StageLeft, OnStageLeft);
        }

        private void OnStageEntered()
        {
            _stageActive = true;
            _states.Clear();                 // 换场：上一张图的怪物状态作废
            UiLog.Info("怪物悬停提示已接管（游戏内才显示；进图清空怪物状态表）");
        }

        private void OnStageLeft()
        {
            _stageActive = false;
            Hide();
            _states.Clear();
            UiLog.Info("已离开游戏内 ⇒ 怪物悬停提示不再显示");
        }

        /// <summary>`Events.MonsterSpawned` / `MonsterChanged` 的收方：只记引用（同一个对象会被原地更新）。</summary>
        private void OnMonsterState(MonsterState state)
        {
            if (state == null || state.id < 0) return;
            _states[state.id] = state;
        }

        /// <summary>
        /// `Events.HoverTargetChanged` 的收方：指针下有怪物 ⇒ 在它头顶显示名字 + 血量。
        /// <para>⚠️ 参数类型必须写**全限定名** `Diablo2.Def.HoverTarget`（事件载荷那个"快照"类）：
        /// 本命名空间 `Diablo2.UI` 里**另有一个**同名类型 `UI/HoverTarget.cs`（uGUI 指针进出的接线件
        /// ⇒ 它没有 cursor/id/gridX/gridY），同命名空间优先于 `using Diablo2.Def` ⇒ 裸写
        /// `HoverTarget` 会解析到那个 MonoBehaviour（实测 CS1061）。</para>
        /// </summary>
        private void OnHoverChanged(Diablo2.Def.HoverTarget t)
        {
            if (!_stageActive || t == null || t.cursor != CursorKind.Attack || t.id < 0)
            {
                _loggedHoverId = int.MinValue;
                Hide();
                return;
            }

            _states.TryGetValue(t.id, out var st);

            // 名字：优先用怪物状态里的名字（含精英前缀，全项目唯一真源 `monster_c.name`），
            //      没有状态（事件未到）时退回悬停载荷里的名字 —— 两条都是"有名字"而不是空串。
            var name = st != null && !string.IsNullOrEmpty(st.name) ? st.name : t.name;
            if (string.IsNullOrEmpty(name)) name = "怪物 #" + t.id;

            _name.text = name;
            _name.color = st != null && st.isChampion
                ? new Color(1f, 0.82f, 0.35f, 1f)          // 精英怪：原版金名（与 ItemTooltip 的稀有黄同族）
                : Color.white;

            if (st != null)
            {
                _hp.text = $"血量 {st.hp}/{st.maxHp}";
                _hp.SetActive(true);
            }
            else
            {
                // 非预期但可解释（怪物刚出生、`MonsterChanged` 还没到）：只显示名字，血量留空
                _hp.text = string.Empty;
                _hp.SetActive(false);
            }

            var pos = AnchoredPositionOf(t.gridX, t.gridY);
            if (!pos.HasValue)
            {
                Hide();
                return;
            }

            _name.SetActive(true);
            _name.rectTransform.anchoredPosition = pos.Value;
            if (_hp.gameObject.activeSelf)
                _hp.rectTransform.anchoredPosition = pos.Value - new Vector2(0f, 20f);

            Showing = true;

            if (_loggedHoverId != t.id)
            {
                _loggedHoverId = t.id;
                UiLog.Info($"[EntityTooltip] 悬停怪物 m#{t.id}「{name}」"
                    + (st != null ? $"血量={st.hp}/{st.maxHp} 精英={st.isChampion}" : "血量=未知（MonsterChanged 未到）")
                    + $" 格=({t.gridX},{t.gridY})");
            }
        }

        /// <summary>隐藏（指针下没有怪物 / 离场）。</summary>
        private void Hide()
        {
            if (_name != null) _name.SetActive(false);
            if (_hp != null) _hp.SetActive(false);
            Showing = false;
        }

        /// <summary>
        /// 怪物格 → 提示在 Canvas 局部坐标里的落点（纯换算：世界 = 格中心抬 1.5 格；
        /// 屏幕 = 相机投影；口径与 `GroundItemLabelView.ScreenAnchoredPositionOf` 完全一致）。
        /// 相机/画布缺失 ⇒ null（调用方藏起来，绝不画在错位置）。
        /// </summary>
        private Vector2? AnchoredPositionOf(int gridX, int gridY)
        {
            if (_canvasRt == null)
            {
                if (!_noCanvasLogged)
                {
                    _noCanvasLogged = true;
                    UiLog.Warn("怪物悬停提示找不到自己的画布 ⇒ 无法定位（提示暂不显示；只报一次）");
                }
                return null;
            }

            var cam = UIFactory.UICamera();
            if (cam == null)
            {
                if (!_noCamLogged)
                {
                    _noCamLogged = true;
                    UiLog.Warn("找不到相机 ⇒ 怪物悬停提示无法从格坐标投影到屏幕（只报一次）");
                }
                return null;
            }

            var world = Iso.GridToWorld(gridX, gridY);
            world.y += GameConst.IsoHalfH * NameLiftHalfTiles;   // 抬到头顶上方（见常量注释）

            var screen = cam.WorldToScreenPoint(world);
            if (screen.z < 0f) return null;                      // 相机背面（切场景那一帧）

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRt, new Vector2(screen.x, screen.y), null, out var local))
            {
                UiLog.WarnOnce("entitytooltip.convert.fail", "屏幕点 → Canvas 局部坐标换算失败 ⇒ 提示位置不更新");
                return null;
            }

            return local;
        }
    }
}
