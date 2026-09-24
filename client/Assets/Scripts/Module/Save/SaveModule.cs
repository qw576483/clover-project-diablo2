// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Save/SaveModule.cs
// 存档门面实现：**多角色存档，一角色一文件**。
//   · 角色档：`<Game.Config.SettingDir>/saves/<角色名>.json`
//     —— 写盘 / 读盘 / 枚举 / 损坏留档**全部走引擎** `CloverEngine.FileSlotStore`
//        （原子写 `*.tmp` → `File.Replace` + 损坏留档 + 枚举；下沉片 A6，
//         引擎侧见 `clover-client-unity-engine/Runtime/Data/FileSlotStore.cs`）。
//   · 角色**创建先后**索引：仍存 `Game.Setting` 的 `char/index`（`GameConst.SaveIndexKey`）
//     —— 这是**业务语义**、不是通用能力：引擎 `FileSlotStore.List()` 是**字典序**不是插入序，
//        其文件头「与 `Setting` 的差异」已明确裁决「要创建先后就自己维护索引键」；
//        索引顺序 = 选角屏卡片顺序，改掉就是可见的行为变化。
//
// ⚠️ 旧档兼容（A6 迁移）：A6 之前的存档存在 `Game.Setting` 的 `char/{角色名}` 键里
//    （`GameConst.SaveKeyPrefix`）。读取时**回退旧键**并在槽位里落一份（懒迁移，**写成功才删旧键**）；
//    删除角色时旧键与索引一并清掉，避免"槽位档删了之后旧档复活"。
//
// ⚠️ `Game.Setting.Set/Get` 按 key 存的是 `object` ⇒ **存字符串 JSON 而不是对象本身**
//    （否则反序列化 `object` 会丢类型，见契约注释）。JSON 编解码见 `SaveJson.cs`（本项目新增）。
//
// 与 agent-05 `CharRoster` 的对接（**逐条对齐它实际调用的成员**，Flow 侧不改一行代码）：
//   `HasAny` / `ListAll()` / `List(name)` / `Exists(name)` / `Save(CharacterSave)` / `Delete(name)`
//   —— 见 `Module/Flow/CharRoster.cs:40-119`。
//
// ⚠️ 读档时谁把数据灌回各模块：`AppFlow` 只在进 Stage 时自己调 `ctx.Player.LoadFrom(save)`
//    （`AppFlow.cs:431`），**从不调用** `ApplyToModules`。所以本模块在 `Load(name)` 成功时
//    **顺手把 Item / Quest / Npc / Skill 装回去**（Player 留给 Flow 装，重复装也幂等）；
//    `ApplyToModules(data)` 仍是"一次装全部（含 Player）"的公开入口，供其它调用点使用。
//
// 版本：`CharacterSave.version`；读档版本不符 ⇒ **降级处理 + Warn + Info**（缺字段取默认值，绝不让读档崩）。
//
// ★ R7（本片 Q，缺陷 source = `.ai-tmp/test/audit-C-logic-num.md` §2 红行 R7）：
//   修前 `Load()` 的**两条路径都返回 null**（`:238-242` 档不存在 与 `:244-250` 解析失败），
//   而 `LastError` 的唯一消费者是 `AppFlow` 的**保存**失败分支 ⇒ 玩家的**读档失败完全静默**
//   （损坏档在选角屏表现为"角色没了"，无任何提示）。
//   修后：三情况**可判别**，判别位 = 契约里已有的 `LastError`（⛔ 不新增/不删除任何契约成员）——
//     · 档不存在（正常：新玩家 / 空槽）⇒ 返回 null 且 **`LastError == ""`** ⇒ 调用方不报错、走新建流程；
//     · 档存在但解析失败 / 损坏 ⇒ 返回 null 且 **`LastError != ""`**（含档名 + 原因）
//       + `Events.LoadDone` 发 **null**（`Core/Events.cs:368` 的既定语义「null = 失败」，修前 0 订阅者）
//       ⇒ `AppFlow.OnLoadDone` 复用 `UI/D2ConfirmPanel` 给出**用户可见**反馈；
//     · 版本 / 字段缺失但可兼容 ⇒ 照 `SaveJson` 既有口径读入 + 一条 Info（见 Load 内注释）。
//   ⚠️ **不在读档失败路径上替玩家删档** —— 损坏文件原样留在槽位（引擎 `FileSlotStore` 另有 `.corrupt` 留档），
//     删档只能由玩家在选角屏显式点 DELETE。
//   🚨 **本片实测发现：R7 还有更深的第 2 层根因**（穷举审计片没写到）——
//     引擎 `FileSlotStore.Read` 对**坏内容**也返回 `null`（`Runtime/Data/FileSlotStore.cs:250-255`：
//     留档 `.corrupt` 后"本次按「没有这个槽」处理"）⇒ 项目侧**只看返回值**根本分不出
//     "文件不在" 与 "内容坏了"，旧代码因此把损坏档一路当成"没有这个档"。
//     ⇒ 判据必须用引擎暴露的 `LastCorruptPath`（**逐字比对本槽应有的留档路径**，
//       非空即算会让批量读互相覆盖，见 `IsThisSlotCorruptArchive`）。
//
// ⚠️ 槽位键 = 角色名 ⇒ 角色名必须能当文件名（⛔ 不能含 `/` `\` `:` `*` `?` `"` `<` `>` `|` 等）。
//    由**创角屏**保证：`CharCreatePanel.OnNameChar` 只收字母 / 数字 / 中文 / `_` / `-`
//    （`IsNameCharAllowed`，非法字符拒绝 + 只报一次 Warn），提交前 `OnConfirm` 再用
//    `FirstDisallowedNameChar` 兜一道（覆盖"默认名被配成非法字符"这类绕过键入的路径）
//    ⇒ 正常流程下 `Save` 不会因名字失败。
//    万一仍有非法名（绕过 UI 的调用点）⇒ `Save` **失败并留下可定位错误**（引擎 `FileSlotStore` 的
//    key 校验报"槽位 key 非法（…）" + 本模块 `Fail` 记 `LastError` ⇒ Flow 侧 Toast「保存失败，见日志」），
//    **不是静默丢档**。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using CloverEngine;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;
// 别名：同时 `using System;` 会让 `AppContext` 撞上 `System.AppContext`（CS0104）
using AppContext = Diablo2.App.AppContext;

