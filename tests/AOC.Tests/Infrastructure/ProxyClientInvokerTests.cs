using System.Text.Json;
using aoc.Infrastructure;
using aoc.Infrastructure.IPC;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Infrastructure;

/// <summary>
/// Tests for the ProxyClientInvoker (IPC client).
/// Most methods require a live pipe server, but we can
/// test the interface contract and error handling paths
/// that don't require a connection.
/// </summary>
public class ProxyClientInvokerTests
{
    [Fact]
    public void Implements_IAocInvoker()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        sut.Should().BeAssignableTo<IAocInvoker>();
    }

    [Fact]
    public void Constructor_SetsDefaultDiagnosticToNull()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        sut.LastDiagnostic.Should().BeNull();
    }

    [Fact]
    public void DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        var act = async () =>
        {
            await sut.DisposeAsync();
            await sut.DisposeAsync();
            await sut.DisposeAsync();
        };
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void TryInitialize_BeforeConnect_ReturnsFalse()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        var ok = sut.TryInitialize(out var diagnostic);
        ok.Should().BeFalse();
        diagnostic.Should().Be("Proxy initialization failed");
    }

    [Fact]
    public void Call_BeforeConnect_ReturnsEmptyJsonElement()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        var result = sut.Call("SomeMethod", "arg1");

        // On error paths, Call should return a safe empty object (not null/undefined)
        result.Should().NotBeNull();
        result.Should().BeOfType<JsonElement>();
        var json = (JsonElement)result!;
        json.ValueKind.Should().Be(JsonValueKind.Object);

        // Empty object - TryGetProperty returns false
        json.TryGetProperty("anything", out _).Should().BeFalse();
    }

    [Fact]
    public void Ok_BeforeConnect_ReturnsFalse()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        var ok = sut.Ok("SomeMethod", "arg1");
        ok.Should().BeFalse();
    }

    [Fact]
    public async Task TryInitialize_AfterDispose_ReturnsFalse()
    {
        var sut = new ProxyClientInvoker("test-pipe");
        await sut.DisposeAsync();
        var ok = sut.TryInitialize(out var diagnostic);
        ok.Should().BeFalse();
    }
}
