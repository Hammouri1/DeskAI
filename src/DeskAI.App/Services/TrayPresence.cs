using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DeskAI.App.Services;

/// <summary>
/// DeskAI's icon near the clock, drawn by the Windows shell.
/// </summary>
/// <remarks>
/// <para>
/// This class holds no decision. It is told what the tooltip says and whether checking is
/// paused, and it repeats both back to the shell; it never composes a tooltip, reads a setting,
/// or looks at a clock. Its menu starts nothing — open, pause, quit — so nothing here can begin
/// reading folders while there is no window on screen and no page to report the result. It
/// touches no file, no executor, no scanner, and no credential.
/// </para>
/// <para>
/// A failure is contained. The notification area can legitimately refuse an icon while the shell
/// is restarting or under policy; that is logged and leaves <see cref="IsShowing"/> false, the
/// same posture <see cref="WindowsFindingNotifier"/> takes. A missing icon is a missing
/// convenience, and taking DeskAI down with it would not be.
/// </para>
/// <para>
/// Call <see cref="Show"/>, <see cref="Update"/>, and <see cref="Hide"/> from the thread that
/// pumps messages for this window — the UI thread — because the shell calls back on that thread.
/// </para>
/// </remarks>
public sealed partial class TrayPresence : IBackgroundPresence, IDisposable
{
    /// <summary>
    /// <see cref="SingleInstance"/> finds this window by this exact string. Shared rather than
    /// repeated, so the lookup and the registration cannot drift apart.
    /// </summary>
    internal const string WindowClassName = "DeskAI.TrayWindow";

    /// <summary>The shell sends the icon's mouse events back as this private message.</summary>
    private const uint TrayCallbackMessage = 0x8000 + 1; // WM_APP + 1

    private const uint IconId = 1;
    private const uint CommandOpen = 1;
    private const uint CommandPause = 2;
    private const uint CommandQuit = 3;
    private const nint IdiApplication = 32512;

    /// <summary>
    /// Every live window of this class, by handle. The class carries one shared window procedure,
    /// so that procedure has to find the instance a message belongs to. Messages that arrive while
    /// the window is still being created find nothing here and fall through to Windows' default.
    /// </summary>
    private static readonly ConcurrentDictionary<nint, TrayPresence> Instances = new();

    private static readonly object ClassLock = new();
    private static bool _classRegistered;
    private static TrayInterop.WindowProcedure? _classProcedure;
    private static nint _classNamePointer;

    private readonly ILogger<TrayPresence> _logger;
    private readonly uint _taskbarCreatedMessage;
    private readonly uint _revealMessage;
    private nint _window;
    private nint _icon;
    private bool _ownsIcon;
    private string _tooltip = string.Empty;
    private bool _isPaused;
    private bool _isShowing;
    private bool _menuShowing;
    private bool _disposed;