namespace Diablo2.Module.Save
{
    /// <summary>存档门面实现（多角色；档在引擎 `FileSlotStore` 槽位里，创建先后索引在 `Game.Setting`）。</summary>
    internal sealed class SaveModule : ISaveModule
    {
        /// <summary>槽位目录名（相对 `Game.Config.SettingDir`）：`&lt;SettingDir&gt;/saves/`。</summary>
        private const string SaveDirName = "saves";

        /// <summary>
        /// 槽位文件扩展名（显式写出来、不再靠引擎默认值）：★ R7 要**反推**引擎留档的坏档路径
        /// （`Path.Combine(Dir, key + ext) + ".corrupt"`，见引擎 `FileSlotStore.cs:459-474`），
        /// 硬编码在别处会与构造参数脱节。值 = 引擎默认 `.json`（`FileSlotStore.cs:83`），行为零变化。
        /// </summary>
        private const string SlotExtension = ".json";

        private string _lastError = "";

        /// <summary>
        /// ★ R7：最近一次 `ReadRaw` 是否命中**内容损坏**的槽位（而不是"文件不存在"）。
        /// <para>为什么必须单独记：引擎 `FileSlotStore.Read` 对坏内容**也返回 `null`**
        /// （`Runtime/Data/FileSlotStore.cs:250-255`：留档 `.corrupt` 后"按没有这个槽处理"）
        /// ⇒ 只看返回值**无法**区分"档不存在"与"档损坏"，这正是 R7 的更深一层根因。
        /// 引擎已经把事实暴露出来了：<see cref="FileSlotStore.LastCorruptPath"/> 指向刚留档的坏文件。</para>
        /// </summary>
        private bool _lastReadFoundCorrupt;

        /// <summary>
        /// 角色存档槽位存储（一角色一文件；实现在引擎 <see cref="FileSlotStore"/>）。
        /// 懒创建：第一次访问时才拼目录并探测可用性（见 <c>Store</c> 属性）。
        /// </summary>
        private FileSlotStore _store;

        /// <summary>本次会话"进入游戏"的时间基（算 playedSeconds 用；不用 Unity Time，离线可跑）。</summary>
        private long _sessionBaseTicks;

        /// <summary>本次会话开始时的累计游戏秒数（从存档里读来的）。</summary>
        private float _sessionBasePlayed;

        /// <summary>最近一次失败原因（成功时为空串）。</summary>
        public string LastError
        {
            get { return _lastError; }
        }

        /// <summary>存档是否就绪（`Game.Setting` 已接入 ⇒ 引擎已启动、`Game.Config.SettingDir` 可用）。</summary>
        public bool Ready
        {
            get { return Game.Setting != null; }
        }

        /// <summary>
        /// 槽位存储（懒创建）。目录口径与 `Game.Setting` **完全一致**：引擎 `Setting` 的兜底目录是
        /// `"setting"`（`Setting.cs:75`），且不解析为绝对路径 ⇒ 这里同样用相对目录，相对路径口径不变。
        /// <para>⚠️ 构造**不抛异常**（`FileSlotStore` 的契约：目录不可用 ⇒ 退化为"写失败 + 可定位错误串"）；
        /// 这里只兜 `Path.Combine` 的极端入参（含 `'\0'` 一类），兜底同样退化为不可用而不是抛。</para>
        /// </summary>
        private FileSlotStore Store
        {
            get
            {
                if (_store != null) return _store;

                var root = Game.Config != null && !string.IsNullOrEmpty(Game.Config.SettingDir)
                    ? Game.Config.SettingDir
                    : "setting";
                try
                {
                    _store = new FileSlotStore(System.IO.Path.Combine(root, SaveDirName), SlotExtension);
                }
                catch (Exception ex)
                {
                    Log.Error("Save", $"存档槽位目录初始化失败（root={root}）：{ex.GetType().Name}: {ex.Message}");
                    _store = new FileSlotStore("");     // 不可用 ⇒ 后续走失败路径（不静默、不伪装成功）
                }
                return _store;
            }
        }

        // ── 写 ─────────────────────────────────────────────────────────────────

