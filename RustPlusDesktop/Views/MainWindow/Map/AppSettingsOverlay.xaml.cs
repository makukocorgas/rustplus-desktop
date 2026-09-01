using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Net.Http;
using System.Text.Json;
using RustPlusDesk.Services;
using WpfUi = Wpf.Ui.Controls;

namespace RustPlusDesk.Views
{
    public partial class AppSettingsOverlay : UserControl
    {
        public MainWindow? ParentWindow { get; set; }
        private bool _isSettingsInitialized = false;
        private IReadOnlyList<SettingsSectionDefinition> _settingsSections = Array.Empty<SettingsSectionDefinition>();
        private IReadOnlyList<SettingsOptionDefinition> _settingsOptions = Array.Empty<SettingsOptionDefinition>();
        private string _activeSettingsCategory = "general";
        private bool _isShowingSearchResults;
        private bool _returnToCategoryPageAfterSearch;
        private readonly List<(AdornerLayer Layer, Adorner Adorner)> _settingsHighlights = new();
        private int _settingsHighlightGeneration;

        private sealed class SettingsSectionDefinition
        {
            public required string Id { get; init; }
            public required string Category { get; init; }
            public required string Title { get; init; }
            public required string Keywords { get; init; }
            public required FrameworkElement Element { get; init; }
        }

        private sealed class SettingsSearchResult
        {
            public required string Id { get; init; }
            public required string Category { get; init; }
            public required string CategoryTitle { get; init; }
            public required string Title { get; init; }
        }

        private sealed class SettingsOptionDefinition
        {
            public required string SectionId { get; init; }
            public required string SectionTitle { get; init; }
            public required string Category { get; init; }
            public required string Title { get; init; }
            public required string SearchText { get; init; }
            public required FrameworkElement Target { get; init; }
        }

        private sealed class SettingsOptionResult
        {
            public required string SectionId { get; init; }
            public required string SectionTitle { get; init; }
            public required string Category { get; init; }
            public required string CategoryTitle { get; init; }
            public required string BeforeMatch { get; init; }
            public required string Match { get; init; }
            public required string AfterMatch { get; init; }
            public required FrameworkElement Target { get; init; }
        }

