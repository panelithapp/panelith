# Designer

The **Designer** builds screens on a live canvas that shows real sensor values as you work.

![The Designer](images/designer.png)

## Starting

- Pick an existing screen from the list at the top to change it. Screens that come with Panelith are never changed: saving makes your own copy, marked **(custom)**.
- Or start from a layout: **Blank**, **Three gauges** (CPU, GPU and memory), **Big number**, or **Clock**. Each one is already the size of the panel.

Panelith remembers which screen was open the next time you start it.

## Adding widgets

The **Add** bar puts a widget in the first open spot, so the next one does not land on top of the last:

| Widget | Shows |
| --- | --- |
| **Text** | Any text, with sensor values inside braces, such as `CPU {@cpu.temp:0}°` |
| **Bar** | A value as a filled bar, optionally in segments |
| **Gauge** | A value as a ring or arc |
| **Graph** | One or more values over the last minutes |
| **Shape** | A rectangle, rounded rectangle or ellipse, for panels and dividers |
| **Picture…** | A PNG, JPEG, BMP, WebP or GIF (animated GIFs play) |
| **Wallpaper…** | A picture or video behind everything |

## Editing

- **Select** a widget by clicking it, or in the **Layers** list. **Ctrl-click** the canvas to add or remove widgets from the selection. The widget whose properties are showing is the primary one.
- **Move** the selection by dragging. A widget snaps to the centre of the canvas and to the edges of the others when it gets close, and a line shows the match. Hold **Shift** while dragging to move freely. **Resize** the primary widget from the corner; hold **Shift** to keep its shape. Arrow keys nudge every selected widget.
- With two or more widgets selected, **Left, Centre, Right, Top, Middle and Bottom** line them up with the box around the selection. **Space across** and **Space down** spread three or more evenly, leaving the outermost two where they are. **Same width, Same height and Same size** give the others the primary widget's size.
- **Delete** removes it; **Ctrl+D** duplicates it.
- **Forward** and **Back** change which widgets are drawn on top.
- **Undo** and **Redo** go back and forth through every change, up to 100 steps.

The panel on the right shows the few choices that change how the selected widget looks: its words or sensor, the size of the type or the thickness of the ring, the colour, and alignment. **More** opens the rest, including position, size, opacity, rotation, show-only-when, and the name. Position is usually easier on the canvas.

**Screen** opens the name, background, gradient, wallpaper and default colours. **Match panel** sets the canvas to the connected panel's size.

Colours are written `#rrggbb`, or `#rrggbbaa` with transparency. The swatch next to a colour offers the screen's background, text and accent colours.

## Sensors

Pick a sensor from the sensor list and it is applied at once. On a bar, gauge or graph it becomes the value. In text it replaces the first `{@…}` in the sentence and leaves the rest of the words as they are; **Use** does the same thing. The names are the portable ones described in [Sensors](sensors.md), such as `@gpu.temp`, so your screen works on any PC. Text can mix several values and formats; see [Screen file format](theme-format.md#bindings-and-formats).

## Saving and showing

**Save** writes the screen to your screens folder. **Show on panel** saves it and puts it on the panel. The title shows **•** while there are unsaved changes.
