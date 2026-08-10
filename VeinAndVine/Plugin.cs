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

    /// <summary>Static node dataset, loaded once at startup from Data/nodes.json.</summary>
    public IReadOnlyList<GatherNode> NodeDatabase { get; private set; }

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
        NodeDatabase = Services.NodeDatabase.Load();

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
    public void ReloadNodeDatabase() => NodeDatabase = Services.NodeDatabase.Load();

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
}
