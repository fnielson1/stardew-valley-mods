using System;
using System.IO;
using System.Text.Json.Nodes;
using Xunit;

namespace ModSyncCheck.Tests;

public class NexusFileCheckTests
{
    /// <summary>A real reply for the SMAPI 4.5.2 installer, which was also uploaded again to two other pages.</summary>
    private const string SmapiReply = """
        {"data":{"fileHash":[
            {"gameId":1303,"modFile":{"modId":2400}},
            {"gameId":1303,"modFile":{"modId":46467}},
            {"gameId":1303,"modFile":{"modId":50518}}
        ]}}
        """;

    [Theory]
    [InlineData(2400)]
    [InlineData(46467)]
    public void Classify_FileOnTheExpectedPage_IsVerified(int nexusId)
    {
        Assert.Equal(NexusCheckResult.Verified, NexusFileCheck.Classify(JsonNode.Parse(SmapiReply), nexusId, out string? error));
        Assert.Null(error);
    }

    [Fact]
    public void Classify_FileOnlyOnOtherPages_IsWrongMod()
    {
        Assert.Equal(NexusCheckResult.WrongMod, NexusFileCheck.Classify(JsonNode.Parse(SmapiReply), 1915, out _));
    }

    [Fact]
    public void Classify_ModWithoutNexusId_AcceptsAnyStardewValleyFile()
    {
        Assert.Equal(NexusCheckResult.Verified, NexusFileCheck.Classify(JsonNode.Parse(SmapiReply), null, out _));
    }

    [Fact]
    public void Classify_NoMatches_IsUnknown()
    {
        Assert.Equal(NexusCheckResult.Unknown, NexusFileCheck.Classify(JsonNode.Parse("""{"data":{"fileHash":[]}}"""), 2400, out _));
    }

    [Fact]
    public void Classify_FileFromAnotherGame_IsUnknown()
    {
        const string reply = """{"data":{"fileHash":[{"gameId":110,"modFile":{"modId":2400}}]}}""";

        Assert.Equal(NexusCheckResult.Unknown, NexusFileCheck.Classify(JsonNode.Parse(reply), 2400, out _));
    }

    [Fact]
    public void Classify_ErrorReply_IsUnavailable_WithTheReason()
    {
        const string reply = """{"errors":[{"message":"Rate limit exceeded"}]}""";

        Assert.Equal(NexusCheckResult.Unavailable, NexusFileCheck.Classify(JsonNode.Parse(reply), 2400, out string? error));
        Assert.Equal("Rate limit exceeded", error);
    }

    [Fact]
    public void GetMd5_MatchesTheKnownHash()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "hello");
            Assert.Equal("5d41402abc4b2a76b9719d911017c592", NexusFileCheck.GetMd5(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
