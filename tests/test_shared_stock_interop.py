#!/usr/bin/env python3
"""Exercise real C++ and C# processes sharing one stock.txt, across platforms."""
import concurrent.futures
import contextlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time

if os.name != "nt":
    import fcntl

CPP = Path(sys.argv[1]).resolve()
DOTNET = Path(sys.argv[2]).resolve()
ACTION = ["--specific", "euro", "1,0,0", "0,0,5", "--specific-stock"]


@contextlib.contextmanager
def exclusive_lock(path):
    if os.name != "nt":
        with path.open("a+b") as handle:
            fcntl.flock(handle, fcntl.LOCK_EX)
            try:
                yield
            finally:
                fcntl.flock(handle, fcntl.LOCK_UN)
        return

    import ctypes
    from ctypes import wintypes
    kernel = ctypes.WinDLL("kernel32", use_last_error=True)
    create = kernel.CreateFileW
    create.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
                       ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD,
                       wintypes.HANDLE]
    create.restype = wintypes.HANDLE
    close = kernel.CloseHandle
    close.argtypes = [wintypes.HANDLE]
    close.restype = wintypes.BOOL
    handle = create(str(path), 0xC0000000, 0, None, 4, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        raise OSError(ctypes.get_last_error(), "CreateFileW exclusive failed")
    try:
        yield
    finally:
        assert close(handle), "CloseHandle failed"


def invoke(command, cwd):
    result = subprocess.run(command + ACTION, cwd=cwd,
                            capture_output=True, text=True, timeout=30)
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

    # BOTH programs must block, not only when the other program owns the lock.
    with exclusive_lock(lockfile):
        first = subprocess.Popen(cpp + ACTION, cwd=root,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                 text=True)
        second = subprocess.Popen(dotnet + ACTION, cwd=root,
                                  stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                  text=True)
        time.sleep(0.35)
        for label, process in (("C++", first), ("C#", second)):
            if process.poll() is not None:
                out, err = process.communicate(timeout=5)
                raise AssertionError(
                    f"{label} ignored exclusive lock or exited {process.returncode}: {out} / {err}"
                )
        assert read_stock(stockfile) == (0, 0, 200), "writes bypassed lock"
    for process in (first, second):
        out, err = process.communicate(timeout=30)
        assert process.returncode == 0 and "OK moneda=euro" in out, (
            process.returncode, out, err
        )
    assert read_stock(stockfile) == (2, 0, 190), "held operations lost updates"

    commands = [cpp, dotnet] * 16
    with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
        futures = [pool.submit(invoke, command, root) for command in commands]
        for future in concurrent.futures.as_completed(futures):
            future.result()
    assert read_stock(stockfile) == (34, 0, 30), (
        "34 independent transactions must not lose writes", read_stock(stockfile)
    )

    for command in (cpp, dotnet):
        result = subprocess.run(
            command + ["--specific", "euro", "0,0,10", "0,0,5", "--specific-stock"],
            cwd=root, text=True, capture_output=True, timeout=30
        )
        assert result.returncode != 0, "invalid unequal-value exchange accepted"
    assert read_stock(stockfile) == (34, 0, 30), "rejected transaction changed stock"
print("PASS: shared inventory lock and 34 cross-language exchanges; no lost updates.")
