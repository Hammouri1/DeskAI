using System.Text;

namespace DeskAI.IconProbe;

internal enum StageOutcome
{
    Passed,
    Failed,
    Skipped,
}

internal sealed record Stage(string Name, StageOutcome Outcome, string Detail);

/// <summary>The probe's findings as plain text, and the ADR 0043 go/no-go verdict.</summary>
internal sealed class ProbeReport
{
    /// <summary>ADR 0043: these must pass. "explorer restart" is recorded but decided by the owner.</summary>
    private static readonly string[] Required = ["place", "refresh", "put back"];

    private readonly List<Stage> _stages = [];
    private readonly List<string> _notes = [];

    internal void Add(Stage stage) => _stages.Add(stage);

    internal void Note(string line) => _notes.Add(line);

    internal bool IsReliable => Required.All(name => _stages.Any(s => s.Name == name && s.Outcome == StageOutcome.Passed));

    internal string Render()
    {
        var text = new StringBuilder();
        text.AppendLine(IsReliable ? "VERDICT: reliable" : "VERDICT: not reliable");
        foreach (var stage in _stages)
        {
            text.AppendLine(stage.Detail.Length == 0 ? $"{stage.Name}: {stage.Outcome}" : $"{stage.Name}: {stage.Outcome} - {stage.Detail}");
        }

        foreach (var note in _notes)
        {
            text.AppendLine(note);
        }

        return text.ToString();
    }
}
