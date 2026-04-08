# Guia de ejecucion - ProgVoraz C#

## Compilacion

```bash
dotnet build ProgVorazCSharp.slnx
```

## Consola

```bash
dotnet run --project ProgVorazCs.Console
```

Flujo rapido:

1. Elegir modo `a`, `b` o `c`.
2. Escribir moneda (`euro`, `dolar`, `yen`).
3. En `a` y `b`, escribir cantidad en centimos.
4. En `c`, usar `anadir` o `quitar` para administrar stock.

Comandos de navegacion:

- `volver`
- `modo`
- `salir`

## GUI (Windows)

```powershell
dotnet run --project ProgVorazCs.Gui
```

Acciones:

1. Cargar moneda.
2. Administrar stock (modo limitado).
3. Calcular devolucion en panel inferior.
