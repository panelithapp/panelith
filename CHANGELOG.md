# Changelog

Notable changes to Panelith. Versions follow [Semantic Versioning](https://semver.org/), and each section here becomes the notes of its GitHub release.

## [Unreleased]

## [0.1.5] - 2026-10-27

### App

- **Now playing.** A screen shows the title, artist, album and album art of whatever Windows is playing. Long titles slide across the line. The reading stays on this PC.
- **A profile can watch a sensor.** An expression such as `@gpu.temp > 85` or `@cpu.fan < 100` brings that profile up, and it is checked before games and programs, so a warning screen can interrupt a game when the profile sits above Gaming.
- **Choose the sensor behind a theme name** on the Sensors page, instead of editing `panelith.json` by hand. Automatic remains the default.
- **Peak temperatures.** `@cpu.temp.peak` and `@gpu.temp.peak` are the highest values since Panelith started.
- **Desk readings.** `@net.ipv4`, `@audio.device`, `@audio.volume` and `@battery` are theme values. Battery stays hidden on the Desk screen until this PC has one.
- **Align, distribute and match size** in the designer. Ctrl-click selects more than one widget. One widget lines up with the canvas; several line up with each other.
- **A simpler designer.** A new screen can start from three gauges, a big number, or a clock. New widgets land in an open spot. The side panel shows the words, sensor, size and colour, with the rest under More, and screen settings stay behind Screen. Dragging snaps to nearby edges; hold Shift to move freely. Align actions appear once two or more widgets are selected. A colour swatch offers the screen's own colours.

## [0.1.4] - 2026-10-19

A security hardening release. Nothing here changes how Panelith looks or behaves in normal use.

### Security

- **An update is checked again just before it is installed.** Panelith already refused an update whose signature did not match the key built into it, but the installer then waited in a folder this account can write to until the permission prompt was answered. The signature is now checked again immediately before Panelith asks Windows for permission, and the file is held open against changes and deletion until it starts, so the bytes that were checked are the bytes that run. An update that fails the check is deleted and reported rather than run.
- **Screens cannot reach outside their own folder.** The check that keeps a screen's images inside its folder compared paths by prefix, so a screen in `rig` could name a file in a sibling folder such as `rig-other`. All three places that resolve a screen's paths now share one rule. Screens named by id in settings or profiles can likewise no longer point somewhere else entirely.
- **The start-up task is created with the full path to `schtasks.exe`**, so an entry on `PATH` cannot be run with administrator rights instead.
- **Limits on what a shared screen can ask for**: a cap on canvas size, on how many series one graph may plot, and on the number of animation frames, alongside the existing limits on file size, nesting and widget count.
- **`panelith.json` and panel profiles have a size limit** and are moved aside if they exceed it, rather than being read into memory whole at start-up.

### Project

- Dependencies are now locked: every project has a committed `packages.lock.json` recording the exact version and hash of each package, so a build cannot quietly resolve to something other than what was reviewed. Restore also checks every package, direct and transitive, against the advisory database.
- Workflow actions are pinned by commit rather than by tag, CodeQL analysis runs on every push and weekly, and Dependabot watches both packages and actions.
- `SECURITY.md` now states the known trade-offs, including that `yt-dlp` updates itself and that media is decoded in the app's own process.

## [0.1.3] - 2026-10-17

### Fixed

- **In-app updates actually install now.** Choosing **Install … and restart** downloaded the new version correctly, but the step that runs the installer used a plain background script, which Windows silently refuses for an installer that needs administrator rights: no prompt appeared, nothing installed, and Panelith stayed on the old version at every check. It now asks Windows for permission directly, the same as running the installer by hand, so the prompt actually appears and the update completes.

## [0.1.2] - 2026-10-17

### App

- **Always run as administrator** is now on by default and asks Windows for administrator rights every time Panelith opens without them, so CPU temperature, clock and power work from the first launch. Saying no only skips that launch instead of turning the option off, and the switch in **Settings > Start-up** can be changed at any time, not only while elevated. A hidden start stays hidden after the prompt.

## [0.1.1] - 2026-10-16

### App

- **Always run as administrator**: once Panelith notices it is running elevated, it remembers that and asks Windows for permission at every later launch, including one opened without **Run as administrator**, until that request is ever declined. If **Start with Windows** is already on, it is upgraded to the elevated scheduled task automatically, so a restart comes back up elevated too.

### Panels

- The Thermalright Trofeo Vision 9.16" (LY) is now marked **verified**: it has been driven end to end on the hardware, rather than only developed against.

### Project

- Pull requests are now built and tested automatically, and checked for dependencies with known vulnerabilities.
- The app's manifest identified itself by the project's former name; it is now Panelith.

## [0.1.0] - 2026-10-15

The first public release.

### Panels

- Drives the Thermalright Trofeo Vision (6.86", 9.16" and 11.3"), Thermalright Frozen Magic, Frozen Warframe and Grand Vision cooler screens, and Turing Smart Screen, XuanFang, Kipye and UsbMonitor serial panels. See [Device support](docs/device-support.md) for how well each is tested.
- Recognises BeadaPanel, WeAct Display FS and the newer Turing USB panels and explains why they cannot be driven yet.
- Several panels at once, each with its own screen. Panels reconnect by themselves after unplugging, sleep or errors.
- No vendor software or extra USB driver needed on Windows.

### App

- **Dashboard** with a live preview of every panel and the readings that matter.
- **Screens**: 13 built-in screens that adapt to any panel size, plus wallpapers behind any screen.
- **Video screens** from a file or a YouTube link, trimmed and baked for the panel.
- **Designer** for building screens: text, gauges, bars, graphs, pictures, clips and shapes, with live values, layers and undo.
- **Profiles** that switch the screen, brightness and frame rate by game, app, time of day or idle time, with keyboard shortcuts.
- **Sensors** page listing every reading, from LibreHardwareMonitor, HWiNFO, RivaTuner and Windows, with demo sensors for trying Panelith without hardware access.
- Starts with Windows, lives in the tray, and reopens exactly as it was left.
- **In-app updates**: checks for new versions and installs them with one click. Updates are verified with an Ed25519 signature before they run.

### Command line

- `panelith` lists panels and sensors, drives panels, renders previews, validates screens and diagnoses problems, on Windows and Linux.

### Licensing

- Panelith is now licensed under the PolyForm Noncommercial License 1.0.0. Earlier, unreleased versions were Apache-2.0.

[0.1.4]: https://github.com/panelithapp/panelith/releases/tag/v0.1.4
[0.1.3]: https://github.com/panelithapp/panelith/releases/tag/v0.1.3
[0.1.2]: https://github.com/panelithapp/panelith/releases/tag/v0.1.2
[0.1.1]: https://github.com/panelithapp/panelith/releases/tag/v0.1.1
[0.1.0]: https://github.com/panelithapp/panelith/releases/tag/v0.1.0
