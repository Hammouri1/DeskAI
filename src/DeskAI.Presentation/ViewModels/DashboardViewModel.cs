using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Indexing;
using DeskAI.Core.Search;

namespace DeskAI.App.ViewModels;

/// <summary>One category row on the storage breakdown.</summary>
public sealed record CategoryUsageViewModel(string Category, string Files, string Size, double ShareOfTotal);

/// <summary>One of the largest files, shown to explain where space went.</summary>
public sealed record LargestFileViewModel(string Name, string Location, string Size);

/// <summary>
/// A set of files that share an exact size and might therefore be copies.
/// </summary>
/// <remarks>
/// <see cref="Locations"/> is a single readable line rather than a nested list, because the
/// point of the row is to let a person recognise the copies, not to browse them.
/// </remarks>
public sealed record DuplicateGroupViewModel(string Headline, string Locations, string Reclaimable);

/// <summary>
/// What a copy check would read, worded for the dialog that asks first. Compare sends
/// <see cref="Question"/> back unchanged, so what was shown is exactly what is read.
/// </summary>
public sealed record CopyCheckQuestionViewModel(DuplicateCheckQuestion Question, string Title, string Body);

/// <summary>One group of same-size files after a copy check: what is identical, what is not, what was not checked.</summary>
public sealed record CheckedCopyGroupViewModel(string Headline, string Verdict, IReadOnlyList<string> Lines);

