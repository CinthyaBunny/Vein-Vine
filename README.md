# Vein & Vine

A read-only gathering companion for FFXIV. Shows a priority-sorted list of active
and upcoming nodes from your wishlist. Never moves the player, never gathers
automatically — the only game-state-changing action is a native map flag, which
the player still has to walk to.

Internal name is `VeinAndVine`; the display name `Vein & Vine` appears only in the
manifest `Name` field and UI strings.

Design rationale — architecture, the theming work, the picker's overlapping
counts — lives in [`docs/design.md`](docs/design.md).

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

**x64 only.** Do not add `Any CPU` or `x86` configurations to the solution: both
projects build to `bin\x64\`, and an AnyCPU mapping makes the plugin build a
second time into `bin\`, producing a second `latest.zip`.

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

## Commands

| Command | Effect |
|---|---|
| `/veinvine` | Toggle the window, on whichever tab you left it |
| `/vnv` | Alias, hidden from the command list |
| `/veinvine cfg` | Open it on the **Display** tab |

## The UI

One window, four tabs.

**Nodes** — one row per node, coloured by how urgent it is: green for up and
expiring, amber for up within five minutes, grey for waiting, ordinary text for
a node that is simply always there. Nodes with no window are grouped below the
timed ones under an `Always available` band, so they can't sit between the node
expiring now and the one worth travelling for. Above the table: `Miner` / `Botanist`,
`Timed only`, `Upcoming` and `This zone`, all persisted. Double-click a row to
set the map flag; so does the marker button at its right end, and right-click
also offers *Stop tracking*.

**Wishlist** — pick the items to track, one row per item rather than per node.
Click anywhere on a row to track or untrack it; tracked rows stay highlighted.
Split across `All / Miner / Botanist`, each single-job tab carrying a second
strip for the finer split (`Mining / Quarrying`, `Logging / Harvesting`). Above
them: search, zone, a level range, timed-only and tracked-only, plus `Track all
shown` for everything the filters left.

Every tab is labelled with the number of rows you get for clicking it. Miner and
Botanist deliberately add up to more than All, because an item both jobs can
gather is counted on each — hover any tab for the arithmetic.

**Display** and **Appearance** — the toggles above the node list, and the game
font, colour theme and node-list placement.

Both lists are ImGui tables with sortable, resizable, hideable columns, and show
each item's game icon with its in-game description on hover. Nothing is fetched
over the network; it all comes from the client you already have.

See [`docs/design.md`](docs/design.md) for why any of this is the way it is.

## Node data

`Data/nodes.json` ships next to the DLL and is read at load time, so a data
refresh doesn't need a rebuild. See
[`Data/nodes.schema.md`](VeinAndVine/Data/nodes.schema.md) for the format.

It currently holds **1,587 nodes** — 1,050 distinct items across 47 zones, Lv1
through Lv100, in a 577 KB file. Of those, **419 are timed** (unspoiled,
legendary and ephemeral) and **1,168 are always up**. Spearfishing is excluded.

The file is generated, not hand-written — run
[`tools/NodeGen`](tools/NodeGen) to rebuild it from the game's own Excel sheets:

```bash
dotnet run --project tools/NodeGen -c Release
```

It verifies its own output by re-reading the file through the plugin's parser,
and fails rather than shipping a node that would silently never appear.

## Shipping via a custom repo

`repo.json` is the third-party repository manifest. Publish `latest.zip` as a
release asset, and users add the raw `repo.json` URL under `/xlsettings` →
**Experimental** → **Custom Plugin Repositories**.

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

1. Add an entry at the top of [`CHANGELOG.md`](CHANGELOG.md), outside any
   `<details>` wrapper so GitHub renders it open, and wrap the previous newest
   entry in one.
2. Set the same version in **both** places:
   - `<Version>` in [`VeinAndVine.csproj`](VeinAndVine/VeinAndVine.csproj)
   - `"AssemblyVersion"` in [`repo.json`](repo.json)
3. `dotnet build -c Release` — the guard runs, the release notes are copied
   into both manifests, and you get
   `VeinAndVine\bin\x64\Release\VeinAndVine\latest.zip`.
4. Commit — including the two manifests, which the build will have updated —
   then `git tag -a vX -m "..."`, push both, and attach `latest.zip` to the
   GitHub release so `repo.json`'s download links resolve.

Note that **version bumps belong to releases, not commits.** Dalamud only
compares `AssemblyVersion` to decide whether a user sees an update, so bumping
per commit burns numbers on work nobody can install.

## Changelog

[`CHANGELOG.md`](CHANGELOG.md) is the index of what changed between versions.
Entries are written for players rather than developers — see the rules in
[`CLAUDE.md`](CLAUDE.md) before adding one.

**It reaches the in-game installer on its own.** Every build, the
`SyncChangelogToManifests` target finds the entry whose `<strong>` marker names
the version being built and writes it into the `"Changelog"` field of both
manifests:

| Manifest | What reads it |
|---|---|
| [`VeinAndVine.json`](VeinAndVine/VeinAndVine.json) | copied into `latest.zip`; the installed plugin's own notes |
| [`repo.json`](repo.json) | the installer, deciding an update exists — the copy a user sees *before* updating |

Markdown and HTML are flattened on the way, because none of it renders there:
`### Added` becomes `Added:`, hard-wrapped bullets are rejoined so they don't
wrap twice in a narrow panel, and emphasis, code spans and tags are stripped.

Three things keep this from being annoying. It only rewrites a file when the
text actually differs, so it's inert once the changelog settles. It **skips a
manifest that declares a different version** rather than filling it with the
wrong release's notes — Debug builds don't run the version guard, so without
that a mismatch would be papered over silently. And a missing entry is a
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
5. **No plugin icon.** `repo.json`'s `IconUrl` points at
   `VeinAndVine/images/icon.png`, which does not exist yet; the csproj already
   includes `images/**` when present, so dropping a PNG there is all it needs.
