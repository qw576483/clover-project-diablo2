// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · App/Bootstrap.cs
// **全项目唯一手动挂载的脚本**（挂 `Boot` 场景），也是唯一组装点。
//
// 职责（照 `docs/agents/agent-05-流程与菜单链路.md` §4.2 与 §2 任务边界）：
//   ① `Game.Launch`（单机最小集：不设 ServerAddr、**不调** `CloverNet.Init`）
//   ② `CloverRes.Init(ResPaths.Root)`（不 Init 资源模块，贴图/模型会静默加载失败）
//   ③ `CloverInput.Init()`（必须**在建 UI 之前**，否则按钮点不动）
//   ④ 配表：`Table.TableLoader.LoadAll(...)`（agent-02 指定的唯一加载入口）
//   ⑤ 存档里的设置应用到引擎（音量/全屏 ⇒ 重进后仍在）
//   ⑥ 装配 `AppContext`（`AutoWire` 反射接入全部模块；缺实现 ⇒ 留 null + Warn 降级）
//   ⑦ 注入 `Stage` 场景根节点（序列化字段 / `App/StageRoots.cs`）
//   ⑧ ★ **最后一次接线** `AppWiring.Install`（agent-12）：Stage 进/出（HUD + 全量快照）/
//      过门去重断言 / 根节点转交 / UI 请求转发
//   ⑨ `Game.Fsm.Force("Boot")` → `Flow.Enter()`（菜单链路从这里开始，**不直接进游戏场景**）
//   `Update()` 只做「转发 Tick」，不写业务；本文件刻意保持在 200 行以内。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Module.Flow;
using UnityEngine;

namespace Diablo2.App
{
    /// <summary>唯一入口：启动引擎 → 装配模块 → 进流程。</summary>
    public class Bootstrap : MonoBehaviour
    {
        /// <summary>单实例守卫（重复的 Bootstrap 只让**一个**走完 Start）。</summary>
        private static bool _started;

        /// <summary>
        /// ★ **每次进 Play 复位本层/流程的静态闸门**（skill `reference/pipeline-and-unity-cli.md` **P-3**）。
        /// <para>真因：工程若开了「Enter Play Mode Options（**不重载域**）」
        /// （`ProjectSettings/EditorSettings.asset`：`m_EnterPlayModeOptionsEnabled: 1` + `m_EnterPlayModeOptions: 1`），
        /// `static` 字段会**跨 Play 局残留** ⇒ 第二次进 Play 时 `_started == true`，新一局的 Bootstrap
        /// 直接 `Destroy(gameObject)` 自毁 ⇒ **整屏只有相机底色、一个字都没有、连一条日志都没有**（不报错）。</para>
        /// <para>本项目实测就踩到了（首次 Play 正常，第二次全黑无日志、`bootstrap=0`、`Game.Fsm.Current` 还是上一局的终态）。
        /// `SubsystemRegistration` 钩子**每次进 Play 都会执行**（即使域不重载），所以复位写在这里最可靠。
        /// 另一条对策是把工程设置里的"不重载域"关掉（更彻底，见回报）。</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForNewPlaySession()
        {
            _started = false;
            AppContext.ResetStaticForNewPlaySession();
            AppWiring.ResetStaticForNewPlaySession();
            AppSnapshots.ResetStaticForNewPlaySession();
            AppDoorGuard.ResetStaticForNewPlaySession();
            AppFlow.ResetStaticForNewPlaySession();
            Log.ResetThrottle();          // 上一局的"只报一次"记录不许压住本局该报的日志

            Game.Logger?.Info("App",
                "[Play] 新一局 Play：App/Flow 静态闸门已复位（工程若开了「不重载域」，没有这一步第二局就是黑屏）");
        }

        /// <summary>`Stage` 场景的「地图根」（`MapModule.AttachRoot` 的落点；留空则由 `StageRoots` 或模块自建）。</summary>
        [SerializeField] private Transform mapRoot;

        /// <summary>`Stage` 场景的「实体根」（`ViewModule.AttachRoot` 的落点；留空则由 `StageRoots` 或模块自建）。</summary>
        [SerializeField] private Transform entityRoot;

        private void Awake()
        {
            var all = Object.FindObjectsByType<Bootstrap>();      // Unity 6：不带排序模式的重载
            if (all != null && all.Length > 1)
            {
                // 不在这里自毁：场景里多个实例同时 Awake 时，「谁先谁留」不确定。
                // 统一交给 Start 的静态守卫（只会有一个走到装配），这里只负责报警。
                Game.Logger.Warn("App",
                    $"场景中有 {all.Length} 个 Bootstrap（应只有 1 个）→ 只保留第一个完成启动的实例");
            }
        }

