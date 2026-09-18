using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using RustPlusDesk.Services.Achievements;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    private bool _achievementsWired;

    /// <summary>
    /// Hooks the achievement service up to the button and the snackbar. Called once
    /// during startup; everything after that is event-driven, so nothing polls.
    /// </summary>
    private void WireAchievements()
    {
        if (_achievementsWired) return;
        _achievementsWired = true;

        AchievementService.Unlocked += OnAchievementUnlocked;
        AchievementService.UnseenChanged += OnAchievementUnseenChanged;

        UpdateAchievementBadge(AchievementService.UnseenCount);

        // Connecting, restoring the team, reading the device list and reloading the
        // cloud state all happen in the first seconds and can each earn something.
        // Those are recorded silently; toasts start once the app has settled.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(12));
            AchievementService.SuppressUnlockedEvents = false;
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void OnAchievementUnlocked(AchievementDef def)
    {
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                ShowInfoSnackbar(
                    Helpers.Loc.Text("AchievementUnlocked", "Achievement unlocked"),
                    def.Name,
                    WpfUi.ControlAppearance.Success,
                    WpfUi.SymbolRegular.Trophy24);

                FlashAchievementButton();

                if (AchievementService.AllEarned)
                {
                    ShowInfoSnackbar(
                        Helpers.Loc.Text("AchievementsAllUnlockedTitle", "All Achievements unlocked"),
                        Helpers.Loc.Text("AchievementsAllUnlockedBody", "All Achievements unlocked. Well done!"),
                        WpfUi.ControlAppearance.Success,
                        WpfUi.SymbolRegular.Trophy24);
                }
            }
            catch
            {
                // Never let a bit of celebration break whatever earned it.
            }
        });
    }

    private void OnAchievementUnseenChanged(int count)
        => Dispatcher.InvokeAsync(() => UpdateAchievementBadge(count));

    private void UpdateAchievementBadge(int count)
    {
        if (AchievementBadge == null || TxtAchievementBadge == null) return;

        if (count <= 0)
        {
            AchievementBadge.Visibility = Visibility.Collapsed;
            return;
        }

        TxtAchievementBadge.Text = count > 9 ? "9+" : count.ToString();
        AchievementBadge.Visibility = Visibility.Visible;
    }

    /// <summary>A short glow, so something earned mid-game is noticed without a dialog.</summary>
    private void FlashAchievementButton()
    {
        if (BtnAchievements == null) return;

        var animation = new DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = TimeSpan.FromMilliseconds(320),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        };

        BtnAchievements.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void BtnAchievements_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var window = new Windows.AchievementsWindow(this);
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            AppendLog($"[Achievements] Could not open: {ex.Message}");
        }
    }

    /// <summary>
    /// Two finished tutorials earns it. Asks the progress store rather than counting
    /// events, so tutorials finished in an earlier session count as well.
    /// </summary>
    private async System.Threading.Tasks.Task CheckTutorialAchievementAsync()
    {
        try
        {
            if (_tutorialRegistry == null || _tutorialProgressStore == null) return;

            int done = 0;
            foreach (var def in _tutorialRegistry.Tutorials)
            {
                var progress = await _tutorialProgressStore.GetAsync(def);
                if (progress?.CompletedAtUtc != null) done++;
                if (done >= 2) break;
            }

            if (done >= 2) Ach.Unlock(Ach.Tutorials);
        }
        catch
        {
            // An achievement is never worth surfacing an error for.
        }
    }
}
