using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using App_Desktop.Pages;
using App.Models.Domain;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace App_Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        ApplyWindowIcon();
        NavFrame.Navigate(typeof(HomePage));
        RootGrid.Loaded += MainWindow_Loaded;
    }

    private void ApplyWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        AppWindow.SetIcon(iconPath);

        var hwnd = WindowNative.GetWindowHandle(this);
        SetIcon(hwnd, IconSize.Small, iconPath);
        SetIcon(hwnd, IconSize.Large, iconPath);
    }

    private static void SetIcon(IntPtr hwnd, IconSize size, string iconPath)
    {
        var icon = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 0, 0, LoadFromFile | DefaultSize | SharedIcon);
        if (icon != IntPtr.Zero)
        {
            SendMessage(hwnd, SetWindowIcon, new IntPtr((int)size), icon);
        }
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "transcription":
                    NavFrame.Navigate(typeof(HomePage));
                    break;
                case "live":
                    NavFrame.Navigate(typeof(LiveTranscriptionPage));
                    break;
                case "models":
                    NavFrame.Navigate(typeof(ModelsPage));
                    break;
                case "history":
                    NavFrame.Navigate(typeof(HistoryPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowIcon();

        var settings = await AppServices.Settings.LoadAsync();
        ApplyTheme(settings.Theme);

        if (!settings.HasCompletedOnboarding)
        {
            await ShowOnboardingAsync(settings);
        }
    }

    private void ApplyTheme(AppThemePreference theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            AppThemePreference.Light => ElementTheme.Light,
            AppThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private async System.Threading.Tasks.Task ShowOnboardingAsync(AppSettings settings)
    {
        var models = AppServices.ModelManager.GetSupportedModels();
        var modelBox = new ComboBox
        {
            Header = "Choose a first model",
            DisplayMemberPath = nameof(SpeechModelDefinition.DisplayName),
            ItemsSource = models,
            SelectedItem = models.FirstOrDefault(model => model.IsRecommended) ?? models.FirstOrDefault(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var performanceBox = new ComboBox
        {
            Header = "Performance mode",
            ItemsSource = Enum.GetValues<PerformanceMode>(),
            SelectedItem = settings.PerformanceMode,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var tokenBox = new PasswordBox
        {
            Header = "Hugging Face token",
            PlaceholderText = "Optional, useful for gated models and diarization"
        };

        var content = new StackPanel { Spacing = 14, MinWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = "Your audio stays on this device. Choose a local model and the app will only use the network when downloading models.",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(modelBox);
        content.Children.Add(performanceBox);
        content.Children.Add(tokenBox);

        var dialog = new ContentDialog
        {
            Title = "Set up QuietScribe",
            Content = content,
            PrimaryButtonText = "Start",
            CloseButtonText = "Skip",
            XamlRoot = RootGrid.XamlRoot,
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            if (!string.IsNullOrWhiteSpace(tokenBox.Password))
            {
                await AppServices.Settings.SaveHuggingFaceTokenAsync(tokenBox.Password);
            }

            var selectedModel = modelBox.SelectedItem as SpeechModelDefinition;
            var selectedMode = performanceBox.SelectedItem is PerformanceMode mode ? mode : PerformanceMode.Auto;
            await AppServices.Settings.SaveAsync(settings with
            {
                DefaultModelRepoId = selectedModel?.RepoId,
                PerformanceMode = selectedMode,
                HasCompletedOnboarding = true,
                HasHuggingFaceToken = !string.IsNullOrWhiteSpace(tokenBox.Password) || settings.HasHuggingFaceToken
            });
        }
        else
        {
            await AppServices.Settings.SaveAsync(settings with { HasCompletedOnboarding = true });
        }
    }

    private enum IconSize
    {
        Small = 0,
        Large = 1
    }

    private const int SetWindowIcon = 0x0080;
    private const int ImageIcon = 1;
    private const int LoadFromFile = 0x00000010;
    private const int DefaultSize = 0x00000040;
    private const int SharedIcon = 0x00008000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr instance,
        string name,
        int type,
        int desiredWidth,
        int desiredHeight,
        int loadFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);
}
