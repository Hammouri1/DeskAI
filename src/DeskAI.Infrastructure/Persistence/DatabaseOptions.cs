namespace DeskAI.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string DatabasePath { get; set; } = string.Empty;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DatabasePath);
        if (!Path.IsPathFullyQualified(DatabasePath))
        {
            throw new InvalidOperationException("The database path must be absolute.");
        }
    }
}
