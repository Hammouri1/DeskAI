namespace DeskAI.Core.Classification;

public sealed class FileTypeRuleSet
{
    private readonly IReadOnlyList<FileTypeRule> _rules;

    public FileTypeRuleSet(IEnumerable<FileTypeRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var materialized = rules.ToArray();
        var duplicate = materialized
            .GroupBy(rule => rule.Extension, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException($"Duplicate file type rule: {duplicate.Key}", nameof(rules));
        }

        _rules = materialized
            .OrderByDescending(rule => rule.Extension.Length)
            .ToArray();
    }

    public FileTypeRule? Match(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var fileName = Path.GetFileName(relativePath);
        return _rules.FirstOrDefault(rule => fileName.EndsWith(rule.Extension, StringComparison.OrdinalIgnoreCase));
    }
}
