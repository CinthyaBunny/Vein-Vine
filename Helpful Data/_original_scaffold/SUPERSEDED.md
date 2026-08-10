# Superseded — reference only

This is the original scaffold. **It is not the live project** and is not part of
the solution. The live code is at `C:\AGB_C3\VeinAndVine\`.

Do not build this folder. Its `VeinAndVine.csproj` targets
`Dalamud.NET.Sdk/12.0.2` / `net9.0-windows` / `DalamudApiLevel 12`, which cannot
load on Dalamud 15 (API 15). An IDE opening it will show restore errors.

## What carried over

All of it, adapted to API 15 — models, `PriorityEngine`, `WeatherService`,
`MainWindow`, `Configuration`, and the plugin wiring.

## What changed on the way

- **Eorzea time constant** — `20.5716` was a rounded value that drifts an
  in-game half-day against a modern timestamp. Now `3600.0 / 175.0` exactly.
- **`TimeWindow`** — was `(int, int)?`; ValueTuple exposes fields, not
  properties, so System.Text.Json silently serialized it as `{}`. Now the
  `EorzeaHourWindow` record struct.
- **Distance** — compared world-space player position against map coordinates.
  Now converted via `Services/MapUtil.cs`.
- **Weather lookup** — keyed on `TerritoryTypeId` rather than zone name, since
  the weather rate table is per territory.
- **`ImGuiNET`** — replaced by `Dalamud.Bindings.ImGui`.
- **`GatherNode`** — gained `TerritoryTypeId` and `MapId`, both required for
  the map flag and the weather lookup.
- **`PriorityEngine`** — now depends on the `IWeatherProvider` interface, so it
  stays testable without Dalamud.
