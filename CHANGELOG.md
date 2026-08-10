# Changelog

Everything notable that changes in Vein & Vine, newest first. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions are four-part `major.minor.patch.build`, because Dalamud requires a
four-part `AssemblyVersion` and the in-game installer compares them to decide
whether a user is offered an update.

**Add entries to `## [Unreleased]` as you go**, not at release time — the notes
are far more accurate written next to the change than reconstructed from a diff
a week later. At release, rename that section to `## [x.y.z.w] - YYYY-MM-DD`
and leave a fresh empty one behind. See [Releasing](README.md#releasing) for the
rest of the steps.

## [Unreleased]

### Changed

- **The two windows are now one, tabbed.** The node list is the first tab of
  what used to be the settings window; `Wishlist`, `Display` and `Appearance`
  sit behind it. Two windows meant hunting for the second one every time you
  wanted to track something, and closing one left the other floating.

  `/veinvine` toggles it on whichever tab you left it, `/veinvine cfg` and the
  installer's cog open it on `Display`, and the node list's cog jumps to
  `Wishlist`. The window keeps its old ImGui id, so its saved position and size
  carry over.

### Added

- An **Appearance** tab with three independent switches for borrowing the
  game's own look: **Font** (Axis, the game's UI typeface), **Colours** (its
  dark blue panels, warm off-white text and muted gold borders), and **Window
  frame** (the WindowA nine-slice panel every normal game window is built
  from).
- `Services/UiStyle.cs`, which owns the Axis font handle, the palette, and the
  frame art. All three come out of the client — nothing is downloaded.

  All three are on by default — a plugin window sitting next to the game's own
  windows may as well look like one — and each drops back to Dalamud's default
  with a click. An explicit choice outranks the default, so anyone who has
  already picked Dalamud keeps it.

  The frame is the least finished of the three: it replaces the window
  background but leaves ImGui's title bar and resize grip in place, so it reads
  as a blend rather than a true native window.

## [0.0.0.3] - 2026-08-10

### Added

- Item icons in both lists, and a mouseover giving the item's own in-game
  description plus its gathering requirements — perception needed for the full
  yield, and the node's star rating.
- `Services/ItemInfo.cs`, reading icons through Dalamud's `ITextureProvider`
  and descriptions from the `Item` sheet. Deliberately not XIVAPI: the client
  already has both on disk, already localised, and a network source would add
  latency, a cache, a rate limit and an offline failure mode for the same
  bytes.
- `iconId`, `perceptionRequired` and `stars` in `nodes.json`. Only the icon id
  is baked in, since every visible row needs it; descriptions are read live for
  the row under the cursor.

### Changed

- The picker splits its items across **All / Miner / Botanist** sub-tabs
  instead of two job checkboxes. Each tab keeps its own sort order and its own
  filtered list, so the two jobs can be sorted differently and switching
  between them is instant. The `Job` column hides itself where a tab has
  already answered it, and the footer counts against that tab's population
  rather than the whole dataset.
- The picker's level range is two boxes you type into rather than sliders.
  They're clamped when editing finishes, not per keystroke, so a half-typed
  number neither blanks the list nor fights you as you type.

### Fixed

- **98 items were missing from a job tab.** Nearly a tenth of the index has
  both mining and botany nodes, but the picker collapsed each item to a single
  job, so every one of them showed on only one of the two tabs. `GatherItem`
  now carries the full set of jobs its nodes span, the Job column reads
  `MIN+BTN` for them, and sorting by job groups them together.
- The main window drew each row's icon before the row-spanning selectable, so
  hovering a row painted the highlight over its own icon. The selectable is now
  submitted first and the icon and name drawn back over it.
- `Configuration.Save` no longer lets a failed disk write escape. Every call
  site is inside an ImGui draw, where an exception unwinds out of a half-drawn
  table and leaves the begin/end stack unbalanced — much worse than a
  preference that did not persist.
- Both list tables release `EndTable` in a `finally`, so a throw from a single
  row costs a log line rather than the game client.

