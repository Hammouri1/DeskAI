using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;

namespace DeskAI.Core.Tests;

/// <summary>
/// What Ask DeskAI can reach, and how a question's answer is read (V1.1, ADR 0035). The answers
/// themselves are tested through the whole app in <c>AskDeskAiPageTests</c>.
/// </summary>
public sealed class AskDeskAiServiceTests
{
    [Fact]
    public void Constructor_CannotReachAnythingThatOpensOrChangesAFile()
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
            typeof(IFileFingerprinter),
            typeof(ICredentialVault),
            typeof(IReadOnlyFolderService),
        };

        var dependencies = typeof(AskDeskAiService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    [Theory]
    [InlineData("""{"schemaVersion":"1","kind":"space","folder":"Downloads","search":null}""", AskIntentKind.Space, "Downloads")]
    [InlineData("""{"schemaVersion":"1","kind":"tidy","folder":null,"search":null}""", AskIntentKind.Tidy, null)]
    [InlineData("""{"schemaVersion":"1","kind":"unsure","folder":null,"search":null}""", AskIntentKind.Unsure, null)]
    [InlineData("""{"schemaVersion":"1","kind":"tidy","folder":"..\\Windows\\System32","search":null}""", AskIntentKind.Tidy, "Windows System32")]
    public void A_question_answer_becomes_a_kind_and_harmless_folder_words(string json, AskIntentKind kind, string? folder)
    {
        var reading = AiSentenceReading.ReadQuestion(json, 8192);

        Assert.True(reading.IsValid, reading.Problem);
        Assert.Equal(kind, reading.Intent!.Kind);
        Assert.Equal(folder, reading.Intent.FolderName);
        Assert.Null(reading.Intent.SearchSentence);
    }

    [Fact]
    public void A_search_question_carries_the_search_in_DeskAI_s_own_words()
    {
        const string json = """{"schemaVersion":"1","kind":"search","folder":"Pictures","search":{"schemaVersion":"1","endings":[],"categories":["Images"],"largerThanBytes":null,"smallerThanBytes":null,"changedInLastDays":30,"text":"beach"}}""";

        var reading = AiSentenceReading.ReadQuestion(json, 8192);

        Assert.True(reading.IsValid, reading.Problem);
        Assert.Equal(AskIntentKind.Search, reading.Intent!.Kind);
        Assert.Equal("Pictures", reading.Intent.FolderName);
        Assert.Equal("photos last 30 days beach", reading.Intent.SearchSentence);
    }

    [Theory]
    [InlineData("""{"schemaVersion":"1","kind":"delete","folder":null,"search":null}""")]
    [InlineData("""{"schemaVersion":"1","kind":"search","folder":null,"search":null}""")]
    [InlineData("""{"schemaVersion":"1","kind":"tidy","folder":null,"search":null,"command":"rm -rf"}""")]
    [InlineData("""{"schemaVersion":"2","kind":"tidy","folder":null,"search":null}""")]
    [InlineData("""{"schemaVersion":"1","kind":"search","folder":null,"search":{"schemaVersion":"1","endings":[],"categories":["Malware"],"largerThanBytes":null,"smallerThanBytes":null,"changedInLastDays":null,"text":null}}""")]
    [InlineData("nope")]
    public void An_off_shape_question_answer_is_refused_whole(string json)
    {
        var reading = AiSentenceReading.ReadQuestion(json, 8192);

        Assert.False(reading.IsValid);
        Assert.Null(reading.Intent);
        Assert.False(string.IsNullOrWhiteSpace(reading.Problem));
    }
}
