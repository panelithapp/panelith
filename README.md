<p align="center">
  <img src="src/Panelith.App/Assets/panelith.png" width="96" alt="Panelith logo" />
</p>

<h1 align="center">Panelith</h1>

<p align="center">
  <b>Live PC stats, clocks, games and video loops on your USB sensor panel.</b><br />
  Built for the Thermalright Trofeo Vision. Free for noncommercial use.
</p>

<p align="center">
  <a href="https://github.com/panelithapp/panelith/releases/latest"><b>Download for Windows</b></a> ·
  <a href="docs/getting-started.md">Getting started</a> ·
  <a href="docs/README.md">User guide</a> ·
  <a href="https://github.com/panelithapp/panelith/wiki">Wiki</a> ·
  <a href="CHANGELOG.md">What's new</a>
</p>

![The Panelith dashboard with the Aurora screen live on a Trofeo Vision](docs/images/dashboard.png)

## What is Panelith?

Small USB screens have become a popular way to show CPU and GPU temperatures inside a PC case. The software that comes with them tends to be closed, Windows-only, and tied to one brand. Panelith is a replacement: one app that draws a screen you choose, or one you design, on the panel, and keeps it live with your machine's real sensors.

- **Thirteen ready-made screens** for gaming, streaming, work and night time, each designed for the 1920×480 and 1280×480 Trofeo sizes.
- **A designer** for making your own: drag gauges, bars, graphs, text, pictures and videos onto a live canvas.
- **Video screens and wallpapers** from your own videos or a YouTube link, prepared once so playback costs almost nothing.
- **Profiles** that switch the panel by themselves: a game screen when a game starts, a clock when you step away, a dim night screen on a schedule.
- **Every sensor on your PC** through LibreHardwareMonitor, HWiNFO and RivaTuner (for in-game FPS), plus network, disks and the time.
- **In-app updates** that are checked against a signature before anything is installed.
- **Gentle on your PC**: frames that did not change are not sent, the frame rate is capped, and nothing is drawn while the window is hidden.

## Screenshots

| Pick a screen | Design your own |
| --- | --- |
| ![The Screens page](docs/images/screens.png) | ![The Designer](docs/images/designer.png) |
| **Switch automatically with profiles** | **See every sensor** |
| ![The Profiles page](docs/images/profiles.png) | ![The Sensors page](docs/images/sensors.png) |

### A few of the screens that come with Panelith

![Aurora](docs/images/screens/aurora.png)
![Esports](docs/images/screens/esports.png)
![Neon](docs/images/screens/neon.png)
![Studio](docs/images/screens/studio.png)

See all of them in [Screens](docs/screens.md).

## Install

1. Download `Panelith-<version>-setup.exe` from the [latest release](https://github.com/panelithapp/panelith/releases/latest).
2. Close Thermalright TRCC or any other panel software. Only one program can talk to the panel at a time.
3. Run the installer. Windows may warn that the publisher is not recognised; see [Installing](docs/installing.md#windows-says-the-publisher-is-unknown) for why and how to check the download.
4. Panelith opens, finds the panel and puts a screen on it.

Panelith needs Windows 10 (1809) or Windows 11, 64-bit. Once installed, it updates itself: see [Updates](docs/updates.md).

## Supported panels

| Panel | USB id | Status |
| --- | --- | --- |
| Thermalright Trofeo Vision 9.16" and 11.3" | `0416:5408` | Verified on the 9.16" |
| Thermalright Trofeo Vision 9.16" (LY1 firmware) | `0416:5409` | Supported, not yet tested on hardware |
| Thermalright Trofeo Vision 6.86" | `0416:5302` | Supported |
| Thermalright Frozen Magic, Frozen Vision, Grand Vision, Frozen Warframe | various | Driver included, not yet tested on hardware |
| Turing Smart Screen 3.5" and 5", XuanFang, Kipye, UsbMonitor | various | Driver included, not yet tested on hardware |
| Turing USB models, BeadaPanel, WeAct Display FS | various | Recognised and explained, not yet drivable |

The full list, with what "tested" means, is in [Device support](docs/device-support.md). Panelith is not affiliated with Thermalright, Turing or any other panel vendor.

## Documentation

The [user guide](docs/README.md) covers everything, and the same pages are on the [wiki](https://github.com/panelithapp/panelith/wiki):

- [Installing](docs/installing.md) and [Getting started](docs/getting-started.md)
- [Screens](docs/screens.md), [Video screens](docs/video-screens.md), [Designer](docs/designer.md) and [Profiles](docs/profiles.md)
- [Sensors](docs/sensors.md), [Settings](docs/settings.md) and [Updates](docs/updates.md)
- [Troubleshooting](docs/troubleshooting.md) and [FAQ](docs/faq.md)
- For screen authors: [Screen file format](docs/theme-format.md)
- For developers: [Building](docs/building.md), [Command line](docs/cli.md), [Architecture](docs/architecture.md) and [Releasing](docs/releasing.md)

## Privacy

Panelith has no accounts, no analytics and no telemetry. It goes online only to check GitHub for updates (which you can turn off), and to download a YouTube video when you ask it to. See [Privacy](docs/privacy.md).

## License

Copyright (c) 2026 Bharath Vadlamannati.

Panelith is source-available under the [PolyForm Noncommercial License 1.0.0](LICENSE). You may use, study, change and share it for any noncommercial purpose: personal use, hobby projects, research, teaching, and use by charities and public institutions. Selling it, bundling it with a product, or using it in a business needs a separate license; contact bharry29@gmail.com.

Panelith includes third-party components under their own licenses; see [NOTICE](NOTICE) and [Third-party licenses](docs/third-party-licenses.md). The device protocol code is a clean-room implementation; see [Protocols](docs/protocols.md).
