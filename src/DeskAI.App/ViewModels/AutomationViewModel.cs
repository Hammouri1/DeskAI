using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeskAI.Core.Abstractions;
using DeskAI.Core.Rules;

namespace DeskAI.App.ViewModels;

/// <summary>One stored rule, written out the way a person can check it.</summary>
public sealed record RuleViewModel(Guid Id, string Name, string Sentence, bool IsEnabled)
{
    public string State => IsEnabled ? "On" : "Off";

    public string ToggleAction => IsEnabled ? "Turn off" : "Turn on";
}

/// <summary>One file a practice run would move, and where it would go.</summary>
public sealed record RuleProposalViewModel(string Folder, string File, string Destination, string Reason);

/// <summary>Files the rules disagreed about, which are therefore left alone.</summary>
public sealed record RuleConflictViewModel(string Folder, string File, string Explanation);

/// <summary>
/// Drives the Automatic tasks page: write rules, and see what they would do.
/// </summary>
/// <remarks>
/// <para>
/// Everything here stops at a practice run. There is deliberately no button that carries a
/// rule out: rules produce proposals, and a proposal still has to become a plan that is
/// checked, previewed, and approved. Adding a "run it" button here would be a way around
/// the preview, not a feature.
/// </para>
/// <para>
/// Nothing runs on a schedule and nothing runs in the background. The page says so, because
/// a rules screen is exactly where someone would reasonably assume otherwise.
/// </para>
/// </remarks>
public sealed class AutomationViewModel : ObservableObject
{
    private readonly IRuleRepository _rules;
    private readonly RuleSimulationService _simulation;
    private readonly IClock _clock;
    private string _newRuleName = string.Empty;
    private string _newRuleNameContains = string.Empty;
    private string _newRuleExtension = string.Empty;
    private string _newRuleDestination = string.Empty;
    private string _message = "No rules yet. Write one below and try a practice run.";
    private string _practiceHeadline = string.Empty;
    private string _practiceDetail = string.Empty;
    private bool _hasPractised;
    private bool _isBusy;

    public AutomationViewModel(IRuleRepository rules, RuleSimulationService simulation, IClock clock)
    {
        _rules = rules;
        _simulation = simulation;
        _clock = clock;
        AddRuleCommand = new AsyncRelayCommand(AddRuleAsync, () => !IsBusy);
        PractiseCommand = new AsyncRelayCommand(PractiseAsync, () => !IsBusy);
        ToggleRuleCommand = new AsyncRelayCommand<Guid>(ToggleRuleAsync, _ => !IsBusy);
        DeleteRuleCommand = new AsyncRelayCommand<Guid>(DeleteRuleAsync, _ => !IsBusy);
    }

    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    public ObservableCollection<RuleProposalViewModel> Proposals { get; } = [];

    public ObservableCollection<RuleConflictViewModel> Conflicts { get; } = [];

    public AsyncRelayCommand AddRuleCommand { get; }

    public AsyncRelayCommand PractiseCommand { get; }

    public AsyncRelayCommand<Guid> ToggleRuleCommand { get; }

    public AsyncRelayCommand<Guid> DeleteRuleCommand { get; }

    public string NewRuleName
    {
        get => _newRuleName;
        set => SetProperty(ref _newRuleName, value);
    }

    public string NewRuleNameContains
    {
        get => _newRuleNameContains;
        set => SetProperty(ref _newRuleNameContains, value);
    }

    public string NewRuleExtension
    {
        get => _newRuleExtension;
        set => SetProperty(ref _newRuleExtension, value);
    }

