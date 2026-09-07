using CommunityToolkit.Mvvm.ComponentModel;

namespace DeskAI.App.ViewModels;

public sealed class ShellViewModel : ObservableObject
{
    public string ApplicationName { get; } = "DeskAI";

    public string ActiveMode { get; } = "Rule Engine Only";

    public string FoundationStatus { get; } = "Practice mode—using sample files only.";
}
