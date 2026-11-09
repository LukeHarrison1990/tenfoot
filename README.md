# TenFoot

TenFoot is a full-screen, ten-foot launcher for a Windows PC connected to a TV. It shows a
grid of big tiles that you can drive with arrow keys and Enter, an air mouse, or a mouse.

- **Streaming services** open as full-screen Edge app windows, with no tabs or address bar.
  TenFoot uses its own Edge profile, so you sign in to each service once and stay signed in.
- **Sky Go** (or any other installed program) launches from its Start menu shortcut.
- **One service runs at a time.** The **Home** hotkey closes the current service and brings
  you back to the launcher. By default that's the Browser Home key on most air mice and
  media keyboards, or `Ctrl+Alt+H`. Closing a service window with Alt+F4 also brings you back.
- **Sleep** and **Exit to Windows** tiles are included.
- The layout scales to fit 1080p or 4K at any Windows display scaling. The mouse pointer
  hides when you stop moving it.

## Build

**GitHub Actions:** push the repo to GitHub and open **Actions › Build TenFoot**. Download
the `TenFoot` artifact, which is a single self-contained `TenFoot.exe`. The PC doesn't need
.NET installed.

**On the PC:** install the .NET 8 SDK, then run:
```powershell
dotnet publish src/TenFoot/TenFoot.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o out
```

## Set up

1. Copy `TenFoot.exe` somewhere permanent, for example `C:\TenFoot\`.
2. Run `TenFoot.exe --autostart` once. It starts now and every time you sign in.
   `TenFoot.exe --no-autostart` turns this off again.
3. Open each service once and sign in. The sign-ins are saved in
   `%LOCALAPPDATA%\TenFoot\EdgeProfile`.
4. Optional: to boot straight to the TV screen, set Windows to sign in automatically.
   Run `netplwiz` and untick *Users must enter a user name and password*. If that option
   is missing, turn off *Require Windows Hello sign-in* in Settings › Accounts first.

## Customise: `%APPDATA%\TenFoot\tiles.json`

TenFoot creates this file on first run. Press **F5** in TenFoot to reload it after editing.

```jsonc
{
  "columns": 4,
  "homeHotkeys": ["BrowserHome", "Ctrl+Alt+H"],   // any WPF Key name, plus Ctrl/Alt/Shift/Win
  "edgePath": null,                                // null = find Edge automatically
  "edgeArguments": ["--start-fullscreen", "--no-first-run", "--no-default-browser-check",
                    "--autoplay-policy=no-user-gesture-required"],
  "hideCursorWhenIdle": true,
  "tiles": [
    { "title": "Netflix", "color": "#8E0A12", "type": "web", "url": "https://www.netflix.com/browse" },
    { "title": "Sky Go",  "color": "#1E3A8A", "type": "shortcut", "shortcut": "Sky Go",
      "closeOnHome": true, "processName": "Sky Go" },
    { "title": "Kodi", "type": "app", "path": "C:\\Program Files\\Kodi\\kodi.exe" },
    { "title": "Sleep", "type": "action", "action": "sleep" }
  ]
}
```

| Tile type  | Fields | What it does |
|------------|--------|--------------|
| `web`      | `url` | Opens the page in a full-screen Edge app window. |
| `shortcut` | `shortcut` | Runs the Start menu or desktop shortcut with that name (exact match first, then partial). |
| `app`      | `path`, `arguments` | Runs an `.exe`. |
| `action`   | `action` | One of `sleep`, `shutdown`, `restart`, `reload` or `exit`. |

Any tile can also have an `image` (path to a PNG or JPG shown above the title) and a `color`.

**Sky Go and the Home key:** by default, Home brings TenFoot to the front but leaves Sky Go
running behind it, and Sky Go keeps playing sound. To have Home close Sky Go too:
1. Start Sky Go.
2. Open Task Manager › Details and find its process name.
3. Put that name in `processName` without the `.exe`, and set `"closeOnHome": true`.

## Picture quality notes

- Edge is used on purpose. Netflix only plays 4K on Windows in Edge or its own app. It also
  needs the *HEVC Video Extensions* codec and an HDCP 2.2 link to the TV, which means a
  4K60 / HDCP 2.2 adapter on the M90n.
- Some services limit browser playback to 1080p or lower. That's their limit, not TenFoot's.
