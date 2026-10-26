"""Read-only local checks; does not start programs or display credentials."""
from pathlib import Path
import json
import os
import shutil
import sys
import urllib.request
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from appconfig import ROOT, load, instance_id


def main():
    config = load()
    report = {'platform': sys.platform, 'python': sys.version.split()[0],
              'companionBuilt': (ROOT / 'FairyCompanion.exe').is_file(),
              'chatSelected': bool(config.get('sessionPath')),
              'chatExists': bool(config.get('sessionPath')) and Path(config['sessionPath']).is_file(),
              'voiceEnabled': config['tts'].get('enabled', False),
              'nodeFound': bool(config.get('nodeExecutable') or shutil.which('node')),
              'ffmpegFound': bool(config.get('ffmpegExecutable') or shutil.which('ffmpeg')),
              'openingImported': (ROOT / 'assets/opening.mp4').is_file(),
              'privateWeightsPresent': all(Path(config['tts'].get(key) or '__missing__').is_file() for key in ('gptWeights', 'sovitsWeights', 'referenceAudio')),
              'nativeSkinConnectionFile': (Path(os.environ.get('LOCALAPPDATA', '.')) / 'CodexDreamSkin/native-session.json').is_file()}
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for name, url in (('bridge', 'http://127.0.0.1:18742/api/status'), ('skin', 'http://127.0.0.1:18743/status')):
        try:
            with opener.open(url, timeout=2) as response:
                status = json.load(response)
            report[name] = {'reachable': True, 'thisInstallation': status.get('instance') == instance_id(),
                            'readyOrApplied': status.get('ready', status.get('installed', False))}
        except (OSError, ValueError):
            report[name] = {'reachable': False}
    print(json.dumps(report, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
