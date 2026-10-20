using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace HighlightCrops;

/// <summary>The mod settings, editable in <c>config.json</c> or through Generic Mod Config Menu.</summary>
internal class ModConfig
{
    /// <summary>The keys which show or hide the overlay.</summary>
    public KeybindList ToggleKey { get; set; } = new(SButton.Y);

    /// <summary>Whether to highlight tilled soil with nothing planted in it.</summary>
    public bool HighlightEmpty { get; set; } = true;

    /// <summary>Whether to highlight crops which are ready to harvest.</summary>
    public bool HighlightReady { get; set; } = true;

    /// <summary>Reset every setting to its default value.</summary>
    public void Reset()
    {
        ModConfig defaults = new();
        this.ToggleKey = defaults.ToggleKey;
        this.HighlightEmpty = defaults.HighlightEmpty;
        this.HighlightReady = defaults.HighlightReady;
    }
}
