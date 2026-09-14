using DeskAI.App.ViewModels;
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
