using KCI.Core;
using System.Windows;
using System.Windows.Threading;

namespace KCI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Initialize();
        if (e.Args.Any(argument =>
                argument.Equals("--restore-windows-update", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                new WindowsUpdateService().RestoreWindowsUpdate();
            }
            catch
            {
                // The scheduled task runs without UI and will be retried by Windows.
            }
            Shutdown(0);
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MessageBox.Show(args.ExceptionObject?.ToString() ?? "未知错误", "KCI 未处理异常",
                MessageBoxButton.OK, MessageBoxImage.Error);
        };
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.ToString(), "KCI 运行错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
