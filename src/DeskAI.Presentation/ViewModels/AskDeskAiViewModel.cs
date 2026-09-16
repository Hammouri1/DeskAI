using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DeskAI.Core.Ai;
using DeskAI.Core.Roots;

namespace DeskAI.App.ViewModels;

/// <summary>One question and DeskAI's reply, with at most one thing to do next.</summary>
public sealed class AskExchangeViewModel(string question, AskDeskAiAnswer answer)
{
    public string Question { get; } = question;

    public string Reply { get; } = answer.Reply;

    public AskAction Action { get; } = answer.Action;

    public string? SearchPhrase { get; } = answer.SearchPhrase;

    public Guid? FolderId { get; } = answer.FolderId;

    public PersonalFolderKind? ConnectKind { get; } = answer.ConnectKind;

    public bool HasAction => Action != AskAction.None;

    /// <summary>The button's words: what pressing it opens, never what it changes.</summary>
    public string ActionText => Action switch
    {
        AskAction.OpenSearch => "Open in Search",
        AskAction.OpenOrganize => "Open in Organize",
        AskAction.ConnectFolder => $"Connect {ConnectKind}",
        _ => string.Empty,
    };
}

/// <summary>
/// The "Ask DeskAI" card on Home (V1.1, ADR 0035). A question in the person's words goes to the
/// AI they set up, after the dialog; DeskAI answers from what it remembers and offers one button
/// that opens Search or Organize, or connects a folder through the usual dialog.
/// </summary>
/// <remarks>
/// Each question stands alone; the list on the card is this visit's questions and replies and
/// is not saved. The buttons do exactly what the pages' own buttons do, through the same
/// requests, so nothing here grants anything.
/// </remarks>
public sealed class AskDeskAiViewModel(
    AskDeskAiService ask,
    SearchRequest searchRequest,
    OrganizeRequest organizeRequest) : ObservableObject
{
    private SentenceAiStatus? _status;
    private string _question = string.Empty;
    private string _message = string.Empty;
    private bool _isBusy;

    public ObservableCollection<AskExchangeViewModel> Exchanges { get; } = [];

    public bool HasExchanges => Exchanges.Count > 0;

    /// <summary>Whether AI is set up; the box and button are on only then.</summary>
    public bool HasAi => _status?.IsSetUp == true;

    public bool CanAsk => HasAi && !string.IsNullOrWhiteSpace(Question) && !IsBusy;

    public string AskText => HasAi ? $"Ask {_status!.ServiceName}" : "Ask";

    /// <summary>The line under the box: who would get the question, or how to turn AI on.</summary>
    public string Note => _status is null
        ? string.Empty
        : HasAi
            ? $"Only your question is sent to {_status.ServiceName}. DeskAI answers from what it remembers and never sends anything about your files."
            : "Turn on AI in Privacy and AI to ask questions here.";

    public string Question
    {
        get => _question;
        set
        {
            if (SetProperty(ref _question, value))
            {
                OnPropertyChanged(nameof(CanAsk));
            }
        }
    }

    /// <summary>A refusal or a problem, in plain words. Empty when there is none.</summary>
    public string Message
    {
        get => _message;
        private set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAsk));
            }
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            _status = await ask.GetStatusAsync().ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            _status = null;
        }

        OnPropertyChanged(nameof(HasAi));
        OnPropertyChanged(nameof(CanAsk));
        OnPropertyChanged(nameof(AskText));
        OnPropertyChanged(nameof(Note));
    }

    /// <summary>Says who would get the question and where. Sends nothing.</summary>
    public async Task<SentenceAiQuestion?> PrepareAsync()
    {
        var prepared = await ask.PrepareAsync(Question).ConfigureAwait(true);
        if (prepared.Question is null)
        {
            Message = prepared.Explanation;
        }

        return prepared.Question;
    }

    /// <summary>Sends the prepared question and adds DeskAI's reply to the card.</summary>
    public async Task SendAsync(SentenceAiQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);
        IsBusy = true;
        AskDeskAiAnswer answer;
        try
        {
            answer = await ask.AskAsync(question).ConfigureAwait(true);
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            Message = $"DeskAI stopped safely: {exception.Message}";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (!answer.Succeeded)
        {
            Message = answer.Reply;
            return;
        }

        Message = string.Empty;
        Exchanges.Insert(0, new AskExchangeViewModel(question.Sentence, answer));
        OnPropertyChanged(nameof(HasExchanges));
        Question = string.Empty;
    }

    /// <summary>
    /// Leaves the phrase or folder for the page the button opens, exactly as that page's own
    /// buttons would, and says which page to go to. Connecting is the page's job, dialog first.
    /// </summary>
    /// <returns>"search" or "organize", or null when there is nothing to open.</returns>
    public string? Act(AskExchangeViewModel exchange)
    {
        ArgumentNullException.ThrowIfNull(exchange);
        switch (exchange.Action)
        {
            case AskAction.OpenSearch when exchange.SearchPhrase is { } phrase:
                searchRequest.AskPhrase(phrase);
                return "search";
            case AskAction.OpenOrganize when exchange.FolderId is { } id:
                organizeRequest.Ask(id);
                return "organize";
            default:
                return null;
        }
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is InvalidOperationException
            or System.Data.Common.DbException
            or IOException
            or UnauthorizedAccessException;
}
