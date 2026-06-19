using System.Windows;

namespace AndroidEmulatorPlus.Services;

/// <summary>
/// C-12: live theme swap. App.OnStartup loads the chosen palette + Styles into
/// the App-level merged dictionaries. ApplyAsync swaps the palette dictionary
/// at index 0 in place. Because all brush references in XAML use
/// <c>DynamicResource ...Brush</c>, controls re-resolve and re-paint without
/// a restart.
/// </summary>
public sealed class ThemeService
{
    private readonly SettingsService _settings;
    private readonly LogService _log;

    public ThemeService(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    /// <summary>"mocha" or "latte".</summary>
    public string Current => _settings.Current.Theme;

    public static readonly IReadOnlyList<string> AvailableThemes = new[] { "mocha", "frappe", "macchiato", "latte" };

    public static string NormalizeThemeName(string? theme)
    {
        if (theme is null) return "mocha";
        foreach (var t in AvailableThemes)
            if (theme.Equals(t, System.StringComparison.OrdinalIgnoreCase)) return t;
        return "mocha";
    }

    private static string ThemeToXamlPath(string norm) => norm switch
    {
        "latte" => "Themes/Latte.xaml",
        "frappe" => "Themes/Frappe.xaml",
        "macchiato" => "Themes/Macchiato.xaml",
        _ => "Themes/Mocha.xaml",
    };

    public void Apply(string theme)
    {
        var norm = NormalizeThemeName(theme);
        var src = ThemeToXamlPath(norm);
        var app = Application.Current;
        if (app is null) return;
        try
        {
            var newPalette = new ResourceDictionary { Source = new System.Uri(src, System.UriKind.Relative) };
            // Slot 0 is always the palette (per App.OnStartup); replace in place so the
            // Styles dictionary at slot 1 keeps resolving brushes through it.
            if (app.Resources.MergedDictionaries.Count == 0)
                app.Resources.MergedDictionaries.Add(newPalette);
            else
                app.Resources.MergedDictionaries[0] = newPalette;
            _settings.Current.Theme = norm;
            _settings.Save();
            _log.Info($"Theme applied: {norm}");
        }
        catch (System.Exception ex)
        {
            _log.Error("Theme swap failed: " + ex.Message);
        }
    }
}
