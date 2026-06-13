using System.Text.Json.Serialization;

namespace aoc.Domain;

/// <summary>
/// SDK GetDisPlayMessage 返回值 — 显示器识别与 EDID 信息。
/// 字段命名保持与 SDK 原始返回值一致。
/// </summary>
public sealed record MonitorInfo
{
    /// <summary>SDK 错误码 (0 表示成功)</summary>
    [JsonPropertyName("err_code")]
    public int ErrCode { get; init; }

    /// <summary>SDK 调用是否成功</summary>
    public bool IsSucc { get; init; }

    /// <summary>SDK 返回的消息文本</summary>
    [JsonPropertyName("err_msg")]
    public string? ErrMsg { get; init; }

    /// <summary>请求 ID (通常为 null)</summary>
    public string? RequestId { get; init; }

    /// <summary>显示器标签信息 (EDID)</summary>
    public MonitorTag? Tag { get; init; }

    /// <summary>功能名称 (通常为 null)</summary>
    public string? FunctionName { get; init; }

    /// <summary>当前项 (通常为 null)</summary>
    public string? CurrItem { get; init; }

    /// <summary>用户友好的显示名称: "制造商 型号"</summary>
    public string DisplayName => Tag is not null
        ? $"{Tag.SManufacturer} {Tag.SMonitorName}".Trim()
        : "未知显示器";

    /// <summary>概要: "型号 | 尺寸 | 分辨率"</summary>
    public string Summary => Tag is not null
        ? $"{Tag.SMonitorName} | {Tag.ScreenSize} | {Tag.TimingRecommandation}"
        : "无法获取显示器信息";
}

/// <summary>
/// 显示器标签信息 — 对应 SDK 返回的 Tag 对象。
/// </summary>
public sealed record MonitorTag
{
    /// <summary>制造商名称 (AOC)</summary>
    public string? SManufacturer { get; init; }

    /// <summary>制造日期 (Week24-2025)</summary>
    public string? SManufacturerDate { get; init; }

    /// <summary>即插即用 ID (AOCB306)</summary>
    public string? PlugAndPlayID { get; init; }

    /// <summary>显示器型号 (CU34G3X)</summary>
    public string? SMonitorName { get; init; }

    /// <summary>序列号</summary>
    public string? SSerialNumber { get; init; }

    /// <summary>固件版本</summary>
    public string? SVersion { get; init; }

    /// <summary>屏幕尺寸</summary>
    public string? ScreenSize { get; init; }

    /// <summary>推荐分辨率</summary>
    public string? TimingRecommandation { get; init; }

    /// <summary>显示 Gamma 值</summary>
    public string? DisplayGamma { get; init; }

    /// <summary>显示类型和信号</summary>
    public string? DisplayTypeAndSignal { get; init; }

    /// <summary>红色色度坐标</summary>
    public string? RedChromaticity { get; init; }

    /// <summary>绿色色度坐标</summary>
    public string? GreenChromaticity { get; init; }

    /// <summary>蓝色色度坐标</summary>
    public string? BlueChromaticity { get; init; }

    /// <summary>白点色度坐标</summary>
    public string? WhitePoint { get; init; }
}
