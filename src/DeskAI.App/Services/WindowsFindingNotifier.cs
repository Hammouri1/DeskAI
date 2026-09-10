using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DeskAI.App.Services;

/// <summary>
/// Shows a Windows notification, when Windows will let it.
/// </summary>
/// <remarks>
/// <para>
/// DeskAI runs unpackaged, so notification support has to be registered at startup and can
/// legitimately fail — on a machine where notifications are turned off, or under policy.
/// A failure is recorded and <see cref="IsAvailable"/> stays false; it is never surfaced as
/// an error, because a missing notification is a missing convenience and the in-app notice
/// has already done the important part.
/// </para>
/// <para>
/// The text is built from a count. No file name, folder name, or path is ever put into a
/// notification: those appear on a lock screen and in a notification centre, which is not
/// somewhere a person chose to show anyone their filenames.
/// </para>
/// </remarks>
public sealed partial class WindowsFindingNotifier : IFindingNotifier, IDisposable
{
    private readonly ILogger<WindowsFindingNotifier> _logger;
    private bool _registered;
    private bool _disposed;

    public WindowsFindingNotifier(ILogger<WindowsFindingNotifier> logger)
    {
        _logger = logger;
        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception exception) when (exception is COMException or SecurityException or InvalidOperationException)
        {
            LogUnavailable(_logger, exception);
            _registered = false;
        }
    }

    public bool IsAvailable => _registered;

    public void Notify(string title, string message)
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            LogNotifyFailed(_logger, exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_registered)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            LogNotifyFailed(_logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 4300,
        Level = LogLevel.Information,
        Message = "Windows notifications are unavailable. DeskAI will show findings inside the app only.")]
    private static partial void LogUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4301,
        Level = LogLevel.Information,
        Message = "A notification could not be shown. The in-app notice still reports the finding.")]
    private static partial void LogNotifyFailed(ILogger logger, Exception exception);
}
