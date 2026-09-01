using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Services.Cloud;

/// <summary>
/// The other half of the Home Assistant integration: acting on what
/// <see cref="CloudHomeAssistantAdapter"/>'s token authenticates.
///
/// Home Assistant cannot reach the Rust game server itself — only the desktop app, over its own
/// Rust+ connection, can. So a REST switch call there does not flip anything directly; it drops
/// a row in <c>ha_commands</c>, and this polls for it, executes it through the same path a
/// Discord command uses, and reports back what actually happened so the switch's state in Home
/// Assistant reflects reality rather than the request that was merely accepted.
///
/// Polls regardless of connection state — a command sent while offline is not lost, only
/// deferred, since it is acknowledged (and only then removed) after it is actually run.
/// </summary>
public static class HomeAssistantRelay
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(8);
    private static readonly object Gate = new();
    private static CancellationTokenSource? _cts;

    public static void EnsureStarted()
    {
        if (!Cloud.CloudAuth.IsAuthenticated) return;

        lock (Gate)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
        }

        _ = PollLoopAsync(_cts.Token);
    }

    public static void Stop()
    {
        CancellationTokenSource? cts;
        lock (Gate)
        {
            cts = _cts;
            _cts = null;
        }

        try { cts?.Cancel(); } catch { }
        cts?.Dispose();
    }

    private static async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(PollInterval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            if (ct.IsCancellationRequested) return;
            if (!Cloud.CloudAuth.IsAuthenticated) continue;

            try { await PollOnceAsync().ConfigureAwait(false); }
            catch { /* transient — the next tick tries again */ }
        }
    }

    private static async Task PollOnceAsync()
    {
        var body = await SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/commands", HttpMethod.Get)
            .ConfigureAwait(false);

        using var doc = JsonDocument.Parse(body);
        if (!doc.RootElement.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
            return;

        var mainWindow = System.Windows.Application.Current?.Dispatcher.Invoke(
            () => System.Windows.Application.Current.MainWindow as Views.MainWindow);
        if (mainWindow == null) return;

        var executedIds = new System.Collections.Generic.List<string>();

        foreach (var row in rows.EnumerateArray())
        {
            var id = row.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var entityIdRaw = row.TryGetProperty("entity_id", out var eidEl) ? eidEl.GetRawText() : null;
            var turnOn = row.TryGetProperty("turn_on", out var onEl) && onEl.ValueKind == JsonValueKind.True;

            if (string.IsNullOrEmpty(id) || entityIdRaw == null || !uint.TryParse(entityIdRaw, out var entityId))
                continue;

            bool success = false;
            try
            {
                success = await mainWindow.Dispatcher.Invoke(
                    () => mainWindow.ToggleSmartSwitchFromDiscordAsync(entityId, turnOn));
            }
            catch { /* not connected, or the toggle itself failed — left for the next poll */ }

            if (!success) continue;

            executedIds.Add(id!);

            try
            {
                await SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/state", HttpMethod.Post,
                    payload: new { entity_id = entityId, is_on = turnOn }).ConfigureAwait(false);
            }
            catch { /* the switch already moved; a stale read-back is a cosmetic miss, not a retry-worthy one */ }
        }

        if (executedIds.Count > 0)
        {
            try
            {
                await SupabaseAuthManager.CallEdgeFunctionAsync("home-assistant/commands/ack", HttpMethod.Post,
                    payload: new { ids = executedIds }).ConfigureAwait(false);
            }
            catch { /* left un-acked — executed again next tick, which is a toggle back to the same state */ }
        }
    }
}
