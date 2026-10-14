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
                "Wrong version: Content Patcher 2.5.0 (host has 2.9.1)"
            },
            entries
        );
    }

    [Fact]
    public void BuildWarning_ListsCountsAndMods()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(Comparison).Split('\n');

        Assert.Contains("2 missing, 1 with a different version", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal(new[] { "Missing: Automate 2.3.0", "Missing: TimeSpeed 2.8.0", "Wrong version: Content Patcher 2.5.0 (host has 2.9.1)" }, lines[2..]);
    }

    [Fact]
    public void BuildWarning_OverTheLimit_CutsOffTheListWithACount()
    {
        string[] lines = ComparisonTextTests.CreateText(maxMods: 2).BuildWarning(Comparison).Split('\n');

        Assert.Equal(new[] { "Missing: Automate 2.3.0", "Missing: TimeSpeed 2.8.0", "...and 1 more (see the SMAPI log)." }, lines[2..]);
    }

    [Fact]
    public void BuildWarning_AtTheLimit_ListsEverything()
    {
        string[] lines = ComparisonTextTests.CreateText(maxMods: 3).BuildWarning(Comparison).Split('\n');

        Assert.Equal(3, lines[2..].Length);
        Assert.DoesNotContain(lines, line => line.Contains("more"));
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

        Assert.Equal(new[] { "Strings\\UI:", Translations["reject.install-hint"], "github.com/fnielson1/stardew-valley-mods", "/releases/latest" }, lines);
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
                "You must have the same version of Mod Sync Check as the host (2.0.0, you have 1.0.4).",
                "",
                "Wrong version: Mod Sync Check 1.0.4 (host has 2.0.0)"
            },
            lines[1..]
        );
    }

    [Fact]
    public void BuildRejectionMessage_ThisModMismatched_SaysWhichVersionToGet()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildRejectionMessage(ThisModComparison, suggestInstall: false).Split('^');

        Assert.Equal(new[] { "Strings\\UI:", "Can't join: get Mod Sync Check 2.0.0:", "github.com/fnielson1/stardew-valley-mods", "/releases/latest" }, lines);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, 45));
    }

    [Fact]
    public void BuildModRequiredMessage_NamesTheModAndWhereToGetIt()
    {
        string message = ComparisonTextTests.CreateText().BuildModRequiredMessage();

        Assert.Equal(new[] { "Strings\\UI:", Translations["reject.mod-required"], "github.com/fnielson1/stardew-valley-mods", "/releases/latest" }, message.Split('^'));
        Assert.Contains("Mod Sync Check", message);
    }

    [Fact]
    public void GetDownloadUrlLines_JoinBackIntoTheDownloadPage()
    {
        Assert.Equal("https://github.com/fnielson1/stardew-valley-mods/releases/latest", ComparisonText.DownloadUrl);
        Assert.Equal(ComparisonText.DownloadUrl, "https://" + string.Concat(ComparisonText.GetDownloadUrlLines()));
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

    /// <summary>Create the text builder using the mod's default translations.</summary>
    /// <param name="maxMods">The maximum number of mods to list.</param>
    private static ComparisonText CreateText(int maxMods = 10)
    {
        return new ComparisonText(ComparisonTextTests.Translate, new ModConfig { MaxModsInPopup = maxMods });
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
