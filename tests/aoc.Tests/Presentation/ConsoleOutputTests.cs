using System.Text.Json;
using aoc.Application;
using aoc.Domain;
using aoc.Presentation;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Presentation;

public class ConsoleOutputTests
{
    private readonly ConsoleOutput _output = new();

    // ── PrintHelp ──

    [Fact]
    public void PrintHelp_OutputsHeader()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        text.Should().Contain("aoc — USB 私有协议 OSD 设置工具");
        text.Should().Contain("--help");
    }

    [Fact]
    public void PrintHelp_HasNoCategoryHeaders()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        // Headers used "── {category} ──" which contained "──" (box-drawing chars)
        text.Should().NotContain("──");
    }

    [Fact]
    public void PrintHelp_ListsAllSettings()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        // Spot-check each category
        text.Should().Contain("overdrive");
        text.Should().Contain("low-blue");
        text.Should().Contain("color-gamut");
        text.Should().Contain("hdr");
    }

    [Fact]
    public void PrintHelp_ShowsEnumValues()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        // overdrive's enum values should appear
        text.Should().Contain("off");
        text.Should().Contain("weak");
        text.Should().Contain("medium");
        text.Should().Contain("strong");
        text.Should().Contain("strongest");
    }

    [Fact]
    public void PrintHelp_ShowsExamples()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        text.Should().Contain("示例");
        text.Should().Contain("--set overdrive strong");
        text.Should().Contain("--set hdr hdr1");
        text.Should().Contain("--get hdr");
    }

    [Fact]
    public void PrintHelp_EmptySettings_DoesNotThrow()
    {
        var act = () => _output.PrintHelp(Array.Empty<SettingDef>());
        act.Should().NotThrow();
    }

    [Fact]
    public void PrintHelp_ShowsInfoSection()
    {
        var text = CaptureStdOut(() =>
            _output.PrintHelp(SettingCatalog.All.Values.ToArray()));

        text.Should().Contain("--info");
        text.Should().Contain("信息查询");
        text.Should().Contain("message");
        text.Should().Contain("list");
        text.Should().Contain("support");
        text.Should().Contain("oled");
    }

    // ── PrintMessage ──

    [Fact]
    public void PrintMessage_Normal_WritesToStdOut()
    {
        var text = CaptureStdOut(() => _output.PrintMessage("hello"));
        text.TrimEnd().Should().Be("hello");
    }

    [Fact]
    public void PrintMessage_Error_WritesToStdErr()
    {
        var text = CaptureStdErr(() => _output.PrintMessage("error msg", error: true));
        text.TrimEnd().Should().Be("error msg");
    }

    [Fact]
    public void PrintMessage_EmptyString_DoesNotThrow()
    {
        var act = () => _output.PrintMessage("");
        act.Should().NotThrow();
    }

    // ── PrintInfo ──

    [Fact]
    public void PrintInfo_EmptyResults_DoesNotThrow()
    {
        var act = () => _output.PrintInfo(Array.Empty<InfoQueryResult>());
        act.Should().NotThrow();
    }

    [Fact]
    public void PrintInfo_FailedResult_PrintsError()
    {
        var result = new InfoQueryResult("message", "显示器型号/固件版本",
            Success: false, Message: "❌ 查询失败");

        var text = CaptureStdErr(() => _output.PrintInfo(new[] { result }));

        text.Should().Contain("❌");
        text.Should().Contain("查询失败");
    }

    [Fact]
    public void PrintInfo_FailedResult_WithDiagnostic_PrintsDiagnostic()
    {
        var result = new InfoQueryResult("message", "显示器型号/固件版本",
            Success: false, Message: "❌ 查询失败", Diagnostic: "timeout");

        var text = CaptureStdErr(() => _output.PrintInfo(new[] { result }));

        text.Should().Contain("诊断");
        text.Should().Contain("timeout");
    }

    [Fact]
    public void PrintInfo_StringResult_PrintsValue()
    {
        var result = new InfoQueryResult("name", "当前显示器名称",
            Success: true, RawValue: JsonDocument.Parse("\"U27G3X\"").RootElement);

        var text = CaptureStdOut(() => _output.PrintInfo(new[] { result }));

        text.Should().Contain("显示器名称");
        text.Should().Contain("U27G3X");
    }

    [Fact]
    public void PrintInfo_ObjectResult_PrintsProperties()
    {
        var json = JsonDocument.Parse("""{"Model":"U27G3X","FW":"v1.0"}""").RootElement;
        var result = new InfoQueryResult("message", "显示器型号/固件版本",
            Success: true, RawValue: json);

        var text = CaptureStdOut(() => _output.PrintInfo(new[] { result }));

        text.Should().Contain("Model");
        text.Should().Contain("U27G3X");
        text.Should().Contain("FW");
        text.Should().Contain("v1.0");
    }

    [Fact]
    public void PrintInfo_ArrayResult_PrintsItems()
    {
        var json = JsonDocument.Parse("""["DP-1", "HDMI-1"]""").RootElement;
        var result = new InfoQueryResult("list", "已连接显示器列表",
            Success: true, RawValue: json);

        var text = CaptureStdOut(() => _output.PrintInfo(new[] { result }));

        text.Should().Contain("DP-1");
        text.Should().Contain("HDMI-1");
    }

    [Fact]
    public void PrintInfo_MultipleResults_PrintsAll()
    {
        var r1 = new InfoQueryResult("message", "显示器型号/固件版本",
            Success: true, RawValue: JsonDocument.Parse("\"U27G3X\"").RootElement);
        var r2 = new InfoQueryResult("name", "当前显示器名称",
            Success: true, RawValue: JsonDocument.Parse("\"Main Display\"").RootElement);

        var text = CaptureStdOut(() => _output.PrintInfo(new[] { r1, r2 }));

        text.Should().Contain("信息总览");
        text.Should().Contain("U27G3X");
        text.Should().Contain("Main Display");
    }

    // ── Helpers ──

    /// <summary>Captures Console.WriteLine output during an action.</summary>
    private static string CaptureStdOut(Action action)
    {
        var original = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);
            action();
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>Captures Console.Error.WriteLine output during an action.</summary>
    private static string CaptureStdErr(Action action)
    {
        var original = Console.Error;
        try
        {
            using var sw = new StringWriter();
            Console.SetError(sw);
            action();
            return sw.ToString();
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
