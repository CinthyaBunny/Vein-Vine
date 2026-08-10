using VeinAndVine.Models;

namespace VeinAndVine.Services;

/// <summary>
/// Which gathering jobs a list is showing. Flags rather than a nullable
/// <see cref="NodeType"/> because "miner and botanist but not fisher" is the
/// ordinary case - most players level both and toggle them independently.
/// </summary>
[Flags]
public enum JobFilter
{
    None = 0,
    Miner = 1 << 0,
    Botanist = 1 << 1,
    Fisher = 1 << 2,
    All = Miner | Botanist | Fisher,
}

/// <summary>
/// Sort key for a node or item list.
///
/// <see cref="Priority"/> is the only composite key - the rest sort on a single
/// field, so that clicking a column header does exactly what the header says
/// and nothing else.
/// </summary>
public enum NodeSort
{
    /// <summary>Up now first, then soonest to expire, then nearest.</summary>
    Priority,
    ItemName,
    Job,
    Level,
    Zone,
    Distance,
}

/// <summary>
/// One row in the item picker: a single gatherable item, collapsed across every
/// node that yields it.
///
/// The wishlist tracks items, not nodes, but the dataset is node-shaped - the
/// same item shows up in several zones with different windows. Collapsing that
/// here means the picker can show one honest row per item instead of the same
/// name three times.
/// </summary>
public sealed record GatherItem
{
    public required uint ItemId { get; init; }
    public required string ItemName { get; init; }
    public required NodeType Type { get; init; }

    /// <summary>Lowest level at which the item can be gathered from any of its nodes.</summary>
    public required int JobLevelRequired { get; init; }

    /// <summary>Every zone the item appears in, alphabetical.</summary>
    public required IReadOnlyList<string> Zones { get; init; }

    /// <summary>Union of the Eorzea-hour windows across all of the item's nodes.</summary>
    public required string WindowSummary { get; init; }

    /// <summary>
    /// At least one of the item's nodes is gated on time or weather. An item
    /// can be both: the same ore sometimes has an ordinary node and a timed
    /// unspoiled one, and it counts as timed because one of its nodes needs
    /// watching.
    /// </summary>
    public required bool IsTimed { get; init; }

    public string ZoneSummary => Zones.Count switch
    {
        0 => "-",
        1 => Zones[0],
        _ => $"{Zones[0]}  +{Zones.Count - 1}",
    };
}

/// <summary>
/// The set of conditions a node or item has to meet to be listed. Pure and
/// value-typed, so the windows can hand one to the engine and both the filter
/// and the sort stay testable with no game running.
/// </summary>
public sealed record NodeFilter
{
    public const int MinGatheringLevel = 1;
    public const int MaxGatheringLevel = 100;

    /// <summary>Matches everything. The default when a caller passes no filter.</summary>
    public static readonly NodeFilter Unfiltered = new();

    public JobFilter Jobs { get; init; } = JobFilter.All;

    /// <summary>Substring match against the item name or any of its zone names.</summary>
    public string Search { get; init; } = string.Empty;

    /// <summary>
    /// Restrict to one zone by display name. Null means any zone.
    ///
    /// The picker filters by name because that is what its dropdown lists;
    /// the main window filters by <see cref="TerritoryTypeId"/> instead, since
    /// it starts from the player's territory and several territories can share
    /// a zone name.
    /// </summary>
    public string? ZoneName { get; init; }

    /// <summary>Restrict to one territory. Null means any.</summary>
    public uint? TerritoryTypeId { get; init; }

    public int MinLevel { get; init; } = MinGatheringLevel;
    public int MaxLevel { get; init; } = MaxGatheringLevel;

    /// <summary>
    /// Drop nodes that are always up, leaving only the ones with a window.
    ///
    /// The dataset is mostly always-up nodes, and those never need watching -
    /// this is the switch between "where do I get this" and "what's up right
    /// now", which is the question the plugin exists to answer.
    /// </summary>
    public bool TimedOnly { get; init; }

    public bool Matches(GatherNode node)
    {
        if (!MatchesJob(node.Type) || !MatchesLevel(node.JobLevelRequired))
            return false;

        if (TimedOnly && node.TimeWindows.Count == 0 && node.RequiredWeather.Count == 0)
            return false;

        if (TerritoryTypeId is { } territory && node.TerritoryTypeId != territory)
            return false;

        if (ZoneName is not null && !string.Equals(node.ZoneName, ZoneName, StringComparison.Ordinal))
            return false;

        return Search.Length == 0
               || Contains(node.ItemName, Search)
               || Contains(node.ZoneName, Search);
    }

