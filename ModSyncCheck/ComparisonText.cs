using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;

namespace ModSyncCheck;

/// <summary>Builds the player-facing text for a mod comparison.</summary>
internal class ComparisonText
{
    /*********
    ** Fields
    *********/
    /// <summary>The prefix for the rejection text sent to a joining player.</summary>
    /// <remarks>The game treats the text as a translation key in the form <c>asset:key</c> and shows it as-is when the key doesn't exist, so prefixing a real asset name lets the host show custom text to a player who doesn't have this mod. The <c>^</c> is a line break.</remarks>
    private const string RejectionMessagePrefix = "Strings\\UI:^";

    /// <summary>The mod translations.</summary>
    private readonly ITranslationHelper Translation;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="translation">The mod translations.</param>
    /// <param name="config">The mod settings.</param>
    public ComparisonText(ITranslationHelper translation, ModConfig config)
    {
        this.Translation = translation;
        this.Config = config;
    }

    /// <summary>Get the warning shown to a player who can't join because their mods don't match.</summary>
    /// <param name="comparison">The mod differences.</param>
    public string BuildWarning(ModComparison comparison)
    {
        List<string> lines = new() { this.Translation.Get("client.blocked", new { counts = this.GetCounts(comparison) }), "" };
        lines.AddRange(this.GetLimitedEntries(comparison, this.Translation.Get("entry.more-in-log", new { count = comparison.Count - this.Config.MaxModsInPopup })));
        return string.Join("\n", lines);
    }

    /// <summary>Get the text the game shows on the farmhand selection screen of a player the host rejected, which works even if they don't have this mod.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="suggestInstall">Whether to suggest installing this mod, for a player who doesn't have it.</param>
    public string BuildRejectionMessage(ModComparison comparison, bool suggestInstall)
    {
        List<string> lines = new() { this.Translation.Get("reject.summary") };
        lines.AddRange(this.GetLimitedEntries(comparison, this.Translation.Get("entry.more", new { count = comparison.Count - this.Config.MaxModsInPopup })));
        if (suggestInstall)
        {
            lines.Add("");
            lines.Add(this.Translation.Get("reject.install-hint-1"));
            lines.Add(this.Translation.Get("reject.install-hint-2"));
        }
        return ComparisonText.RejectionMessagePrefix + string.Join("^", lines.Select(line => line.Replace('^', ' ')));
    }

    /// <summary>Get a summary of the number of mismatched mods of each type, like "2 missing, 1 extra".</summary>
    /// <param name="comparison">The mod differences.</param>
    public string GetCounts(ModComparison comparison)
    {
        List<string> counts = new();
        if (comparison.Missing.Count > 0)
            counts.Add(this.Translation.Get("counts.missing", new { count = comparison.Missing.Count }));
        if (comparison.Extra.Count > 0)
            counts.Add(this.Translation.Get("counts.extra", new { count = comparison.Extra.Count }));
        if (comparison.VersionMismatches.Count > 0)
            counts.Add(this.Translation.Get("counts.versions", new { count = comparison.VersionMismatches.Count }));
        return string.Join(", ", counts);
    }

    /// <summary>Get a line describing each mismatched mod.</summary>
    /// <param name="comparison">The mod differences.</param>
    public IEnumerable<string> GetEntries(ModComparison comparison)
    {
        foreach (ModInfo mod in comparison.Missing)
            yield return this.Translation.Get("entry.missing", new { name = mod.Name, version = mod.Version });
        foreach (ModInfo mod in comparison.Extra)
            yield return this.Translation.Get("entry.extra", new { name = mod.Name });
        foreach (VersionMismatch mod in comparison.VersionMismatches)
            yield return this.Translation.Get("entry.version", new { name = mod.Name, playerVersion = mod.PlayerVersion, hostVersion = mod.HostVersion });
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get a line describing each mismatched mod up to the configured limit, followed by a note about the rest if any.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="moreText">The note to add if some mods were left out.</param>
    private IEnumerable<string> GetLimitedEntries(ModComparison comparison, string moreText)
    {
        int max = this.Config.MaxModsInPopup;
        foreach (string line in this.GetEntries(comparison).Take(max))
            yield return line;

        if (comparison.Count > max)
            yield return moreText;
    }
}
