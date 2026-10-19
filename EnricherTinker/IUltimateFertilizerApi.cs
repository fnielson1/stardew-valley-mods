using StardewValley;
using StardewValley.TerrainFeatures;

namespace EnricherTinker;

/// <summary>The part of Ultimate Fertilizer's API this mod uses.</summary>
public interface IUltimateFertilizerApi
{
    /// <summary>Apply a fertilizer to a tile if it can go on it, without playing a sound.</summary>
    /// <param name="dirt">The tile.</param>
    /// <param name="itemId">The fertilizer's item ID.</param>
    /// <param name="who">The player applying it, whose professions affect crop growth speed.</param>
    /// <returns>Whether it was applied.</returns>
    bool ApplyFertilizerOnDirt(HoeDirt dirt, string itemId, Farmer who);
}
