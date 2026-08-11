using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using VeinAndVine.Models;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

/// <summary>
/// The node list: what is up now, what is coming, and where. The plugin's
/// reason for existing, and the first tab of <see cref="MainWindow"/>.
///
/// Not a Window in its own right - the host window owns the frame, the theme
/// and the font, so this only ever draws into whatever region it is given.
/// </summary>
public sealed class NodeListTab
{
    private readonly Plugin plugin;

    // Mirrors the table's own sort state, which ImGui persists in its ini. Kept
    // here only so the list can be ordered before the rows are emitted.
    private NodeSort sort = NodeSort.Priority;
    private bool sortDescending;

    public NodeListTab(Plugin plugin) => this.plugin = plugin;

    public void Draw()
    {
        if (plugin.NodeDatabase.Count == 0)
        {
            DrawNoDataset();
            return;
        }

        var config = plugin.Configuration;
        var player = GetPlayerPosition();

        DrawToolbar(player);

        // Counted off the wishlist rather than the results, so an empty list
        // can tell "you haven't picked anything" apart from "your filters hide
        // everything you picked" - they need opposite advice.
        var trackedCount = config.Wishlist.Count(w => w.Enabled);
        if (trackedCount == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped("Nothing tracked yet. Pick the items you're hunting for and they'll show up here.");
            ImGui.Spacing();
            if (ImGui.Button("Pick items to track"))
                plugin.OpenWishlist();
            return;
        }

        var results = plugin.PriorityEngine.BuildPriorityList(
            plugin.NodeDatabase,
            config.Wishlist,
            player,
            BuildFilter(player));

        var visible = config.ShowInactiveNodes
            ? results
            : results.Where(r => r.IsActive).ToList();

        DrawSummary(results, trackedCount);

        if (visible.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled(results.Count == 0
                ? $"None of your {trackedCount} tracked item(s) match the filters above."
                : "Nothing is up right now. Tick \"Upcoming\" to see what's next.");
            return;
        }

        DrawTable(visible, player?.TerritoryTypeId);
    }

    private void DrawNoDataset()
    {
        ImGui.TextDisabled("No node data loaded.");
        ImGui.TextDisabled($"Expected: Data/{NodeDatabase.DataFileName}");
        if (ImGui.SmallButton("Reload data"))
            plugin.ReloadNodeDatabase();
    }

    private void DrawToolbar((float X, float Y, uint TerritoryTypeId)? player)
    {
        var config = plugin.Configuration;
        var dirty = false;

        var mining = config.ShowMiningNodes;
        if (ImGui.Checkbox("Miner", ref mining))
        {
            config.ShowMiningNodes = mining;
            dirty = true;
        }

        ImGui.SameLine();
        var botany = config.ShowBotanyNodes;
        if (ImGui.Checkbox("Botanist", ref botany))
        {
            config.ShowBotanyNodes = botany;
            dirty = true;
        }

        ImGui.SameLine();
        ImGui.TextDisabled("|");

        ImGui.SameLine();
        var timedOnly = config.TimedNodesOnly;
        if (ImGui.Checkbox("Timed only", ref timedOnly))
        {
            config.TimedNodesOnly = timedOnly;
            dirty = true;
        }

        UiShared.Tooltip(
            "Hide nodes that are always up, leaving only the ones with a\n" +
            "spawn window. Most of the dataset is always up, so this is the\n" +
            "switch between \"where do I get this\" and \"what's up now\".");

        ImGui.SameLine();
        var showInactive = config.ShowInactiveNodes;
        if (ImGui.Checkbox("Upcoming", ref showInactive))
        {
            config.ShowInactiveNodes = showInactive;
            dirty = true;
        }

        UiShared.Tooltip("Also list nodes that aren't up yet, with a countdown.");

        ImGui.SameLine();
        ImGui.BeginDisabled(player is null);
        var zoneOnly = config.CurrentZoneOnly;
        if (ImGui.Checkbox("This zone", ref zoneOnly))
        {
            config.CurrentZoneOnly = zoneOnly;
            dirty = true;
        }

        UiShared.Tooltip(player is null
            ? "Needs a logged-in character."
            : "Only nodes in the zone you're standing in.");
        ImGui.EndDisabled();

        // Clock and cog share the right end of the toolbar, so the reserved
        // width is both of them plus the gap between.
        UiShared.RightAlign(
            UiShared.ClockWidth() + ImGui.GetStyle().ItemSpacing.X + ImGui.GetFrameHeight());

        UiShared.DrawClock(plugin);

        ImGui.SameLine();
        if (ImGuiComponents.IconButton("##pick", FontAwesomeIcon.Cog))
            plugin.OpenWishlist();

        UiShared.Tooltip("Pick items to track");

        if (dirty)
            config.Save();

        ImGui.Separator();
    }

