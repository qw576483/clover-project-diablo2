// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/GameConst.cs
// 全项目**唯一**的常量来源（`tools/ai-skill/conventions.md`「常量类：静态类 + const，不写裸字面量」）。
//
// ⛔ 契约冻结：带「契约」标记的常量逐字来自 `docs/步骤文档.md` §3.5，**不许改值**。
//    本文件只放「手感参数且永不增长的」常量；**同质化数值一律走配表**（不在此处硬编码）。
// ─────────────────────────────────────────────────────────────────────────────

namespace Diablo2.Core
{
    /// <summary>项目级常量（格子 / 投影 / 速度 / 背包尺寸 / 距离 / 排序 / id 约定）。</summary>
    public static class GameConst
    {
        // ── 契约 §3.5：格子与等距投影（**不许改值**）──────────────────────────
        /// <summary>契约：逻辑格子边长 = 1 世界单位。</summary>
        public const int TileSize = 1;

        /// <summary>契约：等距菱形瓦片宽（像素）。</summary>
        public const int IsoTilePxW = 128;

        /// <summary>契约：等距菱形瓦片高（像素）。</summary>
        public const int IsoTilePxH = 64;

        /// <summary>契约：精灵 PPU。</summary>
        public const int PixelsPerUnit = 64;

        /// <summary>
        /// 契约：玩家**跑**速（格/秒）。名字里的 `Walk` 是历史（契约 §3.5 已定名，不改名）。
        /// <para>★ **片 2b 修正：原值 6.0 是原版的 2 倍**（旧注释自称"原版基础跑速"，但无出处）。
        /// 出处推导（三条都在参考工程里）：</para>
        /// <list type="bullet">
        /// <item>原版 → `unit.runSpeed = 15`（**map 单位/秒**）：
        /// `原版资源/参考工程_Diablerie/Diablerie/Assets/Scripts/Diablerie/Engine/Player.cs:48-49`
        /// （`unit.walkSpeed = 7; unit.runSpeed = 15;`）。</item>
        /// <item>**1 格 = 5 map 单位**：`Engine/Iso.cs:9-12` 的 `SubTileCount = 5`
        /// （换算点 `Engine/World/LevelBuilder.cs:245/328/508` 的 `* Iso.SubTileCount`）。</item>
        /// <item>⇒ 15 ÷ 5 = **3.0 格/秒**（对应的走速见 <see cref="PlayerWalkSpeedFactor"/>）。</item>
        /// </list>
        /// </summary>
        public const float PlayerWalkSpeed = 3.0f;

        /// <summary>
        /// 玩家**走**速倍率 = 原版 `walkSpeed / runSpeed` = `7 / 15`。
        /// <para>出处：`Player.cs:48-49` 的 `unit.walkSpeed = 7; unit.runSpeed = 15;`
        /// ⇒ 走速 = <see cref="PlayerWalkSpeed"/> × 7/15 = 3.0 × 7/15 = **1.4 格/秒**
        /// （原版"走"的绝对速度）。</para>
        /// <para>★ 片 2b：本常量**迁到此处**（原在 `Module/Player/PlayerModule.WalkSpeedFactor`，值 0.5
        /// = "走 ≈ 跑的一半"，**无出处**）——全项目只保留这一份字面量，`PlayerModule` 改为引用它。</para>
        /// </summary>
        public const float PlayerWalkSpeedFactor = 7f / 15f;

        /// <summary>契约：背包列数。</summary>
        public const int InventoryCols = 10;

        /// <summary>契约：背包行数。</summary>
        public const int InventoryRows = 4;

        /// <summary>契约：腰带格数。</summary>
        public const int BeltSlots = 4;

        // ── 由契约常量派生（**禁止再写第二份字面量**）─────────────────────────
        /// <summary>瓦片半宽（像素）= 64。</summary>
        public const float HalfTilePxW = IsoTilePxW * 0.5f;

