using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using RustPlusDesk.Models.Craft;
using RustPlusDesk.Services.Craft;
using RustPlusDesk.Views;

namespace RustPlusDesk.ViewModels;

public sealed class CraftCalculatorViewModel : INotifyPropertyChanged
{
    private readonly CraftDataService _dataService = new();
    private CraftDataSet? _data;
    private CraftCalculatorEngine? _engine;
    private List<CraftItemCardViewModel> _allItems = [];
    private string _searchText = string.Empty;
    private CraftItemCardViewModel? _selectedItem;
    private int _quantity = 1;
    private string _statusMessage = RustPlusDesk.Properties.Resources.CraftCalculatorLoading;
    private bool _isLoading = true;

    public ObservableCollection<CraftItemCardViewModel> FilteredItems { get; } = [];
    public ObservableCollection<CraftTreeRowViewModel> TreeRows { get; } = [];
    public ObservableCollection<CraftBaseResourceRowViewModel> BaseResourceTotals { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    public CraftItemCardViewModel? SelectedItem
    {
        get => _selectedItem;
        private set { if (SetField(ref _selectedItem, value)) Recalculate(); }
    }

    public int Quantity
    {
        get => _quantity;
        set
        {
            int clamped = Math.Clamp(value, 1, 99999);
            if (SetField(ref _quantity, clamped)) Recalculate();
        }
    }

    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public bool IsLoading { get => _isLoading; private set { if (SetField(ref _isLoading, value)) OnPropertyChanged(nameof(IsReady)); } }
    public bool IsReady => !IsLoading && _data is not null;
    public bool HasSelection => SelectedItem is not null;
    public bool IsCatalogueEmpty => FilteredItems.Count == 0;
    public bool SelectedItemUnverified => SelectedItem is { Item.Verified: false };

    public string DataCaption => _data is null ? string.Empty
        : $"{_data.Items.Count(item => item.Ingredients.Count > 0)} craftable items · dataset {_data.GeneratedAt:yyyy-MM-dd}";

    public async Task InitializeAsync()
    {
        try
        {
            _data = await _dataService.LoadAsync();
            _engine = new CraftCalculatorEngine(_data);
            _allItems = _data.Items
                .Where(item => item.Ingredients.Count > 0)
                .OrderBy(item => item.DisplayName)
                .Select(item => new CraftItemCardViewModel(item))
                .ToList();
            ApplyFilter();
            StatusMessage = _allItems.Count == 0 ? RustPlusDesk.Properties.Resources.CraftCalculatorNoItemsAvailable : string.Empty;
            OnPropertyChanged(nameof(DataCaption));
        }
        catch (Exception exception)
        {
            StatusMessage = string.Format(RustPlusDesk.Properties.Resources.CraftCalculatorUnavailableFormat, exception.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void SelectItem(CraftItemCardViewModel item)
    {
        _quantity = 1;
        OnPropertyChanged(nameof(Quantity));
        SelectedItem = item;
    }

    private void ApplyFilter()
    {
        IEnumerable<CraftItemCardViewModel> items = _allItems;
        if (!string.IsNullOrWhiteSpace(_searchText))
            items = items.Where(item => item.SearchText.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase));

        FilteredItems.Clear();
        foreach (CraftItemCardViewModel item in items) FilteredItems.Add(item);
        OnPropertyChanged(nameof(IsCatalogueEmpty));
    }

    private void Recalculate()
    {
        TreeRows.Clear();
        BaseResourceTotals.Clear();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedItemUnverified));

        if (_engine is null || SelectedItem is null) return;

        CraftTreeNode root = _engine.BuildTree(SelectedItem.Item, Quantity);
        foreach (CraftTreeNode child in root.Children)
            Flatten(child, TreeRows);

        foreach (CraftBaseResourceTotal total in CraftCalculatorEngine.AggregateBaseResources(root))
            BaseResourceTotals.Add(new CraftBaseResourceRowViewModel(total));
    }

    private static void Flatten(CraftTreeNode node, ObservableCollection<CraftTreeRowViewModel> into)
    {
        into.Add(new CraftTreeRowViewModel(node));
        foreach (CraftTreeNode child in node.Children) Flatten(child, into);
    }

    public string BuildSummary()
    {
        if (SelectedItem is null) return string.Empty;
        var plan = string.Format(RustPlusDesk.Properties.Resources.CraftCalculatorPlanFormat, SelectedItem.DisplayName, Quantity);
        var text = new StringBuilder("**").Append(plan).Append("**");
        if (BaseResourceTotals.Count > 0)
        {
            text.AppendLine().AppendLine().Append("**").Append(RustPlusDesk.Properties.Resources.CraftCalculatorBaseResourcesNeeded).Append("**");
            foreach (CraftBaseResourceRowViewModel total in BaseResourceTotals)
                text.AppendLine().Append("• ").Append(total.DisplayName).Append(": ").Append(total.AmountText);
        }
        if (SelectedItemUnverified)
            text.AppendLine().AppendLine().Append(RustPlusDesk.Properties.Resources.CraftCalculatorSummaryUnverifiedNote);
        return text.ToString();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class CraftItemCardViewModel : CraftIconViewModelBase
{
    public CraftItemCardViewModel(CraftItem item) => Item = item;

    public CraftItem Item { get; }
    public string DisplayName => Item.DisplayName;
    public bool IsUnverified => !Item.Verified;
    public string WorkbenchText => Item.WorkbenchLevelRequired is > 0
        ? string.Format(RustPlusDesk.Properties.Resources.CraftCalculatorWorkbenchFormat, Item.WorkbenchLevelRequired)
        : RustPlusDesk.Properties.Resources.CraftCalculatorNoWorkbench;
    public string SearchText => $"{Item.DisplayName} {Item.Shortname}";
    public ImageSource? Icon => GetIcon(Item.ItemId, Item.Shortname, 28);
}

public sealed class CraftTreeRowViewModel : CraftIconViewModelBase
{
    public CraftTreeRowViewModel(CraftTreeNode node) => Node = node;

    public CraftTreeNode Node { get; }
    public string DisplayName => Node.DisplayName;
    public double IndentWidth => Node.Depth * 18;
    public string QuantityText => Node.Quantity.ToString("0.##");
    public bool IsBaseResource => Node.IsBaseResource;
    public string WorkbenchText => Node.Item?.WorkbenchLevelRequired is > 0
        ? string.Format(RustPlusDesk.Properties.Resources.CraftCalculatorWorkbenchFormat, Node.Item.WorkbenchLevelRequired) : string.Empty;
    public ImageSource? Icon => GetIcon(Node.Item?.ItemId ?? 0, Node.Shortname, 22);
}

public sealed class CraftBaseResourceRowViewModel(CraftBaseResourceTotal total) : CraftIconViewModelBase
{
    public string DisplayName => total.DisplayName;
    public double Amount => total.Amount;
    public string AmountText => total.Amount.ToString("0.#");
    public ImageSource? Icon => GetIcon(total.ItemId, total.Shortname, 26);
}

/// <summary>Shared lazy-icon-loading base, mirroring RaidIconViewModelBase's retry behaviour.</summary>
public abstract class CraftIconViewModelBase : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _isLoadingIcon;

    protected ImageSource? GetIcon(int itemId, string? shortname, int size)
        => GetIcon(() => MainWindow.ResolveItemIcon(itemId, shortname, size));

    private ImageSource? GetIcon(Func<ImageSource?> resolve)
    {
        if (_icon is not null || _isLoadingIcon) return _icon;
        _icon = resolve();
        if (_icon is null)
        {
            _isLoadingIcon = true;
            _ = RefreshIconAsync(resolve);
        }
        return _icon;
    }

    private async Task RefreshIconAsync(Func<ImageSource?> resolve)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(250 + (attempt * 150));
            ImageSource? icon = resolve();
            if (icon is null) continue;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _icon = icon;
                _isLoadingIcon = false;
                OnPropertyChanged("Icon");
            });
            return;
        }
        _isLoadingIcon = false;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