/// <summary>
/// One measured part of the organization health score.
/// </summary>
/// <remarks>
/// The part carries its own evidence and its own weight, so the total can be read as a sum
/// a person can check rather than as a verdict they have to take on trust.
/// </remarks>
public sealed record HealthComponentViewModel(
    string Name,
    string Explanation,
    string Measurement,
    string ScoreLabel,
    string WeightLabel,
    double PartScore);

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
public sealed class DashboardViewModel(
    StorageSummaryService storage,
    DuplicateFinderService duplicates,
    DuplicateCheckService copyCheck,
    IClock clock) : ObservableObject, IDisposable
{
    private readonly StorageSummaryService _storage = storage;
    private readonly DuplicateFinderService _duplicates = duplicates;
    private readonly DuplicateCheckService _copyCheck = copyCheck;
    private readonly IClock _clock = clock;
    private bool _isCheckingCopies;
    private string _copyCheckSummary = string.Empty;
    private CancellationTokenSource? _copyCheckStop;
    private RelayCommand? _stopCopyCheckCommand;

    /// <summary>The result of the last copy check, group by group.</summary>
    public ObservableCollection<CheckedCopyGroupViewModel> CheckedCopyGroups { get; } = [];

    /// <summary>One line saying what the copy check found, read, and did not do.</summary>
    public string CopyCheckSummary
    {
        get => _copyCheckSummary;
        private set
        {
            if (SetProperty(ref _copyCheckSummary, value))
            {
                OnPropertyChanged(nameof(HasCopyCheckSummary));
            }
        }
    }

    public bool HasCopyCheckSummary => !string.IsNullOrEmpty(CopyCheckSummary);

    public bool IsCheckingCopies
    {
        get => _isCheckingCopies;
        private set
        {
            if (SetProperty(ref _isCheckingCopies, value))
            {
                OnPropertyChanged(nameof(CanCheckCopies));
                StopCopyCheckCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>There are possible copies, and no check is running.</summary>
    public bool CanCheckCopies => HasDuplicates && !IsCheckingCopies;

    public RelayCommand StopCopyCheckCommand =>
        _stopCopyCheckCommand ??= new(() => _copyCheckStop?.Cancel(), () => IsCheckingCopies);

    /// <summary>
    /// Works out what a copy check would read and words it for the dialog. Opens no file.
    /// </summary>
    /// <returns>The question to show, or null when there is nothing to compare.</returns>
    public async Task<CopyCheckQuestionViewModel?> PrepareCopyCheckAsync()
    {
        DuplicateCheckQuestion question;
        try
        {
            question = await _copyCheck.PrepareAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException or IOException)
        {
            CopyCheckSummary = $"DeskAI could not get the list ready: {exception.Message}";
            return null;
        }

        if (!question.HasAnything)
        {
            CopyCheckSummary = "There are no possible copies to compare right now.";
            return null;
        }

        var files = question.Files.Count == 1 ? "1 file" : $"{question.Files.Count} files";
        var folders = question.FolderCount == 1 ? "1 folder" : $"{question.FolderCount} folders";
        var leftOut = question.LeftOut == 0
            ? string.Empty
            : $"\n\n{question.LeftOut} more files that might be copies are left for another check.";
        var body =
            $"DeskAI will read {files} ({DescribeSize(question.TotalBytes)}) in {folders} from beginning to end, on this computer, "
            + "to see which are really the same. It starts with the beginning of each file and reads further only when two beginnings match."
            + leftOut
            + "\n\nNothing it reads is saved or sent anywhere, and it does not change, move, or delete anything. "
            + "Files stored online only are not read, because that would download them.";
        return new CopyCheckQuestionViewModel(question, $"Compare {files}?", body);
    }

    /// <summary>
    /// Reads the files in the question the person just agreed to, and shows which are copies.
    /// Called only after Compare was pressed in the page's dialog.
    /// </summary>
    public async Task CompareCopiesAsync(CopyCheckQuestionViewModel question)
    {
        ArgumentNullException.ThrowIfNull(question);
        if (IsCheckingCopies)
        {
            return;
        }

        var stop = new CancellationTokenSource();
        _copyCheckStop = stop;
        IsCheckingCopies = true;
        CheckedCopyGroups.Clear();
        CopyCheckSummary = "Comparing… Nothing is being changed.";
        try
        {
            var result = await _copyCheck.CompareAsync(question.Question, stop.Token).ConfigureAwait(true);
            ApplyCopyCheck(result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Data.Common.DbException or IOException)
        {
            CopyCheckSummary = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            stop.Dispose();
            if (ReferenceEquals(_copyCheckStop, stop))
            {
                _copyCheckStop = null;
            }

            IsCheckingCopies = false;
        }
    }

    /// <summary>Leaving the page stops a check that is still reading.</summary>
    public void Dispose()
    {
        _copyCheckStop?.Cancel();
    }

    private void ApplyCopyCheck(DuplicateCheckResult result)
    {
        foreach (var group in result.Groups)
        {
            var lines = new List<string>();
            lines.AddRange(group.IdenticalSets.Select(set => "Identical: " + string.Join(", ", set.Select(file => file.DisplayName))));
            lines.AddRange(group.Different.Select(file => $"Not a copy of the others: {file.DisplayName}"));
            lines.AddRange(group.NotCompared.Select(item => $"Not checked: {item.File.DisplayName}. {item.Reason}"));
            var verdict = (group.IdenticalSets.Count, group.Different.Count, group.NotCompared.Count) switch
            {
                ( > 0, 0, 0) => "Identical",
                (0, > 0, 0) => "Same size, different contents",
                (0, 0, > 0) => "Not checked",
                _ => "Some identical",
            };
            var count = group.IdenticalCount + group.Different.Count + group.NotCompared.Count;
            CheckedCopyGroups.Add(new CheckedCopyGroupViewModel(
                $"{count} files of {DescribeSize(group.SizeBytes)}", verdict, lines.AsReadOnly()));
        }

        var identical = result.IdenticalCount;
        var found = identical == 0
            ? "None of them are identical copies."
            : $"{identical} files are identical copies. Keeping one of each would free up to {DescribeSize(result.ReclaimableBytes)}.";
        var notChecked = result.NotComparedCount == 0 ? string.Empty : $" {result.NotComparedCount} could not be checked; each says why.";
        var read = $" DeskAI read {result.FilesOpened} files ({DescribeSize(result.BytesRead)}). Nothing was saved, sent, or changed.";
        CopyCheckSummary = (result.Stopped ? "Stopped. " : string.Empty) + found + notChecked + read;
    }
    private string _duplicateHeadline = "0";
    private string _duplicateDetail = "Connect a folder to look for possible copies.";
    private bool _hasDuplicates;
    private string _foldersConnected = "0";
    private string _totalFiles = "0";
    private string _totalSize = "Nothing remembered yet";
    private string _oldFilesHeadline = "0";
    private string _oldFilesDetail = "Connect a folder to see what has been sitting unused.";
    private string _lastChecked = string.Empty;
    private bool _hasStorage;
    private string _healthScore = "--";
    private string _healthBand = "Not measured yet";
    private string _healthMessage =
        "Connect a folder in Search and DeskAI can tell you how settled it looks.";
    private bool _hasHealth;
    private string _healthCoverage = string.Empty;
    private bool _hasHealthCoverage;
    private string _heroState = "Nothing connected yet";
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

    public ObservableCollection<HealthComponentViewModel> HealthComponents { get; } = [];

    /// <summary>
    /// The health score out of a hundred, shown next to every part that produced it.
    /// </summary>
    /// <remarks>
    /// A bare number would be an opinion. It is only shown alongside
    /// <see cref="HealthComponents"/>, which state what was measured and how much each part
    /// counted for, so the total can be checked by hand.
    /// </remarks>
    public string HealthScore
    {
        get => _healthScore;
        private set => SetProperty(ref _healthScore, value);
    }

    public string HealthBand
    {
        get => _healthBand;
        private set => SetProperty(ref _healthBand, value);
    }

    public string HealthMessage
    {
        get => _healthMessage;
        private set => SetProperty(ref _healthMessage, value);
    }

    public bool HasHealth
    {
        get => _hasHealth;
        private set => SetProperty(ref _hasHealth, value);
    }

    /// <summary>
    /// How much of the folder DeskAI could actually recognise.
    /// </summary>
    /// <remarks>
    /// Shown next to the score, never inside it. A file type DeskAI has not learned is this
    /// app's gap, so it limits how complete the reading is rather than costing the person
    /// points for it.
    /// </remarks>
    public string HealthCoverage
    {
        get => _healthCoverage;
        private set => SetProperty(ref _healthCoverage, value);
    }

    public bool HasHealthCoverage
    {
        get => _hasHealthCoverage;
        private set => SetProperty(ref _hasHealthCoverage, value);
    }

    public ObservableCollection<CategoryUsageViewModel> Categories { get; } = [];

    public ObservableCollection<LargestFileViewModel> LargestFiles { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> DuplicateGroups { get; } = [];

    /// <summary>
    /// How many files share a size with another. Worded as "possible" everywhere, because
    /// matching sizes is evidence and not proof.
    /// </summary>
    public string DuplicateHeadline
    {
        get => _duplicateHeadline;
        private set => SetProperty(ref _duplicateHeadline, value);
    }

    public string DuplicateDetail
    {
        get => _duplicateDetail;
        private set => SetProperty(ref _duplicateDetail, value);
    }

    public bool HasDuplicates
    {
        get => _hasDuplicates;
        private set
        {
            if (SetProperty(ref _hasDuplicates, value))
            {
                OnPropertyChanged(nameof(CanCheckCopies));
            }
        }
    }

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
        DuplicateReport duplicates;
        try
        {
            summary = await _storage.BuildAsync(_clock.UtcNow).ConfigureAwait(true);
            duplicates = await _duplicates.FindAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException)
        {
            // Say the reading failed rather than showing zeros, which would read as "you
            // have nothing" instead of "DeskAI could not check".
            TotalSize = "DeskAI could not read the storage summary.";
            HasStorage = false;
            HasDuplicates = false;
            HasHealth = false;
            return;
        }

        Apply(summary);
        ApplyDuplicates(duplicates);
        ApplyHealth(OrganizationHealthCalculator.Evaluate(summary, duplicates));
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

    /// <summary>
    /// Presents size matches as possibilities, never as facts.
    /// </summary>
    /// <remarks>
    /// Every string here says "possible" or "might". Proving two files identical means
    /// reading their bytes, which the metadata-only authorization these folders were
    /// connected under does not permit, so the wording must not outrun the evidence.
    /// </remarks>
    private void ApplyDuplicates(DuplicateReport report)
    {
        DuplicateGroups.Clear();
        HasDuplicates = report.HasAnything;

        if (!report.HasAnything)
        {
            DuplicateHeadline = "0";
            DuplicateDetail = report.FoldersIncluded == 0
                ? "Connect a folder to look for possible copies."
                : "No files share a size, so nothing looks duplicated.";
            return;
        }

        DuplicateHeadline = report.TotalFiles.ToString("N0", CultureInfo.CurrentCulture);
        DuplicateDetail =
            $"These share an exact size, so up to {DescribeSize(report.ReclaimableBytes)} might be duplicated. "
            + "DeskAI has not compared their contents, so they are not confirmed copies.";

        foreach (var group in report.Groups)
        {
            var locations = string.Join(
                "   •   ",
                group.Files.Select(file => $"{file.RootName} / {file.RelativePath}"));

            DuplicateGroups.Add(new DuplicateGroupViewModel(
                $"{group.Count} files of {DescribeSize(group.SizeBytes)}",
                locations,
                $"up to {DescribeSize(group.ReclaimableBytes)}"));
        }
    }

    /// <summary>
    /// Words the health score and each part behind it.
    /// </summary>
    /// <remarks>
    /// Every part states what was actually measured and how much it counted for. Nothing
    /// here offers to fix anything: the score describes, and any cleanup a person chooses
    /// still goes through the ordinary preview and approval path.
    /// </remarks>
    private void ApplyHealth(OrganizationHealth health)
    {
        HealthComponents.Clear();
        HasHealth = health.IsMeasured;

        if (!health.IsMeasured)
        {
            HealthScore = "--";
            HealthBand = "Not measured yet";
            HealthMessage = "Connect a folder in Search and DeskAI can tell you how settled it looks.";
            HasHealthCoverage = false;
            HealthCoverage = string.Empty;
            return;
        }

        HealthScore = health.Score.ToString(CultureInfo.CurrentCulture);
        HealthBand = health.Band switch
        {
            Core.Indexing.HealthBand.Good => "Looking tidy",
            Core.Indexing.HealthBand.Fair => "Mostly fine",
            _ => "Worth a look",
        };
        HealthMessage =
            "Out of 100, worked out from the two things below. DeskAI is describing what it "
            + "remembers about your folders. It is not suggesting you change anything.";

        // Said out loud rather than folded into the score. Not knowing a file type is a gap
        // in what DeskAI has learned, and charging someone points for it would be blaming
        // them for this app's limits.
        HasHealthCoverage = health.IsRecognitionPartial;
        HealthCoverage = health.IsRecognitionPartial
            ? $"DeskAI could tell the type of about {health.RecognisedShare.ToString("P0", CultureInfo.CurrentCulture)} "
                + $"of this space. The other {DescribeFileCount(health.UnrecognisedFileCount)} use file types it does "
                + "not know yet, so they are left out of the picture rather than counted against you."
            : string.Empty;

        foreach (var component in health.Components)
        {
            var (name, explanation) = Describe(component.Kind);
            HealthComponents.Add(new HealthComponentViewModel(
                name,
                explanation,
                DescribeMeasurement(component),
                $"{component.Score} out of 100",
                $"counts for {component.Weight}%",
                component.Score / 100d));
        }
    }

    private static (string Name, string Explanation) Describe(HealthComponentKind kind) => kind switch
    {
        HealthComponentKind.PossibleCopies =>
            ("Possible copies", "Space that may be held twice by files of the same exact size."),
        _ => ("Sitting unused", "Space in files that have not changed in about six months. "
            + "Older files are perfectly normal, so this only counts for a little."),
    };

    private static string DescribeMeasurement(HealthComponent component)
    {
        if (component.MeasuredBytes <= 0)
        {
            return "Nothing found here.";
        }

        var share = component.ShareOfTotal.ToString("P0", CultureInfo.CurrentCulture);
        return $"{DescribeSize(component.MeasuredBytes)} across "
            + $"{DescribeFileCount(component.MeasuredFileCount)}, about {share} of the space.";
    }

    private static string DescribeFileCount(int count) => count == 1
        ? "1 file"
        : $"{count.ToString("N0", CultureInfo.CurrentCulture)} files";

    private void Apply(StorageSummary summary)
    {
        Categories.Clear();
        LargestFiles.Clear();

        FoldersConnected = summary.FoldersIncluded.ToString(CultureInfo.CurrentCulture);
        TotalFiles = summary.TotalFiles.ToString("N0", CultureInfo.CurrentCulture);
        HasStorage = summary.HasAnything;

        if (!summary.HasAnything)
        {
            HeroState = "Nothing connected yet";
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
        // Not "it has not opened any of them": that reassurance would expire silently the
        // moment someone allowed reading inside a folder, and this page cannot see that.
        // Since V0.6 step 3 a folder someone allowed DeskAI to tidy can change, so the old
        // "cannot move anything" became untrue and was replaced by the narrower promise that
        // still holds: only in such a folder, only when Tidy is pressed, and never a delete.
        HeroMessage =
            "DeskAI remembers names, sizes, and dates for these files. It opens a file only if you "
            + "allowed that for its folder. It moves files only in a folder you allowed it to tidy, "
            + "only when you press Tidy, and it never deletes anything.";

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
