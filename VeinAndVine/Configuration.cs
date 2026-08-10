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

    /// <summary>Items the player is tracking.</summary>
    public List<WishlistEntry> Wishlist { get; set; } = [];

    /// <summary>Show nodes that are waiting on weather or time, greyed out.</summary>
    public bool ShowInactiveNodes { get; set; } = true;

    public bool OpenMainWindowOnStartup { get; set; } = false;

    // Main-window job filter. Two bools rather than a JobFilter field so an
    // older config file that predates them still deserializes - a missing
    // property keeps its initializer, and both jobs on is the sane default.
    public bool ShowMiningNodes { get; set; } = true;
    public bool ShowBotanyNodes { get; set; } = true;

    /// <summary>Limit the main window to the zone the player is standing in.</summary>
    public bool CurrentZoneOnly { get; set; } = false;

    /// <summary>Hide always-up nodes, leaving only the ones with a spawn window.</summary>
    public bool TimedNodesOnly { get; set; } = false;

    /// <summary>
    /// The job toggles as the filter the engine wants. A method, not a
    /// property, so Dalamud's serializer doesn't write a derived value into the
    /// config file alongside the two bools it's derived from.
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