        /// <summary>把当前游戏状态存成角色存档（名字取 `IPlayerModule.Name`）。</summary>
        public bool Save()
        {
            if (!Ready)
            {
                return FailSave("存档失败：`Game.Setting` 未接入（引擎未启动？）");
            }

            var p = Player;
            if (p == null)
            {
                return FailSave("存档失败：`IPlayerModule` 未接入 ⇒ 拿不到角色名/属性（无法构成存档）");
            }
            if (string.IsNullOrEmpty(p.Name))
            {
                return FailSave("存档失败：当前角色名为空（还没创角？）");
            }

            var data = new CharacterSave
            {
                version = GameConst.SaveVersion,
                name = p.Name,
                cls = p.Class,
            };

            p.WriteTo(data);                                     // 属性 / 等级 / 经验 / 金币 / 位置
            Item?.WriteTo(data);                                 // 背包 / 装备 / 腰带
            Quest?.WriteTo(data);                                // 任务状态
            WriteSkills(data);                                   // 技能（`ISkillModule` 没有 WriteTo ⇒ 反查）

            var map = Map;
            if (map != null && map.IsGenerated)
            {
                data.mapSeed = map.Seed;        // 本局地图 seed（读档要复现同一张图）
                // ★ save-areaid 修复（2026-09-24）：**所在区域也由地图负责**，与 mapSeed 同源同分支。
                //   缺陷（前片 `rebuild-entries` 实测 + 本片 49/49 存档全量反证）：本方法在 :167 **新造**
                //   一个 `CharacterSave`，从 Live 模块逐字段收集；而 `areaId` **全仓没有写者** ——
                //   `PlayerModule.WriteTo`（:471-473 注释明文「mapSeed / areaId … 这里不碰」）、
                //   `ItemModule.WriteTo` / `QuestModule.WriteTo` / `WriteSkills` 都不写它
                //   ⇒ 落盘 `"areaId":0`（= 默认值），读档 `GoStage(ToArea(save.areaId))` 于是**一律回营地**。
                //   注意：`AppFlow._selected.areaId`（`EnterArea`/`GoStage` 会写）**不是**落盘对象 ——
                //   `AppFlow.SaveCurrentCharacter` 调的是**无参** `Save()`，它只认这里的 Live 收集。
                //   判据同源：`AppFlow.EnterArea` 的"过门闸门"用的就是 `IMapModule.Area`（玩家脚下那张图）。
                data.areaId = (int)map.Area;
            }
            else
            {
                // 非预期分支（进图前/地图不可用）：⛔ 不静默 —— 留痕说明 areaId 取了默认值（= 营地），
                // 免得下次又变成"读档回营地"这类无声错。
                Log.Warn("Save", $"[Save] IMapModule 未接入或地图未生成（map={(map == null ? "null" : "IsGenerated=false")}）"
                    + $" ⇒ 本次存档的 `areaId` 只能取默认值 {data.areaId}（= {AreaId.Town}），读档会落回营地");
            }

            // ★ save-progress（2026-09-24）：**传送点已激活列表**（`App/AppWaypoint.Visited`）与
            //   **小地图已探索格**（渲染层 `MapView._explored` 经 `IMapModule.ExploredCells`）的持有者
            //   不在模块侧 ⇒ 与上面 `mapSeed`/`areaId` **同一个收集阶段**里，由 App 层往 `data` 里填
            //   （`Events.SaveCollect`，收方 = `App/AppProgress.cs`；⛔ 不另开一条收集路径）。
            //   修前这两项**恒为默认值**（空集合）⇒ 读档后传送点全变未激活、automap 全空
            //   （前片 `save-areaid` 的《同族穷举表》第 9/10 行登记的缺口）。
            CollectAppProgress(data);

            data.playedSeconds = _sessionBasePlayed + ElapsedSinceBase();

            Log.Info("Save", $"[Save] 收集完成：{data.name} 职业={data.cls} 等级={data.level} 金币={data.gold} "
                + $"背包锚点={CountAnchors(data)} 装备={data.equip.Count} 技能={data.skillIds.Count} "
                + $"区域={data.areaId} 位置=({data.gridX},{data.gridY}) seed={data.mapSeed} "
                + $"传送点={data.visitedWaypoints.Count} 探索区={data.exploredByArea.Count} "
                + $"{DescribeExplored(data)}");
            return Save(data);
        }

        /// <summary>存一份指定数据（创角时先把角色写进去）。</summary>
        public bool Save(CharacterSave data)
        {
            if (data == null)
            {
                return FailSave("存档失败：CharacterSave 为 null");
            }
            if (string.IsNullOrEmpty(data.name))
            {
                return FailSave("存档失败：角色名为空（空名不合法）");
            }
            if (!Ready)
            {
                return FailSave("存档失败：`Game.Setting` 未接入（引擎未启动？）");
            }

            data.version = GameConst.SaveVersion;
            data.savedAtTicks = DateTime.UtcNow.Ticks;

            var json = SaveJson.Write(data);
            var name = data.name;                       // 槽位键 = 角色名（一角色一文件）
            string err;
            if (!Store.Write(name, json, out err))
            {
                return FailSave($"存档失败（写槽位「{name}」）：{err}");
            }

            try
            {
                // 迁移期遗留：旧键 `char/{名}` 若还在就清掉（否则"槽位档被删后旧档复活"）
                Game.Setting.Delete(KeyOf(name));
                AddToIndex(name);                       // 创建先后索引（选角屏顺序依赖它，见文件头）
                Game.Setting.Save();
            }
            catch (Exception ex)
            {
                // 槽位已写成功 ⇒ 存档本身没丢：索引/清理失败只告警，**不把这次保存判成失败**
                Log.Warn("Save", $"存档已写入槽位，但索引更新失败（{ex.GetType().Name}: {ex.Message}）"
                    + "⇒ 选角列表可能不含本角色，但存档文件在");
            }

            // 记时基：本次会话的累计时长从这份存档继续算
            _sessionBaseTicks = data.savedAtTicks;
            _sessionBasePlayed = data.playedSeconds;

            _lastError = "";
            Log.Info("Save", $"[Save] 已落盘「{data.name}」：槽位={Store.Dir} 大小={json.Length} 字节 "
                + $"版本={data.version} 等级={data.level} 任务={DescribeQuests(data)}");

            // ★ T0FIX-D：**存档成功**的唯一出口 —— 发 `Events.SaveDone(true)`。
            //   为什么是"接线"而不是"删事件"：`Events.SaveDone`（`Core/Events.cs:316`，参数 = bool 是否成功）
            //   的参数语义明确、且 `Core/` 是冻结层（删它要改 Core）⇒ 按验收表规则 7「定义了但没人用」
            //   的本意补**生产者 + 消费者**。参数口径 = 「本次存档尝试是否成功」（失败也发，参数 false）。
            SignalSaveDone(true);
            return true;
        }

