using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using RustPlusDesk.Models;
using RustPlusDesk.Services.Auth;

namespace RustPlusDesk.Views.Windows
{
    public partial class AdminPanelWindow : Window
    {
        private ObservableCollection<AdminUserViewModel> _users = new ObservableCollection<AdminUserViewModel>();

        public AdminPanelWindow()
        {
            InitializeComponent();
            UsersGrid.ItemsSource = _users;
            Loaded += AdminPanelWindow_Loaded;
        }

        private async void AdminPanelWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var (isAdmin, error) = await SupabaseAuthManager.CheckIsAdminDetailedAsync();
            if (!isAdmin)
            {
                string msg = Properties.Resources.ResourceManager.GetString("CodeUiAdminAccessRequiresDiscordAuthAndADeveloperLeadContributorRole") ?? "Admin access requires Discord auth and a developer/lead contributor role.";
                if (!string.IsNullOrEmpty(error))
                {
                    string detailsLabel = Properties.Resources.ResourceManager.GetString("CodeUiDetailsLabel") ?? "\n\nDetails: ";
                    msg += $"{detailsLabel}{error}";
                }
                string title = Properties.Resources.ResourceManager.GetString("UiAdminPanel") ?? "Admin Panel";
                MessageBox.Show(msg, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            await LoadUsersAsync();
        }

        private async Task LoadUsersAsync()
        {
            try
            {
                if (SupabaseAuthManager.Client == null) return;
                
                var body = await SupabaseAuthManager.CallEdgeFunctionAsync("admin/users", System.Net.Http.HttpMethod.Get);
                var profiles = JsonSerializer.Deserialize<List<UserProfileModel>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                _users.Clear();
                if (profiles != null)
                {
                    foreach (var p in profiles)
                    {
                        var vm = new AdminUserViewModel
                        {
                            SteamId = p.SteamId,
                            DiscordName = p.DiscordName,
                            SubscriptionTier = p.SubscriptionTier,
                            SyncAccepted = p.SyncAccepted,
                            LastActiveAt = p.LastActiveAt,
                            DatabaseIsOnline = p.IsOnline,
                            CurrentServerName = p.CurrentServerName,
                            CurrentServerKey = p.CurrentServerKey,
                            TeamMemberCount = p.TeamMemberCount,
                            TeamMembersJson = p.TeamMembersJson,
                            IsManualSupporter = p.IsManualSupporter,
                            ManualPremiumAt = p.ManualPremiumAt
                        };

                        // Subscribe to changes for Manual Override
                        vm.PropertyChanged += Vm_PropertyChanged;
                        _users.Add(vm);
                    }
                }
            }
            catch (Exception ex)
            {
                string errorLabel = Properties.Resources.ResourceManager.GetString("CodeUiErrorLoadingUsersLabel") ?? "Error loading users: ";
                MessageBox.Show($"{errorLabel}{ex.Message}");
            }
        }

        private async void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AdminUserViewModel.IsManualSupporter) && sender is AdminUserViewModel vm)
            {
                try
                {
                    var payload = new
                    {
                        steam_id = vm.SteamId,
                        is_manual_supporter = vm.IsManualSupporter
                    };

                    await SupabaseAuthManager.CallEdgeFunctionAsync("admin/set-supporter", System.Net.Http.HttpMethod.Post, payload);
                    vm.ManualPremiumAt = vm.IsManualSupporter ? DateTime.UtcNow : null;
                }
                catch (Exception ex)
                {
                    string errorLabel = Properties.Resources.ResourceManager.GetString("CodeUiErrorUpdatingOverrideLabel") ?? "Error updating override: ";
                    MessageBox.Show($"{errorLabel}{ex.Message}");
                }
            }
        }

        private static bool IsCurrentUserAdmin()
        {
            var tier = SupabaseAuthManager.CurrentTier;
            return SupabaseAuthManager.IsDiscordAuthenticated &&
                   (tier == "developer" || tier == "lead_contributor" || tier == "lead_developer");
        }

        private void UsersGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (UsersGrid.SelectedItem is not AdminUserViewModel vm) return;

            var team = vm.TeamMembers;
            string noTeamSnapshot = Properties.Resources.ResourceManager.GetString("CodeUiNoTeamSnapshotAvailable") ?? "No team snapshot available.";
            string leaderLabel = Properties.Resources.ResourceManager.GetString("CodeUiLeaderLabel") ?? "[Leader] ";
            string onlineWord = Properties.Resources.ResourceManager.GetString("OnlineTab") ?? "Online";
            string offlineWord = Properties.Resources.ResourceManager.GetString("CodeUiOfflineWord") ?? "Offline";
            string deadSuffix = Properties.Resources.ResourceManager.GetString("CodeUiDeadSuffix") ?? ", Dead";
            var teamText = team.Count == 0
                ? noTeamSnapshot
                : string.Join(Environment.NewLine, team.ConvertAll(t =>
                    $"- {t.Name} ({t.SteamId}) {(t.IsLeader ? leaderLabel : "")}{(t.IsOnline ? onlineWord : offlineWord)}{(t.IsDead ? deadSuffix : "")}"));

            string steamIdLabel = Properties.Resources.ResourceManager.GetString("CodeUiSteamIdLabel") ?? "Steam ID: ";
            string discordLabel = Properties.Resources.ResourceManager.GetString("CodeUiDiscordLabel") ?? "Discord: ";
            string tierLabel = Properties.Resources.ResourceManager.GetString("CodeUiTierLabel") ?? "Tier: ";
            string lastActiveLabel = Properties.Resources.ResourceManager.GetString("CodeUiLastActiveLabel") ?? "Last Active: ";
            string onlineLabel = Properties.Resources.ResourceManager.GetString("CodeUiOnlineLabel") ?? "Online: ";
            string serverLabel = Properties.Resources.ResourceManager.GetString("CodeUiServerLabel") ?? "Server: ";
            string serverKeyLabel = Properties.Resources.ResourceManager.GetString("CodeUiServerKeyLabel") ?? "Server Key: ";
            string teamSizeLabel = Properties.Resources.ResourceManager.GetString("CodeUiTeamSizeLabel") ?? "Team Size: ";
            string userActivityTitle = Properties.Resources.ResourceManager.GetString("CodeUiUserActivityTitle") ?? "User Activity";

            MessageBox.Show(
                $"{steamIdLabel}{vm.SteamId}\n" +
                $"{discordLabel}{vm.DiscordName}\n" +
                $"{tierLabel}{vm.SubscriptionTier}\n" +
                $"{lastActiveLabel}{vm.LastActiveDisplay}\n" +
                $"{onlineLabel}{vm.IsOnline}\n" +
                $"{serverLabel}{vm.CurrentServerName}\n" +
                $"{serverKeyLabel}{vm.CurrentServerKey}\n" +
                $"{teamSizeLabel}{vm.TeamMemberCount}\n\n" +
                teamText,
                userActivityTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    public class AdminUserViewModel : INotifyPropertyChanged
    {
        public string SteamId { get; set; } = string.Empty;
        public string DiscordName { get; set; } = string.Empty;
        public string SubscriptionTier { get; set; } = string.Empty;
        public bool SyncAccepted { get; set; }
        public DateTime LastActiveAt { get; set; }
        public string LastActiveDisplay => LastActiveAt == default
            ? "-"
            : LastActiveAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

        public bool DatabaseIsOnline { get; set; }
        public bool IsOnline => DatabaseIsOnline && (DateTime.UtcNow - LastActiveAt.ToUniversalTime()).TotalMinutes <= 5;

        public string CurrentServerName { get; set; } = string.Empty;
        public string CurrentServerKey { get; set; } = string.Empty;
        public int TeamMemberCount { get; set; }
        public string TeamMembersJson { get; set; } = string.Empty;
        public DateTime? ManualPremiumAt { get; set; }
        public string ManualPremiumAtDisplay => ManualPremiumAt.HasValue
            ? ManualPremiumAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";

        public List<AdminTeamMemberViewModel> TeamMembers
        {
            get
            {
                if (string.IsNullOrWhiteSpace(TeamMembersJson)) return new List<AdminTeamMemberViewModel>();
                try
                {
                    return JsonSerializer.Deserialize<List<AdminTeamMemberViewModel>>(TeamMembersJson) ?? new List<AdminTeamMemberViewModel>();
                }
                catch
                {
                    return new List<AdminTeamMemberViewModel>();
                }
            }
        }

        private bool _isManualSupporter;
        public bool IsManualSupporter
        {
            get => _isManualSupporter;
            set
            {
                if (_isManualSupporter != value)
                {
                    _isManualSupporter = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ManualPremiumAtDisplay));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class AdminTeamMemberViewModel
    {
        public string SteamId { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsOnline { get; set; }
        public bool IsDead { get; set; }
        public bool IsLeader { get; set; }
    }
}
