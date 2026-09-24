// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Core/ClientConfig.cs
// 部署期可变的量走配置文件（`Assets/Configs/config.json`），**不许写死在业务代码里**
// （范式见 skill `patterns/client/config.md`）。
//
// 铁律：
//   ① 同一项配置**只允许一处默认值**（本文件里 `GameConfigSection` 的字段默认值），业务里不要再写第二遍；
//   ② 文件缺失 / 解析失败 → **回退默认值 + Warn，绝不抛异常**（配置问题不该让游戏起不来）；
//   ③ 业务读配置一律 `Cfg.Xxx`，不出现裸字面量。
//
// 本类**故意不提供**名为 `Game` 的成员 —— 那会遮蔽引擎的 `Game` 门面
//    （`Game.Logger` 会变成 CS1061，见 skill `patterns/client/config.md` 常见问题）。
//    取 game 段用 `Cfg.GameCfg`，或直接用下面的属性快捷方式。
//
// 加载顺序（引擎件 `ConfigSectionLoader<T>` 按序试，全坏则回内置默认值）：
//   ① `Resources/Configs/config`                      —— 引擎资源模块（跨平台：WebGL / 移动端只能走这条）
//   ② `Application.dataPath/Configs/config.json`      —— Editor 与桌面平台
//   ③ 内置默认值 + 一条 Warn
//
// 本文件只负责「这个项目的 config.json 长什么样」：
//   · 逐来源怎么读（资源模块 / 磁盘文件） = 本文件的两个读取委托；
//   · ①→②→③ 的顺序回退、读取/解析失败跳过、全坏回默认值、`Reload` 重跑链 = **引擎件**
//     `CloverEngine.ConfigSectionLoader<T>`（`clover-client-unity-engine/Runtime/Core/ClientConfig.cs`）。
//   · 公开 API（`Source` / `FilePath` / `Root` / `GameCfg` / `DefaultPlayerName` / `BgmVolume` /
//     `SfxVolume` / `Fullscreen` / `Reload`）由本文件提供。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.IO;
using CloverEngine;
using UnityEngine;

namespace Diablo2.Core
{
    /// <summary>`config.json` 的 `game` 段。**字段名 = JSON 键名**（JsonUtility 按字段名映射），不许改。</summary>
    [Serializable]
    public class GameConfigSection
    {
        /// <summary>创建角色时的默认名字。</summary>
        public string default_player_name = "Hero";

        /// <summary>BGM 音量 0~1。</summary>
        public float bgm_volume = 0.7f;

        /// <summary>音效音量 0~1。</summary>
        public float sfx_volume = 0.8f;

        /// <summary>是否全屏启动。</summary>
        public bool fullscreen = false;

        // 这里**没有**「方向键备选移动」开关字段：原版 D2 只有鼠标点地面移动
    }

    /// <summary>`config.json` 根对象。</summary>
    [Serializable]
    public class ConfigRootSection
    {
        /// <summary>game 段。</summary>
        public GameConfigSection game = new GameConfigSection();
    }

    /// <summary>客户端配置入口（静态类）。</summary>
    public static class Cfg
    {
        private const string Tag = "Cfg";

        /// <summary>加载链（首次访问时构造；`Root` 惰性触发一次加载，`Reload` 重跑）。</summary>
        private static ConfigSectionLoader<ConfigRootSection> _loader;

        private static string _filePath;

        private static ConfigSectionLoader<ConfigRootSection> Loader
        {
            get
            {
                if (_loader == null)
                {
                    // 来源表在**首次访问时**求值（`FilePath` 会碰 `Application.dataPath`）：
                    //    静态构造期不读盘、不碰 Unity API。
                    _loader = new ConfigSectionLoader<ConfigRootSection>(
                        Tag,
                        new[]
                        {
                            // ① 引擎资源模块（跨平台：WebGL / 移动端只能走这条）
                            new ConfigSource($"Resources/{ResPaths.ConfigResourceKey}", ReadResourceText),
                            // ② Application.dataPath（Editor / 桌面）；`LogLabel` = 磁盘路径（解析错误点名用）
                            new ConfigSource("文件:" + FilePath, ReadFileText, FilePath),
                        },
                        Parse,
                        () => new ConfigRootSection(),
                        (root, from) => ClampAndWarn(root.game, from));
                }

                return _loader;
            }
        }

        /// <summary>配置来源描述（`Resources` / `文件:<路径>` / `默认值`），打日志时带上便于定位。</summary>
        public static string Source => Loader.Source;

        /// <summary>
        /// 配置文件在磁盘上的绝对路径（Editor / 桌面平台）。
        /// `Application.dataPath` 在**非 Unity 宿主**（单元测试/纯 C# 进程）里会抛
        /// `SecurityException`；本属性**绝不向上抛**，退回相对路径并只报一次错。
        /// </summary>
        public static string FilePath
        {
            get
            {
                if (_filePath != null) return _filePath;

                try
                {
                    _filePath = Path.Combine(Application.dataPath, ResPaths.ConfigRelativePath);
                }
                catch (Exception e)
                {
                    Log.ErrorOnce(Tag, "cfg.datapath",
                        $"Application.dataPath 不可用（{e.GetType().Name}），退回相对路径 {ResPaths.ConfigRelativePath}");
                    _filePath = ResPaths.ConfigRelativePath;
                }

                return _filePath;
            }
        }

        /// <summary>根对象（首次访问时加载）。</summary>
        public static ConfigRootSection Root => Loader.Value;

