using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace LiveFarmMap;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The fewest seconds allowed between redraws of the farm.</summary>
    public const int MinRefreshSeconds = 1;

    /// <summary>The most seconds allowed between redraws of the farm.</summary>
    public const int MaxRefreshSeconds = 10;

    /// <summary>Whether the farm map can be opened at all.</summary>
    public bool ModEnabled { get; set; } = true;

    /// <summary>The keys which open the farm map directly.</summary>
    public KeybindList OpenKey { get; set; } = new(new Keybind(SButton.LeftShift, SButton.M));

    /// <summary>How many seconds to wait between redraws of the farm while the map is open.</summary>
    public int RefreshSeconds { get; set; } = 1;

    /// <summary>Whether to show icons for the players and characters on the farm.</summary>
    public bool ShowPeople { get; set; } = true;

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.ModEnabled = defaults.ModEnabled;
        this.OpenKey = defaults.OpenKey;
        this.RefreshSeconds = defaults.RefreshSeconds;
        this.ShowPeople = defaults.ShowPeople;
    }
}
