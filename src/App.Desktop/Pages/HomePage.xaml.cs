using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Core.Contracts;
using App.Models.Domain;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace App_Desktop.Pages;

public sealed partial class HomePage : Page
{
    private readonly IModelManager _modelManager = AppServices.ModelManager;
    private readonly ITranscriptionClient _transcriptionClient = AppServices.TranscriptionClient;
    private readonly IExportService _exportService = AppServices.ExportService;
    private readonly IHistoryService _historyService = AppServices.HistoryService;
    private CancellationTokenSource? _transcriptionCts;
    private string? _selectedFilePath;
    private LocalModelInfo? _selectedLocalModel;
    private TranscriptDocument? _currentTranscript;
    private string? _currentHistoryId;
    private bool _hasLoaded;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        OutputModeComboBox.ItemsSource = Enum.GetValues<TranscriptOutputMode>();
        OutputModeComboBox.SelectedItem = TranscriptOutputMode.PlainText;
        PerformanceComboBox.ItemsSource = Enum.GetValues<PerformanceMode>();

        var settings = await AppServices.Settings.LoadAsync();
        PerformanceComboBox.SelectedItem = settings.PerformanceMode;
        ModelComboBox.ItemsSource = _modelManager.GetSupportedModels();
        ModelComboBox.SelectedItem = _modelManager.GetSupportedModels()
            .FirstOrDefault(model => model.RepoId.Equals(settings.DefaultModelRepoId, StringComparison.OrdinalIgnoreCase))
            ?? _modelManager.GetSupportedModels().FirstOrDefault(model => model.IsRecommended)
            ?? _modelManager.GetSupportedModels().FirstOrDefault();

