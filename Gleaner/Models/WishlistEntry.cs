namespace Gleaner.Models;

/// <summary>
/// A single item the player wants to track. Kept separate from GatherNode
/// so the static dataset and the player's live wishlist never get merged
/// into one mutable blob.
/// </summary>
public sealed class WishlistEntry
{
    public required uint ItemId { get; init; }
    public int DesiredQuantity { get; set; } = 1;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Cached display name, so the settings list stays readable even if the
    /// node dataset is missing or the item was dropped from it.
    /// </summary>
    public string? Label { get; set; }
}
