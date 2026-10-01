using System;
using Avalonia;

namespace ModelDownloader
{
    internal sealed class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

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
