import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import sys
import tempfile
import unittest
import zipfile

PROJECT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(PROJECT / 'app'))
sys.path.insert(0, str(PROJECT / 'tools'))
from appconfig import load
import assets
import package


def create_app(folder):
    root = Path(folder) / 'app'
    root.mkdir()
    shutil.copy2(PROJECT / 'app/config.example.json', root / 'config.example.json')
    return root


class PortabilityTests(unittest.TestCase):
    def test_fresh_install_never_binds_old_chat_and_generates_own_token(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            a, b = load(create_app(first)), load(create_app(second))
            self.assertEqual(a['sessionPath'], '')
            self.assertFalse(a['tts']['enabled'])
            self.assertGreaterEqual(len(a['token']), 32)
            self.assertNotEqual(a['token'], b['token'])

    def test_relative_model_paths_survive_relocation(self):
        with tempfile.TemporaryDirectory() as first, tempfile.TemporaryDirectory() as second:
            a, b = create_app(first), create_app(second)
            config = load(a)
            config['tts']['gptWeights'] = '../private/models/fairy.ckpt'
            (a / 'config.json').write_text(json.dumps(config))
            shutil.copy2(a / 'config.json', b / 'config.json')
            self.assertEqual(Path(load(b)['tts']['gptWeights']), (Path(second) / 'private/models/fairy.ckpt').resolve())

    def test_remote_tts_endpoint_refused(self):
        with tempfile.TemporaryDirectory() as folder:
            root = create_app(folder)
            config = load(root)
            config['tts']['endpoint'] = 'https://example.com/voice'
            (root / 'config.json').write_text(json.dumps(config))
            with self.assertRaises(ValueError):
                load(root)

    def test_private_export_import_excludes_config_and_history(self):
        with tempfile.TemporaryDirectory() as source, tempfile.TemporaryDirectory() as target:
            origin, destination = create_app(source), create_app(target)
            config = load(origin)
            config['sessionPath'] = 'PRIVATE_CHAT_SENTINEL.jsonl'
            config['token'] = 'PRIVATE_TOKEN_SENTINEL'
            (origin / 'config.json').write_text(json.dumps(config))
            (origin / 'chat.jsonl').write_text('PRIVATE_CHAT_BODY')
            (origin / 'server.log').write_text('PRIVATE_LOG')
            (origin / 'assets/reference.wav').write_bytes(b'reference')
            archive = Path(source) / 'private.zip'
            assets.export(origin, archive)
            with zipfile.ZipFile(archive) as saved:
                payload = b''.join(saved.read(name) for name in saved.namelist())
                for marker in (b'PRIVATE_CHAT', b'PRIVATE_TOKEN', b'PRIVATE_LOG'):
                    self.assertNotIn(marker, payload)
            self.assertEqual(assets.import_pack(archive, destination), 1)
            imported = load(destination)
            self.assertEqual(imported['sessionPath'], '')
            self.assertFalse(imported['tts']['enabled'])
            self.assertEqual((destination / 'assets/reference.wav').read_bytes(), b'reference')

    def test_zip_traversal_rejected_without_writing(self):
        with tempfile.TemporaryDirectory() as folder:
            root = create_app(folder)
            archive = Path(folder) / 'bad.zip'
            with zipfile.ZipFile(archive, 'w') as saved:
                saved.writestr('../../escape.txt', 'bad')
                saved.writestr('private-manifest.json', json.dumps({'version': 1, 'private': True, 'files': {'../../escape.txt': 'x'}}))
            with self.assertRaises(ValueError):
                assets.import_pack(archive, root)
            self.assertFalse((Path(folder) / 'escape.txt').exists())

    def test_background_patch_preserves_voice_and_chat_settings(self):
        with tempfile.TemporaryDirectory() as source, tempfile.TemporaryDirectory() as target:
            origin, destination = create_app(source), create_app(target)
            (origin / 'skin').mkdir()
            (origin / 'skin/hdd-background.png').write_bytes(b'private background')
            config = load(destination)
            config['tts']['promptText'] = 'keep my reference words'
            config['sessionPath'] = 'selected.jsonl'
            (destination / 'config.json').write_text(json.dumps(config))
            archive = Path(source) / 'background.zip'
            assets.export(origin, archive)
            assets.import_pack(archive, destination)
            changed = load(destination)
            self.assertEqual(changed['tts']['promptText'], 'keep my reference words')
            self.assertEqual(Path(changed['sessionPath']).name, 'selected.jsonl')
            self.assertEqual((destination / 'skin/hdd-background.png').read_bytes(), b'private background')

    def test_bad_asset_checksum_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = create_app(folder)
            archive = Path(folder) / 'bad.zip'
            with zipfile.ZipFile(archive, 'w') as saved:
                saved.writestr('app/assets/reference.wav', 'bad')
                saved.writestr('private-manifest.json', json.dumps({'version': 1, 'private': True, 'files': {'app/assets/reference.wav': '0' * 64}}))
            with self.assertRaises(ValueError):
                assets.import_pack(archive, root)
            self.assertFalse((root / 'assets/reference.wav').exists())

    def test_release_allowlist_omits_local_data(self):
        paths = {p.relative_to(PROJECT).as_posix() for p in package.members()}
        self.assertNotIn('app/config.json', paths)
        self.assertEqual({p for p in paths if p.startswith('app/assets/')}, {'app/assets/default-emblem.json'})
        self.assertFalse(any(p.endswith(('.wav', '.ckpt', '.pth', '.mp4', '.jsonl', '.log')) for p in paths))
        self.assertNotIn('app/FairyCompanion.exe', paths)


if __name__ == '__main__':
    unittest.main()
