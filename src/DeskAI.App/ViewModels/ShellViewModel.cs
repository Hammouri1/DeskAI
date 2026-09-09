using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>
/// Backs the permanent scope reminder in the navigation pane.
/// </summary>
/// <remarks>
/// <para>
/// This text was previously a fixed string reading "Sample files only. Your personal
/// folders are not connected." Once a person could connect a real folder, that sentence
/// became false while still being displayed, which is worse than showing nothing: the one
/// label that promises what DeskAI can reach was the label that lied.
/// </para>
/// <para>
/// It is therefore derived from what is actually connected, and refreshed on every
/// navigation, so it cannot drift away from the truth.
/// </para>
/// </remarks>
public sealed class ShellViewModel(ConnectedFolderService folders) : ObservableObject
{
    private readonly ConnectedFolderService _folders = folders;
    private string _scopeTitle = "Practice mode";
    private string _scopeMessage = "No folders connected. DeskAI cannot see any of your files.";

    public string ScopeTitle
    {
        get => _scopeTitle;
        private set => SetProperty(ref _scopeTitle, value);
    }

    public string ScopeMessage
    {
        get => _scopeMessage;
        private set => SetProperty(ref _scopeMessage, value);
    }

    /// <summary>Re-reads what is connected. Cheap, and called on every navigation.</summary>
    public async Task RefreshAsync()
    {
        IReadOnlyList<ConnectedFolder> connected;
        try
        {
            connected = await _folders.ListAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Never fall back to the reassuring message. If the real scope cannot be read,
            // say that rather than implying nothing is connected.
            ScopeTitle = "Scope unavailable";
            ScopeMessage = "DeskAI could not check which folders are connected.";
            return;
        }

        if (connected.Count == 0)
        {
            ScopeTitle = "Practice mode";
            ScopeMessage = "No folders connected. DeskAI cannot see any of your files.";
            return;
        }

        var files = connected.Sum(folder => folder.FileCount);
        ScopeTitle = connected.Count == 1 ? "1 folder connected" : $"{connected.Count} folders connected";
        ScopeMessage = $"DeskAI remembers names, sizes, and dates for {files} file(s). It has not opened any of them.";
    }
}
