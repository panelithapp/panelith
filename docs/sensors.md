# Sensors

![The Sensors page](images/sensors.png)

## Where readings come from

On Windows, Panelith combines:

- **LibreHardwareMonitor**, built in, for CPU, GPU, memory, disks, fans and motherboard sensors. CPU temperature, clock and power need administrator rights and, on most CPUs, the **PawnIO** driver.
- **HWiNFO**, when it is running with **Shared Memory Support** turned on. Panelith uses its readings too, which helps on hardware LibreHardwareMonitor does not know.
- **RivaTuner Statistics Server** (RTSS), for in-game FPS, frame time and 1% lows. It comes with MSI Afterburner, or can be installed on its own.
- **Windows itself**, for network, disk activity, uptime, the time, the computer name, the IPv4 address, and the default playback device and its volume. The song or video that is playing, including its album art, is read from the Windows media session on this PC. None of it is sent to a music service.

On Linux, the command-line `panelith` reads `/sys/class/hwmon` and `/proc`.

**Demo sensors** in [Settings](settings.md) replace all of these with animated made-up values, for designing screens on a machine without access to its sensors.

## Theme values

Screens refer to sensors by portable names such as `@cpu.temp`, and Panelith picks the matching sensor on each machine. The **Theme values** list on the Sensors page shows what each name resolves to on your PC.

| Name | Meaning | Name | Meaning |
| --- | --- | --- | --- |
| `@cpu.temp` | CPU temperature | `@gpu.temp` | GPU temperature |
| `@cpu.load` | CPU load, % | `@gpu.load` | GPU load, % |
| `@cpu.clock` | CPU clock | `@gpu.clock` | GPU clock |
| `@cpu.power` | CPU power, W | `@gpu.power` | GPU power, W |
| `@cpu.fan` | CPU fan, RPM | `@gpu.fan` | GPU fan, RPM |
| `@cpu.name` | CPU model | `@gpu.name` | GPU model |
| `@cpu.core1` … | Load of one core | `@gpu.video` | GPU video engine load, % |
| `@mem.used` | Memory used | `@gpu.vram.used` | GPU memory used |
| `@mem.total` | Memory installed | `@gpu.vram.total` | GPU memory total |
| `@mem.load` | Memory used, % | `@gpu.vram.load` | GPU memory used, % |
| `@net.down` | Download rate | `@net.up` | Upload rate |
| `@net.ipv4` | This PC's IPv4 address | `@battery` | Battery charge, % |
| `@audio.device` | Playback device | `@audio.volume` | Volume, % |
| `@media.title` | What is playing | `@media.artist` | Artist |
| `@media.album` | Album | `@cpu.temp.peak` | Highest CPU temperature since Panelith started |
| `@gpu.temp.peak` | Highest GPU temperature since Panelith started | `@disk.load` | Disk activity, % |
| `@disk.temp` | Disk temperature | `@disk.read` | Disk read rate |
| `@disk.write` | Disk write rate | `@disk.used` | System drive used |
| `@disk.free` | System drive free | `@game.fps` | Game FPS (RTSS) |
| `@game.frametime` | Game frame time, ms | `@game.low1` | Game 1% low FPS |
| `@game.name` | The game RTSS is measuring | `@sys.time` | The time |
| `@sys.date` | The date | `@sys.uptime` | Time since start-up |
| `@sys.host` | Computer name | `@sys.os` | Windows version |
| `@panel.fps` | The panel's own frame rate | | |

## Every sensor

The **Every sensor** list shows every raw reading with its id in brackets, such as `[cpu/0/temperature/package]`. Use the bracketed id in a screen for a reading that has no theme value: a single fan header, a VRM temperature, a second GPU. Ids are specific to the machine, so a screen that uses them may show `--` on another PC.

Type in the filter box to search names, ids and sources.

## Choosing a different sensor for a theme value

If Panelith picks the wrong sensor for a name (for example the wrong one of two GPUs), use **Choose the sensor behind a name** on the Sensors page. Pick the theme name, then the raw sensor, or **Automatic** to go back to the built-in choice. The same choice can be written in `panelith.json`, using an id from the **Every sensor** list:

```json
{
  "sensors": {
    "aliasOverrides": { "@cpu.temp": "cpu/0/temperature/package" }
  }
}
```

See [Configuration files](configuration.md).
