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

    /// <summary>The most lines of text which can be sent to a rejected player.</summary>
    /// <remarks>The game draws the text downwards from the middle of the screen without wrapping it, and the first line is taken up by <see cref="RejectionMessagePrefix"/>, so any more than this runs past the bottom of the farmhand selection box.</remarks>
    public const int MaxRejectionLines = 3;

    /// <summary>This mod's unique ID.</summary>
    public const string ThisModId = "fnielson.ModSyncCheck";

    /// <summary>The most mods listed in each section of the list before the rest are hidden behind a link which shows them.</summary>
    public const int SectionLimit = 4;

    /// <summary>The section of the list for the mods the player can get as files checked with Nexus Mods.</summary>
    public const int VerifiableSection = 0;

    /// <summary>The section of the list for the other mods.</summary>
    public const int UnverifiableSection = 1;

    /// <summary>This mod's ID on Nexus Mods.</summary>
    public const int ThisModNexusId = 53850;

    /// <summary>The page from which a player can download this mod.</summary>
    public static readonly string DownloadUrl = NexusLinks.GetModUrl(ComparisonText.ThisModNexusId);

    /// <summary>Get a translation, given its key and an optional object whose properties are the token values.</summary>
    private readonly Func<string, object?, string> Translate;



    /*********
    ** Public methods
    *********/
    /// <summary>Construct an instance.</summary>
    /// <param name="translate">Get a translation, given its key and an optional object whose properties are the token values.</param>
    public ComparisonText(Func<string, object?, string> translate)
    {
        this.Translate = translate;
    }

    /// <summary>Get the warning shown to a player who can't join because their mods don't match.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="verifiableIds">The unique IDs of the mods the player can get as files checked with Nexus Mods, or <c>null</c> if there are none.</param>
    /// <param name="expandedSections">The sections of the list to show in full.</param>
    public string BuildWarning(ModComparison comparison, IReadOnlyCollection<string>? verifiableIds = null, IReadOnlyCollection<int>? expandedSections = null)
    {
        List<string> lines = new() { this.Get("client.blocked", new { counts = this.GetCounts(comparison) }), "" };
        if (ComparisonText.GetThisModMismatch(comparison) is { } thisMod)
        {
            lines.Add(this.Get("client.version-required", new { hostVersion = thisMod.HostVersion, version = thisMod.PlayerVersion }));
            lines.Add("");
        }

        bool first = true;
        foreach ((ModComparison section, string headingKey, int index) in ComparisonText.GetSections(comparison, verifiableIds))
        {
            if (!first)
                lines.Add("");
            first = false;

            int max = ComparisonText.GetSectionLimit(index, expandedSections);
            lines.Add(this.Get(headingKey, new { count = section.Count }));
            lines.AddRange(this.GetEntries(section).Take(max));
            if (section.Count > max)
                lines.Add(this.GetShowMoreEntry(section.Count - max));
        }
        return string.Join("\n", lines);
    }

    /// <summary>Get the text the game shows on the farmhand selection screen of a player the host rejected because their mods don't match, which works even if they don't have this mod.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="suggestInstall">Whether to say where to get this mod, for a player who doesn't have it. A player who has it is shown the full list in a dialog instead.</param>
    public string BuildRejectionMessage(ModComparison comparison, bool suggestInstall)
    {
        // a player whose version of this mod doesn't understand ours may not be able to show anything else
        if (!suggestInstall && ComparisonText.GetThisModMismatch(comparison) is { } thisMod)
            return ComparisonText.BuildRejection(this.Get("reject.version-required", new { version = thisMod.HostVersion }), ComparisonText.GetDownloadAddress());

        return suggestInstall
            ? ComparisonText.BuildRejection(this.Get("reject.install-hint"), ComparisonText.GetDownloadAddress())
            : ComparisonText.BuildRejection(this.Get("reject.summary"), this.GetCounts(comparison));
    }

    /// <summary>Get the text the game shows on the farmhand selection screen of a player the host rejected only because they don't have this mod.</summary>
    public string BuildModRequiredMessage()
    {
        return ComparisonText.BuildRejection(this.Get("reject.mod-required"), ComparisonText.GetDownloadAddress());
    }

    /// <summary>Get the text the game briefly shows on the farmhand selection screen of a player who's comparing their settings with the host's before joining.</summary>
    public string BuildCheckingMessage()
    {
        return ComparisonText.RejectionMessagePrefix + this.Get("reject.checking");
    }

    /// <summary>Get the notice shown to a player whose version of this mod may not understand the host's fully, or <c>null</c> if the two are compatible. Versions which only differ by a minor or patch version are compatible.</summary>
    /// <param name="hostVersion">The host's version of this mod.</param>
    /// <param name="hostMajorVersion">The major part of <paramref name="hostVersion"/>.</param>
    /// <param name="version">The player's version of this mod.</param>
    /// <param name="majorVersion">The major part of <paramref name="version"/>.</param>
    public string? BuildVersionNotice(string hostVersion, int hostMajorVersion, string version, int majorVersion)
    {
        if (hostMajorVersion == majorVersion)
            return null;

        return this.Get(
            hostMajorVersion > majorVersion ? "client.host-version-newer" : "client.host-version-older",
            new { hostVersion, version, url = ComparisonText.DownloadUrl }
        );
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


    /// <summary>Get a link to the Nexus Mods page for each listed mod which has one, and a link to show the rest of each section which is cut short, in the order <see cref="BuildWarning"/> lists them.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="verifiableIds">The unique IDs of the mods the player can get as files checked with Nexus Mods, or <c>null</c> if there are none.</param>
    /// <param name="expandedSections">The sections of the list shown in full.</param>
    public List<ModLink> GetNexusLinks(ModComparison comparison, IReadOnlyCollection<string>? verifiableIds = null, IReadOnlyCollection<int>? expandedSections = null)
    {
        List<ModLink> links = new();
        foreach ((ModComparison section, _, int index) in ComparisonText.GetSections(comparison, verifiableIds))
        {
            int max = ComparisonText.GetSectionLimit(index, expandedSections);
            links.AddRange(this.GetNexusLinks(section, max));
            if (section.Count > max)
                links.Add(new ModLink(this.Get("entry.show-more"), "", this.GetShowMoreEntry(section.Count - max), ExpandSection: index));
        }
        return links;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get the download page address as the player should type it, short enough to fit on one line.</summary>
    public static string GetDownloadAddress()
    {
        return ComparisonText.DownloadUrl["https://www.".Length..];
    }

    /// <summary>Get the version mismatch for this mod itself, if the player needs the host's version of it.</summary>
    /// <param name="comparison">The mod differences.</param>
    private static VersionMismatch? GetThisModMismatch(ModComparison comparison)
    {
        return comparison.VersionMismatches.FirstOrDefault(mod => string.Equals(mod.Id, ComparisonText.ThisModId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Get text in the form the game shows on a rejected player's farmhand selection screen.</summary>
    /// <param name="lines">The lines of text to show, up to <see cref="MaxRejectionLines"/>.</param>
    private static string BuildRejection(params string[] lines)
    {
        return ComparisonText.RejectionMessagePrefix + string.Join("^", lines.Take(ComparisonText.MaxRejectionLines).Select(line => line.Replace('^', ' ')));
    }

    /// <summary>Get a translation.</summary>
    /// <param name="key">The translation key.</param>
    /// <param name="tokens">An object whose properties are the token values, if any.</param>
    private string Get(string key, object? tokens = null)
    {
        return this.Translate(key, tokens);
    }

    /// <summary>Get a link to the Nexus Mods page for each mismatched mod which has one, for a list of every mod like <see cref="GetEntries"/>.</summary>
    /// <param name="comparison">The mod differences.</param>
    public List<ModLink> GetAllNexusLinks(ModComparison comparison)
    {
        return this.GetNexusLinks(comparison, int.MaxValue).ToList();
    }

    /// <summary>Get a link to the Nexus Mods page for each of the first mismatched mods which has one.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="max">The number of mods listed.</param>
    private IEnumerable<ModLink> GetNexusLinks(ModComparison comparison, int max)
    {
        return comparison.Missing
            .Select(mod => (mod.Name, mod.NexusId))
            .Concat(comparison.VersionMismatches.Select(mod => (mod.Name, mod.NexusId)))
            .Zip(this.GetEntries(comparison), (mod, entry) => (mod.Name, mod.NexusId, Entry: entry))
            .Take(max)
            .Where(mod => mod.NexusId > 0)
            .Select(mod => new ModLink(mod.Name, NexusLinks.GetModUrl(mod.NexusId!.Value), mod.Entry));
    }

    /// <summary>Get the line which ends a section of the list that's cut short, with a link to show the rest.</summary>
    /// <param name="hidden">The number of mods not listed.</param>
    private string GetShowMoreEntry(int hidden)
    {
        return this.Get("entry.more-expand", new { count = hidden, link = this.Get("entry.show-more") });
    }

    /// <summary>Get the number of mods to list in a section.</summary>
    /// <param name="section">The section of the list.</param>
    /// <param name="expandedSections">The sections to show in full.</param>
    private static int GetSectionLimit(int section, IReadOnlyCollection<int>? expandedSections)
    {
        return expandedSections?.Contains(section) == true
            ? int.MaxValue
            : ComparisonText.SectionLimit;
    }

    /// <summary>Split the mismatched mods into those the player can get as files checked with Nexus Mods and the rest, leaving out an empty group.</summary>
    /// <param name="comparison">The mod differences.</param>
    /// <param name="verifiableIds">The unique IDs of the mods the player can get as files checked with Nexus Mods, or <c>null</c> if there are none.</param>
    /// <returns>Returns each group with the translation key for its heading and its section number.</returns>
    private static IEnumerable<(ModComparison Mods, string HeadingKey, int Section)> GetSections(ModComparison comparison, IReadOnlyCollection<string>? verifiableIds)
    {

        HashSet<string> ids = new(verifiableIds ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        ModComparison Filter(bool verifiable) => new(
            comparison.Missing.Where(mod => ids.Contains(mod.Id) == verifiable).ToList(),
            comparison.VersionMismatches.Where(mod => ids.Contains(mod.Id) == verifiable).ToList()
        );

        ModComparison verifiableMods = Filter(verifiable: true);
        if (verifiableMods.Count > 0)
            yield return (verifiableMods, "client.section-verifiable", ComparisonText.VerifiableSection);

        ModComparison unverifiableMods = Filter(verifiable: false);
        if (unverifiableMods.Count > 0)
            yield return (unverifiableMods, "client.section-unverifiable", ComparisonText.UnverifiableSection);
    }
}
