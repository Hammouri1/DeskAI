using DeskAI.Core.Templates;
using DeskAI.Core.Workspace;

namespace DeskAI.Core.Tests;

/// <summary>
/// The folder templates are the one place DeskAI names folders on someone's behalf, so every
/// name is checked here: a single plain folder name, few enough, readable, and matching the
/// starter pack whose rules point at it.
/// </summary>
public sealed class FolderTemplateCatalogTests
{
    private static readonly string[] TechnicalWords =
    [
        "metadata", "endpoint", "provider", "schema", "sqlite", "deterministic", "authorization",
        "telemetry", "dto", "api", "index", "token", "json", "http", "llm", "scope", "query", "regex",
    ];

    [Fact]
    public void There_is_one_template_per_starter_pack_in_the_same_order()
    {
        Assert.Equal(
            StarterPackCatalog.All.Select(pack => pack.Id),
            FolderTemplateCatalog.All.Select(template => template.Id));
        Assert.Equal(
            StarterPackCatalog.All.Select(pack => pack.Name),
            FolderTemplateCatalog.All.Select(template => template.Name));
    }

    [Fact]
    public void Find_returns_a_template_by_its_id_and_nothing_for_anything_else()
    {
        Assert.Equal(["Assignments", "Slides", "Screenshots", "Notes"], FolderTemplateCatalog.Find("student")!.Folders);
        Assert.Null(FolderTemplateCatalog.Find("Student"));
        Assert.Null(FolderTemplateCatalog.Find(FolderTemplate.OwnId));
    }

    [Fact]
    public void Every_template_makes_between_one_and_eight_single_plain_folder_names()
    {
        foreach (var template in FolderTemplateCatalog.All)
        {
            Assert.InRange(template.Folders.Count, 1, FolderTemplateCatalog.MaxFolders);
            Assert.Equal(template.Folders.Count, template.Folders.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            foreach (var name in template.Folders)
            {
                Assert.Null(FolderNameCheck.Check(name));
                Assert.DoesNotContain('\\', name);
                Assert.DoesNotContain('/', name);
                Assert.DoesNotContain(TechnicalWords, word => name.Contains(word, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [Fact]
    public void Every_pack_rule_destination_is_a_folder_in_that_packs_template()
    {
        foreach (var pack in StarterPackCatalog.All)
        {
            var template = FolderTemplateCatalog.Find(pack.Id)!;
            foreach (var rule in pack.Rules)
            {
                Assert.Contains(rule.Destination, template.Folders);
            }
        }
    }

    [Fact]
    public void The_folder_list_reads_as_one_line()
    {
        Assert.Equal("Screenshots, Installers", FolderTemplateCatalog.Find("minimal")!.FolderList);
        Assert.Equal("Your own folders", FolderTemplate.Own(["Tax"]).Name);
    }
}
