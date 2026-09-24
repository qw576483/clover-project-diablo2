// ─────────────────────────────────────────────────────────────────────────────
// E-core-19 最小复现：`IsoLayout` 的「格增量 → 8 方向」表
//
//
// 判据（三层，互相独立）：
//   ① **权威表**：8 个格增量 → 期望 `Dir8`（由 `GridToWorld` 的公式直接反算，
//      并与 Diablo2 参考实现 Diablerie `Iso.Direction(pos, target, 8)` 逐条一致）；
//   ② **独立推导**：把格增量换成 `GridToWorld` 的世界位移，再用 `WorldToGridContinuous`
//      换回 map 空间，用 Diablerie `Iso.Direction` 的原始公式（基准向量 `(1,1)`、顺时针）
//      算出 8 向序号，看它是否与 ① 的期望一致 —— **不依赖 `DirectionTo` 的实现**；
//   ③ **往返自洽**：`DirectionDelta(DirectionTo(delta))` 与 `delta` 的符号方向一致。
//
// 出处：`GridToWorld` 是 `x = (gx − gy)·HalfW`、`y = −(gx + gy + 1)·HalfH`
//   ⇒ `Δworld = ((Δgx − Δgy)·HalfW, −(Δgx + Δgy)·HalfH)`（W 轴 = 屏幕右，Y 轴 = 屏幕上）。
//   屏幕上的等距投影把 map 空间的 45° 斜向压成 2:1（斜向 `Δworld` 只有 26.57°）
//      ⇒ "屏幕夹角 45°"这种判据**是错的**（会误报），角度必须在 **map 空间**判。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using CloverEngine;
using UnityEngine;

internal static class Program
{
    private static int _fail;
    private static int _run;

