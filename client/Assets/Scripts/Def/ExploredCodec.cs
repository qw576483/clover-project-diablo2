// ─────────────────────────────────────────────────────────────────────────────
// Diablo2 · Def/ExploredCodec.cs   ★ 片 save-progress 新增（2026-09-24）
//
// 作用：把「小地图已探索格」这一份**体积大的集合**做成可落盘的紧凑表示。
//
// 为什么需要它（链条断在哪）：
//   读档后进度类状态全丢 —— 这是片 save-areaid 在《同族穷举表》里点出来的两个缺口之一：
//   小地图已探索格的**权威持有者是渲染层** `Module/Map/MapView._explored`（`bool[,]`），
//   它只在**本局内**累积、从不落盘（`Module/Contracts.cs` 的 `IMapModule.ExploredCells` 注释原文
//   = 「作用域 = 当前区域」）⇒ 读档后 automap 的"记忆"全空。
//
// ── 表示选型（**为什么是位图 + base64**，而不是另外两种）──────────────────────
//   候选：① 逐格写进 JSON（格坐标数组）；② RLE（行程编码）；③ **位图 + base64**（本实现）。
//   ① **否**：一格一条 = 每格 4~6 字符（`{"x":12,"y":34}` 更贵），BloodMoor 80×80 走满约 3000 格
//      ⇒ 15 KB+ 的存档体积，且它是**逐格写全图**那类做法（任务书明令避免）。
//   ② **否（但不劣）**：RLE 对"成片探索"最省，但①语义脆（探索集是 BFS 半径 6 的形状，边界碎）
//      ②解码要处理"行末跨行"的段，离线断言与旧档兼容都要额外一整套边界用例。
//   ③ **是**：位图 = 宽×高 bit（80×80 = 6400 bit = **800 B** ⇒ base64 后 ≈1080 字符），
//      与尺寸无关地恒定紧凑、编解码各 10 行、**逐格幂等**（同集合编码结果逐字节相同 ⇒ 可当断言用），
//      且天然容错（越界位一律丢弃）⇒ 旧档/坏串读进来最坏退化成"少记几格"，不会崩。
//   实测（`tools/probes/hosts/savecheck` §14）：80×80 满图 6400 格 ⇒ cells 串 8544 字符；
//   本工程实际一局（走一片）600 格 ⇒ cells 串 ≈1108 字符（对比①的逐格数组 ≈3 KB）。
//
// ── 旧档兼容（硬要求）────────────────────────────────────────────────────────
//   `CharacterSave.exploredByArea` 是**新加**字段 ⇒ 旧档 JSON 里没有它
//   ⇒ `SaveJson.TryParse` 读缺字段时给**空列表**（既有约定：缺字段取默认值、绝不抛异常），
//   `Decode` 对 null / 空串 / 非法 base64 一律**返回 0 并保持输出列表为空**（⛔ 不抛）
//   ⇒ 缺字段的旧档读进来就是"没有已探索记录"，读档流程照常成功（本文件不参与版本降级判断）。
//
// 与 `Def.CharacterSave` 的字段一一对应：`ExploredAreaDto.area / w / h / cells`。
// ⛔ 无 Unity 依赖（只用 System / System.Collections.Generic / System.Text）⇒ `savecheck` 宿主可离线跑。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Text;

namespace Diablo2.Def
{
    /// <summary>
    /// 一个区域的小地图已探索格（存档里的紧凑表示；见文件头的选型理由）。
    /// <para>
    /// `w`/`h` **必须一起存**：格索引 `i = y * w + x` 是**区域局部**坐标，而各区域尺寸不同
    /// （Town 固定 40×40 量级、BloodMoor/DenOfEvil 随机 32~80）⇒ 只存位图不存尺寸 = 解不出来。
    /// </para>
    /// </summary>
    [Serializable]
    public class ExploredAreaDto
    {
        /// <summary>区域（`(int)AreaId`）。⛔ 各区域的格坐标**不可比**（见 `IMapModule.ExploredCells` 注释）。</summary>
        public int area;

        /// <summary>该区域地图宽（格）。</summary>
        public int w;

