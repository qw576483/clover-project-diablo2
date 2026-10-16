// ─────────────────────────────────────────────────────────────────────────────
//  tools/probes/hosts/_shared/HostConsoleEncoding.cs
//
//  全部离线自检宿主共用：进程启动最早期把控制台输出编码钉死为 UTF-8。
//  （由 Directory.Build.props 统一 Compile 进每个宿主工程；见那里的 ItemGroup。）
//
//  为什么：宿主的 Console.OutputEncoding 随上下文漂移 ——
//  带控制台时跟随控制台代码页（本机 cp936/GBK），被重定向时又是另一套默认值；
//  捕获端（PowerShell 5.1 | Out-String / Out-File、cmd 重定向、CI 管道）各按各自
//  默认解码 ⇒ 同一份输出在不同跑法下出现**两种**乱码：
//    a) UTF-8 字节按 GBK 解码（可逆）；
//    b) GBK 字节按 UTF-8 解码（含替换符，**字节已丢，不可完全逆**）。
//  在进程入口（ModuleInitializer，早于 Main）钉死 UTF-8 后，输出字节流与跑法无关。
//
//  与 buildcheck 既有写法一致（其 Main 里本就有一行同款设置；重复设置无害）。
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Runtime.CompilerServices;
using System.Text;

internal static class HostConsoleEncoding
{
    [ModuleInitializer]
    internal static void Init()
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch (Exception) { /* 个别重定向场景不支持，忽略（此时 stdout 已是纯管道语义） */ }
    }
}
