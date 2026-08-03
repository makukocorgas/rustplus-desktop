using System;
<<<<<<< Updated upstream
using System.Collections.ObjectModel;
using System.ComponentModel;
=======
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
>>>>>>> Stashed changes
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
<<<<<<< Updated upstream
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    public ObservableCollection<ClanMemberVM> ClanMembers { get; } = new();

    private bool _clanLoadInFlight;
    private DateTime? _clanLastPullUtc;
    private RustPlusApi.Data.Clans.ClanInfo? _lastClanInfo;
    private bool _clanChangedHooked;
=======
using RustPlusDesk.Models;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views
{
    public partial class MainWindow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // --- Clan Info Properties ---
        private string _clanName = "";
        public string ClanName
        {
            get => _clanName;
            set { if (_clanName == value) return; _clanName = value; OnPropertyChanged(nameof(ClanName)); }
        }

        private string _clanMotd = "";
        public string ClanMotd
        {
            get => _clanMotd;
            set { if (_clanMotd == value) return; _clanMotd = value; OnPropertyChanged(nameof(ClanMotd)); }
        }

        private string _clanCreatedText = "";
        public string ClanCreatedText
        {
            get => _clanCreatedText;
            set { if (_clanCreatedText == value) return; _clanCreatedText = value; OnPropertyChanged(nameof(ClanCreatedText)); }
        }

        private string _clanCreatorText = "";
        public string ClanCreatorText
        {
            get => _clanCreatorText;
            set { if (_clanCreatorText == value) return; _clanCreatorText = value; OnPropertyChanged(nameof(ClanCreatorText)); }
        }

        private string _clanMotdAuthorText = "";
        public string ClanMotdAuthorText
        {
            get => _clanMotdAuthorText;
            set { if (_clanMotdAuthorText == value) return; _clanMotdAuthorText = value; OnPropertyChanged(nameof(ClanMotdAuthorText)); }
        }

        private int _clanMaxMemberCount;
        public int ClanMaxMemberCount
        {
            get => _clanMaxMemberCount;
            set { if (_clanMaxMemberCount == value) return; _clanMaxMemberCount = value; OnPropertyChanged(nameof(ClanMaxMemberCount)); }
        }

        private int _clanMemberCount;
        public int ClanMemberCount
        {
            get => _clanMemberCount;
            set { if (_clanMemberCount == value) return; _clanMemberCount = value; OnPropertyChanged(nameof(ClanMemberCount)); }
        }

        private string _clanMembersRatio = "";
        public string ClanMembersRatio
        {
            get => _clanMembersRatio;
            set { if (_clanMembersRatio == value) return; _clanMembersRatio = value; OnPropertyChanged(nameof(ClanMembersRatio)); }
        }

        private string _clanScoreText = "";
        public string ClanScoreText
        {
            get => _clanScoreText;
            set { if (_clanScoreText == value) return; _clanScoreText = value; OnPropertyChanged(nameof(ClanScoreText)); }
        }

        private string _lastClanPullTime = "";
        public string LastClanPullTime
        {
            get => _lastClanPullTime;
            set { if (_lastClanPullTime == value) return; _lastClanPullTime = value; OnPropertyChanged(nameof(LastClanPullTime)); }
        }

        private bool _hasClanInfo;
        public bool HasClanInfo
        {
            get => _hasClanInfo;
            set { if (_hasClanInfo == value) return; _hasClanInfo = value; OnPropertyChanged(nameof(HasClanInfo)); }
        }

        private bool _isClanListView;
        public bool IsClanListView
        {
            get => _isClanListView;
            set { if (_isClanListView == value) return; _isClanListView = value; OnPropertyChanged(nameof(IsClanListView)); }
        }

        public ObservableCollection<ClanMemberVM> ClanMembers { get; } = new();

        private DateTime _lastClanPoll = DateTime.MinValue;

        private async Task<string> GetSteamNameAsync(ulong steamId)
        {
            if (steamId == 0) return "";
            if (_steamNames.TryGetValue(steamId, out var name)) return name;

            try
            {
                using var http = new HttpClient();
                var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");
                var mName = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.IgnoreCase);
                if (mName.Success)
                {
                    var fetchedName = mName.Groups[1].Value;
                    _steamNames[steamId] = fetchedName;
                    return fetchedName;
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-steam-name] {steamId}: {ex.Message}");
            }
            return steamId.ToString();
        }

        public async Task LoadClanAsync()
        {
            if (_real is null) return;

            try
            {
                var clan = await _real.GetClanInfoAsync();
                if (clan is null) return;

                ClanName = clan.Name;
                ClanMotd = clan.Motd;
                ClanCreatedText = clan.Created != default ? clan.Created.ToString("dd/MM/yyyy") : "-";
                ClanMaxMemberCount = clan.MaxMemberCount ?? 100;
                ClanMemberCount = clan.Members.Count;
                ClanMembersRatio = $"{ClanMemberCount} / {ClanMaxMemberCount}";
                ClanScoreText = clan.Score?.ToString() ?? "-";
                LastClanPullTime = DateTime.Now.ToString("HH:mm:ss");
                HasClanInfo = true;

                // Load Creator / Founder Name
                _ = Task.Run(async () =>
                {
                    var founder = await GetSteamNameAsync(clan.Creator);
                    App.Current.Dispatcher.Invoke(() => ClanCreatorText = founder);
                });

                // Load MOTD Author Name
                if (clan.MotdAuthor.HasValue && clan.MotdAuthor.Value != 0)
                {
                    _ = Task.Run(async () =>
                    {
                        var author = await GetSteamNameAsync(clan.MotdAuthor.Value);
                        var dateStr = clan.MotdTimestamp?.ToString("dd/MM/yyyy") ?? "";
                        App.Current.Dispatcher.Invoke(() => ClanMotdAuthorText = $"Set by {author} on {dateStr}");
                    });
                }
                else
                {
                    ClanMotdAuthorText = "";
                }

                // Sync: remove members that are no longer in the clan
                var currentClanIds = clan.Members.Select(m => m.SteamId).ToHashSet();
                for (int i = ClanMembers.Count - 1; i >= 0; i--)
                {
                    if (!currentClanIds.Contains(ClanMembers[i].SteamId))
                    {
                        ClanMembers.RemoveAt(i);
                    }
                }

                var avatarTasks = new List<Task>();

                foreach (var m in clan.Members)
                {
                    var sid = m.SteamId;
                    if (sid == 0) continue;

                    var vm = ClanMembers.FirstOrDefault(c => c.SteamId == sid);
                    if (vm == null)
                    {
                        vm = new ClanMemberVM { SteamId = sid };
                        ClanMembers.Add(vm);
                    }

                    vm.RoleId = m.RoleId;
                    vm.RoleName = m.RoleName;
                    vm.Rank = m.Rank;
                    vm.Joined = m.Joined;
                    vm.LastSeen = m.LastSeen;
                    vm.Notes = m.Notes;
                    vm.IsOnline = m.IsOnline;
                    vm.IsInTeam = TeamMembers.Any(tm => tm.SteamId == sid);

                    // Fetch avatar and SteamID name in the background
                    if (vm.Avatar == null || vm.Name == "(player)")
                    {
                        avatarTasks.Add(LoadClanMemberProfileAsync(vm));
                    }
                }

                if (avatarTasks.Count > 0)
                {
                    _ = Task.WhenAll(avatarTasks);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-load] Error: {ex.Message}");
            }
        }

        private async Task LoadClanMemberProfileAsync(ClanMemberVM vm)
        {
            try
            {
                if (vm.SteamId == 0) return;

                // 1. Try Cache First
                if (_avatarCache.TryGetValue(vm.SteamId, out var cachedImg) && cachedImg != null)
                {
                    vm.Avatar = cachedImg;
                    if (_steamNames.TryGetValue(vm.SteamId, out var cachedName))
                    {
                        vm.Name = cachedName;
                        return;
                    }
                }

                // 2. Fetch Steam profile details (XML contains both steamID/nickname and avatar link)
                using var http = new HttpClient();
                var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{vm.SteamId}?xml=1");

                // Parse Steam ID Name (Nickname)
                var mName = Regex.Match(xml, @"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.IgnoreCase);
                if (mName.Success)
                {
                    var name = mName.Groups[1].Value;
                    _steamNames[vm.SteamId] = name;
                    vm.Name = name;
                }

                // Parse Avatar
                string url = "";
                var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
                var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);
                if (mFull.Success) url = mFull.Groups[1].Value;
                else if (mMedium.Success) url = mMedium.Groups[1].Value;

                if (!string.IsNullOrWhiteSpace(url))
                {
                    var bytes = await http.GetByteArrayAsync(url);
                    var img = BytesToImage(bytes);
                    if (img != null)
                    {
                        _avatarCache[vm.SteamId] = img;
                        vm.Avatar = img;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[clan-avatar] {vm.SteamId}: {ex.Message}");
            }
        }
        private void Clan_OpenProfile_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if ((sender as System.Windows.FrameworkElement)?.DataContext is ClanMemberVM vm)
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = $"https://steamcommunity.com/profiles/{vm.SteamId}",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void Clan_CopySteamId_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if ((sender as System.Windows.FrameworkElement)?.DataContext is ClanMemberVM vm)
            {
                try
                {
                    System.Windows.Clipboard.SetText(vm.SteamId.ToString());
                }
                catch { }
            }
        }

        private async void BtnRefreshClan_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await LoadClanAsync();
        }

        private void BtnToggleClanView_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            IsClanListView = !IsClanListView;
        }

        private async void BtnOpenClanChat_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            TabClanChat.IsChecked = true;
            await OpenChatOverlayAsync();
        }

        private void UpdateClanMembersTeamStatus()
        {
            var teamIds = TeamMembers.Select(tm => tm.SteamId).ToHashSet();
            foreach (var vm in ClanMembers)
            {
                vm.IsInTeam = teamIds.Contains(vm.SteamId);
            }
        }
    }
