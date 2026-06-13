Console.OutputEncoding = System.Text.Encoding.UTF8;

var host = new ProxyHost();
await host.StartAsync(TimeSpan.FromSeconds(5));

var invoker = new ProxyClientInvoker();
await invoker.ConnectAsync(TimeSpan.FromSeconds(5));

try
{
    var parser = new CliParser();
    var settingService = new SettingService(invoker);
    var infoService = new InfoService(invoker);
    var output = new ConsoleOutput();
    var runner = new AppRunner(parser, settingService, infoService, invoker, output);

    var exitCode = runner.Run(Environment.GetCommandLineArgs().Skip(1).ToArray());
    return exitCode;
}
finally
{
    await invoker.DisposeAsync();
    await host.DisposeAsync();
}
