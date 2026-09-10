namespace DeskAI.Core.Rules;

/// <summary>Whether DeskAI keeps checking after its window is closed.</summary>
/// <remarks>
/// This is a person's choice, and the default is the narrow one. See ADR 0017.
/// </remarks>
public enum AutomaticCheckMode
{
    /// <summary>
    /// Checks happen only while DeskAI is running. Closing the window stops them, and
    /// nothing is registered with Windows to start it again.
    /// </summary>
    WhileAppIsOpen = 0,

    /// <summary>
    /// DeskAI keeps checking after the window is closed. Decided in ADR 0017, deliberately
    /// not yet buildable: no code produces this value, and the refusal is tested.
    /// </summary>
    InBackground = 1,
}

/// <summary>How often DeskAI looks, in the words a person chose it by.</summary>
/// <remarks>
/// Frequencies are stored as the choice rather than as a number of minutes, so the setting
/// someone picked is the setting that comes back. A stored interval would let a later change
/// to what "every hour" means silently rewrite what they agreed to.
/// </remarks>
public enum AutomaticCheckFrequency
{
    /// <summary>DeskAI never checks on its own. Nothing overrides this.</summary>
    OnlyWhenIAsk = 0,

    EveryFifteenMinutes = 1,

    EveryHour = 2,

    ACoupleOfTimesADay = 3,
}

/// <summary>
/// How DeskAI keeps an eye on connected folders for rule matches.
/// </summary>
/// <remarks>
/// <para>
/// None of this grants a capability. A check reads remembered metadata, evaluates rules in
/// memory, and produces a count; it opens no file and moves none. What these settings decide
/// is only how often that description is refreshed and how loudly the result is mentioned.
/// </para>
/// <para>
/// The defaults are the quiet ones: check while the app is open, every fifteen minutes, no
/// notifications. A notification arrives without being asked for, so it is something a
/// person switches on rather than something they must discover and switch off.
/// </para>
/// </remarks>
public sealed record AutomaticCheckSettings(
    AutomaticCheckMode Mode,
    AutomaticCheckFrequency Frequency,
    bool IsPaused,
    bool NotifyWhenSomethingIsFound)
{
    public static AutomaticCheckSettings Default { get; } = new(
        AutomaticCheckMode.WhileAppIsOpen,
        AutomaticCheckFrequency.EveryFifteenMinutes,
        IsPaused: false,
        NotifyWhenSomethingIsFound: false);
}
