using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>The page from which a player can download this mod.</summary>
    public const string DownloadUrl = "https://example.com/mod-sync-check"; // placeholder until the mod has a download page

    /// <summary>Get a translation, given its key and an optional object whose properties are the token values.</summary>
    private readonly Func<string, object?, string> Translate;

    /// <summary>The mod settings.</summary>
    private readonly ModConfig Config;


    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="translate">Get a translation, given its key and an optional object whose properties are the token values.</param>
    /// <param name="config">The mod settings.</param>
    public ComparisonText(Func<string, object?, string> translate, ModConfig config)
    {
        this.Translate = translate;
        this.Config = config;
    }

    /// <summary>Get the warning shown to a player who can't join because their mods don't match.</summary>
    /// <param name="comparison">The mod differences.</param>
    public string BuildWarning(ModComparison comparison)
    {
        List<string> lines = new() { this.Get("client.blocked", new { counts = this.GetCounts(comparison) }), "" };
        lines.AddRange(this.GetLimitedEntries(comparison, this.Get("entry.more-in-log", new { count = comparison.Count - this.Config.MaxModsInPopup })));
        return string.Join("\n", lines);
    }

    /// <summary>Get the text the game shows on the farmhand selection screen of a player the host rejected, which works even if they don't have this mod.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="suggestInstall">Whether to suggest installing this mod, for a player who doesn't have it.</param>
    public string BuildRejectionMessage(ModComparison comparison, bool suggestInstall)
    {
        List<string> lines = new() { this.Get("reject.summary") };
        lines.AddRange(this.GetLimitedEntries(comparison, this.Get("entry.more", new { count = comparison.Count - this.Config.MaxModsInPopup })));
        if (suggestInstall)
        {
            lines.Add("");
            lines.Add(this.Get("reject.install-hint-1"));
            lines.Add(this.Get("reject.install-hint-2"));
            lines.Add(this.Get("reject.download-hint"));
            lines.Add(ComparisonText.DownloadUrl);
        }
        return ComparisonText.RejectionMessagePrefix + string.Join("^", lines.Select(line => line.Replace('^', ' ')));
    }

    /// <summary>Get the text the game shows on the farmhand selection screen of a player the host rejected only because they don't have this mod.</summary>
    public string BuildModRequiredMessage()
    {
        string[] lines =
        {
            this.Get("reject.mod-required-1"),
            this.Get("reject.mod-required-2"),
            "",
            this.Get("reject.download-hint"),
            ComparisonText.DownloadUrl
        };
        return ComparisonText.RejectionMessagePrefix + string.Join("^", lines.Select(line => line.Replace('^', ' ')));
    }

    /// <summary>Get the text the game briefly shows on the farmhand selection screen of a player who's comparing their settings with the host's before joining.</summary>
    public string BuildCheckingMessage()
    {
        return ComparisonText.RejectionMessagePrefix + this.Get("reject.checking");
    }

    /// <summary>Get a summary of the number of mismatched mods of each type, like "2 missing, 1 with a different version".</summary>
    /// <param name="comparison">The mod differences.</param>
    public string GetCounts(ModComparison comparison)
    {
        List<string> counts = new();
        if (comparison.Missing.Count > 0)
            counts.Add(this.Get("counts.missing", new { count = comparison.Missing.Count }));
        if (comparison.VersionMismatches.Count > 0)
            counts.Add(this.Get("counts.versions", new { count = comparison.VersionMismatches.Count }));
        return string.Join(", ", counts);
    }

    /// <summary>Get a line describing each mismatched mod.</summary>
    /// <param name="comparison">The mod differences.</param>
    public IEnumerable<string> GetEntries(ModComparison comparison)
    {
        foreach (ModInfo mod in comparison.Missing)
            yield return this.Get("entry.missing", new { name = mod.Name, version = mod.Version });
        foreach (VersionMismatch mod in comparison.VersionMismatches)
            yield return this.Get("entry.version", new { name = mod.Name, playerVersion = mod.PlayerVersion, hostVersion = mod.HostVersion });
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get a translation.</summary>
    /// <param name="key">The translation key.</param>
    /// <param name="tokens">An object whose properties are the token values, if any.</param>
    private string Get(string key, object? tokens = null)
    {
        return this.Translate(key, tokens);
    }

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
