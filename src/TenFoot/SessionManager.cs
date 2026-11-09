using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace TenFoot;

public sealed class LaunchException : Exception
{
    public LaunchException(string message) : base(message) { }
}

/// <summary>
/// Runs one service at a time. Web tiles open in a dedicated Edge profile as a full-screen app
/// window, which we own and can close; app/shortcut tiles are started and left alone unless
/// closeOnHome + processName are set.
/// </summary>
public sealed class SessionManager
{
    private Process? _browser;
    private TileConfig? _tile;
    private DateTime _started;
    private bool _sawWindow;
    private int _missingTicks;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Raised when the user closes the browser window themselves (e.g. Alt+F4).</summary>
    public event Action? BrowserClosed;

    public SessionManager()
    {
        _watch.Tick += (_, _) => Watch();
    }

    public static string EdgeProfileFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TenFoot", "EdgeProfile");

    public void Launch(TileConfig tile, AppConfig config)
    {
        CloseCurrent();
        _tile = tile;
        switch (tile.Type.Trim().ToLowerInvariant())
        {
            case "web":
                LaunchWeb(tile, config);
                break;
            case "app":
                Start(new ProcessStartInfo(Require(tile.Path, "path"))
                {
                    Arguments = tile.Arguments ?? "",
                    UseShellExecute = true,
                }, tile.Title);
                break;
            case "shortcut":
                var name = Require(tile.Shortcut, "shortcut");
                var lnk = FindShortcut(name)
                    ?? throw new LaunchException($"Couldn't find a Start menu shortcut called \"{name}\". Is it installed?");
                Start(new ProcessStartInfo(lnk) { UseShellExecute = true }, tile.Title);
                break;
            default:
                throw new LaunchException($"Unknown tile type \"{tile.Type}\".");
        }
    }

    /// <summary>Close whatever TenFoot opened last (where it can).</summary>
    public void CloseCurrent()
    {
        _watch.Stop();
        if (_browser != null)
        {
            CloseGracefully(_browser, 4000);
            _browser.Dispose();
            _browser = null;
        }

        if (_tile is { CloseOnHome: true } t && !string.IsNullOrWhiteSpace(t.ProcessName))
        {
            foreach (var p in Process.GetProcessesByName(t.ProcessName))
            {
                CloseGracefully(p, 3000);
                p.Dispose();
            }
        }
        _tile = null;
    }

    private void LaunchWeb(TileConfig tile, AppConfig config)
    {
        var edge = FindEdge(config)
            ?? throw new LaunchException("Microsoft Edge wasn't found. Set \"edgePath\" in tiles.json.");
        var url = Require(tile.Url, "url");
        Directory.CreateDirectory(EdgeProfileFolder);

        var psi = new ProcessStartInfo(edge) { UseShellExecute = false };
        // A private profile means this Edge is a separate instance we can watch and close,
        // and your sign-ins for each service are kept between sessions.
        psi.ArgumentList.Add("--user-data-dir=" + EdgeProfileFolder);
        foreach (var arg in config.EdgeArguments) psi.ArgumentList.Add(arg);
        psi.ArgumentList.Add("--app=" + url);

        _browser = Start(psi, tile.Title);
        _started = DateTime.UtcNow;
        _sawWindow = false;
        _missingTicks = 0;
        _watch.Start();
    }

    private void Watch()
    {
        if (_browser == null)
        {
            _watch.Stop();
            return;
        }

        bool alive = !_browser.HasExited;
        if (alive && Native.HasVisibleWindow(_browser.Id))
        {
            _sawWindow = true;
            _missingTicks = 0;
            return;
        }

        bool windowGone = _sawWindow && ++_missingTicks >= 2;
        bool neverAppeared = !_sawWindow && DateTime.UtcNow - _started > TimeSpan.FromSeconds(30);
        if (!alive || windowGone || neverAppeared)
        {
            _watch.Stop();
            if (alive) CloseGracefully(_browser, 2000); // Edge lingering in the background
            _browser.Dispose();
            _browser = null;
            _tile = null;
            BrowserClosed?.Invoke();
        }
    }

    private static void CloseGracefully(Process p, int waitMs)
    {
        try
        {
            if (p.HasExited) return;
            p.CloseMainWindow();
            if (!p.WaitForExit(waitMs)) p.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (Exception) { }
        }
    }

    private static Process? Start(ProcessStartInfo psi, string title)
    {
        try
        {
            return Process.Start(psi);
        }
        catch (Win32Exception ex)
        {
            throw new LaunchException($"Couldn't open {title}: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            throw new LaunchException($"Couldn't open {title}: {ex.Message}");
        }
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value) ? throw new LaunchException($"This tile is missing \"{field}\" in tiles.json.") : value;

    public static string? FindEdge(AppConfig config)
    {
        var candidates = new[]
        {
            config.EdgePath,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        };
        return candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c) && File.Exists(c));
    }

    public static string? FindShortcut(string name)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var all = new List<string>();
        foreach (var root in roots.Where(r => !string.IsNullOrEmpty(r) && Directory.Exists(r)))
        {
            try { all.AddRange(Directory.EnumerateFiles(root, "*.lnk", options)); }
            catch (Exception) { }
        }
        return all.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? all.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(name, StringComparison.OrdinalIgnoreCase));
    }
}
