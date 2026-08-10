namespace VeinAndVine.Models;

public enum NodeType
{
    Mining,
    Botany,
    Fishing,
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
    public required float MapX { get; init; }
    public required float MapY { get; init; }
    public required int JobLevelRequired { get; init; }

    /// <summary>
    /// Eorzea-hour window the node is available, e.g. 0-8. Null means the
    /// node has no time restriction.
    /// </summary>
    public (int StartHour, int EndHour)? TimeWindow { get; init; }

    /// <summary>
    /// Weather types that must be active for the node to appear. Empty means
    /// no weather restriction.
    /// </summary>
    public IReadOnlyList<string> RequiredWeather { get; init; } = [];

    /// <summary>
    /// How long the node stays up once its conditions are met, in minutes.
    /// </summary>
    public int SpawnDurationMinutes { get; init; } = 60;
}
