using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ModSyncCheck.Tests;

/// <summary>Checks that the messages survive being sent the way SMAPI sends mod messages: converted to a JSON token, sent as text, and read back as the message type.</summary>
public class SyncMessagesTests
{
    [Fact]
    public void BlockedMessage_RoundTrips()
    {
        BlockedMessage message = new(
            new ModComparison(
                Missing: new[] { new ModInfo("Pathoschild.Automate", "Automate", "2.3.0", NexusId: 1063), new ModInfo("Example.Private", "Private Mod", "1.0.0") },
                VersionMismatches: new[] { new VersionMismatch("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0", "2.9.1", NexusId: 1915) }
            ),
            CanDownload: true,
            DownloadBytes: 5_000_000_000,
            ConfigHashes: new Dictionary<string, Dictionary<string, string>>
            {
                ["cantorsdust.TimeSpeed"] = new() { ["config.json"] = "ABC123", ["[CP] Pack/config.json"] = "DEF456" }
            },
            ArchiveSizes: new Dictionary<string, long> { ["Pathoschild.Automate"] = 224_321 }
        );

        BlockedMessage result = SyncMessagesTests.RoundTrip(message);

        Assert.True(result.CanDownload);
        Assert.Equal(message.ConfigHashes!["cantorsdust.TimeSpeed"], Assert.Single(result.ConfigHashes!).Value);
        Assert.Equal(5_000_000_000, result.DownloadBytes);
        Assert.Equal(new KeyValuePair<string, long>("Pathoschild.Automate", 224_321), Assert.Single(result.ArchiveSizes!));
        Assert.Equal(message.Comparison.Missing, result.Comparison.Missing);
        Assert.Equal(message.Comparison.VersionMismatches, result.Comparison.VersionMismatches);
        Assert.Equal(new int?[] { 1063, null }, result.Comparison.Missing.Select(mod => mod.NexusId));
        Assert.Equal(1915, Assert.Single(result.Comparison.VersionMismatches).NexusId);
        Assert.Equal(3, result.Comparison.Count);
        Assert.Equal(new[] { "Pathoschild.Automate", "Example.Private", "Pathoschild.ContentPatcher" }, result.Comparison.GetNeededMods().Select(mod => mod.Id));
    }

    [Fact]
    public void BlockedMessage_FromAnOlderHost_HasNoNexusIdsOrArchiveSizes()
    {
        // an older host doesn't send the field at all
        const string sent = """{"Comparison":{"Missing":[{"Id":"Pathoschild.Automate","Name":"Automate","Version":"2.3.0"}],"VersionMismatches":[{"Id":"Pathoschild.ContentPatcher","Name":"Content Patcher","PlayerVersion":"2.5.0","HostVersion":"2.9.1"}]},"CanDownload":true,"DownloadBytes":100}""";

        BlockedMessage result = JToken.Parse(sent).ToObject<BlockedMessage>()!;

        Assert.Null(Assert.Single(result.Comparison.Missing).NexusId);
        Assert.Null(Assert.Single(result.Comparison.VersionMismatches).NexusId);
        Assert.Null(result.ArchiveSizes);
        Assert.True(result.CanDownload);
    }

    [Fact]
    public void BlockedMessage_WithoutConfigHashes_RoundTripsAsNull()
    {
        BlockedMessage message = new(new ModComparison(System.Array.Empty<ModInfo>(), System.Array.Empty<VersionMismatch>()), CanDownload: false, DownloadBytes: 0);

        Assert.Null(SyncMessagesTests.RoundTrip(message).ConfigHashes);
    }

    [Fact]
    public void EmptyMessages_RoundTrip()
    {
        Assert.NotNull(SyncMessagesTests.RoundTrip(new VerifiedMessage()));
        Assert.NotNull(SyncMessagesTests.RoundTrip(new SyncEndedMessage()));
    }

    [Fact]
    public void ChunkRequest_RoundTrips()
    {
        ChunkRequest message = new("Pathoschild.Automate", 3_000_000_000, ConfigOnly: true, AcceptArchive: true);

        Assert.Equal(message, SyncMessagesTests.RoundTrip(message));
    }

    [Fact]
    public void ChunkResponse_RoundTripsBinaryData()
    {
        byte[] data = Enumerable.Range(0, ChunkResponse.MaxDataSize).Select(i => (byte)(i * 31)).ToArray();
        ChunkResponse message = new("Pathoschild.Automate", 65536, 3_000_000_000, "Group\\Automate", data, Error: null);

        ChunkResponse result = SyncMessagesTests.RoundTrip(message);

        Assert.Equal(message.ModId, result.ModId);
        Assert.Equal(message.Offset, result.Offset);
        Assert.Equal(message.TotalBytes, result.TotalBytes);
        Assert.Equal(message.RelativePath, result.RelativePath);
        Assert.Equal(data, result.Data);
        Assert.Null(result.Error);
    }

    [Fact]
    public void ChunkResponse_RoundTripsError()
    {
        ChunkResponse result = SyncMessagesTests.RoundTrip(new ChunkResponse("Pathoschild.Automate", 0, 0, "", System.Array.Empty<byte>(), "The host can't send this mod."));

        Assert.Equal("The host can't send this mod.", result.Error);
        Assert.Empty(result.Data);
    }

    /// <summary>Send a message through JSON the way SMAPI does.</summary>
    internal static TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : notnull
    {
        string sent = JsonConvert.SerializeObject(JToken.FromObject(message), Formatting.None);
        return JToken.Parse(sent).ToObject<TMessage>()!;
    }
}
