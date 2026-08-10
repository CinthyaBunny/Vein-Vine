using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

/// <summary>
/// The plugin's only window. The node list leads, because that is the question
/// you keep the window open to answer; everything that configures it lives
/// behind it in the same frame rather than in a second window you have to find.
/// </summary>
public sealed class MainWindow : Window, IDisposable
{
    /// <summary>Which tab a command or the installer wants brought forward.</summary>
    public enum Tab
    {
        Nodes,
        Wishlist,
        Display,
        Appearance,
    }

    private const ImGuiTableFlags TableFlags =
        ImGuiTableFlags.Resizable |
        ImGuiTableFlags.Reorderable |
        ImGuiTableFlags.Hideable |
        ImGuiTableFlags.Sortable |
        ImGuiTableFlags.RowBg |
        ImGuiTableFlags.BordersInnerV |
        ImGuiTableFlags.ScrollY |
        ImGuiTableFlags.SizingStretchProp;

    private const string AnyZone = "All zones";

    private readonly Plugin plugin;
    private readonly Configuration configuration;

    // Picker state. Deliberately not persisted: a search box and a level slider
    // are where you left them within a session, but restoring a filter you set
    // last week only hides items you're now looking for.
    private string search = string.Empty;
    private string? zoneFilter;
    private bool trackedOnly;
    private bool timedOnly;
    private int minLevel = NodeFilter.MinGatheringLevel;
    private int maxLevel = NodeFilter.MaxGatheringLevel;

    // The per-item index walks the whole dataset, so it's built once per load
    // rather than once per frame. The version is the dataset's, bumped on reload.
    private List<GatherItem> items = [];
    private List<string> zones = [];
    private int indexedVersion = -1;

    /// <summary>
    /// Per-tab state. Each job tab keeps its own sort and its own filtered
    /// list, so switching between them is instant and sorting Miner by level
    /// doesn't disturb how Botanist was left.
    /// </summary>
    private sealed class JobTab
    {
        public NodeSort Sort = NodeSort.ItemName;
        public bool Descending;

        // Filtering and sorting a thousand-odd rows is not free, and none of
        // its inputs change more than a few times a second. Recomputed only
        // when one of them actually does.
        public List<GatherItem> Visible = [];
        public object? VisibleKey;
    }

    private readonly Dictionary<JobFilter, JobTab> jobTabs = new()
    {
        [JobFilter.All] = new JobTab(),
        [JobFilter.Miner] = new JobTab(),
        [JobFilter.Botanist] = new JobTab(),
    };

    /// <summary>
    /// The node list itself. Shared with <see cref="NodeListWindow"/> so the
    /// tab and the docked panel are the same object with the same sort state,
    /// rather than two lists that drift apart.
    /// </summary>
    public NodeListTab NodeList { get; }

    /// <summary>Which tab to bring forward on the next draw, if any.</summary>
    private Tab? requestedTab;

    /// <summary>
    /// Where the window actually ended up last frame, for the docked node list
    /// to pin itself to. Only meaningful while <see cref="IsDrawing"/> is true.
    /// </summary>
    public Vector2 LastPosition { get; private set; }

    public Vector2 LastSize { get; private set; }

    /// <summary>
    /// True when the window drew a body last frame - open, not collapsed, not
    /// clipped away. The docked panel hangs off this rather than off IsOpen,
    /// which stays true while collapsed.
    /// </summary>
    public bool IsDrawing { get; private set; }

