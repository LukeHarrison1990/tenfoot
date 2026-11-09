using System;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace TenFoot;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        bool Has(string flag) => e.Args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        if (Has("--no-autostart"))
        {
            SetAutostart(false);
            Shutdown();
            return;
        }
        if (Has("--autostart")) SetAutostart(true);

        _singleInstance = new Mutex(true, "TenFoot.SingleInstance", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Start TenFoot when this Windows user signs in.</summary>
    private static void SetAutostart(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled) run.SetValue("TenFoot", $"\"{Environment.ProcessPath}\"");
        else run.DeleteValue("TenFoot", throwOnMissingValue: false);
    }
}
