using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Appearance;
using DeskAI.Core.Desktop;
using DeskAI.Core.Search;
using DeskAI.Core.Templates;
using DeskAI.Core.Tidy;
using DeskAI.Core.Workspace;

namespace DeskAI.App.ViewModels;

/// <summary>
/// One folder template's card. The result of making it is written on the card itself.
/// </summary>
/// <remarks>
/// The "Your own folders" card is the one place a person types folder names. What they type
/// stays here until they press the button; it is checked then, and again by the safety policy.
/// </remarks>
public sealed class FolderTemplateCardViewModel(string id, string name, string folderList) : ObservableObject
{
    private string _result = string.Empty;
    private string _typedNames = string.Empty;

    public string Id { get; } = id;

    public string Name { get; } = name;

    /// <summary>The folder names on one line, empty for the card a person fills in.</summary>
    public string FolderList { get; } = folderList;

    public bool IsOwn => Id == FolderTemplate.OwnId;

    public bool IsBuiltIn => !IsOwn;

    /// <summary>What the person typed on the "Your own folders" card, unchecked.</summary>
    public string TypedNames
    {
        get => _typedNames;
        set => SetProperty(ref _typedNames, value);
    }

    public string Result
    {
        get => _result;
        set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(Result);
}

/// <summary>A connected folder a template could be made in.</summary>
public sealed record TemplateFolderOption(Guid Id, string Name, string Path, bool CanTidy)
{
    public override string ToString() => Name;
}

/// <summary>One look's card: its name, a line, two swatches, and whether it is the chosen one.</summary>
public sealed class LookCardViewModel(DeskLook look) : ObservableObject
{
    private bool _isChosen;

    public string Id { get; } = look.Id;

    public string Name { get; } = look.Name;

    public string Summary { get; } = look.Summary;

    /// <summary>"#RRGGBB" swatches, so the card can show the look before it is chosen.</summary>
    public string DarkGround { get; } = look.Dark.Ground;

    public string DarkSurface { get; } = look.Dark.SurfaceRaised;

    public string LightGround { get; } = look.Light.Ground;

    public string LightSurface { get; } = look.Light.Line;

    public bool IsChosen
    {
        get => _isChosen;
        set
        {
            if (SetProperty(ref _isChosen, value))
            {
                OnPropertyChanged(nameof(ChosenText));
            }
        }
    }

    public string ChosenText => IsChosen ? "Chosen" : string.Empty;
}

/// <summary>
/// One pinned search as a tile: its name, a number for the big slot when there is one, and
/// the words beside or instead of it. Words never go in the number slot: they were clipped
/// there (owner's screenshot, 2026-09-16).
/// </summary>
public sealed record PinnedSearchTileViewModel(Guid Id, string Name, string Number, string Words)
{
    public bool HasNumber => Number.Length > 0;

    /// <summary>The whole count in one line, as a screen reader or a test reads it.</summary>
    public string Count => HasNumber ? $"{Number} {Words}" : Words;
}

/// <summary>A saved search that is not pinned, offered with a Pin button.</summary>
public sealed record UnpinnedSearchViewModel(Guid Id, string Name, string Phrase);

/// <summary>
/// One starter pack's card. The result of adding it is shown on the card itself, because the
/// answer to pressing a button belongs next to that button.
/// </summary>
public sealed class StarterPackCardViewModel(string id, string name, string summary) : ObservableObject
{
    private string _result = string.Empty;

    public string Id { get; } = id;

    public string Name { get; } = name;

    public string Summary { get; } = summary;

    public string Result
    {
        get => _result;
        set
        {
            if (SetProperty(ref _result, value))
            {
                OnPropertyChanged(nameof(HasResult));
            }
        }
    }

