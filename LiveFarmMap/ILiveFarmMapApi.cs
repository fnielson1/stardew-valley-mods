using System.Collections.Generic;

namespace LiveFarmMap;

/// <summary>The API other mods can get from Live Farm Map with <c>helper.ModRegistry.GetApi&lt;ILiveFarmMapApi&gt;("fnielson.LiveFarmMap")</c>.</summary>
/// <remarks>To use it, copy this file and <see cref="IFarmBuilding"/> into your mod; SMAPI maps them to Live Farm Map's versions by their member names.</remarks>
public interface ILiveFarmMapApi
{
    /// <summary>Get the buildings on the farm, and the mini-obelisks placed on it, as they are right now.</summary>
    /// <returns>Each building and mini-obelisk on the farm, or an empty list if no save is loaded.</returns>
    IReadOnlyList<IFarmBuilding> GetBuildings();
}
