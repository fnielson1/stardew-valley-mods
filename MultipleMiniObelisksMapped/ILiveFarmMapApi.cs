using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace MultipleMiniObelisksMapped;

/// <summary>The API other mods can get from Live Farm Map with <c>helper.ModRegistry.GetApi&lt;ILiveFarmMapApi&gt;("fnielson.LiveFarmMap")</c>.</summary>
/// <remarks>Copied from Live Farm Map; SMAPI maps it to Live Farm Map's version by its member names.</remarks>
public interface ILiveFarmMapApi
{
    /// <summary>Get the buildings on the farm, and the mini-obelisks placed on it, as they are right now.</summary>
    /// <returns>Each building and mini-obelisk on the farm, or an empty list if no save is loaded.</returns>
    IReadOnlyList<IFarmBuilding> GetBuildings();

    /// <summary>Get whether the farm map is open right now.</summary>
    bool IsMapOpen();

    /// <summary>Open the farm map, replacing any open menu. Closing it goes back to the world.</summary>
    /// <returns>Whether it opened. It doesn't if no save is loaded or the player turned the farm map off.</returns>
    bool OpenMap();

    /// <summary>Get where a farm tile is drawn on screen while the farm map is open, for drawing over it or checking clicks on it (for example in SMAPI's <c>RenderedActiveMenu</c> and <c>ButtonPressed</c> events).</summary>
    /// <param name="tileX">The farm tile X position.</param>
    /// <param name="tileY">The farm tile Y position.</param>
    /// <param name="area">The tile's area on screen in UI pixels, like <c>Game1.getMouseX()</c>; tiles are usually smaller than in the world.</param>
    /// <returns>Whether the farm map is open, so <paramref name="area"/> is set.</returns>
    bool TryGetTileScreenArea(int tileX, int tileY, out Rectangle area);
}
