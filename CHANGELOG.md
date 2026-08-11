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

<details>
<summary><strong>v0.0.1.3b</strong> - 2026-08-10</summary>

### Changed

- **Finalized Repository Push Functions** Completed integration for pushing code and complete builds to the github repository.

</details>

<details>
<summary><strong>v0.0.1.3</strong> - 2026-08-10</summary>

### Added

- **Sub-tabs under Miner and Botanist** for the finer split within each job:
  `All / Mining / Quarrying` and `All / Logging / Harvesting`, named as the
  game's `GatheringType` sheet names them. Each job tab remembers its own
  narrowing, so pinning Miner to Quarrying leaves Botanist as you left it.

  The dataset was throwing this away — the generator folded gathering types 0
  and 1 into "Mining" and 2 and 3 into "Botany" — so nodes now carry a `method`
  alongside their `type`. 426 Mining, 295 Quarrying, 363 Logging, 503
  Harvesting.

  As with the job tabs, an item that comes off more than one kind of node
  appears under each: 16 items in each job are gatherable both ways, and would
  otherwise have gone missing from one of the two sub-tabs.

  Narrowing to a method rebuilds the item index rather than filtering it. A row
  summarises the nodes behind it, so summarising all of them and then dropping
  rows leaves the survivors describing nodes the tab has excluded. Across the
  seven scopes the picker can show, 109 rows listed a zone that scope can't
  reach, 28 quoted a level below any node it could actually use, and 15 had
  their timed flag decided by the wrong nodes. `BuildItemIndex` now takes the
  scope and applies it before the grouping; the window keeps one index per
  scope, built on first use and dropped on reload.

  The `All` job tab has no sub-strip. It already spans both jobs, so a five-way
  method strip under it would just be a second job filter in a different hat.
- The generator rejects a run where every node came out as `Mining`, which is
  what a broken `GatheringType` link looks like, and one where a node's method
  doesn't belong to its job.

- **Every wishlist tab is labelled with its live row count** — `All (1,050)`,
  `Miner (517)`, `Botanist (631)`, and the same on the method sub-tabs. The
  counts are taken from the filtered list rather than from the population
  behind it, so searching "ore" leaves the strip reading `All (143)`, `Miner
  (118)`, `Botanist (27)`.

  They reconcile at every level: a parent is its two children less the items in
  both. `1,050 = 517 + 631 − 98` across the jobs, `517 = 331 + 202 − 16` across
  the miner's methods. That overlap moves with the filters — under "ore" it is
  2 and 1 — so it is counted, not remembered, and the footer's tooltip spells
  the subtraction out rather than leaving three labels that appear not to add
  up.

  A tab's number is what you get for clicking it, narrowing included, so a
  Miner tab pinned to Quarrying reads `Miner (202)`. That is the one case where
  a label and the sum disagree — the sum uses the jobs in full, being what
  `All` is made of — and the tooltip says which job is narrowed rather than
  leaving the two to contradict each other.

  Rows, labels and footer now share one definition of what is in a tab, so a
  filter added later is reflected in all of them at once instead of leaving the
  counts describing an older list.

### Changed

- **The level boxes refuse a bad keystroke instead of clamping afterwards.**
  They were `InputInt`, which permits `+`, `-`, `.`, `*` and `/`, accepted any
  number at all, and quietly rewrote it when you clicked away. They now decide
  per keystroke whether the text the box *would* hold is still a gathering
  level — the only way a 1–100 limit can hold, since every digit is legal alone
  and still turns 10 into 105 — so there is no invalid state to clamp, and
  nothing changes under you on the way out. Pasted text goes through the same
  filter character by character.

  The rule is pure and sits next to the range it enforces, so it is checked
  exhaustively: every state the box can reach, through every printable key at
  every cursor and selection position, is 54,910 keystrokes reaching exactly
  101 states — the empty box and the hundred levels. Empty means "no bound from
  this end" while you retype and fills itself back in when you leave.

### Fixed

- **The wishlist footer's total left out items shared between the jobs.** It
  counted against `GatherItem.Type`, a single arbitrary job, rather than the
  `Jobs` set the tabs themselves filter on — so Miner reported "of 492" while
  listing 517 items, and Botanist "of 558" while listing 631. Both totals were
  smaller than the list they were describing. It now counts against `Jobs`, and
  off the sub-tab's own scoped index, so a narrowed tab compares itself to its
  method rather than to the whole job.
