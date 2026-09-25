using System.ComponentModel;
using DeskAI.App.Services;
using DeskAI.App.ViewModels;
using DeskAI.App.Views.Buddies;
using DeskAI.Core.QuickSearch;
using DeskAI.Core.Roots;
using DeskAI.Core.Templates;
using DeskAI.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace DeskAI.App.Views;

/// <summary>One line in the pack preview: a search or rule, and why it will be skipped if it will.</summary>
public sealed class PackLine(string name, string description, string? note)
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    public string Note { get; } = note ?? string.Empty;

    public Visibility NoteVisibility { get; } = note is null ? Visibility.Collapsed : Visibility.Visible;
}

public sealed partial class WorkspacePage : Page
{
    private readonly IPicturePickerService _picturePicker;

    public WorkspacePage(WorkspaceViewModel viewModel, IPicturePickerService picturePicker)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _picturePicker = picturePicker;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Lets the person pick one picture, shows it, and makes it the wallpaper only if the
    /// dialog's button is pressed.
    /// </summary>
    private async void OnChoosePictureClick(object sender, RoutedEventArgs e)
    {
        if (((App)Application.Current).MainAppWindow is not { } window)
        {
            return;
        }

        var picked = await _picturePicker.PickPictureAsync(WinRT.Interop.WindowNative.GetWindowHandle(window));
        if (picked.Outcome == FolderPickOutcome.Unavailable)
        {
            await ViewModel.PreviewWallpaperAsync(null);
            return;
        }

        if (!picked.WasPicked)
        {
            return;
        }

        var preview = await ViewModel.PreviewWallpaperAsync(picked.Path);
        if (preview is null)
        {
            return;
        }

        WallpaperPreviewImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(preview.Path));
        WallpaperPreviewName.Text = preview.Name;
        WallpaperPreviewCurrent.Text = $"Windows shows now: {preview.CurrentDescription}.";
        WallpaperDialog.Title = $"Make {preview.Name} your wallpaper?";
        WallpaperDialog.XamlRoot = XamlRoot;
        if (await WallpaperDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.UseWallpaperAsync(preview);
        }
    }

    /// <summary>
    /// Asks before connecting one of the person's own folders, then hands it to Organize, which
    /// asks again before tidying. Connecting reads names, sizes, and dates and moves nothing.
    /// </summary>
    private async void OnFolderClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PersonalFolderKind kind } || ViewModel.Folders.Find(kind) is not { } row)
        {
            return;
        }

        if (!row.IsConnected && !await PersonalFolderDialogs.ConfirmConnectAsync(XamlRoot, row.Name))
        {
            return;
        }

        if (await ViewModel.ConnectFolderAsync(kind) is not null)
        {
            GoTo("organize", fresh: true);
        }
    }

    public WorkspaceViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
        // The page and its view model are made fresh for each visit, so one handler per page is all there ever is.
        ShowStage(popIn: false);
        ViewModel.QuickSearch.PropertyChanged += OnQuickSearchCardChanged;
        Unloaded += (_, _) => ViewModel.QuickSearch.PropertyChanged -= OnQuickSearchCardChanged;
    }

    /// <summary>
    /// Shows exactly what a pack would add, and adds it only if Add is pressed.
    /// </summary>
    /// <remarks>
    /// The preview is worked out fresh when the button is pressed, and the view model works it
    /// out again when Add is pressed, so a dialog left open while something changed cannot
    /// cause anything to be overwritten.
    /// </remarks>
    private async void OnPackClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packId })
        {
            return;
        }

        var preview = await ViewModel.PreviewPackAsync(packId);
        PackDialog.Title = $"Add the {preview.Pack.Name} starter pack?";
        PackSearches.ItemsSource = Lines(preview, StarterPackItemKind.Search);
        var rules = Lines(preview, StarterPackItemKind.Rule);
        PackRules.ItemsSource = rules;
        PackRulesTitle.Visibility = rules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        PackNothingToAdd.Visibility = preview.AddsAnything ? Visibility.Collapsed : Visibility.Visible;
        PackDialog.IsPrimaryButtonEnabled = preview.AddsAnything;
        PackDialog.XamlRoot = XamlRoot;

        if (await PackDialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.AddPackAsync(packId);
        }
    }

    private void OnOpenInSearchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid savedSearchId })
        {
            return;
        }

        ViewModel.OpenInSearch(savedSearchId);
        GoTo("search", fresh: true);
    }

    private void OnGoToSearchClick(object sender, RoutedEventArgs e) => GoTo("search", fresh: false);

    private void OnGoToOrganizeClick(object sender, RoutedEventArgs e) => GoTo("organize", fresh: false);

    /// <summary>A "#RRGGBB" swatch from a look card as a brush, for the card's colour strip.</summary>
    public static Microsoft.UI.Xaml.Media.SolidColorBrush Swatch(string hex) =>
        new(DeskAI.App.Services.WindowsAppearanceApplier.ToColor(hex));

    /// <summary>Only a change the person made reaches the switch, never the page setting its own value.</summary>
    private async void OnQuickSearchToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.QuickSearch.IsOn)
        {
            await ViewModel.QuickSearch.SetOnAsync(toggle.IsOn);
        }
    }

    /// <summary>Only a change the person made reaches the switch, never the page setting its own value.</summary>
    private async void OnBuddyMotionToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && toggle.IsOn != ViewModel.QuickSearch.LetsBuddyMove)
        {
            await ViewModel.QuickSearch.SetMotionAsync(toggle.IsOn);
        }
    }

    /// <summary>Only a choice the person made reaches the switch, never the page setting its own value.</summary>
    private async void OnShortcutChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: >= 0 } box && box.SelectedIndex != ViewModel.QuickSearch.ShortcutIndex)
        {
            await ViewModel.QuickSearch.SetShortcutAsync((QuickSearchShortcut)box.SelectedIndex);
        }
    }

    /// <summary>Clicking a face, or moving to it with the arrow keys, chooses that buddy at once.</summary>
    private async void OnBuddyFaceChosen(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons { SelectedIndex: >= 0 } faces && faces.SelectedIndex != ViewModel.QuickSearch.ChosenIndex)
        {
            await ViewModel.QuickSearch.ChooseBuddyAsync((SearchBuddy)faces.SelectedIndex);
        }
    }

    private void OnFaceBackgroundLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Ellipse { Tag: string stage } face)
        {
            face.Fill = BuddyStageBrushes.For(stage);
        }
    }

    private void OnQuickSearchCardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickSearchCardViewModel.Chosen))
        {
            ShowStage(popIn: true);
            AnnounceChosenBuddy();
        }
    }

    /// <summary>
    /// Tells a screen reader the new buddy's name and hello line. A polite live setting alone is
    /// never announced; the live-region event is what makes Narrator read the changed text.
    /// </summary>
    private void AnnounceChosenBuddy()
    {
        // After the bindings have caught up, so the text read out is the new buddy's.
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var line in new[] { ChosenBuddyName, ChosenBuddyHello })
            {
                FrameworkElementAutomationPeer.FromElement(line)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            }
        });
    }

    /// <summary>The chosen buddy, large, on its own background; it pops in when it changes and buddies may move.</summary>
    private void ShowStage(bool popIn)
    {
        var chosen = ViewModel.QuickSearch.Chosen;
        if (BuddyStageHost.Content is BuddyControl { Tag: SearchBuddy current } && current == chosen.Buddy)
        {
            return;
        }

        BuddyStage.Background = BuddyStageBrushes.For(chosen.Stage);
        var buddy = BuddyFactory.Create(chosen.Buddy, ViewModel.QuickSearch.Motion);
        buddy.Tag = chosen.Buddy;
        BuddyStageHost.Content = buddy;
        if (popIn && ViewModel.QuickSearch.Motion.IsOn)
        {
            BuddyAnimations.PopIn(BuddyStageHost, TimeSpan.Zero);
        }
    }

    /// <summary>Draws each face's buddy standing still in its Idle pose.</summary>
    private void OnBuddyPictureLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ContentControl { Tag: BuddyTileViewModel tile, Content: null } host)
        {
            var buddy = BuddyFactory.Create(tile.Buddy, motion: null);
            buddy.HoldsStill = true;
            host.Content = buddy;
        }
    }

    private async void OnThemeModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is RadioButtons { SelectedIndex: >= 0 } choice)
        {
            await ViewModel.ChooseThemeModeAsync(choice.SelectedIndex);
        }
    }

    /// <summary>
    /// Shows exactly which folders a template would make, and makes them only if the
    /// confirming button is pressed.
    /// </summary>
    /// <remarks>
    /// If the folder may not be tidied yet, the same permission dialog Organize uses is shown
    /// first, because making a folder is a change to the folder. The view model works the list
    /// out again when Make is pressed; if the folder changed meanwhile, the fresh list is shown
    /// again instead of anything being guessed.
    /// </remarks>
    private async void OnTemplateClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string cardId })
        {
            return;
        }

        var preview = await ViewModel.PreviewTemplateAsync(cardId);
        if (preview is { NeedsPermission: true })
        {
            if (ViewModel.SelectedTemplateFolder is not { } folder || !await ConfirmTidyPermissionAsync(folder))
            {
                return;
            }

            await ViewModel.AllowTemplateFolderTidyAsync();
            preview = await ViewModel.PreviewTemplateAsync(cardId);
        }

        while (preview is { NeedsPermission: false })
        {
            ShowTemplatePreview(preview);
            if (await TemplateDialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            preview = await ViewModel.MakeTemplateAsync(preview);
        }
    }

    private void ShowTemplatePreview(FolderTemplatePreview preview)
    {
        var willMake = preview.Lines.Where(line => line.Kind == FolderTemplateLineKind.WillMake).Select(line => line.Name).ToList();
        var already = preview.Lines.Where(line => line.Kind == FolderTemplateLineKind.AlreadyThere).Select(line => line.Name).ToList();
        var blocked = preview.Lines
            .Where(line => line.Kind == FolderTemplateLineKind.Blocked)
            .Select(line => new PackLine(line.Name, string.Empty, line.Reason))
            .ToList();
        TemplateDialog.Title = preview.Template.Id == FolderTemplate.OwnId
            ? $"Make your folders in {preview.FolderName}?"
            : $"Make the {preview.Template.Name} folders in {preview.FolderName}?";
        TemplateNewFolders.ItemsSource = willMake;
        TemplateAlreadyFolders.ItemsSource = already;
        TemplateBlockedFolders.ItemsSource = blocked;
        TemplateWillMake.Visibility = willMake.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TemplateAlready.Visibility = already.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TemplateBlocked.Visibility = blocked.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TemplateNothingToMake.Visibility = preview.CanMake ? Visibility.Collapsed : Visibility.Visible;
        TemplateDialog.PrimaryButtonText = willMake.Count == 1 ? "Make 1 folder" : $"Make {willMake.Count} folders";
        TemplateDialog.IsPrimaryButtonEnabled = preview.CanMake;
        TemplateDialog.XamlRoot = XamlRoot;
    }

    /// <summary>The same permission dialog Organize shows, because it is the same permission.</summary>
    private async Task<bool> ConfirmTidyPermissionAsync(TemplateFolderOption folder)
    {
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"Allow DeskAI to tidy {folder.Name}?",
            Content = $"{folder.Path}\n\nDeskAI may move loose files at the top of this folder into folders inside it, and make empty folders there.\n"
                + "It never deletes anything, never touches files in subfolders, and never moves anything out of this folder.\n"
                + "Nothing changes until you press Tidy, Make, or Undo.\n\nYou can take this back at any time.",
            PrimaryButtonText = "Allow tidying",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        return await confirm.ShowAsync() == ContentDialogResult.Primary;
    }

    private static void GoTo(string route, bool fresh)
    {
        if (((App)Application.Current).MainAppWindow is MainWindow window)
        {
            window.GoTo(route, fresh);
        }
    }

    private static List<PackLine> Lines(StarterPackPreview preview, StarterPackItemKind kind) =>
        preview.Items
            .Where(item => item.Kind == kind)
            .Select(item => new PackLine(item.Name, item.Description, item.SkipReason))
            .ToList();
}
