"""Build a public archive from an explicit allowlist, never from the workspace."""
from pathlib import Path
import argparse
import hashlib
import zipfile

ROOT = Path(__file__).resolve().parents[1]
TOP = {'README.md', 'LICENSE', 'NOTICE.md', 'SECURITY.md', 'CONTRIBUTING.md', 'CHANGELOG.md', 'VERSION',
       '.gitignore', 'requirements.txt', 'Install.ps1', 'Install.cmd', 'Start.cmd', 'Stop.cmd',
       'Configure.cmd', 'Diagnose.cmd', 'Import-Private-Assets.cmd'}
APP = {'FairyCompanion.cs', 'FairyCompanion.manifest', 'FairyEmblem.cs', 'OpeningPlayer.cs',
       'FairyLauncher.cs', 'FairyStopper.cs', 'ExportFairyIcon.cs', 'replies.py', 'server.py',
       'launch.py', 'stop_fairy.py', 'appconfig.py', 'prepare_opening.py', 'skin-runtime.cjs',
       'codex-hdd.css', 'config.example.json', 'voice_cache.py', 'WindowPreferences.cs'}
BINARIES = {'FairyCompanion.exe', 'FairyLauncher.exe', 'FairyStopper.exe'}


def members(binaries=False):
    files = [ROOT / name for name in TOP] + [ROOT / 'app' / name for name in APP]
    files.append(ROOT / 'app/assets/default-emblem.json')
    for folder, pattern in (('tools', '*.py'), ('tests', '*.py'), ('docs', '*.md'),
                            ('skills/fairy-mode', '*.md'), ('.github/workflows', '*.yml'),
                            ('.github/ISSUE_TEMPLATE', '*.md')):
        files.extend((ROOT / folder).glob(pattern))
    if binaries:
        files.extend(ROOT / 'app' / name for name in BINARIES)
    return sorted(files)


def build(output, binaries=False):
    output = Path(output).resolve()
    if output.exists():
        raise FileExistsError('Choose a new output archive; existing releases are never overwritten.')
    files = members(binaries)
    missing = [str(path.relative_to(ROOT)) for path in files if not path.is_file()]
    if missing:
        raise FileNotFoundError('Release inputs missing: ' + ', '.join(missing))
    output.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for path in files:
            archive.write(path, 'Fairy-Companion/' + path.relative_to(ROOT).as_posix())
    checksum = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(output.suffix + '.sha256').write_text(checksum + '  ' + output.name + '\n', encoding='ascii')
    return len(files), checksum


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--binaries', action='store_true')
    args = parser.parse_args()
    count, checksum = build(args.output, args.binaries)
    print(f'{count} allowlisted public files packaged. SHA256: {checksum}')
