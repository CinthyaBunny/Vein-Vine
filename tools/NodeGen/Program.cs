using System.Text.Json;
using System.Text.Json.Serialization;
using Lumina;
using Lumina.Excel.Sheets;
using VeinAndVine.Models;
using VeinAndVine.Services;

namespace NodeGen;

/// <summary>
/// Regenerates <c>VeinAndVine/Data/nodes.json</c> from the game's own Excel
/// sheets.
///
/// Hand-maintaining this dataset is not viable: territoryTypeId and mapId have
/// to be exactly right or the node silently never goes active, and the spawn
/// windows live in GatheringRarePopTimeTable in an encoding nobody remembers.
/// Reading them straight out of sqpack makes the whole file reproducible, and
/// re-runnable after a patch.
///
/// Usage:
///   dotnet run --project tools/NodeGen [-- &lt;sqpack path&gt; [output path]]
///
/// With no arguments it reads the game path from XIVLauncher's config and
/// writes to VeinAndVine/Data/nodes.json.
/// </summary>
internal static class Program
{
    /// <summary>Sentinel the sheets use for "no value" in the time columns.</summary>
    private const ushort NoValue = 65535;

    private static int Main(string[] args)
    {
        try
        {
            var sqpack = args.Length > 0 ? args[0] : LocateSqPack();
            var output = args.Length > 1 ? args[1] : LocateOutput();

            Console.WriteLine($"sqpack : {sqpack}");
            Console.WriteLine($"output : {output}");
            Console.WriteLine();

            var nodes = Build(new GameData(sqpack));
            Write(nodes, output);
            return Verify(output, nodes.Count) ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static List<GatherNode> Build(GameData gd)
    {
        var points = Sheet<GatheringPoint>(gd);
        var transients = Sheet<GatheringPointTransient>(gd);
        var rarePops = Sheet<GatheringRarePopTimeTable>(gd);
        var exported = Sheet<ExportedGatheringPoint>(gd);
        var gatheringItems = Sheet<GatheringItem>(gd);
        var items = Sheet<Item>(gd);

        // (GatheringPointBase, Item) is the unique key. Several GatheringPoint
        // rows can share one base, and one base yields several items.
        var seen = new HashSet<(uint Base, uint Item)>();
        var nodes = new List<GatherNode>();
        var skippedNoCoords = 0;
        var skippedNoItems = 0;
        var skippedNoTerritory = 0;
        var skippedNoBase = 0;
        var skippedFishing = 0;
        var timedCount = 0;

        foreach (var point in points)
        {
            // A missing transient row just means the node has no timing
            // mechanism, which is the normal case for the great majority of
            // nodes - it is not a reason to drop it.
            var windows = transients.TryGetRow(point.RowId, out var transient)
                ? ReadWindows(transient, rarePops)
                : [];

            // Every row ref below is dereferenced defensively. Dropping the
            // timed-node filter means walking rows that were previously never
            // reached, and a fair number of them are placeholders whose refs
            // point at nothing - Value would throw rather than yield a null row.
            if (point.GatheringPointBase.ValueNullable is not { } pointBase)
            {
                skippedNoBase++;
                continue;
            }

            var type = ToNodeType(pointBase.GatheringType.RowId);
            var method = ToMethod(pointBase.GatheringType.RowId);

            // Spearfishing is a gathering node in the sheets, but it needs bait
            // and a tug the plugin models nothing of, and neither window
            // exposes a Fisher filter - emitting it would ship rows no UI can
            // reach. Flip this to include them once fishing is handled.
            if (type == NodeType.Fishing)
            {
                skippedFishing++;
                continue;
            }

            // TerritoryType 0 and 1 are both null rows: no map, no place name.
            // A node without a territory can do neither the weather lookup nor
            // the map flag, so it would sit in the list permanently inactive.
            // Six legacy bases (the old Diadem nodes) only ever appear here.
            if (point.TerritoryType.ValueNullable is not { } territory ||
                territory.Map.ValueNullable is not { } map ||
                territory.PlaceName.ValueNullable is not { } placeName)
            {
                skippedNoTerritory++;
                continue;
            }

            var zoneName = placeName.Name.ExtractText();
            if (map.RowId == 0 || zoneName.Length == 0)
            {
                skippedNoTerritory++;
                continue;
            }

            if (!exported.TryGetRow(pointBase.RowId, out var coords))
            {
                skippedNoCoords++;
                continue;
            }

            // ExportedGatheringPoint.Y is the world Z axis, which is what the
            // map's Y coordinate is derived from - hence OffsetY for it.
            var mapX = MapUtil.WorldToMap(coords.X, map.OffsetX, map.SizeFactor);
            var mapY = MapUtil.WorldToMap(coords.Y, map.OffsetY, map.SizeFactor);

            var resolvable = 0;

            foreach (var gatheringItemRef in pointBase.Item)
            {
                if (gatheringItemRef.RowId == 0)
                    continue;
                if (!gatheringItems.TryGetRow(gatheringItemRef.RowId, out var gatheringItem))
                    continue;
                if (!items.TryGetRow(gatheringItem.Item.RowId, out var item))
                    continue;

                var itemName = item.Name.ExtractText();
                if (string.IsNullOrWhiteSpace(itemName))
                    continue;

                resolvable++;

                // Several GatheringPoint rows can point at one base - the same
                // physical node placed in more than one territory row. Emitting
                // the first is intentional; this is deduplication, not a skip.
                if (!seen.Add((pointBase.RowId, item.RowId)))
                    continue;

                if (windows.Count > 0)
                    timedCount++;

                // Stars live one hop away, on the convert table the gathering
                // item points at. Missing is normal for unrestricted items.
                var levelRow = gatheringItem.GatheringItemLevel.ValueNullable;

                nodes.Add(new GatherNode
                {
                    ItemId = item.RowId,
                    ItemName = itemName,
                    Type = type,
                    Method = method,
                    ZoneName = zoneName,
                    GatheringPointBaseId = pointBase.RowId,
                    TerritoryTypeId = territory.RowId,
                    MapId = map.RowId,
                    MapX = Round(mapX),
                    MapY = Round(mapY),
                    JobLevelRequired = pointBase.GatheringLevel,
                    IconId = item.Icon,
                    PerceptionRequired = gatheringItem.PerceptionReq,
                    Stars = levelRow.HasValue ? levelRow.Value.Stars : 0,
                    TimeWindows = windows,
                    RequiredWeather = [],
                    SpawnDurationMinutes = LongestWindowRealMinutes(windows),
                });
            }

            if (resolvable == 0)
                skippedNoItems++;
        }

        Console.WriteLine($"emitted              : {nodes.Count} nodes ({timedCount} timed, {nodes.Count - timedCount} always up)");
        Console.WriteLine($"skipped, spearfishing: {skippedFishing}");
        Console.WriteLine($"skipped, no base     : {skippedNoBase}");
        Console.WriteLine($"skipped, no territory: {skippedNoTerritory}");
        Console.WriteLine($"skipped, no coords   : {skippedNoCoords}");
        Console.WriteLine($"skipped, no items    : {skippedNoItems}");

        // Stable ordering so regenerating after a patch produces a readable diff.
        return nodes
            .OrderBy(n => n.ZoneName, StringComparer.Ordinal)
            .ThenBy(n => n.JobLevelRequired)
            .ThenBy(n => n.ItemName, StringComparer.Ordinal)
            .ThenBy(n => n.GatheringPointBaseId)
            .ToList();
    }

    /// <summary>
    /// Spawn windows for a node, from whichever of the two mechanisms it uses.
    ///
    /// Unspoiled/legendary nodes use GatheringRarePopTimeTable, which holds up
    /// to several start times per day. Ephemeral nodes instead use a single
    /// EphemeralStartTime/EndTime pair. No node in the sheets uses both.
    /// </summary>
    private static List<EorzeaHourWindow> ReadWindows(
        GatheringPointTransient transient,
        Lumina.Excel.ExcelSheet<GatheringRarePopTimeTable> rarePops)
    {
        var windows = new List<EorzeaHourWindow>();

        if (transient.GatheringRarePopTimeTable.RowId != 0 &&
            rarePops.TryGetRow(transient.GatheringRarePopTimeTable.RowId, out var table))
        {
            for (var i = 0; i < table.StartTime.Count; i++)
            {
                var start = table.StartTime[i];
                if (start == NoValue)
                    continue;

                var durationHours = ToHours(table.Duration[i]);
                if (durationHours <= 0)
                    continue;

                var startHour = ToHours(start);
                windows.Add(new EorzeaHourWindow(startHour, (startHour + durationHours) % 24));
            }
        }
        else if (transient.EphemeralStartTime != NoValue &&
                 transient.EphemeralStartTime != transient.EphemeralEndTime)
        {
            windows.Add(new EorzeaHourWindow(
                ToHours(transient.EphemeralStartTime),
                ToHours(transient.EphemeralEndTime)));
        }

        return windows;
    }

    /// <summary>
    /// The time columns are packed decimal HHMM: 900 is 09:00 and 160 is one
    /// hour and sixty minutes, i.e. two hours. Every value in the current
    /// sheets lands on a whole hour once decoded.
    /// </summary>
    private static int ToHours(ushort packed)
    {
        var minutes = packed / 100 * 60 + packed % 100;
        return minutes / 60 % 24;
    }

    /// <summary>
    /// The node's longest window expressed in real minutes, rounded up, or 0
    /// for a node that never closes.
    ///
    /// PriorityEngine caps its "time remaining" readout at this, so rounding
    /// up matters: rounding down would clip the last few seconds off a window
    /// that is genuinely still open. 0 means "no cap" - an always-up node has
    /// no expiry to count down to, and any positive number here would invent
    /// one.
    /// </summary>
    private static int LongestWindowRealMinutes(List<EorzeaHourWindow> windows)
    {
        if (windows.Count == 0)
            return 0;

        var longest = windows.Max(w => (w.EndHour - w.StartHour + 24) % 24);
        var realSeconds = longest * EorzeaTime.RealSecondsPerEorzeaHour;
        return (int)Math.Ceiling(realSeconds / 60.0);
    }

    private static NodeType ToNodeType(uint gatheringType) => gatheringType switch
    {
        0 or 1 => NodeType.Mining,    // Mining, Quarrying
        2 or 3 => NodeType.Botany,    // Logging, Harvesting
        _ => NodeType.Fishing,        // Spearfishing
    };

    /// <summary>
    /// The GatheringType row, kept as-is rather than folded into the job. The
    /// ids and names come straight from the sheet: 0 Mining, 1 Quarrying,
    /// 2 Logging, 3 Harvesting, and 4/5 both spearfishing.
    /// </summary>
    private static GatheringMethod ToMethod(uint gatheringType) => gatheringType switch
    {
        0 => GatheringMethod.Mining,
        1 => GatheringMethod.Quarrying,
        2 => GatheringMethod.Logging,
        3 => GatheringMethod.Harvesting,
        _ => GatheringMethod.Spearfishing,
    };

    /// <summary>Map coordinates are shown to one decimal; store them that way.</summary>
    private static float Round(float value) => MathF.Round(value, 1);

    private static Lumina.Excel.ExcelSheet<T> Sheet<T>(GameData gd) where T : struct, Lumina.Excel.IExcelRow<T> =>
        gd.GetExcelSheet<T>() ?? throw new InvalidOperationException($"Sheet {typeof(T).Name} is missing from the game data.");

    private static void Write(List<GatherNode> nodes, string output)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
            // LF, not Environment.NewLine. .gitattributes normalises the repo
            // to LF, so emitting CRLF here would make every regeneration after
            // a fresh checkout show up as a whole-file diff.
            NewLine = "\n",
        };

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(nodes, options) + "\n");

