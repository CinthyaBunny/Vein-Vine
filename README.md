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
| `/veinvine` | Toggle the main window |
| `/vnv` | Alias, hidden from the command list |
| `/veinvine cfg` | Toggle settings |

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
| `Services/PriorityEngine.cs` | **pure** | Sorting, gated behind `IWeatherProvider` |
| `Models/` | **pure** | `GatherNode`, `WishlistEntry` |
| `Windows/` | ImGui | Node list, wishlist editor |
| `tools/NodeGen/` | Lumina, build-time | Regenerates `Data/nodes.json` from game sheets |

`PriorityEngine` depends on `IWeatherProvider`, not `WeatherService`, specifically
so it can be tested against a fake clock.

`tools/NodeGen` is in the solution so it keeps compiling against the model, but
it is a standalone exe and is never shipped. It touches only the Dalamud-free
parts of the plugin (`Models`, the pure `MapUtil.WorldToMap` overload,
`NodeDatabase.Parse`), so it runs with no game or Dalamud host attached.

## Node data

`Data/nodes.json` ships next to the DLL and is read at load time, so a data
refresh doesn't need a rebuild. See
[`Data/nodes.schema.md`](VeinAndVine/Data/nodes.schema.md) for the format.

It currently holds **419 timed nodes** — 333 distinct items across 46 zones,
Lv50 through Lv100. Only timed nodes are included: the plugin exists to say when
something is up, and an always-available node has nothing to report.

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

Keep `<Version>` in the csproj, `AssemblyVersion` in `repo.json`, and the release
tag in lockstep.

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
