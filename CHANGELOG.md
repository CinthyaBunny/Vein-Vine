# Changelog

Everything notable that changes in Vein & Vine, newest first. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Versions are four-part `major.minor.patch.build`, because Dalamud requires a
four-part `AssemblyVersion` and the in-game installer compares them to decide
whether a user is offered an update.

The newest release sits outside a `<details>` wrapper so GitHub renders it open;
everything older is collapsed. A release build reads its notes straight out of
this file by matching the `<strong>` version marker, so keep that line's shape
when adding an entry.

Entries are written for players, not developers — a release build copies them
into the in-game installer, which is where most people will read them. Describe
what happened to the plugin in plain language, and leave file names, internals
and the reasoning behind a fix to the code and the commit history. Where a
change has no visible effect, say so briefly rather than explaining it.


## <strong>v0.0.1.4</strong> - 2026-08-10

Housekeeping release. Nothing you can see has changed.

### Fixed

- **Fixed an issue where there were multiple instances of final build files.**

- **Fixed an issue where release notes were not handed off to the ingame
  installer.**

- **Fixed a version mismatching issue.**

- **Fixed an issue where the main window reset its own window settings every
  frame.**

- **Fixed two different brightness calculations being used to decide whether a
  theme is light or dark.** Both agreed on all eight themes, so nothing looked
  wrong, but they could have disagreed on a theme added later.

### Changed

- Dead and duplicated code removed.


<details>
<summary><strong>v0.0.1.3b</strong> - 2026-08-10</summary>

### Changed

- Repository publishing set up. No change to the plugin itself.

</details>


<details>
<summary><strong>v0.0.1.3</strong> - 2026-08-10</summary>

### Added

- **Miner and Botanist now have sub-tabs** for the finer split within each job:
  `All / Mining / Quarrying` and `All / Logging / Harvesting`, named as the game
  names them. Each job tab remembers its own choice, so narrowing Miner to
  Quarrying leaves Botanist as you left it.

  An item that comes off more than one kind of node appears under each, so
  nothing goes missing from one of the two sub-tabs.

  The `All` tab has no sub-tabs. It already covers both jobs, so a five-way
  strip under it would just be a second job filter in a different hat.

- **Every wishlist tab is labelled with how many items it holds** — `All
  (1,050)`, `Miner (517)`, `Botanist (631)`, and the same on the sub-tabs. The
  counts follow whatever you have filtered, so searching "ore" leaves the strip
  reading `All (143)`, `Miner (118)`, `Botanist (27)`.

  An item both jobs can gather is counted on both tabs, so the tabs add up to
  more than the total. Hovering the count spells out the difference rather than
  leaving three numbers that look like they disagree.

  A tab's number is what you get for clicking it, narrowing included, so a Miner
  tab pinned to Quarrying reads `Miner (202)`.

### Changed

- **The level boxes only accept whole numbers from 1 to 100.** Anything else is
  refused as you type, so a box can never be left holding something that isn't a
  level, and nothing is silently rewritten when you click away.

- **Buttons and fields are pill-shaped**, as they are in game.

- **Tabs are shaped like the game's** — elongated hexagons pointed at both ends,
  with the selected one recessed and darker. The game uses a dark tab with a
  pale label on every theme, light ones included, and this lets each tab carry
  its own label colour to match.

### Fixed

- **The wishlist footer's total left out items shared between the jobs**, so it
  could report a smaller total than the number of rows it was showing — "of 492"
  while listing 517 items, and "of 558" while listing 631. It now counts the
  same items the tab does, and a narrowed tab compares itself to its own
  sub-tab rather than to the whole job.

- **The "no items match" message named the wrong filter** when a sub-tab was
  what had emptied the list, sending you off to check something that wasn't
  hiding the rows.

- **The node list's green and amber were the same on every theme**, which left
  them all but invisible on the light ones — the amber was very nearly the
  colour of the Light panel behind it. They are now fitted to whichever theme
  you are using. The colours still mean the same thing; only their lightness
  moves, so on the light themes they become a deep green and a dark olive.

