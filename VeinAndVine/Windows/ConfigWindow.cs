using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using VeinAndVine.Models;

namespace VeinAndVine.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    private string filter = string.Empty;

    public ConfigWindow(Plugin plugin) : base("Vein & Vine Settings###VeinAndVineConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 320),
            MaximumSize = new Vector2(700, 900),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("##veinandvine_config_tabs"))
        {
            if (ImGui.BeginTabItem("Wishlist"))
            {
                DrawWishlistTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Display"))
            {
                DrawDisplayTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawWishlistTab()
    {
        if (plugin.NodeDatabase.Count == 0)
        {
            ImGui.TextWrapped(
                "No node data loaded, so there is nothing to pick from. Drop a " +
                $"dataset at Data/{Services.NodeDatabase.DataFileName} next to the plugin DLL.");
            if (ImGui.Button("Reload data"))
                plugin.ReloadNodeDatabase();
            return;
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Filter items...", ref filter, 128);

        ImGui.Separator();

        // One row per distinct item in the dataset; an item can have several
        // nodes, and the wishlist tracks the item, not the node.
        var items = plugin.NodeDatabase
            .GroupBy(n => n.ItemId)
            .Select(g => g.First())
            .Where(n => filter.Length == 0 ||
                        n.ItemName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n.ItemName)
            .ToList();

        if (ImGui.BeginChild("##wishlist_items", new Vector2(0, -ImGui.GetFrameHeightWithSpacing())))
        {
            foreach (var node in items)
            {
                ImGui.PushID((int)node.ItemId);

                var entry = configuration.Wishlist.FirstOrDefault(w => w.ItemId == node.ItemId);
                var tracked = entry is not null;

                if (ImGui.Checkbox($"{node.ItemName}##track", ref tracked))
                {
                    if (tracked)
                    {
                        configuration.Wishlist.Add(new WishlistEntry
                        {
                            ItemId = node.ItemId,
                            Label = node.ItemName,
                        });
                    }
                    else if (entry is not null)
                    {
                        configuration.Wishlist.Remove(entry);
                    }

                    configuration.Save();
                }

                ImGui.SameLine();
                ImGui.TextDisabled($"  {node.Type}  Lv{node.JobLevelRequired}  {node.ZoneName}");

                ImGui.PopID();
            }

            ImGui.EndChild();
        }

        ImGui.Separator();
        ImGui.TextDisabled($"{configuration.Wishlist.Count} item(s) tracked.");
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear all"))
        {
            configuration.Wishlist.Clear();
            configuration.Save();
        }
    }

    private void DrawDisplayTab()
    {
        var showInactive = configuration.ShowInactiveNodes;
        if (ImGui.Checkbox("Show nodes waiting on weather or time", ref showInactive))
        {
            configuration.ShowInactiveNodes = showInactive;
            configuration.Save();
        }

        var openOnStartup = configuration.OpenMainWindowOnStartup;
        if (ImGui.Checkbox("Open main window on startup", ref openOnStartup))
        {
            configuration.OpenMainWindowOnStartup = openOnStartup;
            configuration.Save();
        }

        ImGui.Separator();

        var hour = plugin.WeatherService.CurrentEorzeaHour;
        ImGui.TextDisabled($"Eorzea time: {hour:00}:xx");

        var territory = Service.ClientState.TerritoryType;
        if (territory != 0)
        {
            var weather = plugin.WeatherService.GetCurrentWeather(territory) ?? "unknown";
            ImGui.TextDisabled($"Weather here: {weather}");
        }
    }
}
