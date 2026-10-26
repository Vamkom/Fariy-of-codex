import json
from pathlib import Path
import sys
import threading
import unittest
import urllib.error
import urllib.request
from http.server import ThreadingHTTPServer

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'app'))
import server as bridge


class LocalApiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.server = ThreadingHTTPServer(('127.0.0.1', 0), bridge.Handler)
        cls.thread = threading.Thread(target=cls.server.serve_forever, daemon=True)
        cls.thread.start()
        cls.opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    @classmethod
    def tearDownClass(cls):
        cls.server.shutdown()
        cls.server.server_close()

    def request(self, path, *, token=None, origin=None, method=None):
        headers = {'Host': '127.0.0.1:18742'}
        if token is not None:
            headers['X-Fairy-Token'] = token
        if origin:
            headers['Origin'] = origin
        return self.opener.open(urllib.request.Request(f'http://127.0.0.1:{self.server.server_port}' + path,
                                                     headers=headers, method=method), timeout=3)

    def test_cross_origin_status_refused(self):
        with self.assertRaises(urllib.error.HTTPError) as result:
            self.request('/api/status', origin='https://untrusted.invalid')
        self.assertEqual(result.exception.code, 403)

    def test_configuration_not_accessible_without_token(self):
        with self.assertRaises(urllib.error.HTTPError) as result:
            self.request('/api/config')
        self.assertEqual(result.exception.code, 403)

    def test_own_status_and_empty_queue(self):
        with self.request('/api/status') as response:
            self.assertTrue(json.load(response)['instance'])
        with self.request('/api/audio', token=bridge.CONFIG['token']) as response:
            self.assertEqual(json.load(response)['items'], [])


if __name__ == '__main__':
    unittest.main()
