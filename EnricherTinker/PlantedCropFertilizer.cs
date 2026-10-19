using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace EnricherTinker;

/// <summary>Spreads an Enricher's fertilizer onto crops already planted in its sprinkler's range.</summary>
/// <remarks>The game only applies an Enricher's fertilizer when a seed is planted. This needs Ultimate Fertilizer, whose API applies fertilizer to a planted crop without playing a sound for each tile.</remarks>
internal class PlantedCropFertilizer
{
    /*********
    ** Fields
    *********/
    /// <summary>The qualified item ID of an Enricher.</summary>
    private const string EnricherId = "(O)913";

    /// <summary>Writes messages to the console and log file.</summary>
    private readonly IMonitor Monitor;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;

    /// <summary>Reads whether Ultimate Fertilizer is installed and lets a tile take more than one fertilizer.</summary>
    private readonly UltimateFertilizer UltimateFertilizer;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="monitor">Writes messages to the console and log file.</param>
    /// <param name="config">The mod settings.</param>
    /// <param name="ultimateFertilizer">Reads whether Ultimate Fertilizer is installed and lets a tile take more than one fertilizer.</param>
    public PlantedCropFertilizer(IMonitor monitor, ModConfig config, UltimateFertilizer ultimateFertilizer)
    {
        this.Monitor = monitor;
        this.Config = config;
        this.UltimateFertilizer = ultimateFertilizer;
    }

    /// <summary>Apply every Enricher's fertilizer to the planted crops in its sprinkler's range, in every location.</summary>
    public void FertilizeAll()
    {
        if (!this.Config.ModEnabled || !this.Config.FertilizePlantedCrops)
            return;

        IUltimateFertilizerApi? api = this.UltimateFertilizer.GetApi();
        if (api == null)
            return;

        bool applyAll = this.Config.ApplyAllFertilizers && this.UltimateFertilizer.AllowsMultiple();
        int applied = 0;
        Utility.ForEachLocation(location =>
        {
            foreach (SObject sprinkler in location.Objects.Values)
            {
                if (sprinkler.IsSprinkler() && sprinkler.heldObject.Value is { QualifiedItemId: PlantedCropFertilizer.EnricherId } enricher && enricher.heldObject.Value is Chest chest)
                    applied += this.Fertilize(location, sprinkler, chest, api, applyAll);
            }
            return true;
        });

        if (applied > 0)
            this.Monitor.Log($"Applied {applied} fertilizer from Enrichers to planted crops.");
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Apply an Enricher's fertilizer to the planted crops in its sprinkler's range.</summary>
    /// <param name="location">The location containing the sprinkler.</param>
    /// <param name="sprinkler">The sprinkler holding the Enricher.</param>
    /// <param name="chest">The Enricher's chest.</param>
    /// <param name="api">Ultimate Fertilizer's API.</param>
    /// <param name="applyAll">Whether to apply every stack which can go on a tile, rather than only the first.</param>
    /// <returns>The number of fertilizers applied.</returns>
    private int Fertilize(GameLocation location, SObject sprinkler, Chest chest, IUltimateFertilizerApi api, bool applyAll)
    {
        if (chest.Items.CountItemStacks() == 0 || chest.GetMutex().IsLocked())
            return 0;

        // the game uses the planter's professions for crop growth speed, so use the sprinkler's owner here
        Farmer who = Game1.GetPlayer(sprinkler.owner.Value) ?? Game1.MasterPlayer;

        int applied = 0;
        foreach (Vector2 tile in sprinkler.GetSprinklerTiles())
        {
            if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not HoeDirt { crop: { } crop } dirt || crop.dead.Value)
                continue;

            applied += FertilizerApplier.Apply(
                slots: chest.Items,
                tryApply: item => item.Category == SObject.fertilizerCategory && api.ApplyFertilizerOnDirt(dirt, item.ItemId, who),
                consumeOne: item => item.ConsumeStack(1),
                applyAll: applyAll,
                infinite: this.Config.InfiniteFertilizer
            );
        }

        // the game only looks for fertilizer in the first slot when a seed is planted, so keep the stacks at the front
        if (applied > 0)
            chest.clearNulls();
        return applied;
    }
}
