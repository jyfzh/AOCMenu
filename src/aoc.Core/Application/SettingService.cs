using System.Collections.Concurrent;
using System.Reflection;
using aoc.Domain;
using aoc.Infrastructure;

namespace aoc.Application;

public sealed class SettingService
{
    private readonly IAocInvoker _invoker;

    // ── Reflection cache for non-proxy path ─────────────────────────
    private static readonly ConcurrentDictionary<Type, FrozenDictionary<string, PropertyInfo>> s_propertyCache = new();

    public SettingService(IAocInvoker invoker)
    {
        _invoker = invoker;
    }

    public OperationResult Set(string key, string value)
    {
        if (!SettingCatalog.TryGet(key, out var def) || def is null)
            return OperationResult.Fail(ErrorKind.InvalidArgument, $"❌ 未知设置 '{key}'。使用 --help 查看可用设置。",
                "unknown setting key");

        // ── 前置条件检查：受限于依赖设置的状态 ──
        if (def.PrerequisiteSetting is not null && def.PrerequisiteValueSet is { Count: >0 })
        {
            var prereqResult = Get(def.PrerequisiteSetting);
            if (prereqResult.Success && prereqResult.Value is not null
                && !def.PrerequisiteValueSet.Contains(prereqResult.Value))
            {
                var prereqDef = SettingCatalog.All[def.PrerequisiteSetting];
                var prereqDisplayValues = FormatPrerequisiteValues(def.PrerequisiteSetting, def.PrerequisiteValueSet);

                return OperationResult.Fail(
                    ErrorKind.PrerequisiteNotMet,
                    $"❌ {def.Description} 当前状态无法调整。需要 {prereqDef.Description} 为 {prereqDisplayValues}。",
                    $"prerequisite not met: {def.PrerequisiteSetting}={prereqResult.Value}");
            }
        }

        var effective = value;
        if (def.EnumMap is not null)
        {
            // Try exact match → lowercase → case-insensitive scan.
            // Case-insensitive fallback handles mixed-case EnumMap keys
            // such as "DisplayHDR" which wouldn't match after ToLowerInvariant().
            if (!def.EnumMap.TryGetValue(value, out var mapped) &&
                !def.EnumMap.TryGetValue(value.ToLowerInvariant(), out mapped))
            {
                mapped = def.EnumMap
                    .FirstOrDefault(kv => string.Equals(kv.Key, value, StringComparison.OrdinalIgnoreCase))
                    .Value;
            }

            if (mapped is not null)
            {
                effective = mapped;
            }
            else
            {
                // ── Fallback: raw SDK value path ──
                var reverseMap = SettingCatalog.GetReverseMap(key);
                if (reverseMap is not null && reverseMap.TryGetValue(value.ToLowerInvariant(), out _))
                {
                    effective = value.ToLowerInvariant();
                }
                else
                {
                    var choices = SettingCatalog.GetEnumDisplayValues(key) ?? string.Join(", ", def.EnumMap.Keys.OrderBy(x => x));
                    return OperationResult.Fail(
                        ErrorKind.InvalidArgument,
                        $"❌ 设置 '{def.Name}' 不接受值 '{value}'。可用值: {choices}",
                        "enum value invalid");
                }
            }
        }

        bool ok;
        if (def.ExtraArg is not null)
        {
            ok = _invoker.Ok(def.Method, def.ExtraArg, effective);
        }
        else
        {
            ok = _invoker.Ok(def.Method, effective);
        }

        return ok
            ? OperationResult.Ok($"{def.Description} → {value}... ✅")
            : OperationResult.Fail(ErrorKind.InvocationFailed, $"{def.Description} → {value}... ❌", _invoker.LastDiagnostic);
    }

