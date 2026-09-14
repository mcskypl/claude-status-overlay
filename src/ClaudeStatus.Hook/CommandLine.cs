namespace ClaudeStatus.Hook;

/// <summary>Minimalny parser argumentów - hook ma trzy tryby i dwa parametry.</summary>
internal sealed class CommandLine
{
    public enum Command
    {
        Unknown,
        State,
        Install,
        Uninstall,
    }

    public Command Mode { get; private init; }
    public string? State { get; private init; }
    public string? Note { get; private init; }

    public static CommandLine Parse(string[] args)
    {
        string? state = null, note = null;
        var mode = Command.Unknown;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--install":
                case "-install":
                    mode = Command.Install;
                    break;
                case "--uninstall":
                case "-uninstall":
                    mode = Command.Uninstall;
                    break;
                case "--state":
                case "-state":
                    state = Next(args, ref i);
                    break;
                case "--note":
                case "-note":
                    note = Next(args, ref i);
                    break;
            }
        }

        if (mode == Command.Unknown && state is not null) mode = Command.State;
        return new CommandLine { Mode = mode, State = state, Note = note };
    }

    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;
}
