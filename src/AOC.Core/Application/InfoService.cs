using aoc.Infrastructure;

namespace aoc.Application;

/// <summary>
/// 只读信息查询服务。调用 SDK 的 GetDisPlayMessage / GetDisPlayList 等方法
/// 获取显示器状态信息，不涉及任何设置修改。
/// </summary>
public sealed class InfoService
{
    private readonly IAocInvoker _invoker;

    /// <summary>
    /// 信息查询主题注册表。Key 是 CLI 子命令名。
    /// </summary>
    public static readonly FrozenDictionary<string, InfoTopicDef> Topics = new Dictionary<string, InfoTopicDef>
    {
        ["message"] = new("message", "GetDisPlayMessage", "显示器型号/固件版本", IsFeatured: true),
        ["list"]    = new("list",    "GetDisPlayList",    "已连接显示器列表"),
        ["name"]    = new("name",    "GetCurDisplayName", "当前显示器名称"),
        ["device"]  = new("device",  "GetConnectDevice",  "已连接的设备"),
        ["support"] = new("support", "GetSupport",         "支持的功能列表"),
        ["oled"]    = new("oled",    "GetOLEDPanelCareInfo", "OLED 保养信息(像素刷新/位移)"),
    }.ToFrozenDictionary();

    public InfoService(IAocInvoker invoker) => _invoker = invoker;

    /// <summary>获取所有信息主题的定义。</summary>
    public static IReadOnlyCollection<InfoTopicDef> GetAllTopics() => Topics.Values;

    /// <summary>
    /// 查询单个信息主题。返回查询结果，包含原始 SDK 返回值或错误信息。
    /// </summary>
    public InfoQueryResult Query(string topic)
    {
        if (!Topics.TryGetValue(topic, out var def))
        {
            var available = string.Join(", ", Topics.Keys.Order());
            return new InfoQueryResult(topic, "未知查询", Success: false,
                Message: $"❌ 未知信息查询 '{topic}'。可用子命令: {available}");
        }

        var raw = _invoker.Call(def.SdkMethod);
        if (raw is null)
        {
            var diag = _invoker.LastDiagnostic;
            return new InfoQueryResult(topic, def.DisplayName, Success: false,
                Message: $"❌ {def.DisplayName}: 查询失败",
                Diagnostic: diag);
        }

        return new InfoQueryResult(topic, def.DisplayName, Success: true, RawValue: raw);
    }

    /// <summary>查询所有信息主题。</summary>
    public List<InfoQueryResult> QueryAll()
    {
        var results = new List<InfoQueryResult>(Topics.Count);
        foreach (var kv in Topics)
        {
            results.Add(Query(kv.Key));
        }
        return results;
    }
}

/// <summary>
/// 信息主题定义。描述一个 SDK 信息查询方法及其显示信息。
/// </summary>
public sealed record InfoTopicDef(
    string Key,
    string SdkMethod,
    string DisplayName,
    bool IsFeatured = false
);

/// <summary>
/// 信息查询结果。包含原始 SDK 返回值（通常是 <see cref="System.Text.Json.JsonElement"/>）
/// 或失败时的错误信息。
/// </summary>
public sealed record InfoQueryResult(
    string TopicKey,
    string DisplayName,
    bool Success,
    object? RawValue = null,
    string? Message = null,
    string? Diagnostic = null
);
