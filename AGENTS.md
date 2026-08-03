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
| Version is also printed in | `PathingPlusCode/MainFile.cs` |
| Gameplay | Informational only; `affects_gameplay` is `false` |
| Dependencies | None yet |

The mod adds route planning to the map screen. Clicking a non-travelable map node pins
it as a waypoint; the mod enumerates every route from the current position through all
pins and draws them as runs of the game's `map_dot` texture (native spacing, jitter,
flips, and rotation noise, seeded per route) in its own overlay layer inside `TheMap`
(above the game's dotted connections, below the node icons). Colors come from
`StsColors`. With five or fewer routes left, a legend panel above the native Share
button shows them as a table — one row per route, named by a letter in the route's
line color ("A)", "B)", …), count columns headed by map icons in the fixed order
elites / fires / combats / shops / chests / events, zeros dimmed — plus a vertical
icon-column tooltip (boss end at the top) on hover/focus, darkening that route to
the traveled-ink color on the map while the rest fade. Pinned nodes are stamped with
the game's own map_circle art tinted rust — always `map_circle_4`, the completed
frame: the earlier four are the drawing animation and look torn as stills. The
controller cursor is the same art in gold, shown only while
`NControllerManager.IsUsingDirectionalNavigation`.

Two lifecycle rules the hard way: the map screen's root stays in the tree when the
map closes (the game only hides its own contents), so every panel parented to the
screen root hides itself on `Closed` and shows on open — or it lingers over combat
and the settings menu. And a locked route is matched by suffix, not equality:
advancing a floor along it shortens every recomputed route by its head, so the tail
IS the same plan; deviation clears it naturally because the new position is no
longer on the stored route.

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

**The zoomed-out view is the controller mode** (Right Trigger or the Zoom button;
every map open starts un-zoomed). While zoomed: all native scrolling and screen
mouse handling is frozen, every drawn node — the whole map, not just route members —
becomes focusable in a four-way grid wired from the drawn layout (`NodeNavigator`),
a gold ink ring marks the focused node, and the cursor survives pin presses and the
game's own focus grabs via a deferred re-grab of the remembered node. Select pins a
non-travelable node, select on a travelable node travels. All focus state is
snapshotted before wiring and restored on exit, map change, screen close, and
dispose — the mode must stay inert unless deliberately toggled.

The route enumeration, any-pin filtering, and boss-tail dedupe live in
`PathingPlusCode/Pathing/` — pure logic, linked into `PathingPlus.Tests`, and the
part that must never regress silently.

Game coupling that a game update can move (verify after every update):

- Harmony targets: `NMapScreen.Open` / `SetMap` / `_Input` /
  `RecalculateTravelability` / `ProcessControllerEvent` (the last two private,
  patched by string name), `NClickableControl._GuiInput`, and
  `NMapDrawings.ClearDrawnLinesLocal`.
- Reflection: `NMapScreen._mapPointDictionary`, `NMapScreen._targetDragPos`, and
  `NMapScreen._distY` (zoom-out restore).
- Scroll assumptions: the `_targetDragPos` clamp range [-600, 1800] and the
  "-600 + row * _distY" current-row formula.
- Input action: `Controller.rightTrigger` (`controller_right_trigger`).
- Node paths: `TheMap`, `TheMap/Points`, `MapLegend/Header`, `MapLegend/LegendItems`.
- Resources: `images/atlases/ui_atlas.sprites/map/icons/map_*.tres`,
  `images/atlases/compressed.sprites/map/map_dot.tres`,
  `images/atlases/compressed.sprites/map/map_circle_4.tres`,
  `images/ui/tiny_nine_patch.png`, and the `StsColors` palette.
- Scroll behavior: plan mode writes `_targetDragPos` directly and assumes the
  [-600, 1800] clamp range from `UpdateScrollPosition`.

### Surfaces to audit

- The map screen overlay: route dot runs (individual colors, union view when more
  than five routes, ink highlight / fade states) and gold pin rings.
- The routes panel: header count, hint line, up to five route rows, lock marker,
  hover/focus/select behavior, and its focus chain from the native map legend.
- The routes table: column header icons, per-row counts (zeros dimmed), fixed
  category order, row hover/focus/select, and the vertical icon-column tooltip
  (boss at top) with its position and clamping.
- Persistence: pins and locked route restored only onto their own map, pruned when
  stale, saved on every change; the Clear button empties the file's pin list too.
- The Zoom button: label states, full-map framing at any act size, drag/wheel while
  zoomed, and the snap back to the current row.
- Controller mode: the Right Trigger hotkey, all-node focus wiring and its
  restoration, the gold cursor ring, cursor retention across pin presses,
  select-to-pin versus select-to-travel, and the frozen native scroll/mouse
  handlers while zoomed.
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
