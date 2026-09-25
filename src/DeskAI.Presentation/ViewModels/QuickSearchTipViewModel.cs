using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.QuickSearch;

namespace DeskAI.App.ViewModels;

/// <summary>The line on Home and Search that tells people about Ctrl + Alt + Space. Closed once, gone on both.</summary>
public sealed class QuickSearchTipViewModel(QuickSearchSettingsService settings) : ObservableObject
{
    public const string Text = "Tip: press Ctrl + Alt + Space anywhere to find a file.";

    private readonly QuickSearchSettingsService _settings = settings;
    private bool _isShown;

    public bool IsShown { get => _isShown; private set => SetProperty(ref _isShown, value); }

    public async Task LoadAsync()
    {
        var stored = await _settings.LoadAsync().ConfigureAwait(true);
        IsShown = stored.IsOn && !stored.TipDismissed;
    }

    public async Task DismissAsync()
    {
        IsShown = false;
        await _settings.DismissTipAsync().ConfigureAwait(true);
    }
}