    /// <summary>
    /// The one line worth reading at a glance. "Up now" counts only timed
    /// nodes - always-up ones would inflate it into a number that never
    /// changes and never means anything.
    /// </summary>
    private void DrawSummary(IReadOnlyList<PriorityResult> results, int trackedCount)
    {
        var timedUp = results.Count(r => r.IsActive && !r.IsAlwaysAvailable);
        var always = results.Count(r => r.IsAlwaysAvailable);

        if (timedUp > 0)
        {
            ImGui.TextColored(plugin.UiStyle.StatusUp, $"{timedUp} up now");
        }
        else
        {
            var next = results
                .Where(r => r.TimeUntilActive is not null)
                .MinBy(r => r.TimeUntilActive!.Value);

            if (next?.TimeUntilActive is { } until)
                ImGui.TextColored(plugin.UiStyle.StatusSoon,
                    $"Next: {next.Node.ItemName} in {UiShared.FormatDuration(until)}");
            else
                ImGui.TextDisabled("Nothing timed to wait for");
        }

        ImGui.SameLine();
        ImGui.TextDisabled(always > 0
            ? $"  -  {always} always up  -  {results.Count} node(s) from {trackedCount} tracked item(s)"
            : $"  -  {results.Count} node(s) from {trackedCount} tracked item(s)");
    }

