using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VeinAndVine;

/// <summary>
/// Dalamud service container. Populated once by <c>pluginInterface.Create&lt;Service&gt;()</c>
/// in the plugin constructor; every property is null until then.
///
/// Add whatever else you need here - Dalamud fills in any property marked
/// [PluginService] whose type it knows about. Services are owned by Dalamud,
/// so never dispose them.
/// </summary>
public sealed class Service
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;

    /// <summary>Per-frame tick. The place to drive any gathering automation loop.</summary>
    [PluginService] public static IFramework Framework { get; private set; } = null!;

    /// <summary>Local player, territory changes, login state.</summary>
    [PluginService] public static IClientState ClientState { get; private set; } = null!;

    /// <summary>Condition flags - Gathering, Mounted, BetweenAreas, Occupied, etc.</summary>
    [PluginService] public static ICondition Condition { get; private set; } = null!;

    /// <summary>Excel sheets: GatheringItem, GatheringPointBase, Item, TerritoryType, ...</summary>
    [PluginService] public static IDataManager DataManager { get; private set; } = null!;

    /// <summary>Nearby objects - gathering nodes appear here as EventObj entries.</summary>
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;

    [PluginService] public static ITargetManager TargetManager { get; private set; } = null!;

    /// <summary>Addon lookup - needed to read the Gathering window.</summary>
    [PluginService] public static IGameGui GameGui { get; private set; } = null!;

    [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] public static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] public static INotificationManager NotificationManager { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;

    /// <summary>Unlocked aetherytes, for teleport-to-node routing.</summary>
    [PluginService] public static IAetheryteList AetheryteList { get; private set; } = null!;

    /// <summary>Inventory change events - useful for counting what you actually gathered.</summary>
    [PluginService] public static IGameInventory GameInventory { get; private set; } = null!;
}
