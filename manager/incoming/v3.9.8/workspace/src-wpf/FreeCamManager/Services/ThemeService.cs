using Microsoft.Win32;
using System.Windows;

namespace FreeCamManager.Services;

public sealed class ThemeService : IDisposable
{
    private bool _disposed;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public string SelectedMode { get; private set; } = "system";
    public string EffectiveTheme { get; private set; } = "dark";
    public string CurrentTheme => EffectiveTheme;

    public event EventHandler? EffectiveThemeChanged;

    public void Apply(string theme) => ApplyMode(theme);

    public void ApplyMode(string mode)
    {
        SelectedMode = NormalizeMode(mode);
        ApplyEffective(SelectedMode == "system" ? ResolveSystemTheme() : SelectedMode);
    }

    internal static string NormalizeMode(string? mode)
        => string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase) ? "light"
            : string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase) ? "dark"
            : "system";

    internal static string ResolveSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false);
            var raw = key?.GetValue("AppsUseLightTheme");
            if (raw is int value) return value == 1 ? "light" : "dark";
            if (raw is long longValue) return longValue == 1 ? "light" : "dark";
        }
        catch
        {
            // Windows theme lookup is best-effort; dark is the safe fallback.
        }
        return "dark";
    }

    private void ApplyEffective(string theme)
    {
        theme = theme == "light" ? "light" : "dark";
        var app = Application.Current;
        if (app is null)
        {
            EffectiveTheme = theme;
            return;
        }

        void ApplyOnUiThread()
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var target = dictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Colors.", StringComparison.OrdinalIgnoreCase) == true);
            var replacement = new ResourceDictionary
            {
                Source = new Uri($"Styles/Colors.{(theme == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative)
            };
            if (target is null) dictionaries.Insert(0, replacement);
            else dictionaries[dictionaries.IndexOf(target)] = replacement;

            var changed = !string.Equals(EffectiveTheme, theme, StringComparison.Ordinal);
            EffectiveTheme = theme;
            if (changed) EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
        }

        if (app.Dispatcher.CheckAccess()) ApplyOnUiThread();
        else _ = app.Dispatcher.InvokeAsync(ApplyOnUiThread);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed || SelectedMode != "system") return;
        ApplyEffective(ResolveSystemTheme());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
