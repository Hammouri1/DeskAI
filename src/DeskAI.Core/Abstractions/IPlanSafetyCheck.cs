using DeskAI.Core.Plans;
using DeskAI.Core.Roots;

namespace DeskAI.Core.Abstractions;

/// <summary>
/// Asks the safety policy which operations in a plan it would refuse, and why.
/// </summary>
/// <remarks>
/// Core cannot reference Safety, so it asks through this contract, which Safety implements.
/// A key of <see cref="Guid.Empty"/> means the whole plan was refused.
/// </remarks>
public interface IPlanSafetyCheck
{
    string PolicyVersion { get; }

    IReadOnlyDictionary<Guid, string> FindBlocked(OrganizationPlan plan, AuthorizedRoot root);

    /// <summary>
    /// True when the policy refuses this path inside the folder — a protected location, a
    /// protected entry, or a path it does not accept at all.
    /// </summary>
    /// <remarks>
    /// Used before describing a file to AI: protected items are never disclosed, and this asks
    /// the policy directly rather than trusting that the scanner already skipped them.
    /// </remarks>
    bool IsProtected(AuthorizedRoot root, string relativePath);
}