    public bool Matches(GatherItem item)
    {
        if (!MatchesJob(item.Type) || !MatchesLevel(item.JobLevelRequired))
            return false;

        if (TimedOnly && !item.IsTimed)
            return false;

        if (ZoneName is not null && !item.Zones.Contains(ZoneName, StringComparer.Ordinal))
            return false;

        return Search.Length == 0
               || Contains(item.ItemName, Search)
               || item.Zones.Any(zone => Contains(zone, Search));
    }

    private bool MatchesJob(NodeType type) => (Jobs & type.ToJobFilter()) != 0;

    private bool MatchesLevel(int level) => level >= MinLevel && level <= MaxLevel;

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Pure helpers for turning the node dataset into something a list view can
/// show: the per-item index behind the picker, the zone dropdown, window
/// summaries, and the picker's sort.
///
/// Sorting for the main window lives on <see cref="PriorityEngine"/> instead,
/// because it needs the live availability and distance that only the engine
/// computes.
/// </summary>
public static class NodeQuery
{
    public static JobFilter ToJobFilter(this NodeType type) => type switch
    {
        NodeType.Mining => JobFilter.Miner,
        NodeType.Botany => JobFilter.Botanist,
        NodeType.Fishing => JobFilter.Fisher,
        _ => JobFilter.None,
    };

    /// <summary>
    /// "00-02, 12-14", or "Always up" for a node with no time restriction.
    /// Duplicates are collapsed, so an item gathered from three nodes that
    /// share a window shows that window once.
    /// </summary>
    public static string DescribeWindows(IEnumerable<EorzeaHourWindow> windows)
    {
        var distinct = windows
            .Distinct()
            .OrderBy(w => w.StartHour)
            .ThenBy(w => w.EndHour)
            .ToList();

        return distinct.Count == 0
            ? "Always up"
            : string.Join(", ", distinct.Select(w => $"{w.StartHour:00}-{w.EndHour:00}"));
    }

    /// <summary>
    /// One <see cref="GatherItem"/> per distinct item id, alphabetical.
    ///
    /// Worth caching: it walks the whole dataset, and the dataset only changes
    /// on an explicit reload.
    /// </summary>
    public static List<GatherItem> BuildItemIndex(IEnumerable<GatherNode> nodes) =>
        nodes
            .GroupBy(n => n.ItemId)
            .Select(group =>
            {
                var first = group.First();
                return new GatherItem
                {
                    ItemId = group.Key,
                    ItemName = first.ItemName,
                    Type = first.Type,
                    // The lowest level any of its nodes needs - that's the
                    // level at which the item actually becomes reachable.
                    JobLevelRequired = group.Min(n => n.JobLevelRequired),
                    Zones = group
                        .Select(n => n.ZoneName)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
                        .ToList(),
                    WindowSummary = DescribeWindows(group.SelectMany(n => n.TimeWindows)),
                    IsTimed = group.Any(n => n.TimeWindows.Count > 0 || n.RequiredWeather.Count > 0),
                };
            })
            .OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Distinct zone names in the dataset, alphabetical.</summary>
    public static List<string> BuildZoneIndex(IEnumerable<GatherNode> nodes) =>
        nodes
            .Select(n => n.ZoneName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(z => z, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Sorts picker rows. <see cref="NodeSort.Priority"/> and
    /// <see cref="NodeSort.Distance"/> need live state the picker doesn't have,
    /// so they fall back to name order rather than pretending.
    /// </summary>
    public static List<GatherItem> SortItems(IEnumerable<GatherItem> items, NodeSort sort, bool descending)
    {
        IOrderedEnumerable<GatherItem> ordered = sort switch
        {
            NodeSort.Level => descending
                ? items.OrderByDescending(i => i.JobLevelRequired)
                : items.OrderBy(i => i.JobLevelRequired),
            NodeSort.Job => descending
                ? items.OrderByDescending(i => i.Type)
                : items.OrderBy(i => i.Type),
            NodeSort.Zone => descending
                ? items.OrderByDescending(i => i.ZoneSummary, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(i => i.ZoneSummary, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? items.OrderByDescending(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
                : items.OrderBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase),
        };

        // Name breaks every tie, so the order is stable frame to frame instead
        // of drifting with whatever order the group-by happened to produce.
        return ordered
            .ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.ItemId)
            .ToList();
    }
}
