from pathlib import Path
import sys

root = Path(__file__).resolve().parents[1]
phase5 = (root / "Themes" / "Phase5.xaml").read_text(encoding="utf-8")
dialog = (root / "Views" / "AppDialogWindow.cs").read_text(encoding="utf-8")
file_browser = (root / "Views" / "FileBrowserView.xaml").read_text(encoding="utf-8")
gallery = (root / "Views" / "GalleryView.xaml").read_text(encoding="utf-8")

errors = []
if '<Setter Property="WindowStartupLocation"' in phase5:
    errors.append("WindowStartupLocation is a CLR property and must not be set by a Style Setter")
if 'WindowStartupLocation = WindowStartupLocation.CenterOwner;' not in dialog:
    errors.append("AppDialogWindow must assign WindowStartupLocation directly")
for name, text in (("FileBrowserView.xaml", file_browser), ("GalleryView.xaml", gallery)):
    if 'Source={x:Reference Root}' in text:
        errors.append(f"{name} contains x:Reference inside a ContextMenu popup")
    if 'PlacementTarget.Tag' not in text:
        errors.append(f"{name} must route ContextMenu commands through PlacementTarget.Tag")


for name, text in (("FileBrowserView.xaml", file_browser), ("GalleryView.xaml", gallery)):
    if 'Value="{Binding TransferProgress}"' in text:
        errors.append(f"{name} must bind read-only TransferProgress with Mode=OneWay")
    if 'Value="{Binding TransferProgress, Mode=OneWay}"' not in text:
        errors.append(f"{name} is missing the explicit one-way TransferProgress binding")

if errors:
    print("Phase 9 XAML runtime-fix verification failed:")
    for error in errors:
        print(f"- {error}")
    sys.exit(1)
print("Phase 9 XAML runtime-fix verification passed")
