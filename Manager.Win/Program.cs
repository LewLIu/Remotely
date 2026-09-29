using Avalonia;
using Avalonia.Controls;
using Remotely.Manager.Win.Services;

namespace Remotely.Manager.Win;

internal static class Program
{
    private const string ManagerMutexName = @"Local\Remotely_Manager";

    [STAThread]
    public static void Main(string[] args)
    {
        using var singleInstance = new SingleInstanceGuard(ManagerMutexName);
        if (!singleInstance.IsPrimaryInstance)
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
