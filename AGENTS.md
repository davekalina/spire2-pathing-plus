# __MOD_NAME__ — repository instructions

This repository is one Slay the Spire 2 mod. These instructions apply throughout it.
A more specific `AGENTS.md` may add to or override them for its directory.

`docs/sts2-modding.md` describes how the game's mod loader, manifest, and Steam Workshop
pipeline work. Those are platform facts. This file is how I want the work done.

## This mod

| | |
| --- | --- |
| Mod id | `__MOD_ID__` |
| Display name | __MOD_NAME__ |
| Installs to | `<game>/mods/__MOD_ID__/` |
| Manifest | `__MOD_ID__.json` |
| Version is also printed in | `__MOD_ID__Code/MainFile.cs` |
| Gameplay | Informational only; `affects_gameplay` is `false` |
| Dependencies | None yet |

> **Fill this in.** Replace the paragraph below with what the mod actually does and which
> parts must not regress silently, then keep it current. An agent reading only this file
> should understand the mod's shape without reading the source.

_No behavior implemented yet._

### Surfaces to audit

> **Fill this in.** List every screen, overlay, control, and state this mod touches. A
> directional change has to be applied across all of them, and this list is what makes
> that checkable.

- _none yet_

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