        Console.WriteLine();
        Console.WriteLine($"wrote {output} ({new FileInfo(output).Length / 1024} KB)");
    }

    /// <summary>
    /// Reads the file back through the plugin's own parser and checks the
    /// invariants the plugin relies on but cannot enforce.
    ///
    /// Worth the extra pass: every one of these failures produces a node that
    /// loads fine and then silently never appears, which is the single most
    /// annoying way for this dataset to be wrong.
    /// </summary>
    private static bool Verify(string path, int expectedCount)
    {
        using var stream = File.OpenRead(path);
        var nodes = NodeDatabase.Parse(stream);

        var problems = new List<string>();

        if (nodes.Count != expectedCount)
            problems.Add($"round-trip lost nodes: wrote {expectedCount}, read back {nodes.Count}");

        foreach (var n in nodes)
        {
            var id = $"{n.ItemName} (base {n.GatheringPointBaseId})";

            if (n.TerritoryTypeId is 0 or 1)
                problems.Add($"{id}: null territory {n.TerritoryTypeId} - weather and map flag would both fail");
            if (n.MapId == 0)
                problems.Add($"{id}: mapId 0 - map flag would fail");
            if (string.IsNullOrWhiteSpace(n.ZoneName))
                problems.Add($"{id}: empty zone name");
            if (n.ItemId == 0)
                problems.Add($"{id}: itemId 0");

            // Map coordinates run 1..42 on a standard-scale map; anything
            // outside that means the scale/offset conversion went wrong.
            if (n.MapX is < 1 or > 42 || n.MapY is < 1 or > 42)
                problems.Add($"{id}: implausible map coords ({n.MapX}, {n.MapY})");

            // No time windows is legitimate - that's an always-up node - but it
            // must not also carry a spawn duration, or the plugin would count
            // down to an expiry that never comes.
            if (n.TimeWindows.Count == 0 && n.SpawnDurationMinutes != 0)
                problems.Add($"{id}: always-up but has spawnDurationMinutes {n.SpawnDurationMinutes}");
            if (n.TimeWindows.Count > 0 && n.SpawnDurationMinutes <= 0)
                problems.Add($"{id}: timed but has no spawn duration");

            foreach (var w in n.TimeWindows)
            {
                if (w.StartHour is < 0 or > 23 || w.EndHour is < 0 or > 23)
                    problems.Add($"{id}: hour out of range ({w.StartHour}-{w.EndHour})");
                if (w.StartHour == w.EndHour)
                    problems.Add($"{id}: zero-length window at {w.StartHour}");
            }
        }

        // Duplicate keys would collide in the UI, which keys rows on this pair.
        // Every node reading as Mining means the GatheringType link came back
        // empty and the whole column defaulted, which is invisible in the file
        // itself - it just looks like a game with no quarries in it.
        if (nodes.Count > 0 && nodes.TrueForAll(n => n.Method == GatheringMethod.Mining))
            problems.Add("every node came out as Mining - the gathering method was not read");

        foreach (var n in nodes)
        {
            var jobForMethod = n.Method switch
            {
                GatheringMethod.Mining or GatheringMethod.Quarrying => NodeType.Mining,
                GatheringMethod.Logging or GatheringMethod.Harvesting => NodeType.Botany,
                _ => NodeType.Fishing,
            };

            if (jobForMethod != n.Type)
                problems.Add($"{n.ItemName} (base {n.GatheringPointBaseId}): " +
                             $"method {n.Method} does not belong to job {n.Type}");
        }

        var duplicates = nodes
            .GroupBy(n => (n.GatheringPointBaseId, n.ItemId))
            .Where(g => g.Count() > 1)
            .ToList();
        foreach (var d in duplicates)
            problems.Add($"duplicate (base {d.Key.GatheringPointBaseId}, item {d.Key.ItemId}) x{d.Count()}");

        Console.WriteLine();
        if (problems.Count == 0)
        {
            Console.WriteLine($"verify: OK - {nodes.Count} nodes parsed by the plugin's own loader, all invariants hold");
            Console.WriteLine($"        {nodes.Select(n => n.ItemId).Distinct().Count()} distinct items across " +
                              $"{nodes.Select(n => n.ZoneName).Distinct().Count()} zones");
            Console.WriteLine($"        {nodes.Count(n => n.TimeWindows.Count > 0)} timed, " +
                              $"{nodes.Count(n => n.TimeWindows.Count == 0)} always up");

            // Cosmetic rather than fatal - a node with no icon still works, it
            // just draws a gap. Reported so a wiring mistake that zeroed the
            // whole column would be obvious instead of silent.
            Console.WriteLine("        " + string.Join(", ", nodes
                .GroupBy(n => n.Method)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Count()} {g.Key}")));

            Console.WriteLine($"        {nodes.Count(n => n.IconId != 0)} with an icon, " +
                              $"{nodes.Count(n => n.Stars > 0)} starred, " +
                              $"{nodes.Count(n => n.PerceptionRequired > 0)} with a perception requirement");
            return true;
        }

        Console.Error.WriteLine($"verify: FAILED with {problems.Count} problem(s)");
        foreach (var p in problems.Take(25))
            Console.Error.WriteLine($"  {p}");
        if (problems.Count > 25)
            Console.Error.WriteLine($"  ... and {problems.Count - 25} more");
        return false;
    }

    /// <summary>Game path from XIVLauncher's config, so the common case needs no arguments.</summary>
    private static string LocateSqPack()
    {
        var config = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "XIVLauncher", "launcherConfigV3.json");

        if (File.Exists(config))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            if (doc.RootElement.TryGetProperty("GamePath", out var gamePath) &&
                gamePath.GetString() is { Length: > 0 } path)
            {
                var sqpack = Path.Combine(path, "game", "sqpack");
                if (Directory.Exists(sqpack))
                    return sqpack;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not find the game's sqpack folder via XIVLauncher. Pass it as the first argument.");
    }

    private static string LocateOutput()
    {
        // From tools/NodeGen/bin/x64/<cfg>/ back up to the repository root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VeinAndVine.sln")))
            dir = dir.Parent;

        if (dir is null)
            throw new DirectoryNotFoundException("Could not locate the repository root. Pass an output path as the second argument.");

        return Path.Combine(dir.FullName, "VeinAndVine", "Data", "nodes.json");
    }
}
