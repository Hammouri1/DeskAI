namespace DeskAI.IconProbe;

/// <summary>
/// The probe changes Explorer settings and icon positions, so it may run only on Windows
/// Sandbox's throwaway Desktop. The Sandbox always signs in as this exact account.
/// </summary>
internal static class SandboxGuard
{
    internal const string SandboxUser = "WDAGUtilityAccount";

    /// <returns>Null when the probe may run; otherwise why it refuses.</returns>
    internal static string? Check(string userName) =>
        string.Equals(userName, SandboxUser, StringComparison.Ordinal)
            ? null
            : "This probe runs only inside Windows Sandbox, because it moves Desktop icons. Nothing was changed.";
}
