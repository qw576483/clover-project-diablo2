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
// 版本：`CharacterSave.version`；读档版本不符 ⇒ **降级处理 + Warn**（缺字段取默认值，绝不让读档崩）。
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

        private string _lastError = "";

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
                    _store = new FileSlotStore(System.IO.Path.Combine(root, SaveDirName));
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
                return Fail("存档失败：`Game.Setting` 未接入（引擎未启动？）");
            }

            var p = Player;
            if (p == null)
            {
                return Fail("存档失败：`IPlayerModule` 未接入 ⇒ 拿不到角色名/属性（无法构成存档）");
            }
            if (string.IsNullOrEmpty(p.Name))
            {
                return Fail("存档失败：当前角色名为空（还没创角？）");
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
            if (map != null && map.IsGenerated) data.mapSeed = map.Seed;   // 本局地图 seed（读档要复现同一张图）

            data.playedSeconds = _sessionBasePlayed + ElapsedSinceBase();

            Log.Info("Save", $"[Save] 收集完成：{data.name} 职业={data.cls} 等级={data.level} 金币={data.gold} "
                + $"背包锚点={CountAnchors(data)} 装备={data.equip.Count} 技能={data.skillIds.Count} seed={data.mapSeed}");
            return Save(data);
        }

        /// <summary>存一份指定数据（创角时先把角色写进去）。</summary>
        public bool Save(CharacterSave data)
        {
            if (data == null)
            {
                return Fail("存档失败：CharacterSave 为 null");
            }
            if (string.IsNullOrEmpty(data.name))
            {
                return Fail("存档失败：角色名为空（空名不合法）");
            }
            if (!Ready)
            {
                return Fail("存档失败：`Game.Setting` 未接入（引擎未启动？）");
            }

            data.version = GameConst.SaveVersion;
            data.savedAtTicks = DateTime.UtcNow.Ticks;

            var json = SaveJson.Write(data);
            var name = data.name;                       // 槽位键 = 角色名（一角色一文件）
            string err;
            if (!Store.Write(name, json, out err))
            {
                return Fail($"存档失败（写槽位「{name}」）：{err}");
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
            return true;
        }

        // ── 读 ─────────────────────────────────────────────────────────────────

        /// <summary>读某个角色的存档；不存在返回 null 并打日志。</summary>
        public CharacterSave Load(string name)
        {
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
                Log.Info("Save", $"读档：没有角色「{name}」的存档（槽位 {Store.Dir} 与旧键 {KeyOf(name)} 均为空）");
                return null;
            }

            string err;
            var data = SaveJson.TryParse(json, out err);
            if (data == null)
            {
                Fail($"读档失败（解析槽位「{name}」）：{err}");
                return null;
            }

            // 版本不符 ⇒ **降级处理**（缺字段已在 SaveJson 里取默认值），绝不抛异常
            if (data.version != GameConst.SaveVersion)
            {
                Log.Warn("Save", $"读档：「{name}」的存档版本 {data.version} ≠ 当前 {GameConst.SaveVersion} "
                    + "⇒ 降级处理（缺字段取默认值、按当前版本继续读），请勿在此基础上回写覆盖原档");
                data.version = GameConst.SaveVersion;
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

        /// <summary>读存档（不存在的返回 false，不抛异常）。</summary>
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
            var text = store.Read(name);
            if (!string.IsNullOrEmpty(text)) return text;

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

            if (string.IsNullOrEmpty(legacy)) return null;

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
