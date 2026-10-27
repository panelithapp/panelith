# Configuration files

Panelith keeps everything in one folder:

- Windows: `%APPDATA%\Panelith`
- Linux: `$XDG_CONFIG_HOME/panelith` (usually `~/.config/panelith`)
- macOS: `~/Library/Application Support/Panelith`

Set the `PANELITH_HOME` environment variable to use another folder, for example a portable setup or a test.

| File or folder | Contents | Written by |
| --- | --- | --- |
| `panelith.json` | Panel settings: the screen, frame rate, quality, orientation, per-panel overrides, sensor options | The app and the command line |
| `app.json` | Desktop preferences: start-up, tray, shortcuts, demo sensors, smooth motion, update checks | The app |
| `session.json` | How the window was left: size, position, page, and choices on each page | The app, when it closes or hides |
| `profiles.json` | Profiles, the pinned profile and automatic switching | The app |
| `updates.json` | When updates were last checked | The updater |
| `themes\` | Your own screens, one folder each | The app (Designer, video screens) |
| `logs\` | Daily log files, kept for 14 days | The app |

Video screens also keep copies in `%USERPROFILE%\Videos\Panelith`: `Downloads` for full YouTube downloads and `Screens` for an MP4 of each loop.

All files are JSON and can be edited while Panelith is closed. A file that cannot be read is renamed with `.bad` (or `.broken` for `panelith.json`) and replaced with defaults, so a typo never stops Panelith from starting; the log says what happened.

## panelith.json

```json
{
  "version": 1,
  "discoveryIntervalSeconds": 3,
  "defaults": {
    "theme": "rig",
    "framesPerSecond": 15,
    "jpegQuality": 85,
    "orientation": "portrait",
    "enabled": true
  },
  "panels": [
    { "key": "<stable key from panelith devices>", "displayName": "Desk", "forceProfile": "thermalright.frozen-magic-hid", "theme": "clock" }
  ],
  "sensors": {
    "pollIntervalSeconds": 1,
    "historySeconds": 300,
    "disabledSources": ["hwinfo"],
    "aliasOverrides": { "@cpu.temp": "cpu/0/temperature/package" }
  }
}
```

- `defaults` applies to every panel. Each entry in `panels` overrides it for one panel, matched by the stable `key` that `panelith devices` prints, and takes the same settings plus `displayName`. `forceProfile` is how to drive a panel whose USB id is shared by several products.
- `orientation` is `portrait` (the panel's native way up) or `reversePortrait` (upside down). Other settings: `brightness` (0 to 100), `flipHorizontally` for panels behind a mirror.
- `sensors.disabledSources` turns off a sensor source; `aliasOverrides` chooses the sensor behind a theme value. The Sensors page writes the same choice (see [Sensors](sensors.md)).
