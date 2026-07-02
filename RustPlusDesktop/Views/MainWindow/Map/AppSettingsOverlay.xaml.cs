using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Navigation;
using System.Net.Http;
using System.Text.Json;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class AppSettingsOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private bool _isSettingsInitialized = false;

        public class LanguageOption
        {
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
            public string? ImagePath { get; set; }
        }

        public AppSettingsOverlay()
        {
            InitializeComponent();
            Loaded += AppSettingsOverlay_Loaded;
        }

        private static string T(string key, string fallback)
        {
            return Properties.Resources.ResourceManager.GetString(key) ?? fallback;
        }

        private void AppSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSettingsInitialized) return;
            
            PopulateLanguages();
            LoadSettings();
            _isSettingsInitialized = true;
        }

        private void PopulateLanguages()
        {
            var langs = new List<LanguageOption>
            {
                new() { Name = "System Default", Code = "", ImagePath = null },
                // Region-specific codes matching %locale% Crowdin-generated folders
                new() { Name = "English",           Code = "en-US",  ImagePath = "pack://application:,,,/Assets/Flags/en.png" },
                new() { Name = "Deutsch",            Code = "de-DE",  ImagePath = "pack://application:,,,/Assets/Flags/de.png" },
                new() { Name = "Français",           Code = "fr-FR",  ImagePath = "pack://application:,,,/Assets/Flags/fr.png" },
                new() { Name = "Español",            Code = "es-ES",  ImagePath = "pack://application:,,,/Assets/Flags/es-ES.png" },
                new() { Name = "Italiano",           Code = "it-IT",  ImagePath = "pack://application:,,,/Assets/Flags/it.png" },
                new() { Name = "Polski",             Code = "pl-PL",  ImagePath = "pack://application:,,,/Assets/Flags/pl.png" },
                new() { Name = "Русский",            Code = "ru-RU",  ImagePath = "pack://application:,,,/Assets/Flags/ru.png" },
                new() { Name = "Türkçe",             Code = "tr-TR",  ImagePath = "pack://application:,,,/Assets/Flags/tr.png" },
                new() { Name = "Português (BR)",     Code = "pt-BR",  ImagePath = "pack://application:,,,/Assets/Flags/pt-BR.png" },
                new() { Name = "Português (PT)",     Code = "pt-PT",  ImagePath = "pack://application:,,,/Assets/Flags/pt-PT.png" },
                new() { Name = "Nederlands",         Code = "nl-NL",  ImagePath = "pack://application:,,,/Assets/Flags/nl.png" },
                new() { Name = "Dansk",              Code = "da-DK",  ImagePath = "pack://application:,,,/Assets/Flags/da.png" },
                new() { Name = "Norsk",              Code = "no-NO",  ImagePath = "pack://application:,,,/Assets/Flags/no.png" },
                new() { Name = "Svenska",            Code = "sv-SE",  ImagePath = "pack://application:,,,/Assets/Flags/sv-SE.png" },
                new() { Name = "Suomi",              Code = "fi-FI",  ImagePath = "pack://application:,,,/Assets/Flags/fi.png" },
                new() { Name = "Čeština",            Code = "cs-CZ",  ImagePath = "pack://application:,,,/Assets/Flags/cs.png" },
                new() { Name = "Magyar",             Code = "hu-HU",  ImagePath = "pack://application:,,,/Assets/Flags/hu.png" },
                new() { Name = "Română",             Code = "ro-RO",  ImagePath = "pack://application:,,,/Assets/Flags/ro.png" },
                new() { Name = "Srpski",             Code = "sr-SP",  ImagePath = "pack://application:,,,/Assets/Flags/sr.png" },
                new() { Name = "Ελληνικά",           Code = "el-GR",  ImagePath = "pack://application:,,,/Assets/Flags/el.png" },
                new() { Name = "Українська",         Code = "uk-UA",  ImagePath = "pack://application:,,,/Assets/Flags/uk.png" },
                new() { Name = "Tiếng Việt",         Code = "vi-VN",  ImagePath = "pack://application:,,,/Assets/Flags/vi.png" },
                new() { Name = "العربية",             Code = "ar-SA",  ImagePath = "pack://application:,,,/Assets/Flags/ar.png" },
                new() { Name = "עברית",              Code = "he-IL",  ImagePath = "pack://application:,,,/Assets/Flags/he.png" },
                new() { Name = "日本語",              Code = "ja-JP",  ImagePath = "pack://application:,,,/Assets/Flags/ja.png" },
                new() { Name = "한국어",              Code = "ko-KR",  ImagePath = "pack://application:,,,/Assets/Flags/ko.png" },
                new() { Name = "简体中文",            Code = "zh-CN",  ImagePath = "pack://application:,,,/Assets/Flags/zh-CN.png" },
                new() { Name = "繁體中文",            Code = "zh-TW",  ImagePath = "pack://application:,,,/Assets/Flags/zh-TW.png" },
                new() { Name = "简体中文 (Hans)",     Code = "zh-Hans", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hans.png" },
                new() { Name = "繁體中文 (Hant)",     Code = "zh-Hant", ImagePath = "pack://application:,,,/Assets/Flags/zh-Hant.png" },
                new() { Name = "Català",             Code = "ca-ES",  ImagePath = "pack://application:,,,/Assets/Flags/ca.png" },
                new() { Name = "Afrikaans",          Code = "af-ZA",  ImagePath = "pack://application:,,,/Assets/Flags/af.png" },
            };

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Code == "" ? 1 : 0).ThenBy(l => l.Name).ToList();
        }

        public void LoadSettings()
        {
            CmbLanguage.SelectedValue = TrackingService.SelectedLanguage;
            
            ChkAutoStart.IsChecked = TrackingService.AutoStartEnabled;
            ChkStartMinimized.IsChecked = TrackingService.StartMinimizedEnabled;
            ChkAutoConnect.IsChecked = TrackingService.AutoConnectEnabled;
            ChkCloseToTray.IsChecked = TrackingService.CloseToTrayEnabled;
            ChkBackgroundTracking.IsChecked = TrackingService.IsBackgroundTrackingEnabled;
            ChkAutoLoadShops.IsChecked = TrackingService.AutoLoadShops;
            CmbMonumentDisplayMode.SelectedIndex = Math.Clamp(TrackingService.MapMonumentDisplayMode, 0, 1);
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            ChkStreamerMode.IsChecked = TrackingService.MapAbbreviateNames;
            SliderMonumentScale.Value = TrackingService.MapMonumentScale;
            SliderMonumentOpacity.Value = TrackingService.MapMonumentOpacity;
            PopulateExtraMonumentFilters();

            // Cloud Sync Setting load
            ChkCloudSync.IsChecked = TrackingService.CloudSyncEnabled;

            // Team marker settings
            ChkShowProfileMarkers.IsChecked  = TrackingService.MapShowSteamMarkers;
            ChkShowPlayerArrows.IsChecked    = TrackingService.MapShowPlayerArrows;
            ChkShowDeathMarkers.IsChecked    = TrackingService.MapShowDeathTags;
            ChkStreamerModeMarkers.IsChecked  = TrackingService.MapAbbreviateNames;
            SliderPlayerIconScaleOverlay.Value = TrackingService.MapPlayerIconScale;

            // Offline Death
            ChkOfflineDeathAlerts.IsChecked = TrackingService.OfflineDeathAlertsEnabled;
            TxtOfflineDeathSoundPath.Text = string.IsNullOrEmpty(TrackingService.OfflineDeathSoundPath) ? Properties.Resources.DefaultSoundLabel : System.IO.Path.GetFileName(TrackingService.OfflineDeathSoundPath);
            ChkOfflineDeathSoundLoop.IsChecked = TrackingService.OfflineDeathSoundLoopEnabled;

            // Notification Center Settings
            ChkNotificationsToast.IsChecked = TrackingService.NotificationsToastEnabled;
            ChkNotificationsSounds.IsChecked = TrackingService.NotificationsSoundsEnabled;
            SliderNotificationsRetention.Value = TrackingService.NotificationsRetentionDays;
            TxtRetentionDays.Text = string.Format(T("NotificationRetentionDays", "{0} days"), (int)SliderNotificationsRetention.Value);
            PopulateMutedServers();


            // Auth connection state
            // Bot connection status is handled by LoadDiscordBotSettingsAsync
            BtnDiscordConnect.Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary;

            // BrdSupporterSettings and BtnEmailConnect removed from UI

            // Always load Discord bot settings (no premium check needed)
            _ = LoadDiscordBotSettingsAsync();
        }

        private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            var code = CmbLanguage.SelectedValue as string;
            if (code != null)
            {
                TrackingService.SelectedLanguage = code;
                
                // Try to apply it immediately
                if (Application.Current is App app)
                {
                    app.SetLanguage();
                }
            }
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.AutoStartEnabled = ChkAutoStart.IsChecked == true;
            TrackingService.StartMinimizedEnabled = ChkStartMinimized.IsChecked == true;
            TrackingService.AutoConnectEnabled = ChkAutoConnect.IsChecked == true;
            TrackingService.CloseToTrayEnabled = ChkCloseToTray.IsChecked == true;
            TrackingService.IsBackgroundTrackingEnabled = ChkBackgroundTracking.IsChecked == true;
            TrackingService.AutoLoadShops = ChkAutoLoadShops.IsChecked == true;
            if (CmbMonumentDisplayMode != null && CmbMonumentDisplayMode.SelectedIndex >= 0)
            {
                TrackingService.MapMonumentDisplayMode = CmbMonumentDisplayMode.SelectedIndex;
            }
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
            TrackingService.MapAbbreviateNames = ChkStreamerMode.IsChecked == true;
            TrackingService.MapMonumentScale = SliderMonumentScale.Value;
            TrackingService.MapMonumentOpacity = SliderMonumentOpacity.Value;
            
            // Save Cloud Sync setting
            if (sender == ChkCloudSync)
            {
                if (ChkCloudSync.IsChecked == true)
                {
                    if (ParentWindow != null)
                    {
                        var dlg = new CloudDisclaimerWindow { Owner = ParentWindow };
                        dlg.ShowDialog();
                        if (dlg.CloudSyncAccepted)
                        {
                            TrackingService.CloudSyncEnabled = true;
                            TrackingService.UploadConsentGiven = true;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                        }
                        else
                        {
                            _isSettingsInitialized = false;
                            ChkCloudSync.IsChecked = false;
                            _isSettingsInitialized = true;
                            TrackingService.CloudSyncEnabled = false;
                            TrackingService.UploadConsentGiven = false;
                            _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                        }
                    }
                    else
                    {
                        TrackingService.CloudSyncEnabled = true;
                        TrackingService.UploadConsentGiven = true;
                        _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(true);
                    }
                }
                else
                {
                    TrackingService.CloudSyncEnabled = false;
                    TrackingService.UploadConsentGiven = false;
                    _ = Services.Auth.SupabaseAuthManager.UpdateCloudSyncConsentAsync(false);
                }
            }
            else
            {
                TrackingService.CloudSyncEnabled = ChkCloudSync.IsChecked == true;
            }

            TrackingService.OfflineDeathAlertsEnabled = ChkOfflineDeathAlerts.IsChecked == true;
            TrackingService.OfflineDeathSoundLoopEnabled = ChkOfflineDeathSoundLoop.IsChecked == true;

            // Notification Center Settings
            TrackingService.NotificationsToastEnabled = ChkNotificationsToast.IsChecked == true;
            TrackingService.NotificationsSoundsEnabled = ChkNotificationsSounds.IsChecked == true;
            TrackingService.NotificationsRetentionDays = (int)SliderNotificationsRetention.Value;
            TxtRetentionDays.Text = string.Format(T("NotificationRetentionDays", "{0} days"), (int)SliderNotificationsRetention.Value);
            PopulateMutedServers();

            ParentWindow?.ApplySettings();
            ParentWindow?.UpdateCloudSyncUI();
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
        }

        private void PopulateExtraMonumentFilters()
        {
            PnlExtraMonumentFilters.Children.Clear();

            var types = ParentWindow?.GetKnownExtraMonumentTypes();
            if (types == null || types.Count == 0)
            {
                PnlExtraMonumentFilters.Children.Add(TxtExtraMonFiltersEmpty);
                return;
            }

            var dotStyle = TryFindResource("DotCheckBox") as System.Windows.Style;
            foreach (var name in types)
            {
                var chk = new System.Windows.Controls.CheckBox
                {
                    Content = name,
                    IsChecked = !TrackingService.IsExtraMonumentTypeHidden(name),
                    Margin = new System.Windows.Thickness(0, 3, 0, 3),
                    Tag = name,
                    FontSize = 12,
                    Style = dotStyle,
                };
                chk.Checked += OnExtraMonumentFilterChanged;
                chk.Unchecked += OnExtraMonumentFilterChanged;
                PnlExtraMonumentFilters.Children.Add(chk);
            }
        }

        private void OnExtraMonumentFilterChanged(object? sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            if (sender is not System.Windows.Controls.CheckBox chk || chk.Tag is not string name) return;
            TrackingService.SetExtraMonumentTypeHidden(name, chk.IsChecked != true);
            ParentWindow?.RebuildExtraMonumentOverlay();
        }

        private void OnMarkerSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.MapShowSteamMarkers  = ChkShowProfileMarkers.IsChecked == true;
            TrackingService.MapShowPlayerArrows  = ChkShowPlayerArrows.IsChecked == true;
            TrackingService.MapShowDeathTags     = ChkShowDeathMarkers.IsChecked == true;
            TrackingService.MapAbbreviateNames   = ChkStreamerModeMarkers.IsChecked == true;
            TrackingService.MapPlayerIconScale   = SliderPlayerIconScaleOverlay.Value;

            ParentWindow?.SyncPlayerSettingsFromTrackingService();
        }


        private void BtnShowResetDialog_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;
            var dialog = new ResetDataWindow { Owner = ParentWindow };
            if (dialog.ShowDialog() == true)
            {
                _ = ParentWindow.PerformGranularResetAsync(
                    dialog.ResetConnection,
                    dialog.ResetProfiles,
                    dialog.ResetSteam,
                    dialog.ResetPairing,
                    dialog.ResetCrosshairs,
                    dialog.ResetCache
                );
            }
        }
      
        private void BtnDelete3DMapData_Click(object sender, RoutedEventArgs e)
        {
            var owner = ParentWindow ?? Window.GetWindow(this);
            var result = MessageBox.Show(
                "Delete all cached 3D map data for every server? This removes parsed map files and generated viewer JSON, but keeps app assets and icons.",
                "Delete 3D Map Data",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var deleted = Map3DLocalBuildService.DeleteAllCachedMapData();
            ParentWindow?.ResetBuildingBlockedZonesAfterCacheDelete();
            ParentWindow?.AppendLog($"[3D Map] Deleted cached 3D map data ({deleted.DeletedFiles} files, {deleted.DeletedDirectories} folders). Generated data will be rebuilt when needed.");
            MessageBox.Show(owner, "Cached 3D map data deleted. It will be rebuilt when you open a 3D map again.", "3D Map Data", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void BtnBackupData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var dialog = new BackupPasswordDialog { Owner = ParentWindow };
            dialog.SetMode(false); // Encryption mode

            if (dialog.ShowDialog() == true)
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ZIP Archives (*.zip)|*.zip",
                    FileName = "RustPlusDesk_Backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip",
                    Title = Properties.Resources.BackupApplicationDataTitle
                };

                if (sfd.ShowDialog() == true)
                {
                    try
                    {
                        RustPlusDesk.Services.Data.BackupDataModule.CreateBackup(sfd.FileName, dialog.Password);
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupSuccessLog, sfd.FileName));
                        MessageBox.Show(Properties.Resources.BackupSuccessMessage, Properties.Resources.BackupSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        ParentWindow.AppendLog(string.Format(Properties.Resources.BackupErrorLog, ex.Message));
                        MessageBox.Show(string.Format(Properties.Resources.BackupErrorMessage, ex.Message), Properties.Resources.BackupFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void BtnRestoreData_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;

            var ask = MessageBox.Show(
                Properties.Resources.RestoreConfirmMessage,
                Properties.Resources.RestoreConfirmTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (ask != MessageBoxResult.Yes) return;

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ZIP Archives (*.zip)|*.zip",
                Title = Properties.Resources.RestoreApplicationDataTitle
            };

            if (ofd.ShowDialog() == true)
            {
                string password = "";
                if (RustPlusDesk.Services.Data.BackupDataModule.IsBackupEncrypted(ofd.FileName))
                {
                    var dialog = new BackupPasswordDialog { Owner = ParentWindow };
                    dialog.SetMode(true); // Decryption mode

                    if (dialog.ShowDialog() == true)
                    {
                        password = dialog.Password;
                    }
                    else
                    {
                        // User canceled decryption prompt, abort restore
                        return;
                    }
                }

                try
                {
                    RustPlusDesk.Services.Data.BackupDataModule.RestoreBackup(ofd.FileName, password);
                    ParentWindow.ReloadApplicationData();
                    MessageBox.Show(Properties.Resources.RestoreSuccessMessage, Properties.Resources.RestoreSuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    ParentWindow.AppendLog(Properties.Resources.RestorePasswordErrorLog);
                    MessageBox.Show(Properties.Resources.RestorePasswordErrorMessage, Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch (Exception ex)
                {
                    ParentWindow.AppendLog(string.Format(Properties.Resources.RestoreErrorLog, ex.Message));
                    MessageBox.Show(string.Format(Properties.Resources.RestoreErrorMessage, ex.Message), Properties.Resources.RestoreFailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCompareCloud_Click(object sender, RoutedEventArgs e)
        {
            var cloudWindow = new RustPlusDesk.Views.Windows.CloudFeaturesWindow();
            cloudWindow.Owner = ParentWindow ?? Window.GetWindow(this);
            cloudWindow.ShowDialog();
        }

        // ── Discord Bot ──────────────────────────────────────────────────────────

        private void BtnDiscordConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://discord.com/oauth2/authorize?client_id=1519500428146901082&permissions=8&integration_type=0&scope=bot+applications.commands",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Failed to open invite link: {ex.Message}");
            }
        }

        private void OnChannelSettingChanged(object sender, RoutedEventArgs e)
        {
            // Auto-save TTS/Sound toggles when changed
            _ = SaveChannelTogglesAsync();
        }

        private async Task SaveChannelTogglesAsync()
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;
            try
            {
                var channels = await LoadDiscordChannelsFromSupabase();
                if (channels == null) return;

                foreach (var ch in channels)
                {
                    bool tts = false, audio = false;
                    switch (ch.NotificationType)
                    {
                        case "raid":         tts = ChkRaidTTS.IsChecked == true;      audio = ChkRaidAudio.IsChecked == true;      break;
                        case "events":       tts = ChkEventsTTS.IsChecked == true;    audio = ChkEventsAudio.IsChecked == true;    break;
                        case "teamchat":
                        case "chat":         tts = ChkChatTTS.IsChecked == true;      audio = ChkChatAudio.IsChecked == true;      break;
                        default: continue;
                    }

                    await Services.Auth.SupabaseAuthManager.Client
                        .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                        .Where(x => x.GuildId == ch.GuildId && x.NotificationType == ch.NotificationType)
                        .Set(x => x.TtsEnabled, tts)
                        .Set(x => x.AudioAlertEnabled, audio)
                        .Update();
                }
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Error saving channel toggles: {ex.Message}");
            }
        }

        private async Task<List<RustPlusDesk.Models.DiscordChannelsConfigModel>?> LoadDiscordChannelsFromSupabase()
        {
            try
            {
                var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
                var steamId = vm?.SteamId64;
                if (string.IsNullOrEmpty(steamId)) return null;

                // Get guild_id from discord_bot_settings by steam_id
                var settingsRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                    .Where(x => x.OwnerSteamId == steamId)
                    .Get();

                var guildSetting = settingsRes.Models?.FirstOrDefault();
                if (guildSetting == null || string.IsNullOrEmpty(guildSetting.GuildId)) return null;

                var channelsRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                    .Where(x => x.GuildId == guildSetting.GuildId)
                    .Get();

                return channelsRes.Models ?? new();
            }
            catch { return null; }
        }

        private async Task LoadDiscordBotSettingsAsync()
        {
            if (Services.Auth.SupabaseAuthManager.Client == null) return;

            try
            {
                var vm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel;
                var steamId = vm?.SteamId64;
                if (string.IsNullOrEmpty(steamId)) return;

                // Check connection status
                var settingsRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordBotSettingsModel>()
                    .Where(x => x.OwnerSteamId == steamId)
                    .Get();

                var guildSetting = settingsRes.Models?.FirstOrDefault();
                bool connected = guildSetting != null && !string.IsNullOrEmpty(guildSetting.GuildId);

                Dispatcher.Invoke(() =>
                {
                    if (connected)
                    {
                        TxtAuthStatus.Text = "Connected";
                        TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
                        DotBotStatus.Fill = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
                    }
                    else
                    {
                        TxtAuthStatus.Text = "Not connected - Please invite Discord Bot to your server and do /setup <steamid64>";
                        TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
                        DotBotStatus.Fill = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
                    }
                });

                if (!connected) return;

                // Load discord_guild_channels to get channel names
                var guildChannelsRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordGuildChannelsModel>()
                    .Where(x => x.GuildId == guildSetting!.GuildId)
                    .Get();

                var guildChannels = guildChannelsRes.Models?.FirstOrDefault();

                // Load discord_channels_config for TTS/Sound settings
                var channelsRes = await Services.Auth.SupabaseAuthManager.Client
                    .From<RustPlusDesk.Models.DiscordChannelsConfigModel>()
                    .Where(x => x.GuildId == guildSetting!.GuildId)
                    .Get();

                var channelsList = channelsRes.Models ?? new();

                Dispatcher.Invoke(() =>
                {
                    // Set channel display names from discord_guild_channels
                    TxtChannelRaid.Text     = FormatChannelId(guildChannels?.RaidId);
                    TxtChannelEvents.Text   = FormatChannelId(guildChannels?.EventsId);
                    TxtChannelChat.Text     = FormatChannelId(guildChannels?.TeamchatId);

                    // Reset all toggles
                    ChkRaidTTS.IsChecked = ChkRaidAudio.IsChecked = false;
                    ChkEventsTTS.IsChecked = ChkEventsAudio.IsChecked = false;
                    ChkChatTTS.IsChecked = ChkChatAudio.IsChecked = false;

                    foreach (var ch in channelsList)
                    {
                        switch (ch.NotificationType)
                        {
                            case "raid":            ChkRaidTTS.IsChecked = ch.TtsEnabled;      ChkRaidAudio.IsChecked = ch.AudioAlertEnabled;      break;
                            case "events":          ChkEventsTTS.IsChecked = ch.TtsEnabled;    ChkEventsAudio.IsChecked = ch.AudioAlertEnabled;    break;
                            case "teamchat":
                            case "chat":            ChkChatTTS.IsChecked = ch.TtsEnabled;      ChkChatAudio.IsChecked = ch.AudioAlertEnabled;      break;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                ParentWindow?.AppendLog($"[DiscordBot] Error loading settings: {ex.Message}");
            }
        }

        private static string FormatChannelId(string? channelId)
        {
            if (string.IsNullOrEmpty(channelId)) return "—";
            // Show as #channel-id (Discord channel IDs are numeric)
            return $"#{channelId}";
        }

                private void BtnSelectOfflineDeathSound_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio Files (*.mp3, *.wav)|*.mp3;*.wav",
                Title = "Select Custom Death Sound"
            };
            if (ofd.ShowDialog() == true)
            {
                TrackingService.OfflineDeathSoundPath = ofd.FileName;
                TxtOfflineDeathSoundPath.Text = System.IO.Path.GetFileName(ofd.FileName);
            }
        }

        private void BtnResetOfflineDeathSound_Click(object sender, RoutedEventArgs e)
        {
            TrackingService.OfflineDeathSoundPath = string.Empty;
            TxtOfflineDeathSoundPath.Text = Properties.Resources.DefaultSoundLabel;
        }

        private void BtnOpenOfflineDeathsLog_Click(object sender, RoutedEventArgs e)
        {
            if (ParentWindow == null) return;
            var win = new Windows.OfflineDeathsHistoryWindow { Owner = ParentWindow };
            win.ShowDialog();
        }

        private void PopulateMutedServers()
        {
            if (PnlMutedServers == null) return;
            PnlMutedServers.Children.Clear();

            var muted = TrackingService.MutedNotificationServers;
            if (muted == null || muted.Count == 0)
            {
                PnlMutedServers.Children.Add(new TextBlock
                {
                    Text = "No servers muted",
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (var serverKey in muted)
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var txt = new TextBlock
                {
                    Text = serverKey,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11
                };
                Grid.SetColumn(txt, 0);
                grid.Children.Add(txt);

                var btn = new Button
                {
                    Content = "Unmute",
                    Height = 20,
                    Padding = new Thickness(6, 1, 6, 1),
                    FontSize = 10,
                    Tag = serverKey,
                    Style = FindResource("GhostButton") as Style
                };
                btn.Click += (s, e) =>
                {
                    if (s is Button b && b.Tag is string key)
                    {
                        var parts = key.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int port))
                        {
                            TrackingService.UnmuteServer(parts[0], port);
                            PopulateMutedServers();
                        }
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);

                PnlMutedServers.Children.Add(grid);
            }
        }

        private void BtnModifyChatAlerts_Click(object sender, RoutedEventArgs e) =>
            ParentWindow?.OpenChatAlertsFromSettings();

        private void BtnChatCommands_Click(object sender, RoutedEventArgs e) =>
            ParentWindow?.OpenChatCommandsFromSettings();

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch { }
        }

      
    }
}
