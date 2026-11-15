# FloatView

A focused Picture-in-Picture utility for Windows. Pick any open window, drag out
the exact area you want, and keep it floating above your work as a live,
always-on-top preview.

FloatView is an independent, from-scratch C# / WPF application with no external
packages or runtime downloads.

## Features

- **Any window** — mirror browsers, video players, dashboards, documents, or
  anything else with a top-level window.
- **Precise region selection** — drag, move, and resize the selection with a
  live size readout.
- **Live DWM preview** — smooth, hardware-composited mirroring with no screen
  recording overhead.
- **Always on top** — keep the preview visible while you work in other apps.
- **Minimized-source handling** — Windows pauses rendering for minimized
  windows, so FloatView swaps the frozen frame for a clean restore card. Enable
  *Always restore automatically* and the source is brought back without
  stealing focus, keeping the preview live.
- **Media controls** — an optional toolbar cluster adds play/pause and
  back/forward seek for video sources without opening the player. Commands are
  posted to the source window, so the preview never loses focus.
- **Aspect-ratio lock** — resize without distorting the source.
- **Hover toolbar** — change the source window or close the preview from a
  compact floating toolbar.
- **Region memory** — FloatView remembers the last selected area for each app.
- **Dark, distraction-free UI** — neutral charcoal surfaces with a single blue
  accent.

## Requirements

- Windows 10 version 2004 (build 19041) or newer
- Windows 11 recommended for rounded window corners
- 64-bit Windows

## Usage

1. Launch `FloatView.exe`.
2. Select the window you want to mirror.
3. Click **Start Picture-in-Picture**.
4. Drag over the source window to choose the area, then press **Enter** or click
   **Start PiP**.
5. Move the floating preview by dragging it. Hover it to reveal the toolbar for
   aspect-ratio lock, source change, and close.
6. Click the music-note button in the preview toolbar to show the optional media
   controls: play/pause, seek back, and seek forward. Seeking posts the player's
   own back/forward shortcut, so about 5 seconds in browsers; VLC follows its
   configured short-jump value. Raise `SeekPresses` in the settings file to send
   more than one jump per click. Play/pause works with VLC and Chromium browsers
   playing web video (YouTube and similar sites); other native players work when
   they handle Windows media commands.
7. If the source is minimized, the preview pauses and shows a restore card.
   Tick *Always restore automatically* to bring the source back without focus
   stealing and keep the live preview running. To keep the preview live without
   auto-restore, switch windows with **Alt+Tab** instead of minimizing.
8. Press **Esc** while selecting a region to cancel.

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
