using System.Numerics;
using Dalamud.Bindings.ImGui;
using VeinAndVine.Models;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

/// <summary>
/// Presentation bits both lists need: job and duration formatting, shared
/// tooltip and icon drawing, and the bridge between an ImGui table's sort specs
/// and <see cref="NodeSort"/>.
/// </summary>
internal static class UiShared
{
    /// <summary>
    /// The flags every list in the plugin is built with. Shared so the node
    /// list and the item picker cannot drift into behaving differently - they
    /// are the same kind of object and should sort, resize and scroll alike.
    /// </summary>
    public const ImGuiTableFlags TableFlags =
        ImGuiTableFlags.Resizable |
        ImGuiTableFlags.Reorderable |
        ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Sortable |
        ImGuiTableFlags.RowBg |
        ImGuiTableFlags.BordersInnerV |
        ImGuiTableFlags.ScrollY |
        ImGuiTableFlags.SizingStretchProp;

    /// <summary>Anything opening within this long is coloured as "soon".</summary>
    public static readonly TimeSpan SoonThreshold = TimeSpan.FromMinutes(5);

    // Clock tuning, gathered so it can be adjusted by eye without reading the
    // drawing below - the same reason GameTabBar keeps its shape constants
    // together. Where the clock sits is the caller's decision, so moving it
    // means calling DrawClock somewhere else, not editing it.

    /// <summary>
    /// Font scale for the clock, 1f being the window's ordinary text. This
    /// scales the button along with its text, so raising it makes the whole
    /// toolbar row taller.
    ///
    /// Deliberately not a const: at 1f the compiler folds the guards below into
    /// dead code and warns, which is a poor thing to greet the next person who
    /// wants to try 1.3f.
    /// </summary>
    private static readonly float ClockScale = 1f;

    /// <summary>
    /// The widest text the clock can hold. Its width is fixed to this so the
    /// toolbar does not shift as the digits tick or the mode is switched -
    /// which is also why both labels are two characters.
    /// </summary>
    private const string ClockSample = "ET 00:00";

    /// <summary>
    /// How long the clock ignores a repeat click. A label that flickers under a
    /// double-click reads as a fault rather than a feature, and nothing here is
    /// worth switching twice inside a third of a second.
    /// </summary>
    private const double ClockToggleCooldown = 0.3;

    /// <summary>
    /// When the clock last changed mode, on ImGui's own clock.
    ///
    /// Static because exactly one clock is ever on screen: the Nodes tab and the
    /// docked panel are the same list and never draw together. A second one
    /// would share this cooldown rather than get its own.
    /// </summary>
    private static double lastClockToggle;

    /// <summary>
    /// Width the clock will occupy, for callers that need to place it before
    /// drawing it - <see cref="RightAlign"/> being the reason this exists.
    /// </summary>
    public static float ClockWidth() =>
        (ImGui.CalcTextSize(ClockSample).X * ClockScale) + (ImGui.GetStyle().FramePadding.X * 2f);

    /// <summary>
    /// The clock, as a button that swaps between Eorzea and local time.
    ///
    /// The node list mixes two clocks and says so nowhere: the Windows column is
    /// Eorzea hours, while "4m30s left" and "in 12m" are real minutes. A window
    /// of 12-14 means nothing without the time in the same units, and the local
    /// reading turns "in 12m" into a wall-clock answer to whether there is time
    /// to do something else first.
    /// </summary>
    public static void DrawClock(Plugin plugin)
    {
        var showLocal = plugin.Configuration.ClockShowsLocalTime;

        string text;
        if (showLocal)
        {
            var now = DateTime.Now;
            text = $"LT {now.Hour:00}:{now.Minute:00}";
        }
        else
        {
            var (hour, minute) = EorzeaTime.CurrentEorzeaClock();
            text = $"ET {hour:00}:{minute:00}";
        }

        // Scaling is per-window in ImGui, so it has to be put back immediately
        // or every widget drawn afterwards inherits it.
        if (ClockScale != 1f)
            ImGui.SetWindowFontScale(ClockScale);

        // The trailing ### fixes the widget's identity. Without it the label is
        // the id, so the button would become a different widget every minute
        // and lose whatever interaction was in flight across that frame.
        var clicked = ImGui.Button($"{text}###veinandvine_clock", new Vector2(ClockWidth(), 0));

        if (ClockScale != 1f)
            ImGui.SetWindowFontScale(1f);

        Tooltip(
            (showLocal ? "Your local time. Click for Eorzea time." : "Eorzea time. Click for local time.") +
            "\n\nThe Windows column is in Eorzea hours; the countdowns beside\n" +
            "them are in real minutes.");

        if (!clicked)
            return;

        // ImGui's clock rather than the system's: it is monotonic and already
        // ticking per frame, so a cooldown cannot be skipped or stranded by the
        // wall clock being adjusted underneath it.
        var pressedAt = ImGui.GetTime();
        if (pressedAt - lastClockToggle < ClockToggleCooldown)
            return;

        lastClockToggle = pressedAt;
        plugin.Configuration.ClockShowsLocalTime = !showLocal;
        plugin.Configuration.Save();
    }

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
