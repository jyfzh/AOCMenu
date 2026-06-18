using System.Collections.Concurrent;
using System.Reflection;

namespace aoc.Infrastructure;

public sealed class MethodResolver
{
    // Composite key: (TypeHandle, methodName, parameterSignature)
    // Uses TypeHandle.Value (IntPtr, unique per type in the process lifetime)
    // instead of the very long AssemblyQualifiedName.
    private readonly ConcurrentDictionary<(long TypeId, string Method, string Signature), MethodInfo?> _cache = new();

    public int CacheCount => _cache.Count;

    public MethodInfo? Resolve(Type targetType, string method, Type[] parameterTypes)
    {
        var typeId = targetType.TypeHandle.Value.ToInt64();
        var sig = BuildSignature(parameterTypes);
        var key = (typeId, method, sig);

        return _cache.GetOrAdd(key, _ =>
            targetType.GetMethod(
                method,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: parameterTypes,
                modifiers: null));
    }

    private static string BuildSignature(Type[] types)
    {
        if (types.Length == 0) return "";
        if (types.Length == 1) return types[0].Name;

        // For 2+ params, use comma-separated short names (cheaper than FullName)
        var parts = new string[types.Length];
        for (var i = 0; i < types.Length; i++)
            parts[i] = types[i].Name;
        return string.Join(",", parts);
    }
}
