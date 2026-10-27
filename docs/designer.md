# Designer

The **Designer** builds screens on a live canvas that shows real sensor values as you work.

![The Designer](images/designer.png)

## Starting

- Pick an existing screen from the list at the top to change it. Screens that come with Panelith are never changed: saving makes your own copy, marked **(custom)**.
- Or choose **New blank screen** for an empty canvas the size of your panel.

Panelith remembers which screen was open the next time you start it.

## Adding widgets

The **Add** bar puts a widget in the middle of the canvas:

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
- **Move** the selection by dragging. **Resize** the primary widget from the corner; hold **Shift** to keep its shape. Arrow keys nudge every selected widget.
- **Left, Centre, Right, Top, Middle and Bottom** line the selection up. One widget lines up with the canvas; several line up with the box around all of them.
- **Space across** and **Space down** spread three or more widgets evenly, leaving the outermost two where they are.
- **Same width, Same height and Same size** give the other selected widgets the primary widget's size.
- **Delete** removes it; **Ctrl+D** duplicates it.
- **Forward** and **Back** change which widgets are drawn on top.
- **Undo** and **Redo** go back and forth through every change, up to 100 steps.

The panel on the right edits the selected widget: its position and size, colours, font, the sensor it shows, its range, and so on. With nothing selected it edits the **Screen**: name, canvas size (**Match panel** sets it to your panel's), background, gradient, wallpaper dimming, video speed, font and the default colours.

Colours are written `#rrggbb`, or `#rrggbbaa` with transparency.

## Sensors

Pick a sensor from the sensor list to bind it to the selected widget. The names are the portable ones described in [Sensors](sensors.md), such as `@gpu.temp`, so your screen works on any PC. Text can mix several values and formats; see [Screen file format](theme-format.md#bindings-and-formats).

## Saving and showing

**Save** writes the screen to your screens folder. **Show on panel** saves it and puts it on the panel. The title shows **•** while there are unsaved changes.