    public string NewRuleDestination
    {
        get => _newRuleDestination;
        set => SetProperty(ref _newRuleDestination, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public string PracticeHeadline
    {
        get => _practiceHeadline;
        private set => SetProperty(ref _practiceHeadline, value);
    }

    public string PracticeDetail
    {
        get => _practiceDetail;
        private set => SetProperty(ref _practiceDetail, value);
    }

    public bool HasPractised
    {
        get => _hasPractised;
        private set => SetProperty(ref _hasPractised, value);
    }

    public bool HasRules => Rules.Count > 0;

    public bool HasProposals => Proposals.Count > 0;

    public bool HasConflicts => Conflicts.Count > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                AddRuleCommand.NotifyCanExecuteChanged();
                PractiseCommand.NotifyCanExecuteChanged();
                ToggleRuleCommand.NotifyCanExecuteChanged();
                DeleteRuleCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsIdle));
            }
        }
    }

    public bool IsIdle => !IsBusy;

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI could not read your rules: {exception.Message}";
        }
    }

    private async Task AddRuleAsync()
    {
        IsBusy = true;
        try
        {
            var conditions = new List<RuleCondition>();
            if (!string.IsNullOrWhiteSpace(NewRuleNameContains))
            {
                conditions.Add(new NameContainsCondition(NewRuleNameContains.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(NewRuleExtension))
            {
                conditions.Add(new ExtensionIsCondition(NewRuleExtension.Trim()));
            }

            // The domain refuses a rule with no conditions because it would match every
            // file. Saying so here is friendlier than letting the exception be the message.
            if (conditions.Count == 0)
            {
                Message = "Add at least one thing to look for, or this rule would match every file.";
                return;
            }

            var rule = AutomationRule.Create(
                Guid.NewGuid(),
                NewRuleName.Trim(),
                conditions,
                new MoveToFolderAction(NewRuleDestination.Trim()));

            await _rules.SaveAsync(rule).ConfigureAwait(true);
            NewRuleName = string.Empty;
            NewRuleNameContains = string.Empty;
            NewRuleExtension = string.Empty;
            NewRuleDestination = string.Empty;
            await ReloadAsync().ConfigureAwait(true);
            Message = $"Saved. {rule.Describe()} Nothing has moved — try a practice run.";
        }
        catch (ArgumentException exception)
        {
            // Every refusal in the rule domain is written for a person to read, so the
            // message can be shown as-is rather than replaced with something vaguer.
            Message = exception.Message;
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ToggleRuleAsync(Guid ruleId)
    {
        IsBusy = true;
        try
        {
            var rule = await _rules.FindAsync(ruleId).ConfigureAwait(true);
            if (rule is null)
            {
                Message = "That rule is no longer there.";
                return;
            }

            await _rules.SaveAsync(rule.WithEnabled(!rule.IsEnabled)).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            Message = rule.IsEnabled
                ? $"\"{rule.Name}\" is off. It will not be included in a practice run."
                : $"\"{rule.Name}\" is on.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DeleteRuleAsync(Guid ruleId)
    {
        IsBusy = true;
        try
        {
            await _rules.RemoveAsync(ruleId).ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
            Message = "Rule deleted. No files were touched.";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Shows what the rules would do. It does none of it.
    /// </summary>
    private async Task PractiseAsync()
    {
        IsBusy = true;
        try
        {
            var simulation = await _simulation.SimulateAsync(_clock.UtcNow).ConfigureAwait(true);
            Apply(simulation);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            HasPractised = false;
            Message = $"DeskAI stopped safely: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(RuleSimulation simulation)
    {
        Proposals.Clear();
        Conflicts.Clear();

        foreach (var folder in simulation.Folders)
        {
            foreach (var proposal in folder.Preview.Proposals)
            {
                Proposals.Add(new RuleProposalViewModel(
                    folder.RootName,
                    proposal.RelativePath,
                    proposal.DestinationRelativeDirectory,
                    proposal.Reason));
            }

            foreach (var conflict in folder.Preview.Conflicts)
            {
                Conflicts.Add(new RuleConflictViewModel(
                    folder.RootName,
                    conflict.RelativePath,
                    conflict.Explanation));
            }
        }

        HasPractised = true;
        PracticeHeadline = simulation.ProposalCount switch
        {
            0 when simulation.RulesConsidered == 0 => "No rules are on",
            0 when simulation.Folders.Count == 0 => "No folders are connected",
            0 => "Nothing would move",
            1 => "1 file would move",
            _ => $"{simulation.ProposalCount} files would move",
        };

        PracticeDetail = simulation.RulesConsidered == 0
            ? "Turn a rule on, then try again."
            : simulation.Folders.Count == 0
                ? "Connect a folder in Search first, so there is something for rules to look at."
                : "This is a practice run. Nothing has moved, and DeskAI cannot move anything "
                    + "from this page — any real change still goes through the preview where you approve it.";

        OnPropertyChanged(nameof(HasProposals));
        OnPropertyChanged(nameof(HasConflicts));
    }

    private async Task ReloadAsync()
    {
        var stored = await _rules.ListAsync().ConfigureAwait(true);
        Rules.Clear();
        foreach (var rule in stored)
        {
            Rules.Add(new RuleViewModel(rule.Id, rule.Name, rule.Describe(), rule.IsEnabled));
        }

        OnPropertyChanged(nameof(HasRules));
        if (Rules.Count == 0)
        {
            Message = "No rules yet. Write one below and try a practice run.";
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException
            or InvalidOperationException
            or FormatException
            or System.Data.Common.DbException;
}
