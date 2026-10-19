#!/usr/bin/env python3
"""Compare the native port to a pinned, separately built C reference.

Unlike self-comparison, this uses the original executable as the oracle.
No data files are modified and neither language invokes the C binary at runtime.
"""
import csv
import io
import os
from pathlib import Path
import subprocess
import sys

REFERENCE = Path(sys.argv[1]).resolve()
PORT = Path(sys.argv[2]).resolve()
KIND = sys.argv[3] if len(sys.argv) > 3 else "cpp"

NAMES = (
    "euro", "dolar", "yen", "libra", "franco_suizo",
    "dolar_canadiense", "dolar_australiano", "yuan_chino", "peso_mexicano",
)
AMOUNTS = (0, 1, 6, 19, 73, 127, 500, 1500)
ROWS = [("a", name, str(amount), "") for name in NAMES for amount in AMOUNTS]
ROWS += [("b", name, str(amount), "") for name in NAMES for amount in AMOUNTS]
ROWS += [
    ("a", "euro", "6", "2-3"),
    ("a", "euro", "6", "1-10"),
    ("b", "euro", "6", "1-10"),
    ("a", "dolar", "12345", "1-100"),
    ("a", "franco_suizo", "73", "2-50"),
    ("a", "euro", "invalid", ""),
    ("a", "unknown", "10", ""),
]
out = io.StringIO()
writer = csv.writer(out, lineterminator="\n")
writer.writerow(("mode", "moneda", "monto", "range"))
writer.writerows(ROWS)
fixture = out.getvalue()


def execute(command, directory):
    result = subprocess.run(
        command, input=fixture, text=True, capture_output=True,
        cwd=directory, timeout=120,
        env={**os.environ, "PROGVORAZ_LANG": "en"},
        check=False,
    )
    if result.returncode != 0:
        raise AssertionError(
            f"{command!r} failed ({result.returncode}): {result.stderr[-2000:]}"
        )
    parsed = list(csv.DictReader(io.StringIO(result.stdout)))
    if len(parsed) != len(ROWS):
        raise AssertionError(
            f"{command!r} produced {len(parsed)} rows, expected {len(ROWS)}.\n"
            f"stdout={result.stdout[:1000]!r}; stderr={result.stderr[:1000]!r}"
        )
    return parsed


reference_binary = REFERENCE / "progvoraz"
original = execute([str(reference_binary), "--stream", "--lang", "en"], REFERENCE)
if KIND == "cpp":
    executable = PORT / "progvoraz"
    native = execute([str(executable), "--stream", "--lang", "en"], PORT)
elif KIND == "csharp":
    native = execute(
        ["dotnet", "run", "--no-build", "-c", "Release",
         "--project", "ProgVorazCs.Console", "--",
         "--stream", "--lang", "en"], PORT
    )
else:
    raise ValueError("Unknown language: " + KIND)

differences = []
for index, (expected, actual) in enumerate(zip(original, native)):
    # The C reference and the new engines deliberately keep a compatible
    # batch result/status. Formatting details in explanatory notes may differ.
    for field in ("currency", "amount", "result"):
        if actual[field] != expected[field]:
            differences.append(
                f"row {index + 1}: {ROWS[index]!r} field {field}: "
                f"C={expected[field]!r}, {KIND}={actual[field]!r}"
            )
if differences:
    print("\n".join(differences[:30]), file=sys.stderr)
    raise SystemExit(f"{len(differences)} real divergences against C reference")
print(f"PASS: {len(ROWS)} C vs {KIND} batch cases "
      f"({len(NAMES)} currencies; unlimited, stock, constraints and errors)")
