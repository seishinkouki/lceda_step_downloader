using System;
using System.Diagnostics;
using Avalonia;

namespace ModelDownloader
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // 调试模式：传 --debug 参数或设置环境变量 MODELDOWNLOADER_DEBUG=1 时，
            // Avalonia 内部日志（LogToTrace）与本应用的剪贴板诊断日志输出到 stderr。
            // 例：MODELDOWNLOADER_DEBUG=1 ./ModelDownloader
            if (Array.IndexOf(args, "--debug") >= 0
                || Environment.GetEnvironmentVariable("MODELDOWNLOADER_DEBUG") == "1")
            {
                Trace.Listeners.Add(new TextWriterTraceListener(Console.Error));
                Trace.AutoFlush = true;
                Console.Error.WriteLine(
                    $"[Debug] 会话环境: XDG_SESSION_TYPE={Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")} " +
                    $"WAYLAND_DISPLAY={Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")} " +
                    $"DISPLAY={Environment.GetEnvironmentVariable("DISPLAY")}");
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            var builder = AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .With(new AvaloniaNativePlatformOptions
                {
                    RenderingMode =
                    [
                        AvaloniaNativeRenderingMode.OpenGl,
                        AvaloniaNativeRenderingMode.Software
                    ]
                })
#if DEBUG
                .WithDeveloperTools()
#endif
                .WithInterFont()
                .LogToTrace();

            if (OperatingSystem.IsLinux())
            {
                // The model viewer needs GL interop even when Mesa uses a CPU renderer.
                builder = builder.With(new X11PlatformOptions
                {
                    GlxRendererBlacklist = ["SVGA3D"]
                });
            }

            return builder;
        }
    }
}
