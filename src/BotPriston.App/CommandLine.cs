namespace BotPriston.App;

public sealed class UsageException(string message) : Exception(message);

/// <summary>Tiny parser for "command --option value --flag" style arguments.</summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumed = new(StringComparer.OrdinalIgnoreCase);

    public CommandLine(string[] args)
    {
        int i = 0;
        if (args.Length > 0 && !args[0].StartsWith('-'))
        {
            Command = args[0].ToLowerInvariant();
            i = 1;
        }

        for (; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith('-'))
                throw new UsageException($"Unexpected argument '{arg}'.");

            var name = arg.TrimStart('-');
            string? value = null;
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                value = args[++i];
            _options[name] = value;
        }
    }

    public string Command { get; } = "help";

    public bool Flag(string name, string? alias = null)
    {
        if (TryTake(name, alias, out var value))
        {
            if (value is not null)
                throw new UsageException($"--{name} does not take a value.");
            return true;
        }
        return false;
    }

    public string? String(string name, string? alias = null)
    {
        if (!TryTake(name, alias, out var value)) return null;
        return value ?? throw new UsageException($"--{name} requires a value.");
    }

    public int Int(string name, int defaultValue, int min = int.MinValue)
    {
        var text = String(name);
        if (text is null) return defaultValue;
        if (!int.TryParse(text, out var value) || value < min)
            throw new UsageException($"--{name} must be an integer >= {min}.");
        return value;
    }

    public double Double(string name, double defaultValue, double min = double.MinValue)
    {
        var text = String(name);
        if (text is null) return defaultValue;
        if (!double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var value) || value < min)
            throw new UsageException($"--{name} must be a number >= {min}.");
        return value;
    }

    /// <summary>Call after reading all options to reject typos.</summary>
    public void EnsureAllConsumed()
    {
        var unknown = _options.Keys.Where(k => !_consumed.Contains(k)).ToList();
        if (unknown.Count > 0)
            throw new UsageException($"Unknown option(s) for '{Command}': " + string.Join(", ", unknown.Select(u => "--" + u)));
    }

    private bool TryTake(string name, string? alias, out string? value)
    {
        foreach (var key in alias is null ? [name] : new[] { name, alias })
        {
            if (_options.TryGetValue(key, out value))
            {
                _consumed.Add(key);
                return true;
            }
        }
        value = null;
        return false;
    }
}
