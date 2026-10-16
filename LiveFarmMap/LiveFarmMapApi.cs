using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.TokenizableStrings;

namespace LiveFarmMap;

/// <inheritdoc cref="ILiveFarmMapApi" />
public sealed class LiveFarmMapApi : ILiveFarmMapApi
{
    /*********
    ** Fields
    *********/
    /// <summary>The qualified item ID of the mini-obelisk.</summary>
    private const string MiniObeliskId = "(BC)238";


    /*********
    ** Public methods
    *********/
    /// <inheritdoc />
    public IReadOnlyList<IFarmBuilding> GetBuildings()
    {
        if (!Context.IsWorldReady)
            return Array.Empty<IFarmBuilding>();

        Farm farm = Game1.getFarm();
        List<IFarmBuilding> found = new();

        foreach (Building building in farm.buildings)
        {
            string id = building.buildingType.Value;
            string? name = building.GetData()?.Name;
            found.Add(new FarmBuilding(
                Kind: "building",
                Id: id,
                DisplayName: name != null ? TokenParser.ParseText(name) : id,
                TileX: building.tileX.Value,
                TileY: building.tileY.Value,
                TilesWide: building.tilesWide.Value,
                TilesHigh: building.tilesHigh.Value,
                IsUnderConstruction: building.isUnderConstruction()
            ));
        }

        foreach (StardewValley.Object obj in farm.objects.Values)
        {
            if (obj.QualifiedItemId != LiveFarmMapApi.MiniObeliskId)
                continue;

            found.Add(new FarmBuilding(
                Kind: "mini-obelisk",
                Id: obj.QualifiedItemId,
                DisplayName: obj.DisplayName,
                TileX: (int)obj.TileLocation.X,
                TileY: (int)obj.TileLocation.Y,
                TilesWide: 1,
                TilesHigh: 1,
                IsUnderConstruction: false
            ));
        }

        return found;
    }
}

/// <inheritdoc cref="IFarmBuilding" />
/// <remarks>This is public so SMAPI can map it to the other mod's copy of <see cref="IFarmBuilding"/>.</remarks>
public sealed record FarmBuilding(string Kind, string Id, string DisplayName, int TileX, int TileY, int TilesWide, int TilesHigh, bool IsUnderConstruction) : IFarmBuilding;
