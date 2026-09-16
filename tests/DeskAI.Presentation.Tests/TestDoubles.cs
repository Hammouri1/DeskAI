using System.Collections.Concurrent;
using System.Net;
using DeskAI.AI.Transport;
using DeskAI.App.Services;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Execution;

namespace DeskAI.Presentation.Tests;

/// <summary>DeskAI "stopping" — the test's stand-in for a crash or a power cut.</summary>
internal sealed class SimulatedStop : Exception
{
    public SimulatedStop()
        : base("DeskAI stopped here, as if the computer had turned off.")
    {
    }
}

/// <summary>
/// The real journal, which can stop DeskAI at one chosen moment of a run.
/// </summary>
/// <remarks>
/// A tidy writes "about to move this file" before moving it and "moved" after. Stopping just
/// before one of those writes, or just after, leaves the journal and the disk exactly as a
/// crash at that moment would, because the real code ran up to that point. Nothing is faked
/// but the stop itself.
/// </remarks>
internal sealed class StoppingJournal(IOperationJournal inner) : IOperationJournal
{
    private Guid? _operation;
    private JournalOperationState _state;
    private bool _afterWriting;

    /// <summary>Stops instead of recording that <paramref name="operationId"/> reached <paramref name="state"/>.</summary>
    public void StopBefore(Guid operationId, JournalOperationState state) => Arm(operationId, state, afterWriting: false);

    /// <summary>Records that <paramref name="operationId"/> reached <paramref name="state"/>, then stops.</summary>
    public void StopAfter(Guid operationId, JournalOperationState state) => Arm(operationId, state, afterWriting: true);

    public async Task UpdateOperationAsync(
        Guid transactionId,
        Guid operationId,
        JournalOperationState state,
        string? failureMessage,
        CancellationToken cancellationToken = default)
    {
        var stop = _operation == operationId && _state == state;
        if (stop && !_afterWriting)
        {
            _operation = null;
            throw new SimulatedStop();
        }

        await inner.UpdateOperationAsync(transactionId, operationId, state, failureMessage, cancellationToken);
        if (stop)
        {
            _operation = null;
            throw new SimulatedStop();
        }
    }

    public Task CreateAsync(ExecutionJournalEntry entry, CancellationToken cancellationToken = default) =>
        inner.CreateAsync(entry, cancellationToken);

    public Task UpdateTransactionAsync(
        Guid transactionId,
        ExecutionTransactionState state,
        DateTimeOffset? finishedAtUtc,
        CancellationToken cancellationToken = default) =>
        inner.UpdateTransactionAsync(transactionId, state, finishedAtUtc, cancellationToken);