        private sealed class SettingsMatchAdorner(UIElement adornedElement) : Adorner(adornedElement)
        {
            protected override void OnRender(DrawingContext drawingContext)
            {
                var bounds = new Rect(0, 0, AdornedElement.RenderSize.Width, AdornedElement.RenderSize.Height);
                drawingContext.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromArgb(0x24, 0x60, 0xCD, 0xFF)),
                    new Pen(new SolidColorBrush(Color.FromRgb(0x60, 0xCD, 0xFF)), 1.5),
                    bounds,
                    5,
                    5);
            }
        }

        public class LanguageOption
        {
            public string Name { get; set; } = "";
            public string Code { get; set; } = "";
            public string? ImagePath { get; set; }
        }

        public AppSettingsOverlay()
        {
            InitializeComponent();
            InitializeSettingsNavigation();
            Loaded += AppSettingsOverlay_Loaded;
            IsVisibleChanged += AppSettingsOverlay_IsVisibleChanged;

            Loaded += (_, __) => ApplyEventCapabilities();
            Services.EventCapabilities.Changed += OnEventCapabilitiesChanged;
            Unloaded += (_, __) => Services.EventCapabilities.Changed -= OnEventCapabilitiesChanged;
        }

        private void OnEventCapabilitiesChanged() => Dispatcher.Invoke(ApplyEventCapabilities);

        /// <summary>
        /// Greys out the Discord shop-alerts channel on servers without vending data. Left in
        /// place rather than hidden: the channel ID stays configured and works again elsewhere,
        /// and a visibly disabled field answers "why do I get no shop alerts?" on its own.
        /// </summary>
        private void ApplyEventCapabilities()
        {
            bool shopsAvailable = !Services.EventCapabilities.IsCloudSourced;
            string? hint = shopsAvailable ? null : Properties.Resources.AlertUnavailableOnServer;

            if (ShopChannelLabel != null)
            {
                ShopChannelLabel.Opacity = shopsAvailable ? 1.0 : 0.45;
                ShopChannelLabel.ToolTip = hint;
            }

            if (ShopChannelRow != null)
            {
                ShopChannelRow.IsEnabled = shopsAvailable;
                ShopChannelRow.Opacity = shopsAvailable ? 1.0 : 0.45;
                ShopChannelRow.ToolTip = hint;
            }
        }

        private void AppSettingsOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SettingsSearchBox.Focus();
                    Keyboard.Focus(SettingsSearchBox);
                    SettingsSearchBox.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);
                return;
            }

            _isShowingSearchResults = false;
            _returnToCategoryPageAfterSearch = false;
            if (DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.Save();
            }
            SettingsSearchBox.Clear();
            ClearSettingsHighlights();
            ShowSettingsCategoryList();
        }

        private static string T(string key, string fallback)
        {
            return RustPlusDesk.Helpers.Loc.TextOrNull(key) ?? fallback;
        }

        private void InitializeSettingsNavigation()
        {
            _settingsSections = new[]
            {
                Section("general", "general", T("General", "General"), "language startup launch windows minimized auto connect server", SectionGeneral),
                Section("behavior", "general", T("Behavior", "Behavior"), "tray closing streamer privacy background tracking console cloud sync upload", SectionBehavior),
                Section("server-events", "alerts", T("ServerEventsSection", "Server Events"), "server events audio detection listen oil rig cargo deep sea trust own detections confirm", SectionServerEvents),
                Section("offline-death", "alerts", T("OfflineDeathNotifications", "Offline Death Notifications"), "offline death raid alerts sound loop discord log", SectionOfflineDeath),
                Section("notifications", "alerts", T("NotificationCenterSettings", "Notification Center"), "toast sound alerts retention days muted servers notification center", SectionNotifications),
                Section("map-performance", "map", T("UiMapPerformanceQuality", "Map Performance & Quality"), "image scaling quality gpu bitmap cache rendering scale anti aliasing performance", SectionMapPerformance),
                Section("team-markers", "map", T("TeamMarkersSettings", "Team Markers"), "profile player direction arrows death markers streamer icon scale", SectionTeamMarkers),
                Section("3d-map", "map", T("ThreeDMapSectionTitle", "3D Map"), "3d map delete data parse manually quality", SectionThreeDMap),
                Section("discord", "connected", T("UiDiscordSettings", "Discord Settings"), "discord bot invite channels raid events chat shop trackers tts sound integrations wipe tracker player backup", SectionDiscord),
                Section("chat-commands", "chat-commands", T("ChatCommandsSettings", "Chat Commands"), "chat team commands prefix delay population time promote cargo oil rig heli vendor upkeep afk timers switches logic rules", SectionChatCommands),
                Section("alert-templates", "alert-templates", T("CustomAlertsHeader", "Chat Alert Templates"), "chat alert templates messages oil rig crate alarm deep sea shop cargo event heli player tracking online offline death respawn", SectionChatAlertTemplates),
                Section("battlemetrics", "system", "BattleMetrics", "battlemetrics api key online players roster", SectionBattleMetrics),
                Section("maintenance", "system", T("MaintenanceTitle", "Maintenance"), "reset app data backup restore maintenance", SectionMaintenance),
                Section("credits", "system", T("CreditsTitle", "Credits"), "credits rustmaps icons legal", SectionCredits)
            };

            ShowSettingsCategoryList();
        }

        private static SettingsSectionDefinition Section(string id, string category, string title, string keywords, FrameworkElement element) =>
            new() { Id = id, Category = category, Title = title, Keywords = keywords, Element = element };

        private void BuildSettingsOptionIndex()
        {
            _settingsOptions = _settingsSections
                .SelectMany(section => new[]
                    {
                        new SettingsOptionDefinition
                        {
                            SectionId = section.Id,
                            SectionTitle = section.Title,
                            Category = section.Category,
                            Title = section.Title,
                            SearchText = $"{section.Title} {section.Keywords}",
                            Target = section.Element
                        }
                    }
                    .Concat(EnumerateControls(section.Element)
                    .Where(IsSearchableSettingControl)
                    .Select(control => new SettingsOptionDefinition
                    {
                        SectionId = section.Id,
                        SectionTitle = section.Title,
                        Category = section.Category,
                        Title = GetControlTitle(control),
                        SearchText = $"{GetControlTitle(control)} {control.ToolTip} {section.Title}",
                        Target = control
                    })))
                .Where(option => option.Title.Length > 1)
                .DistinctBy(option => $"{option.SectionId}|{option.Title}", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSearchableSettingControl(Control control) =>
            control is CheckBox or ComboBox or Slider or TextBox or ButtonBase or Expander;

        private static IEnumerable<Control> EnumerateControls(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                if (child is Control control)
                {
                    yield return control;
                }

                foreach (var descendant in EnumerateControls(child))
                {
                    yield return descendant;
                }
            }
        }

        private static string GetControlTitle(Control control)
        {
            var automationName = System.Windows.Automation.AutomationProperties.GetName(control);
            if (!string.IsNullOrWhiteSpace(automationName))
            {
                return automationName.Trim();
            }

            object? label = control switch
            {
                HeaderedContentControl headered => headered.Header,
                ContentControl content => content.Content,
                _ => null
            };
            var contentText = ExtractText(label);
            return string.IsNullOrWhiteSpace(contentText) ? HumanizeControlName(control.Name) : contentText;
        }

        private static string ExtractText(object? value)
        {
            if (value is string text)
            {
                return text.Trim();
            }

            if (value is TextBlock textBlock)
            {
                return textBlock.Text.Trim();
            }

            if (value is not DependencyObject element)
            {
                return "";
            }

            return string.Join(" ", LogicalTreeHelper.GetChildren(element)
                .Cast<object>()
                .Select(ExtractText)
                .Where(text => text.Length > 0));
        }

        private static string HumanizeControlName(string name)
        {
            foreach (var prefix in new[] { "Chk", "Cmb", "Slider", "Txt", "Btn" })
            {
                if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    name = name[prefix.Length..];
                    break;
                }
            }

            var words = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    words.Append(' ');
                }
                words.Append(name[i]);
            }
            return words.ToString().Replace("Url", "URL", StringComparison.Ordinal).Trim();
        }

        private void SettingsCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsCategoryList.SelectedItem is not ListBoxItem { Tag: string category })
            {
                return;
            }

            SettingsCategoryList.SelectedItem = null;
            SettingsSearchBox.Clear();
            ShowSettingsCategory(category);
        }

        private void ShowSettingsCategoryList()
        {
            SettingsCategoryPage.Visibility = Visibility.Visible;
            SettingsDetailPage.Visibility = Visibility.Collapsed;
            SettingsCategoryList.SelectedItem = null;
        }

        private void ShowSettingsCategory(string category, string? selectedSectionId = null)
        {
            _activeSettingsCategory = category;

            // This page can be reached from the gear icon, the settings search and the tutorial,
            // none of which went through BtnOpenChatCommands_Click - the only caller that used to
            // rebuild the mappings. Sync here so every route shows the current devices.
            if (category == "chat-commands")
            {
                var chatVm = ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel
                             ?? DataContext as RustPlusDesk.ViewModels.MainViewModel;
                chatVm?.Selected?.SyncChatCommands();
            }
            var sections = _settingsSections.Where(section => section.Category == category).ToList();
            foreach (var section in _settingsSections)
            {
                section.Element.Visibility = section.Category == category ? Visibility.Visible : Visibility.Collapsed;
            }

            var (title, description) = GetSettingsCategoryText(category);
            SettingsDetailTitle.Text = title;
            SettingsDetailSubtitle.Text = description;
            SettingsCategoryPage.Visibility = Visibility.Collapsed;
            SettingsDetailPage.Visibility = Visibility.Visible;
            var submenuItems = sections.Select(section => new SettingsSearchResult
            {
                Id = section.Id,
                Category = section.Category,
                CategoryTitle = title,
                Title = section.Title
            }).ToList();
            SettingsSectionList.ItemsSource = submenuItems;
            SettingsSectionList.SelectedItem = selectedSectionId == null
                ? null
                : submenuItems.FirstOrDefault(item => item.Id == selectedSectionId);
            SettingsSectionList.Visibility = Visibility.Visible;
            SettingsSearchResultsScroller.Visibility = Visibility.Collapsed;
            SettingsScrollViewer.Visibility = Visibility.Visible;
            SettingsScrollViewer.ScrollToTop();

            if (string.IsNullOrWhiteSpace(SettingsSearchBox.Text))
            {
                ClearSettingsHighlights();
            }
            else
            {
                HighlightMatchingSettings(SettingsSearchBox.Text, category);
            }
        }

        private void HighlightMatchingSettings(string query, string category)
        {
            ClearSettingsHighlights();
            var targets = _settingsOptions
                .Where(option => option.Category == category && SettingsSearchMatcher.Matches(query, option.Title, option.SearchText))
                .Select(option => option.Target)
                .Distinct()
                .ToList();
            var generation = _settingsHighlightGeneration;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != _settingsHighlightGeneration)
                {
                    return;
                }

                foreach (var target in targets)
                {
                    var layer = AdornerLayer.GetAdornerLayer(target);
                    if (layer == null)
                    {
                        continue;
                    }

                    var adorner = new SettingsMatchAdorner(target) { IsHitTestVisible = false };
                    layer.Add(adorner);
                    _settingsHighlights.Add((layer, adorner));
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void ClearSettingsHighlights()
        {
            _settingsHighlightGeneration++;
            foreach (var (layer, adorner) in _settingsHighlights)
            {
                layer.Remove(adorner);
            }
            _settingsHighlights.Clear();
        }

        private void SettingsSectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SettingsSectionList.SelectedItem is not SettingsSearchResult selected)
            {
                return;
            }

            var section = _settingsSections.First(candidate => candidate.Id == selected.Id);
            ScrollToSettingsElement(section.Element);
        }

        private void ScrollToSettingsElement(FrameworkElement target, bool focus = false)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ExpandAncestorSettingsGroups(target);
                SettingsScrollViewer.UpdateLayout();
                target.UpdateLayout();

                try
                {
                    var top = target.TransformToAncestor(SettingsSectionsPanel).Transform(new Point()).Y;
                    SettingsScrollViewer.ScrollToVerticalOffset(Math.Clamp(top - 12, 0, SettingsScrollViewer.ScrollableHeight));
                }
                catch (InvalidOperationException)
                {
                    target.BringIntoView();
                }

                if (focus)
                {
                    target.Focus();
                    Keyboard.Focus(target);
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private static void ExpandAncestorSettingsGroups(DependencyObject element)
        {
            for (var parent = GetSettingsParent(element); parent != null; parent = GetSettingsParent(parent))
            {
                if (parent is Expander expander)
                {
                    expander.IsExpanded = true;
                }
            }
        }

        private static DependencyObject? GetSettingsParent(DependencyObject element) =>
            LogicalTreeHelper.GetParent(element) ?? (element is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : null);

        private static (string Title, string Description) GetSettingsCategoryText(string category) => category switch
        {
            "alerts" => (T("Alerts", "Alerts"), T("UiNotificationsSoundsAndOfflineDeath", "Notifications, sounds, and offline death")),
            "map" => (T("UiMap", "Map"), T("UiPerformanceMarkersAnd3DData", "Performance, markers, and 3D data")),
            "connected" => (T("UiConnectedServices", "Connected Services"), "Discord and chat"),
            "chat-commands" => (T("ChatCommandsSettings", "Chat Commands"), "Team chat commands and device bindings"),
            "alert-templates" => (T("CustomAlertsHeader", "Chat Alert Templates"), T("CustomAlertsDesc", "Customize automated chat alert messages")),
            "system" => (T("UiSystem", "System"), T("UiMaintenanceBackupResetAndCredits", "Maintenance, backup, reset, and credits")),
            _ => (T("General", "General"), T("UiLanguageStartupAndAppBehavior", "Language, startup, and app behavior"))
        };

        private void SettingsBack_Click(object sender, RoutedEventArgs e)
        {
            _isShowingSearchResults = false;
            SettingsSearchBox.Clear();
            ShowSettingsCategoryList();
        }

        public void OpenCategory(string category)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isShowingSearchResults = false;
                _returnToCategoryPageAfterSearch = false;
                SettingsSearchBox.Clear();
                ShowSettingsCategory(category);
            });
        }

        private void SettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SettingsSearchBox.Text.Trim();
            if (query.Length == 0)
            {
                ClearSettingsHighlights();
                if (!_isShowingSearchResults)
                {
                    return;
                }

                _isShowingSearchResults = false;
                if (_returnToCategoryPageAfterSearch)
                {
                    ShowSettingsCategoryList();
                }
                else
                {
                    ShowSettingsCategory(_activeSettingsCategory);
                }
                return;
            }

            ClearSettingsHighlights();
            if (!_isShowingSearchResults)
            {
                _returnToCategoryPageAfterSearch = SettingsCategoryPage.Visibility == Visibility.Visible;
            }
            _isShowingSearchResults = true;

            if (_settingsOptions.Count == 0)
            {
                BuildSettingsOptionIndex();
            }

            var matches = _settingsOptions
                .Where(option => SettingsSearchMatcher.Matches(query, option.Title, option.SearchText))
                .OrderBy(option => option.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(option => option.Title)
                .ToList();
            SettingsSearchResults.ItemsSource = matches.Select(option => CreateSettingsOptionResult(option, query)).ToList();
            SettingsDetailTitle.Text = T("CodeUiSearchResults", "Search results");
            SettingsDetailSubtitle.Text = matches.Count == 0
                ? string.Format(T("CodeUiNoSettingsFoundForQuery", "No settings found for “{0}”"), query)
                : string.Format(T("CodeUiSettingsFoundForQuery", "{0} setting{1} found for “{2}”"), matches.Count, matches.Count == 1 ? "" : "s", query);
            SettingsCategoryPage.Visibility = Visibility.Collapsed;
            SettingsDetailPage.Visibility = Visibility.Visible;
            SettingsSectionList.Visibility = Visibility.Collapsed;
            SettingsScrollViewer.Visibility = Visibility.Collapsed;
            SettingsSearchResultsScroller.Visibility = Visibility.Visible;
        }

        private static SettingsOptionResult CreateSettingsOptionResult(SettingsOptionDefinition option, string query)
        {
            var matchingTerm = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(term => option.Title.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (matchingTerm == null)
            {
                return new SettingsOptionResult
                {
                    SectionId = option.SectionId,
                    SectionTitle = option.SectionTitle,
                    Category = option.Category,
                    CategoryTitle = GetSettingsCategoryText(option.Category).Title,
                    BeforeMatch = option.Title,
                    Match = "",
                    AfterMatch = "",
                    Target = option.Target
                };
            }

            var matchIndex = option.Title.IndexOf(matchingTerm, StringComparison.OrdinalIgnoreCase);
            return new SettingsOptionResult
            {
                SectionId = option.SectionId,
                SectionTitle = option.SectionTitle,
                Category = option.Category,
                CategoryTitle = GetSettingsCategoryText(option.Category).Title,
                BeforeMatch = option.Title[..matchIndex],
                Match = option.Title.Substring(matchIndex, matchingTerm.Length),
                AfterMatch = option.Title[(matchIndex + matchingTerm.Length)..],
                Target = option.Target
            };
        }

        private void SettingsSearchResult_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfUi.Button { Tag: SettingsOptionResult result })
            {
                return;
            }

            _isShowingSearchResults = false;
            _returnToCategoryPageAfterSearch = false;
            ShowSettingsCategory(result.Category, result.SectionId);
            ScrollToSettingsElement(result.Target, focus: true);
        }

        private void AppSettingsOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isSettingsInitialized) return;

            PopulateLanguages();
            LoadSettings();
            BuildSettingsOptionIndex();
            _isSettingsInitialized = true;
        }

        private void PopulateLanguages()
        {
            // One list, shared with the LFG filter and the flag on a listing. They used to be
            // three, and they disagreed.
            var langs = new List<LanguageOption>
            {
                new() { Name = T("CodeUiSystemDefaultLanguage", "System Default"), Code = "", ImagePath = null },
            };

            langs.AddRange(Helpers.AppLanguages.All.Select(l => new LanguageOption
            {
                Name = l.Name,
                Code = l.Code,
                ImagePath = l.FlagPath,
            }));

            CmbLanguage.ItemsSource = langs.OrderBy(l => l.Code == "" ? 1 : 0).ThenBy(l => l.Name).ToList();
        }

        public void LoadSettings()
        {
            CmbLanguage.SelectedValue = TrackingService.SelectedLanguage;

            TxtBattleMetricsApiKey.Password = TrackingService.BattleMetricsApiKey;

            ChkAutoStart.IsChecked = TrackingService.AutoStartEnabled;
            ChkStartMinimized.IsChecked = TrackingService.StartMinimizedEnabled;
            ChkAutoConnect.IsChecked = TrackingService.AutoConnectEnabled;
            ChkCloseToTray.IsChecked = TrackingService.CloseToTrayEnabled;
            ChkHideConsole.IsChecked = TrackingService.HideConsole;
            ChkTrafficMonitor.IsChecked = TrackingService.TrafficMonitorEnabled;
            ChkStreamerMode.IsChecked = TrackingService.MapAbbreviateNames;

            // Map performance settings
            CmbMapScalingMode.SelectedIndex = Math.Clamp(TrackingService.MapBitmapScalingMode, 0, 2);
            ChkMapUseCacheMode.IsChecked = TrackingService.MapUseCacheMode;
            
            double scale = TrackingService.MapRenderScale;
            int renderScaleIdx = 2; // Default to 1.0 (Native)
            if (Math.Abs(scale - 0.5) < 0.01) renderScaleIdx = 0;
            else if (Math.Abs(scale - 0.75) < 0.01) renderScaleIdx = 1;
            else if (Math.Abs(scale - 1.0) < 0.01) renderScaleIdx = 2;
            else if (Math.Abs(scale - 1.25) < 0.01) renderScaleIdx = 3;
            else if (Math.Abs(scale - 1.5) < 0.01) renderScaleIdx = 4;
            else if (Math.Abs(scale - 2.0) < 0.01) renderScaleIdx = 5;
            CmbMapRenderScale.SelectedIndex = renderScaleIdx;

            ChkMapUseAliasedEdgeMode.IsChecked = TrackingService.MapUseAliasedEdgeMode;

            // Custom HD Map Image setting load
            var selectedProfile = ParentWindow?.ViewModel?.Selected;
            if (selectedProfile != null && TxtCustomMapUrl != null)
            {
                TxtCustomMapUrl.Text = selectedProfile.CustomMapUrl ?? "";
                if (!string.IsNullOrWhiteSpace(selectedProfile.CustomMapUrl))
                {
                    TxtCustomMapStatus.Text = T("CodeUiCustomHDMapActiveDesc", "Custom HD Map active. Map image is cached locally and will automatically reset on server wipe.");
                }
                else
                {
                    TxtCustomMapStatus.Text = T("CodeUiNoCustomMapUrlSetDesc", "No custom map URL set. Standard server map image is currently in use.");
                }
            }

            // Cloud Sync Setting load
            ChkCloudSync.IsChecked = TrackingService.CloudSyncEnabled;
            SyncPlayerWipeTrackerToggles();

            // Team marker settings
            ChkShowProfileMarkers.IsChecked  = TrackingService.MapShowSteamMarkers;
            ChkShowPlayerArrows.IsChecked    = TrackingService.MapShowPlayerArrows;
            ChkShowDeathMarkers.IsChecked    = TrackingService.MapShowDeathTags;
            ChkStreamerModeMarkers.IsChecked  = TrackingService.MapAbbreviateNames;
            SliderPlayerIconScaleOverlay.Value = TrackingService.MapPlayerIconScale;

            // Server events (audio fallback)
            ChkListenForServerEvents.IsChecked = TrackingService.ListenForServerEvents;
            ChkTrustOwnDetections.IsChecked = TrackingService.TrustOwnDetections;

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

            // Home Assistant token management runs on our own Supabase functions, not the
            // Laravel platform this panel originally required - so it loads unconditionally too.
            _ = LoadHomeAssistantSettingsAsync();
            _ = InitFeatureFlagsAsync();
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

        private void TxtBattleMetricsApiKey_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;
            TrackingService.BattleMetricsApiKey = TxtBattleMetricsApiKey.Password;
        }

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized) return;

            TrackingService.AutoStartEnabled = ChkAutoStart.IsChecked == true;
            TrackingService.StartMinimizedEnabled = ChkStartMinimized.IsChecked == true;
            TrackingService.AutoConnectEnabled = ChkAutoConnect.IsChecked == true;
            TrackingService.CloseToTrayEnabled = ChkCloseToTray.IsChecked == true;
            TrackingService.HideConsole = ChkHideConsole.IsChecked == true;
            TrackingService.TrafficMonitorEnabled = ChkTrafficMonitor.IsChecked == true;
            TrackingService.MapAbbreviateNames = ChkStreamerMode.IsChecked == true;

            if (CmbMapScalingMode != null && CmbMapScalingMode.SelectedIndex >= 0)
            {
                TrackingService.MapBitmapScalingMode = CmbMapScalingMode.SelectedIndex;
            }
            TrackingService.MapUseCacheMode = ChkMapUseCacheMode.IsChecked == true;
            if (CmbMapRenderScale != null && CmbMapRenderScale.SelectedIndex >= 0)
            {
                double val = 1.0;
                switch (CmbMapRenderScale.SelectedIndex)
                {
                    case 0: val = 0.5; break;
                    case 1: val = 0.75; break;
                    case 2: val = 1.0; break;
                    case 3: val = 1.25; break;
                    case 4: val = 1.5; break;
                    case 5: val = 2.0; break;
                }
                TrackingService.MapRenderScale = val;
            }
            TrackingService.MapUseAliasedEdgeMode = ChkMapUseAliasedEdgeMode.IsChecked == true;

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

            // NOTE: PlayerWipeTracker flags are intentionally NOT written here. They have a
            // dedicated handler (OnPlayerWipeTrackerToggled) so the global setting can only
            // change when the user clicks those toggles — never as a side effect of another
            // setting changing or a panel reload firing this bulk rewrite from stale state.

            TrackingService.ListenForServerEvents = ChkListenForServerEvents.IsChecked == true;
            TrackingService.TrustOwnDetections = ChkTrustOwnDetections.IsChecked == true;
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
            ParentWindow?.RefreshPlayerWipeTrackerSession();
        }

        // Dedicated handler for the Player Wipe Tracker toggles. Kept separate from the bulk
        // OnSettingChanged rewrite so these global flags only change when the user actually
        // clicks the toggles — never as a side effect of another setting or a panel reload.
        // This is why the tracker no longer switches itself off on wipe/server changes.
        //
        // Both flags appear twice — under General and under Connected Services — so whichever
        // copy was clicked is the one that carries the new value, and the other has to be brought
        // along. _syncingWipeToggles stops that write from coming straight back in as another
        // toggle event and overwriting what the user just chose.
        private bool _syncingWipeToggles;

        private void OnPlayerWipeTrackerToggled(object sender, RoutedEventArgs e)
        {
            if (!_isSettingsInitialized || _syncingWipeToggles) return;

            bool tracker = ReferenceEquals(sender, ChkPlayerWipeTrackerConnected)
                ? ChkPlayerWipeTrackerConnected.IsChecked == true
                : ReferenceEquals(sender, ChkPlayerWipeTracker)
                    ? ChkPlayerWipeTracker.IsChecked == true
                    : TrackingService.PlayerWipeTrackerEnabled;

            bool cloud = ReferenceEquals(sender, ChkPlayerWipeCloudConnected)
                ? ChkPlayerWipeCloudConnected.IsChecked == true
                : ReferenceEquals(sender, ChkPlayerWipeCloud)
                    ? ChkPlayerWipeCloud.IsChecked == true
                    : TrackingService.PlayerWipeTrackerCloudBackupEnabled;

            TrackingService.PlayerWipeTrackerEnabled = tracker;
            TrackingService.PlayerWipeTrackerCloudBackupEnabled = cloud;

            SyncPlayerWipeTrackerToggles();
            ParentWindow?.RefreshPlayerWipeTrackerSession();
        }

        /// <summary>Points all four switches at what the setting now says.</summary>
        private void SyncPlayerWipeTrackerToggles()
        {
            _syncingWipeToggles = true;
            try
            {
                ChkPlayerWipeTracker.IsChecked = TrackingService.PlayerWipeTrackerEnabled;
                ChkPlayerWipeCloud.IsChecked = TrackingService.PlayerWipeTrackerCloudBackupEnabled;
                ChkPlayerWipeTrackerConnected.IsChecked = TrackingService.PlayerWipeTrackerEnabled;
                ChkPlayerWipeCloudConnected.IsChecked = TrackingService.PlayerWipeTrackerCloudBackupEnabled;
            }
            finally { _syncingWipeToggles = false; }
        }

        private void BtnCloseSettings_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            ParentWindow?.ApplySettings();
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
            var result = MessageBox.Show(
                T("CodeUiDeleteAllCached3DMapDataConfirm", "Delete all cached 3D map data for every server? This removes parsed map files and generated viewer JSON, but keeps app assets and icons."),
                T("CodeUiDelete3DMapDataTitle", "Delete 3D Map Data"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            var deleted = Map3DLocalBuildService.DeleteAllCachedMapData();
            ParentWindow?.ResetBuildingBlockedZonesAfterCacheDelete();
            ParentWindow?.AppendLog($"[3D Map] Deleted cached 3D map data ({deleted.DeletedFiles} files, {deleted.DeletedDirectories} folders). Generated data will be rebuilt when needed.");
            ParentWindow?.ShowInfoSnackbar(RustPlusDesk.Properties.Resources.GetString("CodeUi3DMapData"), RustPlusDesk.Properties.Resources.GetString("CodeUiCached3DMapDataDeletedItWillBeRebuiltWhenYouOpenA3DMapAgain"), WpfUi.ControlAppearance.Success);
        }

        private void BtnManuallyParseMap_Click(object sender, RoutedEventArgs e)
        {
            ParentWindow?.ManuallyImportMapFile();
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
                        ParentWindow?.ShowInfoSnackbar(Properties.Resources.BackupSuccessTitle, Properties.Resources.BackupSuccessMessage, WpfUi.ControlAppearance.Success);
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
                    ParentWindow?.ShowInfoSnackbar(Properties.Resources.RestoreSuccessTitle, Properties.Resources.RestoreSuccessMessage, WpfUi.ControlAppearance.Success);
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
                        case "shop":         tts = ChkShopTTS.IsChecked == true;      audio = ChkShopAudio.IsChecked == true;      break;
                        case "trackers":     tts = ChkTrackersTTS.IsChecked == true;  audio = ChkTrackersAudio.IsChecked == true;  break;
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
            if (!Services.Cloud.CloudAuth.IsCloudAvailable || !Services.Auth.SupabaseAuthManager.IsPremium) return;

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
                        TxtAuthStatus.Text = T("CodeUiConnected", "Connected");
                        TxtAuthStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
                        DotBotStatus.Fill = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2E, 0xCC, 0x71));
                    }
                    else
                    {
                        TxtAuthStatus.Text = T("CodeUiDiscordNotConnectedStatus", "Not connected - Please invite Discord Bot to your server and do /setup <steamid64>");
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
                    TxtChannelShop.Text     = FormatChannelId(guildChannels?.ShopId);
                    TxtChannelTrackers.Text = FormatChannelId(guildChannels?.TrackersId);

                    // Reset all toggles
                    ChkRaidTTS.IsChecked = ChkRaidAudio.IsChecked = false;
                    ChkEventsTTS.IsChecked = ChkEventsAudio.IsChecked = false;
                    ChkChatTTS.IsChecked = ChkChatAudio.IsChecked = false;
                    ChkShopTTS.IsChecked = ChkShopAudio.IsChecked = false;
                    ChkTrackersTTS.IsChecked = ChkTrackersAudio.IsChecked = false;

                    foreach (var ch in channelsList)
                    {
                        switch (ch.NotificationType)
                        {
                            case "raid":            ChkRaidTTS.IsChecked = ch.TtsEnabled;      ChkRaidAudio.IsChecked = ch.AudioAlertEnabled;      break;
                            case "events":          ChkEventsTTS.IsChecked = ch.TtsEnabled;    ChkEventsAudio.IsChecked = ch.AudioAlertEnabled;    break;
                            case "teamchat":
                            case "chat":            ChkChatTTS.IsChecked = ch.TtsEnabled;      ChkChatAudio.IsChecked = ch.AudioAlertEnabled;      break;
                            case "shop":            ChkShopTTS.IsChecked = ch.TtsEnabled;      ChkShopAudio.IsChecked = ch.AudioAlertEnabled;      break;
                            case "trackers":        ChkTrackersTTS.IsChecked = ch.TtsEnabled;  ChkTrackersAudio.IsChecked = ch.AudioAlertEnabled;  break;
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
                Title = T("SelectCustomDeathSound", "Select Custom Death Sound")
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
                    Text = Properties.Resources.NoServersMuted,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    FontSize = 11,
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (var serverKey in muted)
            {
                var savedProfile = (ParentWindow?.DataContext as RustPlusDesk.ViewModels.MainViewModel)?.Servers
                    .FirstOrDefault(server => $"{server.Host}:{server.Port}" == serverKey);
                var serverName = TrackingService.GetMutedServerName(serverKey) ?? savedProfile?.Name;

                var grid = new Grid { Margin = new Thickness(4, 4, 4, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var serverDetails = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 12, 0)
                };
                var nameText = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(serverName) ? serverKey : serverName,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                nameText.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                serverDetails.Children.Add(nameText);

                if (!string.IsNullOrWhiteSpace(serverName))
                {
                    var endpointText = new TextBlock
                    {
                        Text = serverKey,
                        FontSize = 10,
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    };
                    endpointText.SetResourceReference(TextBlock.ForegroundProperty, "TextSubtle");
                    serverDetails.Children.Add(endpointText);
                }

                Grid.SetColumn(serverDetails, 0);
                grid.Children.Add(serverDetails);

                var btn = new WpfUi.Button
                {
                    Content = Properties.Resources.UnmuteServer,
                    Icon = new WpfUi.SymbolIcon { Symbol = WpfUi.SymbolRegular.AlertOn24 },
                    Appearance = WpfUi.ControlAppearance.Secondary,
                    Height = 30,
                    Padding = new Thickness(10, 4, 10, 4),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = serverKey,
                };
                btn.Click += (s, e) =>
                {
                    if (s is WpfUi.Button { Tag: string key })
                    {
                        TrackingService.UnmuteServer(key);
                        PopulateMutedServers();
                    }
                };
                Grid.SetColumn(btn, 1);
                grid.Children.Add(btn);

                PnlMutedServers.Children.Add(grid);
            }
        }

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

        // --- Home Assistant integration ---

        /// <summary>Public base URL of the Supabase edge function that serves the switch endpoints.</summary>
        private const string HaWorkerBaseUrl = "https://iposrabchlvpfpdymgmj.supabase.co/functions/v1/home-assistant";

        /// <summary>
        /// Supabase's gateway requires this on every request regardless of the function's own
        /// auth, and it identifies the project rather than the user — safe to ship in a
        /// configuration snippet the same way the desktop client itself carries it.
        /// </summary>
        private const string HaApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imlwb3NyYWJjaGx2cGZwZHltZ21qIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODIyNDc0MzcsImV4cCI6MjA5NzgyMzQzN30.XbumfrRPCooBh_f1ZxpbpNB1NhSNWMxHGzuO-rpEarA";

        private async Task LoadHomeAssistantSettingsAsync()
        {
            try
            {
                var token = await Services.Cloud.CloudHomeAssistantAdapter.GetTokenAsync();
                ApplyHaToken(token);
            }
            catch
            {
                // No token yet, or the endpoint is unreachable — leave the panel in its empty state.
                ApplyHaToken(null);
            }
        }

        /// <summary>Reflect a token (or its absence) into the token box, copy button, and snippet.</summary>
        private void ApplyHaToken(string? token)
        {
            var hasToken = !string.IsNullOrEmpty(token);

            TxtHaToken.Text = hasToken ? token : string.Empty;
            BtnCopyHaToken.IsEnabled = hasToken;
            BtnGenerateHaToken.Content = hasToken ? "Regenerate Token" : "Generate Token";
            TxtHaSnippet.Text = BuildHaSnippet(token);
        }

        private static string BuildHaSnippet(string? token)
        {
            var bearer = string.IsNullOrEmpty(token) ? "<YOUR_TOKEN>" : token;

            return
                "# Add to Home Assistant configuration.yaml\n" +
                "switch:\n" +
                "  - platform: rest\n" +
                "    name: Rust Smart Switch\n" +
                $"    resource: {HaWorkerBaseUrl}/switch/ENTITYID\n" +
                "    headers:\n" +
                $"      Authorization: \"Bearer {bearer}\"\n" +
                $"      apikey: \"{HaApiKey}\"\n" +
                "    body_on: '{\"on\": true}'\n" +
                "    body_off: '{\"on\": false}'\n" +
                "    is_on_template: \"{{ value_json.on }}\"";
        }

        private async void BtnGenerateHaToken_Click(object sender, RoutedEventArgs e)
        {
            if (!Services.Cloud.CloudAuth.IsAuthenticated)
            {
                MessageBox.Show(RustPlusDesk.Properties.Resources.GetString("CodeUiPleaseConnectYourCloudAccountFirst"), RustPlusDesk.Properties.Resources.GetString("ErrorPrefix"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnGenerateHaToken.IsEnabled = false;
            try
            {
                var token = await Services.Cloud.CloudHomeAssistantAdapter.RegenerateTokenAsync();
                ApplyHaToken(token);
                ParentWindow?.ShowInfoSnackbar("Success", "Home Assistant token generated. Copy it into your configuration.yaml.", WpfUi.ControlAppearance.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate Home Assistant token: {ex.Message}", Properties.Resources.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnGenerateHaToken.IsEnabled = true;
            }
        }

        private void BtnCopyHaToken_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtHaToken.Text)) return;
            try
            {
                Clipboard.SetText(TxtHaToken.Text);
                ParentWindow?.ShowInfoSnackbar("Copied", "Token copied to clipboard.", WpfUi.ControlAppearance.Success);
            }
            catch { /* clipboard can transiently fail; nothing actionable */ }
        }

        private void BtnCopyHaSnippet_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TxtHaSnippet.Text)) return;
            try
            {
                Clipboard.SetText(TxtHaSnippet.Text);
                ParentWindow?.ShowInfoSnackbar("Copied", "Configuration snippet copied to clipboard.", WpfUi.ControlAppearance.Success);
            }
            catch { /* clipboard can transiently fail; nothing actionable */ }
        }

        private async void BtnRevokeHa_Click(object sender, RoutedEventArgs e)
        {
            if (!Services.Cloud.CloudAuth.IsAuthenticated) return;

            BtnRevokeHa.IsEnabled = false;
            try
            {
                await Services.Cloud.CloudHomeAssistantAdapter.RevokeAsync();
                ApplyHaToken(null);
                ParentWindow?.ShowInfoSnackbar("Success", "Home Assistant token revoked.", WpfUi.ControlAppearance.Success);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to revoke Home Assistant token: {ex.Message}", Properties.Resources.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnRevokeHa.IsEnabled = true;
            }
        }

        private async void BtnHomeAssistantHelp_Click(object sender, RoutedEventArgs e)
        {
            var msg = "How to use the Home Assistant integration:\n\n" +
                      "1. Click 'Generate Token' to create your API token.\n" +
                      "2. Copy the token (or the whole example snippet).\n" +
                      "3. In Home Assistant, open configuration.yaml and add a REST switch per device.\n" +
                      "4. Replace ENTITYID with the numeric Entity ID shown next to the switch in your device list.\n" +
                      "5. Restart Home Assistant. The switch appears and can be toggled while the desktop app is connected to the server that switch belongs to; its state is read back from what the app last observed.\n\n" +
                      "Keep your token secret — anyone with it can control your linked switches. Use 'Revoke Token' to invalidate it.";

            var box = new WpfUi.MessageBox
            {
                Title = "Home Assistant Setup",
                Content = msg,
                PrimaryButtonText = Properties.Resources.OK,
            };
            await box.ShowDialogAsync();
        }

        // --- Global feature flags (admin on/off + status note) ---

        private async Task InitFeatureFlagsAsync()
        {
            await Services.Cloud.CloudFeatureFlags.RefreshAsync();
            ApplyFeatureFlags();
        }

        /// <summary>
        /// Reflect the admin feature flags into each integration panel: show the
        /// status note (or a default "disabled" message) and enable/disable the
        /// panel's controls when a feature is turned off globally.
        /// </summary>
        private void ApplyFeatureFlags()
        {
            // Note: the HA copy buttons are intentionally excluded — their enabled
            // state is owned by token-presence logic (ApplyHaToken), and copying an
            // existing token/snippet is harmless even when the feature is off.
            ApplyFeatureFlag("home_assistant", TxtHaStatusNote,
                BtnGenerateHaToken, BtnRevokeHa);
        }

        private static void ApplyFeatureFlag(string key, System.Windows.Controls.TextBlock note, params System.Windows.UIElement[] controls)
        {
            var enabled = Services.Cloud.CloudFeatureFlags.IsEnabled(key);
            var statusNote = Services.Cloud.CloudFeatureFlags.Note(key);

            // The banner is shown only when the feature is disabled — it uses the
            // admin status note if one is set, otherwise a default message.
            if (!enabled)
            {
                note.Text = string.IsNullOrWhiteSpace(statusNote)
                    ? "This integration is currently disabled by the administrator."
                    : statusNote;
                note.Visibility = Visibility.Visible;
            }
            else
            {
                note.Visibility = Visibility.Collapsed;
            }

            // Disable interaction and dim the controls — WPF-UI's default disabled
            // styling is too subtle on the dark theme to read as "off", so the
            // explicit opacity makes the disabled state obvious.
            foreach (var control in controls)
            {
                control.IsEnabled = enabled;
                control.Opacity = enabled ? 1.0 : 0.4;
            }
        }

        private void TxtCustomMapUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
        }

        private void BtnApplyCustomMapUrl_Click(object sender, RoutedEventArgs e)
        {
            var selectedProf = ParentWindow?.ViewModel?.Selected;
            if (selectedProf == null)
            {
                MessageBox.Show(ParentWindow, T("CodeUiNoActiveServerProfileSelected", "No active server profile selected."), T("CodeUiCustomMapUrlTitle", "Custom Map URL"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string url = TxtCustomMapUrl.Text.Trim();
            if (!string.IsNullOrEmpty(url) && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            {
                MessageBox.Show(ParentWindow, T("CodeUiPleaseEnterValidAbsoluteUrl", "Please enter a valid absolute HTTP or HTTPS URL (e.g. https://example.com/rust_map.png)."), T("CodeUiInvalidUrlTitle", "Invalid URL"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            selectedProf.CustomMapUrl = string.IsNullOrWhiteSpace(url) ? null : url;
            ParentWindow?.ViewModel?.Save();

            if (!string.IsNullOrWhiteSpace(selectedProf.CustomMapUrl))
            {
                string host = selectedProf.Host;
                int port = selectedProf.Port;
                foreach (var c in System.IO.Path.GetInvalidFileNameChars()) host = host.Replace(c, '_');
                string key = $"{host}_{port}";
                MainWindow.DeleteCustomMapCache(key);

                TxtCustomMapStatus.Text = T("CodeUiCustomMapUrlUpdatedReloading", "Custom HD Map URL updated. Reloading map image...");
            }
            else
            {
                TxtCustomMapStatus.Text = T("CodeUiCustomMapUrlClearedReverting", "Custom HD Map URL cleared. Reverting to standard server map...");
            }

            _ = ParentWindow?.ReloadMapAsync();
        }

        private void BtnClearCustomMapUrl_Click(object sender, RoutedEventArgs e)
        {
            TxtCustomMapUrl.Text = "";
            BtnApplyCustomMapUrl_Click(sender, e);
        }

        private void BtnOpenCrashLogs_Click(object sender, RoutedEventArgs e)
        {
            CrashReporter.OpenCrashLogsFolder();
        }
    }
}
