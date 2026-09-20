// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · UI/SettingsPanel.cs
// 站点：MainMenu / Pause 的**子面板**。层：Popup。预制体：`Resources/UI/SettingsPanel`。
//
// ★ agent-15 §A（保真口径）：
//   · 原版 **没有** options 屏的 prefab（`Prefabs/` 下只有 `Menu/` 四个文件 + 游戏内面板）
//     ⇒ 本面板的"框"是本项目新增，但**一切尺寸/间距都走原版量纲**：按钮一律用原版
//     `MediumButton`(128×35)、± 用原版按钮行高 35×35、行节奏 = 原版 35+10 = 45 原版px
//     （见 `UI/UiLayoutFlow.cs` 的 `Settings` 组，注释里逐条写了推导）。
//   · 英文/数字（OPTIONS / 音量数值 / ON·OFF / CLOSE）走**原版位图字体**；
//     中文（选项名）走原版位图字模（片 3：`D2/Fonts/font16_chi`）；色调 = 原版亮度（白）。
//
// 职责（验收表 #42「暂停与设置」）：选项**真能改**且**重进后仍在**：
//   · BGM / 音效音量 → `Game.Sound.SetVolume` + 写 `Game.Setting` + `Save()`
//   · 全屏          → `Screen.fullScreen` + 写 `Game.Setting`
//   · 画质          → `QualitySettings.SetQualityLevel` + 写 `Game.Setting`
//   改完立刻 `Game.Setting.Save()`（落盘 settings.json）⇒ 重进仍在。
// ⛔ **没有**「方向键移动开关」那一行：原版 D2 只有鼠标点地面移动 ⇒ 该行与它读的设置项一并删除
//   （全局 skill §0「A 没有 ⇒ 不加」；验收表 U-1 / 本项目 bug 表 B35）。
// 打开/关闭由兄弟面板直接 `Game.UI.Open/Close<SettingsPanel>()`（菜单类站点共用一个 UI 场景），
// 也支持 ESC 关闭。⛔ 不引用任何业务模块。
// ─────────────────────────────────────────────────────────────────────────────

using CloverEngine;
using Diablo2.Core;
using Diablo2.Def;
using UnityEngine;
using UnityEngine.UI;

namespace Diablo2.UI
{
    /// <summary>选项面板（音量 / 全屏 / 画质），设置真改真存。</summary>
    public class SettingsPanel : UIPanel
    {
        private const float VolumeStep = 0.1f;

        /// <summary>文案（英文 = 原版位图字体；中文 = 回退默认字体）。</summary>
        private static class Text
        {
            public const string Title = "OPTIONS";
            public const string On = "ON";
            public const string Off = "OFF";
            public const string Close = "CLOSE";
            public const string Bgm = "背景音乐";
            public const string Sfx = "音效";
            public const string Fullscreen = "全屏显示";
            public const string Quality = "画质";
            public const string Foot = "设置写入 Game.Setting，重进仍在。";
        }

        /// <summary>
        /// 画质档位的设置键（`Game.Setting`）。
        /// <para>⚠️ 为什么键名写在这里而不是 `Core/GameConst.cs`：`Core/` 是冻结层，
        /// 新增设置键不许改它（本轮 `agent-a2` 的越界说明见回报）。键名沿用既有口径 `video/{项}`
        /// ——与 `GameConst.SettingKeyFullscreen` = `"video/fullscreen"` 同构。</para>
        /// </summary>
        private const string KeyQuality = "video/quality";

        /// <summary>画质档位的按钮文案（拉丁串 ⇒ 走原版位图字体，与 ON/OFF 同口径）。</summary>
        private static readonly string[] QualityLabels = { "LOW", "MED", "HIGH" };

        /// <summary>画质档位的中文名（只进日志，不进画面）。</summary>
        private static readonly string[] QualityNames = { "低", "中", "高" };

