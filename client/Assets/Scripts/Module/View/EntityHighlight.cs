// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Module/View/EntityHighlight.cs   ★ u44（悬停选择表现 · 契约 C1/C2）
//
// 职责：把「鼠标悬停到某个实体」变成**该实体整体变亮**（原版 D2 的选中效果）。
//   ⛔ 不是描边 / 不是外框 / 不是换色块 —— 原版是**把整个精灵乘亮**。
//
// ── 出处（逐条，参考实现 `mofr/Diablerie`，源码不在本机、按 blob sha 取回复算过）──────
//   ① 触发：`MouseSelection.cs:82-95`（`HotEntity` setter ⇒ `entity.selected = true/false`）
//      → `Entity.cs:33-37`（`selected` ⇒ `_renderer.selected`）
//      → `COFRenderer.cs:51-64`（**逐图层**调 `Materials.SetRendererHighlighted`）。
//   ② 数值与手段：`Materials.cs:48-54`
//        renderer.GetPropertyBlock(block);
//        block.SetFloat("_Brightness", highlighted ? 3.0f : 1.0f);
//        block.SetFloat("_Contrast",   highlighted ? 1.01f : 1.0f);
//        renderer.SetPropertyBlock(block);
//      ⇒ 三个硬口径：**3.0 / 1.01 / 1.0**、属性名 `_Brightness` / `_Contrast`、
//        载体 **MaterialPropertyBlock**（⛔ 绝不用 `renderer.material` —— 那会克隆材质实例）。
//   ③ 视觉：`Sprite.shader:84-85`
//        o.color.rgb *= o.color.a * _Brightness;
//        o.color.rgb = (o.color.rgb - 0.5) * _Contrast + 0.5;
//      ⇒ 该着色器是本片新增的唯一资产：`Assets/Resources/Clover/Shaders/Sprite.shader`
//        （**逐字节搬运**，git-blob-sha1 = 204985028e18f0dbefc5dc94e7aa948e124a86e6）。
//
// ── 为什么"悬停时才换上自定义材质"（与参考实现的唯一差异，显式登记）─────────────
//   参考实现里**所有**实体图层常驻 `Materials.Normal`（= `new Material(Shader.Find("Sprite"))`），
//   高亮只改属性块。本工程原有实体用的是 Unity 默认精灵材质 ⇒ 若把自定义材质**永久**换上，
//   未悬停时的渲染路径就换了一套（本项目是 URP；默认精灵着色器与这份 legacy CG 着色器
//   只是"数学上等价"）⇒ **"未悬停必须与现在逐像素一致"这条就成了推论而不是保证**。
//   所以本文件的口径是：**只在悬停期间换上共享的自定义材质，移开立刻还原原材质**
//   （`EntityView.OriginalMaterial`，建节点时记下）——
//     · 未悬停 = 原材质 = **由构造保证**逐像素与改动前一致（不是"我算过它一样"）；
//     · 悬停 = 参考实现的着色器 + 3.0/1.01（副作用面最小：万一着色器在本机不兼容，
//       坏的只有"被悬停的那一个实体"，而不是全屏所有精灵）。
//   共享材质只 `new` 一次并被所有实体复用（`sharedMaterial` 赋值**不克隆**；
//   ⛔ `renderer.material` 才会克隆 —— 本文件与全仓都不许出现它）。
//
// ── 分层 ─────────────────────────────────────────────────────────────────────
//   本文件在 `Module/View`：只依赖 UnityEngine + 兄弟文件 `ViewLog`。
//   ⛔ 不引用 `Diablo2.UI`（UI 在 View 之上）、不引用任何别的 Module。
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

namespace Diablo2.Module.View
{
    /// <summary>实体整体的"悬停变亮"（原版 `Materials.SetRendererHighlighted` 的等价实现）。</summary>
    internal static class EntityHighlight
    {
        // ═════════════════════════════════════════════════════════════════════
        // 原版常量（出处见文件头 ②；⛔ 一个都不许改、不许"手感微调"）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>着色器名 —— 参考实现 `Materials.cs:39` 的 `Shader.Find("Sprite")`（逐字）。</summary>
        public const string ShaderName = "Sprite";

        /// <summary>亮度属性名（原版 `Materials.cs:51`）。</summary>
        public const string BrightnessProperty = "_Brightness";

        /// <summary>对比度属性名（原版 `Materials.cs:52`）。</summary>
        public const string ContrastProperty = "_Contrast";

