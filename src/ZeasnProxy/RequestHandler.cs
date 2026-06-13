namespace aoc.SdkProxy;

/// <summary>
/// JSON-RPC request handlers for the ZeasnProxy pipe server.
/// Each handler produces a JSON-RPC response string for a given request type.
/// </summary>
static class RequestHandler
{
    // ── TryInitialize ──────────────────────────────────────────────

    public static string HandleTryInitialize(AocSdkInvoker invoker, JsonElement id)
    {
        var ok = invoker.TryInitialize(out var diagnostic);
        ProxyState.Initialized = ok;

        using var w = new ResultWriter();
        w.BeginResult();
        w.WriteBool("success", ok);
        w.WriteString("diagnostic", diagnostic);
        w.EndResult(id);
        return w.ToStringAndClear();
    }

    public static string FastInitResult(JsonElement id)
    {
        using var w = new ResultWriter();
        w.BeginResult();
        w.WriteBool("success", true);
        w.WriteString("diagnostic", null);
        w.EndResult(id);
        return w.ToStringAndClear();
    }

    // ── Ok ─────────────────────────────────────────────────────────

    public static string HandleOk(AocSdkInvoker invoker, JsonElement? @params, JsonElement id)
    {
        var args = DeserializeParams(@params);
        if (args is null || args.Length < 1 || args[0] is not string method)
            return ErrorResponse(-32602, "Invalid params: expected [method, ...args]", id);

        var rest = args.AsSpan(1);
        var success = invoker.Ok(method, rest.ToArray());

        using var w = new ResultWriter();
        w.BeginResult();
        w.WriteBool("success", success);
        w.WriteString("diagnostic", invoker.LastDiagnostic);
        w.EndResult(id);
        return w.ToStringAndClear();
    }

    // ── Call ───────────────────────────────────────────────────────

    public static string HandleCall(AocSdkInvoker invoker, JsonElement? @params, JsonElement id)
    {
        var args = DeserializeParams(@params);
        if (args is null || args.Length < 1 || args[0] is not string method)
            return ErrorResponse(-32602, "Invalid params: expected [method, ...args]", id);

        var rest = args.AsSpan(1);
        var result = invoker.Call(method, rest.ToArray());

        if (result is null)
            return $"{{\"jsonrpc\":\"2.0\",\"result\":null,\"id\":{SerializeToken(id)}}}";

        using var w = new ResultWriter();
        w.BeginResult();
        FastSerializer.Serialize(result, w);
        w.EndResult(id);
        return w.ToStringAndClear();
    }

    // ── Parameter deserialization ──────────────────────────────────

    public static object?[]? DeserializeParams(JsonElement? @params)
    {
        if (@params is null) return [];
        if (@params.Value.ValueKind != JsonValueKind.Array) return [];

        var count = @params.Value.GetArrayLength();
        if (count == 0) return [];

        var result = new object?[count];
        var i = 0;
        foreach (var e in @params.Value.EnumerateArray())
        {
            result[i++] = e.ValueKind switch
            {
                JsonValueKind.String => e.GetString(),
                JsonValueKind.Number => e.TryGetInt32(out var iv) ? (object)iv : e.GetRawText(),
                JsonValueKind.True => (object)true,
                JsonValueKind.False => (object)false,
                JsonValueKind.Null => null,
                _ => e.GetRawText()
            };
        }
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────

    public static string SerializeToken(JsonElement token)
    {
        if (token.ValueKind == JsonValueKind.Undefined) return "null";
        return token.GetRawText();
    }

    public static string ErrorResponse(int code, string message, JsonElement id)
    {
        using var w = new ResultWriter();
        w.BeginError();
        w.Writer.WriteNumber("code", code);
        w.Writer.WriteString("message", message);
        w.Writer.WriteEndObject();
        w.WriteId(id);
        w.Writer.WriteEndObject();
        w.Writer.Flush();
        return w.ToStringAndClear();
    }
}
