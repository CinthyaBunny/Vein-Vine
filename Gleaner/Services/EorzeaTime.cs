namespace Gleaner.Services;

/// <summary>
/// Pure Eorzea clock and weather-seed math. No Dalamud, no game state, no
/// I/O - every method here is a function of a Unix timestamp, which makes the
/// whole thing trivially unit testable.
/// </summary>
public static class EorzeaTime
{
    /// <summary>
    /// Real seconds per Eorzea hour. One Eorzea day is 70 real minutes (4200s)
    /// and 86400 Eorzea seconds, so Eorzea time runs at exactly
    /// 86400/4200 = 3600/175 real time.
    ///
    /// Everything here divides by this integer rather than multiplying by that
    /// ratio, and it must stay that way. Expressed as a scale factor the ratio
    /// is 20.571428..., and rounding it to 20.5716 before multiplying a modern
    /// Unix timestamp (~1.7e9) drifts by roughly 300,000 Eorzea seconds - about
    /// half a day off in the reported hour.
    /// </summary>
    public const long RealSecondsPerEorzeaHour = 175;

    public const long RealSecondsPerEorzeaDay = 4200;

    /// <summary>
    /// Weather is rolled once per 8 Eorzea hours, i.e. every 1400 real
    /// seconds, three times per Eorzea day.
    /// </summary>
    public const long RealSecondsPerWeatherWindow = 1400;

    public static long CurrentUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static int GetEorzeaHour(long unixSeconds) =>
        (int)(unixSeconds / RealSecondsPerEorzeaHour % 24);

    public static int GetEorzeaMinute(long unixSeconds) =>
        (int)(unixSeconds * 60 / RealSecondsPerEorzeaHour % 60);

    public static int GetCurrentEorzeaHour() => GetEorzeaHour(CurrentUnixSeconds());

    /// <summary>
    /// The Eorzea clock at a real timestamp. Both halves come from the one
    /// timestamp deliberately: read separately they can straddle a tick and
    /// report 12:59 as 12:00.
    /// </summary>
    public static (int Hour, int Minute) EorzeaClockAt(long unixSeconds) =>
        (GetEorzeaHour(unixSeconds), GetEorzeaMinute(unixSeconds));

    public static (int Hour, int Minute) CurrentEorzeaClock() => EorzeaClockAt(CurrentUnixSeconds());

    /// <summary>
    /// The Eorzea clock as it will read once <paramref name="ahead"/> of real
    /// time has passed - what a countdown running out lands on.
    /// </summary>
    public static (int Hour, int Minute) EorzeaClockIn(TimeSpan ahead) =>
        EorzeaClockAt(CurrentUnixSeconds() + (long)ahead.TotalSeconds);

    public static long GetWeatherWindowStart(long unixSeconds) =>
        unixSeconds - FloorMod(unixSeconds, RealSecondsPerWeatherWindow);

    public static long GetWeatherWindowEnd(long unixSeconds) =>
        GetWeatherWindowStart(unixSeconds) + RealSecondsPerWeatherWindow;

    /// <summary>
    /// The deterministic 0-99 roll that selects weather for a window. Every
    /// zone shares this roll; zones differ only in how their rate table maps
    /// the roll onto a weather type.
    ///
    /// This is the long-documented community algorithm: derive a value from
    /// the Eorzea day plus the 8-hour block index, then hash it.
    /// </summary>
    public static uint CalculateWeatherChance(long unixSeconds)
    {
        // Which 8-hour block of the Eorzea day we're in, as 0 / 8 / 16.
        var totalEorzeaHours = unixSeconds / RealSecondsPerEorzeaHour;
        var increment = (uint)((totalEorzeaHours + 8 - totalEorzeaHours % 8) % 24);

        var totalEorzeaDays = (uint)(unixSeconds / RealSecondsPerEorzeaDay);

        var calcBase = totalEorzeaDays * 100u + increment;

        var step1 = (calcBase << 11) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;

        return step2 % 100u;
    }

    /// <summary>
    /// Real seconds until the Eorzea clock next reaches <paramref name="targetHour"/>.
    /// Returns 0 if we are exactly on it.
    /// </summary>
    public static long RealSecondsUntilEorzeaHour(long unixSeconds, int targetHour)
    {
        var secondsIntoDay = FloorMod(unixSeconds, RealSecondsPerEorzeaDay);
        var targetSeconds = (long)targetHour * RealSecondsPerEorzeaHour;
        var delta = targetSeconds - secondsIntoDay;
        if (delta < 0)
            delta += RealSecondsPerEorzeaDay;
        return delta;
    }

    /// <summary>Modulo that is always non-negative, unlike C#'s % on negatives.</summary>
    private static long FloorMod(long value, long modulus)
    {
        var r = value % modulus;
        return r < 0 ? r + modulus : r;
    }
}
