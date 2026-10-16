using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
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

    /// <summary>Open the farm map, returning whether it opened.</summary>
    private readonly Func<bool> OpenMapImpl;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="openMap">Open the farm map, returning whether it opened.</param>
    public LiveFarmMapApi(Func<bool> openMap)
    {
        this.OpenMapImpl = openMap;
    }

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

    /// <inheritdoc />
    public bool IsMapOpen()
    {
        return Game1.activeClickableMenu is FarmMapMenu;
    }

    /// <inheritdoc />
    public bool OpenMap()
    {
        return this.OpenMapImpl();
    }

    /// <inheritdoc />
    public bool TryGetTileScreenArea(int tileX, int tileY, out Rectangle area)
    {
        if (Game1.activeClickableMenu is not FarmMapMenu menu)
        {
            area = Rectangle.Empty;
            return false;
        }

        MapLayout layout = menu.Layout;
        (float x, float y) = layout.TileToScreen(tileX, tileY);
        (float right, float bottom) = layout.TileToScreen(tileX + 1, tileY + 1);
        area = new Rectangle((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(right) - (int)Math.Round(x), (int)Math.Round(bottom) - (int)Math.Round(y));
        return true;
    }
}

/// <inheritdoc cref="IFarmBuilding" />
/// <remarks>This is public so SMAPI can map it to the other mod's copy of <see cref="IFarmBuilding"/>.</remarks>
public sealed record FarmBuilding(string Kind, string Id, string DisplayName, int TileX, int TileY, int TilesWide, int TilesHigh, bool IsUnderConstruction) : IFarmBuilding;
