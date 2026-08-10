# Vein & Vine

A read-only gathering companion for FFXIV. Shows a priority-sorted list of active
and upcoming nodes from your wishlist. Never moves the player, never gathers
automatically — the only game-state-changing action is a native map flag, which
the player still has to walk to.

Internal name is `VeinAndVine`; the display name `Vein & Vine` appears only in the
manifest `Name` field and UI strings.

## Target environment

| | |
|---|---|
| Dalamud | 15.0.3.1 |
| `DalamudApiLevel` | **15** |
| TFM | `net10.0-windows`, x64 |
| Packager | DalamudPackager 15.0.0 |

The API level is a hard gate — Dalamud refuses to load a plugin whose
`DalamudApiLevel` doesn't match the running Dalamud.

## Building

```bash
dotnet build -c Release
```

Dalamud reference assemblies resolve from `%AppData%\XIVLauncher\addon\Hooks\dev\`
(see `Directory.Build.props`); override with `DALAMUD_HOME`. The build fails with a
readable message if it can't find `Dalamud.dll`.

Release output: `VeinAndVine\bin\x64\Release\VeinAndVine\latest.zip`.

## Loading it in-game (dev)

The `devPlugins` folder is deprecated. Instead:

1. `/xlsettings` → **Experimental** → **Dev Plugin Locations**
2. Add `C:\AGB_C3\VeinAndVine\bin\x64\Debug`
3. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** → load / reload

Unload before rebuilding, or the DLL stays file-locked.

## VS Code

`.vscode/` is set up. Install the **C#** extension (`ms-dotnettools.csharp`) —
it provides both IntelliSense and the `coreclr` debugger that `launch.json`
uses.

- <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>B</kbd> → Debug build. **Unload the dev
  plugin first**, or the DLL is file-locked and the build fails.
- <kbd>F5</kbd> → **Attach to FFXIV**. Attaching does not load the plugin; build
  and load it via Dev Tools first, then attach.
- `justMyCode` is off so you can step into Dalamud and FFXIVClientStructs.

A breakpoint freezes the entire game client, not just the plugin. Sitting on one
for more than a few seconds while online will disconnect you — use the log
(`/xllog`) for anything you'd otherwise trace in a hot path.

`dotnet.defaultSolution` is pinned in `settings.json` so the C# extension doesn't
try to load the stale scaffold's csproj.

## Commands

| Command | Effect |
|---|---|
| `/veinvine` | Toggle the window, on whichever tab you left it |
| `/vnv` | Alias, hidden from the command list |
| `/veinvine cfg` | Open it on the **Display** tab |

## Architecture

The deliberate split is **game-coupled** vs **pure**. Everything in the pure
column is a function of its inputs and can be unit tested with no game running.

| File | Coupling | Purpose |
|---|---|---|
| `Plugin.cs` | Dalamud | Wiring: services, config, commands, window lifecycle |
| `Service.cs` | Dalamud | `[PluginService]` container |
| `Services/WeatherService.cs` | Lumina sheets | Territory → weather via the rate table |
| `Services/NodeDatabase.cs` | filesystem | Loads `Data/nodes.json` |
| `Services/MapUtil.cs` | Lumina sheets | World-space → map-coordinate conversion |
| `Services/EorzeaTime.cs` | **pure** | Eorzea clock + the weather seed roll |
| `Services/PriorityEngine.cs` | **pure** | Availability + sorting, gated behind `IWeatherProvider` |
| `Services/NodeQuery.cs` | **pure** | Filters, the per-item index, picker sorting |
| `Services/ItemInfo.cs` | Lumina, textures | Item icons and descriptions, from the game client |
| `Services/UiStyle.cs` | fonts, textures | Optional game font, palette and window frame |
| `Models/` | **pure** | `GatherNode`, `WishlistEntry` |
| `Windows/MainWindow.cs` | ImGui | The one window: tab bar, Display and Appearance |
| `Windows/NodeListTab.cs` | ImGui | The node list, and the map flag |
| `Windows/NodeListWindow.cs` | ImGui | Optional docked panel hosting the same node list |
| `Windows/UiShared.cs` | ImGui | Colours, duration formatting, sort-spec bridge |
| `tools/NodeGen/` | Lumina, build-time | Regenerates `Data/nodes.json` from game sheets |

`PriorityEngine` depends on `IWeatherProvider`, not `WeatherService`, specifically
so it can be tested against a fake clock.

## The UI

One window, four tabs. **Nodes** leads because that is the question you keep the
window open to answer; **Wishlist**, **Display** and **Appearance** configure it
and sit behind it in the same frame. They used to be two separate windows, which
meant hunting for the second one every time you wanted to track something.

Both lists are ImGui tables with sortable, resizable, hideable columns.

**Nodes tab** — one row per node, coloured green (up, and going to expire),
amber (up within five minutes), grey (waiting), or ordinary text for a node
that is simply always there. That fourth state matters: three quarters of the
dataset is always up, and painting it the same green as a node with four
minutes left would drown out the only rows worth hurrying for. For the same
reason the summary counts only *timed* nodes as "up now", and the default sort
puts what expires above what doesn't.

Above the table: `Miner` / `Botanist`, `Timed only`, `Upcoming` (show nodes that
aren't up yet), and `This zone` — all persisted. Double-clicking a row sets the
map flag; so does the marker button at its right end, and right-click also
offers *Stop tracking*.

**Wishlist tab** — one row per *item*, not per node. The dataset is node-shaped,

so the same item appears in several zones with several windows; `NodeQuery`
collapses that into one row per item, with the zone count and the union of its
windows.

Items are split across **All / Miner / Botanist** sub-tabs — 517 mining and 631
botany out of 1,050, because **98 items have both mining and botany nodes** and
so appear on both tabs. That overlap is why `GatherItem` carries a `Jobs` flag
set rather than one `NodeType`: collapsing it to a single job hides each of
those items from one of the two tabs.

Each tab owns its sort order and its own filtered list, so sorting
Miner by level doesn't disturb how Botanist was left, and switching between
them is instant. The `Job` column hides itself on the single-job tabs, where it
has nothing to say. `All` is kept because searching for an item whose job you
don't know is a real thing you do.

The filter row sits *above* the tabs and applies to all of them — a search you
had to retype on every tab switch would be worse than no tabs. It holds text,
zone, a level range you type into, timed-only, and tracked-only. `Track all
shown` applies to exactly the rows the filters left on the current tab, which
is what makes "every level 80 botany item" a two-click operation.

At 1,050 rows the picker is clipped with `ImGuiListClipper` and its
filter/sort result is cached behind a key of its inputs, so scrolling submits
only the visible slice and a still frame does no work at all.

The level boxes are clamped when you finish editing rather than on each
keystroke — clamping live means backspacing the field to empty snaps it to 1,
and the next two digits you type land on the wrong side of the limit.
Filtering reads a clamped view in the meantime, so a half-typed number never
blanks the list, and a min above the max reads as a range rather than as
nothing.

Both lists show the item's game icon, and hovering a row gives the item's own
in-game description plus its gathering requirements — perception needed for the
full yield, and the node's star rating.

**None of that is fetched over the network.** The client already has the icons
and the localised text on disk, so `ItemInfo` reads them through Dalamud's
`ITextureProvider` and the `Item` sheet. XIVAPI would trade that for latency, a
cache, a rate limit, and an offline failure mode in exchange for the same
bytes. The only thing baked into `nodes.json` is the two-byte `iconId`, because
every visible row needs it; descriptions are read live for the single row under
the cursor.

Icons load asynchronously, so a row reserves the space whether or not the
texture has arrived — otherwise the whole list twitches on first paint.

### Wearing the game's clothes

The **Appearance** tab has three independent switches, all sourced from the
client rather than from bundled assets:

| Switch | What it uses |
|---|---|
| Font | **Axis**, via `FontAtlas.NewGameFontHandle` — the typeface the game's own UI draws with, and the single biggest contributor to looking native |
| Colours | The game's six UI themes, read from its own `UIColor` sheet |
| Window frame | `ui/uld/WindowA_BgNormal_{Corner,H,V,HV}.tex`, drawn as a nine-slice |

The colour themes are **Dark, Light, Classic FF, Clear Blue, Clear White and
Clear Green** — named as the game names them in System Configuration (it really
is "Classic FF"), in the game's own dropdown order. Those six are exactly the
ones the `UIColor` sheet has a column for; the options screen also lists Clear
Grey and Clear Pink, but the sheet carries no colours for them.

Each palette is derived from four anchors rather than hand-written, so there is
one code path instead of six tables to keep in step:

| Anchor | Source |
|---|---|
| Text | `UIColor` row 1 — inverts correctly, white on Dark, brown on Light |
| Dimmed text | row 3 |
| Accent | row 22 |
| Ground | row 7, except Clear Blue and Clear Green |

Row 22 rather than the paler row 8 for the accent: row 8 is pure white under
Classic FF, which would sink every border and checkmark into the text. Row 22
stays distinct in all six and gives Classic FF its pale blue.

The ground is the one anchor that isn't always the sheet's. Clear Blue and
Clear Green have no blue or green entry anywhere in all 204 rows — the game
tints its window *textures* for that rather than storing a colour — so those
two get a hand-picked ground and the rest use row 7.

The frame textures are loaded whole with hand-computed UVs rather than through
`UldWrapper`'s part indices. The part tables are undocumented and shift between
patches, whereas those four files each hold exactly one kind of tile — which
the filenames make unambiguous.

All three are **on by default** — a plugin window sitting next to the game's own
windows may as well look like one — and each drops back to Dalamud's default
with a click. An explicit choice outranks the default: only a config that
predates these settings picks the game look up automatically.

The frame is the least finished of the three: it replaces the window background
but leaves ImGui's title bar and resize grip alone, so it reads as a blend
rather than a true native window. It also degrades safely — if any of the four
textures fail to load, the normal ImGui background is drawn instead of a broken
frame.

### Docking the node list (experimental)

`Node list` on the Appearance tab moves the list out of its tab and into a
panel welded to the main window's left edge. The Nodes tab disappears while
docked — two copies of the same table, one of them stale-looking, is worse than
one — and both share a single `NodeListTab` instance, so sort state can't drift
between them.

The geometry is deliberately lopsided:

| Dimension | Owner |
|---|---|
| Position | The host — recomputed every frame, so the panel follows it around |
| Height | The host — pinned by `SetNextWindowSizeConstraints` with equal min and max |
| Width | You — the grip in the bottom-left corner; the right edge stays welded |

**The resize grip is hand-drawn on the bottom-left**, and ImGui's own resizing
is switched off (`NoResize`) rather than steered. Its grip is fixed to the
bottom-right, which is the corner that *cannot* move here — the right edge is
welded to the host — and the second grip it offers on the bottom-left only
exists when the user has enabled resize-from-edges globally, which is not
something a plugin should depend on or change.

So the panel draws its own: an `InvisibleButton` in the corner, the mouse delta
subtracted from the width (dragging left grows it, because width is the
distance the left edge has travelled from a fixed right edge), and the same
triangle ImGui uses for its grips, mirrored. With nothing else writing the
size, both axes are then just set outright each frame.

The panel hangs off the host's `IsDrawing`, not its `IsOpen` — a window that is
open but collapsed has no rect to pin to, and the panel would otherwise strand
itself wherever it last saw one. Width is written to the config when the drag
ends, not during it, since each save serialises the whole file.

Font and palette are pushed from `PreDraw` and popped in `PostDraw`, not inside
`Draw` — the window background, border and title bar are drawn by `ImGui.Begin`
itself and would otherwise ignore them. `PushWindowStyle` pops any leftover
state before pushing, so a skipped `PostDraw` can't leak a few style entries per
frame until ImGui asserts.

Sort state deliberately lives in ImGui's own `.ini`, not in `Configuration`.
ImGui already persists table column sorting, and a second copy in our config
would only get a chance to disagree with the arrows in the header.

The filtering and sorting are all in the pure column — `NodeFilter.Matches`,
`NodeQuery.SortItems`, `PriorityEngine.Sort` — so the parts that decide what
you see and in what order can be exercised against the shipped dataset with no
game running. The windows only draw.

`tools/NodeGen` is in the solution so it keeps compiling against the model, but
it is a standalone exe and is never shipped. It touches only the Dalamud-free
parts of the plugin (`Models`, the pure `MapUtil.WorldToMap` overload,
`NodeDatabase.Parse`), so it runs with no game or Dalamud host attached.

## Node data

`Data/nodes.json` ships next to the DLL and is read at load time, so a data
refresh doesn't need a rebuild. See
[`Data/nodes.schema.md`](VeinAndVine/Data/nodes.schema.md) for the format.

It currently holds **1,587 nodes** — 1,050 distinct items across 47 zones, Lv1
through Lv100, in a 577 KB file. Of those, **419 are timed** (unspoiled,
legendary and ephemeral) and **1,168 are always up**.

Always-up nodes earn their place even though they never need watching: without
them the wishlist can only track the 333 timed items, so "where do I get iron
ore" has no answer. They are reported as available with no countdown and shown
as `Always`, and the `Timed only` toggle in both windows hides them when the
question is "what's up right now" instead.

**Spearfishing is excluded.** It is a gathering node in the sheets, but nothing
here models bait or the tug, and neither window offers a Fisher filter — 168
nodes that no UI could reach. One `if` in the generator, marked for when fishing
is handled.

The file is **generated, not hand-written** — run
[`tools/NodeGen`](tools/NodeGen) to rebuild it from the game's own Excel sheets:

```bash
dotnet run --project tools/NodeGen -c Release
```

Hand-maintaining it isn't viable. `territoryTypeId` matters more than it looks —
it drives both the weather lookup and the map flag, and a node with a wrong id
silently never goes active — and the spawn windows live in
`GatheringRarePopTimeTable` in a packed-decimal encoding (`160` means one hour
and sixty minutes, i.e. two hours). Reading them out of sqpack makes the dataset
reproducible and re-runnable after a patch.

The generator references the plugin project, so it emits the real `GatherNode`
type and reuses `MapUtil`'s coordinate formula. After writing, it re-reads the
file through the plugin's own `NodeDatabase.Parse` and fails if any node has a
null territory, a zero map id, out-of-range coordinates, no time window, or a
duplicate key.

## Shipping via a custom repo

`repo.json` is the third-party repository manifest. Replace the `USER`
placeholders, publish `latest.zip` as a release asset, and users add the raw
`repo.json` URL under `/xlsettings` → **Experimental** → **Custom Plugin
Repositories**.

## Releasing

The version lives in two files that nothing keeps in step: `<Version>` in the
csproj, which DalamudPackager stamps into the manifest inside `latest.zip`, and
`AssemblyVersion` in `repo.json`, which the in-game installer compares against
what the user already has. Drift between them fails *quietly* — the installer
either never offers the update, or offers one that appears not to apply.

Bumping is by hand, but a **Release build refuses to run** when the two
disagree — `VerifyRepoManifestVersion` in the csproj. Debug builds are left
alone; they're never shipped, and blocking the inner loop over a manifest isn't
worth it. So the mistake is caught before it can reach a user, at the only
moment that matters.

Release steps:

1. Move `## [Unreleased]` in [`CHANGELOG.md`](CHANGELOG.md) into a dated
   `## [x.y.z.w] - YYYY-MM-DD` section, and leave a fresh empty `[Unreleased]`.
