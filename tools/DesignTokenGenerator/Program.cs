using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AndroidDeck.Tools.DesignTokens;

internal static class Program
{
    public static Task<int> Main(string[] args) => DesignTokenCli.RunAsync(args);
}

public static class DesignTokenCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = FindRepositoryRoot(args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal)));
        var check = args.Contains("--check", StringComparer.Ordinal);
        var document = await DesignTokenCompiler.LoadAsync(Path.Combine(root, "design-tokens.json"));
        var outputs = DesignTokenCompiler.Generate(document);

        var differences = new List<string>();
        foreach (var output in outputs)
        {
            var path = Path.Combine(root, output.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var normalized = DesignTokenCompiler.NormalizeNewlines(output.Content);
            if (check)
            {
                var existing = File.Exists(path)
                    ? DesignTokenCompiler.NormalizeNewlines(await File.ReadAllTextAsync(path, Encoding.UTF8))
                    : string.Empty;
                if (!StringComparer.Ordinal.Equals(existing, normalized))
                    differences.Add(output.RelativePath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, normalized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Console.WriteLine($"generated {output.RelativePath}");
        }

        if (differences.Count == 0)
            return 0;

        Console.Error.WriteLine("Generated design-token resources are stale:");
        foreach (var difference in differences)
            Console.Error.WriteLine($"- {difference}");
        Console.Error.WriteLine("Run: dotnet run --project tools/DesignTokenGenerator");
        return 1;
    }

    private static string FindRepositoryRoot(string? start)
    {
        DirectoryInfo? directory = new DirectoryInfo(start is null ? Environment.CurrentDirectory : Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "design-tokens.json")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate design-tokens.json from the current directory.");
    }
}

public static partial class DesignTokenCompiler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly string[] ColorOrder =
    [
        "background", "surface", "surfaceAlt", "surfaceRaised",
        "textPrimary", "textSecondary", "textDisabled",
        "border", "borderStrong",
        "primary", "primaryHover", "onPrimary", "primaryContainer", "onPrimaryContainer",
        "success", "onSuccess", "successContainer",
        "warning", "onWarning", "warningContainer",
        "error", "errorHover", "onError", "errorContainer",
        "info", "focus", "disabledSurface", "disabledContent",
        "inverseSurface", "inverseSurfaceAlt", "onInverseSurface", "onInverseSurfaceMuted", "overlay", "mediaScrim"
    ];

    public static async Task<DesignTokenDocument> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<DesignTokenDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("The design token document is empty.");
        Validate(document);
        return document;
    }

    public static IReadOnlyList<GeneratedOutput> Generate(DesignTokenDocument document)
    {
        Validate(document);
        return
        [
            new("Themes/Generated.Colors.Light.xaml", GenerateWpfColors(document.Colors.Light)),
            new("Themes/Generated.Colors.Dark.xaml", GenerateWpfColors(document.Colors.Dark)),
            new("Themes/Generated.Metrics.xaml", GenerateWpfMetrics(document)),
            new("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/theme/GeneratedTokens.kt", GenerateComposeTokens(document))
        ];
    }

    public static string NormalizeNewlines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";

    public static void Validate(DesignTokenDocument document)
    {
        if (document.SchemaVersion != 1)
            throw new InvalidDataException("Only design-token schema version 1 is supported.");
        if (!StringComparer.Ordinal.Equals(document.Product.Name, "AndroidDeck"))
            throw new InvalidDataException("The canonical product name must be AndroidDeck.");

        ValidateColors("light", document.Colors.Light);
        ValidateColors("dark", document.Colors.Dark);

        var expectedRadii = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["xs"] = 4, ["sm"] = 6, ["md"] = 8, ["lg"] = 12, ["xl"] = 16, ["full"] = 999
        };
        foreach (var pair in expectedRadii)
        {
            if (!document.Radius.TryGetValue(pair.Key, out var actual) || actual != pair.Value)
                throw new InvalidDataException($"radius.{pair.Key} must be {pair.Value}.");
        }

        foreach (var required in new[] { "xs", "sm", "md", "lg", "xl", "xxl", "xxxl" })
        {
            if (!document.Spacing.TryGetValue(required, out var value) || value < 0)
                throw new InvalidDataException($"spacing.{required} must be a non-negative integer.");
        }
    }

    private static void ValidateColors(string mode, Dictionary<string, string> colors)
    {
        foreach (var name in ColorOrder)
        {
            if (!colors.TryGetValue(name, out var value))
                throw new InvalidDataException($"colors.{mode}.{name} is required.");
            if (!HexColorRegex().IsMatch(value))
                throw new InvalidDataException($"colors.{mode}.{name} must be #RRGGBB or #AARRGGBB.");
        }
    }

    private static string GenerateWpfColors(Dictionary<string, string> colors)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
        builder.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");
        builder.AppendLine("    <!-- Generated from design-tokens.json. Do not edit by hand. -->");
        foreach (var name in ColorOrder)
            AppendInvariantLine(builder, $"    <Color x:Key=\"Color.{Pascal(name)}\">{colors[name]}</Color>");
        builder.AppendLine();
        foreach (var name in ColorOrder)
            AppendInvariantLine(builder, $"    <SolidColorBrush x:Key=\"Brush.{Pascal(name)}\" Color=\"{{StaticResource Color.{Pascal(name)}}}\" />");

        builder.AppendLine();
        builder.AppendLine("    <!-- Compatibility keys are generated from the same semantic values until screens migrate. -->");
        var compatibilityColors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PrimaryColor"] = "primary", ["PrimaryDarkColor"] = "primaryHover",
            ["SecondaryColor"] = "textSecondary", ["AccentColor"] = "primary",
            ["SuccessColor"] = "success", ["WarningColor"] = "warning", ["ErrorColor"] = "error",
            ["BackgroundColor"] = "background", ["SurfaceColor"] = "surface",
            ["TextPrimaryColor"] = "textPrimary", ["TextSecondaryColor"] = "textSecondary",
            ["BorderColor"] = "borderStrong"
        };
        foreach (var pair in compatibilityColors)
            AppendInvariantLine(builder, $"    <Color x:Key=\"{pair.Key}\">{colors[pair.Value]}</Color>");

        var compatibilityBrushes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PrimaryBrush"] = "primary", ["PrimaryDarkBrush"] = "primaryHover",
            ["SecondaryBrush"] = "textSecondary", ["AccentBrush"] = "primary",
            ["SuccessBrush"] = "success", ["WarningBrush"] = "warning", ["ErrorBrush"] = "error",
            ["BackgroundBrush"] = "background", ["SurfaceBrush"] = "surface",
            ["TextPrimaryBrush"] = "textPrimary", ["TextSecondaryBrush"] = "textSecondary",
            ["BorderBrush"] = "borderStrong", ["MutedSurfaceBrush"] = "surfaceAlt",
            ["SoftPrimaryBrush"] = "primaryContainer", ["SoftSuccessBrush"] = "successContainer",
            ["SoftWarningBrush"] = "warningContainer", ["WindowBackgroundBrush"] = "background",
            ["PrimaryTextBrush"] = "textPrimary"
        };
        foreach (var pair in compatibilityBrushes)
            AppendInvariantLine(builder, $"    <SolidColorBrush x:Key=\"{pair.Key}\" Color=\"{{StaticResource Color.{Pascal(pair.Value)}}}\" />");
        builder.AppendLine("    <LinearGradientBrush x:Key=\"AppChromeGradientBrush\" StartPoint=\"0,0\" EndPoint=\"1,1\">");
        builder.AppendLine("        <GradientStop Color=\"{StaticResource Color.Background}\" Offset=\"0\" />");
        builder.AppendLine("        <GradientStop Color=\"{StaticResource Color.PrimaryContainer}\" Offset=\"1\" />");
        builder.AppendLine("    </LinearGradientBrush>");
        builder.AppendLine("</ResourceDictionary>");
        return NormalizeNewlines(builder.ToString());
    }

    private static string GenerateWpfMetrics(DesignTokenDocument document)
    {
        var r = document.Radius;
        var s = document.Spacing;
        var t = document.Typography;
        var l = document.Layout;
        return NormalizeNewlines($$"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                xmlns:sys="clr-namespace:System;assembly=System.Runtime">
                <!-- Generated from design-tokens.json. Do not edit by hand. -->
                <CornerRadius x:Key="RadiusXs">{{r["xs"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusSm">{{r["sm"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusMd">{{r["md"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusLg">{{r["lg"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusXl">{{r["xl"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusFull">{{r["full"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusSmall">{{r["sm"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusMedium">{{r["md"]}}</CornerRadius>
                <CornerRadius x:Key="RadiusLarge">{{r["lg"]}}</CornerRadius>

                <Thickness x:Key="SpacingXs">{{s["xs"]}}</Thickness>
                <Thickness x:Key="SpacingSm">{{s["sm"]}}</Thickness>
                <Thickness x:Key="SpacingMd">{{s["md"]}}</Thickness>
                <Thickness x:Key="SpacingLg">{{s["lg"]}}</Thickness>
                <Thickness x:Key="SpacingXl">{{s["xl"]}}</Thickness>
                <Thickness x:Key="Spacing2Xl">{{s["xxl"]}}</Thickness>
                <Thickness x:Key="Spacing3Xl">{{s["xxxl"]}}</Thickness>
                <Thickness x:Key="PageMargin">{{l.PageMargin}}</Thickness>
                <Thickness x:Key="CardPadding">{{l.CardPadding}}</Thickness>
                <Thickness x:Key="CardPaddingLg">{{s["xl"]}}</Thickness>
                <Thickness x:Key="SectionSpacing">0,0,0,{{s["lg"]}}</Thickness>
                <Thickness x:Key="ItemSpacing">0,0,0,{{s["sm"]}}</Thickness>

                <sys:Double x:Key="FontSize.Display">{{t.Display}}</sys:Double>
                <sys:Double x:Key="FontSize.Heading">{{t.Heading}}</sys:Double>
                <sys:Double x:Key="FontSize.PageTitle">{{t.PageTitle}}</sys:Double>
                <sys:Double x:Key="FontSize.Title">{{t.Title}}</sys:Double>
                <sys:Double x:Key="FontSize.BodyLarge">{{t.BodyLarge}}</sys:Double>
                <sys:Double x:Key="FontSize.Body">{{t.Body}}</sys:Double>
                <sys:Double x:Key="FontSize.Caption">{{t.Caption}}</sys:Double>
                <sys:Double x:Key="LineHeight.Body">{{t.BodyLineHeight}}</sys:Double>
                <sys:Double x:Key="ControlHeight.Compact">{{l.CompactControlHeight}}</sys:Double>
                <sys:Double x:Key="ControlHeight.Standard">{{l.StandardControlHeight}}</sys:Double>
                <sys:Double x:Key="TouchTarget.Minimum">{{l.TouchTarget}}</sys:Double>
                <sys:Double x:Key="DialogWidth.Maximum">{{l.DialogMaxWidth}}</sys:Double>
                <FontFamily x:Key="FontFamily.Application">{{t.DesktopFamily}}</FontFamily>
            </ResourceDictionary>
            """);
    }

    private static string GenerateComposeTokens(DesignTokenDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine("package com.aeliavision.androiddeck.core.ui.theme");
        builder.AppendLine();
        builder.AppendLine("import androidx.compose.ui.graphics.Color");
        builder.AppendLine("import androidx.compose.ui.unit.dp");
        builder.AppendLine("import androidx.compose.ui.unit.sp");
        builder.AppendLine();
        builder.AppendLine("// Generated from design-tokens.json. Do not edit by hand.");
        AppendComposeColorObject(builder, "LightColorTokens", document.Colors.Light);
        builder.AppendLine();
        AppendComposeColorObject(builder, "DarkColorTokens", document.Colors.Dark);
        builder.AppendLine();
        builder.AppendLine("internal object AppRadii {");
        foreach (var name in new[] { "xs", "sm", "md", "lg", "xl", "full" })
            AppendInvariantLine(builder, $"    val {Pascal(name)} = {document.Radius[name]}.dp");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal object AppSpacing {");
        foreach (var name in new[] { "none", "xs", "sm", "md", "lg", "xl", "xxl", "xxxl" })
            AppendInvariantLine(builder, $"    val {Pascal(name)} = {document.Spacing[name]}.dp");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal object AppTypographyTokens {");
        AppendInvariantLine(builder, $"    val Display = {document.Typography.Display}.sp");
        AppendInvariantLine(builder, $"    val Heading = {document.Typography.Heading}.sp");
        AppendInvariantLine(builder, $"    val PageTitle = {document.Typography.PageTitle}.sp");
        AppendInvariantLine(builder, $"    val Title = {document.Typography.Title}.sp");
        AppendInvariantLine(builder, $"    val BodyLarge = {document.Typography.BodyLarge}.sp");
        AppendInvariantLine(builder, $"    val Body = {document.Typography.Body}.sp");
        AppendInvariantLine(builder, $"    val Caption = {document.Typography.Caption}.sp");
        AppendInvariantLine(builder, $"    val BodyLineHeight = {document.Typography.BodyLineHeight}.sp");
        builder.AppendLine("}");
        builder.AppendLine();
        builder.AppendLine("internal object AppElevation {");
        AppendInvariantLine(builder, $"    val None = {document.Elevation.None}.dp");
        AppendInvariantLine(builder, $"    val Low = {document.Elevation.Low}.dp");
        AppendInvariantLine(builder, $"    val Medium = {document.Elevation.Medium}.dp");
        AppendInvariantLine(builder, $"    val High = {document.Elevation.High}.dp");
        builder.AppendLine("}");
        return NormalizeNewlines(builder.ToString());
    }

    private static void AppendComposeColorObject(StringBuilder builder, string name, Dictionary<string, string> colors)
    {
        AppendInvariantLine(builder, $"internal object {name} {{");
        foreach (var color in ColorOrder)
            AppendInvariantLine(builder, $"    val {Pascal(color)} = Color(0x{ToComposeHex(colors[color])})");
        builder.AppendLine("}");
    }

    private static void AppendInvariantLine(StringBuilder builder, FormattableString value)
        => builder.AppendLine(value.ToString(CultureInfo.InvariantCulture));

    private static string ToComposeHex(string value)
    {
        var raw = value[1..].ToUpperInvariant();
        return raw.Length == 6 ? $"FF{raw}" : raw;
    }

    private static string Pascal(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    [GeneratedRegex("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColorRegex();
}

public sealed record GeneratedOutput(string RelativePath, string Content);

public sealed class DesignTokenDocument
{
    public required int SchemaVersion { get; init; }
    public required ProductTokens Product { get; init; }
    public required ColorModes Colors { get; init; }
    public required Dictionary<string, int> Radius { get; init; }
    public required Dictionary<string, int> Spacing { get; init; }
    public required TypographyTokens Typography { get; init; }
    public required ElevationTokens Elevation { get; init; }
    public required LayoutTokens Layout { get; init; }
}

public sealed class ProductTokens
{
    public required string Name { get; init; }
    public required string DesktopTitle { get; init; }
    public required string Description { get; init; }
}

public sealed class ColorModes
{
    public required Dictionary<string, string> Light { get; init; }
    public required Dictionary<string, string> Dark { get; init; }
}

public sealed class TypographyTokens
{
    public required string DesktopFamily { get; init; }
    public required string AndroidFamily { get; init; }
    public required int Display { get; init; }
    public required int Heading { get; init; }
    public required int PageTitle { get; init; }
    public required int Title { get; init; }
    public required int BodyLarge { get; init; }
    public required int Body { get; init; }
    public required int Caption { get; init; }
    public required int BodyLineHeight { get; init; }
}

public sealed class ElevationTokens
{
    public required int None { get; init; }
    public required int Low { get; init; }
    public required int Medium { get; init; }
    public required int High { get; init; }
}

public sealed class LayoutTokens
{
    public required int PageMargin { get; init; }
    public required int CardPadding { get; init; }
    public required int CompactControlHeight { get; init; }
    public required int StandardControlHeight { get; init; }
    public required int TouchTarget { get; init; }
    public required int DialogMaxWidth { get; init; }
}
