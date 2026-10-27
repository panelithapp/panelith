# Screen file format

Every screen is a folder containing a `theme.json` and any pictures or clips it uses. The [Designer](designer.md) writes these files for you; this page is for people who want to write or change them by hand. The screens in [`themes/`](../themes) are complete examples.

Check a screen for mistakes with `panelith theme validate <folder>` (see [Command line](cli.md)).

## The document

```json
{
  "schema": 1,
  "id": "my-screen",
  "name": "My screen",
  "author": "You",
  "description": "Shown on the Screens page.",
  "canvas": { "width": 1920, "height": 480, "scaling": "fit", "background": "#000000" },
  "background": { "color": "#07090d", "gradientTo": "#10151d", "gradientAngle": 110 },
  "defaults": { "fontFamily": "Inter", "color": "#e8eef6", "accentColor": "#3ee0c5", "trackColor": "#1c2430" },
  "widgets": [ ]
}
```

| Field | Meaning |
| --- | --- |
| `schema` | Always `1` for now. |
| `id` | Folder-safe identifier, unique among your screens. |
| `name`, `author`, `description`, `version`, `license` | Shown on the Screens page. |
| `canvas` | The size the screen is designed at, and how it maps onto other panel sizes (below). |
| `background` | What is drawn behind the widgets (below). |
| `defaults` | Values widgets use when they set none: `fontFamily`, `color`, `accentColor`, `trackColor`, and `motion` (`false` turns off gliding values for the whole screen). |
| `widgets` | The widgets, drawn in order: later ones on top. |

Colours are `#rgb`, `#rrggbb` or `#rrggbbaa` (the last pair is opacity).

## One screen, many panel sizes

The `canvas` is a design size, not a device size. `scaling` says how it is mapped onto a panel of a different size:

| `scaling` | Effect |
| --- | --- |
| `fit` | Scale evenly until the canvas fits, with bars on two sides when the shapes differ. The default. |
| `fill` | Scale evenly until the canvas covers the panel, cropping the overflow. |
| `stretch` | Scale each axis to match exactly. Distorts circles and text. |
| `none` | Do not scale. Combine with `anchor` so the layout adapts by moving widgets instead. |

With `scaling: "none"`, each widget's `anchor` says which canvas edges it keeps its distance to. It is a number adding up: `1` left, `2` top, `4` right, `8` bottom. For example `3` pins top-left, `6` top-right, `7` stretches across the top, and `15` stretches both ways. The bundled `rig` screen is drawn at 1280×480 and uses anchors to fill a 1920×480 panel as well.

## Background

| Field | Meaning |
| --- | --- |
| `color` | Flat colour. |
| `gradientTo`, `gradientAngle` | Makes the background a gradient from `color`, at an angle in degrees clockwise from left-to-right. |
| `image`, `fit` | A picture, relative to the screen folder. `fit` is `contain`, `cover` (default), `stretch`, `none` or `tile`. |
| `video`, `videoSpeed` | A looping `.ptclip` clip made by Panelith, and its playback speed. |
| `scrim` | A colour, usually translucent black such as `#00000080`, laid over the picture or video so text stays readable. |

## Widgets

Every widget has:

| Field | Meaning |
| --- | --- |
| `type` | `text`, `bar`, `gauge`, `graph`, `image`, `video`, `shape` or `group`. |
| `id` | Optional name, shown in the Designer's layer list. |
| `x`, `y`, `width`, `height` | Its box, in canvas units. |
| `anchor` | See above. |
| `opacity` | 0 to 1. |
| `rotation` | Degrees clockwise. |
| `visibleWhen` | An expression; the widget is only drawn when it is true, such as `@gpu.temp > 80`. |
| `animate` | `false` to show each reading exactly instead of gliding to it. |

### text

`text` is the text to draw, with values in braces: `"CPU {@cpu.temp:0}°"`. Also: `fontFamily`, `fontSize`, `fontWeight` (100 to 900), `italic`, `color`, `align` (`left`, `center`, `right`), `verticalAlign` (`top`, `middle`, `bottom`, `baseline`), `letterSpacing`, `lineHeight`, `wrap`, `shrinkToFit` (on by default), `scroll` (slides a line that is wider than its box, and turns shrink-to-fit off for that line), `strokeColor` and `strokeWidth` for an outline, and `tabularNumbers` and `stabilizeWidth` (both on by default) so numbers do not jitter as digits change.

