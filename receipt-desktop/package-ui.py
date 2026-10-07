"""Build the distributable UI from an explicit public-asset allowlist."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED, ZipInfo
import hashlib
import json

base = Path(__file__).parent
public_files = [
    'index.html', 'fonts/DotGothic16-Regular.ttf', 'fonts/VT323-Regular.ttf',
    'fonts/dotgothic16-OFL.txt', 'fonts/vt323-OFL.txt',
    'assets/codex-symbol-monochrome.svg', 'assets/codex-wordmark-monochrome.svg',
    'assets/ink-grain.svg', 'assets/来源.txt',
]
files = {name: (base / name).read_bytes() for name in public_files}
for name in ['LICENSE.txt', 'NOTICE.txt']:
    bundled = base / 'licenses' / ('WebView2-' + name)
    package_file = base / '.packages/microsoft.web.webview2/1.0.4258.31' / name
    files['licenses/WebView2-' + name] = (bundled if bundled.exists() else package_file).read_bytes()
for package, name in [('microsoft.netcore.app.runtime.win-x64', 'LICENSE.TXT'),
                      ('microsoft.netcore.app.runtime.win-x64', 'THIRD-PARTY-NOTICES.TXT'),
                      ('microsoft.windowsdesktop.app.runtime.win-x64', 'LICENSE')]:
    path = base / '.packages' / package / '10.0.8' / name
    bundled = base / 'licenses' / (package + '-' + name)
    if bundled.exists():
        path = bundled
    if path.exists():
        files['licenses/' + package + '-' + name] = path.read_bytes()
for name, data in files.items():
    if name.endswith(('.html', '.svg', '.txt')):
        text = data.decode('utf-8-sig')
        for private_path in [str(Path.home()), str(base.resolve()), str(base.parent.resolve())]:
            assert private_path not in text and private_path.replace('\\', '/') not in text, name
with ZipFile(base / 'ui.zip', 'w', ZIP_DEFLATED) as archive:
    for name, data in files.items():
        entry = ZipInfo(name, (2026, 1, 1, 0, 0, 0))
        entry.compress_type = ZIP_DEFLATED
        archive.writestr(entry, data)
print(json.dumps({'assets': len(files), 'bytes': (base / 'ui.zip').stat().st_size,
    'sha256': hashlib.sha256((base / 'ui.zip').read_bytes()).hexdigest(),
    'excluded': ['usage', 'screenshots', 'sessions', 'credentials', 'webview-profile', 'preferences']}, ensure_ascii=False))
