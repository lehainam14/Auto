using System.IO;
using System.Windows;
using Macro.Actions;

namespace Macro;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every built-in action registers itself through the registry.
        ActionsModule.RegisterAll();
        Core.ActionDefaults.LoadConfig();
        Core.ActionDefaults.ClearDefaultImageTemplate();

        // Hidden headless mode used by the developer tests:
        //   macro.exe --selftest   -> runs core tests, writes selftest-result.txt
        if (e.Args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            SelfTest.Run(Path.Combine(AppContext.BaseDirectory, "selftest-result.txt"));
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"An unexpected error occurred:\n\n{args.Exception.Message}",
                            "macro", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            window.TryOpenFile(e.Args[0]);
        window.Show();
    }
}