    public Task<ExecutionJournalEntry?> FindAsync(Guid transactionId, CancellationToken cancellationToken = default) =>
        inner.FindAsync(transactionId, cancellationToken);

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListRecentAsync(
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        inner.ListRecentAsync(maximumCount, cancellationToken);

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListIncompleteAsync(CancellationToken cancellationToken = default) =>
        inner.ListIncompleteAsync(cancellationToken);

    public Task<IReadOnlyList<ExecutionJournalEntry>> ListForRootAsync(
        Guid rootId,
        int maximumCount,
        CancellationToken cancellationToken = default) =>
        inner.ListForRootAsync(rootId, maximumCount, cancellationToken);

    private void Arm(Guid operationId, JournalOperationState state, bool afterWriting)
    {
        _operation = operationId;
        _state = state;
        _afterWriting = afterWriting;
    }
}

/// <summary>Stands in for Windows Credential Manager. Holds only generated test strings.</summary>
internal sealed class InMemoryCredentialVault : ICredentialVault
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.Ordinal);

    public Task SaveAsync(string reference, string secret, CancellationToken cancellationToken = default)
    {
        _secrets[reference] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> RetrieveAsync(string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_secrets.TryGetValue(reference, out var secret) ? secret : null);

    public Task RemoveAsync(string reference, CancellationToken cancellationToken = default)
    {
        _secrets.TryRemove(reference, out _);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for the internet. Records what DeskAI would have sent and answers with a
/// canned reply, so no test ever reaches a real, paid AI service.
/// </summary>
internal sealed class RecordingAiTransport : IAiHttpTransport
{
    public List<(Uri Endpoint, string Body, IReadOnlyDictionary<string, string> Headers)> Requests { get; } = [];

    /// <summary>Builds the reply from the request, so it can echo the file IDs it was sent.</summary>
    public Func<string, AiHttpResponse> Reply { get; set; } =
        _ => new AiHttpResponse(HttpStatusCode.InternalServerError, "{}");

    public Task<AiHttpResponse> PostJsonAsync(
        Uri endpoint,
        string json,
        IReadOnlyDictionary<string, string> headers,
        int maximumResponseBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((endpoint, json, headers));
        return Task.FromResult(Reply(json));
    }
}

/// <summary>
/// Stands in for Windows' wallpaper: remembers what it was set to. The real one is never in a
/// test, so no test can change the developer's wallpaper.
/// </summary>
internal sealed class RecordingWallpaperSetter : IWallpaperSetter
{
    /// <summary>What Windows "shows": a path, an empty string for a plain colour, or null when it will not say.</summary>
    public string? Current { get; set; } = string.Empty;

    /// <summary>Every path it was asked to set, in order.</summary>
    public List<string> Sets { get; } = [];

    /// <summary>When set, the next Set refuses with this reason, as Windows would under a policy.</summary>
    public string? RefuseWith { get; set; }

    public string? ReadCurrent() => Current;

    public void Set(string imagePath)
    {
        if (RefuseWith is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        Sets.Add(imagePath);
        Current = imagePath;
    }
}

/// <summary>
/// The person's four folders, all inside the test's own temp folder. Never the real ones.
/// "Documents" is the sandbox's folders root, so every generated folder counts as inside it.
/// </summary>
internal sealed class SandboxKnownFolders(string desktop, string downloads, string documents, string pictures) : IKnownFolders
{
    public string? Desktop { get; set; } = desktop;

    public string? Downloads { get; set; } = downloads;

    public string? Documents { get; set; } = documents;

    public string? Pictures { get; set; } = pictures;
}

/// <summary>Stands in for the window painter: remembers every look it was asked to apply.</summary>
internal sealed class RecordingAppearanceApplier : IAppearanceApplier
{
    public List<DeskAI.Core.Appearance.AppearanceSettings> Applied { get; } = [];

    public void Apply(DeskAI.Core.Appearance.AppearanceSettings settings) => Applied.Add(settings);
}

internal sealed class RecordingNotifier : IFindingNotifier
{
    public List<string> Messages { get; } = [];

    public bool IsAvailable => true;

    public void Notify(string title, string message) => Messages.Add(message);
}

/// <summary>The icon near the clock, as a test can see it.</summary>
internal sealed class RecordingPresence : IBackgroundPresence
{
    /// <summary>Every tooltip it has been given, in order. The last is what it says now.</summary>
    public List<string> Tooltips { get; } = [];

    /// <summary>Whether its menu showed checking as paused, alongside each tooltip.</summary>
    public List<bool> PausedStates { get; } = [];

    public bool IsShowing { get; private set; }

    public void Show(string tooltip, bool isPaused)
    {
        IsShowing = true;
        Tooltips.Add(tooltip);
        PausedStates.Add(isPaused);
    }

    public void Update(string tooltip, bool isPaused)
    {
        if (IsShowing)
        {
            Tooltips.Add(tooltip);
            PausedStates.Add(isPaused);
        }
    }

    public void Hide() => IsShowing = false;

    public event EventHandler? OpenRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? QuitRequested;

    public void RaiseOpen() => OpenRequested?.Invoke(this, EventArgs.Empty);

    public void RaisePauseToggle() => PauseToggleRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}
