using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Studio;

namespace DeskAI.App.ViewModels;

/// <summary>One row a moving card lists, with its tick box.</summary>
public sealed class DesktopMoveItemViewModel(DesktopMoveItem item, DesktopMoveCard card) : ObservableObject
{
    private bool _isTicked = item.TickedByDefault;

    public DesktopMoveItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>Folder or page glyph from Segoe Fluent Icons.</summary>
    public string Glyph => Item.IsFolder ? "" : "";

    public string Detail => card == DesktopMoveCard.ClearOldStuff
        ? $"{Kind()} · last changed {Item.LastChangedUtc.ToLocalTime():d}"
        : $"{Kind()} · goes into {Item.Destination}";

    public string Warning => Item.Warning ?? string.Empty;

    public bool HasWarning => Item.Warning is not null;

    public bool IsTicked
    {
        get => _isTicked;
        set => SetProperty(ref _isTicked, value);
    }

    private string Kind()
    {
        if (!Item.IsFolder)
        {
            return "File";
        }

        var atLeast = Item.LookedAllTheWay ? string.Empty : "at least ";
        return Item.FileCount == 1 ? $"Folder with {atLeast}1 file" : $"Folder with {atLeast}{Item.FileCount:N0} files";
    }
}

/// <summary>
/// One Desktop Studio card that moves things: what it would move, the total of what is ticked,
/// and its last change for Put back. It holds no service; <see cref="DesktopStudioViewModel"/> fills it.
/// </summary>
public sealed class DesktopMoveCardViewModel(DesktopMoveCard card) : ObservableObject
{
    private DesktopMovePreview? _preview;
    private DesktopLastChange? _last;
    private string _message = string.Empty;

    public DesktopMoveCard Card { get; } = card;

    public ObservableCollection<DesktopMoveItemViewModel> Items { get; } = [];

    /// <summary>"Name: reason" for each thing left where it is.</summary>
    public ObservableCollection<string> LeftAlone { get; } = [];

    public DesktopMovePreview? Preview => _preview;

    public bool HasPreview => Items.Count > 0;

    public bool HasLeftAlone => LeftAlone.Count > 0;

    public IReadOnlyList<Guid> Ticked => Items.Where(item => item.IsTicked).Select(item => item.Item.OperationId).ToList();

    public bool CanApply => Items.Any(item => item.IsTicked);

    public string ApplyButtonText => $"Move {DesktopMoveText.Things(Items.Count(item => item.IsTicked))}";

    public string TotalText => HasPreview
        ? $"Ticked: {DesktopMoveText.Total(Items.Where(item => item.IsTicked).Select(item => item.Item))}"
        : string.Empty;

    public bool CanPutBack => _last is not null;

    public string LastText => _last is null
        ? string.Empty
        : $"Last change: {DesktopMoveText.Things(_last.Moved.Count)} moved, at {_last.FinishedAtUtc.ToLocalTime():t} on {_last.FinishedAtUtc.ToLocalTime():d}.";

    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => Message.Length > 0;

    internal void ShowPreview(DesktopMovePreview? preview)
    {
        foreach (var row in Items)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Items.Clear();
        LeftAlone.Clear();
        _preview = preview;
        if (preview is not null)
        {
            foreach (var item in preview.Items)
            {
                var row = new DesktopMoveItemViewModel(item, Card);
                row.PropertyChanged += OnRowChanged;
                Items.Add(row);
            }

            foreach (var alone in preview.LeftAlone)
            {
                LeftAlone.Add($"{alone.Name}: {alone.Reason}");
            }
        }

        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(HasLeftAlone));
        RaiseTicks();
    }

    /// <summary>After Move or Put back: the rows go, and anything not moved is listed with its reason.</summary>
    internal void ShowOutcome(string summary, IReadOnlyList<DesktopLeftAlone> notMoved)
    {
        ShowPreview(null);
        foreach (var alone in notMoved)
        {
            LeftAlone.Add($"{alone.Name}: {alone.Reason}");
        }

        OnPropertyChanged(nameof(HasLeftAlone));
        Message = summary;
    }

    internal void ShowLast(DesktopLastChange? last)
    {
        _last = last;
        OnPropertyChanged(nameof(CanPutBack));
        OnPropertyChanged(nameof(LastText));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopMoveItemViewModel.IsTicked))
        {
            RaiseTicks();
        }
    }

    private void RaiseTicks()
    {
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(ApplyButtonText));
        OnPropertyChanged(nameof(TotalText));
    }
}