        /// <summary>瓦片半高（像素）= 32。</summary>
        public const float HalfTilePxH = IsoTilePxH * 0.5f;

        /// <summary>等距投影 X 轴半格（世界单位）= 1.0 —— `Iso.GridToWorld` 用。</summary>
        public const float IsoHalfW = HalfTilePxW / PixelsPerUnit;

        /// <summary>等距投影 Y 轴半格（世界单位）= 0.5 —— `Iso.GridToWorld` 用。</summary>
        public const float IsoHalfH = HalfTilePxH / PixelsPerUnit;

        /// <summary>背包总格数（10×4 = 40）。</summary>
        public const int InventoryCellCount = InventoryCols * InventoryRows;

        // ── 地图尺寸范围（格）─────────────────────────────────────────────────
        /// <summary>
        /// 罗格营地（固定布局）**宽** = 原版关卡尺寸 **56**。
        /// <para>出处：原版 `Levels.txt`「Act 1 - Town」（LevelName = Rogue Encampment）的
        /// `SizeX`（`原版资源/d2raw/data/global/excel/Levels.txt`）；生成物
        /// `Module/Map/MapGenTownLayout.Width` 由 `tools/d2codec/export_town_layout.py`
        /// 直接读那一行得出 ⇒ 两者**必须相等**（`MapGenTown` 不一致时直接报错，见该文件）。</para>
        /// <para>历史上这里曾是 32（= 只取 4 块里的 `TownW1` 营地本体再裁成 32×32），
        /// 那会把营地外的河裁掉；agent-41 改成整关、agent-42 按主 agent 裁决同步本常量。</para>
        /// </summary>
        public const int TownWidth = 56;

        /// <summary>罗格营地（固定布局）**高** = 原版关卡尺寸 **40**；出处同 <see cref="TownWidth"/>。</summary>
        public const int TownHeight = 40;

        /// <summary>血腥荒野随机尺寸下限。</summary>
        public const int WildernessMinSize = 48;

        /// <summary>血腥荒野随机尺寸上限。</summary>
        public const int WildernessMaxSize = 80;

        /// <summary>
        /// 邪恶洞穴：每轴的**原版洞穴预设块数**下限（`MapGenCave`）。
        /// <para>为什么按"块数"而不是"格数"：原版洞穴层就是由 **25×25 的预设块**拼出来的
        /// （`原版资源/d2raw/.../ACT1/CAVES/*.ds1`）⇒ 合法尺寸只能是块边长的整数倍，
        /// 自定一个 40~64 的格数会让块拼不齐。块边长 = 25，见 `MapGenCaveLayout.PieceSize`。</para>
        /// </summary>
        public const int CaveSlotsMin = 2;

        /// <summary>邪恶洞穴：每轴的原版块数上限（⇒ 尺寸 75）。</summary>
        public const int CaveSlotsMax = 3;

        /// <summary>邪恶洞穴随机尺寸下限（= <see cref="CaveSlotsMin"/> × 原版块边长 25）。</summary>
        public const int CaveMinSize = 50;

        /// <summary>邪恶洞穴随机尺寸上限（= <see cref="CaveSlotsMax"/> × 原版块边长 25）。</summary>
        public const int CaveMaxSize = 75;

        /// <summary>任意区域尺寸硬下限（越小越可能生成失败，低于此值拒绝生成并报错）。</summary>
        public const int MapMinSize = 24;

        /// <summary>任意区域尺寸硬上限（超过会拖慢瓦片铺装）。</summary>
        public const int MapMaxSize = 96;

        /// <summary>随机生成失败后的重试次数上限（连通性校验不过 ⇒ 换 seed 重生成）。</summary>
        public const int MapGenMaxRetry = 8;

        // ── 距离常量（**单位：格**）───────────────────────────────────────────
        /// <summary>点击移动时判定「已到达路径点」的距离。</summary>
        public const float ArriveEpsilon = 0.08f;

        /// <summary>近战攻击距离。</summary>
        public const float MeleeRange = 1.6f;

