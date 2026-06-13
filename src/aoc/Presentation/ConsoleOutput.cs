using System.Collections.Frozen;
using System.Text.Json;


namespace aoc.Presentation;

public sealed class ConsoleOutput : IConsoleOutput
{
    /// <summary>
    /// Pre-computed enum value display strings per setting key.
    /// Built once to avoid repeated string.Join/OrderBy allocations in PrintHelp.
    /// </summary>
    private static readonly FrozenDictionary<string, string> EnumHelpDisplay = SettingCatalog.All
        .Where(kv => kv.Value.EnumMap is not null)
        .ToFrozenDictionary(
            kv => kv.Key,
            kv => string.Join("/", kv.Value.EnumMap!.Keys.OrderBy(x => x).Select(k => $"`{k}`")));

    public void PrintHelp(IReadOnlyCollection<SettingDef> settings)
    {
        Console.WriteLine("aoc — USB 私有协议 OSD 设置工具");
        Console.WriteLine();
        Console.WriteLine("用法: aoc --set <设置名> <值>");
        Console.WriteLine("       aoc --get <设置名>");
        Console.WriteLine("       aoc --help");
        Console.WriteLine("       aoc --info [子命令]");
        Console.WriteLine();

        foreach (var setting in settings.OrderBy(s => s.Name))
        {
            var values = setting.EnumMap is not null
                ? (EnumHelpDisplay.TryGetValue(setting.Name, out var cached) ? cached : "数字")
                : "数字";
            Console.WriteLine($"  {setting.Name,-18} {setting.Description} ({values})");
        }

        Console.WriteLine();
        Console.WriteLine("信息查询 (--info):");
        foreach (var topic in InfoService.GetAllTopics().OrderBy(t => t.Key))
        {
            var feature = topic.IsFeatured ? " ⭐" : "";
            Console.WriteLine($"  --info {topic.Key,-10} {topic.DisplayName}{feature}");
        }

        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  aoc --set overdrive strong");
        Console.WriteLine("  aoc --set hdr hdr1");
        Console.WriteLine("  aoc --get hdr");
        Console.WriteLine("  aoc --info");
        Console.WriteLine("  aoc --info message");
    }

    public void PrintInfo(IReadOnlyList<InfoQueryResult> results)
    {
        if (results.Count == 0)
        {
            PrintMessage("ℹ️ 无可用信息", error: true);
            return;
        }

        // Single topic — compact output
        if (results.Count == 1)
        {
            PrintSingleInfoResult(results[0]);
            return;
        }

        // All topics — sectioned output
        Console.WriteLine("ℹ️ 显示器信息总览");
        Console.WriteLine("──────────────────");

        foreach (var result in results)
        {
            Console.WriteLine();
            PrintSingleInfoResult(result);
        }
    }

    private void PrintSingleInfoResult(InfoQueryResult result)
    {
        if (!result.Success)
        {
            PrintMessage(result.Message ?? $"❌ {result.DisplayName}: 查询失败", error: true);
            if (result.Diagnostic is not null)
                PrintMessage($"  诊断: {result.Diagnostic}", error: true);
            return;
        }

        var header = $"ℹ️ {result.DisplayName}";
        Console.WriteLine(header);
        Console.WriteLine(new string('─', Math.Min(header.Length, 60)));

        if (result.RawValue is JsonElement json)
        {
            PrintJsonValue(json, indent: 1);
        }
        else if (result.RawValue is not null)
        {
            Console.WriteLine($"  {result.RawValue}");
        }
        else
        {
            PrintMessage("  (空)", error: true);
        }
    }

    /// <summary>
    /// Recursively renders a JsonElement to console with indentation.
    /// </summary>
    private static void PrintJsonValue(JsonElement el, int indent = 0)
    {
        var pad = new string(' ', indent * 2);

        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                Console.WriteLine($"{pad}{el.GetString()}");
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                Console.WriteLine($"{pad}{el.GetRawText()}");
                break;

            case JsonValueKind.Null:
                Console.WriteLine($"{pad}(null)");
                break;

            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        Console.WriteLine($"{pad}{prop.Name}:");
                        PrintJsonValue(prop.Value, indent + 1);
                    }
                    else
                    {
                        var val = FormatCompactValue(prop.Value);
                        Console.WriteLine($"{pad}{prop.Name}: {val}");
                    }
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in el.EnumerateArray())
                {
                    if (item.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        Console.WriteLine($"{pad}[{++index}]");
                        PrintJsonValue(item, indent + 1);
                    }
                    else
                    {
                        Console.WriteLine($"{pad}[{++index}] {FormatCompactValue(item)}");
                    }
                }
                if (index == 0)
                    Console.WriteLine($"{pad}(空)");
                break;

            default:
                Console.WriteLine($"{pad}{el.GetRawText()}");
                break;
        }
    }

    /// <summary>Formats a leaf JSON value as a compact inline string.</summary>
    private static string FormatCompactValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "(null)",
        _ => el.GetRawText() ?? "",
    };

    public void PrintMessage(string message, bool error = false)
    {
        if (error) Console.Error.WriteLine(message);
        else Console.WriteLine(message);
    }
}
