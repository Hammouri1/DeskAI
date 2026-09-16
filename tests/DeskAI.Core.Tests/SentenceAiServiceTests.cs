using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Classification;

namespace DeskAI.Core.Tests;

/// <summary>
/// What the service that lets AI read a typed sentence can reach, and the two-step flow with the
/// person between the steps. The pages are tested through the whole app in
/// <c>SentenceAiPageTests</c>; this fixes the reach and the refusals.
/// </summary>
public sealed class SentenceAiServiceTests
{
    private static readonly AiSettings OpenRouter = AiSettings.Default with
    {
        Mode = AiMode.Cloud,
        ProviderId = "openrouter",
        ModelId = "test-model",
        CredentialReference = "DeskAI/OpenRouter",
        CloudConsentGranted = true,
    };

    [Fact]
    public void Constructor_CannotReachAnythingThatReadsOrChangesAFile()
    {
        var forbidden = new[]
        {
            typeof(IFolderTidyExecutor),
            typeof(IOperationJournal),
            typeof(IPlanRepository),
            typeof(IFileScanner),
            typeof(IFileIndex),
            typeof(IMetadataIndexService),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
            typeof(IAuthorizedRootRepository),
            typeof(IReadOnlyFolderService),
            typeof(IKnownFolders),
        };

        var dependencies = typeof(SentenceAiService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
        Assert.Equal(3, dependencies.Length);
    }

    [Fact]
    public async Task With_AI_off_nothing_is_prepared_and_the_reason_says_where_to_turn_it_on()
    {
        var ai = new RecordingProvider();
        var service = new SentenceAiService(new FixedSettings(AiSettings.Default), ai, new FixedClock());

        var prepared = await service.PrepareAsync(SentenceTask.SearchPhrase, "photos from my trip", TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Equal("Turn on AI in Privacy and AI first.", prepared.Explanation);
        Assert.False((await service.GetStatusAsync(TestContext.Current.CancellationToken)).IsSetUp);
        Assert.Empty(ai.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_sentence_is_not_sent(string sentence)
    {
        var ai = new RecordingProvider();
        var service = new SentenceAiService(new FixedSettings(OpenRouter), ai, new FixedClock());

        var prepared = await service.PrepareAsync(SentenceTask.SearchPhrase, sentence, TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Equal("Type something first.", prepared.Explanation);
    }

    [Fact]
    public async Task A_sentence_longer_than_the_bound_is_refused_before_anything_is_built()
    {
        var service = new SentenceAiService(new FixedSettings(OpenRouter), new RecordingProvider(), new FixedClock());

        var prepared = await service.PrepareAsync(SentenceTask.SearchPhrase, new string('a', AiSentenceRequest.MaxSentenceLength + 1), TestContext.Current.CancellationToken);

        Assert.Null(prepared.Question);
        Assert.Contains("shorter", prepared.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preparing_sends_nothing_and_names_the_service_and_its_address()
    {
        var ai = new RecordingProvider();
        var service = new SentenceAiService(new FixedSettings(OpenRouter), ai, new FixedClock());

        var prepared = await service.PrepareAsync(SentenceTask.RuleSentence, "  put my bank statements somewhere tidy  ", TestContext.Current.CancellationToken);

        var question = Assert.IsType<SentenceAiQuestion>(prepared.Question);
        Assert.Equal("OpenRouter", question.ServiceName);
        Assert.Equal("openrouter.ai", question.Destination);
        Assert.Equal("put my bank statements somewhere tidy", question.Sentence);
        Assert.Equal(question.Sentence, question.Request.Sentence);
        Assert.Equal(SentenceTask.RuleSentence, question.Request.Task);
        Assert.Equal(new DateOnly(2026, 9, 16), question.Request.TodayUtc);
        Assert.Empty(ai.Requests);
    }

    [Fact]
    public async Task Asking_sends_the_prepared_request_and_returns_a_sentence_DeskAI_can_read()
    {
        var ai = new RecordingProvider
        {
            Json = """{"schemaVersion":"1","ending":".pdf","category":null,"largerThanBytes":null,"smallerThanBytes":null,"olderThanDays":null,"nameContains":"statement","destination":"Bank"}""",
        };
        var service = new SentenceAiService(new FixedSettings(OpenRouter), ai, new FixedClock());
        var question = (await service.PrepareAsync(SentenceTask.RuleSentence, "put my bank statements somewhere tidy", TestContext.Current.CancellationToken)).Question!;

        var answer = await service.AskAsync(question, TestContext.Current.CancellationToken);

        Assert.True(answer.WasSent);
        Assert.True(answer.Succeeded);
        Assert.Equal("move .pdf statement into Bank", answer.Reading);
        Assert.Same(question.Request, Assert.Single(ai.Requests));
        Assert.Contains("OpenRouter read it as", answer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_changed_AI_choice_between_looking_and_sending_refuses_without_sending()
    {
        var ai = new RecordingProvider();
        var settings = new FixedSettings(OpenRouter);
        var service = new SentenceAiService(settings, ai, new FixedClock());
        var question = (await service.PrepareAsync(SentenceTask.SearchPhrase, "photos", TestContext.Current.CancellationToken)).Question!;
        settings.Current = OpenRouter with { ProviderId = "groq", CredentialReference = "DeskAI/Groq" };

        var answer = await service.AskAsync(question, TestContext.Current.CancellationToken);

        Assert.False(answer.WasSent);
        Assert.Contains("changed since you looked", answer.Message, StringComparison.Ordinal);
        Assert.Empty(ai.Requests);
    }

    [Fact]
    public async Task An_answer_that_fails_the_checks_is_ignored_with_a_reason()
    {
        var ai = new RecordingProvider { Json = """{"schemaVersion":"1","endings":[],"categories":["Malware"],"largerThanBytes":null,"smallerThanBytes":null,"changedInLastDays":null,"text":null}""" };
        var service = new SentenceAiService(new FixedSettings(OpenRouter), ai, new FixedClock());
        var question = (await service.PrepareAsync(SentenceTask.SearchPhrase, "photos", TestContext.Current.CancellationToken)).Question!;

        var answer = await service.AskAsync(question, TestContext.Current.CancellationToken);

        Assert.True(answer.WasSent);
        Assert.False(answer.Succeeded);
        Assert.Null(answer.Reading);
        Assert.Contains("did not pass DeskAI's checks", answer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_connection_refusal_is_passed_on_as_words()
    {
        var ai = new RecordingProvider { Status = AiProviderStatus.CostLimitReached, Message = "You have reached today's online AI limit. Nothing was sent." };
        var service = new SentenceAiService(new FixedSettings(OpenRouter), ai, new FixedClock());
        var question = (await service.PrepareAsync(SentenceTask.SearchPhrase, "photos", TestContext.Current.CancellationToken)).Question!;

        var answer = await service.AskAsync(question, TestContext.Current.CancellationToken);

        Assert.False(answer.Succeeded);
        Assert.Equal("You have reached today's online AI limit. Nothing was sent.", answer.Message);
    }

    private sealed class FixedSettings(AiSettings settings) : IAiSettingsRepository
    {
        public AiSettings Current { get; set; } = settings;

        public Task<AiSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);

        public Task SaveAsync(AiSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class RecordingProvider : IOrganizationSuggestionProvider
    {
        public List<AiSentenceRequest> Requests { get; } = [];

        public string Json { get; set; } = """{"schemaVersion":"1","endings":[],"categories":["Images"],"largerThanBytes":null,"smallerThanBytes":null,"changedInLastDays":null,"text":null}""";

        public AiProviderStatus Status { get; set; } = AiProviderStatus.Success;

        public string Message { get; set; } = "OpenRouter read the sentence.";

        public Task<OrganizationSuggestionResponse> SuggestAsync(OrganizationSuggestionRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new OrganizationSuggestionResponse(AiProviderStatus.Disabled, "test", [], "not used"));

        public Task<AiSentenceResponse> ReadSentenceAsync(AiSentenceRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new AiSentenceResponse(Status, "OpenRouter", Status == AiProviderStatus.Success ? Json : null, Message));
        }
    }
}
