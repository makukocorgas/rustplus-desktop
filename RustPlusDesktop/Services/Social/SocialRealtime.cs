using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RustPlusDesk.Services.Social;

/// <summary>
/// The live half of the social layer.
///
/// Upstream drives this off Reverb (Laravel's push channel), which we do not run. Polling the
/// same authoritative endpoints on a short interval gets to the same place: every event here is
/// still a nudge, never a delivery — a subscriber re-reads through <see cref="SocialApi"/>, which
/// is the only place blocks and sanctions are enforced. A missed tick costs nothing beyond the
/// panel being a few seconds behind, and it is always correct on the next one.
///
/// Static because there is one poll loop per process, like the panel that listens to it.
/// </summary>
public static class SocialRealtime
{
    /// <summary>The public room gained a line (or its sanction changed). Carries nothing: the reader re-reads.</summary>
    public static event Action? ChatChanged;

    /// <summary>A message landed in a thread. The argument is the conversation it belongs to.</summary>
    public static event Action<string>? MessageArrived;

    /// <summary>Somebody wants to open a thread and is waiting to be let in.</summary>
    public static event Action? RequestArrived;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);
    private static readonly object Gate = new();

    private static CancellationTokenSource? _cts;
    private static string? _lastChatSeenAt;
    private static readonly Dictionary<string, string?> _lastMessageAtByThread = new();
    private static readonly HashSet<string> _knownPendingThreads = new();

    /// <summary>
    /// Starts polling, once. Safe to call on every panel open — the second call and the two
    /// hundredth do nothing until <see cref="Stop"/> is called.
    /// </summary>
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

    /// <summary>Stops polling. Called when the account goes away.</summary>
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

        _lastChatSeenAt = null;
        _lastMessageAtByThread.Clear();
        _knownPendingThreads.Clear();
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
        // The public room: a cheap "since" read is enough to know whether it moved.
        var snapshot = await SocialApi.GetChatAsync(_lastChatSeenAt).ConfigureAwait(false);
        if (snapshot.Ok)
        {
            if (snapshot.Lines.Count > 0)
            {
                _lastChatSeenAt = snapshot.Lines[^1].SentAtIso ?? _lastChatSeenAt;
                Raise(() => ChatChanged?.Invoke());
            }
        }

        // The inbox: compare each thread's last-message time against what we last saw, and
        // watch for a pending thread appearing that we had not seen yet.
        var threads = await SocialApi.GetThreadsAsync().ConfigureAwait(false);
        foreach (var thread in threads)
        {
            var stamp = thread.LastMessageAt?.ToString("O");
            if (_lastMessageAtByThread.TryGetValue(thread.Id, out var previous))
            {
                if (!string.Equals(previous, stamp, StringComparison.Ordinal))
                {
                    var id = thread.Id;
                    Raise(() => MessageArrived?.Invoke(id));
                }
            }
            _lastMessageAtByThread[thread.Id] = stamp;

            if (string.Equals(thread.State, "pending", StringComparison.OrdinalIgnoreCase)
                && _knownPendingThreads.Add(thread.Id))
            {
                Raise(() => RequestArrived?.Invoke());
            }
        }
    }

    /// <summary>
    /// Handlers touch controls, and polling runs on a background task. Marshalling here rather
    /// than in each handler means a subscriber cannot forget.
    /// </summary>
    private static void Raise(Action action)
    {
        try
        {
            var app = System.Windows.Application.Current;
            if (app?.Dispatcher != null)
                app.Dispatcher.BeginInvoke(action);
            else
                action();
        }
        catch { }
    }
}
