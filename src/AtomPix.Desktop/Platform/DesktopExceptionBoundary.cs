namespace AtomPix.Desktop.Platform;

using AtomPix.Infrastructure.Diagnostics;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

public sealed class DesktopExceptionBoundary : IDisposable
{
    private readonly ILogger<DesktopExceptionBoundary> _logger;
    private readonly IDesktopDialogService _dialogs;
    private int _attached;
    private int _dialogVisible;

    public DesktopExceptionBoundary(
        ILogger<DesktopExceptionBoundary> logger,
        IDesktopDialogService dialogs)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    public void Attach()
    {
        if (Interlocked.Exchange(ref _attached, 1) != 0) return;
        Dispatcher.UIThread.UnhandledException += HandleDispatcherException;
        TaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += HandleFatalException;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _attached, 0) == 0) return;
        Dispatcher.UIThread.UnhandledException -= HandleDispatcherException;
        TaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= HandleFatalException;
    }

    private void HandleDispatcherException(object? sender, DispatcherUnhandledExceptionEventArgs args)
    {
        args.Handled = true;
        Report(args.Exception, "DesktopUnhandledException", showDialog: true);
    }

    private void HandleUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        args.SetObserved();
        Report(args.Exception, "DesktopUnobservedTaskException", showDialog: true);
    }

    private void HandleFatalException(object? sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            Report(exception, "DesktopFatalException", showDialog: false);
    }

    private void Report(Exception exception, string eventName, bool showDialog)
    {
        var diagnosticId = LocalJsonLoggerProvider.CreateDiagnosticId();
        _logger.LogError(
            new EventId(9001, eventName),
            exception,
            "Unexpected Desktop exception. DiagnosticId={DiagnosticId}",
            diagnosticId);

        if (showDialog && Interlocked.Exchange(ref _dialogVisible, 1) == 0)
            Dispatcher.UIThread.Post(() => _ = ShowDialogAsync(diagnosticId));
    }

    private async Task ShowDialogAsync(string diagnosticId)
    {
        try
        {
            await _dialogs.ShowErrorAsync(
                "AtomPix 遇到未预期错误",
                $"当前操作未能完成。诊断编号：{diagnosticId}\n\n该编号只用于定位本机日志，不包含图片路径或日志原文。",
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to display the Desktop error dialog.");
        }
        finally
        {
            Interlocked.Exchange(ref _dialogVisible, 0);
        }
    }
}
