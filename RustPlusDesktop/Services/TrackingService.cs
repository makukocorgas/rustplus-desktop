using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services;

public class TrackedPlayer
{
    public string BMId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LastServerName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string GroupColor { get; set; } = string.Empty;
    public List<PlayerSession> Sessions { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsOnline { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string PlayTimeStr { get; set; } = string.Empty;

    public bool IsBMOnly { get; set; } = false;

    public TrackedPlayer CloneWithSnapshots()
    {
        lock (Sessions) // Extra safety for the list itself
        {
            return new TrackedPlayer
            {
                BMId = this.BMId,
                Name = this.Name,
                LastServerName = this.LastServerName,
                GroupName = this.GroupName,
                GroupColor = this.GroupColor,
                IsBMOnly = this.IsBMOnly,
                Sessions = this.Sessions.ToList() // Take snapshot of sessions
            };
        }
    }
}

public class HarborInfo
{
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
}

public class CargoTriggerPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class TrackingSettings
{
    public string LastHost { get; set; } = string.Empty;
    public int LastPort { get; set; }
    public string LastServerName { get; set; } = string.Empty;
    public string? LastBMId { get; set; } = null;
    public bool MapShowSteamMarkers { get; set; } = true;
    public bool MapShowPlayerArrows { get; set; } = true;
    public bool MapShowDeathTags { get; set; } = false;
    public bool MapShowDeathHeatmap { get; set; } = false;
    public int MaxSelfDeathMarkers { get; set; } = 3;
    public int MaxTeamDeathMarkers { get; set; } = 3;
    public bool MapAbbreviateNames { get; set; } = false;
    public double MapPlayerIconScale { get; set; } = 1.0;
    public bool MapUseMonumentText { get; set; } = false;
    public int MapMonumentDisplayMode { get; set; } = 0;
    public double MapMonumentScale { get; set; } = 1.0;
    public double MapMonumentOpacity { get; set; } = 1.0;
    public double MapGridOpacity { get; set; } = 0.7;
    public List<string> HiddenExtraMonumentTypes { get; set; } = new();
    public bool BackgroundTrackingEnabled { get; set; } = true;
    public bool CloseToTrayEnabled { get; set; } = false;
    public bool StartMinimizedEnabled { get; set; } = false;
    public bool AutoConnectEnabled { get; set; } = false;
    public bool AutoStartEnabled { get; set; } = false;
    public bool AutoLoadShops { get; set; } = true;
    public bool HideConsole { get; set; } = true;
    public string BattleMetricsApiKey { get; set; } = "";
    public double SidebarWidth { get; set; } = 420;
    public bool SidebarPinned { get; set; } = true;
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public double? WindowLeft { get; set; } = null;
    public double? WindowTop { get; set; } = null;
    public bool WindowMaximized { get; set; } = false;
    public string SteamId64 { get; set; } = string.Empty;
    public bool AnnounceCargo { get; set; } = false;
    public bool AnnounceHeli { get; set; } = false;
    public bool AnnounceChinook { get; set; } = false;
    public bool AnnounceVendor { get; set; } = false;
    public bool AnnounceOilRig { get; set; } = false;
    public bool AnnounceDeepSea { get; set; } = false;

    /// <summary>
    /// Listen to the game's audio for server-wide monument cues on servers that no longer
    /// send event markers over Rust+. On by default: without it those servers show nothing at
    /// all, and the listener only runs while Rust itself is running.
    /// </summary>
    public bool ListenForServerEvents { get; set; } = true;

    /// <summary>
    /// Treat a cue this client heard itself as true, without waiting for another player to
    /// corroborate it.
    ///
    /// On by default, because the alternative is refusing to show someone an event they
    /// personally just heard. Corroboration exists to stop one client speaking for a whole
    /// server; it was never meant to stop a client speaking for itself. Reporting is
    /// unaffected — the backend still applies its own rules to what everyone else sees.
    /// </summary>
    public bool TrustOwnDetections { get; set; } = true;

    public bool AnnouncePlayerOnline { get; set; } = false;
    public bool AnnouncePlayerOffline { get; set; } = false;
    public bool AnnouncePlayerAfk { get; set; } = false;
    public bool AnnouncePlayerAfkReturn { get; set; } = false;
    public int AfkAlertMinutes { get; set; } = 5;
    public bool AnnouncePlayerDeathSelf { get; set; } = false;
    public bool AnnouncePlayerDeathTeam { get; set; } = false;
    public bool AnnouncePlayerRespawnSelf { get; set; } = false;
    public bool AnnouncePlayerRespawnTeam { get; set; } = false;
    public bool AnnounceNewShops { get; set; } = false;
    public bool AnnounceSuspiciousShops { get; set; } = false;
    public bool AnnounceTradeAlerts { get; set; } = false;
    public string SelectedLanguage { get; set; } = "";
    public Dictionary<string, bool> GroupStates { get; set; } = new();
    public Dictionary<string, List<string>> GroupOrder { get; set; } = new();
    public bool AnnounceCargoDocking { get; set; } = false;
    public bool AnnounceCargoEgress { get; set; } = false;
    public bool AnnounceCargoArrival { get; set; } = false;
    public bool AnnounceCargoArrivalUserSet { get; set; } = false;
    public bool AnnounceSmartAlerts { get; set; } = false;
    public bool GenericAlarmPopupEnabled { get; set; } = true;
    public bool GenericAlarmOverlayEnabled { get; set; } = true;
    public bool GenericAlarmAudioEnabled { get; set; } = true;
    public string GenericAlarmAudioFilePath { get; set; } = string.Empty;
    public Dictionary<string, int> LearnedDockingDurations { get; set; } = new();
    public Dictionary<string, int> LearnedCargoFullLifeMinutes { get; set; } = new();
    public Dictionary<string, int> LearnedCargoTravelMinutes { get; set; } = new();
    public Dictionary<string, List<HarborInfo>> ServerHarbors { get; set; } = new();
    public Dictionary<string, Dictionary<string, CargoTriggerPoint>> ServerCargoTriggers { get; set; } = new();
    public bool AnnounceSpawnsMaster { get; set; } = false;
    public bool ChatMasterOfferSoundEnabled { get; set; } = true;
    public bool SaveAlertSelection { get; set; } = true;
    public DateTime? FcmIssuedAt { get; set; }
    public DateTime? FcmExpiresAt { get; set; }
    public bool AnnounceTracking { get; set; } = false;
    public Dictionary<string, int> LearnedQueryPorts { get; set; } = new();
    public bool TranslationConsentGiven { get; set; } = false;
    public bool UploadConsentGiven { get; set; } = false;
    public bool CloudSyncEnabled { get; set; } = false;
    // Key = "host:port|entityId", value = true if that device should send a chat alert when toggled via hotkey
    public Dictionary<string, bool> HotkeyTriggerChatAlertEnabled { get; set; } = new();
    public bool HotkeyTriggerChatAlertsEnabled { get; set; } = true;
    public string LastCrosshairStyle { get; set; } = "GreenDot";
    public string LastCustomCrosshairId { get; set; } = string.Empty;
    public bool OfflineDeathAlertsEnabled { get; set; } = true;
    public string OfflineDeathSoundPath { get; set; } = string.Empty;
    public bool OfflineDeathSoundLoopEnabled { get; set; } = false;
    public bool OfflineDeathDiscordEnabled { get; set; } = false;
    public List<OfflineDeathNotification> OfflineDeathHistory { get; set; } = new();
    
    // Notifications Center Settings
    public bool NotificationsToastEnabled { get; set; } = true;
    public bool NotificationsSoundsEnabled { get; set; } = true;
    public int NotificationsRetentionDays { get; set; } = 30;
    public List<string> MutedNotificationServers { get; set; } = new();
    public Dictionary<string, string> MutedNotificationServerNames { get; set; } = new();
    public Dictionary<string, ulong> ServerFollowingSteamId { get; set; } = new();

    public int MapBitmapScalingMode { get; set; } = 0;
    public bool MapUseCacheMode { get; set; } = false;
    public double MapRenderScale { get; set; } = 1.0;
    public bool MapUseAliasedEdgeMode { get; set; } = false;

    // Servers the user explicitly deleted from the list. The phone's Rust+ app keeps
    // sending FCM "pairing" keepalives for a server as long as it's paired in-game,
    // regardless of whether it's still in this app's list — without this, every such
    // keepalive silently re-adds the deleted server on next launch.
    public List<string> DismissedPairingSignatures { get; set; } = new();
}


public class PlayerSession
{
    public DateTime ConnectTime { get; set; }
    public DateTime? DisconnectTime { get; set; }
}

public class OnlinePlayerBM
{
    public string Name { get; set; } = string.Empty;
    public string BMId { get; set; } = string.Empty;
    public DateTime SessionStartTimeUtc { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsTracked { get; set; }
    public string PlayTimeStr => $"{(int)Duration.TotalHours:D2}:{Duration.Minutes:D2}";
}

/// <summary>
/// Um jogador do "roster" do servidor — qualquer um já visto pelo bot BattleMetrics
/// dedicado, esteja online ou não agora. Fonte: tabela bm_seen_players (gravada só
/// pelo bot, nunca pela app). Usado só para a pesquisa de jogadores offline; quem já
/// está online continua a aparecer via <see cref="OnlinePlayerBM"/>/LastOnlinePlayers.
/// </summary>
public class RosterPlayer
{
    public string Name { get; set; } = string.Empty;
    public string BMId { get; set; } = string.Empty;
    public DateTime LastSeenAtUtc { get; set; }
    public bool IsOnline { get; set; }
    public bool IsTracked { get; set; }
    public string LastSeenStr => IsOnline ? "Online now" : FormatRelativeTime(LastSeenAtUtc);

    private static string FormatRelativeTime(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 60) return $"{Math.Max(1, (int)span.TotalMinutes)}m ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
        return $"{(int)span.TotalDays}d ago";
    }
}

public static class TrackingService
{
    private static readonly HttpClient _http = new();
    private static readonly string _dbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "RustPlusDesk", "tracked_players.json");
    private static readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "tracking_settings.json");

    private static readonly string _fcmConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RustPlusDesk", "rustplusjs-config.json");

    public static bool IsFcmConfigured()
        => File.Exists(_fcmConfigPath) && new FileInfo(_fcmConfigPath).Length > 50;

    /// <summary>
    /// Reads steam_id, issue_date, expiry_date from rustplusjs-config.json and seeds
    /// the in-memory TrackingSettings if those values are missing.  Call this on startup
    /// and after every pairing event.
    /// </summary>
    public static void ReadFcmConfig()
    {
        try
        {
            if (!File.Exists(_fcmConfigPath)) return;
            var json = File.ReadAllText(_fcmConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("steam_id", out var sid) && sid.ValueKind == JsonValueKind.String)
            {
                var s = sid.GetString() ?? "";
                if (!string.IsNullOrEmpty(s) && string.IsNullOrEmpty(_settings.SteamId64))
                    _settings.SteamId64 = s;
            }

            if (root.TryGetProperty("issue_date", out var iss) && iss.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(iss.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    if (_settings.FcmIssuedAt == null)
                        _settings.FcmIssuedAt = dt.ToLocalTime();
                }
            }

            if (root.TryGetProperty("expiry_date", out var exp) && exp.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(exp.GetString(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                {
                    if (_settings.FcmExpiresAt == null)
                        _settings.FcmExpiresAt = dt.ToLocalTime();
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Patches only the steam_id field in rustplusjs-config.json without
    /// touching the rest of the file.  Safe to call after pairing.
    /// </summary>
    public static void PatchFcmConfigSteamId(string steamId)
    {
        try
        {
            if (!File.Exists(_fcmConfigPath) || string.IsNullOrEmpty(steamId)) return;
            var json = File.ReadAllText(_fcmConfigPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            using var ms  = new System.IO.MemoryStream();
            using var wtr = new System.Text.Json.Utf8JsonWriter(ms,
                new JsonWriterOptions { Indented = true });
            wtr.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Name == "steam_id") continue; // skip old value
                prop.WriteTo(wtr);
            }
            wtr.WriteString("steam_id", steamId);
            wtr.WriteEndObject();
            wtr.Flush();
            File.WriteAllBytes(_fcmConfigPath, ms.ToArray());
        }
        catch { }
    }
    
    private static readonly object _dbLock = new();
    private static Dictionary<string, TrackedPlayer> _trackedPlayers = new();
    private static TrackingSettings _settings = new();
    public static TrackingSettings Settings => _settings;
    private static Timer? _trackingTimer;
    private static string? _lastServerHost;
    private static int _lastServerPort;
    private static string? _lastServerName;

    public static event Action? OnOnlinePlayersUpdated;
    public static event Action<string>? OnServerInfoUpdated;
    public static event Action<string, string>? OnTrackingNotification;
    public static string StatusMessage { get; private set; } = "";
    public static List<OnlinePlayerBM> LastOnlinePlayers { get; private set; } = new();
    public static DateTime? LastPullTime { get; private set; }
    public static bool IsTracking => _trackingTimer != null;

    static TrackingService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        LoadDB();
    }

    private static void LoadDB()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                var json = File.ReadAllText(_dbPath);
                var list = JsonSerializer.Deserialize<List<TrackedPlayer>>(json);
                if (list != null) _trackedPlayers = list.ToDictionary(p => p.BMId);
            }
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<TrackingSettings>(json) ?? new();
            }
        }
        catch { }
    }