    public bool HasResult => !string.IsNullOrEmpty(Result);
}

/// <summary>
/// The logic behind My workspace: starter packs, pinned searches, and folder templates.
/// </summary>
/// <remarks>
/// <para>
/// Nothing on this page moves a file. Adding a pack creates saved searches and switched-off
/// rules; pinning changes a flag on a saved search; a tile's count is an ordinary search over
/// what DeskAI remembers. The one thing here that changes a folder is a template, which makes
/// empty folders — after a preview and a yes, with the tidy permission, through the same
/// executor Tidy uses — and can be undone.
/// </para>
/// <para>
/// Everything that changes something is done in two steps with the person between them:
/// <see cref="PreviewPackAsync"/> and <see cref="PreviewTemplateAsync"/> say what would happen,
/// and only <see cref="AddPackAsync"/> and <see cref="MakeTemplateAsync"/>, after the view's
/// dialog, do it.
/// </para>
/// </remarks>
public sealed class WorkspaceViewModel : ObservableObject
{
    private readonly StarterPackService _packs;
    private readonly PinnedSearchService _pins;
    private readonly FolderTemplateService _templates;
    private readonly ConnectedFolderService _folders;
    private readonly TidyPermissionService _permission;
    private readonly ISavedSearchRepository _searches;
    private readonly IAppearanceSettingsRepository _appearance;
    private readonly IAppearanceApplier _applier;
    private readonly WallpaperService _wallpaper;
    private readonly IKnownFolders _knownFolders;
    private readonly OrganizeRequest _organize;
    private readonly SearchRequest _request;
    private readonly IClock _clock;
    private string _wallpaperMessage = string.Empty;
    private WallpaperRestore? _restore;
    private string _desktopStatus = string.Empty;
    private bool _isDesktopConnected;
    private AppearanceSettings _chosenAppearance = AppearanceSettings.Default;
    private string _lookMessage = string.Empty;
    private string _pinsCaption = string.Empty;
    private string _pinMessage = string.Empty;
    private bool _canPinMore = true;
    private bool _isBusy;
    private TemplateFolderOption? _selectedTemplateFolder;
    private LastFolderTemplate? _lastTemplate;
    private string _lastTemplateSummary = string.Empty;
    private string _templateMessage = string.Empty;

    // The history lookup started by choosing a folder, kept so callers can wait for it.
    private Task _pendingLast = Task.CompletedTask;

    public WorkspaceViewModel(
        StarterPackService packs,
        PinnedSearchService pins,
        FolderTemplateService templates,
        ConnectedFolderService folders,
        TidyPermissionService permission,
        ISavedSearchRepository searches,
        IAppearanceSettingsRepository appearance,
        IAppearanceApplier applier,
        WallpaperService wallpaper,
        IKnownFolders knownFolders,
        OrganizeRequest organize,
        SearchRequest request,
        IClock clock)
    {
        _packs = packs;
        _pins = pins;
        _templates = templates;
        _folders = folders;
        _permission = permission;
        _searches = searches;
        _appearance = appearance;
        _applier = applier;
        _wallpaper = wallpaper;
        _knownFolders = knownFolders;
        _organize = organize;
        _request = request;
        _clock = clock;
        PutWallpaperBackCommand = new AsyncRelayCommand(PutWallpaperBackAsync, () => CanPutWallpaperBack && !IsBusy);
        Looks = DeskLookCatalog.All.Select(look => new LookCardViewModel(look)).ToArray();
        ChooseLookCommand = new AsyncRelayCommand<string>(ChooseLookAsync, _ => !IsBusy);
        Packs = StarterPackCatalog.All
            .Select(pack => new StarterPackCardViewModel(pack.Id, pack.Name, pack.Summary))
            .ToArray();
        Templates = FolderTemplateCatalog.All
            .Select(template => new FolderTemplateCardViewModel(template.Id, template.Name, template.FolderList))
            .Append(new FolderTemplateCardViewModel(FolderTemplate.OwnId, "Your own folders", string.Empty))
            .ToArray();
        PinCommand = new AsyncRelayCommand<Guid>(PinAsync, _ => !IsBusy);
        UnpinCommand = new AsyncRelayCommand<Guid>(UnpinAsync, _ => !IsBusy);
        UndoTemplateCommand = new AsyncRelayCommand(UndoTemplateAsync, () => CanUndoTemplate && !IsBusy);
    }

