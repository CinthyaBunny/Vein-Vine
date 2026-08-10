using VeinAndVine.Models;

namespace VeinAndVine.Services;

public sealed class PriorityResult
{
    public required GatherNode Node { get; init; }
    public required bool IsActive { get; init; }
    public required TimeSpan? TimeRemaining { get; init; }
    public float? DistanceFromPlayer { get; init; }
}

/// <summary>
/// Combines the static node dataset, current weather/time, the player's
/// wishlist, and (optionally) player position into a single sorted list.
/// Nothing in here moves the player or the camera - it only produces data
/// for the UI to render.
/// </summary>
public sealed class PriorityEngine
{
    private readonly WeatherService weatherService;

    public PriorityEngine(WeatherService weatherService)
    {
        this.weatherService = weatherService;
    }

    public IReadOnlyList<PriorityResult> BuildPriorityList(
        IEnumerable<GatherNode> allNodes,
        IEnumerable<WishlistEntry> wishlist,
        (float X, float Y, string Zone)? playerPosition = null)
    {
        var wantedIds = wishlist
            .Where(w => w.Enabled)
            .Select(w => w.ItemId)
            .ToHashSet();

        var results = new List<PriorityResult>();

        foreach (var node in allNodes.Where(n => wantedIds.Contains(n.ItemId)))
        {
            var isActive = IsNodeActive(node);
            var remaining = isActive ? EstimateTimeRemaining(node) : null;

            float? distance = null;
            if (playerPosition is { } pos && pos.Zone == node.ZoneName)
            {
                distance = MathF.Sqrt(
                    MathF.Pow(pos.X - node.MapX, 2) +
                    MathF.Pow(pos.Y - node.MapY, 2));
            }

            results.Add(new PriorityResult
            {
                Node = node,
                IsActive = isActive,
                TimeRemaining = remaining,
                DistanceFromPlayer = distance,
            });
        }

        // Active and closing-soon nodes first, then sort by distance within
        // that. Inactive nodes (waiting on weather/time) sort last.
        return results
            .OrderByDescending(r => r.IsActive)
            .ThenBy(r => r.TimeRemaining ?? TimeSpan.MaxValue)
            .ThenBy(r => r.DistanceFromPlayer ?? float.MaxValue)
            .ToList();
    }

    private bool IsNodeActive(GatherNode node)
    {
        if (node.TimeWindow is { } window)
        {
            var hour = weatherService.GetCurrentEorzeaHour();
            var inWindow = window.StartHour <= window.EndHour
                ? hour >= window.StartHour && hour < window.EndHour
                : hour >= window.StartHour || hour < window.EndHour; // wraps past midnight
            if (!inWindow)
                return false;
        }

        if (node.RequiredWeather.Count > 0)
        {
            var current = weatherService.GetCurrentWeather(node.ZoneName);
            if (!node.RequiredWeather.Contains(current))
                return false;
        }

        return true;
    }

    private TimeSpan? EstimateTimeRemaining(GatherNode node)
    {
        // Placeholder: real version should compute exact minutes left in the
        // current weather/time window rather than just the node's max
        // spawn duration.
        return TimeSpan.FromMinutes(node.SpawnDurationMinutes);
    }
}