    public static void SaveDB()
    {
        try
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string jsonP;
            lock (_dbLock)
            {
                var cutoff = DateTime.UtcNow.AddDays(-84); // 12 weeks
                foreach (var p in _trackedPlayers.Values)
                {
                    p.Sessions.RemoveAll(s => s.ConnectTime < cutoff);
                }
                jsonP = JsonSerializer.Serialize(_trackedPlayers.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            }
            File.WriteAllText(_dbPath, jsonP);

            var jsonS = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, jsonS);
        }
        catch { }
    }

    // Settings-only writes should not pay the cost of re-serializing and pruning the
    // entire tracked-players database (which SaveDB() does on every call). Use this for
    // property setters that only touch _settings.
    private static void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string json;
            lock (_dbLock)
            {
                json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            }
            File.WriteAllText(_settingsPath, json);
        }
        catch { }
    }

    private static void Log(string message)
    {
        try
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RustPlusDesk", "tracking_log.txt");
            var dir = Path.GetDirectoryName(logPath);
            if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    public static void TrackPlayer(string bmId, string name, string serverName, PlayerSession? initialSession = null, bool isBMOnly = false)
    {
        lock (_dbLock)
        {
            if (!_trackedPlayers.TryGetValue(bmId, out var p))
            {
                p = new TrackedPlayer { BMId = bmId, Name = name, LastServerName = serverName, IsBMOnly = isBMOnly };
                _trackedPlayers[bmId] = p;

                // Add initial session if provided and not already present
                if (initialSession != null)
                {
                    p.Sessions.Add(initialSession);
                }
            }
            else
            {
                p.LastServerName = serverName;
                if (name != "Unknown Player") p.Name = name;
                if (isBMOnly) p.IsBMOnly = true;

                if (initialSession != null && !p.Sessions.Any(s => s.ConnectTime == initialSession.ConnectTime))
                {
                    p.Sessions.Add(initialSession);
                    p.Sessions = p.Sessions.OrderBy(s => s.ConnectTime).ToList();
                }
            }
        }

        SaveDB();

        // Auto-start tracking if we have a server but no timer yet
        if (_trackingTimer == null && !string.IsNullOrEmpty(_settings.LastHost))
        {
            StartPolling(_settings.LastHost, _settings.LastPort, _settings.LastServerName);
        }
        OnOnlinePlayersUpdated?.Invoke();

        // O bot BattleMetrics dedicado (Node/Railway) só consegue vigiar este jogador
        // se souber que ele está na watchlist partilhada — sem isto, o tracking pára
        // assim que a app fechar.
        _ = SyncTrackedPlayerToSupabaseAsync(bmId, name, serverName, isBMOnly);

        // O bot já grava sessões deste jogador desde muito antes de ser adicionado à
        // watchlist (agora que grava para todo o roster do servidor, não só quem está
        // vigiado) — traz esse histórico já gravado de imediato, em vez de só na
        // próxima vez que se abrir o relatório de análise.
        _ = RefreshPlayerSessionsFromCloudAsync(bmId);
    }

    public static void UntrackPlayer(string bmId)
    {
        bool removed = false;
        lock (_dbLock)
        {
            removed = _trackedPlayers.Remove(bmId);
        }

        if (removed)
        {
            SaveDB();
            if (GetTrackedPlayers().Count == 0)
            {
                StopPolling();
            }
            OnOnlinePlayersUpdated?.Invoke();
            _ = RemoveTrackedPlayerFromSupabaseAsync(bmId);
        }
    }

    // ── Sincronização com o bot BattleMetrics dedicado (Supabase) ──────────
    // Best-effort: se o Supabase não estiver disponível ou o utilizador não
    // tiver guild_id associado (sem bot Discord configurado), o tracking local
    // continua a funcionar normalmente — isto é só um espelho para a nuvem.
    private static async Task<string?> ResolveGuildIdAsync()
    {
        try
        {
            if (SupabaseAuthManager.Client?.Auth == null) return null;
            var steamId = SteamId64;
            if (string.IsNullOrWhiteSpace(steamId)) return null;

            var res = await SupabaseAuthManager.Client
                .From<DiscordBotSettingsModel>()
                .Where(x => x.OwnerSteamId == steamId)
                .Get();

            return res.Models?.FirstOrDefault()?.GuildId;
        }
        catch (Exception ex)
        {
            Log($"[Team/Supabase] ResolveGuildIdAsync falhou: {ex.Message}");
            return null;
        }
    }

    private static async Task SyncTrackedPlayerToSupabaseAsync(string bmId, string name, string serverName, bool isBMOnly)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return;

            var serverKey = !string.IsNullOrEmpty(_lastServerHost) ? $"{_lastServerHost}:{_lastServerPort}" : serverName;

            await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Upsert(new BmTrackedPlayerModel
                {
                    GuildId = guildId,
                    BmId = bmId,
                    Name = name,
                    ServerKey = serverKey,
                    IsBmOnly = isBMOnly,
                    CreatedAt = DateTime.UtcNow,
                }, new Postgrest.QueryOptions { OnConflict = "guild_id,bm_id" });
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao sincronizar jogador tracked: {ex.Message}");
        }
    }


    private static bool _cloudRosterSyncedThisSession = false;

    /// <summary>
    /// Traz para o cache local jogadores que um colega de equipa adicionou à watchlist
    /// a partir do PC dele — sem isto, cada instalação só veria o que foi adicionado
    /// localmente. Chamado uma vez por sessão, best effort.
    /// </summary>
    public static async Task SyncTrackedPlayersFromCloudAsync()
    {
        if (_cloudRosterSyncedThisSession) return;

        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return;

            var res = await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Where(x => x.GuildId == guildId)
                .Get();

            var cloudPlayers = res.Models ?? new List<BmTrackedPlayerModel>();
            if (cloudPlayers.Count == 0) { _cloudRosterSyncedThisSession = true; return; }

            bool changed = false;
            lock (_dbLock)
            {
                foreach (var cp in cloudPlayers)
                {
                    if (_trackedPlayers.ContainsKey(cp.BmId)) continue;
                    _trackedPlayers[cp.BmId] = new TrackedPlayer
                    {
                        BMId = cp.BmId,
                        Name = cp.Name ?? "Unknown Player",
                        LastServerName = cp.ServerKey,
                        GroupName = cp.GroupName ?? "",
                        GroupColor = cp.GroupColor ?? "",
                        IsBMOnly = cp.IsBmOnly,
                    };
                    changed = true;
                }
            }

            _cloudRosterSyncedThisSession = true;
            if (changed)
            {
                SaveDB();
                OnOnlinePlayersUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao sincronizar watchlist da equipa: {ex.Message}");
        }
    }

    private static async Task PublishTrackedServerToSupabaseAsync(string battlemetricsServerId)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(_lastServerHost)) return;

            await SupabaseAuthManager.Client
                .From<BmTrackedServerModel>()
                .Upsert(new BmTrackedServerModel
                {
                    GuildId = guildId,
                    ServerKey = $"{_lastServerHost}:{_lastServerPort}",
                    Host = _lastServerHost,
                    Port = _lastServerPort,
                    ServerName = _lastServerName,
                    BattlemetricsServerId = battlemetricsServerId,
                    UpdatedAt = DateTime.UtcNow,
                }, new Postgrest.QueryOptions { OnConflict = "guild_id,server_key" });
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao publicar servidor tracked: {ex.Message}");
        }
    }

    private static async Task RemoveTrackedPlayerFromSupabaseAsync(string bmId)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return;

            await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Where(x => x.GuildId == guildId && x.BmId == bmId)
                .Delete();
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao remover jogador tracked: {ex.Message}");
        }
    }

    /// <summary>
    /// Roster pesquisável de todos os jogadores já vistos neste servidor pelo bot
    /// BattleMetrics dedicado (bm_seen_players), watchlist ou não — usado pela barra de
    /// pesquisa do separador "Roster" para encontrar jogadores offline. Best effort:
    /// devolve lista vazia se o Supabase não estiver disponível ou sem guild_id.
    /// </summary>
    public static async Task<List<RosterPlayer>> GetServerRosterAsync(string serverKey)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return new List<RosterPlayer>();

            var res = await SupabaseAuthManager.Client
                .From<BmSeenPlayerModel>()
                .Where(x => x.GuildId == guildId && x.ServerKey == serverKey)
                .Order(x => x.LastSeenAt, Postgrest.Constants.Ordering.Descending)
                .Limit(500)
                .Get();

            var onlineIds = LastOnlinePlayers.Select(p => p.BMId).ToHashSet();
            bool isTracked;

            return (res.Models ?? new List<BmSeenPlayerModel>())
                .Select(s =>
                {
                    lock (_dbLock) isTracked = _trackedPlayers.ContainsKey(s.BmId);
                    return new RosterPlayer
                    {
                        BMId = s.BmId,
                        Name = string.IsNullOrWhiteSpace(s.Name) ? s.BmId : s.Name!,
                        LastSeenAtUtc = s.LastSeenAt,
                        IsOnline = onlineIds.Contains(s.BmId),
                        IsTracked = isTracked,
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao obter roster do servidor: {ex.Message}");
            return new List<RosterPlayer>();
        }
    }

    /// <summary>
    /// Histórico de sessões gravado pelo bot BattleMetrics dedicado — inclui o tempo
    /// em que a app esteve fechada, ao contrário de <see cref="TrackedPlayer.Sessions"/> local.
    /// </summary>
    public static async Task<List<PlayerSession>> GetPlayerSessionsFromSupabaseAsync(string bmId)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return new List<PlayerSession>();

            var res = await SupabaseAuthManager.Client
                .From<BmPlayerSessionModel>()
                .Where(x => x.GuildId == guildId && x.BmId == bmId)
                .Order(x => x.ConnectTime, Postgrest.Constants.Ordering.Descending)
                .Limit(200)
                .Get();

            return (res.Models ?? new List<BmPlayerSessionModel>())
                .Select(s => new PlayerSession { ConnectTime = s.ConnectTime, DisconnectTime = s.DisconnectTime })
                .OrderBy(s => s.ConnectTime)
                .ToList();
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao obter histórico de sessões: {ex.Message}");
            return new List<PlayerSession>();
        }
    }

    /// <summary>
    /// Junta ao cache local as sessões que o bot BattleMetrics gravou enquanto a app
    /// esteve fechada, para que o relatório de análise (<see cref="GetAnalysisReport"/>)
    /// fique completo mesmo que ninguém tenha tido a app aberta durante esse período.
    /// </summary>
    public static async Task RefreshPlayerSessionsFromCloudAsync(string bmId)
    {
        var cloudSessions = await GetPlayerSessionsFromSupabaseAsync(bmId);
        if (cloudSessions.Count == 0) return;

        lock (_dbLock)
        {
            if (!_trackedPlayers.TryGetValue(bmId, out var p)) return;

            foreach (var cs in cloudSessions)
            {
                bool exists = p.Sessions.Any(s => s.ConnectTime == cs.ConnectTime);
                if (!exists)
                {
                    p.Sessions.Add(cs);
                }
                else
                {
                    // A sessão da cloud pode ter fechado entretanto (disconnect) sem que
                    // este PC tenha estado aberto para ver isso acontecer — actualiza.
                    var local = p.Sessions.First(s => s.ConnectTime == cs.ConnectTime);
                    if (!local.DisconnectTime.HasValue && cs.DisconnectTime.HasValue)
                        local.DisconnectTime = cs.DisconnectTime;
                }
            }

            // Auto-cura: sessões locais nunca fechadas (DisconnectTime null) que a
            // cloud não reconhece como a sessão aberta actual são resíduo do antigo
            // tracking local (removido, mas cujos dados já gravados nunca foram
            // limpos) — sem isto, o merge de intervalos trata "nunca fechou" como
            // "chega até agora", esticando o intervalo indefinidamente e escondendo
            // gaps reais de offline atrás de barras do heatmap sempre preenchidas.
            // A cloud é a fonte de verdade agora, por isso qualquer sessão aberta
            // localmente que não bata certo com a sessão aberta da cloud é lixo.
            var cloudOpenConnectTime = cloudSessions
                .Where(cs => !cs.DisconnectTime.HasValue)
                .Select(cs => (DateTime?)cs.ConnectTime)
                .FirstOrDefault();

            p.Sessions.RemoveAll(s => !s.DisconnectTime.HasValue && s.ConnectTime != cloudOpenConnectTime);

            p.Sessions = p.Sessions.OrderBy(s => s.ConnectTime).ToList();
        }

        SaveDB();
    }

    private static bool _isSyncingTrackedSessions = false;

    /// <summary>
    /// Substitui a antiga deteção local de connect/disconnect (que fazia o seu próprio
    /// polling à BattleMetrics em paralelo com o bot dedicado, gravando a mesma sessão
    /// duas vezes). Em vez disso, lê o estado que o bot já gravou no Supabase e só o
    /// reflete localmente — incluindo o alerta de equipa/Discord quando alguém liga ou
    /// desliga — sem nunca voltar a decidir isso por conta própria.
    /// </summary>
    public static async Task SyncTrackedSessionsFromCloudAsync()
    {
        if (_isSyncingTrackedSessions) return;
        _isSyncingTrackedSessions = true;
        try
        {
            var serverName = _lastServerName;
            if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(_lastServerHost)) return;

            var players = GetTrackedPlayers().Where(p => p.LastServerName == serverName && !p.IsBMOnly).ToList();
            if (players.Count == 0) return;

            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return;

            var serverKey = $"{_lastServerHost}:{_lastServerPort}";

            List<BmPlayerSessionModel> openSessions;
            try
            {
                var res = await SupabaseAuthManager.Client
                    .From<BmPlayerSessionModel>()
                    .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                    .Filter("server_key", Postgrest.Constants.Operator.Equals, serverKey)
                    .Filter("disconnect_time", Postgrest.Constants.Operator.Is, "null")
                    .Get();
                openSessions = res.Models ?? new List<BmPlayerSessionModel>();
            }
            catch (Exception ex)
            {
                Log($"[BM/Supabase] Erro ao ler sessões abertas: {ex.Message}");
                return;
            }

            var openBmIds = openSessions.Select(s => s.BmId).ToHashSet();

            foreach (var cloneTp in players)
            {
                TrackedPlayer tp;
                lock (_dbLock)
                {
                    if (!_trackedPlayers.TryGetValue(cloneTp.BMId, out tp)) continue;
                }

                bool wasOnline = tp.Sessions.LastOrDefault()?.DisconnectTime.HasValue == false;
                bool isOnlineNow = openBmIds.Contains(tp.BMId);
                if (wasOnline == isOnlineNow) continue;

                // Traz o histórico autoritativo do bot (nova sessão aberta, ou a hora
                // real de desconexão) antes de anunciar a transição.
                await RefreshPlayerSessionsFromCloudAsync(tp.BMId);

                if (!AnnounceTracking) continue;
                var groupStr = string.IsNullOrWhiteSpace(tp.GroupName) ? "" : $" [{tp.GroupName}]";
                var alertKey = isOnlineNow ? "AlertTrackingOnline" : "AlertTrackingOffline";
                OnTrackingNotification?.Invoke(AlertTemplateService.GetFormattedAlert(alertKey, tp.Name, groupStr), serverName);
            }
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao sincronizar tracked sessions: {ex.Message}");
        }
        finally
        {
            _isSyncingTrackedSessions = false;
        }
    }
    
    /// <summary>
    /// Detects a tracked player changing their in-game display name. The bot only ever
    /// sees BM IDs that are already on the watchlist, so it can't notice a rename by
    /// itself — this needs the full "everyone currently on the server" snapshot that
    /// PollOnceAsync already fetches for the online-players list, correlated by session
    /// start time. When found, migrates the watchlist entry (local + Supabase) to the
    /// new identity so the bot starts tracking it under the new BM ID.
    /// </summary>
    private static async Task DetectPossibleNameChangesAsync()
    {
        var serverName = _lastServerName;
        if (string.IsNullOrEmpty(serverName)) return;

        var onlineSnapshot = LastOnlinePlayers.ToList();
        if (onlineSnapshot.Count == 0) return;

        var players = GetTrackedPlayers().Where(p => p.LastServerName == serverName && !p.IsBMOnly).ToList();
        if (players.Count == 0) return;

        var now = DateTime.UtcNow;
        var onlineBmIds = onlineSnapshot.Select(o => o.BMId).ToHashSet();

        foreach (var cloneTp in players)
        {
            TrackedPlayer tp;
            lock (_dbLock)
            {
                if (!_trackedPlayers.TryGetValue(cloneTp.BMId, out tp)) continue;
            }

            if (onlineBmIds.Contains(tp.BMId)) continue; // still online under the same id

            var lastSession = tp.Sessions.LastOrDefault();
            if (lastSession == null || lastSession.DisconnectTime.HasValue) continue; // wasn't marked online

            var match = onlineSnapshot.FirstOrDefault(o =>
                !players.Any(p => p.BMId == o.BMId) &&
                Math.Abs((o.SessionStartTimeUtc - lastSession.ConnectTime).TotalSeconds) <= 1 &&
                (now - lastSession.ConnectTime).TotalSeconds > 60);

            if (match == null) continue;

            string oldName = tp.Name;
            string newName = match.Name;
            string oldBmId = tp.BMId;

            Log($"[NAME_CHANGE] {oldName} -> {newName} (session start matched: {lastSession.ConnectTime:HH:mm:ss} vs {match.SessionStartTimeUtc:HH:mm:ss})");

            if (oldBmId.Length == 17 && oldBmId.StartsWith("7656"))
            {
                // SteamID-tracked players keep the same id — just the display name changed.
                RenameTrackedPlayer(oldBmId, newName);
            }
            else
            {
                MigrateTrackedPlayer(oldBmId, match.BMId, newName);
            }

            if (AnnounceTracking)
            {
                var groupStr = string.IsNullOrWhiteSpace(tp.GroupName) ? "" : $" [{tp.GroupName}]";
                OnTrackingNotification?.Invoke(AlertTemplateService.GetFormattedAlert("AlertTrackingRenamed", oldName, groupStr, newName), serverName);
            }
        }

        await Task.CompletedTask;
    }

    public static string? CurrentServerBMId => _foundServerId;

    /// <summary>
    /// BattleMetrics now requires an API key (paid account) for the endpoints used here —
    /// unauthenticated requests started returning 403 on 2026-07-20. Set via the app once
    /// the user has a key; requests fall back to unauthenticated (and will keep 403ing)
    /// if left empty.
    /// </summary>
    public static string BattleMetricsApiKey
    {
        get => _settings.BattleMetricsApiKey;
        set { _settings.BattleMetricsApiKey = value?.Trim() ?? ""; SaveSettings(); }
    }

    private static async Task<HttpResponseMessage> BmGetAsync(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(_settings.BattleMetricsApiKey))
        {
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.BattleMetricsApiKey);
        }
        return await _http.SendAsync(req);
    }

    /// <summary>
    /// Overrides the auto-discovered BattleMetrics server ID for the currently connected
    /// server. Needed when the automatic IP/name lookup fails to match — e.g. right after
    /// the server's IP changes and BattleMetrics hasn't re-indexed it under the new address
    /// yet, or the in-game name doesn't match exactly. Immediately republishes the mapping
    /// so the dedicated tracker bot (which re-reads bm_tracked_servers every poll cycle)
    /// picks it up on its next tick.
    /// </summary>
    public static async Task SetServerBMIdManuallyAsync(string bmId)
    {
        bmId = bmId?.Trim() ?? "";
        if (string.IsNullOrEmpty(bmId)) return;

        _foundServerId = bmId;
        Log($"[BM] Server ID set manually: {bmId}");
        await PublishTrackedServerToSupabaseAsync(bmId);
        await PollOnceAsync();
    }

    public static void RenameTrackedPlayer(string bmId, string newName)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var player))
            {
                player.Name = newName;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
        _ = RenameTrackedPlayerInSupabaseAsync(bmId, newName);
    }

    public static void MigrateTrackedPlayer(string oldBmId, string newBmId, string newName)
    {
        string? serverKey = null;
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(oldBmId, out var player))
            {
                _trackedPlayers.Remove(oldBmId);
                player.BMId = newBmId;
                player.Name = newName;
                _trackedPlayers[newBmId] = player;
                serverKey = !string.IsNullOrEmpty(_lastServerHost) ? $"{_lastServerHost}:{_lastServerPort}" : player.LastServerName;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
        _ = MigrateTrackedPlayerInSupabaseAsync(oldBmId, newBmId, newName, serverKey);
    }

    private static async Task RenameTrackedPlayerInSupabaseAsync(string bmId, string newName)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId)) return;

            await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                .Filter("bm_id", Postgrest.Constants.Operator.Equals, bmId)
                .Set(x => x.Name, newName)
                .Update();
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao renomear jogador tracked: {ex.Message}");
        }
    }

    private static async Task MigrateTrackedPlayerInSupabaseAsync(string oldBmId, string newBmId, string newName, string? serverKey)
    {
        try
        {
            var guildId = await ResolveGuildIdAsync();
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(serverKey)) return;

            // O bot só vigia quem está em bm_tracked_players — sem isto, continuaria a
            // vigiar o BM ID antigo (já não usado por este jogador) e nunca começaria a
            // vigiar o novo. Também migra o histórico de sessões para o novo ID, para não
            // perder a continuidade do tracking deste jogador.
            await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                .Filter("bm_id", Postgrest.Constants.Operator.Equals, oldBmId)
                .Delete();

            await SupabaseAuthManager.Client
                .From<BmTrackedPlayerModel>()
                .Upsert(new BmTrackedPlayerModel
                {
                    GuildId = guildId,
                    BmId = newBmId,
                    Name = newName,
                    ServerKey = serverKey,
                    CreatedAt = DateTime.UtcNow,
                }, new Postgrest.QueryOptions { OnConflict = "guild_id,bm_id" });

            await SupabaseAuthManager.Client
                .From<BmPlayerSessionModel>()
                .Filter("guild_id", Postgrest.Constants.Operator.Equals, guildId)
                .Filter("bm_id", Postgrest.Constants.Operator.Equals, oldBmId)
                .Set(x => x.BmId, newBmId)
                .Update();
        }
        catch (Exception ex)
        {
            Log($"[BM/Supabase] Erro ao migrar jogador tracked: {ex.Message}");
        }
    }
    public static void SetPlayerGroup(string bmId, string groupName, string groupColor)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var player))
            {
                player.GroupName = groupName;
                player.GroupColor = groupColor;
            }
            else return;
        }
        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }
    public static List<TrackedPlayer> GetTrackedPlayers() 
    {
        lock (_dbLock)
        {
            return _trackedPlayers.Values.Select(p => p.CloneWithSnapshots()).ToList();
        }
    }
    public static bool IsTracked(string bmId)
    {
        lock (_dbLock)
        {
            return _trackedPlayers.ContainsKey(bmId);
        }
    }

    public static bool GetGroupState(string serverName, string groupName)
    {
        var key = $"{serverName}|{groupName}";
        if (_settings.GroupStates.TryGetValue(key, out var expanded)) return expanded;
        return true; // Default to expanded
    }

    public static void SetGroupState(string serverName, string groupName, bool expanded)
    {
        var key = $"{serverName}|{groupName}";
        _settings.GroupStates[key] = expanded;
        SaveSettings();
    }

    public static List<string> GetGroupOrder(string serverName)
    {
        if (_settings.GroupOrder.TryGetValue(serverName, out var order)) return order;
        return new List<string>();
    }

    public static void SetGroupOrder(string serverName, List<string> order)
    {
        _settings.GroupOrder[serverName] = order;
        SaveSettings();
    }

    public static bool IsBackgroundTrackingEnabled
    {
        get => _settings.BackgroundTrackingEnabled;
        set { _settings.BackgroundTrackingEnabled = value; SaveSettings(); }
    }

    public static bool CloseToTrayEnabled
    {
        get => _settings.CloseToTrayEnabled;
        set { _settings.CloseToTrayEnabled = value; SaveSettings(); }
    }

    public static bool StartMinimizedEnabled
    {
        get => _settings.StartMinimizedEnabled;
        set { _settings.StartMinimizedEnabled = value; SaveSettings(); }
    }

    public static bool AutoConnectEnabled
    {
        get => _settings.AutoConnectEnabled;
        set { _settings.AutoConnectEnabled = value; SaveSettings(); }
    }

    public static bool AutoStartEnabled
    {
        get => _settings.AutoStartEnabled;
        set 
        { 
            if (_settings.AutoStartEnabled == value) return;
            _settings.AutoStartEnabled = value;
            SetAutoStart(value);
            SaveSettings();
        }
    }

    public static bool AutoLoadShops
    {
        get => _settings.AutoLoadShops;
        set { _settings.AutoLoadShops = value; SaveSettings(); }
    }

    public static bool HideConsole
    {
        get => _settings.HideConsole;
        set { _settings.HideConsole = value; SaveSettings(); }
    }

    public static double SidebarWidth
    {
        get => _settings.SidebarWidth;
        set { _settings.SidebarWidth = value; SaveSettings(); }
    }
    public static bool SidebarPinned
    {
        get => _settings.SidebarPinned;
        set { _settings.SidebarPinned = value; SaveSettings(); }
    }

    public static double WindowWidth => _settings.WindowWidth;
    public static double WindowHeight => _settings.WindowHeight;
    public static double? WindowLeft => _settings.WindowLeft;
    public static double? WindowTop => _settings.WindowTop;
    public static bool WindowMaximized => _settings.WindowMaximized;

    public static void SaveWindowBounds(double width, double height, double left, double top, bool maximized)
    {
        _settings.WindowWidth = width;
        _settings.WindowHeight = height;
        _settings.WindowLeft = left;
        _settings.WindowTop = top;
        _settings.WindowMaximized = maximized;
        SaveSettings();
    }

    public static string SteamId64
    {
        get => _settings.SteamId64;
        set { _settings.SteamId64 = value; SaveSettings(); }
    }

    public static DateTime? FcmIssuedAt
    {
        get => _settings.FcmIssuedAt;
        set { _settings.FcmIssuedAt = value; SaveSettings(); }
    }

    public static DateTime? FcmExpiresAt
    {
        get => _settings.FcmExpiresAt;
        set { _settings.FcmExpiresAt = value; SaveSettings(); }
    }

    public static bool AnnounceCargo
    {
        get => _settings.AnnounceCargo;
        set { _settings.AnnounceCargo = value; SaveSettings(); }
    }
    public static bool ListenForServerEvents
    {
        get => _settings.ListenForServerEvents;
        set { _settings.ListenForServerEvents = value; SaveDB(); }
    }
    public static bool TrustOwnDetections
    {
        get => _settings.TrustOwnDetections;
        set { _settings.TrustOwnDetections = value; SaveDB(); }
    }
    public static bool AnnounceHeli
    {
        get => _settings.AnnounceHeli;
        set { _settings.AnnounceHeli = value; SaveSettings(); }
    }
    public static bool AnnounceChinook
    {
        get => _settings.AnnounceChinook;
        set { _settings.AnnounceChinook = value; SaveSettings(); }
    }
    public static bool AnnounceVendor
    {
        get => _settings.AnnounceVendor;
        set { _settings.AnnounceVendor = value; SaveSettings(); }
    }
    public static bool AnnounceOilRig
    {
        get => _settings.AnnounceOilRig;
        set { _settings.AnnounceOilRig = value; SaveSettings(); }
    }
    public static bool AnnounceDeepSea
    {
        get => _settings.AnnounceDeepSea;
        set { _settings.AnnounceDeepSea = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerOnline
    {
        get => _settings.AnnouncePlayerOnline;
        set { _settings.AnnouncePlayerOnline = value; SaveSettings(); }
    }
    public static bool AnnounceTracking
    {
        get => _settings.AnnounceTracking;
        set { _settings.AnnounceTracking = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerOffline
    {
        get => _settings.AnnouncePlayerOffline;
        set { _settings.AnnouncePlayerOffline = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerAfk
    {
        get => _settings.AnnouncePlayerAfk;
        set { _settings.AnnouncePlayerAfk = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerAfkReturn
    {
        get => _settings.AnnouncePlayerAfkReturn;
        set { _settings.AnnouncePlayerAfkReturn = value; SaveSettings(); }
    }
    public static int AfkAlertMinutes
    {
        get => _settings.AfkAlertMinutes;
        set { _settings.AfkAlertMinutes = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerDeathSelf
    {
        get => _settings.AnnouncePlayerDeathSelf;
        set { _settings.AnnouncePlayerDeathSelf = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerDeathTeam
    {
        get => _settings.AnnouncePlayerDeathTeam;
        set { _settings.AnnouncePlayerDeathTeam = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerRespawnSelf
    {
        get => _settings.AnnouncePlayerRespawnSelf;
        set { _settings.AnnouncePlayerRespawnSelf = value; SaveSettings(); }
    }
    public static bool AnnouncePlayerRespawnTeam
    {
        get => _settings.AnnouncePlayerRespawnTeam;
        set { _settings.AnnouncePlayerRespawnTeam = value; SaveSettings(); }
    }
    public static bool AnnounceNewShops
    {
        get => _settings.AnnounceNewShops;
        set { _settings.AnnounceNewShops = value; SaveSettings(); }
    }
    public static bool AnnounceSuspiciousShops
    {
        get => _settings.AnnounceSuspiciousShops;
        set { _settings.AnnounceSuspiciousShops = value; SaveSettings(); }
    }
    public static bool AnnounceTradeAlerts
    {
        get => _settings.AnnounceTradeAlerts;
        set { _settings.AnnounceTradeAlerts = value; SaveSettings(); }
    }

    public static string SelectedLanguage
    {
        get => _settings.SelectedLanguage;
        set { _settings.SelectedLanguage = value; SaveSettings(); }
    }

    public static bool AnnounceSpawnsMaster
    {
        get => _settings.AnnounceSpawnsMaster;
        set { _settings.AnnounceSpawnsMaster = value; SaveSettings(); }
    }

    public static bool ChatMasterOfferSoundEnabled
    {
        get => _settings.ChatMasterOfferSoundEnabled;
        set { _settings.ChatMasterOfferSoundEnabled = value; SaveSettings(); }
    }

    public static bool TranslationConsentGiven
    {
        get => _settings.TranslationConsentGiven;
        set { _settings.TranslationConsentGiven = value; SaveSettings(); }
    }

    public static bool UploadConsentGiven
    {
        get => _settings.UploadConsentGiven;
        set { _settings.UploadConsentGiven = value; SaveSettings(); }
    }

    public static bool CloudSyncEnabled
    {
        get => _settings.CloudSyncEnabled;
        set { _settings.CloudSyncEnabled = value; SaveSettings(); }
    }

    private static string HotkeyAlertKey(string serverKey, long entityId) => $"{serverKey}|{entityId}";

    public static bool GetHotkeyTriggerChatAlert(string serverKey, long entityId)
    {
        var key = HotkeyAlertKey(serverKey, entityId);
        return _settings.HotkeyTriggerChatAlertEnabled.TryGetValue(key, out var val) && val;
    }

    public static void SetHotkeyTriggerChatAlert(string serverKey, long entityId, bool enabled)
    {
        var key = HotkeyAlertKey(serverKey, entityId);
        _settings.HotkeyTriggerChatAlertEnabled[key] = enabled;
        SaveSettings();
    }

    public static IReadOnlyDictionary<string, bool> GetAllHotkeyTriggerChatAlerts()
        => _settings.HotkeyTriggerChatAlertEnabled;

    public static bool HotkeyTriggerChatAlertsEnabled
    {
        get => _settings.HotkeyTriggerChatAlertsEnabled;
        set { _settings.HotkeyTriggerChatAlertsEnabled = value; SaveSettings(); }
    }

    public static bool AnnounceCargoDocking
    {
        get => _settings.AnnounceCargoDocking;
        set { _settings.AnnounceCargoDocking = value; SaveSettings(); }
    }
    public static bool AnnounceCargoEgress
    {
        get => _settings.AnnounceCargoEgress;
        set { _settings.AnnounceCargoEgress = value; SaveSettings(); }
    }
    public static int GetLearnedDockingDuration(string host)
    {
        if (_settings.LearnedDockingDurations.TryGetValue(host, out var d)) return d;
        return 8; // Default 8 minutes (before server-specific value is learned)
    }
    public static void SetLearnedDockingDuration(string host, int minutes)
    {
        if (minutes < 1 || minutes > 60) return;
        _settings.LearnedDockingDurations[host] = minutes;
        SaveSettings();
    }
    public static bool AnnounceCargoArrival
    {
        get => _settings.AnnounceCargoArrival;
        set { _settings.AnnounceCargoArrival = value; _settings.AnnounceCargoArrivalUserSet = true; SaveSettings(); }
    }

    /// <summary>
    /// Auto-liga o aviso de chegada de cargo assim que a app aprende a rota de um harbor
    /// — mas só se o utilizador nunca tiver mexido nesta opção. Se já a desligou de
    /// propósito, respeita essa escolha e não volta a ligar sozinha.
    /// </summary>
    public static bool AutoEnableCargoArrivalIfEligible()
    {
        if (_settings.AnnounceCargoArrivalUserSet || _settings.AnnounceCargoArrival)
            return false;

        _settings.AnnounceCargoArrival = true;
        SaveSettings();
        return true;
    }
    public static bool AnnounceSmartAlerts
    {
        get => _settings.AnnounceSmartAlerts;
        set { _settings.AnnounceSmartAlerts = value; SaveSettings(); }
    }
    public static bool GenericAlarmPopupEnabled
    {
        get => _settings.GenericAlarmPopupEnabled;
        set { _settings.GenericAlarmPopupEnabled = value; SaveSettings(); }
    }
    public static bool GenericAlarmOverlayEnabled
    {
        get => _settings.GenericAlarmOverlayEnabled;
        set { _settings.GenericAlarmOverlayEnabled = value; SaveSettings(); }
    }
    public static bool GenericAlarmAudioEnabled
    {
        get => _settings.GenericAlarmAudioEnabled;
        set { _settings.GenericAlarmAudioEnabled = value; SaveSettings(); }
    }
    public static string GenericAlarmAudioFilePath
    {
        get => _settings.GenericAlarmAudioFilePath ?? string.Empty;
        set { _settings.GenericAlarmAudioFilePath = value; SaveSettings(); }
    }
    public static string LastServerName
    {
        get => _settings.LastServerName;
        set { _settings.LastServerName = value; SaveSettings(); }
    }

    public static bool MapShowSteamMarkers
    {
        get => _settings.MapShowSteamMarkers;
        set { _settings.MapShowSteamMarkers = value; SaveSettings(); }
    }
    public static bool MapShowPlayerArrows
    {
        get => _settings.MapShowPlayerArrows;
        set { _settings.MapShowPlayerArrows = value; SaveSettings(); }
    }
    public static bool MapShowDeathTags
    {
        get => _settings.MapShowDeathTags;
        set { _settings.MapShowDeathTags = value; SaveSettings(); }
    }
    public static bool MapShowDeathHeatmap
    {
        get => _settings.MapShowDeathHeatmap;
        set { _settings.MapShowDeathHeatmap = value; SaveDB(); }
    }
    public static int MaxSelfDeathMarkers
    {
        get => _settings.MaxSelfDeathMarkers;
        set { _settings.MaxSelfDeathMarkers = value; SaveSettings(); }
    }
    public static int MaxTeamDeathMarkers
    {
        get => _settings.MaxTeamDeathMarkers;
        set { _settings.MaxTeamDeathMarkers = value; SaveSettings(); }
    }
    public static bool MapAbbreviateNames
    {
        get => _settings.MapAbbreviateNames;
        set { _settings.MapAbbreviateNames = value; SaveSettings(); }
    }
    public static double MapPlayerIconScale
    {
        get => _settings.MapPlayerIconScale;
        set { _settings.MapPlayerIconScale = value; SaveSettings(); }
    }
    public static bool MapUseMonumentText
    {
        get => _settings.MapMonumentDisplayMode == 1;
        set { _settings.MapMonumentDisplayMode = value ? 1 : 0; SaveSettings(); }
    }
    public static int MapMonumentDisplayMode
    {
        get => _settings.MapMonumentDisplayMode;
        set { _settings.MapMonumentDisplayMode = value; SaveSettings(); }
    }
    public static double MapMonumentScale
    {
        get => _settings.MapMonumentScale;
        set { _settings.MapMonumentScale = value; SaveSettings(); }
    }
    public static double MapMonumentOpacity
    {
        get => _settings.MapMonumentOpacity;
        set { _settings.MapMonumentOpacity = value; SaveSettings(); }
    }
    public static double MapGridOpacity
    {
        get => _settings.MapGridOpacity;
        set { _settings.MapGridOpacity = value; SaveSettings(); }
    }
    public static int MapBitmapScalingMode
    {
        get => _settings.MapBitmapScalingMode;
        set { _settings.MapBitmapScalingMode = value; SaveSettings(); }
    }
    public static bool MapUseCacheMode
    {
        get => _settings.MapUseCacheMode;
        set { _settings.MapUseCacheMode = value; SaveSettings(); }
    }
    public static double MapRenderScale
    {
        get => _settings.MapRenderScale;
        set { _settings.MapRenderScale = value; SaveSettings(); }
    }
    public static bool MapUseAliasedEdgeMode
    {
        get => _settings.MapUseAliasedEdgeMode;
        set { _settings.MapUseAliasedEdgeMode = value; SaveSettings(); }
    }

    public static IReadOnlyList<string> HiddenExtraMonumentTypes
        => _settings.HiddenExtraMonumentTypes;

    public static bool IsExtraMonumentTypeHidden(string name)
        => _settings.HiddenExtraMonumentTypes.Contains(name, StringComparer.OrdinalIgnoreCase);

    public static void SetExtraMonumentTypeHidden(string name, bool hidden)
    {
        bool changed;
        if (hidden)
        {
            if (!_settings.HiddenExtraMonumentTypes.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _settings.HiddenExtraMonumentTypes.Add(name);
                changed = true;
            }
            else changed = false;
        }
        else
            changed = _settings.HiddenExtraMonumentTypes.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (changed) SaveSettings();
    }

    public static int GetLearnedCargoFullLife(string host)
    {
        if (_settings.LearnedCargoFullLifeMinutes.TryGetValue(host, out var d)) return d;
        return 0; 
    }
    public static void SetLearnedCargoFullLife(string host, int minutes)
    {
        if (minutes < 10 || minutes > 120) return;
        _settings.LearnedCargoFullLifeMinutes[host] = minutes;
        SaveSettings();
    }
    public static int GetLearnedCargoTravelTime(string host)
    {
        if (_settings.LearnedCargoTravelMinutes.TryGetValue(host, out var d)) return d;
        return 0;
    }
    public static void SetLearnedCargoTravelTime(string host, int minutes)
    {
        if (minutes < 1 || minutes > 30) return;
        _settings.LearnedCargoTravelMinutes[host] = minutes;
        SaveSettings();
    }

    public static List<HarborInfo> GetServerHarbors(string host)
    {
        if (_settings.ServerHarbors.TryGetValue(host, out var list)) return list;
        return new();
    }

    public static void SetServerHarbors(string host, List<HarborInfo> harbors)
    {
        _settings.ServerHarbors[host] = harbors;
        _settings.ServerCargoTriggers.Remove(host); // Wipe detected -> Clear triggers
        SaveSettings();
    }

    public static CargoTriggerPoint? GetCargoTriggerPoint(string host, string harborName)
    {
        if (_settings.ServerCargoTriggers.TryGetValue(host, out var dict))
        {
            if (dict.TryGetValue(harborName, out var p)) return p;
        }
        return null;
    }

    public static void SetCargoTriggerPoint(string host, string harborName, double x, double y)
    {
        if (!_settings.ServerCargoTriggers.ContainsKey(host))
            _settings.ServerCargoTriggers[host] = new();
        _settings.ServerCargoTriggers[host][harborName] = new CargoTriggerPoint { X = x, Y = y };
        SaveSettings();
    }

    public static bool HasAnyCargoTrigger(string host)
    {
        return _settings.ServerCargoTriggers.TryGetValue(host, out var dict) && dict.Count > 0;
    }

    public static bool SaveAlertSelection
    {
        get => _settings.SaveAlertSelection;
        set { _settings.SaveAlertSelection = value; SaveSettings(); }
    }

    public static string LastCrosshairStyle
    {
        get => _settings.LastCrosshairStyle ?? "GreenDot";
        set { _settings.LastCrosshairStyle = value; SaveSettings(); }
    }

    public static string LastCustomCrosshairId
    {
        get => _settings.LastCustomCrosshairId ?? string.Empty;
        set { _settings.LastCustomCrosshairId = value; SaveSettings(); }
    }

    private static void SetAutoStart(bool enabled)
    {
        try
        {
            const string runKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(runKey, true);
            if (key == null) return;

            string appName = "RustPlusDesk";
            if (enabled)
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
                key.SetValue(appName, $"\"{exePath}\" --background");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch { }
    }

    public static (string host, int port, string name) LastServer => (_settings.LastHost, _settings.LastPort, _settings.LastServerName);
    public static string? LastBMId => _settings.LastBMId;

    public static async Task<string> FetchPlayerNameAsync(string bmId)
    {
        if (bmId.Length == 17 && bmId.StartsWith("7656") && ulong.TryParse(bmId, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            try
            {
                var xml = await _http.GetStringAsync($"https://steamcommunity.com/profiles/{bmId}?xml=1");
                var m = System.Text.RegularExpressions.Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) return m.Groups[1].Value.Trim();
            }
            catch { }
        }
        return await Task.FromResult(bmId);
    }

    public static async Task<DateTime?> FetchPlayerLastSeenAsync(string bmId)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var tp) && tp.Sessions.Any())
            {
                var last = tp.Sessions.Last();
                if (last.DisconnectTime.HasValue) return last.DisconnectTime;
            }
        }
        return await Task.FromResult<DateTime?>(null);
    }

    public static void LoadDemoData()
    {
        lock (_dbLock)
        {
            _trackedPlayers.Clear();
            var now = DateTime.UtcNow;

            // 1. The Night Owl (Plays 00:00 - 06:00)
            var owl = new TrackedPlayer { BMId = "demo_1", Name = "NightOwl_X" };
            for (int d = 0; d < 14; d++) {
                var date = now.Date.AddDays(-d).AddHours(1); // 01:00
                owl.Sessions.Add(new PlayerSession { ConnectTime = date, DisconnectTime = date.AddHours(4) });
            }
            _trackedPlayers[owl.BMId] = owl;

            // 2. The Grinder (Huge playtime, active 12:00 - 02:00)
            var grinder = new TrackedPlayer { BMId = "demo_2", Name = "IndustrialPvP" };
            for (int d = 0; d < 7; d++) {
                var date = now.Date.AddDays(-d).AddHours(12); // Noon
                grinder.Sessions.Add(new PlayerSession { ConnectTime = date, DisconnectTime = date.AddHours(14) }); // Until 02:00
            }
            _trackedPlayers[grinder.BMId] = grinder;

            // 3. The Weekend Warrior (Only Sat/Sun)
            var weekend = new TrackedPlayer { BMId = "demo_3", Name = "CasualFriday" };
            for (int d = 0; d < 30; d++) {
                var date = now.Date.AddDays(-d);
                if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) {
                    weekend.Sessions.Add(new PlayerSession { ConnectTime = date.AddHours(10), DisconnectTime = date.AddHours(18) });
                }
            }
            _trackedPlayers[weekend.BMId] = weekend;
        }

        SaveDB();
        OnOnlinePlayersUpdated?.Invoke();
    }

    public static async Task<PlayerSession?> FetchPlayerLastSessionAsync(string bmId)
    {
        lock (_dbLock)
        {
            if (_trackedPlayers.TryGetValue(bmId, out var tp) && tp.Sessions.Any())
            {
                return tp.Sessions.Last();
            }
        }
        return await Task.FromResult<PlayerSession?>(null);
    }

    /// <summary>One pre-computed Play Schedule window/offset, embedded as JSON for the
    /// report's client-side tab/navigation JS (see GetAnalysisReport).</summary>
    private sealed class ScheduleWindowDto
    {
        public string Label { get; set; } = "";
        public string[] DayLabels { get; set; } = Array.Empty<string>();
        public int[][] Lv { get; set; } = Array.Empty<int[]>();
        public double[][] Avg { get; set; } = Array.Empty<double[]>();
        public int Sessions { get; set; }
        public int Weeks { get; set; }
        public bool HasPrev { get; set; }
    }


    public static string GetAnalysisReport(string? targetBmId = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset='utf-8'>");
        sb.AppendLine("<style>");
        // Root styles
        sb.AppendLine("body { background: #0d1117; color: #c9d1d9; font-family: -apple-system,BlinkMacSystemFont,'Segoe UI',Helvetica,Arial,sans-serif; margin: 30px; line-height: 1.5; }");
        sb.AppendLine(".player-card { background: #161b22; border: 1px solid #30363d; border-radius: 8px; padding: 24px; margin-bottom: 30px; box-shadow: 0 8px 24px rgba(0,0,0,0.2); }");
        sb.AppendLine("h1 { color: #f0f6fc; font-size: 28px; font-weight: 600; margin-bottom: 30px; letter-spacing: -0.5px; }");

        // Theme variables (to be overridden per card)
        sb.AppendLine(".theme-online { --theme-accent: #3fb950; --theme-accent-soft: rgba(63, 185, 80, 0.1); --theme-accent-border: rgba(63, 185, 80, 0.3); }");
        sb.AppendLine(".theme-offline { --theme-accent: #8b949e; --theme-accent-soft: rgba(139, 148, 158, 0.1); --theme-accent-border: rgba(139, 148, 158, 0.3); }");

        sb.AppendLine("h2 { color: var(--theme-accent); margin: 0 0 16px 0; font-size: 22px; border-bottom: 1px solid #21262d; padding-bottom: 8px; }");

        sb.AppendLine(".section-title { font-size: 13px; font-weight: 600; color: #8b949e; margin: 25px 0 10px 0; display: flex; align-items: center; }");
        sb.AppendLine(".section-title::after { content: ''; flex: 1; height: 1px; background: #21262d; margin-left: 10px; }");

        // Header / stat tiles / server row (RustPlayerTrack-style layout)
        sb.AppendLine(".ph-header { margin-bottom: 14px; }");
        sb.AppendLine(".ph-name-row { display: flex; align-items: center; gap: 10px; }");
        sb.AppendLine(".ph-name { font-size: 20px; font-weight: 700; color: #f0f6fc; }");
        sb.AppendLine(".ph-dot { width: 9px; height: 9px; border-radius: 50%; display: inline-block; flex-shrink: 0; }");
        sb.AppendLine(".ph-dot.online { background: #3fb950; box-shadow: 0 0 6px #3fb950; }");
        sb.AppendLine(".ph-dot.offline { background: #6e7681; }");
        sb.AppendLine(".ph-bmlink { margin-left: auto; font-size: 11px; color: #58a6ff; text-decoration: none; }");
        sb.AppendLine(".ph-substatus { font-size: 12px; color: #8b949e; margin-top: 4px; }");
        sb.AppendLine(".ph-pills { display: flex; gap: 8px; margin-top: 10px; flex-wrap: wrap; }");
        sb.AppendLine(".ph-pill { background: #0d1117; border: 1px solid #21262d; border-radius: 6px; padding: 4px 10px; font-size: 11px; color: #c9d1d9; }");
        sb.AppendLine(".ph-pill.accent { border-color: rgba(255,140,0,0.4); color: #ff9800; }");
        sb.AppendLine(".ph-subtitle { font-size: 10px; color: #6e7681; text-transform: uppercase; letter-spacing: 0.3px; margin: -4px 0 10px 0; }");

        sb.AppendLine(".tile-grid { display: grid; grid-template-columns: repeat(5, 1fr); gap: 10px; margin-bottom: 6px; }");
        sb.AppendLine(".tile { background: #0d1117; border: 1px solid #21262d; border-radius: 8px; padding: 12px 6px; text-align: center; }");
        sb.AppendLine(".tile-icon { font-size: 16px; margin-bottom: 6px; }");
        sb.AppendLine(".tile-value { font-size: 17px; font-weight: 700; color: #f0f6fc; }");
        sb.AppendLine(".tile-label { font-size: 9px; color: #8b949e; text-transform: uppercase; margin-top: 4px; letter-spacing: 0.3px; }");

        sb.AppendLine(".server-row { display: flex; justify-content: space-between; align-items: center; background: #0d1117; border: 1px solid #21262d; border-radius: 6px; padding: 10px 14px; font-size: 12px; margin-bottom: 6px; }");
        sb.AppendLine(".server-name { color: #f0f6fc; font-weight: 600; }");
        sb.AppendLine(".server-stats { color: #8b949e; }");

        // Play Schedule Heatmap (7 days × 24 hours) with window tabs + date navigation
        sb.AppendLine(".sched-toolbar { display: flex; justify-content: space-between; align-items: center; margin: 10px 0; flex-wrap: wrap; gap: 8px; }");
        sb.AppendLine(".sched-tabs { display: flex; gap: 4px; }");
        sb.AppendLine(".sched-tab { background: #0d1117; border: 1px solid #21262d; color: #8b949e; border-radius: 6px; padding: 4px 12px; font-size: 12px; cursor: pointer; font-family: inherit; }");
        sb.AppendLine(".sched-tab.active { background: rgba(255,140,0,0.15); border-color: #ff9800; color: #ff9800; }");
        sb.AppendLine(".sched-nav { display: flex; align-items: center; gap: 8px; font-size: 11px; color: #8b949e; }");
        sb.AppendLine(".sched-nav button { background: #0d1117; border: 1px solid #21262d; color: #c9d1d9; border-radius: 4px; width: 22px; height: 22px; cursor: pointer; }");
        sb.AppendLine(".sched-nav button:disabled { opacity: 0.3; cursor: default; }");

        sb.AppendLine(".schedule-heatmap { background: #0d1117; padding: 15px; border-radius: 6px; border: 1px solid #21262d; overflow-x: auto; }");
        sb.AppendLine(".schedule-hourlabels { display: flex; gap: 2px; margin-left: 34px; margin-bottom: 4px; }");
        sb.AppendLine(".schedule-hourlabel { width: 15px; font-size: 8px; color: #8b949e; text-align: center; font-family: monospace; flex-shrink: 0; }");
        sb.AppendLine(".schedule-row { display: flex; align-items: center; gap: 2px; margin-bottom: 2px; }");
        sb.AppendLine(".schedule-daylabel { width: 30px; font-size: 9px; color: #8b949e; font-family: monospace; flex-shrink: 0; line-height: 1.2; }");
        sb.AppendLine(".schedule-cell { width: 15px; height: 15px; border-radius: 2px; background: #161b22; flex-shrink: 0; }");
        sb.AppendLine(".schedule-cell.lv1 { background: #7c2d12; }");
        sb.AppendLine(".schedule-cell.lv2 { background: #9a3412; }");
        sb.AppendLine(".schedule-cell.lv3 { background: #c2410c; }");
        sb.AppendLine(".schedule-cell.lv4 { background: #ea580c; }");
        sb.AppendLine(".schedule-cell.lv5 { background: #f97316; }");

        sb.AppendLine(".sched-footer { display: flex; justify-content: space-between; align-items: center; margin-top: 8px; font-size: 11px; color: #8b949e; flex-wrap: wrap; gap: 6px; }");
        sb.AppendLine(".sched-legend { display: flex; align-items: center; gap: 3px; }");
        sb.AppendLine(".sched-legend .schedule-cell { width: 11px; height: 11px; }");

        sb.AppendLine(".insight-box { background: var(--theme-accent-soft); border: 1px solid var(--theme-accent-border); padding: 16px; margin-top: 20px; border-radius: 8px; }");
        sb.AppendLine(".insight-item { margin: 8px 0; font-size: 14px; display: flex; align-items: center; }");
        sb.AppendLine(".insight-icon { margin-right: 10px; font-size: 18px; }");
        sb.AppendLine(".warning { background: rgba(210, 153, 34, 0.1); border: 1px solid rgba(210, 153, 34, 0.2); color: #d29922; padding: 10px; border-radius: 6px; font-size: 12px; margin-top: 15px; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine(@"<script>
window.SCHED_DATA = window.SCHED_DATA || {};
window.schedState = window.schedState || {};
function schedEnsure(pid) {
    if (!window.schedState[pid]) window.schedState[pid] = { win: '7', off: 0 };
    return window.schedState[pid];
}
function schedSetWindow(pid, win) {
    var st = schedEnsure(pid);
    st.win = win; st.off = 0;
    var tabs = document.querySelectorAll('#sched-tabs-' + pid + ' .sched-tab');
    for (var i = 0; i < tabs.length; i++) tabs[i].classList.toggle('active', tabs[i].getAttribute('data-win') === win);
    schedRender(pid);
}
function schedNav(pid, dir) {
    var st = schedEnsure(pid);
    var arr = window.SCHED_DATA[pid][st.win];
    var newOff = st.off + dir;
    if (newOff < 0 || newOff >= arr.length) return;
    st.off = newOff;
    schedRender(pid);
}
function schedRender(pid) {
    var st = schedEnsure(pid);
    var arr = window.SCHED_DATA[pid][st.win];
    if (!arr || !arr.length) return;
    var d = arr[st.off];
    var rows = document.querySelectorAll('#sched-grid-' + pid + ' .schedule-row');
    for (var day = 0; day < 7; day++) {
        var row = rows[day];
        var dayLabelHtml = d.dayLabels[day].replace('\n', '<br>');
        row.querySelector('.schedule-daylabel').innerHTML = dayLabelHtml;
        var cells = row.querySelectorAll('.schedule-cell');
        for (var hour = 0; hour < 24; hour++) {
            var cell = cells[hour];
            var lv = d.lv[day][hour];
            cell.className = 'schedule-cell' + (lv > 0 ? (' lv' + lv) : '');
            var hh = (hour < 10 ? '0' : '') + hour;
            cell.title = d.dayLabels[day].replace('\n', ' ') + ' ' + hh + ':00 — avg ' + d.avg[day][hour] + ' min/week';
        }
    }
    var rangeEl = document.getElementById('sched-range-' + pid);
    if (rangeEl) rangeEl.textContent = d.label;
    var capEl = document.getElementById('sched-caption-' + pid);
    if (capEl) capEl.textContent = d.sessions + ' session(s) · avg per week over ' + d.weeks + ' week(s)';
    var prevBtn = document.getElementById('sched-prev-' + pid);
    var nextBtn = document.getElementById('sched-next-' + pid);
    if (prevBtn) prevBtn.disabled = !d.hasPrev;
    if (nextBtn) nextBtn.disabled = (st.off === 0);
}
</script>");

        sb.AppendLine("<h1>Activity Intelligence Report</h1>");
        
        List<TrackedPlayer> playersToReport;
        lock (_dbLock)
        {
            playersToReport = targetBmId == null 
                ? _trackedPlayers.Values.ToList() 
                : _trackedPlayers.Values.Where(p => p.BMId == targetBmId).ToList();
        }

        if (!playersToReport.Any())
        {
            sb.AppendLine("<p>No players in tracking database. Start by tracking players from the server list.</p>");
        }

        var groupedPlayers = playersToReport.GroupBy(p => string.IsNullOrEmpty(p.LastServerName) ? "Global / Legacy" : p.LastServerName);

        foreach(var group in groupedPlayers)
        {
            sb.AppendLine($"<div class='section-title' style='color:#58a6ff; font-size:16px; margin-top:40px; border-bottom: 2px solid #30363d;'>{group.Key}</div>");
            
            foreach(var p in group)
            {
                if (p.IsBMOnly)
                {
                    sb.AppendLine($"<div class='player-card theme-offline' style='padding: 15px; display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px;'>");
                    sb.AppendLine($"<h2 style='margin: 0;'>{p.Name}</h2>");
                    sb.AppendLine($"<a href='https://www.battlemetrics.com/players/{p.BMId}' target='_blank' style='background-color: #58a6ff; color: #ffffff; padding: 8px 16px; text-decoration: none; border-radius: 4px; font-weight: bold; cursor: pointer;'>View on BattleMetrics</a>");
                    sb.AppendLine("</div>");
                    continue;
                }

                var totalTime = TimeSpan.Zero;
                var past7Days = TimeSpan.Zero;
                var now = DateTime.UtcNow;
                
                int[] hourActivity = new int[24];
                Dictionary<DateTime, int> dailyActivity = new Dictionary<DateTime, int>();

                List<PlayerSession> sessionsSnapshot;
                lock (_dbLock)
                {
                    sessionsSnapshot = p.Sessions.ToList();
                }

                // Sessions can come from two independent sources now — this PC's own polling
                // and the 24/7 BattleMetrics bot — so the same real playtime can end up
                // recorded twice with slightly different Connect/Disconnect timestamps
                // (never an exact match, so a simple "same ConnectTime" dedup misses it).
                // Merge overlapping/adjacent intervals first so every stat below is computed
                // from real, non-overlapping playtime — this is also what guarantees no
                // single calendar day can ever add up to more than its own 24h.
                var rawIntervals = sessionsSnapshot
                    .Select(s => (Start: s.ConnectTime, End: s.DisconnectTime ?? now))
                    .Where(iv => iv.End > iv.Start)
                    .OrderBy(iv => iv.Start)
                    .ToList();

                var mergedIntervals = new List<(DateTime Start, DateTime End)>();
                foreach (var iv in rawIntervals)
                {
                    if (mergedIntervals.Count > 0 && iv.Start <= mergedIntervals[^1].End)
                    {
                        if (iv.End > mergedIntervals[^1].End)
                            mergedIntervals[^1] = (mergedIntervals[^1].Start, iv.End);
                    }
                    else
                    {
                        mergedIntervals.Add(iv);
                    }
                }

                foreach (var (ivStart, ivEnd) in mergedIntervals)
                {
                    var dur = ivEnd - ivStart;
                    totalTime += dur;
                    if (ivStart > now.AddDays(-7)) past7Days += dur;

                    // Split by calendar day so a session crossing midnight attributes its
                    // time to both days correctly, instead of dumping all of it on day 1.
                    var dayIter = ivStart;
                    while (dayIter < ivEnd)
                    {
                        var dayEnd = dayIter.Date.AddDays(1);
                        var chunkEnd = dayEnd < ivEnd ? dayEnd : ivEnd;
                        var date = dayIter.Date;
                        if (!dailyActivity.ContainsKey(date)) dailyActivity[date] = 0;
                        dailyActivity[date] += (int)(chunkEnd - dayIter).TotalMinutes;
                        dayIter = chunkEnd;
                    }

                    var iter = ivStart;
                    while (iter < ivEnd)
                    {
                        hourActivity[iter.ToLocalTime().Hour]++;
                        iter = iter.AddHours(1);
                    }
                }

                double avgSessionMins = mergedIntervals.Count > 0 ? totalTime.TotalMinutes / mergedIntervals.Count : 0;
                var isOnline = sessionsSnapshot.Any(s => !s.DisconnectTime.HasValue);
                var themeClass = isOnline ? "theme-online" : "theme-offline";

                sb.AppendLine($"<div class='player-card {themeClass}'>");

                var lastS = sessionsSnapshot.LastOrDefault();
                string lastConnectedStr = lastS != null ? lastS.ConnectTime.ToLocalTime().ToString("dd/MM/yyyy, hh:mm tt") : "Never";
                string lastSeenStr = lastS != null ? (lastS.DisconnectTime?.ToLocalTime().ToString("dd/MM/yyyy, hh:mm tt") ?? "Active Now") : "Never";

                // ── Derived stats (RustPlayerTrack-style) ──────────────────────
                var longestSession = mergedIntervals.Count > 0 ? mergedIntervals.Max(iv => iv.End - iv.Start) : TimeSpan.Zero;

                int dayStreak = 0;
                {
                    var activeDates = dailyActivity.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderByDescending(d => d).ToList();
                    if (activeDates.Count > 0)
                    {
                        dayStreak = 1;
                        var cursor = activeDates[0];
                        for (int i = 1; i < activeDates.Count; i++)
                        {
                            if (activeDates[i] == cursor.AddDays(-1)) { dayStreak++; cursor = activeDates[i]; }
                            else break;
                        }
                    }
                }

                var cutoff90 = now.AddDays(-90);
                var last90 = mergedIntervals.Where(iv => iv.Start >= cutoff90).ToList();
                int sessions90d = last90.Count;
                var playtime90d = last90.Aggregate(TimeSpan.Zero, (acc, iv) => acc + (iv.End - iv.Start));
                double avgSession90dMins = sessions90d > 0 ? playtime90d.TotalMinutes / sessions90d : 0;
                int activeDaysLast90 = dailyActivity.Count(kv => kv.Value > 0 && kv.Key >= cutoff90.Date);

                var playSessions = mergedIntervals
                    .Select(iv => new PlaySession(new DateTimeOffset(iv.Start, TimeSpan.Zero), new DateTimeOffset(iv.End, TimeSpan.Zero)))
                    .ToList();
                var nowOffset = new DateTimeOffset(now, TimeSpan.Zero);
                string[] dowNames = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
                // Row d in the schedule grid is Monday..Sunday (calendar-week order); this
                // maps display row d -> actual System.DayOfWeek index (Sunday = 0) used by
                // PlayScheduleHeatmap's Cells array.
                int[] dayOrder = { 1, 2, 3, 4, 5, 6, 0 };
                static string Hour12Short(int h) => h == 0 ? "12a" : h < 12 ? $"{h}a" : h == 12 ? "12p" : $"{h - 12}p";
                static string Hour12Long(int h) => h == 0 ? "12 AM" : h < 12 ? $"{h} AM" : h == 12 ? "12 PM" : $"{h - 12} PM";

                // Peak day/hour from the default (7-day) view, for the header pill.
                var defaultHm = PlayScheduleHeatmap.Build(playSessions, windowDays: 7, offset: 0, tz: TimeZoneInfo.Local, now: nowOffset);
                int peakDay = 0, peakHour = 0; double peakAvg = -1;
                for (int d = 0; d < 7; d++)
                    for (int h = 0; h < 24; h++)
                        if (defaultHm.Cells[d][h].AverageMinutes > peakAvg) { peakAvg = defaultHm.Cells[d][h].AverageMinutes; peakDay = d; peakHour = h; }
                string peakStr = peakAvg > 0 ? $"{dowNames[peakDay]} {Hour12Long(peakHour)}" : "N/A";

                string pid = "p" + System.Text.RegularExpressions.Regex.Replace(p.BMId ?? "x", "[^a-zA-Z0-9]", "");
                string statusLine = isOnline
                    ? $"Online since {lastConnectedStr} &middot; {activeDaysLast90}d active in last 90 days"
                    : $"Last seen {lastSeenStr} &middot; {activeDaysLast90}d active in last 90 days";

                // ── Header ──────────────────────────────────────────────────────
                sb.AppendLine("<div class='ph-header'>");
                sb.AppendLine("<div class='ph-name-row'>");
                sb.AppendLine($"<span class='ph-dot {(isOnline ? "online" : "offline")}'></span>");
                sb.AppendLine($"<span class='ph-name'>{p.Name}</span>");
                sb.AppendLine($"<a class='ph-bmlink' href='https://www.battlemetrics.com/players/{p.BMId}' target='_blank'>View on BattleMetrics</a>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class='ph-substatus'>{statusLine}</div>");
                sb.AppendLine("<div class='ph-pills'>");
                sb.AppendLine($"<span class='ph-pill'>90d sessions: {sessions90d}</span>");
                sb.AppendLine($"<span class='ph-pill'>playtime: {(int)playtime90d.TotalDays}d {playtime90d.Hours}h</span>");
                sb.AppendLine($"<span class='ph-pill accent'>🔥 peak: {peakStr}</span>");
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");

                // ── Session History tiles ──────────────────────────────────────
                sb.AppendLine("<div class='section-title'>SESSION HISTORY</div>");
                sb.AppendLine("<div class='ph-subtitle'>Last 90 days &middot; this server</div>");
                sb.AppendLine("<div class='tile-grid'>");
                sb.AppendLine($"<div class='tile'><div class='tile-icon'>📊</div><div class='tile-value'>{sessions90d}</div><div class='tile-label'>Total Sessions</div></div>");
                sb.AppendLine($"<div class='tile'><div class='tile-icon'>⏱️</div><div class='tile-value'>{(int)playtime90d.TotalDays}d {playtime90d.Hours}h</div><div class='tile-label'>Total Playtime</div></div>");
                sb.AppendLine($"<div class='tile'><div class='tile-icon'>📈</div><div class='tile-value'>{(int)avgSession90dMins}m</div><div class='tile-label'>Avg Session</div></div>");
                sb.AppendLine($"<div class='tile'><div class='tile-icon'>⚡</div><div class='tile-value'>{(int)longestSession.TotalHours}h {longestSession.Minutes}m</div><div class='tile-label'>Longest Session</div></div>");
                sb.AppendLine($"<div class='tile'><div class='tile-icon'>🔥</div><div class='tile-value'>{dayStreak}</div><div class='tile-label'>Day Streak</div></div>");
                sb.AppendLine("</div>");

                // ── Activity on this server (a tracked player is only watched on the
                // server it was added from, so there's just the one row to show) ──
                sb.AppendLine("<div class='section-title'>ACTIVITY ON THIS SERVER</div>");
                sb.AppendLine("<div class='server-row'>");
                sb.AppendLine($"<span class='server-name'>{(string.IsNullOrEmpty(p.LastServerName) ? "Unknown server" : p.LastServerName)}</span>");
                sb.AppendLine($"<span class='server-stats'>{(int)playtime90d.TotalDays}d {playtime90d.Hours}h &middot; {activeDaysLast90} day(s)</span>");
                sb.AppendLine("</div>");

                // ── Play Schedule — 7×24 heatmap with interactive 7d/30d/90d/All tabs
                // and date navigation. Built from the same overlap-merged intervals used
                // for the stats above, so it can't double-count sessions the bot and this
                // PC both recorded. All window/offset combinations are pre-computed here
                // and embedded as JSON; switching tabs/dates is pure client-side JS
                // (schedSetWindow/schedNav in the shared <script> block above) — no need
                // to regenerate or reload the report.
                sb.AppendLine("<div class='section-title'>PLAY SCHEDULE</div>");

                var scheduleWindows = new Dictionary<string, List<ScheduleWindowDto>>();
                foreach (var winDays in new[] { 7, 30, 90 })
                {
                    var list = new List<ScheduleWindowDto>();
                    int offset = 0;
                    const int maxOffsets = 24; // safety cap against pathological histories
                    while (offset < maxOffsets)
                    {
                        var hm = PlayScheduleHeatmap.Build(playSessions, windowDays: winDays, offset: offset, tz: TimeZoneInfo.Local, now: nowOffset);
                        var dayLabels = new string[7];
                        for (int d = 0; d < 7; d++)
                        {
                            int actualDow = dayOrder[d];
                            if (winDays == 7 && hm.WindowStart is DateTimeOffset ws0)
                            {
                                // WindowStart is now the Monday of the calendar week, so row d
                                // (Monday..Sunday) lines up directly with WindowStart + d days.
                                var cellDate = ws0.AddDays(d);
                                dayLabels[d] = $"{dowNames[actualDow]}\n{cellDate.Day}/{cellDate.Month}";
                            }
                            else
                            {
                                dayLabels[d] = dowNames[actualDow];
                            }
                        }
                        string label = hm.WindowStart is DateTimeOffset ws && hm.WindowEnd is DateTimeOffset we
                            ? $"{ws:d MMM} - {we:d MMM yyyy}"
                            : "All Time";

                        var lv = new int[7][];
                        var avg = new double[7][];
                        for (int d = 0; d < 7; d++)
                        {
                            int actualDow = dayOrder[d];
                            lv[d] = new int[24];
                            avg[d] = new double[24];
                            for (int h = 0; h < 24; h++)
                            {
                                lv[d][h] = hm.Cells[actualDow][h].IntensityLevel;
                                avg[d][h] = Math.Round(hm.Cells[actualDow][h].AverageMinutes, 1);
                            }
                        }

                        list.Add(new ScheduleWindowDto
                        {
                            Label = label,
                            DayLabels = dayLabels,
                            Lv = lv,
                            Avg = avg,
                            Sessions = hm.SessionsInWindow,
                            Weeks = hm.WeeksInWindow,
                            HasPrev = hm.HasOlderData,
                        });

                        if (!hm.HasOlderData) break;
                        offset++;
                    }
                    scheduleWindows[winDays.ToString()] = list;
                }
                {
                    var hmAll = PlayScheduleHeatmap.Build(playSessions, windowDays: null, offset: 0, tz: TimeZoneInfo.Local, now: nowOffset);
                    var lv = new int[7][]; var avg = new double[7][];
                    var allDayLabels = new string[7];
                    for (int d = 0; d < 7; d++)
                    {
                        int actualDow = dayOrder[d];
                        lv[d] = new int[24]; avg[d] = new double[24];
                        allDayLabels[d] = dowNames[actualDow];
                        for (int h = 0; h < 24; h++)
                        {
                            lv[d][h] = hmAll.Cells[actualDow][h].IntensityLevel;
                            avg[d][h] = Math.Round(hmAll.Cells[actualDow][h].AverageMinutes, 1);
                        }
                    }
                    scheduleWindows["all"] = new List<ScheduleWindowDto>
                    {
                        new ScheduleWindowDto { Label = "All Time", DayLabels = allDayLabels, Lv = lv, Avg = avg, Sessions = hmAll.SessionsInWindow, Weeks = hmAll.WeeksInWindow, HasPrev = false }
                    };
                }

                string scheduleJson = JsonSerializer.Serialize(scheduleWindows, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                sb.AppendLine($"<script>window.SCHED_DATA['{pid}'] = {scheduleJson};</script>");

                var initial = scheduleWindows["7"][0];

                sb.AppendLine("<div class='sched-toolbar'>");
                sb.AppendLine($"<div class='sched-tabs' id='sched-tabs-{pid}'>");
                sb.AppendLine($"<button type='button' class='sched-tab active' data-win='7' onclick='schedSetWindow(\"{pid}\",\"7\")'>7d</button>");
                sb.AppendLine($"<button type='button' class='sched-tab' data-win='30' onclick='schedSetWindow(\"{pid}\",\"30\")'>30d</button>");
                sb.AppendLine($"<button type='button' class='sched-tab' data-win='90' onclick='schedSetWindow(\"{pid}\",\"90\")'>90d</button>");
                sb.AppendLine($"<button type='button' class='sched-tab' data-win='all' onclick='schedSetWindow(\"{pid}\",\"all\")'>All</button>");
                sb.AppendLine("</div>");
                sb.AppendLine("<div class='sched-nav'>");
                sb.AppendLine($"<button type='button' id='sched-prev-{pid}' onclick='schedNav(\"{pid}\",1)'{(initial.HasPrev ? "" : " disabled")}>&lsaquo;</button>");
                sb.AppendLine($"<span id='sched-range-{pid}' class='sched-range'>{initial.Label}</span>");
                sb.AppendLine($"<button type='button' id='sched-next-{pid}' disabled onclick='schedNav(\"{pid}\",-1)'>&rsaquo;</button>");
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");

                sb.AppendLine("<div class='schedule-heatmap'>");
                sb.AppendLine("<div class='schedule-hourlabels'>");
                for (int h = 0; h < 24; h++)
                    sb.AppendLine(h % 2 == 0 ? $"<div class='schedule-hourlabel'>{Hour12Short(h)}</div>" : "<div class='schedule-hourlabel'></div>");
                sb.AppendLine("</div>");
                sb.AppendLine($"<div class='schedule-grid' id='sched-grid-{pid}'>");
                for (int d = 0; d < 7; d++)
                {
                    sb.AppendLine("<div class='schedule-row'>");
                    string dayLabelHtml = initial.DayLabels[d].Replace("\n", "<br>");
                    string dayPlain = initial.DayLabels[d].Replace("\n", " ");
                    sb.AppendLine($"<div class='schedule-daylabel'>{dayLabelHtml}</div>");
                    for (int h = 0; h < 24; h++)
                    {
                        int lvVal = initial.Lv[d][h];
                        string lv = lvVal > 0 ? $"lv{lvVal}" : "";
                        sb.AppendLine($"<div class='schedule-cell {lv}' title='{dayPlain} {h:00}:00 — avg {initial.Avg[d][h]} min/week'></div>");
                    }
                    sb.AppendLine("</div>");
                }
                sb.AppendLine("</div>");
                sb.AppendLine("</div>");

                sb.AppendLine("<div class='sched-footer'>");
                sb.AppendLine($"<span id='sched-caption-{pid}'>{initial.Sessions} session(s) &middot; avg per week over {initial.Weeks} week(s)</span>");
                sb.AppendLine("<span class='sched-legend'>Less <span class='schedule-cell'></span><span class='schedule-cell lv1'></span><span class='schedule-cell lv2'></span><span class='schedule-cell lv3'></span><span class='schedule-cell lv4'></span><span class='schedule-cell lv5'></span> More</span>");
                sb.AppendLine("</div>");

            // AI Insights Box
            int peakPlay = 0; int maxPlayVal = -1;
            int peakSleep = 0; int minPlayVal = int.MaxValue;
            for(int i=0; i<24; i++) {
                if (hourActivity[i] > maxPlayVal) { maxPlayVal = hourActivity[i]; peakPlay = i; }
                if (hourActivity[i] < minPlayVal) { minPlayVal = hourActivity[i]; peakSleep = i; }
            }

            sb.AppendLine("<div class='insight-box'>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>⚡</span> Most likely to play: <b>" + $"{peakPlay:00}:00 - {(peakPlay + 3) % 24:00}:00" + "</b></div>");
            sb.AppendLine("<div class='insight-item'><span class='insight-icon'>💤</span> Most likely to sleep: <b>" + $"{peakSleep:00}:00 - {(peakSleep + 5) % 24:00}:00" + "</b></div>");
            if (mergedIntervals.Count < 5) {
                sb.AppendLine("<div class='warning'><b>Data Confidence: LOW</b><br/>More sessions needed for accurate pattern recognition. Predictions currenty represent early observations.</div>");
            } else {
                sb.AppendLine("<div style='color: #8b949e; font-size: 11px; margin-top: 10px;'>Forecast based on " + mergedIntervals.Count + " recorded sessions.</div>");
            }
            sb.AppendLine("</div>");

            sb.AppendLine("</div>");
        }
    }
        
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static string? _foundServerId;

    public static void StartPolling(string host, int port, string name, string? bmId = null)
    {
        _lastServerHost = host;
        _lastServerPort = port;
        _lastServerName = name;
        _foundServerId = null; // Always reset — forces fresh BM lookup for this server

        _settings.LastHost = host;
        _settings.LastPort = port;
        _settings.LastServerName = name;
        _settings.LastBMId = null;
        SaveSettings();

        _trackingTimer?.Dispose();
        if (GetTrackedPlayers().Any(p => !p.IsBMOnly))
        {
            _trackingTimer = new Timer(async _ =>
            {
                await PollOnceAsync();
                // Runs before the cloud sync below so a rename is already migrated to the
                // new BM ID by the time we ask the cloud who's online/offline.
                await DetectPossibleNameChangesAsync();
                await SyncTrackedSessionsFromCloudAsync();
            }, null, 0, 120_000);
        }
        else
        {
            _trackingTimer = null;
        }
    }

    public static void StopPolling()
    {
        _trackingTimer?.Dispose();
        _trackingTimer = null;
    }

    public static async Task FetchOnlinePlayersNowAsync()
    {
        await PollOnceAsync();
    }

    private static async Task PollOnceAsync()
    {
        if (string.IsNullOrEmpty(_lastServerHost)) return;

        try
        {
            // ── STEP 1: Discover BM Server ID ──
            if (string.IsNullOrEmpty(_foundServerId))
            {
                StatusMessage = "Looking up server on BattleMetrics...";
                OnOnlinePlayersUpdated?.Invoke();

                // A: Search by IP address
                var searchUrlAddr = $"https://api.battlemetrics.com/servers?filter[address]={Uri.EscapeDataString(_lastServerHost)}&filter[game]=rust";
                using var responseAddr = await BmGetAsync(searchUrlAddr);
                if (responseAddr.IsSuccessStatusCode)
                {
                    var resAddr = await responseAddr.Content.ReadAsStringAsync();
                    using var docAddr = JsonDocument.Parse(resAddr);
                    var dataArr = docAddr.RootElement.GetProperty("data");

                    foreach (var serverObj in dataArr.EnumerateArray())
                    {
                        var attr = serverObj.GetProperty("attributes");
                        var foundIp   = attr.TryGetProperty("ip",        out var ipEl)    ? ipEl.GetString()    : "";
                        var foundPort = attr.TryGetProperty("port",      out var portEl)  ? portEl.GetInt32()   : 0;
                        var foundPortQ= attr.TryGetProperty("portQuery", out var portQEl) ? portQEl.GetInt32()  : 0;
                        var foundName = attr.TryGetProperty("name",      out var nameEl)  ? nameEl.GetString() ?? "" : "";

                        if (foundIp == _lastServerHost)
                        {
                            // BM stores the game port; Rust+ uses the companion app port
                            // (typically gamePort + 67, e.g. 28015 → 28082).
                            // Accept ±100 tolerance to bridge that offset.
                            bool portMatch = foundPort == _lastServerPort
                                || (foundPortQ > 0 && foundPortQ == _lastServerPort)
                                || Math.Abs(foundPort - _lastServerPort) <= 100;
                            bool nameMatch = !string.IsNullOrEmpty(_lastServerName)
                                && foundName.Contains(_lastServerName, StringComparison.OrdinalIgnoreCase);

                            if (portMatch || nameMatch || string.IsNullOrEmpty(_lastServerName))
                            {
                                _foundServerId = serverObj.GetProperty("id").GetString();
                                Log($"[BM] Found server by IP: {_foundServerId} ({foundName})");
                                break;
                            }
                        }
                    }
                }

                // B: Fallback — search by server name
                if (string.IsNullOrEmpty(_foundServerId) && !string.IsNullOrEmpty(_lastServerName))
                {
                    StatusMessage = "Searching BattleMetrics by name...";
                    OnOnlinePlayersUpdated?.Invoke();

                    var searchUrlName = $"https://api.battlemetrics.com/servers?filter[game]=rust&filter[search]={Uri.EscapeDataString(_lastServerName)}&page[size]=10";
                    using var responseName = await BmGetAsync(searchUrlName);
                    if (responseName.IsSuccessStatusCode)
                    {
                        var resName = await responseName.Content.ReadAsStringAsync();
                        using var docName = JsonDocument.Parse(resName);
                        var dataArr = docName.RootElement.GetProperty("data");

                        foreach (var serverObj in dataArr.EnumerateArray())
                        {
                            var attr = serverObj.GetProperty("attributes");
                            var foundName = attr.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";

                            if (foundName.Equals(_lastServerName, StringComparison.OrdinalIgnoreCase))
                            {
                                _foundServerId = serverObj.GetProperty("id").GetString();
                                Log($"[BM] Found server by name: {_foundServerId} ({foundName})");
                                break;
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(_foundServerId))
            {
                StatusMessage = $"Server not found on BattleMetrics ({_lastServerHost}:{_lastServerPort})";
                OnOnlinePlayersUpdated?.Invoke();
                return;
            }

            // Publica o servidor + ID resolvido na BattleMetrics para o Supabase, para
            // o bot dedicado (Node/Railway) saber que servidor vigiar sem ter de repetir
            // a heurística de pesquisa por IP/nome aqui feita.
            _ = PublishTrackedServerToSupabaseAsync(_foundServerId);

            // ── STEP 2: Fetch players ──
            StatusMessage = "Fetching players...";
            OnOnlinePlayersUpdated?.Invoke();

            var reqUrl = $"https://api.battlemetrics.com/servers/{_foundServerId}?include=player,session";
            using var responsePlayers = await BmGetAsync(reqUrl);
            if (!responsePlayers.IsSuccessStatusCode)
            {
                StatusMessage = $"BattleMetrics error: {(int)responsePlayers.StatusCode}";
                OnOnlinePlayersUpdated?.Invoke();
                return;
            }

            var pRes = await responsePlayers.Content.ReadAsStringAsync();
            using var pDoc = JsonDocument.Parse(pRes);

            var onlineList = new List<OnlinePlayerBM>();
            var newOnlineIds = new HashSet<string>();

            // Build session start time map: playerId -> sessionStart
            var sessionStartByPlayer = new Dictionary<string, DateTime>();
            if (pDoc.RootElement.TryGetProperty("included", out var includedAll))
            {
                foreach (var inc in includedAll.EnumerateArray())
                {
                    if (!inc.TryGetProperty("type", out var tEl)) continue;
                    if (tEl.GetString() != "session") continue;

                    // Session has relationships.player.data.id
                    if (!inc.TryGetProperty("relationships", out var rels)) continue;
                    if (!rels.TryGetProperty("player", out var playerRel)) continue;
                    if (!playerRel.TryGetProperty("data", out var playerData)) continue;
                    if (!playerData.TryGetProperty("id", out var playerIdEl)) continue;
                    var playerId = playerIdEl.GetString() ?? "";

                    // Session start is in attributes.start (ISO 8601)
                    if (!inc.TryGetProperty("attributes", out var sAttrs)) continue;
                    if (!sAttrs.TryGetProperty("start", out var startEl)) continue;
                    if (DateTime.TryParse(startEl.GetString(), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out var sessionStart))
                    {
                        // Keep earliest start if multiple sessions (shouldn't happen but safe)
                        if (!sessionStartByPlayer.ContainsKey(playerId))
                            sessionStartByPlayer[playerId] = sessionStart.ToUniversalTime();
                    }
                }

                // Now process players
                foreach (var inc in includedAll.EnumerateArray())
                {
                    if (!inc.TryGetProperty("type", out var typeEl) || typeEl.GetString() != "player")
                        continue;

                    var bmId = inc.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    var name = "";
                    if (inc.TryGetProperty("attributes", out var attrs) &&
                        attrs.TryGetProperty("name", out var nameEl))
                        name = nameEl.GetString() ?? "Unknown";

                    if (string.IsNullOrEmpty(bmId)) continue;

                    // Calculate duration from session start
                    TimeSpan duration = TimeSpan.Zero;
                    int seconds = 0;

                    if (sessionStartByPlayer.TryGetValue(bmId, out var sessionStart))
                    {
                        duration = DateTime.UtcNow - sessionStart;
                        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                        seconds = (int)duration.TotalSeconds;
                    }
                    else
                    {
                        // Fallback: try meta fields (legacy format)
                        if (inc.TryGetProperty("meta", out var meta))
                        {
                            if (meta.TryGetProperty("metadata", out var metaArr) && metaArr.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var mObj in metaArr.EnumerateArray())
                                {
                                    if (mObj.TryGetProperty("key", out var k) && k.GetString() == "time" &&
                                        mObj.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number)
                                    {
                                        seconds = v.GetInt32();
                                        break;
                                    }
                                }
                            }
                            else if (meta.TryGetProperty("time", out var timeProp) && timeProp.ValueKind == JsonValueKind.Number)
                            {
                                seconds = timeProp.GetInt32();
                            }
                        }
                        duration = TimeSpan.FromSeconds(seconds);
                    }

                    bool isTracked;
                    lock (_dbLock) isTracked = _trackedPlayers.ContainsKey(bmId);

                    onlineList.Add(new OnlinePlayerBM
                    {
                        BMId = bmId,
                        Name = name,
                        Duration = duration,
                        SessionStartTimeUtc = DateTime.UtcNow - duration,
                        IsTracked = isTracked
                    });
                    newOnlineIds.Add(bmId);
                }
            }

            StatusMessage = onlineList.Count == 0 ? "No players found on BattleMetrics." : "";
            LastOnlinePlayers = onlineList.OrderByDescending(x => x.Duration).ToList();
            LastPullTime = DateTime.Now;
            OnOnlinePlayersUpdated?.Invoke();

            // Session bookkeeping (connect/disconnect detection, history) is now owned
            // entirely by the dedicated BattleMetrics bot — see SyncTrackedSessionsFromCloudAsync.
            // Polling here only feeds the live "who's online" list above and keeps
            // bm_tracked_servers fresh for the bot; it no longer writes session history
            // itself, so the same real playtime doesn't get recorded twice.
        }
        catch (Exception ex)
        {
            StatusMessage = $"BattleMetrics error: {ex.Message}";
            OnOnlinePlayersUpdated?.Invoke();
        }
    }

    // A deteção de connect/disconnect local (que fazia polling próprio à BattleMetrics
    // em paralelo com o bot dedicado) foi substituída por SyncTrackedSessionsFromCloudAsync,
    // que lê o estado já gravado pelo bot em vez de o decidir aqui outra vez.

    public static bool OfflineDeathAlertsEnabled
    {
        get => _settings.OfflineDeathAlertsEnabled;
        set { _settings.OfflineDeathAlertsEnabled = value; SaveSettings(); }
    }

    public static string OfflineDeathSoundPath
    {
        get => _settings.OfflineDeathSoundPath;
        set { _settings.OfflineDeathSoundPath = value; SaveSettings(); }
    }

    public static bool OfflineDeathSoundLoopEnabled
    {
        get => _settings.OfflineDeathSoundLoopEnabled;
        set { _settings.OfflineDeathSoundLoopEnabled = value; SaveSettings(); }
    }

    public static bool OfflineDeathDiscordEnabled
    {
        get => _settings.OfflineDeathDiscordEnabled;
        set { _settings.OfflineDeathDiscordEnabled = value; SaveSettings(); }
    }

    public static List<OfflineDeathNotification> OfflineDeathHistory
    {
        get
        {
            if (_settings.OfflineDeathHistory == null) _settings.OfflineDeathHistory = new();
            return _settings.OfflineDeathHistory;
        }
    }

    public static void AddOfflineDeath(OfflineDeathNotification notification)
    {
        if (_settings.OfflineDeathHistory == null) _settings.OfflineDeathHistory = new();
        _settings.OfflineDeathHistory.Insert(0, notification);
        if (_settings.OfflineDeathHistory.Count > 100)
        {
            _settings.OfflineDeathHistory.RemoveAt(_settings.OfflineDeathHistory.Count - 1);
        }
        SaveSettings();
    }

    public static void ClearOfflineDeathHistory()
    {
        if (_settings.OfflineDeathHistory != null)
        {
            _settings.OfflineDeathHistory.Clear();
        }
        SaveSettings();
    }

    public static bool NotificationsToastEnabled
    {
        get => _settings.NotificationsToastEnabled;
        set { _settings.NotificationsToastEnabled = value; SaveSettings(); }
    }

    public static bool NotificationsSoundsEnabled
    {
        get => _settings.NotificationsSoundsEnabled;
        set { _settings.NotificationsSoundsEnabled = value; SaveSettings(); }
    }

    public static int NotificationsRetentionDays
    {
        get => _settings.NotificationsRetentionDays <= 0 ? 30 : _settings.NotificationsRetentionDays;
        set { _settings.NotificationsRetentionDays = value; SaveSettings(); }
    }

    public static List<string> MutedNotificationServers
    {
        get
        {
            if (_settings.MutedNotificationServers == null) _settings.MutedNotificationServers = new();
            return _settings.MutedNotificationServers;
        }
    }

    public static void MuteServer(string host, int port, string? name = null)
    {
        var key = $"{host}:{port}";
        if (_settings.MutedNotificationServers == null) _settings.MutedNotificationServers = new();
        if (_settings.MutedNotificationServerNames == null) _settings.MutedNotificationServerNames = new();
        if (!string.IsNullOrWhiteSpace(name))
            _settings.MutedNotificationServerNames[key] = name;
        if (!_settings.MutedNotificationServers.Contains(key))
        {
            _settings.MutedNotificationServers.Add(key);
            SaveSettings();
        }
        else if (!string.IsNullOrWhiteSpace(name))
        {
            SaveSettings();
        }
    }

    public static string? GetMutedServerName(string key) =>
        _settings.MutedNotificationServerNames?.GetValueOrDefault(key);

    public static void UnmuteServer(string host, int port) => UnmuteServer($"{host}:{port}");

    public static void UnmuteServer(string key)
    {
        var removed = _settings.MutedNotificationServers?.Remove(key) == true;
        var removedName = _settings.MutedNotificationServerNames?.Remove(key) == true;
        if (removed || removedName)
        {
            SaveSettings();
        }
    }

    private static readonly TimeSpan DismissedPairingTtl = TimeSpan.FromMinutes(30);

    private static string PairingSignature(string host, int port, string steamId64) =>
        $"{(host ?? "").Trim().ToLowerInvariant()}:{port}|{steamId64 ?? ""}";

    // Entries are stored as "signature::isoTimestamp" so a dismissal only suppresses the
    // automatic FCM keepalive resend for a limited window, not forever — otherwise the
    // user could never re-pair a server they deleted in a previous session.
    public static void AddDismissedPairing(string host, int port, string steamId64)
    {
        _settings.DismissedPairingSignatures ??= new();
        var sig = PairingSignature(host, port, steamId64);
        _settings.DismissedPairingSignatures.RemoveAll(e => SignaturePart(e) == sig);
        _settings.DismissedPairingSignatures.Add($"{sig}::{DateTime.UtcNow:o}");
        SaveSettings();
    }

    public static bool IsPairingDismissed(string host, int port, string steamId64)
    {
        var list = _settings.DismissedPairingSignatures;
        if (list == null || list.Count == 0) return false;

        var sig = PairingSignature(host, port, steamId64);
        var match = list.FirstOrDefault(e => SignaturePart(e) == sig);
        if (match == null) return false;

        var idx = match.IndexOf("::", StringComparison.Ordinal);
        DateTime dismissedAt = idx >= 0 && DateTime.TryParse(match[(idx + 2)..],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : DateTime.MinValue; // legacy entries with no timestamp: treat as expired

        if (DateTime.UtcNow - dismissedAt > DismissedPairingTtl)
        {
            list.Remove(match);
            SaveSettings();
            return false;
        }
        return true;
    }

    private static string SignaturePart(string entry)
    {
        var idx = entry.IndexOf("::", StringComparison.Ordinal);
        return idx >= 0 ? entry[..idx] : entry;
    }
}
