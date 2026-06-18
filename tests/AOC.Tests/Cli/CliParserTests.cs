using aoc.Cli;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Cli;

public class CliParserTests
{
    private readonly CliParser _parser = new();

    // ── Help command ──

    [Fact]
    public void Parse_NoArgs_ReturnsHelp()
    {
        var cmd = _parser.Parse(Array.Empty<string>());
        cmd.Should().BeOfType<HelpCommand>();
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("/help")]
    [InlineData("-h")]
    [InlineData("/h")]
    public void Parse_HelpFlags_ReturnsHelp(string arg)
    {
        var cmd = _parser.Parse(new[] { arg });
        cmd.Should().BeOfType<HelpCommand>();
    }

    [Fact]
    public void Parse_HelpAmidstUnknownArgs_StillReturnsHelp()
    {
        // Help flag is checked first in the loop, so even if there are unknown trailing args
        // the first known flag wins. Parser returns on first match.
        var cmd = _parser.Parse(new[] { "--help", "some-unknown-arg" });
        cmd.Should().BeOfType<HelpCommand>();
    }

    // ── Set command (--) ──

    [Fact]
    public void Parse_SetCommand_WithDashDash_ReturnsSetCommand()
    {
        var cmd = _parser.Parse(new[] { "--set", "Brightness", "50" });
        var set = cmd.Should().BeOfType<SetCommand>().Subject;
        set.Key.Should().Be("brightness");
        set.Value.Should().Be("50");
    }

    [Fact]
    public void Parse_SetCommand_WithSlash_ReturnsSetCommand()
    {
        var cmd = _parser.Parse(new[] { "/set", "contrast", "70" });
        var set = cmd.Should().BeOfType<SetCommand>().Subject;
        set.Key.Should().Be("contrast");
        set.Value.Should().Be("70");
    }

    [Fact]
    public void Parse_SetCommand_NormalizesKeyToLowercase()
    {
        var cmd = _parser.Parse(new[] { "--set", "HDR", "1" });
        var set = cmd.Should().BeOfType<SetCommand>().Subject;
        set.Key.Should().Be("hdr");
    }

    [Fact]
    public void Parse_SetCommand_MissingValue_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "--set", "brightness" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    [Fact]
    public void Parse_SetCommand_WithSlash_MissingValue_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "/set", "brightness" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    [Fact]
    public void Parse_SetCommand_Slash_MissingBothKeyAndValue_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "/set" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    [Fact]
    public void Parse_SetCommand_DashDash_MissingBothKeyAndValue_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "--set" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    // ── Get command (--) ──

    [Fact]
    public void Parse_GetCommand_WithDashDash_ReturnsGetCommand()
    {
        var cmd = _parser.Parse(new[] { "--get", "HDR" });
        var get = cmd.Should().BeOfType<GetCommand>().Subject;
        get.Key.Should().Be("hdr");
    }

    [Fact]
    public void Parse_GetCommand_WithSlash_ReturnsGetCommand()
    {
        var cmd = _parser.Parse(new[] { "/get", "brightness" });
        var get = cmd.Should().BeOfType<GetCommand>().Subject;
        get.Key.Should().Be("brightness");
    }

    [Fact]
    public void Parse_GetCommand_NormalizesKeyToLowercase()
    {
        var cmd = _parser.Parse(new[] { "--get", "OverDrive" });
        var get = cmd.Should().BeOfType<GetCommand>().Subject;
        get.Key.Should().Be("overdrive");
    }

    [Fact]
    public void Parse_GetCommand_MissingKey_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "--get" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    [Fact]
    public void Parse_GetCommand_WithSlash_MissingKey_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "/get" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("用法");
    }

    // ── Unknown / invalid args ──

    [Fact]
    public void Parse_UnknownArg_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "foobar" });
        cmd.Should().BeOfType<InvalidCommand>();
    }

    [Fact]
    public void Parse_MultipleUnknownArgs_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "foo", "bar", "baz" });
        cmd.Should().BeOfType<InvalidCommand>();
    }

    [Fact]
    public void Parse_SetDashDashWithExtraTrailingArgs_IgnoresExtras()
    {
        // Parser only takes the next 2 args for --set, extra args after are ignored
        var cmd = _parser.Parse(new[] { "--set", "brightness", "50", "extra1", "extra2" });
        var set = cmd.Should().BeOfType<SetCommand>().Subject;
        set.Key.Should().Be("brightness");
        set.Value.Should().Be("50");
    }

    [Fact]
    public void Parse_GetDashDashWithExtraTrailingArgs_IgnoresExtras()
    {
        var cmd = _parser.Parse(new[] { "--get", "hdr", "extra" });
        var get = cmd.Should().BeOfType<GetCommand>().Subject;
        get.Key.Should().Be("hdr");
    }

    // ── Info command ──

    [Fact]
    public void Parse_Info_WithDashDash_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().BeNull();
    }

    [Fact]
    public void Parse_Info_WithSlash_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "/info" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().BeNull();
    }

    [Fact]
    public void Parse_Info_WithTopicMessage_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "message" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("message");
    }

    [Fact]
    public void Parse_Info_WithTopicList_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "list" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("list");
    }

    [Fact]
    public void Parse_Info_WithTopicSupport_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "support" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("support");
    }

    [Fact]
    public void Parse_Info_WithTopicName_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "name" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("name");
    }

    [Fact]
    public void Parse_Info_WithTopicDevice_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "device" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("device");
    }

    [Fact]
    public void Parse_Info_WithTopicOled_ReturnsInfoCommand()
    {
        var cmd = _parser.Parse(new[] { "--info", "oled" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("oled");
    }

    [Fact]
    public void Parse_Info_WithUnknownTopic_ReturnsInvalid()
    {
        var cmd = _parser.Parse(new[] { "--info", "bogus" });
        var invalid = cmd.Should().BeOfType<InvalidCommand>().Subject;
        invalid.Message.Should().Contain("未知信息查询");
        invalid.Message.Should().Contain("message"); // should list available topics
    }

    [Fact]
    public void Parse_Info_NormalizesTopicToLowercase()
    {
        var cmd = _parser.Parse(new[] { "--info", "MESSAGE" });
        var info = cmd.Should().BeOfType<InfoCommand>().Subject;
        info.Topic.Should().Be("message");
    }

    // ── Mixed precedence ──

    [Fact]
    public void Parse_HelpFlagBeforeSet_ReturnsHelp()
    {
        var cmd = _parser.Parse(new[] { "--help", "--set", "brightness", "50" });
        cmd.Should().BeOfType<HelpCommand>();
    }

    [Fact]
    public void Parse_OnlyFirstCommandIsProcessed()
    {
        // Parser returns on first matched command; subsequent --set is ignored
        var cmd = _parser.Parse(new[] { "--get", "hdr", "--set", "brightness", "50" });
        var get = cmd.Should().BeOfType<GetCommand>().Subject;
        get.Key.Should().Be("hdr");
    }
}