        // ── 读 ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// 读某个角色的存档。
        /// <para>返回 null 时有**两种含义**，由 <see cref="LastError"/> 区分（★ R7，见文件头）：
        /// <c>LastError == ""</c> ⇒ 档不存在（正常）；<c>LastError != ""</c> ⇒ 真的读失败（损坏 / 目录不可用 / 未接入）。
        /// 后者同时把 `Events.LoadDone` 以 **null** 发出（既定语义「null = 失败」）。</para>
        /// </summary>
        public CharacterSave Load(string name)
        {
            // ★ R7：入口先清 —— 否则"上一次失败"的 LastError 会残留到本次"档不存在"这条**正常**路径上，
            //   让调用方把一个空槽误判成"损坏档"并弹提示。
            _lastError = "";

            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("Save", "Load：角色名为空 ⇒ 忽略");
                return null;
            }
            if (!Ready)
            {
                Fail("读档失败：`Game.Setting` 未接入（引擎未启动？）");
                return null;
            }

            string json;
            try
            {
                json = ReadRaw(name);           // 槽位 → 旧键回退（顺带懒迁移，见 ReadRaw）
            }
            catch (Exception ex)
            {
                Fail($"读档失败（读「{name}」）：{ex.GetType().Name}: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(json))
            {
                // ★ R7 情况 ②（更深一层）：槽位**文件在、内容坏了** ⇒ 引擎 `FileSlotStore.Read` 也返回 null
                //   （它留档 `.corrupt` 后按"没有这个槽"处理，`FileSlotStore.cs:250-255`）⇒ 只看返回值会把
                //   "损坏"误判成"档不存在"。判据 = 刚留档的那份坏文件**恰好是本槽**的。
                if (_lastReadFoundCorrupt)
                {
                    return FailCorrupt(name,
                        $"—— 引擎 `FileSlotStore` 判定槽位内容不是合法 JSON，已留档副本 {Store.LastCorruptPath}");
                }

                // ★ R7 情况 ①：档不存在 = **正常情形**（新玩家 / 空槽）⇒ 不报错、LastError 保持空串，
                //   调用方据此走"新建流程"，⛔ 不起报错界面。
                Log.Info("Save", $"读档：没有角色「{name}」的存档（槽位 {Store.Dir} 与旧键 {KeyOf(name)} 均为空）"
                    + "⇒ 正常情形（新玩家 / 空槽），不报错");
                return null;
            }

            string err;
            var data = SaveJson.TryParse(json, out err);
            if (data == null)
            {
                // ★ R7 情况 ②：档在、但解析失败 = **损坏**（例如从旧键 `char/{名}` 回退来的内容坏了）
                //   ⇒ 必须与"档不存在"可区分（LastError 非空）并让用户可见。
                return FailCorrupt(name, $"—— {err}");
            }

            // ★ R7 情况 ③：版本不符 ⇒ **降级处理**（缺字段已在 SaveJson 里取默认值），绝不抛异常，
            //   走兼容路径并**留一条 Info**（按任务书 §2.3；Warn 也保留）。
            //
            // ★★ u52cur（2026-09-24）：**旧档不再在这里被"修好"** —— 这是 E60 迁移在真实链路上
            //   不可达的根因（`charstat` 实机证据：老档仍 `耐力 20/84`）。
            //   链路：`SaveModule.Load`（此处）→ 返回的 `data` → `AppFlow` 取到手 → `PlayerModule.LoadFrom(data)`。
            //   旧版这里写 `data.version = GameConst.SaveVersion;` ⇒ 等 `PlayerModule.LoadFrom` 拿到时
            //   `save.version` 已是**当前值** ⇒ 它的判据 `save.version < GameConst.SaveVersion`
            //   **恒 false** ⇒ 迁移分支是**死代码**（单元级夹具直接构造 `CharacterSave`，跳过本模块，
            //   所以当时是绿的 —— "单元级绿 / 链路级红"）。
            //   ⇒ 本模块**只报不改**：保留档里的原版本号，让下游能判"这是旧口径档"。
            //   ⛔ 不会因此让档永远停在旧版本：**回写磁盘时一律写当前版本** ——
            //      `SaveModule.Save()` 的 `data.version = GameConst.SaveVersion`（本文件 `:236`）
            //      与 `PlayerModule.WriteTo` 的同名赋值（`Module/Player/PlayerModule.cs:483`）两处都在。
            //   ⛔ 也**不**把"存了就沿用（钳上限）"改成"一律补满"（活档 `SAArea1.json` `life=34` 是中局
            //      受伤档，一律补满会静默治成满血；判据见 `itemcheck` §11c / `playercheck` §9c）。
            var fileVersion = data.version;
            if (fileVersion != GameConst.SaveVersion)
            {
                if (fileVersion > GameConst.SaveVersion)
                {
                    // 比当前还新（旧客户端读新档）：尽力读，但**别回写** —— 回写会把新字段丢掉。
                    Log.Warn("Save", $"读档：「{name}」的存档版本 {fileVersion} **比当前 {GameConst.SaveVersion} 还新** "
                        + "⇒ 按兼容路径尽力读（缺字段取默认值），请勿在此基础上回写覆盖原档");
                    Log.Info("Save", $"读档：「{name}」走**兼容路径**（版本 {fileVersion} > 当前 {GameConst.SaveVersion}；"
                        + $"`data.version` 保持 {fileVersion} 不改写；存档里缺失的字段由 SaveJson.TryParse 按默认值补齐）");
                }
                else
                {
                    Log.Warn("Save", $"读档：「{name}」的存档版本 {fileVersion} < 当前 {GameConst.SaveVersion} "
                        + $"⇒ 走旧档兼容路径（缺字段取默认值、语义按当前版本继续读）；档内版本号**保持 {fileVersion} 不改写**"
                        + "（下游 `PlayerModule.LoadFrom` 用它判「旧档资源迁移」）");
                    Log.Info("Save", $"读档：「{name}」走**兼容路径**（版本 {fileVersion}（不改写）→ 语义按当前 "
                        + $"{GameConst.SaveVersion}；存档里缺失的字段由 SaveJson.TryParse 按默认值补齐；"
                        + $"**下一次保存会把它写成 {GameConst.SaveVersion}**）");
                }
            }

            if (string.IsNullOrEmpty(data.name)) data.name = name;

            _sessionBaseTicks = DateTime.UtcNow.Ticks;
            _sessionBasePlayed = data.playedSeconds;
            _lastError = "";

            Log.Info("Save", $"[Save] 读档成功：「{data.name}」等级={data.level} 金币={data.gold} "
                + $"位置=({data.gridX},{data.gridY}) area={data.areaId} seed={data.mapSeed} "
                + $"背包锚点={CountAnchors(data)} 装备={data.equip.Count} 技能={data.skillIds.Count} "
                + $"任务={DescribeQuests(data)} 大小={json.Length} 字节");

            // Flow 只会自己装 Player（AppFlow.cs:431）⇒ 这里把其余模块顺手装回去（幂等）
            ApplyOtherModules(data);

            Game.Event?.Emit(Events.LoadDone, data);
            return data;
        }

        /// <summary>
        /// 读存档（不抛异常）。返回 false 表示"没能拿到存档"，**两种含义由 <see cref="LastError"/> 区分**：
        /// <list type="bullet">
        /// <item><c>LastError == ""</c> ⇒ **档不存在**（正常：新玩家 / 空槽）⇒ 调用方走新建流程，⛔ 不报错；</item>
        /// <item><c>LastError != ""</c> ⇒ **读失败**（损坏 / 槽位目录不可用 / `Game.Setting` 未接入）
        /// ⇒ 调用方**必须**给用户可见反馈（本项目在 `AppFlow.OnLoadDone` 里复用 `UI/D2ConfirmPanel`）。</item>
        /// </list>
        /// <para>★ R7：本方法此前**全仓 0 调用点**（只有契约声明 `Module/Contracts.cs:1687` + 本实现）。
        /// 修法 = **接上它**（`AppFlow.OnCharSelectRequest` 改走本方法拿"失败原因"判别位），
        /// ⛔ 不删 —— 契约成员（`ISaveModule`）不属本片可改范围。</para>
        /// </summary>
        public bool TryLoad(string name, out CharacterSave data)
        {
            data = Load(name);
            return data != null;
        }

        // ── 删除 / 列举 ────────────────────────────────────────────────────────

        /// <summary>删除某个角色存档。</summary>
        public bool Delete(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("Save", "Delete：角色名为空 ⇒ 忽略");
                return false;
            }
            if (!Ready)
            {
                Fail("删除存档失败：`Game.Setting` 未接入（引擎未启动？）");
                return false;
            }
            if (!Exists(name))
            {
                Log.Warn("Save", $"Delete：没有角色「{name}」的存档 ⇒ 无事可做");
                return false;
            }

            try
            {
                Store.Delete(name);                     // 槽位档（不存在 ⇒ 返回 false，但上面 Exists 已保证有一个）
                Game.Setting.Delete(KeyOf(name));       // 迁移期遗留的旧键（不清 ⟹ 删掉的角色下次会"复活"）
                var names = ReadIndex();
                if (names.Remove(name)) WriteIndex(names);
                Game.Setting.Save();
            }
            catch (Exception ex)
            {
                return Fail($"删除存档失败（{name}）：{ex.GetType().Name}: {ex.Message}");
            }

            _lastError = "";
            Log.Info("Save", $"已删除存档「{name}」：剩余 {List().Count} 个角色");
            return true;
        }

