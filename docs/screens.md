# Screens

A *screen* is one layout for the panel: a set of gauges, bars, graphs, text, pictures or videos bound to live sensor readings. Panelith comes with fourteen, and you can make your own with the [Designer](designer.md) or from a [video](video-screens.md).

![The Screens page](images/screens.png)

## Putting a screen on the panel

Click a screen on the **Screens** page. It goes on the panel immediately, without reconnecting, and Panelith remembers it for next time.

Each card's **⋯** menu can put a picture or video behind the screen (**Wallpaper…**), take it off again, or delete one of your own screens. The **⋯** menu at the top of the page reloads screens after you edit their files, and opens your screens and videos folders.

## Rotation

Tick **In rotation** on the screens you want to cycle through and turn on **Rotate screens**. Set how long each stays up with **Every … seconds**. The rotation belongs to the active [profile](profiles.md), so a Gaming profile can rotate game screens while Everyday rotates others.

**Next screen** (in the window, the tray menu, or **Ctrl+Alt+Right**) steps forward at any time.

## Wallpapers

Any screen can have a picture or video behind its widgets. Choose **Wallpaper…** from a card's **⋯** menu. A screen that comes with Panelith is copied into your own screens first, so the original stays untouched. Videos are prepared once, as described in [Video screens](video-screens.md).

## The screens that come with Panelith

All of them are drawn for a 1920×480 panel and adapt to 1280×480; the images below are rendered with demo sensors.

**Aurora**: five glowing ring gauges for CPU, GPU and memory, with the clock up top.
![Aurora](images/screens/aurora.png)

**Clock**: for when you step away; a wall-to-wall clock and date, with temperatures in small print.
![Clock](images/screens/clock.png)

**Desk**: a calm workday board with the time, three soft rings and network and disk.
![Desk](images/screens/desk.png)

**Esports**: three giant high-contrast numbers you can read mid-match.
![Esports](images/screens/esports.png)

**Game**: FPS, frame time and 1% lows from RivaTuner, with GPU and CPU vitals.
![Game](images/screens/game.png)

**GPU Focus**: everything about the graphics card.
![GPU Focus](images/screens/gpu-focus.png)

**Minimal**: a few large readings and lots of space.
![Minimal](images/screens/minimal.png)

**Neon**: segmented bars in a synthwave palette.
![Neon](images/screens/neon.png)

**Night**: dim and warm, for a dark room.
![Night](images/screens/night.png)

**Now playing**: the song or video Windows is playing, with its title, artist and album art. A long title slides across the line. The picture and the names stay on this PC.

**Overview**: the most information on one screen.
![Overview](images/screens/overview.png)

**Pulse**: live graphs of load and temperature.
![Pulse](images/screens/pulse.png)

**Rig**: the original Panelith dashboard, CPU and GPU side by side with a history graph.
![Rig](images/screens/rig.png)

**Studio**: for streaming and creating: encoder load, network upload and system health.
![Studio](images/screens/studio.png)

## Where your screens live

Your own screens are folders in `%APPDATA%\Panelith\themes`, one per screen, each with a `theme.json` and any pictures or clips it uses. **Open my screens folder**, in the **⋯** menu at the top of the Screens page, goes there. You can copy a folder to another PC to share a screen; the format is described in [Screen file format](theme-format.md).
