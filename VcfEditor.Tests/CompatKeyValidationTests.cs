using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VcfEditor.Tests;

public sealed class CompatKeyValidationTests
{
    private static readonly string[] ForbiddenKeys =
    [
        "BackgroundBrush", "SurfaceBrush", "TextPrimaryBrush", "TextSecondaryBrush",
        "PrimaryBrush", "PrimaryDarkBrush", "SecondaryBrush", "AccentBrush",
        "SuccessBrush", "WarningBrush", "ErrorBrush",
        "MutedSurfaceBrush", "SoftPrimaryBrush", "SoftSuccessBrush", "SoftWarningBrush",
        "WindowBackgroundBrush", "PrimaryTextBrush", "Brush.Accent",
    ];

    private static string GetViewsRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Views")))
            dir = dir.Parent;
        return dir is not null
            ? Path.Combine(dir.FullName, "Views")
            : throw new DirectoryNotFoundException("Cannot find Views directory.");
    }

    [Test]
    public void NoProductionXamlFileUsesLegacyCompatBrushKeys()
    {
        var viewsRoot = GetViewsRoot();
        var xamlFiles = Directory.GetFiles(viewsRoot, "*.xaml", SearchOption.AllDirectories);
        var violations = new List<string>();
        foreach (var file in xamlFiles)
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var key in ForbiddenKeys)
                {
                    if (line.Contains("Resource " + key + "}", StringComparison.OrdinalIgnoreCase) ||
                        line.Contains("Resource " + key + " ", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add("  [" + Path.GetRelativePath(viewsRoot, file) + ":" + (i + 1) + "]  key=" + key);
                    }
                }
            }
        }
        Assert.That(violations.Count == 0, Is.True,
            "Phase 8 enforcement failure - legacy compat keys still in use:\n" +
            string.Join(Environment.NewLine, violations));
    }
}
