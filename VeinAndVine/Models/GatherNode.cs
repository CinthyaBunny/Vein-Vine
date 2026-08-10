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

    /// <summary>Null means the node has no time restriction.</summary>
    public EorzeaHourWindow? TimeWindow { get; init; }

    /// <summary>
    /// Weather names that must be active for the node to appear, matched
    /// against the Weather sheet's display name. Empty means unrestricted.
    /// </summary>
    public IReadOnlyList<string> RequiredWeather { get; init; } = [];

    /// <summary>How long the node stays up once its conditions are met.</summary>
    public int SpawnDurationMinutes { get; init; } = 60;
}
