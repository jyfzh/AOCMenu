namespace aoc.Cli;

public sealed class CliParser
{
    public CliCommand Parse(string[] args)
    {
        if (args.Length == 0)
            return new HelpCommand();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "/help" or "/h" or "-h")
                return new HelpCommand();

            if (arg is "--info" or "/info")
            {
                var topic = i + 1 < args.Length ? args[i + 1].ToLowerInvariant() : null;
                // Don't treat arbitrary unknown strings as topics — validate against known topics
                if (topic is not null && !InfoService.Topics.ContainsKey(topic))
                    return new InvalidCommand(
                        $"未知信息查询 '{topic}'。可用子命令: {string.Join(", ", InfoService.Topics.Keys.Order())}");
                return new InfoCommand(topic);
            }

            if (arg is "--set" or "/set")
            {
                if (i + 2 >= args.Length)
                    return new InvalidCommand("用法: aoc --set <设置名> <值>  或  --get <设置名>");

                var key = args[i + 1].ToLowerInvariant();
                var value = args[i + 2];
                return new SetCommand(key, value);
            }

            if (arg is "--get" or "/get")
            {
                if (i + 1 >= args.Length)
                    return new InvalidCommand("用法: aoc --set <设置名> <值>  或  --get <设置名>");

                var key = args[i + 1].ToLowerInvariant();
                return new GetCommand(key);
            }
        }

        return new InvalidCommand("用法: aoc --set <设置名> <值>  或  --get <设置名>");
    }
}
