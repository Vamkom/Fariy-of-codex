"""Local Fairy media and GPT-SoVITS v2 bridge. No third-party Python packages required."""
from __future__ import annotations
import hashlib
import hmac
import io
import json
import mimetypes
import re
import secrets
import threading
import urllib.error
import urllib.parse
import urllib.request
import wave
import time
from voice_cache import cached_path, write_cache
from replies import ReplyReader
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from appconfig import ROOT, load, instance_id
CONFIG = load()
PORT = 18742
BASE = f'http://127.0.0.1:{PORT}'
ENGINE = CONFIG['tts']['endpoint'].rstrip('/')
if urllib.parse.urlsplit(ENGINE).hostname not in ('127.0.0.1', 'localhost', '::1'):
    raise ValueError('The speech engine must be on this computer.')
OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
VOICE_LOCK = threading.Lock()
STATE = {'ready': False}
STREAM_ID = secrets.token_hex(12)
GREETING = ROOT / 'assets/greeting.wav'
CACHE_META = ROOT / 'assets/greeting.json'
STATIC = {name: ROOT / name for name in (
    'assets/opening.mp4',
    'assets/reference.wav', 'assets/greeting.wav', 'skin/background.png')}


def signature():
    payload = {'greeting': CONFIG['greeting'], 'tts': CONFIG['tts']}
    return hashlib.sha256(json.dumps(payload, sort_keys=True).encode()).hexdigest()


def greeting_cached():
    return cached_path(GREETING, CACHE_META, signature()) is not None


def request_engine(path, payload=None):
    data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode()
    request = urllib.request.Request(ENGINE + path, data=data, headers={'Content-Type': 'application/json'})
    try:
        with OPENER.open(request, timeout=180) as response:
            return response.read()
    except urllib.error.HTTPError as error:
        raise RuntimeError('语音引擎拒绝了请求，请检查 v2 模型兼容性与引擎日志。') from error
    except (urllib.error.URLError, TimeoutError) as error:
        raise RuntimeError('本地 GPT-SoVITS 引擎未启动或未响应，请先在 127.0.0.1:9880 启动 api_v2.py。') from error


def synthesize(text):
    tts = CONFIG['tts']
    data = request_engine('/tts', {
        'text': text, 'text_lang': 'zh', 'ref_audio_path': tts['referenceAudio'],
        'prompt_text': tts['promptText'], 'prompt_lang': 'zh',
        'text_split_method': 'cut5', 'batch_size': 1, 'media_type': 'wav',
        'streaming_mode': False, 'speed_factor': 1.0,
    })
    try:
        with wave.open(io.BytesIO(data), 'rb') as audio:
            if audio.getnframes() == 0:
                raise ValueError('empty audio')
    except (wave.Error, EOFError, ValueError) as error:
        raise RuntimeError('引擎没有返回有效的 WAV 音频。') from error
    return data


def prepare_voice():
    if not CONFIG['tts'].get('enabled'):
        raise RuntimeError('Voice is disabled. Run Configure first.')
    if STATE['ready'] and greeting_cached():
        return
    for key in ('gptWeights', 'sovitsWeights', 'referenceAudio'):
        if not Path(CONFIG['tts'][key]).is_file():
            raise RuntimeError('缺少 Fairy 权重或参考音频，请检查 config.json。')
    for route, key in (('/set_gpt_weights', 'gptWeights'), ('/set_sovits_weights', 'sovitsWeights')):
        query = urllib.parse.urlencode({'weights_path': CONFIG['tts'][key]})
        request_engine(route + '?' + query)
    # Model readiness must not be lost merely because a greeting file is busy.
    STATE['ready'] = True
    if not greeting_cached():
        write_cache(GREETING, CACHE_META, signature(), synthesize(CONFIG['greeting']))
    STATE.pop('error', None)


