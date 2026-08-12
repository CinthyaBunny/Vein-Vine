# `nodes.json` schema

An array of node objects. `Data/nodes.json` ships next to the plugin DLL and is
read at load time (and via **Reload data** in the UI), so refreshing the dataset
does not need a rebuild.

**This file is generated.** Don't hand-edit it — run
[`tools/NodeGen`](../../tools/NodeGen) instead, which reads the game's own Excel
sheets. See [Regenerating](#regenerating) below.

```jsonc
[
  {
    // Required
    "itemId": 12538,               // uint - Item sheet row id
    "itemName": "Adamantite Ore",  // string - display name
    "type": "Mining",              // "Mining" | "Botany" | "Fishing"
    "zoneName": "Azys Lla",        // string - display name only
    "territoryTypeId": 402,        // uint - TerritoryType row id; drives weather AND the map flag
    "mapId": 216,                  // uint - Map row id; needed for the map flag
    "mapX": 23.8,                  // float - map coordinate, as shown in a chat map link
    "mapY": 5.9,                   // float
    "jobLevelRequired": 60,        // int

    // Optional
    "method": "Quarrying",         // "Mining" | "Quarrying" | "Logging" | "Harvesting" | "Spearfishing"
    "iconId": 26586,               // uint - Item sheet icon; 0 = none
    "perceptionRequired": 4000,    // int  - for the full yield; 0 = none
    "stars": 3,                    // int  - node star rating, 0-4
    "gatheringPointBaseId": 425,   // uint - provenance; see below
    "timeWindows": [               // omit or [] for an always-up node
      { "startHour": 0,  "endHour": 2  },
      { "startHour": 12, "endHour": 14 }
    ],
    "requiredWeather": ["Clear Skies", "Fair Skies"],  // omit or [] for no weather gate
    "spawnDurationMinutes": 6      // 0 = never expires; defaults to 60
  }
]
```

## Notes

- **`territoryTypeId` is not optional in practice.** Weather is resolved per
  territory, and the map flag needs it. A node with a wrong id will silently
  never appear as active. TerritoryType rows `0` and `1` are both null rows —
  the generator drops nodes that land on them.
- **`timeWindows` is a list, not a single window.** Most timed nodes in the game
  spawn two or three times per Eorzea day. A node is up if *any* of its windows
  contains the current Eorzea hour; the countdown uses whichever window is open,
  and "opens at" uses whichever comes round next.
- **An empty `timeWindows` means the node is always up**, and that is the common
  case — roughly three quarters of the dataset. Such a node is reported as
  available with no countdown, and the UI shows "Always" rather than inventing a
  deadline. The `Timed only` filter in both windows hides them.
- `startHour` is inclusive, `endHour` exclusive, and a window may wrap past
  midnight: `{"startHour": 22, "endHour": 4}` is handled correctly.
- `gatheringPointBaseId` is the GatheringPointBase row the node came from. It
  isn't used for any game lookup — it exists so a row can be traced back to the
  sheet, and because `(gatheringPointBaseId, itemId)` is the only unique key: the
  same item appears on several nodes, and one node yields several items. The UI
  keys its rows on that pair.
- **`method` is the finer split inside `type`**, straight off the game's
  `GatheringType` sheet: a miner mines and quarries, a botanist logs and
  harvests. `type` still decides which job can work the node and is what the
  main window filters on; `method` only ever narrows within it. Omitted in a
  dataset written before this field existed, in which case every node reads as
  `Mining` — the generator's verify pass rejects that.
- **`method` is a property of the node, not of the item.** An item is whatever
  its nodes make it, and plenty of items have nodes of both kinds within a job:
  16 per job, plus the 98 that span both jobs outright. The picker builds its
  per-item summary from the nodes in the scope it is showing, so such an item
  appears under both sub-tabs, each time describing only the zones, level and
  windows that sub-tab can actually get it from.
- **`iconId` is baked in, the item's description is not.** The icon id is two
  bytes and is needed for every visible row, so keeping it here means the list
  draws without a sheet read per row. Descriptions are long and localised, so
  they're read live from the `Item` sheet for the one row under the cursor. Both
  come out of the game client; nothing is fetched over the network.
- `stars` reaches **4**, not 3 — only on the level 90 collectable nodes, of
  which there are six. `perceptionRequired` is 0 on most nodes and only appears
  alongside a star rating.
- `requiredWeather` strings are matched against the Weather sheet's display
  name, so they must match the in-game spelling exactly (`"Clear Skies"`, not
  `"clear"`). Gathering nodes are time-gated rather than weather-gated, so the
  generated dataset leaves this empty everywhere; the field stays because the
  engine supports it.
- `spawnDurationMinutes` is **real** minutes, and only caps the countdown — the
  windows are what actually decide availability. The generator sets it to the
  node's longest window rounded up (a 3 Eorzea-hour window is 8m45s real, so 9),
  and to **0 for an always-up node**, meaning "no cap". 0 and an empty
  `timeWindows` have to agree: the generator rejects a node that has one without
  the other, because either combination would make the plugin count down to an
  expiry that never arrives.
- Comments and trailing commas are allowed — the loader enables both.
- A missing or malformed file logs a warning and yields an empty database. It
  will not stop the plugin from loading.

## Regenerating

```bash
dotnet run --project tools/NodeGen -c Release
```

It finds the game via XIVLauncher's `launcherConfigV3.json` and overwrites
`Gleaner/Data/nodes.json`. Override either end:

```bash
dotnet run --project tools/NodeGen -c Release -- "<path>\game\sqpack" out.json
```

The generator emits every miner and botanist node it can fully resolve, timed or
not. **Spearfishing is skipped**: it is a gathering node in the sheets, but it
needs bait and a tug that nothing here models, and neither window offers a
Fisher filter — so emitting it would ship rows no UI can reach. The skip is one
`if` in `Build`, marked for when fishing is handled. Sources:

| Sheet | Supplies |
|---|---|
| `GatheringPoint` | territory, and the link to the base |
| `GatheringPointBase` | gathering type, level, item list |
| `GatheringPointTransient` | which timing mechanism the node uses |
| `GatheringRarePopTimeTable` | unspoiled/legendary windows (up to several per day) |
| `ExportedGatheringPoint` | world X/Z, converted via `MapUtil.WorldToMap` |
| `GatheringItem` → `Item` | item id and name |
| `TerritoryType` → `Map` | map id, scale and offset, zone name |

After writing, it re-reads the file through the plugin's own
`NodeDatabase.Parse` and fails the run if any node has a null territory, a zero
map id, out-of-range coordinates, a spawn duration that disagrees with its
window list, or a duplicate key. Ordering is stable (zone, level, item name,
base id) so a post-patch regeneration produces a readable diff.

Because the great majority of `GatheringPoint` rows are placeholders whose refs
point at nothing, every row dereference in `Build` goes through `ValueNullable`
rather than `Value`. The timed-node filter used to hide those rows; without it,
`Value` throws.
