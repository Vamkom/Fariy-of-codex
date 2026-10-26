"""Start this installation's optional services, then its desktop window."""
from __future__ import annotations
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import time
import urllib.request
import psutil
from appconfig import ROOT, instance_id, load
from prepare_opening import prepare

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
FLAGS = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0


def status(url):
    try:
        with OPENER.open(url, timeout=2) as response:
            return json.load(response)
    except (OSError, ValueError):
        return None


def start(arguments, label, directory=ROOT):
    with (ROOT / (label + '.log')).open('ab', buffering=0) as output, (ROOT / (label + '-error.log')).open('ab', buffering=0) as error:
        return subprocess.Popen(arguments, cwd=directory, stdout=output, stderr=error, creationflags=FLAGS)


def main():
    config = load()
    # Refuse to reuse or stop a different installation's loopback services.
    for port, route in ((18742, '/api/status'), (18743, '/status')):
        existing = status(f'http://127.0.0.1:{port}{route}')
        if existing is not None and existing.get('instance') != instance_id():
            raise RuntimeError(f'Port {port} belongs to another app/install. Close that Fairy installation first.')
    prepare()
    if config.get('skinEnabled') and status('http://127.0.0.1:18743/status') is None:
        node = config.get('nodeExecutable') or shutil.which('node')
        if node:
            start([node, str(ROOT / 'skin-runtime.cjs')], 'skin-runtime')
    if status('http://127.0.0.1:18742/api/status') is None:
        start([sys.executable, '-X', 'utf8', str(ROOT / 'server.py')], 'server')
    for _ in range(30):
        bridge = status('http://127.0.0.1:18742/api/status')
        if bridge is not None:
            break
        time.sleep(.2)
    else:
        raise RuntimeError('Speech bridge did not start. See app/server-error.log.')
    if config['tts'].get('enabled') and not bridge.get('ready'):
        command = config['tts'].get('engineCommand', [])
        if status(config['tts']['endpoint'].rstrip('/') + '/openapi.json') is None and command:
            start(command, 'engine', config['tts'].get('engineWorkingDirectory') or ROOT)
            for _ in range(90):
                if status(config['tts']['endpoint'].rstrip('/') + '/openapi.json') is not None:
                    break
                time.sleep(1)
        request = urllib.request.Request('http://127.0.0.1:18742/api/prepare', data=b'{}', headers={
            'Content-Type': 'application/json', 'X-Fairy-Token': config['token']})
        try:
            with OPENER.open(request, timeout=240) as response:
                response.read()
        except OSError as error:
            (ROOT / 'launcher-warning.txt').write_text('Voice unavailable: ' + str(error), encoding='utf-8')
    if '--services-only' in sys.argv:
        return
    companion = str(ROOT / 'FairyCompanion.exe').casefold()
    for process in psutil.process_iter(['exe']):
        if (process.info.get('exe') or '').casefold() == companion:
            return
    subprocess.Popen([str(ROOT / 'FairyCompanion.exe')], cwd=ROOT)


if __name__ == '__main__':
    try:
        main()
    except Exception:
        import traceback
        (ROOT / 'launcher-error.log').write_text(traceback.format_exc(), encoding='utf-8')
        sys.exit(1)
