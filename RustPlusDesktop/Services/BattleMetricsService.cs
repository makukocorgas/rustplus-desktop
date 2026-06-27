// ============================================================
// BattleMetricsService.cs
// Localização: RustPlusDesktop/Services/BattleMetricsService.cs
//
// Integração com a API pública do BattleMetrics:
//   • Pesquisar jogadores por nome
//   • Obter perfil de um jogador por BM ID
//   • Verificar se um jogador está online num servidor específico
//   • Obter última sessão de um jogador
//   • Pesquisar um servidor Rust por IP:Port para obter o BM Server ID
// ============================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services
{
    public class BmPlayerResult
    {
        public string BmId       { get; set; } = string.Empty;
        public string Name       { get; set; } = string.Empty;
        public bool   IsOnline   { get; set; }
        public string? ServerName { get; set; }
        public DateTime? LastSeen { get; set; }
    }

    public class BmSessionResult
    {
        public DateTime ConnectTime    { get; set; }
        public DateTime? DisconnectTime { get; set; }
        public string   ServerId       { get; set; } = string.Empty;
        public string   ServerName     { get; set; } = string.Empty;
    }

    public static class BattleMetricsService
    {
        private static readonly HttpClient _http = new()
        {
            DefaultRequestHeaders = { { "User-Agent", "RustPlusDesktop/7.1 (github.com/makukocorgas/rustplus-desktop)" } }
        };

        private const string BaseUrl = "https://api.battlemetrics.com";

        // Cache simples para evitar hammer na API
        private static readonly Dictionary<string, (BmPlayerResult result, DateTime expiry)> _playerCache = new();
        private static readonly Dictionary<string, (string bmId, DateTime expiry)> _serverCache = new();
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // ────────────────────────────────────────────────────────
        // Pesquisar jogador por nome — devolve os primeiros resultados
        // ────────────────────────────────────────────────────────
        public static async Task<List<BmPlayerResult>> SearchPlayersByNameAsync(
            string name,
            string? serverBmId = null,
            int maxResults = 10,
            CancellationToken ct = default)
        {
            try
            {
                var url = $"{BaseUrl}/players?filter[search]={Uri.EscapeDataString(name)}&filter[game]=rust&page[size]={maxResults}";
                if (!string.IsNullOrEmpty(serverBmId))
                    url += $"&filter[servers]={serverBmId}";

                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return new();

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                var results = new List<BmPlayerResult>();
                if (!doc.RootElement.TryGetProperty("data", out var data)) return results;

                foreach (var item in data.EnumerateArray())
                {
                    var attrs = item.GetProperty("attributes");
                    results.Add(new BmPlayerResult
                    {
                        BmId     = item.GetProperty("id").GetString() ?? "",
                        Name     = attrs.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        IsOnline = attrs.TryGetProperty("online", out var o) && o.GetBoolean(),
                    });
                }
                return results;
            }
            catch { return new(); }
        }

        // ────────────────────────────────────────────────────────
        // Obter perfil completo de um jogador por BM ID
        // Inclui estado online e nome do servidor actual
        // ────────────────────────────────────────────────────────
        public static async Task<BmPlayerResult?> GetPlayerProfileAsync(
            string bmId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(bmId)) return null;

            await _lock.WaitAsync(ct);
            try
            {
                if (_playerCache.TryGetValue(bmId, out var cached) && DateTime.UtcNow < cached.expiry)
                    return cached.result;
            }
            finally { _lock.Release(); }

            try
            {
                using var resp = await _http.GetAsync($"{BaseUrl}/players/{bmId}?include=server", ct);
                if (!resp.IsSuccessStatusCode) return null;

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;
                var attrs = data.GetProperty("attributes");

                var result = new BmPlayerResult
                {
                    BmId     = bmId,
                    Name     = attrs.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    IsOnline = attrs.TryGetProperty("online", out var o) && o.GetBoolean(),
                };

                // Tentar obter servidor actual do include
                if (doc.RootElement.TryGetProperty("included", out var included))
                {
                    foreach (var inc in included.EnumerateArray())
                    {
                        if (inc.TryGetProperty("type", out var t) && t.GetString() == "server"
                            && inc.TryGetProperty("attributes", out var sa))
                        {
                            result.ServerName = sa.TryGetProperty("name", out var sn) ? sn.GetString() : null;
                            break;
                        }
                    }
                }

                await _lock.WaitAsync(ct);
                try { _playerCache[bmId] = (result, DateTime.UtcNow.AddMinutes(2)); }
                finally { _lock.Release(); }

                return result;
            }
            catch { return null; }
        }

        // ────────────────────────────────────────────────────────
        // Última sessão de um jogador (ConnectTime e DisconnectTime)
        // ────────────────────────────────────────────────────────
        public static async Task<BmSessionResult?> GetLastSessionAsync(
            string bmId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(bmId)) return null;
            try
            {
                var url = $"{BaseUrl}/players/{bmId}/relationships/sessions?page[size]=1&filter[game]=rust&include=server";
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

                var sessions = data.EnumerateArray().ToList();
                if (sessions.Count == 0) return null;

                var first = sessions[0];
                var attrs = first.GetProperty("attributes");

                DateTime? stop = null;
                if (attrs.TryGetProperty("stop", out var stopEl) && stopEl.ValueKind != JsonValueKind.Null)
                    DateTime.TryParse(stopEl.GetString(), out var dt).Then(() => stop = dt.ToUniversalTime());

                DateTime start = DateTime.UtcNow;
                if (attrs.TryGetProperty("start", out var startEl) && startEl.ValueKind != JsonValueKind.Null)
                    DateTime.TryParse(startEl.GetString(), out start);

                // Tentar apanhar nome do servidor via included
                string serverId = "", serverName = "";
                if (first.TryGetProperty("relationships", out var rels)
                    && rels.TryGetProperty("server", out var srvRel)
                    && srvRel.TryGetProperty("data", out var srvData))
                {
                    serverId = srvData.TryGetProperty("id", out var sid) ? sid.GetString() ?? "" : "";
                }

                if (doc.RootElement.TryGetProperty("included", out var included))
                {
                    foreach (var inc in included.EnumerateArray())
                    {
                        if (inc.TryGetProperty("id", out var iid) && iid.GetString() == serverId
                            && inc.TryGetProperty("attributes", out var ia))
                        {
                            serverName = ia.TryGetProperty("name", out var sn) ? sn.GetString() ?? "" : "";
                            break;
                        }
                    }
                }

                return new BmSessionResult
                {
                    ConnectTime    = start.ToUniversalTime(),
                    DisconnectTime = stop,
                    ServerId       = serverId,
                    ServerName     = serverName,
                };
            }
            catch { return null; }
        }

        // ────────────────────────────────────────────────────────
        // Descobre o BM Server ID a partir de IP:Port
        // Usado para filtrar pesquisas de jogadores ao servidor actual
        // ────────────────────────────────────────────────────────
        public static async Task<string?> FindServerBmIdAsync(
            string host,
            int port,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(host)) return null;

            var key = $"{host}:{port}";
            await _lock.WaitAsync(ct);
            try
            {
                if (_serverCache.TryGetValue(key, out var cached) && DateTime.UtcNow < cached.expiry)
                    return cached.bmId;
            }
            finally { _lock.Release(); }

            try
            {
                // Pesquisar por IP+port no BM
                var url = $"{BaseUrl}/servers?filter[game]=rust&filter[search]={Uri.EscapeDataString(host)}&page[size]=20";
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return null;

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("data", out var data)) return null;

                string? foundId = null;
                foreach (var srv in data.EnumerateArray())
                {
                    if (!srv.TryGetProperty("attributes", out var attrs)) continue;

                    var srvIp   = attrs.TryGetProperty("ip", out var ipEl)   ? ipEl.GetString()   : null;
                    var srvPort = attrs.TryGetProperty("port", out var portEl) ? portEl.GetInt32()  : 0;

                    if (string.Equals(srvIp, host, StringComparison.OrdinalIgnoreCase)
                        && (srvPort == port || Math.Abs(srvPort - port) <= 2))
                    {
                        foundId = srv.GetProperty("id").GetString();
                        break;
                    }
                }

                if (foundId != null)
                {
                    await _lock.WaitAsync(ct);
                    try { _serverCache[key] = (foundId, DateTime.UtcNow.AddMinutes(30)); }
                    finally { _lock.Release(); }
                }

                return foundId;
            }
            catch { return null; }
        }

        // ────────────────────────────────────────────────────────
        // Verificar se um jogador está online num servidor específico
        // Útil para alertas de tracking BM-only
        // ────────────────────────────────────────────────────────
        public static async Task<bool> IsPlayerOnServerAsync(
            string bmPlayerId,
            string bmServerId,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(bmPlayerId) || string.IsNullOrEmpty(bmServerId)) return false;
            try
            {
                var url = $"{BaseUrl}/players/{bmPlayerId}?include=server";
                using var resp = await _http.GetAsync(url, ct);
                if (!resp.IsSuccessStatusCode) return false;

                var body = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(body);

                if (!doc.RootElement.TryGetProperty("data", out var data)) return false;
                var attrs = data.GetProperty("attributes");

                if (!attrs.TryGetProperty("online", out var o) || !o.GetBoolean()) return false;

                // Verificar se está no servidor correcto
                if (!doc.RootElement.TryGetProperty("included", out var included)) return false;
                foreach (var inc in included.EnumerateArray())
                {
                    if (inc.TryGetProperty("type", out var t) && t.GetString() == "server"
                        && inc.TryGetProperty("id", out var sid) && sid.GetString() == bmServerId)
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        // Helper para correcção de bool
        public static void InvalidatePlayerCache(string bmId)
        {
            _lock.Wait();
            try { _playerCache.Remove(bmId); }
            finally { _lock.Release(); }
        }

        public static string BuildProfileUrl(string bmId) =>
            $"https://www.battlemetrics.com/players/{bmId}";

        public static string BuildServerUrl(string bmServerId) =>
            $"https://www.battlemetrics.com/servers/rust/{bmServerId}";

        /// <summary>
        /// Detecta se o input é um BM ID numérico (só dígitos) vs um nome de jogador
        /// </summary>
        public static bool IsBmId(string input) =>
            !string.IsNullOrWhiteSpace(input) &&
            input.All(char.IsDigit) &&
            input.Length >= 5 && input.Length <= 15;

        /// <summary>
        /// Detecta se é um SteamID64 válido
        /// </summary>
        public static bool IsSteamId64(string input) =>
            input.Length == 17 &&
            input.StartsWith("7656") &&
            ulong.TryParse(input, out _);
    }
}

// Extensão helper interna
file static class BoolExt
{
    public static void Then(this bool b, Action a) { if (b) a(); }
}
