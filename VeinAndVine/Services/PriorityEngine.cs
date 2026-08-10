using VeinAndVine.Models;

namespace VeinAndVine.Services;

public sealed class PriorityResult
{
    public required GatherNode Node { get; init; }
    public required bool IsActive { get; init; }

    /// <summary>How long the node stays up. Null when it isn't up.</summary>
    public TimeSpan? TimeRemaining { get; init; }

    /// <summary>How long until it comes up. Null when it's already up or unknown.</summary>
    public TimeSpan? TimeUntilActive { get; init; }

    public float? DistanceFromPlayer { get; init; }

    /// <summary>Why the node isn't up, for the UI to explain itself.</summary>
    public string? BlockedReason { get; init; }
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
        (float X, float Y, uint TerritoryTypeId)? playerPosition = null)
    {
        var wantedIds = wishlist
            .Where(w => w.Enabled)
            .Select(w => w.ItemId)
            .ToHashSet();

        var now = weather.CurrentUnixSeconds;
        var hour = weather.CurrentEorzeaHour;
        var results = new List<PriorityResult>();

        foreach (var node in allNodes.Where(n => wantedIds.Contains(n.ItemId)))
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
                TimeRemaining = isActive ? EstimateTimeRemaining(node, now) : null,
                TimeUntilActive = isActive ? null : EstimateTimeUntilActive(node, now),
                DistanceFromPlayer = distance,
                BlockedReason = blockedReason,
            });
        }

        // Up now, soonest-to-expire first (grab it before it goes), then
        // nearest. Nodes waiting on weather or time sort last, by how soon
        // they open.
        return results
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.TimeRemaining ?? TimeSpan.MaxValue)
            .ThenBy(r => r.TimeUntilActive ?? TimeSpan.MaxValue)
            .ThenBy(r => r.DistanceFromPlayer ?? float.MaxValue)
            .ToList();
    }

    /// <summary>Null when the node is available; otherwise a short explanation.</summary>
    private string? GetBlockedReason(GatherNode node, int eorzeaHour)
    {
        if (node.TimeWindow is { } window && !window.Contains(eorzeaHour))
            return $"Opens at {window.StartHour:00}:00 ET";

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
    /// the time window or the weather window closes first, capped by the
    /// node's own spawn duration.
    /// </summary>
    private static TimeSpan? EstimateTimeRemaining(GatherNode node, long now)
    {
        var limits = new List<long>();

        if (node.TimeWindow is { } window)
            limits.Add(EorzeaTime.RealSecondsUntilEorzeaHour(now, window.EndHour));

        if (node.RequiredWeather.Count > 0)
            limits.Add(EorzeaTime.GetWeatherWindowEnd(now) - now);

        if (limits.Count == 0)
            return TimeSpan.FromMinutes(node.SpawnDurationMinutes);

        var seconds = System.Math.Min(limits.Min(), node.SpawnDurationMinutes * 60L);
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Real time until a time-gated node opens. Weather-gated nodes return
    /// null here - forecasting those needs the sheet-backed
    /// <see cref="WeatherService.FindNextWeatherWindow"/>, which the engine
    /// deliberately does not depend on.
    /// </summary>
    private static TimeSpan? EstimateTimeUntilActive(GatherNode node, long now)
    {
        if (node.TimeWindow is { } window)
            return TimeSpan.FromSeconds(EorzeaTime.RealSecondsUntilEorzeaHour(now, window.StartHour));

        return null;
    }
}
