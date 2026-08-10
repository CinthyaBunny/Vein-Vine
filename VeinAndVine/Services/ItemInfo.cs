using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;

namespace VeinAndVine.Services;

/// <summary>
/// Item presentation the static dataset deliberately does not carry: the icon
/// texture and the item's in-game description.
///
/// Both come straight out of the game rather than over the network. The client
/// already has them on disk, they are already localised to whatever language
/// the player runs, and they cannot fall out of step with the installed patch.
/// A remote source would add latency, a cache, a failure mode, and an offline
/// story, in exchange for the same bytes.
///
/// Game-coupled by definition - it reads Lumina sheets and Dalamud's texture
/// provider - so nothing in the pure column depends on it.
/// </summary>
public sealed class ItemInfo
{
    /// <summary>
    /// Extracted descriptions, keyed by item id. Null is cached too: it means
    /// "the sheet has nothing for this", which is worth remembering so a
    /// hovered row doesn't re-read the sheet every frame.
    /// </summary>
    private readonly Dictionary<uint, string?> descriptions = [];

    /// <summary>
    /// The item's icon, or null while it is still loading or if there isn't
    /// one. Callers should reserve the space either way, so rows don't jump as
    /// textures arrive.
    ///
    /// Dalamud owns the returned wrap and caches it internally, so this is
    /// cheap to call once per visible row per frame and must not be disposed.
    /// </summary>
    public IDalamudTextureWrap? GetIcon(uint iconId)
    {
        if (iconId == 0)
            return null;

        try
        {
            return Service.TextureProvider
                .GetFromGameIcon(new GameIconLookup(iconId))
                .GetWrapOrDefault();
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, $"Could not load icon {iconId}.");
            return null;
        }
    }

    /// <summary>
    /// The item's in-game description, or null if it has none. Read from the
    /// Item sheet on first ask and cached - the text is long, and extracting a
    /// SeString per frame for a hovered row would be wasteful.
    /// </summary>
    public string? GetDescription(uint itemId)
    {
        if (descriptions.TryGetValue(itemId, out var cached))
            return cached;

        string? description = null;

        try
        {
            var sheet = Service.DataManager.GetExcelSheet<Item>();
            if (sheet is not null && sheet.TryGetRow(itemId, out var row))
            {
                var text = row.Description.ExtractText().Trim();
                if (text.Length > 0)
                    description = text;
            }
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, $"Could not read the description for item {itemId}.");
        }

        descriptions[itemId] = description;
        return description;
    }
}
