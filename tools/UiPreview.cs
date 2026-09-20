using DeskAI.AI.Transport;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using System.Text;

namespace DeskAI.App;

/// <summary>
/// Compiled only by -p:DeskAiUiPreview=true. The real views and services run against generated
/// files, never the owner's database or known folders. No network, key vault, system picker,
/// wallpaper, notifications, tray, or hosted timer is reachable through this composition.
/// The unique temporary folder is deliberately retained for inspection, not recursively deleted.
/// </summary>
internal sealed class UiPreview : IKnownFolders, IFolderPickerService, IPicturePickerService,
    IBackupFilePickerService, ICredentialVault, IAiHttpTransport, IWallpaperSetter, IFindingNotifier
{
    private static readonly UiPreview Instance = new();
    private readonly string _stateDirectory;

    private UiPreview()
    {
        _stateDirectory = Directory.CreateTempSubdirectory("DeskAI-UI-").FullName;
        Directory.CreateDirectory(Path.Combine(_stateDirectory, "state"));
        var folders = Path.Combine(_stateDirectory, "samples");
        Desktop = Directory.CreateDirectory(Path.Combine(folders, "Desktop")).FullName;
        Downloads = Directory.CreateDirectory(Path.Combine(folders, "Downloads")).FullName;
        Documents = Directory.CreateDirectory(Path.Combine(folders, "Documents")).FullName;
        Pictures = Directory.CreateDirectory(Path.Combine(folders, "Pictures")).FullName;
        File.WriteAllText(Path.Combine(Downloads, "Holiday plans.txt"), "Generated UI test file. No personal content.");
        File.WriteAllText(Path.Combine(Downloads, "Shopping list.csv"), "item,count\nnotebook,2");
        File.WriteAllText(Path.Combine(Downloads, "Project notes.md"), "# Sample notes\nGenerated for UI testing.");
        File.WriteAllBytes(Path.Combine(Downloads, "Lesson handout.pdf"), SamplePdf("generated nebula lesson"));
        File.WriteAllText(Path.Combine(Downloads, "Broken sample.pdf"), "%PDF-1.4 generated broken sample");
        var nested = Directory.CreateDirectory(Path.Combine(Downloads, "Presentations"));
        File.WriteAllBytes(Path.Combine(nested.FullName, "Nested handout.pdf"),
            SamplePdf("generated aurora presentation"));
        foreach (var file in Directory.EnumerateFiles(Downloads, "*", SearchOption.AllDirectories))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        }
    }

    /// <summary>A tiny, generated PDF for the manual preview; no document parser runs here.</summary>
    private static byte[] SamplePdf(string text)
    {
        var stream = $"BT /F1 18 Tf 50 700 Td ({text}) Tj ET";
        var parts = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream",
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < parts.Length; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(index + 1).Append(" 0 obj\n").Append(parts[index]).Append("\nendobj\n");
        }

        var xref = pdf.Length;
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            pdf.Append(offset.ToString("D10", System.Globalization.CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        }

        pdf.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    // Keep the protected application database apart from generated sample folders.
    // A parent-level protected path would correctly refuse the samples as children.
    public static string StateDirectory => Path.Combine(Instance._stateDirectory, "state");

    public string Desktop { get; }

    public string Downloads { get; }

    public string Documents { get; }

    public string Pictures { get; }

    public bool IsAvailable => false;

    public static void Configure(IServiceCollection services)
    {
        Replace<IKnownFolders>(services);
        Replace<IFolderPickerService>(services);
        Replace<IPicturePickerService>(services);
        Replace<IBackupFilePickerService>(services);
        Replace<ICredentialVault>(services);
        Replace<IAiHttpTransport>(services);
        Replace<IWallpaperSetter>(services);
        Replace<IFindingNotifier>(services);
        services.RemoveAll<IHostedService>();
        services.RemoveAll<IBackgroundPresence>();
        services.AddSingleton<IBackgroundPresence, NoBackgroundPresence>();
    }

    private static void Replace<T>(IServiceCollection services) where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton((T)(object)Instance);
    }

    public Task<FolderPickResult> PickFolderAsync(nint ownerWindowHandle) =>
        Task.FromResult(FolderPickResult.Picked(Downloads));

    public Task<FolderPickResult> PickPictureAsync(nint ownerWindowHandle) =>
        Task.FromResult(FolderPickResult.Cancelled);

    public Task<FolderPickResult> PickSaveAsync(nint ownerWindowHandle, string suggestedFileName) =>
        Task.FromResult(FolderPickResult.Cancelled);

    public Task<FolderPickResult> PickOpenAsync(nint ownerWindowHandle) =>
        Task.FromResult(FolderPickResult.Cancelled);

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default) =>
        Task.FromException(new InvalidOperationException("Keys cannot be saved in the UI preview."));

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<AiHttpResponse> PostJsonAsync(Uri endpoint, string json,
        IReadOnlyDictionary<string, string> headers, int maximumResponseBytes, CancellationToken cancellationToken) =>
        Task.FromException<AiHttpResponse>(new HttpRequestException("Network access is disabled in the UI preview."));

    public string? ReadCurrent() => null;

    public void Apply(string imagePath) => throw new InvalidOperationException("Wallpaper cannot change in the UI preview.");

    public void Notify(string title, string message)
    {
    }
}
