using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using FloatView.Services;

namespace FloatView;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppLog.StartSession();
        AppLog.Info("FloatView started");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Unhandled AppDomain exception", args.ExceptionObject as Exception);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppLog.Info("Process exit");
        Exit += (_, _) => AppLog.Info("Application exit");

        AttachBindingTraceListener();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled UI exception", e.Exception);
        MessageBox.Show(
            "FloatView hit an unexpected error.\n\n" + e.Exception.Message,
            "FloatView",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    /// <summary>
    /// Routes WPF binding warnings into the app log so silent binding
    /// problems become visible during testing.
    /// </summary>
    private static void AttachBindingTraceListener()
    {
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new BindingTraceListener());
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
    }

    private sealed class BindingTraceListener : TraceListener
    {
        public override void Write(string? message) { }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                AppLog.Warn("Binding: " + message);
        }
    }
}
