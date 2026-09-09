using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>One category row on the storage breakdown.</summary>
public sealed record CategoryUsageViewModel(string Category, string Files, string Size, double ShareOfTotal);

/// <summary>One of the largest files, shown to explain where space went.</summary>
public sealed record LargestFileViewModel(string Name, string Location, string Size);

/// <summary>
/// Drives the Home page: what is connected, and where the space is going.
/// </summary>
/// <remarks>
/// <para>
/// The numbers describe remembered metadata, not the live disk, which is why the page shows
/// when they were last checked. Presenting a stale reading as current would be the storage
/// equivalent of the scope label claiming nothing is connected.
/// </para>
/// <para>
/// Nothing here proposes an action. There is deliberately no "clean this up" button: any
/// cleanup still has to go through the ordinary preview and approval path, and a summary
/// must not become a shortcut around it.
/// </para>
/// </remarks>
public sealed class DashboardViewModel(StorageSummaryService storage, IClock clock) : ObservableObject
{
    private readonly StorageSummaryService _storage = storage;
    private readonly IClock _clock = clock;
    private string _foldersConnected = "0";
    private string _totalFiles = "0";
    private string _totalSize = "Nothing remembered yet";
    private string _oldFilesHeadline = "0";
    private string _oldFilesDetail = "Connect a folder to see what has been sitting unused.";
    private string _lastChecked = string.Empty;
    private bool _hasStorage;
    private string _heroState = "Practice mode";
    private string _heroTitle = "Your files are untouched";
    private string _heroMessage =
        "No folder of yours is connected, so nothing on your computer can be moved, renamed, or deleted.";

    /// <summary>
    /// The headline state. Written from what is actually connected for the same reason the
    /// navigation pane's reminder is: a fixed reassurance becomes a lie the moment someone
    /// connects a real folder.
    /// </summary>
    public string HeroState
    {
        get => _heroState;
        private set => SetProperty(ref _heroState, value);
    }

    public string HeroTitle
    {
        get => _heroTitle;
        private set => SetProperty(ref _heroTitle, value);
    }

    public string HeroMessage
    {
        get => _heroMessage;
        private set => SetProperty(ref _heroMessage, value);
    }

    public ObservableCollection<CategoryUsageViewModel> Categories { get; } = [];

    public ObservableCollection<LargestFileViewModel> LargestFiles { get; } = [];

    public string FoldersConnected
    {
        get => _foldersConnected;
        private set => SetProperty(ref _foldersConnected, value);
    }

    public string TotalFiles
    {
        get => _totalFiles;
        private set => SetProperty(ref _totalFiles, value);
    }

    public string TotalSize
    {
        get => _totalSize;
        private set => SetProperty(ref _totalSize, value);
    }

    public string OldFilesHeadline
    {
        get => _oldFilesHeadline;
        private set => SetProperty(ref _oldFilesHeadline, value);
    }

    public string OldFilesDetail
    {
        get => _oldFilesDetail;
        private set => SetProperty(ref _oldFilesDetail, value);
    }

    /// <summary>States when the numbers were true, so they are never implied to be live.</summary>
    public string LastChecked
    {
        get => _lastChecked;
        private set
        {
            if (SetProperty(ref _lastChecked, value))
            {
                OnPropertyChanged(nameof(HasLastChecked));
            }
        }
    }

    public bool HasLastChecked => !string.IsNullOrEmpty(LastChecked);

    public bool HasStorage
    {
        get => _hasStorage;
        private set => SetProperty(ref _hasStorage, value);
    }

    public async Task InitializeAsync()
    {
        StorageSummary summary;
        try
        {
            summary = await _storage.BuildAsync(_clock.UtcNow).ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Say the reading failed rather than showing zeros, which would read as "you
            // have nothing" instead of "DeskAI could not check".
            TotalSize = "DeskAI could not read the storage summary.";
            HasStorage = false;
            return;
        }

        Apply(summary);
    }

    private static string DescribeSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024d * 1024 * 1024):0.#} GB",
        >= 1024L * 1024 => $"{bytes / (1024d * 1024):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };

    /// <summary>Turns an enum name such as "SourceCode" into "Source code".</summary>
    private static string Humanize(string name) => string.Concat(name.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? " " + char.ToLowerInvariant(character) : character.ToString()));

    private void Apply(StorageSummary summary)
    {
        Categories.Clear();
        LargestFiles.Clear();

        FoldersConnected = summary.FoldersIncluded.ToString(CultureInfo.CurrentCulture);
        TotalFiles = summary.TotalFiles.ToString("N0", CultureInfo.CurrentCulture);
        HasStorage = summary.HasAnything;

        if (!summary.HasAnything)
        {
            HeroState = "Practice mode";
            HeroTitle = "Your files are untouched";
            HeroMessage =
                "No folder of yours is connected, so nothing on your computer can be moved, renamed, or deleted.";
            TotalSize = "Nothing remembered yet";
            OldFilesHeadline = "0";
            OldFilesDetail = "Connect a folder to see what has been sitting unused.";
            LastChecked = string.Empty;
            return;
        }

        HeroState = summary.FoldersIncluded == 1 ? "1 folder connected" : $"{summary.FoldersIncluded} folders connected";
        HeroTitle = $"{DescribeSize(summary.TotalSizeBytes)} across {summary.TotalFiles:N0} files";
        HeroMessage =
            "DeskAI remembers names, sizes, and dates for these files. It has not opened any of them, "
            + "and it cannot move, rename, or delete anything without showing you first.";

        TotalSize = DescribeSize(summary.TotalSizeBytes);

        var months = (int)Math.Round(StorageSummaryService.OldFileAge.TotalDays / 30);
        OldFilesHeadline = summary.OldFileCount.ToString("N0", CultureInfo.CurrentCulture);
        OldFilesDetail = summary.OldFileCount == 0
            ? $"Nothing has gone {months} months without a change."
            : $"Not changed in {months} months, using {DescribeSize(summary.OldFileBytes)}. "
                + "DeskAI is only pointing them out, not suggesting you remove them.";

        foreach (var usage in summary.Categories)
        {
            Categories.Add(new CategoryUsageViewModel(
                Humanize(usage.Category.ToString()),
                usage.FileCount.ToString("N0", CultureInfo.CurrentCulture),
                DescribeSize(usage.TotalSizeBytes),
                summary.TotalSizeBytes == 0 ? 0 : (double)usage.TotalSizeBytes / summary.TotalSizeBytes));
        }

        foreach (var file in summary.LargestFiles)
        {
            var folder = Path.GetDirectoryName(file.RelativePath);
            LargestFiles.Add(new LargestFileViewModel(
                file.Name,
                string.IsNullOrEmpty(folder) ? file.RootName : $"{file.RootName} / {folder}",
                DescribeSize(file.SizeBytes)));
        }

        LastChecked = summary.LastCheckedUtc is { } checkedAt
            ? $"Last checked {checkedAt.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture)}. Refresh a folder in Search to update."
            : string.Empty;
    }
}
