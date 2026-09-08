using DeskAI.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace DeskAI.App.Converters;

/// <summary>
/// Maps a presentation status to a system semantic colour.
/// </summary>
/// <remarks>
/// System brushes are used rather than invented colours so the palette stays correct in
/// light, dark, and high-contrast themes. Every place these brushes are used also shows
/// an icon and a word, because colour must never be the only carrier of meaning.
/// </remarks>
public sealed partial class StatusLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Resource(value switch
        {
            PreviewStatusLevel.Blocked => "SystemFillColorCriticalBrush",
            PreviewStatusLevel.Attention => "SystemFillColorCautionBrush",
            _ => "SystemFillColorSuccessBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Status brushes are display-only.");

    internal static Brush Resource(string key) =>
        Application.Current.Resources[key] as Brush
        ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
}

/// <summary>Maps a presentation status to its paired Segoe Fluent icon.</summary>
public sealed partial class StatusLevelToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        PreviewStatusLevel.Blocked => "",    // Cancel
        PreviewStatusLevel.Attention => "",  // Warning
        _ => "",                             // CheckMark
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Status glyphs are display-only.");
}

/// <summary>Maps a presentation status to the tinted background behind its badge.</summary>
public sealed partial class StatusLevelToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        StatusLevelToBrushConverter.Resource(value switch
        {
            PreviewStatusLevel.Blocked => "SystemFillColorCriticalBackgroundBrush",
            PreviewStatusLevel.Attention => "SystemFillColorCautionBackgroundBrush",
            _ => "SystemFillColorSuccessBackgroundBrush",
        });

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("Status backgrounds are display-only.");
}
