"""Move a user's assets separately from the public source/release archive."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import sys
import zipfile
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from appconfig import ROOT, load, save

# Only these names are ever exported/imported. No configuration, chats or logs.
MEDIA = {
    'app/assets/opening.mp4': 'assets/opening.mp4',
    'app/assets/reference.wav': 'assets/reference.wav',
    'app/assets/greeting.wav': 'assets/greeting.wav',
    'app/assets/fairy-emblem.json': 'assets/fairy-emblem.json',
    'app/assets/fairy-icon.png': 'assets/fairy-icon.png',
    'app/assets/fairy.ico': 'assets/fairy.ico',
    'app/skin/background.png': 'skin/background.png',
    'app/skin/hdd-background.png': 'skin/hdd-background.png',
}
MODEL_NAMES = {'private/models/fairy.ckpt', 'private/models/fairy.pth'}
ALLOWED = set(MEDIA) | MODEL_NAMES
MAX_BYTES = 1024 * 1024 * 1024


def digest(path):
    value = hashlib.sha256()
    with Path(path).open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def export(source, output, models=None):
    source, output = Path(source).resolve(), Path(output).resolve()
    if output.exists():
        raise FileExistsError('Output exists; choose a new archive name.')
    files = {name: source / relative for name, relative in MEDIA.items() if (source / relative).is_file()}
    if models:
        for suffix, name in (('*.ckpt', 'private/models/fairy.ckpt'), ('*.pth', 'private/models/fairy.pth')):
            choices = list(Path(models).glob(suffix))
            if len(choices) != 1:
                raise ValueError(f'Expected exactly one {suffix} in model directory.')
            files[name] = choices[0]
    if not files:
        raise ValueError('No supported media found.')
    metadata = {'version': 1, 'private': True, 'files': {name: digest(path) for name, path in files.items()}}
    config_path = source / 'config.json'
    if config_path.is_file():
        original = json.loads(config_path.read_text(encoding='utf-8-sig'))
        metadata['greeting'] = original.get('greeting', '')
        metadata['promptText'] = original.get('tts', {}).get('promptText', '')
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
        for name, path in files.items():
            archive.write(path, name)
        archive.writestr('private-manifest.json', json.dumps(metadata, ensure_ascii=False, indent=2))
        archive.writestr('PRIVATE-DO-NOT-PUBLISH.txt', 'Personal migration assets, NOT a public release.\nThe voice model author prohibits commercial use. No redistribution permission is implied.\nDo not upload this archive to GitHub.\n')
    return len(files)


def import_pack(archive_path, root=ROOT):
    root = Path(root).resolve()
    project = root.parent
    with zipfile.ZipFile(archive_path) as archive:
        names = archive.namelist()
        if len(names) != len(set(names)):
            raise ValueError('Duplicate archive entries refused.')
        if archive.getinfo('private-manifest.json').file_size > 65536:
            raise ValueError('Asset manifest is too large.')
        metadata = json.loads(archive.read('private-manifest.json'))
        if metadata.get('version') != 1 or metadata.get('private') is not True:
            raise ValueError('Not a private Fairy asset pack.')
        files = metadata.get('files', {})
        if not files or not set(files) <= ALLOWED:
            raise ValueError('Unsupported asset names.')
        if set(names) - set(files) - {'private-manifest.json', 'PRIVATE-DO-NOT-PUBLISH.txt'}:
            raise ValueError('Unexpected files in asset pack.')
        if sum(archive.getinfo(name).file_size for name in files) > MAX_BYTES:
            raise ValueError('Asset pack exceeds the 1 GiB uncompressed limit.')
        # Verify every member before making changes. Never extract arbitrary paths.
        for name, expected in files.items():
            with archive.open(name) as stream:
                actual = hashlib.file_digest(stream, 'sha256').hexdigest()
            if actual != expected:
                raise ValueError('Asset checksum mismatch: ' + name)
        for name in files:
            target = root / MEDIA[name] if name in MEDIA else project / name
            target = target.resolve()
            if not target.is_relative_to(project):
                raise ValueError('Asset path escaped this project.')
            target.parent.mkdir(parents=True, exist_ok=True)
            temporary = target.with_suffix(target.suffix + '.importing')
            with archive.open(name) as source, temporary.open('wb') as output:
                shutil.copyfileobj(source, output)
            temporary.replace(target)
    load(root)
    config = json.loads((root / 'config.json').read_text(encoding='utf-8'))
    if metadata.get('greeting'):
        config['greeting'] = metadata['greeting']
    if 'promptText' in metadata:
        config['tts']['promptText'] = metadata['promptText']
    for key, relative in (('gptWeights', '../private/models/fairy.ckpt'), ('sovitsWeights', '../private/models/fairy.pth'), ('referenceAudio', 'assets/reference.wav')):
        if (root / relative).is_file():
            config['tts'][key] = relative
    # Importing assets never silently enables voice or chooses a chat to read.
    save(config, root)
    return len(files)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='action', required=True)
    out = commands.add_parser('export')
    out.add_argument('--from-package', type=Path, required=True)
    out.add_argument('--models', type=Path)
    out.add_argument('--output', type=Path, required=True)
    incoming = commands.add_parser('import')
    incoming.add_argument('archive', type=Path)
    args = parser.parse_args()
    count = export(args.from_package, args.output, args.models) if args.action == 'export' else import_pack(args.archive)
    print(f'{count} asset files processed. Private packs must not be published.')