        /// <summary>全部存档角色名（主菜单「继续」与选角屏用）。</summary>
        public List<string> List()
        {
            var res = new List<string>();
            if (!Ready) return res;

            var names = ReadIndex();
            for (var i = 0; i < names.Count; i++)
            {
                if (Exists(names[i])) res.Add(names[i]);
                else Log.Warn("Save", $"索引里的角色「{names[i]}」没有对应存档（槽位与旧键都没有）⇒ 已忽略（索引待清理）");
            }
            return res;
        }

        /// <summary>全部存档（带等级/职业，选角屏卡片用）。</summary>
        public List<CharacterSave> ListAll()
        {
            var res = new List<CharacterSave>();
            var names = List();
            for (var i = 0; i < names.Count; i++)
            {
                var data = Load(names[i]);
                if (data != null) res.Add(data);
            }
            return res;
        }

        /// <summary>
        /// 是否存在该角色的存档。判据与改前同口径（"那份存档文本非空"）：
        /// ① 槽位文件在不在（**只看文件、不解析内容** ⇒ 坏内容的槽也算存在，与旧实现"键非空"一致）；
        /// ② 回退旧键 `char/{名}`（A6 迁移期；这里**不写盘**，懒迁移只在真正读取时发生）。
        /// </summary>
        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name) || !Ready) return false;
            try
            {
                if (Store.Exists(name)) return true;
                return !string.IsNullOrEmpty(Game.Setting.Get<string>(KeyOf(name), ""));
            }
            catch (Exception ex)
            {
                Log.Warn("Save", $"Exists(\"{name}\") 查询失败：{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>是否存在任意存档（主菜单「继续」按钮可用性）。</summary>
        public bool HasAny
        {
            get { return List().Count > 0; }
        }

        // ── 装配回模块 ─────────────────────────────────────────────────────────

        /// <summary>把存档数据装配回各模块（Player / Skill / Item / Quest / Npc）。</summary>
        public void ApplyToModules(CharacterSave data)
        {
            if (data == null)
            {
                Log.Warn("Save", "ApplyToModules 收到 null 存档 ⇒ 忽略");
                return;
            }

            var p = Player;
            if (p != null) p.LoadFrom(data);
            else Log.Warn("Save", "ApplyToModules：`IPlayerModule` 未接入 ⇒ 角色属性未装配");

            ApplyOtherModules(data);
            Log.Info("Save", $"[Save] 存档已装配到各模块：{data.name}（Player/Item/Quest/Npc/Skill）");
        }

        // ── 内部 ───────────────────────────────────────────────────────────────

        /// <summary>旧档键（A6 之前的存放位置）：`char/{角色名}`。**仅用于懒迁移与清理**，不再是权威存储。</summary>
        private static string KeyOf(string name)
        {
            return GameConst.SaveKeyPrefix + name;
        }

        /// <summary>
        /// 读槽位文本；槽位没有时**回退旧键**并把内容搬进槽位（懒迁移，幂等）。
        /// <para>
        /// 迁移的三条硬规矩（防"迁移把老档弄丢"）：
        /// ① **写成功才删旧键** —— 槽位写失败就保持原样从旧键读（本次照常玩，下次再试）；
        /// ② 写成功后删旧键并 `Setting.Save()`，否则"槽位档被删掉"之后旧档会**复活**；
        /// ③ 任何异常都不外抛（读不到只返回 <c>null</c>，由调用方按"没有这个档"处理）。
        /// </para>
        /// </summary>
        private string ReadRaw(string name)
        {
            var store = Store;
            _lastReadFoundCorrupt = false;          // 每次读先复位（上一次的事实不得残留）
            var text = store.Read(name);
            if (!string.IsNullOrEmpty(text)) return text;

            // ★ R7：`Read` 返回 null 有**两种**成因 —— "文件不在" 与 "内容坏了（引擎已留档）"。
            //   后者可从引擎的 `LastCorruptPath` 反查出来（见字段注释）。
            var slotCorrupt = IsThisSlotCorruptArchive(store, name);

            // 槽位没有 ⇒ 看 A6 之前的老档（`char/{名}` 键）还在不在
            var legacyKey = KeyOf(name);
            string legacy;
            try
            {
                legacy = Game.Setting.Get<string>(legacyKey, "");
            }
            catch (Exception ex)
            {
                Log.Warn("Save", $"回退读旧键失败（{legacyKey}）：{ex.GetType().Name}: {ex.Message}");
                return null;
            }

            if (string.IsNullOrEmpty(legacy))
            {
                // ★ R7：旧键也没有 ⇒ 若槽位是**坏了**（而非"不在"），把事实交给 `Load` 报「损坏」。
                _lastReadFoundCorrupt = slotCorrupt;
                return null;
            }

            if (slotCorrupt)
            {
                // 槽位档坏了、但旧键里还有一份（A6 迁移期的老副本）：**照旧恢复**（玩家还能玩），
                // 但必须点名说清"刚读到的不是槽位档"，⛔ 不静默换源。
                Log.Warn("Save", $"槽位档「{name}」内容损坏（已留档 {store.LastCorruptPath}）"
                    + $"⇒ 本次回退旧键 {legacyKey} 读取并重新迁入槽位（旧副本的价值在此体现）");
            }

            string err;
            if (!store.Write(name, legacy, out err))
            {
                // 迁移失败也照常读得到档（不影响玩家），下次访问再试
                Log.Warn("Save", $"旧档「{name}」迁入槽位失败（{err}）⇒ 本次仍按旧键读取，下次再试");
                return legacy;
            }

            try
            {
                Game.Setting.Delete(legacyKey);
                Game.Setting.Save();
            }
            catch (Exception ex)
            {
                Log.Warn("Save", $"旧档「{name}」已迁入槽位，但旧键清理失败（{legacyKey}）："
                    + $"{ex.GetType().Name}: {ex.Message}");
            }

            Log.Info("Save", $"[Save] 旧档已迁移到槽位：「{name}」（{legacyKey} → {store.Dir}）");
            return legacy;
        }

        private static IPlayerModule Player
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Player : null;
            }
        }

        private static IItemModule Item
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Item : null;
            }
        }

        private static IQuestModule Quest
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Quest : null;
            }
        }

        private static INpcModule Npc
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Npc : null;
            }
        }

        private static ISkillModule Skill
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Skill : null;
            }
        }

        private static IMapModule Map
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Map : null;
            }
        }

        /// <summary>
        /// ★ 片 save-progress：把**由 App 层持有**的进度类状态收进本次存档（`Events.SaveCollect`）。
        /// <list type="bullet">
        /// <item>传送点已激活列表（`App/AppWaypoint.Visited`，进程内 static）；</item>
        /// <item>小地图已探索格（渲染层 `MapView._explored`，按区域各一份）。</item>
        /// </list>
        /// <para>为什么走事件而不是直接调 App：① 保持**依赖方向**（本模块只认 `AppContext` 这个注册表，
        /// 不认 App 的交互类）；② 离线宿主（`tools/probes/hosts/savecheck`）能**自己订阅**本事件
        /// 来证明"收集接线成立"，从而让断言**可能变红**（收方不填 ⇒ 落盘就是空集合）。</para>
        /// <para>⛔ 非预期分支必须留痕：无总线 / 无订阅者 / 收方抛异常 ⇒ Warn 并保持空集合
        /// （**存档照常成功** —— 丢的是"进度记忆"，不是整份档）。</para>
        /// </summary>
        private void CollectAppProgress(CharacterSave data)
        {
            var bus = Game.Event;
            if (bus == null)
            {
                Log.Warn("Save", $"[Save] Game.Event 未接入 ⇒ 未发 {Events.SaveCollect}"
                    + "（传送点已激活列表 / 小地图已探索格本次落盘为空集合）");
                return;
            }
            try
            {
                bus.Emit(Events.SaveCollect, data);
            }
            catch (Exception ex)
            {
                Log.Warn("Save", $"[Save] {Events.SaveCollect} 的收方抛异常：{ex.GetType().Name}: {ex.Message}"
                    + " ⇒ 这两个字段保持空集合（存档仍会成功）");
            }
        }

        /// <summary>日志/断言用的可读摘要（例：`探索［area=1 80x80 612格］`）。</summary>
        private static string DescribeExplored(CharacterSave data)
        {
            if (data.exploredByArea == null || data.exploredByArea.Count == 0) return "探索=无";
            var sb = new System.Text.StringBuilder(64);
            for (var i = 0; i < data.exploredByArea.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("［").Append(ExploredCodec.Describe(data.exploredByArea[i])).Append('］');
            }
            return "探索=" + sb;
        }

        /// <summary>Player 之外的模块装配（Flow 只装 Player，所以这里必须补全）。</summary>
        private void ApplyOtherModules(CharacterSave data)
        {
            var skill = Skill;
            if (skill != null) skill.ResetForClass(data.cls, data);       // 技能等级/左右键从存档恢复
            else Log.Warn("Save", "读档：`ISkillModule` 未接入 ⇒ 技能与左右键绑定未恢复");

            var item = Item;
            if (item != null) item.LoadFrom(data);
            else Log.Warn("Save", "读档：`IItemModule` 未接入 ⇒ 背包/装备/腰带未恢复");

            var quest = Quest;
            if (quest != null) quest.LoadFrom(data);
            else Log.Warn("Save", "读档：`IQuestModule` 未接入 ⇒ 任务进度未恢复");

            var npc = Npc;
            if (npc != null) npc.LoadFrom(data);
        }

        /// <summary>
        /// 把技能写进存档。`ISkillModule` 没有 `WriteTo`（契约冻结），但公开了
        /// `Available` / `GetLevel(id)` / `GetButtonSkill(button)` ⇒ 可以完整反查出来。
        /// </summary>
        private void WriteSkills(CharacterSave data)
        {
            data.skillIds = new List<int>();
            data.skillLevels = new List<int>();
            data.buttonSkills = new List<int> { -1, -1 };

            var skill = Skill;
            if (skill == null)
            {
                Log.Warn("Save", "存档：`ISkillModule` 未接入 ⇒ 技能列表按空写入（读档后技能会丢）");
                return;
            }

            var avail = skill.Available;
            if (avail != null)
            {
                for (var i = 0; i < avail.Count; i++)
                {
                    var def = avail[i];
                    if (def == null) continue;
                    var lvl = skill.GetLevel(def.id);
                    if (lvl <= 0) continue;
                    data.skillIds.Add(def.id);
                    data.skillLevels.Add(lvl);
                }
            }

            data.buttonSkills[0] = skill.GetButtonSkill(0);
            data.buttonSkills[1] = skill.GetButtonSkill(1);
        }

        /// <summary>
        /// 读角色**创建先后**索引（`char/index`，JSON 字符串数组）。
        /// <para>⚠️ 它**只表达顺序**、不再是"有哪些角色"的权威来源（权威 = 槽位目录里有哪些文件）：
        /// 索引里多出来的名字会被 <see cref="List"/> 过滤掉并告警；迁移期的老角色也会经
        /// <see cref="Exists"/> → <see cref="ReadRaw"/> 被搬进槽位后照常列出。</para>
        /// </summary>
        private List<string> ReadIndex()
        {
            string raw;
            try
            {
                raw = Game.Setting.Get<string>(GameConst.SaveIndexKey, "");
            }
            catch (Exception ex)
            {
                Log.Warn("Save", $"读取角色索引失败（{GameConst.SaveIndexKey}）：{ex.GetType().Name}: {ex.Message}");
                return new List<string>();
            }

            var names = SaveJson.ParseStringList(raw);
            if (!string.IsNullOrEmpty(SaveJson.LastIndexError))
            {
                Log.Warn("Save", $"角色索引（{GameConst.SaveIndexKey}）解析失败 ⇒ 按空清单处理，"
                    + $"角色文件本身未受影响：{SaveJson.LastIndexError}");
            }
            return names;
        }

        private void AddToIndex(string name)
        {
            var names = ReadIndex();
            for (var i = 0; i < names.Count; i++)
            {
                if (names[i] == name) return;
            }
            names.Add(name);
            WriteIndex(names);
        }

        private void WriteIndex(List<string> names)
        {
            Game.Setting.Set(GameConst.SaveIndexKey, SaveJson.WriteStringList(names));
        }

        private float ElapsedSinceBase()
        {
            if (_sessionBaseTicks <= 0) return 0f;
            var ticks = DateTime.UtcNow.Ticks - _sessionBaseTicks;
            if (ticks <= 0) return 0f;
            return (float)(ticks / (double)TimeSpan.TicksPerSecond);
        }

        private bool Fail(string reason)
        {
            _lastError = reason;
            Log.Error("Save", reason);
            return false;
        }

        /// <summary>
        /// ★ R7 情况 ②：**损坏档**的统一出口（除 `Load` 外无其它调用点）。
        /// ① 记 `LastError`（含档名 + 原因）⇒ 调用方据此把"损坏"与"档不存在"分开；
        /// ② Error 日志（⛔ 不静默）；
        /// ③ `Events.LoadDone` 发 **null** —— 既定契约（`Core/Events.cs:368`「null = 失败」），
        ///    唯一消费者 = `AppFlow.OnLoadDone`（复用 `UI/D2ConfirmPanel` 给玩家可见反馈）。
        /// <para>⛔ 不删除/不覆盖损坏文件（引擎已留档 `.corrupt` 副本，原文件也在）—— 删档只能由玩家显式操作。</para>
        /// </summary>
        private CharacterSave FailCorrupt(string name, string detail)
        {
            // ① **完整技术细节只进日志**（档名 / 引擎判定 / 留档副本路径 / 原文件位置）—— 排障要的是这一行。
            Log.Error("Save", $"读档失败：存档已损坏（槽位「{name}」）{detail}；文件仍在 {Store.Dir}（未被覆盖）");
            // ② `LastError` 保持**短的玩家口径**：它会被 `AppFlow` 拿去弹**用户可见的提示框**
            //    （`UI/D2ConfirmPanel` 的正文框只有 272×90 原版px = `UiLayoutFlow.Confirm.MessageSizeOrig`
            //    ⇒ 长技术串会**溢出框外**；首版实机图 `.ai-tmp/screenshots/q3_corrupt_dialog.png` 就是这样，
            //    本行按实机图改成短文案）。
            _lastError = $"角色「{name}」的存档已损坏，无法读取";
            // ③ 既定契约（`Core/Events.cs:368`）：参数 null = 读档失败 ⇒ 唯一消费者 = `AppFlow.OnLoadDone`。
            Game.Event?.Emit(Events.LoadDone, (CharacterSave)null);
            return null;
        }

        /// <summary>
        /// ★ R7：`FileSlotStore.LastCorruptPath` 是否**恰好**指向**本槽**刚留档的坏文件。
        /// <para>为什么不能用"`LastCorruptPath` 非空"当判据：它是**存储级**的"最近一次留档"，
        /// 批量读（`ListAll()`）时会被别的槽覆盖 ⇒ 必须逐字比对本槽应有的留档路径。</para>
        /// <para>路径口径 = 引擎 `FileSlotStore.Archive`：`Path.Combine(Dir, key + ext) + ".corrupt"`
        /// （`FileSlotStore.cs:459-474`）；`ext` 由本类显式传入（<see cref="SlotExtension"/>）。</para>
        /// </summary>
        private static bool IsThisSlotCorruptArchive(FileSlotStore store, string name)
        {
            var archived = store.LastCorruptPath;
            if (string.IsNullOrEmpty(archived)) return false;
            try
            {
                var expected = System.IO.Path.Combine(store.Dir, name + SlotExtension) + ".corrupt";
                return string.Equals(archived, expected, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                // 非预期分支：路径拼不出来（name 含 '\0' 一类）⇒ 留痕后按"不是损坏"处理
                Log.Warn("Save", $"反查损坏留档路径失败（{name}）：{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// ★ T0FIX-D：**存档尝试失败**的唯一出口 —— 记错误 + 发 `Events.SaveDone(false)`。
        /// <para>所有"保存"入口（`Save()` / `Save(CharacterSave)`）的失败分支一律改走本方法，
        /// 保证 `Events.SaveDone` 的语义 = 「**每一次**存档尝试的结果」，不是"只报成功"。</para>
        /// <para>⚠️ `Fail` 仍被读档/删除路径使用（那些**不是**存档尝试 ⇒ 不发 `SaveDone`）。</para>
        /// </summary>
        private bool FailSave(string reason)
        {
            Fail(reason);
            SignalSaveDone(false);
            return false;
        }

        /// <summary>
        /// ★ T0FIX-D：发 `Events.SaveDone`（参数 = 是否成功）。
        /// `Game.Event` 未挂载（纯逻辑宿主）⇒ 静默跳过（`?.`），由调用方自己的日志兜底。
        /// </summary>
        private static void SignalSaveDone(bool ok)
        {
            Game.Event?.Emit(Events.SaveDone, ok);
        }

        private static int CountAnchors(CharacterSave data)
        {
            if (data.inventory == null) return 0;
            var n = 0;
            for (var i = 0; i < data.inventory.Count; i++)
            {
                var s = data.inventory[i];
                if (s != null && s.isAnchor && s.item != null) n++;
            }
            return n;
        }

        private static string DescribeQuests(CharacterSave data)
        {
            if (data.quests == null || data.quests.Count == 0) return "无";
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < data.quests.Count; i++)
            {
                var q = data.quests[i];
                if (q == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(q.name).Append('=').Append(q.state).Append('(').Append(q.progress).Append('/').Append(q.required).Append(')');
            }
            return sb.Length == 0 ? "无" : sb.ToString();
        }
    }
}