    public TrayPresence(ILogger<TrayPresence> logger)
    {
        _logger = logger;

        // Explorer announces its own restart by broadcasting this message, so the id has to be
        // known before any window of ours can be woken by it.
        _taskbarCreatedMessage = TrayInterop.RegisterWindowMessageW("TaskbarCreated");

        // A second launch of DeskAI posts this to say "show the window you already have". Same
        // name in both processes, so both get the same id; see SingleInstance.
        _revealMessage = TrayInterop.RegisterWindowMessageW(SingleInstance.RevealMessageName);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? PauseToggleRequested;

    public event EventHandler? QuitRequested;

    public bool IsShowing => _isShowing;

    /// <summary>
    /// Creates the hidden window that receives messages, without showing any icon.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window has to exist before the icon does, because it is what a second launch of DeskAI
    /// posts its "show the window you already have" message to, and it is found by class name. Left
    /// to <see cref="Show"/>, the window would exist only while the icon did — so launching DeskAI
    /// again while it was already open with the keep-running mode off would find nothing, reveal
    /// nothing, and simply exit. To the person, double-clicking DeskAI would appear to do nothing at
    /// all, which is the one outcome ADR 0025 rules out.
    /// </para>
    /// <para>
    /// Call this from the UI thread, for the reason given on the class: the shell and every other
    /// sender call back on the thread that pumps messages for this window. It is deliberately not
    /// called from the constructor, so the thread affinity is a visible decision at the call site
    /// rather than whichever thread dependency injection happened to build this object on.
    /// </para>
    /// <para>
    /// Safe to call more than once and safe to call alongside <see cref="Show"/>: the window is
    /// created at most once, <see cref="Hide"/> only removes the icon and leaves the window alive,
    /// and only <see cref="Dispose"/> destroys it.
    /// </para>
    /// </remarks>
    internal bool EnsureMessageWindow() => !_disposed && EnsureWindow();

    public void Show(string tooltip, bool isPaused)
    {
        if (_disposed || _isShowing)
        {
            return;
        }

        _tooltip = tooltip;
        _isPaused = isPaused;

        if (!EnsureWindow())
        {
            return;
        }

        var data = BuildIconData();
        if (!TrayInterop.Shell_NotifyIconW(TrayInterop.NIM_ADD, ref data))
        {
            LogLastWindowsError();
            return;
        }

        _isShowing = true;
    }

    public void Update(string tooltip, bool isPaused)
    {
        if (_disposed || !_isShowing)
        {
            return;
        }

        // Stored before the call so a later re-add repeats the newest pair. The tooltip and the
        // pause tick must never be able to disagree.
        _tooltip = tooltip;
        _isPaused = isPaused;

        var data = BuildIconData();
        if (!TrayInterop.Shell_NotifyIconW(TrayInterop.NIM_MODIFY, ref data))
        {
            LogLastWindowsError();
        }
    }

    public void Hide()
    {
        if (!_isShowing)
        {
            return;
        }

        RemoveIcon();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RemoveIcon();
        if (_ownsIcon && _icon != 0)
        {
            TrayInterop.DestroyIcon(_icon);
            _icon = 0;
            _ownsIcon = false;
        }

        if (_window != 0)
        {
            Instances.TryRemove(_window, out _);
            TrayInterop.DestroyWindow(_window);
            _window = 0;
        }
    }

    /// <summary>Takes the icon away, whether or not the shell agrees, and forgets it is showing.</summary>
    private void RemoveIcon()
    {
        _isShowing = false;
        if (_window == 0)
        {
            return;
        }

        var data = EmptyIconData();
        TrayInterop.Shell_NotifyIconW(TrayInterop.NIM_DELETE, ref data);
    }

    /// <summary>The struct with only the fields the shell needs to identify the icon.</summary>
    private TrayInterop.NotifyIconDataW EmptyIconData() => new()
    {
        // Correct because every inline text field is declared ByValTStr, so the managed and the
        // native layout are the same size. The shell rejects a cbSize it does not recognise.
        Size = (uint)Marshal.SizeOf<TrayInterop.NotifyIconDataW>(),
        Window = _window,
        Id = IconId,

        // Never null: a null ByValTStr field has no native representation.
        Tip = string.Empty,
        Info = string.Empty,
        InfoTitle = string.Empty,
    };

    private TrayInterop.NotifyIconDataW BuildIconData()
    {
        var data = EmptyIconData();
        data.Flags = TrayInterop.NIF_MESSAGE | TrayInterop.NIF_ICON | TrayInterop.NIF_TIP;
        data.CallbackMessage = TrayCallbackMessage;
        data.Icon = _icon;

        // szTip is a fixed 128-character field. Callers keep the tooltip well short of that;
        // truncating here anyway is cheaper than trusting them, and what the marshaller would
        // otherwise do with an over-long string is not something to leave to chance.
        var room = TrayInterop.TipLength - 1;
        data.Tip = _tooltip.Length > room ? _tooltip[..room] : _tooltip;
        return data;
    }

    private bool EnsureWindow()
    {
        if (_window != 0)
        {
            return true;
        }

        try
        {
            if (!EnsureWindowClass())
            {
                return false;
            }

            // An ordinary top-level window that is never shown — deliberately NOT HWND_MESSAGE.
            // Windows announces an Explorer restart by BROADCASTING TaskbarCreated, and broadcasts
            // do not reach message-only windows. With a message-only window an Explorer crash would
            // leave DeskAI running, checking, and invisible, which is the worst outcome this
            // feature has. ShowWindow is never called, so the window stays off screen.
            _window = TrayInterop.CreateWindowExW(
                0,
                WindowClassName,
                WindowClassName,
                TrayInterop.WS_OVERLAPPED,
                0,
                0,
                0,
                0,
                0,
                0,
                TrayInterop.GetModuleHandleW(null),
                0);

            if (_window == 0)
            {
                LogLastWindowsError();
                return false;
            }

            Instances[_window] = this;
            (_icon, _ownsIcon) = LoadTrayIcon();
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            LogPresenceFailed(_logger, exception);
            return false;
        }
    }

    /// <summary>
    /// Registers the class once per process and never unregisters it: the window procedure is a
    /// managed delegate whose lifetime has to outlive any window still queued to receive a message.
    /// </summary>
    private static bool EnsureWindowClass()
    {
        lock (ClassLock)
        {
            if (_classRegistered)
            {
                return true;
            }

            _classProcedure = WindowProcedure;
            _classNamePointer = Marshal.StringToHGlobalUni(WindowClassName);

            var windowClass = new TrayInterop.WindowClassExW
            {
                Size = (uint)Marshal.SizeOf<TrayInterop.WindowClassExW>(),
                WindowProcedure = Marshal.GetFunctionPointerForDelegate(_classProcedure),
                Instance = TrayInterop.GetModuleHandleW(null),
                ClassName = _classNamePointer,
            };

            if (TrayInterop.RegisterClassExW(ref windowClass) == 0)
            {
                var error = Marshal.GetLastWin32Error();
                if (error != TrayInterop.ERROR_CLASS_ALREADY_EXISTS)
                {
                    // Windows never saw the name, so nothing is holding this string. Free it, or a
                    // later attempt allocates a second one and the first is lost.
                    Marshal.FreeHGlobal(_classNamePointer);
                    _classNamePointer = 0;
                    _classProcedure = null;
                    return false;
                }
            }

            _classRegistered = true;
            return true;
        }
    }

    /// <summary>
    /// DeskAI's logo at the small size Windows uses near the clock, read from the same icon file the
    /// window uses. The program's embedded icon is not used: it asked for icon number 1, which .NET
    /// does not use, and so showed Windows' generic program icon (owner-found 2026-09-24).
    /// </summary>
    /// <returns>The icon, and whether this class owns it and must destroy it. The generic fallback is shared.</returns>
    private static (nint Icon, bool Owned) LoadTrayIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "DeskAI.ico");
        var icon = File.Exists(path)
            ? TrayInterop.LoadImageFromFileW(
                0, path, TrayInterop.IMAGE_ICON,
                TrayInterop.GetSystemMetrics(TrayInterop.SM_CXSMICON), TrayInterop.GetSystemMetrics(TrayInterop.SM_CYSMICON),
                TrayInterop.LR_LOADFROMFILE)
            : 0;
        return icon != 0 ? (icon, true) : (TrayInterop.LoadIconW(0, IdiApplication), false);
    }

    /// <summary>
    /// The window procedure. An exception must never cross back into native code, so everything a
    /// handler might throw is caught here rather than in the callers of this class.
    /// </summary>
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (Instances.TryGetValue(window, out var instance))
        {
            try
            {
                if (instance.Handle(message, wParam, lParam))
                {
                    return 0;
                }
            }
            catch (Exception exception)
            {
                LogPresenceFailed(instance._logger, exception);
            }
        }

        return TrayInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    private bool Handle(uint message, nint wParam, nint lParam)
    {
        if (message == _taskbarCreatedMessage)
        {
            // Explorer restarted and forgot every icon. Put ours back, if it was there.
            if (_isShowing && !_disposed)
            {
                var data = BuildIconData();
                if (!TrayInterop.Shell_NotifyIconW(TrayInterop.NIM_ADD, ref data))
                {
                    _isShowing = false;
                    LogLastWindowsError();
                }
            }

            return true;
        }

        // The zero guard is not defensive tidiness: a failed RegisterWindowMessageW returns 0,
        // and message 0 is WM_NULL, which ShowMenu posts to this very window. Without it a
        // machine where registration failed would open the window on every right-click.
        if (_revealMessage != 0 && message == _revealMessage)
        {
            // Someone launched DeskAI again. The same thing a left-click means: give them back
            // the window they already have.
            OpenRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }

        switch (message)
        {
            case TrayInterop.WM_QUERYENDSESSION:
                // Only a question, and any application may still cancel the shutdown. The icon
                // stays: removing it here and then having the shutdown vetoed would leave DeskAI
                // running and checking with nothing near the clock to find it by, which is exactly
                // the outcome ADR 0025 calls the worst this feature can produce. Falling through
                // to DefWindowProcW answers TRUE, so DeskAI never blocks a sign-out.
                return false;

            case TrayInterop.WM_ENDSESSION:
                // Now it is an answer, not a question. wParam is zero when the session is not
                // ending after all, and only a real ending should take the icon away.
                if (wParam != 0)
                {
                    RemoveIcon();
                }

                return false;

            case TrayCallbackMessage:
                switch ((uint)lParam)
                {
                    case TrayInterop.WM_LBUTTONUP:
                        OpenRequested?.Invoke(this, EventArgs.Empty);
                        return true;
                    case TrayInterop.WM_RBUTTONUP:
                    case TrayInterop.WM_CONTEXTMENU:
                        ShowMenu();
                        return true;
                    default:
                        return true;
                }

            default:
                return false;
        }
    }

    /// <summary>Open, pause, quit. Nothing in here begins work; see ADR 0025.</summary>
    private void ShowMenu()
    {
        // TrackPopupMenuEx runs a nested message loop, so messages that arrive while the menu is up
        // — another right-click, or a TaskbarCreated broadcast — are dispatched inside this call.
        // Without this guard a second menu could nest inside the first, and worse, a Dispose reached
        // from a handler running in that nested loop would destroy the window that owns a menu still
        // being tracked. One menu at a time is the only safe shape.
        if (_menuShowing)
        {
            return;
        }

        var menu = TrayInterop.CreatePopupMenu();
        if (menu == 0)
        {
            var failure = new Win32Exception(Marshal.GetLastWin32Error());
            LogPresenceFailed(_logger, failure);
            return;
        }

        _menuShowing = true;
        try
        {
            TrayInterop.AppendMenuW(menu, TrayInterop.MF_STRING, CommandOpen, "Open DeskAI");
            TrayInterop.AppendMenuW(
                menu,
                TrayInterop.MF_STRING | (_isPaused ? TrayInterop.MF_CHECKED : 0),
                CommandPause,
                "Pause checking");
            TrayInterop.AppendMenuW(menu, TrayInterop.MF_STRING, CommandQuit, "Quit DeskAI");

            // Bold, and what a left-click does, so the obvious action is the same either way.
            TrayInterop.SetMenuDefaultItem(menu, CommandOpen, false);

            if (!TrayInterop.GetCursorPos(out var cursor))
            {
                return;
            }

            // Documented Win32 requirement: without the foreground change before and a posted
            // message after, the menu refuses to go away when the person clicks elsewhere.
            TrayInterop.SetForegroundWindow(_window);
            var command = TrayInterop.TrackPopupMenuEx(
                menu,
                TrayInterop.TPM_RIGHTBUTTON | TrayInterop.TPM_RETURNCMD,
                cursor.X,
                cursor.Y,
                _window,
                0);
            TrayInterop.PostMessageW(_window, TrayInterop.WM_NULL, 0, 0);

            switch (command)
            {
                case (int)CommandOpen:
                    OpenRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case (int)CommandPause:
                    PauseToggleRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case (int)CommandQuit:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    break;
                default:
                    break;
            }
        }
        finally
        {
            _menuShowing = false;
            TrayInterop.DestroyMenu(menu);
        }
    }

    /// <summary>
    /// Records why Windows refused, immediately after the call that failed. In one place so the
    /// error code is always read before anything else can overwrite it.
    /// </summary>
    private void LogLastWindowsError()
    {
        var error = Marshal.GetLastWin32Error();
        LogIconUnavailable(_logger, error);
    }

    [LoggerMessage(
        EventId = 4310,
        Level = LogLevel.Information,
        Message = "The notification area would not show DeskAI's icon (Windows error {Error}). DeskAI carries on without it.")]
    private static partial void LogIconUnavailable(ILogger logger, int error);

    [LoggerMessage(
        EventId = 4311,
        Level = LogLevel.Information,
        Message = "DeskAI's icon near the clock hit a problem. DeskAI carries on without it.")]
    private static partial void LogPresenceFailed(ILogger logger, Exception exception);
}
