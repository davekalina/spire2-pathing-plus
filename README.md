# Pathing Plus

An informational Slay the Spire 2 mod for the map screen: see every route before you
commit to one.

Draw roughly where you want to go and the mod snaps your stroke to the nodes it
passes, working out the real routes that fit and drawing them as hand-drawn trails in
the game's own colours. A replacement legend counts what each route holds, so "which
way gets me three elites and two fires" is a glance rather than a squint.

It changes nothing about the game. No node moves, no reward changes, and a `?` stays
a `?`; the mod only reads the map and draws over it, and its manifest declares
`affects_gameplay: false`.

## What it does

- **Draw your plan.** The quill snaps to the nodes your stroke passes and joins them
  into whole routes. The eraser takes out a single step — rub the line between two
  nodes and only that link goes, both nodes and their other links untouched. Clear
  drawings starts over. Nothing is ever drawn that you did not point at.
- **Or click.** Selecting two nodes the map joins draws the step between them.
  Double-click a node to select every node of that type at once — all the elites, all
  the fires. Draw to one step short of the end of the act and the last step is drawn
  for you.
- **Auto-Path.** Pick Max Elites, Fires, Shops, Events or Combats and the map is
  redrawn with the routes that collect the most of it.
- **A legend that counts.** Node types down the side, one column per route, in the
  map's own parchment. Hover a type to light up every node of that type; hover a
  route to preview it; select it to lock it in. Only finished routes are tabulated,
  so a half-drawn plan does not clutter the comparison. Locked routes survive
  travelling along them, and your plan survives restarting the game.
- **Three map views.** A button cycles the game's normal view, the whole act on one
  screen, and the whole act rotated on its side — start at the left, boss at the
  right. It says what pressing it will do: Zoom Out, then Rotate, then Zoom In.
- **Controller support throughout.** Right Trigger zooms and clicking the left stick
  swaps quill for eraser; the left stick drives the quill, slowing as you zoom out so
  it tracks the map rather than the screen. In the zoomed views the d-pad walks the
  map node by node with a cursor ring. The toolbar and settings are reachable too —
  press up from the top of the legend. In the normal view the right stick scrolls the
  map, as it did in the first game.
- **Settings** behind a gear: **Override Drawing Controls** (on by default) is what
  gives the quill its new meaning, and turning it off hands the map's own freehand
  drawing back. Live sliders for the trail's look and the wide view's framing sit
  under *Advanced*.

## A note on Steam Input

The right stick scrolling the map needs the pad's own axes to reach the game. With
Steam Input enabled and the right stick unbound, Steam sends nothing and the game
never sees it. Either disable Steam Input for Slay the Spire 2, or bind the right
stick to *Joystick Move*. Everything else works either way.

## Install

Subscribe on the Steam Workshop, then enable **Pathing Plus** in
Settings → Mod Settings.

## Feedback

Issues and feature requests are welcome in
[the issue tracker](https://github.com/davekalina/spire2-pathing-plus/issues).

Building the mod from source is covered in [DEVELOPING.md](DEVELOPING.md).
