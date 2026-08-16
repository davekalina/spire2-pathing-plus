# Pathing Plus — repository instructions

This repository is one Slay the Spire 2 mod. These instructions apply throughout it.
A more specific `AGENTS.md` may add to or override them for its directory.

`docs/sts2-modding.md` describes how the game's mod loader, manifest, and Steam Workshop
pipeline work. Those are platform facts. This file is how I want the work done.

## This mod

| | |
| --- | --- |
| Mod id | `PathingPlus` |
| Display name | Pathing Plus |
| Installs to | `<game>/mods/PathingPlus/` |
| Manifest | `PathingPlus.json` |
| Version is also printed in | `PathingPlusCode/MainFile.cs` (`Version`, shown in the in-game byline under the settings gear) |
| Gameplay | Informational only; `affects_gameplay` is `false` |
| Dependencies | None yet |

The mod adds route planning to the map screen. Clicking a non-travelable map node pins
it as a waypoint — double-clicking pins every pinnable node of that kind at once, and
toggles off when the rest of the kind is already pinned (the double-click's own first
click flips the clicked node, so the rule judges by the others). The mod enumerates
every route from the current position through all pins and draws them as runs of the
game's `map_dot` texture (native spacing, jitter, flips, and rotation noise, seeded
per route; girth 1.6x native so the trails read heavier than the map's own dashes) in
its own overlay layer inside `TheMap` (above the game's dotted connections, below the
node icons). Colors come from `StsColors`; the >threshold union view is darkBlue at
0.65 alpha — it is the entire display at act start, so it must not be faint. With five or fewer routes left, a legend panel above the native Share
button shows them as a table — one row per route, named by a letter in the route's
line color ("A.", "B.", …), sorted most elites first then most fires, count columns
headed by map icons in the fixed order
elites / fires / combats / shops / chests / events, zeros dimmed — plus a vertical
icon-column tooltip (boss end at the top) on hover/focus, darkening that route to
the traveled-ink color on the map while the rest fade. Pinned nodes are stamped with
the game's own map_circle art tinted rust — always `map_circle_4`, the completed
frame: the earlier four are the drawing animation and look torn as stills. The
controller cursor is the same art in gold, shown only while
`NControllerManager.IsUsingDirectionalNavigation` — and taken down by the mouse's own
first event, since nothing announces the switch away from a controller: the focus
events that place the ring simply stop arriving, leaving it stranded on the map.

Overlay stacking is tree order only — **never `ZIndex`**: a nonzero ZIndex is
canvas-global and draws over screens the game layers above the map (a z-10 route
highlight once cut a black line across the deck screen). The overlay keeps dot, pin,
and cursor sub-layers, and highlight prominence is a `MoveChild` within the dot
sub-layer.

Two lifecycle rules the hard way: the map screen's root stays in the tree when the
map closes (the game only hides its own contents), so every panel parented to the
screen root hides itself on `Closed` and shows on open — or it lingers over combat
and the settings menu. And a locked route is matched by suffix, not equality:
advancing a floor along it shortens every recomputed route by its head, so the tail
IS the same plan; deviation clears it naturally because the new position is no
longer on the stored route.

`MapToolbar` is the mod's corner of the screen: one tray holding the help badge, the
settings gear, the view button, and the byline, with the slot geometry as constants
the other three read. Its face is the Compendium's own card panel
(`common_ui/submenu_panel_short.png`, portrait art) built at swapped dimensions and
turned 90° — rotation about the top-left corner sends local (x, y) to (−y, x), so the
node starts at the tray's right edge for the turned rectangle to land on the tray.
The view button wears the pause menu's face (`reward_screen/reward_item_button.png`
plus the `hsv` shader at s 0.8 / v 0.9) and its lettering, and **names the next
action rather than the current state**: Zoom Out → Rotate → Zoom In. The byline is
pale lettering over a dark outline and a light drop shadow rather than dark ink on the
parchment — the panel's grain eats dark text at that size. It carries the **name and
version only**; the author is said once, properly, in the help panel.

The settings panel hangs directly beneath the toolbar on the same right edge, wearing
the same card art upright, and any click outside it dismisses it (a full-rect catcher
added before the panel, alive only while it is open). Its height follows
`_rows.GetCombinedMinimumSize()`, so folding a section away takes the parchment with
it. Path Mode and Path Markers are pull-downs built here (`AddDropdown<T>`, any enum)
rather than the native `settings_dropdown` scene, whose root is scriptless — its
behaviour lives in the settings screen, so it cannot be instantiated as a control.
Everything but those two lives under a collapsible **Advanced** heading.

