using System.Diagnostics;
using System.Text.Json;
using aoc.Application;
using aoc.Domain;
using aoc.Infrastructure;
using aoc.Infrastructure.IPC;

namespace aoc.UI.Services;

/// <summary>
/// Manages the proxy process lifecycle and SDK connection for the UI.
/// Provides async wrappers around the synchronous IAocInvoker interface.
/// Caches the SettingService instance to avoid per-call allocation.
/// </summary>
public sealed class ProxyService : IAsyncDisposable
{
    private ProxyHost? _host;
    private ProxyClientInvoker? _invoker;
    private SettingService? _settingService;
    private bool _disposed;

    public IAocInvoker Invoker => _invoker!;

    public bool IsConnected => _invoker is not null;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Debug.WriteLine("[ProxyService] Connecting...");
        _host = new ProxyHost();
        await _host.StartAsync(TimeSpan.FromSeconds(5), ct);

        _invoker = new ProxyClientInvoker();
        await _invoker.ConnectAsync(TimeSpan.FromSeconds(5), ct);
        _settingService = new SettingService(_invoker);
        Debug.WriteLine("[ProxyService] Connected.");
    }

    public bool TryInitialize(out string? diagnostic)
    {
        if (_invoker is null)
        {
            diagnostic = "Not connected to proxy.";
            Debug.WriteLine("[ProxyService] TryInitialize: not connected.");
            return false;
        }
        Debug.WriteLine("[ProxyService] TryInitialize...");
        var result = _invoker.TryInitialize(out diagnostic);
        Debug.WriteLine($"[ProxyService] TryInitialize result: {result}, diagnostic: {diagnostic}");
        return result;
    }

    public async Task<(bool Success, string? Diagnostic)> TryInitializeAsync()
    {
        if (_invoker is null)
        {
            Debug.WriteLine("[ProxyService] TryInitializeAsync: not connected.");
            return (false, "Not connected to proxy.");
        }
        Debug.WriteLine("[ProxyService] TryInitializeAsync...");
        var result = await _invoker.TryInitializeAsync();
        Debug.WriteLine($"[ProxyService] TryInitializeAsync result: {result}");
        return result;
    }

    public async Task<OperationResult> GetSettingAsync(string key)
    {
        Debug.WriteLine($"[ProxyService] GetSetting: {key}");
        var result = await _settingService!.GetAsync(key);
        Debug.WriteLine($"[ProxyService] GetSetting result: success={result.Success}, value={result.Value}");
        return result;
    }

    public async Task<OperationResult> SetSettingAsync(string key, string value)
    {
        Debug.WriteLine($"[ProxyService] SetSetting: {key} = {value}");
        var result = await _settingService!.SetAsync(key, value);
        Debug.WriteLine($"[ProxyService] SetSetting result: success={result.Success}");
        return result;
    }

    /// <summary>
    /// Fetches monitor identification info by calling SDK's GetDisPlayMessage.
    /// Returns null if not connected or the call fails.
    /// </summary>
    public async Task<MonitorInfo?> GetMonitorInfoAsync()
    {
        if (!IsConnected) return null;

        var result = await _invoker.CallAsync("GetDisPlayMessage");
        if (result is not JsonElement json || json.ValueKind == JsonValueKind.Null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<MonitorInfo>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new JsonException("Unexpected null deserialization");
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[ProxyService] GetMonitorInfoAsync deserialize failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Sends a Shutdown RPC to the ZeasnProxy process (causing it to exit
    /// immediately), then disposes all proxy resources. After this call,
    /// the proxy process will have terminated regardless of its idle timeout.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_disposed) return;

        Debug.WriteLine("[ProxyService] ShutdownAsync: requesting proxy shutdown...");

        // Send Shutdown RPC so the proxy exits immediately
        if (_invoker is ProxyClientInvoker ipcInvoker)
            ipcInvoker.SendShutdown();

        await DisposeAsync();
        Debug.WriteLine("[ProxyService] ShutdownAsync: completed.");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        Debug.WriteLine("[ProxyService] Disposing...");
        if (_invoker is not null)
            await _invoker.DisposeAsync();

        if (_host is not null)
            await _host.DisposeAsync();
        Debug.WriteLine("[ProxyService] Disposed.");
    }
}
