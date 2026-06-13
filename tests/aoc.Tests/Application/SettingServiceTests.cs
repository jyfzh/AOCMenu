using System.Text.Json;
using aoc.Application;
using aoc.Domain;
using aoc.Infrastructure;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Application;

public class SettingServiceTests
{
    // ── Set: unknown key ──

    [Fact]
    public void Set_UnknownKey_ReturnsInvalidArgument()
    {
        var invoker = new FakeInvoker();
        var sut = new SettingService(invoker);

        var result = sut.Set("no-such-setting", "1");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.InvalidArgument);
        result.UserMessage.Should().Contain("未知设置");
    }

    // ── Set: invalid enum value ──

    [Fact]
    public void Set_InvalidEnumValue_ReturnsInvalidArgument()
    {
        var invoker = new FakeInvoker();
        var sut = new SettingService(invoker);

        var result = sut.Set("overdrive", "ultra");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.InvalidArgument);
        result.UserMessage.Should().Contain("可用值");
    }

    [Fact]
    public void Set_InvalidEnumValue_ShowsAllChoices()
    {
        var invoker = new FakeInvoker();
        var sut = new SettingService(invoker);

        var result = sut.Set("overdrive", "invalid");

        result.UserMessage.Should().Contain("off");
        result.UserMessage.Should().Contain("weak");
        result.UserMessage.Should().Contain("medium");
        result.UserMessage.Should().Contain("strong");
        result.UserMessage.Should().Contain("strongest");
    }

    // ── Set: valid enum (string-based methods) ──

    [Theory]
    [InlineData("overdrive", "off", "SetOverDrive", "0")]
    [InlineData("overdrive", "weak", "SetOverDrive", "1")]
    [InlineData("overdrive", "medium", "SetOverDrive", "2")]
    [InlineData("overdrive", "strong", "SetOverDrive", "3")]
    [InlineData("overdrive", "strongest", "SetOverDrive", "4")]
    [InlineData("color-gamut", "srgb", "SetColorGamut", "2")]
    [InlineData("color-gamut", "standard", "SetColorGamut", "0")]
    [InlineData("low-blue", "关闭", "SetLowBlueModel", "0")]
    [InlineData("low-blue", "多媒体", "SetLowBlueModel", "1")]
    [InlineData("low-blue", "网络", "SetLowBlueModel", "2")]
    [InlineData("low-blue", "办公室", "SetLowBlueModel", "3")]
    [InlineData("low-blue", "阅读", "SetLowBlueModel", "4")]
    [InlineData("gamma", "1", "SetGamma", "0")]
    [InlineData("gamma", "2", "SetGamma", "1")]
    [InlineData("gamma", "3", "SetGamma", "2")]
    [InlineData("hdr", "关闭", "SetHDR", "0")]
    [InlineData("hdr", "DisplayHDR", "SetHDR", "1")]
    [InlineData("hdr", "图片", "SetHDR", "2")]
    [InlineData("hdr", "电影", "SetHDR", "3")]
    [InlineData("hdr", "游戏", "SetHDR", "4")]
    public void Set_ValidEnum_InvokesWithMappedValue(
        string key, string userValue, string expectedMethod, string expectedMappedValue)
    {
        var invoker = new FakeInvoker { NextOkResult = true };
        var sut = new SettingService(invoker);

        var result = sut.Set(key, userValue);

        result.Success.Should().BeTrue();
        invoker.OkCalls.Should().ContainSingle();
        invoker.OkCalls[0].Method.Should().Be(expectedMethod);
        invoker.OkCalls[0].Args.Should().Contain(expectedMappedValue);
    }



    // ── Set: prerequisite (color-gamut standard required) ──

    [Fact]
    public void Set_Gamma_WhenColorGamutNotStandard_ReturnsPrerequisiteNotMet()
    {
        var invoker = new FakeInvoker
        {
            // Get("color-gamut") returns raw value "2" (= srgb)
            NextCallResult = BuildColorGamutResult("2")
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("gamma", "1");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.PrerequisiteNotMet);
        result.UserMessage.Should().Contain("色彩空间");
        result.UserMessage.Should().Contain("standard");
    }

    [Fact]
    public void Set_LowBlue_WhenColorGamutNotStandard_ReturnsPrerequisiteNotMet()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = BuildColorGamutResult("2")
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("low-blue", "1");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.PrerequisiteNotMet);
    }

    [Fact]
    public void Set_Hdr_WhenColorGamutNotStandard_ReturnsPrerequisiteNotMet()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = BuildColorGamutResult("2")
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("hdr", "1");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.PrerequisiteNotMet);
    }

    [Fact]
    public void Set_Gamma_WhenColorGamutIsStandard_AllowsSetting()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = BuildColorGamutResult("0"), // standard
            NextOkResult = true
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("gamma", "1");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Set_Overdrive_WhenColorGamutNotStandard_IsUnaffected()
    {
        // overdrive has no prerequisite — should work regardless
        var invoker = new FakeInvoker
        {
            NextCallResult = BuildColorGamutResult("2"),
            NextOkResult = true
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("overdrive", "strong");

        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Set_Gamma_WhenColorGamutPrereqCheckFails_AllowsSetting()
    {
        // If Get("color-gamut") fails (returns NotSupported), allow the operation
        var invoker = new FakeInvoker
        {
            NextCallResult = null, // Get fails
            NextOkResult = true
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("gamma", "1");

        result.Success.Should().BeTrue();
    }

    // ── Set: color-gamut requires HDR off ──

    [Fact]
    public void Set_ColorGamut_WhenHdrNotZero_ReturnsPrerequisiteNotMet()
    {
        var invoker = new FakeInvoker
        {
            // HDR value "1" (on) returned from GetHDR()
            NextCallResult = JsonDocument.Parse("""{"Value":"1","MaxValue":"4"}""").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("color-gamut", "srgb");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.PrerequisiteNotMet);
        result.UserMessage.Should().Contain("色彩空间");
        result.UserMessage.Should().Contain("HDR");
    }

    [Fact]
    public void Set_ColorGamut_WhenHdrIsZero_AllowsSetting()
    {
        var invoker = new FakeInvoker
        {
            // HDR value "0" (off) returned from GetHDR()
            NextCallResult = JsonDocument.Parse("""{"Value":"0","MaxValue":"4"}""").RootElement,
            NextOkResult = true
        };
        var sut = new SettingService(invoker);

        var result = sut.Set("color-gamut", "srgb");

        result.Success.Should().BeTrue();
    }

    // ── Set: invocation failure ──

    [Fact]
    public void Set_WhenInvokerFails_ReturnsInvocationFailed()
    {
        var invoker = new FakeInvoker { NextOkResult = false, Diag = "device busy" };
        var sut = new SettingService(invoker);

        var result = sut.Set("hdr", "1");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.InvocationFailed);
        result.UserMessage.Should().Contain("❌");
    }

    // ── Get: unknown key ──

    [Fact]
    public void Get_UnknownKey_ReturnsInvalidArgument()
    {
        var invoker = new FakeInvoker();
        var sut = new SettingService(invoker);

        var result = sut.Get("no-such-setting");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.InvalidArgument);
    }

    // ── Get: without read strategy ──

    [Theory]
    [InlineData("gamma")]
    public void Get_WithoutReadStrategy_ReturnsNotSupported(string key)
    {
        var invoker = new FakeInvoker();
        var sut = new SettingService(invoker);

        var result = sut.Get(key);

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.NotSupported);
        result.UserMessage.Should().Contain("不支持查询");
    }





    // ── Get: with Getter ──

    [Fact]
    public void Get_WithGetter_LowBlue_ReturnsValue()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse("""{"Value":"2","MaxValue":"4"}""").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("low-blue");

        result.Success.Should().BeTrue();
        result.Value.Should().Be("2");
        result.MaxValue.Should().Be(4);
    }

    [Fact]
    public void Get_WithGetter_ReturnsValue()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse("""{"Value":"1","MaxValue":"5"}""").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("hdr");

        result.Success.Should().BeTrue();
        result.Value.Should().Be("1");
        result.MaxValue.Should().Be(5);
    }

    [Fact]
    public void Get_WithGetter_ShowsRawValue()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse("""{"Value":"1","MaxValue":"4"}""").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("hdr");

        result.Success.Should().BeTrue();
        // Reverse map converts raw "1" to display name "DisplayHDR", appends raw value in parens
        result.UserMessage.Should().Be("HDR 模式: DisplayHDR (1)");
        result.Value.Should().Be("1");
        result.MaxValue.Should().Be(4);
    }



    // ── Get: with ReadProperty ──

    [Fact]
    public void Get_WithReadPropertyViaJsonElement_ReturnsValue()
    {
        var displayJson = BuildDisplayJson("EXT_OP_E2A0_20_ColorGamut", "0", "2");
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse(displayJson).RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("color-gamut");

        result.Success.Should().BeTrue();
        result.Value.Should().Be("0");
        result.MaxValue.Should().Be(2);
    }

    [Fact]
    public void Get_WithReadProperty_MissingCurrItem_ReturnsNull()
    {
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse("""{"OtherProp":"value"}""").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("color-gamut");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.NotSupported);
    }

    // ── Get: read property with enum map ──

    [Fact]
    public void Get_WithReadPropertyAndEnum_DisplaysEnumName()
    {
        var displayJson = BuildDisplayJson("EXT_OP_E2A0_20_ColorGamut", "0", "2");
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse(displayJson).RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("color-gamut");

        result.Success.Should().BeTrue();
        result.UserMessage.Should().Contain("standard");
    }

    // ── Get: invoker returns null ──

    [Fact]
    public void Get_WhenCallReturnsNull_ReturnsNotSupported()
    {
        var invoker = new FakeInvoker { NextCallResult = null };
        var sut = new SettingService(invoker);

        var result = sut.Get("hdr");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.NotSupported);
    }

    [Fact]
    public void Get_WhenCallReturnsEmptyJsonObject_ReturnsNotSupported()
    {
        // Simulates ProxyClientInvoker error path (returns {} instead of default).
        var invoker = new FakeInvoker
        {
            NextCallResult = JsonDocument.Parse("{}").RootElement
        };
        var sut = new SettingService(invoker);

        var result = sut.Get("color-gamut");

        // Empty object is not null, but has no Tag/CurrItem — returns not-supported.
        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.NotSupported);
    }

    // ── Get: invoker returns string (non-JsonElement) ──

    [Fact]
    public void Get_WhenCallReturnsString_ReturnsNotSupported()
    {
        var invoker = new FakeInvoker { NextCallResult = "some string" };
        var sut = new SettingService(invoker);

        var result = sut.Get("hdr");

        result.Success.Should().BeFalse();
        result.ErrorKind.Should().Be(ErrorKind.NotSupported);
    }

    // ── Helpers ──

    private static string BuildDisplayJson(string propertyName, string value, string maxValue)
    {
        return $$"""
        {
          "CurrItem": {
            "{{propertyName}}": {
              "Value": "{{value}}",
              "MaxValue": "{{maxValue}}"
            }
          }
        }
        """;
    }

    /// <summary>
    /// Builds a GetDisPlay JSON result that includes color-gamut at
    /// CurItem.EXT_OP_E2A0_20_ColorGamut, matching the SettingDef setup.
    /// </summary>
    private static JsonElement BuildColorGamutResult(string rawValue)
    {
        var json = BuildDisplayJson("EXT_OP_E2A0_20_ColorGamut", rawValue, "2");
        return JsonDocument.Parse(json).RootElement;
    }

    private sealed class FakeInvoker : IAocInvoker
    {
        public bool NextOkResult { get; set; } = true;
        public object? NextCallResult { get; set; } = null;
        public string? Diag { get; set; } = null;
        public string? LastDiagnostic => Diag;

        public List<(string Method, object?[] Args)> OkCalls { get; } = new();

        public bool TryInitialize(out string? diagnostic)
        {
            diagnostic = null;
            return true;
        }

        public object? Call(string method, params object?[] args)
        {
            return NextCallResult;
        }

        public bool Ok(string method, params object?[] args)
        {
            OkCalls.Add((method, args));
            return NextOkResult;
        }

        public Task<(bool Success, string? Diagnostic)> TryInitializeAsync()
            => Task.FromResult((TryInitialize(out var d), d));

        public Task<object?> CallAsync(string method, params object?[] args)
            => Task.FromResult(Call(method, args));

        public Task<bool> OkAsync(string method, params object?[] args)
            => Task.FromResult(Ok(method, args));
    }
}
