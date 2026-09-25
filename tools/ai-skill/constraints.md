# 本项目约束与踩坑（只记本项目特有的）

> ⚠️ **本文件【不放引擎修复记录】**。引擎改动影响所有项目，记录在**引擎仓库**：
> `clover-client-unity-engine/修复记录.md`（E 编号 + 最小复现 + 自证）。
> 这里只记"本项目自己踩的坑"，以及"本项目依赖了哪个 E 修复"。

---

## #1 一个 `.cs` 里不许放多个 MonoBehaviour

预制体的 `m_Script` 会指向错的 fileID ⇒ **运行时面板打不开，且编译期不报错**。
一个类一个文件。

## #2 `timeScale = 0` 时不许用 `After/Every`

引擎 `Timer` 用 `Time.deltaTime` 推进 ⇒ 暂停菜单/死亡屏/结算屏里的定时器**永不触发、零报错、零日志**。
一律用 `Game.Timer.AfterUnscaled` / `EveryUnscaled`。

## #3 `Image.Type = Filled` + 无 sprite ⇒ `fillAmount` 静默失效

血球（`healthbar.png` / `manabar.png`）与经验条**不要**靠 `fillAmount`：
① 改用**锚点宽度**（`anchorMax.x = ratio`，`type = Simple`），或 ② 显式给一张 1×1 白 sprite。
交付前**看图**确认它真的在动（数字对但画面不动 = 经典静默失效）。

## #4 等距反投影的取整必须 `FloorToInt`

C# 的 `(int)` 强转对**负数向零截断**，会导致"图外可走/格子错半格"。
一律 `Mathf.FloorToInt`（与引擎 `MapFormat.cs:204` 同一口径）。

## #5 占据排序：`sortingOrder` 必须随格子变化

固定 `sortingOrder` 会让角色被身后的树/建筑盖住。
用 `Iso.SortOrder(gx, gy) = (gx + gy) * 4 + 基准`，同格内按子层级微调。

## #6 点击移动：屏幕 → 世界的反投影必须用**主相机**且相机角度固定

用 `Camera.main.ScreenToWorldPoint` 时，`z` 分量决定投影平面。等距相机下必须显式给
"到地面的距离"（本项目取 `-camera.transform.position.z`），否则点击位置整体偏移。

## #7 面板状态值只能由调用方传入

面板 `Awake` 里取"当前上下文"会拿到空值（面板打开时机早于数据装配）⇒ 静默回退默认值。
一律 `Game.UI.Open<T>(参数)` + 在 `OnOpen(param)` 里刷；**漏传参数要打 `Warn`**。

## #8 单机不得触碰网络门面

`Game.Net` / `Game.Sync` / `Game.Alert` / `Game.CloverScene` / `Game.FrameRoom` / `Game.Schema` /
`Game.Http` / `Game.LanBrowser` 在单机下**全为 null**（引擎内部用 `?.`，业务侧调用则 NRE）。
代码审查时 grep `Game.Net`、`Game.Sync`、`Game.Http` 应为 **0 命中**。

## #9 原版贴图必须按目录设置导入参数

原版素材是 8bit 调色板像素图：若走默认导入（双线性过滤 + 压缩）会**糊掉**。
`Assets/Editor/AssetImporter.cs` 按目录统一设置：
`FilterMode.Point` / `TextureImporterType.Sprite` / `spritePixelsPerUnit = 64` / `alphaIsTransparency = true` /
压缩关闭。**新增素材目录必须同步改它**，否则新素材是糊的（且不报错）。

## #10 等距瓦片必须按"层"画

地面 / 物件 / 遮蔽物是三层；把物件和地面画在同一层会导致"人被楼盖住/楼被人盖住"随机出现。
用 `Tilemap` 或三个排序层（`GroundLayer` / `ObjectLayer` / `OverlayLayer`）。

## #11 ★ 驱动活编辑器时：**必须**先打开 `Application.runInBackground`

**实测（2026-09-17 首次进 Play）**：编辑器窗口失焦时，Unity 的 Play 循环被节流到**约 14 秒一帧**
（探针读到 `unscaledDeltaTime = 13.75`、`deltaTime = 0.02`）。
后果：引擎 `Timer` 用 `Time.deltaTime` 推进 ⇒ **定时器几乎不推进**，
`SceneModule.Load` 的"进度 ≥ 0.9 才放行"门控永不满足 ⇒ **读条永久卡在 Loading**，而**日志零报错**。
极易被误判成"场景加载有 bug"（我为此绕了两轮）。