        private UiLayoutFlow.FlowLabel _bgmText;
        private UiLayoutFlow.FlowLabel _sfxText;
        private Image _bgmBar;
        private Image _sfxBar;
        private UiLayoutFlow.FlowButton _fullscreenButton;
        private UiLayoutFlow.FlowButton _qualityButton;

        /// <summary>屏适配容器（`UiLayoutFlow.FitRoot` 的产物；行内构件都挂在它下面）。</summary>
        private Transform _screen;

        private bool _built;

        private float _bgm;
        private float _sfx;
        private bool _fullscreen;
        private int _quality;

        /// <summary>
        /// 层：<see cref="UILayer.Top"/>。
        /// <para>★ 本轮（agent-a2 · 验收 #42）实测修掉一个真缺陷：原为 <see cref="UILayer.Popup"/>
        /// （= 引擎层序 Normal=1 &lt; Popup=2 &lt; Top=3 &lt; System=4，见
        /// `clover-client-unity-engine/Runtime/Core/PresentationContracts.cs:20-27`），
        /// 而**暂停菜单 `PausePanel` 在 Top** ⇒ 从暂停菜单点「OPTIONS」打开的选项面板**整块被暂停菜单压住**：
        /// 鼠标点到的是暂停菜单那一层（选项面板上的控件既收不到点击，点到的位置还会误触
        /// 暂停菜单的 `SAVE & EXIT` / `MAIN MENU`）—— 表现就是「选项真能改」这条根本不成立，
        /// 实测：点画质那一行直接把整局打回主菜单（`a2_plan_full.txt` 17:27:10 `fsm=MainMenu`）。</para>
        /// <para>改成 Top 后（与 `PausePanel` 同层、它在暂停菜单之后创建 ⇒ 兄弟序在后 ⇒ 在上），
        /// 选项面板拿到鼠标事件；`AppFlow.SweepStaleStationPanels` 按**类型**判断允许存在，不受影响。</para>
        /// </summary>
        public override UILayer Layer => UILayer.Top;

        /// <inheritdoc/>
        public override void OnOpen(object param)
        {
            UiArt.PrepareRoot(Root);
            Build();
            Load();
            Refresh();
            UiLayoutFlow.LogTable(nameof(SettingsPanel));
            Log.Info("Ui", $"选项面板打开：{DescribeCurrent()}");
        }

        /// <inheritdoc/>
        public override void OnUpdate(float dt)
        {
            // ESC 关闭（与 Flow 的 Stage 站点 ESC=暂停 约定：`AppFlow` 见到本面板已开就不抢 ESC）。
            if (Game.Input == null) return;
            if (!Game.Input.GetKeyDown(GameKey.Escape)) return;

            Log.Info("Ui", "ESC 关闭选项面板");
            Game.UI.Close<SettingsPanel>();
        }

        // ── 读 / 写设置 ──────────────────────────────────────────────────────

        private void Load()
        {
            // 默认值唯一来源 = `Cfg`（配置文件）；不存在则回退内置默认值（已 Warn）。
            _bgm = Mathf.Clamp01(Game.Setting.Get<float>(GameConst.SettingKeyBgmVolume, Cfg.BgmVolume));
            _sfx = Mathf.Clamp01(Game.Setting.Get<float>(GameConst.SettingKeySfxVolume, Cfg.SfxVolume));
            _fullscreen = Game.Setting.Get<bool>(GameConst.SettingKeyFullscreen, Cfg.Fullscreen);

            // 画质：缺项默认取引擎当前档位（前一次会话存过的值优先 ⇒ 「重进仍在」）。
            var engineLevel = QualitySettings.GetQualityLevel();
            var fallback = Mathf.Clamp(engineLevel, 0, QualityLabels.Length - 1);
            _quality = Mathf.Clamp(Game.Setting.Get<int>(KeyQuality, fallback), 0, QualityLabels.Length - 1);

            // 读到的值当场应用到引擎（否则"改过又重进"时画面仍是引擎默认档 ⇒ 设置等于没生效）
            ApplyQualityToEngine();

            Log.Info("Ui", $"[设置] 画质读自 Game.Setting[\"{KeyQuality}\"] = {_quality}"
                + $"（{QualityNames[_quality]}；引擎 QualitySettings.GetQualityLevel()={engineLevel}，"
                + $"可用档位 {QualitySettings.names.Length} 档）");
        }

