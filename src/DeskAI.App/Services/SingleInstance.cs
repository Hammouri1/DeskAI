using System.Runtime.InteropServices;

namespace DeskAI.App.Services;

/// <summary>
/// Answers whether another DeskAI is already running, and can bring that one back.
/// </summary>
/// <remarks>
/// <para>
/// Once DeskAI can keep running with no window, launching it again is the obvious thing a
/// person does when they want it back. This class is only the mechanism; what to do about the
/// answer is decided in <see cref="DeskAI.Core.Rules.SingleInstanceDecision"/>, where it is a
/// unit test rather than something only two real processes could demonstrate. See ADR 0025.
/// </para>
/// <para>
/// It holds no state about files and touches none. The one thing it can make happen in another
/// process is a window appearing.
/// </para>
/// </remarks>
public sealed class SingleInstance : IDisposable
{
    /// <summary>
    /// The lock's name. <c>Local\</c>, deliberately not <c>Global\</c>: the name is then per
    /// sign-in session, so a second Windows user signed in to the same machine gets their own
    /// DeskAI rather than being told one is already running — it would be somebody else's, over
    /// somebody else's folders and database, and revealing it would put their window on this
    /// person's screen.
    /// </summary>
    private const string MutexName = @"Local\DeskAI.SingleInstance";

    /// <summary>
    /// The name of the message a second launch posts to the first one's window.
    /// </summary>
    /// <remarks>
    /// Registered by name, so both processes get the same id without either knowing a number.
    /// <see cref="TrayPresence"/> registers the same name and treats it as "open the window".
    /// </remarks>
    internal const string RevealMessageName = "DeskAI.ShowExistingWindow";

    private readonly Mutex _mutex;
    private bool _disposed;

    private SingleInstance(Mutex mutex, bool anotherIsAlreadyRunning)
    {
        _mutex = mutex;
        AnotherIsAlreadyRunning = anotherIsAlreadyRunning;
    }

    /// <summary>Whether a DeskAI was already running in this sign-in session.</summary>
    public bool AnotherIsAlreadyRunning { get; }

    /// <summary>
    /// Takes the instance lock and reports whether someone else already had it.
    /// </summary>
    /// <remarks>
    /// A named kernel object rather than a scan of running processes: a process list can name a
    /// DeskAI that is already on its way out, and two launches racing each other would both
    /// decide they were first. Holding the handle for the life of the process is what keeps the
    /// name in existence — Windows destroys the object when the last handle closes, so a DeskAI
    /// that crashes leaves nothing behind for the next launch to mistake for a running one.
    /// </remarks>
    public static SingleInstance Acquire()
    {
        var mutex = new Mutex(initiallyOwned: false, MutexName, out var createdNew);
        try
        {
            // Ownership is not what answers the question — createdNew already did — and the wait
            // is zero-length so a launch never blocks on one. Taking ownership is only where an
            // abandoned lock surfaces.
            mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            // A previous DeskAI died holding the lock. That is not a running DeskAI, so this
            // launch carries on as normal; the wait succeeded despite the exception, and this
            // process now owns it.
        }

        return new SingleInstance(mutex, !createdNew);
    }

    /// <summary>
    /// Asks the DeskAI that is already running to show its window, then does nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Posted, never sent: <c>SendMessage</c> blocks until the other process answers, so a first
    /// instance busy with a check would freeze this launch instead of ending it.
    /// </para>
    /// <para>
    /// The window is found by the class name <see cref="TrayPresence"/> registers, which exists
    /// only while the icon near the clock does. That is the case this feature is for — a DeskAI
    /// with no window on screen. When there is no such window there is nothing to reveal, and
    /// this quietly does nothing rather than guessing at some other process's window.
    /// </para>
    /// </remarks>
    public void RevealTheRunningOne()
    {
        if (!AnotherIsAlreadyRunning)
        {
            // There is nothing to reveal: this process holds the lock, so this process is the
            // DeskAI. Guarded here rather than trusted to the caller, because the alternative is
            // posting "show yourself" at a window belonging to something else.
            return;
        }

        try
        {
            var window = FindWindowW(TrayPresence.WindowClassName, null);
            if (window == 0)
            {
                return;
            }

            var message = TrayInterop.RegisterWindowMessageW(RevealMessageName);
            if (message == 0)
            {
                return;
            }

            // No wParam and no lParam, on purpose. Any program on this desktop can post a
            // registered message to a window it can find, so this channel carries no instruction
            // and no data: the most anything else can achieve through it is making a window
            // appear. A payload would be an argument from an untrusted sender.
            TrayInterop.PostMessageW(window, message, 0, 0);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            // Nothing to report to: this launch has no window and is about to end. Exiting
            // quietly is still better than starting a second DeskAI over the same database.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Closing the handle is what matters. This process is the only holder when it created
        // the object, so the name disappears with it and the next launch starts normally.
        _mutex.Dispose();
    }

    /// <summary>
    /// Declared here rather than in <see cref="TrayInterop"/>, which is deliberately only the
    /// calls behind the icon near the clock.
    /// </summary>
    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint FindWindowW(string? className, string? windowName);
}
