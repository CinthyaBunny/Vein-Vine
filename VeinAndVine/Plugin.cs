using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using VeinAndVine.Models;
using VeinAndVine.Services;
using VeinAndVine.Windows;

namespace VeinAndVine;

/// <summary>
/// Entry point. Dalamud finds the single IDalamudPlugin implementation in the
/// assembly, constructs it with injected services, and calls Dispose() on unload.
///
/// Vein and Vine is read-only by design: it never moves the player and never
/// gathers. The single game-state-changing action is placing a map flag, which
/// the player still has to walk to.
///
/// Everything set up in the constructor must be torn down in Dispose() - plugins
/// are hot-reloaded constantly during development and a missed unsubscribe means
/// a leaked delegate calling into a dead assembly.
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/veinvine";
    private const string CommandAlias = "/vnv";

    public Configuration Configuration { get; }
    public WeatherService WeatherService { get; }
    public PriorityEngine PriorityEngine { get; }

    /// <summary>Icons and item descriptions, read from the game's own data.</summary>
    public ItemInfo ItemInfo { get; }

    /// <summary>Static node dataset, loaded once at startup from Data/nodes.json.</summary>
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

    private readonly WindowSystem windowSystem = new("VeinAndVine");
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        // Populates the static Service properties. Must happen first.
        pluginInterface.Create<Service>();

        Configuration = Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        WeatherService = new WeatherService();
        PriorityEngine = new PriorityEngine(WeatherService);
        ItemInfo = new ItemInfo();
        NodeDatabase = Services.NodeDatabase.Load();

        RebuildTrackedIndex();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        Service.PluginInterface.UiBuilder.Draw += DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;  // cog icon in the installer
        Service.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;      // plugin name click in the installer

        Service.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Vein & Vine window. \"/veinvine cfg\" for settings.",
        });
        Service.CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Alias for {CommandName}.",
            ShowInHelp = false,
        });

        if (Configuration.OpenMainWindowOnStartup)
            mainWindow.IsOpen = true;

        Service.Log.Information("Vein & Vine loaded.");
    }

    public void Dispose()
    {
        Service.CommandManager.RemoveHandler(CommandName);
        Service.CommandManager.RemoveHandler(CommandAlias);

        Service.PluginInterface.UiBuilder.Draw -= DrawUi;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        Service.PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        configWindow.Dispose();
    }

    /// <summary>Re-reads Data/nodes.json without a plugin reload.</summary>
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

    public void ToggleMainUi() => mainWindow.Toggle();

    public void ToggleConfigUi() => configWindow.Toggle();

    /// <summary>Opens settings straight on the item picker.</summary>
    public void OpenWishlist() => configWindow.OpenWishlist();
}
