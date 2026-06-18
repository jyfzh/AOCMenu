using aoc.Infrastructure;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Infrastructure;

public class MethodResolverTests
{
    private sealed class DummyTarget
    {
        public string Echo(string text) => text;
        public int Add(int a, int b) => a + b;
    }

    [Fact]
    public void Resolve_SameSignature_ReturnsSameMethodInfoInstance()
    {
        var resolver = new MethodResolver();

        var first = resolver.Resolve(typeof(DummyTarget), "Echo", new[] { typeof(string) });
        var second = resolver.Resolve(typeof(DummyTarget), "Echo", new[] { typeof(string) });

        first.Should().NotBeNull();
        second.Should().BeSameAs(first);
    }

    [Fact]
    public void Resolve_MissingMethod_IsCachedAsNull()
    {
        var resolver = new MethodResolver();

        var first = resolver.Resolve(typeof(DummyTarget), "Missing", new[] { typeof(string) });
        var second = resolver.Resolve(typeof(DummyTarget), "Missing", new[] { typeof(string) });

        first.Should().BeNull();
        second.Should().BeNull();
        resolver.CacheCount.Should().Be(1);
    }
}