        /// <summary>远程/施法攻击距离上限。</summary>
        public const float RangedRange = 8f;

        /// <summary>拾取地面物品的距离。</summary>
        public const float PickupRange = 1.4f;

        /// <summary>与 NPC 对话/交易的触发距离。</summary>
        public const float TalkRange = 2.4f;

        /// <summary>踩到出入口 / 传送门触发距离。</summary>
        public const float PortalRange = 1.2f;

        /// <summary>怪物发现玩家（进入仇恨）的距离。</summary>
        public const float MonsterAggroRange = 8f;

        /// <summary>怪物放弃追击的距离（超过即脱战回原位）。</summary>
        public const float MonsterLeashRange = 14f;

        /// <summary>鼠标悬停可交互物件的最大距离（超距离光标不变）。</summary>
        public const float HoverRange = 10f;

        // ── 深度排序（`constraints.md` #5：sortingOrder 必须随格子变化）──────
        /// <summary>每格的排序步长（同格内再按层加偏移）。</summary>
        public const int SortOrderStep = 4;

        /// <summary>排序基准值（保证 > 背景层排序）。</summary>
        public const int SortOrderBase = 100;

        /// <summary>地面层偏移。</summary>
        public const int LayerOffsetGround = 0;

        /// <summary>物件层偏移（建筑 / 树 / 岩石）。</summary>
        public const int LayerOffsetObject = 1;

        /// <summary>实体层偏移（角色 / 怪物 / 地面物品）。</summary>
        public const int LayerOffsetEntity = 2;

        /// <summary>遮蔽层偏移（屋顶 / 树冠，半透明遮挡）。</summary>
        public const int LayerOffsetOverlay = 3;

        /// <summary>
        /// **deck（桥面 / 平台 / 甲板）上实体的排序档位** —— 只对"站在 deck 格上的实体"生效
        /// （判定 = `IMapModule.IsDeckGrid`，登记见 `Module/Map/DeckTiles` + `GridMap.SetTiles`），
        /// 其余实体仍用 <see cref="LayerOffsetEntity"/>。
        ///
        /// <para><b>为什么需要它</b>（2026-09-22 用户实测「营地出门的桥，还是从桥下走」）：
        /// 罗格营地出城那座桥的桥面格，**正南一格恒是桥栏杆**（`moor_bridge` 物件，
        /// `tools/probes/measure/measure_bridge_deck.py` 实测：栏杆内容自本格底边**向上溢出 ≈2 格**）
        /// ⇒ 桥面上的实体按普通档 `4D+102` 排，必然被南侧栏杆 `4(D+1)+101 = 4D+105` 盖住。</para>
        ///
        /// <para><b>数值推导</b>（⛔ 不写裸数字；D = gx+gy）：
        ///   `物件(D+1) = (D+1)*SortOrderStep + SortOrderBase + LayerOffsetObject = 4D+105`；
        ///   `物件(D+2) = (D+2)*SortOrderStep + SortOrderBase + LayerOffsetObject = 4D+109`。
        ///   桥面实体必须 **&gt; 物件(D+1)**（才不被正南栏杆盖住）、
        ///   又必须 **&lt; 物件(D+2)**（否则会盖住正南第二格那些更靠前的物件/栏杆）。
        ///   ⇒ `LayerOffsetObject + SortOrderStep &lt; X &lt; LayerOffsetObject + 2*SortOrderStep`
        ///   ⇒ `5 &lt; X &lt; 9` ⇒ 取**满足约束的最小值** `LayerOffsetObject + SortOrderStep + 1`
        ///   （改动量最小；= 6，桥面实体 = `4D+106` > 物件(D+1)=4D+105 且 &lt; 物件(D+2)=4D+109）。</para>
        /// <para>⚠️ `4D+106` 与 `实体(D+1)` 同值：桥面格的正南恒为栏杆（**不可走** ⇒ 不会有实体）
        /// ⇒ 实际不会出现并列；`mapcheck` 有断言守着这条（逐桥面格都要求正南是栏杆）。</para>
        /// </summary>
        public const int LayerOffsetDeckEntity = LayerOffsetObject + SortOrderStep + 1;