**对策（每次驱动前先跑一次）**：
```csharp
UnityEngine.Application.runInBackground = true;
UnityEngine.QualitySettings.vSyncCount = 0;
```
探针一律放 `.ai-tmp/drivers/`、一次性自检放 `.ai-tmp/test/`；⛔ 不许在 `client/_dev/`、`_assets_src/`、`_assets_tmp/` 下新增文件 —— `tools/verify.ps1` 的 `stray-temp-files` 把这三处下的 `.cs` 判为散落产物（白名单里只有 1 个历史条目）。

**附带两条同源经验（同样已成坑）**：

- **截图必须等 UI 变化后 ≥ 6 秒**再 `capture_game_view`，否则拿到**上一帧的过期图**
  （实测两张截图字节数完全相同 = 完全没重绘）。
- **判定"面板没关/对象还在"不许用 `FindObjectsByType` 列表**：
  `UIManager.Close` 走 `Object.Destroy`（帧末才 flush），失焦停帧时会给出**假的"面板叠加/残留"**。
  真值用 `Game.UI.IsOpen<T>()`（立刻更新）。
- **输入注入**：见 engine skill `reference/pipeline-and-unity-cli.md`【P-4】。
  本项目可用的配方：只往**现有** `Keyboard.current` / `Mouse.current` 投事件，
  **不增删设备、不手动 `Update()`**；另注意 `RemoveDevice` + `AddDevice` 会让**同一 Play 会话里只有第一次注入有效**。

## #12 素材下载器：分块重试必须**回滚**已写字节

素材下载器的 chunk 循环里，`$fs.Write` 写在 try 内、失败重试时**没有把文件截断回 `$offset`** ⇒
重试会**重复追加**，产出比预期更大的损坏文件（实测：`.part` 2241641414 字节 > 预期 2145506049），
最终 `SIZE_MISMATCH` 且 zip 不可读。
**修法**：每次重试前 `$fs.SetLength($offset)` 并把 `Position` 复位；或改成"每块写独立临时文件再拼接"。

---

## 本项目依赖的引擎修复（E 编号）

| E | 内容 | 本项目受影响处 |
|---|---|---|
| **E-build-01**（片 2 起依赖） | 引擎 `Tests/Editor/**` **编译不过**（`NullInputBackend.GetAxis("Horizontal")` 少了第二个实参 `raw`）⇒ 引擎程序集编译失败 ⇒ **下游工程全都进不了 Play** | **本项目直接受影响**：本工程 `client/` 依赖引擎包，引擎 `Tests/Editor` 编译失败会让整个 Play 起不来（`unity command editor_play` 进 Safe Mode）。修法 = `backend.GetAxis("Horizontal", false)`（引擎侧 `Runtime/Presentation/Input.cs:652` 的签名是 `GetAxis(string axis, bool raw)`，**没有默认值**）。出处：引擎仓库 `修复记录.md` §「E-build-01 · 引擎 `Tests/Editor` 编译不过 ⇒ 下游工程全都进不了 Play」（:533-539）。片 8 复核：本工程 `unity command recompile` = `completed / failed=false / errors=[]`，`editor_play` 正常进 Play ⇒ 该修复在位 |

| **E-build-02**（片 17 起依赖，2026-09-19） | 引擎 `Runtime/Presentation/Sound.cs` 的 **SFX 音源池硬编码 8 个** ⇒ 池满时 `GetAvailableSource()` 走"丢弃本次播放 + 告警一次"分支，**音效静默丢失** | **本项目直接受影响**：`clover-project-diablo2` 战斗实测（验收表 **BL-10**）出现 `[Warn] [Sound] 音效池（8 个音源）已全部占用，本次播放被丢弃` —— 一次命中会同时触发 挥砍 / 命中 / 受击 / 死亡 / 掉落 若干音效，AOE / 群体战下 8 个源不够。**修法** = 新增 `private const int SfxPoolSize = 32;` 并把它用在构造循环（多挂 24 个**空载** `AudioSource`，无行为副作用、纯增益）。**出处**：引擎仓库 `修复记录.md`；改动点 `Runtime/Presentation/Sound.cs`（常量 + `for (var i = 0; i < SfxPoolSize; i++)`） |