`HelpTip` is the **?** badge left of the gear: the map's own hand-inked circle
(`map_circle_4`) with a "?" over it, showing a parchment panel of instructions on
hover, pinned open by a click. It exists because the mod repurposes controls the game
already taught — the quill, the eraser, Clear Drawings — so nothing on screen would
otherwise say they now mean something different. **Its prose lives in
`text/help.txt`, not in the C#** — player-facing text is David's to edit, and should
never need a code change or a JSON escape to reword. It is an `EmbeddedResource`, so
there is no loose file to package and no way for the two to disagree; `#` lines are
comments and the lines within a paragraph are joined, so the file can be hard-wrapped
for editing without every wrap becoming a line break on screen. The title above it
(name, version, author) stays generated from the manifest — a version typed into a
text file goes stale. The same rule covers `text/description.txt` (the in-game mod
list; a build target copies it into `PathingPlus.json`) and
`workshop/workshop-description.txt` (the Workshop page; `package-workshop.ps1` copies
it into `workshop.json`). Both syncs are one-way and never fail the build — the `.txt`
wins, so never hand-edit those JSON fields.

Two things about that panel are load-bearing. Its text is laid out **absolutely, not
in a container**: an autowrapping `Label` reports a near-zero minimum width, so inside
a `VBoxContainer` it collapses to one word per line. And body text gets a **drop
shadow, never an outline** — `outline_size` traces every glyph on all sides, which at
17px closes the counters and thickens the strokes until a paragraph reads as a grey
mass. The card art is modulated to ~40% brightness underneath, because pale lettering
only separates once the parchment stops competing for the light end of the range.

`AutoPathMenu` is the pull-down on the toolbar's second row: pick Max Elites / Fires /
Shops / Events / Combats and every complete route is scored by that legend row, the
best up to five replacing the plan outright. It wears the pause-menu button face
because a bare label is nearly invisible when focused — there is nothing to light up
but the text. Treasure is not offered: an act carries one, so maximising it is not a
choice. Selecting every node of five routes also selects pairs those routes never step
between, and the edge model would draw them as extra routes nobody asked for, so
`ApplyAutoPath` **cuts every step no chosen route uses**. That is what makes "the best
five" mean five.

**The toolbar is reachable with a gamepad.** `WireToolbarFocus` chains help ↔ gear ↔
zoom across the button row, down to Auto-Path, and on into the legend, with
`RouteLegendPanel.SetTopNeighbor` closing the loop upward. Two rules keep it working.
Focus neighbour paths must be computed **from the control that carries the property**,
never from a parent — a path measured from the legend panel resolves one level short
and Godot rejects it outright (`Neighbor focus node path is invalid`). And every
control that acts on `select` must call `AcceptEvent()`, or the same press travels on
to the map and moves the player a node; for the same reason `NodeNavigator.SetActive`
takes `takeFocus: false` while the toolbar holds focus.

**The right stick scrolls the map** (`RightStickScrollPatch`), the arrangement the
first game used: left stick and d-pad for selection, right for the view. It nudges
`_targetDragPos` in a prefix on `NMapScreen._Process`, so the screen's own easing and
its rubber-band clamp back into [-600, 1800] still apply; writing the container
position would fight both. Suspended while zoomed out, where the whole act is on
screen already.

**Beware `NMapDrawingInput.Create`.** It picks its implementation from
`IsUsingDirectionalNavigation` **at the moment of creation** and never revisits it, so
a quill picked up with the mouse stays a `NMouseModeMapDrawingInput` — one that
follows the pointer and never asks the left stick anything, which looks exactly like a
broken stick. `SyncToolToInput` rebuilds the tool when the device changes, keeping
mode and cursor position.

