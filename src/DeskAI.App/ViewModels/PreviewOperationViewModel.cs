using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Plans;

namespace DeskAI.App.ViewModels;

public sealed class PreviewOperationViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private readonly bool _isAllowed;
    private bool _isInteractionEnabled = true;
    private bool _isSelected;

    public PreviewOperationViewModel(
        PlanOperation operation,
        ValidationResult validation,
        Action selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(validation);
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));

        Id = operation.Id;
        OperationType = operation.Kind switch
        {
            PlanOperationKind.CreateDirectory => "Create folder",
            PlanOperationKind.MoveFile => "Move file",
            PlanOperationKind.RenameFile => "Rename file",
            _ => "Unknown operation",
        };
        Source = operation switch
        {
            MoveFileOperation move => move.SourceRelativePath,
            RenameFileOperation rename => rename.SourceRelativePath,
            _ => "Not applicable",
        };
        Destination = operation switch
        {
            CreateDirectoryOperation create => create.DestinationRelativePath,
            MoveFileOperation move => move.DestinationRelativePath,
            RenameFileOperation rename => rename.DestinationRelativePath,
            _ => "Unknown",
        };
        Reason = operation.Reason;
        Provenance = operation.Provenance.ToString();
        SafetyStatus = validation.Status == ValidationStatus.Blocked ? "Blocked" : "Allowed";
        SafetyExplanation = validation.Explanation;
        _isAllowed = validation.Status != ValidationStatus.Blocked;
        _isSelected = IsSelectable;
        DisplayName = Path.GetFileName(Source);
        FromFolder = FriendlyFolder(Path.GetDirectoryName(Source));
        ToFolder = FriendlyFolder(Path.GetDirectoryName(Destination));
        FileType = FriendlyFileType(Source);
        StatusText = _isAllowed ? "Ready" : "Not included";
        StatusDetail = _isAllowed
            ? "Ready to organize"
            : "Two sample files have the same name. Choose a different name later.";
    }

    public Guid Id { get; }
    public string OperationType { get; }
    public string Source { get; }
    public string Destination { get; }
    public string Reason { get; }
    public string Provenance { get; }
    public string SafetyStatus { get; }
    public string SafetyExplanation { get; }
    public string DisplayName { get; }
    public string FromFolder { get; }
    public string ToFolder { get; }
    public string FileType { get; }
    public string StatusText { get; }
    public string StatusDetail { get; }
    public bool IsSelectable => _isAllowed && _isInteractionEnabled;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            var safeValue = IsSelectable && value;
            if (SetProperty(ref _isSelected, safeValue))
            {
                _selectionChanged();
            }
        }
    }

    public void SetInteractionEnabled(bool value)
    {
        if (_isInteractionEnabled == value)
        {
            return;
        }

        _isInteractionEnabled = value;
        OnPropertyChanged(nameof(IsSelectable));
    }

    private static string FriendlyFolder(string? path) =>
        string.IsNullOrWhiteSpace(path) ? "Demo folder" : path.Replace("\\", " › ");

    private static string FriendlyFileType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "PDF document",
        ".png" or ".jpg" or ".jpeg" => "Image",
        ".xlsx" or ".xls" => "Spreadsheet",
        ".md" => "Notes",
        _ => "File",
    };
}