- **Buttons, tabs, inputs and selected rows now stand out from the panel**
  rather than being a slightly different shade of it, and each carries a thin
  border so it has an edge and not just a tint. Checked against all eight themes
  so a control looks like a control, and its label stays readable, on every one.

</details>


<details>
<summary><strong>v0.0.1.2</strong> - 2026-08-10</summary>

### Changed

- **`Colours` and `Window frame` are now a single `Theme` dropdown.** Picking
  one of the game's themes means wanting the whole look, so keeping the two
  apart only allowed combinations nobody wanted.

- **The window panel and border now follow the theme**, instead of every theme
  wearing the same dark frame, and the border is a hairline rather than a heavy
  band.

- **The game's window art is no longer used.** It could never produce a light
  panel for Light, Clear White or Clear Pink, and the way it was being pieced
  together is where the thick dark band across the top of every window came
  from. A themed background with a one-pixel border reads closer to a real game
  panel and behaves the same across all eight themes.

- **The theme colours are corrected**, sampled from the game's own theme
  previews rather than guessed at. Four themes had been rendering identically
  black and two identically white.

  They also turn out to be far more saturated than they look in memory: Classic
  FF is a vivid blue-violet, not a dark navy; Clear White is a mid grey, not
  white; Clear Pink is a strong pink, not a blush; Light is a warm peach, not a
  grey parchment.

- **Dimmed text is brightened where it would be hard to read** against the
  panel. Only Clear Pink needed it, where the zone, level and window columns sat
  below a comfortable contrast.

- **Table column separators are a faint hairline** instead of a full-height gold
  rule between every column, which no game list has.

- **Every part of the window is themed**, not just the obvious ones — sliders,
  separators, text selection, menu bars and the dimming behind pop-ups
  included. Those leftovers are what gave a half-themed window away.

### Fixed

- **0.0.1.1 could render a completely transparent window.** It switched its own
  background off as soon as the border art had loaded, without checking that the
  colours had been read. The background is now always drawn.

### Notes

Reference screenshots of all eight in-game themes are kept with the project, and
the panel colours are sampled from them.

</details>


<details>
<summary><strong>v0.0.1.1</strong> - 2026-08-10</summary>

Vein & Vine now uses FFXIV's own themes.

### Added

- **An Appearance tab**, with four dropdowns. Everything it uses comes out of
  the client — nothing is downloaded.

  - **Font** — Axis, the typeface the game's own interface draws with. The
    single biggest thing that makes a window read as native.
  - **Colours** — all eight of the game's UI themes: Dark, Light, Classic FF,
    Clear Blue, Clear White, Clear Green, Clear Grey and Clear Pink, named and
    ordered as they are in System Configuration.
  - **Window frame** — the panel every normal game window is built from.
  - **Node list** — tabbed, or docked.

  The first three are on by default: a plugin window sitting next to the game's
  own windows may as well look like one. Each drops back to Dalamud's default
  with a click, and an explicit choice outranks the default, so anyone who has
  already picked Dalamud keeps it.

- **The node list can be docked to the main window's left edge** instead of
  living in a tab. The panel follows the window around and matches its height,
  but its width is yours: drag the grip in its bottom-left corner and it widens
  leftwards into free space rather than shoving the window it is attached to.
  The Nodes tab steps aside while docked, and both show the same list, so their
  sort order cannot drift apart.

### Changed

- **The two windows are now one, tabbed.** The node list is the first tab of
  what used to be the settings window; `Wishlist`, `Display` and `Appearance`
  sit behind it. Two windows meant hunting for the second one every time you
  wanted to track something, and closing one left the other floating.

  `/veinvine` toggles it on whichever tab you left it, `/veinvine cfg` and the
  installer's cog open it on `Display`, and the node list's cog jumps to
  `Wishlist`. Your saved window position and size carry over.

