# Vein and Vine - starting scaffold

A read-only gathering companion for FFXIV. Shows a priority-sorted list of
active and upcoming nodes from your wishlist. Never moves the player,
never gathers automatically - the only game-state-changing action is an
optional native map flag, which the player still has to walk to.

## What's here

- `Models/GatherNode.cs` - static node data shape (location, timing, weather)
- `Models/WishlistEntry.cs` - the player's tracked items
- `Services/WeatherService.cs` - Eorzea time is implemented; the real
  per-zone weather calculation is a stub, see the TODO in that file
- `Services/PriorityEngine.cs` - sorts active/upcoming nodes by urgency and
  distance, fully unit-testable with no Dalamud dependency
- `Windows/MainWindow.cs` - ImGui panel rendering the sorted list
- `Plugin.cs` - wiring: services, config, command, window lifecycle
- `Configuration.cs` - persisted wishlist and settings

## What's stubbed and needs real implementation

1. **Weather calculation** - `WeatherService.GetCurrentWeather` always
   returns "Clear Skies". Needs the real per-zone weather rate tables and
   seeded calculation.
2. **Node database** - `Plugin.LoadNodeDatabase` returns an empty list.
   Needs a bundled JSON dataset of real node locations, sourced from a
   public gathering dataset with attribution.
3. **Map flag button** - the button exists in the UI but doesn't call
   anything yet. Needs Dalamud's native map-marker facility wired in.
4. **Wishlist editing UI** - there's no settings window yet to add/remove
   items from the wishlist; only the display panel exists.

## Getting it running

1. Open this folder in Claude Code (or Visual Studio).
2. `dotnet build` - it will fail until Dalamud.NET.Sdk can resolve against
   your local Dalamud install; point `DALAMUD_HOME` at your XIVLauncher
   dev install if needed.
3. Use Dalamud's dev plugin locations (`/xlsettings` -> Experimental) to
   point at this build output for hot reload.
4. `/veinvine` in-game toggles the window once loaded.
