using DeskAI.Core.Classification;
using DeskAI.Core.Recipes;

namespace DeskAI.Core.Tests;

public sealed class FolderRecipeTests
{
    [Fact]
    public void DefaultRecipe_MapsClassificationToRelativeDirectory()
    {
        var recipe = DefaultFolderRecipe.Create();

        Assert.Equal(@"Images\Screenshots", recipe.FindDestination(FileCategory.Screenshots));
        Assert.Equal("Code", recipe.FindDestination(FileCategory.SourceCode));
        Assert.Null(recipe.FindDestination(FileCategory.Unknown));
    }

    [Fact]
    public void CustomRecipe_CanMapCategoryWithoutChangingClassifier()
    {
        var recipe = new FolderRecipe(
            "student",
            "Student",
            1,
            [new FolderRecipeEntry(FileCategory.Documents, @"University\Assignments")]);

        Assert.Equal(@"University\Assignments", recipe.FindDestination(FileCategory.Documents));
    }

    [Theory]
    [InlineData(@"..\Outside")]
    [InlineData(@"C:\Absolute")]
    [InlineData(@"Folder\..\Outside")]
    [InlineData("folder:stream")]
    [InlineData("folder*")]
    public void RecipeEntry_RejectsUnsafeDestination(string destination)
    {
        var action = () => new FolderRecipeEntry(FileCategory.Documents, destination);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void Recipe_RejectsDuplicateCategoryMappings()
    {
        var action = () => new FolderRecipe(
            "duplicate",
            "Duplicate",
            1,
            [
                new FolderRecipeEntry(FileCategory.Documents, "Documents"),
                new FolderRecipeEntry(FileCategory.Documents, "OtherDocuments"),
            ]);

        Assert.Throws<ArgumentException>(action);
    }
}
