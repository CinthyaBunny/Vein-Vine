using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using VeinAndVine.Services;

namespace VeinAndVine.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
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
    private bool showMining = true;
    private bool showBotany = true;
    private int minLevel = NodeFilter.MinGatheringLevel;
    private int maxLevel = NodeFilter.MaxGatheringLevel;
    private NodeSort sort = NodeSort.ItemName;
    private bool sortDescending;

    // The per-item index walks the whole dataset, so it's built once per load
    // rather than once per frame. The version is the dataset's, bumped on reload.
    private List<GatherItem> items = [];
    private List<string> zones = [];
    private int indexedVersion = -1;

    // Filtering and sorting a thousand-odd rows is not free, and none of its
    // inputs change more than a few times a second. Recomputed only when one
    // of them actually does.
    private List<GatherItem> visible = [];
    private object? visibleKey;

    /// <summary>Set to jump to a tab the next time the window draws.</summary>
    private bool selectWishlistTab;

    public ConfigWindow(Plugin plugin) : base("Vein & Vine Settings###VeinAndVineConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 380),
            MaximumSize = new Vector2(1400, 1200),
        };
    }

    public void Dispose() { }

    /// <summary>Opens the window on the item picker, from wherever it was last.</summary>
    public void OpenWishlist()
    {
        selectWishlistTab = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##veinandvine_config_tabs"))
            return;

        var wishlistFlags = selectWishlistTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        selectWishlistTab = false;

        if (ImGui.BeginTabItem("Wishlist", wishlistFlags))
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
        DrawPickerFilters();
        RefreshVisible();

        DrawPickerTable(visible);
        DrawPickerFooter(visible, configuration.Wishlist.Count);
    }

    /// <summary>
    /// Re-filters and re-sorts only when something it depends on changed. The
    /// wishlist is in the key because "tracked only" reads it, and the sort is
    /// in it because the table reports a header click a frame after the fact.
    /// </summary>
    private void RefreshVisible()
    {
        var key = (search, zoneFilter, trackedOnly, timedOnly, showMining, showBotany,
                   minLevel, maxLevel, sort, sortDescending,
                   indexedVersion, plugin.WishlistVersion);

        if (visibleKey is not null && key.Equals(visibleKey))
            return;

        visibleKey = key;

        var filter = new NodeFilter
        {
            Jobs = (showMining ? JobFilter.Miner : JobFilter.None) |
                   (showBotany ? JobFilter.Botanist : JobFilter.None),
            Search = search,
            ZoneName = zoneFilter,
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            TimedOnly = timedOnly,
        };

        var matched = items
            .Where(filter.Matches)
            .Where(i => !trackedOnly || plugin.IsTracked(i.ItemId));

        visible = NodeQuery.SortItems(matched, sort, sortDescending);
    }

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
            showMining = true;
            showBotany = true;
            minLevel = NodeFilter.MinGatheringLevel;
            maxLevel = NodeFilter.MaxGatheringLevel;
        }

        UiShared.Tooltip("Reset every filter on this tab.");

        ImGui.SameLine();
        ImGui.Checkbox("Tracked only", ref trackedOnly);

        ImGui.SameLine();
        ImGui.Checkbox("Timed only", ref timedOnly);
        UiShared.Tooltip("Only items with a spawn window. Most items are always up.");

        ImGui.Checkbox("Miner", ref showMining);
        ImGui.SameLine();
        ImGui.Checkbox("Botanist", ref showBotany);

        ImGui.SameLine();
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
        ImGui.SetNextItemWidth(70f * scale);
        if (ImGui.SliderInt("##minlevel", ref minLevel, NodeFilter.MinGatheringLevel, NodeFilter.MaxGatheringLevel, "Lv%d"))
            maxLevel = Math.Max(maxLevel, minLevel);

        UiShared.Tooltip("Lowest gathering level to show.");

        ImGui.SameLine();
        ImGui.TextDisabled("-");

        ImGui.SameLine();
        ImGui.SetNextItemWidth(70f * scale);
        if (ImGui.SliderInt("##maxlevel", ref maxLevel, NodeFilter.MinGatheringLevel, NodeFilter.MaxGatheringLevel, "Lv%d"))
            minLevel = Math.Min(minLevel, maxLevel);

        UiShared.Tooltip("Highest gathering level to show.");

        ImGui.Separator();
    }

    private void DrawPickerTable(IReadOnlyList<GatherItem> shown)
    {
        // Leave the footer a line of its own; the table takes what's left.
        var height = ImGui.GetContentRegionAvail().Y - ImGui.GetFrameHeightWithSpacing();
        if (height <= 0)
            return;

        if (shown.Count == 0)
        {
            ImGui.TextDisabled(trackedOnly
                ? "Nothing tracked matches these filters."
                : "No items match these filters.");
            return;
        }

        if (!ImGui.BeginTable("##veinandvine_picker", 5, TableFlags, new Vector2(0, height)))
            return;

        var scale = ImGuiHelpers.GlobalScale;

        ImGui.TableSetupColumn("Item",
            ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.DefaultSort,
            3f, UiShared.SortId(NodeSort.ItemName));
        ImGui.TableSetupColumn("Job",
            ImGuiTableColumnFlags.WidthFixed, 38f * scale, UiShared.SortId(NodeSort.Job));
        ImGui.TableSetupColumn("Lv",
            ImGuiTableColumnFlags.WidthFixed, 28f * scale, UiShared.SortId(NodeSort.Level));
        ImGui.TableSetupColumn("Zone",
            ImGuiTableColumnFlags.WidthStretch, 2f, UiShared.SortId(NodeSort.Zone));
        ImGui.TableSetupColumn("Windows (ET)",
            ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoSort, 130f * scale);

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        UiShared.ReadSortSpecs(ref sort, ref sortDescending);

        // The unfiltered index is over a thousand rows. Every row is the same
        // height, so the clipper can skip straight to the visible slice instead
        // of submitting widgets that get scissored away.
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

        ImGui.EndTable();
    }

    private void DrawPickerRow(GatherItem item)
    {
        ImGui.TableNextRow();
        ImGui.PushID((int)item.ItemId);

        ImGui.TableNextColumn();
        var isTracked = plugin.IsTracked(item.ItemId);
        if (ImGui.Checkbox(item.ItemName, ref isTracked))
            plugin.SetTracked(item.ItemId, item.ItemName, isTracked);

        ImGui.TableNextColumn();
        ImGui.TextDisabled(UiShared.JobLabel(item.Type));

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

    private void DrawPickerFooter(IReadOnlyList<GatherItem> shown, int trackedCount)
    {
        ImGui.TextDisabled($"{shown.Count} of {items.Count} shown  -  {trackedCount} tracked");

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