    public OperationResult Get(string key)
    {
        if (!SettingCatalog.TryGet(key, out var def) || def is null)
            return OperationResult.Fail(ErrorKind.InvalidArgument, $"❌ 未知设置 '{key}'。使用 --help 查看可用设置。",
                "unknown setting key");

        var (raw, max) = ReadSetting(def);
        if (raw is null)
        {
            var diag = _invoker.LastDiagnostic;
            var msg = diag is not null
                ? $"❌ {def.Description}: 不支持查询\n   代理诊断: {diag}"
                : $"❌ {def.Description}: 不支持查询";
            return OperationResult.Fail(ErrorKind.NotSupported, msg, diag ?? "not supported");
        }

        if (def.EnumMap is not null)
        {
            var reverseMap = SettingCatalog.GetReverseMap(key);
            string? displayName = null;

            if (reverseMap is not null)
            {
                if (!reverseMap.TryGetValue(raw, out displayName))
                {
                    if (int.TryParse(raw, out var iv))
                        reverseMap.TryGetValue(iv.ToString(), out displayName);
                }
            }

            if (displayName is not null)
            {
                var suffix = string.Equals(displayName, raw, StringComparison.Ordinal) ? "" : $" ({raw})";
                return OperationResult.Ok($"{def.Description}: {displayName}{suffix}", raw, max);
            }
        }

        return OperationResult.Ok($"{def.Description}: {raw}", raw, max);
    }

    // ════════════════════════════════════════════════════════════════
    //  Async public methods — UI path (avoids Task.Run / blocked threads)
    // ════════════════════════════════════════════════════════════════

    public async Task<OperationResult> SetAsync(string key, string value)
    {
        if (!SettingCatalog.TryGet(key, out var def) || def is null)
            return OperationResult.Fail(ErrorKind.InvalidArgument, $"❌ 未知设置 '{key}'。使用 --help 查看可用设置。",
                "unknown setting key");

        // ── 前置条件检查 ──
        if (def.PrerequisiteSetting is not null && def.PrerequisiteValueSet is { Count: >0 })
        {
            var prereqResult = await GetAsync(def.PrerequisiteSetting).ConfigureAwait(false);
            if (prereqResult.Success && prereqResult.Value is not null
                && !def.PrerequisiteValueSet.Contains(prereqResult.Value))
            {
                var prereqDef = SettingCatalog.All[def.PrerequisiteSetting];
                var prereqDisplayValues = FormatPrerequisiteValues(def.PrerequisiteSetting, def.PrerequisiteValueSet);

                return OperationResult.Fail(
                    ErrorKind.PrerequisiteNotMet,
                    $"❌ {def.Description} 当前状态无法调整。需要 {prereqDef.Description} 为 {prereqDisplayValues}。",
                    $"prerequisite not met: {def.PrerequisiteSetting}={prereqResult.Value}");
            }
        }

        // ── Enum 映射 ──
        var effective = value;
        if (def.EnumMap is not null)
        {
            if (!def.EnumMap.TryGetValue(value, out var mapped) &&
                !def.EnumMap.TryGetValue(value.ToLowerInvariant(), out mapped))
            {
                mapped = def.EnumMap
                    .FirstOrDefault(kv => string.Equals(kv.Key, value, StringComparison.OrdinalIgnoreCase))
                    .Value;
            }

            if (mapped is not null)
            {
                effective = mapped;
            }
            else
            {
                var reverseMap = SettingCatalog.GetReverseMap(key);
                if (reverseMap is not null && reverseMap.TryGetValue(value.ToLowerInvariant(), out _))
                {
                    effective = value.ToLowerInvariant();
                }
                else
                {
                    var choices = SettingCatalog.GetEnumDisplayValues(key)
                        ?? string.Join(", ", def.EnumMap.Keys.OrderBy(x => x));
                    return OperationResult.Fail(
                        ErrorKind.InvalidArgument,
                        $"❌ 设置 '{def.Name}' 不接受值 '{value}'。可用值: {choices}",
                        "enum value invalid");
                }
            }
        }

        // ── SDK invoke ──
        bool ok = def.ExtraArg is not null
            ? await _invoker.OkAsync(def.Method, def.ExtraArg, effective).ConfigureAwait(false)
            : await _invoker.OkAsync(def.Method, effective).ConfigureAwait(false);

        return ok
            ? OperationResult.Ok($"{def.Description} → {value}... ✅")
            : OperationResult.Fail(ErrorKind.InvocationFailed, $"{def.Description} → {value}... ❌", _invoker.LastDiagnostic);
    }

