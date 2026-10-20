#!/usr/bin/env python3
"""Real cross-language transaction test, no mocks, shared on-disk stock.

The C++ CLI and .NET CLI are separate processes and use the same temp
directory. A Python flock holder verifies they really honor the POSIX
protocol, and parallel mutations detect lost updates.
"""
import concurrent.futures
import fcntl
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

CPP = Path(sys.argv[1]).resolve()
DOTNET = Path(sys.argv[2]).resolve()
ACTION = ["--specific", "euro", "1,0,0", "0,0,5", "--specific-stock"]


def invoke(command, cwd):
    result = subprocess.run(command + ACTION, cwd=cwd,
                            capture_output=True, text=True, timeout=25)
    if result.returncode != 0 or "OK moneda=euro" not in result.stdout:
        raise AssertionError(
            f"{command[0]} failed exit={result.returncode}\n"
            f"stdout={result.stdout}\nstderr={result.stderr}"
        )
    return result.stdout


def read_stock(path):
    data = path.read_text(encoding="utf-8").split()
    assert data[0] == "euro" and len(data) == 4, data
    return tuple(0 if s == "-1" else int(s) for s in data[1:])


with tempfile.TemporaryDirectory(prefix="cambio-shared-lock-") as directory:
    root = Path(directory)
    (root / "monedas.txt").write_text("euro\n5\n3\n1\n", encoding="utf-8")
    stockfile = root / "stock.txt"
    stockfile.write_text("euro\n-1\n-1\n200\n", encoding="utf-8")
    lockfile = root / "stock.txt.lock"

    cpp = [str(CPP)]
    dotnet = ["dotnet", str(DOTNET)]
    # Demonstrate that BOTH independent executables wait for the same lock.
    with lockfile.open("a+b") as held:
        fcntl.flock(held, fcntl.LOCK_EX)
        first = subprocess.Popen(cpp + ACTION, cwd=root,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                 text=True)
        second = subprocess.Popen(dotnet + ACTION, cwd=root,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                  text=True)
        try:
            time.sleep(0.35)
            if first.poll() is not None:
                out, err = first.communicate(timeout=5)
                raise AssertionError(f"C++ ignored flock or exited: {first.returncode}: {out} / {err}")
            if second.poll() is not None:
                out, err = second.communicate(timeout=5)
                raise AssertionError(f"C# ignored flock or exited: {second.returncode}: {out} / {err}")
            assert read_stock(stockfile) == (0, 0, 200), "writes bypassed flock"
        finally:
            fcntl.flock(held, fcntl.LOCK_UN)
        for process in (first, second):
            out, err = process.communicate(timeout=25)
            assert process.returncode == 0 and "OK moneda=euro" in out, (
                process.returncode, out, err
            )
    assert read_stock(stockfile) == (2, 0, 190), "blocked operations lost updates"

    # Alternate clients concurrently; a racy read/modify/write loses changes.
    commands = [cpp, dotnet] * 16
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(invoke, command, root) for command in commands]
        for future in concurrent.futures.as_completed(futures):
            future.result()
    assert read_stock(stockfile) == (34, 0, 30), (
        "final stock must equal 34 successful exchanges", read_stock(stockfile)
    )

    # A failed operation from either app must leave the shared stock intact.
    for command in (cpp, dotnet):
        result = subprocess.run(
            command + ["--specific", "euro", "0,0,10", "0,0,5", "--specific-stock"],
            cwd=root, text=True, capture_output=True, timeout=20
        )
        assert result.returncode != 0, "unequal values accepted"
    assert read_stock(stockfile) == (34, 0, 30), "failed operation mutated stock"
print("PASS: native flock blocks both programs; 34 cross-language transactions, no lost updates.")
