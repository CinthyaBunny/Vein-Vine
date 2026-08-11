using Dalamud.Configuration;
using VeinAndVine.Models;
using VeinAndVine.Services;

namespace VeinAndVine;

/// <summary>
/// Serialized to %AppData%\XIVLauncher\pluginConfigs\VeinAndVine.json by Dalamud
/// (Newtonsoft.Json, public fields and properties).
///
/// Bump <see cref="Version"/> and migrate in a Migrate() call if you ever
/// change the shape of this in a breaking way.
/// </summary>
[Serializable]
public class Configuration : IPluginConfiguration
{
    /// <summary>
    /// Bumped whenever a stored value changes meaning rather than just being
    /// added. <see cref="Migrate"/> is what acts on it.
    /// </summary>
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;

    public List<WishlistEntry> Wishlist { get; set; } = [];

    /// <summary>Inactive meaning waiting on weather or time, as opposed to always-up.</summary>
    public bool ShowInactiveNodes { get; set; } = true;

    public bool OpenMainWindowOnStartup { get; set; } = false;

    // Main-window job filter. Two bools rather than a JobFilter field so an
    // older config file that predates them still deserializes - a missing
    // property keeps its initializer, and both jobs on is the sane default.
    public bool ShowMiningNodes { get; set; } = true;
    public bool ShowBotanyNodes { get; set; } = true;

    public bool CurrentZoneOnly { get; set; } = false;
    public bool TimedNodesOnly { get; set; } = false;

    /// <summary>The toolbar clock reads local time instead of Eorzea time.</summary>
    public bool ClockShowsLocalTime { get; set; } = false;

    /// <summary>
    /// The node list's job column draws the game's gathering icon rather than
    /// MIN / BTN. A setting rather than a straight swap because three letters
    /// stay legible at a column width an icon does not.
    /// </summary>
    public bool ShowJobIcons { get; set; } = true;

    // Row metrics. Bounds are declared beside the values because the config is
    // a plain JSON file on disk: a hand-edited 40x row scale would produce a
    // list of one row and no way back to this screen, so every read clamps.
    public const float MinRowPaddingScale = 0.5f;
    public const float MaxRowPaddingScale = 2.0f;
    public const float MinLeadingRowScale = 1.0f;
    public const float MaxLeadingRowScale = 2.0f;

    /// <summary>
    /// The frame-padding multiplier that counts as normal, i.e. what the
    /// settings screen shows as 100%.
    ///
    /// A row wants appreciably more room around its contents than the window's
    /// own frame padding gives a button, so the neutral point is not 1. Folding
    /// that into a constant here keeps it out of the setting, where a default
    /// of 220% would invite the reasonable question of a percentage of what.
    /// </summary>
    public const float BaseRowPadding = 2.0f;

    // No text-size setting here on purpose. Scaling text per-window resamples
    // the font atlas rather than rendering at the new size, so it buys height
    // at the cost of sharpness. Row height gets the same legibility from
    // spacing and leaves the glyphs alone; a genuinely larger, still-crisp
    // typeface has to come from the font itself - see UiStyle's Axis handle.

    /// <summary>
    /// Row height, where 1 is the ordinary row and the real padding is this
    /// times <see cref="BaseRowPadding"/>. Everything in a row measures from
    /// the frame, so this carries the icons and buttons with it.
    /// </summary>
    public float RowPaddingScale { get; set; } = 1.0f;

    /// <summary>How much taller the row being waited on is than the rest.</summary>
    public float LeadingRowScale { get; set; } = 1.25f;

    // Appearance. The game's own look is the default: this is a plugin for a
    // game, shown next to that game's windows, and matching them is the less
    // surprising of the two options. Each axis is still one click from
    // Dalamud's default in the Appearance tab.
    //
    // Anyone who has already chosen Dalamud keeps it - the value is written to
    // the config file, so an explicit choice outranks a changed default. Only
    // a config that predates these settings picks the new one up.
    // Theme covers both the palette and the window frame: picking one of the
    // game's themes means wanting the whole look, and the frame is drawn from
    // the theme's own colours anyway, so splitting them only allowed
    // combinations nobody wanted.
    public UiFontChoice Font { get; set; } = UiFontChoice.GameAxis;
    public UiThemeChoice Theme { get; set; } = UiThemeChoice.Dark;

    public NodeListPlacement NodeListPlacement { get; set; } = NodeListPlacement.Tabbed;

    /// <summary>
    /// Width of the docked node list. Its height and position come from the
    /// main window, so this is the only part of its geometry worth keeping.
    /// </summary>
    public float DockedNodeListWidth { get; set; } = 420f;

    /// <summary>
    /// Brings a file written by an older build up to date. Called once on load,
    /// before anything reads a setting.
    ///
    /// Only for values whose *meaning* changed. A newly added setting needs
    /// nothing here: a property missing from the file keeps its initializer.
    /// </summary>
    public void Migrate()
    {
        if (Version >= CurrentVersion)
            return;

        // Version 2 re-based the row height. It was a raw frame-padding
        // multiplier, where an ordinary row was 2.2; it is now a multiple of an
        // ordinary row, where an ordinary row is 1. Carried across untouched, a
        // value written under the old meaning reads as about twice the height
        // it was chosen for.
        RowPaddingScale = 1.0f;

        Version = CurrentVersion;
        Save();
    }

    /// <summary>
    /// A method, not a property, so Dalamud's serializer doesn't write a
    /// derived value into the config file alongside the two bools it comes from.
    /// </summary>
    public JobFilter GetJobFilter()
    {
        var jobs = JobFilter.None;
        if (ShowMiningNodes) jobs |= JobFilter.Miner;
        if (ShowBotanyNodes) jobs |= JobFilter.Botanist;
        return jobs;
    }

    /// <summary>
    /// Writes the config to disk. Never throws.
    ///
    /// Every call site is inside an ImGui draw - a checkbox, a row toggle, a
    /// footer button. An exception escaping there would unwind out of the
    /// middle of a table or a tab bar, leaving ImGui's begin/end stack
    /// unbalanced for the rest of the frame, which is far worse than a
    /// preference that failed to persist.
    /// </summary>
    public void Save()
    {
        try
        {
            Service.PluginInterface.SavePluginConfig(this);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "Could not save the Vein & Vine configuration.");
        }
    }
}
