import json
from pathlib import Path
import sys
import tempfile
import threading
import unittest
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
from voice_cache import cached_path, write_cache
from replies import ReplyReader
import server


class VoiceReliabilityTests(unittest.TestCase):
    def test_new_cache_does_not_replace_wav_held_by_player(self):
        with tempfile.TemporaryDirectory() as folder:
            legacy, metadata = Path(folder) / 'greeting.wav', Path(folder) / 'greeting.json'
            legacy.write_bytes(b'old audio still playing')
            metadata.write_text(json.dumps({'signature': 'old'}))
            with legacy.open('rb') as playing:
                path = write_cache(legacy, metadata, 'new-signature', b'new audio')
                self.assertEqual(playing.read(), b'old audio still playing')
            self.assertNotEqual(path, legacy)
            self.assertEqual(cached_path(legacy, metadata, 'new-signature'), path)
            self.assertIsNone(cached_path(legacy, metadata, 'old'))

    def test_new_reply_does_not_cancel_previous_reply(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'assets').mkdir()
            reader = ReplyReader(root / 'chat.jsonl', root, threading.Lock(), lambda: True, lambda text: text.encode())
            reader.publish('first reply')
            reader.publish('second reply')
            self.assertEqual(len(reader.messages), 2)
            reader.render_message(*reader.messages.popleft())
            reader.render_message(*reader.messages.popleft())
            result = [(root / 'assets' / item['file']).read_bytes() for item in reader.items_after(0)]
            self.assertEqual(result, [b'first reply', b'second reply'])

    def test_restart_writes_distinct_audio_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'assets').mkdir()
            names = []
            for _ in range(2):
                reader = ReplyReader(root / 'chat.jsonl', root, threading.Lock(), lambda: True, lambda _: b'audio')
                reader.render_message(1, 'reply')
                names.append(reader.items_after(0)[0]['file'])
            self.assertNotEqual(*names)

    def test_transient_engine_error_retries_same_sentence(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / 'assets').mkdir()
            calls = []
            def synthesize(text):
                calls.append(text)
                if len(calls) == 1:
                    raise OSError('engine restarting')
                return b'audio'
            reader = ReplyReader(root / 'chat.jsonl', root, threading.Lock(), lambda: True, synthesize)
            with patch.object(reader.stop_event, 'wait', return_value=False):
                reader.render_message(1, 'do not drop me')
            self.assertEqual(calls, ['do not drop me', 'do not drop me'])
            self.assertEqual(len(reader.items_after(0)), 1)

    def test_preparing_ready_cached_voice_does_not_reload_or_overwrite(self):
        previous = dict(server.STATE)
        enabled = server.CONFIG['tts'].get('enabled')
        try:
            server.CONFIG['tts']['enabled'] = True
            server.STATE['ready'] = True
            with patch.object(server, 'greeting_cached', return_value=True), patch.object(server, 'request_engine') as engine, patch.object(server, 'synthesize') as synth:
                server.prepare_voice()
                engine.assert_not_called()
                synth.assert_not_called()
                self.assertTrue(server.STATE['ready'])
        finally:
            if enabled is None:
                server.CONFIG['tts'].pop('enabled', None)
            else:
                server.CONFIG['tts']['enabled'] = enabled
            server.STATE.clear()
            server.STATE.update(previous)

    def test_failed_greeting_save_does_not_disable_loaded_models(self):
        previous = dict(server.STATE)
        original = server.CONFIG['tts'].get('enabled')
        try:
            server.CONFIG['tts']['enabled'] = True
            server.STATE['ready'] = False
            with patch('pathlib.Path.is_file', return_value=True), patch.object(server, 'greeting_cached', return_value=False), patch.object(server, 'request_engine'), patch.object(server, 'synthesize', return_value=b'new'), patch.object(server, 'write_cache', side_effect=PermissionError('locked')):
                with self.assertRaises(PermissionError):
                    server.prepare_voice()
                self.assertTrue(server.STATE['ready'])
        finally:
            if original is None:
                server.CONFIG['tts'].pop('enabled', None)
            else:
                server.CONFIG['tts']['enabled'] = original
            server.STATE.clear()
            server.STATE.update(previous)


if __name__ == '__main__':
    unittest.main()
