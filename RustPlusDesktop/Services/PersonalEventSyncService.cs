using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RustPlusDesk.Services.Data;

namespace RustPlusDesk.Services;

/// <summary>
/// Mirrors paired servers into the personal_servers/personal_event_log Supabase tables so a
/// separate always-on Node.js bot can keep a persistent Rust+ connection per server and log
/// real event timestamps (heli, cargo, etc.) while the desktop app is closed. On next launch
/// the app reads that history back to replace "??:??" placeholders for events already in
/// progress. This is a personal tool for the developer's own account only — every call here is
/// gated on OwnerSteamId64 so other installations of this app never write to or read from it.
/// </summary>
public static class PersonalEventSyncService
{
    private const string OwnerSteamId64 = "76561198797467172";

    private static readonly HttpClient Http = new();
    private static string RestBase => $"{DataManager.SUPABASE_URL.TrimEnd('/')}/rest/v1";

    public static bool IsOwner(string? steamId64) =>
        !string.IsNullOrEmpty(steamId64) && steamId64 == OwnerSteamId64;

    public static string BuildServerKey(string host, int port, string steamId64) => $"{host}:{port}:{steamId64}";

    /// <summary>Upserts a paired server so the Node.js bot picks it up (fire-and-forget, best-effort).</summary>
    public static void SyncServer(string host, int port, string steamId64, string playerToken, string serverName)
    {
        if (!IsOwner(steamId64) || string.IsNullOrWhiteSpace(host)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    server_key = BuildServerKey(host, port, steamId64),
                    host,
                    port,
                    steam_id = steamId64,
                    player_token = playerToken,
                    server_name = serverName,
                };
                var req = new HttpRequestMessage(HttpMethod.Post, $"{RestBase}/personal_servers");
                req.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
                req.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await Http.SendAsync(req);
            }
            catch { /* best-effort: personal event history sync is never allowed to affect pairing */ }
        });
    }

    /// <summary>Deletes a removed server; personal_event_log rows cascade automatically.</summary>
    public static void DeleteServer(string host, int port, string steamId64)
    {
        if (!IsOwner(steamId64) || string.IsNullOrWhiteSpace(host)) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var key = BuildServerKey(host, port, steamId64);
                var url = $"{RestBase}/personal_servers?server_key=eq.{Uri.EscapeDataString(key)}";
                var req = new HttpRequestMessage(HttpMethod.Delete, url);
                req.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
                await Http.SendAsync(req);
            }
            catch { /* best-effort: personal event history sync is never allowed to affect deletion */ }
        });
    }

    /// <summary>
    /// Looks up the most recent real spawn time for an event type still in progress on this
    /// server, to replace a "??:??" placeholder. Returns null on any failure or if there's no
    /// matching row (e.g. the bot hasn't seen this event yet, or the server isn't tracked).
    /// </summary>
    public static async Task<DateTime?> GetLastEventTimeAsync(string host, int port, string steamId64, string eventType)
    {
        if (!IsOwner(steamId64) || string.IsNullOrWhiteSpace(host)) return null;

        try
        {
            var key = BuildServerKey(host, port, steamId64);
            var url = $"{RestBase}/personal_event_log" +
                       $"?server_key=eq.{Uri.EscapeDataString(key)}" +
                       $"&event_type=eq.{Uri.EscapeDataString(eventType)}" +
                       $"&order=occurred_at.desc&limit=1&select=occurred_at";

            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("apikey", DataManager.SUPABASE_ANON_KEY);
            var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            var body = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;

            // Every other spawn-time field in this codebase is stored/compared as UTC
            // (e.g. `_heliSpawnTime = DateTime.UtcNow` on a fresh spawn, and the UI does
            // `DateTime.UtcNow - _heliSpawnTime`). Returning LocalDateTime here made that
            // subtraction go negative by exactly the local UTC offset.
            var occurredAtStr = doc.RootElement[0].GetProperty("occurred_at").GetString();
            return DateTimeOffset.TryParse(occurredAtStr, out var dto) ? dto.UtcDateTime : null;
        }
        catch
        {
            return null;
        }
    }
}