        // ── 实体 id 约定（**全项目唯一**）────────────────────────────────────
        /// <summary>玩家实体 id（`DamageArgs.targetId` 用它表示玩家）。</summary>
        public const int PlayerEntityId = 1;

        /// <summary>怪物实体 id 起点（`GameConst.MonsterIdBase + 递增序号`）。</summary>
        public const int MonsterIdBase = 1000;

        /// <summary>地面物品实体 id 起点。</summary>
        public const int GroundItemIdBase = 100000;

        /// <summary>是否为本项目怪物 id。</summary>
        public static bool IsMonsterId(int id) => id >= MonsterIdBase && id < GroundItemIdBase;

        // ── 存档 ─────────────────────────────────────────────────────────────
        /// <summary>
        /// 存档格式版本（`CharacterSave.version` 应写本值；版本不一致时按需迁移）。
        /// <para>★ **2026-09-24 `u52cur` 片：1 → 2**（**`Core/**` 属冻结区，本次由 main 明确授权改这一行**）。
        /// 变更内容 = **只把常量 +1**，**不改任何字段布局**（存档 schema 一字未动）。</para>
        /// <para>**为什么必须 +1**：`charstat` 片把 1 级"生命/法力/耐力"的**起始量**口径改对了
        /// （`PlayerStats.Max*` ← `class_c.hp_add` / `base_stamina` ← 官方 `charstats.txt`），
        /// 而**改前创建**的档里 `life/mana/stamina` 是"起始四维 × 成长系数"那套旧式子的产物
        /// （活档实证 `client/setting/saves/S2203805.json`：`life=60 / mana=22 / stamina=20`）——
        /// 与现在的上限**不同源**。磁盘上 **74/74** 个现存档都是 `version == 1`
        /// （普查 = `.ai-tmp/test/u52block-save-census.txt`；其中 69 档是旧口径）
        /// ⇒ **不 +1**，`PlayerModule.LoadFrom` 的 `save.version &lt; GameConst.SaveVersion` **永不成立**，
        /// 那 69 档迁不动 ⇒ 实机残留「耐力 cur = 20 / max = 84」（用户报的"人物状态框数值不对"那格）。</para>
        /// <para>**消费方**：`Module/Player/PlayerModule.LoadFrom`（更旧版本 ⇒ 三资源按当前口径补满 +
        /// 一条 Info，并在档里旧值打出来）；`Module/Save/SaveModule.cs` 的"版本不符 ⇒ Warn + 兼容路径"。
        /// ⛔ **旧客户端读新档**：走既有"降级处理"（`fileVersion != SaveVersion` ⇒ Warn + 尽力读）
        /// —— 因为本变更**不动字段**，所以旧客户端读到的是完整数据（`itemcheck §11` 的 `version:99` 用例覆盖该路径）。</para>
        /// </summary>
        public const int SaveVersion = 2;

        /// <summary>
        /// 存档的键前缀（`char/{角色名}`）。
        /// <para>⚠️ **A6 起角色档已改为「一角色一文件」**（`&lt;SettingDir&gt;/saves/&lt;角色名&gt;.json`，
        /// 走引擎 `CloverEngine.FileSlotStore`）⇒ 这个前缀现在**只用于旧档懒迁移与清理**
        /// （见 `SaveModule.ReadRaw`：读到老键才搬进槽位，**搬成功就删旧键**）。</para>
        /// </summary>
        public const string SaveKeyPrefix = "char/";

        /// <summary>
        /// 存档**创建先后**索引键（角色名清单）。
        /// <para>⚠️ 它只表达「选角屏的排列顺序」、**不再是"有哪些角色"的权威来源**（权威 = 槽位目录里的文件）：
        /// 引擎 `FileSlotStore.List()` 是**字典序**、不是插入序（其文件头已裁决"要创建先后就自己维护索引键"），
        /// 而选角屏卡片顺序是**可见行为** ⇒ 顺序仍留在这里维护。</para>
        /// </summary>
        public const string SaveIndexKey = "char/index";

