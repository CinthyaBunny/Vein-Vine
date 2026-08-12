using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Gleaner.Models;
using Gleaner.Services;
using Gleaner.Windows;

namespace Gleaner;

/// <summary>
/// Entry point. Dalamud finds the single IDalamudPlugin implementation in the
/// assembly, constructs it with injected services, and calls Dispose() on unload.
///
/// Gleaner is read-only by design: it never moves the player and never
/// gathers. The single game-state-changing action is placing a map flag, which
/// the player still has to walk to.
///
/// Everything set up in the constructor must be torn down in Dispose() - plugins
/// are hot-reloaded constantly during development and a missed unsubscribe means
/// a leaked delegate calling into a dead assembly.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/gleaner";
    private const string CommandAlias = "/gln";

    public Configuration Configuration { get; }
    public WeatherService WeatherService { get; }
    public PriorityEngine PriorityEngine { get; }

    public ItemInfo ItemInfo { get; }
    public UiStyle UiStyle { get; }
    public IReadOnlyList<GatherNode> NodeDatabase { get; private set; }

    /// <summary>
    /// Bumped every time <see cref="NodeDatabase"/> is replaced. The windows
    /// derive indexes from the dataset that are too expensive to rebuild per
    /// frame, and this is how they know their cache went stale.
    /// </summary>
    public int NodeDatabaseVersion { get; private set; }

    /// <summary>Bumped on every wishlist change, for the same reason.</summary>
    public int WishlistVersion { get; private set; }

    /// <summary>
    /// Membership index over the wishlist. The picker asks this once per drawn
    /// row, so a linear scan of the config list would be O(rows x wishlist)
    /// every frame.
    /// </summary>
    private readonly HashSet<uint> trackedItemIds = [];

    // One window, tabbed. The node list and the things that configure it are
    // the same task, and splitting them across two windows meant hunting for
    // the second one every time you wanted to track something.
    private readonly WindowSystem windowSystem = new("Gleaner");
    private readonly MainWindow mainWindow;
    private readonly NodeListWindow nodeListWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Every Service property is null until this runs, so nothing above it
        // may touch one.
        pluginInterface.Create<Service>();

        Configuration = Configuration.Load();
        Configuration.Migrate();

        WeatherService = new WeatherService();
        PriorityEngine = new PriorityEngine(WeatherService);
        ItemInfo = new ItemInfo();
        UiStyle = new UiStyle(Configuration);
        NodeDatabase = Services.NodeDatabase.Load();

        RebuildTrackedIndex();

        mainWindow = new MainWindow(this);

        // Added after the main window, and the order matters: the docked panel
        // positions itself from where the main window landed this frame, so the
        // main window has to have drawn first.
        nodeListWindow = new NodeListWindow(this, mainWindow, mainWindow.NodeList);

        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(nodeListWindow);

        Service.PluginInterface.UiBuilder.Draw += DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;  // cog icon in the installer
        Service.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;      // plugin name click in the installer

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Gleaner window. \"/gleaner cfg\" opens it on settings.",
        });
        Service.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Alias for {CommandName}.",
            ShowInHelp = false,
        });

        if (Configuration.OpenMainWindowOnStartup)
            mainWindow.IsOpen = true;

        Service.Log.Information("Gleaner loaded.");
    }

    public void Dispose()
    {
        Service.CommandManager.RemoveHandler(CommandName);
        Service.CommandManager.RemoveHandler(CommandAlias);

        Service.PluginInterface.UiBuilder.Draw -= DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        // The windows hold no resources of their own - only UiStyle does, via a
        // font handle occupying a slot in Dalamud's font atlas.
        windowSystem.RemoveAllWindows();
        UiStyle.Dispose();
    }

    public void ReloadNodeDatabase()
    {
        NodeDatabase = Services.NodeDatabase.Load();
        NodeDatabaseVersion++;
    }

    public bool IsTracked(uint itemId) => trackedItemIds.Contains(itemId);

    /// <summary>
    /// Adds or removes a wishlist entry, saving unless the caller is in the
    /// middle of a batch. Every wishlist mutation goes through here so that
    /// <see cref="WishlistVersion"/> and <see cref="trackedItemIds"/> cannot
    /// drift from the config list they describe.
    /// </summary>
    public void SetTracked(uint itemId, string? label, bool tracked, bool save = true)
    {
        var existing = Configuration.Wishlist.FirstOrDefault(w => w.ItemId == itemId);

        if (tracked && existing is null)
        {
            Configuration.Wishlist.Add(new WishlistEntry { ItemId = itemId, Label = label });
            trackedItemIds.Add(itemId);
        }
        else if (!tracked && existing is not null)
        {
            Configuration.Wishlist.Remove(existing);
            trackedItemIds.Remove(itemId);
        }
        else
        {
            return;
        }

        WishlistVersion++;

        if (save)
            Configuration.Save();
    }

    public void ClearWishlist()
    {
        if (Configuration.Wishlist.Count == 0)
            return;

        Configuration.Wishlist.Clear();
        trackedItemIds.Clear();
        WishlistVersion++;
        Configuration.Save();
    }

    /// <summary>Persists a batch of <see cref="SetTracked"/> calls made with save: false.</summary>
    public void SaveWishlist() => Configuration.Save();

    private void RebuildTrackedIndex()
    {
        trackedItemIds.Clear();
        foreach (var entry in Configuration.Wishlist)
            trackedItemIds.Add(entry.ItemId);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "cfg":
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            default:
                ToggleMainUi();
                break;
        }
    }

    private void DrawUi() => windowSystem.Draw();

    /// <summary>Toggles the window, leaving whichever tab was last open in front.</summary>
    public void ToggleMainUi() => mainWindow.Toggle();

    /// <summary>
    /// The installer's cog and "/gleaner cfg". Both now land on the same
    /// window as everything else, so this opens rather than toggles - a cog
    /// that closes the window you were reading would be a surprise.
    /// </summary>
    public void ToggleConfigUi() => mainWindow.Open(MainWindow.Tab.Display);

    public void OpenWishlist() => mainWindow.Open(MainWindow.Tab.Wishlist);
}
