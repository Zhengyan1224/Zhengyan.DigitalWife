"""Serve local Web builds without retaining files from earlier builds."""

import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class PreviewHandler(SimpleHTTPRequestHandler):
    def end_headers(self):
        self.send_header("Cache-Control", "no-store, max-age=0")
        super().end_headers()

    def send_head(self):
        # Old browser entries may already have a long-lived freshness policy.
        # Return the current file even when they send a conditional request.
        if "If-Modified-Since" in self.headers:
            del self.headers["If-Modified-Since"]
        return super().send_head()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=5052)
    parser.add_argument("--bind", default="127.0.0.1")
    parser.add_argument("--directory", required=True)
    args = parser.parse_args()
    handler = partial(PreviewHandler, directory=args.directory)
    with ThreadingHTTPServer((args.bind, args.port), handler) as server:
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