2. Set the same version in **both** places:
   - `<Version>` in [`VeinAndVine.csproj`](VeinAndVine/VeinAndVine.csproj)
   - `"AssemblyVersion"` in [`repo.json`](repo.json)
3. `dotnet build -c Release` — the guard runs, the release notes are copied
   into both manifests (see below), and you get
   `VeinAndVine\bin\x64\Release\VeinAndVine\latest.zip`.
4. Commit — including the two manifests, which the build will have updated —
   then `git tag -a vX -m "..."`, push both, and attach `latest.zip` to the
   GitHub release so `repo.json`'s download links resolve.

Note that **version bumps belong to releases, not commits.** Dalamud only
compares `AssemblyVersion` to decide whether a user sees an update, so bumping
per commit burns numbers on work nobody can install.

## Changelog

[`CHANGELOG.md`](CHANGELOG.md) is the index of what changed between versions,
in [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) format. Write
entries into `## [Unreleased]` as you go rather than reconstructing them from a
diff at release time — they're far more accurate written next to the change.

**It reaches the in-game installer on its own.** Every build, the
`SyncChangelogToManifests` target reads the `## [<current version>]` section
out of `CHANGELOG.md` and writes it into the `"Changelog"` field of both
manifests:

| Manifest | What reads it |
|---|---|
| [`VeinAndVine.json`](VeinAndVine/VeinAndVine.json) | copied into `latest.zip`; the installed plugin's own notes |
| [`repo.json`](repo.json) | the installer, deciding an update exists — the copy a user sees *before* updating |

