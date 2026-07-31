#!/usr/bin/env python3
"""Static Phase 2 verification for environments without .NET/Gradle toolchains."""
from __future__ import annotations

import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ERRORS: list[str] = []


def require(condition: bool, message: str) -> None:
    if not condition:
        ERRORS.append(message)


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8-sig")


def main() -> int:
    tokens_path = ROOT / "design-tokens.json"
    require(tokens_path.exists(), "design-tokens.json is missing")
    if tokens_path.exists():
        data = json.loads(tokens_path.read_text(encoding="utf-8"))
        require(data.get("schemaVersion") == 1, "token schemaVersion must be 1")
        require(data.get("product", {}).get("name") == "AndroidDeck", "product.name must be AndroidDeck")
        for mode in ("light", "dark"):
            colors = data.get("colors", {}).get(mode, {})
            for key in (
                "background", "surface", "surfaceAlt", "textPrimary", "textSecondary",
                "border", "primary", "primaryHover", "onPrimary", "primaryContainer",
                "success", "warning", "error", "focus", "disabledSurface", "disabledContent",
            ):
                require(key in colors, f"colors.{mode}.{key} is missing")
        require(data.get("radius") == {"xs": 4, "sm": 6, "md": 8, "lg": 12, "xl": 16, "full": 999},
                "radius scale does not match the approved 4/6/8/12/16/full scale")

    generated = [
        "Themes/Generated.Colors.Light.xaml",
        "Themes/Generated.Colors.Dark.xaml",
        "Themes/Generated.Metrics.xaml",
        "AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/theme/GeneratedTokens.kt",
    ]
    for path in generated:
        require((ROOT / path).exists(), f"generated output missing: {path}")

    if tokens_path.exists() and all((ROOT / path).exists() for path in generated):
        light_xaml = read("Themes/Generated.Colors.Light.xaml")
        dark_xaml = read("Themes/Generated.Colors.Dark.xaml")
        compose_tokens = read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/theme/GeneratedTokens.kt")
        metrics_xaml = read("Themes/Generated.Metrics.xaml")

        def pascal(name: str) -> str:
            return name[:1].upper() + name[1:]

        for mode, xaml, object_name in (
            ("light", light_xaml, "LightColorTokens"),
            ("dark", dark_xaml, "DarkColorTokens"),
        ):
            require(f"internal object {object_name}" in compose_tokens,
                    f"Compose generated token object is missing: {object_name}")
            for name, value in data["colors"][mode].items():
                require(f'<Color x:Key="Color.{pascal(name)}">{value}</Color>' in xaml,
                        f"stale WPF {mode} color token: {name}")
                raw = value[1:].upper()
                compose_value = raw if len(raw) == 8 else "FF" + raw
                require(f"val {pascal(name)} = Color(0x{compose_value})" in compose_tokens,
                        f"stale Compose {mode} color token: {name}")
        for name, value in data["radius"].items():
            require(f'<CornerRadius x:Key="Radius{pascal(name)}">{value}</CornerRadius>' in metrics_xaml,
                    f"stale WPF radius token: {name}")
            require(f"val {pascal(name)} = {value}.dp" in compose_tokens,
                    f"stale Compose radius token: {name}")

    solution = read("VcfEditor.sln")
    require("DesignTokenGenerator" in solution, "DesignTokenGenerator projects are not included in VcfEditor.sln")
    require("DesignTokenGenerator.Tests" in solution, "DesignTokenGenerator.Tests is not included in VcfEditor.sln")
    require("--check" in read("scripts/verify-windows.ps1"),
            "Windows verification does not enforce deterministic token generation")

    app_xaml = read("App.xaml")
    required_dictionaries = [
        "Themes/Generated.Colors.Light.xaml",
        "Themes/Generated.Metrics.xaml",
        "Themes/Typography.xaml",
        "Themes/Controls.xaml",
        "Themes/Layout.xaml",
        "Themes/Navigation.xaml",
    ]
    for dictionary in required_dictionaries:
        require(dictionary in app_xaml, f"App.xaml does not merge {dictionary}")
    require("<Color " not in app_xaml and "<Style " not in app_xaml,
            "App.xaml still contains inline colors or styles")
    require(not (ROOT / "Themes/Colors.Light.xaml").exists(), "obsolete Themes/Colors.Light.xaml still exists")
    require((ROOT / "Services/IThemeService.cs").exists(), "IThemeService is missing")
    require((ROOT / "Services/ThemeService.cs").exists(), "ThemeService is missing")
    require((ROOT / "Models/AppTheme.cs").exists(), "AppTheme model is missing")
    if (ROOT / "Services/ThemeService.cs").exists():
        theme_service = read("Services/ThemeService.cs")
        require("Generated.Colors.Light.xaml" in theme_service, "ThemeService does not activate the light palette")
        require("Generated.Colors.Dark.xaml" in theme_service, "ThemeService does not activate the dark palette")
        require("SystemParameters.HighContrast" in theme_service, "ThemeService does not honor High Contrast")
    require((ROOT / "tests/VcfEditor.UI.Tests/ThemeResourceCatalogTests.cs").exists(),
            "WPF theme resource catalog tests are missing")
    active_theme_references = app_xaml + "\n" + (read("Services/ThemeService.cs") if (ROOT / "Services/ThemeService.cs").exists() else "")
    inactive_themes = [
        path.name for path in (ROOT / "Themes").glob("*.xaml")
        if path.name not in active_theme_references
    ]
    require(not inactive_themes, "inactive WPF theme dictionaries remain: " + ", ".join(inactive_themes))

    for path in [ROOT / "App.xaml", *(ROOT / "Themes").glob("*.xaml"), *(ROOT / "Views").glob("*.xaml")]:
        try:
            ET.parse(path)
        except ET.ParseError as exc:
            ERRORS.append(f"invalid XAML/XML {path.relative_to(ROOT)}: {exc}")

    kotlin_root = ROOT / "AndroidCompanion/app/src/main/java"
    compatibility_toggle = kotlin_root / "com/aeliavision/androiddeck/core/ui/components/luxury/LuxuryToggle.kt"
    kotlin_text = "\n".join(
        path.read_text(encoding="utf-8")
        for path in kotlin_root.rglob("*.kt")
        if path != compatibility_toggle
    )
    require("LuxuryCard" not in kotlin_text, "LuxuryCard usage remains")
    require("LuxuryToggle" not in kotlin_text, "LuxuryToggle usage remains outside the compatibility shim")
    if compatibility_toggle.exists():
        shim_text = compatibility_toggle.read_text(encoding="utf-8")
        require("ChampagneGold" not in shim_text, "compatibility toggle still references removed gold tokens")
    require("ChampagneGold" not in kotlin_text, "gold luxury token remains")
    require("GoogleFont" not in kotlin_text and "googlefonts.Font" not in kotlin_text,
            "runtime downloadable font code remains")
    require("Brush.radialGradient" not in read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/theme/Theme.kt"),
            "global radial-gradient theme background remains")
    require("extraLarge = RoundedCornerShape(16.dp)" in read("AndroidCompanion/app/src/main/java/com/aeliavision/androiddeck/core/ui/theme/Shape.kt"),
            "Compose extraLarge radius is not 16dp")

    gradle = read("AndroidCompanion/app/build.gradle.kts")
    catalog = read("AndroidCompanion/gradle/libs.versions.toml")
    require("ui.text.google.fonts" not in gradle, "Google fonts dependency remains in app build")
    require("androidx-ui-text-google-fonts" not in catalog, "Google fonts alias remains in version catalog")
    android_config_text = "\n".join(
        path.read_text(encoding="utf-8-sig", errors="replace")
        for path in (ROOT / "AndroidCompanion").rglob("*")
        if path.is_file() and path.suffix.lower() in {".xml", ".kts", ".toml", ".kt"}
    )
    require("font_certs.xml" not in android_config_text, "deleted downloadable-font certificate is still referenced")

    all_text = "\n".join(
        path.read_text(encoding="utf-8-sig", errors="replace")
        for path in ROOT.rglob("*")
        if path.is_file() and ".git" not in path.parts and path.suffix.lower() in {".cs", ".xaml", ".kt", ".xml"}
    )
    for forbidden in ("AndroidDeck Studio Pro", "Premium UI", "Premium Contact Editor Dashboard", 'Text="Premium"', 'Text="VCF Contact Editor"', 'Text="1.0.0"'):
        require(forbidden not in all_text, f"hard-coded/legacy product copy remains: {forbidden}")

    require('public const string AppName = "AndroidDeck";' in read("Constants.cs"), "Constants.AppName is not AndroidDeck")
    require("AssemblyInformationalVersionAttribute" in read("Constants.cs"), "desktop version is not assembly-derived")
    require("BuildConfig.VERSION_NAME" in kotlin_text, "Android UI does not expose BuildConfig.VERSION_NAME")

    radius_forbidden = []
    for path in [*(ROOT / "Themes").glob("*.xaml"), *(ROOT / "Views").glob("*.xaml")]:
        text = path.read_text(encoding="utf-8-sig")
        for line_no, line in enumerate(text.splitlines(), 1):
            if re.search(r'CornerRadius="(?:10|14|28)(?:[,\"]|$)', line):
                radius_forbidden.append(f"{path.relative_to(ROOT)}:{line_no}: {line.strip()}")
    require(not radius_forbidden, "non-standard WPF radii remain:\n" + "\n".join(radius_forbidden))
    static_theme_color_refs = []
    for path in (ROOT / "Themes").glob("*.xaml"):
        if path.name.startswith("Generated.Colors."):
            continue
        for line_no, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if "{StaticResource Color." in line:
                static_theme_color_refs.append(f"{path.relative_to(ROOT)}:{line_no}: {line.strip()}")
    require(not static_theme_color_refs,
            "theme-dependent colors use StaticResource outside generated palettes:\n" +
            "\n".join(static_theme_color_refs))
    require("RoundedCornerShape(24.dp)" not in kotlin_text and "RoundedCornerShape(28.dp)" not in kotlin_text,
            "non-standard Compose radii remain")

    if ERRORS:
        print("PHASE 2 VERIFICATION FAILED")
        for error in ERRORS:
            print(f"- {error}")
        return 1

    print("PHASE 2 VERIFICATION PASSED")
    print("- canonical semantic token schema")
    print("- generated WPF and Compose resources")
    print("- modular WPF resource dictionaries")
    print("- Android semantic theme and offline typography")
    print("- product naming/version bindings")
    print("- approved radius scale")
    return 0


if __name__ == "__main__":
    sys.exit(main())
