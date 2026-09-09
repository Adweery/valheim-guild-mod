"""Run production installers against isolated fixtures; never touch a real game."""
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BASE = 'https://github.com/Adweery/valheim-guild-mod'


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


class InstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.game = self.root / 'Steam library with spaces' / 'Valheim'
        self.game.mkdir(parents=True)
        self.platform = 'windows' if sys.platform == 'win32' else 'mac'
        if self.platform == 'windows':
            (self.game / 'valheim.exe').write_bytes(b'fixture, never executable')
        else:
            (self.game / 'valheim.app/Contents/MacOS').mkdir(parents=True)
        core = self.root / 'core'; core.write_bytes(b'loader fixture')
        dll = self.root / 'dll'; dll.write_bytes(b'mod fixture')
        self.mod = self.root / 'mod.zip'
        with zipfile.ZipFile(self.mod, 'w') as z:
            z.writestr('ValheimGuildTelemetry.dll', dll.read_bytes())
        self.loader = self.root / 'loader.zip'
        with zipfile.ZipFile(self.loader, 'w') as z:
            for name, data in {'BepInEx/core/BepInEx.dll': core.read_bytes(), 'BepInEx/config/BepInEx.cfg': b'original config', 'winhttp.dll': b'doorstop', 'doorstop_config.ini': b'config', 'doorstop_libs/libdoorstop_x64.dylib': b'mac doorstop', 'start_game_bepinex.sh': b'#!/bin/bash\n'}.items():
                z.writestr('BepInExPack_Valheim/' + name, data)
        self.manifest = dict(schema=1, version='9.9.9', mod_url=BASE+'/releases/download/v9.9.9/ValheimGuildTelemetry.zip', mod_sha256=sha(self.mod), dll_sha256=sha(dll), loader_url='https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/5.4.2350/', loader_sha256=sha(self.loader), loader_core_sha256=sha(core))
        self.manifest_path = self.root / 'latest.json'

    def tearDown(self):
        self.temp.cleanup()

    def run_installer(self, running=False):
        self.manifest_path.write_text(json.dumps(self.manifest))
        if self.platform == 'windows':
            shell = shutil.which('powershell.exe')
            if not shell:
                self.skipTest('Windows PowerShell required')
            wrapper = self.root / 'wrapper.ps1'
            # Mock only network and process observation. All file installation is real in temp.
            wrapper.write_text('''function Invoke-WebRequest { param($UseBasicParsing, $Uri, $OutFile, $TimeoutSec)
 $source = if ($Uri.EndsWith('latest.json')) { 'latest.json' } elseif ($Uri.EndsWith('ValheimGuildTelemetry.zip')) { 'mod.zip' } else { 'loader.zip' }
 Copy-Item -LiteralPath (Join-Path $env:FIXTURES $source) -Destination $OutFile
}
function Get-Process { param($Name, $ErrorAction) if ($env:FAKE_RUNNING -eq '1') { [pscustomobject]@{Name='valheim'} } }
& $env:INSTALLER -GamePath $env:GAME_FIXTURE
exit $LASTEXITCODE
'''.replace('param($UseBasicParsing,', 'param([switch]$UseBasicParsing,'))
            env = dict(os.environ, FIXTURES=str(self.root), FAKE_RUNNING=str(int(running)), INSTALLER=str(ROOT/'Install-Valheim.ps1'), GAME_FIXTURE=str(self.game))
            command = [shell, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(wrapper)]
        else:
            if sys.platform != 'darwin': self.skipTest('macOS tools required')
            # A private script copy substitutes process observation and transport only.
            fetch = self.root / 'curl'; fetch.write_text('#!'+sys.executable+'\nimport sys,shutil,os\na=sys.argv; out=a[a.index("-o")+1]; name=os.path.basename(out); shutil.copyfile(os.path.join(os.environ["FIXTURES"],name),out)\n'); fetch.chmod(0o755)
            pgrep = self.root / 'pgrep'; pgrep.write_text('#!/bin/sh\nexit '+('0' if running else '1')+'\n'); pgrep.chmod(0o755)
            script = self.root / 'test.command'
            script.write_text((ROOT/'Install-Valheim.command').read_text().replace('/usr/bin/curl', '"'+str(fetch)+'"').replace('/usr/bin/pgrep', '"'+str(pgrep)+'"'))
            env = dict(os.environ, FIXTURES=str(self.root))
            command = ['/bin/bash', str(script), str(self.game)]
        return subprocess.run(command, env=env, capture_output=True, text=True, input='', timeout=60)

    @property
    def plugin(self):
        return self.game / 'BepInEx/plugins/ValheimGuildTelemetry/ValheimGuildTelemetry.dll'

    def test_first_install_then_update_preserves_other_mods_and_config(self):
        result = self.run_installer()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(self.plugin.read_bytes(), b'mod fixture')
        config = self.game / 'BepInEx/config/BepInEx.cfg'; config.write_bytes(b'user settings')
        other = self.game / 'BepInEx/plugins/Other.dll'; other.write_bytes(b'other mod')
        self.plugin.write_bytes(b'previous version')
        result = self.run_installer()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(config.read_bytes(), b'user settings')
        self.assertEqual(other.read_bytes(), b'other mod')
        self.assertTrue(any(p.read_bytes() == b'previous version' for p in (self.game/'ValheimGuildBackups').rglob('ValheimGuildTelemetry.dll')))

    def test_corrupt_download_does_not_install(self):
        self.manifest['mod_sha256'] = '0'*64
        result = self.run_installer()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.game/'BepInEx').exists())

    def test_running_game_is_not_modified(self):
        result = self.run_installer(running=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.game/'BepInEx').exists())

    def test_foreign_manifest_url_rejected(self):
        self.manifest['mod_url'] = 'https://example.com/mod.zip'
        result = self.run_installer()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.game/'BepInEx').exists())

    def test_conflicting_loader_is_preserved(self):
        (self.game/'BepInEx').mkdir()
        sentinel = self.game/'BepInEx/keep.txt'; sentinel.write_bytes(b'personal')
        result = self.run_installer()
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(sentinel.read_bytes(), b'personal')

    def test_path_traversal_rejected(self):
        with zipfile.ZipFile(self.mod, 'w') as z: z.writestr('../escaped.txt', b'bad')
        self.manifest['mod_sha256'] = sha(self.mod)
        result = self.run_installer()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.plugin.exists())


if __name__ == '__main__': unittest.main()
