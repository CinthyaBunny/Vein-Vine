using System.Text.Json;
using System.Text.Json.Serialization;
using Gleaner.Models;

namespace Gleaner.Services;

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

    /// <summary>
    /// Public so the dataset generator in <c>tools/NodeGen</c> can verify its
    /// output against the exact options the plugin parses with, rather than a
    /// second copy of them that could drift.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Parses the dataset. Pure: no Dalamud, no logging, no filesystem - it
    /// throws on malformed input and leaves the policy decision about what to
    /// do with that to <see cref="Load"/>.
    /// </summary>
    public static List<GatherNode> Parse(Stream json) =>
        JsonSerializer.Deserialize<List<GatherNode>>(json, Options) ?? [];

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
            var nodes = Parse(stream);
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
