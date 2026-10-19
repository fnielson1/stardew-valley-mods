using System;

namespace SkipLongAnimations;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The slowest geode speed, which is the base game's.</summary>
    public const int MinGeodeSpeed = 1;

    /// <summary>The fastest geode speed.</summary>
    public const int MaxGeodeSpeed = 10;

    /// <summary>Whether to skip Pam's bus driving to and from the Calico Desert.</summary>
    public bool SkipBusRide { get; set; } = true;

    /// <summary>How many times faster Clint cracks geodes, from <see cref="MinGeodeSpeed"/> to <see cref="MaxGeodeSpeed"/>.</summary>
    public int GeodeSpeed { get; set; } = 5;

    /// <summary>Get <see cref="GeodeSpeed"/>, kept within the allowed range in case <c>config.json</c> was edited by hand.</summary>
    public int GetGeodeSpeed()
    {
        return Math.Clamp(this.GeodeSpeed, ModConfig.MinGeodeSpeed, ModConfig.MaxGeodeSpeed);
    }

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.SkipBusRide = defaults.SkipBusRide;
        this.GeodeSpeed = defaults.GeodeSpeed;
    }
}
