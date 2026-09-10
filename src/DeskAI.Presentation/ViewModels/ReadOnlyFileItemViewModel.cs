using DeskAI.Core.Files;
using System.Globalization;

namespace DeskAI.App.ViewModels;

public sealed class ReadOnlyFileItemViewModel
{
    private ReadOnlyFileItemViewModel(string name, string folder, string size, string modified)
    {
        Name = name;
        Folder = folder;
        Size = size;
        Modified = modified;
    }

    public string Name { get; }
    public string Folder { get; }
    public string Size { get; }
    public string Modified { get; }

    public static ReadOnlyFileItemViewModel FromFile(FileItem file)
    {
        var folder = Path.GetDirectoryName(file.RelativePath);
        return new ReadOnlyFileItemViewModel(
            Path.GetFileName(file.RelativePath),
            string.IsNullOrEmpty(folder) ? "Top level" : folder,
            FormatSize(file.SizeBytes),
            file.ModifiedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
