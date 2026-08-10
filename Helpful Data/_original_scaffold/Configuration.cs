using Dalamud.Configuration;
using VeinAndVine.Models;

namespace VeinAndVine;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public List<WishlistEntry> Wishlist { get; set; } = [];

    public bool ShowInactiveNodes { get; set; } = true;
}
