# agent-36：引擎下沉 A5 —— 文字渲染后端挂钩（消掉 E19）

## 0. 技能（开工必做）

- 有 `use_skill` → `use_skill("clover-engine")`、`use_skill("unity-cli")`；没有就按序读文件：
  - 项目级：`<项目根>/tools/ai-skill/SKILL.md` → `constraints.md` / `registry.md`
  - 全局：`~/.codebuddy/skills/ai-skill/SKILL.md`（规则层不可被项目级覆盖）、`~/.codebuddy/skills/unity-cli/SKILL.md`
- **必读**：`patterns/engine-fix.md`、§2（成本闸门：进 Play 记账 ≤2）、§3 四拍、§4 证据契约。

## 1. 目标（一句话）

引擎通用件（`ToastLayer` / `LoadingLayer` / `ConfirmLayer` / `FloatTextLayer` 等）创建的 `Text` 一律用引擎内置字体 `UIFactory.DefaultFont()` ⇒ 像素风业务**无法让引擎自己的提示也走原版字模**（本项目 E19 就是这条：引擎 Toast 的「角色名已存在」「背包已满」与加载中「加载中...」全是默认字体）。

**本片**：引擎新增一个**文字渲染挂钩**（纯新增），让业务能在"引擎刚创建 `Text`"时把自己的字模渲染器挂上去；本项目注册该挂钩，让引擎通用件的中文也走原版字模 ⇒ **E19 消除**。

## 2. 任务边界

**只做**：
- 引擎**新增** `Runtime/Presentation/TextHooks.cs`（位置/命名以你取证后与 `Runtime/Presentation/*` 风格一致为准）
- 引擎：在**所有创建 `Text` 的位置**插入一次挂钩通知（⛔ 不改 `DefaultFont()` 的既有行为：**未注册挂钩时行为与现在逐字一致**）
- 项目：注册挂钩（Bootstrap 里一行）+ 一个 `ITextHook` 实现（决定"哪些 Text 要挂字模镜像"）
- 验收表 E19 行**不要改**（⛔ 文档归主 agent）；把"是否可移除"写进回报

**绝不做的**：
- ⛔ 不许改 `UIFactory` / `UIWidgets` 的既有公开签名；⛔ 不许改 `D2Text.cs` / `D2TextMirror.cs` 的既有语义（只允许**新增**注册代码）
- ⛔ 不许改 `tools/ai-skill/**`、`策划/**`、`docs/**`、任何全局 skill；⛔ 不许读其它 `clover-project-*`；⛔ 不许再派子 agent；⛔ 不许自己写 `# adjudicated:` 行

## 3. 契约（主 agent 已定；实现细节先取证再落地）

```csharp
namespace CloverEngine
{
    /// <summary>业务可注册的"文字渲染挂钩"：引擎通用件创建 <c>Text</c> 后通知它。</summary>
    public interface ITextHook
    {
        /// <summary>
        /// 引擎刚创建了一个 <c>Text</c>。业务可在此把它改造成自己的渲染方式
        /// （本项目的做法：保留 <c>Text</c> 作数据持有者、挂 <c>D2TextMirror</c> 用原版字模画）。
        /// ⛔ 不允许抛异常；抛了引擎吞掉并 Warn 一次（⛔ 不许让引擎通用件打不开）。
        /// </summary>
        void OnTextCreated(UnityEngine.UI.Text text);
    }

    public static class TextHooks
    {
        /// <summary>当前挂钩；<c>null</c> = 引擎内置默认字体（= 现状，逐字不变）。</summary>
        public static ITextHook Current { get; set; }

        /// <summary>引擎侧唯一入口：创建 Text 后调用（见"注入点"）。</summary>
        public static void NotifyCreated(UnityEngine.UI.Text text);
    }
}
```

**必须取证并写进回报的三件事**：
1. **注入点清单**：`Runtime/**` 里**所有**创建 `Text` 的位置（`GetComponents`/`AddComponent<Text>`/`new GameObject(...).AddComponent<Text>()`/`UIWidgets.CreateText` 等）——逐个列出 `文件:行`，确保**每一个**都过 `TextHooks.NotifyCreated`。已知一处：`UIWidgets.cs:134`（`text.font = DefaultFont()`）。
2. **调用时机**：通知必须在"文字内容/对齐/字号已就位"之后（业务镜像要读得到这些）—— 若不满足，说明该怎么改（⛔ 不许改 `Text` 的既有赋值顺序，只许选通知点）。
3. **未注册时的等价性**：`TextHooks.Current == null` ⇒ 引擎行为与现在**逐字一致**（这是硬判据，见 §5）。

