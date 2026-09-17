using DeskAI.AI.Transport;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

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
        var folders = Path.Combine(_stateDirectory, "samples");
        Desktop = Directory.CreateDirectory(Path.Combine(folders, "Desktop")).FullName;
        Downloads = Directory.CreateDirectory(Path.Combine(folders, "Downloads")).FullName;
        Documents = Directory.CreateDirectory(Path.Combine(folders, "Documents")).FullName;
        Pictures = Directory.CreateDirectory(Path.Combine(folders, "Pictures")).FullName;
        File.WriteAllText(Path.Combine(Downloads, "Holiday plans.txt"), "Generated UI test file. No personal content.");
        File.WriteAllText(Path.Combine(Downloads, "Shopping list.csv"), "item,count\nnotebook,2");
        File.WriteAllText(Path.Combine(Downloads, "Project notes.md"), "# Sample notes\nGenerated for UI testing.");
        foreach (var file in Directory.EnumerateFiles(Downloads))
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        }
    }

    public static string StateDirectory => Instance._stateDirectory;

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