## [0.0.0.2] - 2026-08-10

### Added

- Sortable, filterable node table in the main window: Item, Job, Lv, Zone,
  Status and Dist columns, a map-flag button per row, double-click to flag, and
  a right-click menu offering *Set map flag* and *Stop tracking*.
- Toolbar toggles for `Miner`, `Botanist`, `Timed only`, `Upcoming` and
  `This zone`, all persisted to the config.
- Summary line naming the next node to come up and its countdown when nothing
  timed is currently available.
- The wishlist tab is now an item picker: one row per item rather than per
  node, with text, job, zone, level-band, timed-only and tracked-only filters,
  and `Track all shown` / `Untrack all shown` scoped to whatever the filters
  left.
- `Services/NodeQuery.cs`, a pure layer holding `NodeFilter`, the per-item
  index, window summaries and the picker's sort — so what you see and in what
  order is testable with no game running.
- `PriorityEngine.Sort` is public and static, letting the UI reorder on a
  column-header click without rebuilding the list.
- `CHANGELOG.md`, and a Release-build guard that fails the build if `repo.json`
  and the csproj disagree on the version — the drift that otherwise makes the
  in-game installer silently refuse to offer an update.
- The build copies the current version's changelog section into the `Changelog`
  field of both plugin manifests, so release notes show up in the in-game
  installer without being maintained in three places. Markdown is flattened,
  since none of it renders there.

### Changed

- **The dataset now covers every miner and botanist item, not only timed ones**
  — 1,587 nodes and 1,050 items across 47 zones at Lv1-100, up from 419 nodes
  and 333 items at Lv50-100. Without the always-up nodes the wishlist could
  only track the 333 timed items, so "where do I get iron ore" had no answer.
- Always-up nodes report no countdown and display as `Always`. They render in
  ordinary text rather than the green reserved for something that will expire,
  are excluded from the "up now" count, and sort below nodes that do expire.
- `PriorityEngine.BuildPriorityList` takes an optional `NodeFilter`, a sort key
  and a direction.
- Wishlist reads and writes go through `Plugin`, which keeps a membership set
  and a version counter so the picker can cache its filtered rows.
- The picker clips its rows with `ImGuiListClipper` and recomputes its filter
  and sort only when an input changes, so a still frame does no work.
- The generator no longer drops untimed nodes, and dereferences every sheet row
  through `ValueNullable` — the timed filter used to hide thousands of
  placeholder rows whose refs throw on `Value`.
- `spawnDurationMinutes` of `0` now means "never expires"; the generator writes
  it for always-up nodes and the verifier rejects a node whose duration and
  window list disagree.

### Known gaps

- Spearfishing is still excluded from the dataset (168 nodes): nothing models
  bait or the tug, and neither window offers a Fisher filter.
- The ImGui layer has not been exercised against a running client.

## [0.0.0.1] - 2026-08-10

Initial version.

### Added

- Dalamud plugin scaffold targeting API level 15 on `net10.0-windows`, with
  `/veinvine` and `/vnv` commands, a main window and a settings window.
- `Services/EorzeaTime.cs`, a pure Eorzea clock and the weather seed roll.
- `Services/WeatherService.cs`, resolving a territory's current weather through
  the `WeatherRate` cumulative distribution table.
- `Services/PriorityEngine.cs`, combining the dataset, the clock and the
  wishlist into one sorted list, gated behind `IWeatherProvider` so it can be
  tested against a fake clock.
- `Services/MapUtil.cs` and a native map flag as the plugin's only
  game-state-changing action.
- `tools/NodeGen`, regenerating `Data/nodes.json` from the game's own Excel
  sheets, including the packed-decimal spawn windows in
  `GatheringRarePopTimeTable`, and verifying its output through the plugin's
  own parser.
- `Data/nodes.json` with 419 timed nodes — 333 items across 46 zones — plus a
  schema document.
- `repo.json`, the third-party repository manifest.
- `.gitattributes` pinning the tree to LF.
