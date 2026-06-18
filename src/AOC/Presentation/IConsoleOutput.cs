using aoc.Application;
using aoc.Domain;

namespace aoc.Presentation;

public interface IConsoleOutput
{
    void PrintHelp(IReadOnlyCollection<SettingDef> settings);
    void PrintInfo(IReadOnlyList<InfoQueryResult> results);
    void PrintMessage(string message, bool error = false);
}
