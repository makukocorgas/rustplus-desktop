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

    /// <summary>
    /// A single new line, for a subscriber that would rather append one than re-read the room.
    ///
    /// Declared for parity with the push-based original, but never raised here: the batched
    /// catch-up that <see cref="ChatChanged"/> triggers already fetches and appends every new
    /// line by id, and a subscriber wired to both dedupes by id on its own — so firing this too
    /// would only cost a second parse of the same message.
    /// </summary>
    public static event Action<Models.ChatLine>? ChatMessageReceived;

    /// <summary>
    /// A line was removed from the room by a moderator.
    ///
    /// Declared but not yet raised: nothing in the client can delete a line yet, so there is
    /// nothing for a poll to detect. Wiring this up is for whenever that moderation action lands.
    /// </summary>
    public static event Action<string>? ChatMessageDeleted;

    /// <summary>The room's slow-mode cooldown changed. Polled alongside the room itself.</summary>
    public static event Action<Models.ChatSlowModeEvent>? SlowModeUpdated;

    /// <summary>
    /// A sanction was issued against or lifted from the account reading right now.
    ///
    /// Declared for parity, but not raised: the room read that already drives
    /// <see cref="ChatChanged"/> carries the caller's own sanction state on every tick, and the
    /// panel applies it from there. What this event adds beyond that is the public "so-and-so was
    /// silenced" transparency line, which needs knowing who acted and why — left for when
    /// moderator tooling exists to say so.
    /// </summary>
    public static event Action<Models.SystemSanctionEvent>? SanctionEventReceived;

    /// <summary>A message landed in a thread. The argument is the conversation it belongs to.</summary>
    public static event Action<string>? MessageArrived;

    /// <summary>Somebody would like to be on your friends list.</summary>
    public static event Action? FriendRequestArrived;

    /// <summary>Somebody wants to open a thread and is waiting to be let in.</summary>
    public static event Action? RequestArrived;

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(6);
    private static readonly object Gate = new();

    private static CancellationTokenSource? _cts;
    private static string? _lastChatSeenAt;
    private static int _lastSlowModeSeconds = -1;
    private static readonly Dictionary<string, string?> _lastMessageAtByThread = new();
    private static readonly HashSet<string> _knownPendingThreads = new();
    private static readonly HashSet<string> _knownIncomingFriendRequests = new();

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
        _lastSlowModeSeconds = -1;
        _lastMessageAtByThread.Clear();
        _knownPendingThreads.Clear();
        _knownIncomingFriendRequests.Clear();
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
        // The public room: a cheap "since" read is enough to know whether it moved, and it also
        // carries the room's current slow-mode value along for free.
        var snapshot = await SocialApi.GetChatAsync(_lastChatSeenAt).ConfigureAwait(false);
        if (snapshot.Ok)
        {
            if (snapshot.Lines.Count > 0)
            {
                _lastChatSeenAt = snapshot.Lines[^1].SentAtIso ?? _lastChatSeenAt;
                Raise(() => ChatChanged?.Invoke());
            }

            if (snapshot.SlowModeSeconds != _lastSlowModeSeconds)
            {
                _lastSlowModeSeconds = snapshot.SlowModeSeconds;
                var slowMode = new Models.ChatSlowModeEvent { Seconds = snapshot.SlowModeSeconds };
                Raise(() => SlowModeUpdated?.Invoke(slowMode));
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

        // Friend requests: the same "a new pending row appeared" trick, on the incoming side only
        // - an outgoing request the other side has not answered yet is not something to nudge for.
        var friends = await SocialApi.GetFriendsAsync().ConfigureAwait(false);
        if (friends.Ok)
        {
            var seenNow = new HashSet<string>();
            foreach (var request in friends.Incoming)
            {
                seenNow.Add(request.Id);
                if (_knownIncomingFriendRequests.Add(request.Id))
                    Raise(() => FriendRequestArrived?.Invoke());
            }

            // Answered elsewhere (another device, or expired) - stop watching for it here too.
            _knownIncomingFriendRequests.IntersectWith(seenNow);
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
