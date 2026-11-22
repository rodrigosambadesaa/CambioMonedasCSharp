#!/usr/bin/env python3
"""Regression for destructive --input/--output collisions, including filesystem aliases."""
import pathlib
import subprocess
import sys
import tempfile

def main():
    if len(sys.argv) < 2:
        raise SystemExit("usage: test_batch_path_collision.py PROGRAM [ARG ...]")
    native = "--native" in sys.argv[1:]
    command = [str(pathlib.Path(part).resolve()) if pathlib.Path(part).exists() else part
               for part in sys.argv[1:] if part != "--native"]
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)
        (root / "monedas.txt").write_text("euro\n4\n3\n1\n", encoding="utf-8")
        (root / "stock.txt").write_text("euro\n4\n3\n2\n", encoding="utf-8")
        source = root / "batch.csv"
        source.write_text("mode,currency,amount,range\na,euro,6,2\n", encoding="utf-8")
        original = source.read_bytes()
        def check(label, inp, out):
            result = subprocess.run(command + ["--input", inp, "--output", out],
                                    capture_output=True, text=True, cwd=root, timeout=30)
            assert result.returncode != 0, (label, result.stdout, result.stderr)
            assert source.read_bytes() == original, (label, "input changed")
        check("same path", "batch.csv", "batch.csv")
        check("relative alias", "./batch.csv", "batch.csv")
        link = root / "alias.csv"
        try:
            link.symlink_to(source)
        except (OSError, NotImplementedError):
            pass
        else:
            check("symlink", "batch.csv", "alias.csv")
        # Hardlinks must be recognized on native C/C++ platforms; managed
        # wrappers may not expose native inode identity cross-platform.
        if native:
            hard = root / "hardlink.csv"
            hard.hardlink_to(source)
            check("hardlink", "batch.csv", "hardlink.csv")
        def check_log(label, output_path, log_path, expected_output=None):
            attempt = subprocess.run(
                command + ["--input", "batch.csv", "--output", output_path,
                           "--log", log_path],
                capture_output=True, text=True, cwd=root, timeout=30)
            assert attempt.returncode != 0, (
                label, attempt.stdout, attempt.stderr)
            assert source.read_bytes() == original, (label, "source changed")
            if expected_output is not None:
                assert (root / output_path).read_bytes() == expected_output, (
                    label, "output changed")

        check_log("input equals log", "out.csv", "batch.csv")
        check_log("relative input equals log", "out.csv", "./batch.csv")
        output_log = root / "shared-output.csv"
        output_log.write_bytes(b"ORIGINAL OUTPUT\n")
        check_log("output equals log", "shared-output.csv",
                  "shared-output.csv", b"ORIGINAL OUTPUT\n")
        check_log("relative output equals log", "shared-output.csv",
                  "./shared-output.csv", b"ORIGINAL OUTPUT\n")

        result = subprocess.run(command + ["--input", "batch.csv", "--output", "out.csv"],
                                cwd=root, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, (result.stdout, result.stderr)
        assert source.read_bytes() == original
        assert "OK total=2" in (root / "out.csv").read_text(encoding="utf-8")
        print("PASS: rejected identical/aliased files, preserved source, allowed separate output")

if __name__ == "__main__":
    main()
