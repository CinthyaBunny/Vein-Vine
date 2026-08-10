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

`PriorityEngine` depends on `IWeatherProvider`, not `WeatherService`, specifically
so it can be tested against a fake clock.

## Node data

`Data/nodes.json` ships next to the DLL and is read at load time, so a data
refresh doesn't need a rebuild. It's currently `[]` — see
[`Data/nodes.schema.md`](VeinAndVine/Data/nodes.schema.md) for the format.

`territoryTypeId` matters more than it looks: it drives both the weather lookup
and the map flag. A node with a wrong id silently never goes active.

## Shipping via a custom repo

`repo.json` is the third-party repository manifest. Replace the `USER`
placeholders, publish `latest.zip` as a release asset, and users add the raw
`repo.json` URL under `/xlsettings` → **Experimental** → **Custom Plugin
Repositories**.

Keep `<Version>` in the csproj, `AssemblyVersion` in `repo.json`, and the release
tag in lockstep.

## Still stubbed

1. **Node dataset** — the loader, schema, and reload button are done; the data
   itself is empty. Source from a public dataset with attribution.
2. **Weather forecasting in the UI** — `WeatherService.FindNextWeatherWindow`
   exists and works, but `PriorityEngine` doesn't call it yet, so
   weather-gated nodes show "Needs Clear Skies" without a countdown. Wiring it
   means letting the engine take an optional forecaster.
3. **Map-coordinate conversion is unverified** — `MapUtil.WorldToMap` uses the
   standard scale/offset formula but has not been checked against live values.
   Sanity-check the distance readout in-game before trusting it.
4. **Fishing** — `NodeType.Fishing` exists in the model but fishing spots have
   different mechanics (bait, tug) that nothing handles yet.

## API notes

- `Service.ObjectTable.LocalPlayer` — moved off `IClientState` in API 15.
- `Service.ObjectTable.EventObjects` — where gathering nodes appear in the live
  object table, if you later want proximity detection.
- ImGui is `Dalamud.Bindings.ImGui`, not `ImGuiNET`.
- `TerritoryType.WeatherRate` is a `RowRef<WeatherRate>`, so it navigates
  directly; `WeatherRate` holds parallel `Weather` / `Rate` collections forming a
  cumulative distribution.