    private void DrawTable(IReadOnlyList<PriorityResult> results, uint? playerTerritory)
    {
        var height = ImGui.GetContentRegionAvail().Y;
        if (height <= 0)
            return;

        if (!ImGui.BeginTable("##veinandvine_nodes", 8, UiShared.TableFlags, new Vector2(0, height)))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        // Item first because its Selectable spans the row: ImGui anchors a
        // span-all-columns item at the column it's submitted in, so putting it
        // anywhere else would leave the columns before it out of the hit box.
        ImGui.TableSetupColumn("Item",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide, 3f, UiShared.SortId(NodeSort.ItemName));
        // Wide enough for either occupant: three letters, or a row-height icon
        // once the roomier row padding is in effect.
        ImGui.TableSetupColumn("Job",
            ImGuiTableColumnFlags.WidthFixed,
            Math.Max(38f * scale, UiShared.PredictedRowHeight(plugin, leading: true) + (ImGui.GetStyle().CellPadding.X * 2f)),
            UiShared.SortId(NodeSort.Job));
        ImGui.TableSetupColumn("Lv",
            ImGuiTableColumnFlags.WidthFixed, 28f * scale, UiShared.SortId(NodeSort.Level));
        ImGui.TableSetupColumn("Zone",
            ImGuiTableColumnFlags.WidthStretch, 2f, UiShared.SortId(NodeSort.Zone));
        // Map coordinates, not world ones - the pair a chat map link quotes, so
        // they can be read straight across to the in-game map. Unsortable
        // because an ordering on them would not mean anything.
        // Sized for the widest the dataset actually gets - "25.4, 20.4", ten
        // characters - plus the cell padding, rather than for the format.
        ImGui.TableSetupColumn("Coords",
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 80f * scale);
        ImGui.TableSetupColumn("Status",
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.DefaultSort,
            110f * scale, UiShared.SortId(NodeSort.Priority));
        ImGui.TableSetupColumn("Dist",
            ImGuiTableColumnFlags.WidthFixed, 46f * scale, UiShared.SortId(NodeSort.Distance));
        // Row height is the icon button's own size once the row padding is
        // pushed; the cell padding keeps the column border off it rather than
        // clipping its right edge.
        ImGui.TableSetupColumn("##flag",
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort | ImGuiTableColumnFlags.NoHeaderLabel,
            UiShared.PredictedRowHeight(plugin, leading: true) + (ImGui.GetStyle().CellPadding.X * 2f));

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        UiShared.ReadSortSpecs(ref sort, ref sortDescending);

        // After the header row, so the headers keep the window's ordinary
        // metrics while the rows below them get room to breathe.
        UiShared.PushRowStyle(plugin);

        // EndTable in a finally so a throw from a single row cannot unwind past
        // it. Dalamud would log the exception either way, but leaving ImGui's
        // begin/end stack unbalanced mid-frame takes the game client down with
        // it - a bad row should cost a log line, not the session.
        try
        {
            var ordered = PriorityEngine.Sort(results, sort, sortDescending);

            // Nothing worth pointing out when the list is already restricted to
            // the current zone: every row would qualify, and a mark every row
            // carries is not a mark.
            var hereTerritory = plugin.Configuration.CurrentZoneOnly ? null : playerTerritory;

            // Only the priority order groups always-up nodes apart. An explicit
            // column sort does what its header says and nothing else, so it gets
            // no bands - they would read as the sort having been overridden.
            var grouped = sort == NodeSort.Priority;
            var alwaysCount = grouped ? ordered.Count(r => r.IsAlwaysAvailable) : 0;

            // Detected at the boundary rather than by drawing two lists, so this
            // holds however the groups are ordered - descending priority puts
            // the always-up band first.
            bool? previousGroup = null;

            // Only ascending priority has a "first" worth emphasising. Reversed,
            // the leading timed row is the least urgent one; under a column sort
            // it is whatever the alphabet put there.
            var emphasise = grouped && !sortDescending;
            var seenTimed = false;

            foreach (var result in ordered)
            {
                if (grouped && previousGroup is { } previous && previous != result.IsAlwaysAvailable)
                {
                    DrawSectionRow(result.IsAlwaysAvailable
                        ? $"Always available ({alwaysCount})"
                        : $"Timed ({ordered.Count - alwaysCount})");
                }

                previousGroup = result.IsAlwaysAvailable;

                var leading = emphasise && !seenTimed && !result.IsAlwaysAvailable;
                seenTimed |= !result.IsAlwaysAvailable;

                // Everything in a row measures from the frame, so widening the
                // padding for one row is enough - the icons, the flag button and
                // the hit box all follow without being told.
                //
                // Popped in a finally of its own: the outer one below balances a
                // single push, so a row throwing while this second one was up
                // would leave ImGui's style stack short by one for the rest of
                // the frame.
                if (leading)
                    UiShared.PushLeadingRow(plugin);

                try
                {
                    DrawRow(result, hereTerritory);
                }
                finally
                {
                    // Only the nested padding comes off here. Popping the whole
                    // row style would take the text scale with it and shrink
                    // every row after this one.
                    if (leading)
                        UiShared.PopLeadingRow();
                }
            }
        }
        finally
        {
            UiShared.PopRowStyle(plugin);
            ImGui.EndTable();
        }
    }

    /// <summary>
    /// A labelled band marking where one group of rows ends and the next
    /// begins. The whole row is tinted rather than ruled off, because a
    /// Separator inside a table cell spans only that cell.
    /// </summary>
    private static void DrawSectionRow(string label)
    {
        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
        ImGui.TableNextColumn();
        ImGui.TextDisabled(label);
    }