    public IReadOnlyList<StarterPackCardViewModel> Packs { get; }

    /// <summary>The five built-in templates, then the card a person fills in.</summary>
    public IReadOnlyList<FolderTemplateCardViewModel> Templates { get; }

    public ObservableCollection<PinnedSearchTileViewModel> Pins { get; } = [];

    public ObservableCollection<UnpinnedSearchViewModel> OtherSearches { get; } = [];

    /// <summary>The connected folders a template can be made in. Connecting belongs to Organize.</summary>
    public ObservableCollection<TemplateFolderOption> TemplateFolders { get; } = [];

    public AsyncRelayCommand<Guid> PinCommand { get; }

    public AsyncRelayCommand<Guid> UnpinCommand { get; }

    public AsyncRelayCommand UndoTemplateCommand { get; }

    /// <summary>DeskAI's looks, in the catalog's order. Exactly one is chosen.</summary>
    public IReadOnlyList<LookCardViewModel> Looks { get; }

    public AsyncRelayCommand<string> ChooseLookCommand { get; }

    /// <summary>The theme choice as the page's list shows it: Follow Windows, Light, Dark.</summary>
    public static IReadOnlyList<string> ThemeModeNames { get; } = ["Follow Windows", "Light", "Dark"];

    public int ThemeModeIndex => (int)_chosenAppearance.Mode;

    public string ChosenLookName => _chosenAppearance.Look.Name;

    /// <summary>Why a look could not be saved, when it could not. Empty otherwise.</summary>
    public string LookMessage
    {
        get => _lookMessage;
        private set
        {
            if (SetProperty(ref _lookMessage, value))
            {
                OnPropertyChanged(nameof(HasLookMessage));
            }
        }
    }

    public bool HasLookMessage => !string.IsNullOrEmpty(LookMessage);

    public AsyncRelayCommand PutWallpaperBackCommand { get; }

    /// <summary>The answer to the last wallpaper press, shown on the wallpaper card.</summary>
    public string WallpaperMessage
    {
        get => _wallpaperMessage;
        private set
        {
            if (SetProperty(ref _wallpaperMessage, value))
            {
                OnPropertyChanged(nameof(HasWallpaperMessage));
            }
        }
    }

    public bool HasWallpaperMessage => !string.IsNullOrEmpty(WallpaperMessage);

    public bool CanPutWallpaperBack => _restore is not null;

    /// <summary>"Put the old wallpaper back: holiday.jpg", or empty when there is none to put back.</summary>
    public string PutBackSummary => _restore is null ? string.Empty : $"Your old wallpaper: {_restore.PreviousDescription}.";

    /// <summary>Said before Put back is pressed, when Windows now shows something DeskAI did not set.</summary>
    public string PutBackNote => _restore is { WindowsShowsSomethingElse: true }
        ? "Windows now shows a different wallpaper than the one DeskAI set. Put back restores the old one anyway."
        : string.Empty;

    public bool HasPutBackNote => !string.IsNullOrEmpty(PutBackNote);

    /// <summary>What the Desktop card says: not found, not connected yet, or connected.</summary>
    public string DesktopStatus
    {
        get => _desktopStatus;
        private set => SetProperty(ref _desktopStatus, value);
    }

    public bool IsDesktopConnected
    {
        get => _isDesktopConnected;
        private set => SetProperty(ref _isDesktopConnected, value);
    }

    /// <summary>Whether Windows told DeskAI where the Desktop is at all.</summary>
    public bool HasDesktop => _knownFolders.Desktop is not null;

    public bool HasTemplateFolders => TemplateFolders.Count > 0;

    public bool HasNoTemplateFolders => TemplateFolders.Count == 0;

    /// <summary>The folder templates are made in. Changing it looks up that folder's last template run.</summary>
    public TemplateFolderOption? SelectedTemplateFolder
    {
        get => _selectedTemplateFolder;
        set
        {
            if (SetProperty(ref _selectedTemplateFolder, value))
            {
                TemplateMessage = string.Empty;
                _pendingLast = LoadLastTemplateAsync(value);
            }
        }
    }

