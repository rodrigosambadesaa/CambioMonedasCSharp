#!/usr/bin/env python3
"""Isolated interactive exchange tests on live, temporary stock.txt."""
import os
from pathlib import Path
import subprocess
import sys
import tempfile

def run(cmd, directory, lines, expected):
    env=os.environ.copy()
    env.pop("PROGVORAZ_JSON",None)
    result=subprocess.run(cmd,input="\n".join(lines)+"\n",cwd=directory,env=env,
        text=True,encoding="utf-8",errors="replace",capture_output=True,timeout=40)
    output=result.stdout+"\n"+result.stderr
    assert result.returncode==0,output[-4000:]
    assert expected.casefold() in output.casefold(),output[-4000:]

def balance(directory):
    words=(directory/"stock.txt").read_text(encoding="utf-8").split()
    assert len(words)==3 and words[0]=="euro",words
    return tuple(int(n) for n in words[1:])

def main():
    args=sys.argv[1:]
    assert args
    if args[0]=="dotnet":
        args[1]=str(Path(args[1]).resolve())
    else:
        args[0]=str(Path(args[0]).resolve())
    ok=("Cambio específico correcto. Valor=100" if args[0]=="dotnet"
        else "OK moneda=euro total_centimos=100")
    error=("Cambio específico rechazado" if args[0]=="dotnet"
           else "Cambio especifico rechazado")
    with tempfile.TemporaryDirectory(prefix="progvoraz-menu-") as tmp:
        path=Path(tmp)
        (path/"monedas.txt").write_text("euro\n100\n50\n",encoding="utf-8")
        inv=path/"stock.txt"
        inv.write_text("euro\n2\n4\n",encoding="utf-8")
        run(args,path,["a","euro","especifico","1,0","0,2","salir"],ok)
        assert balance(path)==(2,4),"unlimited exchange mutated inventory"
        run(args,path,["b","euro","especifico","1,0","0,2","salir"],ok)
        assert balance(path)==(3,2),"wrong committed balance"
        run(args,path,["b","euro","especifico","1,0","0,1","salir"],error)
        assert balance(path)==(3,2),"unequal amounts mutated inventory"
        inv.write_text("euro\n0\n1\n",encoding="utf-8")
        run(args,path,["b","euro","especifico","1,0","0,2","salir"],error)
        assert balance(path)==(0,1),"stock deficit was overdrawn"
        run(args,path,["b","euro","especifico","1","0,2","salir"],error)
        assert balance(path)==(0,1),"invalid vector mutated inventory"
    print("PASS: five isolated currency-exchange cases")

if __name__=="__main__":
    main()
