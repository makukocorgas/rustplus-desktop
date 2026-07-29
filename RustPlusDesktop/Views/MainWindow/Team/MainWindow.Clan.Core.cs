using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using RustPlusDesk.Services;

namespace RustPlusDesk.Views;

public partial class MainWindow
{
    public ObservableCollection<ClanMemberVM> ClanMembers { get; } = new();

    private bool _clanLoadInFlight;
    private DateTime? _clanLastPullUtc;
    private RustPlusApi.Data.Clans.ClanInfo? _lastClanInfo;
    private bool _clanChangedHooked;

    public sealed class ClanMemberVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ulong SteamId { get; init; }

        private string _name = "";
        public string Name
        {
            get => _name;
            set { if (_name == value) return; _name = value; OnChanged(nameof(Name)); }
        }

        private ImageSource? _avatar;
        public ImageSource? Avatar
        {
            get => _avatar;
            set { if (_avatar == value) return; _avatar = value; OnChanged(nameof(Avatar)); }
        }

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
    }
}