    public async Task<OperationResult> GetAsync(string key)
    {
        if (!SettingCatalog.TryGet(key, out var def) || def is null)
            return OperationResult.Fail(ErrorKind.InvalidArgument, $"❌ 未知设置 '{key}'。使用 --help 查看可用设置。",
                "unknown setting key");

        var (raw, max) = await ReadSettingAsync(def).ConfigureAwait(false);
        if (raw is null)
        {
            var diag = _invoker.LastDiagnostic;
            var msg = diag is not null
                ? $"❌ {def.Description}: 不支持查询\n   代理诊断: {diag}"
                : $"❌ {def.Description}: 不支持查询";
            return OperationResult.Fail(ErrorKind.NotSupported, msg, diag ?? "not supported");
        }

        if (def.EnumMap is not null)
        {
            var reverseMap = SettingCatalog.GetReverseMap(key);
            string? displayName = null;

            if (reverseMap is not null)
            {
                if (!reverseMap.TryGetValue(raw, out displayName))
                {
                    if (int.TryParse(raw, out var iv))
                        reverseMap.TryGetValue(iv.ToString(), out displayName);
                }
            }

            if (displayName is not null)
            {
                var suffix = string.Equals(displayName, raw, StringComparison.Ordinal) ? "" : $" ({raw})";
                return OperationResult.Ok($"{def.Description}: {displayName}{suffix}", raw, max);
            }
        }

        return OperationResult.Ok($"{def.Description}: {raw}", raw, max);
    }

    // ════════════════════════════════════════════════════════════════
    //  ReadSetting — 三策略读取流水线 (sync for CLI)
    // ════════════════════════════════════════════════════════════════

    private (string? value, int? maxValue) ReadSetting(SettingDef def)
    {
        // 策略 1: 专用 Getter 方法（如 GetHDR）
        if (def.Getter is not null)
            return ReadAttributeInfo(_invoker.Call(def.Getter));

        // 策略 2: ReadProperty — 从 GetDisPlay / GetE2A0Profile 返回值中导航
        if (def.ReadProperty is not null)
            return ReadFromProfile(def);

        // 策略 3: ReadDeviceProperty — 尝试多个 SDK 方法
        if (def.ReadDeviceProperty is not null)
            return ReadFromDeviceProperty(def);

        return (null, null);
    }

    /// <summary>
    /// 从 GetDisPlay / GetE2A0Profile 返回的 JSON/对象中按路径读取。
    /// 路径 A: Tag.DisPlay.{path} 或 Tag.{path} (E2A0)
    /// 路径 B: CurrItem.{path}（回退）
    /// 先尝试 JSON 解析，失败则回退到反射。
    /// </summary>
    private (string? value, int? maxValue) ReadFromProfile(SettingDef def)
    {
        var getterName = def.UseE2A0Profile ? "GetE2A0Profile" : "GetDisPlay";
        var display = _invoker.Call(getterName);
        if (display is null) return (null, null);

        if (display is JsonElement json)
            return TryReadJsonProfile(json, def);

        return TryReadReflectionProfile(display, def);
    }

