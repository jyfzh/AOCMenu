using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace aoc.Tests.Infrastructure;

/// <summary>
/// Integration tests for the multi-client named pipe server pattern.
///
/// These tests create a lightweight in-process echo server (not the real
/// ZeasnProxy with SDK) to verify the multi-instance pipe architecture:
/// - Multiple clients can connect simultaneously
/// - Each client can send/receive independently
/// - Server shutdown terminates all connections
/// - Client disconnect doesn't affect others
///
/// The echo server uses the same NamedPipeServerStream multi-instance
/// pattern as the real proxy but echoes JSON back instead of calling SDK.
///
/// Each test instance gets a unique pipe name for isolation.
/// </summary>
public sealed class ProxyMultiClientIntegrationTests : IAsyncLifetime
{
    private static int s_instanceCounter;
    private readonly string _pipeName;
    private CancellationTokenSource? _serverCts;
    private Task? _serverTask;

    public ProxyMultiClientIntegrationTests()
    {
        var instance = Interlocked.Increment(ref s_instanceCounter);
        _pipeName = $"aoc-Test-MC-{instance}";
    }

    public async Task InitializeAsync()
    {
        _serverCts = new CancellationTokenSource();
        _serverTask = RunEchoServerAsync(_pipeName, _serverCts);
        await Task.Delay(300); // Wait for server to start
    }

    public async Task DisposeAsync()
    {
        if (_serverCts is not null)
        {
            await _serverCts.CancelAsync();
            if (_serverTask is not null)
            {
                try { await _serverTask.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }
            _serverCts.Dispose();
        }
    }

    // ── Echo server ────────────────────────────────────────────────

    private static async Task RunEchoServerAsync(string pipeName,
        CancellationTokenSource serverCts)
    {
        var ct = serverCts.Token;

        while (!ct.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                maxNumberOfServerInstances: 10,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }

            // Fire-and-forget each session
            _ = Task.Run(async () =>
            {
                try
                {
                    await HandleEchoSessionAsync(pipe, serverCts, ct);
                }
                finally
                {
                    pipe.Dispose();
                }
            }, ct);
        }
    }

    private static async Task HandleEchoSessionAsync(
        NamedPipeServerStream pipe,
        CancellationTokenSource serverCts,
        CancellationToken ct)
    {
        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        string? line;
        while (!ct.IsCancellationRequested && (line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // Shutdown signal — terminates the entire server
                if (root.TryGetProperty("method", out var methodEl)
                    && methodEl.GetString() == "__shutdown__")
                {
                    await writer.WriteLineAsync(
                        """{"jsonrpc":"2.0","result":"shutting down","id":0}""");
                    serverCts.Cancel();
                    return;
                }

                // Echo the request wrapped in a JSON-RPC response
                var id = root.TryGetProperty("id", out var idEl)
                    ? idEl.GetRawText()
                    : "null";
                await writer.WriteLineAsync(
                    $$"""{"jsonrpc":"2.0","result":{"echoed":{{root.GetRawText()}}},"id":{{id}}}""");
            }
            catch (JsonException)
            {
                await writer.WriteLineAsync(
                    """{"jsonrpc":"2.0","error":{"code":-32700,"message":"Parse error"},"id":null}""");
            }
        }
    }

    // ── Test helpers ───────────────────────────────────────────────

    private async Task<JsonElement> SendRequestAsync(string method,
        string? param = null, int timeoutMs = 5000)
    {
        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);

        await pipe.ConnectAsync(timeoutMs);

        var request = param is not null
            ? $$"""{"method":"{{method}}","params":["{{param}}"],"id":1}"""
            : $$"""{"method":"{{method}}","params":[],"id":1}""";
        var requestBytes = Encoding.UTF8.GetBytes(request + "\n");

        await pipe.WriteAsync(requestBytes);
        await pipe.FlushAsync();

        using var reader = new StreamReader(pipe);
        var responseText = await reader.ReadLineAsync();
        responseText.Should().NotBeNull("server should respond");

        using var doc = JsonDocument.Parse(responseText!);
        return doc.RootElement.Clone();
    }

    // ── Tests ──────────────────────────────────────────────────────

    [Fact]
    public async Task SingleClient_CanConnectAndReceiveResponse()
    {
        var response = await SendRequestAsync("ping");
        response.GetProperty("jsonrpc").GetString().Should().Be("2.0");
        response.TryGetProperty("result", out _).Should().BeTrue();
    }

    [Fact]
    public async Task TwoClients_CanConnectSimultaneously()
    {
        var task1 = SendRequestAsync("alpha", "hello");
        var task2 = SendRequestAsync("beta", "world");

        var results = await Task.WhenAll(task1, task2);

        results[0].GetProperty("result").GetProperty("echoed")
            .GetProperty("method").GetString().Should().Be("alpha");
        results[1].GetProperty("result").GetProperty("echoed")
            .GetProperty("method").GetString().Should().Be("beta");
    }

    [Fact]
    public async Task ThreeClients_AllReceiveCorrectResponses()
    {
        var tasks = Enumerable.Range(0, 3)
            .Select(i => SendRequestAsync($"c{i}", $"d{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        for (var i = 0; i < 3; i++)
        {
            results[i].GetProperty("result").GetProperty("echoed")
                .GetProperty("method").GetString().Should().Be($"c{i}");
        }
    }

    [Fact]
    public async Task ClientDisconnect_DoesNotAffectOthers()
    {
        // Client 1: connect, send, disconnect
        using var pipe1 = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe1.ConnectAsync(5000);
        var w1 = new StreamWriter(pipe1) { AutoFlush = true };
        var r1 = new StreamReader(pipe1);

        await w1.WriteLineAsync("""{"method":"first","params":[],"id":1}""");
        var response1 = await r1.ReadLineAsync();
        response1.Should().NotBeNull();
        pipe1.Close();

        // Client 2: should still connect and get a response
        var response2 = await SendRequestAsync("second");
        response2.GetProperty("result").GetProperty("echoed")
            .GetProperty("method").GetString().Should().Be("second");
    }

    [Fact]
    public async Task ShutdownSignal_TerminatesServerForAll()
    {
        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        var w = new StreamWriter(pipe) { AutoFlush = true };
        var r = new StreamReader(pipe);

        await w.WriteLineAsync("""{"method":"__shutdown__","params":[],"id":0}""");
        var response = await r.ReadLineAsync();
        response.Should().NotBeNull();
        using var doc = JsonDocument.Parse(response!);
        doc.RootElement.GetProperty("result").GetString().Should().Be("shutting down");
        pipe.Close();

        // Server should have stopped — new connection attempt should fail
        using var deadPipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => deadPipe.ConnectAsync(2000));
        // Assert.ThrowsAsync rethrows if the exception type doesn't match,
        // so reaching here means the server is indeed shut down.
    }

    [Fact]
    public async Task MalformedJson_GetsErrorResponse()
    {
        using var pipe = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        var w = new StreamWriter(pipe) { AutoFlush = true };
        var r = new StreamReader(pipe);

        await w.WriteLineAsync("not valid json");

        var response = await r.ReadLineAsync();
        response.Should().NotBeNull();
        using var doc = JsonDocument.Parse(response!);
        doc.RootElement.GetProperty("error").GetProperty("code").GetInt32()
            .Should().Be(-32700);
    }

    [Fact]
    public async Task ConcurrentRequests_AllSucceed()
    {
        var tasks = Enumerable.Range(0, 5)
            .Select(i => SendRequestAsync($"s{i}"))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        results.Should().AllSatisfy(r =>
            r.GetProperty("jsonrpc").GetString().Should().Be("2.0"));
    }
}
