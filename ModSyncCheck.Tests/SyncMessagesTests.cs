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
                Missing: new[] { new ModInfo("Pathoschild.Automate", "Automate", "2.3.0") },
                VersionMismatches: new[] { new VersionMismatch("Pathoschild.ContentPatcher", "Content Patcher", "2.5.0", "2.9.1") }
            ),
            CanDownload: true,
            DownloadBytes: 5_000_000_000,
            ConfigHashes: new Dictionary<string, Dictionary<string, string>>
            {
                ["cantorsdust.TimeSpeed"] = new() { ["config.json"] = "ABC123", ["[CP] Pack/config.json"] = "DEF456" }
            }
        );

        BlockedMessage result = SyncMessagesTests.RoundTrip(message);

        Assert.True(result.CanDownload);
        Assert.Equal(message.ConfigHashes!["cantorsdust.TimeSpeed"], Assert.Single(result.ConfigHashes!).Value);
        Assert.Equal(5_000_000_000, result.DownloadBytes);
        Assert.Equal(message.Comparison.Missing, result.Comparison.Missing);
        Assert.Equal(message.Comparison.VersionMismatches, result.Comparison.VersionMismatches);
        Assert.Equal(2, result.Comparison.Count);
        Assert.Equal(new[] { "Pathoschild.Automate", "Pathoschild.ContentPatcher" }, result.Comparison.GetNeededMods().Select(mod => mod.Id));
    }

    [Fact]
    public void BlockedMessage_WithoutConfigHashes_RoundTripsAsNull()
    {
        BlockedMessage message = new(new ModComparison(System.Array.Empty<ModInfo>(), System.Array.Empty<VersionMismatch>()), CanDownload: false, DownloadBytes: 0);

        Assert.Null(SyncMessagesTests.RoundTrip(message).ConfigHashes);
    }

    [Fact]
    public void ChunkRequest_ConfigOnly_RoundTrips()
    {
        ChunkRequest result = SyncMessagesTests.RoundTrip(new ChunkRequest("Pathoschild.Automate", 0, ConfigOnly: true));

        Assert.True(result.ConfigOnly);
    }

    [Fact]
    public void VerifiedMessage_RoundTrips()
    {
        Assert.NotNull(SyncMessagesTests.RoundTrip(new VerifiedMessage()));
    }

    [Fact]
    public void ChunkRequest_RoundTrips()
    {
        ChunkRequest result = SyncMessagesTests.RoundTrip(new ChunkRequest("Pathoschild.Automate", 3_000_000_000));

        Assert.Equal(new ChunkRequest("Pathoschild.Automate", 3_000_000_000), result);
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

    [Fact]
    public void SyncEndedMessage_RoundTrips()
    {
        Assert.NotNull(SyncMessagesTests.RoundTrip(new SyncEndedMessage()));
    }

    /// <summary>Send a message through JSON the way SMAPI does.</summary>
    internal static TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : notnull
    {
        string sent = JsonConvert.SerializeObject(JToken.FromObject(message), Formatting.None);
        return JToken.Parse(sent).ToObject<TMessage>()!;
    }
}
