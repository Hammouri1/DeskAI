namespace DeskAI.App.Services;

public enum HotKeyState { Off, Listening, TakenByAnotherProgram, Unavailable }

/// <summary>
/// Ctrl + Alt + Space, anywhere in Windows (ADR 0047).
/// </summary>
/// <remarks>
/// Windows tells DeskAI only about this one combination. It is not a keyboard hook and sees
/// nothing else anyone types. The Windows one is registered by the app; this project and the
/// tests never register a real shortcut.
/// </remarks>
public interface IQuickSearchHotKey
{
    HotKeyState State { get; }

    /// <summary>Starts or stops listening. Returns what happened, including Windows' refusal.</summary>
    HotKeyState Listen(bool isOn);

    /// <summary>The shortcut was pressed. Raised on the UI thread.</summary>
    event EventHandler? Pressed;
}

/// <summary>A DeskAI with no shortcut. Everything else still works.</summary>
public sealed class NoQuickSearchHotKey : IQuickSearchHotKey
{
    public HotKeyState State => HotKeyState.Unavailable;

    public HotKeyState Listen(bool isOn) => HotKeyState.Unavailable;

#pragma warning disable CS0067 // A shortcut that is never registered is never pressed.
    public event EventHandler? Pressed;
#pragma warning restore CS0067
}
