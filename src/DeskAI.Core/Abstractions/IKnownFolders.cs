namespace DeskAI.Core.Abstractions;

/// <summary>
/// Where Windows keeps the person's own folders, asked through the official known-folder API,
/// never built from a user name. Each is null when Windows has no such folder for the account.
/// </summary>
/// <remarks>
/// These four are the only places DeskAI may connect (owner's decision, 2026-09-16, ADR 0032):
/// a person's own Desktop, Downloads, Documents, and Pictures, or a folder inside one of them.
/// Everything else on the computer stays out of reach, whatever a dialog or a model says.
/// </remarks>
public interface IKnownFolders
{
    string? Desktop { get; }

    string? Downloads { get; }

    string? Documents { get; }

    string? Pictures { get; }
}
