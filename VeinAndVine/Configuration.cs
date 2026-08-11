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
    public int Version { get; set; } = 1;

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
