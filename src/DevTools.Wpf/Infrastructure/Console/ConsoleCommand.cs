namespace DevTools.Wpf.Infrastructure.Console;

public sealed record ConsoleCommand(
    string Name,
    string Description,
    Func<string[], string?> Execute,
    params string[] Aliases)
{
    public IEnumerable<string> Keys => new[] { Name }.Concat(Aliases ?? Array.Empty<string>());
}