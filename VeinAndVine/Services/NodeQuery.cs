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
/// Which gathering methods a list is showing - the finer split inside a job.
/// Flags for the same reason <see cref="JobFilter"/> is: an item can come from
/// both a mining node and a quarrying one.
/// </summary>
[Flags]
public enum MethodFilter
{
    None = 0,
    Mining = 1 << 0,
    Quarrying = 1 << 1,
    Logging = 1 << 2,
    Harvesting = 1 << 3,
    Spearfishing = 1 << 4,

    AllMiner = Mining | Quarrying,
    AllBotanist = Logging | Harvesting,
    All = AllMiner | AllBotanist | Spearfishing,
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

    /// <summary>
    /// Every job that can gather this item, which is not always one: 98 items
    /// in the current dataset have both mining and botany nodes. Collapsing
    /// that to a single job would hide each of them from one of the two job
    /// tabs.
    /// </summary>
    public required JobFilter Jobs { get; init; }

    /// <summary>
    /// Every method that yields this item. A union for the same reason
    /// <see cref="Jobs"/> is - plenty of items come off both a mining node and
    /// a quarrying one, and picking either sub-tab should still find them.
    /// </summary>
    public required MethodFilter Methods { get; init; }

    /// <summary>Lowest level at which the item can be gathered from any of its nodes.</summary>
    public required int JobLevelRequired { get; init; }

    /// <summary>Item sheet icon id. Zero when the dataset predates it.</summary>
    public required uint IconId { get; init; }

    /// <summary>
    /// The gentlest requirements across the item's nodes - the easiest node is
    /// the one you would actually go to, so quoting the harshest would put
    /// someone off an item they can already gather.
    /// </summary>
    public required int PerceptionRequired { get; init; }

    public required int Stars { get; init; }

    /// <summary>Every zone the item appears in, alphabetical.</summary>
    public required IReadOnlyList<string> Zones { get; init; }

    /// <summary>Union of the Eorzea-hour windows across the item's nodes, or "Always up".</summary>
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

    public static readonly NodeFilter Unfiltered = new();

    public JobFilter Jobs { get; init; } = JobFilter.All;

    /// <summary>
    /// The finer split inside the job - mining versus quarrying, logging versus
    /// harvesting. Defaults to all of them, so a caller that only cares about
    /// the job never has to mention it.
    ///
    /// For nodes this is the whole story. For items it only decides which rows
    /// survive, not what they say: a <see cref="GatherItem"/> summarises the
    /// nodes it was built from, so narrowing to a method also means building
    /// the index with that same <see cref="MethodFilter"/> - see
    /// <see cref="NodeQuery.BuildItemIndex"/>.
    /// </summary>
    public MethodFilter Methods { get; init; } = MethodFilter.All;

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

        if ((Methods & node.Method.ToMethodFilter()) == 0)
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
        if ((Jobs & item.Jobs) == 0 || (Methods & item.Methods) == 0 ||
            !MatchesLevel(item.JobLevelRequired))
            return false;

        if (TimedOnly && !item.IsTimed)
            return false;

        if (ZoneName is not null && !item.Zones.Contains(ZoneName, StringComparer.Ordinal))
            return false;

        return Search.Length == 0
               || Contains(item.ItemName, Search)
               || item.Zones.Any(zone => Contains(zone, Search));
    }

    /// <summary>
    /// Whether a level box should accept a keystroke: true if replacing
    /// <paramref name="current"/>[<paramref name="start"/>..<paramref name="end"/>]
    /// with <paramref name="typed"/> leaves text that is still a gathering
    /// level, or empty.
    ///
    /// Deciding on the prospective text rather than the character is the whole
    /// point - every digit is fine on its own and still turns 10 into 105. It
    /// lives here because this is where the legal range is defined, and it is
    /// pure so the rule can be tested without an ImGui context to type into.
    /// </summary>
    public static bool AcceptsLevelKeystroke(ReadOnlySpan<char> current, int start, int end, char typed)
    {
        if (typed is < '0' or > '9')
            return false;

        start = Math.Clamp(start, 0, current.Length);
        end = Math.Clamp(end, start, current.Length);

        // No level starts with a zero, so refusing one keeps "007" out of the
        // box rather than tidying it away afterwards.
        if (typed == '0' && start == 0)
            return false;

        var value = 0;

        for (var index = 0; index < start; index++)
        {
            if (!Accumulate(current[index], ref value))
                return false;
        }

        if (!Accumulate(typed, ref value))
            return false;

        for (var index = end; index < current.Length; index++)
        {
            if (!Accumulate(current[index], ref value))
                return false;
        }

        return value >= MinGatheringLevel;

        // False the moment the number stops being a possible level. Anything
        // already in the box that isn't a digit fails here too: it should not
        // be reachable, and reading it as a digit would invent a wild value.
        static bool Accumulate(char digit, ref int value)
        {
            if (digit is < '0' or > '9')
                return false;

            value = (value * 10) + (digit - '0');
            return value <= MaxGatheringLevel;
        }
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

    public static MethodFilter ToMethodFilter(this GatheringMethod method) => method switch
    {
        GatheringMethod.Mining => MethodFilter.Mining,
        GatheringMethod.Quarrying => MethodFilter.Quarrying,
        GatheringMethod.Logging => MethodFilter.Logging,
        GatheringMethod.Harvesting => MethodFilter.Harvesting,
        GatheringMethod.Spearfishing => MethodFilter.Spearfishing,
        _ => MethodFilter.None,
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
    /// <paramref name="methods"/> scopes the walk before the grouping, and that
    /// ordering is the whole point. A <see cref="GatherItem"/> is a summary of
    /// the nodes behind it - its zones, its level, its windows, whether it is
    /// timed at all - so summarising every node and then filtering the summary
    /// describes an item by nodes the caller has excluded. On a Quarrying
    /// sub-tab that means zones you can only reach by mining, and a level lower
    /// than any quarrying node actually needs. Scoping first means the row
    /// describes what the sub-tab is showing.
    ///
    /// Worth caching per scope: it walks the whole dataset, and neither the
    /// dataset nor the handful of scopes the UI asks for change per frame.
    /// </summary>
    public static List<GatherItem> BuildItemIndex(
        IEnumerable<GatherNode> nodes, MethodFilter methods = MethodFilter.All) =>
        nodes
            .Where(n => (methods & n.Method.ToMethodFilter()) != 0)
            .GroupBy(n => n.ItemId)
            .Select(group =>
            {
                var first = group.First();
                return new GatherItem
                {
                    ItemId = group.Key,
                    ItemName = first.ItemName,
                    Methods = group.Aggregate(
                        MethodFilter.None, (acc, n) => acc | n.Method.ToMethodFilter()),
                    Jobs = group.Aggregate(
                        JobFilter.None, (jobs, n) => jobs | n.Type.ToJobFilter()),
                    // The lowest level any of its nodes needs - that's the
                    // level at which the item actually becomes reachable.
                    JobLevelRequired = group.Min(n => n.JobLevelRequired),
                    IconId = first.IconId,
                    PerceptionRequired = group.Min(n => n.PerceptionRequired),
                    Stars = group.Min(n => n.Stars),
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
            // By the job set, so the both-jobs items group together instead of
            // being scattered through whichever half they were typed as.
            NodeSort.Job => descending
                ? items.OrderByDescending(i => i.Jobs)
                : items.OrderBy(i => i.Jobs),
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
