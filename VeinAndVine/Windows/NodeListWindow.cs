using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

/// <summary>
/// The node list as a panel pinned to the main window's left edge.
///
/// A concept rather than a finished docking system. ImGui has real docking,
/// but it is a user-driven arrangement, not something a plugin can impose on
/// two of its own windows - so this fakes the one arrangement that is actually
/// wanted: the list glued to the left of the window it belongs to.
///
/// The geometry is deliberately lopsided. Position and height are recomputed
/// from the main window every frame, so the panel follows it around and grows
/// with it. Width is the one dimension left to the player, because it is the
/// one that decides how much of the item and zone columns you can read.
///
/// Drag the grip in the panel's bottom-left corner to widen it. The right edge
/// stays welded to the main window, so widening runs leftwards into free
/// screen space rather than pushing the window it is attached to - which is
/// why the grip is on the left, on the corner that actually moves.
///
/// ImGui's own resizing is switched off rather than steered. Its grip is fixed
/// to the bottom-right, and the second grip it offers on the bottom-left only
/// appears when the user has turned on resize-from-edges globally - not
/// something a plugin should be depending on, or changing.
/// </summary>
public sealed class NodeListWindow : Window, IDisposable
{
    /// <summary>Narrow enough to be useless below this; the columns collapse.</summary>
    private const float MinimumWidth = 260f;

    private const float MaximumWidth = 800f;

    private readonly Plugin plugin;
    private readonly MainWindow host;
    private readonly NodeListTab nodeList;

    /// <summary>
    /// Live width, updated from the window every frame. Kept separate from the
    /// stored setting so a drag can be followed continuously and only written
    /// to disk once it finishes.
    /// </summary>
    private float width;

    public NodeListWindow(Plugin plugin, MainWindow host, NodeListTab nodeList)
        : base("Vein & Vine - Nodes###VeinAndVineNodes")
    {
        this.plugin = plugin;
        this.host = host;
        this.nodeList = nodeList;

        width = plugin.Configuration.DockedNodeListWidth;

        // No title bar, because the main window's is right next to it and two
        // of them reads as clutter. No move or collapse, because the position
        // is not the player's to set - it is derived from the host.
        Flags = BaseFlags;

        // Whether it is actually shown is DrawConditions' business; this just
        // means "not explicitly closed", and there is no close button to change
        // it.
        IsOpen = true;
    }

    private static ImGuiWindowFlags BaseFlags =>
        ImGuiWindowFlags.NoTitleBar |
        ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoCollapse |
        ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoDocking |
        ImGuiWindowFlags.NoResize;

    public void Dispose() { }

    /// <summary>
    /// Only shown while docked, and only while the host actually has a body on
    /// screen - a panel pinned to a collapsed or closed window has nothing to
    /// pin to, and would strand itself at the last place it saw it.
    /// </summary>
    public override bool DrawConditions() =>
        plugin.Configuration.NodeListPlacement == NodeListPlacement.DockedLeft &&
        host.IsOpen &&
        host.IsDrawing;

    public override void PreDraw()
    {
        plugin.UiStyle.PushWindowStyle();
        Flags = BaseFlags | plugin.UiStyle.ExtraWindowFlags;

        width = Math.Clamp(width, MinimumWidth, MaximumWidth);

        // Both axes are set outright now that ImGui's own resizing is off.
        // Nothing else writes the size, so there is no contention to design
        // around: the height comes from the host, the width from the grip.
        ImGui.SetNextWindowSize(new Vector2(width, host.LastSize.Y), ImGuiCond.Always);

        // Right edge welded to the host's left edge, so widening runs leftwards
        // into free space instead of shoving the window it belongs to. Always,
        // not FirstUseEver: the point is that it tracks the host rather than
        // remembering where it once sat.
        ImGui.SetNextWindowPos(
            new Vector2(host.LastPosition.X - width, host.LastPosition.Y), ImGuiCond.Always);
    }

    public override void PostDraw() => plugin.UiStyle.PopWindowStyle();

    public override void Draw()
    {
        plugin.UiStyle.DrawChrome();
        nodeList.Draw();

        DrawResizeGrip();
        PersistWidthWhenDragFinishes();
    }

    /// <summary>
    /// The bottom-left resize corner, drawn and driven by hand.
    ///
    /// Dragging left grows the panel, so the mouse delta is subtracted: the
    /// right edge is fixed, and the width is the distance the left edge has
    /// travelled away from it. The new width lands in next frame's position as
    /// well as its size, which is what keeps the right edge from drifting.
    /// </summary>
    private void DrawResizeGrip()
    {
        var size = 14f * ImGuiHelpers.GlobalScale;
        var windowPos = ImGui.GetWindowPos();
        var windowSize = ImGui.GetWindowSize();

        // Bottom-left corner of the window, in screen space.
        var corner = new Vector2(windowPos.X, windowPos.Y + windowSize.Y);

        ImGui.SetCursorScreenPos(new Vector2(corner.X, corner.Y - size));
        ImGui.InvisibleButton("##resize_grip", new Vector2(size, size));

        var hovered = ImGui.IsItemHovered();
        var active = ImGui.IsItemActive();

        if (hovered || active)
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNesw);

        if (active)
            width = Math.Clamp(width - ImGui.GetIO().MouseDelta.X, MinimumWidth, MaximumWidth);

        // Same triangle ImGui draws for its own grips, mirrored to this corner,
        // so it reads as a resize handle rather than a stray decoration.
        var colour = ImGui.GetColorU32(
            active ? ImGuiCol.ResizeGripActive :
            hovered ? ImGuiCol.ResizeGripHovered :
            ImGuiCol.ResizeGrip);

        ImGui.AddTriangleFilled(
            ImGui.GetWindowDrawList(),
            corner,
            new Vector2(corner.X + size, corner.Y),
            new Vector2(corner.X, corner.Y - size),
            colour);
    }

    /// <summary>
    /// Writes the width to the config once, when the drag ends. Saving on
    /// every frame of a drag would serialise the whole config to disk sixty
    /// times a second.
    /// </summary>
    private void PersistWidthWhenDragFinishes()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            return;

        var rounded = MathF.Round(Math.Clamp(width, MinimumWidth, MaximumWidth));
        if (MathF.Abs(rounded - plugin.Configuration.DockedNodeListWidth) < 0.5f)
            return;

        plugin.Configuration.DockedNodeListWidth = rounded;
        plugin.Configuration.Save();
    }
}
