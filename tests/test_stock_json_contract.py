#!/usr/bin/env python3
"""Validate the same lossless stock export contract for the C, C++ and C# ports."""
import json
import re
import subprocess
import sys
import tempfile
from pathlib import Path


def blocks(path: Path):
    result = {}
    active = None
    for token in path.read_text(encoding="utf-8").split():
        if re.fullmatch(r"-?[0-9]+", token):
            if active is None:
                raise AssertionError(f"Orphan numeric token in {path}")
            result[active].append(0 if token == "-1" and path.name == "stock.txt" else int(token))
        else:
            if token in result:
                raise AssertionError(f"Repeated currency {token}")
            active = token
            result[active] = []
    return result


def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: test_stock_json_contract.py EXECUTABLE [ARG ...]")
    denominations = blocks(Path("monedas.txt"))
    stocks = blocks(Path("stock.txt"))
    assert denominations and denominations.keys() == stocks.keys(), "Currency keys mismatch"
    with tempfile.TemporaryDirectory(prefix="progvoraz-json-") as temp:
        output = Path(temp) / "stock.json"
        command = [*sys.argv[1:], "--export-stock-json", str(output)]
        result = subprocess.run(command, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, (command, result.stdout, result.stderr)
        # Parse strictly: duplicate JSON object keys are not allowed.
        def unique_keys(pairs):
            object_value = {}
            for key, value in pairs:
                if key in object_value:
                    raise ValueError(f"Duplicated JSON key: {key}")
                object_value[key] = value
            return object_value
        data = json.loads(output.read_text(encoding="utf-8"), object_pairs_hook=unique_keys)
        assert isinstance(data, dict) and set(data) == {"monedas"}, data
        assert isinstance(data["monedas"], list), data
        actual = {}
        for entry in data["monedas"]:
            assert isinstance(entry, dict) and set(entry) == {"nombre", "denominaciones", "stock"}, entry
            name = entry["nombre"]
            assert isinstance(name, str) and name not in actual, name
            assert isinstance(entry["denominaciones"], list) and isinstance(entry["stock"], list)
            assert all(isinstance(v, (str, int)) and not isinstance(v, bool) for v in entry["denominaciones"]), name
            assert all(isinstance(v, (str, int)) and not isinstance(v, bool) for v in entry["stock"]), name
            actual[name] = (
                [int(str(value)) for value in entry["denominaciones"]],
                [int(str(value)) for value in entry["stock"]],
            )
        assert set(actual) == set(denominations), (set(actual), set(denominations))
        for name, (d, s) in actual.items():
            assert d == denominations[name], f"Denominations changed for {name}"
            assert s == stocks[name], f"Stock changed for {name}"
            assert d and len(d) == len(s) and all(x > 0 for x in d)
            assert all(x >= 0 for x in s)
        print(f"Verified lossless stock JSON export for {len(actual)} currencies")


if __name__ == "__main__":
    main()
