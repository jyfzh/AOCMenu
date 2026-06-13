using System.Buffers;
using System.Text;
using System.Text.Json;

namespace aoc.SdkProxy;

/// <summary>
/// Pooled Utf8JsonWriter wrapper that avoids per-call allocations.
/// Uses ArrayBufferWriter so all JSON bytes stream into a growable buffer.
/// After ToStringAndClear(), do not reuse — the buffer is cleared.
/// </summary>
ref struct ResultWriter
{
    internal ArrayBufferWriter<byte> _buffer;
    internal Utf8JsonWriter _writer;
    public Utf8JsonWriter Writer => _writer;

    /// <summary>Start a JSON-RPC result envelope: {"jsonrpc":"2.0","result":{...</summary>
    public void BeginResult()
    {
        this = default;
        _buffer = new ArrayBufferWriter<byte>(256);
        _writer = new Utf8JsonWriter(_buffer);
        _writer.WriteStartObject();
        _writer.WriteString("jsonrpc", "2.0");
        _writer.WriteStartObject("result");
    }

    /// <summary>Start a JSON-RPC error envelope: {"jsonrpc":"2.0","error":{...</summary>
    public void BeginError()
    {
        this = default;
        _buffer = new ArrayBufferWriter<byte>(256);
        _writer = new Utf8JsonWriter(_buffer);
        _writer.WriteStartObject();
        _writer.WriteString("jsonrpc", "2.0");
        _writer.WriteStartObject("error");
    }

    public void WriteBool(string name, bool value) => _writer.WriteBoolean(name, value);

    public void WriteString(string name, string? value)
    {
        if (value is null) _writer.WriteNull(name);
        else _writer.WriteString(name, value);
    }

    /// <summary>Close result object, add id, close root.</summary>
    public void EndResult(JsonElement idToken)
    {
        _writer.WriteEndObject(); // result
        WriteId(idToken);
        _writer.WriteEndObject(); // root
        _writer.Flush();
    }

    /// <summary>Write the "id" field preserving the original JSON type (number, string, etc.).</summary>
    public void WriteId(JsonElement idToken)
    {
        if (idToken.ValueKind == JsonValueKind.Undefined)
        {
            _writer.WriteNull("id");
        }
        else
        {
            _writer.WritePropertyName("id");
            _writer.WriteRawValue(idToken.GetRawText());
        }
    }

    public readonly string ToStringAndClear()
    {
        var result = Encoding.UTF8.GetString(_buffer.WrittenSpan);
        return result;
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _buffer?.Clear();
    }
}
