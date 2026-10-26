"""Per-install configuration. No machine-specific paths are shipped."""
from __future__ import annotations
import copy
import hashlib
import json
import os
from pathlib import Path
import secrets
import urllib.parse

ROOT = Path(__file__).resolve().parent


def save(config, root=ROOT):
    path = root / 'config.json'
    temporary = path.with_suffix('.tmp')
    temporary.write_text(json.dumps(config, ensure_ascii=False, indent=2), encoding='utf-8')
    temporary.replace(path)


def load(root=ROOT):
    default = json.loads((root / 'config.example.json').read_text(encoding='utf-8-sig'))
    path = root / 'config.json'
    config = json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else copy.deepcopy(default)
    if config.get('schemaVersion') != 1:
        raise ValueError('Unsupported configuration schema')
    changed = False
    if not config.get('token'):
        config['token'] = secrets.token_urlsafe(32)
        changed = True
    endpoint = urllib.parse.urlsplit(config['tts']['endpoint'])
    if endpoint.scheme != 'http' or endpoint.hostname not in ('127.0.0.1', 'localhost', '::1'):
        raise ValueError('The speech engine must use HTTP on this computer.')
    if not isinstance(config['tts'].get('engineCommand', []), list):
        raise ValueError('engineCommand must be an argument list, not a shell command.')
    for name in ('assets', 'skin'):
        (root / name).mkdir(exist_ok=True)
    if changed:
        save(config, root)
    # Resolve without persisting expanded paths, so relative configs move safely.
    resolved = copy.deepcopy(config)
    for name in ('gptWeights', 'sovitsWeights', 'referenceAudio', 'engineWorkingDirectory'):
        value = resolved['tts'].get(name, '')
        if value:
            candidate = Path(os.path.expandvars(value)).expanduser()
            resolved['tts'][name] = str((root / candidate).resolve())
    value = resolved.get('sessionPath', '')
    if value:
        resolved['sessionPath'] = str((root / Path(os.path.expandvars(value)).expanduser()).resolve())
    return resolved


def instance_id(root=ROOT):
    return hashlib.sha256(str(root.resolve()).lower().encode()).hexdigest()[:16]
