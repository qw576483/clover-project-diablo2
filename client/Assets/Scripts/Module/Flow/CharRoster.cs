// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/Flow/CharRoster.cs
// 「有哪些角色可进图」的**唯一问询点**：主菜单「继续」可用性、选角屏列表、建角、删角。
//
// 数据来源分两级（**不重复实现存档**）：
//   ① `ISaveModule`（`Module/Save/SaveModule.cs`，agent-08）已接入 ⇒ **全部委托给它**
//      （档 = 引擎 `FileSlotStore` 的槽位 `saves/{角色名}.json`，A6 下沉；创建先后索引
//       `char/index` 见 `GameConst.SaveIndexKey`）；
//   ② 未接入（`AppContext.Save == null`） ⇒ 退化为**本次会话内存**并把角色列表照常服务，
//      但**不落盘**、并打一条 Warn（明确告知"重进会丢"）。
//      —— 这是「null 容忍 + 日志」的降级，**不是**给别模块写假实现：本类不碰任何
//         存档键、不落盘、不假装成功（见 `docs/agents/_common.md` §4 与回报「未决」）。
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Diablo2.App;
using Diablo2.Core;
using Diablo2.Def;

namespace Diablo2.Module.Flow
{
    /// <summary>角色名册（存档模块已接入则全权委托，否则会话内内存降级）。</summary>
    internal sealed class CharRoster
    {
        private readonly List<CharacterSave> _sessionOnly = new List<CharacterSave>();

        /// <summary>存档模块（可能为 null）。</summary>
        private static ISaveModule SaveModule
        {
            get
            {
                var ctx = AppContext.I;
                return ctx != null ? ctx.Save : null;
            }
        }

        /// <summary>是否已接到存档模块（false = 角色只在本次会话内存在）。</summary>
        public bool Persisted => SaveModule != null;

        /// <summary>是否存在任意存档（主菜单「继续」按钮可用性）。</summary>
        public bool HasAny
        {
            get
            {
                var save = SaveModule;
                if (save != null) return save.HasAny;
                return _sessionOnly.Count > 0;
            }
        }

        /// <summary>全部角色（选角屏卡片用）。</summary>
        public List<CharacterSave> ListAll()
        {
            var save = SaveModule;
            if (save == null) return new List<CharacterSave>(_sessionOnly);

            var list = save.ListAll();
            if (list == null)
            {
                Log.Warn("Flow", "ISaveModule.ListAll() 返回 null（应为空列表），按空处理");
                return new List<CharacterSave>();
            }
            return list;
        }

        /// <summary>是否存在该角色。</summary>
        public bool Exists(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var save = SaveModule;
            if (save != null) return save.Exists(name);
            return Find(name) != null;
        }

        /// <summary>取某角色的存档数据（不存在返回 null）。</summary>
        public CharacterSave Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var save = SaveModule;
            if (save != null) return save.Load(name);
            return Find(name);
        }

        /// <summary>新增一个角色（创角）。成功返回 true。</summary>
        public bool Create(CharacterSave data)
        {
            if (data == null || string.IsNullOrEmpty(data.name))
            {
                Log.Warn("Flow", "CharRoster.Create 收到非法数据（null 或空名），拒绝");
                return false;
            }

            var save = SaveModule;
            if (save != null) return save.Save(data);

            if (Find(data.name) != null)
            {
                Log.Warn("Flow", $"CharRoster.Create：角色「{data.name}」已存在（会话内），拒绝重复创建");
                return false;
            }

            _sessionOnly.Add(data);
            Log.WarnOnce("Flow", "roster.no_save_module",
                "ISaveModule 未接入（AppContext.Save == null）：角色只存在于本次会话，**退出即丢**；" +
                $"当前会话角色数={_sessionOnly.Count}（存档模块接入后自动改为落盘）");
            Log.Info("Flow", $"角色「{data.name}」（会话内）已加入名册：共 {_sessionOnly.Count} 个");
            return true;
        }

        /// <summary>删除一个角色。成功返回 true。</summary>
        public bool Delete(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Log.Warn("Flow", "CharRoster.Delete 收到空名，拒绝");
                return false;
            }

            var save = SaveModule;
            if (save != null) return save.Delete(name);

            var found = Find(name);
            if (found == null)
            {
                Log.Warn("Flow", $"CharRoster.Delete：会话内找不到角色「{name}」");
                return false;
            }

            _sessionOnly.Remove(found);
            Log.Info("Flow", $"角色「{name}」（会话内）已删除：剩 {_sessionOnly.Count} 个");
            return true;
        }

        private CharacterSave Find(string name)
        {
            for (var i = 0; i < _sessionOnly.Count; i++)
            {
                if (_sessionOnly[i] != null && _sessionOnly[i].name == name) return _sessionOnly[i];
            }
            return null;
        }
    }
}