`colorStops` changes the colour with a value: `[{ "at": 0, "color": "#3ee0c5" }, { "at": 80, "color": "#ff5d73" }]`. The highest `at` not above the value wins. `colorBinding` sets which value; by default the first sensor in `text`.

### bar

`value` (an expression), `minimum` and `maximum` (default: the sensor's own range), `direction` (`leftToRight`, `rightToLeft`, `bottomToTop`, `topToBottom`), `color`, `trackColor`, `cornerRadius`, `segments` and `segmentGap` for a segmented bar, and `colorStops`.

### gauge

`value`, `minimum`, `maximum`, `startAngle` (degrees clockwise from twelve o'clock, default 135) and `sweepAngle` (default 270), `thickness`, `color`, `trackColor`, `roundCaps`, `ticks`, `colorStops`, and an optional `label`, which is a text widget drawn in the middle.

### graph

`series`: a list of `{ "value": "@cpu.load", "color": "#3ee0c5", "thickness": 2, "fill": true, "fillOpacity": 0.25, "smooth": true }`. Also `windowSeconds` (how much history the width covers, default 60), `minimum` and `maximum` (omit to scale automatically), `gridColor`, `gridDivisions` and `backgroundColor`.

### image

`source` (relative to the screen folder; PNG, JPEG, BMP, WebP or GIF), `fit`, `tint`, and `animationSpeed` for animated GIFs (`0` freezes the first frame). The special source `@media.art` draws the album art of whatever is playing, and draws nothing when there is no picture.

### video

`source` (a `.ptclip` in the screen folder), `fit` (default `cover`) and `speed`.

### shape

`shape` (`rectangle`, `ellipse` or `line`), `fill`, `stroke`, `strokeWidth`, `cornerRadius`, and `gradientTo` with `gradientAngle` for a gradient fill.

### group

`items`: child widgets positioned inside the group's box, so they move, fade and hide together. `clip` cuts children off at the box.

## Bindings and formats

Inside `text` braces, and in `value`, `visibleWhen` and `colorBinding`, you can write:

- a theme value such as `@cpu.temp` (see [Sensors](sensors.md#theme-values)), or a raw sensor id in brackets such as `[cpu/0/temperature/package]`;
- numbers, `+ - * / %`, comparisons `< <= > >= == !=`, `&&` (or `and`), `||` (or `or`), `!` (or `not`), `condition ? a : b`, and parentheses;
- functions: `min`, `max`, `sum`, `avg`, `abs`, `sqrt`, `sign`, `floor`, `ceil`, `round`, `pow`, `clamp`, `lerp`, `map`, `if(condition, then, else)`, `coalesce`, `has`, `isnum`, `istext`, `upper`, `lower`, `trim`, `len`, `concat`, and `avgover`, `minover`, `maxover` for the average, lowest or highest value over a number of seconds, such as `maxover(@cpu.temp, 300)`.

After a colon, a format says how to write the value:

| Format | Example | Writes |
| --- | --- | --- |
| `0`, `0.0`, `0.00` | `{@cpu.temp:0}` | `64` |
| `int` | `{@cpu.fan:int}` | `1200` |
| `percent` | `{@cpu.load:percent}` | `37%` |
| `bytes`, `bytes0` … `bytes2` | `{@mem.used:bytes}` | `10.3 GiB` |
| `rate`, `rate0`, `rate1` | `{@net.down:rate}` | `4.2 MiB/s` |
| `clock`, `clock0` … `clock2` | `{@cpu.clock:clock1}` | `5.1 GHz` |
| `duration` | `{@sys.uptime:duration}` | `1:04:22`, or `2d 03:10:00` |
| `onoff`, `yesno`, `bool` | | `ON`, `Yes`, `true` |
| `hex` | | `0xFF` |
| `auto` | | Picks one from the kind of sensor |

A missing reading is written as `--`.
