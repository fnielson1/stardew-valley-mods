namespace NoCollisionMultiplayer;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>Whether to walk through the farm's cat or dog.</summary>
    public bool PassThroughPets { get; set; } = true;

    /// <summary>Whether to walk through horses.</summary>
    public bool PassThroughHorses { get; set; } = true;

    /// <summary>Whether to walk through Junimos.</summary>
    public bool PassThroughJunimos { get; set; } = true;

    /// <summary>Whether to walk through villagers.</summary>
    public bool PassThroughVillagers { get; set; } = false;

    /// <summary>Whether to walk through farm animals.</summary>
    public bool PassThroughFarmAnimals { get; set; } = true;

    /// <summary>Whether to walk through monsters, which then can't hurt you.</summary>
    public bool PassThroughMonsters { get; set; } = false;

    /// <summary>Whether to walk through everything, like furniture, fences and walls.</summary>
    public bool PassThroughEverything { get; set; } = false;

    /// <summary>Get whether the player walks through a kind of character.</summary>
    /// <param name="kind">The kind of character.</param>
    public bool Allows(PassThroughKind kind)
    {
        return kind switch
        {
            PassThroughKind.Pet => this.PassThroughPets,
            PassThroughKind.Horse => this.PassThroughHorses,
            PassThroughKind.Junimo => this.PassThroughJunimos,
            PassThroughKind.Villager => this.PassThroughVillagers,
            PassThroughKind.FarmAnimal => this.PassThroughFarmAnimals,
            PassThroughKind.Monster => this.PassThroughMonsters,
            _ => false
        };
    }

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.PassThroughPets = defaults.PassThroughPets;
        this.PassThroughHorses = defaults.PassThroughHorses;
        this.PassThroughJunimos = defaults.PassThroughJunimos;
        this.PassThroughVillagers = defaults.PassThroughVillagers;
        this.PassThroughFarmAnimals = defaults.PassThroughFarmAnimals;
        this.PassThroughMonsters = defaults.PassThroughMonsters;
        this.PassThroughEverything = defaults.PassThroughEverything;
    }
}
