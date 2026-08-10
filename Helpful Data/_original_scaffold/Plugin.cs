using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using VeinAndVine.Models;
using VeinAndVine.Services;
using VeinAndVine.Windows;

namespace VeinAndVine;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string ToggleCommand = "/veinvine";

    public Configuration Configuration { get; }
    public WeatherService WeatherService { get; }
    public PriorityEngine PriorityEngine { get; }

    /// <summary>
    /// Static node dataset, loaded once at startup. Empty until real data
    /// is loaded from a bundled JSON file - see LoadNodeDatabase below.
    /// </summary>
    public List<GatherNode> NodeDatabase { get; private set; } = [];

    private readonly WindowSystem windowSystem = new("VeinAndVine");
    private readonly MainWindow mainWindow;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        WeatherService = new WeatherService();
        PriorityEngine = new PriorityEngine(WeatherService);

        NodeDatabase = LoadNodeDatabase();

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenMainUi += () => mainWindow.IsOpen = true;

        CommandManager.AddHandler(ToggleCommand, new CommandInfo(OnToggleCommand)
        {
            HelpMessage = "Toggle the Vein and Vine window.",
        });

        Log.Information("Vein and Vine loaded");
    }

    private void OnToggleCommand(string command, string args)
    {
        mainWindow.IsOpen = !mainWindow.IsOpen;
    }

    private List<GatherNode> LoadNodeDatabase()
    {
        // TODO: load from a bundled JSON file shipped alongside the plugin
        // DLL, sourced from a public gathering dataset with credit given.
        // Returning an empty list for now so the plugin runs end to end.
        return [];
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(ToggleCommand);
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }
}
