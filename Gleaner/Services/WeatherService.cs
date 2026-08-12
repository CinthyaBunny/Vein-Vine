using Lumina.Excel.Sheets;

namespace Gleaner.Services;

/// <summary>
/// FFXIV weather is deterministic: a single 0-99 roll per 8-Eorzea-hour
/// window (see <see cref="EorzeaTime.CalculateWeatherChance"/>), mapped onto a
/// weather type through each territory's WeatherRate table. There are no
/// hooks and no live game reads here beyond the static Excel sheets, so this
/// can answer questions about the past and the future just as easily as now.
/// </summary>
public sealed class WeatherService : IWeatherProvider
{
    private readonly Dictionary<uint, string> weatherNameCache = [];

    public int CurrentEorzeaHour => EorzeaTime.GetCurrentEorzeaHour();

    public long CurrentUnixSeconds => EorzeaTime.CurrentUnixSeconds();

    public string? GetCurrentWeather(uint territoryTypeId) =>
        GetWeatherAt(territoryTypeId, EorzeaTime.CurrentUnixSeconds());

    /// <summary>
    /// Weather active in a territory at an arbitrary real time. Pass a future
    /// timestamp to forecast.
    /// </summary>
    public string? GetWeatherAt(uint territoryTypeId, long unixSeconds)
    {
        var weatherId = GetWeatherIdAt(territoryTypeId, unixSeconds);
        return weatherId is null ? null : GetWeatherName(weatherId.Value);
    }

    public uint? GetWeatherIdAt(uint territoryTypeId, long unixSeconds)
    {
        var territory = Service.DataManager.GetExcelSheet<TerritoryType>()
                               .GetRowOrDefault(territoryTypeId);
        if (territory is null)
            return null;

        var rateRow = territory.Value.WeatherRate.ValueNullable;
        if (rateRow is null)
            return null;

        var chance = EorzeaTime.CalculateWeatherChance(unixSeconds);

        // The rate table is a cumulative distribution: walk it and take the
        // first entry whose running total exceeds the roll.
        var cumulative = 0;
        for (var i = 0; i < rateRow.Value.Rate.Count; i++)
        {
            cumulative += rateRow.Value.Rate[i];
            if (chance < cumulative)
            {
                var id = rateRow.Value.Weather[i].RowId;
                return id == 0 ? null : id;
            }
        }

        return null;
    }

    /// <summary>
    /// Scans forward in weather windows for the next time a territory has one
    /// of the wanted weather types. Returns null if none occurs within
    /// <paramref name="maxWindows"/> (default ~28 real hours).
    /// </summary>
    public long? FindNextWeatherWindow(
        uint territoryTypeId,
        IReadOnlyCollection<string> wantedWeather,
        long fromUnixSeconds,
        int maxWindows = 72)
    {
        if (wantedWeather.Count == 0)
            return fromUnixSeconds;

        var windowStart = EorzeaTime.GetWeatherWindowStart(fromUnixSeconds);
        for (var i = 0; i < maxWindows; i++)
        {
            var probe = windowStart + i * EorzeaTime.RealSecondsPerWeatherWindow;
            var weather = GetWeatherAt(territoryTypeId, probe);
            if (weather is not null && wantedWeather.Contains(weather))
                return probe;
        }

        return null;
    }

    private string? GetWeatherName(uint weatherId)
    {
        if (weatherNameCache.TryGetValue(weatherId, out var cached))
            return cached;

        var row = Service.DataManager.GetExcelSheet<Weather>().GetRowOrDefault(weatherId);
        if (row is null)
            return null;

        var name = row.Value.Name.ExtractText();
        weatherNameCache[weatherId] = name;
        return name;
    }
}
