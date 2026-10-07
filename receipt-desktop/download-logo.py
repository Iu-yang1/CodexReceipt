from pathlib import Path
from urllib.request import Request, urlopen
from xml.etree import ElementTree as ET

out = Path(__file__).parent / 'assets'
out.mkdir(exist_ok=True)
for name in ['codex-symbol-monochrome', 'codex-wordmark-monochrome']:
    url = f'https://asvg.app/assets/svg/codex/{name}.svg'
    request = Request(url, headers={'User-Agent': 'Mozilla/5.0'})
    with urlopen(request, timeout=30) as response:
        svg = response.read().decode('utf-8')
    root = ET.fromstring(svg)
    allowed = {'svg','g','path','rect','circle','ellipse','polygon','polyline','line','defs','clipPath','title','desc'}
    for node in root.iter():
        if node.tag.split('}')[-1] not in allowed:
            raise ValueError('Unexpected SVG element')
        if any(k.lower().startswith('on') or 'href' in k for k in node.attrib):
            raise ValueError('Unexpected SVG reference')
    (out / (name + '.svg')).write_text(svg, encoding='utf-8')
    print(name, root.attrib.get('viewBox'), len(svg))
(out / '来源.txt').write_text('Codex monochrome logo artwork: community-maintained @lobehub/icons-static-svg, distributed by https://asvg.app/icons/codex\nStored SVG paths are reproduced without redrawing.\n',encoding='utf-8')
