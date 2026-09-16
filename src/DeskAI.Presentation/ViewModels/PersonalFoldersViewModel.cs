using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Roots;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>One of the person's own four folders as a row: its name, its state, and one button.</summary>
public sealed class PersonalFolderRowViewModel(PersonalFolderKind kind, string name, bool isConnected, bool canTidy)
{
    public PersonalFolderKind Kind { get; } = kind;

    public string Name { get; } = name;

    public bool IsConnected { get; } = isConnected;

    public bool CanTidy { get; } = canTidy;

    /// <summary>What the row says under the name. Never a location.</summary>
    public string Status => (IsConnected, CanTidy) switch
    {
        (false, _) => "Not connected yet.",
        (true, false) => "Connected. Tidy it in Organize.",
        (true, true) => "Connected and allowed to tidy.",
    };

    /// <summary>"Connect" until the folder is connected, then "Tidy"; both open Organize on it.</summary>
    public string ButtonText => IsConnected ? "Tidy" : "Connect";

    public string ButtonName => $"{ButtonText} {Name}";
}

/// <summary>
/// The "Your folders" card on Home and My workspace: the person's own Desktop, Downloads,
/// Documents, and Pictures, found through Windows, each with one button that connects it (after
/// the page's dialog) and opens it in Organize.
/// </summary>
/// <remarks>
/// Connecting here is exactly what picking that folder in the Windows dialog would do — the same
/// service, the same names-sizes-dates memory, no tidy permission — so the card grants nothing
/// the picker could not. These four are also the only places DeskAI may connect at all (ADR
/// 0032); the rule lives in <see cref="PersonalFolderPolicy"/> and is enforced where folders
/// are connected and tidied, not here.
/// </remarks>
public sealed class PersonalFoldersViewModel(
    PersonalFolderPolicy policy,
    ConnectedFolderService folders,
    OrganizeRequest organize) : ObservableObject
{
    private readonly PersonalFolderPolicy _policy = policy;
    private readonly ConnectedFolderService _folders = folders;
    private readonly OrganizeRequest _organize = organize;
    private string _message = string.Empty;
    private bool _isBusy;

    public ObservableCollection<PersonalFolderRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    /// <summary>Shown instead of the rows when Windows reports none of the four folders.</summary>
    public bool HasNoRows => Rows.Count == 0;

    public static string NoneKnownText => PersonalFolderPolicy.NoneKnownReason;

    /// <summary>A refusal or a problem, in plain words. Empty when there is none.</summary>
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

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public PersonalFolderRowViewModel? Find(PersonalFolderKind kind) => Rows.FirstOrDefault(row => row.Kind == kind);

    public async Task ReloadAsync()
    {
        IReadOnlyList<ConnectedFolder> connected;
        try
        {
            connected = await _folders.ListAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not check which folders are connected: {exception.Message}";
            connected = [];
        }

        Rows.Clear();
        foreach (var folder in _policy.List())
        {
            var match = connected.FirstOrDefault(item => SamePath(item.Path, folder.Path));
            Rows.Add(new PersonalFolderRowViewModel(folder.Kind, folder.Name, match is not null, match?.CanTidy == true));
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(HasNoRows));
    }

    /// <summary>
    /// Connects the folder if it is not connected yet, and leaves it for Organize to open.
    /// Called only after the page's dialog.
    /// </summary>
    /// <returns>The connected folder's ID for the page to navigate with, or null with the reason on the card.</returns>
    public async Task<Guid?> ConnectAsync(PersonalFolderKind kind)
    {
        if (_policy.Find(kind) is not { } folder)
        {
            Message = $"DeskAI could not find your {kind} folder.";
            return null;
        }

        IsBusy = true;
        try
        {
            var already = (await _folders.ListAsync().ConfigureAwait(true))
                .FirstOrDefault(item => SamePath(item.Path, folder.Path));
            Guid id;
            if (already is not null)
            {
                id = already.Id;
            }
            else
            {
                var result = await _folders.ConnectAsync(folder.Path).ConfigureAwait(true);
                if (!result.IsAllowed || result.Folder is null)
                {
                    Message = result.Explanation;
                    return null;
                }

                id = result.Folder.Id;
            }

            Message = string.Empty;
            await ReloadAsync().ConfigureAwait(true);
            _organize.Ask(id);
            return id;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(first),
            Path.TrimEndingDirectorySeparator(second),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedFailure(Exception exception) =>
        exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or UnauthorizedAccessException;
}
