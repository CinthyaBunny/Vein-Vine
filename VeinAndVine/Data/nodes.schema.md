# `nodes.json` schema

An array of node objects. `Data/nodes.json` ships next to the plugin DLL and is
read at load time (and via **Reload data** in the UI), so refreshing the dataset
does not need a rebuild.

```jsonc
[
  {
    // Required
    "itemId": 12345,               // uint - Item sheet row id
    "itemName": "Example Ore",     // string - display name
    "type": "Mining",              // "Mining" | "Botany" | "Fishing"
    "zoneName": "Example Zone",    // string - display name only
    "territoryTypeId": 397,        // uint - TerritoryType row id; drives weather AND the map flag
    "mapId": 200,                  // uint - Map row id; needed for the map flag
    "mapX": 21.5,                  // float - map coordinate, as shown in a chat map link
    "mapY": 27.1,                  // float
    "jobLevelRequired": 70,        // int

    // Optional
    "timeWindow": {                // omit for no time restriction
      "startHour": 0,              // Eorzea hour, inclusive
      "endHour": 8                 // Eorzea hour, exclusive; may wrap past midnight
    },
    "requiredWeather": ["Clear Skies", "Fair Skies"],  // omit or [] for no weather gate
    "spawnDurationMinutes": 60     // defaults to 60
  }
]
```

## Notes

- **`territoryTypeId` is not optional in practice.** Weather is resolved per
  territory, and the map flag needs it. A node with a wrong id will silently
  never appear as active.
- `requiredWeather` strings are matched against the Weather sheet's display
  name, so they must match the in-game spelling exactly (`"Clear Skies"`, not
  `"clear"`).
- `timeWindow` uses Eorzea hours. `{"startHour": 22, "endHour": 4}` wraps past
  midnight and is handled correctly.
- Comments and trailing commas are allowed — the loader enables both.
- A missing or malformed file logs a warning and yields an empty database. It
  will not stop the plugin from loading.

## Sourcing the data

Public gathering datasets (Teamcraft, Garland Tools) expose all of these
fields. Credit whichever source you use, and check its license before
redistributing the data inside the plugin.
