using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.PlayerWipeTracker;

/// <summary>
/// Talks to our own Supabase edge functions (<c>player-wipe-tracker</c>, <c>server-wipe-maps</c>)
/// via the same <see cref="SupabaseAuthManager.CallEdgeFunctionAsync"/> path every other cloud
/// feature uses — same auth, same anon key, same backend. No third-party service is involved.
/// </summary>
public sealed class PlayerWipeTrackerCloudClient
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<JsonDocument?> GetBootstrapAsync(CancellationToken cancellationToken = default)
        => await SendJsonAsync(HttpMethod.Get, "player-wipe-tracker/bootstrap").ConfigureAwait(false);

    public async Task<int> PutDayAsync(object payload, CancellationToken cancellationToken = default)
    {
        try
        {
            await RustPlusDesk.Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync(
                "player-wipe-tracker/days", HttpMethod.Put, payload).ConfigureAwait(false);
            return 200;
        }
        catch
        {
            return 599;
        }
    }

    public async Task<JsonDocument?> GetWipesAsync(CancellationToken cancellationToken = default)
        => await SendJsonAsync(HttpMethod.Get, "player-wipe-tracker/wipes").ConfigureAwait(false);

    public async Task<IReadOnlyList<CloudArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default)
    {
        using var document = await GetWipesAsync(cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Array.Empty<CloudArchiveSummary>();

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudArchiveSummary>();

        return data.EnumerateArray()
            .Select(ParseArchive)
            .Where(archive => archive is not null)
            .Cast<CloudArchiveSummary>()
            .ToArray();
    }

    public async Task<CloudArchiveSummary?> GetArchiveDetailsAsync(string archiveId, CancellationToken cancellationToken = default)
    {
        using var document = await SendJsonAsync(HttpMethod.Get, $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}").ConfigureAwait(false);
        if (document is null)
            return null;

        var data = UnwrapData(document);
        return data.ValueKind == JsonValueKind.Object ? ParseArchive(data) : null;
    }

    public async Task<IReadOnlyList<CloudRestoreDay>> GetRestoreDaysAsync(
        string archiveId,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        using var document = await GetPlayerDaysAsync(archiveId, steamId, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document is null)
            return Array.Empty<CloudRestoreDay>();

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Array)
            return Array.Empty<CloudRestoreDay>();

        var result = new List<CloudRestoreDay>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
                continue;

            var payload = payloadElement.Deserialize<CloudTrackerDayPayload>(_json);
            var day = String(item, "day");
            var playerSteamId = String(item, "player_steam_id");
            if (payload is null || string.IsNullOrWhiteSpace(day) || string.IsNullOrWhiteSpace(playerSteamId))
                continue;

            result.Add(new CloudRestoreDay(playerSteamId, String(item, "player_name"), day, payload));
        }

        return result;
    }

    public async Task<JsonDocument?> GetPlayerDaysAsync(string archiveId, string steamId, DateOnly? from = null, DateOnly? to = null, CancellationToken cancellationToken = default)
    {
        Dictionary<string, string>? query = null;
        if (from is not null || to is not null)
        {
            query = new Dictionary<string, string>();
            if (from is not null) query["from"] = from.Value.ToString("yyyy-MM-dd");
            if (to is not null) query["to"] = to.Value.ToString("yyyy-MM-dd");
        }

        return await SendJsonAsync(
            HttpMethod.Get,
            $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}/players/{Uri.EscapeDataString(steamId)}",
            query).ConfigureAwait(false);
    }

    public async Task<int> DeleteArchiveAsync(string archiveId, CancellationToken cancellationToken = default)
    {
        try
        {
            await RustPlusDesk.Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync(
                $"player-wipe-tracker/wipes/{Uri.EscapeDataString(archiveId)}", HttpMethod.Delete).ConfigureAwait(false);
            return 200;
        }
        catch
        {
            return 599;
        }
    }

    public async Task<int> DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await RustPlusDesk.Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync(
                "player-wipe-tracker", HttpMethod.Delete).ConfigureAwait(false);
            return 200;
        }
        catch
        {
            return 599;
        }
    }

    private static async Task<JsonDocument?> SendJsonAsync(HttpMethod method, string path, Dictionary<string, string>? query = null)
    {
        try
        {
            var body = await RustPlusDesk.Services.Auth.SupabaseAuthManager.CallEdgeFunctionAsync(path, method, null, query).ConfigureAwait(false);
            return JsonDocument.Parse(body);
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement UnwrapData(JsonDocument document)
        => document.RootElement.TryGetProperty("data", out var data) ? data : document.RootElement;

    private static readonly HttpClient Http = new(new TrafficTrackingHttpMessageHandler("Player Wipe Cloud")) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<TrackerWipeMap?> DownloadWipeMapAsync(string serverKey, string wipeKey, CancellationToken cancellationToken = default)
    {
        // Reads from the decoupled /server-wipe-maps API (the old
        // /player-wipe-tracker/maps routes were retired). Uploading lives in
        // ServerWipeMapCloudClient; this stays here only for the archive restore.
        using var document = await SendJsonAsync(
            HttpMethod.Get,
            $"server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}",
            null).ConfigureAwait(false);
        if (document is null)
            return null;

        var data = UnwrapData(document);
        if (data.ValueKind != JsonValueKind.Object)
            return null;

        var worldSize = Integer(data, "world_size");
        var rx = Double(data, "world_rect_x");
        var ry = Double(data, "world_rect_y");
        var rw = Double(data, "world_rect_width");
        var rh = Double(data, "world_rect_height");
        var oceanMargin = Double(data, "ocean_margin");
        var monuments = ParseMonuments(data, "monuments");
        monuments.AddRange(ParseMonuments(data, "extra_monuments"));

        byte[]? pngBytes = null;
        try
        {
            var url = $"{RustPlusDesk.Services.Data.DataManager.SUPABASE_URL.TrimEnd('/')}/functions/v1/server-wipe-maps/{Uri.EscapeDataString(serverKey)}/{Uri.EscapeDataString(wipeKey)}/image";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("apikey", RustPlusDesk.Services.Data.DataManager.SUPABASE_ANON_KEY);
            request.Headers.Add("X-Client-Version", Helpers.VersionHelper.GetClientVersion());
            var token = SupabaseAuthManager.Client?.Auth?.CurrentSession?.AccessToken;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                pngBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore map image download errors
        }

        return new TrackerWipeMap(
            pngBytes ?? Array.Empty<byte>(),
            worldSize,
            rx,
            ry,
            rw,
            rh,
            oceanMargin,
            monuments);
    }

    private static List<TrackerMonument> ParseMonuments(JsonElement data, string property)
    {
        var result = new List<TrackerMonument>();
        if (!data.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = String(item, "name");
            if (string.IsNullOrWhiteSpace(name))
                continue;

            result.Add(new TrackerMonument(name, Double(item, "x"), Double(item, "y"), String(item, "size")));
        }

        return result;
    }

    private static CloudArchiveSummary? ParseArchive(JsonElement item)
    {
        var id = String(item, "id");
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var serverKey = String(item, "server_key") ?? string.Empty;
        var serverName = String(item, "server_name") ?? serverKey;

        var players = new List<CloudArchivePlayer>();
        if (item.TryGetProperty("players", out var playerList) && playerList.ValueKind == JsonValueKind.Array)
        {
            foreach (var player in playerList.EnumerateArray())
            {
                var steamId = String(player, "steam_id");
                if (!string.IsNullOrWhiteSpace(steamId))
                {
                    players.Add(new CloudArchivePlayer(
                        steamId,
                        Integer(player, "day_count"),
                        String(player, "player_name"),
                        Boolean(player, "is_linked"),
                        String(player, "user_id"),
                        String(player, "display_name"),
                        String(player, "avatar_url")));
                }
            }
        }

        var hasMap = Boolean(item, "has_map");
        var mapUrl = String(item, "map_url");

        return new CloudArchiveSummary(
            id,
            serverKey,
            serverName,
            String(item, "wipe_key") ?? string.Empty,
            Date(item, "wipe_started_at"),
            Date(item, "first_observed_at"),
            Date(item, "last_observed_at"),
            NullableInteger(item, "player_count"),
            Long(item, "stored_bytes"),
            players,
            hasMap,
            mapUrl);
    }

    private static string? String(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Boolean(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.False ? false : false));

    private static double Double(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var result))
            return 0;
        return result;
    }

    private static DateTime? Date(JsonElement item, string property)
        => DateTimeOffset.TryParse(String(item, property), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value.UtcDateTime
            : null;

    private static int Integer(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            return 0;
        return result;
    }

    private static int? NullableInteger(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : null;

    private static long? Long(JsonElement item, string property)
        => item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : null;
}