    private void DrawRow(PriorityResult result, uint? hereTerritory)
    {
        var node = result.Node;

        ImGui.TableNextRow();

        // An always-up node in the zone you are standing in can be collected
        // now, without travelling for it - which is the one thing about a row
        // that never expires still worth saying. Timed rows are left alone:
        // their colour already reports whether they are worth acting on.
        //
        // Marked rather than reordered. Being underfoot is a note about a row,
        // not a claim that it outranks the rows above it.
        var isHere =
            result.IsAlwaysAvailable &&
            hereTerritory is { } here &&
            node.TerritoryTypeId == here;

        if (isHere)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg1, ImGui.GetColorU32(ImGuiCol.Header, 0.45f));

        // Keyed on the node, not the item: the same item is gatherable from
        // several nodes, and an ItemId-only key would give two rows the same
        // ImGui id, so clicking one row's button would act on the other.
        ImGui.PushID($"{node.GatheringPointBaseId}:{node.ItemId}");

        // Null means "leave the text its ordinary colour": an always-up node is
        // gatherable but not urgent, and painting it the same green as a node
        // that expires in four minutes would drown out the ones that matter.
        var color = result switch
        {
            { IsAlwaysAvailable: true } => (Vector4?)null,
            { IsActive: true } => plugin.UiStyle.StatusUp,
            { TimeUntilActive: { } until } when until <= UiShared.SoonThreshold => plugin.UiStyle.StatusSoon,
            _ => plugin.UiStyle.StatusIdle,
        };

        ImGui.TableNextColumn();

        // The row-spanning Selectable is submitted first and the icon and name
        // drawn back over it. Its hover highlight is a filled rect added to the
        // draw list at the point of submission, so an icon drawn beforehand
        // would sit underneath it and wash out blue exactly when you are
        // looking at it.
        var rowStart = ImGui.GetCursorPos();

        // Sized to the row rather than left at its default text height, which
        // covered a band across the top and left the rest of the row dead to
        // the double-click that sets the map flag.
        ImGui.Selectable("##row", false,
            ImGuiSelectableFlags.SpanAllColumns |
            ImGuiSelectableFlags.AllowItemOverlap |
            ImGuiSelectableFlags.AllowDoubleClick,
            new Vector2(0, ImGui.GetFrameHeight()));

        var rowHovered = ImGui.IsItemHovered();
        if (rowHovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            SetMapFlag(node);

        // Has to sit immediately after the Selectable: the popup opens off
        // whichever item was submitted last.
        DrawRowContextMenu(result);

        ImGui.SetCursorPos(rowStart);
        UiShared.DrawItemIcon(plugin, node.IconId, ImGui.GetFrameHeight());

        if (color is { } textColor)
            ImGui.PushStyleColor(ImGuiCol.Text, textColor);

        // The icon is now taller than the text, so the name has to be centred
        // against it or it rides the top of the row.
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(node.ItemName);

        if (color is not null)
            ImGui.PopStyleColor();

        if (rowHovered)
            DrawRowTooltip(result, isHere);

        // Table cells align to the top, so every text cell in a row this tall
        // has to be centred against the frame or it rides the ceiling while the
        // icons beside it sit in the middle.
        ImGui.TableNextColumn();
        DrawJobCell(node);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{node.JobLevelRequired}");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(node.ZoneName);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled($"{node.MapX:F1}, {node.MapY:F1}");

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        DrawStatusCell(result, color);

        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled(result.DistanceFromPlayer is { } dist ? $"{dist:F0}y" : "-");

        ImGui.TableNextColumn();
        if (ImGuiComponents.IconButton("##flag", FontAwesomeIcon.MapMarkerAlt))
            SetMapFlag(node);

        UiShared.Tooltip("Put a flag on the map here");

        ImGui.PopID();
    }

