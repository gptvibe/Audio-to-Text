using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Core.Contracts;
using App.Core.Live;
using App.Models.Domain;
using App_Desktop.Audio;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace App_Desktop.Pages;

public sealed partial class LiveTranscriptionPage : Page
{
    private static readonly TimeSpan RollingWindowDuration = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan RollingBufferDuration = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ChunkInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SpeechProbeDuration = TimeSpan.FromSeconds(2.2);

    private readonly IModelManager _modelManager = AppServices.ModelManager;
    private readonly ILiveTranscriptionClient _liveClient = AppServices.LiveTranscriptionClient;
    private readonly IExportService _exportService = AppServices.ExportService;
    private readonly IHistoryService _historyService = AppServices.HistoryService;
    private readonly MicrophoneCaptureService _microphoneCapture = new();
    private readonly LiveAudioRollingBuffer _audioBuffer = new(MicrophoneCaptureService.TargetBytesPerSecond, RollingBufferDuration);
    private readonly LiveTranscriptMerger _transcriptMerger = new();
    private readonly DispatcherTimer _elapsedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _chunkTimer = new() { Interval = ChunkInterval };
    private readonly SemaphoreSlim _chunkGate = new(1, 1);
    private readonly Dictionary<long, string> _chunkFilesById = new();

    private CancellationTokenSource? _liveCts;
    private ILiveTranscriptionSession? _liveSession;
    private LocalModelInfo? _selectedLocalModel;
    private DateTimeOffset? _recordingStartedAt;
    private DateTimeOffset? _pauseStartedAt;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private TimeSpan _lastSubmittedAudioEnd = TimeSpan.Zero;
    private string? _tempDirectory;
    private string? _currentHistoryId;
    private long _nextChunkId;
    private bool _hasLoaded;
    private bool _isPaused;
    private bool _isStopping;

    public LiveTranscriptionPage()
    {
        InitializeComponent();
        Loaded += LiveTranscriptionPage_Loaded;
        Unloaded += LiveTranscriptionPage_Unloaded;
        _microphoneCapture.Pcm16AudioAvailable += MicrophoneCapture_Pcm16AudioAvailable;
        _elapsedTimer.Tick += ElapsedTimer_Tick;
        _chunkTimer.Tick += ChunkTimer_Tick;
    }

