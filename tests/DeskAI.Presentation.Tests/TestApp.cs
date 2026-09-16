using DeskAI.AI.Transport;
using DeskAI.App.Composition;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// A whole DeskAI, built the way the app builds itself, living entirely in a temp folder.
/// </summary>
/// <remarks>
/// It uses the app's own registration, a real SQLite database, the real scanner, and the
/// real safety checks. Only three things are swapped: the credential store (so no key is
/// ever written to Windows), the internet (so no request ever leaves the machine), and
/// Windows notifications. The folders tests connect are generated under <see cref="Sandbox"/>.
/// A test about crashes can also start one whose real journal can stop a run part-way, and
/// any test can close DeskAI and open it again over the same database.
/// </remarks>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    // Set once this DeskAI has been "closed" and a new one opened over the same folder, which
    // then owns the folder and deletes it at the end.
    private bool _handedOver;

    private TestApp(ServiceProvider services, TemporaryDirectory directory)
    {
        _services = services;
        Directory = directory;
    }

    public TemporaryDirectory Directory { get; }

    /// <summary>
    /// The journal, able to stop DeskAI part-way through a run. Only in an app started with
    /// <see cref="StartStoppableAsync"/>; otherwise the journal is the plain real one.
    /// </summary>
    public StoppingJournal Stopping => (StoppingJournal)_services.GetRequiredService<IOperationJournal>();

    /// <summary>Where tests put the generated folders they connect.</summary>
    public string Sandbox => System.IO.Path.Combine(Directory.Path, "folders");

    public RecordingAiTransport Internet => (RecordingAiTransport)_services.GetRequiredService<IAiHttpTransport>();

    public InMemoryCredentialVault Vault => (InMemoryCredentialVault)_services.GetRequiredService<ICredentialVault>();

    public RecordingNotifier Notifier => (RecordingNotifier)_services.GetRequiredService<IFindingNotifier>();

    public RecordingPresence Presence => (RecordingPresence)_services.GetRequiredService<IBackgroundPresence>();

    public RecordingAppearanceApplier Painter => (RecordingAppearanceApplier)_services.GetRequiredService<IAppearanceApplier>();

    public RecordingWallpaperSetter Wallpaper => (RecordingWallpaperSetter)_services.GetRequiredService<IWallpaperSetter>();

    public SandboxKnownFolders KnownFolders => (SandboxKnownFolders)_services.GetRequiredService<IKnownFolders>();

    /// <summary>The generated folder standing in for the person's Desktop. Created on first use.</summary>
    public string DesktopPath => System.IO.Path.Combine(Sandbox, "Desktop");

    /// <summary>The protected stand-in for DeskAI's own program folder, inside the test Desktop.</summary>
    public string ProgramFolderPath => System.IO.Path.Combine(DesktopPath, "DeskAI", "app");

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public static Task<TestApp> StartAsync() => StartAsync(new TemporaryDirectory(), stoppable: false);

    /// <summary>
    /// A DeskAI whose journal can stop it part-way through a run, as a crash would. Everything
    /// else is the same as <see cref="StartAsync()"/>.
    /// </summary>
    public static Task<TestApp> StartStoppableAsync() => StartAsync(new TemporaryDirectory(), stoppable: true);

    /// <summary>
    /// Closes this DeskAI and opens a new one over the same database and folders — the test's
    /// version of quitting DeskAI and starting it again. Nothing is carried over in memory.
    /// </summary>
    public async Task<TestApp> ReopenAsync()
    {
        await _services.DisposeAsync();
        _handedOver = true;
        return await StartAsync(Directory, stoppable: false);
    }

    private static async Task<TestApp> StartAsync(TemporaryDirectory directory, bool stoppable)
    {
        var services = new ServiceCollection();
        var database = System.IO.Path.Combine(directory.Path, "deskai.db");
        services.AddLogging();
        // Like the real app, DeskAI's own program folder is protected, and as on the owner's
        // machine it sits inside the Desktop. The Desktop must still connect (found 2026-09-16).
        var desktop = System.IO.Path.Combine(directory.Path, "folders", "Desktop");
        services.AddDeskAiApplication(
            database,
            [
                System.IO.Path.Combine(directory.Path, "protected"),
                System.IO.Path.Combine(desktop, "DeskAI", "app"),
            ]);

        // Replace, never add alongside: a second registration would leave the real one
        // reachable through IEnumerable<T>.
        Replace<ICredentialVault>(services, new InMemoryCredentialVault());
        Replace<IAiHttpTransport>(services, new RecordingAiTransport());
        Replace<IFindingNotifier>(services, new RecordingNotifier());
        Replace<IBackgroundPresence>(services, new RecordingPresence());
        Replace<IAppearanceApplier>(services, new RecordingAppearanceApplier());

        // The real wallpaper and the real Desktop must be unreachable from any test. Both
        // are replaced, and the Desktop is asserted to be inside this test's own folder.
        Replace<IWallpaperSetter>(services, new RecordingWallpaperSetter());
        if (!desktop.StartsWith(directory.Path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A test Desktop must live inside the test's own folder.");
        }

        Replace<IKnownFolders>(services, new SandboxKnownFolders(desktop));
        if (stoppable)
        {
            Replace<IOperationJournal>(services, new StoppingJournal(
                new SqliteOperationJournal(Options.Create(new DatabaseOptions { DatabasePath = database }))));
        }

        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync();
        return new TestApp(provider, directory);
    }

    /// <summary>Creates a generated folder inside the sandbox and returns its full path.</summary>
    public string MakeFolder(string name, params string[] files)
    {
        var folder = Directory.CreateDummyDirectory(System.IO.Path.Combine("folders", name));
        foreach (var file in files)
        {
            MakeFile(name, file);
        }

        return folder;
    }

    /// <summary>
    /// Creates one file in a generated folder, last changed <paramref name="age"/> ago.
    /// </summary>
    /// <remarks>
    /// Backdated by default: a file changed in the last few minutes is deliberately left alone
    /// by tidying, and every freshly generated file would otherwise look exactly like that.
    /// </remarks>
    public string MakeFile(string folder, string name, string content = "Generated DeskAI test data", TimeSpan? age = null)
    {
        var path = Directory.CreateDummyFile(System.IO.Path.Combine("folders", folder, name), content);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow - (age ?? TimeSpan.FromDays(3)));
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        if (!_handedOver)
        {
            Directory.Dispose();
        }
    }

    private static void Replace<T>(ServiceCollection services, T instance) where T : class
    {
        foreach (var existing in services.Where(descriptor => descriptor.ServiceType == typeof(T)).ToArray())
        {
            services.Remove(existing);
        }

        services.AddSingleton(instance);
    }
}