Markdown is flattened on the way, because none of it renders there: `### Added`
becomes `Added:`, hard-wrapped bullets are rejoined so they don't wrap twice in
a narrow panel, and emphasis and code spans are stripped.

Three things keep this from being annoying. It only rewrites a file when the
text actually differs, so it's inert once the changelog settles. It **skips a
manifest that declares a different version** rather than filling it with the
wrong release's notes — Debug builds don't run the version guard, so without
that a mismatch would be papered over silently. And a missing section is a
warning, not an error, so bumping `<Version>` before writing the notes doesn't
block the build.

Worth knowing: DalamudPackager does expose a `Changelog` MSBuild property, but
it's only a fallback for projects with no manifest file. When
`VeinAndVine.json` exists it wins outright and the properties are ignored, so
the field has to be correct in the file before the packager reads it. Hence
writing the manifests rather than passing a property.

## Still stubbed

1. **Weather forecasting in the UI** — `WeatherService.FindNextWeatherWindow`
   exists and works, but `PriorityEngine` doesn't call it yet, so
   weather-gated nodes show "Needs Clear Skies" without a countdown. Wiring it
   means letting the engine take an optional forecaster. Note that nothing in
   the generated dataset is weather-gated today, so this is currently dead
   weight rather than a visible gap.
2. **Map-coordinate conversion is unverified** — `MapUtil.WorldToMap` uses the
   standard scale/offset formula. Node coordinates now come from the game's own
   `ExportedGatheringPoint` sheet through that same function, so node and player
   coordinates are at least consistent with each other and land in range, but
   the formula itself still hasn't been checked against a live readout.
   Sanity-check a distance in-game before trusting it.
3. **Fishing** — `NodeType.Fishing` exists in the model but fishing spots have
   different mechanics (bait, tug) that nothing handles yet. The generator maps
   spearfishing types onto it; no such node survives the current filters.
4. **Folklore and levequest gating is not modelled** — some nodes in the dataset
   additionally require a folklore tome. They'll show as available when the
   clock says so, whether or not you've read the book.

## API notes

- `Service.ObjectTable.LocalPlayer` — moved off `IClientState` in API 15.
- `Service.ObjectTable.EventObjects` — where gathering nodes appear in the live
  object table, if you later want proximity detection.
- ImGui is `Dalamud.Bindings.ImGui`, not `ImGuiNET`.
- `TerritoryType.WeatherRate` is a `RowRef<WeatherRate>`, so it navigates
  directly; `WeatherRate` holds parallel `Weather` / `Rate` collections forming a
  cumulative distribution.