        /// <summary>game 段（**不要**命名为 `Game`，会遮蔽引擎门面）。</summary>
        public static GameConfigSection GameCfg => Root.game;

        /// <summary>创角默认名字（为空时回退内置默认值）。</summary>
        public static string DefaultPlayerName
        {
            get
            {
                var v = GameCfg.default_player_name;
                if (string.IsNullOrWhiteSpace(v))
                {
                    Log.WarnOnce(Tag, "cfg.name.empty", "config.json 的 game.default_player_name 为空，回退默认值");
                    return new GameConfigSection().default_player_name;
                }
                return v;
            }
        }

        /// <summary>BGM 音量（已裁剪到 0~1）。</summary>
        public static float BgmVolume => Mathf.Clamp01(GameCfg.bgm_volume);

        /// <summary>音效音量（已裁剪到 0~1）。</summary>
        public static float SfxVolume => Mathf.Clamp01(GameCfg.sfx_volume);

        /// <summary>是否全屏启动。</summary>
        public static bool Fullscreen => GameCfg.fullscreen;

        /// <summary>重新加载（改完 json 不必重启编辑器时用）。</summary>
        public static void Reload()
        {
            Loader.Reload();
            Log.Info(Tag, $"配置已重载，来源={Loader.Source}");
        }

        /// <summary>
        /// 来源 ①：从**引擎资源模块**同步取 `Configs/config`（该路径下的第一个）。
        /// <para>路径是**相对 `CloverRes` 根前缀**的写法（`Configs/config`），根前缀由后端拼。</para>
        /// <para>返回 null / 空 = 本来源没有内容（引擎静默跳过，改试来源 ②）。</para>
        /// </summary>
        private static string ReadResourceText()
        {
            try
            {
                var asset = LoadConfigAsset();
                return asset != null ? asset.text : null;
            }
            catch (Exception e)
            {
                // 非预期分支：跳过本条来源，退回文件与默认值（不静默）
                Log.Warn(Tag, $"Resources 读取 {ResPaths.ConfigResourceKey} 异常：{e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 来源 ②：`Application.dataPath/Configs/config.json`。
        /// <para>文件不存在 / 读不动 ⇒ **Warn + 返回 null**（不抛；引擎改试下一个来源 = 内置默认值）。</para>
        /// </summary>
        private static string ReadFileText()
        {
            // catch 里**不许再调 FilePath**：失败的操作在 catch 里重演一次 = 异常直接漏出去
            var path = string.Empty;
            try
            {
                path = FilePath;
                if (!File.Exists(path))
                {
                    Log.Warn(Tag, $"未找到配置文件 {path}，使用内置默认值");
                    return null;
                }

                return File.ReadAllText(path);
            }
            catch (Exception e)
            {
                Log.Warn(Tag, $"读取配置文件异常（path=\"{path}\"），使用内置默认值：{e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        /// <summary>引擎资源模块里取 config 资源（`Game.Res` 未初始化 / 无资源 ⇒ null）。</summary>
        private static TextAsset LoadConfigAsset()
        {
            if (Game.Res == null)
            {
                // 非预期分支（宿主 / 启动顺序错）：跳过本条来源，退回文件与默认值（不静默）
                Log.WarnOnce(Tag, "cfg.res.null",
                    "Game.Res 未初始化（CloverRes.Init 未调用）⇒ 跳过资源模块读取 config.json，改用文件 / 默认值");
                return null;
            }

            var all = Game.Res.LoadAll<TextAsset>(ResPaths.ConfigResourceKey);
            return all != null && all.Length > 0 ? all[0] : null;
        }

        /// <summary>
        /// 解析一段 json（**返回 null = 本来源不可用**，引擎改试下一个来源 / 回默认值）。
        /// <para>由引擎件按来源逐个调用（`from` = 该来源的日志标签：`Resources/...` 或磁盘路径）。</para>
        /// </summary>
        private static ConfigRootSection Parse(string json, string from)
        {
            try
            {
                var root = JsonUtility.FromJson<ConfigRootSection>(json);
                if (root == null)
                {
                    Log.Error(Tag, $"解析失败（内容为空/不是对象）：{from}，回退默认值");
                    return null;
                }

                // JsonUtility 对 json 里缺失的段会留 null，逐段兜底防下游空引用
                if (root.game == null)
                {
                    Log.Warn(Tag, $"{from} 缺少 game 段，使用默认 game 段");
                    root.game = new GameConfigSection();
                }

                return root;
            }
            catch (Exception e)
            {
                Log.Error(Tag, $"解析异常（JSON 格式错误？）：{from} → {e.Message}，回退默认值");
                return null;
            }
        }

        /// <summary>命中来源后的规范化钩子（引擎在解析成功、返回给业务之前调用一次）。</summary>
        private static void ClampAndWarn(GameConfigSection game, string from)
        {
            if (game == null) return;

            if (game.bgm_volume < 0f || game.bgm_volume > 1f)
            {
                Log.Warn(Tag, $"{from} 的 game.bgm_volume={game.bgm_volume} 越界，已裁剪到 0~1");
                game.bgm_volume = Mathf.Clamp01(game.bgm_volume);
            }
            if (game.sfx_volume < 0f || game.sfx_volume > 1f)
            {
                Log.Warn(Tag, $"{from} 的 game.sfx_volume={game.sfx_volume} 越界，已裁剪到 0~1");
                game.sfx_volume = Mathf.Clamp01(game.sfx_volume);
            }
        }
    }
}