**Settings** (`PathingOptions` + `OptionsPanel`, a gear in the toolbar)
persist to `PathingPlus.settings.json` in the game's user data dir and raise
`Changed`, which redraws the open map. **Override Drawing Controls** (on by default) is the only behavioural setting left; the three path modes are gone, since drawing is simply how the mod works. With it on, the mod prefixes
`NMapDrawings.UpdateCurrentLinePositionLocal`, the single funnel every freehand point
passes through for both mouse and controller, suppresses the native line, and pins
the nearest node within `SnapRadius` (add-only, so a stroke doubling back cannot undo
itself). **The eraser works on steps, not nodes**: over the middle of a run it puts
that one (from, to) into `_cut`, and over a node (judged by the node's own rect) it
deselects the node and forgets every cut touching it. That is the whole reason the
plan is kept as edges — rubbing out one link between two nodes has to leave both
nodes, and their other links, exactly as they were. Drawing from one node onto the
next restores a cut step between *that pair only*; `_lastDrawn` tracks it and is
cleared when the stroke ends (`MapDrawingStopPatch`), or a later stroke starting
elsewhere would restore a link nobody drew over.

**A cut lives only while both its ends are selected.** Deselecting either end forgets
it, and selecting a node clears every cut at that node. A cut outliving its context is
invisible state of exactly the kind the old node blocks were — two adjacent nodes
selected, no line between them, nothing on screen to say why — and it is the one way
this model can still surprise. Keep the invariant.

**A plan one floor short of the end is finished for the player.** `WithLastStep` adds
an act end node once some selected node is a direct predecessor of it. They stay out
of `_pins`, so they are never persisted and the eraser has nothing of its own to lift.

Suppressing that funnel is **not enough on its own** to keep native ink off the map.
`BeginLine` creates the `Line2D` and seeds it with two points half a pixel apart,
which round-capped in the character's drawing colour renders as a dot — so with every
later point dropped, that seed became the only thing drawn: one blob per stroke.

**The line must still be created.** `IsDrawing` is defined as
`currentlyDrawingLine != null`, and every input driver — `NMouseModeMapDrawingInput`,
`NMouseHeldMapDrawingInput`, `NControllerMapDrawingInput` — forwards motion only while
`IsLocalDrawing()`. Refusing to begin the line does not suppress a stroke, it
suppresses every *point* of it, and Drawing mode stops working altogether; that was
tried and it broke the mode outright. So the line is begun as usual and hidden:
`MapDrawingBeginPatch` raises a flag around `BeginLineLocal` and
`MapDrawingCreateLinePatch` sets `Visible = false` on the `Line2D` that comes back.
The flag is what scopes it to the local player — `CreateLineForPlayer` also runs for
lines arriving from other players, and theirs should still show.

Two consequences follow. The `UpdateCurrentLinePositionLocal` prefix **always** skips
the original in Drawing mode, so the hidden line never gains a point. And the eraser
has no visible native ink to remove while the mode is on, so it only ever lifts pins
(Clear drawings still takes everything).

Use the **point the patch is handed** — it is the controller's cursor as much as the
mouse — but converting it back is a trap worth knowing. The game produces it with
`Transform2D.Inverse()`, which Godot only defines for an **orthonormal** basis:
rotation is fine, scale is not. Vanilla never scales the map so their conversion
holds, but the zoom does, and their point then means nothing literal. Undo *their*
matrix with `AffineInverse()` to recover the true global point, then map-local via
`TheMap.GetGlobalTransform().AffineInverse()`. `Control.GetLocalMousePosition()` is
no use either: it is a plain translation that ignores rotation and scale.

In both, `PathSolver.ConnectSelected` replaces route enumeration entirely, and it does
**no pathfinding at all**: the plan is one segment per map step whose two ends are
both selected, less anything in `_cut`. The player's own position counts as selected,
so the first step needs no selecting. Selecting two nodes the map does not join draws
nothing, and a gap in the selection stays a gap.

Its predecessor, `ConnectWaypoints`, bridged selected nodes with *shortest* paths and
fell back to ever-earlier floors when a link was cut. That is the trap to remember:
**in a layered map every route between two rows is the same length**, one edge per
row, so "shortest" discriminates nothing and asking for the shortest paths between two
floors returns *every* path between them. A fallback from a distant floor therefore
drew a whole fan of line at once, and erasing one step could summon a sweep of route
across the far side of the map. `ConnectWaypoints` and `ShortestPaths` are gone; do
not reintroduce shortest-path bridging here.

`AssembleRoutes` then stitches those steps back into whole
routes, because the legend counts what a **path** holds, not what each link holds —
a plan that forks yields one route per branch. **Every assembled route is drawn**; the
legend's five are ranked by elites, then fires, then shops, and only those get a
colour and a column, the rest going down first as a faint backdrop. Cutting the extras
instead would answer the player's own drawing with silence.

**Only complete routes are tabulated.** A route earns a colour and a column only if it
ends where the act ends — the terminal nodes of the full enumerated routes, the boss
already trimmed off. A half-drawn path is a plan in progress, and putting it in the
table invites a comparison that means nothing: of course the short one has fewer
elites. Incomplete routes still draw, as backdrop, so a path does not vanish while it
is being made — which is why `UpdateOverlay` clears only when the backdrop is empty
too, not merely when nothing is shown.

Two earlier rules were wrong here and must not come back. Judging by "first waypoint
reached" admits any detour that dodges every intermediate pin — up an edge column
and back — because such a path is the *only* member of its pair and so survives even
a shortest-per-pair filter; that is what made the map's outer columns light up.
Requiring one route through every pin drew nothing at all as soon as two pins sat on
rival branches. Two earlier attempts were wrong and should not be
reinstated: best-match scoring made a second pin *widen* the picture by re-admitting
routes that skipped the first, and requiring every pin on one route drew **nothing**
the moment two pins sat on different branches. Pinnability is still computed from the
full routes, so every node ahead stays reachable. **Path Markers** picks the pin ring:
**Path markers have no setting** — they are small, and **transient**: they fade in when
the plan changes, hold three seconds, then fade out, and hovering a pinned node brings
them back (asking where the plan goes should not require changing it). Once transient,
none of *off* / *small* / *regular* was worth choosing between, so the option went.
A ring
that stays put reads as "somewhere I have been", which is the wrong thing to see on a
node ahead at the moment of choosing where to move; as feedback while drawing it is
exactly right. `ShowPins` is therefore told *whether the plan changed*: a redraw for
travelling, zooming, or reopening the map must not restart the timer, or the markers
come back at precisely the wrong moment. Sliders expose the dash
geometry (`DashWidth`, `DashLength`, `DashLengthVariance`, `DashSpacing`,
`RouteSeparation`) and the landscape framing (`LandscapeFit`, `LandscapeZoom`,
`LandscapeShiftX/Y`, re-fitted live through `MapZoom.Reapply`) for live tuning. The
saved file's fields are all nullable and applied only when present, so an older file
never zeroes a newly added option; **Reset to defaults** exists because a saved file
otherwise masks any default that moves in a later version.

Pins and the locked route persist across map opens and game restarts via
`PinStore` — one JSON file (`PathingPlus.pins.json` in the game's user data dir)
keyed by a SHA-256 of the map graph, so state only ever restores onto the exact map
it was made on. The **Zoom** button (upper right) scales `TheMap` around a pivot on
the screen's horizontal centre line so the native scroll code (which only writes
X = 0) cannot shove the view sideways; zooming back in snaps to the current row
using the native formula. While zoomed, all native scrolling is suspended — the
controller handler and the screen's own `_GuiInput` (drag, wheel, and with it quill
drawing starts) — because the whole map is on screen and movement is pure noise;
this is also what lets plan-mode controller navigation work without the view
fighting it.

