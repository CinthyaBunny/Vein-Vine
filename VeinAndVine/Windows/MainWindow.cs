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

    // Held as text, because that is what the boxes edit. The only strings the
    // filter below lets you type are the empty one and a whole number from 1
    // to 100, so parsing them back is total apart from the empty case.
    private string minLevelText = NodeFilter.MinGatheringLevel.ToString();
    private string maxLevelText = NodeFilter.MaxGatheringLevel.ToString();

    // The per-item index walks the whole dataset, so it's built once per load
    // rather than once per frame. The version is the dataset's, bumped on reload.
    //
    // One index per method scope, not one overall: an item's zones, level and
    // windows are a summary of its nodes, so a Quarrying sub-tab needs a
    // summary of only the quarrying ones. There are seven scopes the UI can
    // ask for and each is built at most once per dataset load.
    private readonly Dictionary<MethodFilter, List<GatherItem>> indexes = [];
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

        /// <summary>
        /// The sub-tab: which of the job's two methods is showing, or all of
        /// them. Per job tab, so Miner can be narrowed to Quarrying without
        /// disturbing where Botanist was left.
        /// </summary>
        public MethodFilter Methods = MethodFilter.All;

        // Filtering and sorting a thousand-odd rows is not free, and none of
        // its inputs change more than a few times a second. Recomputed only
        // when one of them actually does.
        public List<GatherItem> Visible = [];
        public object? VisibleKey;
    }

    // Seeded with each job's own "all methods" rather than the enum's, because
    // a tab's label counts whatever scope it is holding - and on the first
    // frame, before its sub-strip has ever been drawn, that has to already be
    // the job's own scope or Miner would open labelled with the whole dataset.
    private readonly Dictionary<JobFilter, JobTab> jobTabs = new()
    {
        [JobFilter.All] = new JobTab { Methods = MethodFilter.All },
        [JobFilter.Miner] = new JobTab { Methods = MethodFilter.AllMiner },
        [JobFilter.Botanist] = new JobTab { Methods = MethodFilter.AllBotanist },
    };

    private JobFilter currentJob = JobFilter.All;

    /// <summary>
    /// The node list itself. Shared with <see cref="NodeListWindow"/> so the
    /// tab and the docked panel are the same object with the same sort state,
    /// rather than two lists that drift apart.
    /// </summary>
    public NodeListTab NodeList { get; }

    /// <summary>Which tab to bring forward on the next draw, if any.</summary>
    private Tab? requestedTab;

    /// <summary>
    /// The open tab. Tracked here rather than by ImGui, because the hand-drawn
    /// strip has no state of its own.
    /// </summary>
    private Tab currentTab = Tab.Nodes;

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
        LastPosition = ImGui.GetWindowPos();
        LastSize = ImGui.GetWindowSize();
        IsDrawing = true;

        // The Nodes tab steps aside when the list has its own docked panel -
        // two copies of the same table, one of them stale-looking, is worse
        // than one. The strip is built per frame, so a tab coming and going
        // costs nothing.
        var tabs = new List<Tab>(4);
        if (configuration.NodeListPlacement == NodeListPlacement.Tabbed)
            tabs.Add(Tab.Nodes);

        tabs.Add(Tab.Wishlist);
        tabs.Add(Tab.Display);
        tabs.Add(Tab.Appearance);

        // Consumed by whichever tab claims it this frame; cleared either way so
        // a request can't pin a tab open forever.
        if (requestedTab is { } wanted && tabs.Contains(wanted))
            currentTab = wanted;

        requestedTab = null;

        if (!tabs.Contains(currentTab))
            currentTab = tabs[0];

        var labels = tabs.ConvertAll(TabLabel);
        var chosen = GameTabBar.Draw(plugin, "##veinandvine_tabs", labels, tabs.IndexOf(currentTab));
        currentTab = tabs[chosen];

        ImGui.Separator();

        switch (currentTab)
        {
            case Tab.Nodes: NodeList.Draw(); break;
            case Tab.Wishlist: DrawWishlistTab(); break;
            case Tab.Display: DrawDisplayTab(); break;
            case Tab.Appearance: DrawAppearanceTab(); break;
        }
    }

    private static string TabLabel(Tab tab) => tab switch
    {
        Tab.Nodes => "Nodes",
        Tab.Wishlist => "Wishlist",
        Tab.Display => "Display",
        _ => "Appearance",
    };

    private void DrawAppearanceTab()
    {
        ImGui.TextWrapped(
            "Vein & Vine borrows the game's own look by default. The typeface, the " +
            "colours and the window panel all come out of the client - nothing is " +
            "downloaded - and either can drop back to Dalamud's default on its own.");

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
        if (DrawChoice("Theme", ref theme,
                [
                    UiThemeChoice.Dalamud,
                    UiThemeChoice.Dark,
                    UiThemeChoice.Light,
                    UiThemeChoice.ClassicFF,
                    UiThemeChoice.ClearBlue,
                    UiThemeChoice.ClearWhite,
                    UiThemeChoice.ClearGreen,
                    UiThemeChoice.ClearGrey,
                    UiThemeChoice.ClearPink,
                ],
                [
                    "Dalamud default",
                    "Dark",
                    "Light",
                    "Classic FF",
                    "Clear Blue",
                    "Clear White",
                    "Clear Green",
                    "Clear Grey",
                    "Clear Pink",
                ],
                "All eight of the game's UI themes, named and ordered as they are in\n" +
                "System Configuration, and coloured from the same UIColor sheet the\n" +
                "game tints its own interface with.\n\n" +
                "This covers the whole window: the panel and its border as well as\n" +
                "every control inside it.\n\n" +
                "Leave this on Dalamud default if you have themed Dalamud yourself."))
        {
            configuration.Theme = theme;
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

        // After the filter row, because every tab's label counts what that tab
        // would show under the filters as they now stand.
        RefreshCounts();

        // Each label counts the scope its tab is actually holding, narrowing
        // included, so the number on a tab is the number of rows you get for
        // clicking it. The cost is that a narrowed job no longer adds up with
        // its siblings - the footer's tooltip says so where that shows.
        JobFilter[] jobs = [JobFilter.All, JobFilter.Miner, JobFilter.Botanist];
        string[] labels =
        [
            Labelled("All", jobTabs[JobFilter.All].Methods),
            Labelled("Miner", jobTabs[JobFilter.Miner].Methods),
            Labelled("Botanist", jobTabs[JobFilter.Botanist].Methods),
        ];

        currentJob = jobs[GameTabBar.Draw(
            plugin, "##veinandvine_picker_jobs", labels, Array.IndexOf(jobs, currentJob))];

        ImGui.Separator();

        var tab = jobTabs[currentJob];
        DrawMethodTabs(tab, currentJob);
        RefreshVisible(tab, currentJob);

        DrawPickerTable(tab, currentJob);
        DrawPickerFooter(tab, currentJob, configuration.Wishlist.Count);
    }

    /// <summary>
    /// The sub-strip under a job tab: All, and then that job's two methods.
    ///
    /// Only the single-job tabs get one. The All tab already spans both jobs,
    /// so a five-way method strip under it would be a second job filter wearing
    /// a different hat.
    /// </summary>
    private void DrawMethodTabs(JobTab tab, JobFilter job)
    {
        MethodFilter[] methods = job switch
        {
            JobFilter.Miner => [MethodFilter.AllMiner, MethodFilter.Mining, MethodFilter.Quarrying],
            JobFilter.Botanist => [MethodFilter.AllBotanist, MethodFilter.Logging, MethodFilter.Harvesting],
            _ => [],
        };

        if (methods.Length == 0)
        {
            // Nothing to choose from, so make sure a narrowing left behind on
            // another tab isn't still quietly filtering this one.
            tab.Methods = MethodFilter.All;
            return;
        }

        string[] names = job == JobFilter.Miner
            ? ["All", "Mining", "Quarrying"]
            : ["All", "Logging", "Harvesting"];

        var labels = new string[names.Length];
        for (var i = 0; i < names.Length; i++)
            labels[i] = Labelled(names[i], methods[i]);

        var selected = Array.IndexOf(methods, tab.Methods);
        if (selected < 0)
            selected = 0;

        tab.Methods = methods[GameTabBar.Draw(
            plugin, $"##veinandvine_methods_{job}", labels, selected)];

        ImGui.Spacing();
    }

    /// <summary>One method rather than a job's pair of them - i.e. a sub-tab is narrowing.</summary>
    private static bool IsSingleMethod(MethodFilter methods) =>
        methods != MethodFilter.None && (methods & (methods - 1)) == 0;

    private static string MethodLabel(MethodFilter methods) => methods switch
    {
        MethodFilter.Mining => "Mining",
        MethodFilter.Quarrying => "Quarrying",
        MethodFilter.Logging => "Logging",
        MethodFilter.Harvesting => "Harvesting",
        MethodFilter.Spearfishing => "Spearfishing",
        _ => "Gathering",
    };

    /// <summary>
    /// Every tab whose label carries a count - which is all of them. Ordered
    /// parent before children so the reconciliation reads in that order too.
    /// </summary>
    private static readonly (JobFilter Job, MethodFilter Methods)[] CountedTabs =
    [
        (JobFilter.All, MethodFilter.All),
        (JobFilter.Miner, MethodFilter.AllMiner),
        (JobFilter.Miner, MethodFilter.Mining),
        (JobFilter.Miner, MethodFilter.Quarrying),
        (JobFilter.Botanist, MethodFilter.AllBotanist),
        (JobFilter.Botanist, MethodFilter.Logging),
        (JobFilter.Botanist, MethodFilter.Harvesting),
    ];

    // How many rows each tab would show as the filters now stand. Keyed by
    // method scope, which identifies a tab on its own: AllMiner only exists
    // under Miner. Not keyed on the sort, which can't change a count, nor on
    // the open tab, which mustn't change what the others report.
    private readonly Dictionary<MethodFilter, int> counts = [];
    private object? countsKey;

    /// <summary>
    /// What is in one tab, filters and all. Everything that counts or lists
    /// rows goes through here, so there is exactly one definition of "in this
    /// tab" - add a filter to it and the rows, every tab label and the footer
    /// all move together, instead of the labels quietly describing the list as
    /// it was two filters ago.
    /// </summary>
    private IEnumerable<GatherItem> Matching(JobFilter jobs, MethodFilter methods)
    {
        var filter = new NodeFilter
        {
            Jobs = jobs,
            Methods = methods,
            Search = search,
            ZoneName = zoneFilter,
            MinLevel = EffectiveMinLevel,
            MaxLevel = EffectiveMaxLevel,
            TimedOnly = timedOnly,
        };

        return ItemsFor(methods)
            .Where(filter.Matches)
            .Where(i => !trackedOnly || plugin.IsTracked(i.ItemId));
    }

    /// <summary>
    /// Recounts every tab when a filter moves. Seven predicated passes over at
    /// most a thousand rows, and only on the frames where an input actually
    /// changed - a label that lags the list it describes is worse than the
    /// arithmetic is expensive.
    /// </summary>
    private void RefreshCounts()
    {
        var key = (search, zoneFilter, trackedOnly, timedOnly,
                   EffectiveMinLevel, EffectiveMaxLevel, indexedVersion, plugin.WishlistVersion);

        if (countsKey is not null && key.Equals(countsKey))
            return;

        countsKey = key;
        counts.Clear();

        foreach (var (job, methods) in CountedTabs)
            counts[methods] = Matching(job, methods).Count();
    }

    private int CountOf(MethodFilter methods) => counts.GetValueOrDefault(methods);

    /// <summary>A tab's name with its live row count, e.g. "Quarrying (202)".</summary>
    private string Labelled(string name, MethodFilter methods) =>
        $"{name} ({CountOf(methods):N0})";

    /// <summary>
    /// Re-filters and re-sorts only when something it depends on changed. The
    /// wishlist is in the key because "tracked only" reads it, and the sort is
    /// in it because the table reports a header click a frame after the fact.
    /// </summary>
    private void RefreshVisible(JobTab tab, JobFilter jobs)
    {
        var key = (search, zoneFilter, trackedOnly, timedOnly, jobs, tab.Methods,
                   EffectiveMinLevel, EffectiveMaxLevel, tab.Sort, tab.Descending,
                   indexedVersion, plugin.WishlistVersion);

        if (tab.VisibleKey is not null && key.Equals(tab.VisibleKey))
            return;

        tab.VisibleKey = key;
        tab.Visible = NodeQuery.SortItems(Matching(jobs, tab.Methods), tab.Sort, tab.Descending);
    }

    /// <summary>
    /// A level box's contents as a number. The character filter guarantees the
    /// value is in range if there is one at all, so the only reading left to do
    /// is the empty box mid-edit, which means "no bound from this end" rather
    /// than zero.
    /// </summary>
    private static int LevelOr(string text, int whenBlank) =>
        int.TryParse(text, out var level)
        && level >= NodeFilter.MinGatheringLevel
        && level <= NodeFilter.MaxGatheringLevel
            ? level
            : whenBlank;

    // Ordered rather than clamped: a min above the max is a band you typed
    // backwards, not an error, and reading it either way round beats showing
    // nothing until you fix it. Filtering goes through these, so a box you have
    // emptied on the way to retyping it never blanks the list.
    private int EffectiveMinLevel => Math.Min(
        LevelOr(minLevelText, NodeFilter.MinGatheringLevel),
        LevelOr(maxLevelText, NodeFilter.MaxGatheringLevel));

    private int EffectiveMaxLevel => Math.Max(
        LevelOr(minLevelText, NodeFilter.MinGatheringLevel),
        LevelOr(maxLevelText, NodeFilter.MaxGatheringLevel));

    /// <summary>
    /// The item index for one method scope, built on first use and kept until
    /// the dataset reloads.
    /// </summary>
    private List<GatherItem> ItemsFor(MethodFilter methods)
    {
        if (indexes.TryGetValue(methods, out var cached))
            return cached;

        var built = NodeQuery.BuildItemIndex(plugin.NodeDatabase, methods);
        indexes[methods] = built;
        return built;
    }

    /// <summary>Rebuilds the item and zone indexes when the dataset changes underneath us.</summary>
    private void RefreshIndex()
    {
        if (indexedVersion == plugin.NodeDatabaseVersion)
            return;

        indexes.Clear();
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
            minLevelText = NodeFilter.MinGatheringLevel.ToString();
            maxLevelText = NodeFilter.MaxGatheringLevel.ToString();
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
        DrawLevelBox("##minlevel", ref minLevelText, NodeFilter.MinGatheringLevel,
            "Lowest gathering level to show. Whole numbers from 1 to 100.");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.AlignTextToFramePadding();
        ImGui.TextDisabled("to");

        ImGui.SameLine(0, ImGui.GetStyle().ItemInnerSpacing.X);
        DrawLevelBox("##maxlevel", ref maxLevelText, NodeFilter.MaxGatheringLevel,
            "Highest gathering level to show. Whole numbers from 1 to 100.");

        ImGui.Separator();
    }

    // Static, and built once: the delegate is handed to native code, so a new
    // one per frame would both allocate on the draw path and risk being
    // collected while ImGui still holds it.
    private static readonly ImGui.ImGuiInputTextCallbackDelegate LevelCharFilter = FilterLevelCharacter;

    /// <summary>
    /// A level box you type into rather than drag.
    ///
    /// Rejection happens at the keystroke, not on commit. Refusing the
    /// character means the box can only ever read as a gathering level, so
    /// there is no invalid state to explain, no error to show, and nothing to
    /// silently rewrite under you the moment you click away - which is what a
    /// clamp does, and it is indistinguishable from the box eating your input.
    /// The one state the filter permits and a level does not is empty, which
    /// this reads as "no bound from this end" while you retype, and fills back
    /// in when you leave.
    /// </summary>
    private static void DrawLevelBox(string id, ref string text, int whenBlank, string tooltip)
    {
        ImGui.SetNextItemWidth(44f * ImGuiHelpers.GlobalScale);

        // AutoSelectAll so clicking in and typing replaces rather than appends.
        // Without it the box holding "100" would refuse every digit you type,
        // each one making a number over the limit, and read as simply broken.
        ImGui.InputText(id, ref text, 8,
            ImGuiInputTextFlags.CharsDecimal |
            ImGuiInputTextFlags.CallbackCharFilter |
            ImGuiInputTextFlags.AutoSelectAll,
            LevelCharFilter);

        UiShared.Tooltip(tooltip);

        if (ImGui.IsItemDeactivatedAfterEdit() && !int.TryParse(text, out _))
            text = whenBlank.ToString();
    }

    /// <summary>
    /// Marshals ImGui's character event into <see cref="NodeFilter.AcceptsLevelKeystroke"/>
    /// and turns the answer back into ImGui's keep/discard convention.
    ///
    /// ImGui runs pasted text through here one character at a time as well, so
    /// pasting "abc" or "9999" is refused exactly the way typing it is.
    /// </summary>
    private static int FilterLevelCharacter(ref ImGuiInputTextCallbackData data)
    {
        const int discard = 1;
        const int keep = 0;

        // What the keystroke replaces: the selection if there is one,
        // otherwise nothing, with the character landing at the cursor.
        var start = Math.Min(data.SelectionStart, data.SelectionEnd);
        var end = Math.Max(data.SelectionStart, data.SelectionEnd);
        if (start == end)
            start = end = data.CursorPos;

        // The box only ever holds ASCII digits, so a byte is a character. The
        // length is bounded before it reaches the stack: a level is three
        // characters, and anything longer is already not one.
        var current = data.BufTextSpan;
        if (current.Length > 8)
            return discard;

        Span<char> text = stackalloc char[current.Length];
        for (var i = 0; i < current.Length; i++)
            text[i] = (char)current[i];

        return NodeFilter.AcceptsLevelKeystroke(text, start, end, (char)data.EventChar)
            ? keep
            : discard;
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
            // Name whatever is actually narrowing. On a sub-tab that is the
            // method, and blaming the job instead sends you looking for a
            // filter that isn't the one hiding the rows.
            var noun = IsSingleMethod(tab.Methods)
                ? $"{MethodLabel(tab.Methods).ToLowerInvariant()} items"
                : jobs switch
                {
                    // Not "mining items" - that now names a method rather than
                    // the job, and Mining is one of the two sub-tabs below it.
                    JobFilter.Miner => "items for a miner",
                    JobFilter.Botanist => "items for a botanist",
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

    private void DrawPickerFooter(JobTab tab, JobFilter jobs, int trackedCount)
    {
        var shown = tab.Visible;

        // This tab's own population with the filter row cleared, which is the
        // only figure here that isn't already on a tab label. It comes off the
        // scoped index and counts against Jobs rather than Type, so a both-jobs
        // item is in both jobs' totals.
        var unfiltered = ItemsFor(tab.Methods).Count(i => (jobs & i.Jobs) != 0);

        // "517 of 517" is noise. The second figure only earns its place once a
        // filter has actually removed something.
        ImGui.TextDisabled(shown.Count == unfiltered
            ? $"{shown.Count:N0} shown  -  {trackedCount:N0} tracked overall"
            : $"{shown.Count:N0} of {unfiltered:N0} shown  -  {trackedCount:N0} tracked overall");

        UiShared.Tooltip(CountExplanation(jobs, tab.Methods, shown.Count, unfiltered));

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

    /// <summary>
    /// Spells out how this tab's count decomposes into its children's.
    ///
    /// The parts overlap on purpose - an item gatherable two ways is genuinely
    /// in both child tabs - so the labels on the strip add up to more than the
    /// parent's, and a reader with no way to see the overlap can only read that
    /// as a miscount. Stating it turns three numbers that look inconsistent
    /// into one subtraction that balances.
    /// </summary>
    private string CountExplanation(JobFilter jobs, MethodFilter methods, int shown, int unfiltered)
    {
        var text = shown == unfiltered
            ? $"All {shown:N0} item(s) on this tab.\n"
            : $"{shown:N0} of this tab's {unfiltered:N0} item(s) survive the filters above.\n";

        var (whole, first, second, firstName, secondName) = (jobs, methods) switch
        {
            (JobFilter.All, _) =>
                (MethodFilter.All, MethodFilter.AllMiner, MethodFilter.AllBotanist, "miner", "botanist"),
            (JobFilter.Miner, MethodFilter.AllMiner) =>
                (MethodFilter.AllMiner, MethodFilter.Mining, MethodFilter.Quarrying, "mining", "quarrying"),
            (JobFilter.Botanist, MethodFilter.AllBotanist) =>
                (MethodFilter.AllBotanist, MethodFilter.Logging, MethodFilter.Harvesting, "logging", "harvesting"),
            _ => (MethodFilter.None, MethodFilter.None, MethodFilter.None, string.Empty, string.Empty),
        };

        // A leaf tab has nothing under it to reconcile against.
        if (whole == MethodFilter.None)
            return text.TrimEnd();

        var total = CountOf(whole);
        var a = CountOf(first);
        var b = CountOf(second);
        var both = a + b - total;

        // Guarded rather than assumed: the subtraction only balances while
        // every item belongs to at least one child tab, and printing a sum that
        // visibly doesn't add up would be worse than printing no sum at all.
        if (both >= 0)
        {
            text += both > 0
                ? $"\n{a:N0} {firstName} + {b:N0} {secondName} - {both:N0} gatherable both ways = {total:N0}."
                : $"\n{a:N0} {firstName} + {b:N0} {secondName} = {total:N0}.";
        }

        // Those two figures are the jobs in full, but a job tab is labelled
        // with whatever it is currently narrowed to. Left unsaid, the sum above
        // would look like it disagrees with the strip right above it.
        if (jobs == JobFilter.All)
        {
            foreach (var (name, job) in new[] { ("Miner", JobFilter.Miner), ("Botanist", JobFilter.Botanist) })
            {
                var narrowing = jobTabs[job].Methods;
                if (!IsSingleMethod(narrowing))
                    continue;

                text += $"\n\n{name} is narrowed to {MethodLabel(narrowing).ToLowerInvariant()}, so its tab " +
                        $"is labelled {CountOf(narrowing):N0} of those.";
            }
        }

        return text.TrimEnd();
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
