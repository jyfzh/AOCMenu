using System.Text.Json;
using aoc.Application;
using aoc.Cli;
using aoc.Domain;
using aoc.Infrastructure;
using aoc.Presentation;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Application;

public class AppRunnerTests
{
    // ── Help command ──

    [Fact]
    public void Run_NoArgs_DoesNotInitializeSdk_AndReturns0()
    {
        var invoker = new FakeInvoker();
        var runner = CreateRunner(invoker);

        var code = runner.Run(Array.Empty<string>());

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(0);
    }

    [Fact]
    public void Run_NoArgs_PrintsHelpToStdOut()
    {
        var output = new FakeOutput();
        var runner = CreateRunner(output: output);

        runner.Run(Array.Empty<string>());

        output.StdOut.Should().Contain("AOC OSD 控制器");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("/help")]
    [InlineData("-h")]
    [InlineData("/h")]
    public void Run_HelpFlags_DoNotInitializeSdk_AndReturn0(string flag)
    {
        var invoker = new FakeInvoker();
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { flag });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(0);
    }

    // ── Info command ──

    [Fact]
    public void Run_Info_InitializesSdk_AndReturns0()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("{}").RootElement };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--info" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    [Fact]
    public void Run_Info_WithTopic_Returns0()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"U27G3X\"").RootElement };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--info", "message" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    [Fact]
    public void Run_Info_WhenSomeQueriesFail_Returns1()
    {
        var invoker = new FakeInvoker { NextCallResult = null };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--info" });

        // Some topics may fail — exit code 1
        code.Should().Be(1);
    }

    [Fact]
    public void Run_Info_SdkInitFails_Returns1()
    {
        var invoker = new FakeInvoker { NextInitResult = false, InitDiagnostic = "no monitor" };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--info" });

        code.Should().Be(1);
    }

    // ── Invalid args ──

    [Fact]
    public void Run_InvalidArgs_Returns2()
    {
        var runner = CreateRunner();

        var code = runner.Run(new[] { "--set", "hdr" });

        code.Should().Be(2);
    }

    [Fact]
    public void Run_InvalidArgs_PrintsToStdErr()
    {
        var output = new FakeOutput();
        var runner = CreateRunner(output: output);

        runner.Run(new[] { "--set", "hdr" });

        output.StdErr.Should().ContainSingle();
    }

    [Fact]
    public void Run_UnknownArgs_Returns2()
    {
        var runner = CreateRunner();

        var code = runner.Run(new[] { "foobar" });

        code.Should().Be(2);
    }

    // ── Set command ──

    [Fact]
    public void Run_Set_InitializesSdk_AndReturns0()
    {
        var invoker = new FakeInvoker();
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--set", "hdr", "1" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    [Fact]
    public void Run_Set_PrintsSuccessMessage()
    {
        var output = new FakeOutput();
        var runner = CreateRunner(output: output);

        runner.Run(new[] { "--set", "hdr", "1" });

        output.StdOut.Should().ContainSingle(msg => msg.Contains("✅"));
    }

    [Fact]
    public void Run_Set_UnknownKey_Returns2()
    {
        var runner = CreateRunner();

        var code = runner.Run(new[] { "--set", "unknown-setting", "1" });

        code.Should().Be(2);
    }

    [Fact]
    public void Run_Set_UnknownKey_PrintsError()
    {
        var output = new FakeOutput();
        var runner = CreateRunner(output: output);

        runner.Run(new[] { "--set", "unknown-setting", "1" });

        output.StdErr.Should().ContainSingle(msg => msg.Contains("未知设置"));
    }

    [Fact]
    public void Run_Set_InvalidEnumValue_Returns2()
    {
        var runner = CreateRunner();

        var code = runner.Run(new[] { "--set", "overdrive", "invalid" });

        code.Should().Be(2);
    }

    [Fact]
    public void Run_Set_WhenInvocationFails_Returns1()
    {
        var invoker = new FakeInvoker { NextOkResult = false };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--set", "hdr", "1" });

        code.Should().Be(1);
    }

    // ── Get command ──

    [Fact]
    public void Run_Get_InitializesSdk_AndReturns0()
    {
        var invoker = new FakeInvoker { NextCallResult = System.Text.Json.JsonDocument.Parse("""{"Value":"1"}""").RootElement };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--get", "hdr" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    [Fact]
    public void Run_Get_UnknownKey_Returns2()
    {
        var runner = CreateRunner();

        var code = runner.Run(new[] { "--get", "unknown-setting" });

        code.Should().Be(2);
    }

    [Fact]
    public void Run_Get_UnknownKey_PrintsError()
    {
        var output = new FakeOutput();
        var runner = CreateRunner(output: output);

        runner.Run(new[] { "--get", "unknown-setting" });

        output.StdErr.Should().ContainSingle(msg => msg.Contains("未知设置"));
    }

    // ── SDK initialization failure ──

    [Fact]
    public void Run_WhenSdkInitFails_Returns1()
    {
        var invoker = new FakeInvoker { NextInitResult = false, InitDiagnostic = "no monitor found" };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "--set", "hdr", "1" });

        code.Should().Be(1);
    }

    [Fact]
    public void Run_WhenSdkInitFails_PrintsDiagnostic()
    {
        var output = new FakeOutput();
        var invoker = new FakeInvoker { NextInitResult = false, InitDiagnostic = "connection failed" };
        var runner = CreateRunner(invoker, output);

        runner.Run(new[] { "--set", "hdr", "1" });

        output.StdErr.Should().Contain(msg => msg.Contains("初始化失败"));
        output.StdErr.Should().Contain(msg => msg.Contains("connection failed"));
    }

    // ── Edge cases ──

    [Fact]
    public void Run_Set_WithSlash_Succeeds()
    {
        var invoker = new FakeInvoker();
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "/set", "hdr", "1" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    [Fact]
    public void Run_Get_WithSlash_Succeeds()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = System.Text.Json.JsonDocument.Parse("""{"Value":"1"}""").RootElement
        };
        var runner = CreateRunner(invoker);

        var code = runner.Run(new[] { "/get", "hdr" });

        code.Should().Be(0);
        invoker.InitializeCalls.Should().Be(1);
    }

    // ── Factory ──

    private static AppRunner CreateRunner(
        FakeInvoker? invoker = null,
        FakeOutput? output = null)
    {
        invoker ??= new FakeInvoker();
        output ??= new FakeOutput();
        return new AppRunner(
            new CliParser(),
            new SettingService(invoker),
            new InfoService(invoker),
            invoker,
            output);
    }

    // ── Fakes ──

    private sealed class FakeInvoker : IAocInvoker
    {
        public int InitializeCalls { get; private set; }
        public bool NextInitResult { get; set; } = true;
        public string? InitDiagnostic { get; set; }
        public bool NextOkResult { get; set; } = true;
        public object? NextCallResult { get; set; }
        public string? LastDiagnostic { get; private set; }

        public bool TryInitialize(out string? diagnostic)
        {
            InitializeCalls++;
            diagnostic = InitDiagnostic;
            return NextInitResult;
        }

        public object? Call(string method, params object?[] args) => NextCallResult;

        public bool Ok(string method, params object?[] args) => NextOkResult;

        public Task<(bool Success, string? Diagnostic)> TryInitializeAsync()
            => Task.FromResult((TryInitialize(out var d), d));

        public Task<object?> CallAsync(string method, params object?[] args)
            => Task.FromResult(Call(method, args));

        public Task<bool> OkAsync(string method, params object?[] args)
            => Task.FromResult(Ok(method, args));
    }

    private sealed class FakeOutput : IConsoleOutput
    {
        public List<string> StdOut { get; } = new();
        public List<string> StdErr { get; } = new();
        public List<InfoQueryResult> InfoResults { get; } = new();

        public void PrintHelp(IReadOnlyCollection<SettingDef> settings)
        {
            StdOut.Add("AOC OSD 控制器");
        }

        public void PrintInfo(IReadOnlyList<InfoQueryResult> results)
        {
            InfoResults.AddRange(results);
            StdOut.Add($"(info: {results.Count} topic(s))");
        }

        public void PrintMessage(string message, bool error = false)
        {
            if (error) StdErr.Add(message);
            else StdOut.Add(message);
        }
    }
}
