namespace aoc.Cli;

public abstract record CliCommand;

public sealed record HelpCommand : CliCommand;

public sealed record SetCommand(string Key, string Value) : CliCommand;

public sealed record GetCommand(string Key) : CliCommand;

public sealed record InfoCommand(string? Topic = null) : CliCommand;

public sealed record InvalidCommand(string Message) : CliCommand;
