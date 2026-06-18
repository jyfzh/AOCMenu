using System.Text.Json;
using aoc.Application;
using aoc.Infrastructure;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Application;

public class InfoServiceTests
{
    // ── Topics registration ──

    [Fact]
    public void Topics_ContainsAllExpectedKeys()
    {
        InfoService.Topics.Keys.Should().BeEquivalentTo(
            new[] { "message", "list", "name", "device", "support", "oled" });
    }

    [Fact]
    public void Topics_AllHaveSdkMethodDefined()
    {
        foreach (var (key, def) in InfoService.Topics)
        {
            def.Key.Should().Be(key);
            def.SdkMethod.Should().NotBeNullOrWhiteSpace($"topic '{key}' must have an SDK method");
        }
    }

    [Fact]
    public void Topics_MessageIsFeatured()
    {
        InfoService.Topics["message"].IsFeatured.Should().BeTrue();
    }

    // ── Query single topic ──

    [Fact]
    public void Query_UnknownTopic_ReturnsFailedResult()
    {
        var invoker = new FakeInvoker();
        var sut = new InfoService(invoker);

        var result = sut.Query("no-such-topic");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("未知信息查询");
    }

    [Fact]
    public void Query_UnknownTopic_MessageListsAvailableTopics()
    {
        var invoker = new FakeInvoker();
        var sut = new InfoService(invoker);

        var result = sut.Query("bogus");

        result.Message.Should().Contain("message");
        result.Message.Should().Contain("list");
        result.Message.Should().Contain("oled");
    }

    [Fact]
    public void Query_WhenSdkReturnsNull_ReturnsFailedResult()
    {
        var invoker = new FakeInvoker();
        var sut = new InfoService(invoker);

        var result = sut.Query("message");

        // FakeInvoker returns null by default
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("查询失败");
    }

    [Fact]
    public void Query_WhenSdkReturnsNull_IncludesDiagnostic()
    {
        var invoker = new FakeInvoker { LastDiagnostic = "SDK timeout" };
        var sut = new InfoService(invoker);

        var result = sut.Query("message");

        result.Diagnostic.Should().Be("SDK timeout");
    }

    [Fact]
    public void Query_WhenSdkReturnsValue_ReturnsSuccess()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"result\"").RootElement };
        var sut = new InfoService(invoker);

        var result = sut.Query("name");

        result.Success.Should().BeTrue();
        result.RawValue.Should().NotBeNull();
    }

    [Fact]
    public void Query_CallsCorrectSdkMethod()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"ok\"").RootElement };
        var sut = new InfoService(invoker);

        sut.Query("message");

        invoker.LastCalledMethod.Should().Be("GetDisPlayMessage");
    }

    [Fact]
    public void Query_Device_CallsGetConnectDevice()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"ok\"").RootElement };
        var sut = new InfoService(invoker);

        sut.Query("device");

        invoker.LastCalledMethod.Should().Be("GetConnectDevice");
    }

    [Fact]
    public void Query_Oled_CallsGetOLEDPanelCareInfo()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"ok\"").RootElement };
        var sut = new InfoService(invoker);

        sut.Query("oled");

        invoker.LastCalledMethod.Should().Be("GetOLEDPanelCareInfo");
    }

    // ── QueryAll ──

    [Fact]
    public void QueryAll_ReturnsAllTopics()
    {
        var invoker = new FakeInvoker { NextCallResult = JsonDocument.Parse("\"x\"").RootElement };
        var sut = new InfoService(invoker);

        var results = sut.QueryAll();

        results.Should().HaveCount(InfoService.Topics.Count);
        results.Select(r => r.TopicKey).Should().BeEquivalentTo(InfoService.Topics.Keys);
    }

    [Fact]
    public void QueryAll_PartialFailure_SomeResultsFail()
    {
        var invoker = new FakeInvoker();
        // Default NextCallResult is null, so all queries fail
        var sut = new InfoService(invoker);

        var results = sut.QueryAll();

        results.Should().AllSatisfy(r => r.Success.Should().BeFalse());
    }

    // ── GetAllTopics ──

    [Fact]
    public void GetAllTopics_ReturnsAllDefs()
    {
        var topics = InfoService.GetAllTopics();

        topics.Should().HaveCount(6);
    }

    // ── Fakes ──

    private sealed class FakeInvoker : IAocInvoker
    {
        public string? LastCalledMethod { get; private set; }
        public string? LastDiagnostic { get; set; }
        public object? NextCallResult { get; set; }

        public bool TryInitialize(out string? diagnostic)
        {
            diagnostic = null;
            return true;
        }

        public object? Call(string method, params object?[] args)
        {
            LastCalledMethod = method;
            return NextCallResult;
        }

        public bool Ok(string method, params object?[] args) => true;

        public Task<(bool Success, string? Diagnostic)> TryInitializeAsync()
            => Task.FromResult((TryInitialize(out var d), d));

        public Task<object?> CallAsync(string method, params object?[] args)
            => Task.FromResult(Call(method, args));

        public Task<bool> OkAsync(string method, params object?[] args)
            => Task.FromResult(Ok(method, args));
    }
}
