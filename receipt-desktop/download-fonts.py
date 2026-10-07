from pathlib import Path
from urllib.request import urlopen

out = Path(__file__).parent / 'fonts'
out.mkdir(exist_ok=True)
for family, font in [('dotgothic16', 'DotGothic16-Regular.ttf'), ('vt323', 'VT323-Regular.ttf')]:
    for name in [font, 'OFL.txt']:
        target = out / (name if name != 'OFL.txt' else family + '-OFL.txt')
        if target.exists() and target.stat().st_size > 100:
            continue
        url = f'https://raw.githubusercontent.com/google/fonts/main/ofl/{family}/{name}'
        with urlopen(url, timeout=30) as response:
            data = response.read()
        target.write_bytes(data)
        print(target.name, len(data))
