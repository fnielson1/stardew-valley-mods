namespace LiveFarmMap;

/// <summary>A building or mini-obelisk on the farm, from <see cref="ILiveFarmMapApi.GetBuildings"/>.</summary>
public interface IFarmBuilding
{
    /// <summary>What this is: <c>"building"</c> or <c>"mini-obelisk"</c>.</summary>
    string Kind { get; }

    /// <summary>The building type ID from <c>Data/Buildings</c> (like <c>Barn</c> or <c>Farmhouse</c>), or the mini-obelisk's qualified item ID (<c>(BC)238</c>).</summary>
    string Id { get; }

    /// <summary>The translated display name, like "Big Barn" or "Mini-Obelisk".</summary>
    string DisplayName { get; }

    /// <summary>The farm tile X position of the top-left corner of its footprint.</summary>
    int TileX { get; }

    /// <summary>The farm tile Y position of the top-left corner of its footprint.</summary>
    int TileY { get; }

    /// <summary>The footprint width in tiles. A mini-obelisk's is 1.</summary>
    int TilesWide { get; }

    /// <summary>The footprint height in tiles. A mini-obelisk's is 1.</summary>
    int TilesHigh { get; }

    /// <summary>Whether the building is still being built or upgraded. Always false for a mini-obelisk.</summary>
    bool IsUnderConstruction { get; }
}
