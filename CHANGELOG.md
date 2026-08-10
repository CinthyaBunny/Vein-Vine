# Changelog

Everything notable that changes in Vein & Vine, newest first. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions are four-part `major.minor.patch.build`, because Dalamud requires a
four-part `AssemblyVersion` and the in-game installer compares them to decide
whether a user is offered an update.

**Add entries to `## [Unreleased]` as you go**, not at release time — the notes
are far more accurate written next to the change than reconstructed from a
diff a week later. [`tools/bump-version.ps1`](tools/bump-version.ps1) promotes
that section into a dated release, and copies it into both plugin manifests so
it shows up in-game.

## [Unreleased]

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
- `CHANGELOG.md`, a Release-build guard that fails if `repo.json` and the csproj
  disagree on the version, and `tools/bump-version.ps1` to move all of it
  forward in one step.

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
