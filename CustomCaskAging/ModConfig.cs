namespace CustomCaskAging;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The lowest multiplier the mod accepts.</summary>
    public const float MinMultiplier = 0.1f;

    /// <summary>The highest multiplier the mod accepts. At 56 or more, wine reaches iridium after one night.</summary>
    public const float MaxMultiplier = 100f;

    /// <summary>How many times faster than the base game everything in a cask ages. 1 is the base game's speed, 2 takes half as long, and 0.5 takes twice as long.</summary>
    public float AgingMultiplier { get; set; } = 1f;

    /// <summary>Get <see cref="AgingMultiplier"/> limited to what the game accepts. The game refuses to start aging when the rate isn't above 0, so a typo in <c>config.json</c> would otherwise make casks stop working.</summary>
    public float GetEffectiveMultiplier() => ModConfig.Clamp(this.AgingMultiplier);

    /// <summary>Limit a multiplier to the range the mod accepts, using the default for something which isn't a number.</summary>
    /// <param name="multiplier">The multiplier to limit.</param>
    public static float Clamp(float multiplier)
    {
        if (float.IsNaN(multiplier))
            return new ModConfig().AgingMultiplier;
        return System.Math.Clamp(multiplier, ModConfig.MinMultiplier, ModConfig.MaxMultiplier);
    }

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        this.AgingMultiplier = new ModConfig().AgingMultiplier;
    }
}
