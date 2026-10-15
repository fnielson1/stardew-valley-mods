using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Xunit;

namespace ModSyncCheck.Tests;

/// <summary>Checks the player-facing text using the mod's real default translations.</summary>
public class ComparisonTextTests
{
    /// <summary>The mod's default translations.</summary>
    private static readonly Dictionary<string, string> Translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "i18n", "default.json"))
    )!;

    private static readonly ModComparison Comparison = new(
        Missing: new[] { new ModInfo("Pathoschild.Automate", "Automate", "2.3.0"), new ModInfo("cantorsdust.TimeSpeed", "TimeSpeed", "2.8.0") },
        VersionMismatches: new[] { new VersionMismatch("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0", "2.9.1") }
    );

    /// <summary>A player who only needs the host's version of this mod.</summary>
    private static readonly ModComparison ThisModComparison = new(
        Missing: Array.Empty<ModInfo>(),
        VersionMismatches: new[] { new VersionMismatch(ComparisonText.ThisModId, "Mod Sync Check", "1.0.4", "2.0.0") }
    );

    /// <summary>More verified mods than a section lists before cutting it short, and one unverified mod, all with a Nexus Mods ID.</summary>
    private static readonly ModComparison ManyComparison = new(
        Missing: Enumerable.Range(1, 7).Select(i => new ModInfo($"V{i}", $"V{i}", "1.0", NexusId: i)).Append(new ModInfo("U1", "U1", "1.0", NexusId: 100)).ToArray(),
        VersionMismatches: Array.Empty<VersionMismatch>()
    );

    /// <summary>The verified mods in <see cref="ManyComparison"/>.</summary>
    private static readonly string[] ManyVerifiedIds = Enumerable.Range(1, 7).Select(i => $"V{i}").ToArray();

    [Fact]
    public void GetCounts_ListsEachType()
    {
        Assert.Equal("2 missing, 1 with a different version", ComparisonTextTests.CreateText().GetCounts(Comparison));
    }

    [Fact]
    public void GetCounts_LeavesOutEmptyTypes()
    {
        ModComparison comparison = Comparison with { Missing = Array.Empty<ModInfo>() };

        Assert.Equal("1 with a different version", ComparisonTextTests.CreateText().GetCounts(comparison));
    }

    [Fact]
    public void GetEntries_DescribesEachMod()
    {
        string[] entries = ComparisonTextTests.CreateText().GetEntries(Comparison).ToArray();

        Assert.Equal(
            new[]
            {
                "Missing: Automate 2.3.0",
                "Missing: TimeSpeed 2.8.0",
                "Wrong version: Content Patcher 2.5.0 (host: 2.9.1)"
            },
            entries
        );
    }

    [Fact]
    public void BuildWarning_NoVerifiedMods_ListsCountsAndEveryModAsUnverified()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(Comparison).Split('\n');

        Assert.Contains("2 missing, 1 with a different version", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal(
            new[]
            {
                ComparisonTextTests.Translate("client.section-unverified", new { count = 3 }),
                "Missing: Automate 2.3.0",
                "Missing: TimeSpeed 2.8.0",
                "Wrong version: Content Patcher 2.5.0 (host: 2.9.1)"
            },
            lines[2..]
        );
    }

    [Fact]
    public void BuildWarning_OverTheLimit_CutsOffTheListWithAShowMoreLine()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(ManyComparison).Split('\n');

        Assert.Equal(new[] { "Missing: V1 1.0", "Missing: V2 1.0", "Missing: V3 1.0", "Missing: V4 1.0", "...and 4 more (show more)" }, lines[3..]);
    }

    [Fact]
    public void BuildWarning_AtTheLimit_ListsEverything()
    {
        ModComparison comparison = ManyComparison with { Missing = ManyComparison.Missing.Take(ComparisonText.SectionLimit).ToArray() };

        string[] lines = ComparisonTextTests.CreateText().BuildWarning(comparison).Split('\n');

        Assert.Equal(ComparisonText.SectionLimit, lines[3..].Length);
        Assert.DoesNotContain(lines, line => line.Contains("more"));
    }

    [Fact]
    public void BuildWarning_WithVerifiedMods_ListsVerifiedThenUnverified()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(Comparison, verifiedIds: new[] { "cantorsdust.TimeSpeed", "Pathoschild.ContentPatcher" }).Split('\n');

        Assert.Equal(
            new[]
            {
                ComparisonTextTests.Translate("client.section-verified", new { count = 2 }),
                "Missing: TimeSpeed 2.8.0",
                "Wrong version: Content Patcher 2.5.0 (host: 2.9.1)",
                "",
                ComparisonTextTests.Translate("client.section-unverified", new { count = 1 }),
                "Missing: Automate 2.3.0"
            },
            lines[2..]
        );
    }

    [Fact]
    public void BuildWarning_AllVerified_LeavesOutTheUnverifiedSection()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(Comparison, verifiedIds: new[] { "Pathoschild.Automate", "cantorsdust.TimeSpeed", "Pathoschild.ContentPatcher" }).Split('\n');

        Assert.Equal(ComparisonTextTests.Translate("client.section-verified", new { count = 3 }), lines[2]);
        Assert.Equal(6, lines.Length);
    }

    [Fact]
    public void BuildWarning_WithVerifiedMods_CutsEachSectionShortWithAShowMoreLine()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(ManyComparison, verifiedIds: ManyVerifiedIds).Split('\n');

        Assert.Equal(
            new[]
            {
                ComparisonTextTests.Translate("client.section-verified", new { count = 7 }),
                "Missing: V1 1.0",
                "Missing: V2 1.0",
                "Missing: V3 1.0",
                "Missing: V4 1.0",
                "...and 3 more (show more)",
                "",
                ComparisonTextTests.Translate("client.section-unverified", new { count = 1 }),
                "Missing: U1 1.0"
            },
            lines[2..]
        );
    }

    [Fact]
    public void BuildWarning_ExpandedSection_ListsEveryMod()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(ManyComparison, verifiedIds: ManyVerifiedIds, expandedSections: new[] { ComparisonText.VerifiedSection }).Split('\n');

        Assert.Equal(new[] { "Missing: V6 1.0", "Missing: V7 1.0", "" }, lines[8..11]);
        Assert.DoesNotContain(lines, line => line.Contains("show more"));
    }

    [Fact]
    public void GetNexusLinks_SectionCutShort_EndsWithAShowMoreLink()
    {
        List<ModLink> links = ComparisonTextTests.CreateText().GetNexusLinks(ManyComparison, verifiedIds: ManyVerifiedIds);

        Assert.Equal(new[] { "V1", "V2", "V3", "V4", "show more", "U1" }, links.Select(link => link.Name));
        Assert.Equal(new ModLink("show more", "", "...and 3 more (show more)", ExpandSection: ComparisonText.VerifiedSection), links[4]);
        Assert.All(links.Where(link => link.Name != "show more"), link => Assert.Equal(-1, link.ExpandSection));
    }

    [Fact]
    public void GetNexusLinks_ExpandedSection_LinksEveryModWithoutShowMore()
    {
        List<ModLink> links = ComparisonTextTests.CreateText().GetNexusLinks(ManyComparison, verifiedIds: ManyVerifiedIds, expandedSections: new[] { ComparisonText.VerifiedSection });

        Assert.Equal(new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V7", "U1" }, links.Select(link => link.Name));
    }

    [Fact]
    public void BuildRejectionMessage_UsesTheGamesTextFormat()
    {
        string message = ComparisonTextTests.CreateText().BuildRejectionMessage(Comparison, suggestInstall: false);

        // the game needs an 'asset:key' path for an asset which exists, and breaks lines on '^'
        Assert.StartsWith("Strings\\UI:^", message);
        Assert.DoesNotContain('\n', message);
        Assert.Equal(
            new[]
            {
                "Strings\\UI:",
                "Can't join: your mods don't match the host's.",
                "2 missing, 1 with a different version"
            },
            message.Split('^')
        );
    }

    [Fact]
    public void BuildRejectionMessage_SuggestInstall_SaysWhereToGetTheMod()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildRejectionMessage(Comparison, suggestInstall: true).Split('^');

        Assert.Equal(new[] { "Strings\\UI:", Translations["reject.install-hint"], "nexusmods.com/stardewvalley/mods/53850" }, lines);
        Assert.Contains("Mod Sync Check", lines[1]);
    }

    [Fact]
    public void BuildWarning_ThisModMismatched_SaysTheSameVersionIsRequired()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(ThisModComparison).Split('\n');

        Assert.Equal(
            new[]
            {
                "",
                "You need the host's Mod Sync Check 2.0.0 (you have 1.0.4).",
                "",
                ComparisonTextTests.Translate("client.section-unverified", new { count = 1 }),
                "Wrong version: Mod Sync Check 1.0.4 (host: 2.0.0)"
            },
            lines[1..]
        );
    }

    [Fact]
    public void BuildRejectionMessage_ThisModMismatched_SaysWhichVersionToGet()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildRejectionMessage(ThisModComparison, suggestInstall: false).Split('^');

        Assert.Equal(new[] { "Strings\\UI:", "Can't join: get Mod Sync Check 2.0.0:", "nexusmods.com/stardewvalley/mods/53850" }, lines);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, 45));
    }

    [Fact]
    public void BuildModRequiredMessage_NamesTheModAndWhereToGetIt()
    {
        string message = ComparisonTextTests.CreateText().BuildModRequiredMessage();

        Assert.Equal(new[] { "Strings\\UI:", Translations["reject.mod-required"], "nexusmods.com/stardewvalley/mods/53850" }, message.Split('^'));
        Assert.Contains("Mod Sync Check", message);
    }

    [Fact]
    public void GetDownloadAddress_IsTheNexusModsPage()
    {
        Assert.Equal("https://www.nexusmods.com/stardewvalley/mods/53850", ComparisonText.DownloadUrl);
        Assert.Equal(ComparisonText.DownloadUrl, "https://www." + ComparisonText.GetDownloadAddress());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectionMessages_FitInTheFarmhandSelectionBox(bool suggestInstall)
    {
        // many mismatched mods mustn't make the text longer, since it runs off the bottom of the box
        ModComparison comparison = Comparison with
        {
            Missing = Enumerable.Range(0, 40).Select(i => new ModInfo($"Example.Mod{i}", $"Example Mod {i}", "1.0.0")).ToArray()
        };
        ComparisonText text = ComparisonTextTests.CreateText();

        foreach (string message in new[] { text.BuildRejectionMessage(comparison, suggestInstall), text.BuildModRequiredMessage(), text.BuildCheckingMessage() })
        {
            string[] lines = message.Split('^')[1..];
            Assert.InRange(lines.Length, 1, ComparisonText.MaxRejectionLines);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, 45));
        }
    }

    [Fact]
    public void BuildCheckingMessage_UsesTheGamesTextFormat()
    {
        string message = ComparisonTextTests.CreateText().BuildCheckingMessage();

        Assert.Equal("Strings\\UI:^" + Translations["reject.checking"], message);
    }

    [Theory]
    [InlineData("1.0.4", 1, "1.0.4", 1)]
    [InlineData("1.0.5", 1, "1.0.4", 1)]
    [InlineData("1.0.4", 1, "1.0.5", 1)]
    [InlineData("1.9.0", 1, "1.0.4", 1)]
    [InlineData("1.0.4", 1, "1.9.0", 1)]
    [InlineData("1.0.0-beta", 1, "1.0.4", 1)]
    public void BuildVersionNotice_SameMajorVersion_ShowsNothing(string hostVersion, int hostMajorVersion, string version, int majorVersion)
    {
        Assert.Null(ComparisonTextTests.CreateText().BuildVersionNotice(hostVersion, hostMajorVersion, version, majorVersion));
    }

    [Theory]
    [InlineData("2.0.0", 2, "1.9.9", 1)]
    [InlineData("3.1.0", 3, "1.0.4", 1)]
    public void BuildVersionNotice_HostMajorVersionAhead_SaysTheHostIsNewer(string hostVersion, int hostMajorVersion, string version, int majorVersion)
    {
        string? notice = ComparisonTextTests.CreateText().BuildVersionNotice(hostVersion, hostMajorVersion, version, majorVersion);

        Assert.Equal(ComparisonTextTests.Translate("client.host-version-newer", new { hostVersion, version, url = ComparisonText.DownloadUrl }), notice);
        Assert.Contains($"{hostVersion}, you have {version}", notice);
    }

    [Theory]
    [InlineData("1.9.9", 1, "2.0.0", 2)]
    [InlineData("1.0.4", 1, "3.1.0", 3)]
    public void BuildVersionNotice_HostMajorVersionBehind_SaysTheHostIsOlder(string hostVersion, int hostMajorVersion, string version, int majorVersion)
    {
        string? notice = ComparisonTextTests.CreateText().BuildVersionNotice(hostVersion, hostMajorVersion, version, majorVersion);

        Assert.Equal(ComparisonTextTests.Translate("client.host-version-older", new { hostVersion, version, url = ComparisonText.DownloadUrl }), notice);
        Assert.Contains($"{hostVersion}, you have {version}", notice);
    }

    [Fact]
    public void GetNexusLinks_LinksModsWithANexusId_InListOrder()
    {
        ModComparison comparison = new(
            Missing: new[] { new ModInfo("Pathoschild.Automate", "Automate", "2.3.0", NexusId: 1063), new ModInfo("Example.Private", "Private Mod", "1.0.0") },
            VersionMismatches: new[] { new VersionMismatch("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0", "2.9.1", NexusId: 1915) }
        );

        Assert.Equal(
            new[]
            {
                new ModLink("Automate", "https://www.nexusmods.com/stardewvalley/mods/1063", "Missing: Automate 2.3.0"),
                new ModLink("Content Patcher", "https://www.nexusmods.com/stardewvalley/mods/1915", "Wrong version: Content Patcher 2.5.0 (host: 2.9.1)")
            },
            ComparisonTextTests.CreateText().GetNexusLinks(comparison)
        );
    }

    [Fact]
    public void GetNexusLinks_OnlyLinksTheListedMods()
    {
        // the links shouldn't name mods which the dialog leaves out
        List<ModLink> links = ComparisonTextTests.CreateText().GetNexusLinks(ManyComparison);

        Assert.Equal(new[] { "V1", "V2", "V3", "V4", "show more" }, links.Select(link => link.Name));
        Assert.Equal(ComparisonText.UnverifiedSection, links[4].ExpandSection);
    }

    [Fact]
    public void GetNexusLinks_WithVerifiedMods_FollowsTheSections()
    {
        ModComparison comparison = new(
            Missing: new[] { new ModInfo("A", "A", "1.0", NexusId: 1), new ModInfo("B", "B", "1.0", NexusId: 2), new ModInfo("C", "C", "1.0") },
            VersionMismatches: new[] { new VersionMismatch("D", "D", "1.0", "2.0", NexusId: 4) }
        );

        Assert.Equal(new[] { "B", "D", "A" }, ComparisonTextTests.CreateText().GetNexusLinks(comparison, verifiedIds: new[] { "b", "D" }).Select(link => link.Name));
    }

    [Fact]
    public void GetNexusLinks_InvalidIdFromTheHost_IsLeftOut()
    {
        ModComparison comparison = new(
            Missing: new[] { new ModInfo("A", "A", "1.0", NexusId: 0), new ModInfo("B", "B", "1.0", NexusId: -4) },
            VersionMismatches: Array.Empty<VersionMismatch>()
        );

        Assert.Empty(ComparisonTextTests.CreateText().GetNexusLinks(comparison));
    }

    /// <summary>Create the text builder using the mod's default translations.</summary>
    private static ComparisonText CreateText()
    {
        return new ComparisonText(ComparisonTextTests.Translate);
    }

    /// <summary>Get a default translation with its tokens replaced, like SMAPI does. This fails for a key which doesn't exist.</summary>
    private static string Translate(string key, object? tokens)
    {
        Assert.True(Translations.TryGetValue(key, out string? text), $"There's no translation with the key '{key}'.");

        if (tokens != null)
        {
            foreach (var property in tokens.GetType().GetProperties())
                text = text!.Replace("{{" + property.Name + "}}", property.GetValue(tokens)?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotContain("{{", text);
        return text!;
    }
}
