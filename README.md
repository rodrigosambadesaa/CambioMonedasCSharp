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

## CI y Release en GitHub Actions

- `CI CSharp` (`.github/workflows/ci.yml`): build y smoke tests de consola en Linux/macOS/Windows, mas smoke launch de GUI en Windows.
- `Release CSharp` (`.github/workflows/release.yml`): se dispara al hacer push de tags `v*`, publica artefactos `.zip` para:
	- Consola: `win-x64`, `linux-x64`, `osx-x64`
	- GUI: `win-x64`

Para disparar release:

```bash
git tag -a v0.5.0-cs -m "Release v0.5.0-cs"
git push origin main --follow-tags
```

## Tags iniciales

Este repositorio incluye tags equivalentes de migracion en C#:

- `v0.1.0-cs`
- `v0.2.0-cs`
- `v0.3.0-cs`
- `v0.3.1-cs`
- `v0.4.0-cs`
- `v0.5.0-cs-rc1`
- `v0.5.0-cs`

## Servidor HTTP local

Reimplementación administrada de las rutas originales del servidor C:

```bash
dotnet run --project ProgVorazCs.Console -- --server --port 8080
curl http://127.0.0.1:8080/health
curl http://127.0.0.1:8080/export_stock_json
curl -X POST --data-binary @tests/sample_batch.csv http://127.0.0.1:8080/batch
```

Se escucha **únicamente** en `127.0.0.1`, con límite de 1 MiB por petición batch. No hay procesos ni archivos temporales auxiliares. Este cambio no incorpora todavía conversión entre divisas con tasas remotas.


## Conversion de divisas (paridad del original C)

Se ha incorporado un proveedor de tipos de cambio con JSON válido, códigos
ISO de las nueve monedas y aritmética \`BigInteger\` para evitar desbordamientos.
Se utiliza primero la API configurada en la versión C y, ante fallos, el
fichero \`rates_stub.json\` para los pares explícitamente definidos.
La variable \`PROGVORAZ_EXCHANGE_SOURCE=stub\` fuerza la reproducción
determinista sin red:

\`\`\`bash
PROGVORAZ_EXCHANGE_SOURCE=stub dotnet run --project ProgVorazCs.Console -- --convert EUR USD 100
\`\`\`

Una tasa de ejemplo del stub no representa una cotización real ni actual.
Si falta una tasa y falla la API, se retorna error, sin utilizar tasas
inventadas. Se redondea a céntimos con precisión decimal.


## Integridad del inventario (concurrencia)

El módulo \`StockLedger\` reimplementa la escritura de \`stock.txt\` con
fichero de preparación y reemplazo, bloqueo de archivo entre procesos .NET y
validación estricta de los límites de cada moneda. La consola utiliza operaciones
read-modify-write bajo bloqueo para descontar piezas y modificar stock de forma
atómica. Si otra instancia consume las piezas antes, se informa al usuario en
lugar de persistir un stock negativo. Las pruebas de regresión incluyen operaciones
concurrentes, saldo insuficiente y protección entre bloques. Los procesos ajenos
al módulo, como el programa original en C, **no comparten el bloqueo**.


## Pruebas diferenciales exhaustivas

La suite \`ProgVorazCs.Tests\` compara el algoritmo de precisión arbitraria
con un oráculo independiente que enumera combinaciones de monedas.
Hay 480 escenarios deterministas con distintos stocks, cantidades, límites
de piezas y cambio cercano, además de las pruebas de persistencia, HTTP,
concurrencia e integridad previamente existentes. No se usa el propio motor
para calcular los resultados esperados.

### Optimización del cambio cercano

Para importes compatibles con programación dinámica, se utiliza **una sola tabla DP** para buscar la mayor cantidad alcanzable. Se elimina la reconstrucción repetida de la tabla para cada céntimo inferior, evitando el peor caso cuadrático anterior. Las pruebas incluyen un importe elevado sin ninguna moneda alcanzable.