        /// <summary>把 <see cref="_quality"/> 应用到引擎（档位越界 ⇒ 钳制并 Warn，不静默）。</summary>
        private void ApplyQualityToEngine()
        {
            var max = QualitySettings.names.Length - 1;
            if (max < 0)
            {
                Log.Warn("Ui", "[设置] 引擎没有质量档位（QualitySettings.names 为空）⇒ 画质设置只落盘、不应用");
                return;
            }

            var level = Mathf.Clamp(_quality, 0, Mathf.Min(max, QualityLabels.Length - 1));
            if (level != _quality)
            {
                Log.Warn("Ui", $"[设置] 画质档位 {_quality} 超出引擎档位范围 0..{max} ⇒ 钳制为 {level}（设置值仍按原样落盘）");
                return;
            }

            try
            {
                QualitySettings.SetQualityLevel(level, false);
            }
            catch (System.Exception e)
            {
                Log.Warn("Ui", $"[设置] 应用画质档位 {level} 失败：{e.Message}（设置值已保存，下次启动生效）");
            }
        }

        private void Refresh()
        {
            if (_bgmText != null) _bgmText.SetText(_bgm.ToString("0.00"));
            if (_sfxText != null) _sfxText.SetText(_sfx.ToString("0.00"));
            UiArt.SetBarRatio(_bgmBar, _bgm);
            UiArt.SetBarRatio(_sfxBar, _sfx);
            if (_fullscreenButton != null) _fullscreenButton.SetText(_fullscreen ? Text.On : Text.Off);
            if (_qualityButton != null) _qualityButton.SetText(QualityLabelOf(_quality));
        }

        /// <summary>档位 → 按钮文案（越界 ⇒ 打 Warn 并退回最低档，不静默）。</summary>
        private static string QualityLabelOf(int level)
        {
            if (level < 0 || level >= QualityLabels.Length)
            {
                Log.WarnOnce("Ui", "settings.quality.range",
                    $"画质档位 {level} 越界（合法 0..{QualityLabels.Length - 1}）⇒ 按钮按最低档显示");
                return QualityLabels[0];
            }
            return QualityLabels[level];
        }

        private void OnBgmDelta(float delta)
        {
            _bgm = Mathf.Clamp01(_bgm + delta);
            ApplyAndPersist();
            Log.Info("Ui", $"[设置] BGM 音量 = {_bgm:0.00} → Game.Sound.SetVolume(BGM) + 写 \"{GameConst.SettingKeyBgmVolume}\" 并 Save()");
        }

        private void OnSfxDelta(float delta)
        {
            _sfx = Mathf.Clamp01(_sfx + delta);
            ApplyAndPersist();
            Log.Info("Ui", $"[设置] 音效音量 = {_sfx:0.00} → Game.Sound.SetVolume(SFX) + 写 \"{GameConst.SettingKeySfxVolume}\" 并 Save()");
        }

        private void OnToggleFullscreen()
        {
            _fullscreen = !_fullscreen;
            Game.Setting.Set(GameConst.SettingKeyFullscreen, _fullscreen);
            try
            {
                Screen.fullScreen = _fullscreen;
            }
            catch (System.Exception e)
            {
                Log.Warn("Ui", $"[设置] 应用全屏失败（{_fullscreen}）：{e.Message}（设置值已保存，下次启动生效）");
            }

            Game.Setting.Save();
            Refresh();
            Log.Info("Ui", $"[设置] 全屏 = {_fullscreen} → 写 \"{GameConst.SettingKeyFullscreen}\" 并 Save()");
        }

