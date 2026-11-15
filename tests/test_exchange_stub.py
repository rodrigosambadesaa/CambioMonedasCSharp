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


# Values larger than signed 64-bit and cent values requiring exact half-up
# rounding. Python integers are the independent reference oracle.
def rounded(numerator, denominator, amount):
    return (amount * numerator * 2 + denominator) // (2 * denominator)

for source, target, amount, numerator, denominator in [
    ("EUR", "USD", 50, 109, 100),       # exact 54.5 -> 55
    ("EUR", "USD", 1, 109, 100),
    ("USD", "EUR", 50, 917, 1000),
    ("EUR", "USD", 0, 109, 100),
    ("EUR", "EUR", 10**45 + 123, 1, 1),
    ("EUR", "USD", 10**40 + 12345, 109, 100),
    ("USD", "EUR", 10**40 + 125, 917, 1000),
]:
    outcome = invoke(source, target, str(amount))
    assert outcome.returncode == 0, (source, target, amount, outcome.stdout, outcome.stderr)
    match = re.search(r"Destino=(\d+)", outcome.stdout)
    expected = rounded(numerator, denominator, amount)
    assert match is not None and int(match.group(1)) == expected, (
        source, target, amount, expected, outcome.stdout, outcome.stderr
    )

for invalid in ("-1", "+5", "1.5", "1e10", "nonnumeric", "123garbage"):
    outcome = invoke("EUR", "USD", invalid)
    assert outcome.returncode != 0, ("Malformed integer accepted", invalid, outcome.stdout)

missing_amount = subprocess.run([*base, "--convert", "EUR", "USD"],
                                capture_output=True, text=True, env=env, timeout=30)
assert missing_amount.returncode != 0, "Missing --convert amount should be rejected."

result = invoke("EUR", "JPY", "100")
assert result.returncode != 0, "An absent EUR/JPY rate must never be fabricated."
print("PASS: decimal stub conversions, 64-bit overflow regressions, half-up rounding and strict inputs")
