using Dalamud.Interface.Windowing;
using VeinAndVine.Services;
using ImGuiNET;

namespace VeinAndVine.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin) : base("Vein and Vine###VeinAndVineMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new(320, 200),
            MaximumSize = new(600, 800),
        };
    }

    public override void Draw()
    {
        var results = plugin.PriorityEngine.BuildPriorityList(
            plugin.NodeDatabase,
            plugin.Configuration.Wishlist);

        if (results.Count == 0)
        {
            ImGui.TextDisabled("Your wishlist is empty. Add items in settings.");
            return;
        }

        foreach (var result in results)
        {
            if (!result.IsActive && !plugin.Configuration.ShowInactiveNodes)
                continue;

            ImGui.PushID((int)result.Node.ItemId);

            var color = result.IsActive
                ? new System.Numerics.Vector4(0.5f, 0.85f, 0.5f, 1f)
                : new System.Numerics.Vector4(0.6f, 0.6f, 0.6f, 1f);

            ImGui.TextColored(color, result.Node.ItemName);
            ImGui.SameLine();
            ImGui.TextDisabled($"  {result.Node.ZoneName}");

            if (result.DistanceFromPlayer is { } dist)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"  {dist:F0}y");
            }

            if (result.TimeRemaining is { } remaining)
            {
                ImGui.SameLine();
                ImGui.Text($"  {remaining:mm\\:ss}");
            }

            if (result.IsActive && ImGui.SmallButton("Set map flag"))
            {
                // TODO: call the game's native map-flag API here, e.g. via
                // Dalamud's Map Marker / gaming.MapLinkPayload facilities.
                // This places a marker only - it does not move the player.
            }

            ImGui.Separator();
            ImGui.PopID();
        }
    }
}
