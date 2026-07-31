using AndroidDeck.Tools.DesignTokens;
using FluentAssertions;

namespace DesignTokenGenerator.Tests;

public sealed class DesignTokenCompilerTests
{
    [Fact]
    public async Task GenerateIsDeterministicAndMatchesCommittedOutputs()
    {
        var root = FindRepositoryRoot();
        var document = await DesignTokenCompiler.LoadAsync(Path.Combine(root, "design-tokens.json"));

        var first = DesignTokenCompiler.Generate(document);
        var second = DesignTokenCompiler.Generate(document);

        first.Should().Equal(second);
        foreach (var output in first)
        {
            var committed = await File.ReadAllTextAsync(Path.Combine(root, output.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            DesignTokenCompiler.NormalizeNewlines(committed).Should().Be(output.Content);
        }
    }

    [Fact]
    public void ValidateRejectsInvalidColorValues()
    {
        var document = CreateValidDocument();
        document.Colors.Light["primary"] = "blue";

        var act = () => DesignTokenCompiler.Validate(document);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*colors.light.primary*");
    }

    [Fact]
    public void ValidateRejectsRadiusScaleDrift()
    {
        var document = CreateValidDocument();
        document.Radius["lg"] = 20;

        var act = () => DesignTokenCompiler.Validate(document);

        act.Should().Throw<InvalidDataException>()
            .WithMessage("*radius.lg must be 12*");
    }

    private static DesignTokenDocument CreateValidDocument()
    {
        var colors = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["background"] = "#F7F9FC", ["surface"] = "#FFFFFF", ["surfaceAlt"] = "#F1F5F9",
            ["surfaceRaised"] = "#FFFFFF", ["textPrimary"] = "#0F172A", ["textSecondary"] = "#475569",
            ["textDisabled"] = "#94A3B8", ["border"] = "#E2E8F0", ["borderStrong"] = "#CBD5E1",
            ["primary"] = "#2563EB", ["primaryHover"] = "#1D4ED8", ["onPrimary"] = "#FFFFFF",
            ["primaryContainer"] = "#DBEAFE", ["onPrimaryContainer"] = "#1E3A8A", ["success"] = "#16A34A",
            ["onSuccess"] = "#FFFFFF", ["successContainer"] = "#DCFCE7", ["warning"] = "#D97706",
            ["onWarning"] = "#FFFFFF", ["warningContainer"] = "#FEF3C7", ["error"] = "#DC2626",
            ["errorHover"] = "#B91C1C", ["onError"] = "#FFFFFF", ["errorContainer"] = "#FEE2E2",
            ["info"] = "#2563EB", ["focus"] = "#2563EB", ["disabledSurface"] = "#E2E8F0",
            ["disabledContent"] = "#94A3B8", ["inverseSurface"] = "#0F172A", ["inverseSurfaceAlt"] = "#1E293B",
            ["onInverseSurface"] = "#F8FAFC", ["onInverseSurfaceMuted"] = "#CBD5E1", ["overlay"] = "#99000000"
        };
        return new DesignTokenDocument
        {
            SchemaVersion = 1,
            Product = new ProductTokens { Name = "AndroidDeck", DesktopTitle = "AndroidDeck", Description = "Test" },
            Colors = new ColorModes { Light = new(colors), Dark = new(colors) },
            Radius = new() { ["xs"] = 4, ["sm"] = 6, ["md"] = 8, ["lg"] = 12, ["xl"] = 16, ["full"] = 999 },
            Spacing = new() { ["none"] = 0, ["xs"] = 4, ["sm"] = 8, ["md"] = 12, ["lg"] = 16, ["xl"] = 24, ["xxl"] = 32, ["xxxl"] = 40 },
            Typography = new TypographyTokens { DesktopFamily = "Segoe UI", AndroidFamily = "SansSerif", Display = 32, Heading = 24, PageTitle = 28, Title = 18, BodyLarge = 16, Body = 14, Caption = 12, BodyLineHeight = 20 },
            Elevation = new ElevationTokens { None = 0, Low = 1, Medium = 3, High = 8 },
            Layout = new LayoutTokens { PageMargin = 24, CardPadding = 16, CompactControlHeight = 34, StandardControlHeight = 40, TouchTarget = 48, DialogMaxWidth = 640 }
        };
    }

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
}