        /// <summary>
        /// 画质档位循环（LOW → MED → HIGH → LOW）：应用引擎 + 写 <see cref="KeyQuality"/> + 落盘。
        /// </summary>
        private void OnCycleQuality()
        {
            _quality = (_quality + 1) % QualityLabels.Length;
            Game.Setting.Set(KeyQuality, _quality);
            Game.Setting.Save();
            ApplyQualityToEngine();
            Refresh();
            Log.Info("Ui", $"[设置] 画质 = {QualityLabels[_quality]}（{QualityNames[_quality]}，档位 {_quality}）"
                + $" → QualitySettings.SetQualityLevel({_quality}) + 写 \"{KeyQuality}\" 并 Save()"
                + $"（引擎现读回 {QualitySettings.GetQualityLevel()}）");
        }

        /// <summary>把音量应用到引擎音频 + 落盘 + 通知 Audio 模块。</summary>
        private void ApplyAndPersist()
        {
            if (Game.Sound == null)
            {
                Log.WarnOnce("Ui", "settings.no_sound", "Game.Sound 未挂载，音量只写入设置、未应用到引擎");
            }
            else
            {
                Game.Sound.SetVolume(SoundGroup.BGM, _bgm);
                Game.Sound.SetVolume(SoundGroup.SFX, _sfx);
            }

            Game.Setting.Set(GameConst.SettingKeyBgmVolume, _bgm);
            Game.Setting.Set(GameConst.SettingKeySfxVolume, _sfx);
            Game.Setting.Save();

            Game.Event.Emit(Events.VolumeChanged, new AudioVolumeArgs
            {
                bgm = _bgm,
                sfx = _sfx,
                bgmMute = _bgm <= 0f,
                sfxMute = _sfx <= 0f,
            });

            Refresh();
        }

        private string DescribeCurrent()
        {
            return $"bgm={_bgm:0.00} sfx={_sfx:0.00} fullscreen={_fullscreen}"
                 + $" quality={QualityLabels[Mathf.Clamp(_quality, 0, QualityLabels.Length - 1)]}";
        }

        // ── 构建 ────────────────────────────────────────────────────────────

        private void Build()
        {
            if (_built) return;
            _built = true;

            UiArt.FullPanel(transform, "Shade", UiArt.Overlay, true);

            // 屏适配容器（选项屏内容都在原版 ±225 内 ⇒ 系数 = 1，即纯 ×1.8；与其他屏同一套口径）
            var screen = UiLayoutFlow.FitRoot(transform, UiLayoutFlow.FitMenu);
            _screen = screen;

            UiArt.Panel(screen, "Box", UiLayoutFlow.Settings.BoxSize, UiLayoutFlow.Settings.BoxPos,
                UiArt.PanelBg, true);

            UiLayoutFlow.FlowLabel.Create(screen, "Title", Text.Title, D2Text.D2Font.Font24,
                TextAnchor.MiddleCenter, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.Settings.TitleSize),
                UiLayoutFlow.Settings.TitlePos);

            // 背景音乐
            BuildVolumeRow(0, Text.Bgm, UiLayoutFlow.Settings.Row1Y,
                new Color(0.14f, 0.12f, 0.11f, 1f), UiArt.AccentColor, out _bgmBar, out _bgmText);

            // 音效
            BuildVolumeRow(1, Text.Sfx, UiLayoutFlow.Settings.Row2Y,
                new Color(0.14f, 0.12f, 0.11f, 1f), new Color(0.30f, 0.55f, 0.85f, 1f), out _sfxBar, out _sfxText);

            // 全屏
            BuildToggleRow(Text.Fullscreen, UiLayoutFlow.Settings.Row3Y, OnToggleFullscreen, out _fullscreenButton);

            // 画质（档位循环：原版 Video Options 的画质开关按同一行节奏排在这里）
            // ⛔ 原先这里还有一行「方向键移动开关」，已按原版整体删除（验收表 U-1）⇒
            //    画质行接在「全屏」下一行（第 4 行，行节奏 45），面板不留空行。
            BuildToggleRow(Text.Quality, UiLayoutFlow.Settings.Row4Y, OnCycleQuality, out _qualityButton);

