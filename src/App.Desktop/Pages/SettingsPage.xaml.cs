// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using App.Models.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace App_Desktop.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        ThemeComboBox.ItemsSource = Enum.GetValues<AppThemePreference>();
        PerformanceComboBox.ItemsSource = Enum.GetValues<PerformanceMode>();

        var settings = await AppServices.Settings.LoadAsync();
        ThemeComboBox.SelectedItem = settings.Theme;
        PerformanceComboBox.SelectedItem = settings.PerformanceMode;
        TokenStatusTextBlock.Text = settings.HasHuggingFaceToken ? "Token saved securely in Windows Credential Manager." : "No token saved.";
        DataPathTextBlock.Text = $"App data: {AppServices.Paths.RootDirectory}";
        await RenderDevicesAsync();
    }

    private async void SavePreferences_Click(object sender, RoutedEventArgs e)
    {
        var settings = await AppServices.Settings.LoadAsync();
        await AppServices.Settings.SaveAsync(settings with
        {
            Theme = ThemeComboBox.SelectedItem is AppThemePreference theme ? theme : AppThemePreference.System,
            PerformanceMode = PerformanceComboBox.SelectedItem is PerformanceMode mode ? mode : PerformanceMode.Auto
        });
        await ShowMessageAsync("Preferences saved", "Restart the app if the theme does not update immediately.");
    }

    private async void SaveToken_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TokenPasswordBox.Password))
        {
            await ShowMessageAsync("Token needed", "Paste a Hugging Face access token before saving.");
            return;
        }

        await AppServices.Settings.SaveHuggingFaceTokenAsync(TokenPasswordBox.Password);
        TokenPasswordBox.Password = string.Empty;
        TokenStatusTextBlock.Text = "Token saved securely in Windows Credential Manager.";
    }

    private async void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        await AppServices.Settings.ClearHuggingFaceTokenAsync();
        TokenStatusTextBlock.Text = "No token saved.";
    }

    private async void DetectHardware_Click(object sender, RoutedEventArgs e)
    {
        await RenderDevicesAsync();
    }

    private async Task RenderDevicesAsync()
    {
        DevicesStackPanel.Children.Clear();
        try
        {
            var devices = await AppServices.HardwareDetection.DetectAsync();
            foreach (var device in devices)
            {
                DevicesStackPanel.Children.Add(new TextBlock
                {
                    Text = $"{(device.IsPreferred ? "Recommended: " : string.Empty)}{device.Name} - {device.Backend} - {device.Detail}",
                    Style = (Style)Application.Current.Resources["MutedTextBlockStyle"]
                });
            }
        }
        catch (Exception ex)
        {
            DevicesStackPanel.Children.Add(new TextBlock { Text = $"Hardware detection failed. CPU fallback is still available. {ex.Message}" });
        }
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var diagnostics = await AppServices.Diagnostics.CreateDiagnosticInfoAsync();
        var package = new DataPackage();
        package.SetText(diagnostics);
        Clipboard.SetContent(package);
        await ShowMessageAsync("Diagnostics copied", "Diagnostic info is on the clipboard.");
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo { FileName = AppServices.Paths.LogsDirectory, UseShellExecute = true });
    }

    private async Task ShowMessageAsync(string title, string message)
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
}
