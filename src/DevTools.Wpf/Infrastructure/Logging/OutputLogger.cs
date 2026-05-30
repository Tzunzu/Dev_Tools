using System.Collections.ObjectModel;
using System.Windows;

namespace DevTools.Wpf.Infrastructure.Logging;

/// <summary>
/// Centralized logging service for output console.
/// </summary>
public sealed class OutputLogger
{
    private static readonly Lazy<OutputLogger> instance = new(() => new OutputLogger());

    public static OutputLogger Instance => instance.Value;

    private readonly ObservableCollection<string> logs = new();
    private readonly object lockObject = new();

    public ObservableCollection<string> Logs => logs;

    private OutputLogger()
    {
    }

    public void Log(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] {message}";

        lock (lockObject)
        {
            // WPF requires UI changes on the dispatcher thread
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    logs.Add(logEntry);
                    TrimLogs();
                });
            }
            else
            {
                logs.Add(logEntry);
                TrimLogs();
            }
        }
    }

    public void Clear()
    {
        lock (lockObject)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.Invoke(logs.Clear);
            }
            else
            {
                logs.Clear();
            }
        }
    }

    private void TrimLogs()
    {
        // Keep only the last 1000 log entries to avoid memory issues
        while (logs.Count > 1000)
        {
            logs.RemoveAt(0);
        }
    }
}
