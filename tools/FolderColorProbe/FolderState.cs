namespace DeskAI.FolderColorProbe;

/// <summary>
/// What colouring a folder can change: the folder's attributes and its <c>desktop.ini</c>
/// (whether it exists, its exact bytes, its attributes). Put back restores exactly this.
/// </summary>
internal sealed record FolderState(FileAttributes Attributes, byte[]? IniBytes, FileAttributes? IniAttributes)
{
    internal const string IniName = "desktop.ini";

    internal static FolderState Read(string folder)
    {
        var ini = Path.Combine(folder, IniName);
        return File.Exists(ini)
            ? new FolderState(File.GetAttributes(folder), File.ReadAllBytes(ini), File.GetAttributes(ini))
            : new FolderState(File.GetAttributes(folder), null, null);
    }

    /// <summary>Puts the folder back to <paramref name="state"/>; a <c>desktop.ini</c> that was not there is removed.</summary>
    internal static void Restore(string folder, FolderState state)
    {
        var ini = Path.Combine(folder, IniName);
        if (File.Exists(ini))
        {
            // Windows refuses to overwrite or delete a hidden or system file otherwise.
            File.SetAttributes(ini, FileAttributes.Normal);
        }

        if (state.IniBytes is null)
        {
            File.Delete(ini);
        }
        else
        {
            File.WriteAllBytes(ini, state.IniBytes);
            File.SetAttributes(ini, state.IniAttributes ?? FileAttributes.Normal);
        }

        File.SetAttributes(folder, state.Attributes);
    }

    internal static IReadOnlyList<string> Differences(FolderState before, FolderState after)
    {
        var differences = new List<string>();
        if (before.Attributes != after.Attributes)
        {
            differences.Add($"folder attributes {before.Attributes} became {after.Attributes}");
        }

        if (before.IniBytes is null && after.IniBytes is not null)
        {
            differences.Add("desktop.ini was added");
        }
        else if (before.IniBytes is not null && after.IniBytes is null)
        {
            differences.Add("desktop.ini was removed");
        }
        else if (before.IniBytes is not null && after.IniBytes is not null)
        {
            if (!before.IniBytes.AsSpan().SequenceEqual(after.IniBytes))
            {
                differences.Add("desktop.ini contents changed");
            }

            if (before.IniAttributes != after.IniAttributes)
            {
                differences.Add($"desktop.ini attributes {before.IniAttributes} became {after.IniAttributes}");
            }
        }

        return differences;
    }
}
