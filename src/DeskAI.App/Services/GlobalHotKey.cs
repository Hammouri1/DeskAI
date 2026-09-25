using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace DeskAI.App.Services;

/// <summary>
/// Ctrl + Alt + Space, anywhere in Windows, for quick search (ADR 0047).
/// </summary>
/// <remarks>
/// <para>
/// <c>RegisterHotKey</c> asks Windows to tell DeskAI about this one combination and nothing else.
/// It is not a keyboard hook: DeskAI never sees any other key anyone presses. With
/// <c>MOD_NOREPEAT</c>, holding the keys down reports once.
/// </para>
/// <para>
/// Windows posts the press to a window, so this class owns one hidden message-only window of its
/// own. Call <see cref="EnsureMessageWindow"/> and <see cref="Listen"/> on the UI thread: the press
/// arrives on the thread that made the window, which is then the thread <see cref="Pressed"/> is
/// raised on. A failure is contained and reported as a state, never thrown.
/// </para>
/// </remarks>
public sealed partial class GlobalHotKey(ILogger<GlobalHotKey> logger) : IQuickSearchHotKey, IDisposable
{
    private const string WindowClassName = "DeskAI.HotKeyWindow";
    private const int HotKeyId = 1;

    /// <summary>Every live window of this class, by handle, so the shared procedure finds its instance.</summary>
    private static readonly ConcurrentDictionary<nint, GlobalHotKey> Instances = new();
    private static readonly Lock ClassLock = new();
    private static bool _classRegistered;
    private static TrayInterop.WindowProcedure? _classProcedure;
    private static nint _classNamePointer;

    private readonly ILogger<GlobalHotKey> _logger = logger;
    private nint _window;
    private bool _registered;
    private bool _disposed;

    public HotKeyState State { get; private set; } = HotKeyState.Off;

    public event EventHandler? Pressed;

    /// <summary>Creates the hidden window that receives the press. Safe to call more than once.</summary>
    internal bool EnsureMessageWindow() => !_disposed && EnsureWindow();

    public HotKeyState Listen(bool isOn)
    {
        if (!isOn)
        {
            if (_registered)
            {
                HotKeyInterop.UnregisterHotKey(_window, HotKeyId);
                _registered = false;
            }

            return State = HotKeyState.Off;
        }

        if (_registered)
        {
            return State = HotKeyState.Listening;
        }

        if (!EnsureMessageWindow())
        {
            return State = HotKeyState.Unavailable;
        }

        if (HotKeyInterop.RegisterHotKey(_window, HotKeyId,
                HotKeyInterop.MOD_CONTROL | HotKeyInterop.MOD_ALT | HotKeyInterop.MOD_NOREPEAT, HotKeyInterop.VK_SPACE))
        {
            _registered = true;
            return State = HotKeyState.Listening;
        }

        var error = Marshal.GetLastWin32Error();
        LogShortcutRefused(_logger, error);
        return State = error == HotKeyInterop.ERROR_HOTKEY_ALREADY_REGISTERED
            ? HotKeyState.TakenByAnotherProgram
            : HotKeyState.Unavailable;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            HotKeyInterop.UnregisterHotKey(_window, HotKeyId);
            _registered = false;
        }

        if (_window != 0)
        {
            Instances.TryRemove(_window, out _);
            TrayInterop.DestroyWindow(_window);
            _window = 0;
        }

        State = HotKeyState.Off;
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

            // Message-only: it receives the posted WM_HOTKEY and nothing is ever drawn.
            _window = TrayInterop.CreateWindowExW(
                0, WindowClassName, WindowClassName, 0, 0, 0, 0, 0,
                HotKeyInterop.HWND_MESSAGE, 0, TrayInterop.GetModuleHandleW(null), 0);
            if (_window == 0)
            {
                var error = Marshal.GetLastWin32Error();
                LogShortcutRefused(_logger, error);
                return false;
            }

            Instances[_window] = this;
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or ExternalException)
        {
            LogShortcutFailed(_logger, exception);
            return false;
        }
    }

    /// <summary>Registers the class once per process and never unregisters it, as <see cref="TrayPresence"/> does.</summary>
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

            if (TrayInterop.RegisterClassExW(ref windowClass) == 0
                && Marshal.GetLastWin32Error() != TrayInterop.ERROR_CLASS_ALREADY_EXISTS)
            {
                Marshal.FreeHGlobal(_classNamePointer);
                _classNamePointer = 0;
                _classProcedure = null;
                return false;
            }

            _classRegistered = true;
            return true;
        }
    }

    /// <summary>An exception must never cross back into native code, so everything is caught here.</summary>
    private static nint WindowProcedure(nint window, uint message, nint wParam, nint lParam)
    {
        if (message == HotKeyInterop.WM_HOTKEY && wParam == HotKeyId && Instances.TryGetValue(window, out var instance))
        {
            try
            {
                instance.Pressed?.Invoke(instance, EventArgs.Empty);
            }
            catch (Exception exception)
            {
                LogShortcutFailed(instance._logger, exception);
            }

            return 0;
        }

        return TrayInterop.DefWindowProcW(window, message, wParam, lParam);
    }

    [LoggerMessage(
        EventId = 4320,
        Level = LogLevel.Information,
        Message = "Windows would not give DeskAI the quick search shortcut (Windows error {Error}). Everything else still works.")]
    private static partial void LogShortcutRefused(ILogger logger, int error);

    [LoggerMessage(
        EventId = 4321,
        Level = LogLevel.Information,
        Message = "The quick search shortcut hit a problem. DeskAI carries on without it.")]
    private static partial void LogShortcutFailed(ILogger logger, Exception exception);
}
