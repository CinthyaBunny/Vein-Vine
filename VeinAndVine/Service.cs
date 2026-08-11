using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VeinAndVine;

/// <summary>
/// Dalamud service container. Populated once by <c>pluginInterface.Create&lt;Service&gt;()</c>
/// in the plugin constructor; every property is null until then.
///
/// Only what is actually used belongs here. Create&lt;Service&gt;() resolves every
/// [PluginService] property at construction, so an unused one is still a hard
/// dependency on that service surviving Dalamud's API surface - one renamed in
/// a future API bump would fail the whole plugin's load for something it never
/// called. Add them back at the point of use, not in anticipation of it.
///
/// Services are owned by Dalamud, so never dispose them.
/// </summary>
public sealed class Service
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
}
