namespace VeinAndVine.Services;

/// <summary>
/// FFXIV weather is deterministic - each zone has a weather rate table, and
/// the active weather is derived from a seeded calculation based on the
/// current Eorzea time. That means this class needs no live game hooks at
/// all; it's pure math and can be unit tested standalone.
///
/// This is a stub. To make it real:
///   1. Source each zone's weather rate table (chance per weather type).
///      These are publicly documented; several community sites and the
///      Teamcraft/Garland Tools datasets already expose them.
///   2. Implement the known calculation: derive a per-zone seed from the
///      current Eorzea timestamp, then walk the rate table with that seed
///      to pick the active weather.
///   3. Extend to also compute the *next* weather change, not just current,
///      so the priority engine can show upcoming windows.
/// </summary>
public sealed class WeatherService
{
    /// <summary>
    /// Returns the currently active weather name for a zone.
    /// Placeholder implementation - always returns "Clear Skies".
    /// </summary>
    public string GetCurrentWeather(string zoneName)
    {
        // TODO: replace with the real per-zone seeded calculation.
        return "Clear Skies";
    }

    /// <summary>
    /// Returns the current Eorzea hour (0-23), used for time-windowed nodes.
    /// FFXIV's Eorzea clock runs 20.5716x real time.
    /// </summary>
    public int GetCurrentEorzeaHour()
    {
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var eorzeaSeconds = unixSeconds * 20.5716;
        var eorzeaHour = (int)(eorzeaSeconds / 3600) % 24;
        return eorzeaHour;
    }
}
