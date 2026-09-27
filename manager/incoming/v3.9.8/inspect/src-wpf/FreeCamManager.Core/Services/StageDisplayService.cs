using System.Text.RegularExpressions;

namespace FreeCamManager.Core.Services;

public static partial class StageDisplayService
{
    public static string Format(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage)) return "—";

        var value = stage.Trim();
        value = TypePrefixRegex().Replace(value, "");
        value = FixSeparatorRegex().Replace(value, " · Fix");
        value = value.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(value) ? "—" : value;
    }

    [GeneratedRegex(@"^(?:Probe|Test|Experiment|Regression)", RegexOptions.IgnoreCase)]
    private static partial Regex TypePrefixRegex();

    [GeneratedRegex(@"[ _.-]*Fix\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FixSeparatorRegex();
}
