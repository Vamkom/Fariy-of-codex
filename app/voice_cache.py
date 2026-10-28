"""Immutable greeting audio: never replace a WAV that Windows may be playing."""
import json
from pathlib import Path
import secrets


def cached_path(legacy, metadata, signature):
    legacy, metadata = Path(legacy), Path(metadata)
    try:
        saved = json.loads(metadata.read_text(encoding='utf-8'))
        name = saved.get('file', legacy.name)
        if Path(name).name != name or not name.endswith('.wav'):
            return None
        target = legacy.parent / name
        return target if saved['signature'] == signature and target.is_file() else None
    except (OSError, ValueError, KeyError, TypeError):
        return None


def write_cache(legacy, metadata, signature, data):
    legacy, metadata = Path(legacy), Path(metadata)
    target = legacy.parent / ('greeting-' + signature[:12] + '-' + secrets.token_hex(4) + '.wav')
    temporary = target.with_suffix('.tmp')
    temporary.write_bytes(data)
    temporary.replace(target)
    saved = metadata.with_suffix('.tmp')
    saved.write_text(json.dumps({'signature': signature, 'file': target.name}), encoding='utf-8')
    saved.replace(metadata)
    return target