    private static (string? value, int? maxValue) TryReadJsonProfile(JsonElement json, SettingDef def)
    {
        if (json.ValueKind != JsonValueKind.Object) return (null, null);

        // 路径 A: Tag / Tag.DisPlay
        if (json.TryGetProperty("Tag", out var tag) && tag.ValueKind == JsonValueKind.Object)
        {
            var target = def.UseE2A0Profile ? tag : (tag.TryGetProperty("DisPlay", out var d) ? d : default);
            if (target.ValueKind == JsonValueKind.Object)
            {
                var result = ResolveJsonPath(target, def.ReadProperty!);
                if (result.value is not null) return result;
            }
        }

        // 路径 B: CurrItem.{path}
        if (json.TryGetProperty("CurrItem", out var curr) && curr.ValueKind == JsonValueKind.Object)
        {
            var result = ResolveJsonPath(curr, def.ReadProperty!);
            if (result.value is not null) return result;
        }

        return (null, null);
    }

    private (string? value, int? maxValue) TryReadReflectionProfile(object display, SettingDef def)
    {
        var typeCache = GetCachedProperties(display.GetType());

        if (!typeCache.TryGetValue("Tag", out var tagProp)) return (null, null);
        var tagVal = tagProp.GetValue(display);
        if (tagVal is null) return (null, null);

        if (def.UseE2A0Profile)
            return ResolveReflectionPath(tagVal, def.ReadProperty!);

        var tagCache = GetCachedProperties(tagVal.GetType());
        if (!tagCache.TryGetValue("DisPlay", out var dispProp)) return (null, null);
        var dispVal = dispProp.GetValue(tagVal);
        if (dispVal is null) return (null, null);

        return ResolveReflectionPath(dispVal, def.ReadProperty!);
    }

    /// <summary>
    /// 设备属性读取。尝试多个 SDK 方法（GetGameMode、GetGameModeData、GetDisPlay）
    /// 及其子路径（Tag、CurrItem），返回第一个成功的 AttributeInfo。
    /// </summary>
    private (string? value, int? maxValue) ReadFromDeviceProperty(SettingDef def)
    {
        var propPath = def.ReadDeviceProperty!;
        var segments = propPath.Split('.');
        var paths = segments.Length > 1
            ? new[] { propPath, segments[^1] }
            : new[] { propPath };

        // 3a: GetGameMode()
        (string? value, int? maxValue)? result = TryReadJsonResult(_invoker.Call("GetGameMode"), paths);
        if (result is not null) return result.Value;

        // 3b: GetGameModeData(false)
        result = TryReadJsonResult(_invoker.Call("GetGameModeData", false), paths);
        if (result is not null) return result.Value;

        // 3c: 从 GetDisPlay() 的 Tag / CurrItem / DisPlay 子路径尝试
        var dp = _invoker.Call("GetDisPlay");
        if (dp is JsonElement dpJson && dpJson.ValueKind == JsonValueKind.Object)
        {
            if (dpJson.TryGetProperty("Tag", out var tag) && tag.ValueKind == JsonValueKind.Object)
            {
                result = TryReadPathsFromContainer(tag, paths);
                if (result is not null) return result.Value;

                if (tag.TryGetProperty("DisPlay", out var disp) && disp.ValueKind == JsonValueKind.Object)
                {
                    result = TryReadPathsFromContainer(disp, paths);
                    if (result is not null) return result.Value;
                }
            }

            if (dpJson.TryGetProperty("CurrItem", out var ci) && ci.ValueKind == JsonValueKind.Object)
            {
                result = TryReadPathsFromContainer(ci, paths);
                if (result is not null) return result.Value;
            }
        }

        return (null, null);
    }

    // ════════════════════════════════════════════════════════════════
    //  ReadSetting — 三策略读取流水线 (async for UI)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Async variant of ReadSetting. Uses the same shared helper methods
    /// (TryReadJsonProfile, ResolveJsonPath, ReadAttributeInfo, etc.)
    /// since those are pure functions with no invoker calls.
    /// </summary>
    private async Task<(string? value, int? maxValue)> ReadSettingAsync(SettingDef def)
    {
        // 策略 1: 专用 Getter 方法（如 GetHDR）
        if (def.Getter is not null)
            return ReadAttributeInfo(await _invoker.CallAsync(def.Getter).ConfigureAwait(false));

        // 策略 2: ReadProperty — 从 GetDisPlay / GetE2A0Profile 返回值中导航
        if (def.ReadProperty is not null)
            return await ReadFromProfileAsync(def).ConfigureAwait(false);

        // 策略 3: ReadDeviceProperty — 尝试多个 SDK 方法
        if (def.ReadDeviceProperty is not null)
            return await ReadFromDevicePropertyAsync(def).ConfigureAwait(false);

        return (null, null);
    }

