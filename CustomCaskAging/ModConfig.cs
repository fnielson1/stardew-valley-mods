namespace CustomCaskAging;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The lowest multiplier the mod accepts.</summary>
    public const float MinMultiplier = 0.1f;

    /// <summary>The highest multiplier the mod accepts. At 56 or more, wine reaches iridium after one night.</summary>
    public const float MaxMultiplier = 100f;

    /// <summary>The default multiplier: the base game's speed.</summary>
    public const float DefaultMultiplier = 1f;

    /// <summary>How many times faster than the base game cheese ages. 1 is the base game's speed, 2 takes half as long, and 0.5 takes twice as long.</summary>
    public float CheeseMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>How many times faster than the base game goat cheese ages.</summary>
    public float GoatCheeseMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>How many times faster than the base game wine ages.</summary>
    public float WineMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>How many times faster than the base game mead ages.</summary>
    public float MeadMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>How many times faster than the base game pale ale ages.</summary>
    public float PaleAleMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>How many times faster than the base game beer ages.</summary>
    public float BeerMultiplier { get; set; } = ModConfig.DefaultMultiplier;

    /// <summary>Get the configured multiplier for one of the base game's cask rules, as written in the config.</summary>
    /// <param name="ruleId">The rule's ID, one of the base game's.</param>
    /// <returns>Returns the multiplier, or <c>null</c> if it isn't one of the base game's rules.</returns>
    public float? GetConfiguredMultiplier(string? ruleId)
    {
        return ruleId switch
        {
            "Cheese" => this.CheeseMultiplier,
            "GoatCheese" => this.GoatCheeseMultiplier,
            "Wine" => this.WineMultiplier,
            "Mead" => this.MeadMultiplier,
            "PaleAle" => this.PaleAleMultiplier,
            "Beer" => this.BeerMultiplier,
            _ => null
        };
    }

    /// <summary>Get the multiplier for one of the base game's cask rules, limited to what the game accepts. The game refuses to start aging when the rate isn't above 0, so a typo in <c>config.json</c> would otherwise make casks stop working.</summary>
    /// <param name="ruleId">The rule's ID.</param>
    /// <returns>Returns the multiplier, or <c>null</c> if it isn't one of the base game's rules.</returns>
    public float? GetEffectiveMultiplier(string? ruleId)
    {
        float? configured = this.GetConfiguredMultiplier(ruleId);
        return configured.HasValue ? ModConfig.Clamp(configured.Value) : null;
    }

    /// <summary>Limit a multiplier to the range the mod accepts, using the default for something which isn't a number.</summary>
    /// <param name="multiplier">The multiplier to limit.</param>
    public static float Clamp(float multiplier)
    {
        if (float.IsNaN(multiplier))
            return ModConfig.DefaultMultiplier;
        return System.Math.Clamp(multiplier, ModConfig.MinMultiplier, ModConfig.MaxMultiplier);
    }

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        this.CheeseMultiplier = ModConfig.DefaultMultiplier;
        this.GoatCheeseMultiplier = ModConfig.DefaultMultiplier;
        this.WineMultiplier = ModConfig.DefaultMultiplier;
        this.MeadMultiplier = ModConfig.DefaultMultiplier;
        this.PaleAleMultiplier = ModConfig.DefaultMultiplier;
        this.BeerMultiplier = ModConfig.DefaultMultiplier;
    }
}
