using System.Reflection;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Ai;
using DeskAI.Core.Tidy;

namespace DeskAI.Core.Tests;

/// <summary>
/// What the service that talks to AI about your own folders is able to reach. The behaviour
/// is tested through the whole app in <c>TidyAiTests</c>; this fixes its reach.
/// </summary>
public sealed class TidyAiServiceTests
{
    /// <summary>
    /// It is handed file descriptions by the page and hands a request to the AI connection. It
    /// must not be able to look in a folder, open a file, or change one itself, so a later
    /// change that gives it one of these fails here rather than in someone's folder.
    /// </summary>
    [Fact]
    public void Constructor_CannotReachAnythingThatReadsOrChangesAFile()
    {
        var forbidden = new[]
        {
            typeof(IPlanExecutor),
            typeof(IFolderTidyExecutor),
            typeof(IUndoService),
            typeof(IOperationJournal),
            typeof(IFileScanner),
            typeof(IFileIndex),
            typeof(IMetadataIndexService),
            typeof(IContentTextExtractor),
            typeof(ICredentialVault),
        };

        var dependencies = typeof(TidyAiService)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.All(forbidden, type => Assert.DoesNotContain(type, dependencies));
    }

    [Fact]
    public void Only_file_type_size_and_date_and_name_can_ever_be_sent_from_your_own_folders()
    {
        Assert.Equal(
            new[] { DisclosureCategory.Extension, DisclosureCategory.Metadata, DisclosureCategory.FileName }.Order(),
            TidyAiService.RealFolderShareable.Order());
    }
}