        private void Start()
        {
            if (_started)
            {
                Game.Logger.Warn("App", "重复的 Bootstrap 实例，自毁（单实例守卫）");
                Destroy(gameObject);
                return;
            }
            _started = true;

            // 本对象跨场景常驻：Boot 场景在进主菜单时会被 Menu 场景替换，
            // 而模块 Tick 转发必须一直有效（否则进主菜单后所有模块停止推进）。
            DontDestroyOnLoad(gameObject);

            // ① 引擎（单机：不设 ServerAddr）
            // ★ P-3：域不重载时 `Game.IsRunning` 会残留为 true ⇒ `Game.Launch` 会**直接 return**
            //    （`Runtime/Core/Game.cs:321-325`：`if (IsRunning) { Debug.LogWarning("Already running"); return; }`）
            //    ⇒ UI/Scene/Timer 全不初始化。先显式关掉上一局，再 Launch。
            if (Game.IsRunning)
            {
                Game.Logger.Warn("App",
                    "Game.IsRunning 仍为 true（上一局 Play 的静态残留：工程开了「不重载域」）⇒ 先 Game.Shutdown() 再 Launch");
                Game.Shutdown();
            }

            // ①b ★ 引擎下沉 A5（消验收表 E19）：装配"文字渲染挂钩" —— 引擎通用件（Toast / Loading /
            //    确认框 / 飘字 / 引导）建的 Text 从此与业务面板一样走**原版字模**。
            //    ⚠️ **必须在 Game.Launch 之前**：引擎的 LoadingLayer 在 `UIManager` 构造时
            //    （= Launch 内的 `CloverPresentation.Init` → `new UIManager()`）就把「加载中...」
            //    那条 Text 建好并通知过了，晚于 Launch 装配会**永久漏掉它**（E19 取证里它正是其中一条）。
            //    未注册时引擎行为逐字不变（`TextHooks.NotifyCreated` 判空即返回）；实现见 `UI/D2EngineTextHook.cs`
            //    （此处按全名写，避免 App 层为此多引一个 `using Diablo2.UI`）。
            Diablo2.UI.D2EngineTextHook.Install();

            Game.Launch(new GameConfig
            {
                LogDir = "logs",
                SettingDir = "setting",
                ResourceRoot = "",          // 空 = Resources 根；资源前缀由 CloverRes.Init 决定
            });
            Game.Logger.Info("App", $"引擎已启动：tag=App，日志目录=logs，设置目录=setting（单机版）");

            // ② 资源模块（不 Init ⇒ 贴图静默加载失败）
            CloverRes.Init(ResPaths.Root);

            // ②b ★ A5 挂钩的第二半：**资源就绪后**补挂上一行期间被寄存的引擎 Text（就是 LoadingLayer
            //    的「加载中...」）。⚠️ 顺序不能提前：`D2Text.EnsureChi` 在 `Game.Res == null` 时会走
            //    `MarkBitmapUnavailable`（**把整个项目的字模永久降级成系统字体**，一局内不再恢复）
            //    ⇒ 挂钩侧刻意把"资源未就绪"的 Text 寄存起来，等这一行。
            Diablo2.UI.D2EngineTextHook.FlushPending();

            // ③ 输入 + EventSystem（必须在建 UI 之前）
            CloverInput.Init();

            // ④ 配表（agent-02 的唯一运行时加载入口；失败只打日志，创角会给出明确提示）
            LoadTables();

            // ⑤ 把上次存的设置应用到引擎（音量 / 全屏 ⇒ 「重进后仍在」）
            ApplyStoredSettings();

            // ⑥ 模块装配（先 AutoWire 自动接入已实现模块，再显式建流程）
            var ctx = AppContext.Create();
            ctx.AutoWire();
            ctx.Flow = new AppFlow();

            // ⑦ 场景根节点：`Stage` 场景的 MapRoot / EntityRoot 交进来（为 null ⇒ 模块自建根；见 AppWiring）
            ctx.MapRoot = mapRoot;
            ctx.EntityRoot = entityRoot;

            // ⑧ ★ 最后一次接线（App 层）：Stage 进/出（HUD 开关 + 全量快照）/ 过门去重断言 /
            //    根节点转交 / UI 请求转发。必须在 `Flow.Enter()` 之前完成订阅（HUD 的 Awake 晚于本层）。
            AppWiring.Install(ctx);

            // ⑨ 进流程：先落到 Boot 站点（启动画面），再由玩家点进主菜单。
            // ★§B：**唯一入口是 `Flow.Enter()`** —— 它内部 `Force(Boot)`（挂在 Boot 上时幂等）。
            //       这里**不许**再 `Game.Fsm.Force(StateBoot)`：那样 OnChange 已经打过一条
            //       `[Flow] → Boot`，`Enter()` 再打一条 ⇒ 启动时站点日志两条（agent-14 §B 现象 2，
            //       Play 日志 seq 47/48 就是这么来的）。
            ctx.Flow.Enter();

            Game.Logger.Info("App", ctx.Describe());
            Game.Logger.Info("App", $"应用启动完成：站点={ctx.Flow.CurrentState}");
        }

