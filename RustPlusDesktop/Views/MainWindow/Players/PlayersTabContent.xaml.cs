using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.Views;

namespace RustPlusDesk.Views;

public partial class PlayersTabContent : UserControl
{
    private Views.MainWindow? _mainWindow;

    public PlayersTabContent()
    {
        InitializeComponent();
    }

    public void SetMainWindow(Views.MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    private void BtnSearchBM_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnSearchBM_Click(sender, e);

    private void BtnSearchBM_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _mainWindow?.BtnSearchBM_PreviewMouseRightButtonDown(sender, e);

    private void BtnShowOnline_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnShowOnline_Click(sender, e);

    private void BtnPopoutPlayers_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnPopoutPlayers_Click(sender, e);

    private void BtnAddManual_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnAddManual_Click(sender, e);

    private void TxtOnlineFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        _mainWindow?.TxtOnlineFilter_TextChanged(sender, e);

    private void BtnTrackPlayer_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnTrackPlayer_Click(sender, e);

    private void BtnManageGroups_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnManageGroups_Click(sender, e);

    private void TxtTrackedFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        _mainWindow?.TxtTrackedFilter_TextChanged(sender, e);

    private void BtnViewAllAnalysis_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnViewAllAnalysis_Click(sender, e);

    private void Server_Delete_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.Server_Delete_Click(sender, e);

    private void RootTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        _mainWindow?.PlayersRootTabControl_SelectionChanged(sender, e);

    private void BtnRefreshRoster_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnRefreshRoster_Click(sender, e);

    private void TxtRosterFilter_TextChanged(object sender, TextChangedEventArgs e) =>
        _mainWindow?.TxtRosterFilter_TextChanged(sender, e);

    private void BtnTrackRosterPlayer_Click(object sender, RoutedEventArgs e) =>
        _mainWindow?.BtnTrackRosterPlayer_Click(sender, e);
}