    /// <summary>
    /// The folder's last template run, found in DeskAI's history, so it can still be undone
    /// after DeskAI was closed and opened again.
    /// </summary>
    public string LastTemplateSummary
    {
        get => _lastTemplateSummary;
        private set
        {
            if (SetProperty(ref _lastTemplateSummary, value))
            {
                OnPropertyChanged(nameof(CanUndoTemplate));
                UndoTemplateCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanUndoTemplate => _lastTemplate is not null;

    /// <summary>The answer to pressing Undo, shown beside it.</summary>
    public string TemplateMessage
    {
        get => _templateMessage;
        private set
        {
            if (SetProperty(ref _templateMessage, value))
            {
                OnPropertyChanged(nameof(HasTemplateMessage));
            }
        }
    }

    public bool HasTemplateMessage => !string.IsNullOrEmpty(TemplateMessage);

    public bool HasPins => Pins.Count > 0;

    public bool HasNoPins => Pins.Count == 0;

    public bool HasOtherSearches => OtherSearches.Count > 0;

    public static int MaxPinned => SavedSearch.MaxPinned;

    /// <summary>Where the counts come from and when they were made, so no tile looks live.</summary>
    public string PinsCaption
    {
        get => _pinsCaption;
        private set => SetProperty(ref _pinsCaption, value);
    }

    /// <summary>The answer to pressing Pin, shown beside the list the button is in.</summary>
    public string PinMessage
    {
        get => _pinMessage;
        private set
        {
            if (SetProperty(ref _pinMessage, value))
            {
                OnPropertyChanged(nameof(HasPinMessage));
            }
        }
    }

    public bool HasPinMessage => !string.IsNullOrEmpty(PinMessage);

    public bool CanPinMore
    {
        get => _canPinMore;
        private set => SetProperty(ref _canPinMore, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                PinCommand.NotifyCanExecuteChanged();
                UnpinCommand.NotifyCanExecuteChanged();
                UndoTemplateCommand.NotifyCanExecuteChanged();
                ChooseLookCommand.NotifyCanExecuteChanged();
                PutWallpaperBackCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public async Task InitializeAsync()
    {
        await ReloadAsync().ConfigureAwait(true);
        await ReloadTemplateFoldersAsync().ConfigureAwait(true);
        await LoadAppearanceAsync().ConfigureAwait(true);
        await LoadWallpaperRestoreAsync().ConfigureAwait(true);
        RefreshDesktopStatus();
    }

    /// <summary>
    /// Checks the picture the person picked and says what Windows shows now. Changes nothing.
    /// </summary>
    /// <returns>The preview to show, or null when the reason is already on the card.</returns>
    public async Task<WallpaperPreview?> PreviewWallpaperAsync(string? path)
    {
        WallpaperMessage = string.Empty;
        try
        {
            var preview = await _wallpaper.PreviewAsync(path).ConfigureAwait(true);
            if (!preview.CanUse)
            {
                WallpaperMessage = preview.Problem!;
                return null;
            }

            return preview;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            WallpaperMessage = $"DeskAI stopped safely: {exception.Message}";
            return null;
        }
    }

    /// <summary>Makes the previewed picture the wallpaper. Called only after the page's dialog.</summary>
    public async Task UseWallpaperAsync(WallpaperPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        IsBusy = true;
        try
        {
            var outcome = await _wallpaper.UseAsync(preview).ConfigureAwait(true);
            WallpaperMessage = outcome.Summary;
            await LoadWallpaperRestoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            WallpaperMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Connects the person's Desktop folder, found through Windows, and leaves it for Organize
    /// to open. Called only after the page's dialog.
    /// </summary>
    /// <returns>The connected folder's ID for the page to navigate with, or null with the reason on the card.</returns>
    public async Task<Guid?> ConnectDesktopAsync()
    {
        if (_knownFolders.Desktop is not { } desktop)
        {
            DesktopStatus = "DeskAI could not find your Desktop folder.";
            return null;
        }

        IsBusy = true;
        try
        {
            var already = (await _folders.ListAsync().ConfigureAwait(true))
                .FirstOrDefault(folder => SamePath(folder.Path, desktop));
            Guid id;
            if (already is not null)
            {
                id = already.Id;
            }
            else
            {
                var result = await _folders.ConnectAsync(desktop).ConfigureAwait(true);
                if (!result.IsAllowed || result.Folder is null)
                {
                    DesktopStatus = result.Explanation;
                    return null;
                }

                id = result.Folder.Id;
                await ReloadTemplateFoldersAsync().ConfigureAwait(true);
            }

            _organize.Ask(id);
            RefreshDesktopStatus();
            return id;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            DesktopStatus = $"DeskAI stopped safely: {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PutWallpaperBackAsync()
    {
        IsBusy = true;
        try
        {
            var outcome = await _wallpaper.PutBackAsync().ConfigureAwait(true);
            WallpaperMessage = outcome.Summary;
            await LoadWallpaperRestoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            WallpaperMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadWallpaperRestoreAsync()
    {
        try
        {
            _restore = await _wallpaper.FindRestoreAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            _restore = null;
            WallpaperMessage = $"DeskAI could not read what it changed before: {exception.Message}";
        }

        OnPropertyChanged(nameof(CanPutWallpaperBack));
        OnPropertyChanged(nameof(PutBackSummary));
        OnPropertyChanged(nameof(PutBackNote));
        OnPropertyChanged(nameof(HasPutBackNote));
        PutWallpaperBackCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDesktopStatus()
    {
        var desktop = _knownFolders.Desktop;
        IsDesktopConnected = desktop is not null && TemplateFolders.Any(folder => SamePath(folder.Path, desktop));
        DesktopStatus = desktop is null
            ? "DeskAI could not find your Desktop folder."
            : IsDesktopConnected
                ? "Your Desktop is connected. Tidy it in Organize."
                : "Your Desktop is not connected yet.";
        OnPropertyChanged(nameof(HasDesktop));
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(
            System.IO.Path.TrimEndingDirectorySeparator(first),
            System.IO.Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>Chooses a look: saves it, then repaints DeskAI's window. Changes nothing else.</summary>
    public async Task ChooseLookAsync(string? lookId)
    {
        if (lookId is null || DeskLookCatalog.Find(lookId) is null)
        {
            return;
        }

        await SaveAppearanceAsync(_chosenAppearance with { LookId = lookId }).ConfigureAwait(true);
    }

    /// <summary>Chooses light, dark, or follow Windows, by the index in <see cref="ThemeModeNames"/>.</summary>
    public async Task ChooseThemeModeAsync(int index)
    {
        if (index < 0 || index >= ThemeModeNames.Count || index == ThemeModeIndex)
        {
            return;
        }

        await SaveAppearanceAsync(_chosenAppearance with { Mode = (ThemeMode)index }).ConfigureAwait(true);
    }

    private async Task SaveAppearanceAsync(AppearanceSettings settings)
    {
        IsBusy = true;
        try
        {
            await _appearance.SaveAsync(settings).ConfigureAwait(true);
            ShowAppearance(settings);
            _applier.Apply(settings);
            LookMessage = string.Empty;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            LookMessage = $"DeskAI could not save that look: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAppearanceAsync()
    {
        try
        {
            ShowAppearance(await _appearance.LoadAsync().ConfigureAwait(true));
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            ShowAppearance(AppearanceSettings.Default);
            LookMessage = $"DeskAI could not read your look, so it is showing the usual one: {exception.Message}";
        }
    }

    private void ShowAppearance(AppearanceSettings settings)
    {
        _chosenAppearance = settings;
        foreach (var card in Looks)
        {
            card.IsChosen = card.Id == settings.Look.Id;
        }

        OnPropertyChanged(nameof(ThemeModeIndex));
        OnPropertyChanged(nameof(ChosenLookName));
    }

    /// <summary>
    /// What making a template in the chosen folder would do right now. Changes nothing.
    /// </summary>
    /// <returns>
    /// The preview, or null when there is nothing to show because the reason is already written
    /// on the card (no folder chosen, a bad typed name, a folder that cannot be looked at).
    /// </returns>
    public async Task<FolderTemplatePreview?> PreviewTemplateAsync(string cardId)
    {
        var card = Templates.First(item => item.Id == cardId);
        card.Result = string.Empty;
        if (SelectedTemplateFolder is not { } folder)
        {
            card.Result = "Connect a folder in Organize first.";
            return null;
        }

        try
        {
            var preview = card.IsOwn
                ? await _templates.PreviewOwnAsync(folder.Id, card.TypedNames).ConfigureAwait(true)
                : await _templates.PreviewAsync(folder.Id, cardId).ConfigureAwait(true);
            if (preview.Problem is not null)
            {
                card.Result = preview.Problem;
                return null;
            }

            if (!preview.NeedsPermission && !preview.CanMake)
            {
                card.Result = $"Every folder in this list is already in {folder.Name}.";
            }

            return preview;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            card.Result = $"DeskAI stopped safely: {exception.Message}";
            return null;
        }
    }

    /// <summary>Called only after the page's permission dialog was accepted.</summary>
    public async Task AllowTemplateFolderTidyAsync()
    {
        if (SelectedTemplateFolder is not { } folder)
        {
            return;
        }

        var result = await _permission.AllowAsync(folder.Id).ConfigureAwait(true);
        TemplateMessage = result.IsAllowed ? string.Empty : result.Explanation;
        await ReloadTemplateFoldersAsync(folder.Id).ConfigureAwait(true);
    }

    /// <summary>
    /// Makes the folders after the person agreed in the preview dialog, and says what happened
    /// on the card.
    /// </summary>
    /// <returns>A fresh preview when the folder changed while the dialog was open, to show again; else null.</returns>
    public async Task<FolderTemplatePreview?> MakeTemplateAsync(FolderTemplatePreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var card = Templates.First(item => item.Id == preview.Template.Id);
        IsBusy = true;
        try
        {
            var outcome = await _templates.MakeAsync(preview).ConfigureAwait(true);
            card.Result = DescribeTemplateOutcome(outcome);
            await LoadLastTemplateAsync(SelectedTemplateFolder).ConfigureAwait(true);
            return outcome.LookAgain;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            card.Result = $"DeskAI stopped safely: {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The folders a run made, kept by a page test to check undo names them.</summary>
    internal LastFolderTemplate? LastTemplate => _lastTemplate;

    /// <summary>
    /// Writes what making a template did as one calm line. It never says "Made" for a folder
    /// that was already there, and never hides a folder that was not made.
    /// </summary>
    internal static string DescribeTemplateOutcome(FolderTemplateOutcome outcome)
    {
        if (outcome.Problem is not null)
        {
            return outcome.Problem;
        }

        var total = outcome.Made.Count + outcome.NotMade.Count;
        var parts = new List<string>();
        if (outcome.Made.Count == total)
        {
            parts.Add($"Made {Count(outcome.Made.Count, "folder", "folders")} in {outcome.FolderName}.");
        }
        else if (outcome.Made.Count == 0)
        {
            parts.Add($"No folders were made in {outcome.FolderName}.");
        }
        else
        {
            parts.Add($"Made {outcome.Made.Count} of {Count(total, "folder", "folders")} in {outcome.FolderName}.");
        }

        parts.AddRange(outcome.NotMade.Select(item => $"{item.Name}: {item.Reason}"));
        if (outcome.AlreadyThere.Count > 0)
        {
            parts.Add($"Already there: {string.Join(", ", outcome.AlreadyThere)}.");
        }

        return string.Join(" ", parts);
    }

    private async Task UndoTemplateAsync()
    {
        if (_lastTemplate is not { } last || SelectedTemplateFolder is not { } folder)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _templates.UndoAsync(folder.Id, last.TransactionId, last.MadeById).ConfigureAwait(true);
            TemplateMessage = result.Summary;
            await LoadLastTemplateAsync(folder).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            TemplateMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadTemplateFoldersAsync(Guid? keep = null)
    {
        var wanted = keep ?? SelectedTemplateFolder?.Id;
        var connected = await _folders.ListAsync().ConfigureAwait(true);
        TemplateFolders.Clear();
        foreach (var folder in connected)
        {
            TemplateFolders.Add(new TemplateFolderOption(folder.Id, folder.Name, folder.Path, folder.CanTidy));
        }

        OnPropertyChanged(nameof(HasTemplateFolders));
        OnPropertyChanged(nameof(HasNoTemplateFolders));

        // Re-selecting the same folder must still reload, because its permission may have just
        // changed; clearing first makes the assignment below count as a change.
        _selectedTemplateFolder = null;
        SelectedTemplateFolder = TemplateFolders.FirstOrDefault(item => item.Id == wanted) ?? TemplateFolders.FirstOrDefault();
        if (SelectedTemplateFolder is null)
        {
            OnPropertyChanged(nameof(SelectedTemplateFolder));
            _pendingLast = LoadLastTemplateAsync(null);
        }

        await _pendingLast.ConfigureAwait(true);
    }

    /// <summary>Waits for the history lookup a folder choice started. For the page and its tests.</summary>
    public Task WaitForTemplateHistoryAsync() => _pendingLast;

    private async Task LoadLastTemplateAsync(TemplateFolderOption? folder)
    {
        _lastTemplate = null;
        if (folder is null)
        {
            LastTemplateSummary = string.Empty;
            return;
        }

        try
        {
            var last = await _templates.FindLastAsync(folder.Id).ConfigureAwait(true);
            if (SelectedTemplateFolder?.Id != folder.Id)
            {
                return;
            }

            _lastTemplate = last;
            LastTemplateSummary = last is null
                ? string.Empty
                : $"Made in {folder.Name} at {last.FinishedAtUtc.ToLocalTime():t} on {last.FinishedAtUtc.ToLocalTime():d}: {string.Join(", ", last.Made)}.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            LastTemplateSummary = string.Empty;
            TemplateMessage = $"DeskAI could not read what it did here before: {exception.Message}";
        }

        OnPropertyChanged(nameof(CanUndoTemplate));
        UndoTemplateCommand.NotifyCanExecuteChanged();
    }

    /// <summary>What adding a pack would do right now. Saves nothing.</summary>
    public Task<StarterPackPreview> PreviewPackAsync(string packId) => _packs.PreviewAsync(packId);

    /// <summary>
    /// Adds a pack after the person agreed in the preview dialog, and says what happened on its card.
    /// </summary>
    public async Task AddPackAsync(string packId)
    {
        var card = Packs.First(item => item.Id == packId);
        IsBusy = true;
        try
        {
            var outcome = await _packs.AddAsync(packId).ConfigureAwait(true);
            card.Result = DescribeOutcome(outcome);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            card.Result = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Leaves the saved search for Search to run when it opens. The view then navigates there.
    /// </summary>
    public void OpenInSearch(Guid savedSearchId) => _request.Ask(savedSearchId);

    /// <summary>
    /// Writes what adding a pack did as one calm paragraph.
    /// </summary>
    /// <remarks>
    /// Rules are always said to be switched off when any were added, because the one thing a
    /// person must not come away believing is that DeskAI started doing something new.
    /// </remarks>
    internal static string DescribeOutcome(StarterPackOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.StoppedBecause is not null)
        {
            parts.Add($"DeskAI stopped safely: {outcome.StoppedBecause}");
        }

        if (outcome.AddedSearches.Count == 0 && outcome.AddedRules.Count == 0)
        {
            if (outcome.StoppedBecause is null)
            {
                parts.Add("Nothing added — you already have everything in this pack.");
            }
        }
        else
        {
            parts.Add(outcome.AddedRules.Count == 0
                ? $"Added {Count(outcome.AddedSearches.Count, "search", "searches")}."
                : $"Added {Count(outcome.AddedSearches.Count, "search", "searches")} and {Count(outcome.AddedRules.Count, "rule", "rules")}.");
        }

        var alreadyHad = outcome.Skipped
            .Where(item => item.SkipReason!.StartsWith("You already have a ", StringComparison.Ordinal))
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (alreadyHad.Length > 0 && (outcome.AddedSearches.Count > 0 || outcome.AddedRules.Count > 0))
        {
            parts.Add($"Skipped {alreadyHad.Length} you already had: {string.Join(", ", alreadyHad)}.");
        }

        var overLimit = outcome.Skipped.Count(item => !item.SkipReason!.StartsWith("You already have a ", StringComparison.Ordinal));
        if (overLimit > 0)
        {
            parts.Add($"{Count(overLimit, "search was", "searches were")} not added — you already have {SavedSearch.MaxSavedSearches} saved searches.");
        }

        if (outcome.AddedButNotPinned.Count > 0)
        {
            parts.Add($"Not pinned, because {SavedSearch.MaxPinned} are already pinned: {string.Join(", ", outcome.AddedButNotPinned)}.");
        }

        if (outcome.AddedRules.Count > 0)
        {
            parts.Add("The rules are switched off — turn them on in Automatic tasks.");
        }

        return string.Join(" ", parts);
    }

    internal static (string Number, string Words) DescribeCount(PinnedCount count) => count.Kind switch
    {
        PinnedCountKind.NoFolders => (string.Empty, "No folders connected"),
        PinnedCountKind.NotUnderstood => (string.Empty, "Search not understood"),
        PinnedCountKind.AtLimit => ($"{count.Files}+", "files"),
        _ => (count.Files.ToString(CultureInfo.CurrentCulture), count.Files == 1 ? "file" : "files"),
    };

    private static string Count(int value, string one, string many) => value == 1 ? $"1 {one}" : $"{value} {many}";

    private async Task PinAsync(Guid savedSearchId)
    {
        IsBusy = true;
        try
        {
            PinMessage = await _pins.PinAsync(savedSearchId).ConfigureAwait(true)
                ? string.Empty
                : $"You can pin up to {SavedSearch.MaxPinned} searches. Unpin one to make room.";
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            PinMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task UnpinAsync(Guid savedSearchId)
    {
        IsBusy = true;
        try
        {
            await _pins.UnpinAsync(savedSearchId).ConfigureAwait(true);
            PinMessage = string.Empty;
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            PinMessage = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadAsync()
    {
        var all = await _searches.ListAsync().ConfigureAwait(true);

        Pins.Clear();
        foreach (var saved in all.Where(item => item.IsPinned))
        {
            (string Number, string Words) count;
            try
            {
                count = DescribeCount(await _pins.CountAsync(saved).ConfigureAwait(true));
            }
            catch (Exception exception) when (IsExpectedFailure(exception))
            {
                // One tile failing to count must not blank the others.
                count = (string.Empty, "Could not count");
            }

            Pins.Add(new PinnedSearchTileViewModel(saved.Id, saved.Name, count.Number, count.Words));
        }

        OtherSearches.Clear();
        foreach (var saved in all.Where(item => !item.IsPinned))
        {
            OtherSearches.Add(new UnpinnedSearchViewModel(saved.Id, saved.Name, saved.Phrase));
        }

        CanPinMore = Pins.Count < SavedSearch.MaxPinned;
        var now = _clock.UtcNow.ToLocalTime();
        PinsCaption = $"Counted at {now:t} from what DeskAI remembers. Refresh a folder in Search to update it.";
        OnPropertyChanged(nameof(HasPins));
        OnPropertyChanged(nameof(HasNoPins));
        OnPropertyChanged(nameof(HasOtherSearches));
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InvalidOperationException
            or FormatException
            or System.Data.Common.DbException;
}
