# Building from source

## Requirements

- The [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is pinned in [`global.json`](../global.json)).
- Windows 10 or 11 for the desktop app. The command line and the tests also build on Linux and macOS.
- Optional, for installers: [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Build and test

```powershell
git clone https://github.com/panelithapp/panelith.git
cd panelith
dotnet build Panelith.slnx
dotnet test Panelith.slnx
```

Warnings are errors, so a clean build has none.

## Dependencies

Each project has a committed `packages.lock.json` recording the exact version and
hash of every package it uses, directly or indirectly. CI restores in locked mode,
which fails if a project asks for anything the lock files do not already describe,
so a dependency cannot change without a reviewable diff.

Regenerate the lock files and commit them alongside the change whenever a package
version changes, and whenever the version in `Directory.Build.props` changes,
since the lock files record project versions too:

```powershell
dotnet restore Panelith.slnx --force-evaluate
```

The lock files describe a plain, runtime-independent restore. Release builds name
a runtime and so resolve extra runtime assets; `build/publish.ps1` leaves the lock
files untouched rather than rewriting them with runtime-specific entries.

Restore also checks every package against the advisory database and reports
findings as `NU1901`–`NU1904` warnings. These are warnings rather than errors so
a new advisory cannot break the build of an already-tagged commit; CI fails on
them separately, so they still get noticed.

## Run

```powershell
dotnet run --project src/Panelith.App          # the desktop app
dotnet run --project src/Panelith.Cli -- run   # the command line
```

Set `PANELITH_HOME` to a scratch folder to keep a development copy away from your real settings. Turn on **Demo sensors** to design without hardware access. A copy run from source never updates itself.

## Publish and package

```powershell
./build/publish.ps1                 # self-contained app in artifacts\publish\app, and the installer
./build/publish.ps1 -SkipInstaller  # just the app
```

`publish.ps1` first refuses to continue if any NuGet package, direct or transitive, has a known vulnerability. It then downloads the bundled tools (yt-dlp, QuickJS-ng, PawnIO setup) at pinned versions, checking each against its published SHA-256, publishes the app, signs it when a certificate is configured, and builds the installer.

To make a release, see [Releasing](releasing.md).

## Screenshots

```powershell
./build/capture-screenshots.ps1
```

Starts the published app once per page with demo sensors and a throwaway settings folder, and saves the images in `docs/images`, including a render of every bundled screen. Quit any running Panelith first.

## Code style

- Match the code around your change: naming, comments and idioms.
- Comments explain constraints the code cannot show, not what the next line does.
- New behaviour comes with tests. Hardware-facing code is tested against byte layouts, not devices.
