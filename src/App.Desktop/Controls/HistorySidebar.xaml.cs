using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Contracts;
using App.Models.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App_Desktop.Controls;

public sealed partial class HistorySidebar : UserControl
{
    private readonly IHistoryService _historyService = AppServices.HistoryService;
    private readonly ObservableCollection<HistorySidebarItemViewModel> _items = [];
    private IReadOnlyList<HistoryItem> _allItems = [];
    private bool _hasLoaded;
    private bool _suppressSelectionChanged;

    public HistorySidebar()
    {
        InitializeComponent();
        HistoryListView.ItemsSource = _items;
        Loaded += HistorySidebar_Loaded;
    }

    public event EventHandler<HistoryItemSelectedEventArgs>? HistoryItemSelected;

    public event EventHandler? NewTranscriptionRequested;

    public event EventHandler<SidebarNavigationRequestedEventArgs>? NavigationRequested;

    public string? SelectedHistoryId { get; private set; }

    public async Task RefreshAsync()
    {
        _allItems = await _historyService.GetHistoryAsync();
        ApplyFilter(SelectedHistoryId);
    }

    public void ClearSelection()
    {
        SelectedHistoryId = null;
        _suppressSelectionChanged = true;
        HistoryListView.SelectedItem = null;
        _suppressSelectionChanged = false;
    }

    private async void HistorySidebar_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        await RefreshAsync();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(SelectedHistoryId);
    }

    private void ApplyFilter(string? selectedId)
    {
        var query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item => Matches(item, query)).ToList();

        _suppressSelectionChanged = true;
        _items.Clear();
        foreach (var item in filtered)
        {
            _items.Add(new HistorySidebarItemViewModel(item));
        }

        HistoryListView.SelectedItem = _items.FirstOrDefault(item => item.Id == selectedId);
        _suppressSelectionChanged = false;

        EmptyStateTextBlock.Text = _allItems.Count == 0
            ? "No transcripts yet."
            : "No matching transcripts.";
        EmptyStateTextBlock.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Matches(HistoryItem item, string query)
    {
        return Contains(item.SourceName, query)
            || Contains(item.ModelRepoId, query)
            || Contains(item.Language, query)
            || item.Transcript.Segments.Any(segment => Contains(segment.Text, query));
    }

    private static bool Contains(string? value, string query)
    {
        return value?.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }

    private void HistoryListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not HistorySidebarItemViewModel item)
        {
            return;
        }

        SelectedHistoryId = item.Id;
        HistoryListView.SelectedItem = item;

        if (!_suppressSelectionChanged)
        {
            HistoryItemSelected?.Invoke(this, new HistoryItemSelectedEventArgs(item.Item));
        }
    }

    private void NewTranscription_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        NewTranscriptionRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Navigation_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string route)
        {
            ClearSelection();
            NavigationRequested?.Invoke(this, new SidebarNavigationRequestedEventArgs(route));
        }
    }
}

public sealed class HistoryItemSelectedEventArgs : EventArgs
{
    public HistoryItemSelectedEventArgs(HistoryItem item)
    {
        Item = item;
    }

    public HistoryItem Item { get; }
}

public sealed class SidebarNavigationRequestedEventArgs : EventArgs
{
    public SidebarNavigationRequestedEventArgs(string route)
    {
        Route = route;
    }

    public string Route { get; }
}

public sealed class HistorySidebarItemViewModel
{
    public HistorySidebarItemViewModel(HistoryItem item)
    {
        Item = item;
        SourceName = string.IsNullOrWhiteSpace(item.SourceName) ? "Untitled transcript" : item.SourceName;

        var model = string.IsNullOrWhiteSpace(item.ModelRepoId) ? "Unknown model" : item.ModelRepoId;
        Details = string.Create(
            CultureInfo.CurrentCulture,
            $"{item.CreatedAt:g} - {model}");
    }

    public HistoryItem Item { get; }

    public string Id => Item.Id;

    public string SourceName { get; }

    public string Details { get; }
}
