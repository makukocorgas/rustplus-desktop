using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RustPlusDesk.ViewModels;

namespace RustPlusDesk.Views;

public partial class CraftCalculatorView : UserControl
{
    private readonly CraftCalculatorViewModel _viewModel = new();
    private bool _initialized;

    public CraftCalculatorView()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public event RoutedEventHandler? CloseRequested;

    private async void CraftCalculatorView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        await _viewModel.InitializeAsync();
    }

    private void SelectItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CraftItemCardViewModel item)
            _viewModel.SelectItem(item);
    }

    private void Increment_Click(object sender, RoutedEventArgs e) => _viewModel.Quantity++;
    private void Decrement_Click(object sender, RoutedEventArgs e) => _viewModel.Quantity--;

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, e);

    private async void CopySummary_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.HasSelection) return;
        try { await SetClipboardTextAsync(_viewModel.BuildSummary()); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, RustPlusDesk.Properties.Resources.CraftCalculatorCopySummaryErrorTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Mirrors RaidCalculatorView's clipboard retry helper: WPF clipboard access can transiently
    // fail with "clipboard busy" (0x800401D0) when another process holds it.
    private static Task SetClipboardTextAsync(string text)
    {
        const int clipboardBusy = unchecked((int)0x800401D0);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetDataObject(text, true);
                    completion.SetResult();
                    return;
                }
                catch (COMException exception) when (exception.HResult == clipboardBusy && attempt < 49)
                {
                    Thread.Sleep(100);
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                    return;
                }
            }
        }) { IsBackground = true, Name = "Craft summary clipboard" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
