"""Prepare a local frame cache for the native opening, keeping the original MP4."""
from __future__ import annotations
import json
import shutil
import subprocess
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parent


def valid(root=ROOT):
    source = root / 'assets/opening.mp4'
    try:
        data = json.loads((root / 'assets/opening-cache.json').read_text(encoding='utf-8'))
        stat = source.stat()
        frames = root / 'assets/opening-frames'
        return (data['sourceSize'] == stat.st_size
                and data['sourceModifiedNs'] == stat.st_mtime_ns
                and data['fps'] == 30 and data['count'] > 0
                and len(list(frames.glob('*.jpg'))) == data['count']
                and all((frames / f'{i:05d}.jpg').is_file() for i in range(1, data['count'] + 1))
                and (root / 'assets/opening-audio.wav').stat().st_size > 44)
    except (OSError, ValueError, KeyError, TypeError):
        return False


def prepare(root=ROOT):
    if not (root / 'assets/opening.mp4').is_file():
        return
    if valid(root):
        return
    from appconfig import load
    config = load(root)
    ffmpeg = config.get('ffmpegExecutable') or shutil.which('ffmpeg')
    if not ffmpeg:
        raise RuntimeError('FFmpeg is required for an imported opening video. Configure its path first.')
    source = root / 'assets/opening.mp4'
    frames = root / 'assets/opening-frames'
    frames.mkdir(exist_ok=True)
    flags = subprocess.CREATE_NO_WINDOW
    common = [str(ffmpeg), '-hide_banner', '-loglevel', 'error', '-y', '-i', str(source)]
    subprocess.run(common + ['-an', '-vf', 'fps=30', '-q:v', '3', str(frames / '%05d.jpg')],
                   check=True, creationflags=flags, timeout=120)
    subprocess.run(common + ['-vn', '-c:a', 'pcm_s16le', str(root / 'assets/opening-audio.wav')],
                   check=True, creationflags=flags, timeout=60)
    with wave.open(str(root / 'assets/opening-audio.wav'), 'rb') as audio:
        duration = audio.getnframes() / audio.getframerate()
    # Audio and video lengths can differ by an AAC encoder's padding. The input
    # clip is fixed; reject leftover files rather than play stale tail frames.
    count = len(list(frames.glob('*.jpg')))
    if not count or abs(count / 30 - duration) > .15:
        raise ValueError('开屏缓存不完整，请检查 opening-frames 目录。')
    stat = source.stat()
    (root / 'assets/opening-cache.json').write_text(json.dumps({
        'sourceSize': stat.st_size, 'sourceModifiedNs': stat.st_mtime_ns,
        'fps': 30, 'count': count, 'duration': count / 30,
        'renderer': 'WPF Image sequence; original audio as PCM WAV',
    }, indent=2), encoding='utf-8')


if __name__ == '__main__':
    prepare()