**Pins use best-match scoring** (`PathSolver.MatchByPins`): routes are ranked by how
many pins they visit. The best tier always shows in full — ALL when a route hits
every pin, best-achievable coverage when pins conflict, never an empty result — and
lower tiers (at most two below the best, never zero-hit routes) are appended one
whole tier at a time while they fit the legend. Legend rows carry the score
("Route 1 — 6/7"). Same-floor pins are meaningful and allowed. The native Clear
drawings button clears the pins too.

**Start in Wide View** (`PathingOptions.StartWide`, off by default) opens the map
already rotated. `MapZoom.ShowInitialView` must run **after** the first `Refresh` and
never in place of `Reset`: `Apply` drops back to the normal view when `_nodeCenters()`
is empty, and it is empty until a refresh has read the point dictionary. It is
deferred a frame on top of that, because the node rects it frames against are only
final after a layout pass. It snaps rather than tweens — animating on open shows the
normal map and then flips it, which reads as a glitch. `Toggled` therefore carries an
**instant** flag: the node icons counter-rotate in step with the map, and snapping the
map while they tween leaves every icon visibly spinning into place on a map that has
already arrived. Anything else that follows the view must honour that flag too.

**A snap must kill the tween it is overriding.** Closing the map resets the view,
which starts a 0.55s tween pulling every icon back to its base rotation; the next open
snapped them a frame later and the surviving tween simply carried on writing
`rotation_degrees` and won. Symptom: correct on the first open, a quarter-turn out on
every one after. `_iconTweens` keeps the handle per icon so it can be killed first —
`MapZoom.AnimateTo` already did the same for the map's own tween, which is why the map
looked right while its icons did not.