        /// <summary>悬停亮度（原版 `Materials.cs:51` 的 `highlighted ? 3.0f : 1.0f` 前半）。</summary>
        public const float HoverBrightness = 3.0f;

        /// <summary>悬停对比度（原版 `Materials.cs:52`）。</summary>
        public const float HoverContrast = 1.01f;

        /// <summary>未悬停亮度（原版 `Materials.cs:51` 后半 = **恒等变换**）。</summary>
        public const float NormalBrightness = 1.0f;

        /// <summary>未悬停对比度（原版 `Materials.cs:52` 后半 = 恒等变换）。</summary>
        public const float NormalContrast = 1.0f;

        // ═════════════════════════════════════════════════════════════════════
        // 纯函数（离线宿主逐条断言；⛔ 表现层不许内联这些三元式）
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>该状态下的 `_Brightness`。</summary>
        public static float BrightnessFor(bool highlighted)
            => highlighted ? HoverBrightness : NormalBrightness;

        /// <summary>该状态下的 `_Contrast`。</summary>
        public static float ContrastFor(bool highlighted)
            => highlighted ? HoverContrast : NormalContrast;

        // ═════════════════════════════════════════════════════════════════════
        // 运行时
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>共享材质对象的名字（实机判据据此确认"挂的确实是我们这个材质"）。</summary>
        public const string SharedMaterialName = "D2EntityHighlight";

        private static MaterialPropertyBlock _block;
        private static Material _sharedMaterial;
        private static bool _shaderMissingLogged;
        private static bool _shaderMismatchLogged;

        /// <summary>
        /// 共享的自定义材质（第一次需要时建；**取到的着色器不具备两个属性 ⇒ 返回 null 并点名报错**）。
        /// <para>⚠️★ 收口（`select` §9-② 的静默失效风险）：`Shader.Find("Sprite")` **会撞名** ——
        /// URP 的精灵着色器族里也有名字里带 `Sprite` 的（本工程实体默认材质实测 =
        /// `Universal Render Pipeline/2D/Sprite-Lit-Default`），一旦取到**同名但没有这两个属性**的着色器，
        /// 属性块里的 `_Brightness`/`_Contrast` 会被**静默忽略**（画面 = 悬停毫无反应，且没有任何日志）。
        /// 同族坑见 `tools/ai-skill/constraints.md` #3 / `UI/UiBar.cs:5-15`（`Filled` + 空 sprite 静默无绘制）。</para>
        /// <para>⇒ 口径：① **先按 <see cref="ResourcePath"/> 从 Resources 取**（唯一）；② 退路才用
        /// `Shader.Find(<see cref="ShaderName"/>)`；③ **无论哪条路，都必须 `HasProperty` 校验两个属性**，
        /// 不过就**点名 Warn/Error 并不换材质**（宁可不亮，也不静默）。</para>
        /// </summary>
        private static Material SharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;

            // ⚠️ 取法**只能**是 `Shader.Find`：项目硬闸门 E1（`uicheck`「`Assets/Scripts` 里直连
            //   `Resources.Load*` 命中 0」）禁止业务代码直连 `Resources.Load*` —— 本片第 2 轮曾改用
            //   `Resources.Load<Shader>(<Resources 路径>)` 以求"唯一不撞名"，**该闸门立刻判红**
            //   （`EntityHighlight.cs:116,117`）⇒ 撤回，改回 `Shader.Find` + **强制属性校验**：
            //   撞名的后果从"静默不亮"变成"**点名报错**"（下面那个 `_shaderMismatchLogged` 分支）。
            //   实测依据：本片那次 Play 里 `shader=Sprite` 且 `_Brightness=3` 生效 ⇒ 本工程
            //   `Shader.Find("Sprite")` 命中的就是我们这份资产（不是 URP 的同族着色器）。
            var shader = Shader.Find(ShaderName);
            var via = "Shader.Find(\"" + ShaderName + "\")";

            if (shader == null)
            {
                // 非预期分支：资产缺失 / 没被包进 build（放在 Resources 目录下才会被包含）
                if (!_shaderMissingLogged)
                {
                    _shaderMissingLogged = true;
                    ViewLog.Error($"EntityHighlight：找不到着色器（`{via}` 返回 null）"
                        + "；应落在 `Assets/Resources/Clover/Shaders/Sprite.shader`"
                        + " ⇒ 本局悬停变亮不可用（用鼠标悬停怪物时不会有任何高亮，且不会有别的症状）");
                }
                return null;
            }

