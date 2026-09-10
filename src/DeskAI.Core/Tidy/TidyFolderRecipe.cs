using DeskAI.Core.Classification;
using DeskAI.Core.Recipes;

namespace DeskAI.Core.Tidy;

/// <summary>
/// The folders tidying puts things into, named the way people name them.
/// </summary>
/// <remarks>
/// Separate from the practice recipe so its everyday names ("Pictures", "Music") can differ
/// without changing the practice run. Every destination is relative, so it always lands
/// inside the folder being tidied.
/// </remarks>
public static class TidyFolderRecipe
{
    public static FolderRecipe Create() => new(
        id: "tidy",
        displayName: "Tidy",
        version: 1,
        entries:
        [
            new(FileCategory.Documents, "Documents"),
            new(FileCategory.Presentations, @"Documents\Presentations"),
            new(FileCategory.Spreadsheets, @"Documents\Spreadsheets"),
            new(FileCategory.Images, "Pictures"),
            new(FileCategory.Screenshots, @"Pictures\Screenshots"),
            new(FileCategory.Videos, "Videos"),
            new(FileCategory.Audio, "Music"),
            new(FileCategory.Archives, "Archives"),
            new(FileCategory.Installers, "Installers"),
            new(FileCategory.SourceCode, "Code"),
            new(FileCategory.Data, "Data"),
        ]);
}