**Map nodes only pulse and hover while no drawing tool is out.** That is
`NMapPoint.IsInputAllowed`, and it gates the "you can go here" pulse in `_Process`,
the controller reticle, and the history hover tip — every caller is a visual, none is
on the click path. Vanilla only holds a tool while you scribble, so the rule reads as
"don't fight the pen"; this mod holds one permanently, which silently cost every node
its invitation to be clicked. `MapPointHoverPatch` turns that false back to true, and
only for the reason the mod created.

**Travel works in every view.** `BeforeMapPointSelected` lets a node through when
`IsEnabled` — the game offering it as a move — and only plans with the rest. It used to
swallow travel outright while zoomed, on the reasoning that the zoomed views were a
look at the act rather than somewhere to play from; once **Start in Wide View** could
make one of them the default, that turned into "zoom in first" on every single turn.
Hit testing goes through `Polyline`, so what can be hovered is exactly what is
drawn. Leaving the first leg *undrawn* while travel is live was tried and reverted:
it read as the plan having a hole in it rather than as an invitation to click.

`Reset` stays **tweened** at all four callers. Snapping the map upright on close was
tried and is worse than watching it turn — more jarring, not less. Only the initial
view snaps. `Reset` still returns to Normal
on close and dispose, or the game keeps a scaled, rotated map after the mod is gone.

**The Zoom button cycles three views** (Right Trigger or the button; every map open
starts Normal): Normal → Zoomed (whole act on screen) → Rotated (whole act on its
side, start left / boss right, a quarter-turn of `TheMap`). Transitions animate:
scale and rotation by tween, position by writing `_targetDragPos` and letting the
screen's own lerp chase it; every transform pivots on the map content's centre so
the combined tween cannot lurch. Both zoomed states are the controller mode: all
native scrolling and screen mouse handling is frozen, every drawn node — the whole
map, not just route members — becomes focusable in a four-way grid wired from the
drawn layout (`NodeNavigator`; in the Rotated view the wiring swaps axes so right is
bossward and up/down walk a floor), a gold ink ring marks the focused node, and the
cursor survives pin presses and the game's own focus grabs via a deferred re-grab.
All focus state is snapshotted before wiring and restored on exit, map change,
screen close, and dispose. Zoomed is planning, not moving:
`OnMapPointSelectedLocally` is suppressed while zoomed and select becomes a pin
toggle, travelable nodes included; travel requires cycling back to Normal.

**The replacement Legend** (`RouteLegendPanel`) sits bottom right — the space the
mod's retired routes table held — on the native `map_legend` parchment, its width
fitted to its contents (right edge fixed, left edge hugs). Transposed: type rows in
the native legend's order (unknown / shop / treasure / rest / monster / elite), one
column per route headed by its colored letter; with zero routes it shows the type
names instead, reading like the native legend. Route selection: up to
`BestPickPool` (10) candidates survive `MatchByPins`, and the best
`LegendThreshold` (5) are shown — ranked by pin hits first (a full match must never
lose its slot to a near-miss), then elites + fires, then "?" count; that ranking is
also the display order, and the remainder go down as backdrop. **The best five are
always named**, however wide the field: a large match set used to fall back to an
unlabelled union with an empty table, which answered "which of these is better?" with
silence at exactly the moment the question was being asked. The union view is now only
for **no pins at all**, where no route is a better answer than any other and the shape
of the whole act is the honest display. Type icon hover/focus fires the game's own
`HighlightPointType` broadcast. **Hover deepens, selection inks.** A hovered route
keeps its own colour, saturated (`PathOverlay.Emphasis.Hover`); only a locked one goes
to the game's traveled-path ink. Turning a route black on hover reads as a commitment
not yet made, and throws away the colour tying the line to its column at the very
moment the two are being matched. A hovered route with **no** column gets a headerless
one appended to the legend (`SetPreview`), drawn in `TraceColor` — deliberately
outside the five route colours, and display-only, since a column that vanishes on the
next mouse move must not be lockable.

