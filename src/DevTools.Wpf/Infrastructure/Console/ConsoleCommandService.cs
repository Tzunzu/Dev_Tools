namespace DevTools.Wpf.Infrastructure.Console;

public sealed class ConsoleCommandService
{
    private readonly ConsoleCommandRegistry registry = new();

    public ConsoleCommandService(
        Func<string> getActiveViewName,
        Func<bool> isConsoleExpanded,
        Func<int> getLogCount,
        IEnumerable<string> availableViews,
        Action clearOutput,
        Func<string> getDiagnosticsLevel,
        Func<string[], string> setDiagnosticsLevel,
        Func<string[], string> dumpRegisters,
        Func<string[], string> syncViews)
    {
        ArgumentNullException.ThrowIfNull(getActiveViewName);
        ArgumentNullException.ThrowIfNull(isConsoleExpanded);
        ArgumentNullException.ThrowIfNull(getLogCount);
        ArgumentNullException.ThrowIfNull(availableViews);
        ArgumentNullException.ThrowIfNull(clearOutput);
        ArgumentNullException.ThrowIfNull(getDiagnosticsLevel);
        ArgumentNullException.ThrowIfNull(setDiagnosticsLevel);
        ArgumentNullException.ThrowIfNull(dumpRegisters);
        ArgumentNullException.ThrowIfNull(syncViews);

        var viewNames = availableViews
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        registry.Register(new ConsoleCommand(
            "help",
            "Show available console commands.",
            _ => string.Join(Environment.NewLine, registry.Commands.Select(command => $"/{command.Name} - {command.Description}")),
            "?"));

        registry.Register(new ConsoleCommand(
            "clear",
            "Clear the output console.",
            _ =>
            {
                clearOutput();
                return "Console cleared.";
            },
            "cls"));

        registry.Register(new ConsoleCommand(
            "time",
            "Show the current local time.",
            _ => $"Local time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));

        registry.Register(new ConsoleCommand(
            "status",
            "Show the active view and console state.",
            _ =>
            {
                var activeView = getActiveViewName();
                var logCount = getLogCount();
                var consoleState = isConsoleExpanded() ? "open" : "collapsed";
                return $"Active view: {activeView}{Environment.NewLine}Console: {consoleState}{Environment.NewLine}Log entries: {logCount}";
            }));

        registry.Register(new ConsoleCommand(
            "views",
            "List the available tool views.",
            _ => string.Join(Environment.NewLine, viewNames.Select(name => $"- {name}"))));

        registry.Register(new ConsoleCommand(
            "about",
            "Show app information.",
            _ => $"DevTools WPF{Environment.NewLine}Runtime: {Environment.Version}{Environment.NewLine}OS: {Environment.OSVersion}"));

        registry.Register(new ConsoleCommand(
            "loglevel",
            "Get or set diagnostics level. Usage: /loglevel [info|debug]",
            args => args.Length == 0
                ? $"Diagnostics level: {getDiagnosticsLevel()}"
                : setDiagnosticsLevel(args),
            "diag"));

        registry.Register(new ConsoleCommand(
            "packets",
            "Enable/disable packet trace logging. Usage: /packets [on|off]",
            args => args.Length == 0
                ? $"Packet trace: {(string.Equals(getDiagnosticsLevel(), "debug", StringComparison.OrdinalIgnoreCase) ? "on" : "off")}"
                : setDiagnosticsLevel(new[]
                {
                    string.Equals(args[0], "on", StringComparison.OrdinalIgnoreCase)
                        ? "debug"
                        : string.Equals(args[0], "off", StringComparison.OrdinalIgnoreCase)
                            ? "info"
                            : args[0]
                })));

        registry.Register(new ConsoleCommand(
            "regs",
            "Dump mapped values. Usage: /regs [tcp|rtu] <hr|ir|co|di|all> <start> <count>",
            dumpRegisters,
            "registers"));

        registry.Register(new ConsoleCommand(
            "sync",
            "Force server view refresh from datastore. Usage: /sync [tcp|rtu|all]",
            syncViews,
            "refresh"));
    }

    public bool TryExecute(string input, out string? response)
    {
        return registry.TryExecute(input, out response);
    }
}