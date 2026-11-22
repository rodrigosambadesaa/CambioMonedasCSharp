#!/usr/bin/env python3
"""Ensure export and backup destinations cannot destroy live currency data."""
import pathlib
import subprocess
import sys
import tempfile

def main():
    if len(sys.argv) < 2:
        raise SystemExit("Usage: test_protected_exports.py COMMAND...")
    command = [str(pathlib.Path(x).resolve()) if pathlib.Path(x).exists() else x
               for x in sys.argv[1:]]
    with tempfile.TemporaryDirectory() as tmp:
        root = pathlib.Path(tmp)
        originals = {
            "monedas.txt": b"euro\n4\n3\n1\ndolar\n5\n1\n",
            "stock.txt": b"euro\n8\n8\n8\ndolar\n9\n9\n",
            "historial.txt": b"UNIQUE-HISTORY-DATA\n",
            "stock.txt.lock": b"UNIQUE-LOCK-DATA\n",
        }
        for name, content in originals.items():
            (root / name).write_bytes(content)
        for flag in ("--export-stock-json", "--export-report", "--snapshot-stock"):
            for name in originals:
                arguments = [flag, name]
                outcome = subprocess.run(command + arguments, cwd=root,
                                         capture_output=True, text=True, timeout=30)
                assert outcome.returncode != 0, (flag, name, outcome.stdout,
                                                   outcome.stderr)
                for key, original in originals.items():
                    assert (root / key).read_bytes() == original, (
                        flag, name, key, "operative file damaged")

            for alias_kind in ("symlink", "hardlink"):
                alias = root / ("alias-" + alias_kind)
                try:
                    if alias_kind == "symlink":
                        alias.symlink_to(root / "stock.txt")
                    else:
                        alias.hardlink_to(root / "stock.txt")
                except (OSError, NotImplementedError):
                    continue
                outcome = subprocess.run(command + [flag, alias.name],
                                         cwd=root, capture_output=True, text=True,
                                         timeout=30)
                # An implementation may reject the alias or safely replace only
                # its directory entry. The actual stock file must never change.
                assert (root / "stock.txt").read_bytes() == originals["stock.txt"], (
                    flag, alias_kind, outcome.returncode)
                if alias.is_symlink() or alias.exists():
                    alias.unlink()

        for flag, target in (("--export-stock-json", "safe.json"),
                             ("--export-report", "safe-report.txt"),
                             ("--snapshot-stock", "safe-backup.txt")):
            result = subprocess.run(command + [flag, target], cwd=root,
                                    capture_output=True, text=True, timeout=30)
            assert result.returncode == 0, (flag, result.stdout, result.stderr)
            assert (root / target).is_file() and (root / target).stat().st_size > 0
        print("PASS: operational files preserved for exports/backups and aliases")

if __name__ == "__main__":
    main()