### Known gaps

- The window frame replaces the background but leaves the title bar and resize
  grip as they were, so it reads as a blend rather than a true native window.
  Docking is likewise a concept rather than a finished system.

</details>


<details>
<summary><strong>v0.0.0.3</strong> - 2026-08-10</summary>

### Added

- **Item icons in both lists**, and a mouseover giving the item's own in-game
  description along with what a gatherer needs for the full yield — the
  perception required, and the node's star rating. All of it is read from the
  game client, so it works offline and matches the language you play in.

### Changed

- The picker splits its items across **All / Miner / Botanist** tabs instead of
  two job checkboxes. Each tab keeps its own sort order and its own filtered
  list, so the two jobs can be sorted differently and switching between them is
  instant. The `Job` column hides itself where a tab has already answered it.

- The picker's level range is two boxes you type into rather than sliders, so a
  half-typed number neither blanks the list nor fights you as you type.

### Fixed

- **98 items were missing from a job tab.** Nearly a tenth of the list can be
  gathered by both jobs, but each item was filed under only one of them, so
  every one of those showed on a single tab. They now appear under both, read
  `MIN+BTN` in the Job column, and group together when you sort by job.

- **Hovering a row in the node list painted the highlight over the row's own
  icon.** The icon now sits on top where it belongs.

- **A failed settings save could disturb the window mid-draw**, which was far
  worse than the preference simply not persisting. It is now contained.

- **A problem drawing a single row could take the game client down with it.** It
  now costs a line in the log instead.

</details>


<details>
<summary><strong>v0.0.0.2</strong> - 2026-08-10</summary>

### Added

- **A sortable, filterable node table** in the main window: Item, Job, Lv, Zone,
  Status and Dist columns, a map-flag button on every row, double-click to flag,
  and a right-click menu offering *Set map flag* and *Stop tracking*.

- **Toolbar toggles** for `Miner`, `Botanist`, `Timed only`, `Upcoming` and
  `This zone`, all remembered between sessions.

- **A summary line** naming the next node to come up and its countdown, for when
  nothing timed is available right now.

- **The wishlist tab is now an item picker**: one row per item rather than per
  node, with text, job, zone, level-band, timed-only and tracked-only filters,
  and `Track all shown` / `Untrack all shown` scoped to whatever the filters
  left.

- **Release notes now reach the in-game installer**, and a release build refuses
  to package the plugin when its version numbers disagree — the drift that
  otherwise makes the installer silently decline to offer an update.

### Changed

- **The data covers all mining and botany nodes, not just timed ones** — 1,587
  nodes and 1,050 items across 47 zones at Lv1-100, up from 419 nodes and 333
  items at Lv50-100. Without the always-up nodes the wishlist could only track
  the 333 timed items, so "where do I get iron ore" had no answer.

- **Always-up nodes read as `Always`** and show no countdown. They are drawn in
  ordinary text rather than the green kept for something about to expire, are
  left out of the "up now" count, and sort below nodes that do expire.

- **The item picker is faster with the full list**, drawing only the rows on
  screen and recalculating only when something you changed asks it to.

### Known gaps

- Spearfishing is still left out of the data (168 nodes): nothing models bait or
  the tug, and neither window offers a Fisher filter.
- The interface has not been exercised against a running client.

</details>


<details>
<summary><strong>v0.0.0.1</strong> - 2026-08-10</summary>

Initial version.

### Added

- The plugin itself, with `/veinvine` and `/vnv` commands, a main window and a
  settings window.
- FFXIV's Eorzea clock and its deterministic weather, which is what makes it
  possible to say when a node is up without reading anything from the game.
- A priority-sorted list built from the node data, the clock and your wishlist.
- A map flag — the plugin's only action that touches the game.
- The node data: 419 timed nodes, 333 items across 46 zones, generated from the
  game's own data files.

</details>
