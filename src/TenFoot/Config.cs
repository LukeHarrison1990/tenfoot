using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TenFoot;

public sealed class AppConfig
{
    /// <summary>Tiles per row.</summary>
    public int Columns { get; set; } = 4;

    /// <summary>Global keys that close the current service and return to TenFoot.</summary>
    public List<string> HomeHotkeys { get; set; } = new() { "BrowserHome", "Ctrl+Alt+H" };

    /// <summary>Leave empty to find Edge automatically.</summary>
    public string? EdgePath { get; set; }

    public List<string> EdgeArguments { get; set; } = new()
    {
        "--start-fullscreen",
        "--no-first-run",
        "--no-default-browser-check",
        "--autoplay-policy=no-user-gesture-required",
    };

    public bool HideCursorWhenIdle { get; set; } = true;

    public List<TileConfig> Tiles { get; set; } = new();
}

public sealed class TileConfig
{
    public string Title { get; set; } = "";
    public string Color { get; set; } = "#2B3340";

    /// <summary>web | app | shortcut | action</summary>
    public string Type { get; set; } = "web";

    /// <summary>web: the page to open.</summary>
    public string? Url { get; set; }

    /// <summary>app: full path to an .exe.</summary>
    public string? Path { get; set; }
    public string? Arguments { get; set; }

    /// <summary>shortcut: name of a Start menu or desktop shortcut, e.g. "Sky Go".</summary>
    public string? Shortcut { get; set; }

    /// <summary>action: sleep | shutdown | restart | reload | exit</summary>
    public string? Action { get; set; }

    /// <summary>Optional image file shown above the title.</summary>
    public string? Image { get; set; }

    /// <summary>app/shortcut: process name to close when Home is pressed (requires closeOnHome).</summary>
    public string? ProcessName { get; set; }
    public bool CloseOnHome { get; set; }
}

public static class ConfigStore
{
    public static string Folder =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TenFoot");

    public static string FilePath => System.IO.Path.Combine(Folder, "tiles.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppConfig Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Defaults(), Options));
            }
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options);
            if (config == null || config.Tiles.Count == 0)
            {
                error = "tiles.json has no tiles – showing the defaults.";
                return Defaults();
            }
            return config;
        }
        catch (Exception ex)
        {
            error = $"Couldn't read tiles.json ({ex.Message}) – showing the defaults.";
            return Defaults();
        }
    }

    public static AppConfig Defaults() => new()
    {
        Tiles = new()
        {
            new() { Title = "Netflix", Color = "#8E0A12", Url = "https://www.netflix.com/browse" },
            new() { Title = "YouTube", Color = "#B31217", Url = "https://www.youtube.com/" },
            new() { Title = "Disney+", Color = "#0B1F52", Url = "https://www.disneyplus.com/" },
            new() { Title = "BBC iPlayer", Color = "#C2185B", Url = "https://www.bbc.co.uk/iplayer" },
            new() { Title = "Prime Video", Color = "#0F79AF", Url = "https://www.primevideo.com/" },
            new() { Title = "Sky Go", Color = "#1E3A8A", Type = "shortcut", Shortcut = "Sky Go" },
            new() { Title = "Sleep", Color = "#2B3340", Type = "action", Action = "sleep" },
            new() { Title = "Exit to Windows", Color = "#2B3340", Type = "action", Action = "exit" },
        },
    };
}