    public MainWindow(Plugin plugin) : base("Vein & Vine###VeinAndVineMain")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
        NodeList = new NodeListTab(plugin);

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(2000, 3000),
        };
    }

    public void Dispose() { }

    /// <summary>Opens the window with a particular tab in front.</summary>
    public void Open(Tab tab)
    {
        requestedTab = tab;
        IsOpen = true;
    }

    // The theme has to go on before ImGui.Begin: the window background, border
    // and title bar are drawn by Begin itself and would ignore a style pushed
    // inside Draw.
    public override void PreDraw()
    {
        plugin.UiStyle.PushWindowStyle();
        Flags = plugin.UiStyle.ExtraWindowFlags;

        // Cleared here and set again in Draw, which only runs when the window
        // actually has a body this frame.
        IsDrawing = false;
    }

    public override void PostDraw() => plugin.UiStyle.PopWindowStyle();

    public override void Draw()
    {
        // First thing in the window, so everything else lands on top of it.
        plugin.UiStyle.DrawChrome();

        LastPosition = ImGui.GetWindowPos();
        LastSize = ImGui.GetWindowSize();
        IsDrawing = true;

        if (!ImGui.BeginTabBar("##veinandvine_tabs"))
            return;

        // Consumed by whichever tab claims it this frame; cleared either way so
        // a request can't pin a tab open forever.
        var requested = requestedTab;
        requestedTab = null;

        // The Nodes tab steps aside when the list has its own docked panel -
        // two copies of the same table, one of them stale-looking, is worse
        // than one.
        if (configuration.NodeListPlacement == NodeListPlacement.Tabbed)
            DrawTab("Nodes", Tab.Nodes, requested, NodeList.Draw);

        DrawTab("Wishlist", Tab.Wishlist, requested, DrawWishlistTab);
        DrawTab("Display", Tab.Display, requested, DrawDisplayTab);
        DrawTab("Appearance", Tab.Appearance, requested, DrawAppearanceTab);

        ImGui.EndTabBar();
    }

    private static void DrawTab(string label, Tab tab, Tab? requested, Action draw)
    {
        var flags = requested == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

        if (!ImGui.BeginTabItem(label, flags))
            return;

        draw();
        ImGui.EndTabItem();
    }

    private void DrawAppearanceTab()
    {
        ImGui.TextWrapped(
            "Vein & Vine borrows the game's own look by default. The font, the palette " +
            "and the window frame all come out of the client - nothing is downloaded - " +
            "and each one is separate, so you can drop back to Dalamud's default for " +
            "any of them.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        var font = configuration.Font;
        if (DrawChoice("Font", ref font,
                [UiFontChoice.Dalamud, UiFontChoice.GameAxis],
                ["Dalamud default", "Axis (game UI font)"],
                "Axis is the typeface the game's own interface uses. This is the single\n" +
                "biggest change of the three - it is what makes a window read as native."))
        {
            configuration.Font = font;
            configuration.Save();
        }

        var theme = configuration.Theme;
        if (DrawChoice("Colours", ref theme,
                [
                    UiThemeChoice.Dalamud,
                    UiThemeChoice.Dark,
                    UiThemeChoice.Light,
                    UiThemeChoice.ClassicFF,
                    UiThemeChoice.ClearBlue,
                    UiThemeChoice.ClearWhite,
                    UiThemeChoice.ClearGreen,
                ],
                [
                    "Dalamud default",
                    "Dark",
                    "Light",
                    "Classic FF",
                    "Clear Blue",
                    "Clear White",
                    "Clear Green",
                ],
                "The game's own UI themes, named as it names them in System\n" +
                "Configuration, and coloured from the same UIColor sheet the game\n" +
                "tints its interface with.\n\n" +
                "Clear Grey and Clear Pink are missing because the sheet has no column\n" +
                "for them - there is nothing to read their colours from.\n\n" +
                "Leave this on Dalamud default if you have themed Dalamud yourself."))
        {
            configuration.Theme = theme;
            configuration.Save();
        }

        var chrome = configuration.Chrome;
        if (DrawChoice("Window frame", ref chrome,
                [UiChromeChoice.Dalamud, UiChromeChoice.GameFrame],
                ["Dalamud default", "Game panel"],
                "Draws the game's WindowA panel art behind the window, the same nine-slice\n" +
                "every normal game window is built from.\n\n" +
                "The most experimental of the three: it replaces the window background but\n" +
                "leaves ImGui's title bar and resize grip in place, so it is a blend rather\n" +
                "than a perfect match."))
        {
            configuration.Chrome = chrome;
            configuration.Save();
        }

        var placement = configuration.NodeListPlacement;
        if (DrawChoice("Node list", ref placement,
                [NodeListPlacement.Tabbed, NodeListPlacement.DockedLeft],
                ["First tab", "Docked to the left"],
                "Where the node list lives.\n\n" +
                "Docked gives it its own panel welded to this window's left edge. It\n" +
                "follows this window around and matches its height, but you set its\n" +
                "width with the grip in its bottom-left corner - so the list can be as\n" +
                "wide as it needs without making this window wider.\n\n" +
                "Experimental."))
        {
            configuration.NodeListPlacement = placement;
            configuration.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ImGui.TextDisabled("Everything here affects only Vein & Vine's own windows.");
    }

    /// <summary>
    /// One labelled dropdown per appearance axis.
    ///
    /// A dropdown rather than a row of radio buttons because these lists are
    /// expected to grow - more fonts, more palettes - and radios spread
    /// sideways until they wrap, whereas a combo costs the same width at two
    /// options as at ten.
    /// </summary>
    private static bool DrawChoice<T>(string label, ref T current, T[] values, string[] names, string help)
        where T : struct, Enum
    {
        var changed = false;
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        UiShared.Tooltip(help);

        ImGui.SameLine(130f * scale);

        var selected = Array.IndexOf(values, current);
        var preview = selected >= 0 ? names[selected] : "(unset)";

        ImGui.SetNextItemWidth(200f * scale);
        if (ImGui.BeginCombo($"##{label}", preview))
        {
            for (var i = 0; i < values.Length; i++)
            {
                if (!ImGui.Selectable(names[i], i == selected))
                    continue;

                current = values[i];
                changed = true;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGuiComponents.HelpMarker(help);

        return changed;
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

        RefreshIndex();

        // Filters sit above the tabs because they apply to all of them - a
        // search you have to retype when you switch jobs is worse than no tabs.
        DrawPickerFilters();

        if (!ImGui.BeginTabBar("##veinandvine_picker_jobs"))
            return;

        DrawJobTab("All", JobFilter.All);
        DrawJobTab("Miner", JobFilter.Miner);
        DrawJobTab("Botanist", JobFilter.Botanist);

        ImGui.EndTabBar();
    }

    private void DrawJobTab(string label, JobFilter jobs)
    {
        if (!ImGui.BeginTabItem(label))
            return;

        var tab = jobTabs[jobs];
        RefreshVisible(tab, jobs);

        DrawPickerTable(tab, jobs);
        DrawPickerFooter(tab.Visible, jobs, configuration.Wishlist.Count);

        ImGui.EndTabItem();
    }

    /// <summary>
    /// Re-filters and re-sorts only when something it depends on changed. The
    /// wishlist is in the key because "tracked only" reads it, and the sort is
    /// in it because the table reports a header click a frame after the fact.
    /// </summary>
    private void RefreshVisible(JobTab tab, JobFilter jobs)
    {
        var key = (search, zoneFilter, trackedOnly, timedOnly, jobs,
                   EffectiveMinLevel, EffectiveMaxLevel, tab.Sort, tab.Descending,
                   indexedVersion, plugin.WishlistVersion);

        if (tab.VisibleKey is not null && key.Equals(tab.VisibleKey))
            return;

        tab.VisibleKey = key;

        var filter = new NodeFilter
        {
            Jobs = jobs,
            Search = search,
            ZoneName = zoneFilter,
            MinLevel = EffectiveMinLevel,
            MaxLevel = EffectiveMaxLevel,
            TimedOnly = timedOnly,
        };

        var matched = items
            .Where(filter.Matches)
            .Where(i => !trackedOnly || plugin.IsTracked(i.ItemId));

        tab.Visible = NodeQuery.SortItems(matched, tab.Sort, tab.Descending);
    }

    // The level boxes are free text, so mid-edit they can hold 0 or 250 or a
    // min above the max. Filtering reads these instead of the raw fields, so a
    // half-typed number never empties the list; the fields themselves are
    // tidied up when editing finishes.
    private int EffectiveMinLevel => Math.Clamp(
        Math.Min(minLevel, maxLevel), NodeFilter.MinGatheringLevel, NodeFilter.MaxGatheringLevel);

    private int EffectiveMaxLevel => Math.Clamp(
        Math.Max(minLevel, maxLevel), NodeFilter.MinGatheringLevel, NodeFilter.MaxGatheringLevel);

    /// <summary>Rebuilds the item and zone indexes when the dataset changes underneath us.</summary>
    private void RefreshIndex()
    {
        if (indexedVersion == plugin.NodeDatabaseVersion)
            return;

        items = NodeQuery.BuildItemIndex(plugin.NodeDatabase);
        zones = NodeQuery.BuildZoneIndex(plugin.NodeDatabase);
        indexedVersion = plugin.NodeDatabaseVersion;

        // A zone that no longer exists in the dataset would filter everything
        // out with no obvious way back.
        if (zoneFilter is not null && !zones.Contains(zoneFilter, StringComparer.Ordinal))
            zoneFilter = null;
    }

    private void DrawPickerFilters()
    {
        var scale = ImGuiHelpers.GlobalScale;

        ImGui.SetNextItemWidth(-220f * scale);
        ImGui.InputTextWithHint("##search", "Search items and zones...", ref search, 128);

        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            search = string.Empty;
            zoneFilter = null;
            trackedOnly = false;
            timedOnly = false;
            minLevel = NodeFilter.MinGatheringLevel;
            maxLevel = NodeFilter.MaxGatheringLevel;
        }

        UiShared.Tooltip("Reset every filter back to its default.");

        ImGui.SameLine();
        ImGui.Checkbox("Tracked only", ref trackedOnly);

        ImGui.SameLine();
        ImGui.Checkbox("Timed only", ref timedOnly);
        UiShared.Tooltip("Only items with a spawn window. Most items are always up.");

        ImGui.SetNextItemWidth(180f * scale);
        if (ImGui.BeginCombo("##zone", zoneFilter ?? AnyZone))
        {
            if (ImGui.Selectable(AnyZone, zoneFilter is null))
                zoneFilter = null;

            foreach (var zone in zones)
            {
                if (ImGui.Selectable(zone, string.Equals(zone, zoneFilter, StringComparison.Ordinal)))
                    zoneFilter = zone;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("Lv");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        DrawLevelBox("##minlevel", ref minLevel, "Lowest gathering level to show.");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("to");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        DrawLevelBox("##maxlevel", ref maxLevel, "Highest gathering level to show.");

        ImGui.Separator();
    }

    /// <summary>
    /// A level box you type into rather than drag. Step 0 suppresses the -/+
    /// buttons, which would otherwise eat most of the width.
    ///
    /// The value is clamped when you finish editing, not on every keystroke:
    /// clamping live means backspacing the field to empty snaps it to 1, and
    /// the next two digits you type land on the wrong side of the limit.
    /// Filtering reads the clamped view of these in the meantime, so a
    /// half-typed number never blanks the list.
    /// </summary>
    private static void DrawLevelBox(string id, ref int level, string tooltip)
    {
        ImGui.SetNextItemWidth(44f * ImGuiHelpers.GlobalScale);
        ImGui.InputInt(id, ref level, 0, 0);
        UiShared.Tooltip(tooltip);

        if (ImGui.IsItemDeactivatedAfterEdit())
            level = Math.Clamp(level, NodeFilter.MinGatheringLevel, NodeFilter.MaxGatheringLevel);
    }

    private void DrawPickerTable(JobTab tab, JobFilter jobs)
    {
        var shown = tab.Visible;

        // Leave the footer a line of its own; the table takes what's left.
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (height <= 0)
            return;

        if (shown.Count == 0)
        {
            var noun = jobs switch
            {
                JobFilter.Miner => "mining items",
                JobFilter.Botanist => "botany items",
                _ => "items",
            };

            ImGui.TextDisabled(trackedOnly
                ? $"No tracked {noun} match these filters."
                : $"No {noun} match these filters.");
            return;
        }

        // The table id carries the job, so ImGui keeps each tab's column
        // widths and sort direction apart in its own ini.
        if (!ImGui.BeginTable($"##veinandvine_picker_{jobs}", 5, TableFlags, new Vector2(0, height)))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        ImGui.TableSetupColumn("Item",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.DefaultSort,
            3f, UiShared.SortId(NodeSort.ItemName));

        // A single-job tab already answers "which job", so the column only
        // earns its width on the All tab.
        ImGui.TableSetupColumn("Job",
            ImGuiTableColumnFlags.WidthFixed |
            (jobs == JobFilter.All ? ImGuiTableColumnFlags.None : ImGuiTableColumnFlags.DefaultHide),
            38f * scale, UiShared.SortId(NodeSort.Job));
        ImGui.TableSetupColumn("Lv",
            ImGuiTableColumnFlags.WidthFixed, 28f * scale, UiShared.SortId(NodeSort.Level));
        ImGui.TableSetupColumn("Zone",
            ImGuiTableColumnFlags.WidthStretch, 2f, UiShared.SortId(NodeSort.Zone));
        ImGui.TableSetupColumn("Windows (ET)",
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 130f * scale);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        UiShared.ReadSortSpecs(ref tab.Sort, ref tab.Descending);

        // The unfiltered index is over a thousand rows. Every row is the same
        // height, so the clipper can skip straight to the visible slice instead
        // of submitting widgets that get scissored away.
        //
        // Both the clipper and the table are released in finally blocks: the
        // clipper is a native allocation that would otherwise leak once per
        // frame, and skipping EndTable would leave ImGui's begin/end stack
        // unbalanced mid-frame, which takes the game client down rather than
        // just logging.
        try
        {
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(shown.Count);
                while (clipper.Step())
                {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        DrawPickerRow(shown[i]);
                }
            }
            finally
            {
                clipper.Destroy();
            }
        }
        finally
        {
            ImGui.EndTable();
        }
    }

    private void DrawPickerRow(GatherItem item)
    {
        ImGui.TableNextRow();
        ImGui.PushID((int)item.ItemId);

        ImGui.TableNextColumn();
        var isTracked = plugin.IsTracked(item.ItemId);
        if (ImGui.Checkbox("##track", ref isTracked))
            plugin.SetTracked(item.ItemId, item.ItemName, isTracked);

        // Icon sized to the checkbox rather than the text, so the row reads as
        // one horizontal band instead of three things at three heights.
        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        UiShared.DrawItemIcon(plugin, item.IconId, ImGui.GetFrameHeight());

        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(item.ItemName);

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            UiShared.DrawItemTooltipHeader(plugin, item.ItemId, item.IconId, item.ItemName);
            ImGui.Separator();
            ImGui.TextDisabled($"{UiShared.JobLabel(item.Jobs)}  Lv{item.JobLevelRequired}");
            ImGui.TextDisabled($"Windows: {item.WindowSummary}");
            UiShared.DrawGatheringRequirements(item.PerceptionRequired, item.Stars);
            ImGui.EndTooltip();
        }

        ImGui.TableNextColumn();
        ImGui.TextDisabled(UiShared.JobLabel(item.Jobs));

        ImGui.TableNextColumn();
        ImGui.TextDisabled($"{item.JobLevelRequired}");

        ImGui.TableNextColumn();
        ImGui.TextUnformatted(item.ZoneSummary);
        if (item.Zones.Count > 1)
            UiShared.Tooltip(string.Join("\n", item.Zones));

        ImGui.TableNextColumn();
        ImGui.TextDisabled(item.WindowSummary);

        ImGui.PopID();
    }

    private void DrawPickerFooter(IReadOnlyList<GatherItem> shown, JobFilter jobs, int trackedCount)
    {
        // Counted against this tab's own population, not the whole dataset -
        // "492 of 1050" on the Miner tab would be comparing to a total that
        // tab can never reach.
        var available = items.Count(i => (jobs & i.Type.ToJobFilter()) != 0);

        ImGui.TextDisabled($"{shown.Count} of {available} shown  -  {trackedCount} tracked overall");

        ImGui.SameLine();
        ImGui.BeginDisabled(shown.Count == 0);
        if (ImGui.SmallButton("Track all shown"))
            SetTrackedForAll(shown, tracked: true);

        UiShared.Tooltip($"Track the {shown.Count} item(s) the filters left.");

        ImGui.SameLine();
        if (ImGui.SmallButton("Untrack all shown"))
            SetTrackedForAll(shown, tracked: false);

        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(trackedCount == 0);
        if (ImGui.SmallButton("Clear all"))
            plugin.ClearWishlist();

        ImGui.EndDisabled();
    }

    private void SetTrackedForAll(IReadOnlyList<GatherItem> shown, bool tracked)
    {
        // One save for the batch, not one per item - this can be a thousand
        // items, and each save serialises the whole config to disk.
        foreach (var item in shown)
            plugin.SetTracked(item.ItemId, item.ItemName, tracked, save: false);

        plugin.SaveWishlist();
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
        ImGui.TextDisabled("These mirror the toggles on the main window's toolbar.");

        var mining = configuration.ShowMiningNodes;
        if (ImGui.Checkbox("List mining nodes", ref mining))
        {
            configuration.ShowMiningNodes = mining;
            configuration.Save();
        }

        var botany = configuration.ShowBotanyNodes;
        if (ImGui.Checkbox("List botany nodes", ref botany))
        {
            configuration.ShowBotanyNodes = botany;
            configuration.Save();
        }

        var zoneOnly = configuration.CurrentZoneOnly;
        if (ImGui.Checkbox("Only the zone I'm standing in", ref zoneOnly))
        {
            configuration.CurrentZoneOnly = zoneOnly;
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
