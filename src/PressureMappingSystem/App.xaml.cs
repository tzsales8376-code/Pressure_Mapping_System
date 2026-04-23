using System.Windows;

namespace PressureMappingSystem;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handling
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show(
                $"發生未預期的錯誤:\n\n{args.Exception.Message}\n\n{args.Exception.StackTrace}",
                "TRANZX Pressure Mapping System - Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