Hovering works **both ways**: a column lights its route
on the map, and a drawn route under the mouse lights its column — which is why the
pointer handler stands down while the pointer is over the legend (`Covers`), or it
would clear the column the legend had just lit. Map-side hit testing must use
`PathOverlay.RouteShift`, the same sideways offset the routes are **drawn** with:
against the shared centreline every route lies on top of every other and picking
between two neighbours is a coin toss. Its radius is `HoverRadius` (14), not the
quill's `SnapRadius` (55) — that one is scaled to a node and swallows whole bundles of
lines. Backdrop routes have no column but are hoverable too: they are drawn as merged
edges, so there is nothing per-route to light, and `ShowTrace` picks the one route out
over the top instead. Only the **run between two nodes** answers the pointer: hover is
off while a drawing tool is in hand (the mouse is drawing, not pointing, and lighting
whatever the stroke crosses fights the gesture) and off over a node (that is something
about to be clicked, and a route lighting under it makes the map twitch on the way to
every pin). The node radius comes from the node's own rect, not a constant, so it
tracks whatever the game draws. Column hover darkens the column (black 0.15),
locking gives it a `StyleBoxFlat` — a 4px border in the route's colour plus a fill of
the same colour at 0.42 alpha, both **deepened toward black** first. A plain wash in
the route's own colour is what it used to be, and it was invisible: half the palette
is pale, and pale over pale parchment reads as nothing. The border, not the fill, is
what makes a locked column unmistakable. The native legend is
`Visible = false` for the view's lifetime (restored on dispose) and its hotkey
handler `OnLegendHotkeyPressed` is prefix-rerouted into this panel with the same
toggle semantics. Hovering or focusing a type icon also bands that whole row inside
the panel — the map-side pulse alone left controller focus invisible in the legend
itself.

In the Rotated view every node icon counter-spins a quarter turn in step with the
map tween so the art stays upright; base rotations (each node carries a small random
tilt) are captured on first sight — always outside the rotated state — and restored
on the way back. Toggling the view also kills any node hover tip left open, one
deferred frame later so the tip conjured by the mode's own focus grab dies too.

The route enumeration, any-pin filtering, and boss-tail dedupe live in
`PathingPlusCode/Pathing/` — pure logic, linked into `PathingPlus.Tests`, and the
part that must never regress silently.

Game coupling that a game update can move (verify after every update):

- Harmony targets: `NMapScreen.Open` / `SetMap` / `_Input` /
  `OnMapPointSelectedLocally` / `RecalculateTravelability` /
  `ProcessControllerEvent` / `OnLegendHotkeyPressed` (the last three private,
  patched by string name), `NClickableControl._GuiInput`, and
  `NMapDrawings.ClearDrawnLinesLocal`.
- Reflection: `NMapScreen._mapPointDictionary`, `NMapScreen._targetDragPos`, and
  `NMapScreen._distY` (zoom-out restore).
- Scroll assumptions: the `_targetDragPos` clamp range [-600, 1800] and the
  "-600 + row * _distY" current-row formula.
- Input actions: `Controller.rightTrigger` for Zoom, `Controller.lStickPress` **and**
  `MegaInput.peek` (Steam Input commonly binds L3 to peek, so the game's own stick
  click never fires — the same class of gap as the analog stick) to cycle
  the drawing tools (nothing → quill → eraser → quill, by invoking the screen's own
  private `OnMapDrawingButtonPressed` / `OnMapErasingButtonPressed`, which already
  handle stopping and swapping), `MegaInput.confirm` for the legend, and the
  `raw_l_stick_*` axes for the quill. A trigger is an axis, so it needs a held latch
  or one pull fires repeatedly.
- **Build the tool switch by hand, and set the mode last.** `SwitchTool` stops every
  live `NMapDrawingInput`, clears the screen's `_drawingInput`, creates and adds the
  new tool itself, then calls `SetDrawingModeLocal` **after** the node is in the tree.
  The screen's own `OnMapDrawingButtonPressed` / `OnMapErasingButtonPressed` are not
  used: they stop the old tool and start the new one in one call with the mode set in
  the middle, so any teardown landing late resets the mode to `None` and the new tool
  throws *"Player 1 is not currently in a drawing mode"* on its first stroke — a quill
  that draws nothing and an eraser that erases nothing. Do not add a patch that stops
  "leftover" tools on `_EnterTree`; that was tried and it clobbered the mode of the
  tool that had just arrived, breaking the game's own buttons too.
