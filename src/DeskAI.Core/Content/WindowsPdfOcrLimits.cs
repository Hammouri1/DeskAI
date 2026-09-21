namespace DeskAI.Core.Content;

/// <summary>Product limits shared by the Windows adapter and honest UI wording.</summary>
public static class WindowsPdfOcrLimits
{
    public const int MaxPdfBytes = 8 * 1024 * 1024;
    public const int MaxPages = 20;
    public const int MaxTextBytes = 256 * 1024;
    public const int MaxFiles = 10;
}
