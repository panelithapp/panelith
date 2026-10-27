# Profiles

A *profile* is a set of screens that comes up by itself: when a sensor crosses a line, when a game or app starts, on a schedule, or when you step away. Panelith checks what the PC is doing every two seconds and shows the right profile.

![The Profiles page](images/profiles.png)

## The starter profiles

| Profile | Comes up when |
| --- | --- |
| **Gaming** | Any game starts: from Steam, Epic, Xbox, EA, Ubisoft, GOG or Riot libraries, anything RivaTuner is measuring, or a fullscreen window keeping the GPU busy |
| **Streaming** | OBS, Streamlabs, XSplit or vMix is running |
| **Creating** | Premiere Pro, After Effects, DaVinci Resolve, Photoshop, Lightroom, Blender, Unreal, Unity and about a dozen more creative apps are running |
| **Away** | The PC has been idle for 5 minutes, or is locked |
| **Night** | Every day from 23:00 to 07:00 |
| **Work** | Weekday working hours (off by default) |
| **Everyday** | Nothing else applies (the default) |

Each also has a shortcut, **Ctrl+Alt+1** to **Ctrl+Alt+7** in the order Everyday, Gaming, Streaming, Creating, Away, Night, Work, that pins it. Change any of them, or add your own with **New**.

## Which profile wins

Profiles are checked from the top of the list down, and the first one whose conditions match is shown. A sensor condition is checked before programs and games, so a warning profile has to sit above Gaming if it should interrupt a game. Use the arrow buttons to reorder them. The default profile is used when nothing else matches.

The card at the bottom left of the window shows the active profile and why it was chosen, for example *"Gaming · Cyberpunk2077.exe"*.

## Editing a profile

Select a profile to edit it:

- **Name, colour and icon**, shown in the window and the tray menu.
- **Can turn on by itself**: when off, the profile only comes up when you pin it.
- **Default when nothing else applies**: exactly one profile is the default.
- **Screens**: the screens it shows. The first one comes up when the profile starts. **Rotate every … seconds** cycles through them.
- **When**: any one of these brings the profile up:
  - **A sensor crosses a line**: an expression such as `@gpu.temp > 85` or `@cpu.fan < 100`. A sensor this PC does not have does not match, so a missing battery does not bring the profile up;
  - **Any game starts**, optionally counting a fullscreen window that keeps the GPU above 50% for 10 seconds;
  - **These programs are running**: add programs by name, such as `obs64`;
  - **On a schedule**: days and times, which may cross midnight;
  - **After … minutes idle**, or **when the PC is locked**;
  - **Shortcut**: a global keyboard shortcut that pins this profile.
- **Panel**: dim the panel, or run it at a different frame rate, while this profile is active.

## Pinning

**Use now** (or the pin button at the bottom left of the window) pins a profile: it stays up until you unpin it, whatever the PC is doing. Pick a profile from the tray menu to pin it, or **Automatic** to go back to switching by itself. **Ctrl+Alt+P** steps through profiles, pinning each.

Turning **Automatic switching** off keeps the pinned or default profile up permanently.

Profiles, the pinned profile and automatic switching are saved in `%APPDATA%\Panelith\profiles.json` and survive restarts.