        private void Update()
        {
            // 只转发（引擎自驱 Game.Tick；业务模块的 Tick 由 AppContext 转发）
            AppContext.I?.Tick(Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            Game.Setting?.Save();
            Game.Logger.Info("App", "应用退出：设置已落盘");
        }

        /// <summary>加载全部配表到 `Table.Tables.Default`（路径解析见 `Table/TableLoader.cs`）。</summary>
        private static void LoadTables()
        {
            var error = Table.TableLoader.LoadAll(Application.streamingAssetsPath, Application.dataPath);
            if (!string.IsNullOrEmpty(error))
            {
                Game.Logger.Error("Table", error);
                return;
            }

            Game.Logger.Info("Table", $"配表已加载：{Table.TableLoader.LastDir}");
        }

        /// <summary>画质档位的设置键（须与 `UI/SettingsPanel.cs` 的 `KeyQuality` **逐字一致**；`Core/` 是冻结层，故不放进 `GameConst`）。</summary>
        private const string SettingKeyQuality = "video/quality";

        /// <summary>画质档位上限（`LOW/MED/HIGH` ⇒ 0..2；与 `UI/SettingsPanel` 的 `QualityLabels` 同口径）。</summary>
        private const int MaxQualityLevel = 2;

        /// <summary>把 `Game.Setting` 里的持久化设置应用到引擎（缺项用 `Cfg` 默认值）。</summary>
        private static void ApplyStoredSettings()
        {
            if (Game.Setting == null)
            {
                Game.Logger.Warn("App", "Game.Setting 为 null（引擎未启动？）⇒ 跳过设置应用");
                return;
            }

            var bgm = Mathf.Clamp01(Game.Setting.Get<float>(GameConst.SettingKeyBgmVolume, Cfg.BgmVolume));
            var sfx = Mathf.Clamp01(Game.Setting.Get<float>(GameConst.SettingKeySfxVolume, Cfg.SfxVolume));
            var fullscreen = Game.Setting.Get<bool>(GameConst.SettingKeyFullscreen, Cfg.Fullscreen);
            var quality = ApplyStoredQuality();   // ★ 冷启动就把画质档位落到引擎（原缺陷：只在打开选项面板时才应用）

            Game.Sound?.SetVolume(SoundGroup.BGM, bgm);
            Game.Sound?.SetVolume(SoundGroup.SFX, sfx);

            try
            {
                if (Screen.fullScreen != fullscreen) Screen.fullScreen = fullscreen;
            }
            catch (System.Exception e)
            {
                Game.Logger.Warn("App", $"应用全屏设置失败（{fullscreen}）：{e.Message}");
            }

            Game.Logger.Info("App",
                $"[设置] 已应用持久化设置：bgm={bgm:0.00} sfx={sfx:0.00} fullscreen={fullscreen} quality={quality}");
        }

        /// <summary>把 `video/quality` 应用到引擎档位（口径同 `UI/SettingsPanel`；无档位 / 越界钳制 / 应用失败 / 读回不一致 一律 Warn）。返回引擎最终档位，`-1` = 未应用。</summary>
        private static int ApplyStoredQuality()
        {
            var max = QualitySettings.names.Length - 1;
            if (max < 0) { Game.Logger.Warn("App", "[设置] 引擎没有质量档位（QualitySettings.names 为空）⇒ video/quality 不应用"); return -1; }

            var ceiling = Mathf.Min(max, MaxQualityLevel);
            var stored = Game.Setting.Get<int>(SettingKeyQuality, Mathf.Clamp(QualitySettings.GetQualityLevel(), 0, ceiling));
            var level = Mathf.Clamp(stored, 0, ceiling);
            if (level != stored) Game.Logger.Warn("App", $"[设置] video/quality={stored} 超出可用档位 0..{ceiling}（引擎 {QualitySettings.names.Length} 档）⇒ 钳制为 {level}");

            try { QualitySettings.SetQualityLevel(level, false); }
            catch (System.Exception e) { Game.Logger.Warn("App", $"[设置] 应用画质档位 {level} 失败：{e.Message}"); return -1; }

            var actual = QualitySettings.GetQualityLevel();
            if (actual != level) Game.Logger.Warn("App", $"[设置] 画质未生效：期望档位={level}，引擎实际读回={actual}");
            Game.Logger.Info("App", $"[设置] 画质已应用：video/quality={stored} → QualitySettings.SetQualityLevel({level})（引擎现读回 {actual}）");
            return actual;
        }
    }
}