| **E-build-03**（收尾轮起依赖，2026-09-19） | 引擎 `Runtime/Presentation/Scene.cs` 的 `SceneModule.Load` 每次 Load 都先 `Stop` 上一次的进度轮询定时器，而被停掉的那个 `AsyncOperation` 的 `allowSceneActivation` 仍是 `false` ⇒ **它永远不会激活**（僵尸场景：`isLoaded=false / rootCount=0`）⇒ 累积后编辑器内后续 `LoadSceneAsync` 的 **`progress` 恒 `0.000`**、主菜单/创角屏全都不开 | **本项目直接受影响**：本项目"进图 / 回主菜单"全走 `Game.Scene.Load` —— 收尾轮实测 `Loading scene: Menu` 永不完成、`SceneManager.sceneCount=4`（Boot + 3 个僵尸 Menu）、`AppFlow.EnsureMenuScene` 的 `onReady` 永不触发。**修法** = 停旧轮询**之前**先放行旧 op（`pending.allowSceneActivation = true`）+ 一条 Warn（非预期分支）；新增 `_progressOp` / `_progressScene` 字段并在轮询与 `completed` 里按 `_progressOp == op` 自判归属（正常路径逐行未改）。**出处**：引擎仓库 `修复记录.md` §「E-build-03」；⚠️ 该修复的**实机回归待用户重启编辑器后**做（见本项目 `策划/验收表.md`「收尾轮」节） |

| **E-core-17**（pass5 起依赖，2026-09-19；**补** 2026-09-20 07:24） | 引擎 `Runtime/Presentation/Sound.cs` 原先**没有起播闸门** + 缺失告警每次调用都裸打 ⇒ 高频音效把日志刷爆、一帧几十个音效把音源池占满（互相顶掉）。**修法** = 四处 `clip == null` 统一走 `LogThrottle.WarnOnce`；新增 `MaxPlaysPerFrame` / `MaxConcurrentPerClip` 两个闸门属性；池满告警由裸 `Warn` 换 `LogThrottle.WarnOnce("Sound","pool.exhausted",…)`（**池逻辑与"只报一次"语义逐字未变**） | **本项目受影响处**：战斗一次命中会同时触发 挥砍/命中/受击/死亡/掉落 若干音效（验收表 **BL-10** 的 `[Warn] 音效池…已全部占用` 即此路径）；`SfxPoolSize=32` 与"只报一次"共同保证战斗日志不被刷爆。出处 = 引擎 `修复记录.md` §E-core-17（含 2026-09-20 的「补」两条） |

| **E-core-19**（pass5 起依赖，2026-09-19） | 引擎 `Runtime/Core/IsoLayout.cs` 的「格增量 → 8 方向」表**整体逆时针偏 45°**（`DirectionTo` / `DirectionDelta` 两个方法**必须整表一起改**，只改一边会让互逆性静默失效）⇒ 人物/怪物的**贴图朝向看着偏一档** | **本项目直接影响**：本项目 `Module/View/ViewModule.cs`（NPC 转身、受击反向位移）、`Module/Monster/MonsterRuntime.cs`、`Module/Player/PlayerMotor.cs` 都经 `Core/Iso.cs` 薄门面调用它 ⇒ pass5 的四方向取证 **G10~G13**（`[P41] DIR-SHOT … want=S dir=S dirOk=1 key=D2/Chars/amazon/run_s_1 keyOk=1 spriteOk=1`，四方向全 OK）验证的就是修后的表。项目侧 `Core/Iso.cs` 的 `<summary>` 已同步（**只改注释、代码未动**）。⚠️ 素材侧方向槽位是**另一处**独立缺陷（`tools/d2codec/export_chars.py`），见引擎 `修复记录.md` §E-core-19「已知边界」 |

细节见引擎仓库 `修复记录.md`。

---

## 未决问题

| 问题 | 待谁定 | 时间 |
|---|---|---|
| 官方素材（角色/怪物/地形/音效）下载中，到位前角色/怪物/地形用纯色占位 | 主 agent（后台下载） | 2026-09-16 |
| 中文文本（NPC 对话/物品名/技能名）来源：官方数据表 string 字段能否抽全 | 主 agent | 2026-09-16 |
