using System.Numerics;
using Lumina.Excel.Sheets;

namespace VeinAndVine.Services;

/// <summary>
/// World-space to map-coordinate conversion.
///
/// Node data is stored in map coordinates (the numbers you see in a chat map
/// link), but the player's live position is world space. Comparing the two
/// directly gives nonsense distances, so anything that mixes them has to
/// convert first.
///
/// The scale/offset formula below is the long-standing community one, derived
/// from the Map sheet's SizeFactor and Offset columns.
/// </summary>
public static class MapUtil
{
    /// <summary>
    /// Converts one world-space axis to its map coordinate.
    /// Pass the Map row's OffsetX for world X, OffsetY for world Z.
    /// </summary>
    public static float WorldToMap(float worldCoord, short offset, ushort sizeFactor)
    {
        var scale = sizeFactor / 100f;
        var scaled = (worldCoord + offset) * scale;
        return 41f / scale * ((scaled + 1024f) / 2048f) + 1f;
    }

    /// <summary>
    /// Player world position to map coordinates for the given map row.
    /// Note the Y/Z swap: the game's world Z axis is the map's Y axis.
    /// Returns null if the map row can't be resolved.
    /// </summary>
    public static Vector2? WorldToMap(Vector3 worldPosition, uint mapId)
    {
        var map = Service.DataManager.GetExcelSheet<Map>().GetRowOrDefault(mapId);
        if (map is null)
            return null;

        var sizeFactor = map.Value.SizeFactor;
        return new Vector2(
            WorldToMap(worldPosition.X, map.Value.OffsetX, sizeFactor),
            WorldToMap(worldPosition.Z, map.Value.OffsetY, sizeFactor));
    }
}
