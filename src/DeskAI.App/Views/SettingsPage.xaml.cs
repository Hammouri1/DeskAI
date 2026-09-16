using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.Core.Ai;
using DeskAI.Core.Backup;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskAI.App.Views;

public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;
    private readonly IBackupFilePickerService _backupFiles;

    public SettingsPage(SettingsViewModel viewModel, IBackupFilePickerService backupFiles)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _backupFiles = backupFiles;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    /// <summary>The Windows save dialog, then the file. Cancel writes nothing.</summary>
    private async void OnSaveBackupClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainAppWindow is not { } window)
        {
            return;
        }

        var picked = await _backupFiles.PickSaveAsync(
            WinRT.Interop.WindowNative.GetWindowHandle(window), _viewModel.SuggestedBackupFileName);
        if (picked.WasPicked)
        {
            await _viewModel.ExportBackupAsync(picked.Path!);
        }
    }

    /// <summary>
    /// The Windows open dialog, then a preview of exactly what the file would add and skip.
    /// Only Restore in that dialog adds anything.
    /// </summary>
    private async void OnRestoreBackupClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainAppWindow is not { } window)
        {
            return;
        }

        var picked = await _backupFiles.PickOpenAsync(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (!picked.WasPicked)
        {
            return;
        }

        var preview = await _viewModel.PreviewRestoreAsync(picked.Path!);
        if (preview is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Restore from this backup?",
            Content = DescribeRestore(preview),
            PrimaryButtonText = preview.AddsAnything
                ? $"Restore {preview.RulesToAdd + preview.SearchesToAdd}"
                : "Restore",
            IsPrimaryButtonEnabled = preview.AddsAnything,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.RestoreBackupAsync(picked.Path!);
        }
    }

    /// <summary>Asks in plain words what will be forgotten and what will not, then forgets.</summary>
    private async void OnStartFreshClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Start fresh?",
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 480,
                Text = "DeskAI will forget every connected folder and its permissions, every rule and saved search, "
                    + "your AI choice and every saved key, its check history, and the wallpaper it remembered. "
                    + "It will stop keeping running after the window is closed.\n\n"
                    + "Your files are not touched: anything DeskAI tidied stays where it is, and your wallpaper stays as it is now. "
                    + "This cannot be undone.",
            },
            PrimaryButtonText = "Forget everything",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await _viewModel.StartFreshAsync();
        }
    }

    private StackPanel DescribeRestore(RestorePreview preview)
    {
        var panel = new StackPanel { Spacing = 10, MaxWidth = 480 };
        if (!preview.AddsAnything)
        {
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Everything in this file is already here, or cannot be used. Nothing would be added.",
            });
        }

        AddGroup(panel, "Rules", preview.Rules);
        AddGroup(panel, "Saved searches", preview.Searches);
        panel.Children.Add(new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(0, 4, 4, 0),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Background = (Brush)Application.Current.Resources["DeskSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["DeskAccentBrush"],
            Child = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "Restored rules start switched off. Nothing moves until you turn a rule on and press Tidy. Nothing already here is replaced.",
            },
        });
        return panel;
    }

    private void AddGroup(StackPanel panel, string title, IReadOnlyList<RestoreLine> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        panel.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.Resources["SectionTitleStyle"],
        });
        foreach (var line in lines)
        {
            var item = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 6) };
            item.Children.Add(new TextBlock { Text = line.Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            if (line.Description.Length > 0)
            {
                item.Children.Add(new TextBlock
                {
                    Text = line.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["CaptionStyle"],
                });
            }

            if (line.SkipReason is { } reason)
            {
                item.Children.Add(new TextBlock
                {
                    Text = reason,
                    TextWrapping = TextWrapping.Wrap,
                    Style = (Style)Application.Current.Resources["CaptionStyle"],
                    Foreground = (Brush)Application.Current.Resources["DeskCautionBrush"],
                });
            }

            panel.Children.Add(item);
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await _viewModel.InitializeAsync();
    }

    private async void OnSavePrivacyClick(object sender, RoutedEventArgs e)
    {
        var expansions = _viewModel.PendingExpansions();
        if (expansions.Count > 0)
        {
            var names = string.Join(", ", expansions.Select(DisplayName));
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Share more information?",
                Content = $"You chose to allow: {names}. Nothing is sent while online AI is off. Private and protected files are always left out.",
                PrimaryButtonText = "Allow and save",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _viewModel.SavePrivacyAsync();
    }

    private async void OnSaveProviderClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedModeIndex == (int)AiMode.Cloud)
        {
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Turn on online AI?",
                Content = _viewModel.CloudConsentSummary(),
                PrimaryButtonText = $"Turn on {_viewModel.SelectedProviderName}",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        await _viewModel.SaveProviderAsync(CloudKeyBox.Password);
        // Managed strings cannot be forcibly erased, so clear the box as soon as possible.
        CloudKeyBox.Password = string.Empty;
    }

    private async void OnRemoveCloudKeyClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.RemoveCloudKeyAsync();
        CloudKeyBox.Password = string.Empty;
    }

    private static string DisplayName(DisclosureCategory category) => category switch
    {
        DisclosureCategory.Extension => "file type",
        DisclosureCategory.Metadata => "file size and last changed date",
        DisclosureCategory.FileName => "file name",
        DisclosureCategory.FolderNames => "folder names",
        DisclosureCategory.FullPath => "full file location",
        DisclosureCategory.ExtractedContent => "text inside files",
        DisclosureCategory.ImageContent => "images inside files",
        _ => category.ToString(),
    };
}
