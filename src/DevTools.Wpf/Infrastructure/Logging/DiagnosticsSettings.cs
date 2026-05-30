namespace DevTools.Wpf.Infrastructure.Logging;

public enum DiagnosticsLogLevel
{
    Info,
    Debug
}

public static class DiagnosticsSettings
{
    private static readonly object sync = new();
    private static DiagnosticsLogLevel level = DiagnosticsLogLevel.Info;

    public static DiagnosticsLogLevel Level
    {
        get
        {
            lock (sync)
            {
                return level;
            }
        }
        set
        {
            lock (sync)
            {
                level = value;
            }
        }
    }

    public static bool IsDebugEnabled => Level == DiagnosticsLogLevel.Debug;
}