    private async Task<(string? value, int? maxValue)> ReadFromProfileAsync(SettingDef def)
    {
        var getterName = def.UseE2A0Profile ? "GetE2A0Profile" : "GetDisPlay";
        var display = await _invoker.CallAsync(getterName).ConfigureAwait(false);
        if (display is not JsonElement json || json.ValueKind != JsonValueKind.Object)
            return (null, null);

        return TryReadJsonProfile(json, def);
    }

    private async Task<(string? value, int? maxValue)> ReadFromDevicePropertyAsync(SettingDef def)
    {
        var propPath = def.ReadDeviceProperty!;
        var segments = propPath.Split('.');
        var paths = segments.Length > 1
            ? new[] { propPath, segments[^1] }
            : new[] { propPath };

        // 3a: GetGameMode()
        (string? value, int? maxValue)? result = TryReadJsonResult(
            await _invoker.CallAsync("GetGameMode").ConfigureAwait(false), paths);
        if (result is not null) return result.Value;

        // 3b: GetGameModeData(false)
        result = TryReadJsonResult(
            await _invoker.CallAsync("GetGameModeData", false).ConfigureAwait(false), paths);
        if (result is not null) return result.Value;

        // 3c: 从 GetDisPlay() 的 Tag / CurrItem / DisPlay 子路径尝试
        var dp = await _invoker.CallAsync("GetDisPlay").ConfigureAwait(false);
        if (dp is JsonElement dpJson && dpJson.ValueKind == JsonValueKind.Object)
        {
            if (dpJson.TryGetProperty("Tag", out var tag) && tag.ValueKind == JsonValueKind.Object)
            {
                result = TryReadPathsFromContainer(tag, paths);
                if (result is not null) return result.Value;

                if (tag.TryGetProperty("DisPlay", out var disp) && disp.ValueKind == JsonValueKind.Object)
                {
                    result = TryReadPathsFromContainer(disp, paths);
                    if (result is not null) return result.Value;
                }
            }

            if (dpJson.TryGetProperty("CurrItem", out var ci) && ci.ValueKind == JsonValueKind.Object)
            {
                result = TryReadPathsFromContainer(ci, paths);
                if (result is not null) return result.Value;
            }
        }

        return (null, null);
    }

    // ════════════════════════════════════════════════════════════════
    //  JSON 提取
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 从 JsonResult（含 Tag/CurrItem 顶层属性）中提取 AttributeInfo，
    /// 依次尝试多条路径。返回 null 表示未找到。
    /// </summary>
    private static (string? value, int? maxValue)? TryReadJsonResult(object? source, ReadOnlySpan<string> paths)
    {
        if (source is not JsonElement json || json.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in new[] { "Tag", "CurrItem" })
        {
            if (json.TryGetProperty(key, out var container) && container.ValueKind == JsonValueKind.Object)
            {
                var result = TryReadPathsFromContainer(container, paths);
                if (result is not null) return result;
            }
        }

        return null;
    }

    /// <summary>
    /// 在一个容器（Tag / CurrItem / DisPlay）中依次尝试：
    /// 1. 容器本身作为 AttributeInfo
    /// 2. 每条路径导航到叶子
    /// </summary>
    private static (string? value, int? maxValue)? TryReadPathsFromContainer(JsonElement container, ReadOnlySpan<string> paths)
    {
        // 先尝试容器本身作为 AttributeInfo
        var direct = ReadAttributeInfo(container);
        if (direct.value is not null) return direct;

        // 再尝试每条路径
        foreach (var path in paths)
        {
            var r = ResolveJsonPath(container, path);
            if (r.value is not null) return r;
        }

        return null;
    }