- **Do it deferred.** The switch frees and adds nodes, which cannot happen during
  input processing: a node added there is created but never entered.
- The quill's cursor moves in **screen** space at a fixed 700 px/s, which crosses
  proportionally more map as the zoom shrinks it. `QuillSpeedPatch` measures the step
  the game just took and rescales it by `TheMap.Scale`, covering every input source
  without replicating the movement logic.
- `NControllerManager.GetLeftAnalogStickDirection` is postfixed so the **left stick
  drives the drawing quill**. The quill already asks for that reading and only falls
  back to the d-pad when it is near zero; the gap is that under Steam Input the
  strategy returns a Steam *analog action*, so a controller config that binds the
  left stick elsewhere reports zero forever while the d-pad (digital actions) still
  works. The postfix fills a zero reading from Godot's raw axes, and only while the
  map is open with a quill or eraser in hand — every other screen's input is
  untouched.
- Controller glyphs go through `HotkeyGlyph`, never `NHotkeyIcon.UpdateInput`:
  `NInputManager.GetHotkeyIcon` only resolves **remappable actions**, so a raw button
  (the right trigger) returns null and the native icon keeps its placeholder art,
  silently showing the south face button. `NControllerManager.GetHotkeyIcon` reads
  the controller config's glyph map and is the fallback that makes raw buttons
  render as themselves.
