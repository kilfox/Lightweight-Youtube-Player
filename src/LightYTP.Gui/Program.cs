using Avalonia;

namespace LightYTP.Gui;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--version", StringComparer.Ordinal))
        {
            Console.WriteLine($"lightytp-gui {Version}");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();

    private static string Version =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.4.0";
}