    // ════════════════════════════════════════════════════════════════
    //  JSON / Reflection 路径导航
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// 沿点分隔的 JSON 属性路径导航，在叶子节点提取 AttributeInfo。
    /// 例如 "GameModeInfo.GameColor" → root["GameModeInfo"]["GameColor"] → {Value, MaxValue}。
    /// </summary>
    private static (string? value, int? maxValue) ResolveJsonPath(JsonElement root, string path)
    {
        var segments = path.Split('.');
        var current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return (null, null);
            if (!current.TryGetProperty(segments[i], out current))
                return (null, null);
        }
        return ReadAttributeInfo(current);
    }

    /// <summary>
    /// 沿点分隔的属性路径通过反射导航，在叶子节点提取 AttributeInfo。
    /// 用于非 Proxy 模式（直接 SDK 访问）。
    /// </summary>
    private static (string? value, int? maxValue) ResolveReflectionPath(object root, string path)
    {
        var segments = path.Split('.');
        object? current = root;
        for (var i = 0; i < segments.Length; i++)
        {
            if (current is null) return (null, null);
            var cache = GetCachedProperties(current.GetType());
            if (!cache.TryGetValue(segments[i], out var prop)) return (null, null);
            current = prop.GetValue(current);
        }
        return ReadAttributeInfo(current);
    }

    /// <summary>
    /// 从任意对象中提取 AttributeInfo（Value + MaxValue）。
    /// 支持 JsonElement（Object → 属性提取，原始值 → ValueToString）和 POCO（反射）。
    /// </summary>
    private static (string? value, int? maxValue) ReadAttributeInfo(object? attribute)
    {
        if (attribute is JsonElement json)
            return ReadAttributeInfoFromJson(json);

        if (attribute is null) return (null, null);

        // POCO 反射路径
        var cache = GetCachedProperties(attribute.GetType());
        var valStr = cache.TryGetValue("Value", out var vProp) ? vProp.GetValue(attribute)?.ToString() : null;
        var maxRawStr = cache.TryGetValue("MaxValue", out var mProp) ? mProp.GetValue(attribute)?.ToString() : null;
        var maxVal = int.TryParse(maxRawStr, out var parsedVal) ? (int?)parsedVal : null;

        return (valStr, maxVal);
    }

    private static (string? value, int? maxValue) ReadAttributeInfoFromJson(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            var value = json.TryGetProperty("Value", out var v) ? ValueToString(v) : null;
            var maxRaw = json.TryGetProperty("MaxValue", out var m) ? ValueToString(m) : null;
            var max = int.TryParse(maxRaw, out var parsed) ? (int?)parsed : null;
            return (value, max);
        }

        return (ValueToString(json), null);
    }

    // ════════════════════════════════════════════════════════════════
    //  Helpers
    // ════════════════════════════════════════════════════════════════

    private static FrozenDictionary<string, PropertyInfo> GetCachedProperties(Type type)
    {
        return s_propertyCache.GetOrAdd(type, static t =>
        {
            var props = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var dict = new Dictionary<string, PropertyInfo>(props.Length, StringComparer.Ordinal);
            foreach (var p in props)
                dict[p.Name] = p;
            return dict.ToFrozenDictionary(StringComparer.Ordinal);
        });
    }

    private static string? ValueToString(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.Null => null,
            _ => el.GetRawText()
        };
    }

    private static string FormatPrerequisiteValues(string settingKey, FrozenSet<string> values)
    {
        var reverseMap = SettingCatalog.GetReverseMap(settingKey);
        if (reverseMap is not null)
        {
            return string.Join(" / ", values.Select(v =>
                reverseMap.TryGetValue(v, out var dn) ? dn : v));
        }
        return string.Join(" / ", values);
    }
}
