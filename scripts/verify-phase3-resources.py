#!/usr/bin/env python3
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
xaml_ns = '{http://schemas.microsoft.com/winfx/2006/xaml}'
errors: list[str] = []

files = sorted((root / 'Themes').glob('*.xaml')) + sorted((root / 'Views').glob('*.xaml'))
keys: set[str] = set()

for path in files:
    try:
        document = ET.parse(path)
    except ET.ParseError as exc:
        errors.append(f'{path.relative_to(root)}: XML parse error: {exc}')
        continue
    for element in document.iter():
        key = element.attrib.get(xaml_ns + 'Key')
        if key:
            keys.add(key)

        if 'Style' in element.attrib:
            local_name = element.tag.rsplit('}', 1)[-1]
            style_property_name = f'{local_name}.Style'
            if any(child.tag.rsplit('}', 1)[-1] == style_property_name for child in element):
                errors.append(
                    f'{path.relative_to(root)}: {local_name} assigns Style both as an attribute and property element')

resource_pattern = re.compile(r'\{(?:Static|Dynamic)Resource\s+([^}\s]+)')
for path in files:
    text = path.read_text(encoding='utf-8-sig')
    for key in resource_pattern.findall(text):
        if key.startswith('{x:Static'):
            continue
        if key not in keys:
            errors.append(f'{path.relative_to(root)}: unresolved resource {key}')

class_pattern = re.compile(r'x:Class="([^"]+)"')
for path in sorted((root / 'Views').glob('*.xaml')):
    text = path.read_text(encoding='utf-8-sig')
    match = class_pattern.search(text)
    if match and not path.with_suffix(path.suffix + '.cs').exists():
        errors.append(f'{path.relative_to(root)}: missing code-behind for {match.group(1)}')


event_names = {
    'Click', 'Loaded', 'Unloaded', 'SizeChanged', 'Closed', 'Closing',
    'DataContextChanged', 'IsVisibleChanged', 'MouseDown', 'MouseUp',
    'MouseLeftButtonDown', 'MouseLeftButtonUp', 'MouseDoubleClick', 'MouseWheel',
    'KeyDown', 'KeyUp', 'Drop', 'DragOver', 'SelectionChanged', 'TextChanged',
    'Checked', 'Unchecked', 'PasswordChanged', 'PreviewKeyDown', 'PreviewMouseWheel'
}
handler_pattern = re.compile(r'^[A-Za-z_][A-Za-z0-9_]*$')
for path in sorted((root / 'Views').glob('*.xaml')):
    codebehind_path = path.with_suffix(path.suffix + '.cs')
    if not codebehind_path.exists():
        continue
    codebehind = codebehind_path.read_text(encoding='utf-8-sig')
    document = ET.parse(path)
    for element in document.iter():
        for attribute, value in element.attrib.items():
            event_name = attribute.rsplit('}', 1)[-1]
            if event_name not in event_names or not handler_pattern.fullmatch(value):
                continue
            if re.search(r'\b' + re.escape(value) + r'\s*\(', codebehind) is None:
                errors.append(
                    f'{path.relative_to(root)}: event handler {event_name}={value} is missing from code-behind')

required_names = {
    'Views/ShellWindow.xaml': ('OverlayOpenButton', 'DesktopSidebar', 'OverlaySidebar'),
    'Views/ShellSidebarView.xaml': ('NavigationList',),
}
for relative, names in required_names.items():
    text = (root / relative).read_text(encoding='utf-8-sig')
    for name in names:
        if f'x:Name="{name}"' not in text:
            errors.append(f'{relative}: missing x:Name {name}')


phone_status = (root / "Views/PhoneConnectionStatusView.xaml").read_text(encoding="utf-8-sig")
for required in ("SystemColors.WindowTextBrushKey", "SystemColors.HighlightBrushKey"):
    if required not in phone_status:
        errors.append(f"Views/PhoneConnectionStatusView.xaml: missing high-contrast resource {required}")

navigation_theme = (root / "Themes/Navigation.xaml").read_text(encoding="utf-8-sig")
if "TextElement.Foreground" not in navigation_theme:
    errors.append("Themes/Navigation.xaml: navigation badges do not inherit semantic foreground")

if errors:
    print('Phase 3 resource verification FAILED')
    for error in errors:
        print(' -', error)
    sys.exit(1)

print(f'Phase 3 resource verification passed: {len(files)} XAML files, {len(keys)} resource keys')