>>>>>>> Stashed changes

    public sealed class ClanMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ulong SteamId { get; init; }

<<<<<<< Updated upstream
        private string _name = "";
=======
        private bool _isInTeam;
        public bool IsInTeam
        {
            get => _isInTeam;
            set { if (_isInTeam == value) return; _isInTeam = value; OnChanged(nameof(IsInTeam)); }
        }

        private string _name = "(player)";
>>>>>>> Stashed changes
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
        }

<<<<<<< Updated upstream
=======
        private string _roleName = "";
        public string RoleName
        {
            get => _roleName;
            set { if (_roleName == value) return; _roleName = value; OnChanged(nameof(RoleName)); }
        }

        private int _roleId;
        public int RoleId
        {
            get => _roleId;
            set { if (_roleId == value) return; _roleId = value; OnChanged(nameof(RoleId)); }
        }

        private int _rank;
        public int Rank
        {
            get => _rank;
            set { if (_rank == value) return; _rank = value; OnChanged(nameof(Rank)); }
        }

        private bool _isOnline;
        public bool IsOnline
        {
            get => _isOnline;
            set { if (_isOnline == value) return; _isOnline = value; OnChanged(nameof(IsOnline)); }
        }

        private DateTime _joined;
        public DateTime Joined
        {
            get => _joined;
            set { if (_joined == value) return; _joined = value; OnChanged(nameof(Joined)); }
        }

        private DateTime _lastSeen;
        public DateTime LastSeen
        {
            get => _lastSeen;
            set 
            { 
                if (_lastSeen == value) return; 
                _lastSeen = value; 
                OnChanged(nameof(LastSeen)); 
                OnChanged(nameof(LastSeenText)); 
            }
        }

        public string LastSeenText => (LastSeen == default || LastSeen == DateTime.MinValue) ? "Never" : LastSeen.ToString("g");

        private string _notes = "";
        public string Notes
        {
            get => _notes;
            set 
            { 
                if (_notes == value) return; 
                _notes = value; 
                OnChanged(nameof(Notes)); 
                OnChanged(nameof(HasNotes)); 
            }
        }

        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);