    /// <summary>
    /// The job column: the game's icon for how the node is worked, or the
    /// three-letter job when icons are off or the sheet has none.
    ///
    /// The icon says more than the text it replaces - it separates mining from
    /// quarrying where "MIN" covers both - so the tooltip names the method
    /// rather than the job, which is the part the picture cannot spell.
    /// </summary>
    private void DrawJobCell(GatherNode node)
    {
        var iconId = plugin.Configuration.ShowJobIcons
            ? GatheringIcons.IconFor(node.Method)
            : 0;

        if (iconId == 0)
        {
            ImGui.TextDisabled(UiShared.JobLabel(node.Type));
            return;
        }

        UiShared.DrawIcon(plugin, iconId, ImGui.GetFrameHeight());
        UiShared.Tooltip($"{node.Method}");
    }

    private void DrawStatusCell(PriorityResult result, Vector4? color)
    {
        if (result.IsAlwaysAvailable)
        {
            ImGui.TextDisabled("Always");
            return;
        }

        if (result.IsActive)
        {
            ImGui.TextColored(color ?? plugin.UiStyle.StatusUp, result.TimeRemaining is { } remaining
                ? $"{UiShared.FormatDuration(remaining)} left"
                : "Up now");
            return;
        }

        if (result.TimeUntilActive is { } until)
        {
            ImGui.TextColored(color ?? plugin.UiStyle.StatusIdle, $"in {UiShared.FormatDuration(until)}");
            return;
        }

        // No countdown: the node is weather-gated, and forecasting that isn't
        // wired up yet, so say what it's waiting on instead of guessing when.
        ImGui.TextDisabled(result.BlockedReason ?? "Not up");
    }

    private void DrawRowTooltip(PriorityResult result, bool isHere)
    {
        var node = result.Node;

        ImGui.BeginTooltip();

        UiShared.DrawItemTooltipHeader(plugin, node.ItemId, node.IconId, node.ItemName);

        ImGui.Separator();
        ImGui.TextDisabled($"{node.Type}  Lv{node.JobLevelRequired}");
        ImGui.TextDisabled($"{node.ZoneName}  ({node.MapX:F1}, {node.MapY:F1})");
        ImGui.TextDisabled($"Windows: {NodeQuery.DescribeWindows(node.TimeWindows)} ET");
        UiShared.DrawGatheringRequirements(node.PerceptionRequired, node.Stars);

        if (node.RequiredWeather.Count > 0)
            ImGui.TextDisabled($"Weather: {string.Join(" / ", node.RequiredWeather)}");

        if (!result.IsActive && result.BlockedReason is { } reason)
            ImGui.TextDisabled(reason);

        // Says why the row is marked, so the highlight is not a colour you have
        // to work out for yourself.
        if (isHere)
            ImGui.TextDisabled("In the zone you're standing in - no travelling needed.");

        ImGui.Separator();
        ImGui.TextDisabled("Double-click to flag it on the map.");
        ImGui.EndTooltip();
    }

    private void DrawRowContextMenu(PriorityResult result)
    {
        if (!ImGui.BeginPopupContextItem("##row_menu"))
            return;

        ImGui.TextDisabled(result.Node.ItemName);
        ImGui.Separator();

        if (ImGui.MenuItem("Set map flag"))
            SetMapFlag(result.Node);

        if (ImGui.MenuItem("Stop tracking this item"))
            plugin.SetTracked(result.Node.ItemId, result.Node.ItemName, tracked: false);

        ImGui.EndPopup();
    }

    /// <summary>
    /// Zone is matched by territory id rather than name because that is what
    /// the player's position gives us, and it is the same id the map flag needs.
    /// </summary>
    private NodeFilter BuildFilter((float X, float Y, uint TerritoryTypeId)? player)
    {
        var config = plugin.Configuration;

        return new NodeFilter
        {
            Jobs = config.GetJobFilter(),
            TerritoryTypeId = config.CurrentZoneOnly ? player?.TerritoryTypeId : null,
            TimedOnly = config.TimedNodesOnly,
        };
    }

    /// <summary>
    /// Places a marker on the player's map. This is the plugin's only
    /// game-state-changing action - it does not move the player.
    /// </summary>
    private void SetMapFlag(GatherNode node)
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
