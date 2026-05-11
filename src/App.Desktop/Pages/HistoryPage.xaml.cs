using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Contracts;
using App.Models.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace App_Desktop.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly IHistoryService _historyService = AppServices.HistoryService;
    private readonly IExportService _exportService = AppServices.ExportService;
    private HistoryItem? _selectedItem;

    public HistoryPage()
    {
        InitializeComponent();
        Loaded += HistoryPage_Loaded;
    }

    private async void HistoryPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RenderHistoryAsync();
    }

    private async Task RenderHistoryAsync()
    {
        HistoryStackPanel.Children.Clear();
        var items = await _historyService.GetHistoryAsync();

        if (items.Count == 0)
        {
            HistoryStackPanel.Children.Add(new TextBlock
            {
                Text = "No transcripts yet.",
                Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
            });
            return;
        }

        foreach (var item in items)
        {
            var button = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Tag = item.Id,
                Content = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = item.SourceName, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                        new TextBlock
                        {
                            Text = $"{item.CreatedAt:g} - {item.ModelRepoId ?? "Unknown model"}",
                            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
                        }
                    }
                }
            };
            button.Click += HistoryItem_Click;
            HistoryStackPanel.Children.Add(button);
        }
    }

    private async void HistoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string id)
        {
            return;
        }

        _selectedItem = await _historyService.GetAsync(id);
        if (_selectedItem is null)
        {
            return;
        }

        SelectedTitleTextBlock.Text = _selectedItem.SourceName;
        SelectedMetaTextBlock.Text = $"{_selectedItem.CreatedAt:g} - {_selectedItem.Language ?? "auto"} - Diarization {(_selectedItem.DiarizationEnabled ? "on" : "off")}";
        TranscriptTextBox.Text = _exportService.FormatTranscript(_selectedItem.Transcript, TranscriptOutputMode.SpeakersAndTimestamps);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(TranscriptTextBox.Text);
        Clipboard.SetContent(package);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || string.IsNullOrWhiteSpace(TranscriptTextBox.Text))
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(_selectedItem.SourceName) + "-transcript"
        };
        picker.FileTypeChoices.Add("Text transcript", [".txt"]);
        InitializePicker(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await File.WriteAllTextAsync(file.Path, TranscriptTextBox.Text);
            await _historyService.SaveAsync(_selectedItem with { ExportPath = file.Path });
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete history item?",
            Content = "The transcript history entry will be removed. Audio files are not affected.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _historyService.DeleteAsync(_selectedItem.Id);
            _selectedItem = null;
            TranscriptTextBox.Text = string.Empty;
            SelectedTitleTextBlock.Text = "Select a transcript";
            SelectedMetaTextBlock.Text = "Previous transcripts appear here.";
            await RenderHistoryAsync();
        }
    }

    private static void InitializePicker(object picker)
    {
        if (App.CurrentWindow is null)
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(App.CurrentWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }
}
