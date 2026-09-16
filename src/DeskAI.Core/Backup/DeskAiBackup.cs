using DeskAI.Core.Rules;

namespace DeskAI.Core.Backup;

/// <summary>One rule as it is written to a backup file: its name, its conditions, and where it moves things.</summary>
/// <remarks>
/// No ID and no on/off flag, on purpose. A restored rule gets a new ID, and it always arrives
/// switched off, so leaving the flag out of the file makes that structural rather than a check.
/// </remarks>
public sealed record BackupRule(string Name, IReadOnlyList<RuleConditionData> Conditions, RuleActionData Action);

/// <summary>One saved search as it is written to a backup file.</summary>
public sealed record BackupSearch(string Name, string Phrase, bool IsPinned);

/// <summary>
/// What a backup file holds: rules and saved searches, and nothing else.
/// </summary>
/// <remarks>
/// Not in the file, by design: connected folders and their permissions (a permission is given
/// through the Windows picker, never carried in a file), the AI choice, any key, any path, the
/// index, and the tidy history. The file is untrusted input when it comes back in, exactly like
/// a stored database row: every rule and search is rebuilt through the same factories a typed
/// one uses.
/// </remarks>
public sealed record DeskAiBackup(
    int Version,
    DateTimeOffset MadeAtUtc,
    IReadOnlyList<BackupRule> Rules,
    IReadOnlyList<BackupSearch> SavedSearches)
{
    public const int CurrentVersion = 1;

    /// <summary>The most a backup file may be, in bytes. A real one is a few kilobytes.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The most rules or searches a file may list. More than this is not a backup.</summary>
    public const int MaxItems = 200;
}
