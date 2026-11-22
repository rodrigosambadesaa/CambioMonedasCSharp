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

        # Protect persistent data, not just the CSV source being processed.
        protected = {
            "stock.txt": (root / "stock.txt").read_bytes(),
            "monedas.txt": (root / "monedas.txt").read_bytes(),
            "historial.txt": b"HISTORY-UNIQUE\n",
            "stock.txt.lock": b"LOCK-UNIQUE\n",
        }
        (root / "historial.txt").write_bytes(protected["historial.txt"])
        (root / "stock.txt.lock").write_bytes(protected["stock.txt.lock"])

        for destination in protected:
            for flag in ("--output", "--log"):
                arguments = ["--input", "batch.csv", flag, destination]
                result = subprocess.run(command + arguments, cwd=root,
                                        capture_output=True, text=True, timeout=30)
                assert result.returncode != 0, (
                    "operational target accepted", flag, destination,
                    result.stdout, result.stderr)
                for name, original_content in protected.items():
                    assert (root / name).read_bytes() == original_content, (
                        flag, destination, name, "operational file changed")

        for kind in ("symlink", "hardlink"):
            sensitive_alias = root / ("stock-" + kind)
            try:
                if kind == "symlink":
                    sensitive_alias.symlink_to(root / "stock.txt")
                else:
                    sensitive_alias.hardlink_to(root / "stock.txt")
            except (OSError, NotImplementedError):
                continue
            for flag in ("--output", "--log"):
                result = subprocess.run(
                    command + ["--input", "batch.csv", flag, sensitive_alias.name],
                    cwd=root, capture_output=True, text=True, timeout=30)
                # Hardlink-aware platforms should reject; staged writes may
                # safely replace the alias entry without modifying source.
                if kind == "symlink" or native:
                    assert result.returncode != 0, (
                        flag, kind, result.stdout, result.stderr)
                assert (root / "stock.txt").read_bytes() == protected["stock.txt"], (
                    flag, kind, "stock changed")
            if sensitive_alias.exists() or sensitive_alias.is_symlink():
                sensitive_alias.unlink()

        result = subprocess.run(command + ["--input", "batch.csv", "--output", "out.csv"],
                                cwd=root, capture_output=True, text=True, timeout=30)
        assert result.returncode == 0, (result.stdout, result.stderr)
        assert source.read_bytes() == original
        assert "OK total=2" in (root / "out.csv").read_text(encoding="utf-8")
        print("PASS: rejected identical/aliased files, preserved source, allowed separate output")

if __name__ == "__main__":
    main()
