using DeskAI.App.ViewModels;
using DeskAI.Core.Templates;
using DeskAI.Core.Workspace;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
    public WorkspacePage(WorkspaceViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    public WorkspaceViewModel ViewModel { get; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await ViewModel.InitializeAsync();
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
