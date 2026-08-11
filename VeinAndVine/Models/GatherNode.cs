namespace VeinAndVine.Models;

/// <summary>The job that can work a node.</summary>
public enum NodeType
{
    Mining,
    Botany,
    Fishing,
}

/// <summary>
/// How a node is actually worked, which is a finer split than the job: a miner
/// mines and quarries, a botanist logs and harvests. Named as the game's own
/// GatheringType sheet names them.
///
/// Kept alongside <see cref="NodeType"/> rather than replacing it, because the
/// job is what the wishlist and the main window filter on and the method is
/// only ever a refinement of it.
/// </summary>
public enum GatheringMethod
{
    Mining,
    Quarrying,
    Logging,
    Harvesting,
    Spearfishing,
}

/// <summary>
/// An Eorzea-hour availability window, e.g. 0-8. Wrapping past midnight is
/// allowed (StartHour 22, EndHour 4).
///
/// This is a record struct rather than a tuple so it round-trips through
/// System.Text.Json - ValueTuple exposes fields, not properties, and the
/// serializer silently writes {} for it.
/// </summary>
public readonly record struct EorzeaHourWindow(int StartHour, int EndHour)
{
    public bool Contains(int eorzeaHour) =>
        StartHour <= EndHour
            ? eorzeaHour >= StartHour && eorzeaHour < EndHour
            : eorzeaHour >= StartHour || eorzeaHour < EndHour;
}

/// <summary>
/// Static, unchanging data about a single gatherable node. This never touches
/// live game state - it's the same shape whether loaded from a bundled JSON
/// file or fetched from an external dataset at build time.
/// </summary>
public sealed class GatherNode
{
    public required uint ItemId { get; init; }
    public required string ItemName { get; init; }
    public required NodeType Type { get; init; }

    /// <summary>
    /// The finer split within the job. Optional so a dataset written before
    /// this existed still parses; such a file leaves every node reading as
    /// Mining, which the generator's verify pass would catch.
    /// </summary>
    public GatheringMethod Method { get; init; }
    public required string ZoneName { get; init; }

    /// <summary>
    /// GatheringPointBase row id the node was generated from. Not used for any
    /// game lookup - it exists so a row can be traced back to the sheet it came
    /// from, and because (GatheringPointBaseId, ItemId) is the only unique key
    /// for a node: the same item appears on several nodes, and one node yields
    /// several items.
    /// </summary>
    public uint GatheringPointBaseId { get; init; }

    /// <summary>
    /// TerritoryType row id. Drives both the weather lookup and the map flag,
    /// so a node without it can do neither.
    /// </summary>
    public required uint TerritoryTypeId { get; init; }

    /// <summary>Map row id, needed to build a MapLinkPayload.</summary>
    public required uint MapId { get; init; }

    /// <summary>In-game map coordinates, the ones shown in a chat map link.</summary>
    public required float MapX { get; init; }
    public required float MapY { get; init; }

    public required int JobLevelRequired { get; init; }

    /// <summary>
    /// Item sheet icon id, for <c>ITextureProvider.GetFromGameIcon</c>. Zero
    /// means unknown, and the UI leaves the space blank rather than guessing.
    ///
    /// Baked into the dataset rather than looked up live: it is two bytes, it
    /// is needed for every visible row, and having it here keeps the icon
    /// working without a sheet read on the draw path.
    /// </summary>
    public uint IconId { get; init; }

    /// <summary>
    /// Gathering perception needed for the node's full yield. Zero means no
    /// requirement.
    /// </summary>
    public int PerceptionRequired { get; init; }

    /// <summary>
    /// Node star rating, 0-4. Higher means stricter stat requirements. Four is
    /// rare - only the level 90 collectable nodes reach it.
    /// </summary>
    public int Stars { get; init; }

    /// <summary>
    /// Eorzea-hour windows during which the node is up. Empty means no time
    /// restriction.
    ///
    /// This is a list rather than a single window because most timed nodes in
    /// the game spawn two or three times per Eorzea day - the game's
    /// GatheringRarePopTimeTable holds up to several start times per node, and
    /// collapsing them to one would report the node as unavailable for most of
    /// the day.
    /// </summary>
    public IReadOnlyList<EorzeaHourWindow> TimeWindows { get; init; } = [];

    /// <summary>
    /// Weather names that must be active for the node to appear, matched
    /// against the Weather sheet's display name. Empty means unrestricted.
    /// </summary>
    public IReadOnlyList<string> RequiredWeather { get; init; } = [];

    /// <summary>How long the node stays up once its conditions are met.</summary>
    public int SpawnDurationMinutes { get; init; } = 60;

    /// <summary>
    /// The window currently holding <paramref name="eorzeaHour"/>, or null if
    /// none does. A node with no windows is always open and returns null too,
    /// so callers must check <see cref="TimeWindows"/> being empty separately.
    /// </summary>
    public EorzeaHourWindow? ActiveWindowAt(int eorzeaHour)
    {
        foreach (var window in TimeWindows)
        {
            if (window.Contains(eorzeaHour))
                return window;
        }

        return null;
    }
}
