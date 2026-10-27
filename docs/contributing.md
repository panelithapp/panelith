# Contributing

Thank you for helping. Panelith is licensed under the [PolyForm Noncommercial License](../LICENSE), and contributions are accepted on these terms:

- By sending a change, you agree that it may be distributed under Panelith's license, and that Bharath Vadlamannati may also license it to others, including commercially. This keeps one owner able to license the whole project. If you cannot agree to that, open an issue describing the change instead.
- Only send code you wrote or have the right to contribute. In particular, do not copy code from GPL-licensed panel projects; describe the protocol instead (see [Protocols](protocols.md)).

## Reporting bugs and asking for features

Use the [issue forms](https://github.com/panelithapp/panelith/issues/new/choose). For bugs, the version, the panel, and a log file from `%APPDATA%\Panelith\logs` make the difference. Report security problems privately: see [SECURITY.md](../SECURITY.md).

## Adding a panel

What helps most, roughly in order:

1. The output of `panelith devices -v` with the panel plugged in.
2. The panel's USB descriptors (for example from USBView on Windows, or `lsusb -v` on Linux).
3. A USB capture of the vendor's software sending a few frames (Wireshark with USBPcap), with a note of what was on screen.
4. Links to any published protocol notes.

A new size of an existing family is usually just a profile entry in [`src/Panelith.Devices/Profiles/Builtin`](../src/Panelith.Devices/Profiles/Builtin). A new protocol needs a driver in `src/Panelith.Devices` and byte-layout tests in `tests/Panelith.Devices.Tests`.

## Sharing screens

Screens are folders with a `theme.json` (see [Screen file format](theme-format.md)). To propose one for inclusion, open a pull request adding it under `themes/`, with only assets you have the right to share, and check it with `panelith theme validate`.

## Sending changes

1. Build and test as described in [Building](building.md). `dotnet test Panelith.slnx` must pass with no warnings.
2. Keep each pull request to one change, with tests.
3. Write the commit message in plain language: what changed for the user, and why.
