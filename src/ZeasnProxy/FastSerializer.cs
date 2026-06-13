using System.Collections.Concurrent;
using System.Reflection;


namespace aoc.SdkProxy;

/// <summary>Cached-reflection-based JSON serializer for SDK result objects.</summary>
static class FastSerializer
{
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> s_memberCache = new();

    /// <summary>
    /// Write properties/fields of an object as key-value pairs into the current JSON context.
    /// Does NOT wrap in {} — the caller (HandleCall via BeginResult) already opened the result object.
    /// For recursive nested objects, WriteValue wraps each nested object explicitly.
    /// </summary>
    public static void Serialize(object obj, ResultWriter w)
    {
        var type = obj.GetType();
        var members = s_memberCache.GetOrAdd(type, static t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                          .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                          .Cast<MemberInfo>();
            var fields = t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                          .Cast<MemberInfo>();
            return props.Concat(fields).ToArray();
        });

        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            object? val;
            string name;
            if (member is PropertyInfo prop)
            {
                val = prop.GetValue(obj);
                name = prop.Name;
            }
            else if (member is FieldInfo field)
            {
                val = field.GetValue(obj);
                name = field.Name;
            }
            else continue;

            w._writer.WritePropertyName(name);
            WriteValue(val, w);
        }
    }

    private static void WriteValue(object? val, ResultWriter w)
    {
        if (val is null)
        {
            w._writer.WriteNullValue();
            return;
        }

        if (val is string s) { w._writer.WriteStringValue(s); return; }
        if (val is int i) { w._writer.WriteNumberValue(i); return; }
        if (val is long l) { w._writer.WriteNumberValue(l); return; }
        if (val is bool b) { w._writer.WriteBooleanValue(b); return; }
        if (val is double d) { w._writer.WriteNumberValue(d); return; }
        if (val is float f) { w._writer.WriteNumberValue(f); return; }
        if (val is decimal dec) { w._writer.WriteNumberValue(dec); return; }
        if (val is uint ui) { w._writer.WriteNumberValue(ui); return; }
        if (val is short sh) { w._writer.WriteNumberValue(sh); return; }
        if (val is byte by) { w._writer.WriteNumberValue(by); return; }
        if (val is sbyte sb) { w._writer.WriteNumberValue(sb); return; }
        if (val is ushort us) { w._writer.WriteNumberValue(us); return; }
        if (val is Enum) { w._writer.WriteNumberValue(Convert.ToInt32(val)); return; }

        // Nested object — wrap in {}
        w._writer.WriteStartObject();
        Serialize(val, w);
        w._writer.WriteEndObject();
    }
}
