using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TenFoot;

public partial class MainWindow : Window
{
    private AppConfig _config = new();
    private readonly SessionManager _sessions = new();
    private readonly List<int> _hotkeyIds = new();
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(6) };
    private Button? _lastFocused;
    private IntPtr _hwnd;
    private string _hint = "";

    private static readonly Brush HintBrush = new SolidColorBrush(Color.FromRgb(0x5D, 0x65, 0x73));
    private static readonly Brush StatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB0, 0x4F));

    public MainWindow()
    {
        InitializeComponent();

        _clock.Tick += (_, _) => UpdateClock();
        _cursorTimer.Tick += (_, _) => { _cursorTimer.Stop(); if (_config.HideCursorWhenIdle) Mouse.OverrideCursor = Cursors.None; };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); HintText.Text = _hint; HintText.Foreground = HintBrush; };
        _sessions.BrowserClosed += BringToFront;

        PreviewKeyDown += OnPreviewKeyDown;
        MouseMove += (_, _) => { Mouse.OverrideCursor = null; _cursorTimer.Stop(); _cursorTimer.Start(); };
        Loaded += (_, _) => FocusTile();
        Closing += (_, _) => _sessions.CloseCurrent();

        LoadConfig();
        UpdateClock();
        _clock.Start();
        _cursorTimer.Start();
    }

    // ---------- Config & tiles ----------

    private void LoadConfig()
    {
        _config = ConfigStore.Load(out var error);
        BuildTiles();
        RegisterHotkeys();
        if (error != null) ShowStatus(error);
    }

    private void BuildTiles()
    {
        TileGrid.Children.Clear();
        TileGrid.Columns = Math.Max(1, _config.Columns);
        _lastFocused = null;

        foreach (var tile in _config.Tiles)
        {
            var button = new Button
            {
                Style = (Style)FindResource("TileButton"),
                Background = ParseBrush(tile.Color),
                Content = BuildContent(tile),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
            };
            button.Click += (_, _) => OnTile(tile);
            button.GotKeyboardFocus += (_, _) => { Scale(button, 1.08); Panel.SetZIndex(button, 1); _lastFocused = button; };
            button.LostKeyboardFocus += (_, _) => { Scale(button, 1.0); Panel.SetZIndex(button, 0); };
            button.MouseEnter += (_, _) => button.Focus();
            TileGrid.Children.Add(button);
        }

        if (IsLoaded) FocusTile();
    }

    private static UIElement BuildContent(TileConfig tile)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        if (!string.IsNullOrWhiteSpace(tile.Image) && File.Exists(tile.Image))
        {
            try
            {
                stack.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(Path.GetFullPath(tile.Image))),
                    Height = 110,
                    Margin = new Thickness(0, 0, 0, 12),
                    Stretch = Stretch.Uniform,
                });
            }
            catch (Exception) { /* bad image: fall back to text only */ }
        }

        stack.Children.Add(new TextBlock
        {
            Text = tile.Title,
            FontSize = 44,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(16, 0, 16, 0),
        });

        if (tile.Type.Equals("shortcut", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(tile.Shortcut) &&
            SessionManager.FindShortcut(tile.Shortcut) == null)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "Not installed",
                FontSize = 22,
                Opacity = 0.7,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }
        return stack;
    }

    private static Brush ParseBrush(string? color)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(color) && new BrushConverter().ConvertFromString(color) is Brush b) return b;
        }
        catch (Exception) { }
        return new SolidColorBrush(Color.FromRgb(0x2B, 0x33, 0x40));
    }

    private static void Scale(Button button, double to)
    {
        if (button.RenderTransform is not ScaleTransform st) return;
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase() };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private void FocusTile()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var target = _lastFocused ?? TileGrid.Children.OfType<Button>().FirstOrDefault();
            if (target == null) return;
            target.Focus();
            Keyboard.Focus(target);
        }, DispatcherPriority.Input);
    }

    // ---------- Launching ----------

    private void OnTile(TileConfig tile)
    {
        if (tile.Type.Equals("action", StringComparison.OrdinalIgnoreCase))
        {
            RunAction(tile.Action);
            return;
        }
        try
        {
            _sessions.Launch(tile, _config);
        }
        catch (LaunchException ex)
        {
            ShowStatus(ex.Message);
        }
    }

    private void RunAction(string? action)
    {
        switch (action?.Trim().ToLowerInvariant())
        {
            case "sleep":
                _sessions.CloseCurrent();
                Native.SetSuspendState(false, false, false);
                break;
            case "shutdown":
                _sessions.CloseCurrent();
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false });
                break;
            case "restart":
                _sessions.CloseCurrent();
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
                break;
            case "reload":
                LoadConfig();
                ShowStatus("Tiles reloaded");
                break;
            case "exit":
                Close();
                break;
            default:
                ShowStatus($"Unknown action \"{action}\"");
                break;
        }
    }

    /// <summary>Home pressed: close the current service and show the launcher.</summary>
    private void GoHome()
    {
        _sessions.CloseCurrent();
        BringToFront();
    }

    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
        Show();
        Activate();
        Topmost = true;
        Topmost = false;
        if (_hwnd != IntPtr.Zero) Native.SetForegroundWindow(_hwnd);
        FocusTile();
    }

    // ---------- Keys ----------

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                if (Keyboard.FocusedElement is Button b)
                {
                    b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    e.Handled = true;
                }
                break;
            case Key.F5:
                RunAction("reload");
                e.Handled = true;
                break;
            case Key.Escape:
            case Key.BrowserBack:
                e.Handled = true; // this is the home screen; nothing to go back to
                break;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var id in _hotkeyIds) Native.UnregisterHotKey(_hwnd, id);
        _hotkeyIds.Clear();

        var registered = new List<string>();
        var failed = new List<string>();
        int nextId = 1;
        foreach (var text in _config.HomeHotkeys)
        {
            if (!TryParseHotkey(text, out uint mods, out uint vk))
            {
                failed.Add(text);
                continue;
            }
            int id = nextId++;
            if (Native.RegisterHotKey(_hwnd, id, mods | Native.MOD_NOREPEAT, vk))
            {
                _hotkeyIds.Add(id);
                registered.Add(text);
            }
            else failed.Add(text);
        }

        _hint = registered.Count > 0
            ? $"Press {string.Join(" or ", registered)} to come back here  ·  F5 reloads tiles"
            : "No Home hotkey is set – edit homeHotkeys in tiles.json";
        HintText.Text = _hint;
        if (failed.Count > 0) ShowStatus($"Couldn't use hotkey: {string.Join(", ", failed)} (in use by another app or not recognised)");
    }

    private static bool TryParseHotkey(string text, out uint mods, out uint vk)
    {
        mods = 0;
        vk = 0;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Key>(part, ignoreCase: true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _hotkeyIds.Contains(wParam.ToInt32()))
        {
            GoHome();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ---------- Misc ----------

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm");
        DateText.Text = now.ToString("dddd d MMMM");
    }

    private void ShowStatus(string message)
    {
        HintText.Text = message;
        HintText.Foreground = StatusBrush;
        _statusTimer.Stop();
        _statusTimer.Start();
    }
}
