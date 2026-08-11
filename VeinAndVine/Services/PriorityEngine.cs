using VeinAndVine.Models;

namespace VeinAndVine.Services;

public sealed class PriorityResult
{
    public required GatherNode Node { get; init; }
    public required bool IsActive { get; init; }

    /// <summary>
    /// How long the node stays up. Null when it isn't up, and also null when it
    /// never closes - see <see cref="IsAlwaysAvailable"/>.
    /// </summary>
    public TimeSpan? TimeRemaining { get; init; }

    /// <summary>How long until it comes up. Null when it's already up or unknown.</summary>
    public TimeSpan? TimeUntilActive { get; init; }

    public float? DistanceFromPlayer { get; init; }

    public string? BlockedReason { get; init; }

    /// <summary>
    /// The node is simply always there - no window, no weather gate. Worth
    /// distinguishing from "up right now": both are gatherable, but only the
    /// timed one is going to stop being gatherable, and that difference is the
    /// entire reason to look at this list.
    /// </summary>
    public bool IsAlwaysAvailable =>
        Node.TimeWindows.Count == 0 && Node.RequiredWeather.Count == 0;
}

/// <summary>
/// Combines the static node dataset, current weather/time, the player's
/// wishlist, and (optionally) player position into a single sorted list.
/// Nothing in here moves the player or the camera - it only produces data
/// for the UI to render.
///
/// Depends only on <see cref="IWeatherProvider"/>, so it can be tested with a
/// fake clock and no game running.
/// </summary>
public sealed class PriorityEngine(IWeatherProvider weather)
{
    public IReadOnlyList<PriorityResult> BuildPriorityList(
        IEnumerable<GatherNode> allNodes,
        IEnumerable<WishlistEntry> wishlist,
        (float X, float Y, uint TerritoryTypeId)? playerPosition = null,
        NodeFilter? filter = null,
        NodeSort sort = NodeSort.Priority,
        bool descending = false)
    {
        filter ??= NodeFilter.Unfiltered;

        var wantedIds = wishlist
            .Where(w => w.Enabled)
            .Select(w => w.ItemId)
            .ToHashSet();

        var now = weather.CurrentUnixSeconds;
        var hour = weather.CurrentEorzeaHour;
        var results = new List<PriorityResult>();

        foreach (var node in allNodes.Where(n => wantedIds.Contains(n.ItemId) && filter.Matches(n)))
        {
            var blockedReason = GetBlockedReason(node, hour);
            var isActive = blockedReason is null;

            float? distance = null;
            if (playerPosition is { } pos && pos.TerritoryTypeId == node.TerritoryTypeId)
            {
                distance = MathF.Sqrt(
                    MathF.Pow(pos.X - node.MapX, 2) +
                    MathF.Pow(pos.Y - node.MapY, 2));
            }

            results.Add(new PriorityResult
            {
                Node = node,
                IsActive = isActive,
                TimeRemaining = isActive ? EstimateTimeRemaining(node, now, hour) : null,
                TimeUntilActive = isActive ? null : EstimateTimeUntilActive(node, now, hour),
                DistanceFromPlayer = distance,
                BlockedReason = blockedReason,
            });
        }

        return Sort(results, sort, descending);
    }

