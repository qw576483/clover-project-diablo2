# 实机-A：#34 / #37 / #42 实机取证（agent-24）

驱动：editor_stop→clear_console→editor_play→_dev/p_runbg.cs→eval_file _dev/p_a24_*.cs；截图 1920x1080，读图前裁到 700~800 宽（_dev/crop24.ps1）。

## #34 五品质 tooltip（通过 / 2 趟）

**第1趟**（Stage 内、走 HUD 开面板路径）原始输出 `_dev/a24_tip_out_pass1.txt`：五件由 `IItemModule.CreateRandom` 真实生成，各品质一件，逐件悬停读 tooltip：

| 品质 | 标题 | 实测色 | 期望色 | 命中 |
|---|---|---|---|---|
| 白 | 轻弩 | #FFFFFF | #FFFFFF | 是 |
| 蓝 | 冰冷抗性 5 鹰盔 | #6969FF | 同 | 是 |
| 黄 | 火焰抗性5~10…盔之精力1 | #FFFF64 | 同 | 是 |
| 绿 | stam5~10轻型手套之res-pois-len25 | #00FF00 | 同 | 是 |
| 暗金 | mana-kill1标枪之最小伤害3~4 | #C7B377 | 同 | 是 |

读图：`Screenshots/a24_34p1_tip_0..4.png` 逐张看过，标题色与正文数值（耐久/价）与日志一致。

**第2趟**（重进 Play；`_dev/p_a24_fin.cs` → `_dev/a24_fin_out.txt`）同样 5/5 命中，图 `Screenshots/a24_34p2_tip_0..4.png`（已读 3/4 两张确认：绿=#00FF00、暗金=#C7B377，正文耐久/伤害与日志逐项一致）。

## #37 商店与修理（通过 / 3 趟）

链路（有调用栈坐实）：对话面板『交易 / 修理』→ `Events.ShopOpenRequest` → `App/AppEventRouting` → `NpcModule.GetShop(2)` → `Events.ShopOpen` → `HudPanel.OnShopOpen` → `ShopPanel`。

**买入/卖出**（`_dev/a24_shop_out.txt`，真鼠标点按钮）：金币 60000→59994（花 6 买「投掷小刀」）→ 卖出后 59994→60026（+32）。读图 `a24_37_2_buy.png`（面板金币 59994）、`a24_37_3_sell.png`（金币 60026）与日志逐项一致。

**修理**（`_dev/a24_fin_out.txt`，真鼠标点『修理全部』）：快照`修理费=430` → 金币 120000→119570（-430），7 件磨损装备全部回满。读图 `a24_37f_2_repair.png`（金币 119570、修理全部 0）。

## #42 暂停与设置（通过 / 2 趟）

**第1趟** `_dev/a24_set_a2_out.txt`+`a24_fs_out.txt`：真按 ESC → 站点=Pause、PausePanel=True、Time.timeScale=0（读图 `a24_42_1_pause.png`）→ 点 OPTIONS → 改 BGM+3/音效-2/WASD 开 → 值 bgm=1.00 sfx=0.60 wasd=true（读图 `a24_42_3_options_after.png`）→ 落盘 `client/setting/settings.json` 含 `"audio/bgm_volume":1,"input/use_wasd_move":true`；『全屏显示』开关也按过（false→true→false）。

**第2趟**（重进 Play）`_dev/a24_set_b_out.txt`：进程一启动就读到 `bgm=1.00 sfx=0.60 wasd=True`，与第1趟写下的期望值**一致=True**（读图 `a24_42_6_reenter_options.png` 仍是 1.00 / 0.60）。

## 环境性发现（非本 3 行）

1. 点 UI 的鼠标左键同时被读成"点地面"（`Module/Input/InputReader` + `Module/Player/PlayerModule.HandleMoveIntent`，无 UI 判定）⇒ 角色乱走、撞进出口就换区读档（冲掉注入物品）、落点在恰西 TalkRange 内还会自动开对话顶掉商店面板。**越界（Module/Input、Module/Player），只报不改**。
2. uGUI 指针事件时好时坏：同一份注入代码，PausePanel（Top 层）点得动，ShopPanel/SettingsPanel（Popup 层）常点不动（`_dev/a24_click_out.txt`：真点击不生效、`onClick.Invoke()` 生效）。
3. 同机同 Play 还有别的 agent 在跑探针（把站点打到 CharSelect、清背包）⇒ 本轮 `#34` 第2趟改成不依赖 Stage 的写法才过；建议后续串行化"谁在 Play 里跑"。

## #42 暂停与设置（通过 / 2 趟）

**第1趟** `_dev/a24_set_a2_out.txt` + `a24_fs_out.txt`：Stage 真按 ESC → 站点=Pause、PausePanel=True、`Time.timeScale=0`（读图 `a24_42_1_pause.png`：暂停菜单叠在营地实景上）→ 点 OPTIONS → 改 BGM +3 / 音效 -2 / WASD 开 →
 冰冷抗性 5 鹰盔 | #6969FF | #6969FF | 是 |
| 黄 | 火焰抗性 5~10 闪电抗性 5~10 盔 之 精力 1 | #FFFF64 | #FFFF64 | 是 |