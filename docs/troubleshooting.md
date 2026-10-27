# Troubleshooting

## The panel stays dark

1. **Close the panel's own software.** Thermalright TRCC keeps the panel open even from the notification area; quit it there, or end it in Task Manager. Only one program can use the panel.
2. **Check the status** at the bottom left of the window:
   - *No panel connected*: Windows does not see the panel. Try another USB port (a port on the motherboard rather than a hub or front-panel header), and another cable if it has one.
   - *connecting* for a long time, or *failed*: open **Settings > Activity** for the reason.
3. **Unplug and replug the panel.** Panelith reconnects by itself after unplugging, sleep and resume.
4. **Restart the panel's power.** Some panels need a power cycle after firmware errors; shut down the PC fully, or unplug the panel for ten seconds.
5. On a **6.86" Trofeo** that is detected as a cooler screen, or the other way round, see [Device support](device-support.md#one-usb-id-several-products).

The `panelith devices` command lists exactly what is plugged in and why something cannot be used; see [Command line](cli.md).

## CPU temperature shows `--`

Most CPUs only report temperature, clock and power to programs running as administrator through the PawnIO driver.

1. **Settings > Sensors > Install PawnIO**.
2. **Settings > Sensors > Restart as administrator**.
3. To always start elevated, turn on **Start with Windows** while elevated.

If it still shows `--`, check the **Sensors** page for what `@cpu.temp` resolves to. Running HWiNFO with Shared Memory Support on gives Panelith a second source.

## In-game FPS shows `--`

FPS comes from RivaTuner Statistics Server. Install it (it comes with MSI Afterburner), make sure it is running, and that the game is not in its list of applications with detection turned off. **Settings > In-game FPS** shows what Panelith sees.

## The wrong profile comes up

The **Profiles** page and the card at the bottom left show why the active profile was chosen. Profiles are checked top to bottom, so move the one you want higher, or narrow its conditions. To stop switching, pin a profile or turn off **Automatic switching**.

## Videos will not open

- On **Windows N** editions, install the Media Feature Pack from **Settings > Sensors**, then restart.
- Some formats need a codec from the Microsoft Store: **HEVC Video Extensions** for H.265, **AV1 Video Extension** for AV1, **VP9 Video Extensions** for WebM.
- YouTube changes often. When a download fails, the YouTube window offers **Update downloader**, which fetches the newest yt-dlp into your own folder. If a link still fails, download the video another way and use **From a video on this PC…**.

## A deleted screen comes back

Panelith will not delete a screen while something is still reading its files, usually a video it was playing. It keeps trying for five seconds, and if the files are still held it puts the screen back rather than leaving a half-deleted one behind. Show a different screen, wait a moment, and delete it again. The log says `Could not delete` when this happens.

## A shortcut does nothing

Another program may already use it; **Settings > Shortcuts** says so. Pick another combination. Some games running as administrator only see shortcuts from programs that also run as administrator.

## Panelith says it could not start

The window shows the reason. The most common cause is damaged settings: Panelith moves an unreadable `app.json`, `session.json` or `profiles.json` aside (renamed `.bad`) and starts with defaults, so this rarely stops it. If it does, rename `%APPDATA%\Panelith` and start again.

## Reporting a problem

[Open an issue](https://github.com/panelithapp/panelith/issues/new/choose) and include:

- the Panelith version (bottom of **Settings**) and your Windows version;
- the panel model, and the line about it from `panelith devices` if you can;
- the newest log file from `%APPDATA%\Panelith\logs`. Logs write your profile folder as `%USERPROFILE%`, so they do not reveal your account name.