    private static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [ OK ] " : "  [FAIL] ") + name + "   (" + detail + ")");
        if (!ok) _fail++;
    }

    private static int Sign(float v) => v > 0f ? 1 : (v < 0f ? -1 : 0);

    private static void Main()
    {
        LogThrottle.Clock = () => 0f;               // 确定性（本用例也不该产生任何日志）

        var newLayout = new IsoLayout(1f, 0.5f, 10, 1000);
        var oldLayout = new IsoLayoutOld(1f, 0.5f, 10, 1000);

        var oldFail = RunSuite("OLD（修复前）", oldLayout.DirectionTo, oldLayout.DirectionDelta,
                               oldLayout.GridToWorld, oldLayout.WorldToGridContinuous);
        var newFail = RunSuite("NEW（修复后 = 引擎真实源码）", newLayout.DirectionTo, newLayout.DirectionDelta,
                               newLayout.GridToWorld, newLayout.WorldToGridContinuous);

        Console.WriteLine();
        Console.WriteLine("===== 汇总 =====");
        Console.WriteLine("  OLD 失败项 = " + oldFail + " / " + _run);
        Console.WriteLine("  NEW 失败项 = " + newFail + " / " + _run);
        Console.WriteLine("  判据：OLD 必须 > 0（修复前确实错）、NEW 必须 == 0");
        var ok = oldFail > 0 && newFail == 0;
        Console.WriteLine(ok ? "===== DIRCHECK: ALL PASS ====="
                             : "===== DIRCHECK: FAILED（对照不成立）=====");
        Environment.ExitCode = ok ? 0 : 1;
    }

    private static int RunSuite(string label,
                                Func<Vector2Int, Dir8> directionTo,
                                Func<Dir8, Vector2Int> directionDelta,
                                Func<int, int, Vector3> gridToWorld,
                                Func<Vector3, Vector2> worldToGrid)
    {
        Console.WriteLine();
        Console.WriteLine("############ " + label + " ############");

        var dirOrder = new[] { Dir8.S, Dir8.SW, Dir8.W, Dir8.NW, Dir8.N, Dir8.NE, Dir8.E, Dir8.SE };
        var cases = new[]
        {
            new { Delta = new Vector2Int(1, 1),   Dir = Dir8.S  },
            new { Delta = new Vector2Int(0, 1),   Dir = Dir8.SW },
            new { Delta = new Vector2Int(-1, 1),  Dir = Dir8.W  },
            new { Delta = new Vector2Int(-1, 0),  Dir = Dir8.NW },
            new { Delta = new Vector2Int(-1, -1), Dir = Dir8.N  },
            new { Delta = new Vector2Int(0, -1),  Dir = Dir8.NE },
            new { Delta = new Vector2Int(1, -1),  Dir = Dir8.E  },
            new { Delta = new Vector2Int(1, 0),   Dir = Dir8.SE },
        };

        var local = _fail;
        _run = 0;

        Console.WriteLine("--- ① DirectionTo：8 个格增量 → 期望 Dir8 ---");
        var seen = new System.Collections.Generic.HashSet<Dir8>();
        foreach (var c in cases)
        {
            var got = directionTo(c.Delta);
            _run++;
            Check(string.Format("({0,2},{1,2}) → {2}", c.Delta.x, c.Delta.y, c.Dir), got == c.Dir, "got " + got);
            seen.Add(got);
        }
        _run++;
        Check("8 个增量映射到 8 个互不相同的方向（双射）", seen.Count == 8, "distinct = " + seen.Count);

        Console.WriteLine("--- ② 独立推导：GridToWorld → WorldToGridContinuous → Diablerie 角度公式 ---");
        foreach (var c in cases)
        {
            var o = gridToWorld(0, 0);
            var p = gridToWorld(c.Delta.x, c.Delta.y);
            var w = new Vector3(p.x - o.x, p.y - o.y, 0f);
            var map = worldToGrid(w);
            var mapOk = Mathf.Abs(map.x - c.Delta.x) < 1e-3f && Mathf.Abs(map.y - c.Delta.y) < 1e-3f;

            // Diablerie `Iso.Direction(pos, target, directionCount=8)` 的原始公式（逐字照搬）：
            //   angle = Angle((1,1), dir) * Sign(dir.y - dir.x)
            //   k     = Round(((angle + 360) % 360) / (360/directionCount)) % directionCount
            var angle = Vector3.Angle(new Vector3(1f, 1f, 0f), new Vector3(map.x, map.y, 0f))
                        * Mathf.Sign(map.y - map.x);
            var idx = Mathf.RoundToInt((angle + 360f) % 360f / (360f / 8f)) % 8;
            var derived = dirOrder[idx];          // 独立判据（只用到 GridToWorld/WorldToGridContinuous）

            var got = directionTo(c.Delta);       // 被测实现
            _run++;
            Check(string.Format("位移 ({0,2},{1,2}) ⇒ map({2:0.#},{3:0.#}) ⇒ 角度公式给 {4} / DirectionTo 给 {5}",
                                c.Delta.x, c.Delta.y, map.x, map.y, derived, got),
                  mapOk && derived == c.Dir && got == derived,
                  string.Format("mapOk={0} derived={1} expect={2} got={3}", mapOk, derived, c.Dir, got));
        }

        Console.WriteLine("--- ③ 往返自洽：DirectionDelta(DirectionTo(delta)) 与 delta 同号 ---");
        foreach (var c in cases)
        {
            var back = directionDelta(directionTo(c.Delta));
            var signOk = Sign(back.x) == Sign(c.Delta.x) && Sign(back.y) == Sign(c.Delta.y);
            _run++;
            Check(string.Format("({0,2},{1,2}) → 朝向 → ({2,2},{3,2})", c.Delta.x, c.Delta.y, back.x, back.y),
                  signOk, "signOk=" + signOk);
        }

        Console.WriteLine("--- ④ 回归护栏（旧错表必须不再成立）---");
        var guards = new[]
        {
            new { D = new Vector2Int(0, 1),  Bad = Dir8.S,  Txt = "(0,+1) 不再返回 S（旧表整档逆时针偏 45°）" },
            new { D = new Vector2Int(1, 1),  Bad = Dir8.SE, Txt = "(+1,+1) 不再返回 SE" },
            new { D = new Vector2Int(1, 0),  Bad = Dir8.E,  Txt = "(+1,0) 不再返回 E" },
            new { D = new Vector2Int(1, -1), Bad = Dir8.NE, Txt = "(+1,-1) 不再返回 NE" },
            new { D = new Vector2Int(0, -1), Bad = Dir8.N,  Txt = "(0,-1) 不再返回 N" },
        };
        foreach (var g in guards)
        {
            _run++;
            var got = directionTo(g.D);
            Check(g.Txt, got != g.Bad, "got " + got);
        }

        Console.WriteLine("--- ⑤ 只看符号（(3,7) 与 (1,1) 同解）---");
        _run++;
        Check("DirectionTo(3,7) == DirectionTo(1,1) == S",
              directionTo(new Vector2Int(3, 7)) == Dir8.S &&
              directionTo(new Vector2Int(3, 7)) == directionTo(new Vector2Int(1, 1)),
              directionTo(new Vector2Int(3, 7)).ToString());
        _run++;
        Check("DirectionTo(0,5) == SW", directionTo(new Vector2Int(0, 5)) == Dir8.SW,
              directionTo(new Vector2Int(0, 5)).ToString());
        _run++;
        Check("DirectionTo(-9,0) == NW", directionTo(new Vector2Int(-9, 0)) == Dir8.NW,
              directionTo(new Vector2Int(-9, 0)).ToString());

        var fail = _fail - local;
        Console.WriteLine("--- " + label + "：失败 " + fail + " / " + _run + " ---");
        return fail;
    }
}
