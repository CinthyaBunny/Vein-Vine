using System.Text.Json;
using System.Text.Json.Serialization;
using VeinAndVine.Models;

namespace VeinAndVine.Services;

/// <summary>
/// Loads the static node dataset shipped alongside the plugin DLL
/// (<c>Data/nodes.json</c>).
///
/// The dataset is deliberately a plain file rather than compiled-in data: it
/// is the part most likely to need updating between patches, and keeping it
/// external means a data refresh doesn't need a rebuild.
/// </summary>
public static class NodeDatabase
{
    public const string DataFileName = "nodes.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DataFilePath
    {
        get
        {
            var dir = Service.PluginInterface.AssemblyLocation.Directory?.FullName
                      ?? AppContext.BaseDirectory;
            return Path.Combine(dir, "Data", DataFileName);
        }
    }

    /// <summary>
    /// Reads and parses the dataset. Never throws - a missing or malformed
    /// file logs and yields an empty list, so a bad data drop degrades the
    /// plugin to "shows nothing" rather than failing to load at all.
    /// </summary>
    public static List<GatherNode> Load()
    {
        var path = DataFilePath;

        if (!File.Exists(path))
        {
            Service.Log.Warning($"Node dataset not found at {path}. Starting with an empty database.");
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            var nodes = JsonSerializer.Deserialize<List<GatherNode>>(stream, Options) ?? [];
            Service.Log.Information($"Loaded {nodes.Count} gathering node(s) from {DataFileName}.");
            return nodes;
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, $"Failed to parse {path}. Starting with an empty database.");
            return [];
        }
    }
}