- The picker's empty state named the job when a sub-tab was what had emptied
  the list, sending you off to check a filter that wasn't the one hiding rows.
- **The node list's status colours were fixed constants**, so the green for "up
  now" and the amber for "up soon" were the same on every theme. On Light they
  were all but invisible — the amber was `#F2C759` on a `#F5D4A9` panel, which
  is the same colour twice. They are now fitted to each theme: the hue is kept,
  because green and amber carry the meaning, and only the lightness moves. On
  the three light themes they invert to a deep green and a dark olive.
- **Buttons, tabs, inputs and selected rows are now objects you can see**,
  rather than tints of the panel that happened to keep their text readable.

  Sampling the game's own previews shows its controls always stand off the
  panel — lighter on the dark themes, darker on the light ones — and that it
  flips the label colour to suit when it sinks one hard. ImGui has a single
  text colour, so instead the lift is kept modest and the direction reversed
  whenever the game's would cost the label its contrast.

  Both constraints are now checked together, which is what caught the two
  themes where only one direction works: Clear Pink, whose dark text leaves so
  little headroom that a darker button is unreadable and a lighter one
  invisible, and Classic FF, whose idle tab barely registered against its very
  dark panel.

  Every control also gets a 1px border pinned by the theme, so it has an edge
  and not just a shade.

  Measured across all eight themes and seven control states: body text
  6.4–15.7:1, status colours 4.5–9.8:1, control fills 1.21–2.80:1 against their
  panel, every label 4.5:1 or better.

### Changed

- **Buttons and fields are pill-shaped**, as they are in game. `FrameRounding`
  is half the frame height, and since ImGui clamps rounding to half the shorter
  side, one number gives a short button a full pill and a wide search field the
  same end caps — the game's two shapes from a single setting.
- **Tabs are the game's shape**, drawn by hand in `GameTabBar.cs`: elongated
  hexagons pointed at both ends, with the selected one recessed and darker as
  it is in game. ImGui's tab bar offers a corner radius and nothing else.

  Point sharpness, the gap between tabs and the label padding are three
  constants at the top of the file, so the strip can be retuned without
  touching the drawing.

  The shape is not the whole reason. The game uses a dark tab with a pale label
  on every theme, light ones included — Light's tabs are near-black lettered in
  cream on a peach panel — and ImGui cannot express that, because one `Text`
  colour serves the panel and the tabs alike. Drawing the strip lets each tab
  choose its own label.

  The strip is one call returning the selected index rather than a begin/end
  pair, which also makes a changing set of tabs free: the Nodes tab comes and
  goes as the list is docked or undocked.

  </details>

## [0.0.1.2] - 2026-08-10

The themes now actually match the game's, measured against it rather than
guessed at.

### Changed

- **`Colours` and `Window frame` are now one `Theme` dropdown.** Picking one of
  the game's themes means wanting the whole look, and the border is drawn from
  that theme's own colours anyway, so keeping them apart only allowed
  combinations nobody wanted.
- **The window panel and border now follow the theme**, instead of every theme
  wearing the same dark frame, and the border is a **hairline** rather than a
  heavy band. Every border width is pinned by the theme too, rather than
  inherited from the user's Dalamud style.
- **The game's window art is no longer used.** Reading its pixels turned up two
  problems: the tiles are near-black neutral grey and ImGui tints by
  multiplying, which can only darken — so no tint could ever produce a light
  panel for Light, Clear White or Clear Pink — and the set is not a nine-slice
  at all. `Corner` is a 32×96 strip holding the panel's whole vertical profile,
  so treating it as one quadrant of a 2×2 atlas made every corner 16×48, which
  is where the thick dark band across the top of each window came from.

  A themed background plus a one-pixel border reads closer to a real game panel
  at its edges, behaves the same across all eight themes, and takes the texture
  loading, the readiness gating and the transparent-window failure mode out with
  it.
- **The theme grounds are corrected**, and now sampled from the game's own
  theme previews rather than guessed. `UIColor` row 7 looked like the window
  colour but is each theme's darkest or lightest tone: pure black for Dark,
  Clear Blue, Clear Green and Clear Grey alike, and pure white for both Clear
  White and Clear Pink — so four themes rendered identically black and two
  identically white.

  The themes turn out to be far more saturated than they look in memory:
  Classic FF is a vivid blue-violet, not a dark navy; Clear White is a mid
  grey, not white; Clear Pink is a strong pink, not a blush; Light is a warm
  peach, not a grey parchment.
