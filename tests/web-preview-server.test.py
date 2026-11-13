import functools
import http.client
import importlib.util
import json
from pathlib import Path
import tempfile
import threading
import unittest
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

spec = importlib.util.spec_from_file_location('preview', Path(__file__).parents[1] / 'scripts/web-preview-server.py')
preview = importlib.util.module_from_spec(spec)
spec.loader.exec_module(preview)


class ApiProxyTest(unittest.TestCase):
    def test_configured_api_streams_before_completion_and_preserves_authorization(self):
        finish = threading.Event()
        requests = []

        class Upstream(BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def do_POST(self):
                requests.append((self.path, self.headers.get('Authorization'), json.loads(self.rfile.read(int(self.headers['Content-Length'])))))
                self.send_response(200)
                self.send_header('Content-Type', 'text/event-stream')
                self.end_headers()
                self.wfile.write(b'data: {"choices":[]}\n\n')
                self.wfile.flush()
                finish.wait(3)
                self.wfile.write(b'data: [DONE]\n\n')

        upstream = ThreadingHTTPServer(('127.0.0.1', 0), Upstream)
        threading.Thread(target=upstream.serve_forever, daemon=True).start()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'web-game').mkdir()
            (root / 'web-game/game.project.json').write_text(json.dumps({'llm': {
                'baseUrl': f'http://127.0.0.1:{upstream.server_port}/api', 'apiKey': 'private-config-key'}}))
            handler = functools.partial(preview.PreviewHandler, directory=directory)
            server = preview.PreviewServer(('127.0.0.1', 0), handler)
            threading.Thread(target=server.serve_forever, daemon=True).start()
            try:
                connection = http.client.HTTPConnection('127.0.0.1', server.server_port, timeout=3)
                connection.request('GET', '/__web_api/capabilities')
                capabilities = connection.getresponse().read()
                self.assertNotIn(b'private-config-key', capabilities)
                self.assertEqual(json.loads(capabilities)['endpoint'], '/__web_api/llm')
                connection.request('POST', '/__web_api/llm', body='{"model":"fixture"}', headers={'Authorization': 'Bearer request-key'})
                response = connection.getresponse()
                self.assertEqual(response.status, 200)
                self.assertEqual(response.readline(), b'data: {"choices":[]}\n')
                self.assertFalse(finish.is_set())
                finish.set()
                self.assertIn(b'[DONE]', response.read())
                self.assertEqual(requests, [('/api/v1/chat/completions', 'Bearer request-key', {'model': 'fixture'})])
                connection.request('POST', '/__web_api/arbitrary-target', body='{}')
                self.assertEqual(connection.getresponse().status, 404)
                connection.close()
            finally:
                finish.set()
                server.shutdown()
                server.server_close()
                upstream.shutdown()
                upstream.server_close()


if __name__ == '__main__':
    unittest.main()
