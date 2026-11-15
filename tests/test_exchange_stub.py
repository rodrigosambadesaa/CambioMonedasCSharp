#!/usr/bin/env python3
"""Deterministic exchange parity: positive, identity, reverse and absent stub rates."""
import os
import re
import subprocess
import sys

if len(sys.argv) < 2:
    raise SystemExit("usage: test_exchange_stub.py EXECUTABLE [ARG ...]")

base = sys.argv[1:]
env = {**os.environ, "PROGVORAZ_EXCHANGE_SOURCE": "stub", "PROGVORAZ_LANG": "es"}

def invoke(*args):
    return subprocess.run([*base, "--convert", *args], capture_output=True, text=True,
                          env=env, timeout=30)

for arguments, expected in [
    (("EUR", "USD", "100"), 109),
    (("USD", "EUR", "109"), 100),
    (("EUR", "EUR", "100"), 100),
]:
    result = invoke(*arguments)
    assert result.returncode == 0, (arguments, result.stdout, result.stderr)
    found = re.search(r"Destino=(\d+)", result.stdout)
    assert found and int(found.group(1)) == expected, (arguments, result.stdout, result.stderr)

result = invoke("EUR", "JPY", "100")
assert result.returncode != 0, "An absent EUR/JPY rate must never be fabricated."
print("PASS: stub conversion, exact reverse rounding, identity, no invented exchange rate")
