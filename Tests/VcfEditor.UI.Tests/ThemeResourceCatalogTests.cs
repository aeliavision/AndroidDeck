using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace VcfEditor.UI.Tests;

public sealed partial class ThemeResourceCatalogTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] RequiredKeys = new[]
    {
        "Brush.Background",
        "Brush.Surface",
        "Brush.TextPrimary",
        "Brush.Primary",
        "Brush.Error",
        "Brush.Focus"
    };

    [Fact]
    public void LightAndDarkPalettesExposeTheSameResourceKeys()
    {
        var root = FindRepositoryRoot();
        var light = ReadKeys(root, "Themes/Generated.Colors.Light.xaml");
        var dark = ReadKeys(root, "Themes/Generated.Colors.Dark.xaml");

        light.Should().BeEquivalentTo(dark);
        light.Should().Contain(RequiredKeys);
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrast")]
    public void EveryViewResourceReferenceResolvesForThemeMode(string mode)
    {
        var root = FindRepositoryRoot();
        var themesDirectory = Path.Combine(root, "Themes");
        var palette = Path.Combine(
            themesDirectory,
            mode == "Dark" ? "Generated.Colors.Dark.xaml" : "Generated.Colors.Light.xaml");
        var sharedThemeFiles = Directory.EnumerateFiles(themesDirectory, "*.xaml")
            .Where(path => !Path.GetFileName(path).StartsWith("Generated.Colors.", StringComparison.Ordinal))
            .ToArray();
        var viewFiles = Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.xaml").ToArray();
        var catalogFiles = sharedThemeFiles.Concat(viewFiles).Append(palette).ToArray();
        var knownKeys = catalogFiles.SelectMany(ReadKeys).ToHashSet(StringComparer.Ordinal);
        var unresolved = new List<string>();

        foreach (var path in sharedThemeFiles.Concat(viewFiles))
        {
            var text = File.ReadAllText(path);
            foreach (Match match in ResourceReferenceRegex().Matches(text))
            {
                var key = match.Groups[1].Value;
                if (key.StartsWith("{x:Static", StringComparison.Ordinal))
                    continue;
                if (!knownKeys.Contains(key))
                    unresolved.Add($"{mode}: {Path.GetRelativePath(root, path)}: {key}");
            }
        }

        unresolved.Should().BeEmpty();
    }

    [Fact]
    public void ResourceDictionariesDefineHighContrastOverrides()
    {
        var root = FindRepositoryRoot();
        var themeText = string.Join('\n', Directory.EnumerateFiles(Path.Combine(root, "Themes"), "*.xaml")
            .Select(File.ReadAllText));

        themeText.Should().Contain("SystemParameters.HighContrast");
        themeText.Should().Contain("SystemColors.WindowBrushKey");
        themeText.Should().Contain("SystemColors.WindowTextBrushKey");
        themeText.Should().Contain("SystemColors.HighlightBrushKey");
    }

    [Fact]
    public void AppResourceDictionariesAreMergedInDependencyOrder()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "App.xaml"));
        var sources = document.Descendants()
            .Attributes("Source")
            .Select(attribute => attribute.Value)
            .ToArray();

        sources.Should().Equal(
            "Themes/Generated.Colors.Light.xaml",
            "Themes/Generated.Metrics.xaml",
            "Themes/Typography.xaml",
            "Themes/Controls.xaml",
            "Themes/Layout.xaml",
            "Themes/Phase5.xaml",
            "Themes/Navigation.xaml");
    }

    private static IEnumerable<string> ReadKeys(string path)
    {
        var document = XDocument.Load(path);
        return document.Descendants()
            .Attributes(Xaml + "Key")
            .Select(attribute => attribute.Value);
    }

    private static IEnumerable<string> ReadKeys(string root, string relativePath) =>
        ReadKeys(Path.Combine(root, relativePath));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "design-tokens.json")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }

    [GeneratedRegex(@"\{(?:Static|Dynamic)Resource\s+([^}\s]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceReferenceRegex();
}
