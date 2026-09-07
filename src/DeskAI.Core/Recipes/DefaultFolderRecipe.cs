using DeskAI.Core.Classification;

namespace DeskAI.Core.Recipes;

public static class DefaultFolderRecipe
{
    public static FolderRecipe Create() => new(
        id: "default",
        displayName: "Default",
        version: 1,
        entries:
        [
            new(FileCategory.Documents, "Documents"),
            new(FileCategory.Presentations, @"Documents\Presentations"),
            new(FileCategory.Spreadsheets, @"Documents\Spreadsheets"),
            new(FileCategory.Images, "Images"),
            new(FileCategory.Screenshots, @"Images\Screenshots"),
            new(FileCategory.Videos, "Videos"),
            new(FileCategory.Audio, "Audio"),
            new(FileCategory.Archives, "Archives"),
            new(FileCategory.Installers, "Installers"),
            new(FileCategory.SourceCode, "Code"),
            new(FileCategory.Data, "Data"),
        ]);
}