- Dimmed text is nudged toward the full text colour when it falls below 3:1
  against the panel. Only Clear Pink needed it — purple-grey on pink sat at
  2.7:1, and dimmed text carries the zone, level and window columns.
- Table column separators are a faint text-tinted hairline instead of
  accent-coloured. They were drawing a full-height gold rule between every
  column, which no game list has.
- **Every ImGui colour slot is themed**, not just the obvious ones — sliders,
  separators, text selection, nav highlights, menu bars, drag-drop and the
  modal dimming layers included. Those leftovers at Dalamud's defaults are what
  gave a half-themed window away.

### Fixed

- 0.0.1.1 could render a completely transparent window: it switched ImGui's
  background off as soon as the border textures had loaded, without checking
  that the palette had been read. Dropping the texture path removes the
  possibility rather than guarding it — the background is always drawn now.

### Notes

Reference screenshots of all eight in-game themes are kept in
`Alpha 0.0.1.x Photos/Theme Examples`. The panel colours in `UiStyle` are
sampled from them, so that folder is the source those values answer to.

## [0.0.1.1] - 2026-08-10

Vein & Vine now wears the game's own clothes.

### Added

- **An Appearance tab**, with four dropdowns. Everything it uses comes out of
  the client — nothing is downloaded.

  - **Font** — Axis, the typeface the game's own interface draws with. The
    single biggest thing that makes a window read as native.
  - **Colours** — all eight of the game's UI themes: Dark, Light, Classic FF,
    Clear Blue, Clear White, Clear Green, Clear Grey and Clear Pink. Named and
    ordered as they are in System Configuration, and coloured from the same
    `UIColor` sheet the game tints its own interface with.
  - **Window frame** — the WindowA nine-slice panel every normal game window is
    built from.
  - **Node list** — tabbed, or docked (see below).

  The first three are on by default: a plugin window sitting next to the game's
  own windows may as well look like one. Each drops back to Dalamud's default
  with a click, and an explicit choice outranks the default, so anyone who has
  already picked Dalamud keeps it.

- **The node list can be docked to the main window's left edge** instead of
  living in a tab. The panel follows the window around and matches its height,
  but its width is yours: drag the grip in its bottom-left corner and it widens
  leftwards into free space rather than shoving the window it is attached to.
  The Nodes tab steps aside while docked, and both share one `NodeListTab`, so
  their sort state cannot drift apart.

- `Services/UiStyle.cs`, which owns the font handle, the palettes and the frame
  art.

### Changed

- **The two windows are now one, tabbed.** The node list is the first tab of
  what used to be the settings window; `Wishlist`, `Display` and `Appearance`
  sit behind it. Two windows meant hunting for the second one every time you
  wanted to track something, and closing one left the other floating.

  `/veinvine` toggles it on whichever tab you left it, `/veinvine cfg` and the
  installer's cog open it on `Display`, and the node list's cog jumps to
  `Wishlist`. The window keeps its old ImGui id, so its saved position and size
  carry over.

### Notes

Three things the game's data decided, rather than taste:

- Palettes are derived from four anchors per theme rather than hand-written
  eight times over. The accent comes from `UIColor` row 22 and not the paler
  row 8, which is pure white under Classic FF and would sink every border into
  the text. Clear Blue and Clear Green get a hand-picked ground, because
  neither has a blue or green entry anywhere in the sheet — the game tints its
  window *textures* for that instead of storing a colour.
- Clear Grey and Clear Pink come from the two `UIColor` columns Lumina has not
  named yet. The column order and the data agree on which is which, and if a
  future Lumina names them the plugin stops compiling rather than silently
  reading the wrong colours.
- The docked panel's resize grip is hand-drawn, and ImGui's own resizing is
  switched off. Its grip sits on the bottom-right, which is the one corner that
  cannot move when the right edge is welded to the host.

### Known gaps

- The window frame replaces the background but leaves ImGui's title bar and
  resize grip in place, so it reads as a blend rather than a true native
  window. Docking is likewise a concept rather than a finished system.

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