        /// <summary>设置项在 `Game.Setting` 里的键：BGM 音量。</summary>
        public const string SettingKeyBgmVolume = "audio/bgm_volume";

        /// <summary>设置项在 `Game.Setting` 里的键：音效音量。</summary>
        public const string SettingKeySfxVolume = "audio/sfx_volume";

        /// <summary>
        /// 设置项在 `Game.Setting` 里的键：**BGM 静音**（bool）。
        /// <para>agent-11 的 `Module/Audio/AudioModule.cs:25` 把"静音持久化"列为未决项（当时缺这两个键）；
        /// 语义 = 与 <see cref="SettingKeyBgmVolume"/> **两个维度、互不覆盖**：音量为 0 与静音是两回事，
        /// 取消静音要能还原到静音前的音量（原版设置界面就是这么做的）。</para>
        /// <para>键名与既有口径同构（`audio/{项}`，对照 <see cref="SettingKeyBgmVolume"/> / <see cref="SettingKeySfxVolume"/>）。</para>
        /// </summary>
        public const string SettingKeyBgmMute = "audio/bgm_mute";

        /// <summary>设置项在 `Game.Setting` 里的键：**音效静音**（bool）；语义同 <see cref="SettingKeyBgmMute"/>。</summary>
        public const string SettingKeySfxMute = "audio/sfx_mute";

        /// <summary>设置项在 `Game.Setting` 里的键：全屏。</summary>
        public const string SettingKeyFullscreen = "video/fullscreen";

        /// <summary>自动存档间隔（秒）。</summary>
        public const float AutoSaveIntervalSeconds = 120f;

        // ── UI ───────────────────────────────────────────────────────────────
        /// <summary>
        /// UI 参考分辨率宽 = **1920**（`CanvasScaler` ScaleWithScreenSize, match 0.5）。
        /// <para>★ **出处（不是估的，是引擎写死的）**：引擎 `UIManager` 构造函数里
        /// `scaler.referenceResolution = new Vector2(1920f, 1080f)` 且 `matchWidthOrHeight = 0.5f`
        /// —— `clover-client-unity-engine/Runtime/Presentation/UI.cs:52-56`。
        /// 引擎**不接受业务覆盖** ⇒ 项目侧若按 1280×720 布点，所有面板在真机上整体放大 1.5 倍并错位。</para>
        /// <para>⇒ **一律按 1920×1080 写布局**（`UI/UiArt.cs` 已提供 `RefWidth/RefHeight`）。</para>
        /// </summary>
        public const int UiReferenceWidth = 1920;

        /// <summary>UI 参考分辨率高 = **1080**；出处同 <see cref="UiReferenceWidth"/>。</summary>
        public const int UiReferenceHeight = 1080;

        /// <summary>Toast 默认停留秒数。</summary>
        public const float ToastDuration = 2f;

        /// <summary>伤害飘字默认停留秒数。</summary>
        public const float FloatTextDuration = 1.2f;

        // ── 战斗节奏 ─────────────────────────────────────────────────────────
        /// <summary>玩家普通攻击间隔（秒）——手感参数，不随等级增长。</summary>
        public const float PlayerAttackInterval = 0.55f;

        /// <summary>命中率上下限（官方公式：5%~95%）。</summary>
        public const float MinHitChance = 0.05f;

        /// <summary>命中率上限（官方公式）。</summary>
        public const float MaxHitChance = 0.95f;

        /// <summary>死亡后可在城内复活的等待秒数。</summary>
        public const float ReviveDelaySeconds = 0f;

        /// <summary>尸体/掉落保留时间（秒）。</summary>
        public const float GroundItemLifetimeSeconds = 600f;
    }
}