- Node paths: `TheMap`, `TheMap/Points`, `MapLegend/Header`, `MapLegend/LegendItems`.
- Resources: `images/atlases/ui_atlas.sprites/map/icons/map_*.tres`,
  `images/atlases/ui_atlas.sprites/map/map_legend.tres` (replacement legend bg),
  `images/atlases/compressed.sprites/map/map_dot.tres`,
  `images/atlases/compressed.sprites/map/map_circle_4.tres`,
  `images/packed/common_ui/submenu_panel_short.png` (the toolbar tray, the settings
  panel, and the help panel), `images/ui/reward_screen/reward_item_button.png` plus
  `hsv.gdshader` (the view button's face),
  `images/atlases/ui_atlas.sprites/top_bar/top_bar_settings.tres` (the gear),
  `images/ui/tiny_nine_patch.png`, and the `StsColors` palette.
- Scroll behavior: plan mode writes `_targetDragPos` directly and assumes the
  [-600, 1800] clamp range from `UpdateScrollPosition`.

### Surfaces to audit

- The map screen overlay: route dot runs (individual colors, union view above the
  legend threshold, ink highlight / fade states) and rust pin rings — which yield on
  directly-travelable nodes while `IsTravelEnabled`, so the game's own travel
  markers stay unambiguous; the pin itself keeps filtering.
- The routes panel: header count, hint line, up to five route rows, lock marker,
  hover/focus/select behavior, and its focus chain from the native map legend.
- The routes table: column header icons, per-row counts (zeros dimmed), fixed
  category order, row hover/focus/select, and the vertical icon-column tooltip
  (boss at top) with its position and clamping.
- Persistence: pins and locked route restored only onto their own map, pruned when
  stale, saved on every change; the Clear button empties the file's pin list too.
- The Zoom button: label states, its Right Trigger glyph (controller only), full-map
  framing at any act size, drag/wheel while zoomed, and the snap back to the
  current row.
- The settings gear and its panel: both pull-downs and every slider, the Advanced
  fold and the panel resizing with it, live redraw, persistence across restarts, and
  manual (Auto Path off) planning with zero, one, and several pins.
- The help badge: hover shows and unhover hides the panel, a click pins it open, and
  the text neither overruns the parchment nor wraps a word per line.
- Controller mode: the Right Trigger hotkey, all-node focus wiring and its
  restoration, the gold cursor ring, cursor retention across pin presses,
  select-to-pin versus select-to-travel, and the frozen native scroll/mouse
  handlers while zoomed — which must keep letting a right/middle **press** through
  in Drawing mode, since `_GuiInput` is where a stroke is created and freezing it
  wholesale kills drawing in both zoomed views.
- Drawing mode in all three views and with both tools: quill snapping, eraser
  lifting pins, and neither disturbing pan/zoom.
- Interactions with native map input: travel clicks on travelable nodes, drag-to-pan,
  quill drawing / erase modes (pins must not fire during them), and travel animation.
- Multiplayer map voting and the FTUE first-map flow (pins must stay inert there).

## Mod UI: match the game

Treat the game's existing UI as the design system for this mod.

- Prefer duplicating or instantiating native game scenes, controls, textures,
  frames, hover tips, labels, buttons, selection outlines, and animations.
- Use the game's `MegaLabel`/`MegaRichTextLabel` fonts and theme values. Do not
  introduce generic Godot controls, arbitrary fonts, improvised colors, or
  custom panel styling when an equivalent game element exists.
- Before creating a UI element, inspect the installed game/decompiled source
  and find the closest native screen or widget to use as the reference.
- Preserve native interaction behavior: hover animation, click handling,
  focus behavior, tooltips, dismissal, input capture, and selection feedback.
- If a native element is duplicated outside its original container, reset all
  inherited anchors, offsets, scale, rotation, minimum size, and mouse filters,
  then explicitly center its label after it has a real layout size.
- Selection state must be visually unmistakable and should reuse the relevant
  native selection outline or reticle. Do not layer multiple independent
  selection indicators.
- Prefer native hover tips for contextual details instead of persistent tiny
  overlays. Persistent information belongs in the screen's native information
  strip or another established game surface.

UI work is not complete until it has been inspected in-game at the target
resolution and checked for overlap, centering, clipping, input leakage, hover
behavior, and consistency with adjacent native UI.

## Dense information: use structure and alignment

When presenting several related values, optimize for scanning.

- Prefer tables or explicit columns over prose-like runs of labels and values.
- Keep row labels left-aligned and numeric values right-aligned.
- Give numeric columns enough fixed or proportional width that values line up
  against the right side of their containing panel.
- Center controls within their rows and center text within button bounds.
- Use concise visible labels when space is limited and put the longer
  explanation in a native tooltip.
- Avoid placing important text over detailed card art. Use native dark
  backgrounds, information strips, or tooltip frames for legibility.

## Directional changes apply across the whole mod

A product or visual direction is a mod-wide rule unless the request explicitly
limits it to one screen.

Do not fix only the screenshot or screen where the inconsistency was reported.
Search for every implementation of the old wording, style, calculation, or
interaction and update or intentionally exempt each one. Keep shared behavior
in shared helpers where practical so screens cannot silently diverge.

For every directional change, audit all equivalent surfaces of this mod — see
**Surfaces to audit** above — including buttons, labels, selection state, hover
tips, persistent overlays, bottom information text, empty states, and
keyboard/controller dismissal.

## Version, verification, and GitHub

Every new mod version must be committed and pushed to this repository during the
same task.

1. Keep the manifest version and any displayed/logged version in sync.
2. Update user-facing documentation when behavior or controls change.
3. Build the mod and run its test suite before committing.
4. Check `git diff --check` and review the complete scoped diff.
5. Commit only the intended project files; preserve unrelated and untracked
   user files.
6. Push the active branch to GitHub and report the commit hash.

Do not describe a version as complete if it exists only in the working tree.
A local game deployment is separate from a GitHub release. Always deploy a
verified mod build when the game is not running, even if the user did not
separately request deployment. Verify the installed manifest and DLL after
copying. If the game is running, do not terminate it to replace a locked DLL;
report that deployment is pending instead.

## Steam Workshop

The `workshop/` directory is this mod's uploader workspace. See
`docs/sts2-modding.md` for what each file means.

- Never change the manifest `id` once the mod has been published. It is the
  Workshop item's identity, the install folder name, and the DLL filename.
- `workshop/mod_id.txt` is created by the uploader on the first publish and must
  be committed. Losing it orphans the published item and the next upload creates
  a duplicate.
- Stage uploads with `scripts/package-workshop.ps1`. It rebuilds Release and
  populates `workshop/content/`. Do not hand-copy files, and do not ship the
  `.pdb`.
- Fill in `changeNote` in `workshop/workshop.json` before every update; it is
  the changelog subscribers see.
- Bump the manifest version and push to GitHub before uploading, so the
  published version always corresponds to a commit.
