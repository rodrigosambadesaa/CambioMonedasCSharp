#!/usr/bin/env python3
"""Smoke-test the C# portable panel without writing to repository stock.txt."""
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from test_stock_json_contract import blocks


def run(dll: Path, directory: Path, stdin: str):
    result = subprocess.run(
        ["dotnet", str(dll), "--gui"], input=stdin, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE, cwd=directory, timeout=25,
    )
    assert result.returncode == 0, (result.returncode, result.stdout, result.stderr)
    return result.stdout


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: test_portable_gui.py path/to/ProgVorazCs.Console.dll")
    dll = Path(sys.argv[1]).resolve(strict=True)
    with tempfile.TemporaryDirectory(prefix="progvoraz-gui-") as name:
        root = Path(name)
        for file in ("monedas.txt", "stock.txt", "rates_stub.json"):
            shutil.copy2(file, root / file)
        initial = blocks(root / "stock.txt")
        stdout = run(dll, root, "ilimitado\neuro\ncalcular\n6\nvolver\nsalir\n")
        assert "Cambio calculado: 6 centimos" in stdout, stdout
        assert blocks(root / "stock.txt") == initial, "Unlimited mode modified stock"

        stdout = run(dll, root, "limitado\neuro\ncalcular\n1\nvolver\nsalir\n")
        assert "Cambio calculado: 1 centimos" in stdout, stdout
        after = blocks(root / "stock.txt")
        for coin, before in initial.items():
            expected = list(before)
            if coin == "euro":
                expected[-1] -= 1
            assert after[coin] == expected, (coin, before, after[coin])

        stdout = run(dll, root, "limitado\neuro\ncalcular\nincorrecto\n")
        assert "Importe invalido." in stdout, stdout
        assert blocks(root / "stock.txt") == after, "Invalid input modified stock"
        print("C# portable GUI: unlimited, limited, invalid input and EOF passed")


if __name__ == "__main__":
    main()
