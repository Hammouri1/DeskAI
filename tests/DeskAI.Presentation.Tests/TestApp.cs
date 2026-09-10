using DeskAI.AI.Transport;
using DeskAI.App.Composition;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace DeskAI.Presentation.Tests;

/// <summary>
/// A whole DeskAI, built the way the app builds itself, living entirely in a temp folder.
/// </summary>
/// <remarks>
/// It uses the app's own registration, a real SQLite database, the real scanner, and the
/// real safety checks. Only three things are swapped: the credential store (so no key is
/// ever written to Windows), the internet (so no request ever leaves the machine), and
/// Windows notifications. The folders tests connect are generated under <see cref="Sandbox"/>.
/// </remarks>
internal sealed class TestApp : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private TestApp(ServiceProvider services, TemporaryDirectory directory)
    {
        _services = services;
        Directory = directory;
    }

    public TemporaryDirectory Directory { get; }

    /// <summary>Where tests put the generated folders they connect.</summary>
    public string Sandbox => System.IO.Path.Combine(Directory.Path, "folders");

    public RecordingAiTransport Internet => (RecordingAiTransport)_services.GetRequiredService<IAiHttpTransport>();

    public InMemoryCredentialVault Vault => (InMemoryCredentialVault)_services.GetRequiredService<ICredentialVault>();

    public RecordingNotifier Notifier => (RecordingNotifier)_services.GetRequiredService<IFindingNotifier>();

    public T Get<T>() where T : notnull => _services.GetRequiredService<T>();

    public static async Task<TestApp> StartAsync()
    {
        var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDeskAiApplication(
            System.IO.Path.Combine(directory.Path, "deskai.db"),
            [System.IO.Path.Combine(directory.Path, "protected")],
            demo => demo.BasePath = System.IO.Path.Combine(directory.Path, "demos"));

        // Replace, never add alongside: a second registration would leave the real one
        // reachable through IEnumerable<T>.
        Replace<ICredentialVault>(services, new InMemoryCredentialVault());
        Replace<IAiHttpTransport>(services, new RecordingAiTransport());
        Replace<IFindingNotifier>(services, new RecordingNotifier());

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
            Directory.CreateDummyFile(System.IO.Path.Combine("folders", name, file));
        }

        return folder;
    }

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();
        Directory.Dispose();
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