            // ⚠️ 校验必须走 **`Material.HasProperty`**：本工程 Unity 版本的可访问 API 里
            //   **`Shader` 没有 `HasProperty`**（实测 2026-09-24 由 `playercheck` 编译报
            //   `CS1061: "Shader" 未包含 "HasProperty" 的定义` ⇒ 换成先建材质再校验）。
            var mat = new Material(shader) { name = SharedMaterialName };
            var hasB = mat.HasProperty(BrightnessProperty);
            var hasC = mat.HasProperty(ContrastProperty);
            if (!hasB || !hasC)
            {
                // ★ 这是本片最要紧的"静默失效"分支：**取到了着色器、但它没有我们要的属性**
                if (!_shaderMismatchLogged)
                {
                    _shaderMismatchLogged = true;
                    ViewLog.Error($"EntityHighlight：取到的着色器**不具备高亮属性** ⇒ 悬停不会变亮（属性块会被静默忽略）。"
                        + $"取法={via}；shader.name=\"{shader.name}\"；"
                        + $"Material.HasProperty({BrightnessProperty})={(hasB ? 1 : 0)} "
                        + $"Material.HasProperty({ContrastProperty})={(hasC ? 1 : 0)}"
                        + "；检查 `Assets/Resources/Clover/Shaders/Sprite.shader` 是否被同名着色器顶掉"
                        + "（撞名同族坑：URP 的名字里带 `Sprite` 的那一族）");
                }
                UnityEngine.Object.Destroy(mat);          // 校验不过就别留着这个材质
                return null;
            }

            _sharedMaterial = mat;
            ViewLog.Info($"EntityHighlight：共享材质已建（取法={via}；shader.name=\"{shader.name}\"；"
                + $"Material.HasProperty({BrightnessProperty})={(hasB ? 1 : 0)} "
                + $"Material.HasProperty({ContrastProperty})={(hasC ? 1 : 0)}；"
                + $"{BrightnessProperty}={HoverBrightness} / {ContrastProperty}={HoverContrast} 为悬停档）");
            return _sharedMaterial;
        }

        /// <summary>
        /// 按"是否悬停"设置该渲染器：悬停 ⇒ 换上自定义材质 + 属性块 3.0/1.01；
        /// 未悬停 ⇒ 还原 <paramref name="originalMaterial"/> + 属性块 1.0/1.0。
        /// <para>⛔ 只用 `MaterialPropertyBlock` + `sharedMaterial`；⛔ 不用 `renderer.material`（会克隆）。</para>
        /// </summary>
        /// <param name="r">目标渲染器（null ⇒ 直接返回 false）。</param>
        /// <param name="originalMaterial">建节点时记下的原材质（还原用；可 null ⇒ 还原成 Unity 默认）。</param>
        /// <param name="highlighted">true = 悬停档。</param>
        /// <returns>true = 状态确实被落到了渲染器上。</returns>
        public static bool Apply(SpriteRenderer r, Material originalMaterial, bool highlighted)
        {
            if (r == null) return false;

            if (highlighted)
            {
                var mat = SharedMaterial();
                if (mat == null) return false;            // 着色器缺失（已报过）⇒ 不换材质、不改属性
                if (r.sharedMaterial != mat) r.sharedMaterial = mat;
            }
            else
            {
                if (r.sharedMaterial != originalMaterial) r.sharedMaterial = originalMaterial;
            }

            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetFloat(BrightnessProperty, BrightnessFor(highlighted));
            _block.SetFloat(ContrastProperty, ContrastFor(highlighted));
            r.SetPropertyBlock(_block);
            return true;
        }

        /// <summary>
        /// 回读该渲染器属性块里的两个值（**只读诊断 / 实机探针判据用**，不改状态）。
        /// <para>⚠️ 属性块里没写过这两个键时返回 0 —— 所以调用方必须先 `Apply` 过一次
        /// （`ViewModule` 建节点时就写了"未悬停档"，正是为了这条判据可读）。</para>
        /// </summary>
        public static bool ReadBack(SpriteRenderer r, out float brightness, out float contrast)
        {
            brightness = 0f;
            contrast = 0f;
            if (r == null) return false;

            if (_block == null) _block = new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            brightness = _block.GetFloat(BrightnessProperty);
            contrast = _block.GetFloat(ContrastProperty);
            return true;
        }

        /// <summary>本片自检用：共享材质是否已建出来（null = 还没被需要过）。</summary>
        public static bool HasSharedMaterial => _sharedMaterial != null;
    }
}
