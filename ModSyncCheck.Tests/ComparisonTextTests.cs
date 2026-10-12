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
        Missing: new[] { new ModInfo("Pathoschild.Automate", "Automate", "2.3.0") },
        Extra: new[] { new ModInfo("cantorsdust.TimeSpeed", "TimeSpeed", "2.8.0") },
        VersionMismatches: new[] { new VersionMismatch("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0", "2.9.1") }
    );

    [Fact]
    public void GetCounts_ListsEachType()
    {
        Assert.Equal("1 missing, 1 extra, 1 with a different version", ComparisonTextTests.CreateText().GetCounts(Comparison));
    }

    [Fact]
    public void GetCounts_LeavesOutEmptyTypes()
    {
        ModComparison comparison = Comparison with { Missing = Array.Empty<ModInfo>(), VersionMismatches = Array.Empty<VersionMismatch>() };

        Assert.Equal("1 extra", ComparisonTextTests.CreateText().GetCounts(comparison));
    }

    [Fact]
    public void GetEntries_DescribesEachMod()
    {
        string[] entries = ComparisonTextTests.CreateText().GetEntries(Comparison).ToArray();

        Assert.Equal(
            new[]
            {
                "Missing: Automate 2.3.0",
                "Not on host: TimeSpeed",
                "Wrong version: Content Patcher 2.5.0 (host has 2.9.1)"
            },
            entries
        );
    }

    [Fact]
    public void BuildWarning_ListsCountsAndMods()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildWarning(Comparison).Split('\n');

        Assert.Contains("1 missing, 1 extra, 1 with a different version", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal(new[] { "Missing: Automate 2.3.0", "Not on host: TimeSpeed", "Wrong version: Content Patcher 2.5.0 (host has 2.9.1)" }, lines[2..]);
    }

    [Fact]
    public void BuildWarning_OverTheLimit_CutsOffTheListWithACount()
    {
        string[] lines = ComparisonTextTests.CreateText(maxMods: 2).BuildWarning(Comparison).Split('\n');

        Assert.Equal(new[] { "Missing: Automate 2.3.0", "Not on host: TimeSpeed", "...and 1 more (see the SMAPI log)." }, lines[2..]);
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
                "Missing: Automate 2.3.0",
                "Not on host: TimeSpeed",
                "Wrong version: Content Patcher 2.5.0 (host has 2.9.1)"
            },
            message.Split('^')
        );
    }

    [Fact]
    public void BuildRejectionMessage_SuggestInstall_AddsTheHint()
    {
        string[] lines = ComparisonTextTests.CreateText().BuildRejectionMessage(Comparison, suggestInstall: true).Split('^');

        Assert.Equal(new[] { "", Translations["reject.install-hint-1"], Translations["reject.install-hint-2"] }, lines[^3..]);
        Assert.Contains("Mod Sync Check", lines[^2]);
    }

    [Fact]
    public void BuildRejectionMessage_OverTheLimit_CutsOffTheListWithoutMentioningTheLog()
    {
        string[] lines = ComparisonTextTests.CreateText(maxMods: 1).BuildRejectionMessage(Comparison, suggestInstall: false).Split('^');

        Assert.Equal(new[] { "Missing: Automate 2.3.0", "...and 2 more." }, lines[2..]);
    }

    [Fact]
    public void BuildRejectionMessage_ModNameWithLineBreakCharacter_IsKeptOnOneLine()
    {
        ModComparison comparison = Comparison with { Missing = new[] { new ModInfo("Example.Mod", "Up^Down", "1.0.0") } };

        string[] lines = ComparisonTextTests.CreateText().BuildRejectionMessage(comparison, suggestInstall: false).Split('^');

        Assert.Equal("Missing: Up Down 1.0.0", lines[2]);
        Assert.Equal(5, lines.Length);
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
