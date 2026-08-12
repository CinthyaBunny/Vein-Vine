using Lumina.Excel.Sheets;
using Gleaner.Models;

namespace Gleaner.Services;

/// <summary>
/// The game's own icon for each gathering method, read from the
/// <c>GatheringType</c> sheet.
///
/// Taken from the sheet rather than from the icon-id conventions because the
/// conventions are the part that rots: <c>ClassJob</c> carries no icon column
/// at all, so a miner's icon could only be reached by hardcoding an offset into
/// the icon range and hoping it survives a patch. <c>GatheringType.IconMain</c>
/// is a real column with a real value in it.
///
/// It also says more than a job would. The sheet distinguishes mining from
/// quarrying and logging from harvesting, which is the same split the wishlist
/// sub-tabs use, where "MIN" collapses the first two into one word.
/// </summary>
public static class GatheringIcons
{
    /// <summary>
    /// Row ids as the sheet numbers them - 0 Mining, 1 Quarrying, 2 Logging,
    /// 3 Harvesting, 4 and 5 both spearfishing. The generator reads the same
    /// ids in the other direction when it stamps a node's method.
    /// </summary>
    private static uint RowFor(GatheringMethod method) => method switch
    {
        GatheringMethod.Mining => 0,
        GatheringMethod.Quarrying => 1,
        GatheringMethod.Logging => 2,
        GatheringMethod.Harvesting => 3,
        _ => 4,
    };

    private static readonly Dictionary<GatheringMethod, uint> icons = [];

    /// <summary>
    /// The method's icon id, or zero when the sheet has none - which callers
    /// should read as "fall back to the text label" rather than as an error.
    /// Zero is cached too, so a missing row is not looked up once per frame.
    /// </summary>
    public static uint IconFor(GatheringMethod method)
    {
        if (icons.TryGetValue(method, out var cached))
            return cached;

        uint icon = 0;

        try
        {
            var sheet = Service.DataManager.GetExcelSheet<GatheringType>();
            if (sheet.TryGetRow(RowFor(method), out var row) && row.IconMain > 0)
                icon = (uint)row.IconMain;
        }
        catch (Exception ex)
        {
            Service.Log.Warning(ex, $"Could not read the {method} icon from the GatheringType sheet.");
        }

        icons[method] = icon;
        return icon;
    }
}
