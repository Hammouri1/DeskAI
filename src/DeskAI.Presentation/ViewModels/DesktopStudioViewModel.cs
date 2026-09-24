using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;
using DeskAI.Core.Studio;

namespace DeskAI.App.ViewModels;

/// <summary>One folder or file on the board. <see cref="Path"/> is relative to the Desktop.</summary>
public sealed record DesktopItemViewModel(string Path, string Name, bool IsFolder)
{
    /// <summary>Folder or page glyph from Segoe Fluent Icons.</summary>
    public string Glyph => IsFolder ? "" : "";
}

public sealed class DesktopGroupViewModel(string name, IEnumerable<DesktopItemViewModel> items)
{
    public string Name { get; } = name;

    public ObservableCollection<DesktopItemViewModel> Items { get; } = new(items);
}

/// <summary>
/// Desktop Studio (ADR 0042). Its one card so far, Find groups, sorts the connected Desktop's
/// folders and files into a board the person can rename, merge, and rearrange.
/// </summary>
/// <remarks>
/// Thin on purpose: every rule — which folder is the Desktop, what may be sent, how an answer is
/// read, which names are allowed — lives in <see cref="DesktopGroupingService"/>. Nothing here
/// can move, rename, or open a file, or change Windows.
/// </remarks>
public sealed class DesktopStudioViewModel(
    DesktopGroupingService grouping,
    ConnectedFolderService folders,
    PersonalFolderPolicy personalFolders) : ObservableObject
{
    private readonly DesktopGroupingService _grouping = grouping;
    private readonly ConnectedFolderService _folders = folders;
    private readonly PersonalFolderPolicy _personalFolders = personalFolders;
    private Guid? _desktopId;
    private string _serviceName = "AI";
    private bool _hasAi;
    private string _message = string.Empty;
    private string _sourceNote = string.Empty;
    private bool _isBusy;

    public bool IsDesktopConnected => _desktopId is not null;

    public bool IsDesktopNotConnected => !IsDesktopConnected;

    public bool HasAi
    {
        get => _hasAi;
        private set => SetProperty(ref _hasAi, value);
    }

    public string SendButtonText => $"Find groups with {_serviceName}";

    public ObservableCollection<DesktopGroupViewModel> Groups { get; } = [];

    public ObservableCollection<DesktopItemViewModel> NotSure { get; } = [];

    public bool HasBoard => Groups.Count > 0 || NotSure.Count > 0;

    public bool HasNotSure => NotSure.Count > 0;

    public IReadOnlyList<string> GroupNames => Groups.Select(g => g.Name).ToList();

    public string SourceNote
    {
        get => _sourceNote;
        private set => SetProperty(ref _sourceNote, value);
    }

    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message.Length > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public Task InitializeAsync() => RunAsync(async () =>
    {
        var ai = await _grouping.GetAiAsync().ConfigureAwait(true);
        HasAi = ai.IsSetUp;
        _serviceName = ai.Name;
        OnPropertyChanged(nameof(SendButtonText));
        _desktopId = (await _grouping.FindDesktopAsync().ConfigureAwait(true))?.Id;
        OnPropertyChanged(nameof(IsDesktopConnected));
        OnPropertyChanged(nameof(IsDesktopNotConnected));
        if (_desktopId is { } id)
        {
            var loaded = await _grouping.LoadBoardAsync(id).ConfigureAwait(true);
            Show(loaded.Board);
            Message = loaded.Message;
        }
        else
        {
            Show(null);
        }
    });

    /// <summary>Connects the Desktop for names, sizes, and dates only. Called after the page's dialog.</summary>
    public Task ConnectDesktopAsync() => RunAsync(async () =>
    {
        if (_personalFolders.Find(PersonalFolderKind.Desktop) is not { } desktop)
        {
            Message = "DeskAI could not find your Desktop.";
            return;
        }

        if (await _grouping.FindDesktopAsync().ConfigureAwait(true) is null)
        {
            var result = await _folders.ConnectAsync(desktop.Path).ConfigureAwait(true);
            if (!result.IsAllowed)
            {
                Message = result.Explanation;
                return;
            }
        }

        await InitializeAsync().ConfigureAwait(true);
    });

    /// <summary>Builds the exact list AI would see, for the page's Send window. Sends nothing.</summary>
    public async Task<DesktopGroupQuestion?> PrepareAsync()
    {
        if (_desktopId is not { } id)
        {
            Message = DesktopGroupingService.NotConnected;
            return null;
        }

        DesktopGroupQuestion? question = null;
        await RunAsync(async () =>
        {
            var prepared = await _grouping.PrepareAsync(id).ConfigureAwait(true);
            question = prepared.Question;
            Message = prepared.Question is null ? prepared.Explanation : string.Empty;
        }).ConfigureAwait(true);
        return question;
    }

    /// <summary>Sends the list the person saw, after they pressed Send.</summary>
    public Task SendAsync(DesktopGroupQuestion question) => RunAsync(async () =>
    {
        ArgumentNullException.ThrowIfNull(question);
        Apply(await _grouping.SendAsync(question).ConfigureAwait(true));
    });

    public Task GuessAsync() => WithDesktopAsync(id => _grouping.GuessAsync(id));

    public Task RenameGroupAsync(string group, string newName) => WithDesktopAsync(id => _grouping.RenameAsync(id, group, newName));

    public Task MergeGroupAsync(string from, string into) => WithDesktopAsync(id => _grouping.MergeAsync(id, from, into));

    /// <summary>Moves an item to a group, or to Not sure when <paramref name="toGroup"/> is null.</summary>
    public Task MoveItemAsync(string itemPath, string? toGroup) => WithDesktopAsync(id => _grouping.MoveAsync(id, itemPath, toGroup));

    private Task WithDesktopAsync(Func<Guid, Task<DesktopGroupResult>> action)
    {
        if (_desktopId is not { } id)
        {
            Message = DesktopGroupingService.NotConnected;
            return Task.CompletedTask;
        }

        return RunAsync(async () => Apply(await action(id).ConfigureAwait(true)));
    }

    private void Apply(DesktopGroupResult result)
    {
        Show(result.Board);
        Message = result.Message;
    }

    private void Show(DesktopGroupBoard? board)
    {
        Groups.Clear();
        NotSure.Clear();
        if (board is not null)
        {
            DesktopItemViewModel Item(string path) => new(path, System.IO.Path.GetFileName(path), board.Folders.Contains(path));
            foreach (var group in board.Groups)
            {
                Groups.Add(new DesktopGroupViewModel(group.Name, group.Items.Select(Item)));
            }

            foreach (var item in board.NotSure)
            {
                NotSure.Add(Item(item));
            }
        }

        SourceNote = board?.Source switch
        {
            DesktopGroupSource.Ai => $"Grouped by {board.MadeBy ?? "AI"}.",
            DesktopGroupSource.LocalGuess => "Grouped by DeskAI's own simpler guess.",
            _ => string.Empty,
        };
        OnPropertyChanged(nameof(HasBoard));
        OnPropertyChanged(nameof(HasNotSure));
        OnPropertyChanged(nameof(GroupNames));
    }

    private async Task RunAsync(Func<Task> work)
    {
        IsBusy = true;
        try
        {
            await work().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or System.Data.Common.DbException;
}
