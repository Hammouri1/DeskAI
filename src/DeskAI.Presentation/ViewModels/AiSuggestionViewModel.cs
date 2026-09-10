using System.Globalization;
using DeskAI.Core.Ai;

namespace DeskAI.App.ViewModels;

public sealed class AiSuggestionViewModel
{
    private AiSuggestionViewModel(string fileName, string category, string confidence, string provider, string reason)
    {
        FileName = fileName;
        Category = category;
        Confidence = confidence;
        Provider = provider;
        Reason = reason;
    }

    public string FileName { get; }
    public string Category { get; }
    public string Confidence { get; }
    public string Provider { get; }
    public string Reason { get; }

    public static AiSuggestionViewModel FromSuggestion(
        OrganizationSuggestion suggestion,
        string fileName,
        string providerDisplayName) =>
        new(
            fileName,
            suggestion.Category.ToString(),
            suggestion.Confidence.ToString("P0", CultureInfo.CurrentCulture),
            providerDisplayName,
            suggestion.Reason);
}