**项目侧**：Bootstrap 里注册一个 `ITextHook` 实现（建议放 `UI/D2EngineTextHook.cs`，一个 `.cs` 一个类、⛔ 不放第二个 MonoBehaviour），`OnTextCreated` 里判断"这条 Text 该不该挂字模镜像"（判据自己取证：例如 `HasNonAscii(text.text)` 或按父节点属于引擎 Layer），该挂则 `text.gameObject.AddComponent<D2TextMirror>()` 并按现有 `D2TextMirror` 的约定填 `Source` / `Font`。

## 4. 产出物

- 引擎：`Runtime/Presentation/TextHooks.cs`（新增）+ 注入点改动（逐个列出行）
- 项目：注册代码 + `ITextHook` 实现
- 证据：`client/Assets/Screenshots/p36_engine_text.png`（**表现类**：至少覆盖「引擎 Toast（中文）」与「LoadingLayer 加载中...」两处，能看出是**原版字模**而非默认字体）
- 回报（消息）：产出物 + 自检原始输出 + 未决

## 5. 验收标准（逐条自查）

- [ ] 编译绿：`recompile_status` ⇒ `completed / failed=false / errors=[]`
- [ ] **未注册时逐字等价（本片最关键）**：把注册那行注释掉（或让 `Current = null`）跑一次 —— 引擎通用件行为与改前**逐字一致**（可离线判：`uicheck` 等宿主的 Text 相关断言改前/改后逐行 0 差异；实机图可与改前对照）
- [ ] `.ai-tmp/hosts/run_all_hosts.ps1` ⇒ `TOTAL_HOSTS=10 FAILED=0`
- [ ] **E19 消除的机检判据（复用项目已有探针，⛔ 不许新写一套）**：E19 的出处是片 3 的"启用 Text 含非 ASCII"扫描（探针里打印过 `[A54] … 其中含非ASCII=2 [Label=加载中...][Label=角色名已存在]`）。**找到并复用那个扫描**，本轮判据 = **含非 ASCII 的「仍在绘制」Text 数 = 0**（`D2TextMirror` 已把 `Text.enabled=false / font=null` ⇒ 它们不算"在绘制"）。把这个数字做成一行日志贴进回报。
- [ ] **实机证据（1 轮，≤2 次 Play）**：驱动到"引擎 Toast 出现"+"LoadingLayer 出现"两个时刻，采 `p36_engine_text.png` 并**自己读图**确认两处都是原版字模（读不到 ⇒ `BLOCKED`，⛔ 不许硬写画面描述）
- [ ] **进 Play 记账**：`.ai-tmp/test/play-log.tsv` 追加行（含理由）
- [ ] `consoleErrors = 0`（`editor_stop → clear_console → editor_play → 跑链 → 读`）
- [ ] `git diff --stat -- clover-client-unity-engine` 只多出本片新增/改动（逐条列出）
- [ ] 回报里明确写：**E19 是否可移除**（给那行机检数字）＋ **E20 的处理建议**（E20 是 `InputField.textComponent` 必须是真 `Text` 的 uGUI 限制；本片是否顺带做了"字模输入框"或维持登记）
- [ ] 跑一次只读 `tools/verify.ps1`，把 summary 行贴进回报（如实贴红项）

## 6. 约束

- 批次流水线：① 只读取证（注入点清单 + `D2TextMirror` 的挂载约定 + `uicheck` 的 Text 断言 + E19 探针原文）→ ② 一次改完 → ③ 一次编译 + 全部宿主 → ④ **一轮** Play 采证据（⛔ 不许逐项进 Play）。
- ⛔ **驱动的复用优先**：`.ai-tmp/drivers/` 里已有 p29/p34 轮的探针，**先读、优先改**，⛔ 不许从零重写。
- ⛔ 不许把"引擎默认字体被用到"这件事在业务侧"扫一遍就完"——判据是**画面像素由谁产生**，不是源码 grep。
- 回报格式：`产出物 / 自检（注入点清单 + 编译 + 未注册等价性 + 宿主 + E19 机检数字 + 读图结论 + play-log + diff）/ 未决`。
