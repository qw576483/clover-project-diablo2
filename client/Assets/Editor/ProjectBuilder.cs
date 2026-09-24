// ============================================================================================
//
//  菜单：`Diablo2/一键生成工程（场景 + 预制体 + BuildSettings）`
//         `Diablo2/重建工程资产（覆盖已有场景与预制体）`
//
//  干什么：
//    ① 为 17 个面板类各生成一个**空壳预制体** `Assets/Resources/UI/{类名}.prefab`
//       —— 根节点 = 面板组件 + 铺满父层的 RectTransform（anchorMin=0/anchorMax=1/pivot=0.5/
//       offsetMin=offsetMax=0）；**面板内容不在预制体里摆**，由脚本 `OnOpen` 用 `UIFactory` 搭。
//    ② 生成三个场景 `Assets/Scenes/{Boot,Menu,Stage}.unity`（内容见下）
//    ③ 写 Build Settings：`Boot`(0) → `Menu`(1) → `Stage`(2)
//
//  幂等（硬要求）：
//    · 预制体/场景**已存在就跳过**（绝不覆盖用户手改过的资产），只在「缺失 / 组件丢了 / 文件坏了」时才重建；
//    · Build Settings 每次按 Boot→Menu→Stage 重排（顺序固定，不重复加）；
//    · 每次运行打一条分组汇总（新建 N / 跳过 M），**重复运行日志条数大幅下降**（天然幂等自证）。
//
//  `[InitializeOnLoadMethod]` 自愈：编辑器每次启动时做一次**廉价体检**
//    （`AssetDatabase.LoadAssetAtPath` 返回 null = 文件缺失或序列化坏掉；预制体上取不到面板组件 = 引用坏了），
//    只有在体检不通过时才重建那一项。这样「用户打开工程点 Play」不必先手动点菜单。
//    体检不通过才动手，所以正常情况下**一条日志都不打**。
//
//  为什么用「按类名反射找类型」而不是直接 `typeof(BootPanel)`：
//    本文件在 `Assembly-CSharp-Editor` 里，直接引用业务类型会让「业务程序集编译失败 ⇒
//
//  本文件不动 `Assets/Scripts/**`、不动 `Packages/manifest.json`、不动契约文档。
// ============================================================================================

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diablo2.Editor
{
    /// <summary>一键生成场景 / 面板预制体 / Build Settings（幂等、可自愈、可命令行调用）。</summary>
    public static class ProjectBuilder
    {
        /// <summary>日志前缀（契约：每个生成动作都带它，便于在 Editor.log 里 grep）。</summary>
        private const string Tag = "[D2.ProjectBuilder]";

        public const string MenuPath = "Diablo2/一键生成工程（场景 + 预制体 + BuildSettings）";

        /// <summary>强制重建菜单（覆盖已有资产；只给「手工修坏了想复原」用）。</summary>
        public const string MenuPathForce = "Diablo2/重建工程资产（覆盖已有场景与预制体）";

        /// <summary>面板图层（Unity 内置层 UI = 5）。</summary>
        private const int UiLayer = 5;

        /// <summary>
        /// 是否打印「逐项跳过」明细（默认关：重复运行时日志条数才能明显下降 ⇒ 幂等自证）。
        /// 刻意用 <c>static readonly</c> 而不是 <c>const</c>：const=false 会让编译器把下面的
        /// 日志分支判成「无法访问的代码」（CS0162）。
        /// </summary>
        private static readonly bool VerboseSkipLog = false;

        /// <summary>场景里的主相机参数（与 `Module/Camera/CameraRig.cs` 对齐：正交 / 尺寸 6 / z=-10）。</summary>
        private const float CameraOrthoSize = 6f;

        /// <summary>相机到地面（z=0）的距离 ⇒ 机位 z = -10（`CameraRig.CameraDistance`）。</summary>
        private const float CameraDistance = 10f;

        /// <summary>17 个面板类名（顺序无意义，只影响生成顺序）。</summary>
        public static readonly string[] PanelNames =
        {
            "BootPanel", "MainMenuPanel", "SettingsPanel", "CharSelectPanel", "CharCreatePanel",
            "LoadingPanel", "PausePanel",
            // 本轮新增：**原版风格的二次确认弹窗**（替掉引擎默认 uGUI 弹窗；见 `UI/D2ConfirmPanel.cs`）。
            //   它是弹窗、不属任何 FSM 站点，但同样要一个 `Resources/UI/{类名}` 空壳预制体
            //   （`UIManager.Open<T>` 按类名加载，见 `Runtime/Presentation/UI.cs:131-140`）。
            "D2ConfirmPanel",
            "HudPanel", "MiniMapPanel", "InventoryPanel", "CharacterPanel", "SkillTreePanel",
            "QuestLogPanel", "NpcDialogPanel", "ShopPanel", "DeathPanel",
            //   与 `D2ConfirmPanel` 同理：不属 FSM 站点，但同样要一个 `Resources/UI/{类名}` 空壳预制体
            //   （`UIManager.Open<T>` 按类名加载）。它是**游戏内面板**（层 = Popup），由
            //   `App/AppWaypoint.cs` 在"走到传送点"后打开。
            "WaypointPanel",
        };

        /// <summary>预制体目录（契约）。</summary>
        public const string PanelsDir = "Assets/Resources/UI";

        /// <summary>场景目录（契约）。</summary>
        public const string ScenesDir = "Assets/Scenes";

        /// <summary>场景名（**数组顺序 = Build Index**，Boot 必须为 0）。</summary>
        public static readonly string[] SceneOrder = { "Boot", "Menu", "Stage" };

        /// <summary>唯一手动挂载的业务脚本（Boot 场景里那个 GameObject 用它）。</summary>
        public const string BootstrapTypeName = "Diablo2.App.Bootstrap";

        /// <summary>会话内「自愈体检」只跑一次的标记键。</summary>
        private const string SessionKey = "D2.ProjectBuilder.AutoCheck";

        // ═════════════════════════════════════════════════════════════════════════════════════
        //  菜单入口
        // ═════════════════════════════════════════════════════════════════════════════════════

        /// <summary>一键生成：已存在的资产一律跳过（不覆盖用户手改）。</summary>
        [MenuItem(MenuPath, false, 1)]
        public static void GenerateAll()
        {
            Run(false);
        }

        /// <summary>强制重建：先删掉已有预制体/场景再生成（用于「被改坏了想复原」）。</summary>
        [MenuItem(MenuPathForce, false, 2)]
        public static void RebuildAll()
        {
            if (!EditorUtility.DisplayDialog(
                    "重建工程资产",
                    "将删除并重建 3 个场景与 17 个面板预制体（你手改过的样式会丢）。\n\n确定继续？",
                    "重建", "取消"))
                return;

            Run(true);
        }

        /// <summary>菜单可用性：正在播放或正在编译时不许动资产。</summary>
        [MenuItem(MenuPath, true)]
        [MenuItem(MenuPathForce, true)]
        private static bool Validate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        /// <summary>
        /// 供 `-executeMethod` 用的命令行入口：**失败以非零退出码结束**（便于 CI 判定）。
        /// 用法：`Unity.exe -batchmode -quit -projectPath &lt;client&gt; -executeMethod Diablo2.Editor.ProjectBuilder.GenerateFromCommandLine`
        /// </summary>
        public static void GenerateFromCommandLine()
        {
            int errors = 1;
            try
            {
                errors = Run(true, false);
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} 命令行生成抛异常：{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }

            if (errors > 0)
            {
                Debug.LogError($"{Tag} 生成失败：{errors} 个错误 ⇒ 以退出码 1 结束");
                Exit(1);
                return;
            }

            Debug.Log($"{Tag} 生成完成：无错误");
            Exit(0);
        }

        /// <summary>批处理模式下才真的退出编辑器（交互模式下 `Exit` 会把编辑器关掉）。</summary>
        private static void Exit(int code)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }

        // ═════════════════════════════════════════════════════════════════════════════════════
        //  编辑器启动自愈体检（只在缺东西/坏东西时动手）
        // ═════════════════════════════════════════════════════════════════════════════════════
        /// <summary>
        /// 每次编辑器启动做一次体检：缺资产 / 预制体上没有面板组件 / 场景文件坏了 ⇒ 补建那一项。
        /// 刻意用 `delayCall`：等首次导入与资源库稳定后再动文件。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoCheckOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                if (SessionState.GetBool(SessionKey, false))
                    return; // 同一次编辑器会话只体检一次
                SessionState.SetBool(SessionKey, true);

                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                    return;

                try
                {
                    int fixedCount = Run(false, true);
                    if (fixedCount > 0)
                        Debug.Log($"{Tag} 启动自愈：补建/补写了 {fixedCount} 项（缺失或损坏的资产 / 编辑器设置）；"
                                  + $"想手动重来请点菜单「{MenuPath}」");
                }
                catch (Exception e)
                {
                    // 自愈失败不能影响编辑器启动：只报错，用户可手动点菜单
                    Debug.LogError($"{Tag} 启动自愈失败（可手动点菜单「{MenuPath}」重来）：{e.GetType().Name}: {e.Message}");
                }
            };
        }

        // ═════════════════════════════════════════════════════════════════════════════════════
        //  主流程
        // ═════════════════════════════════════════════════════════════════════════════════════

        /// <summary>跑完整流程；返回「错误个数」（0 = 全部 OK）。</summary>
        /// <param name="force">true = 删掉已有资产重建；false = 已存在就跳过。</param>
        /// <param name="quiet">true = 自愈模式：没动手就不打日志。</param>
        private static int Run(bool force, bool quiet = false)
        {
            int errors = 0;

            // ① 「会落盘资产」的写入（决定要不要 SaveAssets/Refresh）
            int assetsChanged = 0;
            assetsChanged += BuildPanelPrefabs(force, quiet, ref errors);
            assetsChanged += BuildScenes(force, quiet, ref errors);
            assetsChanged += ApplyBuildSettings(quiet);

            // ② 「点 Play 就能跑」的最后一块：把 Play 模式起始场景指向 Boot。
            //    否则用户打开工程时停在哪一幕（模板的 SampleScene 等），▶ Play 就从那一幕跑 ——
            //    而 `Bootstrap`（唯一入口）只在 Boot 场景里 ⇒ 看上去"点 Play 没反应"。
            //    自愈路径（quiet）只在为空时设置，绝不覆盖用户自己选的起始场景。
            //    它只是编辑器设置（不落资产），所以**不触发** Refresh。
            int settingsChanged = EnsurePlayModeStartScene(quiet);

            int changed = assetsChanged + settingsChanged;
            if (!quiet || changed > 0)
            {
                Debug.Log($"{Tag} 汇总：本次实际写入 {changed} 项（资产 {assetsChanged} / 编辑器设置 {settingsChanged}），"
                          + $"错误 {errors} 项"
                          + (force ? "（强制重建模式）" : "（幂等模式：已存在的一律跳过）"));
            }

            if (assetsChanged > 0)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return errors;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  ① 面板预制体
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>生成 17 个面板空壳预制体。</summary>
        private static int BuildPanelPrefabs(bool force, bool quiet, ref int errors)
        {
            if (!AssetDatabase.IsValidFolder(PanelsDir))
            {
                AssetDatabase.CreateFolder("Assets/Resources", "UI");
                Debug.Log($"{Tag} 创建目录 {PanelsDir}");
            }

            int created = 0, skipped = 0;
            for (int i = 0; i < PanelNames.Length; i++)
            {
                string name = PanelNames[i];
                string path = PanelsDir + "/" + name + ".prefab";

                if (!force && PrefabLooksGood(path, name))
                {
                    skipped++;
                    if (VerboseSkipLog)
                        Debug.Log($"{Tag} 跳过已存在的预制体：{path}");
                    continue;
                }

                if (!CreatePanelPrefab(path, name))
                {
                    errors++; // CreatePanelPrefab 内部已 LogError
                    continue;
                }

                created++;
                Debug.Log($"{Tag} 生成面板预制体：{path}");
            }

            if (!quiet || created > 0)
                Debug.Log($"{Tag} 面板预制体：新建 {created} / 跳过 {skipped}（共 {PanelNames.Length}）");
            return created;
        }

        /// <summary>
        /// 预制体体检：文件能加载 **且** 根节点上真的挂着该面板组件。
        /// （只看"文件在不在"不够：脚本 GUID 变了/组件丢了时文件仍能加载，但运行期 `Game.UI.Open&lt;T&gt;`
        /// 会打 `Component xxx not found on prefab` 然后面板打不开。）
        /// </summary>
        private static bool PrefabLooksGood(string path, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                return false; // 缺失，或 YAML 坏掉（解析失败 → null）

            var type = FindPanelType(name);
            if (type == null)
                return true; // 类型找不到：交给 CreatePanelPrefab 去报错，不要在这里判定"坏"
            return prefab.GetComponent(type) != null;
        }

        /// <summary>造一个面板空壳预制体：铺满的 RectTransform + 面板组件。</summary>
        private static bool CreatePanelPrefab(string path, string name)
        {
            var type = FindPanelType(name);
            if (type == null)
            {
                // 非预期分支：一个 .cs 里放多个 MonoBehaviour / 类改名都会走到这里 ⇒ 点名并继续
                Debug.LogError($"{Tag} 找不到面板类型 {name}（应为一个 .cs 一个 MonoBehaviour）：{path} 未生成");
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                AssetDatabase.DeleteAsset(path); // 强制重建 / 自愈：先删旧的

            var go = new GameObject(name, typeof(RectTransform));
            try
            {
                go.layer = UiLayer;
                var rt = go.GetComponent<RectTransform>();
                // 契约：根节点铺满父层（anchorMin=0 / anchorMax=1 / pivot=0.5 / offsetMin=offsetMax=0）。
                // 注：RectTransform 的序列化字段是 m_AnchoredPosition + m_SizeDelta，
                //     offsetMin/offsetMax 是它们的**导出属性**：anchoredPosition=0 & sizeDelta=0 ⇔ offsetMin=offsetMax=0。
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;

                go.AddComponent(type);

                bool ok;
                PrefabUtility.SaveAsPrefabAsset(go, path, out ok);
                if (!ok)
                {
                    Debug.LogError($"{Tag} 保存预制体失败：{path}");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} 生成预制体 {path} 抛异常：{e.GetType().Name}: {e.Message}");
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go); // 临时物体不要留在当前场景里
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  ② 场景
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>生成 Boot / Menu / Stage 三个场景。</summary>
        private static int BuildScenes(bool force, bool quiet, ref int errors)
        {
            if (!AssetDatabase.IsValidFolder(ScenesDir))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
                Debug.Log($"{Tag} 创建目录 {ScenesDir}");
            }

            int created = 0;
            for (int i = 0; i < SceneOrder.Length; i++)
            {
                string name = SceneOrder[i];
                string path = ScenePath(name);

                if (!force && SceneLooksGood(path))
                {
                    if (VerboseSkipLog)
                        Debug.Log($"{Tag} 跳过已存在的场景：{path}");
                    continue;
                }

                if (!CreateScene(path, name))
                {
                    errors++;
                    continue;
                }

                created++;
                Debug.Log($"{Tag} 生成场景：{path}");
            }

            if (!quiet || created > 0)
                Debug.Log($"{Tag} 场景：新建 {created} / 共 {SceneOrder.Length}（order = Build Index）");
            return created;
        }

        /// <summary>场景相对路径。</summary>
        public static string ScenePath(string sceneName)
        {
            return ScenesDir + "/" + sceneName + ".unity";
        }

        /// <summary>场景体检：能作为 SceneAsset 加载（= 文件存在且 YAML 能解析）。</summary>
        private static bool SceneLooksGood(string path)
        {
            return AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;
        }

        /// <summary>
        /// 造一个场景。用 **Additive**（不替换用户当前打开的场景，避免弹保存框），存完立刻关掉。
        ///   Boot  = 主相机 + `Bootstrap`（挂 `Diablo2.App.Bootstrap`）
        ///   Menu  = 主相机（纯 UI 场景，靠面板切换、不切场景）
        ///   Stage = 主相机 + Global Light 2D + `MapRoot` + `EntityRoot`
        /// **不放 Canvas**：引擎 `UIManager` 自己建常驻 Canvas（`Runtime/Presentation/UI.cs:44-58`，
        ///    `DontDestroyOnLoad` + 5 个层节点 + `referenceResolution=1920x1080`）；
        ///    场景里再放一个 ⇒ 两套 UI 根，面板会挂到错的那套上。
        /// **不放 EventSystem**：`CloverInput.Init()` 会 `EnsureEventSystem()`
        ///    （`Runtime/Presentation/CloverInput.cs:39`），场景里预置反而可能撞成两个 InputModule。
        /// </summary>
        private static bool CreateScene(string path, string sceneName)
        {
            try
            {
                // 强制重建 / 自愈：先删旧文件（真场景由 SceneScaffold 内部 SaveScene 覆盖写）
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                    AssetDatabase.DeleteAsset(path);

                //   相机参数 / 根节点 / 入口脚本 / URP 相机数据 / Additive 模式 / 存完关闭 全部经参数传入；
                //   本文件只保留**项目取值**（size=6、z=-10、黑底、各场景该有哪些节点、入口类型名）。
                //   ⇒ 场景结构改了要改的是这里的参数，不是再抄一份建场景代码。
                var o = new CloverEngine.Editor.SceneScaffoldOptions
                {
                    OrthoSize = CameraOrthoSize,
                    CameraZ = CameraDistance,
                    NearClip = 0.3f,
                    FarClip = 1000f,
                    SolidColorBackground = true,
                    ClearColor = Color.black,           // 原版暗黑：背景近黑，别用模板的天蓝
                    AddUrpCameraData = true,
                    AdditiveMode = true,                // 不替换用户当前打开的场景（避免弹保存框）
                    CloseAfterSave = true,
                    Create2DLight = sceneName == "Stage",
                    MapRootName = sceneName == "Stage" ? "MapRoot" : null,
                    EntityRootName = sceneName == "Stage" ? "EntityRoot" : null,
                    EntryTypeName = sceneName == "Boot" ? BootstrapTypeName : null,
                    ApplyBuildSettings = false,         // 由 ApplyBuildSettings() 统一写（③）
                    SetPlayModeStartScene = false       // 由 EnsurePlayModeStartScene() 统一设（④）
                };

                if (!CloverEngine.Editor.SceneScaffold.Create(path, o, out var err))
                {
                    Debug.LogError($"{Tag} 生成场景 {path} 失败：{err}");
                    return false;
                }

                Debug.Log($"{Tag} {sceneName} 场景已生成：{path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"{Tag} 生成场景 {path} 抛异常：{e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>在场景里造一个空 GameObject（返回它，供挂组件）。</summary>
        private static GameObject CreateEmpty(Scene scene, string name)
        {
            var go = new GameObject(name);
            EditorSceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        /// <summary>造主相机：正交 / 尺寸 6 / z=-10 / tag=MainCamera + AudioListener +（能拿到就加）URP 相机数据。</summary>
        private static void CreateMainCamera(Scene scene, string sceneName)
        {
            var go = CreateEmpty(scene, "Main Camera");
            go.tag = "MainCamera";

            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = CameraOrthoSize;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black; // 原版暗黑：背景近黑，别用模板的天蓝
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1000f;
            go.transform.localPosition = new Vector3(0f, 0f, -CameraDistance);

            go.AddComponent<AudioListener>();

            // URP 的相机附加数据：包程序集不保证被 Editor 预定义程序集自动引用 ⇒ 反射加，拿不到不算错
            // （URP 在渲染时会自己补一个 `UniversalAdditionalCameraData`）。
            AddComponentIfFound(go, "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false);
            Debug.Log($"{Tag} {sceneName} 场景主相机：正交 size={CameraOrthoSize} pos.z={-CameraDistance}"
                      + "（与 CameraRig 对齐；Camera.main 能命中，点击反投影才正确）");
        }

        /// <summary>
        /// 造 Global Light 2D。**必需**：项目用 URP 2D，精灵默认材质是 `Sprite-Lit-Default`，
        /// 没有全局 2D 光时世界里的瓦片/角色会渲染成一片黑（且不报错）。
        /// </summary>
        private static void CreateGlobalLight2D(Scene scene)
        {
            var go = CreateEmpty(scene, "Global Light 2D");
            if (AddComponentIfFound(go, "UnityEngine.Rendering.Universal.Light2D", true) == null)
                Debug.LogWarning($"{Tag} 未装上 Light2D（URP 包不在？）⇒ Stage 里的世界精灵可能全黑，请手动加一个 Global Light 2D");
        }

        /// <summary>按全名反射加组件；<paramref name="required"/> = true 时失败返回 null 供调用方告警。</summary>
        private static Component AddComponentIfFound(GameObject go, string fullName, bool required)
        {
            var t = FindTypeByName(fullName);
            if (t == null)
            {
                if (required)
                    Debug.LogWarning($"{Tag} 找不到类型 {fullName} ⇒ 未挂载");
                else
                    Debug.Log($"{Tag} 未找到可选组件 {fullName}（不影响生成，交给运行时自动补）");
                return null;
            }

            try
            {
                return go.AddComponent(t);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{Tag} 挂载 {fullName} 失败：{e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  ③ Build Settings
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 写 Build Settings：`Boot`(0) → `Menu`(1) → `Stage`(2)。
        /// **幂等**：每次都按同一顺序整体重写，不重复加、不乱序（`Game.Scene.Load(name)` 靠它找场景）。
        /// </summary>
        private static int ApplyBuildSettings(bool quiet)
        {
            var want = new List<string>();
            for (int i = 0; i < SceneOrder.Length; i++)
            {
                string path = ScenePath(SceneOrder[i]);
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogError($"{Tag} 场景缺失，Build Settings 不写它：{path}");
                    continue;
                }
                want.Add(path);
            }

            if (want.Count == 0)
                return 0;   // 一个场景都没生成 ⇒ 不动 Build Settings（引擎件也会拒空清单）

            // 写入 + 幂等判定都在引擎件里（`replace: true` = 结果只有这三个场景，与本文件原语义一致）
            if (CloverEngine.Editor.SceneScaffold.ApplyBuildSettings(want, replace: true))
            {
                Debug.Log($"{Tag} Build Settings 已写入：" + string.Join(" → ", want.ToArray()));
                return 1;
            }

            if (!quiet)
                Debug.Log($"{Tag} Build Settings 已正确（Boot→Menu→Stage），无需改动");
            return 0;
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        //  ④ Play 模式起始场景 = Boot
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// 把 `EditorSceneManager.playModeStartScene` 指向 `Boot`：这样**不管编辑器当前打开哪个场景**，
        /// 点 ▶ Play 都从 Boot 跑起（`Bootstrap` 是唯一入口，只在 Boot 里）。
        /// </summary>
        /// <param name="onlyIfNull">true = 只在当前为空时设置（自愈路径：不覆盖用户自己的选择）。</param>
        private static int EnsurePlayModeStartScene(bool onlyIfNull)
        {
            var path = ScenePath("Boot");
            var boot = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            if (boot == null)
                return 0; // 场景还没生成（不应该发生：上面刚生成过）——不报错，BuildScenes 已经点过名

            try
            {
                // 设置 + 幂等判定都在引擎件里；`onlyIfNull` = 自愈路径不覆盖用户自己的选择
                var before = EditorSceneManager.playModeStartScene;
                if (CloverEngine.Editor.SceneScaffold.SetPlayModeStartScene(path, onlyIfNull))
                {
                    Debug.Log($"{Tag} Play 模式起始场景 = {path}"
                              + "（点 ▶ Play 会从 Boot 启动；不需要手动切场景）");
                    return 1;
                }

                if (onlyIfNull && before != null && before != boot)
                {
                    Debug.Log($"{Tag} Play 模式起始场景已被指定为 " + before.name + " ⇒ 不覆盖"
                              + $"（想从 Boot 跑请点菜单「{MenuPath}」）");
                }
                return 0;
            }
            catch (Exception e)
            {
                // 非预期分支：设置起始场景失败不该让整体失败（用户仍可手动打开 Boot 再 Play）
                Debug.LogWarning($"{Tag} 设置 Play 模式起始场景失败（可手动打开 Assets/Scenes/Boot.unity 再 Play）："
                                 + $"{e.GetType().Name}: {e.Message}");
                return 0;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────────────────
        // ─────────────────────────────────────────────────────────────────────────────────────
        /// <summary>面板类：先试 `Diablo2.UI.{name}`，再全程序集按简单名兜底。</summary>
        private static Type FindPanelType(string simpleName)
        {
            var t = FindTypeByName("Diablo2.UI." + simpleName);
            if (t != null)
                return t;
            return FindTypeBySimpleName(simpleName);
        }

        /// <summary>按全名找类型（含已加载的全部程序集）。</summary>
        private static Type FindTypeByName(string fullName)
        {
            var t = Type.GetType(fullName);
            if (t != null)
                return t;

            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                try
                {
                    t = asms[i].GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue; // 反射-only 程序集/加载失败：跳过，不打断生成
                }
                if (t != null)
                    return t;
            }
            return null;
        }

        /// <summary>按简单名找类型（兜底：命名空间变了也还能找到）。</summary>
        private static Type FindTypeBySimpleName(string simpleName)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < asms.Length; i++)
            {
                Type[] types;
                try
                {
                    types = asms[i].GetTypes();
                }
                catch (Exception)
                {
                    continue; // 该程序集里存在无法加载的类型：跳过它继续找
                }

                for (int k = 0; k < types.Length; k++)
                {
                    if (string.Equals(types[k].Name, simpleName, StringComparison.Ordinal)
                        && typeof(MonoBehaviour).IsAssignableFrom(types[k]))
                        return types[k];
                }
            }
            return null;
        }
    }
}
