using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace ModelDownloader.Platform;

/// <summary>
/// Linux 下 Avalonia 的 X11 剪贴板实现（X11Clipboard）在部分桌面环境中读取不到内容，
/// 且 TextBox 的粘贴路径会静默吞掉失败（表现为"粘贴没有反应"）。
/// 典型场景：纯 Wayland 会话下，Wayland 原生应用复制的文本只注册在 Wayland 剪贴板协议上，
/// 经 XWayland 运行的 Avalonia 客户端通过 X11 CLIPBOARD 选择读不到。
/// 因此提供"框架剪贴板 -> 系统工具"的读取兜底链：
///   wl-paste(Wayland 原生) -> xclip(X11) -> xsel(X11)。
/// </summary>
public static class ClipboardHelper
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromMilliseconds(800);

    /// <summary>读取剪贴板文本：先走 Avalonia 框架剪贴板，失败/为空时回退到系统工具。</summary>
    public static async Task<string?> GetTextWithFallbackAsync(TopLevel? topLevel)
    {
        // 1. Avalonia 框架剪贴板（正常环境下直接成功）
        try
        {
            Trace.WriteLine("[Clipboard] 尝试框架剪贴板 TryGetTextAsync ...");
            // Avalonia 12: GetTextAsync 已移至 ClipboardExtensions.TryGetTextAsync(不可用时返回 null)
            var text = topLevel?.Clipboard is { } clipboard
                ? await clipboard.TryGetTextAsync()
                : null;
            Trace.WriteLine($"[Clipboard] 框架剪贴板返回: {(text == null ? "null" : text.Length + " 字符")}");
            if (!string.IsNullOrEmpty(text)) return text;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[Clipboard] 框架 GetTextAsync 抛出异常: {ex}");
        }

        // 2. Linux 回退：外部命令行工具读取
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // wl-paste：Wayland 会话下能读到 Wayland 原生应用复制的内容
            var fromWl = await RunCaptureAsync("wl-paste", "--no-newline");
            if (!string.IsNullOrEmpty(fromWl)) return fromWl;

            // xclip / xsel：X11 会话下的标准剪贴板工具
            var fromXclip = await RunCaptureAsync("xclip", "-selection", "clipboard", "-o");
            if (!string.IsNullOrEmpty(fromXclip)) return fromXclip;

            var fromXsel = await RunCaptureAsync("xsel", "--clipboard", "--output");
            if (!string.IsNullOrEmpty(fromXsel)) return fromXsel;
        }

        Trace.WriteLine("[Clipboard] 所有剪贴板读取方式均失败");
        return null;
    }

    /// <summary>运行外部剪贴板工具并捕获 stdout。工具未安装、超时或无内容时返回 null。</summary>
    private static async Task<string?> RunCaptureAsync(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = args[0],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            for (var i = 1; i < args.Length; i++) psi.ArgumentList.Add(args[i]);

            using var process = Process.Start(psi);
            if (process == null) return null;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            // xclip 在剪贴板为空时会阻塞等待，必须带超时并终止进程
            using var cts = new CancellationTokenSource(ToolTimeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { /* 进程可能已退出 */ }
                return null;
            }

            var output = await outputTask;
            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex)
        {
            // 工具未安装（FileNotFoundException）等情况
            Trace.WriteLine($"[Clipboard] {args[0]} 读取失败: {ex.Message}");
            return null;
        }
    }
}
