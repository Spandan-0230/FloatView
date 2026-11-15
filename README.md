# FloatView

A focused Picture-in-Picture utility for Windows. Pick any open window,
fine-tune the exact crop you want, and keep it floating above your work as a
live, always-on-top preview.

FloatView is an independent, from-scratch C# / WPF application with no external
packages or runtime downloads.

## Features

- **Any window** — mirror browsers, video players, dashboards, documents, or
  anything else with a top-level window.
- **Precise region selection** — the picker opens on a suggested crop. Drag
  inside the selection to move it, drag the edges or corner handles to resize,
  and watch the live size readout. Clicking the dimmed area is ignored, and no
  mouse or wheel input ever reaches the source window.
- **Live DWM preview** — smooth, hardware-composited mirroring with no screen
  recording overhead.
- **Always on top** — keep the preview visible while you work in other apps.
- **Minimized-source handling** — Windows pauses rendering for minimized
  windows, so FloatView swaps the frozen frame for a clean restore card. Enable
  *Always restore automatically* and the source is brought back without
  stealing focus, keeping the preview live.
- **Media controls** — an optional toolbar cluster adds play/pause and ±5 s seek
  for video sources without opening the player. Websites such as YouTube are
  controlled through the Windows media session, so they respond even when the
  page is not focused; players without a media session fall back to posted
  player keys, and the preview never loses focus.
- **Aspect-ratio lock** — resize without distorting the source.
- **Hover toolbar** — change the source window or close the preview from a
  compact floating toolbar.
- **Launcher on close** — closing the preview returns to the launcher instead of
  leaving the app running without a visible window.
- **Region memory** — FloatView remembers the last selected area for each app.
- **Pure-black UI** — black surfaces with a single white accent. Buttons get a
  soft white glow on hover and keyboard focus.

## Requirements

- Windows 10 version 2004 (build 19041) or newer
- Windows 11 recommended for rounded window corners
- 64-bit Windows

## Install

Run `FloatView-Setup-<version>-x64.msi` and follow the prompts. The installer
targets the current user only, so it does not ask for administrator rights. It
installs to `%LocalAppData%\Programs\FloatView` and adds a Start menu shortcut.

After installation, press the Windows key and type **FloatView** to open it.
Right-click the result to pin FloatView to Start or the taskbar. To remove it,
use **Settings > Apps > Installed apps > FloatView > Uninstall**. Installing a
newer MSI upgrades the existing copy in place; if FloatView is running, the
installer asks it to close before replacing the executable.

## Usage

1. Open FloatView from Windows Search, the Start menu, or its shortcut.
2. Select the window you want to mirror and click **Start Picture-in-Picture**.
   FloatView brings that window to the front first, then opens the region picker
   over it.
3. The picker opens on a suggested crop. Drag inside it to move the selection,
   drag an edge or corner handle to resize, then press **Enter** or click
   **Start PiP**. Clicks in the dimmed area do nothing, so choosing a region
   never disturbs the source.
5. Move the floating preview by dragging it. Hover it to reveal the toolbar for
   aspect-ratio lock, source change, and close. Closing the preview reopens the
   launcher.
6. Click the music-note button in the preview toolbar to show the optional media
   controls. Play/pause and seek use the Windows media session when the source
   publishes one — this is what makes video work even when the page is not focused, with exact ±5-second seeks.
   Players without a media session fall back to posted player shortcuts; VLC,
   for example, follows its short-jump setting (10 seconds by default). Raise
   `SeekPresses` in the settings file to send more than one jump per click.
7. If the source is minimized, the preview pauses and shows a restore card.
   Tick *Always restore automatically* to bring the source back without focus
   stealing and keep the live preview running. To keep the preview live without
   auto-restore, switch windows with **Alt+Tab** instead of minimizing.
8. Press **Esc** while selecting a region to cancel.

## UI 
<img width="460" height="275" alt="image" src="https://github.com/user-attachments/assets/9e372abd-5966-4615-bf04-4fa34d7ffc8b" /><br>
<img width="439" height="325" alt="image" src="https://github.com/user-attachments/assets/158d8d2b-2837-4b55-a2ac-a58ae9955745" /><br>
<img width="916" height="717" alt="image" src="https://github.com/user-attachments/assets/4cc2b508-cfa0-4d72-9a36-781866d1bc57" /><br>
- Add as many it needs to get you in shape... <br><br>
<img width="1272" height="618" alt="image" src="https://github.com/user-attachments/assets/c786972e-12f5-4ab5-9273-00ef2c7e3889" /><br>

- You can use it over VLC or any app-based video and control it.<br>
<img width="257" height="41" alt="image" src="https://github.com/user-attachments/assets/50bc7206-1b43-4897-9542-0adde23c39c7" /> 

## Build

Requires the .NET 8 SDK on Windows.

```powershell
dotnet build FloatView.sln -c Release
```

To create a self-contained single-file executable:

```powershell
dotnet publish FloatView\FloatView.csproj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o publish
```

The result is `publish\FloatView.exe`.

### Build the installer

The MSI uses WiX Toolset 4.0.5. Later WiX releases require accepting the paid
Open Source Maintenance Fee EULA, so this project stays on 4.0.5. Install the
tool and its Util extension once:

```powershell
dotnet tool install --global wix --version 4.0.5
wix extension add -g WixToolset.Util.wixext/4.0.5
```

Then run the build script from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
```

The script publishes the self-contained executable and writes
`dist\FloatView-Setup-<version>-x64.msi`. The MSI is per-user: it installs to
`%LocalAppData%\Programs\FloatView`, creates the Start menu shortcut that
Windows Search and pinning use, and registers FloatView in Apps & Features.

## Data and privacy

FloatView works entirely on your machine. It does not record audio or video, and
it does not send any data over the network.

Settings are stored at `%LocalAppData%\FloatView\settings.json`. Diagnostics are
written to `%LocalAppData%\FloatView\floatview.log`.

Settings include `AutoRestoreSource` (restore minimized sources automatically),
`ShowMediaControls` (remember the media-controls toggle), and `SeekPresses`
(number of player jumps sent per seek click).

## Attribution

The concept of a Windows Picture-in-Picture utility was inspired by the
open-source project [PiP-Tool](https://github.com/LionelJouin/PiP-Tool) by
Lionel Jouin. FloatView contains no source code, icons, artwork, fonts, or other
assets from that project.

Copyright (c) 2026 Spandan. All rights reserved. See [LICENSE](LICENSE).
