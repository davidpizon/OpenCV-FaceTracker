using Avalonia;

namespace FaceFinderDemo;

/// <summary>Application entry point.</summary>
class Program
{
    /// <summary>
    /// Main entry point. <see cref="STAThreadAttribute"/> is required on Windows for COM
    /// interop compatibility used by some Avalonia platform backends.
    /// </summary>
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures and returns the Avalonia application builder.
    /// <see cref="AppBuilder.UsePlatformDetect"/> selects the best available rendering
    /// backend (Skia, Direct2D, etc.) for the current OS at runtime.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
