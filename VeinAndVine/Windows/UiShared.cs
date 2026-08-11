using System.Numerics;
using Dalamud.Bindings.ImGui;
using VeinAndVine.Models;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

/// <summary>
/// Presentation bits both windows need: the colour vocabulary, job and
/// duration formatting, and the bridge between an ImGui table's sort specs and
/// <see cref="NodeSort"/>.
/// </summary>
internal static class UiShared
{
    // The status colours are no longer constants: green on a cream panel is
    // not the same green that works on a black one. They come from UiStyle,
    // which keeps each theme's hue but fits its lightness to that theme's
    // panel. See UiStyle.Legible.

    /// <summary>Up now.</summary>
    public static Vector4 ActiveColor(Plugin plugin) => plugin.UiStyle.StatusUp;

    /// <summary>Not up, but soon - worth walking towards.</summary>
    public static Vector4 SoonColor(Plugin plugin) => plugin.UiStyle.StatusSoon;

    /// <summary>Not up, or up permanently and so never urgent.</summary>
    public static Vector4 InactiveColor(Plugin plugin) => plugin.UiStyle.StatusIdle;

    /// <summary>Anything opening within this long is coloured as "soon".</summary>
    public static readonly TimeSpan SoonThreshold = TimeSpan.FromMinutes(5);

    public static string JobLabel(NodeType type) => type switch
    {
        NodeType.Mining => "MIN",
        NodeType.Botany => "BTN",
        NodeType.Fishing => "FSH",
        _ => "?",
    };

    /// <summary>
    /// Label for a set of jobs. Plenty of items are gatherable by both, and
    /// showing only one of them in a list you can filter by job is how an item
    /// goes missing.
    /// </summary>
    public static string JobLabel(JobFilter jobs)
    {
        var parts = new List<string>(3);
        if ((jobs & JobFilter.Miner) != 0) parts.Add("MIN");
        if ((jobs & JobFilter.Botanist) != 0) parts.Add("BTN");
        if ((jobs & JobFilter.Fisher) != 0) parts.Add("FSH");

        return parts.Count == 0 ? "?" : string.Join("+", parts);
    }

    /// <summary>
    /// Compact enough for a table cell: "1h04m", "6m30s", "12s". Seconds are
    /// dropped past an hour, where they'd be noise.
    /// </summary>
    public static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h{span.Minutes:00}m";

        return span.TotalMinutes >= 1
            ? $"{span.Minutes}m{span.Seconds:00}s"
            : $"{span.Seconds}s";
    }

    /// <summary>
    /// Column user id for a sortable column. Offset by one because ImGui uses
    /// 0 to mean "this column was never given a user id", which would otherwise
    /// be indistinguishable from <see cref="NodeSort.Priority"/>.
    /// </summary>
    public static uint SortId(NodeSort sort) => (uint)sort + 1;

    /// <summary>
    /// Pulls the current sort out of the table being drawn. Call it inside
    /// BeginTable/EndTable, after the columns are set up.
    ///
    /// ImGui owns this state and persists it in its own ini, so there is no
    /// copy in our config to fall out of step with the header arrows.
    /// </summary>
    public static void ReadSortSpecs(ref NodeSort sort, ref bool descending)
    {
        var specs = ImGui.TableGetSortSpecs();
        if (specs.IsNull || specs.SpecsCount == 0)
            return;

        // Single-column sort only (no SortMulti), so the first spec is the
        // whole story.
        var column = specs.Specs;
        if (column.IsNull || column.ColumnUserID == 0)
            return;

        var candidate = (NodeSort)(column.ColumnUserID - 1);
        if (!Enum.IsDefined(candidate))
            return;

        sort = candidate;
        descending = column.SortDirection == ImGuiSortDirection.Descending;
        specs.SpecsDirty = false;
    }

    /// <summary>
    /// Draws an item's icon at <paramref name="size"/> square, followed by
    /// SameLine.
    ///
    /// Always occupies the space, even with nothing to draw: icons stream in
    /// asynchronously, and letting the row collapse to nothing until the
    /// texture arrives makes the whole list twitch on first paint.
    /// </summary>
    public static void DrawItemIcon(Plugin plugin, uint iconId, float size)
    {
        var icon = plugin.ItemInfo.GetIcon(iconId);

        if (icon is not null)
            ImGui.Image(icon.Handle, new Vector2(size, size));
        else
            ImGui.Dummy(new Vector2(size, size));

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
    }

    /// <summary>
    /// The shared top of an item tooltip: icon, name, and the item's own
    /// in-game description.
    /// </summary>
    public static void DrawItemTooltipHeader(Plugin plugin, uint itemId, uint iconId, string itemName)
    {
        DrawItemIcon(plugin, iconId, ImGui.GetFontSize() * 2f);

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(itemName);

        if (plugin.ItemInfo.GetDescription(itemId) is { } description)
        {
            ImGui.Separator();

            // Tooltips size to their content, so an unwrapped description
            // would stretch one to the width of the screen.
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 24f);
            ImGui.TextWrapped(description);
            ImGui.PopTextWrapPos();
        }
    }

    /// <summary>
    /// What a gatherer needs to actually get the full yield. Draws nothing
    /// when the node asks for nothing in particular.
    ///
    /// Spelled out rather than drawn as star glyphs - the game font has no
    /// reliable star, and a row of missing-glyph boxes reads worse than words.
    /// </summary>
    public static void DrawGatheringRequirements(int perceptionRequired, int stars)
    {
        if (stars > 0)
            ImGui.TextDisabled($"{stars}-star node");

        if (perceptionRequired > 0)
            ImGui.TextDisabled($"Perception {perceptionRequired} for the full yield");
    }

    /// <summary>Tooltip for the widget just submitted.</summary>
    public static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(text);
    }

    /// <summary>
    /// Moves the cursor so the next <paramref name="width"/>-wide widget ends
    /// flush with the right edge of the content region.
    /// </summary>
    public static void RightAlign(float width)
    {
        var target = ImGui.GetContentRegionMax().X - width;
        if (target > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(target);
    }
}
