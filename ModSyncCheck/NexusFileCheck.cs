using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace ModSyncCheck;

/// <summary>The result of checking a downloaded archive against the files on Nexus Mods.</summary>
internal enum NexusCheckResult
{
    /// <summary>The archive is a file from the expected mod's Nexus Mods page.</summary>
    Verified,

    /// <summary>Nexus Mods doesn't have a file with the archive's hash, so it isn't an original download.</summary>
    Unknown,

    /// <summary>The archive is a file from Nexus Mods, but not from the expected mod's page.</summary>
    WrongMod,

    /// <summary>Nexus Mods couldn't be asked, e.g. because the player is offline.</summary>
    Unavailable
}

/// <summary>Checks whether an archive sent by the host is an unchanged file from Nexus Mods, by looking up its MD5 hash.</summary>
internal static class NexusFileCheck
{
    /*********
    ** Fields
    *********/
    /// <summary>The Nexus Mods API address which can look up a file hash without an API key.</summary>
    private const string ApiUrl = "https://api.nexusmods.com/v2/graphql";

    /// <summary>The Nexus Mods ID for Stardew Valley.</summary>
    public const int GameId = 1303;

    /// <summary>How long to wait for Nexus Mods to answer.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>The client used to ask Nexus Mods.</summary>
    private static readonly Lazy<HttpClient> Client = new(() =>
    {
        HttpClient client = new() { Timeout = NexusFileCheck.Timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ModSyncCheck (+https://www.nexusmods.com/stardewvalley/mods/53850)");
        return client;
    });


    /*********
    ** Public methods
    *********/
    /// <summary>Get the MD5 hash of a file as lowercase hex, which is how Nexus Mods stores it.</summary>
    /// <param name="path">The file path.</param>
    public static string GetMd5(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using MD5 md5 = MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>Check whether a file is an unchanged download from a mod's Nexus Mods page. This blocks until Nexus Mods answers, so it should be called in the background.</summary>
    /// <param name="path">The file path.</param>
    /// <param name="nexusId">The mod's Nexus Mods ID, or <c>null</c> to accept a file from any Stardew Valley mod page.</param>
    /// <param name="error">The reason Nexus Mods couldn't be asked, if applicable.</param>
    public static NexusCheckResult Check(string path, int? nexusId, out string? error)
    {
        string md5 = NexusFileCheck.GetMd5(path);

        string body = JsonSerializer.Serialize(new
        {
            query = "query ($md5: String!) { fileHash(md5: $md5) { gameId modFile { modId } } }",
            variables = new { md5 }
        });

        try
        {
            using StringContent content = new(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = NexusFileCheck.Client.Value.PostAsync(NexusFileCheck.ApiUrl, content).GetAwaiter().GetResult();
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                error = $"Nexus Mods replied with HTTP {(int)response.StatusCode}";
                return NexusCheckResult.Unavailable;
            }

            return NexusFileCheck.Classify(JsonNode.Parse(json), nexusId, out error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            error = ex.Message;
            return NexusCheckResult.Unavailable;
        }
    }

    /// <summary>Get the result of a hash lookup from the Nexus Mods reply.</summary>
    /// <param name="response">The reply from Nexus Mods.</param>
    /// <param name="nexusId">The mod's Nexus Mods ID, or <c>null</c> to accept a file from any Stardew Valley mod page.</param>
    /// <param name="error">The reason the reply couldn't be used, if applicable.</param>
    /// <remarks>The same file is often uploaded again to other pages, like modpacks, so it only needs to be on the expected page once.</remarks>
    public static NexusCheckResult Classify(JsonNode? response, int? nexusId, out string? error)
    {
        error = null;

        if (response?["data"]?["fileHash"] is not JsonArray files)
        {
            error = (response?["errors"] as JsonArray)?.FirstOrDefault()?["message"]?.ToString() ?? "Nexus Mods sent an unexpected reply";
            return NexusCheckResult.Unavailable;
        }

        int[] modIds = files
            .Where(file => NexusFileCheck.GetInt(file?["gameId"]) == NexusFileCheck.GameId)
            .Select(file => NexusFileCheck.GetInt(file?["modFile"]?["modId"]))
            .OfType<int>()
            .ToArray();

        if (modIds.Length == 0)
            return NexusCheckResult.Unknown;
        if (nexusId != null && !modIds.Contains(nexusId.Value))
            return NexusCheckResult.WrongMod;
        return NexusCheckResult.Verified;
    }


    /*********
    ** Private methods
    *********/
    /// <summary>Get a whole number from a reply field, if it has one.</summary>
    /// <param name="node">The field value.</param>
    private static int? GetInt(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out int number) ? number : null;
    }
}
