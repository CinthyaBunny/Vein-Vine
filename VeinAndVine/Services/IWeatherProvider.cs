namespace VeinAndVine.Services;

/// <summary>
/// The only thing PriorityEngine needs to know about weather and time.
/// Keeping it behind an interface is what lets the engine be unit tested
/// with no Dalamud, no game client, and no clock dependency.
/// </summary>
public interface IWeatherProvider
{
    int CurrentEorzeaHour { get; }

    long CurrentUnixSeconds { get; }

    /// <summary>
    /// Display name of the weather currently active in a territory, or null
    /// if the territory has no weather table.
    /// </summary>
    string? GetCurrentWeather(uint territoryTypeId);
}