        await RefreshDeviceSummaryAsync();
        await RefreshSelectedModelAsync();
        UpdateStartButton();
    }

    private async Task RefreshDeviceSummaryAsync()
    {
        try
        {
            var devices = await AppServices.HardwareDetection.DetectAsync();
            var preferred = devices.FirstOrDefault(device => device.IsPreferred) ?? devices.FirstOrDefault();
            DeviceSummaryTextBlock.Text = preferred is null
                ? "CPU fallback available"
                : $"{preferred.Name} - {preferred.Backend}";
        }
        catch
        {
            DeviceSummaryTextBlock.Text = "CPU fallback available";
        }
    }

    private async void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshSelectedModelAsync();
        UpdateStartButton();
    }

    private async Task RefreshSelectedModelAsync()
    {
        if (ModelComboBox.SelectedItem is not SpeechModelDefinition model)
        {
            return;
        }

        _selectedLocalModel = await _modelManager.GetLocalModelAsync(model.RepoId);
        ModelStatusTextBlock.Text = _selectedLocalModel.Status switch
        {
            ModelDownloadStatus.Downloaded => $"Ready at {_selectedLocalModel.LocalPath}",
            ModelDownloadStatus.Partial => "Partially downloaded. Resume to finish.",
            ModelDownloadStatus.Invalid => "Downloaded files need validation. Redownload recommended.",
            _ => $"{model.SizeEstimate} - {model.SpeedEstimate} - {model.QualityEstimate}"
        };
        DownloadModelButton.Content = _selectedLocalModel.Status == ModelDownloadStatus.Downloaded ? "Redownload" : "Download";
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Use this file for transcription";
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        var file = items.OfType<StorageFile>().FirstOrDefault();
        if (file is not null)
        {
            SelectFile(file.Path);
        }
    }

    private async void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
            FileTypeFilter = { ".mp3", ".wav", ".m4a", ".mp4", ".mov", ".webm", ".aac", ".flac" }
        };
        InitializePicker(picker);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            SelectFile(file.Path);
        }
    }

    private void SelectFile(string path)
    {
        _selectedFilePath = path;
        SelectedFileTextBox.Text = path;
        UpdateStartButton();
    }

    private async void DownloadSelectedModel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelComboBox.SelectedItem is not SpeechModelDefinition model)
        {
            return;
        }

        DownloadModelButton.IsEnabled = false;
        TaskProgressBar.IsIndeterminate = false;
        CurrentTaskTextBlock.Text = "Downloading model";

        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                CurrentTaskTextBlock.Text = value.Message;
                if (value.Percent is not null)
                {
                    TaskProgressBar.Value = value.Percent.Value;
                }
            });

            _selectedLocalModel = await _modelManager.DownloadModelAsync(model.RepoId, progress);
            ModelStatusTextBlock.Text = $"Ready at {_selectedLocalModel.LocalPath}";
            CurrentTaskTextBlock.Text = "Model ready";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Model download failed", ex.Message);
            await LogErrorAsync(ex);
            CurrentTaskTextBlock.Text = "Download failed";
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
            DownloadModelButton.Content = "Redownload";
            UpdateStartButton();
        }
    }

    private async void StartTranscription_Click(object sender, RoutedEventArgs e)
    {
        await RunTranscriptionAsync();
    }

    private async Task RunTranscriptionAsync()
    {
        if (_selectedFilePath is null || _selectedLocalModel is null)
        {
            return;
        }

        if (_selectedLocalModel.Status != ModelDownloadStatus.Downloaded)
        {
            await ShowErrorAsync("Model required", "Download the selected model before transcription.");
            return;
        }

        var token = await AppServices.Settings.GetHuggingFaceTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            Environment.SetEnvironmentVariable("HF_TOKEN", token);
        }

        _transcriptionCts = new CancellationTokenSource();
        StartButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        TaskProgressBar.Value = 0;
        TaskProgressBar.IsIndeterminate = false;
        TranscriptTextBox.Text = string.Empty;
        SpeakerNamesPanel.Children.Clear();

        try
        {
            var options = CreateTranscriptionOptions();
            var progress = new Progress<TranscriptionProgress>(UpdateProgress);
            _currentTranscript = await _transcriptionClient.TranscribeFileAsync(
                _selectedFilePath,
                _selectedLocalModel.LocalPath,
                options,
                progress,
                _transcriptionCts.Token);

            RenderTranscript();
            await SaveHistoryAsync(options);
        }
        catch (OperationCanceledException)
        {
            CurrentTaskTextBlock.Text = "Canceled";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Transcription failed", ex.Message);
            await LogErrorAsync(ex);
            CurrentTaskTextBlock.Text = "Transcription failed";
        }
        finally
        {
            _transcriptionCts.Dispose();
            _transcriptionCts = null;
            CancelButton.IsEnabled = false;
            UpdateStartButton();
        }
    }

    private void CancelTranscription_Click(object sender, RoutedEventArgs e)
    {
        _transcriptionCts?.Cancel();
    }

    private void UpdateProgress(TranscriptionProgress progress)
    {
        CurrentTaskTextBlock.Text = progress.Message;
        TaskProgressBar.IsIndeterminate = progress.Percent is null;
        if (progress.Percent is not null)
        {
            TaskProgressBar.Value = progress.Percent.Value;
        }
    }

    private TranscriptionOptions CreateTranscriptionOptions()
    {
        var outputMode = OutputModeComboBox.SelectedItem is TranscriptOutputMode mode
            ? mode
            : TranscriptOutputMode.PlainText;

        var expectedSpeakers = SpeakerCountComboBox.SelectedItem is ComboBoxItem item
            && int.TryParse(item.Tag?.ToString(), out var speakerCount)
            ? speakerCount
            : (int?)null;

        var minSegmentSeconds = MinimumSegmentLengthNumberBox.Value;
        if (double.IsNaN(minSegmentSeconds) || minSegmentSeconds <= 0)
        {
            minSegmentSeconds = 1.25;
        }

        return new TranscriptionOptions
        {
            ModelRepoId = (ModelComboBox.SelectedItem as SpeechModelDefinition)?.RepoId ?? _selectedLocalModel?.RepoId ?? string.Empty,
            Language = GetSelectedLanguage(),
            TranslateToEnglish = TranslateCheckBox.IsChecked == true,
            OutputMode = outputMode,
            PerformanceMode = PerformanceComboBox.SelectedItem is PerformanceMode performanceMode ? performanceMode : PerformanceMode.Auto,
            Diarization = new DiarizationSettings
            {
                IsEnabled = DiarizationCheckBox.IsChecked == true || outputMode is TranscriptOutputMode.Speakers or TranscriptOutputMode.SpeakersAndTimestamps,
                ExpectedSpeakers = expectedSpeakers,
                MergeShortTurns = MergeShortTurnsCheckBox.IsChecked == true,
                MinimumSegmentLength = TimeSpan.FromSeconds(minSegmentSeconds)
            }
        };
    }

    private string? GetSelectedLanguage()
    {
        return LanguageComboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Tag?.ToString())
            ? item.Tag.ToString()
            : null;
    }

    private void RenderTranscript()
    {
        if (_currentTranscript is null)
        {
            return;
        }

        var mode = OutputModeComboBox.SelectedItem is TranscriptOutputMode outputMode
            ? outputMode
            : TranscriptOutputMode.PlainText;
        TranscriptTextBox.Text = _exportService.FormatTranscript(_currentTranscript, mode);
        RenderSpeakerEditors();
    }

    private void RenderSpeakerEditors()
    {
        SpeakerNamesPanel.Children.Clear();
        if (_currentTranscript is null || _currentTranscript.SpeakerNames.Count == 0)
        {
            return;
        }

        foreach (var speaker in _currentTranscript.SpeakerNames.OrderBy(pair => pair.Key))
        {
            var box = new TextBox
            {
                Header = speaker.Key,
                Text = speaker.Value,
                Tag = speaker.Key,
                Width = 120
            };
            box.LostFocus += SpeakerName_LostFocus;
            SpeakerNamesPanel.Children.Add(box);
        }
    }

    private void SpeakerName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_currentTranscript is null || sender is not TextBox box || box.Tag is not string speakerId)
        {
            return;
        }

        var names = new Dictionary<string, string>(_currentTranscript.SpeakerNames, StringComparer.OrdinalIgnoreCase)
        {
            [speakerId] = string.IsNullOrWhiteSpace(box.Text) ? speakerId : box.Text.Trim()
        };
        _currentTranscript = _currentTranscript with { SpeakerNames = names };
        RenderTranscript();
    }

    private void OutputModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderTranscript();
    }

    private void CopyTranscript_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(TranscriptTextBox.Text);
        Clipboard.SetContent(package);
    }

    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        _currentTranscript = null;
        _currentHistoryId = null;
        TranscriptTextBox.Text = string.Empty;
        SpeakerNamesPanel.Children.Clear();
        CurrentTaskTextBlock.Text = "Ready";
        TaskProgressBar.Value = 0;
    }

    private async void ExportTxt_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TranscriptTextBox.Text))
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = _currentTranscript?.SourceName is null
                ? "transcript"
                : Path.GetFileNameWithoutExtension(_currentTranscript.SourceName) + "-transcript"
        };
        picker.FileTypeChoices.Add("Text transcript", [".txt"]);
        InitializePicker(picker);

        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(file.Path, TranscriptTextBox.Text);
            await RememberExportFolderAsync(file.Path);
            CurrentTaskTextBlock.Text = $"Exported {file.Name}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Export failed", ex.Message);
            await LogErrorAsync(ex);
        }
    }

    private void SearchTranscript_Click(object sender, RoutedEventArgs e)
    {
        var query = SearchTextBox.Text;
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        var start = Math.Max(TranscriptTextBox.SelectionStart + TranscriptTextBox.SelectionLength, 0);
        var index = TranscriptTextBox.Text.IndexOf(query, start, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0 && start > 0)
        {
            index = TranscriptTextBox.Text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        }

        if (index >= 0)
        {
            TranscriptTextBox.Focus(FocusState.Programmatic);
            TranscriptTextBox.Select(index, query.Length);
        }
    }

    private async void Rerun_Click(object sender, RoutedEventArgs e)
    {
        await RunTranscriptionAsync();
    }

    private async Task SaveHistoryAsync(TranscriptionOptions options)
    {
        if (_currentTranscript is null || _selectedFilePath is null)
        {
            return;
        }

        _currentHistoryId = Guid.NewGuid().ToString("N");
        await _historyService.SaveAsync(new HistoryItem
        {
            Id = _currentHistoryId,
            SourceName = Path.GetFileName(_selectedFilePath),
            ModelRepoId = options.ModelRepoId,
            Language = _currentTranscript.Language,
            Duration = _currentTranscript.Duration,
            DiarizationEnabled = options.Diarization.IsEnabled,
            Transcript = _currentTranscript
        });
    }

    private async Task RememberExportFolderAsync(string exportPath)
    {
        var settings = await AppServices.Settings.LoadAsync();
        await AppServices.Settings.SaveAsync(settings with { LastExportFolder = Path.GetDirectoryName(exportPath) });

        if (_currentHistoryId is not null)
        {
            var item = await _historyService.GetAsync(_currentHistoryId);
            if (item is not null)
            {
                await _historyService.SaveAsync(item with { ExportPath = exportPath });
            }
        }
    }

    private void UpdateStartButton()
    {
        StartButton.IsEnabled = _transcriptionCts is null
            && !string.IsNullOrWhiteSpace(_selectedFilePath)
            && _selectedLocalModel?.Status == ModelDownloadStatus.Downloaded;
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
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

    private static async Task LogErrorAsync(Exception exception)
    {
        try
        {
            var path = Path.Combine(AppServices.Paths.LogsDirectory, "app.log");
            await File.AppendAllTextAsync(path, $"{DateTimeOffset.Now:O} {exception}\n");
        }
        catch
        {
            // Logging must never become another user-facing failure.
        }
    }
}
