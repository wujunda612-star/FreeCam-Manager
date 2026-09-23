using System.Reflection;

namespace FreeCamManager.Core.Services;

public static class AppVersionService
{
    public static Version FromAssembly(Assembly assembly)
        => Normalize(assembly.GetName().Version);

    public static Version Normalize(Version? version)
    {
        version ??= new Version(0, 0, 0);
        var build = version.Build < 0 ? 0 : version.Build;
        return new Version(Math.Max(0, version.Major), Math.Max(0, version.Minor), build);
    }

    public static bool TryParse(string? value, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value) || !Version.TryParse(value.Trim(), out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    public static string FormatDisplay(Version? version)
    {
        var normalized = Normalize(version);
        var baseText = $"V{normalized.Major}.{normalized.Minor}";
        return normalized.Build > 0 ? $"{baseText} Fix{normalized.Build}" : baseText;
    }
}
