"""Restore this install's skin and stop only its own process paths."""
import json
import os
import urllib.request
import psutil
from appconfig import ROOT, instance_id, load


def main():
    config = load()
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    try:
        with opener.open('http://127.0.0.1:18743/status', timeout=2) as response:
            same = json.load(response).get('instance') == instance_id()
        if same:
            request = urllib.request.Request('http://127.0.0.1:18743/restore', data=b'{}', headers={'X-Fairy-Token': config['token']})
            with opener.open(request, timeout=10) as response:
                response.read()
    except (OSError, ValueError):
        pass
    own_executables = {str(ROOT / name).casefold() for name in ('FairyCompanion.exe', 'FairyLauncher.exe')}
    own_scripts = {str(ROOT / name).casefold() for name in ('launch.py', 'server.py', 'skin-runtime.cjs')}
    # An external GPT-SoVITS engine may be shared: never terminate it here.
    for process in psutil.process_iter(['exe', 'name', 'cmdline']):
        if process.pid == os.getpid():
            continue
        try:
            executable = (process.info['exe'] or '').casefold()
            name = (process.info['name'] or '').casefold()
            arguments = {arg.casefold() for arg in process.info['cmdline'] or []}
            if executable in own_executables or (name in ('python.exe', 'pythonw.exe', 'node.exe') and own_scripts & arguments):
                process.terminate()
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass


if __name__ == '__main__':
    main()
