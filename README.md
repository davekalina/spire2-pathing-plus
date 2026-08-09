# Pathing Plus

An informational Slay the Spire 2 mod for the map screen: see every route before you
commit to one.

Click a node ahead of you to pin it, and the mod draws every route that reaches your
pins as hand-drawn trails in the game's own colours. Double-click to pin a whole node
type at once — all the elites, all the fires. A replacement legend counts what each
route holds, so "which way gets me three elites and two fires" is a glance rather
than a squint.

It changes nothing about the game. No node moves, no reward changes, and a `?` stays
a `?`; the mod only reads the map and draws over it, and its manifest declares
`affects_gameplay: false`.

## What it does

- **Pin nodes to compare routes.** Pins are candidates, not constraints: routes are
  ranked by how many pins they reach, so a route through all of them wins, and when
  your pins disagree you still see the best options instead of nothing.
- **A legend that counts.** Node types down the side, one column per route, in the
  map's own parchment. Hover a type to light up every node of that type; hover a
  route to preview it; select it to lock it in. Locked routes survive travelling
  along them, and pins survive restarting the game.
- **Three map views.** A Zoom button cycles the game's normal view, the whole act on
  one screen, and the whole act rotated on its side — start at the left, boss at the
  right.
- **Controller support throughout.** Right Trigger zooms and clicking the left stick
  cycles the drawing tools; in the zoomed views the d-pad walks the map node by node with a
  cursor ring, and select pins whatever it is on. The left stick drives the drawing
  quill — which the game asks for but Steam Input can quietly withhold — and the
  quill slows down as you zoom out so it tracks the map rather than the screen.
- **Settings** behind a gear: marker size, live sliders for the trail's look and the
  wide view's framing, and a **Path Mode** with three settings — *Auto* scores routes
  to the boss, *Manual* connects only the nodes you pin, and *Drawing* takes over the
  map's quill so a stroke over the map snaps to the nodes it passes instead of
  leaving a freehand line.

## Install

Subscribe on the Steam Workshop, then enable **Pathing Plus** in
Settings → Mod Settings.

## Feedback

Issues and feature requests are welcome in
[the issue tracker](https://github.com/davekalina/spire2-pathing-plus/issues).

Building the mod from source is covered in [DEVELOPING.md](DEVELOPING.md).
