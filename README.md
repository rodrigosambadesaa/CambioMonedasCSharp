# ProgVoraz C#

Migracion de ProgVoraz a C# con:

- `ProgVorazCs.Core`: logica de monedas, stock y algoritmo voraz.
- `ProgVorazCs.Console`: aplicacion de consola (modos `a`, `b`, `c`).
- `ProgVorazCs.Gui`: interfaz grafica WinForms para Windows.

## Requisitos

- .NET SDK 10.0+
- `monedas.txt` y `stock.txt` en la raiz del repositorio.

## Compilar

```bash
dotnet build ProgVorazCSharp.slnx
```

## Ejecutar consola

```bash
dotnet run --project ProgVorazCs.Console --configuration Release
```

## Ejecutar GUI (Windows)

```powershell
dotnet run --project ProgVorazCs.Gui --configuration Release
```

## Tags iniciales

Este repositorio incluye tags equivalentes de migracion en C#:

- `v0.1.0-cs`
- `v0.2.0-cs`
- `v0.3.0-cs`
- `v0.3.1-cs`
- `v0.4.0-cs`