    private async void LiveTranscriptionPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded)
        {
            return;
        }

        _hasLoaded = true;
        OutputModeComboBox.ItemsSource = Enum.GetValues<TranscriptOutputMode>();
        OutputModeComboBox.SelectedItem = TranscriptOutputMode.Timestamps;
        PerformanceComboBox.ItemsSource = Enum.GetValues<PerformanceMode>();

        var settings = await AppServices.Settings.LoadAsync();
        PerformanceComboBox.SelectedItem = settings.PerformanceMode;
        ModelComboBox.ItemsSource = _modelManager.GetSupportedModels();
        ModelComboBox.SelectedItem = _modelManager.GetSupportedModels()
            .FirstOrDefault(model => model.RepoId.Equals(settings.DefaultModelRepoId, StringComparison.OrdinalIgnoreCase))
            ?? _modelManager.GetSupportedModels().FirstOrDefault(model => model.IsRecommended)
            ?? _modelManager.GetSupportedModels().FirstOrDefault();

        RefreshMicrophones();
        await RefreshSelectedModelAsync();
        UpdateButtonStates(LiveRecordingState.Idle);
    }

    private async void LiveTranscriptionPage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_liveSession is not null)
        {
            await StopLiveAsync(saveHistory: false);
        }
        else
        {
            _liveCts?.Cancel();
            await CleanupLiveSessionAsync();
        }

        _microphoneCapture.Dispose();
        _chunkGate.Dispose();
    }

    private void RefreshMicrophones_Click(object sender, RoutedEventArgs e)
    {
        RefreshMicrophones();
    }

    private void RefreshMicrophones()
    {
        try
        {
            var devices = _microphoneCapture.GetDevices();
            MicrophoneComboBox.ItemsSource = devices;
            MicrophoneComboBox.SelectedItem = devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault();
            MicrophoneStatusTextBlock.Text = devices.Count == 0
                ? "No active microphones were found."
                : $"{devices.Count} microphone{(devices.Count == 1 ? string.Empty : "s")} available via WASAPI.";
        }
        catch (Exception ex)
        {
            MicrophoneStatusTextBlock.Text = "Could not enumerate microphones.";
            _ = LogErrorAsync(ex);
        }

        UpdateButtonStates(CurrentState);
    }

    private async void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await RefreshSelectedModelAsync();
        UpdateButtonStates(CurrentState);
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
        CurrentModelTextBlock.Text = $"Model: {model.DisplayName}";
    }

    private async void DownloadSelectedModel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelComboBox.SelectedItem is not SpeechModelDefinition model)
        {
            return;
        }

        DownloadModelButton.IsEnabled = false;
        LiveMessageTextBlock.Text = "Downloading model";

        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                LiveMessageTextBlock.Text = value.Message;
            });

            _selectedLocalModel = await _modelManager.DownloadModelAsync(model.RepoId, progress);
            ModelStatusTextBlock.Text = $"Ready at {_selectedLocalModel.LocalPath}";
            LiveMessageTextBlock.Text = "Model ready";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Model download failed", ex.Message);
            await LogErrorAsync(ex);
            LiveMessageTextBlock.Text = "Download failed";
        }
        finally
        {
            DownloadModelButton.IsEnabled = true;
            DownloadModelButton.Content = "Redownload";
            UpdateButtonStates(CurrentState);
        }
    }

    private async void StartLive_Click(object sender, RoutedEventArgs e)
    {
        await StartLiveAsync();
    }

    private async Task StartLiveAsync()
    {
        if (_selectedLocalModel is null || _selectedLocalModel.Status != ModelDownloadStatus.Downloaded)
        {
            await ShowErrorAsync("Model required", "Download the selected model before starting live transcription.");
            return;
        }

        if (MicrophoneComboBox.SelectedItem is not MicrophoneDeviceInfo microphone)
        {
            await ShowErrorAsync("Microphone required", "Choose a microphone before starting live transcription.");
            return;
        }

        _liveCts = new CancellationTokenSource();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "quietscribe-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);

        _transcriptMerger.Clear();
        _audioBuffer.Clear();
        _chunkFilesById.Clear();
        _lastSubmittedAudioEnd = TimeSpan.Zero;
        _pausedDuration = TimeSpan.Zero;
        _pauseStartedAt = null;
        _nextChunkId = 0;
        _isPaused = false;
        _isStopping = false;
        _currentHistoryId = null;
        LiveTranscriptTextBox.Text = string.Empty;
        PartialTextBlock.Text = "Loading model.";
        LatencyTextBlock.Text = "Latency --";

        UpdateButtonStates(LiveRecordingState.LoadingModel);

        try
        {
            var options = CreateTranscriptionOptions();
            var progress = new Progress<LiveTranscriptionEvent>(HandleLiveEvent);
            _liveSession = await _liveClient.StartLiveSessionAsync(
                _selectedLocalModel.LocalPath,
                options,
                progress,
                _liveCts.Token);

            _microphoneCapture.Start(microphone.Id);
            _recordingStartedAt = DateTimeOffset.Now;
            _elapsedTimer.Start();
            _chunkTimer.Start();
            DeviceBackendTextBlock.Text = $"{microphone.Name} - WASAPI - 16 kHz mono PCM";
            LiveMessageTextBlock.Text = "Recording";
            PartialTextBlock.Text = "Listening for speech.";
            UpdateButtonStates(LiveRecordingState.Recording);
        }
        catch (OperationCanceledException)
        {
            LiveMessageTextBlock.Text = "Live transcription canceled";
            await CleanupLiveSessionAsync();
            UpdateButtonStates(LiveRecordingState.Idle);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Live transcription failed", ex.Message);
            await LogErrorAsync(ex);
            LiveMessageTextBlock.Text = "Live transcription failed";
            await CleanupLiveSessionAsync();
            UpdateButtonStates(LiveRecordingState.Failed);
        }
    }

    private async void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_liveSession is null)
        {
            return;
        }

        if (_isPaused)
        {
            _isPaused = false;
            if (_pauseStartedAt is not null)
            {
                _pausedDuration += DateTimeOffset.Now - _pauseStartedAt.Value;
            }

            _pauseStartedAt = null;
            _microphoneCapture.Resume();
            _chunkTimer.Start();
            LiveMessageTextBlock.Text = "Recording";
            PartialTextBlock.Text = "Listening for speech.";
            UpdateButtonStates(LiveRecordingState.Recording);
        }
        else
        {
            await SubmitChunkAsync(isFinal: true, force: true);
            _isPaused = true;
            _pauseStartedAt = DateTimeOffset.Now;
            _microphoneCapture.Pause();
            _chunkTimer.Stop();
            LiveMessageTextBlock.Text = "Paused";
            PartialTextBlock.Text = "Paused.";
            UpdateButtonStates(LiveRecordingState.Paused);
        }
    }

    private async void StopLive_Click(object sender, RoutedEventArgs e)
    {
        await StopLiveAsync();
    }

    private async Task StopLiveAsync(bool saveHistory = true)
    {
        if (_liveSession is null)
        {
            _liveCts?.Cancel();
            LiveMessageTextBlock.Text = "Canceling live transcription startup";
            UpdateButtonStates(LiveRecordingState.Stopping);
            return;
        }

        if (_isStopping)
        {
            return;
        }

        _isStopping = true;
        UpdateButtonStates(LiveRecordingState.Stopping);
        LiveMessageTextBlock.Text = "Stopping live transcription";
        _chunkTimer.Stop();
        _elapsedTimer.Stop();
        _microphoneCapture.Stop();

        try
        {
            await SubmitChunkAsync(isFinal: true, force: true);
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await _liveSession.StopAsync(stopCts.Token);
        }
        catch (OperationCanceledException)
        {
            LiveMessageTextBlock.Text = "Live worker cleanup timed out";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Stop failed", ex.Message);
            await LogErrorAsync(ex);
        }
        finally
        {
            await CleanupLiveSessionAsync();
            RenderTranscript();
            LiveTranscriptTextBox.IsReadOnly = false;
            PartialTextBlock.Text = "Stopped.";
            LiveMessageTextBlock.Text = "Stopped";
            UpdateButtonStates(LiveRecordingState.Stopped);

            if (saveHistory)
            {
                await SaveHistoryAsync();
            }
        }
    }

    private async void ChunkTimer_Tick(object? sender, object e)
    {
        await SubmitChunkAsync();
    }

    private async Task SubmitChunkAsync(bool isFinal = false, bool force = false)
    {
        if (_liveSession is null || _liveCts is null || (_isPaused && !force))
        {
            return;
        }

        if (!await _chunkGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            if (!_audioBuffer.TryCreateWindow(RollingWindowDuration, out var pcm16, out var audioStart, out var duration))
            {
                return;
            }

            var audioEnd = audioStart + duration;
            if (!force && audioEnd - _lastSubmittedAudioEnd < TimeSpan.FromSeconds(1.2))
            {
                return;
            }

            if (!force)
            {
                _audioBuffer.TryCreateRecent(SpeechProbeDuration, out var recentAudio);
                if (!LiveSilenceDetector.ContainsSpeech(recentAudio))
                {
                    LiveMessageTextBlock.Text = "Listening for speech";
                    return;
                }
            }
            else if (!LiveSilenceDetector.ContainsSpeech(pcm16) && _transcriptMerger.Segments.Count == 0)
            {
                return;
            }

            var chunkId = Interlocked.Increment(ref _nextChunkId);
            var path = Path.Combine(_tempDirectory!, $"chunk-{chunkId:000000}.wav");
            await WaveChunkWriter.WritePcm16MonoAsync(path, pcm16, _liveCts.Token);
            _chunkFilesById[chunkId] = path;

            await _liveSession.SendAudioChunkAsync(new LiveAudioChunk
            {
                Id = chunkId,
                Path = path,
                Start = audioStart,
                Duration = duration,
                IsFinal = isFinal
            }, _liveCts.Token);

            _lastSubmittedAudioEnd = audioEnd;
            LiveMessageTextBlock.Text = "Sent audio to local worker";
        }
        catch (OperationCanceledException)
        {
            // Cancellation is reflected by the owning start/stop flow.
        }
        catch (Exception ex)
        {
            LiveMessageTextBlock.Text = "Could not submit live audio";
            await LogErrorAsync(ex);
        }
        finally
        {
            _chunkGate.Release();
        }
    }

    private void MicrophoneCapture_Pcm16AudioAvailable(object? sender, byte[] e)
    {
        _audioBuffer.Append(e);
    }

    private void HandleLiveEvent(LiveTranscriptionEvent liveEvent)
    {
        CleanupChunkFileIfComplete(liveEvent);

        switch (liveEvent.Kind)
        {
            case LiveTranscriptionEventKind.Ready:
                DeviceBackendTextBlock.Text = $"Worker backend: {liveEvent.Backend ?? "cpu"} {liveEvent.ComputeType ?? "int8"}";
                LiveMessageTextBlock.Text = liveEvent.Message ?? "Live worker ready";
                break;
            case LiveTranscriptionEventKind.Progress:
                if (!string.IsNullOrWhiteSpace(liveEvent.Message))
                {
                    LiveMessageTextBlock.Text = liveEvent.Message;
                }

                if (!string.IsNullOrWhiteSpace(liveEvent.Backend))
                {
                    DeviceBackendTextBlock.Text = $"Worker backend: {liveEvent.Backend} {liveEvent.ComputeType ?? string.Empty}".Trim();
                }

                UpdateLatency(liveEvent);
                break;
            case LiveTranscriptionEventKind.PartialText:
                _transcriptMerger.Apply(liveEvent);
                PartialTextBlock.Text = string.IsNullOrWhiteSpace(_transcriptMerger.PartialText)
                    ? "Listening for speech."
                    : _transcriptMerger.PartialText;
                UpdateLatency(liveEvent);
                break;
            case LiveTranscriptionEventKind.FinalSegment:
                if (_transcriptMerger.Apply(liveEvent))
                {
                    RenderTranscript();
                }

                UpdateLatency(liveEvent);
                break;
            case LiveTranscriptionEventKind.Error:
                LiveMessageTextBlock.Text = liveEvent.Message ?? "Live worker error";
                _ = ShowErrorAsync("Live transcription error", LiveMessageTextBlock.Text);
                break;
            case LiveTranscriptionEventKind.Stopped:
                _transcriptMerger.Apply(liveEvent);
                PartialTextBlock.Text = "Stopped.";
                LiveMessageTextBlock.Text = liveEvent.Message ?? "Stopped";
                RenderTranscript();
                break;
        }
    }

    private void CleanupChunkFileIfComplete(LiveTranscriptionEvent liveEvent)
    {
        if (liveEvent.ChunkId is null || liveEvent.LatencyMilliseconds is null)
        {
            return;
        }

        if (_chunkFilesById.Remove(liveEvent.ChunkId.Value, out var path))
        {
            TryDeleteFile(path);
        }
    }

    private void RenderTranscript()
    {
        var document = CreateCurrentTranscriptDocument();
        var mode = OutputModeComboBox.SelectedItem is TranscriptOutputMode outputMode
            ? outputMode
            : TranscriptOutputMode.Timestamps;
        LiveTranscriptTextBox.Text = _exportService.FormatTranscript(document, mode);
        LiveTranscriptTextBox.SelectionStart = LiveTranscriptTextBox.Text.Length;
        LiveTranscriptTextBox.SelectionLength = 0;
    }

    private TranscriptDocument CreateCurrentTranscriptDocument()
    {
        var sourceName = _recordingStartedAt is null
            ? "Live transcription"
            : $"Live transcription {_recordingStartedAt.Value:yyyy-MM-dd HH-mm}";

        return _transcriptMerger.ToDocument(
            sourceName,
            (ModelComboBox.SelectedItem as SpeechModelDefinition)?.RepoId ?? _selectedLocalModel?.RepoId,
            GetSelectedLanguage(),
            GetElapsed());
    }

    private TranscriptionOptions CreateTranscriptionOptions()
    {
        return new TranscriptionOptions
        {
            ModelRepoId = (ModelComboBox.SelectedItem as SpeechModelDefinition)?.RepoId ?? _selectedLocalModel?.RepoId ?? string.Empty,
            Language = GetSelectedLanguage(),
            TranslateToEnglish = TranslateCheckBox.IsChecked == true,
            OutputMode = OutputModeComboBox.SelectedItem is TranscriptOutputMode mode ? mode : TranscriptOutputMode.Timestamps,
            PerformanceMode = PerformanceComboBox.SelectedItem is PerformanceMode performanceMode ? performanceMode : PerformanceMode.Auto,
            Diarization = new DiarizationSettings { IsEnabled = false }
        };
    }

    private string? GetSelectedLanguage()
    {
        return LanguageComboBox.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Tag?.ToString())
            ? item.Tag.ToString()
            : null;
    }

    private void OutputModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderTranscript();
    }

    private void CopyLive_Click(object sender, RoutedEventArgs e)
    {
        var text = LiveTranscriptTextBox.Text;
        if (!string.IsNullOrWhiteSpace(_transcriptMerger.PartialText))
        {
            text = string.IsNullOrWhiteSpace(text)
                ? _transcriptMerger.PartialText
                : text.TrimEnd() + Environment.NewLine + _transcriptMerger.PartialText;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void ClearLive_Click(object sender, RoutedEventArgs e)
    {
        _transcriptMerger.Clear();
        _currentHistoryId = null;
        LiveTranscriptTextBox.Text = string.Empty;
        PartialTextBlock.Text = _liveSession is null ? "Cleared." : "Listening for speech.";
        LiveMessageTextBlock.Text = "Transcript cleared";
    }

    private async void ExportLive_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LiveTranscriptTextBox.Text))
        {
            return;
        }

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = CreateCurrentTranscriptDocument().SourceName?.Replace(' ', '-').ToLowerInvariant() ?? "live-transcript"
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
            await File.WriteAllTextAsync(file.Path, LiveTranscriptTextBox.Text);
            await RememberExportFolderAsync(file.Path);
            LiveMessageTextBlock.Text = $"Exported {file.Name}";
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Export failed", ex.Message);
            await LogErrorAsync(ex);
        }
    }

    private async Task SaveHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(LiveTranscriptTextBox.Text) || _transcriptMerger.Segments.Count == 0)
        {
            return;
        }

        var transcript = CreateCurrentTranscriptDocument();
        _currentHistoryId = Guid.NewGuid().ToString("N");
        await _historyService.SaveAsync(new HistoryItem
        {
            Id = _currentHistoryId,
            SourceName = transcript.SourceName ?? "Live transcription",
            ModelRepoId = transcript.ModelRepoId,
            Language = transcript.Language,
            Duration = transcript.Duration,
            DiarizationEnabled = false,
            Transcript = transcript
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

    private async Task CleanupLiveSessionAsync()
    {
        _chunkTimer.Stop();
        _elapsedTimer.Stop();
        _microphoneCapture.Stop();
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        _liveCts = null;

        if (_liveSession is not null)
        {
            await _liveSession.DisposeAsync();
            _liveSession = null;
        }

        foreach (var path in _chunkFilesById.Values)
        {
            TryDeleteFile(path);
        }

        _chunkFilesById.Clear();

        if (!string.IsNullOrWhiteSpace(_tempDirectory) && Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Temporary files are best-effort cleanup.
            }
        }

        _tempDirectory = null;
        _isPaused = false;
        _isStopping = false;
    }

    private void ElapsedTimer_Tick(object? sender, object e)
    {
        var elapsed = GetElapsed();
        ElapsedTextBlock.Text = elapsed.TotalHours >= 1
            ? $"Elapsed {elapsed:hh\\:mm\\:ss}"
            : $"Elapsed {elapsed:mm\\:ss}";
    }

    private TimeSpan GetElapsed()
    {
        if (_recordingStartedAt is null)
        {
            return TimeSpan.Zero;
        }

        var now = _isPaused && _pauseStartedAt is not null ? _pauseStartedAt.Value : DateTimeOffset.Now;
        return now - _recordingStartedAt.Value - _pausedDuration;
    }

    private void UpdateLatency(LiveTranscriptionEvent liveEvent)
    {
        if (liveEvent.LatencyMilliseconds is not null)
        {
            LatencyTextBlock.Text = $"Latency {liveEvent.LatencyMilliseconds.Value / 1000:0.0}s";
        }
    }

    private LiveRecordingState CurrentState
    {
        get
        {
            if (_liveSession is null)
            {
                return LiveRecordingState.Idle;
            }

            if (_isStopping)
            {
                return LiveRecordingState.Stopping;
            }

            return _isPaused ? LiveRecordingState.Paused : LiveRecordingState.Recording;
        }
    }

    private void UpdateButtonStates(LiveRecordingState state)
    {
        var modelReady = _selectedLocalModel?.Status == ModelDownloadStatus.Downloaded;
        var hasMicrophone = MicrophoneComboBox.SelectedItem is MicrophoneDeviceInfo;
        var isRunning = state is LiveRecordingState.Recording or LiveRecordingState.Paused or LiveRecordingState.LoadingModel or LiveRecordingState.Stopping;

        StartLiveButton.IsEnabled = !isRunning && modelReady && hasMicrophone;
        PauseResumeButton.IsEnabled = state is LiveRecordingState.Recording or LiveRecordingState.Paused;
        PauseResumeButton.Content = state == LiveRecordingState.Paused ? "Resume" : "Pause";
        StopLiveButton.IsEnabled = state is LiveRecordingState.Recording or LiveRecordingState.Paused or LiveRecordingState.LoadingModel;
        DownloadModelButton.IsEnabled = !isRunning;
        ModelComboBox.IsEnabled = !isRunning;
        MicrophoneComboBox.IsEnabled = !isRunning;
        RefreshMicrophonesButton.IsEnabled = !isRunning;
        PerformanceComboBox.IsEnabled = !isRunning;
        LanguageComboBox.IsEnabled = !isRunning;
        TranslateCheckBox.IsEnabled = !isRunning;
        LiveTranscriptTextBox.IsReadOnly = isRunning;
        LiveStateTextBlock.Text = state switch
        {
            LiveRecordingState.LoadingModel => "Loading model",
            LiveRecordingState.Recording => "Recording",
            LiveRecordingState.Paused => "Paused",
            LiveRecordingState.Stopping => "Stopping",
            LiveRecordingState.Stopped => "Stopped",
            LiveRecordingState.Failed => "Failed",
            _ => "Idle"
        };
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Temporary files are best-effort cleanup.
        }
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
