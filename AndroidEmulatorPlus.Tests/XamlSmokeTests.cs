using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Xml;
using Xunit;

namespace AndroidEmulatorPlus.Tests;

public class XamlSmokeTests
{
    private static readonly string ProjectDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "AndroidEmulatorPlus"));

    private static readonly string[] ThemeFiles =
    {
        "Themes/Mocha.xaml",
        "Themes/Latte.xaml",
        "Themes/Frappe.xaml",
        "Themes/Macchiato.xaml",
        "Themes/Styles.xaml",
    };

    private static readonly string[] ViewFiles =
    {
        "Views/AppsView.xaml",
        "Views/AvdView.xaml",
        "Views/ConfigView.xaml",
        "Views/ConfirmDialog.xaml",
        "Views/ConsoleView.xaml",
        "Views/InstallView.xaml",
        "Views/LaunchOptionsDialog.xaml",
        "Views/LogcatView.xaml",
        "Views/MagiskModulesDialog.xaml",
        "Views/MigrateView.xaml",
        "Views/PromptDialog.xaml",
        "Views/RootView.xaml",
        "Views/SettingsDialog.xaml",
        "Views/SnapshotDialog.xaml",
        "Views/SystemImagePickerDialog.xaml",
        "Views/WelcomeDialog.xaml",
        "MainWindow.xaml",
    };

    [Theory]
    [MemberData(nameof(ThemeFileData))]
    public void Theme_xaml_parses_without_errors(string relativePath)
    {
        var path = Path.Combine(ProjectDir, relativePath);
        Assert.True(File.Exists(path), $"Theme file missing: {path}");

        using var reader = XmlReader.Create(path);
        var rd = (ResourceDictionary)XamlReader.Load(reader);
        Assert.NotNull(rd);
        Assert.True(rd.Count > 0, $"Theme {relativePath} loaded but is empty");
    }

    [Theory]
    [MemberData(nameof(ViewFileData))]
    public void View_xaml_exists_and_is_well_formed_xml(string relativePath)
    {
        var path = Path.Combine(ProjectDir, relativePath);
        Assert.True(File.Exists(path), $"View file missing: {path}");

        using var reader = XmlReader.Create(path);
        while (reader.Read()) { }
    }

    [Theory]
    [MemberData(nameof(ThemeFileData))]
    public void Theme_declares_all_required_brush_keys(string relativePath)
    {
        if (relativePath.Contains("Styles")) return;

        var path = Path.Combine(ProjectDir, relativePath);
        using var reader = XmlReader.Create(path);
        var rd = (ResourceDictionary)XamlReader.Load(reader);

        string[] requiredBrushes =
        {
            "BaseBrush", "MantleBrush", "CrustBrush", "SurfaceBrush",
            "Surface1Brush", "OverlayBrush", "TextBrush", "SubtextBrush",
            "BlueBrush", "LavenderBrush", "GreenBrush", "YellowBrush",
            "RedBrush", "MauveBrush", "TealBrush", "PeachBrush",
        };

        foreach (var key in requiredBrushes)
            Assert.True(rd.Contains(key), $"{relativePath} is missing brush '{key}'");
    }

    [Fact]
    public void All_four_themes_define_identical_brush_key_sets()
    {
        var palettes = new[] { "Themes/Mocha.xaml", "Themes/Latte.xaml", "Themes/Frappe.xaml", "Themes/Macchiato.xaml" };
        HashSet<string>? referenceKeys = null;
        string? referenceName = null;

        foreach (var rel in palettes)
        {
            var path = Path.Combine(ProjectDir, rel);
            using var reader = XmlReader.Create(path);
            var rd = (ResourceDictionary)XamlReader.Load(reader);
            var keys = new HashSet<string>(rd.Keys.Cast<string>());

            if (referenceKeys is null)
            {
                referenceKeys = keys;
                referenceName = rel;
                continue;
            }

            var missing = referenceKeys.Except(keys).ToList();
            var extra = keys.Except(referenceKeys).ToList();
            Assert.True(missing.Count == 0 && extra.Count == 0,
                $"{rel} vs {referenceName}: missing=[{string.Join(",", missing)}] extra=[{string.Join(",", extra)}]");
        }
    }

    public static IEnumerable<object[]> ThemeFileData() =>
        ThemeFiles.Select(f => new object[] { f });

    public static IEnumerable<object[]> ViewFileData() =>
        ViewFiles.Select(f => new object[] { f });
}
