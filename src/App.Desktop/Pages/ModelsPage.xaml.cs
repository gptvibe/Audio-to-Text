using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Contracts;
using App.Models.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace App_Desktop.Pages;

public sealed partial class ModelsPage : Page
{
    private readonly IModelManager _modelManager = AppServices.ModelManager;

    public ModelsPage()
    {
        InitializeComponent();
        Loaded += ModelsPage_Loaded;
    }

    private async void ModelsPage_Loaded(object sender, RoutedEventArgs e)
    {
        await RenderModelsAsync();
    }

    private async Task RenderModelsAsync()
    {
        ModelsStackPanel.Children.Clear();
        var catalog = _modelManager.GetSupportedModels();
        foreach (var model in catalog)
        {
            ModelsStackPanel.Children.Add(await CreateModelCardAsync(model));
        }

        var catalogRepoIds = catalog.Select(model => model.RepoId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var customModels = (await _modelManager.GetDownloadedModelsAsync())
            .Where(model => !catalogRepoIds.Contains(model.RepoId))
            .ToList();

        if (customModels.Count > 0)
        {
            ModelsStackPanel.Children.Add(new TextBlock
            {
                Text = "Custom downloads",
                FontSize = 18,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 0)
            });

            foreach (var model in customModels)
            {
                ModelsStackPanel.Children.Add(CreateDownloadedModelCard(model));
            }
        }
    }

    private async Task<FrameworkElement> CreateModelCardAsync(SpeechModelDefinition model)
    {
        var local = await _modelManager.GetLocalModelAsync(model.RepoId);
        var root = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { Spacing = 6 };
        textStack.Children.Add(new TextBlock { Text = model.DisplayName, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock
        {
            Text = $"{model.RepoId} - {model.SizeEstimate} - {model.LanguageSupport}",
            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
        });
        textStack.Children.Add(new TextBlock
        {
            Text = $"{model.SpeedEstimate} speed, {model.QualityEstimate.ToLowerInvariant()} quality. {model.Notes}",
            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
        });
        textStack.Children.Add(new TextBlock
        {
            Text = local.Status == ModelDownloadStatus.Downloaded
                ? $"Downloaded to {local.LocalPath}"
                : local.Status == ModelDownloadStatus.Partial
                    ? "Partially downloaded"
                    : "Not downloaded",
            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var downloadButton = new Button { Content = local.Status == ModelDownloadStatus.Downloaded ? "Redownload" : "Download", Tag = model.RepoId };
        downloadButton.Click += DownloadModel_Click;
        actions.Children.Add(downloadButton);

        var openButton = new Button { Content = "Open Folder", Tag = local.LocalPath, IsEnabled = local.Status == ModelDownloadStatus.Downloaded };
        openButton.Click += OpenFolder_Click;
        actions.Children.Add(openButton);

        var deleteButton = new Button { Content = "Delete", Tag = model.RepoId, IsEnabled = local.Status != ModelDownloadStatus.NotDownloaded };
        deleteButton.Click += DeleteModel_Click;
        actions.Children.Add(deleteButton);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);
        root.Child = grid;
        return root;
    }

    private FrameworkElement CreateDownloadedModelCard(LocalModelInfo model)
    {
        var root = new Border { Style = (Style)Application.Current.Resources["CardBorderStyle"] };
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel { Spacing = 6 };
        textStack.Children.Add(new TextBlock { Text = model.DisplayName, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        textStack.Children.Add(new TextBlock
        {
            Text = $"{model.RepoId} - {FormatBytes(model.DownloadedBytes)} downloaded",
            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
        });
        textStack.Children.Add(new TextBlock
        {
            Text = model.LocalPath,
            Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var downloadButton = new Button { Content = "Redownload", Tag = model.RepoId };
        downloadButton.Click += DownloadModel_Click;
        actions.Children.Add(downloadButton);

        var openButton = new Button { Content = "Open Folder", Tag = model.LocalPath };
        openButton.Click += OpenFolder_Click;
        actions.Children.Add(openButton);

        var deleteButton = new Button { Content = "Delete", Tag = model.RepoId };
        deleteButton.Click += DeleteModel_Click;
        actions.Children.Add(deleteButton);

        Grid.SetColumn(actions, 1);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);
        root.Child = grid;
        return root;
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string repoId)
        {
            return;
        }

        await DownloadRepoAsync(repoId, button);
    }

    private async void DownloadCustomRepo_Click(object sender, RoutedEventArgs e)
    {
        var repoId = CustomRepoTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(repoId) || !repoId.Contains('/'))
        {
            await ShowErrorAsync("Repo ID needed", "Enter a Hugging Face repo ID like Systran/faster-whisper-small.");
            return;
        }

        await DownloadRepoAsync(repoId, null);
    }

    private async Task DownloadRepoAsync(string repoId, Button? sourceButton)
    {
        if (sourceButton is not null)
        {
            sourceButton.IsEnabled = false;
            sourceButton.Content = "Downloading...";
        }

        try
        {
            var progress = new Progress<ModelDownloadProgress>(value =>
            {
                if (sourceButton is not null && value.Percent is not null)
                {
                    sourceButton.Content = $"{value.Percent.Value:0}%";
                }
            });
            await _modelManager.DownloadModelAsync(repoId, progress);
            await RenderModelsAsync();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync("Download failed", ex.Message);
        }
        finally
        {
            if (sourceButton is not null)
            {
                sourceButton.IsEnabled = true;
            }
        }
    }

    private async void DeleteModel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string repoId)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete model?",
            Content = "The local model files will be removed. You can download them again later.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _modelManager.DeleteModelAsync(repoId);
            await RenderModelsAsync();
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string path && System.IO.Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
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

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var index = 0;
        while (value >= 1024 && index < suffixes.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {suffixes[index]}";
    }
}
