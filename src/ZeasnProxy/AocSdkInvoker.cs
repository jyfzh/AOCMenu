using System.Collections.Concurrent;
using System.Reflection;
using aoc.Infrastructure;

namespace aoc.SdkProxy;

public sealed class AocSdkInvoker
{
    private readonly Type _aocType;
    private readonly object _aoc;
    private readonly MethodResolver _resolver;

    // ── Synchronization for thread-safe access from multiple pipe sessions ─
    private readonly SemaphoreSlim _invokeLock = new(1, 1);

    // ── Cached err_code PropertyInfo per result type ─────────────────
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> s_errCodeCache = new();

    public string? LastDiagnostic { get; private set; }

    private AocSdkInvoker(Type aocType, object aoc, MethodResolver resolver)
    {
        _aocType = aocType;
        _aoc = aoc;
        _resolver = resolver;
    }

    public static AocSdkInvoker CreateDefault(string binDir)
    {
        var loaded = new Dictionary<string, Assembly>();
        foreach (var dll in Directory.GetFiles(binDir, "Zeasn.*.dll"))
        {
            try
            {
                var asm = Assembly.LoadFrom(dll);
                loaded[asm.GetName().Name!] = asm;
            }
            catch { }
        }

        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name).Name!;
            if (loaded.TryGetValue(name, out var asm)) return asm;
            var path = Path.Combine(binDir, name + ".dll");
            if (!File.Exists(path)) return null;
            var loadedAsm = Assembly.LoadFrom(path);
            loaded[name] = loadedAsm;
            return loadedAsm;
        };

        var aocType = loaded["Zeasn.Equipment.Base.Lib"].GetType("Zeasn.Equipment.Base.Lib.AOCOper")
            ?? throw new InvalidOperationException("AOCOper type not found");

        var aoc = Activator.CreateInstance(aocType, nonPublic: true)
            ?? throw new InvalidOperationException("AOCOper instance create failed");

        return new AocSdkInvoker(aocType, aoc, new MethodResolver());
    }

    public bool TryInitialize(out string? diagnostic)
    {
        var ok = Ok("DisPlayIni");
        diagnostic = ok ? null : (LastDiagnostic ?? "DisPlayIni returned error");
        return ok;
    }

    /// <summary>
    /// Thread-safe SDK method invocation. Acquires _invokeLock to serialize
    /// concurrent access from multiple pipe session handlers to the single
    /// AOCOper instance and to LastDiagnostic state.
    /// </summary>
    public object? Call(string method, params object?[] args)
    {
        _invokeLock.Wait();
        try
        {
            return CallCore(method, args);
        }
        finally
        {
            _invokeLock.Release();
        }
    }

    /// <summary>
    /// Thread-safe SDK Ok() invocation. Acquires _invokeLock once for the
    /// entire invoke + err_code check, avoiding a separate lock acquisition
    /// inside Call() (which CallCore bypasses).
    /// </summary>
    public bool Ok(string method, params object?[] args)
    {
        _invokeLock.Wait();
        try
        {
            var result = CallCore(method, args);
            if (result is null) return false;

            // Cache the err_code property lookup per result type
            var resultType = result.GetType();
            var errCodeProp = s_errCodeCache.GetOrAdd(resultType, static t =>
                t.GetProperty("err_code", BindingFlags.Public | BindingFlags.Instance));

            if (errCodeProp is null) return false;

            var errCode = errCodeProp.GetValue(result)?.ToString();
            if (errCode == "0") return true;

            LastDiagnostic = $"{method} returned err_code={errCode}";
            return false;
        }
        finally
        {
            _invokeLock.Release();
        }
    }

    /// <summary>
    /// Core invoke logic — resolves the method and invokes it on the SDK object.
    /// MUST be called under _invokeLock. Shared by Call() and Ok() to avoid
    /// reentrancy deadlock (Ok() does NOT call Call() anymore).
    /// </summary>
    private object? CallCore(string method, object?[] args)
    {
        LastDiagnostic = null;
        try
        {
            var parameterTypes = args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
            var targetMethod = _resolver.Resolve(_aocType, method, parameterTypes);
            if (targetMethod is null)
            {
                LastDiagnostic = $"method not found: {method}({string.Join(",", parameterTypes.Select(t => t.Name))})";
                return null;
            }
            return targetMethod.Invoke(_aoc, args);
        }
        catch (TargetInvocationException tex)
        {
            var inner = tex.InnerException ?? tex;
            LastDiagnostic = $"{method} invoke failed: {inner.GetType().Name}: {inner.Message}";
            return null;
        }
        catch (Exception ex)
        {
            LastDiagnostic = $"{method} invoke failed: {ex.GetType().Name}: {ex.Message}";
            return null;
        }
    }
}