    /// <summary>
    /// Orders an already-built list. Public and static because the UI resorts
    /// on a column-header click without rebuilding - rebuilding would re-read
    /// the clock mid-frame, and re-deriving availability to answer "sort by
    /// name" is work for nothing.
    /// </summary>
    public static IReadOnlyList<PriorityResult> Sort(
        IEnumerable<PriorityResult> results,
        NodeSort sort,
        bool descending = false)
    {
        if (sort == NodeSort.Priority)
        {
            // Up now, soonest-to-expire first (grab it before it goes), then
            // nearest. Nodes waiting on weather or time sort last, by how soon
            // they open.
            var byPriority = results
                .OrderByDescending(r => r.IsActive)
                .ThenBy(r => r.TimeRemaining ?? TimeSpan.MaxValue)
                .ThenBy(r => r.TimeUntilActive ?? TimeSpan.MaxValue)
                .ThenBy(r => r.DistanceFromPlayer ?? float.MaxValue)
                .ThenBy(r => r.Node.ItemName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (descending)
                byPriority.Reverse();

            return byPriority;
        }

        IOrderedEnumerable<PriorityResult> ordered = sort switch
        {
            NodeSort.Job => descending
                ? results.OrderByDescending(r => r.Node.Type)
                : results.OrderBy(r => r.Node.Type),
            NodeSort.Level => descending
                ? results.OrderByDescending(r => r.Node.JobLevelRequired)
                : results.OrderBy(r => r.Node.JobLevelRequired),
            NodeSort.Zone => descending
                ? results.OrderByDescending(r => r.Node.ZoneName, StringComparer.OrdinalIgnoreCase)
                : results.OrderBy(r => r.Node.ZoneName, StringComparer.OrdinalIgnoreCase),
            // Nodes in another zone have no distance, and sink either way -
            // "furthest first" still shouldn't be led by rows with no answer.
            NodeSort.Distance => descending
                ? results.OrderByDescending(r => r.DistanceFromPlayer ?? -1f)
                : results.OrderBy(r => r.DistanceFromPlayer ?? float.MaxValue),
            _ => descending
                ? results.OrderByDescending(r => r.Node.ItemName, StringComparer.OrdinalIgnoreCase)
                : results.OrderBy(r => r.Node.ItemName, StringComparer.OrdinalIgnoreCase),
        };

        // Within an equal key, keep what's up now at the top and then hold a
        // fixed order, so rows don't shuffle under the cursor every frame.
        return ordered
            .ThenByDescending(r => r.IsActive)
            .ThenBy(r => r.Node.ItemName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Node.GatheringPointBaseId)
            .ToList();
    }

    /// <summary>Null when the node is available; otherwise a short explanation.</summary>
    private string? GetBlockedReason(GatherNode node, int eorzeaHour)
    {
        if (node.TimeWindows.Count > 0 && node.ActiveWindowAt(eorzeaHour) is null)
        {
            var next = NextWindowStart(node, eorzeaHour);
            return $"Opens at {next:00}:00 ET";
        }

        if (node.RequiredWeather.Count > 0)
        {
            var current = weather.GetCurrentWeather(node.TerritoryTypeId);
            if (current is null || !node.RequiredWeather.Contains(current))
                return $"Needs {string.Join(" / ", node.RequiredWeather)}";
        }

        return null;
    }

    /// <summary>
    /// Real time left before the node's conditions stop holding: whichever of
    /// the currently-open time window or the weather window closes first,
    /// capped by the node's own spawn duration.
    /// </summary>
    private static TimeSpan? EstimateTimeRemaining(GatherNode node, long now, int eorzeaHour)
    {
        var limits = new List<long>();

        // Only the window we are actually inside bounds the remaining time;
        // the node's other windows are later today and say nothing about now.
        if (node.ActiveWindowAt(eorzeaHour) is { } window)
            limits.Add(EorzeaTime.RealSecondsUntilEorzeaHour(now, window.EndHour));

        if (node.RequiredWeather.Count > 0)
            limits.Add(EorzeaTime.GetWeatherWindowEnd(now) - now);

        // Nothing bounds it, so nothing is going to close: the node has neither
        // a window nor a weather gate. Any number here would be an invented
        // deadline, and the UI says "Always" instead.
        if (limits.Count == 0)
            return null;

        var seconds = limits.Min();

        // A duration of 0 in the dataset means "no cap", which is how always-up
        // nodes are written - guard so it can never zero out a real countdown.
        if (node.SpawnDurationMinutes > 0)
            seconds = System.Math.Min(seconds, node.SpawnDurationMinutes * 60L);

        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Real time until a time-gated node next opens, using whichever of its
    /// windows comes round first. Weather-gated nodes return null here -
    /// forecasting those needs the sheet-backed
    /// <see cref="WeatherService.FindNextWeatherWindow"/>, which the engine
    /// deliberately does not depend on.
    /// </summary>
    private static TimeSpan? EstimateTimeUntilActive(GatherNode node, long now, int eorzeaHour)
    {
        if (node.TimeWindows.Count == 0)
            return null;

        // A window is already open, so whatever is blocking the node is the
        // weather, not the clock. Reporting the time to the *next* window here
        // would claim it opens in nearly a full day when it may clear on the
        // next weather roll.
        if (node.ActiveWindowAt(eorzeaHour) is not null)
            return null;

        var start = NextWindowStart(node, eorzeaHour);
        return TimeSpan.FromSeconds(EorzeaTime.RealSecondsUntilEorzeaHour(now, start));
    }

    /// <summary>
    /// Start hour of the node's soonest upcoming window, measured forward from
    /// <paramref name="eorzeaHour"/> and wrapping past midnight.
    ///
    /// Only meaningful when the node is closed - if a window were open, the
    /// distance to its own start would be 0 and it would win trivially.
    /// </summary>
    private static int NextWindowStart(GatherNode node, int eorzeaHour)
    {
        var best = node.TimeWindows[0].StartHour;
        var bestDistance = int.MaxValue;

        foreach (var window in node.TimeWindows)
        {
            var distance = (window.StartHour - eorzeaHour + 24) % 24;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = window.StartHour;
            }
        }

        return best;
    }
}
