namespace EnricherTinker;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>Whether the mod changes Enrichers at all, including giving them <see cref="EnricherSlots.Count"/> slots. If this is off, they work as in the base game, except that one already holding several stacks still shows them all.</summary>
    public bool ModEnabled { get; set; } = true;

    /// <summary>Whether to apply every fertilizer in the Enricher which can go on the tile, in slot order. Otherwise only the first one which can is applied. This is only used if Ultimate Fertilizer is installed and its Fertilizer Mode is <see cref="FertilizerModes.MultiStack"/>.</summary>
    public bool ApplyAllFertilizers { get; set; } = true;

    /// <summary>Whether to apply an Enricher's fertilizer each morning to crops already planted in its sprinkler's range, as well as when a seed is planted. This is only used if Ultimate Fertilizer is installed.</summary>
    public bool FertilizePlantedCrops { get; set; } = true;

    /// <summary>Whether fertilizer in an Enricher is never used up.</summary>
    public bool InfiniteFertilizer { get; set; } = false;

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.ModEnabled = defaults.ModEnabled;
        this.ApplyAllFertilizers = defaults.ApplyAllFertilizers;
        this.FertilizePlantedCrops = defaults.FertilizePlantedCrops;
        this.InfiniteFertilizer = defaults.InfiniteFertilizer;
    }
}
