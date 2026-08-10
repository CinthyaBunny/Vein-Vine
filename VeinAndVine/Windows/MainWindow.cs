using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Windowing;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly Vector4 ActiveColor = new(0.5f, 0.85f, 0.5f, 1f);
    private static readonly Vector4 InactiveColor = new(0.6f, 0.6f, 0.6f, 1f);

    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Vein & Vine###VeinAndVineMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 200),
            MaximumSize = new Vector2(600, 800),
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        if (plugin.NodeDatabase.Count == 0)
        {
            ImGui.TextDisabled("No node data loaded.");
            ImGui.TextDisabled($"Expected: Data/{NodeDatabase.DataFileName}");
            if (ImGui.SmallButton("Reload data"))
                plugin.ReloadNodeDatabase();
            ImGui.SameLine();
            if (ImGui.SmallButton("Settings"))
                plugin.ToggleConfigUi();
            return;
        }

        var results = plugin.PriorityEngine.BuildPriorityList(
            plugin.NodeDatabase,
            plugin.Configuration.Wishlist,
            GetPlayerPosition());

        if (results.Count == 0)
        {
            ImGui.TextDisabled("Your wishlist is empty. Add items in settings.");
            if (ImGui.SmallButton("Settings"))
                plugin.ToggleConfigUi();
            return;
        }

        foreach (var result in results)
        {
            if (!result.IsActive && !plugin.Configuration.ShowInactiveNodes)
                continue;

            ImGui.PushID((int)result.Node.ItemId);

            ImGui.TextColored(result.IsActive ? ActiveColor : InactiveColor, result.Node.ItemName);
            ImGui.SameLine();
            ImGui.TextDisabled($"  {result.Node.ZoneName}");

            if (result.DistanceFromPlayer is { } dist)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"  {dist:F0}y");
            }

            if (result.IsActive)
            {
                if (result.TimeRemaining is { } remaining)
                {
                    ImGui.SameLine();
                    ImGui.TextUnformatted($"  {remaining:hh\\:mm\\:ss} left");
                }

                if (ImGui.SmallButton("Set map flag"))
                    SetMapFlag(result.Node);
            }
            else
            {
                ImGui.TextDisabled($"  {result.BlockedReason}");
                if (result.TimeUntilActive is { } until)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"(in {until:hh\\:mm\\:ss})");
                }
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }

    /// <summary>
    /// Places a marker on the player's map. This is the plugin's only
    /// game-state-changing action - it does not move the player.
    /// </summary>
    private void SetMapFlag(Models.GatherNode node)
    {
        try
        {
            var payload = new MapLinkPayload(
                node.TerritoryTypeId,
                node.MapId,
                node.MapX,
                node.MapY);

            Service.GameGui.OpenMapWithMapLink(payload);
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to set map flag for {node.ItemName}.");
        }
    }

    /// <summary>
    /// Player position in map coordinates, so it can be compared against node
    /// data. Null when not logged in or the map can't be resolved.
    /// </summary>
    private static (float X, float Y, uint TerritoryTypeId)? GetPlayerPosition()
    {
        var player = Service.ObjectTable.LocalPlayer;
        if (player is null)
            return null;

        var territoryId = Service.ClientState.TerritoryType;
        var mapId = Service.ClientState.MapId;

        var mapCoords = MapUtil.WorldToMap(player.Position, mapId);
        if (mapCoords is null)
            return null;

        return (mapCoords.Value.X, mapCoords.Value.Y, territoryId);
    }
}