        /// <summary>该区域地图高（格）。</summary>
        public int h;

        /// <summary>`base64` 位图（行优先，`i = y * w + x`；位 = 已探索）。空串 = 没有已探索格。</summary>
        public string cells;
    }

    /// <summary>`ExploredAreaDto` 的编解码（**纯函数**，离线可断言；见文件头）。</summary>
    public static class ExploredCodec
    {
        /// <summary>位图上限（格）：防御坏档里的超大 `w*h`（不设上限的话一个坏字段就能吃掉几百 MB）。</summary>
        public const int MaxCells = 1 << 20;      // 1,048,576 格（本工程上限 80×80 = 6400）

        /// <summary>
        /// 把格索引集合编码成 base64 位图。
        /// <para>越界索引（&lt;0 / ≥ w*h）**丢弃**（⛔ 不抛、⛔ 不静默放大）；`w/h ≤ 0` ⇒ 返回空串。</para>
        /// <para>确定性：同集合 ⇒ 同串（位图按索引落位、无顺序依赖）⇒ 可直接当"存→读→再存"幂等断言用。</para>
        /// </summary>
        public static string Encode(IEnumerable<int> indices, int w, int h)
        {
            if (w <= 0 || h <= 0) return string.Empty;
            var n = (long)w * h;
            if (n > MaxCells) return string.Empty;         // 防御：非法尺寸不做位图

            var bytes = new byte[(n + 7) / 8];
            if (indices != null)
            {
                foreach (var i in indices)
                {
                    if (i < 0 || i >= n) continue;
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// 把 base64 位图解码成格索引（**只并入、不清空** <paramref name="into"/>；返回本次并入的格数）。
        /// <para>**兼容旧档 / 坏串**：`dto == null` / `cells` 空或 null / base64 非法 / 尺寸非法
        /// ⇒ 返回 0 且**不抛**（调用方拿到"空集合"，读档照常成功）。</para>
        /// </summary>
        public static int Decode(ExploredAreaDto dto, List<int> into)
        {
            if (into == null) return 0;
            if (dto == null) return 0;
            if (dto.w <= 0 || dto.h <= 0) return 0;
            var n = (long)dto.w * dto.h;
            if (n > MaxCells) return 0;
            if (string.IsNullOrEmpty(dto.cells)) return 0;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(dto.cells);
            }
            catch (FormatException)
            {
                return 0;                                   // 坏串（旧档 / 手改）⇒ 当"没有已探索记录"
            }

            var count = 0;
            var total = (int)n;
            for (var i = 0; i < total; i++)
            {
                var b = i >> 3;
                if (b >= bytes.Length) break;               // 短串：后面一律视为未探索（不抛）
                if ((bytes[b] & (1 << (i & 7))) == 0) continue;
                into.Add(i);
                count++;
            }
            return count;
        }

        /// <summary>把格坐标（区域局部）折成索引（越界返回 -1）。</summary>
        public static int IndexOf(int x, int y, int w, int h)
        {
            if (w <= 0 || h <= 0) return -1;
            if (x < 0 || y < 0 || x >= w || y >= h) return -1;
            return y * w + x;
        }

        /// <summary>把索引还原成格坐标（越界返回 `(0,0)` 并把 <paramref name="ok"/> 置 false）。</summary>
        public static void ToCell(int i, int w, int h, out int x, out int y, out bool ok)
        {
            x = 0; y = 0; ok = false;
            if (w <= 0 || h <= 0 || i < 0 || i >= w * h) return;
            x = i % w;
            y = i / w;
            ok = true;
        }

        /// <summary>日志/断言用的可读摘要（例：`area=1 80x80 格=612 cells=1108B`）。</summary>
        public static string Describe(ExploredAreaDto dto)
        {
            if (dto == null) return "(null)";
            var len = dto.cells == null ? 0 : dto.cells.Length;
            var sb = new StringBuilder(48);
            sb.Append("area=").Append(dto.area).Append(' ').Append(dto.w).Append('x').Append(dto.h)
              .Append(" cells=").Append(len).Append("B");
            return sb.ToString();
        }
    }
}
