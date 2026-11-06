# Theme Toggle

A tiny Windows 11 tray app. Click the sun/moon icon to switch between light and dark mode (apps and system).

![Theme Toggle](ThemeToggle-post.png)

## Use

1. Download `ThemeToggle.exe` from the [latest release](../../releases/latest).
2. Run it. On first launch it asks whether to start with Windows.
3. Left-click the tray icon to toggle the theme. Right-click for "Start with Windows" and Exit.

`ThemeToggle.exe --toggle` switches once and exits, handy for shortcuts and scripts.

Windows 11 hides new tray icons in the `^` overflow menu; drag the icon onto the taskbar to keep it visible.

The exe is unsigned, so SmartScreen may warn on first run ("More info", then "Run anyway").

## Build

Needs nothing beyond Windows itself (uses the .NET Framework compiler that ships with it):

```
build.cmd
```

## Why not a Quick Settings tile?

Windows 11 does not let third-party apps add tiles to Quick Settings, so a tray icon is the closest option.

## License

MIT
