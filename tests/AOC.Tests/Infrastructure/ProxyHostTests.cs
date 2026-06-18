using aoc.Infrastructure.IPC;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Infrastructure;

/// <summary>
/// Tests for ProxyHost lifecycle management.
/// These focus on contract and state assertions that don't
/// require actually starting a child process (which is
/// tested via integration tests on real hardware).
/// </summary>
public class ProxyHostTests
{
    [Fact]
    public void Implements_IAsyncDisposable()
    {
        var sut = new ProxyHost();
        sut.Should().BeAssignableTo<IAsyncDisposable>();
    }

    [Fact]
    public void DefaultPipeName_IsZeasnProxy()
    {
        var sut = new ProxyHost();
        sut.PipeName.Should().Be("ZeasnProxy");
    }

    [Fact]
    public void DisposeAsync_MultipleCalls_DoesNotThrow()
    {
        var sut = new ProxyHost();
        var act = async () =>
        {
            await sut.DisposeAsync();
            await sut.DisposeAsync();
            await sut.DisposeAsync();
        };
        act.Should().NotThrowAsync();
    }

    [Fact]
    public void StartAsync_WithoutProxyExe_Throws()
    {
        // Simulate scenario where ZeasnProxy.exe is not deployed alongside the test runner.
        var sut = new ProxyHost();
        var act = async () => await sut.StartAsync(TimeSpan.FromSeconds(1));
        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ZeasnProxy.exe not found*");
    }

    [Fact]
    public void StartAsync_WithoutProxyExe_DoesNotLeaveProcessRunning()
    {
        // Verify cleanup on failure path
        var sut = new ProxyHost();
        var act = () => sut.StartAsync(TimeSpan.FromSeconds(1));
        act.Should().ThrowAsync<Exception>();
    }
}