>>>>>>> Stashed changes
        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }
<<<<<<< Updated upstream

        public bool IsOnline { get; init; }
        public string RoleName { get; init; } = "";
        public string? Notes { get; init; }
        public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
        public DateTime Joined { get; init; }
        public DateTime LastSeen { get; init; }
    }

    /// <summary>
    /// Loads the caller's clan roster (members, resolved role names, notes). Unlike Team,
    /// Rust+ doesn't give clan members' display names or positions — only a SteamID — so
    /// names are resolved the same way avatars already are, via the public Steam profile
    /// XML endpoint (see FetchSteamAvatarAsync in MainWindow.Team.Core.cs).
    /// </summary>
    public async Task LoadClanAsync()
    {
        if (_rust is RustPlusClientReal realHook && !_clanChangedHooked)
        {
            _clanChangedHooked = true;
            realHook.ClanChanged -= Real_ClanChanged;
            realHook.ClanChanged += Real_ClanChanged;
        }

        if (_clanLoadInFlight) return;
        if (_rust is not RustPlusClientReal real) return;

        _clanLoadInFlight = true;
        try
        {
            var clan = await real.GetClanInfoAsync();
            _clanLastPullUtc = DateTime.UtcNow;
            UpdateClanLastPullText();
            await ApplyClanInfoAsync(clan);
        }
        catch (Exception ex)
        {
            AppendLog($"[clan] {ex.Message}");
        }
        finally
        {
            _clanLoadInFlight = false;
        }
    }

    /// <summary>
    /// Fires whenever Rust+ pushes a clan roster change (member joined/left, role changed,
    /// MOTD edited, …) — no polling needed, the tab just stays in sync while it's open.
    /// </summary>
    private void Real_ClanChanged(object? sender, RustPlusApi.Data.Clans.ClanInfo? clanInfo)
    {
        _ = ApplyClanInfoAsync(clanInfo);
    }

    private async Task ApplyClanInfoAsync(RustPlusApi.Data.Clans.ClanInfo? clan)
    {
        _lastClanInfo = clan;

        if (clan?.Members == null)
        {
            await Dispatcher.InvokeAsync(() => ClanMembers.Clear());
            return;
        }

        var roleNames = (clan.Roles ?? Enumerable.Empty<RustPlusApi.Data.Clans.ClanRole>())
            .ToDictionary(r => r.RoleId, r => r.Name);

        var members = clan.Members
            .OrderByDescending(m => m.Online == true)
            .ThenBy(m => roleNames.TryGetValue(m.RoleId, out var rn) ? rn : "")
            .Select(m => new ClanMemberVM
            {
                SteamId = m.SteamId,
                Name = m.SteamId.ToString(),
                IsOnline = m.Online == true,
                RoleName = roleNames.TryGetValue(m.RoleId, out var roleName) ? roleName : Properties.Resources.ClanRoleUnknown,
                Notes = m.Notes,
                Joined = m.Joined,
                LastSeen = m.LastSeen
            })
            .ToList();

        await Dispatcher.InvokeAsync(() =>
        {
            ClanMembers.Clear();
            foreach (var m in members) ClanMembers.Add(m);
        });

        foreach (var vm in members)
        {
            if (_avatarCache.TryGetValue(vm.SteamId, out var cachedAvatar) && cachedAvatar != null)
            {
                vm.Avatar = cachedAvatar;
            }
            _ = LoadClanMemberProfileAsync(vm);
        }
    }

    private void UpdateClanLastPullText()
    {
        if (TxtClanLastPull == null) return;
        var timeText = _clanLastPullUtc.HasValue
            ? _clanLastPullUtc.Value.ToLocalTime().ToString("HH:mm:ss")
            : "--:--";
        TxtClanLastPull.Text = string.Format(Properties.Resources.LastPull, timeText);
    }

    private async void BtnRefreshClan_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await LoadClanAsync();
    }

    private void BtnClanInfo_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (PopupClanInfo == null) return;

        var clan = _lastClanInfo;
        if (clan == null)
        {
            PopupClanInfo.IsOpen = !PopupClanInfo.IsOpen;
            return;
        }

        TxtClanInfoName.Text = clan.Name;
        TxtClanInfoScore.Text = clan.Score?.ToString("N0") ?? "-";
        TxtClanInfoMembers.Text = clan.MaxMemberCount.HasValue
            ? $"{ClanMembers.Count} / {clan.MaxMemberCount}"
            : ClanMembers.Count.ToString();
        TxtClanInfoCreated.Text = clan.Created.ToLocalTime().ToString("d");

        var creator = ClanMembers.FirstOrDefault(m => m.SteamId == clan.Creator);
        TxtClanInfoCreator.Text = creator?.Name ?? clan.Creator.ToString();

        bool hasMotd = !string.IsNullOrWhiteSpace(clan.Motd);
        TxtClanInfoMotd.Text = hasMotd ? clan.Motd : Properties.Resources.ClanNoMotd;
        if (hasMotd && clan.MotdAuthor.HasValue)
        {
            var author = ClanMembers.FirstOrDefault(m => m.SteamId == clan.MotdAuthor.Value);
            var authorName = author?.Name ?? clan.MotdAuthor.Value.ToString();
            var when = clan.MotdTimestamp.HasValue ? clan.MotdTimestamp.Value.ToLocalTime().ToString("d") : "";
            TxtClanInfoMotdMeta.Text = string.IsNullOrEmpty(when)
                ? string.Format(Properties.Resources.ClanMotdSetBy, authorName)
                : string.Format(Properties.Resources.ClanMotdSetByOn, authorName, when);
            TxtClanInfoMotdMeta.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            TxtClanInfoMotdMeta.Visibility = System.Windows.Visibility.Collapsed;
        }

        PopupClanInfo.IsOpen = !PopupClanInfo.IsOpen;
    }

    private async Task LoadClanMemberProfileAsync(ClanMemberVM vm)
    {
        try
        {
            var (name, avatar) = await FetchSteamProfileAsync(vm.SteamId).ConfigureAwait(false);

            if (avatar != null) _avatarCache[vm.SteamId] = avatar;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(name)) vm.Name = name;
                if (avatar != null) vm.Avatar = avatar;
            });
        }
        catch { /* best-effort — SteamID stays as the fallback display name */ }
    }

    private static readonly Regex SteamProfileNameRegex = new(@"<steamID><!\[CDATA\[(.*?)\]\]></steamID>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static async Task<(string? name, ImageSource? avatar)> FetchSteamProfileAsync(ulong steamId)
    {
        if (steamId == 0) return (null, null);
        try
        {
            using var http = new HttpClient();
            var xml = await http.GetStringAsync($"https://steamcommunity.com/profiles/{steamId}?xml=1");

            string? name = null;
            var mName = SteamProfileNameRegex.Match(xml);
            if (mName.Success) name = System.Net.WebUtility.HtmlDecode(mName.Groups[1].Value);

            string avatarUrl = "";
            var mFull = Regex.Match(xml, @"<avatarFull><!\[CDATA\[(.*?)\]\]></avatarFull>", RegexOptions.IgnoreCase);
            var mMedium = Regex.Match(xml, @"<avatarMedium><!\[CDATA\[(.*?)\]\]></avatarMedium>", RegexOptions.IgnoreCase);
            if (mFull.Success) avatarUrl = mFull.Groups[1].Value;
            else if (mMedium.Success) avatarUrl = mMedium.Groups[1].Value;

            ImageSource? avatar = null;
            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                var bytes = await http.GetByteArrayAsync(avatarUrl);
                avatar = BytesToImage(bytes);
            }

            return (name, avatar);
        }
        catch
        {
            return (null, null);
        }
=======
>>>>>>> Stashed changes
    }
}
