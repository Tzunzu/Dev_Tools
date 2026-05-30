using System.Collections.ObjectModel;

namespace DevTools.Wpf.Infrastructure.Console;

public sealed class ConsoleCommandRegistry
{
    private readonly Dictionary<string, ConsoleCommand> commands = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ConsoleCommand> Commands => commands.Values
        .DistinctBy(command => command.Name)
        .OrderBy(command => command.Name, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Register(ConsoleCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        foreach (var key in command.Keys)
        {
            var normalized = NormalizeKey(key);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            commands[normalized] = command;
        }
    }

    public bool TryExecute(string input, out string? response)
    {
        response = null;

        var parts = Tokenize(input);
        if (parts.Count == 0)
        {
            return false;
        }

        var commandName = NormalizeKey(parts[0]);
        if (!commands.TryGetValue(commandName, out var command))
        {
            return false;
        }

        var args = parts.Skip(1).ToArray();
        response = command.Execute(args);
        return true;
    }

    private static string NormalizeKey(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('/') ? trimmed[1..] : trimmed;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(input))
        {
            return tokens;
        }

        var trimmed = input.Trim();
        var index = 0;
        while (index < trimmed.Length)
        {
            while (index < trimmed.Length && char.IsWhiteSpace(trimmed[index]))
            {
                index++;
            }

            if (index >= trimmed.Length)
            {
                break;
            }

            if (trimmed[index] == '"')
            {
                index++;
                var start = index;
                while (index < trimmed.Length && trimmed[index] != '"')
                {
                    index++;
                }

                tokens.Add(trimmed[start..index]);
                if (index < trimmed.Length && trimmed[index] == '"')
                {
                    index++;
                }

                continue;
            }

            var tokenStart = index;
            while (index < trimmed.Length && !char.IsWhiteSpace(trimmed[index]))
            {
                index++;
            }

            tokens.Add(trimmed[tokenStart..index]);
        }

        return tokens;
    }
}