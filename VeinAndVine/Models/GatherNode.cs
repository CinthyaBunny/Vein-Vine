namespace VeinAndVine.Models;

public enum NodeType
{
    Mining,
    Botany,
    Fishing,
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
