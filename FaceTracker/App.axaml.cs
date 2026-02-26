using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace FaceFinderDemo;

/// <summary>
/// Avalonia <see cref="Application"/> subclass.
/// Loads XAML-declared resources and styles, then creates the application's main window.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Loads resources and styles declared in <c>App.axaml</c> (themes, colours, data templates).
    /// Called by the framework before <see cref="OnFrameworkInitializationCompleted"/>.
    /// </summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Called once the Avalonia framework is fully initialised.
    /// Creates and assigns <see cref="MainWindow"/> as the desktop application's main window.
    /// </summary>
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
