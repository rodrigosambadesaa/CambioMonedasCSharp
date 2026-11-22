#!/usr/bin/env python3
"""A stalled local HTTP client must not block the next healthy request forever."""
import os
from pathlib import Path
import socket
import subprocess
import sys
import tempfile
import time

def choose_port():
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]

def main():
    if len(sys.argv) < 2:
        raise SystemExit("Usage: test_http_stall.py EXECUTABLE [args...]")
    command = [str(Path(part).resolve()) if Path(part).exists() else part
               for part in sys.argv[1:]]
    with tempfile.TemporaryDirectory(prefix="progvoraz-stall-") as tmp:
        root = Path(tmp)
        (root / "monedas.txt").write_text("euro\n4\n3\n1\n", encoding="utf-8")
        (root / "stock.txt").write_text("euro\n5\n5\n5\n", encoding="utf-8")
        port = choose_port()
        proc = subprocess.Popen([*command, "--server", "--port", str(port)],
                                cwd=root, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, env=os.environ.copy())
        try:
            deadline = time.monotonic() + 10
            while True:
                if proc.poll() is not None:
                    out, err = proc.communicate(timeout=2)
                    raise AssertionError(f"server exited {proc.returncode} {out!r} {err!r}")
                try:
                    probe = socket.create_connection(("127.0.0.1", port), timeout=0.3)
                    probe.close()
                    break
                except OSError:
                    if time.monotonic() >= deadline:
                        raise AssertionError("server did not start")
                    time.sleep(0.1)
            with socket.create_connection(("127.0.0.1", port), timeout=3) as stalled:
                stalled.sendall(b"POST /batch HTTP/1.1\r\nHost: localhost\r\n"
                                b"Content-Length: 100\r\n\r\n")
                time.sleep(0.2)
                start = time.monotonic()
                with socket.create_connection(("127.0.0.1", port), timeout=3) as healthy:
                    healthy.settimeout(9)
                    healthy.sendall(b"GET /health HTTP/1.1\r\nHost: localhost\r\n"
                                    b"Connection: close\r\n\r\n")
                    response = b""
                    while True:
                        data = healthy.recv(8192)
                        if not data:
                            break
                        response += data
                duration = time.monotonic() - start
                assert b"HTTP/1.1 200" in response or b"HTTP/1.0 200" in response, response
                assert response.rsplit(b"\r\n\r\n", 1)[-1] == b"OK", response
                assert duration < 9, f"Healthy client starved for {duration:.1f}s"
                print(f"PASS: stalled POST expired; healthy GET succeeded in {duration:.2f}s")
        finally:
            proc.terminate()
            try:
                proc.communicate(timeout=3)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.communicate(timeout=3)

if __name__ == "__main__":
    main()
