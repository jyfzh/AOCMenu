using aoc.SdkProxy;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Infrastructure;

/// <summary>
/// Unit tests for the SessionTracker class used by ZeasnProxy to manage
/// active client sessions and idle timeout coordination.
///
/// SessionTracker is pure C# (ConcurrentDictionary, CancellationTokenSource)
/// with no SDK dependency — these tests run without monitor hardware.
/// </summary>
public sealed class SessionTrackerTests
{
    private static SessionTracker CreateTracker(int idleTimeoutSec = 30)
        => new(idleTimeoutSec);

    [Fact]
    public void Constructor_StartsIdle()
    {
        var sut = CreateTracker();
        sut.IsIdle.Should().BeTrue();
        sut.ActiveSessionCount.Should().Be(0);
    }

    [Fact]
    public void RegisterSession_ReturnsNonEmptyGuid()
    {
        var sut = CreateTracker();
        var id = sut.RegisterSession();
        id.Should().NotBeEmpty();
    }

    [Fact]
    public void RegisterSession_IncrementsCount()
    {
        var sut = CreateTracker();
        sut.RegisterSession();
        sut.ActiveSessionCount.Should().Be(1);
        sut.IsIdle.Should().BeFalse();
    }

    [Fact]
    public void MultipleRegisters_IncrementCount()
    {
        var sut = CreateTracker();
        sut.RegisterSession();
        sut.RegisterSession();
        sut.RegisterSession();
        sut.ActiveSessionCount.Should().Be(3);
    }

    [Fact]
    public void UnregisterSession_DecrementsCount()
    {
        var sut = CreateTracker();
        var id1 = sut.RegisterSession();
        var id2 = sut.RegisterSession();
        sut.UnregisterSession(id1);

        sut.ActiveSessionCount.Should().Be(1);
        sut.IsIdle.Should().BeFalse();
    }

    [Fact]
    public void UnregisterAll_BecomesIdle()
    {
        var sut = CreateTracker();
        var id1 = sut.RegisterSession();
        var id2 = sut.RegisterSession();
        sut.UnregisterSession(id1);
        sut.UnregisterSession(id2);

        sut.IsIdle.Should().BeTrue();
        sut.ActiveSessionCount.Should().Be(0);
    }

    [Fact]
    public void RegisterSession_WhenIdle_CancelsIdleTimeout()
    {
        var sut = CreateTracker(idleTimeoutSec: 30);

        // Become idle
        var id = sut.RegisterSession();
        sut.UnregisterSession(id);
        sut.IsIdle.Should().BeTrue();

        // Register a new session — should cancel the idle timeout
        sut.RegisterSession();

        sut.IsIdle.Should().BeFalse();
        sut.ActiveSessionCount.Should().Be(1);
    }

    [Fact]
    public void CreateWaitCts_WhenActive_NoIdleTimeout()
    {
        var sut = CreateTracker();
        using var shutdownCts = new CancellationTokenSource();

        sut.RegisterSession();
        using var waitCts = sut.CreateWaitCts(shutdownCts.Token);

        // Token should NOT be cancelled (no idle timeout linked)
        waitCts.Token.IsCancellationRequested.Should().BeFalse();

        // Cancelling shutdown should propagate
        shutdownCts.Cancel();
        waitCts.Token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task CreateWaitCts_WhenIdle_HasIdleTimeout()
    {
        var sut = CreateTracker(idleTimeoutSec: 1); // short timeout
        using var shutdownCts = new CancellationTokenSource();

        // Create idle state
        var id = sut.RegisterSession();
        sut.UnregisterSession(id);
        sut.IsIdle.Should().BeTrue();

        using var waitCts = sut.CreateWaitCts(shutdownCts.Token);

        // The token should fire within ~1s due to idle timeout
        var cancellationTask = Task.Run(() =>
        {
            try
            {
                waitCts.Token.WaitHandle.WaitOne(-1);
                return true;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        });

        var timeout = Task.Delay(TimeSpan.FromSeconds(5));
        var completed = await Task.WhenAny(cancellationTask, timeout);
        completed.Should().Be(cancellationTask, "idle timeout should fire within 1s");
    }

    [Fact]
    public void ClearWaitCts_ClearsReference()
    {
        var sut = CreateTracker();
        using var shutdownCts = new CancellationTokenSource();

        sut.RegisterSession();
        using var waitCts = sut.CreateWaitCts(shutdownCts.Token);

        sut.ClearWaitCts();

        // After ClearWaitCts, UnregisterSession should not try to cancel
        // the now-unreferenced CTS (no crash).
        var id = sut.RegisterSession();
        sut.UnregisterSession(id);

        // Verify we can still create new wait CTSes
        using var cts2 = sut.CreateWaitCts(CancellationToken.None);
        cts2.Token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public void Dispose_CancelsIdleAndClearsSessions()
    {
        var sut = CreateTracker();
        sut.RegisterSession();
        sut.RegisterSession();

        sut.Dispose();

        sut.ActiveSessionCount.Should().Be(0);
        sut.IsIdle.Should().BeTrue();
    }

    [Fact]
    public void UnregisterSession_IgnoresUnknownId()
    {
        var sut = CreateTracker();
        sut.RegisterSession();

        var ex = Record.Exception(() => sut.UnregisterSession(Guid.NewGuid()));
        ex.Should().BeNull();
        sut.ActiveSessionCount.Should().Be(1);
    }

    [Fact]
    public void RegisterSession_ReturnsUniqueIds()
    {
        var sut = CreateTracker();
        var id1 = sut.RegisterSession();
        var id2 = sut.RegisterSession();
        id1.Should().NotBe(id2);
    }

    [Fact]
    public void MultipleDispose_DoesNotThrow()
    {
        var sut = CreateTracker();
        sut.Dispose();

        var ex = Record.Exception(() => sut.Dispose());
        ex.Should().BeNull();
    }

    [Fact]
    public void CreateWaitCts_DisposeToken_DoesNotAffectTracker()
    {
        var sut = CreateTracker();
        using var shutdownCts = new CancellationTokenSource();

        sut.RegisterSession();

        // Create and dispose a wait CTS
        var cts = sut.CreateWaitCts(shutdownCts.Token);
        cts.Dispose();
        sut.ClearWaitCts();

        // Create another — should work fine
        using var cts2 = sut.CreateWaitCts(shutdownCts.Token);
        cts2.Token.IsCancellationRequested.Should().BeFalse();
    }
}
