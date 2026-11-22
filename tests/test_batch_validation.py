#!/usr/bin/env python3
"""Cross-language CSV contract: modes, exact/ranged coin limits and rejection paths."""
import csv
import io
from pathlib import Path
import subprocess
import sys
import tempfile

CASES = [
    ("a", "6", "=2", "OK total=2"),
    ("A", "6", "=2", "OK total=2"),
    ("b", "6", "=2", "OK total=2"),
    ("c", "6", "=2", "OK total=2"),
    ("", "6", "=2", "OK total=2"),
    ("a", "6", "2-2", "OK total=2"),
    ("a", "6", "0-2", "OK total=2"),
    ("a", "6", "2", "OK total=2"),
    ("a", "6", "=1", "NO"),
    ("a", "6", "0", "NO"),
    ("a", "6", "=0", "NO"),
    ("a", "6", "2garbage", "ERROR"),
    ("a", "6", "+2", "ERROR"),
    ("a", "6", "-1", "ERROR"),
    ("a", "6", "1-", "ERROR"),
    ("a", "6", "1-0", "ERROR"),
    ("a", "6", "1-2-3", "ERROR"),
    ("a", "6", "=2-3", "ERROR"),
    ("a", "6", "=", "ERROR"),
    ("a", "6", "9999999999999999999999999999999", "ERROR"),
    ("x", "6", "2", "ERROR"),
    ("ac", "6", "2", "ERROR"),
    ("a", "x", "=2", "ERROR"),
]
def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: test_batch_validation.py EXECUTABLE [ARG ...]")
    command = [str(Path(part).resolve()) if Path(part).exists() else part
               for part in sys.argv[1:]] + ["--stream"]
    with tempfile.TemporaryDirectory(prefix="progvoraz-batch-validation-") as tmp:
        root = Path(tmp)
        (root / "monedas.txt").write_text("euro\n4\n3\n1\ndolar\n5\n1\n", encoding="utf-8")
        (root / "stock.txt").write_text("euro\n5\n5\n5\ndolar\n6\n6\n", encoding="utf-8")
        rows = [("mode", "currency", "amount", "range")]
        rows += [(mode, "euro", amount, limit) for mode, amount, limit, _ in CASES]
        buf = io.StringIO()
        csv.writer(buf, lineterminator="\n").writerows(rows)
        result = subprocess.run(command, input=buf.getvalue(), cwd=root,
                                capture_output=True, text=True, timeout=90)
        assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
        table = list(csv.reader(io.StringIO(result.stdout)))
        assert len(table) == len(CASES) + 1, (len(table), len(CASES), result.stdout)
        assert len(table[0]) == 5, table[0]
        for i, (mode, amount, limit, expected) in enumerate(CASES, 1):
            row = table[i]
            assert len(row) == 5, (i, row)
            assert row[3] == expected or (expected == "ERROR" and row[3] == "ERROR"), (
                f"case {i}: mode={mode!r}, amount={amount!r}, range={limit!r}, "
                f"expected {expected!r}, actual {row!r}"
            )
        assert (root / "stock.txt").read_text(encoding="utf-8") == (
            "euro\n5\n5\n5\ndolar\n6\n6\n"
        ), "CSV calculations unexpectedly mutated inventory"
        print(f"PASS: {len(CASES)} CSV rows, exact/range/mode contracts, untouched inventory")

if __name__ == "__main__":
    main()
