"""Serve local Web builds without retaining files from earlier builds."""

import argparse
import json
from pathlib import Path
from urllib.error import HTTPError, URLError
from urllib.parse import urljoin, urlsplit
from urllib.request import Request, urlopen
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class PreviewHandler(SimpleHTTPRequestHandler):
    def llm_settings(self):
        with open(Path(self.directory) / 'web-game' / 'game.project.json', encoding='utf-8-sig') as file:
            return json.load(file).get('llm', {})

    def do_GET(self):
        if urlsplit(self.path).path != '/__web_api/capabilities':
            return super().do_GET()
        try:
            settings = self.llm_settings()
            body = json.dumps({'baseUrl': settings.get('baseUrl', ''),
                               'chatCompletionsPath': settings.get('chatCompletionsPath', '/v1/chat/completions'),
                               'endpoint': '/__web_api/llm'}).encode()
            self.send_response(200)
            self.send_header('Content-Type', 'application/json')
            self.send_header('Content-Length', str(len(body)))
            self.end_headers()
            self.wfile.write(body)
        except (OSError, ValueError):
            self.send_error(404, 'No configured Web game API')

    def do_POST(self):
        if urlsplit(self.path).path != '/__web_api/llm':
            return self.send_error(404)
        try:
            settings = self.llm_settings()
            target = urljoin(settings['baseUrl'].rstrip('/') + '/',
                             settings.get('chatCompletionsPath', '/v1/chat/completions').lstrip('/'))
            data = self.rfile.read(int(self.headers.get('Content-Length', '0')))
            headers = {'Content-Type': self.headers.get('Content-Type', 'application/json')}
            if self.headers.get('Authorization'):
                headers['Authorization'] = self.headers['Authorization']
            request = Request(target, data=data, headers=headers, method='POST')
            try:
                upstream = urlopen(request, timeout=max(30, settings.get('timeoutSeconds', 300)))
            except HTTPError as error:
                upstream = error
            with upstream:
                self.send_response(upstream.status)
                self.send_header('Content-Type', upstream.headers.get('Content-Type', 'application/json'))
                self.send_header('Connection', 'close')
                self.end_headers()
                self.close_connection = True
                while chunk := upstream.read1(8192):
                    self.wfile.write(chunk)
                    self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError):
            pass
        except (OSError, ValueError, KeyError, URLError) as error:
            self.send_error(502, 'Configured LLM API request failed: ' + str(error))

    def end_headers(self):
        self.send_header("Cache-Control", "no-store, max-age=0")
        super().end_headers()

    def send_head(self):
        # Old browser entries may already have a long-lived freshness policy.
        # Return the current file even when they send a conditional request.
        if "If-Modified-Since" in self.headers:
            del self.headers["If-Modified-Since"]
        return super().send_head()


class PreviewServer(ThreadingHTTPServer):
    # Browser startup requests many framework and model files concurrently.
    request_queue_size = 128


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=5052)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--directory", required=True)
    args = parser.parse_args()
    handler = partial(PreviewHandler, directory=args.directory)
    with PreviewServer((args.bind, args.port), handler) as server:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
