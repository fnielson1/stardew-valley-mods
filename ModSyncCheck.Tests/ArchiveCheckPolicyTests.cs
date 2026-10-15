using System;
using Xunit;

namespace ModSyncCheck.Tests;

public class ArchiveCheckPolicyTests
{
    // the values are passed by name, since test methods must be public and these types are internal
    [Theory]
    [InlineData(nameof(NexusCheckResult.Verified), nameof(ArchiveDecision.Install))]
    [InlineData(nameof(NexusCheckResult.Unknown), nameof(ArchiveDecision.Reject))]
    [InlineData(nameof(NexusCheckResult.WrongMod), nameof(ArchiveDecision.Reject))]
    [InlineData(nameof(NexusCheckResult.Unavailable), nameof(ArchiveDecision.Reject))]
    [InlineData(null, nameof(ArchiveDecision.Reject))]
    public void Decide_Untrusted_InstallsOnlyConfirmedArchives(string? result, string expected)
    {
        Assert.Equal(Enum.Parse<ArchiveDecision>(expected), ArchiveCheckPolicy.Decide(Parse(result), trustHost: false, hasNexusId: true));
    }

    [Theory]
    [InlineData(nameof(NexusCheckResult.Verified))]
    [InlineData(nameof(NexusCheckResult.Unavailable))]
    [InlineData(null)]
    public void Decide_Untrusted_WithoutNexusId_Rejects(string? result)
    {
        Assert.Equal(ArchiveDecision.Reject, ArchiveCheckPolicy.Decide(Parse(result), trustHost: false, hasNexusId: false));
    }

    [Theory]
    [InlineData(nameof(NexusCheckResult.Verified), nameof(ArchiveDecision.Install))]
    [InlineData(nameof(NexusCheckResult.Unknown), nameof(ArchiveDecision.InstallModified))]
    [InlineData(nameof(NexusCheckResult.WrongMod), nameof(ArchiveDecision.InstallModified))]
    [InlineData(nameof(NexusCheckResult.Unavailable), nameof(ArchiveDecision.InstallUnchecked))]
    [InlineData(null, nameof(ArchiveDecision.Install))]
    public void Decide_Trusted_InstallsTheHostsFiles(string? result, string expected)
    {
        ArchiveDecision decision = Enum.Parse<ArchiveDecision>(expected);

        Assert.Equal(decision, ArchiveCheckPolicy.Decide(Parse(result), trustHost: true, hasNexusId: true));
        Assert.Equal(decision, ArchiveCheckPolicy.Decide(Parse(result), trustHost: true, hasNexusId: false));
    }

    [Theory]
    [InlineData(nameof(NexusCheckResult.Unknown), true)]
    [InlineData(nameof(NexusCheckResult.WrongMod), true)]
    [InlineData(nameof(NexusCheckResult.Verified), false)]
    [InlineData(nameof(NexusCheckResult.Unavailable), false)]
    [InlineData(null, false)]
    public void IsModified_OnlyForFilesNexusModsDoesntMatchToTheMod(string? result, bool expected)
    {
        Assert.Equal(expected, ArchiveCheckPolicy.IsModified(Parse(result)));
    }

    /// <summary>Get a check result from its name, or <c>null</c> for a package of the host's mod folder.</summary>
    private static NexusCheckResult? Parse(string? name)
    {
        return name != null ? Enum.Parse<NexusCheckResult>(name) : null;
    }
}
