#!/usr/bin/env python3
"""EOF in each interactive menu must terminate without an infinite loop."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

def main():
    executable=[sys.argv[1],str(Path(sys.argv[2]).resolve())]
    examples=[
        ("main menu",[]),
        ("currency selection",["a"]),
        ("change amount",["a","euro"]),
        ("administrator action",["c","euro"]),
        ("administrator denomination",["c","euro","anadir"]),
        ("administrator quantity",["c","euro","anadir","1"]),
        ("specific input",["b","euro","especifico"])
    ]
    with tempfile.TemporaryDirectory(prefix="progvoraz-cs-eof-") as temporary:
        path=Path(temporary)
        (path/"monedas.txt").write_text("euro\n100\n50\n",encoding="utf-8")
        (path/"stock.txt").write_text("euro\n2\n4\n",encoding="utf-8")
        for scenario,lines in examples:
            env=os.environ.copy()
            env.pop("PROGVORAZ_JSON",None)
            process=subprocess.run(executable,input="\n".join(lines)+("\n" if lines else ""),
                cwd=path,env=env,text=True,encoding="utf-8",errors="replace",
                capture_output=True,timeout=10)
            assert process.returncode==0,(scenario,process.stdout,process.stderr)
            assert "Gracias por utilizar" in process.stdout,(scenario,process.stdout)
    print("PASS: EOF terminates all seven native interactive console states")

if __name__=="__main__":
    main()
