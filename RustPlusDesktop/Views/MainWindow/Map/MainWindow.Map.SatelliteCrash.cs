using RustPlusDesk.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RustPlusDesk.Views;

/// <summary>
/// Scaffolding for Rust's upcoming "Satellite Crash" event (Naval Update follow-up content,
/// still on Facepunch's staging branch as of July 2026 — not yet released, no confirmed
/// ship date). Rust+ has no documented marker type or trigger for it yet, so the actual
/// detection below is a stub: it never fires until <see cref="SatelliteCrashMarkerType"/> and
/// <see cref="TryDetectSatelliteCrash"/> are filled in with real data once the update ships.
/// Everything else (settings toggle, chat/Discord/toast notifications, event dock entry) is
/// fully wired so only the detection needs to change later.
/// </summary>
public partial class MainWindow
{
    // TODO: replace with the real AppMarkerType value once Facepunch documents/ships it.
    // -1 guarantees this never matches a real marker's Type in the meantime.
    private const int SatelliteCrashMarkerType = -1;

    private bool _satelliteCrashActive;
    private DateTime? _satelliteCrashSpawnTime;
    private DateTime? _satelliteCrashDespawnTime;

    private class SatelliteCrashSite
    {
        public uint MarkerId;
        public double X, Y;
        public DateTime CrashedAt;
        public FrameworkElement? MapElement;
        public TextBlock? TimerLabel;
    }
    private readonly List<SatelliteCrashSite> _satelliteCrashSites = new();

    /// <summary>
    /// Distinct from <see cref="PlaceHeliCrashSite"/> — uses the satellite icon (not the shared
    /// explosion.png) and a cyan timer label instead of orange, per the user's explicit request
    /// to tell the two crash sites apart at a glance.
    /// </summary>
    private FrameworkElement PlaceSatelliteCrashSite(SatelliteCrashSite site)
    {
        var container = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };

        var img = new Image { Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
        try { img.Source = new BitmapImage(new Uri("pack://application:,,,/Assets/icons/satellitecrash.png")); } catch { }
        container.Children.Add(img);

        var lbl = new TextBlock
        {
            Text = "0m ago",
            Foreground = new SolidColorBrush(Color.FromRgb(80, 200, 255)),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8, Color = Colors.Black }
        };
        container.Children.Add(lbl);
        site.TimerLabel = lbl;

        ToolTipService.SetToolTip(container, Properties.Resources.SatelliteCrash);

        var p = WorldToImagePx(site.X, site.Y);
        Canvas.SetLeft(container, p.X - 14);
        Canvas.SetTop(container, p.Y - 14);
        Panel.SetZIndex(container, 910);
        Overlay.Children.Add(container);
        return container;
    }

    private void UpdateSatelliteCrashSites()
    {
        var expired = _satelliteCrashSites.Where(cs => (DateTime.UtcNow - cs.CrashedAt).TotalMinutes >= 10).ToList();
        foreach (var cs in expired)
        {
            if (cs.MapElement != null) Overlay.Children.Remove(cs.MapElement);
            _satelliteCrashSites.Remove(cs);
        }

        foreach (var cs in _satelliteCrashSites)
        {
            if (cs.TimerLabel != null)
            {
                int mins = (int)(DateTime.UtcNow - cs.CrashedAt).TotalMinutes;
                cs.TimerLabel.Text = mins == 0 ? (RustPlusDesk.Properties.Resources.ResourceManager.GetString("CodeUiJustNow") ?? "just now") : $"{mins}m ago";
            }
        }
    }

    /// <summary>
    /// TODO: fill in once the event ships. Likely candidates to check, based on how the other
    /// world events (Cargo Ship, Patrol Heli, CH47, Locked Crate) are represented:
    ///  - A dedicated <see cref="RustPlusClientReal.DynMarker"/> Type value in the
    ///    GetMapMarkers feed (mirrors how Cargo Ship=5, Patrol Heli=8, CH47=4 work today) — the
    ///    cleanest case, just set <see cref="SatelliteCrashMarkerType"/>.
    ///  - No dedicated type: falls back to a label/name heuristic instead (this is how the
    ///    *old* Deep Sea NPC shop event is detected today, see CheckDeepSeaEvent in
    ///    MainWindow.Map.Shops.cs, matching on the "Casino Bar Shopkeeper" shop name).
    ///  - A team chat system message rather than a map marker at all.
    /// Inspect real marker data once reachable (e.g. via GetDynamicMapMarkersAsync2's raw dump
    /// logging in RustPlusClientReal.cs) to find the actual signal before wiring this up.
    /// </summary>
    private static bool TryDetectSatelliteCrash(IReadOnlyList<RustPlusClientReal.DynMarker> markers, out RustPlusClientReal.DynMarker crash)
    {
        crash = default;
        if (SatelliteCrashMarkerType < 0) return false; // detection not implemented yet

        crash = markers.FirstOrDefault(m => m.Type == SatelliteCrashMarkerType);
        return crash.Id != 0;
    }

    private void CheckSatelliteCrashEvent(IReadOnlyList<RustPlusClientReal.DynMarker> markers)
    {
        bool found = TryDetectSatelliteCrash(markers, out var crash);

        if (found && !_satelliteCrashActive)
        {
            // State change: inactive → active
            _satelliteCrashActive = true;
            _satelliteCrashSpawnTime = DateTime.UtcNow;
            _satelliteCrashDespawnTime = null;

            if (_announceSpawns && TrackingService.AnnounceSatelliteCrash)
            {
                var msg = AlertTemplateService.GetAlertTemplate("AlertSatelliteCrash");
                _ = SendTeamChatSafeAsync(msg, false, true);
                _ = RustPlusDesk.Services.DiscordBotListenerService.Instance.SendNotificationAsync("events", $"🛰️ **Event:** {msg}");

                if (RustPlusDesk.Services.TrackingService.NotificationsToastEnabled)
                {
                    var notif = new RustPlusDesk.Models.RustPlusNotification(
                        type: "Event",
                        title: "🛰️ Satellite Crash",
                        message: msg,
                        serverIp: _vm?.Selected?.Host ?? "",
                        serverPort: _vm?.Selected?.Port ?? 0,
                        serverName: _vm?.Selected?.Name ?? "");
                    RustPlusDesk.Services.NotificationCenterService.AddNotification(notif);
                }
            }

            AppendLog($"[SATELLITE-CRASH] Spawn detected at {crash.X:F0},{crash.Y:F0}");

            var site = new SatelliteCrashSite { MarkerId = crash.Id, X = crash.X, Y = crash.Y, CrashedAt = DateTime.UtcNow };
            _satelliteCrashSites.Add(site);
            _ = Dispatcher.InvokeAsync(() => site.MapElement = PlaceSatelliteCrashSite(site));
        }
        else if (!found && _satelliteCrashActive)
        {
            // State change: active → inactive (looted / expired)
            _satelliteCrashActive = false;
            _satelliteCrashDespawnTime = DateTime.UtcNow;
            AppendLog("[SATELLITE-CRASH] No longer on map.");
        }
    }
}
