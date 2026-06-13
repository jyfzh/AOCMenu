namespace aoc.Application;

public sealed class AppRunner
{
    private readonly CliParser _parser;
    private readonly SettingService _settingService;
    private readonly InfoService _infoService;
    private readonly IAocInvoker _invoker;
    private readonly IConsoleOutput _output;

    public AppRunner(CliParser parser, SettingService settingService, InfoService infoService, IAocInvoker invoker, IConsoleOutput output)
    {
        _parser = parser;
        _settingService = settingService;
        _infoService = infoService;
        _invoker = invoker;
        _output = output;
    }

    public int Run(string[] args)
    {
        var command = _parser.Parse(args);

        switch (command)
        {
            case HelpCommand:
                _output.PrintHelp(SettingCatalog.All.Values.ToArray());
                return 0;

            case InvalidCommand invalid:
                _output.PrintMessage(invalid.Message, error: true);
                return 2;

            case InfoCommand info:
                if (!EnsureInitialized()) return 1;
                var infoResults = info.Topic is null
                    ? _infoService.QueryAll()
                    : [_infoService.Query(info.Topic)];
                _output.PrintInfo(infoResults);
                return infoResults.Any(r => !r.Success) ? 1 : 0;

            case SetCommand set:
                if (!EnsureInitialized()) return 1;
                return PrintAndMap(_settingService.Set(set.Key, set.Value));

            case GetCommand get:
                if (!EnsureInitialized()) return 1;
                return PrintAndMap(_settingService.Get(get.Key));

            default:
                _output.PrintMessage("未知命令", error: true);
                return 2;
        }
    }

    private bool EnsureInitialized()
    {
        if (_invoker.TryInitialize(out var diagnostic)) return true;

        var msg = "ERROR: 显示器初始化失败";
        if (diagnostic is not null)
            msg += $"\n{diagnostic}";
        _output.PrintMessage(msg, error: true);
        return false;
    }

    private int PrintAndMap(OperationResult result)
    {
        _output.PrintMessage(result.UserMessage, error: !result.Success);

        if (!result.Success && result.DiagnosticMessage is not null)
            _output.PrintMessage($"🔍 诊断: {result.DiagnosticMessage}", error: true);

        if (result.Success) return 0;
        if (result.ErrorKind is ErrorKind.InvalidArgument or ErrorKind.PrerequisiteNotMet) return 2;
        return 1;
    }
}
