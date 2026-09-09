"""Package a separately validated plugin without game files, tokens or save data."""
import argparse
import hashlib
import json
import re
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
BASE = 'https://github.com/Adweery/valheim-guild-mod'
p = argparse.ArgumentParser()
p.add_argument('--version', required=True)
p.add_argument('--dll', required=True, type=Path)
p.add_argument('--loader', required=True, type=Path)
p.add_argument('--output', required=True, type=Path)
a = p.parse_args()
if not re.fullmatch(r'\d+\.\d+\.\d+', a.version):
    p.error('Expected semantic version X.Y.Z')
a.output.mkdir(parents=True, exist_ok=True)
if any(a.output.iterdir()):
    p.error('Output directory must be empty; never replace an existing release')
dll = a.dll.read_bytes()
if dll[:2] != b'MZ':
    p.error('DLL is not a PE assembly')
sha = lambda b: hashlib.sha256(b).hexdigest()
with zipfile.ZipFile(a.loader) as z:
    core = z.read('BepInExPack_Valheim/BepInEx/core/BepInEx.dll')
with zipfile.ZipFile(a.output/'ValheimGuildTelemetry.zip', 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('ValheimGuildTelemetry.dll', dll)
for system, files in [('Windows', ['Install-Valheim.cmd', 'Install-Valheim.ps1']), ('Mac', ['Install-Valheim.command'])]:
    with zipfile.ZipFile(a.output/f'Valheim-Guild-Installer-{system}.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for name in files + ['README.md']:
            z.write(ROOT/name, name)
manifest = dict(schema=1, version=a.version, mod_url=f'{BASE}/releases/download/v{a.version}/ValheimGuildTelemetry.zip', mod_sha256=sha((a.output/'ValheimGuildTelemetry.zip').read_bytes()), dll_sha256=sha(dll), loader_url='https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/', loader_sha256=sha(a.loader.read_bytes()), loader_core_sha256=sha(core))
(a.output/'latest.json').write_text(json.dumps(manifest, indent=2)+'\n')
print('Prepared version', a.version, 'with four release assets.')
