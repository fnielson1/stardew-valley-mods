using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CustomCaskAging;

/// <summary>Which of the cask's output rules this mod changes, and how.</summary>
/// <remarks>
/// A cask's aging rate comes from the <c>AgingMultiplier</c> in the custom data of the output rule that matches what's put in it, and the first matching rule wins. Custom Cask Mod adds rules of its own, so this mod leaves those alone and only changes the base game's.
/// </remarks>
internal static class CaskRules
{
    /// <summary>The machine ID of the cask in <c>Data/Machines</c>.</summary>
    public const string CaskMachineId = "(BC)163";

    /// <summary>The custom data key the game reads the aging rate from.</summary>
    public const string AgingMultiplierKey = "AgingMultiplier";

    /// <summary>The IDs of the cask's output rules in the base game: what the cask takes without any mod.</summary>
    public static readonly IReadOnlyList<string> BaseGameRuleIds = new[]
    {
        "Cheese",
        "GoatCheese",
        "Wine",
        "Mead",
        "PaleAle",
        "Beer"
    };

    /// <summary>Get whether a cask output rule is one of the base game's, so this mod should change its aging rate.</summary>
    /// <param name="ruleId">The rule's ID.</param>
    public static bool IsBaseGameRule(string? ruleId)
    {
        return ruleId != null && CaskRules.BaseGameRuleIds.Contains(ruleId);
    }

    /// <summary>Get a rule's aging rate multiplied, as the text the game parses.</summary>
    /// <param name="current">The rule's current <c>AgingMultiplier</c> text, or <c>null</c> if it has none, which the game treats as 1.</param>
    /// <param name="multiplier">How many times faster to make it.</param>
    /// <param name="culture">The format the game reads numbers with. It reads the rate with the player's own number format, so it has to be written with the same one.</param>
    public static string Scale(string? current, float multiplier, CultureInfo culture)
    {
        float rate = CaskRules.TryParseRate(current, culture, out float parsed) ? parsed : 1f;
        return (rate * multiplier).ToString("0.####", culture);
    }

    /// <summary>Read an aging rate the way the game does: it must be a number above 0.</summary>
    /// <param name="text">The rule's <c>AgingMultiplier</c> text.</param>
    /// <param name="culture">The format the game reads numbers with.</param>
    /// <param name="rate">The rate, if it's usable.</param>
    public static bool TryParseRate(string? text, CultureInfo culture, out float rate)
    {
        if (text != null && float.TryParse(text, NumberStyles.Float, culture, out rate) && rate > 0f)
            return true;

        rate = 0f;
        return false;
    }
}