def maintain_voice():
    while True:
        if not CONFIG['tts'].get('enabled'):
            return
        if VOICE_LOCK.locked():
            time.sleep(3)
            continue
        try:
            with OPENER.open(ENGINE + '/openapi.json', timeout=3) as response:
                response.read(1)
            if not STATE['ready'] and VOICE_LOCK.acquire(blocking=False):
                try:
                    prepare_voice()
                finally:
                    VOICE_LOCK.release()
        except (OSError, RuntimeError) as error:
            STATE.update(ready=False, error=str(error))
        time.sleep(10)


REPLIES = ReplyReader(CONFIG['sessionPath'], ROOT, VOICE_LOCK, lambda: STATE['ready'], synthesize) if CONFIG.get('sessionPath') else None


class Handler(BaseHTTPRequestHandler):
    def allowed_origin(self):
        origin = self.headers.get('Origin', '')
        return origin == BASE

    def common_headers(self):
        if self.allowed_origin():
            self.send_header('Access-Control-Allow-Origin', self.headers['Origin'])
            self.send_header('Vary', 'Origin')
        self.send_header('X-Content-Type-Options', 'nosniff')

    def respond(self, status, data, content_type='application/json; charset=utf-8'):
        if not isinstance(data, bytes):
            data = json.dumps(data, ensure_ascii=False).encode()
        self.send_response(status)
        self.common_headers()
        self.send_header('Content-Type', content_type)
        self.send_header('Content-Length', str(len(data)))
        self.send_header('Cache-Control', 'no-store')
        self.end_headers()
        if self.command != 'HEAD':
            self.wfile.write(data)

    def trusted_request(self):
        return self.headers.get('Host') in ('127.0.0.1:18742', 'localhost:18742') and self.headers.get('Origin', BASE) == BASE

    def do_OPTIONS(self):
        if not self.allowed_origin():
            return self.respond(403, {'error': 'Origin refused'})
        self.send_response(204)
        self.common_headers()
        self.send_header('Access-Control-Allow-Methods', 'GET, HEAD, POST, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type, X-Fairy-Token')
        self.end_headers()

    def do_HEAD(self):
        self.do_GET()

    def do_GET(self):
        if not self.trusted_request():
            return self.respond(403, {'error': 'Origin or Host refused'})
        route = urllib.parse.urlsplit(self.path).path
        if route == '/api/audio':
            if not hmac.compare_digest(self.headers.get('X-Fairy-Token', ''), CONFIG['token']):
                return self.respond(403, {'error': 'Invalid Fairy token'})
            try:
                after = int(urllib.parse.parse_qs(urllib.parse.urlsplit(self.path).query).get('after', ['0'])[0])
            except ValueError:
                return self.respond(400, {'error': 'Invalid cursor'})
            return self.respond(200, {'items': REPLIES.items_after(after) if REPLIES else [], 'streamId': STREAM_ID, 'latest': REPLIES.counter if REPLIES else 0})
        if route == '/api/status':
            return self.respond(200, {'ready': STATE['ready'], 'greetingCached': greeting_cached(), 'instance': instance_id(), 'error': STATE.get('error'), 'reader': REPLIES.status() if REPLIES else None})
        if route == '/api/config':
            if not hmac.compare_digest(self.headers.get('X-Fairy-Token', ''), CONFIG['token']):
                return self.respond(403, {'error': 'Invalid Fairy token'})
            return self.respond(200, {'enabled': True, 'api': BASE, 'token': CONFIG['token'], 'greeting': CONFIG['greeting']})
        if route == '/assets/greeting.wav' and not greeting_cached():
            return self.respond(404, {'error': '问候尚未合成'})
        if route == '/assets/greeting.wav':
            return self.serve_media(cached_path(GREETING, CACHE_META, signature()))
        name = 'index.html' if route == '/' else route.lstrip('/')
        path = STATIC.get(name)
        if path is None or not path.is_file():
            return self.respond(404, {'error': 'Not found'})
        self.serve_media(path)

    def serve_media(self, path):
        size = path.stat().st_size
        first, last = 0, size - 1
        range_header = self.headers.get('Range')
        if range_header:
            match = re.fullmatch(r'bytes=(\d*)-(\d*)', range_header)
            if not match or not any(match.groups()):
                return self.respond(416, {'error': 'Invalid range'})
            left, right = match.groups()
            if left:
                first, last = int(left), min(int(right), last) if right else last
            else:
                first = max(0, size - int(right))
            if first > last or first >= size:
                return self.respond(416, {'error': 'Range outside file'})
        length = last - first + 1
        self.send_response(206 if range_header else 200)
        self.common_headers()
        self.send_header('Content-Type', mimetypes.guess_type(path.name)[0] or 'application/octet-stream')
        self.send_header('Content-Length', str(length))
        self.send_header('Accept-Ranges', 'bytes')
        if range_header:
            self.send_header('Content-Range', f'bytes {first}-{last}/{size}')
        self.end_headers()
        if self.command == 'HEAD':
            return
        try:
            with path.open('rb') as stream:
                stream.seek(first)
                while length:
                    chunk = stream.read(min(length, 1024 * 1024))
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    length -= len(chunk)
        except (ConnectionResetError, BrokenPipeError, ConnectionAbortedError):
            pass

    def do_POST(self):
        if not self.trusted_request():
            return self.respond(403, {'error': 'Origin or Host refused'})
        token = self.headers.get('X-Fairy-Token', '')
        if not hmac.compare_digest(token, CONFIG['token']):
            return self.respond(403, {'error': 'Invalid Fairy token'})
        try:
            length = int(self.headers.get('Content-Length', '0'))
            if length < 0 or length > 40000:
                return self.respond(413, {'error': 'Request too large'})
            payload = json.loads(self.rfile.read(length))
            if not isinstance(payload, dict):
                raise ValueError('Expected object')
        except (ValueError, TypeError):
            return self.respond(400, {'error': 'Invalid JSON'})
        if not VOICE_LOCK.acquire(blocking=False):
            return self.respond(409, {'error': 'Fairy 正在处理另一段语音，请稍后重试。'})
        try:
            self.handle_speech(urllib.parse.urlsplit(self.path).path, payload)
        except (RuntimeError, OSError) as error:
            self.respond(503, {'error': str(error)})
        finally:
            VOICE_LOCK.release()

    def handle_speech(self, route, payload):
        if route == '/api/prepare':
            prepare_voice()
            return self.respond(200, {'ready': True, 'greetingCached': True})
        if route == '/api/say':
            text = payload.get('text')
            if not isinstance(text, str) or not text.strip() or len(text) > 6000:
                return self.respond(400, {'error': '请输入 1–6000 字的文本。'})
            if not STATE['ready'] or not REPLIES:
                return self.respond(503, {'error': 'Fairy 语音尚未就绪。'})
            REPLIES.publish(text.strip())
            return self.respond(202, {'queued': True})
        if route != '/api/tts':
            return self.respond(404, {'error': 'Not found'})
        text = payload.get('text')
        if not isinstance(text, str) or not text.strip() or len(text) > 6000:
            return self.respond(400, {'error': '请输入 1–6000 字的文本。'})
        if not STATE['ready']:
            return self.respond(503, {'error': '请先启动合成引擎并启用 Fairy 音色。'})
        self.respond(200, synthesize(text.strip()), 'audio/wav')

    def log_message(self, fmt, *args):
        if self.path.startswith('/api/audio') and args and args[1] == '200':
            return
        print(fmt % args, flush=True)


if __name__ == '__main__':
    server = ThreadingHTTPServer(('127.0.0.1', PORT), Handler)
    print('Fairy preview: ' + BASE, flush=True)
    if REPLIES: REPLIES.start()
    threading.Thread(target=maintain_voice, daemon=True, name='fairy-voice-reconnect').start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        server.server_close()