            UiLayoutFlow.FlowButton.Create(screen, "Close", Text.Close, UiLayoutFlow.MediumButtonOrig,
                UiLayoutFlow.Settings.ClosePos, () =>
                {
                    Log.Info("Ui", "选项面板：关闭");
                    Game.UI.Close<SettingsPanel>();
                });

            UiLayoutFlow.FlowLabel.Create(screen, "Foot", Text.Foot, D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, new Color(0.66f, 0.63f, 0.58f, 1f),
                UiLayoutFlow.Orig(UiLayoutFlow.Settings.FootSize), UiLayoutFlow.Settings.FootPos);
        }

        /// <summary>一行音量：中文标签 + 「−」+ 数值（位图字体）+ 「+」+ 锚点宽度条。</summary>
        private void BuildVolumeRow(int index, string label, float y, Color track, Color fill,
            out Image bar, out UiLayoutFlow.FlowLabel value)
        {
            UiLayoutFlow.FlowLabel.Create(_screen, "Label" + index, label, D2Text.D2Font.Font16,
                TextAnchor.MiddleRight, UiArt.TextColor, UiLayoutFlow.Orig(UiLayoutFlow.Settings.LabelSize),
                new Vector2(UiLayoutFlow.Settings.LabelPos.x, y));

            // ± 用原版按钮行高做边长（35×35 原版px）；底图仍是原版中等按钮帧
            UiLayoutFlow.FlowButton.Create(_screen, "Minus" + index, "-", new Vector2(35f, 35f),
                new Vector2(UiLayoutFlow.Settings.MinusPos.x, y),
                () => ApplyVolumeDelta(index, -VolumeStep));

            value = UiLayoutFlow.FlowLabel.Create(_screen, "Value" + index, "0.00", D2Text.D2Font.Font16,
                TextAnchor.MiddleCenter, Color.white, UiLayoutFlow.Orig(UiLayoutFlow.Settings.ValueSize),
                new Vector2(UiLayoutFlow.Settings.ValuePos.x, y));

            UiLayoutFlow.FlowButton.Create(_screen, "Plus" + index, "+", new Vector2(35f, 35f),
                new Vector2(UiLayoutFlow.Settings.PlusPos.x, y),
                () => ApplyVolumeDelta(index, VolumeStep));

            bar = UiArt.ProgressBar(_screen, "Bar" + index, UiLayoutFlow.Settings.BarSize,
                new Vector2(UiLayoutFlow.Settings.BarPos.x, y + UiLayoutFlow.Settings.BarPos.y), track, fill);
        }

        /// <summary>音量加减的唯一入口（index: 0=BGM / 1=SFX）。</summary>
        private void ApplyVolumeDelta(int index, float delta)
        {
            if (index == 0) OnBgmDelta(delta);
            else if (index == 1) OnSfxDelta(delta);
            else Log.Warn("Ui", $"ApplyVolumeDelta 收到未知行下标 {index} ⇒ 忽略（期望 0=BGM / 1=SFX）");
        }

        /// <summary>一行开关：中文标签 + 原版中等按钮（ON / OFF，位图字体）。</summary>
        private void BuildToggleRow(string label, float y, System.Action onClick,
            out UiLayoutFlow.FlowButton button)
        {
            UiLayoutFlow.FlowLabel.Create(_screen, label + "Label", label, D2Text.D2Font.Font16,
                TextAnchor.MiddleRight, UiArt.TextColor, UiLayoutFlow.Orig(UiLayoutFlow.Settings.LabelSize),
                new Vector2(UiLayoutFlow.Settings.LabelPos.x, y));

            button = UiLayoutFlow.FlowButton.Create(_screen, label + "Toggle", Text.Off,
                UiLayoutFlow.MediumButtonOrig, new Vector2(UiLayoutFlow.Settings.TogglePos.x, y), onClick);
        }
    }
}
