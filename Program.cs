// Application entry point. Uses the classic desktop lifetime (single main window).
// Rendering is locked to the software backend so the AOT publish does not need to ship
// the ANGLE/EGL native dependencies (libEGL.dll, av_libglesv2.dll).

using System;
using Avalonia;

namespace RegCompare;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.Software],
            })
            .WithInterFont()
            .LogToTrace();
}
