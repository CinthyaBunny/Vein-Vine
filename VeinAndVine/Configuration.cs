using Dalamud.Configuration;
using VeinAndVine.Models;

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

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
